# Offload pro Windows — plán 9: nový vzhled podle docs/ui-navrh (úkoly 10–19) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Druhá část plánu 9 — GUI podle návrhu `docs/ui-navrh/` nad jádrem z úkolů 2–9 (téma, horní lišta, stránka Nastavení, levý sloupec, fronta s novými kartami, Dnes a Historie karet), ukázkový režim pro vizuální kontrolu, verze 0.5.0 s dokumentací a nakonec vizuální kontrola proti `docs/ui-navrh/screeny`.

**Architecture:** Jako v první části: logika v `Offload.Core` s testy, `Offload.App` dostane vlastnosti view-modelů a XAML s tokeny `DynamicResource`. Úkoly 14–16 nemají vlastní test, protože všechno, co počítají, testují úkoly 3–9; kontrolou je build s `-warnaserror`, celá sada a kontrola klíčů zdrojů, vzhled úkol 19.

**Tech Stack:** C# / .NET 10, WPF (Fluent + vlastní tokeny), xUnit, Windows PowerShell 5.1 (`windows\scripts\app-drive.ps1`), Microsoft Edge bez okna (tisk návodu).

**Spec:** `docs/ui-navrh/README.md` (včetně sekce „Rozhodnutí (2026-10-07, závazné)“ z úkolu 1), `docs/ui-navrh/mockup/*.html`, `docs/ui-navrh/screeny/*.png`. **Global Constraints, Review Focus, Rozhodnutí a mapa funkcí jsou v první části** `windows/docs/superpowers/plans/2026-10-07-offload-windows-9-ui.md` a platí pro každý úkol tady; před prvním úkolem této části je přečtěte.

## Global Constraints

Platí celé znění z první části. Nejdůležitější pro úkoly 10–19 zopakované:

- Žádný nový NuGet; texty česky podle mockupů, „Projekt“, nikdy „Preset“; UI vlákno nikdy nečeká na disk; na kartu se nic nezapisuje; `mac/` beze změny.
- Žádná logika v code-behind; nové `.xaml.cs` jen `InitializeComponent`.
- GUI se při úkolech 10–18 **nespouští**; úkol 19 ho spustí až po souhlasu uživatele.
- Každý úkol: `dotnet build windows\Offload.sln -warnaserror` bez varování, celá sada zelená, řádek do `docs/VYVOJ-WINDOWS.md` (sekce `## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)`, nahoru), commit přes `.git/COMMIT_MSG_PLAN9` se soubory výslovně a posledním řádkem `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Bloky „Najít / Nahradit“ jsou podle stavu souboru před úkolem; „(celý soubor nahradit)“ = přepsat celý.

## Review Focus

Pět vstupů s testy je v první části (vlastní je úkoly 2–5 a 10). Pro tuto část: test `ThemeTokensTests` (úkol 10) pokrývá světlé téma a přepnutí za běhu (Review Focus 5); snímky obou témat dělá úkol 19.

---

### Task 10: Téma — tokeny tmavé i světlé sady, styly, ikony, přepínání podle Windows (postup 1)

Barvy z návrhu jsou tokeny (`SolidColorBrush`) ve `Themes/Dark.xaml`; světlá sada `Themes/Light.xaml` má stejné
klíče. Styly a čárové ikony z mockupu jsou v `Themes/Offload.xaml` a `Themes/Icons.xaml`, nové prvky je berou přes
`DynamicResource`. `ThemeWatcher` vybere sadu podle „Režim aplikací“ ve Windows a přepne ji za běhu. Test hlídá Review
Focus 5 (stejné klíče, kontrast ≥ 4,5 : 1, slovníky se načtou). Testovací projekt dostane referenci na WPF z .NET
(žádný NuGet), aby uměl načíst XAML; `Offload.App` dál nereferencuje.

**Files:**
- Create: `windows/src/Offload.App/ThemeWatcher.cs`
- Create: `windows/src/Offload.App/Themes/Dark.xaml`
- Create: `windows/src/Offload.App/Themes/Icons.xaml`
- Create: `windows/src/Offload.App/Themes/Light.xaml`
- Create: `windows/src/Offload.App/Themes/Offload.xaml`
- Create: `windows/src/Offload.Core/Desktop/ThemeChoice.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.App/App.xaml`
- Modify: `windows/src/Offload.App/App.xaml.cs`
- Test: `windows/tests/Offload.Core.Tests/Offload.Core.Tests.csproj`
- Test: `windows/tests/Offload.Core.Tests/ThemeTokensTests.cs` (nový)

**Interfaces:**
- Consumes: `App.xaml` (Fluent `ThemeMode="System"`), registr `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`.
- Produces:
  - `Offload.Core.Desktop.AppTheme { Dark, Light }`, `ThemeChoice` — `const string OverrideVariable = "OFFLOAD_THEME"`, `AppTheme Resolve(string? overrideValue, object? appsUseLightTheme)`, `bool IsOverride(string?)`.
  - `Offload.App.ThemeWatcher.Start(Application app)` (volá `App.OnStartup`).
  - Klíče zdrojů (obě sady): `Bg`, `BgHeader`, `Panel`, `Raised`, `RaisedAlt`, `Input`, `Line`, `LineSoft`, `LineStrong`, `LineInput`, `Track`, `Text`, `Text2`, `Text3`, `TextSoft`, `Accent`, `AccentHover`, `AccentText`, `AccentBar`, `AccentSoft`, `AccentSoft2`, `AccentBorder`, `PillRunText`, `OnAccent`, `Success`, `SuccessFill`, `OnSuccess`, `SuccessBg`, `SuccessBorder`, `SuccessLine`, `SuccessText2`, `SuccessLabel`, `SuccessBtnBg`, `SuccessBtnBorder`, `CheckBg`, `Warn`, `WarnFill`, `OnWarn`, `WarnBg`, `WarnBorder`, `WarnText2`, `WarnListBg`, `WarnListBorder`, `WarnSoftBg`, `WarnSoftBorder`, `WarnSoftText`, `Danger`, `DangerText`, `DangerText2`, `DangerBorder`, `DangerBg`, `DangerCardBorder`, `DiskUsed`, `Chip`, `TabActive`, `DashedLine`, `RunBorder`, `RunBg`, `RowLine`, `FlowLine`, `NavActive`, `SegmentBg`, `SegmentActive`, `ThumbBg`, `HoverBg`, `PreviewBg`, `PreviewText`, `Shade`, `FrameLine` (69 klíčů); styly `StepTitle`, `TextMuted`, `TextFaint`, `TextMono`, `TextMonoFaint`, `TileBorder`, `PillBorder`, `DashedBox`, `PrimaryButton`, `SecondaryButton`, `GhostButton`, `LinkButton`, `DangerButton`, `SuccessButton`, `WarnButton`, `WarnSoftButton`, `Check`, `Switch`, `InputBox`, `ProgressThin`, záložky a segmenty; ikony `Icon*` (Geometry).

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/Offload.Core.Tests.csproj`

Najít (1/1):

```xml
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Offload.Core\Offload.Core.csproj" />
    <ProjectReference Include="..\..\src\Offload.Cli\Offload.Cli.csproj" />
```

Nahradit:

```xml
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <!-- WPF itself (part of .NET, no package): ThemeXamlTests load the theme dictionaries of Offload.App with XamlReader.
         The test project still does not reference Offload.App. -->
    <FrameworkReference Include="Microsoft.WindowsDesktop.App.WPF" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Offload.Core\Offload.Core.csproj" />
    <ProjectReference Include="..\..\src\Offload.Cli\Offload.Cli.csproj" />
```

#### `windows/tests/Offload.Core.Tests/ThemeTokensTests.cs` (nový soubor)

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// The theme of the new look (UI návrh, „Design tokeny“; rozhodnutí: tmavé i světlé podle Windows): both token sets have
/// the same keys, the dark one is the design's, every text colour is readable on its backgrounds, and every theme
/// dictionary of Offload.App loads (XamlReader — the GUI itself is never started).
public class ThemeTokensTests
{
    static string Themes
    {
        get
        {
            var d = AppContext.BaseDirectory;
            while (d != null && !File.Exists(Path.Combine(d, "Directory.Build.props"))) d = Path.GetDirectoryName(d.TrimEnd('\\'));
            return Path.Combine(d ?? throw new InvalidOperationException("Directory.Build.props not found"), "src", "Offload.App", "Themes");
        }
    }

    static Dictionary<string, string> Tokens(string file)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(Path.Combine(Themes, file)).Root!.Elements()
            .ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (string)e.Attribute("Color")!);
    }

    static double Luminance(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..];   // alpha is not part of these checks
        double C(int i)
        {
            double c = int.Parse(h.Substring(i, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * C(0) + 0.7152 * C(2) + 0.0722 * C(4);
    }

    static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void DarkAndLight_SameKeys_ValidColours()
    {
        var dark = Tokens("Dark.xaml");
        var light = Tokens("Light.xaml");
        Assert.Equal(dark.Keys.Order(), light.Keys.Order());
        Assert.All(dark.Values.Concat(light.Values), v => Assert.Matches("^#([0-9A-F]{6}|[0-9A-F]{8})$", v));
    }

    [Theory]
    [InlineData("Bg", "#0E1012")]
    [InlineData("BgHeader", "#121518")]
    [InlineData("Panel", "#14171A")]
    [InlineData("Raised", "#1B1F23")]
    [InlineData("RaisedAlt", "#171A1E")]
    [InlineData("Input", "#0F1215")]
    [InlineData("Line", "#262B31")]
    [InlineData("LineSoft", "#22272C")]
    [InlineData("Track", "#2A3038")]
    [InlineData("Text", "#ECEFF2")]
    [InlineData("Text2", "#A7AFB8")]
    [InlineData("Text3", "#808994")]
    [InlineData("Accent", "#2366D1")]
    [InlineData("AccentText", "#7DB6FF")]
    [InlineData("AccentBar", "#5AA2FF")]
    [InlineData("AccentSoft", "#1E3354")]
    [InlineData("AccentBorder", "#3A6FBF")]
    [InlineData("Success", "#42D392")]
    [InlineData("SuccessBg", "#111A16")]
    [InlineData("SuccessBorder", "#1F4A36")]
    [InlineData("SuccessText2", "#9FD9BC")]
    [InlineData("Warn", "#F2B04A")]
    [InlineData("WarnBg", "#1A1510")]
    [InlineData("WarnBorder", "#5A3A1E")]
    [InlineData("WarnText2", "#E8C891")]
    [InlineData("Danger", "#FF7D6B")]
    [InlineData("DangerText", "#FF9A8A")]
    [InlineData("DangerText2", "#FFB4A6")]
    [InlineData("DiskUsed", "#5D6670")]
    public void Dark_IsTheDesign(string key, string hex) => Assert.Equal(hex, Tokens("Dark.xaml")[key]);

    // README "Design tokeny": Text3 at least 4.5 : 1 on Panel — every text colour on every background it is used on.
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void Texts_Readable(string file)
    {
        var t = Tokens(file);
        foreach (var fg in new[] { "Text", "Text2", "Text3", "TextSoft", "AccentText" })
            foreach (var bg in new[] { "Bg", "BgHeader", "Panel", "Raised", "Input" })
                Assert.True(Contrast(t[fg], t[bg]) >= 4.5, $"{file}: {fg} on {bg} = {Contrast(t[fg], t[bg]):0.00}");
        foreach (var (fg, bg) in new[] { ("Success", "SuccessBg"), ("SuccessText2", "SuccessBg"), ("SuccessLabel", "SuccessBg"), ("Warn", "WarnBg"),
                     ("WarnText2", "WarnBg"), ("WarnSoftText", "WarnSoftBg"), ("DangerText", "Bg"), ("DangerText", "DangerBg"), ("DangerText2", "WarnListBg"),
                     ("OnAccent", "Accent"), ("OnSuccess", "SuccessFill"), ("OnWarn", "WarnFill"), ("PillRunText", "AccentSoft2"), ("AccentText", "AccentSoft") })
            Assert.True(Contrast(t[fg], t[bg]) >= 4.5, $"{file}: {fg} on {bg} = {Contrast(t[fg], t[bg]):0.00}");
    }

    [Fact]
    public void Choice_WindowsSetting_OverrideWins()
    {
        Assert.Equal(AppTheme.Dark, ThemeChoice.Resolve(null, 0));
        Assert.Equal(AppTheme.Light, ThemeChoice.Resolve(null, 1));
        Assert.Equal(AppTheme.Light, ThemeChoice.Resolve(null, null));      // no value: the Windows default
        Assert.Equal(AppTheme.Light, ThemeChoice.Resolve(null, "0"));       // not a DWORD: ignored
        Assert.Equal(AppTheme.Dark, ThemeChoice.Resolve(" DARK ", 1));
        Assert.Equal(AppTheme.Light, ThemeChoice.Resolve("light", 0));
        Assert.Equal(AppTheme.Dark, ThemeChoice.Resolve("blue", 0));
        Assert.True(ThemeChoice.IsOverride("Dark"));
        Assert.False(ThemeChoice.IsOverride(""));
    }

    // Every dictionary parses, and every style of Offload.xaml finds what it refers to (StaticResource inside it, the
    // icons it merges). Colours are DynamicResource — resolved by the running window.
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    [InlineData("Icons.xaml")]
    [InlineData("Offload.xaml")]
    public void Dictionary_Loads(string file)
    {
        Exception? error = null;
        int count = 0;
        var t = new Thread(() =>
        {
            try
            {
                var path = Path.Combine(Themes, file);
                var context = new ParserContext { BaseUri = new Uri(path, UriKind.Absolute) };
                using var stream = File.OpenRead(path);
                var dictionary = (ResourceDictionary)XamlReader.Load(stream, context);
                foreach (var key in dictionary.Keys) _ = dictionary[key];   // realises deferred values (and their StaticResources)
                count = dictionary.Keys.Count + dictionary.MergedDictionaries.Sum(m => m.Keys.Count);
                if (file is "Dark.xaml" or "Light.xaml") Assert.IsType<SolidColorBrush>(dictionary["Bg"]);
            }
            catch (Exception e) { error = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Assert.Null(error);
        Assert.True(count > 10);
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ThemeTokensTests"`

Expected: FAIL — build selže — `CS0103: Název ThemeChoice v aktuálním kontextu neexistuje`; po přidání `ThemeChoice` by test selhal na chybějícím `Themes/Dark.xaml`.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.App/App.xaml`

Najít (1/2):

```xml
             ThemeMode="System"
             ShutdownMode="OnMainWindowClose">
    <Application.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVisible" />
        <local:BoolToCollapsedConverter x:Key="BoolToCollapsed" />
        <local:NullToCollapsedConverter x:Key="NullToCollapsed" />
```

Nahradit:

```xml
             ThemeMode="System"
             ShutdownMode="OnMainWindowClose">
    <Application.Resources>
        <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <!-- Index 0: the colour tokens, Dark.xaml or Light.xaml — ThemeWatcher swaps it by the Windows setting. -->
            <ResourceDictionary Source="Themes/Dark.xaml" />
            <ResourceDictionary Source="Themes/Offload.xaml" />
        </ResourceDictionary.MergedDictionaries>
        <BooleanToVisibilityConverter x:Key="BoolToVisible" />
        <local:BoolToCollapsedConverter x:Key="BoolToCollapsed" />
        <local:NullToCollapsedConverter x:Key="NullToCollapsed" />
```

Najít (2/2):

```xml
        <Style x:Key="ErrorText" TargetType="TextBlock" BasedOn="{StaticResource WarningText}">
            <Setter Property="Foreground" Value="{DynamicResource SystemFillColorCriticalBrush}" />
        </Style>
    </Application.Resources>
</Application>
```

Nahradit:

```xml
        <Style x:Key="ErrorText" TargetType="TextBlock" BasedOn="{StaticResource WarningText}">
            <Setter Property="Foreground" Value="{DynamicResource SystemFillColorCriticalBrush}" />
        </Style>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

#### `windows/src/Offload.App/App.xaml.cs`

Najít (1/1):

```csharp
        };
        try
        {
            var window = new MainWindow(new MainViewModel(StartupOptions.Parse(e.Args)));
            MainWindow = window;
            window.Show();
```

Nahradit:

```csharp
        };
        try
        {
            ThemeWatcher.Start(this);
            var window = new MainWindow(new MainViewModel(StartupOptions.Parse(e.Args)));
            MainWindow = window;
            window.Show();
```

#### `windows/src/Offload.App/ThemeWatcher.cs` (nový soubor)

```csharp
using System.Security;
using System.Windows;
using Microsoft.Win32;
using Offload.Core.Desktop;

namespace Offload.App;

/// Swaps Themes/Dark.xaml and Themes/Light.xaml (the first merged dictionary of App.xaml) by the Windows setting, at start
/// and whenever the user changes it (WM_SETTINGCHANGE → SystemEvents.UserPreferenceChanged, category General). Reading
/// the setting is a registry read (memory), never a disk.
static class ThemeWatcher
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    static ResourceDictionary? current;

    public static void Start(Application app)
    {
        var forced = Environment.GetEnvironmentVariable(ThemeChoice.OverrideVariable);
        if (ThemeChoice.IsOverride(forced))
        {
            // Screenshots: the whole app (also the Fluent controls) in the theme asked for.
            var theme = ThemeChoice.Resolve(forced, null);
            app.ThemeMode = theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;   // WPF0001 is off in the csproj
            Apply(app, theme);
            return;
        }
        Apply(app, ThemeChoice.Resolve(null, ReadSetting()));
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) app.Dispatcher.BeginInvoke(() => Apply(app, ThemeChoice.Resolve(null, ReadSetting())));
        };
    }

    static object? ReadSetting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme");
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            AppLog.Write($"theme: {e.Message}");
            return null;
        }
    }

    static void Apply(Application app, AppTheme theme)
    {
        var source = new Uri($"pack://application:,,,/Themes/{theme}.xaml", UriKind.Absolute);
        if (current?.Source == source) return;
        var next = new ResourceDictionary { Source = source };
        var merged = app.Resources.MergedDictionaries;
        if (current != null) merged.Remove(current);
        merged.Insert(0, next);
        current = next;
    }
}
```

#### `windows/src/Offload.App/Themes/Dark.xaml` (nový soubor)

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Tmavé téma. Same keys in Dark.xaml and Light.xaml (ThemeTokensTests); ThemeWatcher swaps them by the Windows setting.
         Colours from docs/ui-navrh (README "Design tokeny", mockup/*.html); the light set is derived (contrast checked by the test). -->
    <SolidColorBrush x:Key="Bg" Color="#0E1012" /> <!-- pozadí okna, pravý sloupec -->
    <SolidColorBrush x:Key="BgHeader" Color="#121518" /> <!-- horní lišta, prázdné / čekající karty ve frontě -->
    <SolidColorBrush x:Key="Panel" Color="#14171A" /> <!-- levý sloupec, panely -->
    <SolidColorBrush x:Key="Raised" Color="#1B1F23" /> <!-- dlaždice (karta, disk), sekundární tlačítka -->
    <SolidColorBrush x:Key="RaisedAlt" Color="#171A1E" /> <!-- nevybraná karta -->
    <SolidColorBrush x:Key="Input" Color="#0F1215" /> <!-- textová pole, tabulka rychlostí -->
    <SolidColorBrush x:Key="Line" Color="#262B31" /> <!-- okraje panelů a dlaždic -->
    <SolidColorBrush x:Key="LineSoft" Color="#22272C" /> <!-- oddělovače řádků -->
    <SolidColorBrush x:Key="LineStrong" Color="#2E343B" /> <!-- okraje tlačítek -->
    <SolidColorBrush x:Key="LineInput" Color="#323840" /> <!-- okraje vstupů -->
    <SolidColorBrush x:Key="Track" Color="#2A3038" /> <!-- podklad progress barů, kolečka s číslem disku -->
    <SolidColorBrush x:Key="Text" Color="#ECEFF2" /> <!-- primární text -->
    <SolidColorBrush x:Key="Text2" Color="#A7AFB8" /> <!-- sekundární text, popisky -->
    <SolidColorBrush x:Key="Text3" Color="#808994" /> <!-- nejslabší text (cesty, nápovědy) — min. 4,5 : 1 na Panel -->
    <SolidColorBrush x:Key="TextSoft" Color="#C9D0D7" /> <!-- text tlačítek v liště, názvy řádků -->
    <SolidColorBrush x:Key="Accent" Color="#2366D1" /> <!-- primární tlačítko (bílý text), logo -->
    <SolidColorBrush x:Key="AccentHover" Color="#2B72E0" /> <!-- primární tlačítko pod myší -->
    <SolidColorBrush x:Key="AccentText" Color="#7DB6FF" /> <!-- odkazy, ikony na modrém pozadí -->
    <SolidColorBrush x:Key="AccentBar" Color="#5AA2FF" /> <!-- progress, checkboxy, potřebné místo v pruhu disku -->
    <SolidColorBrush x:Key="AccentSoft" Color="#1E3354" /> <!-- pozadí ikony karty -->
    <SolidColorBrush x:Key="AccentSoft2" Color="#172235" /> <!-- vybrané řádky, pilulka Běží -->
    <SolidColorBrush x:Key="AccentBorder" Color="#3A6FBF" /> <!-- okraj vybrané karty -->
    <SolidColorBrush x:Key="PillRunText" Color="#A9CEFF" /> <!-- text pilulky Běží -->
    <SolidColorBrush x:Key="OnAccent" Color="#FFFFFF" /> <!-- text na Accent -->
    <SolidColorBrush x:Key="Success" Color="#42D392" /> <!-- hotovo, ověřeno (text) -->
    <SolidColorBrush x:Key="SuccessFill" Color="#42D392" /> <!-- zelená výplň (štít, Vysunout kartu) -->
    <SolidColorBrush x:Key="OnSuccess" Color="#0B1F15" /> <!-- text na SuccessFill -->
    <SolidColorBrush x:Key="SuccessBg" Color="#111A16" /> <!-- karta hotovo -->
    <SolidColorBrush x:Key="SuccessBorder" Color="#1F4A36" /> <!-- okraj karty hotovo -->
    <SolidColorBrush x:Key="SuccessLine" Color="#1C3328" /> <!-- oddělovače v kartě hotovo -->
    <SolidColorBrush x:Key="SuccessText2" Color="#9FD9BC" /> <!-- popis pod nadpisem hotovo -->
    <SolidColorBrush x:Key="SuccessLabel" Color="#8FA89B" /> <!-- popisky čísel v kartě hotovo -->
    <SolidColorBrush x:Key="SuccessBtnBg" Color="#16201B" /> <!-- tlačítka v kartě hotovo -->
    <SolidColorBrush x:Key="SuccessBtnBorder" Color="#2B3A33" /> <!-- okraj tlačítek v kartě hotovo -->
    <SolidColorBrush x:Key="CheckBg" Color="#163A2A" /> <!-- zelené odškrtnutí v kroku 3 -->
    <SolidColorBrush x:Key="Warn" Color="#F2B04A" /> <!-- už zálohováno, nesouhlasí soubor, málo místa (text) -->
    <SolidColorBrush x:Key="WarnFill" Color="#F2B04A" /> <!-- žlutá výplň (trojúhelník, Zkopírovat znovu) -->
    <SolidColorBrush x:Key="OnWarn" Color="#2A1A05" /> <!-- text na WarnFill -->
    <SolidColorBrush x:Key="WarnBg" Color="#1A1510" /> <!-- karta s varováním -->
    <SolidColorBrush x:Key="WarnBorder" Color="#5A3A1E" /> <!-- okraj karty s varováním -->
    <SolidColorBrush x:Key="WarnText2" Color="#E8C891" /> <!-- popis varování -->
    <SolidColorBrush x:Key="WarnListBg" Color="#120F0B" /> <!-- seznam nesouhlasných souborů -->
    <SolidColorBrush x:Key="WarnListBorder" Color="#3A2A18" /> <!-- okraj seznamu nesouhlasných souborů -->
    <SolidColorBrush x:Key="WarnSoftBg" Color="#2A2418" /> <!-- tlačítko Doplnit -->
    <SolidColorBrush x:Key="WarnSoftBorder" Color="#3B3426" /> <!-- okraj tlačítka Doplnit -->
    <SolidColorBrush x:Key="WarnSoftText" Color="#F2C77A" /> <!-- text tlačítka Doplnit -->
    <SolidColorBrush x:Key="Danger" Color="#FF7D6B" /> <!-- chyba (ikony, výplně) -->
    <SolidColorBrush x:Key="DangerText" Color="#FF9A8A" /> <!-- Zrušit, text chyby -->
    <SolidColorBrush x:Key="DangerText2" Color="#FFB4A6" /> <!-- ✕ hash v seznamu souborů -->
    <SolidColorBrush x:Key="DangerBorder" Color="#3F2A27" /> <!-- okraj tlačítka Zrušit -->
    <SolidColorBrush x:Key="DangerBg" Color="#1C1312" /> <!-- karta s chybou -->
    <SolidColorBrush x:Key="DangerCardBorder" Color="#5A2A24" /> <!-- okraj karty s chybou -->
    <SolidColorBrush x:Key="DiskUsed" Color="#5D6670" /> <!-- obsazené místo v pruhu disku -->
    <SolidColorBrush x:Key="Chip" Color="#23292F" /> <!-- štítek kamery, šedé ikony, pilulka Čeká -->
    <SolidColorBrush x:Key="TabActive" Color="#1F2429" /> <!-- vybraná záložka Fronta / Historie karet -->
    <SolidColorBrush x:Key="DashedLine" Color="#343B43" /> <!-- přerušovaný rámeček prázdné karty -->
    <SolidColorBrush x:Key="RunBorder" Color="#2B3B55" /> <!-- okraj běžící úlohy -->
    <SolidColorBrush x:Key="RunBg" Color="#13171C" /> <!-- pozadí běžící úlohy -->
    <SolidColorBrush x:Key="RowLine" Color="#1D2125" /> <!-- oddělovače v tabulkách -->
    <SolidColorBrush x:Key="FlowLine" Color="#3A424B" /> <!-- čáry mezi ikonami prázdné fronty -->
    <SolidColorBrush x:Key="NavActive" Color="#1F2933" /> <!-- vybraná sekce Nastavení -->
    <SolidColorBrush x:Key="SegmentBg" Color="#171A1E" /> <!-- podklad segmentového přepínače -->
    <SolidColorBrush x:Key="SegmentActive" Color="#2E3640" /> <!-- vybraný segment -->
    <SolidColorBrush x:Key="ThumbBg" Color="#26302B" /> <!-- dlaždice náhledu bez snímku -->
    <SolidColorBrush x:Key="HoverBg" Color="#22272C" /> <!-- tlačítka bez rámečku pod myší -->
    <SolidColorBrush x:Key="PreviewBg" Color="#2A2F33" /> <!-- náhled burn-inu (obraz, v obou tématech tmavý) -->
    <SolidColorBrush x:Key="PreviewText" Color="#6F7882" /> <!-- text v náhledu burn-inu -->
    <SolidColorBrush x:Key="Shade" Color="#99000000" /> <!-- podklad popisků v náhledu burn-inu -->
    <SolidColorBrush x:Key="FrameLine" Color="#8CFFFFFF" /> <!-- frame lines v náhledu burn-inu -->
</ResourceDictionary>
```

#### `windows/src/Offload.App/Themes/Icons.xaml` (nový soubor)

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- The line icons of docs/ui-navrh/mockup (SVG, viewBox 0 0 24 24), drawn by the IconLine / IconFill styles in
         Offload.xaml: a 24×24 canvas scaled to the control's size, stroke = Foreground. Circles and rectangles are paths. -->
    <Geometry x:Key="IconLogo">M12 3v11 M7.5 9.5L12 14l4.5-4.5 M4 15v4h16v-4</Geometry>
    <Geometry x:Key="IconChevronDown">M6 9l6 6 6-6</Geometry>
    <Geometry x:Key="IconBack">M15 6l-6 6 6 6</Geometry>
    <Geometry x:Key="IconShield">M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z M9 12l2 2 4-4</Geometry>
    <Geometry x:Key="IconChip">M8 6h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z M9 2v4M15 2v4M9 18v4M15 18v4M2 9h4M2 15h4M18 9h4M18 15h4</Geometry>
    <Geometry x:Key="IconSliders">M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12 M18 6a2 2 0 1 1-4 0a2 2 0 1 1 4 0z M12 12a2 2 0 1 1-4 0a2 2 0 1 1 4 0z M20 18a2 2 0 1 1-4 0a2 2 0 1 1 4 0z</Geometry>
    <Geometry x:Key="IconCard">M8 3h7l4 4v13a1 1 0 0 1-1 1H8a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M10.5 3v3.5M13.5 3v3.5</Geometry>
    <Geometry x:Key="IconCardPlain">M8 3h7l4 4v13a1 1 0 0 1-1 1H8a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z</Geometry>
    <Geometry x:Key="IconDisk">M4.5 13h15a1.5 1.5 0 0 1 1.5 1.5v4a1.5 1.5 0 0 1-1.5 1.5h-15a1.5 1.5 0 0 1-1.5-1.5v-4a1.5 1.5 0 0 1 1.5-1.5z M5 13l2-8h10l2 8 M17 16.5h.01</Geometry>
    <Geometry x:Key="IconVideo">M5 5h14a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M10 9.5v5l4-2.5z</Geometry>
    <Geometry x:Key="IconReport">M7 3h7l5 5v12a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M14 3v5h5M9 13h6M9 17h6</Geometry>
    <Geometry x:Key="IconFolder">M3 7a1 1 0 0 1 1-1h5l2 2h9a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z</Geometry>
    <Geometry x:Key="IconPlus">M12 5v14M5 12h14</Geometry>
    <Geometry x:Key="IconClose">M6 6l12 12M18 6L6 18</Geometry>
    <Geometry x:Key="IconCheck">M5 12.5l4.5 4.5L19 7.5</Geometry>
    <Geometry x:Key="IconInfo">M21 12a9 9 0 1 1-18 0a9 9 0 1 1 18 0z M12 8v4M12 16h.01</Geometry>
    <Geometry x:Key="IconWarning">M12 4l9 16H3z M12 10v4M12 17h.01</Geometry>
    <Geometry x:Key="IconEject">M12 5l7 8H5z M5 18h14</Geometry>
    <Geometry x:Key="IconRefresh">M20 12a8 8 0 1 1-2.34-5.66 M20 4v5h-5</Geometry>
    <Geometry x:Key="IconHistory">M3 12a9 9 0 1 0 2.64-6.36 M3 4v5h5 M12 7v5l3 2</Geometry>
    <Geometry x:Key="IconPause">M9 5v14M15 5v14</Geometry>
    <!-- filled -->
    <Geometry x:Key="IconPlay">M7 4.5v15l12-7.5z</Geometry>
</ResourceDictionary>
```

#### `windows/src/Offload.App/Themes/Light.xaml` (nový soubor)

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Světlé téma. Same keys in Dark.xaml and Light.xaml (ThemeTokensTests); ThemeWatcher swaps them by the Windows setting.
         Colours from docs/ui-navrh (README "Design tokeny", mockup/*.html); the light set is derived (contrast checked by the test). -->
    <SolidColorBrush x:Key="Bg" Color="#F3F5F7" /> <!-- pozadí okna, pravý sloupec -->
    <SolidColorBrush x:Key="BgHeader" Color="#FFFFFF" /> <!-- horní lišta, prázdné / čekající karty ve frontě -->
    <SolidColorBrush x:Key="Panel" Color="#FFFFFF" /> <!-- levý sloupec, panely -->
    <SolidColorBrush x:Key="Raised" Color="#F7F8FA" /> <!-- dlaždice (karta, disk), sekundární tlačítka -->
    <SolidColorBrush x:Key="RaisedAlt" Color="#F1F3F5" /> <!-- nevybraná karta -->
    <SolidColorBrush x:Key="Input" Color="#FFFFFF" /> <!-- textová pole, tabulka rychlostí -->
    <SolidColorBrush x:Key="Line" Color="#DCE1E6" /> <!-- okraje panelů a dlaždic -->
    <SolidColorBrush x:Key="LineSoft" Color="#E6E9ED" /> <!-- oddělovače řádků -->
    <SolidColorBrush x:Key="LineStrong" Color="#C8CFD6" /> <!-- okraje tlačítek -->
    <SolidColorBrush x:Key="LineInput" Color="#C3CAD2" /> <!-- okraje vstupů -->
    <SolidColorBrush x:Key="Track" Color="#E1E5EA" /> <!-- podklad progress barů, kolečka s číslem disku -->
    <SolidColorBrush x:Key="Text" Color="#15191D" /> <!-- primární text -->
    <SolidColorBrush x:Key="Text2" Color="#4B545E" /> <!-- sekundární text, popisky -->
    <SolidColorBrush x:Key="Text3" Color="#5E6873" /> <!-- nejslabší text (cesty, nápovědy) — min. 4,5 : 1 na Panel -->
    <SolidColorBrush x:Key="TextSoft" Color="#3A424B" /> <!-- text tlačítek v liště, názvy řádků -->
    <SolidColorBrush x:Key="Accent" Color="#2366D1" /> <!-- primární tlačítko (bílý text), logo -->
    <SolidColorBrush x:Key="AccentHover" Color="#1D5BC0" /> <!-- primární tlačítko pod myší -->
    <SolidColorBrush x:Key="AccentText" Color="#1A56B8" /> <!-- odkazy, ikony na modrém pozadí -->
    <SolidColorBrush x:Key="AccentBar" Color="#2F7BE6" /> <!-- progress, checkboxy, potřebné místo v pruhu disku -->
    <SolidColorBrush x:Key="AccentSoft" Color="#E2ECFB" /> <!-- pozadí ikony karty -->
    <SolidColorBrush x:Key="AccentSoft2" Color="#EEF4FD" /> <!-- vybrané řádky, pilulka Běží -->
    <SolidColorBrush x:Key="AccentBorder" Color="#7FA8E6" /> <!-- okraj vybrané karty -->
    <SolidColorBrush x:Key="PillRunText" Color="#1A56B8" /> <!-- text pilulky Běží -->
    <SolidColorBrush x:Key="OnAccent" Color="#FFFFFF" /> <!-- text na Accent -->
    <SolidColorBrush x:Key="Success" Color="#147A4B" /> <!-- hotovo, ověřeno (text) -->
    <SolidColorBrush x:Key="SuccessFill" Color="#2BBF7A" /> <!-- zelená výplň (štít, Vysunout kartu) -->
    <SolidColorBrush x:Key="OnSuccess" Color="#06281A" /> <!-- text na SuccessFill -->
    <SolidColorBrush x:Key="SuccessBg" Color="#EEF8F2" /> <!-- karta hotovo -->
    <SolidColorBrush x:Key="SuccessBorder" Color="#A9DDBF" /> <!-- okraj karty hotovo -->
    <SolidColorBrush x:Key="SuccessLine" Color="#CDEBD9" /> <!-- oddělovače v kartě hotovo -->
    <SolidColorBrush x:Key="SuccessText2" Color="#1E6B45" /> <!-- popis pod nadpisem hotovo -->
    <SolidColorBrush x:Key="SuccessLabel" Color="#4F6B5D" /> <!-- popisky čísel v kartě hotovo -->
    <SolidColorBrush x:Key="SuccessBtnBg" Color="#FFFFFF" /> <!-- tlačítka v kartě hotovo -->
    <SolidColorBrush x:Key="SuccessBtnBorder" Color="#A9DDBF" /> <!-- okraj tlačítek v kartě hotovo -->
    <SolidColorBrush x:Key="CheckBg" Color="#DDF3E7" /> <!-- zelené odškrtnutí v kroku 3 -->
    <SolidColorBrush x:Key="Warn" Color="#8F5600" /> <!-- už zálohováno, nesouhlasí soubor, málo místa (text) -->
    <SolidColorBrush x:Key="WarnFill" Color="#F2B04A" /> <!-- žlutá výplň (trojúhelník, Zkopírovat znovu) -->
    <SolidColorBrush x:Key="OnWarn" Color="#2A1A05" /> <!-- text na WarnFill -->
    <SolidColorBrush x:Key="WarnBg" Color="#FFF6E6" /> <!-- karta s varováním -->
    <SolidColorBrush x:Key="WarnBorder" Color="#EBC27F" /> <!-- okraj karty s varováním -->
    <SolidColorBrush x:Key="WarnText2" Color="#7A4D0A" /> <!-- popis varování -->
    <SolidColorBrush x:Key="WarnListBg" Color="#FFFBF3" /> <!-- seznam nesouhlasných souborů -->
    <SolidColorBrush x:Key="WarnListBorder" Color="#F1D7A8" /> <!-- okraj seznamu nesouhlasných souborů -->
    <SolidColorBrush x:Key="WarnSoftBg" Color="#FFF1D6" /> <!-- tlačítko Doplnit -->
    <SolidColorBrush x:Key="WarnSoftBorder" Color="#EBC27F" /> <!-- okraj tlačítka Doplnit -->
    <SolidColorBrush x:Key="WarnSoftText" Color="#7A4D0A" /> <!-- text tlačítka Doplnit -->
    <SolidColorBrush x:Key="Danger" Color="#C2382B" /> <!-- chyba (ikony, výplně) -->
    <SolidColorBrush x:Key="DangerText" Color="#B42318" /> <!-- Zrušit, text chyby -->
    <SolidColorBrush x:Key="DangerText2" Color="#A3261A" /> <!-- ✕ hash v seznamu souborů -->
    <SolidColorBrush x:Key="DangerBorder" Color="#F0B4AC" /> <!-- okraj tlačítka Zrušit -->
    <SolidColorBrush x:Key="DangerBg" Color="#FFF1EF" /> <!-- karta s chybou -->
    <SolidColorBrush x:Key="DangerCardBorder" Color="#F0B4AC" /> <!-- okraj karty s chybou -->
    <SolidColorBrush x:Key="DiskUsed" Color="#A3ACB6" /> <!-- obsazené místo v pruhu disku -->
    <SolidColorBrush x:Key="Chip" Color="#EBEEF1" /> <!-- štítek kamery, šedé ikony, pilulka Čeká -->
    <SolidColorBrush x:Key="TabActive" Color="#E6EAEE" /> <!-- vybraná záložka Fronta / Historie karet -->
    <SolidColorBrush x:Key="DashedLine" Color="#C3CAD2" /> <!-- přerušovaný rámeček prázdné karty -->
    <SolidColorBrush x:Key="RunBorder" Color="#9DB9E6" /> <!-- okraj běžící úlohy -->
    <SolidColorBrush x:Key="RunBg" Color="#FFFFFF" /> <!-- pozadí běžící úlohy -->
    <SolidColorBrush x:Key="RowLine" Color="#E6E9ED" /> <!-- oddělovače v tabulkách -->
    <SolidColorBrush x:Key="FlowLine" Color="#C3CAD2" /> <!-- čáry mezi ikonami prázdné fronty -->
    <SolidColorBrush x:Key="NavActive" Color="#E2ECFB" /> <!-- vybraná sekce Nastavení -->
    <SolidColorBrush x:Key="SegmentBg" Color="#EBEEF1" /> <!-- podklad segmentového přepínače -->
    <SolidColorBrush x:Key="SegmentActive" Color="#FFFFFF" /> <!-- vybraný segment -->
    <SolidColorBrush x:Key="ThumbBg" Color="#DDE3E0" /> <!-- dlaždice náhledu bez snímku -->
    <SolidColorBrush x:Key="HoverBg" Color="#E9EDF1" /> <!-- tlačítka bez rámečku pod myší -->
    <SolidColorBrush x:Key="PreviewBg" Color="#2A2F33" /> <!-- náhled burn-inu (obraz, v obou tématech tmavý) -->
    <SolidColorBrush x:Key="PreviewText" Color="#8A939C" /> <!-- text v náhledu burn-inu -->
    <SolidColorBrush x:Key="Shade" Color="#99000000" /> <!-- podklad popisků v náhledu burn-inu -->
    <SolidColorBrush x:Key="FrameLine" Color="#8CFFFFFF" /> <!-- frame lines v náhledu burn-inu -->
</ResourceDictionary>
```

#### `windows/src/Offload.App/Themes/Offload.xaml` (nový soubor)

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- The styles of the new look (docs/ui-navrh, "Rozměry" and "Typografie"). Every colour is a DynamicResource of
         Dark.xaml / Light.xaml, so a switch of the Windows theme repaints the window at once. -->

    <!-- The icon geometries are merged here, so the styles below find them as StaticResource. -->
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="Icons.xaml" />
    </ResourceDictionary.MergedDictionaries>

    <FontFamily x:Key="UiFont">Segoe UI Variable Text, Segoe UI</FontFamily>
    <FontFamily x:Key="MonoFont">Cascadia Mono, Consolas</FontFamily>

    <!-- ── Icons: Content = a Geometry of Icons.xaml, Foreground = its colour ───────────────────────────────────── -->
    <Style x:Key="IconLine" TargetType="ContentControl">
        <Setter Property="Width" Value="16" />
        <Setter Property="Height" Value="16" />
        <Setter Property="Focusable" Value="False" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="Tag" Value="1.7" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ContentControl">
                    <Viewbox Stretch="Uniform">
                        <Canvas Width="24" Height="24">
                            <Path Data="{Binding Content, RelativeSource={RelativeSource TemplatedParent}}" Stroke="{TemplateBinding Foreground}"
                                  StrokeThickness="{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}"
                                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" />
                        </Canvas>
                    </Viewbox>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style x:Key="IconFill" TargetType="ContentControl" BasedOn="{StaticResource IconLine}">
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ContentControl">
                    <Viewbox Stretch="Uniform">
                        <Canvas Width="24" Height="24">
                            <Path Data="{Binding Content, RelativeSource={RelativeSource TemplatedParent}}" Fill="{TemplateBinding Foreground}" />
                        </Canvas>
                    </Viewbox>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ── Text ───────────────────────────────────────────────────────────────────────────────────────────────── -->
    <!-- "1 · KARTY": 12 / SemiBold / Text2 (WPF has no letter spacing; the text itself is in capitals). -->
    <Style x:Key="StepTitle" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{DynamicResource Text2}" />
        <Setter Property="VerticalAlignment" Value="Center" />
    </Style>
    <Style x:Key="TextMuted" TargetType="TextBlock">
        <Setter Property="FontSize" Value="13" />
        <Setter Property="Foreground" Value="{DynamicResource Text2}" />
        <Setter Property="TextWrapping" Value="Wrap" />
    </Style>
    <Style x:Key="TextFaint" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="{DynamicResource Text3}" />
        <Setter Property="TextWrapping" Value="Wrap" />
    </Style>
    <Style x:Key="TextMono" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource MonoFont}" />
        <Setter Property="FontSize" Value="13" />
        <Setter Property="Foreground" Value="{DynamicResource TextSoft}" />
    </Style>
    <Style x:Key="TextMonoFaint" TargetType="TextBlock" BasedOn="{StaticResource TextMono}">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="{DynamicResource Text3}" />
        <Setter Property="TextTrimming" Value="CharacterEllipsis" />
    </Style>
    <Style x:Key="TextWarn" TargetType="TextBlock">
        <Setter Property="FontSize" Value="13" />
        <Setter Property="Foreground" Value="{DynamicResource Warn}" />
        <Setter Property="TextWrapping" Value="Wrap" />
    </Style>
    <Style x:Key="TextDanger" TargetType="TextBlock" BasedOn="{StaticResource TextWarn}">
        <Setter Property="Foreground" Value="{DynamicResource DangerText}" />
    </Style>

    <!-- ── Surfaces ───────────────────────────────────────────────────────────────────────────────────────────── -->
    <Style x:Key="TileBorder" TargetType="Border">
        <Setter Property="CornerRadius" Value="12" />
        <Setter Property="Background" Value="{DynamicResource Raised}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Line}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Padding" Value="14,12" />
    </Style>
    <Style x:Key="ListBorder" TargetType="Border">
        <Setter Property="CornerRadius" Value="12" />
        <Setter Property="BorderBrush" Value="{DynamicResource LineSoft}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="ClipToBounds" Value="True" />
    </Style>
    <Style x:Key="PillBorder" TargetType="Border">
        <Setter Property="CornerRadius" Value="99" />
        <Setter Property="Padding" Value="10,4" />
        <Setter Property="Background" Value="{DynamicResource Chip}" />
        <Setter Property="VerticalAlignment" Value="Center" />
    </Style>
    <!-- The round number of a destination (24 px, Track). -->
    <Style x:Key="NumberBadge" TargetType="Border">
        <Setter Property="Width" Value="24" />
        <Setter Property="Height" Value="24" />
        <Setter Property="CornerRadius" Value="12" />
        <Setter Property="Background" Value="{DynamicResource Track}" />
        <Setter Property="VerticalAlignment" Value="Center" />
    </Style>
    <!-- The dashed empty state of step 1: ContentControl with the content inside a dashed rounded frame. -->
    <Style x:Key="DashedBox" TargetType="ContentControl">
        <Setter Property="Padding" Value="18,26" />
        <Setter Property="Focusable" Value="False" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ContentControl">
                    <Grid>
                        <Rectangle RadiusX="12" RadiusY="12" Stroke="{DynamicResource DashedLine}" StrokeThickness="1" StrokeDashArray="4 3" />
                        <ContentPresenter Margin="{TemplateBinding Padding}" />
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ── Buttons ────────────────────────────────────────────────────────────────────────────────────────────── -->
    <ControlTemplate x:Key="Flat8" TargetType="ButtonBase">
        <Border x:Name="Chrome" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                              RecognizesAccessKey="True" />
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="Chrome" Property="Opacity" Value="0.8" />
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>
    <ControlTemplate x:Key="Flat10" TargetType="ButtonBase">
        <Border x:Name="Chrome" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="10" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                              RecognizesAccessKey="True" />
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="Chrome" Property="Opacity" Value="0.8" />
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <Style x:Key="ButtonBaseFlat" TargetType="ButtonBase">
        <Setter Property="Template" Value="{StaticResource Flat8}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FontSize" Value="13" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="BorderBrush" Value="Transparent" />
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="HorizontalContentAlignment" Value="Center" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Style.Triggers>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.5" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
                <Setter Property="BorderBrush" Value="{DynamicResource AccentBar}" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- ▶ Spustit offload: 48 px, Accent, radius 10; disabled = Raised with a LineStrong edge and Text3 (obr. 2, 3). -->
    <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource ButtonBaseFlat}">
        <Setter Property="Template" Value="{StaticResource Flat10}" />
        <Setter Property="Height" Value="48" />
        <Setter Property="FontSize" Value="15" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Background" Value="{DynamicResource Accent}" />
        <Setter Property="Foreground" Value="{DynamicResource OnAccent}" />
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{DynamicResource AccentHover}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Background" Value="{DynamicResource Raised}" />
                <Setter Property="BorderBrush" Value="{DynamicResource LineStrong}" />
                <Setter Property="Foreground" Value="{DynamicResource Text3}" />
                <Setter Property="Opacity" Value="1" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style x:Key="SecondaryButton" TargetType="ButtonBase" BasedOn="{StaticResource ButtonBaseFlat}">
        <Setter Property="Height" Value="34" />
        <Setter Property="Padding" Value="12,0" />
        <Setter Property="Background" Value="{DynamicResource Raised}" />
        <Setter Property="BorderBrush" Value="{DynamicResource LineStrong}" />
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{DynamicResource HoverBg}" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style x:Key="GhostButton" TargetType="ButtonBase" BasedOn="{StaticResource ButtonBaseFlat}">
        <Setter Property="Height" Value="34" />
        <Setter Property="Padding" Value="12,0" />
        <Setter Property="Foreground" Value="{DynamicResource TextSoft}" />
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{DynamicResource HoverBg}" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style x:Key="IconButton" TargetType="ButtonBase" BasedOn="{StaticResource GhostButton}">
        <Setter Property="Width" Value="34" />
        <Setter Property="Padding" Value="0" />
    </Style>
    <Style x:Key="SmallIconButton" TargetType="ButtonBase" BasedOn="{StaticResource GhostButton}">
        <Setter Property="Width" Value="32" />
        <Setter Property="Height" Value="32" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="Foreground" Value="{DynamicResource Text3}" />
    </Style>
    <!-- "Ze složky…", "+ Přidat disk", "Upravit projekt": a link, 13 px, AccentText. -->
    <Style x:Key="LinkButton" TargetType="ButtonBase" BasedOn="{StaticResource ButtonBaseFlat}">
        <Setter Property="Padding" Value="0,6" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Foreground" Value="{DynamicResource AccentText}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ButtonBase">
                    <ContentPresenter Margin="{TemplateBinding Padding}" RecognizesAccessKey="True" />
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Opacity" Value="0.8" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style x:Key="DangerButton" TargetType="Button" BasedOn="{StaticResource ButtonBaseFlat}">
        <Setter Property="Height" Value="36" />
        <Setter Property="Padding" Value="14,0" />
        <Setter Property="BorderBrush" Value="{DynamicResource DangerBorder}" />
        <Setter Property="Foreground" Value="{DynamicResource DangerText}" />
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{DynamicResource HoverBg}" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style x:Key="SuccessButton" TargetType="Button" BasedOn="{StaticResource ButtonBaseFlat}">
        <Setter Property="Template" Value="{StaticResource Flat10}" />
        <Setter Property="Height" Value="44" />
        <Setter Property="Padding" Value="18,0" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Background" Value="{DynamicResource SuccessFill}" />
        <Setter Property="Foreground" Value="{DynamicResource OnSuccess}" />
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Opacity" Value="0.9" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style x:Key="WarnButton" TargetType="Button" BasedOn="{StaticResource SuccessButton}">
        <Setter Property="Height" Value="40" />
        <Setter Property="Padding" Value="16,0" />
        <Setter Property="Background" Value="{DynamicResource WarnFill}" />
        <Setter Property="Foreground" Value="{DynamicResource OnWarn}" />
    </Style>
    <!-- "Doplnit" on a backed-up card. -->
    <Style x:Key="WarnSoftButton" TargetType="ButtonBase" BasedOn="{StaticResource ButtonBaseFlat}">
        <Setter Property="Height" Value="32" />
        <Setter Property="Padding" Value="12,0" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="Background" Value="{DynamicResource WarnSoftBg}" />
        <Setter Property="BorderBrush" Value="{DynamicResource WarnSoftBorder}" />
        <Setter Property="Foreground" Value="{DynamicResource WarnSoftText}" />
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Opacity" Value="0.9" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <!-- Report PDF · Otevřít složku · Proxy on the green card. -->
    <Style x:Key="SuccessSecondaryButton" TargetType="Button" BasedOn="{StaticResource SecondaryButton}">
        <Setter Property="Height" Value="36" />
        <Setter Property="Padding" Value="14,0" />
        <Setter Property="Background" Value="{DynamicResource SuccessBtnBg}" />
        <Setter Property="BorderBrush" Value="{DynamicResource SuccessBtnBorder}" />
    </Style>

    <!-- Tabs "Fronta" / "Historie karet", settings sections and segmented choices: radio buttons. -->
    <Style x:Key="TabButton" TargetType="RadioButton" BasedOn="{StaticResource ButtonBaseFlat}">
        <Setter Property="Height" Value="32" />
        <Setter Property="Padding" Value="14,0" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Foreground" Value="{DynamicResource Text2}" />
        <Style.Triggers>
            <Trigger Property="IsChecked" Value="True">
                <Setter Property="Background" Value="{DynamicResource TabActive}" />
                <Setter Property="Foreground" Value="{DynamicResource Text}" />
                <Setter Property="FontWeight" Value="SemiBold" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style x:Key="NavButton" TargetType="RadioButton" BasedOn="{StaticResource TabButton}">
        <Setter Property="Height" Value="38" />
        <Setter Property="Padding" Value="12,0" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="HorizontalContentAlignment" Value="Left" />
        <Setter Property="Foreground" Value="{DynamicResource TextSoft}" />
        <Style.Triggers>
            <Trigger Property="IsChecked" Value="True">
                <Setter Property="Background" Value="{DynamicResource NavActive}" />
                <Setter Property="Foreground" Value="{DynamicResource Text}" />
                <Setter Property="FontWeight" Value="SemiBold" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style x:Key="SegmentButton" TargetType="RadioButton" BasedOn="{StaticResource TabButton}">
        <Setter Property="Padding" Value="12,0" />
        <Style.Triggers>
            <Trigger Property="IsChecked" Value="True">
                <Setter Property="Background" Value="{DynamicResource SegmentActive}" />
                <Setter Property="Foreground" Value="{DynamicResource Text}" />
                <Setter Property="FontWeight" Value="SemiBold" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style x:Key="SegmentHost" TargetType="Border">
        <Setter Property="CornerRadius" Value="9" />
        <Setter Property="Padding" Value="3" />
        <Setter Property="Background" Value="{DynamicResource SegmentBg}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Line}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="HorizontalAlignment" Value="Left" />
    </Style>

    <!-- ── Inputs ─────────────────────────────────────────────────────────────────────────────────────────────── -->
    <Style x:Key="InputBox" TargetType="TextBox">
        <Setter Property="Height" Value="36" />
        <Setter Property="Padding" Value="10,0" />
        <Setter Property="FontFamily" Value="{StaticResource MonoFont}" />
        <Setter Property="FontSize" Value="13" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="CaretBrush" Value="{DynamicResource Text}" />
        <Setter Property="Background" Value="{DynamicResource Input}" />
        <Setter Property="BorderBrush" Value="{DynamicResource LineInput}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border x:Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8">
                        <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" VerticalAlignment="Center" Focusable="False"
                                      HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBorder}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- A check box of the design: 18 px, AccentBar when ticked. -->
    <Style x:Key="Check" TargetType="CheckBox">
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <DockPanel Background="Transparent">
                        <Border x:Name="Box" Width="18" Height="18" CornerRadius="4" BorderThickness="1" BorderBrush="{DynamicResource LineInput}"
                                Background="{DynamicResource Input}" VerticalAlignment="Center">
                            <ContentControl x:Name="Tick" Style="{StaticResource IconLine}" Content="{StaticResource IconCheck}" Width="13" Height="13"
                                            Tag="2.8" Foreground="{DynamicResource OnAccent}" Visibility="Collapsed" />
                        </Border>
                        <ContentPresenter Margin="10,0,0,0" VerticalAlignment="{TemplateBinding VerticalContentAlignment}" RecognizesAccessKey="True" />
                    </DockPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Box" Property="Background" Value="{DynamicResource AccentBar}" />
                            <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource AccentBar}" />
                            <Setter TargetName="Tick" Property="Visibility" Value="Visible" />
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource AccentBorder}" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.5" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- "Zapnuto ( ●)": a switch, 42 × 24. -->
    <Style x:Key="Switch" TargetType="CheckBox" BasedOn="{StaticResource Check}">
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <DockPanel Background="Transparent">
                        <Border x:Name="Track" DockPanel.Dock="Right" Width="42" Height="24" CornerRadius="12" Padding="3" Background="{DynamicResource Track}"
                                VerticalAlignment="Center">
                            <Ellipse x:Name="Knob" Width="18" Height="18" Fill="{DynamicResource OnAccent}" HorizontalAlignment="Left" />
                        </Border>
                        <ContentPresenter Margin="0,0,10,0" VerticalAlignment="Center" TextElement.FontSize="13"
                                          TextElement.Foreground="{DynamicResource TextSoft}" />
                    </DockPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Track" Property="Background" Value="{DynamicResource Accent}" />
                            <Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ── Progress ───────────────────────────────────────────────────────────────────────────────────────────── -->
    <Style x:Key="ProgressThin" TargetType="ProgressBar">
        <Setter Property="Height" Value="6" />
        <Setter Property="Minimum" Value="0" />
        <Setter Property="Maximum" Value="1" />
        <Setter Property="Background" Value="{DynamicResource Track}" />
        <Setter Property="Foreground" Value="{DynamicResource AccentBar}" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ProgressBar">
                    <Border CornerRadius="3" Background="{TemplateBinding Background}" ClipToBounds="True">
                        <Grid x:Name="PART_Track">
                            <Border x:Name="PART_Indicator" CornerRadius="3" Background="{TemplateBinding Foreground}" HorizontalAlignment="Left" />
                        </Grid>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style x:Key="ProgressSegment" TargetType="ProgressBar" BasedOn="{StaticResource ProgressThin}">
        <Setter Property="Height" Value="4" />
    </Style>
</ResourceDictionary>
```

#### `windows/src/Offload.Core/Desktop/ThemeChoice.cs` (nový soubor)

```csharp
namespace Offload.Core.Desktop;

public enum AppTheme { Dark, Light }

/// Dark or light (UI návrh, rozhodnutí 2026-10-07): the Windows setting "Režim aplikací" decides; the environment variable
/// OFFLOAD_THEME (dark / light) wins — for screenshots and checks only, off by default.
public static class ThemeChoice
{
    public const string OverrideVariable = "OFFLOAD_THEME";

    /// `appsUseLightTheme`: HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme — 0 = dark,
    /// anything else or missing = light (the Windows default).
    public static AppTheme Resolve(string? overrideValue, object? appsUseLightTheme) => overrideValue?.Trim().ToLowerInvariant() switch
    {
        "dark" => AppTheme.Dark,
        "light" => AppTheme.Light,
        _ => appsUseLightTheme is int i && i == 0 ? AppTheme.Dark : AppTheme.Light,
    };

    /// True when OFFLOAD_THEME names a theme (then the whole app, also the Fluent controls, uses it).
    public static bool IsOverride(string? overrideValue) => overrideValue?.Trim().ToLowerInvariant() is "dark" or "light";
}
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ThemeTokensTests"`

Expected: PASS (`Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 upozornění, 0 chyb`; celá sada `Neúspěšné: 0` (časově citlivé `ProxyWatchdogTests` / `ProxyRendererTests.Feeder_*` při jednom selhání pusťte znovu samostatně — viz Global Constraints).

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)`.

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1):

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 9 — texty levého sloupce a dnešní historie (nová věc 7): `SetupTexts.AfterCopy` (krok 3: Ověření zpětným čtením `xxHash64` / „vypnuto“ žlutě, Analýza klipů a náhledy `3 snímky / klip`, Proxy `1080p · H.264 · LogC4 → 709` — křivka z LUTu nebo z kamery vybrané karty, Report `PDF · HTML · ASC MHL` + zapnuté seznamy klipů), `AfterCopyLine` (krok 3 během kopie na jeden řádek), `StartSummary` („1 karta → 2 disky · 1,70 GB“), `NoCard`, `Compact`, `PathPreview` („…\2026-10-07\A035_ARRI\“), `QueueEmpty` („Připraveno — A035_ARRI na 2 disky“ + věta podle počtu disků a ověření) a `Flow` (Karta → 2 disky naráz → Ověření → Proxy → Report jen z toho, co projekt dělá); `HistoryView` z `history.json`: „DNES“ („3 karty · 412 GB · vše ověřeno“, řádek „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“, „✕ s chybami (3 soubory)“, „! neověřeno“) a dny pro záložku Historie karet („Dnes“, „Včera“, „4. 10. 2026“; nejvýš 300 karet)
- Úkol 8 — štítek kamery a typ připojení (nová věc 6) a dlaždice disku: `CameraHints.Probe` při prověření karty (mimo UI vlákno, jen čtení): první klip, který není proxy kamery — video přes ffprobe s limitem 5 s (`FFProbe.Probe(…, timeout)`) a automatickým výběrem LUTu (křivka vestavěného LUTu), RAW podle formátu („Blackmagic RAW“, „RED“, „ARRIRAW“, „Canon RAW“); `CardInsight.Camera` → „ALEXA 35 · LogC4“ (`CameraLabel.Model` bez výrobce, `ShortCurve`: LogC4, S-Log3, Film Gen 5, C-Log3); `ConnectionLabel.Short` z `DriveLinks` („čtečka USB 3 · 5 Gb/s“, „USB 2.0“, „SD čtečka“, „NVMe“, „SATA“, „síť“; Thunderbolt Windows nerozliší); `DestinationTile` („7,20 TB volných z 8 TB · NTFS · NVMe“, „7,20 TB volno“, pruh obsazeno / potřeba vybraných karet, barva podle kontroly místa)
- Úkol 7 — odhad času (nová věc 10): `RateMemory` si v paměti (jen tato relace) pamatuje rychlost kopie a zpětného čtení poslední úlohy od 200 MB a čas 2. fáze na sekundu záznamu zvlášť pro „jen analýza“ a pro proxy daného kodeku, výšky a datového toku (`JobProgress.Stage2Elapsed` bez pauz); odhad startu = kopie + ověření + délka klipů (z velikosti opatrným datovým tokem jako kontrola místa) × čas na sekundu — když některá část chybí, odhad není a lišta ukáže jen velikost; „odhad ~1:30 vč. proxy“, u běžící kopie „~6:00 celkem“ (`Remaining`)
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 10 — téma (postup 1): barevné tokeny `Themes/Dark.xaml` (barvy z návrhu) a `Themes/Light.xaml` (odvozená světlá sada, stejné klíče), styly `Themes/Offload.xaml` (`StepTitle`, `TextMuted`, `TextFaint`, `TextMono`, `TileBorder`, `PillBorder`, `DashedBox`, tlačítka `PrimaryButton` / `SecondaryButton` / `GhostButton` / `LinkButton` / `DangerButton` / `SuccessButton` / `WarnButton` / `WarnSoftButton`, záložky, segmenty, `Check`, `Switch`, `InputBox`, `ProgressThin`) a čárové ikony z mockupu `Themes/Icons.xaml`; písmo Segoe UI Variable / Segoe UI a Cascadia Mono / Consolas (IBM Plex se nepřibaluje); `ThemeWatcher` vybere sadu podle „Režim aplikací“ ve Windows a přepne ji při změně (`ThemeChoice`, proměnná `OFFLOAD_THEME=dark|light` jen pro snímky); test: stejné klíče, tmavá sada = návrh, kontrast textů ≥ 4,5 : 1 v obou sadách, všechny slovníky se načtou (`XamlReader`; testy referencují WPF z .NET, ne `Offload.App`)
- Úkol 9 — texty levého sloupce a dnešní historie (nová věc 7): `SetupTexts.AfterCopy` (krok 3: Ověření zpětným čtením `xxHash64` / „vypnuto“ žlutě, Analýza klipů a náhledy `3 snímky / klip`, Proxy `1080p · H.264 · LogC4 → 709` — křivka z LUTu nebo z kamery vybrané karty, Report `PDF · HTML · ASC MHL` + zapnuté seznamy klipů), `AfterCopyLine` (krok 3 během kopie na jeden řádek), `StartSummary` („1 karta → 2 disky · 1,70 GB“), `NoCard`, `Compact`, `PathPreview` („…\2026-10-07\A035_ARRI\“), `QueueEmpty` („Připraveno — A035_ARRI na 2 disky“ + věta podle počtu disků a ověření) a `Flow` (Karta → 2 disky naráz → Ověření → Proxy → Report jen z toho, co projekt dělá); `HistoryView` z `history.json`: „DNES“ („3 karty · 412 GB · vše ověřeno“, řádek „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“, „✕ s chybami (3 soubory)“, „! neověřeno“) a dny pro záložku Historie karet („Dnes“, „Včera“, „4. 10. 2026“; nejvýš 300 karet)
- Úkol 8 — štítek kamery a typ připojení (nová věc 6) a dlaždice disku: `CameraHints.Probe` při prověření karty (mimo UI vlákno, jen čtení): první klip, který není proxy kamery — video přes ffprobe s limitem 5 s (`FFProbe.Probe(…, timeout)`) a automatickým výběrem LUTu (křivka vestavěného LUTu), RAW podle formátu („Blackmagic RAW“, „RED“, „ARRIRAW“, „Canon RAW“); `CardInsight.Camera` → „ALEXA 35 · LogC4“ (`CameraLabel.Model` bez výrobce, `ShortCurve`: LogC4, S-Log3, Film Gen 5, C-Log3); `ConnectionLabel.Short` z `DriveLinks` („čtečka USB 3 · 5 Gb/s“, „USB 2.0“, „SD čtečka“, „NVMe“, „SATA“, „síť“; Thunderbolt Windows nerozliší); `DestinationTile` („7,20 TB volných z 8 TB · NTFS · NVMe“, „7,20 TB volno“, pruh obsazeno / potřeba vybraných karet, barva podle kontroly místa)
- Úkol 7 — odhad času (nová věc 10): `RateMemory` si v paměti (jen tato relace) pamatuje rychlost kopie a zpětného čtení poslední úlohy od 200 MB a čas 2. fáze na sekundu záznamu zvlášť pro „jen analýza“ a pro proxy daného kodeku, výšky a datového toku (`JobProgress.Stage2Elapsed` bez pauz); odhad startu = kopie + ověření + délka klipů (z velikosti opatrným datovým tokem jako kontrola místa) × čas na sekundu — když některá část chybí, odhad není a lišta ukáže jen velikost; „odhad ~1:30 vč. proxy“, u běžící kopie „~6:00 celkem“ (`Remaining`)
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: theme tokens dark and light, styles, icons, follow Windows (plan 9, task 10)

Colour tokens from docs/ui-navrh as Themes/Dark.xaml and a derived Themes/Light.xaml with the same
keys, styles and line icons from the mockups, ThemeWatcher switches by the Windows app mode at run
time (OFFLOAD_THEME only for screenshots). The test checks keys, the design colours, contrast >= 4.5
and that every dictionary loads.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.App\App.xaml windows\src\Offload.App\App.xaml.cs windows\src\Offload.App\ThemeWatcher.cs windows\src\Offload.App\Themes\Dark.xaml windows\src\Offload.App\Themes\Icons.xaml windows\src\Offload.App\Themes\Light.xaml windows\src\Offload.App\Themes\Offload.xaml windows\src\Offload.Core\Desktop\ThemeChoice.cs windows\tests\Offload.Core.Tests\Offload.Core.Tests.csproj windows\tests\Offload.Core.Tests\ThemeTokensTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 11: Horní lišta místo menu (postup 2)

Menu Projekt a Nástroje nahradí horní lišta: logo, rozbalovací **Projekt** (stejné chování jako přepínač z plánu 8,
zamčený během startu — I1), vpravo odkaz na novou verzi, pilulka stavu fronty, „Ověřit zálohu“, „Hardware“ s tečkou
a tlačítko s posuvníky (do úkolu 12 rozbalí zbytek menu Nástroje, aby nic nezmizelo). Okno 1360 × 900. Hlášky
doplnění odkazovaly na zrušené menu — nově `Msg.VerifyHint`. Přepínač „Projekt:“ zmizí z levého panelu.

**Files:**
- Create: `windows/src/Offload.App/MainViewModel.TopBar.cs`
- Create: `windows/src/Offload.Core/Desktop/TopBar.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.App/App.xaml`
- Modify: `windows/src/Offload.App/Converters.cs`
- Modify: `windows/src/Offload.App/JobItemViewModel.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Hardware.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Projects.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs`
- Modify: `windows/src/Offload.App/MainWindow.xaml`
- Modify: `windows/src/Offload.App/QueueItem.cs`
- Modify: `windows/src/Offload.App/SetupPanel.xaml`
- Modify: `windows/src/Offload.Core/Messages.cs`
- Test: `windows/tests/Offload.Core.Tests/AppendEngineTests.cs`
- Test: `windows/tests/Offload.Core.Tests/AppendPlannerTests.cs`
- Test: `windows/tests/Offload.Core.Tests/TopBarTests.cs` (nový)

**Interfaces:**
- Consumes: `QueueSummary` (úkol 6), `HwTestResult`, `Projects` (stávající), styly a ikony (úkol 10).
- Produces:
  - `Offload.Core.Desktop.HardwareDot { None, Ok, Warning }`, `TopBar.Dot(HwTestResult?)`, `TopBar.ProjectLabel(string?)`; `Msg.VerifyHint`.
  - `MainViewModel`: `ProjectItems` (`IReadOnlyList<ProjectItem>`), `record ProjectItem(string Name, bool IsActive, RelayCommand Select)`, `ProjectLabel`, `ProjectMenuOpen`, `MoreMenuOpen`, `QueuePill`, `QueueBadge`, `HardwareDot`, `HardwareTooltip`; privátní `RefreshQueueSummary()` (volá ji obnova fronty).
  - `QueueItem.IsPaused` (`virtual bool`), `JobItemViewModel.IsPaused`; konvertory `Not`, `BoolToHidden` (App.xaml).
  - AutomationId: `ProjectChoice`, `SaveAsProjectMenuItem`, `RenameProjectMenuItem`, `DeleteProjectMenuItem`, `NewVersionLink`, `QueuePill`, `VerifyBackupButton`, `HardwareButton`, `SettingsButton`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/AppendEngineTests.cs`

Najít (1/1):

```csharp

        Assert.Equal(JobPhase.Failed, append.Status().Phase);
        var disk = AppendPlanner.Disk(paths[0]);
        Assert.Equal($"Zálohy na cílech se liší ({disk} má 1 generaci MHL, {disk} 2) — zkontrolujte je přes Nástroje → Ověřit zálohu…; doplnit nejde.",
            append.Status().Error);
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
    }
```

Nahradit:

```csharp

        Assert.Equal(JobPhase.Failed, append.Status().Phase);
        var disk = AppendPlanner.Disk(paths[0]);
        Assert.Equal($"Zálohy na cílech se liší ({disk} má 1 generaci MHL, {disk} 2) — zkontrolujte je přes Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify); doplnit nejde.",
            append.Status().Error);
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
    }
```

#### `windows/tests/Offload.Core.Tests/AppendPlannerTests.cs`

Najít (1/4):

```csharp
        File.Delete(Layout.Join(paths[1], "CLIP/A001C002.mov"));
        var e = Refused(card, paths);
        Assert.Equal(PreflightCode.AppendMissingFile, e.Code);
        Assert.Equal($"Na {AppendPlanner.Disk(paths[1])} chybí soubor z dřívější zálohy: CLIP/A001C002.mov — zkontrolujte zálohu přes Nástroje → Ověřit zálohu…", e.Message);
    }

    static void MarkFailed(string cardPath, string rel)
```

Nahradit:

```csharp
        File.Delete(Layout.Join(paths[1], "CLIP/A001C002.mov"));
        var e = Refused(card, paths);
        Assert.Equal(PreflightCode.AppendMissingFile, e.Code);
        Assert.Equal($"Na {AppendPlanner.Disk(paths[1])} chybí soubor z dřívější zálohy: CLIP/A001C002.mov — zkontrolujte zálohu přes Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify)", e.Message);
    }

    static void MarkFailed(string cardPath, string rel)
```

Najít (2/4):

```csharp

        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        var disk = AppendPlanner.Disk(paths[0]);
        Assert.Equal($"Zálohy na cílech se liší ({disk} má 1 generaci MHL, {disk} 2) — zkontrolujte je přes Nástroje → Ověřit zálohu…; doplnit nejde.", e.Message);
    }

    // Ruling I1: an append whose reports landed but whose MHL generation did not — the next one must not reuse 0002.
```

Nahradit:

```csharp

        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        var disk = AppendPlanner.Disk(paths[0]);
        Assert.Equal($"Zálohy na cílech se liší ({disk} má 1 generaci MHL, {disk} 2) — zkontrolujte je přes Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify); doplnit nejde.", e.Message);
    }

    // Ruling I1: an append whose reports landed but whose MHL generation did not — the next one must not reuse 0002.
```

Najít (3/4):

```csharp

        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        Assert.Equal($"V {paths[0]} nesouhlasí reporty s ASC MHL (poslední report je generace 2, poslední generace MHL 1) — dřívější doplnění se nedokončilo. "
            + "Zkontrolujte zálohu přes Nástroje → Ověřit zálohu… a složku opravte ručně; doplnit nejde.", e.Message);
    }

    [Fact]
```

Nahradit:

```csharp

        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        Assert.Equal($"V {paths[0]} nesouhlasí reporty s ASC MHL (poslední report je generace 2, poslední generace MHL 1) — dřívější doplnění se nedokončilo. "
            + "Zkontrolujte zálohu přes Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify) a složku opravte ručně; doplnit nejde.", e.Message);
    }

    [Fact]
```

Najít (4/4):

```csharp
        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.AppendMhlDamaged, e.Code);
        Assert.Equal($"Historie ASC MHL v {paths[1]} je poškozená — zkontrolujte ji přes Nástroje → Ověřit zálohu…; doplnit nejde.", e.Message);

        File.WriteAllText(gen1, File.ReadAllText(gen1)[..^1]);
        Assert.Single(AppendPlanner.Plan(card, paths).NewFiles);   // repaired: fine again
```

Nahradit:

```csharp
        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.AppendMhlDamaged, e.Code);
        Assert.Equal($"Historie ASC MHL v {paths[1]} je poškozená — zkontrolujte ji přes Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify); doplnit nejde.", e.Message);

        File.WriteAllText(gen1, File.ReadAllText(gen1)[..^1]);
        Assert.Single(AppendPlanner.Plan(card, paths).NewFiles);   // repaired: fine again
```

#### `windows/tests/Offload.Core.Tests/TopBarTests.cs` (nový soubor)

```csharp
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

/// The top bar (UI návrh): the Hardware dot and the project switcher's label.
public class TopBarTests
{
    static HwTestResult Result(params AdviceKind[] advice) =>
        new(DateTime.UtcNow, [], [], [], advice.Select(k => new Advice(k, "x")).ToList());

    [Fact]
    public void HardwareDot_NoneOkWarning()
    {
        Assert.Equal(HardwareDot.None, TopBar.Dot(null));
        Assert.Equal(HardwareDot.Ok, TopBar.Dot(Result()));
        Assert.Equal(HardwareDot.Ok, TopBar.Dot(Result(AdviceKind.Tip, AdviceKind.Info)));
        Assert.Equal(HardwareDot.Warning, TopBar.Dot(Result(AdviceKind.Info, AdviceKind.Warning)));
    }

    [Fact]
    public void ProjectLabel_NameOrBezProjektu()
    {
        Assert.Equal("Film X", TopBar.ProjectLabel("Film X"));
        Assert.Equal("Bez projektu", TopBar.ProjectLabel(null));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~TopBarTests|FullyQualifiedName~AppendPlannerTests|FullyQualifiedName~AppendEngineTests"`

Expected: FAIL — build selže — `CS0103: Název TopBar v aktuálním kontextu neexistuje` (testy doplnění pak čekají nový text `Msg.VerifyHint`).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.App/App.xaml`

Najít (1/1):

```xml
        <local:BoolToCollapsedConverter x:Key="BoolToCollapsed" />
        <local:NullToCollapsedConverter x:Key="NullToCollapsed" />
        <local:EmptyToVisibleConverter x:Key="EmptyToVisible" />
        <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
        <Style x:Key="SectionTitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="15" />
```

Nahradit:

```xml
        <local:BoolToCollapsedConverter x:Key="BoolToCollapsed" />
        <local:NullToCollapsedConverter x:Key="NullToCollapsed" />
        <local:EmptyToVisibleConverter x:Key="EmptyToVisible" />
        <local:NotConverter x:Key="Not" />
        <local:BoolToHiddenConverter x:Key="BoolToHidden" />
        <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
        <Style x:Key="SectionTitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="15" />
```

#### `windows/src/Offload.App/Converters.cs`

Najít (1/1):

```csharp
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
```

Nahradit:

```csharp
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// true → Visible, false → Hidden (keeps its place: the tick column of the project list).
public sealed class BoolToHiddenConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Visible : Visibility.Hidden;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
```

#### `windows/src/Offload.App/JobItemViewModel.cs`

Najít (1/1):

```csharp
    internal IEnumerable<VolumeDemand> SpaceDemands(long writtenAtProbe) =>
        space.Select(d => d with { Need = SpaceCheck.Remaining(d.Need, Phase, writtenAtProbe) });
    public bool IsRunning => progress.IsRunning;
    public override bool IsFinished => Phase.IsFinished();
    public bool SafeToEject => progress.Status.SafeToEject;
    public bool? CopySucceeded => progress.Status.CopySucceeded;
```

Nahradit:

```csharp
    internal IEnumerable<VolumeDemand> SpaceDemands(long writtenAtProbe) =>
        space.Select(d => d with { Need = SpaceCheck.Remaining(d.Need, Phase, writtenAtProbe) });
    public bool IsRunning => progress.IsRunning;
    public override bool IsPaused => progress.Status.Paused;
    public override bool IsFinished => Phase.IsFinished();
    public bool SafeToEject => progress.Status.SafeToEject;
    public bool? CopySucceeded => progress.Status.CopySucceeded;
```

#### `windows/src/Offload.App/MainViewModel.Hardware.cs`

Najít (1/1):

```csharp
    internal void HardwareTestFinished(HwTestResult result)
    {
        lastHwTest = result;
        Raise(nameof(HardwareSummary));
        // A quick test never replaces a saved measurement on disk (only the summary shows it).
        bool measured = result.Measurements.Count > 0;
        if (!measured && savedMeasured) return;
```

Nahradit:

```csharp
    internal void HardwareTestFinished(HwTestResult result)
    {
        lastHwTest = result;
        RaiseMany(nameof(HardwareSummary), nameof(HardwareDot), nameof(HardwareTooltip));
        // A quick test never replaces a saved measurement on disk (only the summary shows it).
        bool measured = result.Measurements.Count > 0;
        if (!measured && savedMeasured) return;
```

#### `windows/src/Offload.App/MainViewModel.Projects.cs`

Najít (1/1):

```csharp
    {
        IReadOnlyList<string> now = [Projects.NoProject, .. AllSettings.Projects.Select(p => p.Name)];
        if (!now.SequenceEqual(projectChoices, StringComparer.Ordinal)) projectChoices = now;
    }

    public string ActiveProjectChoice
```

Nahradit:

```csharp
    {
        IReadOnlyList<string> now = [Projects.NoProject, .. AllSettings.Projects.Select(p => p.Name)];
        if (!now.SequenceEqual(projectChoices, StringComparer.Ordinal)) projectChoices = now;
        RebuildProjectItems();   // the top bar's switcher (the active one may have changed too)
    }

    public string ActiveProjectChoice
```

#### `windows/src/Offload.App/MainViewModel.Queue.cs`

Najít (1/2):

```csharp
    {
        if (!CanStart) return;
        starting = true;
        RaiseMany(nameof(CanStart), nameof(CanSwitchProject));
        // Ruling I1: every check below is made for the profile shown now. The project switcher and the Projekt menu are
        // off until Start ends; should another profile be shown anyway, Start stops — its checks were not for that one.
```

Nahradit:

```csharp
    {
        if (!CanStart) return;
        starting = true;
        ProjectMenuOpen = false;
        RaiseMany(nameof(CanStart), nameof(CanSwitchProject));
        // Ruling I1: every check below is made for the profile shown now. The project switcher and the Projekt menu are
        // off until Start ends; should another profile be shown anyway, Start stops — its checks were not for that one.
```

Najít (2/2):

```csharp
        foreach (var j in Jobs)
            if (!j.IsFinished) j.Refresh();
        RefreshSpace();   // memory only: a running job needs less as it copies (against its baseline)
        // A queued job cancelled behind a running copy is finished before the queue reports it.
        if (HasFinished != shownHasFinished)
        {
```

Nahradit:

```csharp
        foreach (var j in Jobs)
            if (!j.IsFinished) j.Refresh();
        RefreshSpace();   // memory only: a running job needs less as it copies (against its baseline)
        RefreshQueueSummary();   // the top bar's pill and the queue tab's number
        // A queued job cancelled behind a running copy is finished before the queue reports it.
        if (HasFinished != shownHasFinished)
        {
```

#### `windows/src/Offload.App/MainViewModel.TopBar.cs` (nový soubor)

```csharp
using Offload.Core.Desktop;

namespace Offload.App;

/// One project in the switcher's list: its name, whether it is shown now, and the command that switches to it.
public sealed record ProjectItem(string Name, bool IsActive, RelayCommand Select);

/// The top bar (UI návrh): the project switcher in the place of the preset switcher, the queue pill, the Hardware dot.
public sealed partial class MainViewModel
{
    bool projectMenuOpen, moreMenuOpen;
    string? queuePill;
    int queueBadge;
    IReadOnlyList<ProjectItem> projectItems = [];

    /// "Bez projektu" first, then the projects in the order they were saved; the active one ticked.
    public IReadOnlyList<ProjectItem> ProjectItems => projectItems;

    /// What the switcher shows after "Projekt".
    public string ProjectLabel => TopBar.ProjectLabel(ActiveProjectName);

    public bool ProjectMenuOpen
    {
        get => projectMenuOpen;
        set => Set(ref projectMenuOpen, value && CanSwitchProject);
    }

    /// The switcher's list changed (a project saved, renamed, deleted, or another one shown).
    void RebuildProjectItems()
    {
        projectItems = ProjectChoices.Select(name => new ProjectItem(name, name == ActiveProjectChoice, new RelayCommand(() =>
        {
            ProjectMenuOpen = false;
            ActiveProjectChoice = name;
        }, () => CanSwitchProject))).ToList();
        RaiseMany(nameof(ProjectItems), nameof(ProjectLabel));
    }

    /// The menu behind the last top-bar button.
    public bool MoreMenuOpen
    {
        get => moreMenuOpen;
        set => Set(ref moreMenuOpen, value);
    }

    /// "Běží 1 offload · 1 ve frontě" — null hides the pill.
    public string? QueuePill => queuePill;

    /// The unfinished jobs, next to the "Fronta" tab (0 = no number).
    public int QueueBadge => queueBadge;

    /// Green / yellow / none after the last USB check.
    public HardwareDot HardwareDot => TopBar.Dot(lastHwTest);

    public string HardwareTooltip => $"Prověřit USB porty… (Ctrl+U) — {HardwareSummary}";

    /// Every tick: the pill and the tab's number follow the queue (raised only when they changed).
    void RefreshQueueSummary()
    {
        var (running, paused, waiting) = QueueSummary.Count(Jobs.Select(j => (j.Phase, j.IsPaused)));
        var pill = QueueSummary.Pill(running, paused, waiting);
        int badge = running + paused + waiting;
        if (pill == queuePill && badge == queueBadge) return;
        queuePill = pill;
        queueBadge = badge;
        RaiseMany(nameof(QueuePill), nameof(QueueBadge));
    }
}
```

#### `windows/src/Offload.App/MainWindow.xaml` (celý soubor nahradit)

```xml
<Window x:Class="Offload.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Offload.App"
        Title="{Binding WindowTitle}" Width="1360" Height="900" MinWidth="940" MinHeight="640"
        WindowStartupLocation="CenterScreen" Background="{DynamicResource Bg}" Foreground="{DynamicResource Text}"
        FontFamily="{StaticResource UiFont}" FontSize="14"
        Icon="pack://application:,,,/Offload.ico">
    <Window.InputBindings>
        <KeyBinding Gesture="Ctrl+Enter" Command="{Binding StartCommand}" />
        <KeyBinding Gesture="Ctrl+U" Command="{Binding OpenHardwareCommand}" />
        <KeyBinding Gesture="Ctrl+Shift+O" Command="{Binding VerifyBackupCommand}" />
    </Window.InputBindings>
    <DockPanel>
        <!-- Top bar (UI návrh): logo · project switcher (in the place of the preset switcher) · … · Ověřit zálohu · Hardware · more -->
        <Border DockPanel.Dock="Top" Background="{DynamicResource BgHeader}" BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="20,10">
            <DockPanel>
                <StackPanel DockPanel.Dock="Left" Orientation="Horizontal" VerticalAlignment="Center">
                    <Border Width="28" Height="28" CornerRadius="7" Background="{DynamicResource Accent}">
                        <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconLogo}" Tag="2.2" Foreground="{DynamicResource OnAccent}" />
                    </Border>
                    <TextBlock Text="Offload" FontSize="15" FontWeight="SemiBold" Margin="10,0,0,0" VerticalAlignment="Center" />
                    <Border Width="1" Height="22" Background="{DynamicResource Line}" Margin="12,0" />
                    <!-- Projekt ▾ — off while Start checks and asks (ruling I1, CanSwitchProject). -->
                    <Grid>
                        <ToggleButton x:Name="ProjectButton" Style="{StaticResource SecondaryButton}" IsChecked="{Binding ProjectMenuOpen}"
                                      IsEnabled="{Binding CanSwitchProject}"
                                      IsHitTestVisible="{Binding IsOpen, ElementName=ProjectPopup, Converter={StaticResource Not}}"
                                      ToolTip="Projekt — pojmenované nastavení (cíle, kontrola, výstupy, proxy, oznámení)"
                                      AutomationProperties.AutomationId="ProjectChoice" AutomationProperties.Name="Projekt">
                            <StackPanel Orientation="Horizontal">
                                <TextBlock Text="Projekt" Foreground="{DynamicResource Text3}" Margin="0,0,8,0" />
                                <TextBlock Text="{Binding ProjectLabel}" FontWeight="Medium" MaxWidth="320" TextTrimming="CharacterEllipsis" />
                                <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconChevronDown}" Width="14" Height="14" Tag="2"
                                                Foreground="{DynamicResource Text2}" Margin="8,0,0,0" />
                            </StackPanel>
                        </ToggleButton>
                        <Popup x:Name="ProjectPopup" IsOpen="{Binding ProjectMenuOpen}" PlacementTarget="{Binding ElementName=ProjectButton}" Placement="Bottom"
                               StaysOpen="False" AllowsTransparency="True" VerticalOffset="6">
                            <Border Background="{DynamicResource Raised}" BorderBrush="{DynamicResource LineStrong}" BorderThickness="1" CornerRadius="10"
                                    Padding="6" MinWidth="260">
                                <StackPanel>
                                    <ItemsControl ItemsSource="{Binding ProjectItems}">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <Button Style="{StaticResource GhostButton}" Command="{Binding Select}" HorizontalContentAlignment="Left"
                                                        Foreground="{DynamicResource Text}" AutomationProperties.Name="{Binding Name}">
                                                    <StackPanel Orientation="Horizontal">
                                                        <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconCheck}" Width="14" Height="14" Tag="2.4"
                                                                        Foreground="{DynamicResource AccentText}" Margin="0,0,8,0"
                                                                        Visibility="{Binding IsActive, Converter={StaticResource BoolToHidden}}" />
                                                        <TextBlock Text="{Binding Name}" />
                                                    </StackPanel>
                                                </Button>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                    <Border Height="1" Background="{DynamicResource LineSoft}" Margin="4,6" />
                                    <Button Style="{StaticResource GhostButton}" Content="Uložit jako nový projekt…" Command="{Binding SaveAsProjectCommand}"
                                            HorizontalContentAlignment="Left" AutomationProperties.AutomationId="SaveAsProjectMenuItem" />
                                    <Button Style="{StaticResource GhostButton}" Content="Přejmenovat…" Command="{Binding RenameProjectCommand}"
                                            HorizontalContentAlignment="Left" AutomationProperties.AutomationId="RenameProjectMenuItem" />
                                    <Button Style="{StaticResource GhostButton}" Content="Smazat…" Command="{Binding DeleteProjectCommand}"
                                            HorizontalContentAlignment="Left" AutomationProperties.AutomationId="DeleteProjectMenuItem" />
                                </StackPanel>
                            </Border>
                        </Popup>
                    </Grid>
                </StackPanel>
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Center">
                    <!-- Spec 4.4: a quiet link when the manifest names a newer version. -->
                    <TextBlock VerticalAlignment="Center" Margin="0,0,12,0" FontSize="13"
                               Visibility="{Binding NewVersionText, Converter={StaticResource NullToCollapsed}}">
                        <Hyperlink Command="{Binding UpdatesCommand}" Foreground="{DynamicResource AccentText}" AutomationProperties.AutomationId="NewVersionLink">
                            <Run Text="{Binding NewVersionText, Mode=OneWay}" />
                        </Hyperlink>
                    </TextBlock>
                    <!-- ● Běží 1 offload · 1 ve frontě -->
                    <Border CornerRadius="8" Background="{DynamicResource AccentSoft2}" Height="34" Padding="12,0" Margin="0,0,8,0"
                            Visibility="{Binding QueuePill, Converter={StaticResource NullToCollapsed}}">
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                            <Ellipse Width="7" Height="7" Fill="{DynamicResource AccentBar}" Margin="0,0,8,0" />
                            <TextBlock Text="{Binding QueuePill}" Foreground="{DynamicResource PillRunText}" FontSize="13" FontWeight="Medium"
                                       AutomationProperties.AutomationId="QueuePill" />
                        </StackPanel>
                    </Border>
                    <Button Style="{StaticResource GhostButton}" Command="{Binding VerifyBackupCommand}" ToolTip="Ověřit zálohu… (Ctrl+Shift+O)"
                            AutomationProperties.AutomationId="VerifyBackupButton">
                        <StackPanel Orientation="Horizontal">
                            <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconShield}" Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" Margin="0,0,8,0" />
                            <TextBlock Text="Ověřit zálohu" />
                        </StackPanel>
                    </Button>
                    <Button Style="{StaticResource GhostButton}" Command="{Binding OpenHardwareCommand}" ToolTip="{Binding HardwareTooltip}"
                            AutomationProperties.AutomationId="HardwareButton">
                        <StackPanel Orientation="Horizontal">
                            <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconChip}" Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" Margin="0,0,8,0" />
                            <TextBlock Text="Hardware" />
                            <Ellipse Width="7" Height="7" Margin="8,0,0,0" VerticalAlignment="Center">
                                <Ellipse.Style>
                                    <Style TargetType="Ellipse">
                                        <Setter Property="Fill" Value="{DynamicResource SuccessFill}" />
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding HardwareDot}" Value="Warning">
                                                <Setter Property="Fill" Value="{DynamicResource WarnFill}" />
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding HardwareDot}" Value="None">
                                                <Setter Property="Visibility" Value="Collapsed" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Ellipse.Style>
                            </Ellipse>
                        </StackPanel>
                    </Button>
                    <!-- More: the former Nástroje items until the settings page has them (next task). -->
                    <Grid>
                        <ToggleButton x:Name="MoreButton" Style="{StaticResource IconButton}" IsChecked="{Binding MoreMenuOpen}" ToolTip="Další"
                                      IsHitTestVisible="{Binding IsOpen, ElementName=MorePopup, Converter={StaticResource Not}}"
                                      AutomationProperties.AutomationId="SettingsButton" AutomationProperties.Name="Nastavení">
                            <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconSliders}" Width="17" Height="17"
                                            Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=ToggleButton}}" />
                        </ToggleButton>
                        <Popup x:Name="MorePopup" IsOpen="{Binding MoreMenuOpen}" PlacementTarget="{Binding ElementName=MoreButton}" Placement="Bottom"
                               HorizontalOffset="-220" StaysOpen="False" AllowsTransparency="True" VerticalOffset="6">
                            <Border Background="{DynamicResource Raised}" BorderBrush="{DynamicResource LineStrong}" BorderThickness="1" CornerRadius="10" Padding="6" MinWidth="250">
                                <StackPanel>
                                    <Button Style="{StaticResource GhostButton}" Content="Denní report…" Command="{Binding DayReportCommand}" HorizontalContentAlignment="Left"
                                            AutomationProperties.AutomationId="DayReportMenuItem" />
                                    <Button Style="{StaticResource GhostButton}" Content="Oznámení…" Command="{Binding NotifySettingsCommand}" HorizontalContentAlignment="Left"
                                            AutomationProperties.AutomationId="NotifyMenuItem" />
                                    <Button Style="{StaticResource GhostButton}" Content="Aktualizace…" Command="{Binding UpdatesCommand}" HorizontalContentAlignment="Left"
                                            AutomationProperties.AutomationId="UpdatesMenuItem" />
                                    <CheckBox Style="{StaticResource Check}" Content="Ukládat report ověření" IsChecked="{Binding SaveVerifyReport}" Margin="12,8"
                                              FontSize="13" AutomationProperties.AutomationId="SaveVerifyReportMenuItem" />
                                </StackPanel>
                            </Border>
                        </Popup>
                    </Grid>
                </StackPanel>
                <Border />
            </DockPanel>
        </Border>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="400" MinWidth="360" MaxWidth="480" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" MinWidth="520" />
            </Grid.ColumnDefinitions>
            <local:SetupPanel Grid.Column="0" />
            <GridSplitter Grid.Column="1" Width="1" HorizontalAlignment="Stretch" Background="{DynamicResource Line}" />
            <local:QueuePanel Grid.Column="2" />
        </Grid>
    </DockPanel>
</Window>
```

#### `windows/src/Offload.App/QueueItem.cs`

Najít (1/1):

```csharp
    /// As a copy phase — a running verification counts as Verifying (no speed measurement meanwhile).
    public abstract JobPhase Phase { get; }
    public abstract bool IsFinished { get; }
    /// Failed or with problems: the error sound and flash.
    public abstract bool IsError { get; }
    /// Still needs the app when closing (not finished, or an eject in flight).
```

Nahradit:

```csharp
    /// As a copy phase — a running verification counts as Verifying (no speed measurement meanwhile).
    public abstract JobPhase Phase { get; }
    public abstract bool IsFinished { get; }
    /// Pozastaveno (the top bar's pill counts it apart); only an offload can be paused.
    public virtual bool IsPaused => false;
    /// Failed or with problems: the error sound and flash.
    public abstract bool IsError { get; }
    /// Still needs the app when closing (not finished, or an eject in flight).
```

#### `windows/src/Offload.App/SetupPanel.xaml`

Najít (1/1):

```xml
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <StackPanel x:Name="Sections" Margin="18">

                <!-- Projekt (spec 4.1); off while Start checks and asks (ruling I1) -->
                <DockPanel Margin="0,0,0,14">
                    <TextBlock Text="Projekt:" Style="{StaticResource Caption}" VerticalAlignment="Center" Margin="0,0,8,0" />
                    <ComboBox ItemsSource="{Binding ProjectChoices}" SelectedItem="{Binding ActiveProjectChoice}"
                              IsEnabled="{Binding CanSwitchProject}"
                              ToolTip="Pojmenované nastavení (cíle, kontrola, výstupy, proxy, oznámení) — menu Projekt"
                              AutomationProperties.AutomationId="ProjectChoice" AutomationProperties.Name="Projekt" />
                </DockPanel>

                <!-- Karty -->
                <DockPanel>
                    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
```

Nahradit:

```xml
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <StackPanel x:Name="Sections" Margin="18">

                <!-- Karty -->
                <DockPanel>
                    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
```

#### `windows/src/Offload.Core/Desktop/TopBar.cs` (nový soubor)

```csharp
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

/// The dot of the Hardware button (UI návrh, horní lišta): green = the last USB check found nothing to fix, yellow = it
/// has advice (a warning); none before the first check.
public enum HardwareDot { None, Ok, Warning }

/// The top bar (UI návrh): the project switcher in the place of the design's preset switcher, the Hardware dot.
public static class TopBar
{
    public static HardwareDot Dot(HwTestResult? last) =>
        last == null ? HardwareDot.None : last.Advice.Any(a => a.Kind == AdviceKind.Warning) ? HardwareDot.Warning : HardwareDot.Ok;

    /// What the switcher shows after "Projekt": the active project, or "Bez projektu".
    public static string ProjectLabel(string? activeProject) => activeProject ?? Projects.NoProject;
}
```

#### `windows/src/Offload.Core/Messages.cs`

Najít (1/3):

```csharp
    public static string CliAppendProject(string name) => $"Nastavení z projektu: {name} (projekt zálohy zůstane podle dřívějšího reportu)";
    public static string CardUnreadable(string detail) => $"Kartu nejde přečíst: {detail}";

    // Append (spec 2.2)
    public static string AppendUnavailable(string cardPath) =>
        $"Dřívější záloha není dostupná: {cardPath} — připojte disk, nebo udělejte celou kopii.";
```

Nahradit:

```csharp
    public static string CliAppendProject(string name) => $"Nastavení z projektu: {name} (projekt zálohy zůstane podle dřívějšího reportu)";
    public static string CardUnreadable(string detail) => $"Kartu nejde přečíst: {detail}";

    /// Where a backup is checked: the top bar of the window (since the new look, UI návrh) or the CLI.
    public const string VerifyHint = "Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify)";

    // Append (spec 2.2)
    public static string AppendUnavailable(string cardPath) =>
        $"Dřívější záloha není dostupná: {cardPath} — připojte disk, nebo udělejte celou kopii.";
```

Najít (2/3):

```csharp
    public static string AppendNoMhl(string cardPath) =>
        $"V {cardPath} chybí ASC MHL (ascmhl\\ascmhl_chain.xml) — doplnit nejde.";
    public static string AppendDiverged(string details) =>
        $"Zálohy na cílech se liší ({details}) — zkontrolujte je přes Nástroje → Ověřit zálohu…; doplnit nejde.";
    /// Ruling I1: the numbered reports and the ASC MHL chain of one card folder disagree (an append stopped half way).
    public static string AppendReportsMismatch(string cardPath, int report, int mhl) =>
        $"V {cardPath} nesouhlasí reporty s ASC MHL (poslední report je generace {report}, poslední generace MHL {mhl}) — dřívější doplnění se nedokončilo. "
        + "Zkontrolujte zálohu přes Nástroje → Ověřit zálohu… a složku opravte ručně; doplnit nejde.";
    public static string AppendNotACardFolder(string path) =>
        $"{path} není složka karty v záloze (<cíl>\\<datum>\\<karta>) — doplnit nejde.";
    public static string AppendDifferentFolders(IEnumerable<string> cardPaths) =>
```

Nahradit:

```csharp
    public static string AppendNoMhl(string cardPath) =>
        $"V {cardPath} chybí ASC MHL (ascmhl\\ascmhl_chain.xml) — doplnit nejde.";
    public static string AppendDiverged(string details) =>
        $"Zálohy na cílech se liší ({details}) — zkontrolujte je přes {VerifyHint}; doplnit nejde.";
    /// Ruling I1: the numbered reports and the ASC MHL chain of one card folder disagree (an append stopped half way).
    public static string AppendReportsMismatch(string cardPath, int report, int mhl) =>
        $"V {cardPath} nesouhlasí reporty s ASC MHL (poslední report je generace {report}, poslední generace MHL {mhl}) — dřívější doplnění se nedokončilo. "
        + $"Zkontrolujte zálohu přes {VerifyHint} a složku opravte ručně; doplnit nejde.";
    public static string AppendNotACardFolder(string path) =>
        $"{path} není složka karty v záloze (<cíl>\\<datum>\\<karta>) — doplnit nejde.";
    public static string AppendDifferentFolders(IEnumerable<string> cardPaths) =>
```

Najít (3/3):

```csharp
        + string.Join("\n  ", lines.Take(10)) + (lines.Count > 10 ? "\n  …" : "")
        + $"\nUdělejte celou kopii (vznikne {fullCopyFolder}).";
    public static string AppendMissingFile(string disk, string path) =>
        $"Na {disk} chybí soubor z dřívější zálohy: {path} — zkontrolujte zálohu přes Nástroje → Ověřit zálohu…";
    public static string AppendFailedFile(string path, string disk) =>
        $"{path} se při dřívější záloze na {disk} nezkopíroval správně — doplnit nejde, udělejte celou kopii.";
    public const string AppendOtherCard = "Na kartě není žádný soubor z dřívější zálohy — jde o jinou kartu? Udělejte celou kopii.";
    public static string AppendMhlDamaged(string cardPath) =>
        $"Historie ASC MHL v {cardPath} je poškozená — zkontrolujte ji přes Nástroje → Ověřit zálohu…; doplnit nejde.";
    /// One line of the AppendConflict list: a file on a destination that no report records (a crashed append leaves one).
    public static string AppendUnknownFile(string disk, string path) =>
        $"na {disk} je soubor {path}, který žádný report nezná (nedokončené doplnění?)";
```

Nahradit:

```csharp
        + string.Join("\n  ", lines.Take(10)) + (lines.Count > 10 ? "\n  …" : "")
        + $"\nUdělejte celou kopii (vznikne {fullCopyFolder}).";
    public static string AppendMissingFile(string disk, string path) =>
        $"Na {disk} chybí soubor z dřívější zálohy: {path} — zkontrolujte zálohu přes {VerifyHint}";
    public static string AppendFailedFile(string path, string disk) =>
        $"{path} se při dřívější záloze na {disk} nezkopíroval správně — doplnit nejde, udělejte celou kopii.";
    public const string AppendOtherCard = "Na kartě není žádný soubor z dřívější zálohy — jde o jinou kartu? Udělejte celou kopii.";
    public static string AppendMhlDamaged(string cardPath) =>
        $"Historie ASC MHL v {cardPath} je poškozená — zkontrolujte ji přes {VerifyHint}; doplnit nejde.";
    /// One line of the AppendConflict list: a file on a destination that no report records (a crashed append leaves one).
    public static string AppendUnknownFile(string disk, string path) =>
        $"na {disk} je soubor {path}, který žádný report nezná (nedokončené doplnění?)";
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~TopBarTests|FullyQualifiedName~AppendPlannerTests|FullyQualifiedName~AppendEngineTests"`

Expected: PASS (`Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 upozornění, 0 chyb`; celá sada `Neúspěšné: 0` (časově citlivé `ProxyWatchdogTests` / `ProxyRendererTests.Feeder_*` při jednom selhání pusťte znovu samostatně — viz Global Constraints).

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)`.

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1):

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 10 — téma (postup 1): barevné tokeny `Themes/Dark.xaml` (barvy z návrhu) a `Themes/Light.xaml` (odvozená světlá sada, stejné klíče), styly `Themes/Offload.xaml` (`StepTitle`, `TextMuted`, `TextFaint`, `TextMono`, `TileBorder`, `PillBorder`, `DashedBox`, tlačítka `PrimaryButton` / `SecondaryButton` / `GhostButton` / `LinkButton` / `DangerButton` / `SuccessButton` / `WarnButton` / `WarnSoftButton`, záložky, segmenty, `Check`, `Switch`, `InputBox`, `ProgressThin`) a čárové ikony z mockupu `Themes/Icons.xaml`; písmo Segoe UI Variable / Segoe UI a Cascadia Mono / Consolas (IBM Plex se nepřibaluje); `ThemeWatcher` vybere sadu podle „Režim aplikací“ ve Windows a přepne ji při změně (`ThemeChoice`, proměnná `OFFLOAD_THEME=dark|light` jen pro snímky); test: stejné klíče, tmavá sada = návrh, kontrast textů ≥ 4,5 : 1 v obou sadách, všechny slovníky se načtou (`XamlReader`; testy referencují WPF z .NET, ne `Offload.App`)
- Úkol 9 — texty levého sloupce a dnešní historie (nová věc 7): `SetupTexts.AfterCopy` (krok 3: Ověření zpětným čtením `xxHash64` / „vypnuto“ žlutě, Analýza klipů a náhledy `3 snímky / klip`, Proxy `1080p · H.264 · LogC4 → 709` — křivka z LUTu nebo z kamery vybrané karty, Report `PDF · HTML · ASC MHL` + zapnuté seznamy klipů), `AfterCopyLine` (krok 3 během kopie na jeden řádek), `StartSummary` („1 karta → 2 disky · 1,70 GB“), `NoCard`, `Compact`, `PathPreview` („…\2026-10-07\A035_ARRI\“), `QueueEmpty` („Připraveno — A035_ARRI na 2 disky“ + věta podle počtu disků a ověření) a `Flow` (Karta → 2 disky naráz → Ověření → Proxy → Report jen z toho, co projekt dělá); `HistoryView` z `history.json`: „DNES“ („3 karty · 412 GB · vše ověřeno“, řádek „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“, „✕ s chybami (3 soubory)“, „! neověřeno“) a dny pro záložku Historie karet („Dnes“, „Včera“, „4. 10. 2026“; nejvýš 300 karet)
- Úkol 8 — štítek kamery a typ připojení (nová věc 6) a dlaždice disku: `CameraHints.Probe` při prověření karty (mimo UI vlákno, jen čtení): první klip, který není proxy kamery — video přes ffprobe s limitem 5 s (`FFProbe.Probe(…, timeout)`) a automatickým výběrem LUTu (křivka vestavěného LUTu), RAW podle formátu („Blackmagic RAW“, „RED“, „ARRIRAW“, „Canon RAW“); `CardInsight.Camera` → „ALEXA 35 · LogC4“ (`CameraLabel.Model` bez výrobce, `ShortCurve`: LogC4, S-Log3, Film Gen 5, C-Log3); `ConnectionLabel.Short` z `DriveLinks` („čtečka USB 3 · 5 Gb/s“, „USB 2.0“, „SD čtečka“, „NVMe“, „SATA“, „síť“; Thunderbolt Windows nerozliší); `DestinationTile` („7,20 TB volných z 8 TB · NTFS · NVMe“, „7,20 TB volno“, pruh obsazeno / potřeba vybraných karet, barva podle kontroly místa)
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 11 — horní lišta (postup 2) místo menu: logo a „Offload“, přepínač **Projekt** na místě přepínače presetu (rozbalovací seznam projektů s odškrtnutím aktivního, „Uložit jako nový projekt…“, „Přejmenovat…“, „Smazat…“; zamčený, dokud Start kontroluje — I1), vpravo odkaz „Nová verze …“, pilulka „● Běží 1 offload · 1 ve frontě“ (`QueueSummary`, pozastavené zvlášť), „Ověřit zálohu“ (Ctrl+Shift+O), „Hardware“ s tečkou (zelená = porty bez varování, žlutá = doporučení, žádná před prvním testem; `TopBar.Dot`; Ctrl+U) a tlačítko s posuvníky (zatím nabídka Denní report…, Oznámení…, Aktualizace…, Ukládat report ověření — příští úkol je přesune do Nastavení); okno 1360 × 900 (min 940 × 640 beze změny); přepínač „Projekt:“ z levého panelu je pryč
- Úkol 10 — téma (postup 1): barevné tokeny `Themes/Dark.xaml` (barvy z návrhu) a `Themes/Light.xaml` (odvozená světlá sada, stejné klíče), styly `Themes/Offload.xaml` (`StepTitle`, `TextMuted`, `TextFaint`, `TextMono`, `TileBorder`, `PillBorder`, `DashedBox`, tlačítka `PrimaryButton` / `SecondaryButton` / `GhostButton` / `LinkButton` / `DangerButton` / `SuccessButton` / `WarnButton` / `WarnSoftButton`, záložky, segmenty, `Check`, `Switch`, `InputBox`, `ProgressThin`) a čárové ikony z mockupu `Themes/Icons.xaml`; písmo Segoe UI Variable / Segoe UI a Cascadia Mono / Consolas (IBM Plex se nepřibaluje); `ThemeWatcher` vybere sadu podle „Režim aplikací“ ve Windows a přepne ji při změně (`ThemeChoice`, proměnná `OFFLOAD_THEME=dark|light` jen pro snímky); test: stejné klíče, tmavá sada = návrh, kontrast textů ≥ 4,5 : 1 v obou sadách, všechny slovníky se načtou (`XamlReader`; testy referencují WPF z .NET, ne `Offload.App`)
- Úkol 9 — texty levého sloupce a dnešní historie (nová věc 7): `SetupTexts.AfterCopy` (krok 3: Ověření zpětným čtením `xxHash64` / „vypnuto“ žlutě, Analýza klipů a náhledy `3 snímky / klip`, Proxy `1080p · H.264 · LogC4 → 709` — křivka z LUTu nebo z kamery vybrané karty, Report `PDF · HTML · ASC MHL` + zapnuté seznamy klipů), `AfterCopyLine` (krok 3 během kopie na jeden řádek), `StartSummary` („1 karta → 2 disky · 1,70 GB“), `NoCard`, `Compact`, `PathPreview` („…\2026-10-07\A035_ARRI\“), `QueueEmpty` („Připraveno — A035_ARRI na 2 disky“ + věta podle počtu disků a ověření) a `Flow` (Karta → 2 disky naráz → Ověření → Proxy → Report jen z toho, co projekt dělá); `HistoryView` z `history.json`: „DNES“ („3 karty · 412 GB · vše ověřeno“, řádek „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“, „✕ s chybami (3 soubory)“, „! neověřeno“) a dny pro záložku Historie karet („Dnes“, „Včera“, „4. 10. 2026“; nejvýš 300 karet)
- Úkol 8 — štítek kamery a typ připojení (nová věc 6) a dlaždice disku: `CameraHints.Probe` při prověření karty (mimo UI vlákno, jen čtení): první klip, který není proxy kamery — video přes ffprobe s limitem 5 s (`FFProbe.Probe(…, timeout)`) a automatickým výběrem LUTu (křivka vestavěného LUTu), RAW podle formátu („Blackmagic RAW“, „RED“, „ARRIRAW“, „Canon RAW“); `CardInsight.Camera` → „ALEXA 35 · LogC4“ (`CameraLabel.Model` bez výrobce, `ShortCurve`: LogC4, S-Log3, Film Gen 5, C-Log3); `ConnectionLabel.Short` z `DriveLinks` („čtečka USB 3 · 5 Gb/s“, „USB 2.0“, „SD čtečka“, „NVMe“, „SATA“, „síť“; Thunderbolt Windows nerozliší); `DestinationTile` („7,20 TB volných z 8 TB · NTFS · NVMe“, „7,20 TB volno“, pruh obsazeno / potřeba vybraných karet, barva podle kontroly místa)
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: top bar instead of the menus (plan 9, task 11)

Logo, the Projekt switcher (same behaviour as plan 8, locked while Start checks), the update link,
the queue pill, Ověřit zálohu, Hardware with the last test's dot and a settings button that keeps
the rest of the Nástroje menu reachable until the settings page. Append messages point to the new
Ověřit zálohu button (Msg.VerifyHint).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.App\App.xaml windows\src\Offload.App\Converters.cs windows\src\Offload.App\JobItemViewModel.cs windows\src\Offload.App\MainViewModel.Hardware.cs windows\src\Offload.App\MainViewModel.Projects.cs windows\src\Offload.App\MainViewModel.Queue.cs windows\src\Offload.App\MainViewModel.TopBar.cs windows\src\Offload.App\MainWindow.xaml windows\src\Offload.App\QueueItem.cs windows\src\Offload.App\SetupPanel.xaml windows\src\Offload.Core\Desktop\TopBar.cs windows\src\Offload.Core\Messages.cs windows\tests\Offload.Core.Tests\AppendEngineTests.cs windows\tests\Offload.Core.Tests\AppendPlannerTests.cs windows\tests\Offload.Core.Tests\TopBarTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 12: Stránka Nastavení (postup 6, předsunutý)

Volby, které levý sloupec v úkolu 13 ztratí, musí být nejdřív jinde: stránka Nastavení se otevře místo obou
sloupců (ikona s posuvníky, Ctrl+,; Esc, „‹ Zpět“ a „Hotovo“ zavřou) a má sekce Struktura složek, Ověření, Analýza
a náhledy, **Proxy** podle obr. 4 (segmenty, tok za minutu, tabulka LUTů, šedý náhled burn-inu), Report, Oznámení,
Dekodéry RAW a Aplikace. Úpravy jdou do zobrazeného profilu jako dosud.

**Files:**
- Create: `windows/src/Offload.App/MainViewModel.SettingsPage.cs`
- Create: `windows/src/Offload.App/SettingsPage.xaml`
- Create: `windows/src/Offload.App/SettingsPage.xaml.cs`
- Create: `windows/src/Offload.Core/Desktop/SettingsTexts.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.App/App.xaml`
- Modify: `windows/src/Offload.App/Converters.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Notify.cs`
- Modify: `windows/src/Offload.App/MainViewModel.TopBar.cs`
- Modify: `windows/src/Offload.App/MainViewModel.cs`
- Modify: `windows/src/Offload.App/MainWindow.xaml`
- Modify: `windows/src/Offload.App/ProxyOptionsViewModel.cs`
- Test: `windows/tests/Offload.Core.Tests/SettingsTextsTests.cs` (nový)

**Interfaces:**
- Consumes: `ProxyOptionsViewModel`, `MainViewModel` (stávající příkazy `SaveAsProjectCommand`, `NotifySettingsCommand`, `UpdatesCommand`, `VerifyBackupCommand`, `DayReportCommand`, `OpenHardwareCommand`), styly (úkol 10), `SetupTexts.CodecShort` (úkol 9).
- Produces:
  - `Offload.Core.Desktop.LutRow(string Id, string Title, string Detail, string? Marker)`, `SettingsTexts` — `BitrateHint(int mbps)`, `Row(BuiltinLut, ProxySettings)`, `NotifySummary(NotifySettings)`, `FrameText(ProxySettings)`, `Frame(ProxySettings)` → `(double X, double Y, double W, double H)?`.
  - `Offload.App.Choice(string label, Func<bool> isSelected, Action select)` (`Label`, `IsSelected`, `Changed()`).
  - `MainViewModel`: `OpenSettingsCommand`, `CloseSettingsCommand`, `IsSettingsOpen`, `SettingsSection`, `NotifySummary`, `VersionText`; `static class SettingsSections` (`Folders`, `Verify`, `Analysis`, `Proxy`, `Report`, `Notify`, `Decoders`, `App`).
  - `ProxyOptionsViewModel`: `CodecChoices`, `HeightChoices`, `BitrateHint`, `LutRows`, `HasFrame`, `FrameText`, `FrameLeft/Width/Right/Top/Height/Bottom` (`GridLength`).
  - `SettingsPage` (UserControl); konvertory `Equals`, `EqualsToVisible`, `CountToVisible`, `IsNull`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/SettingsTextsTests.cs` (nový soubor)

```csharp
using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.Core.Tests;

/// The settings page (UI návrh obr. 4): the LUT table, the bitrate hint, the burn-in frame, the notification summary.
public class SettingsTextsTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    [Fact]
    public void LutRows_MakerAndCurve_ChosenMarked()
    {
        var p = new ProxySettings { LutMode = LutMode.Builtin, LutBuiltin = "arri-logc4-awg4" };
        var rows = Cat.Luts.Select(l => SettingsTexts.Row(l, p)).ToDictionary(r => r.Id);

        Assert.Equal(("ARRI · LogC4", "LogC4 / AWG4 → Rec.709", "použito"),
            (rows["arri-logc4-awg4"].Title, rows["arri-logc4-awg4"].Detail, rows["arri-logc4-awg4"].Marker));
        Assert.Equal(("Sony · S-Log3", "S-Log3 / S-Gamut3.Cine → Rec.709", (string?)null),
            (rows["sony-slog3-sgamut3cine"].Title, rows["sony-slog3-sgamut3cine"].Detail, rows["sony-slog3-sgamut3cine"].Marker));
        Assert.Equal("Blackmagic · Film Gen 5", rows["bmd-film-gen5"].Title);
        Assert.Equal("Film Gen 5 → Rec.709", rows["bmd-film-gen5"].Detail);
        Assert.Equal("Canon · C-Log3", rows["canon-clog3-cinemagamut"].Title);
        Assert.All(Cat.Luts.Select(l => SettingsTexts.Row(l, new ProxySettings())), r => Assert.Null(r.Marker));   // automatic: none marked
    }

    [Fact]
    public void Bitrate_Frame_Notify()
    {
        Assert.Equal("≈ 75 MB / minutu", SettingsTexts.BitrateHint(10));
        Assert.Equal("≈ 15 MB / minutu", SettingsTexts.BitrateHint(2));

        Assert.Null(SettingsTexts.Frame(new ProxySettings()));
        Assert.Equal("celý obraz", SettingsTexts.FrameText(new ProxySettings()));
        var crop = new ProxySettings { FrameAspect = 2.39, FrameScale = 0.9 };
        var (x, y, w, h) = SettingsTexts.Frame(crop)!.Value;
        Assert.Equal(0.05, x, 2);
        Assert.Equal(0.9, w, 2);
        Assert.Equal(1 - 2 * y, h, 2);
        Assert.Equal("2.39 · 90 %", SettingsTexts.FrameText(crop));

        Assert.Equal("Windows: zapnuto · telefon (ntfy): vypnuto", SettingsTexts.NotifySummary(new NotifySettings()));
        Assert.Equal("Windows: vypnuto · telefon (ntfy): film-x",
            SettingsTexts.NotifySummary(new NotifySettings { Windows = false, Ntfy = new NtfySettings { Enabled = true, Topic = "film-x" } }));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~SettingsTextsTests"`

Expected: FAIL — build selže — `CS0103: Název SettingsTexts v aktuálním kontextu neexistuje`.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.App/App.xaml`

Najít (1/1):

```xml
        <local:EmptyToVisibleConverter x:Key="EmptyToVisible" />
        <local:NotConverter x:Key="Not" />
        <local:BoolToHiddenConverter x:Key="BoolToHidden" />
        <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
        <Style x:Key="SectionTitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="15" />
```

Nahradit:

```xml
        <local:EmptyToVisibleConverter x:Key="EmptyToVisible" />
        <local:NotConverter x:Key="Not" />
        <local:BoolToHiddenConverter x:Key="BoolToHidden" />
        <local:EqualsConverter x:Key="Equals" />
        <local:EqualsToVisibleConverter x:Key="EqualsToVisible" />
        <local:CountToVisibleConverter x:Key="CountToVisible" />
        <local:IsNullConverter x:Key="IsNull" />
        <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
        <Style x:Key="SectionTitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="15" />
```

#### `windows/src/Offload.App/Converters.cs`

Najít (1/1):

```csharp
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
```

Nahradit:

```csharp
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// A radio button of a choice by key (settings sections): checked when the value equals the parameter; checking it sets it.
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value?.ToString(), parameter?.ToString());
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? parameter : Binding.DoNothing;
}

/// A panel shown for one key (the settings section on screen).
public sealed class EqualsToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString()) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CountToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class IsNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
```

#### `windows/src/Offload.App/MainViewModel.Notify.cs`

Najít (1/1):

```csharp
        if (EditNotifyDialog?.Invoke(SettingsStore.Clone(Settings.Notify)) is not { } edited) return;
        Settings.Notify = edited;
        SettingsChanged();
    }

    /// End of stage 1, stage 2 still running and the card safe to eject: "kartu lze vysunout" (Windows only).
```

Nahradit:

```csharp
        if (EditNotifyDialog?.Invoke(SettingsStore.Clone(Settings.Notify)) is not { } edited) return;
        Settings.Notify = edited;
        SettingsChanged();
        Raise(nameof(NotifySummary));
    }

    /// End of stage 1, stage 2 still running and the card safe to eject: "kartu lze vysunout" (Windows only).
```

#### `windows/src/Offload.App/MainViewModel.SettingsPage.cs` (nový soubor)

```csharp
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

/// One choice of a segmented control (Kodek, Výška): selected when the setting has its value; selecting it sets it.
public sealed class Choice(string label, Func<bool> isSelected, Action select) : ObservableObject
{
    public string Label => label;

    public bool IsSelected
    {
        get => isSelected();
        set
        {
            if (value && !isSelected()) select();
        }
    }

    internal void Changed() => Raise(nameof(IsSelected));
}

/// The settings page (UI návrh obr. 4) in place of the two columns: "‹ Zpět", the project, "Uložit jako nový projekt",
/// "Hotovo"; sections on the left. Everything edits the profile shown (as the left panel did) and is saved as before.
public sealed partial class MainViewModel
{
    bool settingsOpen;
    string settingsSection = SettingsSections.Folders;

    public RelayCommand OpenSettingsCommand { get; private set; } = null!;
    public RelayCommand CloseSettingsCommand { get; private set; } = null!;

    void InitSettingsPage()
    {
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false, () => IsSettingsOpen);
    }

    public bool IsSettingsOpen
    {
        get => settingsOpen;
        set
        {
            if (!Set(ref settingsOpen, value)) return;
            ProjectMenuOpen = false;
            Raise(nameof(NotifySummary));
        }
    }

    /// The section shown: one of SettingsSections.
    public string SettingsSection
    {
        get => settingsSection;
        set => Set(ref settingsSection, value ?? SettingsSections.Folders);
    }

    /// "Windows: zapnuto · telefon (ntfy): vypnuto".
    public string NotifySummary => SettingsTexts.NotifySummary(Settings.Notify);

    /// "Offload 0.5.0-win".
    public string VersionText => $"Offload {OffloadVersion.String}";
}

/// The sections of the settings page (the keys the navigation and the panels compare with).
public static class SettingsSections
{
    public const string Folders = "folders";
    public const string Verify = "verify";
    public const string Analysis = "analysis";
    public const string Proxy = "proxy";
    public const string Report = "report";
    public const string Notify = "notify";
    public const string Decoders = "decoders";
    public const string App = "app";
}
```

#### `windows/src/Offload.App/MainViewModel.TopBar.cs`

Najít (1/2):

```csharp
/// The top bar (UI návrh): the project switcher in the place of the preset switcher, the queue pill, the Hardware dot.
public sealed partial class MainViewModel
{
    bool projectMenuOpen, moreMenuOpen;
    string? queuePill;
    int queueBadge;
    IReadOnlyList<ProjectItem> projectItems = [];
```

Nahradit:

```csharp
/// The top bar (UI návrh): the project switcher in the place of the preset switcher, the queue pill, the Hardware dot.
public sealed partial class MainViewModel
{
    bool projectMenuOpen;
    string? queuePill;
    int queueBadge;
    IReadOnlyList<ProjectItem> projectItems = [];
```

Najít (2/2):

```csharp
        RaiseMany(nameof(ProjectItems), nameof(ProjectLabel));
    }

    /// The menu behind the last top-bar button.
    public bool MoreMenuOpen
    {
        get => moreMenuOpen;
        set => Set(ref moreMenuOpen, value);
    }

    /// "Běží 1 offload · 1 ve frontě" — null hides the pill.
    public string? QueuePill => queuePill;
```

Nahradit:

```csharp
        RaiseMany(nameof(ProjectItems), nameof(ProjectLabel));
    }

    /// "Běží 1 offload · 1 ve frontě" — null hides the pill.
    public string? QueuePill => queuePill;
```

#### `windows/src/Offload.App/MainViewModel.cs`

Najít (1/1):

```csharp
        InitProjects();
        InitNotify();
        InitUpdates();
    }

    public StartupOptions Options { get; }
```

Nahradit:

```csharp
        InitProjects();
        InitNotify();
        InitUpdates();
        InitSettingsPage();
    }

    public StartupOptions Options { get; }
```

#### `windows/src/Offload.App/MainWindow.xaml`

Najít (1/2):

```xml
        <KeyBinding Gesture="Ctrl+Enter" Command="{Binding StartCommand}" />
        <KeyBinding Gesture="Ctrl+U" Command="{Binding OpenHardwareCommand}" />
        <KeyBinding Gesture="Ctrl+Shift+O" Command="{Binding VerifyBackupCommand}" />
    </Window.InputBindings>
    <DockPanel>
        <!-- Top bar (UI návrh): logo · project switcher (in the place of the preset switcher) · … · Ověřit zálohu · Hardware · more -->
        <Border DockPanel.Dock="Top" Background="{DynamicResource BgHeader}" BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="20,10">
            <DockPanel>
                <StackPanel DockPanel.Dock="Left" Orientation="Horizontal" VerticalAlignment="Center">
                    <Border Width="28" Height="28" CornerRadius="7" Background="{DynamicResource Accent}">
```

Nahradit:

```xml
        <KeyBinding Gesture="Ctrl+Enter" Command="{Binding StartCommand}" />
        <KeyBinding Gesture="Ctrl+U" Command="{Binding OpenHardwareCommand}" />
        <KeyBinding Gesture="Ctrl+Shift+O" Command="{Binding VerifyBackupCommand}" />
        <KeyBinding Gesture="Ctrl+OemComma" Command="{Binding OpenSettingsCommand}" />
        <KeyBinding Key="Escape" Command="{Binding CloseSettingsCommand}" />
    </Window.InputBindings>
    <DockPanel>
        <!-- Top bar (UI návrh): logo · project switcher (in the place of the preset switcher) · … · Ověřit zálohu · Hardware · more -->
        <Border DockPanel.Dock="Top" Background="{DynamicResource BgHeader}" BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="20,10"
                Visibility="{Binding IsSettingsOpen, Converter={StaticResource BoolToCollapsed}}">
            <DockPanel>
                <StackPanel DockPanel.Dock="Left" Orientation="Horizontal" VerticalAlignment="Center">
                    <Border Width="28" Height="28" CornerRadius="7" Background="{DynamicResource Accent}">
```

Najít (2/2):

```xml
                            </Ellipse>
                        </StackPanel>
                    </Button>
                    <!-- More: the former Nástroje items until the settings page has them (next task). -->
                    <Grid>
                        <ToggleButton x:Name="MoreButton" Style="{StaticResource IconButton}" IsChecked="{Binding MoreMenuOpen}" ToolTip="Další"
                                      IsHitTestVisible="{Binding IsOpen, ElementName=MorePopup, Converter={StaticResource Not}}"
                                      AutomationProperties.AutomationId="SettingsButton" AutomationProperties.Name="Nastavení">
                            <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconSliders}" Width="17" Height="17"
                                            Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=ToggleButton}}" />
                        </ToggleButton>
                        <Popup x:Name="MorePopup" IsOpen="{Binding MoreMenuOpen}" PlacementTarget="{Binding ElementName=MoreButton}" Placement="Bottom"
                               HorizontalOffset="-220" StaysOpen="False" AllowsTransparency="True" VerticalOffset="6">
                            <Border Background="{DynamicResource Raised}" BorderBrush="{DynamicResource LineStrong}" BorderThickness="1" CornerRadius="10" Padding="6" MinWidth="250">
                                <StackPanel>
                                    <Button Style="{StaticResource GhostButton}" Content="Denní report…" Command="{Binding DayReportCommand}" HorizontalContentAlignment="Left"
                                            AutomationProperties.AutomationId="DayReportMenuItem" />
                                    <Button Style="{StaticResource GhostButton}" Content="Oznámení…" Command="{Binding NotifySettingsCommand}" HorizontalContentAlignment="Left"
                                            AutomationProperties.AutomationId="NotifyMenuItem" />
                                    <Button Style="{StaticResource GhostButton}" Content="Aktualizace…" Command="{Binding UpdatesCommand}" HorizontalContentAlignment="Left"
                                            AutomationProperties.AutomationId="UpdatesMenuItem" />
                                    <CheckBox Style="{StaticResource Check}" Content="Ukládat report ověření" IsChecked="{Binding SaveVerifyReport}" Margin="12,8"
                                              FontSize="13" AutomationProperties.AutomationId="SaveVerifyReportMenuItem" />
                                </StackPanel>
                            </Border>
                        </Popup>
                    </Grid>
                </StackPanel>
                <Border />
            </DockPanel>
        </Border>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="400" MinWidth="360" MaxWidth="480" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" MinWidth="520" />
            </Grid.ColumnDefinitions>
            <local:SetupPanel Grid.Column="0" />
            <GridSplitter Grid.Column="1" Width="1" HorizontalAlignment="Stretch" Background="{DynamicResource Line}" />
            <local:QueuePanel Grid.Column="2" />
        </Grid>
    </DockPanel>
</Window>
```

Nahradit:

```xml
                            </Ellipse>
                        </StackPanel>
                    </Button>
                    <!-- Nastavení (Ctrl+,): the settings page in place of the two columns. -->
                    <Button Style="{StaticResource IconButton}" Command="{Binding OpenSettingsCommand}" ToolTip="Nastavení (Ctrl+,)"
                            AutomationProperties.AutomationId="SettingsButton" AutomationProperties.Name="Nastavení">
                        <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconSliders}" Width="17" Height="17"
                                        Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" />
                    </Button>
                </StackPanel>
                <Border />
            </DockPanel>
        </Border>
        <Grid>
            <local:SettingsPage Visibility="{Binding IsSettingsOpen, Converter={StaticResource BoolToVisible}}" />
            <Grid Visibility="{Binding IsSettingsOpen, Converter={StaticResource BoolToCollapsed}}">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="400" MinWidth="360" MaxWidth="480" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" MinWidth="520" />
                </Grid.ColumnDefinitions>
                <local:SetupPanel Grid.Column="0" />
                <GridSplitter Grid.Column="1" Width="5" HorizontalAlignment="Stretch" Background="{DynamicResource Bg}" />
                <local:QueuePanel Grid.Column="2" />
            </Grid>
        </Grid>
    </DockPanel>
</Window>
```

#### `windows/src/Offload.App/ProxyOptionsViewModel.cs`

Najít (1/7):

```csharp
        ScaleDownCommand = new RelayCommand(() => ScalePercent--, () => ScalePercent > 50);
        ScaleUpCommand = new RelayCommand(() => ScalePercent++, () => ScalePercent < 100);
        EditRulesCommand = new RelayCommand(() => owner.EditLutRules(catalog));
        BuildLutLists();
    }

    // The built-in LUTs are loaded off the UI thread (MainViewModel); until then only auto / none / custom are offered.
    LutCatalog catalog = new(null, []);
    IReadOnlyList<Option<string>> lutChoices = [];
```

Nahradit:

```csharp
        ScaleDownCommand = new RelayCommand(() => ScalePercent--, () => ScalePercent > 50);
        ScaleUpCommand = new RelayCommand(() => ScalePercent++, () => ScalePercent < 100);
        EditRulesCommand = new RelayCommand(() => owner.EditLutRules(catalog));
        CodecChoices = Codecs.Select(c => new Choice(SetupTexts.CodecShort(c.Value), () => p.Codec == c.Value, () => Codec = c)).ToList();
        HeightChoices = Heights.Select(h => new Choice(h.Label, () => p.Height == h.Value, () => Height = h)).ToList();
        BuildLutLists();
    }

    /// The segmented Kodek and Výška of the settings page (UI návrh obr. 4).
    public IReadOnlyList<Choice> CodecChoices { get; }
    public IReadOnlyList<Choice> HeightChoices { get; }

    /// "≈ 75 MB / minutu" next to the bitrate.
    public string BitrateHint => SettingsTexts.BitrateHint(p.BitrateMbps);

    /// "LUT podle kamery": every built-in LUT, the chosen one marked "použito".
    public IReadOnlyList<LutRow> LutRows => catalog.Luts.Select(l => SettingsTexts.Row(l, p)).ToList();

    /// The burn-in preview's frame lines (the crop of "Výřez"): shown only with a crop; star sizes of a 3 × 3 grid.
    public bool HasFrame => SettingsTexts.Frame(p) != null;
    public string FrameText => SettingsTexts.FrameText(p);
    System.Windows.GridLength Star(Func<(double X, double Y, double W, double H), double> part) =>
        new(SettingsTexts.Frame(p) is { } f ? Math.Max(part(f), 0.0001) : 1, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength FrameLeft => Star(f => f.X);
    public System.Windows.GridLength FrameWidth => Star(f => f.W);
    public System.Windows.GridLength FrameRight => Star(f => 1 - f.X - f.W);
    public System.Windows.GridLength FrameTop => Star(f => f.Y);
    public System.Windows.GridLength FrameHeight => Star(f => f.H);
    public System.Windows.GridLength FrameBottom => Star(f => 1 - f.Y - f.H);
    static readonly string[] FrameProperties =
        [nameof(HasFrame), nameof(FrameText), nameof(FrameLeft), nameof(FrameWidth), nameof(FrameRight), nameof(FrameTop), nameof(FrameHeight), nameof(FrameBottom)];

    // The built-in LUTs are loaded off the UI thread (MainViewModel); until then only auto / none / custom are offered.
    LutCatalog catalog = new(null, []);
    IReadOnlyList<Option<string>> lutChoices = [];
```

Najít (2/7):

```csharp
    {
        catalog = loaded;
        BuildLutLists();
        RaiseMany(nameof(LutChoices), nameof(FallbackChoices), nameof(LutChoice), nameof(Fallback));
    }

    LutMode Mode => LutResolver.EffectiveMode(p);
```

Nahradit:

```csharp
    {
        catalog = loaded;
        BuildLutLists();
        RaiseMany(nameof(LutChoices), nameof(FallbackChoices), nameof(LutChoice), nameof(Fallback), nameof(LutRows));
    }

    LutMode Mode => LutResolver.EffectiveMode(p);
```

Najít (3/7):

```csharp
        }
    }

    static readonly string[] LutProperties = [nameof(LutChoice), nameof(ShowFallback), nameof(ShowCustom), nameof(LutName), nameof(LutPath), nameof(ShowRules)];

    /// "Pravidla LUTů… (2)" (spec 4.2) — only with "Automaticky podle kamery": the rules apply in that mode only (R18).
    public bool ShowRules => Mode == LutMode.Auto;
```

Nahradit:

```csharp
        }
    }

    static readonly string[] LutProperties = [nameof(LutChoice), nameof(ShowFallback), nameof(ShowCustom), nameof(LutName), nameof(LutPath), nameof(ShowRules), nameof(LutRows)];

    /// "Pravidla LUTů… (2)" (spec 4.2) — only with "Automaticky podle kamery": the rules apply in that mode only (R18).
    public bool ShowRules => Mode == LutMode.Auto;
```

Najít (4/7):

```csharp
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
```

Nahradit:

```csharp
    public Option<ProxyCodec> Codec
    {
        get => Codecs.First(c => c.Value == p.Codec);
        set
        {
            if (value == null) return;
            p.Codec = value.Value;
            Changed(nameof(Codec), nameof(ShowBitrate));
            foreach (var c in CodecChoices) c.Changed();
        }
    }

    public Option<int> Height
    {
        get => Heights.FirstOrDefault(h => h.Value == p.Height) ?? Heights[2];
        set
        {
            if (value == null) return;
            p.Height = value.Value;
            Changed(nameof(Height));
            foreach (var h in HeightChoices) h.Changed();
        }
    }

    public bool ShowBitrate => p.Codec is ProxyCodec.H264 or ProxyCodec.Hevc;
```

Najít (5/7):

```csharp
    public int BitrateMbps
    {
        get => p.BitrateMbps;
        set { p.BitrateMbps = Math.Clamp(value, 2, 50); Changed(nameof(BitrateMbps), nameof(BitrateText)); }
    }

    public string BitrateText => $"{p.BitrateMbps} Mb/s";
```

Nahradit:

```csharp
    public int BitrateMbps
    {
        get => p.BitrateMbps;
        set { p.BitrateMbps = Math.Clamp(value, 2, 50); Changed(nameof(BitrateMbps), nameof(BitrateText), nameof(BitrateHint)); }
    }

    public string BitrateText => $"{p.BitrateMbps} Mb/s";
```

Najít (6/7):

```csharp
    public Option<double?> Aspect
    {
        get => Aspects.FirstOrDefault(a => a.Value == p.FrameAspect) ?? Aspects[0];
        set { if (value != null) { p.FrameAspect = value.Value; Changed(nameof(Aspect), nameof(ShowFrameScale)); } }
    }

    public bool ShowFrameScale => p.FrameAspect != null;
```

Nahradit:

```csharp
    public Option<double?> Aspect
    {
        get => Aspects.FirstOrDefault(a => a.Value == p.FrameAspect) ?? Aspects[0];
        set { if (value != null) { p.FrameAspect = value.Value; Changed([nameof(Aspect), nameof(ShowFrameScale), .. FrameProperties]); } }
    }

    public bool ShowFrameScale => p.FrameAspect != null;
```

Najít (7/7):

```csharp
    public int ScalePercent
    {
        get => (int)Math.Round(p.FrameScale * 100);
        set { p.FrameScale = Math.Clamp(value, 50, 100) / 100.0; Changed(nameof(ScalePercent), nameof(ScaleText)); }
    }

    public string ScaleText => $"{ScalePercent} % obrazu";
```

Nahradit:

```csharp
    public int ScalePercent
    {
        get => (int)Math.Round(p.FrameScale * 100);
        set { p.FrameScale = Math.Clamp(value, 50, 100) / 100.0; Changed([nameof(ScalePercent), nameof(ScaleText), .. FrameProperties]); }
    }

    public string ScaleText => $"{ScalePercent} % obrazu";
```

#### `windows/src/Offload.App/SettingsPage.xaml` (nový soubor)

```xml
<UserControl x:Class="Offload.App.SettingsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:core="clr-namespace:Offload.Core.Desktop;assembly=Offload.Core">
    <!-- Nastavení projektu (UI návrh obr. 4): every option that is not in step 3's summary. Edits the profile shown, like
         the left panel did; "Hotovo" and "‹ Zpět" only close the page (changes are saved as they are made). -->
    <UserControl.Resources>
        <Style x:Key="PageTitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="22" />
            <Setter Property="FontWeight" Value="SemiBold" />
        </Style>
        <Style x:Key="RowLabel" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource Text2}" />
            <Setter Property="VerticalAlignment" Value="Center" />
            <Setter Property="Margin" Value="0,0,18,0" />
        </Style>
        <Style x:Key="Section" TargetType="StackPanel">
            <Setter Property="MaxWidth" Value="760" />
            <Setter Property="HorizontalAlignment" Value="Left" />
        </Style>
    </UserControl.Resources>
    <DockPanel Background="{DynamicResource Bg}">
        <!-- ‹ Zpět · Projekt <name> · … · Uložit jako nový projekt · Hotovo -->
        <Border DockPanel.Dock="Top" Background="{DynamicResource BgHeader}" BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="20,10">
            <DockPanel>
                <Button DockPanel.Dock="Left" Style="{StaticResource GhostButton}" Command="{Binding CloseSettingsCommand}" Padding="10,0"
                        AutomationProperties.AutomationId="SettingsBackButton">
                    <StackPanel Orientation="Horizontal">
                        <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconBack}" Tag="2" Margin="0,0,6,0"
                                        Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" />
                        <TextBlock Text="Zpět" />
                    </StackPanel>
                </Button>
                <Border DockPanel.Dock="Left" Width="1" Height="22" Background="{DynamicResource Line}" Margin="14,0" />
                <StackPanel DockPanel.Dock="Left" VerticalAlignment="Center">
                    <TextBlock Text="Projekt" FontSize="12" Foreground="{DynamicResource Text3}" />
                    <TextBlock Text="{Binding ProjectLabel}" FontWeight="SemiBold" AutomationProperties.AutomationId="SettingsProjectName" />
                </StackPanel>
                <Button DockPanel.Dock="Right" Style="{StaticResource PrimaryButton}" Height="34" FontSize="13" Padding="16,0" Content="Hotovo"
                        Command="{Binding CloseSettingsCommand}" AutomationProperties.AutomationId="SettingsDoneButton" />
                <Button DockPanel.Dock="Right" Style="{StaticResource SecondaryButton}" Content="Uložit jako nový projekt…" Margin="0,0,8,0"
                        Command="{Binding SaveAsProjectCommand}" AutomationProperties.AutomationId="SettingsSaveAsProjectButton" />
                <Border />
            </DockPanel>
        </Border>
        <!-- Sections -->
        <Border DockPanel.Dock="Left" Width="240" Background="{DynamicResource Panel}" BorderBrush="{DynamicResource Line}" BorderThickness="0,0,1,0" Padding="14,20">
            <StackPanel>
                <RadioButton Style="{StaticResource NavButton}" Content="Struktura složek" AutomationProperties.AutomationId="NavFolders"
                             IsChecked="{Binding SettingsSection, Converter={StaticResource Equals}, ConverterParameter=folders}" />
                <RadioButton Style="{StaticResource NavButton}" Content="Ověření" AutomationProperties.AutomationId="NavVerify"
                             IsChecked="{Binding SettingsSection, Converter={StaticResource Equals}, ConverterParameter=verify}" />
                <RadioButton Style="{StaticResource NavButton}" Content="Analýza a náhledy" AutomationProperties.AutomationId="NavAnalysis"
                             IsChecked="{Binding SettingsSection, Converter={StaticResource Equals}, ConverterParameter=analysis}" />
                <RadioButton Style="{StaticResource NavButton}" Content="Proxy" AutomationProperties.AutomationId="NavProxy"
                             IsChecked="{Binding SettingsSection, Converter={StaticResource Equals}, ConverterParameter=proxy}" />
                <RadioButton Style="{StaticResource NavButton}" Content="Report" AutomationProperties.AutomationId="NavReport"
                             IsChecked="{Binding SettingsSection, Converter={StaticResource Equals}, ConverterParameter=report}" />
                <RadioButton Style="{StaticResource NavButton}" Content="Oznámení" AutomationProperties.AutomationId="NavNotify"
                             IsChecked="{Binding SettingsSection, Converter={StaticResource Equals}, ConverterParameter=notify}" />
                <RadioButton Style="{StaticResource NavButton}" Content="Dekodéry RAW" AutomationProperties.AutomationId="NavDecoders"
                             IsChecked="{Binding SettingsSection, Converter={StaticResource Equals}, ConverterParameter=decoders}" />
                <Border Height="1" Background="{DynamicResource LineSoft}" Margin="12,10" />
                <RadioButton Style="{StaticResource NavButton}" Content="Aplikace" AutomationProperties.AutomationId="NavApp"
                             IsChecked="{Binding SettingsSection, Converter={StaticResource Equals}, ConverterParameter=app}" />
            </StackPanel>
        </Border>
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <Grid Margin="32,26,32,32">

                <!-- ── Struktura složek ── -->
                <StackPanel Style="{StaticResource Section}" Visibility="{Binding SettingsSection, Converter={StaticResource EqualsToVisible}, ConverterParameter=folders}">
                    <TextBlock Text="Struktura složek" Style="{StaticResource PageTitle}" />
                    <TextBlock Text="Kam se karta na cíli zapíše: &lt;cíl&gt;\&lt;datum&gt;\&lt;název karty&gt;\." Style="{StaticResource TextMuted}" FontSize="14" Margin="0,4,0,26" />
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="120" />
                            <ColumnDefinition />
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                        </Grid.RowDefinitions>
                        <TextBlock Text="Cesta" Style="{StaticResource RowLabel}" />
                        <TextBlock Grid.Column="1" Text="{Binding PathPreview}" Style="{StaticResource TextMono}" VerticalAlignment="Center" />
                        <TextBlock Grid.Row="1" Text="Datum" Style="{StaticResource RowLabel}" Margin="0,16,18,0" />
                        <StackPanel Grid.Row="1" Grid.Column="1" Orientation="Horizontal" Margin="0,16,0,0">
                            <CheckBox Style="{StaticResource Check}" Content="Vlastní datum" IsChecked="{Binding UseCustomDate}" AutomationProperties.AutomationId="UseCustomDate" />
                            <DatePicker SelectedDate="{Binding CustomDate}" Margin="14,0,0,0" Width="150"
                                        Visibility="{Binding UseCustomDate, Converter={StaticResource BoolToVisible}}" />
                        </StackPanel>
                        <TextBlock Grid.Row="2" Text="Název karty" Style="{StaticResource RowLabel}" Margin="0,16,18,0" />
                        <CheckBox Grid.Row="2" Grid.Column="1" Style="{StaticResource Check}" Margin="0,16,0,0" Content="Navrhovat název karty podle klipů"
                                  IsChecked="{Binding SuggestCardNames}" AutomationProperties.AutomationId="SuggestCardNames" />
                    </Grid>
                </StackPanel>

                <!-- ── Ověření ── -->
                <StackPanel Style="{StaticResource Section}" Visibility="{Binding SettingsSection, Converter={StaticResource EqualsToVisible}, ConverterParameter=verify}">
                    <DockPanel>
                        <CheckBox DockPanel.Dock="Right" Style="{StaticResource Switch}" Content="Zapnuto" IsChecked="{Binding Verify}" VerticalAlignment="Top"
                                  AutomationProperties.AutomationId="VerifySwitch" />
                        <StackPanel>
                            <TextBlock Text="Ověření" Style="{StaticResource PageTitle}" />
                            <TextBlock Text="Každý soubor se po zápisu přečte z disku a porovná s kartou." Style="{StaticResource TextMuted}" FontSize="14" Margin="0,4,0,0" />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock Text="{x:Static core:SetupTexts.VerifyOffWarning}" Style="{StaticResource TextWarn}" Margin="0,12,0,0"
                               Visibility="{Binding Verify, Converter={StaticResource BoolToCollapsed}}" />
                    <Grid Margin="0,26,0,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="120" />
                            <ColumnDefinition />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="Kontrolní součet" Style="{StaticResource RowLabel}" />
                        <ComboBox Grid.Column="1" ItemsSource="{Binding HashChoices}" SelectedItem="{Binding Hash}" DisplayMemberPath="Label" HorizontalAlignment="Left"
                                  MinWidth="200" AutomationProperties.AutomationId="HashChoice" />
                    </Grid>
                    <TextBlock Text="{Binding Md5Note}" Style="{StaticResource TextWarn}" Margin="138,8,0,0" Visibility="{Binding Md5Note, Converter={StaticResource NullToCollapsed}}" />
                    <TextBlock Text="OVĚŘIT ZÁLOHU" Style="{StaticResource StepTitle}" Margin="0,30,0,10" />
                    <TextBlock Text="Zkontroluje dřívější zálohu na cíli proti ASC MHL, bez karty (Ctrl+Shift+O)." Style="{StaticResource TextMuted}" />
                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                        <Button Style="{StaticResource SecondaryButton}" Content="Ověřit zálohu…" Command="{Binding VerifyBackupCommand}" />
                        <CheckBox Style="{StaticResource Check}" Content="Ukládat report ověření" IsChecked="{Binding SaveVerifyReport}" Margin="18,0,0,0"
                                  AutomationProperties.AutomationId="SaveVerifyReport" />
                    </StackPanel>
                </StackPanel>

                <!-- ── Analýza a náhledy ── -->
                <StackPanel Style="{StaticResource Section}" Visibility="{Binding SettingsSection, Converter={StaticResource EqualsToVisible}, ConverterParameter=analysis}">
                    <DockPanel>
                        <CheckBox DockPanel.Dock="Right" Style="{StaticResource Switch}" Content="Zapnuto" IsChecked="{Binding AnalyzeClips}" VerticalAlignment="Top"
                                  AutomationProperties.AutomationId="AnalyzeSwitch" />
                        <StackPanel>
                            <TextBlock Text="Analýza a náhledy" Style="{StaticResource PageTitle}" />
                            <TextBlock Text="Po ověření se klipy prozkoumají a do reportu přijdou 3 snímky z každého klipu (začátek, střed, konec)." Style="{StaticResource TextMuted}"
                                       FontSize="14" Margin="0,4,0,0" />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock Text="{Binding ToolsWarning}" Style="{StaticResource TextWarn}" Margin="0,16,0,0"
                               Visibility="{Binding ToolsWarning, Converter={StaticResource NullToCollapsed}}" />
                </StackPanel>

                <!-- ── Proxy (obr. 4) ── -->
                <WrapPanel Visibility="{Binding SettingsSection, Converter={StaticResource EqualsToVisible}, ConverterParameter=proxy}" Orientation="Horizontal">
                    <StackPanel Width="560" Margin="0,0,32,0" DataContext="{Binding Proxy}">
                        <DockPanel>
                            <CheckBox DockPanel.Dock="Right" Style="{StaticResource Switch}" Content="Zapnuto" IsChecked="{Binding Enabled}" VerticalAlignment="Top"
                                      AutomationProperties.AutomationId="ProxySwitch" />
                            <StackPanel>
                                <TextBlock Text="Proxy" Style="{StaticResource PageTitle}" />
                                <TextBlock Text="Renderují se až po ověření — originály mají vždy přednost." Style="{StaticResource TextMuted}" FontSize="14" Margin="0,4,0,0" />
                            </StackPanel>
                        </DockPanel>
                        <Grid Margin="0,26,0,0">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="120" />
                                <ColumnDefinition />
                            </Grid.ColumnDefinitions>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                            </Grid.RowDefinitions>
                            <TextBlock Text="Kodek" Style="{StaticResource RowLabel}" />
                            <Border Grid.Column="1" Style="{StaticResource SegmentHost}">
                                <ItemsControl ItemsSource="{Binding CodecChoices}" AutomationProperties.AutomationId="CodecChoice">
                                    <ItemsControl.ItemsPanel>
                                        <ItemsPanelTemplate>
                                            <WrapPanel />
                                        </ItemsPanelTemplate>
                                    </ItemsControl.ItemsPanel>
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <RadioButton Style="{StaticResource SegmentButton}" Content="{Binding Label}" IsChecked="{Binding IsSelected}" GroupName="Codec" Margin="0,0,4,0" />
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </Border>
                            <TextBlock Grid.Row="1" Text="Výška" Style="{StaticResource RowLabel}" Margin="0,16,18,0" />
                            <Border Grid.Row="1" Grid.Column="1" Style="{StaticResource SegmentHost}" Margin="0,16,0,0">
                                <ItemsControl ItemsSource="{Binding HeightChoices}" AutomationProperties.AutomationId="HeightChoice">
                                    <ItemsControl.ItemsPanel>
                                        <ItemsPanelTemplate>
                                            <StackPanel Orientation="Horizontal" />
                                        </ItemsPanelTemplate>
                                    </ItemsControl.ItemsPanel>
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <RadioButton Style="{StaticResource SegmentButton}" Content="{Binding Label}" IsChecked="{Binding IsSelected}" GroupName="Height" Margin="0,0,4,0" />
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </Border>
                            <TextBlock Grid.Row="2" Text="Datový tok" Style="{StaticResource RowLabel}" Margin="0,16,18,0"
                                       Visibility="{Binding ShowBitrate, Converter={StaticResource BoolToVisible}}" />
                            <StackPanel Grid.Row="2" Grid.Column="1" Orientation="Horizontal" Margin="0,16,0,0" Visibility="{Binding ShowBitrate, Converter={StaticResource BoolToVisible}}">
                                <Border CornerRadius="8" BorderBrush="{DynamicResource LineInput}" BorderThickness="1" Background="{DynamicResource Input}">
                                    <StackPanel Orientation="Horizontal">
                                        <Button Style="{StaticResource IconButton}" Content="−" FontSize="16" Command="{Binding BitrateDownCommand}"
                                                AutomationProperties.Name="Snížit datový tok" />
                                        <TextBlock Text="{Binding BitrateText}" Style="{StaticResource TextMono}" Foreground="{DynamicResource Text}" Margin="10,0" VerticalAlignment="Center" />
                                        <Button Style="{StaticResource IconButton}" Content="+" FontSize="16" Command="{Binding BitrateUpCommand}"
                                                AutomationProperties.Name="Zvýšit datový tok" />
                                    </StackPanel>
                                </Border>
                                <TextBlock Text="{Binding BitrateHint}" Style="{StaticResource TextFaint}" FontSize="13" Margin="10,0,0,0" VerticalAlignment="Center" />
                            </StackPanel>
                            <TextBlock Grid.Row="3" Text="Výřez" Style="{StaticResource RowLabel}" Margin="0,16,18,0" />
                            <StackPanel Grid.Row="3" Grid.Column="1" Orientation="Horizontal" Margin="0,16,0,0">
                                <ComboBox ItemsSource="{Binding Aspects}" SelectedItem="{Binding Aspect}" DisplayMemberPath="Label" Width="130"
                                          ToolTip="Ořez na frame lines (poměr stran)" AutomationProperties.AutomationId="AspectChoice" />
                                <StackPanel Orientation="Horizontal" Margin="10,0,0,0" Visibility="{Binding ShowFrameScale, Converter={StaticResource BoolToVisible}}"
                                            ToolTip="Velikost frame lines vůči obrazu – 90 % = 10 % overscan">
                                    <Button Style="{StaticResource IconButton}" Content="−" FontSize="16" Command="{Binding ScaleDownCommand}" />
                                    <TextBlock Text="{Binding ScaleText}" Width="90" TextAlignment="Center" VerticalAlignment="Center" />
                                    <Button Style="{StaticResource IconButton}" Content="+" FontSize="16" Command="{Binding ScaleUpCommand}" />
                                </StackPanel>
                            </StackPanel>
                        </Grid>

                        <!-- LUT podle kamery -->
                        <DockPanel Margin="0,26,0,10">
                            <Button DockPanel.Dock="Right" Style="{StaticResource LinkButton}" Content="Vlastní .cube…" Command="{Binding PickLutCommand}"
                                    AutomationProperties.AutomationId="PickLutButton" />
                            <TextBlock Text="LUT PODLE KAMERY" Style="{StaticResource StepTitle}" />
                        </DockPanel>
                        <DockPanel>
                            <TextBlock Text="LUT" Style="{StaticResource RowLabel}" Width="120" Margin="0" />
                            <ComboBox ItemsSource="{Binding LutChoices}" SelectedItem="{Binding LutChoice}" DisplayMemberPath="Label" HorizontalAlignment="Left" MinWidth="260"
                                      AutomationProperties.AutomationId="LutChoice" />
                        </DockPanel>
                        <Border Style="{StaticResource ListBorder}" Margin="0,12,0,0" Visibility="{Binding LutRows.Count, Converter={StaticResource CountToVisible}}">
                            <ItemsControl ItemsSource="{Binding LutRows}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Border BorderBrush="{DynamicResource LineSoft}" BorderThickness="0,0,0,1" Padding="14,10">
                                            <Border.Style>
                                                <Style TargetType="Border">
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding Marker, Converter={StaticResource IsNull}}" Value="False">
                                                            <Setter Property="Background" Value="{DynamicResource AccentSoft2}" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </Border.Style>
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="1.3*" />
                                                    <ColumnDefinition Width="1.6*" />
                                                    <ColumnDefinition Width="70" />
                                                </Grid.ColumnDefinitions>
                                                <TextBlock Text="{Binding Title}" FontSize="13" />
                                                <TextBlock Grid.Column="1" Text="{Binding Detail}" FontSize="13" Foreground="{DynamicResource Text2}" TextTrimming="CharacterEllipsis" Margin="12,0" />
                                                <TextBlock Grid.Column="2" Text="{Binding Marker}" FontSize="13" Foreground="{DynamicResource AccentText}" TextAlignment="Right" />
                                            </Grid>
                                        </Border>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </Border>
                        <DockPanel Margin="0,12,0,0" Visibility="{Binding ShowFallback, Converter={StaticResource BoolToVisible}}">
                            <TextBlock Text="Když kameru nepoznám:" Style="{StaticResource TextMuted}" VerticalAlignment="Center" Margin="0,0,10,0" />
                            <ComboBox ItemsSource="{Binding FallbackChoices}" SelectedItem="{Binding Fallback}" DisplayMemberPath="Label" HorizontalAlignment="Left" MinWidth="200"
                                      AutomationProperties.AutomationId="LutFallbackChoice" />
                        </DockPanel>
                        <DockPanel Margin="0,10,0,0" Visibility="{Binding ShowCustom, Converter={StaticResource BoolToVisible}}">
                            <TextBlock Text="Vlastní LUT:" Style="{StaticResource TextMuted}" Margin="0,0,10,0" />
                            <TextBlock Text="{Binding LutName}" Style="{StaticResource TextMono}" ToolTip="{Binding LutPath}" TextTrimming="CharacterEllipsis" />
                        </DockPanel>
                        <Button Style="{StaticResource SecondaryButton}" Content="{Binding RulesButtonText}" Command="{Binding EditRulesCommand}" HorizontalAlignment="Left"
                                Margin="0,12,0,0" ToolTip="Vlastní pořadí: kamera / sériové číslo / cívka / složka → LUT"
                                Visibility="{Binding ShowRules, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="LutRulesButton" />

                        <!-- Další -->
                        <TextBlock Text="DALŠÍ" Style="{StaticResource StepTitle}" Margin="0,26,0,10" />
                        <CheckBox Style="{StaticResource Check}" Content="Zvuk v proxy" IsChecked="{Binding Audio}" Margin="0,0,0,10" />
                        <CheckBox Style="{StaticResource Check}" Content="Přeskočit proxy nahrané kamerou (SUB / PROXY)" IsChecked="{Binding SkipCameraProxies}" Margin="0,0,0,10" />
                        <CheckBox Style="{StaticResource Check}" Content="Kopírovat proxy na všechny cíle" IsChecked="{Binding CopyToAllDestinations}" Margin="0,0,0,10" />
                        <DockPanel Margin="0,4,0,0">
                            <TextBlock Text="Kopírovat proxy i do složky" Style="{StaticResource TextMuted}" VerticalAlignment="Center" Width="200" />
                            <Button DockPanel.Dock="Right" Style="{StaticResource SmallIconButton}" Command="{Binding ClearFolderCommand}" ToolTip="Nekopírovat do složky"
                                    Visibility="{Binding HasExtraFolder, Converter={StaticResource BoolToVisible}}"
                                    AutomationProperties.AutomationId="ProxyFolderClearButton" AutomationProperties.Name="Nekopírovat do složky">
                                <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconClose}" Width="14" Height="14" Tag="2"
                                                Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" />
                            </Button>
                            <Button DockPanel.Dock="Right" Style="{StaticResource SecondaryButton}" Content="Vybrat…" Command="{Binding PickFolderCommand}" Margin="8,0"
                                    AutomationProperties.AutomationId="ProxyFolderButton" />
                            <TextBlock Text="{Binding ExtraFolderName}" ToolTip="{Binding ExtraFolderPath}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                        </DockPanel>
                        <!-- Why a start is blocked or what is skipped (the window's checks, not the proxy settings themselves). -->
                        <TextBlock Text="{Binding DataContext.LutProblem, RelativeSource={RelativeSource AncestorType=UserControl}}" Style="{StaticResource TextDanger}" Margin="0,16,0,0"
                                   Visibility="{Binding DataContext.LutProblem, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={StaticResource NullToCollapsed}}" />
                        <TextBlock Text="{Binding DataContext.ProxyFolderProblem, RelativeSource={RelativeSource AncestorType=UserControl}}" Style="{StaticResource TextDanger}" Margin="0,8,0,0"
                                   Visibility="{Binding DataContext.ProxyFolderProblem, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={StaticResource NullToCollapsed}}" />
                        <TextBlock Text="{Binding DataContext.ToolsWarning, RelativeSource={RelativeSource AncestorType=UserControl}}" Style="{StaticResource TextWarn}" Margin="0,8,0,0"
                                   Visibility="{Binding DataContext.ToolsWarning, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={StaticResource NullToCollapsed}}" />
                    </StackPanel>

                    <!-- Náhled burn-inu: a grey 16:9 frame (rozhodnutí: no real frame yet) -->
                    <StackPanel Width="380" DataContext="{Binding Proxy}">
                        <TextBlock Text="NÁHLED BURN-INU" Style="{StaticResource StepTitle}" Margin="0,4,0,14" />
                        <Border CornerRadius="10" Background="{DynamicResource PreviewBg}" BorderBrush="{DynamicResource LineInput}" BorderThickness="1" ClipToBounds="True">
                            <Viewbox Stretch="Uniform">
                                <Grid Width="384" Height="216">
                                    <TextBlock Text="[náhled snímku z klipu]" Foreground="{DynamicResource PreviewText}" FontSize="12" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                    <Grid Visibility="{Binding HasFrame, Converter={StaticResource BoolToVisible}}">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="{Binding FrameLeft}" />
                                            <ColumnDefinition Width="{Binding FrameWidth}" />
                                            <ColumnDefinition Width="{Binding FrameRight}" />
                                        </Grid.ColumnDefinitions>
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="{Binding FrameTop}" />
                                            <RowDefinition Height="{Binding FrameHeight}" />
                                            <RowDefinition Height="{Binding FrameBottom}" />
                                        </Grid.RowDefinitions>
                                        <Border Grid.Row="1" Grid.Column="1" BorderBrush="{DynamicResource FrameLine}" BorderThickness="1" />
                                    </Grid>
                                    <Border Background="{DynamicResource Shade}" Padding="6,2" HorizontalAlignment="Left" VerticalAlignment="Bottom" Margin="25,0,0,17"
                                            Visibility="{Binding BurnFilename, Converter={StaticResource BoolToVisible}}">
                                        <TextBlock Text="A035C010_261007_R2KQ" FontFamily="{StaticResource MonoFont}" FontSize="11" Foreground="White" />
                                    </Border>
                                    <Border Background="{DynamicResource Shade}" Padding="6,2" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,25,17"
                                            Visibility="{Binding BurnTimecode, Converter={StaticResource BoolToVisible}}">
                                        <TextBlock Text="14:22:07:13" FontFamily="{StaticResource MonoFont}" FontSize="11" Foreground="White" />
                                    </Border>
                                </Grid>
                            </Viewbox>
                        </Border>
                        <Border CornerRadius="12" BorderBrush="{DynamicResource Line}" BorderThickness="1" Padding="14,6" Margin="0,14,0,0">
                            <StackPanel>
                                <DockPanel Margin="0,9">
                                    <TextBlock DockPanel.Dock="Right" Text="vlevo dole" Style="{StaticResource TextFaint}" VerticalAlignment="Center" />
                                    <CheckBox Style="{StaticResource Check}" Content="Název klipu" IsChecked="{Binding BurnFilename}" AutomationProperties.AutomationId="BurnFilename" />
                                </DockPanel>
                                <Border Height="1" Background="{DynamicResource LineSoft}" />
                                <DockPanel Margin="0,9">
                                    <TextBlock DockPanel.Dock="Right" Text="vpravo dole" Style="{StaticResource TextFaint}" VerticalAlignment="Center" />
                                    <CheckBox Style="{StaticResource Check}" Content="Timecode" IsChecked="{Binding BurnTimecode}" AutomationProperties.AutomationId="BurnTimecode" />
                                </DockPanel>
                                <Border Height="1" Background="{DynamicResource LineSoft}" />
                                <DockPanel Margin="0,9">
                                    <TextBlock DockPanel.Dock="Right" Text="{Binding FrameText}" Style="{StaticResource TextMono}" FontSize="12" VerticalAlignment="Center" />
                                    <TextBlock Text="Výřez (frame lines)" Margin="28,0,0,0" />
                                </DockPanel>
                            </StackPanel>
                        </Border>
                        <TextBlock Text="Canon Cinema RAW Light a Sony X-OCN se zkopírují a ověří, proxy pro ně zatím nejde vyrobit." Style="{StaticResource TextFaint}"
                                   FontSize="13" Margin="0,14,0,0" LineHeight="20" />
                    </StackPanel>
                </WrapPanel>

                <!-- ── Report ── -->
                <StackPanel Style="{StaticResource Section}" Visibility="{Binding SettingsSection, Converter={StaticResource EqualsToVisible}, ConverterParameter=report}">
                    <TextBlock Text="Report" Style="{StaticResource PageTitle}" />
                    <TextBlock Text="Vždy: HTML a PDF v _REPORT, ASC MHL a seznam kontrolních součtů na každém cíli." Style="{StaticResource TextMuted}" FontSize="14" Margin="0,4,0,26" />
                    <CheckBox Style="{StaticResource Check}" Content="Seznam klipů CSV" IsChecked="{Binding ClipListCsv}" Margin="0,0,0,10" AutomationProperties.AutomationId="ClipListCsv" />
                    <CheckBox Style="{StaticResource Check}" Content="Seznam klipů pro Avid (ALE)" IsChecked="{Binding ClipListAle}" Margin="0,0,0,10" AutomationProperties.AutomationId="ClipListAle" />
                    <CheckBox Style="{StaticResource Check}" Content="Denní report po každé kartě" IsChecked="{Binding DayReportAfterEachCard}" AutomationProperties.AutomationId="DayReportAfterEachCard" />
                    <TextBlock Text="DENNÍ REPORT" Style="{StaticResource StepTitle}" Margin="0,30,0,10" />
                    <TextBlock Text="Vytvoří znovu denní report vybrané složky dne na cíli a otevře ho." Style="{StaticResource TextMuted}" />
                    <Button Style="{StaticResource SecondaryButton}" Content="Denní report…" Command="{Binding DayReportCommand}" HorizontalAlignment="Left" Margin="0,12,0,0"
                            AutomationProperties.AutomationId="DayReportButton" />
                </StackPanel>

                <!-- ── Oznámení ── -->
                <StackPanel Style="{StaticResource Section}" Visibility="{Binding SettingsSection, Converter={StaticResource EqualsToVisible}, ConverterParameter=notify}">
                    <TextBlock Text="Oznámení" Style="{StaticResource PageTitle}" />
                    <TextBlock Text="Po konci úlohy oznámení ve Windows (když okno není vpředu) a na telefon přes ntfy." Style="{StaticResource TextMuted}" FontSize="14" Margin="0,4,0,26" />
                    <TextBlock Text="{Binding NotifySummary}" AutomationProperties.AutomationId="NotifySummary" />
                    <Button Style="{StaticResource SecondaryButton}" Content="Upravit oznámení…" Command="{Binding NotifySettingsCommand}" HorizontalAlignment="Left" Margin="0,14,0,0"
                            AutomationProperties.AutomationId="NotifyButton" />
                </StackPanel>

                <!-- ── Dekodéry RAW ── -->
                <StackPanel Style="{StaticResource Section}" Visibility="{Binding SettingsSection, Converter={StaticResource EqualsToVisible}, ConverterParameter=decoders}">
                    <TextBlock Text="Dekodéry RAW" Style="{StaticResource PageTitle}" />
                    <TextBlock Text="Nástroje pro proxy a náhledy. Platí pro všechny projekty." Style="{StaticResource TextMuted}" FontSize="14" Margin="0,4,0,26" />
                    <ItemsControl ItemsSource="{Binding Tools}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Style="{StaticResource TileBorder}" Margin="0,0,0,10">
                                    <DockPanel>
                                        <Button DockPanel.Dock="Right" Style="{StaticResource SecondaryButton}" Content="Vybrat…" Command="{Binding PickCommand}"
                                                ToolTip="Vybrat cestu k nástroji" VerticalAlignment="Center" />
                                        <ContentControl Width="16" Height="16" Margin="0,0,12,0">
                                            <ContentControl.Style>
                                                <Style TargetType="ContentControl" BasedOn="{StaticResource IconLine}">
                                                    <Setter Property="Content" Value="{StaticResource IconClose}" />
                                                    <Setter Property="Foreground" Value="{DynamicResource Text3}" />
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding Found}" Value="True">
                                                            <Setter Property="Content" Value="{StaticResource IconCheck}" />
                                                            <Setter Property="Foreground" Value="{DynamicResource Success}" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </ContentControl.Style>
                                        </ContentControl>
                                        <StackPanel>
                                            <TextBlock>
                                                <Run Text="{Binding Name, Mode=OneWay}" FontWeight="SemiBold" />
                                                <Run Text="{Binding Purpose, Mode=OneWay}" FontSize="12" Foreground="{DynamicResource Text2}" />
                                            </TextBlock>
                                            <TextBlock Text="{Binding PathText}" Style="{StaticResource TextMonoFaint}" Margin="0,2,0,0" />
                                        </StackPanel>
                                    </DockPanel>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <TextBlock Text="Canon Cinema RAW Light a Sony X-OCN se zkopírují a ověří, proxy pro ně zatím nejde vyrobit." Style="{StaticResource TextFaint}" FontSize="13" Margin="0,4,0,0" />
                    <CheckBox Style="{StaticResource Check}" Content="Blackmagic RAW Film Gen 4 převést na Gen 5 (použije se LUT Gen 5)" IsChecked="{Binding Proxy.BrawGen4ToGen5}"
                              Margin="0,18,0,0" AutomationProperties.AutomationId="BrawGen4ToGen5" />
                    <TextBlock Text="ověří až vzorek .braw Gen 4" Style="{StaticResource TextFaint}" Margin="28,2,0,0" />
                    <TextBlock Text="{Binding ToolsWarning}" Style="{StaticResource TextWarn}" Margin="0,16,0,0" Visibility="{Binding ToolsWarning, Converter={StaticResource NullToCollapsed}}" />
                </StackPanel>

                <!-- ── Aplikace (global) ── -->
                <StackPanel Style="{StaticResource Section}" Visibility="{Binding SettingsSection, Converter={StaticResource EqualsToVisible}, ConverterParameter=app}">
                    <TextBlock Text="Aplikace" Style="{StaticResource PageTitle}" />
                    <TextBlock Text="Platí pro všechny projekty." Style="{StaticResource TextMuted}" FontSize="14" Margin="0,4,0,26" />
                    <TextBlock Text="{Binding VersionText}" FontWeight="SemiBold" />
                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                        <Button Style="{StaticResource SecondaryButton}" Content="Aktualizace…" Command="{Binding UpdatesCommand}" AutomationProperties.AutomationId="UpdatesButton" />
                        <Button Style="{StaticResource SecondaryButton}" Content="Prověřit USB porty… (Ctrl+U)" Command="{Binding OpenHardwareCommand}" Margin="8,0,0,0" />
                    </StackPanel>
                    <TextBlock Text="{Binding HardwareSummary}" Style="{StaticResource TextMuted}" Margin="0,10,0,0" />
                </StackPanel>
            </Grid>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

#### `windows/src/Offload.App/SettingsPage.xaml.cs` (nový soubor)

```csharp
using System.Windows.Controls;

namespace Offload.App;

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();
}
```

#### `windows/src/Offload.Core/Desktop/SettingsTexts.cs` (nový soubor)

```csharp
using Offload.Core.Notify;

namespace Offload.Core.Desktop;

/// One built-in LUT in "LUT podle kamery": "ARRI · LogC4", "LogC4 / AWG4 → Rec.709" and "použito" for the one chosen.
public sealed record LutRow(string Id, string Title, string Detail, string? Marker);

/// The settings page (UI návrh obr. 4), without a window.
public static class SettingsTexts
{
    /// "≈ 75 MB / minutu" under the bitrate (video only; the audio adds little).
    public static string BitrateHint(int mbps) => $"≈ {mbps * 60 / 8} MB / minutu";

    static string Maker(string maker) => maker.StartsWith("Blackmagic", StringComparison.Ordinal) ? "Blackmagic" : maker;

    public static LutRow Row(BuiltinLut l, ProxySettings p)
    {
        var name = l.Name;
        foreach (var prefix in new[] { l.Maker + " ", Maker(l.Maker) + " " })
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name[prefix.Length..];
                break;
            }
        bool used = LutResolver.EffectiveMode(p) == LutMode.Builtin && p.LutBuiltin == l.Id;
        return new LutRow(l.Id, $"{Maker(l.Maker)} · {CameraLabel.ShortCurve(l.Curve)}", name, used ? "použito" : null);
    }

    /// "Windows: zapnuto · telefon (ntfy): vypnuto" / "… telefon (ntfy): film-x-tajne".
    public static string NotifySummary(NotifySettings n) =>
        $"Windows: {(n.Windows ? "zapnuto" : "vypnuto")} · telefon (ntfy): {(n.Ntfy.Enabled && n.Ntfy.Topic.Length > 0 ? n.Ntfy.Topic : "vypnuto")}";

    /// "Výřez" in the burn-in preview: "celý obraz", or "1.85 · 90 %".
    public static string FrameText(ProxySettings p) => p.FrameLabel ?? "celý obraz";

    /// Where the crop's frame lines sit in a 16:9 picture (fractions of its width and height); null = no crop.
    public static (double X, double Y, double W, double H)? Frame(ProxySettings p) =>
        p.FrameCrop(1920, 1080) is { } c ? (c.X / 1920.0, c.Y / 1080.0, c.W / 1920.0, c.H / 1080.0) : null;
}
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~SettingsTextsTests"`

Expected: PASS (`Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 upozornění, 0 chyb`; celá sada `Neúspěšné: 0` (časově citlivé `ProxyWatchdogTests` / `ProxyRendererTests.Feeder_*` při jednom selhání pusťte znovu samostatně — viz Global Constraints).

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)`.

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1):

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 11 — horní lišta (postup 2) místo menu: logo a „Offload“, přepínač **Projekt** na místě přepínače presetu (rozbalovací seznam projektů s odškrtnutím aktivního, „Uložit jako nový projekt…“, „Přejmenovat…“, „Smazat…“; zamčený, dokud Start kontroluje — I1), vpravo odkaz „Nová verze …“, pilulka „● Běží 1 offload · 1 ve frontě“ (`QueueSummary`, pozastavené zvlášť), „Ověřit zálohu“ (Ctrl+Shift+O), „Hardware“ s tečkou (zelená = porty bez varování, žlutá = doporučení, žádná před prvním testem; `TopBar.Dot`; Ctrl+U) a tlačítko s posuvníky (zatím nabídka Denní report…, Oznámení…, Aktualizace…, Ukládat report ověření — příští úkol je přesune do Nastavení); okno 1360 × 900 (min 940 × 640 beze změny); přepínač „Projekt:“ z levého panelu je pryč
- Úkol 10 — téma (postup 1): barevné tokeny `Themes/Dark.xaml` (barvy z návrhu) a `Themes/Light.xaml` (odvozená světlá sada, stejné klíče), styly `Themes/Offload.xaml` (`StepTitle`, `TextMuted`, `TextFaint`, `TextMono`, `TileBorder`, `PillBorder`, `DashedBox`, tlačítka `PrimaryButton` / `SecondaryButton` / `GhostButton` / `LinkButton` / `DangerButton` / `SuccessButton` / `WarnButton` / `WarnSoftButton`, záložky, segmenty, `Check`, `Switch`, `InputBox`, `ProgressThin`) a čárové ikony z mockupu `Themes/Icons.xaml`; písmo Segoe UI Variable / Segoe UI a Cascadia Mono / Consolas (IBM Plex se nepřibaluje); `ThemeWatcher` vybere sadu podle „Režim aplikací“ ve Windows a přepne ji při změně (`ThemeChoice`, proměnná `OFFLOAD_THEME=dark|light` jen pro snímky); test: stejné klíče, tmavá sada = návrh, kontrast textů ≥ 4,5 : 1 v obou sadách, všechny slovníky se načtou (`XamlReader`; testy referencují WPF z .NET, ne `Offload.App`)
- Úkol 9 — texty levého sloupce a dnešní historie (nová věc 7): `SetupTexts.AfterCopy` (krok 3: Ověření zpětným čtením `xxHash64` / „vypnuto“ žlutě, Analýza klipů a náhledy `3 snímky / klip`, Proxy `1080p · H.264 · LogC4 → 709` — křivka z LUTu nebo z kamery vybrané karty, Report `PDF · HTML · ASC MHL` + zapnuté seznamy klipů), `AfterCopyLine` (krok 3 během kopie na jeden řádek), `StartSummary` („1 karta → 2 disky · 1,70 GB“), `NoCard`, `Compact`, `PathPreview` („…\2026-10-07\A035_ARRI\“), `QueueEmpty` („Připraveno — A035_ARRI na 2 disky“ + věta podle počtu disků a ověření) a `Flow` (Karta → 2 disky naráz → Ověření → Proxy → Report jen z toho, co projekt dělá); `HistoryView` z `history.json`: „DNES“ („3 karty · 412 GB · vše ověřeno“, řádek „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“, „✕ s chybami (3 soubory)“, „! neověřeno“) a dny pro záložku Historie karet („Dnes“, „Včera“, „4. 10. 2026“; nejvýš 300 karet)
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 12 — stránka Nastavení (postup 6, předsunutý před levý sloupec, aby žádná volba nebyla ani na jeden commit nedostupná): ikona s posuvníky v liště nebo Ctrl+, otevře místo obou sloupců stránku s lištou „‹ Zpět · Projekt <název> · Uložit jako nový projekt… · Hotovo“ (Esc zavře) a sekcemi Struktura složek (cesta, vlastní datum, název podle klipů) · Ověření (vypínač, kontrolní součet, MD5, Ověřit zálohu… + „Ukládat report ověření“) · Analýza a náhledy · **Proxy** podle obr. 4 (vypínač, segmenty Kodek a Výška, datový tok s „≈ 75 MB / minutu“, výřez, LUT + tabulka vestavěných LUTů s „použito“, „Když kameru nepoznám“, Pravidla LUTů…, Vlastní .cube…, zvuk, kamerové proxy, kopie na všechny cíle, složka pro proxy; vpravo šedý náhled burn-inu 16:9 s názvem klipu, TC a frame lines výřezu) · Report (CSV, ALE, denní report po kartě, Denní report…) · Oznámení (souhrn + Upravit oznámení…) · Dekodéry RAW (nástroje, Gen 4 → Gen 5) · Aplikace (verze, Aktualizace…, Prověřit USB porty…); úpravy jdou do zobrazeného profilu jako dosud (`SettingsTexts`: řádky LUTů, tok za minutu, rám výřezu, souhrn oznámení)
- Úkol 11 — horní lišta (postup 2) místo menu: logo a „Offload“, přepínač **Projekt** na místě přepínače presetu (rozbalovací seznam projektů s odškrtnutím aktivního, „Uložit jako nový projekt…“, „Přejmenovat…“, „Smazat…“; zamčený, dokud Start kontroluje — I1), vpravo odkaz „Nová verze …“, pilulka „● Běží 1 offload · 1 ve frontě“ (`QueueSummary`, pozastavené zvlášť), „Ověřit zálohu“ (Ctrl+Shift+O), „Hardware“ s tečkou (zelená = porty bez varování, žlutá = doporučení, žádná před prvním testem; `TopBar.Dot`; Ctrl+U) a tlačítko s posuvníky (zatím nabídka Denní report…, Oznámení…, Aktualizace…, Ukládat report ověření — příští úkol je přesune do Nastavení); okno 1360 × 900 (min 940 × 640 beze změny); přepínač „Projekt:“ z levého panelu je pryč
- Úkol 10 — téma (postup 1): barevné tokeny `Themes/Dark.xaml` (barvy z návrhu) a `Themes/Light.xaml` (odvozená světlá sada, stejné klíče), styly `Themes/Offload.xaml` (`StepTitle`, `TextMuted`, `TextFaint`, `TextMono`, `TileBorder`, `PillBorder`, `DashedBox`, tlačítka `PrimaryButton` / `SecondaryButton` / `GhostButton` / `LinkButton` / `DangerButton` / `SuccessButton` / `WarnButton` / `WarnSoftButton`, záložky, segmenty, `Check`, `Switch`, `InputBox`, `ProgressThin`) a čárové ikony z mockupu `Themes/Icons.xaml`; písmo Segoe UI Variable / Segoe UI a Cascadia Mono / Consolas (IBM Plex se nepřibaluje); `ThemeWatcher` vybere sadu podle „Režim aplikací“ ve Windows a přepne ji při změně (`ThemeChoice`, proměnná `OFFLOAD_THEME=dark|light` jen pro snímky); test: stejné klíče, tmavá sada = návrh, kontrast textů ≥ 4,5 : 1 v obou sadách, všechny slovníky se načtou (`XamlReader`; testy referencují WPF z .NET, ne `Offload.App`)
- Úkol 9 — texty levého sloupce a dnešní historie (nová věc 7): `SetupTexts.AfterCopy` (krok 3: Ověření zpětným čtením `xxHash64` / „vypnuto“ žlutě, Analýza klipů a náhledy `3 snímky / klip`, Proxy `1080p · H.264 · LogC4 → 709` — křivka z LUTu nebo z kamery vybrané karty, Report `PDF · HTML · ASC MHL` + zapnuté seznamy klipů), `AfterCopyLine` (krok 3 během kopie na jeden řádek), `StartSummary` („1 karta → 2 disky · 1,70 GB“), `NoCard`, `Compact`, `PathPreview` („…\2026-10-07\A035_ARRI\“), `QueueEmpty` („Připraveno — A035_ARRI na 2 disky“ + věta podle počtu disků a ověření) a `Flow` (Karta → 2 disky naráz → Ověření → Proxy → Report jen z toho, co projekt dělá); `HistoryView` z `history.json`: „DNES“ („3 karty · 412 GB · vše ověřeno“, řádek „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“, „✕ s chybami (3 soubory)“, „! neověřeno“) a dny pro záložku Historie karet („Dnes“, „Včera“, „4. 10. 2026“; nejvýš 300 karet)
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: settings page with folders, verify, analysis, proxy, report, notify, decoders, app (plan 9, task 12)

The settings page replaces both columns (Ctrl+, opens, Esc closes); the proxy section follows the
design (segmented codec and height, bitrate per minute, built-in LUT table, grey burn-in preview).
It comes before the left column so no option is ever unreachable.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.App\App.xaml windows\src\Offload.App\Converters.cs windows\src\Offload.App\MainViewModel.Notify.cs windows\src\Offload.App\MainViewModel.SettingsPage.cs windows\src\Offload.App\MainViewModel.TopBar.cs windows\src\Offload.App\MainViewModel.cs windows\src\Offload.App\MainWindow.xaml windows\src\Offload.App\ProxyOptionsViewModel.cs windows\src\Offload.App\SettingsPage.xaml windows\src\Offload.App\SettingsPage.xaml.cs windows\src\Offload.Core\Desktop\SettingsTexts.cs windows\tests\Offload.Core.Tests\SettingsTextsTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 13: Levý sloupec — kroky 1 · Karty, 2 · Cíle, 3 · Po zkopírování a lišta se spuštěním (postup 3)

`SetupPanel.xaml` se přepíše na tři kroky s připnutou spodní lištou podle obr. 1 a kompaktní podobu podle obr. 2
a 3. Dlaždice karty má štítek kamery a typ připojení (spojení se zjišťuje mimo UI vlákno), dlaždice cíle pruh místa,
krok 3 shrnuje projekt a „Upravit projekt“ otevře Nastavení. Lišta ukáže „1 karta → 2 disky · 1,70 GB“ a odhad.

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.App/DestinationViewModel.cs`
- Modify: `windows/src/Offload.App/JobItemViewModel.cs`
- Modify: `windows/src/Offload.App/MainViewModel.History.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Space.cs`
- Modify: `windows/src/Offload.App/MainViewModel.cs`
- Modify: `windows/src/Offload.App/SetupPanel.xaml`
- Modify: `windows/src/Offload.App/SourceItemViewModel.cs`
- Modify: `windows/src/Offload.Core/Desktop/SetupTexts.cs`
- Test: `windows/tests/Offload.Core.Tests/CardTileTests.cs` (nový)

**Interfaces:**
- Consumes: `CameraHints`, `ConnectionLabel`, `DestinationTile` (úkol 8), `SetupTexts` (úkol 9), `RateMemory` (úkol 7), `QueueSummary.StartLabel` (úkol 6), `OpenSettingsCommand` (úkol 12), `DriveLinks.Resolve` (stávající).
- Produces:
  - `SetupTexts.CardFacts(CardInsight? insight, long? usedOnVolume, string? filesystem, string? connection, bool looksLikeCard)`, `SetupTexts.Total(IEnumerable<long?>)`.
  - `SourceItemViewModel`: `Facts`, `CameraText`, `CanAppend`, `NameHintSuffix`, `FactsChanged()`.
  - `DestinationViewModel`: `CompactFreeText`, `RemoveName`, `HasBar`, `BarUsed/BarNeed/BarRest` (`GridLength`), `BarTone`, `SpaceNote`, `SetSpace(long needHere, SpaceSeverity? severity, string? note)`.
  - `MainViewModel`: `StartCounts`, `StartSize`, `EstimateText`, `StartLabel`, `IsCompact`, `NoCardTitle`, `NoCardText`, `AfterCopyRows`, `AfterCopyLine`, `VerifyOffWarning`, `PathPreview`, `SourceFacts(string path)`; `JobItemViewModel.Stage2Elapsed`.
  - AutomationId: `RefreshVolumesButton`, `AddSourceButton`, `CardCameraText`, `CardFactsText`, `CardHistoryText`, `AppendNewFiles`, `CardNameBox`, `AddDestinationButton`, `PathPreviewText`, `AfterCopyRows`, `AfterCopyLine`, `EditProjectButton`, `StartSummaryText`, `EstimateText`, `StartButton`, `StartProblemText`, `StartStatusText`, `SpaceProblemText`, `SpaceWarningText`, `NoCardTitle`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/CardTileTests.cs` (nový soubor)

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

/// The card tile's line and the start bar's size (UI návrh obr. 1).
public class CardTileTests
{
    static Clip Clip(int i) => new() { Name = $"A035C{i:000}", Path = $"A035C{i:000}.mxf", Segments = [$"A035C{i:000}.mxf"] };

    [Fact]
    public void Facts_FromTheLook_ElseTheVolume()
    {
        var insight = new CardInsight { Source = @"E:\", TotalBytes = 1_700_000_000, FileCount = 30, Clips = Enumerable.Range(1, 24).Select(Clip).ToList() };
        Assert.Equal("24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s", SetupTexts.CardFacts(insight, 1_800_000_000, "exFAT", "čtečka USB 3 · 5 Gb/s", true));
        Assert.Equal("1,80 GB · exFAT", SetupTexts.CardFacts(null, 1_800_000_000, "exFAT", null, true));   // not looked at yet
        Assert.Equal("3 soubory · 850 MB · NTFS · nevypadá jako karta",
            SetupTexts.CardFacts(new CardInsight { Source = @"F:\", TotalBytes = 850_000_000, FileCount = 3 }, null, "NTFS", null, false));
        Assert.Equal("1,80 GB", SetupTexts.CardFacts(new CardInsight { Source = @"E:\", Error = "x" }, 1_800_000_000, null, null, true));
    }

    [Fact]
    public void Total_NullWhileOneIsUnknown()
    {
        Assert.Equal(30, SetupTexts.Total([10, 20]));
        Assert.Null(SetupTexts.Total([10, null]));
        Assert.Equal(0, SetupTexts.Total([]));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CardTileTests"`

Expected: FAIL — build selže — `CS0117: SetupTexts neobsahuje definici pro CardFacts` (a `Total`).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.App/DestinationViewModel.cs`

Najít (1/6):

```csharp
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.App;
```

Nahradit:

```csharp
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Hardware;
using Offload.Core.Space;

namespace Offload.App;
```

Najít (2/6):

```csharp
{
    readonly MainViewModel owner;
    bool exists, isChecking = true, probing, mayDropOut;
    string fileSystem = "", freeText = "zjišťuji…";
    string? fsWarning, volume;
    long? freeBytes;
    Task probeTask = Task.CompletedTask;

    public DestinationViewModel(MainViewModel owner, int index, string root)
```

Nahradit:

```csharp
{
    readonly MainViewModel owner;
    bool exists, isChecking = true, probing, mayDropOut;
    string fileSystem = "", freeText = "zjišťuji…", compactFreeText = "";
    string? fsWarning, volume, spaceNote, barTone;
    long? freeBytes, totalBytes;
    long need;
    Task probeTask = Task.CompletedTask;

    public DestinationViewModel(MainViewModel owner, int index, string root)
```

Najít (3/6):

```csharp
            string beforeText = freeText;
            if (t.IsCompletedSuccessfully)
            {
                var (ex, fs, free, volatileDisk, vol) = t.Result;
                exists = ex;
                mayDropOut = volatileDisk;
                volume = ex ? vol : null;
                freeBytes = ex ? free : null;
                fileSystem = fs;
                freeText = ex ? $"{(free is long f ? Format.Bytes(f) : "?")} volno · {fs}" : "";
                fsWarning = ex && fs is "exFAT" or "FAT32" or "FAT" ? "Doporučeno NTFS — spolehlivější než exFAT/FAT32" : null;
            }
            else
```

Nahradit:

```csharp
            string beforeText = freeText;
            if (t.IsCompletedSuccessfully)
            {
                var (ex, fs, free, volatileDisk, vol, total, connection) = t.Result;
                exists = ex;
                mayDropOut = volatileDisk;
                volume = ex ? vol : null;
                freeBytes = ex ? free : null;
                totalBytes = ex ? total : null;
                fileSystem = fs;
                // "7,20 TB volných z 8 TB · NTFS · NVMe" (UI návrh obr. 1); "7,20 TB volno" in the compact list.
                freeText = ex ? (free is long f ? DestinationTile.FreeText(f, total, fs, connection) : $"? volno · {fs}") : "";
                compactFreeText = ex && free is long cf ? DestinationTile.CompactFree(cf) : "";
                fsWarning = ex && fs is "exFAT" or "FAT32" or "FAT" ? "Doporučeno NTFS — spolehlivější než exFAT/FAT32" : null;
            }
            else
```

Najít (4/6):

```csharp
            }
            isChecking = false;
            if (!wasChecking && before == exists && beforeText == freeText) return;   // nothing new
            RaiseMany(nameof(Exists), nameof(FileSystem), nameof(FreeText), nameof(FsWarning), nameof(IsChecking), nameof(ShowMissing));
            owner.DestinationInfoChanged();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
```

Nahradit:

```csharp
            }
            isChecking = false;
            if (!wasChecking && before == exists && beforeText == freeText) return;   // nothing new
            RaiseMany(nameof(Exists), nameof(FileSystem), nameof(FreeText), nameof(CompactFreeText), nameof(FsWarning), nameof(IsChecking), nameof(ShowMissing),
                nameof(HasBar), nameof(BarUsed), nameof(BarNeed), nameof(BarRest));
            owner.DestinationInfoChanged();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
```

Najít (5/6):

```csharp
        await Probe();
    }

    static (bool, string, long?, bool, string?) ProbeDisk(string root)
    {
        bool mayDropOut = DestinationHealth.MayDropOut(root, DriveTypeOf(root));
        if (!Directory.Exists(root)) return (false, "", null, mayDropOut, null);
        return (true, NativeIO.FilesystemType(root), NativeIO.AvailableBytes(root), mayDropOut, SpacePlanner.VolumeKey(root));
    }

    /// The type of the root's drive letter (network, removable, fixed…), null for a UNC path or when unknown.
```

Nahradit:

```csharp
        await Probe();
    }

    static (bool, string, long?, bool, string?, long?, string?) ProbeDisk(string root)
    {
        bool mayDropOut = DestinationHealth.MayDropOut(root, DriveTypeOf(root));
        if (!Directory.Exists(root)) return (false, "", null, mayDropOut, null, null, null);
        // The connection from the HW diagnostics (UI návrh 6): never throws, "unknown" is null.
        var connection = ConnectionLabel.Short(DriveLinks.Resolve(root), card: false);
        return (true, NativeIO.FilesystemType(root), NativeIO.AvailableBytes(root), mayDropOut, SpacePlanner.VolumeKey(root), NativeIO.TotalBytes(root), connection);
    }

    /// The type of the root's drive letter (network, removable, fixed…), null for a UNC path or when unknown.
```

Najít (6/6):

```csharp
    public bool ShouldReprobe => DestinationHealth.ShouldReprobe(isChecking, exists, mayDropOut);
    public string FileSystem => fileSystem;
    public string FreeText => freeText;
    public string? FsWarning => fsWarning;
    /// For the space check (spec 2.5): the volume and its free bytes from the last probe; null until known or when missing.
    public SpaceTarget? SpaceTarget => exists && volume != null ? new SpaceTarget(Root, volume, HistoryText.DiskName(Root)) : null;
```

Nahradit:

```csharp
    public bool ShouldReprobe => DestinationHealth.ShouldReprobe(isChecking, exists, mayDropOut);
    public string FileSystem => fileSystem;
    public string FreeText => freeText;
    public string CompactFreeText => compactFreeText;
    public string RemoveName => $"Odebrat cíl {Name}";

    // The space bar (UI návrh obr. 1): grey = used, blue = what the ticked cards need here; yellow / red with the space check.
    public bool HasBar => exists && totalBytes > 0 && freeBytes != null;
    (double Used, double Need) Bar => HasBar ? DestinationTile.Bar(totalBytes!.Value, freeBytes!.Value, need) : (0, 0);
    public System.Windows.GridLength BarUsed => new(Math.Max(Bar.Used, 0.0001), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength BarNeed => new(Math.Max(Bar.Need, 0.0001), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength BarRest => new(Math.Max(1 - Bar.Used - Bar.Need, 0.0001), System.Windows.GridUnitType.Star);
    /// null, "Warning" or "Blocking" (SpaceSeverity) — the bar's and the note's colour.
    public string? BarTone => barTone;
    /// The space check's sentence about this disk ("Na cíli … není dost místa …"), or null.
    public string? SpaceNote => spaceNote;

    /// The window's space check ran (3× a second): raised only when something changed.
    internal void SetSpace(long needHere, SpaceSeverity? severity, string? note)
    {
        var tone = severity?.ToString();
        if (needHere == need && tone == barTone && note == spaceNote) return;
        need = needHere;
        barTone = tone;
        spaceNote = note;
        RaiseMany(nameof(BarUsed), nameof(BarNeed), nameof(BarRest), nameof(BarTone), nameof(SpaceNote));
    }
    public string? FsWarning => fsWarning;
    /// For the space check (spec 2.5): the volume and its free bytes from the last probe; null until known or when missing.
    public SpaceTarget? SpaceTarget => exists && volume != null ? new SpaceTarget(Root, volume, HistoryText.DiskName(Root)) : null;
```

#### `windows/src/Offload.App/JobItemViewModel.cs`

Najít (1/1):

```csharp
        space.Select(d => d with { Need = SpaceCheck.Remaining(d.Need, Phase, writtenAtProbe) });
    public bool IsRunning => progress.IsRunning;
    public override bool IsPaused => progress.Status.Paused;
    public override bool IsFinished => Phase.IsFinished();
    public bool SafeToEject => progress.Status.SafeToEject;
    public bool? CopySucceeded => progress.Status.CopySucceeded;
```

Nahradit:

```csharp
        space.Select(d => d with { Need = SpaceCheck.Remaining(d.Need, Phase, writtenAtProbe) });
    public bool IsRunning => progress.IsRunning;
    public override bool IsPaused => progress.Status.Paused;
    /// How long stage 2 took without pauses, once finished (RateMemory learns the next estimate from it).
    public TimeSpan? Stage2Elapsed => progress.Stage2Elapsed;
    public override bool IsFinished => Phase.IsFinished();
    public bool SafeToEject => progress.Status.SafeToEject;
    public bool? CopySucceeded => progress.Status.CopySucceeded;
```

#### `windows/src/Offload.App/MainViewModel.History.cs`

Najít (1/2):

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;
```

Nahradit:

```csharp
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.History;
```

Najít (2/2):

```csharp
        if (history is not { } entries || closing) return;   // InspectAll runs once history is loaded
        if (inspections.Start(path, IdentityOf(path)) is not CancellationToken ct) return;
        var store = historyStore;
        Task.Run(() => CardInspector.Inspect(path, entries, store, ct), ct).ContinueWith(t =>
        {
            if (t.IsFaulted) AppLog.Write($"card inspect {path}: {t.Exception?.GetBaseException().Message}");
            bool listed = Sources.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
```

Nahradit:

```csharp
        if (history is not { } entries || closing) return;   // InspectAll runs once history is loaded
        if (inspections.Start(path, IdentityOf(path)) is not CancellationToken ct) return;
        var store = historyStore;
        var tools = resolvedTools;
        // With the look at the card, one clip names the camera (UI návrh 6) — reading only, ffprobe at most 5 s.
        Task.Run(() => CardInspector.Inspect(path, entries, store, ct) is var i && i.Error == null
            ? i with { Camera = CameraHints.Probe(i, tools, LutCatalog.Default, ct) }
            : i, ct).ContinueWith(t =>
        {
            if (t.IsFaulted) AppLog.Write($"card inspect {path}: {t.Exception?.GetBaseException().Message}");
            bool listed = Sources.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
```

#### `windows/src/Offload.App/MainViewModel.Queue.cs`

Najít (1/4):

```csharp
public sealed partial class MainViewModel
{
    readonly JobQueue queue = new(m => AppLog.Write(m));
    readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(330) };
    bool closing, closeRaised, waitNoticeShown, shownHasFinished, shownCopyRunning, starting;
    string? startStatus;
```

Nahradit:

```csharp
public sealed partial class MainViewModel
{
    readonly JobQueue queue = new(m => AppLog.Write(m));
    // What the jobs of this session took — the start bar's estimate (UI návrh 10).
    readonly RateMemory rates = new();
    bool shownCompact, shownJobsRunning;
    string? shownStartLabel;
    readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(330) };
    bool closing, closeRaised, waitNoticeShown, shownHasFinished, shownCopyRunning, starting;
    string? startStatus;
```

Najít (2/4):

```csharp
        queue.JobFinished += j => Dispatch(() => OnJobFinished(j));
        ticker.Tick += (_, _) => Tick();
        ticker.Start();
        SetupChanged += () => RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart), nameof(StartProblem));
        RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart));
    }

    static void Dispatch(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public string CardsText => Texts.Cards(selected.Count);
    public string DestinationsText => Texts.Destinations(Settings.DestRoots.Count) + (Settings.Proxy.Enabled ? " · proxy" : "");
    /// In memory only (evaluated 3×/s): the LUT, the proxy folder and the destinations come from checks made off the UI thread.
    public bool CanStart => !starting && ReadyToStart;
```

Nahradit:

```csharp
        queue.JobFinished += j => Dispatch(() => OnJobFinished(j));
        ticker.Tick += (_, _) => Tick();
        ticker.Start();
        SetupChanged += () => RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart), nameof(StartProblem),
            nameof(StartCounts), nameof(StartSize), nameof(EstimateText), nameof(AfterCopyRows), nameof(AfterCopyLine), nameof(PathPreview));
        RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart));
    }

    static void Dispatch(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public string CardsText => Texts.Cards(selected.Count);

    // ── The start bar and step 3 (UI návrh obr. 1) ─────────────────────────────────────────────────────────────
    /// What each ticked card will copy (an append only its new files), null while one is not known yet.
    long? SelectedBytes => SetupTexts.Total(SelectedSources().Select(p => AppendChosen(p) && Insight(p)?.History is { } h
        ? h.NewBytes : Insight(p) is { Error: null } i ? i.TotalBytes : (long?)null));

    /// "1 karta → 2 disky" (bold) and " · 1,70 GB".
    public string StartCounts => SetupTexts.StartSummary(selected.Count, Settings.DestRoots.Count, SelectedBytes).Counts;
    public string? StartSize => SetupTexts.StartSummary(selected.Count, Settings.DestRoots.Count, SelectedBytes).Size;

    /// "odhad ~8:00 vč. proxy" — only from what this session measured (RateMemory); null shows only the size.
    public string? EstimateText => SelectedBytes is long bytes && selected.Count > 0
        ? RateMemory.StartText(rates.Estimate(bytes, SelectedSources().SelectMany(p => Insight(p)?.Clips ?? []).ToList(), Settings.Verify,
            Settings.AnalyzeClips, Settings.Proxy), Settings.Proxy.Enabled)
        : null;

    /// "Spustit offload", or "Zařadit do fronty" while a card waits for or is in its copy.
    public string StartLabel => QueueSummary.StartLabel(Jobs.Select(j => j.Phase));

    /// No card and jobs in the queue: steps 2 and 3 compact (obr. 2, 3).
    public bool IsCompact => SetupTexts.Compact(HasSources, HasJobs);

    bool JobsRunning => Jobs.Any(j => !j.IsFinished);
    public string NoCardTitle => SetupTexts.NoCard(JobsRunning).Title;
    public string NoCardText => SetupTexts.NoCard(JobsRunning).Text;

    /// Step 3: Ověření · Analýza · Proxy · Report of the profile shown (the camera of the first ticked card names the LUT).
    public IReadOnlyList<AfterCopyRow> AfterCopyRows =>
        SetupTexts.AfterCopy(Settings, lutCatalog, SelectedSources().Select(p => Insight(p)?.Camera).FirstOrDefault(c => c != null));
    public string AfterCopyLine => SetupTexts.AfterCopyLine(Settings);
    public string VerifyOffWarning => SetupTexts.VerifyOffWarning;

    /// The parts of the left column that follow the queue (checked every tick, raised only when they change).
    void RefreshLayout()
    {
        if (IsCompact != shownCompact)
        {
            shownCompact = IsCompact;
            Raise(nameof(IsCompact));
        }
        if (JobsRunning != shownJobsRunning)
        {
            shownJobsRunning = JobsRunning;
            RaiseMany(nameof(NoCardTitle), nameof(NoCardText));
        }
        if (StartLabel != shownStartLabel)
        {
            shownStartLabel = StartLabel;
            Raise(nameof(StartLabel));
        }
    }
    public string DestinationsText => Texts.Destinations(Settings.DestRoots.Count) + (Settings.Proxy.Enabled ? " · proxy" : "");
    /// In memory only (evaluated 3×/s): the LUT, the proxy folder and the destinations come from checks made off the UI thread.
    public bool CanStart => !starting && ReadyToStart;
```

Najít (3/4):

```csharp
            if (!j.IsFinished) j.Refresh();
        RefreshSpace();   // memory only: a running job needs less as it copies (against its baseline)
        RefreshQueueSummary();   // the top bar's pill and the queue tab's number
        // A queued job cancelled behind a running copy is finished before the queue reports it.
        if (HasFinished != shownHasFinished)
        {
```

Nahradit:

```csharp
            if (!j.IsFinished) j.Refresh();
        RefreshSpace();   // memory only: a running job needs less as it copies (against its baseline)
        RefreshQueueSummary();   // the top bar's pill and the queue tab's number
        RefreshLayout();   // compact steps, the empty card text, the start button's label
        // A queued job cancelled behind a running copy is finished before the queue reports it.
        if (HasFinished != shownHasFinished)
        {
```

Najít (4/4):

```csharp
    {
        if (Find(job) is { } item)
        {
            if (item is JobItemViewModel j) j.LeftQueue = true;
            item.Refresh();
            if (!item.Alerted) Alert(item);
            if (QueueRules.NotifyAtJobEnd(item.Notified) && item is JobItemViewModel ended)
```

Nahradit:

```csharp
    {
        if (Find(job) is { } item)
        {
            if (item is JobItemViewModel j)
            {
                j.LeftQueue = true;
                if (j.Job.Report is { } r) rates.Learn(r, j.Job.Config.AnalyzeClips, j.Job.Config.Proxy, j.Stage2Elapsed);   // the next estimate
            }
            item.Refresh();
            if (!item.Alerted) Alert(item);
            if (QueueRules.NotifyAtJobEnd(item.Notified) && item is JobItemViewModel ended)
```

#### `windows/src/Offload.App/MainViewModel.Setup.cs`

Najít (1/4):

```csharp
    // Volume labels of manual sources that are a drive root the scan does not list (an internal disk, a network
    // letter), read off the UI thread when the source is added.
    readonly Dictionary<string, string?> manualLabels = new(StringComparer.OrdinalIgnoreCase);
    List<VolumeItem> volumes = [];
    ExternalTools resolvedTools = new();
    bool scanning, rescan, useCustomDate;
```

Nahradit:

```csharp
    // Volume labels of manual sources that are a drive root the scan does not list (an internal disk, a network
    // letter), read off the UI thread when the source is added.
    readonly Dictionary<string, string?> manualLabels = new(StringComparer.OrdinalIgnoreCase);
    // How each source's disk is connected (UI návrh 6), looked up off the UI thread once per card in the drive.
    readonly Dictionary<string, (string? Identity, string? Label)> sourceLinks = new(StringComparer.OrdinalIgnoreCase);
    List<VolumeItem> volumes = [];
    ExternalTools resolvedTools = new();
    bool scanning, rescan, useCustomDate;
```

Najít (2/4):

```csharp
            RebuildDestinations();
        }
        InspectMissing();   // new or swapped cards
        foreach (var j in Jobs.OfType<JobItemViewModel>()) j.VolumesChanged();   // "Vysunout kartu" only while the copied card is in its drive
        RefreshLut();   // the LUT may sit on a disk that just came or went
        RefreshProxyFolder();   // and so may the proxy folder
```

Nahradit:

```csharp
            RebuildDestinations();
        }
        InspectMissing();   // new or swapped cards
        ResolveSourceLinks();   // how the new cards are connected
        foreach (var j in Jobs.OfType<JobItemViewModel>()) j.VolumesChanged();   // "Vysunout kartu" only while the copied card is in its drive
        RefreshLut();   // the LUT may sit on a disk that just came or went
        RefreshProxyFolder();   // and so may the proxy folder
```

Najít (3/4):

```csharp
        RebuildSources();
        ReadManualLabel(path);
        Inspect(path);
    }

    /// A drive root picked as a folder: its label is the card name the job will use — read it off the UI thread.
```

Nahradit:

```csharp
        RebuildSources();
        ReadManualLabel(path);
        Inspect(path);
        ResolveSourceLinks();
    }

    /// The connection of every listed source whose card has not been looked at yet ("čtečka USB 3 · 5 Gb/s"), each off
    /// the UI thread (DriveLinks asks the device; it never throws).
    void ResolveSourceLinks()
    {
        foreach (var s in Sources)
        {
            var path = s.Path;
            var identity = IdentityOf(path);
            if (sourceLinks.TryGetValue(path, out var known) && string.Equals(known.Identity, identity, StringComparison.OrdinalIgnoreCase)) continue;
            sourceLinks[path] = (identity, null);
            Task.Run(() => ConnectionLabel.Short(Offload.Core.Hardware.DriveLinks.Resolve(path), card: true)).ContinueWith(t =>
            {
                if (closing || !t.IsCompletedSuccessfully) return;
                if (!sourceLinks.TryGetValue(path, out var now) || !string.Equals(now.Identity, identity, StringComparison.OrdinalIgnoreCase)) return;
                sourceLinks[path] = (identity, t.Result);
                RowFor(path)?.FactsChanged();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    /// The card tile's line: clips, size, file system, connection (memory only).
    internal string SourceFacts(string path)
    {
        var volume = volumes.FirstOrDefault(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase));
        var link = sourceLinks.TryGetValue(path, out var l) && string.Equals(l.Identity, IdentityOf(path), StringComparison.OrdinalIgnoreCase) ? l.Label : null;
        return SetupTexts.CardFacts(Insight(path), volume is { } v ? v.Total - v.Free : null, volume?.Format ?? Insight(path)?.Filesystem, link,
            volume?.LooksLikeCard ?? true);
    }

    /// A drive root picked as a folder: its label is the card name the job will use — read it off the UI thread.
```

Najít (4/4):

```csharp
        ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : Layout.Today();

    public string PathPreview => $@"…\{DateString}\<název karty>\";

    public bool Verify
    {
```

Nahradit:

```csharp
        ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : Layout.Today();

    /// "…\2026-10-07\A035_ARRI\" with one card ticked, else "…\2026-10-07\<název karty>\".
    public string PathPreview => SetupTexts.PathPreview(DateString, SelectedSources() is [var only] ? PlannedName(only) : null);

    public bool Verify
    {
```

#### `windows/src/Offload.App/MainViewModel.Space.cs`

Najít (1/1):

```csharp
    void RefreshSpace()
    {
        var targets = SpaceTargets();
        var demands = SelectedSources().SelectMany(p => CardSpace(p, targets)).ToList();
        foreach (var j in Jobs.OfType<JobItemViewModel>()) demands.AddRange(j.SpaceDemands(spaceBaseline.WrittenAtProbe(j)));
        var free = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Destinations)
            if (d.SpaceTarget is { } t && d.FreeBytes is long f) free.TryAdd(t.Volume, f);
        if (folderTarget is { } ft && folderFree is long ff) free.TryAdd(ft.Volume, ff);
        var problems = SpacePlanner.Check(demands, free);
        var problem = problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Blocking)?.Text;
        var warning = problem == null ? problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Warning)?.Text : null;
        if (problem == spaceProblem && warning == spaceWarning) return;
```

Nahradit:

```csharp
    void RefreshSpace()
    {
        var targets = SpaceTargets();
        var selectedDemands = SelectedSources().SelectMany(p => CardSpace(p, targets)).ToList();
        var demands = selectedDemands.ToList();
        foreach (var j in Jobs.OfType<JobItemViewModel>()) demands.AddRange(j.SpaceDemands(spaceBaseline.WrittenAtProbe(j)));
        var free = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Destinations)
            if (d.SpaceTarget is { } t && d.FreeBytes is long f) free.TryAdd(t.Volume, f);
        if (folderTarget is { } ft && folderFree is long ff) free.TryAdd(ft.Volume, ff);
        var problems = SpacePlanner.Check(demands, free);
        // Every destination tile: its bar (what the ticked cards need there) and the check's sentence about it.
        foreach (var d in Destinations)
        {
            if (d.SpaceTarget is not { } t) { d.SetSpace(0, null, null); continue; }
            var severity = DestinationTile.Severity(problems, t.Label);
            var note = problems.Where(p => p.Label.Split(", ").Contains(t.Label, StringComparer.OrdinalIgnoreCase)).OrderByDescending(p => p.Severity).FirstOrDefault()?.Text;
            d.SetSpace(DestinationTile.Need(selectedDemands, t.Volume), severity, note);
        }
        var problem = problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Blocking)?.Text;
        var warning = problem == null ? problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Warning)?.Text : null;
        if (problem == spaceProblem && warning == spaceWarning) return;
```

#### `windows/src/Offload.App/MainViewModel.cs`

Najít (1/1):

```csharp
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    public void SaveNow()
```

Nahradit:

```csharp
    {
        saveTimer.Stop();
        saveTimer.Start();
        RaiseMany(nameof(AfterCopyRows), nameof(AfterCopyLine), nameof(EstimateText));   // step 3 and the estimate show the profile
    }

    public void SaveNow()
```

#### `windows/src/Offload.App/SetupPanel.xaml` (celý soubor nahradit)

```xml
<UserControl x:Class="Offload.App.SetupPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- The left column (UI návrh obr. 1–3): 1 · Karty → 2 · Cíle → 3 · Po zkopírování, the start bar pinned at the bottom.
         Without a card while the queue has jobs (IsCompact) steps 2 and 3 shrink. Every other option is on the settings page. -->
    <UserControl.Resources>
        <!-- The green tick / grey ring of a step 3 row. -->
        <Style x:Key="RowMark" TargetType="Border">
            <Setter Property="Width" Value="20" />
            <Setter Property="Height" Value="20" />
            <Setter Property="CornerRadius" Value="6" />
            <Setter Property="Background" Value="{DynamicResource CheckBg}" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding On}" Value="False">
                    <Setter Property="Background" Value="Transparent" />
                    <Setter Property="CornerRadius" Value="10" />
                    <Setter Property="BorderBrush" Value="{DynamicResource LineStrong}" />
                    <Setter Property="BorderThickness" Value="1.5" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </UserControl.Resources>
    <Border Background="{DynamicResource Panel}" BorderBrush="{DynamicResource Line}" BorderThickness="0,0,1,0">
        <DockPanel x:Name="Root">
            <!-- Start bar (pinned): "1 karta → 2 disky · 1,70 GB" + estimate, the messages, ▶ Spustit offload [Ctrl ↵] -->
            <Border DockPanel.Dock="Bottom" BorderBrush="{DynamicResource Line}" BorderThickness="0,1,0,0" Padding="22,16,22,20" Background="{DynamicResource Panel}">
                <StackPanel>
                    <DockPanel Visibility="{Binding IsCompact, Converter={StaticResource BoolToCollapsed}}">
                        <TextBlock DockPanel.Dock="Right" Text="{Binding EstimateText}" FontSize="13" Foreground="{DynamicResource Text2}"
                                   AutomationProperties.AutomationId="EstimateText" />
                        <TextBlock FontSize="13" Foreground="{DynamicResource Text2}" TextTrimming="CharacterEllipsis" AutomationProperties.AutomationId="StartSummaryText">
                            <Run Text="{Binding StartCounts, Mode=OneWay}" FontWeight="SemiBold" Foreground="{DynamicResource Text}" /><Run Text="{Binding StartSize, Mode=OneWay}" />
                        </TextBlock>
                    </DockPanel>
                    <TextBlock Text="{Binding SpaceProblem}" Style="{StaticResource TextDanger}" Margin="0,10,0,0"
                               Visibility="{Binding SpaceProblem, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="SpaceProblemText" />
                    <TextBlock Text="{Binding SpaceWarning}" Style="{StaticResource TextWarn}" Margin="0,10,0,0"
                               Visibility="{Binding SpaceWarning, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="SpaceWarningText" />
                    <TextBlock Text="{Binding StartStatus}" Style="{StaticResource TextMuted}" Margin="0,10,0,0"
                               Visibility="{Binding StartStatus, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="StartStatusText" />
                    <TextBlock Text="{Binding StartProblem}" Style="{StaticResource TextWarn}" Margin="0,10,0,0"
                               Visibility="{Binding StartProblem, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="StartProblemText" />
                    <Button Style="{StaticResource PrimaryButton}" Command="{Binding StartCommand}" Margin="0,12,0,0" ToolTip="Ctrl+Enter"
                            AutomationProperties.AutomationId="StartButton" AutomationProperties.Name="{Binding StartLabel}">
                        <StackPanel Orientation="Horizontal">
                            <ContentControl Style="{StaticResource IconFill}" Content="{StaticResource IconPlay}" Width="14" Height="14" Margin="0,0,10,0"
                                            Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
                                            Visibility="{Binding IsEnabled, RelativeSource={RelativeSource AncestorType=Button}, Converter={StaticResource BoolToVisible}}" />
                            <TextBlock Text="{Binding StartLabel}" />
                            <Border CornerRadius="5" Background="#29FFFFFF" Padding="6,2" Margin="10,0,0,0" VerticalAlignment="Center"
                                    Visibility="{Binding IsEnabled, RelativeSource={RelativeSource AncestorType=Button}, Converter={StaticResource BoolToVisible}}">
                                <TextBlock Text="Ctrl ↵" FontSize="12" FontWeight="Medium" />
                            </Border>
                        </StackPanel>
                    </Button>
                </StackPanel>
            </Border>

            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel Margin="22,22,22,8">

                    <!-- 1 · KARTY -->
                    <DockPanel>
                        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                            <Button Style="{StaticResource SmallIconButton}" Command="{Binding RefreshVolumesCommand}" ToolTip="Znovu načíst připojené disky"
                                    AutomationProperties.AutomationId="RefreshVolumesButton" AutomationProperties.Name="Znovu načíst připojené disky">
                                <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconRefresh}" Width="14" Height="14" Tag="2"
                                                Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" />
                            </Button>
                            <Button Style="{StaticResource LinkButton}" Content="Ze složky…" Command="{Binding AddSourceCommand}" Margin="8,0,0,0"
                                    AutomationProperties.AutomationId="AddSourceButton" />
                        </StackPanel>
                        <TextBlock Text="1 · KARTY" Style="{StaticResource StepTitle}" />
                    </DockPanel>
                    <ContentControl Style="{StaticResource DashedBox}" Margin="0,10,0,0" Visibility="{Binding HasSources, Converter={StaticResource BoolToCollapsed}}">
                        <StackPanel HorizontalAlignment="Center">
                            <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconCard}" Width="26" Height="26" Tag="1.6"
                                            Foreground="{DynamicResource Text3}" HorizontalAlignment="Center" />
                            <TextBlock Text="{Binding NoCardTitle}" FontWeight="Medium" HorizontalAlignment="Center" Margin="0,8,0,0" AutomationProperties.AutomationId="NoCardTitle" />
                            <TextBlock Text="{Binding NoCardText}" Style="{StaticResource TextMuted}" TextAlignment="Center" Margin="0,8,0,0" />
                        </StackPanel>
                    </ContentControl>
                    <ItemsControl ItemsSource="{Binding Sources}" Margin="0,10,0,0">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border CornerRadius="12" BorderThickness="1" Padding="14" Margin="0,0,0,10">
                                    <Border.Style>
                                        <Style TargetType="Border">
                                            <Setter Property="Background" Value="{DynamicResource RaisedAlt}" />
                                            <Setter Property="BorderBrush" Value="{DynamicResource Line}" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding IsSelected}" Value="True">
                                                    <Setter Property="Background" Value="{DynamicResource Raised}" />
                                                    <Setter Property="BorderBrush" Value="{DynamicResource AccentBorder}" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <StackPanel>
                                        <DockPanel>
                                            <!-- the icon: blue on a selected card, grey otherwise -->
                                            <Border DockPanel.Dock="Left" Width="38" Height="38" CornerRadius="9" VerticalAlignment="Top" Margin="0,0,12,0">
                                                <Border.Style>
                                                    <Style TargetType="Border">
                                                        <Setter Property="Background" Value="{DynamicResource Chip}" />
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding IsSelected}" Value="True">
                                                                <Setter Property="Background" Value="{DynamicResource AccentSoft}" />
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </Border.Style>
                                                <ContentControl Content="{StaticResource IconCard}" Width="20" Height="20">
                                                    <ContentControl.Style>
                                                        <Style TargetType="ContentControl" BasedOn="{StaticResource IconLine}">
                                                            <Setter Property="Foreground" Value="{DynamicResource Text2}" />
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding IsSelected}" Value="True">
                                                                    <Setter Property="Foreground" Value="{DynamicResource AccentText}" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </ContentControl.Style>
                                                </ContentControl>
                                            </Border>
                                            <CheckBox DockPanel.Dock="Right" Style="{StaticResource Check}" IsChecked="{Binding IsSelected}" VerticalAlignment="Top" Margin="8,2,0,0"
                                                      AutomationProperties.Name="{Binding Title}" />
                                            <!-- Doplnit: only the new clips into the earlier backup (spec 2.2) -->
                                            <ToggleButton DockPanel.Dock="Right" Style="{StaticResource WarnSoftButton}" IsChecked="{Binding AppendNewFiles}" VerticalAlignment="Center"
                                                          Content="Doplnit" ToolTip="{Binding AppendText}" Margin="8,0,0,0" AutomationProperties.AutomationId="AppendNewFiles"
                                                          Visibility="{Binding CanAppend, Converter={StaticResource BoolToVisible}}" />
                                            <StackPanel>
                                                <WrapPanel>
                                                    <TextBlock Text="{Binding Title}" FontSize="15" FontWeight="SemiBold" Margin="0,0,8,2" />
                                                    <Border Style="{StaticResource PillBorder}" Padding="7,2" Margin="0,0,0,2" Visibility="{Binding CameraText, Converter={StaticResource NullToCollapsed}}">
                                                        <TextBlock Text="{Binding CameraText}" FontSize="11" FontWeight="SemiBold" Foreground="{DynamicResource TextSoft}"
                                                                   AutomationProperties.AutomationId="CardCameraText" />
                                                    </Border>
                                                </WrapPanel>
                                                <TextBlock Text="{Binding Facts}" Style="{StaticResource TextMuted}" Margin="0,3,0,0" AutomationProperties.AutomationId="CardFactsText" />
                                                <TextBlock Text="{Binding HistoryText}" FontSize="13" Margin="0,3,0,0" TextWrapping="Wrap" AutomationProperties.AutomationId="CardHistoryText"
                                                           Visibility="{Binding HistoryText, Converter={StaticResource NullToCollapsed}}">
                                                    <TextBlock.Style>
                                                        <Style TargetType="TextBlock">
                                                            <Setter Property="Foreground" Value="{DynamicResource Text2}" />
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding HistoryTone}" Value="Ok">
                                                                    <Setter Property="Foreground" Value="{DynamicResource Success}" />
                                                                </DataTrigger>
                                                                <DataTrigger Binding="{Binding HistoryTone}" Value="Warning">
                                                                    <Setter Property="Foreground" Value="{DynamicResource Warn}" />
                                                                </DataTrigger>
                                                                <DataTrigger Binding="{Binding HistoryTone}" Value="Error">
                                                                    <Setter Property="Foreground" Value="{DynamicResource DangerText}" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </TextBlock.Style>
                                                </TextBlock>
                                            </StackPanel>
                                        </DockPanel>
                                        <!-- Selected: the folder name (or, appending, the folder of the earlier backup) -->
                                        <StackPanel Margin="0,12,0,0" Visibility="{Binding IsSelected, Converter={StaticResource BoolToVisible}}">
                                            <TextBlock Text="{Binding AppendFolderText}" Style="{StaticResource TextMono}" FontSize="12"
                                                       Visibility="{Binding AppendFolderText, Converter={StaticResource NullToCollapsed}}" />
                                            <StackPanel Visibility="{Binding AppendNewFiles, Converter={StaticResource BoolToCollapsed}}">
                                                <TextBlock FontSize="12" Foreground="{DynamicResource Text2}" Margin="0,0,0,6" TextTrimming="CharacterEllipsis">
                                                    <Run Text="Název složky" /><Run Text="{Binding NameHintSuffix, Mode=OneWay}" Foreground="{DynamicResource Text3}" />
                                                </TextBlock>
                                                <Grid>
                                                    <TextBox Style="{StaticResource InputBox}" Text="{Binding CardName, UpdateSourceTrigger=PropertyChanged}"
                                                             AutomationProperties.AutomationId="CardNameBox" AutomationProperties.Name="Název složky" />
                                                    <TextBlock Text="{Binding CardNamePlaceholder}" Style="{StaticResource TextMono}" Foreground="{DynamicResource Text3}" Margin="11,0,0,0"
                                                               VerticalAlignment="Center" IsHitTestVisible="False"
                                                               Visibility="{Binding CardName, Converter={StaticResource EmptyToVisible}}" />
                                                </Grid>
                                            </StackPanel>
                                        </StackPanel>
                                    </StackPanel>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <!-- 2 · CÍLE -->
                    <DockPanel Margin="0,16,0,0">
                        <Button DockPanel.Dock="Right" Style="{StaticResource LinkButton}" Command="{Binding AddDestinationCommand}" AutomationProperties.AutomationId="AddDestinationButton"
                                Visibility="{Binding IsCompact, Converter={StaticResource BoolToCollapsed}}">
                            <StackPanel Orientation="Horizontal">
                                <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconPlus}" Width="14" Height="14" Tag="2"
                                                Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" Margin="0,0,4,0" />
                                <TextBlock Text="Přidat disk" />
                            </StackPanel>
                        </Button>
                        <TextBlock Text="2 · CÍLE" Style="{StaticResource StepTitle}" />
                    </DockPanel>
                    <TextBlock Text="Přidejte dva disky — každá karta se zapíše na oba zároveň." Style="{StaticResource TextMuted}" Margin="0,10,0,0"
                               Visibility="{Binding HasNoDestinations, Converter={StaticResource BoolToVisible}}" />
                    <!-- full tiles -->
                    <ItemsControl ItemsSource="{Binding Destinations}" Margin="0,10,0,0" Visibility="{Binding IsCompact, Converter={StaticResource BoolToCollapsed}}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Style="{StaticResource TileBorder}" Margin="0,0,0,10">
                                    <StackPanel>
                                        <DockPanel>
                                            <Border DockPanel.Dock="Left" Style="{StaticResource NumberBadge}" Margin="0,0,12,0">
                                                <TextBlock Text="{Binding Index}" FontSize="12" FontWeight="SemiBold" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                            </Border>
                                            <Button DockPanel.Dock="Right" Style="{StaticResource SmallIconButton}" Command="{Binding RemoveCommand}" ToolTip="Odebrat cíl"
                                                    AutomationProperties.Name="{Binding RemoveName}">
                                                <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconClose}" Width="15" Height="15" Tag="2"
                                                                Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" />
                                            </Button>
                                            <StackPanel>
                                                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" ToolTip="{Binding Root}" />
                                                <TextBlock Text="{Binding FreeText}" Style="{StaticResource TextMuted}" Margin="0,2,0,0" />
                                            </StackPanel>
                                        </DockPanel>
                                        <!-- the space bar: grey = used, blue = what the ticked cards need (yellow / red with the space check) -->
                                        <Border CornerRadius="3" Height="6" Background="{DynamicResource Track}" Margin="0,10,0,0" ClipToBounds="True"
                                                Visibility="{Binding HasBar, Converter={StaticResource BoolToVisible}}">
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="{Binding BarUsed}" />
                                                    <ColumnDefinition Width="{Binding BarNeed}" />
                                                    <ColumnDefinition Width="{Binding BarRest}" />
                                                </Grid.ColumnDefinitions>
                                                <Border Background="{DynamicResource DiskUsed}" />
                                                <Border Grid.Column="1">
                                                    <Border.Style>
                                                        <Style TargetType="Border">
                                                            <Setter Property="Background" Value="{DynamicResource AccentBar}" />
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding BarTone}" Value="Warning">
                                                                    <Setter Property="Background" Value="{DynamicResource WarnFill}" />
                                                                </DataTrigger>
                                                                <DataTrigger Binding="{Binding BarTone}" Value="Blocking">
                                                                    <Setter Property="Background" Value="{DynamicResource Danger}" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </Border.Style>
                                                </Border>
                                            </Grid>
                                        </Border>
                                        <TextBlock Text="{Binding SpaceNote}" Margin="0,8,0,0" Visibility="{Binding SpaceNote, Converter={StaticResource NullToCollapsed}}">
                                            <TextBlock.Style>
                                                <Style TargetType="TextBlock" BasedOn="{StaticResource TextWarn}">
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding BarTone}" Value="Blocking">
                                                            <Setter Property="Foreground" Value="{DynamicResource DangerText}" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </TextBlock.Style>
                                        </TextBlock>
                                        <TextBlock Text="Disk není připojen" Style="{StaticResource TextDanger}" Margin="0,8,0,0" Visibility="{Binding ShowMissing, Converter={StaticResource BoolToVisible}}" />
                                        <TextBlock Text="{Binding FsWarning}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding FsWarning, Converter={StaticResource NullToCollapsed}}" />
                                    </StackPanel>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <!-- compact list (obr. 2, 3) -->
                    <Border Style="{StaticResource ListBorder}" Margin="0,10,0,0" BorderBrush="{DynamicResource Line}"
                            Visibility="{Binding IsCompact, Converter={StaticResource BoolToVisible}}">
                        <ItemsControl ItemsSource="{Binding Destinations}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border BorderBrush="{DynamicResource LineSoft}" BorderThickness="0,0,0,1" Padding="14,11">
                                        <DockPanel>
                                            <Border DockPanel.Dock="Left" Style="{StaticResource NumberBadge}" Width="22" Height="22" CornerRadius="11" Margin="0,0,12,0">
                                                <TextBlock Text="{Binding Index}" FontSize="12" FontWeight="SemiBold" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                            </Border>
                                            <TextBlock DockPanel.Dock="Right" Text="{Binding CompactFreeText}" FontSize="13" VerticalAlignment="Center">
                                                <TextBlock.Style>
                                                    <Style TargetType="TextBlock">
                                                        <Setter Property="Foreground" Value="{DynamicResource Text2}" />
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding BarTone}" Value="Warning">
                                                                <Setter Property="Foreground" Value="{DynamicResource Warn}" />
                                                            </DataTrigger>
                                                            <DataTrigger Binding="{Binding BarTone}" Value="Blocking">
                                                                <Setter Property="Foreground" Value="{DynamicResource DangerText}" />
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </TextBlock.Style>
                                            </TextBlock>
                                            <TextBlock Text="{Binding Name}" FontWeight="Medium" VerticalAlignment="Center" />
                                        </DockPanel>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </Border>
                    <TextBlock Text="{Binding PathPreview}" Style="{StaticResource TextMonoFaint}" Margin="2,6,2,0" Visibility="{Binding IsCompact, Converter={StaticResource BoolToCollapsed}}"
                               AutomationProperties.AutomationId="PathPreviewText" />
                    <TextBlock Text="Jen jeden cíl — doporučeny jsou dvě nezávislé kopie." Style="{StaticResource TextWarn}" Margin="0,8,0,0"
                               Visibility="{Binding SingleDestination, Converter={StaticResource BoolToVisible}}" />

                    <!-- 3 · PO ZKOPÍROVÁNÍ -->
                    <DockPanel Margin="0,26,0,0">
                        <Button DockPanel.Dock="Right" Style="{StaticResource LinkButton}" Content="Upravit projekt" Command="{Binding OpenSettingsCommand}"
                                AutomationProperties.AutomationId="EditProjectButton" Visibility="{Binding IsCompact, Converter={StaticResource BoolToCollapsed}}" />
                        <TextBlock Text="3 · PO ZKOPÍROVÁNÍ" Style="{StaticResource StepTitle}" />
                    </DockPanel>
                    <Border Style="{StaticResource ListBorder}" BorderBrush="{DynamicResource Line}" Margin="0,10,0,0"
                            Visibility="{Binding IsCompact, Converter={StaticResource BoolToCollapsed}}">
                        <ItemsControl ItemsSource="{Binding AfterCopyRows}" AutomationProperties.AutomationId="AfterCopyRows">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border BorderBrush="{DynamicResource LineSoft}" BorderThickness="0,0,0,1" Padding="14,11">
                                        <StackPanel>
                                            <DockPanel>
                                                <Border DockPanel.Dock="Left" Style="{StaticResource RowMark}" Margin="0,0,12,0">
                                                    <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconCheck}" Width="13" Height="13" Tag="2.6"
                                                                    Foreground="{DynamicResource Success}" Visibility="{Binding On, Converter={StaticResource BoolToVisible}}" />
                                                </Border>
                                                <TextBlock DockPanel.Dock="Right" Text="{Binding Value}" FontSize="12" Foreground="{DynamicResource Text2}" VerticalAlignment="Center" />
                                                <TextBlock Text="{Binding Label}" VerticalAlignment="Center">
                                                    <TextBlock.Style>
                                                        <Style TargetType="TextBlock">
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding Warn}" Value="True">
                                                                    <Setter Property="Foreground" Value="{DynamicResource Warn}" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </TextBlock.Style>
                                                </TextBlock>
                                            </DockPanel>
                                            <TextBlock Text="{Binding DataContext.VerifyOffWarning, RelativeSource={RelativeSource AncestorType=ItemsControl}}" Style="{StaticResource TextWarn}"
                                                       FontSize="12" Margin="32,6,0,0" Visibility="{Binding Warn, Converter={StaticResource BoolToVisible}}" />
                                        </StackPanel>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </Border>
                    <TextBlock Text="{Binding AfterCopyLine}" Foreground="{DynamicResource Text2}" TextWrapping="Wrap" LineHeight="22" Margin="0,10,0,0"
                               Visibility="{Binding IsCompact, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="AfterCopyLine" />
                    <!-- What blocks a start or is skipped, from the profile's checks (red / orange). -->
                    <TextBlock Text="{Binding LutProblem}" Style="{StaticResource TextDanger}" Margin="0,10,0,0" Visibility="{Binding LutProblem, Converter={StaticResource NullToCollapsed}}" />
                    <TextBlock Text="{Binding ProxyFolderProblem}" Style="{StaticResource TextDanger}" Margin="0,10,0,0" Visibility="{Binding ProxyFolderProblem, Converter={StaticResource NullToCollapsed}}" />
                    <TextBlock Text="{Binding ToolsWarning}" Style="{StaticResource TextWarn}" Margin="0,10,0,0" Visibility="{Binding ToolsWarning, Converter={StaticResource NullToCollapsed}}" />
                </StackPanel>
            </ScrollViewer>
        </DockPanel>
    </Border>
</UserControl>
```

#### `windows/src/Offload.App/SourceItemViewModel.cs`

Najít (1/3):

```csharp
    /// "podle názvů klipů (A001C001…)", "další v řadě po A001 (dnes)", "Na kartě je víc cívek: …" — or null.
    public string? NameHint => owner.CardName(path).Trim().Length > 0 ? null : owner.Placeholder(path).Hint;

    /// "Zálohováno … — ověřeno" (spec 2.1), or null for a card history does not know.
    public string? HistoryText => owner.HistoryLine(path).Text;
    public HistoryTone HistoryTone => owner.HistoryLine(path).Tone;

    /// A card inspection finished: the history line (and what depends on it) may have changed.
    internal void InsightChanged() => RaiseMany(nameof(HistoryText), nameof(HistoryTone), nameof(CardNamePlaceholder), nameof(NameHint),
        nameof(AppendText), nameof(AppendNewFiles), nameof(AppendFolderText));

    /// "Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)", or null when there is nothing to append.
    public string? AppendText => owner.AppendOffer(path);
```

Nahradit:

```csharp
    /// "podle názvů klipů (A001C001…)", "další v řadě po A001 (dnes)", "Na kartě je víc cívek: …" — or null.
    public string? NameHint => owner.CardName(path).Trim().Length > 0 ? null : owner.Placeholder(path).Hint;

    /// " — podle názvů klipů (A001C001…)" after "Název složky" (UI návrh obr. 1), or nothing.
    public string NameHintSuffix => NameHint is { } hint ? $" — {hint}" : "";

    /// "24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s".
    public string Facts => owner.SourceFacts(path);

    /// "ALEXA 35 · LogC4" from one clip of the card, or null.
    public string? CameraText => owner.Insight(path)?.Camera?.Text;

    /// "Doplnit" is offered (new clips, the earlier backup reachable).
    public bool CanAppend => AppendText != null;

    /// The disk's connection was looked up (off the UI thread).
    internal void FactsChanged() => Raise(nameof(Facts));

    /// "Zálohováno … — ověřeno" (spec 2.1), or null for a card history does not know.
    public string? HistoryText => owner.HistoryLine(path).Text;
    public HistoryTone HistoryTone => owner.HistoryLine(path).Tone;

    /// A card inspection finished: the history line (and what depends on it) may have changed.
    internal void InsightChanged() => RaiseMany(nameof(HistoryText), nameof(HistoryTone), nameof(CardNamePlaceholder), nameof(NameHint),
        nameof(NameHintSuffix), nameof(AppendText), nameof(AppendNewFiles), nameof(AppendFolderText), nameof(Facts), nameof(CameraText), nameof(CanAppend));

    /// "Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)", or null when there is nothing to append.
    public string? AppendText => owner.AppendOffer(path);
```

Najít (2/3):

```csharp
        set
        {
            owner.SetAppend(path, value);
            RaiseMany(nameof(AppendNewFiles), nameof(AppendFolderText));
        }
    }
```

Nahradit:

```csharp
        set
        {
            owner.SetAppend(path, value);
            if (value && !IsSelected) IsSelected = true;   // "Doplnit" on the tile also ticks the card
            RaiseMany(nameof(AppendNewFiles), nameof(AppendFolderText));
        }
    }
```

Najít (3/3):

```csharp
    public string? AppendFolderText => owner.AppendFolder(path);

    /// The date folder or the naming option changed: the suggested name may be another.
    internal void NamingChanged() => RaiseMany(nameof(CardNamePlaceholder), nameof(NameHint));

    public bool IsSelected
    {
```

Nahradit:

```csharp
    public string? AppendFolderText => owner.AppendFolder(path);

    /// The date folder or the naming option changed: the suggested name may be another.
    internal void NamingChanged() => RaiseMany(nameof(CardNamePlaceholder), nameof(NameHint), nameof(NameHintSuffix));

    public bool IsSelected
    {
```

#### `windows/src/Offload.Core/Desktop/SetupTexts.cs`

Najít (1/1):

```csharp

    public const string VerifyOffWarning = "Bez ověření není jisté, že data na disku jsou v pořádku.";

    /// "1 karta → 2 disky" (bold in the bar) and " · 1,70 GB" (null while a size is not known); nothing ticked: "Vyberte kartu".
    public static (string Counts, string? Size) StartSummary(int cards, int disks, long? bytes)
    {
```

Nahradit:

```csharp

    public const string VerifyOffWarning = "Bez ověření není jisté, že data na disku jsou v pořádku.";

    /// The card tile's line (obr. 1): "24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s". Before the card is looked at,
    /// the used space of its volume stands for the size; a disk that is not a camera card says so.
    public static string CardFacts(History.CardInsight? insight, long? usedOnVolume, string? filesystem, string? connection, bool looksLikeCard)
    {
        var parts = new List<string>();
        if (insight is { Error: null } i)
        {
            parts.Add(i.Clips.Count > 0 ? Texts.Clips(i.Clips.Count) : $"{i.FileCount} {Texts.Plural(i.FileCount, "soubor", "soubory", "souborů")}");
            parts.Add(Format.BytesCs3(i.TotalBytes));
        }
        else if (usedOnVolume is long used) parts.Add(Format.BytesCs3(used));
        if (!string.IsNullOrEmpty(filesystem)) parts.Add(filesystem);
        if (connection != null) parts.Add(connection);
        if (!looksLikeCard) parts.Add("nevypadá jako karta");
        return string.Join(" · ", parts);
    }

    /// The size of everything ticked, or null while one card's size is not known (the bar then shows only the counts).
    public static long? Total(IEnumerable<long?> sizes)
    {
        long sum = 0;
        foreach (var s in sizes)
        {
            if (s is not long b) return null;
            sum += b;
        }
        return sum;
    }

    /// "1 karta → 2 disky" (bold in the bar) and " · 1,70 GB" (null while a size is not known); nothing ticked: "Vyberte kartu".
    public static (string Counts, string? Size) StartSummary(int cards, int disks, long? bytes)
    {
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CardTileTests"`

Expected: PASS (`Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 upozornění, 0 chyb`; celá sada `Neúspěšné: 0` (časově citlivé `ProxyWatchdogTests` / `ProxyRendererTests.Feeder_*` při jednom selhání pusťte znovu samostatně — viz Global Constraints).

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)`.

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1):

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 12 — stránka Nastavení (postup 6, předsunutý před levý sloupec, aby žádná volba nebyla ani na jeden commit nedostupná): ikona s posuvníky v liště nebo Ctrl+, otevře místo obou sloupců stránku s lištou „‹ Zpět · Projekt <název> · Uložit jako nový projekt… · Hotovo“ (Esc zavře) a sekcemi Struktura složek (cesta, vlastní datum, název podle klipů) · Ověření (vypínač, kontrolní součet, MD5, Ověřit zálohu… + „Ukládat report ověření“) · Analýza a náhledy · **Proxy** podle obr. 4 (vypínač, segmenty Kodek a Výška, datový tok s „≈ 75 MB / minutu“, výřez, LUT + tabulka vestavěných LUTů s „použito“, „Když kameru nepoznám“, Pravidla LUTů…, Vlastní .cube…, zvuk, kamerové proxy, kopie na všechny cíle, složka pro proxy; vpravo šedý náhled burn-inu 16:9 s názvem klipu, TC a frame lines výřezu) · Report (CSV, ALE, denní report po kartě, Denní report…) · Oznámení (souhrn + Upravit oznámení…) · Dekodéry RAW (nástroje, Gen 4 → Gen 5) · Aplikace (verze, Aktualizace…, Prověřit USB porty…); úpravy jdou do zobrazeného profilu jako dosud (`SettingsTexts`: řádky LUTů, tok za minutu, rám výřezu, souhrn oznámení)
- Úkol 11 — horní lišta (postup 2) místo menu: logo a „Offload“, přepínač **Projekt** na místě přepínače presetu (rozbalovací seznam projektů s odškrtnutím aktivního, „Uložit jako nový projekt…“, „Přejmenovat…“, „Smazat…“; zamčený, dokud Start kontroluje — I1), vpravo odkaz „Nová verze …“, pilulka „● Běží 1 offload · 1 ve frontě“ (`QueueSummary`, pozastavené zvlášť), „Ověřit zálohu“ (Ctrl+Shift+O), „Hardware“ s tečkou (zelená = porty bez varování, žlutá = doporučení, žádná před prvním testem; `TopBar.Dot`; Ctrl+U) a tlačítko s posuvníky (zatím nabídka Denní report…, Oznámení…, Aktualizace…, Ukládat report ověření — příští úkol je přesune do Nastavení); okno 1360 × 900 (min 940 × 640 beze změny); přepínač „Projekt:“ z levého panelu je pryč
- Úkol 10 — téma (postup 1): barevné tokeny `Themes/Dark.xaml` (barvy z návrhu) a `Themes/Light.xaml` (odvozená světlá sada, stejné klíče), styly `Themes/Offload.xaml` (`StepTitle`, `TextMuted`, `TextFaint`, `TextMono`, `TileBorder`, `PillBorder`, `DashedBox`, tlačítka `PrimaryButton` / `SecondaryButton` / `GhostButton` / `LinkButton` / `DangerButton` / `SuccessButton` / `WarnButton` / `WarnSoftButton`, záložky, segmenty, `Check`, `Switch`, `InputBox`, `ProgressThin`) a čárové ikony z mockupu `Themes/Icons.xaml`; písmo Segoe UI Variable / Segoe UI a Cascadia Mono / Consolas (IBM Plex se nepřibaluje); `ThemeWatcher` vybere sadu podle „Režim aplikací“ ve Windows a přepne ji při změně (`ThemeChoice`, proměnná `OFFLOAD_THEME=dark|light` jen pro snímky); test: stejné klíče, tmavá sada = návrh, kontrast textů ≥ 4,5 : 1 v obou sadách, všechny slovníky se načtou (`XamlReader`; testy referencují WPF z .NET, ne `Offload.App`)
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 13 — levý sloupec (postup 3): kroky „1 · KARTY“ (⟳ a „Ze složky…“, prázdný stav v přerušovaném rámečku „Vložte kartu do čtečky“ / během offloadu „Vložte další kartu“, dlaždice karty: ikona, název, štítek kamery, „24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s“, řádek historie, zaškrtávátko, žluté „Doplnit“ u karty s novými klipy — zaškrtne i kartu, pole „Název složky — <nápověda>“ v monospace), „2 · CÍLE“ (+ Přidat disk, dlaždice s kolečkem, „7,20 TB volných z 8 TB · NTFS · NVMe“, ✕, pruh místa šedá / modrá / žlutá / červená a věta kontroly místa pod ním, exFAT, nepřipojený disk, náhled cesty „…\2026-10-07\A035_ARRI\“, jen jeden cíl) a „3 · PO ZKOPÍROVÁNÍ“ (4 řádky se zeleným odškrtnutím, vypnuté ověření žlutě, „Upravit projekt“ otevře Nastavení, pod tím červené / oranžové důvody, proč nejde start); připnutá spodní lišta „**1 karta → 2 disky** · 1,70 GB“ + odhad, hlášky místa a startu, „▶ Spustit offload [Ctrl ↵]“ / „Zařadit do fronty“; bez karty a s úlohami ve frontě kompaktní kroky 2 a 3 (obr. 2, 3); spojení karet se zjišťuje mimo UI vlákno, kamera s prověřením karty; volby Struktura, Proxy, Dekodéry a Hardware z levého panelu zmizely (jsou v Nastavení a v liště); `SetupTexts.CardFacts`, `Total`
- Úkol 12 — stránka Nastavení (postup 6, předsunutý před levý sloupec, aby žádná volba nebyla ani na jeden commit nedostupná): ikona s posuvníky v liště nebo Ctrl+, otevře místo obou sloupců stránku s lištou „‹ Zpět · Projekt <název> · Uložit jako nový projekt… · Hotovo“ (Esc zavře) a sekcemi Struktura složek (cesta, vlastní datum, název podle klipů) · Ověření (vypínač, kontrolní součet, MD5, Ověřit zálohu… + „Ukládat report ověření“) · Analýza a náhledy · **Proxy** podle obr. 4 (vypínač, segmenty Kodek a Výška, datový tok s „≈ 75 MB / minutu“, výřez, LUT + tabulka vestavěných LUTů s „použito“, „Když kameru nepoznám“, Pravidla LUTů…, Vlastní .cube…, zvuk, kamerové proxy, kopie na všechny cíle, složka pro proxy; vpravo šedý náhled burn-inu 16:9 s názvem klipu, TC a frame lines výřezu) · Report (CSV, ALE, denní report po kartě, Denní report…) · Oznámení (souhrn + Upravit oznámení…) · Dekodéry RAW (nástroje, Gen 4 → Gen 5) · Aplikace (verze, Aktualizace…, Prověřit USB porty…); úpravy jdou do zobrazeného profilu jako dosud (`SettingsTexts`: řádky LUTů, tok za minutu, rám výřezu, souhrn oznámení)
- Úkol 11 — horní lišta (postup 2) místo menu: logo a „Offload“, přepínač **Projekt** na místě přepínače presetu (rozbalovací seznam projektů s odškrtnutím aktivního, „Uložit jako nový projekt…“, „Přejmenovat…“, „Smazat…“; zamčený, dokud Start kontroluje — I1), vpravo odkaz „Nová verze …“, pilulka „● Běží 1 offload · 1 ve frontě“ (`QueueSummary`, pozastavené zvlášť), „Ověřit zálohu“ (Ctrl+Shift+O), „Hardware“ s tečkou (zelená = porty bez varování, žlutá = doporučení, žádná před prvním testem; `TopBar.Dot`; Ctrl+U) a tlačítko s posuvníky (zatím nabídka Denní report…, Oznámení…, Aktualizace…, Ukládat report ověření — příští úkol je přesune do Nastavení); okno 1360 × 900 (min 940 × 640 beze změny); přepínač „Projekt:“ z levého panelu je pryč
- Úkol 10 — téma (postup 1): barevné tokeny `Themes/Dark.xaml` (barvy z návrhu) a `Themes/Light.xaml` (odvozená světlá sada, stejné klíče), styly `Themes/Offload.xaml` (`StepTitle`, `TextMuted`, `TextFaint`, `TextMono`, `TileBorder`, `PillBorder`, `DashedBox`, tlačítka `PrimaryButton` / `SecondaryButton` / `GhostButton` / `LinkButton` / `DangerButton` / `SuccessButton` / `WarnButton` / `WarnSoftButton`, záložky, segmenty, `Check`, `Switch`, `InputBox`, `ProgressThin`) a čárové ikony z mockupu `Themes/Icons.xaml`; písmo Segoe UI Variable / Segoe UI a Cascadia Mono / Consolas (IBM Plex se nepřibaluje); `ThemeWatcher` vybere sadu podle „Režim aplikací“ ve Windows a přepne ji při změně (`ThemeChoice`, proměnná `OFFLOAD_THEME=dark|light` jen pro snímky); test: stejné klíče, tmavá sada = návrh, kontrast textů ≥ 4,5 : 1 v obou sadách, všechny slovníky se načtou (`XamlReader`; testy referencují WPF z .NET, ne `Offload.App`)
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: left column with steps 1-3 and the pinned start bar (plan 9, task 13)

Card tiles with camera label and connection, destination tiles with the space bar, step 3 as a
summary of the project with "Upravit projekt", the pinned start bar with "1 karta -> 2 disky", size
and estimate; compact steps while jobs run without a card. Links are resolved off the UI thread.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.App\DestinationViewModel.cs windows\src\Offload.App\JobItemViewModel.cs windows\src\Offload.App\MainViewModel.History.cs windows\src\Offload.App\MainViewModel.Queue.cs windows\src\Offload.App\MainViewModel.Setup.cs windows\src\Offload.App\MainViewModel.Space.cs windows\src\Offload.App\MainViewModel.cs windows\src\Offload.App\SetupPanel.xaml windows\src\Offload.App\SourceItemViewModel.cs windows\src\Offload.Core\Desktop\SetupTexts.cs windows\tests\Offload.Core.Tests\CardTileTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 14: Fronta se stávajícími daty (postup 4, 1. část)

`QueuePanel.xaml` se přepíše: prázdná fronta jako karta s ikonami toku, karta úlohy mění vzhled podle
`JobCard.Headline` (čeká / běží / zelená / žlutá / červená / šedá) s dosavadními daty, ověření zálohy ve stejném stylu,
„Fronta 2“ a „Vyčistit hotové“. Logika je v jádru (úkoly 6 a 9), proto úkol nemá vlastní test — kontrolou je build
s `-warnaserror` a celá sada; vzhled ověří úkol 19.

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.App/JobItemViewModel.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs`
- Modify: `windows/src/Offload.App/MainViewModel.TopBar.cs`
- Modify: `windows/src/Offload.App/MainViewModel.cs`
- Modify: `windows/src/Offload.App/QueuePanel.xaml`

**Interfaces:**
- Consumes: `JobCard.Headline`, `JobCard.Sub`, `JobCardFacts`, `QueueSummary` (úkol 6), `SetupTexts.QueueEmpty`, `Flow`, `PathPreview` (úkol 9).
- Produces:
  - `JobItemViewModel`: `Kind` (`JobCardKind`), `HeadTitle`, `HeadSub`, `DestNames` (internal), `Sub`, `TitleName`, `TitleSuffix`, `PercentText`, `PathText`; konstruktor s `JobCardFacts? facts = null`.
  - `MainViewModel`: `FlowSteps` (`IReadOnlyList<FlowStep>`), `QueueEmptyTitle`, `QueueEmptyText`, `QueueBadgeText`; `record FlowStep(string Label, string Icon, bool First)`.
  - AutomationId: `QueueEmptyTitle`, `QueueTab`, `ClearFinishedButton`, `JobHeadTitle`, `JobHeadSub`, `JobPercentText`, `JobStatusText`, `JobNoteText`, `JobWarningText`, `JobProfileNote`, `CancelButton`, `EjectButton`, `ReportButton`, `ExplorerButton`, `VerifyStatusText`, `VerifyResultText`, `VerifyReportNote`, `VerifyReportButton`, `VerifyExplorerButton`, `VerifyCancelButton`.

- [ ] **Step 1: Implementace**

GUI se při tomto úkolu **nespouští** (vzhled zkontroluje úkol 19).

#### `windows/src/Offload.App/JobItemViewModel.cs`

Najít (1/2):

```csharp
    bool chainRequested, ejected, ejecting, openingReport;

    readonly IReadOnlyList<VolumeDemand> space;

    /// `space`: what the card needs on each destination volume (spec 2.5) — counted while this job is queued or copies.
    /// `profileNote`: orange, for the whole life of the card — e.g. an append whose project no longer exists.
    public JobItemViewModel(MainViewModel owner, OffloadJob job, string sourceName, bool canEject, IReadOnlyList<VolumeDemand>? space = null,
        string? profileNote = null)
    {
        this.owner = owner;
        ProfileNote = profileNote;
        this.sourceName = sourceName;
        this.space = space ?? [];
```

Nahradit:

```csharp
    bool chainRequested, ejected, ejecting, openingReport;

    readonly IReadOnlyList<VolumeDemand> space;
    // What the card header says before the plan exists (from the look at the card when it was started).
    readonly JobCardFacts? facts;
    CardHeadline headline;
    IReadOnlyList<string>? destNames;
    int? clipCount;

    /// `space`: what the card needs on each destination volume (spec 2.5) — counted while this job is queued or copies.
    /// `profileNote`: orange, for the whole life of the card — e.g. an append whose project no longer exists.
    /// `facts`: the clips, size and camera the card had when it was started (the header while the job waits).
    public JobItemViewModel(MainViewModel owner, OffloadJob job, string sourceName, bool canEject, IReadOnlyList<VolumeDemand>? space = null,
        string? profileNote = null, JobCardFacts? facts = null)
    {
        this.owner = owner;
        this.facts = facts;
        headline = new CardHeadline(JobCardKind.Waiting, sourceName, "");
        ProfileNote = profileNote;
        this.sourceName = sourceName;
        this.space = space ?? [];
```

Najít (2/2):

```csharp
            chainRequested = true;
            ResolveChain(plan);
        }
        Raise(string.Empty);   // every binding of the card may have changed
    }

    /// Where the card and each destination are connected — resolved once, off the UI thread.
    void ResolveChain(OffloadPlan plan)
    {
```

Nahradit:

```csharp
            chainRequested = true;
            ResolveChain(plan);
        }
        headline = JobCard.Headline(CardName, progress.Status, Job.Report, DestNames);
        Raise(string.Empty);   // every binding of the card may have changed
    }

    // ── The new card (UI návrh obr. 2, 3) ──────────────────────────────────────────────────────────────────────
    /// Waiting, Running, Done (green), Problem (yellow), Failed (red), Cancelled (grey) — which look the card has.
    public JobCardKind Kind => headline.Kind;
    /// "A035_ARRI — kartu lze vysunout" / "B012_FX6 — kartu zatím nevysunujte" …
    public string HeadTitle => headline.Title;
    /// "Všech 24 souborů je na 2 discích a ověřených (xxHash64)." / "1 soubor na disku SHUTTLE_B nesouhlasí s kartou. …"
    public string HeadSub => headline.Sub;

    /// The destinations as the card names them ("RAID_A").
    internal IReadOnlyList<string> DestNames => destNames ??= (Job.Plan?.DestRoots ?? Job.Config.DestRoots).Select(JobCard.DiskName).ToList();

    /// "24 klipů · 1,70 GB · ALEXA 35"; before the plan, what the card had when it was started.
    public string Sub
    {
        get
        {
            if (Job.Plan is { } p)
            {
                clipCount ??= ClipDetector.Detect(p.Scan.Files).Count;
                return JobCard.Sub(new JobCardFacts(p.Scan.Files.Count, clipCount.Value, p.Scan.TotalBytes, facts?.Camera, p.Mode == PlanMode.Append));
            }
            return facts is { } f ? JobCard.Sub(f) : FilesText;
        }
    }

    /// The waiting row: "B012_FX6" + " — doplnění" (or " — oprava").
    public string TitleName => CardName;
    public string TitleSuffix => Job.Config.AppendInto != null ? " — doplnění" : Job.Config.RepairInto != null ? " — oprava" : "";

    /// "38 %" — the phase's share (the indicator shows which phase).
    public string PercentText => $"{Math.Round(Progress * 100):0} %";

    /// "…\2026-10-07\A035_ARRI\" on the finished card.
    public string PathText => Job.Plan is { } plan ? SetupTexts.PathPreview(plan.Date, plan.CardName) : "";

    /// Where the card and each destination are connected — resolved once, off the UI thread.
    void ResolveChain(OffloadPlan plan)
    {
```

#### `windows/src/Offload.App/MainViewModel.Queue.cs`

Najít (1/4):

```csharp
        ticker.Tick += (_, _) => Tick();
        ticker.Start();
        SetupChanged += () => RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart), nameof(StartProblem),
            nameof(StartCounts), nameof(StartSize), nameof(EstimateText), nameof(AfterCopyRows), nameof(AfterCopyLine), nameof(PathPreview));
        RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart));
    }
```

Nahradit:

```csharp
        ticker.Tick += (_, _) => Tick();
        ticker.Start();
        SetupChanged += () => RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart), nameof(StartProblem),
            nameof(StartCounts), nameof(StartSize), nameof(EstimateText), nameof(AfterCopyRows), nameof(AfterCopyLine), nameof(PathPreview),
            nameof(FlowSteps), nameof(QueueEmptyTitle), nameof(QueueEmptyText));
        RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart));
    }
```

Najít (2/4):

```csharp
    public string AfterCopyLine => SetupTexts.AfterCopyLine(Settings);
    public string VerifyOffWarning => SetupTexts.VerifyOffWarning;

    /// The parts of the left column that follow the queue (checked every tick, raised only when they change).
    void RefreshLayout()
    {
```

Nahradit:

```csharp
    public string AfterCopyLine => SetupTexts.AfterCopyLine(Settings);
    public string VerifyOffWarning => SetupTexts.VerifyOffWarning;

    // ── The empty queue (obr. 1) ───────────────────────────────────────────────────────────────────────────────
    /// Karta → 2 disky naráz → Ověření → Proxy → Report (only what the profile shown does).
    public IReadOnlyList<FlowStep> FlowSteps => SetupTexts.Flow(Settings.DestRoots.Count, Settings.Verify, Settings.AnalyzeClips, Settings.Proxy.Enabled)
        .Select((label, i) => new FlowStep(label, i switch
        {
            0 => "card",
            1 => "disk",
            _ => label switch { "Ověření" => "verify", "Proxy" or "Analýza" => "proxy", _ => "report" },
        }, i == 0)).ToList();

    /// "Připraveno — A035_ARRI na 2 disky" with a ticked card, else "Zatím žádný offload".
    public string QueueEmptyTitle => SetupTexts.QueueEmpty(SelectedSources().Select(PlannedName).ToList(), Settings.DestRoots.Count, Settings.Verify).Title;
    public string QueueEmptyText => SetupTexts.QueueEmpty(SelectedSources().Select(PlannedName).ToList(), Settings.DestRoots.Count, Settings.Verify).Text;

    /// " 2" after "Fronta" (the unfinished jobs), nothing for none.
    public string QueueBadgeText => QueueBadge > 0 ? $" {QueueBadge}" : "";

    /// The parts of the left column that follow the queue (checked every tick, raised only when they change).
    void RefreshLayout()
    {
```

Najít (3/4):

```csharp
                    AppLog.Write($"append {src}: project {known.Entry.Project} no longer exists — the Bez projektu settings are used");
                    profileNote = AppendRules.GoneNote(known.Entry.Project!);
                }
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), AppendRules.Title(known.Entry.CardName),
                    canEject: isVolume && CardEject.CanEject(src), space, profileNote);
                Jobs.Insert(0, appendItem);
                selected.Remove(src);
                appendTicks.Untick(src);
```

Nahradit:

```csharp
                    AppLog.Write($"append {src}: project {known.Entry.Project} no longer exists — the Bez projektu settings are used");
                    profileNote = AppendRules.GoneNote(known.Entry.Project!);
                }
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), known.Entry.CardName,
                    canEject: isVolume && CardEject.CanEject(src), space, profileNote,
                    new JobCardFacts(known.New.Count, ClipDetector.Detect(known.New).Count, known.NewBytes, null, Append: true));
                Jobs.Insert(0, appendItem);
                selected.Remove(src);
                appendTicks.Untick(src);
```

Najít (4/4):

```csharp
            // The profile shown now, as the job's own copy (a later switch or edit does not change it), and its project.
            var config = JobConfigs.Full(AllSettings, src, name, DateString);
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
                canEject: isVolume && CardEject.CanEject(src), space);
            Jobs.Insert(0, item);
            selected.Remove(src);
            queue.Enqueue(item.Runner);
```

Nahradit:

```csharp
            // The profile shown now, as the job's own copy (a later switch or edit does not change it), and its project.
            var config = JobConfigs.Full(AllSettings, src, name, DateString);
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
                canEject: isVolume && CardEject.CanEject(src), space, facts: Insight(src) is { Error: null } look
                    ? new JobCardFacts(look.FileCount, look.Clips.Count, look.TotalBytes, look.Camera?.Camera, Append: false) : null);
            Jobs.Insert(0, item);
            selected.Remove(src);
            queue.Enqueue(item.Runner);
```

#### `windows/src/Offload.App/MainViewModel.TopBar.cs`

Najít (1/2):

```csharp

namespace Offload.App;

/// One project in the switcher's list: its name, whether it is shown now, and the command that switches to it.
public sealed record ProjectItem(string Name, bool IsActive, RelayCommand Select);
```

Nahradit:

```csharp

namespace Offload.App;

/// One icon of the empty queue's flow: its label, which icon (card, disk, verify, proxy, report), the first is blue.
public sealed record FlowStep(string Label, string Icon, bool First);

/// One project in the switcher's list: its name, whether it is shown now, and the command that switches to it.
public sealed record ProjectItem(string Name, bool IsActive, RelayCommand Select);
```

Najít (2/2):

```csharp
        if (pill == queuePill && badge == queueBadge) return;
        queuePill = pill;
        queueBadge = badge;
        RaiseMany(nameof(QueuePill), nameof(QueueBadge));
    }
}
```

Nahradit:

```csharp
        if (pill == queuePill && badge == queueBadge) return;
        queuePill = pill;
        queueBadge = badge;
        RaiseMany(nameof(QueuePill), nameof(QueueBadge), nameof(QueueBadgeText));
    }
}
```

#### `windows/src/Offload.App/MainViewModel.cs`

Najít (1/1):

```csharp
    {
        saveTimer.Stop();
        saveTimer.Start();
        RaiseMany(nameof(AfterCopyRows), nameof(AfterCopyLine), nameof(EstimateText));   // step 3 and the estimate show the profile
    }

    public void SaveNow()
```

Nahradit:

```csharp
    {
        saveTimer.Stop();
        saveTimer.Start();
        RaiseMany(nameof(AfterCopyRows), nameof(AfterCopyLine), nameof(EstimateText), nameof(FlowSteps), nameof(QueueEmptyText));   // they show the profile
    }

    public void SaveNow()
```

#### `windows/src/Offload.App/QueuePanel.xaml` (celý soubor nahradit)

```xml
<UserControl x:Class="Offload.App.QueuePanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Offload.App">
    <!-- The right column (UI návrh obr. 1–3): the queue — an empty card, then one card per job, newest on top. A job card
         changes its look with its kind (JobCardKind: Waiting, Running, Done, Problem, Failed, Cancelled). -->
    <UserControl.Resources>
        <!-- The flow of the empty queue: an icon per step. -->
        <Style x:Key="FlowIcon" TargetType="ContentControl" BasedOn="{StaticResource IconLine}">
            <Setter Property="Width" Value="24" />
            <Setter Property="Height" Value="24" />
            <Setter Property="Tag" Value="1.6" />
            <Setter Property="Content" Value="{StaticResource IconReport}" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Icon}" Value="card">
                    <Setter Property="Content" Value="{StaticResource IconCard}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Icon}" Value="disk">
                    <Setter Property="Content" Value="{StaticResource IconDisk}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Icon}" Value="verify">
                    <Setter Property="Content" Value="{StaticResource IconShield}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Icon}" Value="proxy">
                    <Setter Property="Content" Value="{StaticResource IconVideo}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>

        <!-- ── Waiting: one compact row ── -->
        <DataTemplate x:Key="WaitingCard">
            <Border CornerRadius="14" BorderBrush="{DynamicResource LineSoft}" BorderThickness="1" Background="{DynamicResource BgHeader}" Padding="18,14" Margin="0,0,0,18">
                <DockPanel>
                    <Border DockPanel.Dock="Left" Width="34" Height="34" CornerRadius="9" Background="{DynamicResource Chip}" Margin="0,0,14,0">
                        <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconCard}" Width="18" Height="18" Foreground="{DynamicResource Text2}" />
                    </Border>
                    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Center">
                        <Button Style="{StaticResource GhostButton}" Content="Zrušit" Command="{Binding CancelCommand}" Height="30" Margin="0,0,8,0"
                                AutomationProperties.AutomationId="CancelButton" />
                        <Border Style="{StaticResource PillBorder}">
                            <TextBlock Text="Čeká ve frontě" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource TextSoft}" AutomationProperties.AutomationId="JobStatusText" />
                        </Border>
                    </StackPanel>
                    <StackPanel VerticalAlignment="Center">
                        <TextBlock>
                            <Run Text="{Binding TitleName, Mode=OneWay}" FontWeight="SemiBold" /><Run Text="{Binding TitleSuffix, Mode=OneWay}" Foreground="{DynamicResource Text2}" />
                        </TextBlock>
                        <TextBlock Text="{Binding Sub}" Style="{StaticResource TextMuted}" Margin="0,2,0,0" />
                        <TextBlock Text="{Binding ProfileNote}" Style="{StaticResource TextWarn}" Margin="0,4,0,0" Visibility="{Binding ProfileNote, Converter={StaticResource NullToCollapsed}}"
                                   AutomationProperties.AutomationId="JobProfileNote" />
                    </StackPanel>
                </DockPanel>
            </Border>
        </DataTemplate>

        <!-- ── Running (obr. 2) ── -->
        <DataTemplate x:Key="RunningCard">
            <Border CornerRadius="16" BorderBrush="{DynamicResource RunBorder}" BorderThickness="1" Background="{DynamicResource RunBg}" Padding="24,22" Margin="0,0,0,18">
                <StackPanel>
                    <DockPanel>
                        <Border DockPanel.Dock="Left" Width="42" Height="42" CornerRadius="10" Background="{DynamicResource AccentSoft}" Margin="0,0,14,0">
                            <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconCard}" Width="22" Height="22" Foreground="{DynamicResource AccentText}" />
                        </Border>
                        <StackPanel DockPanel.Dock="Right" HorizontalAlignment="Right">
                            <TextBlock Text="{Binding PercentText}" FontSize="30" FontWeight="SemiBold" HorizontalAlignment="Right" Typography.NumeralAlignment="Tabular"
                                       AutomationProperties.AutomationId="JobPercentText" />
                            <TextBlock Text="{Binding StatusText}" Style="{StaticResource TextMuted}" HorizontalAlignment="Right" AutomationProperties.AutomationId="JobStatusText" />
                        </StackPanel>
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding Title}" FontSize="18" FontWeight="SemiBold" />
                            <TextBlock Text="{Binding Sub}" Style="{StaticResource TextMuted}" Margin="0,2,0,0" />
                        </StackPanel>
                    </DockPanel>
                    <StackPanel Margin="0,22,0,0">
                        <ProgressBar Style="{StaticResource ProgressThin}" Value="{Binding Progress, Mode=OneWay}" />
                        <TextBlock Text="{Binding DetailText}" Style="{StaticResource TextMuted}" Margin="0,8,0,0" />
                        <ItemsControl ItemsSource="{Binding ChainLines}" Margin="0,8,0,0" Visibility="{Binding HasChain, Converter={StaticResource BoolToVisible}}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding}" Style="{StaticResource TextMonoFaint}" />
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                    <TextBlock Text="{Binding WarningText}" Style="{StaticResource TextWarn}" Margin="0,12,0,0" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobWarningText" />
                    <TextBlock Text="{Binding ProfileNote}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding ProfileNote, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobProfileNote" />
                    <!-- safe to eject while stage 2 still runs -->
                    <DockPanel Margin="0,12,0,0" Visibility="{Binding ShowSafe, Converter={StaticResource BoolToVisible}}">
                        <Button DockPanel.Dock="Right" Style="{StaticResource SuccessSecondaryButton}" Content="Vysunout kartu" Command="{Binding EjectCommand}"
                                Visibility="{Binding ShowEjectButton, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="EjectButton" />
                        <TextBlock Text="{Binding SafeText}" Foreground="{DynamicResource Success}" TextWrapping="Wrap" VerticalAlignment="Center" />
                    </DockPanel>
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
                        <Button Style="{StaticResource DangerButton}" Content="Zrušit" Command="{Binding CancelCommand}" AutomationProperties.AutomationId="CancelButton"
                                Visibility="{Binding CanCancel, Converter={StaticResource BoolToVisible}}" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </DataTemplate>

        <!-- ── Done, green (obr. 3) ── -->
        <DataTemplate x:Key="DoneCard">
            <Border CornerRadius="16" BorderBrush="{DynamicResource SuccessBorder}" BorderThickness="1" Background="{DynamicResource SuccessBg}" Margin="0,0,0,18" ClipToBounds="True">
                <StackPanel>
                    <DockPanel Margin="24,22">
                        <Border DockPanel.Dock="Left" Width="52" Height="52" CornerRadius="14" Background="{DynamicResource SuccessFill}" Margin="0,0,18,0">
                            <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconShield}" Width="28" Height="28" Tag="2" Foreground="{DynamicResource OnSuccess}" />
                        </Border>
                        <Button DockPanel.Dock="Right" Style="{StaticResource SuccessButton}" Command="{Binding EjectCommand}" VerticalAlignment="Center"
                                Visibility="{Binding ShowEjectButton, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="EjectButton">
                            <StackPanel Orientation="Horizontal">
                                <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconEject}" Tag="2" Margin="0,0,8,0" Foreground="{DynamicResource OnSuccess}" />
                                <TextBlock Text="Vysunout kartu" />
                            </StackPanel>
                        </Button>
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding HeadTitle}" FontSize="20" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                            <TextBlock Text="{Binding HeadSub}" Foreground="{DynamicResource SuccessText2}" TextWrapping="Wrap" Margin="0,4,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                            <TextBlock Text="Karta vysunuta" Foreground="{DynamicResource SuccessLabel}" Margin="0,4,0,0" Visibility="{Binding Ejected, Converter={StaticResource BoolToVisible}}" />
                        </StackPanel>
                    </DockPanel>
                    <Border BorderBrush="{DynamicResource SuccessLine}" BorderThickness="0,1,0,0" Padding="24,18">
                        <TextBlock Text="{Binding SummaryText}" Foreground="{DynamicResource SuccessText2}" TextWrapping="Wrap" />
                    </Border>
                    <StackPanel Margin="24,0,24,18">
                        <TextBlock Text="{Binding NoteText}" Foreground="{DynamicResource SuccessText2}" TextWrapping="Wrap" Margin="0,0,0,10"
                                   Visibility="{Binding NoteText, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="JobNoteText" />
                        <TextBlock Text="{Binding WarningText}" Style="{StaticResource TextWarn}" Margin="0,0,0,10" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
                                   AutomationProperties.AutomationId="JobWarningText" />
                        <TextBlock Text="{Binding ErrorText}" Style="{StaticResource TextWarn}" Margin="0,0,0,10" Visibility="{Binding ErrorText, Converter={StaticResource NullToCollapsed}}" />
                        <TextBlock Text="{Binding ProfileNote}" Style="{StaticResource TextWarn}" Margin="0,0,0,10" Visibility="{Binding ProfileNote, Converter={StaticResource NullToCollapsed}}"
                                   AutomationProperties.AutomationId="JobProfileNote" />
                        <DockPanel Visibility="{Binding HasReport, Converter={StaticResource BoolToVisible}}">
                            <TextBlock DockPanel.Dock="Right" Text="{Binding PathText}" Style="{StaticResource TextMonoFaint}" Foreground="{DynamicResource SuccessLabel}" VerticalAlignment="Center" />
                            <StackPanel Orientation="Horizontal">
                                <Button Style="{StaticResource SuccessSecondaryButton}" Command="{Binding OpenReportCommand}" Margin="0,0,8,0" AutomationProperties.AutomationId="ReportButton">
                                    <StackPanel Orientation="Horizontal">
                                        <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconReport}" Width="15" Height="15" Margin="0,0,8,0"
                                                        Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" />
                                        <TextBlock Text="Report PDF" />
                                    </StackPanel>
                                </Button>
                                <Button Style="{StaticResource SuccessSecondaryButton}" Command="{Binding RevealCommand}" Margin="0,0,8,0" AutomationProperties.AutomationId="ExplorerButton">
                                    <StackPanel Orientation="Horizontal">
                                        <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconFolder}" Width="15" Height="15" Margin="0,0,8,0"
                                                        Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" />
                                        <TextBlock Text="Otevřít složku" />
                                    </StackPanel>
                                </Button>
                            </StackPanel>
                        </DockPanel>
                    </StackPanel>
                </StackPanel>
            </Border>
        </DataTemplate>

        <!-- ── Not on every disk, yellow (obr. 3) ── -->
        <DataTemplate x:Key="ProblemCard">
            <Border CornerRadius="16" BorderBrush="{DynamicResource WarnBorder}" BorderThickness="1" Background="{DynamicResource WarnBg}" Padding="22,18" Margin="0,0,0,18">
                <StackPanel>
                    <DockPanel>
                        <Border DockPanel.Dock="Left" Width="40" Height="40" CornerRadius="11" Background="{DynamicResource WarnFill}" Margin="0,0,14,0" VerticalAlignment="Top">
                            <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconWarning}" Width="22" Height="22" Tag="2" Foreground="{DynamicResource OnWarn}" />
                        </Border>
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding HeadTitle}" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                            <TextBlock Text="{Binding HeadSub}" Foreground="{DynamicResource WarnText2}" TextWrapping="Wrap" Margin="0,3,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock Text="{Binding FailedText}" Foreground="{DynamicResource WarnText2}" TextWrapping="Wrap" Margin="0,12,0,0"
                               Visibility="{Binding FailedText, Converter={StaticResource NullToCollapsed}}" />
                    <TextBlock Text="{Binding ErrorText}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding ErrorText, Converter={StaticResource NullToCollapsed}}" />
                    <TextBlock Text="{Binding WarningText}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobWarningText" />
                    <TextBlock Text="{Binding ProfileNote}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding ProfileNote, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobProfileNote" />
                    <StackPanel Orientation="Horizontal" Margin="0,14,0,0" Visibility="{Binding HasReport, Converter={StaticResource BoolToVisible}}">
                        <Button Style="{StaticResource SecondaryButton}" Content="Report PDF" Command="{Binding OpenReportCommand}" Margin="0,0,8,0" AutomationProperties.AutomationId="ReportButton" />
                        <Button Style="{StaticResource SecondaryButton}" Content="Otevřít složku" Command="{Binding RevealCommand}" AutomationProperties.AutomationId="ExplorerButton" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </DataTemplate>

        <!-- ── Failed before or without a copy, red ── -->
        <DataTemplate x:Key="FailedCard">
            <Border CornerRadius="16" BorderBrush="{DynamicResource DangerCardBorder}" BorderThickness="1" Background="{DynamicResource DangerBg}" Padding="22,18" Margin="0,0,0,18">
                <DockPanel>
                    <Border DockPanel.Dock="Left" Width="40" Height="40" CornerRadius="11" Background="{DynamicResource Danger}" Margin="0,0,14,0" VerticalAlignment="Top">
                        <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconClose}" Width="20" Height="20" Tag="2.4" Foreground="{DynamicResource OnAccent}" />
                    </Border>
                    <StackPanel VerticalAlignment="Center">
                        <TextBlock Text="{Binding HeadTitle}" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                        <TextBlock Text="{Binding HeadSub}" Foreground="{DynamicResource DangerText}" TextWrapping="Wrap" Margin="0,3,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                        <TextBlock Text="{Binding ProfileNote}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding ProfileNote, Converter={StaticResource NullToCollapsed}}"
                                   AutomationProperties.AutomationId="JobProfileNote" />
                    </StackPanel>
                </DockPanel>
            </Border>
        </DataTemplate>

        <!-- ── Cancelled, grey ── -->
        <DataTemplate x:Key="CancelledCard">
            <Border CornerRadius="16" BorderBrush="{DynamicResource Line}" BorderThickness="1" Background="{DynamicResource BgHeader}" Padding="22,18" Margin="0,0,0,18">
                <StackPanel>
                    <TextBlock Text="{Binding HeadTitle}" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                    <TextBlock Text="{Binding HeadSub}" Style="{StaticResource TextMuted}" FontSize="14" Margin="0,3,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0" Visibility="{Binding HasReport, Converter={StaticResource BoolToVisible}}">
                        <Button Style="{StaticResource SecondaryButton}" Content="Report PDF" Command="{Binding OpenReportCommand}" Margin="0,0,8,0" AutomationProperties.AutomationId="ReportButton" />
                        <Button Style="{StaticResource SecondaryButton}" Content="Otevřít složku" Command="{Binding RevealCommand}" AutomationProperties.AutomationId="ExplorerButton" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </DataTemplate>
    </UserControl.Resources>

    <DockPanel Background="{DynamicResource Bg}">
        <!-- Fronta -->
        <DockPanel DockPanel.Dock="Top" Margin="28,22,28,0">
            <Button DockPanel.Dock="Right" Style="{StaticResource LinkButton}" Content="Vyčistit hotové" Command="{Binding ClearFinishedCommand}"
                    Visibility="{Binding HasFinished, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="ClearFinishedButton" />
            <StackPanel Orientation="Horizontal">
                <RadioButton Style="{StaticResource TabButton}" IsChecked="True" AutomationProperties.AutomationId="QueueTab">
                    <TextBlock>
                        <Run Text="Fronta" /><Run Text="{Binding QueueBadgeText, Mode=OneWay}" Foreground="{DynamicResource AccentText}" />
                    </TextBlock>
                </RadioButton>
            </StackPanel>
        </DockPanel>
        <Grid>
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel Margin="28,18,28,28">
                    <!-- Empty queue: the flow Karta → disks → Ověření → Proxy → Report and what a start does -->
                    <Border CornerRadius="16" BorderBrush="{DynamicResource LineSoft}" BorderThickness="1" Background="{DynamicResource BgHeader}" Padding="32,44" Margin="0,0,0,22"
                            Visibility="{Binding HasJobs, Converter={StaticResource BoolToCollapsed}}">
                        <StackPanel HorizontalAlignment="Center">
                            <ItemsControl ItemsSource="{Binding FlowSteps}" HorizontalAlignment="Center">
                                <ItemsControl.ItemsPanel>
                                    <ItemsPanelTemplate>
                                        <WrapPanel HorizontalAlignment="Center" />
                                    </ItemsPanelTemplate>
                                </ItemsControl.ItemsPanel>
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                            <Border Width="34" Height="1" Background="{DynamicResource FlowLine}" Margin="10,26,10,0" VerticalAlignment="Top"
                                                    Visibility="{Binding First, Converter={StaticResource BoolToCollapsed}}" />
                                            <StackPanel Width="92">
                                                <Border Width="52" Height="52" CornerRadius="14" BorderThickness="1">
                                                    <Border.Style>
                                                        <Style TargetType="Border">
                                                            <Setter Property="Background" Value="{DynamicResource Raised}" />
                                                            <Setter Property="BorderBrush" Value="{DynamicResource LineStrong}" />
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding First}" Value="True">
                                                                    <Setter Property="Background" Value="{DynamicResource AccentSoft}" />
                                                                    <Setter Property="BorderBrush" Value="{DynamicResource AccentSoft}" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </Border.Style>
                                                    <ContentControl>
                                                        <ContentControl.Style>
                                                            <Style TargetType="ContentControl" BasedOn="{StaticResource FlowIcon}">
                                                                <Setter Property="Foreground" Value="{DynamicResource Text}" />
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding First}" Value="True">
                                                                        <Setter Property="Foreground" Value="{DynamicResource AccentText}" />
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </ContentControl.Style>
                                                    </ContentControl>
                                                </Border>
                                                <TextBlock Text="{Binding Label}" Style="{StaticResource TextMuted}" TextAlignment="Center" Margin="0,8,0,0" />
                                            </StackPanel>
                                        </StackPanel>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                            <TextBlock Text="{Binding QueueEmptyTitle}" FontSize="18" FontWeight="SemiBold" HorizontalAlignment="Center" TextAlignment="Center" Margin="0,26,0,0"
                                       TextWrapping="Wrap" AutomationProperties.AutomationId="QueueEmptyTitle" />
                            <TextBlock Text="{Binding QueueEmptyText}" Foreground="{DynamicResource Text2}" HorizontalAlignment="Center" TextAlignment="Center" MaxWidth="440"
                                       TextWrapping="Wrap" LineHeight="21" Margin="0,6,0,0" />
                        </StackPanel>
                    </Border>

                    <ItemsControl ItemsSource="{Binding Jobs}">
                        <ItemsControl.ItemTemplateSelector>
                            <local:QueueItemTemplateSelector>
                                <local:QueueItemTemplateSelector.JobTemplate>
                                    <DataTemplate>
                                        <!-- The look follows the job's kind as it changes (a selector would pick once). -->
                                        <ContentControl Content="{Binding}" Focusable="False">
                                            <ContentControl.Style>
                                                <Style TargetType="ContentControl">
                                                    <Setter Property="ContentTemplate" Value="{StaticResource RunningCard}" />
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding Kind}" Value="Waiting">
                                                            <Setter Property="ContentTemplate" Value="{StaticResource WaitingCard}" />
                                                        </DataTrigger>
                                                        <DataTrigger Binding="{Binding Kind}" Value="Done">
                                                            <Setter Property="ContentTemplate" Value="{StaticResource DoneCard}" />
                                                        </DataTrigger>
                                                        <DataTrigger Binding="{Binding Kind}" Value="Problem">
                                                            <Setter Property="ContentTemplate" Value="{StaticResource ProblemCard}" />
                                                        </DataTrigger>
                                                        <DataTrigger Binding="{Binding Kind}" Value="Failed">
                                                            <Setter Property="ContentTemplate" Value="{StaticResource FailedCard}" />
                                                        </DataTrigger>
                                                        <DataTrigger Binding="{Binding Kind}" Value="Cancelled">
                                                            <Setter Property="ContentTemplate" Value="{StaticResource CancelledCard}" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </ContentControl.Style>
                                        </ContentControl>
                                    </DataTemplate>
                                </local:QueueItemTemplateSelector.JobTemplate>
                                <local:QueueItemTemplateSelector.VerifyTemplate>
                                    <DataTemplate>
                                        <!-- Ověřit zálohu (spec 2.4) as a card of the queue -->
                                        <Border CornerRadius="16" BorderBrush="{DynamicResource LineSoft}" BorderThickness="1" Background="{DynamicResource BgHeader}" Padding="22,18" Margin="0,0,0,18">
                                            <StackPanel>
                                                <DockPanel>
                                                    <Border DockPanel.Dock="Left" Width="40" Height="40" CornerRadius="10" Background="{DynamicResource AccentSoft}" Margin="0,0,14,0">
                                                        <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconShield}" Width="22" Height="22" Foreground="{DynamicResource AccentText}" />
                                                    </Border>
                                                    <Border DockPanel.Dock="Right" Style="{StaticResource PillBorder}">
                                                        <TextBlock Text="{Binding StatusText}" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource TextSoft}"
                                                                   AutomationProperties.AutomationId="VerifyStatusText" />
                                                    </Border>
                                                    <StackPanel VerticalAlignment="Center">
                                                        <TextBlock Text="{Binding Title}" FontSize="16" FontWeight="SemiBold" />
                                                        <TextBlock Text="{Binding FolderText}" Style="{StaticResource TextMonoFaint}" ToolTip="{Binding FolderText}" Margin="0,2,0,0" />
                                                    </StackPanel>
                                                </DockPanel>
                                                <StackPanel Margin="0,14,0,0" Visibility="{Binding IsRunning, Converter={StaticResource BoolToVisible}}">
                                                    <ProgressBar Style="{StaticResource ProgressThin}" Value="{Binding Progress, Mode=OneWay}" />
                                                    <TextBlock Text="{Binding DetailText}" Style="{StaticResource TextMuted}" Margin="0,8,0,0" />
                                                </StackPanel>
                                                <TextBlock Text="{Binding ResultText}" FontSize="14" Margin="0,10,0,0" TextWrapping="Wrap" AutomationProperties.AutomationId="VerifyResultText"
                                                           Visibility="{Binding ResultText, Converter={StaticResource NullToCollapsed}}">
                                                    <TextBlock.Style>
                                                        <Style TargetType="TextBlock">
                                                            <Setter Property="Foreground" Value="{DynamicResource Success}" />
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding ResultTone}" Value="Error">
                                                                    <Setter Property="Foreground" Value="{DynamicResource DangerText}" />
                                                                </DataTrigger>
                                                                <DataTrigger Binding="{Binding ResultTone}" Value="Warning">
                                                                    <Setter Property="Foreground" Value="{DynamicResource Warn}" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </TextBlock.Style>
                                                </TextBlock>
                                                <ItemsControl ItemsSource="{Binding Warnings}" Margin="0,4,0,0">
                                                    <ItemsControl.ItemTemplate>
                                                        <DataTemplate>
                                                            <TextBlock Text="{Binding}" Style="{StaticResource TextWarn}" />
                                                        </DataTemplate>
                                                    </ItemsControl.ItemTemplate>
                                                </ItemsControl>
                                                <TextBlock Text="{Binding ErrorText}" Style="{StaticResource TextDanger}" Margin="0,8,0,0" Visibility="{Binding ErrorText, Converter={StaticResource NullToCollapsed}}" />
                                                <TextBlock Text="{Binding ReportNote}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" AutomationProperties.AutomationId="VerifyReportNote"
                                                           Visibility="{Binding ReportNote, Converter={StaticResource NullToCollapsed}}" />
                                                <DockPanel Margin="0,14,0,0">
                                                    <Button DockPanel.Dock="Right" Style="{StaticResource DangerButton}" Content="Zrušit" Command="{Binding CancelCommand}"
                                                            AutomationProperties.AutomationId="VerifyCancelButton" Visibility="{Binding CanCancel, Converter={StaticResource BoolToVisible}}" />
                                                    <StackPanel Orientation="Horizontal">
                                                        <Button Style="{StaticResource SecondaryButton}" Content="Report ověření" Command="{Binding OpenReportCommand}" Margin="0,0,8,0"
                                                                AutomationProperties.AutomationId="VerifyReportButton" Visibility="{Binding HasReport, Converter={StaticResource BoolToVisible}}" />
                                                        <Button Style="{StaticResource SecondaryButton}" Content="Otevřít složku" Command="{Binding RevealCommand}" AutomationProperties.AutomationId="VerifyExplorerButton" />
                                                    </StackPanel>
                                                </DockPanel>
                                            </StackPanel>
                                        </Border>
                                    </DataTemplate>
                                </local:QueueItemTemplateSelector.VerifyTemplate>
                            </local:QueueItemTemplateSelector>
                        </ItemsControl.ItemTemplateSelector>
                    </ItemsControl>
                </StackPanel>
            </ScrollViewer>
        </Grid>
    </DockPanel>
</UserControl>
```

- [ ] **Step 2: Build a celá sada**

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 upozornění, 0 chyb`; celá sada `Neúspěšné: 0` (časově citlivé `ProxyWatchdogTests` / `ProxyRendererTests.Feeder_*` při jednom selhání pusťte znovu samostatně — viz Global Constraints).

Expected navíc: žádná chyba XAML při buildu (`MC3000`, `MC3072`). Chybějící klíč zdroje se ukáže až za běhu, proto
zkontrolujte, že každý `StaticResource` / `DynamicResource` v oknech je definovaný (`App.xaml`, `Themes\*.xaml` nebo
v samotném souboru); klíče Fluent tématu (`…Brush`, `AccentButtonStyle`) definuje WPF:

```powershell
$xaml = @(Get-ChildItem windows\src\Offload.App\*.xaml) + @(Get-ChildItem windows\src\Offload.App\Themes\*.xaml)
Select-String -Path windows\src\Offload.App\*.xaml -Pattern '(Static|Dynamic)Resource (\w+)' -AllMatches | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[2].Value } | Sort-Object -Unique | Where-Object { $_ -notmatch 'Brush$|^AccentButtonStyle$' -and -not (Select-String -Path $xaml.FullName -Pattern "x:Key=`"$_`"" -Quiet) }
```

Expected: prázdný výstup.

- [ ] **Step 3: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)`.

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1):

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 13 — levý sloupec (postup 3): kroky „1 · KARTY“ (⟳ a „Ze složky…“, prázdný stav v přerušovaném rámečku „Vložte kartu do čtečky“ / během offloadu „Vložte další kartu“, dlaždice karty: ikona, název, štítek kamery, „24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s“, řádek historie, zaškrtávátko, žluté „Doplnit“ u karty s novými klipy — zaškrtne i kartu, pole „Název složky — <nápověda>“ v monospace), „2 · CÍLE“ (+ Přidat disk, dlaždice s kolečkem, „7,20 TB volných z 8 TB · NTFS · NVMe“, ✕, pruh místa šedá / modrá / žlutá / červená a věta kontroly místa pod ním, exFAT, nepřipojený disk, náhled cesty „…\2026-10-07\A035_ARRI\“, jen jeden cíl) a „3 · PO ZKOPÍROVÁNÍ“ (4 řádky se zeleným odškrtnutím, vypnuté ověření žlutě, „Upravit projekt“ otevře Nastavení, pod tím červené / oranžové důvody, proč nejde start); připnutá spodní lišta „**1 karta → 2 disky** · 1,70 GB“ + odhad, hlášky místa a startu, „▶ Spustit offload [Ctrl ↵]“ / „Zařadit do fronty“; bez karty a s úlohami ve frontě kompaktní kroky 2 a 3 (obr. 2, 3); spojení karet se zjišťuje mimo UI vlákno, kamera s prověřením karty; volby Struktura, Proxy, Dekodéry a Hardware z levého panelu zmizely (jsou v Nastavení a v liště); `SetupTexts.CardFacts`, `Total`
- Úkol 12 — stránka Nastavení (postup 6, předsunutý před levý sloupec, aby žádná volba nebyla ani na jeden commit nedostupná): ikona s posuvníky v liště nebo Ctrl+, otevře místo obou sloupců stránku s lištou „‹ Zpět · Projekt <název> · Uložit jako nový projekt… · Hotovo“ (Esc zavře) a sekcemi Struktura složek (cesta, vlastní datum, název podle klipů) · Ověření (vypínač, kontrolní součet, MD5, Ověřit zálohu… + „Ukládat report ověření“) · Analýza a náhledy · **Proxy** podle obr. 4 (vypínač, segmenty Kodek a Výška, datový tok s „≈ 75 MB / minutu“, výřez, LUT + tabulka vestavěných LUTů s „použito“, „Když kameru nepoznám“, Pravidla LUTů…, Vlastní .cube…, zvuk, kamerové proxy, kopie na všechny cíle, složka pro proxy; vpravo šedý náhled burn-inu 16:9 s názvem klipu, TC a frame lines výřezu) · Report (CSV, ALE, denní report po kartě, Denní report…) · Oznámení (souhrn + Upravit oznámení…) · Dekodéry RAW (nástroje, Gen 4 → Gen 5) · Aplikace (verze, Aktualizace…, Prověřit USB porty…); úpravy jdou do zobrazeného profilu jako dosud (`SettingsTexts`: řádky LUTů, tok za minutu, rám výřezu, souhrn oznámení)
- Úkol 11 — horní lišta (postup 2) místo menu: logo a „Offload“, přepínač **Projekt** na místě přepínače presetu (rozbalovací seznam projektů s odškrtnutím aktivního, „Uložit jako nový projekt…“, „Přejmenovat…“, „Smazat…“; zamčený, dokud Start kontroluje — I1), vpravo odkaz „Nová verze …“, pilulka „● Běží 1 offload · 1 ve frontě“ (`QueueSummary`, pozastavené zvlášť), „Ověřit zálohu“ (Ctrl+Shift+O), „Hardware“ s tečkou (zelená = porty bez varování, žlutá = doporučení, žádná před prvním testem; `TopBar.Dot`; Ctrl+U) a tlačítko s posuvníky (zatím nabídka Denní report…, Oznámení…, Aktualizace…, Ukládat report ověření — příští úkol je přesune do Nastavení); okno 1360 × 900 (min 940 × 640 beze změny); přepínač „Projekt:“ z levého panelu je pryč
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 14 — fronta se stávajícími daty (postup 4, 1. část): prázdná fronta = karta s ikonami Karta → 2 disky naráz → Ověření → Proxy → Report (jen co projekt dělá) a „Připraveno — A035_ARRI na 2 disky“ + věta (bez karty „Zatím žádný offload“); karta úlohy mění vzhled podle `JobCard.Headline` (čeká: kompaktní řádek „B012_FX6 — doplnění · 2 nové klipy · 14,2 GB · stejné cíle · Čeká ve frontě“ se Zrušit; běží: ikona, název, „24 klipů · 1,70 GB · ALEXA 35“, velké „38 %“, průběh a dosavadní texty, Zrušit červeným obrysem; hotovo zeleně: štít, „… — kartu lze vysunout“, Vysunout kartu, souhrn, Report PDF, Otevřít složku, cesta; nesouhlasí žlutě: „… — kartu zatím nevysunujte“ + věta; chyba červeně; zrušeno šedě); ověření zálohy jako karta ve stejném stylu; „Fronta 2“ s počtem nedokončených, „Vyčistit hotové“ jako odkaz; karta si pamatuje klipy, velikost a kameru z prověření při startu
- Úkol 13 — levý sloupec (postup 3): kroky „1 · KARTY“ (⟳ a „Ze složky…“, prázdný stav v přerušovaném rámečku „Vložte kartu do čtečky“ / během offloadu „Vložte další kartu“, dlaždice karty: ikona, název, štítek kamery, „24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s“, řádek historie, zaškrtávátko, žluté „Doplnit“ u karty s novými klipy — zaškrtne i kartu, pole „Název složky — <nápověda>“ v monospace), „2 · CÍLE“ (+ Přidat disk, dlaždice s kolečkem, „7,20 TB volných z 8 TB · NTFS · NVMe“, ✕, pruh místa šedá / modrá / žlutá / červená a věta kontroly místa pod ním, exFAT, nepřipojený disk, náhled cesty „…\2026-10-07\A035_ARRI\“, jen jeden cíl) a „3 · PO ZKOPÍROVÁNÍ“ (4 řádky se zeleným odškrtnutím, vypnuté ověření žlutě, „Upravit projekt“ otevře Nastavení, pod tím červené / oranžové důvody, proč nejde start); připnutá spodní lišta „**1 karta → 2 disky** · 1,70 GB“ + odhad, hlášky místa a startu, „▶ Spustit offload [Ctrl ↵]“ / „Zařadit do fronty“; bez karty a s úlohami ve frontě kompaktní kroky 2 a 3 (obr. 2, 3); spojení karet se zjišťuje mimo UI vlákno, kamera s prověřením karty; volby Struktura, Proxy, Dekodéry a Hardware z levého panelu zmizely (jsou v Nastavení a v liště); `SetupTexts.CardFacts`, `Total`
- Úkol 12 — stránka Nastavení (postup 6, předsunutý před levý sloupec, aby žádná volba nebyla ani na jeden commit nedostupná): ikona s posuvníky v liště nebo Ctrl+, otevře místo obou sloupců stránku s lištou „‹ Zpět · Projekt <název> · Uložit jako nový projekt… · Hotovo“ (Esc zavře) a sekcemi Struktura složek (cesta, vlastní datum, název podle klipů) · Ověření (vypínač, kontrolní součet, MD5, Ověřit zálohu… + „Ukládat report ověření“) · Analýza a náhledy · **Proxy** podle obr. 4 (vypínač, segmenty Kodek a Výška, datový tok s „≈ 75 MB / minutu“, výřez, LUT + tabulka vestavěných LUTů s „použito“, „Když kameru nepoznám“, Pravidla LUTů…, Vlastní .cube…, zvuk, kamerové proxy, kopie na všechny cíle, složka pro proxy; vpravo šedý náhled burn-inu 16:9 s názvem klipu, TC a frame lines výřezu) · Report (CSV, ALE, denní report po kartě, Denní report…) · Oznámení (souhrn + Upravit oznámení…) · Dekodéry RAW (nástroje, Gen 4 → Gen 5) · Aplikace (verze, Aktualizace…, Prověřit USB porty…); úpravy jdou do zobrazeného profilu jako dosud (`SettingsTexts`: řádky LUTů, tok za minutu, rám výřezu, souhrn oznámení)
- Úkol 11 — horní lišta (postup 2) místo menu: logo a „Offload“, přepínač **Projekt** na místě přepínače presetu (rozbalovací seznam projektů s odškrtnutím aktivního, „Uložit jako nový projekt…“, „Přejmenovat…“, „Smazat…“; zamčený, dokud Start kontroluje — I1), vpravo odkaz „Nová verze …“, pilulka „● Běží 1 offload · 1 ve frontě“ (`QueueSummary`, pozastavené zvlášť), „Ověřit zálohu“ (Ctrl+Shift+O), „Hardware“ s tečkou (zelená = porty bez varování, žlutá = doporučení, žádná před prvním testem; `TopBar.Dot`; Ctrl+U) a tlačítko s posuvníky (zatím nabídka Denní report…, Oznámení…, Aktualizace…, Ukládat report ověření — příští úkol je přesune do Nastavení); okno 1360 × 900 (min 940 × 640 beze změny); přepínač „Projekt:“ z levého panelu je pryč
```

- [ ] **Step 4: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: queue with the cards of the new design, existing data (plan 9, task 14)

The empty queue shows the flow card, every job card switches its look by JobCard.Headline (waiting,
running, done, problem, failed, cancelled); the verify job uses the same style.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.App\JobItemViewModel.cs windows\src\Offload.App\MainViewModel.Queue.cs windows\src\Offload.App\MainViewModel.TopBar.cs windows\src\Offload.App\MainViewModel.cs windows\src\Offload.App\QueuePanel.xaml
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 15: Fronta — průběh po cílech, fáze, Pozastavit, souhrn, náhledy, Zkopírovat znovu (postup 4, 2. část)

Běžící karta dostane ukazatel fází, tabulku rychlostí (bublina řádku = dosavadní řádek řetězu připojení),
„Soubor 10 / 24“, **Pozastavit / Pokračovat**, „zbývá …“ a „Nejpomalejší článek“; zelená karta souhrn čtyř čísel,
náhledy (dekódované mimo UI vlákno) a tlačítko Proxy; žlutá seznam nesouhlasných souborů a **Zkopírovat znovu**.
Řádky a fáze se aktualizují na místě, seznam se 3× za sekundu nepřekresluje. Bez vlastního testu (logika je v úkolech
3–7); kontrolou je build a celá sada, vzhled úkol 19.

**Files:**
- Create: `windows/src/Offload.App/JobItemViewModel.Card.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.App/JobItemViewModel.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs`
- Modify: `windows/src/Offload.App/QueuePanel.xaml`

**Interfaces:**
- Consumes: `OffloadJob.Pause/Resume` (úkol 3), `JobConfigs.Repair`, `RepairRules` (úkol 5), `JobCard.*` (úkol 6), `RateMemory.Remaining` (úkol 7).
- Produces:
  - `PhaseStepViewModel` (`Text`, `State`, `Fill`, `Set(PhaseStep)`), `TransferRowViewModel` (`Name`, `Number`, `HasNumber`, `Share`, `RateText`, `Tooltip`, `Set(TransferRow, string?)`), `ThumbViewModel(string clip, string? more = null)` (`Clip`, `More`, `Image`).
  - `JobItemViewModel` (partial `JobItemViewModel.Card.cs`): `Steps`, `Rows`, `Thumbs` (`ObservableCollection`), `PauseCommand`, `ResumeCommand`, `RepairCommand`, `OpenProxyCommand`, `RemainingText`, `FileCaption`, `FileValue`, `PauseText`, `CanPause`, `BrakeText`, `SummaryFields`, `HasThumbs`, `HasProxies`, `FailedRows`, `MoreFailed`, `DiskCount`, `CanRepair`, `PlanClips` (internal).
  - `MainViewModel`: `SourcePresent(JobItemViewModel)`, `RepairPending(JobItemViewModel)`, `Repair(JobItemViewModel)`, `RemainingTotal(JobItemViewModel, double?)` (internal).
  - AutomationId: `JobSteps`, `JobRows`, `JobFileCaption`, `JobRemainingText`, `JobPauseText`, `PauseButton`, `ResumeButton`, `JobBrakeText`, `JobSummary`, `ProxyFolderOpenButton`, `FailedFiles`, `RepairButton`.

- [ ] **Step 1: Implementace**

GUI se při tomto úkolu **nespouští** (vzhled zkontroluje úkol 19).

#### `windows/src/Offload.App/JobItemViewModel.Card.cs` (nový soubor)

```csharp
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.App;

/// One segment of the phase indicator, updated in place every tick (no rebuilt rows).
public sealed class PhaseStepViewModel : ObservableObject
{
    public string Text { get; private set; } = "";
    public StepState State { get; private set; }
    public double Fill { get; private set; }

    internal void Set(PhaseStep s)
    {
        if (Text == s.Text && State == s.State && Fill == s.Fill) return;
        (Text, State, Fill) = (s.Text, s.State, s.Fill);
        Raise(string.Empty);
    }
}

/// One row of the speed table, updated in place every tick; the tooltip is the old chain line (product, port, % linky).
public sealed class TransferRowViewModel : ObservableObject
{
    public string Name { get; private set; } = "";
    public int? Number { get; private set; }
    public bool HasNumber => Number != null;
    public double Share { get; private set; }
    public string RateText { get; private set; } = "";
    public string? Tooltip { get; private set; }

    internal void Set(TransferRow r, string? tooltip)
    {
        if (Name == r.Name && Number == r.Number && Share == r.Share && RateText == r.RateText && Tooltip == tooltip) return;
        (Name, Number, Share, RateText, Tooltip) = (r.Name, r.Number, r.Share, r.RateText, tooltip);
        Raise(string.Empty);
    }
}

/// A thumbnail of the finished card: the clip's name and its middle frame (decoded off the UI thread); the last tile
/// may be "+18" (More) instead.
public sealed class ThumbViewModel(string clip, string? more = null) : ObservableObject
{
    public string Clip => clip;
    public string? More => more;
    ImageSource? image;
    public ImageSource? Image
    {
        get => image;
        internal set => Set(ref image, value);
    }
}

/// The new parts of the job card (UI návrh, nové věci 1–4 and 9): phase indicator, speed table, file line, pause,
/// remaining time, the brake sentence, the summary fields, thumbnails, the failed files and "Zkopírovat znovu".
public sealed partial class JobItemViewModel
{
    IReadOnlyList<ChainElement>? chain;
    IReadOnlyList<Clip>? planClips;
    bool thumbsLoaded;

    /// The clips of what this job copies (once the plan exists), found once.
    internal IReadOnlyList<Clip> PlanClips
    {
        get
        {
            if (planClips == null && Job.Plan is { } p) planClips = ClipDetector.Detect(p.Scan.Files);
            return planClips ?? [];
        }
    }

    public ObservableCollection<PhaseStepViewModel> Steps { get; } = [];
    public ObservableCollection<TransferRowViewModel> Rows { get; } = [];
    public ObservableCollection<ThumbViewModel> Thumbs { get; } = [];
    public RelayCommand PauseCommand { get; private set; } = null!;
    public RelayCommand ResumeCommand { get; private set; } = null!;
    public RelayCommand RepairCommand { get; private set; } = null!;
    public RelayCommand OpenProxyCommand { get; private set; } = null!;

    void InitCard()
    {
        PauseCommand = new RelayCommand(() => { Job.Pause(); Refresh(); }, () => CanPause);
        ResumeCommand = new RelayCommand(() => { Job.Resume(); Refresh(); }, () => IsPaused);
        RepairCommand = new RelayCommand(() => owner.Repair(this), () => CanRepair);
        OpenProxyCommand = new RelayCommand(OpenProxies);
    }

    /// Every tick: the indicator and the table are updated in place; the thumbnails once the job has finished.
    void RefreshCard()
    {
        var steps = JobCard.Steps(progress.Status, Job.Config, Progress);
        Sync(Steps, steps.Count, () => new PhaseStepViewModel(), (vm, i) => vm.Set(steps[i]));
        chain = links != null && Phase is JobPhase.Copying or JobPhase.Verifying
            ? ChainView.Build(links, [Phase == JobPhase.Copying ? progress.ReadRate / 1e6 : 0, .. progress.DestRates.Select(r => r / 1e6)], connectors, portNames)
            : null;
        var rows = JobCard.Rows(progress, DestNames);
        bool verifying = Phase == JobPhase.Verifying;
        Sync(Rows, rows.Count, () => new TransferRowViewModel(), (vm, i) =>
        {
            // The old chain line as the row's tooltip: the card reader is chain[0], destination d is chain[d + 1].
            int element = verifying ? i + 1 : i;
            var tip = chain is { } c && element < c.Count ? $"{c[element].Role} {c[element].Product} — {c[element].Connection}" : null;
            vm.Set(rows[i], tip);
        });
        if (!thumbsLoaded && IsFinished && Job.Report is { } report)
        {
            thumbsLoaded = true;
            LoadThumbs(report);
        }
    }

    static void Sync<T>(ObservableCollection<T> list, int count, Func<T> create, Action<T, int> update)
    {
        while (list.Count > count) list.RemoveAt(list.Count - 1);
        while (list.Count < count) list.Add(create());
        for (int i = 0; i < count; i++) update(list[i], i);
    }

    // ── Running ────────────────────────────────────────────────────────────────────────────────────────────────
    /// "zbývá ~0:02 kopie · ~6:00 celkem" (the whole job only when the session's speeds are known).
    public string? RemainingText => JobCard.Remaining(Phase, progress.Eta, owner.RemainingTotal(this, progress.Eta));

    public string FileCaption => JobCard.FileLine(progress.Status).Caption;
    public string FileValue => JobCard.FileLine(progress.Status).Value;

    /// "Pozastavuje se…" / "Pozastaví se po proxy klipu A003…" / "Pozastaveno — …", or null.
    public string? PauseText => progress.PauseText;
    public bool CanPause => IsRunning && !IsPaused && !Job.IsCancelled;

    /// "Nejpomalejší článek: čtečka karty (…). Disky by zvládly víc." while copying.
    public string? BrakeText => Phase == JobPhase.Copying && chain is { } c ? JobCard.BrakeSentence(progress.Brake, c, DestNames) : null;

    // ── Done ───────────────────────────────────────────────────────────────────────────────────────────────────
    /// Kopie · Ověření · Proxy · Celkem.
    public IReadOnlyList<SummaryField> SummaryFields => IsFinished && Job.Report is { } r ? JobCard.Summary(r, progress.Elapsed) : [];
    public bool HasThumbs => Thumbs.Count > 0;
    public bool HasProxies => Job.Report?.Proxy?.Enabled == true && progress.Status.ProxiesDone > 0;

    void LoadThumbs(OffloadReport report)
    {
        var (thumbs, more) = JobCard.Thumbs(report);
        foreach (var (clip, jpeg) in thumbs)
        {
            var vm = new ThumbViewModel(clip);
            Thumbs.Add(vm);
            // Decoding is CPU work on bytes in memory (the report kept them): off the UI thread, frozen for it.
            Task.Run(() =>
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 240;
                image.StreamSource = new MemoryStream(jpeg);
                image.EndInit();
                image.Freeze();
                return image;
            }).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully) vm.Image = t.Result;
                else AppLog.Write($"{CardName}: thumbnail {clip}: {t.Exception?.GetBaseException().Message}");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (more > 0) Thumbs.Add(new ThumbViewModel("", $"+{more}"));   // the sixth tile
        Raise(nameof(HasThumbs));
    }

    void OpenProxies()
    {
        if (Job.Plan is { } plan && Shell.OpenFolder(Path.Combine(plan.CardPaths[0], Layout.ProxyDirName)) is string error) owner.ShowMessage(error);
    }

    // ── Not on every disk ──────────────────────────────────────────────────────────────────────────────────────
    const int ShownFailed = 5;

    /// The files that are not on every destination (first five), with "RAID_A ✓" / "SHUTTLE_B ✕ hash" per destination.
    public IReadOnlyList<FailedFileRow> FailedRows => Job.Report is { Success: false } r ? JobCard.FailedRows(r, DestNames).Take(ShownFailed).ToList() : [];
    public string? MoreFailed => Job.Report is { Success: false } r ? JobCard.MoreFiles(r.FailedFiles.Count, ShownFailed) : null;
    public int DiskCount => DestNames.Count;

    /// "Zkopírovat znovu": only the failed files, only where they failed (a repair job, RepairRules).
    public bool CanRepair => RepairRules.Offer(Phase, Job.Report, owner.SourcePresent(this), owner.RepairPending(this));
}
```

#### `windows/src/Offload.App/JobItemViewModel.cs`

Najít (1/6):

```csharp
public sealed record DestinationLine(string Name, string Sub, string FullPath);

/// One card in the queue: mirrors the job's status every tick (Mac `JobItem` + `JobCard`).
public sealed class JobItemViewModel : QueueItem
{
    readonly MainViewModel owner;
    readonly string sourceName;
```

Nahradit:

```csharp
public sealed record DestinationLine(string Name, string Sub, string FullPath);

/// One card in the queue: mirrors the job's status every tick (Mac `JobItem` + `JobCard`).
public sealed partial class JobItemViewModel : QueueItem
{
    readonly MainViewModel owner;
    readonly string sourceName;
```

Najít (2/6):

```csharp
    readonly JobCardFacts? facts;
    CardHeadline headline;
    IReadOnlyList<string>? destNames;
    int? clipCount;

    /// `space`: what the card needs on each destination volume (spec 2.5) — counted while this job is queued or copies.
    /// `profileNote`: orange, for the whole life of the card — e.g. an append whose project no longer exists.
```

Nahradit:

```csharp
    readonly JobCardFacts? facts;
    CardHeadline headline;
    IReadOnlyList<string>? destNames;

    /// `space`: what the card needs on each destination volume (spec 2.5) — counted while this job is queued or copies.
    /// `profileNote`: orange, for the whole life of the card — e.g. an append whose project no longer exists.
```

Najít (3/6):

```csharp
        EjectCommand = new RelayCommand(() => owner.Eject(this), () => !Ejecting);
        OpenReportCommand = new RelayCommand(OpenReport);
        RevealCommand = new RelayCommand(Reveal);
    }

    public OffloadJob Job { get; }
```

Nahradit:

```csharp
        EjectCommand = new RelayCommand(() => owner.Eject(this), () => !Ejecting);
        OpenReportCommand = new RelayCommand(OpenReport);
        RevealCommand = new RelayCommand(Reveal);
        InitCard();
    }

    public OffloadJob Job { get; }
```

Najít (4/6):

```csharp
            ResolveChain(plan);
        }
        headline = JobCard.Headline(CardName, progress.Status, Job.Report, DestNames);
        Raise(string.Empty);   // every binding of the card may have changed
    }
```

Nahradit:

```csharp
            ResolveChain(plan);
        }
        headline = JobCard.Headline(CardName, progress.Status, Job.Report, DestNames);
        RefreshCard();
        Raise(string.Empty);   // every binding of the card may have changed
    }
```

Najít (5/6):

```csharp
        get
        {
            if (Job.Plan is { } p)
            {
                clipCount ??= ClipDetector.Detect(p.Scan.Files).Count;
                return JobCard.Sub(new JobCardFacts(p.Scan.Files.Count, clipCount.Value, p.Scan.TotalBytes, facts?.Camera, p.Mode == PlanMode.Append));
            }
            return facts is { } f ? JobCard.Sub(f) : FilesText;
        }
    }
```

Nahradit:

```csharp
        get
        {
            if (Job.Plan is { } p)
                return JobCard.Sub(new JobCardFacts(p.Scan.Files.Count, PlanClips.Count, p.Scan.TotalBytes, facts?.Camera, p.Mode == PlanMode.Append));
            return facts is { } f ? JobCard.Sub(f) : FilesText;
        }
    }
```

Najít (6/6):

```csharp
    public double Progress => progress.PhaseProgress;
    public string DetailText => progress.DetailText;

    public string Title => Job.Plan is { Mode: PlanMode.Append } p ? AppendRules.Title(p.CardName) : Job.Plan?.CardName ?? sourceName;
    /// The card folder's name (notifications, spec 4.3).
    internal string CardName => Job.Plan?.CardName ?? sourceName;
    public string FilesText
```

Nahradit:

```csharp
    public double Progress => progress.PhaseProgress;
    public string DetailText => progress.DetailText;

    public string Title => Job.Plan switch
    {
        { Mode: PlanMode.Append } p => AppendRules.Title(p.CardName),
        { Mode: PlanMode.Repair } p => RepairRules.Title(p.CardName),
        { } p => p.CardName,
        _ => sourceName,
    };
    /// The card folder's name (notifications, spec 4.3).
    internal string CardName => Job.Plan?.CardName ?? sourceName;
    public string FilesText
```

#### `windows/src/Offload.App/MainViewModel.Queue.cs`

Najít (1/1):

```csharp
    /// The copied card is in its drive per the last scan (not pulled out, not replaced by another card).
    internal bool CardInDrive(JobItemViewModel item) => QueueRules.CardInDrive(item.Job.Config.Source, item.Runner.SourceIdentity, volumes);

    QueueItem? Find(IQueuedJob job) => Jobs.FirstOrDefault(j => ReferenceEquals(j.Queued, job));

    void OnCopyFinished(IQueuedJob job)
```

Nahradit:

```csharp
    /// The copied card is in its drive per the last scan (not pulled out, not replaced by another card).
    internal bool CardInDrive(JobItemViewModel item) => QueueRules.CardInDrive(item.Job.Config.Source, item.Runner.SourceIdentity, volumes);

    /// "Zkopírovat znovu" needs the card the job copied: a drive root must hold that card now; a folder is taken as there
    /// (the repair's planner reads it first and refuses a changed one). Memory only.
    internal bool SourcePresent(JobItemViewModel item)
    {
        var source = item.Job.Config.Source;
        try
        {
            bool driveRoot = string.Equals(Layout.DriveRootOf(source)?.TrimEnd('\\'), source.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            return !driveRoot || CardInDrive(item);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// A repair of the same backup is queued or running (one at a time; the folder lock would refuse a second).
    internal bool RepairPending(JobItemViewModel item) => item.Job.Plan is { } plan && Jobs.OfType<JobItemViewModel>().Any(j =>
        !ReferenceEquals(j, item) && !j.IsFinished && j.Job.Config.RepairInto is { } into && RepairRules.SameBackup(into, plan.CardPaths));

    /// "Zkopírovat znovu" (UI návrh 4): a repair job — only the failed files, only where they failed — with the finished
    /// job's own settings; it waits in the copy queue like any card (it reads the card).
    internal void Repair(JobItemViewModel item)
    {
        if (closing || item.Job.Plan is not { } plan || !item.CanRepair) return;
        var repair = new JobItemViewModel(this, new OffloadJob(JobConfigs.Repair(item.Job.Config, plan)), plan.CardName, item.CanEject);
        Jobs.Insert(0, repair);
        queue.Enqueue(repair.Runner);
        RaiseMany(nameof(HasJobs), nameof(HasFinished));
    }

    /// The rest of a running job (its phase's ETA and what follows), from what this session measured; null when not known.
    internal double? RemainingTotal(JobItemViewModel item, double? phaseEta) => item.Job.Plan is { } plan
        ? rates.Remaining(item.Phase, phaseEta, plan.Scan.TotalBytes, item.PlanClips, item.Job.Config.Verify,
            item.Job.Config.AnalyzeClips && plan.Mode != PlanMode.Repair, plan.Mode == PlanMode.Repair ? new ProxySettings() : item.Job.Config.Proxy)
        : null;

    QueueItem? Find(IQueuedJob job) => Jobs.FirstOrDefault(j => ReferenceEquals(j.Queued, job));

    void OnCopyFinished(IQueuedJob job)
```

#### `windows/src/Offload.App/QueuePanel.xaml`

Najít (1/6):

```xml

        <!-- ── Running (obr. 2) ── -->
        <DataTemplate x:Key="RunningCard">
            <Border CornerRadius="16" BorderBrush="{DynamicResource RunBorder}" BorderThickness="1" Background="{DynamicResource RunBg}" Padding="24,22" Margin="0,0,0,18">
                <StackPanel>
                    <DockPanel>
                        <Border DockPanel.Dock="Left" Width="42" Height="42" CornerRadius="10" Background="{DynamicResource AccentSoft}" Margin="0,0,14,0">
```

Nahradit:

```xml

        <!-- ── Running (obr. 2) ── -->
        <DataTemplate x:Key="RunningCard">
            <StackPanel Margin="0,0,0,18">
            <Border CornerRadius="16" BorderBrush="{DynamicResource RunBorder}" BorderThickness="1" Background="{DynamicResource RunBg}" Padding="24,22">
                <StackPanel>
                    <DockPanel>
                        <Border DockPanel.Dock="Left" Width="42" Height="42" CornerRadius="10" Background="{DynamicResource AccentSoft}" Margin="0,0,14,0">
```

Najít (2/6):

```xml
                        <StackPanel DockPanel.Dock="Right" HorizontalAlignment="Right">
                            <TextBlock Text="{Binding PercentText}" FontSize="30" FontWeight="SemiBold" HorizontalAlignment="Right" Typography.NumeralAlignment="Tabular"
                                       AutomationProperties.AutomationId="JobPercentText" />
                            <TextBlock Text="{Binding StatusText}" Style="{StaticResource TextMuted}" HorizontalAlignment="Right" AutomationProperties.AutomationId="JobStatusText" />
                        </StackPanel>
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding Title}" FontSize="18" FontWeight="SemiBold" />
                            <TextBlock Text="{Binding Sub}" Style="{StaticResource TextMuted}" Margin="0,2,0,0" />
                        </StackPanel>
                    </DockPanel>
                    <StackPanel Margin="0,22,0,0">
                        <ProgressBar Style="{StaticResource ProgressThin}" Value="{Binding Progress, Mode=OneWay}" />
                        <TextBlock Text="{Binding DetailText}" Style="{StaticResource TextMuted}" Margin="0,8,0,0" />
                        <ItemsControl ItemsSource="{Binding ChainLines}" Margin="0,8,0,0" Visibility="{Binding HasChain, Converter={StaticResource BoolToVisible}}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding}" Style="{StaticResource TextMonoFaint}" />
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                    <TextBlock Text="{Binding WarningText}" Style="{StaticResource TextWarn}" Margin="0,12,0,0" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobWarningText" />
                    <TextBlock Text="{Binding ProfileNote}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding ProfileNote, Converter={StaticResource NullToCollapsed}}"
```

Nahradit:

```xml
                        <StackPanel DockPanel.Dock="Right" HorizontalAlignment="Right">
                            <TextBlock Text="{Binding PercentText}" FontSize="30" FontWeight="SemiBold" HorizontalAlignment="Right" Typography.NumeralAlignment="Tabular"
                                       AutomationProperties.AutomationId="JobPercentText" />
                            <TextBlock Text="{Binding RemainingText}" Style="{StaticResource TextMuted}" HorizontalAlignment="Right" TextWrapping="NoWrap"
                                       Visibility="{Binding RemainingText, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="JobRemainingText" />
                            <TextBlock Text="{Binding StatusText}" Style="{StaticResource TextFaint}" HorizontalAlignment="Right" AutomationProperties.AutomationId="JobStatusText" />
                        </StackPanel>
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding Title}" FontSize="18" FontWeight="SemiBold" />
                            <TextBlock Text="{Binding Sub}" Style="{StaticResource TextMuted}" Margin="0,2,0,0" />
                        </StackPanel>
                    </DockPanel>
                    <!-- Phase indicator (nová věc 2): Kopírování · Ověření · Proxy · Report -->
                    <ItemsControl ItemsSource="{Binding Steps}" Margin="0,22,0,0" AutomationProperties.AutomationId="JobSteps">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <UniformGrid Rows="1" />
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <StackPanel Margin="0,0,8,0">
                                    <ProgressBar Style="{StaticResource ProgressSegment}" Value="{Binding Fill, Mode=OneWay}" />
                                    <TextBlock Text="{Binding Text}" FontSize="13" Margin="0,8,0,0">
                                        <TextBlock.Style>
                                            <Style TargetType="TextBlock">
                                                <Setter Property="Foreground" Value="{DynamicResource Text3}" />
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding State}" Value="Active">
                                                        <Setter Property="Foreground" Value="{DynamicResource Text}" />
                                                        <Setter Property="FontWeight" Value="SemiBold" />
                                                    </DataTrigger>
                                                    <DataTrigger Binding="{Binding State}" Value="Done">
                                                        <Setter Property="Foreground" Value="{DynamicResource Text2}" />
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBlock.Style>
                                    </TextBlock>
                                </StackPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <!-- Speed table (nová věc 1): Čtení karty + every destination — name, share, MB/s -->
                    <Border CornerRadius="12" Background="{DynamicResource Input}" BorderBrush="{DynamicResource LineSoft}" BorderThickness="1" Margin="0,22,0,0" ClipToBounds="True"
                            Visibility="{Binding Rows.Count, Converter={StaticResource CountToVisible}}">
                        <ItemsControl ItemsSource="{Binding Rows}" AutomationProperties.AutomationId="JobRows">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border BorderBrush="{DynamicResource RowLine}" BorderThickness="0,0,0,1" Padding="16,12" ToolTip="{Binding Tooltip}">
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="1.4*" />
                                                <ColumnDefinition Width="3*" />
                                                <ColumnDefinition Width="0.9*" />
                                            </Grid.ColumnDefinitions>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                                <Border Width="18" Height="18" CornerRadius="9" Background="{DynamicResource Track}" Margin="0,0,8,0"
                                                        Visibility="{Binding HasNumber, Converter={StaticResource BoolToVisible}}">
                                                    <TextBlock Text="{Binding Number}" FontSize="11" FontWeight="SemiBold" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                                </Border>
                                                <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconCardPlain}" Width="15" Height="15" Margin="0,0,8,0"
                                                                Foreground="{DynamicResource TextSoft}" Visibility="{Binding HasNumber, Converter={StaticResource BoolToCollapsed}}" />
                                                <TextBlock Text="{Binding Name}" Foreground="{DynamicResource TextSoft}" TextTrimming="CharacterEllipsis" />
                                            </StackPanel>
                                            <ProgressBar Grid.Column="1" Style="{StaticResource ProgressThin}" Value="{Binding Share, Mode=OneWay}" Margin="16,0" VerticalAlignment="Center" />
                                            <TextBlock Grid.Column="2" Text="{Binding RateText}" Style="{StaticResource TextMono}" Foreground="{DynamicResource Text}" TextAlignment="Right" VerticalAlignment="Center" />
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </Border>
                    <TextBlock Text="{Binding WarningText}" Style="{StaticResource TextWarn}" Margin="0,12,0,0" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobWarningText" />
                    <TextBlock Text="{Binding ProfileNote}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding ProfileNote, Converter={StaticResource NullToCollapsed}}"
```

Najít (3/6):

```xml
                                Visibility="{Binding ShowEjectButton, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="EjectButton" />
                        <TextBlock Text="{Binding SafeText}" Foreground="{DynamicResource Success}" TextWrapping="Wrap" VerticalAlignment="Center" />
                    </DockPanel>
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
                        <Button Style="{StaticResource DangerButton}" Content="Zrušit" Command="{Binding CancelCommand}" AutomationProperties.AutomationId="CancelButton"
                                Visibility="{Binding CanCancel, Converter={StaticResource BoolToVisible}}" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </DataTemplate>

        <!-- ── Done, green (obr. 3) ── -->
```

Nahradit:

```xml
                                Visibility="{Binding ShowEjectButton, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="EjectButton" />
                        <TextBlock Text="{Binding SafeText}" Foreground="{DynamicResource Success}" TextWrapping="Wrap" VerticalAlignment="Center" />
                    </DockPanel>
                    <TextBlock Text="{Binding PauseText}" Style="{StaticResource TextWarn}" Margin="0,12,0,0" Visibility="{Binding PauseText, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobPauseText" />
                    <!-- "Soubor 10 / 24" + the file, Pozastavit / Pokračovat (nová věc 9), Zrušit -->
                    <DockPanel Margin="0,16,0,0">
                        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Center">
                            <Button Style="{StaticResource SecondaryButton}" Height="36" Padding="14,0" Content="Pozastavit" Command="{Binding PauseCommand}" Margin="0,0,8,0"
                                    Visibility="{Binding IsPaused, Converter={StaticResource BoolToCollapsed}}" AutomationProperties.AutomationId="PauseButton" />
                            <Button Style="{StaticResource SecondaryButton}" Height="36" Padding="14,0" Content="Pokračovat" Command="{Binding ResumeCommand}" Margin="0,0,8,0"
                                    Visibility="{Binding IsPaused, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="ResumeButton" />
                            <Button Style="{StaticResource DangerButton}" Content="Zrušit" Command="{Binding CancelCommand}" AutomationProperties.AutomationId="CancelButton"
                                    Visibility="{Binding CanCancel, Converter={StaticResource BoolToVisible}}" />
                        </StackPanel>
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding FileCaption}" FontSize="12" Foreground="{DynamicResource Text3}" AutomationProperties.AutomationId="JobFileCaption" />
                            <TextBlock Text="{Binding FileValue}" Style="{StaticResource TextMono}" TextTrimming="CharacterEllipsis" Margin="0,3,0,0" ToolTip="{Binding DetailText}" />
                        </StackPanel>
                    </DockPanel>
                </StackPanel>
            </Border>
            <!-- "Nejpomalejší článek: …" (progress.Brake) -->
            <DockPanel Margin="4,14,4,0" Visibility="{Binding BrakeText, Converter={StaticResource NullToCollapsed}}">
                <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconInfo}" Width="15" Height="15" Foreground="{DynamicResource Text3}" Margin="0,0,10,0" />
                <TextBlock Text="{Binding BrakeText}" FontSize="13" Foreground="{DynamicResource Text3}" TextWrapping="Wrap" AutomationProperties.AutomationId="JobBrakeText" />
            </DockPanel>
            </StackPanel>
        </DataTemplate>

        <!-- ── Done, green (obr. 3) ── -->
```

Najít (4/6):

```xml
                            <TextBlock Text="Karta vysunuta" Foreground="{DynamicResource SuccessLabel}" Margin="0,4,0,0" Visibility="{Binding Ejected, Converter={StaticResource BoolToVisible}}" />
                        </StackPanel>
                    </DockPanel>
                    <Border BorderBrush="{DynamicResource SuccessLine}" BorderThickness="0,1,0,0" Padding="24,18">
                        <TextBlock Text="{Binding SummaryText}" Foreground="{DynamicResource SuccessText2}" TextWrapping="Wrap" />
                    </Border>
                    <StackPanel Margin="24,0,24,18">
                        <TextBlock Text="{Binding NoteText}" Foreground="{DynamicResource SuccessText2}" TextWrapping="Wrap" Margin="0,0,0,10"
                                   Visibility="{Binding NoteText, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="JobNoteText" />
                        <TextBlock Text="{Binding WarningText}" Style="{StaticResource TextWarn}" Margin="0,0,0,10" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
```

Nahradit:

```xml
                            <TextBlock Text="Karta vysunuta" Foreground="{DynamicResource SuccessLabel}" Margin="0,4,0,0" Visibility="{Binding Ejected, Converter={StaticResource BoolToVisible}}" />
                        </StackPanel>
                    </DockPanel>
                    <!-- Kopie · Ověření · Proxy · Celkem (nová věc 3) -->
                    <Border BorderBrush="{DynamicResource SuccessLine}" BorderThickness="0,1,0,1" Padding="24,18" Visibility="{Binding SummaryFields.Count, Converter={StaticResource CountToVisible}}">
                        <ItemsControl ItemsSource="{Binding SummaryFields}" ToolTip="{Binding SummaryText}" AutomationProperties.AutomationId="JobSummary">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <UniformGrid Rows="1" />
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <StackPanel>
                                        <TextBlock Text="{Binding Label}" FontSize="12" Foreground="{DynamicResource SuccessLabel}" />
                                        <TextBlock Text="{Binding Value}" FontSize="18" FontWeight="SemiBold" Typography.NumeralAlignment="Tabular" Margin="0,2,0,0" />
                                    </StackPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </Border>
                    <!-- the first five clips (middle frame) and "+N" -->
                    <ItemsControl ItemsSource="{Binding Thumbs}" Margin="24,18,24,0" Visibility="{Binding HasThumbs, Converter={StaticResource BoolToVisible}}">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <UniformGrid Rows="1" Columns="6" />
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border CornerRadius="6" Background="{DynamicResource ThumbBg}" Margin="0,0,8,0" ClipToBounds="True">
                                    <Viewbox Stretch="Uniform">
                                        <Grid Width="160" Height="90">
                                            <Image Source="{Binding Image}" Stretch="UniformToFill" />
                                            <TextBlock Text="{Binding Clip}" FontFamily="{StaticResource MonoFont}" FontSize="10" Foreground="White" Margin="6"
                                                       VerticalAlignment="Bottom" HorizontalAlignment="Left">
                                                <TextBlock.Effect>
                                                    <DropShadowEffect BlurRadius="4" ShadowDepth="0" Opacity="0.8" />
                                                </TextBlock.Effect>
                                            </TextBlock>
                                            <!-- the last tile: "+18" more clips -->
                                            <TextBlock Text="{Binding More}" FontSize="12" Foreground="{DynamicResource SuccessText2}" HorizontalAlignment="Center" VerticalAlignment="Center"
                                                       Visibility="{Binding More, Converter={StaticResource NullToCollapsed}}" />
                                        </Grid>
                                    </Viewbox>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <StackPanel Margin="24,14,24,18">
                        <TextBlock Text="{Binding NoteText}" Foreground="{DynamicResource SuccessText2}" TextWrapping="Wrap" Margin="0,0,0,10"
                                   Visibility="{Binding NoteText, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="JobNoteText" />
                        <TextBlock Text="{Binding WarningText}" Style="{StaticResource TextWarn}" Margin="0,0,0,10" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
```

Najít (5/6):

```xml
                                        <TextBlock Text="Otevřít složku" />
                                    </StackPanel>
                                </Button>
                            </StackPanel>
                        </DockPanel>
                    </StackPanel>
```

Nahradit:

```xml
                                        <TextBlock Text="Otevřít složku" />
                                    </StackPanel>
                                </Button>
                                <Button Style="{StaticResource SuccessSecondaryButton}" Command="{Binding OpenProxyCommand}" Margin="0,0,8,0" AutomationProperties.AutomationId="ProxyFolderOpenButton"
                                        Visibility="{Binding HasProxies, Converter={StaticResource BoolToVisible}}">
                                    <StackPanel Orientation="Horizontal">
                                        <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconVideo}" Width="15" Height="15" Margin="0,0,8,0"
                                                        Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" />
                                        <TextBlock Text="Proxy" />
                                    </StackPanel>
                                </Button>
                            </StackPanel>
                        </DockPanel>
                    </StackPanel>
```

Najít (6/6):

```xml
                        <Border DockPanel.Dock="Left" Width="40" Height="40" CornerRadius="11" Background="{DynamicResource WarnFill}" Margin="0,0,14,0" VerticalAlignment="Top">
                            <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconWarning}" Width="22" Height="22" Tag="2" Foreground="{DynamicResource OnWarn}" />
                        </Border>
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding HeadTitle}" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                            <TextBlock Text="{Binding HeadSub}" Foreground="{DynamicResource WarnText2}" TextWrapping="Wrap" Margin="0,3,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock Text="{Binding FailedText}" Foreground="{DynamicResource WarnText2}" TextWrapping="Wrap" Margin="0,12,0,0"
                               Visibility="{Binding FailedText, Converter={StaticResource NullToCollapsed}}" />
                    <TextBlock Text="{Binding ErrorText}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding ErrorText, Converter={StaticResource NullToCollapsed}}" />
                    <TextBlock Text="{Binding WarningText}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobWarningText" />
```

Nahradit:

```xml
                        <Border DockPanel.Dock="Left" Width="40" Height="40" CornerRadius="11" Background="{DynamicResource WarnFill}" Margin="0,0,14,0" VerticalAlignment="Top">
                            <ContentControl Style="{StaticResource IconLine}" Content="{StaticResource IconWarning}" Width="22" Height="22" Tag="2" Foreground="{DynamicResource OnWarn}" />
                        </Border>
                        <!-- "Zkopírovat znovu": only the failed files, only where they failed (nová věc 4) -->
                        <Button DockPanel.Dock="Right" Style="{StaticResource WarnButton}" Content="Zkopírovat znovu" Command="{Binding RepairCommand}" VerticalAlignment="Top"
                                Visibility="{Binding CanRepair, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="RepairButton"
                                ToolTip="Znovu zkopíruje jen nesouhlasné soubory a jen na disk, kde nesouhlasí; nesouhlasnou kopii odloží do _REPORT
eshoda_NNNN." />
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding HeadTitle}" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                            <TextBlock Text="{Binding HeadSub}" Foreground="{DynamicResource WarnText2}" TextWrapping="Wrap" Margin="0,3,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                        </StackPanel>
                    </DockPanel>
                    <!-- path · state on each destination ("RAID_A ✓", "SHUTTLE_B ✕ hash") -->
                    <Border CornerRadius="10" Background="{DynamicResource WarnListBg}" BorderBrush="{DynamicResource WarnListBorder}" BorderThickness="1" Padding="14,10" Margin="0,14,0,0"
                            Visibility="{Binding FailedRows.Count, Converter={StaticResource CountToVisible}}">
                        <StackPanel>
                            <ItemsControl ItemsSource="{Binding FailedRows}" AutomationProperties.AutomationId="FailedFiles">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Grid Margin="0,2">
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="2*" />
                                                <ColumnDefinition Width="2*" />
                                            </Grid.ColumnDefinitions>
                                            <TextBlock Text="{Binding Path}" FontFamily="{StaticResource MonoFont}" FontSize="12" TextWrapping="Wrap" Margin="0,0,12,0" />
                                            <ItemsControl Grid.Column="1" ItemsSource="{Binding Disks}">
                                                <ItemsControl.ItemsPanel>
                                                    <ItemsPanelTemplate>
                                                        <UniformGrid Rows="1" />
                                                    </ItemsPanelTemplate>
                                                </ItemsControl.ItemsPanel>
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate>
                                                        <TextBlock Text="{Binding Text}" FontFamily="{StaticResource MonoFont}" FontSize="12" TextTrimming="CharacterEllipsis">
                                                            <TextBlock.Style>
                                                                <Style TargetType="TextBlock">
                                                                    <Setter Property="Foreground" Value="{DynamicResource DangerText2}" />
                                                                    <Style.Triggers>
                                                                        <DataTrigger Binding="{Binding Ok}" Value="True">
                                                                            <Setter Property="Foreground" Value="{DynamicResource SuccessText2}" />
                                                                        </DataTrigger>
                                                                    </Style.Triggers>
                                                                </Style>
                                                            </TextBlock.Style>
                                                        </TextBlock>
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                            </ItemsControl>
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                            <TextBlock Text="{Binding MoreFailed}" FontSize="12" Foreground="{DynamicResource WarnText2}" Margin="0,6,0,0"
                                       Visibility="{Binding MoreFailed, Converter={StaticResource NullToCollapsed}}" />
                        </StackPanel>
                    </Border>
                    <TextBlock Text="{Binding ErrorText}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding ErrorText, Converter={StaticResource NullToCollapsed}}" />
                    <TextBlock Text="{Binding WarningText}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobWarningText" />
```

- [ ] **Step 2: Build a celá sada**

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 upozornění, 0 chyb`; celá sada `Neúspěšné: 0` (časově citlivé `ProxyWatchdogTests` / `ProxyRendererTests.Feeder_*` při jednom selhání pusťte znovu samostatně — viz Global Constraints).

Expected navíc: žádná chyba XAML při buildu (`MC3000`, `MC3072`). Chybějící klíč zdroje se ukáže až za běhu, proto
zkontrolujte, že každý `StaticResource` / `DynamicResource` v oknech je definovaný (`App.xaml`, `Themes\*.xaml` nebo
v samotném souboru); klíče Fluent tématu (`…Brush`, `AccentButtonStyle`) definuje WPF:

```powershell
$xaml = @(Get-ChildItem windows\src\Offload.App\*.xaml) + @(Get-ChildItem windows\src\Offload.App\Themes\*.xaml)
Select-String -Path windows\src\Offload.App\*.xaml -Pattern '(Static|Dynamic)Resource (\w+)' -AllMatches | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[2].Value } | Sort-Object -Unique | Where-Object { $_ -notmatch 'Brush$|^AccentButtonStyle$' -and -not (Select-String -Path $xaml.FullName -Pattern "x:Key=`"$_`"" -Quiet) }
```

Expected: prázdný výstup.

- [ ] **Step 3: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)`.

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1):

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 14 — fronta se stávajícími daty (postup 4, 1. část): prázdná fronta = karta s ikonami Karta → 2 disky naráz → Ověření → Proxy → Report (jen co projekt dělá) a „Připraveno — A035_ARRI na 2 disky“ + věta (bez karty „Zatím žádný offload“); karta úlohy mění vzhled podle `JobCard.Headline` (čeká: kompaktní řádek „B012_FX6 — doplnění · 2 nové klipy · 14,2 GB · stejné cíle · Čeká ve frontě“ se Zrušit; běží: ikona, název, „24 klipů · 1,70 GB · ALEXA 35“, velké „38 %“, průběh a dosavadní texty, Zrušit červeným obrysem; hotovo zeleně: štít, „… — kartu lze vysunout“, Vysunout kartu, souhrn, Report PDF, Otevřít složku, cesta; nesouhlasí žlutě: „… — kartu zatím nevysunujte“ + věta; chyba červeně; zrušeno šedě); ověření zálohy jako karta ve stejném stylu; „Fronta 2“ s počtem nedokončených, „Vyčistit hotové“ jako odkaz; karta si pamatuje klipy, velikost a kameru z prověření při startu
- Úkol 13 — levý sloupec (postup 3): kroky „1 · KARTY“ (⟳ a „Ze složky…“, prázdný stav v přerušovaném rámečku „Vložte kartu do čtečky“ / během offloadu „Vložte další kartu“, dlaždice karty: ikona, název, štítek kamery, „24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s“, řádek historie, zaškrtávátko, žluté „Doplnit“ u karty s novými klipy — zaškrtne i kartu, pole „Název složky — <nápověda>“ v monospace), „2 · CÍLE“ (+ Přidat disk, dlaždice s kolečkem, „7,20 TB volných z 8 TB · NTFS · NVMe“, ✕, pruh místa šedá / modrá / žlutá / červená a věta kontroly místa pod ním, exFAT, nepřipojený disk, náhled cesty „…\2026-10-07\A035_ARRI\“, jen jeden cíl) a „3 · PO ZKOPÍROVÁNÍ“ (4 řádky se zeleným odškrtnutím, vypnuté ověření žlutě, „Upravit projekt“ otevře Nastavení, pod tím červené / oranžové důvody, proč nejde start); připnutá spodní lišta „**1 karta → 2 disky** · 1,70 GB“ + odhad, hlášky místa a startu, „▶ Spustit offload [Ctrl ↵]“ / „Zařadit do fronty“; bez karty a s úlohami ve frontě kompaktní kroky 2 a 3 (obr. 2, 3); spojení karet se zjišťuje mimo UI vlákno, kamera s prověřením karty; volby Struktura, Proxy, Dekodéry a Hardware z levého panelu zmizely (jsou v Nastavení a v liště); `SetupTexts.CardFacts`, `Total`
- Úkol 12 — stránka Nastavení (postup 6, předsunutý před levý sloupec, aby žádná volba nebyla ani na jeden commit nedostupná): ikona s posuvníky v liště nebo Ctrl+, otevře místo obou sloupců stránku s lištou „‹ Zpět · Projekt <název> · Uložit jako nový projekt… · Hotovo“ (Esc zavře) a sekcemi Struktura složek (cesta, vlastní datum, název podle klipů) · Ověření (vypínač, kontrolní součet, MD5, Ověřit zálohu… + „Ukládat report ověření“) · Analýza a náhledy · **Proxy** podle obr. 4 (vypínač, segmenty Kodek a Výška, datový tok s „≈ 75 MB / minutu“, výřez, LUT + tabulka vestavěných LUTů s „použito“, „Když kameru nepoznám“, Pravidla LUTů…, Vlastní .cube…, zvuk, kamerové proxy, kopie na všechny cíle, složka pro proxy; vpravo šedý náhled burn-inu 16:9 s názvem klipu, TC a frame lines výřezu) · Report (CSV, ALE, denní report po kartě, Denní report…) · Oznámení (souhrn + Upravit oznámení…) · Dekodéry RAW (nástroje, Gen 4 → Gen 5) · Aplikace (verze, Aktualizace…, Prověřit USB porty…); úpravy jdou do zobrazeného profilu jako dosud (`SettingsTexts`: řádky LUTů, tok za minutu, rám výřezu, souhrn oznámení)
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 15 — fronta, nové věci 1–4, 9 a 10 (postup 4, 2. část): běžící karta má ukazatel fází (4 segmenty, hotové plné, aktivní podle průběhu), tabulku rychlostí na pozadí `Input` („Čtení karty“ + každý cíl s číslem, pruh, MB/s v monospace; bublina = dosavadní řádek řetězu: výrobek, připojení), „Soubor 10 / 24“ + soubor, **Pozastavit / Pokračovat** a Zrušit, text pauzy, „zbývá ~0:02 kopie · ~6:00 celkem“ pod procenty a pod kartou „Nejpomalejší článek: …“; zelená karta má řádek čtyř čísel Kopie · Ověření · Proxy · Celkem, prvních 5 náhledů (prostřední snímek, dekódovaný mimo UI vlákno) + dlaždici „+N“ a tlačítko Proxy; žlutá karta má seznam nesouhlasných souborů (cesta · „RAID_A ✓“ · „SHUTTLE_B ✕ hash“, prvních 5 + „… a další N souborů v reportu“) a **Zkopírovat znovu** — úloha „A001 · oprava“ ve frontě se stejným nastavením jako karta, nabízí se jen s kartou ve čtečce a bez jiné opravy téže zálohy; řádky tabulky a fáze se aktualizují na místě (žádné překreslování seznamu 3× za sekundu)
- Úkol 14 — fronta se stávajícími daty (postup 4, 1. část): prázdná fronta = karta s ikonami Karta → 2 disky naráz → Ověření → Proxy → Report (jen co projekt dělá) a „Připraveno — A035_ARRI na 2 disky“ + věta (bez karty „Zatím žádný offload“); karta úlohy mění vzhled podle `JobCard.Headline` (čeká: kompaktní řádek „B012_FX6 — doplnění · 2 nové klipy · 14,2 GB · stejné cíle · Čeká ve frontě“ se Zrušit; běží: ikona, název, „24 klipů · 1,70 GB · ALEXA 35“, velké „38 %“, průběh a dosavadní texty, Zrušit červeným obrysem; hotovo zeleně: štít, „… — kartu lze vysunout“, Vysunout kartu, souhrn, Report PDF, Otevřít složku, cesta; nesouhlasí žlutě: „… — kartu zatím nevysunujte“ + věta; chyba červeně; zrušeno šedě); ověření zálohy jako karta ve stejném stylu; „Fronta 2“ s počtem nedokončených, „Vyčistit hotové“ jako odkaz; karta si pamatuje klipy, velikost a kameru z prověření při startu
- Úkol 13 — levý sloupec (postup 3): kroky „1 · KARTY“ (⟳ a „Ze složky…“, prázdný stav v přerušovaném rámečku „Vložte kartu do čtečky“ / během offloadu „Vložte další kartu“, dlaždice karty: ikona, název, štítek kamery, „24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s“, řádek historie, zaškrtávátko, žluté „Doplnit“ u karty s novými klipy — zaškrtne i kartu, pole „Název složky — <nápověda>“ v monospace), „2 · CÍLE“ (+ Přidat disk, dlaždice s kolečkem, „7,20 TB volných z 8 TB · NTFS · NVMe“, ✕, pruh místa šedá / modrá / žlutá / červená a věta kontroly místa pod ním, exFAT, nepřipojený disk, náhled cesty „…\2026-10-07\A035_ARRI\“, jen jeden cíl) a „3 · PO ZKOPÍROVÁNÍ“ (4 řádky se zeleným odškrtnutím, vypnuté ověření žlutě, „Upravit projekt“ otevře Nastavení, pod tím červené / oranžové důvody, proč nejde start); připnutá spodní lišta „**1 karta → 2 disky** · 1,70 GB“ + odhad, hlášky místa a startu, „▶ Spustit offload [Ctrl ↵]“ / „Zařadit do fronty“; bez karty a s úlohami ve frontě kompaktní kroky 2 a 3 (obr. 2, 3); spojení karet se zjišťuje mimo UI vlákno, kamera s prověřením karty; volby Struktura, Proxy, Dekodéry a Hardware z levého panelu zmizely (jsou v Nastavení a v liště); `SetupTexts.CardFacts`, `Total`
- Úkol 12 — stránka Nastavení (postup 6, předsunutý před levý sloupec, aby žádná volba nebyla ani na jeden commit nedostupná): ikona s posuvníky v liště nebo Ctrl+, otevře místo obou sloupců stránku s lištou „‹ Zpět · Projekt <název> · Uložit jako nový projekt… · Hotovo“ (Esc zavře) a sekcemi Struktura složek (cesta, vlastní datum, název podle klipů) · Ověření (vypínač, kontrolní součet, MD5, Ověřit zálohu… + „Ukládat report ověření“) · Analýza a náhledy · **Proxy** podle obr. 4 (vypínač, segmenty Kodek a Výška, datový tok s „≈ 75 MB / minutu“, výřez, LUT + tabulka vestavěných LUTů s „použito“, „Když kameru nepoznám“, Pravidla LUTů…, Vlastní .cube…, zvuk, kamerové proxy, kopie na všechny cíle, složka pro proxy; vpravo šedý náhled burn-inu 16:9 s názvem klipu, TC a frame lines výřezu) · Report (CSV, ALE, denní report po kartě, Denní report…) · Oznámení (souhrn + Upravit oznámení…) · Dekodéry RAW (nástroje, Gen 4 → Gen 5) · Aplikace (verze, Aktualizace…, Prověřit USB porty…); úpravy jdou do zobrazeného profilu jako dosud (`SettingsTexts`: řádky LUTů, tok za minutu, rám výřezu, souhrn oznámení)
```

- [ ] **Step 4: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: running card per destination, phases, pause, summary, thumbnails, repair (plan 9, task 15)

The running card shows the phase indicator, the speed table per destination, file N / M, Pozastavit
and Pokračovat, the remaining time and the slowest link; the green card the four-number summary and
the first five thumbnails; the yellow card the failed files per disk and "Zkopírovat znovu".

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.App\JobItemViewModel.Card.cs windows\src\Offload.App\JobItemViewModel.cs windows\src\Offload.App\MainViewModel.Queue.cs windows\src\Offload.App\QueuePanel.xaml
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 16: Dnes a Historie karet (postup 5, nová věc 7)

Pod frontou „DNES“ se souhrnem, odkazem „Denní report…“ a řádky dnešních karet; záložka „Historie karet“ se
všemi dny. Obojí z `history.json`, které okno už načítá mimo UI vlákno. Bez vlastního testu (logika je `HistoryView`,
úkol 9).

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.App/App.xaml`
- Modify: `windows/src/Offload.App/Converters.cs`
- Modify: `windows/src/Offload.App/MainViewModel.History.cs`
- Modify: `windows/src/Offload.App/QueuePanel.xaml`

**Interfaces:**
- Consumes: `HistoryView.Today`, `HistoryView.Days` (úkol 9), `DayReportCommand` (plán 7).
- Produces: `MainViewModel.QueueTab` (`"queue"` / `"history"`), `Today` (`HistoryDay?`), `HistoryDays`; konvertor `CountToCollapsed`; AutomationId `TodaySummary`, `TodayRows`, `DayReportButton`, `HistoryTab`, `HistoryDays`, `HistoryDayReportButton`.

- [ ] **Step 1: Implementace**

GUI se při tomto úkolu **nespouští** (vzhled zkontroluje úkol 19).

#### `windows/src/Offload.App/App.xaml`

Najít (1/1):

```xml
        <local:EqualsConverter x:Key="Equals" />
        <local:EqualsToVisibleConverter x:Key="EqualsToVisible" />
        <local:CountToVisibleConverter x:Key="CountToVisible" />
        <local:IsNullConverter x:Key="IsNull" />
        <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
        <Style x:Key="SectionTitle" TargetType="TextBlock">
```

Nahradit:

```xml
        <local:EqualsConverter x:Key="Equals" />
        <local:EqualsToVisibleConverter x:Key="EqualsToVisible" />
        <local:CountToVisibleConverter x:Key="CountToVisible" />
        <local:CountToCollapsedConverter x:Key="CountToCollapsed" />
        <local:IsNullConverter x:Key="IsNull" />
        <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
        <Style x:Key="SectionTitle" TargetType="TextBlock">
```

#### `windows/src/Offload.App/Converters.cs`

Najít (1/1):

```csharp
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class IsNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is null;
```

Nahradit:

```csharp
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CountToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int n && n > 0 ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class IsNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is null;
```

#### `windows/src/Offload.App/MainViewModel.History.cs`

Najít (1/2):

```csharp
                AppLog.Write($"history load: {t.Exception?.GetBaseException().Message}");
                history ??= [];
            }
            InspectAll();
            if (historyReload)
            {
```

Nahradit:

```csharp
                AppLog.Write($"history load: {t.Exception?.GetBaseException().Message}");
                history ??= [];
            }
            RaiseMany(nameof(Today), nameof(HistoryDays));   // the right column's DNES and the history tab
            InspectAll();
            if (historyReload)
            {
```

Najít (2/2):

```csharp
    SourceItemViewModel? RowFor(string path) =>
        Sources.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

    /// The last look at the card that is at `path` now, or null.
    internal CardInsight? Insight(string path) => inspections.Result(path, IdentityOf(path));
```

Nahradit:

```csharp
    SourceItemViewModel? RowFor(string path) =>
        Sources.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

    string queueTab = "queue";

    /// "queue" (Fronta) or "history" (Historie karet).
    public string QueueTab
    {
        get => queueTab;
        set => Set(ref queueTab, value ?? "queue");
    }

    /// DNES under the queue (UI návrh 7): the cards finished today, from history.json as loaded; null hides it.
    public HistoryDay? Today => history is { } h ? HistoryView.Today(h, DateTime.Now) : null;

    /// The history tab: every day with a card, newest first.
    public IReadOnlyList<HistoryDay> HistoryDays => history is { } h ? HistoryView.Days(h, DateTime.Now) : [];

    /// The last look at the card that is at `path` now, or null.
    internal CardInsight? Insight(string path) => inspections.Result(path, IdentityOf(path));
```

#### `windows/src/Offload.App/QueuePanel.xaml`

Najít (1/3):

```xml
                </StackPanel>
            </Border>
        </DataTemplate>
    </UserControl.Resources>

    <DockPanel Background="{DynamicResource Bg}">
```

Nahradit:

```xml
                </StackPanel>
            </Border>
        </DataTemplate>
        <!-- One card of the history (nová věc 7): name · size · time · "✓ 2 kopie ověřeny" -->
        <DataTemplate x:Key="HistoryRowTemplate">
            <Border BorderBrush="{DynamicResource RowLine}" BorderThickness="0,0,0,1" Padding="16,12">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="2*" />
                        <ColumnDefinition Width="1*" />
                        <ColumnDefinition Width="1*" />
                        <ColumnDefinition Width="1.2*" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="{Binding CardName}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" ToolTip="{Binding Project}" />
                    <TextBlock Grid.Column="1" Text="{Binding Size}" Foreground="{DynamicResource Text2}" />
                    <TextBlock Grid.Column="2" Text="{Binding Time}" Foreground="{DynamicResource Text2}" />
                    <TextBlock Grid.Column="3" Text="{Binding Status}" FontSize="13" TextTrimming="CharacterEllipsis">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Setter Property="Foreground" Value="{DynamicResource Text2}" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding Tone}" Value="Ok">
                                        <Setter Property="Foreground" Value="{DynamicResource Success}" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding Tone}" Value="Warning">
                                        <Setter Property="Foreground" Value="{DynamicResource Warn}" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding Tone}" Value="Error">
                                        <Setter Property="Foreground" Value="{DynamicResource DangerText}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
                </Grid>
            </Border>
        </DataTemplate>
    </UserControl.Resources>

    <DockPanel Background="{DynamicResource Bg}">
```

Najít (2/3):

```xml
        <DockPanel DockPanel.Dock="Top" Margin="28,22,28,0">
            <Button DockPanel.Dock="Right" Style="{StaticResource LinkButton}" Content="Vyčistit hotové" Command="{Binding ClearFinishedCommand}"
                    Visibility="{Binding HasFinished, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="ClearFinishedButton" />
            <StackPanel Orientation="Horizontal">
                <RadioButton Style="{StaticResource TabButton}" IsChecked="True" AutomationProperties.AutomationId="QueueTab">
                    <TextBlock>
                        <Run Text="Fronta" /><Run Text="{Binding QueueBadgeText, Mode=OneWay}" Foreground="{DynamicResource AccentText}" />
                    </TextBlock>
                </RadioButton>
            </StackPanel>
        </DockPanel>
        <Grid>
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel Margin="28,18,28,28">
                    <!-- Empty queue: the flow Karta → disks → Ověření → Proxy → Report and what a start does -->
                    <Border CornerRadius="16" BorderBrush="{DynamicResource LineSoft}" BorderThickness="1" Background="{DynamicResource BgHeader}" Padding="32,44" Margin="0,0,0,22"
```

Nahradit:

```xml
        <DockPanel DockPanel.Dock="Top" Margin="28,22,28,0">
            <Button DockPanel.Dock="Right" Style="{StaticResource LinkButton}" Content="Vyčistit hotové" Command="{Binding ClearFinishedCommand}"
                    Visibility="{Binding HasFinished, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="ClearFinishedButton" />
            <Button DockPanel.Dock="Right" Style="{StaticResource LinkButton}" Content="Denní report…" Command="{Binding DayReportCommand}" Margin="0,0,16,0"
                    Visibility="{Binding QueueTab, Converter={StaticResource EqualsToVisible}, ConverterParameter=history}" AutomationProperties.AutomationId="HistoryDayReportButton" />
            <StackPanel Orientation="Horizontal">
                <RadioButton Style="{StaticResource TabButton}" IsChecked="{Binding QueueTab, Converter={StaticResource Equals}, ConverterParameter=queue}"
                             AutomationProperties.AutomationId="QueueTab">
                    <TextBlock>
                        <Run Text="Fronta" /><Run Text="{Binding QueueBadgeText, Mode=OneWay}" Foreground="{DynamicResource AccentText}" />
                    </TextBlock>
                </RadioButton>
                <RadioButton Style="{StaticResource TabButton}" Content="Historie karet" Margin="4,0,0,0"
                             IsChecked="{Binding QueueTab, Converter={StaticResource Equals}, ConverterParameter=history}" AutomationProperties.AutomationId="HistoryTab" />
            </StackPanel>
        </DockPanel>
        <Grid>
            <ScrollViewer VerticalScrollBarVisibility="Auto" Visibility="{Binding QueueTab, Converter={StaticResource EqualsToVisible}, ConverterParameter=queue}">
                <StackPanel Margin="28,18,28,28">
                    <!-- Empty queue: the flow Karta → disks → Ověření → Proxy → Report and what a start does -->
                    <Border CornerRadius="16" BorderBrush="{DynamicResource LineSoft}" BorderThickness="1" Background="{DynamicResource BgHeader}" Padding="32,44" Margin="0,0,0,22"
```

Najít (3/3):

```xml
                            </local:QueueItemTemplateSelector>
                        </ItemsControl.ItemTemplateSelector>
                    </ItemsControl>
                </StackPanel>
            </ScrollViewer>
        </Grid>
```

Nahradit:

```xml
                            </local:QueueItemTemplateSelector>
                        </ItemsControl.ItemTemplateSelector>
                    </ItemsControl>

                    <!-- DNES (nová věc 7): the cards finished today, from history.json -->
                    <StackPanel Margin="0,4,0,0" Visibility="{Binding Today, Converter={StaticResource NullToCollapsed}}">
                        <DockPanel>
                            <Button DockPanel.Dock="Right" Style="{StaticResource LinkButton}" Content="Denní report…" Command="{Binding DayReportCommand}" Margin="14,0,0,0"
                                    AutomationProperties.AutomationId="DayReportButton" />
                            <TextBlock DockPanel.Dock="Right" Text="{Binding Today.Summary}" FontSize="13" Foreground="{DynamicResource Text3}" VerticalAlignment="Center"
                                       AutomationProperties.AutomationId="TodaySummary" />
                            <TextBlock Text="DNES" Style="{StaticResource StepTitle}" />
                        </DockPanel>
                        <Border Style="{StaticResource ListBorder}" Margin="0,10,0,0">
                            <ItemsControl ItemsSource="{Binding Today.Rows}" ItemTemplate="{StaticResource HistoryRowTemplate}" AutomationProperties.AutomationId="TodayRows" />
                        </Border>
                    </StackPanel>
                </StackPanel>
            </ScrollViewer>

            <!-- Historie karet: every day with a card, newest first -->
            <ScrollViewer VerticalScrollBarVisibility="Auto" Visibility="{Binding QueueTab, Converter={StaticResource EqualsToVisible}, ConverterParameter=history}">
                <StackPanel Margin="28,18,28,28">
                    <TextBlock Text="Zatím žádná zálohovaná karta." Style="{StaticResource TextMuted}" FontSize="14"
                               Visibility="{Binding HistoryDays.Count, Converter={StaticResource CountToCollapsed}}" />
                    <ItemsControl ItemsSource="{Binding HistoryDays}" AutomationProperties.AutomationId="HistoryDays">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <StackPanel Margin="0,0,0,22">
                                    <DockPanel>
                                        <TextBlock DockPanel.Dock="Right" Text="{Binding Summary}" FontSize="13" Foreground="{DynamicResource Text3}" VerticalAlignment="Center" />
                                        <TextBlock Text="{Binding Title}" Style="{StaticResource StepTitle}" />
                                    </DockPanel>
                                    <Border Style="{StaticResource ListBorder}" Margin="0,10,0,0">
                                        <ItemsControl ItemsSource="{Binding Rows}" ItemTemplate="{StaticResource HistoryRowTemplate}" />
                                    </Border>
                                </StackPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ScrollViewer>
        </Grid>
```

- [ ] **Step 2: Build a celá sada**

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 upozornění, 0 chyb`; celá sada `Neúspěšné: 0` (časově citlivé `ProxyWatchdogTests` / `ProxyRendererTests.Feeder_*` při jednom selhání pusťte znovu samostatně — viz Global Constraints).

Expected navíc: žádná chyba XAML při buildu (`MC3000`, `MC3072`). Chybějící klíč zdroje se ukáže až za běhu, proto
zkontrolujte, že každý `StaticResource` / `DynamicResource` v oknech je definovaný (`App.xaml`, `Themes\*.xaml` nebo
v samotném souboru); klíče Fluent tématu (`…Brush`, `AccentButtonStyle`) definuje WPF:

```powershell
$xaml = @(Get-ChildItem windows\src\Offload.App\*.xaml) + @(Get-ChildItem windows\src\Offload.App\Themes\*.xaml)
Select-String -Path windows\src\Offload.App\*.xaml -Pattern '(Static|Dynamic)Resource (\w+)' -AllMatches | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[2].Value } | Sort-Object -Unique | Where-Object { $_ -notmatch 'Brush$|^AccentButtonStyle$' -and -not (Select-String -Path $xaml.FullName -Pattern "x:Key=`"$_`"" -Quiet) }
```

Expected: prázdný výstup.

- [ ] **Step 3: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)`.

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1):

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 15 — fronta, nové věci 1–4, 9 a 10 (postup 4, 2. část): běžící karta má ukazatel fází (4 segmenty, hotové plné, aktivní podle průběhu), tabulku rychlostí na pozadí `Input` („Čtení karty“ + každý cíl s číslem, pruh, MB/s v monospace; bublina = dosavadní řádek řetězu: výrobek, připojení), „Soubor 10 / 24“ + soubor, **Pozastavit / Pokračovat** a Zrušit, text pauzy, „zbývá ~0:02 kopie · ~6:00 celkem“ pod procenty a pod kartou „Nejpomalejší článek: …“; zelená karta má řádek čtyř čísel Kopie · Ověření · Proxy · Celkem, prvních 5 náhledů (prostřední snímek, dekódovaný mimo UI vlákno) + dlaždici „+N“ a tlačítko Proxy; žlutá karta má seznam nesouhlasných souborů (cesta · „RAID_A ✓“ · „SHUTTLE_B ✕ hash“, prvních 5 + „… a další N souborů v reportu“) a **Zkopírovat znovu** — úloha „A001 · oprava“ ve frontě se stejným nastavením jako karta, nabízí se jen s kartou ve čtečce a bez jiné opravy téže zálohy; řádky tabulky a fáze se aktualizují na místě (žádné překreslování seznamu 3× za sekundu)
- Úkol 14 — fronta se stávajícími daty (postup 4, 1. část): prázdná fronta = karta s ikonami Karta → 2 disky naráz → Ověření → Proxy → Report (jen co projekt dělá) a „Připraveno — A035_ARRI na 2 disky“ + věta (bez karty „Zatím žádný offload“); karta úlohy mění vzhled podle `JobCard.Headline` (čeká: kompaktní řádek „B012_FX6 — doplnění · 2 nové klipy · 14,2 GB · stejné cíle · Čeká ve frontě“ se Zrušit; běží: ikona, název, „24 klipů · 1,70 GB · ALEXA 35“, velké „38 %“, průběh a dosavadní texty, Zrušit červeným obrysem; hotovo zeleně: štít, „… — kartu lze vysunout“, Vysunout kartu, souhrn, Report PDF, Otevřít složku, cesta; nesouhlasí žlutě: „… — kartu zatím nevysunujte“ + věta; chyba červeně; zrušeno šedě); ověření zálohy jako karta ve stejném stylu; „Fronta 2“ s počtem nedokončených, „Vyčistit hotové“ jako odkaz; karta si pamatuje klipy, velikost a kameru z prověření při startu
- Úkol 13 — levý sloupec (postup 3): kroky „1 · KARTY“ (⟳ a „Ze složky…“, prázdný stav v přerušovaném rámečku „Vložte kartu do čtečky“ / během offloadu „Vložte další kartu“, dlaždice karty: ikona, název, štítek kamery, „24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s“, řádek historie, zaškrtávátko, žluté „Doplnit“ u karty s novými klipy — zaškrtne i kartu, pole „Název složky — <nápověda>“ v monospace), „2 · CÍLE“ (+ Přidat disk, dlaždice s kolečkem, „7,20 TB volných z 8 TB · NTFS · NVMe“, ✕, pruh místa šedá / modrá / žlutá / červená a věta kontroly místa pod ním, exFAT, nepřipojený disk, náhled cesty „…\2026-10-07\A035_ARRI\“, jen jeden cíl) a „3 · PO ZKOPÍROVÁNÍ“ (4 řádky se zeleným odškrtnutím, vypnuté ověření žlutě, „Upravit projekt“ otevře Nastavení, pod tím červené / oranžové důvody, proč nejde start); připnutá spodní lišta „**1 karta → 2 disky** · 1,70 GB“ + odhad, hlášky místa a startu, „▶ Spustit offload [Ctrl ↵]“ / „Zařadit do fronty“; bez karty a s úlohami ve frontě kompaktní kroky 2 a 3 (obr. 2, 3); spojení karet se zjišťuje mimo UI vlákno, kamera s prověřením karty; volby Struktura, Proxy, Dekodéry a Hardware z levého panelu zmizely (jsou v Nastavení a v liště); `SetupTexts.CardFacts`, `Total`
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 16 — dnešní historie (postup 5, nová věc 7): pod frontou „DNES“ se souhrnem „3 karty · 412 GB · vše ověřeno“, odkazem „Denní report…“ a řádky „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“ (barva podle stavu, bublina = projekt); záložka „Historie karet“ se všemi dny („Dnes“, „Včera“, „4. 10. 2026“, nejvýš 300 karet) a „Denní report…“; obojí z `history.json`, které okno už načítá mimo UI vlákno (po každé dokončené kopii znovu)
- Úkol 15 — fronta, nové věci 1–4, 9 a 10 (postup 4, 2. část): běžící karta má ukazatel fází (4 segmenty, hotové plné, aktivní podle průběhu), tabulku rychlostí na pozadí `Input` („Čtení karty“ + každý cíl s číslem, pruh, MB/s v monospace; bublina = dosavadní řádek řetězu: výrobek, připojení), „Soubor 10 / 24“ + soubor, **Pozastavit / Pokračovat** a Zrušit, text pauzy, „zbývá ~0:02 kopie · ~6:00 celkem“ pod procenty a pod kartou „Nejpomalejší článek: …“; zelená karta má řádek čtyř čísel Kopie · Ověření · Proxy · Celkem, prvních 5 náhledů (prostřední snímek, dekódovaný mimo UI vlákno) + dlaždici „+N“ a tlačítko Proxy; žlutá karta má seznam nesouhlasných souborů (cesta · „RAID_A ✓“ · „SHUTTLE_B ✕ hash“, prvních 5 + „… a další N souborů v reportu“) a **Zkopírovat znovu** — úloha „A001 · oprava“ ve frontě se stejným nastavením jako karta, nabízí se jen s kartou ve čtečce a bez jiné opravy téže zálohy; řádky tabulky a fáze se aktualizují na místě (žádné překreslování seznamu 3× za sekundu)
- Úkol 14 — fronta se stávajícími daty (postup 4, 1. část): prázdná fronta = karta s ikonami Karta → 2 disky naráz → Ověření → Proxy → Report (jen co projekt dělá) a „Připraveno — A035_ARRI na 2 disky“ + věta (bez karty „Zatím žádný offload“); karta úlohy mění vzhled podle `JobCard.Headline` (čeká: kompaktní řádek „B012_FX6 — doplnění · 2 nové klipy · 14,2 GB · stejné cíle · Čeká ve frontě“ se Zrušit; běží: ikona, název, „24 klipů · 1,70 GB · ALEXA 35“, velké „38 %“, průběh a dosavadní texty, Zrušit červeným obrysem; hotovo zeleně: štít, „… — kartu lze vysunout“, Vysunout kartu, souhrn, Report PDF, Otevřít složku, cesta; nesouhlasí žlutě: „… — kartu zatím nevysunujte“ + věta; chyba červeně; zrušeno šedě); ověření zálohy jako karta ve stejném stylu; „Fronta 2“ s počtem nedokončených, „Vyčistit hotové“ jako odkaz; karta si pamatuje klipy, velikost a kameru z prověření při startu
- Úkol 13 — levý sloupec (postup 3): kroky „1 · KARTY“ (⟳ a „Ze složky…“, prázdný stav v přerušovaném rámečku „Vložte kartu do čtečky“ / během offloadu „Vložte další kartu“, dlaždice karty: ikona, název, štítek kamery, „24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s“, řádek historie, zaškrtávátko, žluté „Doplnit“ u karty s novými klipy — zaškrtne i kartu, pole „Název složky — <nápověda>“ v monospace), „2 · CÍLE“ (+ Přidat disk, dlaždice s kolečkem, „7,20 TB volných z 8 TB · NTFS · NVMe“, ✕, pruh místa šedá / modrá / žlutá / červená a věta kontroly místa pod ním, exFAT, nepřipojený disk, náhled cesty „…\2026-10-07\A035_ARRI\“, jen jeden cíl) a „3 · PO ZKOPÍROVÁNÍ“ (4 řádky se zeleným odškrtnutím, vypnuté ověření žlutě, „Upravit projekt“ otevře Nastavení, pod tím červené / oranžové důvody, proč nejde start); připnutá spodní lišta „**1 karta → 2 disky** · 1,70 GB“ + odhad, hlášky místa a startu, „▶ Spustit offload [Ctrl ↵]“ / „Zařadit do fronty“; bez karty a s úlohami ve frontě kompaktní kroky 2 a 3 (obr. 2, 3); spojení karet se zjišťuje mimo UI vlákno, kamera s prověřením karty; volby Struktura, Proxy, Dekodéry a Hardware z levého panelu zmizely (jsou v Nastavení a v liště); `SetupTexts.CardFacts`, `Total`
```

- [ ] **Step 4: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: DNES under the queue and the Historie karet tab (plan 9, task 16)

Today's cards with the summary and "Denní report…" under the queue, every day in the history tab,
both from history.json loaded off the UI thread.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.App\App.xaml windows\src\Offload.App\Converters.cs windows\src\Offload.App\MainViewModel.History.cs windows\src\Offload.App\QueuePanel.xaml
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 17: Ukázkový režim pro vizuální kontrolu (výchozí vypnutý)

Aby šlo okno porovnat se snímky návrhu bez karty a bez kopírování, `Offload.exe --demo pripraveno|probiha|hotovo|nastaveni`
naplní okno daty z mockupů (`DemoScenario`) místo skenů, prověření a historie. Úloha náhledu (`OffloadJob.ForPreview`)
nikdy neběží, nic nezapisuje; Pozastavit na ní hned drží (snímek pozastavené karty), Zrušit ji zruší. Test ověří, že ukázka
prochází stejným kódem karet jako ostrá úloha. `app-drive.ps1` dostane `Resize-Offload`. Bez `--demo` se nic nemění.

**Files:**
- Create: `windows/src/Offload.App/MainViewModel.Demo.cs`
- Create: `windows/src/Offload.Core/Desktop/DemoScenario.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/scripts/app-drive.ps1`
- Modify: `windows/src/Offload.App/MainViewModel.History.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs`
- Modify: `windows/src/Offload.App/MainViewModel.cs`
- Modify: `windows/src/Offload.Core/Desktop/StartupOptions.cs`
- Modify: `windows/src/Offload.Core/OffloadJob.cs`
- Test: `windows/tests/Offload.Core.Tests/DemoScenarioTests.cs` (nový)

**Interfaces:**
- Consumes: `JobCard`, `SetupTexts`, `HistoryView`, `CameraLabel`, `JobCardFacts` (úkoly 6–9), `JobItemViewModel(…, JobCardFacts? facts)` (úkol 14).
- Produces:
  - `Offload.Core.Desktop.DemoState { Ready, Running, Finished, Settings }`, `record DemoCard(string Path, string Label, CardInsight Insight, string Connection, string Format, long Total, long Free)`, `static class DemoScenario` — `Parse(string?)`, `Ready(string folder)`, `BackedUp(string folder, IReadOnlyList<string> destRoots)`, `Today(IReadOnlyList<string> destRoots, DateTime todayLocal)`, `Running(string source, IReadOnlyList<string> roots, Func<double> elapsed)`, `WaitingAppend(…)` → `(OffloadJob Job, JobCardFacts Facts)`, `Done(string source, IReadOnlyList<string> roots, Func<int, byte[]>? thumbnail)`, `Mismatch(…)`.
  - `StartupOptions(bool NoHwPrompt, IReadOnlyList<string> Sources, DemoState? Demo = null)`; argument `--demo <stav>`.
  - `OffloadJob.ForPreview(JobConfig config, OffloadPlan? plan, Func<JobStatus> status, OffloadReport? report = null)`.
  - `windows\scripts\app-drive.ps1`: `Resize-Offload $Process <šířka> <výška>`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/DemoScenarioTests.cs` (nový soubor)

```csharp
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// `Offload.exe --demo` (off by default): the states of docs/ui-navrh/screeny, checked through the same Core code the
/// window binds — so the visual check compares the design with what the app really computes.
public class DemoScenarioTests
{
    static readonly string[] Roots = [@"D:\demo\RAID_A", @"F:\demo\SHUTTLE_B"];
    static readonly string[] Disks = ["RAID_A", "SHUTTLE_B"];
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Parse_AndStartupOptions_OffByDefault()
    {
        Assert.Equal([DemoState.Ready, DemoState.Running, DemoState.Finished, DemoState.Settings],
            new[] { "pripraveno", "probiha", "HOTOVO", "nastaveni" }.Select(s => DemoScenario.Parse(s)!.Value));
        Assert.Null(DemoScenario.Parse("x"));
        Assert.Null(StartupOptions.Parse([]).Demo);
        Assert.Equal(DemoState.Running, StartupOptions.Parse(["--demo", "probiha"]).Demo);
        Assert.Null(StartupOptions.Parse(["--demo"]).Demo);   // no state: no demo
        Assert.Empty(StartupOptions.Parse(["--demo", "hotovo"]).Sources);
    }

    [Fact]
    public void Ready_TheCardsOfScreen1()
    {
        var ready = DemoScenario.Ready(@"C:\demo");
        Assert.Equal("24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s",
            SetupTexts.CardFacts(ready.Insight, ready.Total - ready.Free, ready.Format, ready.Connection, looksLikeCard: true));
        Assert.Equal("ALEXA 35 · LogC4", ready.Insight.Camera!.Text);
        var backedUp = DemoScenario.BackedUp(@"C:\demo", Roots);
        Assert.True(AppendRules.Offer(backedUp.Insight));
        Assert.Equal("3 karty · 412 GB · vše ověřeno", HistoryView.Today(DemoScenario.Today(Roots, DateTime.Now), DateTime.Now)!.Summary);
    }

    [Fact]
    public void Running_CopiesAt661_ThenWaitingAppend()
    {
        double t = 0;
        var job = DemoScenario.Running(@"C:\demo\A035_ARRI", Roots, () => t);
        var p = new JobProgress(T0);
        p.Update(job.Status(), T0.AddSeconds(0.5));   // the first tick of the copy: the baseline
        t = 1;
        p.Update(job.Status(), T0.AddSeconds(1.5));

        Assert.Equal(JobCardKind.Running, JobCard.Headline("A035_ARRI", p.Status, null, Disks).Kind);
        Assert.Equal(0.38, p.PhaseProgress, 2);
        var rows = JobCard.Rows(p, Disks);
        Assert.Equal(["Čtení karty", "RAID_A", "SHUTTLE_B"], rows.Select(r => r.Name));
        Assert.Equal("661 MB/s", rows[0].RateText);
        Assert.Equal("1 · Kopírování", JobCard.Steps(p.Status, job.Config, p.PhaseProgress)[0].Text);
        Assert.Equal("Soubor 10 / 24", JobCard.FileLine(p.Status).Caption);
        Assert.False(job.RunCopy());   // a preview never runs

        job.Pause();   // the screenshot of a paused card: Pozastavit holds at once
        p.Update(job.Status(), T0.AddSeconds(2.5));
        Assert.Equal("Pozastaveno", p.StatusText);
        Assert.Equal("—", JobCard.Rows(p, Disks)[0].RateText);
        job.Resume();
        Assert.False(job.Status().Paused);

        var (waiting, facts) = DemoScenario.WaitingAppend(@"C:\demo\B012_FX6", Roots);
        Assert.Equal(JobPhase.Waiting, waiting.Status().Phase);
        Assert.Equal("2 nové klipy · 14,2 GB · stejné cíle", JobCard.Sub(facts));
        job.Cancel();
        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);   // closing the demo window ends it
    }

    [Fact]
    public void Finished_GreenAndYellow()
    {
        var done = DemoScenario.Done(@"C:\demo\A035_ARRI", Roots, _ => [0xFF, 0xD8]);
        var h = JobCard.Headline("A035_ARRI", done.Status(), done.Report, Disks);
        Assert.Equal((JobCardKind.Done, "A035_ARRI — kartu lze vysunout", "Všech 24 souborů je na 2 discích a ověřených (xxHash64)."), (h.Kind, h.Title, h.Sub));
        Assert.Equal(["602 MB/s", "799 MB/s", "24 / 24", "6 min 12 s"], JobCard.Summary(done.Report!, null).Select(f => f.Value));
        Assert.Equal(5, JobCard.Thumbs(done.Report!).Thumbs.Count);

        var bad = DemoScenario.Mismatch(@"C:\demo\B012_FX6", Roots);
        h = JobCard.Headline("B012_FX6", bad.Status(), bad.Report, Disks);
        Assert.Equal((JobCardKind.Problem, "B012_FX6 — kartu zatím nevysunujte", "1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku."),
            (h.Kind, h.Title, h.Sub));
        var row = Assert.Single(JobCard.FailedRows(bad.Report!, Disks));
        Assert.Equal((@"XDROOT\Clip\B012C015_261007_9QW1.MXF", "RAID_A ✓", "SHUTTLE_B ✕ hash"), (row.Path, row.Disks[0].Text, row.Disks[1].Text));
        Assert.True(RepairRules.Offer(bad.Status().Phase, bad.Report, cardPresent: true, repairPending: false));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~DemoScenarioTests"`

Expected: FAIL — build selže — `CS0103: Název DemoScenario v aktuálním kontextu neexistuje` (a `StartupOptions.Demo`).

- [ ] **Step 3: Implementace**

#### `windows/scripts/app-drive.ps1`

Najít (1/3):

```powershell
#   . .\scripts\app-drive.ps1
#   $env:OFFLOAD_SETTINGS_DIR = 'D:\tmp\offload-settings'   # isolated settings for the run
#   $p = Start-Offload -Exe .\src\Offload.App\bin\Debug\net10.0-windows\Offload.exe
#   Invoke-OffloadButton $p 'HardwareButton'                 # USB check on demand (or Ctrl+U / Nástroje menu)
#   Invoke-OffloadButton $p 'StartButton'
#   Save-OffloadScreenshot $p 'D:\tmp\shot.png'
#   Stop-Offload $p
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
if (-not ('OffloadWin' -as [type])) {
Add-Type @'
```

Nahradit:

```powershell
#   . .\scripts\app-drive.ps1
#   $env:OFFLOAD_SETTINGS_DIR = 'D:\tmp\offload-settings'   # isolated settings for the run
#   $p = Start-Offload -Exe .\src\Offload.App\bin\Debug\net10.0-windows\Offload.exe
#   Invoke-OffloadButton $p 'HardwareButton'                 # USB check on demand (Hardware in the top bar, or Ctrl+U)
#   Invoke-OffloadButton $p 'StartButton'
#   Save-OffloadScreenshot $p 'D:\tmp\shot.png'
#   Stop-Offload $p
#   $p = Start-Offload -Exe <Offload.exe> -Arguments '--demo probiha'   # a state of docs/ui-navrh/screeny (off by default)
#   Resize-Offload $p 1000 900                                         # the narrow screenshot
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
if (-not ('OffloadWin' -as [type])) {
Add-Type @'
```

Najít (2/3):

```powershell
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
}
```

Nahradit:

```powershell
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
'@
}
```

Najít (3/3):

```powershell
    $bmp.Dispose()
}

function Stop-Offload($Process) {
    if ($Process.HasExited) { return }
    $Process.CloseMainWindow() | Out-Null
```

Nahradit:

```powershell
    $bmp.Dispose()
}

# Sets the window's outer size in pixels (docs/ui-navrh: 1360 px wide, the narrow screen 1000 px), keeping its position.
function Resize-Offload($Process, [int]$Width, [int]$Height, [string]$WindowTitle = 'Offload') {
    $h = [IntPtr]([int](Find-OffloadWindow $Process $WindowTitle).Current.NativeWindowHandle)
    [OffloadWin]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, $Width, $Height, 0x0016) | Out-Null   # SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE
    Start-Sleep -Milliseconds 700
}

function Stop-Offload($Process) {
    if ($Process.HasExited) { return }
    $Process.CloseMainWindow() | Out-Null
```

#### `windows/src/Offload.App/MainViewModel.Demo.cs` (nový soubor)

```csharp
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

/// `Offload.exe --demo <stav>` (DemoScenario, off by default): the window shows a state of docs/ui-navrh/screeny for the
/// visual check — the scans, the card inspections and history.json are replaced by the scenario's data; nothing is
/// copied, nothing is written. The destinations are the ones of settings.json (the check gives it two empty folders).
public sealed partial class MainViewModel
{
    DemoState? demo;
    readonly Stopwatch demoClock = new();
    List<VolumeItem> demoVolumes = [];

    /// True in the demo: the real scans and inspections stay off.
    bool IsDemo => demo != null;

    void InitDemo()
    {
        if (demo is not { } state) return;
        demoClock.Start();
        var folder = Path.Combine(AppData.Folder, "demo");
        var roots = Settings.DestRoots.ToList();
        if (state == DemoState.Ready)
        {
            var cards = new[] { DemoScenario.Ready(folder), DemoScenario.BackedUp(folder, roots) };
            demoVolumes = cards.Select(c => new VolumeItem(c.Label, c.Path, c.Total, c.Free, c.Format, LooksLikeCard: true, IsDestination: false,
                Identity: "demo-" + c.Label, Label: c.Label)).ToList();
            foreach (var c in cards)
            {
                var identity = "demo-" + c.Label;
                if (inspections.Start(c.Path, identity) is not null) inspections.Finished(c.Path, c.Insight, identity, stillListed: true);
                sourceLinks[c.Path] = (identity, c.Connection);
            }
            history = DemoScenario.Today(roots, DateTime.Now);
            ApplyVolumes(demoVolumes);
            selected.Clear();
            selected.Add(cards[0].Path);   // A035 ticked, B012 offers Doplnit
            RebuildSources();
        }
        else history = [];
        string Source(string card) => Path.Combine(folder, card);
        switch (state)
        {
            case DemoState.Running:
            {
                var (waiting, facts) = DemoScenario.WaitingAppend(Source("B012_FX6"), roots);
                Jobs.Insert(0, new JobItemViewModel(this, waiting, "B012_FX6", canEject: false, facts: facts));
                Jobs.Insert(0, new JobItemViewModel(this, DemoScenario.Running(Source("A035_ARRI"), roots, () => demoClock.Elapsed.TotalSeconds), "A035_ARRI",
                    canEject: false, facts: new JobCardFacts(24, 24, 170_000_000_000, "ALEXA 35", false)));
                break;
            }
            case DemoState.Finished:
                Jobs.Insert(0, new JobItemViewModel(this, DemoScenario.Mismatch(Source("B012_FX6"), roots), "B012_FX6", canEject: false));
                Jobs.Insert(0, new JobItemViewModel(this, DemoScenario.Done(Source("A035_ARRI"), roots, GreyThumbnail), "A035_ARRI", canEject: false));
                break;
            case DemoState.Settings:
                SettingsSection = SettingsSections.Proxy;
                IsSettingsOpen = true;
                break;
        }
        foreach (var j in Jobs) j.Refresh();
        RaiseMany(nameof(HasJobs), nameof(HasFinished), nameof(Today), nameof(HistoryDays));
    }

    static readonly Color[] ThumbColors =
        [Color.FromRgb(0x26, 0x30, 0x2B), Color.FromRgb(0x2B, 0x2A, 0x26), Color.FromRgb(0x23, 0x2A, 0x33), Color.FromRgb(0x2E, 0x28, 0x28), Color.FromRgb(0x26, 0x2B, 0x2E)];

    /// A grey 160 × 90 JPEG in the design's thumbnail colours.
    static byte[] GreyThumbnail(int i)
    {
        var c = ThumbColors[i % ThumbColors.Length];
        var pixels = new byte[160 * 90 * 3];
        for (int p = 0; p < pixels.Length; p += 3) (pixels[p], pixels[p + 1], pixels[p + 2]) = (c.B, c.G, c.R);
        var image = BitmapSource.Create(160, 90, 96, 96, PixelFormats.Bgr24, null, pixels, 160 * 3);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
```

#### `windows/src/Offload.App/MainViewModel.History.cs`

Najít (1/2):

```csharp
    /// Reads history.json off the UI thread, then looks at every card again; a request during a read runs once more.
    void ReloadHistory()
    {
        if (historyLoading)
        {
            historyReload = true;
```

Nahradit:

```csharp
    /// Reads history.json off the UI thread, then looks at every card again; a request during a read runs once more.
    void ReloadHistory()
    {
        if (IsDemo) return;   // the scenario's history
        if (historyLoading)
        {
            historyReload = true;
```

Najít (2/2):

```csharp

    void Inspect(string path)
    {
        if (history is not { } entries || closing) return;   // InspectAll runs once history is loaded
        if (inspections.Start(path, IdentityOf(path)) is not CancellationToken ct) return;
        var store = historyStore;
        var tools = resolvedTools;
```

Nahradit:

```csharp

    void Inspect(string path)
    {
        if (IsDemo || history is not { } entries || closing) return;   // InspectAll runs once history is loaded
        if (inspections.Start(path, IdentityOf(path)) is not CancellationToken ct) return;
        var store = historyStore;
        var tools = resolvedTools;
```

#### `windows/src/Offload.App/MainViewModel.Setup.cs`

Najít (1/2):

```csharp
    /// Rescans volumes on a background thread; a request during a scan runs once more afterwards.
    public void RefreshVolumes()
    {
        if (scanning)
        {
            rescan = true;
```

Nahradit:

```csharp
    /// Rescans volumes on a background thread; a request during a scan runs once more afterwards.
    public void RefreshVolumes()
    {
        if (IsDemo) return;   // the demo's cards stay as the scenario put them
        if (scanning)
        {
            rescan = true;
```

Najít (2/2):

```csharp
    /// the UI thread (DriveLinks asks the device; it never throws).
    void ResolveSourceLinks()
    {
        foreach (var s in Sources)
        {
            var path = s.Path;
```

Nahradit:

```csharp
    /// the UI thread (DriveLinks asks the device; it never throws).
    void ResolveSourceLinks()
    {
        if (IsDemo) return;
        foreach (var s in Sources)
        {
            var path = s.Path;
```

#### `windows/src/Offload.App/MainViewModel.cs`

Najít (1/2):

```csharp
    public MainViewModel(StartupOptions options, string? settingsPath = null)
    {
        Options = options;
        this.settingsPath = settingsPath ?? SettingsStore.DefaultPath;
        AllSettings = SettingsStore.Load(this.settingsPath);
        saveTimer.Tick += (_, _) =>
```

Nahradit:

```csharp
    public MainViewModel(StartupOptions options, string? settingsPath = null)
    {
        Options = options;
        demo = options.Demo;   // before the scans start: the demo replaces them (MainViewModel.Demo.cs)
        this.settingsPath = settingsPath ?? SettingsStore.DefaultPath;
        AllSettings = SettingsStore.Load(this.settingsPath);
        saveTimer.Tick += (_, _) =>
```

Najít (2/2):

```csharp
        InitNotify();
        InitUpdates();
        InitSettingsPage();
    }

    public StartupOptions Options { get; }
```

Nahradit:

```csharp
        InitNotify();
        InitUpdates();
        InitSettingsPage();
        InitDemo();
    }

    public StartupOptions Options { get; }
```

#### `windows/src/Offload.Core/Desktop/DemoScenario.cs` (nový soubor)

```csharp
using Offload.Core.History;

namespace Offload.Core.Desktop;

/// `Offload.exe --demo <stav>` (off by default): the window filled with the states of docs/ui-navrh/screeny, for the
/// visual check against the design — nothing is scanned, inspected, copied or written. pripraveno = obr. 1, probiha =
/// obr. 2, hotovo = obr. 3, nastaveni = obr. 4.
public enum DemoState { Ready, Running, Finished, Settings }

/// A card of the demo: where it "is" (a folder path that need not exist), its look and how it is connected.
public sealed record DemoCard(string Path, string Label, CardInsight Insight, string Connection, string Format, long Total, long Free);

/// The data of the demo states, built from the texts of the mockups (docs/ui-navrh/mockup/*.html). Pure: the window
/// puts it in place of its scans (MainViewModel.Demo.cs).
public static class DemoScenario
{
    public static DemoState? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "pripraveno" => DemoState.Ready,
        "probiha" => DemoState.Running,
        "hotovo" => DemoState.Finished,
        "nastaveni" => DemoState.Settings,
        _ => null,
    };

    static readonly DateTime Shot = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    static List<Clip> Clips(string prefix, int count, string folder = "") => Enumerable.Range(1, count).Select(i =>
    {
        var name = $"{prefix}C{i:000}_261007_R2KQ";
        var path = $"{folder}{name}.mxf";
        return new Clip { Name = name, Path = path, Format = ClipFormat.Video, Segments = [path], Bytes = 70_000_000 };
    }).ToList();

    /// The card that is ready to go (obr. 1): A035_ARRI, 24 clips, 1,70 GB, ALEXA 35 · LogC4.
    public static DemoCard Ready(string folder) => new(System.IO.Path.Combine(folder, "A035_ARRI"), "A035_ARRI",
        new CardInsight
        {
            Source = System.IO.Path.Combine(folder, "A035_ARRI"), TotalBytes = 1_700_000_000, FileCount = 24, Filesystem = "exFAT",
            Clips = Clips("A035", 24), Camera = new CameraLabel("ALEXA 35", "LogC4"),
        }, "čtečka USB 3 · 5 Gb/s", "exFAT", 128_000_000_000, 126_300_000_000);

    /// The card backed up yesterday with two new clips (obr. 1): "Už zálohováno 6. 10. 21:40 · 2 nové klipy" + Doplnit.
    public static DemoCard BackedUp(string folder, IReadOnlyList<string> destRoots)
    {
        var entry = new HistoryEntry
        {
            Id = "20261006T194000Z-b012b012", CardName = "B012_FX6", Date = "2026-10-06", Finished = new DateTime(2026, 10, 6, 19, 40, 0, DateTimeKind.Utc),
            Success = true, Verified = true, Files = 22, Bytes = 118_000_000_000, Project = null,
            Destinations = destRoots.Select(r => new HistoryDestination { Root = r, CardPath = System.IO.Path.Combine(r, "2026-10-06", "B012_FX6") }).ToList(),
        };
        var fresh = new List<SourceFile>
        {
            new("XDROOT/Clip/B012C023_261007_9QW1.MXF", 7_100_000_000, Shot),
            new("XDROOT/Clip/B012C024_261007_9QW1.MXF", 7_100_000_000, Shot),
        };
        var source = System.IO.Path.Combine(folder, "B012_FX6");
        return new DemoCard(source, "B012_FX6", new CardInsight
        {
            Source = source, TotalBytes = 132_200_000_000, FileCount = 24, Filesystem = "exFAT", Clips = Clips("B012", 24, "XDROOT/Clip/"),
            Volume = new CardVolume(null, "1A2B3C4D", 256_000_000_000, "B012_FX6", "exFAT"), AppendReachable = true,
            History = new HistoryMatch(entry, HistoryMatchKind.NewFiles, fresh, 14_200_000_000, [], 0),
            Camera = new CameraLabel("ILME-FX6V", "S-Log3"),
        }, "čtečka USB 3 · 5 Gb/s", "exFAT", 256_000_000_000, 123_800_000_000);
    }

    /// DNES (obr. 1): three cards of today, each on two disks and verified.
    public static IReadOnlyList<HistoryEntry> Today(IReadOnlyList<string> destRoots, DateTime todayLocal) =>
    [
        Entry("A034_ARRI", 256_000_000_000, todayLocal.Date.AddHours(18).AddMinutes(12), destRoots),
        Entry("B011_FX6", 118_000_000_000, todayLocal.Date.AddHours(16).AddMinutes(47), destRoots),
        Entry("SND_DAY03", 38_000_000_000, todayLocal.Date.AddHours(14).AddMinutes(5), destRoots),
    ];

    static HistoryEntry Entry(string card, long bytes, DateTime finishedLocal, IReadOnlyList<string> roots) => new()
    {
        Id = $"{finishedLocal.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}-{card.Length:x8}", CardName = card, Bytes = bytes, Files = 20, Success = true, Verified = true,
        Finished = finishedLocal.ToUniversalTime(), Date = finishedLocal.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        Destinations = roots.Select(r => new HistoryDestination { Root = r, CardPath = System.IO.Path.Combine(r, card) }).ToList(),
    };

    static OffloadPlan Plan(string source, string card, IReadOnlyList<string> roots, IReadOnlyList<SourceFile> files, PlanMode mode = PlanMode.Full) => new()
    {
        Source = source, CardName = card, Date = "2026-10-07", DestRoots = roots, Mode = mode,
        Scan = new SourceScan { Root = source, Files = files, Dirs = [], Skipped = [] },
    };

    static List<SourceFile> Files(IReadOnlyList<Clip> clips, long each) => clips.Select(c => new SourceFile(c.Path, each, Shot)).ToList();

    /// Obr. 2: A035_ARRI copying at ~661 MB/s from 38 % (the card is 170 GB here, so the picture holds for minutes), the
    /// second disk a little behind. `elapsed` = seconds since the window opened (the status moves like a real copy).
    public static OffloadJob Running(string source, IReadOnlyList<string> roots, Func<double> elapsed)
    {
        var clips = Clips("A035", 24);
        var files = Files(clips, 170_000_000_000 / 24);
        var plan = Plan(source, "A035_ARRI", roots, files);
        long total = files.Sum(f => f.Size);
        var config = new JobConfig(source, roots) { Proxy = new ProxySettings { Enabled = true } };
        return OffloadJob.ForPreview(config, plan, () =>
        {
            double t = elapsed();
            long read = Math.Min(total, (long)(total * 0.38 + 661e6 * t));
            long behind = Math.Min(read, (long)(6e6 * t) + 16_000_000);
            int file = (int)Math.Min(23, read / (total / 24));
            return new JobStatus
            {
                Phase = JobPhase.Copying,
                Copy = new ProgressSnapshot(CopyPhase.Copy, total, 24, Math.Max(file, 9), clips[Math.Max(file, 9)].Path, read, [read, read - behind], [0, 0], 0),
            };
        });
    }

    /// Obr. 2: the append of B012_FX6 waiting behind it.
    public static (OffloadJob Job, JobCardFacts Facts) WaitingAppend(string source, IReadOnlyList<string> roots) =>
        (OffloadJob.ForPreview(new JobConfig(source, roots) { AppendInto = roots.Select(r => System.IO.Path.Combine(r, "2026-10-06", "B012_FX6")).ToList() },
            null, () => new JobStatus()), new JobCardFacts(2, 2, 14_200_000_000, null, Append: true));

    static OffloadReport Report(string source, string card, IReadOnlyList<string> roots, IReadOnlyList<ReportFile> files, long bytes, double copyMBps, double verifyMBps) => new()
    {
        CardName = card, Date = "2026-10-07", Source = source, SourceFilesystem = "exFAT", Verified = true,
        Started = Shot, CopyFinished = Shot.AddSeconds(bytes / (copyMBps * 1e6)),
        Finished = Shot.AddSeconds(bytes / (copyMBps * 1e6) + bytes / (verifyMBps * 1e6)), TotalFiles = files.Count, TotalBytes = bytes,
        Destinations = roots.Select(r => new ReportDestination { Root = r, CardPath = System.IO.Path.Combine(r, "2026-10-07", card), Filesystem = "NTFS" }).ToList(),
        Files = files, Skipped = [],
    };

    /// Obr. 3, green: A035_ARRI done — 602 MB/s, 799 MB/s, proxy 24 / 24, 6 min 12 s (the card is 128 GB here: copy and
    /// read-back at those speeds take 6 min 12 s). `thumbnail`: a JPEG for the first five clips (the window makes grey
    /// ones), null = none.
    public static OffloadJob Done(string source, IReadOnlyList<string> roots, Func<int, byte[]>? thumbnail)
    {
        var clips = Clips("A035", 24);
        var files = Files(clips, 127_700_000_000 / 24);
        var report = Report(source, "A035_ARRI", roots,
            files.Select(f => new ReportFile { Path = f.RelPath, Size = f.Size, Xxh64 = "0123456789abcdef", Destinations = ["verified", "verified"], Errors = [null, null] }).ToList(),
            files.Sum(f => f.Size), 602, 799);
        report.Proxy = new ProxySettings { Enabled = true };
        report.Clips = clips.Select((c, i) => new ClipEntry
        {
            Clip = new Clip { Name = $"C{i + 1:000}", Path = c.Path, Segments = c.Segments, Format = ClipFormat.Video },
            ProxyStatus = ProxyStatus.Done,
            Thumbnails = thumbnail != null && i < 5 ? [new Thumbnail(thumbnail(i), "a"), new Thumbnail(thumbnail(i), "b"), new Thumbnail(thumbnail(i), "c")] : [],
        }).ToList();
        var config = new JobConfig(source, roots) { Proxy = new ProxySettings { Enabled = true } };
        return OffloadJob.ForPreview(config, Plan(source, "A035_ARRI", roots, files), () => new JobStatus
        {
            Phase = JobPhase.Done, SafeToEject = true, CopySucceeded = true, ProxiesDone = 24,
        }, report);
    }

    /// Obr. 3, yellow: B012_FX6 with one file that does not match on the second disk.
    public static OffloadJob Mismatch(string source, IReadOnlyList<string> roots)
    {
        var clips = Clips("B012", 24, "XDROOT/Clip/");
        var files = Files(clips, 118_000_000_000 / 24);
        const string bad = "XDROOT/Clip/B012C015_261007_9QW1.MXF";
        var report = Report(source, "B012_FX6", roots,
            files.Select(f => new ReportFile { Path = f.RelPath, Size = f.Size, Xxh64 = "0123456789abcdef", Destinations = ["verified", "verified"], Errors = [null, null] })
                .Append(new ReportFile
                {
                    Path = bad, Size = 4_900_000_000, Xxh64 = "fedcba9876543210", Destinations = ["verified", "failed"],
                    Errors = [null, "HASH MISMATCH: dest 1111111111111111 != source fedcba9876543210"],
                }).ToList(),
            files.Sum(f => f.Size), 600, 800);
        return OffloadJob.ForPreview(new JobConfig(source, roots), Plan(source, "B012_FX6", roots, files), () => new JobStatus
        {
            Phase = JobPhase.Done, CopySucceeded = false,
        }, report);
    }
}
```

#### `windows/src/Offload.Core/Desktop/StartupOptions.cs`

Najít (1/2):

```csharp

/// Offload.exe arguments: `--no-hw-prompt` is still accepted (older scripts) but has no effect — the USB check
/// is on demand only since 2026-10-05 (spec 8a.2), there is no start-up question; every argument that is
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
```

Nahradit:

```csharp

/// Offload.exe arguments: `--no-hw-prompt` is still accepted (older scripts) but has no effect — the USB check
/// is on demand only since 2026-10-05 (spec 8a.2), there is no start-up question; every argument that is
/// an existing folder becomes a ticked manual source (drag a folder onto Offload.exe). `--demo <stav>` (pripraveno,
/// probiha, hotovo, nastaveni) fills the window with a state of docs/ui-navrh for the visual check — off by default.
public sealed record StartupOptions(bool NoHwPrompt, IReadOnlyList<string> Sources, DemoState? Demo = null)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        bool noPrompt = false;
        DemoState? demo = null;
        var sources = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.Equals("--no-hw-prompt", StringComparison.OrdinalIgnoreCase)) noPrompt = true;
            else if (a.Equals("--demo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count) demo = DemoScenario.Parse(args[++i]);
            else if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                try
```

Najít (2/2):

```csharp
                }
            }
        }
        return new StartupOptions(noPrompt, sources);
    }
}
```

Nahradit:

```csharp
                }
            }
        }
        return new StartupOptions(noPrompt, sources, demo);
    }
}
```

#### `windows/src/Offload.Core/OffloadJob.cs`

Najít (1/4):

```csharp
        Engine = new OffloadEngine(options);
    }

    /// The project of the earlier backup: the first destination whose report can be read decides; `fallback` only when
    /// none can (M4).
    public static string? EarlierProject(IReadOnlyList<string> cardPaths, string? fallback)
```

Nahradit:

```csharp
        Engine = new OffloadEngine(options);
    }

    // A preview job (DemoScenario): its status comes from here; it never runs.
    Func<JobStatus>? preview;

    /// `Offload.exe --demo` only (DemoScenario): a job that never runs — Status() returns `status()`, Plan and Report are
    /// the given ones. RunCopy and RunPost do nothing.
    public static OffloadJob ForPreview(JobConfig config, OffloadPlan? plan, Func<JobStatus> status, OffloadReport? report = null) =>
        new(config) { preview = status, Plan = plan, Report = report };

    /// The project of the earlier backup: the first destination whose report can be read decides; `fallback` only when
    /// none can (M4).
    public static string? EarlierProject(IReadOnlyList<string> cardPaths, string? fallback)
```

Najít (2/4):

```csharp

    public JobStatus Status()
    {
        JobStatus s;
        bool wasStarted;
        lock (gate) { s = state; wasStarted = started; }
```

Nahradit:

```csharp

    public JobStatus Status()
    {
        if (preview != null)
        {
            var p = preview();
            if (IsCancelled && !p.Phase.IsFinished()) return p with { Phase = JobPhase.Cancelled };   // closing the demo cancels it
            // Pozastavit in the demo: nothing runs, so the pause holds at once.
            bool live = p.Phase != JobPhase.Waiting && !p.Phase.IsFinished();
            return live && Engine.Pauses.IsPaused ? p with { Paused = true, PauseHeld = true, PausedTotal = Engine.Pauses.PausedTotal } : p;
        }
        JobStatus s;
        bool wasStarted;
        lock (gate) { s = state; wasStarted = started; }
```

Najít (3/4):

```csharp
    /// After this the card is no longer needed, so the app can start the next card while stage 2 runs.
    public bool RunCopy(Action<string>? log = null)
    {
        IdentifyCard(log);
        lock (gate)
        {
```

Nahradit:

```csharp
    /// After this the card is no longer needed, so the app can start the next card while stage 2 runs.
    public bool RunCopy(Action<string>? log = null)
    {
        if (preview != null) return false;
        IdentifyCard(log);
        lock (gate)
        {
```

Najít (4/4):

```csharp
    /// A failure here never changes the copy's result (SafeToEject stays as stage 1 left it).
    public void RunPost(Action<string>? log = null)
    {
        if (dayReportOnly)
        {
            try
```

Nahradit:

```csharp
    /// A failure here never changes the copy's result (SafeToEject stays as stage 1 left it).
    public void RunPost(Action<string>? log = null)
    {
        if (preview != null) return;
        if (dayReportOnly)
        {
            try
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~DemoScenarioTests"`

Expected: PASS (`Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 upozornění, 0 chyb`; celá sada `Neúspěšné: 0` (časově citlivé `ProxyWatchdogTests` / `ProxyRendererTests.Feeder_*` při jednom selhání pusťte znovu samostatně — viz Global Constraints).

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)`.

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1):

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 16 — dnešní historie (postup 5, nová věc 7): pod frontou „DNES“ se souhrnem „3 karty · 412 GB · vše ověřeno“, odkazem „Denní report…“ a řádky „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“ (barva podle stavu, bublina = projekt); záložka „Historie karet“ se všemi dny („Dnes“, „Včera“, „4. 10. 2026“, nejvýš 300 karet) a „Denní report…“; obojí z `history.json`, které okno už načítá mimo UI vlákno (po každé dokončené kopii znovu)
- Úkol 15 — fronta, nové věci 1–4, 9 a 10 (postup 4, 2. část): běžící karta má ukazatel fází (4 segmenty, hotové plné, aktivní podle průběhu), tabulku rychlostí na pozadí `Input` („Čtení karty“ + každý cíl s číslem, pruh, MB/s v monospace; bublina = dosavadní řádek řetězu: výrobek, připojení), „Soubor 10 / 24“ + soubor, **Pozastavit / Pokračovat** a Zrušit, text pauzy, „zbývá ~0:02 kopie · ~6:00 celkem“ pod procenty a pod kartou „Nejpomalejší článek: …“; zelená karta má řádek čtyř čísel Kopie · Ověření · Proxy · Celkem, prvních 5 náhledů (prostřední snímek, dekódovaný mimo UI vlákno) + dlaždici „+N“ a tlačítko Proxy; žlutá karta má seznam nesouhlasných souborů (cesta · „RAID_A ✓“ · „SHUTTLE_B ✕ hash“, prvních 5 + „… a další N souborů v reportu“) a **Zkopírovat znovu** — úloha „A001 · oprava“ ve frontě se stejným nastavením jako karta, nabízí se jen s kartou ve čtečce a bez jiné opravy téže zálohy; řádky tabulky a fáze se aktualizují na místě (žádné překreslování seznamu 3× za sekundu)
- Úkol 14 — fronta se stávajícími daty (postup 4, 1. část): prázdná fronta = karta s ikonami Karta → 2 disky naráz → Ověření → Proxy → Report (jen co projekt dělá) a „Připraveno — A035_ARRI na 2 disky“ + věta (bez karty „Zatím žádný offload“); karta úlohy mění vzhled podle `JobCard.Headline` (čeká: kompaktní řádek „B012_FX6 — doplnění · 2 nové klipy · 14,2 GB · stejné cíle · Čeká ve frontě“ se Zrušit; běží: ikona, název, „24 klipů · 1,70 GB · ALEXA 35“, velké „38 %“, průběh a dosavadní texty, Zrušit červeným obrysem; hotovo zeleně: štít, „… — kartu lze vysunout“, Vysunout kartu, souhrn, Report PDF, Otevřít složku, cesta; nesouhlasí žlutě: „… — kartu zatím nevysunujte“ + věta; chyba červeně; zrušeno šedě); ověření zálohy jako karta ve stejném stylu; „Fronta 2“ s počtem nedokončených, „Vyčistit hotové“ jako odkaz; karta si pamatuje klipy, velikost a kameru z prověření při startu
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 17 — ukázkový režim pro vizuální kontrolu (výchozí vypnutý): `Offload.exe --demo pripraveno|probiha|hotovo|nastaveni` naplní okno stavy obrazovek `docs/ui-navrh/screeny` z `DemoScenario` (A035_ARRI s ALEXA 35 · LogC4 a čtečkou USB 3, zálohovaná B012_FX6 s Doplnit, DNES se třemi kartami; běžící kopie ~661 MB/s od 38 % a čekající doplnění; zelená hotová karta 602 / 799 MB/s, 24 / 24, 6 min 12 s s šedými náhledy a žlutá s nesouhlasným souborem na SHUTTLE_B; Nastavení na sekci Proxy) — místo skenů, prověření karet a `history.json`, nic nekopíruje ani nezapisuje (`OffloadJob.ForPreview`: úloha, která nikdy neběží; zavření okna ji zruší); test, že ukázka prochází stejným kódem karet jako ostrá úloha; `app-drive.ps1`: `Resize-Offload` (úzké okno 1000 px)
- Úkol 16 — dnešní historie (postup 5, nová věc 7): pod frontou „DNES“ se souhrnem „3 karty · 412 GB · vše ověřeno“, odkazem „Denní report…“ a řádky „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“ (barva podle stavu, bublina = projekt); záložka „Historie karet“ se všemi dny („Dnes“, „Včera“, „4. 10. 2026“, nejvýš 300 karet) a „Denní report…“; obojí z `history.json`, které okno už načítá mimo UI vlákno (po každé dokončené kopii znovu)
- Úkol 15 — fronta, nové věci 1–4, 9 a 10 (postup 4, 2. část): běžící karta má ukazatel fází (4 segmenty, hotové plné, aktivní podle průběhu), tabulku rychlostí na pozadí `Input` („Čtení karty“ + každý cíl s číslem, pruh, MB/s v monospace; bublina = dosavadní řádek řetězu: výrobek, připojení), „Soubor 10 / 24“ + soubor, **Pozastavit / Pokračovat** a Zrušit, text pauzy, „zbývá ~0:02 kopie · ~6:00 celkem“ pod procenty a pod kartou „Nejpomalejší článek: …“; zelená karta má řádek čtyř čísel Kopie · Ověření · Proxy · Celkem, prvních 5 náhledů (prostřední snímek, dekódovaný mimo UI vlákno) + dlaždici „+N“ a tlačítko Proxy; žlutá karta má seznam nesouhlasných souborů (cesta · „RAID_A ✓“ · „SHUTTLE_B ✕ hash“, prvních 5 + „… a další N souborů v reportu“) a **Zkopírovat znovu** — úloha „A001 · oprava“ ve frontě se stejným nastavením jako karta, nabízí se jen s kartou ve čtečce a bez jiné opravy téže zálohy; řádky tabulky a fáze se aktualizují na místě (žádné překreslování seznamu 3× za sekundu)
- Úkol 14 — fronta se stávajícími daty (postup 4, 1. část): prázdná fronta = karta s ikonami Karta → 2 disky naráz → Ověření → Proxy → Report (jen co projekt dělá) a „Připraveno — A035_ARRI na 2 disky“ + věta (bez karty „Zatím žádný offload“); karta úlohy mění vzhled podle `JobCard.Headline` (čeká: kompaktní řádek „B012_FX6 — doplnění · 2 nové klipy · 14,2 GB · stejné cíle · Čeká ve frontě“ se Zrušit; běží: ikona, název, „24 klipů · 1,70 GB · ALEXA 35“, velké „38 %“, průběh a dosavadní texty, Zrušit červeným obrysem; hotovo zeleně: štít, „… — kartu lze vysunout“, Vysunout kartu, souhrn, Report PDF, Otevřít složku, cesta; nesouhlasí žlutě: „… — kartu zatím nevysunujte“ + věta; chyba červeně; zrušeno šedě); ověření zálohy jako karta ve stejném stylu; „Fronta 2“ s počtem nedokončených, „Vyčistit hotové“ jako odkaz; karta si pamatuje klipy, velikost a kameru z prověření při startu
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: demo mode for the visual check, off by default (plan 9, task 17)

Offload.exe --demo pripraveno|probiha|hotovo|nastaveni fills the window with the states of
docs/ui-navrh/screeny through the same Core code as a real job; nothing is copied or written.
app-drive.ps1 can resize the window for the narrow screenshot.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\scripts\app-drive.ps1 windows\src\Offload.App\MainViewModel.Demo.cs windows\src\Offload.App\MainViewModel.History.cs windows\src\Offload.App\MainViewModel.Setup.cs windows\src\Offload.App\MainViewModel.cs windows\src\Offload.Core\Desktop\DemoScenario.cs windows\src\Offload.Core\Desktop\StartupOptions.cs windows\src\Offload.Core\OffloadJob.cs windows\tests\Offload.Core.Tests\DemoScenarioTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 18: Verze 0.5.0 a dokumentace (postup 8)

Verze **0.5.0** (`0.5.0-win`) a dokumentace současného chování: návod (`navod.html` → `Offload-navod.pdf`, schéma
okna podle nového vzhledu), `windows/README.md` (stav, GUI, ruční test 40–47), `docs/FORMATY.md` (nové klíče
a oddíl Oprava), `docs/ROZDILY.md`, `docs/VYVOJ-MAC.md` (co Mac převezme po schválení) a řádek plánu 9 v
`docs/VYVOJ-WINDOWS.md`. `ReleaseScriptTests` hlídá, že návod dál zmiňuje `Offload-win-x64-&lt;verze&gt;.zip`.

**Files:**
- Modify (vygeneruje skript): `windows/package/Offload-navod.pdf`
- Modify: `docs/FORMATY.md`
- Modify: `docs/ROZDILY.md`
- Modify: `docs/VYVOJ-MAC.md`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/Directory.Build.props`
- Modify: `windows/README.md`
- Modify: `windows/package/navod/navod.html`
- Test: `windows/tests/Offload.Core.Tests/FormatTests.cs`
- Test: `windows/tests/Offload.Core.Tests/HtmlReportTests.cs`
- Test: `windows/tests/Offload.Core.Tests/ReportTests.cs`

**Interfaces:**
- Consumes: vše z úkolů 1–17 (texty v návodu odpovídají `Msg.*`, `JobCard.*`, `SetupTexts.*` a popiskům XAML).
- Produces: `OffloadVersion.String` = `0.5.0-win` (z `Directory.Build.props`), `Offload-navod.pdf` 0.5.0.

- [ ] **Step 1: Upravit testy s verzí**

#### `windows/tests/Offload.Core.Tests/FormatTests.cs`

Najít (1/1):

```csharp
    }

    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.4.0-win", OffloadVersion.String);

    // R22: the version comes from Directory.Build.props (InformationalVersion), shaped x.y.z-win - never typed twice.
    [Fact]
```

Nahradit:

```csharp
    }

    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.5.0-win", OffloadVersion.String);

    // R22: the version comes from Directory.Build.props (InformationalVersion), shaped x.y.z-win - never typed twice.
    [Fact]
```

#### `windows/tests/Offload.Core.Tests/HtmlReportTests.cs`

Najít (1/1):

```csharp
        Assert.Contains("<td class=\"ok\">ověřeno</td>", html);
        Assert.Contains("<details><summary>Všechny soubory (2)</summary>", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.Contains("offload 0.4.0-win", html);
    }

    [Fact]
```

Nahradit:

```csharp
        Assert.Contains("<td class=\"ok\">ověřeno</td>", html);
        Assert.Contains("<details><summary>Všechny soubory (2)</summary>", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.Contains("offload 0.5.0-win", html);
    }

    [Fact]
```

#### `windows/tests/Offload.Core.Tests/ReportTests.cs`

Najít (1/1):

```csharp
        var root = doc.RootElement;
        Assert.Equal("2026-10-04T08:00:00Z", root.GetProperty("started").GetString());
        Assert.Equal("offload", root.GetProperty("tool").GetString());
        Assert.Equal("0.4.0-win", root.GetProperty("version").GetString());
        Assert.Equal(0, root.GetProperty("clips").GetArrayLength());
        Assert.False(root.TryGetProperty("proxy", out _));
```

Nahradit:

```csharp
        var root = doc.RootElement;
        Assert.Equal("2026-10-04T08:00:00Z", root.GetProperty("started").GetString());
        Assert.Equal("offload", root.GetProperty("tool").GetString());
        Assert.Equal("0.5.0-win", root.GetProperty("version").GetString());
        Assert.Equal(0, root.GetProperty("clips").GetArrayLength());
        Assert.False(root.TryGetProperty("proxy", out _));
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~FormatTests|FullyQualifiedName~ReportTests|FullyQualifiedName~HtmlReportTests|FullyQualifiedName~ReleaseScriptTests"`

Expected: FAIL — 3 testy selžou — očekávají `0.5.0-win`, sestavení hlásí `0.4.0-win`.

- [ ] **Step 3: Verze 0.5.0**

#### `windows/Directory.Build.props`

Najít (1/1):

```xml
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Version>0.4.0</Version>
    <InformationalVersion>0.4.0-win</InformationalVersion>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
  </PropertyGroup>
```

Nahradit:

```xml
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Version>0.5.0</Version>
    <InformationalVersion>0.5.0-win</InformationalVersion>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
  </PropertyGroup>
```

- [ ] **Step 4: Spustit testy — projdou**

Run: `dotnet build windows\Offload.sln -warnaserror` a `dotnet test windows\Offload.sln --filter "FullyQualifiedName~FormatTests|FullyQualifiedName~ReportTests|FullyQualifiedName~HtmlReportTests|FullyQualifiedName~ReleaseScriptTests"`

Expected: PASS (`Neúspěšné: 0`).

- [ ] **Step 5: Dokumentace — návod, README, FORMATY, ROZDILY, VYVOJ-MAC, VYVOJ-WINDOWS**

Bloky níže jsou podle stavu souborů po úkolu 17. V návodu se kapitola 3 mění celá (nové schéma okna a legenda), ostatní
kapitoly jen v místech, která zmiňují menu Nástroje, levý panel nebo starou kartu úlohy.

#### `docs/FORMATY.md`

Najít (1/5):

```markdown
        offload_report_0002.txt
        <karta>_report_0002.html
        <karta>_report_0002.pdf
        <karta>_verify_<YYYY-MM-DD_HHMMSS>Z.html   jen po ověření zálohy s reportem (viz „Report ověření“)
        <karta>_verify_<YYYY-MM-DD_HHMMSS>Z.pdf
        offload_verify_<YYYY-MM-DD_HHMMSS>Z.json
    ascmhl/
        0001_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl
        0002_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl     jen po doplnění
        ascmhl_chain.xml
    _PROXY/                           jen když se renderují proxy
        <klip>.mov
```

Nahradit:

```markdown
        offload_report_0002.txt
        <karta>_report_0002.html
        <karta>_report_0002.pdf
        neshoda_0002/<cesta na kartě>  jen po opravě (Windows od 0.5.0): nesouhlasná kopie odložená před novou
        <karta>_verify_<YYYY-MM-DD_HHMMSS>Z.html   jen po ověření zálohy s reportem (viz „Report ověření“)
        <karta>_verify_<YYYY-MM-DD_HHMMSS>Z.pdf
        offload_verify_<YYYY-MM-DD_HHMMSS>Z.json
    ascmhl/
        0001_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl
        0002_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl     jen po doplnění nebo opravě
        ascmhl_chain.xml
    _PROXY/                           jen když se renderují proxy
        <klip>.mov
```

Najít (2/5):

```markdown
| Klíč | Typ | Význam |
|---|---|---|
| `tool` | string | `offload` |
| `version` | string | `0.1.0` (Mac), `0.4.0-win` (Windows) |
| `generation` | číslo? | jen Windows od 0.2.0 a jen v reportu doplnění (`offload_report_NNNN.json`): číslo generace ASC MHL; chybí = 1 (celá kopie) |
| `project` | string? | jen Windows od 0.4.0: projekt, ve kterém se karta kopírovala; chybí bez projektu. Doplnění zapíše projekt dřívější zálohy, ne projekt zobrazený v aplikaci. Denní report ho ukáže, když ho mají všechny karty dne stejný |
| `hashAlgorithm` | string | `xxh64` (vždy, i s MD5 — Mac ho jen zobrazí) |
| `hashAlgorithms` | string[]? | jen Windows od 0.3.0 a jen když nejsou jen xxh64: `["xxh64", "md5"]` |
```

Nahradit:

```markdown
| Klíč | Typ | Význam |
|---|---|---|
| `tool` | string | `offload` |
| `version` | string | `0.1.0` (Mac), `0.5.0-win` (Windows) |
| `generation` | číslo? | jen Windows od 0.2.0 a jen v reportu doplnění nebo opravy (`offload_report_NNNN.json`): číslo generace ASC MHL; chybí = 1 (celá kopie) |
| `kind` | string? | jen Windows od 0.5.0: `repair` u opravy („Zkopírovat znovu“); chybí u celé kopie a doplnění. Mac ignoruje |
| `copyPausedSeconds`, `verifyPausedSeconds` | číslo? | jen Windows od 0.5.0: sekundy, které kopie / zpětné čtení stály pozastavené (od Pozastavit do Pokračovat); chybí, když se nepozastavovalo. Rychlosti a doby v TXT, HTML a PDF je odečítají; Mac ignoruje |
| `setAside[]` | objekt? | jen Windows od 0.5.0 a jen u opravy: `destination` (index cíle), `path` (cesta na kartě), `movedTo` (`_REPORT/neshoda_NNNN/<path>`) — nesouhlasné kopie přesunuté před novou; chybí, když žádná nebyla |
| `project` | string? | jen Windows od 0.4.0: projekt, ve kterém se karta kopírovala; chybí bez projektu. Doplnění zapíše projekt dřívější zálohy, ne projekt zobrazený v aplikaci. Denní report ho ukáže, když ho mají všechny karty dne stejný |
| `hashAlgorithm` | string | `xxh64` (vždy, i s MD5 — Mac ho jen zobrazí) |
| `hashAlgorithms` | string[]? | jen Windows od 0.3.0 a jen když nejsou jen xxh64: `["xxh64", "md5"]` |
```

Najít (3/5):

```markdown
| `verified` | bool | proběhlo ověření zpětným čtením |
| `totalFiles`, `totalBytes` | číslo | |
| `destinations[]` | objekt | `root`, `cardPath`, `filesystem`, `filesOK`, `filesFailed`, volitelně `unbuffered` (jen `false`, jen Windows) |
| `files[]` | objekt | `path`, `size`, `modified`, `xxh64`?, `md5`? (32 malých hex, jen s MD5, Windows od 0.3.0), `sourceError`?, `destinations[]` (stav pro každý cíl), `errors[]` (chyba nebo `null` pro každý cíl; anglicky — HTML je zobrazí česky) |
| `skipped[]` | string | vynechané systémové položky |
| `clips[]` | objekt | `clip` {`name`, `path`, `format`, `segments`, `bytes`, `isCameraProxy`}, `info`?, `probeError`?, `proxyStatus`, `proxyPath`?, `proxyNote`?, `proxySeconds`?, `lut`? {`source`, `id`, `name`, `reason`} (použitý LUT a proč; Windows od plánu 5, Mac zatím ignoruje) |
| `proxy` | objekt? | nastavení proxy (`enabled`, `height`, `codec`, `bitrateMbps`, `lutPath`, `lutMode` (`auto`/`builtin`/`custom`/`none`), `lutBuiltin`, `lutFallback`, `burnFilename`, `burnTimecode`, `audio`, `skipCameraProxies`, `copyToAllDestinations`, `frameAspect`, `frameScale`; jen Windows, volitelně `extraFolder`, od 0.4.0 `lutRules` a `brawGen4ToGen5` — viz níže) |
```

Nahradit:

```markdown
| `verified` | bool | proběhlo ověření zpětným čtením |
| `totalFiles`, `totalBytes` | číslo | |
| `destinations[]` | objekt | `root`, `cardPath`, `filesystem`, `filesOK`, `filesFailed`, volitelně `unbuffered` (jen `false`, jen Windows) |
| `files[]` | objekt | `path`, `size`, `modified`, `xxh64`?, `md5`? (32 malých hex, jen s MD5, Windows od 0.3.0), `sourceError`?, `destinations[]` (stav pro každý cíl), `errors[]` (chyba nebo `null` pro každý cíl; anglicky — HTML je zobrazí česky), `rewritten[]`? (jen oprava, Windows od 0.5.0: pro každý cíl `true` = nová kopie, `false` = dobrá kopie jen znovu přečtená) |
| `skipped[]` | string | vynechané systémové položky |
| `clips[]` | objekt | `clip` {`name`, `path`, `format`, `segments`, `bytes`, `isCameraProxy`}, `info`?, `probeError`?, `proxyStatus`, `proxyPath`?, `proxyNote`?, `proxySeconds`?, `lut`? {`source`, `id`, `name`, `reason`} (použitý LUT a proč; Windows od plánu 5, Mac zatím ignoruje) |
| `proxy` | objekt? | nastavení proxy (`enabled`, `height`, `codec`, `bitrateMbps`, `lutPath`, `lutMode` (`auto`/`builtin`/`custom`/`none`), `lutBuiltin`, `lutFallback`, `burnFilename`, `burnTimecode`, `audio`, `skipCameraProxies`, `copyToAllDestinations`, `frameAspect`, `frameScale`; jen Windows, volitelně `extraFolder`, od 0.4.0 `lutRules` a `brawGen4ToGen5` — viz níže) |
```

Najít (4/5):

```markdown
- dvě doplnění do téže složky karty najednou nejdou: pojmenovaný mutex `Local\Offload-append-<sha256(svazek|cesta)>`
  na každou složku karty, bez čekání (druhé skončí „Do této zálohy právě doplňuje jiná úloha …“); celá kopie zámek nebere.

## Report ověření (Windows od 0.2.0)

„Ověřit zálohu“ (GUI) a `offload verify --report` zapisují jen do složky `_REPORT`, nikdy do `ascmhl/` — `_REPORT` je
```

Nahradit:

```markdown
- dvě doplnění do téže složky karty najednou nejdou: pojmenovaný mutex `Local\Offload-append-<sha256(svazek|cesta)>`
  na každou složku karty, bez čekání (druhé skončí „Do této zálohy právě doplňuje jiná úloha …“); celá kopie zámek nebere.

### Oprava — „Zkopírovat znovu“ (Windows od 0.5.0, Mac naplánováno)

Soubory, jejichž **nejnovější** záznam v reportech na cílech selhal, se znovu zkopírují z karty — jen na cíle, kde
selhaly — jako další generace ve stejné složce karty (jen GUI, tlačítko na kartě úlohy):

- kontroly zálohy a pořadí zápisu jsou stejné jako u doplnění (složky, reporty, neporušený řetěz MHL, stejný počet
  generací na všech cílech, zámek `Local\Offload-append-…`); generace N = max + 1 na **všech** cílech;
- soubor na kartě musí mít velikost a čas změny jako v záznamu a při čtení dát zaznamenaný xxh64 (jinak `sourceError`
  `card differs from the backup …` a soubor zůstane `failed`); dobrá kopie na ostatních cílech se nepřepíše, jen přečte
  a porovná se zaznamenaným hashem (z nejnovějšího záznamu, který ho má);
- nesouhlasná kopie, která na cíli je, se před novou přesune do `_REPORT/neshoda_NNNN/<cesta>` (nikdy se nesmaže ani
  nepřepíše; obsazené místo = odmítnutí bez zápisu);
- generace MHL N obsahuje opravované soubory na všech cílech: nová kopie `action="original"`, přečtená dobrá kopie
  `action="verified"` (`hashdate` = čas opravy), `<comment>Offload oprava: 1 soubor znovu z E:\</comment>`;
- report `offload_report_NNNN.*` s `kind: "repair"`, `rewritten`, `setAside`; `<karta>.xxh64` (a `.md5`) se přepíše bez
  opakovaných řádků; nic k opravě = nic se nezapíše.

## Report ověření (Windows od 0.2.0)

„Ověřit zálohu“ (GUI) a `offload verify --report` zapisují jen do složky `_REPORT`, nikdy do `ascmhl/` — `_REPORT` je
```

Najít (5/5):

```markdown
| `fingerprint`, `shape` | string | SHA-256 (hex) přes řádky seřazené podle cesty `relpath\tsize\tmtimeTicksUtc\n` resp. `relpath\tsize\n` |
| `fileList` | string | `history/<id>.files.gz` — gzip řádků `relpath\tsize\tmtimeTicksUtc\n` (seznam po všech generacích) |
| `destinations[]` | objekt | `root`, `cardPath` |
| `generations[]` | objekt | `seq`, `kind` (`full` / `append`), `finished`, `files`, `bytes`, `verified` |
| `reels[]` | string | cívky z názvů klipů (`A001`) |

Karta se pozná podle `fsSerial` + `capacity` (GUID svazku dává Windows podle čtečky, proto se k poznání nepoužije);
```

Nahradit:

```markdown
| `fingerprint`, `shape` | string | SHA-256 (hex) přes řádky seřazené podle cesty `relpath\tsize\tmtimeTicksUtc\n` resp. `relpath\tsize\n` |
| `fileList` | string | `history/<id>.files.gz` — gzip řádků `relpath\tsize\tmtimeTicksUtc\n` (seznam po všech generacích) |
| `destinations[]` | objekt | `root`, `cardPath` |
| `generations[]` | objekt | `seq`, `kind` (`full` / `append` / `repair` — Windows od 0.5.0), `finished`, `files`, `bytes`, `verified`; po opravě se `success` a `failedFiles` záznamu spočítají znovu z reportů na cílech |
| `reels[]` | string | cívky z názvů klipů (`A001`) |

Karta se pozná podle `fsSerial` + `capacity` (GUID svazku dává Windows podle čtečky, proto se k poznání nepoužije);
```

#### `docs/ROZDILY.md`

Najít (1/3):

```markdown
| Složka | [`mac/`](../mac/) | [`windows/`](../windows/) |
| Systém | macOS 13+ | Windows 10/11 x64 |
| Jazyk / UI | Swift 5.9, SwiftUI | C# / .NET 10, WPF |
| Verze | 0.1.0 | 0.3.0-win (ve vývoji) |
| Aplikace | `Offload.app` (CLI uvnitř `Contents/Resources/bin/offload`) | `Offload.exe` + CLI `bin\offload.exe` |
| Build | `mac/scripts/build-app.sh` | `windows/scripts/build.ps1` |
```

Nahradit:

```markdown
| Složka | [`mac/`](../mac/) | [`windows/`](../windows/) |
| Systém | macOS 13+ | Windows 10/11 x64 |
| Jazyk / UI | Swift 5.9, SwiftUI | C# / .NET 10, WPF |
| Verze | 0.1.0 | 0.5.0-win (ve vývoji) |
| Aplikace | `Offload.app` (CLI uvnitř `Contents/Resources/bin/offload`) | `Offload.exe` + CLI `bin\offload.exe` |
| Build | `mac/scripts/build-app.sh` | `windows/scripts/build.ps1` |
```

Najít (2/3):

```markdown
| **Oznámení** (Windows: oznamovací oblast; ntfy na telefon, `--ntfy`) | Mac má notifikace; ntfy ⏳ naplánováno | ✅ CLI (ntfy) + GUI (čeká ruční test) |
| **Kontrola aktualizací** (manifest z URL / NAS, 1× za 24 h, ve výchozím stavu vypnuto) | ⏳ naplánováno (vlastní manifest) | ✅ GUI (čeká ruční test) |
| **Podpis exe** (`build.ps1 -Sign`, signtool) | `codesign` + notarizace (mimo tento návrh) | ✅ skript (čeká certifikát; nic se nekupuje) |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ❌ | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI: řetěz připojení s rychlostí, % linky, součtem na řadiči a úzkým hrdlem na kartě úlohy |

✅ hotovo · 🔨 rozpracováno · ⏳ naplánováno · ❌ není
```

Nahradit:

```markdown
| **Oznámení** (Windows: oznamovací oblast; ntfy na telefon, `--ntfy`) | Mac má notifikace; ntfy ⏳ naplánováno | ✅ CLI (ntfy) + GUI (čeká ruční test) |
| **Kontrola aktualizací** (manifest z URL / NAS, 1× za 24 h, ve výchozím stavu vypnuto) | ⏳ naplánováno (vlastní manifest) | ✅ GUI (čeká ruční test) |
| **Podpis exe** (`build.ps1 -Sign`, signtool) | `codesign` + notarizace (mimo tento návrh) | ✅ skript (čeká certifikát; nic se nekupuje) |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ❌ | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI (od 0.5.0): tabulka rychlostí po cílech, bublina se zařízením a připojením, „Nejpomalejší článek: …“ (% linky a součet na řadiči jen v okně Hardware) |
| **Nový vzhled** (`docs/ui-navrh/`: horní lišta, kroky 1–3, karty úloh po stavech, stránka Nastavení, tmavé i světlé téma) | ⏳ po schválení Windows verze | ✅ GUI 0.5.0 (čeká ruční test) |
| **Pozastavit / Pokračovat** (kopie, ověření, 2. fáze mezi klipy; `copyPausedSeconds`, `verifyPausedSeconds`) | ⏳ naplánováno | ✅ GUI (CLI ne) |
| **Zkopírovat znovu** (oprava: jen nesouhlasné soubory, jen na postižený disk, generace ASC MHL N, `kind: "repair"`, `_REPORT/neshoda_NNNN/`) | ⏳ naplánováno | ✅ GUI (CLI ne) |
| **Dnes a historie karet** (z `history.json`), štítek kamery a připojení, odhad času | ⏳ naplánováno | ✅ GUI |

✅ hotovo · 🔨 rozpracováno · ⏳ naplánováno · ❌ není
```

Najít (3/3):

```markdown
- **Proxy pro přelinkování** (zatím jen Windows): s timecodem `reel_name` ve stopě TC proxy (cívka: tag kamery, název
  klipu, složka karty; v reportu `clips[].info.reel`), všechny zvukové stopy zdroje (AAC 64 kb/s na kanál, ProRes PCM);
  přejmenovaná proxy (`_2`) má poznámku. Mac dává proxy jen první zvukovou stopu a bez cívky.
- **Chyby souborů v HTML česky** (Windows): JSON je sdílený, takže chyby v něm zůstávají anglicky (`size mismatch`,
  `HASH MISMATCH` …); HTML/PDF report je zobrazí česky.
- **Historie karet** (zatím jen Windows, pro Mac naplánováno): `%APPDATA%\Offload\history.json` (formát ve FORMATY.md,
```

Nahradit:

```markdown
- **Proxy pro přelinkování** (zatím jen Windows): s timecodem `reel_name` ve stopě TC proxy (cívka: tag kamery, název
  klipu, složka karty; v reportu `clips[].info.reel`), všechny zvukové stopy zdroje (AAC 64 kb/s na kanál, ProRes PCM);
  přejmenovaná proxy (`_2`) má poznámku. Mac dává proxy jen první zvukovou stopu a bez cívky.
- **Oprava — „Zkopírovat znovu“** (zatím jen Windows GUI, od 0.5.0): další generace ASC MHL N ve stejné složce karty
  na **všech** cílech (jako doplnění, stejný zámek složky), report `offload_report_NNNN.*` s `kind: "repair"`,
  `files[].rewritten` a `setAside`; dobrá kopie má v MHL `action="verified"`, nová `original`; nesouhlasná kopie se
  přesune do `_REPORT/neshoda_NNNN/<cesta>` (nemaže se). Mac takovou složku ověří ze seznamu `<karta>.xxh64` (oprava
  neopakuje řádky) a klíče ignoruje; `ascmhl verify` projde, protože `_REPORT` je ignorovaný. CLI opravu nemá.
- **Pozastavení** (zatím jen Windows GUI, od 0.5.0): report `copyPausedSeconds` / `verifyPausedSeconds` (chybí, když se
  nepozastavovalo), rychlosti a TXT `Paused:` bez pauzy. Mac klíče ignoruje — jeho rychlosti by pauzu zahrnuly.
- **Vzhled** (Windows od 0.5.0): podle `docs/ui-navrh/` s přepínačem **projektů** místo presetů, systémovým písmem
  (Segoe UI, Cascadia Mono) a tmavým i světlým tématem podle Windows; Mac převezme po schválení Windows verze. Thunderbolt
  Windows nerozliší (čtečka za ním je „NVMe“ / „USB“), Mac ano.
- **Chyby souborů v HTML česky** (Windows): JSON je sdílený, takže chyby v něm zůstávají anglicky (`size mismatch`,
  `HASH MISMATCH` …); HTML/PDF report je zobrazí česky.
- **Historie karet** (zatím jen Windows, pro Mac naplánováno): `%APPDATA%\Offload\history.json` (formát ve FORMATY.md,
```

#### `docs/VYVOJ-MAC.md`

Najít (1/1):

```markdown
  limity velikosti, síťové cesty se neotevírají; vlastní soubor pro Mac rozhodne Mac). Podpis má Mac vlastní
  (`codesign` + notarizace).

- **Mimo plán — zbývá i po něm:**
  - Blackmagic RAW Film Gen 4: Windows od 0.4.0 umí převod na Gen 5 (`--gen5`, výchozí vypnuto, neověřeno — čeká vzorek
    Gen 4); Mac zatím bez LUTu (provizorně) — rozhodne uživatel.
```

Nahradit:

```markdown
  limity velikosti, síťové cesty se neotevírají; vlastní soubor pro Mac rozhodne Mac). Podpis má Mac vlastní
  (`codesign` + notarizace).

- **Nový vzhled (Windows plán 9, 2026-10-07, 0.5.0):** návrh [`docs/ui-navrh/`](ui-navrh/) (README = zadání, sekce
  „Rozhodnutí (2026-10-07, závazné)“). Mac převezme **až po schválení Windows verze**: rozvržení (horní lišta, kroky
  1–3, karty úloh po stavech, stránka Nastavení, dnes a historie karet), přepínač **projektů** místo presetů, tmavé
  i světlé téma podle systému; **Pozastavit** (brána mezi 16MB bloky kopie a zpětného čtení a mezi klipy 2. fáze, žádný
  proces se neuspává, zámek dne se během pauzy nedrží, report `copyPausedSeconds` / `verifyPausedSeconds`);
  **Zkopírovat znovu** (oprava jako generace ASC MHL N: jen selhané soubory, jen na cíle, kde selhaly, dobrá kopie
  `verified`, nesouhlasná do `_REPORT/neshoda_NNNN/`, `kind: "repair"`, `rewritten`, `setAside`, historie
  `generations[].kind = repair` — FORMATY.md „Oprava“); štítek kamery z prvního klipu a typ připojení (Mac pozná
  i Thunderbolt); odhad času jen z rychlostí naměřených v relaci.

- **Mimo plán — zbývá i po něm:**
  - Blackmagic RAW Film Gen 4: Windows od 0.4.0 umí převod na Gen 5 (`--gen5`, výchozí vypnuto, neověřeno — čeká vzorek
    Gen 4); Mac zatím bez LUTu (provizorně) — rozhodne uživatel.
```

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1):

```markdown
| 6 | karty a bezpečnost dat: historie karet, doplnění nových klipů, název z klipů, ověření zálohy proti ASC MHL, kontrola místa, české hlášky (0.2.0) | hotovo (čeká ruční test s kartou) |
| 7 | výstupy pro produkci a střih: denní report, seznam klipů CSV/ALE, proxy pro přelinkování (reel name, všechny zvukové stopy), MD5 navíc k xxh64 (0.3.0) | hotovo (čeká ruční test: Resolve, Premiere, Avid, Excel) |
| 8 | nastavení, oznámení, distribuce: projekty, vlastní pravidla LUTů, BRAW Film Gen 4 → Gen 5, oznámení Windows a ntfy, kontrola aktualizací, verze z `Directory.Build.props`, `build.ps1 -Sign` (0.4.0) | hotovo (čeká ruční test: oznámení, podpis; Gen 4 čeká vzorek) |
| 9 | nový vzhled podle `docs/ui-navrh/`: tmavé a světlé téma, horní lišta, kroky 1–3, karty úloh s průběhem po cílech, pozastavení, oprava nesouhlasných souborů, dnešní historie, okno Nastavení (0.5.0) | rozpracováno |

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 17 — ukázkový režim pro vizuální kontrolu (výchozí vypnutý): `Offload.exe --demo pripraveno|probiha|hotovo|nastaveni` naplní okno stavy obrazovek `docs/ui-navrh/screeny` z `DemoScenario` (A035_ARRI s ALEXA 35 · LogC4 a čtečkou USB 3, zálohovaná B012_FX6 s Doplnit, DNES se třemi kartami; běžící kopie ~661 MB/s od 38 % a čekající doplnění; zelená hotová karta 602 / 799 MB/s, 24 / 24, 6 min 12 s s šedými náhledy a žlutá s nesouhlasným souborem na SHUTTLE_B; Nastavení na sekci Proxy) — místo skenů, prověření karet a `history.json`, nic nekopíruje ani nezapisuje (`OffloadJob.ForPreview`: úloha, která nikdy neběží; zavření okna ji zruší); test, že ukázka prochází stejným kódem karet jako ostrá úloha; `app-drive.ps1`: `Resize-Offload` (úzké okno 1000 px)
- Úkol 16 — dnešní historie (postup 5, nová věc 7): pod frontou „DNES“ se souhrnem „3 karty · 412 GB · vše ověřeno“, odkazem „Denní report…“ a řádky „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“ (barva podle stavu, bublina = projekt); záložka „Historie karet“ se všemi dny („Dnes“, „Včera“, „4. 10. 2026“, nejvýš 300 karet) a „Denní report…“; obojí z `history.json`, které okno už načítá mimo UI vlákno (po každé dokončené kopii znovu)
- Úkol 15 — fronta, nové věci 1–4, 9 a 10 (postup 4, 2. část): běžící karta má ukazatel fází (4 segmenty, hotové plné, aktivní podle průběhu), tabulku rychlostí na pozadí `Input` („Čtení karty“ + každý cíl s číslem, pruh, MB/s v monospace; bublina = dosavadní řádek řetězu: výrobek, připojení), „Soubor 10 / 24“ + soubor, **Pozastavit / Pokračovat** a Zrušit, text pauzy, „zbývá ~0:02 kopie · ~6:00 celkem“ pod procenty a pod kartou „Nejpomalejší článek: …“; zelená karta má řádek čtyř čísel Kopie · Ověření · Proxy · Celkem, prvních 5 náhledů (prostřední snímek, dekódovaný mimo UI vlákno) + dlaždici „+N“ a tlačítko Proxy; žlutá karta má seznam nesouhlasných souborů (cesta · „RAID_A ✓“ · „SHUTTLE_B ✕ hash“, prvních 5 + „… a další N souborů v reportu“) a **Zkopírovat znovu** — úloha „A001 · oprava“ ve frontě se stejným nastavením jako karta, nabízí se jen s kartou ve čtečce a bez jiné opravy téže zálohy; řádky tabulky a fáze se aktualizují na místě (žádné překreslování seznamu 3× za sekundu)
```

Nahradit:

```markdown
| 6 | karty a bezpečnost dat: historie karet, doplnění nových klipů, název z klipů, ověření zálohy proti ASC MHL, kontrola místa, české hlášky (0.2.0) | hotovo (čeká ruční test s kartou) |
| 7 | výstupy pro produkci a střih: denní report, seznam klipů CSV/ALE, proxy pro přelinkování (reel name, všechny zvukové stopy), MD5 navíc k xxh64 (0.3.0) | hotovo (čeká ruční test: Resolve, Premiere, Avid, Excel) |
| 8 | nastavení, oznámení, distribuce: projekty, vlastní pravidla LUTů, BRAW Film Gen 4 → Gen 5, oznámení Windows a ntfy, kontrola aktualizací, verze z `Directory.Build.props`, `build.ps1 -Sign` (0.4.0) | hotovo (čeká ruční test: oznámení, podpis; Gen 4 čeká vzorek) |
| 9 | nový vzhled podle `docs/ui-navrh/`: tmavé a světlé téma, horní lišta, kroky 1–3, karty úloh s průběhem po cílech, pozastavení, oprava nesouhlasných souborů, dnešní historie, okno Nastavení (0.5.0) | hotovo (čeká ruční test 40–47 s kartou) |

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 18 — verze **0.5.0** (`Directory.Build.props`, testy s verzí) a dokumentace: návod (`windows/package/navod/navod.html`
  → `Offload-navod.pdf`): kapitola 3 nově (schéma okna podle nového vzhledu: horní lišta, kroky 1–3, lišta se spuštěním,
  fronta s kartami podle stavu, DNES; Projekt v liště, Nastavení se sekcemi), kapitola 8 „Karta úlohy“, „Pozastavení“
  a „Zkopírovat znovu“, kapitola 9 „Nejpomalejší článek“, kapitola 10 `neshoda_NNNN`, kapitoly 2, 4–7 a 12 bez menu
  Nástroje (nové hlášky opravy); `windows/README.md` (stav, GUI, ruční test 40–47); `docs/FORMATY.md` (`kind`,
  `copyPausedSeconds`, `verifyPausedSeconds`, `setAside`, `files[].rewritten`, oddíl Oprava, `generations[].kind = repair`,
  `neshoda_NNNN`); `docs/ROZDILY.md` (verze, nový vzhled, Pozastavit, Zkopírovat znovu); `docs/VYVOJ-MAC.md` (co Mac převezme
  po schválení)
- Úkol 17 — ukázkový režim pro vizuální kontrolu (výchozí vypnutý): `Offload.exe --demo pripraveno|probiha|hotovo|nastaveni` naplní okno stavy obrazovek `docs/ui-navrh/screeny` z `DemoScenario` (A035_ARRI s ALEXA 35 · LogC4 a čtečkou USB 3, zálohovaná B012_FX6 s Doplnit, DNES se třemi kartami; běžící kopie ~661 MB/s od 38 % a čekající doplnění; zelená hotová karta 602 / 799 MB/s, 24 / 24, 6 min 12 s s šedými náhledy a žlutá s nesouhlasným souborem na SHUTTLE_B; Nastavení na sekci Proxy) — místo skenů, prověření karet a `history.json`, nic nekopíruje ani nezapisuje (`OffloadJob.ForPreview`: úloha, která nikdy neběží; zavření okna ji zruší); test, že ukázka prochází stejným kódem karet jako ostrá úloha; `app-drive.ps1`: `Resize-Offload` (úzké okno 1000 px)
- Úkol 16 — dnešní historie (postup 5, nová věc 7): pod frontou „DNES“ se souhrnem „3 karty · 412 GB · vše ověřeno“, odkazem „Denní report…“ a řádky „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“ (barva podle stavu, bublina = projekt); záložka „Historie karet“ se všemi dny („Dnes“, „Včera“, „4. 10. 2026“, nejvýš 300 karet) a „Denní report…“; obojí z `history.json`, které okno už načítá mimo UI vlákno (po každé dokončené kopii znovu)
- Úkol 15 — fronta, nové věci 1–4, 9 a 10 (postup 4, 2. část): běžící karta má ukazatel fází (4 segmenty, hotové plné, aktivní podle průběhu), tabulku rychlostí na pozadí `Input` („Čtení karty“ + každý cíl s číslem, pruh, MB/s v monospace; bublina = dosavadní řádek řetězu: výrobek, připojení), „Soubor 10 / 24“ + soubor, **Pozastavit / Pokračovat** a Zrušit, text pauzy, „zbývá ~0:02 kopie · ~6:00 celkem“ pod procenty a pod kartou „Nejpomalejší článek: …“; zelená karta má řádek čtyř čísel Kopie · Ověření · Proxy · Celkem, prvních 5 náhledů (prostřední snímek, dekódovaný mimo UI vlákno) + dlaždici „+N“ a tlačítko Proxy; žlutá karta má seznam nesouhlasných souborů (cesta · „RAID_A ✓“ · „SHUTTLE_B ✕ hash“, prvních 5 + „… a další N souborů v reportu“) a **Zkopírovat znovu** — úloha „A001 · oprava“ ve frontě se stejným nastavením jako karta, nabízí se jen s kartou ve čtečce a bez jiné opravy téže zálohy; řádky tabulky a fáze se aktualizují na místě (žádné překreslování seznamu 3× za sekundu)
```

#### `windows/README.md`

Najít (1/6):

```markdown
| Karty a bezpečnost dat (plán 6, 0.2.0): historie, doplnění, název z klipů, ověření zálohy, místo, české hlášky | hotovo (čeká ruční test s kartou) |
| Výstupy pro produkci a střih (plán 7, 0.3.0): denní report, seznam klipů CSV/ALE, proxy pro přelinkování, MD5 | hotovo (čeká ruční test v Resolve, Premiere, Avid, Excelu) |
| Nastavení, oznámení, distribuce (plán 8, 0.4.0): projekty, vlastní pravidla LUTů, BRAW Film Gen 4 → Gen 5, oznámení Windows a ntfy, kontrola aktualizací, podpis | hotovo (čeká ruční test: projekty a oznámení v GUI, podpis s certifikátem; převod Gen 4 je neověřený, čeká vzorek .braw Gen 4) |

## Build a testy
```

Nahradit:

```markdown
| Karty a bezpečnost dat (plán 6, 0.2.0): historie, doplnění, název z klipů, ověření zálohy, místo, české hlášky | hotovo (čeká ruční test s kartou) |
| Výstupy pro produkci a střih (plán 7, 0.3.0): denní report, seznam klipů CSV/ALE, proxy pro přelinkování, MD5 | hotovo (čeká ruční test v Resolve, Premiere, Avid, Excelu) |
| Nastavení, oznámení, distribuce (plán 8, 0.4.0): projekty, vlastní pravidla LUTů, BRAW Film Gen 4 → Gen 5, oznámení Windows a ntfy, kontrola aktualizací, podpis | hotovo (čeká ruční test: projekty a oznámení v GUI, podpis s certifikátem; převod Gen 4 je neověřený, čeká vzorek .braw Gen 4) |
| Nový vzhled (plán 9, 0.5.0, návrh `docs/ui-navrh/`): tmavé i světlé téma podle Windows, horní lišta, kroky 1–3, karty úloh s průběhem po cílech, Pozastavit, Zkopírovat znovu (oprava), dnes a historie karet, stránka Nastavení, štítek kamery a připojení, odhad času | hotovo (čeká ruční test 40–47 s kartou; snímky ukázkového režimu porovnané s `docs/ui-navrh/screeny`) |

## Build a testy
```

Najít (2/6):

```markdown
   `-ReleaseUrl` je adresa, kterou uživatel otevře tlačítkem *Otevřít* (https adresa, `\\NAS\Offload\`, sdílená složka).
3. Nahrajte zip na web / NAS a **až potom** manifest (manifest nesmí ukazovat na zip, který tam ještě není).

Kontrola aktualizací v aplikaci (Nástroje → Aktualizace…) čte manifest z adresy, kterou tam zadáte (`https://…`,
`\\NAS\Offload\offload-update.json`, místní cesta); nejvýš jednou za 24 h, 5 s po startu, nic nestahuje ani nespouští,
čte nejvýš 64 KB. Tlačítko *Otevřít* otevře jen adresu `https://…` nebo existující složku / zip (v Průzkumníku); cokoli
jiného se jen zobrazí. Prázdná adresa = vypnuto (výchozí). Repozitář je soukromý, proto se nečte GitHub.
```

Nahradit:

```markdown
   `-ReleaseUrl` je adresa, kterou uživatel otevře tlačítkem *Otevřít* (https adresa, `\\NAS\Offload\`, sdílená složka).
3. Nahrajte zip na web / NAS a **až potom** manifest (manifest nesmí ukazovat na zip, který tam ještě není).

Kontrola aktualizací v aplikaci (Nastavení → Aplikace → Aktualizace…) čte manifest z adresy, kterou tam zadáte (`https://…`,
`\\NAS\Offload\offload-update.json`, místní cesta); nejvýš jednou za 24 h, 5 s po startu, nic nestahuje ani nespouští,
čte nejvýš 64 KB. Tlačítko *Otevřít* otevře jen adresu `https://…` nebo existující složku / zip (v Průzkumníku); cokoli
jiného se jen zobrazí. Prázdná adresa = vypnuto (výchozí). Repozitář je soukromý, proto se nečte GitHub.
```

Najít (3/6):

```markdown
Bez podpisu (build bez `-Sign`, viz Podepisování) Windows při prvním spuštění ukáže „Windows ochránil váš počítač“ —
klikněte na „Další informace“ → „Přesto spustit“.

- **Projekt:** nahoře v levém panelu „Projekt: [Bez projektu ▾]“ — pojmenovaná nastavení (cíle, kontrola, součet, název
  z klipů, výstupy, oznámení, proxy včetně LUTu, pravidel LUTů a složky pro proxy). Menu *Projekt*: „Uložit jako
  projekt…“, „Přejmenovat…“, „Smazat…“. Titulek okna „Offload — Film X“. Přepnutí nezmění úlohy ve frontě (mají svou
  kopii nastavení); během přípravy startu je přepínač i menu zamčené (a když se projekt přesto změní, start se
  zruší s hláškou). Doplnění karty se řídí projektem dřívější zálohy; když ten už neexistuje (smazaný, přejmenovaný),
  běží s nastavením „Bez projektu“ a karta úlohy to napíše. Start kontroluje jen LUTy a složky, které se opravdu
  použijí. Nástroje, názvy USB portů a kontrola aktualizací jsou společné.
- **Pravidla LUTů:** u „Automaticky podle kamery“ tlačítko „Pravidla LUTů… (N)“ — řádky *kamera / sériové číslo / cívka /
  složka → LUT*, zkoušejí se shora dolů před rozpoznáním kamery; report u klipu ukáže „pravidlo 2: …“. Cíl „Bez LUT“
  dá klipu bez LUTu. Pravidlo z novější verze Offloadu (neznámý druh, např. `lens`) editor ukáže jen ke čtení,
  přeskočí ho a při OK ho zapíše beze změny.
- **Oznámení:** Nástroje → Oznámení… — oznámení Windows (když okno není v popředí) a volitelně ntfy na telefon (jen
  název karty, výsledek a počty, nikdy cesty). Selhání kopie se oznámí hned, když kopie skončí. Nástroje → Aktualizace…
  — adresa manifestu (výchozí prázdná = kontrola vypnutá) a „Zkontrolovat teď“; nová verze se ukáže odkazem vpravo
  v řádku menu. *Otevřít* otevře jen https adresu nebo místní složku / zip; síťové cesty (UNC, namapovaný síťový disk)
  se nikdy neotevírají, jen zobrazí k ručnímu zkopírování.

- **Karty:** karta vložená do čtečky se objeví sama a je zaškrtnutá; složku lze přidat tlačítkem „Složka…“ nebo přetažením
  složky na `Offload.exe`. Název složky v cíli lze upravit.
- **Cíle:** tlačítkem „Přidat…“ (pamatují se; zobrazí volné místo a souborový systém, u disku exFAT/FAT32 aplikace upozorní „Doporučeno NTFS“).
- **Proxy:** „Kopírovat proxy i do složky“ → „Vybrat…“: každá hotová proxy se kromě `_PROXY` na cílech zkopíruje
  i do `<složka>\<datum>\<karta>\` (nic se nepřepíše, shodné jméno dostane `_2`, `_3`…); křížek volbu zruší.
  Když složka není dostupná (odpojený disk), start nejde a v sekci Proxy je červeně „Složka pro proxy není dostupná“.
- **LUT:** v sekci Proxy seznam „Automaticky podle kamery“ (výchozí) / vestavěné LUTy / „Bez LUT“ / „Vlastní…“ (`.cube`).
  „Když kameru nepoznám:“ je záložní LUT pro klipy, které automatika nepozná (výchozí Bez LUT). Automaticky se pozná:
  Sony podle sidecaru `M01.XML` (jen S-Log3 s S-Gamut3.Cine nebo S-Gamut3, jiná gama = bez LUTu), ARRI podle modelu
  kamery (ALEXA 35 → LogC4, ostatní ALEXA/AMIRA → LogC3), ARRIRAW podle metadat, Blackmagic RAW (BRAW) podle gamy klipu (Film Gen 5 → vestavěný LUT; Film starší generace = bez LUTu, s volbou „převést na Gen 5“ (Dekodéry; **neověřené**, čeká vzorek Gen 4) LUT Gen 5; Video, Extended Video a další gamy = bez LUTu; bez údaje o gamě se předpokládá Film Gen 5),
```

Nahradit:

```markdown
Bez podpisu (build bez `-Sign`, viz Podepisování) Windows při prvním spuštění ukáže „Windows ochránil váš počítač“ —
klikněte na „Další informace“ → „Přesto spustit“.

- **Vzhled (0.5.0, návrh `docs/ui-navrh/`):** horní lišta (Projekt, stav fronty, Ověřit zálohu, Hardware s tečkou,
  Nastavení), vlevo kroky „1 · Karty“, „2 · Cíle“, „3 · Po zkopírování“ s připnutou lištou Spustit offload, vpravo
  Fronta / Historie karet a „DNES“; volby jsou na stránce Nastavení (ikona s posuvníky, Ctrl+,; Esc zavře). Tmavé
  i světlé téma podle Windows (`Themes/Dark.xaml`, `Light.xaml`); `OFFLOAD_THEME=dark|light` téma vynutí (pro snímky).
  Ukázkový režim pro vizuální kontrolu: `Offload.exe --demo pripraveno|probiha|hotovo|nastaveni` (stavy obrazovek
  z `docs/ui-navrh/screeny`; nic nekopíruje, výchozí vypnutý), snímky přes `scripts\app-drive.ps1`.
- **Projekt:** rozbalovací „Projekt <název> ▾“ v horní liště — pojmenovaná nastavení (cíle, kontrola, součet, název
  z klipů, výstupy, oznámení, proxy včetně LUTu, pravidel LUTů a složky pro proxy). V seznamu: „Uložit jako nový
  projekt…“, „Přejmenovat…“, „Smazat…“; „Upravit projekt“ u kroku 3 otevře Nastavení. Titulek okna „Offload — Film X“. Přepnutí nezmění úlohy ve frontě (mají svou
  kopii nastavení); během přípravy startu je přepínač i menu zamčené (a když se projekt přesto změní, start se
  zruší s hláškou). Doplnění karty se řídí projektem dřívější zálohy; když ten už neexistuje (smazaný, přejmenovaný),
  běží s nastavením „Bez projektu“ a karta úlohy to napíše. Start kontroluje jen LUTy a složky, které se opravdu
  použijí. Cesty k nástrojům, názvy USB portů a kontrola aktualizací jsou společné.
- **Pravidla LUTů:** u „Automaticky podle kamery“ tlačítko „Pravidla LUTů… (N)“ — řádky *kamera / sériové číslo / cívka /
  složka → LUT*, zkoušejí se shora dolů před rozpoznáním kamery; report u klipu ukáže „pravidlo 2: …“. Cíl „Bez LUT“
  dá klipu bez LUTu. Pravidlo z novější verze Offloadu (neznámý druh, např. `lens`) editor ukáže jen ke čtení,
  přeskočí ho a při OK ho zapíše beze změny.
- **Oznámení:** Nastavení → Oznámení → Upravit oznámení… — oznámení Windows (když okno není v popředí) a volitelně ntfy
  na telefon (jen název karty, výsledek a počty, nikdy cesty). Selhání kopie se oznámí hned, když kopie skončí.
  Nastavení → Aplikace → Aktualizace… — adresa manifestu (výchozí prázdná = kontrola vypnutá) a „Zkontrolovat teď“;
  nová verze se ukáže odkazem v horní liště. *Otevřít* otevře jen https adresu nebo místní složku / zip; síťové cesty (UNC, namapovaný síťový disk)
  se nikdy neotevírají, jen zobrazí k ručnímu zkopírování.

- **Karty:** karta vložená do čtečky se objeví sama a je zaškrtnutá; složku lze přidat tlačítkem „Ze složky…“ nebo přetažením
  složky na `Offload.exe`. Dlaždice ukáže kameru („ALEXA 35 · LogC4“ z prvního klipu, ffprobe nejvýš 5 s, jen čtení)
  a připojení („čtečka USB 3 · 5 Gb/s“ z HW diagnostiky; Thunderbolt Windows nerozliší). Název složky v cíli lze upravit;
  „Doplnit“ přepne kartu na doplnění nových klipů.
- **Cíle:** tlačítkem „+ Přidat disk“ (pamatují se; zobrazí „… volných z … · FS · připojení“ a pruh místa, u disku
  exFAT/FAT32 aplikace upozorní „Doporučeno NTFS“).
- **Proxy:** „Kopírovat proxy i do složky“ → „Vybrat…“: každá hotová proxy se kromě `_PROXY` na cílech zkopíruje
  i do `<složka>\<datum>\<karta>\` (nic se nepřepíše, shodné jméno dostane `_2`, `_3`…); křížek volbu zruší.
  Když složka není dostupná (odpojený disk), start nejde a pod krokem 3 i v Nastavení → Proxy je červeně „Složka pro
  proxy není dostupná“.
- **LUT:** v Nastavení → Proxy seznam „Automaticky podle kamery“ (výchozí) / vestavěné LUTy / „Bez LUT“ / „Vlastní…“ (`.cube`).
  „Když kameru nepoznám:“ je záložní LUT pro klipy, které automatika nepozná (výchozí Bez LUT). Automaticky se pozná:
  Sony podle sidecaru `M01.XML` (jen S-Log3 s S-Gamut3.Cine nebo S-Gamut3, jiná gama = bez LUTu), ARRI podle modelu
  kamery (ALEXA 35 → LogC4, ostatní ALEXA/AMIRA → LogC3), ARRIRAW podle metadat, Blackmagic RAW (BRAW) podle gamy klipu (Film Gen 5 → vestavěný LUT; Film starší generace = bez LUTu, s volbou „převést na Gen 5“ (Dekodéry; **neověřené**, čeká vzorek Gen 4) LUT Gen 5; Video, Extended Video a další gamy = bez LUTu; bez údaje o gamě se předpokládá Film Gen 5),
```

Najít (4/6):

```markdown
  takové klipy dostanou záložní LUT. U ARRI jde o předpoklad pro ProRes: klip nahraný přímo v Rec.709 by LUT dostal
  neprávem, pak zvolte „Bez LUT“. Report ukáže u každého klipu použitý LUT a důvod. Na vzorcích z kamer se ještě
  ověří rozsah: R3D a ARRIRAW (přes ProRes/MXF mezisoubor) a ProRes z ALEXA 35 (LogC4) se berou jako legální video.
- **Spuštění:** tlačítko „Spustit offload“ nebo Ctrl+Enter. Každá karta má v pravém panelu kartu úlohy: průběh, rychlosti,
  odhad času, řetěz připojení (karta → řadič → disk) a „Úzké hrdlo“. Karty se kopírují za sebou, proxy předchozí karty
  se renderují souběžně s kopií další.
- **Po dokončení:** zelený pruh, „Vysunout kartu“ (karta ve čtečce se vysune jako médium, čtečka zůstane; interní disky
  a složky se nevysouvají nikdy; vysune se jen ta karta, která se kopírovala — po výměně karty ve čtečce tlačítko zmizí),
  „Report“ (HTML z prvního připojeného cíle, PDF je vedle) a „Průzkumník“. Zrušená úloha ukáže oranžové
  „Zrušeno — karta není kompletně zálohovaná.“. Upozornění: zvuk a blikání tlačítka v hlavním panelu.
- **Hardware:** kontrola USB portů se nespouští při startu, jen na vyžádání — menu „Nástroje → Prověřit USB porty…“ (Ctrl+U)
  nebo tlačítko „Prověřit…“ v sekci Hardware. Okno Hardware ukazuje připojení, doporučení, měření rychlosti
  (s potvrzením) a USB porty s vlastními názvy. Přepínač `--no-hw-prompt` se stále přijímá, ale nic nedělá.
- **Soubory:** nastavení `%APPDATA%\Offload\settings.json`, historie offloadů `history.json` + `history\` (seznamy souborů),
  log `%APPDATA%\Offload\app.log`, poslední test HW `hwtest.json`
```

Nahradit:

```markdown
  takové klipy dostanou záložní LUT. U ARRI jde o předpoklad pro ProRes: klip nahraný přímo v Rec.709 by LUT dostal
  neprávem, pak zvolte „Bez LUT“. Report ukáže u každého klipu použitý LUT a důvod. Na vzorcích z kamer se ještě
  ověří rozsah: R3D a ARRIRAW (přes ProRes/MXF mezisoubor) a ProRes z ALEXA 35 (LogC4) se berou jako legální video.
- **Spuštění:** tlačítko „▶ Spustit offload“ (s úlohou ve frontě „Zařadit do fronty“) nebo Ctrl+Enter; nad ním
  „1 karta → 2 disky · 1,70 GB“ a odhad času (jen z rychlostí naměřených v tomto spuštění; jinak jen velikost). Každá karta
  má v pravém sloupci kartu úlohy: ukazatel fází, velké procento, „zbývá …“, tabulka rychlostí (Čtení karty + každý cíl;
  bublina = zařízení a připojení), „Soubor 10 / 24“, Pozastavit / Pokračovat, Zrušit a „Nejpomalejší článek: …“.
  Karty se kopírují za sebou, proxy předchozí karty se renderují souběžně s kopií další.
- **Pozastavit:** kopie a ověření stojí mezi 16MB bloky, 2. fáze mezi klipy (ffmpeg se neuspává); čas pauzy se do
  rychlostí nepočítá (report `copyPausedSeconds` / `verifyPausedSeconds`); Zrušit a zavření aplikace fungují i během pauzy.
- **Po dokončení:** zelená karta „… — kartu lze vysunout“ se souhrnem Kopie · Ověření · Proxy · Celkem a náhledy,
  „Vysunout kartu“ (karta ve čtečce se vysune jako médium, čtečka zůstane; interní disky a složky se nevysouvají nikdy;
  vysune se jen ta karta, která se kopírovala — po výměně karty ve čtečce tlačítko zmizí), „Report PDF“, „Otevřít
  složku“, „Proxy“. Žlutá karta („… — kartu zatím nevysunujte“) vypíše nesouhlasné soubory po discích a nabídne
  **Zkopírovat znovu**: úloha „A001 · oprava“ zkopíruje jen ty soubory a jen na disky, kde nesouhlasí (dobrá kopie se jen
  znovu přečte, nesouhlasná se odloží do `_REPORT\neshoda_NNNN\`), jako další generace ASC MHL; jen s kartou ve čtečce.
  Zrušená úloha je šedá. Upozornění: zvuk a blikání tlačítka v hlavním panelu. Pod frontou „DNES“, záložka
  „Historie karet“ (z `history.json`).
- **Hardware:** kontrola USB portů se nespouští při startu, jen na vyžádání — tlačítko „Hardware“ v horní liště (Ctrl+U;
  tečka = výsledek posledního testu) nebo Nastavení → Aplikace → „Prověřit USB porty…“. Okno Hardware ukazuje připojení, doporučení, měření rychlosti
  (s potvrzením) a USB porty s vlastními názvy. Přepínač `--no-hw-prompt` se stále přijímá, ale nic nedělá.
- **Soubory:** nastavení `%APPDATA%\Offload\settings.json`, historie offloadů `history.json` + `history\` (seznamy souborů),
  log `%APPDATA%\Offload\app.log`, poslední test HW `hwtest.json`
```

Najít (5/6):

```markdown
### Checklist ručního testu (s kartou)

Vše níže zatím nikdo neověřil na skutečné kartě — GUI je jinak zkoušené jen automaticky a na snímcích.

- [ ] 1. Vložit kartu do čtečky → objeví se v „Karty“ a je zaškrtnutá
- [ ] 2. Přidat dva cíle na různých discích
- [ ] 3. Spustit (Ctrl+Enter) → průběh, rychlosti, řetěz připojení a „Úzké hrdlo“
- [ ] 4. Kartu vysunujte až po zeleném pruhu: „Vysunout kartu“ → karta zmizí ze seznamu, čtečka zůstane; vložit další
      kartu → objeví se (skutečné vysunutí je zatím nevyzkoušené — nejdůležitější bod testu)
- [ ] 5. „Report“ otevře HTML report, PDF je vedle
```

Nahradit:

```markdown
### Checklist ručního testu (s kartou)

Vše níže zatím nikdo neověřil na skutečné kartě — GUI je jinak zkoušené jen automaticky a na snímcích.
Od 0.5.0 jsou volby dřívějších sekcí „Struktura a kontrola“, „Proxy“ a „Dekodéry“ na stránce Nastavení (Ctrl+,), menu
„Nástroje“ nahradila horní lišta (Ověřit zálohu, Hardware) a Nastavení (Oznámení, Aktualizace, Denní report) — mapování
je v plánu 9 (`docs/superpowers/plans/2026-10-07-offload-windows-9-ui.md`).

- [ ] 1. Vložit kartu do čtečky → objeví se v „Karty“ a je zaškrtnutá
- [ ] 2. Přidat dva cíle na různých discích
- [ ] 3. Spustit (Ctrl+Enter) → průběh, tabulka rychlostí (bublina = zařízení a připojení) a „Nejpomalejší článek“
- [ ] 4. Kartu vysunujte až po zeleném pruhu: „Vysunout kartu“ → karta zmizí ze seznamu, čtečka zůstane; vložit další
      kartu → objeví se (skutečné vysunutí je zatím nevyzkoušené — nejdůležitější bod testu)
- [ ] 5. „Report“ otevře HTML report, PDF je vedle
```

Najít (6/6):

```markdown
      3 pokusy, „Časové razítko se nepodařilo získat …“; heslo se nikde nevypíše. (b) **skutečný certifikát (až bude
      koupený, viz Podepisování):** `build.ps1 -Sign -CertThumbprint …` → `signtool verify /pa` projde, SmartScreen na
      čistém počítači po stažení zipu ukáže vydavatele (reputace se buduje časem)

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.
```

Nahradit:

```markdown
      3 pokusy, „Časové razítko se nepodařilo získat …“; heslo se nikde nevypíše. (b) **skutečný certifikát (až bude
      koupený, viz Podepisování):** `build.ps1 -Sign -CertThumbprint …` → `signtool verify /pa` projde, SmartScreen na
      čistém počítači po stažení zipu ukáže vydavatele (reputace se buduje časem)
- [ ] 40. **Vzhled (0.5.0):** Windows v tmavém režimu → tmavé okno jako `docs/ui-navrh/screeny`; přepnout Režim aplikací na
      světlý za běhu → okno se přebarví bez restartu, všechny texty čitelné (karty úloh, žlutá / červená hlášení, Nastavení);
      dialogy (Hardware, Oznámení, Karty už jsou zálohované) jsou ve stejném tématu; okno 1000 px široké → nic se neořízne
- [ ] 41. **Dlaždice karty:** karta z ALEXA 35 → „ALEXA 35 · LogC4“, z FX6 → „FX6 · S-Log3“, BRAW → „Blackmagic RAW“;
      čtečka USB 3 → „čtečka USB 3 · 5 Gb/s“, SD čtečka v notebooku → „SD čtečka“, CFexpress přes Thunderbolt → „NVMe“;
      karta s novými klipy → žluté „Doplnit“ (bublina = počet a velikost), po stisku „Složka: … na 2 cílech“
- [ ] 42. **Pozastavit při kopii:** karta 50+ GB na 2 disky → Pozastavit během kopie → „Pozastavuje se…“, do pár sekund
      „Pozastaveno“, rychlosti „—“, Správce úloh ukáže 0 MB/s na kartě i discích; po 2 min Pokračovat → kopie doběhne,
      „kartu lze vysunout“; v reportu „pozastaveno · mimo rychlosti“ a rychlost kopie bez pauzy; totéž během ověření
- [ ] 43. **Pozastavit ve 2. fázi:** během proxy → „Pozastaví se po proxy klipu …“, ffmpeg po doběhnutí klipu zmizí ze
      Správce úloh a nic se neobnoví; po 3 min Pokračovat → proxy pokračují (žádné „ffmpeg se zasekl“); během pauzy jiná
      karta ve frontě čeká; denní report se mezitím vytvořit dá (Denní report… u DNES)
- [ ] 44. **Pauza a výpadek:** pozastavit, vytáhnout druhý cílový disk, Pokračovat → soubory na něm selžou, první disk
      dokončí, karta žlutá „… nesouhlasí“ s „✕ zápis“; pozastavit a zavřít aplikaci → dotaz, po Ano se okno zavře do pár
      sekund a na discích nezůstane `.offload-part`; pozastavené doplnění + druhé okno se stejným doplněním → „Do této
      zálohy právě doplňuje jiná úloha …“
- [ ] 45. **Zkopírovat znovu:** po bodu 44 připojit disk → Zkopírovat znovu → úloha „A001 · oprava“ zkopíruje jen selhané
      soubory na druhý disk, první disk se jen přečte (Správce úloh: zápis jen na druhý); zelená karta; ve složce karty
      `_REPORT\offload_report_0002.json` s `"kind": "repair"`, `ascmhl\0002_…mhl` na obou discích (`verified` na prvním,
      `original` na druhém), řádek u karty zase „— ověřeno“; `offload verify` a `ascmhl verify` (na macOS) projdou.
      Ručně poškodit jeden soubor na disku (hex editor, stejná velikost) → Ověřit zálohu → nesedí; nová kopie → žlutá →
      Zkopírovat znovu → poškozený soubor je v `_REPORT\neshoda_0003\…`, nový sedí
- [ ] 46. **Zkopírovat znovu odmítne:** vyměnit kartu ve čtečce → tlačítko zmizí; zformátovat a nahrát jiný klip stejného
      jména → „Oprava zastavena — nic se nezkopírovalo …“, na discích se nic nezměnilo
- [ ] 47. **Odhad a dnešek:** první karta po spuštění → lišta jen „1 karta → 2 disky · … GB“; po první dokončené kartě
      (od 200 MB) druhá karta stejného projektu → „odhad ~… vč. proxy“, po dokončení porovnat se skutečností (±30 %);
      DNES ukazuje obě karty, Historie karet včerejší karty pod „Včera“, „Denní report…“ otevře report dne

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.
```

#### `windows/package/navod/navod.html`

Najít (1/32):

```html
.legend div { break-inside: avoid; }
.two { display: grid; grid-template-columns: 1fr 1fr; gap: 6mm; }
.note { font-size: 9.2pt; color: var(--muted); }
</style>
</head>
<body>
```

Nahradit:

```html
.legend div { break-inside: avoid; }
.two { display: grid; grid-template-columns: 1fr 1fr; gap: 6mm; }
.note { font-size: 9.2pt; color: var(--muted); }

/* Schéma hlavního okna od 0.5.0 (docs/ui-navrh) */
.win .top { display: flex; align-items: center; gap: 2mm; padding: 1.4mm 3mm; border-bottom: 1px solid #c7cdd3; font-size: 8pt; }
.win .top .sp { flex: 1; }
.win .pillq { border: 1px solid #b2b9c1; border-radius: 6pt; padding: 0 2mm; font-size: 7.6pt; white-space: nowrap; }
.win .step { font-size: 7.6pt; font-weight: 700; color: var(--muted); margin-bottom: 0.6mm; display: flex; justify-content: space-between; align-items: center; }
.win .row { display: flex; justify-content: space-between; font-size: 7.8pt; padding: 0.3mm 0; }
.win .row .okc { color: var(--ok); font-weight: 700; }
.win .phases { display: flex; gap: 1mm; margin: 1.2mm 0 0.6mm; }
.win .phases i { flex: 1; height: 1mm; background: #dde1e5; border-radius: 1mm; }
.win .phases i.done { background: var(--ok); }
.win .phases i.act { background: var(--accent); }
.win .rates { background: var(--soft); border-radius: 3pt; padding: 1mm 2mm; margin-top: 1mm; font-family: Consolas, monospace; font-size: 7.2pt; }
.win .rates div { display: flex; justify-content: space-between; }
.win .job.ok { border-color: var(--ok); background: var(--ok-soft); }
.win .job.warn { border-color: #d08a12; background: var(--warn-soft); }
.win .space { height: 1mm; background: #dde1e5; border-radius: 1mm; margin-top: 0.8mm; }
.win .space i { display: block; height: 100%; background: var(--accent); border-radius: 1mm; }
.win .tabs { display: flex; gap: 3mm; align-items: baseline; }
.win .tabs .on { border-bottom: 2px solid var(--accent); }
.win .today { margin: 2.5mm 3.5mm 3mm; border-top: 1px solid #dde1e5; padding-top: 1.5mm; }
</style>
</head>
<body>
```

Najít (2/32):

```html
    náhledy klipů, proxy s LUTy a kontrola USB zapojení. Pro kameramanské asistenty, DITy, data wranglery a střihače.
  </div>
  <div class="meta">
    Verze aplikace 0.2.0-win · Windows 10 a 11 (64bitové) · přenosná verze bez instalace<br>
    Návod popisuje aplikaci <b>Offload.exe</b> a příkazovou řádku <b>bin\offload.exe</b> z balíčku <b>Offload-win-x64-&lt;verze&gt;.zip</b>.
  </div>
</div>
```

Nahradit:

```html
    náhledy klipů, proxy s LUTy a kontrola USB zapojení. Pro kameramanské asistenty, DITy, data wranglery a střihače.
  </div>
  <div class="meta">
    Verze aplikace 0.5.0-win · Windows 10 a 11 (64bitové) · přenosná verze bez instalace<br>
    Návod popisuje aplikaci <b>Offload.exe</b> a příkazovou řádku <b>bin\offload.exe</b> z balíčku <b>Offload-win-x64-&lt;verze&gt;.zip</b>.
  </div>
</div>
```

Najít (3/32):

```html
  <ol>
    <li><span class="n">1</span><span>Co Offload dělá<span class="sub">kopie, ověření, report; proxy, náhledy, LUTy; na kartu se nikdy nezapisuje</span></span></li>
    <li><span class="n">2</span><span>Instalace a první spuštění<span class="sub">rozbalení ZIPu, SmartScreen, požadavky, volitelné doplňky, kde je nastavení a log, aktualizace</span></span></li>
    <li><span class="n">3</span><span>Hlavní okno<span class="sub">projekt, karty, cíle, volby, dekodéry, hardware, lišta se spuštěním, fronta</span></span></li>
    <li><span class="n">4</span><span>Krok za krokem: první offload<span class="sub">od vložení karty po vysunutí</span></span></li>
    <li><span class="n">5</span><span>Volby kopírování a ověření<span class="sub">datum, kontrolní součet (MD5), ověření, analýza klipů, seznamy klipů, denní report, co se kontroluje před kopií</span></span></li>
    <li><span class="n">6</span><span>Proxy, náhledy a LUTy<span class="sub">nastavení proxy, přelinkování (reel name, zvukové stopy), složka pro proxy navíc, automatický výběr LUTu, vlastní pravidla LUTů, RAW formáty, BRAW Film Gen 4</span></span></li>
    <li><span class="n">7</span><span>Report<span class="sub">co je v HTML a PDF, tři snímky na klip, řádky LUT, seznam klipů CSV / ALE, denní report</span></span></li>
    <li><span class="n">8</span><span>Fronta úloh, zrušení, vysunutí karty<span class="sub">stavy úlohy, oznámení (Windows, telefon), zrušení, zavření aplikace, vysunutí</span></span></li>
    <li><span class="n">9</span><span>Nástroje → Prověřit USB porty… a okno Hardware<span class="sub">zapojení, doporučení, měření, názvy portů, úzké hrdlo při kopii</span></span></li>
    <li><span class="n">10</span><span>Co se nachází na cílovém disku<span class="sub">struktura složek, _REPORT karty i dne, ascmhl, _PROXY</span></span></li>
    <li><span class="n">11</span><span>Příkazová řádka<span class="sub">offload copy, append, verify, day-report, bench, ports, luts</span></span></li>
    <li><span class="n">12</span><span>Řešení problémů a časté otázky</span></li>
```

Nahradit:

```html
  <ol>
    <li><span class="n">1</span><span>Co Offload dělá<span class="sub">kopie, ověření, report; proxy, náhledy, LUTy; na kartu se nikdy nezapisuje</span></span></li>
    <li><span class="n">2</span><span>Instalace a první spuštění<span class="sub">rozbalení ZIPu, SmartScreen, požadavky, volitelné doplňky, kde je nastavení a log, aktualizace</span></span></li>
    <li><span class="n">3</span><span>Hlavní okno<span class="sub">horní lišta, projekt, kroky karty – cíle – po zkopírování, lišta se spuštěním, fronta, dnes a historie karet, Nastavení</span></span></li>
    <li><span class="n">4</span><span>Krok za krokem: první offload<span class="sub">od vložení karty po vysunutí</span></span></li>
    <li><span class="n">5</span><span>Volby kopírování a ověření<span class="sub">datum, kontrolní součet (MD5), ověření, analýza klipů, seznamy klipů, denní report, co se kontroluje před kopií</span></span></li>
    <li><span class="n">6</span><span>Proxy, náhledy a LUTy<span class="sub">nastavení proxy, přelinkování (reel name, zvukové stopy), složka pro proxy navíc, automatický výběr LUTu, vlastní pravidla LUTů, RAW formáty, BRAW Film Gen 4</span></span></li>
    <li><span class="n">7</span><span>Report<span class="sub">co je v HTML a PDF, tři snímky na klip, řádky LUT, seznam klipů CSV / ALE, denní report</span></span></li>
    <li><span class="n">8</span><span>Fronta úloh, pozastavení, zrušení, vysunutí karty<span class="sub">karta úlohy, pozastavení, Zkopírovat znovu, oznámení (Windows, telefon), zrušení, zavření aplikace, vysunutí</span></span></li>
    <li><span class="n">9</span><span>Hardware: USB porty a nejpomalejší článek<span class="sub">zapojení, doporučení, měření, názvy portů, nejpomalejší článek při kopii</span></span></li>
    <li><span class="n">10</span><span>Co se nachází na cílovém disku<span class="sub">struktura složek, _REPORT karty i dne, ascmhl, _PROXY</span></span></li>
    <li><span class="n">11</span><span>Příkazová řádka<span class="sub">offload copy, append, verify, day-report, bench, ports, luts</span></span></li>
    <li><span class="n">12</span><span>Řešení problémů a časté otázky</span></li>
```

Najít (4/32):

```html

<div class="box tip"><span class="t">Složka jako argument</span>
Složku se záznamem (např. kopii karty na disku) můžete přetáhnout myší na <code>Offload.exe</code>. Aplikace se otevře
a složka bude v seznamu <b class="ui">Karty</b> rovnou zaškrtnutá.</div>

<h3>Co je ve složce</h3>
<table>
```

Nahradit:

```html

<div class="box tip"><span class="t">Složka jako argument</span>
Složku se záznamem (např. kopii karty na disku) můžete přetáhnout myší na <code>Offload.exe</code>. Aplikace se otevře
a složka bude v kroku <b class="ui">1 · Karty</b> rovnou zaškrtnutá.</div>

<h3>Co je ve složce</h3>
<table>
```

Najít (5/32):

```html
  <tr><td>RED (<code>.R3D</code>)</td><td>REDline z balíku REDCINE-X PRO</td><td>klipy se jen zkopírují a ověří</td></tr>
  <tr><td>ARRIRAW (<code>.ari</code>, <code>.mxf</code>)</td><td>ARRI Reference Tool (<code>art-cmd</code>)</td><td>klipy se jen zkopírují a ověří</td></tr>
</table>
<p>Nainstalované nástroje Offload najde sám ve standardních umístěních; jinou cestu lze zadat v sekci
<b class="ui">Dekodéry</b> hlavního okna (kapitola 3). Podpora RED a ARRIRAW je v aplikaci hotová, ale zatím nebyla
vyzkoušená se skutečnými nástroji výrobců.</p>

<h3>Kde je nastavení a log</h3>
```

Nahradit:

```html
  <tr><td>RED (<code>.R3D</code>)</td><td>REDline z balíku REDCINE-X PRO</td><td>klipy se jen zkopírují a ověří</td></tr>
  <tr><td>ARRIRAW (<code>.ari</code>, <code>.mxf</code>)</td><td>ARRI Reference Tool (<code>art-cmd</code>)</td><td>klipy se jen zkopírují a ověří</td></tr>
</table>
<p>Nainstalované nástroje Offload najde sám ve standardních umístěních; jinou cestu lze zadat v
<b class="ui">Nastavení → Dekodéry RAW</b> (kapitola 3). Podpora RED a ARRIRAW je v aplikaci hotová, ale zatím nebyla
vyzkoušená se skutečnými nástroji výrobců.</p>

<h3>Kde je nastavení a log</h3>
```

Najít (6/32):

```html
<p>Do Průzkumníka stačí do adresního řádku napsat <code>%APPDATA%\Offload</code>.</p>

<h3>Aktualizace</h3>
<p>Offload sám nic nestahuje a ve výchozím stavu se na novou verzi vůbec neptá. V <b class="ui">Nástroje → Aktualizace…</b>
zadejte <b>adresu manifestu</b> — webovou adresu <code>https://…</code> nebo cestu na NAS či sdílenou složku (např.
<code>\\NAS\Offload\offload-update.json</code>), kterou vám dá ten, kdo Offload připravuje. Aplikace se pak nejvýš jednou
za 24 hodin (5 s po spuštění) podívá, jestli je nová verze; když ano, vpravo v řádku menu se objeví odkaz
<span class="q">„Nová verze 0.5.0“</span>. Okno ukáže poznámky k verzi a adresu, kde je nový ZIP. Tlačítko
<b class="ui">Otevřít</b> je jen u adresy <code>https://…</code> (otevře prohlížeč) a u místní složky nebo ZIPu (otevře
Průzkumník s vybranou položkou). <b>Síťové cesty</b> (<code>\\NAS\…</code>, namapovaný síťový disk) a cokoli jiného se
neotevírají — zobrazí se jen jako text, který si zkopírujete a otevřete ručně (<span class="q">„Síťová cesta — zkopírujte
```

Nahradit:

```html
<p>Do Průzkumníka stačí do adresního řádku napsat <code>%APPDATA%\Offload</code>.</p>

<h3>Aktualizace</h3>
<p>Offload sám nic nestahuje a ve výchozím stavu se na novou verzi vůbec neptá. V <b class="ui">Nastavení → Aplikace →
Aktualizace…</b>
zadejte <b>adresu manifestu</b> — webovou adresu <code>https://…</code> nebo cestu na NAS či sdílenou složku (např.
<code>\\NAS\Offload\offload-update.json</code>), kterou vám dá ten, kdo Offload připravuje. Aplikace se pak nejvýš jednou
za 24 hodin (5 s po spuštění) podívá, jestli je nová verze; když ano, v horní liště se objeví odkaz
<span class="q">„Nová verze 0.6.0“</span>. Okno ukáže poznámky k verzi a adresu, kde je nový ZIP. Tlačítko
<b class="ui">Otevřít</b> je jen u adresy <code>https://…</code> (otevře prohlížeč) a u místní složky nebo ZIPu (otevře
Průzkumník s vybranou položkou). <b>Síťové cesty</b> (<code>\\NAS\…</code>, namapovaný síťový disk) a cokoli jiného se
neotevírají — zobrazí se jen jako text, který si zkopírujete a otevřete ručně (<span class="q">„Síťová cesta — zkopírujte
```

Najít (7/32):

```html
<section class="chapter" id="ch3">
<h2 class="ch"><span class="n">3</span>Hlavní okno</h2>

<p>Okno má nahoře menu <b class="ui">Nástroje</b>, vlevo panel s nastavením offloadu a dole tlačítko
<b class="ui">Spustit offload</b>, vpravo <b class="ui">Fronta</b> s kartami běžících a hotových úloh. Šířku obou
panelů lze měnit tažením dělicí čáry. Vzhled (světlý / tmavý) se řídí nastavením Windows.</p>

<figure>
<div class="win">
  <div class="bar"><span>Offload</span><span>— ☐ ✕</span></div>
  <div class="menu"><span class="callout">1</span><b>Nástroje</b> &nbsp;›&nbsp; Ověřit zálohu… &nbsp;Ctrl+Shift+O &nbsp;·&nbsp; Denní report… &nbsp;·&nbsp; Prověřit USB porty… &nbsp;Ctrl+U</div>
  <div class="body">
    <div class="left">
      <div class="sec">
        <div class="h"><span><span class="callout">2</span>Karty</span><span><span class="btn">⟳</span> <span class="btn">Složka…</span></span></div>
        <div class="tile sel">☑ <b>A001 (E:)</b><br><span class="cap">118.42 GB · exFAT</span><br>
          <span class="cap">Název složky</span> <span class="btn" style="width:48%">A001</span></div>
      </div>
      <div class="sec">
        <div class="h"><span><span class="callout">3</span>Cíle</span><span class="btn">Přidat…</span></div>
        <div class="tile"><span class="num">1</span><b>Zaloha</b> <span class="cap">· 3.21 TB volno · NTFS</span></div>
        <div class="tile"><span class="num red">2</span><b>Zaloha2</b> <span style="color:#b3261e">Disk není připojen</span></div>
      </div>
      <div class="sec">
        <div class="h"><span><span class="callout">4</span>Struktura a kontrola</span></div>
        <span class="cap">☐ Vlastní datum &nbsp;·&nbsp; <span class="mono">…\2026-10-06\&lt;název karty&gt;\</span></span><br>
        ☑ Ověřit kopie zpětným čtením (xxHash64)<br>☑ Analýza klipů a náhledy v reportu
      </div>
      <div class="sec">
        <div class="h"><span><span class="callout">5</span>Proxy</span></div>
        ☐ Renderovat proxy po ověření <span class="cap">(po zapnutí: kodek, výška, výřez, LUT…)</span>
      </div>
      <div class="sec">
        <div class="h"><span><span class="callout">6</span>› Dekodéry</span></div>
        <span class="cap">ffmpeg, ffprobe, braw-decode, REDline, art-cmd</span>
      </div>
      <div class="sec">
        <div class="h"><span><span class="callout">7</span>Hardware</span><span class="btn">Prověřit…</span></div>
        <span class="cap">Zatím neproběhl</span>
      </div>
      <div class="startbar">
        <span><span class="callout">8</span><b>1 karta</b><br><span class="cap">2 cíle · proxy</span></span>
        <span class="btn acc">▶ Spustit offload</span>
      </div>
    </div>
    <div class="right">
      <div class="qhead"><span><span class="callout">9</span>Fronta</span><span class="btn">Vyčistit hotové</span></div>
      <div class="job">
        <div style="display:flex;justify-content:space-between"><span><b>A001</b> <span class="cap">1 342 souborů · 118.42 GB</span></span><span class="pill act">Kopírování</span></div>
        <div class="cap">Zaloha › 2026-10-06/A001 &nbsp; Zaloha2 › 2026-10-06/A001</div>
        <div class="prog"><i></i></div>
        <div class="cap">Karta 310 MB/s · D1 310 MB/s · D2 310 MB/s · zbývá 2:41 · A001C004.mov</div>
        <div class="chain">Karta … — USB 3 (5 Gb/s), port 6  310 MB/s  78 % linky<br>Cíl 1 … — USB 3 (10 Gb/s a víc), port 3  310 MB/s  39 % linky<br>Úzké hrdlo: Karta …</div>
        <div style="text-align:right;margin-top:1mm"><span class="btn">Zrušit</span></div>
      </div>
      <div class="job" style="margin-bottom:3mm">
        <div style="display:flex;justify-content:space-between"><span><b>A000</b> <span class="cap">964 souborů · 87.10 GB</span></span><span class="pill ok">Hotovo</span></div>
        <div class="okline">✓ Všechny soubory jsou na 2 discích a ověřené — kartu lze vysunout. <span class="btn" style="float:right">Vysunout kartu</span></div>
        <div class="cap">kopie 305 MB/s · ověření 640 MB/s · 24 klipů · proxy 24</div>
        <div style="margin-top:1mm"><span class="btn">Report</span> <span class="btn">Průzkumník</span></div>
      </div>
    </div>
  </div>
</div>
```

Nahradit:

```html
<section class="chapter" id="ch3">
<h2 class="ch"><span class="n">3</span>Hlavní okno</h2>

<p>Nahoře je <b>lišta</b> s projektem, stavem fronty a nástroji. Pod ní vlevo jsou <b>tři kroky</b> nastavení
offloadu — karty, cíle a co proběhne po zkopírování — s připnutou lištou <b class="ui">Spustit offload</b> dole, vpravo
<b class="ui">Fronta</b> s kartami běžících a hotových úloh a pod ní přehled <b>DNES</b>. Šířku obou sloupců lze měnit
tažením dělicí čáry. Ostatní volby jsou na stránce <b class="ui">Nastavení</b> (ikona s posuvníky vpravo nahoře nebo
<kbd>Ctrl</kbd>+<kbd>,</kbd>). Vzhled — tmavý nebo světlý — se řídí nastavením Windows (Přizpůsobení → Barvy → Režim
aplikací) a přepne se hned, když ho ve Windows změníte.</p>

<figure>
<div class="win">
  <div class="bar"><span>Offload — Film X</span><span>— ☐ ✕</span></div>
  <div class="top"><span class="callout">1</span><b>Offload</b> <span class="btn">Projekt <b>Film X</b> ▾</span><span class="sp"></span>
    <span class="pillq">● Běží 1 offload · 1 ve frontě</span> <span class="btn">Ověřit zálohu</span> <span class="btn"><span class="txt-ok">●</span> Hardware</span> <span class="btn">⚙</span></div>
  <div class="body">
    <div class="left">
      <div class="sec">
        <div class="step"><span><span class="callout">2</span>1 · KARTY</span><span><span class="btn">⟳</span> <span class="btn">Ze složky…</span></span></div>
        <div class="tile sel">☑ <b>A035_ARRI</b> <span class="cap">ALEXA 35 · LogC4</span><br>
          <span class="cap">24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s</span><br>
          <span class="cap">Název složky</span> <span class="btn mono" style="width:48%">A035_ARRI</span></div>
        <div class="tile">☐ <b>B012_FX6</b> <span class="btn" style="float:right">Doplnit</span><br>
          <span class="txt-warn" style="font-size:7.6pt">Zálohováno 6. 10. 2026 18:12 … · nové soubory: 2 (14,2 GB)</span></div>
      </div>
      <div class="sec">
        <div class="step"><span><span class="callout">3</span>2 · CÍLE</span><span class="btn">+ Přidat disk</span></div>
        <div class="tile"><span class="num">1</span><b>RAID_A</b> <span class="cap">· 7,20 TB volných z 8 TB · NTFS · NVMe</span>
          <div class="space"><i style="width:12%"></i></div></div>
        <div class="tile"><span class="num">2</span><b>SHUTTLE_B</b> <span class="cap">· 1,84 TB volných z 2 TB · exFAT · USB 3</span>
          <div class="space"><i style="width:9%"></i></div></div>
        <span class="cap mono">…\2026-10-07\A035_ARRI\</span>
      </div>
      <div class="sec">
        <div class="step"><span><span class="callout">4</span>3 · PO ZKOPÍROVÁNÍ</span><span class="btn">Upravit projekt</span></div>
        <div class="row"><span><span class="okc">✓</span> Ověření zpětným čtením</span><span class="cap">xxHash64</span></div>
        <div class="row"><span><span class="okc">✓</span> Analýza klipů a náhledy</span><span class="cap">3 snímky / klip</span></div>
        <div class="row"><span><span class="okc">✓</span> Proxy</span><span class="cap">1080p · H.264 · LogC4 → 709</span></div>
        <div class="row"><span><span class="okc">✓</span> Report</span><span class="cap">PDF · HTML · ASC MHL · CSV</span></div>
      </div>
      <div class="startbar">
        <span><span class="callout">5</span><b>1 karta → 2 disky</b> · 1,70 GB<br><span class="cap">odhad ~1:30 vč. proxy</span></span>
        <span class="btn acc">▶ Spustit offload &nbsp;Ctrl ↵</span>
      </div>
    </div>
    <div class="right">
      <div class="qhead"><span class="tabs"><span class="callout">6</span><span class="on">Fronta <span class="cap">2</span></span><span class="cap">Historie karet</span></span><span class="cap">Vyčistit hotové</span></div>
      <div class="job">
        <div style="display:flex;justify-content:space-between"><span><b>A034_ARRI</b> <span class="cap">24 klipů · 1,70 GB · ALEXA 35</span></span><b>38 %</b></div>
        <div class="phases"><i class="act"></i><i></i><i></i><i></i></div>
        <div class="cap" style="display:flex;justify-content:space-between"><span>Kopírování · Ověření · Proxy · Report</span><span>zbývá ~0:02 kopie · ~6:00 celkem</span></div>
        <div class="rates"><div><span>Čtení karty</span><span>661 MB/s</span></div><div><span>1 RAID_A</span><span>661 MB/s</span></div><div><span>2 SHUTTLE_B</span><span>661 MB/s</span></div></div>
        <div class="cap" style="margin-top:1mm">Soubor 10 / 24 · <span class="mono">A034C010_261007_R2KQ.mxf</span></div>
        <div style="text-align:right;margin-top:1mm"><span class="callout">7</span><span class="btn">Pozastavit</span> <span class="btn">Zrušit</span></div>
      </div>
      <div class="job ok">
        <div><b>A033_ARRI — kartu lze vysunout</b> <span class="btn" style="float:right">Vysunout kartu</span></div>
        <div class="cap">Kopie 602 MB/s · Ověření 799 MB/s · Proxy 24 / 24 · Celkem 6 min 12 s</div>
        <div style="margin-top:1mm"><span class="btn">Report PDF</span> <span class="btn">Otevřít složku</span> <span class="btn">Proxy</span></div>
      </div>
      <div class="job warn">
        <div><b>A032_ARRI — kartu zatím nevysunujte</b></div>
        <div class="cap">1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku.</div>
        <div class="cap mono">CLIP\A032C007.mxf · RAID_A ✓ · SHUTTLE_B ✕ hash</div>
        <div style="margin-top:1mm"><span class="callout">8</span><span class="btn">Zkopírovat znovu</span> <span class="btn">Report PDF</span></div>
      </div>
      <div class="today cap"><span class="callout">9</span><b>DNES</b> · 3 karty · 412 GB · vše ověřeno <span class="btn" style="float:right">Denní report…</span></div>
    </div>
  </div>
</div>
```

Najít (8/32):

```html
</figure>

<div class="legend">
  <div><span class="callout">1</span><b>Menu Nástroje</b> — <b class="ui">Ověřit zálohu…</b> (kapitola 8), <b class="ui">Denní report…</b> (kapitola 7), <b class="ui">Prověřit USB porty…</b> (<kbd>Ctrl</kbd>+<kbd>U</kbd>), kapitola 9.</div>
  <div><span class="callout">2</span><b>Karty</b> — zdroje ke kopírování.</div>
  <div><span class="callout">3</span><b>Cíle</b> — disky a složky, kam se kopíruje.</div>
  <div><span class="callout">4</span><b>Struktura a kontrola</b> — datum, kontrolní součet a ověření, analýza klipů, seznamy klipů, denní report.</div>
  <div><span class="callout">5</span><b>Proxy</b> — proxy a LUTy, kapitola 6.</div>
  <div><span class="callout">6</span><b>Dekodéry</b> — nalezené nástroje (rozbalovací sekce).</div>
  <div><span class="callout">7</span><b>Hardware</b> — výsledek posledního testu USB.</div>
  <div><span class="callout">8</span><b>Lišta se spuštěním</b> — souhrn a <b class="ui">Spustit offload</b>.</div>
  <div><span class="callout">9</span><b>Fronta</b> — karty úloh, kapitola 8.</div>
</div>

<h3>Projekt</h3>
<p>Nahoře v levém panelu je <b class="ui">Projekt:</b> se seznamem <b>Bez projektu</b> a uložených projektů. Projekt je
pojmenované nastavení — cíle, struktura a kontrola (ověření, kontrolní součet, název z klipů, seznamy klipů, denní
report), oznámení a celá sekce Proxy včetně LUTu, pravidel LUTů a složky pro proxy. Co změníte, uloží se do právě
vybraného projektu; přepnutím na jiný a zpět se nic neztratí. Úlohy, které už jsou ve frontě, si nastavení vzaly při
spuštění — přepnutí projektu je nezmění. Během přípravy startu (kontroly, dotazy) je přepínač i menu Projekt zamčené.
Společné pro všechny projekty zůstávají cesty k nástrojům, názvy USB portů a kontrola aktualizací.</p>
<p>Menu <b class="ui">Projekt</b>: <b class="ui">Uložit jako projekt…</b> (nový projekt z toho, co je teď nastavené; stane
se vybraným), <b class="ui">Přejmenovat…</b> a <b class="ui">Smazat…</b> (<span class="q">„Smazat projekt Film X? Nastavení
projektu se ztratí, zálohy na discích zůstanou.“</span>). Název: 1–60 znaků, nesmí se opakovat (ani jinými velkými
písmeny) a <span class="q">„Bez projektu“</span> je vyhrazené. Titulek okna ukazuje vybraný projekt, např.
<span class="q">„Offload — Film X“</span>. Projekt se zapíše do reportu a historie: návrh <span class="q">„další v řadě“</span>
počítá jen karty stejného projektu a denní report ukáže <span class="q">„Projekt: Film X“</span>, když jsou z něj všechny
karty dne.</p>
<p><b>Doplnění karty</b> se řídí projektem, ve kterém se karta původně zálohovala (ne tím, který je zrovna vybraný).
Když ten projekt už neexistuje (smazaný nebo přejmenovaný), doplnění běží s nastavením <b>Bez projektu</b> a karta úlohy
to napíše. Start kontroluje jen ty LUTy a složky pro proxy, které se v úlohách opravdu použijí.</p>

<h3>Karty</h3>
<ul>
  <li>Karta vložená do čtečky se v seznamu <b>objeví sama a je zaškrtnutá</b>. Dokud žádná není, sekce ukazuje
    <span class="q">„Vložte kartu do čtečky — objeví se tady automaticky.“</span></li>
  <li>Každý řádek ukazuje jmenovku a písmeno jednotky (např. <code>A001 (E:)</code>), obsazené místo a souborový systém.
    Disk, který nevypadá jako kamerová karta, má navíc <span class="q">„nevypadá jako karta“</span> a sám se nezaškrtne.
    Za kartu se považuje médium se složkami typu <code>DCIM</code>, <code>PRIVATE</code>, <code>CLIP</code>,
    <code>CONTENTS</code>, <code>XDROOT</code>, <code>M4ROOT</code> apod. nebo s klipy (<code>.braw</code>, <code>.r3d</code>,
```

Nahradit:

```html
</figure>

<div class="legend">
  <div><span class="callout">1</span><b>Horní lišta</b> — projekt, stav fronty, <b class="ui">Ověřit zálohu</b>, <b class="ui">Hardware</b>, Nastavení.</div>
  <div><span class="callout">2</span><b>1 · Karty</b> — zdroje ke kopírování.</div>
  <div><span class="callout">3</span><b>2 · Cíle</b> — disky a složky, kam se kopíruje.</div>
  <div><span class="callout">4</span><b>3 · Po zkopírování</b> — co projekt udělá po kopii; <b class="ui">Upravit projekt</b> otevře Nastavení.</div>
  <div><span class="callout">5</span><b>Lišta se spuštěním</b> — souhrn, odhad času a <b class="ui">Spustit offload</b>.</div>
  <div><span class="callout">6</span><b>Fronta</b> a <b>Historie karet</b> — karty úloh, kapitola 8.</div>
  <div><span class="callout">7</span><b class="ui">Pozastavit</b> / <b class="ui">Zrušit</b> běžící úlohu, kapitola 8.</div>
  <div><span class="callout">8</span><b class="ui">Zkopírovat znovu</b> — oprava nesouhlasných souborů, kapitola 8.</div>
  <div><span class="callout">9</span><b>DNES</b> — karty zálohované dnes, <b class="ui">Denní report…</b></div>
</div>

<h3>Horní lišta</h3>
<ul>
  <li><b class="ui">Projekt <i>název</i> ▾</b> — přepínač projektů (níže).</li>
  <li>Odkaz <span class="q">„Nová verze 0.6.0“</span>, když kontrola aktualizací našla novější verzi (kapitola 2).</li>
  <li>Pilulka se stavem fronty: <span class="q">„● Běží 1 offload · 1 ve frontě“</span>, <span class="q">„Pozastaveno: 1 offload · 1 ve frontě“</span>;
    bez úloh se nezobrazuje.</li>
  <li><b class="ui">Ověřit zálohu</b> (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd>) — ověření hotové zálohy proti ASC MHL (kapitola 8).</li>
  <li><b class="ui">Hardware</b> (<kbd>Ctrl</kbd>+<kbd>U</kbd>) — okno Hardware (kapitola 9). Tečka u tlačítka ukazuje výsledek posledního
    testu USB: zelená = bez varování, žlutá = jsou doporučení; bez tečky test ještě neproběhl. Bublina ukáže datum testu.</li>
  <li>Ikona s posuvníky (<kbd>Ctrl</kbd>+<kbd>,</kbd>) — stránka <b class="ui">Nastavení</b> (níže).</li>
</ul>

<h3>Projekt</h3>
<p>Rozbalovací <b class="ui">Projekt</b> v horní liště má seznam <b>Bez projektu</b> a uložených projektů (vybraný je
odškrtnutý) a pod ním <b class="ui">Uložit jako nový projekt…</b>, <b class="ui">Přejmenovat…</b> a <b class="ui">Smazat…</b>.
Projekt je pojmenované nastavení — cíle, struktura a kontrola (ověření, kontrolní součet, název z klipů, seznamy klipů,
denní report), oznámení a celé nastavení proxy včetně LUTu, pravidel LUTů a složky pro proxy. Co změníte, uloží se do
právě vybraného projektu; přepnutím na jiný a zpět se nic neztratí. Úlohy, které už jsou ve frontě, si nastavení vzaly
při spuštění — přepnutí projektu je nezmění. Během přípravy startu (kontroly, dotazy) je přepínač zamčený. Společné pro
všechny projekty zůstávají cesty k nástrojům, názvy USB portů a kontrola aktualizací.</p>
<p><b class="ui">Uložit jako nový projekt…</b> založí projekt z toho, co je teď nastavené, a vybere ho;
<b class="ui">Smazat…</b> se zeptá <span class="q">„Smazat projekt Film X? Nastavení projektu se ztratí, zálohy na discích
zůstanou.“</span> Název: 1–60 znaků, nesmí se opakovat (ani jinými velkými písmeny) a <span class="q">„Bez projektu“</span>
je vyhrazené. Titulek okna ukazuje vybraný projekt, např. <span class="q">„Offload — Film X“</span>. Projekt se zapíše do
reportu a historie: návrh <span class="q">„další v řadě“</span> počítá jen karty stejného projektu a denní report ukáže
<span class="q">„Projekt: Film X“</span>, když jsou z něj všechny karty dne.</p>
<p><b>Doplnění karty</b> se řídí projektem, ve kterém se karta původně zálohovala (ne tím, který je zrovna vybraný).
Když ten projekt už neexistuje (smazaný nebo přejmenovaný), doplnění běží s nastavením <b>Bez projektu</b> a karta úlohy
to napíše. Start kontroluje jen ty LUTy a složky pro proxy, které se v úlohách opravdu použijí.</p>

<h3>1 · Karty</h3>
<ul>
  <li>Karta vložená do čtečky se <b>objeví sama a je zaškrtnutá</b>. Dokud žádná není, krok ukazuje přerušovaný rámeček
    <span class="q">„Vložte kartu do čtečky“</span> (během offloadu <span class="q">„Vložte další kartu“</span>).</li>
  <li>Dlaždice karty ukazuje název, <b>kameru</b> (např. <span class="q">„ALEXA 35 · LogC4“</span> — zjistí se z prvního klipu
    karty, jen čtením, nejvýš 5 s; když ji Offload nepozná, štítek chybí) a řádek <span class="q">„24 klipů · 1,70 GB ·
    exFAT · čtečka USB 3 · 5 Gb/s“</span>. Typ připojení je <span class="q">„čtečka USB 3 · 5 Gb/s“</span>,
    <span class="q">„USB 2.0“</span>, <span class="q">„SD čtečka“</span>, <span class="q">„NVMe“</span>, <span class="q">„SATA“</span>
    nebo <span class="q">„síť“</span>. Thunderbolt Windows nerozliší — čtečka za ním se ukáže jako NVMe nebo USB.
    Disk, který nevypadá jako kamerová karta, má navíc <span class="q">„nevypadá jako karta“</span> a sám se nezaškrtne.
    Za kartu se považuje médium se složkami typu <code>DCIM</code>, <code>PRIVATE</code>, <code>CLIP</code>,
    <code>CONTENTS</code>, <code>XDROOT</code>, <code>M4ROOT</code> apod. nebo s klipy (<code>.braw</code>, <code>.r3d</code>,
```

Najít (9/32):

```html
    karta vyprázdněná v kameře ukáže neutrálně <span class="q">„Karta je prázdná — dřív zálohováno …“</span>. Kartu pozná podle
    sériového čísla souborového systému a kapacity, takže i v jiné čtečce; přeformátovaná karta je nová. Historie je jen
    v tomto počítači (<code>%APPDATA%\Offload\history.json</code>), kopie zrušená uprostřed se do ní nezapíše.</li>
  <li>U zaškrtnuté karty je pole <b class="ui">Název složky</b>. Šedě je předvyplněný návrh: cívka z názvů klipů
    (<code>A001C003_…</code> → <code>A001</code>, pod polem <span class="q">„podle názvů klipů (A001C003…)“</span>); u karty bez
    takových názvů a bez historie <span class="q">„další v řadě po A001 (dnes)“</span> podle karet zkopírovaných do stejné
    složky data; jinak jmenovka svazku (u složky její název). Víc cívek na jedné kartě návrh nedá
    (<span class="q">„Na kartě je víc cívek: A001, A002“</span>). Dvě neznámé karty dostanou po sobě A002, A003. Napsaný název má
    vždy přednost. Návrh vypne volba v kapitole 5.</li>
  <li>U karty, na které od zálohy přibyly klipy (a dřívější cíle jsou připojené), je zaškrtávátko
    <b class="ui">Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)</b>. Zaškrtnuté místo názvu ukáže
    <span class="q">„Složka: 2026-10-06\A001 na 2 cílech“</span>: zkopírují se jen nové soubory do <b>stejné</b> složky
    karty na disky dřívější zálohy (sekce Cíle může být prázdná). Nenabízí se pro změněnou kartu, jinou kartu ani složku
    přidanou ručně. Podrobně v kapitolách 8 a 10.</li>
  <li>Tlačítko <b>⟳</b> (<span class="q">„Znovu načíst připojené disky“</span>) načte disky znovu a zkontroluje cíle.</li>
  <li><b class="ui">Složka…</b> přidá libovolnou složku (kopii karty na disku, NAS, kartu ve čtečce, kterou aplikace sama nenabídla).
    Chová se jako karta: lze ji zaškrtnout a pojmenovat.</li>
</ul>

<h3>Cíle</h3>
<ul>
  <li><b class="ui">Přidat…</b> vybere cílovou složku (kořen, pod který se zakládají složky s datem). Cílů může být libovolně;
    seznam si aplikace pamatuje.</li>
  <li>Každý cíl má číslo (v kartě úlohy se z něj stane <b>D1</b>, <b>D2</b>…), název, volné místo a souborový systém.
    Celou cestu ukáže bublina po najetí myší na název. Křížkem (<span class="q">„Odebrat cíl“</span>) cíl odeberete.</li>
  <li>Odpojený disk má červené číslo a text <span class="txt-bad">Disk není připojen</span>; dokud je tak, offload nejde spustit.
    Aplikace cíle kontroluje každých asi 5 sekund, takže se po připojení disku řádek sám vrátí do pořádku.</li>
  <li>U disku s exFAT nebo FAT32 je oranžově <span class="txt-warn">Doporučeno NTFS — spolehlivější než exFAT/FAT32</span>.</li>
  <li>Bez cílů je vidět rada <span class="q">„Přidejte dva disky — každá karta se zapíše na oba zároveň.“</span>, s jediným cílem
    varování <span class="txt-warn">Jen jeden cíl — doporučeny jsou dvě nezávislé kopie.</span></li>
</ul>

<h3>Struktura a kontrola, Proxy</h3>
<p>Volby datové složky, ověření a analýzy klipů popisuje kapitola 5, sekci Proxy kapitola 6. Pod sekcí Proxy se
objevují červené a oranžové hlášky, které vysvětlují, proč nejde spustit offload nebo co se přeskočí (kapitola 12).</p>

<h3>Dekodéry</h3>
<p>Rozbalovací sekce s pěti nástroji: <b>ffmpeg</b> (proxy, náhledy), <b>ffprobe</b> (metadata klipů),
<b>braw-decode</b> (Blackmagic RAW), <b>REDline</b> (RED R3D), <b>art-cmd</b> (ARRIRAW). U každého je cesta, kde ho
aplikace našla, nebo <span class="q">„nenalezeno“</span>; nalezený nástroj má zelenou ikonu. Tlačítkem <b class="ui">…</b>
(<span class="q">„Vybrat cestu k nástroji“</span>) vyberete jiný <code>.exe</code>. Pod seznamem je poznámka
<span class="q">„Canon Cinema RAW Light a Sony X-OCN se zkopírují a ověří, proxy pro ně nejde vyrobit.“</span></p>

<h3>Hardware</h3>
<p>Souhrn posledního testu USB portů — <span class="q">„Zatím neproběhl“</span>, nebo např. <span class="q">„Poslední test
6. 10. 2026 14:05 · bez varování“</span> / <span class="q">„… · 2 varování“</span>. Tlačítko <b class="ui">Prověřit…</b>
otevře okno Hardware (kapitola 9).</p>

<h3>Lišta se spuštěním</h3>
<p>Ukazuje počet vybraných karet (<span class="q">„Vyberte kartu“</span>, <span class="q">„1 karta“</span>, <span class="q">„3 karty“</span>)
a cílů (<span class="q">„2 cíle“</span>, se zapnutými proxy <span class="q">„2 cíle · proxy“</span>). Tlačítko
<b class="ui">Spustit offload</b> (<kbd>Ctrl</kbd>+<kbd>Enter</kbd>) je aktivní, když je vybraná aspoň jedna karta,
existuje aspoň jeden cíl, všechny cíle jsou připojené a nic dalšího nebrání startu (chybějící LUT, nedostupná složka
pro proxy, běžící měření rychlosti, nedostatek místa — důvod je vždy napsaný červeně nebo oranžově).
Pod lištou se ukáže červeně <span class="txt-bad">„Na cíli Zaloha (D:) není dost místa: potřeba 412 GB (karty 380 GB,
proxy ~30 GB, report 2 GB), volno 398 GB.“</span> (start nejde) nebo oranžově <span class="txt-warn">„Na cíli Zaloha (D:)
zbude po offloadu jen 6 GB — rezerva pod 5 %, odhad proxy je přibližný.“</span> (start jde). Počítají se vybrané karty,
úlohy ve frontě i běžící úlohy, cíle na stejném disku a složka pro proxy dohromady. Když karta ještě není zjištěná,
```

Nahradit:

```html
    karta vyprázdněná v kameře ukáže neutrálně <span class="q">„Karta je prázdná — dřív zálohováno …“</span>. Kartu pozná podle
    sériového čísla souborového systému a kapacity, takže i v jiné čtečce; přeformátovaná karta je nová. Historie je jen
    v tomto počítači (<code>%APPDATA%\Offload\history.json</code>), kopie zrušená uprostřed se do ní nezapíše.</li>
  <li>U zaškrtnuté karty je pole <b class="ui">Název složky</b> s nápovědou, odkud je návrh. Šedě je předvyplněný návrh: cívka
    z názvů klipů (<code>A001C003_…</code> → <code>A001</code>, <span class="q">„podle názvů klipů (A001C003…)“</span>); u karty bez
    takových názvů a bez historie <span class="q">„další v řadě po A001 (dnes)“</span> podle karet zkopírovaných do stejné
    složky data; jinak jmenovka svazku (u složky její název). Víc cívek na jedné kartě návrh nedá
    (<span class="q">„Na kartě je víc cívek: A001, A002“</span>). Dvě neznámé karty dostanou po sobě A002, A003. Napsaný název má
    vždy přednost. Návrh vypne volba v kapitole 5.</li>
  <li>U karty, na které od zálohy přibyly klipy (a dřívější cíle jsou připojené), je žluté tlačítko <b class="ui">Doplnit</b>
    (bublina: <span class="q">„Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)“</span>). Stisknuté kartu zaškrtne
    a místo názvu ukáže <span class="q">„Složka: 2026-10-06\A001 na 2 cílech“</span>: zkopírují se jen nové soubory do
    <b>stejné</b> složky karty na disky dřívější zálohy (krok Cíle může být prázdný). Nenabízí se pro změněnou kartu, jinou
    kartu ani složku přidanou ručně. Podrobně v kapitolách 8 a 10.</li>
  <li>Tlačítko <b>⟳</b> (<span class="q">„Znovu načíst připojené disky“</span>) načte disky znovu a zkontroluje cíle.</li>
  <li><b class="ui">Ze složky…</b> přidá libovolnou složku (kopii karty na disku, NAS, kartu ve čtečce, kterou aplikace sama
    nenabídla). Chová se jako karta: lze ji zaškrtnout a pojmenovat.</li>
</ul>

<h3>2 · Cíle</h3>
<ul>
  <li><b class="ui">+ Přidat disk</b> vybere cílovou složku (kořen, pod který se zakládají složky s datem). Cílů může být
    libovolně; seznam si aplikace pamatuje (patří k projektu).</li>
  <li>Dlaždice cíle má číslo v kolečku (v reportu z něj je <b>D1</b>, <b>D2</b>…), název, <span class="q">„7,20 TB volných z
    8 TB · NTFS · NVMe“</span> a pruh místa: šedě obsazeno, modře kolik zaberou vybrané karty (žlutě, když po offloadu zbude
    málo místa, červeně, když se nevejdou — věta pod pruhem řekne proč). Celou cestu ukáže bublina po najetí myší na
    název. Křížkem (<span class="q">„Odebrat cíl“</span>) cíl odeberete.</li>
  <li>Odpojený disk má červené číslo a text <span class="txt-bad">Disk není připojen</span>; dokud je tak, offload nejde spustit.
    Aplikace cíle kontroluje každých asi 5 sekund, takže se po připojení disku dlaždice sama vrátí do pořádku.</li>
  <li>U disku s exFAT nebo FAT32 je oranžově <span class="txt-warn">Doporučeno NTFS — spolehlivější než exFAT/FAT32</span>.</li>
  <li>Bez cílů je vidět rada <span class="q">„Přidejte dva disky — každá karta se zapíše na oba zároveň.“</span>, s jediným cílem
    varování <span class="txt-warn">Jen jeden cíl — doporučeny jsou dvě nezávislé kopie.</span></li>
  <li>Pod cíli je náhled cesty, kam se karta zapíše, např. <code>…\2026-10-07\A035_ARRI\</code> (datum podle volby v Nastavení
    → Struktura složek).</li>
</ul>

<h3>3 · Po zkopírování</h3>
<p>Čtyři řádky shrnují, co vybraný projekt udělá po kopii: <b>Ověření zpětným čtením</b> (<span class="q">„xxHash64“</span>,
s MD5 <span class="q">„xxHash64 + MD5“</span>; vypnuté žlutě s varováním), <b>Analýza klipů a náhledy</b> (<span class="q">„3 snímky
/ klip“</span>), <b>Proxy</b> (např. <span class="q">„1080p · H.264 · LogC4 → 709“</span> — křivka podle LUTu nebo kamery
vybrané karty) a <b>Report</b> (<span class="q">„PDF · HTML · ASC MHL · CSV“</span> + ALE, když je zapnutý). Odkaz
<b class="ui">Upravit projekt</b> otevře stránku Nastavení vybraného projektu. Pod řádky se objevují červené a oranžové
hlášky, které vysvětlují, proč nejde spustit offload nebo co se přeskočí (chybějící LUT, nedostupná složka pro proxy,
chybějící ffmpeg — kapitola 12).</p>
<p>Když je fronta plná a žádná karta není vybraná, kroky 2 a 3 se zmenší na kompaktní řádky (disky s volným místem a
jeden řádek s volbami), aby bylo víc místa pro frontu.</p>

<h3>Lišta se spuštěním</h3>
<p>Je připnutá dole v levém sloupci. Ukazuje <span class="q">„<b>1 karta → 2 disky</b> · 1,70 GB“</span> a pod tím odhad
času, např. <span class="q">„odhad ~1:30 vč. proxy“</span>. Odhad počítá jen z rychlostí, které Offload v tomto spuštění
aplikace opravdu naměřil (kopie, ověření a zpracování po kopii u předchozí karty); u první karty po spuštění a po změně
voleb proxy ho proto neukáže — lišta pak ukáže jen velikost. Bez karty <span class="q">„Vyberte kartu“</span>.
Tlačítko <b class="ui">▶ Spustit offload</b> (<kbd>Ctrl</kbd>+<kbd>Enter</kbd>; když už něco běží,
<b class="ui">Zařadit do fronty</b>) je aktivní, když je vybraná aspoň jedna karta, existuje aspoň jeden cíl, všechny cíle
jsou připojené a nic dalšího nebrání startu (chybějící LUT, nedostupná složka pro proxy, běžící měření rychlosti,
nedostatek místa — důvod je vždy napsaný červeně nebo oranžově nad tlačítkem).
Nad tlačítkem se ukáže červeně <span class="txt-bad">„Na cíli Zaloha (D:) není dost místa: potřeba 412 GB (karty 380 GB,
proxy ~30 GB, report 2 GB), volno 398 GB.“</span> (start nejde) nebo oranžově <span class="txt-warn">„Na cíli Zaloha (D:)
zbude po offloadu jen 6 GB — rezerva pod 5 %, odhad proxy je přibližný.“</span> (start jde). Počítají se vybrané karty,
úlohy ve frontě i běžící úlohy, cíle na stejném disku a složka pro proxy dohromady. Když karta ještě není zjištěná,
```

Najít (10/32):

```html
nedozvíme, jestli je zálohovaná, zeptá se <span class="q">„Karta A001 se ještě prověřuje — nevím, jestli už je zálohovaná.
Spustit přesto celou kopii?“</span> (výchozí odpověď je Ne).</p>

<h3>Fronta</h3>
<p>Prázdná fronta ukazuje <span class="q">„Zatím žádný offload — Vyberte kartu a cíle vlevo a stiskněte Ctrl+Enter.“</span>
Každá spuštěná karta dostane vlastní kartu úlohy, nejnovější nahoře. <b class="ui">Vyčistit hotové</b> odebere
ze seznamu dokončené úlohy (na discích se nic nemaže). Podrobně v kapitole 8.</p>
</section>

<!-- ============ 4 ============ -->
```

Nahradit:

```html
nedozvíme, jestli je zálohovaná, zeptá se <span class="q">„Karta A001 se ještě prověřuje — nevím, jestli už je zálohovaná.
Spustit přesto celou kopii?“</span> (výchozí odpověď je Ne).</p>

<h3>Fronta a Historie karet</h3>
<p>Prázdná fronta ukazuje, co se s kartou stane — ikony Karta → 2 disky naráz → Ověření → Proxy → Report (jen kroky, které
projekt dělá) a <span class="q">„Připraveno — A035_ARRI na 2 disky“</span>; bez karty <span class="q">„Zatím žádný
offload“</span>. Každá spuštěná karta dostane vlastní kartu úlohy, nejnovější nahoře; číslo u záložky
<b class="ui">Fronta</b> je počet nedokončených úloh. <b class="ui">Vyčistit hotové</b> odebere ze seznamu dokončené úlohy
(na discích se nic nemaže). Pod frontou je <b>DNES</b>: <span class="q">„3 karty · 412 GB · vše ověřeno“</span>, odkaz
<b class="ui">Denní report…</b> a řádek pro každou dnes zálohovanou kartu (<span class="q">„A034_ARRI · 256 GB · 18:12 ·
✓ 2 kopie ověřeny“</span>, oranžově <span class="q">„! neověřeno“</span>, červeně <span class="q">„✕ s chybami (3 soubory)“</span>).
Záložka <b class="ui">Historie karet</b> ukáže totéž pro všechny dny (<span class="q">„Dnes“</span>, <span class="q">„Včera“</span>,
<span class="q">„4. 10. 2026“</span>; nejvýš 300 posledních karet). Obojí je z historie offloadů v tomto počítači. Podrobně
o kartách úloh v kapitole 8.</p>

<h3>Nastavení</h3>
<p>Stránka <b class="ui">Nastavení</b> se otevře místo obou sloupců (fronta dál běží); <b class="ui">‹ Zpět</b>,
<b class="ui">Hotovo</b> nebo <kbd>Esc</kbd> ji zavřou. Změny se ukládají hned do vybraného projektu (jeho název je
v liště stránky; tlačítko <b class="ui">Uložit jako nový projekt…</b> založí nový). Sekce:</p>
<table>
  <thead><tr><th class="w25">Sekce</th><th>Co v ní je</th></tr></thead>
  <tr><td><b class="ui">Struktura složek</b></td><td>cesta <code>&lt;cíl&gt;\&lt;datum&gt;\&lt;název karty&gt;\</code>, <b class="ui">Vlastní datum</b>,
      <b class="ui">Navrhovat název karty podle klipů</b> (kapitola 5)</td></tr>
  <tr><td><b class="ui">Ověření</b></td><td>ověření zpětným čtením, kontrolní součet (MD5), <b class="ui">Ověřit zálohu…</b> a
      <b class="ui">Ukládat report ověření</b> (kapitoly 5 a 8)</td></tr>
  <tr><td><b class="ui">Analýza a náhledy</b></td><td>analýza klipů a tři snímky v reportu (kapitola 5)</td></tr>
  <tr><td><b class="ui">Proxy</b></td><td>vše o proxy, LUTech a náhled burn-inu (kapitola 6)</td></tr>
  <tr><td><b class="ui">Report</b></td><td>seznamy klipů CSV a ALE, denní report po každé kartě, <b class="ui">Denní report…</b> (kapitoly 5 a 7)</td></tr>
  <tr><td><b class="ui">Oznámení</b></td><td>souhrn a <b class="ui">Upravit oznámení…</b> (kapitola 8)</td></tr>
  <tr><td><b class="ui">Dekodéry RAW</b></td><td>nástroje ffmpeg, ffprobe, braw-decode, REDline, art-cmd a převod BRAW Film Gen 4 → Gen 5
      (níže a kapitola 6); platí pro všechny projekty</td></tr>
  <tr><td><b class="ui">Aplikace</b></td><td>verze, <b class="ui">Aktualizace…</b> (kapitola 2), <b class="ui">Prověřit USB porty…</b> (kapitola 9)</td></tr>
</table>
<p><b>Dekodéry RAW:</b> u každého nástroje je cesta, kde ho aplikace našla, nebo <span class="q">„nenalezeno“</span>; nalezený
nástroj má zelenou ikonu. Tlačítkem <b class="ui">…</b> (<span class="q">„Vybrat cestu k nástroji“</span>) vyberete jiný
<code>.exe</code>. Pod seznamem je poznámka <span class="q">„Canon Cinema RAW Light a Sony X-OCN se zkopírují a ověří, proxy pro
ně zatím nejde vyrobit.“</span></p>
</section>

<!-- ============ 4 ============ -->
```

Najít (11/32):

```html
<h2 class="ch"><span class="n">4</span>Krok za krokem: první offload</h2>

<ol class="steps">
  <li><b>Připojte cílové disky</b> a v sekci <b class="ui">Cíle</b> je přidejte tlačítkem <b class="ui">Přidat…</b>.
    Doporučené jsou dva nezávislé disky. Stačí to udělat jednou — cíle si aplikace pamatuje.</li>
  <li><b>Vložte kartu do čtečky.</b> Objeví se v sekci <b class="ui">Karty</b> a je zaškrtnutá. Karet může být víc
    najednou; zkopírují se jedna po druhé. Kartu, kterou aplikace nenabídla, přidejte tlačítkem <b class="ui">Složka…</b>.</li>
  <li><b>Zkontrolujte název složky karty.</b> Předvyplněná je jmenovka svazku. Pokud ji chcete změnit (např. na
    <code>A001</code>), napište název do pole <b class="ui">Název složky</b>. Náhled cesty pod volbou
    <b class="ui">Vlastní datum</b> ukazuje, kam se bude kopírovat: <code>…\2026-10-06\&lt;název karty&gt;\</code>.</li>
  <li><b>Zkontrolujte volby</b>: <b class="ui">Ověřit kopie zpětným čtením (xxHash64)</b> nechte zapnuté. Chcete-li proxy,
    zapněte <b class="ui">Renderovat proxy po ověření</b> a nastavte je (kapitola 6).</li>
  <li><b>Spusťte</b> tlačítkem <b class="ui">Spustit offload</b> nebo <kbd>Ctrl</kbd>+<kbd>Enter</kbd>. Spuštěné karty
    se v seznamu odškrtnou a ve frontě vpravo se pro každou objeví karta úlohy.</li>
  <li><b>Sledujte průběh.</b> Karta úlohy ukazuje fázi (<span class="pill act">Kopírování</span>,
    <span class="pill act">Ověřování</span>, …), ukazatel průběhu, rychlost čtení karty a zápisu na každý disk
    (<code>Karta 310 MB/s · D1 310 MB/s · D2 305 MB/s</code>), odhad zbývajícího času a právě kopírovaný soubor.
    Pod tím je řetěz připojení a <b>Úzké hrdlo</b> (kapitola 9).</li>
  <li><b>Počkejte na zelený pruh</b> <span class="txt-ok">„Všechny soubory jsou na 2 discích a ověřené — kartu lze vysunout.“</span>
    Současně zazní zvuk. Teprve teď je karta bezpečně zálohovaná.</li>
  <li><b>Vysuňte kartu</b> tlačítkem <b class="ui">Vysunout kartu</b>. Karta zmizí ze seznamu, čtečka zůstane připojená.
    Vložte další kartu a pokračujte od kroku 2 — proxy předchozí karty se mezitím dál renderují.</li>
  <li><b>Po dokončení</b> se karta úlohy přepne na <span class="pill ok">Hotovo</span> a tlačítko aplikace v hlavním panelu
    Windows zabliká. Pod kartou je souhrn (<span class="q">„kopie 305 MB/s · ověření 640 MB/s · 24 klipů · proxy 24“</span>),
    tlačítko <b class="ui">Report</b> otevře HTML report a <b class="ui">Průzkumník</b> otevře složku karty na prvním cíli.</li>
</ol>

<div class="box warn"><span class="t">Nevytahujte kartu dřív</span>
Dokud karta úlohy neukazuje zelený pruh, kartu ani cílové disky neodpojujte. Pokud se úloha zruší nebo některý
soubor selže, karta <b>není</b> zálohovaná (kapitola 8).</div>

<div class="box tip"><span class="t">Stejná karta podruhé</span>
```

Nahradit:

```html
<h2 class="ch"><span class="n">4</span>Krok za krokem: první offload</h2>

<ol class="steps">
  <li><b>Připojte cílové disky</b> a v kroku <b class="ui">2 · Cíle</b> je přidejte tlačítkem <b class="ui">+ Přidat disk</b>.
    Doporučené jsou dva nezávislé disky. Stačí to udělat jednou — cíle si aplikace pamatuje.</li>
  <li><b>Vložte kartu do čtečky.</b> Objeví se v kroku <b class="ui">1 · Karty</b> a je zaškrtnutá. Karet může být víc
    najednou; zkopírují se jedna po druhé. Kartu, kterou aplikace nenabídla, přidejte tlačítkem <b class="ui">Ze složky…</b>.</li>
  <li><b>Zkontrolujte název složky karty.</b> Předvyplněný je návrh (cívka z názvů klipů, další v řadě nebo jmenovka
    svazku). Pokud ho chcete změnit (např. na <code>A001</code>), napište název do pole <b class="ui">Název složky</b>.
    Náhled cesty pod cíli ukazuje, kam se bude kopírovat: <code>…\2026-10-06\A001\</code>.</li>
  <li><b>Zkontrolujte krok 3 · Po zkopírování</b>: <b>Ověření zpětným čtením</b> má být zelené (vypnuté je žluté s varováním).
    Chcete-li proxy, otevřete <b class="ui">Upravit projekt</b>, v sekci <b class="ui">Proxy</b> je zapněte a nastavte (kapitola 6).</li>
  <li><b>Spusťte</b> tlačítkem <b class="ui">▶ Spustit offload</b> nebo <kbd>Ctrl</kbd>+<kbd>Enter</kbd>. Spuštěné karty
    se odškrtnou a ve frontě vpravo se pro každou objeví karta úlohy.</li>
  <li><b>Sledujte průběh.</b> Karta úlohy ukazuje ukazatel fází (Kopírování · Ověření · Proxy · Report), velké procento
    aktuální fáze, odhad zbývajícího času, tabulku rychlostí (<code>Čtení karty 661 MB/s · 1 RAID_A 661 MB/s · 2 SHUTTLE_B
    661 MB/s</code>) a právě kopírovaný soubor (<span class="q">„Soubor 10 / 24“</span>). Pod kartou je
    <b>Nejpomalejší článek</b> (kapitola 9). Úlohu můžete <b class="ui">Pozastavit</b> (kapitola 8).</li>
  <li><b>Počkejte na zelenou kartu</b> <span class="txt-ok">„A001 — kartu lze vysunout“</span> s větou
    <span class="q">„Všech 24 souborů je na 2 discích a ověřených (xxHash64).“</span> Současně zazní zvuk. Teprve teď je karta
    bezpečně zálohovaná.</li>
  <li><b>Vysuňte kartu</b> tlačítkem <b class="ui">Vysunout kartu</b>. Karta zmizí z kroku 1, čtečka zůstane připojená.
    Vložte další kartu a pokračujte od kroku 2 — proxy předchozí karty se mezitím dál renderují.</li>
  <li><b>Po dokončení</b> ukáže zelená karta souhrn <span class="q">„Kopie 602 MB/s · Ověření 799 MB/s · Proxy 24 / 24 ·
    Celkem 6 min 12 s“</span> a náhledy prvních klipů, tlačítko aplikace v hlavním panelu Windows zabliká.
    <b class="ui">Report PDF</b> otevře report, <b class="ui">Otevřít složku</b> složku karty na prvním cíli a
    <b class="ui">Proxy</b> složku s proxy. Karta se objeví i pod frontou v přehledu <b>DNES</b>.</li>
</ol>

<div class="box warn"><span class="t">Nevytahujte kartu dřív</span>
Dokud karta úlohy není zelená s textem „kartu lze vysunout“, kartu ani cílové disky neodpojujte. Pokud se úloha zruší nebo některý
soubor selže, karta <b>není</b> zálohovaná (kapitola 8).</div>

<div class="box tip"><span class="t">Stejná karta podruhé</span>
```

Najít (12/32):

```html
<section class="chapter" id="ch5">
<h2 class="ch"><span class="n">5</span>Volby kopírování a ověření</h2>

<p>Sekce <b class="ui">Struktura a kontrola</b>. Volby ověření a analýzy klipů si aplikace pamatuje.</p>

<table>
  <thead><tr><th class="w35">Volba</th><th class="w20">Výchozí</th><th>Význam</th></tr></thead>
  <tr><td><b class="ui">Vlastní datum</b></td><td>vypnuto (dnešní datum)</td>
      <td>Složka s datem na cílech je normálně dnešní datum ve tvaru <code>RRRR-MM-DD</code>. Po zaškrtnutí vyberete jiné
      datum v kalendáři (třeba natáčecí den, který skončil po půlnoci). Pod volbou je náhled cesty
      <code>…\2026-10-06\&lt;název karty&gt;\</code>. Vlastní datum platí do zavření aplikace.</td></tr>
  <tr><td><b class="ui">Kontrolní součet</b></td><td>xxHash64 (rychlý)</td>
      <td><b class="ui">xxHash64 + MD5</b> spočítá navíc MD5 každého souboru (pro produkce a pojišťovny, které ho chtějí):
      MD5 je v reportu, v <code>_REPORT\&lt;karta&gt;.md5</code> a v ASC MHL a ověření čte obojí. MD5 nikdy není samotné —
      xxHash64 je vždy, takže zálohu dál ověří i Mac a starší verze. Pod volbou oranžově <span class="txt-warn">„MD5 je
      pomalejší — u rychlých karet (CFexpress) může zpomalit kopii.“</span> Doplnění použije součty dřívější zálohy.</td></tr>
  <tr><td><b class="ui">Ověřit kopie zpětným čtením (xxHash64)</b><br>(s MD5: <b class="ui">(xxHash64 + MD5)</b>)</td><td>zapnuto</td>
      <td>Po kopii se každý soubor z každého cíle přečte znovu a porovnají se kontrolní součty. Bez ověření se objeví
      oranžové <span class="txt-warn">„Bez ověření není jisté, že data na disku jsou v pořádku.“</span>, report má štítek
      <b>ZKOPÍROVÁNO — NEOVĚŘENO</b> a zelený pruh „kartu lze vysunout“ se <b>neukáže</b>.</td></tr>
  <tr><td><b class="ui">Navrhovat název karty podle klipů</b></td><td>zapnuto</td>
      <td>Šedý návrh v poli <b class="ui">Název složky</b> z názvů klipů (ARRI, Sony Venice, Canon Cinema EOS, RED,
      Blackmagic) nebo „další v řadě“ (kapitola 3). Vypnuto = jmenovka svazku jako dřív.</td></tr>
  <tr><td><b class="ui">Analýza klipů a náhledy v reportu</b></td><td>zapnuto</td>
      <td>Po kopii se u každého klipu zjistí formát, kodek, rozlišení, snímková frekvence, délka, timecode a zvukové stopy
      a do reportu se vloží tři snímky. Čte se z cílových disků, ne z karty. Potřebuje ffmpeg a ffprobe (jsou přibalené).</td></tr>
  <tr><td><b class="ui">Seznam klipů CSV</b></td><td>zapnuto</td>
```

Nahradit:

```html
<section class="chapter" id="ch5">
<h2 class="ch"><span class="n">5</span>Volby kopírování a ověření</h2>

<p>Stránka <b class="ui">Nastavení</b> (kapitola 3), sekce <b class="ui">Struktura složek</b>, <b class="ui">Ověření</b>,
<b class="ui">Analýza a náhledy</b> a <b class="ui">Report</b>. Volby patří k vybranému projektu; krok 3 hlavního okna
je shrnuje.</p>

<table>
  <thead><tr><th class="w35">Volba</th><th class="w20">Výchozí</th><th>Význam</th></tr></thead>
  <tr><td><b class="ui">Vlastní datum</b></td><td>vypnuto (dnešní datum)</td>
      <td>Složka s datem na cílech je normálně dnešní datum ve tvaru <code>RRRR-MM-DD</code>. Po zaškrtnutí vyberete jiné
      datum v kalendáři (třeba natáčecí den, který skončil po půlnoci). Náhled cesty je pod cíli v hlavním okně
      (<code>…\2026-10-06\A001\</code>). Vlastní datum platí do zavření aplikace.</td></tr>
  <tr><td><b class="ui">Kontrolní součet</b></td><td>xxHash64 (rychlý)</td>
      <td><b class="ui">xxHash64 + MD5</b> spočítá navíc MD5 každého souboru (pro produkce a pojišťovny, které ho chtějí):
      MD5 je v reportu, v <code>_REPORT\&lt;karta&gt;.md5</code> a v ASC MHL a ověření čte obojí. MD5 nikdy není samotné —
      xxHash64 je vždy, takže zálohu dál ověří i Mac a starší verze. Pod volbou oranžově <span class="txt-warn">„MD5 je
      pomalejší — u rychlých karet (CFexpress) může zpomalit kopii.“</span> Doplnění použije součty dřívější zálohy.</td></tr>
  <tr><td><b class="ui">Ověření</b> → <b class="ui">Zapnuto</b><br>(v kroku 3: Ověření zpětným čtením)</td><td>zapnuto</td>
      <td>Po kopii se každý soubor z každého cíle přečte znovu a porovnají se kontrolní součty. Bez ověření je řádek
      v kroku 3 žlutý s varováním <span class="txt-warn">„Bez ověření není jisté, že data na disku jsou v pořádku.“</span>,
      report má štítek <b>ZKOPÍROVÁNO — NEOVĚŘENO</b> a karta úlohy je žlutá („zkopírováno bez ověření“), nikdy zelená
      „kartu lze vysunout“.</td></tr>
  <tr><td><b class="ui">Navrhovat název karty podle klipů</b></td><td>zapnuto</td>
      <td>Šedý návrh v poli <b class="ui">Název složky</b> z názvů klipů (ARRI, Sony Venice, Canon Cinema EOS, RED,
      Blackmagic) nebo „další v řadě“ (kapitola 3). Vypnuto = jmenovka svazku jako dřív.</td></tr>
  <tr><td><b class="ui">Analýza a náhledy</b> → <b class="ui">Zapnuto</b></td><td>zapnuto</td>
      <td>Po kopii se u každého klipu zjistí formát, kodek, rozlišení, snímková frekvence, délka, timecode a zvukové stopy
      a do reportu se vloží tři snímky. Čte se z cílových disků, ne z karty. Potřebuje ffmpeg a ffprobe (jsou přibalené).</td></tr>
  <tr><td><b class="ui">Seznam klipů CSV</b></td><td>zapnuto</td>
```

Najít (13/32):

```html
<h3>Jak kopie probíhá</h3>
<ul>
  <li>Karta se čte jednou, data jdou současně na všechny cíle. Pomalejší disk může chvíli zaostávat, aniž by brzdil čtení karty.</li>
  <li>Čte i zapisuje se mimo systémovou paměť (cache) a po zápisu se vyprázdní i cache disku, takže ověření čte data
    opravdu z disku. Svazky, které to neumějí (některé síťové), přejdou na běžný zápis a report to u cíle uvede:
    <span class="q">„Cíl 1: svazek nepodporuje přímý zápis, použita systémová cache.“</span></li>
```

Nahradit:

```html
<h3>Jak kopie probíhá</h3>
<ul>
  <li>Karta se čte jednou, data jdou současně na všechny cíle. Pomalejší disk může chvíli zaostávat, aniž by brzdil čtení karty.</li>
  <li>Kopii i ověření lze <b class="ui">Pozastavit</b> a pak <b class="ui">Pokračovat</b> (kapitola 8). Zastaví se mezi bloky
    dat (16 MB) — rozpracovaný soubor zůstane otevřený a po obnovení pokračuje. Doba pauzy se do rychlostí v reportu
    nepočítá.</li>
  <li>Čte i zapisuje se mimo systémovou paměť (cache) a po zápisu se vyprázdní i cache disku, takže ověření čte data
    opravdu z disku. Svazky, které to neumějí (některé síťové), přejdou na běžný zápis a report to u cíle uvede:
    <span class="q">„Cíl 1: svazek nepodporuje přímý zápis, použita systémová cache.“</span></li>
```

Najít (14/32):

```html
<section class="chapter" id="ch6">
<h2 class="ch"><span class="n">6</span>Proxy, náhledy a LUTy</h2>

<p>Proxy se renderují až po ověření kopie a čtou se z cílového disku, ne z karty. Zapnete je volbou
<b class="ui">Renderovat proxy po ověření</b> v sekci <b class="ui">Proxy</b>; teprve pak se ukážou další volby.
Hotové proxy jsou ve složce <code>_PROXY</code> ve složce karty jako <code>&lt;klip&gt;.mov</code>.</p>

<h3>Nastavení proxy</h3>
<table>
  <thead><tr><th class="w25">Volba</th><th class="w20">Výchozí</th><th>Možnosti a význam</th></tr></thead>
  <tr><td><b class="ui">Kodek</b></td><td>H.264</td><td>H.264, H.265 / HEVC, ProRes 422 Proxy, ProRes 422 LT, ProRes 422, ProRes 422 HQ.</td></tr>
  <tr><td><b class="ui">Výška</b></td><td>1080p</td><td>540p, 720p, 1080p, 2160p.</td></tr>
  <tr><td><b class="ui">Datový tok</b></td><td>10 Mb/s</td><td>Jen pro H.264 a H.265; tlačítky − a + v rozsahu 2–50 Mb/s.</td></tr>
  <tr><td><b class="ui">Výřez</b></td><td>celý obraz</td><td>Ořízne proxy na frame lines: celý obraz, 1.33, 1.66, 1.78, 1.85, 2.00, 2.39. Při zvoleném poměru
      se vedle objeví velikost výřezu v <span class="q">„% obrazu“</span> (50–100 %; bublina: <span class="q">„Velikost frame lines vůči
      obrazu – 90 % = 10 % overscan“</span>).</td></tr>
  <tr><td><b class="ui">LUT</b></td><td>Automaticky podle kamery</td><td>Viz níže, „LUT pro proxy a náhledy“.</td></tr>
  <tr><td><b class="ui">Burn-in názvu klipu</b></td><td>zapnuto</td><td>Název klipu vypálený do obrazu proxy.</td></tr>
  <tr><td><b class="ui">Burn-in timecodu</b></td><td>zapnuto</td><td>Timecode vypálený do obrazu proxy.</td></tr>
  <tr><td><b class="ui">Zvuk</b></td><td>zapnuto</td><td>Proxy obsahují <b>všechny</b> zvukové stopy klipu (např. 8 mono stop z Sony MXF) —
      Premiere připojí proxy jen se stejným počtem stop. H.264/H.265: AAC 64 kb/s na kanál každé stopy (nejméně 128,
      nejvýš 512 kb/s); ProRes: nekomprimovaný zvuk (PCM).</td></tr>
```

Nahradit:

```html
<section class="chapter" id="ch6">
<h2 class="ch"><span class="n">6</span>Proxy, náhledy a LUTy</h2>

<p>Proxy se renderují až po ověření kopie a čtou se z cílového disku, ne z karty. Zapnete je v <b class="ui">Nastavení →
Proxy</b> vypínačem <b class="ui">Zapnuto</b> (stránku otevře i odkaz <b class="ui">Upravit projekt</b> u kroku 3). Vpravo je
<b>náhled burn-inu</b> — šedý rám 16:9 s názvem klipu a timecodem vlevo a vpravo dole a se zvoleným výřezem (frame lines);
skutečný snímek z klipu v něm zatím není.
Hotové proxy jsou ve složce <code>_PROXY</code> ve složce karty jako <code>&lt;klip&gt;.mov</code>.</p>

<h3>Nastavení proxy</h3>
<table>
  <thead><tr><th class="w25">Volba</th><th class="w20">Výchozí</th><th>Možnosti a význam</th></tr></thead>
  <tr><td><b class="ui">Kodek</b></td><td>H.264</td><td>H.264, H.265 (HEVC), ProRes Proxy, ProRes LT, ProRes 422, ProRes HQ.</td></tr>
  <tr><td><b class="ui">Výška</b></td><td>1080p</td><td>540p, 720p, 1080p, 2160p.</td></tr>
  <tr><td><b class="ui">Datový tok</b></td><td>10 Mb/s</td><td>Jen pro H.264 a H.265; tlačítky − a + v rozsahu 2–50 Mb/s, vedle
      kolik to dělá za minutu (<span class="q">„≈ 75 MB / minutu“</span>).</td></tr>
  <tr><td><b class="ui">Výřez</b></td><td>celý obraz</td><td>Ořízne proxy na frame lines: celý obraz, 1.33, 1.66, 1.78, 1.85, 2.00, 2.39. Při zvoleném poměru
      se vedle objeví velikost výřezu v <span class="q">„% obrazu“</span> (50–100 %; bublina: <span class="q">„Velikost frame lines vůči
      obrazu – 90 % = 10 % overscan“</span>).</td></tr>
  <tr><td><b class="ui">LUT</b></td><td>Automaticky podle kamery</td><td>Viz níže, „LUT pro proxy a náhledy“. Tabulka
      <b>LUT podle kamery</b> ukazuje vestavěné LUTy; ten, který se použije, je označený <span class="q">„použito“</span>.</td></tr>
  <tr><td><b class="ui">Název klipu</b> (náhled burn-inu)</td><td>zapnuto</td><td>Název klipu vypálený do obrazu proxy.</td></tr>
  <tr><td><b class="ui">Timecode</b> (náhled burn-inu)</td><td>zapnuto</td><td>Timecode vypálený do obrazu proxy.</td></tr>
  <tr><td><b class="ui">Zvuk</b></td><td>zapnuto</td><td>Proxy obsahují <b>všechny</b> zvukové stopy klipu (např. 8 mono stop z Sony MXF) —
      Premiere připojí proxy jen se stejným počtem stop. H.264/H.265: AAC 64 kb/s na kanál každé stopy (nejméně 128,
      nejvýš 512 kb/s); ProRes: nekomprimovaný zvuk (PCM).</td></tr>
```

Najít (15/32):

```html
<ul>
  <li>Nic se nepřepíše: když už tam soubor stejného jména je, nový dostane <code>_2</code>, <code>_3</code>…</li>
  <li>Křížek vedle (<span class="q">„Nekopírovat do složky“</span>) volbu zruší.</li>
  <li>Když složka není dostupná (odpojený disk), offload nejde spustit a v sekci Proxy je červeně
    <span class="txt-bad">„Složka pro proxy není dostupná: …“</span>.</li>
  <li>Když složka zmizí až během úlohy, proxy zůstanou na cílech a u klipu je v reportu poznámka
    <span class="q">„kopie do … selhala: …“</span>.</li>
```

Nahradit:

```html
<ul>
  <li>Nic se nepřepíše: když už tam soubor stejného jména je, nový dostane <code>_2</code>, <code>_3</code>…</li>
  <li>Křížek vedle (<span class="q">„Nekopírovat do složky“</span>) volbu zruší.</li>
  <li>Když složka není dostupná (odpojený disk), offload nejde spustit a v Nastavení → Proxy i pod krokem 3 je červeně
    <span class="txt-bad">„Složka pro proxy není dostupná: …“</span>.</li>
  <li>Když složka zmizí až během úlohy, proxy zůstanou na cílech a u klipu je v reportu poznámka
    <span class="q">„kopie do … selhala: …“</span>.</li>
```

Najít (16/32):

```html

<h3>Blackmagic RAW Film Gen 4</h3>
<p>Klipy natočené v Blackmagic Design Film <b>Gen 4</b> dostávají proxy bez LUTu (vestavěný LUT je pro Gen 5). Volba
v sekci Dekodéry <b class="ui">Blackmagic RAW Film Gen 4 převést na Gen 5 (použije se LUT Gen 5)</b> nechá knihovnu
Blackmagic RAW klip dekódovat s barevnou vědou Gen 5 a použije LUT Gen 5 (důvod v reportu: <span class="q">„Blackmagic RAW:
Film Gen 4 → Gen 5 (převod SDK)“</span>). Týká se jen gamy <b>Film</b> — Video a Extended Video zůstanou jak jsou. Volba je
výchozí <b>vypnutá</b> a <b>neověřená</b>: <i>ověří ji až vzorek .braw Gen 4</i>, zatím se ví jen, že kód funguje s fiktivní
```

Nahradit:

```html

<h3>Blackmagic RAW Film Gen 4</h3>
<p>Klipy natočené v Blackmagic Design Film <b>Gen 4</b> dostávají proxy bez LUTu (vestavěný LUT je pro Gen 5). Volba
v Nastavení → Dekodéry RAW <b class="ui">Blackmagic RAW Film Gen 4 převést na Gen 5 (použije se LUT Gen 5)</b> nechá knihovnu
Blackmagic RAW klip dekódovat s barevnou vědou Gen 5 a použije LUT Gen 5 (důvod v reportu: <span class="q">„Blackmagic RAW:
Film Gen 4 → Gen 5 (převod SDK)“</span>). Týká se jen gamy <b>Film</b> — Video a Extended Video zůstanou jak jsou. Volba je
výchozí <b>vypnutá</b> a <b>neověřená</b>: <i>ověří ji až vzorek .braw Gen 4</i>, zatím se ví jen, že kód funguje s fiktivní
```

Najít (17/32):

```html

<p>Report se zapisuje do složky <code>_REPORT</code> ve složce karty <b>na každém cíli</b>. Hlavní je HTML report
<code>&lt;karta&gt;_report.html</code>; vedle je stejný report jako PDF <code>&lt;karta&gt;_report.pdf</code> (A4 na šířku,
s vloženými snímky). Tlačítko <b class="ui">Report</b> na kartě úlohy otevře HTML z prvního připojeného cíle.</p>

<h3>Co v reportu je</h3>
<ul>
  <li><b>Hlavička</b>: název karty, štítek stavu <span class="pill ok">OVĚŘENO</span>, <span class="pill warn">ZKOPÍROVÁNO — NEOVĚŘENO</span>
    nebo <span class="pill bad">CHYBA</span>, datum a čas začátku a konce.</li>
  <li><b>Souhrn</b>: počet souborů, objem dat, počet klipů a jejich celková délka, doba a rychlost kopírování a ověření, kontrolní součet (XXH64, nebo XXH64 + MD5).</li>
  <li><b>Cíle</b>: tabulka disků — disk, složka, souborový systém, počet souborů OK a chyb. Pod ní zdroj a jeho souborový systém.</li>
  <li><b>Chyby</b> (jen když nějaké jsou): soubor a problém česky, zvlášť pro kartu a pro každý cíl — např.
    <span class="q">„cíl 2: nesouhlasí kontrolní součet (cíl …, karta …)“</span>, <span class="q">„jiná velikost: …“</span>,
```

Nahradit:

```html

<p>Report se zapisuje do složky <code>_REPORT</code> ve složce karty <b>na každém cíli</b>. Hlavní je HTML report
<code>&lt;karta&gt;_report.html</code>; vedle je stejný report jako PDF <code>&lt;karta&gt;_report.pdf</code> (A4 na šířku,
s vloženými snímky). Tlačítko <b class="ui">Report PDF</b> na kartě úlohy otevře report z prvního připojeného cíle (PDF,
když už je hotové, jinak HTML).</p>

<h3>Co v reportu je</h3>
<ul>
  <li><b>Hlavička</b>: název karty, štítek stavu <span class="pill ok">OVĚŘENO</span>, <span class="pill warn">ZKOPÍROVÁNO — NEOVĚŘENO</span>
    nebo <span class="pill bad">CHYBA</span>, datum a čas začátku a konce.</li>
  <li><b>Souhrn</b>: počet souborů, objem dat, počet klipů a jejich celková délka, doba a rychlost kopírování a ověření, kontrolní součet (XXH64, nebo XXH64 + MD5).
    Když byla úloha pozastavená, je tu navíc <span class="q">„pozastaveno · mimo rychlosti“</span> — doba pauzy se do časů ani rychlostí nepočítá.
    Report opravy (<b class="ui">Zkopírovat znovu</b>, kapitola 8) má v hlavičce <span class="q">„oprava (generace 2)“</span>.</li>
  <li><b>Cíle</b>: tabulka disků — disk, složka, souborový systém, počet souborů OK a chyb. Pod ní zdroj a jeho souborový systém.</li>
  <li><b>Chyby</b> (jen když nějaké jsou): soubor a problém česky, zvlášť pro kartu a pro každý cíl — např.
    <span class="q">„cíl 2: nesouhlasí kontrolní součet (cíl …, karta …)“</span>, <span class="q">„jiná velikost: …“</span>,
```

Najít (18/32):

```html
kamery, ověření, chyby, proxy hotovo / selhalo, LUTy, počet generací po doplnění), sekce <b>Problémy</b> (selhané soubory,
neověřené karty, nečitelné reporty, složka karty <span class="q">„bez reportu (probíhá, nebo byl přerušen)“</span>)
a <b>LUTy</b>. Vedle je seznam klipů celého dne <code>&lt;datum&gt;_clips.csv</code> (a <code>.ale</code>).
Vypíná se volbou <b class="ui">Denní report po každé kartě</b>. Ručně: <b class="ui">Nástroje → Denní report…</b>
a vyberte složku dne na cíli (nebo složku karty v ní) — report se vytvoří a otevře. Dvě úlohy, které skončí zároveň,
se vystřídají; každý cíl má report podle toho, co je na něm.</p>
<p>Ve složce <code>ascmhl</code> vedle je ASC MHL (verze 2) se součty všech souborů — lze ji ověřit nástroji třetích stran
(<code>ascmhl</code>, Silverstack, ShotPut). Snímky z reportu jsou jen v HTML a PDF, ne v JSON.</p>
```

Nahradit:

```html
kamery, ověření, chyby, proxy hotovo / selhalo, LUTy, počet generací po doplnění), sekce <b>Problémy</b> (selhané soubory,
neověřené karty, nečitelné reporty, složka karty <span class="q">„bez reportu (probíhá, nebo byl přerušen)“</span>)
a <b>LUTy</b>. Vedle je seznam klipů celého dne <code>&lt;datum&gt;_clips.csv</code> (a <code>.ale</code>).
Vypíná se volbou <b class="ui">Denní report po každé kartě</b> (Nastavení → Report). Ručně: <b class="ui">Denní report…</b>
(pod frontou u DNES, v záložce Historie karet nebo v Nastavení → Report) a vyberte složku dne na cíli (nebo složku karty v ní) — report se vytvoří a otevře. Dvě úlohy, které skončí zároveň,
se vystřídají; každý cíl má report podle toho, co je na něm.</p>
<p>Ve složce <code>ascmhl</code> vedle je ASC MHL (verze 2) se součty všech souborů — lze ji ověřit nástroji třetích stran
(<code>ascmhl</code>, Silverstack, ShotPut). Snímky z reportu jsou jen v HTML a PDF, ne v JSON.</p>
```

Najít (19/32):

```html

<!-- ============ 8 ============ -->
<section class="chapter" id="ch8">
<h2 class="ch"><span class="n">8</span>Fronta úloh, zrušení, vysunutí karty</h2>

<h3>Jak fronta pracuje</h3>
<p>Karty se kopírují <b>jedna po druhé</b> (sdílejí cílové disky). Analýza klipů a proxy předchozí karty běží souběžně
s kopií další karty. Každá úloha si při spuštění uloží nastavení, se kterým startovala — pozdější změny voleb ji neovlivní.</p>

<h3>Stavy úlohy</h3>
<table>
  <thead><tr><th class="w25">Štítek</th><th>Co znamená</th></tr></thead>
  <tr><td><span class="pill wait">Čeká</span></td><td>úloha je ve frontě, kopíruje se jiná karta</td></tr>
  <tr><td><span class="pill act">Kopírování</span></td><td>čtení karty a zápis na všechny cíle</td></tr>
  <tr><td><span class="pill act">Zápis cache disků</span></td><td>vyprázdnění vyrovnávací paměti disků, aby data byla opravdu na disku</td></tr>
  <tr><td><span class="pill act">Ověřování</span></td><td>zpětné čtení z cílů a porovnání kontrolních součtů</td></tr>
  <tr><td><span class="pill act">Report</span></td><td>zápis reportu, seznamu součtů a MHL</td></tr>
  <tr><td><span class="pill act">Analýza klipů</span></td><td>metadata a snímky klipů (<span class="q">„Klip 3/24 · A001C003“</span>)</td></tr>
  <tr><td><span class="pill act">Render proxy</span></td><td>proxy (<span class="q">„Proxy 3/24 · A001C003 · 45 %“</span>)</td></tr>
  <tr><td><span class="pill ok">Hotovo</span></td><td>úloha skončila bez chyby kopie</td></tr>
  <tr><td><span class="pill bad">Chyba</span></td><td>úloha selhala (důvod je červeně na kartě), nebo některé soubory selhaly</td></tr>
  <tr><td><span class="pill warn">Zrušeno</span></td><td>úlohu jste zrušili</td></tr>
</table>

<h3>Texty na kartě úlohy</h3>
<ul>
  <li><span class="txt-ok">„Všechny soubory jsou na N discích a ověřené — kartu lze vysunout.“</span> — karta je zálohovaná;
    zazní zvuk. Platí jen se zapnutým ověřením.</li>
  <li><span class="txt-bad">„N souborů selhalo — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.“</span> — v reportu
    v části Chyby najdete které soubory a proč.</li>
  <li><span class="txt-warn">„Zrušeno — karta není kompletně zálohovaná.“</span> — kopie nebo ověření byly přerušené.</li>
  <li>Po dokončení souhrn, např. <span class="q">„kopie 305 MB/s · ověření 640 MB/s · 24 klipů · proxy 23, 1 chyba“</span>.</li>
</ul>
<p>Po skončení každé úlohy tlačítko Offloadu v hlavním panelu Windows zabliká; při chybě zazní chybový zvuk.</p>

<h3>Oznámení</h3>
<p>Když okno Offloadu není v popředí, ukáže Windows na konci úlohy oznámení (ikona Offloadu se objeví v oznamovací
```

Nahradit:

```html

<!-- ============ 8 ============ -->
<section class="chapter" id="ch8">
<h2 class="ch"><span class="n">8</span>Fronta úloh, pozastavení, zrušení, vysunutí karty</h2>

<h3>Jak fronta pracuje</h3>
<p>Karty se kopírují <b>jedna po druhé</b> (sdílejí cílové disky). Analýza klipů a proxy předchozí karty běží souběžně
s kopií další karty. Každá úloha si při spuštění uloží nastavení, se kterým startovala — pozdější změny voleb ji neovlivní.</p>

<h3>Karta úlohy</h3>
<p>Karta úlohy mění vzhled podle stavu:</p>
<table>
  <thead><tr><th class="w25">Vzhled</th><th>Co znamená</th></tr></thead>
  <tr><td><span class="pill wait">Čeká ve frontě</span></td><td>kompaktní řádek (<span class="q">„B012_FX6 — doplnění · 2 nové klipy · 14,2 GB · stejné
      cíle“</span>) s tlačítkem <b class="ui">Zrušit</b>; kopíruje se jiná karta</td></tr>
  <tr><td><span class="pill act">běží</span></td><td>název, <span class="q">„24 klipů · 1,70 GB · ALEXA 35“</span>, velké procento aktuální fáze,
      ukazatel fází (<b>Kopírování · Ověření · Proxy · Report</b>, nebo <b>Analýza</b> bez proxy; hotové fáze zeleně, aktivní
      modře), <span class="q">„zbývá ~0:02 kopie · ~6:00 celkem“</span>, tabulka rychlostí (<b>Čtení karty</b> a každý cíl —
      bublina řádku ukáže zařízení a připojení), <span class="q">„Soubor 10 / 24“</span> s názvem souboru (ve 2. fázi
      <span class="q">„Klip 3 / 24“</span>, <span class="q">„Proxy 3 / 24 · 45 %“</span>), tlačítka <b class="ui">Pozastavit</b>
      a <b class="ui">Zrušit</b>; pod kartou <b>Nejpomalejší článek</b> (kapitola 9)</td></tr>
  <tr><td><span class="pill ok">zelená</span></td><td><span class="txt-ok">„A001 — kartu lze vysunout“</span> a <span class="q">„Všech 24 souborů je na
      2 discích a ověřených (xxHash64).“</span>; zazní zvuk. Řádek <b>Kopie · Ověření · Proxy · Celkem</b> (Celkem bez pauz),
      náhledy prvních pěti klipů (prostřední snímek, šestá dlaždice <span class="q">„+19“</span>), <b class="ui">Vysunout
      kartu</b>, <b class="ui">Report PDF</b>, <b class="ui">Otevřít složku</b>, <b class="ui">Proxy</b> a cesta ke kartě na
      prvním cíli. Když po ověřené kopii selže zpracování (proxy, PDF), zůstane karta zelená a chyba je oranžový řádek —
      karta zálohovaná je. Doplnění bez nových klipů: <span class="q">„A001 — záloha je úplná“</span>.</td></tr>
  <tr><td><span class="pill warn">žlutá</span></td><td><span class="txt-warn">„A001 — kartu zatím nevysunujte“</span> a věta, co a kde nesouhlasí
      (<span class="q">„1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku.“</span>), seznam souborů
      (<span class="q">„CLIP\A001C007.mxf · RAID_A ✓ · SHUTTLE_B ✕ hash“</span>; důvod <i>hash</i>, <i>velikost</i>,
      <i>čtení</i>, <i>zápis</i>, <i>karta</i> nebo <i>zrušeno</i>; prvních 5, pak <span class="q">„… a další N souborů
      v reportu“</span>) a <b class="ui">Zkopírovat znovu</b> (níže). Žlutá je i <span class="q">„A001 — zkopírováno bez
      ověření“</span> (ověření vypnuté).</td></tr>
  <tr><td><span class="pill bad">červená</span></td><td><span class="txt-bad">„A001 — offload selhal“</span> s důvodem (kontrola před kopií, kapitola 12)</td></tr>
  <tr><td><span class="pill">šedá</span></td><td><span class="q">„A001 — zrušeno“</span>: <span class="q">„Karta není kompletně zálohovaná — zkopírujte
      ji znovu celou.“</span>, nebo po ověřené kopii <span class="q">„Kopie je hotová a ověřená, zrušilo se jen zpracování po
      kopii.“</span></td></tr>
</table>

<h3>Pozastavení</h3>
<p><b class="ui">Pozastavit</b> na běžící kartě úlohu zastaví v nejbližším bezpečném místě a tlačítko se změní na
<b class="ui">Pokračovat</b>. Nic se tím neruší ani nemaže:</p>
<ul>
  <li><b>Kopie a ověření</b> se zastaví mezi bloky dat (16 MB): disky dopíšou to, co už mají, a stojí; rozpracované
    soubory <code>.offload-part</code> zůstanou otevřené. Karta úlohy ukáže nejdřív <span class="q">„Pozastavuje se…“</span>,
    pak <span class="q">„Pozastaveno — Pokračovat spustí úlohu od místa, kde stojí.“</span>; rychlosti ukazují „—“ a odhad
    času zmizí.</li>
  <li><b>Analýza klipů a proxy</b> se zastaví <b>po</b> rozpracovaném klipu (<span class="q">„Pozastaví se po proxy klipu
    A003…“</span>) — ffmpeg se nikdy neuspává uprostřed práce. Stejně tak PDF a denní report doběhnou a pauza přijde až
    před ně.</li>
  <li>Doba pauzy se do rychlostí a časů v reportu ani do „Celkem“ na kartě nepočítá; po <b class="ui">Pokračovat</b> se
    rychlost měří znovu.</li>
  <li>Během pauzy počítač nepřejde do spánku (otevřené soubory na USB disku by spánek mohl poškodit). Pozastavená úloha
    blokuje frontu — další karta čeká.</li>
  <li><b class="ui">Zrušit</b> i zavření aplikace během pauzy úlohu ukončí hned, jako bez pauzy (rozpracované soubory se
    smažou).</li>
  <li>Když během pauzy odpojíte cílový disk nebo vytáhnete kartu, po <b class="ui">Pokračovat</b> soubory na tom disku
    selžou (ostatní cíle pokračují) a karta úlohy zežloutne — pak pomůže <b class="ui">Zkopírovat znovu</b>.</li>
  <li>Doplnění drží svou zálohu zamčenou i během pauzy: jiná úloha do ní mezitím doplňovat nemůže.</li>
</ul>

<h3>Zkopírovat znovu</h3>
<p>Na žluté kartě (některé soubory nesouhlasí nebo selhaly) je tlačítko <b class="ui">Zkopírovat znovu</b>. Spustí
úlohu <span class="q">„A001 · oprava“</span>, která znovu zkopíruje <b>jen soubory, které nesouhlasí, a jen na disky,
kde nesouhlasí</b> — se stejným nastavením jako původní úloha a vždy s ověřením:</p>
<ul>
  <li>Tlačítko je jen u dokončené úlohy (ne zrušené), když je <b>stejná karta ve čtečce</b> a žádná oprava téže zálohy už
    nečeká ani neběží.</li>
  <li>Dobrá kopie se nikdy nepřepíše: na disku, kde je soubor v pořádku, se jen znovu přečte a porovná se zaznamenaným
    kontrolním součtem.</li>
  <li>Nesouhlasná kopie se nesmaže: před novou kopií se přesune do <code>_REPORT\neshoda_0002\…</code> ve složce karty
    (číslo je generace opravy, kapitola 10). Smazat ji můžete ručně, až zálohu ověříte.</li>
  <li>Soubor na kartě musí mít stejnou velikost, čas i kontrolní součet jako při první kopii — když se karta mezitím
    změnila, oprava nic nezapíše (<span class="q">„Oprava zastavena — nic se nezkopírovalo. …“</span>, kapitola 12).</li>
  <li>Oprava zapíše novou generaci reportu a ASC MHL na <b>všechny</b> cíle (jako doplnění), přepíše seznamy součtů,
    seznamy klipů a denní report. Historie karty se opraví — po úspěšné opravě je řádek u karty zase
    <span class="txt-ok">„— ověřeno“</span>. Analýza klipů ani proxy se neopakují.</li>
  <li>Když není co opravit: <span class="q">„Nic k opravě — všechny soubory jsou na všech cílech v pořádku.“</span>
    Přerušená oprava nic nepokazí — spusťte ji znovu.</li>
  <li>Oprava a doplnění téže zálohy nikdy neběží naráz (<span class="q">„Do této zálohy právě doplňuje jiná úloha …“</span>).
    Z příkazové řádky opravu spustit nejde.</li>
</ul>
<p>Po skončení každé úlohy tlačítko Offloadu v hlavním panelu Windows zabliká; při chybě zazní chybový zvuk.
Hotová karta se objeví v přehledu <b>DNES</b> pod frontou a v záložce <b class="ui">Historie karet</b>.</p>

<h3>Oznámení</h3>
<p>Když okno Offloadu není v popředí, ukáže Windows na konci úlohy oznámení (ikona Offloadu se objeví v oznamovací
```

Najít (20/32):

```html
<span class="q">„Offload: A001 — CHYBA“</span> a při renderu proxy už po kopii <span class="q">„Offload: A001 — kartu lze
vysunout“</span>. Úloha, která selže při kopii, ohlásí chybu hned, jak kopie skončí. Zrušená úloha oznámení nemá. Zvuk
a blikání tlačítka zůstávají.</p>
<p><b class="ui">Nástroje → Oznámení…</b>: <b class="ui">Oznámení Windows</b> (zapnuto) a <b class="ui">Posílat na telefon přes
ntfy</b> — do aplikace ntfy v telefonu (server <code>https://ntfy.sh</code> nebo vlastní, <b>téma</b> jen písmena bez
diakritiky, číslice, <code>-</code> a <code>_</code>). Tlačítko <b class="ui">Poslat zkušební oznámení</b> ukáže
<span class="q">„Odesláno.“</span> nebo <span class="q">„Nepodařilo se: …“</span> (nejvýš po 10 s). Téma volte nehádatelné —
```

Nahradit:

```html
<span class="q">„Offload: A001 — CHYBA“</span> a při renderu proxy už po kopii <span class="q">„Offload: A001 — kartu lze
vysunout“</span>. Úloha, která selže při kopii, ohlásí chybu hned, jak kopie skončí. Zrušená úloha oznámení nemá. Zvuk
a blikání tlačítka zůstávají.</p>
<p><b class="ui">Nastavení → Oznámení → Upravit oznámení…</b>: <b class="ui">Oznámení Windows</b> (zapnuto) a <b class="ui">Posílat na telefon přes
ntfy</b> — do aplikace ntfy v telefonu (server <code>https://ntfy.sh</code> nebo vlastní, <b>téma</b> jen písmena bez
diakritiky, číslice, <code>-</code> a <code>_</code>). Tlačítko <b class="ui">Poslat zkušební oznámení</b> ukáže
<span class="q">„Odesláno.“</span> nebo <span class="q">„Nepodařilo se: …“</span> (nejvýš po 10 s). Téma volte nehádatelné —
```

Najít (21/32):

```html
a tu kartu nespustí (ostatní ano).</p>

<h3>Ověření zálohy</h3>
<p><b class="ui">Nástroje → Ověřit zálohu…</b> (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd>) vybere složku karty na cíli,
nebo celou složku dne (ověří se každá karta zvlášť). Každé ověření je položka fronty <span class="q">„Ověření A001“</span>
a běží ve stejné frontě jako kopie, jedno po druhém (zatěžuje stejné disky): <span class="q">„Ověřuji 120 / 1 234 souborů
· 45 GB / 512 GB · 380 MB/s“</span>. Výsledek zeleně <span class="txt-ok">„Ověřeno: všech 1 234 souborů sedí (ASC MHL,
2 generace, xxHash64).“</span>, červeně <span class="txt-bad">„Nesedí 2 soubory, chybí 1, nečitelný 0 — podrobnosti
v reportu ověření.“</span>, oranžově soubory navíc, soubory, které selhaly už při kopii, a změněné generace MHL. Tlačítka
<b class="ui">Report ověření</b> a <b class="ui">Průzkumník</b>; <b class="ui">Zrušit</b> ověření ukončí bez reportu.
Report ověření se ukládá, dokud je v menu zaškrtnuté <b class="ui">Ukládat report ověření</b>: do <code>_REPORT</code>
karty, a u zálohy s MHL z jiného programu (který <code>_REPORT</code> neignoruje) do <code>_REPORT</code> vedle složky
karty, s poznámkou <span class="q">„Report ověření je uložený mimo složku karty, aby nenarušil její historii ASC MHL“</span>.
Ověřují se všechny generace ASC MHL a všechny jeho kontrolní součty (xxHash64, MD5, SHA-1, xxHash3, xxHash128, C4) —
```

Nahradit:

```html
a tu kartu nespustí (ostatní ano).</p>

<h3>Ověření zálohy</h3>
<p><b class="ui">Ověřit zálohu</b> v horní liště (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd>; také v Nastavení → Ověření) vybere složku karty na cíli,
nebo celou složku dne (ověří se každá karta zvlášť). Každé ověření je položka fronty <span class="q">„Ověření A001“</span>
a běží ve stejné frontě jako kopie, jedno po druhém (zatěžuje stejné disky): <span class="q">„Ověřuji 120 / 1 234 souborů
· 45 GB / 512 GB · 380 MB/s“</span>. Výsledek zeleně <span class="txt-ok">„Ověřeno: všech 1 234 souborů sedí (ASC MHL,
2 generace, xxHash64).“</span>, červeně <span class="txt-bad">„Nesedí 2 soubory, chybí 1, nečitelný 0 — podrobnosti
v reportu ověření.“</span>, oranžově soubory navíc, soubory, které selhaly už při kopii, a změněné generace MHL. Tlačítka
<b class="ui">Report ověření</b> a <b class="ui">Průzkumník</b>; <b class="ui">Zrušit</b> ověření ukončí bez reportu.
Report ověření se ukládá, dokud je v Nastavení → Ověření zaškrtnuté <b class="ui">Ukládat report ověření</b>: do <code>_REPORT</code>
karty, a u zálohy s MHL z jiného programu (který <code>_REPORT</code> neignoruje) do <code>_REPORT</code> vedle složky
karty, s poznámkou <span class="q">„Report ověření je uložený mimo složku karty, aby nenarušil její historii ASC MHL“</span>.
Ověřují se všechny generace ASC MHL a všechny jeho kontrolní součty (xxHash64, MD5, SHA-1, xxHash3, xxHash128, C4) —
```

Najít (22/32):

```html
do <code>ascmhl</code> se při ověření nikdy nic nezapisuje.</p>

<h3>Zrušení</h3>
<p>Tlačítko <b class="ui">Zrušit</b> je na kartě úlohy, dokud úloha čeká nebo běží. Rozpracované soubory
(<code>.offload-part</code>) se smažou, hotové zůstanou. Zrušená úloha, která ještě nezačala, se rovnou označí
<span class="pill warn">Zrušeno</span>. Zrušíte-li úlohu až během analýzy klipů nebo proxy, karta už je zálohovaná
a zelený pruh platí dál.</p>

<h3>Zavření aplikace během offloadu</h3>
<p>Při zavření okna s běžící úlohou se aplikace zeptá <span class="q">„Probíhá offload. Opravdu ukončit? Rozpracované soubory
```

Nahradit:

```html
do <code>ascmhl</code> se při ověření nikdy nic nezapisuje.</p>

<h3>Zrušení</h3>
<p>Tlačítko <b class="ui">Zrušit</b> je na kartě úlohy, dokud úloha čeká, běží nebo je pozastavená. Rozpracované soubory
(<code>.offload-part</code>) se smažou, hotové zůstanou. Zrušená úloha, která ještě nezačala, se rovnou označí
<span class="pill warn">Zrušeno</span>. Zrušíte-li úlohu až během analýzy klipů nebo proxy, karta už je zálohovaná
a „kartu lze vysunout“ platí dál.</p>

<h3>Zavření aplikace během offloadu</h3>
<p>Při zavření okna s běžící úlohou se aplikace zeptá <span class="q">„Probíhá offload. Opravdu ukončit? Rozpracované soubory
```

Najít (23/32):

```html

<h3>Vysunutí karty</h3>
<ul>
  <li>Tlačítko <b class="ui">Vysunout kartu</b> je vedle zeleného pruhu. Karta ve čtečce se vysune jako médium, čtečka zůstane
    připojená. Pak je na kartě úlohy <span class="q">„Karta vysunuta“</span> a karta zmizí ze seznamu Karty.</li>
  <li>Vysune se <b>jen ta karta, která se kopírovala</b>. Když kartu ve čtečce vyměníte nebo vytáhnete, tlačítko zmizí.</li>
  <li>Nevysune se, dokud z karty kopíruje jiná úloha (<span class="q">„z karty ještě kopíruje jiná úloha — vysuňte ji, až její
    kopie doběhne“</span>).</li>
```

Nahradit:

```html

<h3>Vysunutí karty</h3>
<ul>
  <li>Tlačítko <b class="ui">Vysunout kartu</b> je na zelené kartě úlohy. Karta ve čtečce se vysune jako médium, čtečka zůstane
    připojená. Pak je na kartě úlohy <span class="q">„Karta vysunuta“</span> a karta zmizí z kroku 1 · Karty.</li>
  <li>Vysune se <b>jen ta karta, která se kopírovala</b>. Když kartu ve čtečce vyměníte nebo vytáhnete, tlačítko zmizí.</li>
  <li>Nevysune se, dokud z karty kopíruje jiná úloha (<span class="q">„z karty ještě kopíruje jiná úloha — vysuňte ji, až její
    kopie doběhne“</span>).</li>
```

Najít (24/32):

```html

<!-- ============ 9 ============ -->
<section class="chapter" id="ch9">
<h2 class="ch"><span class="n">9</span>Nástroje → Prověřit USB porty… a okno Hardware</h2>

<p>Rychlost offloadu často nebrzdí karta ani disk, ale port, kabel nebo sdílený USB řadič. Okno Hardware ukáže, kde
je co zapojené a jak rychle, a poradí, co přepojit. Spouští se <b>jen na vyžádání</b>:</p>
<ul>
  <li>menu <b class="ui">Nástroje → Prověřit USB porty…</b> nebo <kbd>Ctrl</kbd>+<kbd>U</kbd>,</li>
  <li>tlačítko <b class="ui">Prověřit…</b> v sekci <b class="ui">Hardware</b> hlavního okna.</li>
</ul>
<p>Test zahrnuje zaškrtnuté karty a cíle. Když není vybraná žádná karta ani cíl, prověří všechny připojené disky.
Rychlá kontrola trvá sekundy a nic nezapisuje.</p>

<figure>
<div class="win" style="width:78%">
```

Nahradit:

```html

<!-- ============ 9 ============ -->
<section class="chapter" id="ch9">
<h2 class="ch"><span class="n">9</span>Hardware: USB porty a nejpomalejší článek</h2>

<p>Rychlost offloadu často nebrzdí karta ani disk, ale port, kabel nebo sdílený USB řadič. Okno Hardware ukáže, kde
je co zapojené a jak rychle, a poradí, co přepojit. Spouští se <b>jen na vyžádání</b>:</p>
<ul>
  <li>tlačítko <b class="ui">Hardware</b> v horní liště nebo <kbd>Ctrl</kbd>+<kbd>U</kbd>,</li>
  <li><b class="ui">Nastavení → Aplikace → Prověřit USB porty…</b>.</li>
</ul>
<p>Test zahrnuje zaškrtnuté karty a cíle. Když není vybraná žádná karta ani cíl, prověří všechny připojené disky.
Rychlá kontrola trvá sekundy a nic nezapisuje. Výsledek ukáže tečka u tlačítka Hardware: zelená = bez varování,
žlutá = jsou doporučení (bublina řekne kolik a kdy).</p>

<figure>
<div class="win" style="width:78%">
```

Najít (25/32):

```html
<p>Windows nevědí, kde je port na počítači fyzicky. Aplikace porty označuje např. <span class="q">„port 6“</span>, port za hubem
<span class="q">„port 7 › 2“</span>, při více řadičích s názvem řadiče (<span class="q">„Intel USB 3.20 xHCI · port 3“</span>).
Do pole vedle portu si napište vlastní název (např. <span class="q">„přední levý“</span>) — pak se použije všude: v řádcích
disků, v doporučeních i v řetězu připojení na kartě úlohy. Název se uloží.</p>

<h3>Úzké hrdlo během kopie</h3>
<p>Během kopírování a ověřování ukazuje karta úlohy řetěz připojení — pro kartu a každý cíl zařízení, připojení,
aktuální rychlost a u USB i vytížení linky:</p>
<pre class="tree">Karta … — USB 3 (5 Gb/s), port 6  310 MB/s  78 % linky
Cíl 1 … — USB 3 (10 Gb/s a víc), port 3  310 MB/s  39 % linky
Řadič Intel USB 3.20 xHCI: 620 MB/s
Úzké hrdlo: Karta …</pre>
<ul>
  <li><b>Úzké hrdlo</b> je článek, který kopii brzdí: karta (čtečka), nebo některý cíl. Pozná se podle toho, na který disk čeká
    nejvíc dat k zápisu, ne podle okamžité rychlosti — karta i disky jedou v kopii stejným tempem.</li>
  <li>Když je linka vytížená nad 85 %, je u článku <span class="q">„— linka je plná (USB 2.0 (480 Mb/s))“</span>: pomůže rychlejší port nebo kabel.</li>
  <li>Řádek <b>Řadič</b> ukazuje součet rychlostí zařízení na jednom sdíleném USB řadiči.</li>
  <li>Interní disky (SATA, NVMe) nemají „% linky“.</li>
</ul>
<p class="note">Windows nerozliší USB 10 a 20 Gb/s; obojí se zobrazuje jako „USB 3 (10 Gb/s a víc)“.</p>
</section>
```

Nahradit:

```html
<p>Windows nevědí, kde je port na počítači fyzicky. Aplikace porty označuje např. <span class="q">„port 6“</span>, port za hubem
<span class="q">„port 7 › 2“</span>, při více řadičích s názvem řadiče (<span class="q">„Intel USB 3.20 xHCI · port 3“</span>).
Do pole vedle portu si napište vlastní název (např. <span class="q">„přední levý“</span>) — pak se použije všude: v řádcích
disků, v doporučeních i v bublinách tabulky rychlostí na kartě úlohy. Název se uloží.</p>

<h3>Nejpomalejší článek během kopie</h3>
<p>Během kopírování ukazuje karta úlohy tabulku rychlostí — <b>Čtení karty</b> a každý cíl v MB/s; bublina řádku ukáže
zařízení a připojení (např. <span class="q">„Karta … — USB 3 (5 Gb/s), port 6“</span>). Pod kartou je věta:</p>
<pre class="tree">Nejpomalejší článek: čtečka karty (USB 3 (5 Gb/s)). Disky by zvládly víc.
Nejpomalejší článek: disk SHUTTLE_B (USB 2.0 (480 Mb/s) — linka je plná). Karta by šla číst rychleji.</pre>
<ul>
  <li><b>Nejpomalejší článek</b> je ten, který kopii brzdí: karta (čtečka), nebo některý cíl. Pozná se podle toho, na který
    disk čeká nejvíc dat k zápisu, ne podle okamžité rychlosti — karta i disky jedou v kopii stejným tempem.</li>
  <li>Když je linka vytížená nad 85 %, je u článku <span class="q">„— linka je plná“</span>: pomůže rychlejší port nebo kabel.</li>
  <li>Během pauzy a ověřování se věta neukazuje. Vytížení linky v procentech a součet na řadiči ukáže okno Hardware.</li>
</ul>
<p class="note">Windows nerozliší USB 10 a 20 Gb/s; obojí se zobrazuje jako „USB 3 (10 Gb/s a víc)“.</p>
</section>
```

Najít (26/32):

```html
      │  ├─ A001.xxh64                       <span class="c">← kontrolní součty (po doplnění i nové soubory)</span>
      │  ├─ A001.md5                         <span class="c">← jen s volbou xxHash64 + MD5</span>
      │  ├─ A001_clips.csv                   <span class="c">← seznam klipů (+ A001_clips.ale pro Avid)</span>
      │  ├─ offload_report_0002.json         <span class="c">← jen po doplnění: report generace 2</span>
      │  ├─ A001_report_0002.html
      │  └─ A001_verify_2026-10-07_101500Z.html  <span class="c">← jen po Ověřit zálohu (+ .pdf, offload_verify_….json)</span>
      ├─ ascmhl\
      │  ├─ 0001_A001_2026-10-06_081500Z.mhl
      │  ├─ 0002_A001_2026-10-06_143000Z.mhl <span class="c">← jen po doplnění: jen nové soubory</span>
      │  └─ ascmhl_chain.xml
      └─ _PROXY\                             <span class="c">← jen se zapnutými proxy</span>
         ├─ A001C001.mov
```

Nahradit:

```html
      │  ├─ A001.xxh64                       <span class="c">← kontrolní součty (po doplnění i nové soubory)</span>
      │  ├─ A001.md5                         <span class="c">← jen s volbou xxHash64 + MD5</span>
      │  ├─ A001_clips.csv                   <span class="c">← seznam klipů (+ A001_clips.ale pro Avid)</span>
      │  ├─ offload_report_0002.json         <span class="c">← jen po doplnění nebo opravě: report generace 2</span>
      │  ├─ A001_report_0002.html
      │  ├─ neshoda_0002\                    <span class="c">← jen po Zkopírovat znovu: odložené nesouhlasné kopie</span>
      │  │  └─ CLIP\A001C007.mxf
      │  └─ A001_verify_2026-10-07_101500Z.html  <span class="c">← jen po Ověřit zálohu (+ .pdf, offload_verify_….json)</span>
      ├─ ascmhl\
      │  ├─ 0001_A001_2026-10-06_081500Z.mhl
      │  ├─ 0002_A001_2026-10-06_143000Z.mhl <span class="c">← jen po doplnění (nové soubory) nebo opravě</span>
      │  └─ ascmhl_chain.xml
      └─ _PROXY\                             <span class="c">← jen se zapnutými proxy</span>
         ├─ A001C001.mov
```

Najít (27/32):

```html
    <code>ascmhl_chain.xml</code> a seznam <code>A001.xxh64</code>, vždy celé najednou). Mac verze generace nečte, ale
    doplněné soubory ověří ze seznamu <code>A001.xxh64</code>. Kdyby se doplnění uprostřed přerušilo tak, že reporty
    a MHL nesouhlasí, další doplnění se odmítne a složku je třeba opravit ručně; celá kopie do nové složky jde vždy.</li>
  <li>Report ověření jde jen do <code>_REPORT</code> (u cizího MHL bez ignorování <code>_REPORT</code> do
    <code>_REPORT</code> vedle složky karty), do <code>ascmhl</code> se nikdy nezapisuje.</li>
  <li><code>_REPORT</code> ve složce dne patří dennímu reportu (a reportům ověření cizích záloh); karta se tak jmenovat
    nemůže. Denní report a seznamy klipů jsou odvozené z reportů karet — přepisují se celé a lze je kdykoli vytvořit
    znovu (<b class="ui">Nástroje → Denní report…</b>).</li>
</ul>

<div class="box tip"><span class="t">Ověření kopie později</span>
Hotovou kopii můžete kdykoli znovu ověřit proti ASC MHL: <b class="ui">Nástroje → Ověřit zálohu…</b> (kapitola 8),
nebo <code>bin\offload.exe verify D:\Zaloha\2026-10-06\A001</code> (kapitola 11).</div>
</section>
```

Nahradit:

```html
    <code>ascmhl_chain.xml</code> a seznam <code>A001.xxh64</code>, vždy celé najednou). Mac verze generace nečte, ale
    doplněné soubory ověří ze seznamu <code>A001.xxh64</code>. Kdyby se doplnění uprostřed přerušilo tak, že reporty
    a MHL nesouhlasí, další doplnění se odmítne a složku je třeba opravit ručně; celá kopie do nové složky jde vždy.</li>
  <li><b class="ui">Zkopírovat znovu</b> (oprava) zapíše generaci stejně jako doplnění: znovu zkopírované soubory
    a dobré kopie, které jen přečetla, jsou v nové generaci MHL a v reportu <code>_0002</code>. Nesouhlasnou kopii před
    tím přesune do <code>_REPORT\neshoda_0002\</code> se stejnou cestou jako na kartě — nic se nemaže. Složka
    <code>_REPORT</code> do MHL nepatří, takže odložená kopie ověření nevadí.</li>
  <li>Report ověření jde jen do <code>_REPORT</code> (u cizího MHL bez ignorování <code>_REPORT</code> do
    <code>_REPORT</code> vedle složky karty), do <code>ascmhl</code> se nikdy nezapisuje.</li>
  <li><code>_REPORT</code> ve složce dne patří dennímu reportu (a reportům ověření cizích záloh); karta se tak jmenovat
    nemůže. Denní report a seznamy klipů jsou odvozené z reportů karet — přepisují se celé a lze je kdykoli vytvořit
    znovu (<b class="ui">Denní report…</b> pod frontou).</li>
</ul>

<div class="box tip"><span class="t">Ověření kopie později</span>
Hotovou kopii můžete kdykoli znovu ověřit proti ASC MHL: <b class="ui">Ověřit zálohu</b> v horní liště (kapitola 8),
nebo <code>bin\offload.exe verify D:\Zaloha\2026-10-06\A001</code> (kapitola 11).</div>
</section>
```

Najít (28/32):

```html
<h3>Karty a cíle</h3>
<table>
  <thead><tr><th class="w35">Problém</th><th>Řešení</th></tr></thead>
  <tr><td>Karta se neobjeví v seznamu Karty</td><td>Klikněte na <b>⟳</b> (Znovu načíst připojené disky). Čtečky CFexpress přes Thunderbolt se ve Windows hlásí
      jako NVMe disk, takže je aplikace sama nenabídne — kartu přidejte tlačítkem <b class="ui">Složka…</b> (vyberte kořen karty).
      Disk, na kterém je některý cíl, se jako karta nenabízí.</td></tr>
  <tr><td>Disk má „nevypadá jako karta“ a není zaškrtnutý</td><td>Na disku nejsou typické složky ani klipy kamery. Pokud ho opravdu chcete kopírovat, zaškrtněte ho ručně.</td></tr>
  <tr><td><span class="txt-bad">Disk není připojen</span> u cíle, Spustit offload je šedé</td><td>Připojte disk; do pár sekund se řádek opraví sám (nebo klikněte na ⟳). Cíl, který nepotřebujete, odeberte křížkem.</td></tr>
```

Nahradit:

```html
<h3>Karty a cíle</h3>
<table>
  <thead><tr><th class="w35">Problém</th><th>Řešení</th></tr></thead>
  <tr><td>Karta se neobjeví v kroku 1 · Karty</td><td>Klikněte na <b>⟳</b> (Znovu načíst připojené disky). Čtečky CFexpress přes Thunderbolt se ve Windows hlásí
      jako NVMe disk, takže je aplikace sama nenabídne — kartu přidejte tlačítkem <b class="ui">Ze složky…</b> (vyberte kořen karty).
      Disk, na kterém je některý cíl, se jako karta nenabízí.</td></tr>
  <tr><td>Disk má „nevypadá jako karta“ a není zaškrtnutý</td><td>Na disku nejsou typické složky ani klipy kamery. Pokud ho opravdu chcete kopírovat, zaškrtněte ho ručně.</td></tr>
  <tr><td><span class="txt-bad">Disk není připojen</span> u cíle, Spustit offload je šedé</td><td>Připojte disk; do pár sekund se řádek opraví sám (nebo klikněte na ⟳). Cíl, který nepotřebujete, odeberte křížkem.</td></tr>
```

Najít (29/32):

```html
  <tr><td>„Některé názvy Windows neumí uložit: …“</td><td>Na kartě jsou názvy, které Windows neuloží (zakázané znaky, jména jako <code>CON</code>, tečka nebo mezera na konci, nebo dva názvy lišící se jen velikostí písmen). Hláška vypíše, kterých souborů se to týká.</td></tr>
  <tr><td>„Dřívější záloha není dostupná: … — připojte disk, nebo udělejte celou kopii.“</td><td>Doplnění potřebuje všechny disky dřívější zálohy. Připojte je, nebo kartu zkopírujte celou (vznikne nová složka).</td></tr>
  <tr><td>„V … chybí report dřívější zálohy …“, „V … chybí ASC MHL …“, „Report dřívější zálohy v … nejde přečíst …“</td><td>Složka zálohy není úplná (kopie z jiného nástroje bez MHL, smazaný nebo poškozený report). Udělejte celou kopii.</td></tr>
  <tr><td>„Zálohy na cílech se liší (…) — zkontrolujte je přes Nástroje → Ověřit zálohu…; doplnit nejde.“</td><td>Zálohy na discích nejsou stejné (jedna byla doplněna bez druhé, kopie se přerušila). Ověřte obě zálohy; pak udělejte celou kopii.</td></tr>
  <tr><td>„V … nesouhlasí reporty s ASC MHL (poslední report je generace …, poslední generace MHL …) — dřívější doplnění se nedokončilo. …“</td><td>Dřívější doplnění se přerušilo uprostřed zápisu reportů. Zálohu ověřte a složku opravte ručně (nebo udělejte celou kopii do nové složky).</td></tr>
  <tr><td>„Historie ASC MHL v … je poškozená — … doplnit nejde.“</td><td>Soubor MHL někdo změnil nebo ve složce <code>ascmhl</code> přibyl soubor mimo řetěz. Ověřte zálohu; doplnění jde až po opravě, celá kopie vždy.</td></tr>
  <tr><td>„Na kartě není žádný soubor z dřívější zálohy — jde o jinou kartu? Udělejte celou kopii.“</td><td>Doplňujete jinou kartu, než která se zálohovala (nebo kopie, která se zrušila ještě před prvním souborem). Udělejte celou kopii.</td></tr>
  <tr><td>„Doplnění zastaveno — nic se nezkopírovalo. N souborů se neshoduje se zálohou: …“</td><td>Na kartě se změnily už zálohované soubory („jiná velikost nebo datum“, jiná karta se stejným sériovým číslem, úprava v kameře) nebo na cíli je soubor, který žádný report nezná (nedokončené doplnění). Udělejte celou kopii.</td></tr>
  <tr><td>„Na … chybí soubor z dřívější zálohy: …“, „… se při dřívější záloze na … nezkopíroval správně …“</td><td>Dřívější záloha je neúplná. Ověřte ji (Nástroje → Ověřit zálohu…) a kartu zkopírujte celou.</td></tr>
  <tr><td>„Do této zálohy právě doplňuje jiná úloha (…) — počkejte, až skončí.“</td><td>Stejnou složku karty doplňuje jiná úloha (i z druhého okna Offloadu nebo z <code>offload append</code>). Počkejte na její konec; pak spusťte doplnění znovu — doplní se jen to, co mezitím nepřibylo.</td></tr>
  <tr><td>„Nic nového — záloha je úplná.“</td><td>Není chyba — všechny soubory karty už v záloze jsou.</td></tr>
  <tr><td>„Ve složce … není ASC MHL ani seznam součtů — není s čím porovnat.“</td><td>Vybraná složka není složka karty ani dne z Offloadu (nebo v ní chybí <code>ascmhl</code> i <code>_REPORT</code>). Vyberte složku karty <code>&lt;cíl&gt;\&lt;datum&gt;\&lt;karta&gt;</code>.</td></tr>
  <tr><td><span class="txt-warn">„ASC MHL generace 0002 neodpovídá řetězu (soubor MHL byl změněn).“</span> (ověření)</td><td>Soubor MHL po zápisu někdo změnil. Soubory se ověřily i tak; zálohu zkontrolujte, případně udělejte novou kopii.</td></tr>
  <tr><td>„N souborů selhalo — karta NENÍ bezpečně zálohovaná.“</td><td>Otevřete <b class="ui">Report</b>, v části Chyby je u každého souboru důvod (karta nebo konkrétní cíl). Zkontrolujte kartu, čtečku a disky a kartu offloadujte znovu.</td></tr>
  <tr><td>„Report nenalezen na žádném cíli: …“</td><td>Žádný cíl s reportem není připojený. Připojte cílový disk.</td></tr>
  <tr><td>„Cíl N: svazek nepodporuje přímý zápis, použita systémová cache.“ (v reportu)</td><td>Typicky síťový disk, který neumí zápis mimo systémovou cache. Offload na něj zapsal běžným způsobem a v reportu to uvádí.</td></tr>
</table>
```

Nahradit:

```html
  <tr><td>„Některé názvy Windows neumí uložit: …“</td><td>Na kartě jsou názvy, které Windows neuloží (zakázané znaky, jména jako <code>CON</code>, tečka nebo mezera na konci, nebo dva názvy lišící se jen velikostí písmen). Hláška vypíše, kterých souborů se to týká.</td></tr>
  <tr><td>„Dřívější záloha není dostupná: … — připojte disk, nebo udělejte celou kopii.“</td><td>Doplnění potřebuje všechny disky dřívější zálohy. Připojte je, nebo kartu zkopírujte celou (vznikne nová složka).</td></tr>
  <tr><td>„V … chybí report dřívější zálohy …“, „V … chybí ASC MHL …“, „Report dřívější zálohy v … nejde přečíst …“</td><td>Složka zálohy není úplná (kopie z jiného nástroje bez MHL, smazaný nebo poškozený report). Udělejte celou kopii.</td></tr>
  <tr><td>„Zálohy na cílech se liší (…) — zkontrolujte je přes Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify); doplnit nejde.“</td><td>Zálohy na discích nejsou stejné (jedna byla doplněna bez druhé, kopie se přerušila). Ověřte obě zálohy; pak udělejte celou kopii.</td></tr>
  <tr><td>„V … nesouhlasí reporty s ASC MHL (poslední report je generace …, poslední generace MHL …) — dřívější doplnění se nedokončilo. …“</td><td>Dřívější doplnění se přerušilo uprostřed zápisu reportů. Zálohu ověřte a složku opravte ručně (nebo udělejte celou kopii do nové složky).</td></tr>
  <tr><td>„Historie ASC MHL v … je poškozená — … doplnit nejde.“</td><td>Soubor MHL někdo změnil nebo ve složce <code>ascmhl</code> přibyl soubor mimo řetěz. Ověřte zálohu; doplnění jde až po opravě, celá kopie vždy.</td></tr>
  <tr><td>„Na kartě není žádný soubor z dřívější zálohy — jde o jinou kartu? Udělejte celou kopii.“</td><td>Doplňujete jinou kartu, než která se zálohovala (nebo kopie, která se zrušila ještě před prvním souborem). Udělejte celou kopii.</td></tr>
  <tr><td>„Doplnění zastaveno — nic se nezkopírovalo. N souborů se neshoduje se zálohou: …“</td><td>Na kartě se změnily už zálohované soubory („jiná velikost nebo datum“, jiná karta se stejným sériovým číslem, úprava v kameře) nebo na cíli je soubor, který žádný report nezná (nedokončené doplnění). Udělejte celou kopii.</td></tr>
  <tr><td>„Na … chybí soubor z dřívější zálohy: …“, „… se při dřívější záloze na … nezkopíroval správně …“</td><td>Dřívější záloha je neúplná. Ověřte ji (Ověřit zálohu v horní liště) a kartu zkopírujte celou; u souborů, které při kopii selhaly, pomůže na kartě úlohy <b class="ui">Zkopírovat znovu</b>.</td></tr>
  <tr><td>„Oprava zastavena — nic se nezkopírovalo. N souborů nejde opravit: …“</td><td><b class="ui">Zkopírovat znovu</b> nenašlo soubor na kartě („na kartě už není“), soubor má jinou velikost nebo datum než při kopii, ve složce zálohy chybí dobrá kopie, nebo je místo pro odložení nesouhlasné kopie (<code>_REPORT\neshoda_…</code>) obsazené. Zkontrolujte, že je ve čtečce stejná karta, ověřte zálohu, nebo kartu zkopírujte celou.</td></tr>
  <tr><td>„Nic k opravě — všechny soubory jsou na všech cílech v pořádku.“</td><td>Není chyba — mezitím už oprava proběhla (třeba z druhého okna), nebo reporty na discích chyby nemají.</td></tr>
  <tr><td>Soubor zůstal po opravě žlutě s důvodem „karta“</td><td>Karta se při čtení liší od toho, co se zkopírovalo poprvé (vadná karta nebo čtečka). Nová kopie se nepovažuje za dobrou; zkuste jinou čtečku a opravu znovu.</td></tr>
  <tr><td>„Do této zálohy právě doplňuje jiná úloha (…) — počkejte, až skončí.“</td><td>Stejnou složku karty doplňuje jiná úloha (i z druhého okna Offloadu nebo z <code>offload append</code>). Počkejte na její konec; pak spusťte doplnění znovu — doplní se jen to, co mezitím nepřibylo.</td></tr>
  <tr><td>„Nic nového — záloha je úplná.“</td><td>Není chyba — všechny soubory karty už v záloze jsou.</td></tr>
  <tr><td>„Ve složce … není ASC MHL ani seznam součtů — není s čím porovnat.“</td><td>Vybraná složka není složka karty ani dne z Offloadu (nebo v ní chybí <code>ascmhl</code> i <code>_REPORT</code>). Vyberte složku karty <code>&lt;cíl&gt;\&lt;datum&gt;\&lt;karta&gt;</code>.</td></tr>
  <tr><td><span class="txt-warn">„ASC MHL generace 0002 neodpovídá řetězu (soubor MHL byl změněn).“</span> (ověření)</td><td>Soubor MHL po zápisu někdo změnil. Soubory se ověřily i tak; zálohu zkontrolujte, případně udělejte novou kopii.</td></tr>
  <tr><td>Žlutá karta „— kartu zatím nevysunujte“</td><td>Na kartě úlohy je seznam souborů a důvod pro každý disk; podrobně v <b class="ui">Report PDF</b> v části Chyby. Zkontrolujte kartu, čtečku a disky a stiskněte <b class="ui">Zkopírovat znovu</b> (kapitola 8), nebo kartu offloadujte znovu celou.</td></tr>
  <tr><td>„Report nenalezen na žádném cíli: …“</td><td>Žádný cíl s reportem není připojený. Připojte cílový disk.</td></tr>
  <tr><td>„Cíl N: svazek nepodporuje přímý zápis, použita systémová cache.“ (v reportu)</td><td>Typicky síťový disk, který neumí zápis mimo systémovou cache. Offload na něj zapsal běžným způsobem a v reportu to uvádí.</td></tr>
</table>
```

Najít (30/32):

```html
  <thead><tr><th class="w35">Problém</th><th>Řešení</th></tr></thead>
  <tr><td><span class="txt-bad">LUT nenalezen: … — vyberte jiný nebo ho odeberte.</span></td><td>Vlastní <code>.cube</code> není na svém místě (disk odpojený, soubor přesunutý). Připojte disk, vyberte soubor znovu, nebo zvolte jiný LUT.</td></tr>
  <tr><td><span class="txt-bad">Složka pro proxy není dostupná: …</span></td><td>Připojte disk se složkou, nebo volbu zrušte křížkem u <b class="ui">Kopírovat proxy i do složky</b>.</td></tr>
  <tr><td><span class="txt-warn">ffmpeg/ffprobe nenalezen — náhledy a proxy se přeskočí, kopie a ověření proběhnou.</span></td><td>Chybí přibalený ffmpeg nebo ffprobe ve složce <code>bin</code>. Rozbalte ZIP znovu celý, nebo v sekci <b class="ui">Dekodéry</b> vyberte cestu k nástroji.</td></tr>
  <tr><td>Proxy se renderují pomalu</td><td>Bez grafické karty NVIDIA (Intel a AMD se zkoušejí, ale nejsou ověřené) se H.264/H.265 kódují procesorem. ProRes se kóduje procesorem vždy. Pomůže nižší <b class="ui">Výška</b> (např. 720p). Kopie další karty mezitím běží.</td></tr>
  <tr><td>Poznámka „ffmpeg se zasekl (60 s bez nového snímku) …; proxy nevznikla“</td><td>ffmpeg minutu nevydal žádný snímek; Offload ho ukončil a jednou to zkusil bez hardwarového dekódování. Úloha jede dál, ostatní klipy se zpracují. Klip zkontrolujte (může být poškozený).</td></tr>
  <tr><td>„Blackmagic RAW: chybí braw-decode nebo knihovna Blackmagic RAW …“</td><td>Nainstalujte zdarma Blackmagic RAW Player (nebo DaVinci Resolve). Pozor: řádek <b>braw-decode</b> v sekci Dekodéry je zelený i bez knihovny — stav BRAW poznáte z poznámky u klipu v reportu.</td></tr>
  <tr><td>„Blackmagic RAW: klip nelze otevřít — je poškozený, nebo je knihovna … starší než kamera …“</td><td>Nainstalujte aktuální Blackmagic RAW Player. Pokud to nepomůže, klip může být poškozený.</td></tr>
  <tr><td>„Blackmagic RAW: knihovnu … nelze použít …“</td><td>Nainstalujte aktuální Blackmagic RAW Player.</td></tr>
  <tr><td>„RED R3D: nainstalujte REDCINE-X PRO (REDline)“ / „ARRIRAW: nainstalujte ARRI Reference Tool (art-cmd)“</td><td>Nainstalujte nástroj výrobce, nebo v sekci Dekodéry vyberte cestu k němu. Bez něj se klipy jen zkopírují a ověří.</td></tr>
  <tr><td>„kodek nepodporován ffmpegem (např. Sony X-OCN) …“, „Canon Cinema RAW Light: … proxy přeskočeno“</td><td>Tyto formáty Offload jen zkopíruje a ověří; proxy pro ně nejde vyrobit.</td></tr>
  <tr><td>Klip z ARRI nahraný v Rec.709 dostal LUT</td><td>Automatika u ARRI předpokládá log záznam. Pro takové klipy zvolte <b class="ui">Bez LUT</b>.</td></tr>
  <tr><td>Klip dostal „bez LUT (kamera nerozpoznána)“</td><td>Canon, Panasonic, DJI a Fujifilm se zatím automaticky nepoznají. Nastavte záložní LUT v <b class="ui">Když kameru nepoznám:</b>, nebo vyberte LUT ručně.</td></tr>
```

Nahradit:

```html
  <thead><tr><th class="w35">Problém</th><th>Řešení</th></tr></thead>
  <tr><td><span class="txt-bad">LUT nenalezen: … — vyberte jiný nebo ho odeberte.</span></td><td>Vlastní <code>.cube</code> není na svém místě (disk odpojený, soubor přesunutý). Připojte disk, vyberte soubor znovu, nebo zvolte jiný LUT.</td></tr>
  <tr><td><span class="txt-bad">Složka pro proxy není dostupná: …</span></td><td>Připojte disk se složkou, nebo volbu zrušte křížkem u <b class="ui">Kopírovat proxy i do složky</b>.</td></tr>
  <tr><td><span class="txt-warn">ffmpeg/ffprobe nenalezen — náhledy a proxy se přeskočí, kopie a ověření proběhnou.</span></td><td>Chybí přibalený ffmpeg nebo ffprobe ve složce <code>bin</code>. Rozbalte ZIP znovu celý, nebo v <b class="ui">Nastavení → Dekodéry RAW</b> vyberte cestu k nástroji.</td></tr>
  <tr><td>Proxy se renderují pomalu</td><td>Bez grafické karty NVIDIA (Intel a AMD se zkoušejí, ale nejsou ověřené) se H.264/H.265 kódují procesorem. ProRes se kóduje procesorem vždy. Pomůže nižší <b class="ui">Výška</b> (např. 720p). Kopie další karty mezitím běží.</td></tr>
  <tr><td>Poznámka „ffmpeg se zasekl (60 s bez nového snímku) …; proxy nevznikla“</td><td>ffmpeg minutu nevydal žádný snímek; Offload ho ukončil a jednou to zkusil bez hardwarového dekódování. Úloha jede dál, ostatní klipy se zpracují. Klip zkontrolujte (může být poškozený).</td></tr>
  <tr><td>„Blackmagic RAW: chybí braw-decode nebo knihovna Blackmagic RAW …“</td><td>Nainstalujte zdarma Blackmagic RAW Player (nebo DaVinci Resolve). Pozor: řádek <b>braw-decode</b> v Nastavení → Dekodéry RAW je zelený i bez knihovny — stav BRAW poznáte z poznámky u klipu v reportu.</td></tr>
  <tr><td>„Blackmagic RAW: klip nelze otevřít — je poškozený, nebo je knihovna … starší než kamera …“</td><td>Nainstalujte aktuální Blackmagic RAW Player. Pokud to nepomůže, klip může být poškozený.</td></tr>
  <tr><td>„Blackmagic RAW: knihovnu … nelze použít …“</td><td>Nainstalujte aktuální Blackmagic RAW Player.</td></tr>
  <tr><td>„RED R3D: nainstalujte REDCINE-X PRO (REDline)“ / „ARRIRAW: nainstalujte ARRI Reference Tool (art-cmd)“</td><td>Nainstalujte nástroj výrobce, nebo v Nastavení → Dekodéry RAW vyberte cestu k němu. Bez něj se klipy jen zkopírují a ověří.</td></tr>
  <tr><td>„kodek nepodporován ffmpegem (např. Sony X-OCN) …“, „Canon Cinema RAW Light: … proxy přeskočeno“</td><td>Tyto formáty Offload jen zkopíruje a ověří; proxy pro ně nejde vyrobit.</td></tr>
  <tr><td>Klip z ARRI nahraný v Rec.709 dostal LUT</td><td>Automatika u ARRI předpokládá log záznam. Pro takové klipy zvolte <b class="ui">Bez LUT</b>.</td></tr>
  <tr><td>Klip dostal „bez LUT (kamera nerozpoznána)“</td><td>Canon, Panasonic, DJI a Fujifilm se zatím automaticky nepoznají. Nastavte záložní LUT v <b class="ui">Když kameru nepoznám:</b>, nebo vyberte LUT ručně.</td></tr>
```

Najít (31/32):

```html
  <tr><td>Tlačítko Vysunout kartu chybí</td><td>Karta byla vytažena nebo vyměněna, nebo jde o složku či interní disk, které se nevysouvají.</td></tr>
  <tr><td><span class="txt-warn">Probíhá měření rychlosti — počkejte nebo ho zastavte</span></td><td>V okně Hardware běží měření. Počkejte nebo klikněte na <b class="ui">Zastavit</b>.</td></tr>
  <tr><td>Po zavření aplikace během kopie</td><td>Rozpracované soubory se smažou. Karta, jejíž kopie se přerušila, <b>není zálohovaná</b> — offloadujte ji znovu celou.</td></tr>
</table>

<h3>Časté otázky</h3>
```

Nahradit:

```html
  <tr><td>Tlačítko Vysunout kartu chybí</td><td>Karta byla vytažena nebo vyměněna, nebo jde o složku či interní disk, které se nevysouvají.</td></tr>
  <tr><td><span class="txt-warn">Probíhá měření rychlosti — počkejte nebo ho zastavte</span></td><td>V okně Hardware běží měření. Počkejte nebo klikněte na <b class="ui">Zastavit</b>.</td></tr>
  <tr><td>Po zavření aplikace během kopie</td><td>Rozpracované soubory se smažou. Karta, jejíž kopie se přerušila, <b>není zálohovaná</b> — offloadujte ji znovu celou.</td></tr>
  <tr><td>Pozastavená úloha se nehýbe ani po <b class="ui">Pokračovat</b> déle než pár sekund</td><td>Během analýzy klipů a proxy úloha nejdřív dokončí rozpracovaný klip; v kopii čeká na disky. Když disk mezitím zmizel, soubory na něm selžou a karta zežloutne — připojte disk a použijte <b class="ui">Zkopírovat znovu</b>.</td></tr>
</table>

<h3>Časté otázky</h3>
```

Najít (32/32):

```html
<h4>Musím mít dva cíle?</h4>
<p>Ne, ale aplikace s jedním cílem upozorní, že doporučené jsou dvě nezávislé kopie.</p>
<h4>Jak ověřím zálohu později, třeba po převozu disku?</h4>
<p><b class="ui">Nástroje → Ověřit zálohu…</b>, nebo <code>bin\offload.exe verify &lt;cíl&gt;\&lt;datum&gt;\&lt;karta&gt;</code>. Nástroj <code>ascmhl verify</code> na Windows hlásí falešné
„new/missing file“ kvůli lomítkům v cestách — plné <code>ascmhl verify</code> pouštějte na macOS nebo Linuxu, na Windows použijte <code>offload verify</code>.</p>
<h4>Ověří kopii z Windows i Mac verze Offloadu?</h4>
<p>Ano, obě verze zapisují stejný formát (seznam součtů, report, MHL).</p>
```

Nahradit:

```html
<h4>Musím mít dva cíle?</h4>
<p>Ne, ale aplikace s jedním cílem upozorní, že doporučené jsou dvě nezávislé kopie.</p>
<h4>Jak ověřím zálohu později, třeba po převozu disku?</h4>
<p><b class="ui">Ověřit zálohu</b> v horní liště, nebo <code>bin\offload.exe verify &lt;cíl&gt;\&lt;datum&gt;\&lt;karta&gt;</code>. Nástroj <code>ascmhl verify</code> na Windows hlásí falešné
„new/missing file“ kvůli lomítkům v cestách — plné <code>ascmhl verify</code> pouštějte na macOS nebo Linuxu, na Windows použijte <code>offload verify</code>.</p>
<h4>Ověří kopii z Windows i Mac verze Offloadu?</h4>
<p>Ano, obě verze zapisují stejný formát (seznam součtů, report, MHL).</p>
```

Kontrola, že v návodu nezůstal odkaz na zrušené menu ani starou podobu okna:

```powershell
Select-String -Path windows\package\navod\navod.html -Pattern 'Nástroje →|sekci Proxy|sekci Dekodéry|Struktura a kontrola|Renderovat proxy po ověření|Úzké hrdlo|levém panelu|Složka…'
```

Expected: žádný řádek.

- [ ] **Step 6: Vytisknout návod do PDF**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows\package\navod\render.ps1
```

Expected: `printed …\windows\package\Offload-navod.pdf (… KB)` (Edge bez okna, ne Offload). Otevřete PDF a projděte
kapitolu 3: schéma okna (horní lišta, kroky 1–3, lišta se spuštěním, fronta s běžící, zelenou a žlutou kartou, DNES)
je na jedné stránce a callouty 1–9 sedí s legendou.

- [ ] **Step 7: Build a celá sada**

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 upozornění, 0 chyb`; celá sada `Neúspěšné: 0` (časově citlivé `ProxyWatchdogTests` / `ProxyRendererTests.Feeder_*` při jednom selhání pusťte znovu samostatně — viz Global Constraints).

Expected navíc: celkem 1383 testů = 1375 úspěšných + 8 přeskočených (start plánu 1257 = 1249 + 8).

- [ ] **Step 8: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Docs: plan 9 (new look), version 0.5.0

Manual with the new main window (diagram and legend), pause and "Zkopírovat znovu", no Nástroje
menu; README state, GUI and manual tests 40-47; FORMATY repair and pause keys; ROZDILY; what the
Mac takes over after approval; version 0.5.0.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\FORMATY.md docs\ROZDILY.md docs\VYVOJ-MAC.md docs\VYVOJ-WINDOWS.md windows\Directory.Build.props windows\README.md windows\package\navod\navod.html windows\tests\Offload.Core.Tests\FormatTests.cs windows\tests\Offload.Core.Tests\HtmlReportTests.cs windows\tests\Offload.Core.Tests\ReportTests.cs windows\package\Offload-navod.pdf
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 19: Vizuální kontrola proti docs/ui-navrh/screeny (postup 7)

Jediný úkol, který spouští `Offload.exe`. Okno se pustí v ukázkovém režimu (úkol 17) s izolovaným nastavením, snímky se
udělají přes `PrintWindow` (`windows\scripts\app-drive.ps1`, funguje i pod jinými okny) a porovnají se s
`docs/ui-navrh/screeny`. Úkol nic necommituje kromě případných oprav (každá oprava = vlastní malý commit s řádkem do
VYVOJ); snímky jsou mimo repo.

**Files:**
- Read: `docs/ui-navrh/screeny/01-pripraveno.png`, `01-pripraveno-uzke.png`, `02-probiha-offload.png`, `03-hotovo-a-chyba.png`, `04-nastaveni-proxy.png`
- Use: `windows/scripts/app-drive.ps1` (`Start-Offload`, `Resize-Offload`, `Save-OffloadScreenshot`, `Find-OffloadElement`, `Invoke-OffloadButton`, `Stop-Offload`)
- Output (mimo repo): `%TEMP%\offload-ui9\shots\*.png`

**Interfaces:**
- Consumes: `Offload.exe --demo pripraveno|probiha|hotovo|nastaveni` (úkol 17), `OFFLOAD_SETTINGS_DIR` (izolované nastavení a historie), `OFFLOAD_THEME=dark|light` (úkol 10), AutomationId z úkolů 11–16.
- Produces: snímky a seznam rozdílů proti návrhu (do zprávy kontroleru; opravy jako samostatné commity).

- [ ] **Step 1: Souhlas uživatele (dělá kontroler)**

Kontroler se zeptá uživatele: „Můžu teď na pár minut spouštět Offload.exe (ukázkový režim, nic nekopíruje) a dělat
snímky okna? Okna budou chvíli vyskakovat.“ Bez výslovného „ano“ úkol nepokračuje. Nespouštět, když uživatel u počítače
pracuje a neodsouhlasil to.

- [ ] **Step 2: Sestavit Debug**

```powershell
dotnet build windows\Offload.sln -c Debug
```

Expected: `0 chyb`; existuje `windows\src\Offload.App\bin\Debug\net10.0-windows\Offload.exe`.

- [ ] **Step 3: Izolované nastavení se dvěma cíli a proxy**

```powershell
$root = Join-Path $env:TEMP 'offload-ui9'
Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$root\settings", "$root\RAID_A", "$root\SHUTTLE_B", "$root\shots" | Out-Null
$json = @{ destRoots = @("$root\RAID_A", "$root\SHUTTLE_B"); proxy = @{ enabled = $true } } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText("$root\settings\settings.json", $json, (New-Object Text.UTF8Encoding $false))
$env:OFFLOAD_SETTINGS_DIR = "$root\settings"
```

Expected: `settings.json` se dvěma cíli. Skutečné `%APPDATA%\Offload` se nepoužije (historie, nastavení ani log).
Cíle jsou složky na systémovém disku, proto dlaždice ukážou jeho volné místo a „NVMe“ / „SATA“ — v mockupu jsou
„7,20 TB volných z 8 TB · NTFS · NVMe“; to je jediný očekávaný rozdíl v textu dlaždic.

- [ ] **Step 4: Snímky čtyř stavů v tmavém i světlém tématu**

```powershell
Set-Location windows
. .\scripts\app-drive.ps1
$exe = '.\src\Offload.App\bin\Debug\net10.0-windows\Offload.exe'
$shots = Join-Path $env:TEMP 'offload-ui9\shots'
foreach ($theme in 'dark', 'light') {
    $env:OFFLOAD_THEME = $theme
    foreach ($state in 'pripraveno', 'probiha', 'hotovo', 'nastaveni') {
        $p = Start-Offload -Exe $exe -Arguments "--demo $state"
        Resize-Offload $p 1360 900
        Start-Sleep -Seconds 2
        Save-OffloadScreenshot $p "$shots\$theme-$state.png"
        if ($state -eq 'pripraveno') {
            Resize-Offload $p 1000 900; Save-OffloadScreenshot $p "$shots\$theme-pripraveno-uzke.png"
            Resize-Offload $p 940 640;  Save-OffloadScreenshot $p "$shots\$theme-pripraveno-min.png"
            $tab = Find-OffloadElement $p 'HistoryTab'
            $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Resize-Offload $p 1360 900; Save-OffloadScreenshot $p "$shots\$theme-historie.png"
        }
        if ($state -eq 'probiha') {
            Invoke-OffloadButton $p 'PauseButton'; Start-Sleep -Seconds 1
            Save-OffloadScreenshot $p "$shots\$theme-pozastaveno.png"
            Invoke-OffloadButton $p 'ResumeButton'; Start-Sleep -Seconds 1
            Invoke-OffloadButton $p 'CancelButton'; Start-Sleep -Seconds 1
            Save-OffloadScreenshot $p "$shots\$theme-zruseno.png"
        }
        Stop-Offload $p
    }
}
Remove-Item Env:\OFFLOAD_THEME
Get-ChildItem $shots | Select-Object Name, Length
```

Expected: 18 snímků (`dark-*.png`, `light-*.png`: pripraveno, pripraveno-uzke, pripraveno-min, historie, probiha,
pozastaveno, zruseno, hotovo, nastaveni — každý přes 50 KB). Žádný dialog „Offload se nepodařilo spustit“; po
`Stop-Offload` žádný běžící `Offload.exe` (`Get-Process Offload -ErrorAction SilentlyContinue` nic nevrátí).

- [ ] **Step 5: Stavy mimo ukázku — bez karty, jeden cíl, žádný cíl**

```powershell
foreach ($case in @(@{ n = 'jeden-cil'; d = @("$root\RAID_A") }, @{ n = 'bez-cilu'; d = @() })) {
    $json = @{ destRoots = $case.d } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText("$root\settings\settings.json", $json, (New-Object Text.UTF8Encoding $false))
    $env:OFFLOAD_THEME = 'dark'
    $p = Start-Offload -Exe $exe
    Resize-Offload $p 1360 900; Start-Sleep -Seconds 3
    Save-OffloadScreenshot $p "$shots\dark-$($case.n).png"
    Stop-Offload $p
}
Remove-Item Env:\OFFLOAD_THEME
```

Expected: „Vložte kartu do čtečky“ v přerušovaném rámečku (pokud je v PC vložená skutečná karta nebo USB disk,
objeví se jako dlaždice — to je v pořádku, nic se nekopíruje), s jedním cílem žluté „Jen jeden cíl — doporučeny jsou
dvě nezávislé kopie.“, bez cíle „Přidejte dva disky — každá karta se zapíše na oba zároveň.“, Spustit offload neaktivní.

- [ ] **Step 6: Porovnat se snímky návrhu**

Otevřete vedle sebe snímek a návrh (nástrojem Read obrázky, nebo v prohlížeči) a projděte:

| Snímek | Návrh | Co musí sedět |
|---|---|---|
| `dark-pripraveno.png` | `01-pripraveno.png` | lišta: logo, „Projekt Bez projektu ▾“, Ověřit zálohu, Hardware, ikona s posuvníky; kroky „1 · KARTY“ (A035_ARRI zaškrtnutá, „ALEXA 35 · LogC4“, „24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s“, Název složky v monospace; B012_FX6 se žlutým Doplnit), „2 · CÍLE“ (dvě dlaždice s kolečkem a pruhem, „+ Přidat disk“, náhled cesty `…\<dnes>\A035_ARRI\`), „3 · PO ZKOPÍROVÁNÍ“ (4 řádky se zeleným ✓, „1080p · H.264 · LogC4 → 709“, „PDF · HTML · ASC MHL · CSV“, „Upravit projekt“); spodní lišta „1 karta → 2 disky · 1,70 GB“, modré „▶ Spustit offload  Ctrl ↵“; vpravo karta „Připraveno — A035_ARRI na 2 disky“ s ikonami toku a DNES „3 karty · 412 GB · vše ověřeno“ |
| `dark-pripraveno-uzke.png` | `01-pripraveno-uzke.png` | nic se neořízne ani nepřekrývá; levý sloupec 360–480 px, fronta se zúží |
| `dark-pripraveno-min.png` | — (940 × 640) | okno jde zmenšit na minimum, spodní lišta se Spustit offload je vidět, obsah se posouvá |
| `dark-probiha.png` | `02-probiha-offload.png` | pilulka „● Běží 1 offload · 1 ve frontě“; kompaktní kroky 2 a 3; běžící karta A035_ARRI „38 %“ a rostoucí, ukazatel fází (1. segment modrý), tabulka „Čtení karty / 1 RAID_A / 2 SHUTTLE_B“ ~661 MB/s v monospace, „Soubor 10 / 24“, Pozastavit a červeně orámované Zrušit, „Nejpomalejší článek: …“; pod ní kompaktní „B012_FX6 — doplnění · 2 nové klipy · 14,2 GB · stejné cíle · Čeká ve frontě“ |
| `dark-pozastaveno.png` | — | „Pozastaveno“, „Pozastaveno — Pokračovat spustí úlohu od místa, kde stojí.“, rychlosti „—“, místo Pozastavit „Pokračovat“, pilulka „Pozastaveno: 1 offload · 1 ve frontě“ |
| `dark-zruseno.png` | — | šedá karta „A035_ARRI — zrušeno“ s „Karta není kompletně zálohovaná — zkopírujte ji znovu celou.“ |
| `dark-hotovo.png` | `03-hotovo-a-chyba.png` | zelená karta „A035_ARRI — kartu lze vysunout“, „Všech 24 souborů je na 2 discích a ověřených (xxHash64).“, řádek Kopie 602 MB/s · Ověření 799 MB/s · Proxy 24 / 24 · Celkem 6 min 12 s, pět šedých náhledů + „+19“, Report PDF, Otevřít složku, Proxy, cesta; žlutá karta „B012_FX6 — kartu zatím nevysunujte“ s větou o SHUTTLE_B, řádek souboru „XDROOT\Clip\B012C015_261007_9QW1.MXF · RAID_A ✓ · SHUTTLE_B ✕ hash“ a „Zkopírovat znovu“ (v ukázce bez karty ve čtečce tlačítko chybí — tak to má být, `RepairRules.Offer`) |
| `dark-nastaveni.png` | `04-nastaveni-proxy.png` | lišta „‹ Zpět · Projekt Bez projektu · Uložit jako nový projekt… · Hotovo“, navigace sekcí (Proxy zvýrazněná), vypínač Proxy, segmenty Kodek (H.264 vybraný) a Výška (1080p), Datový tok s „≈ 75 MB / minutu“, Výřez, LUT + tabulka „LUT PODLE KAMERY“ s „použito“, vpravo šedý rám 16:9 s názvem klipu a TC |
| `dark-historie.png` | — | záložka Historie karet: „Dnes“ se třemi kartami, „Denní report…“ |
| `light-*.png` | stejné | totéž ve světlé sadě: žádný bílý text na bílém, žluté / červené texty čitelné, rámečky karet viditelné |
| `dark-jeden-cil.png`, `dark-bez-cilu.png` | `01-pripraveno.png` (krok 2) | hlášky z kroku 5 na místě dlaždic |

Rozdíly, které jsou záměrné (Rozhodnutí v první části): „Projekt“ místo „Preset“ a „Upravit projekt“, Segoe UI místo
IBM Plex, cesty s `\`, připojení cíle podle skutečného disku, sekce Oznámení a Aplikace navíc v Nastavení, odkaz
„Denní report…“ u DNES.

- [ ] **Step 7: Úklid a zpráva**

```powershell
Remove-Item Env:\OFFLOAD_SETTINGS_DIR
Get-Process Offload -ErrorAction SilentlyContinue
Set-Location ..
git status --short
```

Expected: žádný běžící Offload; `git status` čistý (snímky jsou v `%TEMP%`). Kontroler pošle uživateli cestu ke
snímkům a seznam rozdílů. Každá oprava, na které se domluví, je samostatný commit (soubor + řádek do VYVOJ, zpráva přes
`.git/COMMIT_MSG_PLAN9`) a po ní se kroky 4–6 zopakují pro dotčený stav.

---

## Pokrytí zadání (kontrola plánu)

| Zadání (`docs/ui-navrh/README.md`) | Úkol |
|---|---|
| Design tokeny (barvy, písmo, rozměry), tmavé i světlé téma podle Windows | 10 |
| Horní lišta (logo, přepínač — projekt místo presetu, pilulka fronty, Ověřit zálohu, Hardware s tečkou, nastavení) | 11 |
| Levý sloupec: 1 · Karty, 2 · Cíle, 3 · Po zkopírování, připnutá lišta se spuštěním, úzké okno, kompaktní kroky | 13 |
| Fronta: prázdný stav, karta čeká / běží / hotovo / nesouhlasí / chyba / zrušeno, ověření zálohy | 14, 15 |
| Nastavení (obr. 4) jako stránka, ostatní sekce ze stávajících prvků | 12 |
| Nová věc 1 — průběh po cílech | 2 (snapshot), 6 (`JobCard.Rows`), 15 |
| Nová věc 2 — ukazatel fází | 6 (`JobCard.Steps`), 15 |
| Nová věc 3 — souhrn po polích, náhledy | 6 (`JobCard.Summary`, `Thumbs`), 15 |
| Nová věc 4 — nesouhlasné soubory po cílech, Zkopírovat znovu | 4, 5, 6 (`FailedRows`), 15 |
| Nová věc 6 — štítek kamery, typ připojení | 8, 13 |
| Nová věc 7 — dnešní historie, Historie karet | 9, 16 |
| Nová věc 8 — náhled burn-inu (šedý rám) | 12 |
| Nová věc 9 — Pozastavit | 2, 3, 15 |
| Nová věc 10 — odhad času | 7, 13, 15 |
| Nová věc 5 — presety | nedělá se (rozhodnutí 5): přepínač projektů, 11 |
| Postup 7 — ruční kontrola 1360×900 a 940×640, stavy | 17 (ukázkový režim), 19 |
| Postup 8 — VYVOJ, ROZDILY | každý úkol (VYVOJ), 18 |
| Vše dosavadní dosažitelné | mapa v první části; 11, 12, 13 |

Co plán vědomě nepokrývá (do zprávy uživateli): vizuální kontrola stavu „málo místa“ (potřebuje skutečný malý disk —
ruční test 40 a dosavadní testy `SpaceCheck`); CLI příkaz pro opravu; skutečný snímek v náhledu burn-inu; Thunderbolt
jako typ připojení; DNxHR (v návrhu zmíněný kodek Offload neumí, segmenty ukazují jen podporované kodeky).
