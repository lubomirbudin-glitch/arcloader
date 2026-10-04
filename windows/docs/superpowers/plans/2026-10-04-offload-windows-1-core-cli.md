# Offload pro Windows — plán 1: jádro + CLI

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Knihovna `Offload.Core` a CLI `offload.exe` (copy / verify / bench), které na Windows zkopírují kartu na více disků, ověří ji zpětným čtením (xxHash64) a zapíšou report (JSON, TXT, HTML, `.xxh64`, ASC MHL) ve stejném formátu jako Mac verze.

**Architecture:** Port `mac/Sources/OffloadKit` do C# soubor po souboru. Karta se čte jednou do zarovnaných bufferů; každý blok jde do vlákna hasheru a do vlákna každého cíle (sériové fronty). Zápis i čtení obcházejí cache Windows (`FILE_FLAG_NO_BUFFERING`), flush přes `FlushFileBuffers`. Analýza klipů, proxy, PDF (plán 2), braw-decode (plán 3) a GUI (plán 4) tu nejsou.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), `System.IO.Hashing` 10.0.12, `System.CommandLine` 2.0.12, xUnit, P/Invoke (`LibraryImport`) do kernel32.

**Spec:** `windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md` (oddíly 2–5, 9, 10). Referenční chování: Mac zdroj v `mac/Sources/OffloadKit/*.swift` a `mac/Sources/offload/main.swift`.

## Rozdělení na plány

| Plán | Obsah | Stav |
|---|---|---|
| **1 (tento)** | jádro kopírování, ověření, reporty JSON/TXT/HTML/xxh64/MHL, CLI | |
| 2 | analýza klipů, 3 snímky na klip (první/prostřední/poslední), proxy (ffmpeg, HW enkodéry), REDline, art-cmd, PDF, CLI volby proxy; Mac změna snímků | po plánu 1 |
| 3 | `braw-decode.exe` (C++, Blackmagic RAW SDK pro Windows) | po plánu 2 |
| HW | HW diagnostika: USB topologie, test portů, doporučení, propustnost článků při kopii, `offload ports` | po plánu 1 |
| 4 | WPF GUI (vč. dotazu na test USB při startu a řetězu HW na kartě úlohy), detekce a vysunutí karet, balíček | po plánech 2 a HW |

## Global Constraints

- Platforma: `net10.0-windows`, Windows 10/11 x64. Všechny projekty dědí `windows/Directory.Build.props`.
- Závislosti jen: `System.IO.Hashing` 10.0.12, `System.CommandLine` 2.0.12, šablona `xunit` pro testy. Nic dalšího.
- Mac kód (celá složka `mac/`) se nemění. Vše nové je ve `windows/`; společná dokumentace v `docs/`.
- Na zdroj (kartu) se nikdy nezapisuje: otevírá se jen `FileAccess.Read`.
- Formáty na disku shodné s Mac verzí: `<root>/<YYYY-MM-DD>/<karta>/`, `_REPORT/`, `_PROXY/`, `ascmhl/`, rozpracované soubory `.<jméno>.offload-part`, relativní cesty v reportech vždy s `/`, JSON klíče jako Swift `Codable` (camelCase), data ISO 8601 UTC na sekundy (`2026-10-04T08:00:00Z`), `null` vlastnosti vynechané.
- `tool` = `offload`, `version` = `0.1.0-win`.
- Chybové hlášky preflightu a CLI anglicky (jako Mac CLI); popisky fází úlohy a HTML report česky (jako Mac).
- Textové soubory reportu: UTF-8 bez BOM, konce řádků `\n`.
- Práce na větvi `windows`. Každý commit končí řádkem `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Příkazy `dotnet` se spouštějí ve složce `D:\My\Projekt\offload\windows` (PowerShell).

## Review Focus

1. **Soubor na kartě se během kopie změní nebo přestane být čitelný** (kamera ještě zapisuje, karta vytažena) → ten soubor `failed`, ostatní se dokopírují, nikdy „kartu lze vysunout“. Test: Task 12, `SourceShrinksAfterPrepare_FileFailsOthersVerified`.
2. **Jeden cíl selže u jednoho souboru, druhý je v pořádku** → stav po cílech, report ukáže který cíl, „nelze vysunout“. Test: Task 12, `OneDestinationBlocked_OtherDestinationComplete`.
3. **Stejná karta zkopírovaná podruhé ve stejný den** → nová složka `NAME_2`, předchozí offload nedotčen. Test: Task 12, `SameCardTwice_SecondGetsSuffixFirstUntouched`.
4. **Čeština, mezery a `&` v názvech souborů i karty** → zkopírováno, hash list obsahuje přesná jména, MHL je escapuje. Test: Task 12, `CzechNamesAndAmpersand_RoundTrip` (+ Task 8 escapování).
5. **Cíl naformátovaný jako FAT32 a klip ≥ 4 GiB** → chyba preflightu se jménem souboru, ne pád v půlce kopie. Test: Task 11, `Fat32Problem_*`.

---

## Struktura souborů

```
windows/
  .gitignore
  Directory.Build.props
  Offload.sln
  scripts/build.ps1
  src/Offload.Core/
    Offload.Core.csproj
    Format.cs            OffloadVersion, Format.Hex64/Bytes/Rate
    Win32.cs             P/Invoke deklarace kernel32
    IOError.cs           výjimka s operací, cestou a Win32 kódem
    AlignedBuffer.cs     nativní paměť zarovnaná na 4096 B
    NativeIO.cs          otevření/čtení/zápis/flush/rename/svazky/keep-awake
    FileReader.cs        čtení bez cache s fallbackem na buffered
    Layout.cs            struktura složek, sanitize, platné názvy
    Scanner.cs           SourceFile, SourceScan, Scanner (+ Windows kontroly)
    Hashing.cs           HashFile
    Models.cs            OffloadOptions, FileState, DestFileResult, OffloadPlan, ProgressSnapshot, OffloadError
    Media.cs             ClipFormat, Clip, ClipInfo, ClipEntry, ProxyStatus, ProxyCodec, ProxySettings (jen data)
    OffloadReport.cs     ReportDestination, ReportFile, OffloadReport (+ JSON, souhrn, hash list, Write)
    Mhl.cs               ASC MHL v2 + C4
    HtmlReport.cs        HTML report
    SerialWorker.cs      vlákno + FIFO fronta
    BufferPool.cs        BufferPool, Chunk
    SourceHasher.cs      xxh64 zdrojového proudu
    DestWriter.cs        zápis na jeden cíl + ověření
    OffloadEngine.cs     preflight + kopie + flush + ověření + report
    Verifier.cs          opakované ověření hotové kopie
    OffloadJob.cs        JobConfig, JobPhase, JobStatus, OffloadJob
    Bench.cs             měření rychlosti
  src/Offload.Cli/
    Offload.Cli.csproj   AssemblyName offload
    Program.cs
    ConsoleLine.cs
    ProgressDisplay.cs
    CopyCommand.cs
    VerifyCommand.cs
    BenchCommand.cs
  tests/Offload.Core.Tests/
    Offload.Core.Tests.csproj
    TestFs.cs            TempDir, TestFs, Reports (pomocníci)
    FormatTests.cs, NativeIOTests.cs, LayoutTests.cs, ScannerTests.cs, HashingTests.cs, MediaTests.cs,
    ReportTests.cs, MhlTests.cs, HtmlReportTests.cs, DestWriterTests.cs, PreflightTests.cs,
    EngineTests.cs, VerifierTests.cs, JobTests.cs, BenchTests.cs
```

---

### Task 1: Kostra řešení + Format

**Files:**
- Create: `windows/.gitignore`, `windows/Directory.Build.props`, `windows/Offload.sln`
- Create: `windows/src/Offload.Core/Offload.Core.csproj`, `windows/src/Offload.Core/Format.cs`
- Create: `windows/src/Offload.Cli/Offload.Cli.csproj`, `windows/src/Offload.Cli/Program.cs` (dočasný)
- Create: `windows/tests/Offload.Core.Tests/Offload.Core.Tests.csproj`, `windows/tests/Offload.Core.Tests/TestFs.cs`, `windows/tests/Offload.Core.Tests/FormatTests.cs`

**Interfaces:**
- Produces: `OffloadVersion.String` (`"0.1.0-win"`); `Format.Hex64(ulong) -> string`, `Format.Bytes(long) -> string`, `Format.Rate(long bytes, double seconds) -> string`; testovací `TempDir` (`Path`, `Sub(params string[])`), `TestFs.WriteRandom(string path, int size, int seed) -> byte[]`.

- [ ] **Step 1: Vytvořit projekty**

```powershell
cd D:\My\Projekt\offload\windows
dotnet new sln -n Offload --format sln
dotnet new classlib -n Offload.Core -o src/Offload.Core
dotnet new console -n Offload.Cli -o src/Offload.Cli
dotnet new xunit -n Offload.Core.Tests -o tests/Offload.Core.Tests
dotnet sln Offload.sln add src/Offload.Core src/Offload.Cli tests/Offload.Core.Tests
dotnet add tests/Offload.Core.Tests reference src/Offload.Core
Remove-Item src/Offload.Core/Class1.cs, tests/Offload.Core.Tests/UnitTest1.cs
```

- [ ] **Step 2: Společná nastavení a `.gitignore`**

`windows/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Version>0.1.0</Version>
    <InformationalVersion>0.1.0-win</InformationalVersion>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
  </PropertyGroup>
</Project>
```

`windows/.gitignore`:

```
bin/
obj/
build/
tools/bin/
TestResults/
.vs/
*.user
```

`windows/src/Offload.Core/Offload.Core.csproj` (celý obsah nahradit):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="System.IO.Hashing" Version="10.0.12" />
    <InternalsVisibleTo Include="Offload.Core.Tests" />
  </ItemGroup>
</Project>
```

`windows/src/Offload.Cli/Offload.Cli.csproj` (celý obsah nahradit):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>offload</AssemblyName>
    <RootNamespace>Offload.Cli</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.12" />
    <ProjectReference Include="..\Offload.Core\Offload.Core.csproj" />
  </ItemGroup>
</Project>
```

`windows/src/Offload.Cli/Program.cs` (dočasný, nahradí ho Task 16):

```csharp
Console.WriteLine(Offload.Core.OffloadVersion.String);
```

V `windows/tests/Offload.Core.Tests/Offload.Core.Tests.csproj` smazat řádky `<TargetFramework>…</TargetFramework>`, `<ImplicitUsings>…</ImplicitUsings>` a `<Nullable>…</Nullable>` (přicházejí z `Directory.Build.props`). Ostatní obsah šablony (balíčky, `<Using Include="Xunit" />`, `IsPackable`) ponechat.

- [ ] **Step 3: Testovací pomocníci**

`windows/tests/Offload.Core.Tests/TestFs.cs`:

```csharp
namespace Offload.Core.Tests;

/// Unique folder under %TEMP%\offload-tests, deleted on Dispose.
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "offload-tests", Guid.NewGuid().ToString("N")[..8]);

    public TempDir() => Directory.CreateDirectory(Path);

    public string Sub(params string[] parts)
    {
        var p = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public static class TestFs
{
    public static byte[] WriteRandom(string path, int size, int seed)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        File.WriteAllBytes(path, data);
        return data;
    }
}
```

- [ ] **Step 4: Napsat padající test**

`windows/tests/Offload.Core.Tests/FormatTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class FormatTests
{
    [Fact]
    public void Hex64_IsSixteenLowercaseDigits() => Assert.Equal("00000000000000ff", Format.Hex64(255));

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(999L, "999 B")]
    [InlineData(1000L, "1.00 KB")]
    [InlineData(1_500_000L, "1.50 MB")]
    [InlineData(2_000_000_000_000L, "2.00 TB")]
    public void Bytes_UsesDecimalUnits(long bytes, string expected) => Assert.Equal(expected, Format.Bytes(bytes));

    [Fact]
    public void Rate_IsMegabytesPerSecond()
    {
        Assert.Equal("250 MB/s", Format.Rate(500_000_000, 2));
        Assert.Equal("-", Format.Rate(1, 0));
    }

    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.1.0-win", OffloadVersion.String);
}
```

- [ ] **Step 5: Spustit test, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~FormatTests"`
Expected: build FAIL — `The name 'Format' does not exist in the current context`.

- [ ] **Step 6: Implementace**

`windows/src/Offload.Core/Format.cs`:

```csharp
using System.Globalization;

namespace Offload.Core;

public static class OffloadVersion
{
    public const string String = "0.1.0-win";
}

public static class Format
{
    static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Hex64(ulong v) => v.ToString("x16", CultureInfo.InvariantCulture);

    /// Decimal units (1 KB = 1000 B), like the Mac version, cameras and disk vendors.
    public static string Bytes(long b)
    {
        double v = b;
        int i = 0;
        while (v >= 1000 && i < Units.Length - 1) { v /= 1000; i++; }
        return i == 0 ? $"{b} B" : v.ToString("0.00", CultureInfo.InvariantCulture) + " " + Units[i];
    }

    public static string Rate(long bytes, double seconds) =>
        seconds > 0 ? (bytes / seconds / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "-";
}
```

- [ ] **Step 7: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~FormatTests"`
Expected: PASS (8 testů). Navíc `dotnet build` bez varování.

- [ ] **Step 8: Commit**

```powershell
git add windows
git commit -m "Windows: solution skeleton and formatting helpers" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Nativní I/O (Win32)

**Files:**
- Create: `windows/src/Offload.Core/Win32.cs`, `IOError.cs`, `AlignedBuffer.cs`, `NativeIO.cs`, `FileReader.cs`
- Test: `windows/tests/Offload.Core.Tests/NativeIOTests.cs`

**Interfaces:**
- Produces:
  - `IOError : Exception` — `Op`, `FilePath`, `Code` (Win32 kód); ctor `(string op, string path, int code)` a `(string op, string path, Exception inner)`.
  - `AlignedBuffer : IDisposable` — `const int Alignment = 4096`, `int Size`, `Span<byte> Span`; ctor `(int size)` vyžaduje kladný násobek 4096.
  - `NativeIO` (static): `OpenRead(string path, bool unbuffered) -> SafeFileHandle`, `OpenWrite(string path, bool unbuffered, long preallocate = 0) -> SafeFileHandle`, `OpenExisting(string path) -> SafeFileHandle`, `Read(SafeFileHandle, Span<byte>, long offset, string path) -> int`, `Write(SafeFileHandle, ReadOnlySpan<byte>, long offset, string path)`, `SetLength(SafeFileHandle, long, string path)`, `Flush(SafeFileHandle, string path)`, `SetModified(SafeFileHandle, DateTime utc, string path)`, `Move(string from, string to, bool replace)`, `Delete(string path)`, `WriteFileDurably(string path, ReadOnlySpan<byte> data)`, `Mkdirs(string path)`, `VolumeRoot(string) -> string?`, `VolumeId(string) -> string?`, `FilesystemType(string) -> string`, `VolumeLabel(string) -> string?`, `AvailableBytes(string) -> long?`, `SectorSize(SafeFileHandle) -> int`, `KeepAwake() -> IDisposable`, `const int ErrorInvalidParameter = 87`.
  - `FileReader : IDisposable` — ctor `(string path)`, `Read(Span<byte> buffer, long offset) -> int`, `bool Unbuffered`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/NativeIOTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class NativeIOTests
{
    [Fact]
    public void UnbufferedWrite_PaddedThenTruncated_HasExactContent()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "f.bin");
        using var buf = new AlignedBuffer(8192);
        var data = new byte[5000];
        new Random(1).NextBytes(data);
        data.CopyTo(buf.Span);
        using (var h = NativeIO.OpenWrite(path, unbuffered: true, preallocate: 5000))
        {
            NativeIO.Write(h, buf.Span[..8192], 0, path);
            NativeIO.SetLength(h, 5000, path);
            NativeIO.Flush(h, path);
        }
        Assert.Equal(data, File.ReadAllBytes(path));
    }

    [Fact]
    public void FileReader_ReturnsShortReadAtEndOfFile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "f.bin");
        var data = TestFs.WriteRandom(path, 5000, 2);
        using var buf = new AlignedBuffer(8192);
        using var reader = new FileReader(path);
        Assert.Equal(5000, reader.Read(buf.Span, 0));
        Assert.Equal(data, buf.Span[..5000].ToArray());
        Assert.True(reader.Unbuffered);
    }

    [Fact]
    public void WriteFileDurably_CreatesAndReplaces_NoTempLeft()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "report.txt");
        NativeIO.WriteFileDurably(path, "one"u8);
        NativeIO.WriteFileDurably(path, "two"u8);
        Assert.Equal("two", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Move_WithoutReplace_FailsWhenTargetExists()
    {
        using var dir = new TempDir();
        var a = Path.Combine(dir.Path, "a");
        var b = Path.Combine(dir.Path, "b");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        var e = Assert.Throws<IOError>(() => NativeIO.Move(a, b, replace: false));
        Assert.Equal("rename", e.Op);
        Assert.Equal("b", File.ReadAllText(b));
    }

    [Fact]
    public void SetModified_SetsLastWriteTime()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "f");
        var when = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        using (var h = NativeIO.OpenWrite(path, unbuffered: false)) NativeIO.SetModified(h, when, path);
        Assert.Equal(when, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void VolumeQueries_DescribeTheTempVolume()
    {
        using var dir = new TempDir();
        var a = dir.Sub("a");
        var b = dir.Sub("b");
        Assert.NotNull(NativeIO.VolumeId(a));
        Assert.Equal(NativeIO.VolumeId(a), NativeIO.VolumeId(b));
        Assert.Contains(NativeIO.FilesystemType(a), new[] { "NTFS", "ReFS" });
        Assert.True(NativeIO.AvailableBytes(a) > 0);
        using var h = NativeIO.OpenWrite(Path.Combine(a, "x"), unbuffered: true);
        Assert.True(NativeIO.SectorSize(h) >= 4096);
    }

    [Fact]
    public void AlignedBuffer_RejectsUnalignedSize() => Assert.Throws<ArgumentException>(() => new AlignedBuffer(1000));

    [Fact]
    public void OpenRead_MissingFile_ThrowsIOErrorWithPath()
    {
        var e = Assert.Throws<IOError>(() => NativeIO.OpenRead(@"C:\nope\missing.bin", unbuffered: true));
        Assert.Equal("open", e.Op);
        Assert.Contains("missing.bin", e.Message);
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~NativeIOTests"`
Expected: build FAIL — `AlignedBuffer`, `NativeIO`, `FileReader`, `IOError` neexistují.

- [ ] **Step 3: P/Invoke deklarace**

`windows/src/Offload.Core/Win32.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Offload.Core;

internal static unsafe partial class Win32
{
    internal const uint MOVEFILE_REPLACE_EXISTING = 0x1;
    internal const uint MOVEFILE_WRITE_THROUGH = 0x8;
    internal const uint ES_SYSTEM_REQUIRED = 0x1;
    internal const uint ES_CONTINUOUS = 0x80000000;
    internal const int FileStorageInfo = 16;

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_STORAGE_INFO
    {
        public uint LogicalBytesPerSector;
        public uint PhysicalBytesPerSectorForAtomicity;
        public uint PhysicalBytesPerSectorForPerformance;
        public uint FileSystemEffectivePhysicalBytesPerSectorForAtomicity;
        public uint Flags;
        public uint ByteOffsetForSectorAlignment;
        public uint ByteOffsetForPartitionAlignment;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FlushFileBuffers(SafeFileHandle file);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetVolumePathName(string fileName, char* volumePathName, uint bufferLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetVolumeNameForVolumeMountPoint(string volumeMountPoint, char* volumeName, uint bufferLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetVolumeInformation(string rootPathName, char* volumeNameBuffer, uint volumeNameSize,
        out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags,
        char* fileSystemNameBuffer, uint fileSystemNameSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetDiskFreeSpaceEx(string directoryName, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int fileInformationClass, void* info, uint bufferSize);

    [LibraryImport("kernel32.dll")]
    internal static partial uint SetThreadExecutionState(uint flags);
}
```

- [ ] **Step 4: Chyba, buffer, čtečka**

`windows/src/Offload.Core/IOError.cs`:

```csharp
using System.ComponentModel;

namespace Offload.Core;

/// I/O failure with the operation and the file, so a report line says exactly what went wrong where.
public sealed class IOError : Exception
{
    public string Op { get; }
    public string FilePath { get; }
    /// Win32 error code (e.g. 87 = invalid parameter, 112 = disk full).
    public int Code { get; }

    public IOError(string op, string path, int code)
        : base($"{op} {path}: {new Win32Exception(code).Message} (error {code})")
    {
        Op = op;
        FilePath = path;
        Code = code;
    }

    public IOError(string op, string path, Exception inner)
        : base($"{op} {path}: {inner.Message}", inner)
    {
        Op = op;
        FilePath = path;
        Code = inner.HResult & 0xFFFF;
    }
}
```

`windows/src/Offload.Core/AlignedBuffer.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Offload.Core;

/// Native memory aligned to 4096 B: unbuffered I/O needs sector-aligned buffers and sizes.
public sealed unsafe class AlignedBuffer : IDisposable
{
    public const int Alignment = 4096;
    byte* pointer;

    public int Size { get; }

    public AlignedBuffer(int size)
    {
        if (size <= 0 || size % Alignment != 0)
            throw new ArgumentException($"Buffer size must be a positive multiple of {Alignment}", nameof(size));
        Size = size;
        pointer = (byte*)NativeMemory.AlignedAlloc((nuint)size, Alignment);
    }

    public Span<byte> Span => new(pointer, Size);

    public void Dispose()
    {
        if (pointer == null) return;
        NativeMemory.AlignedFree(pointer);
        pointer = null;
    }
}
```

`windows/src/Offload.Core/FileReader.cs`:

```csharp
using Microsoft.Win32.SafeHandles;

namespace Offload.Core;

/// Reads a file past the Windows file cache. Volumes that refuse unbuffered I/O (some network shares)
/// fall back to normal reads instead of failing.
public sealed class FileReader : IDisposable
{
    readonly string path;
    SafeFileHandle handle;

    public bool Unbuffered { get; private set; } = true;

    public FileReader(string path)
    {
        this.path = path;
        try
        {
            handle = NativeIO.OpenRead(path, unbuffered: true);
        }
        catch (IOError e) when (e.Code == NativeIO.ErrorInvalidParameter)
        {
            Unbuffered = false;
            handle = NativeIO.OpenRead(path, unbuffered: false);
        }
    }

    /// One read at `offset` (a multiple of the buffer size); fewer bytes than asked only at end of file.
    public int Read(Span<byte> buffer, long offset)
    {
        try
        {
            return NativeIO.Read(handle, buffer, offset, path);
        }
        catch (IOError e) when (Unbuffered && e.Code == NativeIO.ErrorInvalidParameter)
        {
            handle.Dispose();
            Unbuffered = false;
            handle = NativeIO.OpenRead(path, unbuffered: false);
            return NativeIO.Read(handle, buffer, offset, path);
        }
    }

    public void Dispose() => handle.Dispose();
}
```

- [ ] **Step 5: NativeIO**

