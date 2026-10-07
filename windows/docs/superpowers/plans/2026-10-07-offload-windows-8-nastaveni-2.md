# Offload pro Windows — plán 8: nastavení, oznámení, distribuce (úkoly 7–12) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Skupina C návrhu 15 vylepšení — projekty (pojmenovaná nastavení), vlastní pravidla LUTů a převod BRAW Film Gen 4 → Gen 5, oznámení (Windows a ntfy), kontrola aktualizací a podepsaný, verzovaný balíček; výsledek je verze 0.4.0.

**Architecture:** `AppSettings` se rozdělí na `SettingsProfile` (klíče projektu) a globální část; horní úroveň `settings.json` je profil „Bez projektu“, `projects[]` pojmenované profily (R17) a `MainViewModel.Settings` je vždy *aktivní* profil, takže dnešní vazby GUI fungují beze změny a úloha si bere klon jako dosud. Projekt jde do `JobConfig` → historie, reportu (`project`) a denního reportu. Pravidla LUTů jsou čisté funkce (`LutRules`) volané z `LutResolver` před automatikou; převod BRAW je volba `braw-decode --gen5`. Oznámení: čistá tabulka `JobOutcomes` + `NtfyClient` (HTTP POST) v Core, `TrayNotifier` (`Shell_NotifyIconW`) v App. Aktualizace: `UpdateChecker` (manifest z URL nebo souboru, 1× za 24 h). Distribuce: verze jen v `Directory.Build.props`, `scripts\release.ps1` (manifest, signtool) dot-sourcovaný z `build.ps1`.

**Tech Stack:** C# / .NET 10, WPF, xUnit, BCL (`HttpClient`, `HttpListener` jen v testech, `System.Text.Json`, `X509Certificate2`, `SHA1`), P/Invoke `shell32` (`Shell_NotifyIconW`, `ExtractIconExW`), Blackmagic RAW SDK přes COM (`braw-decode`), Windows PowerShell 5.1 (`build.ps1`, `release.ps1`; testy ho spouštějí s falešným `signtool.cmd`), ffmpeg 8.1 jen v testech `[FfmpegFact]`.

**Spec:** `windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md` — skupina C (§4.1–4.5), průřezové věci (§5), plán 8 (§6.3). Rozhodnutí řídicí relace platí beze změny: **R15** (burn-in výchozí zapnutý) a **R16** (MD5 jen navíc k xxh64). Plán má dvě části: `windows/docs/superpowers/plans/2026-10-07-offload-windows-8-nastaveni.md` (úkoly 1–6, s oddíly Review Focus, Rozhodnutí a Struktura souborů — platí i pro tento soubor) a tento soubor (úkoly 7–12).

## Global Constraints

- Žádný nový NuGet balíček — jen BCL .NET 10 (`System.Net.Http.HttpClient`, `System.Net.HttpListener` v testech, `System.Security.Cryptography`), P/Invoke.
- Texty UI česky, doslova podle návrhu; identifikátory v kódu anglicky. Logy (`app.log`, stderr nástrojů), TXT report a `--help` zůstávají anglicky (§2.6).
- UI vlákno nikdy nečeká na disk ani síť: kontrola souborů LUTů (vlastní i z pravidel), ntfy (i „Poslat zkušební oznámení“), kontrola aktualizací — vše `Task.Run` / `await` + výsledek přes dispatcher, jako dnes `RefreshLut` a `DestinationViewModel.Probe`.
- Na kartu se nikdy nezapisuje — nic v plánu 8 kartu nečte jinak než dosud (sériové číslo kamery se čte ze Sony `M01.XML` na **cíli**, kam se zkopírovalo).
- Formáty na disku čitelné Macem: nové soubory a klíče jsou jen **přidané** (`project`, `clips[].info.cameraSerial`, `clips[].info.colorScienceGenOriginal`, `clips[].lut.source = "rule"`, `proxy.lutRules`, `proxy.brawGen4ToGen5`); popis do `docs/FORMATY.md` (úkol 12).
- `settings.json` zachová neznámé klíče (R24): na horní úrovni, v každém projektu, v `proxy`, `naming`, `outputs`, `notify`, `updates` i v jednom pravidle LUTu.
- Testovatelné bez kamer a bez internetu: dočasné složky, ručně psané JSON/XML, falešný HTTP server `LocalHttp` (`HttpListener` na `127.0.0.1` a volném portu), falešný `signtool.cmd` a `braw-decode.cmd`, falešné hodiny (parametr `nowUtc`), falešné COM atributy BRAW. **Žádné skutečné síťové volání v testech.**
- `mac/` se nemění; co má Mac převzít, jde do `docs/VYVOJ-MAC.md` „Naplánováno“ (úkol 12).
- GUI se v testech ani při práci nikdy nespouští (`Offload.exe` ne). Rozhodování je v `Offload.Core` (testuje se tam); WPF části ověří `dotnet build` (XAML se kompiluje) a ruční test (README, úkol 12). Testovací projekt `Offload.App` nereferencuje.
- Testy nikdy nezapíšou do skutečného `%APPDATA%`: historie je přesměrovaná (`TestEnvironment`), nastavení jen přes `SettingsStore.Load/Save(path)` v `TempDir`, CLI `--project` čte `settings.json` přes `CliProject.SettingsPath` (testy ho přesměrují), stav kontroly aktualizací přes parametr `statePath`.
- R15: burn-in beze změny. R16: MD5 nikdy samotné.
- Verze: plán 8 → **0.4.0** (`0.4.0-win`, R22) — nastaví úkol 12 v `windows\Directory.Build.props` i v testech, které řetězec verze kontrolují (`FormatTests`, `ReportTests`, `HtmlReportTests`). Od úkolu 11 je jediným zdrojem verze `Directory.Build.props` (`OffloadVersion.String` se čte z `AssemblyInformationalVersionAttribute`).
- `TreatWarningsAsErrors`: každý úkol končí `dotnet build windows\Offload.sln -warnaserror` bez varování a celou zelenou sadou `dotnet test windows\Offload.sln` (dnes 1029 testů: 1022 úspěšných + 7 přeskočených; po plánu 1149 testů: 1141 úspěšných + 8 přeskočených). `ProxyWatchdogTests` je časově citlivý — když pod zátěží celé sady jednou selže, spusťte ho znovu samostatně (`--filter "FullyQualifiedName~ProxyWatchdogTests"`).
- PDF v testech: `PdfReport.SkipUnlessAsked` je pro celou sadu zapnuté; žádný test plánu 8 PDF netiskne.
- Každý commit: nahoře v sekci `## 2026-10-07 — plán 8: nastavení, oznámení, distribuce` v `docs/VYVOJ-WINDOWS.md` řádek úkolu (sekci založí úkol 1 nad sekcí `## 2026-10-07 — plán 7: výstupy pro produkci a střih`; nejnovější řádek nahoře, bez SHA). Soubory se přidávají výslovně (`git add <cesty>`, nikdy `-A`), zpráva se zapíše do `.git/COMMIT_MSG_PLAN8` (nástrojem Write, UTF-8) a commituje `git commit -F .git/COMMIT_MSG_PLAN8`; poslední řádek zprávy přesně `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Všechny příkazy git a dotnet se spouštějí z kořene repa `D:\My\Projekt\offload` (větev `windows-plan8`).
- Bloky „najít / nahradit“ uvádějí řádky podle stavu souboru **před** daným úkolem (výchozí stav = `windows-plan8` na `5d35441`, pak předchozí úkoly plánu). Kód plánu byl postavený a otestovaný v kopii repa mimo strom repa (každý úkol se zkompiloval bez varování a celá sada byla po něm zelená). Pracovní kopie má konce řádků CRLF (`core.autocrlf=true`), bloky v plánu LF — nástroj Edit je sjednotí; kdo upravuje skriptem, porovnává bez ohledu na `\r`.
- Nesledovaná složka `docs/ui-navrh/` (návrh nového vzhledu z jiné relace) se **neimplementuje ani necommituje**. GUI plánu 8 se drží dnešního rozvržení; logika projektů je ve vlastnostech `MainViewModel` (`ProjectChoices`, `ActiveProjectChoice`, příkazy), aby ji pozdější přepínač „presetu“ z návrhu mohl jen převázat.

---

### Task 7: BRAW Film Gen 4 → Gen 5 (`braw-decode --gen5`)

Spec §4.2 (BRAW Film Gen 4 → Gen 5), R26 (výchozí vypnuto), §6.3 úkol 6. `braw-decode --gen5` (pro `--info` i snímky) naklonuje atributy klipu (`CloneClipProcessingAttributes`), nastaví `ColorScienceGen` = 5 (VARIANT `VT_UI2`, slot 4 `SetClipAttribute`), přečte ho zpět a snímky dekóduje přes `CreateJobDecodeAndProcessFrame(clipAttrs, null)`; `--info --gen5` vrátí účinné hodnoty + `"colorScienceGenOriginal": 4`. Logika převodu se testuje proti falešnému COM objektu atributů (sloty 3 a 4), backend proti falešnému `braw-decode.cmd`; skutečný převod jen `[BrawSampleFact]` se vzorkem Gen 4 (ruční test 35) — **ověří až vzorek .braw Gen 4**.

**Files:**
- Modify: `windows/src/Offload.BrawDecode/Options.cs:13-21, 23-27, 36-39, 54-58`
- Modify: `windows/src/Offload.BrawDecode/InfoJson.cs:6-12, 24-27`
- Modify: `windows/src/Offload.BrawDecode/BrawApi.cs:261-264, 332-341`
- Modify: `windows/src/Offload.BrawDecode/Decoder.cs:48-51, 74-78, 83-86, 93-97, 105-109, 113-117, 228-231, 255-259`
- Modify: `windows/src/Offload.BrawDecode/Program.cs:1-8`
- Modify: `windows/src/Offload.Core/RawBackends.cs:70-74, 83-94, 112-115, 135-139, 174-178`
- Modify: `windows/src/Offload.Core/Media.cs:65-68, 203-206`
- Modify: `windows/src/Offload.Core/Luts.cs:193-196`
- Modify: `windows/src/Offload.Core/ProxyRenderer.cs:68-71`
- Modify: `windows/src/Offload.Cli/StageTwoOptions.cs:23-31, 74-77`
- Modify: `windows/src/Offload.App/ProxyOptionsViewModel.cs:195-198`
- Modify: `windows/src/Offload.App/SetupPanel.xaml:334-337`
- Test: `windows/tests/Offload.Core.Tests/BrawGen5Tests.cs` (nový), `windows/tests/Offload.Core.Tests/BrawSampleTests.cs:38-41`
- Modify: `docs/VYVOJ-WINDOWS.md:22-23`

**Interfaces:**
- Consumes: `BrawClipProcessingAttributes.Number` (plán 3), `ProxySettings` (úkol 5 přidal `LutRules` — sem patří i nová volba), `CliProject.Given` (úkol 4).
- Produces: `Options.Gen5` (init); `ClipFacts(…, int? ColorScienceGen, int? ColorScienceGenOriginal = null)`; `BrawClipProcessingAttributes.SetNumber(uint, ushort) → int`; `BrawFrame.CreateJobDecodeAndProcessFrame(nint clipAttributes, out BrawJob)`; `internal static bool Decoder.ToGen5(BrawClipProcessingAttributes, TextWriter, out int? original)`; `FrameSink.ClipAttributes`; `BrawInfo.ColorScienceGenOriginal`; `BrawBackend(Func<string,string?>? findLibrary = null, TimeSpan? infoTimeout = null, bool gen5 = false)`; `ClipInfo.ColorScienceGenOriginal` (`colorScienceGenOriginal`); `ProxySettings.BrawGen4ToGen5` (`brawGen4ToGen5`, chybí když false); CLI `--braw-gen5`; `ProxyOptionsViewModel.BrawGen4ToGen5`.

- [ ] **Step 1: Padající testy**

Vytvořit `windows/tests/Offload.Core.Tests/BrawGen5Tests.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Offload.BrawDecode;
using Offload.Cli;

namespace Offload.Core.Tests;

/// Spec 4.2, R26: Blackmagic RAW Film Gen 4 decoded as Gen 5 (braw-decode --gen5) so the Gen 5 LUT fits — off by
/// default. The real conversion is checked only with a Gen 4 sample ([BrawSampleFact], manual test).
[Collection("Console")]
public class BrawGen5Tests
{
    [Fact]
    public void Options_Gen5_ForInfoAndFrames()
    {
        Assert.True(Options.Parse(["--info", "--gen5", "--scale", "eighth", "a.braw"])!.Gen5);
        Assert.True(Options.Parse(["--gen5", "--start", "3", "--count", "1", "a.braw"])!.Gen5);
        Assert.False(Options.Parse(["a.braw"])!.Gen5);
        Assert.Contains("[--gen5]", Options.Usage);
    }

    [Fact]
    public void InfoJson_OriginalGenerationAfterTheColour()
    {
        var f = new ClipFacts(1, 2, 3, 4, 25, 1, "", "", "Blackmagic Design Film", "Blackmagic Design", 5, 4);
        Assert.EndsWith(""","gamma":"Blackmagic Design Film","gamut":"Blackmagic Design","colorScienceGen":5,"colorScienceGenOriginal":4}""" + "\n", InfoJson.Format(f));
        var info = BrawBackend.ToClipInfo(BrawBackend.ParseInfo(InfoJson.Format(f)));
        Assert.Equal((5, 4), (info.ColorScienceGen, info.ColorScienceGenOriginal));
    }

    /// A stand-in IBlackmagicRawClipProcessingAttributes: slot 3 reads the colour science generation, slot 4 sets it.
    static unsafe class FakeGen
    {
        public static int Gen;
        public static int SetResult;      // what SetClipAttribute returns
        public static bool Ignore;        // the SDK says OK but keeps the old value

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int Get(nint self, uint attribute, ComVariant* value)
        {
            if (attribute != FourCC.ClipAttributeColorScienceGen || Gen < 0) return Com.E_FAIL;
            *value = ComVariant.Create((ushort)Gen);
            return Com.S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int Set(nint self, uint attribute, ComVariant* value)
        {
            if (SetResult != Com.S_OK) return SetResult;
            if (attribute == FourCC.ClipAttributeColorScienceGen && value->VarType == VarEnum.VT_UI2 && !Ignore) Gen = value->GetRawDataRef<ushort>();
            return Com.S_OK;
        }

        public static nint Create(int gen, int setResult = Com.S_OK, bool ignore = false)
        {
            (Gen, SetResult, Ignore) = (gen, setResult, ignore);
            var vtbl = (nint*)NativeMemory.AllocZeroed(5, (nuint)sizeof(nint));
            vtbl[3] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, ComVariant*, int>)&Get;
            vtbl[4] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, ComVariant*, int>)&Set;
            var obj = (nint*)NativeMemory.Alloc((nuint)sizeof(nint));
            *obj = (nint)vtbl;
            return (nint)obj;
        }

        public static void Free(nint obj)
        {
            NativeMemory.Free(*(void**)obj);
            NativeMemory.Free((void*)obj);
        }
    }

    static (bool Ok, int? Original, int Now, string Stderr) ToGen5(int gen, int setResult = Com.S_OK, bool ignore = false)
    {
        nint obj = FakeGen.Create(gen, setResult, ignore);
        try
        {
            using var stderr = new StringWriter();
            bool ok = Decoder.ToGen5(new BrawClipProcessingAttributes(obj), stderr, out var original);
            return (ok, original, FakeGen.Gen, stderr.ToString());
        }
        finally
        {
            FakeGen.Free(obj);
        }
    }

    [Fact]
    public void ToGen5_Gen4_IsSetTo5_AndReadBack()
    {
        var r = ToGen5(4);
        Assert.Equal((true, (int?)4, 5, ""), r);
    }

    [Fact]
    public void ToGen5_AlreadyGen5_NothingToDo_NoWarning()
    {
        Assert.Equal((false, (int?)5, 5, ""), ToGen5(5));
    }

    // Spec 4.2: when the SDK does not take it — a warning on stderr, decoded as Gen 4 (no LUT, as before).
    [Fact]
    public void ToGen5_Refused_OrIgnored_IsAWarning()
    {
        var refused = ToGen5(4, setResult: Com.E_FAIL);
        Assert.False(refused.Ok);
        Assert.Equal("braw-decode: --gen5: colour science Gen 5 not accepted (0x80004005, reads 4) — decoding as Gen 4" + Environment.NewLine, refused.Stderr);
        var ignored = ToGen5(4, ignore: true);
        Assert.False(ignored.Ok);
        Assert.Contains("(0x00000000, reads 4) — decoding as Gen 4", ignored.Stderr);
        var unknown = ToGen5(-1);
        Assert.False(unknown.Ok);
        Assert.Equal("braw-decode: --gen5: colour science generation unknown — decoding as recorded" + Environment.NewLine, unknown.Stderr);
    }

    // The LUT rule: an effective Gen 5 that was Gen 4 gets the BMD Gen 5 LUT, with the reason; without the conversion
    // Gen 4 stays without a LUT (as before).
    [Fact]
    public void LutResolver_Gen4_WithAndWithoutConversion()
    {
        var cat = LutCatalog.Load(LutCatalog.DefaultFolder);
        var clip = new Clip { Name = "A001", Path = "A001.braw", Format = ClipFormat.Braw, Segments = ["A001.braw"], Bytes = 1 };
        var s = new ProxySettings { Enabled = true, LutMode = LutMode.Auto };
        var converted = LutResolver.Resolve(clip, new ClipInfo { Gamma = "Blackmagic Design Film", ColorScienceGen = 5, ColorScienceGenOriginal = 4 }, @"X:\A001.braw", s, cat);
        Assert.Equal(("auto", "bmd-film-gen5", "Blackmagic RAW: Film Gen 4 → Gen 5 (převod SDK)"), (converted.Source, converted.Id, converted.Reason));
        var plain = LutResolver.Resolve(clip, new ClipInfo { Gamma = "Blackmagic Design Film", ColorScienceGen = 4 }, @"X:\A001.braw", s, cat);
        Assert.Equal(("none", "Blackmagic RAW: Blackmagic Design Film Gen 4 (bez vestavěného LUTu)"), (plain.Source, plain.Reason));
    }

    /// A stand-in braw-decode that writes its arguments to args.txt and answers --info with a converted Gen 4 clip.
    static string RecordingDecoder(string dir)
    {
        var path = Path.Combine(dir, "braw-decode.cmd");
        File.WriteAllText(path, """
            @echo off
            >>"%~dp0args.txt" echo %*
            echo {"width":320,"height":180,"sourceWidth":320,"sourceHeight":180,"fps":25,"frames":1,"timecode":"","camera":"","gamma":"Blackmagic Design Film","colorScienceGen":5,"colorScienceGenOriginal":4}
            exit /b 0

            """.ReplaceLineEndings("\r\n"));
        File.WriteAllText(Path.Combine(dir, "BlackmagicRawAPI.dll"), "x");   // the backend is available
        return path;
    }

    // proxy.brawGen4ToGen5 → the renderer's BRAW backend passes --gen5 (for --info and for the frames); off → it does not.
    [Fact]
    public void Renderer_PassesGen5_OnlyWhenSwitchedOn()
    {
        using var dir = new TempDir();
        var tools = new ExternalTools { BrawDecoder = RecordingDecoder(dir.Path), NoSearch = true };
        var clip = new Clip { Name = "A001", Path = "A001.braw", Format = ClipFormat.Braw, Segments = ["A001.braw"], Bytes = 1 };
        var card = dir.Sub("card");

        using (var on = new ProxyRenderer(new ProxySettings { BrawGen4ToGen5 = true }, tools))
        {
            var info = on.Probe(clip, card);
            Assert.Equal((5, 4), (info.ColorScienceGen, info.ColorScienceGenOriginal));
        }
        using (var off = new ProxyRenderer(new ProxySettings(), tools)) off.Probe(clip, card);

        var lines = File.ReadAllLines(Path.Combine(dir.Path, "args.txt"));
        Assert.StartsWith("--info --gen5 --scale eighth ", lines[0]);
        Assert.StartsWith("--info --scale eighth ", lines[1]);
    }

    [Fact]
    public void Setting_OffByDefault_AbsentWhileOff_CliOption()
    {
        Assert.False(new ProxySettings().BrawGen4ToGen5);
        Assert.DoesNotContain("brawGen4ToGen5", System.Text.Json.JsonSerializer.Serialize(new ProxySettings()));
        Assert.Contains("\"brawGen4ToGen5\":true", System.Text.Json.JsonSerializer.Serialize(new ProxySettings { BrawGen4ToGen5 = true }));

        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "A001.braw"), 100, 1);
        JobConfig? seen = null;
        var old = CopyCommand.JobFactory;
        var (o, e) = (Console.Out, Console.Error);
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        CopyCommand.JobFactory = c => new OffloadJob(seen = c, AppendFixtures.Options());
        try { Assert.Equal(0, CopyCommand.Create().Parse([card, "-d", dir.Sub("d1"), "--dry-run", "--no-history", "--braw-gen5"]).Invoke()); }
        finally
        {
            CopyCommand.JobFactory = old;
            Console.SetOut(o);
            Console.SetError(e);
        }
        Assert.True(seen!.Proxy.BrawGen4ToGen5);
    }
}
```

`windows/tests/Offload.Core.Tests/BrawSampleTests.cs`, řádky 38–41 — najít:

```csharp
        var full = Info("full");
        Assert.Equal((i.SourceWidth, i.SourceHeight), (full.Width, full.Height));
    }

```

nahradit:

```csharp
        var full = Info("full");
        Assert.Equal((i.SourceWidth, i.SourceHeight), (full.Width, full.Height));
    }

    // Spec 4.2 (R26): --gen5 with a real clip. A Gen 4 sample must come out as Gen 5 with "colorScienceGenOriginal": 4 and
    // its frames must decode — that is what confirms the SDK conversion. A Gen 5 sample is left as it is.
    [BrawSampleFact]
    public void Gen5_ConvertsAGen4Clip_LeavesGen5()
    {
        var plain = Info("eighth");
        var r = Run("--info", "--gen5", "--scale", "eighth", Sample);
        Assert.True(r.Code == 0, r.Stderr);
        var gen5 = BrawBackend.ParseInfo(System.Text.Encoding.UTF8.GetString(r.Stdout));
        if (plain.ColorScienceGen < 5)
        {
            Assert.Equal((5, plain.ColorScienceGen), (gen5.ColorScienceGen, gen5.ColorScienceGenOriginal));
            var frame = Run("--gen5", "--scale", "eighth", "--count", "1", Sample);
            Assert.True(frame.Code == 0, frame.Stderr);
            Assert.Equal((long)gen5.Width * gen5.Height * 6, frame.Stdout.LongLength);
        }
        else
        {
            Assert.Equal((plain.ColorScienceGen, (int?)null), (gen5.ColorScienceGen, gen5.ColorScienceGenOriginal));
        }
    }

```


- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~BrawGen5Tests"`
Expected: FAIL — build testů selže (CS1061 / CS0117: `Options.Gen5`, `Decoder.ToGen5`, `ClipInfo.ColorScienceGenOriginal`, `ProxySettings.BrawGen4ToGen5` neexistují).

- [ ] **Step 3: Implementace**

`windows/src/Offload.App/ProxyOptionsViewModel.cs`, řádky 195–198 — najít:

```csharp
    public bool SkipCameraProxies { get => p.SkipCameraProxies; set { p.SkipCameraProxies = value; Changed(nameof(SkipCameraProxies)); } }
    public bool CopyToAllDestinations { get => p.CopyToAllDestinations; set { p.CopyToAllDestinations = value; Changed(nameof(CopyToAllDestinations)); } }

    static readonly string[] FolderProperties = [nameof(ExtraFolderName), nameof(ExtraFolderPath), nameof(HasExtraFolder)];
```

nahradit:

```csharp
    public bool SkipCameraProxies { get => p.SkipCameraProxies; set { p.SkipCameraProxies = value; Changed(nameof(SkipCameraProxies)); } }
    public bool CopyToAllDestinations { get => p.CopyToAllDestinations; set { p.CopyToAllDestinations = value; Changed(nameof(CopyToAllDestinations)); } }
    /// Spec 4.2, R26: off by default until a Gen 4 sample confirms the SDK conversion.
    public bool BrawGen4ToGen5 { get => p.BrawGen4ToGen5; set { p.BrawGen4ToGen5 = value; Changed(nameof(BrawGen4ToGen5)); } }

    static readonly string[] FolderProperties = [nameof(ExtraFolderName), nameof(ExtraFolderPath), nameof(HasExtraFolder)];
```

`windows/src/Offload.App/SetupPanel.xaml`, řádky 334–337 — najít:

```xml
                        <TextBlock Text="Canon Cinema RAW Light a Sony X-OCN se zkopírují a ověří, proxy pro ně nejde vyrobit." Style="{StaticResource Caption}"
                                   TextWrapping="Wrap" TextTrimming="None" Margin="0,4,0,0" />
                    </StackPanel>
                </Expander>
```

nahradit:

```xml
                        <TextBlock Text="Canon Cinema RAW Light a Sony X-OCN se zkopírují a ověří, proxy pro ně nejde vyrobit." Style="{StaticResource Caption}"
                                   TextWrapping="Wrap" TextTrimming="None" Margin="0,4,0,0" />
                        <CheckBox Content="Blackmagic RAW Film Gen 4 převést na Gen 5 (použije se LUT Gen 5)" IsChecked="{Binding Proxy.BrawGen4ToGen5}"
                                  Margin="0,8,0,0" AutomationProperties.AutomationId="BrawGen4ToGen5" />
                        <TextBlock Text="ověří až vzorek .braw Gen 4" Style="{StaticResource Caption}" Margin="24,0,0,0" />
                    </StackPanel>
                </Expander>
```

`windows/src/Offload.BrawDecode/BrawApi.cs`, řádky 261–264 — najít:

```csharp
    }

    /// The one VARIANT read: slot 3 GetClipAttribute. A failed call (attribute not available) is null without an
    /// error; anything thrown while reading or clearing the VARIANT becomes `error`.
```

nahradit:

```csharp
    }

    /// Slot 4 SetClipAttribute with a VT_UI2 value (colour science generation is a u16). The HRESULT.
    public int SetNumber(uint attribute, ushort value)
    {
        ComVariant v = ComVariant.Create(value);
        try
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint, ComVariant*, int>)Com.Slot(ptr, 4))(ptr, attribute, &v);
        }
        finally
        {
            v.Dispose();
        }
    }

    /// The one VARIANT read: slot 3 GetClipAttribute. A failed call (attribute not available) is null without an
    /// error; anything thrown while reading or clearing the VARIANT becomes `error`.
```

`windows/src/Offload.BrawDecode/BrawApi.cs`, řádky 332–341 — najít:

```csharp
    public int SetResourceFormat(uint format) => ((delegate* unmanaged[Stdcall]<nint, uint, int>)Com.Slot(ptr, 11))(ptr, format);

    /// Slot 14, with the clip's own processing attributes (null overrides) — frames come out in the gamma they were
    /// shot in.
    public int CreateJobDecodeAndProcessFrame(out BrawJob job)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)Com.Slot(ptr, 14))(ptr, 0, 0, &p);
        job = new BrawJob(p);
        return hr;
```

nahradit:

```csharp
    public int SetResourceFormat(uint format) => ((delegate* unmanaged[Stdcall]<nint, uint, int>)Com.Slot(ptr, 11))(ptr, format);

    /// Slot 14. `clipAttributes` 0 = the clip's own processing attributes — frames come out in the gamma they were shot
    /// in; with --gen5 the clone set to colour science Gen 5.
    public int CreateJobDecodeAndProcessFrame(nint clipAttributes, out BrawJob job)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)Com.Slot(ptr, 14))(ptr, clipAttributes, 0, &p);
        job = new BrawJob(p);
        return hr;
```

`windows/src/Offload.BrawDecode/Decoder.cs`, řádky 48–51 — najít:

```csharp
        BrawCodec codec = default;
        BrawClip clip = default;
        try
        {
```

nahradit:

```csharp
        BrawCodec codec = default;
        BrawClip clip = default;
        BrawClipProcessingAttributes gen5 = default;
        try
        {
```

`windows/src/Offload.BrawDecode/Decoder.cs`, řádky 74–78 — najít:

```csharp
                return NoLibrary;
            }
            int code = o.Info ? Info(codec, clip, sink, stdout, stderr) : Frames(o, codec, clip, sink, stdout, stderr);
            if (code == ReaderGone) return Ok;   // like the Mac's _exit(0) on a failed write
            return code != Ok ? code : sink.Errors > 0 ? FrameFailed : Ok;
```

nahradit:

```csharp
                return NoLibrary;
            }
            int? original = null;
            if (o.Gen5)
            {
                gen5 = Gen5Attributes(clip, stderr, out original);
                sink.ClipAttributes = gen5.Ptr;   // 0 = not converted: decoded as recorded
            }
            int code = o.Info ? Info(codec, clip, sink, stdout, stderr, gen5, original) : Frames(o, codec, clip, sink, stdout, stderr);
            if (code == ReaderGone) return Ok;   // like the Mac's _exit(0) on a failed write
            return code != Ok ? code : sink.Errors > 0 ? FrameFailed : Ok;
```

`windows/src/Offload.BrawDecode/Decoder.cs`, řádky 83–86 — najít:

```csharp
            // SetCallback(0) gives the SDK's reference to the callback back before `callback` is disposed.
            if (codec.Ptr != 0) codec.FlushJobs();
            Com.Release(clip.Ptr);
            if (codec.Ptr != 0)
```

nahradit:

```csharp
            // SetCallback(0) gives the SDK's reference to the callback back before `callback` is disposed.
            if (codec.Ptr != 0) codec.FlushJobs();
            Com.Release(gen5.Ptr);
            Com.Release(clip.Ptr);
            if (codec.Ptr != 0)
```

`windows/src/Offload.BrawDecode/Decoder.cs`, řádky 93–97 — najít:

```csharp
    }

    static int Info(BrawCodec codec, BrawClip clip, FrameSink sink, Stream stdout, TextWriter stderr)
    {
        ulong frames = clip.FrameCount;
```

nahradit:

