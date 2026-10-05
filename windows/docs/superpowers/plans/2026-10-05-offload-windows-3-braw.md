# Offload — plán 3: dekodér Blackmagic RAW v C# (`braw-decode.exe`)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** BRAW klipy dostanou na Windows proxy, tři snímky v reportu a automatický LUT podle gamy klipu — dekodér `bin\braw-decode.exe` napsaný v C# nad COM rozhraním `BlackmagicRawAPI.dll`, navenek totožný s Mac `braw-decode` (stejné CLI, stejný JSON, stejné surové snímky RGB48LE na stdout).

**Architecture:** Nový malý konzolový projekt `windows/src/Offload.BrawDecode` (assembly `braw-decode`, publikovaný jako samostatný, ořezaný single-file exe do `Offload\bin\`). Knihovnu `BlackmagicRawAPI.dll` načte za běhu přes `NativeLibrary` (pořadí: složka exe → Blackmagic RAW SDK → Blackmagic RAW Player → DaVinci Resolve) a volá ji **přes vtable s ukazateli na funkce** (`delegate* unmanaged[Stdcall]`) — žádné RCW/`[ComImport]`, reference se počítají ručně přesně jako v C++ nástroji. Callback `IBlackmagicRawCallback` je nativní COM objekt z `[UnmanagedCallersOnly]` metod. Tok jako na Macu: CPU pipeline, čtení snímků po dávkách 8 (SDK dekóduje paralelně), výstup v pořadí. Hledání knihovny (`BrawLibrary`) je v Offload.Core a sdílí ho dekodér i `BrawBackend` (BRAW je „dostupný“, jen když existuje i knihovna). `--info` navíc vrací `gamma`, `gamut`, `colorScienceGen`; ty jdou do `ClipInfo` a `LutResolver` podle nich volí LUT. Přitom se opraví start snímku náhledů (zaokrouhlit, ne useknout).

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), function-pointer COM interop, `System.Runtime.InteropServices.Marshalling.ComVariant` (OLE VARIANT), xUnit 2.9, ffmpeg (E2E test).

**Spec:** `windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md` §6.5, §6.5a, §6.6; `windows/docs/superpowers/specs/2026-10-05-offload-luts-design.md` §3 (řádek Blackmagic RAW) a §7. Mac nástroj k portu: `mac/Tools/braw-decode/braw-decode.cpp` (+ hlavička SDK `BlackmagicRawAPI.h`, která už je v repu).

## Ověřeno spikem (2026-10-05, bez SDK, s knihovnou z DaVinci Resolve)

Zahozená konzolová aplikace (.NET 10, mimo repo) nad `C:\Program Files\Blackmagic Design\DaVinci Resolve\BlackmagicRawAPI.dll`
(exporty `CreateBlackmagicRawFactoryInstance`, `DllGetClassObject`, …; importuje `OLEAUT32` → BSTR/VARIANT):

| Volání | Výsledek |
|---|---|
| `NativeLibrary.Load` + `CreateBlackmagicRawFactoryInstance()` | ukazatel na factory; `QI(IID_IBlackmagicRawFactory)` = S_OK, stejný ukazatel |
| `factory.CreateCodec` (slot 3) | S_OK |
| `codec.QI(IBlackmagicRawConfiguration)` | S_OK; `GetVersion` → `"0.0"` (build v Resolve), `GetCameraSupportVersion` → `"5.1"` (BSTR) |
| `IsPipelineSupported` CPU / CUDA / OpenCL / Metal (BOOL předvyplněný `0xCCCCCCCC`) | `0x00000001` / `0x00000001` / `0x00000001` / `0x00000000` → BOOL = 4 bajty |
| `GetPipeline` výchozí → `SetPipeline(CPU)` → `GetPipeline` | `'cpub'` → S_OK → `'cpub'`; `GetCPUThreads` / `GetMaxCPUThreadCount` = 16 / 16 |
| `IBlackmagicRawConfigurationEx.GetInstructionSet` | `'avx2'` |
| `IBlackmagicRawToneCurve.GetToneCurve(BSTR kamera, BSTR gama, gen 5, …)` | Film: contrast 1, sat 1, mid 0.3836; Extended Video: contrast 1.58, sat 1.4, mid 0.409 → vstupní BSTR funguje |
| `CreatePipelineIterator(none)` | „CUDA“ `'cuda'`, „OpenCL“ `'opcl'`, „CPU“ `'cpub'`, pak `Next` = S_FALSE |
| `CreatePipelineDeviceIterator(CPU)` → `CreateDevice` | jméno „AVX2“, pipeline „CPU“ |
| `CreateClipGeometry` → `GetRotation` / `SetRotation(r180)` | `'meta'` → `'r180'` |
| `codec.SetCallback(C# callback)` | S_OK, SDK si drží referenci (refcount 1 → 2), `SetCallback(null)` ji vrátí (→ 1); bez klipu se nic nevolá |
| `codec.OpenClip("C:\nonexistent\clip.braw")` / `OpenClip("C:\Windows\win.ini")` | `0x80004005` (E_FAIL), clip = null |
| ořezaný self-contained single-file publish | 12,8 MB, běží, žádná varování ořezu |
| `ComVariant` | 24 bajtů, VT_BSTR → `As<string>()`, `Dispose` = VariantClear |

Na tomto PC **není žádný .braw** — dekódování snímků, `GetClipAttribute` (gamma/gamut přes VARIANT) a celý tok s
klipem se ověří až testy `[BrawSampleFact]` se vzorkem (Task 6, 7, 8).

## Global Constraints

- **Licence:** hlavička ani knihovny SDK se do Windows projektu nekopírují. Deklarace rozhraní v C# jsou napsané podle
  pořadí metod v IDL (komentář u každé struktury); Mac hlavička `mac/Tools/braw-decode/BlackmagicRawAPI.h` v repu už je
  a slouží jako reference. `BlackmagicRawAPI.dll` se nepřibaluje — uživatel má SDK, Player nebo Resolve.
- Žádné nové NuGet balíčky; kód v `mac/` se nemění (jen `docs/VYVOJ-MAC.md`).
- `TreatWarningsAsErrors` platí i pro nový projekt; `AllowUnsafeBlocks` je už v `Directory.Build.props`.
- Navenek stejné jako Mac: argumenty, klíče JSON a jejich pořadí, `fps` se 6 desetinnými místy, snímky přesně
  `šířka × výška × 6` bajtů, výstupní kódy 0/2/3/4/5, zavřená roura (ffmpeg skončil) = konec s kódem 0.
  Odchylky (záměrné): navíc `gamma`/`gamut`/`colorScienceGen`; chybný argument = kód 2 (Mac potichu bere výchozí).
- Na stdout jen data — `FileStream` nad handlem z `GetStdHandle(STD_OUTPUT_HANDLE)` (rozhodnutí M1: na rozdíl od
  `Console.OpenStandardOutput()` hlásí zavřenou rouru jako chybu, takže dekodér po konci ffmpeg přestane), hlášky jen
  na stderr (UTF-8) s prefixem `braw-decode: `.
- Výjimka nikdy neprojde do vlákna SDK (každá `[UnmanagedCallersOnly]` metoda má `try/catch`).
- Každý `Create*`/`Clone*`/`QueryInterface` výsledek se uvolní právě jednou; callback objekt se uvolní až po uvolnění kodeku.
- Každý commit: řádek nahoře v nové sekci `## 2026-10-05 — plán 3 (dekodér BRAW v C#), průběh` v `docs/VYVOJ-WINDOWS.md`
  (nad sekcí „drobnosti po plánu 5“; datum podle dne provedení), bez SHA; poslední řádek zprávy přesně
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Větev `windows-braw` z aktuálního `main` (pokud `windows-followups` ještě není v `main`, nejdřív ji sloučit — se
  souhlasem uživatele). Příkazy níže se pouštějí ze složky `windows\`, pokud není řečeno jinak.

## Review Focus

1. **Počítání referencí a životnost** — read job se uvolní v `ReadComplete`, decode job v `ProcessComplete` (nebo hned,
   když `Submit` selže), `frame`/`processedImage` z callbacku se neuvolňují (nejsou naše); `FlushJobs` před uvolněním
   klipu; paměť callbacku až po `Release` kodeku. Test: Task 5 `Callback_IsAComObjectTheSdkCanCall`,
   `Sdk_FactoryCodecConfigurationCallback` (refcount 2 → 1, kodek/factory na 0).
2. **Binární stdout, přesně W×H×6 bajtů na snímek** (SDK buffer na konci zarovnává; kratší buffer = chyba, ne posun
   obrazu v ffmpeg). Test: Task 4 `FrameSlot_CopiesExactlyWidthHeightRgb48`, Task 6 `Frames_ExactRgb48Bytes_InOrder`,
   `Exe_StdoutIsBinary`.
3. **Chybějící knihovna** → BRAW klip je „nepodporovaný“ s nápovědou (ne „selhaná proxy“), dekodér kód 3. Test: Task 1
   `Backend_AvailableOnlyWithDecoderAndLibrary`, Task 6 `Run_NoLibrary_Exit3`.
4. **BRAW natočený ve Video / Extended Video gamě** → žádný vestavěný LUT (jinak dvojí převod, přepálený obraz).
   Test: Task 3 `Lut_FromBrawGamma`, `Lut_VideoGamma_IgnoresFallback`.
5. **Náhledy: prostřední a poslední snímek** — start se zaokrouhluje (SDK hlásí float 23.976, pozice počítá s 24000/1001).
   Test: Task 2 `StartFrame_IsRounded`.
6. **Konfigurace před `OpenClip`** (SDK ji čte při prvním `OpenClip`; Mac ji nastavuje až po něm — na Windows správně).
   Kontrola v Task 6 kódu `Decoder.Run`.

---

### Task 1: Core — kde je knihovna Blackmagic RAW, kdy je BRAW dostupný

**Files:**
- Create: `windows/src/Offload.Core/BrawLibrary.cs`
- Modify: `windows/src/Offload.Core/RawBackends.cs` (`BrawBackend.IsAvailable`)
- Create: `windows/tests/Offload.Core.Tests/BrawCoreTests.cs`

**Interfaces:**
- Produces: `public static class BrawLibrary { const string DllName; IReadOnlyList<string> SearchDirs(string decoderDir); string? Find(IEnumerable<string> dirs); string? Find(string decoderDir); }`
- `BrawBackend.IsAvailable(tools)` = `tools.BrawDecoder` existuje **a** `BrawLibrary.Find(složka dekodéru)` není null.

- [ ] **Step 1: Padající testy** — `windows/tests/Offload.Core.Tests/BrawCoreTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class BrawCoreTests
{
    // Task 1 — library search
    [Fact]
    public void Library_SearchOrder_ExeDirFirstThenSdkPlayerResolve()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.Equal(
        [
            @"D:\Offload\bin",
            Path.Combine(pf, @"Blackmagic Design\Blackmagic RAW\Blackmagic RAW SDK\Win\Libraries"),
            Path.Combine(pf, @"Blackmagic Design\Blackmagic RAW\Blackmagic RAW Player"),
            Path.Combine(pf, @"Blackmagic Design\DaVinci Resolve"),
        ], BrawLibrary.SearchDirs(@"D:\Offload\bin"));
    }

    [Fact]
    public void Library_Find_FirstExisting()
    {
        using var dir = new TempDir();
        var a = dir.Sub("a");
        var b = dir.Sub("b");
        Assert.Null(BrawLibrary.Find([a, b]));
        File.WriteAllText(Path.Combine(b, "BlackmagicRawAPI.dll"), "x");
        Assert.Equal(Path.Combine(b, "BlackmagicRawAPI.dll"), BrawLibrary.Find([a, b]));
        File.WriteAllText(Path.Combine(a, "BlackmagicRawAPI.dll"), "x");
        Assert.Equal(Path.Combine(a, "BlackmagicRawAPI.dll"), BrawLibrary.Find([a, b]));
    }

    [Fact]
    public void Backend_AvailableOnlyWithDecoderAndLibrary()
    {
        using var dir = new TempDir();
        var exe = Path.Combine(dir.Path, "braw-decode.exe");
        File.WriteAllText(exe, "x");
        var backend = new BrawBackend();
        Assert.False(backend.IsAvailable(new ExternalTools { NoSearch = true }));
        // A library next to the decoder always counts, wherever Resolve / the SDK are.
        File.WriteAllText(Path.Combine(dir.Path, "BlackmagicRawAPI.dll"), "x");
        Assert.True(backend.IsAvailable(new ExternalTools { BrawDecoder = exe, NoSearch = true }));
    }
}
```

- [ ] **Step 2: Ověřit selhání**

Run: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~BrawCoreTests"` → nesestaví se (`BrawLibrary` neexistuje).

- [ ] **Step 3: Implementace** — `windows/src/Offload.Core/BrawLibrary.cs`:

```csharp
namespace Offload.Core;

/// Where the Blackmagic RAW library (BlackmagicRawAPI.dll) can be. Offload never ships it: it comes with the
/// Blackmagic RAW SDK, the free Blackmagic RAW Player or DaVinci Resolve. Shared by braw-decode (which loads it)
/// and BrawBackend (which only checks it exists, so a missing library reads as "unsupported", not as a failure).
public static class BrawLibrary
{
    public const string DllName = "BlackmagicRawAPI.dll";

    /// Searched in order: next to braw-decode.exe, the SDK, the Player, DaVinci Resolve.
    public static IReadOnlyList<string> SearchDirs(string decoderDir)
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return
        [
            decoderDir,
            Path.Combine(pf, "Blackmagic Design", "Blackmagic RAW", "Blackmagic RAW SDK", "Win", "Libraries"),
            Path.Combine(pf, "Blackmagic Design", "Blackmagic RAW", "Blackmagic RAW Player"),
            Path.Combine(pf, "Blackmagic Design", "DaVinci Resolve"),
        ];
    }

    public static string? Find(IEnumerable<string> dirs) =>
        dirs.Select(d => Path.Combine(d, DllName)).FirstOrDefault(File.Exists);

    public static string? Find(string decoderDir) => Find(SearchDirs(decoderDir));
}
```

V `RawBackends.cs` (třída `BrawBackend`) nahradit:

```csharp
    public bool IsAvailable(ExternalTools tools) => tools.BrawDecoder != null;
```

za:

```csharp
    /// braw-decode alone is not enough: without BlackmagicRawAPI.dll it can only fail.
    public bool IsAvailable(ExternalTools tools) =>
        tools.BrawDecoder is string exe && BrawLibrary.Find(Path.GetDirectoryName(exe) ?? "") != null;
```

Pozn.: cesty SDK a Playeru (`…\Blackmagic RAW\Blackmagic RAW SDK\Win\Libraries`, `…\Blackmagic RAW\Blackmagic RAW Player`)
jsou podle návrhu §6.6 a instalátoru „Blackmagic RAW“; na tomto PC nejsou nainstalované — ověřit v Task 8, až je
uživatel nainstaluje (Resolve je ověřený spikem).

- [ ] **Step 4: Testy + celá sada, Commit**

Run: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~BrawCoreTests"` → PASS (3). Celá sada
`dotnet test tests\Offload.Core.Tests` zelená (pozn.: `ProxyWatchdogTests.Render_StalledWithHwaccel_SoftwareRetrySucceeds`
je časově citlivý a při plném běhu občas spadne i bez změn — zopakovat samostatně).
Řádek VYVOJ: `- Úkol 1 — hledání knihovny Blackmagic RAW (vedle braw-decode, SDK, Player, DaVinci Resolve); bez knihovny je BRAW „nepodporovaný“ s nápovědou místo selhané proxy`

```powershell
git add windows docs
git commit -m "Windows: Blackmagic RAW library search; BRAW available only with the library" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Core — náhledy BRAW: start snímku zaokrouhlit

`BrawBackend.Source` dnes počítá `(int)(t * fps)`: `t` je z přesné sazby (`24000/1001`), `fps` z dekodéru je float SDK
(23.976) → `49.9999…` → snímek 49 místo 50, prostřední i poslední náhled o snímek dřív. Mac plán parity to řeší
`BRAWBackend.startFrame` se zaokrouhlením — stejná funkce i tady.

**Files:**
- Modify: `windows/src/Offload.Core/RawBackends.cs` (`BrawBackend.StartFrame`, `Source`)
- Modify: `windows/tests/Offload.Core.Tests/BrawCoreTests.cs`

**Interfaces:**
- Produces: `public static int BrawBackend.StartFrame(double seconds, double fps, int frames)` — `fps ≤ 0` → 25; výsledek v `[0, frames−1]`, `frames ≤ 0` → 0.

- [ ] **Step 1: Padající test** — do třídy `BrawCoreTests` přidat:

```csharp
    // Task 2 — thumbnails: braw-decode's fps is the SDK float (23.976) while positions use the exact rate.
    [Fact]
    public void StartFrame_IsRounded()
    {
        const double exact = 24000.0 / 1001;
        Assert.Equal(25, BrawBackend.StartFrame(25 / exact, 23.976f, 50));
        Assert.Equal(49, BrawBackend.StartFrame(49 / exact, 23.976f, 50));
        Assert.Equal(49, BrawBackend.StartFrame(100, 25, 50));
        Assert.Equal(25, BrawBackend.StartFrame(1, 0, 50));   // unknown fps → 25
        Assert.Equal(0, BrawBackend.StartFrame(1, 25, 0));
        Assert.Equal(0, BrawBackend.StartFrame(0, 23.976f, 50));
    }
```

- [ ] **Step 2: Ověřit selhání** — Run: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~BrawCoreTests"` → nesestaví se (`StartFrame`).

- [ ] **Step 3: Implementace** — v `BrawBackend` za `ToClipInfo` přidat:

```csharp
    /// The frame braw-decode starts at for a thumbnail `seconds` into the clip. Rounded: `seconds` comes from the
    /// exact rational rate (24000/1001), braw-decode reports the SDK's float (23.976) — truncating lands a frame early.
    public static int StartFrame(double seconds, double fps, int frames)
    {
        double rate = fps > 0 ? fps : 25;
        return Math.Min(Math.Max(0, frames - 1), Math.Max(0, (int)Math.Round(seconds * rate, MidpointRounding.AwayFromZero)));
    }
```

a v `Source` nahradit:

```csharp
            double fps = dims.Fps > 0 ? dims.Fps : 25;
            int start = Math.Min(Math.Max(0, dims.Frames - 1), (int)(t * fps));
```

za:

```csharp
            int start = StartFrame(t, dims.Fps, dims.Frames);
```

- [ ] **Step 4: Testy, Commit**

Run: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~BrawCoreTests"` → PASS (4). Celá sada zelená.
Řádek VYVOJ: `- Úkol 2 — náhledy BRAW: počáteční snímek se zaokrouhluje (prostřední a poslední snímek už nevycházejí o jeden dřív)`

```powershell
git add windows docs
git commit -m "Windows: BRAW thumbnail start frame rounded, not truncated" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Core — gama a gamut klipu z `--info`, LUT podle gamy

**Files:**
- Modify: `windows/src/Offload.Core/Media.cs` (`ClipInfo.Gamma`, `Gamut`, `ColorScienceGen`)
- Modify: `windows/src/Offload.Core/RawBackends.cs` (`BrawInfo`, `ToClipInfo`)
- Modify: `windows/src/Offload.Core/Luts.cs` (`LutResolver.Braw`, `Detect`)
- Modify: `windows/tests/Offload.Core.Tests/BrawCoreTests.cs`
- Modify: `docs/FORMATY.md` (nové volitelné klíče `clips[].info.gamma/gamut/colorScienceGen`)

**Interfaces:**
- Consumes: JSON z `braw-decode --info` s volitelnými klíči `gamma` (string), `gamut` (string), `colorScienceGen` (int) — Task 6 je vydá; Mac `braw-decode` je zatím nevydává (dál se načte, klíče jsou null).
- Produces: `ClipInfo.Gamma`, `ClipInfo.Gamut` (string?), `ClipInfo.ColorScienceGen` (int?) — JSON `gamma`, `gamut`, `colorScienceGen`, při null vynechané.
- Pravidlo LUTu pro BRAW: gama neuvedena → `bmd-film-gen5` (dosavadní předpoklad); „Blackmagic Design Film“ Gen 5 nebo bez gen →
  `bmd-film-gen5`; Film Gen < 5 → žádný LUT („bez vestavěného LUTu“); jakákoli jiná gama → žádný LUT („není log“).

- [ ] **Step 1: Padající testy** — do třídy `BrawCoreTests` přidat:

```csharp
    // Task 3 — gamma / gamut from --info
    [Fact]
    public void Info_GammaGamutGen_ReachClipInfo()
    {
        var i = BrawBackend.ParseInfo("""{"width":1440,"height":810,"sourceWidth":11520,"sourceHeight":6480,"fps":23.976000,"frames":240,"timecode":"01:02:03:04","camera":"URSA Mini Pro 12K","gamma":"Blackmagic Design Film","gamut":"Blackmagic Design","colorScienceGen":5}""");
        var c = BrawBackend.ToClipInfo(i);
        Assert.Equal(("Blackmagic Design Film", "Blackmagic Design", 5), (c.Gamma, c.Gamut, c.ColorScienceGen));
        // The Mac decoder (and older builds) print no gamma: still parses.
        var old = BrawBackend.ToClipInfo(BrawBackend.ParseInfo("""{"width":1,"height":1,"sourceWidth":1,"sourceHeight":1,"fps":25,"frames":1,"timecode":"","camera":""}"""));
        Assert.Equal((null, null, null), (old.Gamma, old.Gamut, old.ColorScienceGen));
    }

    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);
    static readonly Clip BrawClip = new() { Name = "A001", Path = "A001.braw", Format = ClipFormat.Braw, Segments = ["A001.braw"], Bytes = 1 };
    static ProxySettings Auto() => new() { Enabled = true, LutMode = LutMode.Auto };

    [Theory]
    [InlineData("Blackmagic Design Film", 5, "auto", "bmd-film-gen5", "Blackmagic RAW: Blackmagic Design Film Gen 5")]
    [InlineData("Blackmagic Design Film", null, "auto", "bmd-film-gen5", "Blackmagic RAW: Blackmagic Design Film")]
    [InlineData("Blackmagic Design Film", 4, "none", null, "Blackmagic RAW: Blackmagic Design Film Gen 4 (bez vestavěného LUTu)")]
    [InlineData("Blackmagic Design Video", 5, "none", null, "Blackmagic RAW: Blackmagic Design Video Gen 5 (není log)")]
    [InlineData("Blackmagic Design Extended Video", 5, "none", null, "Blackmagic RAW: Blackmagic Design Extended Video Gen 5 (není log)")]
    [InlineData(null, null, "auto", "bmd-film-gen5", "Blackmagic RAW (gama neuvedena — předpoklad Film Gen 5)")]
    public void Lut_FromBrawGamma(string? gamma, int? gen, string source, string? id, string reason)
    {
        var use = LutResolver.Resolve(BrawClip, new ClipInfo { Gamma = gamma, ColorScienceGen = gen }, @"Z:\A001.braw", Auto(), Cat);
        Assert.Equal((source, id, reason), (use.Source, use.Id, use.Reason));
    }

    [Fact]
    public void Lut_VideoGamma_IgnoresFallback()
    {
        var s = new ProxySettings { Enabled = true, LutMode = LutMode.Auto, LutFallback = "bmd-film-gen5" };
        var use = LutResolver.Resolve(BrawClip, new ClipInfo { Gamma = "Blackmagic Design Video" }, @"Z:\A001.braw", s, Cat);
        Assert.Equal("none", use.Source);   // recognised as display-ready, the fallback is only for unknown cameras
    }
