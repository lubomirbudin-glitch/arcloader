# Offload pro Windows — plán 8: nastavení, oznámení, distribuce (úkoly 1–6) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Skupina C návrhu 15 vylepšení — projekty (pojmenovaná nastavení), vlastní pravidla LUTů a převod BRAW Film Gen 4 → Gen 5, oznámení (Windows a ntfy), kontrola aktualizací a podepsaný, verzovaný balíček; výsledek je verze 0.4.0.

**Architecture:** `AppSettings` se rozdělí na `SettingsProfile` (klíče projektu) a globální část; horní úroveň `settings.json` je profil „Bez projektu“, `projects[]` pojmenované profily (R17) a `MainViewModel.Settings` je vždy *aktivní* profil, takže dnešní vazby GUI fungují beze změny a úloha si bere klon jako dosud. Projekt jde do `JobConfig` → historie, reportu (`project`) a denního reportu. Pravidla LUTů jsou čisté funkce (`LutRules`) volané z `LutResolver` před automatikou; převod BRAW je volba `braw-decode --gen5`. Oznámení: čistá tabulka `JobOutcomes` + `NtfyClient` (HTTP POST) v Core, `TrayNotifier` (`Shell_NotifyIconW`) v App. Aktualizace: `UpdateChecker` (manifest z URL nebo souboru, 1× za 24 h). Distribuce: verze jen v `Directory.Build.props`, `scripts\release.ps1` (manifest, signtool) dot-sourcovaný z `build.ps1`.

**Tech Stack:** C# / .NET 10, WPF, xUnit, BCL (`HttpClient`, `HttpListener` jen v testech, `System.Text.Json`, `X509Certificate2`, `SHA1`), P/Invoke `shell32` (`Shell_NotifyIconW`, `ExtractIconExW`), Blackmagic RAW SDK přes COM (`braw-decode`), Windows PowerShell 5.1 (`build.ps1`, `release.ps1`; testy ho spouštějí s falešným `signtool.cmd`), ffmpeg 8.1 jen v testech `[FfmpegFact]`.

**Spec:** `windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md` — skupina C (§4.1–4.5), průřezové věci (§5), plán 8 (§6.3). Rozhodnutí řídicí relace platí beze změny: **R15** (burn-in výchozí zapnutý) a **R16** (MD5 jen navíc k xxh64). Plán má dvě části: tento soubor (úkoly 1–6) a `windows/docs/superpowers/plans/2026-10-07-offload-windows-8-nastaveni-2.md` (úkoly 7–12).

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

## Review Focus

Návrh v §6.3 jmenuje pět vstupů s největším rizikem; každý má v plánu test v úkolu, který vlastní ten kód:

1. **Přepnutí projektu, když jsou úlohy ve frontě a běží render proxy** → zařazená úloha dokončí cíle, kodek, LUT, pravidla i oznámení projektu, ve kterém byla spuštěna; do reportu a historie zapíše ten projekt. Testy: úkol 1 `ProjectsTests.SwitchThereAndBack_LosesNothing`, `ClonedProfileSections_AreIndependentOfTheSwitch`; úkol 2 `ProjectJobTests.Project_IntoHistoryAndReport`, `Append_KeepsTheEntrysProject`; úkol 9 `CliNtfyTests.JobConfig_NotifyIsTheJobsOwnCopy`; v GUI ruční test 32 (úkol 12).
2. **Ručně upravený / starší `settings.json`** (duplicitní projekty, neznámé klíče, chybějící sekce, poškozená sekce `projects`, aktivní projekt, který není) → aplikace vždy naběhne, platí poslední stejné jméno, neplatná položka se vynechá, neznámé klíče se zapíšou zpět, ostatní nastavení zůstane. Testy: úkol 1 `ProjectsTests.HandEdited_DuplicatesInvalidMissingUnknown`, `ActiveProjectMissing_IsNoProject`, `ProjectsNotAList_RestKept_RawUntilAProjectIsSaved`, `NoProjects_WritesEmptyListAndNull_OlderFileWithoutThemLoads`; úkol 5 `LutRulesTests.InvalidRules_DroppedOnLoad_ValidKeptInOrder`; úkol 10 `UpdateCheckerTests.Setting_Updates_GlobalAndKept`.
3. **ntfy server nedostupný, pomalý, TLS chyba, téma s diakritikou** → úloha nikdy nečeká (odeslání běží zvlášť a jen loguje), pomalý server se vzdá po 10 s, chyba je výsledek (ne výjimka), téma s diakritikou se vůbec neodešle a dialog řekne česky proč. Testy: úkol 8 `NtfyClientTests.SilentServer_GivesUpAfterTheLimit`, `NothingListening_IsAResult`, `TlsFailure_IsAResult`, `ServerError_IsAResult_NotAnException`, `BadTopic_RefusedLocally`, `SendOutcome_DoesNotHoldTheCaller_LogsTheResult`; úkol 9 `CliNtfyTests.DialogCheck_ServerAndTopic_OnlyWhenOn`.
4. **Manifest na odpojené UNC cestě, poškozený JSON, verze `0.10.0` vs `0.9.0`, hodiny posunuté zpět** → čtení souboru se vzdá po 5 s (okno nezamrzne), poškozený JSON i chybějící verze jsou „Manifest nejde načíst: …“, verze se porovnávají číselně, `lastCheck` v budoucnosti znamená zkontrolovat hned. Testy: úkol 10 `UpdateCheckerTests.File_ThatDoesNotAnswer_GivesUp` (pojmenovaná roura, která neodpoví — jako mrtvá UNC cesta), `UpToDate_Off_Damaged`, `IsNewer_Table`, `OncePerDay_FakeClock_ClockBack_Force`, `Http_Silent_Or_404_Failed`.
5. **`build.ps1 -Sign` bez signtoolu, s prošlým certifikátem, s nedostupným razítkem** → build skončí hned na začátku s českou hláškou (signtool, otisk, prošlý certifikát, chybějící heslo), razítko se zkusí 3× a pak jasná chyba, heslo se nikdy nevypíše. Testy: úkol 11 `ReleaseScriptTests.NoSigntool_ClearError`, `Certificate_Missing_Unknown_Expired_NoPassword`, `Timestamp_ThreeAttempts_ThenClearError`, `Sign_ThenVerify_PasswordNeverPrinted`, `VerifyFails_ClearError_MissingFileRefusedFirst`; skutečný signtool s certifikátem je ruční test 39.

## Rozhodnutí tam, kde návrh nestačil (platí pro oba soubory plánu)

- **`project` v reportu karty:** §3.2 chce projekt v hlavičce denního reportu a ten čte jen reporty na cíli, §5.3 ale klíč nejmenuje. Přidává se `offload_report.json` → `project` (string, chybí bez projektu; Mac ignoruje). `CardRecord.Project` = první generace, která projekt uvádí. Denní report ukáže „Projekt: X“, jen když ho mají **všechny** karty s čitelným reportem stejný (bez ohledu na velikost písmen).
- **Doplnění a projekt:** záznam historie si projekt nechá (doplnění ho nemění). GUI dává doplnění projekt dřívější zálohy (`known.Entry.Project`), CLI `append` projekt z `--project`, jinak žádný.
- **Další v řadě** porovnává projekt bez ohledu na velikost písmen. Přejmenování projektu historii nepřepisuje (je to záznam), takže další v řadě pro přejmenovaný projekt začne znovu — v návodu.
- **`MainViewModel.Settings`** je od úkolu 3 *aktivní profil* (`SettingsProfile`); celé `settings.json` je `AllSettings` (`Tools`, `PortNames`, `Updates`, `Projects`). Dnešní vazby GUI na `Settings.Proxy`, `Settings.DestRoots` … tak bez úprav edituje aktivní projekt.
- **`projects`, které není pole:** zachová se beze změny (R24), dokud se v aplikaci neuloží první projekt — pak se přepíše novým seznamem. Položka bez platného jména (prázdné, > 60 znaků, „Bez projektu“) se při načtení vynechá (Trace).
- **CLI `--project`:** výslovně zadané volby vyhrávají (`ParseResult.GetResult(option) is { Implicit: false }`); bez `--project` se nic nemění (každá volba platí jako dosud, i výchozí hodnoty). Projekt s `naming.suggestFromClips` = true se chová jako `--name auto` (návrh z klipů, jinak jmenovka), `--name` vyhrává. Přepínače jsou jednosměrné jako dosud (`--no-verify`, `--proxy` …) — projekt s vypnutým ověřením z CLI zapnout nejde; `--lut-rule` nahradí pravidla projektu celá. Chybí-li vlastní LUT projektu (nebo `.cube` z pravidla), CLI skončí před kopií stejnou hláškou jako GUI.
- **Neplatné pravidlo LUTu** (prázdná hodnota, obojí / nic z `lut` a `lutPath`, **neznámé `match`**) se při načtení zahodí (§4.2) — u neznámého `match` z budoucí verze je to výjimka z R24, vědomě (návrh to tak chce, editor by takový řádek neuměl ukázat).
- **Pravidlo s „Bez LUT“** dá `lut.source = "none"` (ne `rule`), důvod „pravidlo 2: …“ — denní report a seznamy klipů ho tak berou jako klip bez LUTu. Pravidlo s vestavěným id, které v katalogu není, dá „bez LUT“ s důvodem „… — vestavěný LUT nenalezen“ (nepropadne do automatiky).
- **Vzor složky:** `*` = libovolné znaky včetně `/`, `?` = jeden znak, bez ohledu na velikost písmen, celý text `<karta>/<složka klipu>` (klip v kořeni karty = jen `<karta>`).
- **Sériové číslo kamery:** Sony `M01.XML` (`Device serialNo`) a ffprobe tagy `com.apple.quicktime.camera.identifier` / `camera_serial_number`. ARRI (art-cmd) a BRAW (SDK) **ne** — názvy metadat ověří až vzorky (§4.2 „ověří vzorky“); do té doby u nich pravidlo `serial` nesedí.
- **`--gen5`:** `ProxyRenderer` s `proxy.brawGen4ToGen5` nahradí výchozí BRAW backend backendem s `--gen5` (pro `--info` i snímky). Klip, který už je Gen 5, zůstane beze změny a `--info` nemá `colorScienceGenOriginal`; neznámá generace nebo SDK, které 5 nepřijme (nebo po zápisu čte jinou hodnotu), = varování na stderr a dekódování jako dosud.
- **Výsledek úlohy (`JobOutcomes`):** doplnění „Nic nového“ = Hotovo (s varováním Pozor); stage 2 spadla, ale kopie je v pořádku = Pozor „Kopie je ověřená, ale zpracování po kopii selhalo.“; „kartu lze vysunout“ bez proxy říká „Klipy se ještě zpracovávají.“; selhané náhledy se nepočítají (`JobStatus` je nezná). Text pro telefon je u varování obecný („… vyžaduje pozornost — podrobnosti v aplikaci.“), protože varování mohou jmenovat disk nebo složku.
- **ntfy:** téma se ověří místně (písmena bez diakritiky, číslice, `-`, `_`, nejvýš 64 — to ntfy přijímá), takže téma s diakritikou se neodešle. JSON je UTF-8 bez `\u` escapů. CLI po skončení úlohy na odeslání počká nejvýš 10 s (proces by jinak skončil) a pošle i odmítnutí před kopií.
- **Ikona v oznamovací oblasti** se přidá až s prvním oznámením (ne při startu) a zmizí při zavření; po restartu Průzkumníka (`TaskbarCreated`) ji další oznámení přidá znovu. Oznámení používá nastavení oznámení *úlohy* (`JobConfig.Notify`, klon při startu).
- **Kontrola aktualizací:** `update-check.json` si pamatuje i `lastUrl` a `lastNotes`, aby odkaz „Nová verze“ ukázal i v těch 24 h, kdy se manifest nečte; `lastCheck` se zapíše i po neúspěchu (jednou denně, ne při každém startu); poškozený stav = zkontrolovat znovu.
- **Podpis:** `scripts\release.ps1` (dot-sourcovaný z `build.ps1`, UTF-8 s BOM kvůli češtině ve Windows PowerShell 5.1) ověří signtool a certifikát **před** buildem; prošlý certifikát (úložiště i PFX) odmítne; heslo v chybách nahradí `***`. Návrh chtěl skript jen ručně — testy navíc pouštějí `release.ps1` ve Windows PowerShell proti falešnému `signtool.cmd` (Review Focus 5); skutečný podpis zůstává ruční test.
- **Existující test** `SettingsStoreTests.UnknownKeys_KeptByClone_NoneForFreshSettings` používal `lutRules` a `updates` jako neznámé klíče — od plánu 8 jsou známé; úkol 5 ho přepne na `futureRules` / `futureTop`.

## Struktura souborů

| Soubor | Úkol | Odpovědnost |
|---|---|---|
| `Offload.Core/Desktop/AppSettings.cs` | 1, 5, 10 | `SettingsProfile` (klíče projektu), `ProjectSettings`, `AppSettings` (globální + `projects`, `activeProject`, `Active`), `SettingsStore` čte/zapisuje profily po sekcích; `ProxyPreflight` + `LutFile` (soubory LUTů z pravidel) |
| `Offload.Core/Desktop/Projects.cs` | 1 | jména projektů, uložit jako / přejmenovat / smazat / přepnout, texty |
| `Offload.Core/Notify/NotifySettings.cs` | 1 | `notify` (`windows`, `ntfy`) |
| `Offload.Core/OffloadJob.cs`, `Models.cs`, `OffloadReport.cs`, `CardRecord.cs`, `Outputs/DayReport.cs`, `ReelNames.cs`, `Desktop/CardNaming.cs` | 2 | projekt úlohy → historie, report, denní report, další v řadě |
| `Offload.App/MainViewModel*.cs`, `ProjectNameWindow.xaml(.cs)`, `MainWindow.xaml(.cs)`, `SetupPanel.xaml` | 3 | přepínač projektů, menu *Projekt*, dialog jména, titulek |
| `Offload.Cli/CliProject.cs`, `CopyCommand.cs`, `AppendCommand.cs`, `StageTwoOptions.cs` | 4, 6, 7, 9 | `--project`, `--lut-rule`, `--braw-gen5`, `--ntfy` |
| `Offload.Core/LutRules.cs` | 5, 6 | `LutRule`, `LutRules` (shoda, důvod, LUT pravidla, parsování CLI, kontrola editoru) |
| `Offload.Core/Luts.cs`, `Media.cs`, `ClipDetector.cs`, `ProxyRenderer.cs` | 5, 7 | pravidla v `LutResolver`, `CameraSerial`, LUT podle cesty, BRAW Gen 4 → 5 |
| `Offload.App/LutRulesWindow.xaml(.cs)`, `LutRulesViewModel.cs`, `ProxyOptionsViewModel.cs` | 6, 7 | editor pravidel, tlačítko, volba Gen 4 → 5 |
| `Offload.BrawDecode/*` | 7 | `--gen5` |
| `Offload.Core/Notify/JobOutcome.cs`, `NtfyClient.cs` | 8 | výsledek úlohy a texty, ntfy |
| `Offload.App/TrayNotifier.cs`, `MainViewModel.Notify.cs`, `NotifyWindow.xaml(.cs)`, `Offload.Cli/CliNtfy.cs` | 9 | oznámení Windows, dialog Oznámení, CLI `--ntfy` |
| `Offload.Core/Updates/UpdateChecker.cs`, `Offload.App/MainViewModel.Updates.cs`, `UpdateWindow.xaml(.cs)` | 10 | kontrola aktualizací |
| `windows/scripts/release.ps1`, `build.ps1`, `Offload.Core/Format.cs` | 11 | verze z props, verzovaný zip, manifest, podpis |
| `tests/.../LocalHttp.cs` | 8 | falešný HTTP server pro ntfy a aktualizace |
| docs (`FORMATY`, `ROZDILY`, `VYVOJ-*`, `README`, návod) | 11, 12 | dokumentace, 0.4.0 |

---

### Task 1: Profily a projekty v `settings.json`

Spec §4.1 (úložiště, R17), §5.1, §5.4 (R24), §6.3 úkol 1. Horní klíče profilu = „Bez projektu“, `projects[]` = pojmenované profily stejného tvaru, `activeProject`. Každý profil se čte po sekcích jako dnes horní úroveň (sekce, kterou verze nepřečte, zůstane syrová a zapíše se zpět). V tomto úkolu se GUI nemění — `AppSettings` dál má všechny vlastnosti (zdědí je ze `SettingsProfile`).

**Files:**
- Modify: `windows/src/Offload.Core/Desktop/AppSettings.cs:1-187` (celý začátek až po konec `SettingsStore`)
- Create: `windows/src/Offload.Core/Desktop/Projects.cs`
- Create: `windows/src/Offload.Core/Notify/NotifySettings.cs`
- Test: `windows/tests/Offload.Core.Tests/ProjectsTests.cs` (nový)
- Modify: `docs/VYVOJ-WINDOWS.md:22` (nová sekce plánu 8)

**Interfaces:**
- Consumes: `SettingsStore.Load/Save/Clone`, `LutCatalog`, `ExternalTools`, `OutputSettings` (vše existuje).
- Produces: `class SettingsProfile` (`DestRoots`, `Verify`, `AnalyzeClips`, `Proxy`, `Naming`, `Hash`, `Outputs`, `Notify`, `Extra`, internal `RawSections`); `sealed class ProjectSettings : SettingsProfile { string Name }`; `AppSettings : SettingsProfile` + `Tools`, `PortNames`, `List<ProjectSettings> Projects`, `string? ActiveProject`, `SettingsProfile Active`; `Offload.Core.Notify.NotifySettings { bool Windows = true; NtfySettings Ntfy }`, `NtfySettings { bool Enabled; string Server = "https://ntfy.sh"; string Topic = ""; const DefaultServer }`; `static class Projects` — `NoProject`, `MaxNameLength`, `NameProblem(string?, IEnumerable<string>, string? except = null) → string?`, `Find(AppSettings, string?) → ProjectSettings?`, `SaveAs(AppSettings, string) → ProjectSettings`, `Rename(AppSettings, string, string)`, `Delete(AppSettings, string)`, `Activate(AppSettings, string?)`, `CopyProfile(SettingsProfile, SettingsProfile)`, `ActiveName(AppSettings) → string?`, `WindowTitle(string?)`, `DeleteQuestion(string)`, `Unknown(string, AppSettings)`.

- [ ] **Step 1: Padající testy**

Vytvořit `windows/tests/Offload.Core.Tests/ProjectsTests.cs`:

```csharp
using System.Text.Json;
using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.Core.Tests;

/// Spec 4.1, R17: projects are named profiles in settings.json; the top level is "Bez projektu".
public class ProjectsTests
{
    static string Write(TempDir dir, string json)
    {
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void RoundTrip_ProjectsAndActive_EveryProfileKey()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var s = new AppSettings();
        s.DestRoots.Add(@"D:\Bez");
        var film = Projects.SaveAs(s, "  Film X ");
        film.DestRoots = [@"E:\Film", @"F:\Film"];
        film.Verify = false;
        film.AnalyzeClips = false;
        film.Hash = "both";
        film.Naming.SuggestFromClips = false;
        film.Outputs.ClipListAle = true;
        film.Notify.Ntfy.Enabled = true;
        film.Notify.Ntfy.Topic = "film-x-tajne";
        film.Proxy.Enabled = true;
        film.Proxy.ExtraFolder = @"G:\Proxy";
        SettingsStore.Save(s, path);

        var again = SettingsStore.Load(path);
        Assert.Equal("Film X", again.ActiveProject);
        Assert.Equal([@"D:\Bez"], again.DestRoots);
        var p = Assert.Single(again.Projects);
        Assert.Same(p, again.Active);
        Assert.Equal("Film X", p.Name);
        Assert.Equal([@"E:\Film", @"F:\Film"], p.DestRoots);
        Assert.False(p.Verify);
        Assert.False(p.AnalyzeClips);
        Assert.Equal("both", p.Hash);
        Assert.False(p.Naming.SuggestFromClips);
        Assert.True(p.Outputs.ClipListAle);
        Assert.True(p.Notify.Ntfy.Enabled);
        Assert.Equal("film-x-tajne", p.Notify.Ntfy.Topic);
        Assert.Equal(NtfySettings.DefaultServer, p.Notify.Ntfy.Server);
        Assert.True(p.Proxy.Enabled);
        Assert.Equal(@"G:\Proxy", p.Proxy.ExtraFolder);
        // the top level keeps its own values
        Assert.True(again.Verify);
        Assert.False(again.Proxy.Enabled);
        // the name comes first in the project's object (readable by hand)
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("name", doc.RootElement.GetProperty("projects")[0].EnumerateObject().First().Name);
    }

    // Spec 4.1 "přepnutí tam a zpět beze ztráty": each profile keeps its own values.
    [Fact]
    public void SwitchThereAndBack_LosesNothing()
    {
        var s = new AppSettings();
        s.DestRoots.Add(@"D:\A");
        Projects.SaveAs(s, "Film X");
        Assert.Equal([@"D:\A"], s.Active.DestRoots);            // a copy of what was shown
        s.Active.DestRoots.Add(@"E:\B");
        s.Active.Proxy.Height = 720;

        Projects.Activate(s, null);
        Assert.Same(s, s.Active);
        Assert.Equal([@"D:\A"], s.Active.DestRoots);
        Assert.Equal(1080, s.Active.Proxy.Height);

        Projects.Activate(s, "film x");                          // any case
        Assert.Equal("Film X", s.ActiveProject);
        Assert.Equal([@"D:\A", @"E:\B"], s.Active.DestRoots);
        Assert.Equal(720, s.Active.Proxy.Height);

        Projects.Activate(s, Projects.NoProject);
        Assert.Null(s.ActiveProject);
    }

    [Fact]
    public void SaveAs_CopiesDeep_NotShared()
    {
        var s = new AppSettings();
        s.Proxy.Enabled = true;
        var p = Projects.SaveAs(s, "Film X");
        p.Proxy.Enabled = false;
        p.Notify.Windows = false;
        Assert.True(s.Proxy.Enabled);
        Assert.True(s.Notify.Windows);
        Assert.NotSame(s.Outputs, p.Outputs);
        Assert.NotSame(s.Naming, p.Naming);
    }

    [Fact]
    public void Rename_Active_StaysActive_Delete_Active_GoesToNoProject()
    {
        var s = new AppSettings();
        Projects.SaveAs(s, "Film X");
        Projects.Activate(s, null);
        Projects.SaveAs(s, "Reklama Y");

        Projects.Rename(s, "reklama y", "Reklama Z");
        Assert.Equal("Reklama Z", s.ActiveProject);
        Projects.Rename(s, "Reklama Z", "reklama z");            // only the case changes: allowed
        Assert.Equal("reklama z", s.ActiveProject);

        Projects.Delete(s, "Film X");                             // not the active one
        Assert.Equal("reklama z", s.ActiveProject);
        Projects.Delete(s, "reklama z");
        Assert.Null(s.ActiveProject);
        Assert.Empty(s.Projects);
    }

    [Theory]
    [InlineData("", "Název nesmí být prázdný.")]
    [InlineData("   ", "Název nesmí být prázdný.")]
    [InlineData("bez PROJEKTU", "„Bez projektu“ je vyhrazený název.")]
    [InlineData("film x", "Projekt s tímto názvem už existuje.")]
    [InlineData(" Film X ", "Projekt s tímto názvem už existuje.")]
    public void Names_Refused_InCzech(string name, string message)
    {
        var s = new AppSettings();
        Projects.SaveAs(s, "Film X");
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => Projects.SaveAs(s, name)).Message);
        Assert.Single(s.Projects);
    }

    [Fact]
    public void Names_LengthLimit_AndRenameOntoAnother()
    {
        var s = new AppSettings();
        Projects.SaveAs(s, new string('x', 60));
        Assert.Equal("Název může mít nejvýš 60 znaků.", Assert.Throws<ArgumentException>(() => Projects.SaveAs(s, new string('y', 61))).Message);
        Projects.SaveAs(s, "B");
        Assert.Equal("Projekt s tímto názvem už existuje.", Assert.Throws<ArgumentException>(() => Projects.Rename(s, "B", new string('x', 60))).Message);
        Assert.Equal("Projekt „Nic“ neexistuje. Projekty: " + new string('x', 60) + ", B.", Assert.Throws<ArgumentException>(() => Projects.Activate(s, "Nic")).Message);
        Assert.Equal("Projekt „Nic“ neexistuje. Žádný projekt není uložený.", Projects.Unknown("Nic", new AppSettings()));
    }

    // Review focus 2: a hand-edited file — the same name twice (the last wins), an invalid entry, missing sections,
    // keys of a newer version inside a project, an active project that is not there.
    [Fact]
    public void HandEdited_DuplicatesInvalidMissingUnknown()
    {
        using var dir = new TempDir();
        var path = Write(dir, """
            {"destRoots":["D:\\Bez"],"activeProject":"FILM X",
             "projects":[
               {"name":"Film X","destRoots":["E:\\Old"]},
               7, {"destRoots":["X:\\NoName"]}, {"name":"  "}, {"name":"Bez projektu"},
               {"name":"film x","destRoots":["E:\\New"],"proxy":{"codec":"av1"},"future":{"a":1}},
               {"name":"Reklama"}
             ]}
            """);
        var s = SettingsStore.Load(path);
        Assert.Equal(["film x", "Reklama"], s.Projects.Select(p => p.Name));
        Assert.Equal("film x", s.ActiveProject);                       // canonical name of the match
        var film = s.Projects[0];
        Assert.Equal([@"E:\New"], film.DestRoots);
        Assert.Equal(ProxyCodec.H264, film.Proxy.Codec);                // unparseable section: defaults …
        var reklama = s.Projects[1];
        Assert.True(reklama.Verify);                                    // missing sections: defaults
        Assert.Equal(1080, reklama.Proxy.Height);

        SettingsStore.Save(s, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var saved = doc.RootElement.GetProperty("projects")[0];
        Assert.Equal("av1", saved.GetProperty("proxy").GetProperty("codec").GetString());   // … and kept raw
        Assert.Equal(1, saved.GetProperty("future").GetProperty("a").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("projects").GetArrayLength());
    }

    [Fact]
    public void ActiveProjectMissing_IsNoProject()
    {
        using var dir = new TempDir();
        var s = SettingsStore.Load(Write(dir, """{"activeProject":"Gone","projects":[{"name":"Film X"}]}"""));
        Assert.Null(s.ActiveProject);
        Assert.Same(s, s.Active);
    }

    // Spec 4.1: a damaged `projects` section → no projects, the rest of the settings stays; it is written back until
    // a project is saved in the app.
    [Fact]
    public void ProjectsNotAList_RestKept_RawUntilAProjectIsSaved()
    {
        using var dir = new TempDir();
        var path = Write(dir, """{"verify":false,"projects":{"oops":true},"activeProject":"Film X"}""");
        var s = SettingsStore.Load(path);
        Assert.False(s.Verify);
        Assert.Empty(s.Projects);
        Assert.Null(s.ActiveProject);

        SettingsStore.Save(s, path);
        Assert.Contains("\"oops\": true", File.ReadAllText(path));

        var again = SettingsStore.Load(path);
        Projects.SaveAs(again, "Film X");
        SettingsStore.Save(again, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("projects").ValueKind);
        Assert.Equal("Film X", SettingsStore.Load(path).ActiveProject);
    }

    [Fact]
    public void NoProjects_WritesEmptyListAndNull_OlderFileWithoutThemLoads()
    {
        using var dir = new TempDir();
        var s = SettingsStore.Load(Write(dir, """{"verify":false}"""));
        Assert.Empty(s.Projects);
        Assert.Null(s.ActiveProject);
        var path = Path.Combine(dir.Path, "out.json");
        SettingsStore.Save(s, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(0, doc.RootElement.GetProperty("projects").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("activeProject").ValueKind);
    }

    // Spec 4.3, 5.1: notify defaults; a blank server falls back to ntfy.sh, the topic is trimmed.
    [Fact]
    public void Notify_DefaultsAndNormalize()
    {
        using var dir = new TempDir();
        var fresh = SettingsStore.Load(Path.Combine(dir.Path, "none.json"));
        Assert.True(fresh.Notify.Windows);
        Assert.False(fresh.Notify.Ntfy.Enabled);
        Assert.Equal("https://ntfy.sh", fresh.Notify.Ntfy.Server);
        Assert.Equal("", fresh.Notify.Ntfy.Topic);
        var s = SettingsStore.Load(Write(dir, """{"notify":{"windows":false,"ntfy":{"enabled":true,"server":" ","topic":"  abc "}}}"""));
        Assert.False(s.Notify.Windows);
        Assert.True(s.Notify.Ntfy.Enabled);
        Assert.Equal("https://ntfy.sh", s.Notify.Ntfy.Server);
        Assert.Equal("abc", s.Notify.Ntfy.Topic);
    }

    // The queue keeps a clone (spec 4.1): a job's settings never change when the project is switched later.
    [Fact]
    public void ClonedProfileSections_AreIndependentOfTheSwitch()
    {
        var s = new AppSettings();
        Projects.SaveAs(s, "Film X");
        s.Active.Proxy.Height = 720;
        var queued = SettingsStore.Clone(s.Active.Proxy);
        Projects.Activate(s, null);
        s.Active.Proxy.Height = 540;
        Assert.Equal(720, queued.Height);
        Assert.Equal(720, s.Projects[0].Proxy.Height);
    }

    [Fact]
    public void WindowTitleAndDeleteQuestion()
    {
        Assert.Equal("Offload", Projects.WindowTitle(null));
        Assert.Equal("Offload — Film X", Projects.WindowTitle("Film X"));
        Assert.Equal("Smazat projekt Film X? Nastavení projektu se ztratí, zálohy na discích zůstanou.", Projects.DeleteQuestion("Film X"));
    }
}
```


- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ProjectsTests"`
Expected: FAIL — build testů selže (CS0246 / CS0103: `Projects`, `NtfySettings`, `ProjectSettings` neexistují).

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 1–187 — celý začátek souboru od `using System.Text.Json;` po `}` konce třídy `SettingsStore` (třída `ProxyPreflight` od řádku 189 zůstává) nahradit tímto:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Offload.Core.Notify;
using Offload.Core.Outputs;

namespace Offload.Core.Desktop;

/// "naming" in settings.json (spec 5.1).
public sealed class NamingSettings
{
    /// Propose the card folder name from the clip names ("A001C003…" → "A001") or the next in line (spec 2.3).
    [JsonPropertyName("suggestFromClips")] public bool SuggestFromClips { get; set; } = true;

    /// Keys of this section a newer version wrote: kept and written back (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// What a project holds (spec 4.1, R17): the top level of settings.json is the profile "Bez projektu", every entry of
/// `projects` is a named profile of the same shape. Tools, port names and the update check stay global (AppSettings).
public class SettingsProfile
{
    [JsonPropertyName("destRoots")] public List<string> DestRoots { get; set; } = [];
    [JsonPropertyName("verify")] public bool Verify { get; set; } = true;
    [JsonPropertyName("analyzeClips")] public bool AnalyzeClips { get; set; } = true;
    [JsonPropertyName("proxy")] public ProxySettings Proxy { get; set; } = new();
    [JsonPropertyName("naming")] public NamingSettings Naming { get; set; } = new();
    /// "xxh64" (default) or "both" = xxh64 + MD5 (spec 3.5, R16). A value a newer version wrote is kept as it is and
    /// read as xxh64 (HashKinds.FromSetting).
    [JsonPropertyName("hash")] public string Hash { get; set; } = "xxh64";
    /// Clip lists and the day report — spec 3.2, 3.3.
    [JsonPropertyName("outputs")] public OutputSettings Outputs { get; set; } = new();
    /// Windows notification and ntfy — spec 4.3.
    [JsonPropertyName("notify")] public NotifySettings Notify { get; set; } = new();

    /// Keys this version does not know (a newer version wrote them): kept as they are and written back, so going back
    /// to an older version never loses anything (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    /// Sections that did not parse (a value from a newer version): the original JSON, written back by Save unchanged so
    /// the next save never drops them. While one is kept, in-app changes to that section do not persist.
    [JsonIgnore] internal Dictionary<string, JsonElement>? RawSections { get; set; }
}

/// One entry of `projects` (spec 4.1): a named profile.
public sealed class ProjectSettings : SettingsProfile
{
    [JsonPropertyName("name"), JsonPropertyOrder(-1)] public string Name { get; set; } = "";
}

/// The app's settings — the Mac `AppSettings` keys (destRoots, verify, analyzeClips, proxy, tools);
/// `portNames` is Windows only: the user's names for USB sockets (`UsbConnector.Id` → name).
/// The profile keys at the top level are "Bez projektu"; `projects` and `activeProject` hold the named ones (R17).
public sealed class AppSettings : SettingsProfile
{
    [JsonPropertyName("tools")] public ExternalTools Tools { get; set; } = new();
    [JsonPropertyName("portNames")]
    public Dictionary<string, string> PortNames
    {
        get => _portNames;
        set
        {
            // Built by hand: keys that differ only in case (a hand-edited file) must not throw — the last one wins.
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, name) in value ?? []) names[id] = name;
            _portNames = names;
        }
    }
    private Dictionary<string, string> _portNames = new(StringComparer.OrdinalIgnoreCase);

    /// The named profiles (spec 4.1), in the order they were created.
    [JsonPropertyName("projects")] public List<ProjectSettings> Projects { get; set; } = [];
    /// The project in use, or null = "Bez projektu" (the top-level keys).
    [JsonPropertyName("activeProject")] public string? ActiveProject { get; set; }

    /// The profile the app shows and edits now: the active project, else the top level.
    [JsonIgnore] public SettingsProfile Active => Projects.FirstOrDefault(p => p.Name == ActiveProject) as SettingsProfile ?? this;
}

public static class SettingsStore
{
    public static readonly int[] ProxyHeights = [540, 720, 1080, 2160];

    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    /// The keys of a profile (top level and every project) Load reads into properties.
    static readonly string[] ProfileKeys = ["destRoots", "verify", "analyzeClips", "proxy", "naming", "hash", "outputs", "notify"];
    /// The global top-level keys; every other top-level key goes to AppSettings.Extra.
    static readonly string[] GlobalKeys = ["tools", "portNames", "projects", "activeProject"];

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// Missing or corrupt file → defaults; never throws (a settings file must never stop the app from starting).
    /// Each section is read on its own: a section that does not parse (a newer codec name, a wrong type) falls back to
    /// its defaults and the others are kept. A key given twice: the last one wins; so does a project name given twice.
    public static AppSettings Load(string path)
    {
        var s = new AppSettings();
        try
        {
            if (!File.Exists(path)) return s;
            var bytes = File.ReadAllBytes(path);
            // Skip UTF-8 BOM if present (EF BB BF), which Windows PowerShell and old Notepad write
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                bytes = bytes[3..];
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return s;
            var root = Members(doc.RootElement);
            ReadProfile(root, s, GlobalKeys);
            Kept(root, s, "tools", (ExternalTools v) => s.Tools = v, () => new ExternalTools());
            Section(root, "portNames", e =>
            {
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in e.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) names[p.Name] = p.Value.GetString()!;
                s.PortNames = names;
            });
            ReadProjects(root, s);
            Section(root, "activeProject", e => s.ActiveProject = e.GetString());
            return Normalize(s);
        }
        catch (Exception)
        {
            // Unreadable, not JSON, or anything unexpected: defaults.
            return new AppSettings();
        }
    }

    /// The members of a JSON object; a key given twice: the last one wins. Values are cloned (the document is disposed).
    static Dictionary<string, JsonElement> Members(JsonElement obj)
    {
        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject()) members[p.Name] = p.Value.Clone();
        return members;
    }

    /// A plain section: one that does not parse keeps its defaults.
    static void Section(Dictionary<string, JsonElement> obj, string key, Action<JsonElement> read)
    {
        if (!obj.TryGetValue(key, out var e) || e.ValueKind == JsonValueKind.Null) return;
        try
        {
            read(e);
        }
        catch (Exception)
        {
            // This section keeps its defaults.
        }
    }

    /// An object section: one this version does not understand is kept raw and written back as it was.
    static void Kept<T>(Dictionary<string, JsonElement> obj, SettingsProfile owner, string key, Action<T> set, Func<T> fresh) where T : class
    {
        if (!obj.TryGetValue(key, out var e) || e.ValueKind == JsonValueKind.Null) return;
        try { set(e.Deserialize<T>(Json) ?? fresh()); }
        catch (Exception)
        {
            (owner.RawSections ??= [])[key] = e;
            System.Diagnostics.Trace.WriteLine($"settings: section \"{key}\" kept as-is (could not be read)");
        }
    }

    /// The profile keys of `obj` into `p`; any key that is neither a profile key nor in `alsoKnown` goes to p.Extra.
    static void ReadProfile(Dictionary<string, JsonElement> obj, SettingsProfile p, IReadOnlyCollection<string> alsoKnown)
    {
        Section(obj, "destRoots", e => p.DestRoots = e.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList());
        Section(obj, "verify", e => p.Verify = e.GetBoolean());
        Section(obj, "analyzeClips", e => p.AnalyzeClips = e.GetBoolean());
        Section(obj, "hash", e => p.Hash = e.GetString() is { Length: > 0 } h ? h : "xxh64");
        Kept(obj, p, "proxy", (ProxySettings v) => p.Proxy = v, () => new ProxySettings());
        Kept(obj, p, "naming", (NamingSettings v) => p.Naming = v, () => new NamingSettings());
        Kept(obj, p, "outputs", (OutputSettings v) => p.Outputs = v, () => new OutputSettings());
        Kept(obj, p, "notify", (NotifySettings v) => p.Notify = v, () => new NotifySettings());
        foreach (var (key, value) in obj)
            if (!ProfileKeys.Contains(key) && !alsoKnown.Contains(key)) (p.Extra ??= [])[key] = value;
    }

    /// `projects` (spec 4.1): an entry that is no object or has no valid name is left out (logged); a name given twice
    /// (a hand-edited file) — the last entry wins. A `projects` that is no array is kept raw until a project is saved.
    static void ReadProjects(Dictionary<string, JsonElement> root, AppSettings s)
    {
        if (!root.TryGetValue("projects", out var e) || e.ValueKind == JsonValueKind.Null) return;
        if (e.ValueKind != JsonValueKind.Array)
        {
            (s.RawSections ??= [])["projects"] = e;
            System.Diagnostics.Trace.WriteLine("settings: \"projects\" is not a list — kept as-is");
            return;
        }
        foreach (var item in e.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var obj = Members(item);
            var name = obj.TryGetValue("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
            if (Projects.NameProblem(name, []) != null)
            {
                System.Diagnostics.Trace.WriteLine($"settings: project \"{name}\" left out (invalid name)");
                continue;
            }
            var p = new ProjectSettings { Name = name };
            ReadProfile(obj, p, ["name"]);
            s.Projects.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            s.Projects.Add(p);
        }
    }

    internal static AppSettings Normalize(AppSettings s) => Normalize(s, LutCatalog.Default);

    internal static AppSettings Normalize(AppSettings s, LutCatalog catalog)
    {
        s.Tools ??= new ExternalTools();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in s.PortNames ?? [])
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) names[id] = name.Trim();
        s.PortNames = names;
        s.Projects = (s.Projects ?? []).Where(p => p != null).ToList();
        NormalizeProfile(s, catalog);
        foreach (var p in s.Projects) NormalizeProfile(p, catalog);
        // An active project that is not there (deleted by hand, renamed): "Bez projektu"; otherwise its exact name.
        s.ActiveProject = Projects.Find(s, s.ActiveProject)?.Name;
        return s;
    }

    static void NormalizeProfile(SettingsProfile s, LutCatalog catalog)
    {
        s.DestRoots = (s.DestRoots ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        s.Proxy ??= new ProxySettings();
        s.Naming ??= new NamingSettings();
        s.Outputs ??= new OutputSettings();
        s.Notify ??= new NotifySettings();
        s.Notify.Ntfy ??= new NtfySettings();
        if (string.IsNullOrWhiteSpace(s.Notify.Ntfy.Server)) s.Notify.Ntfy.Server = NtfySettings.DefaultServer;
        s.Notify.Ntfy.Topic = (s.Notify.Ntfy.Topic ?? "").Trim();
        var p = s.Proxy;
        if (!ProxyHeights.Contains(p.Height)) p.Height = 1080;
        p.BitrateMbps = Math.Clamp(p.BitrateMbps, 2, 50);
        p.FrameScale = double.IsFinite(p.FrameScale) ? Math.Clamp(p.FrameScale, 0.5, 1.0) : 1.0;
        if (p.FrameAspect is double a && (!double.IsFinite(a) || a <= 0)) p.FrameAspect = null;
        if (string.IsNullOrWhiteSpace(p.LutPath)) p.LutPath = null;
        p.LutMode ??= p.LutPath != null ? LutMode.Custom : LutMode.Auto;
        if (catalog.Luts.Count > 0)   // an empty catalog (luts folder missing) must not erase saved choices
        {
            if (catalog.Find(p.LutBuiltin) == null) p.LutBuiltin = null;
            if (catalog.Find(p.LutFallback) == null) p.LutFallback = null;
            if (p.LutMode == LutMode.Builtin && p.LutBuiltin == null) p.LutMode = LutMode.Auto;
        }
        p.ExtraFolder = string.IsNullOrWhiteSpace(p.ExtraFolder) ? null : p.ExtraFolder.Trim();
    }

    /// Written to a temporary file next to it, then moved over — a crash never leaves a half-written file.
    public static void Save(AppSettings settings, string path)
    {
        NativeIO.Mkdirs(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, Json);
        bool raw = settings.RawSections is { Count: > 0 } || settings.Projects.Any(p => p.RawSections is { Count: > 0 });
        if (raw && JsonNode.Parse(bytes) is JsonObject obj)
        {
            // A `projects` kept raw (it was no list) is written back only while no project was saved in the app.
            MergeRaw(obj, settings.RawSections, except: settings.Projects.Count > 0 ? "projects" : null);
            if (obj["projects"] is JsonArray list)
                for (int i = 0; i < list.Count && i < settings.Projects.Count; i++)
                    if (list[i] is JsonObject project) MergeRaw(project, settings.Projects[i].RawSections, except: null);
            bytes = JsonSerializer.SerializeToUtf8Bytes(obj, Json);
        }
        NativeIO.WriteFileDurably(tmp, bytes);
        NativeIO.Move(tmp, path, replace: true);
    }

    static void MergeRaw(JsonObject obj, Dictionary<string, JsonElement>? raw, string? except)
    {
        foreach (var (key, value) in raw ?? [])
            if (key != except) obj[key] = JsonNode.Parse(value.GetRawText());
    }

    /// A deep copy, so a queued job keeps the settings it was started with.
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Json), Json)!;
}
```

Vytvořit `windows/src/Offload.Core/Desktop/Projects.cs`:

```csharp
namespace Offload.Core.Desktop;

/// Projects (spec 4.1, R17): named profiles in settings.json. The app edits the active profile (AppSettings.Active);
/// these operations change which one that is. Names are trimmed, 1–60 characters, unique regardless of case, and
/// "Bez projektu" is reserved. Every method that takes a name throws ArgumentException with a Czech message.
public static class Projects
{
    public const string NoProject = "Bez projektu";
    public const int MaxNameLength = 60;

    /// Why `name` cannot be a project's name (Czech), or null. `except`: the project being renamed (may keep its name).
    public static string? NameProblem(string? name, IEnumerable<string> existing, string? except = null)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) return "Název nesmí být prázdný.";
        if (n.Length > MaxNameLength) return $"Název může mít nejvýš {MaxNameLength} znaků.";
        if (string.Equals(n, NoProject, StringComparison.OrdinalIgnoreCase)) return $"„{NoProject}“ je vyhrazený název.";
        if (existing.Any(e => string.Equals(e, n, StringComparison.OrdinalIgnoreCase) && !string.Equals(e, except, StringComparison.OrdinalIgnoreCase)))
            return "Projekt s tímto názvem už existuje.";
        return null;
    }

    /// The project of that name regardless of case; null for null, "Bez projektu" or an unknown name.
    public static ProjectSettings? Find(AppSettings s, string? name) =>
        name == null ? null : s.Projects.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// "Uložit jako projekt…": a new project with the values shown now (the active profile); it becomes the active one.
    public static ProjectSettings SaveAs(AppSettings s, string name)
    {
        if (NameProblem(name, s.Projects.Select(p => p.Name)) is string problem) throw new ArgumentException(problem);
        var project = new ProjectSettings { Name = name.Trim() };
        CopyProfile(s.Active, project);
        s.Projects.Add(project);
        s.ActiveProject = project.Name;
        return project;
    }

    /// "Přejmenovat…" of a project; the active one stays active under its new name.
    public static void Rename(AppSettings s, string name, string newName)
    {
        var project = Find(s, name) ?? throw new ArgumentException(Unknown(name, s));
        if (NameProblem(newName, s.Projects.Select(p => p.Name), except: project.Name) is string problem) throw new ArgumentException(problem);
        bool active = s.ActiveProject == project.Name;
        project.Name = newName.Trim();
        if (active) s.ActiveProject = project.Name;
    }

    /// "Smazat…": the project's settings are gone (backups on the disks stay); the active one → "Bez projektu".
    public static void Delete(AppSettings s, string name)
    {
        var project = Find(s, name) ?? throw new ArgumentException(Unknown(name, s));
        s.Projects.Remove(project);
        if (s.ActiveProject == project.Name) s.ActiveProject = null;
    }

    /// Switches to a project, or to "Bez projektu" for null / "Bez projektu". Nothing is copied: each profile keeps
    /// its own values, so switching there and back loses nothing.
    public static void Activate(AppSettings s, string? name)
    {
        if (name == null || string.Equals(name.Trim(), NoProject, StringComparison.OrdinalIgnoreCase))
        {
            s.ActiveProject = null;
            return;
        }
        s.ActiveProject = (Find(s, name) ?? throw new ArgumentException(Unknown(name, s))).Name;
    }

    /// A deep copy of the profile values (not the keys only a newer version knows — they belong to the profile they came with).
    public static void CopyProfile(SettingsProfile from, SettingsProfile to)
    {
        to.DestRoots = [.. from.DestRoots];
        to.Verify = from.Verify;
        to.AnalyzeClips = from.AnalyzeClips;
        to.Hash = from.Hash;
        to.Proxy = SettingsStore.Clone(from.Proxy);
        to.Naming = SettingsStore.Clone(from.Naming);
        to.Outputs = SettingsStore.Clone(from.Outputs);
        to.Notify = SettingsStore.Clone(from.Notify);
    }

    /// The project's name, or null for "Bez projektu" — what a job, history and the report record.
    public static string? ActiveName(AppSettings s) => Find(s, s.ActiveProject)?.Name;

    /// The window title: "Offload", or "Offload — Film X".
    public static string WindowTitle(string? project) => project == null ? "Offload" : $"Offload — {project}";

    public static string DeleteQuestion(string name) => $"Smazat projekt {name}? Nastavení projektu se ztratí, zálohy na discích zůstanou.";

    /// The CLI's answer to an unknown --project.
    public static string Unknown(string name, AppSettings s) => s.Projects.Count == 0
        ? $"Projekt „{name}“ neexistuje. Žádný projekt není uložený."
        : $"Projekt „{name}“ neexistuje. Projekty: {string.Join(", ", s.Projects.Select(p => p.Name))}.";
}
```

Vytvořit `windows/src/Offload.Core/Notify/NotifySettings.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Offload.Core.Notify;

/// "notify" in settings.json (spec 4.3, 5.1) — part of a project: where a job's end is announced.
public sealed class NotifySettings
{
    /// A Windows notification (tray balloon) when the main window is not in front — on by default.
    [JsonPropertyName("windows")] public bool Windows { get; set; } = true;
    [JsonPropertyName("ntfy")] public NtfySettings Ntfy { get; set; } = new();

