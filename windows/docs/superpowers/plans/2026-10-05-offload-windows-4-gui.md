# Offload pro Windows — plán 4: GUI (WPF)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aplikace `Offload.exe` s oknem jako na Macu: vlevo karty, cíle a nastavení, vpravo fronta úloh s průběhem, ověřením a vysunutím karty. K tomu USB test při startu, okno Hardware a řetěz článků na kartě úlohy. Výsledek je přenosná složka `build\Offload\` (a zip), kterou uživatel rovnou spustí.

**Architecture:** Veškerá logika, kterou jde testovat bez okna, jde do `Offload.Core/Desktop/` a má xUnit testy: nastavení, detekce svazků, vysunutí, texty a výpočty karty úlohy, fronta úloh. Nový projekt `Offload.App` (WPF, MVVM bez knihoven) je tenká vrstva: ViewModely volají Core, XAML jen zobrazuje. Ověření GUI probíhá spuštěním aplikace, UI Automation (`scripts\app-drive.ps1`) a snímky obrazovky, které implementer prohlédne.

**Tech Stack:** C# / .NET 10, WPF (Fluent theme, `ThemeMode="System"`), xUnit, PowerShell 5.1 (UI Automation pro ověření).

**Spec:** `windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md` — oddíly 8 (GUI) a 8a.2–8a.4 (HW v GUI). Předloha UI: `mac/Sources/OffloadApp/Views.swift`, `AppModel.swift`.

## Global Constraints

- `net10.0-windows`, Windows 10/11 x64, bez administrátorských práv. **Žádné nové NuGet balíčky** (žádný CommunityToolkit; vlastní `ObservableObject`/`RelayCommand`).
- Texty česky, 1:1 z `Views.swift`. Úpravy pro Windows: „Finder“ → „Průzkumník“, „⌘↩︎“ → „Ctrl+Enter“, varování APFS → „Doporučeno NTFS — spolehlivější než exFAT/FAT32“, poznámka k Canon/Sony bez „na macOS Intel“. Čísla a množné číslo česky správně („1 karta“, „3 karty“, „5 karet“) — `Texts` v Core.
- UI vlákno nikdy nečeká na disk: sken svazků, vysunutí, HW test a `DriveLinks`/`UsbScanner` běží na pozadí (`Task.Run`) a výsledek se aplikuje přes dispatcher.
- Na kartu se nikdy nezapisuje. Diagnostika ani GUI nikdy neshodí kopii ani aplikaci (`DispatcherUnhandledException` → hláška + `%APPDATA%\Offload\app.log`, aplikace běží dál).
- Nastavení: `%APPDATA%\Offload\settings.json` (klíče jako Mac `AppSettings`: `destRoots`, `verify`, `analyzeClips`, `proxy`, `tools`; navíc `portNames`). Proměnná prostředí `OFFLOAD_SETTINGS_DIR` přesměruje složku (settings, hwtest.json, app.log) — pro izolované testy a automatizaci.
- Upozornění: zvuk (`SystemSounds`) + blikání v hlavním panelu (`FlashWindowEx`). **Toast notifikace se nedělá** (nebalená aplikace by potřebovala AUMID a zástupce) — zapsat do `docs/ROZDILY.md`.
- Vysunutí: čtečka karet (`DriveType.Removable`) → vysune se jen médium, čtečka zůstane připojená pro další kartu; pevný USB disk → `CM_Request_Device_EjectW`. Složka (ne celý disk) se nevysouvá.
- Automatizace: tlačítka a důležité prvky mají `AutomationProperties.AutomationId` (seznam v úkolech). `--no-hw-prompt` vypne dotaz při startu; argumenty, které jsou existující složky, se přidají jako ruční zdroj a zaškrtnou.
- Mac (`mac/`) se nemění. Každý commit: řádek nahoře v nové sekci `## 2026-10-05 — plán 4 (GUI), průběh` v `docs/VYVOJ-WINDOWS.md` (nad sekcí plánu HW, bez SHA); poslední řádek zprávy přesně `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Testy, které zapisují, jen do dočasných složek. Skutečné vysunutí se v automatických testech nikdy nevolá.

## Review Focus

1. **Zavření okna během kopie** → dotaz; „Ano“ zruší úlohy, počká na úklid a zavře se; v cílech nezůstane `.offload-part`. Test: Task 8 ověření (UI Automation, druhý běh).
2. **Poškozený, starý nebo cizí `settings.json`** (neznámé klíče, chybějící sekce, špatný typ v jedné sekci) → aplikace startuje, zbytek nastavení se zachová. Test: Task 1 `BadSection_KeepsTheRest`, `MacKeys_PartialAndUnknown`.
3. **Disk, na kterém je cíl, vložený jako karta / karta vytažená během skenu** → cílový svazek se jako karta nenabízí, vytažená karta zmizí ze seznamu bez pádu. Test: Task 2 `SelectionAfterRefresh_*`, `Scan_*`.
4. **LUT smazaný po výběru, chybějící ffmpeg** → „Spustit offload“ je neaktivní s červenou hláškou (LUT); u ffmpeg jen oranžové varování, kopie jde spustit. Test: Task 1 `ProxyPreflight_*`.
5. **Úloha zrušená ještě ve frontě, chyba jedné úlohy** → karta úlohy ukáže „Zrušeno“/„Chyba“, další karty se zkopírují. Test: Task 4 `CancelledBeforeStart_ReportsCancelled`, Task 5 `CancelledBeforeStart_Skipped_ButFinished`, `ThrowingJob_DoesNotStopTheQueue`.

---

## Struktura souborů

```
windows/src/Offload.Core/Desktop/
  AppData.cs           složka aplikace (%APPDATA%\Offload nebo OFFLOAD_SETTINGS_DIR)
  AppSettings.cs       AppSettings, SettingsStore (load/save/clone), ProxyPreflight
  StartupOptions.cs    argumenty příkazové řádky aplikace
  Volumes.cs           VolumeItem, sken svazků, „vypadá jako karta“, výběr po rescanu
  CardEject.cs         bezpečné vysunutí karty / disku
  Texts.cs             české texty s množným číslem
  JobProgress.cs       rychlosti, ETA, průběh fáze, texty karty úlohy, úzké hrdlo
  JobQueue.cs          fronta: kopie za sebou, stage 2 souběžně s další kopií
windows/src/Offload.App/              (AssemblyName Offload → Offload.exe)
  Offload.App.csproj, App.xaml(.cs), AppLog.cs, Mvvm.cs, Converters.cs, Dialogs.cs, Shell.cs, NativeMethods.cs, VolumeWatcher.cs
  MainWindow.xaml(.cs), SetupPanel.xaml(.cs), QueuePanel.xaml(.cs), HardwareWindow.xaml(.cs)
  MainViewModel.cs, MainViewModel.Setup.cs, MainViewModel.Queue.cs, MainViewModel.Hardware.cs
  SourceItemViewModel.cs, DestinationViewModel.cs, ProxyOptionsViewModel.cs, ToolRowViewModel.cs, JobItemViewModel.cs, HardwareViewModel.cs
windows/scripts/app-drive.ps1         spuštění, UI Automation, snímky (vývoj a ověření)
windows/tests/Offload.Core.Tests/
  SettingsStoreTests.cs, VolumesTests.cs, CardEjectTests.cs, JobProgressTests.cs, JobQueueTests.cs
```

---

### Task 1: Nastavení aplikace

**Files:**
- Create: `windows/src/Offload.Core/Desktop/AppData.cs`, `windows/src/Offload.Core/Desktop/AppSettings.cs`
- Modify: `windows/src/Offload.Core/Hardware/HwTest.cs` (`DefaultPath` přes `AppData.Folder`)
- Test: `windows/tests/Offload.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `AppData.Folder`; `AppSettings` (`DestRoots: List<string>`, `Verify`, `AnalyzeClips`, `Proxy: ProxySettings`, `Tools: ExternalTools`, `PortNames: Dictionary<string,string>` case-insensitive); `SettingsStore.DefaultPath`, `Load(string) -> AppSettings`, `Save(AppSettings, string)`, `Clone<T>(T) -> T`, `ProxyHeights: int[]`; `ProxyPreflight.Blocking(ProxySettings, bool analyzeClips) -> string?`, `ProxyPreflight.Warning(ProxySettings, bool analyzeClips, ExternalTools resolved) -> string?`.

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/SettingsStoreTests.cs`:

```csharp
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

public class SettingsStoreTests
{
    static AppSettings LoadJson(TempDir dir, string json)
    {
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, json);
        return SettingsStore.Load(path);
    }

    [Fact]
    public void Missing_Defaults()
    {
        using var dir = new TempDir();
        var s = SettingsStore.Load(Path.Combine(dir.Path, "settings.json"));
        Assert.Empty(s.DestRoots);
        Assert.True(s.Verify);
        Assert.True(s.AnalyzeClips);
        Assert.False(s.Proxy.Enabled);
        Assert.Equal(1080, s.Proxy.Height);
        Assert.Empty(s.PortNames);
    }

    [Fact]
    public void Corrupt_Defaults()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, "{not json");
        Assert.True(s.Verify);
        Assert.Empty(s.DestRoots);
    }

    // Review focus 2: a Mac-shaped file with missing and unknown keys.
    [Fact]
    public void MacKeys_PartialAndUnknown()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, """
            {"destRoots":["D:\\A","d:\\a"," "],"verify":false,"proxy":{"enabled":true,"codec":"proresLT"},
             "tools":{"ffmpeg":"C:\\x\\ffmpeg.exe"},"futureKey":42}
            """);
        Assert.Equal([@"D:\A"], s.DestRoots);
        Assert.False(s.Verify);
        Assert.True(s.AnalyzeClips);
        Assert.True(s.Proxy.Enabled);
        Assert.Equal(ProxyCodec.ProresLT, s.Proxy.Codec);
        Assert.Equal(1080, s.Proxy.Height);
        Assert.True(s.Proxy.BurnTimecode);
        Assert.Equal(@"C:\x\ffmpeg.exe", s.Tools.Ffmpeg);
    }

    // Review focus 2: one bad section never costs the others.
    [Fact]
    public void BadSection_KeepsTheRest()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, """{"destRoots":["E:\\Backup"],"proxy":{"codec":"av1"},"verify":"yes","portNames":{"A#1":"vpředu"}}""");
        Assert.Equal([@"E:\Backup"], s.DestRoots);
        Assert.Equal(ProxyCodec.H264, s.Proxy.Codec);
        Assert.True(s.Verify);
        Assert.Equal("vpředu", s.PortNames["a#1"]);
    }

    [Fact]
    public void OutOfRange_Clamped()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, """{"proxy":{"height":999,"bitrateMbps":500,"frameScale":0.1,"frameAspect":-1,"lutPath":""}}""");
        Assert.Equal(1080, s.Proxy.Height);
        Assert.Equal(50, s.Proxy.BitrateMbps);
        Assert.Equal(0.5, s.Proxy.FrameScale);
        Assert.Null(s.Proxy.FrameAspect);
        Assert.Null(s.Proxy.LutPath);
    }

    [Fact]
    public void RoundTrip_PortNamesIgnoreCase_NoTempLeft()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "sub", "settings.json");
        var s = new AppSettings { DestRoots = [@"D:\Backup"], Verify = false };
        s.PortNames[@"USB\ROOT_HUB30\4&X#6"] = "přední levý";
        s.Proxy.FrameAspect = 1.85;
        SettingsStore.Save(s, path);
        var back = SettingsStore.Load(path);
        Assert.Equal([@"D:\Backup"], back.DestRoots);
        Assert.False(back.Verify);
        Assert.Equal("přední levý", back.PortNames[@"usb\root_hub30\4&x#6"]);
        Assert.Equal(1.85, back.Proxy.FrameAspect);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains("přední levý", File.ReadAllText(path));
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var p = new ProxySettings { LutPath = "a.cube" };
        var c = SettingsStore.Clone(p);
        c.LutPath = "b.cube";
        Assert.Equal("a.cube", p.LutPath);
    }

    // Review focus 4
    [Fact]
    public void ProxyPreflight_MissingLut_Blocks_WhenProxyOrAnalysisUseIt()
    {
        using var dir = new TempDir();
        var lut = Path.Combine(dir.Path, "look.cube");
        var p = new ProxySettings { Enabled = true, LutPath = lut };
        Assert.Contains("look.cube", ProxyPreflight.Blocking(p, analyzeClips: false));
        p.Enabled = false;
        Assert.NotNull(ProxyPreflight.Blocking(p, analyzeClips: true));
        Assert.Null(ProxyPreflight.Blocking(p, analyzeClips: false));
        File.WriteAllText(lut, "LUT_3D_SIZE 2");
        p.Enabled = true;
        Assert.Null(ProxyPreflight.Blocking(p, analyzeClips: true));
    }

    [Fact]
    public void ProxyPreflight_MissingFfmpeg_WarnsOnly()
    {
        var none = new ExternalTools();
        Assert.NotNull(ProxyPreflight.Warning(new ProxySettings { Enabled = true }, analyzeClips: false, none));
        Assert.NotNull(ProxyPreflight.Warning(new ProxySettings(), analyzeClips: true, none));
        Assert.Null(ProxyPreflight.Warning(new ProxySettings(), analyzeClips: false, none));
        Assert.Null(ProxyPreflight.Warning(new ProxySettings { Enabled = true }, true, new ExternalTools { Ffmpeg = "f", Ffprobe = "p" }));
    }

    [Fact]
    public void AppDataFolder_Override()
    {
        var old = Environment.GetEnvironmentVariable("OFFLOAD_SETTINGS_DIR");
        try
        {
            Environment.SetEnvironmentVariable("OFFLOAD_SETTINGS_DIR", @"X:\iso");
            Assert.Equal(@"X:\iso", AppData.Folder);
            Assert.Equal(@"X:\iso\settings.json", SettingsStore.DefaultPath);
            Assert.Equal(@"X:\iso\hwtest.json", Hardware.HwTest.DefaultPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OFFLOAD_SETTINGS_DIR", old);
        }
        Assert.EndsWith(@"\Offload", AppData.Folder);
    }
}
```

- [ ] **Step 2: Spustit, ověřit selhání**

Run (z `windows/`): `dotnet test --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: build FAIL — `Offload.Core.Desktop` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Desktop/AppData.cs`:

```csharp
namespace Offload.Core.Desktop;

/// Where the app keeps its files (settings.json, hwtest.json, app.log): %APPDATA%\Offload, or the folder in
/// OFFLOAD_SETTINGS_DIR (isolated test and automation runs).
public static class AppData
{
    public static string Folder =>
        Environment.GetEnvironmentVariable("OFFLOAD_SETTINGS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Offload");
}
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Offload.Core.Desktop;

/// The app's settings — the Mac `AppSettings` keys (destRoots, verify, analyzeClips, proxy, tools);
/// `portNames` is Windows only: the user's names for USB sockets (`UsbConnector.Id` → name).
public sealed class AppSettings
{
    [JsonPropertyName("destRoots")] public List<string> DestRoots { get; set; } = [];
    [JsonPropertyName("verify")] public bool Verify { get; set; } = true;
    [JsonPropertyName("analyzeClips")] public bool AnalyzeClips { get; set; } = true;
    [JsonPropertyName("proxy")] public ProxySettings Proxy { get; set; } = new();
    [JsonPropertyName("tools")] public ExternalTools Tools { get; set; } = new();
    [JsonPropertyName("portNames")] public Dictionary<string, string> PortNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class SettingsStore
{
    public static readonly int[] ProxyHeights = [540, 720, 1080, 2160];

    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// Missing or corrupt file → defaults. Each top-level section is read on its own: a section that does not
    /// parse (a newer codec name, a wrong type) falls back to its defaults and the others are kept.
    public static AppSettings Load(string path)
    {
        var s = new AppSettings();
        JsonObject? root;
        try
        {
            if (!File.Exists(path)) return s;
            root = JsonNode.Parse(File.ReadAllBytes(path)) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return s;
        }
        if (root == null) return s;

        T? Get<T>(string key)
        {
            try
            {
                return root[key] is JsonNode n ? n.Deserialize<T>(Json) : default;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or NotSupportedException)
            {
                return default;
            }
        }

        if (Get<List<string>>("destRoots") is { } roots) s.DestRoots = roots;
        if (Get<bool?>("verify") is bool verify) s.Verify = verify;
        if (Get<bool?>("analyzeClips") is bool analyze) s.AnalyzeClips = analyze;
        if (Get<ProxySettings>("proxy") is { } proxy) s.Proxy = proxy;
        if (Get<ExternalTools>("tools") is { } tools) s.Tools = tools;
        if (Get<Dictionary<string, string>>("portNames") is { } names) s.PortNames = names;
        return Normalize(s);
    }

    internal static AppSettings Normalize(AppSettings s)
    {
        s.DestRoots = (s.DestRoots ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        s.Proxy ??= new ProxySettings();
        s.Tools ??= new ExternalTools();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in s.PortNames ?? [])
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) names[id] = name.Trim();
        s.PortNames = names;
        var p = s.Proxy;
        if (!ProxyHeights.Contains(p.Height)) p.Height = 1080;
        p.BitrateMbps = Math.Clamp(p.BitrateMbps, 2, 50);
        p.FrameScale = double.IsFinite(p.FrameScale) ? Math.Clamp(p.FrameScale, 0.5, 1.0) : 1.0;
        if (p.FrameAspect is double a && (!double.IsFinite(a) || a <= 0)) p.FrameAspect = null;
        if (string.IsNullOrWhiteSpace(p.LutPath)) p.LutPath = null;
        return s;
    }

    /// Written to a temporary file next to it, then moved over — a crash never leaves a half-written file.
    public static void Save(AppSettings settings, string path)
    {
        NativeIO.Mkdirs(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        NativeIO.WriteFileDurably(tmp, JsonSerializer.SerializeToUtf8Bytes(settings, Json));
        NativeIO.Move(tmp, path, replace: true);
    }

    /// A deep copy, so a queued job keeps the settings it was started with.
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Json), Json)!;
}

/// Checks before a start that the proxy/thumbnail settings can work.
public static class ProxyPreflight
{
    /// Why "Spustit offload" must stay disabled, or null. A missing LUT would break thumbnails and proxies.
    public static string? Blocking(ProxySettings p, bool analyzeClips) =>
        (p.Enabled || analyzeClips) && p.LutPath is string lut && !File.Exists(lut)
            ? $"LUT nenalezen: {Path.GetFileName(lut)} — vyberte jiný nebo ho odeberte."
            : null;

    /// A warning that does not block (copy and verification still run), or null.
    public static string? Warning(ProxySettings p, bool analyzeClips, ExternalTools resolved) =>
        (p.Enabled || analyzeClips) && (resolved.Ffmpeg == null || resolved.Ffprobe == null)
            ? "ffmpeg/ffprobe nenalezen — náhledy a proxy se přeskočí, kopie a ověření proběhnou."
            : null;
}
```

V `windows/src/Offload.Core/Hardware/HwTest.cs` nahradit `DefaultPath`:

```csharp
    public static string DefaultPath => Path.Combine(Desktop.AppData.Folder, "hwtest.json");
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~SettingsStoreTests"` → PASS (10). Pak celá sada `dotnet test` → vše zelené, bez varování.

- [ ] **Step 5: Commit**

Do `docs/VYVOJ-WINDOWS.md` založit nad sekcí plánu HW novou sekci `## 2026-10-05 — plán 4 (GUI), průběh` s první položkou `- Úkol 1 — nastavení aplikace (settings.json kompatibilní s Mac klíči, odolné vůči poškozeným sekcím), kontrola LUT a ffmpeg před startem`.

```powershell
git add windows docs
git commit -m "Windows: app settings store and proxy preflight" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Svazky a „vypadá jako karta“

**Files:**
- Create: `windows/src/Offload.Core/Desktop/Volumes.cs`, `windows/src/Offload.Core/Desktop/StartupOptions.cs`
- Test: `windows/tests/Offload.Core.Tests/VolumesTests.cs`

**Interfaces:**
- Consumes: `DriveLinks.Resolve`, `StorageBus`, `HwTest.SameVolume`, `Win32.StrCmpLogicalW` (internal).
- Produces: `record VolumeItem(string Name, string Path, long Total, long Free, string Format, bool LooksLikeCard, bool IsDestination)`; `Volumes.LooksLikeCameraCard(string) -> bool`; `Volumes.IsCandidate(DriveType, StorageBus, bool isSystem) -> bool`; `Volumes.Scan(IReadOnlyList<string> destRoots) -> List<VolumeItem>`; `Volumes.SelectionAfterRefresh(IEnumerable<string> previousPaths, IReadOnlyList<VolumeItem> found, IEnumerable<string> selected, IReadOnlyList<string> manual) -> HashSet<string>`; `record StartupOptions(bool NoHwPrompt, IReadOnlyList<string> Sources)` + `StartupOptions.Parse(IReadOnlyList<string>)`.

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/VolumesTests.cs`:

```csharp
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class VolumesTests
{
    static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void LooksLikeCard_MarkerFolder_AnyCase()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "dcim"));
        Assert.True(Volumes.LooksLikeCameraCard(dir.Path));
    }

    [Fact]
    public void LooksLikeCard_MediaAtRootOrOneLevelDown()
    {
        using var a = new TempDir();
        Touch(Path.Combine(a.Path, "A001.MOV"));
        Assert.True(Volumes.LooksLikeCameraCard(a.Path));

        using var b = new TempDir();
        Touch(Path.Combine(b.Path, "A001", "A001C001.braw"));
        Assert.True(Volumes.LooksLikeCameraCard(b.Path));

        using var red = new TempDir();
        Directory.CreateDirectory(Path.Combine(red.Path, "A001_C001.RDM"));
        Assert.True(Volumes.LooksLikeCameraCard(red.Path));
    }

    [Fact]
    public void LooksLikeCard_No()
    {
        using var deep = new TempDir();
        Touch(Path.Combine(deep.Path, "a", "b", "c.mxf"));
        Touch(Path.Combine(deep.Path, "notes.txt"));
        Assert.False(Volumes.LooksLikeCameraCard(deep.Path));
        Assert.False(Volumes.LooksLikeCameraCard(Path.Combine(deep.Path, "missing")));
    }

    [Theory]
    [InlineData(DriveType.Removable, StorageBus.Unknown, false, true)]
    [InlineData(DriveType.Fixed, StorageBus.Usb, false, true)]
    [InlineData(DriveType.Fixed, StorageBus.Sd, false, true)]
    [InlineData(DriveType.Fixed, StorageBus.Mmc, false, true)]
    [InlineData(DriveType.Fixed, StorageBus.Sata, false, false)]
    [InlineData(DriveType.Fixed, StorageBus.Nvme, false, false)]
    [InlineData(DriveType.Removable, StorageBus.Unknown, true, false)]
    [InlineData(DriveType.Network, StorageBus.Network, false, false)]
    [InlineData(DriveType.CDRom, StorageBus.Unknown, false, false)]
    public void IsCandidate(DriveType type, StorageBus bus, bool isSystem, bool expected) =>
        Assert.Equal(expected, Volumes.IsCandidate(type, bus, isSystem));

    static VolumeItem V(string path, bool card = true, bool dest = false) => new(path, path, 100, 50, "exFAT", card && !dest, dest);

    // Review focus 3
    [Fact]
    public void SelectionAfterRefresh_NewCardTicked_OldChoiceKept_GoneAndDestinationsDropped()
    {
        var previous = new[] { @"F:\", @"G:\", @"H:\" };
        var found = new List<VolumeItem> { V(@"F:\"), V(@"G:\"), V(@"I:\"), V(@"J:\", card: false), V(@"K:\", dest: true) };
        var selected = new[] { @"F:\", @"H:\", @"K:\", @"D:\shoot" };
        var next = Volumes.SelectionAfterRefresh(previous, found, selected, [@"D:\shoot"]);
        Assert.Contains(@"F:\", next);           // still there, still ticked
        Assert.DoesNotContain(@"G:\", next);     // the user unticked it earlier — stays unticked
        Assert.DoesNotContain(@"H:\", next);     // pulled out
        Assert.Contains(@"I:\", next);           // new card → ticked
        Assert.DoesNotContain(@"J:\", next);     // new, but not a card
        Assert.DoesNotContain(@"K:\", next);     // holds a destination
        Assert.Contains(@"D:\shoot", next);      // manual folder
    }

    [Fact]
    public void Scan_NeverOffersTheSystemDrive_AndDoesNotThrow()
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory)!;
        var items = Volumes.Scan([]);
        Assert.DoesNotContain(items, v => string.Equals(v.Path, system, StringComparison.OrdinalIgnoreCase));
        Assert.All(items, v => Assert.False(string.IsNullOrEmpty(v.Name)));
    }

    [UsbDriveFact]
    public void Scan_OffersUsbDisk_AndMarksItWhenItHoldsADestination()
    {
        var root = UsbDriveFactAttribute.FirstUsbDrive!;
        Assert.Contains(Volumes.Scan([]), v => v.Path == root && !v.IsDestination);
        var asDest = Volumes.Scan([Path.Combine(root, "Backup")]).Single(v => v.Path == root);
        Assert.True(asDest.IsDestination);
        Assert.False(asDest.LooksLikeCard);
    }

    [Fact]
    public void StartupOptions_Parse()
    {
        using var dir = new TempDir();
        var o = StartupOptions.Parse(["--no-hw-prompt", dir.Path, Path.Combine(dir.Path, "missing"), "--unknown"]);
        Assert.True(o.NoHwPrompt);
        Assert.Equal([Path.GetFullPath(dir.Path)], o.Sources);
        Assert.False(StartupOptions.Parse([]).NoHwPrompt);
    }
}
```

- [ ] **Step 2: Spustit, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~VolumesTests"` → build FAIL (`Volumes` neexistuje).

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Desktop/Volumes.cs`:

```csharp
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

/// A mounted volume offered in the Karty section (Mac `VolumeItem`). Name: "EOS_DIGITAL (F:)".
public sealed record VolumeItem(string Name, string Path, long Total, long Free, string Format, bool LooksLikeCard, bool IsDestination);

public static class Volumes
{
    static readonly HashSet<string> Markers = new(StringComparer.OrdinalIgnoreCase)
        { "DCIM", "PRIVATE", "CLIP", "CONTENTS", "XDROOT", "M4ROOT", "AVF_INFO", "CANONMSC", "BRAW", "MISC" };

    static readonly HashSet<string> MediaExt = new(StringComparer.OrdinalIgnoreCase)
        { ".braw", ".r3d", ".rdm", ".rdc", ".mxf", ".mov", ".mp4", ".ari", ".crm" };

    /// Typical camera media (1:1 with the Mac): a known top-level folder, or clips at most one folder down
    /// (ARRI/RED/Blackmagic keep each clip or reel in its own folder).
    public static bool LooksLikeCameraCard(string path)
    {
        static bool HasMedia(IEnumerable<string> names) => names.Any(n => MediaExt.Contains(System.IO.Path.GetExtension(n)));
        try
        {
            var items = Directory.GetFileSystemEntries(path).Select(e => System.IO.Path.GetFileName(e)).ToList();
            if (items.Any(Markers.Contains) || HasMedia(items)) return true;
            foreach (var dir in Directory.GetDirectories(path).Where(d => !System.IO.Path.GetFileName(d).StartsWith('.')).Take(50))
            {
                try
                {
                    if (HasMedia(Directory.GetFileSystemEntries(dir))) return true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // System Volume Information and friends.
                }
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// Offered as a card source: removable media, or a fixed disk on USB/SD/MMC; never the system drive.
    public static bool IsCandidate(DriveType type, StorageBus bus, bool isSystem) =>
        !isSystem && (type == DriveType.Removable
                      || (type == DriveType.Fixed && bus is StorageBus.Usb or StorageBus.Sd or StorageBus.Mmc));

    /// Card candidates, sorted by name like Explorer does. Read-only; a drive pulled out mid-scan is skipped.
    public static List<VolumeItem> Scan(IReadOnlyList<string> destRoots)
    {
        var system = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var found = new List<VolumeItem>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType is not (DriveType.Removable or DriveType.Fixed) || !d.IsReady) continue;
                var root = d.RootDirectory.FullName;
                bool isSystem = string.Equals(root, system, StringComparison.OrdinalIgnoreCase);
                var bus = d.DriveType == DriveType.Fixed && !isSystem ? DriveLinks.Resolve(root).Bus : StorageBus.Unknown;
                if (!IsCandidate(d.DriveType, bus, isSystem)) continue;
                bool isDest = destRoots.Any(r => HwTest.SameVolume(r, root));
                var letter = root.TrimEnd('\\');
                var name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? letter : $"{d.VolumeLabel} ({letter})";
                found.Add(new VolumeItem(name, root, d.TotalSize, d.AvailableFreeSpace, d.DriveFormat,
                    !isDest && LooksLikeCameraCard(root), isDest));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Pulled out while we looked at it.
            }
        }
        found.Sort((a, b) => Win32.StrCmpLogicalW(a.Name, b.Name));
        return found;
    }

    /// The selection after a rescan (Mac `refreshVolumes`): a card that just appeared is ticked; a source that
    /// disappeared or now holds a destination drops out; manual folders stay.
    public static HashSet<string> SelectionAfterRefresh(IEnumerable<string> previousPaths, IReadOnlyList<VolumeItem> found,
        IEnumerable<string> selected, IReadOnlyList<string> manual)
    {
        var previous = new HashSet<string>(previousPaths, StringComparer.OrdinalIgnoreCase);
        var result = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        foreach (var v in found)
            if (v.LooksLikeCard && !previous.Contains(v.Path)) result.Add(v.Path);
        result.RemoveWhere(p =>
            !found.Any(v => string.Equals(v.Path, p, StringComparison.OrdinalIgnoreCase) && !v.IsDestination)
            && !manual.Contains(p, StringComparer.OrdinalIgnoreCase));
        return result;
    }
}
```

`windows/src/Offload.Core/Desktop/StartupOptions.cs`:

```csharp
namespace Offload.Core.Desktop;

/// Offload.exe arguments: `--no-hw-prompt` skips the start-up USB question (automation); every argument that is
/// an existing folder becomes a ticked manual source (drag a folder onto Offload.exe).
public sealed record StartupOptions(bool NoHwPrompt, IReadOnlyList<string> Sources)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        bool noPrompt = false;
        var sources = new List<string>();
        foreach (var a in args)
        {
            if (a.Equals("--no-hw-prompt", StringComparison.OrdinalIgnoreCase)) noPrompt = true;
            else if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                try
                {
                    var full = Path.GetFullPath(a.Length == 2 && a[1] == ':' ? a + "\\" : a);
                    if (Directory.Exists(full)) sources.Add(full);
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // Not a path.
                }
            }
        }
        return new StartupOptions(noPrompt, sources);
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~VolumesTests"` → PASS (16 vč. theory; `UsbDriveFact` na tomto PC běží — E: je USB disk). Celá sada zelená.

- [ ] **Step 5: Commit**

Řádek do VYVOJ (plán 4): `- Úkol 2 — svazky: nabídka karet (vyměnitelné a USB/SD disky, nikdy systémový ani cílový disk), „vypadá jako karta“ 1:1 s Macem, výběr po připojení/odpojení, argumenty aplikace`.

```powershell
git add windows docs
git commit -m "Windows: volume candidates, camera-card detection, selection after rescan" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Vysunutí karty

**Files:**
- Create: `windows/src/Offload.Core/Desktop/CardEject.cs`
- Modify: `windows/src/Offload.Core/Hardware/DriveLinks.cs` (sdílené hledání uzlu disku)
- Test: `windows/tests/Offload.Core.Tests/CardEjectTests.cs`

**Interfaces:**
- Consumes: `UsbNative.DeviceIoControl`, `DevNodes.Parent`, `DriveLinks`.
- Produces: `record EjectResult(bool Ok, string? Error)`; `CardEject.Eject(string source) -> EjectResult` (nikdy nevyhodí); `CardEject.CanEject(string) -> bool`; internal `CardEject.Problem(string) -> string?`, `CardEject.VetoText(int) -> string`, `CardEject.BusyText`; internal `DriveLinks.DiskNode(string root) -> uint?`.

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/CardEjectTests.cs`:

```csharp
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

// Never ejects a real device: only inputs that are refused before any device is touched.
public class CardEjectTests
{
    [Fact]
    public void Folder_Network_System_AreRefused()
    {
        using var dir = new TempDir();
        Assert.Equal("zdroj je složka, ne celá karta", CardEject.Problem(dir.Path));
        Assert.Equal("síťová složka se nevysouvá", CardEject.Problem(@"\\server\share"));
        Assert.Equal("systémový disk nelze vysunout", CardEject.Problem(Path.GetPathRoot(Environment.SystemDirectory)!));
        Assert.False(CardEject.CanEject(dir.Path));
        var r = CardEject.Eject(dir.Path);
        Assert.False(r.Ok);
        Assert.Equal("zdroj je složka, ne celá karta", r.Error);
    }

    [Fact]
    public void BareDriveLetter_IsARoot()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var free = "QRSTUVWXYZ".First(c => !used.Contains(c));
        Assert.Null(CardEject.Problem($"{free}:"));
        var r = CardEject.Eject($@"{free}:\");
        Assert.False(r.Ok);
        Assert.Equal("karta není připojená", r.Error);
    }

    [Fact]
    public void VetoTexts()
    {
        Assert.Equal(CardEject.BusyText, CardEject.VetoText(6));
        Assert.Contains("5", CardEject.VetoText(5));
    }

    [UsbDriveFact]
    public void DiskNode_FoundForUsbDrive_WithAParent()
    {
        var node = DriveLinks.DiskNode(UsbDriveFactAttribute.FirstUsbDrive!);
        Assert.NotNull(node);
        Assert.NotNull(DevNodes.Parent(node!.Value));
    }
}
```

- [ ] **Step 2: Spustit, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~CardEjectTests"` → build FAIL.

- [ ] **Step 3: Implementace**

V `DriveLinks.cs` vytáhnout začátek `UsbLinkOf` (hledání rozhraní disku podle čísla) do sdílené funkce a přidat `DiskNode`:

```csharp
    /// The disk device node with this disk number, or null.
    internal static uint? DiskNodeOf(uint diskNumber)
    {
        foreach (var iface in DevNodes.Interfaces(UsbNative.DiskInterface, null))
        {
            using var h = UsbNative.OpenForQuery(iface);
            if (h != null && DiskNumber(h) == diskNumber)
                return DevNodes.InterfaceStr(iface, DevKeys.InstanceId) is string id ? DevNodes.Locate(id) : null;
        }
        return null;
    }

    /// The disk device node holding the volume at `root` ("E:\"), or null.
    internal static uint? DiskNode(string root)
    {
        try
        {
            using var volume = UsbNative.OpenForQuery(@"\\.\" + root.TrimEnd('\\'));
            return volume != null && DiskNumber(volume) is uint n ? DiskNodeOf(n) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
```

a v `UsbLinkOf` nahradit smyčku přes rozhraní disku voláním `if (DiskNodeOf(diskNumber) is not uint diskNode) return null;`.

`windows/src/Offload.Core/Desktop/CardEject.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

public sealed record EjectResult(bool Ok, string? Error);

/// Safely removes a card. The volume is locked (fails while another program has a file open on it) and
/// dismounted first. A card in a reader (removable media) is then ejected as media — the reader stays connected
/// for the next card. A USB disk is removed as a device (`CM_Request_Device_EjectW`, like "Bezpečně odebrat").
public static unsafe partial class CardEject
{
    const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareReadWrite = 3, OpenExisting = 3;
    const uint FsctlLockVolume = 0x00090018, FsctlDismountVolume = 0x00090020;
    const uint IoctlStorageMediaRemoval = 0x002D4804, IoctlStorageEjectMedia = 0x002D4808;
    const int VetoOutstandingOpen = 6;

    internal const string BusyText = "na kartě má otevřený soubor jiný program — zavřete ho a zkuste to znovu";

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint sa, uint disposition, uint flags, nint template);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Request_Device_EjectW")]
    private static partial uint CM_Request_Device_Eject(uint devInst, out int vetoType, char* vetoName, int nameLength, uint flags);

    public static bool CanEject(string source) => Problem(source) == null;

    /// Why this source cannot be ejected, or null.
    internal static string? Problem(string source)
    {
        string full;
        try
        {
            full = Path.GetFullPath(source.Length == 2 && source[1] == ':' ? source + "\\" : source);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "neplatná cesta";
        }
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return "síťová složka se nevysouvá";
        if (!string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return "zdroj je složka, ne celá karta";
        if (string.Equals(root, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase)) return "systémový disk nelze vysunout";
        return null;
    }

    internal static string VetoText(int veto) => veto switch
    {
        VetoOutstandingOpen => BusyText,
        0 => "Windows zařízení nevysunul",
        _ => $"Windows vysunutí odmítl (důvod {veto})",
    };

    /// Never throws; the error text completes "Kartu se nepodařilo vysunout: …".
    public static EjectResult Eject(string source)
    {
        try
        {
            if (Problem(source) is string problem) return new(false, problem);
            var root = Path.GetPathRoot(Path.GetFullPath(source.Length == 2 ? source + "\\" : source))!;
            bool media = new DriveInfo(root).DriveType == DriveType.Removable;
            uint? disk = media ? null : DriveLinks.DiskNode(root);
            using (var h = CreateFileW(@"\\.\" + root.TrimEnd('\\'), GenericRead | GenericWrite, ShareReadWrite, 0, OpenExisting, 0, 0))
            {
                if (h.IsInvalid) return new(false, "karta není připojená");
                if (!Lock(h)) return new(false, BusyText);
                UsbNative.DeviceIoControl(h, FsctlDismountVolume, null, 0, null, 0, out _, 0);
                byte allow = 0;
                UsbNative.DeviceIoControl(h, IoctlStorageMediaRemoval, &allow, 1, null, 0, out _, 0);
                if (media || disk == null)
                {
                    // A reader that cannot eject the media still has it dismounted — safe to pull out.
                    UsbNative.DeviceIoControl(h, IoctlStorageEjectMedia, null, 0, null, 0, out _, 0);
                    return new(true, null);
                }
            }
            int veto = 0;
            char* vetoName = stackalloc char[260];
            uint[] targets = DevNodes.Parent(disk.Value) is uint parent ? [parent, disk.Value] : [disk.Value];
            foreach (var node in targets)
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (CM_Request_Device_Eject(node, out veto, vetoName, 260, 0) == 0 && veto == 0) return new(true, null);
                    Thread.Sleep(300);
                }
            }
            return new(false, VetoText(veto));
        }
        catch (Exception e)
        {
            return new(false, e.Message);
        }
    }

    static bool Lock(SafeFileHandle h)
    {
        for (int i = 0; i < 10; i++)
        {
            if (UsbNative.DeviceIoControl(h, FsctlLockVolume, null, 0, null, 0, out _, 0)) return true;
            Thread.Sleep(200);
        }
        return false;
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~CardEjectTests|FullyQualifiedName~DriveLinksTests"` → PASS (DriveLinks testy dál zelené po refaktoru). Celá sada zelená. **Skutečné vysunutí nespouštět** — ověří ho uživatel s kartou (Task 10, checklist).

- [ ] **Step 5: Commit**

Řádek do VYVOJ: `- Úkol 3 — vysunutí: karta ze čtečky jako médium (čtečka zůstane), USB disk jako zařízení; zamčení svazku odhalí otevřené soubory`.

```powershell
git add windows docs
git commit -m "Windows: safe card eject (media for readers, device for USB disks)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Průběh a texty karty úlohy

**Files:**
- Create: `windows/src/Offload.Core/Desktop/Texts.cs`, `windows/src/Offload.Core/Desktop/JobProgress.cs`
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (viditelnost `Plan`/`Report` mezi vlákny, zrušení ve frontě)
- Test: `windows/tests/Offload.Core.Tests/JobProgressTests.cs`

**Interfaces:**
- Consumes: `JobStatus`, `JobPhase`, `ProgressSnapshot`, `OffloadReport`, `ProxyStatus`, `BrakeMeter`, `Brake`, `Format`.
- Produces: `Texts.Plural/Cards/Destinations/Files/SafeToEject/Failed/Clips/Warnings`; `enum CardTone { Waiting, Active, Ok, Warning, Error }`; `JobProgress(DateTime now)` s `Update(JobStatus, DateTime)`, `AttachBrakeMeter(BrakeMeter)`, `Status`, `ReadRate`, `DestRates`, `Eta`, `Brake`, `IsRunning`, `PhaseProgress`, `StatusText`, `Tone`, `DetailText`; statické `JobProgress.EtaText(double)`, `JobProgress.SummaryText(OffloadReport)`. `OffloadJob.Status()` hlásí `Cancelled` pro úlohu zrušenou před startem.

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/JobProgressTests.cs`:

```csharp
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class JobProgressTests
{
    static readonly DateTime T0 = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    static ProgressSnapshot Snap(long read, long[] written, long[]? verified = null, long total = 1_000_000_000) =>
        new(CopyPhase.Copy, total, 10, 1, "DCIM/100CANON/A001.MOV", read, written, verified ?? new long[written.Length], 0);

    static JobStatus St(JobPhase phase, ProgressSnapshot? copy = null) => new() { Phase = phase, Copy = copy };

    static JobProgress Copying()
    {
        var p = new JobProgress(T0);
        p.Update(St(JobPhase.Copying, Snap(0, [0, 0])), T0.AddSeconds(0.5));
        p.Update(St(JobPhase.Copying, Snap(100_000_000, [100_000_000, 50_000_000])), T0.AddSeconds(1.5));
        p.Update(St(JobPhase.Copying, Snap(150_000_000, [150_000_000, 100_000_000])), T0.AddSeconds(2.5));
        return p;
    }

    [Fact]
    public void Rates_ResetOnPhaseChange_ThenSmoothed()
    {
        var p = Copying();
        Assert.Equal(80e6, p.ReadRate, 1);                  // 0.6 × 100 + 0.4 × 50 MB/s
        Assert.Equal(80e6, p.DestRates[0], 1);
        Assert.Equal(50e6, p.DestRates[1], 1);
        Assert.Equal(850e6 / 80e6, p.Eta!.Value, 3);
        p.Update(St(JobPhase.Verifying, Snap(1_000_000_000, [1_000_000_000, 1_000_000_000], [0, 0])), T0.AddSeconds(3.5));
        Assert.Equal(0, p.ReadRate);
        Assert.Equal([0.0, 0.0], p.DestRates);
    }

    [Fact]
    public void Updates_CloserThan200ms_KeepRates()
    {
        var p = Copying();
        p.Update(St(JobPhase.Copying, Snap(900_000_000, [900_000_000, 900_000_000])), T0.AddSeconds(2.6));
        Assert.Equal(80e6, p.ReadRate, 1);
    }

    [Fact]
    public void Verifying_EtaFromTheSlowestDestination()
    {
        var p = new JobProgress(T0);
        p.Update(St(JobPhase.Verifying, Snap(0, [0, 0], [0, 0])), T0.AddSeconds(0.5));
        p.Update(St(JobPhase.Verifying, Snap(0, [0, 0], [200_000_000, 100_000_000])), T0.AddSeconds(1.5));
        Assert.Equal(100e6, p.ReadRate, 1);
        Assert.Equal(9.0, p.Eta!.Value, 3);
        Assert.Equal("D1 200 MB/s · D2 100 MB/s · zbývá 0:09", p.DetailText);
    }

    [Fact]
    public void PhaseProgress_EachPhase()
    {
        var p = new JobProgress(T0);
        p.Update(St(JobPhase.Copying, Snap(250_000_000, [0])), T0);
        Assert.Equal(0.25, p.PhaseProgress, 3);
        p.Update(St(JobPhase.Verifying, Snap(0, [0, 0], [500_000_000, 250_000_000])), T0);
        Assert.Equal(0.375, p.PhaseProgress, 3);
        p.Update(new JobStatus { Phase = JobPhase.Analyzing, ClipIndex = 1, ClipCount = 4, ClipFraction = 0.5 }, T0);
        Assert.Equal(0.375, p.PhaseProgress, 3);
        p.Update(new JobStatus { Phase = JobPhase.Done }, T0);
        Assert.Equal(1, p.PhaseProgress);
        p.Update(new JobStatus(), T0);
        Assert.Equal(0, p.PhaseProgress);
    }

    [Fact]
    public void DetailTexts()
    {
        Assert.Equal("Karta 80 MB/s · D1 80 MB/s · D2 50 MB/s · zbývá 0:10 · A001.MOV", Copying().DetailText);
        var p = new JobProgress(T0);
        p.Update(new JobStatus { Phase = JobPhase.Proxies, ClipIndex = 2, ClipCount = 5, ClipName = "A003", ClipFraction = 0.426 }, T0);
        Assert.Equal("Proxy 3/5 · A003 · 43 %", p.DetailText);
        p.Update(new JobStatus { Phase = JobPhase.Analyzing, ClipIndex = 0, ClipCount = 3, ClipName = "A001" }, T0);
        Assert.Equal("Klip 1/3 · A001", p.DetailText);
        p.Update(new JobStatus { Phase = JobPhase.Flushing }, T0);
        Assert.Equal("Zápis cache disků…", p.DetailText);
    }

    [Theory]
    [InlineData(59, "0:59")]
    [InlineData(600, "10:00")]
    [InlineData(3725, "1:02:05")]
    public void EtaText(double seconds, string text) => Assert.Equal(text, JobProgress.EtaText(seconds));

    [Theory]
    [InlineData(JobPhase.Done, false, "Chyba", CardTone.Error)]
    [InlineData(JobPhase.Done, true, "Hotovo", CardTone.Ok)]
    [InlineData(JobPhase.Failed, null, "Chyba", CardTone.Error)]
    [InlineData(JobPhase.Cancelled, null, "Zrušeno", CardTone.Warning)]
    [InlineData(JobPhase.Copying, null, "Kopírování", CardTone.Active)]
    [InlineData(JobPhase.Waiting, null, "Čeká", CardTone.Waiting)]
    public void StatusText_AndTone(JobPhase phase, bool? copySucceeded, string text, CardTone tone)
    {
        var p = new JobProgress(T0);
        p.Update(new JobStatus { Phase = phase, CopySucceeded = copySucceeded }, T0);
        Assert.Equal(text, p.StatusText);
        Assert.Equal(tone, p.Tone);
    }

    [Fact]
    public void Brake_OnlyWhileCopying()
    {
        const long mb = 1_000_000;
        var p = new JobProgress(T0);
        p.AttachBrakeMeter(new BrakeMeter(16 << 20, 4));
        for (int i = 0; i <= 8; i++)
        {
            long read = i * 50 * mb;
            p.Update(St(JobPhase.Copying, Snap(read, [read, Math.Max(0, read - 48 * mb)])), T0.AddSeconds(i * 0.5));
        }
        Assert.Equal(2, p.Brake?.Element);
        p.Update(St(JobPhase.Verifying, Snap(400 * mb, [400 * mb, 400 * mb])), T0.AddSeconds(5));
        Assert.Null(p.Brake);
    }

    [Fact]
    public void SummaryText()
    {
        var clip = new Clip { Name = "A001", Path = "A001.MOV", Format = ClipFormat.Video, Segments = ["A001.MOV"], Bytes = 1 };
        var report = new OffloadReport
        {
            CardName = "A001", Date = "2026-10-05", Source = @"F:\", SourceFilesystem = "exFAT",
            Started = T0, CopyFinished = T0.AddSeconds(10), Finished = T0.AddSeconds(15),
            Verified = true, TotalBytes = 1_000_000_000, TotalFiles = 3,
            Destinations = [], Files = [], Skipped = [],
            Clips =
            [
                new ClipEntry { Clip = clip, ProxyStatus = ProxyStatus.Done },
                new ClipEntry { Clip = clip, ProxyStatus = ProxyStatus.Failed },
                new ClipEntry { Clip = clip, ProxyStatus = ProxyStatus.Unsupported },
            ],
            Proxy = new ProxySettings { Enabled = true },
        };
        Assert.Equal("kopie 100 MB/s · ověření 200 MB/s · 3 klipy · proxy 1, 1 chyba, 1 nepodporováno", JobProgress.SummaryText(report));
    }

    [Fact]
    public void Texts_CzechPlurals()
    {
        Assert.Equal("Vyberte kartu", Texts.Cards(0));
        Assert.Equal("1 karta", Texts.Cards(1));
        Assert.Equal("3 karty", Texts.Cards(3));
        Assert.Equal("5 karet", Texts.Cards(5));
        Assert.Equal("0 cílů", Texts.Destinations(0));
        Assert.Equal("1 cíl", Texts.Destinations(1));
        Assert.Equal("2 cíle", Texts.Destinations(2));
        Assert.Equal("1 soubor · 1.00 KB", Texts.Files(1, 1000));
        Assert.Equal("12 souborů · 2.50 GB", Texts.Files(12, 2_500_000_000));
        Assert.Equal("Všechny soubory jsou na 1 disku a ověřené — kartu lze vysunout.", Texts.SafeToEject(1));
        Assert.Equal("Všechny soubory jsou na 2 discích a ověřené — kartu lze vysunout.", Texts.SafeToEject(2));
        Assert.Equal("3 soubory selhaly — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.", Texts.Failed(3));
        Assert.Equal("bez varování", Texts.Warnings(0));
        Assert.Equal("2 varování", Texts.Warnings(2));
    }

    // Review focus 5
    [Fact]
    public void CancelledBeforeStart_ReportsCancelled()
    {
        using var dir = new TempDir();
        var job = new OffloadJob(new JobConfig(dir.Sub("card"), [dir.Sub("d1")]));
        job.Cancel();
        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);
    }
}
```

(Pokud `OffloadReport`/`ClipEntry`/`Clip` vyžadují další `required` vlastnosti, doplnit je v testu minimálními hodnotami — beze změny tvrzení.)

- [ ] **Step 2: Spustit, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~JobProgressTests"` → build FAIL.

- [ ] **Step 3: Implementace**

V `OffloadJob.cs`:

```csharp
    volatile OffloadPlan? planValue;
    volatile OffloadReport? reportValue;

    /// Set on a worker thread, read by the app's UI thread.
    public OffloadPlan? Plan { get => planValue; private set => planValue = value; }
    public OffloadReport? Report { get => reportValue; private set => reportValue = value; }
```

(nahrazují původní auto-vlastnosti) a v `Status()` před `return s;`:

```csharp
        // Cancelled while still queued: it never runs, so say so instead of "Čeká" forever.
        if (s.Phase == JobPhase.Waiting && IsCancelled) s = s with { Phase = JobPhase.Cancelled };
```

`windows/src/Offload.Core/Desktop/Texts.cs`:

```csharp
namespace Offload.Core.Desktop;

/// Czech texts with the right plural (1 karta, 3 karty, 5 karet).
public static class Texts
{
    public static string Plural(long n, string one, string few, string many) => n == 1 ? one : n is >= 2 and <= 4 ? few : many;

    public static string Cards(int n) => n == 0 ? "Vyberte kartu" : $"{n} {Plural(n, "karta", "karty", "karet")}";

    public static string Destinations(int n) => $"{n} {Plural(n, "cíl", "cíle", "cílů")}";

    public static string Files(int n, long bytes) => $"{n} {Plural(n, "soubor", "soubory", "souborů")} · {Format.Bytes(bytes)}";

    public static string Clips(int n) => $"{n} {Plural(n, "klip", "klipy", "klipů")}";

    public static string SafeToEject(int disks) =>
        $"Všechny soubory jsou na {disks} {Plural(disks, "disku", "discích", "discích")} a ověřené — kartu lze vysunout.";

    public static string Failed(int n) =>
        $"{n} {Plural(n, "soubor selhal", "soubory selhaly", "souborů selhalo")} — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.";

    public static string Warnings(int n) => n == 0 ? "bez varování" : $"{n} varování";
}
```

`windows/src/Offload.Core/Desktop/JobProgress.cs`:

```csharp
using System.Globalization;
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

public enum CardTone { Waiting, Active, Ok, Warning, Error }

/// What a job card shows, from successive `JobStatus` snapshots (Mac `JobItem.refresh` and the JobCard texts).
public sealed class JobProgress
{
    readonly DateTime started;
    DateTime lastTime;
    long lastBytes;
    long[] lastDest = [];
    JobPhase lastPhase = JobPhase.Waiting;
    BrakeMeter? brakes;

    public JobProgress(DateTime now)
    {
        started = now;
        lastTime = now;
    }

    public JobStatus Status { get; private set; } = new();
    /// Bytes per second, smoothed: card read while copying, slowest destination while verifying.
    public double ReadRate { get; private set; }
    public double[] DestRates { get; private set; } = [];
    public double? Eta { get; private set; }
    /// The element braking the copy (0 = card, i = destination i); only while copying.
    public Brake? Brake { get; private set; }

    public void AttachBrakeMeter(BrakeMeter meter) => brakes = meter;

    public bool IsRunning => Status.Phase != JobPhase.Waiting && !Status.Phase.IsFinished();

    public void Update(JobStatus s, DateTime now)
    {
        double dt = (now - lastTime).TotalSeconds;
        if (s.Copy is { } c && dt > 0.2)
        {
            bool copying = s.Phase == JobPhase.Copying;
            long bytes = copying ? c.ReadBytes : (c.VerifiedBytes.Length > 0 ? c.VerifiedBytes.Min() : 0);
            var dests = copying ? c.WrittenBytes : c.VerifiedBytes;
            if (s.Phase != lastPhase || lastDest.Length != dests.Length)
            {
                ReadRate = 0;
                DestRates = new double[dests.Length];
            }
            else
            {
                ReadRate = Smooth(ReadRate, (bytes - lastBytes) / dt);
                var rates = new double[dests.Length];
                for (int i = 0; i < dests.Length; i++) rates[i] = Smooth(DestRates[i], (dests[i] - lastDest[i]) / dt);
                DestRates = rates;
                Eta = ReadRate > 1 ? (c.TotalBytes - bytes) / ReadRate : null;
            }
            lastBytes = bytes;
            lastDest = dests.ToArray();
            lastTime = now;
        }
        Brake = s.Phase == JobPhase.Copying && s.Copy is { } cc && brakes != null
            ? brakes.Add((now - started).TotalSeconds, cc.ReadBytes, cc.WrittenBytes) ?? Brake
            : null;
        lastPhase = s.Phase;
        Status = s;
    }

    static double Smooth(double old, double v) => old == 0 ? v : old * 0.6 + v * 0.4;

    /// 0…1 within the current phase.
    public double PhaseProgress => Status switch
    {
        { Phase: JobPhase.Copying, Copy: { TotalBytes: > 0 } c } => (double)c.ReadBytes / c.TotalBytes,
        { Phase: JobPhase.Verifying, Copy: { TotalBytes: > 0 } v } =>
            v.VerifiedBytes.Sum() / ((double)v.TotalBytes * Math.Max(1, v.VerifiedBytes.Length)),
        { Phase: JobPhase.Analyzing or JobPhase.Proxies, ClipCount: > 0 } s => (s.ClipIndex + s.ClipFraction) / s.ClipCount,
        { Phase: JobPhase.Done } => 1,
        _ => 0,
    };

    public string StatusText => Status.Phase == JobPhase.Done && Status.CopySucceeded == false ? "Chyba" : Status.Phase.Label();

    public CardTone Tone => Status.Phase switch
    {
        JobPhase.Done => Status.CopySucceeded == false ? CardTone.Error : CardTone.Ok,
        JobPhase.Failed => CardTone.Error,
        JobPhase.Cancelled => CardTone.Warning,
        JobPhase.Waiting => CardTone.Waiting,
        _ => CardTone.Active,
    };

    static string Mb(double bytesPerSecond) => (bytesPerSecond / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s";

    public static string EtaText(double seconds)
    {
        int i = (int)seconds;
        return i >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{i / 3600}:{i / 60 % 60:00}:{i % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{i / 60}:{i % 60:00}");
    }

    public string DetailText
    {
        get
        {
            var s = Status;
            var eta = Eta is double t ? " · zbývá " + EtaText(t) : "";
            string Dests() => string.Join(" · ", DestRates.Select((r, i) => $"D{i + 1} {Mb(r)}"));
            return s.Phase switch
            {
                JobPhase.Copying => $"Karta {Mb(ReadRate)} · {Dests()}{eta} · {Path.GetFileName(s.Copy?.CurrentFile ?? "")}",
                JobPhase.Verifying => Dests() + eta,
                JobPhase.Analyzing => $"Klip {s.ClipIndex + 1}/{s.ClipCount} · {s.ClipName}",
                JobPhase.Proxies =>
                    $"Proxy {s.ClipIndex + 1}/{s.ClipCount} · {s.ClipName} · {(s.ClipFraction * 100).ToString("0", CultureInfo.InvariantCulture)} %",
                _ => s.Phase.Label() + "…",
            };
        }
    }

    public static string SummaryText(OffloadReport r)
    {
        var parts = new List<string> { "kopie " + Format.Rate(r.TotalBytes, r.CopySeconds) };
        if (r.Verified) parts.Add("ověření " + Format.Rate(r.TotalBytes, r.VerifySeconds));
        if (r.Clips.Count > 0) parts.Add(Texts.Clips(r.Clips.Count));
        if (r.Proxy?.Enabled == true)
        {
            int Count(ProxyStatus st) => r.Clips.Count(c => c.ProxyStatus == st);
            var proxy = $"proxy {Count(ProxyStatus.Done)}";
            if (Count(ProxyStatus.Failed) > 0) proxy += $", {Count(ProxyStatus.Failed)} chyba";
            if (Count(ProxyStatus.Unsupported) > 0) proxy += $", {Count(ProxyStatus.Unsupported)} nepodporováno";
            parts.Add(proxy);
        }
        return string.Join(" · ", parts);
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~JobProgressTests|FullyQualifiedName~JobTests"` → PASS. Celá sada zelená.

- [ ] **Step 5: Commit**

Řádek do VYVOJ: `- Úkol 4 — karta úlohy v jádru: rychlosti a odhad času jako na Macu, průběh fáze, texty s českým množným číslem, úzké hrdlo; zrušená úloha ve frontě hlásí „Zrušeno“`.

```powershell
git add windows docs
git commit -m "Windows: job card progress and texts in Core" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Fronta úloh

**Files:**
- Create: `windows/src/Offload.Core/Desktop/JobQueue.cs`
- Test: `windows/tests/Offload.Core.Tests/JobQueueTests.cs`

**Interfaces:**
- Consumes: `OffloadJob.RunCopy/RunPost/IsCancelled`.
- Produces: `interface IQueuedJob { bool IsCancelled; bool RunCopy(); void RunPost(); }`; `JobQueue(Action<string>? log = null)` s `Enqueue(IQueuedJob)`, událostmi `CopyFinished`, `JobFinished` (`Action<IQueuedJob>`, na pracovních vláknech), `Dispose()`; `JobQueue.Runner(OffloadJob job, Action<string>? log) : IQueuedJob` s vlastností `Job`.

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/JobQueueTests.cs`:

```csharp
using System.Collections.Concurrent;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

public class JobQueueTests
{
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    sealed class FakeJob(string name, bool post = false) : IQueuedJob
    {
        public readonly string Name = name;
        public readonly ManualResetEventSlim CopyStarted = new(), CopyGate = new(true), PostStarted = new(), PostGate = new(true);
        public volatile bool Cancelled;
        public Exception? Throw;
        public int CopyCalls;
        public bool IsCancelled => Cancelled;

        public bool RunCopy()
        {
            Interlocked.Increment(ref CopyCalls);
            CopyStarted.Set();
            CopyGate.Wait(Wait);
            if (Throw != null) throw Throw;
            return post;
        }

        public void RunPost()
        {
            PostStarted.Set();
            PostGate.Wait(Wait);
        }
    }

    [Fact]
    public void Copies_RunOneAfterAnother()
    {
        using var q = new JobQueue();
        var a = new FakeJob("a");
        var b = new FakeJob("b");
        a.CopyGate.Reset();
        q.Enqueue(a);
        q.Enqueue(b);
        Assert.True(a.CopyStarted.Wait(Wait));
        Assert.False(b.CopyStarted.Wait(300));
        a.CopyGate.Set();
        Assert.True(b.CopyStarted.Wait(Wait));
    }

    [Fact]
    public void NextCopy_RunsWhilePreviousProxiesRender()
    {
        using var q = new JobQueue();
        var a = new FakeJob("a", post: true);
        var b = new FakeJob("b");
        a.PostGate.Reset();
        q.Enqueue(a);
        q.Enqueue(b);
        Assert.True(a.PostStarted.Wait(Wait));
        Assert.True(b.CopyStarted.Wait(Wait));
        a.PostGate.Set();
    }

    [Fact]
    public void Events_CopyFinishedBeforeJobFinished_ForEveryJob()
    {
        using var q = new JobQueue();
        var events = new ConcurrentQueue<string>();
        using var done = new CountdownEvent(2);
        q.CopyFinished += j => events.Enqueue("copy:" + ((FakeJob)j).Name);
        q.JobFinished += j => { events.Enqueue("done:" + ((FakeJob)j).Name); done.Signal(); };
        q.Enqueue(new FakeJob("a"));
        q.Enqueue(new FakeJob("c", post: true));
        Assert.True(done.Wait(Wait));
        var list = events.ToList();
        Assert.True(list.IndexOf("copy:a") < list.IndexOf("done:a"));
        Assert.True(list.IndexOf("copy:c") < list.IndexOf("done:c"));
    }

    // Review focus 5
    [Fact]
    public void CancelledBeforeStart_Skipped_ButFinished()
    {
        using var q = new JobQueue();
        var a = new FakeJob("a") { Cancelled = true };
        var copied = false;
        using var finished = new ManualResetEventSlim();
        q.CopyFinished += _ => copied = true;
        q.JobFinished += _ => finished.Set();
        q.Enqueue(a);
        Assert.True(finished.Wait(Wait));
        Assert.Equal(0, a.CopyCalls);
        Assert.False(copied);
    }

    // Review focus 5
    [Fact]
    public void ThrowingJob_DoesNotStopTheQueue()
    {
        var log = new ConcurrentQueue<string>();
        using var q = new JobQueue(log.Enqueue);
        var a = new FakeJob("a") { Throw = new InvalidOperationException("boom") };
        var b = new FakeJob("b");
        using var bDone = new ManualResetEventSlim();
        q.JobFinished += j => { if (j == b) bDone.Set(); };
        q.Enqueue(a);
        q.Enqueue(b);
        Assert.True(bDone.Wait(Wait));
        Assert.Contains(log, l => l.Contains("boom"));
    }

    [Fact]
    public void Runner_RunsARealOffloadJob()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestFs.WriteRandom(Path.Combine(card, "A001.MOV"), 100_000, 1);
        var config = new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-05", AnalyzeClips = false };
        var job = new OffloadJob(config, new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true });
        using var q = new JobQueue();
        using var finished = new ManualResetEventSlim();
        q.JobFinished += _ => finished.Set();
        q.Enqueue(new JobQueue.Runner(job, null));
        Assert.True(finished.Wait(TimeSpan.FromSeconds(30)));
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(File.Exists(Path.Combine(dir.Path, "d1", "2026-10-05", "card", "A001.MOV")));
    }
}
```

- [ ] **Step 2: Spustit, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~JobQueueTests"` → build FAIL.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Desktop/JobQueue.cs`:

```csharp
using System.Collections.Concurrent;

namespace Offload.Core.Desktop;

/// A job as the queue runs it; `JobQueue.Runner` adapts `OffloadJob`, tests use fakes.
public interface IQueuedJob
{
    bool IsCancelled { get; }
    /// Stage 1 (card → disks + verification). True when stage 2 should follow.
    bool RunCopy();
    /// Stage 2 (clips, proxies, PDF) — reads only the destinations.
    void RunPost();
}

/// Copies run one after another (they share the destination disks); stage 2 runs on its own thread, so the next
/// card copies while the previous one's proxies render (Mac copyQueue + postQueue). Events fire on the worker
/// threads; a job cancelled while queued is skipped but still reported as finished.
public sealed class JobQueue : IDisposable
{
    readonly BlockingCollection<IQueuedJob> copies = new();
    readonly BlockingCollection<IQueuedJob> posts = new();
    readonly Action<string>? log;

    public event Action<IQueuedJob>? CopyFinished;
    public event Action<IQueuedJob>? JobFinished;

    public JobQueue(Action<string>? log = null)
    {
        this.log = log;
        new Thread(CopyLoop) { IsBackground = true, Name = "offload-copy" }.Start();
        new Thread(PostLoop) { IsBackground = true, Name = "offload-post" }.Start();
    }

    public void Enqueue(IQueuedJob job) => copies.Add(job);

    void CopyLoop()
    {
        foreach (var job in copies.GetConsumingEnumerable())
        {
            bool post = false;
            if (!job.IsCancelled)
            {
                post = Guard(job.RunCopy, "copy");
                Raise(CopyFinished, job);
            }
            if (post && TryAddPost(job)) continue;
            Raise(JobFinished, job);
        }
    }

    bool TryAddPost(IQueuedJob job)
    {
        try
        {
            return posts.TryAdd(job);
        }
        catch (InvalidOperationException)
        {
            return false;   // shutting down
        }
    }

    void PostLoop()
    {
        foreach (var job in posts.GetConsumingEnumerable())
        {
            Guard(() => { job.RunPost(); return true; }, "post");
            Raise(JobFinished, job);
        }
    }

    bool Guard(Func<bool> body, string stage)
    {
        try
        {
            return body();
        }
        catch (Exception e)
        {
            log?.Invoke($"{stage}: {e.Message}");
            return false;
        }
    }

    void Raise(Action<IQueuedJob>? handler, IQueuedJob job)
    {
        try
        {
            handler?.Invoke(job);
        }
        catch (Exception e)
        {
            log?.Invoke($"event: {e.Message}");
        }
    }

    /// Stops taking jobs; a job that is running finishes on its own (cancel it first to stop it).
    public void Dispose()
    {
        copies.CompleteAdding();
        posts.CompleteAdding();
    }

    public sealed class Runner(OffloadJob job, Action<string>? log) : IQueuedJob
    {
        public OffloadJob Job { get; } = job;
        public bool IsCancelled => Job.IsCancelled;
        public bool RunCopy() => Job.RunCopy(log);
        public void RunPost() => Job.RunPost(log);
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~JobQueueTests"` → PASS (6). Celá sada zelená.