```

- [ ] **Step 2: Ověřit selhání** — Run: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~BrawCoreTests"` → nesestaví se (`ClipInfo.Gamma`, `BrawInfo.Gamma`).

- [ ] **Step 3: Implementace**

`Media.cs`, třída `ClipInfo`, za `ColorRange`:

```csharp
    /// Blackmagic RAW: gamma, gamut and colour science generation the clip was recorded in (from braw-decode --info).
    [JsonPropertyName("gamma")] public string? Gamma { get; set; }
    [JsonPropertyName("gamut")] public string? Gamut { get; set; }
    [JsonPropertyName("colorScienceGen")] public int? ColorScienceGen { get; set; }
```

`RawBackends.cs` — konec záznamu `BrawInfo`:

```csharp
    [property: JsonPropertyName("camera")] string Camera,
    [property: JsonPropertyName("gamma")] string? Gamma = null,
    [property: JsonPropertyName("gamut")] string? Gamut = null,
    [property: JsonPropertyName("colorScienceGen")] int? ColorScienceGen = null);
```

a v `ToClipInfo` za `Camera = …`:

```csharp
        Gamma = string.IsNullOrEmpty(i.Gamma) ? null : i.Gamma,
        Gamut = string.IsNullOrEmpty(i.Gamut) ? null : i.Gamut,
        ColorScienceGen = i.ColorScienceGen,
```