`windows/src/Offload.Core/NativeIO.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Offload.Core;

/// Thin wrappers over Win32 file I/O. Card reads and disk writes bypass the Windows file cache
/// (FILE_FLAG_NO_BUFFERING), so verification reads the media, never what is still in RAM.
public static unsafe class NativeIO
{
    const FileOptions NoBuffering = (FileOptions)0x20000000;
    public const int ErrorInvalidParameter = 87;

    public static SafeFileHandle OpenRead(string path, bool unbuffered) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            unbuffered ? NoBuffering | FileOptions.SequentialScan : FileOptions.SequentialScan));

    /// Creates or truncates a file. `preallocate` reserves the space up front, so a full disk fails here, not halfway.
    public static SafeFileHandle OpenWrite(string path, bool unbuffered, long preallocate = 0) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None,
            unbuffered ? NoBuffering : FileOptions.None, preallocate));

    public static SafeFileHandle OpenExisting(string path) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.None));

    /// One read at `offset`; fewer bytes than asked only at end of file.
    public static int Read(SafeFileHandle h, Span<byte> buffer, long offset, string path)
    {
        try { return RandomAccess.Read(h, buffer, offset); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new IOError("read", path, e); }
    }

    public static void Write(SafeFileHandle h, ReadOnlySpan<byte> data, long offset, string path)
    {
        try { RandomAccess.Write(h, data, offset); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new IOError("write", path, e); }
    }

    public static void SetLength(SafeFileHandle h, long length, string path) =>
        Wrap("truncate", path, () => { RandomAccess.SetLength(h, length); return 0; });

    /// Writes the file out and makes the drive empty its own write cache (the F_FULLFSYNC of Windows).
    public static void Flush(SafeFileHandle h, string path)
    {
        if (!Win32.FlushFileBuffers(h)) throw new IOError("flush", path, Marshal.GetLastPInvokeError());
    }

    public static void SetModified(SafeFileHandle h, DateTime utc, string path) =>
        Wrap("settime", path, () => { File.SetLastWriteTimeUtc(h, utc); return 0; });

    public static void Move(string from, string to, bool replace)
    {
        uint flags = Win32.MOVEFILE_WRITE_THROUGH | (replace ? Win32.MOVEFILE_REPLACE_EXISTING : 0);
        if (!Win32.MoveFileEx(LongPath(from), LongPath(to), flags)) throw new IOError("rename", to, Marshal.GetLastPInvokeError());
    }

    /// Best effort: used for cleanup, never fails.
    public static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// Write + flush via a temp name, so a crash never leaves a half-written report.
    public static void WriteFileDurably(string path, ReadOnlySpan<byte> data)
    {
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + ".tmp");
        try
        {
            using (var h = OpenWrite(tmp, unbuffered: false))
            {
                if (!data.IsEmpty) Write(h, data, 0, tmp);
                Flush(h, tmp);
            }
            Move(tmp, path, replace: true);
        }
        catch
        {
            Delete(tmp);
            throw;
        }
    }

    public static void Mkdirs(string path) => Wrap("mkdir", path, () => Directory.CreateDirectory(path));

    public static string? VolumeRoot(string path)
    {
        char* buf = stackalloc char[1024];
        return Win32.GetVolumePathName(Path.GetFullPath(path), buf, 1024) ? new string(buf) : null;
    }

    /// Identity of the volume a path is on: the volume GUID path, or the share root for network paths.
    public static string? VolumeId(string path)
    {
        var root = VolumeRoot(path);
        if (root == null) return null;
        char* buf = stackalloc char[64];
        return Win32.GetVolumeNameForVolumeMountPoint(root, buf, 64) ? new string(buf) : root.ToLowerInvariant();
    }

    /// "NTFS", "exFAT", "FAT32", "ReFS" … or "?".
    public static string FilesystemType(string path)
    {
        var root = VolumeRoot(path);
        if (root == null) return "?";
        char* fs = stackalloc char[64];
        return Win32.GetVolumeInformation(root, null, 0, out _, out _, out _, fs, 64) ? new string(fs) : "?";
    }

    public static string? VolumeLabel(string path)
    {
        var root = VolumeRoot(path);
        if (root == null) return null;
        char* name = stackalloc char[261];
        return Win32.GetVolumeInformation(root, name, 261, out _, out _, out _, null, 0) ? new string(name) : null;
    }

    public static long? AvailableBytes(string path) =>
        Win32.GetDiskFreeSpaceEx(Path.GetFullPath(path), out var free, out _, out _) ? (long)free : null;

    /// Write size unit for unbuffered I/O on this file's volume (at least 4096).
    public static int SectorSize(SafeFileHandle h)
    {
        Win32.FILE_STORAGE_INFO info;
        if (!Win32.GetFileInformationByHandleEx(h, Win32.FileStorageInfo, &info, (uint)sizeof(Win32.FILE_STORAGE_INFO)))
            return AlignedBuffer.Alignment;
        return (int)Math.Max(AlignedBuffer.Alignment, Math.Max(info.LogicalBytesPerSector, info.PhysicalBytesPerSectorForPerformance));
    }

    /// Keeps the system from sleeping while the returned object lives. Dispose on the same thread.
    public static IDisposable KeepAwake()
    {
        Win32.SetThreadExecutionState(Win32.ES_CONTINUOUS | Win32.ES_SYSTEM_REQUIRED);
        return new AwakeToken();
    }

    sealed class AwakeToken : IDisposable
    {
        public void Dispose() => Win32.SetThreadExecutionState(Win32.ES_CONTINUOUS);
    }

    /// Win32 calls (unlike .NET APIs) need the \\?\ prefix for paths over 260 characters.
    internal static string LongPath(string p)
    {
        var full = Path.GetFullPath(p);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) return full;
        return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }

    static T Wrap<T>(string op, string path, Func<T> f)
    {
        try { return f(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new IOError(op, path, e); }
    }
}
```

- [ ] **Step 6: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~NativeIOTests"`
Expected: PASS (8 testů).

- [ ] **Step 7: Commit**

```powershell
git add windows
git commit -m "Windows: native unbuffered I/O, volume queries" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Layout

**Files:**
- Create: `windows/src/Offload.Core/Layout.cs`
- Test: `windows/tests/Offload.Core.Tests/LayoutTests.cs`

**Interfaces:**
- Consumes: `NativeIO.VolumeLabel(string)`.
- Produces: `Layout.ReportDirName` (`"_REPORT"`), `Layout.ProxyDirName` (`"_PROXY"`), `Today() -> string`, `Join(string root, string rel) -> string`, `CardPath(string root, string date, string cardName) -> string`, `DefaultCardName(string source) -> string`, `Sanitize(string) -> string`, `InvalidNameReason(string name) -> string?`, `UniqueCardFolder(IEnumerable<string> roots, string date, string name) -> string`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/LayoutTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class LayoutTests
{
    [Theory]
    [InlineData("A001", "A001")]
    [InlineData("  CARD/1:2  ", "CARD_1_2")]
    [InlineData("a<b>c|d?e*f\"g\\h", "a_b_c_d_e_f_g_h")]
    [InlineData("..", "CARD")]
    [InlineData("", "CARD")]
    [InlineData("name. ", "name")]
    [InlineData("CON", "_CON")]
    [InlineData("Natáčení den 1", "Natáčení den 1")]
    public void Sanitize(string input, string expected) => Assert.Equal(expected, Layout.Sanitize(input));

    [Theory]
    [InlineData("clip.mov", true)]
    [InlineData("Záběr 01", true)]
    [InlineData("a:b", false)]
    [InlineData("x.", false)]
    [InlineData("x ", false)]
    [InlineData("NUL.txt", false)]
    [InlineData("tab\there", false)]
    public void InvalidNameReason(string name, bool valid) => Assert.Equal(valid, Layout.InvalidNameReason(name) == null);

    [Fact]
    public void Join_ConvertsReportPathToWindowsPath() =>
        Assert.Equal(@"D:\dst\A\B\c.mov", Layout.Join(@"D:\dst", "A/B/c.mov"));

    [Fact]
    public void CardPath_IsRootDateCard() =>
        Assert.Equal(@"D:\dst\2026-10-04\A001", Layout.CardPath(@"D:\dst", "2026-10-04", "A001"));

    [Fact]
    public void UniqueCardFolder_AddsSuffixFreeOnEveryRoot()
    {
        using var dir = new TempDir();
        var r1 = dir.Sub("r1");
        var r2 = dir.Sub("r2");
        Assert.Equal("A001", Layout.UniqueCardFolder([r1, r2], "2026-10-04", "A001"));
        dir.Sub("r1", "2026-10-04", "A001");
        Assert.Equal("A001_2", Layout.UniqueCardFolder([r1, r2], "2026-10-04", "A001"));
        dir.Sub("r2", "2026-10-04", "A001_2");
        Assert.Equal("A001_3", Layout.UniqueCardFolder([r1, r2], "2026-10-04", "A001"));
    }

    [Fact]
    public void DefaultCardName_OfFolderIsFolderName()
    {
        using var dir = new TempDir();
        Assert.Equal("Card X", Layout.DefaultCardName(dir.Sub("Card X")));
    }

    [Fact]
    public void Today_IsIsoDate() => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", Layout.Today());
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~LayoutTests"`
Expected: build FAIL — `Layout` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Layout.cs`:

```csharp
using System.Globalization;

namespace Offload.Core;

/// Destination layout: <root>/<YYYY-MM-DD>/<card name>/
public static class Layout
{
    public const string ReportDirName = "_REPORT";
    public const string ProxyDirName = "_PROXY";

    const string InvalidChars = "<>:\"/\\|?*";

    static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Today() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// Joins a root folder with a report-style relative path ("A/B/c.mov").
    public static string Join(string root, string rel) => Path.Combine(root, rel.Replace('/', '\\'));

    public static string CardPath(string root, string date, string cardName) => Path.Combine(root, date, cardName);

    /// Volume label when the source is a drive root, otherwise the folder name.
    public static string DefaultCardName(string source)
    {
        var full = Path.GetFullPath(source);
        var root = Path.GetPathRoot(full);
        if (root != null && string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            var label = NativeIO.VolumeLabel(root);
            if (!string.IsNullOrWhiteSpace(label)) return Sanitize(label);
        }
        return Sanitize(Path.GetFileName(full.TrimEnd('\\')));
    }

    /// A name Windows can use as a folder: forbidden characters become "_", no trailing dot or space, no device names.
    public static string Sanitize(string s)
    {
        var chars = s.Select(c => c < 0x20 || InvalidChars.Contains(c) ? '_' : c).ToArray();
        var o = new string(chars).Trim().TrimEnd('.', ' ');
        if (o.Length == 0) return "CARD";
        return ReservedNames.Contains(BaseName(o)) ? "_" + o : o;
    }

    /// Why Windows cannot store a file or folder with this name, or null when it can.
    public static string? InvalidNameReason(string name)
    {
        if (name.Any(c => c < 0x20 || InvalidChars.Contains(c))) return "contains a character Windows does not allow";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "ends with a dot or space";
        if (ReservedNames.Contains(BaseName(name))) return "reserved device name on Windows";
        return null;
    }

    static string BaseName(string name)
    {
        int dot = name.IndexOf('.');
        return (dot < 0 ? name : name[..dot]).TrimEnd(' ');
    }

    /// Card folder name that is free on every destination (NAME, NAME_2, NAME_3 …),
    /// so a re-inserted card never overwrites an earlier offload.
    public static string UniqueCardFolder(IEnumerable<string> roots, string date, string name)
    {
        var list = roots.ToList();
        var candidate = name;
        for (int n = 2; list.Any(r => Path.Exists(CardPath(r, date, candidate))); n++) candidate = $"{name}_{n}";
        return candidate;
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~LayoutTests"`
Expected: PASS (20 testů).

- [ ] **Step 5: Commit**

```powershell
git add windows
git commit -m "Windows: destination layout and name rules" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Scanner

**Files:**
- Create: `windows/src/Offload.Core/Scanner.cs`
- Test: `windows/tests/Offload.Core.Tests/ScannerTests.cs`

**Interfaces:**
- Consumes: `IOError`, `Layout.InvalidNameReason`.
- Produces: `record SourceFile(string RelPath, long Size, DateTime ModifiedUtc)`; `SourceScan` (`Root`, `IReadOnlyList<SourceFile> Files`, `IReadOnlyList<string> Dirs`, `IReadOnlyList<string> Skipped`, `long TotalBytes`); `Scanner.Scan(string root) -> SourceScan`; `Scanner.WindowsProblems(SourceScan) -> IReadOnlyList<string>`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/ScannerTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class ScannerTests
{
    [Fact]
    public void Scan_ListsFilesSortedWithSizesAndTimes_SkipsJunk()
    {
        using var dir = new TempDir();
        var root = dir.Sub("card");
        TestFs.WriteRandom(Path.Combine(root, "CLIP", "b.mov"), 10, 1);
        TestFs.WriteRandom(Path.Combine(root, "CLIP", "a.mov"), 20, 2);
        TestFs.WriteRandom(Path.Combine(root, "root.xml"), 5, 3);
        TestFs.WriteRandom(Path.Combine(root, ".DS_Store"), 1, 4);
        TestFs.WriteRandom(Path.Combine(root, "CLIP", "._a.mov"), 1, 5);
        TestFs.WriteRandom(Path.Combine(root, "Thumbs.db"), 1, 6);
        TestFs.WriteRandom(Path.Combine(root, "System Volume Information", "x"), 1, 7);
        TestFs.WriteRandom(Path.Combine(root, ".Trashes", "y"), 1, 8);
        var hidden = Path.Combine(root, "CLIP", "hidden.bin");
        TestFs.WriteRandom(hidden, 3, 9);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        dir.Sub("card", "EMPTY");
        var when = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(root, "CLIP", "a.mov"), when);

        var scan = Scanner.Scan(root);

        Assert.Equal(["CLIP/a.mov", "CLIP/b.mov", "CLIP/hidden.bin", "root.xml"], scan.Files.Select(f => f.RelPath));
        Assert.Equal(["CLIP", "EMPTY"], scan.Dirs);
        Assert.Equal(38, scan.TotalBytes);
        Assert.Equal(when, scan.Files[0].ModifiedUtc);
        Assert.Equal(
            new[] { ".DS_Store", ".Trashes", "CLIP/._a.mov", "System Volume Information", "Thumbs.db" }.Order(StringComparer.Ordinal),
            scan.Skipped.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Scan_MissingRoot_ThrowsIOError() =>
        Assert.Throws<IOError>(() => Scanner.Scan(@"C:\offload-no-such-folder"));

    [Fact]
    public void WindowsProblems_FindsCaseCollisionsAndInvalidNames()
    {
        var scan = new SourceScan
        {
            Root = @"X:\",
            Files =
            [
                new SourceFile("CLIP/A001.MOV", 1, DateTime.UtcNow),
                new SourceFile("CLIP/a001.mov", 1, DateTime.UtcNow),
                new SourceFile("CLIP/bad:name.mov", 1, DateTime.UtcNow),
                new SourceFile("CLIP/ok.mov", 1, DateTime.UtcNow),
            ],
            Dirs = ["CLIP"],
            Skipped = [],
        };
        var problems = Scanner.WindowsProblems(scan);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("CLIP/a001.mov") && p.Contains("upper/lower case"));
        Assert.Contains(problems, p => p.StartsWith("CLIP/bad:name.mov"));
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~ScannerTests"`
Expected: build FAIL — `Scanner`, `SourceScan`, `SourceFile` neexistují.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Scanner.cs`:

```csharp
namespace Offload.Core;

public sealed record SourceFile(string RelPath, long Size, DateTime ModifiedUtc);

public sealed class SourceScan
{
    public required string Root { get; init; }
    /// Relative paths with "/", sorted ordinally.
    public required IReadOnlyList<SourceFile> Files { get; init; }
    public required IReadOnlyList<string> Dirs { get; init; }
    /// System housekeeping, links and other non-regular entries that are not copied.
    public required IReadOnlyList<string> Skipped { get; init; }
    public long TotalBytes => Files.Sum(f => f.Size);
}

public static class Scanner
{
    /// Housekeeping folders of macOS and Windows that are not camera data.
    static readonly HashSet<string> JunkDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".Spotlight-V100", ".fseventsd", ".Trashes", ".TemporaryItems", ".DocumentRevisions-V100",
        "System Volume Information", "$RECYCLE.BIN",
    };

    static bool IsJunkFile(string name) =>
        name == ".DS_Store" || name.StartsWith("._", StringComparison.Ordinal)
        || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);