- [ ] **Step 5: Commit**

Řádek do VYVOJ: `- Úkol 5 — fronta: karty se kopírují za sebou, proxy předchozí karty se renderují souběžně s kopií další; zrušená nebo padající úloha frontu nezastaví`.

```powershell
git add windows docs
git commit -m "Windows: job queue (serial copies, parallel stage 2)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Projekt aplikace, okno a nástroje pro ověření

**Files:**
- Create: `windows/src/Offload.App/Offload.App.csproj`, `App.xaml`, `App.xaml.cs`, `AppLog.cs`, `Mvvm.cs`, `Converters.cs`, `Dialogs.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `SetupPanel.xaml`, `SetupPanel.xaml.cs`, `QueuePanel.xaml`, `QueuePanel.xaml.cs`, `MainViewModel.cs`
- Create: `windows/scripts/app-drive.ps1`
- Modify: `windows/Offload.sln`, `windows/scripts/build.ps1`

**Interfaces:**
- Consumes: `AppSettings`, `SettingsStore`, `StartupOptions`, `AppData`.
- Produces: `ObservableObject` (`Raise([CallerMemberName])`, `RaiseMany(params string[])`, `Set<T>(ref T, T, [CallerMemberName])`); `RelayCommand(Action, Func<bool>?)`; `AppLog.Write(string)`; `Dialogs.PickFolder(string title) -> string?`, `Dialogs.PickFile(string title, string filter) -> string?`; konvertory `BoolToVisible` (vestavěný), `BoolToCollapsed`, `NullToCollapsed`, `EmptyToVisible`; `MainViewModel(StartupOptions)` s `Settings`, `Options`, `SettingsChanged()`, `SaveNow()`, `ShowMessage(string)`, událostí `MessageRequested`; `SetupPanel` a `QueuePanel` (UserControl); PowerShell funkce `Start-Offload`, `Find-OffloadElement`, `Invoke-OffloadButton`, `Find-OffloadWindow`, `Save-OffloadScreenshot`, `Stop-Offload`.

- [ ] **Step 1: Projekt a sestavení**

`windows/src/Offload.App/Offload.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <UseWPF>true</UseWPF>
    <AssemblyName>Offload</AssemblyName>
    <RootNamespace>Offload.App</RootNamespace>
    <!-- ThemeMode (Fluent) is marked experimental in WPF. -->
    <NoWarn>$(NoWarn);WPF0001</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Offload.Core\Offload.Core.csproj" />
  </ItemGroup>
</Project>
```

```powershell
dotnet sln Offload.sln add src\Offload.App\Offload.App.csproj --solution-folder src
```

`windows/src/Offload.App/App.xaml`:

```xml
<Application x:Class="Offload.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Offload.App"
             ThemeMode="System"
             ShutdownMode="OnMainWindowClose">
    <Application.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVisible" />
        <local:BoolToCollapsedConverter x:Key="BoolToCollapsed" />
        <local:NullToCollapsedConverter x:Key="NullToCollapsed" />
        <local:EmptyToVisibleConverter x:Key="EmptyToVisible" />
        <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
        <Style x:Key="SectionTitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="15" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="VerticalAlignment" Value="Center" />
        </Style>
        <Style x:Key="Caption" TargetType="TextBlock">
            <Setter Property="FontSize" Value="12" />
            <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
            <Setter Property="TextTrimming" Value="CharacterEllipsis" />
        </Style>
        <Style x:Key="Icon" TargetType="TextBlock">
            <Setter Property="FontFamily" Value="{StaticResource IconFont}" />
            <Setter Property="FontSize" Value="16" />
            <Setter Property="VerticalAlignment" Value="Center" />
        </Style>
        <Style x:Key="Tile" TargetType="Border">
            <Setter Property="CornerRadius" Value="8" />
            <Setter Property="Padding" Value="10" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="BorderBrush" Value="Transparent" />
            <Setter Property="Background" Value="{DynamicResource SubtleFillColorSecondaryBrush}" />
        </Style>
        <Style x:Key="WarningText" TargetType="TextBlock">
            <Setter Property="FontSize" Value="12" />
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="Foreground" Value="{DynamicResource SystemFillColorCautionBrush}" />
        </Style>
        <Style x:Key="ErrorText" TargetType="TextBlock" BasedOn="{StaticResource WarningText}">
            <Setter Property="Foreground" Value="{DynamicResource SystemFillColorCriticalBrush}" />
        </Style>
    </Application.Resources>
</Application>
```

`windows/src/Offload.App/App.xaml.cs`:

```csharp
using System.Windows;
using Offload.Core.Desktop;

namespace Offload.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A bug in the UI must never take running offloads down: log it, tell the user, keep going.
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Write(ex.Exception.ToString());
            MessageBox.Show($"Neočekávaná chyba aplikace: {ex.Exception.Message}\n\nPodrobnosti: {AppLog.Path}", "Offload",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Write(ex.ExceptionObject.ToString() ?? "");
        var window = new MainWindow(new MainViewModel(StartupOptions.Parse(e.Args)));
        MainWindow = window;
        window.Show();
    }
}
```

`windows/src/Offload.App/AppLog.cs`:

```csharp
using System.Globalization;
using Offload.Core.Desktop;

namespace Offload.App;

/// %APPDATA%\Offload\app.log — the app's own problems (job details are in each job's report).
static class AppLog
{
    static readonly Lock Gate = new();

    public static string Path => System.IO.Path.Combine(AppData.Folder, "app.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppData.Folder);
                File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never fail the app.
        }
    }
}
```

`windows/src/Offload.App/Mvvm.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Offload.App;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void RaiseMany(params string[] names)
    {
        foreach (var n in names) Raise(n);
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();
}
```

`windows/src/Offload.App/Converters.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Offload.App;

public sealed class BoolToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
```

`windows/src/Offload.App/Dialogs.cs`:

```csharp
using System.Windows;
using Microsoft.Win32;

namespace Offload.App;

static class Dialogs
{
    static Window? Owner => Application.Current?.MainWindow;

    public static string? PickFolder(string title)
    {
        var d = new OpenFolderDialog { Title = title, Multiselect = false };
        return (Owner is { } o ? d.ShowDialog(o) : d.ShowDialog()) == true ? d.FolderName : null;
    }

    public static string? PickFile(string title, string filter)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return (Owner is { } o ? d.ShowDialog(o) : d.ShowDialog()) == true ? d.FileName : null;
    }
}
```

`windows/src/Offload.App/MainViewModel.cs`:

```csharp
using System.Windows.Threading;
using Offload.Core.Desktop;

namespace Offload.App;

/// The window's state. Split by area: Setup (left panel), Queue (right panel), Hardware.
public sealed partial class MainViewModel : ObservableObject
{
    readonly string settingsPath;
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public MainViewModel(StartupOptions options, string? settingsPath = null)
    {
        Options = options;
        this.settingsPath = settingsPath ?? SettingsStore.DefaultPath;
        Settings = SettingsStore.Load(this.settingsPath);
        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            SaveNow();
        };
    }

    public StartupOptions Options { get; }
    public AppSettings Settings { get; }

    public event Action<string>? MessageRequested;

    /// Settings changed: written 400 ms after the last change, and on exit.
    public void SettingsChanged()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    public void SaveNow()
    {
        saveTimer.Stop();
        try
        {
            SettingsStore.Save(Settings, settingsPath);
        }
        catch (Exception e)
        {
            AppLog.Write($"settings save: {e.Message}");
        }
    }

    public void ShowMessage(string text) => MessageRequested?.Invoke(text);
}
```

`windows/src/Offload.App/MainWindow.xaml`:

```xml
<Window x:Class="Offload.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Offload.App"
        Title="Offload" Width="1240" Height="860" MinWidth="940" MinHeight="640"
        WindowStartupLocation="CenterScreen">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="400" MinWidth="360" MaxWidth="480" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" MinWidth="520" />
        </Grid.ColumnDefinitions>
        <local:SetupPanel Grid.Column="0" />
        <GridSplitter Grid.Column="1" Width="5" HorizontalAlignment="Stretch" Background="{DynamicResource DividerStrokeColorDefaultBrush}" />
        <local:QueuePanel Grid.Column="2" />
    </Grid>
</Window>
```

`windows/src/Offload.App/MainWindow.xaml.cs`:

```csharp
using System.Windows;

namespace Offload.App;

public partial class MainWindow : Window
{
    readonly MainViewModel vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        DataContext = vm;
        vm.MessageRequested += text => MessageBox.Show(this, text, "Offload");
        Closed += (_, _) => vm.SaveNow();
    }
}
```

`windows/src/Offload.App/SetupPanel.xaml` (sekce doplní Task 7):

```xml
<UserControl x:Class="Offload.App.SetupPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel x:Name="Root" Background="{DynamicResource LayerFillColorDefaultBrush}">
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <StackPanel x:Name="Sections" Margin="18" />
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`windows/src/Offload.App/QueuePanel.xaml` (frontu doplní Task 8):

```xml
<UserControl x:Class="Offload.App.QueuePanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel>
        <Border DockPanel.Dock="Top" Padding="20,14" BorderThickness="0,0,0,1" BorderBrush="{DynamicResource DividerStrokeColorDefaultBrush}">
            <TextBlock Text="Fronta" FontSize="20" FontWeight="SemiBold" />
        </Border>
        <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
            <TextBlock Style="{StaticResource Icon}" Text="&#xE7B8;" FontSize="40" HorizontalAlignment="Center" Foreground="{DynamicResource TextFillColorTertiaryBrush}" />
            <TextBlock Text="Zatím žádný offload" FontSize="17" Margin="0,10,0,0" HorizontalAlignment="Center" Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
            <TextBlock Text="Vyberte kartu a cíle vlevo a stiskněte Ctrl+Enter." Margin="0,6,0,0" HorizontalAlignment="Center" Foreground="{DynamicResource TextFillColorTertiaryBrush}" />
        </StackPanel>
    </DockPanel>
</UserControl>
```

`SetupPanel.xaml.cs` a `QueuePanel.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Offload.App;

public partial class SetupPanel : UserControl
{
    public SetupPanel() => InitializeComponent();
}
```

(stejně `QueuePanel`).

- [ ] **Step 2: Nástroje pro ověření GUI**

`windows/scripts/app-drive.ps1`:

```powershell
# Development helpers for the GUI: start Offload.exe, press its buttons (UI Automation), take screenshots.
#   . .\scripts\app-drive.ps1
#   $env:OFFLOAD_SETTINGS_DIR = 'D:\tmp\offload-settings'   # isolated settings for the run
#   $p = Start-Offload -Exe .\src\Offload.App\bin\Debug\net10.0-windows\Offload.exe -Arguments '--no-hw-prompt'
#   Invoke-OffloadButton $p 'StartButton'
#   Save-OffloadScreenshot $p 'D:\tmp\shot.png'
#   Stop-Offload $p
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OffloadWin {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[OffloadWin]::SetProcessDPIAware() | Out-Null
$AE = [System.Windows.Automation.AutomationElement]

function Start-Offload([string]$Exe, [string]$Arguments = '', [int]$TimeoutSeconds = 30) {
    $p = if ($Arguments) { Start-Process $Exe -ArgumentList $Arguments -PassThru } else { Start-Process $Exe -PassThru }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($p.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200; $p.Refresh() }
    if ($p.MainWindowHandle -eq 0) { throw 'Offload window did not appear' }
    Start-Sleep -Milliseconds 1000
    $p
}

# Top-level windows of the process. Message boxes are Win32 dialogs (class #32770) titled "Offload" like the
# main window, so they are told apart by class: -Dialog picks message boxes, without it they are skipped.
function Get-OffloadWindows($Process, [switch]$Dialog) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $Process.Id)
    @($AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) |
        Where-Object { ($_.Current.ClassName -eq '#32770') -eq [bool]$Dialog }
}

function Find-OffloadWindow($Process, [string]$TitlePattern = 'Offload', [switch]$Dialog, [int]$TimeoutSeconds = 10) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $w = Get-OffloadWindows $Process -Dialog:$Dialog | Where-Object { $_.Current.Name -like $TitlePattern } | Select-Object -First 1
        if ($w) { return $w }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "window '$TitlePattern' not found"
}

function Find-OffloadElement($Process, [string]$AutomationId, [string]$WindowTitle = 'Offload', [switch]$Dialog, [int]$TimeoutSeconds = 10) {
    $window = Find-OffloadWindow $Process $WindowTitle -Dialog:$Dialog -TimeoutSeconds $TimeoutSeconds
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $AutomationId)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $el = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "element '$AutomationId' not found in '$WindowTitle'"
}