`Luts.cs`, třída `LutResolver` — nad `Detect` přidat:

```csharp
    /// BRAW decodes in the gamma the clip was shot in: only "Blackmagic Design Film" Gen 5 matches the built-in LUT.
    /// Video / Extended Video / Custom / HDR gammas are display-ready already; an older decoder without the gamma
    /// in --info keeps the old assumption (Film Gen 5).
    static (string? Id, string Reason) Braw(ClipInfo? info)
    {
        var gamma = info?.Gamma;
        if (string.IsNullOrEmpty(gamma)) return ("bmd-film-gen5", "Blackmagic RAW (gama neuvedena — předpoklad Film Gen 5)");
        var label = info!.ColorScienceGen is int gen ? $"{gamma} Gen {gen}" : gamma;
        if (!gamma.Equals("Blackmagic Design Film", StringComparison.OrdinalIgnoreCase))
            return ("", $"Blackmagic RAW: {label} (není log)");
        if (info.ColorScienceGen is int g && g < 5) return ("", $"Blackmagic RAW: {label} (bez vestavěného LUTu)");
        return ("bmd-film-gen5", $"Blackmagic RAW: {label}");
    }
```

a v `Detect` nahradit řádek `case ClipFormat.Braw: return ("bmd-film-gen5", "Blackmagic RAW (gama Film Gen 5)");` za:

```csharp
            case ClipFormat.Braw: return Braw(info);
```

`docs/FORMATY.md` — v tabulce `offload_report.json`, řádek `clips[]`, za `info`? doplnit poznámku a pod tabulku odstavec:

```markdown
`clips[].info.gamma`, `gamut`, `colorScienceGen` (volitelné, zatím jen Windows): u Blackmagic RAW gama, gamut a generace
barevné vědy, ve kterých byl klip natočený (z `braw-decode --info`, např. `"Blackmagic Design Film"`, `5`). Podle nich se
volí LUT. Mac je zatím nezapisuje a při čtení ignoruje.
```

Pozn. k otevřené otázce: Film **Gen 4** (starší kamery) vestavěný LUT nemá → bez LUTu. Alternativa (SDK umí klip
převést na Gen 5 atributem `ColorScienceGen`) je mimo rozsah; zeptat se uživatele, až bude vzorek Gen 4.

- [ ] **Step 4: Testy + celá sada, Commit**

Run: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~BrawCoreTests|FullyQualifiedName~LutResolverTests|FullyQualifiedName~RawBackendsTests"` → PASS. Celá sada zelená.
Řádek VYVOJ: `- Úkol 3 — BRAW: gama, gamut a generace barev z dekodéru v metadatech klipu; LUT jen pro Blackmagic Design Film Gen 5, Video / Extended Video bez LUTu`

```powershell
git add windows docs
git commit -m "Windows: BRAW gamma/gamut in clip info; LUT chosen from the clip's gamma" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Projekt `braw-decode` — příkazová řádka, JSON, bajty snímku

Rozhodnutí: **samostatný `bin\braw-decode.exe`**, ne podpříkaz `offload braw-decode`. `ExternalTools` už hledá
`braw-decode.exe` (`bin\` vedle aplikace, PATH, nastavení) a `BrawBackend` ho spouští přímo s argumenty Mac nástroje
(`--info --scale S clip`, `--scale S [--start N --count 1] clip`) jako feeder roury do ffmpeg — beze změny backendu,
GUI řádku nástrojů ani nastavení. Na stdout pak nic jiného než data (CLI `offload` vypisuje průběh). Ořezaný
self-contained exe má ~13 MB (bez ořezu ~70 MB).

**Files:**
- Create: `windows/src/Offload.BrawDecode/Offload.BrawDecode.csproj`, `Options.cs`, `InfoJson.cs`, `Program.cs` (dočasný)
- Modify: `windows/Offload.sln`, `windows/tests/Offload.Core.Tests/Offload.Core.Tests.csproj`
- Create: `windows/tests/Offload.Core.Tests/BrawDecodeTests.cs`

**Interfaces:**
- Produces (internal, `InternalsVisibleTo Offload.Core.Tests`):
  - `enum Scale : uint { Full, Half, Quarter, Eighth }` (hodnoty = FourCC SDK)
  - `record Options(bool Info, Scale Scale, ulong Start, ulong? Count, string Path)`; `static Options? Parse(IReadOnlyList<string>)`; `const string Usage`; `(ulong First, ulong End) Range(ulong frames)`
  - `record ClipFacts(uint Width, uint Height, uint SourceWidth, uint SourceHeight, float Fps, ulong Frames, string Timecode, string Camera, string? Gamma, string? Gamut, int? ColorScienceGen)`
  - `static class InfoJson { string Format(ClipFacts); string Escape(string); }`
  - `sealed class FrameSlot { byte[] Data; int Length; uint Width, Height; volatile bool Ok; static long ExpectedBytes(uint, uint); unsafe bool Fill(uint w, uint h, void* resource, uint sizeBytes); }`

- [ ] **Step 1: Projekt**

`windows/src/Offload.BrawDecode/Offload.BrawDecode.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>braw-decode</AssemblyName>
    <RootNamespace>Offload.BrawDecode</RootNamespace>
    <InvariantGlobalization>true</InvariantGlobalization>
    <!-- Trimmed on publish: ~13 MB instead of ~70 MB. Set here, not on the command line, so Offload.Core
         (reflection-based JSON elsewhere) is not trim-analysed as a whole — only the code braw-decode reaches. -->
    <PublishTrimmed>true</PublishTrimmed>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Offload.Core\Offload.Core.csproj" />
    <InternalsVisibleTo Include="Offload.Core.Tests" />
  </ItemGroup>
</Project>
```

Dočasný `windows/src/Offload.BrawDecode/Program.cs` (Task 6 ho nahradí):

```csharp
using Offload.BrawDecode;

if (Options.Parse(args) == null)
{
    Console.Error.WriteLine(Options.Usage);
    return 2;
}
Console.Error.WriteLine("braw-decode: not implemented yet");
return 3;
```

```powershell
dotnet sln Offload.sln add --solution-folder src src\Offload.BrawDecode\Offload.BrawDecode.csproj
```

`tests/Offload.Core.Tests/Offload.Core.Tests.csproj` — za referenci na Offload.Core přidat:

```xml
    <ProjectReference Include="..\..\src\Offload.BrawDecode\Offload.BrawDecode.csproj" />
```

(Reference na exe projekt zkopíruje `braw-decode.exe` + `.dll` + `runtimeconfig.json` do výstupu testů — ověřeno; Task 6
ho spouští jako proces.)

- [ ] **Step 2: Padající testy** — `windows/tests/Offload.Core.Tests/BrawDecodeTests.cs`:

```csharp
using System.Runtime.InteropServices;
using Offload.BrawDecode;

namespace Offload.Core.Tests;

public class BrawDecodeTests
{
    // --- command line (same as the Mac tool) ---

    [Fact]
    public void Options_MacContract()
    {
        Assert.Equal(new Options(true, Scale.Eighth, 0, null, @"E:\A001.braw"), Options.Parse(["--info", "--scale", "eighth", @"E:\A001.braw"]));
        Assert.Equal(new Options(false, Scale.Quarter, 120, 1, "a.braw"), Options.Parse(["--scale", "quarter", "--start", "120", "--count", "1", "a.braw"]));
        Assert.Equal(new Options(false, Scale.Full, 0, null, "a.braw"), Options.Parse(["a.braw"]));
        Assert.Equal(Scale.Half, Options.Parse(["--scale", "half", "a.braw"])!.Scale);
    }

    [Theory]
    [InlineData]
    [InlineData("--info")]
    [InlineData("--scale", "huge", "a.braw")]
    [InlineData("--start", "-1", "a.braw")]
    [InlineData("--count", "x", "a.braw")]
    [InlineData("--scale")]
    [InlineData("--bogus", "a.braw")]
    public void Options_Bad_IsNull(params string[] args) => Assert.Null(Options.Parse(args));

    [Fact]
    public void Options_Range_ClampedToClip()
    {
        Assert.Equal((0UL, 240UL), Options.Parse(["a"])!.Range(240));
        Assert.Equal((239UL, 240UL), Options.Parse(["--start", "239", "--count", "5", "a"])!.Range(240));
        Assert.Equal((120UL, 121UL), Options.Parse(["--start", "120", "--count", "1", "a"])!.Range(240));
        Assert.Equal((300UL, 240UL), Options.Parse(["--start", "300", "a"])!.Range(240));   // nothing to do
        Assert.Equal((5UL, 240UL), Options.Parse(["--start", "5", "--count", "18446744073709551615", "a"])!.Range(240));
    }

    // --- --info JSON ---

    [Fact]
    public void InfoJson_MacKeysThenColour()
    {
        var f = new ClipFacts(1440, 810, 11520, 6480, 23.976f, 240, "01:02:03:04", "URSA Mini Pro 12K", "Blackmagic Design Film", "Blackmagic Design", 5);
        Assert.Equal("""{"width":1440,"height":810,"sourceWidth":11520,"sourceHeight":6480,"fps":23.976000,"frames":240,"timecode":"01:02:03:04","camera":"URSA Mini Pro 12K","gamma":"Blackmagic Design Film","gamut":"Blackmagic Design","colorScienceGen":5}""" + "\n",
            InfoJson.Format(f));
        var bare = new ClipFacts(1, 2, 3, 4, 25, 0, "", "", null, null, null);
        Assert.Equal("""{"width":1,"height":2,"sourceWidth":3,"sourceHeight":4,"fps":25.000000,"frames":0,"timecode":"","camera":""}""" + "\n",
            InfoJson.Format(bare));
    }

    [Fact]
    public void InfoJson_RoundTripsThroughBrawBackend()
    {
        var f = new ClipFacts(720, 405, 5760, 3240, 29.97f, 10, "10:00:00:00", "Pocket \"6K\" \\ Pro\u0001", "Blackmagic Design Video", null, 5);
        var i = BrawBackend.ParseInfo(InfoJson.Format(f));
        Assert.Equal((720, 405, 5760, 3240, 10), (i.Width, i.Height, i.SourceWidth, i.SourceHeight, i.Frames));
        Assert.Equal(29.97, i.Fps, 3);
        Assert.Equal("Pocket \"6K\" \\ Pro", i.Camera);
        Assert.Equal(("Blackmagic Design Video", null, 5), (i.Gamma, i.Gamut, i.ColorScienceGen));
    }

    // --- frame bytes ---

    [Fact]
    public unsafe void FrameSlot_CopiesExactlyWidthHeightRgb48()
    {
        var padded = new byte[4 * 2 * 6 + 100];   // the SDK pads its buffer at the end
        for (int i = 0; i < padded.Length; i++) padded[i] = (byte)i;
        var slot = new FrameSlot();
        fixed (byte* p = padded) Assert.True(slot.Fill(4, 2, p, (uint)padded.Length));
        Assert.True(slot.Ok);
        Assert.Equal(48, slot.Length);
        Assert.Equal(padded[..48], slot.Data[..slot.Length]);
        fixed (byte* p = padded) Assert.False(new FrameSlot().Fill(4, 2, p, 47));   // short buffer = failure
        Assert.False(new FrameSlot().Fill(4, 2, null, 48));
        Assert.Equal(12288L * 6480 * 6, FrameSlot.ExpectedBytes(12288, 6480));
    }
}
```

- [ ] **Step 3: Ověřit selhání** — Run: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~BrawDecodeTests"` → nesestaví se (`Options`, `InfoJson`, `FrameSlot`).