```csharp
    }

    /// --gen5: a clone of the clip's processing attributes set to colour science Gen 5 — or default (Ptr 0) when the clip
    /// is Gen 5 already or the SDK refuses (a warning on stderr; then it is decoded as recorded, without a LUT as before).
    /// `original`: the generation the clip was recorded in, when the SDK says.
    static BrawClipProcessingAttributes Gen5Attributes(BrawClip clip, TextWriter stderr, out int? original)
    {
        original = null;
        if (clip.CloneClipProcessingAttributes(out var attrs) != Com.S_OK || attrs.Ptr == 0)
        {
            Say(stderr, "braw-decode: --gen5: the clip's processing attributes cannot be read — decoding as recorded");
            return default;
        }
        if (ToGen5(attrs, stderr, out original)) return attrs;
        Com.Release(attrs.Ptr);
        return default;
    }

    /// Sets colour science Gen 5 on `attrs` and reads it back (spec 4.2: SetClipAttribute(ColorScienceGen, u16 5)).
    /// False — nothing to use — when the generation is unknown, already 5 or more, or the SDK does not take 5.
    internal static bool ToGen5(BrawClipProcessingAttributes attrs, TextWriter stderr, out int? original)
    {
        original = attrs.Number(FourCC.ClipAttributeColorScienceGen, out var error);
        if (original is not int gen)
        {
            Say(stderr, $"braw-decode: --gen5: colour science generation unknown{(error != null ? $" ({error})" : "")} — decoding as recorded");
            return false;
        }
        if (gen >= 5) return false;
        int hr = attrs.SetNumber(FourCC.ClipAttributeColorScienceGen, 5);
        var now = attrs.Number(FourCC.ClipAttributeColorScienceGen, out _);
        if (hr == Com.S_OK && now == 5) return true;
        Say(stderr, $"braw-decode: --gen5: colour science Gen 5 not accepted ({Com.Hex(hr)}, reads {now?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing"}) — decoding as Gen {gen}");
        return false;
    }

    static int Info(BrawCodec codec, BrawClip clip, FrameSink sink, Stream stdout, TextWriter stderr, BrawClipProcessingAttributes gen5, int? original)
    {
        ulong frames = clip.FrameCount;
```

`windows/src/Offload.BrawDecode/Decoder.cs`, řádky 105–109 — najít:

```csharp
        }
        (string? gamma, string? gamut, int? gen) = (null, null, null);
        if (clip.CloneClipProcessingAttributes(out var attrs) == Com.S_OK && attrs.Ptr != 0)
        {
            try { (gamma, gamut, gen) = Colour(attrs, stderr); }
```

nahradit:

```csharp
        }
        (string? gamma, string? gamut, int? gen) = (null, null, null);
        if (gen5.Ptr != 0)
        {
            // Converted: the effective gamma / gamut / generation after the change (spec 4.2), plus the recorded one.
            (gamma, gamut, gen) = Colour(gen5, stderr);
        }
        else if (clip.CloneClipProcessingAttributes(out var attrs) == Com.S_OK && attrs.Ptr != 0)
        {
            try { (gamma, gamut, gen) = Colour(attrs, stderr); }
```

`windows/src/Offload.BrawDecode/Decoder.cs`, řádky 113–117 — najít:

```csharp
        var facts = new ClipFacts(
            s.Ok ? s.Width : clip.Width, s.Ok ? s.Height : clip.Height, clip.Width, clip.Height, clip.FrameRate, frames,
            clip.TimecodeForFrame(0) ?? "", clip.CameraType ?? "", gamma, gamut, gen);
        var bytes = Encoding.UTF8.GetBytes(InfoJson.Format(facts));
        return Write(stdout, bytes, bytes.Length) ? Ok : Closed(stderr);
```

nahradit:

```csharp
        var facts = new ClipFacts(
            s.Ok ? s.Width : clip.Width, s.Ok ? s.Height : clip.Height, clip.Width, clip.Height, clip.FrameRate, frames,
            clip.TimecodeForFrame(0) ?? "", clip.CameraType ?? "", gamma, gamut, gen, gen5.Ptr != 0 ? original : null);
        var bytes = Encoding.UTF8.GetBytes(InfoJson.Format(facts));
        return Write(stdout, bytes, bytes.Length) ? Ok : Closed(stderr);
```

`windows/src/Offload.BrawDecode/Decoder.cs`, řádky 228–231 — najít:

```csharp
    int errors;

    public int Errors => Volatile.Read(ref errors);

```

nahradit:

```csharp
    int errors;

    /// --gen5: the processing attributes every decode job uses (0 = the clip's own). Set before the first job.
    public nint ClipAttributes { get; set; }

    public int Errors => Volatile.Read(ref errors);

```

`windows/src/Offload.BrawDecode/Decoder.cs`, řádky 255–259 — najít:

```csharp
            if (result == Com.S_OK) result = frame.SetResourceFormat(FourCC.ResourceFormatRgbU16);
            if (result == Com.S_OK) result = frame.SetResolutionScale(scale);
            if (result == Com.S_OK) result = frame.CreateJobDecodeAndProcessFrame(out decode);
            if (result == Com.S_OK)
            {
```

nahradit:

```csharp
            if (result == Com.S_OK) result = frame.SetResourceFormat(FourCC.ResourceFormatRgbU16);
            if (result == Com.S_OK) result = frame.SetResolutionScale(scale);
            if (result == Com.S_OK) result = frame.CreateJobDecodeAndProcessFrame(ClipAttributes, out decode);
            if (result == Com.S_OK)
            {
```

`windows/src/Offload.BrawDecode/InfoJson.cs`, řádky 6–12 — najít:

```csharp
/// What --info reports. Width/Height are the processed size at the requested scale (decoded from frame 0 —
/// the SDK's rounding is the truth, not width/scale), Source* the sensor size.
internal sealed record ClipFacts(
    uint Width, uint Height, uint SourceWidth, uint SourceHeight, float Fps, ulong Frames,
    string Timecode, string Camera, string? Gamma, string? Gamut, int? ColorScienceGen);

internal static class InfoJson
```

nahradit:

```csharp
/// What --info reports. Width/Height are the processed size at the requested scale (decoded from frame 0 —
/// the SDK's rounding is the truth, not width/scale), Source* the sensor size.
/// `ColorScienceGenOriginal`: with --gen5, the generation the clip was recorded in (ColorScienceGen is then the
/// effective one).
internal sealed record ClipFacts(
    uint Width, uint Height, uint SourceWidth, uint SourceHeight, float Fps, ulong Frames,
    string Timecode, string Camera, string? Gamma, string? Gamut, int? ColorScienceGen, int? ColorScienceGenOriginal = null);

internal static class InfoJson
```

`windows/src/Offload.BrawDecode/InfoJson.cs`, řádky 24–27 — najít:

```csharp
        if (f.Gamut != null) sb.Append(inv, $",\"gamut\":\"{Escape(f.Gamut)}\"");
        if (f.ColorScienceGen is int gen) sb.Append(inv, $",\"colorScienceGen\":{gen}");
        sb.Append("}\n");
        return sb.ToString();
```

nahradit:

```csharp
        if (f.Gamut != null) sb.Append(inv, $",\"gamut\":\"{Escape(f.Gamut)}\"");
        if (f.ColorScienceGen is int gen) sb.Append(inv, $",\"colorScienceGen\":{gen}");
        if (f.ColorScienceGenOriginal is int original) sb.Append(inv, $",\"colorScienceGenOriginal\":{original}");
        sb.Append("}\n");
        return sb.ToString();
```

`windows/src/Offload.BrawDecode/Options.cs`, řádky 13–21 — najít:

```csharp

/// Command line, the same as the Mac braw-decode:
///   braw-decode --info [--scale S] clip.braw
///   braw-decode [--scale S] [--start N] [--count N] clip.braw
internal sealed record Options(bool Info, Scale Scale, ulong Start, ulong? Count, string Path)
{
    public const string Usage = "usage: braw-decode [--info] [--scale full|half|quarter|eighth] [--start N] [--count N] clip.braw";

    /// null = bad command line (print Usage, exit 2). Stricter than the Mac (which falls back to "full" / 0):
```

nahradit:

```csharp

/// Command line, the same as the Mac braw-decode:
///   braw-decode --info [--gen5] [--scale S] clip.braw
///   braw-decode [--gen5] [--scale S] [--start N] [--count N] clip.braw
/// `--gen5` (spec 4.2): a Blackmagic Design Film Gen 4 clip is decoded with colour science Gen 5 (and --info reports the
/// effective values plus "colorScienceGenOriginal").
internal sealed record Options(bool Info, Scale Scale, ulong Start, ulong? Count, string Path)
{
    public const string Usage = "usage: braw-decode [--info] [--gen5] [--scale full|half|quarter|eighth] [--start N] [--count N] clip.braw";

    public bool Gen5 { get; init; }

    /// null = bad command line (print Usage, exit 2). Stricter than the Mac (which falls back to "full" / 0):
```

`windows/src/Offload.BrawDecode/Options.cs`, řádky 23–27 — najít:

```csharp
    public static Options? Parse(IReadOnlyList<string> args)
    {
        bool info = false;
        var scale = Scale.Full;
        ulong start = 0;
```

nahradit:

```csharp
    public static Options? Parse(IReadOnlyList<string> args)
    {
        bool info = false, gen5 = false;
        var scale = Scale.Full;
        ulong start = 0;
```

`windows/src/Offload.BrawDecode/Options.cs`, řádky 36–39 — najít:

```csharp
                case "--info":
                    info = true;
                    break;
                case "--scale":
```

nahradit:

```csharp
                case "--info":
                    info = true;
                    break;
                case "--gen5":
                    gen5 = true;
                    break;
                case "--scale":
```

`windows/src/Offload.BrawDecode/Options.cs`, řádky 54–58 — najít:

```csharp
            }
        }
        return path == null ? null : new Options(info, scale, start, count, path);
    }

```

nahradit:

```csharp
            }
        }
        return path == null ? null : new Options(info, scale, start, count, path) { Gen5 = gen5 };
    }

```

`windows/src/Offload.BrawDecode/Program.cs`, řádky 1–8 — najít:

```csharp
// braw-decode for Windows — minimal Blackmagic RAW decoder for the Offload proxy pipeline (same CLI as the Mac tool).
//
//   braw-decode --info [--scale S] clip.braw
//       one JSON line: width, height (processed image at scale S), sourceWidth, sourceHeight, fps, frames,
//       timecode, camera, and when the SDK reports them gamma, gamut, colorScienceGen
//   braw-decode [--scale S] [--start N] [--count N] clip.braw
//       raw interleaved RGB 16-bit little endian frames on stdout (ffmpeg: -f rawvideo -pix_fmt rgb48le)
//
```

nahradit:

```csharp
// braw-decode for Windows — minimal Blackmagic RAW decoder for the Offload proxy pipeline (same CLI as the Mac tool).
//
//   braw-decode --info [--gen5] [--scale S] clip.braw
//       one JSON line: width, height (processed image at scale S), sourceWidth, sourceHeight, fps, frames,
//       timecode, camera, and when the SDK reports them gamma, gamut, colorScienceGen (with --gen5 the effective
//       values and colorScienceGenOriginal)
//   braw-decode [--gen5] [--scale S] [--start N] [--count N] clip.braw
//   --gen5: a Film Gen 4 clip is decoded with colour science Gen 5 (verified only with a Gen 4 sample)
//       raw interleaved RGB 16-bit little endian frames on stdout (ffmpeg: -f rawvideo -pix_fmt rgb48le)
//
```

`windows/src/Offload.Cli/StageTwoOptions.cs`, řádky 23–31 — najít:

```csharp
    readonly Option<bool> noClipList = new("--no-clip-list") { Description = "No clip list CSV (_REPORT\\<card>_clips.csv)." };
    readonly Option<bool> ale = new("--ale") { Description = "Also write the clip list for Avid (_REPORT\\<card>_clips.ale)." };
    readonly Option<bool> noDayReport = new("--no-day-report") { Description = "Do not make the day report (<date>\\_REPORT\\<date>_day_report.html) again after the copy." };

    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, lutRule, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder, noClipList, ale, noDayReport })
            cmd.Options.Add(o);
    }
```

nahradit:

```csharp
    readonly Option<bool> noClipList = new("--no-clip-list") { Description = "No clip list CSV (_REPORT\\<card>_clips.csv)." };
    readonly Option<bool> ale = new("--ale") { Description = "Also write the clip list for Avid (_REPORT\\<card>_clips.ale)." };
    readonly Option<bool> brawGen5 = new("--braw-gen5") { Description = "Decode Blackmagic RAW Film Gen 4 as Gen 5 (the Gen 5 LUT then fits). Not yet verified with a Gen 4 sample." };
    readonly Option<bool> noDayReport = new("--no-day-report") { Description = "Do not make the day report (<date>\\_REPORT\\<date>_day_report.html) again after the copy." };

    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, lutRule, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder, noClipList, ale, noDayReport, brawGen5 })
            cmd.Options.Add(o);
    }
```

`windows/src/Offload.Cli/StageTwoOptions.cs`, řádky 74–77 — najít:

```csharp
        if (Given(frameScale)) p.FrameScale = r.GetValue(frameScale) / 100;
        if (Given(noBurnIn)) p.BurnFilename = p.BurnTimecode = !r.GetValue(noBurnIn);
        if (Given(proxyFolder)) p.ExtraFolder = r.GetValue(proxyFolder) is string pf ? CliPath.Full(pf) : null;
        config.Proxy = p;
```

nahradit:

```csharp
        if (Given(frameScale)) p.FrameScale = r.GetValue(frameScale) / 100;
        if (Given(noBurnIn)) p.BurnFilename = p.BurnTimecode = !r.GetValue(noBurnIn);
        if (Given(brawGen5)) p.BrawGen4ToGen5 = r.GetValue(brawGen5);
        if (Given(proxyFolder)) p.ExtraFolder = r.GetValue(proxyFolder) is string pf ? CliPath.Full(pf) : null;
        config.Proxy = p;
```

`windows/src/Offload.Core/Luts.cs`, řádky 193–196 — najít:

```csharp
        if (!gamma.Equals("Blackmagic Design Film", StringComparison.OrdinalIgnoreCase))
            return ("", $"Blackmagic RAW: {label} (není log)");
        if (info.ColorScienceGen is int g && g < 5) return ("", $"Blackmagic RAW: {label} (bez vestavěného LUTu)");
        return ("bmd-film-gen5", $"Blackmagic RAW: {label}");
```

nahradit:

```csharp
        if (!gamma.Equals("Blackmagic Design Film", StringComparison.OrdinalIgnoreCase))
            return ("", $"Blackmagic RAW: {label} (není log)");
        // Decoded by braw-decode --gen5 (spec 4.2): recorded in an older generation, now Gen 5 — the Gen 5 LUT fits.
        if (info.ColorScienceGenOriginal is int original && original < 5 && info.ColorScienceGen is int effective && effective >= 5)
            return ("bmd-film-gen5", $"Blackmagic RAW: Film Gen {original} → Gen {effective} (převod SDK)");
        if (info.ColorScienceGen is int g && g < 5) return ("", $"Blackmagic RAW: {label} (bez vestavěného LUTu)");
        return ("bmd-film-gen5", $"Blackmagic RAW: {label}");
```

`windows/src/Offload.Core/Media.cs`, řádky 65–68 — najít:

```csharp
    [JsonPropertyName("gamut")] public string? Gamut { get; set; }
    [JsonPropertyName("colorScienceGen")] public int? ColorScienceGen { get; set; }
    /// The reel (tape name): ffprobe's `reel_name` / `tape_name` / `reel` tag after the probe, then ReelResolver's
    /// answer (tag, else the clip name, else the card folder) once the job has the clip (spec 3.4). Report key
```

nahradit:

```csharp
    [JsonPropertyName("gamut")] public string? Gamut { get; set; }
    [JsonPropertyName("colorScienceGen")] public int? ColorScienceGen { get; set; }
    /// With the Gen 4 → Gen 5 conversion (spec 4.2): the generation the clip was recorded in; ColorScienceGen is then
    /// the effective one. Report key `clips[].info.colorScienceGenOriginal`, absent otherwise.
    [JsonPropertyName("colorScienceGenOriginal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ColorScienceGenOriginal { get; set; }
    /// The reel (tape name): ffprobe's `reel_name` / `tape_name` / `reel` tag after the probe, then ReelResolver's
    /// answer (tag, else the clip name, else the card folder) once the job has the clip (spec 3.4). Report key
```

`windows/src/Offload.Core/Media.cs`, řádky 203–206 — najít:

```csharp
    /// How much of the image the frame covers (0.9 = 90 %, i.e. 10 % overscan around it).
    [JsonPropertyName("frameScale")] public double FrameScale { get; set; } = 1.0;
    /// Own LUT rules (spec 4.2), top one first — used only in Auto mode, before the camera is recognised.
    [JsonIgnore] public List<LutRule> LutRules { get; set; } = [];
```

nahradit:

```csharp
    /// How much of the image the frame covers (0.9 = 90 %, i.e. 10 % overscan around it).
    [JsonPropertyName("frameScale")] public double FrameScale { get; set; } = 1.0;
    /// Blackmagic RAW Film Gen 4 decoded as Gen 5, so the Gen 5 LUT fits (spec 4.2) — off by default until a Gen 4
    /// sample confirms it (R26); absent while off.
    [JsonPropertyName("brawGen4ToGen5"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool BrawGen4ToGen5 { get; set; }
    /// Own LUT rules (spec 4.2), top one first — used only in Auto mode, before the camera is recognised.
    [JsonIgnore] public List<LutRule> LutRules { get; set; } = [];
```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 68–71 — najít:

```csharp
        Settings = settings;
        Tools = tools;
        workDir = Path.Combine(Path.GetTempPath(), "offload-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
```

nahradit:

```csharp
        Settings = settings;
        Tools = tools;
        // Spec 4.2: Film Gen 4 BRAW decoded as Gen 5 (braw-decode --gen5) when switched on.
        if (settings.BrawGen4ToGen5) backends[ClipFormat.Braw] = new BrawBackend(gen5: true);
        workDir = Path.Combine(Path.GetTempPath(), "offload-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
```

`windows/src/Offload.Core/RawBackends.cs`, řádky 70–74 — najít:

```csharp
    [property: JsonPropertyName("gamma")] string? Gamma = null,
    [property: JsonPropertyName("gamut")] string? Gamut = null,
    [property: JsonPropertyName("colorScienceGen")] int? ColorScienceGen = null);

internal sealed class BrawBackend : IRawBackend
```

nahradit:

```csharp
    [property: JsonPropertyName("gamma")] string? Gamma = null,
    [property: JsonPropertyName("gamut")] string? Gamut = null,
    [property: JsonPropertyName("colorScienceGen")] int? ColorScienceGen = null,
    [property: JsonPropertyName("colorScienceGenOriginal")] int? ColorScienceGenOriginal = null);

internal sealed class BrawBackend : IRawBackend
```

`windows/src/Offload.Core/RawBackends.cs`, řádky 83–94 — najít:

```csharp
    readonly Func<string, string?> _findLibrary;
    readonly TimeSpan _infoTimeout;

    /// `findLibrary` (decoder folder → library path) is injectable so tests do not depend on what is installed on the PC;
    /// `infoTimeout` so tests need not wait a minute.
    public BrawBackend(Func<string, string?>? findLibrary = null, TimeSpan? infoTimeout = null)
    {
        _findLibrary = findLibrary ?? BrawLibrary.Find;
        _infoTimeout = infoTimeout ?? DefaultInfoTimeout;
    }

    /// braw-decode alone is not enough: without BlackmagicRawAPI.dll it can only fail.
```

nahradit:

```csharp
    readonly Func<string, string?> _findLibrary;
    readonly TimeSpan _infoTimeout;
    readonly bool _gen5;

    /// `findLibrary` (decoder folder → library path) is injectable so tests do not depend on what is installed on the PC;
    /// `infoTimeout` so tests need not wait a minute. `gen5` (proxy.brawGen4ToGen5, spec 4.2): braw-decode gets --gen5.
    public BrawBackend(Func<string, string?>? findLibrary = null, TimeSpan? infoTimeout = null, bool gen5 = false)
    {
        _findLibrary = findLibrary ?? BrawLibrary.Find;
        _infoTimeout = infoTimeout ?? DefaultInfoTimeout;
        _gen5 = gen5;
    }

    /// --gen5 first, as the decoder's usage line lists it.
    List<string> Args(params string[] rest) => _gen5 ? ["--gen5", .. rest] : [.. rest];

    /// braw-decode alone is not enough: without BlackmagicRawAPI.dll it can only fail.
```

`windows/src/Offload.Core/RawBackends.cs`, řádky 112–115 — najít:

```csharp
        Gamut = string.IsNullOrEmpty(i.Gamut) ? null : i.Gamut,
        ColorScienceGen = i.ColorScienceGen,
    };

```

nahradit:

```csharp
        Gamut = string.IsNullOrEmpty(i.Gamut) ? null : i.Gamut,
        ColorScienceGen = i.ColorScienceGen,
        ColorScienceGenOriginal = i.ColorScienceGenOriginal,
    };

```

`windows/src/Offload.Core/RawBackends.cs`, řádky 135–139 — najít:

```csharp
        try
        {
            output = ToolProcess.Run(tools.BrawDecoder!, ["--info", "--scale", scale, path], timeout: _infoTimeout);
        }
        catch (ToolError e)
```

nahradit:

```csharp
        try
        {
            output = ToolProcess.Run(tools.BrawDecoder!, ["--info", .. Args("--scale", scale, path)], timeout: _infoTimeout);
        }
        catch (ToolError e)
```

`windows/src/Offload.Core/RawBackends.cs`, řádky 174–178 — najít:

```csharp
        var scale = Scale(info.Height ?? 2160, target);
        var dims = Info(path, scale, tools);
        var args = new List<string> { "--scale", scale };
        if (singleFrameAt is double t)
        {
```

nahradit:

```csharp
        var scale = Scale(info.Height ?? 2160, target);
        var dims = Info(path, scale, tools);
        var args = Args("--scale", scale);
        if (singleFrameAt is double t)
        {
```


- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~Braw|FullyQualifiedName~LutResolverTests"`
Expected: PASS (`BrawSampleTests` se bez `OFFLOAD_BRAW_SAMPLE` přeskočí). Pak celá sada (`-warnaserror`, vše zelené, 1096 testů: 1088 úspěšných + 8 přeskočených).

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 22–23 — najít:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 7 — BRAW Film Gen 4 → Gen 5 (R26, výchozí vypnuto, **ověří až vzorek .braw Gen 4**): `braw-decode --gen5` (pro `--info` i snímky) naklonuje atributy klipu, nastaví `ColorScienceGen` = 5 a přečte ho zpět, snímky dekóduje s klonem; `--info --gen5` vrátí účinné hodnoty + `colorScienceGenOriginal`; klip Gen 5 beze změny, SDK, které 5 nepřijme → varování na stderr a dekódování jako dosud; `proxy.brawGen4ToGen5` → renderer předá `--gen5`, `LutResolver` dá účinné Gen 5 LUT Gen 5 („Blackmagic RAW: Film Gen 4 → Gen 5 (převod SDK)“); v reportu `clips[].info.colorScienceGenOriginal`; volba v sekci Dekodéry „Blackmagic RAW Film Gen 4 převést na Gen 5 (použije se LUT Gen 5)“ + „ověří až vzorek .braw Gen 4“, CLI `--braw-gen5`
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Windows: braw-decode --gen5 (BRAW Film Gen 4 decoded as Gen 5, off by default)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.BrawDecode/Options.cs windows/src/Offload.BrawDecode/InfoJson.cs windows/src/Offload.BrawDecode/BrawApi.cs windows/src/Offload.BrawDecode/Decoder.cs windows/src/Offload.BrawDecode/Program.cs windows/src/Offload.Core/RawBackends.cs windows/src/Offload.Core/Media.cs windows/src/Offload.Core/Luts.cs windows/src/Offload.Core/ProxyRenderer.cs windows/src/Offload.Cli/StageTwoOptions.cs windows/src/Offload.App/ProxyOptionsViewModel.cs windows/src/Offload.App/SetupPanel.xaml windows/tests/Offload.Core.Tests/BrawGen5Tests.cs windows/tests/Offload.Core.Tests/BrawSampleTests.cs
git commit -F .git/COMMIT_MSG_PLAN8
```

---

### Task 8: Oznámení — jádro (`JobOutcome`, texty, `NtfyClient`)

Spec §4.3 (tabulka výsledků, ntfy R20), §5.6 (soukromí), §6.3 úkol 7. Čisté funkce nad `JobStatus` + reportem a klient ntfy (`HttpClient`, JSON POST na `<server>/`, limit 10 s, jeden pokus, nikdy výjimka). Testy jen proti `LocalHttp` (`HttpListener` na `127.0.0.1` a volném portu) — žádné skutečné síťové volání.

**Files:**
- Create: `windows/src/Offload.Core/Notify/JobOutcome.cs`
- Create: `windows/src/Offload.Core/Notify/NtfyClient.cs`
- Test: `windows/tests/Offload.Core.Tests/LocalHttp.cs` (nový, sdílí ho úkol 10), `windows/tests/Offload.Core.Tests/NotifyTests.cs` (nový: `JobOutcomeTests`, `NtfyClientTests`)
- Modify: `docs/VYVOJ-WINDOWS.md:22-23`

**Interfaces:**
- Consumes: `JobStatus`, `JobPhase`, `OffloadReport` (`Success`, `Verified`, `FailedFiles`, `Destinations`), `Msg.NothingNew`, `Texts.Plural`, `NtfySettings` (úkol 1), `OffloadVersion.String`.
- Produces: `enum OutcomeKind { Done, Attention, Error, Ejectable }`; `sealed record JobOutcome(OutcomeKind Kind, string Title, string Message, string PhoneMessage)` (`Priority`, `Tags`, `ForPhone`); `static class JobOutcomes` — `Final(string card, JobStatus, OffloadReport?) → JobOutcome?`, `CopyDone(string card, JobStatus, bool proxies) → JobOutcome?`; `sealed record NtfyResult(bool Ok, string? Error)`; `static class NtfyClient` — `DefaultTimeout`, `TopicProblem(string?)`, `ServerProblem(string?)`, `ParseUrl(string) → (string Server, string Topic)?`, `SendAsync(string server, string topic, string title, string message, int priority, IReadOnlyList<string> tags, TimeSpan? timeout = null, CancellationToken ct = default) → Task<NtfyResult>`, `SendOutcomeAsync(NtfySettings, JobOutcome?, Action<string>? log, TimeSpan? timeout = null) → Task`, `SendTestAsync(NtfySettings, TimeSpan? timeout = null)`; testovací `LocalHttp` (`Url`, `Requests`, `FreePort()`, `Reply(ctx, status, body)`).

- [ ] **Step 1: Padající testy**

Vytvořit `windows/tests/Offload.Core.Tests/LocalHttp.cs`:

```csharp
using System.Net;
using System.Net.Sockets;

namespace Offload.Core.Tests;

/// A tiny HTTP server on 127.0.0.1 and a free port, for tests of ntfy and the update check — no real network.
/// `handle` answers each request (default: 200 with an empty body); every request is recorded.
public sealed class LocalHttp : IDisposable
{
    public sealed record Request(string Method, string Path, string? ContentType, string? UserAgent, byte[] Body);

    readonly HttpListener listener = new();
    readonly Func<HttpListenerContext, Task> handle;
    readonly List<Request> requests = [];
    readonly CancellationTokenSource stop = new();

    public string Url { get; }

    public LocalHttp(Func<HttpListenerContext, Task>? handle = null)
    {
        this.handle = handle ?? (c => { c.Response.StatusCode = 200; c.Response.Close(); return Task.CompletedTask; });
        Url = $"http://127.0.0.1:{FreePort()}/";
        listener.Prefixes.Add(Url);
        listener.Start();
        _ = Task.Run(Loop);
    }

    public IReadOnlyList<Request> Requests
    {
        get { lock (requests) return [.. requests]; }
    }

    /// A port nothing listens on now (another process may take it — rare enough for a test).
    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext c;
            try { c = await listener.GetContextAsync(); }
            catch (Exception) { return; }   // stopped
            using var body = new MemoryStream();
            await c.Request.InputStream.CopyToAsync(body);
            lock (requests) requests.Add(new Request(c.Request.HttpMethod, c.Request.Url!.AbsolutePath, c.Request.ContentType, c.Request.UserAgent, body.ToArray()));
            _ = Task.Run(async () =>
            {
                try { await handle(c); }
                catch (Exception) { /* the client went away */ }
            });
        }
    }

    /// Answers with `status` and `body` (UTF-8).
    public static async Task Reply(HttpListenerContext c, int status, string body)
    {
        c.Response.StatusCode = status;
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        await c.Response.OutputStream.WriteAsync(bytes);
        c.Response.Close();
    }

    public void Dispose()
    {
        stop.Cancel();
        try { listener.Stop(); listener.Close(); }
        catch (Exception) { }
    }
}
```

Vytvořit `windows/tests/Offload.Core.Tests/NotifyTests.cs`:

```csharp
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Offload.Core.Notify;

namespace Offload.Core.Tests;

/// Spec 4.3: the outcome table, the texts (no paths on the phone) and the ntfy client against a local HTTP server.
public class JobOutcomeTests
{
    static OffloadReport Report(bool verified = true, string? destError = null, string card = @"D:\Zaloha\2026-10-06\A001")
    {
        var r = Reports.Sample(card, verified, destError);
        return r;
    }

    static JobStatus Status(JobPhase phase, int proxiesDone = 0, int proxiesFailed = 0, string? warning = null, string? error = null, string? note = null,
        bool safe = true) =>
        new() { Phase = phase, ProxiesDone = proxiesDone, ProxiesFailed = proxiesFailed, Warning = warning, Error = error, Note = note, SafeToEject = safe, CopySucceeded = true };

    [Fact]
    public void Done_VerifiedOnDisks_WithProxies()
    {
        var o = JobOutcomes.Final("A001", Status(JobPhase.Done, proxiesDone: 24), Report())!;
        Assert.Equal((OutcomeKind.Done, "Offload: A001 hotovo", "Ověřeno na 1 disku. Proxy: 24 hotovo."), (o.Kind, o.Title, o.Message));
        Assert.Equal(o.Message, o.PhoneMessage);
        Assert.Equal((3, "white_check_mark"), (o.Priority, o.Tags.Single()));
        Assert.Equal("Ověřeno na 1 disku.", JobOutcomes.Final("A001", Status(JobPhase.Done), Report())!.Message);
    }