function Invoke-OffloadButton($Process, [string]$AutomationId, [string]$WindowTitle = 'Offload', [switch]$Dialog) {
    $el = Find-OffloadElement $Process $AutomationId $WindowTitle -Dialog:$Dialog
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# Presses a message-box button: '6' = Ano/Yes, '7' = Ne/No, '2' = OK.
function Invoke-OffloadDialogButton($Process, [string]$AutomationId = '6') {
    Invoke-OffloadButton $Process $AutomationId '*' -Dialog
}

# PrintWindow renders the window itself — works even when it is covered by other windows.
function Save-OffloadScreenshot($Process, [string]$Path, [string]$WindowTitle = 'Offload', [switch]$Dialog) {
    $h = [IntPtr]([int](Find-OffloadWindow $Process $WindowTitle -Dialog:$Dialog).Current.NativeWindowHandle)
    $r = New-Object OffloadWin+RECT
    [OffloadWin]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [OffloadWin]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Stop-Offload($Process) {
    if ($Process.HasExited) { return }
    $Process.CloseMainWindow() | Out-Null
    if (-not $Process.WaitForExit(10000)) { $Process.Kill() }
}
```

V `windows/scripts/build.ps1` za publikování CLI (před ffmpeg) přidat:

```powershell
# GUI: Offload.exe next to bin\ (single file; WPF native libraries are extracted at first start).
dotnet publish (Join-Path $root 'src\Offload.App\Offload.App.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (app) failed' }
```

a upravit komentář na začátku souboru („Builds windows\build\Offload: Offload.exe (GUI) + bin\offload.exe …“).

- [ ] **Step 3: Sestavit a podívat se na okno**

```powershell
dotnet build
dotnet test
. .\scripts\app-drive.ps1
$env:OFFLOAD_SETTINGS_DIR = "$env:TEMP\offload-gui-task6"
$p = Start-Offload -Exe .\src\Offload.App\bin\Debug\net10.0-windows\Offload.exe
Save-OffloadScreenshot $p "$env:TEMP\offload-gui-task6\window.png"
Stop-Offload $p
"exit $($p.ExitCode)"
```

Expected: build bez varování, testy zelené; snímek (prohlédnout nástrojem Read) ukazuje okno „Offload“ ~1240×860 se dvěma panely: vlevo prázdný panel, vpravo „Fronta“ a prázdný stav „Zatím žádný offload“ / „Vyberte kartu a cíle vlevo a stiskněte Ctrl+Enter.“; barvy podle systému (světlý/tmavý). Aplikace se zavře, exit 0. `scripts\build.ps1` vytvoří `build\Offload\Offload.exe` a `build\Offload\bin\offload.exe`; zkusit spustit `build\Offload\Offload.exe` stejným způsobem a vyfotit. Smazat `%TEMP%\offload-gui-task6`.

Pokud některý `DynamicResource` klíč Fluent theme neexistuje (prvek bez barvy), najít správný název v `PresentationFramework.Fluent` (např. `Fluent.xaml`) a opravit — popsat v reportu.

- [ ] **Step 4: Commit**

Řádek do VYVOJ: `- Úkol 6 — projekt GUI Offload.App (WPF, Fluent, světlý/tmavý režim), okno se dvěma panely, build.ps1 přidá Offload.exe, skript pro ovládání a snímky GUI`.

```powershell
git add windows docs
git commit -m "Windows: WPF app project, main window frame, GUI drive script" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Levý panel — karty, cíle, struktura, proxy, dekodéry

**Files:**
- Create: `windows/src/Offload.App/MainViewModel.Setup.cs`, `SourceItemViewModel.cs`, `DestinationViewModel.cs`, `ProxyOptionsViewModel.cs`, `ToolRowViewModel.cs`, `VolumeWatcher.cs`
- Modify: `windows/src/Offload.App/SetupPanel.xaml` (celý), `MainViewModel.cs` (volání `InitSetup()`), `MainWindow.xaml.cs` (VolumeWatcher)

**Interfaces:**
- Consumes: `Volumes`, `VolumeItem`, `Layout`, `Format`, `NativeIO`, `ProxyPreflight`, `SettingsStore`, `Dialogs`.
- Produces (pro Task 8/9): v `MainViewModel` — pole `selected` (HashSet), `cardNames`, `manualSources`, `volumes`; `Sources`, `Destinations`, `Tools`, `Proxy`; `RefreshVolumes()`, `RebuildSources()`, `DateString`, `LutProblem`, `ToolsWarning`, událost `SetupChanged`; `DestinationViewModel.Exists`; `SourceItemViewModel.SafeDefaultName(string)`. AutomationId: `AddSourceButton`, `AddDestinationButton`, `RefreshVolumesButton`.

- [ ] **Step 1: ViewModely**

`windows/src/Offload.App/MainViewModel.Setup.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Globalization;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

public sealed partial class MainViewModel
{
    readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> cardNames = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> manualSources = [];
    List<VolumeItem> volumes = [];
    ExternalTools resolvedTools = new();
    bool scanning, rescan, useCustomDate;
    DateTime? customDate = DateTime.Today;

    public ObservableCollection<SourceItemViewModel> Sources { get; } = [];
    public ObservableCollection<DestinationViewModel> Destinations { get; } = [];
    public ObservableCollection<ToolRowViewModel> Tools { get; } = [];
    public ProxyOptionsViewModel Proxy { get; private set; } = null!;
    public RelayCommand RefreshVolumesCommand { get; private set; } = null!;
    public RelayCommand AddSourceCommand { get; private set; } = null!;
    public RelayCommand AddDestinationCommand { get; private set; } = null!;

    /// Selection, destinations or proxy changed — the start bar listens (Task 8).
    public event Action? SetupChanged;

    void InitSetup()
    {
        Proxy = new ProxyOptionsViewModel(Settings.Proxy, this);
        RefreshVolumesCommand = new RelayCommand(RefreshVolumes);
        AddSourceCommand = new RelayCommand(AddManualSource);
        AddDestinationCommand = new RelayCommand(AddDestination);
        foreach (var path in Options.Sources) AddManualSourcePath(path);
        RebuildDestinations();
        RefreshTools();
        RefreshVolumes();
    }

    // ── Karty ──────────────────────────────────────────────────────────────

    public bool HasSources => Sources.Count > 0;

    /// Rescans volumes on a background thread; a request during a scan runs once more afterwards.
    public void RefreshVolumes()
    {
        if (scanning)
        {
            rescan = true;
            return;
        }
        scanning = true;
        var dests = Settings.DestRoots.ToList();
        Task.Run(() => Volumes.Scan(dests)).ContinueWith(t =>
        {
            scanning = false;
            if (t.IsCompletedSuccessfully) ApplyVolumes(t.Result);
            else AppLog.Write($"volume scan: {t.Exception?.GetBaseException().Message}");
            if (rescan)
            {
                rescan = false;
                RefreshVolumes();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void ApplyVolumes(List<VolumeItem> found)
    {
        var next = Volumes.SelectionAfterRefresh(volumes.Select(v => v.Path), found, selected, manualSources);
        volumes = found;
        selected.Clear();
        selected.UnionWith(next);
        RebuildSources();
        RebuildDestinations();   // a destination disk may just have been connected
    }

    void RebuildSources()
    {
        Sources.Clear();
        foreach (var v in volumes.Where(v => !v.IsDestination))
            Sources.Add(new SourceItemViewModel(this, v.Path, v.Name,
                $"{Format.Bytes(v.Total - v.Free)} · {v.Format}{(v.LooksLikeCard ? "" : " · nevypadá jako karta")}",
                v.LooksLikeCard ? SourceKind.Card : SourceKind.Drive));
        foreach (var p in manualSources)
            Sources.Add(new SourceItemViewModel(this, p, Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p, p, SourceKind.Folder));
        Raise(nameof(HasSources));
        SetupChanged?.Invoke();
    }

    void AddManualSource()
    {
        if (Dialogs.PickFolder("Vyber kartu nebo složku se záznamem") is string path) AddManualSourcePath(path);
    }

    void AddManualSourcePath(string path)
    {
        if (!manualSources.Contains(path, StringComparer.OrdinalIgnoreCase)) manualSources.Add(path);
        selected.Add(path);
        RebuildSources();
    }

    internal bool IsSelected(string path) => selected.Contains(path);

    internal void SetSelected(string path, bool on)
    {
        if (on) selected.Add(path);
        else selected.Remove(path);
        SetupChanged?.Invoke();
    }

    internal string CardName(string path) => cardNames.GetValueOrDefault(path, "");

    internal void SetCardName(string path, string name) => cardNames[path] = name;

    // ── Cíle ───────────────────────────────────────────────────────────────

    public bool HasNoDestinations => Destinations.Count == 0;
    public bool SingleDestination => Destinations.Count == 1;

    void AddDestination()
    {
        if (Dialogs.PickFolder("Vyber cílovou složku (root)") is not string path) return;
        if (!Settings.DestRoots.Contains(path, StringComparer.OrdinalIgnoreCase)) Settings.DestRoots.Add(path);
        DestinationsChanged();
    }

    internal void RemoveDestination(string root)
    {
        Settings.DestRoots.RemoveAll(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase));
        DestinationsChanged();
    }

    void DestinationsChanged()
    {
        SettingsChanged();
        RebuildDestinations();
        RefreshVolumes();   // a disk holding a destination is no longer offered as a card
    }

    void RebuildDestinations()
    {
        Destinations.Clear();
        for (int i = 0; i < Settings.DestRoots.Count; i++) Destinations.Add(new DestinationViewModel(this, i + 1, Settings.DestRoots[i]));
        RaiseMany(nameof(HasNoDestinations), nameof(SingleDestination));
        SetupChanged?.Invoke();
    }

    // ── Struktura a kontrola ───────────────────────────────────────────────

    public bool UseCustomDate
    {
        get => useCustomDate;
        set { if (Set(ref useCustomDate, value)) Raise(nameof(PathPreview)); }
    }

    public DateTime? CustomDate
    {
        get => customDate;
        set { if (Set(ref customDate, value)) Raise(nameof(PathPreview)); }
    }

    public string DateString => UseCustomDate && CustomDate is DateTime d
        ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : Layout.Today();

    public string PathPreview => $@"…\{DateString}\<název karty>\";

    public bool Verify
    {
        get => Settings.Verify;
        set { Settings.Verify = value; SettingsChanged(); Raise(); }
    }

    public bool AnalyzeClips
    {
        get => Settings.AnalyzeClips;
        set { Settings.AnalyzeClips = value; SettingsChanged(); RaiseMany(nameof(AnalyzeClips), nameof(LutProblem), nameof(ToolsWarning)); SetupChanged?.Invoke(); }
    }

    // ── Proxy a dekodéry ───────────────────────────────────────────────────

    /// A missing LUT blocks the start (red); missing ffmpeg only warns (orange).
    public string? LutProblem => ProxyPreflight.Blocking(Settings.Proxy, Settings.AnalyzeClips);
    public string? ToolsWarning => ProxyPreflight.Warning(Settings.Proxy, Settings.AnalyzeClips, resolvedTools);

    internal void ProxyChanged()
    {
        SettingsChanged();
        RaiseMany(nameof(LutProblem), nameof(ToolsWarning));
        SetupChanged?.Invoke();
    }

    void RefreshTools()
    {
        resolvedTools = Settings.Tools.Resolved();
        Tools.Clear();
        void Row(string name, string purpose, string? path, Action<string> set) =>
            Tools.Add(new ToolRowViewModel(name, purpose, path, () =>
            {
                if (Dialogs.PickFile($"Vyber {name}", "Programy (*.exe)|*.exe") is not string picked) return;
                set(picked);
                SettingsChanged();
                RefreshTools();
            }));
        Row("ffmpeg", "proxy, náhledy", resolvedTools.Ffmpeg, p => Settings.Tools.Ffmpeg = p);
        Row("ffprobe", "metadata klipů", resolvedTools.Ffprobe, p => Settings.Tools.Ffprobe = p);
        Row("braw-decode", "Blackmagic RAW", resolvedTools.BrawDecoder, p => Settings.Tools.BrawDecoder = p);
        Row("REDline", "RED R3D", resolvedTools.Redline, p => Settings.Tools.Redline = p);
        Row("art-cmd", "ARRIRAW", resolvedTools.ArtCmd, p => Settings.Tools.ArtCmd = p);
        Raise(nameof(ToolsWarning));
    }
}
```

V `MainViewModel.cs` přidat na konec konstruktoru `InitSetup();`.

`windows/src/Offload.App/SourceItemViewModel.cs`:

```csharp
using Offload.Core;

namespace Offload.App;

public enum SourceKind { Card, Drive, Folder }

/// One row in Karty: a volume or a manual folder; the tick and the card name live in MainViewModel.
public sealed class SourceItemViewModel(MainViewModel owner, string path, string title, string detail, SourceKind kind) : ObservableObject
{
    public string Path => path;
    public string Title => title;
    public string Detail => detail;
    public string Glyph => kind switch { SourceKind.Card => "\uE7F1", SourceKind.Drive => "\uEDA2", _ => "\uE8B7" };
    public string CardNamePlaceholder { get; } = SafeDefaultName(path);

    public bool IsSelected
    {
        get => owner.IsSelected(path);
        set { owner.SetSelected(path, value); Raise(); }
    }

    public string CardName
    {
        get => owner.CardName(path);
        set { owner.SetCardName(path, value); Raise(); }
    }

    public static string SafeDefaultName(string source)
    {
        try
        {
            return Layout.DefaultCardName(source);
        }
        catch (Exception)
        {
            return "";
        }
    }
}
```

`windows/src/Offload.App/DestinationViewModel.cs`:

```csharp
using Offload.Core;

namespace Offload.App;

/// One row in Cíle: number, name, free space and file system; red when the disk is not connected.
public sealed class DestinationViewModel : ObservableObject
{
    public DestinationViewModel(MainViewModel owner, int index, string root)
    {
        Index = index;
        Root = root;
        Name = Path.GetFileName(root.TrimEnd('\\')) is { Length: > 0 } n ? n : root;
        Exists = Directory.Exists(root);
        if (Exists)
        {
            FileSystem = NativeIO.FilesystemType(root);
            FreeText = $"{Format.Bytes(NativeIO.AvailableBytes(root) ?? 0)} volno · {FileSystem}";
            if (FileSystem is "exFAT" or "FAT32" or "FAT") FsWarning = "Doporučeno NTFS — spolehlivější než exFAT/FAT32";
        }
        RemoveCommand = new RelayCommand(() => owner.RemoveDestination(root));
    }

    public int Index { get; }
    public string Root { get; }
    public string Name { get; }
    public bool Exists { get; }
    public string FileSystem { get; } = "";
    public string FreeText { get; } = "";
    public string? FsWarning { get; }
    public RelayCommand RemoveCommand { get; }
}
```

`windows/src/Offload.App/ProxyOptionsViewModel.cs`:

```csharp
using System.Globalization;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

public sealed record Option<T>(T Value, string Label);

/// The Proxy section. Edits the live settings; every started job gets its own copy (SettingsStore.Clone).
public sealed class ProxyOptionsViewModel : ObservableObject
{
    readonly ProxySettings p;
    readonly MainViewModel owner;

    public ProxyOptionsViewModel(ProxySettings p, MainViewModel owner)
    {
        this.p = p;
        this.owner = owner;
        PickLutCommand = new RelayCommand(PickLut);
        ClearLutCommand = new RelayCommand(() => { p.LutPath = null; Changed(nameof(LutName), nameof(HasLut)); });
        BitrateDownCommand = new RelayCommand(() => BitrateMbps--, () => p.BitrateMbps > 2);
        BitrateUpCommand = new RelayCommand(() => BitrateMbps++, () => p.BitrateMbps < 50);
        ScaleDownCommand = new RelayCommand(() => ScalePercent--, () => ScalePercent > 50);
        ScaleUpCommand = new RelayCommand(() => ScalePercent++, () => ScalePercent < 100);
    }

    public static IReadOnlyList<Option<ProxyCodec>> Codecs { get; } =
        Enum.GetValues<ProxyCodec>().Select(c => new Option<ProxyCodec>(c, c.Label())).ToList();

    public static IReadOnlyList<Option<int>> Heights { get; } =
        SettingsStore.ProxyHeights.Select(h => new Option<int>(h, $"{h}p")).ToList();

    public static IReadOnlyList<Option<double?>> Aspects { get; } =
    [
        new(null, "celý obraz"),
        .. new[] { 1.33, 1.66, 1.78, 1.85, 2.0, 2.39 }.Select(a => new Option<double?>(a, a.ToString("0.00", CultureInfo.InvariantCulture))),
    ];

    public RelayCommand PickLutCommand { get; }
    public RelayCommand ClearLutCommand { get; }
    public RelayCommand BitrateDownCommand { get; }
    public RelayCommand BitrateUpCommand { get; }
    public RelayCommand ScaleDownCommand { get; }
    public RelayCommand ScaleUpCommand { get; }

    void Changed(params string[] names)
    {
        RaiseMany(names);
        owner.ProxyChanged();
    }

    public bool Enabled { get => p.Enabled; set { p.Enabled = value; Changed(nameof(Enabled)); } }

    public Option<ProxyCodec> Codec
    {
        get => Codecs.First(c => c.Value == p.Codec);
        set { if (value != null) { p.Codec = value.Value; Changed(nameof(Codec), nameof(ShowBitrate)); } }
    }

    public Option<int> Height
    {
        get => Heights.FirstOrDefault(h => h.Value == p.Height) ?? Heights[2];
        set { if (value != null) { p.Height = value.Value; Changed(nameof(Height)); } }
    }

    public bool ShowBitrate => p.Codec is ProxyCodec.H264 or ProxyCodec.Hevc;

    public int BitrateMbps
    {
        get => p.BitrateMbps;
        set { p.BitrateMbps = Math.Clamp(value, 2, 50); Changed(nameof(BitrateMbps), nameof(BitrateText)); }
    }

    public string BitrateText => $"{p.BitrateMbps} Mb/s";

    public Option<double?> Aspect
    {
        get => Aspects.FirstOrDefault(a => a.Value == p.FrameAspect) ?? Aspects[0];
        set { if (value != null) { p.FrameAspect = value.Value; Changed(nameof(Aspect), nameof(ShowFrameScale)); } }
    }

    public bool ShowFrameScale => p.FrameAspect != null;

    public int ScalePercent
    {
        get => (int)Math.Round(p.FrameScale * 100);
        set { p.FrameScale = Math.Clamp(value, 50, 100) / 100.0; Changed(nameof(ScalePercent), nameof(ScaleText)); }
    }

    public string ScaleText => $"{ScalePercent} % obrazu";

    public string LutName => p.LutPath is string l ? Path.GetFileName(l) : "bez LUT";
    public bool HasLut => p.LutPath != null;

    public bool BurnFilename { get => p.BurnFilename; set { p.BurnFilename = value; Changed(nameof(BurnFilename)); } }
    public bool BurnTimecode { get => p.BurnTimecode; set { p.BurnTimecode = value; Changed(nameof(BurnTimecode)); } }
    public bool Audio { get => p.Audio; set { p.Audio = value; Changed(nameof(Audio)); } }
    public bool SkipCameraProxies { get => p.SkipCameraProxies; set { p.SkipCameraProxies = value; Changed(nameof(SkipCameraProxies)); } }
    public bool CopyToAllDestinations { get => p.CopyToAllDestinations; set { p.CopyToAllDestinations = value; Changed(nameof(CopyToAllDestinations)); } }

    void PickLut()
    {
        if (Dialogs.PickFile("Vyber LUT (.cube)", "LUT (*.cube)|*.cube") is not string path) return;
        p.LutPath = path;
        Changed(nameof(LutName), nameof(HasLut));
    }
}
```

`windows/src/Offload.App/ToolRowViewModel.cs`:

```csharp
namespace Offload.App;

public sealed class ToolRowViewModel(string name, string purpose, string? path, Action pick) : ObservableObject
{
    public string Name => name;
    public string Purpose => purpose;
    public bool Found => path != null;
    public string PathText => path ?? "nenalezeno";
    public string Glyph => Found ? "\uE930" : "\uE711";
    public RelayCommand PickCommand { get; } = new(pick);
}
```

`windows/src/Offload.App/VolumeWatcher.cs`:

```csharp
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Offload.App;

/// Rescans volumes when Windows reports a disk or card arriving or leaving (WM_DEVICECHANGE), debounced 500 ms.
sealed class VolumeWatcher : IDisposable
{
    const int WmDeviceChange = 0x0219;
    const long DbtDeviceArrival = 0x8000, DbtDeviceRemoveComplete = 0x8004;

    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    HwndSource? source;

    public event Action? Changed;

    public VolumeWatcher()
    {
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            Changed?.Invoke();
        };
    }

    public void Attach(Window window)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(Hook);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDeviceChange && wParam.ToInt64() is DbtDeviceArrival or DbtDeviceRemoveComplete)
        {
            debounce.Stop();
            debounce.Start();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        source?.RemoveHook(Hook);
        debounce.Stop();
    }
}
```

V `MainWindow.xaml.cs` přidat pole `readonly VolumeWatcher watcher = new();` a:

```csharp
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        watcher.Attach(this);
        watcher.Changed += vm.RefreshVolumes;
        Closed += (_, _) => watcher.Dispose();
    }
```

- [ ] **Step 2: XAML levého panelu**

`windows/src/Offload.App/SetupPanel.xaml` (celý soubor):

```xml
<UserControl x:Class="Offload.App.SetupPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <UserControl.Resources>
        <Style x:Key="SelectableTile" TargetType="Border" BasedOn="{StaticResource Tile}">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsSelected}" Value="True">
                    <Setter Property="BorderBrush" Value="{DynamicResource AccentFillColorDefaultBrush}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="SmallButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Padding" Value="8,3" />
            <Setter Property="MinWidth" Value="0" />
        </Style>
        <Style TargetType="CheckBox" BasedOn="{StaticResource {x:Type CheckBox}}">
            <Setter Property="Margin" Value="0,4,0,0" />
        </Style>
    </UserControl.Resources>
    <DockPanel x:Name="Root" Background="{DynamicResource LayerFillColorDefaultBrush}">
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <StackPanel x:Name="Sections" Margin="18">

                <!-- Karty -->
                <DockPanel>
                    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                        <Button Style="{StaticResource SmallButton}" Command="{Binding RefreshVolumesCommand}" ToolTip="Znovu načíst připojené disky"
                                AutomationProperties.AutomationId="RefreshVolumesButton">
                            <TextBlock Style="{StaticResource Icon}" FontSize="13" Text="&#xE72C;" />
                        </Button>
                        <Button Style="{StaticResource SmallButton}" Margin="4,0,0,0" Content="Složka…" Command="{Binding AddSourceCommand}"
                                AutomationProperties.AutomationId="AddSourceButton" />
                    </StackPanel>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Style="{StaticResource Icon}" Text="&#xE7F1;" Margin="0,0,8,0" />
                        <TextBlock Style="{StaticResource SectionTitle}" Text="Karty" />
                    </StackPanel>
                </DockPanel>
                <Border Style="{StaticResource Tile}" Margin="0,8,0,0" Visibility="{Binding HasSources, Converter={StaticResource BoolToCollapsed}}">
                    <TextBlock Text="Vložte kartu do čtečky — objeví se tady automaticky." TextWrapping="Wrap" Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                </Border>
                <ItemsControl ItemsSource="{Binding Sources}" Margin="0,8,0,0">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Style="{StaticResource SelectableTile}" Margin="0,0,0,6">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="Auto" />
                                        <ColumnDefinition Width="Auto" />
                                        <ColumnDefinition />
                                    </Grid.ColumnDefinitions>
                                    <CheckBox IsChecked="{Binding IsSelected}" VerticalAlignment="Top" MinWidth="0" Margin="0"
                                              AutomationProperties.Name="{Binding Title}" />
                                    <TextBlock Grid.Column="1" Style="{StaticResource Icon}" FontSize="20" Width="26" Margin="4,0,8,0" VerticalAlignment="Top"
                                               Text="{Binding Glyph}" />
                                    <StackPanel Grid.Column="2">
                                        <TextBlock Text="{Binding Title}" FontWeight="SemiBold" />
                                        <TextBlock Text="{Binding Detail}" Style="{StaticResource Caption}" Margin="0,2,0,0" />
                                        <DockPanel Margin="0,6,0,0" Visibility="{Binding IsSelected, Converter={StaticResource BoolToVisible}}">
                                            <TextBlock Text="Název složky" Style="{StaticResource Caption}" VerticalAlignment="Center" Margin="0,0,8,0" />
                                            <Grid>
                                                <TextBox Text="{Binding CardName, UpdateSourceTrigger=PropertyChanged}" FontSize="12" />
                                                <TextBlock Text="{Binding CardNamePlaceholder}" Style="{StaticResource Caption}" Margin="12,0,0,0"
                                                           VerticalAlignment="Center" IsHitTestVisible="False"
                                                           Visibility="{Binding CardName, Converter={StaticResource EmptyToVisible}}" />
                                            </Grid>
                                        </DockPanel>
                                    </StackPanel>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <!-- Cíle -->
                <DockPanel Margin="0,18,0,0">
                    <Button DockPanel.Dock="Right" Style="{StaticResource SmallButton}" Content="Přidat…" Command="{Binding AddDestinationCommand}"
                            AutomationProperties.AutomationId="AddDestinationButton" />
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Style="{StaticResource Icon}" Text="&#xEDA2;" Margin="0,0,8,0" />
                        <TextBlock Style="{StaticResource SectionTitle}" Text="Cíle" />
                    </StackPanel>
                </DockPanel>
                <TextBlock Text="Přidejte dva disky — každá karta se zapíše na oba zároveň." Margin="0,8,0,0" TextWrapping="Wrap"
                           Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                           Visibility="{Binding HasNoDestinations, Converter={StaticResource BoolToVisible}}" />
                <ItemsControl ItemsSource="{Binding Destinations}" Margin="0,8,0,0">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Style="{StaticResource Tile}" Margin="0,0,0,6">
                                <DockPanel>
                                    <Button DockPanel.Dock="Right" Style="{StaticResource SmallButton}" Command="{Binding RemoveCommand}" ToolTip="Odebrat cíl"
                                            VerticalAlignment="Center">
                                        <TextBlock Style="{StaticResource Icon}" FontSize="13" Text="&#xE738;" />
                                    </Button>
                                    <Border Width="22" Height="22" CornerRadius="11" Margin="0,0,10,0" VerticalAlignment="Top">
                                        <Border.Style>
                                            <Style TargetType="Border">
                                                <Setter Property="Background" Value="{DynamicResource AccentFillColorDefaultBrush}" />
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding Exists}" Value="False">
                                                        <Setter Property="Background" Value="{DynamicResource SystemFillColorCriticalBrush}" />
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </Border.Style>
                                        <TextBlock Text="{Binding Index}" FontSize="11" FontWeight="Bold" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                    </Border>
                                    <StackPanel>
                                        <TextBlock Text="{Binding Name}" FontWeight="SemiBold" ToolTip="{Binding Root}" />
                                        <TextBlock Text="{Binding FreeText}" Style="{StaticResource Caption}" Visibility="{Binding Exists, Converter={StaticResource BoolToVisible}}" />
                                        <TextBlock Text="Disk není připojen" Style="{StaticResource ErrorText}" Visibility="{Binding Exists, Converter={StaticResource BoolToCollapsed}}" />
                                        <TextBlock Text="{Binding FsWarning}" Style="{StaticResource WarningText}" Visibility="{Binding FsWarning, Converter={StaticResource NullToCollapsed}}" />
                                    </StackPanel>
                                </DockPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <TextBlock Text="Jen jeden cíl — doporučeny jsou dvě nezávislé kopie." Style="{StaticResource WarningText}"
                           Visibility="{Binding SingleDestination, Converter={StaticResource BoolToVisible}}" />

                <!-- Struktura a kontrola -->
                <StackPanel Orientation="Horizontal" Margin="0,18,0,0">
                    <TextBlock Style="{StaticResource Icon}" Text="&#xE8B7;" Margin="0,0,8,0" />
                    <TextBlock Style="{StaticResource SectionTitle}" Text="Struktura a kontrola" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,6,0,0">
                    <CheckBox Content="Vlastní datum" IsChecked="{Binding UseCustomDate}" VerticalAlignment="Center" />
                    <DatePicker SelectedDate="{Binding CustomDate}" Margin="10,4,0,0" Width="150"
                                Visibility="{Binding UseCustomDate, Converter={StaticResource BoolToVisible}}" />
                </StackPanel>
                <TextBlock Text="{Binding PathPreview}" FontFamily="Consolas" Style="{StaticResource Caption}" Margin="0,6,0,0" />
                <CheckBox Content="Ověřit kopie zpětným čtením (xxHash64)" IsChecked="{Binding Verify}" />
                <TextBlock Text="Bez ověření není jisté, že data na disku jsou v pořádku." Style="{StaticResource WarningText}"
                           Visibility="{Binding Verify, Converter={StaticResource BoolToCollapsed}}" />
                <CheckBox Content="Analýza klipů a náhledy v reportu" IsChecked="{Binding AnalyzeClips}" />

                <!-- Proxy -->
                <StackPanel Orientation="Horizontal" Margin="0,18,0,0">
                    <TextBlock Style="{StaticResource Icon}" Text="&#xE714;" Margin="0,0,8,0" />
                    <TextBlock Style="{StaticResource SectionTitle}" Text="Proxy" />
                </StackPanel>
                <CheckBox Content="Renderovat proxy po ověření" IsChecked="{Binding Proxy.Enabled}" />
                <StackPanel DataContext="{Binding Proxy}" Visibility="{Binding Enabled, Converter={StaticResource BoolToVisible}}" Margin="0,6,0,0">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="90" />
                            <ColumnDefinition />
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                        </Grid.RowDefinitions>
                        <TextBlock Text="Kodek" Style="{StaticResource Caption}" VerticalAlignment="Center" />
                        <ComboBox Grid.Column="1" ItemsSource="{Binding Codecs}" SelectedItem="{Binding Codec}" DisplayMemberPath="Label" Margin="0,2" />
                        <TextBlock Grid.Row="1" Text="Výška" Style="{StaticResource Caption}" VerticalAlignment="Center" />
                        <ComboBox Grid.Row="1" Grid.Column="1" ItemsSource="{Binding Heights}" SelectedItem="{Binding Height}" DisplayMemberPath="Label" Margin="0,2" />
                        <TextBlock Grid.Row="2" Text="Datový tok" Style="{StaticResource Caption}" VerticalAlignment="Center"
                                   Visibility="{Binding ShowBitrate, Converter={StaticResource BoolToVisible}}" />
                        <StackPanel Grid.Row="2" Grid.Column="1" Orientation="Horizontal" Margin="0,2"
                                    Visibility="{Binding ShowBitrate, Converter={StaticResource BoolToVisible}}">
                            <Button Style="{StaticResource SmallButton}" Content="−" Command="{Binding BitrateDownCommand}" />
                            <TextBlock Text="{Binding BitrateText}" Width="70" TextAlignment="Center" VerticalAlignment="Center" />
                            <Button Style="{StaticResource SmallButton}" Content="+" Command="{Binding BitrateUpCommand}" />
                        </StackPanel>
                        <TextBlock Grid.Row="3" Text="Výřez" Style="{StaticResource Caption}" VerticalAlignment="Center" />
                        <StackPanel Grid.Row="3" Grid.Column="1" Orientation="Horizontal" Margin="0,2">
                            <ComboBox ItemsSource="{Binding Aspects}" SelectedItem="{Binding Aspect}" DisplayMemberPath="Label" Width="130" />
                            <StackPanel Orientation="Horizontal" Margin="8,0,0,0" Visibility="{Binding ShowFrameScale, Converter={StaticResource BoolToVisible}}"
                                        ToolTip="Velikost frame lines vůči obrazu – 90 % = 10 % overscan">
                                <Button Style="{StaticResource SmallButton}" Content="−" Command="{Binding ScaleDownCommand}" />
                                <TextBlock Text="{Binding ScaleText}" Width="90" TextAlignment="Center" VerticalAlignment="Center" />
                                <Button Style="{StaticResource SmallButton}" Content="+" Command="{Binding ScaleUpCommand}" />
                            </StackPanel>
                        </StackPanel>
                        <TextBlock Grid.Row="4" Text="LUT" Style="{StaticResource Caption}" VerticalAlignment="Center" />
                        <DockPanel Grid.Row="4" Grid.Column="1" Margin="0,2">
                            <Button DockPanel.Dock="Right" Style="{StaticResource SmallButton}" Margin="4,0,0,0" Command="{Binding ClearLutCommand}"
                                    Visibility="{Binding HasLut, Converter={StaticResource BoolToVisible}}" ToolTip="Bez LUT">
                                <TextBlock Style="{StaticResource Icon}" FontSize="12" Text="&#xE711;" />
                            </Button>
                            <Button DockPanel.Dock="Right" Style="{StaticResource SmallButton}" Content="Vybrat…" Command="{Binding PickLutCommand}" />
                            <TextBlock Text="{Binding LutName}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                        </DockPanel>
                    </Grid>
                    <CheckBox Content="Burn-in názvu klipu" IsChecked="{Binding BurnFilename}" />
                    <CheckBox Content="Burn-in timecodu" IsChecked="{Binding BurnTimecode}" />
                    <CheckBox Content="Zvuk" IsChecked="{Binding Audio}" />
                    <CheckBox Content="Přeskočit proxy nahrané kamerou (SUB/PROXY)" IsChecked="{Binding SkipCameraProxies}" />
                    <CheckBox Content="Kopírovat proxy na všechny cíle" IsChecked="{Binding CopyToAllDestinations}" />
                </StackPanel>
                <TextBlock Text="{Binding LutProblem}" Style="{StaticResource ErrorText}" Margin="0,6,0,0"
                           Visibility="{Binding LutProblem, Converter={StaticResource NullToCollapsed}}" />
                <TextBlock Text="{Binding ToolsWarning}" Style="{StaticResource WarningText}" Margin="0,6,0,0"
                           Visibility="{Binding ToolsWarning, Converter={StaticResource NullToCollapsed}}" />

                <!-- Dekodéry -->
                <Expander Margin="0,18,0,0">
                    <Expander.Header>
                        <StackPanel Orientation="Horizontal">
                            <TextBlock Style="{StaticResource Icon}" Text="&#xE90F;" Margin="0,0,8,0" />
                            <TextBlock Style="{StaticResource SectionTitle}" Text="Dekodéry" />
                        </StackPanel>
                    </Expander.Header>
                    <StackPanel Margin="0,6,0,0">
                        <ItemsControl ItemsSource="{Binding Tools}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <DockPanel Margin="0,0,0,6">
                                        <Button DockPanel.Dock="Right" Style="{StaticResource SmallButton}" Content="…" Command="{Binding PickCommand}"
                                                ToolTip="Vybrat cestu k nástroji" VerticalAlignment="Center" />
                                        <TextBlock Style="{StaticResource Icon}" Text="{Binding Glyph}" Margin="0,0,8,0">
                                            <TextBlock.Style>
                                                <Style TargetType="TextBlock" BasedOn="{StaticResource Icon}">
                                                    <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding Found}" Value="True">
                                                            <Setter Property="Foreground" Value="{DynamicResource SystemFillColorSuccessBrush}" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </TextBlock.Style>
                                        </TextBlock>
                                        <StackPanel>
                                            <TextBlock>
                                                <Run Text="{Binding Name, Mode=OneWay}" FontWeight="SemiBold" />
                                                <Run Text="{Binding Purpose, Mode=OneWay}" FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                                            </TextBlock>
                                            <TextBlock Text="{Binding PathText}" Style="{StaticResource Caption}" FontSize="11" />
                                        </StackPanel>
                                    </DockPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                        <TextBlock Text="Canon Cinema RAW Light a Sony X-OCN se zkopírují a ověří, proxy pro ně nejde vyrobit." Style="{StaticResource Caption}"
                                   TextWrapping="Wrap" TextTrimming="None" Margin="0,4,0,0" />
                    </StackPanel>
                </Expander>

            </StackPanel>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

- [ ] **Step 3: Sestavit, vyzkoušet, vyfotit**

```powershell
dotnet build
. .\scripts\app-drive.ps1
$t = "$env:TEMP\offload-gui-task7"; New-Item -ItemType Directory -Force "$t\settings", "$t\card\DCIM", "$t\dst" | Out-Null
'{"destRoots":["' + ("$t\dst" -replace '\\','\\') + '","Q:\\nedostupny"],"proxy":{"enabled":true,"lutPath":"' + ("$t\missing.cube" -replace '\\','\\') + '"}}' |
    Set-Content -Encoding utf8 "$t\settings\settings.json"
$env:OFFLOAD_SETTINGS_DIR = "$t\settings"
$p = Start-Offload -Exe .\src\Offload.App\bin\Debug\net10.0-windows\Offload.exe -Arguments "--no-hw-prompt `"$t\card`""
Save-OffloadScreenshot $p "$t\setup.png"
Stop-Offload $p
```

Expected (prohlédnout `setup.png`): Karty — ruční složka `card` zaškrtnutá s polem „Název složky“ (zástupný text `card`) a připojené vyměnitelné/USB disky (na tomto PC `Elements … (E:)` s „nevypadá jako karta“, nezaškrtnutý); Cíle — 1 s volným místem a „NTFS“, 2 červeně „Disk není připojen“; Struktura s náhledem cesty `…\2026-…\<název karty>\`; Proxy rozbalené s kodekem, výškou, datovým tokem, výřezem, LUT `missing.cube` a červenou hláškou „LUT nenalezen: missing.cube — …“; Dekodéry sbalené. Rozbalit dekodéry ručně není nutné. Barvy čitelné ve světlém i tmavém režimu (pokud je systém tmavý, ověřit tmavý). Po zavření aplikace `settings.json` stále obsahuje oba cíle. Smazat `$t`.

- [ ] **Step 4: Commit**

Řádek do VYVOJ: `- Úkol 7 — levý panel: karty (automaticky při vložení, ruční složka, název složky), cíle (volné místo, souborový systém, nepřipojený disk), datum a ověření, proxy, dekodéry`.

```powershell
git add windows docs
git commit -m "Windows: GUI setup panel (cards, destinations, structure, proxy, decoders)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Spuštění a fronta úloh

**Files:**
- Create: `windows/src/Offload.App/MainViewModel.Queue.cs`, `JobItemViewModel.cs`, `Shell.cs`, `NativeMethods.cs`
- Modify: `windows/src/Offload.App/QueuePanel.xaml` (celý), `SetupPanel.xaml` (spouštěcí lišta), `MainWindow.xaml` (Ctrl+Enter), `MainWindow.xaml.cs` (zavírání, blikání), `MainViewModel.cs` (volání `InitQueue()`)

**Interfaces:**
- Consumes: Task 4 (`JobProgress`, `Texts`, `CardTone`), Task 5 (`JobQueue`), Task 3 (`CardEject`), Task 7 (`selected`, `cardNames`, `manualSources`, `volumes`, `DateString`, `LutProblem`, `Destinations`, `SetupChanged`, `RefreshVolumes`, `RebuildSources`), `ChainView`, `BrakeMeter`, `DriveLinks`, `UsbScanner`.
- Produces: `MainViewModel.StartCommand`, `ClearFinishedCommand`, `Jobs`, `CardsText`, `DestinationsText`, `CanStart`, `TryClose(Func<bool>)`, události `AttentionRequested`, `CloseReady`; `JobItemViewModel`. AutomationId: `StartButton`, `ClearFinishedButton`; na kartě úlohy `EjectButton`, `ReportButton`, `ExplorerButton`, `CancelButton`, text stavu `JobStatusText`.

- [ ] **Step 1: ViewModely a pomocníci**

`windows/src/Offload.App/MainViewModel.Queue.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

public sealed partial class MainViewModel
{
    readonly JobQueue queue = new(m => AppLog.Write(m));
    readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(330) };
    bool closing;

    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];
    public RelayCommand StartCommand { get; private set; } = null!;
    public RelayCommand ClearFinishedCommand { get; private set; } = null!;

    /// A job finished: flash the taskbar button (true = with an error).
    public event Action<bool>? AttentionRequested;
    /// Closing was requested while jobs ran; they are cancelled and finished now — close the window.
    public event Action? CloseReady;

    void InitQueue()
    {
        StartCommand = new RelayCommand(Start, () => CanStart);
        ClearFinishedCommand = new RelayCommand(ClearFinished);
        queue.CopyFinished += j => Dispatch(() => OnCopyFinished(j));
        queue.JobFinished += j => Dispatch(() => OnJobFinished(j));
        ticker.Tick += (_, _) => Tick();
        ticker.Start();
        SetupChanged += () => RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart));
    }

    static void Dispatch(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public string CardsText => Texts.Cards(selected.Count);
    public string DestinationsText => Texts.Destinations(Settings.DestRoots.Count) + (Settings.Proxy.Enabled ? " · proxy" : "");
    public bool CanStart => selected.Count > 0 && Settings.DestRoots.Count > 0 && Destinations.All(d => d.Exists) && LutProblem == null;
    public bool HasJobs => Jobs.Count > 0;
    public bool HasFinished => Jobs.Any(j => j.IsFinished);

    void Start()
    {
        if (!CanStart) return;
        var sources = volumes.Select(v => v.Path).Concat(manualSources).Where(selected.Contains).ToList();
        foreach (var src in sources)
        {
            var name = cardNames.GetValueOrDefault(src) is { Length: > 0 } n && n.Trim().Length > 0 ? n.Trim() : null;
            var config = new JobConfig(src, Settings.DestRoots.ToList())
            {
                CardName = name,
                Date = DateString,
                Verify = Settings.Verify,
                AnalyzeClips = Settings.AnalyzeClips,
                Proxy = SettingsStore.Clone(Settings.Proxy),
                Tools = SettingsStore.Clone(Settings.Tools),
            };
            bool isVolume = volumes.Any(v => string.Equals(v.Path, src, StringComparison.OrdinalIgnoreCase));
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? SourceItemViewModel.SafeDefaultName(src),
                canEject: isVolume && CardEject.CanEject(src));
            Jobs.Insert(0, item);
            selected.Remove(src);
            queue.Enqueue(item.Runner);
        }
        RebuildSources();
        RaiseMany(nameof(HasJobs), nameof(HasFinished));
    }

    void Tick()
    {
        foreach (var j in Jobs)
            if (j.Phase != JobPhase.Done) j.Refresh();
        CommandManager.InvalidateRequerySuggested();
    }

    JobItemViewModel? Find(IQueuedJob job) => Jobs.FirstOrDefault(j => ReferenceEquals(j.Runner, job));

    void OnCopyFinished(IQueuedJob job)
    {
        if (Find(job) is not { } item) return;
        item.Refresh();
        if (item.SafeToEject) SystemSounds.Asterisk.Play();
    }

    void OnJobFinished(IQueuedJob job)
    {
        if (Find(job) is { } item)
        {
            item.Refresh();
            bool error = item.Phase == JobPhase.Failed || item.CopySucceeded == false;
            if (error) SystemSounds.Hand.Play();
            AttentionRequested?.Invoke(error);
        }
        Raise(nameof(HasFinished));
        if (closing && !Jobs.Any(j => !j.IsFinished)) CloseReady?.Invoke();
    }

    void ClearFinished()
    {
        foreach (var j in Jobs.Where(j => j.IsFinished).ToList()) Jobs.Remove(j);
        RaiseMany(nameof(HasJobs), nameof(HasFinished));
    }

    /// The window asks to close. True = close now. With unfinished jobs: confirm, cancel them all and close once
    /// they have cleaned up (CloseReady).
    public bool TryClose(Func<bool> confirm)
    {
        if (Jobs.Any(j => !j.IsFinished))
        {
            if (closing || !confirm()) return false;
            closing = true;
            foreach (var j in Jobs) j.Job.Cancel();
            foreach (var j in Jobs) j.Refresh();
            return false;
        }
        ticker.Stop();
        queue.Dispose();
        SaveNow();
        return true;
    }

    internal async void Eject(JobItemViewModel item)
    {
        try
        {
            item.Ejecting = true;
            var result = await Task.Run(() => CardEject.Eject(item.Job.Config.Source));
            item.Ejecting = false;
            if (result.Ok)
            {
                item.Ejected = true;
                RefreshVolumes();
            }
            else
            {
                ShowMessage($"Kartu se nepodařilo vysunout: {result.Error}");
            }
        }
        catch (Exception e)
        {
            item.Ejecting = false;
            ShowMessage($"Kartu se nepodařilo vysunout: {e.Message}");
        }
    }
}
```

V `MainViewModel.cs` přidat na konec konstruktoru (za `InitSetup();`) `InitQueue();`.

`windows/src/Offload.App/JobItemViewModel.cs`:

```csharp
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.App;

public sealed record DestinationLine(string Name, string Sub, string FullPath);

/// One card in the queue: mirrors the job's status every tick (Mac `JobItem` + `JobCard`).
public sealed class JobItemViewModel : ObservableObject
{
    readonly MainViewModel owner;
    readonly string sourceName;
    readonly JobProgress progress = new(DateTime.UtcNow);
    IReadOnlyList<RoleLink>? links;
    IReadOnlyList<UsbConnector>? connectors;
    IReadOnlyDictionary<string, string>? portNames;
    bool chainRequested, ejected, ejecting;

    public JobItemViewModel(MainViewModel owner, OffloadJob job, string sourceName, bool canEject)
    {
        this.owner = owner;
        this.sourceName = sourceName;
        Job = job;
        CanEject = canEject;
        Runner = new JobQueue.Runner(job, m => AppLog.Write($"{sourceName}: {m}"));
        CancelCommand = new RelayCommand(() => { Job.Cancel(); Refresh(); });
        EjectCommand = new RelayCommand(() => owner.Eject(this), () => !Ejecting);
        OpenReportCommand = new RelayCommand(OpenReport);
        RevealCommand = new RelayCommand(Reveal);
    }

    public OffloadJob Job { get; }
    public JobQueue.Runner Runner { get; }
    public bool CanEject { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand EjectCommand { get; }
    public RelayCommand OpenReportCommand { get; }
    public RelayCommand RevealCommand { get; }

    public void Refresh()
    {
        progress.Update(Job.Status(), DateTime.UtcNow);
        if (!chainRequested && Job.Plan is { } plan && progress.Status.Phase == JobPhase.Copying)
        {
            chainRequested = true;
            ResolveChain(plan);
        }
        Raise(string.Empty);   // every binding of the card may have changed
    }

    /// Where the card and each destination are connected — resolved once, off the UI thread.
    void ResolveChain(OffloadPlan plan)
    {
        var names = new Dictionary<string, string>(owner.Settings.PortNames, StringComparer.OrdinalIgnoreCase);
        Task.Run(() =>
        {
            var l = new List<RoleLink> { new("Karta", DriveLinks.Resolve(plan.Source)) };
            l.AddRange(plan.DestRoots.Select((d, i) => new RoleLink($"Cíl {i + 1}", DriveLinks.Resolve(d))));
            return (Links: l, Connectors: UsbScanner.Scan().Connectors);
        }).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully) return;
            links = t.Result.Links;
            connectors = t.Result.Connectors;
            portNames = names;
            progress.AttachBrakeMeter(new BrakeMeter((long)Job.Config.ChunkMB << 20, Job.Config.Buffers,
                links.Select(ChainView.LinkCeiling).ToList()));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public JobPhase Phase => progress.Status.Phase;
    public bool IsRunning => progress.IsRunning;
    public bool IsFinished => Phase.IsFinished();
    public bool SafeToEject => progress.Status.SafeToEject;
    public bool? CopySucceeded => progress.Status.CopySucceeded;
    public CardTone Tone => progress.Tone;
    public string StatusText => progress.StatusText;
    public double Progress => progress.PhaseProgress;
    public string DetailText => progress.DetailText;

    public string Title => Job.Plan?.CardName ?? sourceName;
    public string FilesText => Job.Plan is { } p ? Texts.Files(p.Scan.Files.Count, p.Scan.TotalBytes) : "";

    public IReadOnlyList<DestinationLine> DestinationLines => Job.Plan is { } p
        ? p.DestRoots.Select((root, i) => new DestinationLine(
            Path.GetFileName(root.TrimEnd('\\')) is { Length: > 0 } n ? n : root, $"› {p.Date}/{p.CardName}", p.CardPaths[i])).ToList()
        : [];

    /// The hardware chain with live throughput and the brake (spec 8a.3), while copying or verifying.
    public IReadOnlyList<string> ChainLines
    {
        get
        {
            if (links == null || Phase is not (JobPhase.Copying or JobPhase.Verifying)) return [];
            var rates = new List<double> { Phase == JobPhase.Copying ? progress.ReadRate / 1e6 : 0 };
            rates.AddRange(progress.DestRates.Select(r => r / 1e6));
            var chain = ChainView.Build(links, rates, connectors, portNames);
            return ChainView.Lines(chain, Phase == JobPhase.Copying ? progress.Brake : null);
        }
    }

    public bool HasChain => ChainLines.Count > 0;

    public bool Ejected
    {
        get => ejected;
        set { ejected = value; Raise(string.Empty); }
    }

    public bool Ejecting
    {
        get => ejecting;
        set { ejecting = value; Raise(string.Empty); }
    }

    public bool ShowSafe => SafeToEject && !Ejected;
    public bool ShowEjectButton => ShowSafe && CanEject;
    public string SafeText => Texts.SafeToEject(Job.Plan?.CardPaths.Count ?? 0);
    public string? FailedText => Job.Report is { Success: false } r ? Texts.Failed(r.FailedFiles.Count) : null;
    public string? ErrorText => progress.Status.Error;
    public string? SummaryText => IsFinished && Job.Report is { } r ? JobProgress.SummaryText(r) : null;
    public bool HasReport => Job.Report != null;
    public bool CanCancel => IsRunning || Phase == JobPhase.Waiting;

    void OpenReport()
    {
        if (Job.Plan is not { } plan) return;
        var path = Path.Combine(plan.CardPaths[0], Layout.ReportDirName, $"{plan.CardName}_report.html");
        if (!File.Exists(path))
        {
            owner.ShowMessage($"Report nenalezen: {path}");
            return;
        }
        if (Shell.Open(path) is string error) owner.ShowMessage(error);
    }

    void Reveal()
    {
        if (Job.Plan is { } plan && Shell.Reveal(plan.CardPaths[0]) is string error) owner.ShowMessage(error);
    }
}
```

`windows/src/Offload.App/Shell.cs`:

```csharp
using System.Diagnostics;

namespace Offload.App;

/// Opening files and folders in Windows. Returns an error text, or null.
static class Shell
{
    public static string? Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        }
        catch (Exception e)
        {
            return $"Nejde otevřít {path}: {e.Message}";
        }
    }

    public static string? Reveal(string path)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
            return null;
        }
        catch (Exception e)
        {
            return $"Nejde otevřít Průzkumník: {e.Message}";
        }
    }
}
```

`windows/src/Offload.App/NativeMethods.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Offload.App;

static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    struct FlashInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    const uint FlashAll = 3, FlashUntilForeground = 12;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);

    /// Flashes the taskbar button until the user switches to the window (no-op while it is in front).
    public static void Flash(Window window)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = new WindowInteropHelper(window).Handle,
            Flags = FlashAll | FlashUntilForeground,
        };
        FlashWindowEx(ref info);
    }
}
```

V `MainWindow.xaml.cs` přidat do konstruktoru:

```csharp
        Closing += (_, e) => e.Cancel = !vm.TryClose(() => MessageBox.Show(this,
            "Probíhá offload. Opravdu ukončit?\n\nRozpracované soubory se smažou, hotové zůstanou.", "Offload",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
        vm.CloseReady += Close;
        vm.AttentionRequested += _ => NativeMethods.Flash(this);
```

V `MainWindow.xaml` do `<Window …>` přidat:

```xml
    <Window.InputBindings>
        <KeyBinding Gesture="Ctrl+Enter" Command="{Binding StartCommand}" />
    </Window.InputBindings>
```

- [ ] **Step 2: XAML fronty a spouštěcí lišty**

V `SetupPanel.xaml` vložit jako **první** potomek `DockPanel x:Name="Root"` (před `ScrollViewer`):

```xml
        <Border DockPanel.Dock="Bottom" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource DividerStrokeColorDefaultBrush}" Padding="14">
            <DockPanel>
                <Button DockPanel.Dock="Right" Command="{Binding StartCommand}" Style="{DynamicResource AccentButtonStyle}" MinWidth="150" Padding="16,8"
                        ToolTip="Ctrl+Enter" AutomationProperties.AutomationId="StartButton">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Style="{StaticResource Icon}" FontSize="13" Text="&#xE768;" Margin="0,0,8,0" Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" />
                        <TextBlock Text="Spustit offload" />
                    </StackPanel>
                </Button>
                <StackPanel VerticalAlignment="Center">
                    <TextBlock Text="{Binding CardsText}" FontWeight="SemiBold" />
                    <TextBlock Text="{Binding DestinationsText}" Style="{StaticResource Caption}" />
                </StackPanel>
            </DockPanel>
        </Border>
```

`windows/src/Offload.App/QueuePanel.xaml` (celý soubor):

```xml
<UserControl x:Class="Offload.App.QueuePanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <UserControl.Resources>
        <Style x:Key="ToneText" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource AccentTextFillColorPrimaryBrush}" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Tone}" Value="Ok">
                    <Setter Property="Foreground" Value="{DynamicResource SystemFillColorSuccessBrush}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Tone}" Value="Error">
                    <Setter Property="Foreground" Value="{DynamicResource SystemFillColorCriticalBrush}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Tone}" Value="Warning">
                    <Setter Property="Foreground" Value="{DynamicResource SystemFillColorCautionBrush}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Tone}" Value="Waiting">
                    <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="CardButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Padding" Value="10,3" />
            <Setter Property="MinWidth" Value="0" />
            <Setter Property="Margin" Value="0,0,6,0" />
        </Style>
    </UserControl.Resources>
    <DockPanel>
        <Border DockPanel.Dock="Top" Padding="20,14" BorderThickness="0,0,0,1" BorderBrush="{DynamicResource DividerStrokeColorDefaultBrush}">
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="Vyčistit hotové" Command="{Binding ClearFinishedCommand}" AutomationProperties.AutomationId="ClearFinishedButton"
                        Visibility="{Binding HasFinished, Converter={StaticResource BoolToVisible}}" />
                <TextBlock Text="Fronta" FontSize="20" FontWeight="SemiBold" VerticalAlignment="Center" />
            </DockPanel>
        </Border>
        <Grid>
            <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="{Binding HasJobs, Converter={StaticResource BoolToCollapsed}}">
                <TextBlock Style="{StaticResource Icon}" Text="&#xE7B8;" FontSize="40" HorizontalAlignment="Center" Foreground="{DynamicResource TextFillColorTertiaryBrush}" />
                <TextBlock Text="Zatím žádný offload" FontSize="17" Margin="0,10,0,0" HorizontalAlignment="Center" Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                <TextBlock Text="Vyberte kartu a cíle vlevo a stiskněte Ctrl+Enter." Margin="0,6,0,0" HorizontalAlignment="Center" Foreground="{DynamicResource TextFillColorTertiaryBrush}" />
            </StackPanel>
            <ScrollViewer VerticalScrollBarVisibility="Auto" Visibility="{Binding HasJobs, Converter={StaticResource BoolToVisible}}">
                <ItemsControl ItemsSource="{Binding Jobs}" Margin="20">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border CornerRadius="12" Padding="16" Margin="0,0,0,12" BorderThickness="1"
                                    Background="{DynamicResource CardBackgroundFillColorDefaultBrush}" BorderBrush="{DynamicResource CardStrokeColorDefaultBrush}">
                                <StackPanel>
                                    <DockPanel>
                                        <Border DockPanel.Dock="Right" CornerRadius="10" Padding="8,2" Background="{DynamicResource SubtleFillColorSecondaryBrush}" VerticalAlignment="Top">
                                            <TextBlock Text="{Binding StatusText}" FontSize="12" FontWeight="SemiBold" Style="{StaticResource ToneText}"
                                                       AutomationProperties.AutomationId="JobStatusText" />
                                        </Border>
                                        <TextBlock FontFamily="{StaticResource IconFont}" FontSize="16" Text="&#xE7F1;" Margin="0,0,8,0" VerticalAlignment="Center" Style="{StaticResource ToneText}" />
                                        <TextBlock Text="{Binding Title}" FontSize="15" FontWeight="SemiBold" VerticalAlignment="Center" />
                                        <TextBlock Text="{Binding FilesText}" Style="{StaticResource Caption}" Margin="10,0,0,0" VerticalAlignment="Center" />
                                    </DockPanel>
                                    <ItemsControl ItemsSource="{Binding DestinationLines}" Margin="0,6,0,0">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <TextBlock FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}" ToolTip="{Binding FullPath}">
                                                    <Run Text="&#xEDA2;" FontFamily="{StaticResource IconFont}" FontSize="11" />
                                                    <Run Text="{Binding Name, Mode=OneWay}" FontWeight="SemiBold" />
                                                    <Run Text="{Binding Sub, Mode=OneWay}" FontFamily="Consolas" />
                                                </TextBlock>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                    <StackPanel Margin="0,10,0,0" Visibility="{Binding IsRunning, Converter={StaticResource BoolToVisible}}">
                                        <ProgressBar Value="{Binding Progress, Mode=OneWay}" Maximum="1" Height="4" />
                                        <TextBlock Text="{Binding DetailText}" Style="{StaticResource Caption}" Margin="0,6,0,0" FontSize="13" />
                                    </StackPanel>
                                    <ItemsControl ItemsSource="{Binding ChainLines}" Margin="0,8,0,0" Visibility="{Binding HasChain, Converter={StaticResource BoolToVisible}}">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <TextBlock Text="{Binding}" FontFamily="Consolas" FontSize="11" Foreground="{DynamicResource TextFillColorSecondaryBrush}" TextTrimming="CharacterEllipsis" />
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                    <DockPanel Margin="0,10,0,0" Visibility="{Binding ShowSafe, Converter={StaticResource BoolToVisible}}">
                                        <Button DockPanel.Dock="Right" Content="Vysunout kartu" Command="{Binding EjectCommand}" Style="{StaticResource CardButton}" Margin="8,0,0,0"
                                                AutomationProperties.AutomationId="EjectButton" Visibility="{Binding ShowEjectButton, Converter={StaticResource BoolToVisible}}" />
                                        <TextBlock FontFamily="{StaticResource IconFont}" Text="&#xEA18;" Margin="0,0,8,0" VerticalAlignment="Center" Foreground="{DynamicResource SystemFillColorSuccessBrush}" />
                                        <TextBlock Text="{Binding SafeText}" TextWrapping="Wrap" VerticalAlignment="Center" Foreground="{DynamicResource SystemFillColorSuccessBrush}" />
                                    </DockPanel>
                                    <TextBlock Text="Karta vysunuta" Style="{StaticResource Caption}" Margin="0,8,0,0" Visibility="{Binding Ejected, Converter={StaticResource BoolToVisible}}" />
                                    <TextBlock Text="{Binding FailedText}" Style="{StaticResource ErrorText}" FontSize="13" Margin="0,8,0,0" Visibility="{Binding FailedText, Converter={StaticResource NullToCollapsed}}" />
                                    <TextBlock Text="{Binding ErrorText}" Style="{StaticResource ErrorText}" FontSize="13" Margin="0,8,0,0" Visibility="{Binding ErrorText, Converter={StaticResource NullToCollapsed}}" />
                                    <TextBlock Text="{Binding SummaryText}" Style="{StaticResource Caption}" Margin="0,8,0,0" Visibility="{Binding SummaryText, Converter={StaticResource NullToCollapsed}}" />
                                    <DockPanel Margin="0,12,0,0">
                                        <Button DockPanel.Dock="Right" Content="Zrušit" Command="{Binding CancelCommand}" Style="{StaticResource CardButton}" Margin="0"
                                                AutomationProperties.AutomationId="CancelButton" Visibility="{Binding CanCancel, Converter={StaticResource BoolToVisible}}" />
                                        <StackPanel Orientation="Horizontal" Visibility="{Binding HasReport, Converter={StaticResource BoolToVisible}}">
                                            <Button Content="Report" Command="{Binding OpenReportCommand}" Style="{StaticResource CardButton}" AutomationProperties.AutomationId="ReportButton" />
                                            <Button Content="Průzkumník" Command="{Binding RevealCommand}" Style="{StaticResource CardButton}" AutomationProperties.AutomationId="ExplorerButton" />
                                        </StackPanel>
                                    </DockPanel>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Grid>
    </DockPanel>
</UserControl>
```

- [ ] **Step 3: Skutečný offload z GUI a zavření během kopie (Review focus 1)**

```powershell
dotnet build
dotnet test
. .\scripts\app-drive.ps1
$t = "D:\offload-gui-e2e"
New-Item -ItemType Directory -Force "$t\card\DCIM\100TEST", "$t\settings" | Out-Null
$rng = [Random]::new(1); $buf = New-Object byte[] (64MB)
foreach ($i in 1..16) { $rng.NextBytes($buf); [IO.File]::WriteAllBytes("$t\card\DCIM\100TEST\C$('{0:000}' -f $i).MOV", $buf) }
$d1 = "E:\offload-gui-e2e"; $d2 = "$env:TEMP\offload-gui-e2e-d2"
New-Item -ItemType Directory -Force $d1, $d2 | Out-Null
'{"destRoots":["' + ($d1 -replace '\\','\\') + '","' + ($d2 -replace '\\','\\') + '"],"analyzeClips":false}' | Set-Content -Encoding utf8 "$t\settings\settings.json"
$env:OFFLOAD_SETTINGS_DIR = "$t\settings"
$exe = (Resolve-Path .\src\Offload.App\bin\Debug\net10.0-windows\Offload.exe).Path

# Run 1: a full offload
$p = Start-Offload -Exe $exe -Arguments "--no-hw-prompt `"$t\card`""
Save-OffloadScreenshot $p "$t\1-ready.png"
Invoke-OffloadButton $p 'StartButton'
Start-Sleep 4;  Save-OffloadScreenshot $p "$t\2-copying.png"
Start-Sleep 6;  Save-OffloadScreenshot $p "$t\3-later.png"
do { Start-Sleep 2; $s = (Find-OffloadElement $p 'JobStatusText').Current.Name } while ($s -notin 'Hotovo', 'Chyba', 'Zrušeno')
Save-OffloadScreenshot $p "$t\4-done.png"
"status: $s"
Stop-Offload $p

# Run 2: close while copying → confirm "Ano" → cancels, cleans up, exits
$p = Start-Offload -Exe $exe -Arguments "--no-hw-prompt `"$t\card`""
Invoke-OffloadButton $p 'StartButton'
Start-Sleep 3
$p.CloseMainWindow() | Out-Null
Start-Sleep 1
Save-OffloadScreenshot $p "$t\5-confirm.png" '*' -Dialog
Invoke-OffloadDialogButton $p '6'   # "Ano"
"exited: $($p.WaitForExit(60000))"
Get-ChildItem $d1, $d2 -Recurse -Force -Filter '*.offload-part*' | Select-Object FullName
```

Expected:
- `1-ready.png`: složka `card` zaškrtnutá, dole „1 karta“ / „2 cíle“, tlačítko aktivní.
- `2-copying.png`/`3-later.png`: karta úlohy „card“, „16 souborů · 1.07 GB“, cíle `offload-gui-e2e › 2026-…/card`, průběh, řádek „Karta … MB/s · D1 … · D2 … · zbývá … · C0xx.MOV“, pod ním řetěz připojení (`Karta … — SATA …`, `Cíl 1 … — USB 3 (5 Gb/s), port 6 …`, `Úzké hrdlo: Cíl 1 …`).
- `4-done.png`: stav „Hotovo“ zeleně, zelený pruh „Všechny soubory jsou na 2 discích a ověřené — kartu lze vysunout.“ **bez** tlačítka „Vysunout kartu“ (zdroj je složka), souhrn „kopie … · ověření …“, tlačítka Report a Průzkumník. `status: Hotovo`.
- Běh 2: `5-confirm.png` ukazuje dotaz „Probíhá offload. Opravdu ukončit? …“; po „Ano“ se aplikace sama zavře (`exited: True`); výpis `.offload-part` je prázdný.

Pak smazat `D:\offload-gui-e2e`, `E:\offload-gui-e2e`, `$d2`. Snímky popsat v reportu (co je na nich vidět).

- [ ] **Step 4: Commit**

Řádek do VYVOJ: `- Úkol 8 — spuštění a fronta: karty úloh s průběhem, rychlostmi, odhadem času a řetězem připojení s úzkým hrdlem, zelený pruh a vysunutí karty, report a Průzkumník, zrušení, zavření okna během kopie, zvuk a blikání v hlavním panelu`.

```powershell
git add windows docs
git commit -m "Windows: GUI queue, job cards, start bar, eject, close during copy" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Hardware v GUI — dotaz při startu a okno Hardware

**Files:**
- Create: `windows/src/Offload.App/MainViewModel.Hardware.cs`, `HardwareViewModel.cs`, `HardwareWindow.xaml`, `HardwareWindow.xaml.cs`
- Modify: `windows/src/Offload.Core/Hardware/HwTest.cs` (`Advise` zvlášť, `Load` validace), `SetupPanel.xaml` (sekce Hardware), `MainWindow.xaml.cs` (dotaz při startu, otevření okna), `MainViewModel.cs` (volání `InitHardware()`)
- Test: `windows/tests/Offload.Core.Tests/HwTestTests.cs` (přidat testy)

**Interfaces:**
- Consumes: `HwTest.Run/Save/Load/DefaultPath`, `HwTestResult`, `Advisor`, `PortLabels`, `UsbSpeeds`, `AdviceKind`, Task 7 (`selected`, `volumes`, `manualSources`, `Settings.PortNames`).
- Produces: `HwTest.Advise(IReadOnlyList<RoleLink> drives, IReadOnlyList<UsbConnector> connectors, IReadOnlyList<DriveMeasurement> measurements, IReadOnlyDictionary<string,string>? portNames) -> List<Advice>` (to, co dnes dělá konec `Run`); `HwTest.Load` vrací null, když některý seznam chybí; `MainViewModel.OpenHardwareCommand`, `HardwareSummary`, `OpenHardware()`, `HardwareRoles()`, `HardwareTestFinished(HwTestResult)`, `SetPortName(string id, string name)`, událost `HardwareRequested`. AutomationId: `HardwareButton`; v okně Hardware `MeasureButton`, `StopButton`, `ShowInternalPorts`, `HwCloseButton`, stav `HwStatusText`.

- [ ] **Step 1: Core — padající testy**

Do `windows/tests/Offload.Core.Tests/HwTestTests.cs` přidat:

```csharp
    [Fact]
    public void Advise_SameAsRun_AndUsesPortNames()
    {
        using var dir = new TempDir();
        var r = HwTest.Run([dir.Sub("card")], [dir.Sub("dest")], measure: false);
        Assert.Equal(r.Advice, HwTest.Advise(r.Drives, r.Connectors, r.Measurements, null));
        if (r.Connectors.FirstOrDefault(c => c.UserVisible && c.SupportsUsb3 == true && c.Device == null) is { } free)
        {
            var named = HwTest.Advise(r.Drives, r.Connectors, r.Measurements, new Dictionary<string, string> { [free.Id] = "přední levý" });
            Assert.Contains(named, a => a.Text.Contains("přední levý"));
        }
    }

    [Fact]
    public void Load_MissingLists_IsNull()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "hwtest.json");
        File.WriteAllText(path, "{}");
        Assert.Null(HwTest.Load(path));
        File.WriteAllText(path, """{"Time":"2026-10-05T10:00:00Z","Drives":[],"Connectors":[],"Measurements":[]}""");
        Assert.Null(HwTest.Load(path));
    }
