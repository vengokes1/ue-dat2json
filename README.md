# ue-dat2json

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

A fast, lightweight, and fully standalone CLI tool & C# library to **unpack**, **edit**, and **pack** encrypted Unreal Engine localization string table (`.dat`) files into clean, editable JSON and back.

Zero native DLL dependencies — operates independently with managed Blake3, LZ4, and standard AES cryptography.

---

## Features

- **Bidirectional Conversion**: Seamlessly unpack `.dat` $\to$ `.json` and rebuild `.json` $\to$ game-ready encrypted `.dat`.
- **Complete UE FString Engine**: Implements binary `FString` parsing and serialization (UTF-8 for ASCII, UTF-16 LE for multi-byte Unicode/non-ASCII characters).
- **Full Crypto Stack**:
  - **Blake3**: Fast cryptographic hashing and header XOR mask generation.
  - **AES-256-ECB**: Symmetric block encryption for 16-byte aligned payloads.
  - **LZ4 Compression**: High-speed compression preserving 100% byte fidelity.
- **Dynamic Manifest Support**: Automatically extracts and derives locale-specific AES keys from `key_manifest.dat`.
- **Integrity Verification**: `verify` command to validate headers, encryption, and entry counts without writing to disk.

---

## Technical Specifications

The encrypted `.dat` format consists of:

```
+-------------------------------------------------------------+
|  Offset  | Size (Bytes) | Description                       |
+-------------------------------------------------------------+
|  0x00    | 4            | Format Version (int32 = 2)        |
|  0x04    | 16           | Blake3-Masked Header              |
|          |              |  - [0..3]  Packed Size            |
|          |              |  - [4..7]  Compression Type (= 2) |
|          |              |  - [8..11] Aligned Payload Size   |
|          |              |  - [12..15] Raw Decompressed Size |
|  0x14    | AlignedSize  | AES-256-ECB Encrypted Payload     |
|          |              |  - [0..31] Payload Salt Prefix    |
|          |              |  - [32..]  LZ4 Compressed Body    |
|          |              |  - [Padding to 16-byte boundary]  |
+-------------------------------------------------------------+
```

### Decompressed Table Format
Once decrypted and decompressed, the raw binary table follows:
- **Header**: Version `int32` (`1`) + `FString` (Namespace) + `int32` (Entry count $N$)
- **Entries**: $N \times$ `(FString Key, FString Value)`
- **Trailer**: 4 zero bytes (`0x00000000`)

---

## Installation & Build

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher.

### Build from Source
```bash
git clone https://github.com/vengokes1/ue-dat2json.git
cd ue-dat2json
dotnet build -c Release
```

### Publish Single-File Executable
```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```
The compiled binary `dist/ue-dat2json.exe` can be copied anywhere.

---

## Usage

```text
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
```

---

## Examples

### 1. Unpack `.dat` to JSON
```bash
ue-dat2json unpack L10NString.dat L10NString.json
```
Output JSON format:
```json
{
  "Namespace": "Localization",
  "Entries": {
    "String_str_item_name_1001": "Iron Sword",
    "String_str_quest_title_2004": "The Lost Temple"
  }
}
```

### 2. Pack JSON to encrypted `.dat`
```bash
ue-dat2json pack L10NString.json L10NString.dat
```

### 3. Verify `.dat` file integrity
```bash
ue-dat2json verify L10NString.dat
```

### 4. Unpack using a custom `key_manifest.dat`
```bash
ue-dat2json unpack L10NString.dat output.json --manifest key_manifest.dat --locale en-US
```

---

## JSON Structure

The output JSON file contains:
- `Namespace`: Table namespace identifier.
- `Entries`: Key-value map representing string table identifiers and localized text strings.

UTF-8 and multi-language characters (Turkish, Cyrillic, Chinese, Korean, Japanese, etc.) are fully supported and correctly handled by the FString UTF-16 LE encoder.

---

## License

This project is licensed under the [MIT License](LICENSE) — free for personal, educational, and commercial use.