    static readonly EnumerationOptions OneLevel = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
    };

    public static SourceScan Scan(string root)
    {
        var files = new List<SourceFile>();
        var dirs = new List<string>();
        var skipped = new List<string>();

        void Walk(string dir, string prefix)
        {
            List<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", OneLevel).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new IOError("scan", dir, e); }
            foreach (var e in entries)
            {
                var rel = prefix.Length == 0 ? e.Name : prefix + "/" + e.Name;
                if ((e.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skipped.Add(rel);
                }
                else if (e is DirectoryInfo d)
                {
                    if (JunkDirs.Contains(e.Name)) { skipped.Add(rel); continue; }
                    dirs.Add(rel);
                    Walk(d.FullName, rel);
                }
                else if (e is FileInfo f)
                {
                    if (IsJunkFile(e.Name)) skipped.Add(rel);
                    else files.Add(new SourceFile(rel, f.Length, f.LastWriteTimeUtc));
                }
            }
        }

        Walk(Path.GetFullPath(root), "");
        files.Sort((a, b) => string.CompareOrdinal(a.RelPath, b.RelPath));
        dirs.Sort(StringComparer.Ordinal);
        return new SourceScan { Root = root, Files = files, Dirs = dirs, Skipped = skipped };
    }

    /// Paths NTFS / exFAT destinations cannot hold as they are: names that differ only in case
    /// (they would overwrite each other) and names Windows forbids.
    public static IReadOnlyList<string> WindowsProblems(SourceScan scan)
    {
        var problems = new List<string>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in scan.Dirs.Concat(scan.Files.Select(f => f.RelPath)))
        {
            if (seen.TryGetValue(p, out var other)) problems.Add($"{p}: same name as {other} except for upper/lower case");
            else seen[p] = p;
            var reason = p.Split('/').Select(Layout.InvalidNameReason).FirstOrDefault(r => r != null);
            if (reason != null) problems.Add($"{p}: {reason}");
        }
        return problems;
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~ScannerTests"`
Expected: PASS (3 testy).

- [ ] **Step 5: Commit**

```powershell
git add windows
git commit -m "Windows: card scanner with Windows name checks" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Hashing

**Files:**
- Create: `windows/src/Offload.Core/Hashing.cs`
- Test: `windows/tests/Offload.Core.Tests/HashingTests.cs`

**Interfaces:**
- Consumes: `FileReader`, `AlignedBuffer`.
- Produces: `Hashing.HashFile(string path, AlignedBuffer buffer, Action<int>? progress = null, CancellationToken ct = default) -> (ulong Hash, long Size)`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/HashingTests.cs`:

```csharp
using System.IO.Hashing;

namespace Offload.Core.Tests;

public class HashingTests
{
    [Fact]
    public void KnownVectors()
    {
        using var dir = new TempDir();
        using var buf = new AlignedBuffer(4096);
        var empty = Path.Combine(dir.Path, "empty");
        var abc = Path.Combine(dir.Path, "abc");
        File.WriteAllBytes(empty, []);
        File.WriteAllText(abc, "abc");
        Assert.Equal((0xef46db3751d8e999UL, 0L), Hashing.HashFile(empty, buf));
        Assert.Equal((0x44bc2cf5ad770999UL, 3L), Hashing.HashFile(abc, buf));
    }

    [Theory]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(3 * 4096 + 1)]
    public void MatchesInMemoryHash_AcrossBlockBoundaries(int size)
    {
        using var dir = new TempDir();
        using var buf = new AlignedBuffer(4096);
        var path = Path.Combine(dir.Path, "f");
        var data = TestFs.WriteRandom(path, size, size);
        long progress = 0;
        var (hash, read) = Hashing.HashFile(path, buf, n => progress += n);
        Assert.Equal(XxHash64.HashToUInt64(data), hash);
        Assert.Equal(size, read);
        Assert.Equal(size, progress);
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~HashingTests"`
Expected: build FAIL — `Hashing` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Hashing.cs`:

```csharp
using System.IO.Hashing;

namespace Offload.Core;

public static class Hashing
{
    /// Hashes a whole file straight from the media (no file cache). XXH64 is the hash DIT tools and ASC MHL use.
    public static (ulong Hash, long Size) HashFile(string path, AlignedBuffer buffer, Action<int>? progress = null, CancellationToken ct = default)
    {
        using var reader = new FileReader(path);
        var xx = new XxHash64();
        long offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int n = reader.Read(buffer.Span, offset);
            if (n == 0) break;
            xx.Append(buffer.Span[..n]);
            offset += n;
            progress?.Invoke(n);
            if (n < buffer.Size) break;
        }
        return (xx.GetCurrentHashAsUInt64(), offset);
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~HashingTests"`
Expected: PASS (5 testů).

- [ ] **Step 5: Commit**

```powershell
git add windows
git commit -m "Windows: unbuffered xxh64 file hashing" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Datové typy (engine + média)

**Files:**
- Create: `windows/src/Offload.Core/Models.cs`, `windows/src/Offload.Core/Media.cs`
- Test: `windows/tests/Offload.Core.Tests/MediaTests.cs`

**Interfaces:**
- Consumes: `SourceScan`, `Layout.CardPath`.
- Produces:
  - `OffloadOptions` — `ChunkSize` (16 MiB), `BufferCount` (16), `ReadRetries` (3), `Verify` (true), `FullSyncThreshold` (256 MiB), `AllowSameDevice`; internal `AllowSourceVolume`, `ForceBuffered` (jen testy).
  - `enum FileState { Pending, Written, Verified, Failed }`, `FileState.Wire() -> "pending"|"written"|"verified"|"failed"`.
  - `readonly record struct DestFileResult(FileState State, string? Error)`.
  - `OffloadPlan` — `Source`, `Scan`, `Date`, `CardName`, `DestRoots`, `CardPaths`.
  - `enum CopyPhase { Copy, Flush, Verify, Report, Done }`; `record ProgressSnapshot(CopyPhase Phase, long TotalBytes, int TotalFiles, int FileIndex, string CurrentFile, long ReadBytes, long[] WrittenBytes, long[] VerifiedBytes, int Errors)`.
  - `OffloadError : Exception` — `IsPreflight`, `OffloadError.Preflight(string)`, `OffloadError.Failed(string)`.
  - `ClipFormat` (+ `Label()`), `Clip`, `ClipInfo` (+ `FpsLabel`, `ResolutionLabel`, `DurationLabel`), `ProxyStatus`, `ClipEntry`, `ProxyCodec` (+ `Label()`), `ProxySettings` (+ `FrameCrop(int, int)`, `FrameLabel`). JSON názvy přesně jako Swift `Codable`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/MediaTests.cs`:

```csharp
using System.Text.Json;

namespace Offload.Core.Tests;

public class MediaTests
{
    [Fact]
    public void FrameCrop_CentersEvenSizedFrame()
    {
        var s = new ProxySettings { FrameAspect = 1.66, FrameScale = 0.9 };
        Assert.Equal((306, 108, 3226, 1944), s.FrameCrop(3840, 2160)!.Value);
    }

    [Fact]
    public void FrameCrop_NullWithoutAspect() => Assert.Null(new ProxySettings().FrameCrop(3840, 2160));

    [Fact]
    public void FrameLabel_ShowsAspectAndScale()
    {
        Assert.Equal("1.66 · 90 %", new ProxySettings { FrameAspect = 1.66, FrameScale = 0.9 }.FrameLabel);
        Assert.Equal("2.39", new ProxySettings { FrameAspect = 2.39 }.FrameLabel);
        Assert.Null(new ProxySettings().FrameLabel);
    }

    [Theory]
    [InlineData("24000/1001", "23.976")]
    [InlineData("25/1", "25")]
    [InlineData("50", "50")]
    [InlineData(null, "")]
    public void FpsLabel(string? rate, string expected) => Assert.Equal(expected, new ClipInfo { FrameRate = rate }.FpsLabel);

    [Fact]
    public void DurationAndResolutionLabels()
    {
        var i = new ClipInfo { Duration = 3725.5, Width = 3840, Height = 2160 };
        Assert.Equal("1:02:06", i.DurationLabel);
        Assert.Equal("3840×2160", i.ResolutionLabel);
        Assert.Equal("", new ClipInfo().DurationLabel);
    }

    [Fact]
    public void ProxySettings_MissingKeysKeepDefaults()
    {
        var s = JsonSerializer.Deserialize<ProxySettings>("""{"enabled":true,"codec":"proresLT"}""")!;
        Assert.True(s.Enabled);
        Assert.Equal(ProxyCodec.ProresLT, s.Codec);
        Assert.Equal(1080, s.Height);
        Assert.True(s.BurnFilename);
        Assert.Equal(1.0, s.FrameScale);
    }

    [Fact]
    public void Enums_UseMacWireNames()
    {
        Assert.Equal("\"canonRaw\"", JsonSerializer.Serialize(ClipFormat.CanonRaw));
        Assert.Equal("\"unsupported\"", JsonSerializer.Serialize(ProxyStatus.Unsupported));
        Assert.Equal("\"prores422HQ\"", JsonSerializer.Serialize(ProxyCodec.Prores422HQ));
        Assert.Equal("verified", FileState.Verified.Wire());
    }

    [Fact]
    public void Plan_CardPathsAreRootDateCard()
    {
        var plan = new OffloadPlan
        {
            Source = @"E:\", Date = "2026-10-04", CardName = "A001", DestRoots = [@"D:\x", @"F:\"],
            Scan = new SourceScan { Root = @"E:\", Files = [], Dirs = [], Skipped = [] },
        };
        Assert.Equal([@"D:\x\2026-10-04\A001", @"F:\2026-10-04\A001"], plan.CardPaths);
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~MediaTests"`
Expected: build FAIL — `ProxySettings`, `ClipInfo`, … neexistují.

- [ ] **Step 3: Engine typy**

`windows/src/Offload.Core/Models.cs`:

```csharp
namespace Offload.Core;

public sealed class OffloadOptions
{
    /// Read/write block size. Large sequential blocks are what cards and disks like best. Multiple of 4096.
    public int ChunkSize { get; set; } = 16 << 20;
    /// Blocks in flight. Lets a slower destination lag behind without stalling the card read.
    public int BufferCount { get; set; } = 16;
    public int ReadRetries { get; set; } = 3;
    public bool Verify { get; set; } = true;
    /// Files at least this large are flushed individually; the rest are flushed once at the end.
    public long FullSyncThreshold { get; set; } = 256L << 20;
    public bool AllowSameDevice { get; set; }
    /// Tests only: source and destinations in temp folders share a volume.
    internal bool AllowSourceVolume { get; set; }
    /// Tests only: behave as on a volume that refuses unbuffered writes.
    internal bool ForceBuffered { get; set; }
}

public enum FileState { Pending, Written, Verified, Failed }

public static class FileStateExtensions
{
    public static string Wire(this FileState s) => s switch
    {
        FileState.Written => "written",
        FileState.Verified => "verified",
        FileState.Failed => "failed",
        _ => "pending",
    };
}

public readonly record struct DestFileResult(FileState State, string? Error);

public sealed class OffloadPlan
{
    public required string Source { get; init; }
    public required SourceScan Scan { get; init; }
    public required string Date { get; init; }
    public required string CardName { get; init; }
    public required IReadOnlyList<string> DestRoots { get; init; }
    public IReadOnlyList<string> CardPaths => DestRoots.Select(r => Layout.CardPath(r, Date, CardName)).ToList();
}

public enum CopyPhase { Copy, Flush, Verify, Report, Done }

public sealed record ProgressSnapshot(
    CopyPhase Phase, long TotalBytes, int TotalFiles, int FileIndex, string CurrentFile,
    long ReadBytes, long[] WrittenBytes, long[] VerifiedBytes, int Errors);

public sealed class OffloadError : Exception
{
    public bool IsPreflight { get; }

    OffloadError(string message, bool preflight) : base(message) => IsPreflight = preflight;

    public static OffloadError Preflight(string message) => new(message, true);
    public static OffloadError Failed(string message) => new(message, false);
}
```

- [ ] **Step 4: Mediální typy (jen data, logika analýzy je v plánu 2)**

`windows/src/Offload.Core/Media.cs`:

```csharp
using System.Globalization;
using System.Text.Json.Serialization;

namespace Offload.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ClipFormat>))]
public enum ClipFormat
{
    [JsonStringEnumMemberName("video")] Video,      // anything ffmpeg decodes: ProRes, H.264/H.265, XAVC, DNx …
    [JsonStringEnumMemberName("braw")] Braw,
    [JsonStringEnumMemberName("r3d")] R3d,
    [JsonStringEnumMemberName("arriraw")] Arriraw,  // .ari sequence or .mxf
    [JsonStringEnumMemberName("canonRaw")] CanonRaw,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

public static class ClipFormatExtensions
{
    public static string Label(this ClipFormat f) => f switch
    {
        ClipFormat.Video => "Video",
        ClipFormat.Braw => "Blackmagic RAW",
        ClipFormat.R3d => "RED R3D",
        ClipFormat.Arriraw => "ARRIRAW",
        ClipFormat.CanonRaw => "Canon RAW",
        _ => "?",
    };
}

public sealed class Clip
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    /// Primary file, relative to the card folder (for .ari sequences the folder).
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("format")] public ClipFormat Format { get; set; }
    [JsonPropertyName("segments")] public required IReadOnlyList<string> Segments { get; init; }
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    [JsonPropertyName("isCameraProxy")] public bool IsCameraProxy { get; init; }
}

public sealed class ClipInfo
{
    [JsonPropertyName("codec")] public string? Codec { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    /// Rational as ffmpeg likes it ("24000/1001").
    [JsonPropertyName("frameRate")] public string? FrameRate { get; set; }
    [JsonPropertyName("duration")] public double? Duration { get; set; }
    [JsonPropertyName("frames")] public int? Frames { get; set; }
    [JsonPropertyName("timecode")] public string? Timecode { get; set; }
    [JsonPropertyName("audioChannels")] public int AudioChannels { get; set; }
    [JsonPropertyName("camera")] public string? Camera { get; set; }
    /// Log curve the camera recorded in (e.g. "LogC4"), when the format reports it.
    [JsonPropertyName("logEncoding")] public string? LogEncoding { get; set; }
    /// Anamorphic squeeze of the lens (2.0 for 2x), when the format reports it.
    [JsonPropertyName("squeeze")] public double? Squeeze { get; set; }

    [JsonIgnore]
    public string FpsLabel
    {
        get
        {
            if (FrameRate == null) return "";
            var parts = FrameRate.Split('/')
                .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
                .Where(v => !double.IsNaN(v)).ToArray();
            double fps = parts.Length == 2 && parts[1] != 0 ? parts[0] / parts[1] : parts.FirstOrDefault();
            return fps == Math.Round(fps)
                ? ((long)fps).ToString(CultureInfo.InvariantCulture)
                : fps.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }

    [JsonIgnore]
    public string ResolutionLabel => Width is int w && Height is int h ? $"{w}×{h}" : "";

    [JsonIgnore]
    public string DurationLabel
    {
        get
        {
            if (Duration is not double d) return "";
            int s = (int)Math.Round(d, MidpointRounding.AwayFromZero);
            return $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}";
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<ProxyStatus>))]
public enum ProxyStatus
{
    [JsonStringEnumMemberName("none")] None,
    [JsonStringEnumMemberName("skipped")] Skipped,
    [JsonStringEnumMemberName("unsupported")] Unsupported,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("failed")] Failed,
}

public sealed class ClipEntry
{
    [JsonPropertyName("clip")] public required Clip Clip { get; set; }
    [JsonPropertyName("info")] public ClipInfo? Info { get; set; }
    [JsonPropertyName("probeError")] public string? ProbeError { get; set; }
    [JsonPropertyName("proxyStatus")] public ProxyStatus ProxyStatus { get; set; } = ProxyStatus.None;
    /// Relative to the card folder.
    [JsonPropertyName("proxyPath")] public string? ProxyPath { get; set; }
    [JsonPropertyName("proxyNote")] public string? ProxyNote { get; set; }
    [JsonPropertyName("proxySeconds")] public double? ProxySeconds { get; set; }
    /// JPEG; embedded in the HTML report only.
    [JsonIgnore] public byte[]? Thumbnail { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ProxyCodec>))]
public enum ProxyCodec
{
    [JsonStringEnumMemberName("h264")] H264,
    [JsonStringEnumMemberName("hevc")] Hevc,
    [JsonStringEnumMemberName("proresProxy")] ProresProxy,
    [JsonStringEnumMemberName("proresLT")] ProresLT,
    [JsonStringEnumMemberName("prores422")] Prores422,
    [JsonStringEnumMemberName("prores422HQ")] Prores422HQ,
}

public static class ProxyCodecExtensions
{
    public static string Label(this ProxyCodec c) => c switch
    {
        ProxyCodec.H264 => "H.264",
        ProxyCodec.Hevc => "H.265 / HEVC",
        ProxyCodec.ProresProxy => "ProRes 422 Proxy",
        ProxyCodec.ProresLT => "ProRes 422 LT",
        ProxyCodec.Prores422 => "ProRes 422",
        _ => "ProRes 422 HQ",
    };
}

public sealed class ProxySettings
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; } = 1080;
    [JsonPropertyName("codec")] public ProxyCodec Codec { get; set; } = ProxyCodec.H264;
    [JsonPropertyName("bitrateMbps")] public int BitrateMbps { get; set; } = 10;
    /// .cube 3D LUT applied after downscaling (cheap at proxy resolution).
    [JsonPropertyName("lutPath")] public string? LutPath { get; set; }
    [JsonPropertyName("burnFilename")] public bool BurnFilename { get; set; } = true;
    [JsonPropertyName("burnTimecode")] public bool BurnTimecode { get; set; } = true;
    [JsonPropertyName("audio")] public bool Audio { get; set; } = true;
    /// Skip the low-res files cameras record themselves (Sony SUB, PROXY folders).
    [JsonPropertyName("skipCameraProxies")] public bool SkipCameraProxies { get; set; } = true;
    /// Proxies are rendered once, onto the first destination, then copied to the others.
    [JsonPropertyName("copyToAllDestinations")] public bool CopyToAllDestinations { get; set; } = true;
    /// Crop to the frame lines: aspect ratio of the frame (e.g. 1.66); null = full image.
    [JsonPropertyName("frameAspect")] public double? FrameAspect { get; set; }
    /// How much of the image the frame covers (0.9 = 90 %, i.e. 10 % overscan around it).
    [JsonPropertyName("frameScale")] public double FrameScale { get; set; } = 1.0;

    /// Centered crop rectangle (x, y, w, h) inside a W×H image, even-sized, or null when no crop is set.
    public (int X, int Y, int W, int H)? FrameCrop(int width, int height)
    {
        if (FrameAspect is not double aspect || aspect <= 0 || width <= 0 || height <= 0) return null;
        double scale = Math.Min(Math.Max(FrameScale, 0.1), 1.0);
        double w = width * scale;
        double h = w / aspect;
        if (h > height * scale)
        {
            h = height * scale;
            w = h * aspect;
        }
        int cw = (int)w & ~1, ch = (int)h & ~1;
        return (((width - cw) / 2) & ~1, ((height - ch) / 2) & ~1, cw, ch);
    }

    [JsonIgnore]
    public string? FrameLabel => FrameAspect is double a
        ? a.ToString("0.00", CultureInfo.InvariantCulture)
          + (FrameScale < 1 ? " · " + (FrameScale * 100).ToString("0", CultureInfo.InvariantCulture) + " %" : "")
        : null;
}
```

- [ ] **Step 5: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~MediaTests"`
Expected: PASS (12 testů).

- [ ] **Step 6: Commit**

```powershell
git add windows
git commit -m "Windows: engine and media data types" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: OffloadReport (JSON, souhrn, hash list)

**Files:**
- Create: `windows/src/Offload.Core/OffloadReport.cs`
- Modify: `windows/tests/Offload.Core.Tests/TestFs.cs` (přidat `Reports.Sample`)
- Test: `windows/tests/Offload.Core.Tests/ReportTests.cs`

**Interfaces:**
- Consumes: `OffloadPlan`, `DestFileResult`, `FileState.Wire()`, `Format`, `NativeIO.FilesystemType`, `ClipEntry`, `ProxySettings`.
- Produces: `ReportDestination` (`Root`, `CardPath`, `Filesystem`, `FilesOK`, `FilesFailed`, `bool? Unbuffered`), `ReportFile` (`Path`, `Size`, `Modified`, `Xxh64`, `SourceError`, `Destinations`, `Errors`), `OffloadReport` (vlastnosti viz kód; `CopySeconds`, `VerifySeconds`, `FailedFiles`, `Success`, `SafeToEject`, `HashList`, `Summary`, `ToJson()`, `static FromJson(string)`, `static Build(OffloadPlan, IReadOnlyList<ulong?>, IReadOnlyList<string?>, IReadOnlyList<DestFileResult[]>, IReadOnlyList<bool> unbuffered, bool verified, DateTime started, DateTime copyFinished, DateTime finished)`). `Write(string cardPath, bool includeHashRecords)` přidá Task 9.

- [ ] **Step 1: Přidat testovací report do `TestFs.cs`**

Na konec `windows/tests/Offload.Core.Tests/TestFs.cs` přidat:

```csharp
public static class Reports
{
    /// Two-file report for one destination at `cardPath`; `destError` makes the second file fail there.
    public static OffloadReport Sample(string cardPath, bool verified = true, string? destError = null, bool unbuffered = true)
    {
        var t0 = new DateTime(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc);
        var ok = verified ? "verified" : "written";
        return new OffloadReport
        {
            CardName = "A001",
            Date = "2026-10-04",
            Source = @"E:\",
            SourceFilesystem = "exFAT",
            Started = t0,
            CopyFinished = t0.AddSeconds(10),
            Finished = t0.AddSeconds(15),
            Verified = verified,
            TotalFiles = 2,
            TotalBytes = 3000,
            Destinations =
            [
                new ReportDestination
                {
                    Root = Path.GetDirectoryName(Path.GetDirectoryName(cardPath))!, CardPath = cardPath, Filesystem = "NTFS",
                    FilesOK = destError == null ? 2 : 1, FilesFailed = destError == null ? 0 : 1,
                    Unbuffered = unbuffered ? null : false,
                },
            ],
            Files =
            [
                new ReportFile { Path = "CLIP/A&B.mov", Size = 1000, Modified = t0, Xxh64 = "0123456789abcdef", Destinations = [ok], Errors = [null] },
                new ReportFile { Path = "CLIP/c 02.mov", Size = 2000, Modified = t0, Xxh64 = "fedcba9876543210",
                                 Destinations = [destError == null ? ok : "failed"], Errors = [destError] },
            ],
            Skipped = [".DS_Store"],
        };
    }
}
```

- [ ] **Step 2: Napsat padající testy**

`windows/tests/Offload.Core.Tests/ReportTests.cs`:

```csharp
using System.Text.Json;

namespace Offload.Core.Tests;

public class ReportTests
{
    const string Card = @"D:\dst\2026-10-04\A001";

    [Fact]
    public void Json_TopLevelKeysAreTheMacKeysSorted()
    {
        using var doc = JsonDocument.Parse(Reports.Sample(Card).ToJson());
        string[] expected =
        [
            "cardName", "clips", "copyFinished", "date", "destinations", "files", "finished", "hashAlgorithm",
            "skipped", "source", "sourceFilesystem", "started", "tool", "totalBytes", "totalFiles", "verified", "version",
        ];
        Assert.Equal(expected, doc.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Json_NestedShapes()
    {
        using var doc = JsonDocument.Parse(Reports.Sample(Card, destError: "boom", unbuffered: false).ToJson());
        var root = doc.RootElement;
        Assert.Equal("2026-10-04T08:00:00Z", root.GetProperty("started").GetString());
        Assert.Equal("offload", root.GetProperty("tool").GetString());
        Assert.Equal("0.1.0-win", root.GetProperty("version").GetString());
        Assert.Equal(0, root.GetProperty("clips").GetArrayLength());
        Assert.False(root.TryGetProperty("proxy", out _));

        var dest = root.GetProperty("destinations")[0];
        Assert.Equal(["cardPath", "filesFailed", "filesOK", "filesystem", "root", "unbuffered"], dest.EnumerateObject().Select(p => p.Name));
        Assert.False(dest.GetProperty("unbuffered").GetBoolean());

        var f0 = root.GetProperty("files")[0];
        Assert.Equal(["destinations", "errors", "modified", "path", "size", "xxh64"], f0.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, f0.GetProperty("errors")[0].ValueKind);
        Assert.Equal("CLIP/A&B.mov", f0.GetProperty("path").GetString());
        Assert.Equal("boom", root.GetProperty("files")[1].GetProperty("errors")[0].GetString());
    }

    [Fact]
    public void Json_UnbufferedOmittedWhenNormal()
    {
        using var doc = JsonDocument.Parse(Reports.Sample(Card).ToJson());
        Assert.False(doc.RootElement.GetProperty("destinations")[0].TryGetProperty("unbuffered", out _));
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var back = OffloadReport.FromJson(Reports.Sample(Card).ToJson());
        Assert.Equal("A001", back.CardName);
        Assert.Equal(2, back.Files.Count);
        Assert.Equal(new DateTime(2026, 10, 4, 8, 0, 10, DateTimeKind.Utc), back.CopyFinished);
    }

    [Fact]
    public void SuccessAndEject()
    {
        Assert.True(Reports.Sample(Card).SafeToEject);
        var notVerified = Reports.Sample(Card, verified: false);
        Assert.True(notVerified.Success);
        Assert.False(notVerified.SafeToEject);
        var failed = Reports.Sample(Card, destError: "boom");
        Assert.False(failed.Success);
        Assert.Equal(["CLIP/c 02.mov"], failed.FailedFiles.Select(f => f.Path));
    }

    [Fact]
    public void HashList_IsHashTwoSpacesPath()
    {
        Assert.Equal("0123456789abcdef  CLIP/A&B.mov\nfedcba9876543210  CLIP/c 02.mov\n", Reports.Sample(Card).HashList);
    }

    [Fact]
    public void Summary_ShowsStatusTimesAndFailures()
    {
        var ok = Reports.Sample(Card).Summary;
        Assert.StartsWith("OFFLOAD REPORT — OK\nCard:        A001\nSource:      E:\\ [exFAT]\n", ok);
        Assert.Contains("Copy:        10.0 s  0 MB/s\n", ok);
        Assert.Contains($"Destination: {Card} [NTFS]  ok 2  failed 0\n", ok);
        Assert.Contains("Skipped (system files): .DS_Store", ok);

        Assert.Contains("NOT VERIFIED", Reports.Sample(Card, verified: false).Summary);

        var bad = Reports.Sample(Card, destError: "boom", unbuffered: false).Summary;
        Assert.StartsWith("OFFLOAD REPORT — FAILED", bad);
        Assert.Contains("(buffered I/O)", bad);
        Assert.Contains("FAILED FILES (1):\n  CLIP/c 02.mov\n    dest 1: boom\n", bad);
    }

    [Fact]
    public void Build_TakesStatesFromWriterResults()
    {
        var plan = new OffloadPlan
        {
            Source = @"C:\card", Date = "2026-10-04", CardName = "A001", DestRoots = [@"C:\d1", @"C:\d2"],
            Scan = new SourceScan
            {
                Root = @"C:\card",
                Files = [new SourceFile("a", 1, DateTime.UtcNow), new SourceFile("b", 2, DateTime.UtcNow)],
                Dirs = [], Skipped = [],
            },
        };
        var r = OffloadReport.Build(plan, [1UL, null], [null, "read failed"],
            [
                [new DestFileResult(FileState.Verified, null), new DestFileResult(FileState.Failed, "source: read failed")],
                [new DestFileResult(FileState.Verified, null), new DestFileResult(FileState.Failed, "source: read failed")],
            ],
            [true, false], verified: true, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);
        Assert.Equal("0000000000000001", r.Files[0].Xxh64);
        Assert.Null(r.Files[1].Xxh64);
        Assert.Equal(["verified", "verified"], r.Files[0].Destinations);
        Assert.Equal(1, r.Destinations[1].FilesOK);
        Assert.Equal(1, r.Destinations[1].FilesFailed);
        Assert.Null(r.Destinations[0].Unbuffered);
        Assert.False(r.Destinations[1].Unbuffered);
        Assert.Equal(3, r.TotalBytes);
        Assert.False(r.Success);
    }
}
```

- [ ] **Step 3: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~ReportTests"`
Expected: build FAIL — `OffloadReport`, `ReportDestination`, `ReportFile` neexistují.

- [ ] **Step 4: Implementace**

`windows/src/Offload.Core/OffloadReport.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Offload.Core;

public sealed class ReportDestination
{
    [JsonPropertyName("root")] public required string Root { get; init; }
    [JsonPropertyName("cardPath")] public required string CardPath { get; init; }
    [JsonPropertyName("filesystem")] public required string Filesystem { get; init; }
    [JsonPropertyName("filesOK")] public int FilesOK { get; init; }
    [JsonPropertyName("filesFailed")] public int FilesFailed { get; init; }
    /// Present (false) only when the volume refused unbuffered writes and the file cache was used.
    [JsonPropertyName("unbuffered")] public bool? Unbuffered { get; init; }
}

public sealed class ReportFile
{
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("modified")] public DateTime Modified { get; init; }
    [JsonPropertyName("xxh64")] public string? Xxh64 { get; init; }
    [JsonPropertyName("sourceError")] public string? SourceError { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<string> Destinations { get; init; }
    [JsonPropertyName("errors")] public required IReadOnlyList<string?> Errors { get; init; }
}

public sealed partial class OffloadReport
{
    [JsonPropertyName("tool")] public string Tool { get; init; } = "offload";
    [JsonPropertyName("version")] public string Version { get; init; } = OffloadVersion.String;
    [JsonPropertyName("hashAlgorithm")] public string HashAlgorithm { get; init; } = "xxh64";
    [JsonPropertyName("cardName")] public required string CardName { get; init; }
    [JsonPropertyName("date")] public required string Date { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("sourceFilesystem")] public required string SourceFilesystem { get; init; }
    [JsonPropertyName("started")] public DateTime Started { get; init; }
    [JsonPropertyName("copyFinished")] public DateTime CopyFinished { get; init; }
    [JsonPropertyName("finished")] public DateTime Finished { get; init; }
    [JsonPropertyName("verified")] public bool Verified { get; init; }
    [JsonPropertyName("totalFiles")] public int TotalFiles { get; init; }
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<ReportDestination> Destinations { get; init; }
    [JsonPropertyName("files")] public required IReadOnlyList<ReportFile> Files { get; init; }
    [JsonPropertyName("skipped")] public required IReadOnlyList<string> Skipped { get; init; }
    [JsonPropertyName("clips")] public List<ClipEntry> Clips { get; set; } = [];
    [JsonPropertyName("proxy")] public ProxySettings? Proxy { get; set; }

    [JsonIgnore] public double CopySeconds => (CopyFinished - Started).TotalSeconds;
    [JsonIgnore] public double VerifySeconds => (Finished - CopyFinished).TotalSeconds;

    [JsonIgnore]
    public IReadOnlyList<ReportFile> FailedFiles
    {
        get
        {
            var ok = Verified ? "verified" : "written";
            return Files.Where(f => f.SourceError != null || f.Destinations.Any(d => d != ok)).ToList();
        }
    }

    [JsonIgnore] public bool Success => FailedFiles.Count == 0;
    /// Every file is on every destination and was read back with a matching hash.
    [JsonIgnore] public bool SafeToEject => Success && Verified;

    public static OffloadReport Build(OffloadPlan plan, IReadOnlyList<ulong?> hashes, IReadOnlyList<string?> sourceErrors,
        IReadOnlyList<DestFileResult[]> destResults, IReadOnlyList<bool> unbuffered, bool verified,
        DateTime started, DateTime copyFinished, DateTime finished)
    {
        var okState = verified ? FileState.Verified : FileState.Written;
        var files = plan.Scan.Files.Select((f, i) => new ReportFile
        {
            Path = f.RelPath,
            Size = f.Size,
            Modified = f.ModifiedUtc,
            Xxh64 = hashes[i] is ulong h ? Format.Hex64(h) : null,
            SourceError = sourceErrors[i],
            Destinations = destResults.Select(r => r[i].State.Wire()).ToList(),
            Errors = destResults.Select(r => r[i].Error).ToList(),
        }).ToList();
        var cardPaths = plan.CardPaths;
        var dests = plan.DestRoots.Select((root, d) => new ReportDestination
        {
            Root = root,
            CardPath = cardPaths[d],
            Filesystem = NativeIO.FilesystemType(root),
            FilesOK = destResults[d].Count(r => r.State == okState),
            FilesFailed = destResults[d].Count(r => r.State != okState),
            Unbuffered = unbuffered[d] ? null : false,
        }).ToList();
        return new OffloadReport
        {
            CardName = plan.CardName,
            Date = plan.Date,
            Source = plan.Source,
            SourceFilesystem = NativeIO.FilesystemType(plan.Source),
            Started = started,
            CopyFinished = copyFinished,
            Finished = finished,
            Verified = verified,
            TotalFiles = files.Count,
            TotalBytes = plan.Scan.TotalBytes,
            Destinations = dests,
            Files = files,
            Skipped = plan.Scan.Skipped,
        };
    }

    /// Plain hash list, one "<xxh64>  <path>" per line — readable by humans and by `offload verify`.
    [JsonIgnore]
    public string HashList => string.Join("\n", Files.Where(f => f.Xxh64 != null).Select(f => $"{f.Xxh64}  {f.Path}")) + "\n";

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var inv = CultureInfo.InvariantCulture;
            var s = new StringBuilder();
            s.Append($"OFFLOAD REPORT — {(Success ? "OK" : "FAILED")}\n");
            s.Append($"Card:        {CardName}\n");
            s.Append($"Source:      {Source} [{SourceFilesystem}]\n");
            s.Append($"Started:     {LocalIso(Started)}\n");
            s.Append($"Finished:    {LocalIso(Finished)}\n");
            s.Append($"Files:       {TotalFiles}  ({Format.Bytes(TotalBytes)})\n");
            s.Append($"Copy:        {CopySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, CopySeconds)}\n");
            s.Append("Verify:      " + (Verified ? $"{VerifySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, VerifySeconds)}" : "NOT VERIFIED") + "\n");
            s.Append($"Hash:        {HashAlgorithm}\n");
            foreach (var d in Destinations)
                s.Append($"Destination: {d.CardPath} [{d.Filesystem}]  ok {d.FilesOK}  failed {d.FilesFailed}{(d.Unbuffered == false ? "  (buffered I/O)" : "")}\n");
            if (Skipped.Count > 0) s.Append($"\nSkipped (system files): {string.Join(", ", Skipped)}\n");
            var failed = FailedFiles;
            if (failed.Count > 0)
            {
                s.Append($"\nFAILED FILES ({failed.Count}):\n");
                foreach (var f in failed)
                {
                    s.Append($"  {f.Path}\n");
                    if (f.SourceError != null) s.Append($"    source: {f.SourceError}\n");
                    for (int d = 0; d < f.Errors.Count; d++)
                        if (f.Errors[d] is string e) s.Append($"    dest {d + 1}: {e}\n");
                }
            }
            return s.ToString();
        }
    }

    static string LocalIso(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    static readonly JsonSerializerOptions SerializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new IsoSecondsDateConverter() },
    };

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// Pretty JSON with sorted keys and second-precision UTC dates — the shape the Mac version writes.
    public string ToJson() => Sorted(JsonSerializer.SerializeToNode(this, SerializeOptions))!.ToJsonString(WriteOptions);

    public static OffloadReport FromJson(string json) => JsonSerializer.Deserialize<OffloadReport>(json, SerializeOptions)!;

    static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sorted).ToArray()),
        _ => node?.DeepClone(),
    };

    sealed class IsoSecondsDateConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }
}
```

Poznámka: `partial` je kvůli Task 9, který doplní `Write` v tomtéž souboru — ponechat.

- [ ] **Step 5: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~ReportTests"`
Expected: PASS (8 testů).

- [ ] **Step 6: Commit**

```powershell
git add windows
git commit -m "Windows: offload report JSON, summary and hash list" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: ASC MHL v2

**Files:**
- Create: `windows/src/Offload.Core/Mhl.cs`
- Test: `windows/tests/Offload.Core.Tests/MhlTests.cs`

**Interfaces:**
- Consumes: `OffloadReport`, `NativeIO.Mkdirs`, `NativeIO.WriteFileDurably`, `Layout.ReportDirName`, `Layout.ProxyDirName`.
- Produces: `Mhl.DirName` (`"ascmhl"`), `Mhl.Write(OffloadReport, string cardPath)`, internal `Mhl.Write(OffloadReport, string cardPath, DateTime now, string host)`, internal `Mhl.C4(ReadOnlySpan<byte>) -> string`, internal `Mhl.Esc(string) -> string`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/MhlTests.cs`:

```csharp
using System.Text;
using System.Xml.Linq;

namespace Offload.Core.Tests;

public class MhlTests
{
    static readonly XNamespace Ns = "urn:ASC:MHL:v2.0";

    [Fact]
    public void C4_MatchesReferenceIds()
    {
        Assert.Equal("c459dsjfscH38cYeXXYogktxf4Cd9ibshE3BHUo6a58hBXmRQdZrAkZzsWcbWtDg5oQstpDuni4Hirj75GEmTc1sFT", Mhl.C4([]));
        Assert.Equal("c45S4rnaTNWonxss1u8LzsaJdEph1AJhWUF4sh2waXKMsutyfAxg4ybUeuXVWS9HdNcEypmeXn8FZGonD4w1rj9DZp", Mhl.C4("abc"u8));
    }

    [Fact]
    public void Esc_EscapesXmlSpecials() => Assert.Equal("a&amp;b&lt;c&gt;&quot;", Mhl.Esc("a&b<c>\""));

    [Fact]
    public void Write_CreatesHashListAndChain()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        var report = Reports.Sample(card);
        var now = new DateTime(2026, 10, 4, 10, 30, 0, DateTimeKind.Local);

        Mhl.Write(report, card, now, "PC&1");

        var mhlDir = Path.Combine(card, "ascmhl");
        var mhlFile = Assert.Single(Directory.GetFiles(mhlDir, "*.mhl"));
        Assert.Matches(@"\\0001_A001_\d{4}-\d\d-\d\d_\d{6}Z\.mhl$", mhlFile);
        var text = File.ReadAllText(mhlFile, Encoding.UTF8);
        Assert.Contains("<hostname>PC&amp;1</hostname>", text);
        Assert.Contains(">CLIP/A&amp;B.mov</path>", text);
        Assert.DoesNotContain("\r", text);

        var doc = XDocument.Parse(text);
        var hashes = doc.Descendants(Ns + "hash").ToList();
        Assert.Equal(2, hashes.Count);
        Assert.All(hashes, h => Assert.Equal("original", h.Element(Ns + "xxh64")!.Attribute("action")!.Value));
        Assert.Contains(doc.Descendants(Ns + "pattern"), p => p.Value == "_REPORT");

        var chain = XDocument.Load(Path.Combine(mhlDir, "ascmhl_chain.xml"));
        XNamespace dirNs = "urn:ASC:MHL:DIRECTORY:v2.0";
        Assert.Equal(Path.GetFileName(mhlFile), chain.Descendants(dirNs + "path").Single().Value);
        Assert.Equal(Mhl.C4(File.ReadAllBytes(mhlFile)), chain.Descendants(dirNs + "c4").Single().Value);
    }

    [Fact]
    public void Write_MarksFailedFiles()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        Mhl.Write(Reports.Sample(card, destError: "boom"), card, DateTime.Now, "pc");
        var text = File.ReadAllText(Directory.GetFiles(Path.Combine(card, "ascmhl"), "*.mhl").Single());
        Assert.Contains("action=\"failed\"", text);
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~MhlTests"`
Expected: build FAIL — `Mhl` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Mhl.cs`:

```csharp
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Offload.Core;

/// ASC MHL v2 (urn:ASC:MHL:v2.0) — the industry-standard hash record, written as the first generation
/// into `<card>/ascmhl/` so `ascmhl verify` and other DIT tools can check the copy later.
/// Optional directory/root hashes are omitted; per-file xxh64 hashes are what verification relies on.
public static class Mhl
{
    public const string DirName = "ascmhl";
    const string ChainName = "ascmhl_chain.xml";
    const string Base58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// Our own folders inside the card folder are not camera data.
    static readonly string[] IgnorePatterns = [".DS_Store", "ascmhl", "ascmhl/", Layout.ReportDirName, Layout.ProxyDirName];

    public static void Write(OffloadReport report, string cardPath) => Write(report, cardPath, DateTime.Now, Environment.MachineName);

    internal static void Write(OffloadReport report, string cardPath, DateTime now, string host)
    {
        int d = report.Destinations.Select(x => x.CardPath).ToList().IndexOf(cardPath);
        if (d < 0) return;
        var dir = Path.Combine(cardPath, DirName);
        NativeIO.Mkdirs(dir);

        var stamp = IsoDate(now);
        var folder = Path.GetFileName(cardPath.TrimEnd('\\'));
        var fileName = $"0001_{folder}_{now.ToUniversalTime().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}Z.mhl";

        var x = new StringBuilder();
        void Line(string s) => x.Append(s).Append('\n');
        Line("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        Line("<hashlist version=\"2.0\" xmlns=\"urn:ASC:MHL:v2.0\">");
        Line("  <creatorinfo>");
        Line($"    <creationdate>{stamp}</creationdate>");
        Line($"    <hostname>{Esc(host)}</hostname>");
        Line($"    <tool version=\"{Esc(report.Version)}\">{Esc(report.Tool)}</tool>");
        Line($"    <comment>Offload of {Esc(report.CardName)} from {Esc(report.Source)}</comment>");
        Line("  </creatorinfo>");
        Line("  <processinfo>");
        Line("    <process>transfer</process>");
        Line("    <ignore>");
        foreach (var p in IgnorePatterns.Concat(report.Skipped.Select(s => s.Split('/')[^1])))
            Line($"      <pattern>{Esc(p)}</pattern>");
        Line("    </ignore>");
        Line("  </processinfo>");
        Line("  <hashes>");
        foreach (var f in report.Files)
        {
            if (f.Xxh64 == null) continue;
            var state = f.Destinations[d];
            var action = state is "verified" or "written" ? "original" : "failed";
            Line("    <hash>");
            Line($"      <path size=\"{f.Size}\" lastmodificationdate=\"{IsoDate(f.Modified)}\">{Esc(f.Path)}</path>");
            Line($"      <xxh64 action=\"{action}\" hashdate=\"{stamp}\">{f.Xxh64}</xxh64>");
            Line("    </hash>");
        }
        Line("  </hashes>");
        Line("</hashlist>");

        var data = Encoding.UTF8.GetBytes(x.ToString());
        NativeIO.WriteFileDurably(Path.Combine(dir, fileName), data);

        var chain = new StringBuilder();
        chain.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        chain.Append("<ascmhldirectory xmlns=\"urn:ASC:MHL:DIRECTORY:v2.0\">\n");
        chain.Append("  <hashlist sequencenr=\"1\">\n");
        chain.Append($"    <path>{Esc(fileName)}</path>\n");
        chain.Append($"    <c4>{C4(data)}</c4>\n");
        chain.Append("  </hashlist>\n");
        chain.Append("</ascmhldirectory>\n");
        NativeIO.WriteFileDurably(Path.Combine(dir, ChainName), Encoding.UTF8.GetBytes(chain.ToString()));
    }

    static string IsoDate(DateTime d) => d.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    internal static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// C4 ID: "c4" + base58(SHA-512), left-padded with "1" to 88 characters.
    internal static string C4(ReadOnlySpan<byte> data)
    {
        var n = new BigInteger(SHA512.HashData(data), isUnsigned: true, isBigEndian: true);
        var digits = new StringBuilder();
        while (n > 0)
        {
            n = BigInteger.DivRem(n, 58, out var rem);
            digits.Insert(0, Base58[(int)rem]);
        }
        return "c4" + new string('1', Math.Max(0, 88 - digits.Length)) + digits;
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~MhlTests"`
Expected: PASS (4 testy).

- [ ] **Step 5: Commit**

```powershell
git add windows
git commit -m "Windows: ASC MHL v2 hash list with C4 chain" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: HTML report + zápis reportu

**Files:**
- Create: `windows/src/Offload.Core/HtmlReport.cs`
- Modify: `windows/src/Offload.Core/OffloadReport.cs` (přidat metodu `Write`)
- Test: `windows/tests/Offload.Core.Tests/HtmlReportTests.cs`

**Interfaces:**
- Consumes: `OffloadReport`, `ClipEntry`, `Format`, `Layout`, `Mhl.Write`, `NativeIO.WriteFileDurably`.
- Produces: `HtmlReport.Render(OffloadReport, bool forPrint = false) -> string`; `OffloadReport.Write(string cardPath, bool includeHashRecords)` → `_REPORT/offload_report.json`, `offload_report.txt`, `<karta>_report.html` a s `includeHashRecords` také `<karta>.xxh64` + `ascmhl/`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/HtmlReportTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class HtmlReportTests
{
    const string Card = @"D:\dst\2026-10-04\A001";

    [Fact]
    public void Verified_ShowsStatusFilesAndEscapes()
    {
        var html = HtmlReport.Render(Reports.Sample(Card));
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<span class=\"badge ok\">OVĚŘENO</span>", html);
        Assert.Contains("CLIP/A&amp;B.mov", html);
        Assert.DoesNotContain("CLIP/A&B.mov", html);
        Assert.Contains("<td class=\"ok\">ověřeno</td>", html);
        Assert.Contains("<details><summary>Všechny soubory (2)</summary>", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.Contains("offload 0.1.0-win", html);
    }

    [Fact]
    public void Failed_ListsErrorsPerDestination()
    {
        var html = HtmlReport.Render(Reports.Sample(Card, destError: "boom"));
        Assert.Contains("<span class=\"badge bad\">CHYBA</span>", html);
        Assert.Contains("Chyby (1)", html);
        Assert.Contains("cíl 1: boom", html);
    }

    [Fact]
    public void NotVerified_IsWarning() =>
        Assert.Contains("ZKOPÍROVÁNO — NEOVĚŘENO", HtmlReport.Render(Reports.Sample(Card, verified: false)));

    [Fact]
    public void Buffered_ShowsWarning() =>
        Assert.Contains("svazek nepodporuje přímý zápis", HtmlReport.Render(Reports.Sample(Card, unbuffered: false)));

    [Fact]
    public void Print_ExpandsFileListAndUsesLightColours()
    {
        var html = HtmlReport.Render(Reports.Sample(Card), forPrint: true);
        Assert.Contains("<details open>", html);
        Assert.DoesNotContain("prefers-color-scheme:dark", html);
    }

    [Fact]
    public void Clips_RenderRowWithThumbnailAndProxyStatus()
    {
        var r = Reports.Sample(Card);
        r.Clips.Add(new ClipEntry
        {
            Clip = new Clip { Name = "A001C001", Path = "CLIP/A&B.mov", Format = ClipFormat.Video, Segments = ["CLIP/A&B.mov"], Bytes = 1000 },
            Info = new ClipInfo { Codec = "prores", Width = 3840, Height = 2160, FrameRate = "25/1", Duration = 12, AudioChannels = 2, Timecode = "01:00:00:00" },
            ProxyStatus = ProxyStatus.Done,
            Thumbnail = [1, 2, 3],
        });
        r.Proxy = new ProxySettings { Enabled = true, FrameAspect = 1.66 };
        var html = HtmlReport.Render(r);
        Assert.Contains("<h2>Klipy</h2>", html);
        Assert.Contains("src=\"data:image/jpeg;base64,AQID\"", html);
        Assert.Contains("3840×2160", html);
        Assert.Contains("<td class=\"ok\">hotovo</td>", html);
        Assert.Contains("2 ch", html);
        Assert.Contains("Proxy: H.264, 1080p, výřez 1.66, LUT: bez LUT", html);
    }

    [Fact]
    public void Write_CreatesAllReportFiles()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        var report = Reports.Sample(card);

        report.Write(card, includeHashRecords: true);

        var rep = Path.Combine(card, "_REPORT");
        Assert.True(File.Exists(Path.Combine(rep, "offload_report.json")));
        Assert.True(File.Exists(Path.Combine(rep, "offload_report.txt")));
        Assert.True(File.Exists(Path.Combine(rep, "A001_report.html")));
        Assert.Equal(report.HashList, File.ReadAllText(Path.Combine(rep, "A001.xxh64")));
        Assert.Single(Directory.GetFiles(Path.Combine(card, "ascmhl"), "*.mhl"));
        Assert.Empty(Directory.GetFiles(rep, "*.tmp"));
        var json = File.ReadAllBytes(Path.Combine(rep, "offload_report.json"));
        Assert.NotEqual(0xEF, json[0]);   // no UTF-8 BOM
    }

    [Fact]
    public void Write_WithoutHashRecords_SkipsListAndMhl()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        Reports.Sample(card).Write(card, includeHashRecords: false);
        Assert.False(File.Exists(Path.Combine(card, "_REPORT", "A001.xxh64")));
        Assert.False(Directory.Exists(Path.Combine(card, "ascmhl")));
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~HtmlReportTests"`
Expected: build FAIL — `HtmlReport` a `OffloadReport.Write` neexistují.

- [ ] **Step 3: HTML report**

`windows/src/Offload.Core/HtmlReport.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Offload.Core;

/// Self-contained HTML report (thumbnails embedded) — can be emailed or opened straight from the disk.
public static class HtmlReport
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly Dictionary<string, string> StateLabel = new()
    {
        ["verified"] = "ověřeno", ["written"] = "zapsáno", ["failed"] = "CHYBA", ["pending"] = "nezpracováno",
    };

    const string RootCss = ":root{--bg:#f6f6f4;--card:#fff;--fg:#1d1d1f;--muted:#6e6e73;--line:#e3e3e0;--ok:#1f8a4c;--okbg:#e5f4ea;--bad:#c62828;--badbg:#fdeaea;--warn:#a86400;--warnbg:#fdf2dc;--accent:#3a5bd9}";
    const string DarkCss = "@media (prefers-color-scheme:dark){:root{--bg:#141416;--card:#1e1e21;--fg:#ececee;--muted:#9a9aa0;--line:#2e2e33;--ok:#4cc27c;--okbg:#14301f;--bad:#ff6b6b;--badbg:#3a1717;--warn:#f0b04a;--warnbg:#382a10;--accent:#8aa2ff}}";
    /// For the PDF: light colours only and the full file list expanded.
    const string PrintCss = "body{background:#fff;font-size:11px}main{padding:0;max-width:none}th,td{padding:5px 7px}tr{break-inside:avoid}h2{break-after:avoid}.stat b{font-size:17px}.thumb,.nothumb{width:112px;height:63px}";
    const string BaseCss =
        "*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 \"Segoe UI\",-apple-system,BlinkMacSystemFont,\"Helvetica Neue\",sans-serif}\n" +
        "main{max-width:1200px;margin:0 auto;padding:28px 20px 60px}\n" +
        "header{display:flex;flex-wrap:wrap;align-items:center;gap:14px;margin-bottom:22px}\n" +
        "h1{font-size:26px;margin:0;letter-spacing:-.01em}h2{font-size:16px;margin:30px 0 10px}\n" +
        ".sub{color:var(--muted)}.badge{display:inline-block;padding:4px 12px;border-radius:999px;font-weight:600;font-size:13px}\n" +
        ".ok{color:var(--ok)}.bad{color:var(--bad)}.warn{color:var(--warn)}.muted{color:var(--muted)}\n" +
        ".badge.ok{background:var(--okbg)}.badge.bad{background:var(--badbg)}.badge.warn{background:var(--warnbg)}\n" +
        ".stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px}\n" +
        ".stat{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 14px}\n" +
        ".stat b{display:block;font-size:20px;font-variant-numeric:tabular-nums}.stat span{color:var(--muted);font-size:12px}\n" +
        ".panel{background:var(--card);border:1px solid var(--line);border-radius:10px;overflow-x:auto}\n" +
        "table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:middle;white-space:nowrap}\n" +
        "th{font-size:12px;color:var(--muted);font-weight:600}tr:last-child td{border-bottom:0}\n" +
        "td.num{text-align:right;font-variant-numeric:tabular-nums}code,.mono{font:12px ui-monospace,Consolas,Menlo,monospace}\n" +
        "td.clip{white-space:normal;min-width:180px;max-width:300px}.wrap{white-space:normal}\n" +
        ".thumb{width:128px;height:72px;object-fit:contain;background:#000;border-radius:4px;display:block}\n" +
        ".nothumb{width:128px;height:72px;border-radius:4px;background:var(--line)}\n" +
        ".clipname{font-weight:600}.path{color:var(--muted);font-size:12px}\n" +
        "details summary{cursor:pointer;padding:10px 0;font-weight:600}\n" +
        ".err{white-space:normal;color:var(--bad)}\n" +
        "footer{margin-top:40px;color:var(--muted);font-size:12px}\n" +
        "@media print{body{background:#fff}.panel,.stat{border-color:#ccc}details{display:block}}\n";

    internal static string Esc(string s)
    {
        var o = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': o.Append("&amp;"); break;
                case '<': o.Append("&lt;"); break;
                case '>': o.Append("&gt;"); break;
                case '"': o.Append("&quot;"); break;
                default: o.Append(c); break;
            }
        }
        return o.ToString();
    }

    static string DateTimeText(DateTime d) => d.ToLocalTime().ToString("d. M. yyyy HH:mm:ss", Cs);
    static string TimeText(DateTime d) => d.ToLocalTime().ToString("HH:mm:ss", Inv);

    internal static string Duration(double seconds)
    {
        int t = (int)Math.Round(seconds, MidpointRounding.AwayFromZero);
        return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
    }

    static (string Class, string Label) ProxyLabel(ClipEntry c) => c.ProxyStatus switch
    {
        ProxyStatus.Done => ("ok", "hotovo"),
        ProxyStatus.Failed => ("bad", "chyba"),
        ProxyStatus.Unsupported => ("warn", "nepodporováno"),
        ProxyStatus.Skipped => ("muted", "přeskočeno"),
        _ => ("muted", "—"),
    };

    /// Disk column: last folder of the root, or the drive itself ("E:\").
    static string DiskName(string root)
    {
        var name = Path.GetFileName(root.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : root;
    }

    public static string Render(OffloadReport r, bool forPrint = false)
    {
        bool ok = r.Success;
        string status = !ok ? "CHYBA" : (r.Verified ? "OVĚŘENO" : "ZKOPÍROVÁNO — NEOVĚŘENO");
        string statusClass = !ok ? "bad" : (r.Verified ? "ok" : "warn");
        var failed = r.FailedFiles;
        var videoClips = r.Clips.Where(c => !c.Clip.IsCameraProxy).ToList();
        double totalDuration = videoClips.Sum(c => c.Info?.Duration ?? 0);

        var h = new StringBuilder();
        h.Append("<!doctype html>\n<html lang=\"cs\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Offload {Esc(r.CardName)} – {Esc(r.Date)}</title>\n<style>\n");
        h.Append(RootCss).Append('\n').Append(forPrint ? PrintCss : DarkCss).Append('\n').Append(BaseCss);
        h.Append("</style></head><body><main>\n");
        h.Append($"<header><h1>{Esc(r.CardName)}</h1><span class=\"badge {statusClass}\">{status}</span>\n");
        h.Append($"<span class=\"sub\">{DateTimeText(r.Started)} – {TimeText(r.Finished)}</span></header>\n");
        h.Append("<div class=\"stats\">\n");
        Stat(h, r.TotalFiles.ToString(Inv), "souborů");
        Stat(h, Format.Bytes(r.TotalBytes), "data");
        Stat(h, videoClips.Count.ToString(Inv), "klipů" + (totalDuration > 0 ? " · " + Duration(totalDuration) : ""));
        Stat(h, Duration(r.CopySeconds), "kopírování · " + Format.Rate(r.TotalBytes, r.CopySeconds));
        Stat(h, r.Verified ? Duration(r.VerifySeconds) : "—", "ověření · " + (r.Verified ? Format.Rate(r.TotalBytes, r.VerifySeconds) : "neprovedeno"));
        Stat(h, Esc(r.HashAlgorithm.ToUpperInvariant()), "kontrolní součet");
        h.Append("</div>\n\n");

        h.Append("<h2>Cíle</h2><div class=\"panel\"><table>\n");
        h.Append("<tr><th>Disk</th><th>Složka</th><th>FS</th><th class=\"num\">OK</th><th class=\"num\">Chyby</th></tr>\n");
        foreach (var d in r.Destinations)
        {
            h.Append($"<tr><td>{Esc(DiskName(d.Root))}</td><td class=\"mono wrap\">{Esc(d.CardPath)}</td><td>{Esc(d.Filesystem)}</td>");
            h.Append($"<td class=\"num ok\">{d.FilesOK}</td><td class=\"num {(d.FilesFailed > 0 ? "bad" : "muted")}\">{d.FilesFailed}</td></tr>\n");
        }
        h.Append("</table></div>\n");
        for (int i = 0; i < r.Destinations.Count; i++)
        {
            if (r.Destinations[i].Unbuffered == false)
                h.Append($"<p class=\"warn\">Cíl {i + 1}: svazek nepodporuje přímý zápis, použita systémová cache.</p>\n");
        }
        h.Append($"<p class=\"sub\">Zdroj: <span class=\"mono\">{Esc(r.Source)}</span> ({Esc(r.SourceFilesystem)})</p>\n");

        if (failed.Count > 0)
        {
            h.Append($"<h2 class=\"bad\">Chyby ({failed.Count})</h2><div class=\"panel\"><table><tr><th>Soubor</th><th>Problém</th></tr>\n");
            foreach (var f in failed)
            {
                var msgs = new List<string>();
                if (f.SourceError != null) msgs.Add("karta: " + f.SourceError);
                for (int i = 0; i < f.Errors.Count; i++)
                    if (f.Errors[i] is string e) msgs.Add($"cíl {i + 1}: {e}");
                h.Append($"<tr><td class=\"mono\">{Esc(f.Path)}</td><td class=\"err\">{Esc(string.Join(" · ", msgs))}</td></tr>\n");
            }
            h.Append("</table></div>\n");
        }

        if (r.Clips.Count > 0) AppendClips(h, r);

        h.Append($"<details{(forPrint ? " open" : "")}><summary>Všechny soubory ({r.Files.Count})</summary><div class=\"panel\"><table>\n");
        h.Append($"<tr><th>Soubor</th><th class=\"num\">Velikost</th><th>{Esc(r.HashAlgorithm)}</th>");
        for (int i = 0; i < r.Destinations.Count; i++) h.Append($"<th>Cíl {i + 1}</th>");
        h.Append("</tr>\n");
        foreach (var f in r.Files)
        {
            h.Append($"<tr><td class=\"mono\">{Esc(f.Path)}</td><td class=\"num\">{Format.Bytes(f.Size)}</td><td class=\"mono\">{Esc(f.Xxh64 ?? "—")}</td>");
            foreach (var s in f.Destinations)
            {
                var cls = s == "verified" ? "ok" : (s == "failed" ? "bad" : "warn");
                h.Append($"<td class=\"{cls}\">{StateLabel.GetValueOrDefault(s, s)}</td>");
            }
            h.Append("</tr>\n");
        }
        h.Append("</table></div></details>\n");
        if (r.Skipped.Count > 0)
            h.Append($"<p class=\"sub\">Vynechané systémové soubory: {Esc(string.Join(", ", r.Skipped))}</p>\n");
        h.Append($"<footer>{Esc(r.Tool)} {Esc(r.Version)} · ASC MHL v ascmhl/ · {DateTimeText(DateTime.Now)}</footer></main></body></html>\n");
        return h.ToString();
    }

    static void Stat(StringBuilder h, string value, string label) =>
        h.Append($"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>\n");

    static void AppendClips(StringBuilder h, OffloadReport r)
    {
        h.Append("<h2>Klipy</h2><div class=\"panel\"><table>\n");
        h.Append("<tr><th></th><th>Klip</th><th>Formát</th><th>Rozlišení</th><th class=\"num\">FPS</th><th class=\"num\">Délka</th><th>TC</th><th class=\"num\">Audio</th><th class=\"num\">Velikost</th><th>Proxy</th></tr>\n");
        foreach (var c in r.Clips)
        {
            var img = c.Thumbnail != null
                ? $"<img class=\"thumb\" src=\"data:image/jpeg;base64,{Convert.ToBase64String(c.Thumbnail)}\" alt=\"\">"
                : "<div class=\"nothumb\"></div>";
            var i = c.Info;
            var codecParts = new List<string>();
            if (c.Clip.Format != ClipFormat.Video) codecParts.Add(c.Clip.Format.Label());
            if (i?.Codec is string codec && !codecParts.Contains(codec)) codecParts.Add(codec);
            var (pc, pl) = ProxyLabel(c);
            var notes = new List<string>();
            foreach (var n in new[] { c.ProbeError, c.ProxyNote })
                if (n != null && !notes.Contains(n)) notes.Add(n);
            var note = string.Join(" · ", notes);
            var cameraProxy = c.Clip.IsCameraProxy ? " <span class=\"muted\">(proxy kamery)</span>" : "";
            var noteHtml = note.Length == 0 ? "" : $"<div class=\"path err\">{Esc(note)}</div>";
            var audio = i == null ? "" : (i.AudioChannels > 0 ? $"{i.AudioChannels} ch" : "—");
            h.Append($"<tr><td>{img}</td><td class=\"clip\"><div class=\"clipname\">{Esc(c.Clip.Name)}{cameraProxy}</div>");
            h.Append($"<div class=\"path\">{Esc(c.Clip.Path)}</div>{noteHtml}</td>");
            h.Append($"<td class=\"wrap\">{Esc(string.Join(" · ", codecParts))}</td><td>{Esc(i?.ResolutionLabel ?? "")}</td><td class=\"num\">{Esc(i?.FpsLabel ?? "")}</td>");
            h.Append($"<td class=\"num\">{Esc(i?.DurationLabel ?? "")}</td><td class=\"mono\">{Esc(i?.Timecode ?? "")}</td>");
            h.Append($"<td class=\"num\">{audio}</td>");
            h.Append($"<td class=\"num\">{Format.Bytes(c.Clip.Bytes)}</td><td class=\"{pc}\">{pl}</td></tr>\n");
        }
        h.Append("</table></div>\n");
        if (r.Proxy is { Enabled: true } p)
        {
            var lut = p.LutPath != null ? Path.GetFileName(p.LutPath) : "bez LUT";
            var frame = p.FrameLabel is string fl ? $", výřez {fl}" : "";
            h.Append($"<p class=\"sub\">Proxy: {Esc(p.Codec.Label())}, {p.Height}p{frame}, LUT: {Esc(lut)} → <span class=\"mono\">{Layout.ProxyDirName}/</span></p>\n");
        }
    }
}
```

- [ ] **Step 4: `OffloadReport.Write`**

V `windows/src/Offload.Core/OffloadReport.cs` přidat do třídy `OffloadReport` (za `FromJson`):

```csharp
    /// Writes all report files into `<card>/_REPORT`. The hash list and MHL are the record of the copy itself,
    /// so they are written once (`includeHashRecords`); the HTML/JSON are refreshed as clips get analysed.
    public void Write(string cardPath, bool includeHashRecords)
    {
        var dir = System.IO.Path.Combine(cardPath, Layout.ReportDirName);
        NativeIO.Mkdirs(dir);
        var outputs = new List<(string Name, string Text)>
        {
            ("offload_report.json", ToJson()),
            ("offload_report.txt", Summary),
            ($"{CardName}_report.html", HtmlReport.Render(this)),
        };
        if (includeHashRecords) outputs.Add(($"{CardName}.xxh64", HashList));
        foreach (var (name, text) in outputs)
            NativeIO.WriteFileDurably(System.IO.Path.Combine(dir, name), Encoding.UTF8.GetBytes(text));
        if (includeHashRecords) Mhl.Write(this, cardPath);
    }
```

(`System.IO.Path` plně kvalifikovaně — `OffloadReport` nemá vlastnost `Path`, ale `ReportFile` ano a čitelnost je tak jednoznačná.)

- [ ] **Step 5: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~HtmlReportTests|FullyQualifiedName~ReportTests|FullyQualifiedName~MhlTests"`
Expected: PASS (20 testů).

- [ ] **Step 6: Commit**

```powershell
git add windows
git commit -m "Windows: HTML report and report writing" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Pipeline — SerialWorker, BufferPool, SourceHasher, DestWriter

**Files:**
- Create: `windows/src/Offload.Core/SerialWorker.cs`, `BufferPool.cs`, `SourceHasher.cs`, `DestWriter.cs`
- Test: `windows/tests/Offload.Core.Tests/DestWriterTests.cs`

**Interfaces:**
- Consumes: `AlignedBuffer`, `NativeIO`, `Hashing.HashFile`, `Layout.Join`, `SourceFile`, `OffloadOptions`, `DestFileResult`, `FileState`, `Format.Hex64`.
- Produces (všechno `internal`):
  - `SerialWorker(string name)` — `Post(Action)`, `Invoke(Action)`, `Wait()`, `Dispose()`.
  - `BufferPool(int count, int size)` — `Size`, `Take() -> AlignedBuffer`, `Give(AlignedBuffer)`, `Dispose()`.
  - `Chunk(AlignedBuffer buffer, int length, long offset, int consumers, BufferPool pool)` — `Buffer`, `Length`, `Offset`, `Release()`.
  - `SourceHasher(int fileCount)` — `Hashes` (`ulong?[]`), `Begin()`, `Update(Chunk)`, `Finish(int)`, `Abort()`, `Wait()`, `Dispose()`.
  - `DestWriter(int index, string cardPath, int fileCount, OffloadOptions options)` — `CardPath`, `Results` (`DestFileResult[]`), `Unbuffered`, `Written`, `Verified`, `static PartName(string rel)`, `Begin(SourceFile)`, `Write(Chunk)`, `Finish(int, SourceFile)`, `Abort(int, string reason)`, `FlushDevice()`, `Wait()`, `Verify(IReadOnlyList<SourceFile>, ulong?[], CancellationToken)`, `Dispose()`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/DestWriterTests.cs`:

```csharp
using System.IO.Hashing;

namespace Offload.Core.Tests;

public class DestWriterTests
{
    const int Chunk = 64 * 1024;
    static readonly DateTime When = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    /// Feeds `data` to the writer in blocks the way the engine does.
    static void Feed(DestWriter w, BufferPool pool, byte[] data, int fileIndex, SourceFile file)
    {
        w.Begin(file);
        for (long off = 0; off < data.Length; off += Chunk)
        {
            int n = (int)Math.Min(Chunk, data.Length - off);
            var buf = pool.Take();
            data.AsSpan((int)off, n).CopyTo(buf.Span);
            w.Write(new Chunk(buf, n, off, 1, pool));
        }
        w.Finish(fileIndex, file);
        w.Wait();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(Chunk - 1)]
    [InlineData(Chunk)]
    [InlineData(Chunk + 1)]
    [InlineData(3 * Chunk + 123)]
    public void WritesExactBytesAndModificationTime(int size)
    {
        using var dir = new TempDir();
        var card = dir.Sub("card", "CLIP");
        var data = new byte[size];
        new Random(size).NextBytes(data);
        var file = new SourceFile("CLIP/x.mov", size, When);
        var options = new OffloadOptions { ChunkSize = Chunk, BufferCount = 4 };
        using var pool = new BufferPool(options.BufferCount, options.ChunkSize);
        using (var w = new DestWriter(0, Path.GetDirectoryName(card)!, 1, options))
        {
            Feed(w, pool, data, 0, file);
            Assert.Equal(new DestFileResult(FileState.Written, null), w.Results[0]);
            Assert.Equal(size, w.Written);
            Assert.True(w.Unbuffered);
        }
        var final = Path.Combine(card, "x.mov");
        Assert.Equal(data, File.ReadAllBytes(final));
        Assert.Equal(When, File.GetLastWriteTimeUtc(final));
        Assert.False(File.GetAttributes(final).HasFlag(FileAttributes.Hidden));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.offload-part", SearchOption.AllDirectories));
    }

    [Fact]
    public void ForceBuffered_StillWritesExactBytes()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var data = new byte[Chunk + 7];
        new Random(5).NextBytes(data);
        var options = new OffloadOptions { ChunkSize = Chunk, BufferCount = 4, ForceBuffered = true };
        using var pool = new BufferPool(4, Chunk);
        using (var w = new DestWriter(0, card, 1, options))
        {
            Feed(w, pool, data, 0, new SourceFile("x.bin", data.Length, When));
            Assert.False(w.Unbuffered);
            Assert.Equal(FileState.Written, w.Results[0].State);
        }
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(card, "x.bin")));
    }

    [Fact]
    public void Abort_RemovesPartAndRecordsReason()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var pool = new BufferPool(4, Chunk);
        using var w = new DestWriter(0, card, 1, new OffloadOptions { ChunkSize = Chunk });
        var file = new SourceFile("x.bin", 2 * Chunk, When);
        w.Begin(file);
        var buf = pool.Take();
        w.Write(new Chunk(buf, Chunk, 0, 1, pool));
        w.Abort(0, "source: cancelled");
        w.Wait();
        Assert.Equal(new DestFileResult(FileState.Failed, "source: cancelled"), w.Results[0]);
        Assert.Empty(Directory.GetFiles(card));
    }

    [Fact]
    public void FinalPathBlocked_FailsWithRenameErrorAndCleansUp()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        Directory.CreateDirectory(Path.Combine(card, "x.bin"));
        using var pool = new BufferPool(4, Chunk);
        using var w = new DestWriter(0, card, 1, new OffloadOptions { ChunkSize = Chunk });
        Feed(w, pool, [1, 2, 3], 0, new SourceFile("x.bin", 3, When));
        Assert.Equal(FileState.Failed, w.Results[0].State);
        Assert.StartsWith("rename", w.Results[0].Error);
        Assert.Empty(Directory.GetFiles(card));
    }

    [Fact]
    public void Verify_DetectsMatchMismatchAndSize()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var a = new byte[5000]; new Random(1).NextBytes(a);
        var b = new byte[5000]; new Random(2).NextBytes(b);
        var c = new byte[5000]; new Random(3).NextBytes(c);
        var files = new[] { new SourceFile("a", 5000, When), new SourceFile("b", 5000, When), new SourceFile("c", 5000, When) };
        var options = new OffloadOptions { ChunkSize = Chunk };
        using var pool = new BufferPool(4, Chunk);
        using var w = new DestWriter(0, card, 3, options);
        Feed(w, pool, a, 0, files[0]);
        Feed(w, pool, b, 1, files[1]);
        Feed(w, pool, c, 2, files[2]);
        var bytes = File.ReadAllBytes(Path.Combine(card, "b"));
        bytes[100] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(card, "b"), bytes);
        File.WriteAllBytes(Path.Combine(card, "c"), c[..4000]);

        w.Verify(files, [XxHash64.HashToUInt64(a), XxHash64.HashToUInt64(b), XxHash64.HashToUInt64(c)], CancellationToken.None);

        Assert.Equal(FileState.Verified, w.Results[0].State);
        Assert.Equal(FileState.Failed, w.Results[1].State);
        Assert.StartsWith("HASH MISMATCH", w.Results[1].Error);
        Assert.Equal("size mismatch: 4000 != 5000", w.Results[2].Error);
        Assert.Equal(14000, w.Verified);
    }

    [Fact]
    public void FlushDevice_LeavesNoFile()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var w = new DestWriter(0, card, 0, new OffloadOptions());
        w.FlushDevice();
        Assert.Empty(Directory.GetFiles(card));
    }

    [Fact]
    public void PartName_IsHiddenSiblingName()
    {
        Assert.Equal(".x.mov.offload-part", DestWriter.PartName("x.mov"));
        Assert.Equal("A/B/.x.mov.offload-part", DestWriter.PartName("A/B/x.mov"));
    }

    [Fact]
    public void SourceHasher_HashesChunkStream()
    {
        var data = new byte[3 * Chunk + 5];
        new Random(9).NextBytes(data);
        using var pool = new BufferPool(4, Chunk);
        using var hasher = new SourceHasher(2);
        hasher.Begin();
        for (long off = 0; off < data.Length; off += Chunk)
        {
            int n = (int)Math.Min(Chunk, data.Length - off);
            var buf = pool.Take();
            data.AsSpan((int)off, n).CopyTo(buf.Span);
            hasher.Update(new Chunk(buf, n, off, 1, pool));
        }
        hasher.Finish(1);
        hasher.Wait();
        Assert.Null(hasher.Hashes[0]);
        Assert.Equal(XxHash64.HashToUInt64(data), hasher.Hashes[1]);
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~DestWriterTests"`
Expected: build FAIL — `DestWriter`, `BufferPool`, `Chunk`, `SourceHasher` neexistují.

- [ ] **Step 3: SerialWorker**

`windows/src/Offload.Core/SerialWorker.cs`:

```csharp
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Offload.Core;

/// One thread working through a FIFO queue — the counterpart of a serial DispatchQueue on the Mac.
internal sealed class SerialWorker : IDisposable
{
    readonly BlockingCollection<Action> queue = new();
    readonly Thread thread;

    public SerialWorker(string name)
    {
        thread = new Thread(Run) { IsBackground = true, Name = name };
        thread.Start();
    }

    void Run()
    {
        foreach (var action in queue.GetConsumingEnumerable())
        {
            try { action(); }
            catch (Exception) { /* queued actions record their own errors; one must never stop the queue */ }
        }
    }

    public void Post(Action action) => queue.Add(action);

    /// Runs `action` after everything queued so far and waits for it; its exception is rethrown here.
    public void Invoke(Action action)
    {
        using var done = new ManualResetEventSlim();
        ExceptionDispatchInfo? error = null;
        queue.Add(() =>
        {
            try { action(); }
            catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
            finally { done.Set(); }
        });
        done.Wait();
        error?.Throw();
    }

    public void Wait() => Invoke(() => { });

    public void Dispose()
    {
        queue.CompleteAdding();
        thread.Join();
        queue.Dispose();
    }
}
```

- [ ] **Step 4: BufferPool, Chunk, SourceHasher**

`windows/src/Offload.Core/BufferPool.cs`:

```csharp
namespace Offload.Core;

/// Fixed set of aligned buffers; Take blocks when all are in flight (backpressure).
internal sealed class BufferPool : IDisposable
{
    readonly AlignedBuffer[] all;
    readonly Stack<AlignedBuffer> free;
    readonly SemaphoreSlim available;
    readonly Lock gate = new();

    public int Size { get; }

    public BufferPool(int count, int size)
    {
        Size = size;
        all = Enumerable.Range(0, count).Select(_ => new AlignedBuffer(size)).ToArray();
        free = new Stack<AlignedBuffer>(all);
        available = new SemaphoreSlim(count, count);
    }

    public AlignedBuffer Take()
    {
        available.Wait();
        lock (gate) return free.Pop();
    }

    public void Give(AlignedBuffer buffer)
    {
        lock (gate) free.Push(buffer);
        available.Release();
    }

    public void Dispose()
    {
        foreach (var b in all) b.Dispose();
        available.Dispose();
    }
}

/// One block read from the card, shared by the hasher and every writer; back to the pool when all are done.
internal sealed class Chunk(AlignedBuffer buffer, int length, long offset, int consumers, BufferPool pool)
{
    int pending = consumers;

    public AlignedBuffer Buffer => buffer;
    public int Length => length;
    public long Offset => offset;

    public void Release()
    {
        if (Interlocked.Decrement(ref pending) == 0) pool.Give(buffer);
    }
}
```

`windows/src/Offload.Core/SourceHasher.cs`:

```csharp
using System.IO.Hashing;

namespace Offload.Core;

/// Hashes the source stream on its own thread, so hashing never blocks the card read.
internal sealed class SourceHasher : IDisposable
{
    readonly SerialWorker worker = new("offload.hash");
    XxHash64? stream;

    public ulong?[] Hashes { get; }

    public SourceHasher(int fileCount) => Hashes = new ulong?[fileCount];

    public void Begin() => worker.Post(() => stream = new XxHash64());

    public void Update(Chunk c) => worker.Post(() =>
    {
        stream?.Append(c.Buffer.Span[..c.Length]);
        c.Release();
    });

    public void Finish(int index) => worker.Post(() =>
    {
        Hashes[index] = stream?.GetCurrentHashAsUInt64();
        stream = null;
    });

    public void Abort() => worker.Post(() => stream = null);

    public void Wait() => worker.Wait();

    public void Dispose() => worker.Dispose();
}
```

- [ ] **Step 5: DestWriter**

`windows/src/Offload.Core/DestWriter.cs`:

```csharp
using Microsoft.Win32.SafeHandles;

namespace Offload.Core;

/// Writes the stream to one destination. Each destination has its own thread, so disks run in parallel.
internal sealed class DestWriter : IDisposable
{
    readonly SerialWorker worker;
    readonly OffloadOptions options;
    SafeFileHandle? handle;
    string partPath = "";
    string? currentError;
    int align = AlignedBuffer.Alignment;
    long written;
    long verified;

    public string CardPath { get; }
    public DestFileResult[] Results { get; }
    /// False once this volume refused unbuffered writes and the writer fell back to the file cache.
    public bool Unbuffered { get; private set; }
    public long Written => Interlocked.Read(ref written);
    public long Verified => Interlocked.Read(ref verified);

    public DestWriter(int index, string cardPath, int fileCount, OffloadOptions options)
    {
        CardPath = cardPath;
        this.options = options;
        Unbuffered = !options.ForceBuffered;
        Results = new DestFileResult[fileCount];
        worker = new SerialWorker($"offload.dest{index}");
    }

    /// In-progress name: ".<name>.offload-part" next to the final file, so an unfinished copy never looks finished.
    public static string PartName(string rel)
    {
        int slash = rel.LastIndexOf('/');
        var name = "." + rel[(slash + 1)..] + ".offload-part";
        return slash < 0 ? name : rel[..slash] + "/" + name;
    }

    public void Begin(SourceFile file) => worker.Post(() =>
    {
        currentError = null;
        partPath = Layout.Join(CardPath, PartName(file.RelPath));
        try
        {
            handle = OpenPart(file.Size);
            File.SetAttributes(partPath, FileAttributes.Hidden);
        }
        catch (Exception e)
        {
            currentError = e.Message;
            CloseAndRemovePart();
        }
    });

    SafeFileHandle OpenPart(long size)
    {
        if (Unbuffered)
        {
            SafeFileHandle h;
            try
            {
                h = NativeIO.OpenWrite(partPath, unbuffered: true, size);
            }
            catch (IOError e) when (e.Code == NativeIO.ErrorInvalidParameter)
            {
                Unbuffered = false;
                return NativeIO.OpenWrite(partPath, unbuffered: false, size);
            }
            align = NativeIO.SectorSize(h);
            if (options.ChunkSize % align == 0) return h;
            h.Dispose();   // sector larger than the block size: unbuffered writes are impossible here
            Unbuffered = false;
        }
        return NativeIO.OpenWrite(partPath, unbuffered: false, size);
    }

    public void Write(Chunk c) => worker.Post(() =>
    {
        try
        {
            if (handle == null) return;
            // Unbuffered writes must be whole sectors: the last block is written padded, Finish trims the file.
            int len = Unbuffered ? (c.Length + align - 1) / align * align : c.Length;
            try
            {
                NativeIO.Write(handle, c.Buffer.Span[..len], c.Offset, partPath);
            }
            catch (IOError e) when (Unbuffered && e.Code == NativeIO.ErrorInvalidParameter)
            {
                SwitchToBuffered();
                NativeIO.Write(handle!, c.Buffer.Span[..c.Length], c.Offset, partPath);
            }
            Interlocked.Add(ref written, c.Length);
        }
        catch (Exception e)
        {
            currentError = e.Message;
            CloseAndRemovePart();
        }
        finally
        {
            c.Release();
        }
    });

    void SwitchToBuffered()
    {
        handle?.Dispose();
        Unbuffered = false;
        handle = NativeIO.OpenExisting(partPath);
    }

    public void Finish(int index, SourceFile file) => worker.Post(() =>
    {
        if (handle == null || currentError != null)
        {
            Results[index] = new DestFileResult(FileState.Failed, currentError ?? "write failed");
            CloseAndRemovePart();
            return;
        }
        var finalPath = Layout.Join(CardPath, file.RelPath);
        try
        {
            NativeIO.SetLength(handle, file.Size, partPath);
            // Big files are flushed one by one, small ones together in FlushDevice. Without unbuffered
            // writes the data may still sit in the file cache, so then every file is flushed.
            if (file.Size >= options.FullSyncThreshold || !Unbuffered) NativeIO.Flush(handle, partPath);
            NativeIO.SetModified(handle, file.ModifiedUtc, partPath);
            handle.Dispose();
            handle = null;
            NativeIO.Move(partPath, finalPath, replace: false);
            File.SetAttributes(finalPath, File.GetAttributes(finalPath) & ~FileAttributes.Hidden);
            Results[index] = new DestFileResult(FileState.Written, null);
        }
        catch (Exception e)
        {
            CloseAndRemovePart();
            Results[index] = new DestFileResult(FileState.Failed, e.Message);
        }
    });

    public void Abort(int index, string reason) => worker.Post(() =>
    {
        CloseAndRemovePart();
        Results[index] = new DestFileResult(FileState.Failed, reason);
    });

    /// One FlushFileBuffers makes the drive empty its write cache — covering all the small files at once.
    public void FlushDevice() => worker.Invoke(() =>
    {
        var path = Path.Combine(CardPath, ".offload-flush");
        try
        {
            using var h = NativeIO.OpenWrite(path, unbuffered: false);
            NativeIO.Write(h, "x"u8, 0, path);
            NativeIO.Flush(h, path);
        }
        catch (IOError)
        {
            // Best effort: big files were flushed individually, verification reads everything back anyway.
        }
        finally
        {
            NativeIO.Delete(path);
        }
    });

    public void Wait() => worker.Wait();

    /// Re-reads every written file from the disk and compares it with the source hash.
    public void Verify(IReadOnlyList<SourceFile> files, ulong?[] sourceHashes, CancellationToken ct)
    {
        using var buf = new AlignedBuffer(options.ChunkSize);
        for (int i = 0; i < files.Count; i++)
        {
            if (Results[i].State != FileState.Written) continue;
            if (ct.IsCancellationRequested) return;
            var f = files[i];
            try
            {
                var (hash, size) = Hashing.HashFile(Layout.Join(CardPath, f.RelPath), buf, n => Interlocked.Add(ref verified, n), ct);
                if (size != f.Size)
                    Results[i] = new DestFileResult(FileState.Failed, $"size mismatch: {size} != {f.Size}");
                else if (hash != sourceHashes[i])
                    Results[i] = new DestFileResult(FileState.Failed,
                        $"HASH MISMATCH: dest {Format.Hex64(hash)} != source {(sourceHashes[i] is ulong s ? Format.Hex64(s) : "-")}");
                else
                    Results[i] = new DestFileResult(FileState.Verified, null);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Results[i] = new DestFileResult(FileState.Failed, "verify: " + e.Message);
            }
        }
    }

    void CloseAndRemovePart()
    {
        handle?.Dispose();
        handle = null;
        if (partPath.Length > 0) NativeIO.Delete(partPath);
    }

    public void Dispose()
    {
        worker.Dispose();
        CloseAndRemovePart();
    }
}
```

Pozor: `Dispose` maže `partPath` posledního souboru jen pokud ještě nebyl přejmenován — po úspěšném `Finish` už `partPath` neexistuje a `Delete` je no-op.

- [ ] **Step 6: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~DestWriterTests"`
Expected: PASS (17 testů).

- [ ] **Step 7: Commit**

```powershell
git add windows
git commit -m "Windows: copy pipeline — buffer pool, hasher, destination writer" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Engine — preflight

**Files:**
- Create: `windows/src/Offload.Core/OffloadEngine.cs` (zatím jen `Prepare`, `Fat32Problem`, stav a `Snapshot`; `Run` přidá Task 12)
- Test: `windows/tests/Offload.Core.Tests/PreflightTests.cs`

**Interfaces:**
- Consumes: `NativeIO.VolumeId/FilesystemType/AvailableBytes`, `Scanner`, `Layout`, `OffloadError`, `OffloadOptions`, `OffloadPlan`.
- Produces: `OffloadEngine(OffloadOptions? options = null)` — `Options`, `Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date) -> OffloadPlan`, `Cancel()`, `IsCancelled`, `Snapshot() -> ProgressSnapshot`; `internal static Fat32Problem(string filesystem, IEnumerable<SourceFile> files) -> string?`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/PreflightTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class PreflightTests
{
    static OffloadOptions SameVolume() => new() { AllowSameDevice = true, AllowSourceVolume = true };

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 1000, 1);
        return card;
    }

    [Fact]
    public void Plan_HasScanDateAndCardFolder()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var d1 = dir.Sub("d1");
        var plan = new OffloadEngine(SameVolume()).Prepare(card, [d1], null, "2026-10-04");
        Assert.Equal("A001", plan.CardName);
        Assert.Equal("2026-10-04", plan.Date);
        Assert.Equal([Path.Combine(d1, "2026-10-04", "A001")], plan.CardPaths);
        Assert.Single(plan.Scan.Files);
    }

    [Fact]
    public void CardName_IsSanitized()
    {
        using var dir = new TempDir();
        var plan = new OffloadEngine(SameVolume()).Prepare(Card(dir), [dir.Sub("d1")], "Den 1: hlavní", "2026-10-04");
        Assert.Equal("Den 1_ hlavní", plan.CardName);
    }

    [Fact]
    public void SourceMissing() => AssertPreflight("Source not found",
        e => e.Prepare(@"C:\offload-no-such-card", [Path.GetTempPath()], null, null));

    [Fact]
    public void NoDestination()
    {
        using var dir = new TempDir();
        AssertPreflight("No destination given", e => e.Prepare(Card(dir), [], null, null));
    }

    [Fact]
    public void DestinationMissing()
    {
        using var dir = new TempDir();
        AssertPreflight("Destination not found", e => e.Prepare(Card(dir), [Path.Combine(dir.Path, "nope")], null, null));
    }

    [Fact]
    public void DestinationOnSourceVolume_IsRejected()
    {
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() =>
            new OffloadEngine(new OffloadOptions { AllowSameDevice = true }).Prepare(Card(dir), [dir.Sub("d1")], null, null));
        Assert.Contains("same volume as the source", e.Message);
    }

    [Fact]
    public void TwoDestinationsOnOneVolume_NeedAllowSameDevice()
    {
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() =>
            new OffloadEngine(new OffloadOptions { AllowSourceVolume = true }).Prepare(Card(dir), [dir.Sub("d1"), dir.Sub("d2")], null, null));
        Assert.Contains("--allow-same-device", e.Message);
    }

    [Fact]
    public void EmptySource()
    {
        using var dir = new TempDir();
        AssertPreflight("Source contains no files", e => e.Prepare(dir.Sub("empty"), [dir.Sub("d1")], null, null));
    }

    [Fact]
    public void Fat32Problem_RejectsFourGigabyteFile()
    {
        SourceFile[] files = [new("CLIP/small.mov", 100, DateTime.UtcNow), new("CLIP/A001C002.mov", 4L << 30, DateTime.UtcNow)];
        var problem = OffloadEngine.Fat32Problem("FAT32", files);
        Assert.NotNull(problem);
        Assert.Contains("CLIP/A001C002.mov", problem);
        Assert.Null(OffloadEngine.Fat32Problem("exFAT", files));
        Assert.Null(OffloadEngine.Fat32Problem("NTFS", files));
    }

    [Fact]
    public void Fat32Problem_AllowsJustUnderFourGigabytes() =>
        Assert.Null(OffloadEngine.Fat32Problem("FAT32", [new("a", uint.MaxValue, DateTime.UtcNow)]));

    static void AssertPreflight(string message, Action<OffloadEngine> act)
    {
        var e = Assert.Throws<OffloadError>(() => act(new OffloadEngine(SameVolume())));
        Assert.True(e.IsPreflight);
        Assert.Contains(message, e.Message);
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~PreflightTests"`
Expected: build FAIL — `OffloadEngine` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/OffloadEngine.cs`:

```csharp
namespace Offload.Core;

public sealed partial class OffloadEngine
{
    readonly Lock gate = new();
    readonly CancellationTokenSource cts = new();
    CopyPhase phase = CopyPhase.Copy;
    int fileIndex;
    string currentFile = "";
    int errorCount;
    long readBytes;
    DestWriter[] writers = [];
    OffloadPlan? plan;

    public OffloadOptions Options { get; }

    public OffloadEngine(OffloadOptions? options = null) => Options = options ?? new OffloadOptions();

    public void Cancel() => cts.Cancel();

    public bool IsCancelled => cts.IsCancellationRequested;

    /// Scans the card and checks the destinations before a single byte is written.
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date)
    {
        if (!Directory.Exists(source)) throw OffloadError.Preflight($"Source not found: {source}");
        source = Path.GetFullPath(source);
        var roots = destRoots.Select(Path.GetFullPath).ToList();
        if (roots.Count == 0) throw OffloadError.Preflight("No destination given");
        var sourceVolume = NativeIO.VolumeId(source) ?? throw OffloadError.Preflight($"Cannot stat source {source}");

        var volumes = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) throw OffloadError.Preflight($"Destination not found: {root}");
            if (!IsWritable(root)) throw OffloadError.Preflight($"Destination not writable: {root}");
            var volume = NativeIO.VolumeId(root) ?? throw OffloadError.Preflight($"Cannot stat destination {root}");
            if (volume == sourceVolume && !Options.AllowSourceVolume)
                throw OffloadError.Preflight($"Destination {root} is on the same volume as the source");
            if (volumes.Contains(volume) && !Options.AllowSameDevice)
                throw OffloadError.Preflight($"Destinations share a volume ({root}) — that is not a second copy. Use --allow-same-device to override.");
            volumes.Add(volume);
        }

        var scan = Scanner.Scan(source);
        if (scan.Files.Count == 0) throw OffloadError.Preflight("Source contains no files");
        var problems = Scanner.WindowsProblems(scan);
        if (problems.Count > 0)
        {
            var more = problems.Count > 20 ? $"\n  … and {problems.Count - 20} more" : "";
            throw OffloadError.Preflight("Names Windows cannot store:\n  " + string.Join("\n  ", problems.Take(20)) + more);
        }

        long needed = scan.TotalBytes + scan.Files.Count * 64L * 1024 + (64L << 20);
        foreach (var root in roots)
        {
            if (Fat32Problem(NativeIO.FilesystemType(root), scan.Files) is string fat)
                throw OffloadError.Preflight($"{root}: {fat}");
            long free = NativeIO.AvailableBytes(root) ?? 0;
            if (free < needed)
                throw OffloadError.Preflight($"Not enough space on {root}: need {Format.Bytes(needed)}, free {Format.Bytes(free)}");
        }

        var day = date ?? Layout.Today();
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan { Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots };
    }

    /// FAT32 cannot hold files of 4 GiB or more — say so before the copy, not at 99 %.
    internal static string? Fat32Problem(string filesystem, IEnumerable<SourceFile> files)
    {
        if (!filesystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase)) return null;
        var big = files.FirstOrDefault(f => f.Size > uint.MaxValue);
        return big == null ? null : $"{filesystem} cannot store files of 4 GB or more ({big.RelPath}, {Format.Bytes(big.Size)})";
    }

    static bool IsWritable(string dir)
    {
        var probe = Path.Combine(dir, $".offload-write-test-{Environment.ProcessId}");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public ProgressSnapshot Snapshot()
    {
        lock (gate)
        {
            return new ProgressSnapshot(phase, plan?.Scan.TotalBytes ?? 0, plan?.Scan.Files.Count ?? 0, fileIndex, currentFile,
                Interlocked.Read(ref readBytes), writers.Select(w => w.Written).ToArray(), writers.Select(w => w.Verified).ToArray(), errorCount);
        }
    }

    void SetPhase(CopyPhase p)
    {
        lock (gate) phase = p;
    }
}
```

Poznámka: `SetPhase`, `writers`, `plan`, `readBytes`, `errorCount` použije `Run` v Task 12 (soubor `OffloadEngine.Run.cs`, proto `partial`). Do té doby kompilátor hlásí CS0169/CS0414 „never used“ jako chybu (warnings as errors) — pokud ano, přidat dočasně na začátek souboru `#pragma warning disable CS0169, CS0414` a v Task 12 Step 4 ho odstranit.

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~PreflightTests"`
Expected: PASS (10 testů).

- [ ] **Step 5: Commit**

```powershell
git add windows
git commit -m "Windows: preflight checks (volumes, space, names, FAT32)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Engine — kopie, flush, ověření, report

**Files:**
- Create: `windows/src/Offload.Core/OffloadEngine.Run.cs`
- Modify: `windows/src/Offload.Core/OffloadEngine.cs` (odstranit případný `#pragma` z Task 11)
- Test: `windows/tests/Offload.Core.Tests/EngineTests.cs`

**Interfaces:**
- Consumes: vše z Task 10 a 11, `OffloadReport.Build/Write`, `FileReader`.
- Produces: `OffloadEngine.Run(OffloadPlan plan) -> OffloadReport` (vyhazuje `OffloadError.Failed` jen když se nepodaří zapsat report).

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/EngineTests.cs`:

```csharp
using System.Text;

namespace Offload.Core.Tests;

public class EngineTests
{
    const int Chunk = 64 * 1024;

    static OffloadOptions Options(bool forceBuffered = false) => new()
    {
        ChunkSize = Chunk, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ForceBuffered = forceBuffered,
    };

    static readonly int[] Sizes = [0, 1, 4095, 4096, 4097, Chunk - 1, Chunk, Chunk + 1, 3 * Chunk + 123];

    /// Card with files of every boundary size; returns relative path → content.
    static Dictionary<string, byte[]> MakeCard(string card)
    {
        var files = new Dictionary<string, byte[]>();
        for (int i = 0; i < Sizes.Length; i++)
        {
            var rel = $"CLIP/F{i:00}.mov";
            files[rel] = TestFs.WriteRandom(Layout.Join(card, rel), Sizes[i], i);
            File.SetLastWriteTimeUtc(Layout.Join(card, rel), new DateTime(2024, 1, 1, 0, 0, i, DateTimeKind.Utc));
        }
        Directory.CreateDirectory(Path.Combine(card, "EMPTY"));
        return files;
    }

    static void AssertNoPartFiles(string root) =>
        Assert.Empty(Directory.GetFiles(root, "*.offload-part", SearchOption.AllDirectories));

    [Fact]
    public void CopiesEveryFileToTwoDestinations_Verified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var files = MakeCard(card);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-04");

        var report = engine.Run(plan);

        Assert.True(report.SafeToEject);
        Assert.Equal(Sizes.Length, report.TotalFiles);
        foreach (var cardPath in plan.CardPaths)
        {
            foreach (var (rel, data) in files)
            {
                var path = Layout.Join(cardPath, rel);
                Assert.Equal(data, File.ReadAllBytes(path));
                Assert.Equal(File.GetLastWriteTimeUtc(Layout.Join(card, rel)), File.GetLastWriteTimeUtc(path));
            }
            Assert.True(Directory.Exists(Path.Combine(cardPath, "EMPTY")));
            foreach (var name in new[] { "offload_report.json", "offload_report.txt", "A001_report.html", "A001.xxh64" })
                Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", name)), name);
            Assert.Single(Directory.GetFiles(Path.Combine(cardPath, "ascmhl"), "*.mhl"));
            AssertNoPartFiles(cardPath);
        }
        var snap = engine.Snapshot();
        Assert.Equal(CopyPhase.Done, snap.Phase);
        Assert.Equal(report.TotalBytes, snap.ReadBytes);
        Assert.All(snap.VerifiedBytes, v => Assert.Equal(report.TotalBytes, v));
    }

    [Fact]
    public void DefaultBlockSize_FileOneByteOverBlock()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var data = TestFs.WriteRandom(Path.Combine(card, "big.mov"), (16 << 20) + 1, 42);
        var engine = new OffloadEngine(new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true });
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        Assert.True(engine.Run(plan).SafeToEject);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(plan.CardPaths[0], "big.mov")));
    }

    [Fact]
    public void ForceBuffered_IsVerifiedAndReported()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var engine = new OffloadEngine(Options(forceBuffered: true));
        var report = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04"));
        Assert.True(report.SafeToEject);
        Assert.False(report.Destinations[0].Unbuffered);
        Assert.Contains("(buffered I/O)", report.Summary);
    }

    [Fact]
    public void NoVerify_ReportsWrittenNotSafeToEject()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var options = Options();
        options.Verify = false;
        var engine = new OffloadEngine(options);
        var report = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04"));
        Assert.True(report.Success);
        Assert.False(report.SafeToEject);
        Assert.All(report.Files, f => Assert.Equal(["written"], f.Destinations));
    }

    [Fact]
    public void CancelledBeforeRun_CopiesNothingLeavesNoParts()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        engine.Cancel();
        var report = engine.Run(plan);
        Assert.False(report.SafeToEject);
        Assert.All(report.Files, f => Assert.Equal("cancelled", f.SourceError));
        Assert.Empty(Directory.GetFiles(Path.Combine(plan.CardPaths[0], "CLIP")));
    }

    [Fact]
    public async Task CancelledDuringCopy_LeavesNoPartsAndIsNotSafe()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        for (int i = 0; i < 40; i++) TestFs.WriteRandom(Path.Combine(card, $"F{i:00}.bin"), 1 << 20, i);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-04");
        var run = Task.Run(() => engine.Run(plan));
        while (engine.Snapshot().ReadBytes == 0 && !run.IsCompleted) await Task.Delay(1);
        engine.Cancel();
        var report = await run;
        Assert.False(report.SafeToEject);
        Assert.False(report.Verified);
        AssertNoPartFiles(dir.Path);
    }

    // Review focus 1
    [Fact]
    public void SourceShrinksAfterPrepare_FileFailsOthersVerified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        File.WriteAllBytes(Path.Combine(card, "CLIP", "F08.mov"), new byte[10]);

        var report = engine.Run(plan);

        var bad = report.Files.Single(f => f.Path == "CLIP/F08.mov");
        Assert.StartsWith("source size changed during copy", bad.SourceError);
        Assert.Equal(["failed"], bad.Destinations);
        Assert.All(report.Files.Where(f => f.Path != "CLIP/F08.mov"), f => Assert.Equal(["verified"], f.Destinations));
        Assert.False(report.SafeToEject);
        Assert.False(File.Exists(Path.Combine(plan.CardPaths[0], "CLIP", "F08.mov")));
        AssertNoPartFiles(dir.Path);
    }

    // Review focus 2
    [Fact]
    public void OneDestinationBlocked_OtherDestinationComplete()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-04");
        Directory.CreateDirectory(Layout.Join(plan.CardPaths[1], "CLIP/F03.mov"));

        var report = engine.Run(plan);

        Assert.False(report.SafeToEject);
        Assert.Equal(0, report.Destinations[0].FilesFailed);
        Assert.Equal(1, report.Destinations[1].FilesFailed);
        var f = report.Files.Single(x => x.Path == "CLIP/F03.mov");
        Assert.Equal(["verified", "failed"], f.Destinations);
        Assert.StartsWith("rename", f.Errors[1]);
        Assert.Contains("dest 2: rename", report.Summary);
    }

    // Review focus 3
    [Fact]
    public void SameCardTwice_SecondGetsSuffixFirstUntouched()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var d1 = dir.Sub("d1");
        var first = new OffloadEngine(Options());
        var plan1 = first.Prepare(card, [d1], null, "2026-10-04");
        first.Run(plan1);
        var firstJson = File.ReadAllText(Path.Combine(plan1.CardPaths[0], "_REPORT", "offload_report.json"));

        var second = new OffloadEngine(Options());
        var plan2 = second.Prepare(card, [d1], null, "2026-10-04");
        Assert.Equal("A001_2", plan2.CardName);
        Assert.True(second.Run(plan2).SafeToEject);
        Assert.Equal(firstJson, File.ReadAllText(Path.Combine(plan1.CardPaths[0], "_REPORT", "offload_report.json")));
    }

    // Review focus 4
    [Fact]
    public void CzechNamesAndAmpersand_RoundTrip()
    {
        using var dir = new TempDir();
        var card = dir.Sub("Natáčení den 1");
        var a = TestFs.WriteRandom(Path.Combine(card, "Záběr 01", "klip č.1.mov"), 5000, 1);
        TestFs.WriteRandom(Path.Combine(card, "A&B.txt"), 10, 2);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        Assert.Equal("Natáčení den 1", plan.CardName);

        var report = engine.Run(plan);

        Assert.True(report.SafeToEject);
        var cardPath = plan.CardPaths[0];
        Assert.Equal(a, File.ReadAllBytes(Path.Combine(cardPath, "Záběr 01", "klip č.1.mov")));
        var list = File.ReadAllText(Path.Combine(cardPath, "_REPORT", "Natáčení den 1.xxh64"), Encoding.UTF8);
        Assert.Contains("  Záběr 01/klip č.1.mov\n", list);
        var mhl = File.ReadAllText(Directory.GetFiles(Path.Combine(cardPath, "ascmhl"), "*.mhl").Single(), Encoding.UTF8);
        Assert.Contains(">A&amp;B.txt</path>", mhl);
        Assert.Contains(">Záběr 01/klip č.1.mov</path>", mhl);
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~EngineTests"`
Expected: build FAIL — `OffloadEngine` nemá metodu `Run`.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/OffloadEngine.Run.cs`:

```csharp
namespace Offload.Core;

public sealed partial class OffloadEngine
{
    public OffloadReport Run(OffloadPlan plan)
    {
        var files = plan.Scan.Files;
        var started = DateTime.UtcNow;
        var cardPaths = plan.CardPaths;
        foreach (var path in cardPaths)
        {
            NativeIO.Mkdirs(path);
            foreach (var d in plan.Scan.Dirs) NativeIO.Mkdirs(Layout.Join(path, d));
        }
        var ws = cardPaths.Select((p, i) => new DestWriter(i, p, files.Count, Options)).ToArray();
        lock (gate)
        {
            this.plan = plan;
            writers = ws;
        }
        try
        {
            using var pool = new BufferPool(Options.BufferCount, Options.ChunkSize);
            using var hasher = new SourceHasher(files.Count);
            var sourceErrors = new string?[files.Count];

            // Copy: the card is read exactly once; every block fans out to the hasher and all destinations.
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (IsCancelled)
                {
                    sourceErrors[i] = "cancelled";
                    continue;
                }
                lock (gate)
                {
                    fileIndex = i;
                    currentFile = f.RelPath;
                }
                var failure = CopyFile(plan.Source, f, ws, hasher, pool);
                if (failure != null)
                {
                    sourceErrors[i] = failure;
                    hasher.Abort();
                    foreach (var w in ws) w.Abort(i, "source: " + failure);
                    lock (gate) errorCount++;
                }
                else
                {
                    hasher.Finish(i);
                    foreach (var w in ws) w.Finish(i, f);
                }
            }
            hasher.Wait();
            foreach (var w in ws) w.Wait();

            SetPhase(CopyPhase.Flush);
            OnEachDestination(ws, w => w.FlushDevice());
            var copyFinished = DateTime.UtcNow;

            if (Options.Verify && !IsCancelled)
            {
                SetPhase(CopyPhase.Verify);
                var hashes = hasher.Hashes;
                OnEachDestination(ws, w => w.Verify(files, hashes, cts.Token));
            }
            var finished = DateTime.UtcNow;

            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, hasher.Hashes, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled, started, copyFinished, finished);
            var reportErrors = new List<string>();
            foreach (var path in cardPaths)
            {
                try { report.Write(path, includeHashRecords: true); }
                catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { reportErrors.Add(e.Message); }
            }
            SetPhase(CopyPhase.Done);
            if (reportErrors.Count > 0) throw OffloadError.Failed("Report could not be written: " + string.Join("; ", reportErrors));
            return report;
        }
        finally
        {
            foreach (var w in ws) w.Dispose();
        }
    }

    /// Reads one file block by block and hands every block to the hasher and all writers. Returns the failure, or null.
    string? CopyFile(string sourceRoot, SourceFile f, DestWriter[] ws, SourceHasher hasher, BufferPool pool)
    {
        var src = Layout.Join(sourceRoot, f.RelPath);
        foreach (var w in ws) w.Begin(f);
        hasher.Begin();
        FileReader reader;
        try { reader = new FileReader(src); }
        catch (Exception e) { return e.Message; }

        long offset = 0;
        using (reader)
        {
            while (!IsCancelled)
            {
                var buf = pool.Take();
                int n;
                try { n = ReadWithRetries(reader, buf, offset); }
                catch (Exception e)
                {
                    pool.Give(buf);
                    return e.Message;
                }
                if (n == 0)
                {
                    pool.Give(buf);
                    break;
                }
                var chunk = new Chunk(buf, n, offset, ws.Length + 1, pool);
                hasher.Update(chunk);
                foreach (var w in ws) w.Write(chunk);
                offset += n;
                Interlocked.Add(ref readBytes, n);
                if (n < Options.ChunkSize) break;
            }
        }
        if (IsCancelled) return "cancelled";
        if (offset != f.Size) return $"source size changed during copy ({offset} != {f.Size})";
        return null;
    }

    /// Card readers have bad moments; a failed read gets a few more tries before the file is given up.
    int ReadWithRetries(FileReader reader, AlignedBuffer buf, long offset)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return reader.Read(buf.Span, offset);
            }
            catch (IOError e)
            {
                if (attempt >= Options.ReadRetries) throw new IOException($"{e.Message} (after {Options.ReadRetries} retries)", e);
                Thread.Sleep(250);
            }
        }
    }

    /// One thread per destination, so every disk works at its own speed.
    static void OnEachDestination(DestWriter[] ws, Action<DestWriter> work)
    {
        var errors = new List<Exception>();
        var threads = ws.Select(w => new Thread(() =>
        {
            try { work(w); }
            catch (Exception e) { lock (errors) errors.Add(e); }
        }) { IsBackground = true }).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        if (errors.Count > 0) throw OffloadError.Failed(string.Join("; ", errors.Select(e => e.Message)));
    }
}
```

- [ ] **Step 4: Odstranit případný `#pragma warning disable` z `OffloadEngine.cs` (Task 11 Step 3)**

