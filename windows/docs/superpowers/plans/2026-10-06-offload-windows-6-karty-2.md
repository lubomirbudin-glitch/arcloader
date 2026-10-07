# Offload pro Windows — plán 6: karty a bezpečnost dat (úkoly 10–19) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Skupina A návrhu 15 vylepšení — Offload pozná už zálohovanou kartu, umí do zálohy doplnit jen nové klipy, navrhne název karty z klipů, ověří zálohu kdykoli později proti ASC MHL, zkontroluje místo před startem a všechny hlášky kontrol jsou česky; výsledek je verze 0.2.0.

**Architecture:** Jádro dostane nové jmenné prostory `Offload.Core.History` (otisk karty, `history.json`, shoda), `Offload.Core.Verification` (čtení ASC MHL, ověření, report ověření), `Offload.Core.Append` (plán doplnění) a `Offload.Core.Space` (odhad a součet místa); engine umí doplnění (`PrepareAppend`, generace MHL N) a kontrolu místa po svazcích. Rozhodování GUI je v `Offload.Core.Desktop` a testuje se bez okna; WPF jen váže. CLI dostane `append`, nový `verify` a české hlášky.

**Tech Stack:** C# / .NET 10, WPF, xUnit, `System.IO.Hashing` (už v projektu), BCL (`IncrementalHash`, `GZipStream`, `XDocument`, pojmenovaný `Mutex`), ffmpeg jen v testech `[FfmpegFact]`, Microsoft Edge jen v testech `[EdgeFact]`.

**Spec:** `windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md` — skupina A (§2.1–2.6), průřezové věci (§5), plán 6 (§6.1). Toto je druhá část plánu; úkoly 1–9, Review Focus a rozhodnutí tam, kde návrh nestačil, jsou v `windows/docs/superpowers/plans/2026-10-06-offload-windows-6-karty.md` (platí i pro tento soubor).

## Global Constraints

- Žádný nový NuGet balíček — jen BCL .NET 10 (`System.Security.Cryptography`, `System.IO.Compression.GZipStream`, `System.Xml.Linq`), `System.IO.Hashing` už je, P/Invoke.
- Texty UI česky, doslova podle návrhu; identifikátory v kódu anglicky. Logy nástrojů (`app.log`, stderr ffmpeg/braw-decode), TXT report (sdílený s Macem) a `--help` zůstávají anglicky (§2.6).
- UI vlákno nikdy nečeká na disk ani síť — historie, otisk karty, kontrola místa, ověření: vše `Task.Run` + výsledek přes dispatcher, jako dnes `RefreshLut` / `DestinationViewModel.Probe`.
- Na kartu se nikdy nezapisuje — otisk karty, návrh názvu i doplnění kartu jen čtou (výčet složek, `FileReader`).
- Formáty na disku čitelné Macem: nové soubory a klíče jsou jen **přidané** (Swift `Codable` neznámé klíče ignoruje); popis do `docs/FORMATY.md` (úkol 18).
- Testovatelné bez kamer: dočasné složky, syntetické klipy z ffmpeg `testsrc2`, ručně psané MHL/JSON, falešné volné místo (`OffloadOptions.FreeSpace`, úkol 16).
- Mac později: `mac/` se nemění; co má Mac převzít, jde do `docs/VYVOJ-MAC.md` „Naplánováno“ (úkol 18).
- GUI se v testech nikdy nespouští (`Offload.exe` ne). Logika oken je v `Offload.Core.Desktop` a testuje se tam; WPF části se ověřují `dotnet build` (XAML se kompiluje) a ručním testem (úkol 19).
- R15: burn-in beze změny (výchozí zapnuto). R16: MD5 jen navíc k xxh64 — to je plán 7; v plánu 6 se MD5 jen čte při ověření zálohy (R9).
- Verze: plán 6 → 0.2.0 (`0.2.0-win`, R22), nastaví úkol 18.
- `TreatWarningsAsErrors`: každý úkol končí `dotnet build` bez varování a celou zelenou sadou (`cd windows` → `dotnet test`; dnes 558 testů, po plánu ~760). `ProxyWatchdogTests` je časově citlivý — když pod zátěží celé sady jednou selže, spusťte ho znovu samostatně (`dotnet test --filter "FullyQualifiedName~ProxyWatchdogTests"`).
- Testy nikdy nezapíšou do skutečného `%APPDATA%\Offload`: od úkolu 5 `TestEnvironment` (module initializer) přesměruje `HistoryStore.Default` do `%TEMP%\offload-tests\history-<pid>`.
- Každý commit: řádek nahoře v sekci `## 2026-10-06 — plán 6: karty a bezpečnost dat` v `docs/VYVOJ-WINDOWS.md` (sekci založí úkol 1 nad sekcí `## 2026-10-06 — balíček ZIP`; nejnovější řádek nahoře, bez SHA); poslední řádek zprávy commitu přesně `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Bloky „najít / nahradit“ uvádějí řádky podle stavu souboru **před** daným úkolem (u prvního zásahu do souboru = dnešní kód, commit `9e4026d`). Kód plánu byl postavený a otestovaný v kopii repa (všech 17 kódových úkolů se zkompiluje, celá sada zelená). Pracovní kopie má konce řádků CRLF (`core.autocrlf=true`), bloky v plánu LF — nástroj Edit je sjednotí; kdo upravuje skriptem, porovnává bez ohledu na `\r`.

## Review Focus

Stejných pět bodů jako v první části (testy, které je drží, jsou v úkolech obou souborů):

1. **Karta s 10 000+ soubory a hlubokým stromem, vytažená uprostřed skenu** → úkol 6 `CardInspectorTests`, úkol 7 `CardInspectionsTests`.
2. **FAT32 karta po změně letního/zimního času** → úkol 3 `FileTimes_*`, úkol 6 `Fat32_HourShift_IsTheSameCard`, úkol 13 `FatHourShift_IsNoConflict_ButOnExFatItIs`.
3. **Doplnění do složky, jejíž MHL vytvořil Mac / jiný nástroj, má víc generací nebo se cíle liší** → úkol 10 `TwoGenerations_NewestWins_EveryHashType`, úkol 13 `MacBackup_Fixture`, `DifferentGenerations_Diverged`, úkol 14 `Generation2_OnBothDestinations_EarlierFilesUntouched`, `CancelledAppend_RecordsWhatIsDone_NoHistory_NextAppendCopiesTheRest`.
4. **`history.json` poškozený, ze zrušené úlohy nebo zapisovaný zároveň GUI a CLI** → úkol 4 `HistoryStoreTests`, úkol 5 `Cancelled_IsNotRecorded`, úkol 14 (zrušené doplnění historii nezmění).
5. **Místo: dvě karty do fronty na tentýž disk + běžící úloha + složka pro proxy na stejném svazku** → úkol 16 `SameVolume_DestinationsAndProxyFolder_AddUp`, úkol 17 `TwoCards_RunningJob_ProxyFolder_OnOneVolume`.

---

### Task 10: `MhlReader` + `HashAlgo`

Spec §2.4 „Jádro“ (R9), §6.1 úkol 10.

**Files:**
- Modify: `windows/src/Offload.Core/Mhl.cs` (řádky 82–88)
- Create: `windows/src/Offload.Core/Verification/HashAlgo.cs`
- Create: `windows/src/Offload.Core/Verification/MhlReader.cs`
- Create: `windows/tests/Offload.Core.Tests/MhlReaderTests.cs`

**Interfaces:**
- Produces (`namespace Offload.Core.Verification`): `interface IFileHash { void Append(ReadOnlySpan<byte>); string Finish(); }`; `HashAlgo.Names` (`xxh64, md5, sha1, xxh3, xxh128, c4`), `IsKnown(name)`, `Create(name) → IFileHash`, `Of(name, ReadOnlySpan<byte>) → string`, `Same(name, a, b) → bool` (hex bez ohledu na velikost písmen, c4 přesně), `Label(name)` („xxHash64“, „MD5“ …); `record MhlHash(string Algorithm, string Value, string? Action)`, `record MhlFileRecord(string Path, long? Size, DateTime? Modified, IReadOnlyList<MhlHash> Hashes, int Generation)` s `Failed`, `record MhlGeneration(int Seq, string FileName, IReadOnlyList<string> Ignore, IReadOnlyList<MhlFileRecord> Files)`, `record MhlChainEntry(int Seq, string Path, string C4)`, `MhlHistory` (`Generations`, `Files` — nejnovější generace na cestu, `Ignore`, `Chain`, `ChainProblems`); `MhlReader.Read(string cardPath) → MhlHistory?`, `MhlReader.ReadChain(string ascmhlDir, List<string>? problems = null) → IReadOnlyList<MhlChainEntry>?`; `Mhl.C4FromSha512(ReadOnlySpan<byte>)` (interní).

- [ ] **Step 1: Padající testy**

```csharp
using System.Text;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Hand-written ASC MHL generations — what ascmhl, Silverstack or ShotPut leave, and an append (generation 2).
public static class MhlFixtures
{
    /// One generation file `NNNN_<card>_….mhl` in `<card>/ascmhl`; `files`: (path, size, algorithm → value, action).
    public static string Generation(string cardPath, int seq, IEnumerable<(string Path, long Size, (string Algo, string Value)[] Hashes, string Action)> files,
        string[]? ignore = null)
    {
        var x = new StringBuilder();
        x.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<hashlist version=\"2.0\" xmlns=\"urn:ASC:MHL:v2.0\">\n");
        x.Append("  <creatorinfo><creationdate>2026-10-06T10:00:00+02:00</creationdate><hostname>MAC</hostname><tool version=\"1.0\">ascmhl</tool></creatorinfo>\n");
        x.Append("  <processinfo><process>transfer</process><ignore>");
        foreach (var p in ignore ?? [".DS_Store", "ascmhl", "ascmhl/"]) x.Append($"<pattern>{p}</pattern>");
        x.Append("</ignore></processinfo>\n  <hashes>\n");
        foreach (var f in files)
        {
            x.Append($"    <hash><path size=\"{f.Size}\" lastmodificationdate=\"2026-10-06T09:00:00+02:00\">{f.Path}</path>");
            foreach (var (algo, value) in f.Hashes) x.Append($"<{algo} action=\"{f.Action}\" hashdate=\"2026-10-06T10:00:00+02:00\">{value}</{algo}>");
            x.Append("</hash>\n");
        }
        x.Append("    <directoryhash><path>CLIP</path><content><xxh64>0000000000000000</xxh64></content></directoryhash>\n");
        x.Append("  </hashes>\n</hashlist>\n");
        var dir = Path.Combine(cardPath, "ascmhl");
        Directory.CreateDirectory(dir);
        var name = $"{seq:0000}_{Path.GetFileName(cardPath)}_2026-10-06_080000Z.mhl";
        File.WriteAllText(Path.Combine(dir, name), x.ToString(), new UTF8Encoding(false));
        return name;
    }

    /// ascmhl_chain.xml listing `generations` (file names) with their real C4 ids.
    public static void Chain(string cardPath, params string[] generations)
    {
        var dir = Path.Combine(cardPath, "ascmhl");
        var x = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ascmhldirectory xmlns=\"urn:ASC:MHL:DIRECTORY:v2.0\">\n");
        for (int i = 0; i < generations.Length; i++)
            x.Append($"  <hashlist sequencenr=\"{i + 1}\">\n    <path>{generations[i]}</path>\n    <c4>{Mhl.C4(File.ReadAllBytes(Path.Combine(dir, generations[i])))}</c4>\n  </hashlist>\n");
        x.Append("</ascmhldirectory>\n");
        File.WriteAllText(Path.Combine(dir, "ascmhl_chain.xml"), x.ToString(), new UTF8Encoding(false));
    }
}

public class MhlReaderTests
{
    [Fact]
    public void OurOwnGeneration()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        Mhl.Write(Reports.Sample(card, destError: "boom"), card, new DateTime(2026, 10, 4, 10, 30, 0, DateTimeKind.Local), "pc");

        var h = MhlReader.Read(card)!;

        Assert.Empty(h.ChainProblems);
        var g = Assert.Single(h.Generations);
        Assert.Equal(1, g.Seq);
        Assert.Contains("_REPORT", h.Ignore);
        Assert.Contains(".DS_Store", h.Ignore);
        var a = h.Files["CLIP/A&B.mov"];
        Assert.Equal(1000, a.Size);
        Assert.Equal(new MhlHash("xxh64", "0123456789abcdef", "original"), Assert.Single(a.Hashes));
        Assert.False(a.Failed);
        Assert.True(h.Files["CLIP/c 02.mov"].Failed);
        Assert.Single(h.Chain);
    }

    [Fact]
    public void TwoGenerations_NewestWins_EveryHashType()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var g1 = MhlFixtures.Generation(card, 1, [("CLIP/a.mov", 10, [("xxh64", "AAAAAAAAAAAAAAAA")], "original")]);
        var g2 = MhlFixtures.Generation(card, 2,
        [
            ("CLIP/a.mov", 11, [("xxh64", "bbbbbbbbbbbbbbbb")], "original"),
            ("CLIP/b.mov", 20, [("md5", "900150983cd24fb0d6963f7d28e17f72"), ("sha1", "a9993e364706816aba3e25717850c26c9cd0d89d"),
                                ("xxh128", "06b05ab6733a618578af5f94892f3950"), ("xxh3", "78af5f94892f3950")], "original"),
        ]);
        MhlFixtures.Chain(card, g1, g2);

        var h = MhlReader.Read(card)!;

        Assert.Empty(h.ChainProblems);
        Assert.Equal([1, 2], h.Generations.Select(g => g.Seq));
        Assert.Equal((11L, 2), (h.Files["CLIP/a.mov"].Size!.Value, h.Files["CLIP/a.mov"].Generation));
        Assert.Equal(["md5", "sha1", "xxh128", "xxh3"], h.Files["CLIP/b.mov"].Hashes.Select(x => x.Algorithm));
        Assert.Equal(new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc), h.Files["CLIP/b.mov"].Modified);
        Assert.Equal([1, 2], h.Chain.Select(c => c.Seq));
    }

    [Fact]
    public void ChangedGeneration_IsAChainProblem()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var g1 = MhlFixtures.Generation(card, 1, [("CLIP/a.mov", 10, [("xxh64", "aaaaaaaaaaaaaaaa")], "original")]);
        var g2 = MhlFixtures.Generation(card, 2, [("CLIP/b.mov", 10, [("xxh64", "bbbbbbbbbbbbbbbb")], "original")]);
        MhlFixtures.Chain(card, g1, g2);
        File.AppendAllText(Path.Combine(card, "ascmhl", g2), "<!-- edited -->");

        var h = MhlReader.Read(card)!;

        Assert.Equal(["ASC MHL generace 0002 neodpovídá řetězu (soubor MHL byl změněn)."], h.ChainProblems);
        Assert.Equal(2, h.Generations.Count);   // still read: the files are verified anyway
    }

    [Fact]
    public void MissingChain_ReadsByNumber_WithAProblem()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MhlFixtures.Generation(card, 2, [("CLIP/b.mov", 10, [("xxh64", "bbbbbbbbbbbbbbbb")], "original")]);
        MhlFixtures.Generation(card, 1, [("CLIP/a.mov", 10, [("xxh64", "aaaaaaaaaaaaaaaa")], "original")]);

        var h = MhlReader.Read(card)!;

        Assert.Equal(["Chybí ascmhl_chain.xml — generace se čtou podle čísla v názvu souboru."], h.ChainProblems);
        Assert.Equal([1, 2], h.Generations.Select(g => g.Seq));
        Assert.Empty(h.Chain);
    }

    [Fact]
    public void GenerationMissing_AndUnlistedFile()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var g1 = MhlFixtures.Generation(card, 1, [("CLIP/a.mov", 10, [("xxh64", "aaaaaaaaaaaaaaaa")], "original")]);
        var g2 = MhlFixtures.Generation(card, 2, [("CLIP/b.mov", 10, [("xxh64", "bbbbbbbbbbbbbbbb")], "original")]);
        MhlFixtures.Chain(card, g1, g2);
        File.Delete(Path.Combine(card, "ascmhl", g2));
        MhlFixtures.Generation(card, 3, [("CLIP/c.mov", 10, [("xxh64", "cccccccccccccccc")], "original")]);

        var problems = MhlReader.Read(card)!.ChainProblems;

        Assert.Contains($"V řetězu ASC MHL je generace 0002, ale její soubor chybí: {g2}", problems);
        Assert.Contains(problems, p => p.StartsWith("Soubor ASC MHL není v řetězu: 0003_", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenGeneration_IsAProblem_NotACrash()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Directory.CreateDirectory(Path.Combine(card, "ascmhl"));
        File.WriteAllText(Path.Combine(card, "ascmhl", "0001_A001_x.mhl"), "<hashlist");
        var h = MhlReader.Read(card)!;
        Assert.Empty(h.Generations);
        Assert.Contains(h.ChainProblems, p => p.StartsWith("ASC MHL generaci 0001 nejde přečíst", StringComparison.Ordinal));
    }

    [Fact]
    public void NoAscmhl_IsNull()
    {
        using var dir = new TempDir();
        Assert.Null(MhlReader.Read(dir.Sub("A001")));
        Directory.CreateDirectory(Path.Combine(dir.Path, "A001", "ascmhl"));
        Assert.Null(MhlReader.Read(Path.Combine(dir.Path, "A001")));
    }
}

public class HashAlgoTests
{
    [Theory]
    [InlineData("xxh64", "", "ef46db3751d8e999")]
    [InlineData("xxh64", "abc", "44bc2cf5ad770999")]
    [InlineData("xxh3", "", "2d06800538d394c2")]
    [InlineData("xxh3", "abc", "78af5f94892f3950")]
    [InlineData("xxh128", "", "99aa06d3014798d86001c324468d497f")]
    [InlineData("xxh128", "abc", "06b05ab6733a618578af5f94892f3950")]
    [InlineData("md5", "", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("md5", "abc", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("sha1", "abc", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("c4", "", "c459dsjfscH38cYeXXYogktxf4Cd9ibshE3BHUo6a58hBXmRQdZrAkZzsWcbWtDg5oQstpDuni4Hirj75GEmTc1sFT")]
    [InlineData("c4", "abc", "c45S4rnaTNWonxss1u8LzsaJdEph1AJhWUF4sh2waXKMsutyfAxg4ybUeuXVWS9HdNcEypmeXn8FZGonD4w1rj9DZp")]
    public void Vectors(string algo, string input, string expected) => Assert.Equal(expected, HashAlgo.Of(algo, Encoding.ASCII.GetBytes(input)));

    [Fact]
    public void BlockByBlock_EqualsAtOnce()
    {
        var data = new byte[100_000];
        new Random(5).NextBytes(data);
        foreach (var algo in HashAlgo.Names)
        {
            var h = HashAlgo.Create(algo);
            for (int i = 0; i < data.Length; i += 4096) h.Append(data.AsSpan(i, Math.Min(4096, data.Length - i)));
            Assert.Equal(HashAlgo.Of(algo, data), h.Finish());
        }
    }

    [Fact]
    public void Same_HexAnyCase_C4Exact()
    {
        Assert.True(HashAlgo.Same("xxh64", "44BC2CF5AD770999", "44bc2cf5ad770999"));
        Assert.False(HashAlgo.Same("c4", "c45S4", "C45s4"));
        Assert.Equal("xxHash64", HashAlgo.Label("xxh64"));
        Assert.Equal("MD5", HashAlgo.Label("md5"));
    }
}
```

(`MhlFixtures` z tohoto souboru používají i úkoly 11, 13 a 14.)

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~MhlReaderTests|FullyQualifiedName~HashAlgoTests"` → build FAIL.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Mhl.cs`, řádky 82–88 — najít:

```csharp

    /// C4 ID: "c4" + base58(SHA-512), left-padded with "1" to 88 characters.
    internal static string C4(ReadOnlySpan<byte> data)
    {
        var n = new BigInteger(SHA512.HashData(data), isUnsigned: true, isBigEndian: true);
        var digits = new StringBuilder();
        while (n > 0)
```

nahradit:

```csharp

    /// C4 ID: "c4" + base58(SHA-512), left-padded with "1" to 88 characters.
    internal static string C4(ReadOnlySpan<byte> data) => C4FromSha512(SHA512.HashData(data));

    /// The C4 ID of data whose SHA-512 is `digest` (a file hashed block by block).
    internal static string C4FromSha512(ReadOnlySpan<byte> digest)
    {
        var n = new BigInteger(digest, isUnsigned: true, isBigEndian: true);
        var digits = new StringBuilder();
        while (n > 0)
```

```csharp
using System.IO.Hashing;
using System.Security.Cryptography;

namespace Offload.Core.Verification;

/// One running hash of a file, fed block by block.
public interface IFileHash
{
    void Append(ReadOnlySpan<byte> data);
    /// The value as ASC MHL writes it.
    string Finish();
}

/// The ASC MHL hash types the .NET class library has (R9): xxh64, md5, sha1, xxh3, xxh128, c4. Values are lower-case hex
/// of the canonical (big-endian) digest, as ascmhl writes them; c4 is "c4" + base58(SHA-512) like Mhl.C4.
public static class HashAlgo
{
    public static readonly IReadOnlyList<string> Names = ["xxh64", "md5", "sha1", "xxh3", "xxh128", "c4"];

    public static bool IsKnown(string name) => Names.Contains(name);

    public static IFileHash Create(string name) => name switch
    {
        "xxh64" => new NonCrypto(new XxHash64()),
        "xxh3" => new NonCrypto(new XxHash3()),
        "xxh128" => new NonCrypto(new XxHash128()),
        "md5" => new Crypto(IncrementalHash.CreateHash(HashAlgorithmName.MD5), c4: false),
        "sha1" => new Crypto(IncrementalHash.CreateHash(HashAlgorithmName.SHA1), c4: false),
        "c4" => new Crypto(IncrementalHash.CreateHash(HashAlgorithmName.SHA512), c4: true),
        _ => throw new ArgumentException($"unknown hash type {name}", nameof(name)),
    };

    /// The whole of `data` at once (tests, small files).
    public static string Of(string name, ReadOnlySpan<byte> data)
    {
        var h = Create(name);
        h.Append(data);
        return h.Finish();
    }

    /// Hex compares in any case; a c4 id is base58, where case matters.
    public static bool Same(string name, string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), name == "c4" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// For people: "xxHash64", "MD5", "SHA-1", "xxHash3", "xxHash128", "C4".
    public static string Label(string name) => name switch
    {
        "xxh64" => "xxHash64",
        "md5" => "MD5",
        "sha1" => "SHA-1",
        "xxh3" => "xxHash3",
        "xxh128" => "xxHash128",
        "c4" => "C4",
        _ => name,
    };

    sealed class NonCrypto(NonCryptographicHashAlgorithm hash) : IFileHash
    {
        public void Append(ReadOnlySpan<byte> data) => hash.Append(data);
        public string Finish() => Convert.ToHexStringLower(hash.GetCurrentHash());
    }

    sealed class Crypto(IncrementalHash hash, bool c4) : IFileHash
    {
        public void Append(ReadOnlySpan<byte> data) => hash.AppendData(data);

        public string Finish()
        {
            var digest = hash.GetHashAndReset();
            hash.Dispose();
            return c4 ? Mhl.C4FromSha512(digest) : Convert.ToHexStringLower(digest);
        }
    }
}
```

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Offload.Core.Verification;

/// One hash of a file in an ASC MHL generation; `Action`: "original", "verified", "failed" …
public sealed record MhlHash(string Algorithm, string Value, string? Action);

public sealed record MhlFileRecord(string Path, long? Size, DateTime? Modified, IReadOnlyList<MhlHash> Hashes, int Generation)
{
    /// The copy recorded this file as failed (action="failed").
    public bool Failed => Hashes.Any(h => h.Action == "failed");
}

public sealed record MhlGeneration(int Seq, string FileName, IReadOnlyList<string> Ignore, IReadOnlyList<MhlFileRecord> Files);

/// One `<hashlist>` of ascmhl_chain.xml.
public sealed record MhlChainEntry(int Seq, string Path, string C4);

/// Everything ascmhl/ of a card folder says (spec 2.4). `Files`: per path the newest generation that lists it.
public sealed class MhlHistory
{
    public required IReadOnlyList<MhlGeneration> Generations { get; init; }
    public required IReadOnlyDictionary<string, MhlFileRecord> Files { get; init; }
    /// The ignore patterns of every generation.
    public required IReadOnlyList<string> Ignore { get; init; }
    /// The chain as read, in order (an append writes it back with one more entry); empty when there is none.
    public required IReadOnlyList<MhlChainEntry> Chain { get; init; }
    /// Czech texts for the verify report: a changed or missing generation, a missing chain.
    public required IReadOnlyList<string> ChainProblems { get; init; }
}

public static partial class MhlReader
{
    public const string ChainFileName = "ascmhl_chain.xml";
    static readonly XNamespace Ns = "urn:ASC:MHL:v2.0";
    static readonly XNamespace DirNs = "urn:ASC:MHL:DIRECTORY:v2.0";

    [GeneratedRegex(@"^(\d+)_")]
    private static partial Regex LeadingNumber();

    static string Gen(int seq) => seq.ToString("0000", CultureInfo.InvariantCulture);

    /// Null when the card folder has no ascmhl/ or no generation in it (then a hash list is the only record).
    public static MhlHistory? Read(string cardPath)
    {
        var dir = Path.Combine(cardPath, Mhl.DirName);
        if (!Directory.Exists(dir)) return null;
        var mhlFiles = Directory.GetFiles(dir, "*.mhl").Select(f => Path.GetFileName(f)).ToList();
        var problems = new List<string>();
        var chain = ReadChain(dir, problems);
        var order = new List<(int Seq, string File)>();
        if (chain != null)
        {
            foreach (var c in chain)
            {
                var path = Path.Combine(dir, c.Path);
                if (c.Path.IndexOfAny(['/', '\\']) >= 0 || !File.Exists(path))
                {
                    problems.Add($"V řetězu ASC MHL je generace {Gen(c.Seq)}, ale její soubor chybí: {c.Path}");
                    continue;
                }
                if (Mhl.C4(File.ReadAllBytes(path)) != c.C4)
                    problems.Add($"ASC MHL generace {Gen(c.Seq)} neodpovídá řetězu (soubor MHL byl změněn).");
                order.Add((c.Seq, c.Path));
            }
            foreach (var extra in mhlFiles.Where(f => !chain.Any(c => string.Equals(c.Path, f, StringComparison.OrdinalIgnoreCase))))
                problems.Add($"Soubor ASC MHL není v řetězu: {extra}");
        }
        else if (mhlFiles.Count > 0)
        {
            problems.Add($"Chybí {ChainFileName} — generace se čtou podle čísla v názvu souboru.");
            order.AddRange(mhlFiles
                .Select(f => (Seq: LeadingNumber().Match(f) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : int.MaxValue, File: f))
                .OrderBy(x => x.Seq).ThenBy(x => x.File, StringComparer.Ordinal));
        }

        var generations = new List<MhlGeneration>();
        foreach (var (seq, file) in order)
        {
            try
            {
                generations.Add(ParseGeneration(Path.Combine(dir, file), seq));
            }
            catch (Exception e) when (e is XmlException or IOException or UnauthorizedAccessException or FormatException)
            {
                problems.Add($"ASC MHL generaci {Gen(seq)} nejde přečíst: {e.Message}");
            }
        }
        if (generations.Count == 0 && problems.Count == 0) return null;

        var files = new Dictionary<string, MhlFileRecord>(StringComparer.Ordinal);
        foreach (var g in generations)
            foreach (var f in g.Files) files[f.Path] = f;   // a later generation wins
        return new MhlHistory
        {
            Generations = generations,
            Files = files,
            Ignore = generations.SelectMany(g => g.Ignore).Distinct(StringComparer.Ordinal).ToList(),
            Chain = chain ?? [],
            ChainProblems = problems,
        };
    }

    /// ascmhl_chain.xml in `ascmhlDir`, or null when it is missing; a broken one is a problem and null.
    public static IReadOnlyList<MhlChainEntry>? ReadChain(string ascmhlDir, List<string>? problems = null)
    {
        var path = Path.Combine(ascmhlDir, ChainFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var doc = XDocument.Load(path);
            return doc.Descendants(DirNs + "hashlist")
                .Select(h => new MhlChainEntry(
                    int.Parse((string?)h.Attribute("sequencenr") ?? "0", CultureInfo.InvariantCulture),
                    ((string?)h.Element(DirNs + "path") ?? "").Trim(),
                    ((string?)h.Element(DirNs + "c4") ?? "").Trim()))
                .OrderBy(e => e.Seq)
                .ToList();
        }
        catch (Exception e) when (e is XmlException or IOException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            problems?.Add($"{ChainFileName} nejde přečíst: {e.Message}");
            return null;
        }
    }

    internal static MhlGeneration ParseGeneration(string file, int seq)
    {
        var doc = XDocument.Load(file);
        var ignore = doc.Descendants(Ns + "ignore").Elements(Ns + "pattern").Select(p => p.Value.Trim()).Where(p => p.Length > 0).ToList();
        var files = new List<MhlFileRecord>();
        foreach (var h in doc.Descendants(Ns + "hashes").Elements(Ns + "hash"))
        {
            if (h.Element(Ns + "path") is not { } p) continue;
            long? size = long.TryParse((string?)p.Attribute("size"), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;
            DateTime? modified = DateTime.TryParse((string?)p.Attribute("lastmodificationdate"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var m) ? m : null;
            var hashes = h.Elements()
                .Where(e => e.Name != Ns + "path")
                .Select(e => new MhlHash(e.Name.LocalName, e.Value.Trim(), (string?)e.Attribute("action")))
                .ToList();
            files.Add(new MhlFileRecord(p.Value.Trim().Replace('\\', '/'), size, modified, hashes, seq));
        }
        return new MhlGeneration(seq, Path.GetFileName(file), ignore, files);
    }
}
```

- [ ] **Step 4: Testy + celá sada**

Run: `dotnet test --filter "FullyQualifiedName~MhlReaderTests|FullyQualifiedName~HashAlgoTests|FullyQualifiedName~MhlTests"` → PASS (24); `dotnet test` → zelená.

- [ ] **Step 5: VYVOJ + commit**

Řádek nahoru do sekce plánu 6:

```markdown
- Úkol 10 — čtení ASC MHL: všechny generace podle řetězu (C4 každé generace; změněná, chybějící nebo nezapsaná generace je problém, chybějící řetěz = čtení podle čísla v názvu), nejnovější záznam na cestu, hashe xxh64/md5/sha1/xxh3/xxh128/c4
```

```powershell
git add windows docs
git commit -m "Windows: ASC MHL reader and hash registry" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: `BackupVerifier` + report ověření, CLI `verify`

Spec §2.4 „Data na disku“ (R8), „Jádro“, „CLI“, „Okrajové případy“, §6.1 úkol 11. Dnešní `Verifier` (jen `.xxh64`) nahradí `BackupVerifier`, který bez `ascmhl\` ověřuje stejně proti `.xxh64` (`basis: hashList`); jeho testy se přesunou do `BackupVerifierTests`.

**Files:**
- Modify: `windows/src/Offload.Cli/VerifyCommand.cs` (řádky 1–6, 10–16, 18–22, 25–46, 48–62)
- Modify: `windows/src/Offload.Core/HtmlReport.cs` (řádky 61–65, 98–102)
- Modify: `windows/src/Offload.Core/PdfReport.cs` (řádky 19–23, 28–33, 35–39)
- Create: `windows/src/Offload.Core/Verification/BackupVerifier.cs`
- Create: `windows/src/Offload.Core/Verification/VerifyReport.cs`
- Delete: `windows/src/Offload.Core/Verifier.cs`
- Create: `windows/tests/Offload.Core.Tests/BackupVerifierTests.cs`
- Delete: `windows/tests/Offload.Core.Tests/VerifierTests.cs`
- Create: `windows/tests/Offload.Core.Tests/VerifyReportTests.cs`

**Interfaces:**
- Consumes: `MhlReader`, `HashAlgo`, `MhlFixtures` (úkol 10), `Msg.NoHashRecord`, `PreflightCode.NoHashRecord` (úkol 1).
- Produces: `record VerifyProgress(int FileIndex, int FileCount, long Bytes, long TotalBytes, string CurrentFile)`, `record UnreadableFile(string Path, string Error)`, `VerifyResult` (`CardPath`, `Started`, `Finished`, `Basis` „ascmhl“/„hashList“, `Generations`, `HashAlgorithms`, `TotalFiles`, `TotalBytes`, `Ok`, `BytesRead`, `Checked`, `Changed`, `Missing`, `Unreadable`, `Extra`, `FailedAtCopy`, `ChainProblems`, `Cancelled`, `FolderGone`, `Success`); `BackupVerifier.Verify(string cardPath, Action<VerifyProgress>? progress = null, CancellationToken ct = default, int chunkSize = 16 MiB) → VerifyResult`, interní `Ignored(relPath, patterns)`; `VerifyTargets.Resolve(string folder) → IReadOnlyList<string>`; `enum VerifyTone { Ok, Warning, Error }`; `VerifyTexts.Basis/Summary/Warnings/CliSummary`; `VerifyReport.Stamp/Json/Html/Write(VerifyResult, Action<string>? log) → string htmlPath`; `HtmlReport.Styles(bool)` a `HtmlReport.DateTimeText` (interní); `PdfReport.WriteHtml(string html, string path, int timeoutSeconds = 60)`.

- [ ] **Step 1: Padající testy**

```csharp
using System.IO.Hashing;
using System.Security.Cryptography;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

public class BackupVerifierTests
{
    static OffloadOptions Options() => new() { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    /// A real offload (engine-written report, .xxh64 and ASC MHL) of three files; returns the card folder on the destination.
    static string Offloaded(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "b.mov"), 50_000, 2);
        TestFs.WriteRandom(Path.Combine(card, "c.xml"), 1_000, 3);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        engine.Run(plan);
        return plan.CardPaths[0];
    }

    [Fact]
    public void IntactCopy_AgainstAscMhl()
    {
        using var dir = new TempDir();
        var r = BackupVerifier.Verify(Offloaded(dir), chunkSize: 4096);
        Assert.True(r.Success);
        Assert.Equal("ascmhl", r.Basis);
        Assert.Equal(1, r.Generations);
        Assert.Equal(["xxh64"], r.HashAlgorithms);
        Assert.Equal((3, 3, 151_000L, 151_000L), (r.Ok, r.TotalFiles, r.TotalBytes, r.BytesRead));
        Assert.Empty(r.Extra);
        Assert.Empty(r.ChainProblems);
    }

    [Fact]
    public void ChangedByte_Missing_Extra()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        var b = Layout.Join(card, "CLIP/b.mov");
        var bytes = File.ReadAllBytes(b);
        bytes[10] ^= 1;
        File.WriteAllBytes(b, bytes);
        File.Delete(Layout.Join(card, "c.xml"));
        File.WriteAllText(Layout.Join(card, "CLIP/new.mov"), "x");
        File.WriteAllText(Layout.Join(card, "CLIP/.DS_Store"), "x");                 // ignored by the MHL
        File.WriteAllText(Path.Combine(card, "_REPORT", "notes.txt"), "x");          // ours

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.False(r.Success);
        Assert.Equal(1, r.Ok);
        Assert.Equal(["CLIP/b.mov"], r.Changed);
        Assert.Equal(["c.xml"], r.Missing);
        Assert.Equal(["CLIP/new.mov"], r.Extra);
    }

    [Fact]
    public void SizeDiffers_IsChanged_EvenWithoutReading()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        File.AppendAllText(Layout.Join(card, "c.xml"), "x");
        Assert.Equal(["c.xml"], BackupVerifier.Verify(card, chunkSize: 4096).Changed);
    }

    [Fact]
    public void FailedAtCopy_IsReportedNotHashed()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Layout.Join(card, "CLIP/a.mov"), 1000, 1);
        var g1 = MhlFixtures.Generation(card, 1,
        [
            ("CLIP/a.mov", 1000, [("xxh64", Format.Hex64(XxHash64.HashToUInt64(File.ReadAllBytes(Layout.Join(card, "CLIP/a.mov")))))], "original"),
            ("CLIP/b.mov", 2000, [("xxh64", "0000000000000000")], "failed"),
        ]);
        MhlFixtures.Chain(card, g1);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.True(r.Success);
        Assert.Equal(["CLIP/b.mov"], r.FailedAtCopy);
        Assert.Equal("1 soubor selhal už při kopii.", Assert.Single(VerifyTexts.Warnings(r)));
    }

    // R9: a Silverstack/ShotPut style MHL with md5 + sha1 + xxh128, two generations.
    [Fact]
    public void ForeignMhl_EveryHashType_TwoGenerations()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var a = TestFs.WriteRandom(Layout.Join(card, "CLIP/a.mov"), 5000, 1);
        var b = TestFs.WriteRandom(Layout.Join(card, "CLIP/b.mov"), 7000, 2);
        var g1 = MhlFixtures.Generation(card, 1, [("CLIP/a.mov", 5000, [("md5", Convert.ToHexStringLower(MD5.HashData(a))), ("sha1", Convert.ToHexStringLower(SHA1.HashData(a)))], "original")]);
        var g2 = MhlFixtures.Generation(card, 2, [("CLIP/b.mov", 7000, [("xxh128", HashAlgo.Of("xxh128", b)), ("c4", Mhl.C4(b))], "original")]);
        MhlFixtures.Chain(card, g1, g2);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.True(r.Success);
        Assert.Equal(2, r.Ok);
        Assert.Equal(2, r.Generations);
        Assert.Equal(["md5", "sha1", "xxh128", "c4"], r.HashAlgorithms);
        Assert.Equal("Ověřeno: všechny 2 soubory sedí (ASC MHL, 2 generace, MD5 + SHA-1 + xxHash128 + C4).", VerifyTexts.Summary(r).Text);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void HashListFallback_WithoutAscmhl(string newline)
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        var lines = new List<string>();
        foreach (var (rel, seed) in new[] { ("CLIP/a.mov", 1), ("c.xml", 3) })
        {
            var data = TestFs.WriteRandom(Layout.Join(card, rel), 10_000 + seed, seed);
            lines.Add($"{Format.Hex64(XxHash64.HashToUInt64(data))}  {rel}");
        }
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "A001.xxh64"), string.Join(newline, lines) + newline);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.True(r.Success);
        Assert.Equal(("hashList", 2), (r.Basis, r.Ok));
        Assert.Equal("Ověřeno: všechny 2 soubory sedí (seznam součtů, xxHash64).", VerifyTexts.Summary(r).Text);
    }

    [Fact]
    public void NoRecord_IsPreflightError()
    {
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() => BackupVerifier.Verify(dir.Sub("card")));
        Assert.Equal(PreflightCode.NoHashRecord, e.Code);
    }

    [Fact]
    public void Cancelled_StopsAndSaysWhere()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        using var cts = new CancellationTokenSource();
        var r = BackupVerifier.Verify(card, p => { if (p.FileIndex == 1) cts.Cancel(); }, cts.Token, chunkSize: 4096);
        Assert.True(r.Cancelled);
        Assert.False(r.Success);
        Assert.Equal(1, r.Checked);
        Assert.Empty(r.Extra);   // not looked for after a cancel
        Assert.Equal(("Ověření zrušeno po 1 / 3 souborech", VerifyTone.Warning), VerifyTexts.Summary(r));
    }

    [Fact]
    public void ChainProblem_IsAWarning_FilesStillVerified()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        var mhl = Directory.GetFiles(Path.Combine(card, "ascmhl"), "*.mhl").Single();
        File.AppendAllText(mhl, "<!-- x -->");
        var r = BackupVerifier.Verify(card, chunkSize: 4096);
        Assert.True(r.Success);
        Assert.Contains("ASC MHL generace 0001 neodpovídá řetězu (soubor MHL byl změněn).", VerifyTexts.Warnings(r));
    }

    [Theory]
    [InlineData("CLIP/.DS_Store", true)]
    [InlineData("CLIP/x.tmp", true)]
    [InlineData("tmp/x.mov", false)]
    [InlineData("cache/a/b.mov", true)]
    [InlineData("CLIP/a.mov", false)]
    public void IgnorePatterns(string path, bool ignored) =>
        Assert.Equal(ignored, BackupVerifier.Ignored(path, [".DS_Store", "*.tmp", "cache/", "ascmhl/"]));

    [Fact]
    public void Targets_CardItself_OrEveryCardOfADay()
    {
        using var dir = new TempDir();
        var day = dir.Sub("2026-10-06");
        Directory.CreateDirectory(Path.Combine(day, "A001", "ascmhl"));
        Directory.CreateDirectory(Path.Combine(day, "A002", "_REPORT"));
        File.WriteAllText(Path.Combine(day, "A002", "_REPORT", "A002.xxh64"), "");
        Directory.CreateDirectory(Path.Combine(day, "_REPORT"));   // the day report (plan 7) — not a card
        File.WriteAllText(Path.Combine(day, "_REPORT", "2026-10-06_day_report.html"), "");
        Directory.CreateDirectory(Path.Combine(day, "notes"));
        Assert.Equal([Path.Combine(day, "A001"), Path.Combine(day, "A002")], VerifyTargets.Resolve(day));
        Assert.Equal([Path.Combine(day, "A001")], VerifyTargets.Resolve(Path.Combine(day, "A001")));
        Assert.Empty(VerifyTargets.Resolve(Path.Combine(day, "notes")));
    }
}
```

```csharp
using System.Text.Json;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

public class VerifyReportTests
{
    static VerifyResult Sample(string cardPath)
    {
        var r = new VerifyResult
        {
            CardPath = cardPath,
            Started = new DateTime(2026, 10, 6, 8, 15, 0, DateTimeKind.Utc),
            Basis = "ascmhl",
            Generations = 2,
            HashAlgorithms = ["xxh64"],
            TotalFiles = 5,
            TotalBytes = 5000,
            Ok = 2,
            Checked = 5,
            Finished = new DateTime(2026, 10, 6, 8, 16, 0, DateTimeKind.Utc),
        };
        r.Changed.Add("CLIP/b.mov");
        r.Missing.Add("CLIP/c.mov");
        r.Unreadable.Add(new UnreadableFile("CLIP/d.mov", "read failed"));
        r.Extra.Add("CLIP/new.mov");
        r.ChainProblems.Add("ASC MHL generace 0002 neodpovídá řetězu (soubor MHL byl změněn).");
        return r;
    }

    [Fact]
    public void Json_ShapeOfSpec24()
    {
        var json = VerifyReport.Json(Sample(@"D:\Zaloha\2026-10-06\A001"));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var keys = root.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        Assert.Equal(["basis", "cardPath", "chainProblems", "changed", "extra", "failedAtCopy", "finished", "generations", "hashAlgorithms",
            "missing", "ok", "started", "success", "tool", "totalBytes", "totalFiles", "unreadable", "version"], keys);
        Assert.Equal("2026-10-06T08:15:00Z", root.GetProperty("started").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("CLIP/d.mov", root.GetProperty("unreadable")[0].GetProperty("path").GetString());
        Assert.Equal("read failed", root.GetProperty("unreadable")[0].GetProperty("error").GetString());
        Assert.Equal(OffloadVersion.String, root.GetProperty("version").GetString());
        Assert.DoesNotContain("\r", json);
    }

    [Fact]
    public void Html_CzechTexts()
    {
        var html = VerifyReport.Html(Sample(@"D:\Zaloha\2026-10-06\A001"));
        Assert.Contains("<h1>Ověření A001</h1>", html);
        Assert.Contains("NALEZENY PROBLÉMY", html);
        Assert.Contains("Nesedí 1 soubor, chybí 1, nečitelný 1 — podrobnosti v reportu ověření.", html);
        Assert.Contains("1 soubor navíc (nejsou v MHL).", html);
        Assert.Contains("Chybí (1)", html);
        Assert.Contains("CLIP/d.mov — read failed", html);
    }

    [Fact]
    public void CliSummary_OkAndProblems()
    {
        var ok = new VerifyResult { CardPath = "x", Basis = "ascmhl", Started = DateTime.UtcNow.AddSeconds(-10), Finished = DateTime.UtcNow, Checked = 12, Ok = 12, BytesRead = 512_000_000_000 };
        Assert.StartsWith("Ověřeno 12 souborů (512 GB) za 0:10 (", VerifyTexts.CliSummary(ok));
        Assert.EndsWith(" — vše v pořádku.", VerifyTexts.CliSummary(ok));
        Assert.EndsWith(" — NALEZENY PROBLÉMY: nesedí 1, chybí 1, nečitelné 1, navíc 1.", VerifyTexts.CliSummary(Sample("x")));
    }

    // R8: the report goes into _REPORT, never into ascmhl\.
    [Fact]
    public void Write_IntoReportFolderOnly()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-06", "A001");
        Directory.CreateDirectory(Path.Combine(card, "ascmhl"));
        var html = VerifyReport.Write(Sample(card));
        Assert.Equal(Path.Combine(card, "_REPORT", "A001_verify_2026-10-06_081500Z.html"), html);
        Assert.True(File.Exists(Path.Combine(card, "_REPORT", "offload_verify_2026-10-06_081500Z.json")));
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "ascmhl")));
    }

    [EdgeFact]
    public void Write_Pdf()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-06", "A001");
        VerifyReport.Write(Sample(card));
        var pdf = Path.Combine(card, "_REPORT", "A001_verify_2026-10-06_081500Z.pdf");
        Assert.True(File.Exists(pdf));
        Assert.Equal("%PDF"u8.ToArray(), File.ReadAllBytes(pdf)[..4]);
    }
}
```

Smazat `windows/tests/Offload.Core.Tests/VerifierTests.cs` (jeho případy — neporušená kopie, `\r\n`, nesedí/chybí/navíc, bez záznamu, kopie z enginu — pokrývá `BackupVerifierTests`).

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~BackupVerifierTests|FullyQualifiedName~VerifyReportTests"` → build FAIL.

- [ ] **Step 3: Implementace**

Sdílené CSS reportu a tisk libovolného HTML do PDF:

`windows/src/Offload.Core/HtmlReport.cs`, řádky 61–65 — najít:

```csharp
    }

    static string DateTimeText(DateTime d) => d.ToLocalTime().ToString("d. M. yyyy HH:mm:ss", Cs);
    static string TimeText(DateTime d) => d.ToLocalTime().ToString("HH:mm:ss", Inv);

```

nahradit:

```csharp
    }

    /// The report's CSS (screen with dark mode, or print) — shared with the verification report.
    internal static string Styles(bool forPrint) => RootCss + "\n" + (forPrint ? PrintCss : DarkCss) + "\n" + BaseCss;

    internal static string DateTimeText(DateTime d) => d.ToLocalTime().ToString("d. M. yyyy HH:mm:ss", Cs);
    static string TimeText(DateTime d) => d.ToLocalTime().ToString("HH:mm:ss", Inv);

```

`windows/src/Offload.Core/HtmlReport.cs`, řádky 98–102 — najít:

```csharp
        h.Append("<!doctype html>\n<html lang=\"cs\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Offload {Esc(r.CardName)} – {Esc(r.Date)}</title>\n<style>\n");
        h.Append(RootCss).Append('\n').Append(forPrint ? PrintCss : DarkCss).Append('\n').Append(BaseCss);
        h.Append("</style></head><body><main>\n");
        h.Append($"<header><h1>{Esc(r.CardName)}</h1><span class=\"badge {statusClass}\">{status}</span>\n");
```

nahradit:

```csharp
        h.Append("<!doctype html>\n<html lang=\"cs\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Offload {Esc(r.CardName)} – {Esc(r.Date)}</title>\n<style>\n");
        h.Append(Styles(forPrint));
        h.Append("</style></head><body><main>\n");
        h.Append($"<header><h1>{Esc(r.CardName)}</h1><span class=\"badge {statusClass}\">{status}</span>\n");
```
`windows/src/Offload.Core/PdfReport.cs`, řádky 19–23 — najít:

```csharp
    }

    public static void Write(OffloadReport report, string path, int timeoutSeconds = 60)
    {
        var edge = FindEdge() ?? throw new ToolError("Edge", -1, "Microsoft Edge not found — PDF skipped");
```

nahradit:

```csharp
    }

    public static void Write(OffloadReport report, string path, int timeoutSeconds = 60) =>
        WriteHtml(HtmlReport.Render(report, forPrint: true), path, timeoutSeconds);

    /// Any self-contained HTML page (the offload report, the verification report) printed to `path`.
    public static void WriteHtml(string html, string path, int timeoutSeconds = 60)
    {
        var edge = FindEdge() ?? throw new ToolError("Edge", -1, "Microsoft Edge not found — PDF skipped");
```

`windows/src/Offload.Core/PdfReport.cs`, řádky 28–33 — najít:

```csharp
        {
            // A file, not a data URL: the report with thumbnails is easily several MB.
            var html = Path.Combine(work, "report.html");
            File.WriteAllText(html, HtmlReport.Render(report, forPrint: true), new UTF8Encoding(false));
            NativeIO.Delete(tmp);
            using var p = new ToolProcess(edge,
```

nahradit:

```csharp
        {
            // A file, not a data URL: the report with thumbnails is easily several MB.
            var page = Path.Combine(work, "report.html");
            File.WriteAllText(page, html, new UTF8Encoding(false));
            NativeIO.Delete(tmp);
            using var p = new ToolProcess(edge,
```

`windows/src/Offload.Core/PdfReport.cs`, řádky 35–39 — najít:

```csharp
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                $"--user-data-dir={Path.Combine(work, "profile")}",  // never touches the user's own Edge profile
                "--no-pdf-header-footer", $"--print-to-pdf={tmp}", new Uri(html).AbsoluteUri,
            ], work, pipeStdin: false);
            p.Start(_ => { });
```

nahradit:

```csharp
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                $"--user-data-dir={Path.Combine(work, "profile")}",  // never touches the user's own Edge profile
                "--no-pdf-header-footer", $"--print-to-pdf={tmp}", new Uri(page).AbsoluteUri,
            ], work, pipeStdin: false);
            p.Start(_ => { });
```

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace Offload.Core.Verification;

public sealed record VerifyProgress(int FileIndex, int FileCount, long Bytes, long TotalBytes, string CurrentFile);

public sealed record UnreadableFile(string Path, string Error);

/// The outcome of re-verifying one card folder (spec 2.4). Paths are relative with "/".
public sealed class VerifyResult
{
    public required string CardPath { get; init; }
    public DateTime Started { get; init; }
    public DateTime Finished { get; set; }
    /// "ascmhl" (every generation of ascmhl/) or "hashList" (only `_REPORT\<card>.xxh64` / `.md5`).
    public required string Basis { get; init; }
    public int Generations { get; init; }
    public IReadOnlyList<string> HashAlgorithms { get; init; } = [];
    public int TotalFiles { get; init; }
    public long TotalBytes { get; init; }
    public int Ok { get; set; }
    /// Bytes read and hashed so far.
    public long BytesRead { get; set; }
    /// Files looked at (a cancelled run says "po 120 / 1 234 souborech").
    public int Checked { get; set; }
    public List<string> Changed { get; } = [];
    public List<string> Missing { get; } = [];
    public List<UnreadableFile> Unreadable { get; } = [];
    public List<string> Extra { get; } = [];
    public List<string> FailedAtCopy { get; } = [];
    public List<string> ChainProblems { get; } = [];
    public bool Cancelled { get; set; }
    /// The card folder itself vanished during the run (a disconnected disk).
    public bool FolderGone { get; set; }

    public bool Success => !Cancelled && Changed.Count == 0 && Missing.Count == 0 && Unreadable.Count == 0;
}

/// Re-reads a card folder on a destination and compares every file with its ASC MHL (all generations, every hash type
/// R9) — or, without ascmhl/, with the hash lists in _REPORT. Writes nothing (R8): the report is a separate step.
public static class BackupVerifier
{
    sealed record Expected(string Path, long? Size, IReadOnlyList<MhlHash> Hashes, bool Failed);

    static readonly string[] Ours = [Layout.ReportDirName + "/", Layout.ProxyDirName + "/", Mhl.DirName + "/"];

    /// Every file is read once, without the file cache, through all hashes the record has for it. `ct` cancels between
    /// blocks: the result then has Cancelled = true (nothing is thrown). Throws OffloadError (NoHashRecord) when the
    /// folder has neither ASC MHL nor a hash list.
    public static VerifyResult Verify(string cardPath, Action<VerifyProgress>? progress = null, CancellationToken ct = default, int chunkSize = 16 << 20)
    {
        var started = DateTime.UtcNow;
        var mhl = MhlReader.Read(cardPath);
        List<Expected> expected;
        IReadOnlyList<string> ignore;
        string basis;
        if (mhl is { Generations.Count: > 0 })
        {
            expected = mhl.Files.Values.OrderBy(f => f.Path, StringComparer.Ordinal)
                .Select(f => new Expected(f.Path, f.Size, f.Hashes.Where(h => HashAlgo.IsKnown(h.Algorithm)).ToList(), f.Failed)).ToList();
            ignore = mhl.Ignore;
            basis = "ascmhl";
        }
        else
        {
            expected = ReadHashLists(cardPath) ?? throw OffloadError.Preflight(PreflightCode.NoHashRecord, Msg.NoHashRecord(cardPath));
            ignore = [];
            basis = "hashList";
        }

        long total = expected.Sum(e => e.Size ?? SizeOnDisk(Layout.Join(cardPath, e.Path)));
        var result = new VerifyResult
        {
            CardPath = cardPath,
            Started = started,
            Basis = basis,
            Generations = basis == "ascmhl" ? mhl!.Generations.Count : 0,
            HashAlgorithms = expected.SelectMany(e => e.Hashes.Select(h => h.Algorithm)).Distinct().OrderBy(a => HashAlgo.Names.ToList().IndexOf(a)).ToList(),
            TotalFiles = expected.Count,
            TotalBytes = total,
        };
        result.ChainProblems.AddRange(mhl?.ChainProblems ?? []);

        using var buf = new AlignedBuffer(chunkSize);
        for (int i = 0; i < expected.Count; i++)
        {
            var e = expected[i];
            if (ct.IsCancellationRequested)
            {
                result.Cancelled = true;
                break;
            }
            result.Checked = i;
            progress?.Invoke(new VerifyProgress(i, expected.Count, result.BytesRead, total, e.Path));
            if (e.Failed)
            {
                result.FailedAtCopy.Add(e.Path);
                continue;
            }
            var path = Layout.Join(cardPath, e.Path);
            if (!File.Exists(path))
            {
                result.Missing.Add(e.Path);
                continue;
            }
            if (e.Hashes.Count == 0)
            {
                result.Unreadable.Add(new UnreadableFile(e.Path, "záznam nemá žádný podporovaný kontrolní součet"));
                continue;
            }
            try
            {
                var hashers = e.Hashes.Select(h => HashAlgo.Create(h.Algorithm)).ToList();
                long size = ReadOnce(path, buf, hashers, n =>
                {
                    result.BytesRead += n;
                    progress?.Invoke(new VerifyProgress(i, expected.Count, result.BytesRead, total, e.Path));
                }, ct);
                bool same = e.Size is not long want || want == size;
                for (int h = 0; same && h < hashers.Count; h++)
                    same = HashAlgo.Same(e.Hashes[h].Algorithm, hashers[h].Finish(), e.Hashes[h].Value);
                if (same) result.Ok++;
                else result.Changed.Add(e.Path);
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                result.Unreadable.Add(new UnreadableFile(e.Path, ex.Message));
            }
        }
        if (!result.Cancelled)
        {
            result.Checked = expected.Count;
            AddExtra(result, cardPath, expected, ignore);
        }
        result.FolderGone = result.Unreadable.Count + result.Missing.Count > 0 && !Directory.Exists(cardPath);
        result.Finished = DateTime.UtcNow;
        return result;
    }

    static long SizeOnDisk(string path)
    {
        try { return new FileInfo(path) is { Exists: true } f ? f.Length : 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// Reads the file once past the file cache, feeding every hash. Returns its size.
    static long ReadOnce(string path, AlignedBuffer buf, List<IFileHash> hashers, Action<int> progress, CancellationToken ct)
    {
        using var reader = new FileReader(path);
        long offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int n = reader.Read(buf.Span, offset);
            if (n == 0) break;
            foreach (var h in hashers) h.Append(buf.Span[..n]);
            offset += n;
            progress(n);
            if (n < buf.Size) break;
        }
        return offset;
    }

    /// Files on disk that the record does not know: not ours (_REPORT, _PROXY, ascmhl) and not ignored by the MHL.
    static void AddExtra(VerifyResult result, string cardPath, List<Expected> expected, IReadOnlyList<string> ignore)
    {
        var known = expected.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            result.Extra.AddRange(Scanner.Scan(cardPath).Files.Select(f => f.RelPath)
                .Where(p => !known.Contains(p) && !Ours.Any(o => p.StartsWith(o, StringComparison.OrdinalIgnoreCase)) && !Ignored(p, ignore)));
        }
        catch (IOError)
        {
            // Extra files are information only; an unreadable folder already shows as missing or unreadable files.
        }
    }

    /// ASC MHL ignore patterns: a name ("*.tmp", ".DS_Store") matches any path component, "name/" only a folder,
    /// a pattern with "/" inside the path from the card folder.
    internal static bool Ignored(string relPath, IReadOnlyList<string> patterns)
    {
        var parts = relPath.Split('/');
        foreach (var raw in patterns)
        {
            bool folderOnly = raw.EndsWith('/');
            var pattern = raw.TrimEnd('/');
            if (pattern.Length == 0) continue;
            if (pattern.Contains('/'))
            {
                if (Wildcard(relPath, pattern) || relPath.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase)) return true;
                continue;
            }
            var candidates = folderOnly ? parts[..^1] : parts;
            if (candidates.Any(c => Wildcard(c, pattern))) return true;
        }
        return false;
    }

    static bool Wildcard(string text, string pattern) =>
        Regex.IsMatch(text, "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// `_REPORT\*.xxh64` and `*.md5` ("<hash><two spaces><path>" lines, also with \r\n): one record per path.
    static List<Expected>? ReadHashLists(string cardPath)
    {
        var dir = Path.Combine(cardPath, Layout.ReportDirName);
        if (!Directory.Exists(dir)) return null;
        var byPath = new Dictionary<string, List<MhlHash>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var (pattern, algo) in new[] { ("*.xxh64", "xxh64"), ("*.md5", "md5") })
        {
            if (Directory.GetFiles(dir, pattern).Order(StringComparer.Ordinal).FirstOrDefault() is not string list) continue;
            foreach (var raw in File.ReadAllText(list, Encoding.UTF8).Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                int sep = line.IndexOf("  ", StringComparison.Ordinal);
                if (line.Length == 0 || sep < 0) continue;
                var rel = line[(sep + 2)..];
                if (!byPath.TryGetValue(rel, out var hashes))
                {
                    byPath[rel] = hashes = [];
                    order.Add(rel);
                }
                hashes.Add(new MhlHash(algo, line[..sep], "original"));
            }
        }
        return order.Count == 0 ? null : order.Select(p => new Expected(p, null, byPath[p], false)).ToList();
    }
}

/// Which card folders a chosen folder means (spec 2.4): a card folder (ascmhl\, or a report or hash list in _REPORT\)
/// is itself; a day folder means every card folder in it (its own _REPORT holds only the day report, plan 7).
public static class VerifyTargets
{
    static bool IsCard(string dir)
    {
        if (Directory.Exists(Path.Combine(dir, Mhl.DirName))) return true;
        var report = Path.Combine(dir, Layout.ReportDirName);
        return File.Exists(Path.Combine(report, "offload_report.json"))
               || (Directory.Exists(report) && Directory.EnumerateFiles(report, "*.xxh64").Any());
    }

    public static IReadOnlyList<string> Resolve(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return [];
            if (IsCard(folder)) return [folder];
            return Directory.GetDirectories(folder)
                .Where(d => !string.Equals(Path.GetFileName(d), Layout.ReportDirName, StringComparison.OrdinalIgnoreCase) && IsCard(d))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
```

```csharp
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Offload.Core.Desktop;

namespace Offload.Core.Verification;

public enum VerifyTone { Ok, Warning, Error }

/// The Czech texts of a verification (spec 2.4) — GUI job card, CLI summary, HTML report.
public static class VerifyTexts
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    static string N(long n) => n.ToString("N0", Cs);
    static string Files(int n) => $"{N(n)} {Texts.Plural(n, "soubor", "soubory", "souborů")}";

    /// "ASC MHL, 2 generace, xxHash64" / "seznam součtů, xxHash64 + MD5".
    public static string Basis(VerifyResult r)
    {
        var algos = string.Join(" + ", r.HashAlgorithms.Select(HashAlgo.Label));
        return r.Basis == "ascmhl"
            ? $"ASC MHL, {r.Generations} {Texts.Plural(r.Generations, "generace", "generace", "generací")}, {algos}"
            : $"seznam součtů, {algos}";
    }

    /// The main line, green / red / orange (cancelled).
    public static (string Text, VerifyTone Tone) Summary(VerifyResult r)
    {
        if (r.Cancelled)
            return ($"Ověření zrušeno po {N(r.Checked)} / {N(r.TotalFiles)} {(r.TotalFiles == 1 ? "souboru" : "souborech")}", VerifyTone.Warning);
        if (r.Success)
        {
            var all = r.Ok switch
            {
                1 => "soubor sedí",
                >= 2 and <= 4 => $"všechny {r.Ok} soubory sedí",
                _ => $"všech {N(r.Ok)} souborů sedí",
            };
            return ($"Ověřeno: {all} ({Basis(r)}).", VerifyTone.Ok);
        }
        var text = $"Nesedí {Files(r.Changed.Count)}, chybí {N(r.Missing.Count)}, nečitelný {N(r.Unreadable.Count)} — podrobnosti v reportu ověření.";
        if (r.FolderGone) text += " Složka zálohy zmizela — disk se možná odpojil.";
        return (text, VerifyTone.Error);
    }

    /// Orange lines under the summary: files nobody recorded, files that failed already at the copy, chain problems.
    public static IReadOnlyList<string> Warnings(VerifyResult r)
    {
        var lines = new List<string>();
        if (r.Extra.Count > 0)
            lines.Add($"{Files(r.Extra.Count)} navíc (nejsou v {(r.Basis == "ascmhl" ? "MHL" : "seznamu součtů")}).");
        if (r.FailedAtCopy.Count > 0)
            lines.Add($"{Files(r.FailedAtCopy.Count)} {Texts.Plural(r.FailedAtCopy.Count, "selhal", "selhaly", "selhalo")} už při kopii.");
        lines.AddRange(r.ChainProblems);
        return lines;
    }

    /// CLI: "Ověřeno 1 234 souborů (512 GB) za 22:10 (385 MB/s) — vše v pořádku." / "… — NALEZENY PROBLÉMY: …"
    public static string CliSummary(VerifyResult r)
    {
        double secs = (r.Finished - r.Started).TotalSeconds;
        var head = $"Ověřeno {Files(r.Checked)} ({Format.BytesCs(r.BytesRead)}) za {JobProgress.EtaText(secs)} ({Format.Rate(r.BytesRead, secs)})";
        if (r.Cancelled) return Summary(r).Text;
        return r.Success
            ? $"{head} — vše v pořádku."
            : $"{head} — NALEZENY PROBLÉMY: nesedí {N(r.Changed.Count)}, chybí {N(r.Missing.Count)}, nečitelné {N(r.Unreadable.Count)}, navíc {N(r.Extra.Count)}.";
    }
}

/// The verification report (spec 2.4, R8): `_REPORT\<card>_verify_<stamp>Z.html/.pdf` and `offload_verify_<stamp>Z.json`.
/// Nothing goes into ascmhl\ — _REPORT is ignored by every MHL, so a later `ascmhl verify` still passes.
public static class VerifyReport
{
    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static string Iso(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// "2026-10-06_081500" — the start of the run in UTC.
    public static string Stamp(VerifyResult r) => r.Started.ToUniversalTime().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);

    static string CardName(VerifyResult r) => Path.GetFileName(r.CardPath.TrimEnd('\\', '/'));

    /// UTF-8 without BOM, sorted keys, ISO 8601 UTC dates — like offload_report.json.
    public static string Json(VerifyResult r)
    {
        static JsonArray Strings(IEnumerable<string> items) => new(items.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        var o = new JsonObject
        {
            ["basis"] = r.Basis,
            ["cardPath"] = r.CardPath,
            ["chainProblems"] = Strings(r.ChainProblems),
            ["changed"] = Strings(r.Changed),
            ["extra"] = Strings(r.Extra),
            ["failedAtCopy"] = Strings(r.FailedAtCopy),
            ["finished"] = Iso(r.Finished),
            ["generations"] = r.Generations,
            ["hashAlgorithms"] = Strings(r.HashAlgorithms),
            ["missing"] = Strings(r.Missing),
            ["ok"] = r.Ok,
            ["started"] = Iso(r.Started),
            ["success"] = r.Success,
            ["tool"] = "offload",
            ["totalBytes"] = r.TotalBytes,
            ["totalFiles"] = r.TotalFiles,
            ["unreadable"] = new JsonArray(r.Unreadable.Select(u => (JsonNode?)new JsonObject { ["error"] = u.Error, ["path"] = u.Path }).ToArray()),
            ["version"] = OffloadVersion.String,
        };
        return o.ToJsonString(WriteOptions);
    }

    public static string Html(VerifyResult r, bool forPrint = false)
    {
        var (summary, tone) = VerifyTexts.Summary(r);
        var (badgeClass, badge) = tone switch
        {
            VerifyTone.Ok => ("ok", "OVĚŘENO"),
            VerifyTone.Error => ("bad", "NALEZENY PROBLÉMY"),
            _ => ("warn", "ZRUŠENO"),
        };
        var card = HtmlReport.Esc(CardName(r));
        var h = new StringBuilder();
        h.Append("<!doctype html>\n<html lang=\"cs\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Ověření {card}</title>\n<style>\n{HtmlReport.Styles(forPrint)}</style></head><body><main>\n");
        h.Append($"<header><h1>Ověření {card}</h1><span class=\"badge {badgeClass}\">{badge}</span>\n");
        h.Append($"<span class=\"sub\">{HtmlReport.DateTimeText(r.Started)} – {HtmlReport.DateTimeText(r.Finished)}</span></header>\n");
        h.Append($"<p class=\"{badgeClass}\">{HtmlReport.Esc(summary)}</p>\n");
        foreach (var w in VerifyTexts.Warnings(r)) h.Append($"<p class=\"warn\">{HtmlReport.Esc(w)}</p>\n");
        h.Append("<div class=\"stats\">\n");
        Stat(h, r.TotalFiles.ToString(CultureInfo.InvariantCulture), "souborů v záznamu");
        Stat(h, Format.Bytes(r.TotalBytes), "data");
        Stat(h, r.Ok.ToString(CultureInfo.InvariantCulture), "sedí");
        Stat(h, HtmlReport.Esc(VerifyTexts.Basis(r)), "porovnáno s");
        Stat(h, HtmlReport.Duration((r.Finished - r.Started).TotalSeconds), "ověření · " + Format.Rate(r.BytesRead, (r.Finished - r.Started).TotalSeconds));
        h.Append("</div>\n");
        h.Append($"<p class=\"sub\">Složka: <span class=\"mono\">{HtmlReport.Esc(r.CardPath)}</span></p>\n");
        Section(h, "Nesedí (obsah se liší od záznamu)", "bad", r.Changed);
        Section(h, "Chybí", "bad", r.Missing);
        Section(h, "Nečitelné", "bad", r.Unreadable.Select(u => $"{u.Path} — {u.Error}").ToList());
        Section(h, "Selhaly už při kopii", "warn", r.FailedAtCopy);
        Section(h, r.Basis == "ascmhl" ? "Navíc (nejsou v MHL)" : "Navíc (nejsou v seznamu součtů)", "warn", r.Extra);
        Section(h, "Řetěz ASC MHL", "warn", r.ChainProblems);
        h.Append($"<footer>offload {HtmlReport.Esc(OffloadVersion.String)} · report ověření · {HtmlReport.DateTimeText(DateTime.Now)}</footer></main></body></html>\n");
        return h.ToString();
    }

    static void Stat(StringBuilder h, string value, string label) =>
        h.Append($"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>\n");

    static void Section(StringBuilder h, string title, string cls, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        h.Append($"<h2 class=\"{cls}\">{HtmlReport.Esc(title)} ({lines.Count})</h2><div class=\"panel\"><table>\n");
        foreach (var l in lines) h.Append($"<tr><td class=\"mono wrap\">{HtmlReport.Esc(l)}</td></tr>\n");
        h.Append("</table></div>\n");
    }

    /// Writes JSON, HTML and (best effort, logged) PDF into `<card>\_REPORT\`. Returns the HTML path. Not for a cancelled run.
    public static string Write(VerifyResult r, Action<string>? log = null)
    {
        var dir = Path.Combine(r.CardPath, Layout.ReportDirName);
        NativeIO.Mkdirs(dir);
        var stamp = Stamp(r);
        var card = CardName(r);
        NativeIO.WriteFileDurably(Path.Combine(dir, $"offload_verify_{stamp}Z.json"), new UTF8Encoding(false).GetBytes(Json(r)));
        var html = Path.Combine(dir, $"{card}_verify_{stamp}Z.html");
        NativeIO.WriteFileDurably(html, new UTF8Encoding(false).GetBytes(Html(r)));
        try
        {
            PdfReport.WriteHtml(Html(r, forPrint: true), Path.Combine(dir, $"{card}_verify_{stamp}Z.pdf"));
        }
        catch (Exception e)
        {
            log?.Invoke($"verify PDF {card}: {e.Message}");
        }
        return html;
    }
}
```

Smazat `windows/src/Offload.Core/Verifier.cs`.

CLI `verify` přepsat celý — `windows/src/Offload.Cli/VerifyCommand.cs`:

```csharp
using System.CommandLine;
using Offload.Core;
using Offload.Core.Verification;

namespace Offload.Cli;

static class VerifyCommand
{
    public static Command Create()
    {
        var folder = new Argument<string>("folder") { Description = "Card folder (<root>/<date>/<card>) or a whole date folder (every card in it)." };
        var report = new Option<bool>("--report") { Description = "Write the verification report (HTML, PDF, JSON) into each card's _REPORT folder." };
        var cmd = new Command("verify", "Re-verify card folders against their ASC MHL (all generations), or the _REPORT hash list.");
        cmd.Arguments.Add(folder);
        cmd.Options.Add(report);
        cmd.SetAction(r =>
        {
            string full;
            try
            {
                full = CliPath.Full(r.GetValue(folder)!);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
            return Run(full, r.GetValue(report));
        });
        return cmd;
    }

    /// Exit 0 when every card verified; 1 when any had a problem (or there was nothing to verify). The problem lines stay
    /// machine-readable (MISMATCH, MISSING, ERROR, EXTRA, FAILED-AT-COPY, CHAIN) for scripts; the summary is Czech.
    static int Run(string folder, bool writeReport)
    {
        var cards = VerifyTargets.Resolve(folder);
        if (cards.Count == 0)
        {
            Console.Error.WriteLine(Msg.CliError + Msg.NoHashRecord(folder));
            return 1;
        }
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCtrlC = (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += onCtrlC;
        try
        {
            bool allOk = true;
            foreach (var card in cards)
            {
                if (cards.Count > 1) Console.WriteLine($"\n{card}");
                allOk &= VerifyOne(card, writeReport, cancel.Token);
                if (cancel.IsCancellationRequested) return 1;
            }
            return allOk ? 0 : 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCtrlC;
        }
    }

    static bool VerifyOne(string card, bool writeReport, CancellationToken ct)
    {
        var last = DateTime.MinValue;
        VerifyResult r;
        try
        {
            r = BackupVerifier.Verify(card, p =>
            {
                if (DateTime.UtcNow - last < TimeSpan.FromMilliseconds(500)) return;
                last = DateTime.UtcNow;
                ConsoleLine.Write($"{p.FileIndex + 1}/{p.FileCount}  {Format.Bytes(p.Bytes)}  {p.CurrentFile}");
            }, ct);
        }
        catch (Exception e)
        {
            ConsoleLine.Clear();
            Console.Error.WriteLine(Msg.CliError + e.Message);
            return false;
        }
        ConsoleLine.Clear();
        foreach (var m in r.Changed) Console.WriteLine($"MISMATCH  {m}");
        foreach (var m in r.Missing) Console.WriteLine($"MISSING   {m}");
        foreach (var m in r.Unreadable) Console.WriteLine($"ERROR     {m.Path}: {m.Error}");
        foreach (var m in r.Extra) Console.WriteLine($"EXTRA     {m}");
        foreach (var m in r.FailedAtCopy) Console.WriteLine($"FAILED-AT-COPY  {m}");
        foreach (var m in r.ChainProblems) Console.WriteLine($"CHAIN     {m}");
        Console.WriteLine(VerifyTexts.CliSummary(r));
        if (writeReport && !r.Cancelled)
        {
            try
            {
                Console.WriteLine($"Report: {VerifyReport.Write(r, m => Console.Error.WriteLine(m))}");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + Msg.ReportNotWritten(e.Message));
            }
        }
        return r.Success;
    }
}
```

- [ ] **Step 4: Testy + celá sada**

Run: `dotnet test --filter "FullyQualifiedName~BackupVerifierTests|FullyQualifiedName~VerifyReportTests|FullyQualifiedName~HtmlReportTests|FullyQualifiedName~PdfReportTests"` → PASS; `dotnet build` bez varování; `dotnet test` → zelená.

Ruční kontrola CLI (jen konzole): nad hotovou zálohou z ručního testu plánu 1, je-li po ruce, `src\Offload.Cli\bin\Debug\net10.0-windows\offload.exe verify <cíl>\<datum>\<karta> --report` → „Ověřeno N souborů (…) za … (… MB/s) — vše v pořádku.“, kód 0, `Report: …_verify_…Z.html`; bez zálohy krok vynechat (pokrývají ho testy).

- [ ] **Step 5: VYVOJ + commit**

Řádek:

```markdown
- Úkol 11 — ověření zálohy proti ASC MHL všech generací (každý soubor se čte jednou všemi hashi, velikost zvlášť; `failed` z kopie, soubory navíc mimo `_REPORT`/`_PROXY`/`ascmhl` a ignore vzory MHL), bez `ascmhl` proti `.xxh64`/`.md5`; report ověření `_REPORT\<karta>_verify_<stamp>Z.html/.pdf` + `offload_verify_<stamp>Z.json` (do `ascmhl` nic); CLI `verify` s českým souhrnem, `--report` a složkou dne; starý `Verifier` odstraněn
```

```powershell
git add -A windows docs
git commit -m "Windows: verify backups against ASC MHL with a verification report" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: GUI ověření

Spec §2.4 „UX“, §6.1 úkol 12. Ověření je položka **kopírovací** fronty `JobQueue` (sériově s kopiemi — zatěžují stejné disky); fronta v okně nese dva druhy položek (`QueueItem`), šablonu vybírá `QueueItemTemplateSelector` (DataTemplateSelector ze specifikace).

**Files:**
- Modify: `windows/src/Offload.App/JobItemViewModel.cs` (řádky 9–13, 42–47, 78–88)
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs` (řádky 16–20, 170–178, 197–201, 233–237, 255–259)
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs` (řádky 178–182)
- Create: `windows/src/Offload.App/MainViewModel.Verify.cs`
- Modify: `windows/src/Offload.App/MainViewModel.cs` (řádky 24–27)
- Modify: `windows/src/Offload.App/MainWindow.xaml` (řádky 9–12, 14–17)
- Create: `windows/src/Offload.App/QueueItem.cs`
- Modify: `windows/src/Offload.App/QueuePanel.xaml` (řádky 1–5, 42–46, 100–104)
- Create: `windows/src/Offload.App/VerifyItemViewModel.cs`
- Create: `windows/src/Offload.Core/Desktop/VerifyJob.cs`
- Create: `windows/tests/Offload.Core.Tests/VerifyJobTests.cs`

**Interfaces:**
- Consumes: `BackupVerifier`, `VerifyReport`, `VerifyTexts`, `VerifyTargets` (úkol 11), `JobQueue`/`IQueuedJob`, `CardTone`.
- Produces: `enum VerifyJobState { Waiting, Running, Done, Cancelled, Failed }`, `record VerifyJobStatus(VerifyJobState State, VerifyProgress? Progress, VerifyResult? Result, string? Error, string? ReportPath)`, `VerifyJob(string cardPath, bool writeReport, Action<string>? log = null) : IQueuedJob` (`CardPath`, `CardName`, `Cancel()`, `Status()`; `RunCopy` ověří a vrátí false); `VerifyJobView.Title/StatusText/Tone/Detail/Fraction`; v App abstraktní `QueueItem` (`Queued`, `Phase`, `IsFinished`, `IsError`, `Busy`, `Refresh()`, `Cancel()`), `JobItemViewModel : QueueItem`, `VerifyItemViewModel : QueueItem`, `QueueItemTemplateSelector`, `MainViewModel.Jobs : ObservableCollection<QueueItem>`, `VerifyBackupCommand`, `SaveVerifyReport`.

- [ ] **Step 1: Padající testy**

```csharp
using Offload.Core.Desktop;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

public class VerifyJobTests
{
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    static string Offloaded(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 100_000, 1);
        var engine = new OffloadEngine(new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true });
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-06");
        engine.Run(plan);
        return plan.CardPaths[0];
    }

    // Spec 2.4: a verification runs in the copy queue; RunCopy returns false (no stage 2).
    [Fact]
    public void InTheQueue_VerifiesAndWritesTheReport()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        var job = new VerifyJob(card, writeReport: true);
        Assert.Equal(VerifyJobState.Waiting, job.Status().State);
        using var q = new JobQueue();
        using var finished = new ManualResetEventSlim();
        bool copyFinished = false;
        q.CopyFinished += _ => copyFinished = true;
        q.JobFinished += _ => finished.Set();
        q.Enqueue(job);
        Assert.True(finished.Wait(Wait));

        var s = job.Status();
        Assert.True(copyFinished);
        Assert.Equal(VerifyJobState.Done, s.State);
        Assert.True(s.Result!.Success);
        Assert.Equal("Ověřeno", VerifyJobView.StatusText(s));
        Assert.Equal(CardTone.Ok, VerifyJobView.Tone(s));
        Assert.Equal("Ověření A001", VerifyJobView.Title(job));
        Assert.StartsWith(Path.Combine(card, "_REPORT", "A001_verify_"), s.ReportPath);
        Assert.True(File.Exists(s.ReportPath));
    }

    [Fact]
    public void Problems_AreAnErrorTone_NoReportWhenOff()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        File.Delete(Path.Combine(card, "CLIP", "a.mov"));
        var job = new VerifyJob(card, writeReport: false);
        job.RunCopy();
        var s = job.Status();
        Assert.Equal("Problémy", VerifyJobView.StatusText(s));
        Assert.Equal(CardTone.Error, VerifyJobView.Tone(s));
        Assert.Null(s.ReportPath);
        Assert.False(Directory.EnumerateFiles(Path.Combine(card, "_REPORT"), "*_verify_*").Any());
    }

    [Fact]
    public void CancelledWhileQueued_NeverRuns()
    {
        using var dir = new TempDir();
        var job = new VerifyJob(dir.Sub("A001"), writeReport: true);
        job.Cancel();
        Assert.Equal(VerifyJobState.Cancelled, job.Status().State);
        Assert.False(job.RunCopy());
        Assert.Null(job.Status().Result);
    }

    [Fact]
    public void NothingToVerify_Failed_WithCzechText()
    {
        using var dir = new TempDir();
        var job = new VerifyJob(dir.Sub("A001"), writeReport: true);
        job.RunCopy();
        var s = job.Status();
        Assert.Equal(VerifyJobState.Failed, s.State);
        Assert.Contains("není ASC MHL ani seznam součtů", s.Error);
    }

    [Fact]
    public void Detail_Text()
    {
        Assert.Equal("Ověřuji 120 / 1 234 souborů · 45 GB / 512 GB · 380 MB/s",
            VerifyJobView.Detail(new VerifyProgress(119, 1234, 45_000_000_000, 512_000_000_000, "x"), 380_000_000).Replace(' ', ' '));
        Assert.Equal("Ověřuji…", VerifyJobView.Detail(null, 0));
    }
}
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~VerifyJobTests"` → build FAIL.

- [ ] **Step 3: Implementace**

```csharp
using System.Globalization;
using Offload.Core.Verification;

namespace Offload.Core.Desktop;

public enum VerifyJobState { Waiting, Running, Done, Cancelled, Failed }

public sealed record VerifyJobStatus(VerifyJobState State, VerifyProgress? Progress, VerifyResult? Result, string? Error, string? ReportPath);

/// "Ověřit zálohu…" as a queue item (spec 2.4): it runs in the copy queue, one after another with the copies — both
/// load the same disks. `RunCopy` verifies (and writes the report), returns false: there is no stage 2.
public sealed class VerifyJob(string cardPath, bool writeReport, Action<string>? log = null) : IQueuedJob
{
    readonly CancellationTokenSource cts = new();
    readonly Lock gate = new();
    VerifyJobStatus status = new(VerifyJobState.Waiting, null, null, null, null);

    public string CardPath { get; } = cardPath;
    public string CardName => Path.GetFileName(CardPath.TrimEnd('\\', '/'));
    public bool IsCancelled => cts.IsCancellationRequested;

    public void Cancel() => cts.Cancel();

    /// Read by the UI thread every tick.
    public VerifyJobStatus Status()
    {
        lock (gate)
        {
            // Cancelled while still queued: it never runs (JobQueue skips it), so say so instead of "Čeká" forever.
            return status.State == VerifyJobState.Waiting && IsCancelled ? status with { State = VerifyJobState.Cancelled } : status;
        }
    }

    void Set(Func<VerifyJobStatus, VerifyJobStatus> change)
    {
        lock (gate) status = change(status);
    }

    public bool RunCopy()
    {
        if (IsCancelled)
        {
            Set(s => s with { State = VerifyJobState.Cancelled });
            return false;
        }
        Set(s => s with { State = VerifyJobState.Running });
        using var awake = NativeIO.KeepAwake();
        try
        {
            var r = BackupVerifier.Verify(CardPath, p => Set(s => s with { Progress = p }), cts.Token);
            string? report = null;
            if (writeReport && !r.Cancelled)
            {
                try { report = VerifyReport.Write(r, log); }
                catch (Exception e) { log?.Invoke($"verify report: {e.Message}"); }
            }
            Set(s => s with { State = r.Cancelled ? VerifyJobState.Cancelled : VerifyJobState.Done, Result = r, ReportPath = report });
        }
        catch (Exception e)
        {
            log?.Invoke($"verify: {e.Message}");
            Set(s => s with { State = VerifyJobState.Failed, Error = e.Message });
        }
        return false;
    }

    public void RunPost()
    {
    }
}

/// What a verification card in the queue shows (spec 2.4 UX).
public static class VerifyJobView
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    public static string Title(VerifyJob job) => $"Ověření {job.CardName}";

    public static string StatusText(VerifyJobStatus s) => s.State switch
    {
        VerifyJobState.Waiting => "Čeká",
        VerifyJobState.Running => "Ověřování",
        VerifyJobState.Done => s.Result is { Success: true } ? "Ověřeno" : "Problémy",
        VerifyJobState.Cancelled => "Zrušeno",
        _ => "Chyba",
    };

    public static CardTone Tone(VerifyJobStatus s) => s.State switch
    {
        VerifyJobState.Waiting => CardTone.Waiting,
        VerifyJobState.Running => CardTone.Active,
        VerifyJobState.Done => s.Result is { Success: true } ? CardTone.Ok : CardTone.Error,
        VerifyJobState.Cancelled => CardTone.Warning,
        _ => CardTone.Error,
    };

    /// "Ověřuji 120 / 1 234 souborů · 45 GB / 512 GB · 380 MB/s"
    public static string Detail(VerifyProgress? p, double bytesPerSecond)
    {
        if (p == null) return "Ověřuji…";
        var rate = (bytesPerSecond / 1_000_000).ToString("0", CultureInfo.InvariantCulture);
        return $"Ověřuji {(p.FileIndex + 1).ToString("N0", Cs)} / {p.FileCount.ToString("N0", Cs)} souborů · {Format.BytesCs(p.Bytes)} / {Format.BytesCs(p.TotalBytes)} · {rate} MB/s";
    }

    public static double Fraction(VerifyProgress? p) => p is { TotalBytes: > 0 } ? Math.Clamp((double)p.Bytes / p.TotalBytes, 0, 1) : 0;
}
```

Fronta okna se dvěma druhy položek:

```csharp
using System.Windows;
using System.Windows.Controls;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

/// One card in the Fronta panel: an offload (JobItemViewModel) or a verification (VerifyItemViewModel).
public abstract class QueueItem : ObservableObject
{
    /// What the JobQueue runs.
    public abstract IQueuedJob Queued { get; }
    /// As a copy phase — a running verification counts as Verifying (no speed measurement meanwhile).
    public abstract JobPhase Phase { get; }
    public abstract bool IsFinished { get; }
    /// Failed or with problems: the error sound and flash.
    public abstract bool IsError { get; }
    /// Still needs the app when closing (not finished, or an eject in flight).
    public virtual bool Busy => !IsFinished;
    public abstract void Refresh();
    public abstract void Cancel();
}

/// QueuePanel: an offload card or a verification card.
public sealed class QueueItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? JobTemplate { get; set; }
    public DataTemplate? VerifyTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is VerifyItemViewModel ? VerifyTemplate : JobTemplate;
}
```

`windows/src/Offload.App/JobItemViewModel.cs`, řádky 9–13 — najít:

```csharp

/// One card in the queue: mirrors the job's status every tick (Mac `JobItem` + `JobCard`).
public sealed class JobItemViewModel : ObservableObject
{
    readonly MainViewModel owner;
```

nahradit:

```csharp

/// One card in the queue: mirrors the job's status every tick (Mac `JobItem` + `JobCard`).
public sealed class JobItemViewModel : QueueItem
{
    readonly MainViewModel owner;
```

`windows/src/Offload.App/JobItemViewModel.cs`, řádky 42–47 — najít:

```csharp
    public RelayCommand OpenReportCommand { get; }
    public RelayCommand RevealCommand { get; }

    public void Refresh()
    {
        progress.Update(Job.Status(), DateTime.UtcNow);
```

nahradit:

```csharp
    public RelayCommand OpenReportCommand { get; }
    public RelayCommand RevealCommand { get; }
    public override IQueuedJob Queued => Runner;
    public override void Cancel() => Job.Cancel();
    public override bool Busy => !IsFinished || Ejecting;

    public override void Refresh()
    {
        progress.Update(Job.Status(), DateTime.UtcNow);
```

`windows/src/Offload.App/JobItemViewModel.cs`, řádky 78–88 — najít:

```csharp
    }

    public JobPhase Phase => progress.Status.Phase;
    public bool IsRunning => progress.IsRunning;
    public bool IsFinished => Phase.IsFinished();
    public bool SafeToEject => progress.Status.SafeToEject;
    public bool? CopySucceeded => progress.Status.CopySucceeded;
    /// Failed or copied with errors; a cancelled job is not an error.
    public bool IsError => progress.IsError;
    public CardTone Tone => progress.Tone;
    public string StatusText => progress.StatusText;
```

nahradit:

```csharp
    }

    public override JobPhase Phase => progress.Status.Phase;
    public bool IsRunning => progress.IsRunning;
    public override bool IsFinished => Phase.IsFinished();
    public bool SafeToEject => progress.Status.SafeToEject;
    public bool? CopySucceeded => progress.Status.CopySucceeded;
    /// Failed or copied with errors; a cancelled job is not an error.
    public override bool IsError => progress.IsError;
    public CardTone Tone => progress.Tone;
    public string StatusText => progress.StatusText;
```

```csharp
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Verification;

namespace Offload.App;

/// A verification in the queue (spec 2.4): progress, the result in Czech, the report and Explorer buttons.
public sealed class VerifyItemViewModel : QueueItem
{
    readonly MainViewModel owner;
    VerifyJobStatus status;
    DateTime lastTime = DateTime.UtcNow;
    long lastBytes;
    double rate;

    public VerifyItemViewModel(MainViewModel owner, VerifyJob job)
    {
        this.owner = owner;
        Job = job;
        status = job.Status();
        CancelCommand = new RelayCommand(() => { Job.Cancel(); Refresh(); });
        OpenReportCommand = new RelayCommand(OpenReport);
        RevealCommand = new RelayCommand(Reveal);
    }

    public VerifyJob Job { get; }
    public override IQueuedJob Queued => Job;
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenReportCommand { get; }
    public RelayCommand RevealCommand { get; }

    public override JobPhase Phase => status.State switch
    {
        VerifyJobState.Waiting => JobPhase.Waiting,
        VerifyJobState.Running => JobPhase.Verifying,
        VerifyJobState.Done => JobPhase.Done,
        VerifyJobState.Cancelled => JobPhase.Cancelled,
        _ => JobPhase.Failed,
    };

    public override bool IsFinished => Phase.IsFinished();
    public override bool IsError => VerifyJobView.Tone(status) == CardTone.Error;
    public override void Cancel() => Job.Cancel();

    public override void Refresh()
    {
        status = Job.Status();
        var now = DateTime.UtcNow;
        double dt = (now - lastTime).TotalSeconds;
        if (status.Progress is { } p && dt > 0.2)
        {
            var current = Math.Max(0, (p.Bytes - lastBytes) / dt);
            rate = rate == 0 ? current : rate * 0.6 + current * 0.4;
            lastBytes = p.Bytes;
            lastTime = now;
        }
        Raise(string.Empty);
    }

    public string Title => VerifyJobView.Title(Job);
    public string FolderText => Job.CardPath;
    public string StatusText => VerifyJobView.StatusText(status);
    public CardTone Tone => VerifyJobView.Tone(status);
    public bool IsRunning => status.State == VerifyJobState.Running;
    public double Progress => VerifyJobView.Fraction(status.Progress);
    public string DetailText => VerifyJobView.Detail(status.Progress, rate);
    public string? ResultText => status.Result is { } r ? VerifyTexts.Summary(r).Text : null;
    public VerifyTone ResultTone => status.Result is { } r ? VerifyTexts.Summary(r).Tone : VerifyTone.Ok;
    public IReadOnlyList<string> Warnings => status.Result is { Cancelled: false } r ? VerifyTexts.Warnings(r) : [];
    public string? ErrorText => status.Error;
    public bool HasReport => status.ReportPath != null;
    public bool CanCancel => !IsFinished;

    void OpenReport()
    {
        if (status.ReportPath is string path && Shell.Open(path) is string error) owner.ShowMessage(error);
    }

    void Reveal()
    {
        if (Shell.Reveal(Job.CardPath) is string error) owner.ShowMessage(error);
    }
}
```

```csharp
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Verification;

namespace Offload.App;

/// Nástroje → Ověřit zálohu… (spec 2.4): a card folder or a whole day; each card is one item in the copy queue.
public sealed partial class MainViewModel
{
    bool saveVerifyReport = true;

    public RelayCommand VerifyBackupCommand { get; private set; } = null!;

    /// "Ukládat report ověření" (menu, on by default): the HTML/PDF/JSON go into the card's _REPORT folder.
    public bool SaveVerifyReport
    {
        get => saveVerifyReport;
        set => Set(ref saveVerifyReport, value);
    }

    void InitVerify() => VerifyBackupCommand = new RelayCommand(PickAndVerify, () => !closing);

    async void PickAndVerify()
    {
        if (Dialogs.PickFolder("Vyberte složku karty na cíli (nebo celý den)") is not string folder) return;
        try
        {
            var cards = await Task.Run(() => VerifyTargets.Resolve(folder));   // the folder may be on a slow or network disk
            if (closing) return;
            if (cards.Count == 0)
            {
                ShowMessage(Msg.NoHashRecord(folder));
                return;
            }
            foreach (var card in cards.Reverse())   // the first card ends up on top
            {
                var item = new VerifyItemViewModel(this, new VerifyJob(card, SaveVerifyReport, m => AppLog.Write($"{card}: {m}")));
                Jobs.Insert(0, item);
                queue.Enqueue(item.Job);
            }
            RaiseMany(nameof(HasJobs), nameof(HasFinished));
        }
        catch (Exception e)
        {
            AppLog.Write($"verify: {e}");
            ShowMessage($"Ověření se nepodařilo spustit: {e.Message}");
        }
    }
}
```

`windows/src/Offload.App/MainViewModel.cs`, řádky 24–27 — najít:

```csharp
        InitHardware();
        InitHistory();
    }

```

nahradit:

```csharp
        InitHardware();
        InitHistory();
        InitVerify();
    }

```
`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 16–20 — najít:

```csharp
    bool closing, closeRaised, waitNoticeShown, shownHasFinished, shownCopyRunning, starting;

    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];
    public RelayCommand StartCommand { get; private set; } = null!;
    public RelayCommand ClearFinishedCommand { get; private set; } = null!;
```

nahradit:

```csharp
    bool closing, closeRaised, waitNoticeShown, shownHasFinished, shownCopyRunning, starting;

    /// Offloads and verifications, newest on top.
    public ObservableCollection<QueueItem> Jobs { get; } = [];
    public RelayCommand StartCommand { get; private set; } = null!;
    public RelayCommand ClearFinishedCommand { get; private set; } = null!;
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 170–178 — najít:

```csharp
    internal bool CardInDrive(JobItemViewModel item) => QueueRules.CardInDrive(item.Job.Config.Source, item.Runner.SourceIdentity, volumes);

    JobItemViewModel? Find(IQueuedJob job) => Jobs.FirstOrDefault(j => ReferenceEquals(j.Runner, job));

    void OnCopyFinished(IQueuedJob job)
    {
        if (Find(job) is not { } item) return;
        item.Refresh();
        if (!closing && item.SafeToEject) SystemSounds.Asterisk.Play();
```

nahradit:

```csharp
    internal bool CardInDrive(JobItemViewModel item) => QueueRules.CardInDrive(item.Job.Config.Source, item.Runner.SourceIdentity, volumes);

    QueueItem? Find(IQueuedJob job) => Jobs.FirstOrDefault(j => ReferenceEquals(j.Queued, job));

    void OnCopyFinished(IQueuedJob job)
    {
        if (Find(job) is not JobItemViewModel item) return;   // a verification has no stage 2: OnJobFinished follows
        item.Refresh();
        if (!closing && item.SafeToEject) SystemSounds.Asterisk.Play();
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 197–201 — najít:

```csharp

    /// Still needs the app: a job that has not finished, or an eject in flight.
    static bool Busy(JobItemViewModel j) => !j.IsFinished || j.Ejecting;

    /// Closing waits for the cancelled jobs and ejects; once all are done the window is told to close — once.
```

nahradit:

```csharp

    /// Still needs the app: a job that has not finished, or an eject in flight.
    static bool Busy(QueueItem j) => j.Busy;

    /// Closing waits for the cancelled jobs and ejects; once all are done the window is told to close — once.
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 233–237 — najít:

```csharp
            closing = true;
            inspections.CancelAll();
            foreach (var j in Jobs) j.Job.Cancel();
            foreach (var j in Jobs) j.Refresh();
            Raise(nameof(CanStart));
```

nahradit:

```csharp
            closing = true;
            inspections.CancelAll();
            foreach (var j in Jobs) j.Cancel();
            foreach (var j in Jobs) j.Refresh();
            Raise(nameof(CanStart));
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 255–259 — najít:

```csharp
        var source = item.Job.Config.Source;
        var copied = item.Runner.SourceIdentity;
        bool inUse = QueueRules.CardInUse(source, Jobs.Where(j => !ReferenceEquals(j, item)).Select(j => (j.Job.Config.Source, j.Phase)));
        try
        {
```

nahradit:

```csharp
        var source = item.Job.Config.Source;
        var copied = item.Runner.SourceIdentity;
        bool inUse = QueueRules.CardInUse(source, Jobs.OfType<JobItemViewModel>().Where(j => !ReferenceEquals(j, item)).Select(j => (j.Job.Config.Source, j.Phase)));
        try
        {
```
`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 178–182 — najít:

```csharp
        }
        InspectMissing();   // new or swapped cards
        foreach (var j in Jobs) j.VolumesChanged();   // "Vysunout kartu" only while the copied card is in its drive
        RefreshLut();   // the LUT may sit on a disk that just came or went
        RefreshProxyFolder();   // and so may the proxy folder
```

nahradit:

```csharp
        }
        InspectMissing();   // new or swapped cards
        foreach (var j in Jobs.OfType<JobItemViewModel>()) j.VolumesChanged();   // "Vysunout kartu" only while the copied card is in its drive
        RefreshLut();   // the LUT may sit on a disk that just came or went
        RefreshProxyFolder();   // and so may the proxy folder
```

Menu *Nástroje* → „Ověřit zálohu…“ (`Ctrl+Shift+O`), zaškrtávací „Ukládat report ověření“:

`windows/src/Offload.App/MainWindow.xaml`, řádky 9–12 — najít:

```xml
        <KeyBinding Gesture="Ctrl+Enter" Command="{Binding StartCommand}" />
        <KeyBinding Gesture="Ctrl+U" Command="{Binding OpenHardwareCommand}" />
    </Window.InputBindings>
    <DockPanel>
```

nahradit:

```xml
        <KeyBinding Gesture="Ctrl+Enter" Command="{Binding StartCommand}" />
        <KeyBinding Gesture="Ctrl+U" Command="{Binding OpenHardwareCommand}" />
        <KeyBinding Gesture="Ctrl+Shift+O" Command="{Binding VerifyBackupCommand}" />
    </Window.InputBindings>
    <DockPanel>
```

`windows/src/Offload.App/MainWindow.xaml`, řádky 14–17 — najít:

```xml
            <Menu Padding="6,2">
                <MenuItem Header="_Nástroje">
                    <MenuItem Header="Prověřit USB porty…" InputGestureText="Ctrl+U" Command="{Binding OpenHardwareCommand}"
                              AutomationProperties.AutomationId="CheckPortsMenuItem">
```

nahradit:

```xml
            <Menu Padding="6,2">
                <MenuItem Header="_Nástroje">
                    <MenuItem Header="Ověřit zálohu…" InputGestureText="Ctrl+Shift+O" Command="{Binding VerifyBackupCommand}"
                              AutomationProperties.AutomationId="VerifyBackupMenuItem">
                        <MenuItem.Icon>
                            <TextBlock Style="{StaticResource Icon}" FontSize="14" Text="&#xE9D5;" />
                        </MenuItem.Icon>
                    </MenuItem>
                    <MenuItem Header="Ukládat report ověření" IsCheckable="True" IsChecked="{Binding SaveVerifyReport}"
                              AutomationProperties.AutomationId="SaveVerifyReportMenuItem" />
                    <Separator />
                    <MenuItem Header="Prověřit USB porty…" InputGestureText="Ctrl+U" Command="{Binding OpenHardwareCommand}"
                              AutomationProperties.AutomationId="CheckPortsMenuItem">
```

Šablona karty ověření (stávající šablona úlohy se jen přesune pod `JobTemplate`):

`windows/src/Offload.App/QueuePanel.xaml`, řádky 1–5 — najít:

```xml
<UserControl x:Class="Offload.App.QueuePanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <UserControl.Resources>
        <Style x:Key="ToneText" TargetType="TextBlock">
```

nahradit:

```xml
<UserControl x:Class="Offload.App.QueuePanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Offload.App">
    <UserControl.Resources>
        <Style x:Key="ToneText" TargetType="TextBlock">
```

`windows/src/Offload.App/QueuePanel.xaml`, řádky 42–46 — najít:

```xml
            <ScrollViewer VerticalScrollBarVisibility="Auto" Visibility="{Binding HasJobs, Converter={StaticResource BoolToVisible}}">
                <ItemsControl ItemsSource="{Binding Jobs}" Margin="20">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border CornerRadius="12" Padding="16" Margin="0,0,0,12" BorderThickness="1"
```

nahradit:

```xml
            <ScrollViewer VerticalScrollBarVisibility="Auto" Visibility="{Binding HasJobs, Converter={StaticResource BoolToVisible}}">
                <ItemsControl ItemsSource="{Binding Jobs}" Margin="20">
                    <ItemsControl.ItemTemplateSelector>
                        <local:QueueItemTemplateSelector>
                        <local:QueueItemTemplateSelector.JobTemplate>
                        <DataTemplate>
                            <Border CornerRadius="12" Padding="16" Margin="0,0,0,12" BorderThickness="1"
```

`windows/src/Offload.App/QueuePanel.xaml`, řádky 100–104 — najít:

```xml
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
```

nahradit:

```xml
                            </Border>
                        </DataTemplate>
                        </local:QueueItemTemplateSelector.JobTemplate>
                        <local:QueueItemTemplateSelector.VerifyTemplate>
                        <DataTemplate>
                            <Border CornerRadius="12" Padding="16" Margin="0,0,0,12" BorderThickness="1"
                                    Background="{DynamicResource CardBackgroundFillColorDefaultBrush}" BorderBrush="{DynamicResource CardStrokeColorDefaultBrush}">
                                <StackPanel>
                                    <DockPanel>
                                        <Border DockPanel.Dock="Right" CornerRadius="10" Padding="8,2" Background="{DynamicResource SubtleFillColorSecondaryBrush}" VerticalAlignment="Top">
                                            <TextBlock Text="{Binding StatusText}" FontSize="12" FontWeight="SemiBold" Style="{StaticResource ToneText}"
                                                       AutomationProperties.AutomationId="VerifyStatusText" />
                                        </Border>
                                        <TextBlock FontFamily="{StaticResource IconFont}" FontSize="16" Text="&#xE9D5;" Margin="0,0,8,0" VerticalAlignment="Center" Style="{StaticResource ToneText}" />
                                        <TextBlock Text="{Binding Title}" FontSize="15" FontWeight="SemiBold" VerticalAlignment="Center" />
                                    </DockPanel>
                                    <TextBlock Text="{Binding FolderText}" FontFamily="Consolas" FontSize="12" Margin="0,6,0,0"
                                               Foreground="{DynamicResource TextFillColorSecondaryBrush}" TextTrimming="CharacterEllipsis" ToolTip="{Binding FolderText}" />
                                    <StackPanel Margin="0,10,0,0" Visibility="{Binding IsRunning, Converter={StaticResource BoolToVisible}}">
                                        <ProgressBar Value="{Binding Progress, Mode=OneWay}" Maximum="1" Height="4" />
                                        <TextBlock Text="{Binding DetailText}" Style="{StaticResource Caption}" Margin="0,6,0,0" FontSize="13" />
                                    </StackPanel>
                                    <TextBlock Text="{Binding ResultText}" FontSize="13" Margin="0,8,0,0" TextWrapping="Wrap"
                                               AutomationProperties.AutomationId="VerifyResultText"
                                               Visibility="{Binding ResultText, Converter={StaticResource NullToCollapsed}}">
                                        <TextBlock.Style>
                                            <Style TargetType="TextBlock">
                                                <Setter Property="Foreground" Value="{DynamicResource SystemFillColorSuccessBrush}" />
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding ResultTone}" Value="Error">
                                                        <Setter Property="Foreground" Value="{DynamicResource SystemFillColorCriticalBrush}" />
                                                    </DataTrigger>
                                                    <DataTrigger Binding="{Binding ResultTone}" Value="Warning">
                                                        <Setter Property="Foreground" Value="{DynamicResource SystemFillColorCautionBrush}" />
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBlock.Style>
                                    </TextBlock>
                                    <ItemsControl ItemsSource="{Binding Warnings}" Margin="0,4,0,0">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <TextBlock Text="{Binding}" Style="{StaticResource WarningText}" FontSize="13" />
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                    <TextBlock Text="{Binding ErrorText}" Style="{StaticResource ErrorText}" FontSize="13" Margin="0,8,0,0" Visibility="{Binding ErrorText, Converter={StaticResource NullToCollapsed}}" />
                                    <DockPanel Margin="0,12,0,0">
                                        <Button DockPanel.Dock="Right" Content="Zrušit" Command="{Binding CancelCommand}" Style="{StaticResource CardButton}" Margin="0"
                                                AutomationProperties.AutomationId="VerifyCancelButton" Visibility="{Binding CanCancel, Converter={StaticResource BoolToVisible}}" />
                                        <StackPanel Orientation="Horizontal">
                                            <Button Content="Report ověření" Command="{Binding OpenReportCommand}" Style="{StaticResource CardButton}"
                                                    AutomationProperties.AutomationId="VerifyReportButton" Visibility="{Binding HasReport, Converter={StaticResource BoolToVisible}}" />
                                            <Button Content="Průzkumník" Command="{Binding RevealCommand}" Style="{StaticResource CardButton}" AutomationProperties.AutomationId="VerifyExplorerButton" />
                                        </StackPanel>
                                    </DockPanel>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                        </local:QueueItemTemplateSelector.VerifyTemplate>
                        </local:QueueItemTemplateSelector>
                    </ItemsControl.ItemTemplateSelector>
                </ItemsControl>
            </ScrollViewer>
```

- [ ] **Step 4: Testy + build**

Run: `dotnet test --filter "FullyQualifiedName~VerifyJobTests|FullyQualifiedName~JobQueueTests"` → PASS; `dotnet build` (XAML) bez varování; `dotnet test` → zelená. Ruční ověření: úkol 19, bod 18.

- [ ] **Step 5: VYVOJ + commit**

Řádek:

```markdown
- Úkol 12 — GUI: Nástroje → Ověřit zálohu… (Ctrl+Shift+O) na složku karty nebo celý den; každé ověření je položka kopírovací fronty „Ověření A001“ s průběhem, výsledkem česky (zeleně / červeně / oranžově), tlačítky Report ověření, Průzkumník, Zrušit; „Ukládat report ověření“ jako zaškrtávací položka menu (OpenFolderDialog neumí vlastní volbu)
```

```powershell
git add windows docs
git commit -m "Windows GUI: verify backups from the Tools menu" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: `CardRecord` (jádro) + `AppendPlanner`

Spec §2.2 „Jádro“ (R4, R7), hlášky před kopírováním, §3.1 (jádro `CardRecord`), §6.1 úkol 13. Pravdou je obsah cílové složky (reporty všech generací + soubory na disku), ne historie.

**Files:**
- Create: `windows/src/Offload.Core/Append/AppendPlanner.cs`
- Create: `windows/src/Offload.Core/CardRecord.cs`
- Modify: `windows/src/Offload.Core/Messages.cs` (řádky 24–27, 67–70)
- Modify: `windows/src/Offload.Core/OffloadReport.cs` (řádky 52–55)
- Create: `windows/tests/Offload.Core.Tests/AppendPlannerTests.cs`
- Create: `windows/tests/Offload.Core.Tests/CardRecordTests.cs`

**Interfaces:**
- Consumes: `OffloadReport.FromJson`, `MhlReader.ReadChain` (úkol 10), `FileTimes.Close/StoresLocalTime` (úkol 3), `Format.BytesCs`.
- Produces: `OffloadReport.Generation` (`int?`, `"generation"`, chybí = 1); `record CardRecordGeneration(int Generation, OffloadReport Report)`, `record RecordedFile(ReportFile File, int Generation, OffloadReport Report)`, `CardRecord.Load(string cardPath)` (`Generations`, `Files`, `ReportMissing`, `Error`, `Problems`), `CardRecord.DestinationIndex(OffloadReport, string cardPath, int position)`; `AppendPlan` (`Source`, `CardPaths`, `DestRoots`, `Date`, `CardName`, `Generation`, `FullScan`, `NewFiles`, `NewDirs`, `UnverifiedEarlier`, `HashAlgorithms`); `AppendPlanner.Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default)` (+ interní přetížení s `sourceFilesystem` pro testy), `AppendPlanner.Disk(string cardPath)`; `PreflightCode` + `AppendUnavailable, AppendNoReport, AppendNoMhl, AppendDiverged, AppendConflict, AppendMissingFile, AppendFailedFile, NothingNew`; `Msg.Append*`, `Msg.NothingNew`, `Msg.UnverifiedEarlier(int)`, `Msg.CardNotInHistory`.

- [ ] **Step 1: Padající testy**

```csharp
namespace Offload.Core.Tests;

public class CardRecordTests
{
    [Fact]
    public void Generations_Merged_NewestWins()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        var full = Reports.Sample(card);
        full.Write(card, includeHashRecords: false);
        var append = Reports.Sample(card);
        append.Generation = 2;
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), append.ToJson());

        var r = CardRecord.Load(card);

        Assert.Null(r.Error);
        Assert.False(r.ReportMissing);
        Assert.Equal([1, 2], r.Generations.Select(g => g.Generation));
        Assert.Equal(2, r.Files["CLIP/A&B.mov"].Generation);
        Assert.Equal(0, CardRecord.DestinationIndex(full, card, 5));
    }

    [Fact]
    public void Missing_Broken_AndABrokenAppendReport()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Assert.True(CardRecord.Load(card).ReportMissing);
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report.json"), "{broken");
        Assert.NotNull(CardRecord.Load(card).Error);

        Reports.Sample(card).Write(card, includeHashRecords: false);
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), "{broken");
        var r = CardRecord.Load(card);
        Assert.Null(r.Error);
        Assert.Single(r.Generations);
        Assert.StartsWith("offload_report_0002.json: ", Assert.Single(r.Problems));
    }

    [Fact]
    public void DestinationIndex_ByPath_OnlyOne_OrPosition()
    {
        var two = Reports.Sample(@"D:\Zaloha\2026-10-04\A001");
        Assert.Equal(0, CardRecord.DestinationIndex(two, @"d:\zaloha\2026-10-04\A001\", 3));   // the only destination
    }
}
```

```csharp
using System.IO.Hashing;
using System.Text.Json.Nodes;
using Offload.Core.Append;

namespace Offload.Core.Tests;

public static class AppendFixtures
{
    public static OffloadOptions Options() => new() { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    /// Card A001 with two clips, copied in full onto d1 and d2 into 2026-10-06\A001.
    public static (string Card, IReadOnlyList<string> CardPaths) CopiedCard(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 60_000, 2);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-06");
        engine.Run(plan);
        return (card, plan.CardPaths);
    }

    public static void NewClip(string card, string name = "A001C003.mov", int size = 30_000, int seed = 3) =>
        TestFs.WriteRandom(Path.Combine(card, "CLIP", name), size, seed);
}

public class AppendPlannerTests
{
    static OffloadError Refused(string card, IReadOnlyList<string> cardPaths, string? filesystem = null) =>
        Assert.Throws<OffloadError>(() => AppendPlanner.Plan(card, cardPaths, CancellationToken.None, filesystem));

    [Fact]
    public void NewFiles_Generation2_FoldersFromThePaths()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);

        var plan = AppendPlanner.Plan(card, paths);

        Assert.Equal(["CLIP/A001C003.mov"], plan.NewFiles.Select(f => f.RelPath));
        Assert.Equal(["CLIP"], plan.NewDirs);
        Assert.Equal(2, plan.Generation);
        Assert.Equal(("2026-10-06", "A001"), (plan.Date, plan.CardName));
        Assert.Equal([dir.Sub("d1"), dir.Sub("d2")], plan.DestRoots);
        Assert.Equal(3, plan.FullScan.Files.Count);
        Assert.Equal(0, plan.UnverifiedEarlier);
        Assert.Equal(["xxh64"], plan.HashAlgorithms);
    }

    [Fact]
    public void NothingNew_EmptyPlan()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        Assert.Empty(AppendPlanner.Plan(card, paths).NewFiles);
    }

    [Fact]
    public void SizeConflict_StopsWithTheCzechList()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var c1 = Path.Combine(card, "CLIP", "A001C001.mov");
        var t = File.GetLastWriteTimeUtc(c1);
        File.AppendAllText(c1, "x");
        File.SetLastWriteTimeUtc(c1, t);

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.AppendConflict, e.Code);
        Assert.StartsWith("Doplnění zastaveno — nic se nezkopírovalo. 1 soubor na kartě má stejné jméno jako v záloze, ale jinou velikost nebo datum:\n  CLIP/A001C001.mov (karta ", e.Message);
        Assert.EndsWith("\nUdělejte celou kopii (vznikne A001_2).", e.Message);
    }

    [Fact]
    public void MtimeConflict()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var c1 = Path.Combine(card, "CLIP", "A001C001.mov");
        File.SetLastWriteTimeUtc(c1, File.GetLastWriteTimeUtc(c1).AddMinutes(10));
        Assert.Equal(PreflightCode.AppendConflict, Refused(card, paths).Code);
    }

    // Review focus 2: a FAT32 card after the change of time — every file an hour off, still the same files.
    [Fact]
    public void FatHourShift_IsNoConflict_ButOnExFatItIs()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        foreach (var f in Directory.GetFiles(card, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(f, File.GetLastWriteTimeUtc(f).AddHours(-1));
        AppendFixtures.NewClip(card);

        Assert.Single(AppendPlanner.Plan(card, paths, CancellationToken.None, "FAT32").NewFiles);
        Assert.Equal(PreflightCode.AppendConflict, Refused(card, paths, "exFAT").Code);
    }

    [Fact]
    public void FileMissingOnADestination()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        File.Delete(Layout.Join(paths[1], "CLIP/A001C002.mov"));
        var e = Refused(card, paths);
        Assert.Equal(PreflightCode.AppendMissingFile, e.Code);
        Assert.Equal($"Na {AppendPlanner.Disk(paths[1])} chybí soubor z dřívější zálohy: CLIP/A001C002.mov — zkontrolujte zálohu přes Nástroje → Ověřit zálohu…", e.Message);
    }

    static void MarkFailed(string cardPath, string rel)
    {
        var json = Path.Combine(cardPath, "_REPORT", "offload_report.json");
        var doc = JsonNode.Parse(File.ReadAllText(json))!;
        foreach (var f in doc["files"]!.AsArray())
            if ((string?)f!["path"] == rel) f["destinations"] = new JsonArray("failed", "failed");
        File.WriteAllText(json, doc.ToJsonString());
    }

    [Fact]
    public void FailedInTheReport_FileStillThere_Refused()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        MarkFailed(paths[0], "CLIP/A001C002.mov");
        var e = Refused(card, paths);
        Assert.Equal(PreflightCode.AppendFailedFile, e.Code);
        Assert.Equal($"CLIP/A001C002.mov se při dřívější záloze na {AppendPlanner.Disk(paths[0])} nezkopíroval správně — doplnit nejde, udělejte celou kopii.", e.Message);
    }

    // A cancelled append leaves "failed" records of files it never wrote: they are simply copied now.
    [Fact]
    public void FailedInTheReport_FileNowhere_IsCopiedAgain()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        foreach (var p in paths)
        {
            MarkFailed(p, "CLIP/A001C002.mov");
            File.Delete(Layout.Join(p, "CLIP/A001C002.mov"));
        }
        Assert.Equal(["CLIP/A001C002.mov"], AppendPlanner.Plan(card, paths).NewFiles.Select(f => f.RelPath));
    }

    // Review focus 3: the destinations have a different number of MHL generations.
    [Fact]
    public void DifferentGenerations_Diverged()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var gen1 = Path.GetFileName(Directory.GetFiles(Path.Combine(paths[1], "ascmhl"), "0001_*.mhl").Single());
        var gen2 = MhlFixtures.Generation(paths[1], 2, [("CLIP/x.mov", 1, [("xxh64", "0000000000000000")], "original")]);
        MhlFixtures.Chain(paths[1], gen1, gen2);

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        var disk = AppendPlanner.Disk(paths[0]);
        Assert.Equal($"Zálohy na cílech se liší ({disk} má 1 generaci MHL, {disk} 2) — zkontrolujte je přes Nástroje → Ověřit zálohu…; doplnit nejde.", e.Message);
    }

    [Fact]
    public void UnknownFileOnADestination_IsAConflict()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        AppendFixtures.NewClip(Path.Combine(paths[0]), size: 999, seed: 9);   // the same name already on d1, not in any report
        Assert.Equal(PreflightCode.AppendConflict, Refused(card, paths).Code);
    }

    [Fact]
    public void MissingBackup_Report_Mhl()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var gone = Path.Combine(dir.Path, "gone", "2026-10-06", "A001");
        Assert.Equal(PreflightCode.AppendUnavailable, Refused(card, [paths[0], gone]).Code);
        Assert.Equal($"Dřívější záloha není dostupná: {gone} — připojte disk, nebo udělejte celou kopii.", Refused(card, [gone]).Message);

        Directory.Delete(Path.Combine(paths[1], "ascmhl"), recursive: true);
        Assert.Equal($"V {paths[1]} chybí ASC MHL (ascmhl\\ascmhl_chain.xml) — doplnit nejde.", Refused(card, paths).Message);

        File.Delete(Path.Combine(paths[0], "_REPORT", "offload_report.json"));
        Assert.Equal($"V {paths[0]} chybí report dřívější zálohy (_REPORT\\offload_report.json) — doplnit nejde.", Refused(card, paths).Message);
    }

    // Spec 2.2 "záloha udělaná na Macu": a report without the Windows-only keys, a Mac card path.
    [Fact]
    public void MacBackup_Fixture()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var t = new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc);
        var data = TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 1000, 1);
        File.SetLastWriteTimeUtc(Path.Combine(card, "CLIP", "A001C001.mov"), t);
        var backup = dir.Sub("mac", "2026-10-06", "A001");
        Directory.CreateDirectory(Path.Combine(backup, "CLIP"));
        File.WriteAllBytes(Path.Combine(backup, "CLIP", "A001C001.mov"), data);
        File.SetLastWriteTimeUtc(Path.Combine(backup, "CLIP", "A001C001.mov"), t);
        var hash = Format.Hex64(XxHash64.HashToUInt64(data));
        Directory.CreateDirectory(Path.Combine(backup, "_REPORT"));
        File.WriteAllText(Path.Combine(backup, "_REPORT", "offload_report.json"), $$"""
            {
              "cardName" : "A001", "clips" : [ ], "copyFinished" : "2026-10-06T07:10:00Z", "date" : "2026-10-06",
              "destinations" : [ { "cardPath" : "/Volumes/Zaloha/2026-10-06/A001", "filesFailed" : 0, "filesOK" : 1, "filesystem" : "apfs", "root" : "/Volumes/Zaloha" } ],
              "files" : [ { "destinations" : [ "verified" ], "errors" : [ null ], "modified" : "2026-10-06T07:00:00Z", "path" : "CLIP/A001C001.mov", "size" : 1000, "xxh64" : "{{hash}}" } ],
              "finished" : "2026-10-06T07:11:00Z", "hashAlgorithm" : "xxh64", "skipped" : [ ], "source" : "/Volumes/A001", "sourceFilesystem" : "exfat",
              "started" : "2026-10-06T07:09:00Z", "tool" : "offload", "totalBytes" : 1000, "totalFiles" : 1, "verified" : true, "version" : "0.1.0"
            }
            """);
        var gen1 = MhlFixtures.Generation(backup, 1, [("CLIP/A001C001.mov", 1000, [("xxh64", hash)], "original")]);
        MhlFixtures.Chain(backup, gen1);
        AppendFixtures.NewClip(card);

        var plan = AppendPlanner.Plan(card, [backup]);

        Assert.Equal(["CLIP/A001C003.mov"], plan.NewFiles.Select(f => f.RelPath));
        Assert.Equal(2, plan.Generation);
    }
}
```

(`AppendFixtures` z tohoto souboru používá i úkol 14.)

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~CardRecordTests|FullyQualifiedName~AppendPlannerTests"` → build FAIL.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/OffloadReport.cs`, řádky 52–55 — najít:

```csharp
    /// Windows only: the ffmpeg encoder the proxies were rendered with (e.g. "h264_nvenc"); omitted when none were rendered.
    [JsonPropertyName("proxyEncoder")] public string? ProxyEncoder { get; set; }

    [JsonIgnore] public double CopySeconds => (CopyFinished - Started).TotalSeconds;
```

nahradit:

```csharp
    /// Windows only: the ffmpeg encoder the proxies were rendered with (e.g. "h264_nvenc"); omitted when none were rendered.
    [JsonPropertyName("proxyEncoder")] public string? ProxyEncoder { get; set; }
    /// The ASC MHL generation this report belongs to: absent (= 1) for the full copy, 2, 3 … for appends (spec 2.2).
    [JsonPropertyName("generation")] public int? Generation { get; set; }

    [JsonIgnore] public double CopySeconds => (CopyFinished - Started).TotalSeconds;
```
`windows/src/Offload.Core/Messages.cs`, řádky 24–27 — najít:

```csharp
    NoHashRecord,
    NoFilesToRead,
}

```

nahradit:

```csharp
    NoHashRecord,
    NoFilesToRead,
    AppendUnavailable,
    AppendNoReport,
    AppendNoMhl,
    AppendDiverged,
    AppendConflict,
    AppendMissingFile,
    AppendFailedFile,
    /// Not an error: the backup already has everything (the job ends "Hotovo").
    NothingNew,
}

```

`windows/src/Offload.Core/Messages.cs`, řádky 67–70 — najít:

```csharp
    public static string CardUnreadable(string detail) => $"Kartu nejde přečíst: {detail}";

    // CLI
    public const string CliError = "Chyba: ";
```

nahradit:

```csharp
    public static string CardUnreadable(string detail) => $"Kartu nejde přečíst: {detail}";

    // Append (spec 2.2)
    public static string AppendUnavailable(string cardPath) =>
        $"Dřívější záloha není dostupná: {cardPath} — připojte disk, nebo udělejte celou kopii.";
    public static string AppendNoReport(string cardPath) =>
        $"V {cardPath} chybí report dřívější zálohy (_REPORT\\offload_report.json) — doplnit nejde.";
    public static string AppendBadReport(string cardPath, string detail) =>
        $"Report dřívější zálohy v {cardPath} nejde přečíst ({detail}) — doplnit nejde.";
    public static string AppendNoMhl(string cardPath) =>
        $"V {cardPath} chybí ASC MHL (ascmhl\\ascmhl_chain.xml) — doplnit nejde.";
    public static string AppendDiverged(string details) =>
        $"Zálohy na cílech se liší ({details}) — zkontrolujte je přes Nástroje → Ověřit zálohu…; doplnit nejde.";
    public static string AppendConflict(IReadOnlyList<string> lines, string fullCopyFolder) =>
        $"Doplnění zastaveno — nic se nezkopírovalo. {lines.Count} {Desktop.Texts.Plural(lines.Count, "soubor na kartě má", "soubory na kartě mají", "souborů na kartě má")} "
        + "stejné jméno jako v záloze, ale jinou velikost nebo datum:\n  "
        + string.Join("\n  ", lines.Take(10)) + (lines.Count > 10 ? "\n  …" : "")
        + $"\nUdělejte celou kopii (vznikne {fullCopyFolder}).";
    public static string AppendMissingFile(string disk, string path) =>
        $"Na {disk} chybí soubor z dřívější zálohy: {path} — zkontrolujte zálohu přes Nástroje → Ověřit zálohu…";
    public static string AppendFailedFile(string path, string disk) =>
        $"{path} se při dřívější záloze na {disk} nezkopíroval správně — doplnit nejde, udělejte celou kopii.";
    public const string NothingNew = "Nic nového — záloha je úplná.";
    public static string UnverifiedEarlier(int n) =>
        $"{n} {Desktop.Texts.Plural(n, "soubor", "soubory", "souborů")} z dřívější zálohy {Desktop.Texts.Plural(n, "nebyl ověřen", "nebyly ověřeny", "nebylo ověřeno")} — doporučeno Ověřit zálohu.";
    public const string CardNotInHistory = "Karta není v historii — zadejte složky zálohy volbou --into.";

    // CLI
    public const string CliError = "Chyba: ";
```

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Offload.Core;

/// One report of a card folder: the full copy (generation 1, offload_report.json) or an append (offload_report_NNNN.json).
public sealed record CardRecordGeneration(int Generation, OffloadReport Report);

/// A file as the card folder's reports record it (spec 3.1): the newest generation that lists it.
public sealed record RecordedFile(ReportFile File, int Generation, OffloadReport Report);

/// What the reports in `<card>\_REPORT` say about a card folder — every generation, also reports written by the Mac
/// (no "generation", "lut", "proxyEncoder" …). The core for appends (plan 6); plan 7 adds the day report and clip lists.
public sealed partial class CardRecord
{
    public const string MainReport = "offload_report.json";

    public required string CardPath { get; init; }
    public IReadOnlyList<CardRecordGeneration> Generations { get; init; } = [];
    public IReadOnlyDictionary<string, RecordedFile> Files { get; init; } = new Dictionary<string, RecordedFile>();
    /// _REPORT\offload_report.json is not there.
    public bool ReportMissing { get; init; }
    /// Why the main report could not be read; null when it was read (or is missing).
    public string? Error { get; init; }
    /// Append reports that could not be read (file name + reason).
    public IReadOnlyList<string> Problems { get; init; } = [];

    [GeneratedRegex(@"^offload_report_(\d{4})\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex AppendReport();

    public static CardRecord Load(string cardPath)
    {
        var dir = Path.Combine(cardPath, Layout.ReportDirName);
        var main = Path.Combine(dir, MainReport);
        if (!File.Exists(main)) return new CardRecord { CardPath = cardPath, ReportMissing = true };
        var generations = new List<CardRecordGeneration>();
        var problems = new List<string>();
        try
        {
            var r = OffloadReport.FromJson(File.ReadAllText(main));
            generations.Add(new CardRecordGeneration(r.Generation ?? 1, r));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            return new CardRecord { CardPath = cardPath, Error = e.Message };
        }
        foreach (var file in Directory.GetFiles(dir, "offload_report_*.json"))
        {
            if (AppendReport().Match(Path.GetFileName(file)) is not { Success: true } m) continue;
            try
            {
                var r = OffloadReport.FromJson(File.ReadAllText(file));
                generations.Add(new CardRecordGeneration(r.Generation ?? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), r));
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
            {
                problems.Add($"{Path.GetFileName(file)}: {e.Message}");
            }
        }
        generations.Sort((a, b) => a.Generation.CompareTo(b.Generation));
        var files = new Dictionary<string, RecordedFile>(StringComparer.Ordinal);
        foreach (var g in generations)
            foreach (var f in g.Report.Files) files[f.Path] = new RecordedFile(f, g.Generation, g.Report);
        return new CardRecord { CardPath = cardPath, Generations = generations, Files = files, Problems = problems };
    }

    /// Which column of a report's per-destination states belongs to `cardPath`: the destination with that card path;
    /// otherwise (a report from the Mac, a disk under another letter) the only one, or the same position.
    public static int DestinationIndex(OffloadReport report, string cardPath, int position)
    {
        for (int i = 0; i < report.Destinations.Count; i++)
            if (string.Equals(report.Destinations[i].CardPath.TrimEnd('\\', '/'), cardPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return i;
        return report.Destinations.Count == 1 ? 0 : Math.Clamp(position, 0, Math.Max(0, report.Destinations.Count - 1));
    }
}
```

```csharp
using System.Globalization;
using Offload.Core.History;
using Offload.Core.Verification;

namespace Offload.Core.Append;

/// What an append copies (spec 2.2): only the card's new files, into the same card folders, as ASC MHL generation `Generation`.
public sealed class AppendPlan
{
    public required string Source { get; init; }
    /// `<root>\<date>\<card>` on every destination of the earlier backup, in its order.
    public required IReadOnlyList<string> CardPaths { get; init; }
    public required IReadOnlyList<string> DestRoots { get; init; }
    public required string Date { get; init; }
    public required string CardName { get; init; }
    /// The new ASC MHL generation: the number of generations already there + 1 (the same on every destination, R7).
    public required int Generation { get; init; }
    /// The whole card now (history fingerprint and file list).
    public required SourceScan FullScan { get; init; }
    /// The files the backup does not have yet, and the folders they are in.
    public required IReadOnlyList<SourceFile> NewFiles { get; init; }
    public required IReadOnlyList<string> NewDirs { get; init; }
    /// Files of the earlier backup that were written but never read back.
    public int UnverifiedEarlier { get; init; }
    /// The hash types of the earlier backup — an append uses them, not the current setting (spec 3.5).
    public IReadOnlyList<string> HashAlgorithms { get; init; } = ["xxh64"];
}

/// Pure decisions over the card and the content of the card folders (R4): the truth is what is on the destinations —
/// every report generation and the files themselves — not history. Reads only; throws OffloadError.Preflight before
/// anything is written when an append is not safe.
public static class AppendPlanner
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// "D:" for "D:\Zaloha\2026-10-06\A001", the share for a UNC path.
    public static string Disk(string cardPath) => (Path.GetPathRoot(cardPath) ?? cardPath).TrimEnd('\\');

    static string When(DateTime utc) => utc.ToLocalTime().ToString("d. M. H:mm", Cs);

    public static AppendPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default) =>
        Plan(source, cardPaths, ct, sourceFilesystem: null);

    /// `sourceFilesystem`: tests pretend a FAT32 card; null = the card's own.
    internal static AppendPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct, string? sourceFilesystem)
    {
        if (cardPaths.Count == 0) throw OffloadError.Preflight(PreflightCode.NoDestination, Msg.NoDestination);
        var paths = cardPaths.Select(p => Path.GetFullPath(p).TrimEnd('\\')).ToList();

        // 1. Every earlier backup is there, with its report and its ASC MHL chain (R7).
        var records = new List<CardRecord>();
        var generations = new List<int>();
        foreach (var cardPath in paths)
        {
            if (!Directory.Exists(cardPath)) throw OffloadError.Preflight(PreflightCode.AppendUnavailable, Msg.AppendUnavailable(cardPath));
            var record = CardRecord.Load(cardPath);
            if (record.ReportMissing) throw OffloadError.Preflight(PreflightCode.AppendNoReport, Msg.AppendNoReport(cardPath));
            if (record.Error != null) throw OffloadError.Preflight(PreflightCode.AppendNoReport, Msg.AppendBadReport(cardPath, record.Error));
            if (MhlReader.ReadChain(Path.Combine(cardPath, Mhl.DirName)) is not { Count: > 0 } chain)
                throw OffloadError.Preflight(PreflightCode.AppendNoMhl, Msg.AppendNoMhl(cardPath));
            records.Add(record);
            generations.Add(chain.Max(c => c.Seq));
        }
        if (generations.Distinct().Count() > 1)
        {
            var details = string.Join(", ", paths.Select((p, i) =>
                i == 0 ? $"{Disk(p)} má {generations[i]} {Generace(generations[i])} MHL" : $"{Disk(p)} {generations[i]}"));
            throw OffloadError.Preflight(PreflightCode.AppendDiverged, Msg.AppendDiverged(details));
        }

        // 2. The card, compared with every backup: a known file must be the same on the card, in the report and on disk.
        var scan = Scanner.Scan(source, ct);
        bool localTime = FileTimes.StoresLocalTime(sourceFilesystem ?? NativeIO.FilesystemType(source));
        var newFiles = new List<SourceFile>();
        var conflicts = new List<string>();
        int unverified = 0;
        foreach (var f in scan.Files)
        {
            ct.ThrowIfCancellationRequested();
            int present = 0, goodInRecord = 0;
            for (int d = 0; d < paths.Count; d++)
            {
                var onDisk = new FileInfo(Layout.Join(paths[d], f.RelPath));
                bool exists = onDisk.Exists;
                if (exists) present++;
                if (!records[d].Files.TryGetValue(f.RelPath, out var rec))
                {
                    // Not in any report but on the disk: someone else's file — never overwritten, never trusted.
                    if (exists) conflicts.Add(Conflict(f, Disk(paths[d]), onDisk.Length, onDisk.LastWriteTimeUtc));
                    continue;
                }
                int column = CardRecord.DestinationIndex(rec.Report, paths[d], d);
                var state = column < rec.File.Destinations.Count ? rec.File.Destinations[column] : "pending";
                bool good = state is "verified" or "written";
                if (!good)
                {
                    // A failed file that is not on the disk (a cancelled append) is simply copied now.
                    if (exists) throw OffloadError.Preflight(PreflightCode.AppendFailedFile, Msg.AppendFailedFile(f.RelPath, Disk(paths[d])));
                    continue;
                }
                goodInRecord++;
                if (state == "written" || !rec.Report.Verified) unverified++;
                if (rec.File.Size != f.Size || !FileTimes.Close(rec.File.Modified, f.ModifiedUtc, localTime))
                    conflicts.Add(Conflict(f, Disk(paths[d]), rec.File.Size, rec.File.Modified));
                else if (!exists)
                    throw OffloadError.Preflight(PreflightCode.AppendMissingFile, Msg.AppendMissingFile(Disk(paths[d]), f.RelPath));
                else if (onDisk.Length != f.Size || !FileTimes.Close(onDisk.LastWriteTimeUtc, f.ModifiedUtc, localTime))
                    conflicts.Add(Conflict(f, Disk(paths[d]), onDisk.Length, onDisk.LastWriteTimeUtc));
            }
            if (goodInRecord == 0 && present == 0) newFiles.Add(f);
            else if (goodInRecord > 0 && goodInRecord < paths.Count && present < paths.Count)
                throw OffloadError.Preflight(PreflightCode.AppendDiverged,
                    Msg.AppendDiverged($"{f.RelPath} je jen na {string.Join(", ", paths.Where(p => File.Exists(Layout.Join(p, f.RelPath))).Select(Disk))}"));
        }
        var cardName = Path.GetFileName(paths[0]);
        if (conflicts.Count > 0)
            throw OffloadError.Preflight(PreflightCode.AppendConflict, Msg.AppendConflict(conflicts.Distinct().ToList(), cardName + "_2"));

        var newDirs = newFiles
            .SelectMany(f => Ancestors(f.RelPath))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var first = records[0].Generations[0].Report;
        return new AppendPlan
        {
            Source = Path.GetFullPath(source),
            CardPaths = paths,
            DestRoots = paths.Select(p => Path.GetDirectoryName(Path.GetDirectoryName(p))!).ToList(),
            Date = Path.GetFileName(Path.GetDirectoryName(paths[0]))!,
            CardName = cardName,
            Generation = generations[0] + 1,
            FullScan = scan,
            NewFiles = newFiles,
            NewDirs = newDirs,
            UnverifiedEarlier = unverified / Math.Max(1, paths.Count),
            HashAlgorithms = [first.HashAlgorithm],
        };
    }

    static string Generace(int n) => Desktop.Texts.Plural(n, "generaci", "generace", "generací");

    static IEnumerable<string> Ancestors(string rel)
    {
        for (int i = rel.IndexOf('/'); i >= 0; i = rel.IndexOf('/', i + 1)) yield return rel[..i];
    }

    /// "CLIP/A001C003.mov (karta 1,2 GB · 6. 10. 9:14, D: 1,1 GB · 6. 10. 9:10)"
    static string Conflict(SourceFile card, string disk, long size, DateTime modifiedUtc) =>
        $"{card.RelPath} (karta {Format.BytesCs(card.Size)} · {When(card.ModifiedUtc)}, {disk} {Format.BytesCs(size)} · {When(modifiedUtc)})";
}
```

- [ ] **Step 4: Testy + celá sada**

Run: `dotnet test --filter "FullyQualifiedName~CardRecordTests|FullyQualifiedName~AppendPlannerTests"` → PASS (15); `dotnet test` → zelená.

- [ ] **Step 5: VYVOJ + commit**

Řádek:

```markdown
- Úkol 13 — `CardRecord` (reporty všech generací karty, i z Macu bez nových klíčů) a plán doplnění: nové soubory a jejich složky, konflikty (karta ↔ report ↔ soubor na cíli, velikost a čas s tolerancí 2 s a celých hodin na FAT), chybějící / selhaný soubor, rozdílný počet generací MHL na cílech, neověřené soubory dřívější zálohy; selhaný záznam bez souboru na cílech (zrušené doplnění) se kopíruje znovu
```

```powershell
git add windows docs
git commit -m "Windows: card record and append planner" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: Engine v režimu doplnění

Spec §2.2 „Data na disku“ (R5, R6), „Jádro“, „Okrajové případy“, §5.2 (doplnění upraví záznam), §6.1 úkol 14. `OffloadEngine.Run` zůstává beze změny (`DestWriter` přejmenovává s `replace: false`); mění se jména reportů, seznam součtů, MHL a stage 2.

**Files:**
- Modify: `windows/src/Offload.Core/HtmlReport.cs` (řádky 103–107)
- Modify: `windows/src/Offload.Core/Mhl.cs` (řádky 3–11, 27–33, 39–48, 65–75)
- Modify: `windows/src/Offload.Core/Models.cs` (řádky 38–44, 46–49)
- Modify: `windows/src/Offload.Core/OffloadEngine.cs` (řádky 1–2, 38–41)
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (řádky 23–26, 60–63, 104–108, 166–169, 184–187, 211–215, 217–251, 281–285, 348–351, 390–394, 447–454)
- Modify: `windows/src/Offload.Core/OffloadReport.cs` (řádky 113–118, 177–194)
- Modify: `windows/src/Offload.Core/ProxyRenderer.cs` (řádky 54–57, 380–384)
- Create: `windows/tests/Offload.Core.Tests/AppendEngineTests.cs`

**Interfaces:**
- Consumes: `AppendPlanner.Plan` (úkol 13), `MhlReader.Read`/`MhlChainEntry` (úkol 10), `HistoryStore` (úkol 4), kontroly enginu z úkolu 1.
- Produces: `enum PlanMode { Full, Append }`; `OffloadPlan.Mode`, `Generation` (1), `FullScan`, `UnverifiedEarlier`; `OffloadEngine.PrepareAppend(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default)`; `OffloadReport.JsonName/TextName/HtmlName/PdfName(…, int generation)`; `Mhl.Write` píše generaci `report.Generation` a přepíše řetěz; `JobConfig.AppendInto` (`IReadOnlyList<string>?`); `JobStatus.Note`, `JobStatus.Warning`; `ProxyRenderer.ReplaceExisting` (bool, výchozí true).

- [ ] **Step 1: Padající testy**

```csharp
using System.Xml.Linq;
using Offload.Core.History;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Spec 2.2: an append writes generation N into the same card folders — new files only, nothing overwritten.
public class AppendEngineTests
{
    static OffloadJob Job(string card, IReadOnlyList<string> dests, string history, IReadOnlyList<string>? appendInto = null, Action<int>? beforeFile = null)
    {
        var options = AppendFixtures.Options();
        options.BeforeFileCopy = beforeFile;
        var config = new JobConfig(card, dests) { Date = "2026-10-06", AnalyzeClips = false, HistoryFolder = history, AppendInto = appendInto };
        return new OffloadJob(config, options);
    }

    static Dictionary<string, byte[]> Snapshot(string dir) =>
        Directory.GetFiles(dir).ToDictionary(f => Path.GetFileName(f), f => File.ReadAllBytes(f));

    [Fact]
    public void Generation2_OnBothDestinations_EarlierFilesUntouched()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1"), dir.Sub("d2")], history);
        full.Run();
        var paths = full.Plan!.CardPaths;
        var before = paths.Select(p => Snapshot(Path.Combine(p, "_REPORT"))).ToList();
        AppendFixtures.NewClip(card);

        var append = Job(card, [], history, appendInto: paths);
        append.Run();

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        Assert.True(append.Status().SafeToEject);
        Assert.Equal((PlanMode.Append, 2), (append.Plan!.Mode, append.Plan.Generation));
        for (int d = 0; d < paths.Count; d++)
        {
            var report = Path.Combine(paths[d], "_REPORT");
            // the earlier generation's report files, byte for byte (the hash list is the only one that grows)
            foreach (var (name, bytes) in before[d].Where(kv => kv.Key != "A001.xxh64"))
                Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(report, name)));
            foreach (var name in new[] { "offload_report_0002.json", "offload_report_0002.txt", "A001_report_0002.html" })
                Assert.True(File.Exists(Path.Combine(report, name)), name);
            var list = File.ReadAllText(Path.Combine(report, "A001.xxh64"));
            Assert.StartsWith(System.Text.Encoding.UTF8.GetString(before[d]["A001.xxh64"]), list);
            Assert.Contains("  CLIP/A001C003.mov\n", list);
            Assert.Equal(2, list.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

            var mhl = MhlReader.Read(paths[d])!;
            Assert.Empty(mhl.ChainProblems);   // two chain entries, both C4 ids valid
            Assert.Equal([1, 2], mhl.Generations.Select(g => g.Seq));
            Assert.Equal(["CLIP/A001C003.mov"], mhl.Generations[1].Files.Select(f => f.Path));
            Assert.Equal(mhl.Generations[0].Ignore, mhl.Generations[1].Ignore);
            var gen2 = XDocument.Load(Directory.GetFiles(Path.Combine(paths[d], "ascmhl"), "0002_A001_*.mhl").Single());
            XNamespace ns = "urn:ASC:MHL:v2.0";
            Assert.Equal("transfer", gen2.Descendants(ns + "process").Single().Value);
            Assert.StartsWith("Offload doplnění: 1 nový soubor z ", gen2.Descendants(ns + "comment").Single().Value);

            var verify = BackupVerifier.Verify(paths[d], chunkSize: 4096);
            Assert.True(verify.Success);
            Assert.Equal(2, verify.Ok);
        }
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.Equal([1, 2], entry.Generations.Select(g => g.Seq));
        Assert.Equal(HistoryGeneration.Append, entry.Generations[1].Kind);
        Assert.Equal(2, entry.Files);
        Assert.Equal(CardFingerprint.Compute(Scanner.Scan(card)).Fingerprint, entry.Fingerprint);
    }

    [Fact]
    public void NothingNew_IsDone_NotAnError()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history);
        full.Run();

        var append = Job(card, [], history, appendInto: full.Plan!.CardPaths);
        append.Run();

        var s = append.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.Equal("Nic nového — záloha je úplná.", s.Note);
        Assert.Null(s.Error);
        Assert.False(File.Exists(Path.Combine(full.Plan.CardPaths[0], "_REPORT", "offload_report_0002.json")));
    }

    // Review focus 4 + spec 2.2 "zrušení uprostřed": the files done stay and are recorded, history is not touched.
    [Fact]
    public void CancelledAppend_RecordsWhatIsDone_NoHistory_NextAppendCopiesTheRest()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history);
        full.Run();
        AppendFixtures.NewClip(card, "A001C002.mov", seed: 2);
        AppendFixtures.NewClip(card, "A001C003.mov", seed: 3);
        OffloadJob? append = null;
        append = Job(card, [], history, appendInto: full.Plan!.CardPaths, beforeFile: i => { if (i == 1) append!.Cancel(); });

        append.Run();

        Assert.Equal(JobPhase.Cancelled, append.Status().Phase);
        var cardPath = full.Plan.CardPaths[0];
        Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", "offload_report_0002.json")));
        Assert.Equal([1], Assert.Single(new HistoryStore(history).Load()).Generations.Select(g => g.Seq));

        var again = Job(card, [], history, appendInto: full.Plan.CardPaths);
        again.Run();
        Assert.Equal(JobPhase.Done, again.Status().Phase);
        Assert.Equal(3, again.Plan!.Generation);
        Assert.Equal(["CLIP/A001C003.mov"], again.Plan.Scan.Files.Select(f => f.RelPath));
        Assert.True(BackupVerifier.Verify(cardPath, chunkSize: 4096).Success);
    }

    [Fact]
    public void Conflict_NothingIsWritten()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        File.AppendAllText(Path.Combine(card, "CLIP", "A001C001.mov"), "x");
        AppendFixtures.NewClip(card);
        var append = Job(card, [], dir.Sub("appdata"), appendInto: paths);
        append.Run();
        Assert.Equal(JobPhase.Failed, append.Status().Phase);
        Assert.StartsWith("Doplnění zastaveno — nic se nezkopírovalo.", append.Status().Error);
        Assert.False(File.Exists(Layout.Join(paths[0], "CLIP/A001C003.mov")));
    }

    // Spec 2.2: an existing proxy is never overwritten; a new clip of the same name gets "_2".
    [FfmpegFact]
    public void Proxies_ExistingNeverOverwritten()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "CLIP1", "C0001.MP4"), seconds: 1);
        var history = dir.Sub("appdata");
        var options = new OffloadOptions { ChunkSize = 1 << 20, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ProxyEncoder = "libx264" };
        var proxy = new ProxySettings { Enabled = true, Height = 360 };
        var full = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-06", Proxy = proxy, HistoryFolder = history }, options);
        full.Run();
        var cardPath = full.Plan!.CardPaths[0];
        var first = File.ReadAllBytes(Path.Combine(cardPath, "_PROXY", "C0001.mov"));
        TestMedia.MakeClip(Path.Combine(card, "CLIP2", "C0001.MP4"), seconds: 1, timecode: "02:00:00:00");

        var append = new OffloadJob(new JobConfig(card, []) { Proxy = proxy, HistoryFolder = history, AppendInto = full.Plan.CardPaths }, options);
        var report = append.Run();

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(cardPath, "_PROXY", "C0001.mov")));
        Assert.Equal("_PROXY/C0001_2.mov", Assert.Single(report!.Clips).ProxyPath);
        Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", "A001_report_0002.html")));
    }
}
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~AppendEngineTests"` → build FAIL (`PlanMode`, `JobConfig.AppendInto` …).

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Models.cs`, řádky 38–44 — najít:

```csharp
public readonly record struct DestFileResult(FileState State, string? Error);

public sealed class OffloadPlan
{
    public required string Source { get; init; }
    public required SourceScan Scan { get; init; }
    public required string Date { get; init; }
```

nahradit:

```csharp
public readonly record struct DestFileResult(FileState State, string? Error);

public enum PlanMode { Full, Append }

public sealed class OffloadPlan
{
    public required string Source { get; init; }
    /// What is copied: the whole card, or for an append only the new files and their folders.
    public required SourceScan Scan { get; init; }
    public required string Date { get; init; }
```

`windows/src/Offload.Core/Models.cs`, řádky 46–49 — najít:

```csharp
    public required IReadOnlyList<string> DestRoots { get; init; }
    public IReadOnlyList<string> CardPaths => DestRoots.Select(r => Layout.CardPath(r, Date, CardName)).ToList();
}

```

nahradit:

```csharp
    public required IReadOnlyList<string> DestRoots { get; init; }
    public IReadOnlyList<string> CardPaths => DestRoots.Select(r => Layout.CardPath(r, Date, CardName)).ToList();
    public PlanMode Mode { get; init; } = PlanMode.Full;
    /// The ASC MHL generation this copy writes: 1 for a full copy, N for an append (spec 2.2).
    public int Generation { get; init; } = 1;
    /// Append: the whole card as it is now (history); null for a full copy (then Scan is the whole card).
    public SourceScan? FullScan { get; init; }
    /// Append: files of the earlier backup that were never read back.
    public int UnverifiedEarlier { get; init; }
}

```
`windows/src/Offload.Core/OffloadEngine.cs`, řádky 1–2 — najít:

```csharp
namespace Offload.Core;

```

nahradit:

```csharp
using Offload.Core.Append;

namespace Offload.Core;

```

`windows/src/Offload.Core/OffloadEngine.cs`, řádky 38–41 — najít:

```csharp
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan { Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots };
    }

```

nahradit:

```csharp
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan { Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots };
    }

    /// An append (spec 2.2): the same checks as Prepare (options, source, destinations, Windows names, FAT32, space),
    /// but over the new files only, into the card folders of the earlier backup. `NothingNew` when there are none.
    public OffloadPlan PrepareAppend(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default)
    {
        CheckOptions();
        source = CheckSource(source);
        FullRoots(cardPaths);
        CheckNotAnOffload(source);
        var a = AppendPlanner.Plan(source, cardPaths, ct);
        CheckDestinations(source, a.DestRoots);
        if (a.NewFiles.Count == 0) throw OffloadError.Preflight(PreflightCode.NothingNew, Msg.NothingNew);
        var scan = new SourceScan { Root = a.FullScan.Root, Files = a.NewFiles, Dirs = a.NewDirs, Skipped = a.FullScan.Skipped };
        var problems = Scanner.WindowsProblems(scan);
        if (problems.Count > 0) throw OffloadError.Preflight(PreflightCode.BadNames, Msg.BadNames(problems));
        CheckFilesFit(a.DestRoots, scan.Files, scan.TotalBytes);
        return new OffloadPlan
        {
            Source = source, Scan = scan, Date = a.Date, CardName = a.CardName, DestRoots = a.DestRoots,
            Mode = PlanMode.Append, Generation = a.Generation, FullScan = a.FullScan, UnverifiedEarlier = a.UnverifiedEarlier,
        };
    }

```
`windows/src/Offload.Core/OffloadReport.cs`, řádky 113–118 — najít:

```csharp
            Files = files,
            Skipped = plan.Scan.Skipped,
        };
    }

    /// Plain hash list, one "<xxh64>  <path>" per line — readable by humans and by `offload verify`.
```

nahradit:

```csharp
            Files = files,
            Skipped = plan.Scan.Skipped,
            Generation = plan.Generation > 1 ? plan.Generation : null,
        };
    }

    /// Report file names of generation `generation`: the full copy keeps the plain names, an append adds "_NNNN" (R6).
    public static string JsonName(int generation) => generation <= 1 ? "offload_report.json" : $"offload_report_{generation:0000}.json";
    public static string TextName(int generation) => generation <= 1 ? "offload_report.txt" : $"offload_report_{generation:0000}.txt";
    public static string HtmlName(string cardName, int generation) => generation <= 1 ? $"{cardName}_report.html" : $"{cardName}_report_{generation:0000}.html";
    public static string PdfName(string cardName, int generation) => generation <= 1 ? $"{cardName}_report.pdf" : $"{cardName}_report_{generation:0000}.pdf";

    /// Plain hash list, one "<xxh64>  <path>" per line — readable by humans and by `offload verify`.
```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 177–194 — najít:

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

nahradit:

```csharp
    /// Writes all report files into `<card>/_REPORT`. The hash list and MHL are the record of the copy itself,
    /// so they are written once (`includeHashRecords`); the HTML/JSON are refreshed as clips get analysed.
    /// An append (Generation > 1) writes its own `_NNNN` files and adds its lines to the hash list; the reports of the
    /// earlier generations are never rewritten (R5, R6).
    public void Write(string cardPath, bool includeHashRecords)
    {
        int generation = Generation ?? 1;
        var dir = System.IO.Path.Combine(cardPath, Layout.ReportDirName);
        NativeIO.Mkdirs(dir);
        var outputs = new List<(string Name, string Text)>
        {
            (JsonName(generation), ToJson()),
            (TextName(generation), Summary),
            (HtmlName(CardName, generation), HtmlReport.Render(this)),
        };
        if (includeHashRecords) outputs.Add(($"{CardName}.xxh64", generation > 1 ? EarlierHashList(dir) + HashList : HashList));
        foreach (var (name, text) in outputs)
            NativeIO.WriteFileDurably(System.IO.Path.Combine(dir, name), Encoding.UTF8.GetBytes(text));
        if (includeHashRecords) Mhl.Write(this, cardPath);
    }

    /// The card's hash list as the earlier generations left it, ending with a line break.
    string EarlierHashList(string dir)
    {
        var path = System.IO.Path.Combine(dir, $"{CardName}.xxh64");
        var text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        return text.Length == 0 || text.EndsWith('\n') ? text : text + "\n";
    }

```
`windows/src/Offload.Core/Mhl.cs`, řádky 3–11 — najít:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Offload.Core;

/// ASC MHL v2 (urn:ASC:MHL:v2.0) — the industry-standard hash record, written as the first generation
/// into `<card>/ascmhl/` so `ascmhl verify` and other DIT tools can check the copy later.
/// Optional directory/root hashes are omitted; per-file xxh64 hashes are what verification relies on.
public static class Mhl
```

nahradit:

```csharp
using System.Security.Cryptography;
using System.Text;
using Offload.Core.Desktop;
using Offload.Core.Verification;

namespace Offload.Core;

/// ASC MHL v2 (urn:ASC:MHL:v2.0) — the industry-standard hash record in `<card>/ascmhl/`, so `ascmhl verify` and other
/// DIT tools can check the copy later. A full copy writes generation 0001; an append (spec 2.2, R5) writes generation N
/// with only its new files and adds it to the chain — the earlier generations are never touched.
/// Optional directory/root hashes are omitted; per-file xxh64 hashes are what verification relies on.
public static class Mhl
```

`windows/src/Offload.Core/Mhl.cs`, řádky 27–33 — najít:

```csharp
        NativeIO.Mkdirs(dir);

        var stamp = IsoDate(now);
        var folder = Path.GetFileName(cardPath.TrimEnd('\\'));
        var fileName = $"0001_{folder}_{now.ToUniversalTime().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}Z.mhl";

        var x = new StringBuilder();
```

nahradit:

```csharp
        NativeIO.Mkdirs(dir);

        int generation = report.Generation ?? 1;
        // An append keeps the chain and the ignore list of the earlier generations (spec 2.2).
        var earlier = generation > 1 ? MhlReader.Read(cardPath) : null;
        var stamp = IsoDate(now);
        var folder = Path.GetFileName(cardPath.TrimEnd('\\'));
        var fileName = $"{generation:0000}_{folder}_{now.ToUniversalTime().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}Z.mhl";
        var comment = generation > 1
            ? $"Offload doplnění: {report.Files.Count} {Texts.Plural(report.Files.Count, "nový soubor", "nové soubory", "nových souborů")} z {report.Source}"
            : $"Offload of {report.CardName} from {report.Source}";
        IEnumerable<string> ignore = earlier?.Generations.FirstOrDefault() is { } first
            ? first.Ignore
            : IgnorePatterns.Concat(report.Skipped.Select(s => s.Split('/')[^1]));

        var x = new StringBuilder();
```

`windows/src/Offload.Core/Mhl.cs`, řádky 39–48 — najít:

```csharp
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
```

nahradit:

```csharp
        Line($"    <hostname>{Esc(host)}</hostname>");
        Line($"    <tool version=\"{Esc(report.Version)}\">{Esc(report.Tool)}</tool>");
        Line($"    <comment>{Esc(comment)}</comment>");
        Line("  </creatorinfo>");
        Line("  <processinfo>");
        Line("    <process>transfer</process>");
        Line("    <ignore>");
        foreach (var p in ignore)
            Line($"      <pattern>{Esc(p)}</pattern>");
        Line("    </ignore>");
```

`windows/src/Offload.Core/Mhl.cs`, řádky 65–75 — najít:

```csharp
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
```

nahradit:

```csharp
        NativeIO.WriteFileDurably(Path.Combine(dir, fileName), data);

        // The chain: every earlier generation exactly as it was, then this one.
        var entries = (earlier?.Chain ?? []).Where(c => c.Seq < generation).Append(new MhlChainEntry(generation, fileName, C4(data)));
        var chain = new StringBuilder();
        chain.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        chain.Append("<ascmhldirectory xmlns=\"urn:ASC:MHL:DIRECTORY:v2.0\">\n");
        foreach (var e in entries)
        {
            chain.Append($"  <hashlist sequencenr=\"{e.Seq}\">\n");
            chain.Append($"    <path>{Esc(e.Path)}</path>\n");
            chain.Append($"    <c4>{e.C4}</c4>\n");
            chain.Append("  </hashlist>\n");
        }
        chain.Append("</ascmhldirectory>\n");
        NativeIO.WriteFileDurably(Path.Combine(dir, ChainName), Encoding.UTF8.GetBytes(chain.ToString()));
```
`windows/src/Offload.Core/HtmlReport.cs`, řádky 103–107 — najít:

```csharp
        h.Append(Styles(forPrint));
        h.Append("</style></head><body><main>\n");
        h.Append($"<header><h1>{Esc(r.CardName)}</h1><span class=\"badge {statusClass}\">{status}</span>\n");
        h.Append($"<span class=\"sub\">{DateTimeText(r.Started)} – {TimeText(r.Finished)}</span></header>\n");
        h.Append("<div class=\"stats\">\n");
```

nahradit:

```csharp
        h.Append(Styles(forPrint));
        h.Append("</style></head><body><main>\n");
        var append = r.Generation > 1 ? $" <span class=\"sub\">· doplnění (generace {r.Generation})</span>" : "";
        h.Append($"<header><h1>{Esc(r.CardName)}{append}</h1><span class=\"badge {statusClass}\">{status}</span>\n");
        h.Append($"<span class=\"sub\">{DateTimeText(r.Started)} – {TimeText(r.Finished)}</span></header>\n");
        h.Append("<div class=\"stats\">\n");
```
`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 54–57 — najít:

```csharp
    /// Tests only: H.264/HEVC encoder to use instead of the probed one (OffloadOptions.ProxyEncoder).
    internal string? ForcedEncoder { get; init; }

    /// The video encoder proxies are rendered with (probed once, on first use); null without ffmpeg.
```

nahradit:

```csharp
    /// Tests only: H.264/HEVC encoder to use instead of the probed one (OffloadOptions.ProxyEncoder).
    internal string? ForcedEncoder { get; init; }

    /// False for an append (spec 2.2): a proxy of that name that appeared meanwhile is never overwritten — the render
    /// fails instead.
    public bool ReplaceExisting { get; init; } = true;

    /// The video encoder proxies are rendered with (probed once, on first use); null without ffmpeg.
```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 380–384 — najít:

```csharp
            {
                RunFfmpeg(ffmpeg, args, src, info.Duration, progress, isCancelled, StallTimeout);
                NativeIO.Move(part, output, replace: true);
            }
            catch
```

nahradit:

```csharp
            {
                RunFfmpeg(ffmpeg, args, src, info.Duration, progress, isCancelled, StallTimeout);
                NativeIO.Move(part, output, replace: ReplaceExisting);
            }
            catch
```

`OffloadJob`: doplnění místo celé kopie podle `AppendInto`, „Nic nového“ = Hotovo, varování o neověřených souborech, historie s generacemi (metoda `RecordHistory` se nahradí celá), proxy bez přepisu a `_2` proti jménům v `_PROXY`, PDF podle generace:

`windows/src/Offload.Core/OffloadJob.cs`, řádky 23–26 — najít:

```csharp
    /// Where history.json lives; null = HistoryStore.Default (%APPDATA%\Offload).
    public string? HistoryFolder { get; set; }
}

```

nahradit:

```csharp
    /// Where history.json lives; null = HistoryStore.Default (%APPDATA%\Offload).
    public string? HistoryFolder { get; set; }
    /// Append (spec 2.2): only the card's new files go into these card folders (`<root>\<date>\<card>` of the earlier
    /// backup); DestRoots, CardName and Date are then taken from them.
    public IReadOnlyList<string>? AppendInto { get; set; }
}

```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 60–63 — najít:

```csharp
    public bool? CopySucceeded { get; init; }
    public string? Error { get; init; }
}

```

nahradit:

```csharp
    public bool? CopySucceeded { get; init; }
    public string? Error { get; init; }
    /// Information for the job card, e.g. "Nic nového — záloha je úplná."
    public string? Note { get; init; }
    /// A warning that does not fail the job (orange), e.g. unverified files of the earlier backup.
    public string? Warning { get; init; }
}

```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 104–108 — najít:

```csharp
    }

    public OffloadPlan Prepare() => Plan ??= Engine.Prepare(Config.Source, Config.DestRoots, Config.CardName, Config.Date);

    public void Cancel() => Engine.Cancel();
```

nahradit:

```csharp
    }

    /// A full copy, or with Config.AppendInto an append into those card folders.
    public OffloadPlan Prepare() => Plan ??= Config.AppendInto is { Count: > 0 } into
        ? Engine.PrepareAppend(Config.Source, into)
        : Engine.Prepare(Config.Source, Config.DestRoots, Config.CardName, Config.Date);

    public void Cancel() => Engine.Cancel();
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 166–169 — najít:

```csharp
        {
            var plan = Prepare();
            Update(s => s with { Phase = JobPhase.Copying });
            var report = Engine.Run(plan);
```

nahradit:

```csharp
        {
            var plan = Prepare();
            if (plan.UnverifiedEarlier > 0) Update(s => s with { Warning = Msg.UnverifiedEarlier(plan.UnverifiedEarlier) });
            Update(s => s with { Phase = JobPhase.Copying });
            var report = Engine.Run(plan);
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 184–187 — najít:

```csharp
            Update(s => s with { Phase = JobPhase.Analyzing });
            return true;
        }
        catch (OperationCanceledException)
```

nahradit:

```csharp
            Update(s => s with { Phase = JobPhase.Analyzing });
            return true;
        }
        catch (OffloadError e) when (e.Code == PreflightCode.NothingNew)
        {
            // Not an error: the earlier backup already has every file of the card.
            Update(s => s with { Phase = JobPhase.Done, Note = e.Message });
            return false;
        }
        catch (OperationCanceledException)
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 211–215 — najít:

```csharp
    }

    /// The finished stage 1 (with failed files too) goes into history.json; this never fails the job.
    void RecordHistory(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
```

nahradit:

```csharp
    }

    /// The finished stage 1 (with failed files too) goes into history.json; this never fails the job. An append adds
    /// its generation to the entry of the card folder it went into (spec 5.2), whose card data become the card's now.
    void RecordHistory(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 217–251 — najít:

```csharp
        try
        {
            var (fingerprint, shape) = CardFingerprint.Compute(plan.Scan);
            var cardPaths = plan.CardPaths;
            var entry = new HistoryEntry
            {
                Id = HistoryStore.NewId(report.Finished),
                Card = cardVolume?.ToHistoryCard(),
                Source = plan.Source,
                CardName = plan.CardName,
                Date = plan.Date,
                Finished = report.Finished,
                Success = report.Success,
                Verified = report.Verified,
                FailedFiles = report.Success ? null : report.FailedFiles.Count,
                HashAlgorithms = [report.HashAlgorithm],
                Files = plan.Scan.Files.Count,
                Bytes = plan.Scan.TotalBytes,
                Fingerprint = fingerprint,
                Shape = shape,
                Destinations = plan.DestRoots.Select((root, i) => new HistoryDestination { Root = root, CardPath = cardPaths[i] }).ToList(),
                Generations =
                [
                    new HistoryGeneration
                    {
                        Seq = 1, Kind = HistoryGeneration.Full, Finished = report.Finished,
                        Files = plan.Scan.Files.Count, Bytes = plan.Scan.TotalBytes, Verified = report.Verified,
                    },
                ],
                Reels = ReelSuggestion.FromClips(ClipDetector.Detect(plan.Scan.Files)).Reels.ToList(),
            };
            var store = Config.HistoryFolder is string folder ? new HistoryStore(folder, log) : HistoryStore.Default(log);
            store.Record(entry, plan.Scan.Files);
        }
        catch (Exception e)
```

nahradit:

```csharp
        try
        {
            var store = Config.HistoryFolder is string folder ? new HistoryStore(folder, log) : HistoryStore.Default(log);
            var card = plan.FullScan ?? plan.Scan;
            var (fingerprint, shape) = CardFingerprint.Compute(card);
            var cardPaths = plan.CardPaths;
            var entry = plan.Mode == PlanMode.Append
                ? store.Load().FirstOrDefault(e => e.Destinations.Any(d => string.Equals(d.CardPath, cardPaths[0], StringComparison.OrdinalIgnoreCase)))
                : null;
            entry ??= new HistoryEntry
            {
                Id = HistoryStore.NewId(report.Finished),
                Source = plan.Source,
                CardName = plan.CardName,
                Date = plan.Date,
                Success = true,
                Verified = true,
                HashAlgorithms = [report.HashAlgorithm],
                Destinations = plan.DestRoots.Select((root, i) => new HistoryDestination { Root = root, CardPath = cardPaths[i] }).ToList(),
            };
            entry.Card = cardVolume?.ToHistoryCard() ?? entry.Card;
            entry.Finished = report.Finished;
            entry.Success &= report.Success;
            entry.Verified &= report.Verified;
            entry.FailedFiles = entry.Success ? null : (entry.FailedFiles ?? 0) + report.FailedFiles.Count;
            entry.Files = card.Files.Count;
            entry.Bytes = card.TotalBytes;
            entry.Fingerprint = fingerprint;
            entry.Shape = shape;
            entry.Generations.Add(new HistoryGeneration
            {
                Seq = plan.Generation,
                Kind = plan.Mode == PlanMode.Append ? HistoryGeneration.Append : HistoryGeneration.Full,
                Finished = report.Finished,
                Files = plan.Scan.Files.Count,
                Bytes = plan.Scan.TotalBytes,
                Verified = report.Verified,
            });
            entry.Reels = ReelSuggestion.FromClips(ClipDetector.Detect(card.Files)).Reels.ToList();
            store.Record(entry, card.Files);
        }
        catch (Exception e)
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 281–285 — najít:

```csharp
        var clips = ClipDetector.Detect(plan.Scan.Files);
        if (clips.Count == 0) return report;
        using var renderer = new ProxyRenderer(Config.Proxy, Config.Tools.Resolved()) { ForcedEncoder = Engine.Options.ProxyEncoder, Log = log };
        var cardPaths = plan.CardPaths;
        var fileIndex = report.Files.Select((f, i) => (f.Path, i)).ToDictionary(x => x.Path, x => x.i);
```

nahradit:

```csharp
        var clips = ClipDetector.Detect(plan.Scan.Files);
        if (clips.Count == 0) return report;
        using var renderer = new ProxyRenderer(Config.Proxy, Config.Tools.Resolved())
        {
            ForcedEncoder = Engine.Options.ProxyEncoder,
            Log = log,
            ReplaceExisting = plan.Mode == PlanMode.Full,   // an append never overwrites a proxy (spec 2.2)
        };
        var cardPaths = plan.CardPaths;
        var fileIndex = report.Files.Select((f, i) => (f.Path, i)).ToDictionary(x => x.Path, x => x.i);
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 348–351 — najít:

```csharp
        Update(s => s with { Phase = JobPhase.Proxies, ClipCount = entries.Count });
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Count; i++)
        {
```

nahradit:

```csharp
        Update(s => s with { Phase = JobPhase.Proxies, ClipCount = entries.Count });
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (plan.Mode == PlanMode.Append) usedNames.UnionWith(ExistingProxyNames(cardPaths));
        for (int i = 0; i < entries.Count; i++)
        {
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 390–394 — najít:

```csharp
                    {
                        if (o == d) continue;
                        try { ProxyCopy.CopyFile(Layout.Join(cardPaths[d], rel), Layout.Join(cardPaths[o], rel)); }
                        catch (Exception e)
                        {
```

nahradit:

```csharp
                    {
                        if (o == d) continue;
                        try { ProxyCopy.CopyFile(Layout.Join(cardPaths[d], rel), Layout.Join(cardPaths[o], rel), replace: plan.Mode == PlanMode.Full); }
                        catch (Exception e)
                        {
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 447–454 — najít:

```csharp
    }

    /// PDF copy of the report on every destination. Rendered once, then copied; a failure here never fails the offload.
    static void WritePdfReports(OffloadReport report, OffloadPlan plan, Action<string>? log)
    {
        var name = $"{plan.CardName}_report.pdf";
        var first = Path.Combine(plan.CardPaths[0], Layout.ReportDirName, name);
        try
```

nahradit:

```csharp
    }

    /// The proxies earlier generations rendered: a new clip of the same name becomes "_2" in an append (spec 2.2).
    static List<string> ExistingProxyNames(IReadOnlyList<string> cardPaths)
    {
        var names = new List<string>();
        foreach (var p in cardPaths)
        {
            var dir = Path.Combine(p, Layout.ProxyDirName);
            try
            {
                if (Directory.Exists(dir)) names.AddRange(Directory.EnumerateFiles(dir, "*.mov").Select(f => Path.GetFileNameWithoutExtension(f)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A destination that went away: its proxies cannot collide with anything written now.
            }
        }
        return names;
    }

    /// PDF copy of the report on every destination. Rendered once, then copied; a failure here never fails the offload.
    static void WritePdfReports(OffloadReport report, OffloadPlan plan, Action<string>? log)
    {
        var name = OffloadReport.PdfName(plan.CardName, plan.Generation);
        var first = Path.Combine(plan.CardPaths[0], Layout.ReportDirName, name);
        try
```

- [ ] **Step 4: Testy + celá sada**

Run: `dotnet test --filter "FullyQualifiedName~AppendEngineTests|FullyQualifiedName~MhlTests|FullyQualifiedName~EngineTests|FullyQualifiedName~ReportTests|FullyQualifiedName~HistoryJobTests"` → PASS (`Proxies_ExistingNeverOverwritten` potřebuje ffmpeg, ~1 min); `dotnet test` → zelená.

- [ ] **Step 5: VYVOJ + commit**

Řádek:

```markdown
- Úkol 14 — doplnění v jádru: `PrepareAppend` (stejné kontroly jako celá kopie nad novými soubory), generace N ASC MHL jen s novými soubory + přepsaný řetěz s C4, `offload_report_NNNN.json/.txt`, `<karta>_report_NNNN.html/.pdf`, `<karta>.xxh64` rozšířený (původní řádky beze změny), proxy se nikdy nepřepíše (shoda jména → `_2`), „Nic nového — záloha je úplná.“ = Hotovo, historie dostane další generaci; zrušené doplnění zapíše hotové soubory do reportu, historii nezmění
```

```powershell
git add windows docs
git commit -m "Windows: append mode in the engine" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Doplnění v GUI a CLI

Spec §2.2 „UX“, „CLI“, §6.1 úkol 15. Volby stage 2 sdílí `copy` a `append` (`StageTwoOptions`); `CopyCommand` se proto přepíše celý.

**Files:**
- Modify: `windows/src/Offload.App/JobItemViewModel.cs` (řádky 93–97, 160–163, 173–180)
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs` (řádky 46–50, 86–90, 123–126, 136–140)
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs` (řádky 12–15, 160–163, 285–288)
- Modify: `windows/src/Offload.App/QueuePanel.xaml` (řádky 91–94)
- Modify: `windows/src/Offload.App/SetupPanel.xaml` (řádky 95–99)
- Modify: `windows/src/Offload.App/SourceItemViewModel.cs` (řádky 23–27)
- Create: `windows/src/Offload.Cli/AppendCommand.cs`
- Modify: `windows/src/Offload.Cli/CopyCommand.cs` (řádky 22–41, 53–83, 86–108, 111–114, 116–121, 165–170)
- Modify: `windows/src/Offload.Cli/Program.cs` (řádky 7–11, 20–23)
- Create: `windows/src/Offload.Cli/StageTwoOptions.cs`
- Create: `windows/src/Offload.Core/Desktop/AppendRules.cs`
- Modify: `windows/src/Offload.Core/Desktop/QueueRules.cs` (řádky 56–66)
- Create: `windows/tests/Offload.Core.Tests/AppendRulesTests.cs`

**Interfaces:**
- Consumes: `JobConfig.AppendInto`, `PlanMode`, `JobStatus.Note/Warning` (úkol 14), `Insight(path)`, `HistoryMatch` (úkoly 6–7), `Msg.CardNotInHistory` (úkol 13).
- Produces: `AppendRules.Offer/CheckboxText/FolderText/Title/NeedsDestinations`; `QueueRules.ReportPath(cardPath, cardName, int generation = 1)`, `FirstReport(…, int generation = 1)`; `MainViewModel.AppendOffer/AppendChosen/AppendFolder/SetAppend`; `SourceItemViewModel.AppendText/AppendNewFiles/AppendFolderText`; `JobItemViewModel.NoteText/WarningText`; CLI `StageTwoOptions` (`AddTo`, `Apply`), `CopyCommand.RunJob(JobConfig, bool dryRun)` a `Inspect(string source, bool withHistory)` (interní), `AppendCommand`.

- [ ] **Step 1: Padající testy**

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class AppendRulesTests
{
    static CardInsight With(HistoryMatchKind kind, int newFiles = 12, long newBytes = 3_200_000_000)
    {
        var e = HistoryFixtures.Entry(HistoryFixtures.T0);
        e.Destinations = [new HistoryDestination { Root = @"D:\Zaloha", CardPath = @"D:\Zaloha\2026-10-06\A001_2" },
                          new HistoryDestination { Root = @"F:\Zaloha", CardPath = @"F:\Zaloha\2026-10-06\A001_2" }];
        var added = Enumerable.Range(0, newFiles).Select(i => new SourceFile($"n{i}", 1, HistoryFixtures.T0)).ToList();
        return new CardInsight { Source = @"E:\", History = new HistoryMatch(e, kind, added, newBytes, [], 0) };
    }

    [Fact]
    public void Offered_OnlyForNewFiles()
    {
        Assert.True(AppendRules.Offer(With(HistoryMatchKind.NewFiles)));
        Assert.False(AppendRules.Offer(With(HistoryMatchKind.Identical)));
        Assert.False(AppendRules.Offer(With(HistoryMatchKind.Changed)));
        Assert.False(AppendRules.Offer(With(HistoryMatchKind.NotCompared)));
        Assert.False(AppendRules.Offer(null));
    }

    [Fact]
    public void Texts_OfSpec22()
    {
        var m = With(HistoryMatchKind.NewFiles).History!;
        Assert.Equal("Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)", AppendRules.CheckboxText(m));
        Assert.Equal(@"Složka: 2026-10-06\A001_2 na 2 cílech", AppendRules.FolderText(m.Entry));
        Assert.Equal("A001 · doplnění", AppendRules.Title("A001"));
        Assert.False(AppendRules.NeedsDestinations(0));
        Assert.True(AppendRules.NeedsDestinations(1));
    }

    [Fact]
    public void ReportPath_OfAnAppend()
    {
        Assert.Equal(@"D:\Z\A001\_REPORT\A001_report.html", QueueRules.ReportPath(@"D:\Z\A001", "A001"));
        Assert.Equal(@"D:\Z\A001\_REPORT\A001_report_0002.html", QueueRules.ReportPath(@"D:\Z\A001", "A001", 2));
    }
}
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~AppendRulesTests"` → build FAIL.

- [ ] **Step 3: Implementace**

```csharp
using Offload.Core.History;

namespace Offload.Core.Desktop;

/// "Doplnit jen nové klipy" in the Karty list (spec 2.2 UX), without a window.
public static class AppendRules
{
    /// Offered only for a card whose history match found new files (and nothing changed).
    public static bool Offer(CardInsight? insight) => insight?.History is { Kind: HistoryMatchKind.NewFiles };

    /// "Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)"
    public static string CheckboxText(HistoryMatch m) =>
        $"Doplnit jen nové klipy do dřívější zálohy ({m.New.Count} {Texts.Plural(m.New.Count, "soubor", "soubory", "souborů")} · {Format.BytesCs(m.NewBytes)})";

    /// "Složka: 2026-10-06\A001 na 2 cílech" — taken from the backup's own card folders (the name may be "A001_2").
    public static string FolderText(HistoryEntry e)
    {
        var first = e.Destinations.FirstOrDefault()?.CardPath ?? "";
        var folder = Path.GetFileName(first.TrimEnd('\\'));
        var date = Path.GetFileName(Path.GetDirectoryName(first.TrimEnd('\\')) ?? "");
        int n = e.Destinations.Count;
        return $@"Složka: {date}\{folder} na {n} {Texts.Plural(n, "cíli", "cílech", "cílech")}";
    }

    /// The job card title: "A001 · doplnění".
    public static string Title(string cardName) => $"{cardName} · doplnění";

    /// The Cíle section matters only for full copies: a selection of appends alone goes to the destinations in history.
    public static bool NeedsDestinations(int fullCopies) => fullCopies > 0;
}
```

`windows/src/Offload.Core/Desktop/QueueRules.cs`, řádky 56–66 — najít:

```csharp
        phases.Any(p => p is JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying);

    public static string ReportPath(string cardPath, string cardName) =>
        Path.Combine(cardPath, Layout.ReportDirName, $"{cardName}_report.html");

    /// The HTML report from the first destination that has it (a destination may be disconnected), or null.
    /// Touches the destinations — never call it on the UI thread.
    public static string? FirstReport(IEnumerable<string> cardPaths, string cardName) =>
        cardPaths.Select(c => ReportPath(c, cardName)).FirstOrDefault(File.Exists);

    static bool SameDrive(string a, string b) =>
```

nahradit:

```csharp
        phases.Any(p => p is JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying);

    /// The HTML report of generation `generation` (an append has its own, spec 2.2).
    public static string ReportPath(string cardPath, string cardName, int generation = 1) =>
        Path.Combine(cardPath, Layout.ReportDirName, OffloadReport.HtmlName(cardName, generation));

    /// The HTML report from the first destination that has it (a destination may be disconnected), or null.
    /// Touches the destinations — never call it on the UI thread.
    public static string? FirstReport(IEnumerable<string> cardPaths, string cardName, int generation = 1) =>
        cardPaths.Select(c => ReportPath(c, cardName, generation)).FirstOrDefault(File.Exists);

    static bool SameDrive(string a, string b) =>
```

GUI — zaškrtávátko u karty (jen u karty s novými soubory), místo názvu text složky, Start bez sekce Cíle, když jsou vybraná jen doplnění, karta úlohy „A001 · doplnění“ s poznámkou a varováním:

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 12–15 — najít:

```csharp
    readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> cardNames = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> manualSources = [];
    // Volume labels of manual sources that are a drive root the scan does not list (an internal disk, a network
```

nahradit:

```csharp
    readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> cardNames = new(StringComparer.OrdinalIgnoreCase);
    // Cards whose "Doplnit jen nové klipy" is ticked (spec 2.2); it applies only while the card still has new files.
    readonly HashSet<string> appendChosen = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> manualSources = [];
    // Volume labels of manual sources that are a drive root the scan does not list (an internal disk, a network
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 160–163 — najít:

```csharp
        {
            cardNames.Remove(p);
            inspections.Drop(p);   // its history line belonged to the card that left
        }
```

nahradit:

```csharp
        {
            cardNames.Remove(p);
            appendChosen.Remove(p);
            inspections.Drop(p);   // its history line belonged to the card that left
        }
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 285–288 — najít:

```csharp

    internal void SetCardName(string path, string name) => cardNames[path] = name;

    // ── Cíle ───────────────────────────────────────────────────────────────
```

nahradit:

```csharp

    internal void SetCardName(string path, string name) => cardNames[path] = name;

    /// The append checkbox's text, or null when the card has no new files to append (spec 2.2).
    internal string? AppendOffer(string path) =>
        AppendRules.Offer(Insight(path)) ? AppendRules.CheckboxText(Insight(path)!.History!) : null;

    internal bool AppendChosen(string path) => appendChosen.Contains(path) && AppendRules.Offer(Insight(path));

    internal string? AppendFolder(string path) => AppendChosen(path) ? AppendRules.FolderText(Insight(path)!.History!.Entry) : null;

    internal void SetAppend(string path, bool on)
    {
        if (on) appendChosen.Add(path);
        else appendChosen.Remove(path);
        SetupChanged?.Invoke();
    }

    // ── Cíle ───────────────────────────────────────────────────────────────
```
`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 46–50 — najít:

```csharp
    /// In memory only (evaluated 3×/s): the LUT, the proxy folder and the destinations come from checks made off the UI thread.
    public bool CanStart => !starting && ReadyToStart;
    bool ReadyToStart => !closing && selected.Count > 0 && Settings.DestRoots.Count > 0 && Destinations.All(d => d.Exists)
        && LutProblem == null && ProxyFolderProblem == null && StartProblem == null;
    /// The hint under the start bar: why Start is off when the setup itself does not say it (a speed measurement runs).
```

nahradit:

```csharp
    /// In memory only (evaluated 3×/s): the LUT, the proxy folder and the destinations come from checks made off the UI thread.
    public bool CanStart => !starting && ReadyToStart;
    /// The Cíle section is needed only by full copies: appends go to the destinations of their earlier backup (spec 2.2).
    bool ReadyToStart => !closing && selected.Count > 0
        && (!AppendRules.NeedsDestinations(selected.Count(p => !AppendChosen(p))) || (Settings.DestRoots.Count > 0 && Destinations.All(d => d.Exists)))
        && LutProblem == null && ProxyFolderProblem == null && StartProblem == null;
    /// The hint under the start bar: why Start is off when the setup itself does not say it (a speed measurement runs).
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 86–90 — najít:

```csharp
            // Spec 2.1: cards history already knows are copied again in full only when the user says so.
            var skip = new List<string>();
            var backedUp = SelectedSources().Where(p => BackedUpPrompt.ShouldAsk(Insight(p))).ToList();
            if (backedUp.Count > 0 && AskBackedUp is { } ask)
            {
```

nahradit:

```csharp
            // Spec 2.1: cards history already knows are copied again in full only when the user says so.
            var skip = new List<string>();
            var backedUp = SelectedSources().Where(p => BackedUpPrompt.ShouldAsk(Insight(p)) && !AppendChosen(p)).ToList();
            if (backedUp.Count > 0 && AskBackedUp is { } ask)
            {
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 123–126 — najít:

```csharp
                continue;
            }
            // Exactly what the row shows: the typed name, else a suggestion (spec 2.3); null = the volume label.
            var name = cardNames.GetValueOrDefault(src) is { Length: > 0 } n && n.Trim().Length > 0 ? n.Trim()
```

nahradit:

```csharp
                continue;
            }
            bool isVolume = volumes.Any(v => string.Equals(v.Path, src, StringComparison.OrdinalIgnoreCase));
            if (AppendChosen(src) && Insight(src)?.History is { } known)
            {
                // Spec 2.2: only the new files, into the card folders of the earlier backup — not into the Cíle section.
                var appendConfig = new JobConfig(src, known.Entry.Destinations.Select(d => d.Root).ToList())
                {
                    AppendInto = known.Entry.Destinations.Select(d => d.CardPath).ToList(),
                    Verify = Settings.Verify,
                    AnalyzeClips = Settings.AnalyzeClips,
                    Proxy = SettingsStore.Clone(Settings.Proxy),
                    Tools = SettingsStore.Clone(Settings.Tools),
                };
                var title = AppendRules.Title(Path.GetFileName(known.Entry.Destinations[0].CardPath.TrimEnd('\\')));
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), title, canEject: isVolume && CardEject.CanEject(src));
                Jobs.Insert(0, appendItem);
                selected.Remove(src);
                appendChosen.Remove(src);
                queue.Enqueue(appendItem.Runner);
                continue;
            }
            // Exactly what the row shows: the typed name, else a suggestion (spec 2.3); null = the volume label.
            var name = cardNames.GetValueOrDefault(src) is { Length: > 0 } n && n.Trim().Length > 0 ? n.Trim()
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 136–140 — najít:

```csharp
                Tools = SettingsStore.Clone(Settings.Tools),
            };
            bool isVolume = volumes.Any(v => string.Equals(v.Path, src, StringComparison.OrdinalIgnoreCase));
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
                canEject: isVolume && CardEject.CanEject(src));
```

nahradit:

```csharp
                Tools = SettingsStore.Clone(Settings.Tools),
            };
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
                canEject: isVolume && CardEject.CanEject(src));
```
`windows/src/Offload.App/SourceItemViewModel.cs`, řádky 23–27 — najít:

```csharp

    /// A card inspection finished: the history line (and what depends on it) may have changed.
    internal void InsightChanged() => RaiseMany(nameof(HistoryText), nameof(HistoryTone), nameof(CardNamePlaceholder), nameof(NameHint));

    /// The date folder or the naming option changed: the suggested name may be another.
```

nahradit:

```csharp

    /// A card inspection finished: the history line (and what depends on it) may have changed.
    internal void InsightChanged() => RaiseMany(nameof(HistoryText), nameof(HistoryTone), nameof(CardNamePlaceholder), nameof(NameHint),
        nameof(AppendText), nameof(AppendNewFiles), nameof(AppendFolderText));

    /// "Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)", or null when there is nothing to append.
    public string? AppendText => owner.AppendOffer(path);

    public bool AppendNewFiles
    {
        get => owner.AppendChosen(path);
        set
        {
            owner.SetAppend(path, value);
            RaiseMany(nameof(AppendNewFiles), nameof(AppendFolderText));
        }
    }

    /// "Složka: 2026-10-06\A001 na 2 cílech" in place of the name field while appending.
    public string? AppendFolderText => owner.AppendFolder(path);

    /// The date folder or the naming option changed: the suggested name may be another.
```
`windows/src/Offload.App/JobItemViewModel.cs`, řádky 93–97 — najít:

```csharp
    public string DetailText => progress.DetailText;

    public string Title => Job.Plan?.CardName ?? sourceName;
    public string FilesText
    {
```

nahradit:

```csharp
    public string DetailText => progress.DetailText;

    public string Title => Job.Plan is { Mode: PlanMode.Append } p ? AppendRules.Title(p.CardName) : Job.Plan?.CardName ?? sourceName;
    public string FilesText
    {
```

`windows/src/Offload.App/JobItemViewModel.cs`, řádky 160–163 — najít:

```csharp
    public string? CancelledText => IsCancelled && !SafeToEject ? "Zrušeno — karta není kompletně zálohovaná." : null;
    public string? ErrorText => progress.Status.Error;
    public string? SummaryText => IsFinished && !IsCancelled && Job.Report is { } r ? JobProgress.SummaryText(r) : null;
    public bool HasReport => Job.Report != null;
```

nahradit:

```csharp
    public string? CancelledText => IsCancelled && !SafeToEject ? "Zrušeno — karta není kompletně zálohovaná." : null;
    public string? ErrorText => progress.Status.Error;
    /// "Nic nového — záloha je úplná." (spec 2.2) and similar information.
    public string? NoteText => progress.Status.Note;
    /// Orange, never an error: e.g. "5 souborů z dřívější zálohy nebylo ověřeno — doporučeno Ověřit zálohu."
    public string? WarningText => progress.Status.Warning;
    public string? SummaryText => IsFinished && !IsCancelled && Job.Report is { } r ? JobProgress.SummaryText(r) : null;
    public bool HasReport => Job.Report != null;
```

`windows/src/Offload.App/JobItemViewModel.cs`, řádky 173–180 — najít:

```csharp
            var cards = plan.CardPaths;
            var name = plan.CardName;
            var path = await Task.Run(() => QueueRules.FirstReport(cards, name));
            if (path == null)
            {
                owner.ShowMessage("Report nenalezen na žádném cíli:\n" + string.Join("\n", cards.Select(c => QueueRules.ReportPath(c, name))));
                return;
            }
```

nahradit:

```csharp
            var cards = plan.CardPaths;
            var name = plan.CardName;
            var generation = plan.Generation;
            var path = await Task.Run(() => QueueRules.FirstReport(cards, name, generation));
            if (path == null)
            {
                owner.ShowMessage("Report nenalezen na žádném cíli:\n" + string.Join("\n", cards.Select(c => QueueRules.ReportPath(c, name, generation))));
                return;
            }
```
`windows/src/Offload.App/SetupPanel.xaml`, řádky 95–99 — najít:

```xml
                                        </TextBlock>
                                        <StackPanel Margin="0,6,0,0" Visibility="{Binding IsSelected, Converter={StaticResource BoolToVisible}}">
                                            <DockPanel>
                                                <TextBlock Text="Název složky" Style="{StaticResource Caption}" VerticalAlignment="Center" Margin="0,0,8,0" />
                                                <Grid>
```

nahradit:

```xml
                                        </TextBlock>
                                        <StackPanel Margin="0,6,0,0" Visibility="{Binding IsSelected, Converter={StaticResource BoolToVisible}}">
                                            <CheckBox Content="{Binding AppendText}" IsChecked="{Binding AppendNewFiles}" Margin="0,0,0,4"
                                                      AutomationProperties.AutomationId="AppendNewFiles"
                                                      Visibility="{Binding AppendText, Converter={StaticResource NullToCollapsed}}" />
                                            <TextBlock Text="{Binding AppendFolderText}" Style="{StaticResource Caption}" FontFamily="Consolas"
                                                       Visibility="{Binding AppendFolderText, Converter={StaticResource NullToCollapsed}}" />
                                            <DockPanel Visibility="{Binding AppendNewFiles, Converter={StaticResource BoolToCollapsed}}">
                                                <TextBlock Text="Název složky" Style="{StaticResource Caption}" VerticalAlignment="Center" Margin="0,0,8,0" />
                                                <Grid>
```
`windows/src/Offload.App/QueuePanel.xaml`, řádky 91–94 — najít:

```xml
                                    <TextBlock Text="{Binding CancelledText}" Style="{StaticResource WarningText}" FontSize="13" Margin="0,8,0,0" Visibility="{Binding CancelledText, Converter={StaticResource NullToCollapsed}}" />
                                    <TextBlock Text="{Binding ErrorText}" Style="{StaticResource ErrorText}" FontSize="13" Margin="0,8,0,0" Visibility="{Binding ErrorText, Converter={StaticResource NullToCollapsed}}" />
                                    <TextBlock Text="{Binding SummaryText}" Style="{StaticResource Caption}" Margin="0,8,0,0" Visibility="{Binding SummaryText, Converter={StaticResource NullToCollapsed}}" />
                                    <DockPanel Margin="0,12,0,0">
```

nahradit:

```xml
                                    <TextBlock Text="{Binding CancelledText}" Style="{StaticResource WarningText}" FontSize="13" Margin="0,8,0,0" Visibility="{Binding CancelledText, Converter={StaticResource NullToCollapsed}}" />
                                    <TextBlock Text="{Binding ErrorText}" Style="{StaticResource ErrorText}" FontSize="13" Margin="0,8,0,0" Visibility="{Binding ErrorText, Converter={StaticResource NullToCollapsed}}" />
                                    <TextBlock Text="{Binding WarningText}" Style="{StaticResource WarningText}" FontSize="13" Margin="0,8,0,0" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
                                               AutomationProperties.AutomationId="JobWarningText" />
                                    <TextBlock Text="{Binding NoteText}" FontSize="13" Margin="0,8,0,0" TextWrapping="Wrap" Visibility="{Binding NoteText, Converter={StaticResource NullToCollapsed}}"
                                               AutomationProperties.AutomationId="JobNoteText" />
                                    <TextBlock Text="{Binding SummaryText}" Style="{StaticResource Caption}" Margin="0,8,0,0" Visibility="{Binding SummaryText, Converter={StaticResource NullToCollapsed}}" />
                                    <DockPanel Margin="0,12,0,0">
```

CLI — volby stage 2 do vlastní třídy (volba `--hash` z §2.2 přibude s MD5 v plánu 7), `copy` přepsat celý (`windows/src/Offload.Cli/CopyCommand.cs`; „Nic nového“ = kód 0, report podle generace, poznámka a varování úlohy), nový `append`:

```csharp
using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.Cli;

/// The options of stage 2 (clip analysis, proxies, LUT) — the same for `copy` and `append`.
sealed class StageTwoOptions
{
    readonly Option<bool> noAnalyze = new("--no-analyze") { Description = "Skip clip analysis (metadata + thumbnails in the report)." };
    readonly Option<bool> proxy = new("--proxy") { Description = "Render proxies after the copy is verified." };
    readonly Option<string?> lut = new("--lut") { Description = "LUT for proxies and thumbnails: auto (by camera, default), none, a built-in id (offload luts prints them) or a .cube path." };
    readonly Option<string?> lutFallback = new("--lut-fallback") { Description = "Built-in LUT id (or none) for clips whose camera is not recognised with --lut auto." };
    readonly Option<int> proxyHeight = new("--proxy-height") { Description = "Proxy height in pixels.", DefaultValueFactory = _ => 1080 };
    readonly Option<double?> frameAspect = new("--frame-aspect") { Description = "Crop proxies to the frame lines: aspect ratio, e.g. 1.66.", CustomParser = a => CliParse.Parse(a) };
    readonly Option<double> frameScale = new("--frame-scale") { Description = "Frame lines size in % of the image (90 = 10 % overscan).", DefaultValueFactory = _ => 100, CustomParser = a => CliParse.Parse(a) ?? 100 };
    readonly Option<string> proxyCodec = new("--proxy-codec") { Description = "Proxy codec: h264, hevc, proresProxy, proresLT, prores422, prores422HQ.", DefaultValueFactory = _ => "h264" };
    readonly Option<int> proxyBitrate = new("--proxy-bitrate") { Description = "Proxy bitrate (Mb/s) for H.264/HEVC.", DefaultValueFactory = _ => 10 };
    readonly Option<bool> noBurnIn = new("--no-burn-in") { Description = "No burn-in of clip name and timecode." };
    readonly Option<string?> proxyFolder = new("--proxy-folder") { Description = "Also copy every proxy to <folder>\\<date>\\<card name>\\ (with --proxy; never overwrites)." };

    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder })
            cmd.Options.Add(o);
    }

    /// Sets AnalyzeClips and Proxy; throws ArgumentException with a Czech message on a bad value.
    public void Apply(ParseResult r, JobConfig config)
    {
        if (!ProxyCodecExtensions.TryParseWire(r.GetValue(proxyCodec)!, out var codec))
            throw new ArgumentException(Msg.UnknownProxyCodec(r.GetValue(proxyCodec)));
        var (lutMode, lutBuiltin, lutRaw, lutFb, lutErr) = LutOption.Parse(r.GetValue(lut), r.GetValue(lutFallback), LutCatalog.Default);
        if (lutErr != null) throw new ArgumentException(lutErr);
        var lutPath = lutRaw != null ? CliPath.Full(lutRaw) : null;
        if (lutPath != null && !File.Exists(lutPath)) throw new ArgumentException(Msg.LutNotFound(lutPath));
        config.AnalyzeClips = !r.GetValue(noAnalyze);
        config.Proxy = new ProxySettings
        {
            Enabled = r.GetValue(proxy),
            LutMode = lutMode,
            LutBuiltin = lutBuiltin,
            LutPath = lutPath,
            LutFallback = lutFb,
            Height = r.GetValue(proxyHeight),
            Codec = codec,
            BitrateMbps = r.GetValue(proxyBitrate),
            FrameAspect = r.GetValue(frameAspect),
            FrameScale = r.GetValue(frameScale) / 100,
            BurnFilename = !r.GetValue(noBurnIn),
            BurnTimecode = !r.GetValue(noBurnIn),
            ExtraFolder = r.GetValue(proxyFolder) is string pf ? CliPath.Full(pf) : null,
        };
        if (config.Proxy.ExtraFolder != null && !config.Proxy.Enabled)
            Console.Error.WriteLine(Msg.ProxyFolderNoEffect);
        if (ProxyPreflight.ExtraFolderInUse(config.Proxy) is string folder && !Directory.Exists(folder))
            throw new ArgumentException(Msg.ProxyFolderNotFound(folder));
    }
}
```

```csharp
using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Hardware;
using Offload.Core.History;

namespace Offload.Cli;

static class CopyCommand
{
    public static Command Create()
    {
        var source = new Argument<string>("source") { Description = "Card drive or source folder." };
        // Not Required: the parser's "Option '--dest' is required." has no Czech translation — the engine says
        // "Není zadaný žádný cíl." instead.
        var dest = new Option<string[]>("--dest", "-d") { Description = "Destination root (repeat for each disk).", DefaultValueFactory = _ => [] };
        var name = new Option<string?>("--name") { Description = "Card folder name (default: volume label); auto = the reel in the clip names (A001C003… → A001), else the volume label." };
        var date = new Option<string?>("--date") { Description = "Date folder (default: today, YYYY-MM-DD)." };
        var chunkMb = new Option<int>("--chunk-mb") { Description = "Block size in MB.", DefaultValueFactory = _ => 16 };
        var buffers = new Option<int>("--buffers") { Description = "Blocks in flight.", DefaultValueFactory = _ => 16 };
        var noVerify = new Option<bool>("--no-verify") { Description = "Skip read-back verification (not recommended)." };
        var allowSame = new Option<bool>("--allow-same-device") { Description = "Allow two destinations on the same volume." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Only scan and check, do not copy." };
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (%APPDATA%\\Offload\\history.json)." };
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
        {
            JobConfig config;
            try
            {
                config = new JobConfig(CliPath.Full(r.GetValue(source)!), r.GetValue(dest)!.Select(CliPath.Full).ToList())
                {
                    CardName = r.GetValue(name) is string n && !n.Equals("auto", StringComparison.OrdinalIgnoreCase) ? n : null,
                    Date = r.GetValue(date),
                    ChunkMB = r.GetValue(chunkMb),
                    Buffers = r.GetValue(buffers),
                    Verify = !r.GetValue(noVerify),
                    AllowSameDevice = r.GetValue(allowSame),
                    RecordHistory = !r.GetValue(noHistory),
                };
                stageTwo.Apply(r, config);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
            bool autoName = r.GetValue(name) is string a && a.Equals("auto", StringComparison.OrdinalIgnoreCase);
            var insight = config.RecordHistory || autoName ? Inspect(config.Source, withHistory: config.RecordHistory) : null;
            if (insight?.History is { } known)
                Console.WriteLine(HistoryText.Line(known, fullPaths: true).Text);   // no question: a script has nobody to ask
            if (autoName)
            {
                // --name auto: the reel from the clip names, otherwise the volume label (as without --name).
                config.CardName = insight?.Name?.Name;
                if (insight?.NameNote is string note) Console.WriteLine(note);
            }
            return RunJob(config, r.GetValue(dryRun));
        });
        return cmd;
    }

    /// Plans, prints the plan and runs the job (copy or append). Exit 0 = copied (and verified unless --no-verify) or,
    /// for an append, nothing new; 1 = anything else.
    internal static int RunJob(JobConfig config, bool dryRun)
    {
        // Only when proxies or clip analysis (thumbnails) are on — otherwise no LUT is used at all.
        if (LutOption.PlanLine(config.Proxy, config.AnalyzeClips) is string lutLine) Console.WriteLine(lutLine);
        var job = new OffloadJob(config);
        OffloadPlan plan;
        try
        {
            plan = job.Prepare();
        }
        catch (OffloadError e) when (e.Code == PreflightCode.NothingNew)
        {
            Console.WriteLine(e.Message);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(Msg.CliError + e.Message);
            return 1;
        }
        var what = plan.Mode == PlanMode.Append ? $"Card {plan.CardName} (append, generation {plan.Generation})" : $"Card {plan.CardName}";
        Console.WriteLine($"{what}: {plan.Scan.Files.Count} files, {Format.Bytes(plan.Scan.TotalBytes)}");
        foreach (var p in plan.CardPaths) Console.WriteLine($"  → {p}");
        if (plan.UnverifiedEarlier > 0) Console.WriteLine("Varování: " + Msg.UnverifiedEarlier(plan.UnverifiedEarlier));
        if (ProxyPreflight.ExtraFolderInUse(config.Proxy) is string extra)
            Console.WriteLine($"  proxy also → {Layout.CardPath(extra, plan.Date, plan.CardName)}");
        if (config.AnalyzeClips || config.Proxy.Enabled)
            Console.WriteLine($"  ffmpeg: {config.Tools.Resolved().Ffmpeg ?? "NOT FOUND"}");
        // Connection chain: informational only, must never fail a copy.
        List<RoleLink>? chainLinks = null;
        try
        {
            var links = new List<RoleLink> { new("Karta", DriveLinks.Resolve(plan.Source)) };
            links.AddRange(plan.DestRoots.Select((d, i) => new RoleLink($"Cíl {i + 1}", DriveLinks.Resolve(d))));
            var connectors = UsbScanner.Scan().Connectors;
            // Status-line names first ("card", "D1"), so "brzdí: D1" there reads against this list.
            var lines = new List<string> { "  Připojení:" };
            lines.AddRange(links.Select((l, i) => $"    {ProgressDisplay.ShortName(i)} = {l.Role}: {l.Link.Describe(connectors)}"));
            lines.AddRange(Advisor.Advise(links, connectors)
                .Where(a => a.Kind == AdviceKind.Warning).Select(a => $"  ! {a.Text}"));
            foreach (var l in lines) Console.WriteLine(l);
            chainLinks = links;
        }
        catch (Exception)
        {
            // Hardware info unavailable: just omit it.
        }
        if (dryRun) return 0;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine("\n" + Msg.Cancelling);
            job.Cancel();
        };

        OffloadReport? report;
        using (new ProgressDisplay(job, chainLinks)) report = job.Run(message => Console.Error.WriteLine(message));

        var status = job.Status();
        if (report != null)
        {
            Console.WriteLine();
            Console.Write(report.Summary);
            if (report.Clips.Count > 0)
            {
                var done = report.Clips.Count(c => c.ProxyStatus == ProxyStatus.Done);
                Console.WriteLine($"Clips: {report.Clips.Count}" + (config.Proxy.Enabled ? $"  proxies OK {done}, failed {status.ProxiesFailed}" : ""));
                foreach (var c in report.Clips.Where(c => c.ProxyStatus is ProxyStatus.Failed or ProxyStatus.Unsupported))
                    Console.WriteLine($"  {c.Clip.Name}: {c.ProxyStatus.ToString().ToLowerInvariant()} — {c.ProxyNote}");
            }
            Console.WriteLine($"Report: {QueueRules.ReportPath(plan.CardPaths[0], plan.CardName, plan.Generation)}");
        }
        if (status.Note != null) Console.WriteLine(status.Note);
        if (status.Warning != null) Console.WriteLine("Varování: " + status.Warning);
        if (status.Error != null) Console.WriteLine($"ERROR: {status.Error}");
        return status.Phase == JobPhase.Done && report?.Success == true ? 0 : 1;
    }

    /// The card against history (spec 2.1 CLI) and its clip names (2.3). Never fails the copy: any problem only means
    /// no history line and no suggested name.
    internal static CardInsight? Inspect(string source, bool withHistory)
    {
        try
        {
            var store = withHistory ? HistoryStore.Default(m => Console.Error.WriteLine(m)) : null;
            return CardInspector.Inspect(source, store?.Load(), store, CancellationToken.None);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"history: {e.Message}");
            return null;
        }
    }
}
```

```csharp
using System.CommandLine;
using Offload.Core;

namespace Offload.Cli;

/// `offload append <source> [--into <card folder>]…` (spec 2.2): only the card's new files into the card folders of
/// the earlier backup, as a new ASC MHL generation. Without --into the folders come from the offload history.
static class AppendCommand
{
    public static Command Create()
    {
        var source = new Argument<string>("source") { Description = "Card drive or source folder." };
        var into = new Option<string[]>("--into") { Description = "Card folder of the earlier backup (<root>\\<date>\\<card>); repeat for each destination. Default: from the offload history.", DefaultValueFactory = _ => [] };
        var chunkMb = new Option<int>("--chunk-mb") { Description = "Block size in MB.", DefaultValueFactory = _ => 16 };
        var buffers = new Option<int>("--buffers") { Description = "Blocks in flight.", DefaultValueFactory = _ => 16 };
        var noVerify = new Option<bool>("--no-verify") { Description = "Skip read-back verification (not recommended)." };
        var allowSame = new Option<bool>("--allow-same-device") { Description = "Allow two destinations on the same volume." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Only scan and check, do not copy." };
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (needs --into)." };
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("append", "Copy only the new files of a card into its earlier backup (same card folders, next ASC MHL generation).");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { into, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
        {
            JobConfig config;
            try
            {
                config = new JobConfig(CliPath.Full(r.GetValue(source)!), [])
                {
                    ChunkMB = r.GetValue(chunkMb),
                    Buffers = r.GetValue(buffers),
                    Verify = !r.GetValue(noVerify),
                    AllowSameDevice = r.GetValue(allowSame),
                    RecordHistory = !r.GetValue(noHistory),
                };
                stageTwo.Apply(r, config);
                var folders = r.GetValue(into)!.Select(CliPath.Full).ToList();
                if (folders.Count == 0)
                {
                    var known = config.RecordHistory ? CopyCommand.Inspect(config.Source, withHistory: true)?.History : null;
                    if (known == null) throw new ArgumentException(Msg.CardNotInHistory);
                    folders = known.Entry.Destinations.Select(d => d.CardPath).ToList();
                }
                config.AppendInto = folders;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
            return CopyCommand.RunJob(config, r.GetValue(dryRun));
        });
        return cmd;
    }
}
```

`windows/src/Offload.Cli/Program.cs`, řádky 7–11 — najít:

```csharp
static class Program
{
    static readonly string[] Commands = ["copy", "verify", "bench", "ports", "luts"];

    static int Main(string[] args)
```

nahradit:

```csharp
static class Program
{
    static readonly string[] Commands = ["copy", "append", "verify", "bench", "ports", "luts"];

    static int Main(string[] args)
```

`windows/src/Offload.Cli/Program.cs`, řádky 20–23 — najít:

```csharp
        var root = new RootCommand("Camera card offload: read once, write to several disks, verify with xxh64.");
        root.Subcommands.Add(CopyCommand.Create());
        root.Subcommands.Add(VerifyCommand.Create());
        root.Subcommands.Add(BenchCommand.Create());
```

nahradit:

```csharp
        var root = new RootCommand("Camera card offload: read once, write to several disks, verify with xxh64.");
        root.Subcommands.Add(CopyCommand.Create());
        root.Subcommands.Add(AppendCommand.Create());
        root.Subcommands.Add(VerifyCommand.Create());
        root.Subcommands.Add(BenchCommand.Create());
```

- [ ] **Step 4: Testy + build**

Run: `dotnet test --filter "FullyQualifiedName~AppendRulesTests|FullyQualifiedName~QueueRulesTests"` → PASS; `dotnet build` bez varování; `dotnet test` → zelená. `offload.exe append --help` vypíše volby `--into`, `--proxy`, `--no-history` …; `offload.exe append C:\Windows\Temp` (žádná historie) → `Chyba: Karta není v historii — zadejte složky zálohy volbou --into.`, kód 1. Ruční ověření GUI: úkol 19, bod 14.

- [ ] **Step 5: VYVOJ + commit**

Řádek:

```markdown
- Úkol 15 — doplnění v GUI (u karty s novými soubory zaškrtávátko „Doplnit jen nové klipy do dřívější zálohy (N souborů · X GB)“, místo názvu „Složka: datum\karta na N cílech“, cíle z historie — Start jde i bez sekce Cíle, karta úlohy „A001 · doplnění“, poznámka „Nic nového…“ a oranžové varování) a CLI `offload append <karta> [--into <složka karty>]…` (bez `--into` z historie; volby stage 2 sdílené s `copy`)
```

```powershell
git add windows docs
git commit -m "Windows: append new clips from the GUI and the CLI" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 16: `SpaceEstimator` + `SpacePlanner`, engine a přepočet před proxy

Spec §2.5 (vzorce, R10, R11, „Jádro, úloha“, „CLI“), §6.1 úkol 16. Kontrola v enginu zůstává jedna pro GUI, CLI i úlohu, která se spustí až za hodinu; počítá po **svazcích**.

**Files:**
- Modify: `windows/src/Offload.Cli/CopyCommand.cs` (řádky 92–95)
- Modify: `windows/src/Offload.Core/Messages.cs` (řádky 69–74)
- Modify: `windows/src/Offload.Core/Models.cs` (řádky 18–21, 56–59)
- Modify: `windows/src/Offload.Core/OffloadEngine.cs` (řádky 1–3, 25–30, 35–48, 56–64, 128–143)
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (řádky 2–5, 113–118, 376–379, 478–481)
- Create: `windows/src/Offload.Core/Space/SpaceEstimator.cs`
- Modify: `windows/tests/Offload.Core.Tests/PreflightMessagesTests.cs` (řádky 76–81)
- Create: `windows/tests/Offload.Core.Tests/SpaceTests.cs`

**Interfaces:**
- Consumes: `ClipDetector.Detect`, `ExternalTools.Resolved`, `HistoryText.DiskName` (úkol 6), `Format.BytesCs`.
- Produces (`namespace Offload.Core.Space`): `record SpaceNeed(long Card, long Proxy, long Report)` (`Total`, `+`, `None`); `record SpaceExtra(long Proxy, long Report, bool ProxyOnAllDestinations, string? ProxyFolder)` s `ForDestination(int index, long card)`; `SpaceEstimator.CardBytes/ReportBytes/SourceMbps/ProxyMbps/Renderable/ProxyBytes/Extra`; `enum SpaceSeverity { Warning, Blocking }`, `record VolumeDemand(string Volume, string Label, SpaceNeed Need)`, `record SpaceProblem(SpaceSeverity, string Label, SpaceNeed Need, long Free, string Text)`, `SpacePlanner.Check(IEnumerable<VolumeDemand>, IReadOnlyDictionary<string,long> free)`, `SpacePlanner.VolumeKey(string path)`; `OffloadEngine.Prepare(…, Func<SourceScan, SpaceExtra>? extra = null)`, `PrepareAppend(…, Func<SourceScan, SpaceExtra>? extra = null)`, interní `OffloadEngine.FreeBytes(path)`; `OffloadOptions.FreeSpace` (interní, testy); `Msg.NoSpace(string where, SpaceNeed need, long free)` (nový podpis), `Msg.LowSpace`, `Msg.ProxiesSkippedForSpace`, `Msg.SpaceLine`; `record SpaceLine(string Label, SpaceNeed Need, long Free)`, `SpacePlanner.Lines(…)`; `OffloadPlan.Space` (`IReadOnlyList<SpaceLine>`) a `OffloadPlan.SpaceWarnings` (`IReadOnlyList<string>`).

- [ ] **Step 1: Padající testy**

```csharp
using Offload.Core.Space;

namespace Offload.Core.Tests;

public class SpaceEstimatorTests
{
    static Clip C(string path, long bytes, ClipFormat format = ClipFormat.Video, bool cameraProxy = false) =>
        new() { Name = Path.GetFileNameWithoutExtension(path), Path = path, Format = format, Segments = [path], Bytes = bytes, IsCameraProxy = cameraProxy };

    static readonly ExternalTools All = new() { Ffmpeg = "f", Ffprobe = "p", BrawDecoder = "b", Redline = "r", ArtCmd = "a" };

    [Fact]
    public void CardAndReport_Formulas()
    {
        Assert.Equal(1000 + 2 * 65536 + 67_108_864, SpaceEstimator.CardBytes(1000, 2));
        Assert.Equal((32L << 20) + 3 * (1L << 20), SpaceEstimator.ReportBytes(3));
    }

    [Theory]
    [InlineData("A.MOV", ClipFormat.Video, 50)]
    [InlineData("A.mp4", ClipFormat.Video, 50)]
    [InlineData("A.MXF", ClipFormat.Video, 100)]
    [InlineData("A.braw", ClipFormat.Braw, 150)]
    [InlineData("A.R3D", ClipFormat.R3d, 200)]
    [InlineData("A.CRM", ClipFormat.CanonRaw, 500)]
    [InlineData("A", ClipFormat.Arriraw, 1000)]
    public void SourceRate_Table(string path, ClipFormat format, double mbps) => Assert.Equal(mbps, SpaceEstimator.SourceMbps(C(path, 1, format)));

    [Fact]
    public void Proxy_H264_FromBytesBeforeTheCopy()
    {
        var p = new ProxySettings { Enabled = true, Codec = ProxyCodec.H264, BitrateMbps = 10 };
        // 625 MB of MOV at 50 Mb/s = 100 s; proxy 10 × 1.1 + 0.2 (one audio track) = 11.2 Mb/s → 140 MB
        Assert.Equal(140_000_000, SpaceEstimator.ProxyBytes([C("A.MOV", 625_000_000)], p, _ => true));
        p.Audio = false;
        Assert.Equal(137_500_000, SpaceEstimator.ProxyBytes([C("A.MOV", 625_000_000)], p, _ => true));
    }

    [Fact]
    public void Proxy_ProRes_ScaledByHeight_AfterAnalysis()
    {
        var p = new ProxySettings { Enabled = true, Codec = ProxyCodec.ProresProxy, Height = 540 };
        var clip = C("A.MOV", 1);
        var info = new ClipInfo { Duration = 80, Height = 2160, AudioChannels = 2 };
        // 45 Mb/s × (540/1080)² = 11.25 Mb/s × 80 s = 112.5 MB
        Assert.Equal(112_500_000, SpaceEstimator.ProxyBytes([clip], p, _ => true, _ => info));
        Assert.Equal(220, SpaceEstimator.ProxyMbps(new ProxySettings { Codec = ProxyCodec.Prores422HQ }, 0, 1080));
    }

    [Fact]
    public void Proxy_OffOrSkippedClips_Zero()
    {
        var off = new ProxySettings { Enabled = false };
        Assert.Equal(0, SpaceEstimator.ProxyBytes([C("A.MOV", 1_000_000)], off, _ => true));
        var on = new ProxySettings { Enabled = true };
        Assert.Equal(0, SpaceEstimator.ProxyBytes([C("SUB/A.MP4", 1_000_000, cameraProxy: true)], on, _ => true));
        Assert.Equal(0, SpaceEstimator.ProxyBytes([C("A.CRM", 1_000_000, ClipFormat.CanonRaw)], on, c => SpaceEstimator.Renderable(c, All)));
        Assert.False(SpaceEstimator.Renderable(C("A.MOV", 1), new ExternalTools()));
        Assert.True(SpaceEstimator.Renderable(C("A.braw", 1, ClipFormat.Braw), All));
    }

    [Fact]
    public void Extra_PerDestination()
    {
        var extra = new SpaceExtra(Proxy: 30, Report: 2, ProxyOnAllDestinations: false, ProxyFolder: null);
        Assert.Equal(new SpaceNeed(100, 30, 2), extra.ForDestination(0, 100));
        Assert.Equal(new SpaceNeed(100, 0, 2), extra.ForDestination(1, 100));
        Assert.Equal(new SpaceNeed(100, 30, 2), (extra with { ProxyOnAllDestinations = true }).ForDestination(1, 100));
    }
}

public class SpacePlannerTests
{
    const long GB = 1_000_000_000;

    static VolumeDemand D(string volume, string label, long card, long proxy = 0, long report = 0) => new(volume, label, new SpaceNeed(card, proxy, report));

    [Fact]
    public void TwoCardsOneDestination_AddUp()
    {
        var problems = SpacePlanner.Check([D("v1", "Zaloha (D:)", 200 * GB), D("v1", "Zaloha (D:)", 180 * GB, 30 * GB, 2 * GB)],
            new Dictionary<string, long> { ["v1"] = 398 * GB });
        var p = Assert.Single(problems);
        Assert.Equal(SpaceSeverity.Blocking, p.Severity);
        Assert.Equal("Na cíli Zaloha (D:) není dost místa: potřeba 412 GB (karty 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB.", p.Text);
    }

    // Review focus 5: two destinations on one volume (--allow-same-device) + the proxy folder on it too.
    [Fact]
    public void SameVolume_DestinationsAndProxyFolder_AddUp()
    {
        var problems = SpacePlanner.Check(
            [D("v1", "Zaloha (D:)", 100 * GB, 10 * GB), D("v1", "Zaloha2 (D:)", 100 * GB), D("v1", @"D:\Proxy", 0, 10 * GB)],
            new Dictionary<string, long> { ["v1"] = 215 * GB });
        var p = Assert.Single(problems);
        Assert.Equal(SpaceSeverity.Blocking, p.Severity);
        Assert.Equal(@"Zaloha (D:), Zaloha2 (D:), D:\Proxy", p.Label);
        Assert.Equal(220 * GB, p.Need.Total);
    }

    [Fact]
    public void FivePercentBoundary_Warning()
    {
        var need = new[] { D("v1", "Zaloha (D:)", 100 * GB) };
        Assert.Empty(SpacePlanner.Check(need, new Dictionary<string, long> { ["v1"] = 105 * GB }));   // exactly 5 % left
        var w = Assert.Single(SpacePlanner.Check(need, new Dictionary<string, long> { ["v1"] = 104 * GB }));
        Assert.Equal(SpaceSeverity.Warning, w.Severity);
        Assert.Equal("Na cíli Zaloha (D:) zbude po offloadu jen 4 GB — rezerva pod 5 %, odhad proxy je přibližný.", w.Text);
    }

    [Fact]
    public void UnknownFree_NeverBlocks_OtherVolumesSeparately()
    {
        var problems = SpacePlanner.Check([D("v1", "A", 10 * GB), D("v2", "B", 10 * GB)], new Dictionary<string, long> { ["v2"] = 5 * GB });
        Assert.Equal("B", Assert.Single(problems).Label);
    }
}

/// Spec 2.5 in the engine and the job: proxies count before the copy; before the render they are counted again.
public class SpaceEngineTests
{
    [Fact]
    public void NotEnoughForTheProxies_StopsBeforeCopying()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "C0001.MOV"), 1_000_000, 1);
        long cardNeed = SpaceEstimator.CardBytes(1_000_000, 1);
        long report = SpaceEstimator.ReportBytes(1);
        var options = new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, FreeSpace = _ => cardNeed + report };
        var config = new JobConfig(card, [dir.Sub("d1")]) { Proxy = new ProxySettings { Enabled = true }, Tools = new ExternalTools { Ffmpeg = @"C:\Windows\System32\cmd.exe", NoSearch = true } };
        var job = new OffloadJob(config, options);

        job.Run();

        var s = job.Status();
        Assert.Equal(JobPhase.Failed, s.Phase);
        Assert.Contains("není dost místa", s.Error);
        Assert.Contains("proxy ~", s.Error);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "d1", Layout.Today())));   // nothing written
    }

    // Spec 2.5 CLI: the --dry-run breakdown and the 5 % warning come with the plan.
    [Fact]
    public void Plan_HasThePerVolumeLines_AndTheLowSpaceWarning()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "C0001.MOV"), 1_000_000, 1);
        long need = SpaceEstimator.CardBytes(1_000_000, 1);
        var options = new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, FreeSpace = _ => need + need / 50 };
        var d1 = dir.Sub("d1");

        var plan = new OffloadEngine(options).Prepare(card, [d1], null, "2026-10-06");

        var line = Assert.Single(plan.Space);
        Assert.Equal((d1, need, need + need / 50), (line.Label, line.Need.Total, line.Free));
        Assert.StartsWith($"Na cíli {d1} zbude po offloadu jen ", Assert.Single(plan.SpaceWarnings));
        Assert.Equal($"Místo: {d1} potřeba 68 MB (karta 68 MB), volno 70 MB", Msg.SpaceLine(line.Label, line.Need, line.Free));
    }

    [Fact]
    public void CardAlone_Fits_WithoutProxies()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "C0001.MOV"), 1_000_000, 1);
        var options = new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true,
            FreeSpace = _ => SpaceEstimator.CardBytes(1_000_000, 1) + SpaceEstimator.ReportBytes(1) };
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { AnalyzeClips = false }, options);
        job.Prepare();
        Assert.NotNull(job.Plan);
    }

    // Spec 2.5: the disk filled up during the copy — the proxies are skipped, the copy stays verified.
    [FfmpegFact]
    public void ProxiesSkipped_WhenTheDiskFilledMeanwhile()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "C0001.MP4"), seconds: 1);
        long free = long.MaxValue / 4;
        var options = new OffloadOptions
        {
            ChunkSize = 1 << 20, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ProxyEncoder = "libx264",
            FreeSpace = _ => free, BeforeFileCopy = _ => free = 0,
        };
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Proxy = new ProxySettings { Enabled = true, Height = 360 } }, options);

        var report = job.Run();

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.StartsWith("Proxy se nevyrenderovaly: na d1 (", s.Warning);
        Assert.Contains(") chybí místo (potřeba ~", s.Warning);
        Assert.Equal(ProxyStatus.Skipped, Assert.Single(report!.Clips).ProxyStatus);
        Assert.False(Directory.Exists(Path.Combine(job.Plan!.CardPaths[0], "_PROXY")));
    }
}
```

Hláška místa má nově rozpis:

`windows/tests/Offload.Core.Tests/PreflightMessagesTests.cs`, řádky 76–81 — najít:

```csharp
    public void TextsWithoutAnEasyScenario()
    {
        Assert.Equal(@"Na cíli D:\Zaloha není dost místa: potřeba 412 GB, volno 398 GB.",
            Msg.NoSpace(@"D:\Zaloha", 412_000_000_000, 398_000_000_000));
        Assert.Equal(@"Na cíl nejde zapisovat: D:\Zaloha", Msg.DestinationNotWritable(@"D:\Zaloha"));
        Assert.Equal(@"Cíl nejde přečíst: D:\Zaloha", Msg.DestinationUnreadable(@"D:\Zaloha"));
```

nahradit:

```csharp
    public void TextsWithoutAnEasyScenario()
    {
        Assert.Equal(@"Na cíli D:\Zaloha není dost místa: potřeba 412 GB (karty 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB.",
            Msg.NoSpace(@"D:\Zaloha", new Space.SpaceNeed(380_000_000_000, 30_000_000_000, 2_000_000_000), 398_000_000_000));
        Assert.Equal(@"Na cíl nejde zapisovat: D:\Zaloha", Msg.DestinationNotWritable(@"D:\Zaloha"));
        Assert.Equal(@"Cíl nejde přečíst: D:\Zaloha", Msg.DestinationUnreadable(@"D:\Zaloha"));
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~SpaceEstimatorTests|FullyQualifiedName~SpacePlannerTests|FullyQualifiedName~SpaceEngineTests|FullyQualifiedName~PreflightMessagesTests"` → build FAIL.

- [ ] **Step 3: Implementace**

```csharp

namespace Offload.Core.Space;

/// Bytes one destination volume needs: the card (with the per-file overhead), proxies, report.
public sealed record SpaceNeed(long Card, long Proxy, long Report)
{
    public static readonly SpaceNeed None = new(0, 0, 0);
    public long Total => Card + Proxy + Report;
    public static SpaceNeed operator +(SpaceNeed a, SpaceNeed b) => new(a.Card + b.Card, a.Proxy + b.Proxy, a.Report + b.Report);
}

/// What a job needs besides the card itself, per destination (spec 2.5): proxies (on the first destination, on all with
/// "Kopírovat proxy na všechny cíle"), the report everywhere, the proxies once more in the proxy folder.
public sealed record SpaceExtra(long Proxy, long Report, bool ProxyOnAllDestinations, string? ProxyFolder)
{
    public SpaceNeed ForDestination(int index, long card) => new(card, index == 0 || ProxyOnAllDestinations ? Proxy : 0, Report);
}

/// The formulas of spec 2.5 — pure functions, no disk.
public static class SpaceEstimator
{
    /// The card on one destination: its bytes + 64 KiB per file + 64 MiB (the engine's formula since plan 1).
    public static long CardBytes(long totalBytes, int files) => totalBytes + files * 64L * 1024 + (64L << 20);

    /// HTML/PDF with thumbnails, MHL, lists: 32 MiB + 1 MiB per clip.
    public static long ReportBytes(int clips) => (32L << 20) + clips * (1L << 20);

    /// The source data rate assumed before the copy — on the low side, so the clip is assumed longer and the proxy
    /// bigger (careful): MP4/MOV/MTS 50 Mb/s, MXF 100, BRAW 150, R3D 200, Canon RAW 500, ARRIRAW 1 000.
    public static double SourceMbps(Clip clip) => clip.Format switch
    {
        ClipFormat.Braw => 150,
        ClipFormat.R3d => 200,
        ClipFormat.CanonRaw => 500,
        ClipFormat.Arriraw => 1000,
        _ => Path.GetExtension(clip.Path).Equals(".mxf", StringComparison.OrdinalIgnoreCase) ? 100 : 50,
    };

    /// The proxy's data rate: H.264/HEVC the set bitrate × 1.1 + 0.2 Mb/s per audio track; ProRes Apple's 1080p30 rates
    /// (Proxy 45, LT 102, 422 147, HQ 220 Mb/s) × (height/1080)².
    public static double ProxyMbps(ProxySettings p, int audioTracks, int height) => p.Codec switch
    {
        ProxyCodec.H264 or ProxyCodec.Hevc => p.BitrateMbps * 1.1 + 0.2 * audioTracks,
        _ => (p.Codec switch { ProxyCodec.ProresProxy => 45, ProxyCodec.ProresLT => 102, ProxyCodec.Prores422 => 147, _ => 220 })
             * Math.Pow(height / 1080.0, 2),
    };

    /// A clip that a proxy can be made of with these tools (a missing decoder makes none).
    public static bool Renderable(Clip clip, ExternalTools resolved) => resolved.Ffmpeg != null && clip.Format switch
    {
        ClipFormat.Video => true,
        ClipFormat.Braw => resolved.BrawDecoder != null,
        ClipFormat.R3d => resolved.Redline != null,
        ClipFormat.Arriraw => resolved.ArtCmd != null,
        _ => false,
    };

    /// Σ over the clips of length × proxy rate; camera proxies (when skipped) and clips without a decoder count 0.
    /// `info`: after the analysis the real duration, height and audio (spec 2.5 "přepočet před renderem").
    public static long ProxyBytes(IEnumerable<Clip> clips, ProxySettings p, Func<Clip, bool> renderable, Func<Clip, ClipInfo?>? info = null)
    {
        if (!p.Enabled) return 0;
        double bytes = 0;
        foreach (var c in clips)
        {
            if ((c.IsCameraProxy && p.SkipCameraProxies) || !renderable(c)) continue;
            var i = info?.Invoke(c);
            double seconds = i?.Duration ?? c.Bytes * 8.0 / (SourceMbps(c) * 1e6);
            int audio = p.Audio && (i == null || i.AudioChannels > 0) ? 1 : 0;
            int height = Math.Min(p.Height, i?.Height ?? p.Height);
            bytes += seconds * ProxyMbps(p, audio, height) * 1e6 / 8;
        }
        return (long)Math.Ceiling(bytes);
    }

    /// Before the copy, from the card's clip names only (R10).
    public static SpaceExtra Extra(IReadOnlyList<Clip> clips, ProxySettings p, ExternalTools resolved) =>
        new(ProxyBytes(clips, p, c => Renderable(c, resolved)), ReportBytes(clips.Count), p.CopyToAllDestinations, p.Enabled ? p.ExtraFolder : null);
}

public enum SpaceSeverity { Warning, Blocking }

/// `Volume`: which volume (two destinations on one volume add up); `Label`: how the messages name it.
public sealed record VolumeDemand(string Volume, string Label, SpaceNeed Need);

public sealed record SpaceProblem(SpaceSeverity Severity, string Label, SpaceNeed Need, long Free, string Text);

/// One volume of a plan: what it needs in total and what it has free.
public sealed record SpaceLine(string Label, SpaceNeed Need, long Free);

/// Spec 2.5 "Součet": per volume, every demand (selected cards, queued jobs, the rest of running ones) against the free
/// space. Blocking when it does not fit, Warning when less than 5 % of the need would be left (R11). Memory only.
public static class SpacePlanner
{
    /// Which volume a path is on, so demands on one volume add up; a path that cannot be asked counts on its own.
    /// Touches the disk — never on the UI thread.
    public static string VolumeKey(string path) => NativeIO.VolumeId(path) ?? Path.GetFullPath(path).ToLowerInvariant();

    public static IReadOnlyList<SpaceProblem> Check(IEnumerable<VolumeDemand> demands, IReadOnlyDictionary<string, long> free)
    {
        var problems = new List<SpaceProblem>();
        foreach (var g in demands.GroupBy(d => d.Volume, StringComparer.OrdinalIgnoreCase))
        {
            if (!free.TryGetValue(g.Key, out var f)) continue;   // not known yet: never blocks
            var need = g.Select(d => d.Need).Aggregate(SpaceNeed.None, (a, b) => a + b);
            var label = string.Join(", ", g.Select(d => d.Label).Distinct(StringComparer.OrdinalIgnoreCase));
            if (need.Total > f)
                problems.Add(new SpaceProblem(SpaceSeverity.Blocking, label, need, f, Msg.NoSpace(label, need, f)));
            else if ((f - need.Total) * 20 < need.Total)
                problems.Add(new SpaceProblem(SpaceSeverity.Warning, label, need, f, Msg.LowSpace(label, f - need.Total)));
        }
        return problems.OrderByDescending(p => p.Severity).ToList();
    }

    /// Per volume with a known free space: the summed need (the CLI's --dry-run breakdown).
    public static IReadOnlyList<SpaceLine> Lines(IEnumerable<VolumeDemand> demands, IReadOnlyDictionary<string, long> free) =>
        demands.GroupBy(d => d.Volume, StringComparer.OrdinalIgnoreCase)
            .Where(g => free.ContainsKey(g.Key))
            .Select(g => new SpaceLine(string.Join(", ", g.Select(d => d.Label).Distinct(StringComparer.OrdinalIgnoreCase)),
                g.Select(d => d.Need).Aggregate(SpaceNeed.None, (a, b) => a + b), free[g.Key]))
            .ToList();
}
```

`windows/src/Offload.Core/Messages.cs`, řádky 69–74 — najít:

```csharp
    public static string Fat32(string filesystem, string path, long size) =>
        $"{filesystem} neumí uložit soubory od 4 GB ({path}, {Format.Bytes(size)})";
    public static string NoSpace(string root, long need, long free) =>
        $"Na cíli {root} není dost místa: potřeba {Format.BytesCs(need)}, volno {Format.BytesCs(free)}.";
    public static string ReportNotWritten(string details) => $"Report se nepodařilo zapsat: {details}";
    public static string NoHashRecord(string dir) => $"Ve složce {dir} není ASC MHL ani seznam součtů — není s čím porovnat.";
```

nahradit:

```csharp
    public static string Fat32(string filesystem, string path, long size) =>
        $"{filesystem} neumí uložit soubory od 4 GB ({path}, {Format.Bytes(size)})";
    /// "Na cíli Zaloha (D:) není dost místa: potřeba 412 GB (karty 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB."
    public static string NoSpace(string where, Space.SpaceNeed need, long free) =>
        $"Na cíli {where} není dost místa: potřeba {Format.BytesCs(need.Total)} ({Parts(need, "karty")}), volno {Format.BytesCs(free)}.";
    /// CLI --dry-run: "Místo: D:\Zaloha potřeba 412 GB (karta 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB"
    public static string SpaceLine(string where, Space.SpaceNeed need, long free) =>
        $"Místo: {where} potřeba {Format.BytesCs(need.Total)} ({Parts(need, "karta")}), volno {Format.BytesCs(free)}";

    static string Parts(Space.SpaceNeed need, string cards)
    {
        var parts = new List<string>();
        if (need.Card > 0) parts.Add($"{cards} {Format.BytesCs(need.Card)}");
        if (need.Proxy > 0) parts.Add($"proxy ~{Format.BytesCs(need.Proxy)}");
        if (need.Report > 0) parts.Add($"report {Format.BytesCs(need.Report)}");
        return string.Join(", ", parts);
    }
    public static string LowSpace(string where, long left) =>
        $"Na cíli {where} zbude po offloadu jen {Format.BytesCs(left)} — rezerva pod 5 %, odhad proxy je přibližný.";
    public static string ProxiesSkippedForSpace(string where, long need, long free) =>
        $"Proxy se nevyrenderovaly: na {where} chybí místo (potřeba ~{Format.BytesCs(need)}, volno {Format.BytesCs(free)}).";
    public static string ReportNotWritten(string details) => $"Report se nepodařilo zapsat: {details}";
    public static string NoHashRecord(string dir) => $"Ve složce {dir} není ASC MHL ani seznam součtů — není s čím porovnat.";
```
`windows/src/Offload.Core/Models.cs`, řádky 18–21 — najít:

```csharp
    /// Tests only: called on the copy thread just before file i is read (a deterministic point to cancel at).
    internal Action<int>? BeforeFileCopy { get; set; }
    /// Tests only: H.264/HEVC proxies with this encoder (e.g. libx264) instead of the probed one — parallel
    /// NVENC sessions from concurrently running tests can collide.
```

nahradit:

```csharp
    /// Tests only: called on the copy thread just before file i is read (a deterministic point to cancel at).
    internal Action<int>? BeforeFileCopy { get; set; }
    /// Tests only: the free bytes of the volume of a path, instead of NativeIO.AvailableBytes.
    internal Func<string, long?>? FreeSpace { get; set; }
    /// Tests only: H.264/HEVC proxies with this encoder (e.g. libx264) instead of the probed one — parallel
    /// NVENC sessions from concurrently running tests can collide.
```

`windows/src/Offload.Core/Models.cs`, řádky 56–59 — najít:

```csharp
    /// Append: files of the earlier backup that were never read back.
    public int UnverifiedEarlier { get; init; }
}

```

nahradit:

```csharp
    /// Append: files of the earlier backup that were never read back.
    public int UnverifiedEarlier { get; init; }
    /// Spec 2.5: what each destination volume needs and has free (the CLI's --dry-run lists it).
    public IReadOnlyList<Space.SpaceLine> Space { get; init; } = [];
    /// "Na cíli … zbude po offloadu jen … — rezerva pod 5 %…" — never blocks (R11).
    public IReadOnlyList<string> SpaceWarnings { get; init; } = [];
}

```
`windows/src/Offload.Core/OffloadEngine.cs`, řádky 1–3 — najít:

```csharp
using Offload.Core.Append;

namespace Offload.Core;
```

nahradit:

```csharp
using Offload.Core.Append;
using Offload.Core.Space;

namespace Offload.Core;
```

`windows/src/Offload.Core/OffloadEngine.cs`, řádky 25–30 — najít:

```csharp
    public bool IsCancelled => cts.IsCancellationRequested;

    /// Scans the card and checks the destinations before a single byte is written.
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date)
    {
        CheckOptions();
```

nahradit:

```csharp
    public bool IsCancelled => cts.IsCancellationRequested;

    /// Scans the card and checks the destinations before a single byte is written. `extra`: what proxies and the report
    /// will need besides the card (spec 2.5), worked out from the scan; null = the card alone.
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date,
        Func<SourceScan, SpaceExtra>? extra = null)
    {
        CheckOptions();
```

`windows/src/Offload.Core/OffloadEngine.cs`, řádky 35–48 — najít:

```csharp
        CheckDestinations(source, roots);
        var scan = ScanSource(source);
        CheckFilesFit(roots, scan.Files, scan.TotalBytes);

        var day = date ?? Layout.Today();
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan { Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots };
    }

    /// An append (spec 2.2): the same checks as Prepare (options, source, destinations, Windows names, FAT32, space),
    /// but over the new files only, into the card folders of the earlier backup. `NothingNew` when there are none.
    public OffloadPlan PrepareAppend(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default)
    {
        CheckOptions();
```

nahradit:

```csharp
        CheckDestinations(source, roots);
        var scan = ScanSource(source);
        var (space, warnings) = CheckFilesFit(roots, scan, extra?.Invoke(scan));

        var day = date ?? Layout.Today();
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan { Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots, Space = space, SpaceWarnings = warnings };
    }

    /// An append (spec 2.2): the same checks as Prepare (options, source, destinations, Windows names, FAT32, space),
    /// but over the new files only, into the card folders of the earlier backup. `NothingNew` when there are none.
    public OffloadPlan PrepareAppend(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default,
        Func<SourceScan, SpaceExtra>? extra = null)
    {
        CheckOptions();
```

`windows/src/Offload.Core/OffloadEngine.cs`, řádky 56–64 — najít:

```csharp
        var problems = Scanner.WindowsProblems(scan);
        if (problems.Count > 0) throw OffloadError.Preflight(PreflightCode.BadNames, Msg.BadNames(problems));
        CheckFilesFit(a.DestRoots, scan.Files, scan.TotalBytes);
        return new OffloadPlan
        {
            Source = source, Scan = scan, Date = a.Date, CardName = a.CardName, DestRoots = a.DestRoots,
            Mode = PlanMode.Append, Generation = a.Generation, FullScan = a.FullScan, UnverifiedEarlier = a.UnverifiedEarlier,
        };
    }
```

nahradit:

```csharp
        var problems = Scanner.WindowsProblems(scan);
        if (problems.Count > 0) throw OffloadError.Preflight(PreflightCode.BadNames, Msg.BadNames(problems));
        var (space, warnings) = CheckFilesFit(a.DestRoots, scan, extra?.Invoke(scan));
        return new OffloadPlan
        {
            Source = source, Scan = scan, Date = a.Date, CardName = a.CardName, DestRoots = a.DestRoots,
            Mode = PlanMode.Append, Generation = a.Generation, FullScan = a.FullScan, UnverifiedEarlier = a.UnverifiedEarlier,
            Space = space, SpaceWarnings = warnings,
        };
    }
```

`windows/src/Offload.Core/OffloadEngine.cs`, řádky 128–143 — najít:

```csharp
    }

    /// The FAT32 limit and free space on every destination for `files` (all of them, or only the new ones of an append).
    void CheckFilesFit(IReadOnlyList<string> roots, IReadOnlyList<SourceFile> files, long totalBytes)
    {
        long needed = totalBytes + files.Count * 64L * 1024 + (64L << 20);
        foreach (var root in roots)
        {
            if (Fat32Problem(NativeIO.FilesystemType(root), files) is string fat)
                throw OffloadError.Preflight(PreflightCode.Fat32, $"{root}: {fat}");
            long free = NativeIO.AvailableBytes(root) ?? 0;
            if (free < needed) throw OffloadError.Preflight(PreflightCode.NoSpace, Msg.NoSpace(root, needed, free));
        }
    }

    /// FAT32 cannot hold files of 4 GiB or more — say so before the copy, not at 99 %.
```

nahradit:

```csharp
    }

    /// The FAT32 limit on every destination, and the space per volume (spec 2.5: two destinations on one volume add up,
    /// the proxy folder counts on its own volume) for `scan` — the whole card, or only the new files of an append.
    /// Returns the per-volume breakdown and the "rezerva pod 5 %" warnings; not enough space throws.
    (IReadOnlyList<SpaceLine> Lines, IReadOnlyList<string> Warnings) CheckFilesFit(IReadOnlyList<string> roots, SourceScan scan, SpaceExtra? extra)
    {
        foreach (var root in roots)
            if (Fat32Problem(NativeIO.FilesystemType(root), scan.Files) is string fat)
                throw OffloadError.Preflight(PreflightCode.Fat32, $"{root}: {fat}");
        long card = SpaceEstimator.CardBytes(scan.TotalBytes, scan.Files.Count);
        var demands = roots.Select((root, i) => new VolumeDemand(SpacePlanner.VolumeKey(root), root,
            extra?.ForDestination(i, card) ?? new SpaceNeed(card, 0, 0))).ToList();
        if (extra is { ProxyFolder: string folder, Proxy: > 0 } && Directory.Exists(folder))
            demands.Add(new VolumeDemand(SpacePlanner.VolumeKey(folder), folder, new SpaceNeed(0, extra.Proxy, 0)));
        var free = demands.GroupBy(d => d.Volume, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => FreeBytes(g.First().Label) ?? 0, StringComparer.OrdinalIgnoreCase);
        var problems = SpacePlanner.Check(demands, free);
        if (problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Blocking) is { } blocking)
            throw OffloadError.Preflight(PreflightCode.NoSpace, blocking.Text);
        return (SpacePlanner.Lines(demands, free), problems.Select(p => p.Text).ToList());
    }

    /// Free bytes of the volume a path is on (tests: OffloadOptions.FreeSpace).
    internal long? FreeBytes(string path) => Options.FreeSpace != null ? Options.FreeSpace(path) : NativeIO.AvailableBytes(path);

    /// FAT32 cannot hold files of 4 GiB or more — say so before the copy, not at 99 %.
```

`OffloadJob`: odhad z klipů do `Prepare`, před renderem proxy přepočet ze skutečné délky (nevejdou-li se, proxy se přeskočí s oranžovou poznámkou, kopie zůstane ověřená):

`windows/src/Offload.Core/OffloadJob.cs`, řádky 2–5 — najít:

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core;
```

nahradit:

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.Core;
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 113–118 — najít:

```csharp
    /// A full copy, or with Config.AppendInto an append into those card folders.
    public OffloadPlan Prepare() => Plan ??= Config.AppendInto is { Count: > 0 } into
        ? Engine.PrepareAppend(Config.Source, into)
        : Engine.Prepare(Config.Source, Config.DestRoots, Config.CardName, Config.Date);

    public void Cancel() => Engine.Cancel();
```

nahradit:

```csharp
    /// A full copy, or with Config.AppendInto an append into those card folders.
    public OffloadPlan Prepare() => Plan ??= Config.AppendInto is { Count: > 0 } into
        ? Engine.PrepareAppend(Config.Source, into, extra: EstimateExtra)
        : Engine.Prepare(Config.Source, Config.DestRoots, Config.CardName, Config.Date, EstimateExtra);

    /// Spec 2.5: proxies and report on top of the card, from the clip names (the clips are not read before the copy).
    SpaceExtra EstimateExtra(SourceScan scan) => SpaceEstimator.Extra(ClipDetector.Detect(scan.Files), Config.Proxy, Config.Tools.Resolved());

    public void Cancel() => Engine.Cancel();
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 376–379 — najít:

```csharp

        if (!Config.Proxy.Enabled || IsCancelled) return report;
        Update(s => s with { Phase = JobPhase.Proxies, ClipCount = entries.Count });
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
```

nahradit:

```csharp

        if (!Config.Proxy.Enabled || IsCancelled) return report;
        if (ProxySpaceProblem(entries, plan, renderer) is string noRoom)
        {
            // Spec 2.5: the copy stays verified; the proxies are left out and the job card says why (orange).
            foreach (var e in entries.Where(e => e.ProxyStatus == ProxyStatus.None))
            {
                e.ProxyStatus = ProxyStatus.Skipped;
                e.ProxyNote = noRoom;
            }
            Update(s => s with { Warning = noRoom });
            log?.Invoke(noRoom);
            RewriteReports(report, plan, log);
            return report;
        }
        Update(s => s with { Phase = JobPhase.Proxies, ClipCount = entries.Count });
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 478–481 — najít:

```csharp
    }

    /// The proxies earlier generations rendered: a new clip of the same name becomes "_2" in an append (spec 2.2).
    static List<string> ExistingProxyNames(IReadOnlyList<string> cardPaths)
```

nahradit:

```csharp
    }

    /// Spec 2.5: before the proxies, their need again from the analysed clips (real duration, height, audio) against the
    /// free space now. The message when they do not fit, or null.
    string? ProxySpaceProblem(List<ClipEntry> entries, OffloadPlan plan, ProxyRenderer renderer)
    {
        var infos = entries.Where(e => e.Info != null).ToDictionary(e => e.Clip, e => e.Info);
        long proxy = SpaceEstimator.ProxyBytes(entries.Select(e => e.Clip), Config.Proxy,
            c => infos.ContainsKey(c) && renderer.UnsupportedReason(c) == null, c => infos.GetValueOrDefault(c));
        if (proxy == 0) return null;
        var extra = new SpaceExtra(proxy, 0, Config.Proxy.CopyToAllDestinations, Config.Proxy.ExtraFolder);
        var places = plan.DestRoots.Select((root, i) => (Path: root, Label: HistoryText.DiskName(root), Need: extra.ForDestination(i, 0))).ToList();
        if (extra.ProxyFolder is string folder && Directory.Exists(folder)) places.Add((folder, folder, new SpaceNeed(0, proxy, 0)));
        var demands = places.Select(p => new VolumeDemand(SpacePlanner.VolumeKey(p.Path), p.Label, p.Need)).ToList();
        var free = places.GroupBy(p => SpacePlanner.VolumeKey(p.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Engine.FreeBytes(g.First().Path) ?? 0, StringComparer.OrdinalIgnoreCase);
        return SpacePlanner.Check(demands, free).FirstOrDefault(p => p.Severity == SpaceSeverity.Blocking) is { } b
            ? Msg.ProxiesSkippedForSpace(b.Label, b.Need.Proxy, b.Free)
            : null;
    }

    /// The proxies earlier generations rendered: a new clip of the same name becomes "_2" in an append (spec 2.2).
    static List<string> ExistingProxyNames(IReadOnlyList<string> cardPaths)
```

CLI: hláška „není dost místa“ jde z enginu („Chyba: Na cíli … není dost místa: potřeba … (karty …, proxy ~…, report …), volno ….“), varování „rezerva pod 5 %“ jako řádek `Varování: …` (pokračuje se), `--dry-run` vypíše rozpis po svazcích („Místo: D:\Zaloha potřeba 412 GB (karta 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB“); přeskočené proxy vypíše `Varování: Proxy se nevyrenderovaly: …` z `JobStatus.Warning` (úkol 15):

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 92–95 — najít:

```csharp
        foreach (var p in plan.CardPaths) Console.WriteLine($"  → {p}");
        if (plan.UnverifiedEarlier > 0) Console.WriteLine("Varování: " + Msg.UnverifiedEarlier(plan.UnverifiedEarlier));
        if (ProxyPreflight.ExtraFolderInUse(config.Proxy) is string extra)
            Console.WriteLine($"  proxy also → {Layout.CardPath(extra, plan.Date, plan.CardName)}");
```

nahradit:

```csharp
        foreach (var p in plan.CardPaths) Console.WriteLine($"  → {p}");
        if (plan.UnverifiedEarlier > 0) Console.WriteLine("Varování: " + Msg.UnverifiedEarlier(plan.UnverifiedEarlier));
        foreach (var warning in plan.SpaceWarnings) Console.WriteLine("Varování: " + warning);   // R11: never blocks
        if (dryRun)
            foreach (var line in plan.Space) Console.WriteLine(Msg.SpaceLine(line.Label, line.Need, line.Free));
        if (ProxyPreflight.ExtraFolderInUse(config.Proxy) is string extra)
            Console.WriteLine($"  proxy also → {Layout.CardPath(extra, plan.Date, plan.CardName)}");
```

- [ ] **Step 4: Testy + celá sada**

Run: `dotnet test --filter "FullyQualifiedName~SpaceEstimatorTests|FullyQualifiedName~SpacePlannerTests|FullyQualifiedName~SpaceEngineTests|FullyQualifiedName~PreflightMessagesTests|FullyQualifiedName~PreflightTests|FullyQualifiedName~AppendEngineTests"` → PASS; `dotnet test` → zelená.

- [ ] **Step 5: VYVOJ + commit**

Řádek:

```markdown
- Úkol 16 — kontrola místa po svazcích (dva cíle na jednom disku i složka pro proxy se sečtou): karta + 64 KiB/soubor + 64 MiB, proxy odhadem z velikosti klipů a toku (H.264/HEVC bitrate × 1,1 + 0,2 Mb/s zvuk, ProRes tabulka Apple × (výška/1080)²), report 32 MiB + 1 MiB/klip; hláška s rozpisem, CLI `Varování: …` pod 5 % a rozpis v `--dry-run`; před renderem proxy přepočet podle skutečné délky — když se nevejdou, proxy se přeskočí s oranžovou poznámkou a kopie zůstane ověřená
```

```powershell
git add windows docs
git commit -m "Windows: space check for proxies and reports, per volume" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 17: GUI — místo

Spec §2.5 „UX (GUI)“, §6.1 úkol 17. Výpočet jen v paměti (3×/s), vstupy: velikost karet z `CardInsight`, volné místo a svazek z `DestinationViewModel.Probe`, požadavky fronty z `JobItemViewModel`.

**Files:**
- Modify: `windows/src/Offload.App/DestinationViewModel.cs` (řádky 2–5, 14–18, 41–47, 61–69, 93–96)
- Modify: `windows/src/Offload.App/JobItemViewModel.cs` (řádky 3–6, 22–29, 82–85)
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs` (řádky 49–53, 86–89, 118–123, 138–142, 159–163, 172–175)
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs` (řádky 5–8, 465–476)
- Create: `windows/src/Offload.App/MainViewModel.Space.cs`
- Modify: `windows/src/Offload.App/SetupPanel.xaml` (řádky 23–26)
- Create: `windows/src/Offload.Core/Desktop/SpaceCheck.cs`
- Create: `windows/tests/Offload.Core.Tests/SpaceCheckTests.cs`

**Interfaces:**
- Consumes: `SpaceEstimator`, `SpacePlanner`, `SpaceNeed`, `VolumeDemand` (úkol 16), `Insight(path)`, `AppendChosen` (úkoly 7, 15).
- Produces: `record SpaceTarget(string Root, string Volume, string Label)`; `SpaceCheck.ForCard(CardInsight?, long? usedOnVolume, ProxySettings, ExternalTools, IReadOnlyList<SpaceTarget>, SpaceTarget? folder)`, `SpaceCheck.ForAppend(HistoryMatch, …)`, `SpaceCheck.Remaining(SpaceNeed, JobPhase, long written)`; `DestinationViewModel.SpaceTarget`, `FreeBytes`; `MainViewModel.SpaceProblem`, `SpaceWarning`, `RefreshSpace()`; `JobItemViewModel(…, IReadOnlyList<VolumeDemand>? space = null)`, `SpaceDemands()`.

- [ ] **Step 1: Padající testy**

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.Core.Tests;

public class SpaceCheckTests
{
    const long GB = 1_000_000_000;
    static readonly ExternalTools Tools = new() { Ffmpeg = "f" };
    static readonly SpaceTarget D = new(@"D:\Zaloha", "vol-d", "Zaloha (D:)");
    static readonly SpaceTarget F = new(@"F:\Zaloha", "vol-f", "Zaloha (F:)");

    static CardInsight Card(long bytes, int files) =>
        new() { Source = @"E:\", TotalBytes = bytes, FileCount = files, Clips = [] };

    [Fact]
    public void InspectedCard_OnEveryDestination()
    {
        var demands = SpaceCheck.ForCard(Card(100 * GB, 10), null, new ProxySettings(), Tools, [D, F], null);
        Assert.Equal(["vol-d", "vol-f"], demands.Select(d => d.Volume));
        Assert.All(demands, d => Assert.Equal(SpaceEstimator.CardBytes(100 * GB, 10), d.Need.Card));
    }

    [Fact]
    public void NotInspectedYet_UsedSpaceOfTheVolume_OrNothing()
    {
        Assert.Equal(SpaceEstimator.CardBytes(64 * GB, 0), SpaceCheck.ForCard(null, 64 * GB, new ProxySettings(), Tools, [D], null).Single().Need.Card);
        Assert.Empty(SpaceCheck.ForCard(null, null, new ProxySettings(), Tools, [D], null));
    }

    // Review focus 5: two cards for the same disk + a running job + the proxy folder on that disk.
    [Fact]
    public void TwoCards_RunningJob_ProxyFolder_OnOneVolume()
    {
        var proxy = new ProxySettings { Enabled = true, ExtraFolder = @"D:\Proxy" };
        var clip = new Clip { Name = "A001C001", Path = "CLIP/A001C001.mov", Format = ClipFormat.Video, Segments = ["CLIP/A001C001.mov"], Bytes = 50 * GB };
        var withClip = Card(50 * GB, 1) with { Clips = [clip] };
        var folder = new SpaceTarget(@"D:\Proxy", "vol-d", @"D:\Proxy");
        var demands = new List<VolumeDemand>();
        demands.AddRange(SpaceCheck.ForCard(withClip, null, proxy, Tools, [D], folder));
        demands.AddRange(SpaceCheck.ForCard(Card(100 * GB, 1), null, proxy, Tools, [D], folder));
        var running = new VolumeDemand("vol-d", "Zaloha (D:)", new SpaceNeed(200 * GB, 0, 0));
        demands.Add(running with { Need = SpaceCheck.Remaining(running.Need, JobPhase.Copying, 150 * GB) });

        var p = Assert.Single(SpacePlanner.Check(demands, new Dictionary<string, long> { ["vol-d"] = 200 * GB }));

        Assert.Equal(SpaceSeverity.Blocking, p.Severity);
        Assert.Equal(@"Zaloha (D:), D:\Proxy", p.Label);
        Assert.True(p.Need.Proxy > 2 * 8 * GB);   // the proxy twice: on the destination and in the folder
    }

    [Fact]
    public void Append_OnlyNewFiles_OnTheBackupsDestinations()
    {
        var e = HistoryFixtures.Entry(HistoryFixtures.T0);
        var m = new HistoryMatch(e, HistoryMatchKind.NewFiles, [new SourceFile("CLIP/A001C003.mov", 3 * GB, HistoryFixtures.T0)], 3 * GB, [], 0);
        var demands = SpaceCheck.ForAppend(m, new ProxySettings(), Tools, [D, new SpaceTarget(@"G:\Other", "vol-g", "Other (G:)")], null);
        var d = Assert.Single(demands);   // F:\Zaloha of the backup is not in the Cíle section: the engine checks it
        Assert.Equal(("vol-d", SpaceEstimator.CardBytes(3 * GB, 1)), (d.Volume, d.Need.Card));
    }

    [Theory]
    [InlineData(JobPhase.Waiting, 100, 10)]
    [InlineData(JobPhase.Copying, 60, 10)]
    [InlineData(JobPhase.Proxies, 0, 10)]
    [InlineData(JobPhase.Done, 0, 0)]
    [InlineData(JobPhase.Cancelled, 0, 0)]
    public void Remaining_ByPhase(JobPhase phase, long card, long proxy)
    {
        var r = SpaceCheck.Remaining(new SpaceNeed(100, 10, 0), phase, written: 40);
        Assert.Equal((card, proxy), (r.Card, r.Proxy));
    }
}
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~SpaceCheckTests"` → build FAIL.

- [ ] **Step 3: Implementace**

```csharp
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.Core.Desktop;

/// A destination as the space check sees it: its volume (from the probe made off the UI thread) and how it is named.
public sealed record SpaceTarget(string Root, string Volume, string Label);

/// The GUI's space check before a start (spec 2.5), from memory only: card sizes from the inspections, free space and
/// volumes from the destination probes. The engine checks again when each job starts — that is the last word.
public static class SpaceCheck
{
    /// One card about to start: its inspected size, or — not inspected yet — the used space of its volume as an upper
    /// estimate (`usedOnVolume`; null = unknown, then the card adds nothing and never blocks). `folder`: the proxy folder.
    public static List<VolumeDemand> ForCard(CardInsight? insight, long? usedOnVolume, ProxySettings proxy, ExternalTools tools,
        IReadOnlyList<SpaceTarget> targets, SpaceTarget? folder)
    {
        long bytes;
        int files;
        IReadOnlyList<Clip> clips;
        if (insight is { Error: null }) (bytes, files, clips) = (insight.TotalBytes, insight.FileCount, insight.Clips);
        else if (usedOnVolume is long used) (bytes, files, clips) = (used, 0, []);
        else return [];
        return Demands(SpaceEstimator.CardBytes(bytes, files), SpaceEstimator.Extra(clips, proxy, tools), targets, folder);
    }

    /// An append (spec 2.2): only the new files, onto the destinations of the earlier backup that are in `targets`
    /// (others the engine checks when the job starts).
    public static List<VolumeDemand> ForAppend(HistoryMatch match, ProxySettings proxy, ExternalTools tools,
        IReadOnlyList<SpaceTarget> targets, SpaceTarget? folder)
    {
        var mine = match.Entry.Destinations
            .Select(d => targets.FirstOrDefault(t => string.Equals(t.Root.TrimEnd('\\'), d.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            .OfType<SpaceTarget>()
            .ToList();
        var clips = ClipDetector.Detect(match.New);
        return Demands(SpaceEstimator.CardBytes(match.NewBytes, match.New.Count), SpaceEstimator.Extra(clips, proxy, tools), mine, folder);
    }

    static List<VolumeDemand> Demands(long card, SpaceExtra extra, IReadOnlyList<SpaceTarget> targets, SpaceTarget? folder)
    {
        var demands = targets.Select((t, i) => new VolumeDemand(t.Volume, t.Label, extra.ForDestination(i, card))).ToList();
        if (folder != null && extra.ProxyFolder != null && extra.Proxy > 0)
            demands.Add(new VolumeDemand(folder.Volume, folder.Label, new SpaceNeed(0, extra.Proxy, 0)));
        return demands;
    }

    /// What a job in the queue still needs: all of it while waiting, the rest of the card while copying (`written` on
    /// that destination), proxies and report during stage 2, nothing once finished or cancelled.
    public static SpaceNeed Remaining(SpaceNeed need, JobPhase phase, long written) => phase switch
    {
        JobPhase.Waiting => need,
        JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying or JobPhase.Reporting => need with { Card = Math.Max(0, need.Card - written) },
        JobPhase.Analyzing or JobPhase.Proxies => need with { Card = 0 },
        _ => SpaceNeed.None,
    };
}
```

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.App;

/// Spec 2.5 in the window: red "není dost místa" blocks the start, orange "rezerva pod 5 %" only warns. Memory only —
/// sizes from the card inspections, volumes and free space from the destination probes; evaluated 3×/s.
public sealed partial class MainViewModel
{
    string? spaceProblem, spaceWarning;
    // The proxy folder's volume and free space, read off the UI thread with its existence check.
    SpaceTarget? folderTarget;
    long? folderFree;

    public string? SpaceProblem => spaceProblem;
    public string? SpaceWarning => spaceWarning;

    List<SpaceTarget> SpaceTargets() => Destinations.Select(d => d.SpaceTarget).OfType<SpaceTarget>().ToList();

    /// The used space of a scanned card volume: the upper estimate while its inspection has not finished.
    long? UsedOnVolume(string path) =>
        volumes.FirstOrDefault(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase)) is { } v ? v.Total - v.Free : null;

    /// What starting `path` now would need on each destination volume (also handed to its job card).
    List<VolumeDemand> CardSpace(string path, IReadOnlyList<SpaceTarget> targets) =>
        AppendChosen(path) && Insight(path)?.History is { } known
            ? SpaceCheck.ForAppend(known, Settings.Proxy, resolvedTools, targets, folderTarget)
            : SpaceCheck.ForCard(Insight(path), UsedOnVolume(path), Settings.Proxy, resolvedTools, targets, folderTarget);

    /// Recomputes both texts; raises them (and CanStart) only when one changed.
    void RefreshSpace()
    {
        var targets = SpaceTargets();
        var demands = SelectedSources().SelectMany(p => CardSpace(p, targets)).ToList();
        foreach (var j in Jobs.OfType<JobItemViewModel>()) demands.AddRange(j.SpaceDemands());
        var free = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Destinations)
            if (d.SpaceTarget is { } t && d.FreeBytes is long f) free.TryAdd(t.Volume, f);
        if (folderTarget is { } ft && folderFree is long ff) free.TryAdd(ft.Volume, ff);
        var problems = SpacePlanner.Check(demands, free);
        var problem = problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Blocking)?.Text;
        var warning = problem == null ? problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Warning)?.Text : null;
        if (problem == spaceProblem && warning == spaceWarning) return;
        spaceProblem = problem;
        spaceWarning = warning;
        RaiseMany(nameof(SpaceProblem), nameof(SpaceWarning), nameof(CanStart));
    }
}
```

Svazek a volné místo cíle i složky pro proxy z kontrol mimo UI vlákno; Start počká na zjištění vybraných karet nejvýš 10 s (`await`, tlačítko je mezitím neaktivní jako při kontrole LUTu); běžící úlohy se počítají zbytkem:

`windows/src/Offload.App/DestinationViewModel.cs`, řádky 2–5 — najít:

```csharp
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;
```

nahradit:

```csharp
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.App;
```

`windows/src/Offload.App/DestinationViewModel.cs`, řádky 14–18 — najít:

```csharp
    bool exists, isChecking = true, probing, mayDropOut;
    string fileSystem = "", freeText = "zjišťuji…";
    string? fsWarning;

    public DestinationViewModel(MainViewModel owner, int index, string root)
```

nahradit:

```csharp
    bool exists, isChecking = true, probing, mayDropOut;
    string fileSystem = "", freeText = "zjišťuji…";
    string? fsWarning, volume;
    long? freeBytes;

    public DestinationViewModel(MainViewModel owner, int index, string root)
```

`windows/src/Offload.App/DestinationViewModel.cs`, řádky 41–47 — najít:

```csharp
            if (t.IsCompletedSuccessfully)
            {
                var (ex, fs, free, volatileDisk) = t.Result;
                exists = ex;
                mayDropOut = volatileDisk;
                fileSystem = fs;
                freeText = ex ? $"{(free is long f ? Format.Bytes(f) : "?")} volno · {fs}" : "";
```

nahradit:

```csharp
            if (t.IsCompletedSuccessfully)
            {
                var (ex, fs, free, volatileDisk, vol) = t.Result;
                exists = ex;
                mayDropOut = volatileDisk;
                volume = ex ? vol : null;
                freeBytes = ex ? free : null;
                fileSystem = fs;
                freeText = ex ? $"{(free is long f ? Format.Bytes(f) : "?")} volno · {fs}" : "";
```

`windows/src/Offload.App/DestinationViewModel.cs`, řádky 61–69 — najít:

```csharp
    }

    static (bool, string, long?, bool) ProbeDisk(string root)
    {
        bool mayDropOut = DestinationHealth.MayDropOut(root, DriveTypeOf(root));
        if (!Directory.Exists(root)) return (false, "", null, mayDropOut);
        return (true, NativeIO.FilesystemType(root), NativeIO.AvailableBytes(root), mayDropOut);
    }

```

nahradit:

```csharp
    }

    static (bool, string, long?, bool, string?) ProbeDisk(string root)
    {
        bool mayDropOut = DestinationHealth.MayDropOut(root, DriveTypeOf(root));
        if (!Directory.Exists(root)) return (false, "", null, mayDropOut, null);
        return (true, NativeIO.FilesystemType(root), NativeIO.AvailableBytes(root), mayDropOut, SpacePlanner.VolumeKey(root));
    }

```

`windows/src/Offload.App/DestinationViewModel.cs`, řádky 93–96 — najít:

```csharp
    public string FreeText => freeText;
    public string? FsWarning => fsWarning;
    public RelayCommand RemoveCommand { get; }
}
```

nahradit:

```csharp
    public string FreeText => freeText;
    public string? FsWarning => fsWarning;
    /// For the space check (spec 2.5): the volume and its free bytes from the last probe; null until known or when missing.
    public SpaceTarget? SpaceTarget => exists && volume != null ? new SpaceTarget(Root, volume, HistoryText.DiskName(Root)) : null;
    public long? FreeBytes => freeBytes;
    public RelayCommand RemoveCommand { get; }
}
```
`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 5–8 — najít:

```csharp
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;
```

nahradit:

```csharp
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Space;

namespace Offload.App;
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 465–476 — najít:

```csharp
        if (ProxyPreflight.ExtraFolderInUse(Settings.Proxy) is not string folder)
        {
            SetProxyFolderState(null, null);
            return;
        }
        folderChecking = true;
        Task.Run(() => Directory.Exists(folder)).ContinueWith(t =>
        {
            folderChecking = false;
            if (t.IsCompletedSuccessfully) SetProxyFolderState(folder, t.Result);
            else AppLog.Write($"proxy folder check: {t.Exception?.GetBaseException().Message}");
            if (folderRecheck)
```

nahradit:

```csharp
        if (ProxyPreflight.ExtraFolderInUse(Settings.Proxy) is not string folder)
        {
            folderTarget = null;
            folderFree = null;
            SetProxyFolderState(null, null);
            return;
        }
        folderChecking = true;
        // With its volume and free space: the proxies count there too (spec 2.5).
        Task.Run(() => Directory.Exists(folder)
                ? (Exists: true, Volume: SpacePlanner.VolumeKey(folder), Free: NativeIO.AvailableBytes(folder))
                : (Exists: false, Volume: (string?)null, Free: (long?)null))
            .ContinueWith(t =>
        {
            folderChecking = false;
            if (t.IsCompletedSuccessfully)
            {
                var (exists, volume, free) = t.Result;
                folderTarget = exists && volume != null ? new SpaceTarget(folder, volume, folder) : null;
                folderFree = free;
                SetProxyFolderState(folder, exists);
            }
            else AppLog.Write($"proxy folder check: {t.Exception?.GetBaseException().Message}");
            if (folderRecheck)
```
`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 49–53 — najít:

```csharp
    bool ReadyToStart => !closing && selected.Count > 0
        && (!AppendRules.NeedsDestinations(selected.Count(p => !AppendChosen(p))) || (Settings.DestRoots.Count > 0 && Destinations.All(d => d.Exists)))
        && LutProblem == null && ProxyFolderProblem == null && StartProblem == null;
    /// The hint under the start bar: why Start is off when the setup itself does not say it (a speed measurement runs).
    public string? StartProblem => QueueRules.StartProblem(Measuring);
```

nahradit:

```csharp
    bool ReadyToStart => !closing && selected.Count > 0
        && (!AppendRules.NeedsDestinations(selected.Count(p => !AppendChosen(p))) || (Settings.DestRoots.Count > 0 && Destinations.All(d => d.Exists)))
        && LutProblem == null && ProxyFolderProblem == null && StartProblem == null && SpaceProblem == null;
    /// The hint under the start bar: why Start is off when the setup itself does not say it (a speed measurement runs).
    public string? StartProblem => QueueRules.StartProblem(Measuring);
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 86–89 — najít:

```csharp
                break;
            }
            // Spec 2.1: cards history already knows are copied again in full only when the user says so.
            var skip = new List<string>();
```

nahradit:

```csharp
                break;
            }
            // Spec 2.5: the space check needs every selected card's size — wait for their inspections, at most 10 s
            // (awaited: the UI thread never waits on the card); then decide with what is known.
            var until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until && SelectedSources().Any(p => Insight(p) == null && (history == null || inspections.IsRunning(p))))
                await Task.Delay(200);
            RefreshSpace();
            if (SpaceProblem != null) return;   // the red text says why
            // Spec 2.1: cards history already knows are copied again in full only when the user says so.
            var skip = new List<string>();
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 118–123 — najít:

```csharp
    void StartJobs(IReadOnlyCollection<string> skip)
    {
        foreach (var src in SelectedSources())
        {
            if (skip.Contains(src, StringComparer.OrdinalIgnoreCase))
            {
```

nahradit:

```csharp
    void StartJobs(IReadOnlyCollection<string> skip)
    {
        var targets = SpaceTargets();
        foreach (var src in SelectedSources())
        {
            var space = CardSpace(src, targets);   // the job card keeps counting it until it has copied (spec 2.5)
            if (skip.Contains(src, StringComparer.OrdinalIgnoreCase))
            {
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 138–142 — najít:

```csharp
                };
                var title = AppendRules.Title(Path.GetFileName(known.Entry.Destinations[0].CardPath.TrimEnd('\\')));
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), title, canEject: isVolume && CardEject.CanEject(src));
                Jobs.Insert(0, appendItem);
                selected.Remove(src);
```

nahradit:

```csharp
                };
                var title = AppendRules.Title(Path.GetFileName(known.Entry.Destinations[0].CardPath.TrimEnd('\\')));
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), title, canEject: isVolume && CardEject.CanEject(src), space);
                Jobs.Insert(0, appendItem);
                selected.Remove(src);
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 159–163 — najít:

```csharp
            };
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
                canEject: isVolume && CardEject.CanEject(src));
            Jobs.Insert(0, item);
            selected.Remove(src);
```

nahradit:

```csharp
            };
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
                canEject: isVolume && CardEject.CanEject(src), space);
            Jobs.Insert(0, item);
            selected.Remove(src);
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 172–175 — najít:

```csharp
        foreach (var j in Jobs)
            if (!j.IsFinished) j.Refresh();
        // A queued job cancelled behind a running copy is finished before the queue reports it.
        if (HasFinished != shownHasFinished)
```

nahradit:

```csharp
        foreach (var j in Jobs)
            if (!j.IsFinished) j.Refresh();
        RefreshSpace();   // memory only: running jobs need less as they copy
        // A queued job cancelled behind a running copy is finished before the queue reports it.
        if (HasFinished != shownHasFinished)
```
`windows/src/Offload.App/JobItemViewModel.cs`, řádky 3–6 — najít:

```csharp
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.App;
```

nahradit:

```csharp
using Offload.Core.Desktop;
using Offload.Core.Hardware;
using Offload.Core.Space;

namespace Offload.App;
```

`windows/src/Offload.App/JobItemViewModel.cs`, řádky 22–29 — najít:

```csharp
    bool chainRequested, ejected, ejecting, openingReport;

    public JobItemViewModel(MainViewModel owner, OffloadJob job, string sourceName, bool canEject)
    {
        this.owner = owner;
        this.sourceName = sourceName;
        Job = job;
        CanEject = canEject;
```

nahradit:

```csharp
    bool chainRequested, ejected, ejecting, openingReport;

    readonly IReadOnlyList<VolumeDemand> space;

    /// `space`: what the card needs on each destination volume (spec 2.5) — counted while this job is queued or copies.
    public JobItemViewModel(MainViewModel owner, OffloadJob job, string sourceName, bool canEject, IReadOnlyList<VolumeDemand>? space = null)
    {
        this.owner = owner;
        this.sourceName = sourceName;
        this.space = space ?? [];
        Job = job;
        CanEject = canEject;
```

`windows/src/Offload.App/JobItemViewModel.cs`, řádky 82–85 — najít:

```csharp

    public override JobPhase Phase => progress.Status.Phase;
    public bool IsRunning => progress.IsRunning;
    public override bool IsFinished => Phase.IsFinished();
```

nahradit:

```csharp

    public override JobPhase Phase => progress.Status.Phase;

    /// What this job still needs (spec 2.5): all of it while queued, the rest while copying — counted against the least
    /// written destination, on the safe side — proxies and report in stage 2, nothing once finished.
    internal IEnumerable<VolumeDemand> SpaceDemands()
    {
        var written = progress.Status.Copy?.WrittenBytes is { Length: > 0 } w ? w.Min() : 0;
        return space.Select(d => d with { Need = SpaceCheck.Remaining(d.Need, Phase, written) });
    }
    public bool IsRunning => progress.IsRunning;
    public override bool IsFinished => Phase.IsFinished();
```

Červená a oranžová hláška pod lištou *Spustit offload*:

`windows/src/Offload.App/SetupPanel.xaml`, řádky 23–26 — najít:

```xml
                <TextBlock DockPanel.Dock="Bottom" Text="{Binding StartProblem}" Style="{StaticResource WarningText}" Margin="0,8,0,0" TextWrapping="Wrap"
                           Visibility="{Binding StartProblem, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="StartProblemText" />
                <Button DockPanel.Dock="Right" Command="{Binding StartCommand}" Style="{DynamicResource AccentButtonStyle}" MinWidth="150" Padding="16,8"
                        ToolTip="Ctrl+Enter" AutomationProperties.AutomationId="StartButton">
```

nahradit:

```xml
                <TextBlock DockPanel.Dock="Bottom" Text="{Binding StartProblem}" Style="{StaticResource WarningText}" Margin="0,8,0,0" TextWrapping="Wrap"
                           Visibility="{Binding StartProblem, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="StartProblemText" />
                <TextBlock DockPanel.Dock="Bottom" Text="{Binding SpaceWarning}" Style="{StaticResource WarningText}" Margin="0,8,0,0"
                           Visibility="{Binding SpaceWarning, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="SpaceWarningText" />
                <TextBlock DockPanel.Dock="Bottom" Text="{Binding SpaceProblem}" Style="{StaticResource ErrorText}" Margin="0,8,0,0"
                           Visibility="{Binding SpaceProblem, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="SpaceProblemText" />
                <Button DockPanel.Dock="Right" Command="{Binding StartCommand}" Style="{DynamicResource AccentButtonStyle}" MinWidth="150" Padding="16,8"
                        ToolTip="Ctrl+Enter" AutomationProperties.AutomationId="StartButton">
```

- [ ] **Step 4: Testy + build**

Run: `dotnet test --filter "FullyQualifiedName~SpaceCheckTests"` → PASS (9); `dotnet build` bez varování; `dotnet test` → zelená. Ruční ověření: úkol 19, bod 19.

- [ ] **Step 5: VYVOJ + commit**

Řádek:

```markdown
- Úkol 17 — GUI: pod lištou Spustit offload červeně „Na cíli … není dost místa: potřeba … (karty …, proxy ~…, report …), volno …“ (start zablokovaný) a oranžově „… zbude po offloadu jen … — rezerva pod 5 %…“; počítají se vybrané karty (velikost ze zjištění, do té doby obsazené místo svazku), čekající a běžící úlohy (zbytek) a složka pro proxy; Start počká na zjištění karet nejvýš 10 s
```

```powershell
git add windows docs
git commit -m "Windows GUI: space check before the start" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 18: Dokumentace a verze 0.2.0

Spec §5.1–5.4 (formáty, migrace), §8 (Mac), R22, §6.1 úkol 18: FORMATY, ROZDILY, VYVOJ-WINDOWS (tabulka plánů), VYVOJ-MAC („Naplánováno“), README, návod (kapitoly 3, 5, 8, 10, 11, 12; věta „hlášky jsou zatím anglicky“ pryč), verze 0.2.0.

**Files:**
- Modify: `docs/FORMATY.md` (řádky 16–21, 42–45, 52–56, 93–94)
- Modify: `docs/ROZDILY.md` (řádky 17–21, 43–46, 128–130)
- Modify: `docs/VYVOJ-MAC.md` (řádky 17–20)
- Modify: `docs/VYVOJ-WINDOWS.md` (řádky 17–20)
- Modify: `windows/Directory.Build.props` (řádky 6–11)
- Modify: `windows/README.md` (řádky 18–21, 100–104, 137–141)
- Modify: `windows/package/navod/navod.html` (řádky 316–320, 396–401, 437–441, 499–502, 521–530, 732–735, 866–872, 891–899, 903–908, 912–916, 924–928, 934–939, 952–955, 958–963, 984–992, 1029–1033)
- Modify: `windows/src/Offload.Core/Format.cs` (řádky 5–9)
- Modify: `windows/tests/Offload.Core.Tests/FormatTests.cs` (řádky 22–26)
- Modify: `windows/package/Offload-navod.pdf` (znovu vytištěný z `navod.html`)

**Interfaces:**
- Produces: `OffloadVersion.String = "0.2.0-win"`, `Directory.Build.props` `Version 0.2.0` / `InformationalVersion 0.2.0-win`.

- [ ] **Step 1: Verze (test nejdřív)**

`windows/tests/Offload.Core.Tests/FormatTests.cs`, řádky 22–26 — najít:

```csharp

    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.1.0-win", OffloadVersion.String);

    // Spec 2.1/2.5: sizes inside Czech sentences.
```

nahradit:

```csharp

    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.2.0-win", OffloadVersion.String);

    // Spec 2.1/2.5: sizes inside Czech sentences.
```

Run: `dotnet test --filter "FullyQualifiedName~FormatTests"` → FAIL (`0.1.0-win`). Pak:

`windows/src/Offload.Core/Format.cs`, řádky 5–9 — najít:

```csharp
public static class OffloadVersion
{
    public const string String = "0.1.0-win";
}

```

nahradit:

```csharp
public static class OffloadVersion
{
    public const string String = "0.2.0-win";
}

```
`windows/Directory.Build.props`, řádky 6–11 — najít:

```xml
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Version>0.1.0</Version>
    <InformationalVersion>0.1.0-win</InformationalVersion>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
```

nahradit:

```xml
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Version>0.2.0</Version>
    <InformationalVersion>0.2.0-win</InformationalVersion>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
```

Run: `dotnet test --filter "FullyQualifiedName~FormatTests"` → PASS.

- [ ] **Step 2: FORMATY.md**

`docs/FORMATY.md`, řádky 16–21 — najít:

```markdown
        <karta>_report.pdf
        <karta>.xxh64
    ascmhl/
        0001_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl
        ascmhl_chain.xml
    _PROXY/                           jen když se renderují proxy
```

nahradit:

```markdown
        <karta>_report.pdf
        <karta>.xxh64
        offload_report_0002.json      jen po doplnění (generace 2, 3 …): report jen nových souborů
        offload_report_0002.txt
        <karta>_report_0002.html
        <karta>_report_0002.pdf
        <karta>_verify_<YYYY-MM-DD_HHMMSS>Z.html   jen po ověření zálohy s reportem
        <karta>_verify_<YYYY-MM-DD_HHMMSS>Z.pdf
        offload_verify_<YYYY-MM-DD_HHMMSS>Z.json
    ascmhl/
        0001_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl
        0002_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl     jen po doplnění
        ascmhl_chain.xml
    _PROXY/                           jen když se renderují proxy
```

`docs/FORMATY.md`, řádky 42–45 — najít:

```markdown
Čtečka (`offload verify`) toleruje i `\r\n`.

## `offload_report.json`

```

nahradit:

```markdown
Čtečka (`offload verify`) toleruje i `\r\n`.

Po doplnění (Windows od 0.2.0) se soubor přepíše přes dočasné jméno: původní řádky beze změny, za nimi řádky
nových souborů. Starší `offload verify` (Mac i Windows), který čte jen tenhle seznam, tak ověří i doplněné soubory.

## `offload_report.json`

```

`docs/FORMATY.md`, řádky 52–56 — najít:

```markdown
|---|---|---|
| `tool` | string | `offload` |
| `version` | string | `0.1.0` (Mac), `0.1.0-win` (Windows) |
| `hashAlgorithm` | string | `xxh64` |
| `cardName`, `date`, `source`, `sourceFilesystem` | string | |
```

nahradit:

```markdown
|---|---|---|
| `tool` | string | `offload` |
| `version` | string | `0.1.0` (Mac), `0.2.0-win` (Windows) |
| `generation` | číslo? | jen Windows od 0.2.0, jen v reportu doplnění (`offload_report_NNNN.json`): číslo generace ASC MHL; chybí = 1 (celá kopie) |
| `hashAlgorithm` | string | `xxh64` |
| `cardName`, `date`, `source`, `sourceFilesystem` | string | |
```

`docs/FORMATY.md`, řádky 93–94 — najít:

```markdown
- `ascmhl_chain.xml` s C4 ID souboru MHL (`c4` + base58(SHA-512), doplněno `1` na 88 znaků).
- Ověřitelné nástrojem `ascmhl verify <složka karty>`.
```

nahradit:

```markdown
- `ascmhl_chain.xml` s C4 ID souboru MHL (`c4` + base58(SHA-512), doplněno `1` na 88 znaků).
- Ověřitelné nástrojem `ascmhl verify <složka karty>`.

### Doplnění (Windows od 0.2.0, Mac naplánováno)

Karta, na kterou kamera po offloadu natočila další klipy, se doplní do **stejné** složky karty na všech cílech:

- jen nové soubory; existující soubory ani reporty se nikdy nepřepíšou (soubor, který se mezitím objeví, selže);
- nová generace ASC MHL `NNNN_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl` obsahuje **jen nové soubory** (`action="original"`,
  `<process>transfer</process>`, `<comment>Offload doplnění: 12 nových souborů z E:\</comment>`, `<ignore>` stejné
  jako generace 1); `ascmhl_chain.xml` se přepíše: dřívější `<hashlist>` beze změny + nový se `sequencenr` = N a C4;
- report doplnění `_REPORT/offload_report_NNNN.json/.txt`, `<karta>_report_NNNN.html/.pdf` (NNNN = generace MHL, stejná
  na všech cílech) s klíčem `generation`; `<karta>.xxh64` se rozšíří;
- doplnění vyžaduje dostupné všechny dřívější cíle a shodný počet generací MHL na nich (jinak se nic nezkopíruje).

## Report ověření (Windows od 0.2.0)

„Ověřit zálohu“ (GUI) a `offload verify --report` zapisují jen do `_REPORT` (do `ascmhl/` nikdy — `_REPORT` je
v ignore vzorech MHL, další `ascmhl verify` tak projde):

- `_REPORT/<karta>_verify_<YYYY-MM-DD_HHMMSS>Z.html` a `.pdf` (čas začátku ověření v UTC);
- `_REPORT/offload_verify_<YYYY-MM-DD_HHMMSS>Z.json` — UTF-8 bez BOM, seřazené klíče, data ISO 8601 UTC na sekundy:

| Klíč | Typ | Význam |
|---|---|---|
| `tool`, `version` | string | `offload`, `0.2.0-win` |
| `cardPath` | string | ověřená složka karty |
| `started`, `finished` | datum | |
| `basis` | string | `ascmhl` (všechny generace) nebo `hashList` (jen `<karta>.xxh64` / `.md5`) |
| `generations` | číslo | počet přečtených generací MHL (u `hashList` 0) |
| `hashAlgorithms` | string[] | např. `["xxh64"]`; čtou se `xxh64`, `md5`, `sha1`, `xxh3`, `xxh128`, `c4` |
| `totalFiles`, `totalBytes`, `ok` | číslo | soubory v záznamu, jejich velikost, kolik sedí |
| `changed[]`, `missing[]`, `extra[]`, `failedAtCopy[]` | string[] | relativní cesty s `/` |
| `unreadable[]` | objekt[] | `{path, error}` |
| `chainProblems[]` | string[] | česky: změněná nebo chybějící generace, chybějící řetěz |
| `success` | bool | nic nesedí / nechybí / není nečitelné |

## settings.json (jen tento počítač)

- Neznámé klíče (na horní úrovni i v `proxy`) se při uložení zachovají — Windows od 0.2.0 (verze 0.1.0 by je smazala).
- `naming.suggestFromClips` (bool, výchozí `true`, Windows od 0.2.0): navrhovat název složky karty z názvů klipů.

## Místní soubory (jen tento počítač, Mac převezme stejný tvar)

`%APPDATA%\Offload\history.json` (Mac: `~/Library/Application Support/Offload/history.json`) — historie offloadů,
nikam se neposílá. UTF-8 bez BOM, odsazené, data ISO 8601 UTC na sekundy; zapisuje GUI i CLI (pod pojmenovaným
mutexem `Local\Offload-history`, přes dočasné jméno). `version` 1; vyšší neznámá verze = jen číst. Nejvýš 5 000 záznamů
(nejstarší podle `finished` se smažou i se seznamem souborů). Poškozený soubor se přejmenuje na
`history.json.corrupt-<yyyyMMdd-HHmmss>` a začne se prázdný.

| Klíč záznamu | Typ | Význam |
|---|---|---|
| `id` | string | `20261006T081500Z-3f2a9c1b` (konec kopie v UTC + 8 hex) |
| `card` | objekt? | svazek karty: `identity` (`\\?\Volume{…}\|1A2B3C4D`, jen informace), `fsSerial`, `capacity`, `label`, `filesystem`; u ručně přidané složky `null` |
| `source`, `cardName`, `date` | string | zdroj, název složky karty, složka data |
| `project` | string? | projekt (plní plán 8), jinak `null` |
| `finished` | datum | konec poslední kopie / doplnění |
| `success`, `verified` | bool | všechny generace bez chyb / ověřené |
| `failedFiles` | číslo? | jen když `success` = false |
| `hashAlgorithms` | string[] | `["xxh64"]` |
| `files`, `bytes` | číslo | karta naposledy |
| `fingerprint`, `shape` | string | SHA-256 (hex) přes seřazené řádky `relpath\tsize\tmtimeTicksUtc\n` resp. `relpath\tsize\n` |
| `fileList` | string | `history/<id>.files.gz` — gzip řádků `relpath\tsize\tmtimeTicksUtc\n` |
| `destinations[]` | objekt | `root`, `cardPath` |
| `generations[]` | objekt | `seq`, `kind` (`full` / `append`), `finished`, `files`, `bytes`, `verified` |
| `reels[]` | string | cívky z názvů klipů (`A001`) |

Karta se pozná podle `fsSerial` + `capacity` (GUID svazku dává Windows podle čtečky); ručně přidaná složka podle `shape`.
```

- [ ] **Step 3: ROZDILY.md, VYVOJ-MAC.md, VYVOJ-WINDOWS.md (tabulka plánů)**

`docs/ROZDILY.md`, řádky 17–21 — najít:

```markdown
| Systém | macOS 13+ | Windows 10/11 x64 |
| Jazyk / UI | Swift 5.9, SwiftUI | C# / .NET 10, WPF |
| Verze | 0.1.0 | 0.1.0-win (ve vývoji) |
| Aplikace | `Offload.app` (CLI uvnitř `Contents/Resources/bin/offload`) | `Offload.exe` + CLI `bin\offload.exe` |
| Build | `mac/scripts/build-app.sh` | `windows/scripts/build.ps1` |
```

nahradit:

```markdown
| Systém | macOS 13+ | Windows 10/11 x64 |
| Jazyk / UI | Swift 5.9, SwiftUI | C# / .NET 10, WPF |
| Verze | 0.1.0 | 0.2.0-win (ve vývoji) |
| Aplikace | `Offload.app` (CLI uvnitř `Contents/Resources/bin/offload`) | `Offload.exe` + CLI `bin\offload.exe` |
| Build | `mac/scripts/build-app.sh` | `windows/scripts/build.ps1` |
```

`docs/ROZDILY.md`, řádky 43–46 — najít:

```markdown
| **Vestavěné LUTy + automatický výběr podle kamery** (`proxy.lutMode`, `--lut auto`, `offload luts`) | ⏳ naplánováno | ✅ CLI + GUI (GUI čeká ruční test s klipy z kamer) |
| **Složka pro proxy navíc** (`proxy.extraFolder`, `--proxy-folder`) | ⏳ naplánováno | ✅ CLI + GUI (GUI čeká ruční test) |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ❌ | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI: řetěz připojení s rychlostí, % linky, součtem na řadiči a úzkým hrdlem na kartě úlohy |

```

nahradit:

```markdown
| **Vestavěné LUTy + automatický výběr podle kamery** (`proxy.lutMode`, `--lut auto`, `offload luts`) | ⏳ naplánováno | ✅ CLI + GUI (GUI čeká ruční test s klipy z kamer) |
| **Složka pro proxy navíc** (`proxy.extraFolder`, `--proxy-folder`) | ⏳ naplánováno | ✅ CLI + GUI (GUI čeká ruční test) |
| **Karta už zálohovaná** (`history.json`, řádek u karty, dotaz před celou kopií) | ⏳ naplánováno | ✅ CLI + GUI (GUI čeká ruční test s kartou) |
| **Doplnit jen nové klipy** (generace ASC MHL N, `offload_report_NNNN`, `offload append`) | ⏳ naplánováno | ✅ CLI + GUI (`ascmhl verify` po doplnění čeká ruční test) |
| **Název karty z klipů** (`A001C003…` → `A001`, další v řadě, `--name auto`) | ⏳ naplánováno | ✅ CLI + GUI |
| **Ověřit zálohu** (všechny generace MHL, hashe xxh64/md5/sha1/xxh3/xxh128/c4, report ověření) | `verify` jen proti `.xxh64` | ✅ CLI `verify` + GUI Nástroje → Ověřit zálohu… |
| **Kontrola místa** (proxy, report, fronta, po svazcích, rezerva 5 %) | jen karta | ✅ CLI + GUI |
| **Hlášky kontrol před kopií česky** (`PreflightCode`) | anglicky | ✅ |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ❌ | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI: řetěz připojení s rychlostí, % linky, součtem na řadiči a úzkým hrdlem na kartě úlohy |

```

`docs/ROZDILY.md`, řádky 128–130 — najít:

```markdown
- **Enkodér proxy v reportu:** Windows zapisuje do JSON volitelné `proxyEncoder` (např. `h264_nvenc`, `libx264`)
  a ukazuje ho v řádku „Proxy: H.264 (h264_nvenc), …“ v HTML/PDF. Mac klíč nezná a ignoruje ho.
- **Verze v reportu:** Mac `0.1.0`, Windows `0.1.0-win` — z reportu je vidět, která verze kopírovala.
```

nahradit:

```markdown
- **Enkodér proxy v reportu:** Windows zapisuje do JSON volitelné `proxyEncoder` (např. `h264_nvenc`, `libx264`)
  a ukazuje ho v řádku „Proxy: H.264 (h264_nvenc), …“ v HTML/PDF. Mac klíč nezná a ignoruje ho.
- **Verze v reportu:** Mac `0.1.0`, Windows `0.2.0-win` — z reportu je vidět, která verze kopírovala.
- **Historie karet** (zatím jen Windows, pro Mac naplánováno): `%APPDATA%\Offload\history.json` (formát ve FORMATY.md,
  „Místní soubory“) — řádek „Zálohováno … — ověřeno“ u karty, dotaz „Karty už jsou zálohované“ před celou kopií, CLI
  vypíše řádek před plánem; `--no-history` historii vypne. Mac klíč karty převezme z UUID svazku + kapacity.
- **Doplnění** (zatím jen Windows): generace ASC MHL N a `offload_report_NNNN.*` (FORMATY.md). Mac `verify` čte jen
  `<karta>.xxh64`, který doplnění rozšíří — doplněnou zálohu tedy ověří i Mac.
- **Ověření zálohy:** Windows ověřuje proti ASC MHL (všechny generace, i MHL ze Silverstacku/ShotPutu), bez `ascmhl/`
  proti `.xxh64`; Mac zatím jen proti `.xxh64`. Výstup `verify` na Windows: strojové řádky (`MISMATCH`, `MISSING`, `ERROR`,
  `EXTRA`, nově `FAILED-AT-COPY`, `CHAIN`) a český souhrn místo `OK`/`FAILED`.
- **Kontrola místa** (chování, ne formát): Windows sčítá požadavky po svazcích (karta + 64 KiB na soubor + 64 MiB, proxy
  podle odhadu z délky a datového toku, report 32 MiB + 1 MiB na klip, čekající a běžící úlohy, složka pro proxy) a před
  renderem proxy přepočítá podle skutečné délky klipů (nevejde-li se, proxy se přeskočí, kopie zůstane ověřená). Mac
  kontroluje jen kartu.
- **Hlášky česky:** kontroly před kopií, CLI („Chyba: …“, souhrn `verify`) a chyby parseru (kde je System.CommandLine
  přeložené) jsou na Windows česky; logy, TXT report a `--help` zůstávají anglicky. Mac je zatím anglicky.
```
`docs/VYVOJ-MAC.md`, řádky 17–20 — najít:

```markdown
  proxy, vestavěné LUTy s výběrem podle kamery, ikonu a Blackmagic RAW jako Windows (gama → LUT, BRAW jen s knihovnou, limit
  `--info`, české poznámky, náhledy pod hlídačem). Průběh zapisují úkoly plánu níže; úkol 26 tuto odrážku smaže.

- **Mimo plán — zbývá i po něm:**
```

nahradit:

```markdown
  proxy, vestavěné LUTy s výběrem podle kamery, ikonu a Blackmagic RAW jako Windows (gama → LUT, BRAW jen s knihovnou, limit
  `--info`, české poznámky, náhledy pod hlídačem). Průběh zapisují úkoly plánu níže; úkol 26 tuto odrážku smaže.

- **Karty a bezpečnost dat (Windows plán 6, 2026-10-06):** návrh
  [`windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md`](../windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md)
  §2 a §8. Mac převezme: **historii** (`history.json` v Application Support ve stejném formátu — FORMATY.md „Místní
  soubory“; klíč karty = UUID svazku z Disk Arbitration + kapacita, ověřit, že u FAT/exFAT vyjde stejné `fsSerial` jako
  na Windows; texty řádku u karty a dialog „Karty už jsou zálohované“ ve `Views.swift`); **doplnění** (`AppendPlanner`
  v OffloadKit, `MHL.swift` generace N + připsání do řetězu, `Report.swift` klíč `generation` a jména `_NNNN`,
  `offload append`); **název z klipů** (vzory `ReelNames`, další v řadě, zástupný text); **ověření zálohy** (čtení MHL
  všech generací a hashů, report `_verify_<stamp>Z`, menu); **kontrolu místa** (`SpaceEstimator`/`SpacePlanner`, tabulka
  toků, přepočet před proxy); **české hlášky** (`Msg` a `PreflightCode` v `Engine.swift` a `main.swift`).

- **Mimo plán — zbývá i po něm:**
```
`docs/VYVOJ-WINDOWS.md`, řádky 17–20 — najít:

```markdown
| 4 | WPF GUI, detekce a vysunutí karet, balíček | hotovo (čeká ruční test s kartou) |
| 5 | vestavěné LUTy Log → Rec.709 (ARRI, Sony, Blackmagic, RED, Canon, Panasonic, DJI, Fuji), automatický výběr podle metadat klipu | hotovo (čeká ruční test s klipy z kamer) |

## 2026-10-06 — balíček ZIP
```

nahradit:

```markdown
| 4 | WPF GUI, detekce a vysunutí karet, balíček | hotovo (čeká ruční test s kartou) |
| 5 | vestavěné LUTy Log → Rec.709 (ARRI, Sony, Blackmagic, RED, Canon, Panasonic, DJI, Fuji), automatický výběr podle metadat klipu | hotovo (čeká ruční test s klipy z kamer) |
| 6 | karty a bezpečnost dat: historie karet, doplnění nových klipů, název z klipů, ověření zálohy proti ASC MHL, kontrola místa, české hlášky (0.2.0) | hotovo (čeká ruční test s kartou) |

## 2026-10-06 — balíček ZIP
```

- [ ] **Step 4: README**

`windows/README.md`, řádky 18–21 — najít:

```markdown
| GUI `Offload.exe` | hotovo (čeká ruční test s kartou) |
| Vestavěné LUTy (plán 5) | hotovo (čeká ruční test s klipy z kamer) |

## Build a testy
```

nahradit:

```markdown
| GUI `Offload.exe` | hotovo (čeká ruční test s kartou) |
| Vestavěné LUTy (plán 5) | hotovo (čeká ruční test s klipy z kamer) |
| Karty a bezpečnost dat (plán 6, 0.2.0): historie, doplnění, název z klipů, ověření zálohy, místo, české hlášky | hotovo (čeká ruční test s kartou) |

## Build a testy
```

`windows/README.md`, řádky 100–104 — najít:

```markdown
  nebo tlačítko „Prověřit…“ v sekci Hardware. Okno Hardware ukazuje připojení, doporučení, měření rychlosti
  (s potvrzením) a USB porty s vlastními názvy. Přepínač `--no-hw-prompt` se stále přijímá, ale nic nedělá.
- **Soubory:** nastavení `%APPDATA%\Offload\settings.json`, log `%APPDATA%\Offload\app.log`, poslední test HW `hwtest.json`
  (složku lze přesměrovat proměnnou `OFFLOAD_SETTINGS_DIR`).

```

nahradit:

```markdown
  nebo tlačítko „Prověřit…“ v sekci Hardware. Okno Hardware ukazuje připojení, doporučení, měření rychlosti
  (s potvrzením) a USB porty s vlastními názvy. Přepínač `--no-hw-prompt` se stále přijímá, ale nic nedělá.
- **Soubory:** nastavení `%APPDATA%\Offload\settings.json`, historie offloadů `history.json` + `history\` (seznamy souborů),
  log `%APPDATA%\Offload\app.log`, poslední test HW `hwtest.json`
  (složku lze přesměrovat proměnnou `OFFLOAD_SETTINGS_DIR`).

```

`windows/README.md`, řádky 137–141 — najít:

````markdown
offload E:\ -d D:\Zaloha --proxy --lut sony-slog3-sgamut3   # vestavěný LUT podle id
offload luts                                                # seznam vestavěných LUTů (id — název)
```

`--lut auto|none|<id>|<cesta.cube>` (výchozí `auto`): `auto` vybere LUT podle kamery každého klipu, `none` žádný,
````

nahradit:

````markdown
offload E:\ -d D:\Zaloha --proxy --lut sony-slog3-sgamut3   # vestavěný LUT podle id
offload luts                                                # seznam vestavěných LUTů (id — název)
offload E:\ -d D:\Zaloha -d F:\Zaloha --name auto            # název složky z názvů klipů (A001C003… → A001)
offload append E:\                                          # doplnit jen nové klipy do dřívější zálohy (cíle z historie)
offload append E:\ --into D:\Zaloha\2026-10-06\A001 --into F:\Zaloha\2026-10-06\A001
offload verify D:\Zaloha\2026-10-06 --report                # ověřit všechny karty dne proti ASC MHL + report ověření
```

Historie (`%APPDATA%\Offload\history.json`): `copy` před plánem vypíše „Zálohováno … — ověřeno“, když kartu zná (bez
dotazu), a po kopii ji zapíše; `--no-history` čtení i zápis vypne. `append` bez `--into` najde složky zálohy v historii
(jinak „Karta není v historii — zadejte složky zálohy volbou --into.“); kód 0 = doplněno, nebo „Nic nového — záloha je
úplná.“ `verify` čte ASC MHL všech generací (bez `ascmhl\` seznam `_REPORT\*.xxh64`), řádky problémů zůstávají strojové
(`MISMATCH`, `MISSING`, `ERROR`, `EXTRA`, `FAILED-AT-COPY`, `CHAIN`), souhrn je česky; složka dne ověří každou kartu.
Chyby a souhrny CLI jsou česky, nápověda `--help` anglicky (nadpisy nápovědy a chyby parseru česky tam, kde je
System.CommandLine 2.0.12 přeložené).

`--lut auto|none|<id>|<cesta.cube>` (výchozí `auto`): `auto` vybere LUT podle kamery každého klipu, `none` žádný,
````

- [ ] **Step 5: Návod (kapitoly 3, 5, 8, 10, 11, 12) a PDF**

`windows/package/navod/navod.html`, řádky 316–320 — najít:

```html
<div class="win">
  <div class="bar"><span>Offload</span><span>— ☐ ✕</span></div>
  <div class="menu"><span class="callout">1</span><b>Nástroje</b> &nbsp;›&nbsp; Prověřit USB porty… &nbsp;Ctrl+U</div>
  <div class="body">
    <div class="left">
```

nahradit:

```html
<div class="win">
  <div class="bar"><span>Offload</span><span>— ☐ ✕</span></div>
  <div class="menu"><span class="callout">1</span><b>Nástroje</b> &nbsp;›&nbsp; Ověřit zálohu… &nbsp;Ctrl+Shift+O &nbsp;·&nbsp; Prověřit USB porty… &nbsp;Ctrl+U</div>
  <div class="body">
    <div class="left">
```

`windows/package/navod/navod.html`, řádky 396–401 — najít:

```html
  <li>Nabízejí se vyměnitelná média a disky připojené přes USB nebo SD. Systémový disk ani disk, na kterém je některý
    cíl, se jako karta nenabízí.</li>
  <li>U zaškrtnuté karty je pole <b class="ui">Název složky</b> — šedě je předvyplněná jmenovka svazku (u složky její název).
    Napište jiný název, pokud chcete; použije se jako jméno složky karty na cílech.</li>
  <li>Tlačítko <b>⟳</b> (<span class="q">„Znovu načíst připojené disky“</span>) načte disky znovu a zkontroluje cíle.</li>
  <li><b class="ui">Složka…</b> přidá libovolnou složku (kopii karty na disku, NAS, kartu ve čtečce, kterou aplikace sama nenabídla).
```

nahradit:

```html
  <li>Nabízejí se vyměnitelná média a disky připojené přes USB nebo SD. Systémový disk ani disk, na kterém je některý
    cíl, se jako karta nenabízí.</li>
  <li>Pod údaji karty je řádek z <b>historie offloadů</b>: šedě <span class="q">„Zjišťuji, jestli už je karta zálohovaná…“</span>,
    pak zeleně <span class="txt-ok">„Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha2 (F:) — ověřeno“</span>, oranžově
    s doplňkem <span class="txt-warn">„· nové soubory: 12 (3,2 GB)“</span>, <span class="txt-warn">„— NEOVĚŘENO“</span> nebo
    <span class="txt-warn">„…, ale karta se od té doby změnila (4 soubory jinak) — doplnit nejde, jen celá kopie“</span>,
    červeně <span class="txt-bad">„— s chybami (3 soubory)“</span>. Kartu, kterou Offload nikdy nekopíroval, nechá bez řádku.
    Kartu pozná podle sériového čísla souborového systému a kapacity, takže i v jiné čtečce; přeformátovaná karta je nová.
    Historie je jen v tomto počítači (<code>%APPDATA%\Offload\history.json</code>).</li>
  <li>U zaškrtnuté karty je pole <b class="ui">Název složky</b>. Šedě je předvyplněný návrh: cívka z názvů klipů
    (<code>A001C003_…</code> → <code>A001</code>, pod polem <span class="q">„podle názvů klipů (A001C001…)“</span>), u karty bez
    takových názvů <span class="q">„další v řadě po A001 (dnes)“</span> podle karet zkopírovaných do stejné složky data, jinak
    jmenovka svazku (u složky její název). Napsaný název má vždy přednost. Návrh vypne volba v kapitole 5.</li>
  <li>U karty, na které od zálohy přibyly klipy, je zaškrtávátko
    <b class="ui">Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)</b>. Zaškrtnuté místo názvu ukáže
    <span class="q">„Složka: 2026-10-06\A001 na 2 cílech“</span>: zkopírují se jen nové soubory do <b>stejné</b> složky
    karty na disky dřívější zálohy (sekce Cíle může být i prázdná). Podrobně v kapitolách 8 a 10.</li>
  <li>Tlačítko <b>⟳</b> (<span class="q">„Znovu načíst připojené disky“</span>) načte disky znovu a zkontroluje cíle.</li>
  <li><b class="ui">Složka…</b> přidá libovolnou složku (kopii karty na disku, NAS, kartu ve čtečce, kterou aplikace sama nenabídla).
```

`windows/package/navod/navod.html`, řádky 437–441 — najít:

```html
<b class="ui">Spustit offload</b> (<kbd>Ctrl</kbd>+<kbd>Enter</kbd>) je aktivní, když je vybraná aspoň jedna karta,
existuje aspoň jeden cíl, všechny cíle jsou připojené a nic dalšího nebrání startu (chybějící LUT, nedostupná složka
pro proxy, běžící měření rychlosti — důvod je vždy napsaný červeně nebo oranžově).</p>

<h3>Fronta</h3>
```

nahradit:

```html
<b class="ui">Spustit offload</b> (<kbd>Ctrl</kbd>+<kbd>Enter</kbd>) je aktivní, když je vybraná aspoň jedna karta,
existuje aspoň jeden cíl, všechny cíle jsou připojené a nic dalšího nebrání startu (chybějící LUT, nedostupná složka
pro proxy, běžící měření rychlosti, nedostatek místa — důvod je vždy napsaný červeně nebo oranžově).
Pod lištou se ukáže červeně <span class="txt-bad">„Na cíli Zaloha (D:) není dost místa: potřeba 412 GB (karty 380 GB,
proxy ~30 GB, report 2 GB), volno 398 GB.“</span> (start nejde) nebo oranžově <span class="txt-warn">„Na cíli Zaloha (D:)
zbude po offloadu jen 6 GB — rezerva pod 5 %, odhad proxy je přibližný.“</span> (start jde). Počítají se vybrané karty,
úlohy ve frontě i běžící úlohy, cíle na stejném disku a složka pro proxy dohromady. Když karta ještě není zjištěná,
Start na ni počká nejvýš 10 sekund.</p>

<h3>Fronta</h3>
```

`windows/package/navod/navod.html`, řádky 499–502 — najít:

```html
      oranžové <span class="txt-warn">„Bez ověření není jisté, že data na disku jsou v pořádku.“</span>, report má štítek
      <b>ZKOPÍROVÁNO — NEOVĚŘENO</b> a zelený pruh „kartu lze vysunout“ se <b>neukáže</b>.</td></tr>
  <tr><td><b class="ui">Analýza klipů a náhledy v reportu</b></td><td>zapnuto</td>
      <td>Po kopii se u každého klipu zjistí formát, kodek, rozlišení, snímková frekvence, délka, timecode a zvukové stopy
```

nahradit:

```html
      oranžové <span class="txt-warn">„Bez ověření není jisté, že data na disku jsou v pořádku.“</span>, report má štítek
      <b>ZKOPÍROVÁNO — NEOVĚŘENO</b> a zelený pruh „kartu lze vysunout“ se <b>neukáže</b>.</td></tr>
  <tr><td><b class="ui">Navrhovat název karty podle klipů</b></td><td>zapnuto</td>
      <td>Šedý návrh v poli <b class="ui">Název složky</b> z názvů klipů (ARRI, Sony Venice, Canon Cinema EOS, RED,
      Blackmagic) nebo „další v řadě“ (kapitola 3). Vypnuto = jmenovka svazku jako dřív.</td></tr>
  <tr><td><b class="ui">Analýza klipů a náhledy v reportu</b></td><td>zapnuto</td>
      <td>Po kopii se u každého klipu zjistí formát, kodek, rozlišení, snímková frekvence, délka, timecode a zvukové stopy
```

`windows/package/navod/navod.html`, řádky 521–530 — najít:

```html
<h3>Co se kontroluje před kopií</h3>
<p>Než se cokoli zapíše, Offload ověří následující. Když něco nesedí, úloha skončí stavem <span class="pill bad">Chyba</span>
a hláškou na kartě úlohy (hlášky jsou zatím anglicky, význam je v kapitole 12):</p>
<ul>
  <li>zdroj existuje a obsahuje soubory; cíle existují a dá se na ně zapisovat;</li>
  <li>cíl není na stejném svazku jako karta a dva cíle nejsou na stejném svazku (to by nebyla druhá kopie);</li>
  <li>zdroj není sám dřívější offload (nemá v kořeni <code>_REPORT</code>, <code>_PROXY</code> ani <code>ascmhl</code>);</li>
  <li>na každém cíli je dost místa;</li>
  <li>na kartě nejsou názvy, které Windows neumí uložit (znaky <code>&lt; &gt; : " | ? *</code>, jména jako <code>CON</code>,
    <code>NUL</code>, tečka nebo mezera na konci), ani dva soubory lišící se jen velikostí písmen;</li>
```

nahradit:

```html
<h3>Co se kontroluje před kopií</h3>
<p>Než se cokoli zapíše, Offload ověří následující. Když něco nesedí, úloha skončí stavem <span class="pill bad">Chyba</span>
a českou hláškou na kartě úlohy (význam a řešení v kapitole 12):</p>
<ul>
  <li>zdroj existuje a obsahuje soubory; cíle existují a dá se na ně zapisovat;</li>
  <li>cíl není na stejném svazku jako karta a dva cíle nejsou na stejném svazku (to by nebyla druhá kopie);</li>
  <li>zdroj není sám dřívější offload (nemá v kořeni <code>_REPORT</code>, <code>_PROXY</code> ani <code>ascmhl</code>);</li>
  <li>na každém disku cílů je dost místa pro kartu, proxy a report — sčítají se cíle na stejném disku, složka pro
    proxy a karty, které už čekají ve frontě; proxy se odhadují z velikosti klipů a před renderem proxy se místo
    přepočítá podle skutečné délky (když se nevejdou, proxy se přeskočí a kopie zůstane ověřená);</li>
  <li>na kartě nejsou názvy, které Windows neumí uložit (znaky <code>&lt; &gt; : " | ? *</code>, jména jako <code>CON</code>,
    <code>NUL</code>, tečka nebo mezera na konci), ani dva soubory lišící se jen velikostí písmen;</li>
```

`windows/package/navod/navod.html`, řádky 732–735 — najít:

```html
<p>Po skončení každé úlohy tlačítko Offloadu v hlavním panelu Windows zabliká; při chybě zazní chybový zvuk.</p>

<h3>Zrušení</h3>
<p>Tlačítko <b class="ui">Zrušit</b> je na kartě úlohy, dokud úloha čeká nebo běží. Rozpracované soubory
```

nahradit:

```html
<p>Po skončení každé úlohy tlačítko Offloadu v hlavním panelu Windows zabliká; při chybě zazní chybový zvuk.</p>

<h3>Karty už jsou zálohované</h3>
<p>Když spouštíte celou kopii karty, kterou historie zná, Offload se zeptá v okně <b>Karty už jsou zálohované</b>
(u jedné karty <b>Karta už je zálohovaná</b>) se seznamem záloh a složkou, která by vznikla (<code>A001_2</code>):
<b class="ui">Kopírovat znovu celé</b>, <b class="ui">Vynechat zálohované</b> (spustí jen ostatní karty) nebo
<b class="ui">Zrušit</b> (nespustí nic). Karty se zaškrtnutým doplněním se neptají.</p>

<h3>Doplnění nových klipů</h3>
<p>Úloha doplnění má název <span class="q">„A001 · doplnění“</span> a cíle dřívější zálohy. Zkopíruje jen nové soubory,
nic nepřepíše a zapíše novou generaci reportu a ASC MHL (kapitola 10). Když není co kopírovat, skončí
<span class="pill ok">Hotovo</span> s textem <span class="q">„Nic nového — záloha je úplná.“</span> Oranžově může upozornit
<span class="txt-warn">„5 souborů z dřívější zálohy nebylo ověřeno — doporučeno Ověřit zálohu.“</span> Před kopírováním
doplnění odmítne (nic nezapíše), když dřívější záloha není dostupná, chybí její report nebo ASC MHL, cíle se liší nebo je
na kartě soubor stejného jména s jinou velikostí či datem — pak udělejte celou kopii (kapitola 12).</p>

<h3>Ověření zálohy</h3>
<p><b class="ui">Nástroje → Ověřit zálohu…</b> (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd>) vybere složku karty na cíli,
nebo celou složku dne (ověří se každá karta zvlášť). Každé ověření je položka fronty <span class="q">„Ověření A001“</span>
a běží po kopiích, jedno po druhém (zatěžuje stejné disky): <span class="q">„Ověřuji 120 / 1 234 souborů · 45 GB / 512 GB
· 380 MB/s“</span>. Výsledek zeleně <span class="txt-ok">„Ověřeno: všech 1 234 souborů sedí (ASC MHL, 2 generace,
xxHash64).“</span>, červeně <span class="txt-bad">„Nesedí 2 soubory, chybí 1, nečitelný 0 — podrobnosti v reportu
ověření.“</span>, oranžově soubory navíc, soubory, které selhaly už při kopii, a změněné generace MHL. Tlačítka
<b class="ui">Report ověření</b> a <b class="ui">Průzkumník</b>; <b class="ui">Zrušit</b> ověření ukončí bez reportu.
Report ověření se ukládá do <code>_REPORT</code> karty, dokud je v menu zaškrtnuté <b class="ui">Ukládat report ověření</b>.
Ověřují se všechny generace ASC MHL a všechny jeho kontrolní součty (xxHash64, MD5, SHA-1, xxHash3, xxHash128, C4) —
i zálohy z jiných programů; bez <code>ascmhl</code> se ověřuje proti <code>_REPORT\&lt;karta&gt;.xxh64</code>.</p>

<h3>Zrušení</h3>
<p>Tlačítko <b class="ui">Zrušit</b> je na kartě úlohy, dokud úloha čeká nebo běží. Rozpracované soubory
```

`windows/package/navod/navod.html`, řádky 866–872 — najít:

```html
      │  ├─ A001_report.html                 <span class="c">← tlačítko Report</span>
      │  ├─ A001_report.pdf
      │  └─ A001.xxh64                       <span class="c">← kontrolní součty</span>
      ├─ ascmhl\
      │  ├─ 0001_A001_2026-10-06_081500Z.mhl
      │  └─ ascmhl_chain.xml
      └─ _PROXY\                             <span class="c">← jen se zapnutými proxy</span>
```

nahradit:

```html
      │  ├─ A001_report.html                 <span class="c">← tlačítko Report</span>
      │  ├─ A001_report.pdf
      │  ├─ A001.xxh64                       <span class="c">← kontrolní součty (po doplnění i nové soubory)</span>
      │  ├─ offload_report_0002.json         <span class="c">← jen po doplnění: report generace 2</span>
      │  ├─ A001_report_0002.html
      │  └─ A001_verify_2026-10-07_101500Z.html  <span class="c">← jen po Ověřit zálohu (+ .pdf, offload_verify_….json)</span>
      ├─ ascmhl\
      │  ├─ 0001_A001_2026-10-06_081500Z.mhl
      │  ├─ 0002_A001_2026-10-06_143000Z.mhl <span class="c">← jen po doplnění: jen nové soubory</span>
      │  └─ ascmhl_chain.xml
      └─ _PROXY\                             <span class="c">← jen se zapnutými proxy</span>
```

`windows/package/navod/navod.html`, řádky 891–899 — najít:

```html
  <li>Složky <code>_REPORT</code>, <code>_PROXY</code> a <code>ascmhl</code> nejsou součástí karty a do MHL se nepočítají.</li>
  <li>Formát je stejný jako u Mac verze Offloadu — kopii lze ověřit na kterékoli z nich.</li>
</ul>

<div class="box tip"><span class="t">Ověření kopie později</span>
Hotovou kopii můžete kdykoli znovu ověřit proti seznamu součtů v <code>_REPORT</code>:
<code>bin\offload.exe verify D:\Zaloha\2026-10-06\A001</code> (kapitola 11).</div>
</section>

```

nahradit:

```html
  <li>Složky <code>_REPORT</code>, <code>_PROXY</code> a <code>ascmhl</code> nejsou součástí karty a do MHL se nepočítají.</li>
  <li>Formát je stejný jako u Mac verze Offloadu — kopii lze ověřit na kterékoli z nich.</li>
  <li>Doplnění zapíše do složky karty jen nové soubory a ke stávajícím reportům přidá report s číslem generace
    (<code>_0002</code>); původní reporty se nikdy nepřepíšou. Mac verze generace nečte, ale doplněné soubory ověří ze
    seznamu <code>A001.xxh64</code>.</li>
  <li>Report ověření jde jen do <code>_REPORT</code>, do <code>ascmhl</code> se nikdy nezapisuje.</li>
</ul>

<div class="box tip"><span class="t">Ověření kopie později</span>
Hotovou kopii můžete kdykoli znovu ověřit proti ASC MHL: <b class="ui">Nástroje → Ověřit zálohu…</b> (kapitola 8),
nebo <code>bin\offload.exe verify D:\Zaloha\2026-10-06\A001</code> (kapitola 11).</div>
</section>

```

`windows/package/navod/navod.html`, řádky 903–908 — najít:

```html

<p>Příkazová řádka <code>bin\offload.exe</code> dělá totéž co aplikace a zapisuje stejné výstupy. Hodí se pro skripty
a dávkové zpracování. Spusťte ji z PowerShellu nebo příkazového řádku ve složce Offloadu. Nápověda a výpisy jsou
anglicky.</p>
<pre class="cmd">bin\offload.exe --help
bin\offload.exe copy --help</pre>
```

nahradit:

```html

<p>Příkazová řádka <code>bin\offload.exe</code> dělá totéž co aplikace a zapisuje stejné výstupy. Hodí se pro skripty
a dávkové zpracování. Spusťte ji z PowerShellu nebo příkazového řádku ve složce Offloadu. Nápověda a výpisy průběhu
jsou anglicky, chyby a souhrny česky.</p>
<pre class="cmd">bin\offload.exe --help
bin\offload.exe copy --help</pre>
```

`windows/package/navod/navod.html`, řádky 912–916 — najít:

```html
  <thead><tr><th class="w20">Příkaz</th><th>K čemu</th></tr></thead>
  <tr><td><code>copy</code></td><td>zkopíruje kartu na všechny cíle do <code>&lt;cíl&gt;\&lt;datum&gt;\&lt;karta&gt;\</code> a ověří. Je výchozí — slovo <code>copy</code> lze vynechat.</td></tr>
  <tr><td><code>verify</code></td><td>znovu ověří hotovou složku karty proti seznamu součtů v <code>_REPORT</code></td></tr>
  <tr><td><code>bench</code></td><td>změří rychlost čtení karty a zápisu/čtení disků</td></tr>
  <tr><td><code>ports</code></td><td>USB porty, kde a jak rychle jsou karty a disky připojené, doporučení (jako okno Hardware)</td></tr>
```

nahradit:

```html
  <thead><tr><th class="w20">Příkaz</th><th>K čemu</th></tr></thead>
  <tr><td><code>copy</code></td><td>zkopíruje kartu na všechny cíle do <code>&lt;cíl&gt;\&lt;datum&gt;\&lt;karta&gt;\</code> a ověří. Je výchozí — slovo <code>copy</code> lze vynechat.</td></tr>
  <tr><td><code>append</code></td><td>doplní jen nové soubory karty do dřívější zálohy (stejné složky karty, další generace ASC MHL)</td></tr>
  <tr><td><code>verify</code></td><td>znovu ověří složku karty (nebo všechny karty složky dne) proti ASC MHL všech generací, bez <code>ascmhl</code> proti seznamu součtů v <code>_REPORT</code>; <code>--report</code> uloží report ověření</td></tr>
  <tr><td><code>bench</code></td><td>změří rychlost čtení karty a zápisu/čtení disků</td></tr>
  <tr><td><code>ports</code></td><td>USB porty, kde a jak rychle jsou karty a disky připojené, doporučení (jako okno Hardware)</td></tr>
```

`windows/package/navod/navod.html`, řádky 924–928 — najít:

```html
offload E:\ -d D:\Zaloha --proxy --lut sony-slog3-sgamut3   # vestavěný LUT podle id
offload E:\ -d D:\Zaloha --proxy --lut look.cube --frame-aspect 1.85   # vlastní LUT a výřez
offload verify D:\Zaloha\2026-10-06\A001              # znovu ověřit hotovou kopii
offload bench --read E:\ --write D:\ --gb 2           # rychlost karty a disku
offload ports                                         # porty a doporučení
```

nahradit:

```html
offload E:\ -d D:\Zaloha --proxy --lut sony-slog3-sgamut3   # vestavěný LUT podle id
offload E:\ -d D:\Zaloha --proxy --lut look.cube --frame-aspect 1.85   # vlastní LUT a výřez
offload E:\ -d D:\Zaloha -d F:\Zaloha --name auto     # název složky z názvů klipů (A001C003… → A001)
offload append E:\                                     # doplnit nové klipy do dřívější zálohy (cíle z historie)
offload append E:\ --into D:\Zaloha\2026-10-06\A001    # … nebo do zadané složky karty (opakovat pro každý cíl)
offload verify D:\Zaloha\2026-10-06\A001              # znovu ověřit hotovou kopii
offload verify D:\Zaloha\2026-10-06 --report           # ověřit všechny karty dne a uložit reporty ověření
offload bench --read E:\ --write D:\ --gb 2           # rychlost karty a disku
offload ports                                         # porty a doporučení
```

`windows/package/navod/navod.html`, řádky 934–939 — najít:

```html
<table>
  <thead><tr><th class="w30">Volba</th><th>Význam</th></tr></thead>
  <tr><td class="k"><code>-d</code>, <code>--dest</code></td><td>cílová složka; zopakujte pro každý disk (povinné)</td></tr>
  <tr><td class="k"><code>--name</code></td><td>název složky karty (výchozí jmenovka svazku)</td></tr>
  <tr><td class="k"><code>--date</code></td><td>složka s datem (výchozí dnes, <code>RRRR-MM-DD</code>)</td></tr>
  <tr><td class="k"><code>--no-verify</code></td><td>bez ověření zpětným čtením (nedoporučeno)</td></tr>
```

nahradit:

```html
<table>
  <thead><tr><th class="w30">Volba</th><th>Význam</th></tr></thead>
  <tr><td class="k"><code>-d</code>, <code>--dest</code></td><td>cílová složka; zopakujte pro každý disk (bez ní <span class="q">„Chyba: Není zadaný žádný cíl.“</span>)</td></tr>
  <tr><td class="k"><code>--name</code></td><td>název složky karty (výchozí jmenovka svazku); <code>auto</code> = cívka z názvů klipů, jinak jmenovka</td></tr>
  <tr><td class="k"><code>--no-history</code></td><td>historii offloadů nečíst ani nezapsat</td></tr>
  <tr><td class="k"><code>--date</code></td><td>složka s datem (výchozí dnes, <code>RRRR-MM-DD</code>)</td></tr>
  <tr><td class="k"><code>--no-verify</code></td><td>bez ověření zpětným čtením (nedoporučeno)</td></tr>
```

`windows/package/navod/navod.html`, řádky 952–955 — najít:

```html
</table>

<h3>Během kopie a po ní</h3>
<ul>
```

nahradit:

```html
</table>

<h3>Volby příkazu append</h3>
<p><code>offload append &lt;karta&gt;</code> má stejné volby analýzy a proxy jako <code>copy</code> (<code>--no-analyze</code>,
<code>--proxy</code>, <code>--lut</code> …) a navíc <code>--into &lt;složka karty&gt;</code> (zopakujte pro každý cíl dřívější
zálohy; bez ní se složky najdou v historii, jinak <span class="q">„Karta není v historii — zadejte složky zálohy volbou
--into.“</span>), <code>--no-verify</code>, <code>--dry-run</code>, <code>--no-history</code>. Kód <code>0</code> = doplněno
(nebo <span class="q">„Nic nového — záloha je úplná.“</span>), <code>1</code> = chyba.</p>

<h3>Během kopie a po ní</h3>
<ul>
```

`windows/package/navod/navod.html`, řádky 958–963 — najít:

```html
  <li><kbd>Ctrl</kbd>+<kbd>C</kbd> kopii zruší: dokončí aktuální blok a smaže rozpracované soubory.</li>
  <li>Návratový kód: <code>0</code> = vše v pořádku, <code>1</code> = chyba (vhodné pro skripty).</li>
  <li><code>verify</code> vypíše u problémových souborů <code>MISMATCH</code> (jiný součet), <code>MISSING</code> (chybí),
    <code>ERROR</code> (nečitelný), <code>EXTRA</code> (není v seznamu) a na konci <code>OK</code> nebo <code>FAILED</code>.</li>
</ul>
</section>
```

nahradit:

```html
  <li><kbd>Ctrl</kbd>+<kbd>C</kbd> kopii zruší: dokončí aktuální blok a smaže rozpracované soubory.</li>
  <li>Návratový kód: <code>0</code> = vše v pořádku, <code>1</code> = chyba (vhodné pro skripty).</li>
  <li><code>copy</code> u karty, kterou historie zná, vypíše před plánem <code>Zálohováno 6. 10. 2026 8:15 na D:\Zaloha,
    F:\Zaloha — ověřeno</code> (bez dotazu).</li>
  <li><code>verify</code> vypíše u problémových souborů <code>MISMATCH</code> (jiný součet), <code>MISSING</code> (chybí),
    <code>ERROR</code> (nečitelný), <code>EXTRA</code> (není v záznamu), <code>FAILED-AT-COPY</code> (selhal už při kopii),
    <code>CHAIN</code> (problém řetězu ASC MHL) a na konci český souhrn: <span class="q">„Ověřeno 1 234 souborů (512 GB) za 22:10
    (385 MB/s) — vše v pořádku.“</span> nebo <span class="q">„… — NALEZENY PROBLÉMY: nesedí 2, chybí 1, nečitelné 0, navíc 3.“</span></li>
</ul>
</section>
```

`windows/package/navod/navod.html`, řádky 984–992 — najít:

```html
  <tr><td><span class="txt-bad">Disk není připojen</span> u cíle, Spustit offload je šedé</td><td>Připojte disk; do pár sekund se řádek opraví sám (nebo klikněte na ⟳). Cíl, který nepotřebujete, odeberte křížkem.</td></tr>
  <tr><td><span class="txt-warn">Doporučeno NTFS — spolehlivější než exFAT/FAT32</span></td><td>Jen doporučení. Pro offload na Windows je spolehlivější NTFS. Na FAT32 se navíc nevejde soubor 4 GB a víc.</td></tr>
  <tr><td>„… cannot store files of 4 GB or more (…)“</td><td>Cíl je FAT32 a na kartě je soubor 4 GB a větší. Použijte disk s NTFS nebo exFAT.</td></tr>
  <tr><td>„Not enough space on …: need …, free …“</td><td>Na cíli není dost místa. Uvolněte místo nebo použijte jiný disk.</td></tr>
  <tr><td>„Destination … is on the same volume as the source“, „Destinations share a volume …“</td><td>Cíl je na kartě samotné, nebo dva cíle jsou na stejném disku. Druhá kopie musí být na jiném disku.</td></tr>
  <tr><td>„Source looks like an earlier offload (has _REPORT/_PROXY/ascmhl) …“</td><td>Vybraná složka je už hotový offload. Vyberte původní kartu, nebo (u kopie kopie) složku bez <code>_REPORT</code>, <code>_PROXY</code> a <code>ascmhl</code>.</td></tr>
  <tr><td>„Names Windows cannot store: …“</td><td>Na kartě jsou názvy, které Windows neuloží (zakázané znaky, jména jako <code>CON</code>, tečka nebo mezera na konci, nebo dva názvy lišící se jen velikostí písmen). Hláška vypíše, kterých souborů se to týká.</td></tr>
  <tr><td>„N souborů selhalo — karta NENÍ bezpečně zálohovaná.“</td><td>Otevřete <b class="ui">Report</b>, v části Chyby je u každého souboru důvod (karta nebo konkrétní cíl). Zkontrolujte kartu, čtečku a disky a kartu offloadujte znovu.</td></tr>
  <tr><td>„Report nenalezen na žádném cíli: …“</td><td>Žádný cíl s reportem není připojený. Připojte cílový disk.</td></tr>
```

nahradit:

```html
  <tr><td><span class="txt-bad">Disk není připojen</span> u cíle, Spustit offload je šedé</td><td>Připojte disk; do pár sekund se řádek opraví sám (nebo klikněte na ⟳). Cíl, který nepotřebujete, odeberte křížkem.</td></tr>
  <tr><td><span class="txt-warn">Doporučeno NTFS — spolehlivější než exFAT/FAT32</span></td><td>Jen doporučení. Pro offload na Windows je spolehlivější NTFS. Na FAT32 se navíc nevejde soubor 4 GB a víc.</td></tr>
  <tr><td>„…: FAT32 neumí uložit soubory od 4 GB (…)“</td><td>Cíl je FAT32 a na kartě je soubor 4 GB a větší. Použijte disk s NTFS nebo exFAT.</td></tr>
  <tr><td>„Na cíli … není dost místa: potřeba … (karty …, proxy ~…, report …), volno …“</td><td>Na disku není dost místa pro kartu, proxy a report (i s kartami ve frontě). Uvolněte místo, použijte jiný disk, nebo vypněte proxy.</td></tr>
  <tr><td><span class="txt-warn">„Na cíli … zbude po offloadu jen … — rezerva pod 5 %, odhad proxy je přibližný.“</span></td><td>Jen varování, offload jde spustit. Odhad proxy může být vyšší i nižší než skutečnost.</td></tr>
  <tr><td>„Proxy se nevyrenderovaly: na … chybí místo (potřeba ~…, volno …).“ (na kartě úlohy)</td><td>Kopie je hotová a ověřená, jen proxy se nevešly. Uvolněte místo a proxy vyrobte znovu (nová kopie s proxy, nebo jiný nástroj).</td></tr>
  <tr><td>„Cíl … je na stejném disku jako zdroj.“, „Dva cíle jsou na stejném disku (…) — to není druhá kopie. …“</td><td>Cíl je na kartě samotné, nebo dva cíle jsou na stejném disku. Druhá kopie musí být na jiném disku.</td></tr>
  <tr><td>„Zdroj vypadá jako dřívější offload (má _REPORT, _PROXY nebo ascmhl) …“</td><td>Vybraná složka je už hotový offload. Vyberte původní kartu, nebo (u kopie kopie) složku bez <code>_REPORT</code>, <code>_PROXY</code> a <code>ascmhl</code>.</td></tr>
  <tr><td>„Některé názvy Windows neumí uložit: …“</td><td>Na kartě jsou názvy, které Windows neuloží (zakázané znaky, jména jako <code>CON</code>, tečka nebo mezera na konci, nebo dva názvy lišící se jen velikostí písmen). Hláška vypíše, kterých souborů se to týká.</td></tr>
  <tr><td>„Dřívější záloha není dostupná: … — připojte disk, nebo udělejte celou kopii.“</td><td>Doplnění potřebuje všechny disky dřívější zálohy. Připojte je, nebo kartu zkopírujte celou (vznikne nová složka).</td></tr>
  <tr><td>„V … chybí report dřívější zálohy …“, „V … chybí ASC MHL …“</td><td>Složka zálohy není úplná (kopie z jiného nástroje bez MHL, smazaný report). Udělejte celou kopii.</td></tr>
  <tr><td>„Zálohy na cílech se liší (…) — zkontrolujte je přes Nástroje → Ověřit zálohu…; doplnit nejde.“</td><td>Zálohy na discích nejsou stejné (jedna byla doplněna bez druhé, kopie se přerušila). Ověřte obě zálohy; pak udělejte celou kopii.</td></tr>
  <tr><td>„Doplnění zastaveno — nic se nezkopírovalo. N souborů na kartě má stejné jméno jako v záloze, ale jinou velikost nebo datum: …“</td><td>Na kartě se změnily už zálohované soubory (jiná karta se stejným sériovým číslem, úprava v kameře). Udělejte celou kopii.</td></tr>
  <tr><td>„Na …: chybí soubor z dřívější zálohy: …“, „… se při dřívější záloze na … nezkopíroval správně …“</td><td>Dřívější záloha je neúplná. Ověřte ji (Nástroje → Ověřit zálohu…) a kartu zkopírujte celou.</td></tr>
  <tr><td>„Nic nového — záloha je úplná.“</td><td>Není chyba — všechny soubory karty už v záloze jsou.</td></tr>
  <tr><td>„Ve složce … není ASC MHL ani seznam součtů — není s čím porovnat.“</td><td>Vybraná složka není složka karty ani dne z Offloadu (nebo v ní chybí <code>ascmhl</code> i <code>_REPORT</code>). Vyberte složku karty <code>&lt;cíl&gt;\&lt;datum&gt;\&lt;karta&gt;</code>.</td></tr>
  <tr><td><span class="txt-warn">„ASC MHL generace 0002 neodpovídá řetězu (soubor MHL byl změněn).“</span> (ověření)</td><td>Soubor MHL po zápisu někdo změnil. Soubory se ověřily i tak; zálohu zkontrolujte, případně udělejte novou kopii.</td></tr>
  <tr><td>„N souborů selhalo — karta NENÍ bezpečně zálohovaná.“</td><td>Otevřete <b class="ui">Report</b>, v části Chyby je u každého souboru důvod (karta nebo konkrétní cíl). Zkontrolujte kartu, čtečku a disky a kartu offloadujte znovu.</td></tr>
  <tr><td>„Report nenalezen na žádném cíli: …“</td><td>Žádný cíl s reportem není připojený. Připojte cílový disk.</td></tr>
```

`windows/package/navod/navod.html`, řádky 1029–1033 — najít:

```html
<p>Ne, ale aplikace s jedním cílem upozorní, že doporučené jsou dvě nezávislé kopie.</p>
<h4>Jak ověřím zálohu později, třeba po převozu disku?</h4>
<p><code>bin\offload.exe verify &lt;cíl&gt;\&lt;datum&gt;\&lt;karta&gt;</code>. Nástroj <code>ascmhl verify</code> na Windows hlásí falešné
„new/missing file“ kvůli lomítkům v cestách — plné <code>ascmhl verify</code> pouštějte na macOS nebo Linuxu, na Windows použijte <code>offload verify</code>.</p>
<h4>Ověří kopii z Windows i Mac verze Offloadu?</h4>
```

nahradit:

```html
<p>Ne, ale aplikace s jedním cílem upozorní, že doporučené jsou dvě nezávislé kopie.</p>
<h4>Jak ověřím zálohu později, třeba po převozu disku?</h4>
<p><b class="ui">Nástroje → Ověřit zálohu…</b>, nebo <code>bin\offload.exe verify &lt;cíl&gt;\&lt;datum&gt;\&lt;karta&gt;</code>. Nástroj <code>ascmhl verify</code> na Windows hlásí falešné
„new/missing file“ kvůli lomítkům v cestách — plné <code>ascmhl verify</code> pouštějte na macOS nebo Linuxu, na Windows použijte <code>offload verify</code>.</p>
<h4>Ověří kopii z Windows i Mac verze Offloadu?</h4>
```

Run: `powershell -ExecutionPolicy Bypass -File windows\package\navod\render.ps1` → `printed …\windows\package\Offload-navod.pdf (… KB)` (tiskne Edge, ne Offload). Otevřít PDF a zkontrolovat, že nové odstavce nepřetekly stránku (tabulky kapitoly 12 se smí zalomit).

- [ ] **Step 6: Celá sada + commit**

Run: `dotnet build` bez varování; `dotnet test` → zelená.

Řádek:

```markdown
- Úkol 18 — dokumentace: FORMATY (doplnění, report ověření, settings `naming`, místní `history.json`), ROZDILY, VYVOJ-MAC (co převezme Mac), README (příkazy `append`, `verify --report`, `--name auto`, `--no-history`), návod kapitoly 3, 5, 8, 10, 11, 12 (hlášky česky, bez věty „zatím anglicky“), verze 0.2.0
```

```powershell
git add windows docs
git commit -m "Docs: plan 6 (cards and data safety), version 0.2.0" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 19: Závěrečná revize + seznam ručního testu

Spec §6.1 úkol 19 (karta ve dvou čtečkách, DST posun na FAT32, `ascmhl verify` po doplnění).

**Files:**
- Modify: `windows/README.md` (řádky 125–128)

- [ ] **Step 1: Seznam ručního testu do README**

`windows/README.md`, řádky 125–128 — najít:

```markdown
- [ ] 10. BRAW klip: proxy, tři snímky v reportu, LUT podle gamy v reportu (viz „Vzorek BRAW“ výše)

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

```

nahradit:

```markdown
- [ ] 10. BRAW klip: proxy, tři snímky v reportu, LUT podle gamy v reportu (viz „Vzorek BRAW“ výše)

Plán 6 (karty a bezpečnost dat, 0.2.0):

- [ ] 11. Zkopírovanou kartu vytáhnout a vložit znovu → pod kartou šedě „Zjišťuji…“, pak zeleně „Zálohováno <datum čas> na
      <cíle> — ověřeno“; Start → okno „Karta už je zálohovaná“: „Zrušit“ nic nespustí, „Vynechat zálohované“ kartu odškrtne,
      „Kopírovat znovu celé“ vytvoří `<karta>_2`
- [ ] 12. **Karta ve dvou čtečkách:** tutéž kartu vložit do jiné čtečky (jiné písmeno, jiný GUID svazku) → stále „Zálohováno …“
      (párování podle sériového čísla FS + kapacity, R1); přeformátovaná karta → žádný řádek
- [ ] 13. Rychlá výměna dvou karet ve stejné čtečce během „Zjišťuji…“ → řádek patří vždy kartě, která je ve čtečce
      (výsledek staré karty se zahodí), napsaný název zmizí
- [ ] 14. Kamerou natočit na zálohovanou kartu další klipy → oranžově „· nové soubory: N (… GB)“ a zaškrtávátko
      „Doplnit jen nové klipy…“; zaškrtnout → místo názvu „Složka: <datum>\<karta> na 2 cílech“, Start jde i s prázdnými Cíli;
      úloha „<karta> · doplnění“ → na obou cílech `offload_report_0002.*`, `ascmhl\0002_…mhl`, `.xxh64` delší, původní
      `offload_report.json`/HTML beze změny (datum změny souboru); znovu vložit → „Zálohováno … — ověřeno“ bez nových souborů
- [ ] 15. **`ascmhl verify` po doplnění** (na macOS/Linuxu, kapitola „ASC MHL a ascmhl na Windows“): `ascmhl verify
      <cíl>/<datum>/<karta>` projde bez chyb přes obě generace; na Windows totéž `offload verify` → „… — vše v pořádku.“
- [ ] 16. **Posun času na FAT32:** kartu naformátovanou ve FAT32 (např. malá SD) zkopírovat, pak ve Windows změnit časové
      pásmo o hodinu (Nastavení → Čas a jazyk; simuluje přechod letní/zimní čas) a kartu vložit znovu → stále „Zálohováno …
      — ověřeno“ (ne „karta se změnila“) a doplnění nového souboru projde bez konfliktu; pásmo vrátit
- [ ] 17. Název z klipů: karta ARRI/Venice/RED/Blackmagic → šedě „A001“ + „podle názvů klipů (A001C001…)“; karta Sony
      XAVC (`C0001`) po kartě `A001` téhož dne → „A002“ + „další v řadě po A001 (dnes)“; volba „Navrhovat název karty
      podle klipů“ vypnutá → jmenovka svazku; CLI `offload E:\ -d D:\Zaloha --name auto --dry-run` vypíše stejný název
- [ ] 18. Nástroje → Ověřit zálohu… (Ctrl+Shift+O) na složku karty i na složku dne → položky „Ověření …“ ve frontě po
      kopiích, „Ověřeno: všech N souborů sedí (ASC MHL, …)“, tlačítko „Report ověření“ otevře `_REPORT\<karta>_verify_…html`;
      ve zkušební kopii změnit bajt souboru → červeně „Nesedí 1 soubor …“; během ověření odpojit disk → nečitelné soubory,
      „disk se možná odpojil“; „Zrušit“ → „Ověření zrušeno po …“ bez reportu
- [ ] 19. Místo: cíl s málo místem (např. malý USB disk) → červeně „Na cíli … není dost místa: potřeba … (karty …, proxy ~…,
      report …), volno …“ a Start šedý; dvě karty na tentýž disk se sečtou; zapnout proxy → potřeba vzroste; skoro plný disk
      → oranžově „… zbude po offloadu jen … — rezerva pod 5 % …“
- [ ] 20. Hlášky česky: odpojený cíl při startu z CLI (`offload E:\ -d X:\nic`) → „Chyba: Cíl nenalezen: … — je disk
      připojený?“; v GUI chyba před kopií na kartě úlohy česky

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

```

- [ ] **Step 2: Revize proti návrhu**

Projít §2.1–2.6 a §5 návrhu bod po bodu proti kódu (tabulka „Okrajové případy“ každé funkce) a zkontrolovat:

- `rg -n "OffloadError.Preflight\(" windows/src` — každé volání má `PreflightCode` a text z `Msg` (žádný anglický literál);
- `rg -n "\"(Error|Note|Cancelling|Unknown|No |Not enough|Destination|Source )" windows/src/Offload.Cli windows/src/Offload.Core --glob "*.cs"` — žádná anglická hláška pro uživatele (logy `log?.Invoke(...)`, TXT report a `Description` voleb anglicky zůstávají);
- `rg -n "Task.Run|ContinueWith" windows/src/Offload.App/MainViewModel.History.cs windows/src/Offload.App/MainViewModel.Space.cs windows/src/Offload.App/MainViewModel.Verify.cs` — žádné čtení disku mimo `Task.Run` (UI vlákno jen paměť);
- `rg -n "File\.(Write|Create|Delete|Move)|NativeIO\.(Write|Move|Mkdirs|Delete)" windows/src/Offload.Core/History windows/src/Offload.Core/Append windows/src/Offload.Core/Verification` — zápisy jen do `%APPDATA%\Offload` a do `_REPORT` cíle, nikdy do zdroje (karty);
- `git diff 9e4026d --stat -- mac` → prázdné (`mac/` beze změny).

Nálezy opravit v samostatných commitech (řádek VYVOJ „Závěrečné opravy — …“).

- [ ] **Step 3: Sada, Release build, balíček**

Run: `cd windows` → `dotnet test` → zelená (`ProxyWatchdogTests` případně znovu samostatně); `powershell -ExecutionPolicy Bypass -File scripts\build.ps1` → `build\Offload-win-x64.zip` (build GUI nespouští). Ve `build\Offload\bin\` je `cs\System.CommandLine.resources.dll` (české chyby parseru). `build\Offload\bin\offload.exe verify <hotová záloha> --report` → český souhrn a report v `_REPORT`.

- [ ] **Step 4: VYVOJ + commit**

Řádek:

```markdown
- Úkol 19 — závěrečná revize plánu 6 a seznam ručního testu v README (karta ve dvou čtečkách, posun času na FAT32, `ascmhl verify` po doplnění, okno „Karty už jsou zálohované“, název z klipů, ověření zálohy, místo)
```

```powershell
git add windows docs
git commit -m "Windows: plan 6 final review and manual test list" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Předání**

Ruční test (README body 11–20) dělá uživatel u PC s kartou; GUI bez domluvy nespouštět. V reportu pro uživatele uvést: počet testů, co ověřit ručně, a že `ascmhl verify` je potřeba pustit na macOS/Linuxu.