- [ ] **Step 4: Implementace**

`windows/src/Offload.BrawDecode/Options.cs`:

```csharp
using System.Globalization;

namespace Offload.BrawDecode;

/// Decode scale, as the SDK's BlackmagicRawResolutionScale four-character codes.
internal enum Scale : uint
{
    Full = 0x66756C6C,    // 'full'
    Half = 0x68616C66,    // 'half'
    Quarter = 0x71727472, // 'qrtr'
    Eighth = 0x65697468,  // 'eith'
}

/// Command line, the same as the Mac braw-decode:
///   braw-decode --info [--scale S] clip.braw
///   braw-decode [--scale S] [--start N] [--count N] clip.braw
internal sealed record Options(bool Info, Scale Scale, ulong Start, ulong? Count, string Path)
{
    public const string Usage = "usage: braw-decode [--info] [--scale full|half|quarter|eighth] [--start N] [--count N] clip.braw";

    /// null = bad command line (print Usage, exit 2). Stricter than the Mac (which falls back to "full" / 0):
    /// Offload never passes anything else, so a typo should fail loudly.
    public static Options? Parse(IReadOnlyList<string> args)
    {
        bool info = false;
        var scale = Scale.Full;
        ulong start = 0;
        ulong? count = null;
        string? path = null;
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            string? Value() => i + 1 < args.Count ? args[++i] : null;
            switch (a)
            {
                case "--info":
                    info = true;
                    break;
                case "--scale":
                    var s = Value();
                    if (s == null || !TryScale(s, out scale)) return null;
                    break;
                case "--start":
                    if (!ulong.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out start)) return null;
                    break;
                case "--count":
                    if (!ulong.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
                    count = n;
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal)) return null;
                    path = a;
                    break;
            }
        }
        return path == null ? null : new Options(info, scale, start, count, path);
    }

    static bool TryScale(string s, out Scale scale)
    {
        scale = s switch { "full" => Scale.Full, "half" => Scale.Half, "quarter" => Scale.Quarter, "eighth" => Scale.Eighth, _ => 0 };
        return scale != 0;
    }

    /// Frames [first, end) to decode, clamped to the clip.
    public (ulong First, ulong End) Range(ulong frames) =>
        (Start, Count is ulong c ? Math.Min(frames, Start + Math.Min(c, ulong.MaxValue - Start)) : frames);
}
```

`windows/src/Offload.BrawDecode/InfoJson.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Offload.BrawDecode;

/// What --info reports. Width/Height are the processed size at the requested scale (decoded from frame 0 —
/// the SDK's rounding is the truth, not width/scale), Source* the sensor size.
internal sealed record ClipFacts(
    uint Width, uint Height, uint SourceWidth, uint SourceHeight, float Fps, ulong Frames,
    string Timecode, string Camera, string? Gamma, string? Gamut, int? ColorScienceGen);

internal static class InfoJson
{
    /// One line, byte-compatible with the Mac braw-decode for the shared keys ("fps" with 6 decimals);
    /// gamma / gamut / colorScienceGen follow when the SDK reports them (Windows only so far).
    public static string Format(ClipFacts f)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(inv, $"{{\"width\":{f.Width},\"height\":{f.Height},\"sourceWidth\":{f.SourceWidth},\"sourceHeight\":{f.SourceHeight},");
        sb.Append(inv, $"\"fps\":{((double)f.Fps).ToString("F6", inv)},\"frames\":{f.Frames},");
        sb.Append(inv, $"\"timecode\":\"{Escape(f.Timecode)}\",\"camera\":\"{Escape(f.Camera)}\"");
        if (f.Gamma != null) sb.Append(inv, $",\"gamma\":\"{Escape(f.Gamma)}\"");
        if (f.Gamut != null) sb.Append(inv, $",\"gamut\":\"{Escape(f.Gamut)}\"");
        if (f.ColorScienceGen is int gen) sb.Append(inv, $",\"colorScienceGen\":{gen}");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// Like the Mac: quote and backslash escaped, control characters dropped, everything else as is (UTF-8).
    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\');
            if (c >= 0x20) sb.Append(c);
        }
        return sb.ToString();
    }
}

/// One decoded frame waiting for its turn on stdout. Buffers are reused batch after batch.
internal sealed class FrameSlot
{
    public byte[] Data = [];
    public int Length;
    public uint Width, Height;
    public volatile bool Ok;

    /// stdout carries exactly width × height RGB48 pixels; the SDK may pad its buffer at the end.
    public static long ExpectedBytes(uint width, uint height) => (long)width * height * 6;

    /// Copies one processed image; false when the SDK buffer is shorter than a whole frame.
    public unsafe bool Fill(uint width, uint height, void* resource, uint sizeBytes)
    {
        long expected = ExpectedBytes(width, height);
        if (resource == null || expected == 0 || sizeBytes < expected || expected > Array.MaxLength) return false;
        if (Data.Length < expected) Data = new byte[expected];
        new ReadOnlySpan<byte>(resource, (int)expected).CopyTo(Data);
        Length = (int)expected;
        Width = width;
        Height = height;
        Ok = true;
        return true;
    }
}
```

- [ ] **Step 5: Testy, build, Commit**

Run: `dotnet build Offload.sln` → 0 varování; `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~BrawDecodeTests"` → PASS (6 metod, 12 případů).
Řádek VYVOJ: `- Úkol 4 — projekt braw-decode (samostatný bin\braw-decode.exe): argumenty jako na Macu, JSON --info, přesná velikost snímku RGB48`

```powershell
git add windows docs
git commit -m "Windows: braw-decode project (command line, --info JSON, frame bytes)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: COM vrstva — rozhraní Blackmagic RAW a callback

Volání přes vtable (`delegate* unmanaged[Stdcall]`), ne `[ComImport]`/`[GeneratedComInterface]`: snímky drží velké
buffery a C++ nástroj je uvolňuje hned v callbacku — s RCW by uvolnění záviselo na GC a paměť by při tisících
snímků rostla; vtable volání jsou navíc bezpečná pro ořez (trim) a nepotřebují apartmány COM. Spike ověřil sloty,
BSTR, BOOL i předání callbacku.

**Files:**
- Create: `windows/src/Offload.BrawDecode/BrawApi.cs`, `windows/src/Offload.BrawDecode/BrawCallback.cs`
- Create: `windows/tests/Offload.Core.Tests/BrawFacts.cs`
- Modify: `windows/tests/Offload.Core.Tests/BrawDecodeTests.cs`

**Interfaces:**
- Produces (internal):
  - `static class Com { S_OK, S_FALSE, E_FAIL, E_NOINTERFACE, E_POINTER; IID_IUnknown; nint Slot(nint obj, int index); int QueryInterface(nint, Guid, out nint); uint AddRef(nint); uint Release(nint); string? TakeBstr(nint); string Hex(int) }`
  - `static class Iid { Factory, Configuration, Callback }`, `static class FourCC { PipelineCpu, PipelineCuda, PipelineOpenCL, ResourceFormatRgbU16, ClipAttributeColorScienceGen, ClipAttributeGamma, ClipAttributeGamut }`
  - structy `BrawFactory`, `BrawCodec`, `BrawConfiguration`, `BrawClip`, `BrawClipProcessingAttributes`, `BrawJob`, `BrawFrame`, `BrawProcessedImage` (každý `nint` ukazatel + metody podle IDL)
  - `static class BrawRuntime { BrawFactory? CreateFactory(string dllPath, out string? error) }`
  - `interface IBrawCallbackSink { void ReadComplete(nint job, int result, nint frame); void ProcessComplete(nint job, int result, nint processedImage); }`
  - `sealed class BrawCallback(IBrawCallbackSink) : IDisposable { nint Pointer; int RefCount }`
  - testy: `BrawLibraryFactAttribute` (knihovna nalezena, jinak skip; `static string? Library`), `BrawSampleFactAttribute(bool needsFfmpeg = false)` (`OFFLOAD_BRAW_SAMPLE`, jinak skip; `static string? Sample`)

- [ ] **Step 1: Atributy testů** — `windows/tests/Offload.Core.Tests/BrawFacts.cs`:

```csharp
using Offload.Core;

namespace Offload.Core.Tests;

/// BlackmagicRawAPI.dll somewhere braw-decode looks (next to the tests, SDK, Player, DaVinci Resolve).
public sealed class BrawLibraryFactAttribute : FactAttribute
{
    public static string? Library => BrawLibrary.Find(AppContext.BaseDirectory.TrimEnd('\\'));

    public BrawLibraryFactAttribute()
    {
        if (Library == null) Skip = "BlackmagicRawAPI.dll not found (Blackmagic RAW SDK / Player / DaVinci Resolve)";
    }
}

/// A real .braw clip: set OFFLOAD_BRAW_SAMPLE to its full path (see windows\README.md, "Vzorek BRAW").
public sealed class BrawSampleFactAttribute : FactAttribute
{
    public static string? Sample =>
        Environment.GetEnvironmentVariable("OFFLOAD_BRAW_SAMPLE") is string p && File.Exists(p) ? p : null;

    public BrawSampleFactAttribute(bool needsFfmpeg = false)
    {
        if (BrawLibraryFactAttribute.Library == null) Skip = "BlackmagicRawAPI.dll not found";
        else if (Sample == null) Skip = "OFFLOAD_BRAW_SAMPLE not set or the file does not exist";
        else if (needsFfmpeg && TestMedia.Ffmpeg == null) Skip = "ffmpeg not found";
    }
}
```

- [ ] **Step 2: Padající testy** — do třídy `BrawDecodeTests` přidat:

```csharp
    // --- native callback object (no SDK needed) ---

    sealed class RecordingSink : IBrawCallbackSink
    {
        public readonly List<string> Calls = [];
        public bool Throw;
        public void ReadComplete(nint job, int result, nint frame)
        {
            Calls.Add($"read {job} {result} {frame}");
            if (Throw) throw new InvalidOperationException("boom");
        }
        public void ProcessComplete(nint job, int result, nint image) => Calls.Add($"process {job} {result} {image}");
    }

    [Fact]
    public unsafe void Callback_IsAComObjectTheSdkCanCall()
    {
        var sink = new RecordingSink();
        using var cb = new BrawCallback(sink);
        nint p = cb.Pointer;

        Assert.Equal(Com.S_OK, Com.QueryInterface(p, new Guid("E9F98FAC-33DB-4A65-BB94-8A82B027AED0"), out var q));
        Assert.Equal(p, q);
        Assert.Equal(2, cb.RefCount);
        Assert.Equal(1u, Com.Release(p));
        Assert.Equal(Com.S_OK, Com.QueryInterface(p, Com.IID_IUnknown, out q));
        Com.Release(q);
        Assert.Equal(Com.E_NOINTERFACE, Com.QueryInterface(p, Guid.NewGuid(), out q));
        Assert.Equal(0, q);
        Assert.Equal(2u, Com.AddRef(p));
        Assert.Equal(1u, Com.Release(p));

        ((delegate* unmanaged[Stdcall]<nint, nint, int, nint, void>)Com.Slot(p, 3))(p, 11, 0, 22);
        ((delegate* unmanaged[Stdcall]<nint, nint, int, nint, void>)Com.Slot(p, 5))(p, 33, -1, 44);
        ((delegate* unmanaged[Stdcall]<nint, nint, int, void>)Com.Slot(p, 4))(p, 1, 0);          // DecodeComplete
        ((delegate* unmanaged[Stdcall]<nint, nint, float, void>)Com.Slot(p, 6))(p, 1, 0.5f);     // TrimProgress
        ((delegate* unmanaged[Stdcall]<nint, nint, int, void>)Com.Slot(p, 7))(p, 1, 0);          // TrimComplete
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, uint, nint, void>)Com.Slot(p, 8))(p, 0, 0, 1, 0);
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, uint, nint, void>)Com.Slot(p, 9))(p, 0, 0, 1, 0);
        ((delegate* unmanaged[Stdcall]<nint, nint, int, void>)Com.Slot(p, 10))(p, 0, 0);         // PreparePipelineComplete
        Assert.Equal(["read 11 0 22", "process 33 -1 44"], sink.Calls);

        // An exception in the sink must never unwind into the SDK's thread.
        sink.Throw = true;
        ((delegate* unmanaged[Stdcall]<nint, nint, int, nint, void>)Com.Slot(p, 3))(p, 1, 0, 2);
        Assert.Equal(3, sink.Calls.Count);
    }

    // --- with the real library (DaVinci Resolve on this PC), still no clip ---

    [BrawLibraryFact]
    public void Sdk_FactoryCodecConfigurationCallback()
    {
        var factory = BrawRuntime.CreateFactory(BrawLibraryFactAttribute.Library!, out var error);
        Assert.True(factory.HasValue, error);
        var f = factory.Value;
        Assert.Equal(Com.S_OK, f.CreateCodec(out var codec));
        Assert.Equal(Com.S_OK, codec.QueryConfiguration(out var config));
        Assert.Matches(@"^\d+\.\d+", config.GetCameraSupportVersion());
        Assert.NotNull(config.GetVersion());
        Assert.Equal(Com.S_OK, config.IsPipelineSupported(FourCC.PipelineCpu, out var cpu));
        Assert.True(cpu);
        Assert.Equal(Com.S_OK, config.SetPipeline(FourCC.PipelineCpu));
        Assert.Equal(Com.S_OK, config.GetPipeline(out var pipeline));
        Assert.Equal(FourCC.PipelineCpu, pipeline);

        using var cb = new BrawCallback(new RecordingSink());
        Assert.Equal(Com.S_OK, codec.SetCallback(cb.Pointer));
        Assert.True(cb.RefCount >= 2);   // the SDK keeps a reference
        Assert.Equal(Com.E_FAIL, codec.OpenClip(@"C:\nonexistent\A001.braw", out var clip));
        Assert.Equal(0, clip.Ptr);
        Assert.Equal(Com.S_OK, codec.FlushJobs());
        Assert.Equal(Com.S_OK, codec.SetCallback(0));
        Assert.Equal(1, cb.RefCount);

        Com.Release(config.Ptr);
        Assert.Equal(0u, Com.Release(codec.Ptr));
        Assert.Equal(0u, Com.Release(f.Ptr));
    }