    /// Keys of this section a newer version wrote: kept and written back (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// "notify.ntfy": a push to a phone through an ntfy server (R20) — off by default, no token.
public sealed class NtfySettings
{
    public const string DefaultServer = "https://ntfy.sh";

    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("server")] public string Server { get; set; } = DefaultServer;
    [JsonPropertyName("topic")] public string Topic { get; set; } = "";

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
```


- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ProjectsTests|FullyQualifiedName~SettingsStoreTests"`
Expected: PASS (všechny dosavadní `SettingsStoreTests` beze změny, včetně `UnknownKeys_SurviveLoadAndSave` — `projects` a `activeProject` jsou teď známé klíče a zapíšou se zpět). Pak celá sada: `dotnet build windows\Offload.sln -warnaserror` (0 varování) a `dotnet test windows\Offload.sln` (vše zelené, 1046 testů: 1039 úspěšných + 7 přeskočených).

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádek 22 — najít:

````markdown
## 2026-10-07 — plán 7: výstupy pro produkci a střih
````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 1 — projekty v `settings.json` (R17): horní klíče profilu (`destRoots`, `verify`, `analyzeClips`, `hash`, `naming`, `outputs`, `notify`, `proxy`) jsou „Bez projektu“, `projects[]` pojmenované profily stejného tvaru, `activeProject`; každý profil se čte po sekcích (nečitelná sekce se zachová), neznámé klíče i v projektu zůstanou; jména oříznutá, 1–60 znaků, jedinečná bez ohledu na velikost písmen, „Bez projektu“ vyhrazené, dvakrát stejné jméno → platí poslední, `projects`, které není pole, se zachová; uložit jako / přejmenovat / smazat / přepnout (tam a zpět beze ztráty); nová sekce `notify` (`windows`, `ntfy`)

## 2026-10-07 — plán 7: výstupy pro produkci a střih
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Windows: projects in settings.json (profiles, switch, save as, rename, delete)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/Desktop/AppSettings.cs windows/src/Offload.Core/Desktop/Projects.cs windows/src/Offload.Core/Notify/NotifySettings.cs windows/tests/Offload.Core.Tests/ProjectsTests.cs
git commit -F .git/COMMIT_MSG_PLAN8
```

---

### Task 2: Projekt úlohy v historii, reportu a denním reportu; další v řadě po projektech

Spec §4.1 („Historie zapisuje `project` (§5.2) → další v řadě (§2.3) a denní report podle projektu“), §3.2 (projekt v hlavičce dne), §5.2; TODO plánu 6 v `ReelSuggestion.Next` a odložené pole plánu 7 se tím uzavřou.

**Files:**
- Modify: `windows/src/Offload.Core/OffloadJob.cs:34-37, 137-143, 334-337`
- Modify: `windows/src/Offload.Core/Models.cs:72-75`
- Modify: `windows/src/Offload.Core/OffloadReport.cs:59-62, 120-123`
- Modify: `windows/src/Offload.Core/CardRecord.cs:34-37`
- Modify: `windows/src/Offload.Core/Outputs/DayReport.cs:44-47, 133-137`
- Modify: `windows/src/Offload.Core/ReelNames.cs:67-76`
- Modify: `windows/src/Offload.Core/Desktop/CardNaming.cs:11-17, 20-24, 29-33, 41-45`
- Test: `windows/tests/Offload.Core.Tests/ProjectJobTests.cs` (nový)
- Modify: `docs/VYVOJ-WINDOWS.md:22-23`

**Interfaces:**
- Consumes: `JobConfig`, `OffloadPlan`, `HistoryEntry.Project` (plán 6), `CardRecord`, `DayModel`, `CardNaming.Placeholder/Resolve`, `ReelSuggestion.Next` (existují).
- Produces: `JobConfig.Project` (`string?`), `OffloadPlan.Project` (`string?`, nastavuje `OffloadJob.Prepare`), `OffloadReport.Project` (JSON `project`, chybí při `null`), `CardRecord.Project`, `DayModel.Project`; `CardNaming.Placeholder(…, IEnumerable<string>? taken = null, string? project = null)`, `CardNaming.Resolve(…, string today, string? project = null)`.

- [ ] **Step 1: Padající testy**

Vytvořit `windows/tests/Offload.Core.Tests/ProjectJobTests.cs`:

```csharp
using System.Text.Json;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Spec 4.1: the project a job runs in goes into history.json (§5.2) and the report; "next in line" (§2.3) and the day
/// report header (§3.2) follow it.
public class ProjectJobTests
{
    static OffloadJob Job(string card, IReadOnlyList<string> dests, string history, string? project, IReadOnlyList<string>? appendInto = null) =>
        new(new JobConfig(card, dests)
        {
            Date = "2026-10-06", AnalyzeClips = false, HistoryFolder = history, AppendInto = appendInto, Project = project,
        }, AppendFixtures.Options());

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 20_000, 1);
        return card;
    }

    [Fact]
    public void Project_IntoHistoryAndReport()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var job = Job(Card(dir), [dir.Sub("d1")], history, "Film X");

        job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Equal("Film X", Assert.Single(new HistoryStore(history).Load()).Project);
        var json = File.ReadAllText(Path.Combine(job.Plan!.CardPaths[0], "_REPORT", "offload_report.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Film X", doc.RootElement.GetProperty("project").GetString());
        Assert.Equal("Film X", CardRecord.Load(job.Plan.CardPaths[0]).Project);
    }

    // "Bez projektu": the key is absent from the report (the Mac format is unchanged), history says null.
    [Fact]
    public void NoProject_NoKey_HistoryNull()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var job = Job(Card(dir), [dir.Sub("d1")], history, project: null);

        job.Run();

        Assert.Null(Assert.Single(new HistoryStore(history).Load()).Project);
        var json = File.ReadAllText(Path.Combine(job.Plan!.CardPaths[0], "_REPORT", "offload_report.json"));
        Assert.DoesNotContain("\"project\"", json);
        Assert.Null(CardRecord.Load(job.Plan.CardPaths[0]).Project);
    }

    // An append adds a generation to the entry of the earlier backup: that entry's project stays.
    [Fact]
    public void Append_KeepsTheEntrysProject()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history, "Film X");
        full.Run();
        AppendFixtures.NewClip(card, "A001C002.mov", seed: 2);

        var append = Job(card, [], history, "Reklama Y", appendInto: full.Plan!.CardPaths);
        append.Run();

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.Equal("Film X", entry.Project);
        Assert.Equal([1, 2], entry.Generations.Select(g => g.Seq));
        Assert.Equal("Film X", CardRecord.Load(full.Plan.CardPaths[0]).Project);   // the first generation's
    }

    static HistoryEntry Copied(string cardName, string? project) => new() { CardName = cardName, Date = "2026-10-06", Project = project };

    // Spec 2.3 + 4.1: "next in line" counts the cards of the active project only (its name in any case).
    [Fact]
    public void NextInLine_PerProject()
    {
        HistoryEntry[] h = [Copied("A004", null), Copied("A001", "Film X"), Copied("B007", "Reklama Y")];
        var plain = new CardInsight { Source = @"E:\" };
        Assert.Equal("A005", CardNaming.Placeholder(true, plain, h, "2026-10-06", "2026-10-06", "EOS").Name);
        Assert.Equal("A002", CardNaming.Placeholder(true, plain, h, "2026-10-06", "2026-10-06", "EOS", project: "FILM X").Name);
        Assert.Equal("B008", CardNaming.Placeholder(true, plain, h, "2026-10-06", "2026-10-06", "EOS", project: "Reklama Y").Name);
        Assert.Equal("EOS", CardNaming.Placeholder(true, plain, h, "2026-10-06", "2026-10-06", "EOS", project: "Nový").Name);
        var r = CardNaming.Resolve(true, [new CardNameInput("E", null, plain, "EOS"), new CardNameInput("F", null, plain, "EOS")],
            h, "2026-10-06", "2026-10-06", "Film X");
        Assert.Equal(("A002", "A003"), (r["E"].Name, r["F"].Name));
    }

    static void Report(string day, string card, string? project)
    {
        var path = Path.Combine(day, card);
        var r = Reports.Sample(path);
        r.Project = project;
        Directory.CreateDirectory(Path.Combine(path, "_REPORT"));
        File.WriteAllText(Path.Combine(path, "_REPORT", "offload_report.json"), r.ToJson());
    }

    // Spec 3.2: the day report names the project only when every card with a readable report has the same one.
    [Fact]
    public void DayReport_ProjectInTheHeader_OnlyWhenAllCardsAgree()
    {
        using var dir = new TempDir();
        var day = dir.Sub("D", "2026-10-06");
        Report(day, "A001", "Film X");
        Report(day, "A002", "film x");
        Directory.CreateDirectory(Path.Combine(day, "A003", "ascmhl"));   // a card without a report does not count
        var model = DayReport.Load(day);
        Assert.Equal("Film X", model.Project);
        Assert.Contains("<span class=\"sub\">Projekt: Film X</span>", DayReport.Html(model));

        Report(day, "B001", null);                                          // a card of no project (or an older report)
        Assert.Null(DayReport.Load(day).Project);
        Assert.DoesNotContain("Projekt:", DayReport.Html(DayReport.Load(day)));

        using var other = new TempDir();
        var none = other.Sub("D", "2026-10-06");
        Report(none, "A001", null);
        Assert.Null(DayReport.Load(none).Project);
    }
}
```


- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ProjectJobTests"`
Expected: FAIL — build testů selže (CS0117 / CS1061: `JobConfig.Project`, `CardRecord.Project`, `OffloadReport.Project`, `DayModel.Project` a parametr `project` u `CardNaming` neexistují).

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/CardRecord.cs`, řádky 34–37 — najít:

```csharp
    public DateTime? Started => Generations.Count > 0 ? Generations.Min(g => g.Report.Started) : null;
    public DateTime? Finished => Generations.Count > 0 ? Generations.Max(g => g.Report.Finished) : null;
    public int TotalFiles => Files.Count;
    public long TotalBytes => Files.Values.Sum(f => f.File.Size);
```

nahradit:

```csharp
    public DateTime? Started => Generations.Count > 0 ? Generations.Min(g => g.Report.Started) : null;
    public DateTime? Finished => Generations.Count > 0 ? Generations.Max(g => g.Report.Finished) : null;
    /// The project of the card (spec 4.1): the first generation's that names one; null = none (or older reports only).
    public string? Project => Generations.Select(g => g.Report.Project).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
    public int TotalFiles => Files.Count;
    public long TotalBytes => Files.Values.Sum(f => f.File.Size);
```

`windows/src/Offload.Core/Desktop/CardNaming.cs`, řádky 11–17 — najít:

```csharp
    /// The clips' reel first; a card history does not know gets "next in line" for the date folder; otherwise
    /// `fallback` (the volume label or folder name). `Hint`: the grey line under the field, or null.
    /// `taken`: names already typed or suggested for other cards — "next in line" goes past them.
    public static (string Name, string? Hint) Placeholder(bool suggestFromClips, CardInsight? insight,
        IReadOnlyList<HistoryEntry>? history, string date, string today, string fallback, IEnumerable<string>? taken = null)
    {
        if (!suggestFromClips || insight == null) return (fallback, null);
```

nahradit:

```csharp
    /// The clips' reel first; a card history does not know gets "next in line" for the date folder; otherwise
    /// `fallback` (the volume label or folder name). `Hint`: the grey line under the field, or null.
    /// `taken`: names already typed or suggested for other cards — "next in line" goes past them. `project`: the active
    /// project (null = "Bez projektu") — "next in line" counts the cards of that project only (spec 2.3, 4.1).
    public static (string Name, string? Hint) Placeholder(bool suggestFromClips, CardInsight? insight,
        IReadOnlyList<HistoryEntry>? history, string date, string today, string fallback, IEnumerable<string>? taken = null,
        string? project = null)
    {
        if (!suggestFromClips || insight == null) return (fallback, null);
```

`windows/src/Offload.Core/Desktop/CardNaming.cs`, řádky 20–24 — najít:

```csharp
        // A card that could not be read is not "next" anything.
        if (insight.Error == null && insight.History == null && history != null
            && ReelSuggestion.Next(history, date, null, today, taken) is { } next)
            return (next.Name, next.Hint);
        return (fallback, null);
```

nahradit:

```csharp
        // A card that could not be read is not "next" anything.
        if (insight.Error == null && insight.History == null && history != null
            && ReelSuggestion.Next(history, date, project, today, taken) is { } next)
            return (next.Name, next.Hint);
        return (fallback, null);
```

`windows/src/Offload.Core/Desktop/CardNaming.cs`, řádky 29–33 — najít:

```csharp
    /// The GUI placeholders and Start both use this, so what is shown is what is copied.
    public static IReadOnlyDictionary<string, (string Name, string? Hint)> Resolve(bool suggestFromClips,
        IReadOnlyList<CardNameInput> cards, IReadOnlyList<HistoryEntry>? history, string date, string today)
    {
        var result = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase);
```

nahradit:

```csharp
    /// The GUI placeholders and Start both use this, so what is shown is what is copied.
    public static IReadOnlyDictionary<string, (string Name, string? Hint)> Resolve(bool suggestFromClips,
        IReadOnlyList<CardNameInput> cards, IReadOnlyList<HistoryEntry>? history, string date, string today, string? project = null)
    {
        var result = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase);
```

`windows/src/Offload.Core/Desktop/CardNaming.cs`, řádky 41–45 — najít:

```csharp
        {
            if (c.Typed?.Trim() is { Length: > 0 } typed) { result[c.Key] = (typed, null); continue; }
            var p = Placeholder(suggestFromClips, c.Insight, history, date, today, c.Fallback, taken);
            if (p.Name != c.Fallback && c.Insight?.Name == null) taken.Add(p.Name);
            result[c.Key] = p;
```

nahradit:

```csharp
        {
            if (c.Typed?.Trim() is { Length: > 0 } typed) { result[c.Key] = (typed, null); continue; }
            var p = Placeholder(suggestFromClips, c.Insight, history, date, today, c.Fallback, taken, project);
            if (p.Name != c.Fallback && c.Insight?.Name == null) taken.Add(p.Name);
            result[c.Key] = p;
```

`windows/src/Offload.Core/Models.cs`, řádky 72–75 — najít:

```csharp
    /// "Na cíli … zbude po offloadu jen … — rezerva pod 5 %…" — never blocks (R11).
    public IReadOnlyList<string> SpaceWarnings { get; init; } = [];
}

```

nahradit:

```csharp
    /// "Na cíli … zbude po offloadu jen … — rezerva pod 5 %…" — never blocks (R11).
    public IReadOnlyList<string> SpaceWarnings { get; init; } = [];
    /// The project the job runs in (spec 4.1), null = "Bez projektu" — set by OffloadJob, written into the report.
    public string? Project { get; set; }
}

```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 34–37 — najít:

```csharp
    /// --no-day-report).
    public OutputSettings Outputs { get; set; } = new();
}

```

nahradit:

```csharp
    /// --no-day-report).
    public OutputSettings Outputs { get; set; } = new();
    /// The project the job was started in (spec 4.1), null = "Bez projektu": into history.json and the report.
    public string? Project { get; set; }
}

```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 137–143 — najít:

```csharp
        {
            var a = appendPlan = Engine.PlanAppend(Config.Source, into);
            return Plan = Engine.PrepareAppend(a, EstimateExtra);
        }
        return Plan = Engine.Prepare(Config.Source, Config.DestRoots, Config.CardName, Config.Date, EstimateExtra);
    }

```

nahradit:

```csharp
        {
            var a = appendPlan = Engine.PlanAppend(Config.Source, into);
            var append = Engine.PrepareAppend(a, EstimateExtra);
            append.Project = Config.Project;
            return Plan = append;
        }
        var full = Engine.Prepare(Config.Source, Config.DestRoots, Config.CardName, Config.Date, EstimateExtra);
        full.Project = Config.Project;
        return Plan = full;
    }

```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 334–337 — najít:

```csharp
                    CardName = plan.CardName,
                    Date = plan.Date,
                    Success = true,
                    Verified = true,
```

nahradit:

```csharp
                    CardName = plan.CardName,
                    Date = plan.Date,
                    Project = plan.Project,   // a new entry only: an append keeps the project of the entry it adds to
                    Success = true,
                    Verified = true,
```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 59–62 — najít:

```csharp
    /// The ASC MHL generation this report belongs to: absent (= 1) for the full copy, 2, 3 … for appends (spec 2.2).
    [JsonPropertyName("generation")] public int? Generation { get; set; }

    /// The hashes of this copy: `hashAlgorithms`, else the one `hashAlgorithm` (reports without the key, the Mac).
```

nahradit:

```csharp
    /// The ASC MHL generation this report belongs to: absent (= 1) for the full copy, 2, 3 … for appends (spec 2.2).
    [JsonPropertyName("generation")] public int? Generation { get; set; }
    /// The project the card was copied in (spec 4.1); absent for "Bez projektu" and in older reports. The Mac ignores it.
    [JsonPropertyName("project")] public string? Project { get; set; }

    /// The hashes of this copy: `hashAlgorithms`, else the one `hashAlgorithm` (reports without the key, the Mac).
```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 120–123 — najít:

```csharp
            Skipped = plan.Scan.Skipped,
            Generation = plan.Generation > 1 ? plan.Generation : null,
            HashAlgorithms = HashKinds.Normalize(plan.Hashes).HasFlag(HashKind.Md5) ? HashKinds.Names(plan.Hashes) : null,
        };
```

nahradit:

```csharp
            Skipped = plan.Scan.Skipped,
            Generation = plan.Generation > 1 ? plan.Generation : null,
            Project = plan.Project,
            HashAlgorithms = HashKinds.Normalize(plan.Hashes).HasFlag(HashKind.Md5) ? HashKinds.Names(plan.Hashes) : null,
        };
```

`windows/src/Offload.Core/Outputs/DayReport.cs`, řádky 44–47 — najít:

```csharp
    public IReadOnlyList<DayCard> Cards => Folders.Where(f => f.IsCard).ToList();
    public int VerifiedCards => Cards.Count(c => c.Record.Verified && c.Record.FailedFiles.Count == 0);
}

```

nahradit:

```csharp
    public IReadOnlyList<DayCard> Cards => Folders.Where(f => f.IsCard).ToList();
    public int VerifiedCards => Cards.Count(c => c.Record.Verified && c.Record.FailedFiles.Count == 0);
    /// The project in the header (spec 3.2): only when every card with a readable report names the same one (any case).
    public string? Project
    {
        get
        {
            var read = Cards.Where(c => c.Record.Generations.Count > 0).Select(c => c.Record.Project).ToList();
            return read.Count > 0 && read[0] is string first && read.All(p => string.Equals(p, first, StringComparison.OrdinalIgnoreCase)) ? first : null;
        }
    }
}

```

`windows/src/Offload.Core/Outputs/DayReport.cs`, řádky 133–137 — najít:

```csharp
        var (badge, badgeClass) = verified == cards.Count && cards.Count > 0 ? ("VŠE OVĚŘENO", "ok") : ("VYŽADUJE POZORNOST", "warn");
        h.Append($"<header><h1>Denní report — {E(DateText(day.Date))}</h1><span class=\"badge {badgeClass}\">{badge}</span>\n");
        h.Append($"<span class=\"sub\">Cíl: <span class=\"mono\">{E(day.Root)}</span></span></header>\n");
        h.Append("<div class=\"stats\">\n");
        Stat(h, cards.Count.ToString(Inv), Texts.Plural(cards.Count, "karta", "karty", "karet"));
```

nahradit:

```csharp
        var (badge, badgeClass) = verified == cards.Count && cards.Count > 0 ? ("VŠE OVĚŘENO", "ok") : ("VYŽADUJE POZORNOST", "warn");
        h.Append($"<header><h1>Denní report — {E(DateText(day.Date))}</h1><span class=\"badge {badgeClass}\">{badge}</span>\n");
        h.Append($"<span class=\"sub\">Cíl: <span class=\"mono\">{E(day.Root)}</span></span>");
        if (day.Project is string project) h.Append($"<span class=\"sub\">Projekt: {E(project)}</span>");
        h.Append("</header>\n");
        h.Append("<div class=\"stats\">\n");
        Stat(h, cards.Count.ToString(Inv), Texts.Plural(cards.Count, "karta", "karty", "karet"));