- [ ] **Step 5: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~EngineTests"`
Expected: PASS (11 testů).

- [ ] **Step 6: Spustit všechny testy**

Run: `dotnet test`
Expected: PASS, 0 selhání.

- [ ] **Step 7: Commit**

```powershell
git add windows
git commit -m "Windows: offload engine — single card read fanned out to all disks, verify, report" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Verifier

**Files:**
- Create: `windows/src/Offload.Core/Verifier.cs`
- Test: `windows/tests/Offload.Core.Tests/VerifierTests.cs`

**Interfaces:**
- Consumes: `Hashing.HashFile`, `Scanner.Scan`, `Layout`, `Mhl.DirName`, `OffloadError`.
- Produces: `Verifier.Verify(string cardPath, int chunkSize = 16 << 20, Action<string, long>? progress = null, CancellationToken ct = default) -> Verifier.Result` (`Ok`, `Mismatched`, `Missing`, `Unreadable`, `Extra` jako `List<string>`, `Bytes`, `Success`).

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/VerifierTests.cs`:

```csharp
using System.IO.Hashing;

namespace Offload.Core.Tests;

public class VerifierTests
{
    /// Card folder with three files and a hash list, as an offload leaves it.
    static string MakeOffload(TempDir dir, string newline = "\n")
    {
        var card = dir.Sub("2026-10-04", "A001");
        var lines = new List<string>();
        foreach (var (rel, seed) in new[] { ("CLIP/a.mov", 1), ("CLIP/b.mov", 2), ("c.xml", 3) })
        {
            var data = TestFs.WriteRandom(Layout.Join(card, rel), 10_000 + seed, seed);
            lines.Add($"{Format.Hex64(XxHash64.HashToUInt64(data))}  {rel}");
        }
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "A001.xxh64"), string.Join(newline, lines) + newline);
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report.txt"), "x");
        Directory.CreateDirectory(Path.Combine(card, "ascmhl"));
        File.WriteAllText(Path.Combine(card, "ascmhl", "x.mhl"), "x");
        return card;
    }

    [Fact]
    public void IntactCopy_IsOk()
    {
        using var dir = new TempDir();
        var r = Verifier.Verify(MakeOffload(dir), chunkSize: 4096);
        Assert.True(r.Success);
        Assert.Equal(3, r.Ok.Count);
        Assert.Empty(r.Extra);
        Assert.Equal(30_006, r.Bytes);
    }

    [Fact]
    public void WindowsLineEndings_AreAccepted()
    {
        using var dir = new TempDir();
        Assert.True(Verifier.Verify(MakeOffload(dir, "\r\n"), chunkSize: 4096).Success);
    }

    [Fact]
    public void FindsMismatchMissingAndExtra()
    {
        using var dir = new TempDir();
        var card = MakeOffload(dir);
        var b = Layout.Join(card, "CLIP/b.mov");
        var bytes = File.ReadAllBytes(b);
        bytes[0] ^= 1;
        File.WriteAllBytes(b, bytes);
        File.Delete(Layout.Join(card, "c.xml"));
        File.WriteAllText(Layout.Join(card, "CLIP/new.mov"), "x");

        var r = Verifier.Verify(card, chunkSize: 4096);

        Assert.False(r.Success);
        Assert.Equal(["CLIP/a.mov"], r.Ok);
        Assert.Equal(["CLIP/b.mov"], r.Mismatched);
        Assert.Equal(["c.xml"], r.Missing);
        Assert.Equal(["CLIP/new.mov"], r.Extra);
    }

    [Fact]
    public void NoHashList_IsPreflightError()
    {
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() => Verifier.Verify(dir.Sub("card")));
        Assert.Contains("No .xxh64 hash list", e.Message);
    }

    [Fact]
    public void ReverifiesWhatTheEngineWrote()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "x.mov"), 100_000, 7);
        var engine = new OffloadEngine(new OffloadOptions { AllowSourceVolume = true });
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        engine.Run(plan);
        var r = Verifier.Verify(plan.CardPaths[0]);
        Assert.True(r.Success);
        Assert.Equal(["x.mov"], r.Ok);
        Assert.Empty(r.Extra);
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~VerifierTests"`
Expected: build FAIL — `Verifier` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Verifier.cs`:

```csharp
using System.Text;