```

- [ ] **Step 3: Ověřit selhání** — Run: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~BrawDecodeTests"` → nesestaví se (`BrawCallback`, `Com`, `BrawRuntime`).

- [ ] **Step 4: Implementace**

`windows/src/Offload.BrawDecode/BrawApi.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Offload.BrawDecode;

// Blackmagic RAW API for Windows, called through COM vtables with function pointers.
//
// Each wrapper is a raw interface pointer; method slots follow the SDK's IDL order (0–2 = IUnknown). The Mac header
// in mac/Tools/braw-decode/BlackmagicRawAPI.h has the same interfaces, IIDs and method order — on Windows strings
// are BSTR (caller frees with SysFreeString), bool is BOOL (4 bytes), Variant is the OLE VARIANT. Only the methods
// braw-decode needs are declared. References are counted by hand exactly like the C++ tool (no RCWs, no GC timing):
// whoever receives an interface from a Create*/Clone*/QueryInterface call releases it.
//
// The callback convention is __stdcall (STDMETHODCALLTYPE); on x64 it is the one and only convention.

internal static unsafe class Com
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int E_POINTER = unchecked((int)0x80004003);

    public static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");

    public static nint Slot(nint obj, int index) => (*(nint**)obj)[index];

    public static int QueryInterface(nint obj, Guid iid, out nint result)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(obj, 0))(obj, &iid, &p);
        result = p;
        return hr;
    }

    public static uint AddRef(nint obj) => ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 1))(obj);

    public static uint Release(nint obj) => obj == 0 ? 0 : ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 2))(obj);

    /// Takes ownership of a BSTR the API returned.
    public static string? TakeBstr(nint bstr)
    {
        if (bstr == 0) return null;
        try { return Marshal.PtrToStringBSTR(bstr); }
        finally { Marshal.FreeBSTR(bstr); }
    }

    public static string Hex(int hr) => "0x" + hr.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
}

internal static class Iid
{
    public static readonly Guid Factory = new("78DEEB84-98C9-434A-B7E5-7AACC2988399");
    public static readonly Guid Configuration = new("267E9866-FB40-4BFB-8BF8-96EA3F7DA36E");
    public static readonly Guid Callback = new("E9F98FAC-33DB-4A65-BB94-8A82B027AED0");
}

/// Four-character codes from the SDK enums.
internal static class FourCC
{
    public const uint PipelineCpu = 0x63707562;        // 'cpub'
    public const uint PipelineCuda = 0x63756461;       // 'cuda'
    public const uint PipelineOpenCL = 0x6F70636C;     // 'opcl'
    public const uint ResourceFormatRgbU16 = 0x3136696C; // '16il' — interleaved RGB 16-bit = ffmpeg rgb48le
    public const uint ClipAttributeColorScienceGen = 0x6373676E; // 'csgn' u16
    public const uint ClipAttributeGamma = 0x67616D61;           // 'gama' string
    public const uint ClipAttributeGamut = 0x67616D74;           // 'gamt' string
}

/// IBlackmagicRawFactory: 3 CreateCodec, 4 CreatePipelineIterator, 5 CreatePipelineDeviceIterator, 6 CreateClipGeometry.
internal readonly unsafe struct BrawFactory(nint ptr)
{
    public nint Ptr => ptr;

    public int CreateCodec(out BrawCodec codec)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(ptr, 3))(ptr, &p);
        codec = new BrawCodec(p);
        return hr;
    }
}

/// IBlackmagicRaw: 3 OpenClip, 4 OpenClipWithGeometry, 5 SetCallback, 6 PreparePipeline, 7 PreparePipelineForDevice, 8 FlushJobs.
internal readonly unsafe struct BrawCodec(nint ptr)
{
    public nint Ptr => ptr;

    public int OpenClip(string fileName, out BrawClip clip)
    {
        nint bstr = Marshal.StringToBSTR(fileName);
        nint p = 0;
        try
        {
            int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Com.Slot(ptr, 3))(ptr, bstr, &p);
            clip = new BrawClip(p);
            return hr;
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }

    public int SetCallback(nint callback) => ((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(ptr, 5))(ptr, callback);

    /// Blocks until every submitted job (and its callbacks) has finished.
    public int FlushJobs() => ((delegate* unmanaged[Stdcall]<nint, int>)Com.Slot(ptr, 8))(ptr);

    public int QueryConfiguration(out BrawConfiguration config)
    {
        int hr = Com.QueryInterface(ptr, Iid.Configuration, out var p);
        config = new BrawConfiguration(p);
        return hr;
    }
}

/// IBlackmagicRawConfiguration (read on the first OpenClip): 3 SetPipeline, 4 GetPipeline, 5 IsPipelineSupported,
/// 6 SetCPUThreads, 7 GetCPUThreads, 8 GetMaxCPUThreadCount, 9 SetWriteMetadataPerFrame, 10 GetWriteMetadataPerFrame,
/// 11 SetFromDevice, 12 GetVersion, 13 GetCameraSupportVersion.
internal readonly unsafe struct BrawConfiguration(nint ptr)
{
    public nint Ptr => ptr;

    public int SetPipeline(uint pipeline) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)Com.Slot(ptr, 3))(ptr, pipeline, 0, 0);

    public int GetPipeline(out uint pipeline)
    {
        uint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint*, nint*, nint*, int>)Com.Slot(ptr, 4))(ptr, &p, null, null);
        pipeline = p;
        return hr;
    }

    public int IsPipelineSupported(uint pipeline, out bool supported)
    {
        int b = 0; // BOOL
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, int*, int>)Com.Slot(ptr, 5))(ptr, pipeline, &b);
        supported = b != 0;
        return hr;
    }

    public string? GetVersion() => Text(12);

    public string? GetCameraSupportVersion() => Text(13);

    string? Text(int slot)
    {
        nint bstr = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(ptr, slot))(ptr, &bstr);
        var s = Com.TakeBstr(bstr);
        return hr == Com.S_OK ? s : null;
    }
}

/// IBlackmagicRawClip: 3 GetWidth, 4 GetHeight, 5 GetFrameRate, 6 GetFrameCount, 7 GetTimecodeForFrame,
/// 8 GetMetadataIterator, 9 GetMetadata, 10 SetMetadata, 11 GetCameraType, 12 CloneClipProcessingAttributes,
/// 13 GetMulticardFileCount, 14 IsMulticardFilePresent, 15 GetSidecarFileAttached, 16 SaveSidecarFile,
/// 17 ReloadSidecarFile, 18 CreateJobReadFrame, 19 CreateJobTrim, 20 CloneWithGeometry.
internal readonly unsafe struct BrawClip(nint ptr)
{
    public nint Ptr => ptr;

    public uint Width => U32(3);
    public uint Height => U32(4);

    public float FrameRate
    {
        get
        {
            float v = 0;
            ((delegate* unmanaged[Stdcall]<nint, float*, int>)Com.Slot(ptr, 5))(ptr, &v);
            return v;
        }
    }

    public ulong FrameCount
    {
        get
        {
            ulong v = 0;
            ((delegate* unmanaged[Stdcall]<nint, ulong*, int>)Com.Slot(ptr, 6))(ptr, &v);
            return v;
        }
    }

    public string? TimecodeForFrame(ulong frame)
    {
        nint bstr = 0;
        ((delegate* unmanaged[Stdcall]<nint, ulong, nint*, int>)Com.Slot(ptr, 7))(ptr, frame, &bstr);
        return Com.TakeBstr(bstr);
    }

    public string? CameraType
    {
        get
        {
            nint bstr = 0;
            ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(ptr, 11))(ptr, &bstr);
            return Com.TakeBstr(bstr);
        }
    }

    public int CloneClipProcessingAttributes(out BrawClipProcessingAttributes attributes)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(ptr, 12))(ptr, &p);
        attributes = new BrawClipProcessingAttributes(p);
        return hr;
    }

    public int CreateJobReadFrame(ulong frame, out BrawJob job)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, ulong, nint*, int>)Com.Slot(ptr, 18))(ptr, frame, &p);
        job = new BrawJob(p);
        return hr;
    }

    uint U32(int slot)
    {
        uint v = 0;
        ((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(ptr, slot))(ptr, &v);
        return v;
    }
}

/// IBlackmagicRawClipProcessingAttributes: 3 GetClipAttribute, 4 SetClipAttribute, 5 GetClipAttributeRange,
/// 6 GetClipAttributeList, 7 GetISOList, 8 GetPost3DLUT.
internal readonly unsafe struct BrawClipProcessingAttributes(nint ptr)
{
    public nint Ptr => ptr;

    /// String attribute (gamma, gamut); null when missing or not a string.
    public string? Text(uint attribute)
    {
        ComVariant v = default;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, uint, ComVariant*, int>)Com.Slot(ptr, 3))(ptr, attribute, &v) != Com.S_OK) return null;
            return v.VarType == VarEnum.VT_BSTR ? v.As<string>() : null;
        }
        finally
        {
            v.Dispose();
        }
    }

    /// Integer attribute (colour science gen is a u16); null when missing or not an integer.
    public int? Number(uint attribute)
    {
        ComVariant v = default;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, uint, ComVariant*, int>)Com.Slot(ptr, 3))(ptr, attribute, &v) != Com.S_OK) return null;
            return v.VarType switch
            {
                VarEnum.VT_UI2 => v.As<ushort>(),
                VarEnum.VT_I2 => v.As<short>(),
                VarEnum.VT_UI1 => v.As<byte>(),
                VarEnum.VT_I4 or VarEnum.VT_INT => v.As<int>(),
                VarEnum.VT_UI4 or VarEnum.VT_UINT => (int)v.As<uint>(),
                _ => null,
            };
        }
        finally
        {
            v.Dispose();
        }
    }
}

/// IBlackmagicRawJob: 3 Submit, 4 Abort, 5 SetUserData, 6 GetUserData.
internal readonly unsafe struct BrawJob(nint ptr)
{
    public nint Ptr => ptr;

    public int Submit() => ((delegate* unmanaged[Stdcall]<nint, int>)Com.Slot(ptr, 3))(ptr);

    public int SetUserData(nint data) => ((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(ptr, 5))(ptr, data);

    public nint GetUserData()
    {
        nint v = 0;
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(ptr, 6))(ptr, &v);
        return v;
    }
}

/// IBlackmagicRawFrame: 3 GetFrameIndex, 4 GetTimecode, 5 GetMetadataIterator, 6 GetMetadata, 7 SetMetadata,
/// 8 CloneFrameProcessingAttributes, 9 SetResolutionScale, 10 GetResolutionScale, 11 SetResourceFormat,
/// 12 GetResourceFormat, 13 GetSensorRate, 14 CreateJobDecodeAndProcessFrame.
internal readonly unsafe struct BrawFrame(nint ptr)
{
    public int SetResolutionScale(Scale scale) => ((delegate* unmanaged[Stdcall]<nint, uint, int>)Com.Slot(ptr, 9))(ptr, (uint)scale);

    public int SetResourceFormat(uint format) => ((delegate* unmanaged[Stdcall]<nint, uint, int>)Com.Slot(ptr, 11))(ptr, format);

    /// With the clip's own processing attributes (null overrides) — frames come out in the gamma they were shot in.
    public int CreateJobDecodeAndProcessFrame(out BrawJob job)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)Com.Slot(ptr, 14))(ptr, 0, 0, &p);
        job = new BrawJob(p);
        return hr;
    }
}

/// IBlackmagicRawProcessedImage: 3 GetWidth, 4 GetHeight, 5 GetResource, 6 GetResourceType, 7 GetResourceFormat,
/// 8 GetResourceSizeBytes, 9 GetResourceContextAndCommandQueue.
internal readonly unsafe struct BrawProcessedImage(nint ptr)
{
    public uint Width => U32(3);
    public uint Height => U32(4);
    public uint SizeBytes => U32(8);

    public void* Resource
    {
        get
        {
            void* p = null;
            ((delegate* unmanaged[Stdcall]<nint, void**, int>)Com.Slot(ptr, 5))(ptr, &p);
            return p;
        }
    }

    uint U32(int slot)
    {
        uint v = 0;
        ((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(ptr, slot))(ptr, &v);
        return v;
    }
}

/// Loads BlackmagicRawAPI.dll once per process (never unloaded: SDK worker threads live inside it) and creates factories.
internal static unsafe class BrawRuntime
{
    static readonly Dictionary<string, nint> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// null + error text when the library cannot be loaded or has no factory export.
    public static BrawFactory? CreateFactory(string dllPath, out string? error)
    {
        error = null;
        nint create;
        lock (Loaded)
        {
            if (!Loaded.TryGetValue(dllPath, out var lib))
            {
                if (!NativeLibrary.TryLoad(dllPath, out lib))
                {
                    error = $"cannot load {dllPath}";
                    return null;
                }
                Loaded[dllPath] = lib;
            }
            if (!NativeLibrary.TryGetExport(lib, "CreateBlackmagicRawFactoryInstance", out create))
            {
                error = $"{dllPath} has no CreateBlackmagicRawFactoryInstance";
                return null;
            }
        }
        nint factory = ((delegate* unmanaged<nint>)create)();
        if (factory == 0)
        {
            error = "CreateBlackmagicRawFactoryInstance returned null";
            return null;
        }
        return new BrawFactory(factory);
    }
}
```