```

`windows/src/Offload.Core/ReelNames.cs`, řádky 67–76 — najít:

```csharp
    }

    /// "Next in line": history entries of the same date folder (and, from plan 8, project) named like "A001"; exactly
    /// one camera letter → the next number ("A001" → "A002"). Several letters → null (which camera is this card?).
    public static NameSuggestion? Next(IEnumerable<HistoryEntry> history, string date, string? project, string today, IEnumerable<string>? taken = null)
    {
        // TODO(plan 8): `project` is always null until projects exist; callers pass it through then.
        var names = history.Where(e => e.Date == date && e.Project == project && ReelFolder().IsMatch(e.CardName))
            .Select(e => e.CardName).ToList();
        if (names.Count == 0 || names.Select(n => n[0]).Distinct().Count() != 1) return null;
```

nahradit:

```csharp
    }

    /// "Next in line": history entries of the same date folder and project (null = "Bez projektu"; a name in any case)
    /// named like "A001"; exactly one camera letter → the next number ("A001" → "A002"). Several letters → null (which
    /// camera is this card?).
    public static NameSuggestion? Next(IEnumerable<HistoryEntry> history, string date, string? project, string today, IEnumerable<string>? taken = null)
    {
        var names = history.Where(e => e.Date == date && string.Equals(e.Project, project, StringComparison.OrdinalIgnoreCase) && ReelFolder().IsMatch(e.CardName))
            .Select(e => e.CardName).ToList();
        if (names.Count == 0 || names.Select(n => n[0]).Distinct().Count() != 1) return null;
```


- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ProjectJobTests|FullyQualifiedName~ReelNamesTests|FullyQualifiedName~DayReportTests|FullyQualifiedName~CardRecordTests|FullyQualifiedName~HistoryJobTests|FullyQualifiedName~ReportTests"`
Expected: PASS. Pak celá sada (`-warnaserror`, vše zelené, 1051 testů: 1044 úspěšných + 7 přeskočených).

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 22–23 — najít:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 2 — projekt úlohy (`JobConfig.Project`) jde do historie (`project` nového záznamu; doplnění projekt záznamu nemění) a do reportu karty (`offload_report.json` → `project`, bez projektu chybí — Mac ignoruje); `CardRecord.Project`; denní report ukáže „Projekt: X“, když ho mají všechny karty dne s čitelným reportem stejný; „další v řadě“ počítá jen karty stejného projektu (bez ohledu na velikost písmen) — TODO plánu 6 v `ReelSuggestion.Next` pryč
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Windows: the project of a job in history, report and day report; next in line per project

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/OffloadJob.cs windows/src/Offload.Core/Models.cs windows/src/Offload.Core/OffloadReport.cs windows/src/Offload.Core/CardRecord.cs windows/src/Offload.Core/Outputs/DayReport.cs windows/src/Offload.Core/ReelNames.cs windows/src/Offload.Core/Desktop/CardNaming.cs windows/tests/Offload.Core.Tests/ProjectJobTests.cs
git commit -F .git/COMMIT_MSG_PLAN8
```

---

### Task 3: GUI projektů

Spec §4.1 UX: nahoře v levém panelu „Projekt: [Bez projektu ▾]“, menu *Projekt* („Uložit jako projekt…“, „Přejmenovat…“, „Smazat…“ s potvrzením), dialog „Název projektu“ s chybami, titulek „Offload — Film X“; každá změna se uloží do aktivního profilu (dnešní `SettingsChanged` 400 ms), fronta drží klon. Rozhodování (jména, přepnutí, kopie profilu) je v `Projects` (úkol 1, testované); tady jen vazby. **Bez jednotkových testů** — testovací projekt WPF nereferencuje; ověří `dotnet build` (XAML) a ruční testy 31–33 (úkol 12).

`MainViewModel.Settings` se stane aktivním profilem (`SettingsProfile`); celé nastavení je `AllSettings`. Kompilátor najde každé místo, které sahá na globální klíče (`Tools`, `PortNames`) — plán je všechna uvádí.

**Files:**
- Modify: `windows/src/Offload.App/MainViewModel.cs:14-18, 26-33, 45-49`
- Create: `windows/src/Offload.App/MainViewModel.Projects.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs:65-69, 263-267, 579-583, 589-597`
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs:188-193, 211-216`
- Modify: `windows/src/Offload.App/MainViewModel.Hardware.cs:149-154`
- Modify: `windows/src/Offload.App/HardwareViewModel.cs:121-125, 178-182, 195-199`
- Modify: `windows/src/Offload.App/JobItemViewModel.cs:69-73`
- Modify: `windows/src/Offload.App/MainWindow.xaml:3-7, 14-17`
- Modify: `windows/src/Offload.App/MainWindow.xaml.cs:28-31`
- Create: `windows/src/Offload.App/ProjectNameWindow.xaml`, `windows/src/Offload.App/ProjectNameWindow.xaml.cs`
- Modify: `windows/src/Offload.App/SetupPanel.xaml:44-47`
- Modify: `docs/VYVOJ-WINDOWS.md:22-23`

**Interfaces:**
- Consumes: `Projects.*`, `AppSettings.Active`, `SettingsProfile` (úkol 1); `CardNaming.Resolve(…, project)`, `JobConfig.Project` (úkol 2).
- Produces: `MainViewModel.AllSettings` (`AppSettings`), `MainViewModel.Settings` (`SettingsProfile`, aktivní profil), `internal string? ActiveProjectName`, `ProjectChoices`, `ActiveProjectChoice`, `WindowTitle`, `SaveAsProjectCommand`, `RenameProjectCommand`, `DeleteProjectCommand`, `Func<string, string, Func<string, string?>, string?>? AskProjectName`, `void ProfileChanged()`, pole `LutCatalog? lutCatalog`; `ProjectNameWindow.Ask(Window, string heading, string initial, Func<string, string?> problem) → string?`.

- [ ] **Step 1: Implementace**

`windows/src/Offload.App/HardwareViewModel.cs`, řádky 121–125 — najít:

```csharp
        var (cards, dests, all) = owner.HardwareRoles();
        var ejected = owner.EjectedRoots;
        var names = new Dictionary<string, string>(owner.Settings.PortNames, StringComparer.OrdinalIgnoreCase);
        cts = new CancellationTokenSource();
        var token = cts.Token;
```

nahradit:

```csharp
        var (cards, dests, all) = owner.HardwareRoles();
        var ejected = owner.EjectedRoots;
        var names = new Dictionary<string, string>(owner.AllSettings.PortNames, StringComparer.OrdinalIgnoreCase);
        cts = new CancellationTokenSource();
        var token = cts.Token;
```

`windows/src/Offload.App/HardwareViewModel.cs`, řádky 178–182 — najít:

```csharp
    {
        if (result is not { } r) return;
        var names = owner.Settings.PortNames;
        Drives.Clear();
        foreach (var d in r.Drives) Drives.Add(new HwDriveRow(d.Role, d.Link.Root, d.Link.Describe(r.Connectors, names)));
```

nahradit:

```csharp
    {
        if (result is not { } r) return;
        var names = owner.AllSettings.PortNames;
        Drives.Clear();
        foreach (var d in r.Drives) Drives.Add(new HwDriveRow(d.Role, d.Link.Root, d.Link.Describe(r.Connectors, names)));
```

`windows/src/Offload.App/HardwareViewModel.cs`, řádky 195–199 — najít:

```csharp
        PortGroups.Clear();
        if (result is not { } r) return;
        var names = owner.Settings.PortNames;
        foreach (var g in r.Connectors.Where(c => c.UserVisible || ShowInternal).GroupBy(c => c.ControllerId, StringComparer.OrdinalIgnoreCase))
        {
```

nahradit:

```csharp
        PortGroups.Clear();
        if (result is not { } r) return;
        var names = owner.AllSettings.PortNames;
        foreach (var g in r.Connectors.Where(c => c.UserVisible || ShowInternal).GroupBy(c => c.ControllerId, StringComparer.OrdinalIgnoreCase))
        {
```

`windows/src/Offload.App/JobItemViewModel.cs`, řádky 69–73 — najít:

```csharp
    void ResolveChain(OffloadPlan plan)
    {
        var names = new Dictionary<string, string>(owner.Settings.PortNames, StringComparer.OrdinalIgnoreCase);
        Task.Run(() =>
        {
```

nahradit:

```csharp
    void ResolveChain(OffloadPlan plan)
    {
        var names = new Dictionary<string, string>(owner.AllSettings.PortNames, StringComparer.OrdinalIgnoreCase);
        Task.Run(() =>
        {
```

`windows/src/Offload.App/MainViewModel.Hardware.cs`, řádky 149–154 — najít:

```csharp
    internal void SetPortName(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) Settings.PortNames.Remove(id);
        else Settings.PortNames[id] = name.Trim();
        SettingsChanged();
    }
```

nahradit:

```csharp
    internal void SetPortName(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) AllSettings.PortNames.Remove(id);
        else AllSettings.PortNames[id] = name.Trim();
        SettingsChanged();
    }
```

Vytvořit `windows/src/Offload.App/MainViewModel.Projects.cs`:

```csharp
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

/// Projects (spec 4.1): "Projekt: [▾]" at the top of the left panel and the Projekt menu. Switching only changes which
/// profile the window shows; queued and running jobs keep the copy they were started with (StartJobs clones).
public sealed partial class MainViewModel
{
    // The built-in LUTs once loaded: a new Proxy view model (another project) gets them at once.
    LutCatalog? lutCatalog;

    public RelayCommand SaveAsProjectCommand { get; private set; } = null!;
    public RelayCommand RenameProjectCommand { get; private set; } = null!;
    public RelayCommand DeleteProjectCommand { get; private set; } = null!;

    /// Asks for a project name (set by the window): title, the name to start with, the check of a name (Czech problem
    /// or null). Returns the accepted name, or null when the user cancels.
    public Func<string, string, Func<string, string?>, string?>? AskProjectName { get; set; }

    void InitProjects()
    {
        SaveAsProjectCommand = new RelayCommand(SaveAsProject, () => !closing);
        RenameProjectCommand = new RelayCommand(RenameProject, () => !closing && ActiveProjectName != null);
        DeleteProjectCommand = new RelayCommand(DeleteProject, () => !closing && ActiveProjectName != null);
    }

    /// The active project's name; null = "Bez projektu". What a started job, history and the report get.
    internal string? ActiveProjectName => Projects.ActiveName(AllSettings);

    /// "Bez projektu" first, then the projects in the order they were saved.
    public IReadOnlyList<string> ProjectChoices => [Projects.NoProject, .. AllSettings.Projects.Select(p => p.Name)];

    public string ActiveProjectChoice
    {
        get => ActiveProjectName ?? Projects.NoProject;
        set
        {
            // null comes from the ComboBox while its list is rebuilt: not a choice.
            if (value == null || value == ActiveProjectChoice) return;
            try
            {
                Projects.Activate(AllSettings, value);
            }
            catch (ArgumentException e)
            {
                ShowMessage(e.Message);
                return;
            }
            ProfileChanged();
        }
    }

    /// "Offload — Film X" (spec 4.1).
    public string WindowTitle => Projects.WindowTitle(ActiveProjectName);

    void SaveAsProject()
    {
        if (AskProjectName?.Invoke("Uložit jako projekt", "", n => Projects.NameProblem(n, AllSettings.Projects.Select(p => p.Name))) is not string name) return;
        Projects.SaveAs(AllSettings, name);   // the values shown now: nothing on screen changes but the name
        ProfileChanged();
    }

    void RenameProject()
    {
        if (ActiveProjectName is not string current) return;
        if (AskProjectName?.Invoke("Přejmenovat projekt", current, n => Projects.NameProblem(n, AllSettings.Projects.Select(p => p.Name), except: current)) is not string name) return;
        Projects.Rename(AllSettings, current, name);
        ProfileChanged();
    }

    void DeleteProject()
    {
        if (ActiveProjectName is not string current) return;
        if (AskYesNo?.Invoke(Projects.DeleteQuestion(current)) != true) return;
        Projects.Delete(AllSettings, current);
        ProfileChanged();
    }

    /// Another profile is shown (switch, save as, rename, delete): saved, and everything the left panel shows from it is
    /// read again — the destinations and their disks, the LUT and the proxy folder (off the UI thread), the names.
    void ProfileChanged()
    {
        SettingsChanged();
        Proxy = new ProxyOptionsViewModel(Settings.Proxy, this);
        if (lutCatalog != null) Proxy.CatalogLoaded(lutCatalog);
        RebuildDestinations();
        RefreshVolumes();   // a disk that is a destination in this profile is no card here
        _ = ProbeFreeSpaceAsync(TimeSpan.FromSeconds(30));
        RefreshLut();
        RefreshProxyFolder();
        NamingChanged();   // "next in line" counts the cards of this project
        Raise(string.Empty);   // every binding of the window
        SetupChanged?.Invoke();
    }
}
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 188–193 — najít:

```csharp
                    AnalyzeClips = Settings.AnalyzeClips,
                    Proxy = SettingsStore.Clone(Settings.Proxy),
                    Tools = SettingsStore.Clone(Settings.Tools),
                    Outputs = SettingsStore.Clone(Settings.Outputs),
                };
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), AppendRules.Title(known.Entry.CardName),
```

nahradit:

```csharp
                    AnalyzeClips = Settings.AnalyzeClips,
                    Proxy = SettingsStore.Clone(Settings.Proxy),
                    Tools = SettingsStore.Clone(AllSettings.Tools),
                    Outputs = SettingsStore.Clone(Settings.Outputs),
                    Project = known.Entry.Project,   // the earlier backup's project (its history entry keeps it)
                };
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), AppendRules.Title(known.Entry.CardName),
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 211–216 — najít:

```csharp
                AnalyzeClips = Settings.AnalyzeClips,
                Proxy = SettingsStore.Clone(Settings.Proxy),
                Tools = SettingsStore.Clone(Settings.Tools),
                Outputs = SettingsStore.Clone(Settings.Outputs),
            };
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
```

nahradit:

```csharp
                AnalyzeClips = Settings.AnalyzeClips,
                Proxy = SettingsStore.Clone(Settings.Proxy),
                Tools = SettingsStore.Clone(AllSettings.Tools),
                Outputs = SettingsStore.Clone(Settings.Outputs),
                Project = ActiveProjectName,
            };
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 65–69 — najít:

```csharp
        {
            if (closing) return;
            if (t.IsCompletedSuccessfully) Proxy.CatalogLoaded(t.Result);
            else AppLog.Write($"LUT catalog: {t.Exception?.GetBaseException().Message}");
        }, TaskScheduler.FromCurrentSynchronizationContext());
```

nahradit:

```csharp
        {
            if (closing) return;
            if (t.IsCompletedSuccessfully) Proxy.CatalogLoaded(lutCatalog = t.Result);
            else AppLog.Write($"LUT catalog: {t.Exception?.GetBaseException().Message}");
        }, TaskScheduler.FromCurrentSynchronizationContext());
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 263–267 — najít:

```csharp
        if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
        var cards = paths.Select(p => new CardNameInput(p, cardNames.GetValueOrDefault(p), Insight(p), DefaultCardName(p))).ToList();
        return CardNaming.Resolve(Settings.Naming.SuggestFromClips, cards, history, DateString, Layout.Today())[path];
    }

```

nahradit:

```csharp
        if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
        var cards = paths.Select(p => new CardNameInput(p, cardNames.GetValueOrDefault(p), Insight(p), DefaultCardName(p))).ToList();
        return CardNaming.Resolve(Settings.Naming.SuggestFromClips, cards, history, DateString, Layout.Today(), ActiveProjectName)[path];
    }

```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 579–583 — najít:

```csharp
    void RefreshTools()
    {
        resolvedTools = Settings.Tools.Resolved();
        Tools.Clear();
        void Row(string name, string purpose, string? path, Action<string> set) =>
```

nahradit:

```csharp
    void RefreshTools()
    {
        resolvedTools = AllSettings.Tools.Resolved();
        Tools.Clear();
        void Row(string name, string purpose, string? path, Action<string> set) =>
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 589–597 — najít:

```csharp
                RefreshTools();
            }));
        Row("ffmpeg", "proxy, náhledy", resolvedTools.Ffmpeg, p => Settings.Tools.Ffmpeg = p);
        Row("ffprobe", "metadata klipů", resolvedTools.Ffprobe, p => Settings.Tools.Ffprobe = p);
        Row("braw-decode", "Blackmagic RAW", resolvedTools.BrawDecoder, p => Settings.Tools.BrawDecoder = p);
        Row("REDline", "RED R3D", resolvedTools.Redline, p => Settings.Tools.Redline = p);
        Row("art-cmd", "ARRIRAW", resolvedTools.ArtCmd, p => Settings.Tools.ArtCmd = p);
        Raise(nameof(ToolsWarning));
    }
```

nahradit:

```csharp
                RefreshTools();
            }));
        Row("ffmpeg", "proxy, náhledy", resolvedTools.Ffmpeg, p => AllSettings.Tools.Ffmpeg = p);
        Row("ffprobe", "metadata klipů", resolvedTools.Ffprobe, p => AllSettings.Tools.Ffprobe = p);
        Row("braw-decode", "Blackmagic RAW", resolvedTools.BrawDecoder, p => AllSettings.Tools.BrawDecoder = p);
        Row("REDline", "RED R3D", resolvedTools.Redline, p => AllSettings.Tools.Redline = p);
        Row("art-cmd", "ARRIRAW", resolvedTools.ArtCmd, p => AllSettings.Tools.ArtCmd = p);
        Raise(nameof(ToolsWarning));
    }
```

`windows/src/Offload.App/MainViewModel.cs`, řádky 14–18 — najít:

```csharp
        Options = options;
        this.settingsPath = settingsPath ?? SettingsStore.DefaultPath;
        Settings = SettingsStore.Load(this.settingsPath);
        saveTimer.Tick += (_, _) =>
        {
```

nahradit:

```csharp
        Options = options;
        this.settingsPath = settingsPath ?? SettingsStore.DefaultPath;
        AllSettings = SettingsStore.Load(this.settingsPath);
        saveTimer.Tick += (_, _) =>
        {
```

`windows/src/Offload.App/MainViewModel.cs`, řádky 26–33 — najít:

```csharp
        InitVerify();
        InitDayReport();
    }

    public StartupOptions Options { get; }
    public AppSettings Settings { get; }

    public event Action<string>? MessageRequested;
```

nahradit:

```csharp
        InitVerify();
        InitDayReport();
        InitProjects();
    }

    public StartupOptions Options { get; }
    /// The whole settings.json: the global keys (tools, port names) and every project.
    public AppSettings AllSettings { get; }
    /// The profile the window shows and edits: the active project, else "Bez projektu" (spec 4.1). Every change goes
    /// into it; a started job keeps its own copy.
    public SettingsProfile Settings => AllSettings.Active;

    public event Action<string>? MessageRequested;
```

`windows/src/Offload.App/MainViewModel.cs`, řádky 45–49 — najít:

```csharp
        try
        {
            SettingsStore.Save(Settings, settingsPath);
        }
        catch (Exception e)
```

nahradit:

```csharp
        try
        {
            SettingsStore.Save(AllSettings, settingsPath);
        }
        catch (Exception e)
```

`windows/src/Offload.App/MainWindow.xaml`, řádky 3–7 — najít:

```xml
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Offload.App"
        Title="Offload" Width="1240" Height="860" MinWidth="940" MinHeight="640"
        WindowStartupLocation="CenterScreen"
        Icon="pack://application:,,,/Offload.ico">
```

nahradit:

```xml
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Offload.App"
        Title="{Binding WindowTitle}" Width="1240" Height="860" MinWidth="940" MinHeight="640"
        WindowStartupLocation="CenterScreen"
        Icon="pack://application:,,,/Offload.ico">
```

`windows/src/Offload.App/MainWindow.xaml`, řádky 14–17 — najít:

```xml
        <Border DockPanel.Dock="Top" BorderThickness="0,0,0,1" BorderBrush="{DynamicResource DividerStrokeColorDefaultBrush}">
            <Menu Padding="6,2">
                <MenuItem Header="_Nástroje">
                    <MenuItem Header="Ověřit zálohu…" InputGestureText="Ctrl+Shift+O" Command="{Binding VerifyBackupCommand}"
```

nahradit:

```xml
        <Border DockPanel.Dock="Top" BorderThickness="0,0,0,1" BorderBrush="{DynamicResource DividerStrokeColorDefaultBrush}">
            <Menu Padding="6,2">
                <MenuItem Header="_Projekt" AutomationProperties.AutomationId="ProjectMenu">
                    <MenuItem Header="Uložit jako projekt…" Command="{Binding SaveAsProjectCommand}"
                              AutomationProperties.AutomationId="SaveAsProjectMenuItem" />
                    <MenuItem Header="Přejmenovat…" Command="{Binding RenameProjectCommand}"
                              AutomationProperties.AutomationId="RenameProjectMenuItem" />
                    <MenuItem Header="Smazat…" Command="{Binding DeleteProjectCommand}"
                              AutomationProperties.AutomationId="DeleteProjectMenuItem" />
                </MenuItem>
                <MenuItem Header="_Nástroje">
                    <MenuItem Header="Ověřit zálohu…" InputGestureText="Ctrl+Shift+O" Command="{Binding VerifyBackupCommand}"