namespace Offload.Core;

/// Re-verifies an existing offload against the hash list in its _REPORT folder (also one written by the Mac version).
public static class Verifier
{
    public sealed class Result
    {
        public List<string> Ok { get; } = [];
        public List<string> Mismatched { get; } = [];
        public List<string> Missing { get; } = [];
        public List<string> Unreadable { get; } = [];
        public List<string> Extra { get; } = [];
        public long Bytes { get; set; }
        public bool Success => Mismatched.Count == 0 && Missing.Count == 0 && Unreadable.Count == 0;
    }

    public static Result Verify(string cardPath, int chunkSize = 16 << 20, Action<string, long>? progress = null, CancellationToken ct = default)
    {
        var reportDir = Path.Combine(cardPath, Layout.ReportDirName);
        var list = Directory.Exists(reportDir) ? Directory.GetFiles(reportDir, "*.xxh64").FirstOrDefault() : null;
        if (list == null) throw OffloadError.Preflight($"No .xxh64 hash list in {reportDir}");

        var expected = new List<(string Hash, string Path)>();
        foreach (var raw in File.ReadAllText(list, Encoding.UTF8).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int sep = line.IndexOf("  ", StringComparison.Ordinal);
            if (line.Length == 0 || sep < 0) continue;
            expected.Add((line[..sep], line[(sep + 2)..]));
        }

        var result = new Result();
        using var buf = new AlignedBuffer(chunkSize);
        foreach (var (hash, rel) in expected)
        {
            ct.ThrowIfCancellationRequested();
            var path = Layout.Join(cardPath, rel);
            if (!File.Exists(path))
            {
                result.Missing.Add(rel);
                continue;
            }
            try
            {
                var (h, _) = Hashing.HashFile(path, buf, n =>
                {
                    result.Bytes += n;
                    progress?.Invoke(rel, result.Bytes);
                }, ct);
                (Format.Hex64(h) == hash ? result.Ok : result.Mismatched).Add(rel);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                result.Unreadable.Add($"{rel}: {e.Message}");
            }
        }

        var known = expected.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] ours = [Layout.ReportDirName + "/", Layout.ProxyDirName + "/", Mhl.DirName + "/"];
        try
        {
            result.Extra.AddRange(Scanner.Scan(cardPath).Files.Select(f => f.RelPath)
                .Where(p => !known.Contains(p) && !ours.Any(o => p.StartsWith(o, StringComparison.OrdinalIgnoreCase))));
        }
        catch (IOError)
        {
            // Extra files are informational; an unreadable folder is already reported above.
        }
        return result;
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~VerifierTests"`
Expected: PASS (5 testů).

- [ ] **Step 5: Commit**

```powershell
git add windows
git commit -m "Windows: re-verify an offload against its hash list" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: OffloadJob

**Files:**
- Create: `windows/src/Offload.Core/OffloadJob.cs`
- Test: `windows/tests/Offload.Core.Tests/JobTests.cs`

**Interfaces:**
- Consumes: `OffloadEngine`, `OffloadOptions`, `NativeIO.KeepAwake`, `ProxySettings`.
- Produces:
  - `JobConfig(string source, IReadOnlyList<string> destRoots)` — `Source`, `DestRoots`, `CardName`, `Date`, `Verify` (true), `ChunkMB` (16), `Buffers` (16), `AllowSameDevice`, `AnalyzeClips` (true), `Proxy` (`ProxySettings`).
  - `enum JobPhase { Waiting, Copying, Flushing, Verifying, Reporting, Analyzing, Proxies, Done, Failed, Cancelled }`, `Label()` (česky), `IsFinished()`.
  - `record JobStatus` — `Phase`, `Copy`, `ClipIndex`, `ClipCount`, `ClipName`, `ClipFraction`, `ProxiesDone`, `ProxiesFailed`, `SafeToEject`, `CopySucceeded`, `Error`.
  - `OffloadJob(JobConfig)` (+ internal `(JobConfig, OffloadOptions)` pro testy) — `Config`, `Engine`, `Plan`, `Report`, `Prepare() -> OffloadPlan`, `Cancel()`, `IsCancelled`, `Status() -> JobStatus`, `Run() -> OffloadReport?`, `RunCopy() -> bool`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/JobTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class JobTests
{
    static OffloadOptions Options() => new() { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    [Fact]
    public void Run_Success_EndsDoneAndSafeToEject()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 100_000, 1);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-04" }, Options());
        Assert.Equal(JobPhase.Waiting, job.Status().Phase);

        var report = job.Run();

        Assert.NotNull(report);
        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.True(s.CopySucceeded);
        Assert.Equal(100_000, s.Copy!.ReadBytes);
        Assert.Equal("Hotovo", s.Phase.Label());
    }

    [Fact]
    public void Run_PreflightFailure_EndsFailedWithMessage()
    {
        using var dir = new TempDir();
        var job = new OffloadJob(new JobConfig(Path.Combine(dir.Path, "nope"), [dir.Sub("d1")]), Options());
        Assert.Null(job.Run());
        var s = job.Status();
        Assert.Equal(JobPhase.Failed, s.Phase);
        Assert.Contains("Source not found", s.Error);
        Assert.True(s.Phase.IsFinished());
    }

    [Fact]
    public void Cancel_BeforeRun_EndsCancelled()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 1000, 1);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]), Options());
        job.Cancel();
        job.Run();
        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);
        Assert.False(job.Status().SafeToEject);
    }

    [Fact]
    public void Config_MapsToEngineOptions()
    {
        var job = new OffloadJob(new JobConfig(@"E:\", [@"D:\"]) { ChunkMB = 8, Buffers = 4, Verify = false, AllowSameDevice = true });
        Assert.Equal(8 << 20, job.Engine.Options.ChunkSize);
        Assert.Equal(4, job.Engine.Options.BufferCount);
        Assert.False(job.Engine.Options.Verify);
        Assert.True(job.Engine.Options.AllowSameDevice);
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~JobTests"`
Expected: build FAIL — `OffloadJob`, `JobConfig`, `JobPhase` neexistují.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/OffloadJob.cs`:

```csharp
namespace Offload.Core;

public sealed class JobConfig(string source, IReadOnlyList<string> destRoots)
{
    public string Source { get; set; } = source;
    public IReadOnlyList<string> DestRoots { get; set; } = destRoots;
    public string? CardName { get; set; }
    public string? Date { get; set; }
    public bool Verify { get; set; } = true;
    public int ChunkMB { get; set; } = 16;
    public int Buffers { get; set; } = 16;
    public bool AllowSameDevice { get; set; }
    /// Probe clips and put thumbnails + metadata into the report (stage 2, plan 2).
    public bool AnalyzeClips { get; set; } = true;
    public ProxySettings Proxy { get; set; } = new();
}

public enum JobPhase { Waiting, Copying, Flushing, Verifying, Reporting, Analyzing, Proxies, Done, Failed, Cancelled }

public static class JobPhaseExtensions
{
    public static string Label(this JobPhase p) => p switch
    {
        JobPhase.Waiting => "Čeká",
        JobPhase.Copying => "Kopírování",
        JobPhase.Flushing => "Zápis cache disků",
        JobPhase.Verifying => "Ověřování",
        JobPhase.Reporting => "Report",
        JobPhase.Analyzing => "Analýza klipů",
        JobPhase.Proxies => "Render proxy",
        JobPhase.Done => "Hotovo",
        JobPhase.Failed => "Chyba",
        _ => "Zrušeno",
    };

