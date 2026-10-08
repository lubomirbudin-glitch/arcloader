# Offload pro Windows — plán 7: výstupy pro produkci a střih (úkoly 6–10) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Skupina B návrhu 15 vylepšení — denní report natáčecího dne, seznam klipů CSV a Avid ALE, proxy připravené k přelinkování (reel name, všechny zvukové stopy) a MD5 navíc k xxHash64; výsledek je verze 0.3.0.

**Architecture:** `CardRecord` (plán 6) se stane úplným zdrojem pravdy o složce karty (klipy a součty všech generací, reporty z Macu); nad ním stojí nový jmenný prostor `Offload.Core.Outputs` (`ClipList`, `DayReport`, `DayReportService`, `OutputSettings`). Hashování kopie dostane `HashKind` (xxh64 vždy, MD5 navíc) s jedním `SourceHasher` na algoritmus a ověřením všemi součty v jednom čtení; proxy dostanou cívku (`ReelResolver` → `reel_name` ve stopě timecodu) a všechny zvukové proudy. Úloha (`OffloadJob`) po sobě zapíše seznamy klipů a znovu vytvoří denní report každého cíle; GUI jen váže (volby, menu), logika se testuje v Core.

**Tech Stack:** C# / .NET 10, WPF, xUnit, `System.IO.Hashing` (už v projektu), BCL (`IncrementalHash` MD5, `SHA1`, pojmenovaný `Mutex`, `SemaphoreSlim`), ffmpeg/ffprobe 8.1 jen v testech `[FfmpegFact]`, Microsoft Edge jen v testech `[EdgeFact]` (kolekce „Edge“).

**Spec:** `windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md` — skupina B (§3.1–3.5), průřezové věci (§5), plán 7 (§6.2). Rozhodnutí řídicí relace platí: **R15** burn-in zůstává výchozí zapnutý (beze změny), **R16** MD5 jen navíc k xxh64 — nastavení `hash` = `"xxh64"` | `"both"`. Plán má dvě části: `windows/docs/superpowers/plans/2026-10-07-offload-windows-7-vystupy.md` (úkoly 1–5; tam je i oddíl „Ověřeno s ffmpeg 8.1“) a tento soubor (úkoly 6–10). Úkoly 6–10 stavějí na kódu úkolů 1–5 (jejich rozhraní uvádí blok Interfaces každého úkolu).

## Global Constraints

- Žádný nový NuGet balíček — jen BCL .NET 10 (`System.Security.Cryptography` pro MD5 a SHA-1, `System.IO.Hashing` už je), P/Invoke.
- Texty UI česky, doslova podle návrhu; identifikátory v kódu anglicky. Logy nástrojů (`app.log`, stderr ffmpeg), TXT report (sdílený s Macem) a `--help` zůstávají anglicky (§2.6); chyby souborů v `offload_report.json` zůstávají anglicky (sdílený s Macem), HTML/PDF je zobrazí česky (úkol 9).
- UI vlákno nikdy nečeká na disk ani síť — denní report na vyžádání `Task.Run` + výsledek přes dispatcher (jako `PickAndVerify`); po úloze se tvoří na vlákně úlohy.
- Na kartu se nikdy nezapisuje — seznamy klipů a denní report čtou jen reporty na cílech.
- Doplnění nikdy nepřepíše existující soubor na cíli (reporty `_NNNN`, generace MHL, proxy → `_2`). Přepisovat celé (dočasné jméno + nahrazení) se smí jen odvozené soubory: `<karta>.xxh64`, `<karta>.md5`, `ascmhl_chain.xml` a výstupy plánu 7 — `_clips.csv/.ale` karty i dne a denní report (R25).
- Formáty na disku čitelné Macem: nové soubory a klíče jsou jen **přidané** (Swift `Codable` neznámé klíče ignoruje); popis do `docs/FORMATY.md` (úkol 10).
- Testovatelné bez kamer: dočasné složky, syntetické klipy z ffmpeg `testsrc2` + `sine` (MOV s timecodem a `reel_name`, MXF jako Sony s 8 mono stopami), ručně psané JSON reporty (i ve tvaru Macu).
- `mac/` se nemění; co má Mac převzít, jde do `docs/VYVOJ-MAC.md` „Naplánováno“ (úkol 10).
- GUI se v testech ani při práci nikdy nespouští (`Offload.exe` ne). Rozhodování je v `Offload.Core` a testuje se tam; WPF části ověří `dotnet build` (XAML se kompiluje) a ruční test (úkol 10).
- Testy nikdy nezapíšou do skutečného `%APPDATA%`: `TestEnvironment` (module initializer) přesměruje `HistoryStore.Default`; nastavení jen přes `SettingsStore.Load/Save(path)` v `TempDir`; denní reporty a seznamy klipů jen do `TempDir`.
- R15: burn-in beze změny (výchozí zapnuto, volby i `--no-burn-in` beze změny). R16: MD5 nikdy samotné — `HashKind` vždy obsahuje xxh64.
- Verze: plán 7 → 0.3.0 (`0.3.0-win`, R22) — nastaví úkol 10 i v testech, které řetězec verze kontrolují (`FormatTests`, `ReportTests`, `HtmlReportTests`).
- `TreatWarningsAsErrors`: každý úkol končí `dotnet build windows\Offload.sln -warnaserror` bez varování a celou zelenou sadou `dotnet test windows\Offload.sln` (dnes 890 testů + 7 přeskočených, po plánu ~970). `ProxyWatchdogTests` je časově citlivý — když pod zátěží celé sady jednou selže, spusťte ho znovu samostatně (`--filter "FullyQualifiedName~ProxyWatchdogTests"`).
- PDF v testech: `PdfReport.SkipUnlessAsked` je pro celou sadu zapnuté (úloha v testu PDF netiskne, jen zaloguje); test, který PDF kontroluje, je v kolekci `[Collection("Edge")]` a volá `using var _ = PdfReport.PrintHere();`.
- Každý commit: nahoře v sekci `## 2026-10-07 — plán 7: výstupy pro produkci a střih` v `docs/VYVOJ-WINDOWS.md` řádek úkolu (sekci založí úkol 1 nad sekcí `## 2026-10-06 — plán 6: karty a bezpečnost dat`; nejnovější řádek nahoře, bez SHA). Soubory se přidávají výslovně (`git add <cesty>`, nikdy `-A`), zpráva se zapíše do `.git/COMMIT_MSG_PLAN7` (nástrojem Write, UTF-8) a commituje `git commit -F .git/COMMIT_MSG_PLAN7`; poslední řádek zprávy přesně `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Všechny příkazy git a dotnet se spouštějí z kořene repa `D:\My\Projekt\offload`.
- Bloky „najít / nahradit“ uvádějí řádky podle stavu souboru **před** daným úkolem (výchozí stav = `windows-plan7` na `e2452f9`, pak předchozí úkoly plánu). Kód plánu byl postavený a otestovaný v kopii repa mimo strom repa (každý úkol se zkompiluje bez varování a celá sada je po něm zelená; ffmpeg 8.1, Edge). Pracovní kopie má konce řádků CRLF (`core.autocrlf=true`), bloky v plánu LF — nástroj Edit je sjednotí; kdo upravuje skriptem, porovnává bez ohledu na `\r`.

## Review Focus

Návrh v §6.2 jmenuje pět vstupů s největším rizikem; každý má v plánu test v úkolu, který vlastní ten kód:

1. **Klipy bez TC, drop-frame TC, smíšené fps na kartě / dni** → ALE má soubor na každé fps (klip bez fps patří k nejčastějšímu), klip bez TC začíná `00:00:00:00`, `End` u drop-frame počítá jako kamera (bez `;00`/`;01` v minutě kromě každé desáté). Testy: úkol 6 `ClipListTests.Ale_MixedFps_OneFilePerRate`, `DropFrame_EndAndDuration`, `Ale_HeadingTabsTracks`; `TimecodeEncoderTests.Add` (řádky drop-frame 29.97 i 59.94), `Length_NominalFrames`.
2. **Názvy a hodnoty s čárkou, uvozovkou, novým řádkem, tabulátorem a diakritikou** → CSV je dá do uvozovek a uvozovky zdvojí, nic jiného nezmění (ani `=`/`-` na začátku); ALE nahradí tabulátor a konec řádku mezerou. Testy: úkol 6 `ClipListTests.Csv_EscapingAndColumns`, `Ale_HeadingTabsTracks`; úkol 7 `DayReportTests.Html_EscapesCardNames`.
3. **Dvě regenerace denního reportu najednou + Mac (nebo Excel, prohlížeč) se stejným dnem** → druhá počká (nebo po limitu česky vzdá), nic se nezapíše napůl; čtenář se sdílením mazání nevadí, čtenář bez něj (Excel) zápis zastaví jen v logu, úloha je dál „Hotovo“. Testy: úkol 8 `DayReportServiceTests.TwoWriters_OneAtATime`, `OtherProcessHoldsTheDay_Busy`, `ReaderOfTheDayReport_SharingDecides_JobNeverFails`; úkol 6 `ClipListTests.Job_ListHeldOpen_StillDone_Logged`.
4. **Propustnost MD5 na rychlé kartě a ověření v režimu `both`** → MD5 běží na vlastním vlákně jako další konzument bloku (xxh64 ani čtení karty nečeká, dokud má pool volné bloky), ověření čte každý soubor jednou oběma součty a zfalšovaný bajt odhalí. Testy: úkol 4 `EngineTests.Both_TwoBlockPool_EveryFileVerified`, `TamperedDestination_FailsInBothModes`, `DestWriterTests.Verify_Both_Md5Mismatch_Fails`; úkol 5 `EngineTests.Both_ReportHashListMhlAndBackupVerification`, `BenchTests.Md5_HashLine_AndReadBackThroughBoth`; skutečná rychlost na CFexpress je ruční test 21 (úkol 10).
5. **Zdroje s víc zvukovými proudy (Sony MXF 4–8 mono stop) a `reel_name` v `tmcd`** → proxy má všechny proudy (ProRes PCM, H.264 AAC na každý proud) a cívku ve stopě timecodu; bez TC žádná stopa timecodu ani cívka. Testy: úkol 3 `ProxyRendererTests.Render_SonyMxf_EightMonoTracks`, `Render_ReelNameOnTimecodeTrack_EveryAudioStream`, `Render_NoTimecode_NoTimecodeTrackNoReel`.

## Rozhodnutí tam, kde návrh nestačil (platí pro oba soubory plánu)

- **`append --hash` (§2.2) × „doplnění použije algoritmy původní zálohy“ (§3.5):** platí §3.5 — `offload append` volbu `--hash` nemá; součty určí `hashAlgorithms` dřívější zálohy (`AppendPlan.HashAlgorithms` → `OffloadPlan.Hashes`). Proto `HashKind` žije i v `OffloadPlan` (návrh ho jmenuje jen v `OffloadOptions`/`JobConfig`).
- **Hodnota `hash`, které verze nerozumí** (např. `"xxh128"` z budoucí verze): zachová se v `settings.json` (R24) a čte se jako `xxh64`.
- **Kanály zvukových proudů:** AAC bitrate na proud potřebuje kanály každého proudu; `ClipInfo.AudioStreams` je `[JsonIgnore]` (zná se jen po sondě v téže úloze), takže se formát reportu nemění. Neznámé proudy (RAW mezisoubor) dostanou společné `-b:a 192k` jako dnes. Odhad místa proxy počítá 0,2 Mb/s na každý proud (§2.5 „× zvukové stopy“).
- **Klipy bez analýzy:** `CardRecord.Clips` dá u generace bez klipů v reportu klipy nalezené `ClipDetector.Detect` v jejích souborech (bez metadat) — tak je mají denní report (počet klipů) i seznamy klipů (§3.3 „Bez analýzy klipů se řádky vytvoří z ClipDetector.Detect“).
- **Proxy kamery (SUB, PROXY) v seznamech klipů** se neuvádějí (nejsou to klipy pro střih; v reportu se také nepočítají do „klipů“).
- **ALE bez fps:** klip bez známé snímkové frekvence jde do souboru nejčastějšího fps; když fps nezná žádný klip, hlavička je `FPS 25` (Avid hodnotu vyžaduje). ALE, které nový zápis už nevytvoří (změnila se fps), se smaže — jen soubory přesně `<název>_clips[_<fps>].ale`.
- **Sloupec `Duration` (§3.3 „délka jako TC v nominálních snímcích, bez fps `H:MM:SS`“):** se známým fps `HH:MM:SS:FF` v nominálních snímcích (vždy `:`), bez fps délka `H:MM:SS`, bez délky prázdné. Sloupec `MD5` je v hlavičce vždy (stabilní pro import), bez MD5 prázdný.
- **Drop-frame:** `Timecode.Add` počítá drop-frame správně (dřív přibližně) — popisky náhledů tím jen zpřesní.
- **Karty dne (§3.2):** všechny podsložky `<datum>\` kromě `_REPORT` a skrytých (`.…`); bez `_REPORT\offload_report.json` řádek „bez reportu (probíhá, nebo byl přerušen)“. Denní report se nikdy nezapíše do složky, ve které není ani jedna karta s reportem („Ve složce … není žádná karta s reportem — vyberte složku dne na cíli (<cíl>\<datum>).“) — výběr kořene cíle v menu tak nevytvoří `_REPORT` v kořeni. Výběr složky karty znamená její den.
- **Dlaždice „ověřené karty N / M“:** karta je ověřená, když jsou ověřené všechny generace a nemá selhaný soubor; M = všechny karty dne (i bez reportu). Projekt v hlavičce dne přijde až s projekty (plán 8).
- **Čekání na zámek dne:** nejvýš 2 min (PDF trvá sekundy na cíl); pak `TimeoutException` s českou hláškou — úloha ji jen zaloguje.
- **Návod (§6.2 úkol 9 jmenuje kap. 6, 7, 10, 11):** mění se i kap. 1, 3 a 5 (menu Nástroje, volby Kontrolní součet / seznamy klipů / denní report jsou v sekci Struktura a kontrola).

---

### Task 6: Seznam klipů — CSV a Avid ALE

Spec §3.3, R13, R25, §6.2 úkol 6. Nový jmenný prostor `Offload.Core.Outputs`: `OutputSettings` (sekce `outputs` v `settings.json`; tady `clipListCsv` = zapnuto, `clipListAle` = vypnuto, `dayReport` přidá úkol 8) a `ClipList` (řádky z `CardRecord` — všechny generace, proxy kamery vynechané —, zápis CSV a ALE). `Timecode` dostane přesný drop-frame a délku v nominálních snímcích. Úloha píše `_REPORT\<karta>_clips.csv/.ale` na každém cíli při každém přepisu reportu ve stage 2 (`RewriteReports`) a bez stage 2 hned po stage 1; seznam je odvozený (R25), takže ho doplnění přepíše celý — test doplnění `AppendEngineTests.Rewritten` ho proto přidá k souborům, které se smějí přepsat. Selhání zápisu (seznam otevřený v Excelu) jde jen do logu.

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md` (řádky 20–25)
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs` (řádky 188–193, 210–215)
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs` (řádky 424–429)
- Modify: `windows/src/Offload.App/SetupPanel.xaml` (řádky 197–202)
- Modify: `windows/src/Offload.Cli/StageTwoOptions.cs` (řádky 1–6, 18–27, 35–40)
- Modify: `windows/src/Offload.Core/Desktop/AppSettings.cs` (řádky 1–5, 26–31, 56–62, 114–119, 141–146)
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (řádky 2–7, 29–34, 218–223, 645–656)
- Create: `windows/src/Offload.Core/Outputs/ClipList.cs`
- Modify: `windows/src/Offload.Core/Timecode.cs` (řádky 25–41)
- Modify: `windows/tests/Offload.Core.Tests/AppendEngineTests.cs` (řádky 25–32)
- Modify: `windows/tests/Offload.Core.Tests/CliOutputsTests.cs` (řádky 2–8, 56–61)
- Create: `windows/tests/Offload.Core.Tests/ClipListTests.cs`
- Modify: `windows/tests/Offload.Core.Tests/SettingsStoreTests.cs` (řádky 43–48)
- Modify: `windows/tests/Offload.Core.Tests/TimecodeEncoderTests.cs` (řádky 28–35)

**Interfaces:**
- Consumes: `CardRecord.Load`, `CardRecord.Clips`, `FolderName`, `Files` (úkol 1), `ReelResolver.Resolve` (úkol 2), `ReportFile.Md5` (úkol 5), `NativeIO.WriteFileReplacing`, `SettingsStore` (sekce `Kept`), `CliOutputsTests.Run/Card` (úkol 5), `AppendFixtures`.
- Produces: `namespace Offload.Core.Outputs`; `sealed class OutputSettings { bool ClipListCsv = true; bool ClipListAle; Dictionary<string, JsonElement>? Extra }`; `static partial class ClipList` s `sealed record Row(string Card, Clip Clip, ClipInfo? Info, LutUse? Lut, string? Xxh64, string? Md5)` (vlastnosti `Reel`, `Fps`, `FpsText`, `StartTc`, `Frames`, `FileName`, `Codec`, `Resolution`, `LutName`), `Rows(CardRecord) → List<Row>`, `CsvColumns`, `AleColumns`, `Csv(IEnumerable<Row>) → string`, `Ale(IReadOnlyList<Row>) → IReadOnlyList<(string? Suffix, string Text)>`, `Write(string dir, string name, IReadOnlyList<Row> rows, OutputSettings outputs)`, `WriteForCard(string cardPath, OutputSettings outputs)`; `Timecode.Nominal(double) → int`, `Timecode.IsDropFrame(string, double) → bool`, `Timecode.Length(long frames, double fps) → string` (a `Timecode.Add` přesně i pro drop-frame); `AppSettings.Outputs`; `JobConfig.Outputs`; CLI `--no-clip-list`, `--ale` (společné pro `copy` a `append`); GUI `MainViewModel.ClipListCsv`, `ClipListAle`.

- [ ] **Step 1: Padající testy**

Nový soubor `windows/tests/Offload.Core.Tests/ClipListTests.cs`:

```csharp
using System.Text;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Spec 3.3: the clip list of a card — CSV (RFC 4180, UTF-8 with BOM, CRLF) and Avid ALE (tabs, one FPS per file).
public class ClipListTests
{
    const string Xx = "0123456789abcdef";

    static ClipList.Row Row(string name, ClipInfo? info = null, LutUse? lut = null, string? md5 = null, ClipFormat format = ClipFormat.Video) =>
        new("A001", new Clip { Name = name, Path = $"CLIP/{name}.mov", Format = format, Segments = [$"CLIP/{name}.mov"], Bytes = 1234 }, info, lut, Xx, md5);

    static readonly LutUse LogC4 = new("auto", "arri-logc4-awg4", "ARRI LogC4 → Rec.709", "ARRI");

    // Review focus 2: comma, quote, line break and Czech in values — quoted and doubled, nothing else changed.
    [Fact]
    public void Csv_EscapingAndColumns()
    {
        var a = Row("Záběr, 1", new ClipInfo
        {
            Camera = "ARRI \"ALEXA\" 35", Timecode = "01:00:00:00", FrameRate = "25/1", Duration = 12.4, Frames = 310,
            Width = 3840, Height = 2160, Codec = "prores 4444", AudioChannels = 2,
        }, LogC4, md5: "900150983cd24fb0d6963f7d28e17f72");
        var b = Row("B002C001_x", new ClipInfo { Timecode = "10:00:00:00", FrameRate = "24000/1001", Duration = 2.0 },
            new LutUse("custom", null, "look\nB", "vlastní"));
        var c = Row("-take", format: ClipFormat.Braw);

        var csv = ClipList.Csv([a, b, c]);

        Assert.Equal(
            "Clip Name,File Name,Path,Card,Camera,Reel,Start TC,Duration,FPS,Resolution,Codec,LUT,Size (bytes),XXH64,MD5\r\n"
            + "\"Záběr, 1\",\"Záběr, 1.mov\",\"CLIP/Záběr, 1.mov\",A001,\"ARRI \"\"ALEXA\"\" 35\",A001,01:00:00:00,00:00:12:10,25,3840x2160,prores 4444,ARRI LogC4 → Rec.709,1234,0123456789abcdef,900150983cd24fb0d6963f7d28e17f72\r\n"
            + "B002C001_x,B002C001_x.mov,CLIP/B002C001_x.mov,A001,,B002,10:00:00:00,00:00:02:00,23.976,,,\"look\nB\",1234,0123456789abcdef,\r\n"
            + "-take,-take.mov,CLIP/-take.mov,A001,,A001,,,,,Blackmagic RAW,,1234,0123456789abcdef,\r\n",
            csv);
    }

    // Review focus 1+2: a tab inside a value becomes a space; no TC = 00:00:00:00; tracks from the audio channels.
    [Fact]
    public void Ale_HeadingTabsTracks()
    {
        var a = Row("A001C003", new ClipInfo
        {
            Camera = "ARRI ALEXA 35", Timecode = "01:00:00:00", FrameRate = "25/1", Frames = 310, Width = 1920, Height = 1080,
            Codec = "prores 4444", AudioChannels = 2,
        }, LogC4);
        var d = Row("C0001", new ClipInfo { Camera = "Sony\tFX6", FrameRate = "25/1", Frames = 50, Width = 1920, Height = 1080, Codec = "h264 High" });

        var (suffix, text) = Assert.Single(ClipList.Ale([a, d]));

        Assert.Null(suffix);
        Assert.Equal(
            "Heading\r\nFIELD_DELIM\tTABS\r\nVIDEO_FORMAT\t1080\r\nAUDIO_FORMAT\t48khz\r\nFPS\t25\r\n\r\n"
            + "Column\r\nName\tTape\tStart\tEnd\tTracks\tSource File\tCamera\tLUT\tResolution\tCodec\tXXH64\tMD5\r\nData\r\n"
            + "A001C003\tA001\t01:00:00:00\t01:00:12:10\tVA1A2\tA001C003.mov\tARRI ALEXA 35\tARRI LogC4 → Rec.709\t1920x1080\tprores 4444\t0123456789abcdef\t\r\n"
            + "C0001\tA001\t00:00:00:00\t00:00:02:00\tV\tC0001.mov\tSony FX6\t\t1920x1080\th264 High\t0123456789abcdef\t\r\n",
            text);
    }

    // Review focus 1: mixed rates — one ALE per rate; a clip without a rate goes with the most common one.
    [Fact]
    public void Ale_MixedFps_OneFilePerRate()
    {
        var rows = new[]
        {
            Row("A", new ClipInfo { FrameRate = "24000/1001", Frames = 24 }),
            Row("B", new ClipInfo { FrameRate = "25/1", Frames = 25, Height = 2160 }),
            Row("C", new ClipInfo { FrameRate = "25/1", Frames = 25, Height = 2160 }),
            Row("D"),
        };

        var files = ClipList.Ale(rows);

        Assert.Equal(["23.976", "25"], files.Select(f => f.Suffix));
        Assert.Contains("FPS\t23.976\r\n", files[0].Text);
        Assert.Contains("\r\nA\t", files[0].Text);
        Assert.Contains("FPS\t25\r\n", files[1].Text);
        Assert.Contains("VIDEO_FORMAT\tCUSTOM\r\n", files[1].Text);
        Assert.Contains("\r\nB\t", files[1].Text);
        Assert.Contains("\r\nD\tA001\t00:00:00:00\t00:00:00:00\tV\t", files[1].Text);
        // Nothing analysed at all: one ALE, FPS 25 (Avid needs a rate).
        var (suffix, text) = Assert.Single(ClipList.Ale([Row("D")]));
        Assert.Null(suffix);
        Assert.Contains("FPS\t25\r\n", text);
    }

    // Review focus 1: drop-frame — End counts as the camera does; the CSV duration is nominal frames.
    [Fact]
    public void DropFrame_EndAndDuration()
    {
        var df = Row("A", new ClipInfo { FrameRate = "30000/1001", Timecode = "00:00:59;29", Frames = 2 });
        Assert.Contains("\tA001\t00:00:59;29\t00:01:00;03\t", Assert.Single(ClipList.Ale([df])).Text);
        Assert.Contains(",00:00:59;29,00:00:00:02,29.97,", ClipList.Csv([df]));
    }

    // R25: the files are rewritten whole; an ALE the rates no longer call for is removed; CSV has the BOM, ALE none.
    [Fact]
    public void Write_BomCrlf_StaleAleRemoved()
    {
        using var dir = new TempDir();
        var both = new OutputSettings { ClipListCsv = true, ClipListAle = true };
        File.WriteAllText(Path.Combine(dir.Path, "A001_clips_50.ale"), "old");
        File.WriteAllText(Path.Combine(dir.Path, "other_clips.ale"), "someone else's");

        ClipList.Write(dir.Path, "A001", [Row("A", new ClipInfo { FrameRate = "25/1", Frames = 25 })], both);

        var csv = File.ReadAllBytes(Path.Combine(dir.Path, "A001_clips.csv"));
        Assert.Equal(Encoding.UTF8.GetPreamble(), csv[..3]);
        Assert.EndsWith("\r\n", Encoding.UTF8.GetString(csv));
        var ale = File.ReadAllBytes(Path.Combine(dir.Path, "A001_clips.ale"));
        Assert.Equal((byte)'H', ale[0]);
        Assert.False(File.Exists(Path.Combine(dir.Path, "A001_clips_50.ale")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "other_clips.ale")));

        ClipList.Write(dir.Path, "B002", [Row("A")], new OutputSettings());   // defaults: CSV only (R13)
        Assert.True(File.Exists(Path.Combine(dir.Path, "B002_clips.csv")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "B002_clips.ale")));
    }

    static OffloadJob Job(string card, IReadOnlyList<string> dests, string history, OutputSettings outputs, IReadOnlyList<string>? appendInto = null) =>
        new(new JobConfig(card, dests) { Date = "2026-10-07", AnalyzeClips = false, HistoryFolder = history, Outputs = outputs, AppendInto = appendInto },
            AppendFixtures.Options());

    // Spec 3.3: without clip analysis the rows come from the files (metadata empty); an append lists every generation.
    [Fact]
    public void Job_ListsFromTheFiles_AppendCoversEveryGeneration()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var history = dir.Sub("appdata");
        var outputs = new OutputSettings { ClipListCsv = true, ClipListAle = true };
        var full = Job(card, [dir.Sub("d1"), dir.Sub("d2")], history, outputs);
        var report = full.Run()!;
        var paths = full.Plan!.CardPaths;

        foreach (var p in paths)
        {
            var csv = File.ReadAllText(Path.Combine(p, "_REPORT", "A001_clips.csv"));
            Assert.Contains($"A001C001,A001C001.mov,CLIP/A001C001.mov,A001,,A001,,,,,,,10000,{report.Files[0].Xxh64},\r\n", csv);
            Assert.True(File.Exists(Path.Combine(p, "_REPORT", "A001_clips.ale")));
        }

        AppendFixtures.NewClip(card);
        var append = Job(card, [], history, outputs, appendInto: paths);
        append.Run();

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        foreach (var p in paths)
        {
            var lines = File.ReadAllLines(Path.Combine(p, "_REPORT", "A001_clips.csv"));
            Assert.Equal(["CLIP/A001C001.mov", "CLIP/A001C003.mov"], lines.Skip(1).Select(l => l.Split(',')[2]));
        }
    }

    // A list held open (Excel locks a CSV it shows) is not rewritten — the job is still Done, the log says why.
    [Fact]
    public void Job_ListHeldOpen_StillDone_Logged()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history, new OutputSettings());
        full.Run();
        var csvPath = Path.Combine(full.Plan!.CardPaths[0], "_REPORT", "A001_clips.csv");
        var before = File.ReadAllBytes(csvPath);
        AppendFixtures.NewClip(card);
        var log = new List<string>();

        using (new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var append = Job(card, [], history, new OutputSettings(), appendInto: full.Plan.CardPaths);
            append.Run(m => { lock (log) log.Add(m); });
            Assert.Equal(JobPhase.Done, append.Status().Phase);
            Assert.True(append.Status().SafeToEject);
        }

        Assert.Contains(log, m => m.StartsWith("clip list ", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(csvPath));
    }
}
```

`windows/tests/Offload.Core.Tests/TimecodeEncoderTests.cs`, řádky 28–35 — najít:

```csharp
    [InlineData("01:00:00:00", 99, 25, "01:00:03:24")]
    [InlineData("23:59:59:24", 1, 25, "00:00:00:00")]
    [InlineData("10:00:00;00", 30, 29.97, "10:00:01;00")]
    public void Add(string tc, long frames, double fps, string expected) => Assert.Equal(expected, Timecode.Add(tc, frames, fps));

    [Fact]
    public void VideoArgs_PerEncoder()
    {
```

nahradit:

```csharp
    [InlineData("01:00:00:00", 99, 25, "01:00:03:24")]
    [InlineData("23:59:59:24", 1, 25, "00:00:00:00")]
    [InlineData("10:00:00;00", 30, 29.97, "10:00:01;00")]
    // Review focus 1: drop-frame counts as the camera does — no ;00/;01 (;00–;03 at 59.94) at a minute but every tenth.
    [InlineData("00:00:59;29", 1, 29.97, "00:01:00;02")]
    [InlineData("00:09:59;29", 1, 29.97, "00:10:00;00")]
    [InlineData("01:00:00;00", 17982, 29.97, "01:10:00;00")]
    [InlineData("23:59:59;29", 1, 29.97, "00:00:00;00")]
    [InlineData("00:00:59;59", 1, 59.94, "00:01:00;04")]
    [InlineData("00:00:59;29", 1, 30, "00:01:00;00")]   // ";" at exactly 30 fps: no frames dropped
    public void Add(string tc, long frames, double fps, string expected) => Assert.Equal(expected, Timecode.Add(tc, frames, fps));

    [Theory]
    [InlineData(310, 25, "00:00:12:10")]
    [InlineData(48, 23.976, "00:00:02:00")]
    [InlineData(2, 29.97, "00:00:00:02")]
    public void Length_NominalFrames(long frames, double fps, string expected) => Assert.Equal(expected, Timecode.Length(frames, fps));

    [Fact]
    public void VideoArgs_PerEncoder()
    {
```

`windows/tests/Offload.Core.Tests/SettingsStoreTests.cs`, řádky 43–48 — najít:

```csharp
        Assert.Contains("\"hash\": \"xxh128\"", File.ReadAllText(path));
    }

    [Fact]
    public void Corrupt_Defaults()
    {
```

nahradit:

```csharp
        Assert.Contains("\"hash\": \"xxh128\"", File.ReadAllText(path));
    }

    // Spec 5.1: "outputs" — CSV on, ALE off by default; keys of a newer version inside it survive (R24).
    [Fact]
    public void Outputs_DefaultsAndUnknownKeys()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var fresh = SettingsStore.Load(path);
        Assert.Equal((true, false), (fresh.Outputs.ClipListCsv, fresh.Outputs.ClipListAle));
        var s = LoadJson(dir, """{"outputs":{"clipListCsv":false,"clipListAle":true,"future":1}}""");
        Assert.Equal((false, true), (s.Outputs.ClipListCsv, s.Outputs.ClipListAle));
        Assert.Null(s.Extra);
        SettingsStore.Save(s, path);
        var text = File.ReadAllText(path);
        Assert.Contains("\"clipListAle\": true", text);
        Assert.Contains("\"future\": 1", text);
    }

    [Fact]
    public void Corrupt_Defaults()
    {
```

`windows/tests/Offload.Core.Tests/CliOutputsTests.cs`, řádky 2–8 — najít:

```csharp

namespace Offload.Core.Tests;

/// Plan 7: the CLI options of the outputs — `copy --hash`, `bench --hash`.
[Collection("Console")]
public class CliOutputsTests
{
```

nahradit:

```csharp

namespace Offload.Core.Tests;

/// Plan 7: the CLI options of the outputs — `copy --hash`, `bench --hash`, `--no-clip-list`, `--ale`.
[Collection("Console")]
public class CliOutputsTests
{
```

`windows/tests/Offload.Core.Tests/CliOutputsTests.cs`, řádky 56–61 — najít:

```csharp
        Assert.Contains("Chyba: Neznámý kontrolní součet „md5“ — použijte xxh64 nebo both (xxh64 + MD5).", bad.Err);
    }

    // Spec 3.5: `bench --hash md5` adds the MD5 line; an unknown value is refused in Czech.
    [Fact]
    public void BenchHash_Md5Line_UnknownRefused()
```

nahradit:

```csharp
        Assert.Contains("Chyba: Neznámý kontrolní součet „md5“ — použijte xxh64 nebo both (xxh64 + MD5).", bad.Err);
    }

    // Spec 3.3: CSV by default, `--no-clip-list` leaves it out, `--ale` adds the Avid list.
    [Fact]
    public void ClipListOptions()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var plain = dir.Sub("plain");
        var noCsvAle = dir.Sub("ale");

        Assert.Equal(0, Run(CopyCommand.Create(), card, "-d", plain, "--date", "2026-10-07", "--no-history", "--no-analyze").Exit);
        Assert.Equal(0, Run(CopyCommand.Create(), card, "-d", noCsvAle, "--date", "2026-10-07", "--no-history", "--no-analyze", "--no-clip-list", "--ale").Exit);

        var a = Path.Combine(plain, "2026-10-07", "A001", "_REPORT");
        Assert.True(File.Exists(Path.Combine(a, "A001_clips.csv")));
        Assert.False(File.Exists(Path.Combine(a, "A001_clips.ale")));
        var b = Path.Combine(noCsvAle, "2026-10-07", "A001", "_REPORT");
        Assert.False(File.Exists(Path.Combine(b, "A001_clips.csv")));
        Assert.True(File.Exists(Path.Combine(b, "A001_clips.ale")));
    }

    // Spec 3.5: `bench --hash md5` adds the MD5 line; an unknown value is refused in Czech.
    [Fact]
    public void BenchHash_Md5Line_UnknownRefused()
```

`windows/tests/Offload.Core.Tests/AppendEngineTests.cs`, řádky 25–32 — najít:

```csharp
    static Dictionary<string, byte[]> Snapshot(string dir) =>
        Directory.GetFiles(dir).ToDictionary(f => Path.GetFileName(f), f => File.ReadAllBytes(f));

    /// The only earlier files an append may rewrite (spec 2.2): the hash list (lines added) and the chain (entry added).
    static readonly string[] Rewritten = [@"_REPORT\A001.xxh64", @"ascmhl\ascmhl_chain.xml"];

    [Fact]
    public void Generation2_OnBothDestinations_EarlierFilesUntouched()
```

nahradit:

```csharp
    static Dictionary<string, byte[]> Snapshot(string dir) =>
        Directory.GetFiles(dir).ToDictionary(f => Path.GetFileName(f), f => File.ReadAllBytes(f));

    /// The only earlier files an append may rewrite (spec 2.2): the hash list (lines added) and the chain (entry added) —
    /// and the clip list, which is derived from every generation and written whole (spec 3.3, R25).
    static readonly string[] Rewritten = [@"_REPORT\A001.xxh64", @"ascmhl\ascmhl_chain.xml", @"_REPORT\A001_clips.csv"];

    [Fact]
    public void Generation2_OnBothDestinations_EarlierFilesUntouched()
```

- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ClipListTests|FullyQualifiedName~TimecodeEncoderTests|FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~CliOutputsTests|FullyQualifiedName~AppendEngineTests"`
Expected: build FAIL — `CS0234: Typ nebo název oboru názvů 'Outputs' v oboru názvů 'Offload.Core' neexistuje`, `CS0117: 'Timecode' neobsahuje definici pro 'Length'`.

- [ ] **Step 3: Implementace — jádro**

Nový soubor `windows/src/Offload.Core/Outputs/ClipList.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Offload.Core.Outputs;

/// "outputs" in settings.json (spec 5.1) and in a job: the files made for production and editing.
public sealed class OutputSettings
{
    /// `_REPORT\<card>_clips.csv` (and the day's) — on by default (R13).
    [JsonPropertyName("clipListCsv")] public bool ClipListCsv { get; set; } = true;
    /// `_REPORT\<card>_clips.ale` for Avid — off by default (R13).
    [JsonPropertyName("clipListAle")] public bool ClipListAle { get; set; }

    /// Keys of this section a newer version wrote: kept and written back (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// The clip list of a card or a day (spec 3.3): CSV for Resolve and Excel, ALE for Avid. Derived from the reports, so
/// written whole every time (tmp + rename, R25). Camera proxies (Sony SUB, PROXY folders) are not listed — they are not
/// the clips editing works with.
public static partial class ClipList
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// One clip: the card folder it is in, what the report knows, and the hashes of its main file (the first segment).
    public sealed record Row(string Card, Clip Clip, ClipInfo? Info, LutUse? Lut, string? Xxh64, string? Md5)
    {
        public string Reel => ReelResolver.Resolve(Clip, Info, Card);
        public double? Fps => Timecode.Fps(Info?.FrameRate);
        public string FpsText => Fps is double f ? f.ToString("0.###", Inv) : "";
        public string? StartTc => Timecode.IsValid(Info?.Timecode) ? Info!.Timecode : null;
        /// Frames of the clip: the stream's count, else duration × rate; null when either is unknown.
        public long? Frames => Info?.Frames is int n && n > 0 ? n : Info?.Duration is double d && Fps is double f ? (long)Math.Round(d * f) : null;
        public string FileName => Path.GetFileName((Clip.Segments.FirstOrDefault() ?? Clip.Path).Replace('/', '\\'));
        public string Codec => Clip.Format != ClipFormat.Video ? Clip.Format.Label() : Info?.Codec ?? "";
        public string Resolution => Info is { Width: int w, Height: int h } ? $"{w}x{h}" : "";
        public string LutName => Lut is { Source: not "none" } l ? l.Name : "";
    }

    /// The rows of a card folder: every generation's clips (CardRecord.Clips), camera proxies left out.
    public static List<Row> Rows(CardRecord record) =>
        record.Clips.Where(c => !c.Entry.Clip.IsCameraProxy).Select(c =>
        {
            var main = c.Entry.Clip.Segments.FirstOrDefault() ?? c.Entry.Clip.Path;
            var file = record.Files.GetValueOrDefault(main)?.File;
            return new Row(record.FolderName, c.Entry.Clip, c.Entry.Info, c.Entry.Lut, file?.Xxh64, file?.Md5);
        }).ToList();

    public static readonly IReadOnlyList<string> CsvColumns =
    [
        "Clip Name", "File Name", "Path", "Card", "Camera", "Reel", "Start TC", "Duration", "FPS", "Resolution", "Codec", "LUT",
        "Size (bytes)", "XXH64", "MD5",
    ];

    /// Duration as "HH:MM:SS:FF" in nominal frames; without a frame rate "H:MM:SS"; unknown: empty.
    static string DurationText(Row r) =>
        r.Frames is long n && r.Fps is double f ? Timecode.Length(n, f) : r.Info?.DurationLabel ?? "";

    /// RFC 4180, comma, CRLF, header always; UTF-8 with BOM (WriteForCard adds it). Values starting with = + - @ are left
    /// as they are — changing them would break the import into Resolve.
    public static string Csv(IEnumerable<Row> rows)
    {
        var s = new StringBuilder();
        void Line(IEnumerable<string> fields) => s.Append(string.Join(",", fields.Select(CsvField))).Append("\r\n");
        Line(CsvColumns);
        foreach (var r in rows)
            Line([r.Clip.Name, r.FileName, r.Clip.Path, r.Card, r.Info?.Camera ?? "", r.Reel, r.StartTc ?? "", DurationText(r), r.FpsText,
                  r.Resolution, r.Codec, r.LutName, r.Clip.Bytes.ToString(Inv), r.Xxh64 ?? "", r.Md5 ?? ""]);
        return s.ToString();
    }

    static string CsvField(string v) =>
        v.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

    public static readonly IReadOnlyList<string> AleColumns =
        ["Name", "Tape", "Start", "End", "Tracks", "Source File", "Camera", "LUT", "Resolution", "Codec", "XXH64", "MD5"];

    /// One ALE per frame rate (an ALE has one FPS in its heading): `Suffix` null when the clips share one rate, else the
    /// rate ("25", "23.976") for `<name>_clips_<rate>.ale`. A clip whose rate is unknown goes with the most common rate;
    /// when no clip has one, the heading says 25.
    public static IReadOnlyList<(string? Suffix, string Text)> Ale(IReadOnlyList<Row> rows)
    {
        // Rates in the order they first appear; the most common one (ties: the first) also takes the clips without one.
        var rates = rows.Where(r => r.Fps != null).Select(r => r.FpsText).Distinct().ToList();
        if (rates.Count == 0) return [(null, AleText("25", 25, rows))];
        var main = rates.OrderByDescending(f => rows.Count(r => r.FpsText == f)).First();
        return rates.Select(f => (
            rates.Count == 1 ? null : f,
            AleText(f, rows.First(r => r.FpsText == f).Fps!.Value, rows.Where(r => r.FpsText == f || (f == main && r.Fps == null)).ToList())))
            .ToList();
    }

    /// Tabs and line breaks inside a value would break the columns: they become spaces.
    static string AleField(string v) => v.Replace("\r\n", " ").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    static string AleText(string fpsText, double fps, IReadOnlyList<Row> rows)
    {
        var heights = rows.Where(r => r.Info?.Height != null).GroupBy(r => r.Info!.Height!.Value).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
        var format = heights.FirstOrDefault() switch { 1080 => "1080", 720 => "720", _ => "CUSTOM" };
        var s = new StringBuilder();
        void Line(params string[] fields) => s.Append(string.Join("\t", fields.Select(AleField))).Append("\r\n");
        Line("Heading");
        Line("FIELD_DELIM", "TABS");
        Line("VIDEO_FORMAT", format);
        Line("AUDIO_FORMAT", "48khz");
        Line("FPS", fpsText);
        s.Append("\r\n");
        Line("Column");
        Line([.. AleColumns]);
        Line("Data");
        foreach (var r in rows)
        {
            var start = r.StartTc ?? "00:00:00:00";
            var rate = r.Fps ?? fps;
            var end = r.Frames is long n ? Timecode.Add(start, n, rate) : start;
            int channels = Math.Min(r.Info?.AudioChannels ?? 0, 8);
            var tracks = "V" + string.Concat(Enumerable.Range(1, channels).Select(i => $"A{i}"));
            Line(r.Clip.Name, r.Reel, start, end, tracks, r.FileName, r.Info?.Camera ?? "", r.LutName, r.Resolution, r.Codec, r.Xxh64 ?? "", r.Md5 ?? "");
        }
        return s.ToString();
    }

    /// Writes `<name>_clips.csv` and `<name>_clips[_<fps>].ale` into `dir` as `outputs` asks; an ALE of an earlier
    /// write that this one no longer makes (the rates changed) is removed. Throws on an I/O error (the caller logs it).
    public static void Write(string dir, string name, IReadOnlyList<Row> rows, OutputSettings outputs)
    {
        if (!outputs.ClipListCsv && !outputs.ClipListAle) return;
        NativeIO.Mkdirs(dir);
        if (outputs.ClipListCsv)
            NativeIO.WriteFileReplacing(Path.Combine(dir, $"{name}_clips.csv"), [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Csv(rows))]);
        if (!outputs.ClipListAle) return;
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (suffix, text) in Ale(rows))
        {
            var file = suffix == null ? $"{name}_clips.ale" : $"{name}_clips_{suffix}.ale";
            NativeIO.WriteFileReplacing(Path.Combine(dir, file), new UTF8Encoding(false).GetBytes(text));
            written.Add(file);
        }
        var ours = new Regex("^" + Regex.Escape(name) + @"_clips(_[0-9.]+)?\.ale$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var stale in Directory.EnumerateFiles(dir, "*.ale").Select(Path.GetFileName).Where(f => ours.IsMatch(f!) && !written.Contains(f!)))
            NativeIO.Delete(Path.Combine(dir, stale!));
    }

    /// The clip lists of one card folder (all generations) into its `_REPORT`.
    public static void WriteForCard(string cardPath, OutputSettings outputs)
    {
        var record = CardRecord.Load(cardPath);
        if (record.Generations.Count == 0) return;
        Write(Path.Combine(cardPath, Layout.ReportDirName), record.FolderName, Rows(record), outputs);
    }
}
```

`windows/src/Offload.Core/Timecode.cs`, řádky 25–41 — najít:

```csharp
        return fps > 0 ? fps : null;
    }

    /// Start timecode plus a number of frames, counted at the nominal (rounded) rate and wrapping at 24 h.
    /// Drop-frame timecode is labelled approximately — good enough for a thumbnail caption.
    public static string Add(string tc, long frames, double fps)
    {
        int nominal = Math.Max(1, (int)Math.Round(fps));
        var sep = tc[8];
        long h = long.Parse(tc[..2], CultureInfo.InvariantCulture), m = long.Parse(tc[3..5], CultureInfo.InvariantCulture);
        long s = long.Parse(tc[6..8], CultureInfo.InvariantCulture), f = long.Parse(tc[9..11], CultureInfo.InvariantCulture);
        long perDay = 24L * 3600 * nominal;
        long total = (((h * 3600 + m * 60 + s) * nominal + f + frames) % perDay + perDay) % perDay;
        long ff = total % nominal, secs = total / nominal;
        return string.Create(CultureInfo.InvariantCulture, $"{secs / 3600:00}:{secs / 60 % 60:00}:{secs % 60:00}{sep}{ff:00}");
    }
}
```

nahradit:

```csharp
        return fps > 0 ? fps : null;
    }

    /// The nominal (rounded) rate a timecode counts at: 23.976 → 24, 29.97 → 30, 59.94 → 60.
    public static int Nominal(double fps) => Math.Max(1, (int)Math.Round(fps));

    /// Drop-frame counting: a ";" timecode at 29.97 or 59.94 (spec 3.3, review focus 1).
    public static bool IsDropFrame(string tc, double fps) =>
        IsValid(tc) && tc[8] == ';' && Nominal(fps) is 30 or 60 && Math.Abs(fps - Nominal(fps)) > 0.001;

    /// Start timecode plus a number of frames, wrapping at 24 h, with the separator of `tc`. A drop-frame timecode counts
    /// as the camera does: frame numbers 00 and 01 (00–03 at 59.94) are left out at every minute but every tenth.
    public static string Add(string tc, long frames, double fps)
    {
        bool df = IsDropFrame(tc, fps);
        long perDay = df ? 144L * FramesPerTenMinutes(fps) : 24L * 3600 * Nominal(fps);
        long total = ((ToFrames(tc, fps) + frames) % perDay + perDay) % perDay;
        return FromFrames(total, fps, df, tc[8]);
    }

    /// A length in frames as "HH:MM:SS:FF" at the nominal rate (the Duration of a clip list).
    public static string Length(long frames, double fps) => FromFrames(Math.Max(0, frames), fps, dropFrame: false, ':');

    static long FramesPerTenMinutes(double fps) => Nominal(fps) * 600L - Nominal(fps) / 15 * 9;

    /// The frame number of `tc` counted from 00:00:00:00.
    static long ToFrames(string tc, double fps)
    {
        int n = Nominal(fps);
        long h = long.Parse(tc[..2], CultureInfo.InvariantCulture), m = long.Parse(tc[3..5], CultureInfo.InvariantCulture);
        long s = long.Parse(tc[6..8], CultureInfo.InvariantCulture), f = long.Parse(tc[9..11], CultureInfo.InvariantCulture);
        long frames = ((h * 60 + m) * 60 + s) * n + f;
        if (IsDropFrame(tc, fps))
        {
            long minutes = h * 60 + m;
            frames -= n / 15 * (minutes - minutes / 10);
        }
        return frames;
    }

    static string FromFrames(long frames, double fps, bool dropFrame, char separator)
    {
        int n = Nominal(fps);
        if (dropFrame)
        {
            int drop = n / 15;
            long perTen = FramesPerTenMinutes(fps), perMinute = n * 60L - drop;
            long tens = frames / perTen, rest = frames % perTen;
            frames += drop * 9L * tens + (rest > drop ? drop * ((rest - drop) / perMinute) : 0);
        }
        long ff = frames % n, secs = frames / n;
        return string.Create(CultureInfo.InvariantCulture, $"{secs / 3600 % 24:00}:{secs / 60 % 60:00}:{secs % 60:00}{separator}{ff:00}");
    }
}
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 1–5 — najít:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Offload.Core.Desktop;

```

nahradit:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Offload.Core.Outputs;

namespace Offload.Core.Desktop;