```

`windows/src/Offload.App/MainWindow.xaml.cs`, řádky 28–31 — najít:

```csharp
        vm.AskBackedUp = model => BackedUpPromptWindow.Ask(this, model);
        vm.AskYesNo = text => MessageBox.Show(this, text, "Offload", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        // Spec 8a.2: the USB check is on demand only (menu Nástroje, Ctrl+U, the Hardware section) — no start-up question.
        vm.HardwareRequested += hw =>
```

nahradit:

```csharp
        vm.AskBackedUp = model => BackedUpPromptWindow.Ask(this, model);
        vm.AskYesNo = text => MessageBox.Show(this, text, "Offload", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        vm.AskProjectName = (title, initial, problem) => ProjectNameWindow.Ask(this, title, initial, problem);
        // Spec 8a.2: the USB check is on demand only (menu Nástroje, Ctrl+U, the Hardware section) — no start-up question.
        vm.HardwareRequested += hw =>
```

Vytvořit `windows/src/Offload.App/ProjectNameWindow.xaml`:

```xml
<Window x:Class="Offload.App.ProjectNameWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Offload" SizeToContent="Height" Width="420" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Icon="pack://application:,,,/Offload.ico">
    <StackPanel Margin="22,18">
        <TextBlock x:Name="Heading" FontSize="17" FontWeight="SemiBold" />
        <TextBlock Text="Název projektu" Style="{StaticResource Caption}" Margin="0,12,0,4" />
        <TextBox x:Name="NameBox" TextChanged="NameBox_TextChanged" AutomationProperties.AutomationId="ProjectNameBox"
                 AutomationProperties.Name="Název projektu" />
        <TextBlock x:Name="Problem" Style="{StaticResource ErrorText}" Margin="0,6,0,0" TextWrapping="Wrap" Visibility="Collapsed"
                   AutomationProperties.AutomationId="ProjectNameProblem" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
            <Button x:Name="OkButton" Content="OK" Click="Ok_Click" IsDefault="True" MinWidth="80" Padding="12,5"
                    Style="{DynamicResource AccentButtonStyle}" AutomationProperties.AutomationId="ProjectNameOk" />
            <Button Content="Zrušit" IsCancel="True" MinWidth="80" Padding="12,5" Margin="8,0,0,0" AutomationProperties.AutomationId="ProjectNameCancel" />
        </StackPanel>
    </StackPanel>
</Window>
```

Vytvořit `windows/src/Offload.App/ProjectNameWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;

namespace Offload.App;

/// "Název projektu" for Uložit jako projekt… / Přejmenovat… (spec 4.1). The name is checked as it is typed (the Czech
/// problem under the field); OK only takes a name without a problem.
public partial class ProjectNameWindow : Window
{
    readonly Func<string, string?> problem;
    string? accepted;

    ProjectNameWindow(string heading, string initial, Func<string, string?> problem)
    {
        InitializeComponent();
        this.problem = problem;
        Heading.Text = heading;
        NameBox.Text = initial;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
        Show(problem(initial), quiet: true);
    }

    /// The accepted name (trimmed), or null when the user cancelled.
    public static string? Ask(Window owner, string heading, string initial, Func<string, string?> problem)
    {
        var w = new ProjectNameWindow(heading, initial, problem) { Owner = owner };
        w.ShowDialog();
        return w.accepted;
    }

    /// `quiet`: before the user typed anything an empty name is not shouted at — OK just stays off.
    void Show(string? text, bool quiet)
    {
        OkButton.IsEnabled = text == null;
        Problem.Text = text ?? "";
        Problem.Visibility = text != null && !quiet ? Visibility.Visible : Visibility.Collapsed;
    }

    void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Show(problem(NameBox.Text), quiet: false);
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (problem(NameBox.Text) is string text)
        {
            Show(text, quiet: false);
            return;
        }
        accepted = NameBox.Text.Trim();
        DialogResult = true;
    }
}
```

`windows/src/Offload.App/SetupPanel.xaml`, řádky 44–47 — najít:

```xml
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <StackPanel x:Name="Sections" Margin="18">

                <!-- Karty -->
```

nahradit:

```xml
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <StackPanel x:Name="Sections" Margin="18">

                <!-- Projekt (spec 4.1) -->
                <DockPanel Margin="0,0,0,14">
                    <TextBlock Text="Projekt:" Style="{StaticResource Caption}" VerticalAlignment="Center" Margin="0,0,8,0" />
                    <ComboBox ItemsSource="{Binding ProjectChoices}" SelectedItem="{Binding ActiveProjectChoice}"
                              ToolTip="Pojmenované nastavení (cíle, kontrola, výstupy, proxy, oznámení) — menu Projekt"
                              AutomationProperties.AutomationId="ProjectChoice" AutomationProperties.Name="Projekt" />
                </DockPanel>

                <!-- Karty -->
```


- [ ] **Step 2: Build a celá sada**

Run: `dotnet build windows\Offload.sln -warnaserror`
Expected: 0 chyb, 0 varování (XAML `ProjectNameWindow` a nové vazby se zkompilují; žádné `Settings.Tools` / `Settings.PortNames` nezůstalo).
Run: `dotnet test windows\Offload.sln`
Expected: vše zelené (1051 testů: 1044 úspěšných + 7 přeskočených — beze změny proti úkolu 2).

- [ ] **Step 3: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 22–23 — najít:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 3 — GUI projektů: nahoře v levém panelu „Projekt: [Bez projektu ▾]“, menu *Projekt* („Uložit jako projekt…“ z právě zobrazených hodnot, „Přejmenovat…“, „Smazat…“ s dotazem „Smazat projekt …? Nastavení projektu se ztratí, zálohy na discích zůstanou.“), dialog „Název projektu“ s českou chybou pod polem, titulek „Offload — Film X“; přepnutí znovu načte cíle, disky, LUT a složku pro proxy (mimo UI vlákno) a návrhy názvů; spuštěná úloha dostane projekt (doplnění projekt dřívější zálohy) a vlastní kopii nastavení — přepnutí ji nezmění; `MainViewModel.Settings` je aktivní profil, globální nástroje a názvy portů jsou v `AllSettings`
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Windows: projects in the app (switcher, Projekt menu, name dialog, window title)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.App/MainViewModel.cs windows/src/Offload.App/MainViewModel.Projects.cs windows/src/Offload.App/MainViewModel.Setup.cs windows/src/Offload.App/MainViewModel.Queue.cs windows/src/Offload.App/MainViewModel.Hardware.cs windows/src/Offload.App/HardwareViewModel.cs windows/src/Offload.App/JobItemViewModel.cs windows/src/Offload.App/MainWindow.xaml windows/src/Offload.App/MainWindow.xaml.cs windows/src/Offload.App/ProjectNameWindow.xaml windows/src/Offload.App/ProjectNameWindow.xaml.cs windows/src/Offload.App/SetupPanel.xaml
git commit -F .git/COMMIT_MSG_PLAN8
```

---

### Task 4: CLI `--project`

Spec §4.1 CLI: `--project <název>` načte `settings.json` (`AppData.Folder`, respektuje `OFFLOAD_SETTINGS_DIR`), profil projektu jsou výchozí hodnoty, **výslovně zadané volby vyhrávají**, `-d` není povinné, když projekt má cíle; neznámý → „Projekt „X“ neexistuje. Projekty: Film X, Reklama Y.“ (kód 1). Test přednosti voleb `--project` + `--proxy-codec`.

**Files:**
- Create: `windows/src/Offload.Cli/CliProject.cs`
- Modify: `windows/src/Offload.Cli/CopyCommand.cs:24-39, 41-50, 53-57`
- Modify: `windows/src/Offload.Cli/AppendCommand.cs:18-26, 29-41`
- Modify: `windows/src/Offload.Cli/StageTwoOptions.cs:30-64` (`Apply`)
- Modify: `windows/src/Offload.Core/Messages.cs:99-102`
- Test: `windows/tests/Offload.Core.Tests/CliProjectTests.cs` (nový)
- Modify: `docs/VYVOJ-WINDOWS.md:22-23`

**Interfaces:**
- Consumes: `SettingsStore.Load`, `Projects.Find/Unknown`, `ProjectSettings` (úkol 1), `JobConfig.Project` (úkol 2), `CopyCommand.JobFactory` (existuje), `ProxyPreflight.Blocking` (existuje).
- Produces: `static class CliProject` — `internal static Func<string> SettingsPath`, `Create() → Option<string?>` (`--project`), `Load(string?) → ProjectSettings?` (ArgumentException s českou hláškou), `Given(ParseResult, SettingsProfile?, Option) → bool`; `StageTwoOptions.Apply(ParseResult r, JobConfig config, SettingsProfile? project = null)`; `Msg.CliProject(string name)`.

- [ ] **Step 1: Padající testy**

Vytvořit `windows/tests/Offload.Core.Tests/CliProjectTests.cs`:

```csharp
using Offload.Cli;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// Spec 4.1 CLI: `--project <název>` takes a saved project as the defaults; options written on the command line win.
[Collection("Console")]
public class CliProjectTests
{
    /// Runs a command with settings.json in `settingsDir`; `seen` gets the job's config (the job really runs unless --dry-run).
    static (int Exit, string Out, string Err) Run(string settingsDir, List<JobConfig> seen, System.CommandLine.Command command, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var oldFactory = CopyCommand.JobFactory;
        var oldPath = CliProject.SettingsPath;
        Console.SetOut(so);
        Console.SetError(se);
        CliProject.SettingsPath = () => Path.Combine(settingsDir, "settings.json");
        CopyCommand.JobFactory = c =>
        {
            seen.Add(c);
            var options = AppendFixtures.Options();   // card and destinations share a volume in a test
            options.Hashes = c.Hashes;
            options.Verify = c.Verify;
            return new OffloadJob(c, options);
        };
        try { return (command.Parse(args).Invoke(), so.ToString(), se.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = oldFactory;
            CliProject.SettingsPath = oldPath;
        }
    }

    /// settings.json with project "Film X": destination `dest`, MD5, no verify, ProRes 422 at 720 px, ALE, no names from clips.
    static string Settings(TempDir dir, string dest, bool suggestFromClips = false)
    {
        var folder = dir.Sub("appdata");
        var s = new AppSettings();
        var p = Projects.SaveAs(s, "Film X");
        p.DestRoots = [dest];
        p.Hash = "both";
        p.Verify = false;
        p.AnalyzeClips = false;
        p.Proxy.Codec = ProxyCodec.Prores422;
        p.Proxy.Height = 720;
        p.Outputs.ClipListAle = true;
        p.Naming.SuggestFromClips = suggestFromClips;
        Projects.Activate(s, null);
        SettingsStore.Save(s, Path.Combine(folder, "settings.json"));
        return folder;
    }

    static string Card(TempDir dir, string name = "A001")
    {
        var card = dir.Sub(name);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        return card;
    }

    [Fact]
    public void Project_GivesTheDefaults_ReportNamesIt()
    {
        using var dir = new TempDir();
        var d1 = dir.Sub("d1");
        var settings = Settings(dir, d1);
        var seen = new List<JobConfig>();

        var r = Run(settings, seen, CopyCommand.Create(), Card(dir), "--project", "film x", "--date", "2026-10-07", "--no-history");

        Assert.Equal(0, r.Exit);
        Assert.Contains("Projekt: Film X", r.Out);
        var c = Assert.Single(seen);
        Assert.Equal([d1], c.DestRoots);
        Assert.Equal(HashKind.Both, c.Hashes);
        Assert.False(c.Verify);
        Assert.False(c.AnalyzeClips);
        Assert.Equal((ProxyCodec.Prores422, 720), (c.Proxy.Codec, c.Proxy.Height));
        Assert.True(c.Outputs.ClipListAle);
        Assert.Equal("Film X", c.Project);
        var report = Path.Combine(d1, "2026-10-07", "A001", "_REPORT");
        Assert.True(File.Exists(Path.Combine(report, "A001.md5")));
        Assert.True(File.Exists(Path.Combine(report, "A001_clips.ale")));
        Assert.Contains("\"project\": \"Film X\"", File.ReadAllText(Path.Combine(report, "offload_report.json")));
    }

    // Spec 4.1 test "přednost voleb": --project + --proxy-codec — the option wins, the rest stays the project's;
    // -d replaces the project's destinations, --hash and the flags win too.
    [Fact]
    public void WrittenOptionsWin()
    {
        using var dir = new TempDir();
        var settings = Settings(dir, dir.Sub("d1"));
        var other = dir.Sub("other");
        var seen = new List<JobConfig>();

        var r = Run(settings, seen, CopyCommand.Create(), Card(dir), "--project", "Film X", "--proxy-codec", "hevc", "-d", other,
            "--hash", "xxh64", "--no-clip-list", "--dry-run", "--no-history");

        Assert.Equal(0, r.Exit);
        var c = Assert.Single(seen);
        Assert.Equal(ProxyCodec.Hevc, c.Proxy.Codec);
        Assert.Equal(720, c.Proxy.Height);                 // not written: the project's
        Assert.Equal([other], c.DestRoots);
        Assert.Equal(HashKind.Xxh64, c.Hashes);
        Assert.False(c.Outputs.ClipListCsv);
        Assert.True(c.Outputs.ClipListAle);                // not written: the project's
        Assert.False(c.Verify);
    }

    [Fact]
    public void UnknownProject_Exit1_ListsTheProjects()
    {
        using var dir = new TempDir();
        var settings = Settings(dir, dir.Sub("d1"));
        var seen = new List<JobConfig>();

        var r = Run(settings, seen, CopyCommand.Create(), Card(dir), "--project", "Reklama", "--no-history");

        Assert.Equal(1, r.Exit);
        Assert.Contains("Chyba: Projekt „Reklama“ neexistuje. Projekty: Film X.", r.Err);
        Assert.Empty(seen);
    }

    // Without --project nothing changes: the CLI defaults, not "Bez projektu" from settings.json.
    [Fact]
    public void NoProject_CliDefaults()
    {
        using var dir = new TempDir();
        var settings = Settings(dir, dir.Sub("d1"));
        var seen = new List<JobConfig>();

        var r = Run(settings, seen, CopyCommand.Create(), Card(dir), "-d", dir.Sub("d2"), "--dry-run", "--no-history");

        Assert.Equal(0, r.Exit);
        var c = Assert.Single(seen);
        Assert.Null(c.Project);
        Assert.True(c.Verify);
        Assert.Equal(HashKind.Xxh64, c.Hashes);
        Assert.Equal((ProxyCodec.H264, 1080, LutMode.Auto), (c.Proxy.Codec, c.Proxy.Height, c.Proxy.LutMode));
        Assert.DoesNotContain("Projekt:", r.Out);
    }

    // naming.suggestFromClips of the project acts as --name auto; --name still wins.
    [Fact]
    public void ProjectNaming_NameFromClips_UnlessNameGiven()
    {
        using var dir = new TempDir();
        var d1 = dir.Sub("d1");
        var settings = Settings(dir, d1, suggestFromClips: true);
        var card = Card(dir, "EOS_DIGITAL");
        var seen = new List<JobConfig>();

        Assert.Equal(0, Run(settings, seen, CopyCommand.Create(), card, "--project", "Film X", "--date", "2026-10-07", "--no-history").Exit);
        Assert.Equal(0, Run(settings, seen, CopyCommand.Create(), card, "--project", "Film X", "--date", "2026-10-07", "--no-history", "--name", "B007").Exit);

        Assert.True(Directory.Exists(Path.Combine(d1, "2026-10-07", "A001")));
        Assert.True(Directory.Exists(Path.Combine(d1, "2026-10-07", "B007")));
        Assert.False(Directory.Exists(Path.Combine(d1, "2026-10-07", "EOS_DIGITAL")));
    }

    // A project whose custom LUT is gone stops before anything is copied (as the app's red text does).
    [Fact]
    public void ProjectsMissingLut_Refused()
    {
        using var dir = new TempDir();
        var folder = dir.Sub("appdata");
        var s = new AppSettings();
        var p = Projects.SaveAs(s, "Film X");
        p.DestRoots = [dir.Sub("d1")];
        p.Proxy.LutMode = LutMode.Custom;
        p.Proxy.LutPath = Path.Combine(dir.Path, "gone.cube");
        SettingsStore.Save(s, Path.Combine(folder, "settings.json"));
        var seen = new List<JobConfig>();

        var r = Run(folder, seen, CopyCommand.Create(), Card(dir), "--project", "Film X", "--no-history");

        Assert.Equal(1, r.Exit);
        Assert.Contains("LUT nenalezen: gone.cube", r.Err);
        Assert.Empty(seen);
    }
}
```


- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CliProjectTests"`
Expected: FAIL — build testů selže (CS0103: `CliProject` neexistuje).

- [ ] **Step 3: Implementace**

`windows/src/Offload.Cli/AppendCommand.cs`, řádky 18–26 — najít:

```csharp
        var dryRun = new Option<bool>("--dry-run") { Description = "Only scan and check, do not copy (the real run plans again under the folder lock, so it may differ)." };
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (needs --into)." };
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("append", "Copy only the new files of a card into its earlier backup (same card folders, next ASC MHL generation).");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { into, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
```

nahradit:

```csharp
        var dryRun = new Option<bool>("--dry-run") { Description = "Only scan and check, do not copy (the real run plans again under the folder lock, so it may differ)." };
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (needs --into)." };
        var project = CliProject.Create();
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("append", "Copy only the new files of a card into its earlier backup (same card folders, next ASC MHL generation).");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { into, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory, project }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
```

`windows/src/Offload.Cli/AppendCommand.cs`, řádky 29–41 — najít:

```csharp
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
```

nahradit:

```csharp
            try
            {
                // The destinations and checksums come from the earlier backup; the project gives verify and stage 2.
                var profile = CliProject.Load(r.GetValue(project));
                config = new JobConfig(CliPath.Full(r.GetValue(source)!), [])
                {
                    ChunkMB = r.GetValue(chunkMb),
                    Buffers = r.GetValue(buffers),
                    Verify = CliProject.Given(r, profile, noVerify) ? !r.GetValue(noVerify) : profile!.Verify,
                    AllowSameDevice = r.GetValue(allowSame),
                    RecordHistory = !r.GetValue(noHistory),
                    Project = profile?.Name,
                };
                stageTwo.Apply(r, config, profile);
                var folders = r.GetValue(into)!.Select(CliPath.Full).ToList();
                if (folders.Count == 0)
```

Vytvořit `windows/src/Offload.Cli/CliProject.cs`:

```csharp
using System.CommandLine;
using Offload.Core.Desktop;

namespace Offload.Cli;

/// `--project <název>` (spec 4.1): a project saved in the app (settings.json in %APPDATA%\Offload, or
/// OFFLOAD_SETTINGS_DIR) gives the defaults; every option written on the command line wins over it.
static class CliProject
{
    /// Tests: the settings.json the CLI reads (never the real %APPDATA% in a test).
    internal static Func<string> SettingsPath { get; set; } = () => SettingsStore.DefaultPath;

    public static Option<string?> Create() =>
        new("--project") { Description = "Use a project saved in the app (settings.json) for the defaults: destinations, verify, hash, outputs, proxy, notifications. Options given here win." };

    /// The project, or null without --project. An unknown name: ArgumentException with the Czech message that lists
    /// the projects there are.
    public static ProjectSettings? Load(string? name)
    {
        if (name == null) return null;
        var settings = SettingsStore.Load(SettingsPath());
        return Projects.Find(settings, name) ?? throw new ArgumentException(Projects.Unknown(name, settings));
    }

    /// The option was written on the command line (not just its default). Without a project every option counts as
    /// given, so the CLI behaves exactly as it does without --project.
    public static bool Given(ParseResult r, SettingsProfile? project, Option option) =>
        project == null || r.GetResult(option) is { Implicit: false };
}
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 24–39 — najít:

```csharp
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (%APPDATA%\\Offload\\history.json)." };
        var hash = new Option<string>("--hash") { Description = "Checksums: xxh64 (default) or both (xxh64 + MD5; MD5 is slower on fast cards).", DefaultValueFactory = _ => "xxh64" };
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory, hash }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
        {
            JobConfig config;
            try
            {
                config = new JobConfig(CliPath.Full(r.GetValue(source)!), r.GetValue(dest)!.Select(CliPath.Full).ToList())
                {
                    CardName = r.GetValue(name) is string n && !n.Equals("auto", StringComparison.OrdinalIgnoreCase) ? n : null,
```

nahradit:

```csharp
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (%APPDATA%\\Offload\\history.json)." };
        var hash = new Option<string>("--hash") { Description = "Checksums: xxh64 (default) or both (xxh64 + MD5; MD5 is slower on fast cards).", DefaultValueFactory = _ => "xxh64" };
        var project = CliProject.Create();
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory, hash, project }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
        {
            JobConfig config;
            ProjectSettings? profile;
            try
            {
                // Spec 4.1: the project's values are the defaults, what is written here wins (-d replaces its destinations).
                profile = CliProject.Load(r.GetValue(project));
                bool Given(Option o) => CliProject.Given(r, profile, o);
                config = new JobConfig(CliPath.Full(r.GetValue(source)!), (Given(dest) ? r.GetValue(dest)! : [.. profile!.DestRoots]).Select(CliPath.Full).ToList())
                {
                    CardName = r.GetValue(name) is string n && !n.Equals("auto", StringComparison.OrdinalIgnoreCase) ? n : null,
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 41–50 — najít:

```csharp
                    ChunkMB = r.GetValue(chunkMb),
                    Buffers = r.GetValue(buffers),
                    Verify = !r.GetValue(noVerify),
                    AllowSameDevice = r.GetValue(allowSame),
                    RecordHistory = !r.GetValue(noHistory),
                    Hashes = HashKinds.TryParseWire(r.GetValue(hash), out var kinds) ? kinds : throw new ArgumentException(Msg.UnknownHash(r.GetValue(hash))),
                };
                stageTwo.Apply(r, config);
            }
            catch (Exception e)
```

nahradit:

```csharp
                    ChunkMB = r.GetValue(chunkMb),
                    Buffers = r.GetValue(buffers),
                    Verify = Given(noVerify) ? !r.GetValue(noVerify) : profile!.Verify,
                    AllowSameDevice = r.GetValue(allowSame),
                    RecordHistory = !r.GetValue(noHistory),
                    Hashes = !Given(hash) ? HashKinds.FromSetting(profile!.Hash)
                        : HashKinds.TryParseWire(r.GetValue(hash), out var kinds) ? kinds : throw new ArgumentException(Msg.UnknownHash(r.GetValue(hash))),
                    Project = profile?.Name,
                };
                stageTwo.Apply(r, config, profile);
            }
            catch (Exception e)
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 53–57 — najít:

```csharp
                return 1;
            }
            bool autoName = r.GetValue(name) is string a && a.Equals("auto", StringComparison.OrdinalIgnoreCase);
            var insight = config.RecordHistory || autoName ? Inspect(config.Source, withHistory: config.RecordHistory) : null;
            if (insight?.History is { } known)
```

nahradit:

```csharp
                return 1;
            }
            if (profile != null) Console.WriteLine(Msg.CliProject(profile.Name));
            // A project that proposes names from the clips (naming.suggestFromClips) acts as --name auto unless --name is given.
            bool autoName = r.GetValue(name) is string a ? a.Equals("auto", StringComparison.OrdinalIgnoreCase) : profile?.Naming.SuggestFromClips == true;
            var insight = config.RecordHistory || autoName ? Inspect(config.Source, withHistory: config.RecordHistory) : null;
            if (insight?.History is { } known)
```

`windows/src/Offload.Cli/StageTwoOptions.cs`, řádky 30–64 — najít:

```csharp
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
        config.Outputs = new OutputSettings { ClipListCsv = !r.GetValue(noClipList), ClipListAle = r.GetValue(ale), DayReport = !r.GetValue(noDayReport) };
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

nahradit:

```csharp
    }

    /// Sets AnalyzeClips, Outputs and Proxy; throws ArgumentException with a Czech message on a bad value. `project`
    /// (--project): its values are the defaults and only the options written on the command line replace them (spec 4.1).
    public void Apply(ParseResult r, JobConfig config, SettingsProfile? project = null)
    {
        bool Given(Option o) => CliProject.Given(r, project, o);
        var p = project != null ? SettingsStore.Clone(project.Proxy) : new ProxySettings();
        if (Given(proxyCodec))
        {
            if (!ProxyCodecExtensions.TryParseWire(r.GetValue(proxyCodec)!, out var codec))
                throw new ArgumentException(Msg.UnknownProxyCodec(r.GetValue(proxyCodec)));
            p.Codec = codec;
        }
        if (Given(lut) || Given(lutFallback))
        {
            var (lutMode, lutBuiltin, lutRaw, lutFb, lutErr) = LutOption.Parse(r.GetValue(lut), r.GetValue(lutFallback), LutCatalog.Default);
            if (lutErr != null) throw new ArgumentException(lutErr);
            if (Given(lut))
            {
                p.LutMode = lutMode;
                p.LutBuiltin = lutBuiltin;
                p.LutPath = lutRaw != null ? CliPath.Full(lutRaw) : null;
                if (p.LutPath != null && !File.Exists(p.LutPath)) throw new ArgumentException(Msg.LutNotFound(p.LutPath));
            }
            if (Given(lutFallback)) p.LutFallback = lutFb;
        }
        if (Given(proxy)) p.Enabled = r.GetValue(proxy);
        if (Given(proxyHeight)) p.Height = r.GetValue(proxyHeight);
        if (Given(proxyBitrate)) p.BitrateMbps = r.GetValue(proxyBitrate);
        if (Given(frameAspect)) p.FrameAspect = r.GetValue(frameAspect);
        if (Given(frameScale)) p.FrameScale = r.GetValue(frameScale) / 100;
        if (Given(noBurnIn)) p.BurnFilename = p.BurnTimecode = !r.GetValue(noBurnIn);
        if (Given(proxyFolder)) p.ExtraFolder = r.GetValue(proxyFolder) is string pf ? CliPath.Full(pf) : null;
        config.Proxy = p;

        var outputs = project != null ? SettingsStore.Clone(project.Outputs) : new OutputSettings();
        if (Given(noClipList)) outputs.ClipListCsv = !r.GetValue(noClipList);
        if (Given(ale)) outputs.ClipListAle = r.GetValue(ale);
        if (Given(noDayReport)) outputs.DayReport = !r.GetValue(noDayReport);
        config.Outputs = outputs;
        config.AnalyzeClips = Given(noAnalyze) ? !r.GetValue(noAnalyze) : project!.AnalyzeClips;

        if (r.GetValue(proxyFolder) != null && !p.Enabled)
            Console.Error.WriteLine(Msg.ProxyFolderNoEffect);
        if (ProxyPreflight.ExtraFolderInUse(p) is string folder && !Directory.Exists(folder))
            throw new ArgumentException(Msg.ProxyFolderNotFound(folder));
        // A project's own LUT (or, from task 5, a rule's) that is gone stops the job before it starts, as in the app.
        if (ProxyPreflight.Blocking(p, config.AnalyzeClips) is string missing) throw new ArgumentException(missing);
    }
}
```

`windows/src/Offload.Core/Messages.cs`, řádky 99–102 — najít:

```csharp
    public const string CardNameAutoNone = "Návrh názvu nenalezen, použije se jmenovka svazku.";
    public static string CardNameAuto(string name) => $"Název karty: {name} (podle klipů)";
    public static string CardUnreadable(string detail) => $"Kartu nejde přečíst: {detail}";

```

nahradit:

```csharp
    public const string CardNameAutoNone = "Návrh názvu nenalezen, použije se jmenovka svazku.";
    public static string CardNameAuto(string name) => $"Název karty: {name} (podle klipů)";
    public static string CliProject(string name) => $"Projekt: {name} (výchozí hodnoty z nastavení aplikace, volby z příkazové řádky mají přednost)";
    public static string CardUnreadable(string detail) => $"Kartu nejde přečíst: {detail}";

```


- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CliProjectTests|FullyQualifiedName~CliOutputsTests|FullyQualifiedName~CliAppendTests|FullyQualifiedName~PreflightMessagesTests"`
Expected: PASS (bez `--project` se CLI chová přesně jako dřív — `NoProject_CliDefaults` a dosavadní CLI testy). Pak celá sada (`-warnaserror`, vše zelené, 1057 testů: 1050 úspěšných + 7 přeskočených).

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 22–23 — najít:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 4 — CLI `--project <název>` (`copy`, `append`): projekt uložený v aplikaci dává výchozí hodnoty (cíle, ověření, součet, analýza, výstupy, proxy včetně LUTu), napsané volby vyhrávají (`-d` nahradí cíle projektu); projekt, který navrhuje název z klipů, se chová jako `--name auto`; řádek „Projekt: Film X (…)“; neznámý projekt → „Chyba: Projekt „X“ neexistuje. Projekty: …“ (kód 1); chybějící vlastní LUT projektu zastaví start; bez `--project` beze změny
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Windows: CLI --project (project defaults, written options win)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Cli/CliProject.cs windows/src/Offload.Cli/CopyCommand.cs windows/src/Offload.Cli/AppendCommand.cs windows/src/Offload.Cli/StageTwoOptions.cs windows/src/Offload.Core/Messages.cs windows/tests/Offload.Core.Tests/CliProjectTests.cs
git commit -F .git/COMMIT_MSG_PLAN8
```

---

### Task 5: Pravidla LUTů — jádro

Spec §4.2 (pravidlo, `match`, výsledek, jádro, `CameraSerial`, LUT podle cesty v rendereru, preflight), R18, §5.1, §5.3. Pravidla jsou součástí `proxy` (tedy i projektu a reportu). V GUI se jen rozšíří kontrola souborů LUTů (vlastní LUT i `.cube` z pravidel, mimo UI vlákno, červená hláška „LUT z pravidla 2 nenalezen: show.cube“); editor přijde v úkolu 6.

**Files:**
- Create: `windows/src/Offload.Core/LutRules.cs`
- Modify: `windows/src/Offload.Core/Media.cs:69-72, 199-202`
- Modify: `windows/src/Offload.Core/Luts.cs:130-135, 138-142, 151-154`
- Modify: `windows/src/Offload.Core/ClipDetector.cs:126-130`
- Modify: `windows/src/Offload.Core/ProxyRenderer.cs:1-4, 27-34, 72-81, 118-122, 191-206`
- Modify: `windows/src/Offload.Core/OffloadJob.cs:528-532`
- Modify: `windows/src/Offload.Core/Desktop/AppSettings.cs:251-254, 283-297`
- Modify: `windows/src/Offload.Core/Messages.cs:153-156`
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs:30-36, 460-472, 476-480, 482-489, 495-504`
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs:72-84`
- Test: `windows/tests/Offload.Core.Tests/LutRulesTests.cs` (nový), `windows/tests/Offload.Core.Tests/SettingsStoreTests.cs:172-181`
- Modify: `docs/VYVOJ-WINDOWS.md:22-23`

**Interfaces:**
- Consumes: `ReelResolver.Resolve` (plán 7), `LutUse`, `LutCatalog`, `SettingsStore.Normalize` (úkol 1), `ProxyPreflight` (existuje).
- Produces: `sealed class LutRule` (`Match`, `Value`, `Lut`, `LutPath`, `Extra`; konstanty `Camera`, `Serial`, `Reel`, `Folder`, `Kinds`); `static class LutRules` — `IsValid(LutRule?)`, `Valid(IEnumerable<LutRule?>?) → List<LutRule>`, `Match(IReadOnlyList<LutRule>, Clip, ClipInfo?, string? reel, string cardName) → (int Number, LutRule Rule)?`, `FolderOf(Clip, string) → string`, `Describe(int, LutRule) → string`, `Use(int, LutRule, LutCatalog) → LutUse`; `ProxySettings.LutRules` (`List<LutRule>`, JSON `lutRules` přes `LutRulesJson`, chybí když prázdné); `ClipInfo.CameraSerial` (`cameraSerial`); `SonyXml.Serial(string) → string?`; `LutResolver.Resolve(…, LutCatalog cat, string cardName = "")`; `internal string? ProxyRenderer.PrepareLut(LutUse?)`; `sealed record LutFile(string Path, int? Rule)`; `ProxyPreflight.LutFilesInUse(ProxySettings, bool) → IReadOnlyList<LutFile>`, `LutKey(IReadOnlyList<LutFile>) → string`, `FirstMissing(IReadOnlyList<LutFile>) → LutFile?`, `MissingText(LutFile?) → string?`; `Msg.RuleLutNotFound(int, string)`.

- [ ] **Step 1: Padající testy**

Vytvořit `windows/tests/Offload.Core.Tests/LutRulesTests.cs`:

```csharp
using System.Text.Json;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// Spec 4.2, R18: own LUT rules — tried top to bottom, only in Auto mode, before the camera is recognised.
public class LutRulesTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    static Clip VideoClip(string path = "XDROOT/Clip/C0001.MP4") =>
        new() { Name = Path.GetFileNameWithoutExtension(path), Path = path, Format = ClipFormat.Video, Segments = [path], Bytes = 1 };

    static LutRule Rule(string match, string value, string? lut = null, string? path = null) => new() { Match = match, Value = value, Lut = lut, LutPath = path };

    static ProxySettings Auto(params LutRule[] rules) => new() { Enabled = true, LutMode = LutMode.Auto, LutRules = [.. rules] };

    [Fact]
    public void EveryMatch_AndTheReason()
    {
        var info = new ClipInfo { Camera = "Sony ILME-FX6V", CameraSerial = "0123456", Reel = "A007" };
        var clip = VideoClip("XDROOT/Clip/C0001.MP4");
        (string Match, string Value, string Reason)[] cases =
        [
            ("camera", "fx6", "pravidlo 1: kamera obsahuje „fx6“"),
            ("serial", " 0123456 ", "pravidlo 1: sériové číslo kamery „0123456“"),
            ("reel", "A", "pravidlo 1: cívka začíná „A“"),
            ("reel", "a00", "pravidlo 1: cívka začíná „a00“"),
            ("folder", "a001/xdroot/*", "pravidlo 1: složka odpovídá „a001/xdroot/*“"),
            ("folder", "A00?/*/Clip", "pravidlo 1: složka odpovídá „A00?/*/Clip“"),
        ];
        foreach (var (match, value, reason) in cases)
        {
            var use = LutResolver.Resolve(clip, info, @"X:\none\C0001.MP4", Auto(Rule(match, value, "arri-logc4-awg4")), Cat, "A001");
            Assert.Equal(("rule", "arri-logc4-awg4", reason), (use.Source, use.Id, use.Reason));
            Assert.True(File.Exists(use.File));
        }
    }

    [Fact]
    public void NoMatch_FallsThroughToAuto()
    {
        var info = new ClipInfo { Camera = "ARRI ALEXA 35", CameraSerial = "35-1", Reel = "B001" };
        var s = Auto(Rule("camera", "FX6", "none"), Rule("serial", "35", "none"), Rule("reel", "A", "none"),
            Rule("folder", "A001/CLIP", "none"), Rule("folder", "A001", "none"));
        var use = LutResolver.Resolve(VideoClip("XDROOT/Clip/C0001.MP4"), info, @"X:\none\C0001.MP4", s, Cat, "A001");
        Assert.Equal(("auto", "arri-logc4-awg4"), (use.Source, use.Id));
    }

    // The order is the priority; "none" decides too (no LUT, and no camera detection after it).
    [Fact]
    public void FirstMatchWins_NoneDecides()
    {
        var info = new ClipInfo { Camera = "ARRI ALEXA 35", Reel = "A001" };
        var s = Auto(Rule("camera", "Sony", "sony-slog3-sgamut3"), Rule("reel", "A", "none"), Rule("camera", "ALEXA", "arri-logc3-awg3"));
        var use = LutResolver.Resolve(VideoClip(), info, @"X:\none\C0001.MP4", s, Cat, "A001");
        Assert.Equal(("none", null, "pravidlo 2: cívka začíná „A“"), (use.Source, use.Id, use.Reason));
        Assert.Null(use.File);
    }

    [Fact]
    public void OwnCube_IsTheFile()
    {
        using var dir = new TempDir();
        var cube = Path.Combine(dir.Path, "show.cube");
        TestMedia.IdentityLut(cube);
        var use = LutResolver.Resolve(VideoClip(), new ClipInfo { Camera = "FX6" }, @"X:\none\C0001.MP4", Auto(Rule("camera", "FX6", path: cube)), Cat, "A001");
        Assert.Equal(("rule", null, "show.cube", cube), (use.Source, use.Id, use.Name, use.File));
        Assert.False(use.IsBuiltin);
    }

    // R18: rules only in Auto mode — a chosen built-in, custom or no LUT is the user's explicit choice.
    [Theory]
    [InlineData(LutMode.None, "none")]
    [InlineData(LutMode.Builtin, "builtin")]
    public void OnlyInAutoMode(LutMode mode, string source)
    {
        var s = Auto(Rule("camera", "FX6", "arri-logc4-awg4"));
        s.LutMode = mode;
        s.LutBuiltin = "sony-slog3-sgamut3";
        var use = LutResolver.Resolve(VideoClip(), new ClipInfo { Camera = "FX6" }, @"X:\none\C0001.MP4", s, Cat, "A001");
        Assert.Equal(source, use.Source);
    }

    // Unknown serial number: a "serial" rule never matches. Without a reel tag the reel comes from the name / card.
    [Fact]
    public void UnknownSerial_NeverMatches_ReelFromCardFolder()
    {
        var s = Auto(Rule("serial", "123", "none"), Rule("reel", "B0", "arri-logc4-awg4"));
        var use = LutResolver.Resolve(VideoClip("XDROOT/Clip/C0001.MP4"), new ClipInfo(), @"X:\none\C0001.MP4", s, Cat, "B007");
        Assert.Equal(("rule", "pravidlo 2: cívka začíná „B0“"), (use.Source, use.Reason));
    }

    [Fact]
    public void BuiltinMissingFromTheCatalog_NoLut_Said()
    {
        var use = LutResolver.Resolve(VideoClip(), new ClipInfo { Camera = "FX6" }, @"X:\none\C0001.MP4", Auto(Rule("camera", "FX6", "gone-id")), Cat, "A001");
        Assert.Equal(("none", "pravidlo 1: kamera obsahuje „FX6“ — vestavěný LUT nenalezen"), (use.Source, use.Reason));
    }

    // Spec 4.2: an invalid rule (no value, both or neither LUT, unknown match) is dropped when the settings are read.
    [Fact]
    public void InvalidRules_DroppedOnLoad_ValidKeptInOrder()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """
            {"proxy":{"lutRules":[
              {"match":"camera","value":" FX6 ","lut":"sony-slog3-sgamut3cine","lens":"35mm"},
              {"match":"camera","value":"","lut":"none"},
              {"match":"camera","value":"X","lut":"none","lutPath":"C:\\a.cube"},
              {"match":"camera","value":"X"},
              {"match":"lens","value":"X","lut":"none"},
              null,
              {"match":"folder","value":"A001/*","lutPath":"C:\\show.cube"}
            ]}}
            """);
        var s = SettingsStore.Load(path);
        Assert.Equal([("camera", "FX6"), ("folder", "A001/*")], s.Proxy.LutRules.Select(r => (r.Match, r.Value)));
        Assert.Equal(@"C:\show.cube", s.Proxy.LutRules[1].LutPath);

        SettingsStore.Save(s, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var rules = doc.RootElement.GetProperty("proxy").GetProperty("lutRules");
        Assert.Equal(2, rules.GetArrayLength());
        Assert.Equal("35mm", rules[0].GetProperty("lens").GetString());   // a newer version's key inside a rule stays
        Assert.False(rules[0].TryGetProperty("lutPath", out _));
    }

    [Fact]
    public void NoRules_NoKey_InSettingsOrReport()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        SettingsStore.Save(new AppSettings(), path);
        Assert.DoesNotContain("lutRules", File.ReadAllText(path));
        var clone = SettingsStore.Clone(Auto(Rule("reel", "A", "none")));
        Assert.Single(clone.LutRules);
    }

    // Spec 4.2: a rule's own .cube that is missing blocks the start — "LUT z pravidla 2 nenalezen: show.cube".
    [Fact]
    public void Preflight_RuleCube_Missing_Blocks_OnlyInAutoAndWhenUsed()
    {
        using var dir = new TempDir();
        var there = Path.Combine(dir.Path, "there.cube");
        TestMedia.IdentityLut(there);
        var s = Auto(Rule("camera", "FX6", path: there), Rule("reel", "A", "none"), Rule("folder", "*", path: Path.Combine(dir.Path, "show.cube")));
        Assert.Equal([new LutFile(there, 1), new LutFile(Path.Combine(dir.Path, "show.cube"), 3)], ProxyPreflight.LutFilesInUse(s, analyzeClips: false));
        Assert.Equal("LUT z pravidla 3 nenalezen: show.cube", ProxyPreflight.Blocking(s, analyzeClips: false));

        s.Enabled = false;
        Assert.Null(ProxyPreflight.Blocking(s, analyzeClips: false));       // nothing uses a LUT
        Assert.NotNull(ProxyPreflight.Blocking(s, analyzeClips: true));     // thumbnails do
        s.LutMode = LutMode.None;
        Assert.Empty(ProxyPreflight.LutFilesInUse(s, analyzeClips: true));
        s.LutMode = LutMode.Custom;
        s.LutPath = Path.Combine(dir.Path, "gone.cube");
        Assert.Equal("LUT nenalezen: gone.cube — vyberte jiný nebo ho odeberte.", ProxyPreflight.Blocking(s, analyzeClips: true));
    }

    [Fact]
    public void FolderOf_CardAndClipFolder()
    {
        Assert.Equal("A001/CLIP", LutRules.FolderOf(VideoClip("CLIP/A001C001.mov"), "A001"));
        Assert.Equal("A001/PRIVATE/M4ROOT/CLIP", LutRules.FolderOf(VideoClip("PRIVATE/M4ROOT/CLIP/C0001.MP4"), "A001"));
        Assert.Equal("A001", LutRules.FolderOf(VideoClip("A001C001.mov"), "A001"));
    }

    // Spec 4.2: the camera serial number — Sony XML `Device serialNo`, ffprobe tags.
    [Fact]
    public void CameraSerial_SonyXmlAndFfprobeTags()
    {
        using var dir = new TempDir();
        var clip = Path.Combine(dir.Path, "C0001.MP4");
        File.WriteAllText(clip, "x");
        File.WriteAllText(Path.Combine(dir.Path, "C0001M01.XML"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <NonRealTimeMeta xmlns="urn:schemas-professionalDisc:nonRealTimeMeta:ver.2.10">
              <Device manufacturer="Sony" modelName="ILME-FX6V" serialNo=" 5012345 "/>
            </NonRealTimeMeta>
            """);
        Assert.Equal("5012345", SonyXml.Serial(clip));
        Assert.Null(SonyXml.Serial(Path.Combine(dir.Path, "C0002.MP4")));

        var apple = FFProbe.Parse("""{"streams":[],"format":{"tags":{"com.apple.quicktime.camera.identifier":"BMPCC6K-1234"}}}""");
        Assert.Equal("BMPCC6K-1234", apple.CameraSerial);
        var stream = FFProbe.Parse("""{"streams":[{"codec_type":"video","tags":{"Camera_Serial_Number":" 77 "}}],"format":{}}""");
        Assert.Equal("77", stream.CameraSerial);
        Assert.Null(FFProbe.Parse("""{"streams":[],"format":{"tags":{"camera_serial_number":"  "}}}""").CameraSerial);
    }

    [Fact]
    public void CameraSerial_InTheReportOnlyWhenKnown()
    {
        Assert.Contains("\"cameraSerial\":\"5012345\"", JsonSerializer.Serialize(new ClipInfo { CameraSerial = "5012345" }));
        Assert.DoesNotContain("cameraSerial", JsonSerializer.Serialize(new ClipInfo()));
    }

    // The job hands the card folder and the reel to the rules: a folder rule and a reel rule decide two clips of one card
    // (report `lut.source = "rule"` with the reason), a third clip falls through to the camera detection.
    [FfmpegFact]
    public void Job_RulesDecide_ReportSaysRule()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "B", "A001C001.mov"), seconds: 1, audio: false, width: 320, height: 180);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "B002C001.mov"), seconds: 1, audio: false, width: 320, height: 180);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "C0001.mov"), seconds: 1, audio: false, width: 320, height: 180);
        var proxy = Auto(Rule("folder", "A001/B", "arri-logc3-awg3"), Rule("reel", "B", "none"));
        proxy.Enabled = false;
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-07", RecordHistory = false, Proxy = proxy },
            AppendFixtures.Options());

        var report = job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        var luts = report!.Clips.ToDictionary(c => c.Clip.Name, c => c.Lut!);
        Assert.Equal(("rule", "pravidlo 1: složka odpovídá „A001/B“"), (luts["A001C001"].Source, luts["A001C001"].Reason));
        Assert.Equal(("none", "pravidlo 2: cívka začíná „B“"), (luts["B002C001"].Source, luts["B002C001"].Reason));
        Assert.NotEqual("rule", luts["C0001"].Source);
        Assert.Contains("\"source\": \"rule\"", File.ReadAllText(Path.Combine(job.Plan!.CardPaths[0], "_REPORT", "offload_report.json")));
    }

    // Spec 4.2: two rules with two own .cube files in one job — each gets its own file in the renderer's work folder.
    [Fact]
    public void Renderer_TwoOwnCubes_TwoFiles_OnceEach()
    {
        using var dir = new TempDir();
        var a = Path.Combine(dir.Path, "a", "show.cube");
        var b = Path.Combine(dir.Path, "b", "show.cube");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        TestMedia.IdentityLut(a);
        File.WriteAllText(b, "TITLE \"b\"\nLUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
        var logs = new List<string>();
        using var r = new ProxyRenderer(new ProxySettings(), new ExternalTools { NoSearch = true }) { Log = logs.Add };

        var na = r.PrepareLut(new LutUse("rule", null, "show.cube", "x") { File = a });
        var nb = r.PrepareLut(new LutUse("rule", null, "show.cube", "y") { File = b });
        var again = r.PrepareLut(new LutUse("custom", null, "show.cube", "z") { File = a.ToUpperInvariant() });
        var gone = r.PrepareLut(new LutUse("rule", null, "gone.cube", "z") { File = Path.Combine(dir.Path, "gone.cube") });

        Assert.Matches("^lut-custom-[0-9a-f]{8}\\.cube$", na!);
        Assert.NotEqual(na, nb);
        Assert.Equal(na, again);
        Assert.Null(gone);
        Assert.Contains(logs, l => l.Contains("gone.cube"));
        Assert.Equal("lut-arri-logc4-awg4.cube", r.PrepareLut(new LutUse("rule", "arri-logc4-awg4", "x", "y") { File = Cat.Find("arri-logc4-awg4")!.Path }));
    }
}
```

`windows/tests/Offload.Core.Tests/SettingsStoreTests.cs`, řádky 172–181 — najít:

```csharp
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, """{"proxy":{"lutRules":[]},"updates":{"manifest":null}}""");
        Assert.True(SettingsStore.Clone(s.Proxy).Extra!.ContainsKey("lutRules"));
        Assert.True(SettingsStore.Clone(s).Extra!.ContainsKey("updates"));
        var fresh = Path.Combine(dir.Path, "fresh.json");
        SettingsStore.Save(new AppSettings(), fresh);
        Assert.DoesNotContain("Extra", File.ReadAllText(fresh));
        Assert.DoesNotContain("lutRules", File.ReadAllText(fresh));
    }
```

nahradit:

```csharp
    {
        using var dir = new TempDir();
        // Keys no version knows yet (plan 8 made "lutRules" and "updates" known keys).
        var s = LoadJson(dir, """{"proxy":{"futureRules":[]},"futureTop":{"manifest":null}}""");
        Assert.True(SettingsStore.Clone(s.Proxy).Extra!.ContainsKey("futureRules"));
        Assert.True(SettingsStore.Clone(s).Extra!.ContainsKey("futureTop"));
        var fresh = Path.Combine(dir.Path, "fresh.json");
        SettingsStore.Save(new AppSettings(), fresh);
        Assert.DoesNotContain("Extra", File.ReadAllText(fresh));
        Assert.DoesNotContain("futureRules", File.ReadAllText(fresh));
        Assert.DoesNotContain("lutRules", File.ReadAllText(fresh));
    }
```


- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~LutRulesTests|FullyQualifiedName~SettingsStoreTests"`
Expected: FAIL — build testů selže (CS0246 / CS0117: `LutRule`, `LutRules`, `LutFile`, `ProxySettings.LutRules`, `ClipInfo.CameraSerial`, `SonyXml.Serial`, `ProxyRenderer.PrepareLut` neexistují).

- [ ] **Step 3: Implementace**

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 72–84 — najít:

```csharp
        try
        {
            while (ProxyPreflight.LutInUse(Settings.Proxy, Settings.AnalyzeClips) is string lut)
            {
                var check = Task.Run(() => File.Exists(lut));
                bool answered = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(5))) == check;
                bool exists = answered && check.IsCompletedSuccessfully && check.Result;
                if (!answered) AppLog.Write($"LUT check before start: no answer from {lut} in 5 s");
                if (ProxyPreflight.LutInUse(Settings.Proxy, Settings.AnalyzeClips) != lut) continue;   // changed meanwhile
                SetLutState(lut, exists);
                if (!exists) return;   // the red text says why
                break;
            }
```

nahradit:

```csharp
        try
        {
            while (LutFiles is { Count: > 0 } luts)
            {
                var key = ProxyPreflight.LutKey(luts);
                var check = Task.Run(() => ProxyPreflight.FirstMissing(luts));
                bool answered = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(5))) == check;
                // No answer in 5 s (a disk that hangs) counts as missing: the first file is named.
                var missing = answered && check.IsCompletedSuccessfully ? check.Result : luts[0];
                if (!answered) AppLog.Write($"LUT check before start: no answer in 5 s ({key})");
                if (ProxyPreflight.LutKey(LutFiles) != key) continue;   // changed meanwhile
                SetLutState(key, missing);
                if (missing != null) return;   // the red text says why
                break;
            }
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 30–36 — najít:

```csharp
    // (a NAS that dropped out), the LUT and the proxy folder are checked again.
    readonly DispatcherTimer healthTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    // The LUT's existence, checked off the UI thread (CanStart is evaluated 3×/s and must never touch a disk).
    string? lutCheckedPath;
    bool? lutExists;
    bool lutChecking, lutRecheck;
    // The same for the extra proxy folder (a disk that may be disconnected).
```

nahradit:

```csharp
    // (a NAS that dropped out), the LUT and the proxy folder are checked again.
    readonly DispatcherTimer healthTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    // The own LUT files' existence (custom LUT, rules), checked off the UI thread (CanStart is evaluated 3×/s and must
    // never touch a disk): the files checked and the first missing one.
    string? lutCheckedKey;
    LutFile? lutMissing;
    bool lutChecking, lutRecheck;
    // The same for the extra proxy folder (a disk that may be disconnected).
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 460–472 — najít:

```csharp
    // ── Proxy a dekodéry ───────────────────────────────────────────────────

    /// A missing LUT blocks the start (red); missing ffmpeg only warns (orange). From the cached check — no disk here.
    public string? LutProblem => ProxyPreflight.Blocking(Settings.Proxy, Settings.AnalyzeClips, CachedLutExists);

    bool? CachedLutExists =>
        ProxyPreflight.LutInUse(Settings.Proxy, Settings.AnalyzeClips) is string lut && string.Equals(lut, lutCheckedPath, StringComparison.OrdinalIgnoreCase)
            ? lutExists
            : null;

    /// Checks the LUT off the UI thread; a request during a check runs once more afterwards.
    void RefreshLut()
    {
```

nahradit:

```csharp
    // ── Proxy a dekodéry ───────────────────────────────────────────────────

    /// A missing LUT (custom, or an own rule's) blocks the start (red); missing ffmpeg only warns (orange). From the
    /// cached check — no disk here; while the files in use have not been checked yet, nothing blocks.
    public string? LutProblem =>
        lutCheckedKey != null && lutCheckedKey == ProxyPreflight.LutKey(LutFiles) ? ProxyPreflight.MissingText(lutMissing) : null;

    IReadOnlyList<LutFile> LutFiles => ProxyPreflight.LutFilesInUse(Settings.Proxy, Settings.AnalyzeClips);

    /// Checks the LUT files off the UI thread; a request during a check runs once more afterwards.
    void RefreshLut()
    {
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 476–480 — najít:

```csharp
            return;
        }
        if (ProxyPreflight.LutInUse(Settings.Proxy, Settings.AnalyzeClips) is not string lut)
        {
            SetLutState(null, null);
```

nahradit:

```csharp
            return;
        }
        var files = LutFiles;
        if (files.Count == 0)
        {
            SetLutState(null, null);
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 482–489 — najít:

```csharp
        }
        lutChecking = true;
        Task.Run(() => File.Exists(lut)).ContinueWith(t =>
        {
            lutChecking = false;
            if (t.IsCompletedSuccessfully) SetLutState(lut, t.Result);
            else AppLog.Write($"LUT check: {t.Exception?.GetBaseException().Message}");
            if (lutRecheck)
```

nahradit:

```csharp
        }
        lutChecking = true;
        Task.Run(() => ProxyPreflight.FirstMissing(files)).ContinueWith(t =>
        {
            lutChecking = false;
            if (t.IsCompletedSuccessfully) SetLutState(ProxyPreflight.LutKey(files), t.Result);
            else AppLog.Write($"LUT check: {t.Exception?.GetBaseException().Message}");
            if (lutRecheck)
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 495–504 — najít:

```csharp
    }

    /// A new answer about the LUT: the red text and the start button follow it.
    void SetLutState(string? lut, bool? exists)
    {
        var before = LutProblem;
        lutCheckedPath = lut;
        lutExists = exists;
        if (LutProblem == before) return;
        RaiseMany(nameof(LutProblem), nameof(CanStart));
```

nahradit:

```csharp
    }

    /// A new answer about the LUT files (`key` = which, null = none in use): the red text and the start button follow it.
    void SetLutState(string? key, LutFile? missing)
    {
        var before = LutProblem;
        lutCheckedKey = key;
        lutMissing = missing;
        if (LutProblem == before) return;
        RaiseMany(nameof(LutProblem), nameof(CanStart));
```

`windows/src/Offload.Core/ClipDetector.cs`, řádky 126–130 — najít:

```csharp
            ?? Str(ftags, "product_name") ?? Str(ftags, "company_name");
        info.Reel = new[] { format }.Concat(streams).Select(x => Reel(Tags(x))).FirstOrDefault(r => r != null);
        return info;
    }

```

nahradit:

```csharp
            ?? Str(ftags, "product_name") ?? Str(ftags, "company_name");
        info.Reel = new[] { format }.Concat(streams).Select(x => Reel(Tags(x))).FirstOrDefault(r => r != null);
        info.CameraSerial = new[] { format }.Concat(streams).Select(x => Tag(Tags(x), SerialTags)).FirstOrDefault(t => t != null);
        return info;
    }

    /// Tags that carry the camera body's serial number (spec 4.2): QuickTime (Apple, Blackmagic, Canon), others.
    static readonly string[] SerialTags = ["com.apple.quicktime.camera.identifier", "camera_serial_number"];

    /// The first of `names` the tags have (regardless of case), trimmed and not empty; else null.
    static string? Tag(JsonElement tags, IReadOnlyList<string> names)
    {
        if (tags.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
            foreach (var p in tags.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String
                    && p.Value.GetString()!.Trim() is { Length: > 0 } value)
                    return value;
        return null;
    }

```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 251–254 — najít:

```csharp
        }
        p.ExtraFolder = string.IsNullOrWhiteSpace(p.ExtraFolder) ? null : p.ExtraFolder.Trim();
    }

```

nahradit:

```csharp
        }
        p.ExtraFolder = string.IsNullOrWhiteSpace(p.ExtraFolder) ? null : p.ExtraFolder.Trim();
        p.LutRules = LutRules.Valid(p.LutRules);   // spec 4.2: an invalid rule is dropped when the settings are read
    }

```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 283–297 — najít:

```csharp
}

/// Checks before a start that the proxy/thumbnail settings can work.
public static class ProxyPreflight
{
    /// The custom .cube that must exist before a start (only in custom mode — built-in LUTs ship with the app).
    public static string? LutInUse(ProxySettings p, bool analyzeClips) =>
        (p.Enabled || analyzeClips) && LutResolver.EffectiveMode(p) == LutMode.Custom ? p.LutPath : null;

    /// Why "Spustit offload" must stay disabled, or null. A missing LUT would break thumbnails and proxies.
    /// Touches the disk — the GUI uses the overload with a cached answer instead.
    public static string? Blocking(ProxySettings p, bool analyzeClips) =>
        Blocking(p, analyzeClips, LutInUse(p, analyzeClips) is string lut ? File.Exists(lut) : null);

    /// The same from an answer checked off the UI thread: `lutExists` null = not checked yet (does not block).
```

nahradit:

```csharp
}

/// An own .cube a start needs: the custom LUT (Rule null) or the one of own rule number `Rule` (spec 4.2).
public sealed record LutFile(string Path, int? Rule);

/// Checks before a start that the proxy/thumbnail settings can work.
public static class ProxyPreflight
{
    /// Every own .cube the next start uses (only when proxies or thumbnails are made): the custom LUT in custom mode,
    /// the rules' files in Auto mode. Built-in LUTs ship with the app and are not checked.
    public static IReadOnlyList<LutFile> LutFilesInUse(ProxySettings p, bool analyzeClips)
    {
        if (!p.Enabled && !analyzeClips) return [];
        return LutResolver.EffectiveMode(p) switch
        {
            LutMode.Custom => p.LutPath is string path ? [new LutFile(path, null)] : [],
            LutMode.Auto => p.LutRules.Select((r, i) => (r.LutPath, Number: i + 1)).Where(x => x.LutPath != null)
                .Select(x => new LutFile(x.LutPath!, x.Number)).ToList(),
            _ => [],
        };
    }

    /// What the files are — the cache key of a check made off the UI thread.
    public static string LutKey(IReadOnlyList<LutFile> files) => string.Join("\n", files.Select(f => $"{f.Rule}|{f.Path}"));

    /// The first file that is not there, or null. Touches the disk.
    public static LutFile? FirstMissing(IReadOnlyList<LutFile> files) => files.FirstOrDefault(f => !File.Exists(f.Path));

    /// The red text for a missing file: "LUT z pravidla 2 nenalezen: show.cube" (spec 4.2) or the custom LUT's.
    public static string? MissingText(LutFile? missing) => missing == null ? null
        : missing.Rule is int n ? Msg.RuleLutNotFound(n, Path.GetFileName(missing.Path))
        : $"LUT nenalezen: {Path.GetFileName(missing.Path)} — vyberte jiný nebo ho odeberte.";

    /// The custom .cube that must exist before a start (only in custom mode — built-in LUTs ship with the app).
    public static string? LutInUse(ProxySettings p, bool analyzeClips) =>
        (p.Enabled || analyzeClips) && LutResolver.EffectiveMode(p) == LutMode.Custom ? p.LutPath : null;

    /// Why "Spustit offload" must stay disabled, or null: a missing custom LUT or a rule's .cube would break thumbnails and
    /// proxies. Touches the disk — the GUI checks off the UI thread and keeps the answer (LutFilesInUse, FirstMissing).
    public static string? Blocking(ProxySettings p, bool analyzeClips) => MissingText(FirstMissing(LutFilesInUse(p, analyzeClips)));

    /// The same from an answer checked off the UI thread: `lutExists` null = not checked yet (does not block).
```

Vytvořit `windows/src/Offload.Core/LutRules.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Offload.Core;

/// One own LUT rule (spec 4.2, `proxy.lutRules`): "this camera / serial number / reel / folder → this LUT". Exactly one of
/// `lut` (a built-in id or "none") and `lutPath` (an own .cube).
public sealed class LutRule
{
    public const string Camera = "camera", Serial = "serial", Reel = "reel", Folder = "folder";
    public static readonly IReadOnlyList<string> Kinds = [Camera, Serial, Reel, Folder];

    [JsonPropertyName("match")] public string Match { get; set; } = Camera;
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("lut"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Lut { get; set; }
    [JsonPropertyName("lutPath"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LutPath { get; set; }

    /// Keys of a rule a newer version wrote: kept (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// Own LUT rules (spec 4.2, R18): tried top to bottom, only in "Automaticky podle kamery", before the camera is
/// recognised. The first rule that matches decides — also "none".
public static class LutRules
{
    /// A rule this version can use: a known match, a value, exactly one of lut / lutPath.
    public static bool IsValid(LutRule? r) =>
        r != null && LutRule.Kinds.Contains(r.Match) && !string.IsNullOrWhiteSpace(r.Value)
        && string.IsNullOrWhiteSpace(r.Lut) != string.IsNullOrWhiteSpace(r.LutPath);

    /// The usable rules in their order (spec 4.2: an invalid rule is dropped when the settings are read), values trimmed.
    public static List<LutRule> Valid(IEnumerable<LutRule?>? rules) =>
        (rules ?? []).Where(IsValid).Select(r => new LutRule
        {
            Match = r!.Match,
            Value = r.Value.Trim(),
            Lut = string.IsNullOrWhiteSpace(r.Lut) ? null : r.Lut.Trim(),
            LutPath = string.IsNullOrWhiteSpace(r.LutPath) ? null : r.LutPath.Trim(),
            Extra = r.Extra,
        }).ToList();

    /// The first rule that matches the clip, with its number (1 = the top one), or null. `reel`: ReelResolver's answer;
    /// `cardName`: the card folder (the start of the folder pattern).
    public static (int Number, LutRule Rule)? Match(IReadOnlyList<LutRule> rules, Clip clip, ClipInfo? info, string? reel, string cardName)
    {
        for (int i = 0; i < rules.Count; i++)
            if (IsValid(rules[i]) && Matches(rules[i], clip, info, reel, cardName)) return (i + 1, rules[i]);
        return null;
    }

    static bool Matches(LutRule r, Clip clip, ClipInfo? info, string? reel, string cardName)
    {
        var value = r.Value.Trim();
        return r.Match switch
        {
            LutRule.Camera => info?.Camera is string camera && camera.Contains(value, StringComparison.OrdinalIgnoreCase),
            LutRule.Serial => info?.CameraSerial?.Trim() is { Length: > 0 } serial && string.Equals(serial, value, StringComparison.OrdinalIgnoreCase),
            LutRule.Reel => reel?.Trim() is { Length: > 0 } rl && rl.StartsWith(value, StringComparison.OrdinalIgnoreCase),
            LutRule.Folder => Glob(value).IsMatch(FolderOf(clip, cardName)),
            _ => false,
        };
    }

    /// `<card>/<the clip's folder>` with "/" ("A001/CLIP"); a clip at the card's top is just "A001".
    public static string FolderOf(Clip clip, string cardName)
    {
        var path = clip.Path.Replace('\\', '/');
        int slash = path.LastIndexOf('/');
        return slash > 0 ? $"{cardName}/{path[..slash]}" : cardName;
    }

    /// `*` = any characters (also "/"), `?` = one character, regardless of case; the whole text must match.
    static Regex Glob(string pattern) =>
        new("^" + Regex.Escape(pattern.Replace('\\', '/')).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// How the report gives the reason: "pravidlo 2: kamera obsahuje „FX6“".
    public static string Describe(int number, LutRule r) => $"pravidlo {number}: " + r.Match switch
    {
        LutRule.Camera => $"kamera obsahuje „{r.Value.Trim()}“",
        LutRule.Serial => $"sériové číslo kamery „{r.Value.Trim()}“",
        LutRule.Reel => $"cívka začíná „{r.Value.Trim()}“",
        _ => $"složka odpovídá „{r.Value.Trim()}“",
    };

    /// The LUT of a matching rule: an own .cube (source "rule"), a built-in one (source "rule"), or none — "none" keeps
    /// the source "none" so a clip without a LUT counts as one everywhere (day report, clip lists).
    public static LutUse Use(int number, LutRule r, LutCatalog cat)
    {
        var reason = Describe(number, r);
        if (r.LutPath is string path) return new LutUse("rule", null, Path.GetFileName(path), reason) { File = path };
        if (string.Equals(r.Lut, "none", StringComparison.OrdinalIgnoreCase)) return LutUse.None(reason);
        return cat.Find(r.Lut) is { } lut
            ? new LutUse("rule", lut.Id, lut.Name, reason) { File = lut.Path }
            : LutUse.None($"{reason} — vestavěný LUT nenalezen");
    }
}
```

`windows/src/Offload.Core/Luts.cs`, řádky 130–135 — najít:

```csharp
            return null;
        }
    }
}

/// Which LUT a clip gets (spec: luts design §3). Pure rules over metadata; never throws.
```

nahradit:

```csharp
            return null;
        }
    }

    /// The camera's serial number from the same sidecar (`<Device … serialNo="…"/>`), or null.
    public static string? Serial(string clipFullPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(clipFullPath);
            if (dir == null) return null;
            var xml = Path.Combine(dir, Path.GetFileNameWithoutExtension(clipFullPath) + "M01.XML");
            if (!System.IO.File.Exists(xml)) return null;
            var device = System.Xml.Linq.XDocument.Load(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "Device");
            return device?.Attribute("serialNo")?.Value.Trim() is { Length: > 0 } serial ? serial : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// Which LUT a clip gets (spec: luts design §3). Pure rules over metadata; never throws.
```

`windows/src/Offload.Core/Luts.cs`, řádky 138–142 — najít:

```csharp
    public static LutMode EffectiveMode(ProxySettings s) => s.LutMode ?? (!string.IsNullOrEmpty(s.LutPath) ? LutMode.Custom : LutMode.Auto);

    public static LutUse Resolve(Clip clip, ClipInfo? info, string clipFullPath, ProxySettings s, LutCatalog cat)
    {
        switch (EffectiveMode(s))
```

nahradit:

```csharp
    public static LutMode EffectiveMode(ProxySettings s) => s.LutMode ?? (!string.IsNullOrEmpty(s.LutPath) ? LutMode.Custom : LutMode.Auto);

    /// `cardName`: the card folder — own rules match folders below it and fall back to it as the reel (spec 4.2).
    public static LutUse Resolve(Clip clip, ClipInfo? info, string clipFullPath, ProxySettings s, LutCatalog cat, string cardName = "")
    {
        switch (EffectiveMode(s))
```

`windows/src/Offload.Core/Luts.cs`, řádky 151–154 — najít:

```csharp
                return Builtin(cat, s.LutBuiltin, "builtin", "vybraný v nastavení") ?? LutUse.None("vestavěný LUT nenalezen");
        }
        if (cat.Luts.Count == 0) return LutUse.None("vestavěné LUTy chybí");
        var (id, reason) = Detect(clip, info, clipFullPath);
```

nahradit:

```csharp
                return Builtin(cat, s.LutBuiltin, "builtin", "vybraný v nastavení") ?? LutUse.None("vestavěný LUT nenalezen");
        }
        // R18: own rules first, only here in Auto mode.
        if (LutRules.Match(s.LutRules, clip, info, info?.Reel ?? ReelResolver.Resolve(clip, info, cardName), cardName) is { } rule)
            return LutRules.Use(rule.Number, rule.Rule, cat);
        if (cat.Luts.Count == 0) return LutUse.None("vestavěné LUTy chybí");
        var (id, reason) = Detect(clip, info, clipFullPath);
```

`windows/src/Offload.Core/Media.cs`, řádky 69–72 — najít:

```csharp
    /// `clips[].info.reel`, absent when unknown.
    [JsonPropertyName("reel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Reel { get; set; }

    [JsonIgnore]
```

nahradit:

```csharp
    /// `clips[].info.reel`, absent when unknown.
    [JsonPropertyName("reel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Reel { get; set; }
    /// The camera body's serial number (spec 4.2, LUT rules "serial"): Sony XML `Device serialNo`, ffprobe
    /// `com.apple.quicktime.camera.identifier` / `camera_serial_number`. Report key `clips[].info.cameraSerial`, absent
    /// when unknown — then a "serial" rule never matches.
    [JsonPropertyName("cameraSerial"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CameraSerial { get; set; }

    [JsonIgnore]
```

`windows/src/Offload.Core/Media.cs`, řádky 199–202 — najít:

```csharp
    /// How much of the image the frame covers (0.9 = 90 %, i.e. 10 % overscan around it).
    [JsonPropertyName("frameScale")] public double FrameScale { get; set; } = 1.0;
    /// Keys a newer version added inside "proxy" (settings.json, report): kept and written back unchanged (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
```

nahradit:

```csharp
    /// How much of the image the frame covers (0.9 = 90 %, i.e. 10 % overscan around it).
    [JsonPropertyName("frameScale")] public double FrameScale { get; set; } = 1.0;
    /// Own LUT rules (spec 4.2), top one first — used only in Auto mode, before the camera is recognised.
    [JsonIgnore] public List<LutRule> LutRules { get; set; } = [];
    /// `lutRules` as settings.json and the report hold it: absent while there is none (older files stay as they were).
    [JsonPropertyName("lutRules"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LutRule>? LutRulesJson
    {
        get => LutRules.Count > 0 ? LutRules : null;
        set => LutRules = value ?? [];
    }
    /// Keys a newer version added inside "proxy" (settings.json, report): kept and written back unchanged (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
```

`windows/src/Offload.Core/Messages.cs`, řádky 153–156 — najít:

```csharp
        $"Neznámý kodek proxy „{x}“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ.";
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string UnknownHash(string? x) => $"Neznámý kontrolní součet „{x}“ — použijte xxh64 nebo both (xxh64 + MD5).";

```

nahradit:

```csharp
        $"Neznámý kodek proxy „{x}“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ.";
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string RuleLutNotFound(int rule, string name) => $"LUT z pravidla {rule} nenalezen: {name}";
    public static string UnknownHash(string? x) => $"Neznámý kontrolní součet „{x}“ — použijte xxh64 nebo both (xxh64 + MD5).";

```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 528–532 — najít:

```csharp
                entries[i].Info = info;
                // The Sony XML is read from the destination: it sits next to the clip on the card, so it was copied too.
                entries[i].Lut = LutResolver.Resolve(entries[i].Clip, info, Layout.Join(cardPaths[d], entries[i].Clip.Path), Config.Proxy, LutCatalog.Default);
                // RED and ARRI raw cannot give a single frame directly; their thumbnails come from the proxy later.
                if (entries[i].Clip.Format is not (ClipFormat.R3d or ClipFormat.Arriraw))
```

nahradit:

```csharp
                entries[i].Info = info;
                // The Sony XML is read from the destination: it sits next to the clip on the card, so it was copied too.
                entries[i].Lut = LutResolver.Resolve(entries[i].Clip, info, Layout.Join(cardPaths[d], entries[i].Clip.Path), Config.Proxy, LutCatalog.Default, plan.CardName);
                // RED and ARRI raw cannot give a single frame directly; their thumbnails come from the proxy later.
                if (entries[i].Clip.Format is not (ClipFormat.R3d or ClipFormat.Arriraw))
```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 1–4 — najít:

```csharp
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

```

nahradit:

```csharp
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 27–34 — najít:

```csharp
    }
    readonly string workDir;
    readonly bool hasCustomLut;
    readonly bool hasFont;
    /// Built-in LUT ids already copied into the work folder.
    readonly HashSet<string> builtinLuts = new(StringComparer.Ordinal);

    public ProxySettings Settings { get; }
```

nahradit:

```csharp
    }
    readonly string workDir;
    readonly bool hasFont;
    /// The LUT files already copied into the work folder (by their name there).
    readonly HashSet<string> copiedLuts = new(StringComparer.Ordinal);

    public ProxySettings Settings { get; }
```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 72–81 — najít:

```csharp
        try
        {
            // The custom LUT is copied up front as before; built-in ones lazily, per clip (PrepareLut).
            if (LutResolver.EffectiveMode(settings) == LutMode.Custom && !string.IsNullOrEmpty(settings.LutPath) && File.Exists(settings.LutPath))
            {
                File.Copy(settings.LutPath, Path.Combine(workDir, FilterGraph.LutFile));
                hasCustomLut = true;
            }
            if (FontCandidates.FirstOrDefault(File.Exists) is string font)
            {
```

nahradit:

```csharp
        try
        {
            // LUTs are copied lazily, per clip and once each (PrepareLut): built-in ones, the custom one, own rules' ones.
            if (FontCandidates.FirstOrDefault(File.Exists) is string font)
            {
```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 118–122 — najít:

```csharp
        {
            var ffprobe = Tools.Ffprobe ?? throw new ToolError("ffprobe", -1, Msg.FfprobeNotFound);
            return FFProbe.Probe(ffprobe, path);
        }
        if (!backends.TryGetValue(clip.Format, out var b) || !b.IsAvailable(Tools))
```

nahradit:

```csharp
        {
            var ffprobe = Tools.Ffprobe ?? throw new ToolError("ffprobe", -1, Msg.FfprobeNotFound);
            var info = FFProbe.Probe(ffprobe, path);
            info.CameraSerial ??= SonyXml.Serial(path);   // Sony keeps it in the XML next to the clip (spec 4.2)
            return info;
        }
        if (!backends.TryGetValue(clip.Format, out var b) || !b.IsAvailable(Tools))
```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 191–206 — najít:

```csharp
    }

    /// The .cube for this clip by its name in the work folder, or null for no LUT. The custom LUT was copied
    /// in the constructor (null when it was missing); a built-in one is copied on first use, once per id.
    string? PrepareLut(LutUse? lut)
    {
        if (lut?.File is not string file) return null;
        if (!lut.IsBuiltin) return hasCustomLut ? FilterGraph.LutFile : null;
        var name = $"lut-{lut.Id}.cube";
        if (!builtinLuts.Contains(lut.Id!))
        {
            File.Copy(file, Path.Combine(workDir, name), overwrite: true);
            builtinLuts.Add(lut.Id!);
        }
        return name;
    }
```

nahradit:

```csharp
    }

    /// The .cube for this clip by its name in the work folder, or null for no LUT. Copied on first use, once per file:
    /// a built-in one as `lut-<id>.cube`, an own one (the custom LUT, a rule's) as `lut-custom-<8 hex of SHA-1 of its
    /// path>.cube` — two rules with two .cube files in one job each get their own (spec 4.2). A file that cannot be
    /// read (gone since the check before the start) means no LUT, logged.
    internal string? PrepareLut(LutUse? lut)
    {
        if (lut?.File is not string file) return null;
        var name = lut.IsBuiltin
            ? $"lut-{lut.Id}.cube"
            : $"lut-custom-{Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant())))[..8]}.cube";
        if (copiedLuts.Contains(name)) return name;
        try
        {
            File.Copy(file, Path.Combine(workDir, name), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log?.Invoke($"LUT {file}: {e.Message} — no LUT");
            return null;
        }
        copiedLuts.Add(name);
        return name;
    }
```


- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~LutRulesTests|FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~LutResolverTests|FullyQualifiedName~ProxyRendererTests|FullyQualifiedName~ClipDetectorTests|FullyQualifiedName~MediaTests"`
Expected: PASS (`Job_RulesDecide_ReportSaysRule` potřebuje ffmpeg — bez něj se přeskočí). Pak celá sada (`-warnaserror`, vše zelené, 1073 testů: 1066 úspěšných + 7 přeskočených).

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 22–23 — najít:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 5 — vlastní pravidla LUTů (`proxy.lutRules`, R18): `camera` (model obsahuje), `serial` (sériové číslo se rovná), `reel` (cívka začíná), `folder` (vzor s `*` a `?` nad `<karta>/<složka klipu>`), shora dolů, jen v režimu Automaticky, před rozpoznáním kamery; v reportu `lut.source = "rule"` a důvod „pravidlo 2: kamera obsahuje „FX6““ (pravidlo s Bez LUT = `none`); neplatné pravidlo se při načtení zahodí, bez pravidel klíč chybí; `clips[].info.cameraSerial` ze Sony `M01.XML` (`Device serialNo`) a ffprobe tagů (ARRI/BRAW ověří vzorky); renderer kopíruje každý vlastní `.cube` podle cesty (`lut-custom-<8 hex>.cube`, dvě pravidla = dva soubory); chybějící `.cube` z pravidla blokuje start („LUT z pravidla 2 nenalezen: show.cube“, kontrola mimo UI vlákno)
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Windows: own LUT rules (camera, serial, reel, folder) before the camera detection

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/LutRules.cs windows/src/Offload.Core/Media.cs windows/src/Offload.Core/Luts.cs windows/src/Offload.Core/ClipDetector.cs windows/src/Offload.Core/ProxyRenderer.cs windows/src/Offload.Core/OffloadJob.cs windows/src/Offload.Core/Desktop/AppSettings.cs windows/src/Offload.Core/Messages.cs windows/src/Offload.App/MainViewModel.Setup.cs windows/src/Offload.App/MainViewModel.Queue.cs windows/tests/Offload.Core.Tests/LutRulesTests.cs windows/tests/Offload.Core.Tests/SettingsStoreTests.cs
git commit -F .git/COMMIT_MSG_PLAN8
```

---

### Task 6: Editor pravidel LUTů + CLI `--lut-rule`

Spec §4.2 UX (tlačítko „Pravidla LUTů… (2)“ jen u „Automaticky podle kamery“, okno „Vlastní pravidla LUTů“ s řádky `[Podle ▾] [hodnota] → [LUT ▾] [↑] [↓] [✕]`, „Přidat pravidlo“, nápověda, *OK* / *Zrušit*) a CLI (`--lut-rule <match>=<hodnota>:<lut|cesta.cube|none>`, opakovatelné, pořadí = priorita). Testuje se parsování CLI, kontrola editoru (`LutRules.Problem`) a CLI end-to-end; okno ověří build a ruční test 34.

**Files:**
- Modify: `windows/src/Offload.Core/LutRules.cs:76-79`
- Modify: `windows/src/Offload.Core/Luts.cs:256-260` (`LutOption.PlanLine`)
- Modify: `windows/src/Offload.Core/Messages.cs:154-157`
- Modify: `windows/src/Offload.Cli/StageTwoOptions.cs:13-16, 26-30, 55-58`
- Create: `windows/src/Offload.App/LutRulesViewModel.cs`, `windows/src/Offload.App/LutRulesWindow.xaml`, `windows/src/Offload.App/LutRulesWindow.xaml.cs`
- Modify: `windows/src/Offload.App/ProxyOptionsViewModel.cs:25-28, 96-100`
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs:569-572`
- Modify: `windows/src/Offload.App/MainWindow.xaml.cs:29-32`
- Modify: `windows/src/Offload.App/SetupPanel.xaml:264-267`
- Test: `windows/tests/Offload.Core.Tests/LutRuleOptionTests.cs` (nový)
- Modify: `docs/VYVOJ-WINDOWS.md:22-23`

**Interfaces:**
- Consumes: `LutRule`, `LutRules.Valid`, `ProxyPreflight.Blocking` (úkol 5), `CliProject.Given` (úkol 4), `LutCatalog`.
- Produces: `LutRules.Problem(IReadOnlyList<LutRule>) → string?`, `LutRules.ParseOption(string, LutCatalog) → (LutRule? Rule, string? Error)`; `Msg.BadLutRule(string)`; `LutRulesViewModel(IReadOnlyList<LutRule>, LutCatalog)` (`Rows`, `LutChoices`, `AddCommand`, `Problem`, `Result() → List<LutRule>?`), `LutRuleRowViewModel`; `LutRulesWindow.Edit(Window, IReadOnlyList<LutRule>, LutCatalog) → List<LutRule>?`; `ProxyOptionsViewModel.ShowRules`, `RulesButtonText`, `EditRulesCommand`, `internal RulesChanged()`; `MainViewModel.EditLutRulesDialog` (`Func<IReadOnlyList<LutRule>, LutCatalog, List<LutRule>?>?`), `internal EditLutRules(LutCatalog)`.

- [ ] **Step 1: Padající testy**

Vytvořit `windows/tests/Offload.Core.Tests/LutRuleOptionTests.cs`:

```csharp
using Offload.Cli;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// Spec 4.2: CLI `--lut-rule <match>=<value>:<lut|path.cube|none>` and what the rules editor accepts.
[Collection("Console")]
public class LutRuleOptionTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    [Theory]
    [InlineData("camera=FX6:sony-slog3-sgamut3cine", "camera", "FX6", "sony-slog3-sgamut3cine", null)]
    [InlineData("REEL= A0 :none", "reel", "A0", "none", null)]
    [InlineData(@"serial=0123:C:\luts\show.cube", "serial", "0123", null, @"C:\luts\show.cube")]
    [InlineData("folder=A001/*:show.cube", "folder", "A001/*", null, "show.cube")]
    public void Parse_Good(string spec, string match, string value, string? lut, string? path)
    {
        var (rule, error) = LutRules.ParseOption(spec, Cat);
        Assert.Null(error);
        Assert.Equal((match, value, lut, path), (rule!.Match, rule.Value, rule.Lut, rule.LutPath));
    }

    [Theory]
    [InlineData("camera:FX6")]
    [InlineData("=FX6:none")]
    [InlineData("camera=:none")]
    [InlineData("camera=FX6:")]
    [InlineData("lens=35:none")]
    public void Parse_Bad_CzechHelp(string spec)
    {
        var (rule, error) = LutRules.ParseOption(spec, Cat);
        Assert.Null(rule);
        Assert.StartsWith($"Pravidlo LUTu „{spec}“ nejde přečíst — použijte <podle>=<hodnota>:<lut>", error);
    }

    [Fact]
    public void Parse_UnknownBuiltin_ListsTheIds()
    {
        var (rule, error) = LutRules.ParseOption("camera=FX6:sony-xyz", Cat);
        Assert.Null(rule);
        Assert.StartsWith("Neznámý LUT „sony-xyz“. Použijte auto, none, cestu k .cube nebo jedno z: ", error);
    }

    [Fact]
    public void EditorProblem_FirstIncompleteRow()
    {
        Assert.Null(LutRules.Problem([new LutRule { Value = "FX6", Lut = "none" }]));
        Assert.Equal("Pravidlo 2: vyplňte hodnotu.", LutRules.Problem([new LutRule { Value = "FX6", Lut = "none" }, new LutRule { Value = " ", Lut = "none" }]));
        Assert.Equal("Pravidlo 1: vyberte LUT.", LutRules.Problem([new LutRule { Value = "FX6" }]));
    }

    [Fact]
    public void PlanLine_CountsTheRules()
    {
        var p = new ProxySettings { Enabled = true, LutRules = [new LutRule { Value = "FX6", Lut = "none" }] };
        Assert.Equal("LUT: auto + 1 own rule(s)", LutOption.PlanLine(p, analyzeClips: false));
    }

    static (int Exit, string Out, string Err, JobConfig? Config) Run(string? settingsDir, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var oldFactory = CopyCommand.JobFactory;
        var oldPath = CliProject.SettingsPath;
        JobConfig? seen = null;
        Console.SetOut(so);
        Console.SetError(se);
        if (settingsDir != null) CliProject.SettingsPath = () => Path.Combine(settingsDir, "settings.json");
        CopyCommand.JobFactory = c => new OffloadJob(seen = c, AppendFixtures.Options());
        try { return (CopyCommand.Create().Parse(args).Invoke(), so.ToString(), se.ToString(), seen); }
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

    [Fact]
    public void Cli_RulesInOrder_PathMadeFull_MissingCubeRefused()
    {
        using var dir = new TempDir();
        var cube = Path.Combine(dir.Path, "show.cube");
        TestMedia.IdentityLut(cube);
        var r = Run(null, Card(dir), "-d", dir.Sub("d1"), "--dry-run", "--no-history",
            "--lut-rule", "camera=FX6:sony-slog3-sgamut3cine", "--lut-rule", $"reel=A:{cube}");
        Assert.Equal(0, r.Exit);
        Assert.Equal([("camera", "sony-slog3-sgamut3cine", (string?)null), ("reel", null, cube)],
            r.Config!.Proxy.LutRules.Select(x => (x.Match, x.Lut, x.LutPath)));
        Assert.Contains("LUT: auto + 2 own rule(s)", r.Out);

        var bad = Run(null, Card(dir), "-d", dir.Sub("d2"), "--no-history", "--lut-rule", $"reel=A:{Path.Combine(dir.Path, "gone.cube")}");
        Assert.Equal(1, bad.Exit);
        Assert.Contains("Chyba: LUT z pravidla 1 nenalezen: gone.cube", bad.Err);
        var wrong = Run(null, Card(dir), "-d", dir.Sub("d3"), "--no-history", "--lut-rule", "camera:FX6");
        Assert.Equal(1, wrong.Exit);
        Assert.Contains("Chyba: Pravidlo LUTu „camera:FX6“ nejde přečíst", wrong.Err);
    }

    // With --project the project's rules are the default; written --lut-rule options replace them.
    [Fact]
    public void Cli_ProjectRules_ReplacedByWrittenOnes()
    {
        using var dir = new TempDir();
        var folder = dir.Sub("appdata");
        var s = new AppSettings();
        var p = Projects.SaveAs(s, "Film X");
        p.Proxy.LutRules = [new LutRule { Match = "camera", Value = "FX6", Lut = "none" }, new LutRule { Match = "reel", Value = "B", Lut = "none" }];
        SettingsStore.Save(s, Path.Combine(folder, "settings.json"));

        var kept = Run(folder, Card(dir), "--project", "Film X", "-d", dir.Sub("d1"), "--dry-run", "--no-history");
        Assert.Equal(["FX6", "B"], kept.Config!.Proxy.LutRules.Select(x => x.Value));
        var replaced = Run(folder, Card(dir), "--project", "Film X", "-d", dir.Sub("d2"), "--dry-run", "--no-history", "--lut-rule", "serial=9:none");
        Assert.Equal(["9"], replaced.Config!.Proxy.LutRules.Select(x => x.Value));
    }
}
```


- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~LutRuleOptionTests"`
Expected: FAIL — build testů selže (CS0117: `LutRules.ParseOption`, `LutRules.Problem` neexistují).

- [ ] **Step 3: Implementace**

Vytvořit `windows/src/Offload.App/LutRulesViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.IO;
using Offload.Core;

namespace Offload.App;

/// "Vlastní pravidla LUTů" (spec 4.2): a copy of the rules, edited row by row; OK hands back the new list.
public sealed class LutRulesViewModel : ObservableObject
{
    string? problem;

    public LutRulesViewModel(IReadOnlyList<LutRule> rules, LutCatalog catalog)
    {
        LutChoices = [.. catalog.Luts.Select(l => new Option<string>(l.Id, l.Name)), new("none", "Bez LUT"), new(Custom, "Vlastní…")];
        foreach (var r in rules) Rows.Add(new LutRuleRowViewModel(this, r));
        AddCommand = new RelayCommand(() =>
        {
            Rows.Add(new LutRuleRowViewModel(this, new LutRule { Match = LutRule.Camera, Lut = "none" }));
            Problem = null;
        });
    }

    public const string Custom = "custom";

    public ObservableCollection<LutRuleRowViewModel> Rows { get; } = [];
    /// Built-in LUTs, "Bez LUT", "Vlastní…" — the same instances for every row (ComboBox compares by reference).
    public IReadOnlyList<Option<string>> LutChoices { get; }
    public RelayCommand AddCommand { get; }

    /// Why OK cannot take the rules (red), or null.
    public string? Problem
    {
        get => problem;
        set => Set(ref problem, value);
    }

    /// The rules in the rows' order, or null (and Problem says why) when a row is not complete.
    public List<LutRule>? Result()
    {
        var rules = Rows.Select(r => r.ToRule()).ToList();
        Problem = LutRules.Problem(rules);
        return Problem == null ? LutRules.Valid(rules) : null;
    }

    internal void Move(LutRuleRowViewModel row, int by)
    {
        int i = Rows.IndexOf(row), j = i + by;
        if (i < 0 || j < 0 || j >= Rows.Count) return;
        Rows.Move(i, j);
    }

    internal void Remove(LutRuleRowViewModel row)
    {
        Rows.Remove(row);
        Problem = null;
    }
}

/// One rule: [Podle ▾] [hodnota] → [LUT ▾] [↑] [↓] [✕].
public sealed class LutRuleRowViewModel : ObservableObject
{
    readonly LutRulesViewModel owner;
    readonly Dictionary<string, System.Text.Json.JsonElement>? extra;
    Option<string> kind;
    string value;
    string? lut;
    string? lutPath;

    public static IReadOnlyList<Option<string>> Kinds { get; } =
    [
        new(LutRule.Camera, "Kamera (výrobce/model)"),
        new(LutRule.Serial, "Sériové číslo kamery"),
        new(LutRule.Reel, "Cívka (začátek)"),
        new(LutRule.Folder, "Složka (vzor s * a ?)"),
    ];

    public LutRuleRowViewModel(LutRulesViewModel owner, LutRule rule)
    {
        this.owner = owner;
        kind = Kinds.FirstOrDefault(k => k.Value == rule.Match) ?? Kinds[0];
        value = rule.Value;
        lut = rule.Lut;
        lutPath = rule.LutPath;
        extra = rule.Extra;
        UpCommand = new RelayCommand(() => owner.Move(this, -1));
        DownCommand = new RelayCommand(() => owner.Move(this, +1));
        RemoveCommand = new RelayCommand(() => owner.Remove(this));
    }

    public IReadOnlyList<Option<string>> LutChoices => owner.LutChoices;
    public RelayCommand UpCommand { get; }
    public RelayCommand DownCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public Option<string> Kind
    {
        get => kind;
        set { if (value != null) Set(ref kind, value); }
    }

    public string Value
    {
        get => value;
        set => Set(ref this.value, value ?? "");
    }

    /// The chosen LUT; "Vlastní…" opens the .cube dialog (deferred: not inside the ComboBox's own change), a cancel
    /// keeps the earlier choice.
    public Option<string> Lut
    {
        get => LutChoices.FirstOrDefault(c => c.Value == (lutPath != null ? LutRulesViewModel.Custom : lut ?? "none"))
               ?? LutChoices.First(c => c.Value == "none");
        set
        {
            if (value == null) return;
            if (value.Value == LutRulesViewModel.Custom)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (Dialogs.PickFile("Vyber LUT (.cube)", "LUT (*.cube)|*.cube") is string path)
                    {
                        lutPath = path;
                        lut = null;
                    }
                    RaiseMany(nameof(Lut), nameof(CustomName), nameof(CustomPath));
                });
                return;
            }
            lut = value.Value;
            lutPath = null;
            RaiseMany(nameof(Lut), nameof(CustomName), nameof(CustomPath));
        }
    }

    /// The own .cube's file name next to the choice (the full path in its tooltip), or null.
    public string? CustomName => lutPath != null ? Path.GetFileName(lutPath) : null;
    public string? CustomPath => lutPath;

    public LutRule ToRule() => new() { Match = kind.Value, Value = value.Trim(), Lut = lutPath == null ? lut : null, LutPath = lutPath, Extra = extra };
}
```

Vytvořit `windows/src/Offload.App/LutRulesWindow.xaml`:

```xml
<Window x:Class="Offload.App.LutRulesWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Offload.App"
        Title="Vlastní pravidla LUTů" Width="820" SizeToContent="Height" MaxHeight="760" ResizeMode="CanResizeWithGrip"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Icon="pack://application:,,,/Offload.ico">
    <Window.Resources>
        <Style x:Key="RowButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Padding" Value="8,3" />
            <Setter Property="MinWidth" Value="0" />
            <Setter Property="Margin" Value="4,0,0,0" />
        </Style>
    </Window.Resources>
    <DockPanel Margin="22,18">
        <StackPanel DockPanel.Dock="Top">
            <TextBlock Text="Vlastní pravidla LUTů" FontSize="17" FontWeight="SemiBold" />
            <TextBlock Text="Pravidla se zkoušejí shora dolů, před automatickým rozpoznáním kamery." Style="{StaticResource Caption}"
                       Margin="0,4,0,10" TextWrapping="Wrap" />
        </StackPanel>
        <StackPanel DockPanel.Dock="Bottom">
            <Button Content="Přidat pravidlo" Command="{Binding AddCommand}" HorizontalAlignment="Left" Margin="0,8,0,0" Padding="12,5"
                    AutomationProperties.AutomationId="AddLutRule" />
            <TextBlock Text="{Binding Problem}" Style="{StaticResource ErrorText}" Margin="0,8,0,0" TextWrapping="Wrap"
                       Visibility="{Binding Problem, Converter={StaticResource NullToCollapsed}}" AutomationProperties.AutomationId="LutRulesProblem" />
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
                <Button Content="OK" Click="Ok_Click" IsDefault="True" MinWidth="80" Padding="12,5"
                        Style="{DynamicResource AccentButtonStyle}" AutomationProperties.AutomationId="LutRulesOk" />
                <Button Content="Zrušit" IsCancel="True" MinWidth="80" Padding="12,5" Margin="8,0,0,0" AutomationProperties.AutomationId="LutRulesCancel" />
            </StackPanel>
        </StackPanel>
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding Rows}" AutomationProperties.AutomationId="LutRuleRows">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Grid Margin="0,0,0,6">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="200" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="230" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <ComboBox ItemsSource="{Binding Source={x:Static local:LutRuleRowViewModel.Kinds}}" SelectedItem="{Binding Kind}"
                                      DisplayMemberPath="Label" AutomationProperties.Name="Podle" />
                            <TextBox Grid.Column="1" Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}" Margin="6,0,0,0"
                                     AutomationProperties.Name="Hodnota" />
                            <TextBlock Grid.Column="2" Text="→" VerticalAlignment="Center" Margin="8,0" />
                            <StackPanel Grid.Column="3">
                                <ComboBox ItemsSource="{Binding LutChoices}" SelectedItem="{Binding Lut}" DisplayMemberPath="Label"
                                          AutomationProperties.Name="LUT" />
                                <TextBlock Text="{Binding CustomName}" ToolTip="{Binding CustomPath}" Style="{StaticResource Caption}"
                                           TextTrimming="CharacterEllipsis" Visibility="{Binding CustomName, Converter={StaticResource NullToCollapsed}}" />
                            </StackPanel>
                            <StackPanel Grid.Column="4" Orientation="Horizontal" VerticalAlignment="Top">
                                <Button Style="{StaticResource RowButton}" Content="↑" Command="{Binding UpCommand}" ToolTip="Výš" />
                                <Button Style="{StaticResource RowButton}" Content="↓" Command="{Binding DownCommand}" ToolTip="Níž" />
                                <Button Style="{StaticResource RowButton}" Content="✕" Command="{Binding RemoveCommand}" ToolTip="Odebrat pravidlo" />
                            </StackPanel>
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</Window>
```

Vytvořit `windows/src/Offload.App/LutRulesWindow.xaml.cs`:

```csharp
using System.Windows;
using Offload.Core;

namespace Offload.App;

/// "Vlastní pravidla LUTů" (spec 4.2). OK takes the rules only when every row is complete (Problem says what is missing).
public partial class LutRulesWindow : Window
{
    readonly LutRulesViewModel vm;
    List<LutRule>? result;

    LutRulesWindow(LutRulesViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        DataContext = vm;
    }

    /// The edited rules, or null when the user cancelled.
    public static List<LutRule>? Edit(Window owner, IReadOnlyList<LutRule> rules, LutCatalog catalog)
    {
        var w = new LutRulesWindow(new LutRulesViewModel(rules, catalog)) { Owner = owner };
        w.ShowDialog();
        return w.result;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (vm.Result() is not { } rules) return;   // the red line says which row
        result = rules;
        DialogResult = true;
    }
}
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 569–572 — najít:

```csharp
    }

    internal void ProxyChanged()
    {
```

nahradit:

```csharp
    }

    /// Opens "Vlastní pravidla LUTů" (set by the window): the rules to edit, the built-in LUTs; the new rules or null.
    public Func<IReadOnlyList<LutRule>, LutCatalog, List<LutRule>?>? EditLutRulesDialog { get; set; }

    /// "Pravidla LUTů…": OK writes the rules into the active profile; their .cube files are checked again (off the UI thread).
    internal void EditLutRules(LutCatalog catalog)
    {
        if (EditLutRulesDialog?.Invoke(Settings.Proxy.LutRules, catalog) is not { } rules) return;
        Settings.Proxy.LutRules = rules;
        Proxy.RulesChanged();   // saves (ProxyChanged) and rechecks the LUT files
    }

    internal void ProxyChanged()
    {
```

`windows/src/Offload.App/MainWindow.xaml.cs`, řádky 29–32 — najít:

```csharp
        vm.AskYesNo = text => MessageBox.Show(this, text, "Offload", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        vm.AskProjectName = (title, initial, problem) => ProjectNameWindow.Ask(this, title, initial, problem);
        // Spec 8a.2: the USB check is on demand only (menu Nástroje, Ctrl+U, the Hardware section) — no start-up question.
        vm.HardwareRequested += hw =>
```

nahradit:

```csharp
        vm.AskYesNo = text => MessageBox.Show(this, text, "Offload", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        vm.AskProjectName = (title, initial, problem) => ProjectNameWindow.Ask(this, title, initial, problem);
        vm.EditLutRulesDialog = (rules, catalog) => LutRulesWindow.Edit(this, rules, catalog);
        // Spec 8a.2: the USB check is on demand only (menu Nástroje, Ctrl+U, the Hardware section) — no start-up question.
        vm.HardwareRequested += hw =>
```

`windows/src/Offload.App/ProxyOptionsViewModel.cs`, řádky 25–28 — najít:

```csharp
        ScaleDownCommand = new RelayCommand(() => ScalePercent--, () => ScalePercent > 50);
        ScaleUpCommand = new RelayCommand(() => ScalePercent++, () => ScalePercent < 100);
        BuildLutLists();
    }
```

nahradit:

```csharp
        ScaleDownCommand = new RelayCommand(() => ScalePercent--, () => ScalePercent > 50);
        ScaleUpCommand = new RelayCommand(() => ScalePercent++, () => ScalePercent < 100);
        EditRulesCommand = new RelayCommand(() => owner.EditLutRules(catalog));
        BuildLutLists();
    }
```

`windows/src/Offload.App/ProxyOptionsViewModel.cs`, řádky 96–100 — najít:

```csharp
    }

    static readonly string[] LutProperties = [nameof(LutChoice), nameof(ShowFallback), nameof(ShowCustom), nameof(LutName), nameof(LutPath)];

    public Option<string> Fallback
```

nahradit:

```csharp
    }

    static readonly string[] LutProperties = [nameof(LutChoice), nameof(ShowFallback), nameof(ShowCustom), nameof(LutName), nameof(LutPath), nameof(ShowRules)];

    /// "Pravidla LUTů… (2)" (spec 4.2) — only with "Automaticky podle kamery": the rules apply in that mode only (R18).
    public bool ShowRules => Mode == LutMode.Auto;
    public string RulesButtonText => $"Pravidla LUTů… ({p.LutRules.Count})";
    public RelayCommand EditRulesCommand { get; }

    /// The rules were edited (MainViewModel.EditLutRules wrote them into the profile).
    internal void RulesChanged() => Changed(nameof(RulesButtonText));

    public Option<string> Fallback
```

`windows/src/Offload.App/SetupPanel.xaml`, řádky 264–267 — najít:

```xml
                                <TextBlock Text="{Binding LutName}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" ToolTip="{Binding LutPath}" />
                            </DockPanel>
                        </StackPanel>
                    </Grid>
```

nahradit:

```xml
                                <TextBlock Text="{Binding LutName}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" ToolTip="{Binding LutPath}" />
                            </DockPanel>
                            <Button Style="{StaticResource SmallButton}" Content="{Binding RulesButtonText}" Command="{Binding EditRulesCommand}"
                                    HorizontalAlignment="Left" Margin="0,4,0,0" ToolTip="Vlastní pořadí: kamera / sériové číslo / cívka / složka → LUT"
                                    Visibility="{Binding ShowRules, Converter={StaticResource BoolToVisible}}"
                                    AutomationProperties.AutomationId="LutRulesButton" />
                        </StackPanel>
                    </Grid>
```

`windows/src/Offload.Cli/StageTwoOptions.cs`, řádky 13–16 — najít:

```csharp
    readonly Option<string?> lut = new("--lut") { Description = "LUT for proxies and thumbnails: auto (by camera, default), none, a built-in id (offload luts prints them) or a .cube path." };
    readonly Option<string?> lutFallback = new("--lut-fallback") { Description = "Built-in LUT id (or none) for clips whose camera is not recognised with --lut auto." };
    readonly Option<int> proxyHeight = new("--proxy-height") { Description = "Proxy height in pixels.", DefaultValueFactory = _ => 1080 };
    readonly Option<double?> frameAspect = new("--frame-aspect") { Description = "Crop proxies to the frame lines: aspect ratio, e.g. 1.66.", CustomParser = a => CliParse.Parse(a) };
```

nahradit:

```csharp
    readonly Option<string?> lut = new("--lut") { Description = "LUT for proxies and thumbnails: auto (by camera, default), none, a built-in id (offload luts prints them) or a .cube path." };
    readonly Option<string?> lutFallback = new("--lut-fallback") { Description = "Built-in LUT id (or none) for clips whose camera is not recognised with --lut auto." };
    readonly Option<string[]> lutRule = new("--lut-rule") { Description = "Own LUT rule before the camera detection (with --lut auto), repeat in priority order: <camera|serial|reel|folder>=<value>:<lut id|none|path.cube>, e.g. camera=FX6:sony-slog3-sgamut3cine.", DefaultValueFactory = _ => [] };
    readonly Option<int> proxyHeight = new("--proxy-height") { Description = "Proxy height in pixels.", DefaultValueFactory = _ => 1080 };
    readonly Option<double?> frameAspect = new("--frame-aspect") { Description = "Crop proxies to the frame lines: aspect ratio, e.g. 1.66.", CustomParser = a => CliParse.Parse(a) };
```

`windows/src/Offload.Cli/StageTwoOptions.cs`, řádky 26–30 — najít:

```csharp
    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder, noClipList, ale, noDayReport })
            cmd.Options.Add(o);
    }
```

nahradit:

```csharp
    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, lutRule, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder, noClipList, ale, noDayReport })
            cmd.Options.Add(o);
    }
```

`windows/src/Offload.Cli/StageTwoOptions.cs`, řádky 55–58 — najít:

```csharp
            if (Given(lutFallback)) p.LutFallback = lutFb;
        }
        if (Given(proxy)) p.Enabled = r.GetValue(proxy);
        if (Given(proxyHeight)) p.Height = r.GetValue(proxyHeight);
```

nahradit:

```csharp
            if (Given(lutFallback)) p.LutFallback = lutFb;
        }
        if (Given(lutRule))
        {
            var rules = new List<LutRule>();
            foreach (var spec in r.GetValue(lutRule)!)
            {
                var (rule, error) = LutRules.ParseOption(spec, LutCatalog.Default);
                if (rule == null) throw new ArgumentException(error);
                if (rule.LutPath != null) rule.LutPath = CliPath.Full(rule.LutPath);
                rules.Add(rule);
            }
            p.LutRules = rules;   // written rules replace the project's (their order is the priority)
        }
        if (Given(proxy)) p.Enabled = r.GetValue(proxy);
        if (Given(proxyHeight)) p.Height = r.GetValue(proxyHeight);
```

`windows/src/Offload.Core/LutRules.cs`, řádky 76–79 — najít:

```csharp
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// How the report gives the reason: "pravidlo 2: kamera obsahuje „FX6“".
    public static string Describe(int number, LutRule r) => $"pravidlo {number}: " + r.Match switch
```

nahradit:

```csharp
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// The first rule the editor cannot save, in Czech ("Pravidlo 2: vyplňte hodnotu."), or null.
    public static string? Problem(IReadOnlyList<LutRule> rules)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (string.IsNullOrWhiteSpace(r.Value)) return $"Pravidlo {i + 1}: vyplňte hodnotu.";
            if (string.IsNullOrWhiteSpace(r.Lut) == string.IsNullOrWhiteSpace(r.LutPath)) return $"Pravidlo {i + 1}: vyberte LUT.";
        }
        return null;
    }

    /// CLI `--lut-rule <match>=<value>:<lut>` (spec 4.2): `lut` is a built-in id, "none" or a path to a .cube (anything
    /// with ".cube" or a path separator — returned as written). The value ends at the first ":" after "=".
    public static (LutRule? Rule, string? Error) ParseOption(string spec, LutCatalog cat)
    {
        int eq = spec.IndexOf('=');
        int colon = eq < 0 ? -1 : spec.IndexOf(':', eq + 1);
        if (eq <= 0 || colon < 0) return (null, Msg.BadLutRule(spec));
        var match = spec[..eq].Trim().ToLowerInvariant();
        var value = spec[(eq + 1)..colon].Trim();
        var lut = spec[(colon + 1)..].Trim();
        if (!LutRule.Kinds.Contains(match) || value.Length == 0 || lut.Length == 0) return (null, Msg.BadLutRule(spec));
        if (lut.Equals("none", StringComparison.OrdinalIgnoreCase)) return (new LutRule { Match = match, Value = value, Lut = "none" }, null);
        if (lut.EndsWith(".cube", StringComparison.OrdinalIgnoreCase) || lut.Contains('\\') || lut.Contains('/'))
            return (new LutRule { Match = match, Value = value, LutPath = lut }, null);
        return cat.Find(lut) != null
            ? (new LutRule { Match = match, Value = value, Lut = lut }, null)
            : (null, Msg.UnknownLut(lut, string.Join(", ", cat.Luts.Select(l => l.Id))));
    }

    /// How the report gives the reason: "pravidlo 2: kamera obsahuje „FX6“".
    public static string Describe(int number, LutRule r) => $"pravidlo {number}: " + r.Match switch
```

`windows/src/Offload.Core/Luts.cs`, řádky 256–260 — najít:

```csharp
        LutMode.Custom => p.LutPath,
        LutMode.Builtin => p.LutBuiltin,
        _ => "auto" + (p.LutFallback != null ? $" (fallback {p.LutFallback})" : ""),
    };
}
```

nahradit:

```csharp
        LutMode.Custom => p.LutPath,
        LutMode.Builtin => p.LutBuiltin,
        _ => "auto" + (p.LutFallback != null ? $" (fallback {p.LutFallback})" : "") + (p.LutRules.Count > 0 ? $" + {p.LutRules.Count} own rule(s)" : ""),
    };
}
```

`windows/src/Offload.Core/Messages.cs`, řádky 154–157 — najít:

```csharp
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string RuleLutNotFound(int rule, string name) => $"LUT z pravidla {rule} nenalezen: {name}";
    public static string UnknownHash(string? x) => $"Neznámý kontrolní součet „{x}“ — použijte xxh64 nebo both (xxh64 + MD5).";

```

nahradit:

```csharp
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string RuleLutNotFound(int rule, string name) => $"LUT z pravidla {rule} nenalezen: {name}";
    public static string BadLutRule(string spec) =>
        $"Pravidlo LUTu „{spec}“ nejde přečíst — použijte <podle>=<hodnota>:<lut>, např. camera=FX6:sony-slog3-sgamut3cine (podle: camera, serial, reel, folder; lut: vestavěný, none nebo cesta k .cube).";
    public static string UnknownHash(string? x) => $"Neznámý kontrolní součet „{x}“ — použijte xxh64 nebo both (xxh64 + MD5).";

```


- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~LutRuleOptionTests|FullyQualifiedName~LutOptionTests|FullyQualifiedName~CliProjectTests"`
Expected: PASS. `dotnet build windows\Offload.sln -warnaserror` zkompiluje i `LutRulesWindow.xaml`. Pak celá sada (vše zelené, 1087 testů: 1080 úspěšných + 7 přeskočených).

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 22–23 — najít:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

````

nahradit:

````markdown
## 2026-10-07 — plán 8: nastavení, oznámení, distribuce

- Úkol 6 — editor pravidel LUTů: v sekci Proxy u „Automaticky podle kamery“ tlačítko „Pravidla LUTů… (N)“ → okno „Vlastní pravidla LUTů“ (Podle: kamera / sériové číslo / cívka / složka, hodnota, LUT: vestavěný / Bez LUT / Vlastní… `.cube`, ↑ ↓ ✕, „Přidat pravidlo“, nápověda; OK jen s úplnými řádky — „Pravidlo 2: vyplňte hodnotu.“); pravidla patří k projektu; CLI `--lut-rule <camera|serial|reel|folder>=<hodnota>:<id|none|cesta.cube>` (opakovatelné, pořadí = priorita, s `--project` nahradí jeho pravidla; chybná podoba nebo chybějící `.cube` = česká chyba, kód 1); plán CLI „LUT: auto + N own rule(s)“
````

Zpráva do `.git/COMMIT_MSG_PLAN8`:

```text
Windows: LUT rules editor and CLI --lut-rule

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/LutRules.cs windows/src/Offload.Core/Luts.cs windows/src/Offload.Core/Messages.cs windows/src/Offload.Cli/StageTwoOptions.cs windows/src/Offload.App/LutRulesViewModel.cs windows/src/Offload.App/LutRulesWindow.xaml windows/src/Offload.App/LutRulesWindow.xaml.cs windows/src/Offload.App/ProxyOptionsViewModel.cs windows/src/Offload.App/MainViewModel.Setup.cs windows/src/Offload.App/MainWindow.xaml.cs windows/src/Offload.App/SetupPanel.xaml windows/tests/Offload.Core.Tests/LutRuleOptionTests.cs
git commit -F .git/COMMIT_MSG_PLAN8
```

Pokračování (úkoly 7–12): `windows/docs/superpowers/plans/2026-10-07-offload-windows-8-nastaveni-2.md`.