    [Fact]
    public void Attention_Cases()
    {
        var unverified = JobOutcomes.Final("A001", Status(JobPhase.Done), Report(verified: false))!;
        Assert.Equal((OutcomeKind.Attention, "Offload: A001 — vyžaduje pozornost", "Kopie je hotová, ale NEOVĚŘENÁ."), (unverified.Kind, unverified.Title, unverified.Message));
        Assert.Equal((4, "warning"), (unverified.Priority, unverified.Tags.Single()));
        Assert.Equal("Kopie je ověřená, ale 2 proxy selhaly.", JobOutcomes.Final("A001", Status(JobPhase.Done, proxiesFailed: 2), Report())!.Message);
        Assert.Equal("Kopie je ověřená, ale zpracování po kopii selhalo.", JobOutcomes.Final("A001", Status(JobPhase.Failed, error: "x"), Report())!.Message);

        var space = Msg.ProxiesSkippedForSpace("Zaloha (D:)", 30_000_000_000, 12_000_000_000);
        var skipped = JobOutcomes.Final("A001", Status(JobPhase.Done, warning: space), Report())!;
        Assert.Equal(OutcomeKind.Attention, skipped.Kind);
        Assert.Equal("Kopie je ověřená, ale: " + space, skipped.Message);
        Assert.Equal("Kopie je ověřená, ale vyžaduje pozornost — podrobnosti v aplikaci.", skipped.PhoneMessage);
    }

    [Fact]
    public void Error_FailedFiles_OrBeforeTheCopy()
    {
        var failed = JobOutcomes.Final("A001", Status(JobPhase.Done), Report(destError: "write failed"))!;
        Assert.Equal((OutcomeKind.Error, "Offload: A001 — CHYBA", "1 soubor selhal — karta NENÍ zálohovaná."), (failed.Kind, failed.Title, failed.Message));
        Assert.Equal((5, "rotating_light"), (failed.Priority, failed.Tags.Single()));
        var preflight = JobOutcomes.Final("A001", Status(JobPhase.Failed, error: @"Cíl nenalezen: D:\Zaloha — je disk připojený?"), null)!;
        Assert.Equal(@"Cíl nenalezen: D:\Zaloha — je disk připojený?", preflight.Message);   // Windows: the error itself
        Assert.Equal("Chyba před kopírováním — karta NENÍ zálohovaná.", preflight.PhoneMessage);
    }

    [Fact]
    public void Cancelled_NoNotification_NothingNew_Done()
    {
        Assert.Null(JobOutcomes.Final("A001", Status(JobPhase.Cancelled), Report()));
        var nothing = JobOutcomes.Final("A001", Status(JobPhase.Done, note: Msg.NothingNew), null)!;
        Assert.Equal((OutcomeKind.Done, Msg.NothingNew, Msg.NothingNew), (nothing.Kind, nothing.Message, nothing.PhoneMessage));
        var unverifiedEarlier = JobOutcomes.Final("A001", Status(JobPhase.Done, note: Msg.NothingNew, warning: Msg.UnverifiedEarlier(5)), null)!;
        Assert.Equal(OutcomeKind.Attention, unverifiedEarlier.Kind);
    }

    [Fact]
    public void Ejectable_OnlyWhileStageTwoRuns_WindowsOnly()
    {
        var e = JobOutcomes.CopyDone("A001", Status(JobPhase.Proxies), proxies: true)!;
        Assert.Equal((OutcomeKind.Ejectable, "Offload: A001 — kartu lze vysunout", "Proxy se ještě renderují."), (e.Kind, e.Title, e.Message));
        Assert.False(e.ForPhone);
        Assert.Null(JobOutcomes.CopyDone("A001", Status(JobPhase.Done), proxies: true));
        Assert.Null(JobOutcomes.CopyDone("A001", Status(JobPhase.Analyzing, safe: false), proxies: false));
    }

    // R20 / spec 5.6: the phone gets no path, no file name: no "\" and no drive letter "X:" in the title or the text.
    [Fact]
    public void PhoneTexts_NeverAPath()
    {
        var statuses = new (JobStatus Status, OffloadReport? Report)[]
        {
            (Status(JobPhase.Done, proxiesDone: 3), Report()),
            (Status(JobPhase.Done), Report(verified: false)),
            (Status(JobPhase.Done, warning: Msg.ProxiesSkippedForSpace("Zaloha (D:)", 1, 1)), Report()),
            (Status(JobPhase.Done, warning: @"kopie do E:\Proxy selhala"), Report()),
            (Status(JobPhase.Done), Report(destError: @"write failed D:\x")),
            (Status(JobPhase.Failed, error: @"Zdroj nenalezen: E:\"), null),
            (Status(JobPhase.Done, note: Msg.NothingNew), null),
        };
        foreach (var (status, report) in statuses)
        {
            var o = JobOutcomes.Final("A001", status, report)!;
            foreach (var text in new[] { o.Title, o.PhoneMessage })
            {
                Assert.DoesNotContain("\\", text);
                Assert.DoesNotMatch(@"\b[A-Za-z]:", Regex.Replace(text, "^Offload: ", ""));
            }
        }
    }
}

public class NtfyClientTests
{
    [Fact]
    public async Task Send_JsonShape_Utf8_Priority_NoPathInUrl()
    {
        using var server = new LocalHttp();
        var o = new JobOutcome(OutcomeKind.Attention, "Offload: A001 — vyžaduje pozornost", "x", "Kopie je ověřená, ale 2 proxy selhaly.");

        var r = await NtfyClient.SendAsync(server.Url.TrimEnd('/'), "film-x_7", o.Title, o.PhoneMessage, o.Priority, o.Tags);

        Assert.Equal(new NtfyResult(true, null), r);
        var req = Assert.Single(server.Requests);
        Assert.Equal(("POST", "/"), (req.Method, req.Path));
        Assert.Equal("application/json; charset=utf-8", req.ContentType);
        Assert.StartsWith("Offload/", req.UserAgent);
        using var doc = JsonDocument.Parse(req.Body);
        var root = doc.RootElement;
        Assert.Equal("film-x_7", root.GetProperty("topic").GetString());
        Assert.Equal("Offload: A001 — vyžaduje pozornost", root.GetProperty("title").GetString());
        Assert.Equal("Kopie je ověřená, ale 2 proxy selhaly.", root.GetProperty("message").GetString());
        Assert.Equal(4, root.GetProperty("priority").GetInt32());
        Assert.Equal("warning", root.GetProperty("tags")[0].GetString());
        Assert.Contains("ověřená", Encoding.UTF8.GetString(req.Body));   // raw UTF-8, not only escapes
    }

    [Fact]
    public async Task ServerError_IsAResult_NotAnException()
    {
        using var server = new LocalHttp(c => LocalHttp.Reply(c, 429, "slow down"));
        var r = await NtfyClient.SendAsync(server.Url, "t", "a", "b", 3, []);
        Assert.False(r.Ok);
        Assert.StartsWith("Server odpověděl 429", r.Error);
    }

    // Review focus 3: a server that never answers — the send gives up after its limit (10 s; here 300 ms).
    [Fact]
    public async Task SilentServer_GivesUpAfterTheLimit()
    {
        using var server = new LocalHttp(_ => Task.Delay(Timeout.Infinite));
        var sw = Stopwatch.StartNew();
        var r = await NtfyClient.SendAsync(server.Url, "t", "a", "b", 3, [], timeout: TimeSpan.FromMilliseconds(300));
        Assert.False(r.Ok);
        Assert.Equal("Server neodpověděl do 0.3 s.", r.Error!.Replace(',', '.'));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
    }

    [Fact]
    public async Task NothingListening_IsAResult()
    {
        var r = await NtfyClient.SendAsync($"http://127.0.0.1:{LocalHttp.FreePort()}", "t", "a", "b", 3, [], timeout: TimeSpan.FromSeconds(5));
        Assert.False(r.Ok);
        Assert.NotNull(r.Error);
    }

    // Review focus 3: a TLS failure (https to a server that speaks plain HTTP) is a result too, never an exception.
    [Fact]
    public async Task TlsFailure_IsAResult()
    {
        using var server = new LocalHttp();
        var r = await NtfyClient.SendAsync(server.Url.Replace("http://", "https://"), "t", "a", "b", 3, [], timeout: TimeSpan.FromSeconds(5));
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    // Review focus 3: a topic with diacritics never leaves the PC (ntfy would refuse it) — said in Czech.
    [Theory]
    [InlineData("můj-kanál", "Téma smí mít jen písmena bez diakritiky, číslice, - a _ (nejvýš 64 znaků).")]
    [InlineData("", "Téma není vyplněné.")]
    [InlineData("a/b", "Téma smí mít jen písmena bez diakritiky, číslice, - a _ (nejvýš 64 znaků).")]
    public async Task BadTopic_RefusedLocally(string topic, string error)
    {
        using var server = new LocalHttp();
        var r = await NtfyClient.SendAsync(server.Url, topic, "a", "b", 3, []);
        Assert.Equal(new NtfyResult(false, error), r);
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData("https://ntfy.sh/muj-tajny-kanal", "https://ntfy.sh", "muj-tajny-kanal")]
    [InlineData("https://ntfy.example.com/sub/kanal_1/", "https://ntfy.example.com/sub", "kanal_1")]
    public void ParseUrl_ServerAndTopic(string url, string server, string topic) =>
        Assert.Equal((server, topic), NtfyClient.ParseUrl(url));

    [Theory]
    [InlineData("ntfy.sh/kanal")]
    [InlineData("https://ntfy.sh")]
    [InlineData("https://ntfy.sh/kanál")]
    [InlineData("ftp://x/kanal")]
    public void ParseUrl_Bad_IsNull(string url) => Assert.Null(NtfyClient.ParseUrl(url));

    // The job never waits: the phone notification is started and the caller goes on; its result goes to the log.
    [Fact]
    public async Task SendOutcome_DoesNotHoldTheCaller_LogsTheResult()
    {
        using var server = new LocalHttp(_ => Task.Delay(Timeout.Infinite));
        var settings = new NtfySettings { Enabled = true, Server = server.Url, Topic = "t" };
        var log = new List<string>();
        var outcome = new JobOutcome(OutcomeKind.Done, "Offload: A001 hotovo", "x", "Ověřeno na 2 discích.");

        var sw = Stopwatch.StartNew();
        var send = NtfyClient.SendOutcomeAsync(settings, outcome, m => { lock (log) log.Add(m); }, TimeSpan.FromMilliseconds(500));
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(400), sw.Elapsed.ToString());
        Assert.False(send.IsCompleted);
        await send;
        lock (log) Assert.Equal("ntfy: Server neodpověděl do 0.5 s.", Assert.Single(log).Replace(',', '.'));

        await NtfyClient.SendOutcomeAsync(new NtfySettings { Enabled = false, Server = server.Url, Topic = "t" }, outcome, log.Add);
        await NtfyClient.SendOutcomeAsync(settings, outcome with { Kind = OutcomeKind.Ejectable }, log.Add);
        Assert.Single(server.Requests);   // off, and "kartu lze vysunout": nothing sent
    }
}
```


- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~JobOutcomeTests|FullyQualifiedName~NtfyClientTests"`
Expected: FAIL — build testů selže (CS0246: `JobOutcome`, `JobOutcomes`, `NtfyClient`, `NtfyResult` neexistují).

- [ ] **Step 3: Implementace**

Vytvořit `windows/src/Offload.Core/Notify/JobOutcome.cs`:

```csharp
using Offload.Core.Desktop;

namespace Offload.Core.Notify;

/// How a job ended, for a notification (spec 4.3).
public enum OutcomeKind { Done, Attention, Error, Ejectable }

/// A notification: `Title` / `Message` for Windows (may name a disk or carry the error text), `PhoneMessage` for ntfy —
/// only the card name, the result and counts: never a path, a file name or the PC's name (R20, spec 5.6).
public sealed record JobOutcome(OutcomeKind Kind, string Title, string Message, string PhoneMessage)
{
    /// ntfy priority: done 3, attention 4, error 5.
    public int Priority => Kind switch { OutcomeKind.Error => 5, OutcomeKind.Attention => 4, _ => 3 };

    /// ntfy tags (emoji): white_check_mark / warning / rotating_light.
    public IReadOnlyList<string> Tags => Kind switch
    {
        OutcomeKind.Error => ["rotating_light"],
        OutcomeKind.Attention => ["warning"],
        _ => ["white_check_mark"],
    };

    /// "Kartu lze vysunout" is a Windows notification only (spec 4.3), not sent to the phone.
    public bool ForPhone => Kind != OutcomeKind.Ejectable;
}

/// The table of spec 4.3 as pure functions over the job's status and report.
public static class JobOutcomes
{
    /// The notification at the end of a job; null when it was cancelled (no notification).
    public static JobOutcome? Final(string card, JobStatus status, OffloadReport? report)
    {
        if (status.Phase == JobPhase.Cancelled) return null;
        // An append with nothing new ends Done without a report (its note says so).
        if (report == null && status.Phase == JobPhase.Done)
        {
            var note = status.Note ?? Msg.NothingNew;
            return status.Warning != null
                ? Attention(card, $"{note} {FirstLine(status.Warning)}", $"{note} Vyžaduje pozornost — podrobnosti v aplikaci.")
                : new JobOutcome(OutcomeKind.Done, $"Offload: {card} hotovo", note, note == Msg.NothingNew ? note : "Hotovo.");
        }
        if (report == null)
            return Error(card, status.Error ?? "Kopie se nepodařila.", "Chyba před kopírováním — karta NENÍ zálohovaná.");
        if (!report.Success)
        {
            var failed = $"{report.FailedFiles.Count} {Texts.Plural(report.FailedFiles.Count, "soubor selhal", "soubory selhaly", "souborů selhalo")} — karta NENÍ zálohovaná.";
            return Error(card, failed, failed);
        }
        if (!report.Verified) return Attention(card, "Kopie je hotová, ale NEOVĚŘENÁ.", "Kopie je hotová, ale NEOVĚŘENÁ.");
        if (status.ProxiesFailed > 0)
        {
            var proxies = $"Kopie je ověřená, ale {status.ProxiesFailed} {Texts.Plural(status.ProxiesFailed, "proxy selhala", "proxy selhaly", "proxy selhalo")}.";
            return Attention(card, proxies, proxies);
        }
        if (status.Phase == JobPhase.Failed)
            return Attention(card, "Kopie je ověřená, ale zpracování po kopii selhalo.", "Kopie je ověřená, ale zpracování po kopii selhalo.");
        if (status.Warning != null)
            return Attention(card, $"Kopie je ověřená, ale: {FirstLine(status.Warning)}", "Kopie je ověřená, ale vyžaduje pozornost — podrobnosti v aplikaci.");
        int disks = report.Destinations.Count;
        var done = $"Ověřeno na {disks} {Texts.Plural(disks, "disku", "discích", "discích")}." + (status.ProxiesDone > 0 ? $" Proxy: {status.ProxiesDone} hotovo." : "");
        return new JobOutcome(OutcomeKind.Done, $"Offload: {card} hotovo", done, done);
    }

    /// At the end of stage 1 when the card is safe to eject and stage 2 still runs (Windows only); else null.
    public static JobOutcome? CopyDone(string card, JobStatus status, bool proxies) =>
        status.SafeToEject && !status.Phase.IsFinished()
            ? new JobOutcome(OutcomeKind.Ejectable, $"Offload: {card} — kartu lze vysunout",
                proxies ? "Proxy se ještě renderují." : "Klipy se ještě zpracovávají.", "")
            : null;

    static JobOutcome Attention(string card, string message, string phone) =>
        new(OutcomeKind.Attention, $"Offload: {card} — vyžaduje pozornost", message, phone);

    static JobOutcome Error(string card, string message, string phone) =>
        new(OutcomeKind.Error, $"Offload: {card} — CHYBA", message, phone);

    static string FirstLine(string text) => text.Split('\n')[0].Trim();
}
```

Vytvořit `windows/src/Offload.Core/Notify/NtfyClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Offload.Core.Notify;

/// What a send did: Ok, or the Czech reason (for the test button and the log).
public sealed record NtfyResult(bool Ok, string? Error);

/// A push through an ntfy server (spec 4.3, R20): one JSON POST to `<server>/` — HTTP headers cannot carry Czech —
/// one attempt, 10 s at most, never an exception. Without a token (see the spec: no plain-text secrets).
public static partial class NtfyClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    // One client for the process; the time limit is per request (a CancellationToken), not the client's.
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    // Czech as UTF-8 text in the JSON (not \u escapes).
    static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex TopicPattern();

    /// Why `topic` cannot be used (Czech), or null. ntfy accepts letters without diacritics, digits, "-" and "_".
    public static string? TopicProblem(string? topic) =>
        string.IsNullOrWhiteSpace(topic) ? "Téma není vyplněné."
        : TopicPattern().IsMatch(topic.Trim()) ? null
        : "Téma smí mít jen písmena bez diakritiky, číslice, - a _ (nejvýš 64 znaků).";

    /// Why `server` cannot be used (Czech), or null: an http(s) address.
    public static string? ServerProblem(string? server) =>
        Uri.TryCreate(server?.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) && u.Host.Length > 0
            ? null
            : $"Adresa serveru „{server}“ není http(s) adresa.";

    /// CLI --ntfy <url>: "https://ntfy.sh/muj-tajny-kanal" → ("https://ntfy.sh", "muj-tajny-kanal"); the last path segment
    /// is the topic. Null when the URL has no topic or is no http(s) address.
    public static (string Server, string Topic)? ParseUrl(string url)
    {
        var trimmed = url.Trim().TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        if (slash < 0) return null;
        var (server, topic) = (trimmed[..slash], trimmed[(slash + 1)..]);
        return ServerProblem(server) == null && TopicProblem(topic) == null && !server.EndsWith(':') ? (server, topic) : null;
    }

    /// Sends one notification. `timeout` (tests): instead of 10 s.
    public static async Task<NtfyResult> SendAsync(string server, string topic, string title, string message, int priority,
        IReadOnlyList<string> tags, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (ServerProblem(server) is string badServer) return new(false, badServer);
        if (TopicProblem(topic) is string badTopic) return new(false, badTopic);
        var limit = timeout ?? DefaultTimeout;
        var body = new JsonObject
        {
            ["topic"] = topic.Trim(),
            ["title"] = title,
            ["message"] = message,
            ["priority"] = priority,
            ["tags"] = new JsonArray([.. tags.Select(t => (JsonNode?)JsonValue.Create(t))]),
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(limit);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, server.Trim().TrimEnd('/') + "/")
            {
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body.ToJsonString(Json))),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Headers.UserAgent.ParseAdd($"Offload/{OffloadVersion.String}");
            using var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new(true, null)
                : new(false, $"Server odpověděl {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd() + ".");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, $"Server neodpověděl do {limit.TotalSeconds:0.#} s.");
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or InvalidOperationException or UriFormatException)
        {
            return new(false, e.GetBaseException().Message);
        }
    }

    /// The phone notification of a finished job, when ntfy is on in the job's settings (spec 4.3). Never throws, never
    /// waited for by a job: the result goes to `log` only.
    public static async Task SendOutcomeAsync(NtfySettings settings, JobOutcome? outcome, Action<string>? log, TimeSpan? timeout = null)
    {
        if (!settings.Enabled || outcome is not { ForPhone: true }) return;
        try
        {
            var r = await SendAsync(settings.Server, settings.Topic, outcome.Title, outcome.PhoneMessage, outcome.Priority, outcome.Tags, timeout).ConfigureAwait(false);
            log?.Invoke(r.Ok ? "ntfy: sent" : $"ntfy: {r.Error}");
        }
        catch (Exception e)
        {
            log?.Invoke($"ntfy: {e.Message}");
        }
    }

    /// "Poslat zkušební oznámení".
    public static Task<NtfyResult> SendTestAsync(NtfySettings settings, TimeSpan? timeout = null) =>
        SendAsync(settings.Server, settings.Topic, "Offload: zkušební oznámení", "Oznámení z Offloadu fungují.", 3, ["white_check_mark"], timeout);
}
```


- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~JobOutcomeTests|FullyQualifiedName~NtfyClientTests"`
Expected: PASS (bez internetu; `HttpListener` na `127.0.0.1` nepotřebuje správce). Pak celá sada (`-warnaserror`, vše zelené, 1117 testů: 1109 úspěšných + 8 přeskočených).

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 22–23 — najít:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 8 — jádro oznámení: výsledek úlohy podle tabulky návrhu (Hotovo „Ověřeno na 2 discích. Proxy: 24 hotovo.“, Pozor — neověřeno / selhané proxy / zpracování po kopii selhalo / varování, Chyba — selhané soubory nebo chyba před kopií, „kartu lze vysunout“ jen pro Windows, zrušená bez oznámení); text pro telefon bez cest, jmen souborů a jména PC; ntfy JSON POST na `<server>/` (UTF-8, priorita 3/4/5, tagy), limit 10 s, jeden pokus, chyba jen jako výsledek; téma s diakritikou se neodešle („Téma smí mít jen písmena bez diakritiky, číslice, - a _ …“); testy proti místnímu HTTP serveru (pomalý, nedostupný, TLS, 429)
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Windows: job outcome texts and the ntfy client

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/Notify/JobOutcome.cs windows/src/Offload.Core/Notify/NtfyClient.cs windows/tests/Offload.Core.Tests/LocalHttp.cs windows/tests/Offload.Core.Tests/NotifyTests.cs
git commit -F .git/COMMIT_MSG_PLAN8
```

---

### Task 9: Oznámení Windows (tray), dialog Oznámení, CLI `--ntfy`

Spec §4.3 (Windows R19: `TrayNotifier` přes `Shell_NotifyIconW`, `NIM_ADD` + `NIM_MODIFY` s `NIF_INFO`, `NIIF_INFO/WARNING/ERROR`, okno zpráv přes `HwndSource` jako `VolumeWatcher`, klik vrátí okno, jen když hlavní okno není v popředí, `notify.windows`; UX dialogu; CLI `--ntfy` nebo z `--project`), §6.3 úkol 8. Úloha nese klon `notify` (`JobConfig.Notify`) — oznámení jde podle projektu, ve kterém byla spuštěna. `TrayNotifier` se testuje ručně (ruční test 36); CLI a kontrola dialogu automaticky.

**Files:**
- Modify: `windows/src/Offload.Core/OffloadJob.cs:36-39` (`JobConfig.Notify`)
- Modify: `windows/src/Offload.Core/Notify/NtfyClient.cs:35-38` (`SettingsProblem`)
- Modify: `windows/src/Offload.Core/Messages.cs:154-157`
- Create: `windows/src/Offload.Cli/CliNtfy.cs`
- Modify: `windows/src/Offload.Cli/CopyCommand.cs:4-7, 25-33, 52-55, 109-112, 174-179`
- Modify: `windows/src/Offload.Cli/AppendCommand.cs:19-27, 40-43`
- Create: `windows/src/Offload.App/TrayNotifier.cs`, `windows/src/Offload.App/MainViewModel.Notify.cs`, `windows/src/Offload.App/NotifyWindow.xaml`, `windows/src/Offload.App/NotifyWindow.xaml.cs`
- Modify: `windows/src/Offload.App/MainViewModel.cs:27-30`
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs:193-196, 217-220, 261-264, 274-277`
- Modify: `windows/src/Offload.App/JobItemViewModel.cs:112-115`
- Modify: `windows/src/Offload.App/MainWindow.xaml:31-34`
- Modify: `windows/src/Offload.App/MainWindow.xaml.cs:7-10, 13-16, 59-62`
- Test: `windows/tests/Offload.Core.Tests/CliNtfyTests.cs` (nový)
- Modify: `docs/VYVOJ-WINDOWS.md:22-23`

**Interfaces:**
- Consumes: `JobOutcomes`, `NtfyClient`, `LocalHttp` (úkol 8), `NotifySettings` (úkol 1), `CliProject` (úkol 4), `MainViewModel.ActiveProjectName` (úkol 3).
- Produces: `JobConfig.Notify` (`NotifySettings`, výchozí Windows zapnuto, ntfy vypnuto); `NtfyClient.SettingsProblem(NtfySettings) → string?`; `Msg.BadNtfyUrl(string)`; `static class CliNtfy` — `Create() → Option<string?>` (`--ntfy`), `Settings(ParseResult, Option<string?>, SettingsProfile?) → NotifySettings`; `CopyCommand.Ntfy(…)` (private); `TrayNotifier(Window)` — `Attach()`, `WindowInFront`, `Show(JobOutcome)`, `event Activated`, `Dispose()`; `MainViewModel.ShowNotification`, `WindowInFront`, `EditNotifyDialog`, `NotifySettingsCommand`; `JobItemViewModel.CardName`; `NotifyWindow.Edit(Window, NotifySettings copy, string? project) → NotifySettings?`, `NotifyOptionsViewModel`.

- [ ] **Step 1: Padající testy**

Vytvořit `windows/tests/Offload.Core.Tests/CliNtfyTests.cs`:

```csharp
using System.Text.Json;
using Offload.Cli;
using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.Core.Tests;

/// Spec 4.3 CLI: `--ntfy <url>` or the project's ntfy — the result goes to a local HTTP server here, never the internet.
[Collection("Console")]
public class CliNtfyTests
{
    static (int Exit, string Out, string Err) Run(string? settingsDir, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var oldFactory = CopyCommand.JobFactory;
        var oldPath = CliProject.SettingsPath;
        Console.SetOut(so);
        Console.SetError(se);
        if (settingsDir != null) CliProject.SettingsPath = () => Path.Combine(settingsDir, "settings.json");
        CopyCommand.JobFactory = c => new OffloadJob(c, AppendFixtures.Options());
        try { return (CopyCommand.Create().Parse(args).Invoke(), so.ToString(), se.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = oldFactory;
            CliProject.SettingsPath = oldPath;
        }
    }

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 1_000, 1);
        return card;
    }

    static JsonElement Body(LocalHttp server) => JsonDocument.Parse(Assert.Single(server.Requests).Body).RootElement.Clone();

    [Fact]
    public void Ntfy_AfterTheCopy_TopicFromTheUrl()
    {
        using var dir = new TempDir();
        using var server = new LocalHttp();

        var r = Run(null, Card(dir), "-d", dir.Sub("d1"), "--no-history", "--no-analyze", "--ntfy", server.Url + "kanal-1");

        Assert.Equal(0, r.Exit);
        var body = Body(server);
        Assert.Equal("kanal-1", body.GetProperty("topic").GetString());
        Assert.Equal("Offload: A001 hotovo", body.GetProperty("title").GetString());
        Assert.Equal("Ověřeno na 1 disku.", body.GetProperty("message").GetString());
        Assert.Contains("ntfy: sent", r.Err);
    }

    [Fact]
    public void BadUrl_Exit1_Czech_NothingCopied()
    {
        using var dir = new TempDir();
        var d1 = dir.Sub("d1");
        var r = Run(null, Card(dir), "-d", d1, "--no-history", "--ntfy", "https://ntfy.sh/kanál");
        Assert.Equal(1, r.Exit);
        Assert.Contains("Chyba: Adresa ntfy „https://ntfy.sh/kanál“ nejde použít", r.Err);
        Assert.Empty(Directory.GetDirectories(d1));
    }

    // A refusal before the copy is announced too (a script nobody watches), without the path in the text.
    [Fact]
    public void RefusedBeforeTheCopy_Announced()
    {
        using var dir = new TempDir();
        using var server = new LocalHttp();
        var r = Run(null, Card(dir), "-d", Path.Combine(dir.Path, "missing"), "--no-history", "--ntfy", server.Url + "k");
        Assert.Equal(1, r.Exit);
        var body = Body(server);
        Assert.Equal("Offload: A001 — CHYBA", body.GetProperty("title").GetString());
        Assert.Equal("Chyba před kopírováním — karta NENÍ zálohovaná.", body.GetProperty("message").GetString());
        Assert.Equal(5, body.GetProperty("priority").GetInt32());
    }

    // A refusal before the plan exists: the card is named like the engine names it — never the source path.
    [Fact]
    public void RefusedBeforeThePlan_CardNamedNotPathed()
    {
        using var dir = new TempDir();
        using var server = new LocalHttp();
        var r = Run(null, Path.Combine(dir.Path, "B002"), "-d", dir.Sub("d1"), "--no-history", "--ntfy", server.Url + "k");   // no such source
        Assert.Equal(1, r.Exit);
        Assert.Equal("Offload: B002 — CHYBA", Body(server).GetProperty("title").GetString());
    }

    // --project with ntfy on sends there; --ntfy on the command line wins; without either nothing is sent.
    [Fact]
    public void ProjectNtfy_AndTheOptionWins()
    {
        using var dir = new TempDir();
        using var project = new LocalHttp();
        using var written = new LocalHttp();
        var folder = dir.Sub("appdata");
        var s = new AppSettings();
        var p = Projects.SaveAs(s, "Film X");
        p.Notify.Ntfy = new NtfySettings { Enabled = true, Server = project.Url, Topic = "film-x" };
        SettingsStore.Save(s, Path.Combine(folder, "settings.json"));

        Assert.Equal(0, Run(folder, Card(dir), "--project", "Film X", "-d", dir.Sub("d1"), "--no-history", "--no-analyze").Exit);
        Assert.Equal("film-x", Body(project).GetProperty("topic").GetString());

        Assert.Equal(0, Run(folder, Card(dir), "--project", "Film X", "-d", dir.Sub("d2"), "--no-history", "--no-analyze", "--ntfy", written.Url + "jine").Exit);
        Assert.Single(project.Requests);
        Assert.Equal("jine", Body(written).GetProperty("topic").GetString());

        Assert.Equal(0, Run(folder, Card(dir), "-d", dir.Sub("d3"), "--no-history", "--no-analyze").Exit);
        Assert.Single(project.Requests);
        Assert.Single(written.Requests);
    }

    [Fact]
    public void DialogCheck_ServerAndTopic_OnlyWhenOn()
    {
        Assert.Null(NtfyClient.SettingsProblem(new NtfySettings { Enabled = false, Topic = "" }));
        Assert.Equal("Téma není vyplněné.", NtfyClient.SettingsProblem(new NtfySettings { Enabled = true }));
        Assert.Equal("Adresa serveru „ntfy.sh“ není http(s) adresa.", NtfyClient.SettingsProblem(new NtfySettings { Enabled = true, Server = "ntfy.sh", Topic = "a" }));
        Assert.Null(NtfyClient.SettingsProblem(new NtfySettings { Enabled = true, Topic = "a_b-1" }));
    }

    // The queue keeps a copy (spec 4.1): a job started in a project announces to that project's topic even after a switch.
    [Fact]
    public void JobConfig_NotifyIsTheJobsOwnCopy()
    {
        var s = new AppSettings();
        var p = Projects.SaveAs(s, "Film X");
        p.Notify.Ntfy.Topic = "film-x";
        var config = new JobConfig(@"E:\", []) { Notify = SettingsStore.Clone(s.Active.Notify) };
        Projects.Activate(s, null);
        s.Active.Notify.Ntfy.Topic = "jine";
        p.Notify.Ntfy.Topic = "zmeneno";
        Assert.Equal("film-x", config.Notify.Ntfy.Topic);
        Assert.True(new JobConfig(@"E:\", []).Notify.Windows);
        Assert.False(new JobConfig(@"E:\", []).Notify.Ntfy.Enabled);
    }
}
```


- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CliNtfyTests"`
Expected: FAIL — build testů selže (CS0117 / CS1061: `JobConfig.Notify`, `NtfyClient.SettingsProblem` neexistují; `--ntfy` CLI nezná).

- [ ] **Step 3: Implementace**

`windows/src/Offload.App/JobItemViewModel.cs`, řádky 112–115 — najít:

```csharp

    public string Title => Job.Plan is { Mode: PlanMode.Append } p ? AppendRules.Title(p.CardName) : Job.Plan?.CardName ?? sourceName;
    public string FilesText
    {
```

nahradit:

```csharp

    public string Title => Job.Plan is { Mode: PlanMode.Append } p ? AppendRules.Title(p.CardName) : Job.Plan?.CardName ?? sourceName;
    /// The card folder's name (notifications, spec 4.3).
    internal string CardName => Job.Plan?.CardName ?? sourceName;
    public string FilesText
    {
```

Vytvořit `windows/src/Offload.App/MainViewModel.Notify.cs`:

```csharp
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.App;

/// Spec 4.3: at the end of a job a Windows notification (when the window is not in front) and, when switched on, an
/// ntfy push — both from the job's own copy of the settings (the project it was started in). The sound and the
/// taskbar flash stay as they were. Nástroje → Oznámení… edits the active profile's `notify`.
public sealed partial class MainViewModel
{
    /// Shows a Windows notification (set by the window; it knows whether it is in front).
    public Action<JobOutcome>? ShowNotification { get; set; }
    /// The main window is in front: no Windows notification.
    public Func<bool>? WindowInFront { get; set; }
    /// Opens Nástroje → Oznámení… with a copy of the active profile's settings; returns the edited copy or null.
    public Func<NotifySettings, NotifySettings?>? EditNotifyDialog { get; set; }

    public RelayCommand NotifySettingsCommand { get; private set; } = null!;

    void InitNotify() => NotifySettingsCommand = new RelayCommand(EditNotify, () => !closing);

    void EditNotify()
    {
        if (EditNotifyDialog?.Invoke(SettingsStore.Clone(Settings.Notify)) is not { } edited) return;
        Settings.Notify = edited;
        SettingsChanged();
    }

    /// End of stage 1, stage 2 still running and the card safe to eject: "kartu lze vysunout" (Windows only).
    void NotifyCopyDone(JobItemViewModel item)
    {
        if (closing || !item.Job.Config.Notify.Windows) return;
        if (JobOutcomes.CopyDone(item.CardName, item.Job.Status(), item.Job.Config.Proxy.Enabled) is { } outcome) Windows(outcome);
    }

    /// The job has ended: Windows (when not in front) and the phone (fire and forget — the job never waits for it).
    void NotifyJobEnd(JobItemViewModel item)
    {
        if (closing) return;
        var config = item.Job.Config;
        if (JobOutcomes.Final(item.CardName, item.Job.Status(), item.Job.Report) is not { } outcome) return;
        if (config.Notify.Windows) Windows(outcome);
        var name = item.CardName;
        _ = NtfyClient.SendOutcomeAsync(config.Notify.Ntfy, outcome, m => AppLog.Write($"{name}: {m}"));
    }

    void Windows(JobOutcome outcome)
    {
        if (WindowInFront?.Invoke() == true) return;
        ShowNotification?.Invoke(outcome);
    }
}
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 193–196 — najít:

```csharp
                    Outputs = SettingsStore.Clone(Settings.Outputs),
                    Project = known.Entry.Project,   // the earlier backup's project (its history entry keeps it)
                };
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), AppendRules.Title(known.Entry.CardName),
```

nahradit:

```csharp
                    Outputs = SettingsStore.Clone(Settings.Outputs),
                    Project = known.Entry.Project,   // the earlier backup's project (its history entry keeps it)
                    Notify = SettingsStore.Clone(Settings.Notify),
                };
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), AppendRules.Title(known.Entry.CardName),
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 217–220 — najít:

```csharp
                Outputs = SettingsStore.Clone(Settings.Outputs),
                Project = ActiveProjectName,
            };
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
```

nahradit:

```csharp
                Outputs = SettingsStore.Clone(Settings.Outputs),
                Project = ActiveProjectName,
                Notify = SettingsStore.Clone(Settings.Notify),
            };
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 261–264 — najít:

```csharp
        item.Refresh();
        if (!closing && item.SafeToEject) SystemSounds.Asterisk.Play();
        if (!closing) ReloadHistory();   // the job has just recorded the card: its line says "Zálohováno"
        if (!closing) _ = ProbeFreeSpaceAsync(TimeSpan.FromSeconds(30));   // the disks changed: free space again (ruling I4)
```

nahradit:

```csharp
        item.Refresh();
        if (!closing && item.SafeToEject) SystemSounds.Asterisk.Play();
        NotifyCopyDone(item);   // spec 4.3: "kartu lze vysunout" while stage 2 still runs
        if (!closing) ReloadHistory();   // the job has just recorded the card: its line says "Zálohováno"
        if (!closing) _ = ProbeFreeSpaceAsync(TimeSpan.FromSeconds(30));   // the disks changed: free space again (ruling I4)
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 274–277 — najít:

```csharp
            item.Refresh();
            if (!item.Alerted) Alert(item);
        }
        Raise(nameof(HasFinished));
```

nahradit:

```csharp
            item.Refresh();
            if (!item.Alerted) Alert(item);
            if (item is JobItemViewModel ended) NotifyJobEnd(ended);   // spec 4.3
        }
        Raise(nameof(HasFinished));
```

`windows/src/Offload.App/MainViewModel.cs`, řádky 27–30 — najít:

```csharp
        InitDayReport();
        InitProjects();
    }

```

nahradit:

```csharp
        InitDayReport();
        InitProjects();
        InitNotify();
    }

```

`windows/src/Offload.App/MainWindow.xaml`, řádky 31–34 — najít:

```xml
                    <MenuItem Header="Ukládat report ověření" IsCheckable="True" IsChecked="{Binding SaveVerifyReport}"
                              AutomationProperties.AutomationId="SaveVerifyReportMenuItem" />
                    <MenuItem Header="Denní report…" Command="{Binding DayReportCommand}"
                              AutomationProperties.AutomationId="DayReportMenuItem">
```

nahradit:

```xml
                    <MenuItem Header="Ukládat report ověření" IsCheckable="True" IsChecked="{Binding SaveVerifyReport}"
                              AutomationProperties.AutomationId="SaveVerifyReportMenuItem" />
                    <MenuItem Header="Oznámení…" Command="{Binding NotifySettingsCommand}" AutomationProperties.AutomationId="NotifyMenuItem" />
                    <MenuItem Header="Denní report…" Command="{Binding DayReportCommand}"
                              AutomationProperties.AutomationId="DayReportMenuItem">
```

`windows/src/Offload.App/MainWindow.xaml.cs`, řádky 7–10 — najít:

```csharp
    readonly MainViewModel vm;
    readonly VolumeWatcher watcher = new();

    public MainWindow(MainViewModel vm)
```

nahradit:

```csharp
    readonly MainViewModel vm;
    readonly VolumeWatcher watcher = new();
    readonly TrayNotifier tray;

    public MainWindow(MainViewModel vm)
```

`windows/src/Offload.App/MainWindow.xaml.cs`, řádky 13–16 — najít:

```csharp
        this.vm = vm;
        DataContext = vm;
        vm.MessageRequested += text => MessageBox.Show(this, text, "Offload");
        Closed += (_, _) => vm.SaveNow();
```

nahradit:

```csharp
        this.vm = vm;
        DataContext = vm;
        tray = new TrayNotifier(this);
        tray.Activated += () =>
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        };
        vm.ShowNotification = tray.Show;
        vm.WindowInFront = () => tray.WindowInFront;
        vm.EditNotifyDialog = settings => NotifyWindow.Edit(this, settings, vm.ActiveProjectName);
        Closed += (_, _) => tray.Dispose();
        vm.MessageRequested += text => MessageBox.Show(this, text, "Offload");
        Closed += (_, _) => vm.SaveNow();
```

`windows/src/Offload.App/MainWindow.xaml.cs`, řádky 59–62 — najít:

```csharp
        WindowSizing.FitToWorkArea(this);
        watcher.Attach(this);
        watcher.Arrived += vm.VolumesArrived;
        watcher.Changed += vm.RefreshVolumes;
```

nahradit:

```csharp
        WindowSizing.FitToWorkArea(this);
        watcher.Attach(this);
        tray.Attach();
        watcher.Arrived += vm.VolumesArrived;
        watcher.Changed += vm.RefreshVolumes;
```

Vytvořit `windows/src/Offload.App/NotifyWindow.xaml`:

```xml
<Window x:Class="Offload.App.NotifyWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Oznámení" SizeToContent="Height" Width="480" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Icon="pack://application:,,,/Offload.ico">
    <StackPanel Margin="22,18">
        <TextBlock Text="Oznámení" FontSize="17" FontWeight="SemiBold" />
        <TextBlock Text="{Binding ProfileText}" Style="{StaticResource Caption}" Margin="0,4,0,0" />
        <CheckBox Content="Oznámení Windows" IsChecked="{Binding Windows}" Margin="0,12,0,0" AutomationProperties.AutomationId="NotifyWindows" />
        <TextBlock Text="Jen když okno Offloadu není v popředí; zvuk a blikání zůstávají." Style="{StaticResource Caption}" Margin="24,0,0,0" TextWrapping="Wrap" />
        <CheckBox Content="Posílat na telefon přes ntfy" IsChecked="{Binding NtfyEnabled}" Margin="0,10,0,0" AutomationProperties.AutomationId="NotifyNtfy" />
        <Grid Margin="24,6,0,0" IsEnabled="{Binding NtfyEnabled}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="60" />
                <ColumnDefinition />
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>
            <TextBlock Text="Server" Style="{StaticResource Caption}" VerticalAlignment="Center" />
            <TextBox Grid.Column="1" Text="{Binding Server, UpdateSourceTrigger=PropertyChanged}" Margin="0,2" AutomationProperties.AutomationId="NtfyServer" />
            <TextBlock Grid.Row="1" Text="Téma" Style="{StaticResource Caption}" VerticalAlignment="Center" />
            <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding Topic, UpdateSourceTrigger=PropertyChanged}" Margin="0,2" AutomationProperties.AutomationId="NtfyTopic" />
        </Grid>
        <TextBlock Text="Téma volte nehádatelné — kdo ho zná, oznámení čte. Posílá se jen název karty a výsledek, žádné cesty."
                   Style="{StaticResource Caption}" Margin="24,6,0,0" TextWrapping="Wrap" />
        <StackPanel Orientation="Horizontal" Margin="24,8,0,0">
            <Button Content="Poslat zkušební oznámení" Command="{Binding TestCommand}" Padding="12,5" AutomationProperties.AutomationId="NtfyTest" />
            <TextBlock Text="{Binding TestResult}" VerticalAlignment="Center" Margin="10,0,0,0" TextWrapping="Wrap" MaxWidth="220"
                       AutomationProperties.AutomationId="NtfyTestResult" />
        </StackPanel>
        <TextBlock Text="{Binding Problem}" Style="{StaticResource ErrorText}" Margin="0,8,0,0" TextWrapping="Wrap"
                   Visibility="{Binding Problem, Converter={StaticResource NullToCollapsed}}" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
            <Button Content="OK" Click="Ok_Click" IsDefault="True" MinWidth="80" Padding="12,5" Style="{DynamicResource AccentButtonStyle}"
                    AutomationProperties.AutomationId="NotifyOk" />
            <Button Content="Zrušit" IsCancel="True" MinWidth="80" Padding="12,5" Margin="8,0,0,0" />
        </StackPanel>
    </StackPanel>
</Window>
```

Vytvořit `windows/src/Offload.App/NotifyWindow.xaml.cs`:

```csharp
using System.Windows;
using Offload.Core.Notify;

namespace Offload.App;

/// Nástroje → Oznámení… (spec 4.3). Edits a copy; OK hands it back only when ntfy (if on) has a usable server and topic.
public partial class NotifyWindow : Window
{
    readonly NotifyOptionsViewModel vm;
    NotifySettings? result;

    NotifyWindow(NotifyOptionsViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        DataContext = vm;
    }

    public static NotifySettings? Edit(Window owner, NotifySettings copy, string? project)
    {
        var w = new NotifyWindow(new NotifyOptionsViewModel(copy, project)) { Owner = owner };
        w.ShowDialog();
        return w.result;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (vm.Accept() is not { } settings) return;
        result = settings;
        DialogResult = true;
    }
}

/// The dialog's state. "Poslat zkušební oznámení" runs off the UI thread (await) — the window never waits on the network.
public sealed class NotifyOptionsViewModel : ObservableObject
{
    readonly NotifySettings s;
    string? testResult, problem;
    bool testing;

    public NotifyOptionsViewModel(NotifySettings copy, string? project)
    {
        s = copy;
        ProfileText = project == null ? "Platí bez projektu." : $"Platí pro projekt {project}.";
        TestCommand = new RelayCommand(Test, () => !testing);
    }

    public string ProfileText { get; }
    public RelayCommand TestCommand { get; }

    public bool Windows { get => s.Windows; set { s.Windows = value; Raise(); } }
    public bool NtfyEnabled { get => s.Ntfy.Enabled; set { s.Ntfy.Enabled = value; Problem = null; Raise(); } }
    public string Server { get => s.Ntfy.Server; set { s.Ntfy.Server = value ?? ""; Problem = null; Raise(); } }
    public string Topic { get => s.Ntfy.Topic; set { s.Ntfy.Topic = value ?? ""; Problem = null; Raise(); } }

    public string? TestResult { get => testResult; private set => Set(ref testResult, value); }
    public string? Problem { get => problem; private set => Set(ref problem, value); }

    /// The settings to keep, or null (Problem says why): ntfy on needs an http(s) server and a usable topic.
    public NotifySettings? Accept()
    {
        s.Ntfy.Server = s.Ntfy.Server.Trim();
        s.Ntfy.Topic = s.Ntfy.Topic.Trim();
        if (string.IsNullOrEmpty(s.Ntfy.Server)) s.Ntfy.Server = NtfySettings.DefaultServer;
        Problem = NtfyClient.SettingsProblem(s.Ntfy);
        return Problem == null ? s : null;
    }

    async void Test()
    {
        testing = true;
        TestResult = "Odesílám…";
        try
        {
            var r = await NtfyClient.SendTestAsync(new NtfySettings { Enabled = true, Server = s.Ntfy.Server.Trim(), Topic = s.Ntfy.Topic.Trim() });
            TestResult = r.Ok ? "Odesláno." : $"Nepodařilo se: {r.Error}";
        }
        catch (Exception e)
        {
            TestResult = $"Nepodařilo se: {e.Message}";
        }
        finally
        {
            testing = false;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
}
```

Vytvořit `windows/src/Offload.App/TrayNotifier.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Offload.Core.Notify;

namespace Offload.App;

/// Windows notifications without registration (spec 4.3, R19): an icon in the notification area through
/// Shell_NotifyIconW, added at the first notification and removed when the app closes; each notification is a balloon
/// (NIF_INFO), which Windows 10/11 show as a modern notification and keep in the notification centre. A click on the
/// icon or the notification brings the main window back. Messages arrive at the main window's HwndSource (as in
/// VolumeWatcher). Tested by hand (manual test list) — nothing here touches a disk or the network.
sealed unsafe partial class TrayNotifier : IDisposable
{
    const int CallbackMessage = 0x8000 + 42;   // WM_APP + 42
    const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10;
    const uint NiifInfo = 1, NiifWarning = 2, NiifError = 3, NiifLargeIcon = 0x20;
    const uint NotifyIconVersion4 = 4;
    const int NinSelect = 0x400, NinKeySelect = 0x401, NinBalloonUserClick = 0x405, WmLButtonUp = 0x202;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint TimeoutOrVersion;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ExtractIconEx(string file, int index, nint* large, nint* small, uint count);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string name);

    readonly Window window;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    HwndSource? source;
    nint icon;
    bool added;

    /// The icon or a notification was clicked: show the main window.
    public event Action? Activated;

    public TrayNotifier(Window window) => this.window = window;

    /// After the window has its handle (OnSourceInitialized).
    public void Attach()
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(Hook);
    }

    /// The main window is in front: no notification then (the sound and the taskbar flash are enough).
    public bool WindowInFront => window.IsActive && window.WindowState != WindowState.Minimized;

    public void Show(JobOutcome outcome)
    {
        try
        {
            if (!added && !Add()) return;
            var data = Data(NifInfo);
            Copy(outcome.Title, data.InfoTitle, 64);
            Copy(outcome.Message, data.Info, 256);
            data.InfoFlags = NiifLargeIcon | outcome.Kind switch
            {
                OutcomeKind.Error => NiifError,
                OutcomeKind.Attention => NiifWarning,
                _ => NiifInfo,
            };
            if (!ShellNotifyIcon(NimModify, ref data)) AppLog.Write("tray: the notification was not shown");
        }
        catch (Exception e)
        {
            AppLog.Write($"tray: {e.Message}");
        }
    }

    NotifyIconData Data(uint flags) => new()
    {
        Size = (uint)sizeof(NotifyIconData),
        Window = source?.Handle ?? 0,
        Id = 1,
        Flags = flags,
    };

    bool Add()
    {
        if (source == null) return false;
        if (icon == 0 && Environment.ProcessPath is string exe)
        {
            nint small = 0;
            if (ExtractIconEx(exe, 0, null, &small, 1) > 0) icon = small;
        }
        var data = Data(NifMessage | NifIcon | NifTip);
        data.CallbackMessage = CallbackMessage;
        data.Icon = icon;
        Copy("Offload", data.Tip, 128);
        added = ShellNotifyIcon(NimAdd, ref data);
        if (!added)
        {
            AppLog.Write("tray: the icon could not be added");
            return false;
        }
        data.TimeoutOrVersion = NotifyIconVersion4;
        ShellNotifyIcon(NimSetVersion, ref data);
        return true;
    }

    static void Copy(string text, char* to, int capacity)
    {
        var s = text.Length >= capacity ? text[..(capacity - 2)] + "…" : text;
        for (int i = 0; i < s.Length; i++) to[i] = s[i];
        to[s.Length] = '\0';
    }

    nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: the event is the low word of lParam.
            int e = (int)(lParam & 0xFFFF);
            if (e is NinSelect or NinKeySelect or NinBalloonUserClick or WmLButtonUp) Activated?.Invoke();
            handled = true;
        }
        else if (taskbarCreated != 0 && msg == (int)taskbarCreated && added)
        {
            added = false;   // Explorer restarted: the icon is gone, the next notification adds it again
        }
        return 0;
    }

    public void Dispose()
    {
        try
        {
            if (added)
            {
                var data = Data(0);
                ShellNotifyIcon(NimDelete, ref data);
                added = false;
            }
            if (icon != 0) DestroyIcon(icon);
            icon = 0;
            source?.RemoveHook(Hook);
        }
        catch (Exception e)
        {
            AppLog.Write($"tray: {e.Message}");
        }
    }
}
```

`windows/src/Offload.Cli/AppendCommand.cs`, řádky 19–27 — najít:

```csharp
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (needs --into)." };
        var project = CliProject.Create();
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("append", "Copy only the new files of a card into its earlier backup (same card folders, next ASC MHL generation).");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { into, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory, project }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
```

nahradit:

```csharp
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (needs --into)." };
        var project = CliProject.Create();
        var ntfy = CliNtfy.Create();
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("append", "Copy only the new files of a card into its earlier backup (same card folders, next ASC MHL generation).");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { into, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory, project, ntfy }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
```

`windows/src/Offload.Cli/AppendCommand.cs`, řádky 40–43 — najít:

```csharp
                    RecordHistory = !r.GetValue(noHistory),
                    Project = profile?.Name,
                };
                stageTwo.Apply(r, config, profile);
```

nahradit:

```csharp
                    RecordHistory = !r.GetValue(noHistory),
                    Project = profile?.Name,
                    Notify = CliNtfy.Settings(r, ntfy, profile),
                };
                stageTwo.Apply(r, config, profile);
```

Vytvořit `windows/src/Offload.Cli/CliNtfy.cs`:

```csharp
using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.Cli;

/// `--ntfy <url>` (spec 4.3): the job's end goes to an ntfy topic ("https://ntfy.sh/muj-tajny-kanal" = server + topic).
/// Without it a --project with ntfy on sends there. The CLI shows no Windows notifications.
static class CliNtfy
{
    public static Option<string?> Create() =>
        new("--ntfy") { Description = "Send the result to a phone through ntfy: the server and topic as one URL, e.g. https://ntfy.sh/my-secret-topic (only the card name and the result are sent)." };

    /// The job's notify settings: --ntfy wins, else the project's, else none. ArgumentException (Czech) for a bad URL.
    public static NotifySettings Settings(ParseResult r, Option<string?> ntfy, SettingsProfile? project)
    {
        if (r.GetValue(ntfy) is string url)
        {
            var (server, topic) = NtfyClient.ParseUrl(url) ?? throw new ArgumentException(Msg.BadNtfyUrl(url));
            return new NotifySettings { Windows = false, Ntfy = new NtfySettings { Enabled = true, Server = server, Topic = topic } };
        }
        var s = project != null ? SettingsStore.Clone(project.Notify) : new NotifySettings { Ntfy = new NtfySettings { Enabled = false } };
        s.Windows = false;
        return s;
    }
}
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 4–7 — najít:

```csharp
using Offload.Core.Hardware;
using Offload.Core.History;

namespace Offload.Cli;
```

nahradit:

```csharp
using Offload.Core.Hardware;
using Offload.Core.History;
using Offload.Core.Notify;

namespace Offload.Cli;
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 25–33 — najít:

```csharp
        var hash = new Option<string>("--hash") { Description = "Checksums: xxh64 (default) or both (xxh64 + MD5; MD5 is slower on fast cards).", DefaultValueFactory = _ => "xxh64" };
        var project = CliProject.Create();
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory, hash, project }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
```

nahradit:

```csharp
        var hash = new Option<string>("--hash") { Description = "Checksums: xxh64 (default) or both (xxh64 + MD5; MD5 is slower on fast cards).", DefaultValueFactory = _ => "xxh64" };
        var project = CliProject.Create();
        var ntfy = CliNtfy.Create();
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory, hash, project, ntfy }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 52–55 — najít:

```csharp
                        : HashKinds.TryParseWire(r.GetValue(hash), out var kinds) ? kinds : throw new ArgumentException(Msg.UnknownHash(r.GetValue(hash))),
                    Project = profile?.Name,
                };
                stageTwo.Apply(r, config, profile);
```

nahradit:

```csharp
                        : HashKinds.TryParseWire(r.GetValue(hash), out var kinds) ? kinds : throw new ArgumentException(Msg.UnknownHash(r.GetValue(hash))),
                    Project = profile?.Name,
                    Notify = CliNtfy.Settings(r, ntfy, profile),
                };
                stageTwo.Apply(r, config, profile);
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 109–112 — najít:

```csharp
        {
            Console.Error.WriteLine(Msg.CliError + e.Message);
            return 1;
        }
```

nahradit:

```csharp
        {
            Console.Error.WriteLine(Msg.CliError + e.Message);
            if (!dryRun) Ntfy(config, null, new JobStatus { Phase = JobPhase.Failed, Error = e.Message }, null);   // a refusal before the copy too
            return 1;
        }
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 174–179 — najít:

```csharp
        if (status.Warning != null) Console.WriteLine("Varování: " + status.Warning);
        if (status.Error != null) Console.WriteLine(Msg.CliError + status.Error);
        // "Nic nového" ends Done without a report: that is success (exit 0).
        return status.Phase == JobPhase.Done && (report?.Success == true || (report == null && status.Note != null)) ? 0 : 1;
    }

```

nahradit:

```csharp
        if (status.Warning != null) Console.WriteLine("Varování: " + status.Warning);
        if (status.Error != null) Console.WriteLine(Msg.CliError + status.Error);
        Ntfy(config, plan, status, report);
        // "Nic nového" ends Done without a report: that is success (exit 0).
        return status.Phase == JobPhase.Done && (report?.Success == true || (report == null && status.Note != null)) ? 0 : 1;
    }

    /// Spec 4.3: --ntfy (or the project's ntfy) once the job has ended — at most 10 s; a failure is only a line on stderr.
    static void Ntfy(JobConfig config, OffloadPlan? plan, JobStatus status, OffloadReport? report)
    {
        // Never the source path itself (spec 5.6): the plan's name, the --name, else the name the engine would give.
        string card;
        try { card = plan?.CardName ?? config.CardName ?? Layout.DefaultCardName(config.Source); }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException) { card = ""; }
        if (string.IsNullOrWhiteSpace(card)) card = "karta";
        NtfyClient.SendOutcomeAsync(config.Notify.Ntfy, JobOutcomes.Final(card, status, report), m => Console.Error.WriteLine(m)).GetAwaiter().GetResult();
    }

```

`windows/src/Offload.Core/Messages.cs`, řádky 154–157 — najít:

```csharp
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string RuleLutNotFound(int rule, string name) => $"LUT z pravidla {rule} nenalezen: {name}";
    public static string BadLutRule(string spec) =>
        $"Pravidlo LUTu „{spec}“ nejde přečíst — použijte <podle>=<hodnota>:<lut>, např. camera=FX6:sony-slog3-sgamut3cine (podle: camera, serial, reel, folder; lut: vestavěný, none nebo cesta k .cube).";
```

nahradit:

```csharp
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string RuleLutNotFound(int rule, string name) => $"LUT z pravidla {rule} nenalezen: {name}";
    public static string BadNtfyUrl(string url) =>
        $"Adresa ntfy „{url}“ nejde použít — zadejte server a téma, např. https://ntfy.sh/muj-tajny-kanal (téma jen písmena bez diakritiky, číslice, - a _).";
    public static string BadLutRule(string spec) =>
        $"Pravidlo LUTu „{spec}“ nejde přečíst — použijte <podle>=<hodnota>:<lut>, např. camera=FX6:sony-slog3-sgamut3cine (podle: camera, serial, reel, folder; lut: vestavěný, none nebo cesta k .cube).";
```

`windows/src/Offload.Core/Notify/NtfyClient.cs`, řádky 35–38 — najít:

```csharp
            ? null
            : $"Adresa serveru „{server}“ není http(s) adresa.";

    /// CLI --ntfy <url>: "https://ntfy.sh/muj-tajny-kanal" → ("https://ntfy.sh", "muj-tajny-kanal"); the last path segment
```

nahradit:

```csharp
            ? null
            : $"Adresa serveru „{server}“ není http(s) adresa.";

    /// Why the ntfy settings cannot be kept (the dialog's OK), or null: switched on, they need a server and a topic.
    public static string? SettingsProblem(NtfySettings s) => s.Enabled ? ServerProblem(s.Server) ?? TopicProblem(s.Topic) : null;

    /// CLI --ntfy <url>: "https://ntfy.sh/muj-tajny-kanal" → ("https://ntfy.sh", "muj-tajny-kanal"); the last path segment
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 36–39 — najít:

```csharp
    /// The project the job was started in (spec 4.1), null = "Bez projektu": into history.json and the report.
    public string? Project { get; set; }
}

```

nahradit:

```csharp
    /// The project the job was started in (spec 4.1), null = "Bez projektu": into history.json and the report.
    public string? Project { get; set; }
    /// Where the end of the job is announced (spec 4.3): the copy taken when the job was started.
    public Notify.NotifySettings Notify { get; set; } = new();
}

```


- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CliNtfyTests|FullyQualifiedName~NtfyClientTests|FullyQualifiedName~Cli"`
Expected: PASS. `dotnet build windows\Offload.sln -warnaserror` zkompiluje `TrayNotifier` (P/Invoke přes `LibraryImport`, blittable `NOTIFYICONDATAW`) a `NotifyWindow.xaml`. Pak celá sada (vše zelené, 1124 testů: 1116 úspěšných + 8 přeskočených).

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 22–23 — najít:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 9 — oznámení: Windows přes `Shell_NotifyIconW` (ikona v oznamovací oblasti od prvního oznámení do zavření aplikace, po restartu Průzkumníka znovu; balón info/varování/chyba, který Windows 10/11 ukážou jako oznámení i v Centru oznámení; klik vrátí okno), jen když okno není v popředí — zvuk a blikání zůstávají; „kartu lze vysunout“ po kopii, když se ještě renderuje; ntfy z nastavení úlohy (klon `notify` při startu — přepnutí projektu ho nezmění), úloha na odeslání nečeká; Nástroje → Oznámení… (Windows, ntfy server a téma, „Poslat zkušební oznámení“ mimo UI vlákno, poznámka o nehádatelném tématu; platí pro aktivní projekt); CLI `--ntfy <url>` (nebo ntfy projektu), i pro odmítnutí před kopií, nejvýš 10 s po konci
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Windows: notifications (tray, Oznámení dialog, CLI --ntfy)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/OffloadJob.cs windows/src/Offload.Core/Notify/NtfyClient.cs windows/src/Offload.Core/Messages.cs windows/src/Offload.Cli/CliNtfy.cs windows/src/Offload.Cli/CopyCommand.cs windows/src/Offload.Cli/AppendCommand.cs windows/src/Offload.App/TrayNotifier.cs windows/src/Offload.App/MainViewModel.Notify.cs windows/src/Offload.App/NotifyWindow.xaml windows/src/Offload.App/NotifyWindow.xaml.cs windows/src/Offload.App/MainViewModel.cs windows/src/Offload.App/MainViewModel.Queue.cs windows/src/Offload.App/JobItemViewModel.cs windows/src/Offload.App/MainWindow.xaml windows/src/Offload.App/MainWindow.xaml.cs windows/tests/Offload.Core.Tests/CliNtfyTests.cs
git commit -F .git/COMMIT_MSG_PLAN8
```

---

### Task 10: Kontrola aktualizací

Spec §4.4 (manifest `{version, url, notes}`, zdroj URL nebo cesta, výchozí `null` = vypnuto; `UpdateChecker.CheckAsync(source, currentVersion, clock, state)`; nejvýš 1× za 24 h, stav `update-check.json`, `lastCheck` v budoucnosti → zkontrolovat; 5 s po startu, mimo UI vlákno; HTTP limit 10 s, soubor přes `Task.Run` + `WhenAny` 5 s; `MAJOR.MINOR.PATCH`, přípona za `-` se ignoruje; nic nestahuje; `User-Agent: Offload/<verze>`; UX odkaz v řádku menu + okno + Nástroje → Aktualizace…), R21, §6.3 úkol 9. Hodiny jsou parametr `nowUtc`, stav parametr `statePath` (testy nikdy do `%APPDATA%`).

**Files:**
- Create: `windows/src/Offload.Core/Updates/UpdateChecker.cs`
- Modify: `windows/src/Offload.Core/Desktop/AppSettings.cs:4-7, 69-72, 87-91, 113-116, 214-217`
- Create: `windows/src/Offload.App/MainViewModel.Updates.cs`, `windows/src/Offload.App/UpdateWindow.xaml`, `windows/src/Offload.App/UpdateWindow.xaml.cs`
- Modify: `windows/src/Offload.App/MainViewModel.cs:28-31`
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs:343-346`
- Modify: `windows/src/Offload.App/MainWindow.xaml:13-16, 32-35, 47-50`
- Modify: `windows/src/Offload.App/MainWindow.xaml.cs:23-26`
- Test: `windows/tests/Offload.Core.Tests/UpdateCheckerTests.cs` (nový)
- Modify: `docs/VYVOJ-WINDOWS.md:22-23`

**Interfaces:**
- Consumes: `LocalHttp` (úkol 8), `AppData.Folder`, `NativeIO.Mkdirs/WriteFileReplacing`, `OffloadVersion.String`, `Projects.SaveAs` (úkol 1, v testu).
- Produces: `Offload.Core.Updates` — `UpdateSettings { string? Manifest }`, `sealed record UpdateInfo(string Version, string? Url, string? Notes)`, `enum UpdateStatus { Off, NotDue, UpToDate, NewVersion, Failed }`, `sealed record UpdateResult(UpdateStatus, UpdateInfo?, string? Error)` + `Text(string currentVersion) → string?`, `UpdateState`, `static class UpdateChecker` — `Interval`, `HttpTimeout`, `FileTimeout`, `DefaultStatePath`, `CheckAsync(string? source, string currentVersion, DateTime nowUtc, string statePath, bool force = false, TimeSpan? httpTimeout = null, TimeSpan? fileTimeout = null) → Task<UpdateResult>`, `Parse(string) → UpdateInfo`, `IsNewer(string, string) → bool`, `Parts(string) → (int,int,int)?`, `Plain(string) → string`; `UpdateException`; `AppSettings.Updates` (`updates`, globální); `MainViewModel.UpdatesCommand`, `NewVersionText`, `NewVersion`, `UpdateManifest`, `ShowUpdatesDialog`, `internal Task<string?> CheckUpdatesAsync(bool force)`, `internal OpenNewVersion()`; `UpdateWindow.Show(Window, MainViewModel)`.

- [ ] **Step 1: Padající testy**

Vytvořit `windows/tests/Offload.Core.Tests/UpdateCheckerTests.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using Offload.Core.Desktop;
using Offload.Core.Updates;

namespace Offload.Core.Tests;

/// Spec 4.4, R21: a manifest from a URL or a file, at most once in 24 h, never a download. A local HTTP server and temp
/// files only — and a state file in a temp folder, never %APPDATA%.
public class UpdateCheckerTests
{
    static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("0.4.0", "0.3.0-win", true)]
    [InlineData("0.10.0", "0.9.0", true)]
    [InlineData("0.9.0", "0.10.0-win", false)]
    [InlineData("1.0.0", "0.99.99", true)]
    [InlineData("0.4.0", "0.4.0-win", false)]
    [InlineData("0.4.0-beta", "0.4.0", false)]
    [InlineData("0.4.1", "0.4.0-win", true)]
    [InlineData("0.4", "0.3.9", true)]
    [InlineData("x.y", "0.3.0", false)]
    [InlineData("0.4.0", "garbage", false)]
    public void IsNewer_Table(string candidate, string current, bool newer) => Assert.Equal(newer, UpdateChecker.IsNewer(candidate, current));

    static string Manifest(TempDir dir, string json)
    {
        var path = Path.Combine(dir.Path, "offload-update.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task FileManifest_NewVersion_StateWritten()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "state", "update-check.json");
        var source = Manifest(dir, """{"version":"0.5.0","url":"\\\\NAS\\Offload\\","notes":"Denní report, MD5…"}""");

        var r = await UpdateChecker.CheckAsync(source, "0.4.0-win", Now, state);

        Assert.Equal(UpdateStatus.NewVersion, r.Status);
        Assert.Equal(new UpdateInfo("0.5.0", @"\\NAS\Offload\", "Denní report, MD5…"), r.Info);
        Assert.Equal("Je k dispozici verze 0.5.0 (máte 0.4.0).", r.Text("0.4.0-win"));
        using var doc = JsonDocument.Parse(File.ReadAllText(state));
        Assert.Equal(Now, doc.RootElement.GetProperty("lastCheck").GetDateTime().ToUniversalTime());
        Assert.Equal("0.5.0", doc.RootElement.GetProperty("lastVersion").GetString());
    }

    // At most once in 24 h: within it the manifest is not read, the remembered newer version still shows; after it, or
    // with "Zkontrolovat teď", or when the clock went back, it is read again.
    [Fact]
    public async Task OncePerDay_FakeClock_ClockBack_Force()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "update-check.json");
        var source = Manifest(dir, """{"version":"0.5.0"}""");
        Assert.Equal(UpdateStatus.NewVersion, (await UpdateChecker.CheckAsync(source, "0.4.0", Now, state)).Status);
        File.WriteAllText(source, """{"version":"0.6.0"}""");

        var cached = await UpdateChecker.CheckAsync(source, "0.4.0", Now.AddHours(23), state);
        Assert.Equal((UpdateStatus.NewVersion, "0.5.0"), (cached.Status, cached.Info!.Version));   // from the state, not read
        Assert.Equal(UpdateStatus.NotDue, (await UpdateChecker.CheckAsync(source, "0.5.0", Now.AddHours(23), state)).Status);

        Assert.Equal("0.6.0", (await UpdateChecker.CheckAsync(source, "0.4.0", Now.AddHours(25), state)).Info!.Version);
        File.WriteAllText(source, """{"version":"0.7.0"}""");
        Assert.Equal("0.7.0", (await UpdateChecker.CheckAsync(source, "0.4.0", Now.AddHours(26), state, force: true)).Info!.Version);
        File.WriteAllText(source, """{"version":"0.8.0"}""");
        Assert.Equal("0.8.0", (await UpdateChecker.CheckAsync(source, "0.4.0", Now.AddDays(-3), state)).Info!.Version);   // clock went back
    }

    [Fact]
    public async Task UpToDate_Off_Damaged()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "update-check.json");
        var same = await UpdateChecker.CheckAsync(Manifest(dir, """{"version":"0.4.0"}"""), "0.4.0-win", Now, state, force: true);
        Assert.Equal((UpdateStatus.UpToDate, "Máte nejnovější verzi 0.4.0."), (same.Status, same.Text("0.4.0-win")));

        Assert.Equal(UpdateStatus.Off, (await UpdateChecker.CheckAsync(null, "0.4.0", Now, state)).Status);
        Assert.Equal(UpdateStatus.Off, (await UpdateChecker.CheckAsync("  ", "0.4.0", Now, state)).Status);

        var broken = await UpdateChecker.CheckAsync(Manifest(dir, "{\"version\":"), "0.4.0", Now, state, force: true);
        Assert.Equal(UpdateStatus.Failed, broken.Status);
        Assert.StartsWith("Manifest nejde načíst: poškozený JSON", broken.Text("0.4.0"));
        var noVersion = await UpdateChecker.CheckAsync(Manifest(dir, """{"version":"latest"}"""), "0.4.0", Now, state, force: true);
        Assert.Equal("Manifest nejde načíst: v manifestu chybí platná verze (MAJOR.MINOR.PATCH)", noVersion.Text("0.4.0"));
        var missing = await UpdateChecker.CheckAsync(Path.Combine(dir.Path, "none.json"), "0.4.0", Now, state, force: true);
        Assert.Equal(UpdateStatus.Failed, missing.Status);

        File.WriteAllText(state, "{oops");   // a damaged state file: simply checked again
        Assert.Equal(UpdateStatus.UpToDate, (await UpdateChecker.CheckAsync(Manifest(dir, "\uFEFF{\"version\":\"0.4.0\"}"), "0.4.0", Now, state)).Status);
    }

    [Fact]
    public async Task Http_Manifest_UserAgent_NoIdentifiers()
    {
        using var dir = new TempDir();
        using var server = new LocalHttp(c => LocalHttp.Reply(c, 200, """{"version":"0.5.0","url":"https://example.invalid/offload.zip"}"""));

        var r = await UpdateChecker.CheckAsync(server.Url + "offload-update.json", "0.4.0-win", Now, Path.Combine(dir.Path, "s.json"));

        Assert.Equal((UpdateStatus.NewVersion, "https://example.invalid/offload.zip"), (r.Status, r.Info!.Url));
        var req = Assert.Single(server.Requests);
        Assert.Equal(("GET", "/offload-update.json"), (req.Method, req.Path));
        Assert.Matches(@"^Offload/\d+\.\d+\.\d+$", req.UserAgent);
    }

    // Review focus 4: a server that does not answer, a 404 — Failed with the reason, within the limit.
    [Fact]
    public async Task Http_Silent_Or_404_Failed()
    {
        using var dir = new TempDir();
        using var silent = new LocalHttp(_ => Task.Delay(Timeout.Infinite));
        var sw = Stopwatch.StartNew();
        var r = await UpdateChecker.CheckAsync(silent.Url, "0.4.0", Now, Path.Combine(dir.Path, "a.json"), httpTimeout: TimeSpan.FromMilliseconds(300));
        Assert.Equal(UpdateStatus.Failed, r.Status);
        Assert.EndsWith("neodpověděl do 0.3 s", r.Error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), sw.Elapsed.ToString());   // far below a dead path's tens of seconds, with room for a loaded test run

        using var missing = new LocalHttp(c => LocalHttp.Reply(c, 404, "no"));
        var nf = await UpdateChecker.CheckAsync(missing.Url, "0.4.0", Now, Path.Combine(dir.Path, "b.json"));
        Assert.StartsWith("server odpověděl 404", nf.Error);
    }

    // Review focus 4: a dead UNC path would block for tens of seconds — the file read gives up after its limit
    // (5 s; here 300 ms, against a path that does not answer: a named pipe nobody serves).
    [Fact]
    public async Task File_ThatDoesNotAnswer_GivesUp()
    {
        using var dir = new TempDir();
        var pipeName = "offload-test-" + Guid.NewGuid().ToString("N");
        using var pipe = new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.InOut, 1);
        var connected = pipe.WaitForConnectionAsync();   // accepts, then never writes: a read blocks
        var sw = Stopwatch.StartNew();
        var r = await UpdateChecker.CheckAsync($@"\\.\pipe\{pipeName}", "0.4.0", Now, Path.Combine(dir.Path, "s.json"), fileTimeout: TimeSpan.FromMilliseconds(300));
        Assert.Equal(UpdateStatus.Failed, r.Status);
        Assert.EndsWith("neodpověděl do 0.3 s", r.Error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), sw.Elapsed.ToString());   // far below a dead path's tens of seconds, with room for a loaded test run
        pipe.Disconnect();
    }

    [Fact]
    public void Setting_Updates_GlobalAndKept()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """{"updates":{"manifest":"  \\\\NAS\\Offload\\offload-update.json ","future":1}}""");
        var s = SettingsStore.Load(path);
        Assert.Equal(@"\\NAS\Offload\offload-update.json", s.Updates.Manifest);
        Projects.SaveAs(s, "Film X");                       // not part of a project
        SettingsStore.Save(s, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, doc.RootElement.GetProperty("updates").GetProperty("future").GetInt32());
        Assert.False(doc.RootElement.GetProperty("projects")[0].TryGetProperty("updates", out _));
        Assert.Null(SettingsStore.Load(Path.Combine(dir.Path, "none.json")).Updates.Manifest);   // off by default
    }
}
```


- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~UpdateCheckerTests"`
Expected: FAIL — build testů selže (CS0234 / CS0246: `Offload.Core.Updates`, `UpdateChecker`, `AppSettings.Updates` neexistují).

- [ ] **Step 3: Implementace**

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 343–346 — najít:

```csharp
        ticker.Stop();
        healthTimer.Stop();
        queue.Dispose();
        SaveNow();
```

nahradit:

```csharp
        ticker.Stop();
        healthTimer.Stop();
        updateTimer.Stop();
        queue.Dispose();
        SaveNow();
```

Vytvořit `windows/src/Offload.App/MainViewModel.Updates.cs`:

```csharp
using System.Windows.Threading;
using Offload.Core;
using Offload.Core.Updates;

namespace Offload.App;

/// Spec 4.4: 5 s after the start (and at most once in 24 h) the manifest is read off the UI thread; a newer version shows
/// as a quiet link on the right of the menu bar. Nástroje → Aktualizace… sets the manifest and checks now.
public sealed partial class MainViewModel
{
    readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    UpdateInfo? newVersion;
    bool checkingUpdates;

    public RelayCommand UpdatesCommand { get; private set; } = null!;
    /// Opens the Aktualizace window (set by the main window) with this view model.
    public Action<MainViewModel>? ShowUpdatesDialog { get; set; }

    void InitUpdates()
    {
        UpdatesCommand = new RelayCommand(() => ShowUpdatesDialog?.Invoke(this), () => !closing);
        updateTimer.Tick += (_, _) =>
        {
            updateTimer.Stop();
            _ = CheckUpdatesAsync(force: false);
        };
        updateTimer.Start();
    }

    /// "Nová verze 0.5.0" — null while there is none.
    public string? NewVersionText => newVersion is { } v ? $"Nová verze {v.Version}" : null;
    public UpdateInfo? NewVersion => newVersion;

    /// The manifest source in settings.json (`updates.manifest`); empty = off.
    public string UpdateManifest
    {
        get => AllSettings.Updates.Manifest ?? "";
        set
        {
            AllSettings.Updates.Manifest = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            SettingsChanged();
            Raise();
        }
    }

    /// Reads the manifest off the UI thread (the file read and HTTP have their own limits). The Czech result line, or null.
    internal async Task<string?> CheckUpdatesAsync(bool force)
    {
        if (checkingUpdates || closing) return null;
        checkingUpdates = true;
        try
        {
            var source = AllSettings.Updates.Manifest;
            var result = await Task.Run(() => UpdateChecker.CheckAsync(source, OffloadVersion.String, DateTime.UtcNow, UpdateChecker.DefaultStatePath, force));
            if (closing) return null;
            if (result.Status is UpdateStatus.NewVersion) newVersion = result.Info;
            else if (result.Status is UpdateStatus.UpToDate or UpdateStatus.Off) newVersion = null;
            if (result.Status == UpdateStatus.Failed) AppLog.Write($"update check: {result.Error}");
            RaiseMany(nameof(NewVersionText), nameof(NewVersion));
            return result.Text(OffloadVersion.String);
        }
        catch (Exception e)
        {
            AppLog.Write($"update check: {e.Message}");
            return $"Manifest nejde načíst: {e.Message}";
        }
        finally
        {
            checkingUpdates = false;
        }
    }

    /// "Otevřít": the release in the browser or Explorer (never downloaded or started by Offload).
    internal void OpenNewVersion()
    {
        if (newVersion?.Url is string url && Shell.Open(url) is string error) ShowMessage(error);
    }
}
```

`windows/src/Offload.App/MainViewModel.cs`, řádky 28–31 — najít:

```csharp
        InitProjects();
        InitNotify();
    }

```

nahradit:

```csharp
        InitProjects();
        InitNotify();
        InitUpdates();
    }

```

`windows/src/Offload.App/MainWindow.xaml`, řádky 13–16 — najít:

```xml
    <DockPanel>
        <Border DockPanel.Dock="Top" BorderThickness="0,0,0,1" BorderBrush="{DynamicResource DividerStrokeColorDefaultBrush}">
            <Menu Padding="6,2">
                <MenuItem Header="_Projekt" AutomationProperties.AutomationId="ProjectMenu">
```

nahradit:

```xml
    <DockPanel>
        <Border DockPanel.Dock="Top" BorderThickness="0,0,0,1" BorderBrush="{DynamicResource DividerStrokeColorDefaultBrush}">
          <DockPanel>
            <!-- Spec 4.4: a quiet link when the manifest names a newer version. -->
            <TextBlock DockPanel.Dock="Right" VerticalAlignment="Center" Margin="0,0,12,0" FontSize="12"
                       Visibility="{Binding NewVersionText, Converter={StaticResource NullToCollapsed}}">
                <Hyperlink Command="{Binding UpdatesCommand}" AutomationProperties.AutomationId="NewVersionLink">
                    <Run Text="{Binding NewVersionText, Mode=OneWay}" />
                </Hyperlink>
            </TextBlock>
            <Menu Padding="6,2">
                <MenuItem Header="_Projekt" AutomationProperties.AutomationId="ProjectMenu">
```

`windows/src/Offload.App/MainWindow.xaml`, řádky 32–35 — najít:

```xml
                              AutomationProperties.AutomationId="SaveVerifyReportMenuItem" />
                    <MenuItem Header="Oznámení…" Command="{Binding NotifySettingsCommand}" AutomationProperties.AutomationId="NotifyMenuItem" />
                    <MenuItem Header="Denní report…" Command="{Binding DayReportCommand}"
                              AutomationProperties.AutomationId="DayReportMenuItem">
```

nahradit:

```xml
                              AutomationProperties.AutomationId="SaveVerifyReportMenuItem" />
                    <MenuItem Header="Oznámení…" Command="{Binding NotifySettingsCommand}" AutomationProperties.AutomationId="NotifyMenuItem" />
                    <MenuItem Header="Aktualizace…" Command="{Binding UpdatesCommand}" AutomationProperties.AutomationId="UpdatesMenuItem" />
                    <MenuItem Header="Denní report…" Command="{Binding DayReportCommand}"
                              AutomationProperties.AutomationId="DayReportMenuItem">
```

`windows/src/Offload.App/MainWindow.xaml`, řádky 47–50 — najít:

```xml
                </MenuItem>
            </Menu>
        </Border>
        <Grid>
```

nahradit:

```xml
                </MenuItem>
            </Menu>
          </DockPanel>
        </Border>
        <Grid>
```

`windows/src/Offload.App/MainWindow.xaml.cs`, řádky 23–26 — najít:

```csharp
        vm.WindowInFront = () => tray.WindowInFront;
        vm.EditNotifyDialog = settings => NotifyWindow.Edit(this, settings, vm.ActiveProjectName);
        Closed += (_, _) => tray.Dispose();
        vm.MessageRequested += text => MessageBox.Show(this, text, "Offload");
```

nahradit:

```csharp
        vm.WindowInFront = () => tray.WindowInFront;
        vm.EditNotifyDialog = settings => NotifyWindow.Edit(this, settings, vm.ActiveProjectName);
        vm.ShowUpdatesDialog = m => UpdateWindow.Show(this, m);
        Closed += (_, _) => tray.Dispose();
        vm.MessageRequested += text => MessageBox.Show(this, text, "Offload");
```

Vytvořit `windows/src/Offload.App/UpdateWindow.xaml`:

```xml
<Window x:Class="Offload.App.UpdateWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Aktualizace" SizeToContent="Height" Width="520" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Icon="pack://application:,,,/Offload.ico">
    <StackPanel Margin="22,18">
        <TextBlock Text="Aktualizace" FontSize="17" FontWeight="SemiBold" />
        <TextBlock x:Name="Current" Style="{StaticResource Caption}" Margin="0,4,0,0" />
        <TextBlock Text="Adresa manifestu (web, NAS nebo sdílená složka)" Style="{StaticResource Caption}" Margin="0,12,0,4" />
        <TextBox Text="{Binding UpdateManifest, UpdateSourceTrigger=LostFocus}" x:Name="Manifest" AutomationProperties.AutomationId="UpdateManifest"
                 AutomationProperties.Name="Adresa manifestu" />
        <TextBlock Text="Offload jen zjistí, jestli je nová verze — nic nestahuje ani nespouští. Prázdné = kontrola vypnutá."
                   Style="{StaticResource Caption}" Margin="0,4,0,0" TextWrapping="Wrap" />
        <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
            <Button x:Name="CheckButton" Content="Zkontrolovat teď" Click="Check_Click" Padding="12,5" AutomationProperties.AutomationId="UpdateCheckNow" />
            <TextBlock x:Name="Result" VerticalAlignment="Center" Margin="10,0,0,0" TextWrapping="Wrap" MaxWidth="320"
                       AutomationProperties.AutomationId="UpdateResult" />
        </StackPanel>
        <Border Margin="0,14,0,0" Padding="12" BorderThickness="1" CornerRadius="4" BorderBrush="{DynamicResource DividerStrokeColorDefaultBrush}"
                Visibility="{Binding NewVersionText, Converter={StaticResource NullToCollapsed}}">
            <StackPanel>
                <TextBlock Text="{Binding NewVersionText}" FontWeight="SemiBold" />
                <TextBlock Text="{Binding NewVersion.Notes}" TextWrapping="Wrap" Margin="0,4,0,0" />
                <TextBlock Text="{Binding NewVersion.Url}" Style="{StaticResource Caption}" FontFamily="Consolas" TextWrapping="Wrap" Margin="0,4,0,0" />
                <Button Content="Otevřít" Click="Open_Click" HorizontalAlignment="Left" Margin="0,8,0,0" Padding="12,5"
                        AutomationProperties.AutomationId="UpdateOpen" />
            </StackPanel>
        </Border>
        <Button Content="Zavřít" IsCancel="True" IsDefault="True" HorizontalAlignment="Right" MinWidth="80" Padding="12,5" Margin="0,20,0,0" />
    </StackPanel>
</Window>
```

Vytvořit `windows/src/Offload.App/UpdateWindow.xaml.cs`:

```csharp
using System.Windows;
using Offload.Core;

namespace Offload.App;

/// Nástroje → Aktualizace… and the "Nová verze" link (spec 4.4): the manifest source, "Zkontrolovat teď" (off the UI
/// thread), the new version's notes and "Otevřít".
public partial class UpdateWindow : Window
{
    readonly MainViewModel vm;

    UpdateWindow(MainViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        DataContext = vm;
        Current.Text = $"Tato verze: {OffloadVersion.String}";
    }

    public static void Show(Window owner, MainViewModel vm) => new UpdateWindow(vm) { Owner = owner }.ShowDialog();

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        Manifest.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();   // the address as typed
        CheckButton.IsEnabled = false;
        Result.Text = "Zjišťuji…";
        try
        {
            Result.Text = await vm.CheckUpdatesAsync(force: true) ?? "";
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    void Open_Click(object sender, RoutedEventArgs e) => vm.OpenNewVersion();
}
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 4–7 — najít:

```csharp
using Offload.Core.Notify;
using Offload.Core.Outputs;

namespace Offload.Core.Desktop;
```

nahradit:

```csharp
using Offload.Core.Notify;
using Offload.Core.Outputs;
using Offload.Core.Updates;

namespace Offload.Core.Desktop;
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 69–72 — najít:

```csharp
    private Dictionary<string, string> _portNames = new(StringComparer.OrdinalIgnoreCase);

    /// The named profiles (spec 4.1), in the order they were created.
    [JsonPropertyName("projects")] public List<ProjectSettings> Projects { get; set; } = [];
```

nahradit:

```csharp
    private Dictionary<string, string> _portNames = new(StringComparer.OrdinalIgnoreCase);

    /// The update check (spec 4.4): global, off until a manifest is set.
    [JsonPropertyName("updates")] public UpdateSettings Updates { get; set; } = new();

    /// The named profiles (spec 4.1), in the order they were created.
    [JsonPropertyName("projects")] public List<ProjectSettings> Projects { get; set; } = [];
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 87–91 — najít:

```csharp
    static readonly string[] ProfileKeys = ["destRoots", "verify", "analyzeClips", "proxy", "naming", "hash", "outputs", "notify"];
    /// The global top-level keys; every other top-level key goes to AppSettings.Extra.
    static readonly string[] GlobalKeys = ["tools", "portNames", "projects", "activeProject"];

    static readonly JsonSerializerOptions Json = new()
```

nahradit:

```csharp
    static readonly string[] ProfileKeys = ["destRoots", "verify", "analyzeClips", "proxy", "naming", "hash", "outputs", "notify"];
    /// The global top-level keys; every other top-level key goes to AppSettings.Extra.
    static readonly string[] GlobalKeys = ["tools", "portNames", "updates", "projects", "activeProject"];

    static readonly JsonSerializerOptions Json = new()
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 113–116 — najít:

```csharp
            ReadProfile(root, s, GlobalKeys);
            Kept(root, s, "tools", (ExternalTools v) => s.Tools = v, () => new ExternalTools());
            Section(root, "portNames", e =>
            {
```

nahradit:

```csharp
            ReadProfile(root, s, GlobalKeys);
            Kept(root, s, "tools", (ExternalTools v) => s.Tools = v, () => new ExternalTools());
            Kept(root, s, "updates", (UpdateSettings v) => s.Updates = v, () => new UpdateSettings());
            Section(root, "portNames", e =>
            {
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 214–217 — najít:

```csharp
    {
        s.Tools ??= new ExternalTools();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in s.PortNames ?? [])
```

nahradit:

```csharp
    {
        s.Tools ??= new ExternalTools();
        s.Updates ??= new UpdateSettings();
        s.Updates.Manifest = string.IsNullOrWhiteSpace(s.Updates.Manifest) ? null : s.Updates.Manifest.Trim();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in s.PortNames ?? [])
```

Vytvořit `windows/src/Offload.Core/Updates/UpdateChecker.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Offload.Core.Desktop;

namespace Offload.Core.Updates;

/// "updates" in settings.json (spec 4.4, 5.1) — global, not part of a project.
public sealed class UpdateSettings
{
    /// Where the manifest is: an http(s) URL or a file path (local, NAS, UNC). Null = the check is off (the default).
    [JsonPropertyName("manifest")] public string? Manifest { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// The manifest `build.ps1 -ReleaseUrl` writes (spec 4.4): {"version", "url", "notes"}.
public sealed record UpdateInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("notes")] string? Notes);

public enum UpdateStatus { Off, NotDue, UpToDate, NewVersion, Failed }

/// The answer of a check. `Info`: the newer version (NewVersion, also NotDue when the last check found one).
public sealed record UpdateResult(UpdateStatus Status, UpdateInfo? Info, string? Error)
{
    /// The line under "Zkontrolovat teď" (Czech), or null.
    public string? Text(string currentVersion) => Status switch
    {
        UpdateStatus.NewVersion => $"Je k dispozici verze {Info!.Version} (máte {UpdateChecker.Plain(currentVersion)}).",
        UpdateStatus.UpToDate => $"Máte nejnovější verzi {UpdateChecker.Plain(currentVersion)}.",
        UpdateStatus.Failed => $"Manifest nejde načíst: {Error}",
        UpdateStatus.Off => "Adresa manifestu není vyplněná — kontrola je vypnutá.",
        _ => null,
    };
}

/// %APPDATA%\Offload\update-check.json: when the manifest was last read and what it said.
public sealed class UpdateState
{
    [JsonPropertyName("lastCheck")] public DateTime? LastCheck { get; set; }
    [JsonPropertyName("lastVersion")] public string? LastVersion { get; set; }
    [JsonPropertyName("lastUrl")] public string? LastUrl { get; set; }
    [JsonPropertyName("lastNotes")] public string? LastNotes { get; set; }
}

