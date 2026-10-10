# ue-dat2json

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

[English](#english) | [Türkçe Dökümantasyon](#-türkçe-dökümantasyon)

---

## English

A fast, lightweight, and fully standalone CLI tool & C# library to **unpack**, **edit**, and **pack** encrypted Unreal Engine localization string table (`.dat`) files into clean, editable JSON and back.

Zero native DLL dependencies — operates independently with managed Blake3, LZ4, and standard AES cryptography.

### Features

- **Bidirectional Conversion**: Seamlessly unpack `.dat` $\to$ `.json` and rebuild `.json` $\to$ game-ready encrypted `.dat`.
- **Complete UE FString Engine**: Implements binary `FString` parsing and serialization (UTF-8 for ASCII, UTF-16 LE for multi-byte Unicode/non-ASCII characters).
- **Full Crypto Stack**:
  - **Blake3**: Fast cryptographic hashing and header XOR mask generation.
  - **AES-256-ECB**: Symmetric block encryption for 16-byte aligned payloads.
  - **LZ4 Compression**: High-speed compression preserving 100% byte fidelity.
- **Dynamic Manifest Support**: Automatically extracts and derives locale-specific AES keys from `key_manifest.dat`.
- **Integrity Verification**: `verify` command to validate headers, encryption, and entry counts without writing to disk.

### Technical Specifications

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

#### Decompressed Table Format
Once decrypted and decompressed, the raw binary table follows:
- **Header**: Version `int32` (`1`) + `FString` (Namespace) + `int32` (Entry count $N$)
- **Entries**: $N \times$ `(FString Key, FString Value)`
- **Trailer**: 4 zero bytes (`0x00000000`)

### Installation & Build

#### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher.

#### Build from Source
```bash
git clone https://github.com/vengokes1/ue-dat2json.git
cd ue-dat2json
dotnet build -c Release
```

#### Publish Single-File Executable
```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```
The compiled binary `dist/ue-dat2json.exe` can be copied anywhere.

### Usage

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

### Examples

#### 1. Unpack `.dat` to JSON
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

#### 2. Pack JSON to encrypted `.dat`
```bash
ue-dat2json pack L10NString.json L10NString.dat
```

#### 3. Verify `.dat` file integrity
```bash
ue-dat2json verify L10NString.dat
```

#### 4. Unpack using a custom `key_manifest.dat`
```bash
ue-dat2json unpack L10NString.dat output.json --manifest key_manifest.dat --locale en-US
```

---

## 🇹🇷 Türkçe Dökümantasyon

**ue-dat2json**, Unreal Engine tabanlı şifreli yerelleştirme (L10N) `.dat` metin tablolarını okunabilir ve düzenlenebilir JSON dosyasına açan, ardından çevirisi yapılan JSON'u tekrar oyunun okuyabileceği şifreli `.dat` formatına derleyen **tamamen sıfırdan geliştirilmiş, açık kaynaklı bir CLI aracıdır.**

FModel, CUE4Parse gibi harici araçlara veya üçüncü parti DLL'lere ihtiyaç duymaz. Kendi içinde saf C# ile yazılmış Blake3, LZ4, AES-256-ECB ve Unreal Engine FString ikili motorunu barındırır.

### Öne Çıkan Özellikler

- **Çift Yönlü Tam Dönüşüm**: `.dat` $\to$ `.json` açma ve `.json` $\to$ `.dat` şifreli paketleme.
- **Gelişmiş FString Desteği**: Standart İngilizce metinleri UTF-8, Türkçe karakterler (`ç, ğ, ı, ö, ş, ü, İ`) ve diğer dilleri UTF-16 LE olarak hatasız işleyen ikili motor.
- **Bütünleşik Kripto Mimarisi**:
  - **Blake3**: Başlık maskeleme (XOR mask) ve hash hesaplama.
  - **AES-256-ECB**: 16 bayt hizalı güvenli blok şifreleme/çözme.
  - **LZ4**: Yüksek hızlı, kayıpsız veri sıkıştırma ve açma.
- **Dinamik Anahtar Çıkarma**: `key_manifest.dat` dosyası üzerinden istenen dile özel AES anahtarını otomatik türetme desteği.
- **Bütünlük Doğrulama**: `verify` komutuyla diske dosya yazmadan tablonun şifre ve veri geçerliliğini test etme.

### Teknik Dosya Mimarisi

Şifreli `.dat` dosya yapısı aşağıdaki gibidir:

```
+-------------------------------------------------------------+
| Ofset    | Boyut (Bayt) | Açıklama                          |
+-------------------------------------------------------------+
| 0x00     | 4            | Format Versiyonu (int32 = 2)      |
| 0x04     | 16           | Blake3 ile Maskelenmiş Başlık     |
|          |              |  - [0..3]  Sıkıştırılmış Boyut   |
|          |              |  - [4..7]  Sıkıştırma Türü (= 2)  |
|          |              |  - [8..11] Hizalanmış Boyut       |
|          |              |  - [12..15] Orijinal Ham Boyut    |
| 0x14     | Hizalanmış   | AES-256-ECB Şifreli Veri Bloğu    |
|          |              |  - [0..31] Salt / Ön Ek (Prefix)  |
|          |              |  - [32..]  LZ4 Sıkıştırılmış Metin|
|          |              |  - [16 Bayta Tamamlama Padding]   |
+-------------------------------------------------------------+
```

### Kurulum ve Derleme

#### Gereksinimler
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) veya üzeri.