```

Run: `dotnet test --filter "FullyQualifiedName~HwTestTests"` → FAIL (`Advise` neexistuje).

- [ ] **Step 2: Core — implementace**

V `HwTest.cs` přesunout konec `Run` (od výpočtu `onCard` pro varování a `pace` po seřazený seznam) do:

```csharp
    /// The advice for a test result: a destination on a card's volume, then the Advisor's advice with the
    /// measured pace (cards read, destinations written). Run uses it; the app calls it again after a port is renamed.
    public static List<Advice> Advise(IReadOnlyList<RoleLink> drives, IReadOnlyList<UsbConnector> connectors,
        IReadOnlyList<DriveMeasurement> measurements, IReadOnlyDictionary<string, string>? portNames)
    {
        var cards = drives.Where(d => IsCard(d.Role)).ToList();
        var onCard = drives.Where(d => !IsCard(d.Role))
            .Select(d => (d.Role, Card: cards.FirstOrDefault(c => SameVolume(c.Link.Root, d.Link.Root))?.Role))
            .Where(x => x.Card != null);
        var pace = measurements
            .Select(m => (m.Role, Value: IsCard(m.Role) ? m.ReadMBps : m.WriteMBps))
            .Where(x => x.Value is > 0)
            .ToDictionary(x => x.Role, x => x.Value!.Value);
        return onCard
            .Select(x => new Advice(AdviceKind.Warning,
                $"{x.Role} je na stejném svazku jako {x.Card} — na kartu se nezapisuje, zápis se neměří; zvol jiný cílový disk."))
            .Concat(Advisor.Advise(drives, connectors, pace.Count > 0 ? pace : null, portNames))
            .OrderBy(a => a.Kind).ToList();
    }