/// The update check (spec 4.4, R21): reads the manifest at most once in 24 h, never downloads or starts anything;
/// HTTP with a 10 s limit, a file read on its own task with a 5 s limit (a dead UNC path would block for tens of
/// seconds). Never throws — a problem is UpdateStatus.Failed with a Czech reason.
public static class UpdateChecker
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FileTimeout = TimeSpan.FromSeconds(5);

    public static string DefaultStatePath => Path.Combine(AppData.Folder, "update-check.json");

    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// `force` ("Zkontrolovat teď") ignores the 24 h. A last check in the future (the clock went back) is due now.
    public static async Task<UpdateResult> CheckAsync(string? source, string currentVersion, DateTime nowUtc, string statePath,
        bool force = false, TimeSpan? httpTimeout = null, TimeSpan? fileTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(source)) return new(UpdateStatus.Off, null, null);
        var state = LoadState(statePath);
        if (!force && state.LastCheck is DateTime last && last <= nowUtc && nowUtc - last < Interval)
            return state.LastVersion is string v && IsNewer(v, currentVersion)
                ? new(UpdateStatus.NewVersion, new UpdateInfo(v, state.LastUrl, state.LastNotes), null)
                : new(UpdateStatus.NotDue, null, null);
        UpdateResult result;
        try
        {
            var text = await Read(source.Trim(), httpTimeout ?? HttpTimeout, fileTimeout ?? FileTimeout).ConfigureAwait(false);
            var info = Parse(text);
            state.LastVersion = info.Version;
            state.LastUrl = info.Url;
            state.LastNotes = info.Notes;
            result = IsNewer(info.Version, currentVersion) ? new(UpdateStatus.NewVersion, info, null) : new(UpdateStatus.UpToDate, info, null);
        }
        catch (Exception e)
        {
            result = new(UpdateStatus.Failed, null, e is UpdateException ? e.Message : e.GetBaseException().Message);
        }
        state.LastCheck = nowUtc;   // also after a failure: once a day, not at every start
        SaveState(statePath, state);
        return result;
    }

    /// The manifest's text: GET for http(s), otherwise a file (local, NAS, UNC).
    static async Task<string> Read(string source, TimeSpan httpTimeout, TimeSpan fileTimeout)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            using var cts = new CancellationTokenSource(httpTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd($"Offload/{Plain(OffloadVersion.String)}");
            try
            {
                using var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new UpdateException($"server odpověděl {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd());
                return await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new UpdateException($"{source} neodpověděl do {httpTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s");
            }
        }
        var read = Task.Run(() => File.ReadAllText(source));
        if (await Task.WhenAny(read, Task.Delay(fileTimeout)).ConfigureAwait(false) != read)
        {
            _ = read.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);   // observed later, never unobserved
            throw new UpdateException($"{source} neodpověděl do {fileTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s");
        }
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new UpdateException(e.Message);
        }
    }

    /// The manifest; a version that is not MAJOR.MINOR.PATCH or a broken JSON is an UpdateException.
    public static UpdateInfo Parse(string text)
    {
        UpdateInfo? info;
        try
        {
            info = JsonSerializer.Deserialize<UpdateInfo>(text.TrimStart('﻿'));
        }
        catch (JsonException e)
        {
            throw new UpdateException($"poškozený JSON ({e.Message})");
        }
        if (info?.Version is not string v || Parts(v) == null) throw new UpdateException("v manifestu chybí platná verze (MAJOR.MINOR.PATCH)");
        return info;
    }

    /// `candidate` > `current` by MAJOR.MINOR.PATCH; what follows "-" is ignored ("0.4.0-win" = "0.4.0").
    public static bool IsNewer(string candidate, string current) =>
        Parts(candidate) is { } a && Parts(current) is { } b && a.CompareTo(b) > 0;

    /// "0.10.0" > "0.9.0" (numbers, not text). Null when it is no version.
    public static (int, int, int)? Parts(string version)
    {
        var p = Plain(version).Split('.');
        if (p.Length is < 1 or > 3) return null;
        var n = new int[3];
        for (int i = 0; i < p.Length; i++)
            if (!int.TryParse(p[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return null;
        return (n[0], n[1], n[2]);
    }

    /// The version without its suffix: "0.4.0-win" → "0.4.0".
    public static string Plain(string version) => version.Trim().Split('-', 2)[0];

    static UpdateState LoadState(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception)
        {
            return new();   // a damaged state file: check again
        }
    }

    static void SaveState(string path, UpdateState state)
    {
        try
        {
            NativeIO.Mkdirs(Path.GetDirectoryName(path)!);
            NativeIO.WriteFileReplacing(path, JsonSerializer.SerializeToUtf8Bytes(state, Json));
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.WriteLine($"update check: state not saved: {e.Message}");
        }
    }
}

/// A Czech reason the manifest could not be used.
public sealed class UpdateException(string message) : Exception(message);
```


- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~UpdateCheckerTests|FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~ProjectsTests"`
Expected: PASS. `dotnet build windows\Offload.sln -warnaserror` zkompiluje `UpdateWindow.xaml` a odkaz v řádku menu. Pak celá sada (vše zelené, 1141 testů: 1133 úspěšných + 8 přeskočených).

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 22–23 — najít:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 10 — kontrola aktualizací (R21): manifest `{"version","url","notes"}` z http(s) adresy nebo souboru (místní, NAS, UNC) v `updates.manifest` (společné pro projekty, výchozí vypnuto); nejvýš 1× za 24 h (`update-check.json`: `lastCheck`, `lastVersion`, `lastUrl`, `lastNotes`; hodiny posunuté zpět = zkontrolovat), 5 s po startu mimo UI vlákno; HTTP nejvýš 10 s, soubor nejvýš 5 s (mrtvá UNC cesta okno nezdrží); porovnání `MAJOR.MINOR.PATCH` číselně (`0.10.0` > `0.9.0`, přípona `-win` se ignoruje); nic nestahuje ani nespouští, `User-Agent: Offload/<verze>`; odkaz „Nová verze …“ vpravo v řádku menu, okno Aktualizace (adresa manifestu, „Zkontrolovat teď“, poznámky, *Otevřít*), Nástroje → Aktualizace…
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Windows: update check (manifest from a URL or a file, once a day)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/Updates/UpdateChecker.cs windows/src/Offload.Core/Desktop/AppSettings.cs windows/src/Offload.App/MainViewModel.Updates.cs windows/src/Offload.App/UpdateWindow.xaml windows/src/Offload.App/UpdateWindow.xaml.cs windows/src/Offload.App/MainViewModel.cs windows/src/Offload.App/MainViewModel.Queue.cs windows/src/Offload.App/MainWindow.xaml windows/src/Offload.App/MainWindow.xaml.cs windows/tests/Offload.Core.Tests/UpdateCheckerTests.cs
git commit -F .git/COMMIT_MSG_PLAN8
```

---

### Task 11: Verze z `Directory.Build.props`, verzovaný zip, manifest, `build.ps1 -Sign`, README

Spec §4.4 (číslování R22, `OffloadVersion` z assembly s testem tvaru `x.y.z-win`, vydání verze: `build.ps1 [-Sign] -ReleaseUrl` → `Offload-win-x64-<verze>.zip` + `offload-update.json`, manifest jako poslední), §4.5 (parametry `-Sign`, `-CertThumbprint`, `OFFLOAD_SIGN_PFX(+_PASSWORD)`, `-TimestampUrl`; hledání signtoolu; podepisují se jen naše exe R23; `signtool sign /fd SHA256 /td SHA256 /tr … /d "Offload"`, `signtool verify /pa /all`; razítko 3 pokusy po 5 s; české chyby; heslo se nevypisuje; dokumentace jak získat certifikát), §6.3 úkol 10. Logika je v `scripts\release.ps1` (funkce dot-sourcované z `build.ps1`), testy ji pouštějí ve Windows PowerShell proti falešnému `signtool.cmd` a self-signed PFX vytvořenému v testu; `build.ps1` sám se v testech nespouští (dlouhý `dotnet publish`) — ověří ho ruční test 39 a `-Sign` bez certifikátu skončí ještě před buildem.

**Files:**
- Create: `windows/scripts/release.ps1` (**UTF-8 s BOM** — viz Step 3)
- Modify: `windows/scripts/build.ps1:1-7, 17-20, 53-59`
- Modify: `windows/src/Offload.Core/Format.cs:1-3, 5-9`
- Modify: `windows/README.md:26-35`
- Test: `windows/tests/Offload.Core.Tests/ReleaseScriptTests.cs` (nový), `windows/tests/Offload.Core.Tests/FormatTests.cs:24-27`
- Modify: `docs/VYVOJ-WINDOWS.md:22-23`

**Interfaces:**
- Consumes: `UpdateChecker.Parse` (úkol 10 — test ověří, že manifest ze skriptu aplikace přečte).
- Produces: `OffloadVersion.String` je `static readonly` z `AssemblyInformationalVersionAttribute` (dřív `const`); PowerShell funkce `Get-OffloadVersion([string]$Props)`, `Write-UpdateManifest([string]$Path, [string]$Version, [string]$Url, [string]$Notes)`, `Find-SignTool`, `Hide-Secret([string]$Text)`, `Get-OffloadSignSetup([string]$CertThumbprint) → @{ SignTool; CertArgs }`, `Invoke-OffloadSign([hashtable]$Setup, [string[]]$Files, [string]$TimestampUrl, [int]$RetryDelaySeconds = 5)`; `build.ps1 -Sign -CertThumbprint -TimestampUrl -ReleaseUrl -Notes`.

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/FormatTests.cs`, řádky 24–27 — najít:

```csharp
    public void Version_IsWindowsBuild() => Assert.Equal("0.3.0-win", OffloadVersion.String);

    // Spec 2.1/2.5: sizes inside Czech sentences.
    [Theory]
```

nahradit:

```csharp
    public void Version_IsWindowsBuild() => Assert.Equal("0.3.0-win", OffloadVersion.String);

    // R22: the version comes from Directory.Build.props (InformationalVersion), shaped x.y.z-win — never typed twice.
    [Fact]
    public void Version_FromTheAssembly_ShapeAndProps()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+-win$", OffloadVersion.String);
        var d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "Directory.Build.props"))) d = Path.GetDirectoryName(d.TrimEnd('\\'));
        var props = System.Xml.Linq.XDocument.Load(Path.Combine(d!, "Directory.Build.props"));
        Assert.Equal(props.Descendants("InformationalVersion").First().Value, OffloadVersion.String);
        Assert.Equal(props.Descendants("Version").First().Value + "-win", OffloadVersion.String);
    }

    // Spec 2.1/2.5: sizes inside Czech sentences.
    [Theory]
```

Vytvořit `windows/tests/Offload.Core.Tests/ReleaseScriptTests.cs`:

```csharp
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Offload.Core.Tests;

/// Spec 4.4, 4.5: windows\scripts\release.ps1 (dot-sourced by build.ps1) — the version, the update manifest and
/// signing, run in Windows PowerShell against a stand-in signtool (no SDK, no certificate store, no network).
public class ReleaseScriptTests
{
    /// windows\ of this repo (from the test's bin folder up).
    static string WindowsDir
    {
        get
        {
            var d = AppContext.BaseDirectory;
            while (d != null && !File.Exists(Path.Combine(d, "Directory.Build.props"))) d = Path.GetDirectoryName(d.TrimEnd('\\'));
            return d ?? throw new InvalidOperationException("Directory.Build.props not found");
        }
    }

    static string Script => Path.Combine(WindowsDir, "scripts", "release.ps1");

    /// Runs `body` after dot-sourcing release.ps1; a thrown error prints "ERROR: <message>" and exits 1. `env` is set
    /// for that PowerShell only.
    static (int Code, string Out) Ps(string body, Dictionary<string, string?>? env = null)
    {
        var command = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ErrorActionPreference = 'Stop'; " +
            $". '{Script}'; try {{ {body} }} catch {{ [Console]::Out.WriteLine('ERROR: ' + $_.Exception.Message); exit 1 }}";
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) psi.ArgumentList.Add(a);
        foreach (var name in new[] { "SIGNTOOL", "OFFLOAD_SIGN_PFX", "OFFLOAD_SIGN_PFX_PASSWORD" }) psi.Environment.Remove(name);
        foreach (var (k, v) in env ?? []) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout + stderr.Result);
    }

    /// A stand-in signtool: logs every call to calls.txt; `sign` fails with a timestamp error while ts-fail exists,
    /// `verify` fails while verify-fail exists.
    static string FakeSigntool(string dir)
    {
        var path = Path.Combine(dir, "signtool.cmd");
        File.WriteAllText(path, """
            @echo off
            >>"%~dp0calls.txt" echo %*
            if "%1"=="verify" goto verify
            if exist "%~dp0ts-fail" (
              echo SignTool Error: The specified timestamp server either could not be reached or returned an invalid response.
              exit /b 1
            )
            echo Successfully signed
            exit /b 0
            :verify
            if exist "%~dp0verify-fail" (
              echo SignTool Error: No signature found.
              exit /b 1
            )
            echo Successfully verified
            exit /b 0

            """.ReplaceLineEndings("\r\n"));
        return path;
    }

    /// A self-signed PFX (good, or expired) with `password`.
    static string Pfx(string dir, string password, bool expired = false)
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest("CN=Offload test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var from = expired ? DateTimeOffset.Now.AddYears(-2) : DateTimeOffset.Now.AddDays(-1);
        using var cert = req.CreateSelfSigned(from, expired ? DateTimeOffset.Now.AddYears(-1) : DateTimeOffset.Now.AddYears(1));
        var path = Path.Combine(dir, expired ? "old.pfx" : "test.pfx");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        return path;
    }

    static string Exe(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    // Windows PowerShell 5.1 reads a .ps1 without a BOM in the ANSI code page: the Czech messages need it.
    [Fact]
    public void Script_IsUtf8WithBom()
    {
        var bytes = File.ReadAllBytes(Script);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    [Fact]
    public void Version_FromProps_Manifest()
    {
        using var dir = new TempDir();
        var manifest = Path.Combine(dir.Path, "offload-update.json");
        var r = Ps($"Write-UpdateManifest -Path '{manifest}' -Version (Get-OffloadVersion '{Path.Combine(WindowsDir, "Directory.Build.props")}') " +
                   "-Url '\\\\NAS\\Offload\\' -Notes 'Projekty, oznámení'");
        Assert.True(r.Code == 0, r.Out);
        var bytes = File.ReadAllBytes(manifest);
        Assert.NotEqual(0xEF, bytes[0]);   // no BOM
        using var doc = JsonDocument.Parse(bytes);
        var props = XDocument.Load(Path.Combine(WindowsDir, "Directory.Build.props")).Descendants("Version").First().Value;
        Assert.Equal(props, doc.RootElement.GetProperty("version").GetString());
        Assert.Equal(@"\\NAS\Offload\", doc.RootElement.GetProperty("url").GetString());
        Assert.Equal("Projekty, oznámení", doc.RootElement.GetProperty("notes").GetString());
        var parsed = Updates.UpdateChecker.Parse(Encoding.UTF8.GetString(bytes));   // what the app reads
        Assert.Equal(props, parsed.Version);
    }

    // Review focus 5: no signtool — a clear Czech error, nothing signed.
    [Fact]
    public void NoSigntool_ClearError()
    {
        using var dir = new TempDir();
        var r = Ps("Get-OffloadSignSetup -CertThumbprint 'ABCD' | Out-Null", new() { ["SIGNTOOL"] = Path.Combine(dir.Path, "none.exe") });
        Assert.Equal(1, r.Code);
        Assert.Contains("ERROR: signtool nenalezen — nainstalujte Windows SDK (Signing Tools for Desktop Apps), nebo nastavte SIGNTOOL.", r.Out);
    }

    [Fact]
    public void Certificate_Missing_Unknown_Expired_NoPassword()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        var none = Ps("Get-OffloadSignSetup | Out-Null", new() { ["SIGNTOOL"] = signtool });
        Assert.Contains("ERROR: Není zadaný certifikát: použijte -CertThumbprint <otisk>, nebo nastavte OFFLOAD_SIGN_PFX a OFFLOAD_SIGN_PFX_PASSWORD.", none.Out);
        var thumb = new string('0', 40);
        var unknown = Ps($"Get-OffloadSignSetup -CertThumbprint '{thumb}' | Out-Null", new() { ["SIGNTOOL"] = signtool });
        Assert.Contains($@"ERROR: Certifikát s otiskem {thumb} nenalezen v CurrentUser\My ani LocalMachine\My.", unknown.Out);
        var noPassword = Ps("Get-OffloadSignSetup | Out-Null", new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "tajne-heslo-1") });
        Assert.Contains("ERROR: Chybí OFFLOAD_SIGN_PFX_PASSWORD.", noPassword.Out);
        var expired = Ps("Get-OffloadSignSetup | Out-Null", new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "tajne-heslo-1", expired: true), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "tajne-heslo-1" });
        Assert.Contains("vypršel", expired.Out);
        Assert.DoesNotContain("tajne-heslo-1", expired.Out);
    }

    // Spec 4.5: sign (SHA-256, RFC 3161 timestamp, description) then verify; the password is never printed.
    [Fact]
    public void Sign_ThenVerify_PasswordNeverPrinted()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        var pfx = Pfx(dir.Path, "tajne-heslo-2");
        var a = Exe(dir.Path, "Offload.exe");
        var b = Exe(dir.Path, "offload.exe.cli");
        var r = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{a}', '{b}') -TimestampUrl 'http://ts.example' -RetryDelaySeconds 0",
            new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = pfx, ["OFFLOAD_SIGN_PFX_PASSWORD"] = "tajne-heslo-2" });
        Assert.True(r.Code == 0, r.Out);
        Assert.Contains($"podepsáno: {a}", r.Out);
        Assert.DoesNotContain("tajne-heslo-2", r.Out);
        var calls = File.ReadAllLines(Path.Combine(dir.Path, "calls.txt"));
        Assert.Equal(2, calls.Length);
        Assert.Equal($"sign /fd SHA256 /td SHA256 /tr http://ts.example /f {pfx} /p tajne-heslo-2 /d Offload {a} {b}", calls[0].Trim());
        Assert.Equal($"verify /pa /all {a} {b}", calls[1].Trim());
    }

    // Review focus 5: the timestamp server does not answer — 3 attempts, then a clear error.
    [Fact]
    public void Timestamp_ThreeAttempts_ThenClearError()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "ts-fail"), "");
        var a = Exe(dir.Path, "Offload.exe");
        var r = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{a}') -TimestampUrl 'http://ts.invalid' -RetryDelaySeconds 0",
            new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "heslo-3"), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "heslo-3" });
        Assert.Equal(1, r.Code);
        Assert.Contains("ERROR: Časové razítko se nepodařilo získat z http://ts.invalid.", r.Out);
        Assert.Equal(3, File.ReadAllLines(Path.Combine(dir.Path, "calls.txt")).Length);
        Assert.DoesNotContain("heslo-3", r.Out);
    }

    [Fact]
    public void VerifyFails_ClearError_MissingFileRefusedFirst()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "verify-fail"), "");
        var env = new Dictionary<string, string?> { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "h4"), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "h4" };
        var a = Exe(dir.Path, "Offload.exe");
        var r = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{a}') -RetryDelaySeconds 0", env);
        Assert.Contains("ERROR: Podpis nejde ověřit: SignTool Error: No signature found.", r.Out);
        var missing = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{Path.Combine(dir.Path, "gone.exe")}') -RetryDelaySeconds 0", env);
        Assert.Contains("ERROR: Soubor k podpisu nenalezen:", missing.Out);
    }
}
```


- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ReleaseScriptTests|FullyQualifiedName~FormatTests"`
Expected: FAIL — `release.ps1` neexistuje (`Script_IsUtf8WithBom`: FileNotFoundException; ostatní: `ERROR: … release.ps1 … not recognized`), `Version_FromTheAssembly_ShapeAndProps` projde už teď (props a const se shodují) — hlídá, že to tak zůstane.

- [ ] **Step 3: Implementace**

`windows/README.md`, řádky 26–35 — najít:

````markdown
cd windows
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\build.ps1   # → build\Offload\Offload.exe, build\Offload\bin\offload.exe, build\Offload-win-x64.zip
```

Balíček `build\Offload-win-x64.zip` obsahuje složku `Offload` (`Offload.exe` + `bin\` s `offload.exe`, `braw-decode.exe`, `ffmpeg.exe`, `ffprobe.exe`);
stačí ho rozbalit kamkoli a spustit `Offload.exe`. Neobsahuje nic, co by se muselo instalovat. Knihovnu `BlackmagicRawAPI.dll`
balíček neobsahuje — bez ní se BRAW jen zkopíruje a ověří, proxy a náhledy se přeskočí.

### Vzorek BRAW (testy dekodéru Blackmagic RAW)
````

nahradit:

````markdown
cd windows
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\build.ps1   # → build\Offload\Offload.exe, build\Offload\bin\offload.exe, build\Offload-win-x64-<verze>.zip
```

Balíček `build\Offload-win-x64-<verze>.zip` (verze z `Directory.Build.props`) obsahuje složku `Offload` (`Offload.exe` + `bin\` s `offload.exe`, `braw-decode.exe`, `ffmpeg.exe`, `ffprobe.exe`);
stačí ho rozbalit kamkoli a spustit `Offload.exe`. Neobsahuje nic, co by se muselo instalovat. Knihovnu `BlackmagicRawAPI.dll`
balíček neobsahuje — bez ní se BRAW jen zkopíruje a ověří, proxy a náhledy se přeskočí.

### Vydání verze

Verze je jen na jednom místě: `windows\Directory.Build.props` (`Version`, např. `0.4.0`, a `InformationalVersion`, např. `0.4.0-win`);
aplikace, CLI a report ji čtou odtud (`OffloadVersion`). Číslování: plán 6 → 0.2.0, 7 → 0.3.0, 8 → 0.4.0, opravy
zvyšují poslední číslo, 1.0.0 po ostrém testu s kartou.

1. Zvyšte verzi v `Directory.Build.props` (obě hodnoty) a spusťte `dotnet test`.
2. `powershell -ExecutionPolicy Bypass -File scripts\build.ps1 [-Sign …] -ReleaseUrl <kam zip dáte> [-Notes "<co je nového>"]`
   — vznikne `build\Offload-win-x64-<verze>.zip` a `build\offload-update.json` (`{"version", "url", "notes"}`, UTF-8).
   `-ReleaseUrl` je adresa, kterou uživatel otevře tlačítkem *Otevřít* (web, `\\NAS\Offload\`, sdílená složka).
3. Nahrajte zip na web / NAS a **až potom** manifest (manifest nesmí ukazovat na zip, který tam ještě není).

Kontrola aktualizací v aplikaci (Nástroje → Aktualizace…) čte manifest z adresy, kterou tam zadáte (`https://…`,
`\\NAS\Offload\offload-update.json`, místní cesta); nejvýš jednou za 24 h, 5 s po startu, nic nestahuje ani nespouští.
Prázdná adresa = vypnuto (výchozí). Repozitář je soukromý, proto se nečte GitHub.

### Podepisování

`build.ps1 -Sign` podepíše `Offload.exe`, `bin\offload.exe` a `bin\braw-decode.exe` (ffmpeg ne — není náš) před
zabalením: `signtool sign /fd SHA256 /td SHA256 /tr <razítko> /d Offload …`, pak `signtool verify /pa /all`.

```powershell
# certifikát v úložišti Windows (CurrentUser\My, pak LocalMachine\My; funguje i s hardwarovým tokenem)
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Sign -CertThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
# nebo soubor .pfx — heslo jen v proměnné prostředí, nikdy na příkazové řádce build.ps1
$env:OFFLOAD_SIGN_PFX = 'C:\certs\offload.pfx'; $env:OFFLOAD_SIGN_PFX_PASSWORD = '…'
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Sign -TimestampUrl http://timestamp.digicert.com
```

- `signtool.exe` se hledá v proměnné `SIGNTOOL`, pak v `PATH`, pak v nejnovějším `Windows Kits\10\bin\10.*\x64\`
  (Windows SDK, komponenta *Signing Tools for Desktop Apps*). Chybí-li, build hned na začátku skončí: „signtool nenalezen —
  nainstalujte Windows SDK (Signing Tools for Desktop Apps), nebo nastavte SIGNTOOL.“ Stejně hned skončí neexistující
  otisk, prošlý certifikát, chybějící `OFFLOAD_SIGN_PFX_PASSWORD`.
- Časové razítko (výchozí `http://timestamp.digicert.com`): 3 pokusy po 5 s, pak „Časové razítko se nepodařilo získat z …“.
- Heslo se nikdy nevypisuje (v chybách je `***`). Pozor: `signtool /p` je vidět v seznamu procesů — na sdíleném počítači
  raději certifikát v úložišti nebo na tokenu.
- Bez `-Sign` se build nemění.

Jak získat certifikát (nic se nekupuje automaticky; ceny orientační, k datu nákupu ověřit):

| Možnost | Co to je | Cena (orientačně) | Poznámka |
|---|---|---|---|
| OV (Organization Validation) | ověřená firma nebo živnost | ~200–500 USD/rok | od 2023 je klíč povinně na tokenu / HSM (USB token, `-CertThumbprint`) |
| EV (Extended Validation) | přísnější ověření firmy | ~300–700 USD/rok | také token; od 2024 už nedává okamžitou reputaci SmartScreen — ověřit aktuální stav |
| Microsoft Artifact Signing (Azure Trusted Signing) | cloudové podepisování | ~10 USD/měsíc | podmínky podle země a historie firmy — ověřit pro ČR; signtool s jejich doplňkem |
| Certum (polská CA) | OV i pro jednotlivce | levnější OV | token / cloud podle nabídky |

SmartScreen buduje reputaci počtem stažení — první verze mohou varovat i s podpisem. Certifikát podepsaný sám sebou
(self-signed) SmartScreenu nepomůže; hodí se jen k vyzkoušení skriptu (`signtool verify /pa` projde jen s certifikátem,
kterému počítač důvěřuje).

### Vzorek BRAW (testy dekodéru Blackmagic RAW)
````

`windows/scripts/build.ps1`, řádky 1–7 — najít:

```powershell
# Builds windows\build\Offload: Offload.exe (GUI) + bin\offload.exe (CLI) + bin\braw-decode.exe + bin\ffmpeg.exe, ffprobe.exe (all self-contained).
# -Staging builds into build\staging\Offload instead (e.g. while build\Offload\Offload.exe is running); same zip.
param([switch]$Staging)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root $(if ($Staging) { 'build\staging\Offload' } else { 'build\Offload' })
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
```

nahradit:

```powershell
# Builds windows\build\Offload: Offload.exe (GUI) + bin\offload.exe (CLI) + bin\braw-decode.exe + bin\ffmpeg.exe, ffprobe.exe (all self-contained),
# zipped as build\Offload-win-x64-<version>.zip (the version from Directory.Build.props).
# -Staging builds into build\staging\Offload instead (e.g. while build\Offload\Offload.exe is running); same zip.
# -Sign signs Offload.exe, bin\offload.exe and bin\braw-decode.exe before zipping (spec 4.5): -CertThumbprint <sha1>
#   (CurrentUser\My, then LocalMachine\My) or the variables OFFLOAD_SIGN_PFX + OFFLOAD_SIGN_PFX_PASSWORD; -TimestampUrl.
# -ReleaseUrl <where the zip will be> also writes build\offload-update.json for the update check (spec 4.4); -Notes.
param(
    [switch]$Staging,
    [switch]$Sign,
    [string]$CertThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$ReleaseUrl,
    [string]$Notes = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release.ps1')
$root = Split-Path -Parent $PSScriptRoot
$version = Get-OffloadVersion (Join-Path $root 'Directory.Build.props')
# Checked before anything is built: a missing signtool or certificate stops at once.
$signSetup = if ($Sign) { Get-OffloadSignSetup -CertThumbprint $CertThumbprint } else { $null }
$out = Join-Path $root $(if ($Staging) { 'build\staging\Offload' } else { 'build\Offload' })
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
```

`windows/scripts/build.ps1`, řádky 17–20 — najít:

```powershell
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (app) failed' }
# ffmpeg/ffprobe: windows\tools\bin first (pinned copy), otherwise whatever is on PATH.
# A tool on PATH under 5 MB is a launcher shim or a shared build (needs DLLs next to it): not bundled.
```

nahradit:

```powershell
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (app) failed' }
# Our own exes only (R23) — ffmpeg is not ours to sign.
if ($signSetup) {
    Invoke-OffloadSign -Setup $signSetup -TimestampUrl $TimestampUrl -Files @(
        (Join-Path $out 'Offload.exe'), (Join-Path $out 'bin\offload.exe'), (Join-Path $out 'bin\braw-decode.exe'))
}
# ffmpeg/ffprobe: windows\tools\bin first (pinned copy), otherwise whatever is on PATH.
# A tool on PATH under 5 MB is a launcher shim or a shared build (needs DLLs next to it): not bundled.
```

`windows/scripts/build.ps1`, řádky 53–59 — najít:

```powershell
New-Item -ItemType Directory -Force (Join-Path $out 'licence') | Out-Null
Copy-Item (Join-Path $luts 'README.md') (Join-Path $out 'licence\LUTy-README.md')
$zip = Join-Path $root 'build\Offload-win-x64.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $out -DestinationPath $zip
Write-Host "zipped $zip"
Write-Host "built $out"
```

nahradit:

```powershell
New-Item -ItemType Directory -Force (Join-Path $out 'licence') | Out-Null
Copy-Item (Join-Path $luts 'README.md') (Join-Path $out 'licence\LUTy-README.md')
$zip = Join-Path $root "build\Offload-win-x64-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $out -DestinationPath $zip
Write-Host "zipped $zip"
if ($ReleaseUrl) {
    # Upload the zip first, the manifest last (the manifest must never point at a zip that is not there yet).
    $manifest = Join-Path $root 'build\offload-update.json'
    Write-UpdateManifest -Path $manifest -Version $version -Url $ReleaseUrl -Notes $Notes
    Write-Host "manifest $manifest (version $version)"
}
Write-Host "built $out"
```

Vytvořit `windows/scripts/release.ps1`:

```powershell
﻿# Release helpers for build.ps1 (spec 4.4, 4.5): the version, the update manifest and code signing.
# Dot-source it: . (Join-Path $PSScriptRoot 'release.ps1'). Windows PowerShell 5.1 and PowerShell 7.
# Saved as UTF-8 with BOM: Windows PowerShell 5.1 reads a file without one in the ANSI code page (the Czech messages).

# The version from Directory.Build.props (R22: the one source), e.g. "0.4.0".
function Get-OffloadVersion([string]$Props) {
    $v = ([xml](Get-Content -Raw -Encoding UTF8 $Props)).Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $v) { throw "Verze nenalezena v $Props." }
    return $v.Trim()
}

# build\offload-update.json for the update check: {"version", "url", "notes"}, UTF-8 without BOM.
function Write-UpdateManifest([string]$Path, [string]$Version, [string]$Url, [string]$Notes) {
    $json = [ordered]@{ version = $Version; url = $Url; notes = $Notes } | ConvertTo-Json
    [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding $false))
}

# signtool.exe: $env:SIGNTOOL, else PATH, else the newest Windows SDK. A SIGNTOOL that points nowhere is an error
# (never silently another signtool).
function Find-SignTool {
    if ($env:SIGNTOOL) {
        if (Test-Path -LiteralPath $env:SIGNTOOL -PathType Leaf) { return $env:SIGNTOOL }
        throw 'signtool nenalezen — nainstalujte Windows SDK (Signing Tools for Desktop Apps), nebo nastavte SIGNTOOL.'
    }
    $onPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $sdk = Get-ChildItem -Path $kits -Directory -Filter '10.*' -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($sdk) { return $sdk }
    throw 'signtool nenalezen — nainstalujte Windows SDK (Signing Tools for Desktop Apps), nebo nastavte SIGNTOOL.'
}

# The password of the PFX never appears in anything printed.
function Hide-Secret([string]$Text) {
    if ($env:OFFLOAD_SIGN_PFX_PASSWORD) { return $Text.Replace($env:OFFLOAD_SIGN_PFX_PASSWORD, '***') }
    return $Text
}

# Checked before the build starts: signtool and the certificate — by thumbprint (CurrentUser\My, then LocalMachine\My;
# also a hardware token) or OFFLOAD_SIGN_PFX + OFFLOAD_SIGN_PFX_PASSWORD. An expired certificate is refused.
function Get-OffloadSignSetup([string]$CertThumbprint) {
    $signtool = Find-SignTool
    if ($CertThumbprint) {
        $t = ($CertThumbprint -replace '\s', '').ToUpperInvariant()
        $cert = Get-ChildItem -Path "Cert:\CurrentUser\My\$t" -ErrorAction SilentlyContinue
        if (-not $cert) { $cert = Get-ChildItem -Path "Cert:\LocalMachine\My\$t" -ErrorAction SilentlyContinue }
        if (-not $cert) { throw "Certifikát s otiskem $t nenalezen v CurrentUser\My ani LocalMachine\My." }
        if ($cert.NotAfter -lt (Get-Date)) { throw "Certifikát s otiskem $t vypršel $($cert.NotAfter.ToString('d. M. yyyy'))." }
        return @{ SignTool = $signtool; CertArgs = @('/sha1', $t) }
    }
    if ($env:OFFLOAD_SIGN_PFX) {
        if (-not (Test-Path -LiteralPath $env:OFFLOAD_SIGN_PFX -PathType Leaf)) { throw "Soubor certifikátu nenalezen: $env:OFFLOAD_SIGN_PFX (OFFLOAD_SIGN_PFX)." }
        if (-not $env:OFFLOAD_SIGN_PFX_PASSWORD) { throw 'Chybí OFFLOAD_SIGN_PFX_PASSWORD.' }
        try {
            $pfx = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($env:OFFLOAD_SIGN_PFX, $env:OFFLOAD_SIGN_PFX_PASSWORD)
        } catch {
            throw "Certifikát $env:OFFLOAD_SIGN_PFX nejde otevřít (špatné heslo v OFFLOAD_SIGN_PFX_PASSWORD?)."
        }
        if ($pfx.NotAfter -lt (Get-Date)) { throw "Certifikát $env:OFFLOAD_SIGN_PFX vypršel $($pfx.NotAfter.ToString('d. M. yyyy'))." }
        return @{ SignTool = $signtool; CertArgs = @('/f', $env:OFFLOAD_SIGN_PFX, '/p', $env:OFFLOAD_SIGN_PFX_PASSWORD) }
    }
    throw 'Není zadaný certifikát: použijte -CertThumbprint <otisk>, nebo nastavte OFFLOAD_SIGN_PFX a OFFLOAD_SIGN_PFX_PASSWORD.'
}

# Signs our own exes (R23) with SHA-256 and an RFC 3161 timestamp, then verifies. A timestamp that fails is tried
# 3 times, $RetryDelaySeconds apart (5 s); every failure ends the build with a Czech message.
function Invoke-OffloadSign([hashtable]$Setup, [string[]]$Files, [string]$TimestampUrl = 'http://timestamp.digicert.com', [int]$RetryDelaySeconds = 5) {
    foreach ($f in $Files) { if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "Soubor k podpisu nenalezen: $f" } }
    $signtool = $Setup.SignTool
    for ($attempt = 1; ; $attempt++) {
        $out = Hide-Secret ((& $signtool sign /fd SHA256 /td SHA256 /tr $TimestampUrl @($Setup.CertArgs) /d Offload @Files 2>&1) | Out-String)
        if ($LASTEXITCODE -eq 0) { break }
        if ($out -match '(?i)timestamp') {
            if ($attempt -lt 3) {
                Write-Warning "Časové razítko z $TimestampUrl se nepodařilo získat (pokus $attempt ze 3) — zkusím znovu."
                Start-Sleep -Seconds $RetryDelaySeconds
                continue
            }
            throw "Časové razítko se nepodařilo získat z $TimestampUrl."
        }
        throw "Podepsání selhalo: $($out.Trim())"
    }
    $out = Hide-Secret ((& $signtool verify /pa /all @Files 2>&1) | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "Podpis nejde ověřit: $($out.Trim())" }
    foreach ($f in $Files) { Write-Host "podepsáno: $f" }
}
```

`windows/src/Offload.Core/Format.cs`, řádky 1–3 — najít:

```csharp
using System.Globalization;

namespace Offload.Core;
```

nahradit:

```csharp
using System.Globalization;
using System.Reflection;

namespace Offload.Core;
```

`windows/src/Offload.Core/Format.cs`, řádky 5–9 — najít:

```csharp
public static class OffloadVersion
{
    public const string String = "0.3.0-win";
}

```

nahradit:

```csharp
public static class OffloadVersion
{
    /// "x.y.z-win": InformationalVersion of Directory.Build.props (R22 — the one place a version is set), read from this
    /// assembly. The report's `version`, the HTML footers and the update check use it.
    public static readonly string String =
        typeof(OffloadVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-win";
}

```


Soubor `windows/scripts/release.ps1` uložit jako **UTF-8 s BOM** (Windows PowerShell 5.1 čte soubor bez BOM v kódové stránce ANSI a české hlášky by byly rozbité). Nástroj Write zapisuje bez BOM — po vytvoření souboru spusťte:

```powershell
powershell -NoProfile -Command "$p = (Resolve-Path 'windows\scripts\release.ps1').Path; $t = [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8); [IO.File]::WriteAllText($p, $t, (New-Object Text.UTF8Encoding $true))"
```

Kontrola syntaxe obou skriptů (nic nespouští):

```powershell
powershell -NoProfile -Command "foreach ($f in 'windows\scripts\build.ps1','windows\scripts\release.ps1') { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $f).Path, [ref]$null, [ref]$e); \"$f $($e.Count)\" }"
```

Expected: `… build.ps1 0` a `… release.ps1 0`.

- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ReleaseScriptTests|FullyQualifiedName~FormatTests|FullyQualifiedName~ReportTests|FullyQualifiedName~HtmlReportTests|FullyQualifiedName~VerifyReportTests"`
Expected: PASS (testy skriptu spouštějí `powershell.exe` — každý ~1–2 s; nic nepodepisují doopravdy, `calls.txt` falešného signtoolu ukáže přesné argumenty). Pak celá sada (`-warnaserror`, vše zelené, 1149 testů: 1141 úspěšných + 8 přeskočených).

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 22–23 — najít:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 11 — distribuce: verze jen v `Directory.Build.props` (`OffloadVersion.String` z `AssemblyInformationalVersionAttribute`, test hlídá `x.y.z-win` a shodu s props); `build.ps1` balí `build\Offload-win-x64-<verze>.zip`, s `-ReleaseUrl` zapíše `build\offload-update.json` (`version`, `url`, `notes`; UTF-8 bez BOM); `-Sign` podepíše `Offload.exe`, `bin\offload.exe`, `bin\braw-decode.exe` (ffmpeg ne) před zabalením — certifikát otiskem (`CurrentUser\My`, pak `LocalMachine\My`) nebo `OFFLOAD_SIGN_PFX` + `OFFLOAD_SIGN_PFX_PASSWORD`, signtool z `SIGNTOOL` / `PATH` / Windows SDK, SHA-256 + razítko RFC 3161 (3 pokusy po 5 s), pak `signtool verify /pa /all`; signtool a certifikát (i prošlý) se kontrolují před buildem, chyby česky, heslo nikdy ve výpisu (`***`); logika v `scripts\release.ps1` (testy ve Windows PowerShell s falešným signtoolem); README: vydání verze, podepisování a jak získat certifikát (OV, EV, Artifact Signing, Certum, SmartScreen)
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Windows: version from Directory.Build.props, versioned zip, update manifest, build.ps1 -Sign

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/scripts/release.ps1 windows/scripts/build.ps1 windows/src/Offload.Core/Format.cs windows/README.md windows/tests/Offload.Core.Tests/ReleaseScriptTests.cs windows/tests/Offload.Core.Tests/FormatTests.cs
git commit -F .git/COMMIT_MSG_PLAN8
```

---

### Task 12: Dokumentace, návod, 0.4.0

Spec §5.3 (FORMATY), §8 (VYVOJ-MAC), §6.3 úkol 11 (dokumentace + 0.4.0; závěrečnou revizi dělá řídicí relace po tomto úkolu). Verze se zvedá **až tady** (R22): `Directory.Build.props` 0.4.0 a testy, které řetězec verze kontrolují.

**Files:**
- Modify: `windows/Directory.Build.props:6-11`
- Modify: `windows/tests/Offload.Core.Tests/FormatTests.cs:22-26`, `windows/tests/Offload.Core.Tests/ReportTests.cs:26-30`, `windows/tests/Offload.Core.Tests/HtmlReportTests.cs:16-20`
- Modify: `docs/FORMATY.md:84-89, 96-100, 108-113, 115-120, 238-241, 254-258, 269-270`
- Modify: `docs/ROZDILY.md:53-56, 147-150`
- Modify: `docs/VYVOJ-MAC.md:37-42`
- Modify: `docs/VYVOJ-WINDOWS.md:20, 22-23`
- Modify: `windows/README.md:20-23, 126-131, 267-270, 288-292`
- Modify: `windows/package/navod/navod.html:184-194, 305-308, 388-391, 678-682, 690-693, 823-826, 1110-1113`; `windows/package/Offload-navod.pdf` (znovu vytištěný)

**Interfaces:**
- Consumes: všechno z úkolů 1–11 (jména voleb, texty, klíče).
- Produces: verze `0.4.0` / `0.4.0-win`; dokumentace formátů a ruční testy 31–39.

- [ ] **Step 1: Testy na novou verzi (selžou)**

`windows/tests/Offload.Core.Tests/FormatTests.cs`, řádky 22–26 — najít:

```csharp

    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.3.0-win", OffloadVersion.String);

    // R22: the version comes from Directory.Build.props (InformationalVersion), shaped x.y.z-win — never typed twice.
```

nahradit:

```csharp

    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.4.0-win", OffloadVersion.String);

    // R22: the version comes from Directory.Build.props (InformationalVersion), shaped x.y.z-win — never typed twice.
```

`windows/tests/Offload.Core.Tests/HtmlReportTests.cs`, řádky 16–20 — najít:

```csharp
        Assert.Contains("<details><summary>Všechny soubory (2)</summary>", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.Contains("offload 0.3.0-win", html);
    }

```

nahradit:

```csharp
        Assert.Contains("<details><summary>Všechny soubory (2)</summary>", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.Contains("offload 0.4.0-win", html);
    }

```

`windows/tests/Offload.Core.Tests/ReportTests.cs`, řádky 26–30 — najít:

```csharp
        Assert.Equal("2026-10-04T08:00:00Z", root.GetProperty("started").GetString());
        Assert.Equal("offload", root.GetProperty("tool").GetString());
        Assert.Equal("0.3.0-win", root.GetProperty("version").GetString());
        Assert.Equal(0, root.GetProperty("clips").GetArrayLength());
        Assert.False(root.TryGetProperty("proxy", out _));
```

nahradit:

```csharp
        Assert.Equal("2026-10-04T08:00:00Z", root.GetProperty("started").GetString());
        Assert.Equal("offload", root.GetProperty("tool").GetString());
        Assert.Equal("0.4.0-win", root.GetProperty("version").GetString());
        Assert.Equal(0, root.GetProperty("clips").GetArrayLength());
        Assert.False(root.TryGetProperty("proxy", out _));
```


Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~FormatTests|FullyQualifiedName~ReportTests|FullyQualifiedName~HtmlReportTests"`
Expected: FAIL — `Version_IsWindowsBuild`, `ReportTests` (verze v JSON) a `HtmlReportTests` (patička) čekají `0.4.0-win`, sestavení má `0.3.0-win`.

- [ ] **Step 2: Verze a dokumentace**

`docs/FORMATY.md`, řádky 84–89 — najít:

```markdown
|---|---|---|
| `tool` | string | `offload` |
| `version` | string | `0.1.0` (Mac), `0.3.0-win` (Windows) |
| `generation` | číslo? | jen Windows od 0.2.0 a jen v reportu doplnění (`offload_report_NNNN.json`): číslo generace ASC MHL; chybí = 1 (celá kopie) |
| `hashAlgorithm` | string | `xxh64` (vždy, i s MD5 — Mac ho jen zobrazí) |
| `hashAlgorithms` | string[]? | jen Windows od 0.3.0 a jen když nejsou jen xxh64: `["xxh64", "md5"]` |
```

nahradit:

```markdown
|---|---|---|
| `tool` | string | `offload` |
| `version` | string | `0.1.0` (Mac), `0.4.0-win` (Windows) |
| `generation` | číslo? | jen Windows od 0.2.0 a jen v reportu doplnění (`offload_report_NNNN.json`): číslo generace ASC MHL; chybí = 1 (celá kopie) |
| `project` | string? | jen Windows od 0.4.0: projekt, ve kterém se karta kopírovala; chybí bez projektu (denní report ho ukáže, když ho mají všechny karty dne stejný) |
| `hashAlgorithm` | string | `xxh64` (vždy, i s MD5 — Mac ho jen zobrazí) |
| `hashAlgorithms` | string[]? | jen Windows od 0.3.0 a jen když nejsou jen xxh64: `["xxh64", "md5"]` |
```

`docs/FORMATY.md`, řádky 96–100 — najít:

```markdown
| `skipped[]` | string | vynechané systémové položky |
| `clips[]` | objekt | `clip` {`name`, `path`, `format`, `segments`, `bytes`, `isCameraProxy`}, `info`?, `probeError`?, `proxyStatus`, `proxyPath`?, `proxyNote`?, `proxySeconds`?, `lut`? {`source`, `id`, `name`, `reason`} (použitý LUT a proč; Windows od plánu 5, Mac zatím ignoruje) |
| `proxy` | objekt? | nastavení proxy (`enabled`, `height`, `codec`, `bitrateMbps`, `lutPath`, `lutMode` (`auto`/`builtin`/`custom`/`none`), `lutBuiltin`, `lutFallback`, `burnFilename`, `burnTimecode`, `audio`, `skipCameraProxies`, `copyToAllDestinations`, `frameAspect`, `frameScale`; jen Windows, volitelně `extraFolder` — viz níže) |
| `proxyEncoder` | string? | jen Windows: enkodér ffmpeg, kterým se renderovaly proxy (`h264_nvenc`, `libx264`, `prores_ks` …); chybí, když se žádná proxy nevyrenderovala |

```

nahradit:

```markdown
| `skipped[]` | string | vynechané systémové položky |
| `clips[]` | objekt | `clip` {`name`, `path`, `format`, `segments`, `bytes`, `isCameraProxy`}, `info`?, `probeError`?, `proxyStatus`, `proxyPath`?, `proxyNote`?, `proxySeconds`?, `lut`? {`source`, `id`, `name`, `reason`} (použitý LUT a proč; Windows od plánu 5, Mac zatím ignoruje) |
| `proxy` | objekt? | nastavení proxy (`enabled`, `height`, `codec`, `bitrateMbps`, `lutPath`, `lutMode` (`auto`/`builtin`/`custom`/`none`), `lutBuiltin`, `lutFallback`, `burnFilename`, `burnTimecode`, `audio`, `skipCameraProxies`, `copyToAllDestinations`, `frameAspect`, `frameScale`; jen Windows, volitelně `extraFolder`, od 0.4.0 `lutRules` a `brawGen4ToGen5` — viz níže) |
| `proxyEncoder` | string? | jen Windows: enkodér ffmpeg, kterým se renderovaly proxy (`h264_nvenc`, `libx264`, `prores_ks` …); chybí, když se žádná proxy nevyrenderovala |

```

`docs/FORMATY.md`, řádky 108–113 — najít:

```markdown
  (vlastní LUT jako dřív), bez `lutPath` → `auto` se záložním „bez LUT“. `lutFallback` je id vestavěného LUTu nebo `null`.
- `clips[].lut.source`: `auto` (rozpoznaná kamera), `fallback` (kamera nerozpoznaná, záložní LUT), `builtin` (vestavěný
  LUT vybraný v nastavení), `custom` (vlastní `.cube`), `none` (bez LUTu — důvod v `reason`). `id` je id vestavěného
  LUTu, u `custom` a `none` `null`.
- `clips[].info.gamma`, `gamut`, `colorScienceGen` (volitelné, zatím jen Windows): u Blackmagic RAW gama, gamut a generace
  barevné vědy, ve kterých byl klip natočený (z `braw-decode --info`, např. `"Blackmagic Design Film"`, `5`). Podle nich se
```

nahradit:

```markdown
  (vlastní LUT jako dřív), bez `lutPath` → `auto` se záložním „bez LUT“. `lutFallback` je id vestavěného LUTu nebo `null`.
- `clips[].lut.source`: `auto` (rozpoznaná kamera), `fallback` (kamera nerozpoznaná, záložní LUT), `builtin` (vestavěný
  LUT vybraný v nastavení), `custom` (vlastní `.cube`), `rule` (Windows od 0.4.0: vlastní pravidlo, `reason` „pravidlo 2:
  kamera obsahuje „FX6““; `id` vestavěného LUTu nebo `null` u vlastního `.cube`), `none` (bez LUTu — důvod v `reason`,
  i pravidlo s „Bez LUT“). `id` je id vestavěného LUTu, u `custom` a `none` `null`.
- `clips[].info.gamma`, `gamut`, `colorScienceGen` (volitelné, zatím jen Windows): u Blackmagic RAW gama, gamut a generace
  barevné vědy, ve kterých byl klip natočený (z `braw-decode --info`, např. `"Blackmagic Design Film"`, `5`). Podle nich se
```

`docs/FORMATY.md`, řádky 115–120 — najít:

```markdown
- `clips[].info.reel` (volitelné, Windows od 0.3.0): cívka klipu — tag kamery (`reel_name`, `tape_name`, `reel` z ffprobe),
  jinak cívka z názvu klipu (`A001C003…` → `A001`), jinak název složky karty. Je i v proxy (stopa timecodu). Mac ignoruje.

Náhledy klipů (JPEG) se do JSON neukládají — jsou jen v HTML a PDF.

`proxy.extraFolder` (string, volitelný, zatím jen Windows): složka, do které se každá hotová proxy kopíruje navíc,
```

nahradit:

```markdown
- `clips[].info.reel` (volitelné, Windows od 0.3.0): cívka klipu — tag kamery (`reel_name`, `tape_name`, `reel` z ffprobe),
  jinak cívka z názvu klipu (`A001C003…` → `A001`), jinak název složky karty. Je i v proxy (stopa timecodu). Mac ignoruje.
- `clips[].info.cameraSerial` (volitelné, Windows od 0.4.0): sériové číslo těla kamery — Sony `M01.XML` (`Device serialNo`),
  ffprobe `com.apple.quicktime.camera.identifier` / `camera_serial_number`; ARRI a BRAW zatím ne (ověří vzorky). Mac ignoruje.
- `clips[].info.colorScienceGenOriginal` (volitelné, Windows od 0.4.0): u BRAW dekódovaného s převodem Film Gen 4 → Gen 5
  generace, ve které byl klip natočený (`colorScienceGen` je pak účinná, `5`). Mac ignoruje.

Náhledy klipů (JPEG) se do JSON neukládají — jsou jen v HTML a PDF.

`proxy.lutRules` (pole, volitelné, Windows od 0.4.0; chybí, když žádné není): vlastní pravidla LUTů v pořadí priority,
každé `{ "match": "camera" | "serial" | "reel" | "folder", "value": "…", "lut": "<id vestavěného>" | "none" }` nebo
s `"lutPath": "<cesta k .cube>"` místo `lut` (právě jedno z nich). `camera` = model obsahuje hodnotu, `serial` = sériové
číslo se rovná, `reel` = cívka začíná hodnotou, `folder` = `<karta>/<složka klipu>` odpovídá vzoru s `*` a `?` (bez
ohledu na velikost písmen). Platí jen v režimu `lutMode: auto`, před rozpoznáním kamery. Neplatné pravidlo (prázdná hodnota,
obojí / nic, neznámé `match`) se při načtení zahodí. `proxy.brawGen4ToGen5` (bool, chybí = `false`, Windows od 0.4.0):
BRAW Film Gen 4 dekódovat jako Gen 5 (`braw-decode --gen5`). Oba klíče jsou i v `settings.json`.

`proxy.extraFolder` (string, volitelný, zatím jen Windows): složka, do které se každá hotová proxy kopíruje navíc,
```

`docs/FORMATY.md`, řádky 238–241 — najít:

```markdown
- `outputs` (Windows od 0.3.0): `dayReport` (bool, `true`) — denní report po každé úloze; `clipListCsv` (bool, `true`),
  `clipListAle` (bool, `false`) — seznamy klipů. Neznámé klíče sekce se zachovají.

## Místní soubory (jen tento počítač, Mac převezme stejný tvar)
```

nahradit:

```markdown
- `outputs` (Windows od 0.3.0): `dayReport` (bool, `true`) — denní report po každé úloze; `clipListCsv` (bool, `true`),
  `clipListAle` (bool, `false`) — seznamy klipů. Neznámé klíče sekce se zachovají.
- Projekty (Windows od 0.4.0): klíče profilu na horní úrovni (`destRoots`, `verify`, `analyzeClips`, `hash`, `naming`,
  `outputs`, `notify`, `proxy`) jsou profil „Bez projektu“; `projects` je pole pojmenovaných profilů stejného tvaru
  s klíčem `name` (oříznutý, 1–60 znaků, jedinečný bez ohledu na velikost písmen, „Bez projektu“ vyhrazené; dvakrát stejné
  jméno → platí poslední; položka bez platného jména se vynechá); `activeProject` je jméno nebo `null` (neexistující →
  „Bez projektu“). `projects`, které není pole, se zachová beze změny, dokud se v aplikaci neuloží projekt. Společné
  zůstávají `tools`, `portNames` a `updates`.
- `notify` (Windows od 0.4.0, součást projektu): `windows` (bool, `true`) — oznámení Windows; `ntfy` { `enabled` (bool,
  `false`), `server` (string, `https://ntfy.sh`), `topic` (string, `""`) } — bez přihlašovacího tokenu.
- `updates` (Windows od 0.4.0, společné): `manifest` (string | `null`, výchozí `null` = kontrola vypnutá) — http(s) adresa
  nebo cesta k `offload-update.json` (`{"version": "0.4.0", "url": "…", "notes": "…"}`, UTF-8).

## Místní soubory (jen tento počítač, Mac převezme stejný tvar)
```

`docs/FORMATY.md`, řádky 254–258 — najít:

```markdown
| `card` | objekt? | svazek karty: `identity` (`\\?\Volume{…}\|1A2B3C4D`, jen informace), `fsSerial`, `capacity`, `label`, `filesystem`; u ručně přidané složky `null` |
| `source`, `cardName`, `date` | string | zdroj, název složky karty, složka data |
| `project` | string? | projekt (plní plán 8), jinak `null` |
| `finished` | datum | konec poslední kopie / doplnění |
| `success`, `verified` | bool | všechny generace bez chyb / ověřené |
```

nahradit:

```markdown
| `card` | objekt? | svazek karty: `identity` (`\\?\Volume{…}\|1A2B3C4D`, jen informace), `fsSerial`, `capacity`, `label`, `filesystem`; u ručně přidané složky `null` |
| `source`, `cardName`, `date` | string | zdroj, název složky karty, složka data |
| `project` | string? | projekt, ve kterém se karta kopírovala (Windows od 0.4.0), jinak `null`; doplnění ho nemění |
| `finished` | datum | konec poslední kopie / doplnění |
| `success`, `verified` | bool | všechny generace bez chyb / ověřené |
```

`docs/FORMATY.md`, řádky 269–270 — najít:

```markdown
ručně přidaná složka podle `shape`. Doplnění zapíše do záznamu další generaci pod jedním držením zámku.

```

nahradit:

```markdown
ručně přidaná složka podle `shape`. Doplnění zapíše do záznamu další generaci pod jedním držením zámku.

`%APPDATA%\Offload\update-check.json` (jen Windows, od 0.4.0): stav kontroly aktualizací — `lastCheck` (datum UTC
posledního čtení manifestu, i neúspěšného), `lastVersion`, `lastUrl`, `lastNotes` (co manifest naposledy řekl). Manifest
se čte nejvýš jednou za 24 h (`lastCheck` v budoucnosti = číst hned); poškozený soubor = číst znovu.

```

`docs/ROZDILY.md`, řádky 53–56 — najít:

```markdown
| **Proxy pro přelinkování** (`reel_name` ve stopě TC, všechny zvukové stopy) | ⏳ naplánováno (1 zvuková stopa) | ✅ (Resolve/Premiere čeká ruční test) |
| **MD5 navíc k xxh64** (`hash: both`, `--hash both`, `<karta>.md5`, MHL `<md5>`) | ⏳ naplánováno (MD5 ignoruje, xxh64 ověří) | ✅ CLI + GUI |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ❌ | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI: řetěz připojení s rychlostí, % linky, součtem na řadiči a úzkým hrdlem na kartě úlohy |

```

nahradit:

```markdown
| **Proxy pro přelinkování** (`reel_name` ve stopě TC, všechny zvukové stopy) | ⏳ naplánováno (1 zvuková stopa) | ✅ (Resolve/Premiere čeká ruční test) |
| **MD5 navíc k xxh64** (`hash: both`, `--hash both`, `<karta>.md5`, MHL `<md5>`) | ⏳ naplánováno (MD5 ignoruje, xxh64 ověří) | ✅ CLI + GUI |
| **Projekty** (`projects`, `activeProject`, `--project`, `project` v reportu a historii) | ⏳ naplánováno | ✅ CLI + GUI (čeká ruční test) |
| **Vlastní pravidla LUTů** (`proxy.lutRules`, `--lut-rule`, `lut.source = rule`, sériové číslo kamery) | ⏳ naplánováno | ✅ CLI + GUI |
| **BRAW Film Gen 4 → Gen 5** (`braw-decode --gen5`, `proxy.brawGen4ToGen5`, `--braw-gen5`) | ⏳ naplánováno (C++ `--gen5`) | ✅ kód, výchozí vypnuto — čeká vzorek .braw Gen 4 |
| **Oznámení** (Windows: oznamovací oblast; ntfy na telefon, `--ntfy`) | Mac má notifikace; ntfy ⏳ naplánováno | ✅ CLI (ntfy) + GUI (čeká ruční test) |
| **Kontrola aktualizací** (manifest z URL / NAS, 1× za 24 h) | ⏳ naplánováno (vlastní manifest) | ✅ GUI |
| **Podpis exe** (`build.ps1 -Sign`, signtool) | `codesign` + notarizace (mimo tento návrh) | ✅ skript (čeká certifikát) |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ❌ | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI: řetěz připojení s rychlostí, % linky, součtem na řadiči a úzkým hrdlem na kartě úlohy |

```

`docs/ROZDILY.md`, řádky 147–150 — najít:

```markdown
- **MD5** (zatím jen Windows): volba xxHash64 + MD5 přidá `hashAlgorithms`, `files[].md5`, `<karta>.md5` a MHL `<md5>`;
  xxh64 je vždy, takže zálohu s MD5 Mac ověří dál (MD5 přeskočí). Doplnění počítá součty dřívější zálohy.
- **Denní report a seznamy klipů** (zatím jen Windows): `<datum>/_REPORT/` se denním reportem a `_clips.csv/.ale` karty
  i dne jsou odvozené soubory (přepisují se celé); Mac je nečte a jeho `UniqueCardFolder` s `_REPORT` ve složce dne
```

nahradit:

```markdown
- **MD5** (zatím jen Windows): volba xxHash64 + MD5 přidá `hashAlgorithms`, `files[].md5`, `<karta>.md5` a MHL `<md5>`;
  xxh64 je vždy, takže zálohu s MD5 Mac ověří dál (MD5 přeskočí). Doplnění počítá součty dřívější zálohy.
- **Projekty** (zatím jen Windows): horní klíče `settings.json` jsou profil „Bez projektu“, `projects[]` pojmenované
  profily, `activeProject` (FORMATY.md, settings.json). Report má navíc `project` (Mac ignoruje), `history.json` ho plní;
  „další v řadě“ počítá karty jen v rámci projektu. Mac uloží totéž do `UserDefaults`.
- **Oznámení** (Windows): ikona v oznamovací oblasti přes `Shell_NotifyIconW` (ne WinRT toast — ten u nebalené aplikace
  potřebuje zástupce s AUMID v nabídce Start), jen když okno není v popředí; ntfy jako JSON POST (jen název karty,
  výsledek a počty). Mac má `UNUserNotificationCenter`; texty a ntfy převezme.
- **Denní report a seznamy klipů** (zatím jen Windows): `<datum>/_REPORT/` se denním reportem a `_clips.csv/.ale` karty
  i dne jsou odvozené soubory (přepisují se celé); Mac je nečte a jeho `UniqueCardFolder` s `_REPORT` ve složce dne
```

`docs/VYVOJ-MAC.md`, řádky 37–42 — najít:

```markdown
  xxh64/both, `hashAlgorithms`, `files[].md5`, `<karta>.md5`, MHL `<md5>`, `verify` z MHL)); **jména karet** `_REPORT`, `_PROXY`, `ascmhl` s `_` na konci a prázdná `<karta>\_REPORT` hned při začátku kopie (D1); seznam ALE bez BOM a volba doplnění: `<karta>.md5` je třetí soubor, který doplnění přepíše (jako `.xxh64`).