```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 26–31 — najít:

```csharp
    /// "xxh64" (default) or "both" = xxh64 + MD5 (spec 3.5, R16). A value a newer version wrote is kept as it is and
    /// read as xxh64 (HashKinds.FromSetting).
    [JsonPropertyName("hash")] public string Hash { get; set; } = "xxh64";
    [JsonPropertyName("portNames")]
    public Dictionary<string, string> PortNames
    {
```

nahradit:

```csharp
    /// "xxh64" (default) or "both" = xxh64 + MD5 (spec 3.5, R16). A value a newer version wrote is kept as it is and
    /// read as xxh64 (HashKinds.FromSetting).
    [JsonPropertyName("hash")] public string Hash { get; set; } = "xxh64";
    /// Clip lists (and from task 8 the day report) — spec 3.2, 3.3.
    [JsonPropertyName("outputs")] public OutputSettings Outputs { get; set; } = new();
    [JsonPropertyName("portNames")]
    public Dictionary<string, string> PortNames
    {
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 56–62 — najít:

```csharp
    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    /// The top-level keys Load reads into properties; every other key goes to AppSettings.Extra.
    static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal) { "destRoots", "verify", "analyzeClips", "proxy", "tools", "naming", "portNames", "hash" };

    static readonly JsonSerializerOptions Json = new()
    {
```

nahradit:

```csharp
    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    /// The top-level keys Load reads into properties; every other key goes to AppSettings.Extra.
    static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal) { "destRoots", "verify", "analyzeClips", "proxy", "tools", "naming", "portNames", "hash", "outputs" };

    static readonly JsonSerializerOptions Json = new()
    {
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 114–119 — najít:

```csharp
            Kept("proxy", (ProxySettings v) => s.Proxy = v, () => new ProxySettings());
            Kept("tools", (ExternalTools v) => s.Tools = v, () => new ExternalTools());
            Kept("naming", (NamingSettings v) => s.Naming = v, () => new NamingSettings());
            Section("portNames", e =>
            {
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
```

nahradit:

```csharp
            Kept("proxy", (ProxySettings v) => s.Proxy = v, () => new ProxySettings());
            Kept("tools", (ExternalTools v) => s.Tools = v, () => new ExternalTools());
            Kept("naming", (NamingSettings v) => s.Naming = v, () => new NamingSettings());
            Kept("outputs", (OutputSettings v) => s.Outputs = v, () => new OutputSettings());
            Section("portNames", e =>
            {
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 141–146 — najít:

```csharp
        s.Proxy ??= new ProxySettings();
        s.Tools ??= new ExternalTools();
        s.Naming ??= new NamingSettings();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in s.PortNames ?? [])
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) names[id] = name.Trim();
```

nahradit:

```csharp
        s.Proxy ??= new ProxySettings();
        s.Tools ??= new ExternalTools();
        s.Naming ??= new NamingSettings();
        s.Outputs ??= new OutputSettings();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in s.PortNames ?? [])
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) names[id] = name.Trim();
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 2–7 — najít:

```csharp
using Offload.Core.Append;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.Core;
```

nahradit:

```csharp
using Offload.Core.Append;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Outputs;
using Offload.Core.Space;

namespace Offload.Core;
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 29–34 — najít:

```csharp
    /// Append (spec 2.2): only the card's new files go into these card folders (`<root>\<date>\<card>` of the earlier
    /// backup); DestRoots, CardName and Date are then taken from them.
    public IReadOnlyList<string>? AppendInto { get; set; }
}

public enum JobPhase { Waiting, Copying, Flushing, Verifying, Reporting, Analyzing, Proxies, Done, Failed, Cancelled }
```

nahradit:

```csharp
    /// Append (spec 2.2): only the card's new files go into these card folders (`<root>\<date>\<card>` of the earlier
    /// backup); DestRoots, CardName and Date are then taken from them.
    public IReadOnlyList<string>? AppendInto { get; set; }
    /// Clip lists CSV / ALE (spec 3.3; settings `outputs`, CLI --no-clip-list, --ale).
    public OutputSettings Outputs { get; set; } = new();
}

public enum JobPhase { Waiting, Copying, Flushing, Verifying, Reporting, Analyzing, Proxies, Done, Failed, Cancelled }
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 218–223 — najít:

```csharp
            RecordHistory(plan, report, log);
            if (!Config.AnalyzeClips && !Config.Proxy.Enabled)
            {
                WritePdfReports(report, plan, log);
                Update(s => s with { Phase = JobPhase.Done });
                return false;
```

nahradit:

```csharp
            RecordHistory(plan, report, log);
            if (!Config.AnalyzeClips && !Config.Proxy.Enabled)
            {
                WriteClipLists(plan, log);   // the clips found in the files, without metadata (spec 3.3)
                WritePdfReports(report, plan, log);
                Update(s => s with { Phase = JobPhase.Done });
                return false;
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 645–656 — najít:

```csharp
        }
    }

    static void RewriteReports(OffloadReport report, OffloadPlan plan, Action<string>? log)
    {
        foreach (var path in plan.CardPaths)
        {
            try { report.Write(path, includeHashRecords: false); }
            catch (Exception e) { log?.Invoke($"report rewrite {path}: {e.Message}"); /* the first report is already there; this only adds clip details */ }
        }
    }
}
```

nahradit:

```csharp
        }
    }

    void RewriteReports(OffloadReport report, OffloadPlan plan, Action<string>? log)
    {
        foreach (var path in plan.CardPaths)
        {
            try { report.Write(path, includeHashRecords: false); }
            catch (Exception e) { log?.Invoke($"report rewrite {path}: {e.Message}"); /* the first report is already there; this only adds clip details */ }
        }
        WriteClipLists(plan, log);
    }

    /// Spec 3.3: `_REPORT\<card>_clips.csv/.ale` on every destination, from the reports there — every generation (R25).
    /// Derived files: a failure (a list held open in Excel, a disk gone) is only logged.
    void WriteClipLists(OffloadPlan plan, Action<string>? log)
    {
        foreach (var path in plan.CardPaths)
        {
            try { ClipList.WriteForCard(path, Config.Outputs); }
            catch (Exception e) { log?.Invoke($"clip list {path}: {e.Message}"); }
        }
    }
}
```

- [ ] **Step 4: Implementace — CLI a GUI**

`windows/src/Offload.Cli/StageTwoOptions.cs`, řádky 1–6 — najít:

```csharp
using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.Cli;

```

nahradit:

```csharp
using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Outputs;

namespace Offload.Cli;

```

`windows/src/Offload.Cli/StageTwoOptions.cs`, řádky 18–27 — najít:

```csharp
    readonly Option<int> proxyBitrate = new("--proxy-bitrate") { Description = "Proxy bitrate (Mb/s) for H.264/HEVC.", DefaultValueFactory = _ => 10 };
    readonly Option<bool> noBurnIn = new("--no-burn-in") { Description = "No burn-in of clip name and timecode." };
    readonly Option<string?> proxyFolder = new("--proxy-folder") { Description = "Also copy every proxy to <folder>\\<date>\\<card name>\\ (with --proxy; never overwrites)." };

    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder })
            cmd.Options.Add(o);
    }

```

nahradit:

```csharp
    readonly Option<int> proxyBitrate = new("--proxy-bitrate") { Description = "Proxy bitrate (Mb/s) for H.264/HEVC.", DefaultValueFactory = _ => 10 };
    readonly Option<bool> noBurnIn = new("--no-burn-in") { Description = "No burn-in of clip name and timecode." };
    readonly Option<string?> proxyFolder = new("--proxy-folder") { Description = "Also copy every proxy to <folder>\\<date>\\<card name>\\ (with --proxy; never overwrites)." };
    readonly Option<bool> noClipList = new("--no-clip-list") { Description = "No clip list CSV (_REPORT\\<card>_clips.csv)." };
    readonly Option<bool> ale = new("--ale") { Description = "Also write the clip list for Avid (_REPORT\\<card>_clips.ale)." };

    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder, noClipList, ale })
            cmd.Options.Add(o);
    }

```

`windows/src/Offload.Cli/StageTwoOptions.cs`, řádky 35–40 — najít:

```csharp
        var lutPath = lutRaw != null ? CliPath.Full(lutRaw) : null;
        if (lutPath != null && !File.Exists(lutPath)) throw new ArgumentException(Msg.LutNotFound(lutPath));
        config.AnalyzeClips = !r.GetValue(noAnalyze);
        config.Proxy = new ProxySettings
        {
            Enabled = r.GetValue(proxy),
```

nahradit:

```csharp
        var lutPath = lutRaw != null ? CliPath.Full(lutRaw) : null;
        if (lutPath != null && !File.Exists(lutPath)) throw new ArgumentException(Msg.LutNotFound(lutPath));
        config.AnalyzeClips = !r.GetValue(noAnalyze);
        config.Outputs = new OutputSettings { ClipListCsv = !r.GetValue(noClipList), ClipListAle = r.GetValue(ale) };
        config.Proxy = new ProxySettings
        {
            Enabled = r.GetValue(proxy),
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 424–429 — najít:

```csharp
    /// Shown under the choice while MD5 is on.
    public string? Md5Note => HashKinds.FromSetting(Settings.Hash).HasFlag(HashKind.Md5) ? Texts.Md5Slower : null;

    public bool AnalyzeClips
    {
        get => Settings.AnalyzeClips;
```

nahradit:

```csharp
    /// Shown under the choice while MD5 is on.
    public string? Md5Note => HashKinds.FromSetting(Settings.Hash).HasFlag(HashKind.Md5) ? Texts.Md5Slower : null;

    /// "Seznam klipů CSV" / "Seznam klipů pro Avid (ALE)" (spec 3.3) — `outputs` in settings.json.
    public bool ClipListCsv
    {
        get => Settings.Outputs.ClipListCsv;
        set { Settings.Outputs.ClipListCsv = value; SettingsChanged(); Raise(); }
    }

    public bool ClipListAle
    {
        get => Settings.Outputs.ClipListAle;
        set { Settings.Outputs.ClipListAle = value; SettingsChanged(); Raise(); }
    }

    public bool AnalyzeClips
    {
        get => Settings.AnalyzeClips;
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 188–193 — najít:

```csharp
                    AnalyzeClips = Settings.AnalyzeClips,
                    Proxy = SettingsStore.Clone(Settings.Proxy),
                    Tools = SettingsStore.Clone(Settings.Tools),
                };
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), AppendRules.Title(known.Entry.CardName),
                    canEject: isVolume && CardEject.CanEject(src), space);
```

nahradit:

```csharp
                    AnalyzeClips = Settings.AnalyzeClips,
                    Proxy = SettingsStore.Clone(Settings.Proxy),
                    Tools = SettingsStore.Clone(Settings.Tools),
                    Outputs = SettingsStore.Clone(Settings.Outputs),
                };
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), AppendRules.Title(known.Entry.CardName),
                    canEject: isVolume && CardEject.CanEject(src), space);
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 210–215 — najít:

```csharp
                AnalyzeClips = Settings.AnalyzeClips,
                Proxy = SettingsStore.Clone(Settings.Proxy),
                Tools = SettingsStore.Clone(Settings.Tools),
            };
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
                canEject: isVolume && CardEject.CanEject(src), space);
```

nahradit:

```csharp
                AnalyzeClips = Settings.AnalyzeClips,
                Proxy = SettingsStore.Clone(Settings.Proxy),
                Tools = SettingsStore.Clone(Settings.Tools),
                Outputs = SettingsStore.Clone(Settings.Outputs),
            };
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
                canEject: isVolume && CardEject.CanEject(src), space);
```

`windows/src/Offload.App/SetupPanel.xaml`, řádky 197–202 — najít:

```xml
                <CheckBox Content="Analýza klipů a náhledy v reportu" IsChecked="{Binding AnalyzeClips}" />
                <CheckBox Content="Navrhovat název karty podle klipů" IsChecked="{Binding SuggestCardNames}"
                          AutomationProperties.AutomationId="SuggestCardNames" />

                <!-- Proxy -->
                <StackPanel Orientation="Horizontal" Margin="0,18,0,0">
```

nahradit:

```xml
                <CheckBox Content="Analýza klipů a náhledy v reportu" IsChecked="{Binding AnalyzeClips}" />
                <CheckBox Content="Navrhovat název karty podle klipů" IsChecked="{Binding SuggestCardNames}"
                          AutomationProperties.AutomationId="SuggestCardNames" />
                <CheckBox Content="Seznam klipů CSV" IsChecked="{Binding ClipListCsv}" AutomationProperties.AutomationId="ClipListCsv" />
                <CheckBox Content="Seznam klipů pro Avid (ALE)" IsChecked="{Binding ClipListAle}" AutomationProperties.AutomationId="ClipListAle" />

                <!-- Proxy -->
                <StackPanel Orientation="Horizontal" Margin="0,18,0,0">
```

- [ ] **Step 5: Spustit — projdou**

Run: stejný filtr jako v kroku 2.
Expected: PASS. Pak `dotnet build windows\Offload.sln -warnaserror` a celá sada (`AppendEngineTests` musí projít — doplnění teď přepisuje i `A001_clips.csv`).

- [ ] **Step 6: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 20–25 — najít:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 5 — výstupy MD5: v reportu `hashAlgorithms` (jen když není `["xxh64"]`) a `files[].md5`, `hashAlgorithm` zůstává `xxh64`; `_REPORT\<karta>.md5` (formát md5sum, při doplnění se prodlouží jako `.xxh64`); v ASC MHL `<md5>` vedle `<xxh64>`; TXT `Hash: xxh64+md5`, HTML dlaždice „XXH64 + MD5“ a sloupec md5; nastavení `hash` (`xxh64` | `both`, neznámá hodnota se zachová a čte jako xxh64), v GUI „Kontrolní součet“ + „Ověřit kopie zpětným čtením (…)“ + upozornění na pomalejší MD5; CLI `copy --hash xxh64|both`, `bench --hash md5`; doplnění a historie podle algoritmů dřívější zálohy
- Úkol 4 — jádro MD5 (R16: jen navíc k xxh64): `HashKind` (`Xxh64`, `Md5`, xxh64 vždy), `FileHashes` místo `ulong?[]`, jeden `SourceHasher` na algoritmus na vlastním vlákně (blok karty má o konzumenta víc, MD5 nebrzdí xxh64 ani čtení karty), zpětné čtení cíle hashuje všemi zvolenými v jednom průchodu („MD5 MISMATCH“ u nesouhlasu); celá kopie podle `OffloadOptions.Hashes`, doplnění podle dřívější zálohy (`OffloadPlan.Hashes`)
- Úkol 3 — proxy pro přelinkování: s platným TC navíc `reel_name` (muxer MOV ho zapíše do stopy timecodu `tmcd`, ffprobe ho tam ukáže; ověřeno s ffmpeg 8.1), všechny zvukové proudy zdroje (`-map 0:a?`; H.264/HEVC AAC 64 kb/s na kanál každého proudu, 128–512 kb/s; ProRes PCM), bez TC ani `tmcd`, ani cívka; proxy přejmenovaná kvůli shodě jmen má poznámku „Proxy přejmenována na … (shoda jmen) — přelinkujte ji ručně.“; odhad místa počítá 0,2 Mb/s na každou zvukovou stopu
````

nahradit:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 6 — seznam klipů karty `_REPORT\<karta>_clips.csv` (UTF-8 s BOM, čárka, CRLF, RFC 4180; sloupce Clip Name … MD5 podle návrhu, hodnoty s `=`/`+`/`-`/`@` beze změny) a `<karta>_clips.ale` pro Avid (tabulátory, jeden soubor na fps, `End` = start + délka, drop-frame počítá jako kamera, bez TC `00:00:00:00`); píše se při každém přepisu reportu a po doplnění pokrývá všechny generace (bez analýzy z nalezených klipů, bez metadat); proxy kamery se neuvádějí; volby `outputs.clipListCsv` (zapnuto) a `outputs.clipListAle` (vypnuto), v GUI „Seznam klipů CSV“ a „Seznam klipů pro Avid (ALE)“, CLI `--no-clip-list`, `--ale`; seznam otevřený v Excelu se jen zaloguje
- Úkol 5 — výstupy MD5: v reportu `hashAlgorithms` (jen když není `["xxh64"]`) a `files[].md5`, `hashAlgorithm` zůstává `xxh64`; `_REPORT\<karta>.md5` (formát md5sum, při doplnění se prodlouží jako `.xxh64`); v ASC MHL `<md5>` vedle `<xxh64>`; TXT `Hash: xxh64+md5`, HTML dlaždice „XXH64 + MD5“ a sloupec md5; nastavení `hash` (`xxh64` | `both`, neznámá hodnota se zachová a čte jako xxh64), v GUI „Kontrolní součet“ + „Ověřit kopie zpětným čtením (…)“ + upozornění na pomalejší MD5; CLI `copy --hash xxh64|both`, `bench --hash md5`; doplnění a historie podle algoritmů dřívější zálohy
- Úkol 4 — jádro MD5 (R16: jen navíc k xxh64): `HashKind` (`Xxh64`, `Md5`, xxh64 vždy), `FileHashes` místo `ulong?[]`, jeden `SourceHasher` na algoritmus na vlastním vlákně (blok karty má o konzumenta víc, MD5 nebrzdí xxh64 ani čtení karty), zpětné čtení cíle hashuje všemi zvolenými v jednom průchodu („MD5 MISMATCH“ u nesouhlasu); celá kopie podle `OffloadOptions.Hashes`, doplnění podle dřívější zálohy (`OffloadPlan.Hashes`)
- Úkol 3 — proxy pro přelinkování: s platným TC navíc `reel_name` (muxer MOV ho zapíše do stopy timecodu `tmcd`, ffprobe ho tam ukáže; ověřeno s ffmpeg 8.1), všechny zvukové proudy zdroje (`-map 0:a?`; H.264/HEVC AAC 64 kb/s na kanál každého proudu, 128–512 kb/s; ProRes PCM), bez TC ani `tmcd`, ani cívka; proxy přejmenovaná kvůli shodě jmen má poznámku „Proxy přejmenována na … (shoda jmen) — přelinkujte ji ručně.“; odhad místa počítá 0,2 Mb/s na každou zvukovou stopu
````

Zpráva do `.git/COMMIT_MSG_PLAN7`:

```text
Windows: clip lists CSV and Avid ALE per card (all generations)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.App/MainViewModel.Queue.cs windows/src/Offload.App/MainViewModel.Setup.cs windows/src/Offload.App/SetupPanel.xaml windows/src/Offload.Cli/StageTwoOptions.cs windows/src/Offload.Core/Desktop/AppSettings.cs windows/src/Offload.Core/OffloadJob.cs windows/src/Offload.Core/Outputs/ClipList.cs windows/src/Offload.Core/Timecode.cs windows/tests/Offload.Core.Tests/AppendEngineTests.cs windows/tests/Offload.Core.Tests/CliOutputsTests.cs windows/tests/Offload.Core.Tests/ClipListTests.cs windows/tests/Offload.Core.Tests/SettingsStoreTests.cs windows/tests/Offload.Core.Tests/TimecodeEncoderTests.cs
git commit -F .git/COMMIT_MSG_PLAN7
```

---

### Task 7: Denní report — model, HTML, PDF, seznam klipů dne

Spec §3.2 (data, obsah HTML/PDF), R12, R25, §5.5, §6.2 úkol 7. `DayReport.Load(dayPath)` vezme každou podsložku `<datum>\` (kromě `_REPORT` a skrytých) jako kartu (`CardRecord`), `Html` vykreslí přehled ve stylu `HtmlReport` (A4 na šířku při tisku, bez náhledů), `Write` zapíše HTML, PDF (Edge, chyba jen do logu) a seznam klipů dne do `<datum>\_REPORT\`. Kdy se tvoří (po úloze, zámky, menu, CLI) je úkol 8. `<datum>\_REPORT` nemá `offload_report.json` ani `.xxh64`, takže `VerifyTargets` ho za kartu nepovažuje — test to hlídá.

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md` (řádky 20–25)
- Create: `windows/src/Offload.Core/Outputs/DayReport.cs`
- Create: `windows/tests/Offload.Core.Tests/DayReportTests.cs`

**Interfaces:**
- Consumes: `CardRecord` (úkol 1), `ClipList.Rows/Write`, `OutputSettings` (úkol 6), `HtmlReport.Styles/Esc/Duration/DateTimeText` (internal), `PdfReport.WriteHtml(string html, string path, …)`, `Win32.StrCmpLogicalW`, `Texts.Plural`, `VerifyTargets.Resolve` (test).
- Produces: `sealed class DayCard` (`Record`, `Name`, `NoReport`, `Unreadable`, `ClipCount`, `Seconds`, `Cameras`, `Luts`, `ProxiesDone`, `ProxiesFailed`); `sealed class DayModel` (`DayPath`, `Date`, `Root`, `Cards`, `VerifiedCards`); `static class DayReport { HtmlName(string date); PdfName(string date); Load(string dayPath) → DayModel; Html(DayModel, bool forPrint = false) → string; Problems(DayModel) → IReadOnlyList<(string Card, string Text, bool Bad)>; Write(string dayPath, OutputSettings outputs, Action<string>? log = null) → string (cesta HTML) }`; testovací `DayFixtures.Day(TempDir) → string` (den s kartami A001 … E001; použije ho i úkol 8).

- [ ] **Step 1: Padající testy**

Nový soubor `windows/tests/Offload.Core.Tests/DayReportTests.cs`:

```csharp
using System.Text;
using Offload.Core.Outputs;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// A day folder of five cards (spec 3.2 tests): a Windows card with clips and LUTs, a Mac report without the new keys
/// and a failed file, a card with an unverified append, a damaged report and a folder without one.
public static class DayFixtures
{
    static readonly LutUse LogC4 = new("auto", "arri-logc4-awg4", "ARRI LogC4 → Rec.709", "ARRI");

    static ClipEntry Clip(string path, double seconds, ProxyStatus proxy) => new()
    {
        Clip = new Clip { Name = Path.GetFileNameWithoutExtension(path), Path = path, Segments = [path], Bytes = 1000 },
        Info = new ClipInfo { Camera = "ARRI ALEXA 35", Duration = seconds, FrameRate = "25/1", Timecode = "01:00:00:00" },
        Lut = LogC4,
        ProxyStatus = proxy,
    };

    static void MainReport(string cardPath, OffloadReport report)
    {
        Directory.CreateDirectory(Path.Combine(cardPath, "_REPORT"));
        File.WriteAllText(Path.Combine(cardPath, "_REPORT", "offload_report.json"), report.ToJson());
    }

    /// `<dir>\D\2026-10-06\` with A001 … E001, a `_REPORT` holding a verify report of a foreign backup, a hidden folder.
    public static string Day(TempDir dir)
    {
        var day = dir.Sub("D", "2026-10-06");
        var a = Path.Combine(day, "A001");
        var ra = Reports.Sample(a);
        ra.Clips = [Clip("CLIP/A&B.mov", 60, ProxyStatus.Done), Clip("CLIP/c 02.mov", 30, ProxyStatus.Failed)];
        MainReport(a, ra);
        MainReport(Path.Combine(day, "B001"), Reports.Sample(Path.Combine(day, "B001"), destError: "write failed"));
        var c = Path.Combine(day, "C001");
        MainReport(c, Reports.Sample(c));
        var append = Reports.Sample(c, verified: false);
        append.Generation = 2;
        File.WriteAllText(Path.Combine(c, "_REPORT", "offload_report_0002.json"), append.ToJson());
        Directory.CreateDirectory(Path.Combine(day, "D001", "_REPORT"));
        File.WriteAllText(Path.Combine(day, "D001", "_REPORT", "offload_report.json"), "{broken");
        Directory.CreateDirectory(Path.Combine(day, "E001", "CLIP"));
        Directory.CreateDirectory(Path.Combine(day, "_REPORT"));
        File.WriteAllText(Path.Combine(day, "_REPORT", "X001_verify_2026-10-06_120000Z.json"), "{}");
        Directory.CreateDirectory(Path.Combine(day, ".offload-tmp"));
        return day;
    }
}

public class DayReportTests
{
    [Fact]
    public void Load_EveryCardFolder_ButTheDaysReportFolder()
    {
        using var dir = new TempDir();
        var day = DayReport.Load(DayFixtures.Day(dir));

        Assert.Equal(["A001", "B001", "C001", "D001", "E001"], day.Cards.Select(c => c.Name));
        Assert.Equal("2026-10-06", day.Date);
        Assert.Equal(Path.Combine(dir.Path, "D"), day.Root);
        Assert.Equal(1, day.VerifiedCards);   // A001; B001 has a failed file, C001 an unverified append
        var a = day.Cards[0];
        Assert.Equal((2, 90.0, 1, 1), (a.ClipCount, a.Seconds, a.ProxiesDone, a.ProxiesFailed));
        Assert.Equal(["ARRI ALEXA 35"], a.Cameras);
        Assert.Equal(2, a.Luts["ARRI LogC4 → Rec.709"]);
        Assert.Equal(2, day.Cards[1].ClipCount);   // the Mac report has no clips: found in its files
        Assert.True(day.Cards[4].NoReport);
        Assert.NotNull(day.Cards[3].Unreadable);
    }

    [Fact]
    public void Html_TilesTableProblemsLuts()
    {
        using var dir = new TempDir();
        var html = DayReport.Html(DayReport.Load(DayFixtures.Day(dir)));

        Assert.Contains("<h1>Denní report — 6. 10. 2026</h1>", html);
        Assert.Contains("VYŽADUJE POZORNOST", html);
        Assert.Contains("<b>5</b><span>karet</span>", html);
        Assert.Contains("<b>6</b><span>klipů</span>", html);
        Assert.Contains("<b>1:30</b><span>celková délka</span>", html);
        Assert.Contains("<b>1 / 5</b><span>ověřené karty</span>", html);
        Assert.Contains("<td>E001</td><td colspan=\"11\" class=\"warn\">bez reportu (probíhá, nebo byl přerušen)</td>", html);
        Assert.Contains("<td>D001</td><td colspan=\"11\" class=\"warn\">report nejde přečíst</td>", html);
        Assert.Contains("<td>1 / 1</td><td class=\"wrap\">ARRI LogC4 → Rec.709</td><td class=\"num\">—</td>", html);   // A001
        Assert.Contains("<td class=\"warn\">NEOVĚŘENO</td>", html);   // C001
        Assert.Contains("<td class=\"num\">2</td></tr>", html);      // C001: two generations
        Assert.Contains("1 soubor selhal: CLIP/c 02.mov", html);    // B001
        Assert.Contains("neověřeno — kopie nebyla přečtena zpět", html);
        Assert.Contains("<tr><td>ARRI LogC4 → Rec.709</td><td class=\"num\">2</td></tr>", html);
        Assert.Contains("@page{size:A4 landscape", DayReport.Html(DayReport.Load(Path.Combine(dir.Path, "D", "2026-10-06")), forPrint: true));
    }

    // Spec 5.5: a day of 20 cards — reading the reports and the HTML within a second.
    [Fact]
    public void TwentyCards_HtmlUnderOneSecond()
    {
        using var dir = new TempDir();
        var day = dir.Sub("2026-10-06");
        for (int c = 1; c <= 20; c++)
        {
            var card = Path.Combine(day, $"A{c:000}");
            var r = Reports.Sample(card);
            r.Clips = Enumerable.Range(1, 100).Select(i => new ClipEntry
            {
                Clip = new Clip { Name = $"A{c:000}C{i:000}", Path = $"CLIP/A{c:000}C{i:000}.mov", Segments = [$"CLIP/A{c:000}C{i:000}.mov"], Bytes = 1 },
                Info = new ClipInfo { Camera = "ARRI ALEXA 35", Duration = 30, FrameRate = "25/1" },
            }).ToList();
            Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
            File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report.json"), r.ToJson());
        }
        DayReport.Html(DayReport.Load(day));   // warm-up (JIT)

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var html = DayReport.Html(DayReport.Load(day));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"{clock.ElapsedMilliseconds} ms");
        Assert.Contains("<b>2000</b><span>klipů</span>", html);
    }

    [Fact]
    public void Html_EscapesCardNames()
    {
        using var dir = new TempDir();
        var day = dir.Sub("2026-10-06");
        Directory.CreateDirectory(Path.Combine(day, "F&G"));
        Assert.Contains("<td>F&amp;G</td>", DayReport.Html(DayReport.Load(day)));
        Assert.Contains("<b>0 / 1</b>", DayReport.Html(DayReport.Load(day)));
    }

    // Spec 3.2: HTML and the day's clip lists in <day>\_REPORT; the PDF is a convenience (skipped in tests, logged); the
    // day folder is still not a card for "Ověřit zálohu" (its _REPORT has no report or hash list).
    [Fact]
    public void Write_HtmlAndDayClipLists_DayIsNoCard()
    {
        using var dir = new TempDir();
        var dayPath = DayFixtures.Day(dir);
        var log = new List<string>();

        var html = DayReport.Write(dayPath, new OutputSettings { ClipListCsv = true, ClipListAle = true }, log.Add);

        var rep = Path.Combine(dayPath, "_REPORT");
        Assert.Equal(Path.Combine(rep, "2026-10-06_day_report.html"), html);
        Assert.StartsWith("<!doctype html>", File.ReadAllText(html, Encoding.UTF8));
        var csv = File.ReadAllLines(Path.Combine(rep, "2026-10-06_clips.csv"));
        Assert.Equal(1 + 6, csv.Length);
        Assert.Equal(["A001", "A001", "B001", "B001", "C001", "C001"], csv.Skip(1).Select(l => l.Split(',')[3]));
        Assert.True(File.Exists(Path.Combine(rep, "2026-10-06_clips.ale")));
        Assert.Contains(log, m => m.StartsWith("day report PDF ", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(rep, "*.tmp"));
        Assert.Equal(["A001", "B001", "C001", "D001"], VerifyTargets.Resolve(dayPath).Select(Path.GetFileName));
    }
}

/// Launches Edge: in the Edge collection.
[Collection("Edge")]
public class DayReportPdfTests
{
    [EdgeFact]
    public void Write_PrintsThePdf()
    {
        using var pdfs = PdfReport.PrintHere();
        using var dir = new TempDir();
        var dayPath = DayFixtures.Day(dir);

        DayReport.Write(dayPath, new OutputSettings());

        var pdf = File.ReadAllBytes(Path.Combine(dayPath, "_REPORT", "2026-10-06_day_report.pdf"));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
```

- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~DayReport"`
Expected: build FAIL — `CS0103: Název 'DayReport' v aktuálním kontextu neexistuje`.

- [ ] **Step 3: Implementace**

Nový soubor `windows/src/Offload.Core/Outputs/DayReport.cs`:

```csharp
using System.Globalization;
using System.Text;
using Offload.Core.Desktop;

namespace Offload.Core.Outputs;

/// One card folder of a day as the day report shows it (spec 3.2) — sums over every generation of its reports.
public sealed class DayCard
{
    public required CardRecord Record { get; init; }
    public string Name => Record.FolderName;
    /// The folder has no `_REPORT\offload_report.json`: a copy in progress, or one that was stopped.
    public bool NoReport => Record.ReportMissing;
    /// The report could not be read (Error), or an append report could not (Problems).
    public string? Unreadable => Record.Error ?? Record.Problems.FirstOrDefault();
    IEnumerable<ClipEntry> Clips => Record.Clips.Select(c => c.Entry);
    IEnumerable<ClipEntry> VideoClips => Clips.Where(c => !c.Clip.IsCameraProxy);
    public int ClipCount => VideoClips.Count();
    public double Seconds => VideoClips.Sum(c => c.Info?.Duration ?? 0);
    public IReadOnlyList<string> Cameras =>
        VideoClips.Select(c => c.Info?.Camera?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList()!;
    /// LUT name → clips (no LUT and switched-off LUT are left out).
    public IReadOnlyDictionary<string, int> Luts =>
        VideoClips.Where(c => c.Lut is { Source: not "none" }).GroupBy(c => c.Lut!.Name).ToDictionary(g => g.Key, g => g.Count());
    public int ProxiesDone => Clips.Count(c => c.ProxyStatus == ProxyStatus.Done);
    public int ProxiesFailed => Clips.Count(c => c.ProxyStatus == ProxyStatus.Failed);
}

/// A day on one destination (spec 3.2): every card folder in `<root>\<date>\`.
public sealed class DayModel
{
    public required string DayPath { get; init; }
    /// The date folder's name ("2026-10-06").
    public string Date => Path.GetFileName(DayPath.TrimEnd('\\', '/'));
    /// The destination the day is on.
    public string Root => Path.GetDirectoryName(DayPath.TrimEnd('\\', '/')) ?? DayPath;
    public required IReadOnlyList<DayCard> Cards { get; init; }
    public int VerifiedCards => Cards.Count(c => c.Record.Verified && c.Record.FailedFiles.Count == 0);
}

/// The day report (spec 3.2, R12): `<root>\<date>\_REPORT\<date>_day_report.html/.pdf` and the day's clip lists
/// `<date>_clips.csv/.ale`. Derived from the cards' reports — written whole every time (R25).
public static class DayReport
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string HtmlName(string date) => $"{date}_day_report.html";
    public static string PdfName(string date) => $"{date}_day_report.pdf";

    /// The card folders of `dayPath` (every subfolder but `_REPORT` and hidden ones), in Explorer order. Reads only.
    public static DayModel Load(string dayPath)
    {
        var folders = Directory.GetDirectories(dayPath)
            .Where(d => Path.GetFileName(d) is var n && !n.StartsWith('.') && !string.Equals(n, Layout.ReportDirName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        folders.Sort((a, b) => Win32.StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
        return new DayModel { DayPath = dayPath, Cards = folders.Select(f => new DayCard { Record = CardRecord.Load(f) }).ToList() };
    }

    /// "6. 10. 2026" for "2026-10-06"; the folder name itself when it is not a date.
    static string DateText(string date) =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d.ToString("d. M. yyyy", Cs) : date;

    static string Time(DateTime utc) => utc.ToLocalTime().ToString("H:mm", Cs);

    static string Files(int n) => $"{n} {Texts.Plural(n, "soubor", "soubory", "souborů")}";

    static void Stat(StringBuilder h, string value, string label) =>
        h.Append($"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>\n");

    /// Self-contained HTML, A4 landscape when printed; no thumbnails.
    public static string Html(DayModel day, bool forPrint = false)
    {
        string E(string s) => HtmlReport.Esc(s);
        var cards = day.Cards;
        var h = new StringBuilder();
        h.Append("<!doctype html>\n<html lang=\"cs\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Denní report {E(day.Date)}</title>\n<style>\n{HtmlReport.Styles(forPrint)}</style></head><body><main>\n");
        int verified = day.VerifiedCards;
        var (badge, badgeClass) = verified == cards.Count && cards.Count > 0 ? ("VŠE OVĚŘENO", "ok") : ("VYŽADUJE POZORNOST", "warn");
        h.Append($"<header><h1>Denní report — {E(DateText(day.Date))}</h1><span class=\"badge {badgeClass}\">{badge}</span>\n");
        h.Append($"<span class=\"sub\">Cíl: <span class=\"mono\">{E(day.Root)}</span></span></header>\n");
        h.Append("<div class=\"stats\">\n");
        Stat(h, cards.Count.ToString(Inv), Texts.Plural(cards.Count, "karta", "karty", "karet"));
        Stat(h, cards.Sum(c => c.Record.TotalFiles).ToString(Inv), "souborů");
        Stat(h, Format.Bytes(cards.Sum(c => c.Record.TotalBytes)), "data");
        Stat(h, cards.Sum(c => c.ClipCount).ToString(Inv), "klipů");
        Stat(h, HtmlReport.Duration(cards.Sum(c => c.Seconds)), "celková délka");
        Stat(h, $"{verified} / {cards.Count}", "ověřené karty");
        h.Append("</div>\n\n");

        h.Append("<h2>Karty</h2><div class=\"panel\"><table>\n");
        h.Append("<tr><th>Karta</th><th>Čas</th><th class=\"num\">Soubory</th><th class=\"num\">Data</th><th class=\"num\">Klipy</th><th class=\"num\">Délka</th>");
        h.Append("<th>Kamery</th><th>Ověření</th><th class=\"num\">Chyby</th><th>Proxy</th><th>LUTy</th><th class=\"num\">Doplnění</th></tr>\n");
        foreach (var c in cards)
        {
            var r = c.Record;
            if (c.NoReport || (c.Unreadable != null && r.Generations.Count == 0))
            {
                var why = c.NoReport ? "bez reportu (probíhá, nebo byl přerušen)" : "report nejde přečíst";
                h.Append($"<tr><td>{E(c.Name)}</td><td colspan=\"11\" class=\"warn\">{why}</td></tr>\n");
                continue;
            }
            var (vClass, vText) = r.Verified ? ("ok", "ověřeno") : ("warn", "NEOVĚŘENO");
            int failed = r.FailedFiles.Count;
            var proxies = c.ProxiesDone + c.ProxiesFailed == 0 ? "—" : $"{c.ProxiesDone} / {c.ProxiesFailed}";
            var lutNames = c.Luts.Count == 0 ? "—" : string.Join(", ", c.Luts.Keys);
            var generations = r.Generations.Count > 1 ? r.Generations.Count.ToString(Inv) : "—";
            h.Append($"<tr><td>{E(c.Name)}</td><td>{Time(r.Started!.Value)}–{Time(r.Finished!.Value)}</td>");
            h.Append($"<td class=\"num\">{r.TotalFiles}</td><td class=\"num\">{Format.Bytes(r.TotalBytes)}</td><td class=\"num\">{c.ClipCount}</td>");
            h.Append($"<td class=\"num\">{(c.Seconds > 0 ? HtmlReport.Duration(c.Seconds) : "—")}</td><td class=\"wrap\">{E(c.Cameras.Count == 0 ? "—" : string.Join(", ", c.Cameras))}</td>");
            h.Append($"<td class=\"{vClass}\">{vText}</td><td class=\"num {(failed > 0 ? "bad" : "muted")}\">{failed}</td>");
            h.Append($"<td>{proxies}</td><td class=\"wrap\">{E(lutNames)}</td><td class=\"num\">{generations}</td></tr>\n");
        }
        h.Append("</table></div>\n");
        h.Append("<p class=\"sub\">Proxy: hotovo / selhalo. Doplnění: počet generací ASC MHL, když karta byla doplněna.</p>\n");

        var problems = Problems(day);
        if (problems.Count > 0)
        {
            h.Append($"<h2 class=\"warn\">Problémy ({problems.Count})</h2><div class=\"panel\"><table>\n");
            foreach (var (name, text, bad) in problems)
                h.Append($"<tr><td>{E(name)}</td><td class=\"{(bad ? "err" : "warn wrap")}\">{E(text)}</td></tr>\n");
            h.Append("</table></div>\n");
        }

        var luts = cards.SelectMany(c => c.Luts).GroupBy(kv => kv.Key).Select(g => (Name: g.Key, Clips: g.Sum(kv => kv.Value)))
            .OrderByDescending(x => x.Clips).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
        if (luts.Count > 0)
        {
            h.Append("<h2>LUTy</h2><div class=\"panel\"><table>\n<tr><th>LUT</th><th class=\"num\">Klipy</th></tr>\n");
            foreach (var (name, clips) in luts) h.Append($"<tr><td>{E(name)}</td><td class=\"num\">{clips}</td></tr>\n");
            h.Append("</table></div>\n");
        }
        h.Append($"<footer>offload {E(OffloadVersion.String)} · denní report · {HtmlReport.DateTimeText(DateTime.Now)}</footer></main></body></html>\n");
        return h.ToString();
    }

    /// The Problems section: unreadable reports, folders without one, failed files (count + the first five), cards not
    /// verified. `Bad`: red.
    public static IReadOnlyList<(string Card, string Text, bool Bad)> Problems(DayModel day)
    {
        var list = new List<(string, string, bool)>();
        foreach (var c in day.Cards)
        {
            if (c.NoReport)
            {
                list.Add((c.Name, "bez reportu (probíhá, nebo byl přerušen)", false));
                continue;
            }
            if (c.Unreadable is string why) list.Add((c.Name, $"report nejde přečíst: {why}", true));
            if (c.Record.Generations.Count == 0) continue;
            var failed = c.Record.FailedFiles;
            if (failed.Count > 0)
                list.Add((c.Name, $"{Files(failed.Count)} {Texts.Plural(failed.Count, "selhal", "selhaly", "selhalo")}: "
                    + string.Join(", ", failed.Take(5)) + (failed.Count > 5 ? ", …" : ""), true));
            if (!c.Record.Verified && c.Unreadable == null) list.Add((c.Name, "neověřeno — kopie nebyla přečtena zpět", false));
        }
        return list;
    }

    /// Writes the HTML, the PDF (best effort, logged) and the day's clip lists into `<day>\_REPORT`. Returns the HTML path.
    /// Throws when the folder cannot be read or the HTML cannot be written.
    public static string Write(string dayPath, OutputSettings outputs, Action<string>? log = null)
    {
        var day = Load(dayPath);
        var dir = Path.Combine(dayPath, Layout.ReportDirName);
        NativeIO.Mkdirs(dir);
        var html = Path.Combine(dir, HtmlName(day.Date));
        NativeIO.WriteFileReplacing(html, new UTF8Encoding(false).GetBytes(Html(day)));
        try
        {
            ClipList.Write(dir, day.Date, day.Cards.SelectMany(c => ClipList.Rows(c.Record)).ToList(), outputs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or IOError)
        {
            log?.Invoke($"day clip list {dir}: {e.Message}");
        }
        try
        {
            PdfReport.WriteHtml(Html(day, forPrint: true), Path.Combine(dir, PdfName(day.Date)));
        }
        catch (Exception e)
        {
            log?.Invoke($"day report PDF {dir}: {e.Message}");   // the HTML is there; the PDF is a convenience copy
        }
        return html;
    }
}
```

- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~DayReport"`
Expected: PASS (`DayReportPdfTests.Write_PrintsThePdf` je `[EdgeFact]` v kolekci „Edge“ — s Edge vytiskne PDF; `TwentyCards_HtmlUnderOneSecond` hlídá rozpočet §5.5). Pak `dotnet build windows\Offload.sln -warnaserror` a celá sada.

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 20–25 — najít:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 6 — seznam klipů karty `_REPORT\<karta>_clips.csv` (UTF-8 s BOM, čárka, CRLF, RFC 4180; sloupce Clip Name … MD5 podle návrhu, hodnoty s `=`/`+`/`-`/`@` beze změny) a `<karta>_clips.ale` pro Avid (tabulátory, jeden soubor na fps, `End` = start + délka, drop-frame počítá jako kamera, bez TC `00:00:00:00`); píše se při každém přepisu reportu a po doplnění pokrývá všechny generace (bez analýzy z nalezených klipů, bez metadat); proxy kamery se neuvádějí; volby `outputs.clipListCsv` (zapnuto) a `outputs.clipListAle` (vypnuto), v GUI „Seznam klipů CSV“ a „Seznam klipů pro Avid (ALE)“, CLI `--no-clip-list`, `--ale`; seznam otevřený v Excelu se jen zaloguje
- Úkol 5 — výstupy MD5: v reportu `hashAlgorithms` (jen když není `["xxh64"]`) a `files[].md5`, `hashAlgorithm` zůstává `xxh64`; `_REPORT\<karta>.md5` (formát md5sum, při doplnění se prodlouží jako `.xxh64`); v ASC MHL `<md5>` vedle `<xxh64>`; TXT `Hash: xxh64+md5`, HTML dlaždice „XXH64 + MD5“ a sloupec md5; nastavení `hash` (`xxh64` | `both`, neznámá hodnota se zachová a čte jako xxh64), v GUI „Kontrolní součet“ + „Ověřit kopie zpětným čtením (…)“ + upozornění na pomalejší MD5; CLI `copy --hash xxh64|both`, `bench --hash md5`; doplnění a historie podle algoritmů dřívější zálohy
- Úkol 4 — jádro MD5 (R16: jen navíc k xxh64): `HashKind` (`Xxh64`, `Md5`, xxh64 vždy), `FileHashes` místo `ulong?[]`, jeden `SourceHasher` na algoritmus na vlastním vlákně (blok karty má o konzumenta víc, MD5 nebrzdí xxh64 ani čtení karty), zpětné čtení cíle hashuje všemi zvolenými v jednom průchodu („MD5 MISMATCH“ u nesouhlasu); celá kopie podle `OffloadOptions.Hashes`, doplnění podle dřívější zálohy (`OffloadPlan.Hashes`)
````

nahradit:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 7 — denní report `<cíl>\<datum>\_REPORT\<datum>_day_report.html/.pdf` (A4 na šířku, bez náhledů): dlaždice karty, soubory, data, klipy, celková délka, ověřené karty „N / M“; tabulka karet (čas, soubory, data, klipy, délka, kamery, ověření, chyby, proxy hotovo/selhalo, LUTy, počet generací); sekce Problémy (selhané soubory + prvních 5 cest, neověřené, nečitelné reporty, složka bez reportu „bez reportu (probíhá, nebo byl přerušen)“) a LUTy; denní seznam klipů `<datum>_clips.csv/.ale` přes všechny karty; složka `_REPORT` dne se za kartu nepovažuje (ani pro Ověřit zálohu)
- Úkol 6 — seznam klipů karty `_REPORT\<karta>_clips.csv` (UTF-8 s BOM, čárka, CRLF, RFC 4180; sloupce Clip Name … MD5 podle návrhu, hodnoty s `=`/`+`/`-`/`@` beze změny) a `<karta>_clips.ale` pro Avid (tabulátory, jeden soubor na fps, `End` = start + délka, drop-frame počítá jako kamera, bez TC `00:00:00:00`); píše se při každém přepisu reportu a po doplnění pokrývá všechny generace (bez analýzy z nalezených klipů, bez metadat); proxy kamery se neuvádějí; volby `outputs.clipListCsv` (zapnuto) a `outputs.clipListAle` (vypnuto), v GUI „Seznam klipů CSV“ a „Seznam klipů pro Avid (ALE)“, CLI `--no-clip-list`, `--ale`; seznam otevřený v Excelu se jen zaloguje
- Úkol 5 — výstupy MD5: v reportu `hashAlgorithms` (jen když není `["xxh64"]`) a `files[].md5`, `hashAlgorithm` zůstává `xxh64`; `_REPORT\<karta>.md5` (formát md5sum, při doplnění se prodlouží jako `.xxh64`); v ASC MHL `<md5>` vedle `<xxh64>`; TXT `Hash: xxh64+md5`, HTML dlaždice „XXH64 + MD5“ a sloupec md5; nastavení `hash` (`xxh64` | `both`, neznámá hodnota se zachová a čte jako xxh64), v GUI „Kontrolní součet“ + „Ověřit kopie zpětným čtením (…)“ + upozornění na pomalejší MD5; CLI `copy --hash xxh64|both`, `bench --hash md5`; doplnění a historie podle algoritmů dřívější zálohy
- Úkol 4 — jádro MD5 (R16: jen navíc k xxh64): `HashKind` (`Xxh64`, `Md5`, xxh64 vždy), `FileHashes` místo `ulong?[]`, jeden `SourceHasher` na algoritmus na vlastním vlákně (blok karty má o konzumenta víc, MD5 nebrzdí xxh64 ani čtení karty), zpětné čtení cíle hashuje všemi zvolenými v jednom průchodu („MD5 MISMATCH“ u nesouhlasu); celá kopie podle `OffloadOptions.Hashes`, doplnění podle dřívější zálohy (`OffloadPlan.Hashes`)
````

Zpráva do `.git/COMMIT_MSG_PLAN7`:

```text
Windows: day report (HTML, PDF, day clip lists)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/Outputs/DayReport.cs windows/tests/Offload.Core.Tests/DayReportTests.cs
git commit -F .git/COMMIT_MSG_PLAN7
```

---

### Task 8: Regenerace denního reportu — po úloze, zámky, menu „Denní report…“, CLI `day-report`

Spec §3.2 „Kdy se tvoří“ a okrajové případy, §5.1 `outputs.dayReport`, §5.7, §6.2 úkol 8. `DayReportService.Regenerate(root, date, …)` volá `OffloadJob` na pracovním vlákně na konci každé úlohy pro každý cíl (stage 2; bez stage 2 po stage 1; i doplnění; i zrušená úloha — karta se ukáže jako neúplná). Jeden zapisovatel na den: `SemaphoreSlim` na složku dne v procesu + pojmenovaný mutex `Local\Offload-day-<sha1(cesty)>` mezi procesy (GUI + CLI), čekání nejvýš 2 min, pak česká `TimeoutException`; úloha každé selhání jen zaloguje (`day report <cesta>: …`). Denní report se nikdy nezapíše do složky bez karty s reportem. Na vyžádání: *Nástroje* → „Denní report…“ (výběr složky dne nebo karty, mimo UI vlákno, otevře HTML) a `offload day-report <složka>`. Vypnutí: `outputs.dayReport` (zapnuto), „Denní report po každé kartě“, CLI `--no-day-report`. `copy`/`append` vypíšou „Denní report: …“.

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md` (řádky 20–25)
- Create: `windows/src/Offload.App/MainViewModel.DayReport.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs` (řádky 437–442)
- Modify: `windows/src/Offload.App/MainViewModel.cs` (řádky 24–29)
- Modify: `windows/src/Offload.App/MainWindow.xaml` (řádky 22–27)
- Modify: `windows/src/Offload.App/SetupPanel.xaml` (řádky 199–204)
- Modify: `windows/src/Offload.Cli/CopyCommand.cs` (řádky 160–165)
- Create: `windows/src/Offload.Cli/DayReportCommand.cs`
- Modify: `windows/src/Offload.Cli/Program.cs` (řádky 6–12, 21–26)
- Modify: `windows/src/Offload.Cli/StageTwoOptions.cs` (řádky 21–30, 38–44)
- Modify: `windows/src/Offload.Core/Messages.cs` (řádky 152–157)
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (řádky 30–36, 90–95, 100–105, 215–220, 223–228, 385–400)
- Modify: `windows/src/Offload.Core/Outputs/ClipList.cs` (řádky 9–14)
- Create: `windows/src/Offload.Core/Outputs/DayReportService.cs`
- Create: `windows/tests/Offload.Core.Tests/DayReportServiceTests.cs`
- Modify: `windows/tests/Offload.Core.Tests/SettingsStoreTests.cs` (řádky 60–65)

**Interfaces:**
- Consumes: `DayReport.Write`, `DayFixtures.Day` (úkol 7), `OutputSettings`, `JobConfig.Outputs` (úkol 6), `Shell.Open`, `Dialogs.PickFolder`, `RelayCommand`, `AppLog.Write` (App), `CopyCommand.JobFactory`, `CliPath.Full` (CLI), `AppendFixtures.Options()`.
- Produces: `OutputSettings.DayReport` (`dayReport`, výchozí `true`); `static class DayReportService { DefaultWait; MutexName(string dayPath); DayFolderOf(string picked); Regenerate(string root, string date, OutputSettings, Action<string>? log = null, TimeSpan? wait = null) → string; RegenerateDay(string dayPath, OutputSettings, Action<string>? log = null, TimeSpan? wait = null) → string; [ThreadStatic] internal WhileHeld }`; `Msg.DayNotFound`, `Msg.NotADayFolder`, `Msg.DayReportBusy`, `Msg.DayReportFailed`, `Msg.DayReportWritten`; `OffloadJob.DayReports` (`IReadOnlyList<string>`); CLI příkaz `day-report <folder> [--ale] [--no-clip-list]`, volba `--no-day-report`; GUI `MainViewModel.DayReportCommand`, `DayReportAfterEachCard`, menu „Denní report…“.

- [ ] **Step 1: Padající testy**

Nový soubor `windows/tests/Offload.Core.Tests/DayReportServiceTests.cs`:

```csharp
using Offload.Cli;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Spec 3.2 "Kdy se tvoří": after every job on every destination, on request, one writer at a time per day.
public class DayReportServiceTests
{
    static OffloadJob Job(string card, IReadOnlyList<string> dests, string history, OutputSettings? outputs = null, OffloadOptions? options = null) =>
        new(new JobConfig(card, dests) { Date = "2026-10-07", AnalyzeClips = false, HistoryFolder = history, Outputs = outputs ?? new OutputSettings() },
            options ?? AppendFixtures.Options());

    static string Card(TempDir dir, string name, int seed)
    {
        var card = dir.Sub(name);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", $"{name}C001.mov"), 10_000, seed);
        return card;
    }

    [Fact]
    public void AfterEveryJob_OnEveryDestination_OffWhenSwitchedOff()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var d1 = dir.Sub("d1");
        var d2 = dir.Sub("d2");
        Job(Card(dir, "A001", 1), [d1, d2], history).Run();
        var second = Job(Card(dir, "B001", 2), [d1, d2], history);

        second.Run();

        Assert.Equal(JobPhase.Done, second.Status().Phase);
        Assert.Equal(new[] { d1, d2 }.Select(d => Path.Combine(d, "2026-10-07", "_REPORT", "2026-10-07_day_report.html")), second.DayReports);
        foreach (var html in second.DayReports)
        {
            var text = File.ReadAllText(html);
            Assert.Contains("<td>A001</td>", text);
            Assert.Contains("<td>B001</td>", text);
        }

        var d3 = dir.Sub("d3");
        var off = Job(Card(dir, "C001", 3), [d3], history, new OutputSettings { DayReport = false });
        off.Run();
        Assert.Empty(off.DayReports);
        Assert.False(Directory.Exists(Path.Combine(d3, "2026-10-07", "_REPORT")));
    }

    // Spec 3.2: a cancelled job still makes the day report — its card shows as not complete.
    [Fact]
    public void CancelledJob_StillRegenerates()
    {
        using var dir = new TempDir();
        var card = Card(dir, "A001", 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 10_000, 2);
        var options = AppendFixtures.Options();
        OffloadJob? job = null;
        options.BeforeFileCopy = i => { if (i == 1) job!.Cancel(); };
        job = Job(card, [dir.Sub("d1")], dir.Sub("appdata"), options: options);

        job.Run();

        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);
        var html = File.ReadAllText(Assert.Single(job.DayReports));
        Assert.Contains("<td>A001</td>", html);
        Assert.Contains("1 soubor selhal: CLIP/A001C002.mov", html);
    }

    // Review focus 3: two regenerations of one day — the second waits for the first; one that waits too long gives up
    // with a Czech message (a job only logs it) and nothing is half-written.
    [Fact]
    public async Task TwoWriters_OneAtATime()
    {
        using var dir = new TempDir();
        var day = DayFixtures.Day(dir);
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = new Thread(() =>
        {
            DayReportService.WhileHeld = _ => { inside.Set(); release.Wait(); };
            try { DayReportService.RegenerateDay(day, new OutputSettings()); }
            finally { DayReportService.WhileHeld = null; }
        });
        first.Start();
        Assert.True(inside.Wait(TimeSpan.FromSeconds(30)));

        var busy = Assert.Throws<TimeoutException>(() => DayReportService.RegenerateDay(day, new OutputSettings(), wait: TimeSpan.FromMilliseconds(200)));
        Assert.Equal($"Denní report ve složce {day} právě vytváří jiná úloha — zkuste to za chvíli.", busy.Message);

        var waiting = Task.Run(() => DayReportService.RegenerateDay(day, new OutputSettings(), wait: TimeSpan.FromSeconds(30)));
        Thread.Sleep(200);
        Assert.False(waiting.IsCompleted);   // it waits, it does not write beside the first one
        release.Set();
        first.Join();
        Assert.EndsWith("2026-10-06_day_report.html", await waiting);
        Assert.Empty(Directory.GetFiles(Path.Combine(day, "_REPORT"), "*.tmp"));
        Assert.Empty(Directory.GetFiles(Path.Combine(day, "_REPORT"), "*.new"));
    }

    // Review focus 3: another process (the CLI beside the GUI) holds the day's named mutex.
    [Fact]
    public void OtherProcessHoldsTheDay_Busy()
    {
        using var dir = new TempDir();
        var day = DayFixtures.Day(dir);
        using var taken = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var other = new Thread(() =>
        {
            using var m = new Mutex(true, DayReportService.MutexName(day.ToLowerInvariant() + "\\"));   // another spelling, the same name
            taken.Set();
            release.Wait();
            m.ReleaseMutex();
        });
        other.Start();
        Assert.True(taken.Wait(TimeSpan.FromSeconds(30)));

        Assert.Throws<TimeoutException>(() => DayReportService.RegenerateDay(day, new OutputSettings(), wait: TimeSpan.FromMilliseconds(200)));

        release.Set();
        other.Join();
        Assert.True(File.Exists(DayReportService.RegenerateDay(day, new OutputSettings())));
    }

    // Review focus 3: the Mac (over SMB) or a browser reading the day report with delete sharing does not stop it; a
    // reader without it (Excel, some viewers) makes it fail — the job is still Done and the log says why.
    [Fact]
    public void ReaderOfTheDayReport_SharingDecides_JobNeverFails()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var d1 = dir.Sub("d1");
        var first = Job(Card(dir, "A001", 1), [d1], history);
        first.Run();
        var html = first.DayReports.Single();

        using (new FileStream(html, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var second = Job(Card(dir, "B001", 2), [d1], history);
            second.Run();
            Assert.Equal([html], second.DayReports);
        }
        Assert.Contains("<td>B001</td>", File.ReadAllText(html));

        var log = new List<string>();
        using (new FileStream(html, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var third = Job(Card(dir, "C001", 3), [d1], history);
            third.Run(m => { lock (log) log.Add(m); });
            Assert.Equal(JobPhase.Done, third.Status().Phase);
            Assert.Empty(third.DayReports);
        }
        Assert.Contains(log, m => m.StartsWith($"day report {Path.Combine(d1, "2026-10-07")}: ", StringComparison.Ordinal));
    }

    [Fact]
    public void DayFolderOf_ACardMeansItsDay_NeverOutsideADay()
    {
        using var dir = new TempDir();
        var day = DayFixtures.Day(dir);
        Assert.Equal(day, DayReportService.DayFolderOf(Path.Combine(day, "A001")));
        Assert.Equal(day, DayReportService.DayFolderOf(day + "\\"));
        Assert.Equal(Path.Combine(day, "E001"), DayReportService.DayFolderOf(Path.Combine(day, "E001")));   // no report: not a card

        var root = Path.GetDirectoryName(day)!;
        var notADay = Assert.Throws<InvalidOperationException>(() => DayReportService.RegenerateDay(root, new OutputSettings()));
        Assert.Equal($"Ve složce {root} není žádná karta s reportem — vyberte složku dne na cíli (<cíl>\\<datum>).", notADay.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "_REPORT")));
        Assert.Throws<DirectoryNotFoundException>(() => DayReportService.RegenerateDay(Path.Combine(root, "nope"), new OutputSettings()));
    }
}

/// `offload day-report`, `copy --no-day-report` and the "Denní report: …" line.
[Collection("Console")]
public class CliDayReportTests
{
    static (int Exit, string Out, string Err) Run(System.CommandLine.Command command, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var old = CopyCommand.JobFactory;
        Console.SetOut(so);
        Console.SetError(se);
        CopyCommand.JobFactory = c => new OffloadJob(c, AppendFixtures.Options());
        try { return (command.Parse(args).Invoke(), so.ToString(), se.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = old;
        }
    }

    [Fact]
    public void DayReport_PrintsThePath_Exit0_MissingFolderExit1()
    {
        using var dir = new TempDir();
        var day = DayFixtures.Day(dir);

        var ok = Run(DayReportCommand.Create(), Path.Combine(day, "A001"), "--ale");

        Assert.Equal(0, ok.Exit);
        Assert.Equal($"Denní report: {Path.Combine(day, "_REPORT", "2026-10-06_day_report.html")}", ok.Out.Trim());
        Assert.True(File.Exists(Path.Combine(day, "_REPORT", "2026-10-06_clips.ale")));

        var missing = Path.Combine(dir.Path, "nope");
        var bad = Run(DayReportCommand.Create(), missing);
        Assert.Equal(1, bad.Exit);
        Assert.Contains($"Chyba: Složka dne nenalezena: {missing}", bad.Err);
    }

    [Fact]
    public void Copy_PrintsTheDayReport_NoDayReportLeavesItOut()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var d1 = dir.Sub("d1");
        var d2 = dir.Sub("d2");

        var on = Run(CopyCommand.Create(), card, "-d", d1, "--date", "2026-10-07", "--no-history", "--no-analyze");
        var off = Run(CopyCommand.Create(), card, "-d", d2, "--date", "2026-10-07", "--no-history", "--no-analyze", "--no-day-report");

        Assert.Equal((0, 0), (on.Exit, off.Exit));
        Assert.Contains($"Denní report: {Path.Combine(d1, "2026-10-07", "_REPORT", "2026-10-07_day_report.html")}", on.Out);
        Assert.DoesNotContain("Denní report", off.Out);
        Assert.False(Directory.Exists(Path.Combine(d2, "2026-10-07", "_REPORT")));
    }
}
```

`windows/tests/Offload.Core.Tests/SettingsStoreTests.cs`, řádky 60–65 — najít:

```csharp
        Assert.Contains("\"future\": 1", text);
    }

    [Fact]
    public void Corrupt_Defaults()
    {
```

nahradit:

```csharp
        Assert.Contains("\"future\": 1", text);
    }

    // Spec 5.1: "outputs.dayReport" — on by default, off survives a save.
    [Fact]
    public void Outputs_DayReport_DefaultOn_RoundTrip()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        Assert.True(SettingsStore.Load(path).Outputs.DayReport);
        var s = LoadJson(dir, """{"outputs":{"dayReport":false}}""");
        Assert.False(s.Outputs.DayReport);
        Assert.True(s.Outputs.ClipListCsv);   // the other keys of the section keep their defaults
        SettingsStore.Save(s, path);
        Assert.False(SettingsStore.Load(path).Outputs.DayReport);
    }

    [Fact]
    public void Corrupt_Defaults()
    {
```

- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~DayReportServiceTests|FullyQualifiedName~CliDayReportTests|FullyQualifiedName~SettingsStoreTests"`
Expected: build FAIL — `CS0103: Název 'DayReportService' v aktuálním kontextu neexistuje`, `'DayReportCommand'` …, `'OutputSettings' neobsahuje definici pro 'DayReport'`.

- [ ] **Step 3: Implementace — jádro**

`windows/src/Offload.Core/Outputs/ClipList.cs`, řádky 9–14 — najít:

```csharp
/// "outputs" in settings.json (spec 5.1) and in a job: the files made for production and editing.
public sealed class OutputSettings
{
    /// `_REPORT\<card>_clips.csv` (and the day's) — on by default (R13).
    [JsonPropertyName("clipListCsv")] public bool ClipListCsv { get; set; } = true;
    /// `_REPORT\<card>_clips.ale` for Avid — off by default (R13).
```

nahradit:

```csharp
/// "outputs" in settings.json (spec 5.1) and in a job: the files made for production and editing.
public sealed class OutputSettings
{
    /// The day report after every job (spec 3.2) — on by default.
    [JsonPropertyName("dayReport")] public bool DayReport { get; set; } = true;
    /// `_REPORT\<card>_clips.csv` (and the day's) — on by default (R13).
    [JsonPropertyName("clipListCsv")] public bool ClipListCsv { get; set; } = true;
    /// `_REPORT\<card>_clips.ale` for Avid — off by default (R13).
```

Nový soubor `windows/src/Offload.Core/Outputs/DayReportService.cs`:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Offload.Core.Outputs;

/// Spec 3.2 "Kdy se tvoří": the day report is made again after every job and on request — one writer at a time per day
/// folder: a SemaphoreSlim in this process, a named mutex `Local\Offload-day-<sha1(path)>` between processes (the GUI
/// and the CLI). The files are derived (R25): the last writer wins, also against the Mac writing the same day.
public static class DayReportService
{
    /// How long a regeneration waits for another one of the same day (a PDF takes seconds per destination).
    public static readonly TimeSpan DefaultWait = TimeSpan.FromMinutes(2);

    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    /// Tests only: called while the day's locks are held, before anything is written (this thread only).
    [ThreadStatic] internal static Action<string>? WhileHeld;

    static string Key(string dayPath) => Path.GetFullPath(dayPath).TrimEnd('\\').ToUpperInvariant();

    /// `Local\Offload-day-<sha1 hex of the full path, upper-cased>` — the same for every spelling of the folder's path.
    public static string MutexName(string dayPath) =>
        @"Local\Offload-day-" + Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(Key(dayPath))));

    /// The day folder a pick means: a card folder (it has `_REPORT\offload_report.json`) stands for its day.
    public static string DayFolderOf(string picked)
    {
        var full = Path.GetFullPath(picked).TrimEnd('\\');
        return File.Exists(Path.Combine(full, Layout.ReportDirName, CardRecord.MainReport)) && Path.GetDirectoryName(full) is string day ? day : full;
    }

    /// `<root>\<date>` after a job on that destination.
    public static string Regenerate(string root, string date, OutputSettings outputs, Action<string>? log = null, TimeSpan? wait = null) =>
        RegenerateDay(Path.Combine(root, date), outputs, log, wait);

    /// Writes the day report of `dayPath` and returns its HTML path. Throws (Czech message) when the folder is not there,
    /// when another writer holds the day longer than `wait`, or when the HTML cannot be written; the PDF and the day's
    /// clip lists are best effort (logged).
    public static string RegenerateDay(string dayPath, OutputSettings outputs, Action<string>? log = null, TimeSpan? wait = null)
    {
        if (!Directory.Exists(dayPath)) throw new DirectoryNotFoundException(Msg.DayNotFound(dayPath));
        // Never a `_REPORT` in a destination root or anywhere else: a day has at least one card folder with its report.
        if (!Directory.EnumerateDirectories(dayPath).Any(d => File.Exists(Path.Combine(d, Layout.ReportDirName, CardRecord.MainReport))))
            throw new InvalidOperationException(Msg.NotADayFolder(dayPath));
        var limit = wait ?? DefaultWait;
        var clock = Stopwatch.StartNew();
        var gate = Gates.GetOrAdd(Key(dayPath), _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(limit)) throw new TimeoutException(Msg.DayReportBusy(dayPath));
        try
        {
            Mutex? mutex = null;
            bool owned;
            try
            {
                mutex = new Mutex(false, MutexName(dayPath));
                var left = limit - clock.Elapsed;
                owned = mutex.WaitOne(left > TimeSpan.Zero ? left : TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                owned = true;   // its holder died; the files are swapped whole, so there is nothing half-written to fear
            }
            catch (UnauthorizedAccessException)
            {
                owned = false;  // held by an elevated process: busy
            }
            if (!owned)
            {
                mutex?.Dispose();
                throw new TimeoutException(Msg.DayReportBusy(dayPath));
            }
            try
            {
                WhileHeld?.Invoke(dayPath);
                return DayReport.Write(dayPath, outputs, log);
            }
            finally
            {
                mutex!.ReleaseMutex();
                mutex.Dispose();
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
```

`windows/src/Offload.Core/Messages.cs`, řádky 152–157 — najít:

```csharp
        $"Neznámý kodek proxy „{x}“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ.";
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string UnknownHash(string? x) => $"Neznámý kontrolní součet „{x}“ — použijte xxh64 nebo both (xxh64 + MD5).";
    public static string ProxyFolderNotFound(string path) => $"Složka pro proxy nenalezena: {path}";
    public const string ProxyFolderNoEffect = "Poznámka: --proxy-folder bez --proxy nic nedělá.";
    public const string Cancelling = "Ruším — dokončuji aktuální blok, rozpracované soubory se smažou…";
```

nahradit:

```csharp
        $"Neznámý kodek proxy „{x}“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ.";
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string UnknownHash(string? x) => $"Neznámý kontrolní součet „{x}“ — použijte xxh64 nebo both (xxh64 + MD5).";

    // Day report (spec 3.2)
    public static string DayNotFound(string path) => $"Složka dne nenalezena: {path}";
    public static string NotADayFolder(string path) => $"Ve složce {path} není žádná karta s reportem — vyberte složku dne na cíli (<cíl>\\<datum>).";
    public static string DayReportBusy(string path) => $"Denní report ve složce {path} právě vytváří jiná úloha — zkuste to za chvíli.";
    public static string DayReportFailed(string detail) => $"Denní report se nepodařilo vytvořit: {detail}";
    public static string DayReportWritten(string path) => $"Denní report: {path}";
    public static string ProxyFolderNotFound(string path) => $"Složka pro proxy nenalezena: {path}";
    public const string ProxyFolderNoEffect = "Poznámka: --proxy-folder bez --proxy nic nedělá.";
    public const string Cancelling = "Ruším — dokončuji aktuální blok, rozpracované soubory se smažou…";
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 30–36 — najít:

```csharp
    /// Append (spec 2.2): only the card's new files go into these card folders (`<root>\<date>\<card>` of the earlier
    /// backup); DestRoots, CardName and Date are then taken from them.
    public IReadOnlyList<string>? AppendInto { get; set; }
    /// Clip lists CSV / ALE (spec 3.3; settings `outputs`, CLI --no-clip-list, --ale).
    public OutputSettings Outputs { get; set; } = new();
}

```

nahradit:

```csharp
    /// Append (spec 2.2): only the card's new files go into these card folders (`<root>\<date>\<card>` of the earlier
    /// backup); DestRoots, CardName and Date are then taken from them.
    public IReadOnlyList<string>? AppendInto { get; set; }
    /// Clip lists CSV / ALE and the day report (spec 3.2, 3.3; settings `outputs`, CLI --no-clip-list, --ale,
    /// --no-day-report).
    public OutputSettings Outputs { get; set; } = new();
}

```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 90–95 — najít:

```csharp
    volatile OffloadPlan? planValue;
    volatile OffloadReport? reportValue;
    volatile string? sourceIdentity;
    CardVolume? cardVolume;
    /// Append: what the planner found (also when there is nothing new); read on the copy thread only.
    AppendPlan? appendPlan;
```

nahradit:

```csharp
    volatile OffloadPlan? planValue;
    volatile OffloadReport? reportValue;
    volatile string? sourceIdentity;
    volatile IReadOnlyList<string> dayReports = [];
    CardVolume? cardVolume;
    /// Append: what the planner found (also when there is nothing new); read on the copy thread only.
    AppendPlan? appendPlan;
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 100–105 — najít:

```csharp
    /// The card in the source's drive when the copy started (`CardIdentity`), read on the copy thread; null before
    /// that or when unknown. "Vysunout kartu" ejects only this card.
    public string? SourceIdentity => sourceIdentity;

    /// Tests: what CardIdentity.Of / CardVolume.Of would read from the drive.
    internal Func<string, string?> Identify { get; init; } = CardIdentity.Of;
```

nahradit:

```csharp
    /// The card in the source's drive when the copy started (`CardIdentity`), read on the copy thread; null before
    /// that or when unknown. "Vysunout kartu" ejects only this card.
    public string? SourceIdentity => sourceIdentity;
    /// The day reports (HTML) this job made again when it ended, one per destination that has it (the CLI prints them).
    public IReadOnlyList<string> DayReports => dayReports;

    /// Tests: what CardIdentity.Of / CardVolume.Of would read from the drive.
    internal Func<string, string?> Identify { get; init; } = CardIdentity.Of;
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 215–220 — najít:

```csharp
            Update(s => s with { SafeToEject = report.SafeToEject, CopySucceeded = report.Success, Copy = Engine.Snapshot() });
            if (IsCancelled)
            {
                Update(s => s with { Phase = JobPhase.Cancelled });
                return false;   // a cancelled copy is not recorded in history
            }
```

nahradit:

```csharp
            Update(s => s with { SafeToEject = report.SafeToEject, CopySucceeded = report.Success, Copy = Engine.Snapshot() });
            if (IsCancelled)
            {
                RegenerateDayReports(plan, log);   // the card shows as not complete (spec 3.2)
                Update(s => s with { Phase = JobPhase.Cancelled });
                return false;   // a cancelled copy is not recorded in history
            }
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 223–228 — najít:

```csharp
            {
                WriteClipLists(plan, log);   // the clips found in the files, without metadata (spec 3.3)
                WritePdfReports(report, plan, log);
                Update(s => s with { Phase = JobPhase.Done });
                return false;
            }
```

nahradit:

```csharp
            {
                WriteClipLists(plan, log);   // the clips found in the files, without metadata (spec 3.3)
                WritePdfReports(report, plan, log);
                RegenerateDayReports(plan, log);
                Update(s => s with { Phase = JobPhase.Done });
                return false;
            }
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 385–400 — najít:

```csharp
            Report = final;
            Update(s => s with { Phase = JobPhase.Reporting });
            WritePdfReports(final, plan, log);
            Update(s => s with { Phase = IsCancelled ? JobPhase.Cancelled : JobPhase.Done });
        }
        catch (Exception e)
        {
            log?.Invoke($"stage 2 failed: {e.Message}");
            WritePdfReports(report, plan, log);
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
        }
    }

    OffloadReport AnalyzeAndRender(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        var clips = ClipDetector.Detect(plan.Scan.Files);
```

nahradit:

```csharp
            Report = final;
            Update(s => s with { Phase = JobPhase.Reporting });
            WritePdfReports(final, plan, log);
            RegenerateDayReports(plan, log);
            Update(s => s with { Phase = IsCancelled ? JobPhase.Cancelled : JobPhase.Done });
        }
        catch (Exception e)
        {
            log?.Invoke($"stage 2 failed: {e.Message}");
            WritePdfReports(report, plan, log);
            RegenerateDayReports(plan, log);
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
        }
    }

    /// Spec 3.2: the day report of every destination of this job, made again at its end (also an append, also a
    /// cancelled copy). Waits for another writer of the same day; a failure is only logged — never the job's result.
    void RegenerateDayReports(OffloadPlan plan, Action<string>? log)
    {
        if (!Config.Outputs.DayReport) return;
        var written = new List<string>();
        foreach (var root in plan.DestRoots)
        {
            try { written.Add(DayReportService.Regenerate(root, plan.Date, Config.Outputs, log)); }
            catch (Exception e) { log?.Invoke($"day report {Path.Combine(root, plan.Date)}: {e.Message}"); }
        }
        dayReports = written;
    }

    OffloadReport AnalyzeAndRender(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        var clips = ClipDetector.Detect(plan.Scan.Files);
```

- [ ] **Step 4: Implementace — CLI**

Nový soubor `windows/src/Offload.Cli/DayReportCommand.cs`:

```csharp
using System.CommandLine;
using Offload.Core;
using Offload.Core.Outputs;

namespace Offload.Cli;

/// `offload day-report <day folder>` (spec 3.2): makes the day report again and prints where it is. Exit 0 / 1.
static class DayReportCommand
{
    public static Command Create()
    {
        var folder = new Argument<string>("folder") { Description = "Date folder on a destination (<root>\\<date>), or a card folder in it." };
        var noClipList = new Option<bool>("--no-clip-list") { Description = "No day clip list CSV (_REPORT\\<date>_clips.csv)." };
        var ale = new Option<bool>("--ale") { Description = "Also write the day clip list for Avid (_REPORT\\<date>_clips.ale)." };
        var cmd = new Command("day-report", "Write the day report (HTML, PDF, day clip lists) into <date>\\_REPORT.");
        cmd.Arguments.Add(folder);
        cmd.Options.Add(noClipList);
        cmd.Options.Add(ale);
        cmd.SetAction(r =>
        {
            try
            {
                var day = DayReportService.DayFolderOf(CliPath.Full(r.GetValue(folder)!));
                var outputs = new OutputSettings { ClipListCsv = !r.GetValue(noClipList), ClipListAle = r.GetValue(ale) };
                var html = DayReportService.RegenerateDay(day, outputs, m => Console.Error.WriteLine(m));
                Console.WriteLine(Msg.DayReportWritten(html));
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
        });
        return cmd;
    }
}
```

`windows/src/Offload.Cli/Program.cs`, řádky 6–12 — najít:

```csharp

static class Program
{
    static readonly string[] Commands = ["copy", "append", "verify", "bench", "ports", "luts"];

    static int Main(string[] args)
    {
```

nahradit:

```csharp

static class Program
{
    static readonly string[] Commands = ["copy", "append", "verify", "day-report", "bench", "ports", "luts"];

    static int Main(string[] args)
    {
```

`windows/src/Offload.Cli/Program.cs`, řádky 21–26 — najít:

```csharp
        root.Subcommands.Add(CopyCommand.Create());
        root.Subcommands.Add(AppendCommand.Create());
        root.Subcommands.Add(VerifyCommand.Create());
        root.Subcommands.Add(BenchCommand.Create());
        root.Subcommands.Add(PortsCommand.Create());
        root.Subcommands.Add(LutsCommand.Create());
```

nahradit:

```csharp
        root.Subcommands.Add(CopyCommand.Create());
        root.Subcommands.Add(AppendCommand.Create());
        root.Subcommands.Add(VerifyCommand.Create());
        root.Subcommands.Add(DayReportCommand.Create());
        root.Subcommands.Add(BenchCommand.Create());
        root.Subcommands.Add(PortsCommand.Create());
        root.Subcommands.Add(LutsCommand.Create());
```

`windows/src/Offload.Cli/StageTwoOptions.cs`, řádky 21–30 — najít:

```csharp
    readonly Option<string?> proxyFolder = new("--proxy-folder") { Description = "Also copy every proxy to <folder>\\<date>\\<card name>\\ (with --proxy; never overwrites)." };
    readonly Option<bool> noClipList = new("--no-clip-list") { Description = "No clip list CSV (_REPORT\\<card>_clips.csv)." };
    readonly Option<bool> ale = new("--ale") { Description = "Also write the clip list for Avid (_REPORT\\<card>_clips.ale)." };

    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder, noClipList, ale })
            cmd.Options.Add(o);
    }

```

nahradit:

```csharp
    readonly Option<string?> proxyFolder = new("--proxy-folder") { Description = "Also copy every proxy to <folder>\\<date>\\<card name>\\ (with --proxy; never overwrites)." };
    readonly Option<bool> noClipList = new("--no-clip-list") { Description = "No clip list CSV (_REPORT\\<card>_clips.csv)." };
    readonly Option<bool> ale = new("--ale") { Description = "Also write the clip list for Avid (_REPORT\\<card>_clips.ale)." };
    readonly Option<bool> noDayReport = new("--no-day-report") { Description = "Do not make the day report (<date>\\_REPORT\\<date>_day_report.html) again after the copy." };

    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder, noClipList, ale, noDayReport })
            cmd.Options.Add(o);
    }

```

`windows/src/Offload.Cli/StageTwoOptions.cs`, řádky 38–44 — najít:

```csharp
        var lutPath = lutRaw != null ? CliPath.Full(lutRaw) : null;
        if (lutPath != null && !File.Exists(lutPath)) throw new ArgumentException(Msg.LutNotFound(lutPath));
        config.AnalyzeClips = !r.GetValue(noAnalyze);
        config.Outputs = new OutputSettings { ClipListCsv = !r.GetValue(noClipList), ClipListAle = r.GetValue(ale) };
        config.Proxy = new ProxySettings
        {
            Enabled = r.GetValue(proxy),
```

nahradit:

```csharp
        var lutPath = lutRaw != null ? CliPath.Full(lutRaw) : null;
        if (lutPath != null && !File.Exists(lutPath)) throw new ArgumentException(Msg.LutNotFound(lutPath));
        config.AnalyzeClips = !r.GetValue(noAnalyze);
        config.Outputs = new OutputSettings { ClipListCsv = !r.GetValue(noClipList), ClipListAle = r.GetValue(ale), DayReport = !r.GetValue(noDayReport) };
        config.Proxy = new ProxySettings
        {
            Enabled = r.GetValue(proxy),
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 160–165 — najít:

```csharp
            }
            Console.WriteLine($"Report: {QueueRules.ReportPath(plan.CardPaths[0], plan.CardName, plan.Generation)}");
        }
        if (status.Note != null) Console.WriteLine(status.Note);
        if (status.Warning != null) Console.WriteLine("Varování: " + status.Warning);
        if (status.Error != null) Console.WriteLine(Msg.CliError + status.Error);
```

nahradit:

```csharp
            }
            Console.WriteLine($"Report: {QueueRules.ReportPath(plan.CardPaths[0], plan.CardName, plan.Generation)}");
        }
        foreach (var day in job.DayReports) Console.WriteLine(Msg.DayReportWritten(day));
        if (status.Note != null) Console.WriteLine(status.Note);
        if (status.Warning != null) Console.WriteLine("Varování: " + status.Warning);
        if (status.Error != null) Console.WriteLine(Msg.CliError + status.Error);
```

- [ ] **Step 5: Implementace — GUI**

Nový soubor `windows/src/Offload.App/MainViewModel.DayReport.cs`:

```csharp
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Outputs;

namespace Offload.App;

/// Nástroje → Denní report… (spec 3.2): a day folder on a destination (or a card folder in it) → the day report is made
/// again off the UI thread and its HTML opened.
public sealed partial class MainViewModel
{
    bool makingDayReport;

    public RelayCommand DayReportCommand { get; private set; } = null!;

    void InitDayReport() => DayReportCommand = new RelayCommand(PickAndMakeDayReport, () => !closing && !makingDayReport);

    async void PickAndMakeDayReport()
    {
        if (Dialogs.PickFolder("Vyberte složku dne na cíli") is not string folder) return;
        makingDayReport = true;
        var outputs = SettingsStore.Clone(Settings.Outputs);
        try
        {
            // The folder may be on a slow or network disk, the PDF takes seconds: never on the UI thread.
            var html = await Task.Run(() => DayReportService.RegenerateDay(DayReportService.DayFolderOf(folder), outputs, m => AppLog.Write($"day report: {m}")));
            if (closing) return;
            if (Shell.Open(html) is string error) ShowMessage(error);
        }
        catch (Exception e)
        {
            AppLog.Write($"day report {folder}: {e}");
            if (!closing) ShowMessage(Msg.DayReportFailed(e.Message));
        }
        finally
        {
            makingDayReport = false;
        }
    }
}
```

`windows/src/Offload.App/MainViewModel.cs`, řádky 24–29 — najít:

```csharp
        InitHardware();
        InitHistory();
        InitVerify();
    }

    public StartupOptions Options { get; }