```

a `Run` zakončit `return new HwTestResult(DateTime.UtcNow, drives, connectors, measurements, Advise(drives, connectors, measurements, portNames));` (`onCard` v `Run` zůstává jen pro přeskočení zápisu). Pozor: `RoleLink.Link.Root` je kořen svazku — `SameVolume` funguje i s ním; ověřit, že výsledky `Run` beze změny (existující testy HwTest). V `Load` po deserializaci:

```csharp
            var r = File.Exists(path) ? JsonSerializer.Deserialize<HwTestResult>(File.ReadAllText(path), Json) : null;
            // A partial or foreign file: treat as no test rather than hand out null lists.
            return r is { Drives: not null, Connectors: not null, Measurements: not null, Advice: not null } ? r : null;
```

Run: `dotnet test --filter "FullyQualifiedName~HwTestTests"` → PASS.

- [ ] **Step 3: ViewModely**

`windows/src/Offload.App/MainViewModel.Hardware.cs`:

```csharp
using System.Globalization;
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.App;

public sealed partial class MainViewModel
{
    HwTestResult? lastHwTest;

    public RelayCommand OpenHardwareCommand { get; private set; } = null!;

    /// Open (or bring to front) the Hardware window for this view model.
    public event Action<HardwareViewModel>? HardwareRequested;

    void InitHardware()
    {
        lastHwTest = HwTest.Load(HwTest.DefaultPath);
        OpenHardwareCommand = new RelayCommand(OpenHardware);
    }

    public string HardwareSummary => lastHwTest is { } r
        ? $"Poslední test {r.Time.ToLocalTime().ToString("d. M. yyyy H:mm", CultureInfo.InvariantCulture)} · " +
          Texts.Warnings(r.Advice.Count(a => a.Kind == AdviceKind.Warning))
        : "Zatím neproběhl";

    public void OpenHardware()
    {
        var hw = new HardwareViewModel(this);
        HardwareRequested?.Invoke(hw);
        hw.RunQuick();
    }

    /// Who takes part in the test: the ticked cards and the destinations; with neither, every ready disk by letter.
    internal (IReadOnlyList<string> Cards, IReadOnlyList<string> Destinations, bool ByDrive) HardwareRoles()
    {
        var cards = volumes.Select(v => v.Path).Concat(manualSources).Where(selected.Contains).ToList();
        var dests = Settings.DestRoots.Where(Directory.Exists).ToList();
        if (cards.Count + dests.Count > 0) return (cards, dests, false);
        var all = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(d => d.RootDirectory.FullName).ToList();
        return ([], all, true);
    }

    internal void HardwareTestFinished(HwTestResult result)
    {
        lastHwTest = result;
        Raise(nameof(HardwareSummary));
        try
        {
            HwTest.Save(result, HwTest.DefaultPath);
        }
        catch (Exception e)
        {
            AppLog.Write($"hwtest save: {e.Message}");
        }
    }

    internal void SetPortName(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) Settings.PortNames.Remove(id);
        else Settings.PortNames[id] = name.Trim();
        SettingsChanged();
    }
}
```

V `MainViewModel.cs` přidat na konec konstruktoru `InitHardware();`.

`windows/src/Offload.App/HardwareViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Offload.Core.Hardware;

namespace Offload.App;

public sealed record HwDriveRow(string Role, string Root, string Description);
public sealed record HwAdviceRow(string Glyph, string Kind, string Text);
public sealed record HwMeasureRow(string Role, string Root, string Read, string Write);