    public static bool IsFinished(this JobPhase p) => p is JobPhase.Done or JobPhase.Failed or JobPhase.Cancelled;
}

public sealed record JobStatus
{
    public JobPhase Phase { get; init; } = JobPhase.Waiting;
    public ProgressSnapshot? Copy { get; init; }
    public int ClipIndex { get; init; }
    public int ClipCount { get; init; }
    public string ClipName { get; init; } = "";
    public double ClipFraction { get; init; }
    public int ProxiesDone { get; init; }
    public int ProxiesFailed { get; init; }
    /// All files are on all destinations and verified — the card may be ejected even while proxies render.
    public bool SafeToEject { get; init; }
    public bool? CopySucceeded { get; init; }
    public string? Error { get; init; }
}

/// One card: copy → verify → report. Used by both the CLI and the app. (Plan 2 adds stage 2: clips and proxies.)
public sealed class OffloadJob
{
    readonly Lock gate = new();
    JobStatus state = new();

    public JobConfig Config { get; }
    public OffloadEngine Engine { get; }
    public OffloadPlan? Plan { get; private set; }
    public OffloadReport? Report { get; private set; }

    public OffloadJob(JobConfig config)
        : this(config, new OffloadOptions
        {
            ChunkSize = config.ChunkMB << 20,
            BufferCount = config.Buffers,
            Verify = config.Verify,
            AllowSameDevice = config.AllowSameDevice,
        })
    {
    }