`windows/src/Offload.BrawDecode/BrawCallback.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Offload.BrawDecode;

/// What the SDK reports back. Called on SDK worker threads, possibly in parallel; must not throw.
internal interface IBrawCallbackSink
{
    void ReadComplete(nint job, int result, nint frame);
    void ProcessComplete(nint job, int result, nint processedImage);
}

/// A native IBlackmagicRawCallback object for IBlackmagicRaw::SetCallback: a vtable of [UnmanagedCallersOnly]
/// functions plus a GCHandle to the managed sink. Vtable order (IDL): 0 QueryInterface, 1 AddRef, 2 Release,
/// 3 ReadComplete, 4 DecodeComplete, 5 ProcessComplete, 6 TrimProgress, 7 TrimComplete,
/// 8 SidecarMetadataParseWarning, 9 SidecarMetadataParseError, 10 PreparePipelineComplete.
/// Dispose only after the codec that holds it has been released.
internal sealed unsafe class BrawCallback : IDisposable
{
    struct Native
    {
        public nint* Vtbl;
        public nint Sink;   // GCHandle
        public int Refs;
    }

    static readonly nint* Vtbl = CreateVtbl();
    Native* native;

    public BrawCallback(IBrawCallbackSink sink)
    {
        native = (Native*)NativeMemory.AllocZeroed((nuint)sizeof(Native));
        native->Vtbl = Vtbl;
        native->Sink = GCHandle.ToIntPtr(GCHandle.Alloc(sink));
        native->Refs = 1;
    }

    /// The IBlackmagicRawCallback* to hand to SetCallback.
    public nint Pointer => (nint)native;

    /// References held by the SDK plus our own one (for tests and diagnostics).
    public int RefCount => native->Refs;

    public void Dispose()
    {
        if (native == null) return;
        GCHandle.FromIntPtr(native->Sink).Free();
        NativeMemory.Free(native);
        native = null;
    }

    static nint* CreateVtbl()
    {
        var v = (nint*)NativeMemory.Alloc(11, (nuint)sizeof(nint));  // lives as long as the process
        v[0] = (nint)(delegate* unmanaged[Stdcall]<Native*, Guid*, nint*, int>)&QueryInterface;
        v[1] = (nint)(delegate* unmanaged[Stdcall]<Native*, uint>)&AddRef;
        v[2] = (nint)(delegate* unmanaged[Stdcall]<Native*, uint>)&Release;
        v[3] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, int, nint, void>)&ReadComplete;
        v[4] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, int, void>)&JobDone;          // DecodeComplete
        v[5] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, int, nint, void>)&ProcessComplete;
        v[6] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, float, void>)&TrimProgress;
        v[7] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, int, void>)&JobDone;          // TrimComplete
        v[8] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, nint, uint, nint, void>)&SidecarMessage; // Warning
        v[9] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, nint, uint, nint, void>)&SidecarMessage; // Error
        v[10] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, int, void>)&JobDone;         // PreparePipelineComplete
        return v;
    }

    static IBrawCallbackSink SinkOf(Native* self) => (IBrawCallbackSink)GCHandle.FromIntPtr(self->Sink).Target!;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static int QueryInterface(Native* self, Guid* iid, nint* result)
    {
        if (result == null) return Com.E_POINTER;
        if (*iid == Com.IID_IUnknown || *iid == Iid.Callback)
        {
            Interlocked.Increment(ref self->Refs);
            *result = (nint)self;
            return Com.S_OK;
        }
        *result = 0;
        return Com.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static uint AddRef(Native* self) => (uint)Interlocked.Increment(ref self->Refs);

    /// The memory is ours (Dispose), not freed at zero: the SDK may drop its reference before we are done.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static uint Release(Native* self) => (uint)Math.Max(0, Interlocked.Decrement(ref self->Refs));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static void ReadComplete(Native* self, nint job, int result, nint frame)
    {
        try { SinkOf(self).ReadComplete(job, result, frame); }
        catch (Exception e) { Console.Error.WriteLine($"braw-decode: {e.Message}"); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static void ProcessComplete(Native* self, nint job, int result, nint image)
    {
        try { SinkOf(self).ProcessComplete(job, result, image); }
        catch (Exception e) { Console.Error.WriteLine($"braw-decode: {e.Message}"); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static void JobDone(Native* self, nint job, int result) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static void TrimProgress(Native* self, nint job, float progress) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static void SidecarMessage(Native* self, nint clip, nint fileName, uint line, nint info) { }
}
```

Kontrola proti IDL: pořadí slotů v komentářích odpovídá `mac/Tools/braw-decode/BlackmagicRawAPI.h` (IBlackmagicRaw,
…Factory, …Configuration, …Clip, …ClipProcessingAttributes, …Job, …Frame, …ProcessedImage, …Callback) — při review
projít řádek po řádku.

- [ ] **Step 5: Testy, Commit**

Run: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~BrawDecodeTests"` → PASS; na tomto PC běží
i `Sdk_FactoryCodecConfigurationCallback` (knihovna z DaVinci Resolve), bez knihovny je přeskočený.
Řádek VYVOJ: `- Úkol 5 — COM vrstva Blackmagic RAW v C# (volání přes vtable, BSTR/BOOL/VARIANT jako ve Windows SDK, callback jako nativní COM objekt); ověřeno s knihovnou z DaVinci Resolve`

```powershell
git add windows docs
git commit -m "Windows: Blackmagic RAW COM interop (vtable calls) and native callback object" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Dekodér — `--info` a snímky na stdout

**Files:**
- Create: `windows/src/Offload.BrawDecode/Decoder.cs`
- Modify: `windows/src/Offload.BrawDecode/Program.cs` (finální)
- Modify: `windows/tests/Offload.Core.Tests/BrawDecodeTests.cs`
- Create: `windows/tests/Offload.Core.Tests/BrawSampleTests.cs`

**Interfaces:**
- Produces: `static int Decoder.Run(IReadOnlyList<string> args, Stream stdout, TextWriter stderr, IReadOnlyList<string>? libraryDirs = null)` — kódy 0 OK / 2 použití / 3 knihovna nebo kodek / 4 klip nejde otevřít / 5 snímek selhal; `sealed class FrameSink : IBrawCallbackSink`.
- Consumes: Task 1 `BrawLibrary`, Task 4 `Options`/`InfoJson`/`FrameSlot`, Task 5 COM vrstva.

- [ ] **Step 1: Padající testy** — do `BrawDecodeTests` přidat:

```csharp
    static (int Code, byte[] Stdout, string Stderr) Run(IReadOnlyList<string> args, IReadOnlyList<string>? libraryDirs = null)
    {
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();
        int code = Decoder.Run(args, stdout, stderr, libraryDirs);
        return (code, stdout.ToArray(), stderr.ToString());
    }

    [Fact]
    public void Run_Usage_Exit2()
    {
        var r = Run(["--info"]);
        Assert.Equal(2, r.Code);
        Assert.StartsWith("usage: braw-decode", r.Stderr);
    }

    [Fact]
    public void Run_NoLibrary_Exit3()
    {
        using var dir = new TempDir();
        var r = Run(["--info", "a.braw"], [dir.Path]);
        Assert.Equal(3, r.Code);
        Assert.Contains("BlackmagicRawAPI.dll not found", r.Stderr);
        Assert.Empty(r.Stdout);
    }

    [BrawLibraryFact]
    public void Run_MissingClip_Exit4()
    {
        var r = Run(["--info", @"C:\nonexistent\A001.braw"]);
        Assert.Equal(4, r.Code);
        Assert.Contains(@"cannot open C:\nonexistent\A001.braw (0x80004005)", r.Stderr);
        Assert.Empty(r.Stdout);
    }
```

a `windows/tests/Offload.Core.Tests/BrawSampleTests.cs` (bez vzorku se přeskočí):

```csharp
using System.Diagnostics;
using Offload.BrawDecode;

namespace Offload.Core.Tests;

/// Need a real clip: OFFLOAD_BRAW_SAMPLE=<path to .braw> (skipped otherwise; see windows\README.md).
public class BrawSampleTests
{
    static string Sample => BrawSampleFactAttribute.Sample!;

    static (int Code, byte[] Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();
        int code = Decoder.Run(args, stdout, stderr);
        return (code, stdout.ToArray(), stderr.ToString());
    }