```

nahradit:

```csharp
        InitHardware();
        InitHistory();
        InitVerify();
        InitDayReport();
    }

    public StartupOptions Options { get; }
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 437–442 — najít:

```csharp
        set { Settings.Outputs.ClipListAle = value; SettingsChanged(); Raise(); }
    }

    public bool AnalyzeClips
    {
        get => Settings.AnalyzeClips;
```

nahradit:

```csharp
        set { Settings.Outputs.ClipListAle = value; SettingsChanged(); Raise(); }
    }

    /// "Denní report po každé kartě" (spec 3.2).
    public bool DayReportAfterEachCard
    {
        get => Settings.Outputs.DayReport;
        set { Settings.Outputs.DayReport = value; SettingsChanged(); Raise(); }
    }

    public bool AnalyzeClips
    {
        get => Settings.AnalyzeClips;
```

`windows/src/Offload.App/SetupPanel.xaml`, řádky 199–204 — najít:

```xml
                          AutomationProperties.AutomationId="SuggestCardNames" />
                <CheckBox Content="Seznam klipů CSV" IsChecked="{Binding ClipListCsv}" AutomationProperties.AutomationId="ClipListCsv" />
                <CheckBox Content="Seznam klipů pro Avid (ALE)" IsChecked="{Binding ClipListAle}" AutomationProperties.AutomationId="ClipListAle" />

                <!-- Proxy -->
                <StackPanel Orientation="Horizontal" Margin="0,18,0,0">
```