- **Mimo plán — zbývá i po něm:**
  - Blackmagic RAW Film Gen 4: zatím bez LUTu (provizorně, jako Windows) — rozhodne uživatel; platí pro obě verze.
  - Nastavení nástrojů: řádek „braw-decode“ ukazuje „nalezeno“ i bez knihovny Blackmagic RAW (stav BRAW je vidět z poznámky
    u klipu) — otevřené i na Windows.
```

nahradit:

```markdown
  xxh64/both, `hashAlgorithms`, `files[].md5`, `<karta>.md5`, MHL `<md5>`, `verify` z MHL)); **jména karet** `_REPORT`, `_PROXY`, `ascmhl` s `_` na konci a prázdná `<karta>\_REPORT` hned při začátku kopie (D1); seznam ALE bez BOM a volba doplnění: `<karta>.md5` je třetí soubor, který doplnění přepíše (jako `.xxh64`).

- **Nastavení, oznámení, distribuce (Windows plán 8, 2026-10-07):** návrh
  [`windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md`](../windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md)
  §4 a §8. Mac převezme: **projekty** (`projects` / `activeProject` v `UserDefaults`, stejné chování přepínače, `project`
  v reportu a historii, další v řadě po projektech, projekt v hlavičce denního reportu); **vlastní pravidla LUTů**
  (`lutRules` v `LutResolver` před automatikou, `lut.source = rule`, `cameraSerial` ze Sony XML a QuickTime tagů);
  **`braw-decode --gen5`** v C++ (klon atributů klipu, `ColorScienceGen` = 5, `colorScienceGenOriginal` v `--info`);
  **oznámení** (texty `JobOutcome`, ntfy JSON POST bez cest); **kontrolu aktualizací** (formát manifestu; vlastní
  soubor pro Mac rozhodne Mac). Podpis má Mac vlastní (`codesign` + notarizace).