    static BrawInfo Info(string scale)
    {
        var r = Run("--info", "--scale", scale, Sample);
        Assert.True(r.Code == 0, r.Stderr);
        return BrawBackend.ParseInfo(System.Text.Encoding.UTF8.GetString(r.Stdout));
    }

    [BrawSampleFact]
    public void Info_DescribesTheClip()
    {
        var i = Info("eighth");
        Assert.True(i.SourceWidth >= 1920 && i.SourceHeight >= 1080, $"{i.SourceWidth}x{i.SourceHeight}");
        // The processed size at 1/8 is about a eighth of the sensor (the SDK rounds; never bigger than a quarter).
        Assert.InRange(i.Width, i.SourceWidth / 9, i.SourceWidth / 4);
        Assert.True(i.Fps > 0 && i.Frames > 0, $"{i.Fps} fps, {i.Frames} frames");
        Assert.Matches(@"^\d\d:\d\d:\d\d[:;]\d\d$", i.Timecode);
        Assert.False(string.IsNullOrEmpty(i.Camera));
        // Spike risk: gamma/gamut come through the OLE VARIANT — a sample proves the layout.
        Assert.False(string.IsNullOrEmpty(i.Gamma), "gamma missing");
        Assert.False(string.IsNullOrEmpty(i.Gamut), "gamut missing");
        Assert.InRange(i.ColorScienceGen ?? 0, 4, 9);
        var full = Info("full");
        Assert.Equal((i.SourceWidth, i.SourceHeight), (full.Width, full.Height));
    }

    [BrawSampleFact]
    public void Frames_ExactRgb48Bytes_InOrder()
    {
        var i = Info("eighth");
        long frame = (long)i.Width * i.Height * 6;
        var two = Run("--scale", "eighth", "--start", "0", "--count", "2", Sample);
        Assert.True(two.Code == 0, two.Stderr);
        Assert.Equal(2 * frame, two.Stdout.LongLength);
        var second = Run("--scale", "eighth", "--start", "1", "--count", "1", Sample);
        Assert.Equal(two.Stdout[(int)frame..], second.Stdout);   // the same frame decodes to the same bytes
        Assert.Contains(two.Stdout, b => b != 0);                // not a black buffer
    }

    [BrawSampleFact]
    public void Frames_ClampedAtTheEnd()
    {
        var i = Info("eighth");
        long frame = (long)i.Width * i.Height * 6;
        var last = Run("--scale", "eighth", "--start", (i.Frames - 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "--count", "5", Sample);
        Assert.True(last.Code == 0, last.Stderr);
        Assert.Equal(frame, last.Stdout.LongLength);
        var past = Run("--scale", "eighth", "--start", i.Frames.ToString(System.Globalization.CultureInfo.InvariantCulture), Sample);
        Assert.Equal((0, 0), (past.Code, past.Stdout.Length));
    }

    /// The real exe: stdout is binary (no text translation, nothing but frames).
    [BrawSampleFact]
    public void Exe_StdoutIsBinary()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "braw-decode.exe");
        var i = Info("eighth");
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "--scale", "eighth", "--count", "3", Sample }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, err.Result);
        Assert.Equal(3L * i.Width * i.Height * 6, ms.Length);
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~Braw"` → nesestaví se (`Decoder`).

- [ ] **Step 3: Implementace** — `windows/src/Offload.BrawDecode/Decoder.cs`:

```csharp
using System.Text;
using Offload.Core;

namespace Offload.BrawDecode;

/// braw-decode itself: the C++ Mac tool's flow on the Windows COM API. Exit codes as on the Mac:
/// 0 OK (also when the reader closed the pipe), 2 usage, 3 library/codec missing, 4 clip cannot be opened,
/// 5 a frame failed to decode.
internal static class Decoder
{
    /// Frames decoded in parallel by the SDK and then written in order.
    const int Batch = 8;

    /// `libraryDirs` null = BrawLibrary.SearchDirs next to this exe (tests pass their own).
    public static int Run(IReadOnlyList<string> args, Stream stdout, TextWriter stderr, IReadOnlyList<string>? libraryDirs = null)
    {
        var o = Options.Parse(args);
        if (o == null)
        {
            stderr.WriteLine(Options.Usage);
            return 2;
        }
        var dll = BrawLibrary.Find(libraryDirs ?? BrawLibrary.SearchDirs(AppContext.BaseDirectory.TrimEnd('\\')));
        if (dll == null)
        {
            stderr.WriteLine("braw-decode: BlackmagicRawAPI.dll not found (install Blackmagic RAW Player, the Blackmagic RAW SDK or DaVinci Resolve)");
            return 3;
        }
        if (BrawRuntime.CreateFactory(dll, out var error) is not BrawFactory factory)
        {
            stderr.WriteLine($"braw-decode: {error}");
            return 3;
        }

        var sink = new FrameSink(o.Scale, stderr);
        using var callback = new BrawCallback(sink);
        BrawCodec codec = default;
        BrawClip clip = default;
        try
        {
            int hr = factory.CreateCodec(out codec);
            if (hr != Com.S_OK)
            {
                stderr.WriteLine($"braw-decode: CreateCodec failed ({Com.Hex(hr)})");
                return 3;
            }
            // The configuration is read on the first OpenClip, so the pipeline is set before it (CPU like the Mac).
            if (codec.QueryConfiguration(out var config) == Com.S_OK)
            {
                config.SetPipeline(FourCC.PipelineCpu);
                Com.Release(config.Ptr);
            }
            hr = codec.OpenClip(o.Path, out clip);
            if (hr != Com.S_OK)
            {
                stderr.WriteLine($"braw-decode: cannot open {o.Path} ({Com.Hex(hr)})");
                return 4;
            }
            hr = codec.SetCallback(callback.Pointer);
            if (hr != Com.S_OK)
            {
                stderr.WriteLine($"braw-decode: SetCallback failed ({Com.Hex(hr)})");
                return 3;
            }
            int code = o.Info ? Info(codec, clip, sink, stdout) : Frames(o, codec, clip, sink, stdout, stderr);
            return code != 0 ? code : sink.Errors > 0 ? 5 : 0;
        }
        finally
        {
            // No job may still be running (and calling back) when the clip, the codec and the callback go away.
            if (codec.Ptr != 0) codec.FlushJobs();
            Com.Release(clip.Ptr);
            if (codec.Ptr != 0)
            {
                codec.SetCallback(0);
                Com.Release(codec.Ptr);
            }
            Com.Release(factory.Ptr);
        }
    }

    static int Info(BrawCodec codec, BrawClip clip, FrameSink sink, Stream stdout)
    {
        ulong frames = clip.FrameCount;
        var slots = new[] { new FrameSlot() };
        // Decode frame 0 at the requested scale: the processed size is the truth, not width / scale.
        if (frames > 0)
        {
            sink.Begin(slots, 0);
            Submit(clip, 0, sink);
            codec.FlushJobs();
        }
        string? gamma = null, gamut = null;
        int? gen = null;
        if (clip.CloneClipProcessingAttributes(out var attrs) == Com.S_OK && attrs.Ptr != 0)
        {
            gamma = attrs.Text(FourCC.ClipAttributeGamma);
            gamut = attrs.Text(FourCC.ClipAttributeGamut);
            gen = attrs.Number(FourCC.ClipAttributeColorScienceGen);
            Com.Release(attrs.Ptr);
        }
        var s = slots[0];
        var facts = new ClipFacts(
            s.Ok ? s.Width : clip.Width, s.Ok ? s.Height : clip.Height, clip.Width, clip.Height, clip.FrameRate, frames,
            clip.TimecodeForFrame(0) ?? "", clip.CameraType ?? "", gamma, gamut, gen);
        var bytes = Encoding.UTF8.GetBytes(InfoJson.Format(facts));
        Write(stdout, bytes, bytes.Length);
        return 0;
    }

    static int Frames(Options o, BrawCodec codec, BrawClip clip, FrameSink sink, Stream stdout, TextWriter stderr)
    {
        var (first, end) = o.Range(clip.FrameCount);
        var slots = Enumerable.Range(0, Batch).Select(_ => new FrameSlot()).ToArray();
        for (ulong b = first; b < end; b += Batch)
        {
            int n = (int)Math.Min((ulong)Batch, end - b);
            sink.Begin(slots, b);
            for (int i = 0; i < n; i++) Submit(clip, b + (ulong)i, sink);
            codec.FlushJobs();
            for (int i = 0; i < n; i++)
            {
                if (!slots[i].Ok)
                {
                    stderr.WriteLine($"braw-decode: frame {b + (ulong)i} failed");
                    return 5;
                }
                if (!Write(stdout, slots[i].Data, slots[i].Length)) return 0;  // ffmpeg closed the pipe: done
            }
        }
        return 0;
    }

    static void Submit(BrawClip clip, ulong index, FrameSink sink)
    {
        if (clip.CreateJobReadFrame(index, out var job) != Com.S_OK || job.Ptr == 0)
        {
            sink.Fail($"cannot read frame {index}");
            return;
        }
        job.SetUserData((nint)index);
        if (job.Submit() != Com.S_OK)
        {
            Com.Release(job.Ptr);
            sink.Fail($"cannot submit frame {index}");
        }
    }

    /// false when the reader went away (broken pipe) — not an error, ffmpeg simply has what it wanted.
    static bool Write(Stream stdout, byte[] data, int length)
    {
        try
        {
            stdout.Write(data, 0, length);
            stdout.Flush();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

/// Receives the SDK callbacks: read → set format/scale → decode+process job → copy into the frame's slot.
internal sealed unsafe class FrameSink(Scale scale, TextWriter stderr) : IBrawCallbackSink
{
    FrameSlot[] slots = [];
    ulong first;
    int errors;

    public int Errors => Volatile.Read(ref errors);

    /// Called between batches only (no jobs in flight).
    public void Begin(FrameSlot[] batch, ulong firstFrame)
    {
        foreach (var s in batch) s.Ok = false;
        slots = batch;
        first = firstFrame;
    }

    public void Fail(string message)
    {
        Interlocked.Increment(ref errors);
        lock (stderr) stderr.WriteLine($"braw-decode: {message}");
    }

    public void ReadComplete(nint readJob, int result, nint framePtr)
    {
        var job = new BrawJob(readJob);
        BrawJob decode = default;
        try
        {
            nint user = job.GetUserData();
            var frame = new BrawFrame(framePtr);
            if (result == Com.S_OK) result = frame.SetResourceFormat(FourCC.ResourceFormatRgbU16);
            if (result == Com.S_OK) result = frame.SetResolutionScale(scale);
            if (result == Com.S_OK) result = frame.CreateJobDecodeAndProcessFrame(out decode);
            if (result == Com.S_OK)
            {
                decode.SetUserData(user);
                result = decode.Submit();
            }
            if (result != Com.S_OK)
            {
                Com.Release(decode.Ptr);
                Fail($"read/decode setup failed ({Com.Hex(result)})");
            }
        }
        finally
        {
            Com.Release(readJob);
        }
    }

    public void ProcessComplete(nint processJob, int result, nint imagePtr)
    {
        try
        {
            ulong index = (ulong)new BrawJob(processJob).GetUserData();
            var batch = slots;
            if (result != Com.S_OK || index < first || index - first >= (ulong)batch.Length)
            {
                Fail($"process failed ({Com.Hex(result)})");
                return;
            }
            var img = new BrawProcessedImage(imagePtr);
            if (!batch[index - first].Fill(img.Width, img.Height, img.Resource, img.SizeBytes))
                Fail($"frame {index}: processed image smaller than {img.Width}x{img.Height} RGB48");
        }
        finally
        {
            Com.Release(processJob);
        }
    }
}
```

`windows/src/Offload.BrawDecode/Program.cs` (celý soubor):