nahradit:

```xml
                          AutomationProperties.AutomationId="SuggestCardNames" />
                <CheckBox Content="Seznam klipů CSV" IsChecked="{Binding ClipListCsv}" AutomationProperties.AutomationId="ClipListCsv" />
                <CheckBox Content="Seznam klipů pro Avid (ALE)" IsChecked="{Binding ClipListAle}" AutomationProperties.AutomationId="ClipListAle" />
                <CheckBox Content="Denní report po každé kartě" IsChecked="{Binding DayReportAfterEachCard}" AutomationProperties.AutomationId="DayReportAfterEachCard" />

                <!-- Proxy -->
                <StackPanel Orientation="Horizontal" Margin="0,18,0,0">
```

`windows/src/Offload.App/MainWindow.xaml`, řádky 22–27 — najít:

```xml
                    </MenuItem>
                    <MenuItem Header="Ukládat report ověření" IsCheckable="True" IsChecked="{Binding SaveVerifyReport}"
                              AutomationProperties.AutomationId="SaveVerifyReportMenuItem" />
                    <Separator />
                    <MenuItem Header="Prověřit USB porty…" InputGestureText="Ctrl+U" Command="{Binding OpenHardwareCommand}"
                              AutomationProperties.AutomationId="CheckPortsMenuItem">
```

nahradit:

```xml
                    </MenuItem>
                    <MenuItem Header="Ukládat report ověření" IsCheckable="True" IsChecked="{Binding SaveVerifyReport}"
                              AutomationProperties.AutomationId="SaveVerifyReportMenuItem" />
                    <MenuItem Header="Denní report…" Command="{Binding DayReportCommand}"
                              AutomationProperties.AutomationId="DayReportMenuItem">
                        <MenuItem.Icon>
                            <TextBlock Style="{StaticResource Icon}" FontSize="14" Text="&#xE8A5;" />
                        </MenuItem.Icon>
                    </MenuItem>
                    <Separator />
                    <MenuItem Header="Prověřit USB porty…" InputGestureText="Ctrl+U" Command="{Binding OpenHardwareCommand}"
                              AutomationProperties.AutomationId="CheckPortsMenuItem">
```

- [ ] **Step 6: Spustit — projdou**

Run: stejný filtr jako v kroku 2.
Expected: PASS. Pak `dotnet build windows\Offload.sln -warnaserror` (XAML) a celá sada — každá úloha v testech teď na konci vytvoří denní report svého dne v `TempDir` (PDF se v testech přeskočí), sada je o něco pomalejší.

- [ ] **Step 7: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 20–25 — najít:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 7 — denní report `<cíl>\<datum>\_REPORT\<datum>_day_report.html/.pdf` (A4 na šířku, bez náhledů): dlaždice karty, soubory, data, klipy, celková délka, ověřené karty „N / M“; tabulka karet (čas, soubory, data, klipy, délka, kamery, ověření, chyby, proxy hotovo/selhalo, LUTy, počet generací); sekce Problémy (selhané soubory + prvních 5 cest, neověřené, nečitelné reporty, složka bez reportu „bez reportu (probíhá, nebo byl přerušen)“) a LUTy; denní seznam klipů `<datum>_clips.csv/.ale` přes všechny karty; složka `_REPORT` dne se za kartu nepovažuje (ani pro Ověřit zálohu)
- Úkol 6 — seznam klipů karty `_REPORT\<karta>_clips.csv` (UTF-8 s BOM, čárka, CRLF, RFC 4180; sloupce Clip Name … MD5 podle návrhu, hodnoty s `=`/`+`/`-`/`@` beze změny) a `<karta>_clips.ale` pro Avid (tabulátory, jeden soubor na fps, `End` = start + délka, drop-frame počítá jako kamera, bez TC `00:00:00:00`); píše se při každém přepisu reportu a po doplnění pokrývá všechny generace (bez analýzy z nalezených klipů, bez metadat); proxy kamery se neuvádějí; volby `outputs.clipListCsv` (zapnuto) a `outputs.clipListAle` (vypnuto), v GUI „Seznam klipů CSV“ a „Seznam klipů pro Avid (ALE)“, CLI `--no-clip-list`, `--ale`; seznam otevřený v Excelu se jen zaloguje
- Úkol 5 — výstupy MD5: v reportu `hashAlgorithms` (jen když není `["xxh64"]`) a `files[].md5`, `hashAlgorithm` zůstává `xxh64`; `_REPORT\<karta>.md5` (formát md5sum, při doplnění se prodlouží jako `.xxh64`); v ASC MHL `<md5>` vedle `<xxh64>`; TXT `Hash: xxh64+md5`, HTML dlaždice „XXH64 + MD5“ a sloupec md5; nastavení `hash` (`xxh64` | `both`, neznámá hodnota se zachová a čte jako xxh64), v GUI „Kontrolní součet“ + „Ověřit kopie zpětným čtením (…)“ + upozornění na pomalejší MD5; CLI `copy --hash xxh64|both`, `bench --hash md5`; doplnění a historie podle algoritmů dřívější zálohy
````

nahradit:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 8 — denní report se tvoří znovu po každé úloze na každém jejím cíli (celá kopie, doplnění i zrušená úloha; stage 2, jinak stage 1) — jeden zapisovatel na den (`SemaphoreSlim` v procesu + pojmenovaný mutex `Local\Offload-day-<sha1>` mezi GUI a CLI, čekání nejvýš 2 min), selhání jen do logu; nikdy `_REPORT` mimo složku dne (aspoň jedna karta s reportem); na vyžádání Nástroje → „Denní report…“ (složka dne nebo karty, mimo UI vlákno, otevře HTML) a CLI `offload day-report <složka>` (`--ale`, `--no-clip-list`); volba „Denní report po každé kartě“ (`outputs.dayReport`, zapnuto), CLI `--no-day-report`; `copy`/`append` vypíšou „Denní report: …“
- Úkol 7 — denní report `<cíl>\<datum>\_REPORT\<datum>_day_report.html/.pdf` (A4 na šířku, bez náhledů): dlaždice karty, soubory, data, klipy, celková délka, ověřené karty „N / M“; tabulka karet (čas, soubory, data, klipy, délka, kamery, ověření, chyby, proxy hotovo/selhalo, LUTy, počet generací); sekce Problémy (selhané soubory + prvních 5 cest, neověřené, nečitelné reporty, složka bez reportu „bez reportu (probíhá, nebo byl přerušen)“) a LUTy; denní seznam klipů `<datum>_clips.csv/.ale` přes všechny karty; složka `_REPORT` dne se za kartu nepovažuje (ani pro Ověřit zálohu)
- Úkol 6 — seznam klipů karty `_REPORT\<karta>_clips.csv` (UTF-8 s BOM, čárka, CRLF, RFC 4180; sloupce Clip Name … MD5 podle návrhu, hodnoty s `=`/`+`/`-`/`@` beze změny) a `<karta>_clips.ale` pro Avid (tabulátory, jeden soubor na fps, `End` = start + délka, drop-frame počítá jako kamera, bez TC `00:00:00:00`); píše se při každém přepisu reportu a po doplnění pokrývá všechny generace (bez analýzy z nalezených klipů, bez metadat); proxy kamery se neuvádějí; volby `outputs.clipListCsv` (zapnuto) a `outputs.clipListAle` (vypnuto), v GUI „Seznam klipů CSV“ a „Seznam klipů pro Avid (ALE)“, CLI `--no-clip-list`, `--ale`; seznam otevřený v Excelu se jen zaloguje
- Úkol 5 — výstupy MD5: v reportu `hashAlgorithms` (jen když není `["xxh64"]`) a `files[].md5`, `hashAlgorithm` zůstává `xxh64`; `_REPORT\<karta>.md5` (formát md5sum, při doplnění se prodlouží jako `.xxh64`); v ASC MHL `<md5>` vedle `<xxh64>`; TXT `Hash: xxh64+md5`, HTML dlaždice „XXH64 + MD5“ a sloupec md5; nastavení `hash` (`xxh64` | `both`, neznámá hodnota se zachová a čte jako xxh64), v GUI „Kontrolní součet“ + „Ověřit kopie zpětným čtením (…)“ + upozornění na pomalejší MD5; CLI `copy --hash xxh64|both`, `bench --hash md5`; doplnění a historie podle algoritmů dřívější zálohy
````

Zpráva do `.git/COMMIT_MSG_PLAN7`:

```text
Windows: day report after every job (day lock), menu and offload day-report

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.App/MainViewModel.DayReport.cs windows/src/Offload.App/MainViewModel.Setup.cs windows/src/Offload.App/MainViewModel.cs windows/src/Offload.App/MainWindow.xaml windows/src/Offload.App/SetupPanel.xaml windows/src/Offload.Cli/CopyCommand.cs windows/src/Offload.Cli/DayReportCommand.cs windows/src/Offload.Cli/Program.cs windows/src/Offload.Cli/StageTwoOptions.cs windows/src/Offload.Core/Messages.cs windows/src/Offload.Core/OffloadJob.cs windows/src/Offload.Core/Outputs/ClipList.cs windows/src/Offload.Core/Outputs/DayReportService.cs windows/tests/Offload.Core.Tests/DayReportServiceTests.cs windows/tests/Offload.Core.Tests/SettingsStoreTests.cs
git commit -F .git/COMMIT_MSG_PLAN7
```

---

### Task 9: Drobnosti — české chyby souborů v HTML, návod

Spec §2.6 (poslední odstavec: „HTML je může přeložit při zobrazení (mapa v `HtmlReport`, plán 7, úkol drobnosti)“), §6.2 úkol 9. `HtmlReport.ErrorText` přeloží při zobrazení chyby souborů, které JSON drží anglicky (sdílený s Macem); neznámý text (Mac, systém) zůstane. Návod: kapitoly 6, 7, 10, 11 podle návrhu a navíc 1, 3 a 5 (menu a volby; viz „Rozhodnutí“), pak nové PDF návodu.

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md` (řádky 20–25)
- Modify: `windows/package/Offload-navod.pdf` (znovu vytištěný z `navod.html`)
- Modify: `windows/package/navod/navod.html` (řádky 186–198, 234–239, 317–323, 375–384, 520–527, 530–538, 585–591, 607–612, 688–702, 725–731, 925–930, 933–938, 968–973, 991–997, 1007–1012, 1033–1043, 1051–1056)
- Modify: `windows/src/Offload.Core/HtmlReport.cs` (řádky 1–10, 72–77, 135–143)
- Modify: `windows/tests/Offload.Core.Tests/HtmlReportTests.cs` (řádky 27–32)

**Interfaces:**
- Consumes: chyby z `DestWriter` (`size mismatch: …`, `HASH MISMATCH: dest … != source …`, `MD5 MISMATCH: …` z úkolu 4, `verify: …`, `write failed`), z enginu (`source: …`, `cancelled`, `source size changed during copy (…)`, `… (after N retries)`).
- Produces: `HtmlReport.ErrorText(string error) → string` (public; `HtmlReport` je teď `static partial class`).

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/HtmlReportTests.cs`, řádky 27–32 — najít:

```csharp
        Assert.Contains("cíl 1: boom", html);
    }

    [Fact]
    public void NotVerified_IsWarning() =>
        Assert.Contains("ZKOPÍROVÁNO — NEOVĚŘENO", HtmlReport.Render(Reports.Sample(Card, verified: false)));
```

nahradit:

```csharp
        Assert.Contains("cíl 1: boom", html);
    }

    // Spec 2.6/6.2 task 9: the file errors the JSON keeps in English (shared with the Mac) read in Czech in the HTML.
    [Theory]
    [InlineData("size mismatch: 4000 != 5000", "jiná velikost: 4000 != 5000")]
    [InlineData("HASH MISMATCH: dest 0123456789abcdef != source fedcba9876543210", "nesouhlasí kontrolní součet (cíl 0123456789abcdef, karta fedcba9876543210)")]
    [InlineData("MD5 MISMATCH: dest 900150983cd24fb0d6963f7d28e17f72 != source -", "nesouhlasí MD5 (cíl 900150983cd24fb0d6963f7d28e17f72, karta -)")]
    [InlineData("verify: The device is not ready.", "chyba při ověření: The device is not ready.")]
    [InlineData("write failed", "zápis selhal")]
    [InlineData("source: cancelled", "karta: zrušeno")]
    [InlineData("source: source size changed during copy (10 != 20)", "karta: soubor na kartě se během kopie změnil (10 != 20)")]
    [InlineData("source: read E:\\a.mov: The request could not be performed (after 3 retries)", "karta: read E:\\a.mov: The request could not be performed (po 3 opakováních)")]
    [InlineData("disk full (Mac)", "disk full (Mac)")]
    public void ErrorText_Czech(string error, string czech) => Assert.Equal(czech, HtmlReport.ErrorText(error));

    [Fact]
    public void Failed_ErrorsInCzech()
    {
        var html = HtmlReport.Render(Reports.Sample(Card, destError: "HASH MISMATCH: dest 0000000000000001 != source fedcba9876543210"));
        Assert.Contains("cíl 1: nesouhlasí kontrolní součet (cíl 0000000000000001, karta fedcba9876543210)", html);
        Assert.DoesNotContain("HASH MISMATCH", html);
    }

    [Fact]
    public void NotVerified_IsWarning() =>
        Assert.Contains("ZKOPÍROVÁNO — NEOVĚŘENO", HtmlReport.Render(Reports.Sample(Card, verified: false)));
```

- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~HtmlReportTests"`
Expected: build FAIL — `CS0117: 'HtmlReport' neobsahuje definici pro 'ErrorText'`.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/HtmlReport.cs`, řádky 1–10 — najít:

```csharp
using System.Globalization;
using System.Text;

namespace Offload.Core;

/// Self-contained HTML report (thumbnails embedded) — can be emailed or opened straight from the disk.
public static class HtmlReport
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
```

nahradit:

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Offload.Core;

/// Self-contained HTML report (thumbnails embedded) — can be emailed or opened straight from the disk.
public static partial class HtmlReport
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
```

`windows/src/Offload.Core/HtmlReport.cs`, řádky 72–77 — najít:

```csharp
        return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
    }

    static (string Class, string Label) ProxyLabel(ClipEntry c) => c.ProxyStatus switch
    {
        ProxyStatus.Done => ("ok", "hotovo"),
```

nahradit:

```csharp
        return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
    }

    [GeneratedRegex(@"^(?:HASH|MD5) MISMATCH: dest (?<dest>\S+) != source (?<source>\S+)$")]
    private static partial Regex Mismatch();

    [GeneratedRegex(@" \(after (?<n>\d+) retries\)$")]
    private static partial Regex Retries();

    /// A file error as the report JSON keeps it (English, shared with the Mac — spec 2.6) in Czech for the HTML/PDF.
    /// Texts it does not know (the Mac's, the system's) stay as they are.
    public static string ErrorText(string error)
    {
        if (error.StartsWith("source: ", StringComparison.Ordinal)) return "karta: " + ErrorText(error["source: ".Length..]);
        if (error.StartsWith("verify: ", StringComparison.Ordinal)) return "chyba při ověření: " + ErrorText(error["verify: ".Length..]);
        if (Mismatch().Match(error) is { Success: true } m)
            return $"{(error.StartsWith("MD5", StringComparison.Ordinal) ? "nesouhlasí MD5" : "nesouhlasí kontrolní součet")} (cíl {m.Groups["dest"].Value}, karta {m.Groups["source"].Value})";
        if (error.StartsWith("size mismatch: ", StringComparison.Ordinal)) return "jiná velikost: " + error["size mismatch: ".Length..];
        if (error.StartsWith("source size changed during copy ", StringComparison.Ordinal))
            return "soubor na kartě se během kopie změnil " + error["source size changed during copy ".Length..];
        if (error == "write failed") return "zápis selhal";
        if (error == "cancelled") return "zrušeno";
        return Retries().Replace(error, " (po ${n} opakováních)");
    }

    static (string Class, string Label) ProxyLabel(ClipEntry c) => c.ProxyStatus switch
    {
        ProxyStatus.Done => ("ok", "hotovo"),
```

`windows/src/Offload.Core/HtmlReport.cs`, řádky 135–143 — najít:

```csharp
            foreach (var f in failed)
            {
                var msgs = new List<string>();
                if (f.SourceError != null) msgs.Add("karta: " + f.SourceError);
                for (int i = 0; i < f.Errors.Count; i++)
                    if (f.Errors[i] is string e) msgs.Add($"cíl {i + 1}: {e}");
                h.Append($"<tr><td class=\"mono\">{Esc(f.Path)}</td><td class=\"err\">{Esc(string.Join(" · ", msgs))}</td></tr>\n");
            }
            h.Append("</table></div>\n");
```

nahradit:

```csharp
            foreach (var f in failed)
            {
                var msgs = new List<string>();
                if (f.SourceError != null) msgs.Add("karta: " + ErrorText(f.SourceError));
                for (int i = 0; i < f.Errors.Count; i++)
                    if (f.Errors[i] is string e) msgs.Add($"cíl {i + 1}: {ErrorText(e)}");
                h.Append($"<tr><td class=\"mono\">{Esc(f.Path)}</td><td class=\"err\">{Esc(string.Join(" · ", msgs))}</td></tr>\n");
            }
            h.Append("</table></div>\n");
```

- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~HtmlReportTests"`
Expected: PASS. Pak `dotnet build windows\Offload.sln -warnaserror` a celá sada.

- [ ] **Step 5: Návod**

`windows/package/navod/navod.html`, řádky 186–198 — najít:

```html
    <li><span class="n">2</span><span>Instalace a první spuštění<span class="sub">rozbalení ZIPu, SmartScreen, požadavky, volitelné doplňky, kde je nastavení a log</span></span></li>
    <li><span class="n">3</span><span>Hlavní okno<span class="sub">karty, cíle, volby, dekodéry, hardware, lišta se spuštěním, fronta</span></span></li>
    <li><span class="n">4</span><span>Krok za krokem: první offload<span class="sub">od vložení karty po vysunutí</span></span></li>
    <li><span class="n">5</span><span>Volby kopírování a ověření<span class="sub">datum, ověření, analýza klipů, co se kontroluje před kopií</span></span></li>
    <li><span class="n">6</span><span>Proxy, náhledy a LUTy<span class="sub">nastavení proxy, složka pro proxy navíc, automatický výběr LUTu, RAW formáty</span></span></li>
    <li><span class="n">7</span><span>Report<span class="sub">co je v HTML a PDF, tři snímky na klip, řádky LUT, kde report najdete</span></span></li>
    <li><span class="n">8</span><span>Fronta úloh, zrušení, vysunutí karty<span class="sub">stavy úlohy, zrušení, zavření aplikace, vysunutí</span></span></li>
    <li><span class="n">9</span><span>Nástroje → Prověřit USB porty… a okno Hardware<span class="sub">zapojení, doporučení, měření, názvy portů, úzké hrdlo při kopii</span></span></li>
    <li><span class="n">10</span><span>Co se nachází na cílovém disku<span class="sub">struktura složek, _REPORT, ascmhl, _PROXY</span></span></li>
    <li><span class="n">11</span><span>Příkazová řádka<span class="sub">offload copy, verify, bench, ports, luts</span></span></li>
    <li><span class="n">12</span><span>Řešení problémů a časté otázky</span></li>
    <li><span class="n">13</span><span>Licence</span></li>
  </ol>
```

nahradit:

```html
    <li><span class="n">2</span><span>Instalace a první spuštění<span class="sub">rozbalení ZIPu, SmartScreen, požadavky, volitelné doplňky, kde je nastavení a log</span></span></li>
    <li><span class="n">3</span><span>Hlavní okno<span class="sub">karty, cíle, volby, dekodéry, hardware, lišta se spuštěním, fronta</span></span></li>
    <li><span class="n">4</span><span>Krok za krokem: první offload<span class="sub">od vložení karty po vysunutí</span></span></li>
    <li><span class="n">5</span><span>Volby kopírování a ověření<span class="sub">datum, kontrolní součet (MD5), ověření, analýza klipů, seznamy klipů, denní report, co se kontroluje před kopií</span></span></li>
    <li><span class="n">6</span><span>Proxy, náhledy a LUTy<span class="sub">nastavení proxy, přelinkování (reel name, zvukové stopy), složka pro proxy navíc, automatický výběr LUTu, RAW formáty</span></span></li>
    <li><span class="n">7</span><span>Report<span class="sub">co je v HTML a PDF, tři snímky na klip, řádky LUT, seznam klipů CSV / ALE, denní report</span></span></li>
    <li><span class="n">8</span><span>Fronta úloh, zrušení, vysunutí karty<span class="sub">stavy úlohy, zrušení, zavření aplikace, vysunutí</span></span></li>
    <li><span class="n">9</span><span>Nástroje → Prověřit USB porty… a okno Hardware<span class="sub">zapojení, doporučení, měření, názvy portů, úzké hrdlo při kopii</span></span></li>
    <li><span class="n">10</span><span>Co se nachází na cílovém disku<span class="sub">struktura složek, _REPORT karty i dne, ascmhl, _PROXY</span></span></li>
    <li><span class="n">11</span><span>Příkazová řádka<span class="sub">offload copy, append, verify, day-report, bench, ports, luts</span></span></li>
    <li><span class="n">12</span><span>Řešení problémů a časté otázky</span></li>
    <li><span class="n">13</span><span>Licence</span></li>
  </ol>
```

`windows/package/navod/navod.html`, řádky 234–239 — najít:

```html
    LUTu podle kamery (kapitola 6).</li>
  <li>Historii offloadů: řádek „Zálohováno …“ u karty, doplnění jen nových klipů do dřívější zálohy, název složky z názvů
    klipů, ověření hotové zálohy proti ASC MHL a kontrolu místa před startem (kapitoly 3, 8 a 10).</li>
  <li>Kontrolu USB zapojení čtečky a disků s doporučeními a ukazatel úzkého hrdla během kopie (kapitola 9).</li>
  <li>Stejný formát na disku jako Mac verze Offloadu — kopii z Windows lze ověřit na Macu a naopak.</li>
  <li>Příkazovou řádku <code>bin\offload.exe</code> pro skripty (kapitola 11).</li>
```

nahradit:

```html
    LUTu podle kamery (kapitola 6).</li>
  <li>Historii offloadů: řádek „Zálohováno …“ u karty, doplnění jen nových klipů do dřívější zálohy, název složky z názvů
    klipů, ověření hotové zálohy proti ASC MHL a kontrolu místa před startem (kapitoly 3, 8 a 10).</li>
  <li>Výstupy pro produkci a střih: denní report celého dne, seznam klipů CSV (Resolve, Excel) a ALE (Avid), MD5 navíc
    k xxHash64, proxy připravené k přelinkování v Resolve a Premiere (kapitoly 5, 6 a 7).</li>
  <li>Kontrolu USB zapojení čtečky a disků s doporučeními a ukazatel úzkého hrdla během kopie (kapitola 9).</li>
  <li>Stejný formát na disku jako Mac verze Offloadu — kopii z Windows lze ověřit na Macu a naopak.</li>
  <li>Příkazovou řádku <code>bin\offload.exe</code> pro skripty (kapitola 11).</li>
```

`windows/package/navod/navod.html`, řádky 317–323 — najít:

```html
<figure>
<div class="win">
  <div class="bar"><span>Offload</span><span>— ☐ ✕</span></div>
  <div class="menu"><span class="callout">1</span><b>Nástroje</b> &nbsp;›&nbsp; Ověřit zálohu… &nbsp;Ctrl+Shift+O &nbsp;·&nbsp; Prověřit USB porty… &nbsp;Ctrl+U</div>
  <div class="body">
    <div class="left">
      <div class="sec">
```

nahradit:

```html
<figure>
<div class="win">
  <div class="bar"><span>Offload</span><span>— ☐ ✕</span></div>
  <div class="menu"><span class="callout">1</span><b>Nástroje</b> &nbsp;›&nbsp; Ověřit zálohu… &nbsp;Ctrl+Shift+O &nbsp;·&nbsp; Denní report… &nbsp;·&nbsp; Prověřit USB porty… &nbsp;Ctrl+U</div>
  <div class="body">
    <div class="left">
      <div class="sec">
```

`windows/package/navod/navod.html`, řádky 375–384 — najít:

```html
</figure>

<div class="legend">
  <div><span class="callout">1</span><b>Menu Nástroje</b> — <b class="ui">Prověřit USB porty…</b> (<kbd>Ctrl</kbd>+<kbd>U</kbd>), kapitola 9.</div>
  <div><span class="callout">2</span><b>Karty</b> — zdroje ke kopírování.</div>
  <div><span class="callout">3</span><b>Cíle</b> — disky a složky, kam se kopíruje.</div>
  <div><span class="callout">4</span><b>Struktura a kontrola</b> — datum, ověření, analýza klipů.</div>
  <div><span class="callout">5</span><b>Proxy</b> — proxy a LUTy, kapitola 6.</div>
  <div><span class="callout">6</span><b>Dekodéry</b> — nalezené nástroje (rozbalovací sekce).</div>
  <div><span class="callout">7</span><b>Hardware</b> — výsledek posledního testu USB.</div>
```

nahradit:

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
```

`windows/package/navod/navod.html`, řádky 520–527 — najít:

```html
      <td>Složka s datem na cílech je normálně dnešní datum ve tvaru <code>RRRR-MM-DD</code>. Po zaškrtnutí vyberete jiné
      datum v kalendáři (třeba natáčecí den, který skončil po půlnoci). Pod volbou je náhled cesty
      <code>…\2026-10-06\&lt;název karty&gt;\</code>. Vlastní datum platí do zavření aplikace.</td></tr>
  <tr><td><b class="ui">Ověřit kopie zpětným čtením (xxHash64)</b></td><td>zapnuto</td>
      <td>Po kopii se každý soubor z každého cíle přečte znovu a porovná se kontrolní součet. Bez ověření se objeví
      oranžové <span class="txt-warn">„Bez ověření není jisté, že data na disku jsou v pořádku.“</span>, report má štítek
      <b>ZKOPÍROVÁNO — NEOVĚŘENO</b> a zelený pruh „kartu lze vysunout“ se <b>neukáže</b>.</td></tr>
  <tr><td><b class="ui">Navrhovat název karty podle klipů</b></td><td>zapnuto</td>
```

nahradit:

```html
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
```

`windows/package/navod/navod.html`, řádky 530–538 — najít:

```html
  <tr><td><b class="ui">Analýza klipů a náhledy v reportu</b></td><td>zapnuto</td>
      <td>Po kopii se u každého klipu zjistí formát, kodek, rozlišení, snímková frekvence, délka, timecode a zvukové stopy
      a do reportu se vloží tři snímky. Čte se z cílových disků, ne z karty. Potřebuje ffmpeg a ffprobe (jsou přibalené).</td></tr>
</table>

<p class="note"><b>Nevracejte se ke starší verzi než 0.2.0</b> — verze 0.1.0 by z nastavení smazala údaje, kterým nerozumí.</p>

<h3>Jak kopie probíhá</h3>
<ul>
```

nahradit:

```html
  <tr><td><b class="ui">Analýza klipů a náhledy v reportu</b></td><td>zapnuto</td>
      <td>Po kopii se u každého klipu zjistí formát, kodek, rozlišení, snímková frekvence, délka, timecode a zvukové stopy
      a do reportu se vloží tři snímky. Čte se z cílových disků, ne z karty. Potřebuje ffmpeg a ffprobe (jsou přibalené).</td></tr>
  <tr><td><b class="ui">Seznam klipů CSV</b></td><td>zapnuto</td>
      <td><code>_REPORT\&lt;karta&gt;_clips.csv</code> pro Resolve, Excel a produkci (kapitola 7).</td></tr>
  <tr><td><b class="ui">Seznam klipů pro Avid (ALE)</b></td><td>vypnuto</td>
      <td><code>_REPORT\&lt;karta&gt;_clips.ale</code> pro import do Avid Media Composeru (kapitola 7).</td></tr>
  <tr><td><b class="ui">Denní report po každé kartě</b></td><td>zapnuto</td>
      <td>Po každé úloze se na každém cíli znovu vytvoří přehled celého natáčecího dne
      <code>&lt;datum&gt;\_REPORT\&lt;datum&gt;_day_report.html</code> (kapitola 7).</td></tr>
</table>

<p class="note"><b>Nevracejte se ke starší verzi než 0.2.0</b> — verze 0.1.0 by z nastavení smazala údaje, kterým nerozumí.
Verze 0.2.0 nové volby 0.3.0 (kontrolní součet, seznamy klipů, denní report) nezná, ale v nastavení je zachová.</p>

<h3>Jak kopie probíhá</h3>
<ul>
```

`windows/package/navod/navod.html`, řádky 585–591 — najít:

```html
  <tr><td><b class="ui">LUT</b></td><td>Automaticky podle kamery</td><td>Viz níže, „LUT pro proxy a náhledy“.</td></tr>
  <tr><td><b class="ui">Burn-in názvu klipu</b></td><td>zapnuto</td><td>Název klipu vypálený do obrazu proxy.</td></tr>
  <tr><td><b class="ui">Burn-in timecodu</b></td><td>zapnuto</td><td>Timecode vypálený do obrazu proxy.</td></tr>
  <tr><td><b class="ui">Zvuk</b></td><td>zapnuto</td><td>Proxy obsahují zvuk klipu.</td></tr>
  <tr><td><b class="ui">Přeskočit proxy nahrané kamerou (SUB/PROXY)</b></td><td>zapnuto</td><td>Malé soubory, které kamera nahrává sama
      (ve složkách <code>SUB</code> nebo <code>PROXY</code>), se zkopírují a ověří, ale proxy se z nich znovu nedělá. V reportu jsou
      označené <span class="q">„(proxy kamery)“</span> a mají stav <span class="q">„přeskočeno“</span>.</td></tr>
```

nahradit:

```html
  <tr><td><b class="ui">LUT</b></td><td>Automaticky podle kamery</td><td>Viz níže, „LUT pro proxy a náhledy“.</td></tr>
  <tr><td><b class="ui">Burn-in názvu klipu</b></td><td>zapnuto</td><td>Název klipu vypálený do obrazu proxy.</td></tr>
  <tr><td><b class="ui">Burn-in timecodu</b></td><td>zapnuto</td><td>Timecode vypálený do obrazu proxy.</td></tr>
  <tr><td><b class="ui">Zvuk</b></td><td>zapnuto</td><td>Proxy obsahují <b>všechny</b> zvukové stopy klipu (např. 8 mono stop z Sony MXF) —
      Premiere připojí proxy jen se stejným počtem stop. H.264/H.265: AAC 64 kb/s na kanál každé stopy (nejméně 128,
      nejvýš 512 kb/s); ProRes: nekomprimovaný zvuk (PCM).</td></tr>
  <tr><td><b class="ui">Přeskočit proxy nahrané kamerou (SUB/PROXY)</b></td><td>zapnuto</td><td>Malé soubory, které kamera nahrává sama
      (ve složkách <code>SUB</code> nebo <code>PROXY</code>), se zkopírují a ověří, ale proxy se z nich znovu nedělá. V reportu jsou
      označené <span class="q">„(proxy kamery)“</span> a mají stav <span class="q">„přeskočeno“</span>.</td></tr>
```

`windows/package/navod/navod.html`, řádky 607–612 — najít:

```html
    <span class="q">„kopie do … selhala: …“</span>.</li>
</ul>

<h3>Kódování proxy</h3>
<p>H.264 a H.265 kóduje Offload na grafické kartě, pokud ji najde (zkouší NVIDIA, Intel a AMD v tomto pořadí; ověřeno
je s NVIDIA), jinak procesorem. ProRes se kóduje vždy procesorem. Použitý kodér je uvedený v reportu, např.
```

nahradit:

```html
    <span class="q">„kopie do … selhala: …“</span>.</li>
</ul>

<h3>Proxy pro přelinkování (Resolve, Premiere)</h3>
<p>Proxy se dají připojit k originálům ve střižně: mají stejný základ jména jako klip (<code>A001C003.mov</code>), stejný
počáteční timecode, stejnou snímkovou frekvenci, všechny zvukové stopy a <b>reel name</b> — cívku, kterou Premiere
ukazuje jako <i>Tape Name</i> a Resolve jako <i>Reel</i>. Cívka je v pořadí: údaj kamery v klipu (<code>reel_name</code>,
<code>tape_name</code>), jinak cívka z názvu klipu (<code>A001C003…</code> → <code>A001</code>), jinak název složky karty.
Je i v reportu a v seznamech klipů. Klip bez timecodu dostane proxy bez timecodu i bez cívky (Resolve ho přelinkuje
podle jména).</p>
<p>Když už proxy stejného jména existuje (dva klipy stejného jména, nebo doplnění), nová dostane <code>_2</code> a u klipu je
poznámka <span class="q">„Proxy přejmenována na A001C003_2 (shoda jmen) — přelinkujte ji ručně.“</span></p>

<h3>Kódování proxy</h3>
<p>H.264 a H.265 kóduje Offload na grafické kartě, pokud ji najde (zkouší NVIDIA, Intel a AMD v tomto pořadí; ověřeno
je s NVIDIA), jinak procesorem. ProRes se kóduje vždy procesorem. Použitý kodér je uvedený v reportu, např.
```

`windows/package/navod/navod.html`, řádky 688–702 — najít:

```html
<ul>
  <li><b>Hlavička</b>: název karty, štítek stavu <span class="pill ok">OVĚŘENO</span>, <span class="pill warn">ZKOPÍROVÁNO — NEOVĚŘENO</span>
    nebo <span class="pill bad">CHYBA</span>, datum a čas začátku a konce.</li>
  <li><b>Souhrn</b>: počet souborů, objem dat, počet klipů a jejich celková délka, doba a rychlost kopírování a ověření, kontrolní součet (XXH64).</li>
  <li><b>Cíle</b>: tabulka disků — disk, složka, souborový systém, počet souborů OK a chyb. Pod ní zdroj a jeho souborový systém.</li>
  <li><b>Chyby</b> (jen když nějaké jsou): soubor a problém, zvlášť pro kartu a pro každý cíl.</li>
  <li><b>Klipy</b>: u každého klipu tři snímky, název (u proxy z kamery <span class="q">„(proxy kamery)“</span>), cesta na kartě,
    formát a kodek, rozlišení, FPS, délka, timecode, počet zvukových kanálů, velikost a stav proxy
    (<span class="txt-ok">hotovo</span>, <span class="txt-bad">chyba</span>, <span class="txt-warn">nepodporováno</span>, přeskočeno).
    Pod tabulkou řádek s nastavením proxy, např. <span class="q">„Proxy: H.264 (h264_nvenc), 1080p, výřez 1.85, LUT: automaticky
    podle kamery → _PROXY/ · také ve složce G:\Proxy“</span>.</li>
  <li><b>Všechny soubory</b>: každý soubor s velikostí, kontrolním součtem a stavem na každém cíli
    (<span class="txt-ok">ověřeno</span>, zapsáno, <span class="txt-bad">CHYBA</span>, nezpracováno). V HTML je seznam sbalený, v PDF rozbalený.</li>
  <li>Seznam vynechaných systémových souborů a v patičce verze aplikace.</li>
</ul>
```

nahradit:

```html
<ul>
  <li><b>Hlavička</b>: název karty, štítek stavu <span class="pill ok">OVĚŘENO</span>, <span class="pill warn">ZKOPÍROVÁNO — NEOVĚŘENO</span>
    nebo <span class="pill bad">CHYBA</span>, datum a čas začátku a konce.</li>
  <li><b>Souhrn</b>: počet souborů, objem dat, počet klipů a jejich celková délka, doba a rychlost kopírování a ověření, kontrolní součet (XXH64, nebo XXH64 + MD5).</li>
  <li><b>Cíle</b>: tabulka disků — disk, složka, souborový systém, počet souborů OK a chyb. Pod ní zdroj a jeho souborový systém.</li>
  <li><b>Chyby</b> (jen když nějaké jsou): soubor a problém česky, zvlášť pro kartu a pro každý cíl — např.
    <span class="q">„cíl 2: nesouhlasí kontrolní součet (cíl …, karta …)“</span>, <span class="q">„jiná velikost: …“</span>,
    <span class="q">„zápis selhal“</span>. V <code>offload_report.json</code> zůstávají anglicky (sdílí ho Mac verze).</li>
  <li><b>Klipy</b>: u každého klipu tři snímky, název (u proxy z kamery <span class="q">„(proxy kamery)“</span>), cesta na kartě,
    formát a kodek, rozlišení, FPS, délka, timecode, počet zvukových kanálů, velikost a stav proxy
    (<span class="txt-ok">hotovo</span>, <span class="txt-bad">chyba</span>, <span class="txt-warn">nepodporováno</span>, přeskočeno).
    Pod tabulkou řádek s nastavením proxy, např. <span class="q">„Proxy: H.264 (h264_nvenc), 1080p, výřez 1.85, LUT: automaticky
    podle kamery → _PROXY/ · také ve složce G:\Proxy“</span>.</li>
  <li><b>Všechny soubory</b>: každý soubor s velikostí, kontrolním součtem (s MD5 i sloupec md5) a stavem na každém cíli
    (<span class="txt-ok">ověřeno</span>, zapsáno, <span class="txt-bad">CHYBA</span>, nezpracováno). V HTML je seznam sbalený, v PDF rozbalený.</li>
  <li>Seznam vynechaných systémových souborů a v patičce verze aplikace.</li>
</ul>
```

`windows/package/navod/navod.html`, řádky 725–731 — najít:

```html
  <tr><td><code>offload_report.json</code></td><td>úplný strojově čitelný report (pro další nástroje)</td></tr>
  <tr><td><code>offload_report.txt</code></td><td>krátký textový souhrn</td></tr>
  <tr><td><code>&lt;karta&gt;.xxh64</code></td><td>seznam kontrolních součtů xxHash64 všech souborů — z něj ověřuje <code>offload verify</code></td></tr>
</table>
<p>Ve složce <code>ascmhl</code> vedle je ASC MHL (verze 2) se součty všech souborů — lze ji ověřit nástroji třetích stran
(<code>ascmhl</code>, Silverstack, ShotPut). Snímky z reportu jsou jen v HTML a PDF, ne v JSON.</p>
</section>
```

nahradit:

```html
  <tr><td><code>offload_report.json</code></td><td>úplný strojově čitelný report (pro další nástroje)</td></tr>
  <tr><td><code>offload_report.txt</code></td><td>krátký textový souhrn</td></tr>
  <tr><td><code>&lt;karta&gt;.xxh64</code></td><td>seznam kontrolních součtů xxHash64 všech souborů — z něj ověřuje <code>offload verify</code></td></tr>
  <tr><td><code>&lt;karta&gt;.md5</code></td><td>jen s volbou <b class="ui">xxHash64 + MD5</b>: součty MD5 ve formátu <code>md5sum</code></td></tr>
  <tr><td><code>&lt;karta&gt;_clips.csv</code></td><td>seznam klipů (viz níže)</td></tr>
  <tr><td><code>&lt;karta&gt;_clips.ale</code></td><td>seznam klipů pro Avid, jen se zapnutou volbou (viz níže)</td></tr>
</table>

<h3>Seznam klipů (CSV a Avid ALE)</h3>
<p>Při každém zápisu reportu vznikne <code>&lt;karta&gt;_clips.csv</code>: jeden řádek na klip (proxy nahrané kamerou se
neuvádějí), sloupce <code>Clip Name, File Name, Path, Card, Camera, Reel, Start TC, Duration, FPS, Resolution, Codec,
LUT, Size (bytes), XXH64, MD5</code> (anglicky, jak je čte Resolve). Kódování UTF-8, oddělovač čárka. Do Resolve se
načte přes <i>File → Import Metadata To → Media Pool</i>. <b>Excel</b> s českým nastavením čeká středník: soubor
otevřete přes <i>Data → Z textu/CSV</i> a zvolte oddělovač <i>Čárka</i>. Hodnoty začínající <code>=</code>,
<code>+</code>, <code>-</code> nebo <code>@</code> se neupravují (úprava by rozbila import do Resolve) — Excel je může
brát jako vzorec.</p>
<p>Se zapnutou volbou <b class="ui">Seznam klipů pro Avid (ALE)</b> vznikne i <code>&lt;karta&gt;_clips.ale</code>
(<i>File → Input → Import</i> v Media Composeru). ALE má jednu snímkovou frekvenci, takže karta se smíšenými fps dostane
soubor na každou: <code>A001_clips_25.ale</code>, <code>A001_clips_23.976.ale</code>. Bez analýzy klipů mají řádky jen
název, cestu, cívku, velikost a součty.</p>
<p>Po doplnění seznamy pokrývají všechny generace karty. Jsou to odvozené soubory: přepisují se celé; seznam otevřený
v Excelu se nepřepíše (zapíše se jen do logu) a úloha tím neselže.</p>

<h3>Denní report</h3>
<p>Po každé úloze (i doplnění a zrušené) se na každém cíli vytvoří znovu přehled celého dne
<code>&lt;cíl&gt;\&lt;datum&gt;\_REPORT\&lt;datum&gt;_day_report.html</code> a <code>.pdf</code>: dlaždice (karty, soubory,
data, klipy, celková délka, ověřené karty <span class="q">„7 / 8“</span>), tabulka karet (čas, soubory, data, klipy, délka,
kamery, ověření, chyby, proxy hotovo / selhalo, LUTy, počet generací po doplnění), sekce <b>Problémy</b> (selhané soubory,
neověřené karty, nečitelné reporty, složka karty <span class="q">„bez reportu (probíhá, nebo byl přerušen)“</span>)
a <b>LUTy</b>. Vedle je seznam klipů celého dne <code>&lt;datum&gt;_clips.csv</code> (a <code>.ale</code>).
Vypíná se volbou <b class="ui">Denní report po každé kartě</b>. Ručně: <b class="ui">Nástroje → Denní report…</b>
a vyberte složku dne na cíli (nebo složku karty v ní) — report se vytvoří a otevře. Dvě úlohy, které skončí zároveň,
se vystřídají; každý cíl má report podle toho, co je na něm.</p>
<p>Ve složce <code>ascmhl</code> vedle je ASC MHL (verze 2) se součty všech souborů — lze ji ověřit nástroji třetích stran
(<code>ascmhl</code>, Silverstack, ShotPut). Snímky z reportu jsou jen v HTML a PDF, ne v JSON.</p>
</section>
```

`windows/package/navod/navod.html`, řádky 925–930 — najít:

```html

<pre class="tree">D:\Zaloha\                                  <span class="c">← cíl</span>
└─ 2026-10-06\                              <span class="c">← datum (nebo Vlastní datum)</span>
   └─ A001\                                 <span class="c">← karta: jmenovka svazku nebo Název složky</span>
      ├─ …soubory karty ve stejné struktuře…  <span class="c">← např. PRIVATE\M4ROOT\CLIP\…</span>
      ├─ _REPORT\
```

nahradit:

```html

<pre class="tree">D:\Zaloha\                                  <span class="c">← cíl</span>
└─ 2026-10-06\                              <span class="c">← datum (nebo Vlastní datum)</span>
   ├─ _REPORT\                              <span class="c">← denní report (celý den na tomto cíli)</span>
   │  ├─ 2026-10-06_day_report.html
   │  ├─ 2026-10-06_day_report.pdf
   │  └─ 2026-10-06_clips.csv               <span class="c">← seznam klipů dne (+ .ale)</span>
   └─ A001\                                 <span class="c">← karta: jmenovka svazku nebo Název složky</span>
      ├─ …soubory karty ve stejné struktuře…  <span class="c">← např. PRIVATE\M4ROOT\CLIP\…</span>
      ├─ _REPORT\
```

`windows/package/navod/navod.html`, řádky 933–938 — najít:

```html
      │  ├─ A001_report.html                 <span class="c">← tlačítko Report</span>
      │  ├─ A001_report.pdf
      │  ├─ A001.xxh64                       <span class="c">← kontrolní součty (po doplnění i nové soubory)</span>
      │  ├─ offload_report_0002.json         <span class="c">← jen po doplnění: report generace 2</span>
      │  ├─ A001_report_0002.html
      │  └─ A001_verify_2026-10-07_101500Z.html  <span class="c">← jen po Ověřit zálohu (+ .pdf, offload_verify_….json)</span>
```

nahradit:

```html
      │  ├─ A001_report.html                 <span class="c">← tlačítko Report</span>
      │  ├─ A001_report.pdf
      │  ├─ A001.xxh64                       <span class="c">← kontrolní součty (po doplnění i nové soubory)</span>
      │  ├─ A001.md5                         <span class="c">← jen s volbou xxHash64 + MD5</span>
      │  ├─ A001_clips.csv                   <span class="c">← seznam klipů (+ A001_clips.ale pro Avid)</span>
      │  ├─ offload_report_0002.json         <span class="c">← jen po doplnění: report generace 2</span>
      │  ├─ A001_report_0002.html
      │  └─ A001_verify_2026-10-07_101500Z.html  <span class="c">← jen po Ověřit zálohu (+ .pdf, offload_verify_….json)</span>
```

`windows/package/navod/navod.html`, řádky 968–973 — najít:

```html
    a MHL nesouhlasí, další doplnění se odmítne a složku je třeba opravit ručně; celá kopie do nové složky jde vždy.</li>
  <li>Report ověření jde jen do <code>_REPORT</code> (u cizího MHL bez ignorování <code>_REPORT</code> do
    <code>_REPORT</code> vedle složky karty), do <code>ascmhl</code> se nikdy nezapisuje.</li>
</ul>

<div class="box tip"><span class="t">Ověření kopie později</span>
```

nahradit:

```html
    a MHL nesouhlasí, další doplnění se odmítne a složku je třeba opravit ručně; celá kopie do nové složky jde vždy.</li>
  <li>Report ověření jde jen do <code>_REPORT</code> (u cizího MHL bez ignorování <code>_REPORT</code> do
    <code>_REPORT</code> vedle složky karty), do <code>ascmhl</code> se nikdy nezapisuje.</li>
  <li><code>_REPORT</code> ve složce dne patří dennímu reportu (a reportům ověření cizích záloh); karta se tak jmenovat
    nemůže. Denní report a seznamy klipů jsou odvozené z reportů karet — přepisují se celé a lze je kdykoli vytvořit
    znovu (<b class="ui">Nástroje → Denní report…</b>).</li>
</ul>

<div class="box tip"><span class="t">Ověření kopie později</span>
```

`windows/package/navod/navod.html`, řádky 991–997 — najít:

```html
  <tr><td><code>copy</code></td><td>zkopíruje kartu na všechny cíle do <code>&lt;cíl&gt;\&lt;datum&gt;\&lt;karta&gt;\</code> a ověří. Je výchozí — slovo <code>copy</code> lze vynechat.</td></tr>
  <tr><td><code>append</code></td><td>doplní jen nové soubory karty do dřívější zálohy (stejné složky karty, další generace ASC MHL)</td></tr>
  <tr><td><code>verify</code></td><td>znovu ověří složku karty (nebo všechny karty složky dne) proti ASC MHL všech generací, bez <code>ascmhl</code> proti seznamu součtů v <code>_REPORT</code>; <code>--report</code> uloží report ověření</td></tr>
  <tr><td><code>bench</code></td><td>změří rychlost čtení karty a zápisu/čtení disků</td></tr>
  <tr><td><code>ports</code></td><td>USB porty, kde a jak rychle jsou karty a disky připojené, doporučení (jako okno Hardware)</td></tr>
  <tr><td><code>luts</code></td><td>vypíše vestavěné LUTy (id — název)</td></tr>
</table>
```

nahradit:

```html
  <tr><td><code>copy</code></td><td>zkopíruje kartu na všechny cíle do <code>&lt;cíl&gt;\&lt;datum&gt;\&lt;karta&gt;\</code> a ověří. Je výchozí — slovo <code>copy</code> lze vynechat.</td></tr>
  <tr><td><code>append</code></td><td>doplní jen nové soubory karty do dřívější zálohy (stejné složky karty, další generace ASC MHL)</td></tr>
  <tr><td><code>verify</code></td><td>znovu ověří složku karty (nebo všechny karty složky dne) proti ASC MHL všech generací, bez <code>ascmhl</code> proti seznamu součtů v <code>_REPORT</code>; <code>--report</code> uloží report ověření</td></tr>
  <tr><td><code>day-report</code></td><td>vytvoří znovu denní report složky dne (nebo dne, ve kterém je zadaná složka karty) a vypíše cestu; <code>--ale</code>, <code>--no-clip-list</code> jako u <code>copy</code></td></tr>
  <tr><td><code>bench</code></td><td>změří rychlost čtení karty a zápisu/čtení disků; <code>--hash md5</code> navíc rychlost MD5</td></tr>
  <tr><td><code>ports</code></td><td>USB porty, kde a jak rychle jsou karty a disky připojené, doporučení (jako okno Hardware)</td></tr>
  <tr><td><code>luts</code></td><td>vypíše vestavěné LUTy (id — název)</td></tr>
</table>
```

`windows/package/navod/navod.html`, řádky 1007–1012 — najít:

```html
offload append E:\ --into D:\Zaloha\2026-10-06\A001    # … nebo do zadané složky karty (opakovat pro každý cíl)
offload verify D:\Zaloha\2026-10-06\A001              # znovu ověřit hotovou kopii
offload verify D:\Zaloha\2026-10-06 --report           # ověřit všechny karty dne a uložit reporty ověření
offload bench --read E:\ --write D:\ --gb 2           # rychlost karty a disku
offload ports                                         # porty a doporučení
offload ports --measure --card E:\ --dest D:\         # + změřit skutečnou rychlost
```

nahradit:

```html
offload append E:\ --into D:\Zaloha\2026-10-06\A001    # … nebo do zadané složky karty (opakovat pro každý cíl)
offload verify D:\Zaloha\2026-10-06\A001              # znovu ověřit hotovou kopii
offload verify D:\Zaloha\2026-10-06 --report           # ověřit všechny karty dne a uložit reporty ověření
offload E:\ -d D:\Zaloha --hash both --ale            # xxHash64 + MD5, seznam klipů i pro Avid
offload day-report D:\Zaloha\2026-10-06               # denní report znovu (např. po ruční úpravě)
offload bench --read E:\ --write D:\ --gb 2           # rychlost karty a disku
offload ports                                         # porty a doporučení
offload ports --measure --card E:\ --dest D:\         # + změřit skutečnou rychlost
```

`windows/package/navod/navod.html`, řádky 1033–1043 — najít:

```html
  <tr><td class="k"><code>--lut</code></td><td><code>auto</code> (výchozí, podle kamery), <code>none</code>, id vestavěného LUTu, nebo cesta k <code>.cube</code></td></tr>
  <tr><td class="k"><code>--lut-fallback</code></td><td>id vestavěného LUTu (nebo <code>none</code>) pro klipy, které <code>auto</code> nepozná</td></tr>
  <tr><td class="k"><code>--proxy-folder</code></td><td>každou proxy zkopírovat i do <code>&lt;složka&gt;\&lt;datum&gt;\&lt;karta&gt;\</code> (jen s <code>--proxy</code>)</td></tr>