#### Kaynak Koddan Derleme
```bash
git clone https://github.com/vengokes1/ue-dat2json.git
cd ue-dat2json
dotnet build -c Release
```

#### Tek Dosya (Portable Exe) Üretme
```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```
Oluşan `dist/ue-dat2json.exe` dosyasını istediğiniz klasöre kopyalayıp doğrudan kullanabilirsiniz.

### Kullanım ve Komutlar

```text
KULLANIM:
  ue-dat2json unpack <girdi.dat> <cikti.json> [seçenekler]
  ue-dat2json pack   <girdi.json> <cikti.dat> [seçenekler]
  ue-dat2json verify <girdi.dat> [seçenekler]

KOMUTLAR:
  unpack    Şifreli .dat dosyasını çözer, LZ4'ten çıkartır ve JSON yapar
  pack      JSON dosyasını FString formatına derler, LZ4 sıkıştırıp AES ile şifreler
  verify    .dat dosyasının başlık ve kriptografik doğruluğunu test eder

SEÇENEKLER:
  --manifest <dosya>  Dile özel anahtarları türetmek için key_manifest.dat yolu
  --locale <dil>      Manifest kullanırken hedef dil (Varsayılan: en-US)
  --key <hex>         Özel 32 bayt hex formatında AES-256 anahtarı
  --seed <hex>        Özel 8 bayt hex formatında başlık tohumu (uint64)
  --prefix <hex>      Özel 32 bayt hex formatında şifresiz veri ön eki
```

### Türkçe Kullanım Örnekleri

#### 1. `.dat` dosyasını JSON'a açma (Unpack)
```bash
ue-dat2json unpack L10NString.dat L10NString.json
```
Oluşan JSON çıktısı:
```json
{
  "Namespace": "Localization",
  "Entries": {
    "String_str_item_name_1001": "Demir Kılıç",
    "String_str_quest_title_2004": "Kayıp Tapınak"
  }
}
```

#### 2. Düzenlenen JSON'u tekrar şifreli `.dat` yapma (Pack)
```bash
ue-dat2json pack L10NString.json L10NString.dat
```

#### 3. Dosya bütünlüğünü test etme (Verify)
```bash
ue-dat2json verify L10NString.dat
```

#### 4. Özel `key_manifest.dat` dosyası ile açma
```bash
ue-dat2json unpack L10NString.dat cikti.json --manifest key_manifest.dat --locale en-US
```

---

## Lisans / License

Bu proje [MIT Lisansı](LICENSE) ile korunmaktadır. Kişisel, eğitim ve ticari projelerde özgürce kullanılabilir ve değiştirilebilir.