- **Mimo plán — zbývá i po něm:**
  - Blackmagic RAW Film Gen 4: Windows od 0.4.0 umí převod na Gen 5 (`--gen5`, výchozí vypnuto, čeká vzorek Gen 4); Mac
    zatím bez LUTu.
  - Nastavení nástrojů: řádek „braw-decode“ ukazuje „nalezeno“ i bez knihovny Blackmagic RAW (stav BRAW je vidět z poznámky
    u klipu) — otevřené i na Windows.
```

`windows/Directory.Build.props`, řádky 6–11 — najít:

```xml
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Version>0.3.0</Version>
    <InformationalVersion>0.3.0-win</InformationalVersion>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
```

nahradit:

```xml
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Version>0.4.0</Version>
    <InformationalVersion>0.4.0-win</InformationalVersion>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
```

`windows/README.md`, řádky 20–23 — najít:

```markdown
| Karty a bezpečnost dat (plán 6, 0.2.0): historie, doplnění, název z klipů, ověření zálohy, místo, české hlášky | hotovo (čeká ruční test s kartou) |
| Výstupy pro produkci a střih (plán 7, 0.3.0): denní report, seznam klipů CSV/ALE, proxy pro přelinkování, MD5 | hotovo (čeká ruční test v Resolve, Premiere, Avid, Excelu) |

## Build a testy
```

nahradit:

```markdown
| Karty a bezpečnost dat (plán 6, 0.2.0): historie, doplnění, název z klipů, ověření zálohy, místo, české hlášky | hotovo (čeká ruční test s kartou) |
| Výstupy pro produkci a střih (plán 7, 0.3.0): denní report, seznam klipů CSV/ALE, proxy pro přelinkování, MD5 | hotovo (čeká ruční test v Resolve, Premiere, Avid, Excelu) |
| Nastavení, oznámení, distribuce (plán 8, 0.4.0): projekty, vlastní pravidla LUTů, BRAW Film Gen 4 → Gen 5, oznámení Windows a ntfy, kontrola aktualizací, podpis | hotovo (čeká ruční test: oznámení, podpis s certifikátem; převod Gen 4 čeká vzorek .braw Gen 4) |

## Build a testy
```

`windows/README.md`, řádky 126–131 — najít:

```markdown

Spuštění: `Offload\Offload.exe` (jeden soubor, .NET v něm je; první start trvá déle, protože se rozbalí nativní knihovny WPF).
Aplikace není podepsaná, takže při prvním spuštění Windows ukáže „Windows ochránil váš počítač“ —
klikněte na „Další informace“ → „Přesto spustit“.

- **Karty:** karta vložená do čtečky se objeví sama a je zaškrtnutá; složku lze přidat tlačítkem „Složka…“ nebo přetažením
```

nahradit:

```markdown

Spuštění: `Offload\Offload.exe` (jeden soubor, .NET v něm je; první start trvá déle, protože se rozbalí nativní knihovny WPF).
Bez podpisu (build bez `-Sign`, viz Podepisování) Windows při prvním spuštění ukáže „Windows ochránil váš počítač“ —
klikněte na „Další informace“ → „Přesto spustit“.

- **Projekt:** nahoře v levém panelu „Projekt: [Bez projektu ▾]“ — pojmenovaná nastavení (cíle, kontrola, součet, název
  z klipů, výstupy, oznámení, proxy včetně LUTu, pravidel LUTů a složky pro proxy). Menu *Projekt*: „Uložit jako
  projekt…“, „Přejmenovat…“, „Smazat…“. Titulek okna „Offload — Film X“. Přepnutí nezmění úlohy ve frontě (mají svou
  kopii nastavení). Nástroje, názvy USB portů a kontrola aktualizací jsou společné.
- **Pravidla LUTů:** u „Automaticky podle kamery“ tlačítko „Pravidla LUTů… (N)“ — řádky *kamera / sériové číslo / cívka /
  složka → LUT*, zkoušejí se shora dolů před rozpoznáním kamery; report u klipu ukáže „pravidlo 2: …“.
- **Oznámení:** Nástroje → Oznámení… — oznámení Windows (když okno není v popředí) a volitelně ntfy na telefon (jen
  název karty a výsledek). Nástroje → Aktualizace… — adresa manifestu a „Zkontrolovat teď“; nová verze se ukáže
  odkazem vpravo v řádku menu.

- **Karty:** karta vložená do čtečky se objeví sama a je zaškrtnutá; složku lze přidat tlačítkem „Složka…“ nebo přetažením
```

`windows/README.md`, řádky 267–270 — najít:

```markdown
      může číst špatně — poznamenat verzi)

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

```

nahradit:

```markdown
      může číst špatně — poznamenat verzi)

Plán 8 (nastavení, oznámení, distribuce, 0.4.0):

- [ ] 31. **Projekty:** „Uložit jako projekt…“ „Film X“ → titulek „Offload — Film X“; změnit cíle a proxy, přepnout na „Bez
      projektu“ a zpět — obojí má svoje hodnoty; přejmenovat a smazat (dotaz „Smazat projekt …? … zálohy na discích
      zůstanou.“, po smazání „Bez projektu“); jméno prázdné / „bez projektu“ / už použité → červeně pod polem, OK nejde
- [ ] 32. **Přepnutí s frontou (review focus 1):** dvě karty ve frontě z „Film X“ (s proxy) → během renderu proxy přepnout na
      jiný projekt s jinými cíli a kodekem → úlohy dokončí cíle, kodek, LUT i oznámení „Film X“; report a `history.json`
      mají `project` „Film X“; další v řadě (A002 …) se počítá zvlášť v každém projektu; denní report dne jen z „Film X“
      má v hlavičce „Projekt: Film X“
- [ ] 33. **Starší / ručně upravený settings.json:** dva projekty stejného jména → platí poslední; `projects` jako text →
      aplikace naběhne bez projektů, ostatní nastavení zůstane; spustit 0.3.0 a zase 0.4.0 → projekty a pravidla zůstaly
- [ ] 34. **Pravidla LUTů:** Sony FX6 (`M01.XML` se `serialNo`) → pravidlo „Sériové číslo kamery“ → LUT a v reportu
      „pravidlo 1: sériové číslo kamery „…““; pravidlo „Složka“ s vlastním `.cube`, pak `.cube` přejmenovat → červeně „LUT
      z pravidla 1 nenalezen: …“, start nejde; ↑ ↓ ✕ mění pořadí / maže; tlačítko jen u „Automaticky podle kamery“
- [ ] 35. **BRAW Gen 4 (až bude vzorek):** `OFFLOAD_BRAW_SAMPLE=<klip Gen 4>` → `dotnet test --filter BrawSampleTests`
      (Gen5_ConvertsAGen4Clip_LeavesGen5); v GUI Dekodéry → „Blackmagic RAW Film Gen 4 převést na Gen 5“ → proxy má LUT
      a vypadá jako v Blackmagic RAW Playeru s Gen 5; v reportu „Blackmagic RAW: Film Gen 4 → Gen 5 (převod SDK)“.
      Teprve pak zvážit zapnutí jako výchozí (R26)
- [ ] 36. **Oznámení Windows (TrayNotifier, jen ručně):** okno na pozadí → po kopii „Offload: A001 — kartu lze vysunout“
      (s proxy), po konci „Offload: A001 hotovo“; s chybou červená ikona; kliknutí na oznámení / ikonu vrátí okno; okno
      v popředí → žádné oznámení, jen zvuk a blikání; oznámení zůstane v Centru oznámení; po zavření aplikace ikona zmizí;
      restart Průzkumníka → další oznámení ikonu přidá znovu; Nástroje → Oznámení… → „Oznámení Windows“ vypnout → nic
- [ ] 37. **ntfy:** téma `offload-<náhodné>` → „Poslat zkušební oznámení“ → „Odesláno.“ a zpráva v aplikaci ntfy na
      telefonu; server `https://nic.invalid` → „Nepodařilo se: …“ do 10 s, aplikace celou dobu reaguje; téma s diakritikou
      → OK nejde, česky proč; v textu na telefonu žádná cesta ani jméno PC; CLI `--ntfy https://ntfy.sh/<téma>`
- [ ] 38. **Aktualizace:** `build.ps1 -ReleaseUrl \\NAS\Offload\` → `offload-update.json`; Nástroje → Aktualizace… → cesta
      k manifestu → „Zkontrolovat teď“ → „Je k dispozici verze …“ / „Máte nejnovější verzi …“, odkaz „Nová verze …“ vpravo
      v menu, *Otevřít* otevře složku; odpojený NAS → „Manifest nejde načíst: … neodpověděl do 5 s“ a okno nezamrzne
- [ ] 39. **Podpis (stroj s Windows SDK):** `build.ps1 -Sign` bez signtoolu / s neznámým otiskem → česká chyba hned na
      začátku; testovací PFX (`New-SelfSignedCertificate -CodeSigningCert`, export) přes `OFFLOAD_SIGN_PFX` → podepíše tři
      exe, ve vlastnostech souboru je karta Digitální podpisy; nedostupné razítko (`-TimestampUrl http://nic.invalid`) →
      3 pokusy, „Časové razítko se nepodařilo získat …“; heslo se nikde nevypíše

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

```

`windows/README.md`, řádky 288–292 — najít:

````markdown
offload day-report D:\Zaloha\2026-10-06                     # denní report znovu (HTML, PDF, seznam klipů dne)
offload bench --hash md5                                    # rychlost MD5 proti xxh64 (CPU)
```

Výstupy (plán 7): po každé kopii nebo doplnění vznikne `_REPORT\<karta>_clips.csv` (`--no-clip-list` vypne, `--ale`
````

nahradit:

````markdown
offload day-report D:\Zaloha\2026-10-06                     # denní report znovu (HTML, PDF, seznam klipů dne)
offload bench --hash md5                                    # rychlost MD5 proti xxh64 (CPU)
offload E:\ --project "Film X"                              # nastavení projektu z aplikace; volby z příkazové řádky mají přednost
offload E:\ -d D:\Zaloha --proxy --lut-rule camera=FX6:sony-slog3-sgamut3cine --lut-rule "folder=A00*/*:C:\luts\show.cube"
offload E:\ -d D:\Zaloha --proxy --braw-gen5                 # BRAW Film Gen 4 jako Gen 5 (ověří až vzorek Gen 4)
offload E:\ -d D:\Zaloha --ntfy https://ntfy.sh/muj-tajny-kanal   # výsledek na telefon (jen název karty a výsledek)
```

Plán 8: `--project <název>` vezme projekt uložený v aplikaci (`settings.json`; cíle, ověření, součet, výstupy, oznámení,
proxy) jako výchozí hodnoty — co je napsané na příkazové řádce, má přednost (`-d` nahradí cíle projektu, `--lut-rule`
jeho pravidla); projekt, který navrhuje název z klipů, se chová jako `--name auto`; neznámý projekt → „Chyba: Projekt
„X“ neexistuje. Projekty: …“ (kód 1). `--lut-rule <camera|serial|reel|folder>=<hodnota>:<lut id|none|cesta.cube>`
(opakovatelné, pořadí = priorita, platí jen s `--lut auto`). `--ntfy <url>` pošle výsledek (i odmítnutí před kopií)
na ntfy, nejvýš 10 s po konci; oznámení Windows CLI neposílá.

Výstupy (plán 7): po každé kopii nebo doplnění vznikne `_REPORT\<karta>_clips.csv` (`--no-clip-list` vypne, `--ale`
````

`windows/package/navod/navod.html`, řádky 184–194 — najít:

```html
  <ol>
    <li><span class="n">1</span><span>Co Offload dělá<span class="sub">kopie, ověření, report; proxy, náhledy, LUTy; na kartu se nikdy nezapisuje</span></span></li>
    <li><span class="n">2</span><span>Instalace a první spuštění<span class="sub">rozbalení ZIPu, SmartScreen, požadavky, volitelné doplňky, kde je nastavení a log</span></span></li>
    <li><span class="n">3</span><span>Hlavní okno<span class="sub">karty, cíle, volby, dekodéry, hardware, lišta se spuštěním, fronta</span></span></li>
    <li><span class="n">4</span><span>Krok za krokem: první offload<span class="sub">od vložení karty po vysunutí</span></span></li>
    <li><span class="n">5</span><span>Volby kopírování a ověření<span class="sub">datum, kontrolní součet (MD5), ověření, analýza klipů, seznamy klipů, denní report, co se kontroluje před kopií</span></span></li>
    <li><span class="n">6</span><span>Proxy, náhledy a LUTy<span class="sub">nastavení proxy, přelinkování (reel name, zvukové stopy), složka pro proxy navíc, automatický výběr LUTu, RAW formáty</span></span></li>
    <li><span class="n">7</span><span>Report<span class="sub">co je v HTML a PDF, tři snímky na klip, řádky LUT, seznam klipů CSV / ALE, denní report</span></span></li>
    <li><span class="n">8</span><span>Fronta úloh, zrušení, vysunutí karty<span class="sub">stavy úlohy, zrušení, zavření aplikace, vysunutí</span></span></li>
    <li><span class="n">9</span><span>Nástroje → Prověřit USB porty… a okno Hardware<span class="sub">zapojení, doporučení, měření, názvy portů, úzké hrdlo při kopii</span></span></li>
    <li><span class="n">10</span><span>Co se nachází na cílovém disku<span class="sub">struktura složek, _REPORT karty i dne, ascmhl, _PROXY</span></span></li>
```

nahradit:

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
```

`windows/package/navod/navod.html`, řádky 305–308 — najít:

```html
<p>Do Průzkumníka stačí do adresního řádku napsat <code>%APPDATA%\Offload</code>.</p>

<h3>Odinstalace</h3>
<p>Smažte složku s aplikací. Nastavení zůstane v <code>%APPDATA%\Offload</code>, dokud ho nesmažete.</p>
```

nahradit:

```html
<p>Do Průzkumníka stačí do adresního řádku napsat <code>%APPDATA%\Offload</code>.</p>

<h3>Aktualizace</h3>
<p>Offload sám nic nestahuje. V <b class="ui">Nástroje → Aktualizace…</b> zadejte <b>adresu manifestu</b> — webovou adresu
nebo cestu na NAS či sdílenou složku (např. <code>\\NAS\Offload\offload-update.json</code>), kterou vám dá ten, kdo
Offload připravuje. Aplikace se pak nejvýš jednou denně (5 s po spuštění) podívá, jestli je nová verze; když ano,
vpravo v řádku menu se objeví odkaz <span class="q">„Nová verze 0.5.0“</span>. Okno ukáže poznámky k verzi a tlačítko
<b class="ui">Otevřít</b>, které otevře stránku nebo složku s novým ZIPem. Tlačítko <b class="ui">Zkontrolovat teď</b>
zkusí hned: <span class="q">„Máte nejnovější verzi …“</span>, nebo <span class="q">„Manifest nejde načíst: …“</span>
(nedostupný disk se vzdá do 5 s). Prázdná adresa = kontrola vypnutá.</p>

<h3>Odinstalace</h3>
<p>Smažte složku s aplikací. Nastavení zůstane v <code>%APPDATA%\Offload</code>, dokud ho nesmažete.</p>
```

`windows/package/navod/navod.html`, řádky 388–391 — najít:

```html
  <div><span class="callout">9</span><b>Fronta</b> — karty úloh, kapitola 8.</div>
</div>

<h3>Karty</h3>
```

nahradit:

```html
  <div><span class="callout">9</span><b>Fronta</b> — karty úloh, kapitola 8.</div>
</div>

<h3>Projekt</h3>
<p>Nahoře v levém panelu je <b class="ui">Projekt:</b> se seznamem <b>Bez projektu</b> a uložených projektů. Projekt je
pojmenované nastavení — cíle, struktura a kontrola (ověření, kontrolní součet, název z klipů, seznamy klipů, denní
report), oznámení a celá sekce Proxy včetně LUTu, pravidel LUTů a složky pro proxy. Co změníte, uloží se do právě
vybraného projektu; přepnutím na jiný a zpět se nic neztratí. Úlohy, které už jsou ve frontě, si nastavení vzaly při
spuštění — přepnutí projektu je nezmění. Společné pro všechny projekty zůstávají cesty k nástrojům, názvy USB portů
a kontrola aktualizací.</p>
<p>Menu <b class="ui">Projekt</b>: <b class="ui">Uložit jako projekt…</b> (nový projekt z toho, co je teď nastavené; stane
se vybraným), <b class="ui">Přejmenovat…</b> a <b class="ui">Smazat…</b> (<span class="q">„Smazat projekt Film X? Nastavení
projektu se ztratí, zálohy na discích zůstanou.“</span>). Název: 1–60 znaků, nesmí se opakovat (ani jinými velkými
písmeny) a <span class="q">„Bez projektu“</span> je vyhrazené. Titulek okna ukazuje vybraný projekt, např.
<span class="q">„Offload — Film X“</span>. Projekt se zapíše do reportu a historie: návrh <span class="q">„další v řadě“</span>
počítá jen karty stejného projektu a denní report ukáže <span class="q">„Projekt: Film X“</span>, když jsou z něj všechny
karty dne.</p>

<h3>Karty</h3>
```

`windows/package/navod/navod.html`, řádky 678–682 — najít:

```html
  <tr><td>ARRI (ProRes a jiné video)</td><td>model kamery</td><td>ALEXA 35 → LogC4, ostatní ALEXA a AMIRA → LogC3</td></tr>
  <tr><td>ARRIRAW</td><td>metadata klipu</td><td>LogC3 nebo LogC4</td></tr>
  <tr><td>Blackmagic RAW</td><td>gama klipu</td><td>Blackmagic Design Film Gen 5 → vestavěný LUT; Film starší generace, Video, Extended Video a další gamy = bez LUTu; když klip gamu neuvádí, předpokládá se Film Gen 5</td></tr>
  <tr><td>RED R3D</td><td>formát</td><td>Log3G10</td></tr>
</table>
```

nahradit:

```html
  <tr><td>ARRI (ProRes a jiné video)</td><td>model kamery</td><td>ALEXA 35 → LogC4, ostatní ALEXA a AMIRA → LogC3</td></tr>
  <tr><td>ARRIRAW</td><td>metadata klipu</td><td>LogC3 nebo LogC4</td></tr>
  <tr><td>Blackmagic RAW</td><td>gama klipu</td><td>Blackmagic Design Film Gen 5 → vestavěný LUT; Film starší generace = bez LUTu (s volbou převodu Gen 4 → Gen 5, viz níže, vestavěný LUT Gen 5); Video, Extended Video a další gamy = bez LUTu; když klip gamu neuvádí, předpokládá se Film Gen 5</td></tr>
  <tr><td>RED R3D</td><td>formát</td><td>Log3G10</td></tr>
</table>
```

`windows/package/navod/navod.html`, řádky 690–693 — najít:

```html
<p>U každého klipu report ukáže, jaký LUT dostal a proč — např. <span class="q">„LUT: Sony S-Log3 / S-Gamut3.Cine → Rec.709
(Sony XML: …)“</span> nebo <span class="q">„LUT: bez LUT (kamera nerozpoznána)“</span> (kapitola 7).</p>

<h3>RAW formáty</h3>
```

nahradit:

```html
<p>U každého klipu report ukáže, jaký LUT dostal a proč — např. <span class="q">„LUT: Sony S-Log3 / S-Gamut3.Cine → Rec.709
(Sony XML: …)“</span> nebo <span class="q">„LUT: bez LUT (kamera nerozpoznána)“</span> (kapitola 7).</p>

<h3>Vlastní pravidla LUTů</h3>
<p>U volby <b class="ui">Automaticky podle kamery</b> je tlačítko <b class="ui">Pravidla LUTů… (N)</b>. V okně
<b>Vlastní pravidla LUTů</b> má každý řádek <i>podle čeho</i> — <b>Kamera (výrobce/model)</b> (model obsahuje text),
<b>Sériové číslo kamery</b> (přesně), <b>Cívka (začátek)</b> (např. <code>A</code> = celá kamera A) nebo <b>Složka (vzor s *
a ?)</b> nad <code>&lt;karta&gt;/&lt;složka klipu&gt;</code>, např. <code>A00*/CLIP</code> — hodnotu a LUT (vestavěný,
<b>Bez LUT</b>, nebo <b>Vlastní…</b> <code>.cube</code>). Šipkami ↑ ↓ měníte pořadí, ✕ pravidlo smaže. Pravidla se zkoušejí
shora dolů, <b>před</b> automatickým rozpoznáním kamery; první, které sedí, rozhodne. Report u klipu napíše např.
<span class="q">„pravidlo 2: kamera obsahuje „FX6““</span>. Sériové číslo se zatím čte u Sony (soubor <code>M01.XML</code>)
a z metadat QuickTime (Blackmagic, Apple, Canon…); kde ho kamera neuvádí, pravidlo podle sériového čísla nesedí nikdy.
Chybí-li vlastní <code>.cube</code> z pravidla, offload nejde spustit (<span class="q">„LUT z pravidla 2 nenalezen:
show.cube“</span>). Pravidla patří k projektu.</p>

<h3>Blackmagic RAW Film Gen 4</h3>
<p>Klipy natočené v Blackmagic Design Film <b>Gen 4</b> dostávají proxy bez LUTu (vestavěný LUT je pro Gen 5). Volba
v sekci Dekodéry <b class="ui">Blackmagic RAW Film Gen 4 převést na Gen 5 (použije se LUT Gen 5)</b> nechá knihovnu
Blackmagic RAW klip dekódovat s barevnou vědou Gen 5 a použije LUT Gen 5 (důvod v reportu: <span class="q">„Blackmagic RAW:
Film Gen 4 → Gen 5 (převod SDK)“</span>). Volba je výchozí <b>vypnutá</b> — <i>ověří ji až vzorek .braw Gen 4</i>; když
knihovna převod odmítne, klip se dekóduje jako dřív (bez LUTu). Z příkazové řádky <code>--braw-gen5</code>.</p>

<h3>RAW formáty</h3>
```

`windows/package/navod/navod.html`, řádky 823–826 — najít:

```html
</ul>
<p>Po skončení každé úlohy tlačítko Offloadu v hlavním panelu Windows zabliká; při chybě zazní chybový zvuk.</p>

<h3>Karty už jsou zálohované</h3>
```

nahradit:

```html
</ul>
<p>Po skončení každé úlohy tlačítko Offloadu v hlavním panelu Windows zabliká; při chybě zazní chybový zvuk.</p>

<h3>Oznámení</h3>
<p>Když okno Offloadu není v popředí, ukáže Windows na konci úlohy oznámení (ikona Offloadu se objeví v oznamovací
oblasti vpravo dole a zůstane tam do zavření aplikace; kliknutím na oznámení nebo ikonu se okno vrátí):
<span class="q">„Offload: A001 hotovo“</span> (<span class="q">„Ověřeno na 2 discích. Proxy: 24 hotovo.“</span>),
<span class="q">„Offload: A001 — vyžaduje pozornost“</span> (neověřeno, selhané proxy, proxy přeskočené kvůli místu…),
<span class="q">„Offload: A001 — CHYBA“</span> a při renderu proxy už po kopii <span class="q">„Offload: A001 — kartu lze
vysunout“</span>. Zrušená úloha oznámení nemá. Zvuk a blikání tlačítka zůstávají.</p>
<p><b class="ui">Nástroje → Oznámení…</b>: <b class="ui">Oznámení Windows</b> (zapnuto) a <b class="ui">Posílat na telefon přes
ntfy</b> — do aplikace ntfy v telefonu (server <code>https://ntfy.sh</code> nebo vlastní, <b>téma</b> jen písmena bez
diakritiky, číslice, <code>-</code> a <code>_</code>). Tlačítko <b class="ui">Poslat zkušební oznámení</b> ukáže
<span class="q">„Odesláno.“</span> nebo <span class="q">„Nepodařilo se: …“</span>. Téma volte nehádatelné — kdo ho zná,
oznámení čte; posílá se jen název karty, výsledek a počty, nikdy cesty, názvy souborů ani jméno počítače. Úloha na
odeslání nikdy nečeká. Nastavení oznámení patří k projektu.</p>

<h3>Karty už jsou zálohované</h3>
```

`windows/package/navod/navod.html`, řádky 1110–1113 — najít:

```html
  <tr><td class="k"><code>--ale</code></td><td>navíc seznam klipů pro Avid (ALE)</td></tr>
  <tr><td class="k"><code>--no-day-report</code></td><td>po kopii nevytvářet denní report</td></tr>
</table>

```

nahradit:

```html
  <tr><td class="k"><code>--ale</code></td><td>navíc seznam klipů pro Avid (ALE)</td></tr>
  <tr><td class="k"><code>--no-day-report</code></td><td>po kopii nevytvářet denní report</td></tr>
  <tr><td class="k"><code>--project</code></td><td>nastavení projektu uloženého v aplikaci jako výchozí hodnoty; co napíšete na příkazovou řádku, má přednost (<code>-d</code> nahradí cíle projektu)</td></tr>
  <tr><td class="k"><code>--lut-rule</code></td><td>vlastní pravidlo LUTu <code>&lt;camera|serial|reel|folder&gt;=&lt;hodnota&gt;:&lt;id|none|cesta.cube&gt;</code>, opakovatelné, pořadí = priorita (jen s <code>--lut auto</code>)</td></tr>
  <tr><td class="k"><code>--braw-gen5</code></td><td>Blackmagic RAW Film Gen 4 dekódovat jako Gen 5 (ověří až vzorek Gen 4)</td></tr>
  <tr><td class="k"><code>--ntfy</code></td><td>výsledek na telefon přes ntfy, např. <code>https://ntfy.sh/muj-tajny-kanal</code> (i odmítnutí před kopií)</td></tr>
</table>

```


`docs/VYVOJ-WINDOWS.md`, řádek 20 — najít:

````markdown
| 7 | výstupy pro produkci a střih: denní report, seznam klipů CSV/ALE, proxy pro přelinkování (reel name, všechny zvukové stopy), MD5 navíc k xxh64 (0.3.0) | hotovo (čeká ruční test: Resolve, Premiere, Avid, Excel) |
````

nahradit:

````markdown
| 7 | výstupy pro produkci a střih: denní report, seznam klipů CSV/ALE, proxy pro přelinkování (reel name, všechny zvukové stopy), MD5 navíc k xxh64 (0.3.0) | hotovo (čeká ruční test: Resolve, Premiere, Avid, Excel) |
| 8 | nastavení, oznámení, distribuce: projekty, vlastní pravidla LUTů, BRAW Film Gen 4 → Gen 5, oznámení Windows a ntfy, kontrola aktualizací, verze z `Directory.Build.props`, `build.ps1 -Sign` (0.4.0) | hotovo (čeká ruční test: oznámení, podpis; Gen 4 čeká vzorek) |
````

Návod (PDF) se vytiskne znovu z `navod.html` (Microsoft Edge bez okna, jako report):

```powershell
powershell -ExecutionPolicy Bypass -File windows\package\navod\render.ps1
```

Expected: `windows\package\Offload-navod.pdf` je nový (začíná `%PDF`). Offload se přitom nespouští.

- [ ] **Step 3: Spustit — projdou**

Run: `dotnet build windows\Offload.sln -warnaserror` (0 varování), pak `dotnet test windows\Offload.sln`
Expected: vše zelené (1149 testů: 1141 úspěšných + 8 přeskočených). `git status` neukazuje nic pod `mac/` ani `docs/ui-navrh/` k přidání.

- [ ] **Step 4: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 23–24 (po vložení řádku tabulky v kroku 2) — najít:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 12 — dokumentace a verze 0.4.0: FORMATY (`project` v reportu, `clips[].lut.source = rule`, `clips[].info.cameraSerial` a `colorScienceGenOriginal`, `proxy.lutRules` a `brawGen4ToGen5`, `settings.json`: projekty, `notify`, `updates`, `history.json` `project`, `update-check.json`), ROZDILY, VYVOJ-MAC (co převezme Mac), README (stav, příkazy `--project`, `--lut-rule`, `--braw-gen5`, `--ntfy`, GUI, ruční test 31–39), návod kap. 2, 3, 6, 8, 11 a nové PDF; `Directory.Build.props` 0.4.0 a testy s verzí
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Docs: plan 8 (settings, notifications, distribution), version 0.4.0

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md docs/FORMATY.md docs/ROZDILY.md docs/VYVOJ-MAC.md windows/Directory.Build.props windows/README.md windows/package/navod/navod.html windows/package/Offload-navod.pdf windows/tests/Offload.Core.Tests/FormatTests.cs windows/tests/Offload.Core.Tests/ReportTests.cs windows/tests/Offload.Core.Tests/HtmlReportTests.cs
git commit -F .git/COMMIT_MSG_PLAN8
```

Po úkolu 12 následuje závěrečná revize celé větve (řídicí relace) a ruční testy 31–39 z README.
