using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blake3;
using K4os.Compression.LZ4;

namespace UeDat2Json;

public class LocalizationTable
{
    [JsonPropertyName("Namespace")]
    public string Namespace { get; set; } = "Localization";

    [JsonPropertyName("Entries")]
    public Dictionary<string, string> Entries { get; set; } = new();
}

public static class Program
{
    // Default Cryptographic Parameters (en-US / standard Steam build)
    private const string DefaultKeyHex = "0CFDAB7BCB727E404A20577C83BE3399207F02AB68C18F300D36FD3C66CB218E";
    private const ulong DefaultSeed = 0xA875363B0593FEA3UL;
    private const string DefaultPrefixHex = "FA4B2894F6C99ABF3412F741232847D2292D3B339481BD6FD32BCC61E67A8776";
    private const string ManifestKeyHex = "EB0BC09728475AB5196AF8CE785D8A79DF18D2148F51CBEF8B39E43B2A1D4056";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "unpack" or "decode" => RunUnpack(args),
                "pack" or "encode" => RunPack(args),
                "verify" => RunVerify(args),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
========================================================================
  ue-dat2json - Unreal Engine Encrypted L10N String Table Converter
  Version 1.0.0 | Open-Source Tool | MIT License
========================================================================");
        Console.ResetColor();
        Console.WriteLine(@"
USAGE:
  ue-dat2json unpack <input.dat> <output.json> [options]
  ue-dat2json pack   <input.json> <output.dat> [options]
  ue-dat2json verify <input.dat> [options]

COMMANDS:
  unpack    Decrypt, decompress, and convert .dat to editable JSON
  pack      Serialize, compress with LZ4, and encrypt JSON to .dat
  verify    Test cryptographic integrity and Blake3 hashes of a .dat file

OPTIONS:
  --manifest <path>  Path to key_manifest.dat to derive locale keys dynamically
  --locale <locale>  Target locale when using manifest (default: en-US)
  --key <hex>        Custom AES-256 key in hexadecimal (32 bytes)
  --seed <hex>       Custom header seed in hexadecimal (8 bytes uint64)
  --prefix <hex>     Custom unencrypted payload prefix in hexadecimal (32 bytes)

EXAMPLES:
  # Unpack .dat to .json using built-in defaults:
  ue-dat2json unpack L10NString.dat L10NString.json

  # Pack modified .json back to encrypted .dat:
  ue-dat2json pack L10NString.json L10NString.dat

  # Unpack using a custom manifest:
  ue-dat2json unpack L10NString.dat output.json --manifest key_manifest.dat --locale en-US
");
    }