</table>

<h3>Volby příkazu append</h3>
<p><code>offload append &lt;karta&gt;</code> má stejné volby analýzy a proxy jako <code>copy</code> (<code>--no-analyze</code>,
<code>--proxy</code>, <code>--lut</code> …) a navíc <code>--into &lt;složka karty&gt;</code> (zopakujte pro každý cíl dřívější
zálohy; bez ní se složky najdou v historii, jinak <span class="q">„Chyba: Karta není v historii — zadejte složky zálohy
volbou --into.“</span>), <code>--no-verify</code>, <code>--dry-run</code> (vypíše plán, který se u skutečného běhu ještě
upřesní) a <code>--no-history</code> (potom je <code>--into</code> povinné). Kód <code>0</code> = doplněno (nebo
```

nahradit:

```html
  <tr><td class="k"><code>--lut</code></td><td><code>auto</code> (výchozí, podle kamery), <code>none</code>, id vestavěného LUTu, nebo cesta k <code>.cube</code></td></tr>
  <tr><td class="k"><code>--lut-fallback</code></td><td>id vestavěného LUTu (nebo <code>none</code>) pro klipy, které <code>auto</code> nepozná</td></tr>
  <tr><td class="k"><code>--proxy-folder</code></td><td>každou proxy zkopírovat i do <code>&lt;složka&gt;\&lt;datum&gt;\&lt;karta&gt;\</code> (jen s <code>--proxy</code>)</td></tr>
  <tr><td class="k"><code>--hash</code></td><td><code>xxh64</code> (výchozí) nebo <code>both</code> = xxHash64 + MD5</td></tr>
  <tr><td class="k"><code>--no-clip-list</code></td><td>bez seznamu klipů CSV</td></tr>
  <tr><td class="k"><code>--ale</code></td><td>navíc seznam klipů pro Avid (ALE)</td></tr>
  <tr><td class="k"><code>--no-day-report</code></td><td>po kopii nevytvářet denní report</td></tr>
</table>

<h3>Volby příkazu append</h3>
<p><code>offload append &lt;karta&gt;</code> má stejné volby analýzy, proxy a výstupů jako <code>copy</code> (<code>--no-analyze</code>,
<code>--proxy</code>, <code>--lut</code>, <code>--ale</code>, <code>--no-day-report</code> …; kontrolní součty určuje dřívější
záloha, <code>--hash</code> proto nemá) a navíc <code>--into &lt;složka karty&gt;</code> (zopakujte pro každý cíl dřívější
zálohy; bez ní se složky najdou v historii, jinak <span class="q">„Chyba: Karta není v historii — zadejte složky zálohy
volbou --into.“</span>), <code>--no-verify</code>, <code>--dry-run</code> (vypíše plán, který se u skutečného běhu ještě
upřesní) a <code>--no-history</code> (potom je <code>--into</code> povinné). Kód <code>0</code> = doplněno (nebo
```

`windows/package/navod/navod.html`, řádky 1051–1056 — najít:

```html
  <li>Návratový kód: <code>0</code> = vše v pořádku, <code>1</code> = chyba (vhodné pro skripty).</li>
  <li><code>copy</code> u karty, kterou historie zná, vypíše před plánem <code>Zálohováno 6. 10. 2026 8:15 na D:\Zaloha,
    F:\Zaloha — ověřeno</code> (bez dotazu).</li>
  <li><code>verify</code> vypíše u problémových souborů <code>MISMATCH</code> (jiný součet), <code>MISSING</code> (chybí),
    <code>ERROR</code> (nečitelný), <code>EXTRA</code> (není v záznamu), <code>FAILED-AT-COPY</code> (selhal už při kopii),
    <code>FAILED-SUPERSEDED</code> (nejnovější záznam selhal, soubor sedí podle starší generace), <code>CHAIN</code>
```

nahradit:

```html
  <li>Návratový kód: <code>0</code> = vše v pořádku, <code>1</code> = chyba (vhodné pro skripty).</li>
  <li><code>copy</code> u karty, kterou historie zná, vypíše před plánem <code>Zálohováno 6. 10. 2026 8:15 na D:\Zaloha,
    F:\Zaloha — ověřeno</code> (bez dotazu).</li>
  <li>Na konci <code>copy</code> a <code>append</code> vypíšou <code>Denní report: D:\Zaloha\2026-10-06\_REPORT\2026-10-06_day_report.html</code>
    pro každý cíl.</li>
  <li><code>verify</code> vypíše u problémových souborů <code>MISMATCH</code> (jiný součet), <code>MISSING</code> (chybí),
    <code>ERROR</code> (nečitelný), <code>EXTRA</code> (není v záznamu), <code>FAILED-AT-COPY</code> (selhal už při kopii),
    <code>FAILED-SUPERSEDED</code> (nejnovější záznam selhal, soubor sedí podle starší generace), <code>CHAIN</code>
```

Pak PDF návodu znovu (Edge, bez okna; skript vypíše cestu a velikost):

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File windows\package\navod\render.ps1`
Expected: `printed …\windows\package\Offload-navod.pdf (… KB)`. Otevřete PDF (ne aplikaci) a zkontrolujte kapitoly 5–7, 10 a 11: tabulky se vejdou na šířku, strom složek v kap. 10 je zarovnaný.

- [ ] **Step 6: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 20–25 — najít:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 8 — denní report se tvoří znovu po každé úloze na každém jejím cíli (celá kopie, doplnění i zrušená úloha; stage 2, jinak stage 1) — jeden zapisovatel na den (`SemaphoreSlim` v procesu + pojmenovaný mutex `Local\Offload-day-<sha1>` mezi GUI a CLI, čekání nejvýš 2 min), selhání jen do logu; nikdy `_REPORT` mimo složku dne (aspoň jedna karta s reportem); na vyžádání Nástroje → „Denní report…“ (složka dne nebo karty, mimo UI vlákno, otevře HTML) a CLI `offload day-report <složka>` (`--ale`, `--no-clip-list`); volba „Denní report po každé kartě“ (`outputs.dayReport`, zapnuto), CLI `--no-day-report`; `copy`/`append` vypíšou „Denní report: …“
- Úkol 7 — denní report `<cíl>\<datum>\_REPORT\<datum>_day_report.html/.pdf` (A4 na šířku, bez náhledů): dlaždice karty, soubory, data, klipy, celková délka, ověřené karty „N / M“; tabulka karet (čas, soubory, data, klipy, délka, kamery, ověření, chyby, proxy hotovo/selhalo, LUTy, počet generací); sekce Problémy (selhané soubory + prvních 5 cest, neověřené, nečitelné reporty, složka bez reportu „bez reportu (probíhá, nebo byl přerušen)“) a LUTy; denní seznam klipů `<datum>_clips.csv/.ale` přes všechny karty; složka `_REPORT` dne se za kartu nepovažuje (ani pro Ověřit zálohu)
- Úkol 6 — seznam klipů karty `_REPORT\<karta>_clips.csv` (UTF-8 s BOM, čárka, CRLF, RFC 4180; sloupce Clip Name … MD5 podle návrhu, hodnoty s `=`/`+`/`-`/`@` beze změny) a `<karta>_clips.ale` pro Avid (tabulátory, jeden soubor na fps, `End` = start + délka, drop-frame počítá jako kamera, bez TC `00:00:00:00`); píše se při každém přepisu reportu a po doplnění pokrývá všechny generace (bez analýzy z nalezených klipů, bez metadat); proxy kamery se neuvádějí; volby `outputs.clipListCsv` (zapnuto) a `outputs.clipListAle` (vypnuto), v GUI „Seznam klipů CSV“ a „Seznam klipů pro Avid (ALE)“, CLI `--no-clip-list`, `--ale`; seznam otevřený v Excelu se jen zaloguje
````

nahradit:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 9 — drobnosti: HTML/PDF report ukazuje chyby souborů česky („jiná velikost“, „nesouhlasí kontrolní součet (cíl …, karta …)“, „nesouhlasí MD5“, „chyba při ověření“, „zápis selhal“, „zrušeno“, „po N opakováních“), v JSON zůstávají anglicky (sdílený s Macem); návod kapitoly 1, 3, 5, 6, 7, 10, 11 (kontrolní součet a MD5, seznamy klipů včetně importu do Excelu přes Data → Z textu/CSV, denní report, proxy pro přelinkování, nové volby CLI a `day-report`) a nové PDF návodu
- Úkol 8 — denní report se tvoří znovu po každé úloze na každém jejím cíli (celá kopie, doplnění i zrušená úloha; stage 2, jinak stage 1) — jeden zapisovatel na den (`SemaphoreSlim` v procesu + pojmenovaný mutex `Local\Offload-day-<sha1>` mezi GUI a CLI, čekání nejvýš 2 min), selhání jen do logu; nikdy `_REPORT` mimo složku dne (aspoň jedna karta s reportem); na vyžádání Nástroje → „Denní report…“ (složka dne nebo karty, mimo UI vlákno, otevře HTML) a CLI `offload day-report <složka>` (`--ale`, `--no-clip-list`); volba „Denní report po každé kartě“ (`outputs.dayReport`, zapnuto), CLI `--no-day-report`; `copy`/`append` vypíšou „Denní report: …“
- Úkol 7 — denní report `<cíl>\<datum>\_REPORT\<datum>_day_report.html/.pdf` (A4 na šířku, bez náhledů): dlaždice karty, soubory, data, klipy, celková délka, ověřené karty „N / M“; tabulka karet (čas, soubory, data, klipy, délka, kamery, ověření, chyby, proxy hotovo/selhalo, LUTy, počet generací); sekce Problémy (selhané soubory + prvních 5 cest, neověřené, nečitelné reporty, složka bez reportu „bez reportu (probíhá, nebo byl přerušen)“) a LUTy; denní seznam klipů `<datum>_clips.csv/.ale` přes všechny karty; složka `_REPORT` dne se za kartu nepovažuje (ani pro Ověřit zálohu)
- Úkol 6 — seznam klipů karty `_REPORT\<karta>_clips.csv` (UTF-8 s BOM, čárka, CRLF, RFC 4180; sloupce Clip Name … MD5 podle návrhu, hodnoty s `=`/`+`/`-`/`@` beze změny) a `<karta>_clips.ale` pro Avid (tabulátory, jeden soubor na fps, `End` = start + délka, drop-frame počítá jako kamera, bez TC `00:00:00:00`); píše se při každém přepisu reportu a po doplnění pokrývá všechny generace (bez analýzy z nalezených klipů, bez metadat); proxy kamery se neuvádějí; volby `outputs.clipListCsv` (zapnuto) a `outputs.clipListAle` (vypnuto), v GUI „Seznam klipů CSV“ a „Seznam klipů pro Avid (ALE)“, CLI `--no-clip-list`, `--ale`; seznam otevřený v Excelu se jen zaloguje
````

Zpráva do `.git/COMMIT_MSG_PLAN7`:

```text
Windows: Czech file errors in the HTML report; manual for plan 7

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/package/Offload-navod.pdf windows/package/navod/navod.html windows/src/Offload.Core/HtmlReport.cs windows/tests/Offload.Core.Tests/HtmlReportTests.cs
git commit -F .git/COMMIT_MSG_PLAN7
```

---

### Task 10: Dokumentace, verze 0.3.0, závěrečná revize

Spec §5.1–5.4 (formáty a migrace), §8 (Mac), R22, §6.2 úkol 10. FORMATY (`.md5`, `hashAlgorithms`, `files[].md5`, MHL `<md5>`, `clips[].info.reel`, seznam klipů, denní report, settings `hash` a `outputs`), ROZDILY, VYVOJ-MAC („Naplánováno“), VYVOJ-WINDOWS (tabulka plánů), README (příkazy, ruční test plánu 7), verze 0.3.0 i v testech, které ji kontrolují. Pak závěrečná revize celé větve.

**Files:**
- Modify: `docs/FORMATY.md` (řádky 7–12, 15–20, 53–58, 63–77, 91–96, 101–107, 144–150, 158–168, 183–189)
- Modify: `docs/ROZDILY.md` (řádky 16–22, 48–53, 133–139)
- Modify: `docs/VYVOJ-MAC.md` (řádky 28–33)
- Modify: `docs/VYVOJ-WINDOWS.md` (řádky 17–25)
- Modify: `windows/Directory.Build.props` (řádky 5–12)
- Modify: `windows/README.md` (řádky 18–23, 184–189, 202–209)
- Modify: `windows/src/Offload.Core/Format.cs` (řádky 4–10)
- Modify: `windows/tests/Offload.Core.Tests/FormatTests.cs` (řádky 21–27)
- Modify: `windows/tests/Offload.Core.Tests/HtmlReportTests.cs` (řádky 15–21)
- Modify: `windows/tests/Offload.Core.Tests/ReportTests.cs` (řádky 25–31)

**Interfaces:**
- Produces: `OffloadVersion.String = "0.3.0-win"`, `Directory.Build.props` `Version 0.3.0` / `InformationalVersion 0.3.0-win`.

- [ ] **Step 1: Verze — testy nejdřív**

`windows/tests/Offload.Core.Tests/FormatTests.cs`, řádky 21–27 — najít:

```csharp
    }

    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.2.0-win", OffloadVersion.String);

    // Spec 2.1/2.5: sizes inside Czech sentences.
    [Theory]
```

nahradit:

```csharp
    }

    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.3.0-win", OffloadVersion.String);

    // Spec 2.1/2.5: sizes inside Czech sentences.
    [Theory]
```

`windows/tests/Offload.Core.Tests/ReportTests.cs`, řádky 25–31 — najít:

```csharp
        var root = doc.RootElement;
        Assert.Equal("2026-10-04T08:00:00Z", root.GetProperty("started").GetString());
        Assert.Equal("offload", root.GetProperty("tool").GetString());
        Assert.Equal("0.2.0-win", root.GetProperty("version").GetString());
        Assert.Equal(0, root.GetProperty("clips").GetArrayLength());
        Assert.False(root.TryGetProperty("proxy", out _));

```

nahradit:

```csharp
        var root = doc.RootElement;
        Assert.Equal("2026-10-04T08:00:00Z", root.GetProperty("started").GetString());
        Assert.Equal("offload", root.GetProperty("tool").GetString());
        Assert.Equal("0.3.0-win", root.GetProperty("version").GetString());
        Assert.Equal(0, root.GetProperty("clips").GetArrayLength());
        Assert.False(root.TryGetProperty("proxy", out _));

```

`windows/tests/Offload.Core.Tests/HtmlReportTests.cs`, řádky 15–21 — najít:

```csharp
        Assert.Contains("<td class=\"ok\">ověřeno</td>", html);
        Assert.Contains("<details><summary>Všechny soubory (2)</summary>", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.Contains("offload 0.2.0-win", html);
    }

    [Fact]
```

nahradit:

```csharp
        Assert.Contains("<td class=\"ok\">ověřeno</td>", html);
        Assert.Contains("<details><summary>Všechny soubory (2)</summary>", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.Contains("offload 0.3.0-win", html);
    }

    [Fact]
```

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~FormatTests|FullyQualifiedName~ReportTests|FullyQualifiedName~HtmlReportTests"`
Expected: FAIL ve 3 testech (`Version_IsWindowsBuild`, `Json_NestedShapes`, `Verified_ShowsStatusFilesAndEscapes`) — kód pořád říká `0.2.0-win`.

- [ ] **Step 2: Verze**

`windows/src/Offload.Core/Format.cs`, řádky 4–10 — najít:

```csharp

public static class OffloadVersion
{
    public const string String = "0.2.0-win";
}

public static class Format
```

nahradit:

```csharp

public static class OffloadVersion
{
    public const string String = "0.3.0-win";
}

public static class Format
```

`windows/Directory.Build.props`, řádky 5–12 — najít:

```xml
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Version>0.2.0</Version>
    <InformationalVersion>0.2.0-win</InformationalVersion>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
  </PropertyGroup>
```

nahradit:

```xml
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Version>0.3.0</Version>
    <InformationalVersion>0.3.0-win</InformationalVersion>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
  </PropertyGroup>
```

Run: stejný filtr. Expected: PASS.

- [ ] **Step 3: Dokumentace**

`docs/FORMATY.md`, řádky 7–12 — najít:

````markdown
## Struktura složek

```
<cíl>/<YYYY-MM-DD>/<karta>/          karta = jmenovka svazku (nebo název složky), volitelně přepsaná
    …soubory karty se stejnou strukturou…
    _REPORT/
````

nahradit:

````markdown
## Struktura složek

```
<cíl>/<YYYY-MM-DD>/_REPORT/          denní report (Windows od 0.3.0, Mac naplánováno) — viz „Denní report“
    <YYYY-MM-DD>_day_report.html
    <YYYY-MM-DD>_day_report.pdf
    <YYYY-MM-DD>_clips.csv           seznam klipů dne (když je CSV zapnuté)
    <YYYY-MM-DD>_clips.ale           seznam klipů dne pro Avid (když je ALE zapnuté)
<cíl>/<YYYY-MM-DD>/<karta>/          karta = jmenovka svazku (nebo název složky), volitelně přepsaná
    …soubory karty se stejnou strukturou…
    _REPORT/
````

`docs/FORMATY.md`, řádky 15–20 — najít:

````markdown
        <karta>_report.html
        <karta>_report.pdf
        <karta>.xxh64
        offload_report_0002.json      jen po doplnění (generace 2, 3 …): report jen nových souborů
        offload_report_0002.txt
        <karta>_report_0002.html
````

nahradit:

````markdown
        <karta>_report.html
        <karta>_report.pdf
        <karta>.xxh64
        <karta>.md5                   jen s xxh64 + MD5 (Windows od 0.3.0)
        <karta>_clips.csv             seznam klipů (Windows od 0.3.0)
        <karta>_clips.ale             seznam klipů pro Avid (Windows od 0.3.0, když je zapnutý)
        offload_report_0002.json      jen po doplnění (generace 2, 3 …): report jen nových souborů
        offload_report_0002.txt
        <karta>_report_0002.html
````

`docs/FORMATY.md`, řádky 53–58 — najít:

````markdown
bajt po bajtu, za nimi přijdou řádky nových souborů (a řádky, které se z dřívějšího doplnění nedostaly do seznamu,
ale MHL je zná). Starší `offload verify` (Mac i Windows 0.1.0), který čte jen tenhle seznam, tak ověří i doplněné soubory.

## `offload_report.json`

- UTF-8 bez BOM, odsazené, **klíče seřazené**, názvy v camelCase (jako Swift `Codable`).
````

nahradit:

````markdown
bajt po bajtu, za nimi přijdou řádky nových souborů (a řádky, které se z dřívějšího doplnění nedostaly do seznamu,
ale MHL je zná). Starší `offload verify` (Mac i Windows 0.1.0), který čte jen tenhle seznam, tak ověří i doplněné soubory.

## `<karta>.md5` (Windows od 0.3.0, Mac naplánováno)

Jen když kopie počítala i MD5 (volba „xxHash64 + MD5“, `--hash both`; MD5 nikdy není samotné — `<karta>.xxh64` je vždy).
Formát `md5sum`: jeden řádek na soubor, UTF-8 bez BOM, `\n`:

```
<md5 jako 32 malých hex číslic><dvě mezery><relativní cesta s />
```

Po doplnění se prodlouží stejně jako `<karta>.xxh64`. Doplnění počítá stejné součty jako dřívější záloha (podle jejího
`hashAlgorithms`), ne podle aktuální volby.

## `offload_report.json`

- UTF-8 bez BOM, odsazené, **klíče seřazené**, názvy v camelCase (jako Swift `Codable`).
````

`docs/FORMATY.md`, řádky 63–77 — najít:

````markdown
| Klíč | Typ | Význam |
|---|---|---|
| `tool` | string | `offload` |
| `version` | string | `0.1.0` (Mac), `0.2.0-win` (Windows) |
| `generation` | číslo? | jen Windows od 0.2.0 a jen v reportu doplnění (`offload_report_NNNN.json`): číslo generace ASC MHL; chybí = 1 (celá kopie) |
| `hashAlgorithm` | string | `xxh64` |
| `cardName`, `date`, `source`, `sourceFilesystem` | string | |
| `started`, `copyFinished`, `finished` | datum | |
| `verified` | bool | proběhlo ověření zpětným čtením |
| `totalFiles`, `totalBytes` | číslo | |
| `destinations[]` | objekt | `root`, `cardPath`, `filesystem`, `filesOK`, `filesFailed`, volitelně `unbuffered` (jen `false`, jen Windows) |
| `files[]` | objekt | `path`, `size`, `modified`, `xxh64`?, `sourceError`?, `destinations[]` (stav pro každý cíl), `errors[]` (chyba nebo `null` pro každý cíl) |
| `skipped[]` | string | vynechané systémové položky |
| `clips[]` | objekt | `clip` {`name`, `path`, `format`, `segments`, `bytes`, `isCameraProxy`}, `info`?, `probeError`?, `proxyStatus`, `proxyPath`?, `proxyNote`?, `proxySeconds`?, `lut`? {`source`, `id`, `name`, `reason`} (použitý LUT a proč; Windows od plánu 5, Mac zatím ignoruje) |
| `proxy` | objekt? | nastavení proxy (`enabled`, `height`, `codec`, `bitrateMbps`, `lutPath`, `lutMode` (`auto`/`builtin`/`custom`/`none`), `lutBuiltin`, `lutFallback`, `burnFilename`, `burnTimecode`, `audio`, `skipCameraProxies`, `copyToAllDestinations`, `frameAspect`, `frameScale`; jen Windows, volitelně `extraFolder` — viz níže) |
````

nahradit:

````markdown
| Klíč | Typ | Význam |
|---|---|---|
| `tool` | string | `offload` |
| `version` | string | `0.1.0` (Mac), `0.3.0-win` (Windows) |
| `generation` | číslo? | jen Windows od 0.2.0 a jen v reportu doplnění (`offload_report_NNNN.json`): číslo generace ASC MHL; chybí = 1 (celá kopie) |
| `hashAlgorithm` | string | `xxh64` (vždy, i s MD5 — Mac ho jen zobrazí) |
| `hashAlgorithms` | string[]? | jen Windows od 0.3.0 a jen když nejsou jen xxh64: `["xxh64", "md5"]` |
| `cardName`, `date`, `source`, `sourceFilesystem` | string | |
| `started`, `copyFinished`, `finished` | datum | |
| `verified` | bool | proběhlo ověření zpětným čtením |
| `totalFiles`, `totalBytes` | číslo | |
| `destinations[]` | objekt | `root`, `cardPath`, `filesystem`, `filesOK`, `filesFailed`, volitelně `unbuffered` (jen `false`, jen Windows) |
| `files[]` | objekt | `path`, `size`, `modified`, `xxh64`?, `md5`? (32 malých hex, jen s MD5, Windows od 0.3.0), `sourceError`?, `destinations[]` (stav pro každý cíl), `errors[]` (chyba nebo `null` pro každý cíl; anglicky — HTML je zobrazí česky) |
| `skipped[]` | string | vynechané systémové položky |
| `clips[]` | objekt | `clip` {`name`, `path`, `format`, `segments`, `bytes`, `isCameraProxy`}, `info`?, `probeError`?, `proxyStatus`, `proxyPath`?, `proxyNote`?, `proxySeconds`?, `lut`? {`source`, `id`, `name`, `reason`} (použitý LUT a proč; Windows od plánu 5, Mac zatím ignoruje) |
| `proxy` | objekt? | nastavení proxy (`enabled`, `height`, `codec`, `bitrateMbps`, `lutPath`, `lutMode` (`auto`/`builtin`/`custom`/`none`), `lutBuiltin`, `lutFallback`, `burnFilename`, `burnTimecode`, `audio`, `skipCameraProxies`, `copyToAllDestinations`, `frameAspect`, `frameScale`; jen Windows, volitelně `extraFolder` — viz níže) |
````

`docs/FORMATY.md`, řádky 91–96 — najít:

````markdown
- `clips[].info.gamma`, `gamut`, `colorScienceGen` (volitelné, zatím jen Windows): u Blackmagic RAW gama, gamut a generace
  barevné vědy, ve kterých byl klip natočený (z `braw-decode --info`, např. `"Blackmagic Design Film"`, `5`). Podle nich se
  volí LUT. Mac je zatím nezapisuje a při čtení ignoruje.

Náhledy klipů (JPEG) se do JSON neukládají — jsou jen v HTML a PDF.

````

nahradit:

````markdown
- `clips[].info.gamma`, `gamut`, `colorScienceGen` (volitelné, zatím jen Windows): u Blackmagic RAW gama, gamut a generace
  barevné vědy, ve kterých byl klip natočený (z `braw-decode --info`, např. `"Blackmagic Design Film"`, `5`). Podle nich se
  volí LUT. Mac je zatím nezapisuje a při čtení ignoruje.
- `clips[].info.reel` (volitelné, Windows od 0.3.0): cívka klipu — tag kamery (`reel_name`, `tape_name`, `reel` z ffprobe),
  jinak cívka z názvu klipu (`A001C003…` → `A001`), jinak název složky karty. Je i v proxy (stopa timecodu). Mac ignoruje.

Náhledy klipů (JPEG) se do JSON neukládají — jsou jen v HTML a PDF.

````

`docs/FORMATY.md`, řádky 101–107 — najít:

````markdown
## ASC MHL v2

- `urn:ASC:MHL:v2.0`, první generace (`0001_…`), hash `xxh64` pro každý soubor,
  `action="original"` (zapsáno/ověřeno) nebo `"failed"`.
- Ignorované vzory: `.DS_Store`, `ascmhl`, `ascmhl/`, `_REPORT`, `_PROXY` + vynechané systémové soubory.
- `ascmhl_chain.xml` s C4 ID souboru MHL (`c4` + base58(SHA-512), doplněno `1` na 88 znaků).
- Ověřitelné nástrojem `ascmhl verify <složka karty>`.
````

nahradit:

````markdown
## ASC MHL v2

- `urn:ASC:MHL:v2.0`, první generace (`0001_…`), hash `xxh64` pro každý soubor,
  `action="original"` (zapsáno/ověřeno) nebo `"failed"`. S MD5 (Windows od 0.3.0) má `<hash>` navíc prvek
  `<md5 action=… hashdate=…>` se stejným `action` a `hashdate`.
- Ignorované vzory: `.DS_Store`, `ascmhl`, `ascmhl/`, `_REPORT`, `_PROXY` + vynechané systémové soubory.
- `ascmhl_chain.xml` s C4 ID souboru MHL (`c4` + base58(SHA-512), doplněno `1` na 88 znaků).
- Ověřitelné nástrojem `ascmhl verify <složka karty>`.
````

`docs/FORMATY.md`, řádky 144–150 — najít:

````markdown

| Klíč | Typ | Význam |
|---|---|---|
| `tool`, `version` | string | `offload`, `0.2.0-win` |
| `cardPath` | string | ověřená složka karty |
| `started`, `finished` | datum | |
| `basis` | string | `ascmhl` (všechny generace) nebo `hashList` (jen `<karta>.xxh64` / `.md5`) |
````

nahradit:

````markdown

| Klíč | Typ | Význam |
|---|---|---|
| `tool`, `version` | string | `offload`, `0.2.0-win` a novější |
| `cardPath` | string | ověřená složka karty |
| `started`, `finished` | datum | |
| `basis` | string | `ascmhl` (všechny generace) nebo `hashList` (jen `<karta>.xxh64` / `.md5`) |
````

`docs/FORMATY.md`, řádky 158–168 — najít:

````markdown
| `chainProblems[]` | string[] | česky: změněná nebo chybějící generace, chybějící řetěz |
| `success` | bool | nic nesedí špatně, nechybí, není nečitelné a nic nezůstalo `failedAtCopy` (soubory navíc výsledek neovlivní) |

## settings.json (jen tento počítač)

- Neznámé klíče se při uložení zachovají (na horní úrovni, v `proxy` i v `tools`; i sekce `proxy` nebo `tools` s hodnotou
  z novější verze, kterou tahle verze nedokáže přečíst, zůstane beze změny) — Windows od 0.2.0 (verze 0.1.0 je smazala).
- `naming.suggestFromClips` (bool, výchozí `true`, Windows od 0.2.0): navrhovat název složky karty z názvů klipů.

## Místní soubory (jen tento počítač, Mac převezme stejný tvar)

````

nahradit:

````markdown
| `chainProblems[]` | string[] | česky: změněná nebo chybějící generace, chybějící řetěz |
| `success` | bool | nic nesedí špatně, nechybí, není nečitelné a nic nezůstalo `failedAtCopy` (soubory navíc výsledek neovlivní) |

## Seznam klipů — `_clips.csv`, `_clips.ale` (Windows od 0.3.0, Mac naplánováno)

Odvozené soubory: přepisují se celé (dočasné jméno + nahrazení) při každém zápisu reportu, po doplnění pokrývají všechny
generace. Jeden řádek na klip (proxy nahrané kamerou — složky `SUB`, `PROXY` — se neuvádějí); bez analýzy klipů jen
klipy nalezené v souborech, metadata prázdná.

**CSV** (`<karta>_clips.csv`, `<datum>_clips.csv`): UTF-8 **s** BOM, oddělovač čárka, konce řádků CRLF, RFC 4180 (pole
s čárkou, uvozovkou nebo koncem řádku v uvozovkách, uvozovky zdvojené), hlavička vždy, sloupce v tomto pořadí:
`Clip Name`, `File Name` (hlavní soubor s příponou), `Path` (relativní, `/`), `Card` (složka karty), `Camera`, `Reel`,
`Start TC` (jak ho dá zdroj, `;` u drop-frame; prázdné bez TC), `Duration` (`HH:MM:SS:FF` v nominálních snímcích; bez fps
`H:MM:SS`), `FPS` (invariantně, `23.976`, `25`, `29.97`), `Resolution` (`3840x2160`), `Codec` (u RAW formát, např.
`Blackmagic RAW`), `LUT` (název použitého LUTu), `Size (bytes)`, `XXH64` a `MD5` (hlavního souboru; u vícedílných klipů
prvního dílu; MD5 prázdné bez MD5). Hodnoty začínající `=`, `+`, `-`, `@` se neupravují.

**ALE** (`<karta>_clips.ale`, `<datum>_clips.ale`): UTF-8 bez BOM, CRLF, tabulátory (tabulátor a konec řádku uvnitř
hodnoty → mezera). Hlavička `Heading` / `FIELD_DELIM TABS` / `VIDEO_FORMAT` (nejčastější výška: `1080`, `720`, jinak
`CUSTOM`) / `AUDIO_FORMAT 48khz` / `FPS`, pak `Column` se sloupci `Name, Tape, Start, End, Tracks, Source File, Camera,
LUT, Resolution, Codec, XXH64, MD5` a `Data`. `Tape` = cívka, `End` = začátek + délka (výlučně, drop-frame počítá jako
kamera), bez TC začátek `00:00:00:00`, `Tracks` = `V` + `A1…An` podle zvukových kanálů (nejvýš 8). ALE má jedno FPS:
smíšené fps → soubor na každé, `<název>_clips_25.ale`, `<název>_clips_23.976.ale` (klip bez fps patří k nejčastějšímu;
když fps nezná žádný klip, hlavička `FPS 25`). ALE, které se při novém zápisu už nevytváří, se smaže.

## Denní report (Windows od 0.3.0, Mac naplánováno)

`<cíl>/<YYYY-MM-DD>/_REPORT/<YYYY-MM-DD>_day_report.html/.pdf` + seznam klipů dne. Odvozené soubory (přepisují se celé,
poslední zápis vyhraje — i proti Macu, který by zapisoval do stejného dne). Karty dne = podsložky `<datum>/` kromě
`_REPORT` a skrytých; složka bez `_REPORT/offload_report.json` je v reportu „bez reportu (probíhá, nebo byl přerušen)“.
Vytváří se znovu po každé úloze na každém jejím cíli (i doplnění a zrušená úloha) a na vyžádání; jeden zapisovatel na den
(Windows: pojmenovaný mutex `Local\Offload-day-<sha1(cesta)>`). `_REPORT` ve složce dne nemá `offload_report.json` ani
`.xxh64`, takže ho `verify` za kartu nepovažuje; sdílí ho report ověření cizí zálohy (`<karta>_verify_<stamp>Z.*`).

## settings.json (jen tento počítač)

- Neznámé klíče se při uložení zachovají (na horní úrovni, v `proxy` i v `tools`; i sekce `proxy` nebo `tools` s hodnotou
  z novější verze, kterou tahle verze nedokáže přečíst, zůstane beze změny) — Windows od 0.2.0 (verze 0.1.0 je smazala).
- `naming.suggestFromClips` (bool, výchozí `true`, Windows od 0.2.0): navrhovat název složky karty z názvů klipů.
- `hash` (`"xxh64"` | `"both"`, výchozí `"xxh64"`, Windows od 0.3.0): kontrolní součty kopie; `both` = xxh64 + MD5. Jiná
  hodnota (z novější verze) se zachová a čte jako `xxh64`.
- `outputs` (Windows od 0.3.0): `dayReport` (bool, `true`) — denní report po každé úloze; `clipListCsv` (bool, `true`),
  `clipListAle` (bool, `false`) — seznamy klipů. Neznámé klíče sekce se zachovají.

## Místní soubory (jen tento počítač, Mac převezme stejný tvar)

````

`docs/FORMATY.md`, řádky 183–189 — najít:

````markdown
| `finished` | datum | konec poslední kopie / doplnění |
| `success`, `verified` | bool | všechny generace bez chyb / ověřené |
| `failedFiles` | číslo? | jen když `success` = false |
| `hashAlgorithms` | string[] | `["xxh64"]` |
| `files`, `bytes` | číslo | sjednocení všech generací |
| `fingerprint`, `shape` | string | SHA-256 (hex) přes řádky seřazené podle cesty `relpath\tsize\tmtimeTicksUtc\n` resp. `relpath\tsize\n` |
| `fileList` | string | `history/<id>.files.gz` — gzip řádků `relpath\tsize\tmtimeTicksUtc\n` (seznam po všech generacích) |
````

nahradit:

````markdown
| `finished` | datum | konec poslední kopie / doplnění |
| `success`, `verified` | bool | všechny generace bez chyb / ověřené |
| `failedFiles` | číslo? | jen když `success` = false |
| `hashAlgorithms` | string[] | `["xxh64"]`, s MD5 `["xxh64", "md5"]` (Windows od 0.3.0) |
| `files`, `bytes` | číslo | sjednocení všech generací |
| `fingerprint`, `shape` | string | SHA-256 (hex) přes řádky seřazené podle cesty `relpath\tsize\tmtimeTicksUtc\n` resp. `relpath\tsize\n` |
| `fileList` | string | `history/<id>.files.gz` — gzip řádků `relpath\tsize\tmtimeTicksUtc\n` (seznam po všech generacích) |
````

`docs/ROZDILY.md`, řádky 16–22 — najít:

````markdown
| Složka | [`mac/`](../mac/) | [`windows/`](../windows/) |
| Systém | macOS 13+ | Windows 10/11 x64 |
| Jazyk / UI | Swift 5.9, SwiftUI | C# / .NET 10, WPF |
| Verze | 0.1.0 | 0.2.0-win (ve vývoji) |
| Aplikace | `Offload.app` (CLI uvnitř `Contents/Resources/bin/offload`) | `Offload.exe` + CLI `bin\offload.exe` |
| Build | `mac/scripts/build-app.sh` | `windows/scripts/build.ps1` |

````

nahradit:

````markdown
| Složka | [`mac/`](../mac/) | [`windows/`](../windows/) |
| Systém | macOS 13+ | Windows 10/11 x64 |
| Jazyk / UI | Swift 5.9, SwiftUI | C# / .NET 10, WPF |
| Verze | 0.1.0 | 0.3.0-win (ve vývoji) |
| Aplikace | `Offload.app` (CLI uvnitř `Contents/Resources/bin/offload`) | `Offload.exe` + CLI `bin\offload.exe` |
| Build | `mac/scripts/build-app.sh` | `windows/scripts/build.ps1` |

````

`docs/ROZDILY.md`, řádky 48–53 — najít:

````markdown
| **Ověřit zálohu** (všechny generace MHL, hashe xxh64/md5/sha1/xxh3/xxh128/c4, report ověření) | `verify` jen proti `.xxh64` | ✅ CLI `verify` + GUI Nástroje → Ověřit zálohu… |
| **Kontrola místa** (proxy, report, fronta, po svazcích, rezerva 5 %) | jen velikost karty | ✅ CLI + GUI |
| **Hlášky kontrol před kopií česky** (`PreflightCode`) | ⏳ naplánováno (zatím anglicky) | ✅ |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ❌ | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI: řetěz připojení s rychlostí, % linky, součtem na řadiči a úzkým hrdlem na kartě úlohy |

✅ hotovo · 🔨 rozpracováno · ⏳ naplánováno · ❌ není
````

nahradit:

````markdown
| **Ověřit zálohu** (všechny generace MHL, hashe xxh64/md5/sha1/xxh3/xxh128/c4, report ověření) | `verify` jen proti `.xxh64` | ✅ CLI `verify` + GUI Nástroje → Ověřit zálohu… |
| **Kontrola místa** (proxy, report, fronta, po svazcích, rezerva 5 %) | jen velikost karty | ✅ CLI + GUI |
| **Hlášky kontrol před kopií česky** (`PreflightCode`) | ⏳ naplánováno (zatím anglicky) | ✅ |
| **Denní report** (`<datum>/_REPORT/<datum>_day_report.html/.pdf`, `offload day-report`) | ⏳ naplánováno | ✅ CLI + GUI (čeká ruční test) |
| **Seznam klipů CSV / Avid ALE** (`_clips.csv/.ale` karty i dne) | ⏳ naplánováno | ✅ CLI + GUI (import do Resolve/Avid čeká ruční test) |
| **Proxy pro přelinkování** (`reel_name` ve stopě TC, všechny zvukové stopy) | ⏳ naplánováno (1 zvuková stopa) | ✅ (Resolve/Premiere čeká ruční test) |
| **MD5 navíc k xxh64** (`hash: both`, `--hash both`, `<karta>.md5`, MHL `<md5>`) | ⏳ naplánováno (MD5 ignoruje, xxh64 ověří) | ✅ CLI + GUI |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ❌ | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI: řetěz připojení s rychlostí, % linky, součtem na řadiči a úzkým hrdlem na kartě úlohy |

✅ hotovo · 🔨 rozpracováno · ⏳ naplánováno · ❌ není
````

`docs/ROZDILY.md`, řádky 133–139 — najít:

````markdown
- **R3D/ARRI bez proxy** zatím nemají náhledy (stejně jako na Macu).
- **Enkodér proxy v reportu:** Windows zapisuje do JSON volitelné `proxyEncoder` (např. `h264_nvenc`, `libx264`)
  a ukazuje ho v řádku „Proxy: H.264 (h264_nvenc), …“ v HTML/PDF. Mac klíč nezná a ignoruje ho.
- **Verze v reportu:** Mac `0.1.0`, Windows `0.2.0-win` — z reportu je vidět, která verze kopírovala.
- **Historie karet** (zatím jen Windows, pro Mac naplánováno): `%APPDATA%\Offload\history.json` (formát ve FORMATY.md,
  „Místní soubory“) — řádek „Zálohováno … — ověřeno“ u karty, dotaz „Karty už jsou zálohované“ před celou kopií, CLI
  vypíše řádek před plánem (bez dotazu); `--no-history` historii vypne. Karta se pozná podle sériového čísla souborového
````

nahradit:

````markdown
- **R3D/ARRI bez proxy** zatím nemají náhledy (stejně jako na Macu).
- **Enkodér proxy v reportu:** Windows zapisuje do JSON volitelné `proxyEncoder` (např. `h264_nvenc`, `libx264`)
  a ukazuje ho v řádku „Proxy: H.264 (h264_nvenc), …“ v HTML/PDF. Mac klíč nezná a ignoruje ho.
- **Verze v reportu:** Mac `0.1.0`, Windows `0.3.0-win` — z reportu je vidět, která verze kopírovala.
- **MD5** (zatím jen Windows): volba xxHash64 + MD5 přidá `hashAlgorithms`, `files[].md5`, `<karta>.md5` a MHL `<md5>`;
  xxh64 je vždy, takže zálohu s MD5 Mac ověří dál (MD5 přeskočí). Doplnění počítá součty dřívější zálohy.
- **Denní report a seznamy klipů** (zatím jen Windows): `<datum>/_REPORT/` se denním reportem a `_clips.csv/.ale` karty
  i dne jsou odvozené soubory (přepisují se celé); Mac je nečte a jeho `UniqueCardFolder` s `_REPORT` ve složce dne
  nekoliduje. Kdyby Mac zapisoval stejný den, vyhraje poslední zápis.
- **Proxy pro přelinkování** (zatím jen Windows): s timecodem `reel_name` ve stopě TC proxy (cívka: tag kamery, název
  klipu, složka karty; v reportu `clips[].info.reel`), všechny zvukové stopy zdroje (AAC 64 kb/s na kanál, ProRes PCM);
  přejmenovaná proxy (`_2`) má poznámku. Mac dává proxy jen první zvukovou stopu a bez cívky.
- **Chyby souborů v HTML česky** (Windows): JSON je sdílený, takže chyby v něm zůstávají anglicky (`size mismatch`,
  `HASH MISMATCH` …); HTML/PDF report je zobrazí česky.
- **Historie karet** (zatím jen Windows, pro Mac naplánováno): `%APPDATA%\Offload\history.json` (formát ve FORMATY.md,
  „Místní soubory“) — řádek „Zálohováno … — ověřeno“ u karty, dotaz „Karty už jsou zálohované“ před celou kopií, CLI
  vypíše řádek před plánem (bez dotazu); `--no-history` historii vypne. Karta se pozná podle sériového čísla souborového
````

`docs/VYVOJ-MAC.md`, řádky 28–33 — najít:

````markdown
  menu); **kontrolu místa** (odhad a plán po svazcích, tabulka toků, přepočet před proxy); **české hlášky** (`Msg`
  a kódy kontrol v `Engine.swift` a `main.swift`).

- **Mimo plán — zbývá i po něm:**
  - Blackmagic RAW Film Gen 4: zatím bez LUTu (provizorně, jako Windows) — rozhodne uživatel; platí pro obě verze.
  - Nastavení nástrojů: řádek „braw-decode“ ukazuje „nalezeno“ i bez knihovny Blackmagic RAW (stav BRAW je vidět z poznámky
````

nahradit:

````markdown
  menu); **kontrolu místa** (odhad a plán po svazcích, tabulka toků, přepočet před proxy); **české hlášky** (`Msg`
  a kódy kontrol v `Engine.swift` a `main.swift`).

- **Výstupy pro produkci a střih (Windows plán 7, 2026-10-07):** návrh
  [`windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md`](../windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md)
  §3 a §8. Mac převezme: **denní report** (`<datum>/_REPORT/<datum>_day_report.html/.pdf` a seznam klipů dne, tvorba po
  úloze v `Job.swift`, stejné sloupce a texty — FORMATY.md „Denní report“); **seznamy klipů** CSV/ALE stejných sloupců
  (FORMATY.md „Seznam klipů“); **proxy pro přelinkování** (`Proxy.swift`: `reel_name` ve stopě TC, všechny zvukové
  stopy, cívka tag → název → složka, klíč `clips[].info.reel`); **MD5** (`Hash.swift` + CommonCrypto MD5, volba
  xxh64/both, `hashAlgorithms`, `files[].md5`, `<karta>.md5`, MHL `<md5>`, `verify` z MHL).

- **Mimo plán — zbývá i po něm:**
  - Blackmagic RAW Film Gen 4: zatím bez LUTu (provizorně, jako Windows) — rozhodne uživatel; platí pro obě verze.
  - Nastavení nástrojů: řádek „braw-decode“ ukazuje „nalezeno“ i bez knihovny Blackmagic RAW (stav BRAW je vidět z poznámky
````

`windows/README.md`, řádky 18–23 — najít:

````markdown
| GUI `Offload.exe` | hotovo (čeká ruční test s kartou) |
| Vestavěné LUTy (plán 5) | hotovo (čeká ruční test s klipy z kamer) |
| Karty a bezpečnost dat (plán 6, 0.2.0): historie, doplnění, název z klipů, ověření zálohy, místo, české hlášky | hotovo (čeká ruční test s kartou) |

## Build a testy

````

nahradit:

````markdown
| GUI `Offload.exe` | hotovo (čeká ruční test s kartou) |
| Vestavěné LUTy (plán 5) | hotovo (čeká ruční test s klipy z kamer) |
| Karty a bezpečnost dat (plán 6, 0.2.0): historie, doplnění, název z klipů, ověření zálohy, místo, české hlášky | hotovo (čeká ruční test s kartou) |
| Výstupy pro produkci a střih (plán 7, 0.3.0): denní report, seznam klipů CSV/ALE, proxy pro přelinkování, MD5 | hotovo (čeká ruční test v Resolve, Premiere, Avid, Excelu) |

## Build a testy

````

`windows/README.md`, řádky 184–189 — najít:

````markdown
      disk připojený?“; neznámá volba (`offload E:\ -d D:\ --xyz`) → chyba parseru česky (satelit `cs` v `bin\cs\`);
      v GUI chyba před kopií na kartě úlohy česky

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

## Použití
````

nahradit:

````markdown
      disk připojený?“; neznámá volba (`offload E:\ -d D:\ --xyz`) → chyba parseru česky (satelit `cs` v `bin\cs\`);
      v GUI chyba před kopií na kartě úlohy česky

Plán 7 (výstupy pro produkci a střih, 0.3.0):

- [ ] 21. **MD5:** Struktura a kontrola → Kontrolní součet „xxHash64 + MD5“ → pod volbou oranžové „MD5 je pomalejší …“,
      zaškrtávátko „Ověřit kopie zpětným čtením (xxHash64 + MD5)“; offload karty → `_REPORT\<karta>.md5`, v HTML dlaždice
      „XXH64 + MD5“ a sloupec md5; Nástroje → Ověřit zálohu… → „… (ASC MHL, 1 generace, xxHash64 + MD5).“; **rychlá karta
      (CFexpress)**: změřit dobu kopie s xxHash64 a s MD5 (a `offload bench --hash md5`) a zapsat do „Naměřeno“
- [ ] 22. **Proxy v Resolve:** naimportovat originály, proxy z `_PROXY` připojit (Link Proxy Media) — sedí jméno, TC; sloupec
      Reel ukazuje cívku (u ARRI/RED/Blackmagic `A001`, jinak tag kamery nebo složku karty)
- [ ] 23. **Proxy v Premiere:** Sony MXF se 4–8 mono stopami → proxy se stejným počtem stop; „Attach Proxies“ projde;
      Tape Name = cívka. Dva klipy stejného jména → `_2` a u klipu poznámka „Proxy přejmenována na … — přelinkujte ji ručně.“
- [ ] 24. **CSV:** `_REPORT\<karta>_clips.csv` v Resolve (Import Metadata → Media Pool) doplní metadata; v Excelu přes
      Data → Z textu/CSV (oddělovač čárka) čeština a sloupce sedí
- [ ] 25. **ALE:** volba „Seznam klipů pro Avid (ALE)“ → `<karta>_clips.ale` jde importovat do Avid Media Composeru (Start,
      End, Tape, Tracks); karta se smíšenými fps (25 a 23.976) → dva soubory `_clips_25.ale`, `_clips_23.976.ale`
- [ ] 26. **Denní report:** dvě karty týž den na dva cíle → na obou `<datum>\_REPORT\<datum>_day_report.html/.pdf` s oběma
      kartami, dlaždice „ověřené karty 2 / 2“; během kopie třetí karty ji denní report ukáže „bez reportu (probíhá …)“;
      Nástroje → Denní report… na složku dne (i na složku karty) report vytvoří a otevře; na kořen cíle → česká chyba
      „… není žádná karta s reportem …“, nic se nezapíše; GUI a `offload day-report` téhož dne zároveň → oba projdou
- [ ] 27. **Otevřené v Excelu:** denní CSV a `<karta>_clips.csv` otevřít v Excelu, pak doplnit kartu → úloha „Hotovo“,
      v `app.log` řádky „clip list …“ / „day report …“; po zavření Excelu Nástroje → Denní report… soubory obnoví;
      volba „Denní report po každé kartě“ vypnutá → po úloze se denní report nezmění

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

## Použití
````

`windows/README.md`, řádky 202–209 — najít:

````markdown
offload append E:\                                          # doplnit jen nové klipy do dřívější zálohy (cíle z historie)
offload append E:\ --into D:\Zaloha\2026-10-06\A001 --into F:\Zaloha\2026-10-06\A001
offload verify D:\Zaloha\2026-10-06 --report                # ověřit všechny karty dne proti ASC MHL + report ověření
```

Historie (`%APPDATA%\Offload\history.json`): `copy` před plánem vypíše „Zálohováno … — ověřeno“, když kartu zná (bez
dotazu), a po dokončené kopii ji zapíše; `--no-history` čtení i zápis vypne. `append` bez `--into` najde složky zálohy
v historii (jinak „Chyba: Karta není v historii — zadejte složky zálohy volbou --into.“; s `--no-history` je `--into`
````

nahradit:

````markdown
offload append E:\                                          # doplnit jen nové klipy do dřívější zálohy (cíle z historie)
offload append E:\ --into D:\Zaloha\2026-10-06\A001 --into F:\Zaloha\2026-10-06\A001
offload verify D:\Zaloha\2026-10-06 --report                # ověřit všechny karty dne proti ASC MHL + report ověření
offload E:\ -d D:\Zaloha -d F:\Zaloha --hash both --ale     # xxHash64 + MD5, seznam klipů i pro Avid
offload day-report D:\Zaloha\2026-10-06                     # denní report znovu (HTML, PDF, seznam klipů dne)
offload bench --hash md5                                    # rychlost MD5 proti xxh64 (CPU)
```

Výstupy (plán 7): po každé kopii nebo doplnění vznikne `_REPORT\<karta>_clips.csv` (`--no-clip-list` vypne, `--ale`
přidá `<karta>_clips.ale` pro Avid) a na každém cíli znovu denní report `<datum>\_REPORT\<datum>_day_report.html/.pdf`
se seznamem klipů dne (`--no-day-report` vypne); CLI vypíše „Denní report: …“. `--hash both` počítá navíc MD5
(`<karta>.md5`, MD5 v reportu a ASC MHL); `append` volbu nemá — doplnění počítá součty dřívější zálohy. `day-report
<složka>` vytvoří denní report znovu (složka dne, nebo karty v ní), kód 0/1.

Historie (`%APPDATA%\Offload\history.json`): `copy` před plánem vypíše „Zálohováno … — ověřeno“, když kartu zná (bez
dotazu), a po dokončené kopii ji zapíše; `--no-history` čtení i zápis vypne. `append` bez `--into` najde složky zálohy
v historii (jinak „Chyba: Karta není v historii — zadejte složky zálohy volbou --into.“; s `--no-history` je `--into`
````

`docs/VYVOJ-WINDOWS.md`, řádky 17–25 — najít:

````markdown
| 4 | WPF GUI, detekce a vysunutí karet, balíček | hotovo (čeká ruční test s kartou) |
| 5 | vestavěné LUTy Log → Rec.709 (ARRI, Sony, Blackmagic, RED, Canon, Panasonic, DJI, Fuji), automatický výběr podle metadat klipu | hotovo (čeká ruční test s klipy z kamer) |
| 6 | karty a bezpečnost dat: historie karet, doplnění nových klipů, název z klipů, ověření zálohy proti ASC MHL, kontrola místa, české hlášky (0.2.0) | hotovo (čeká ruční test s kartou) |

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 9 — drobnosti: HTML/PDF report ukazuje chyby souborů česky („jiná velikost“, „nesouhlasí kontrolní součet (cíl …, karta …)“, „nesouhlasí MD5“, „chyba při ověření“, „zápis selhal“, „zrušeno“, „po N opakováních“), v JSON zůstávají anglicky (sdílený s Macem); návod kapitoly 1, 3, 5, 6, 7, 10, 11 (kontrolní součet a MD5, seznamy klipů včetně importu do Excelu přes Data → Z textu/CSV, denní report, proxy pro přelinkování, nové volby CLI a `day-report`) a nové PDF návodu
- Úkol 8 — denní report se tvoří znovu po každé úloze na každém jejím cíli (celá kopie, doplnění i zrušená úloha; stage 2, jinak stage 1) — jeden zapisovatel na den (`SemaphoreSlim` v procesu + pojmenovaný mutex `Local\Offload-day-<sha1>` mezi GUI a CLI, čekání nejvýš 2 min), selhání jen do logu; nikdy `_REPORT` mimo složku dne (aspoň jedna karta s reportem); na vyžádání Nástroje → „Denní report…“ (složka dne nebo karty, mimo UI vlákno, otevře HTML) a CLI `offload day-report <složka>` (`--ale`, `--no-clip-list`); volba „Denní report po každé kartě“ (`outputs.dayReport`, zapnuto), CLI `--no-day-report`; `copy`/`append` vypíšou „Denní report: …“
- Úkol 7 — denní report `<cíl>\<datum>\_REPORT\<datum>_day_report.html/.pdf` (A4 na šířku, bez náhledů): dlaždice karty, soubory, data, klipy, celková délka, ověřené karty „N / M“; tabulka karet (čas, soubory, data, klipy, délka, kamery, ověření, chyby, proxy hotovo/selhalo, LUTy, počet generací); sekce Problémy (selhané soubory + prvních 5 cest, neověřené, nečitelné reporty, složka bez reportu „bez reportu (probíhá, nebo byl přerušen)“) a LUTy; denní seznam klipů `<datum>_clips.csv/.ale` přes všechny karty; složka `_REPORT` dne se za kartu nepovažuje (ani pro Ověřit zálohu)
````

nahradit:

````markdown
| 4 | WPF GUI, detekce a vysunutí karet, balíček | hotovo (čeká ruční test s kartou) |
| 5 | vestavěné LUTy Log → Rec.709 (ARRI, Sony, Blackmagic, RED, Canon, Panasonic, DJI, Fuji), automatický výběr podle metadat klipu | hotovo (čeká ruční test s klipy z kamer) |
| 6 | karty a bezpečnost dat: historie karet, doplnění nových klipů, název z klipů, ověření zálohy proti ASC MHL, kontrola místa, české hlášky (0.2.0) | hotovo (čeká ruční test s kartou) |
| 7 | výstupy pro produkci a střih: denní report, seznam klipů CSV/ALE, proxy pro přelinkování (reel name, všechny zvukové stopy), MD5 navíc k xxh64 (0.3.0) | hotovo (čeká ruční test: Resolve, Premiere, Avid, Excel) |

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 10 — dokumentace a verze 0.3.0: FORMATY (`<karta>.md5`, `hashAlgorithms`, `files[].md5`, MHL `<md5>`, `clips[].info.reel`, seznam klipů CSV/ALE, denní report, settings `hash` a `outputs`), ROZDILY, VYVOJ-MAC (co převezme Mac), README (příkazy `day-report`, `--hash`, `--ale`, `--no-clip-list`, `--no-day-report`, ruční test plánu 7), `OffloadVersion` a `Directory.Build.props` 0.3.0
- Úkol 9 — drobnosti: HTML/PDF report ukazuje chyby souborů česky („jiná velikost“, „nesouhlasí kontrolní součet (cíl …, karta …)“, „nesouhlasí MD5“, „chyba při ověření“, „zápis selhal“, „zrušeno“, „po N opakováních“), v JSON zůstávají anglicky (sdílený s Macem); návod kapitoly 1, 3, 5, 6, 7, 10, 11 (kontrolní součet a MD5, seznamy klipů včetně importu do Excelu přes Data → Z textu/CSV, denní report, proxy pro přelinkování, nové volby CLI a `day-report`) a nové PDF návodu
- Úkol 8 — denní report se tvoří znovu po každé úloze na každém jejím cíli (celá kopie, doplnění i zrušená úloha; stage 2, jinak stage 1) — jeden zapisovatel na den (`SemaphoreSlim` v procesu + pojmenovaný mutex `Local\Offload-day-<sha1>` mezi GUI a CLI, čekání nejvýš 2 min), selhání jen do logu; nikdy `_REPORT` mimo složku dne (aspoň jedna karta s reportem); na vyžádání Nástroje → „Denní report…“ (složka dne nebo karty, mimo UI vlákno, otevře HTML) a CLI `offload day-report <složka>` (`--ale`, `--no-clip-list`); volba „Denní report po každé kartě“ (`outputs.dayReport`, zapnuto), CLI `--no-day-report`; `copy`/`append` vypíšou „Denní report: …“
- Úkol 7 — denní report `<cíl>\<datum>\_REPORT\<datum>_day_report.html/.pdf` (A4 na šířku, bez náhledů): dlaždice karty, soubory, data, klipy, celková délka, ověřené karty „N / M“; tabulka karet (čas, soubory, data, klipy, délka, kamery, ověření, chyby, proxy hotovo/selhalo, LUTy, počet generací); sekce Problémy (selhané soubory + prvních 5 cest, neověřené, nečitelné reporty, složka bez reportu „bez reportu (probíhá, nebo byl přerušen)“) a LUTy; denní seznam klipů `<datum>_clips.csv/.ale` přes všechny karty; složka `_REPORT` dne se za kartu nepovažuje (ani pro Ověřit zálohu)
````

- [ ] **Step 4: Závěrečná kontrola (bez GUI)**

Run (z kořene repa, každý zvlášť):

```bash
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
dotnet test windows\Offload.sln
git diff e2452f9 --stat -- mac/
git grep -n "0\.2\.0-win" -- windows/src windows/tests
```

Expected: build 0 varování a 0 chyb; obě sady zelené (~970 úspěšných, 7 přeskočených — BRAW vzorky; s ffmpeg a Edge se `[FfmpegFact]`/`[EdgeFact]` nepřeskakují); `mac/` bez jediné změny (prázdný výstup); `0.2.0-win` už nikde v kódu ani testech (prázdný výstup). Pak ověřit CLI bez karty: `windows\src\Offload.Cli\bin\Debug\net10.0-windows\offload.exe day-report --help` vypíše popis příkazu a `offload.exe copy --help` obsahuje `--hash`, `--ale`, `--no-clip-list`, `--no-day-report`. `Offload.exe` se nespouští.

- [ ] **Step 5: Commit**

Zpráva do `.git/COMMIT_MSG_PLAN7`:

```text
Docs: plan 7 (outputs for production and editing), version 0.3.0

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/FORMATY.md docs/ROZDILY.md docs/VYVOJ-MAC.md docs/VYVOJ-WINDOWS.md windows/Directory.Build.props windows/README.md windows/src/Offload.Core/Format.cs windows/tests/Offload.Core.Tests/FormatTests.cs windows/tests/Offload.Core.Tests/HtmlReportTests.cs windows/tests/Offload.Core.Tests/ReportTests.cs
git commit -F .git/COMMIT_MSG_PLAN7
```

- [ ] **Step 6: Závěrečná revize celé větve (řídicí relace)**

Revizor (nejschopnější model) projde `git diff e2452f9..HEAD` proti návrhu §3, §5 a §6.2 a proti Review Focus výše; zvlášť: (1) doplnění pořád nepřepíše žádný neodvozený soubor (`AppendEngineTests.Rewritten` obsahuje jen `.xxh64`, řetěz, `_clips.csv`); (2) `hashAlgorithm` je v každém reportu `xxh64` a `.xxh64` vzniká vždy (R16); (3) žádné UI vlákno nečeká na disk (`MainViewModel.DayReport.cs` jen `await Task.Run`); (4) testy nepíšou mimo `TempDir` / přesměrovanou historii; (5) burn-in výchozí beze změny (R15). Nálezy opraví další commit se stejnými pravidly. Ruční test plánu 7 (README, body 21–27) zůstává na uživateli — GUI se nespouští.