```csharp
// braw-decode for Windows — minimal Blackmagic RAW decoder for the Offload proxy pipeline (same CLI as the Mac tool).
//
//   braw-decode --info [--scale S] clip.braw
//       one JSON line: width, height (processed image at scale S), sourceWidth, sourceHeight, fps, frames,
//       timecode, camera, and when the SDK reports them gamma, gamut, colorScienceGen
//   braw-decode [--scale S] [--start N] [--count N] clip.braw
//       raw interleaved RGB 16-bit little endian frames on stdout (ffmpeg: -f rawvideo -pix_fmt rgb48le)
//
//   S = full | half | quarter | eighth
//
// Frames come out in the clip's own gamma/gamut; the LUT is applied later by ffmpeg.
// BlackmagicRawAPI.dll is loaded at runtime (Offload.Core.BrawLibrary: next to this exe, SDK, Player, Resolve).

using Offload.BrawDecode;

// Binary stdout: the raw handle stream, never Console.Out (text, code page).
using var stdout = Console.OpenStandardOutput();
return Decoder.Run(args, stdout, Console.Error);
```

- [ ] **Step 4: Testy a ruční kontrola exe**

Run: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~Braw"` → PASS; `BrawSampleTests` přeskočené
(bez `OFFLOAD_BRAW_SAMPLE`). Celá sada zelená.

```powershell
dotnet publish src\Offload.BrawDecode\Offload.BrawDecode.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:DebugType=none -o $env:TEMP\braw-pub
& $env:TEMP\braw-pub\braw-decode.exe; $LASTEXITCODE                                   # usage…, 2
& $env:TEMP\braw-pub\braw-decode.exe --info C:\nonexistent\a.braw; $LASTEXITCODE      # braw-decode: cannot open C:\nonexistent\a.braw (0x80004005), 4
(Get-Item $env:TEMP\braw-pub\braw-decode.exe).Length / 1MB                            # ~12–13
```

Publish nesmí vypsat žádné varování ořezu (IL2026 apod.) — `PublishTrimmed` je jen v csproj dekodéru; kdyby se dal na
příkazovou řádku, analyzovalo by se celé Offload.Core (reflexní JSON) a build by spadl.

- [ ] **Step 5: Commit**

Řádek VYVOJ: `- Úkol 6 — braw-decode dekóduje: --info (rozměr po zmenšení z 1. snímku, TC, kamera, gama, gamut), snímky RGB48 po dávkách 8 v pořadí na stdout, CPU pipeline nastavená před otevřením klipu`

```powershell
git add windows docs
git commit -m "Windows: braw-decode decodes (--info, RGB48 frames on stdout)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Balíček, vývojová kopie, test celé cesty se vzorkem

**Files:**
- Modify: `windows/scripts/build.ps1`
- Modify: `windows/tests/Offload.Core.Tests/BrawSampleTests.cs`
- Modify: `windows/README.md` (oddíl „Vzorek BRAW“, Build a testy)

**Interfaces:**
- Produces: `windows\build\Offload\bin\braw-decode.exe` (self-contained, single-file, ořezaný) v balíčku i ZIPu.
- Consumes: `ExternalTools.Resolved()` najde `bin\braw-decode.exe` vedle `Offload.exe` beze změny kódu.

- [ ] **Step 1: Test celé cesty (přeskočí se bez vzorku nebo ffmpeg)** — do třídy `BrawSampleTests` přidat:

```csharp
    /// End to end through Offload: probe, three thumbnails, a short proxy.
    [BrawSampleFact(needsFfmpeg: true)]
    public void Offload_ProbeThumbnailsProxy()
    {
        var tools = new ExternalTools { Ffmpeg = TestMedia.Ffmpeg, Ffprobe = TestMedia.Ffprobe, BrawDecoder = Path.Combine(AppContext.BaseDirectory, "braw-decode.exe"), NoSearch = true };
        var clip = new Clip { Name = Path.GetFileNameWithoutExtension(Sample), Path = Path.GetFileName(Sample), Format = ClipFormat.Braw, Segments = [Path.GetFileName(Sample)], Bytes = new FileInfo(Sample).Length };
        var card = Path.GetDirectoryName(Sample)!;
        using var r = new ProxyRenderer(new ProxySettings { Enabled = true, Height = 540 }, tools);
        Assert.Null(r.UnsupportedReason(clip));
        var info = r.Probe(clip, card);
        Assert.Equal("Blackmagic RAW", info.Codec);
        Assert.False(string.IsNullOrEmpty(info.Gamma));
        var lut = LutResolver.Resolve(clip, info, Sample, r.Settings, LutCatalog.Load(LutCatalog.DefaultFolder));
        Assert.Contains("Blackmagic RAW", lut.Reason);
        var thumbs = r.Thumbnails(clip, card, info, lut);
        Assert.Equal(info.Frames > 2 ? 3 : info.Frames, thumbs.Count);
        Assert.All(thumbs, t => Assert.True(t.Jpeg.Length > 1000));
        using var dir = new TempDir();
        var output = Path.Combine(dir.Path, "proxy.mp4");
        r.Render(clip, card, info, lut, output, _ => { }, () => false);
        var probed = FFProbe.Probe(TestMedia.Ffprobe!, output);
        Assert.Equal(540, probed.Height);
        Assert.Equal(info.Frames, probed.Frames);
    }
```

- [ ] **Step 2: `build.ps1`** — za publish CLI (před GUI) přidat:

```powershell
# braw-decode: Blackmagic RAW decoder (trimmed single file; BlackmagicRawAPI.dll comes from the SDK, Player or Resolve).
dotnet publish (Join-Path $root 'src\Offload.BrawDecode\Offload.BrawDecode.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:DebugType=none -o (Join-Path $out 'bin')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (braw-decode) failed' }
```

a první řádek komentáře skriptu změnit na
`# Builds windows\build\Offload: Offload.exe (GUI) + bin\offload.exe (CLI) + bin\braw-decode.exe + bin\ffmpeg.exe, ffprobe.exe (all self-contained).`

- [ ] **Step 3: README** — do `windows/README.md` (oddíl „Build a testy“) doplnit:

```markdown
### Vzorek BRAW (testy dekodéru Blackmagic RAW)

Dekodér `bin\braw-decode.exe` potřebuje knihovnu `BlackmagicRawAPI.dll` — stačí nainstalovaný DaVinci Resolve,
nebo zdarma „Blackmagic RAW“ (Player + SDK) z https://www.blackmagicdesign.com/products/blackmagicraw.
Testy, které potřebují skutečný klip, se bez vzorku přeskočí. Vzorek:

1. Na https://www.blackmagicdesign.com/products/blackmagicraw (nebo https://www.blackmagicdesign.com/event/blackmagicrawinstaller,
   „Download RAW Samples“) stáhnout jeden ukázkový klip (zdarma, formulář se jménem a e-mailem), rozbalit.
2. Nastavit proměnnou na plnou cestu ke klipu a pustit testy:

       setx OFFLOAD_BRAW_SAMPLE "D:\Vzorky\BRAW\A001_01011200_C001.braw"
       # nové okno PowerShellu
       dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~Braw"

Při vývoji (GUI/CLI z `bin\Debug`) se `braw-decode.exe` hledá ve `windows\tools\bin`:
`dotnet publish src\Offload.BrawDecode\Offload.BrawDecode.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:DebugType=none -o tools\bin`.
```

(Jméno souboru je příklad; ukázkové klipy z 12K kamery mají stovky MB až jednotky GB.)

- [ ] **Step 4: Ověření**

```powershell
.\scripts\build.ps1
Get-ChildItem build\Offload\bin        # offload.exe, braw-decode.exe, ffmpeg.exe, ffprobe.exe
& build\Offload\bin\braw-decode.exe; $LASTEXITCODE   # usage, 2
```

Bez vzorku: `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~Braw"` → PASS, 5 přeskočeno. Celá sada zelená.

- [ ] **Step 5: Commit**

Řádek VYVOJ: `- Úkol 7 — braw-decode.exe v balíčku (bin\), test celé cesty s klipem (proxy, tři snímky, LUT podle gamy) přes OFFLOAD_BRAW_SAMPLE, návod na vzorek v README`

```powershell
git add windows docs
git commit -m "Windows: braw-decode in the package; end-to-end BRAW test with a sample clip" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Dokumentace a ověření se vzorkem

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md` (tabulka plánů: řádek 3 → hotovo), `docs/ROZDILY.md`, `docs/VYVOJ-MAC.md`
- Modify: `windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md` §6.6 (rozhodnutí: samostatný `bin\braw-decode.exe`; oprava překlepu `binraw-decode.exe`), `windows/docs/superpowers/specs/2026-10-05-offload-luts-design.md` §3/§7 (stav)
- Modify: `windows/README.md` (stav, ruční test)

- [ ] **Step 1: Texty**

- `docs/ROZDILY.md`: řádek Blackmagic RAW → Windows `✅ braw-decode.exe (C#, knihovna z SDK / Player / Resolve)`;
  do „Záměrné rozdíly“: Windows `--info` navíc `gamma`/`gamut`/`colorScienceGen` a LUT podle gamy (Mac zatím vždy Film Gen 5);
  chybný argument = kód 2; konfigurace pipeline před `OpenClip`.
- `docs/VYVOJ-MAC.md` „Naplánováno“: Mac `braw-decode` má vydat `gamma`/`gamut`/`colorScienceGen` (atributy `gama`/`gamt`/`csgn`
  přes `CloneClipProcessingAttributes`) a nastavit pipeline **před** `OpenClip` (SDK čte konfiguraci při prvním otevření);
  pravidlo LUTu podle gamy jako `LutResolver.Braw` na Windows.
- `windows/README.md` „Stav“: BRAW hotovo (čeká ověření se vzorkem / kartou); v checklistu ručního testu bod „BRAW klip:
  proxy, tři snímky, LUT podle gamy v reportu“.

- [ ] **Step 2: Ověření se vzorkem (uživatel dodá klip, viz README)**

```powershell
$env:OFFLOAD_BRAW_SAMPLE = "<cesta ke klipu>.braw"
dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~Braw"     # vše PASS, 0 přeskočeno
& build\Offload\bin\braw-decode.exe --info --scale eighth $env:OFFLOAD_BRAW_SAMPLE
```

Kontrola výstupu `--info`: `gamma`/`gamut` vyplněné (ověřuje rozložení VARIANT — jediná část COM vrstvy, kterou spike
bez klipu ověřit nemohl). Pokud chybí, v `BrawClipProcessingAttributes.Text` vypsat `v.VarType` na stderr a porovnat
s IDL SDK; dokud se neopraví, `LutResolver` padá zpět na „gama neuvedena — předpoklad Film Gen 5“ (žádná regrese).
Pak ruční běh `offload copy <složka se vzorkem> <cíl> --proxy` → v reportu tři snímky, LUT „Blackmagic RAW:
Blackmagic Design Film Gen 5“, proxy vizuálně jako v Blackmagic RAW Playeru s LUTem Film → Rec.709.
Ověřit i cesty SDK/Playeru z Task 1, pokud je uživatel nainstaluje (`BrawLibrary.Find` → cesta).

- [ ] **Step 3: Commit**

Řádek VYVOJ: `- Úkol 8 — dokumentace: ROZDILY, VYVOJ-MAC (gama v Mac braw-decode), návrh §6.6, README; ověření se vzorkem`

```powershell
git add windows docs
git commit -m "Docs: Windows BRAW decoder done; Mac follow-ups" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

## Otevřená rizika

- **VARIANT u `GetClipAttribute`** ověří až vzorek (Task 8). Pád zpět je bezpečný (gama null → dosavadní předpoklad).
- **Cesty SDK a Playeru** jsou podle návrhu, ne ověřené instalací; Resolve ověřený. Kdyby se lišily, opravit `BrawLibrary.SearchDirs` + test.
- **Knihovna z Resolve** vrací `GetVersion` „0.0“ a je svázaná s verzí Resolve; s novějšími kamerami může klip
  odmítnout (`OpenClip` → kód 4) — pak nainstalovat aktuální Blackmagic RAW (SDK/Player), které má v pořadí přednost.
- **Film Gen 4** → bez LUTu (rozhodnutí k potvrzení uživatelem).
- **Plné rozlišení**: dávka 8 snímků 12K při `--scale full` = ~3,8 GB RAM (stejně jako Mac); Offload volí scale podle
  cílové výšky, takže full jen u proxy ≥ výšky senzoru.