    private static int HandleUnknownCommand(string cmd)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] Unknown command: '{cmd}'");
        Console.ResetColor();
        PrintUsage();
        return 1;
    }

    private static int RunUnpack(string[] args)
    {
        if (args.Length < 3)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Usage: ue-dat2json unpack <input.dat> <output.json> [options]");
            Console.ResetColor();
            return 1;
        }

        string inputPath = args[1];
        string outputPath = args[2];
        var opts = ParseOptions(args, 3);

        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input file not found: {inputPath}");

        Console.WriteLine($"[1/3] Reading encrypted DAT file: {inputPath}");
        byte[] dat = File.ReadAllBytes(inputPath);

        byte[] key;
        ulong seed;

        if (opts.TryGetValue("manifest", out string? manifestPath))
        {
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException($"Manifest file not found: {manifestPath}");

            string locale = opts.GetValueOrDefault("locale", "en-US");
            Console.WriteLine($"[2/3] Deriving key from manifest for locale: {locale}");
            byte[] manifest = File.ReadAllBytes(manifestPath);
            (key, seed) = DeriveKeyFromManifest(manifest, locale);
        }
        else
        {
            key = opts.TryGetValue("key", out string? kHex) ? Convert.FromHexString(kHex) : Convert.FromHexString(DefaultKeyHex);
            seed = opts.TryGetValue("seed", out string? sHex) ? Convert.ToUInt64(sHex, 16) : DefaultSeed;
            Console.WriteLine($"[2/3] Using AES key: {Convert.ToHexString(key)[..8]}... (seed: {seed:X16})");
        }

        byte[] clear = DecodeDat(dat, key, seed);
        Console.WriteLine($"[3/3] Deserializing binary table ({clear.Length:N0} bytes) to JSON...");
        var table = DeserializeClear(clear);

        string? outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(table, jsonOpts));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] Exported {table.Entries.Count:N0} entries to {outputPath} ({new FileInfo(outputPath).Length:N0} bytes)");
        Console.ResetColor();
        return 0;
    }

    private static int RunPack(string[] args)
    {
        if (args.Length < 3)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Usage: ue-dat2json pack <input.json> <output.dat> [options]");
            Console.ResetColor();
            return 1;
        }

        string inputPath = args[1];
        string outputPath = args[2];
        var opts = ParseOptions(args, 3);

        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input JSON file not found: {inputPath}");

        Console.WriteLine($"[1/3] Reading JSON file: {inputPath}");
        string jsonContent = File.ReadAllText(inputPath);
        var table = JsonSerializer.Deserialize<LocalizationTable>(jsonContent)
            ?? throw new InvalidDataException("Invalid or empty JSON structure.");

        Console.WriteLine($"[2/3] Serializing {table.Entries.Count:N0} entries to binary FString format...");
        byte[] clear = SerializeClear(table);

        byte[] key = opts.TryGetValue("key", out string? kHex) ? Convert.FromHexString(kHex) : Convert.FromHexString(DefaultKeyHex);
        ulong seed = opts.TryGetValue("seed", out string? sHex) ? Convert.ToUInt64(sHex, 16) : DefaultSeed;
        byte[] prefix = opts.TryGetValue("prefix", out string? pHex) ? Convert.FromHexString(pHex) : Convert.FromHexString(DefaultPrefixHex);

        Console.WriteLine($"[3/3] Compressing with LZ4 and encrypting with AES-256-ECB...");
        byte[] dat = EncodeDat(clear, key, seed, prefix);

        string? outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        File.WriteAllBytes(outputPath, dat);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] Built encrypted DAT file: {outputPath} ({dat.Length:N0} bytes)");
        Console.ResetColor();
        return 0;
    }

    private static int RunVerify(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Usage: ue-dat2json verify <input.dat> [options]");
            Console.ResetColor();
            return 1;
        }

        string inputPath = args[1];
        var opts = ParseOptions(args, 2);

        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input DAT file not found: {inputPath}");

        byte[] dat = File.ReadAllBytes(inputPath);
        byte[] key;
        ulong seed;

        if (opts.TryGetValue("manifest", out string? manifestPath))
        {
            string locale = opts.GetValueOrDefault("locale", "en-US");
            byte[] manifest = File.ReadAllBytes(manifestPath);
            (key, seed) = DeriveKeyFromManifest(manifest, locale);
        }
        else
        {
            key = opts.TryGetValue("key", out string? kHex) ? Convert.FromHexString(kHex) : Convert.FromHexString(DefaultKeyHex);
            seed = opts.TryGetValue("seed", out string? sHex) ? Convert.ToUInt64(sHex, 16) : DefaultSeed;
        }

        byte[] clear = DecodeDat(dat, key, seed);
        var table = DeserializeClear(clear);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[VERIFY OK] Valid format! Version: 1, Namespace: '{table.Namespace}', Entries: {table.Entries.Count:N0}");
        Console.ResetColor();
        return 0;
    }

    // =========================================================================
    // Cryptography & LZ4 Compression
    // =========================================================================

    private static byte[] Blake3Hash(byte[] data)
    {
        using var hasher = Hasher.New();
        hasher.Update(data);
        return hasher.Finalize().AsSpan().ToArray();
    }

    private static byte[] AesEcbEncrypt(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptEcb(data, PaddingMode.None);
    }

    private static byte[] AesEcbDecrypt(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptEcb(data, PaddingMode.None);
    }

    public static byte[] EncodeDat(byte[] clear, byte[] key, ulong seed, byte[] prefix32)
    {
        int raw = clear.Length;

        // 1. LZ4 Compress
        byte[] maxCompressed = new byte[LZ4Codec.MaximumOutputSize(raw)];
        int packed = LZ4Codec.Encode(clear, maxCompressed, LZ4Level.L00_FAST);

        // 2. Build 16-byte aligned payload (32 bytes prefix + compressed bytes + padding)
        int payloadLen = 32 + packed;
        int aligned = (payloadLen + 15) & ~15;
        byte[] unencryptedPayload = new byte[aligned];
        prefix32.CopyTo(unencryptedPayload.AsSpan(0, 32));
        maxCompressed.AsSpan(0, packed).CopyTo(unencryptedPayload.AsSpan(32));

        // 3. Encrypt payload
        byte[] encryptedPayload = AesEcbEncrypt(unencryptedPayload, key);

        // 4. Header: packed, type=2, aligned, raw
        var header = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0), packed);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), aligned);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), raw);

        // 5. Mask header using Blake3 hash
        var headerInput = new byte[24];
        Blake3Hash(BitConverter.GetBytes(0xCD02190910CE83F6UL)).AsSpan(0, 8).CopyTo(headerInput);
        BinaryPrimitives.WriteUInt64LittleEndian(headerInput.AsSpan(8), seed);
        BinaryPrimitives.WriteInt32LittleEndian(headerInput.AsSpan(16), 3);
        var mask = Blake3Hash(headerInput);
        for (int i = 0; i < 16; i++) header[i] ^= mask[i];

        // 6. Final DAT: 4 bytes version (2) + 16 bytes masked header + encrypted payload
        byte[] dat = new byte[4 + 16 + aligned];
        BinaryPrimitives.WriteInt32LittleEndian(dat.AsSpan(0), 2);
        header.CopyTo(dat.AsSpan(4));
        encryptedPayload.CopyTo(dat.AsSpan(20));

        return dat;
    }

    public static byte[] DecodeDat(byte[] dat, byte[] key, ulong seed)
    {
        if (dat.Length < 20 || BinaryPrimitives.ReadInt32LittleEndian(dat.AsSpan(0, 4)) != 2)
            throw new InvalidDataException("Invalid DAT file version (expected version 2).");

        // 1. Unmask header
        var headerInput = new byte[24];
        Blake3Hash(BitConverter.GetBytes(0xCD02190910CE83F6UL)).AsSpan(0, 8).CopyTo(headerInput);
        BinaryPrimitives.WriteUInt64LittleEndian(headerInput.AsSpan(8), seed);
        BinaryPrimitives.WriteInt32LittleEndian(headerInput.AsSpan(16), 3);
        var mask = Blake3Hash(headerInput);

        var header = dat[4..20].ToArray();
        for (int i = 0; i < 16; i++) header[i] ^= mask[i];

        int packed = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        int type = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        int aligned = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4));
        int raw = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12, 4));

        if (type != 2 || aligned <= 32 || aligned % 16 != 0 || (long)aligned + 20 != dat.Length || packed <= 0 || raw <= 0)
            throw new InvalidDataException("Corrupted or invalid decrypted header.");

        // 2. Decrypt payload
        byte[] payload = AesEcbDecrypt(dat[20..], key);

        // 3. Decompress LZ4 payload
        byte[] clear = new byte[raw];
        int decompressed = LZ4Codec.Decode(payload.AsSpan(32, packed), clear);
        if (decompressed != raw)
            throw new InvalidDataException($"Decompression error: expected {raw} bytes, got {decompressed}.");

        return clear;
    }

    public static (byte[] Key, ulong Seed) DeriveKeyFromManifest(byte[] manifest, string locale)
    {
        if (manifest.Length < 8 || (manifest.Length - 8) % 48 != 0)
            throw new InvalidDataException("Invalid key_manifest.dat format.");

        byte[] manifestKey = Blake3Hash(Convert.FromHexString(ManifestKeyHex));
        byte[] decryptedKeys = AesEcbDecrypt(manifest[8..], manifestKey);

        ulong seed = BinaryPrimitives.ReadUInt64LittleEndian(Blake3Hash(Encoding.UTF8.GetBytes("L10NString_" + locale)));
        byte[]? foundKey = null;

        for (int p = 0; p < decryptedKeys.Length; p += 48)
        {
            if (BinaryPrimitives.ReadUInt64LittleEndian(decryptedKeys.AsSpan(p, 8)) == seed)
            {
                foundKey = decryptedKeys[(p + 8)..(p + 40)];
                break;
            }
        }

        if (foundKey is null)
            throw new KeyNotFoundException($"Key for locale '{locale}' not found in manifest.");

        return (foundKey, seed);
    }

    // =========================================================================
    // Unreal Engine 5 FString Binary Serialization
    // =========================================================================

    public static void WriteFString(BinaryWriter writer, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.Write(0);
            return;
        }

        bool isAscii = true;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] > 127) { isAscii = false; break; }
        }

        if (isAscii)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length + 1);
            writer.Write(bytes);
            writer.Write((byte)0);
        }
        else
        {
            byte[] bytes = Encoding.Unicode.GetBytes(value);
            writer.Write(-(value.Length + 1));
            writer.Write(bytes);
            writer.Write((byte)0);
            writer.Write((byte)0);
        }
    }

    public static string ReadFString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length == 0) return string.Empty;

        if (length > 0)
        {
            byte[] bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes, 0, length - 1);
        }
        else
        {
            int charCount = -length;
            byte[] bytes = reader.ReadBytes(charCount * 2);
            return Encoding.Unicode.GetString(bytes, 0, (charCount - 1) * 2);
        }
    }

    public static byte[] SerializeClear(LocalizationTable table)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Version = 1
        bw.Write(1);
        WriteFString(bw, table.Namespace);
        bw.Write(table.Entries.Count);

        foreach (var kv in table.Entries)
        {
            WriteFString(bw, kv.Key);
            WriteFString(bw, kv.Value);
        }

        // 4-byte zero trailer
        bw.Write(0);
        return ms.ToArray();
    }

    public static LocalizationTable DeserializeClear(byte[] clear)
    {
        using var ms = new MemoryStream(clear);
        using var br = new BinaryReader(ms);

        int version = br.ReadInt32();
        if (version != 1)
            throw new InvalidDataException($"Unexpected binary table version: {version}");

        string ns = ReadFString(br);
        int count = br.ReadInt32();

        if (count < 0 || count > 2000000)
            throw new InvalidDataException($"Invalid entry count: {count}");

        var entries = new Dictionary<string, string>(count);
        for (int i = 0; i < count; i++)
        {
            string key = ReadFString(br);
            string val = ReadFString(br);
            entries[key] = val;
        }

        return new LocalizationTable
        {
            Namespace = ns,
            Entries = entries
        };
    }

    private static Dictionary<string, string> ParseOptions(string[] args, int startIndex)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = startIndex; i < args.Length; i++)
        {
            if (args[i].StartsWith("--") && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                string key = args[i][2..];
                options[key] = args[i + 1];
                i++;
            }
        }
        return options;
    }
}