public sealed class HwPortRow(HardwareViewModel owner, UsbConnector connector, string kind, string device, string drives, string name) : ObservableObject
{
    public string Label => PortLabels.WithKind(connector);
    public string Kind => kind;
    public string Device => device;
    public string Drives => drives;
    string customName = name;
    public string CustomName
    {
        get => customName;
        set { customName = value; owner.Rename(connector.Id, value); Raise(); }
    }
}

public sealed record HwPortGroup(string Controller, IReadOnlyList<HwPortRow> Ports);

/// The Hardware window (spec 8a.2): where the cards and disks are connected, advice, optional measurement,
/// the USB ports with user names. Tests run off the UI thread; nothing is ever written to a card.
public sealed class HardwareViewModel : ObservableObject
{
    readonly MainViewModel owner;
    CancellationTokenSource? cts;
    HwTestResult? result;
    bool busy, showInternal, byDrive;
    string status = "";

    public HardwareViewModel(MainViewModel owner)
    {
        this.owner = owner;
        MeasureCommand = new RelayCommand(Measure, () => !Busy && !byDrive && result != null);
        StopCommand = new RelayCommand(() => cts?.Cancel(), () => Busy);
        RerunCommand = new RelayCommand(RunQuick, () => !Busy);
    }

    public ObservableCollection<HwDriveRow> Drives { get; } = [];
    public ObservableCollection<HwAdviceRow> Advice { get; } = [];
    public ObservableCollection<HwMeasureRow> Measurements { get; } = [];
    public ObservableCollection<HwPortGroup> PortGroups { get; } = [];
    public RelayCommand MeasureCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RerunCommand { get; }

    public bool Busy { get => busy; private set => Set(ref busy, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool HasMeasurements => Measurements.Count > 0;
    public string MeasureHint => byDrive ? "Pro měření vyberte kartu nebo přidejte cíl." : "";

    public bool ShowInternal
    {
        get => showInternal;
        set { if (Set(ref showInternal, value)) BuildPorts(); }
    }

    public void RunQuick() => Run(measure: false);

    void Measure()
    {
        var (_, dests, _) = owner.HardwareRoles();
        var list = dests.Count > 0 ? string.Join("\n", dests.Select(d => "   " + d)) : "   (žádné)";
        var text = "Změřit rychlost?\n\nZ karet se jen čte (až 1 GB z každé).\n" +
                   $"Na cílové disky se zapíše a přečte dočasný soubor 1 GB a hned se smaže:\n{list}\n\n" +
                   "Podle rychlosti disků to může trvat několik minut.";
        if (MessageBox.Show(Application.Current.MainWindow, text, "Offload", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Run(measure: true);
    }

    void Run(bool measure)
    {
        var (cards, dests, all) = owner.HardwareRoles();
        byDrive = all;
        var names = new Dictionary<string, string>(owner.Settings.PortNames, StringComparer.OrdinalIgnoreCase);
        cts = new CancellationTokenSource();
        var token = cts.Token;
        Busy = true;
        Status = measure ? "Měřím rychlost… (může trvat několik minut)" : "Zjišťuji připojení…";
        Task.Run(() => HwTest.Run(cards, dests, measure, portNames: names, rolesByDrive: all, cancel: token))
            .ContinueWith(t =>
            {
                Busy = false;
                if (t.IsCompletedSuccessfully)
                {
                    result = t.Result;
                    Status = token.IsCancellationRequested ? "Měření přerušeno — zobrazeno, co se stihlo změřit." : "";
                    Rebuild();
                    if (!token.IsCancellationRequested) owner.HardwareTestFinished(result);
                }
                else
                {
                    Status = "Test se nepodařil: " + t.Exception?.GetBaseException().Message;
                }
                RaiseMany(nameof(MeasureHint), nameof(HasMeasurements));
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void Rebuild()
    {
        if (result is not { } r) return;
        var names = owner.Settings.PortNames;
        Drives.Clear();
        foreach (var d in r.Drives) Drives.Add(new HwDriveRow(d.Role, d.Link.Root, d.Link.Describe(r.Connectors, names)));
        Measurements.Clear();
        foreach (var m in r.Measurements) Measurements.Add(new HwMeasureRow(m.Role, m.Root, Mb(m.ReadMBps), Mb(m.WriteMBps)));
        RebuildAdvice();
        BuildPorts();
    }

    void RebuildAdvice()
    {
        if (result is not { } r) return;
        Advice.Clear();
        foreach (var a in HwTest.Advise(r.Drives, r.Connectors, r.Measurements, owner.Settings.PortNames))
            Advice.Add(a.Kind switch
            {
                AdviceKind.Warning => new HwAdviceRow("\uE7BA", "Warning", a.Text),
                AdviceKind.Info => new HwAdviceRow("\uE946", "Info", a.Text),
                _ => new HwAdviceRow("\uE82F", "Tip", a.Text),
            });
    }

    void BuildPorts()
    {
        PortGroups.Clear();
        if (result is not { } r) return;
        foreach (var g in r.Connectors.Where(c => c.UserVisible || ShowInternal).GroupBy(c => c.ControllerId))
        {
            var rows = g.Select(c =>
            {
                var device = c.Device is { } dev ? $"{dev.Name} — {UsbSpeeds.Label(dev.Link)}" : "volný";
                var drives = string.Join(", ", r.Drives.Where(d => d.Link.Usb is { } u && c.Has(u.ConnectorId))
                    .Select(d => d.Role == d.Link.Root.TrimEnd('\\') ? d.Role : $"{d.Link.Root.TrimEnd('\\')} ({d.Role})"));
                return new HwPortRow(this, c, c.Kind, device, drives, owner.Settings.PortNames.GetValueOrDefault(c.Id, ""));
            }).ToList();
            PortGroups.Add(new HwPortGroup(PortLabels.ShortController(g.First().ControllerName), rows));
        }
    }

    internal void Rename(string id, string name)
    {
        owner.SetPortName(id, name);
        RebuildAdvice();
        if (result is { } r)
        {
            Drives.Clear();
            foreach (var d in r.Drives) Drives.Add(new HwDriveRow(d.Role, d.Link.Root, d.Link.Describe(r.Connectors, owner.Settings.PortNames)));
        }
    }

    static string Mb(double? v) => v is double x ? x.ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "—";
}
```

- [ ] **Step 4: XAML a napojení okna**

`windows/src/Offload.App/HardwareWindow.xaml`:

```xml
<Window x:Class="Offload.App.HardwareWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Hardware — USB porty a disky" Width="880" Height="760" MinWidth="640" MinHeight="480"
        WindowStartupLocation="CenterOwner">
    <DockPanel>
        <Border DockPanel.Dock="Bottom" Padding="16,10" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource DividerStrokeColorDefaultBrush}">
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="Zavřít" IsCancel="True" Click="Close_Click" MinWidth="90" AutomationProperties.AutomationId="HwCloseButton" />
                <Button DockPanel.Dock="Right" Content="Prověřit znovu" Command="{Binding RerunCommand}" Margin="0,0,8,0" />
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <ProgressBar IsIndeterminate="True" Width="80" Height="4" Margin="0,0,10,0" Visibility="{Binding Busy, Converter={StaticResource BoolToVisible}}" />
                    <TextBlock Text="{Binding Status}" Style="{StaticResource Caption}" AutomationProperties.AutomationId="HwStatusText" />
                    <Button Content="Zastavit" Command="{Binding StopCommand}" Margin="10,0,0,0" AutomationProperties.AutomationId="StopButton"
                            Visibility="{Binding Busy, Converter={StaticResource BoolToVisible}}" />
                </StackPanel>
            </DockPanel>
        </Border>
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <StackPanel Margin="20">
                <TextBlock Style="{StaticResource SectionTitle}" Text="Připojení" />
                <ItemsControl ItemsSource="{Binding Drives}" Margin="0,6,0,0">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="0,2">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="80" />
                                    <ColumnDefinition Width="60" />
                                    <ColumnDefinition />
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="{Binding Role}" FontWeight="SemiBold" />
                                <TextBlock Grid.Column="1" Text="{Binding Root}" Style="{StaticResource Caption}" />
                                <TextBlock Grid.Column="2" Text="{Binding Description}" TextWrapping="Wrap" />
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <TextBlock Style="{StaticResource SectionTitle}" Text="Doporučení" Margin="0,18,0,0" />
                <ItemsControl ItemsSource="{Binding Advice}" Margin="0,6,0,0">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <DockPanel Margin="0,3">
                                <TextBlock Text="{Binding Glyph}" FontFamily="{StaticResource IconFont}" FontSize="14" Margin="0,2,10,0" VerticalAlignment="Top">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock">
                                            <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding Kind}" Value="Warning">
                                                    <Setter Property="Foreground" Value="{DynamicResource SystemFillColorCautionBrush}" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding Kind}" Value="Info">
                                                    <Setter Property="Foreground" Value="{DynamicResource AccentTextFillColorPrimaryBrush}" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <TextBlock Text="{Binding Text}" TextWrapping="Wrap" />
                            </DockPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <DockPanel Margin="0,18,0,0">
                    <Button DockPanel.Dock="Right" Content="Změřit rychlost…" Command="{Binding MeasureCommand}" AutomationProperties.AutomationId="MeasureButton" />
                    <StackPanel>
                        <TextBlock Style="{StaticResource SectionTitle}" Text="Měření" />
                        <TextBlock Text="{Binding MeasureHint}" Style="{StaticResource Caption}" />
                    </StackPanel>
                </DockPanel>
                <ItemsControl ItemsSource="{Binding Measurements}" Margin="0,6,0,0" Visibility="{Binding HasMeasurements, Converter={StaticResource BoolToVisible}}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="0,2">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="80" />
                                    <ColumnDefinition Width="60" />
                                    <ColumnDefinition Width="140" />
                                    <ColumnDefinition />
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="{Binding Role}" FontWeight="SemiBold" />
                                <TextBlock Grid.Column="1" Text="{Binding Root}" Style="{StaticResource Caption}" />
                                <TextBlock Grid.Column="2"><Run Text="čtení " Foreground="{DynamicResource TextFillColorSecondaryBrush}" /><Run Text="{Binding Read, Mode=OneWay}" /></TextBlock>
                                <TextBlock Grid.Column="3"><Run Text="zápis " Foreground="{DynamicResource TextFillColorSecondaryBrush}" /><Run Text="{Binding Write, Mode=OneWay}" /></TextBlock>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <DockPanel Margin="0,18,0,0">
                    <CheckBox DockPanel.Dock="Right" Content="Zobrazit interní porty" IsChecked="{Binding ShowInternal}" AutomationProperties.AutomationId="ShowInternalPorts" />
                    <TextBlock Style="{StaticResource SectionTitle}" Text="USB porty" />
                </DockPanel>
                <TextBlock Text="Windows nezná, kde je port na počítači fyzicky — pojmenujte si ho (např. „přední levý“), název se pak použije všude."
                           Style="{StaticResource Caption}" TextWrapping="Wrap" TextTrimming="None" Margin="0,4,0,0" />
                <ItemsControl ItemsSource="{Binding PortGroups}" Margin="0,6,0,0">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Margin="0,6,0,0">
                                <TextBlock Text="{Binding Controller}" FontWeight="SemiBold" Margin="0,0,0,4" />
                                <ItemsControl ItemsSource="{Binding Ports}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Grid Margin="0,2">
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="170" />
                                                    <ColumnDefinition />
                                                    <ColumnDefinition Width="110" />
                                                    <ColumnDefinition Width="170" />
                                                </Grid.ColumnDefinitions>
                                                <TextBlock Text="{Binding Label}" VerticalAlignment="Center" />
                                                <TextBlock Grid.Column="1" Text="{Binding Device}" Style="{StaticResource Caption}" VerticalAlignment="Center" />
                                                <TextBlock Grid.Column="2" Text="{Binding Drives}" FontWeight="SemiBold" VerticalAlignment="Center" />
                                                <TextBox Grid.Column="3" Text="{Binding CustomName, UpdateSourceTrigger=LostFocus}" FontSize="12"
                                                         ToolTip="Vlastní název portu, např. přední levý" />
                                            </Grid>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </ScrollViewer>
    </DockPanel>
</Window>
```

`windows/src/Offload.App/HardwareWindow.xaml.cs`:

```csharp
using System.Windows;

namespace Offload.App;

public partial class HardwareWindow : Window
{
    public HardwareWindow(HardwareViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Closing += (_, _) => vm.StopCommand.Execute(null);   // a running measurement stops after the current disk
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
```

V `SetupPanel.xaml` přidat na konec `StackPanel x:Name="Sections"` (za Expander Dekodéry):

```xml
                <!-- Hardware -->
                <DockPanel Margin="0,18,0,0">
                    <Button DockPanel.Dock="Right" Style="{StaticResource SmallButton}" Content="Prověřit…" Command="{Binding OpenHardwareCommand}"
                            VerticalAlignment="Center" AutomationProperties.AutomationId="HardwareButton" />
                    <StackPanel>
                        <StackPanel Orientation="Horizontal">
                            <TextBlock Style="{StaticResource Icon}" Text="&#xE88E;" Margin="0,0,8,0" />
                            <TextBlock Style="{StaticResource SectionTitle}" Text="Hardware" />
                        </StackPanel>
                        <TextBlock Text="{Binding HardwareSummary}" Style="{StaticResource Caption}" Margin="0,4,0,0" />
                    </StackPanel>
                </DockPanel>
```

V `MainWindow.xaml.cs` přidat pole `HardwareWindow? hardwareWindow;` a do konstruktoru:

```csharp
        vm.HardwareRequested += hw =>
        {
            hardwareWindow?.Close();
            hardwareWindow = new HardwareWindow(hw) { Owner = this };
            hardwareWindow.Closed += (_, _) => hardwareWindow = null;
            hardwareWindow.Show();
        };
        // Spec 8a.2: asked at every start.
        Loaded += (_, _) =>
        {
            if (vm.Options.NoHwPrompt) return;
            var answer = MessageBox.Show(this,
                "Prověřit USB porty?\n\nZjistím, kde a jak rychle jsou připojené karty a disky, a poradím, kam co zapojit. Nic se nezapisuje.",
                "Offload", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) vm.OpenHardware();
        };
```

- [ ] **Step 5: Ověření**

```powershell
dotnet build
dotnet test
. .\scripts\app-drive.ps1
$t = "$env:TEMP\offload-gui-task9"; New-Item -ItemType Directory -Force "$t\settings" | Out-Null
'{"destRoots":["E:\\offload-gui-hw","D:\\offload-gui-hw"]}' | Set-Content -Encoding utf8 "$t\settings\settings.json"
New-Item -ItemType Directory -Force E:\offload-gui-hw, D:\offload-gui-hw | Out-Null
$env:OFFLOAD_SETTINGS_DIR = "$t\settings"
$p = Start-Offload -Exe .\src\Offload.App\bin\Debug\net10.0-windows\Offload.exe
Save-OffloadScreenshot $p "$t\1-prompt.png" '*' -Dialog
Invoke-OffloadDialogButton $p '6'                           # "Ano" in the start-up question
Start-Sleep 3
Save-OffloadScreenshot $p "$t\2-hardware.png" 'Hardware*'
Invoke-OffloadButton $p 'MeasureButton' 'Hardware*'
Invoke-OffloadDialogButton $p '6'                           # confirm the measurement
do { Start-Sleep 3 } while ((Find-OffloadElement $p 'HwStatusText' 'Hardware*').Current.Name -like 'Měřím*')
Save-OffloadScreenshot $p "$t\3-measured.png" 'Hardware*'
Invoke-OffloadButton $p 'HwCloseButton' 'Hardware*'
Save-OffloadScreenshot $p "$t\4-main.png"
Stop-Offload $p
Get-Content "$t\settings\hwtest.json" -TotalCount 5
Get-ChildItem E:\offload-gui-hw, D:\offload-gui-hw -Force
```

Expected: `1-prompt.png` dotaz „Prověřit USB porty? …“; `2-hardware.png` okno Hardware — Připojení `Cíl 1 E:\ WD Elements 25A2 — USB 3 (5 Gb/s), port 6`, `Cíl 2 D:\ … — SATA`, doporučení (volné rychlé porty, porty jen USB 2.0), USB porty seskupené pod řadičem s řádkem `port 6 USB 3 … ← E: (Cíl 1)`; `3-measured.png` Měření se čtením a zápisem obou disků a doporučení „Odhad rychlosti offloadu …“ / „brzdí disk, ne port“; `4-main.png` sekce Hardware vlevo „Poslední test … · …“. `hwtest.json` vznikl v `$t\settings`; ve složkách `offload-gui-hw` nezůstal `.offload-bench-*`. Ručně (bez automatizace) není nutné zkoušet přejmenování portu — stačí jednotkový test `Advise_SameAsRun_AndUsesPortNames`. Smazat `E:\offload-gui-hw`, `D:\offload-gui-hw`, `$t`.

- [ ] **Step 6: Commit**

Řádek do VYVOJ: `- Úkol 9 — Hardware v GUI: dotaz „Prověřit USB porty?“ při každém startu, okno Hardware (připojení, doporučení, měření s potvrzením, USB porty s vlastními názvy), sekce Hardware s posledním testem`.

```powershell
git add windows docs
git commit -m "Windows: GUI hardware check (start-up prompt, Hardware window, port names)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Balíček, dokumentace a ověření sestavené aplikace

**Files:**
- Modify: `windows/scripts/build.ps1` (zip), `windows/README.md`, `docs/ROZDILY.md`, `docs/VYVOJ-WINDOWS.md`, root `README.md` (pokud zmiňuje stav Windows verze)

**Interfaces:**
- Consumes: vše výše.
- Produces: `windows\build\Offload\Offload.exe`, `windows\build\Offload\bin\offload.exe` (+ ffmpeg/ffprobe), `windows\build\Offload-win-x64.zip`; uživatelský návod a checklist ručního testu.

- [ ] **Step 1: Zip**

Na konec `windows/scripts/build.ps1` (před `Write-Host "built $out"`):

```powershell
$zip = Join-Path $root 'build\Offload-win-x64.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $out -DestinationPath $zip
Write-Host "zipped $zip"
```

- [ ] **Step 2: Sestavit a vyzkoušet balíček**

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
Get-ChildItem build\Offload, build\Offload\bin | Select-Object Name, Length
. .\scripts\app-drive.ps1
$t = "$env:TEMP\offload-gui-task10"; New-Item -ItemType Directory -Force "$t\settings" | Out-Null
$env:OFFLOAD_SETTINGS_DIR = "$t\settings"
$p = Start-Offload -Exe (Resolve-Path build\Offload\Offload.exe).Path -Arguments '--no-hw-prompt'
Save-OffloadScreenshot $p "$t\published.png"
Stop-Offload $p
"exit $($p.ExitCode)"
```

Expected: `build\Offload\Offload.exe` (jeden soubor, desítky MB), `build\Offload\bin\offload.exe`, `ffmpeg.exe`, `ffprobe.exe`; zip existuje; publikovaná aplikace se spustí (první start může trvat déle — rozbalení nativních knihoven WPF), snímek ukazuje okno jako v Task 6–9, nástroje v Dekodérech nalezené z `bin\` (ffmpeg, ffprobe ✓). Exit 0. Smazat `$t`.

- [ ] **Step 3: Dokumentace**

- `windows/README.md`:
  - Stav: řádek GUI `hotovo (čeká ruční test s kartou)`.
  - Nová sekce **Aplikace (GUI)**: spuštění `Offload\Offload.exe`; první start — SmartScreen „Windows ochránil váš počítač“ → „Další informace“ → „Přesto spustit“ (aplikace není podepsaná); dotaz na USB porty při každém startu; karty se objeví po vložení, složku lze přidat tlačítkem „Složka…“ nebo přetažením na `Offload.exe`; cíle přes „Přidat…“ (pamatují se); Ctrl+Enter spustí; karta úlohy, zelený pruh a „Vysunout kartu“ (u čtečky se vysune jen karta, čtečka zůstane); Report a Průzkumník; nastavení v `%APPDATA%\Offload\settings.json`, log `app.log`, poslední HW test `hwtest.json`.
  - **Checklist ručního testu** (pro uživatele): 1) vložit kartu do čtečky → objeví se a je zaškrtnutá; 2) přidat dva cíle na různých discích; 3) Spustit → průběh, rychlosti, řetěz připojení a „Úzké hrdlo“; 4) zelený pruh → „Vysunout kartu“ → karta zmizí ze seznamu, čtečka zůstane; vložit další kartu → objeví se; 5) Report otevře HTML report, PDF je vedle; 6) během kopie zavřít okno → dotaz → „Ano“ → aplikace se zavře, v cílech nezůstanou `.offload-part`; 7) okno Hardware: pojmenovat port, změřit rychlost.
- `docs/ROZDILY.md`: řádek GUI → Windows ✅ (WPF); HW řádky → `✅ CLI + GUI`; do „Záměrné rozdíly“: upozornění zvukem a blikáním v hlavním panelu místo notifikace (toast nebalená aplikace neumí bez registrace); vysunutí ze čtečky vysune jen médium; Ctrl+Enter, Průzkumník, doporučení NTFS místo APFS; okno Hardware a vlastní názvy portů jen na Windows; aplikace přijme složku jako argument (přetažení na exe).
- `docs/VYVOJ-WINDOWS.md`: tabulka plánů — 4 `hotovo (čeká ruční test s kartou)`; řádek úkolu 10 nahoru v sekci plánu 4.

- [ ] **Step 4: Commit**

Řádek do VYVOJ: `- Úkol 10 — balíček Offload-win-x64.zip (Offload.exe + bin\offload.exe + ffmpeg), návod k aplikaci a checklist ručního testu`.

```powershell
git add windows docs README.md
git commit -m "Windows: GUI package, docs and manual test checklist" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