    internal OffloadJob(JobConfig config, OffloadOptions options)
    {
        Config = config;
        Engine = new OffloadEngine(options);
    }

    public OffloadPlan Prepare() => Plan ??= Engine.Prepare(Config.Source, Config.DestRoots, Config.CardName, Config.Date);

    public void Cancel() => Engine.Cancel();

    public bool IsCancelled => Engine.IsCancelled;

    public JobStatus Status()
    {
        JobStatus s;
        lock (gate) s = state;
        if (s.Phase is JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying or JobPhase.Reporting)
        {
            var snap = Engine.Snapshot();
            s = s with
            {
                Copy = snap,
                Phase = snap.Phase switch
                {
                    CopyPhase.Copy => JobPhase.Copying,
                    CopyPhase.Flush => JobPhase.Flushing,
                    CopyPhase.Verify => JobPhase.Verifying,
                    _ => JobPhase.Reporting,
                },
            };
        }
        return s;
    }

    void Update(Func<JobStatus, JobStatus> change)
    {
        lock (gate) state = change(state);
    }

    /// Runs the whole job synchronously. Never throws: the outcome is in Status() and the returned report.
    public OffloadReport? Run()
    {
        RunCopy();
        return Report;
    }

    /// Stage 1 — card → disks + verification. Returns true when stage 2 should follow (always false until plan 2).
    /// After this the card is no longer needed, so the app can start the next card while stage 2 runs.
    public bool RunCopy()
    {
        using var awake = NativeIO.KeepAwake();
        try
        {
            var plan = Prepare();
            Update(s => s with { Phase = JobPhase.Copying });
            var report = Engine.Run(plan);
            Report = report;
            Update(s => s with
            {
                SafeToEject = report.SafeToEject,
                CopySucceeded = report.Success,
                Copy = Engine.Snapshot(),
                Phase = IsCancelled ? JobPhase.Cancelled : JobPhase.Done,
            });
            return false;
        }
        catch (Exception e) when (e is OffloadError or IOError or IOException or UnauthorizedAccessException)
        {
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
            return false;
        }
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~JobTests"`
Expected: PASS (4 testy).

- [ ] **Step 5: Commit**

```powershell
git add windows
git commit -m "Windows: offload job with status for CLI and app" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Bench

**Files:**
- Create: `windows/src/Offload.Core/Bench.cs`
- Test: `windows/tests/Offload.Core.Tests/BenchTests.cs`

**Interfaces:**
- Consumes: `AlignedBuffer`, `FileReader`, `NativeIO`, `Hashing`, `Scanner`, `Layout`, `Format.Rate`.
- Produces: `Bench.Line(string Label, long Bytes, double Seconds)` s `Rate`; `Bench.Read(string path, long limit, IReadOnlyList<int> blockSizes) -> IReadOnlyList<Line>`, `Bench.Write(string dir, long size, int blockSize) -> IReadOnlyList<Line>`, `Bench.Hash(int size) -> Line`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/BenchTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class BenchTests
{
    [Fact]
    public void Write_MeasuresWriteAndReadBack_LeavesNothing()
    {
        using var dir = new TempDir();
        var lines = Bench.Write(dir.Path, (1 << 20) + 5, 64 * 1024);
        Assert.Equal(["write 64 KB", "read  64 KB + xxh64"], lines.Select(l => l.Label));
        Assert.All(lines, l => Assert.Equal((1 << 20) + 5, l.Bytes));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Read_ReadsUpToLimitPerBlockSize()
    {
        using var dir = new TempDir();
        TestFs.WriteRandom(Path.Combine(dir.Path, "a.bin"), 300_000, 1);
        TestFs.WriteRandom(Path.Combine(dir.Path, "b.bin"), 100_000, 2);
        var lines = Bench.Read(dir.Path, 350_000, [64 * 1024, 1 << 20]);
        Assert.Equal(["read  64 KB", "read  1 MB"], lines.Select(l => l.Label));
        Assert.All(lines, l => Assert.InRange(l.Bytes, 350_000, 400_000));
    }

    [Fact]
    public void Hash_ReportsEightPasses() => Assert.Equal(8L * (1 << 20), Bench.Hash(1 << 20).Bytes);
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~BenchTests"`
Expected: build FAIL — `Bench` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Bench.cs`:

```csharp
using System.Diagnostics;
using System.IO.Hashing;

namespace Offload.Core;

/// Measures what the hardware can really do, so the engine settings rest on numbers, not guesses.
public static class Bench
{
    public sealed record Line(string Label, long Bytes, double Seconds)
    {
        public string Rate => Format.Rate(Bytes, Seconds);
    }

    /// Sequential read of existing files (largest first) at several block sizes, bypassing the file cache.
    public static IReadOnlyList<Line> Read(string path, long limit, IReadOnlyList<int> blockSizes)
    {
        List<string> targets;
        if (Directory.Exists(path))
            targets = Scanner.Scan(path).Files.OrderByDescending(f => f.Size).Select(f => Layout.Join(path, f.RelPath)).ToList();
        else if (File.Exists(path))
            targets = [path];
        else
            throw new IOError("open", path, 2);
        if (targets.Count == 0) throw OffloadError.Preflight($"No files to read in {path}");

        var lines = new List<Line>();
        foreach (var bs in blockSizes)
        {
            using var buf = new AlignedBuffer(bs);
            long total = 0;
            var sw = Stopwatch.StartNew();
            foreach (var file in targets)
            {
                using var reader = new FileReader(file);
                long off = 0;
                while (total < limit)
                {
                    int n = reader.Read(buf.Span, off);
                    if (n == 0) break;
                    off += n;
                    total += n;
                    if (n < bs) break;
                }
                if (total >= limit) break;
            }
            lines.Add(new Line($"read  {BlockLabel(bs)}", total, sw.Elapsed.TotalSeconds));
        }
        return lines;
    }

    /// Writes a temporary file (unbuffered, flushed to the media), reads it back, deletes it.
    public static IReadOnlyList<Line> Write(string dir, long size, int blockSize)
    {
        var path = Path.Combine(dir, $".offload-bench-{Environment.ProcessId}");
        using var buf = new AlignedBuffer(blockSize);
        Random.Shared.NextBytes(buf.Span);
        try
        {
            var sw = Stopwatch.StartNew();
            using (var h = NativeIO.OpenWrite(path, unbuffered: true, size))
            {
                for (long off = 0; off < size; off += blockSize)
                {
                    int n = (int)Math.Min(blockSize, size - off);
                    int padded = (n + AlignedBuffer.Alignment - 1) / AlignedBuffer.Alignment * AlignedBuffer.Alignment;
                    NativeIO.Write(h, buf.Span[..padded], off, path);
                }
                NativeIO.SetLength(h, size, path);
                NativeIO.Flush(h, path);
            }
            var write = new Line($"write {BlockLabel(blockSize)}", size, sw.Elapsed.TotalSeconds);
            sw.Restart();
            var (_, read) = Hashing.HashFile(path, buf);
            return [write, new Line($"read  {BlockLabel(blockSize)} + xxh64", read, sw.Elapsed.TotalSeconds)];
        }
        finally
        {
            NativeIO.Delete(path);
        }
    }

    /// Raw hashing throughput from memory — must be far above any disk, otherwise it is the bottleneck.
    public static Line Hash(int size)
    {
        using var buf = new AlignedBuffer(size);
        Random.Shared.NextBytes(buf.Span);
        var sw = Stopwatch.StartNew();
        var xx = new XxHash64();
        for (int i = 0; i < 8; i++) xx.Append(buf.Span);
        xx.GetCurrentHashAsUInt64();
        return new Line("xxh64 in RAM", (long)size * 8, sw.Elapsed.TotalSeconds);
    }

    internal static string BlockLabel(int bs) => bs >= 1 << 20 ? $"{bs >> 20} MB" : $"{bs >> 10} KB";
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~BenchTests"`
Expected: PASS (3 testy).

- [ ] **Step 5: Commit**

```powershell
git add windows
git commit -m "Windows: bench for card read, disk write and hashing speed" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 16: CLI `offload.exe`

**Files:**
- Modify: `windows/src/Offload.Cli/Program.cs` (nahradit celý)
- Create: `windows/src/Offload.Cli/ConsoleLine.cs`, `ProgressDisplay.cs`, `CopyCommand.cs`, `VerifyCommand.cs`, `BenchCommand.cs`

**Interfaces:**
- Consumes: `OffloadJob`, `JobConfig`, `JobPhase`, `OffloadReport`, `Verifier`, `Bench`, `Format`, `Layout`, `OffloadError`, `IOError`.
- Produces: `offload.exe copy|verify|bench` se stejnými volbami jako Mac (bez voleb proxy/analýzy — přidá plán 2); `copy` je výchozí příkaz; exit 0 = vše hotovo a ověřeno, jinak 1.

- [ ] **Step 1: Pomocníci konzole**

`windows/src/Offload.Cli/ConsoleLine.cs`:

```csharp
namespace Offload.Cli;

/// One status line on stderr that rewrites itself; silent when stderr goes to a file.
static class ConsoleLine
{
    public static void Write(string text)
    {
        if (Console.IsErrorRedirected) return;
        try
        {
            int width = Math.Max(20, Console.WindowWidth - 1);
            Console.Error.Write("\r" + (text.Length > width ? text[..width] : text.PadRight(width)));
        }
        catch (IOException)
        {
            // No console window (e.g. started from a service): progress is optional.
        }
    }

    public static void Clear()
    {
        Write("");
        if (!Console.IsErrorRedirected) Console.Error.Write("\r");
    }
}
```

`windows/src/Offload.Cli/ProgressDisplay.cs`:

```csharp
using System.Globalization;
using Offload.Core;

namespace Offload.Cli;

/// Status line refreshed twice a second while a job runs.
sealed class ProgressDisplay : IDisposable
{
    readonly OffloadJob job;
    readonly Timer timer;
    readonly Lock gate = new();
    DateTime lastTime = DateTime.UtcNow;
    long lastRead;
    long[] lastWritten = [];
    long[] lastVerified = [];
    JobPhase? lastPhase;

    public ProgressDisplay(OffloadJob job)
    {
        this.job = job;
        timer = new Timer(_ => Tick(), null, 500, 500);
    }

    public void Dispose()
    {
        using (var done = new ManualResetEvent(false))
        {
            timer.Dispose(done);
            done.WaitOne();
        }
        lock (gate) ConsoleLine.Clear();
    }

    void Tick()
    {
        lock (gate)
        {
            var st = job.Status();
            var now = DateTime.UtcNow;
            double dt = Math.Max(0.001, (now - lastTime).TotalSeconds);
            if (st.Phase != lastPhase)
            {
                if (lastPhase != null && !Console.IsErrorRedirected) Console.Error.WriteLine();
                lastPhase = st.Phase;
            }
            string line;
            if (st.Copy is { } s && st.Phase is JobPhase.Copying or JobPhase.Verifying or JobPhase.Flushing or JobPhase.Reporting)
            {
                if (lastWritten.Length != s.WrittenBytes.Length)
                {
                    lastWritten = s.WrittenBytes;
                    lastVerified = s.VerifiedBytes;
                }
                string Rate(long current, long before) =>
                    ((current - before) / dt / 1_000_000).ToString("0", CultureInfo.InvariantCulture).PadLeft(4);
                switch (st.Phase)
                {
                    case JobPhase.Copying:
                        double pct = s.TotalBytes > 0 ? s.ReadBytes * 100.0 / s.TotalBytes : 0;
                        var dests = string.Join("  ", s.WrittenBytes.Select((b, i) => $"D{i + 1} {Rate(b, lastWritten[i])}"));
                        line = $"copy {s.FileIndex + 1}/{s.TotalFiles}  {Format.Bytes(s.ReadBytes)} / {Format.Bytes(s.TotalBytes)}  "
                             + $"{pct,3:0}%  card {Rate(s.ReadBytes, lastRead)} MB/s  {dests} MB/s  {Path.GetFileName(s.CurrentFile)}";
                        break;
                    case JobPhase.Verifying:
                        line = "verify  " + string.Join("  ", s.VerifiedBytes.Select((b, i) =>
                            $"D{i + 1} {b * 100.0 / Math.Max(s.TotalBytes, 1),3:0}% {Rate(b, lastVerified[i])} MB/s"));
                        break;
                    default:
                        line = st.Phase.ToString().ToLowerInvariant() + "…";
                        break;
                }
                if (s.Errors > 0) line += $"  ERRORS: {s.Errors}";
                lastRead = s.ReadBytes;
                lastWritten = s.WrittenBytes;
                lastVerified = s.VerifiedBytes;
            }
            else
            {
                line = st.Phase.ToString().ToLowerInvariant();
            }
            ConsoleLine.Write(line);
            lastTime = now;
        }
    }
}
```

- [ ] **Step 2: Příkazy**

`windows/src/Offload.Cli/CopyCommand.cs`:

```csharp
using System.CommandLine;
using Offload.Core;

namespace Offload.Cli;

static class CopyCommand
{
    public static Command Create()
    {
        var source = new Argument<string>("source") { Description = "Card drive or source folder." };
        var dest = new Option<string[]>("--dest", "-d") { Description = "Destination root (repeat for each disk).", Required = true };
        var name = new Option<string?>("--name") { Description = "Card folder name (default: volume label)." };
        var date = new Option<string?>("--date") { Description = "Date folder (default: today, YYYY-MM-DD)." };
        var chunkMb = new Option<int>("--chunk-mb") { Description = "Block size in MB.", DefaultValueFactory = _ => 16 };
        var buffers = new Option<int>("--buffers") { Description = "Blocks in flight.", DefaultValueFactory = _ => 16 };
        var noVerify = new Option<bool>("--no-verify") { Description = "Skip read-back verification (not recommended)." };
        var allowSame = new Option<bool>("--allow-same-device") { Description = "Allow two destinations on the same volume." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Only scan and check, do not copy." };

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun }) cmd.Options.Add(o);
        cmd.SetAction(r =>
        {
            var config = new JobConfig(Path.GetFullPath(r.GetValue(source)!), r.GetValue(dest)!.Select(Path.GetFullPath).ToList())
            {
                CardName = r.GetValue(name),
                Date = r.GetValue(date),
                ChunkMB = r.GetValue(chunkMb),
                Buffers = r.GetValue(buffers),
                Verify = !r.GetValue(noVerify),
                AllowSameDevice = r.GetValue(allowSame),
            };
            return Run(config, r.GetValue(dryRun));
        });
        return cmd;
    }

    static int Run(JobConfig config, bool dryRun)
    {
        var job = new OffloadJob(config);
        OffloadPlan plan;
        try
        {
            plan = job.Prepare();
        }
        catch (Exception e) when (e is OffloadError or IOError)
        {
            Console.Error.WriteLine($"Error: {e.Message}");
            return 1;
        }
        Console.WriteLine($"Card {plan.CardName}: {plan.Scan.Files.Count} files, {Format.Bytes(plan.Scan.TotalBytes)}");
        foreach (var p in plan.CardPaths) Console.WriteLine($"  → {p}");
        if (dryRun) return 0;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine("\nCancelling — finishing current block, partial files will be removed…");
            job.Cancel();
        };

        OffloadReport? report;
        using (new ProgressDisplay(job)) report = job.Run();

        var status = job.Status();
        if (report != null)
        {
            Console.WriteLine();
            Console.Write(report.Summary);
            Console.WriteLine($"Report: {Path.Combine(plan.CardPaths[0], Layout.ReportDirName, plan.CardName + "_report.html")}");
        }
        if (status.Error != null) Console.WriteLine($"ERROR: {status.Error}");
        return status.Phase == JobPhase.Done && report?.Success == true ? 0 : 1;
    }
}
```

`windows/src/Offload.Cli/VerifyCommand.cs`:

```csharp
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using Offload.Core;

namespace Offload.Cli;

static class VerifyCommand
{
    public static Command Create()
    {
        var cardPath = new Argument<string>("cardPath") { Description = "Card folder (<root>/<date>/<card>)." };
        var cmd = new Command("verify", "Re-verify an existing card folder against its _REPORT hash list.");
        cmd.Arguments.Add(cardPath);
        cmd.SetAction(r => Run(Path.GetFullPath(r.GetValue(cardPath)!)));
        return cmd;
    }

    static int Run(string cardPath)
    {
        var sw = Stopwatch.StartNew();
        var last = TimeSpan.Zero;
        Verifier.Result r;
        try
        {
            r = Verifier.Verify(cardPath, progress: (file, bytes) =>
            {
                if (sw.Elapsed - last < TimeSpan.FromMilliseconds(500)) return;
                last = sw.Elapsed;
                ConsoleLine.Write($"{Format.Bytes(bytes)}  {file}");
            });
        }
        catch (OffloadError e)
        {
            Console.Error.WriteLine($"Error: {e.Message}");
            return 1;
        }
        ConsoleLine.Clear();
        double secs = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"Verified {r.Ok.Count} files, {Format.Bytes(r.Bytes)} in {secs.ToString("0.0", CultureInfo.InvariantCulture)} s ({Format.Rate(r.Bytes, secs)})");
        foreach (var m in r.Mismatched) Console.WriteLine($"MISMATCH  {m}");
        foreach (var m in r.Missing) Console.WriteLine($"MISSING   {m}");
        foreach (var m in r.Unreadable) Console.WriteLine($"ERROR     {m}");
        foreach (var m in r.Extra) Console.WriteLine($"EXTRA     {m} (not in hash list)");
        Console.WriteLine(r.Success ? "OK" : "FAILED");
        return r.Success ? 0 : 1;
    }
}
```

`windows/src/Offload.Cli/BenchCommand.cs`:

```csharp
using System.CommandLine;
using Offload.Core;

namespace Offload.Cli;

static class BenchCommand
{
    public static Command Create()
    {
        var read = new Option<string?>("--read") { Description = "Card or file to read from." };
        var write = new Option<string[]>("--write") { Description = "Destination folder to test write speed (repeatable).", DefaultValueFactory = _ => [] };
        var gb = new Option<double>("--gb") { Description = "Amount of data per test in GB.", DefaultValueFactory = _ => 2 };
        var cmd = new Command("bench", "Measure card read speed and disk write/read speed.");
        cmd.Options.Add(read);
        cmd.Options.Add(write);
        cmd.Options.Add(gb);
        cmd.SetAction(r =>
        {
            long bytes = (long)(r.GetValue(gb) * 1_000_000_000);
            try
            {
                Console.WriteLine("CPU");
                Print(Bench.Hash(256 << 20));
                if (r.GetValue(read) is string rd)
                {
                    Console.WriteLine($"Read {rd}");
                    foreach (var l in Bench.Read(Path.GetFullPath(rd), bytes, [64 << 10, 1 << 20, 4 << 20, 16 << 20])) Print(l);
                }
                foreach (var w in r.GetValue(write) ?? [])
                {
                    Console.WriteLine($"Write {w}");
                    foreach (var l in Bench.Write(Path.GetFullPath(w), bytes, 16 << 20)) Print(l);
                }
                return 0;
            }
            catch (Exception e) when (e is IOError or OffloadError)
            {
                Console.Error.WriteLine($"Error: {e.Message}");
                return 1;
            }
        });
        return cmd;
    }

    static void Print(Bench.Line l) => Console.WriteLine($"  {l.Label,-22} {Format.Bytes(l.Bytes),-10} {l.Rate}");
}
```

`windows/src/Offload.Cli/Program.cs` (celý obsah nahradit):

```csharp
using System.CommandLine;
using System.Text;

namespace Offload.Cli;

static class Program
{
    static readonly string[] Commands = ["copy", "verify", "bench"];

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Like the Mac CLI, `copy` is the default command: `offload E:\ -d D:\ -d F:\` works.
        if (args.Length > 0 && !Commands.Contains(args[0]) && !args[0].StartsWith('-')) args = ["copy", .. args];
        var root = new RootCommand("Camera card offload: read once, write to several disks, verify with xxh64.");
        root.Subcommands.Add(CopyCommand.Create());
        root.Subcommands.Add(VerifyCommand.Create());
        root.Subcommands.Add(BenchCommand.Create());
        return root.Parse(args).Invoke();
    }
}
```

- [ ] **Step 3: Sestavit**

Run: `dotnet build`
Expected: `Build succeeded`, 0 varování.

- [ ] **Step 4: Ruční ověření CLI na zkušebních datech (C: → D: a E:)**

```powershell
$src = "$env:TEMP\offload-e2e\A001"
New-Item -ItemType Directory -Force "$src\CLIP" | Out-Null
foreach ($i in 1..5) { $b = New-Object byte[] (20MB + $i); (New-Object Random $i).NextBytes($b); [IO.File]::WriteAllBytes("$src\CLIP\A001C00$i.mov", $b) }
New-Item -ItemType Directory -Force D:\offload-e2e, E:\offload-e2e | Out-Null
$exe = "src\Offload.Cli\bin\Debug\net10.0-windows\offload.exe"
& $exe --version
& $exe $src -d D:\offload-e2e -d E:\offload-e2e --date 2026-10-04 --dry-run; "exit $LASTEXITCODE"
& $exe $src -d D:\offload-e2e -d E:\offload-e2e --date 2026-10-04; "exit $LASTEXITCODE"
& $exe verify D:\offload-e2e\2026-10-04\A001; "exit $LASTEXITCODE"
& $exe copy $src -d D:\offload-e2e -d D:\offload-e2e; "exit $LASTEXITCODE"
```

Expected:
- `--version` → `0.1.0-win`.
- dry-run → `Card A001: 5 files, 104.86 MB`, dvě řádky `→ …\2026-10-04\A001`, `exit 0`.
- copy → průběh, souhrn `OFFLOAD REPORT — OK`, `Verify:` s rychlostí, dva řádky `Destination: … ok 5  failed 0` (D: `[NTFS]`, E: `[exFAT]`), `exit 0`.
- verify → `Verified 5 files, 104.86 MB …`, `OK`, `exit 0`.
- poslední příkaz → `Error: Destinations share a volume …`, `exit 1`.

- [ ] **Step 5: Zrušení přes Ctrl+C**

Spustit `& $exe $src -d D:\offload-e2e -d E:\offload-e2e --chunk-mb 1` a během kopírování stisknout Ctrl+C.
Expected: hláška `Cancelling — …`, souhrn `FAILED`, `exit 1`; `Get-ChildItem D:\offload-e2e,E:\offload-e2e -Recurse -Force -Filter *.offload-part` nevrátí nic.

- [ ] **Step 6: Commit**

```powershell
git add windows
git commit -m "Windows: offload CLI — copy, verify, bench" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 17: Build, kompatibilita s ASC MHL a ostrý test

**Files:**
- Create: `windows/scripts/build.ps1`, `windows/README.md`

**Interfaces:**
- Consumes: `Offload.Cli` projekt.
- Produces: `windows\build\Offload\bin\offload.exe` (self-contained, single file).

- [ ] **Step 1: Build skript**

`windows/scripts/build.ps1`:

```powershell
# Builds windows\build\Offload: bin\offload.exe (self-contained, single file).
# Plan 2 adds bin\ffmpeg.exe + ffprobe.exe, plan 4 the GUI Offload.exe next to bin\.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'build\Offload'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'src\Offload.Cli\Offload.Cli.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:DebugType=none -o (Join-Path $out 'bin')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Write-Host "built $out"
```

- [ ] **Step 2: Sestavit a spustit**

Run: `powershell -ExecutionPolicy Bypass -File scripts\build.ps1; & build\Offload\bin\offload.exe --version`
Expected: `built …\windows\build\Offload`, pak `0.1.0-win`. Ve `build\Offload\bin` je jen `offload.exe`.

- [ ] **Step 3: ASC MHL kompatibilita**

```powershell
python -m pip install --user ascmhl
python -m ascmhl verify D:\offload-e2e\2026-10-04\A001
```

Expected: ascmhl ověří všech 5 souborů bez chyb (exit 0; žádné „mismatch“, „missing“ ani „new file“). Pokud ascmhl hlásí strukturu MHL jako neplatnou, porovnat výstup s Mac `mac/Sources/OffloadKit/MHL.swift` a opravit `Mhl.cs` + test v `MhlTests` dřív, než se pokračuje.

- [ ] **Step 4: Ostrý test na skutečné kartě (s uživatelem)**

Uživatel vloží kartu z kamery (čtečka) — např. jako `F:\`. Pak:

```powershell
& build\Offload\bin\offload.exe F:\ -d D:\offload-e2e -d E:\offload-e2e
& build\Offload\bin\offload.exe verify "E:\offload-e2e\$(Get-Date -Format yyyy-MM-dd)\<jmenovka karty>"
python -m ascmhl verify "E:\offload-e2e\$(Get-Date -Format yyyy-MM-dd)\<jmenovka karty>"
```

Expected: složka karty pojmenovaná podle jmenovky svazku, `OFFLOAD REPORT — OK`, verify `OK`, ascmhl bez chyb; na kartě se nic nezměnilo (`Get-ChildItem F:\ -Recurse | Measure-Object` před a po stejné). Výsledek (rychlosti karty a disků) zapsat do `windows/README.md`.

- [ ] **Step 5: README**

`windows/README.md`:

```markdown
# Offload pro Windows

Port Mac aplikace Offload (viz `../README.md`) na Windows 10/11 x64, .NET 10.
Kopie karty na více disků najednou, ověření zpětným čtením (xxHash64), report a ASC MHL —
ve stejném formátu jako Mac verze, takže kopii z Windows ověří Mac a naopak.

## Stav

| Část | Stav |
|---|---|
| Jádro + CLI (`bin\offload.exe`) | hotovo (plán 1) |
| Analýza klipů, proxy, RAW, PDF | plán 2 |
| braw-decode | plán 3 |
| GUI `Offload.exe` | plán 4 |

## Build a testy

```powershell
cd windows
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\build.ps1   # → build\Offload\bin\offload.exe
```

## Použití

```powershell
offload E:\ -d D:\Zaloha -d F:\Zaloha          # karta E: na dva disky, ověření, report
offload verify D:\Zaloha\2026-10-04\A001        # znovu ověřit hotovou kopii
offload bench --read E:\ --write D:\ --gb 2     # rychlost karty a disků
```

Na Windows se čte i zapisuje mimo systémovou cache (`FILE_FLAG_NO_BUFFERING`), po zápisu
`FlushFileBuffers`; ověření tak čte data z disku, ne z RAM. Svazky, které to neumí (některé síťové),
přejdou na běžný zápis a report to uvede.

## Naměřeno

(doplnit po ostrém testu: karta, čtečka, disky, MB/s)
```

Po ostrém testu nahradit řádek „(doplnit …)“ skutečnými hodnotami z Step 4 (např. „SD UHS-II v USB čtečce → D: NTFS + E: exFAT USB: kopie 210 MB/s, ověření 380 MB/s“).

- [ ] **Step 6: Úklid testovacích dat**

```powershell
Remove-Item -Recurse -Force D:\offload-e2e, E:\offload-e2e, "$env:TEMP\offload-e2e"
```

- [ ] **Step 7: Všechny testy a commit**

Run: `dotnet test`
Expected: PASS, 0 selhání.

```powershell
git add windows
git commit -m "Windows: build script, README, verified against ascmhl and a real card" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
