# Offload pro Windows — plán 9: nový vzhled podle docs/ui-navrh (úkoly 1–9) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Přestavět GUI Windows verze podle schváleného návrhu `docs/ui-navrh/` (dvousloupcové rozvržení, tmavé i světlé téma podle Windows) a doplnit do jádra, co nový vzhled potřebuje — průběh po cílech, ukazatel fází, souhrn po polích, seznam nesouhlasných souborů s **Zkopírovat znovu** (nová úloha oprava), odhad času, štítek kamery a připojení, dnešní historii a **Pozastavit** — jako verze 0.5.0.

**Architecture:** Nové chování je v `Offload.Core` s testy: `PauseGate` (pauza mezi bloky kopie a zpětného čtení a mezi klipy 2. fáze), `RepairPlanner` + `PlanMode.Repair` (jen chybné soubory, jen na cíle, kde selhaly, jako generace ASC MHL N se zámkem doplnění), a čisté funkce pro to, co okno ukazuje (`JobCard`, `QueueSummary`, `RateMemory`, `CameraHints`, `ConnectionLabel`, `DestinationTile`, `SetupTexts`, `HistoryView`, `SettingsTexts`, `TopBar`, `ThemeChoice`, `DemoScenario`). `Offload.App` dostane jen vlastnosti view-modelů a nový XAML: tokeny `Themes/Dark.xaml` / `Light.xaml` přepínané `ThemeWatcher`em, styly a ikony, horní lišta, stránka Nastavení, levý sloupec s kroky 1–3 a fronta s kartami podle stavu. Ukázkový režim `--demo` (výchozí vypnutý) naplní okno stavy obrazovek pro vizuální kontrolu.

**Tech Stack:** C# / .NET 10, WPF (Fluent `ThemeMode="System"` pro standardní prvky + vlastní tokeny), xUnit, BCL (`System.Text.Json`, `Microsoft.Win32.Registry`, `SystemEvents`), ffprobe (jen čtení jednoho klipu při prověření karty), Windows PowerShell 5.1 (`windows\scripts\app-drive.ps1` pro snímky).

**Spec:** `docs/ui-navrh/README.md` — zadání (sekce „Zadání pro vývojového Clauda“, „Hlavní principy“, „Design tokeny“, „Rozvržení a komponenty“, „Mapování na současný kód“, „Nové věci, které v kódu zatím nejsou“, „Postup implementace“), přesné barvy, rozměry a texty v `docs/ui-navrh/mockup/*.html`, náhledy `docs/ui-navrh/screeny/*.png`. Úkol 1 doplní README o sekci **„Rozhodnutí (2026-10-07, závazné)“** s rozhodnutími uživatele — od té chvíle je součástí zadání. Plán má dva soubory: tento (úkoly 1–9: návrh v repu a jádro) a `windows/docs/superpowers/plans/2026-10-07-offload-windows-9-ui-2.md` (úkoly 10–19: téma, GUI, ukázkový režim, dokumentace a verze, vizuální kontrola).

## Global Constraints

- Žádný nový NuGet balíček. Testovací projekt dostane jen `<FrameworkReference Include="Microsoft.WindowsDesktop.App.WPF" />` (WPF je součást .NET, ne balíček), aby test načetl slovníky `Themes/*.xaml`; `Offload.App` dál nereferencuje.
- Texty UI česky; kde je text v mockupu, doslova podle `docs/ui-navrh/mockup/*.html` (výjimky jmenuje sekce Rozhodnutí). V UI se píše **„Projekt“**, nikdy „Preset“. Identifikátory anglicky; logy, TXT report a chyby v JSON reportu zůstávají anglicky (HTML je ukáže česky).
- UI vlákno nikdy nečeká na disk ani na kartu: `DriveLinks.Resolve`, `NativeIO.TotalBytes`, ffprobe štítku kamery, dekódování náhledů a vše ostatní nové běží `Task.Run` a výsledek přijde přes dispatcher (jako `DestinationViewModel.Probe`).
- Na kartu se nikdy nic nezapisuje. Prověření karty smí přečíst jeden klip (ffprobe, nejvýš 5 s); oprava kartu jen čte.
- `mac/` se nemění. Co z toho má Mac převzít (po schválení Windows verze), jde do `docs/VYVOJ-MAC.md` „Naplánováno“ a rozdíly do `docs/ROZDILY.md` (úkol 18).
- Logika patří do `Offload.Core` s testy; `Offload.App` dostane vlastnosti view-modelů a XAML, žádnou logiku v code-behind (nové `.xaml.cs` mají jen `InitializeComponent`).
- Pořadí GUI podle „Postup implementace“ zadání: 1 téma (úkol 10) → 2 horní lišta (11) → 3 levý sloupec (13) → 4 fronta se stávajícími daty (14), pak nové věci 1–4 (15) → 5 historie dnes (16) → 6 Nastavení (12, předsunuté — viz Rozhodnutí) → 7 ruční kontrola (19) → 8 dokumentace (18). Jádro (2–9) jde před GUI, které ho používá.
- GUI se při práci na úkolech **nikdy nespouští** (`Offload.exe` ne, ani `--demo`). Spouští ho jen úkol 19, a to až po souhlasu uživatele (řídí ho kontroler).
- `TreatWarningsAsErrors`: každý úkol končí `dotnet build windows\Offload.sln -warnaserror` bez varování a celou zelenou sadou `dotnet test windows\Offload.sln` (start: 1257 testů = 1249 úspěšných + 8 přeskočených; po plánu 1383 = 1375 + 8). `ProxyWatchdogTests` a `ProxyRendererTests.Feeder_*` jsou časově citlivé (ffmpeg pod zátěží celé sady) — když jednou selžou, spusťte je znovu samostatně (`--filter "FullyQualifiedName~ProxyRendererTests"`).
- PDF v testech: `PdfReport.SkipUnlessAsked` je pro celou sadu zapnuté; žádný test plánu 9 PDF netiskne.
- Testy nikdy nezapíšou do skutečného `%APPDATA%` (historie přes `TestEnvironment`, úlohy s `HistoryFolder` v `TempDir`).
- Formáty na disku čitelné Macem: jen **přidané** klíče — report `copyPausedSeconds`, `verifyPausedSeconds`, `kind: "repair"`, `files[].rewritten`, `setAside`; `history.json` `generations[].kind: "repair"`; ASC MHL `action="verified"` u kopie, kterou oprava jen přečetla; složka `_REPORT\neshoda_NNNN\` s odloženými nesouhlasnými kopiemi. Popis do `docs/FORMATY.md` v úkolu 18.
- Téma: tmavé **i** světlé podle Windows (`AppsUseLightTheme`), stejné klíče v obou sadách; písmo Segoe UI Variable Text / Segoe UI a Cascadia Mono / Consolas; IBM Plex se nepřibaluje. `OFFLOAD_THEME=dark|light` je jen pomůcka pro snímky.
- Ukázkový režim `--demo pripraveno|probiha|hotovo|nastaveni` je ve výchozím stavu vypnutý; nic nekopíruje ani nezapisuje.
- Verze: plán 9 → **0.5.0** (`0.5.0-win`) — nastaví úkol 18 v `windows\Directory.Build.props` a v testech s verzí (`FormatTests`, `ReportTests`, `HtmlReportTests`).
- Každý úkol přidá řádek do `docs/VYVOJ-WINDOWS.md` do sekce `## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)` (sekci založí úkol 1 nad sekcí `## 2026-10-07 — plán 8: nastavení, oznámení, distribuce`; nejnovější řádek nahoře, bez SHA).
- Commit: soubory výslovně (`git add <cesty>`, nikdy `-A` ani `.`), zpráva se zapíše nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9` a commituje `git commit -F .git/COMMIT_MSG_PLAN9`; poslední řádek zprávy přesně `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Všechny příkazy git a dotnet se spouštějí z kořene repa `D:\My\Projekt\offload` (větev `windows-ui`, základ `main` 2c7798d).
- Bloky „Najít / Nahradit“ uvádějí text podle stavu souboru **před** daným úkolem (výchozí stav = `windows-ui` na 2c7798d, pak předchozí úkoly plánu); každý blok „Najít“ je v souboru právě jednou. „(celý soubor nahradit)“ = soubor se přepíše celý. Kód plánu byl postavený a otestovaný v kopii repa mimo strom repa: každý úkol se zkompiloval s `-warnaserror` a celá sada byla po plánu zelená. Pracovní kopie má konce řádků CRLF (`core.autocrlf=true`), bloky v plánu LF — nástroj Edit je sjednotí; kdo upravuje skriptem, porovnává bez ohledu na `\r`.

## Review Focus

Pět vstupů, které zadání implikuje, ale žádný úkol by je bez výslovného testu nezachytil; každý má test v úkolu, který vlastní kód:

1. **Pozastaveno, mezitím vytažený disk nebo karta, pak Pokračovat** → soubor selže jen na tom disku, ostatní cíle pokračují, report řekne který, „Zkopírovat znovu“ ho opraví. Test: úkol 2 `EnginePauseTests.DiskGoneWhilePaused_FailsOnThatDestinationOnly`.
2. **Zrušit nebo zavřít aplikaci během pauzy** (i během pauzy ve 2. fázi) → úloha skončí hned jako Zrušeno, nezůstane `.offload-part` ani zámek; zámek dne a historie se během pauzy nedrží. Testy: úkol 2 `PauseGateTests.CancelWhilePaused_ReleasesWithFalse`, `EnginePauseTests.CancelWhilePaused_EndsPromptly_NoPartFiles`; úkol 3 `JobPauseTests.CancelWhilePausedInStage2_EndsCancelledPromptly`, `PausedInStage2_HoldsNoDayLock`.
3. **„Zkopírovat znovu“ s kartou, která se mezitím změnila, vyměnila nebo už soubor nemá** → nic se nezapíše, česká hláška; karta, která se při čtení liší, nový zápis nikdy nepovažuje za dobrý. Testy: úkol 4 `RepairPlannerTests.CardChanged_Refused_NothingWritten`, `FileGoneFromCard_Refused`, `RepairEngineTests.CardDiffersWhenRead_FileStaysFailed_GoodCopyStillVerified`; úkol 5 `RepairJobTests.Offer_Table` (karta není ve čtečce → tlačítko není).
4. **Oprava přerušená uprostřed, nebo oprava, zatímco do téže zálohy doplňuje jiná úloha (druhé okno, CLI)** → dobrá kopie zůstane dobrá a nikdy se nepřepíše ani neodloží, další oprava dokončí práci; souběžná se odmítne bez zápisu. Testy: úkol 4 `RepairEngineTests.CancelledRepair_GoodCopyStaysGood_NextRepairFinishes`; úkol 5 `RepairJobTests.FoldersBusy_Refused`; úkol 3 `JobPauseTests.PausedAppend_KeepsTheFolderLock`.
5. **Světlé téma Windows nebo přepnutí za běhu** → každý text čitelný v obou sadách (≥ 4,5 : 1), všechny slovníky se načtou. Testy: úkol 10 `ThemeTokensTests.Texts_Readable`, `Dictionary_Loads`, `DarkAndLight_SameKeys_ValidColours`; snímek ve světlém tématu v úkolu 19.

## Rozhodnutí tam, kde zadání nestačilo (platí pro oba soubory plánu)

**Pozastavit (nová věc 9, nová funkce jádra).**
- Jedna brána `PauseGate` na úlohu (`OffloadEngine.Pauses`). Kopie čeká **před každým souborem a před každým 16MB blokem** (ještě než si vezme buffer — zapisovače doběhnou s tím, co mají, a stojí; soubory `.offload-part` zůstanou otevřené). Zpětné čtení čeká před každým souborem a blokem (`Hashing.HashFile(…, beforeBlock)`). Flush a zápis reportu se nepřerušují (krátké, rozpracovaný report by byl horší).
- 2. fáze (analýza, náhledy, proxy, PDF přes Edge, denní report) se zastaví **mezi klipy** a před PDF + denním reportem — **žádný proces se neuspává**. Důvody: suspendovaný ffmpeg drží relaci NVENC a soubory, `NtSuspendProcess` není dokumentované API, uspaný GPU proces může spadnout do TDR, a hlídač zaseknutí ffmpeg by pauzu musel odčítat. Takhle hlídač pauzu nikdy nevidí (ffmpeg během pauzy neběží; test s limitem 2 s a pauzou 3 s) a Edge PDF i jeho limit 60 s běží celé. Cena: rozpracovaný klip doběhne (karta říká „Pozastaví se po proxy klipu A003…“).
- Časovače: `JobProgress` během pauzy ukazuje 0 / „—“, žádný odhad ani „Nejpomalejší článek“ (`BrakeMeter.Reset`); po Pokračovat se rychlost měří od obnovení, nikdy přes pauzu. Report si čas pauzy odečte (`copyPausedSeconds`, `verifyPausedSeconds`; měří se od stisku Pozastavit do Pokračovat), „Celkem“ na kartě taky.
- Zámky: zámek složek doplnění/opravy (`AppendLock`) se během pauzy **drží** (složka je rozepsaná; jiné doplnění téže zálohy dostane „…právě doplňuje jiná úloha…“). Zámek dne (`DayReportService`) a historie se během pauzy nikdy nedrží — brána je jen mimo ně.
- Zrušit i zavření aplikace (ruší všechny úlohy) pauzu ukončí hned (`WaitWhilePaused` vrátí false) a úloha skončí jako zrušená, včetně úklidu `.offload-part`.
- Disk nebo karta vytažené během pauzy: nic se nehlídá zvlášť — první zápis/čtení po Pokračovat selže jako vytažení za běhu (soubor selže jen tam, kopie pokračuje), report to řekne a „Zkopírovat znovu“ to opraví.
- Pauza drží PC vzhůru (`KeepAwake` úlohy trvá) — uspání s otevřenými soubory na USB discích by kopii poškodilo.
- Pozastavit je jen u běžící úlohy (čekající ne — blokovala by frontu); čekající úloha dál jde zrušit.

**„Zkopírovat znovu“ (nová věc 4) = nová úloha oprava, ne doplnění.**
- Plánovač `RepairPlanner` vezme z **reportů na cílech** (R4, ne z paměti okna) každý soubor, jehož nejnovější záznam selhal, a naplánuje ho **jen na cíle, kde selhal**. Cíl, který má dobrou kopii, nedostane nic zapsáno — v téže generaci se jeho kopie jen přečte a porovná se **zaznamenaným** hashem (`OffloadPlan.Expected`); nová kopie se porovná s kartou a karta musí dát zaznamenaný xxh64 (jinak `card differs from the backup` a nová kopie je `failed`).
- Proč ne režim doplnění: `AppendPlanner` záměrně **odmítá** právě tento případ (selhaný soubor, který na disku je = `AppendFailedFile`; soubor dobrý jen na části cílů = `AppendDiverged`) a jeho uvolnění by oslabilo ochranu doplnění. Oprava proto **sdílí** jeho kontroly dřívější zálohy (`AppendPlanner.CheckBackup`: složky, reporty, neporušený řetěz ASC MHL, stejný počet generací na všech cílech, reporty v souladu s řetězem) i celý zápis doplnění (zámek `AppendLock`, generace N = max + 1 na **všech** cílech — R7, reporty `_NNNN` vytvářené bez přepsání, generace MHL, řetěz jako bod potvrzení, seznam `.xxh64` naposled), ale má vlastní plánovač a `PlanMode.Repair`.
- Nesouhlasná kopie, která na disku je, se před novou **přesune** do `_REPORT\neshoda_NNNN\<cesta>` (nikdy se nesmaže ani nepřepíše; obsazené místo = odmítnutí; `_REPORT` ignoruje ASC MHL i Ověřit zálohu). Nová kopie jde přes `.offload-part` jako vždy.
- Odmítne se (nic se nezapíše): soubor už na kartě není, má jinou velikost nebo čas než v záznamu, dobrá kopie z disku zmizela (jako u doplnění), místo pro odložení je obsazené, dočasné jméno je obsazené, záloha neprojde kontrolami doplnění. „Nic k opravě — všechny soubory jsou na všech cílech v pořádku.“ = Hotovo.
- Záznamy: report generace N s `kind: "repair"`, `files[].rewritten` (true = nová kopie), `setAside`; ASC MHL generace N na všech cílech (přečtená dobrá kopie `action="verified"`, nová `original`, komentář „Offload oprava: …“); `.xxh64` bez opakovaných řádků; `history.json` dostane generaci `kind: "repair"` a úspěch spočítaný z reportů na cílech (`failedFiles` = sjednocení selhaných cest); soubory, otisk a cívky se nemění. Analýza a proxy se neopakují (klipy už analyzované jsou; generace bez analýzy v `CardRecord` analyzovaný klip nepřepíše), PDF, seznamy klipů a denní report ano.
- Přerušená oprava: cíl s dobrou kopií dostane i tak záznam „zapsáno“ (`DestWriter.Keep` před kontrolou zrušení) — nikdy „čeká“; další oprava (generace N+1) ho nepřepíše. Hash pro porovnání se bere z nejnovějšího záznamu, který ho má.
- GUI: tlačítko je na žluté kartě jen u hotové nebo selhané (ne zrušené) úlohy s chybnými soubory, když je kopírovaná karta ve čtečce (složka se bere jako dostupná — planner ji stejně přečte) a žádná oprava téže zálohy nečeká ani neběží (`RepairRules.Offer`). Úloha „A001 · oprava“ jde do fronty kopií (čte kartu) s **vlastní kopií nastavení** dokončené úlohy (`JobConfigs.Repair`: projekt, výstupy, oznámení; vždy s ověřením). CLI příkaz pro opravu není (mimo zadání; do ROZDILY jako Windows GUI).

**Ostatní.**
- **Projekty místo presetů (rozhodnutí 5):** přepínač v liště je přepínač projektů z plánu 8 se stejnou sémantikou (I1: zamčený, dokud Start kontroluje). Odkaz kroku 3 se jmenuje „Upravit projekt“ a otevře stránku Nastavení aktivního profilu; v liště stránky „Projekt <název>“ a „Uložit jako nový projekt…“ (= `SaveAsProjectCommand`). „Hotovo“ i „‹ Zpět“ stránku jen zavřou — změny se ukládají hned jako dosud (400 ms).
- **Nastavení jako stránka, ne okno:** zadání připouští obojí; stránka místo obou sloupců (fronta dál běží, Esc / Ctrl+, ji zavře / otevře). Sekce navíc proti obr. 4: **Oznámení** (souhrn + dosavadní okno) a **Aplikace** (verze, Aktualizace…, Prověřit USB porty) — položky zrušeného menu Nástroje musí zůstat dosažitelné. Stránka Nastavení je **předsunutá před levý sloupec** (postup 6 před 3), aby volby, které levý sloupec ztratí, ani na jeden commit nezmizely.
- **Odhad (nová věc 10):** jen z toho, co tato relace změřila (`RateMemory`: rychlost kopie a ověření poslední úlohy od 200 MB, čas 2. fáze na sekundu záznamu zvlášť pro analýzu a pro proxy daného kodeku/výšky/toku). Chybí-li kterákoli část, odhad není a lišta ukáže jen velikost — první karta relace tedy odhad nemá (zadání: „pokud nejde spolehlivě, zobrazit jen velikost“). Formát `JobProgress.EtaText` (m:ss, nad hodinu h:mm:ss) jako zbytek aplikace.
- **Štítek kamery (nová věc 6):** z prvního klipu karty, který není proxy kamery — video přes ffprobe (nejvýš 5 s) a automatický výběr LUTu (křivka vestavěného LUTu), RAW podle formátu (dekodér RAW je na čerstvě vloženou kartu pomalý). Model bez výrobce („ARRI ALEXA 35“ → „ALEXA 35“).
- **Typ připojení (nová věc 6):** z `DriveLinks` (HW diagnostika): „čtečka USB 3 · 5 Gb/s“, „USB 2.0“, „SD čtečka“, „NVMe“, „SATA“, „síť“. Thunderbolt Windows nerozliší — disk za ním ukáže sběrnici (NVMe, USB); „USB-C“ z mockupu se neukazuje (typ zásuvky zná jen topologie USB, ne disk).
- **Velikosti:** nové plochy používají tři platné číslice jako mockup (`Format.BytesCs3`: „1,70 GB“, „14,2 GB“, „412 GB“); kapacita disku zaokrouhleně („z 8 TB“).
- **Velké procento** na běžící kartě = průběh **aktuální fáze** (jako dosavadní pruh); celek ukazuje ukazatel fází.
- **Cesty** v náhledu a na kartě s oddělovačem Windows `\` (mockup je společný s Macem a má `/`).
- **Řádek Report** kroku 3 vyjmenuje i zapnuté seznamy klipů: výchozí „PDF · HTML · ASC MHL · CSV“ (CSV je ve výchozím stavu zapnuté).
- **Karta úlohy** mění vzhled podle `JobCard.Headline` (DataTriggery na `Kind`, ne selector — ten vybírá jen jednou). Stage 2 selhal po ověřené kopii = zelená karta + oranžový řádek chyby (kartu lze vysunout, jako `JobOutcomes`); neověřená kopie = žlutá „zkopírováno bez ověření“. Nesouhlasné soubory: prvních 5 + „… a další N souborů v reportu“. Náhledy: prvních 5 klipů (prostřední ze 3 snímků, z paměti reportu — nic se nečte z disku), šestá dlaždice „+N“.
- **Dnes a historie (nová věc 7):** z `history.json`, které okno už načítá; „Dnes“ podle místního data konce zálohy; záložka Historie karet ukáže nejvýš 300 posledních karet po dnech.
- **Náhled burn-inu (nová věc 8):** šedý rám 16:9 bez snímku; „Frame lines“ z mockupu = existující volba **Výřez** (ořez na frame lines), rám se kreslí jen se zvoleným výřezem a jeho skutečnou velikostí.
- **Písmo a prostrkání:** WPF nemá prostrkání písmen — nadpisy kroků jsou jen velkými písmeny (12 / SemiBold / Text2).
- **Okno** 1360 × 900 výchozí (minimum 940 × 640 beze změny), sloupec vlevo 400 (360–480) s GridSplitterem jako dosud.
- **Dialogy** (Hardware, Oznámení, Aktualizace, Název projektu, Pravidla LUTů, Karty už jsou zálohované) zůstávají ve Fluent stylu — řídí se tématem Windows stejně jako nové tokeny.
- **Hlášky doplnění** odkazovaly na zrušené menu („…přes Nástroje → Ověřit zálohu…“): nově „…přes Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify)…“ (`Msg.VerifyHint`, úkol 11).
- **Ukázkový režim** (`--demo`): výchozí vypnutý, data z mockupů (`DemoScenario`) místo skenů, prověření a historie; běžící kopie má 170 GB a hotová 128 GB, aby obraz vydržel minuty a rychlosti/časy seděly s mockupem (602 / 799 MB/s, 6 min 12 s). Úloha náhledu (`OffloadJob.ForPreview`) nikdy neběží; zavření okna ji zruší.

## Mapa: kde je každá dnešní funkce v novém vzhledu

| Dnes | Nové místo | Úkol |
|---|---|---|
| Menu **Projekt** (Uložit jako projekt…, Přejmenovat…, Smazat…) | horní lišta → rozbalovací **Projekt <název> ▾** (seznam projektů s odškrtnutím aktivního, „Uložit jako nový projekt…“, „Přejmenovat…“, „Smazat…“); „Uložit jako nový projekt…“ i v liště Nastavení | 11, 12 |
| „Projekt:“ ComboBox v levém panelu | tamtéž (lišta) — z levého panelu zmizí | 11 |
| Titulek okna „Offload — Film X“ | beze změny | — |
| Nástroje → **Ověřit zálohu…** (Ctrl+Shift+O) | horní lišta „Ověřit zálohu“ (Ctrl+Shift+O); i v Nastavení → Ověření | 11, 12 |
| Nástroje → **Ukládat report ověření** | Nastavení → Ověření („Ukládat report ověření“) | 12 |
| Nástroje → **Oznámení…** | Nastavení → Oznámení („Upravit oznámení…“, souhrn) | 12 |
| Nástroje → **Aktualizace…**; odkaz „Nová verze …“ | Nastavení → Aplikace („Aktualizace…“); odkaz „Nová verze …“ vpravo v horní liště | 11, 12 |
| Nástroje → **Denní report…** | „Denní report…“ u DNES pod frontou, v záložce Historie karet a v Nastavení → Report | 12, 16 |
| Nástroje → **Prověřit USB porty…** (Ctrl+U); sekce Hardware s „Prověřit…“ a souhrnem | horní lišta „Hardware“ s tečkou (Ctrl+U; souhrn v bublině); i Nastavení → Aplikace | 11, 12 |
| Okno Hardware (měření, názvy portů) | beze změny (otevře ho tlačítko Hardware) | — |
| Karty: seznam, zaškrtnutí, ikona, název, detail | krok „1 · KARTY“: dlaždice s ikonou, názvem, štítkem kamery, „24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s“, zaškrtávátko | 13 |
| ⟳ Znovu načíst připojené disky; „Složka…“ | ⟳ a „Ze složky…“ v hlavičce kroku 1 | 13 |
| Řádek historie u karty (Zálohováno … / NEOVĚŘENO / s chybami / změnila se) | tamtéž pod údaji karty (barva podle tónu) | 13 |
| „Doplnit jen nové klipy…“ (zaškrtávátko), „Složka: …“ | žluté tlačítko **Doplnit** na dlaždici (přepínač; zaškrtne i kartu), bublina = dosavadní text, pod tím „Složka: …“ | 13 |
| Název složky + zástupný text + nápověda | „Název složky — <nápověda>“ + pole v monospace se zástupným textem | 13 |
| Prázdný stav „Vložte kartu do čtečky — objeví se tady automaticky.“ | přerušovaný rámeček „Vložte kartu do čtečky“ / během offloadu „Vložte další kartu“ | 13 |
| Cíle: Přidat…, ✕, číslo, název, volno, FS, „Doporučeno NTFS“, „Disk není připojen“, rada pro žádný cíl, „Jen jeden cíl…“ | krok „2 · CÍLE“: „+ Přidat disk“, dlaždice s kolečkem, „… volných z … · FS · připojení“, ✕, pruh místa, tytéž hlášky; během offloadu kompaktní seznam | 13 |
| Vlastní datum + náhled cesty | Nastavení → Struktura složek (datum, cesta); náhled cesty „…\2026-10-07\A035_ARRI\“ pod cíli | 12, 13 |
| Kontrolní součet, MD5 poznámka, Ověřit zpětným čtením + varování | Nastavení → Ověření; řádek „Ověření zpětným čtením“ v kroku 3 (vypnuté žlutě s varováním) | 12, 13 |
| Analýza klipů a náhledy | Nastavení → Analýza a náhledy; řádek v kroku 3 | 12, 13 |
| Navrhovat název karty podle klipů | Nastavení → Struktura složek | 12 |
| Seznam klipů CSV / ALE, Denní report po každé kartě | Nastavení → Report; řádek Report v kroku 3 | 12, 13 |
| Sekce Proxy (vše: kodek, výška, tok, výřez, LUT, záložní LUT, Vlastní…, Pravidla LUTů…, burn-in, zvuk, kamerové proxy, kopie na cíle, složka pro proxy) | Nastavení → Proxy (obr. 4); shrnutí v kroku 3 | 12, 13 |
| Editor pravidel LUTů (okno) | beze změny, tlačítko „Pravidla LUTů… (N)“ v Nastavení → Proxy | 12 |
| Dekodéry (nástroje, …, poznámka CRAW/X-OCN, Gen 4 → Gen 5) | Nastavení → Dekodéry RAW | 12 |
| Červené/oranžové LutProblem, ProxyFolderProblem, ToolsWarning | pod krokem 3 (blokují start) a v Nastavení → Proxy | 12, 13 |
| Lišta se spuštěním: CardsText, DestinationsText, Spustit offload (Ctrl+Enter), StartProblem, StartStatus, SpaceProblem, SpaceWarning | připnutá spodní lišta: „**1 karta → 2 disky** · 1,70 GB“ + odhad, tytéž hlášky nad tlačítkem, „▶ Spustit offload [Ctrl ↵]“ / „Zařadit do fronty“; hlášky místa i pod dlaždicí disku | 13 |
| Dotaz „Karty už jsou zálohované“, dotaz S1 (prověření nedoběhlo), hlášky „Prověřuji…“ | beze změny (dialogy a `StartStatus`) | — |
| Fronta: prázdný stav „Zatím žádný offload…“ | karta s ikonami Karta → disky → Ověření → Proxy → Report a „Připraveno — … na 2 disky“ / „Zatím žádný offload“ | 14 |
| Vyčistit hotové | odkaz vpravo nad frontou | 14 |
| Karta úlohy: stav, název, soubory, cíle, průběh, detail, řetěz připojení + úzké hrdlo, Zrušit | běžící karta: ukazatel fází, tabulka rychlostí (bublina řádku = řetěz), „Soubor 10 / 24“, **Pozastavit/Pokračovat**, Zrušit, „zbývá …“, „Nejpomalejší článek: …“ | 14, 15 |
| Kartu lze vysunout + Vysunout kartu, Karta vysunuta, souhrn, Report, Průzkumník | zelená karta: „… — kartu lze vysunout“, Vysunout kartu, 4 čísla, náhledy, Report PDF, Otevřít složku, Proxy, cesta | 14, 15 |
| N souborů selhalo / chyba / zrušeno / varování / poznámka / poznámka projektu | žlutá (nesouhlasí, + seznam souborů a **Zkopírovat znovu**), červená (chyba), šedá (zrušeno); varování a poznámky jako řádky karty | 14, 15 |
| Ověření zálohy jako karta fronty (průběh, výsledek, varování, Report ověření, Průzkumník, Zrušit) | stejné údaje v kartě nového stylu | 14 |
| Zvuk, blikání, oznámení Windows/ntfy, ikona v oznamovací oblasti | beze změny | — |
| Zkratky Ctrl+Enter, Ctrl+U, Ctrl+Shift+O | beze změny; navíc Ctrl+, (Nastavení) a Esc (zavřít Nastavení) | 11, 12 |
| Zavření s běžící úlohou (dotaz, úklid) | beze změny (i během pauzy) | 3 |
| Vzhled podle Windows (Fluent) | tmavé i světlé tokeny podle Windows | 10 |

## Struktura souborů

| Soubor | Úkol | Odpovědnost |
|---|---|---|
| `docs/ui-navrh/**` | 1 | závazný návrh v repu + rozhodnutí |
| `Offload.Core/PauseGate.cs`, `OffloadEngine*.cs`, `DestWriter.cs`, `Hashing.cs`, `Models.cs`, `OffloadReport.cs`, `HtmlReport.cs` | 2 | pauza kopie a zpětného čtení, čas pauzy v reportu |
| `Offload.Core/OffloadJob.cs`, `Desktop/JobProgress.cs`, `Hardware/BrakeMeter.cs` | 3 | pauza úlohy a 2. fáze, časovače karty |
| `Offload.Core/Append/RepairPlanner.cs`, `Append/AppendPlanner.cs`, `Mhl.cs`, `CardRecord.cs`, `Messages.cs` (+ engine, report) | 4 | plán a zápis opravy |
| `Offload.Core/Desktop/RepairRules.cs`, `Desktop/JobConfigs.cs`, `History/HistoryEntry.cs`, `OffloadJob.cs` | 5 | oprava jako úloha, historie |
| `Offload.Core/Desktop/JobCard.cs`, `Format.cs` | 6 | data nové karty úlohy, pilulka, popisek tlačítka |
| `Offload.Core/Desktop/TimeEstimate.cs` | 7 | odhad času |
| `Offload.Core/Desktop/CardFacts.cs`, `History/CardInspector.cs`, `ClipDetector.cs` | 8 | štítek kamery, připojení, dlaždice disku |
| `Offload.Core/Desktop/SetupTexts.cs`, `Desktop/HistoryView.cs` | 9, 13 | texty levého sloupce, prázdná fronta, dnešní historie |
| `Offload.App/Themes/*.xaml`, `ThemeWatcher.cs`, `Offload.Core/Desktop/ThemeChoice.cs` | 10 | tokeny, styly, ikony, přepínání tématu |
| `Offload.App/MainWindow.xaml`, `MainViewModel.TopBar.cs`, `Offload.Core/Desktop/TopBar.cs` | 11 | horní lišta |
| `Offload.App/SettingsPage.xaml(.cs)`, `MainViewModel.SettingsPage.cs`, `ProxyOptionsViewModel.cs`, `Offload.Core/Desktop/SettingsTexts.cs` | 12 | stránka Nastavení |
| `Offload.App/SetupPanel.xaml`, `SourceItemViewModel.cs`, `DestinationViewModel.cs`, `MainViewModel.Setup/Space/History/Queue.cs` | 13 | levý sloupec |
| `Offload.App/QueuePanel.xaml`, `JobItemViewModel.cs`, `JobItemViewModel.Card.cs` | 14, 15 | fronta a karty úloh |
| `Offload.App/QueuePanel.xaml`, `MainViewModel.History.cs` | 16 | DNES a Historie karet |
| `Offload.Core/Desktop/DemoScenario.cs`, `Desktop/StartupOptions.cs`, `Offload.App/MainViewModel.Demo.cs`, `windows/scripts/app-drive.ps1` | 17 | ukázkový režim a pomůcky pro snímky |
| `windows/Directory.Build.props`, docs (`VYVOJ-*`, `ROZDILY`, `FORMATY`), `windows/README.md`, `windows/package/navod/*` | 18 | verze 0.5.0, dokumentace, návod |
| — | 19 | vizuální kontrola (snímky proti `docs/ui-navrh/screeny`) |

---

### Task 1: Návrh nového vzhledu v repu jako závazný podklad

Složka `docs/ui-navrh/` je v pracovní kopii, ale není v gitu (`?? docs/ui-navrh/`). Úkol ji přidá beze změny mockupů
a snímků, doplní README o rozhodnutí uživatele (od té chvíle součást zadání) a založí sekci plánu 9 v
`docs/VYVOJ-WINDOWS.md`. Kód se nemění.

**Files:**
- Create (přidat do gitu tak, jak jsou): `docs/ui-navrh/mockup/01-pripraveno.html`, `docs/ui-navrh/mockup/02-probiha-offload.html`, `docs/ui-navrh/mockup/03-hotovo-a-chyba.html`, `docs/ui-navrh/mockup/04-nastaveni-proxy.html`, `docs/ui-navrh/screeny/01-pripraveno.png`, `docs/ui-navrh/screeny/01-pripraveno-uzke.png`, `docs/ui-navrh/screeny/02-probiha-offload.png`, `docs/ui-navrh/screeny/03-hotovo-a-chyba.png`, `docs/ui-navrh/screeny/04-nastaveni-proxy.png`
- Create + Modify: `docs/ui-navrh/README.md` (doplnit sekci na konec)
- Modify: `docs/VYVOJ-WINDOWS.md`

**Interfaces:**
- Consumes: —
- Produces: sekce `## Rozhodnutí (2026-10-07, závazné)` v `docs/ui-navrh/README.md` (na ni odkazují všechny další úkoly);
  sekce `## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)` v `docs/VYVOJ-WINDOWS.md` (do ní píše každý úkol).

- [ ] **Step 1: Ověřit, že návrh je v pracovní kopii celý**

```powershell
git status --short docs\ui-navrh
Get-ChildItem -Recurse -File docs\ui-navrh | Select-Object FullName, Length
```

Expected: `?? docs/ui-navrh/`; 10 souborů — `README.md`, 4 × `mockup\0N-*.html`, 5 × `screeny\*.png`. Mockupy ani
snímky se neupravují.

- [ ] **Step 2: Doplnit rozhodnutí na konec `docs/ui-navrh/README.md`**

Najít (1/1):

```markdown
## Otevřené otázky

- Jen tmavé téma, nebo i světlé (tokeny jsou připravené na dvě sady)?
- Presety ano/ne a kde je ukládat.
- „Zkopírovat znovu“: jen chybné soubory, nebo celou kartu na postižený disk?
- Přibalit IBM Plex, nebo systémová písma?
```

Nahradit:

```markdown
## Otevřené otázky

- Jen tmavé téma, nebo i světlé (tokeny jsou připravené na dvě sady)?
- Presety ano/ne a kde je ukládat.
- „Zkopírovat znovu“: jen chybné soubory, nebo celou kartu na postižený disk?
- Přibalit IBM Plex, nebo systémová písma?

## Rozhodnutí (2026-10-07, závazné)

Odpovědi uživatele na otevřené otázky a výběr z [Nových věcí](#nové-věci-které-v-kódu-zatím-nejsou). Plán implementace:
`windows/docs/superpowers/plans/2026-10-07-offload-windows-9-ui.md` (+ `-2.md`).

- **Téma:** tmavé **i** světlé, řídí se nastavením Windows (Přizpůsobení → Barvy → Režim aplikací). Tabulka barev výše
  je tmavá sada; světlá sada je z ní odvozená v `windows/src/Offload.App/Themes/Light.xaml` (stejné klíče, kontrast
  textů min. 4,5 : 1 hlídá test).
- **Písmo:** systémové — Segoe UI (Segoe UI Variable Text, kde je) a pro cesty, soubory, rychlosti a hashe Cascadia Mono
  / Consolas. IBM Plex se nepřibaluje.
- **Nové věci 1–4 a 10:** ano.
- **„Zkopírovat znovu“:** jen soubory, které nesouhlasí, a jen na disk, kde nesouhlasí — nová úloha **oprava** (další
  generace ASC MHL ve stejné složce karty; dobrý soubor se nikdy nepřepíše, nesouhlasná kopie se odloží, nová se ověří;
  report, MHL a historie podle pravidel doplnění z plánu 6).
- **5 Presety:** ne. Na místě přepínače presetu je přepínač **projektů** z plánu 8 se stejným chováním; v UI se píše
  „Projekt“, nikdy „Preset“ (`Projekt  Film X ▾`, „Upravit projekt“, „Uložit jako nový projekt“).
- **6 Štítek kamery a typ připojení:** ano — z rychlé analýzy karty (jeden klip, jen čtení) a z HW diagnostiky
  (`DriveLinks`). Thunderbolt Windows nerozliší: disk za ním se ukáže podle sběrnice (NVMe, USB).
- **7 Dnešní historie:** ano — záložka „Historie karet“ a pod frontou „DNES“, z `history.json`.
- **8 Náhled burn-inu:** šedý rám s frame lines, názvem klipu a TC (bez skutečného snímku).
- **9 Pozastavit:** ano — nová funkce jádra (kopie, ověření i zpracování po kopii).
- **Mac:** beze změny, dokud uživatel neschválí Windows verzi; rozdíly jdou do `docs/ROZDILY.md`.
```

- [ ] **Step 3: Tabulka plánů a sekce plánu 9 v `docs/VYVOJ-WINDOWS.md`**

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1):

```markdown
| 6 | karty a bezpečnost dat: historie karet, doplnění nových klipů, název z klipů, ověření zálohy proti ASC MHL, kontrola místa, české hlášky (0.2.0) | hotovo (čeká ruční test s kartou) |
| 7 | výstupy pro produkci a střih: denní report, seznam klipů CSV/ALE, proxy pro přelinkování (reel name, všechny zvukové stopy), MD5 navíc k xxh64 (0.3.0) | hotovo (čeká ruční test: Resolve, Premiere, Avid, Excel) |
| 8 | nastavení, oznámení, distribuce: projekty, vlastní pravidla LUTů, BRAW Film Gen 4 → Gen 5, oznámení Windows a ntfy, kontrola aktualizací, verze z `Directory.Build.props`, `build.ps1 -Sign` (0.4.0) | hotovo (čeká ruční test: oznámení, podpis; Gen 4 čeká vzorek) |

## 2026-10-07 — plán 8: nastavení, oznámení, distribuce
```

Nahradit:

```markdown
| 6 | karty a bezpečnost dat: historie karet, doplnění nových klipů, název z klipů, ověření zálohy proti ASC MHL, kontrola místa, české hlášky (0.2.0) | hotovo (čeká ruční test s kartou) |
| 7 | výstupy pro produkci a střih: denní report, seznam klipů CSV/ALE, proxy pro přelinkování (reel name, všechny zvukové stopy), MD5 navíc k xxh64 (0.3.0) | hotovo (čeká ruční test: Resolve, Premiere, Avid, Excel) |
| 8 | nastavení, oznámení, distribuce: projekty, vlastní pravidla LUTů, BRAW Film Gen 4 → Gen 5, oznámení Windows a ntfy, kontrola aktualizací, verze z `Directory.Build.props`, `build.ps1 -Sign` (0.4.0) | hotovo (čeká ruční test: oznámení, podpis; Gen 4 čeká vzorek) |
| 9 | nový vzhled podle `docs/ui-navrh/`: tmavé a světlé téma, horní lišta, kroky 1–3, karty úloh s průběhem po cílech, pozastavení, oprava nesouhlasných souborů, dnešní historie, okno Nastavení (0.5.0) | rozpracováno |

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 1 — návrh nového vzhledu `docs/ui-navrh/` je v repu jako závazný podklad (README = zadání, `mockup/*.html` = přesné barvy, rozměry a texty, `screeny/*.png` = náhledy); README doplněno o rozhodnutí uživatele z 2026-10-07 (tmavé i světlé téma podle Windows, systémové písmo, nové věci 1–4, 6, 7, 9 a 10, přepínač projektů místo presetů, „Zkopírovat znovu“ jen nesouhlasné soubory na postižený disk)

## 2026-10-07 — plán 8: nastavení, oznámení, distribuce
```

- [ ] **Step 4: Kontrola — kód se nezměnil**

```powershell
git status --short
```

Expected: jen `?? docs/ui-navrh/` a ` M docs/VYVOJ-WINDOWS.md` (a soubory tohoto plánu); build ani testy se nespouštějí,
žádný `.cs` ani `.xaml` se nezměnil.

- [ ] **Step 5: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Docs: UI design docs/ui-navrh as the binding input for plan 9

The design (README = brief, mockups = exact colours, sizes and texts, screenshots) goes into the
repo unchanged; the README gets the user's decisions of 2026-10-07 (dark and light theme by Windows,
system fonts, new things 1-4, 6, 7, 9 and 10, projects instead of presets, "Zkopírovat znovu" only
the failed files to the failed disk). VYVOJ-WINDOWS: plan 9 row and section.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\ui-navrh docs\VYVOJ-WINDOWS.md
git commit -F .git/COMMIT_MSG_PLAN9
git show --stat HEAD
```

Expected: 11 souborů (10 v `docs/ui-navrh`, `docs/VYVOJ-WINDOWS.md`).

---

### Task 2: Pozastavení v jádru — kopie a zpětné čtení (nová věc 9, 1. část)

Kopie i ověření se musí dát zastavit a zase rozběhnout bez rušení: brána `PauseGate` se kontroluje před každým
souborem a před každým 16MB blokem (dřív, než si čtení vezme buffer, takže zapisovače doběhnou a stojí) a před každým
blokem zpětného čtení. Report si čas pauzy uloží a z rychlostí ho odečte. Testy pokrývají Review Focus 1 a 2: vytažený
disk během pauzy a zrušení během pauzy. Pomocník `TestThreads` pouští testovanou práci na vlastním vlákně — pod zátěží
celé sady by `Task.Run` mohl začít až po limitu testu.

**Files:**
- Create: `windows/src/Offload.Core/PauseGate.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.Core/DestWriter.cs`
- Modify: `windows/src/Offload.Core/Hashing.cs`
- Modify: `windows/src/Offload.Core/HtmlReport.cs`
- Modify: `windows/src/Offload.Core/Models.cs`
- Modify: `windows/src/Offload.Core/OffloadEngine.Run.cs`
- Modify: `windows/src/Offload.Core/OffloadEngine.cs`
- Modify: `windows/src/Offload.Core/OffloadReport.cs`
- Test: `windows/tests/Offload.Core.Tests/EnginePauseTests.cs` (nový)
- Test: `windows/tests/Offload.Core.Tests/PauseGateTests.cs` (nový)
- Test: `windows/tests/Offload.Core.Tests/ReportPauseTests.cs` (nový)
- Test: `windows/tests/Offload.Core.Tests/TestThreads.cs` (nový)

**Interfaces:**
- Consumes: `OffloadEngine` (`Run`, `Cancel`, `Snapshot`, `cts`), `DestWriter.Verify`, `Hashing.HashFile`, `OffloadReport.From…` (stávající).
- Produces:
  - `public sealed class PauseGate(Func<DateTime>? clock = null)` — `bool IsPaused`, `bool IsHolding`, `void Pause()`, `void Resume()`, `TimeSpan PausedTotal`, `bool WaitWhilePaused(CancellationToken ct)` (false = zrušeno).
  - `OffloadEngine.Pauses` (`PauseGate`, jedna na engine).
  - `ProgressSnapshot(…, bool Paused = false, bool PauseHeld = false)` — dva poslední poziční parametry navíc.
  - `OffloadOptions.BeforeDestWrite` (`internal Action<int>?`, index cíle; jen testy) — volá se před zápisem každého bloku.
  - `Hashing.HashFile(…, Action? beforeBlock = null)` (obě přetížení); `DestWriter.Verify(…, PauseGate? pause = null)`.
  - `OffloadReport.CopyPausedSeconds`, `VerifyPausedSeconds` (`double?`, JSON `copyPausedSeconds` / `verifyPausedSeconds`), `PausedSeconds`; `CopySeconds` a `VerifySeconds` pauzu odečítají.
  - testy: `static class TestThreads` — `Task<T> Start<T>(Func<T>)`, `Task Start(Action)` (vlastní vlákno, `LongRunning`).

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/EnginePauseTests.cs` (nový soubor)

```csharp
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Pozastavit in stage 1: the copy and the read-back stop between blocks, the report leaves the pause out of the
/// speeds, a cancel or a disk pulled out while paused end as they do without a pause.
public class EnginePauseTests
{
    const int Chunk = 64 * 1024;

    static OffloadOptions Options() => new() { ChunkSize = Chunk, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    /// A card with `count` files of `size` bytes.
    static string Card(TempDir dir, int count = 3, int size = 3 * Chunk + 5)
    {
        var card = dir.Sub("A001");
        for (int i = 0; i < count; i++) TestFs.WriteRandom(Path.Combine(card, "CLIP", $"A001C{i + 1:000}.mov"), size, i + 1);
        return card;
    }

    static bool Until(Func<bool> condition) => SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(30));

    /// Paused, the job comes to rest — the writers finish the blocks they hold, then nothing moves: two snapshots 300 ms
    /// apart are the same (within 30 s). Returns the resting snapshot.
    static ProgressSnapshot AssertComesToRest(OffloadEngine engine)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var a = engine.Snapshot();
            Thread.Sleep(300);
            var b = engine.Snapshot();
            if (a.ReadBytes == b.ReadBytes && a.WrittenBytes.SequenceEqual(b.WrittenBytes) && a.VerifiedBytes.SequenceEqual(b.VerifiedBytes)) return b;
            Assert.True(DateTime.UtcNow < deadline, "the paused job did not come to rest");
        }
    }

    [Fact]
    public async Task PauseBeforeAFile_CopyHolds_ResumeFinishes_SpeedsLeaveThePauseOut()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var options = Options();
        var engine = new OffloadEngine(options);
        options.BeforeFileCopy = i => { if (i == 1) engine.Pauses.Pause(); };
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        var held = AssertComesToRest(engine);
        Assert.True(held.Paused);
        Assert.Equal(CopyPhase.Copy, held.Phase);
        Assert.Equal(3 * Chunk + 5, held.ReadBytes);   // file 1 not started
        Assert.Equal([3 * Chunk + 5, 3 * Chunk + 5], held.WrittenBytes);
        Thread.Sleep(400);
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(report.SafeToEject);
        Assert.False(engine.Snapshot().Paused);
        Assert.True(report.CopyPausedSeconds >= 0.6, $"{report.CopyPausedSeconds}");
        Assert.Null(report.VerifyPausedSeconds);
        Assert.Equal((report.CopyFinished - report.Started).TotalSeconds - report.CopyPausedSeconds!.Value, report.CopySeconds, 3);
        foreach (var path in plan.CardPaths) Assert.True(BackupVerifier.Verify(path, chunkSize: 4096).Success);
    }

    // The gate is checked before every block: a pause pressed inside a big file stops it within the buffer pool.
    [Fact]
    public async Task PauseInsideAFile_StopsBetweenBlocks()
    {
        using var dir = new TempDir();
        var card = Card(dir, count: 1, size: 40 * Chunk);
        var options = Options();
        var engine = new OffloadEngine(options);
        int blocks = 0;
        options.BeforeDestWrite = d => { if (d == 0 && Interlocked.Increment(ref blocks) == 2) engine.Pauses.Pause(); };
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        var held = AssertComesToRest(engine);
        Assert.InRange(held.ReadBytes, 2L * Chunk, (2L + options.BufferCount + 1) * Chunk);   // mid-file, one pool ahead at most
        engine.Pauses.Resume();

        Assert.True((await run.WaitAsync(TimeSpan.FromSeconds(30))).SafeToEject);
    }

    [Fact]
    public async Task PauseDuringReadBack_VerifyHolds_ResumeVerifies()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var options = Options();
        var engine = new OffloadEngine(options);
        options.BeforeVerify = () => engine.Pauses.Pause();
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        Assert.Equal(CopyPhase.Verify, AssertComesToRest(engine).Phase);
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(report.SafeToEject);
        Assert.True(report.VerifyPausedSeconds >= 0.3, $"{report.VerifyPausedSeconds}");
        Assert.Null(report.CopyPausedSeconds);
    }

    // Cancel while paused (and closing the app, which cancels): the copy ends at once, nothing half-written stays.
    [Fact]
    public async Task CancelWhilePaused_EndsPromptly_NoPartFiles()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var options = Options();
        var engine = new OffloadEngine(options);
        options.BeforeFileCopy = i => { if (i == 1) engine.Pauses.Pause(); };
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        engine.Cancel();

        var report = await run.WaitAsync(TimeSpan.FromSeconds(10));   // promptly, not after a timeout
        Assert.False(report.Success);
        Assert.Equal(["cancelled", "cancelled"], report.Files.Skip(1).Select(f => f.SourceError));
        Assert.Equal(["written", "written"], report.Files[0].Destinations);   // copied before the pause; a cancel skips the read-back
        foreach (var root in plan.DestRoots)
            Assert.Empty(Directory.GetFiles(root, "*.offload-part", SearchOption.AllDirectories));
    }

    // A disk pulled out while paused: the next write after Resume fails there like any write error — that file fails on
    // that destination only, the others go on and the report says which ("Zkopírovat znovu" repairs it later).
    [Fact]
    public async Task DiskGoneWhilePaused_FailsOnThatDestinationOnly()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var options = Options();
        var engine = new OffloadEngine(options);
        bool gone = false;
        options.BeforeFileCopy = i => { if (i == 1) engine.Pauses.Pause(); };
        options.BeforeDestWrite = d => { if (d == 1 && Volatile.Read(ref gone)) throw new IOException("Zařízení není připojeno."); };
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        AssertComesToRest(engine);                    // file 0 is on both disks
        Volatile.Write(ref gone, true);
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(report.Success);
        Assert.Equal(["verified", "verified"], report.Files[0].Destinations);
        foreach (var f in report.Files.Skip(1))
        {
            Assert.Equal("verified", f.Destinations[0]);
            Assert.Equal("failed", f.Destinations[1]);
            Assert.Contains("Zařízení není připojeno.", f.Errors[1]);
        }
        Assert.Empty(Directory.GetFiles(plan.DestRoots[1], "*.offload-part", SearchOption.AllDirectories));
    }

    [Fact]
    public void ReadBack_BeforeBlock_RunsBeforeEveryBlock()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "f.bin");
        TestFs.WriteRandom(path, 3 * 4096 + 1, 1);
        using var buf = new AlignedBuffer(4096);
        int calls = 0;
        Hashing.HashFile(path, buf, HashKind.Xxh64, beforeBlock: () => calls++);
        Assert.Equal(4, calls);
    }
}
```

#### `windows/tests/Offload.Core.Tests/PauseGateTests.cs` (nový soubor)

```csharp
namespace Offload.Core.Tests;

/// "Pozastavit" (UI návrh, nová věc 9): the gate every worker of a job waits at.
public class PauseGateTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PausedTotal_CountsFromPauseToResume_FakeClock()
    {
        var now = T0;
        var gate = new PauseGate(() => now);
        Assert.False(gate.IsPaused);
        gate.Pause();
        now = now.AddSeconds(5);
        gate.Pause();                                  // a second press changes nothing
        Assert.True(gate.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(5), gate.PausedTotal);   // still paused: counted up to now
        now = now.AddSeconds(3);
        gate.Resume();
        now = now.AddSeconds(100);
        gate.Resume();                                 // a resume without a pause changes nothing
        Assert.False(gate.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(8), gate.PausedTotal);
        gate.Pause();
        now = now.AddSeconds(2);
        gate.Resume();
        Assert.Equal(TimeSpan.FromSeconds(10), gate.PausedTotal);
    }

    [Fact]
    public void Open_PassesAtOnce_CancelledNever()
    {
        var gate = new PauseGate();
        Assert.True(gate.WaitWhilePaused(CancellationToken.None));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.False(gate.WaitWhilePaused(cts.Token));
        Assert.False(gate.IsHolding);
    }

    [Fact]
    public async Task Paused_HoldsUntilResume_IsHoldingMeanwhile()
    {
        var gate = new PauseGate();
        gate.Pause();
        var worker = TestThreads.Start(() => gate.WaitWhilePaused(CancellationToken.None));
        Assert.True(SpinWait.SpinUntil(() => gate.IsHolding, TimeSpan.FromSeconds(30)));
        Thread.Sleep(200);
        Assert.False(worker.IsCompleted);
        gate.Resume();
        Assert.True(await worker.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(gate.IsHolding);
    }

    // Cancel while paused (also closing the app, which cancels every job): the worker leaves the gate at once with false.
    [Fact]
    public async Task CancelWhilePaused_ReleasesWithFalse()
    {
        var gate = new PauseGate();
        gate.Pause();
        using var cts = new CancellationTokenSource();
        var worker = TestThreads.Start(() => gate.WaitWhilePaused(cts.Token));
        Assert.True(SpinWait.SpinUntil(() => gate.IsHolding, TimeSpan.FromSeconds(30)));
        cts.Cancel();
        Assert.False(await worker.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(gate.IsPaused);   // the gate itself stays paused; the job ends as cancelled
    }
}
```

#### `windows/tests/Offload.Core.Tests/ReportPauseTests.cs` (nový soubor)

```csharp
namespace Offload.Core.Tests;

/// The pause in the report: `copyPausedSeconds` / `verifyPausedSeconds` (absent when never paused), speeds without it.
public class ReportPauseTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);

    static OffloadPlan Plan() => new()
    {
        Source = @"E:\", Date = "2026-10-07", CardName = "A001", DestRoots = [@"D:\dst"],
        Scan = new SourceScan { Root = @"E:\", Files = [new SourceFile("CLIP/A001C001.mov", 1_000_000_000, T0)], Dirs = ["CLIP"], Skipped = [] },
    };

    static OffloadReport Build(TimeSpan copyPaused, TimeSpan verifyPaused) =>
        OffloadReport.Build(Plan(), [new FileHashes(1UL, null)], [null], [[new DestFileResult(FileState.Verified, null)]], [true],
            verified: true, T0, T0.AddSeconds(40), T0.AddSeconds(50), copyPaused, verifyPaused);

    [Fact]
    public void Paused_KeysRoundTrip_SpeedsWithoutThePause_TxtAndHtmlSayIt()
    {
        var r = Build(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(4.04));

        Assert.Equal(30, r.CopyPausedSeconds);
        Assert.Equal(4.0, r.VerifyPausedSeconds);
        Assert.Equal(10, r.CopySeconds, 3);
        Assert.Equal(6, r.VerifySeconds, 3);
        Assert.Equal(34, r.PausedSeconds, 3);
        var again = OffloadReport.FromJson(r.ToJson());
        Assert.Equal((30.0, 4.0), (again.CopyPausedSeconds!.Value, again.VerifyPausedSeconds!.Value));
        Assert.Contains("Copy:        10.0 s  100 MB/s\n", r.Summary);
        Assert.Contains("Paused:      34.0 s (not in the speeds)\n", r.Summary);
        Assert.Contains("pozastaveno · mimo rychlosti", HtmlReport.Render(r));
    }

    [Fact]
    public void NeverPaused_NoKeys_NoLine()
    {
        var r = Build(TimeSpan.Zero, TimeSpan.Zero);

        Assert.DoesNotContain("PausedSeconds", r.ToJson());
        Assert.DoesNotContain("Paused:", r.Summary);
        Assert.DoesNotContain("pozastaveno", HtmlReport.Render(r));
        Assert.Equal(40, r.CopySeconds, 3);
    }
}
```

#### `windows/tests/Offload.Core.Tests/TestThreads.cs` (nový soubor)

```csharp
namespace Offload.Core.Tests;

/// Work a test waits on runs on its own thread, not on the thread pool: under the whole suite (ffmpeg tests, parallel
/// classes) the pool can be busy for seconds, and a Task.Run started that late fails a test's timeout.
static class TestThreads
{
    public static Task<T> Start<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    public static Task Start(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~PauseGateTests|FullyQualifiedName~EnginePauseTests|FullyQualifiedName~ReportPauseTests"`

Expected: FAIL — build selže — `error CS0246: Typ nebo název oboru názvů PauseGate se nenašel` (a `CS1061` u `Pauses`, `BeforeDestWrite`, `CopyPausedSeconds`).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/DestWriter.cs`

Najít (1/4):

```csharp
{
    readonly SerialWorker worker;
    readonly OffloadOptions options;
    SafeFileHandle? handle;
    string partPath = "";
    string? currentError;
```

Nahradit:

```csharp
{
    readonly SerialWorker worker;
    readonly OffloadOptions options;
    readonly int index;
    SafeFileHandle? handle;
    string partPath = "";
    string? currentError;
```

Najít (2/4):

```csharp
    public DestWriter(int index, string cardPath, int fileCount, OffloadOptions options)
    {
        CardPath = cardPath;
        this.options = options;
        Unbuffered = !options.ForceBuffered;
        Results = new DestFileResult[fileCount];
```

Nahradit:

```csharp
    public DestWriter(int index, string cardPath, int fileCount, OffloadOptions options)
    {
        CardPath = cardPath;
        this.index = index;
        this.options = options;
        Unbuffered = !options.ForceBuffered;
        Results = new DestFileResult[fileCount];
```

Najít (3/4):

```csharp
        try
        {
            if (handle == null) return;
            // Unbuffered writes must be whole sectors: the last block is written padded, Finish trims the file.
            int len = Unbuffered ? (c.Length + align - 1) / align * align : c.Length;
            try
```

Nahradit:

```csharp
        try
        {
            if (handle == null) return;
            options.BeforeDestWrite?.Invoke(index);
            // Unbuffered writes must be whole sectors: the last block is written padded, Finish trims the file.
            int len = Unbuffered ? (c.Length + align - 1) / align * align : c.Length;
            try
```

Najít (4/4):

```csharp
    public void Wait() => worker.Wait();

    /// Re-reads every written file from the disk — once, through every hash in `kinds` — and compares it with the
    /// source hashes. The error texts stay English: they go into the report JSON shared with the Mac.
    public void Verify(IReadOnlyList<SourceFile> files, IReadOnlyList<FileHashes> sourceHashes, HashKind kinds, CancellationToken ct)
    {
        using var buf = new AlignedBuffer(options.ChunkSize);
        bool md5 = HashKinds.Normalize(kinds).HasFlag(HashKind.Md5);
        for (int i = 0; i < files.Count; i++)
        {
            if (Results[i].State != FileState.Written) continue;
            if (ct.IsCancellationRequested) return;
            var f = files[i];
            try
            {
                var (got, size) = Hashing.HashFile(Layout.Join(CardPath, f.RelPath), buf, kinds, n => Interlocked.Add(ref verified, n), ct);
                var want = sourceHashes[i];
                if (size != f.Size)
                    Results[i] = new DestFileResult(FileState.Failed, $"size mismatch: {size} != {f.Size}");
```

Nahradit:

```csharp
    public void Wait() => worker.Wait();

    /// Re-reads every written file from the disk — once, through every hash in `kinds` — and compares it with the
    /// source hashes. The error texts stay English: they go into the report JSON shared with the Mac. `pause`: the
    /// read-back waits there before every file and every block (Pozastavit).
    public void Verify(IReadOnlyList<SourceFile> files, IReadOnlyList<FileHashes> sourceHashes, HashKind kinds, CancellationToken ct,
        PauseGate? pause = null)
    {
        using var buf = new AlignedBuffer(options.ChunkSize);
        bool md5 = HashKinds.Normalize(kinds).HasFlag(HashKind.Md5);
        for (int i = 0; i < files.Count; i++)
        {
            if (Results[i].State != FileState.Written) continue;
            if (ct.IsCancellationRequested || (pause != null && !pause.WaitWhilePaused(ct))) return;
            var f = files[i];
            try
            {
                var (got, size) = Hashing.HashFile(Layout.Join(CardPath, f.RelPath), buf, kinds, n => Interlocked.Add(ref verified, n), ct,
                    pause == null ? null : () => pause.WaitWhilePaused(ct));
                var want = sourceHashes[i];
                if (size != f.Size)
                    Results[i] = new DestFileResult(FileState.Failed, $"size mismatch: {size} != {f.Size}");
```

#### `windows/src/Offload.Core/Hashing.cs`

Najít (1/1):

```csharp
    }

    /// Every hash in `kinds` (xxh64 always) in one read of the file — the read-back verification of a copy.
    public static (FileHashes Hashes, long Size) HashFile(string path, AlignedBuffer buffer, HashKind kinds, Action<int>? progress = null, CancellationToken ct = default)
    {
        var xx = HashAlgo.Create("xxh64");
        var md5 = HashKinds.Normalize(kinds).HasFlag(HashKind.Md5) ? HashAlgo.Create("md5") : null;
        long size = HashFile(path, buffer, md5 == null ? [xx] : [xx, md5], progress, ct);
        return (new FileHashes(Convert.ToUInt64(xx.Finish(), 16), md5 == null ? null : Convert.FromHexString(md5.Finish())), size);
    }

    /// The one read loop: the file is read once, straight from the media, and every hash in `hashers` is fed each block.
    /// Returns the size read.
    public static long HashFile(string path, AlignedBuffer buffer, IReadOnlyList<IFileHash> hashers, Action<int>? progress = null, CancellationToken ct = default)
    {
        using var reader = new FileReader(path);
        long offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int n = reader.Read(buffer.Span, offset);
            if (n == 0) break;
```

Nahradit:

```csharp
    }

    /// Every hash in `kinds` (xxh64 always) in one read of the file — the read-back verification of a copy.
    /// `beforeBlock`: called before every block is read (a pause waits there).
    public static (FileHashes Hashes, long Size) HashFile(string path, AlignedBuffer buffer, HashKind kinds, Action<int>? progress = null, CancellationToken ct = default,
        Action? beforeBlock = null)
    {
        var xx = HashAlgo.Create("xxh64");
        var md5 = HashKinds.Normalize(kinds).HasFlag(HashKind.Md5) ? HashAlgo.Create("md5") : null;
        long size = HashFile(path, buffer, md5 == null ? [xx] : [xx, md5], progress, ct, beforeBlock);
        return (new FileHashes(Convert.ToUInt64(xx.Finish(), 16), md5 == null ? null : Convert.FromHexString(md5.Finish())), size);
    }

    /// The one read loop: the file is read once, straight from the media, and every hash in `hashers` is fed each block.
    /// Returns the size read.
    public static long HashFile(string path, AlignedBuffer buffer, IReadOnlyList<IFileHash> hashers, Action<int>? progress = null, CancellationToken ct = default,
        Action? beforeBlock = null)
    {
        using var reader = new FileReader(path);
        long offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            beforeBlock?.Invoke();
            ct.ThrowIfCancellationRequested();
            int n = reader.Read(buffer.Span, offset);
            if (n == 0) break;
```

#### `windows/src/Offload.Core/HtmlReport.cs`

Najít (1/1):

```csharp
        Stat(h, videoClips.Count.ToString(Inv), "klipů" + (totalDuration > 0 ? " · " + Duration(totalDuration) : ""));
        Stat(h, Duration(r.CopySeconds), "kopírování · " + Format.Rate(r.TotalBytes, r.CopySeconds));
        Stat(h, r.Verified ? Duration(r.VerifySeconds) : "—", "ověření · " + (r.Verified ? Format.Rate(r.TotalBytes, r.VerifySeconds) : "neprovedeno"));
        Stat(h, Esc(string.Join(" + ", r.Algorithms.Select(a => a.ToUpperInvariant()))), "kontrolní součet");
        h.Append("</div>\n\n");
```

Nahradit:

```csharp
        Stat(h, videoClips.Count.ToString(Inv), "klipů" + (totalDuration > 0 ? " · " + Duration(totalDuration) : ""));
        Stat(h, Duration(r.CopySeconds), "kopírování · " + Format.Rate(r.TotalBytes, r.CopySeconds));
        Stat(h, r.Verified ? Duration(r.VerifySeconds) : "—", "ověření · " + (r.Verified ? Format.Rate(r.TotalBytes, r.VerifySeconds) : "neprovedeno"));
        if (r.PausedSeconds > 0) Stat(h, Duration(r.PausedSeconds), "pozastaveno · mimo rychlosti");
        Stat(h, Esc(string.Join(" + ", r.Algorithms.Select(a => a.ToUpperInvariant()))), "kontrolní součet");
        h.Append("</div>\n\n");
```

#### `windows/src/Offload.Core/Models.cs`

Najít (1/2):

```csharp
    internal Action<int>? BeforeFileCopy { get; set; }
    /// Tests only: called on the copy thread after every file is copied and flushed, just before verification.
    internal Action? BeforeVerify { get; set; }
    /// Tests only: creates the source hash of an algorithm ("xxh64", "md5") instead of HashAlgo.Create.
    internal Func<string, Verification.IFileHash>? HashFactory { get; set; }
    /// Tests only: the free bytes of the volume of a path, instead of NativeIO.AvailableBytes.
```

Nahradit:

```csharp
    internal Action<int>? BeforeFileCopy { get; set; }
    /// Tests only: called on the copy thread after every file is copied and flushed, just before verification.
    internal Action? BeforeVerify { get; set; }
    /// Tests only: called on destination `index`'s thread before every block it writes; an exception it throws is a write
    /// error there (a disk pulled out).
    internal Action<int>? BeforeDestWrite { get; set; }
    /// Tests only: creates the source hash of an algorithm ("xxh64", "md5") instead of HashAlgo.Create.
    internal Func<string, Verification.IFileHash>? HashFactory { get; set; }
    /// Tests only: the free bytes of the volume of a path, instead of NativeIO.AvailableBytes.
```

Najít (2/2):

```csharp

public enum CopyPhase { Copy, Flush, Verify, Report, Done }

public sealed record ProgressSnapshot(
    CopyPhase Phase, long TotalBytes, int TotalFiles, int FileIndex, string CurrentFile,
    long ReadBytes, long[] WrittenBytes, long[] VerifiedBytes, int Errors);

public sealed class OffloadError : Exception
{
```

Nahradit:

```csharp

public enum CopyPhase { Copy, Flush, Verify, Report, Done }

/// `Paused`: Pozastavit was pressed (PauseGate); `PauseHeld`: the copy or the read-back really waits now.
public sealed record ProgressSnapshot(
    CopyPhase Phase, long TotalBytes, int TotalFiles, int FileIndex, string CurrentFile,
    long ReadBytes, long[] WrittenBytes, long[] VerifiedBytes, int Errors,
    bool Paused = false, bool PauseHeld = false);

public sealed class OffloadError : Exception
{
```

#### `windows/src/Offload.Core/OffloadEngine.Run.cs`

Najít (1/4):

```csharp
    {
        var files = plan.Scan.Files;
        var started = DateTime.UtcNow;
        var cardPaths = plan.CardPaths;
        foreach (var path in cardPaths)
        {
```

Nahradit:

```csharp
    {
        var files = plan.Scan.Files;
        var started = DateTime.UtcNow;
        var pausedBefore = Pauses.PausedTotal;
        var cardPaths = plan.CardPaths;
        foreach (var path in cardPaths)
        {
```

Najít (2/4):

```csharp
                    continue;
                }
                Options.BeforeFileCopy?.Invoke(i);
                lock (gate)
                {
                    fileIndex = i;
```

Nahradit:

```csharp
                    continue;
                }
                Options.BeforeFileCopy?.Invoke(i);
                if (!Pauses.WaitWhilePaused(cts.Token))
                {
                    sourceErrors[i] = "cancelled";
                    continue;
                }
                lock (gate)
                {
                    fileIndex = i;
```

Najít (3/4):

```csharp
            SetPhase(CopyPhase.Flush);
            OnEachDestination(ws, w => w.FlushDevice());
            var copyFinished = DateTime.UtcNow;

            Options.BeforeVerify?.Invoke();
            if (Options.Verify && !IsCancelled)
            {
                SetPhase(CopyPhase.Verify);
                OnEachDestination(ws, w => w.Verify(files, hashes, plan.Hashes, cts.Token));
            }
            var finished = DateTime.UtcNow;

            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, hashes, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled, started, copyFinished, finished);
            var reportErrors = new List<string>();
            foreach (var path in cardPaths)
            {
```

Nahradit:

```csharp
            SetPhase(CopyPhase.Flush);
            OnEachDestination(ws, w => w.FlushDevice());
            var copyFinished = DateTime.UtcNow;
            var copyPaused = Pauses.PausedTotal - pausedBefore;

            Options.BeforeVerify?.Invoke();
            if (Options.Verify && !IsCancelled)
            {
                SetPhase(CopyPhase.Verify);
                OnEachDestination(ws, w => w.Verify(files, hashes, plan.Hashes, cts.Token, Pauses));
            }
            var finished = DateTime.UtcNow;
            var verifyPaused = Pauses.PausedTotal - pausedBefore - copyPaused;

            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, hashes, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled, started, copyFinished, finished, copyPaused, verifyPaused);
            var reportErrors = new List<string>();
            foreach (var path in cardPaths)
            {
```

Najít (4/4):

```csharp
        long offset = 0;
        using (reader)
        {
            while (!IsCancelled)
            {
                var buf = pool.Take();
                int n;
```

Nahradit:

```csharp
        long offset = 0;
        using (reader)
        {
            // A pause waits here, between blocks and before taking a buffer: the writers drain what they hold and idle.
            while (!IsCancelled && Pauses.WaitWhilePaused(cts.Token))
            {
                var buf = pool.Take();
                int n;
```

#### `windows/src/Offload.Core/OffloadEngine.cs`

Najít (1/2):

```csharp

    public bool IsCancelled => cts.IsCancellationRequested;

    /// Scans the card and checks the destinations before a single byte is written. `extra`: what proxies and the report
    /// will need besides the card (spec 2.5), worked out from the scan; null = the card alone.
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date,
```

Nahradit:

```csharp

    public bool IsCancelled => cts.IsCancellationRequested;

    /// "Pozastavit": the copy and the read-back stop before their next block; the job's stage 2 waits at the same gate.
    public PauseGate Pauses { get; } = new();

    /// Scans the card and checks the destinations before a single byte is written. `extra`: what proxies and the report
    /// will need besides the card (spec 2.5), worked out from the scan; null = the card alone.
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date,
```

Najít (2/2):

```csharp
        lock (gate)
        {
            return new ProgressSnapshot(phase, plan?.Scan.TotalBytes ?? 0, plan?.Scan.Files.Count ?? 0, fileIndex, currentFile,
                Interlocked.Read(ref readBytes), writers.Select(w => w.Written).ToArray(), writers.Select(w => w.Verified).ToArray(), errorCount);
        }
    }
```

Nahradit:

```csharp
        lock (gate)
        {
            return new ProgressSnapshot(phase, plan?.Scan.TotalBytes ?? 0, plan?.Scan.Files.Count ?? 0, fileIndex, currentFile,
                Interlocked.Read(ref readBytes), writers.Select(w => w.Written).ToArray(), writers.Select(w => w.Verified).ToArray(), errorCount,
                Pauses.IsPaused, Pauses.IsHolding);
        }
    }
```

#### `windows/src/Offload.Core/OffloadReport.cs`

Najít (1/4):

```csharp
    [JsonPropertyName("generation")] public int? Generation { get; set; }
    /// The project the card was copied in (spec 4.1); absent for "Bez projektu" and in older reports. The Mac ignores it.
    [JsonPropertyName("project")] public string? Project { get; set; }

    /// The hashes of this copy: `hashAlgorithms`, else the one `hashAlgorithm` (reports without the key, the Mac).
    [JsonIgnore] public IReadOnlyList<string> Algorithms => HashAlgorithms is { Count: > 0 } all ? all : [HashAlgorithm];

    [JsonIgnore] public double CopySeconds => (CopyFinished - Started).TotalSeconds;
    [JsonIgnore] public double VerifySeconds => (Finished - CopyFinished).TotalSeconds;

    [JsonIgnore]
    public IReadOnlyList<ReportFile> FailedFiles => Files.Where(IsFailed).ToList();
```

Nahradit:

```csharp
    [JsonPropertyName("generation")] public int? Generation { get; set; }
    /// The project the card was copied in (spec 4.1); absent for "Bez projektu" and in older reports. The Mac ignores it.
    [JsonPropertyName("project")] public string? Project { get; set; }
    /// Seconds the copy / the read-back stood paused (Pozastavit, Windows from 0.5.0); absent when never paused. The speeds
    /// leave the pause out; the Mac ignores the keys (its speeds then include it).
    [JsonPropertyName("copyPausedSeconds")] public double? CopyPausedSeconds { get; set; }
    [JsonPropertyName("verifyPausedSeconds")] public double? VerifyPausedSeconds { get; set; }

    /// The hashes of this copy: `hashAlgorithms`, else the one `hashAlgorithm` (reports without the key, the Mac).
    [JsonIgnore] public IReadOnlyList<string> Algorithms => HashAlgorithms is { Count: > 0 } all ? all : [HashAlgorithm];

    [JsonIgnore] public double CopySeconds => Math.Max(0, (CopyFinished - Started).TotalSeconds - (CopyPausedSeconds ?? 0));
    [JsonIgnore] public double VerifySeconds => Math.Max(0, (Finished - CopyFinished).TotalSeconds - (VerifyPausedSeconds ?? 0));
    [JsonIgnore] public double PausedSeconds => (CopyPausedSeconds ?? 0) + (VerifyPausedSeconds ?? 0);

    [JsonIgnore]
    public IReadOnlyList<ReportFile> FailedFiles => Files.Where(IsFailed).ToList();
```

Najít (2/4):

```csharp

    public static OffloadReport Build(OffloadPlan plan, IReadOnlyList<FileHashes> hashes, IReadOnlyList<string?> sourceErrors,
        IReadOnlyList<DestFileResult[]> destResults, IReadOnlyList<bool> unbuffered, bool verified,
        DateTime started, DateTime copyFinished, DateTime finished)
    {
        bool IsOk(FileState s) => s == FileState.Verified || (!verified && s == FileState.Written);
        var files = plan.Scan.Files.Select((f, i) => new ReportFile
        {
```

Nahradit:

```csharp

    public static OffloadReport Build(OffloadPlan plan, IReadOnlyList<FileHashes> hashes, IReadOnlyList<string?> sourceErrors,
        IReadOnlyList<DestFileResult[]> destResults, IReadOnlyList<bool> unbuffered, bool verified,
        DateTime started, DateTime copyFinished, DateTime finished, TimeSpan copyPaused = default, TimeSpan verifyPaused = default)
    {
        static double? Seconds(TimeSpan t) => t > TimeSpan.Zero ? Math.Round(t.TotalSeconds, 1) : null;
        bool IsOk(FileState s) => s == FileState.Verified || (!verified && s == FileState.Written);
        var files = plan.Scan.Files.Select((f, i) => new ReportFile
        {
```

Najít (3/4):

```csharp
            Skipped = plan.Scan.Skipped,
            Generation = plan.Generation > 1 ? plan.Generation : null,
            Project = plan.Project,
            HashAlgorithms = HashKinds.Normalize(plan.Hashes).HasFlag(HashKind.Md5) ? HashKinds.Names(plan.Hashes) : null,
        };
    }
```

Nahradit:

```csharp
            Skipped = plan.Scan.Skipped,
            Generation = plan.Generation > 1 ? plan.Generation : null,
            Project = plan.Project,
            CopyPausedSeconds = Seconds(copyPaused),
            VerifyPausedSeconds = Seconds(verifyPaused),
            HashAlgorithms = HashKinds.Normalize(plan.Hashes).HasFlag(HashKind.Md5) ? HashKinds.Names(plan.Hashes) : null,
        };
    }
```

Najít (4/4):

```csharp
            s.Append($"Files:       {TotalFiles}  ({Format.Bytes(TotalBytes)})\n");
            s.Append($"Copy:        {CopySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, CopySeconds)}\n");
            s.Append("Verify:      " + (Verified ? $"{VerifySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, VerifySeconds)}" : "NOT VERIFIED") + "\n");
            s.Append($"Hash:        {string.Join("+", Algorithms)}\n");
            foreach (var d in Destinations)
                s.Append($"Destination: {d.CardPath} [{d.Filesystem}]  ok {d.FilesOK}  failed {d.FilesFailed}{(d.Unbuffered == false ? "  (buffered I/O)" : "")}\n");
```

Nahradit:

```csharp
            s.Append($"Files:       {TotalFiles}  ({Format.Bytes(TotalBytes)})\n");
            s.Append($"Copy:        {CopySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, CopySeconds)}\n");
            s.Append("Verify:      " + (Verified ? $"{VerifySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, VerifySeconds)}" : "NOT VERIFIED") + "\n");
            if (PausedSeconds > 0) s.Append($"Paused:      {PausedSeconds.ToString("0.0", inv)} s (not in the speeds)\n");
            s.Append($"Hash:        {string.Join("+", Algorithms)}\n");
            foreach (var d in Destinations)
                s.Append($"Destination: {d.CardPath} [{d.Filesystem}]  ok {d.FilesOK}  failed {d.FilesFailed}{(d.Unbuffered == false ? "  (buffered I/O)" : "")}\n");
```

#### `windows/src/Offload.Core/PauseGate.cs` (nový soubor)

```csharp
namespace Offload.Core;

/// "Pozastavit" (UI návrh, nová věc 9): the job's workers stop at their next safe point — before the next 16 MB block
/// of the card or of a read-back, before the next file, before the next clip of stage 2 — and wait until Resume or
/// cancel. Nothing is ever stopped in the middle of a block, a report, a day report or an ffmpeg run, so no lock
/// (history, day, ASC MHL) is held by a waiting worker; only an append's or a repair's folder lock stays held for the
/// whole of stage 1, paused or not. `PausedTotal` (from Pause to Resume) is what the report subtracts from its times.
public sealed class PauseGate
{
    readonly Lock gate = new();
    readonly ManualResetEventSlim open = new(true);
    readonly Func<DateTime> clock;
    DateTime? pausedAt;
    TimeSpan total;
    int holding;

    /// `clock`: UTC now; tests pass a fake one.
    public PauseGate(Func<DateTime>? clock = null) => this.clock = clock ?? (() => DateTime.UtcNow);

    /// Pause was asked for and Resume not yet.
    public bool IsPaused => !open.IsSet;

    /// A worker is waiting at the gate now — the job has really stopped (until then the UI says "Pozastavuje se…").
    public bool IsHolding => Volatile.Read(ref holding) > 0;

    public void Pause()
    {
        lock (gate)
        {
            if (!open.IsSet) return;
            pausedAt = clock();
            open.Reset();
        }
    }

    public void Resume()
    {
        lock (gate)
        {
            if (open.IsSet) return;
            total += clock() - pausedAt!.Value;
            pausedAt = null;
            open.Set();
        }
    }

    /// Time between Pause and Resume so far (a pause still on counts up to now).
    public TimeSpan PausedTotal
    {
        get
        {
            lock (gate) return total + (pausedAt is DateTime at ? clock() - at : TimeSpan.Zero);
        }
    }

    /// Blocks while paused. False when `ct` is cancelled (also while waiting): the caller stops as on a cancel.
    public bool WaitWhilePaused(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        if (open.IsSet) return true;
        Interlocked.Increment(ref holding);
        try
        {
            open.Wait(ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            Interlocked.Decrement(ref holding);
        }
    }
}
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~PauseGateTests|FullyQualifiedName~EnginePauseTests|FullyQualifiedName~ReportPauseTests"`

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

- Úkol 1 — návrh nového vzhledu `docs/ui-navrh/` je v repu jako závazný podklad (README = zadání, `mockup/*.html` = přesné barvy, rozměry a texty, `screeny/*.png` = náhledy); README doplněno o rozhodnutí uživatele z 2026-10-07 (tmavé i světlé téma podle Windows, systémové písmo, nové věci 1–4, 6, 7, 9 a 10, přepínač projektů místo presetů, „Zkopírovat znovu“ jen nesouhlasné soubory na postižený disk)

## 2026-10-07 — plán 8: nastavení, oznámení, distribuce
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 2 — pozastavení v jádru, 1. fáze (nová věc 9): `PauseGate` (Pozastavit / Pokračovat, čas pozastavení s falešnými hodinami v testech); kopie čeká před každým souborem a před každým 16MB blokem (zapisovače doběhnou a stojí, soubory `.offload-part` zůstanou otevřené), zpětné čtení před každým souborem a blokem (`Hashing.HashFile(…, beforeBlock)`); `ProgressSnapshot.Paused` / `PauseHeld`; zrušení během pauzy skončí hned jako zrušení (rozpracované soubory se smažou); disk odpojený během pauzy = chyba zápisu po Pokračovat jen na tom cíli; report `copyPausedSeconds` / `verifyPausedSeconds` (chybí, když se nepozastavovalo), rychlosti v TXT, HTML a na kartě bez pauzy, TXT řádek `Paused:`, HTML „pozastaveno · mimo rychlosti“
- Úkol 1 — návrh nového vzhledu `docs/ui-navrh/` je v repu jako závazný podklad (README = zadání, `mockup/*.html` = přesné barvy, rozměry a texty, `screeny/*.png` = náhledy); README doplněno o rozhodnutí uživatele z 2026-10-07 (tmavé i světlé téma podle Windows, systémové písmo, nové věci 1–4, 6, 7, 9 a 10, přepínač projektů místo presetů, „Zkopírovat znovu“ jen nesouhlasné soubory na postižený disk)

## 2026-10-07 — plán 8: nastavení, oznámení, distribuce
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: pause and resume in the copy engine (plan 9, task 2)

PauseGate stops the copy before every file and every 16 MB block and the read-back before every
block; the report stores the paused seconds and leaves them out of the speeds. Cancel while paused
ends at once, a disk pulled out while paused fails on that destination only.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\DestWriter.cs windows\src\Offload.Core\Hashing.cs windows\src\Offload.Core\HtmlReport.cs windows\src\Offload.Core\Models.cs windows\src\Offload.Core\OffloadEngine.Run.cs windows\src\Offload.Core\OffloadEngine.cs windows\src\Offload.Core\OffloadReport.cs windows\src\Offload.Core\PauseGate.cs windows\tests\Offload.Core.Tests\EnginePauseTests.cs windows\tests\Offload.Core.Tests\PauseGateTests.cs windows\tests\Offload.Core.Tests\ReportPauseTests.cs windows\tests\Offload.Core.Tests\TestThreads.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 3: Pozastavení celé úlohy — 2. fáze, zámky, časovače karty

Úloha (`OffloadJob`) dostane Pozastavit / Pokračovat. Ve 2. fázi se čeká **mezi klipy** analýzy a proxy a před
PDF a denním reportem — žádný proces se neuspává, takže hlídač zaseknutí ffmpeg ani limit Edge pauzu nevidí. Zámek dne
a historie se během pauzy nedrží, zámek složek doplnění ano (Review Focus 2 a 4). `JobProgress` během pauzy ukazuje
nulové rychlosti, žádný odhad ani úzké hrdlo a po Pokračovat měří od obnovení.

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.Core/Desktop/JobProgress.cs`
- Modify: `windows/src/Offload.Core/Hardware/BrakeMeter.cs`
- Modify: `windows/src/Offload.Core/Models.cs`
- Modify: `windows/src/Offload.Core/OffloadEngine.cs`
- Modify: `windows/src/Offload.Core/OffloadJob.cs`
- Test: `windows/tests/Offload.Core.Tests/JobPauseTests.cs` (nový)
- Test: `windows/tests/Offload.Core.Tests/JobProgressPauseTests.cs` (nový)

**Interfaces:**
- Consumes (úkol 2): `OffloadEngine.Pauses`, `PauseGate.WaitWhilePaused`, `ProgressSnapshot.Paused` / `PauseHeld`.
- Produces:
  - `OffloadEngine.WaitWhilePaused()` (`bool`; s tokenem enginu).
  - `JobStatus.Paused`, `PauseHeld` (`bool`), `PausedTotal` (`TimeSpan`) — jen u běžící úlohy.
  - `OffloadJob.Pause()`, `Resume()`, `bool IsPaused`.
  - `OffloadOptions.ProxyStallTimeout` (`internal TimeSpan?`; jen testy).
  - `BrakeMeter.Reset()`.
  - `JobProgress.StatusText` („Pozastaveno“ / „Pozastavuje se“ přednostně), `JobProgress.PauseText` (`string?`: „Pozastavuje se…“, „Pozastaví se po proxy klipu A003…“, „Pozastaví se po klipu A003…“, „Pozastaveno — Pokračovat spustí úlohu od místa, kde stojí.“).

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/JobPauseTests.cs` (nový soubor)

```csharp
using Offload.Core.Append;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Pozastavit on the whole job: stage 2 waits between clips (no tool is ever suspended), no day or history lock is held
/// while paused, an append keeps its folder lock, cancel (closing the app) while paused ends at once.
public class JobPauseTests
{
    static OffloadOptions Options() => new() { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    static bool Until(Func<bool> condition) => SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(30));

    /// Card with three clips; stage 2 without tools (each clip is a quick "ffprobe nenalezen").
    static (OffloadJob Job, string Dest) ThreeClips(TempDir dir, Action<OffloadJob>? configure = null)
    {
        var card = dir.Sub("A001");
        for (int i = 1; i <= 3; i++) TestFs.WriteRandom(Path.Combine(card, "CLIP", $"A001C00{i}.mov"), 20_000, i);
        var dest = dir.Sub("d1");
        var job = new OffloadJob(new JobConfig(card, [dest])
        {
            Date = "2026-10-07", Tools = new ExternalTools { NoSearch = true }, HistoryFolder = dir.Sub("history"),
            Outputs = new OutputSettings { DayReport = true },
        }, Options());
        configure?.Invoke(job);
        return (job, dest);
    }

    [Fact]
    public async Task Stage2_PausedBeforeTheNextClip_ResumeFinishes()
    {
        using var dir = new TempDir();
        var (job, _) = ThreeClips(dir);
        Assert.True(job.RunCopy());
        job.Pause();
        Assert.False(job.Status().PauseHeld);   // asked, the post thread has not reached the gate yet
        var post = TestThreads.Start(() => job.RunPost());

        Assert.True(Until(() => job.Status().PauseHeld));
        var held = job.Status();
        Assert.Equal((JobPhase.Analyzing, 0, 3), (held.Phase, held.ClipIndex, held.ClipCount));
        Assert.True(held.Paused);
        Thread.Sleep(300);
        Assert.Equal(JobPhase.Analyzing, job.Status().Phase);
        Assert.False(post.IsCompleted);
        job.Resume();
        await post.WaitAsync(TimeSpan.FromSeconds(30));

        var done = job.Status();
        Assert.Equal(JobPhase.Done, done.Phase);
        Assert.False(done.Paused);
        Assert.True(done.PausedTotal >= TimeSpan.FromMilliseconds(300));
        Assert.Equal(3, job.Report!.Clips.Count);
    }

    // The day report and history locks are taken only for a moment and never while waiting: another job of the same
    // day makes its day report while this one stands paused.
    [Fact]
    public async Task PausedInStage2_HoldsNoDayLock()
    {
        using var dir = new TempDir();
        var (job, dest) = ThreeClips(dir);
        Assert.True(job.RunCopy());
        job.Pause();
        var post = TestThreads.Start(() => job.RunPost());
        Assert.True(Until(() => job.Status().PauseHeld));

        var html = DayReportService.Regenerate(dest, "2026-10-07", new OutputSettings(), wait: TimeSpan.FromSeconds(2));

        Assert.True(File.Exists(html));
        job.Resume();
        await post.WaitAsync(TimeSpan.FromSeconds(30));
    }

    // Closing the app cancels every job: a paused one leaves the gate at once, writes its PDF and day report as a
    // cancelled job does and ends Zrušeno.
    [Fact]
    public async Task CancelWhilePausedInStage2_EndsCancelledPromptly()
    {
        using var dir = new TempDir();
        var (job, _) = ThreeClips(dir);
        Assert.True(job.RunCopy());
        job.Pause();
        var post = TestThreads.Start(() => job.RunPost());
        Assert.True(Until(() => job.Status().PauseHeld));

        job.Cancel();

        await post.WaitAsync(TimeSpan.FromSeconds(15));
        var s = job.Status();
        Assert.Equal(JobPhase.Cancelled, s.Phase);
        Assert.False(s.Paused);
        Assert.True(s.SafeToEject);   // the copy itself was complete and verified before the pause
    }

    // An append keeps its card folders locked while paused (they are half-written); after Pokračovat it finishes and
    // the lock is free again.
    [Fact]
    public async Task PausedAppend_KeepsTheFolderLock()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        var options = Options();
        OffloadJob? job = null;
        options.BeforeFileCopy = _ => job!.Pause();
        job = new OffloadJob(new JobConfig(card, []) { AppendInto = paths, AnalyzeClips = false, HistoryFolder = dir.Sub("history") }, options);

        var copy = TestThreads.Start(() => job.RunCopy());
        Assert.True(Until(() => job.Status().PauseHeld));
        var busy = Assert.Throws<OffloadError>(() => AppendLock.Acquire(paths));
        Assert.Equal(PreflightCode.AppendBusy, busy.Code);
        options.BeforeFileCopy = null;
        job.Resume();
        await copy.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        AppendLock.Acquire(paths).Dispose();
    }

    [Fact]
    public void PauseOnAWaitingOrFinishedJob_ShowsNothing()
    {
        using var dir = new TempDir();
        var (job, _) = ThreeClips(dir);
        job.Pause();
        Assert.False(job.Status().Paused);   // waiting: the card shows no pause
        job.Resume();
        job.Run();
        job.Pause();
        Assert.False(job.Status().Paused);   // finished
    }

    // No ffmpeg is suspended: a clip being rendered finishes, the job stands before the next one, and the stall watchdog
    // (here 2 s) never fires however long the pause lasts.
    [FfmpegFact]
    public async Task ProxyRendering_FinishesTheClip_ThenStands_WatchdogNeverFires()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "A001C001.mov"), seconds: 4);
        TestMedia.MakeClip(Path.Combine(card, "A001C002.mov"), seconds: 4);
        var options = Options();
        options.ProxyEncoder = "libx264";
        options.ProxyStallTimeout = TimeSpan.FromSeconds(2);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")])
        {
            Date = "2026-10-07", HistoryFolder = dir.Sub("history"), Proxy = new ProxySettings { Enabled = true, Height = 360 },
        }, options);
        Assert.True(job.RunCopy());
        var post = TestThreads.Start(() => job.RunPost());
        Assert.True(Until(() => job.Status() is { Phase: JobPhase.Proxies, ClipIndex: 0 }));
        job.Pause();

        Assert.True(Until(() => job.Status().PauseHeld));
        var held = job.Status();
        Assert.Equal(1, held.ProxiesDone);       // the clip being rendered finished
        Assert.Equal(0, held.ClipIndex);         // and the second one has not started
        Thread.Sleep(3000);                      // longer than the watchdog's limit
        job.Resume();
        await post.WaitAsync(TimeSpan.FromSeconds(120));

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.All(job.Report!.Clips, c => Assert.Equal(ProxyStatus.Done, c.ProxyStatus));
        Assert.All(job.Report.Clips, c => Assert.Null(c.ProxyNote));
    }
}
```

#### `windows/tests/Offload.Core.Tests/JobProgressPauseTests.cs` (nový soubor)

```csharp
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

/// The card's timers while paused: no rate, no ETA, no brake; after Pokračovat everything is measured from the resume.
public class JobProgressPauseTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    static JobStatus Copy(long read, bool paused = false, bool held = false) => new()
    {
        Phase = JobPhase.Copying, Paused = paused, PauseHeld = held,
        Copy = new ProgressSnapshot(CopyPhase.Copy, 1_000_000_000, 10, 1, "CLIP/A001C002.mov", read, [read, read], [0, 0], 0, paused, held),
    };

    [Fact]
    public void Paused_NoRateNoEta_ResumeMeasuresFromTheResume()
    {
        var p = new JobProgress(T0);
        p.Update(Copy(0), T0.AddSeconds(0.5));
        p.Update(Copy(100_000_000), T0.AddSeconds(1.5));
        Assert.Equal(100e6, p.ReadRate, 1);

        for (int i = 0; i < 60; i++) p.Update(Copy(100_000_000, paused: true, held: true), T0.AddSeconds(2 + i));
        Assert.Equal(0, p.ReadRate);
        Assert.Equal([0.0, 0.0], p.DestRates);
        Assert.Null(p.Eta);
        Assert.Equal("Pozastaveno", p.StatusText);

        p.Update(Copy(150_000_000), T0.AddSeconds(61.5));   // half a second after the resume
        Assert.Equal(100e6, p.ReadRate, 1);                 // not 50 MB over the minute of the pause
        Assert.Equal(850e6 / 100e6, p.Eta!.Value, 3);
        Assert.Equal("Kopírování", p.StatusText);
    }

    [Fact]
    public void PauseTexts_ByPhase()
    {
        var p = new JobProgress(T0);
        p.Update(Copy(1, paused: true), T0);
        Assert.Equal(("Pozastavuje se", "Pozastavuje se…"), (p.StatusText, p.PauseText));
        p.Update(new JobStatus { Phase = JobPhase.Proxies, Paused = true, ClipName = "A001C003", ClipCount = 5, ClipIndex = 2 }, T0);
        Assert.Equal("Pozastaví se po proxy klipu A001C003…", p.PauseText);
        p.Update(new JobStatus { Phase = JobPhase.Analyzing, Paused = true, ClipName = "A001C003", ClipCount = 5, ClipIndex = 2 }, T0);
        Assert.Equal("Pozastaví se po klipu A001C003…", p.PauseText);
        p.Update(new JobStatus { Phase = JobPhase.Analyzing, Paused = true, PauseHeld = true }, T0);
        Assert.Equal(("Pozastaveno", "Pozastaveno — Pokračovat spustí úlohu od místa, kde stojí."), (p.StatusText, p.PauseText));
        p.Update(new JobStatus { Phase = JobPhase.Analyzing }, T0);
        Assert.Null(p.PauseText);
    }

    // The brake meter must not see the pause: its window is forgotten and it answers again only after half a window.
    [Fact]
    public void Brake_ForgottenWhilePaused()
    {
        var meter = new BrakeMeter(16L << 20, 16);
        for (int i = 0; i <= 4; i++) meter.Add(i, i * 400_000_000L, [i * 400_000_000L]);
        Assert.NotNull(meter.Add(5, 2_000_000_000L, [2_000_000_000L]));
        meter.Reset();
        Assert.Null(meter.Add(65, 2_000_000_000L, [2_000_000_000L]));
        Assert.Null(meter.Add(66, 2_400_000_000L, [2_400_000_000L]));
        Assert.NotNull(meter.Add(67, 2_800_000_000L, [2_800_000_000L]));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~JobPauseTests|FullyQualifiedName~JobProgressPauseTests"`

Expected: FAIL — build selže — `CS1061: OffloadJob neobsahuje definici pro Pause` (a `PauseText`, `ProxyStallTimeout`, `JobStatus.Paused`).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/Desktop/JobProgress.cs`

Najít (1/2):

```csharp

    public void Update(JobStatus s, DateTime now)
    {
        double dt = (now - lastTime).TotalSeconds;
        if (s.Copy is { } c && dt > 0.2)
        {
```

Nahradit:

```csharp

    public void Update(JobStatus s, DateTime now)
    {
        if (s.Paused)
        {
            // Nothing moves: no rate and no ETA, the brake forgets its window; after Pokračovat the rates are measured
            // from the moment the job runs again, never over the pause.
            ReadRate = 0;
            DestRates = new double[DestRates.Length];
            Eta = null;
            Brake = null;
            brakes?.Reset();
            if (s.Copy is { } held)
            {
                bool copying = s.Phase == JobPhase.Copying;
                lastBytes = copying ? held.ReadBytes : (held.VerifiedBytes.Length > 0 ? held.VerifiedBytes.Min() : 0);
                lastDest = (copying ? held.WrittenBytes : held.VerifiedBytes).ToArray();
            }
            lastPhase = s.Phase;
            lastTime = now;
            Status = s;
            return;
        }
        double dt = (now - lastTime).TotalSeconds;
        if (s.Copy is { } c && dt > 0.2)
        {
```

Najít (2/2):

```csharp
        _ => 0,
    };

    public string StatusText => Status.Phase == JobPhase.Done && Status.CopySucceeded == false ? "Chyba" : Status.Phase.Label();

    /// Failed, or copied with errors — a job the user cancelled is not an error (no error sound or flash).
    public bool IsError => Tone == CardTone.Error;
```

Nahradit:

```csharp
        _ => 0,
    };

    public string StatusText => PauseLabel ?? (Status.Phase == JobPhase.Done && Status.CopySucceeded == false ? "Chyba" : Status.Phase.Label());

    /// "Pozastaveno" once the job stands, "Pozastavuje se" until then; null while it runs.
    string? PauseLabel => !Status.Paused ? null : Status.PauseHeld ? "Pozastaveno" : "Pozastavuje se";

    /// What the card says while paused, or null: stage 2 finishes the clip it is on first.
    public string? PauseText => Status switch
    {
        { Paused: false } => null,
        { PauseHeld: true } => "Pozastaveno — Pokračovat spustí úlohu od místa, kde stojí.",
        { Phase: JobPhase.Proxies, ClipName: { Length: > 0 } clip } => $"Pozastaví se po proxy klipu {clip}…",
        { Phase: JobPhase.Analyzing, ClipName: { Length: > 0 } clip } => $"Pozastaví se po klipu {clip}…",
        _ => "Pozastavuje se…",
    };

    /// Failed, or copied with errors — a job the user cancelled is not an error (no error sound or flash).
    public bool IsError => Tone == CardTone.Error;
```

#### `windows/src/Offload.Core/Hardware/BrakeMeter.cs`

Najít (1/1):

```csharp
        this.linkMBps = linkMBps ?? [];
    }

    /// Feed one progress snapshot (seconds from any fixed start). Returns the brake once the window holds
    /// enough history (half the window), else null.
    public Brake? Add(double seconds, long readBytes, IReadOnlyList<long> writtenBytes)
```

Nahradit:

```csharp
        this.linkMBps = linkMBps ?? [];
    }

    /// Forgets the window (a pause: nothing moved meanwhile, the backlog says nothing about the disks).
    public void Reset() => samples.Clear();

    /// Feed one progress snapshot (seconds from any fixed start). Returns the brake once the window holds
    /// enough history (half the window), else null.
    public Brake? Add(double seconds, long readBytes, IReadOnlyList<long> writtenBytes)
```

#### `windows/src/Offload.Core/Models.cs`

Najít (1/1):

```csharp
    /// Tests only: H.264/HEVC proxies with this encoder (e.g. libx264) instead of the probed one — parallel
    /// NVENC sessions from concurrently running tests can collide.
    internal string? ProxyEncoder { get; set; }
}

public enum FileState { Pending, Written, Verified, Failed }
```

Nahradit:

```csharp
    /// Tests only: H.264/HEVC proxies with this encoder (e.g. libx264) instead of the probed one — parallel
    /// NVENC sessions from concurrently running tests can collide.
    internal string? ProxyEncoder { get; set; }
    /// Tests only: the proxy stall watchdog's limit instead of ProxyRenderer.DefaultStallTimeout.
    internal TimeSpan? ProxyStallTimeout { get; set; }
}

public enum FileState { Pending, Written, Verified, Failed }
```

#### `windows/src/Offload.Core/OffloadEngine.cs`

Najít (1/1):

```csharp
    /// "Pozastavit": the copy and the read-back stop before their next block; the job's stage 2 waits at the same gate.
    public PauseGate Pauses { get; } = new();

    /// Scans the card and checks the destinations before a single byte is written. `extra`: what proxies and the report
    /// will need besides the card (spec 2.5), worked out from the scan; null = the card alone.
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date,
```

Nahradit:

```csharp
    /// "Pozastavit": the copy and the read-back stop before their next block; the job's stage 2 waits at the same gate.
    public PauseGate Pauses { get; } = new();

    /// Waits while paused; false when the job was cancelled meanwhile (stage 2 uses it before every clip).
    public bool WaitWhilePaused() => Pauses.WaitWhilePaused(cts.Token);

    /// Scans the card and checks the destinations before a single byte is written. `extra`: what proxies and the report
    /// will need besides the card (spec 2.5), worked out from the scan; null = the card alone.
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date,
```

#### `windows/src/Offload.Core/OffloadJob.cs`

Najít (1/7):

```csharp
    public string? Error { get; init; }
    /// Information for the job card, e.g. "Nic nového — záloha je úplná."
    public string? Note { get; init; }
    /// Warnings that do not fail the job (orange), one per line — e.g. unverified files of the earlier backup.
    public string? Warning { get; init; }
```

Nahradit:

```csharp
    public string? Error { get; init; }
    /// Information for the job card, e.g. "Nic nového — záloha je úplná."
    public string? Note { get; init; }
    /// Pozastavit was pressed and not resumed (only while the job runs): it stops at its next safe point.
    public bool Paused { get; init; }
    /// The job really stands now — a worker waits at the gate ("Pozastaveno"; before that "Pozastavuje se…").
    public bool PauseHeld { get; init; }
    /// How long the job has stood paused so far (from Pozastavit to Pokračovat).
    public TimeSpan PausedTotal { get; init; }
    /// Warnings that do not fail the job (orange), one per line — e.g. unverified files of the earlier backup.
    public string? Warning { get; init; }
```

Najít (2/7):

```csharp

    public bool IsCancelled => Engine.IsCancelled;

    public JobStatus Status()
    {
        JobStatus s;
```

Nahradit:

```csharp

    public bool IsCancelled => Engine.IsCancelled;

    /// "Pozastavit" (UI návrh 9): stage 1 stops before its next block of the card or of the read-back, stage 2 before
    /// its next clip and before the PDF and the day report — never in the middle of a block, a clip, a report or a lock
    /// it would hold (an append's or a repair's folder lock stays held through stage 1). Cancel while paused ends the
    /// job as a cancel does.
    public void Pause() => Engine.Pauses.Pause();

    public void Resume() => Engine.Pauses.Resume();

    public bool IsPaused => Engine.Pauses.IsPaused;

    public JobStatus Status()
    {
        JobStatus s;
```

Najít (3/7):

```csharp
        }
        // Cancelled while still queued: it never runs, so say so instead of "Čeká" forever.
        if (s.Phase == JobPhase.Waiting && !wasStarted && IsCancelled) s = s with { Phase = JobPhase.Cancelled };
        return s;
    }

    void Update(Func<JobStatus, JobStatus> change)
```

Nahradit:

```csharp
        }
        // Cancelled while still queued: it never runs, so say so instead of "Čeká" forever.
        if (s.Phase == JobPhase.Waiting && !wasStarted && IsCancelled) s = s with { Phase = JobPhase.Cancelled };
        var pauses = Engine.Pauses;
        bool running = s.Phase != JobPhase.Waiting && !s.Phase.IsFinished();
        return s with { Paused = running && pauses.IsPaused, PauseHeld = running && pauses.IsHolding, PausedTotal = pauses.PausedTotal };
    }

    void Update(Func<JobStatus, JobStatus> change)
```

Najít (4/7):

```csharp
        {
            var final = AnalyzeAndRender(plan, report, log);
            Report = final;
            Update(s => s with { Phase = JobPhase.Reporting });
            WritePdfReports(final, plan, log);
            RegenerateDayReports(plan, log);
```

Nahradit:

```csharp
        {
            var final = AnalyzeAndRender(plan, report, log);
            Report = final;
            Engine.WaitWhilePaused();   // before the PDF and the day report (a cancel meanwhile still writes them)
            Update(s => s with { Phase = JobPhase.Reporting });
            WritePdfReports(final, plan, log);
            RegenerateDayReports(plan, log);
```

Najít (5/7):

```csharp
        using var renderer = new ProxyRenderer(Config.Proxy, Config.Tools.Resolved())
        {
            ForcedEncoder = Engine.Options.ProxyEncoder,
            Log = log,
            ReplaceExisting = plan.Mode == PlanMode.Full,   // an append never overwrites a proxy (spec 2.2)
        };
```

Nahradit:

```csharp
        using var renderer = new ProxyRenderer(Config.Proxy, Config.Tools.Resolved())
        {
            ForcedEncoder = Engine.Options.ProxyEncoder,
            StallTimeout = Engine.Options.ProxyStallTimeout ?? ProxyRenderer.DefaultStallTimeout,
            Log = log,
            ReplaceExisting = plan.Mode == PlanMode.Full,   // an append never overwrites a proxy (spec 2.2)
        };
```

Najít (6/7):

```csharp
        Update(s => s with { Phase = JobPhase.Analyzing, ClipCount = entries.Count });
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsCancelled) break;
            var clip = entries[i].Clip;
            int index = i;
            Update(s => s with { ClipIndex = index, ClipName = clip.Name, ClipFraction = 0 });
```

Nahradit:

```csharp
        Update(s => s with { Phase = JobPhase.Analyzing, ClipCount = entries.Count });
        for (int i = 0; i < entries.Count; i++)
        {
            // A pause waits between clips: the clip being probed or rendered finishes first (no ffmpeg is suspended, so
            // its stall watchdog never sees the pause).
            if (IsCancelled || !Engine.WaitWhilePaused()) break;
            var clip = entries[i].Clip;
            int index = i;
            Update(s => s with { ClipIndex = index, ClipName = clip.Name, ClipFraction = 0 });
```

Najít (7/7):

```csharp
        if (plan.Mode == PlanMode.Append) usedNames.UnionWith(ExistingProxyNames(cardPaths));
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsCancelled) break;
            var clip = entries[i].Clip;  // may have been reclassified during analysis (MXF → ARRIRAW)
            int index = i;
            Update(s => s with { ClipIndex = index, ClipName = clip.Name, ClipFraction = 0 });
```

Nahradit:

```csharp
        if (plan.Mode == PlanMode.Append) usedNames.UnionWith(ExistingProxyNames(cardPaths));
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsCancelled || !Engine.WaitWhilePaused()) break;
            var clip = entries[i].Clip;  // may have been reclassified during analysis (MXF → ARRIRAW)
            int index = i;
            Update(s => s with { ClipIndex = index, ClipName = clip.Name, ClipFraction = 0 });
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~JobPauseTests|FullyQualifiedName~JobProgressPauseTests"`

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

- Úkol 2 — pozastavení v jádru, 1. fáze (nová věc 9): `PauseGate` (Pozastavit / Pokračovat, čas pozastavení s falešnými hodinami v testech); kopie čeká před každým souborem a před každým 16MB blokem (zapisovače doběhnou a stojí, soubory `.offload-part` zůstanou otevřené), zpětné čtení před každým souborem a blokem (`Hashing.HashFile(…, beforeBlock)`); `ProgressSnapshot.Paused` / `PauseHeld`; zrušení během pauzy skončí hned jako zrušení (rozpracované soubory se smažou); disk odpojený během pauzy = chyba zápisu po Pokračovat jen na tom cíli; report `copyPausedSeconds` / `verifyPausedSeconds` (chybí, když se nepozastavovalo), rychlosti v TXT, HTML a na kartě bez pauzy, TXT řádek `Paused:`, HTML „pozastaveno · mimo rychlosti“
- Úkol 1 — návrh nového vzhledu `docs/ui-navrh/` je v repu jako závazný podklad (README = zadání, `mockup/*.html` = přesné barvy, rozměry a texty, `screeny/*.png` = náhledy); README doplněno o rozhodnutí uživatele z 2026-10-07 (tmavé i světlé téma podle Windows, systémové písmo, nové věci 1–4, 6, 7, 9 a 10, přepínač projektů místo presetů, „Zkopírovat znovu“ jen nesouhlasné soubory na postižený disk)
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 3 — pozastavení celé úlohy: `OffloadJob.Pause/Resume/IsPaused`, `JobStatus.Paused` / `PauseHeld` / `PausedTotal` (jen u běžící úlohy); 2. fáze se zastaví mezi klipy (rozpracovaný klip analýzy nebo proxy doběhne, ffmpeg se nikdy neuspává, takže hlídač zaseknutí pauzu nevidí — test s limitem 2 s a pauzou 3 s) a před PDF a denním reportem; zámek dne ani historie se během pauzy nedrží (test), zámek složek doplnění zůstává (test); zrušení během pauzy (i zavření aplikace) skončí hned jako Zrušeno; `JobProgress`: během pauzy rychlost 0, bez odhadu a bez „Úzkého hrdla“ (`BrakeMeter.Reset`), po Pokračovat se měří od obnovení, texty „Pozastavuje se“ / „Pozastaví se po proxy klipu A003…“ / „Pozastaveno“; `OffloadOptions.ProxyStallTimeout` (jen testy)
- Úkol 2 — pozastavení v jádru, 1. fáze (nová věc 9): `PauseGate` (Pozastavit / Pokračovat, čas pozastavení s falešnými hodinami v testech); kopie čeká před každým souborem a před každým 16MB blokem (zapisovače doběhnou a stojí, soubory `.offload-part` zůstanou otevřené), zpětné čtení před každým souborem a blokem (`Hashing.HashFile(…, beforeBlock)`); `ProgressSnapshot.Paused` / `PauseHeld`; zrušení během pauzy skončí hned jako zrušení (rozpracované soubory se smažou); disk odpojený během pauzy = chyba zápisu po Pokračovat jen na tom cíli; report `copyPausedSeconds` / `verifyPausedSeconds` (chybí, když se nepozastavovalo), rychlosti v TXT, HTML a na kartě bez pauzy, TXT řádek `Paused:`, HTML „pozastaveno · mimo rychlosti“
- Úkol 1 — návrh nového vzhledu `docs/ui-navrh/` je v repu jako závazný podklad (README = zadání, `mockup/*.html` = přesné barvy, rozměry a texty, `screeny/*.png` = náhledy); README doplněno o rozhodnutí uživatele z 2026-10-07 (tmavé i světlé téma podle Windows, systémové písmo, nové věci 1–4, 6, 7, 9 a 10, přepínač projektů místo presetů, „Zkopírovat znovu“ jen nesouhlasné soubory na postižený disk)
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: pause the whole job between clips, no day lock while paused (plan 9, task 3)

Stage 2 waits between clips and before the PDF and the day report, so no tool is ever suspended
and the ffmpeg watchdog never sees a pause. The day and history locks are never held while paused,
an append keeps its folder lock. JobProgress shows zero speeds, no ETA and no brake while paused.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Desktop\JobProgress.cs windows\src\Offload.Core\Hardware\BrakeMeter.cs windows\src\Offload.Core\Models.cs windows\src\Offload.Core\OffloadEngine.cs windows\src\Offload.Core\OffloadJob.cs windows\tests\Offload.Core.Tests\JobPauseTests.cs windows\tests\Offload.Core.Tests\JobProgressPauseTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 4: Oprava v jádru — „Zkopírovat znovu“ jen nesouhlasné soubory na postižený disk (nová věc 4)

Nový plánovač `RepairPlanner` vezme z reportů na cílech soubory, jejichž nejnovější záznam selhal, a naplánuje je
jen na cíle, kde selhaly. Kontroly dřívější zálohy sdílí s doplněním (`AppendPlanner.CheckBackup`), zápis generace taky
(engine v `PlanMode.Repair`). Dobrá kopie se nikdy nepřepíše — v téže generaci se jen znovu přečte a porovná se
zaznamenaným hashem; nesouhlasná se před novou přesune do `_REPORT\neshoda_NNNN\`. Testy pokrývají Review Focus 3 a 4
(změněná karta, soubor, který z karty zmizel, karta, která se při čtení liší, přerušená oprava).

**Files:**
- Create: `windows/src/Offload.Core/Append/RepairPlanner.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.Core/Append/AppendPlanner.cs`
- Modify: `windows/src/Offload.Core/CardRecord.cs`
- Modify: `windows/src/Offload.Core/DestWriter.cs`
- Modify: `windows/src/Offload.Core/HtmlReport.cs`
- Modify: `windows/src/Offload.Core/Messages.cs`
- Modify: `windows/src/Offload.Core/Mhl.cs`
- Modify: `windows/src/Offload.Core/Models.cs`
- Modify: `windows/src/Offload.Core/OffloadEngine.Run.cs`
- Modify: `windows/src/Offload.Core/OffloadEngine.cs`
- Modify: `windows/src/Offload.Core/OffloadReport.cs`
- Test: `windows/tests/Offload.Core.Tests/RepairEngineTests.cs` (nový)
- Test: `windows/tests/Offload.Core.Tests/RepairPlannerTests.cs` (nový)

**Interfaces:**
- Consumes: `AppendPlanner` (kontroly zálohy), `OffloadEngine.PrepareAppend`-styl zápisu generace, `DestWriter`, `Mhl`, `CardRecord` (stávající); `PauseGate` (úkol 2).
- Produces:
  - `public sealed class RepairPlan` — `Source`, `CardPaths`, `DestRoots`, `Date`, `CardName`, `Generation`, `Files` (`IReadOnlyList<SourceFile>`), `Dirs`, `Write` (`IReadOnlyList<bool[]>`, soubor × cíl), `Expected` (`IReadOnlyList<FileHashes>`), `SetAside` (`IReadOnlyList<string?[]>`), `HashAlgorithms`.
  - `public static class RepairPlanner` — `RepairPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default)`, `string SetAsidePath(int generation, string relPath)` (`_REPORT/neshoda_NNNN/<relPath>`); odmítnutí jako `OffloadError.Preflight(PreflightCode.RepairNothing | RepairConflict, …)`.
  - `AppendPlanner.CheckBackup(IReadOnlyList<string> cardPaths)` → `internal sealed record Backup(List<string> Paths, List<string> Roots, string Date, string CardName, List<CardRecord> Records, int NextGeneration)`; `AppendPlanner.Conflict(…)` internal.
  - `PlanMode.Repair`; `OffloadPlan.Write`, `Expected`, `SetAside`, `bool Writes(int file, int dest)`; `ProgressSnapshot(…, long[]? PlannedBytes = null)` + `long Planned(int d)`.
  - `OffloadEngine.PlanRepair(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default)` → `RepairPlan`; `OffloadEngine.PrepareRepair(RepairPlan r)` → `OffloadPlan`.
  - `DestWriter.Keep(int index)`, `Fail(int index, string reason)`, `Begin(SourceFile file, string? setAside = null, int fileIndex = -1)`, `Index`, `MovedAside`.
  - `OffloadReport.Kind` (`"repair"`, `RepairKind`, `IsRepair`), `SetAside` (`IReadOnlyList<SetAsideCopy>?`), `ReportFile.Rewritten` (`IReadOnlyList<bool>?`, JSON `rewritten`); `public sealed record SetAsideCopy(int Destination, string Path, string MovedTo)`.
  - `Msg.RepairNothing`, `Msg.RepairConflict(IReadOnlyList<string>)`, `Msg.RepairNotOnCard(string)`, `Msg.RepairSetAsideTaken(string, string, string)`, `Msg.MhlRepairComment(int, string)`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/RepairEngineTests.cs` (nový soubor)

```csharp
using System.Text;
using Offload.Core.Append;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// A repair run by the engine: generation 2 on every destination, the bad copy replaced (the old one set aside), the good
/// one never written but read back, the records consistent with plan 6 (reports, ASC MHL, hash list).
public class RepairEngineTests
{
    static (OffloadEngine Engine, OffloadPlan Plan) Prepare(string card, IReadOnlyList<string> paths, OffloadOptions? options = null)
    {
        var engine = new OffloadEngine(options ?? AppendFixtures.Options());
        return (engine, engine.PrepareRepair(engine.PlanRepair(card, paths)));
    }

    [Fact]
    public void Repair_RewritesOnlyTheBadCopy_Generation2Everywhere_RecordsConsistent()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var good = Layout.Join(paths[0], RepairFixtures.Bad);
        var goodBytes = File.ReadAllBytes(good);
        var goodTime = File.GetLastWriteTimeUtc(good);
        var badBytes = File.ReadAllBytes(Layout.Join(paths[1], RepairFixtures.Bad));
        var lists = paths.Select(p => File.ReadAllText(Path.Combine(p, "_REPORT", "A001.xxh64"))).ToList();
        var (engine, plan) = Prepare(card, paths);

        var report = engine.Run(plan);

        Assert.True(report.SafeToEject);
        Assert.Equal((PlanMode.Repair, 2, "repair"), (plan.Mode, report.Generation!.Value, report.Kind));
        var f = Assert.Single(report.Files);
        Assert.Equal(RepairFixtures.Bad, f.Path);
        Assert.Equal(["verified", "verified"], f.Destinations);
        Assert.Equal([false, true], f.Rewritten);
        Assert.Equal([new SetAsideCopy(1, RepairFixtures.Bad, "_REPORT/neshoda_0002/CLIP/A001C002.mov")], report.SetAside);
        // The good copy: never written (same bytes, same time); the bad one: set aside byte for byte, replaced by the card's.
        Assert.Equal(goodBytes, File.ReadAllBytes(good));
        Assert.Equal(goodTime, File.GetLastWriteTimeUtc(good));
        Assert.Equal(badBytes, File.ReadAllBytes(Layout.Join(paths[1], "_REPORT/neshoda_0002/CLIP/A001C002.mov")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(card, "CLIP", "A001C002.mov")), File.ReadAllBytes(Layout.Join(paths[1], RepairFixtures.Bad)));
        for (int d = 0; d < paths.Count; d++)
        {
            Assert.True(File.Exists(Path.Combine(paths[d], "_REPORT", "offload_report_0002.json")));
            Assert.Contains("oprava (generace 2)", File.ReadAllText(Path.Combine(paths[d], "_REPORT", "A001_report_0002.html")));
            Assert.Equal(lists[d], File.ReadAllText(Path.Combine(paths[d], "_REPORT", "A001.xxh64")));   // the line is there once
            var mhl = MhlReader.Read(paths[d])!;
            Assert.Empty(mhl.ChainProblems);
            Assert.Equal([1, 2], mhl.Chain.Select(c => c.Seq));
            var gen2 = Assert.Single(mhl.Generations[1].Files);
            Assert.Equal(d == 0 ? "verified" : "original", Assert.Single(gen2.Hashes).Action);
            Assert.StartsWith("Offload oprava: 1 soubor znovu z ", File.ReadAllText(Directory.GetFiles(Path.Combine(paths[d], "ascmhl"), "0002_*.mhl").Single()).Split("<comment>")[1]);
            Assert.Empty(CardRecord.Load(paths[d]).FailedFiles);
            var verify = BackupVerifier.Verify(paths[d], chunkSize: 4096);
            Assert.True(verify.Success);
            Assert.Empty(verify.Extra);   // the set-aside copy lives in _REPORT
        }
    }

    // The card reads differently now (same size and time, other bytes): the new copy is not trusted — the file stays failed,
    // the good copy elsewhere is still compared with the recorded hash and stays verified.
    [Fact]
    public void CardDiffersWhenRead_FileStaysFailed_GoodCopyStillVerified()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var (engine, plan) = Prepare(card, paths);
        RepairFixtures.Corrupt(Path.Combine(card, "CLIP", "A001C002.mov"));

        var report = engine.Run(plan);

        var f = Assert.Single(report.Files);
        Assert.StartsWith("card differs from the backup: xxh64 ", f.SourceError);
        Assert.Equal(["verified", "failed"], f.Destinations);   // the new copy is of the changed card: never a good record
        Assert.False(report.Success);
        Assert.Equal([RepairFixtures.Bad], CardRecord.Load(paths[0]).FailedFiles);
    }

    [Fact]
    public void Progress_PlannedBytesPerDestination()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var (engine, plan) = Prepare(card, paths);

        engine.Run(plan);

        var s = engine.Snapshot();
        Assert.Equal([0L, 60_000L], s.PlannedBytes!);
        Assert.Equal(0, s.Planned(0));
        Assert.Equal(60_000, s.Planned(1));
        Assert.Equal([0L, 60_000L], s.WrittenBytes);
    }

    // A repair cancelled before it copied anything: the good copy keeps a good record ("written" in a report that was not
    // read back) — it is never rewritten or set aside later; the next repair (generation 3) finishes the job.
    [Fact]
    public void CancelledRepair_GoodCopyStaysGood_NextRepairFinishes()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var good = Layout.Join(paths[0], RepairFixtures.Bad);
        var goodTime = File.GetLastWriteTimeUtc(good);
        var options = AppendFixtures.Options();
        var (engine, plan) = Prepare(card, paths, options);
        options.BeforeFileCopy = _ => engine.Cancel();

        var cancelled = engine.Run(plan);

        var f = Assert.Single(cancelled.Files);
        Assert.Equal(["written", "pending"], f.Destinations);
        Assert.Equal("cancelled", f.SourceError);
        var next = RepairPlanner.Plan(card, paths);
        Assert.Equal(3, next.Generation);
        Assert.Equal([false, true], next.Write[0]);
        Assert.Equal("_REPORT/neshoda_0003/CLIP/A001C002.mov", next.SetAside[0][1]);
        var (engine2, plan2) = Prepare(card, paths);
        Assert.True(engine2.Run(plan2).SafeToEject);
        Assert.Equal(goodTime, File.GetLastWriteTimeUtc(good));
        foreach (var p in paths) Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
    }

    // After a repair the card folder takes an append like any other: generation 3 is the next one.
    [Fact]
    public void AfterRepair_AppendWritesGeneration3()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var (engine, plan) = Prepare(card, paths);
        engine.Run(plan);
        AppendFixtures.NewClip(card);

        var append = AppendPlanner.Plan(card, paths);

        Assert.Equal(3, append.Generation);
        Assert.Equal(["CLIP/A001C003.mov"], append.NewFiles.Select(n => n.RelPath));
    }

    // A generation without clip analysis (the repair) never replaces the clip the full copy analysed.
    [Fact]
    public void CardRecord_RepairDoesNotEraseTheAnalysedClip()
    {
        using var dir = new TempDir();
        var path = dir.Sub("d1", "2026-10-07", "A001");
        var full = Reports.Sample(path);
        full.Clips = [new ClipEntry { Clip = new Clip { Name = "c 02", Path = "CLIP/c 02.mov", Segments = ["CLIP/c 02.mov"] }, Info = new ClipInfo { Camera = "FX6" } }];
        full.Write(path, includeHashRecords: true);
        var repair = Reports.Sample(path);
        repair.Generation = 2;
        repair.Kind = OffloadReport.RepairKind;
        File.WriteAllText(Path.Combine(path, "_REPORT", "offload_report_0002.json"), repair.ToJson(), Encoding.UTF8);

        var record = CardRecord.Load(path);

        var clip = Assert.Single(record.Clips, c => c.Entry.Clip.Path == "CLIP/c 02.mov");
        Assert.Equal("FX6", clip.Entry.Info!.Camera);
        Assert.Equal(1, clip.Generation);
    }
}
```

#### `windows/tests/Offload.Core.Tests/RepairPlannerTests.cs` (nový soubor)

```csharp
using Offload.Core.Append;

namespace Offload.Core.Tests;

public static class RepairFixtures
{
    public const string Bad = "CLIP/A001C002.mov";

    /// Card A001 (two clips) copied onto d1 and d2; on d2 the copy of A001C002.mov is changed before the read-back, so it
    /// fails there with HASH MISMATCH — the case of screen 03 ("1 soubor na disku d2 nesouhlasí s kartou").
    public static (string Card, IReadOnlyList<string> CardPaths, OffloadReport Report) Mismatch(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 60_000, 2);
        var options = AppendFixtures.Options();
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");
        options.BeforeVerify = () => Corrupt(Layout.Join(plan.CardPaths[1], Bad));
        var report = engine.Run(plan);
        return (card, plan.CardPaths, report);
    }

    public static void Corrupt(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var time = File.GetLastWriteTimeUtc(path);
        bytes[100] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, time);
    }
}

/// "Zkopírovat znovu" planning (UI návrh 4): only the failed files, only where they failed; refuse before writing.
public class RepairPlannerTests
{
    static OffloadError Refused(string card, IReadOnlyList<string> paths) =>
        Assert.Throws<OffloadError>(() => RepairPlanner.Plan(card, paths));

    [Fact]
    public void Mismatch_OnlyTheBadDisk_GoodCopyOnlyReadBack_BadCopySetAside()
    {
        using var dir = new TempDir();
        var (card, paths, report) = RepairFixtures.Mismatch(dir);
        Assert.Equal(["verified", "failed"], report.Files.Single(f => f.Path == RepairFixtures.Bad).Destinations);

        var plan = RepairPlanner.Plan(card, paths);

        Assert.Equal([RepairFixtures.Bad], plan.Files.Select(f => f.RelPath));
        Assert.Equal(["CLIP"], plan.Dirs);
        Assert.Equal([false, true], plan.Write[0]);
        Assert.Equal(new string?[] { null, "_REPORT/neshoda_0002/CLIP/A001C002.mov" }, plan.SetAside[0].ToList());
        Assert.Equal(report.Files.Single(f => f.Path == RepairFixtures.Bad).Xxh64, plan.Expected[0].Xxh64Hex);
        Assert.Equal((2, "2026-10-07", "A001"), (plan.Generation, plan.Date, plan.CardName));
        Assert.Equal([dir.Sub("d1"), dir.Sub("d2")], plan.DestRoots);
    }

    [Fact]
    public void EverythingGood_RepairNothing()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        Assert.Equal(PreflightCode.RepairNothing, Refused(card, paths).Code);
    }

    // A write error leaves no file on that disk: it is simply copied, nothing to set aside.
    [Fact]
    public void WriteFailed_NotOnTheDisk_CopiedWithoutSetAside()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        var options = AppendFixtures.Options();
        options.BeforeDestWrite = d => { if (d == 0) throw new IOException("Zařízení není připojeno."); };
        var engine = new OffloadEngine(options);
        var full = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");
        engine.Run(full);

        var plan = RepairPlanner.Plan(card, full.CardPaths);

        Assert.Equal([true, false], plan.Write[0]);
        Assert.All(plan.SetAside[0], Assert.Null);
    }

    // The card could not be hashed at the backup: no destination holds a good copy — both get a new one.
    [Fact]
    public void FailedEverywhere_BothRewritten()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        var paths = CopyWithBothCorrupted(dir, card);

        var plan = RepairPlanner.Plan(card, paths);

        Assert.Equal([true, true], plan.Write[0]);
        Assert.All(plan.SetAside[0], a => Assert.Equal("_REPORT/neshoda_0002/CLIP/A001C001.mov", a));
    }

    static IReadOnlyList<string> CopyWithBothCorrupted(TempDir dir, string card)
    {
        var options = AppendFixtures.Options();
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");
        options.BeforeVerify = () => { foreach (var p in plan.CardPaths) RepairFixtures.Corrupt(Layout.Join(p, "CLIP/A001C001.mov")); };
        engine.Run(plan);
        return plan.CardPaths;
    }

    [Fact]
    public void CardChanged_Refused_NothingWritten()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var trees = paths.Select(AppendFixtures.Tree).ToList();
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 61_000, 9);

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.RepairConflict, e.Code);
        Assert.Contains("CLIP/A001C002.mov: jiná velikost nebo datum", e.Message);
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
    }

    [Fact]
    public void FileGoneFromCard_Refused()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        File.Delete(Path.Combine(card, "CLIP", "A001C002.mov"));

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.RepairConflict, e.Code);
        Assert.Contains("CLIP/A001C002.mov: na kartě už není", e.Message);
    }

    // A good copy that is gone from its disk: something changed the backup — Ověřit zálohu first, as for an append.
    [Fact]
    public void GoodCopyMissing_Refused()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        File.Delete(Layout.Join(paths[0], RepairFixtures.Bad));

        Assert.Equal(PreflightCode.AppendMissingFile, Refused(card, paths).Code);
    }

    [Fact]
    public void SetAsidePlaceTaken_Refused()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        TestFs.WriteRandom(Layout.Join(paths[1], "_REPORT/neshoda_0002/CLIP/A001C002.mov"), 10, 1);

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.RepairConflict, e.Code);
        Assert.Contains("místo pro odložení nesouhlasné kopie", e.Message);
    }

    // The checks of an append's earlier backup apply (one chain per folder, same generations).
    [Fact]
    public void ChainMissing_Refused()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        File.Delete(Path.Combine(paths[1], "ascmhl", "ascmhl_chain.xml"));
        foreach (var f in Directory.GetFiles(Path.Combine(paths[1], "ascmhl"))) File.Delete(f);

        Assert.Equal(PreflightCode.AppendNoMhl, Refused(card, paths).Code);
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~RepairPlannerTests|FullyQualifiedName~RepairEngineTests|FullyQualifiedName~AppendPlannerTests|FullyQualifiedName~AppendEngineTests"`

Expected: FAIL — build selže — `CS0103: Název RepairPlanner v aktuálním kontextu neexistuje` (a `PlanRepair`, `PlanMode.Repair`, `Rewritten`).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/Append/AppendPlanner.cs`

Najít (1/4):

```csharp
    public static AppendPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default) =>
        Plan(source, cardPaths, ct, sourceFilesystem: null);

    /// `sourceFilesystem`: tests pretend a FAT32 card; null = the card's own. `destFilesystem`: the same for a card
    /// folder on a destination (ruling I5); null = NativeIO.FilesystemType.
    internal static AppendPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct, string? sourceFilesystem,
        Func<string, string>? destFilesystem = null)
    {
        if (cardPaths.Count == 0) throw OffloadError.Preflight(PreflightCode.NoDestination, Msg.NoDestination);
        var paths = cardPaths.Select(p => Path.GetFullPath(p).TrimEnd('\\')).ToList();
```

Nahradit:

```csharp
    public static AppendPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default) =>
        Plan(source, cardPaths, ct, sourceFilesystem: null);

    /// The earlier backup as steps 0 and 1 found it (also the start of a repair, RepairPlanner): the card folders in
    /// their order, their roots, date and name, every folder's reports, and the generation the next copy writes.
    internal sealed record Backup(List<string> Paths, List<string> Roots, string Date, string CardName, List<CardRecord> Records, int NextGeneration);

    /// Steps 0 and 1: one backup `<root>\<date>\<card>` with the same date and name everywhere, every report readable,
    /// every ASC MHL chain intact and of the same length, and the numbered reports in agreement with it. Reads only.
    internal static Backup CheckBackup(IReadOnlyList<string> cardPaths)
    {
        if (cardPaths.Count == 0) throw OffloadError.Preflight(PreflightCode.NoDestination, Msg.NoDestination);
        var paths = cardPaths.Select(p => Path.GetFullPath(p).TrimEnd('\\')).ToList();
```

Najít (2/4):

```csharp
        for (int d = 0; d < paths.Count; d++)
            if (records[d].HighestReportNumber != generations[d])
                throw OffloadError.Preflight(PreflightCode.AppendDiverged, Msg.AppendReportsMismatch(paths[d], records[d].HighestReportNumber, generations[d]));

        // 2. The card, compared with every backup: a known file must be the same on the card, in the report and on disk.
        var scan = Scanner.Scan(source, ct);
```

Nahradit:

```csharp
        for (int d = 0; d < paths.Count; d++)
            if (records[d].HighestReportNumber != generations[d])
                throw OffloadError.Preflight(PreflightCode.AppendDiverged, Msg.AppendReportsMismatch(paths[d], records[d].HighestReportNumber, generations[d]));
        // Ruling I1: max of both (equal here, the check above refused otherwise).
        return new Backup(paths, roots, date, cardName, records, Math.Max(generations[0], records[0].HighestReportNumber) + 1);
    }

    /// `sourceFilesystem`: tests pretend a FAT32 card; null = the card's own. `destFilesystem`: the same for a card
    /// folder on a destination (ruling I5); null = NativeIO.FilesystemType.
    internal static AppendPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct, string? sourceFilesystem,
        Func<string, string>? destFilesystem = null)
    {
        var (paths, roots, date, cardName, records, generation) = CheckBackup(cardPaths);

        // 2. The card, compared with every backup: a known file must be the same on the card, in the report and on disk.
        var scan = Scanner.Scan(source, ct);
```

Najít (3/4):

```csharp
            DestRoots = roots,
            Date = date,
            CardName = cardName,
            // Ruling I1: max of both (equal here, the check above refused otherwise).
            Generation = Math.Max(generations[0], records[0].HighestReportNumber) + 1,
            FullScan = scan,
            NewFiles = newFiles,
            NewDirs = newDirs,
```

Nahradit:

```csharp
            DestRoots = roots,
            Date = date,
            CardName = cardName,
            Generation = generation,
            FullScan = scan,
            NewFiles = newFiles,
            NewDirs = newDirs,
```

Najít (4/4):

```csharp
    }

    /// "CLIP/A001C003.mov (karta 1,2 GB · 6. 10. 9:14, D: 1,1 GB · 6. 10. 9:10)"
    static string Conflict(SourceFile card, string disk, long size, DateTime modifiedUtc)
    {
        // A difference smaller than the rounding ("100 KB" on both sides) shows the exact bytes instead.
        var (a, b) = Format.BytesCs(card.Size) == Format.BytesCs(size) && card.Size != size ? ($"{card.Size} B", $"{size} B") : (Format.BytesCs(card.Size), Format.BytesCs(size));
```

Nahradit:

```csharp
    }

    /// "CLIP/A001C003.mov (karta 1,2 GB · 6. 10. 9:14, D: 1,1 GB · 6. 10. 9:10)"
    internal static string Conflict(SourceFile card, string disk, long size, DateTime modifiedUtc)
    {
        // A difference smaller than the rounding ("100 KB" on both sides) shows the exact bytes instead.
        var (a, b) = Format.BytesCs(card.Size) == Format.BytesCs(size) && card.Size != size ? ($"{card.Size} B", $"{size} B") : (Format.BytesCs(card.Size), Format.BytesCs(size));
```

#### `windows/src/Offload.Core/Append/RepairPlanner.cs` (nový soubor)

```csharp
using Offload.Core.History;

namespace Offload.Core.Append;

/// What a repair copies ("Zkopírovat znovu", UI návrh 4): the files whose newest record failed, each only onto the
/// destinations where it failed, as ASC MHL generation `Generation` in the same card folders. A destination that holds
/// a good copy of such a file gets nothing written — its copy is only read back in the same generation, so every card
/// folder keeps the same generations (R7).
public sealed class RepairPlan
{
    public required string Source { get; init; }
    public required IReadOnlyList<string> CardPaths { get; init; }
    public required IReadOnlyList<string> DestRoots { get; init; }
    public required string Date { get; init; }
    public required string CardName { get; init; }
    public required int Generation { get; init; }
    /// The files to repair, as the card has them now, in the card's order.
    public required IReadOnlyList<SourceFile> Files { get; init; }
    public required IReadOnlyList<string> Dirs { get; init; }
    /// Per file, per destination: true = a new copy is written there.
    public required IReadOnlyList<bool[]> Write { get; init; }
    /// Per file: the hashes the backup recorded from the card (a source read failure recorded none).
    public required IReadOnlyList<FileHashes> Expected { get; init; }
    /// Per file, per destination: where the copy that did not match goes first (relative to the card folder), or null.
    public required IReadOnlyList<string?[]> SetAside { get; init; }
    /// The hash types of the earlier backup — a repair uses them, like an append (spec 3.5).
    public IReadOnlyList<string> HashAlgorithms { get; init; } = ["xxh64"];
}

/// Pure decisions over the card and the card folders, like AppendPlanner (R4: the truth is what the destinations hold).
/// Reads only — never the card is written, never a destination. Refuses (OffloadError.Preflight) before anything is
/// written when the repair would not reproduce what the backup recorded. The rules, against plan 6:
/// - the earlier backup must pass AppendPlanner's steps 0 and 1 (folders, reports, chains, generations);
/// - only a file whose newest record failed is touched, and only on a destination where it failed; a good copy is
///   never written (it is read back and compared with the recorded hash);
/// - the card file must still be what the backup recorded (size and time; the hash is compared while copying);
/// - a failed copy that is on the disk is moved to `_REPORT\neshoda_NNNN\<path>` before the new one — never deleted,
///   never overwritten (the move refuses an existing target);
/// - a good copy that is missing from its disk stops the repair (Ověřit zálohu first), as it stops an append.
public static class RepairPlanner
{
    /// `_REPORT/neshoda_0003/<path>`: where generation 3 moves the copies that did not match.
    public static string SetAsidePath(int generation, string relPath) => $"{Layout.ReportDirName}/neshoda_{generation:0000}/{relPath}";

    public static RepairPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default) =>
        Plan(source, cardPaths, ct, sourceFilesystem: null);

    internal static RepairPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct, string? sourceFilesystem)
    {
        var (paths, roots, date, cardName, records, generation) = AppendPlanner.CheckBackup(cardPaths);

        // What failed, from the reports on the destinations (never from memory): the newest record of each path.
        var failed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int d = 0; d < paths.Count; d++)
            foreach (var (path, rec) in records[d].Files)
                if (rec.File.SourceError != null || !Good(rec, paths[d], d)) failed.Add(path);
        if (failed.Count == 0) throw OffloadError.Preflight(PreflightCode.RepairNothing, Msg.RepairNothing);

        var scan = Scanner.Scan(source, ct);
        var onCard = scan.Files.ToDictionary(f => f.RelPath, StringComparer.OrdinalIgnoreCase);
        bool cardLocalTime = FileTimes.StoresLocalTime(sourceFilesystem ?? NativeIO.FilesystemType(source));
        var taken = scan.Files.Select(f => f.RelPath).Concat(records.SelectMany(r => r.Files.Keys)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<string>();
        var files = new List<SourceFile>();
        var write = new List<bool[]>();
        var expected = new List<FileHashes>();
        var setAside = new List<string?[]>();
        foreach (var path in failed)
        {
            ct.ThrowIfCancellationRequested();
            var recorded = records.Select(r => r.Files.GetValueOrDefault(path)).First(r => r != null)!;
            // The hashes the card gave when it was read in full: the newest report of the path that has them (a cancelled
            // or failed repair recorded none).
            var hashed = Hashed(records, path) ?? recorded.File;
            if (!onCard.TryGetValue(path, out var card))
            {
                conflicts.Add(Msg.RepairNotOnCard(path));
                continue;
            }
            if (recorded.File.Size != card.Size || !FileTimes.Close(recorded.File.Modified, card.ModifiedUtc, cardLocalTime))
            {
                conflicts.Add(AppendPlanner.Conflict(card, "záloha", recorded.File.Size, recorded.File.Modified));
                continue;
            }
            if (taken.Contains(DestWriter.PartName(path))) conflicts.Add(Msg.AppendPartNameTaken(path, DestWriter.PartName(path)));
            var mask = new bool[paths.Count];
            var aside = new string?[paths.Count];
            for (int d = 0; d < paths.Count; d++)
            {
                bool exists = File.Exists(Layout.Join(paths[d], path));
                var rec = records[d].Files.GetValueOrDefault(path);
                // The column's state decides (a repair's record marks a copy it could not trust as failed, a kept one as is).
                if (rec != null && Good(rec, paths[d], d))
                {
                    // A good copy: never written — but it must be there to be read back (as an append requires).
                    if (!exists) throw OffloadError.Preflight(PreflightCode.AppendMissingFile, Msg.AppendMissingFile(AppendPlanner.Disk(paths[d]), path));
                    continue;
                }
                mask[d] = true;
                if (!exists) continue;
                aside[d] = SetAsidePath(generation, path);
                if (File.Exists(Layout.Join(paths[d], aside[d]!)) || Directory.Exists(Layout.Join(paths[d], aside[d]!)))
                    conflicts.Add(Msg.RepairSetAsideTaken(path, AppendPlanner.Disk(paths[d]), aside[d]!));
            }
            files.Add(card);
            write.Add(mask);
            setAside.Add(aside);
            expected.Add(new FileHashes(
                hashed.Xxh64 is string x ? Convert.ToUInt64(x, 16) : null,
                hashed.Md5 is string m ? Convert.FromHexString(m) : null));
        }
        if (conflicts.Count > 0) throw OffloadError.Preflight(PreflightCode.RepairConflict, Msg.RepairConflict(conflicts));

        var dirs = files.SelectMany(f => Ancestors(f.RelPath)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new RepairPlan
        {
            Source = Path.GetFullPath(source), CardPaths = paths, DestRoots = roots, Date = date, CardName = cardName, Generation = generation,
            Files = files, Dirs = dirs, Write = write, Expected = expected, SetAside = setAside,
            HashAlgorithms = records[0].Generations[0].Report.Algorithms,
        };
    }

    /// The newest report entry of `path` in any card folder that carries the card's xxh64.
    static ReportFile? Hashed(IReadOnlyList<CardRecord> records, string path) =>
        records.SelectMany(r => r.Generations).OrderByDescending(g => g.Generation)
            .SelectMany(g => g.Report.Files.Where(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase) && f.Xxh64 != null))
            .FirstOrDefault();

    /// The newest record of a file holds a good copy on the destination of `cardPath` (its column in that report).
    static bool Good(RecordedFile rec, string cardPath, int position)
    {
        int column = CardRecord.DestinationIndex(rec.Report, cardPath, position);
        var state = column < rec.File.Destinations.Count ? rec.File.Destinations[column] : "pending";
        return state is "verified" || (state == "written" && !rec.Report.Verified);
    }

    static IEnumerable<string> Ancestors(string rel)
    {
        for (int i = rel.IndexOf('/'); i >= 0; i = rel.IndexOf('/', i + 1)) yield return rel[..i];
    }
}
```

#### `windows/src/Offload.Core/CardRecord.cs`

Najít (1/1):

```csharp
            foreach (var f in g.Report.Files) files[f.Path] = new RecordedFile(f, g.Generation, g.Report);
            var generationClips = g.Report.Clips.Count > 0 ? g.Report.Clips
                : ClipDetector.Detect(g.Report.Files.Select(f => new SourceFile(f.Path, f.Size, f.Modified)).ToList()).Select(c => new ClipEntry { Clip = c });
            foreach (var c in generationClips)
            {
                var recorded = new RecordedClip(c, g.Generation);
                if (clipAt.TryGetValue(c.Clip.Path, out var i)) clips[i] = recorded;
                else
                {
                    clipAt[c.Clip.Path] = clips.Count;
```

Nahradit:

```csharp
            foreach (var f in g.Report.Files) files[f.Path] = new RecordedFile(f, g.Generation, g.Report);
            var generationClips = g.Report.Clips.Count > 0 ? g.Report.Clips
                : ClipDetector.Detect(g.Report.Files.Select(f => new SourceFile(f.Path, f.Size, f.Modified)).ToList()).Select(c => new ClipEntry { Clip = c });
            bool analysed = g.Report.Clips.Count > 0;
            foreach (var c in generationClips)
            {
                var recorded = new RecordedClip(c, g.Generation);
                // A generation without analysis (a repair rewrites a clip's file) never replaces a clip described before.
                if (clipAt.TryGetValue(c.Clip.Path, out var i)) { if (analysed || clips[i].Entry.Info == null) clips[i] = recorded; }
                else
                {
                    clipAt[c.Clip.Path] = clips.Count;
```

#### `windows/src/Offload.Core/DestWriter.cs`

Najít (1/3):

```csharp
    long verified;

    public string CardPath { get; }
    public DestFileResult[] Results { get; }
    /// False once this volume refused unbuffered writes and the writer fell back to the file cache.
    public bool Unbuffered { get; private set; }
    public long Written => Interlocked.Read(ref written);
```

Nahradit:

```csharp
    long verified;

    public string CardPath { get; }
    public int Index => index;
    public DestFileResult[] Results { get; }
    /// Repair: the non-matching copies moved aside (file index, where to — relative); read after Wait.
    public List<(int File, string MovedTo)> MovedAside { get; } = [];
    /// False once this volume refused unbuffered writes and the writer fell back to the file cache.
    public bool Unbuffered { get; private set; }
    public long Written => Interlocked.Read(ref written);
```

Najít (2/3):

```csharp
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
```

Nahradit:

```csharp
        return slash < 0 ? name : rel[..slash] + "/" + name;
    }

    /// `setAside` (repair): the copy at the file's place did not match the card — it is moved there first (relative to
    /// the card folder, never replacing anything); a failed move fails the file here and leaves that copy where it is.
    public void Begin(SourceFile file, string? setAside = null, int fileIndex = -1) => worker.Post(() =>
    {
        currentError = null;
        partPath = Layout.Join(CardPath, PartName(file.RelPath));
        try
        {
            if (setAside != null)
            {
                var aside = Layout.Join(CardPath, setAside);
                NativeIO.Mkdirs(Path.GetDirectoryName(aside)!);
                NativeIO.Move(Layout.Join(CardPath, file.RelPath), aside, replace: false);
                MovedAside.Add((fileIndex, setAside));
            }
            handle = OpenPart(file.Size);
            File.SetAttributes(partPath, FileAttributes.Hidden);
        }
```

Najít (3/3):

```csharp
        }
    });

    public void Abort(int index, string reason) => worker.Post(() =>
    {
        CloseAndRemovePart();
```

Nahradit:

```csharp
        }
    });

    /// Repair: this destination already holds a good copy of file `index` — nothing is written, the read-back checks it.
    public void Keep(int index) => worker.Post(() => Results[index] = new DestFileResult(FileState.Written, null));

    /// After Wait (the worker is idle): file `index` is not trusted here after all (a repair whose card read differently).
    public void Fail(int index, string reason) => Results[index] = new DestFileResult(FileState.Failed, reason);

    public void Abort(int index, string reason) => worker.Post(() =>
    {
        CloseAndRemovePart();
```

#### `windows/src/Offload.Core/HtmlReport.cs`

Najít (1/1):

```csharp
        h.Append($"<title>Offload {Esc(r.CardName)} – {Esc(r.Date)}</title>\n<style>\n");
        h.Append(Styles(forPrint));
        h.Append("</style></head><body><main>\n");
        var append = r.Generation > 1 ? $" <span class=\"sub\">· doplnění (generace {r.Generation})</span>" : "";
        h.Append($"<header><h1>{Esc(r.CardName)}{append}</h1><span class=\"badge {statusClass}\">{status}</span>\n");
        h.Append($"<span class=\"sub\">{DateTimeText(r.Started)} – {TimeText(r.Finished)}</span></header>\n");
        h.Append("<div class=\"stats\">\n");
```

Nahradit:

```csharp
        h.Append($"<title>Offload {Esc(r.CardName)} – {Esc(r.Date)}</title>\n<style>\n");
        h.Append(Styles(forPrint));
        h.Append("</style></head><body><main>\n");
        var append = r.Generation > 1 ? $" <span class=\"sub\">· {(r.IsRepair ? "oprava" : "doplnění")} (generace {r.Generation})</span>" : "";
        h.Append($"<header><h1>{Esc(r.CardName)}{append}</h1><span class=\"badge {statusClass}\">{status}</span>\n");
        h.Append($"<span class=\"sub\">{DateTimeText(r.Started)} – {TimeText(r.Finished)}</span></header>\n");
        h.Append("<div class=\"stats\">\n");
```

#### `windows/src/Offload.Core/Messages.cs`

Najít (1/2):

```csharp
    AppendBusy,
    /// Not an error: the backup already has everything (the job ends "Hotovo").
    NothingNew,
}

/// Every message a user sees from checks and validation (GUI job card, CLI) — Czech, one method per message, no resx.
```

Nahradit:

```csharp
    AppendBusy,
    /// Not an error: the backup already has everything (the job ends "Hotovo").
    NothingNew,
    /// Not an error: a repair found every file good on every destination (the job ends "Hotovo").
    RepairNothing,
    /// A repair would not copy what the backup recorded: the card or the card folders changed since.
    RepairConflict,
}

/// Every message a user sees from checks and validation (GUI job card, CLI) — Czech, one method per message, no resx.
```

Najít (2/2):

```csharp
    public static string AppendBusy(string cardPath) =>
        $"Do této zálohy právě doplňuje jiná úloha ({cardPath}) — počkejte, až skončí.";
    public const string NothingNew = "Nic nového — záloha je úplná.";
    public static string UnverifiedEarlier(int n) =>
        $"{n} {Desktop.Texts.Plural(n, "soubor", "soubory", "souborů")} z dřívější zálohy {Desktop.Texts.Plural(n, "nebyl ověřen", "nebyly ověřeny", "nebylo ověřeno")} — doporučeno Ověřit zálohu.";
    public const string CardNotInHistory = "Karta není v historii — zadejte složky zálohy volbou --into.";
```

Nahradit:

```csharp
    public static string AppendBusy(string cardPath) =>
        $"Do této zálohy právě doplňuje jiná úloha ({cardPath}) — počkejte, až skončí.";
    public const string NothingNew = "Nic nového — záloha je úplná.";

    // Repair — "Zkopírovat znovu" (UI návrh, nová věc 4)
    public const string RepairNothing = "Nic k opravě — všechny soubory jsou na všech cílech v pořádku.";
    public static string RepairConflict(IReadOnlyList<string> lines) =>
        $"Oprava zastavena — nic se nezkopírovalo. {lines.Count} {Desktop.Texts.Plural(lines.Count, "soubor nejde opravit", "soubory nejde opravit", "souborů nejde opravit")}:\n  "
        + string.Join("\n  ", lines.Take(10)) + (lines.Count > 10 ? "\n  …" : "")
        + "\nKarta se od zálohy změnila nebo ve složce zálohy chybí soubor — zkontrolujte ji přes Ověřit zálohu, nebo udělejte celou kopii.";
    /// One line of RepairConflict: the file is no longer on the card.
    public static string RepairNotOnCard(string path) => $"{path}: na kartě už není";
    /// One line of RepairConflict: the place for the non-matching copy is taken.
    public static string RepairSetAsideTaken(string path, string disk, string aside) =>
        $"{path}: na {disk} je místo pro odložení nesouhlasné kopie ({aside}) obsazené";
    /// The comment of a repair's ASC MHL generation.
    public static string MhlRepairComment(int n, string source) =>
        $"Offload oprava: {n} {Desktop.Texts.Plural(n, "soubor", "soubory", "souborů")} znovu z {source}";
    public static string UnverifiedEarlier(int n) =>
        $"{n} {Desktop.Texts.Plural(n, "soubor", "soubory", "souborů")} z dřívější zálohy {Desktop.Texts.Plural(n, "nebyl ověřen", "nebyly ověřeny", "nebylo ověřeno")} — doporučeno Ověřit zálohu.";
    public const string CardNotInHistory = "Karta není v historii — zadejte složky zálohy volbou --into.";
```

#### `windows/src/Offload.Core/Mhl.cs`

Najít (1/2):

```csharp
        var folder = Path.GetFileName(cardPath.TrimEnd('\\'));
        var fileName = $"{generation:0000}_{folder}_{now.ToUniversalTime().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}Z.mhl";
        int listed = report.Files.Count(f => f.Xxh64 != null);
        var comment = generation > 1
            ? $"Offload doplnění: {listed} {Texts.Plural(listed, "nový soubor", "nové soubory", "nových souborů")} z {report.Source}"
            : $"Offload of {report.CardName} from {report.Source}";
        IEnumerable<string> ignore = earlier?.Generations.FirstOrDefault() is { } first
```

Nahradit:

```csharp
        var folder = Path.GetFileName(cardPath.TrimEnd('\\'));
        var fileName = $"{generation:0000}_{folder}_{now.ToUniversalTime().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}Z.mhl";
        int listed = report.Files.Count(f => f.Xxh64 != null);
        var comment = report.IsRepair ? Msg.MhlRepairComment(listed, report.Source)
            : generation > 1
            ? $"Offload doplnění: {listed} {Texts.Plural(listed, "nový soubor", "nové soubory", "nových souborů")} z {report.Source}"
            : $"Offload of {report.CardName} from {report.Source}";
        IEnumerable<string> ignore = earlier?.Generations.FirstOrDefault() is { } first
```

Najít (2/2):

```csharp
        {
            if (f.Xxh64 == null) continue;
            var state = f.Destinations[d];
            var action = state is "verified" or "written" ? "original" : "failed";
            Line("    <hash>");
            Line($"      <path size=\"{f.Size}\" lastmodificationdate=\"{IsoDate(f.Modified)}\">{Esc(f.Path)}</path>");
            Line($"      <xxh64 action=\"{action}\" hashdate=\"{stamp}\">{f.Xxh64}</xxh64>");
```

Nahradit:

```csharp
        {
            if (f.Xxh64 == null) continue;
            var state = f.Destinations[d];
            // A repair's good copy that was only read back is "verified" (its hash was recorded before), a new one "original".
            var action = state is "verified" or "written" ? (f.Rewritten is { } w && d < w.Count && !w[d] ? "verified" : "original") : "failed";
            Line("    <hash>");
            Line($"      <path size=\"{f.Size}\" lastmodificationdate=\"{IsoDate(f.Modified)}\">{Esc(f.Path)}</path>");
            Line($"      <xxh64 action=\"{action}\" hashdate=\"{stamp}\">{f.Xxh64}</xxh64>");
```

#### `windows/src/Offload.Core/Models.cs`

Najít (1/2):

```csharp

public readonly record struct DestFileResult(FileState State, string? Error);

public enum PlanMode { Full, Append }

public sealed class OffloadPlan
{
```

Nahradit:

```csharp

public readonly record struct DestFileResult(FileState State, string? Error);

public enum PlanMode { Full, Append, Repair }

public sealed class OffloadPlan
{
```

Najít (2/2):

```csharp
    public IReadOnlyList<string> SpaceWarnings { get; init; } = [];
    /// The project the job runs in (spec 4.1), null = "Bez projektu" — set by OffloadJob, written into the report.
    public string? Project { get; set; }
}

public enum CopyPhase { Copy, Flush, Verify, Report, Done }

/// `Paused`: Pozastavit was pressed (PauseGate); `PauseHeld`: the copy or the read-back really waits now.
public sealed record ProgressSnapshot(
    CopyPhase Phase, long TotalBytes, int TotalFiles, int FileIndex, string CurrentFile,
    long ReadBytes, long[] WrittenBytes, long[] VerifiedBytes, int Errors,
    bool Paused = false, bool PauseHeld = false);

public sealed class OffloadError : Exception
{
```

Nahradit:

```csharp
    public IReadOnlyList<string> SpaceWarnings { get; init; } = [];
    /// The project the job runs in (spec 4.1), null = "Bez projektu" — set by OffloadJob, written into the report.
    public string? Project { get; set; }
    /// Repair: per file, the destinations that get a new copy — the others hold a good copy, which is only read back.
    /// Null = every destination (a full copy, an append).
    public IReadOnlyList<bool[]>? Write { get; init; }
    /// Repair: per file the hashes the earlier backup recorded from the card. A copy that is only read back is compared
    /// with them, and the card read now must give the same xxh64.
    public IReadOnlyList<FileHashes>? Expected { get; init; }
    /// Repair: per file and destination, where its copy that did not match goes before the new one is written
    /// (relative to the card folder), else null.
    public IReadOnlyList<string?[]>? SetAside { get; init; }

    /// File `file` gets written to destination `dest`.
    public bool Writes(int file, int dest) => Write?[file][dest] ?? true;
}

public enum CopyPhase { Copy, Flush, Verify, Report, Done }

/// `Paused`: Pozastavit was pressed (PauseGate); `PauseHeld`: the copy or the read-back really waits now.
/// `PlannedBytes`: what each destination gets written (null = TotalBytes each).
public sealed record ProgressSnapshot(
    CopyPhase Phase, long TotalBytes, int TotalFiles, int FileIndex, string CurrentFile,
    long ReadBytes, long[] WrittenBytes, long[] VerifiedBytes, int Errors,
    bool Paused = false, bool PauseHeld = false, long[]? PlannedBytes = null)
{
    /// What destination `d` gets written: TotalBytes, less for a destination a repair only reads back.
    public long Planned(int d) => PlannedBytes is { } p && d < p.Length ? p[d] : TotalBytes;
}

public sealed class OffloadError : Exception
{
```

#### `windows/src/Offload.Core/OffloadEngine.Run.cs`

Najít (1/7):

```csharp
            foreach (var d in plan.Scan.Dirs) NativeIO.Mkdirs(Layout.Join(path, d));
        }
        var ws = cardPaths.Select((p, i) => new DestWriter(i, p, files.Count, Options)).ToArray();
        lock (gate)
        {
            this.plan = plan;
            writers = ws;
        }
        // Created before the try and disposed last: writer queues may still hold chunks that point into the pool's native buffers.
        BufferPool? pool = null;
```

Nahradit:

```csharp
            foreach (var d in plan.Scan.Dirs) NativeIO.Mkdirs(Layout.Join(path, d));
        }
        var ws = cardPaths.Select((p, i) => new DestWriter(i, p, files.Count, Options)).ToArray();
        var plannedNow = ws.Select(w => files.Where((f, i) => plan.Writes(i, w.Index)).Sum(f => f.Size)).ToArray();
        lock (gate)
        {
            this.plan = plan;
            writers = ws;
            planned = plan.Write == null ? null : plannedNow;
        }
        // Created before the try and disposed last: writer queues may still hold chunks that point into the pool's native buffers.
        BufferPool? pool = null;
```

Najít (2/7):

```csharp
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (IsCancelled)
                {
                    sourceErrors[i] = "cancelled";
```

Nahradit:

```csharp
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                // A repair writes a file only where it failed; a destination with a good copy only reads it back (Keep) —
                // also when the repair is cancelled before this file: its good copy must never look unwritten.
                var targets = ws.Where(w => plan.Writes(i, w.Index)).ToArray();
                foreach (var w in ws.Except(targets)) w.Keep(i);
                if (IsCancelled)
                {
                    sourceErrors[i] = "cancelled";
```

Najít (3/7):

```csharp
                    fileIndex = i;
                    currentFile = f.RelPath;
                }
                var failure = CopyFile(plan.Source, f, ws, hashers, pool);
                if (failure != null)
                {
                    sourceErrors[i] = failure;
                    foreach (var h in hashers) h.Abort();
                    foreach (var w in ws) w.Abort(i, "source: " + failure);
                    lock (gate) errorCount++;
                }
                else
                {
                    foreach (var h in hashers) h.Finish(i);
                    foreach (var w in ws) w.Finish(i, f);
                }
            }
            foreach (var h in hashers) h.Wait();
```

Nahradit:

```csharp
                    fileIndex = i;
                    currentFile = f.RelPath;
                }
                var failure = CopyFile(plan, i, targets, hashers, pool);
                if (failure != null)
                {
                    sourceErrors[i] = failure;
                    foreach (var h in hashers) h.Abort();
                    foreach (var w in targets) w.Abort(i, "source: " + failure);
                    lock (gate) errorCount++;
                }
                else
                {
                    foreach (var h in hashers) h.Finish(i);
                    foreach (var w in targets) w.Finish(i, f);
                }
            }
            foreach (var h in hashers) h.Wait();
```

Najít (4/7):

```csharp
                sourceErrors[i] = hashError;
                lock (gate) errorCount++;
            }

            SetPhase(CopyPhase.Flush);
            OnEachDestination(ws, w => w.FlushDevice());
```

Nahradit:

```csharp
                sourceErrors[i] = hashError;
                lock (gate) errorCount++;
            }
            // A repair copies what the backup recorded: a card that reads differently now is not repaired from.
            for (int i = 0; plan.Expected != null && i < files.Count; i++)
            {
                if (sourceErrors[i] != null || plan.Expected[i].Xxh64 is not ulong want || hashes[i].Xxh64 is not ulong got || got == want) continue;
                sourceErrors[i] = $"card differs from the backup: xxh64 {Format.Hex64(got)} != recorded {Format.Hex64(want)}";
                // The new copies are of what the card holds now, not of the backup: never a good record (the writers are idle).
                foreach (var w in ws.Where(w => plan.Writes(i, w.Index))) w.Fail(i, sourceErrors[i]!);
                lock (gate) errorCount++;
            }

            SetPhase(CopyPhase.Flush);
            OnEachDestination(ws, w => w.FlushDevice());
```

Najít (5/7):

```csharp
            if (Options.Verify && !IsCancelled)
            {
                SetPhase(CopyPhase.Verify);
                OnEachDestination(ws, w => w.Verify(files, hashes, plan.Hashes, cts.Token, Pauses));
            }
            var finished = DateTime.UtcNow;
            var verifyPaused = Pauses.PausedTotal - pausedBefore - copyPaused;
```

Nahradit:

```csharp
            if (Options.Verify && !IsCancelled)
            {
                SetPhase(CopyPhase.Verify);
                // A copy that was only read back (repair) is compared with what the backup recorded, a new one with the card.
                OnEachDestination(ws, w => w.Verify(files, plan.Expected == null ? hashes
                    : hashes.Select((h, i) => plan.Writes(i, w.Index) ? h : plan.Expected[i]).ToList(), plan.Hashes, cts.Token, Pauses));
            }
            var finished = DateTime.UtcNow;
            var verifyPaused = Pauses.PausedTotal - pausedBefore - copyPaused;
```

Najít (6/7):

```csharp
            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, hashes, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled, started, copyFinished, finished, copyPaused, verifyPaused);
            var reportErrors = new List<string>();
            foreach (var path in cardPaths)
            {
```

Nahradit:

```csharp
            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, hashes, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled, started, copyFinished, finished, copyPaused, verifyPaused);
            if (plan.Mode == PlanMode.Repair)
                report.SetAside = ws.SelectMany(w => w.MovedAside.Select(m => new SetAsideCopy(w.Index, files[m.File].RelPath, m.MovedTo))).ToList();
            var reportErrors = new List<string>();
            foreach (var path in cardPaths)
            {
```

Najít (7/7):

```csharp
        }
    }

    /// Reads one file block by block and hands every block to the hashers and all writers. Returns the failure, or null.
    string? CopyFile(string sourceRoot, SourceFile f, DestWriter[] ws, IReadOnlyList<SourceHasher> hashers, BufferPool pool)
    {
        var src = Layout.Join(sourceRoot, f.RelPath);
        foreach (var w in ws) w.Begin(f);
        foreach (var h in hashers) h.Begin();
        FileReader reader;
        try { reader = new FileReader(src); }
```

Nahradit:

```csharp
        }
    }

    /// Reads file `index` block by block and hands every block to the hashers and to `ws` (the destinations it is
    /// written to). Returns the failure, or null.
    string? CopyFile(OffloadPlan plan, int index, DestWriter[] ws, IReadOnlyList<SourceHasher> hashers, BufferPool pool)
    {
        var f = plan.Scan.Files[index];
        var src = Layout.Join(plan.Source, f.RelPath);
        foreach (var w in ws) w.Begin(f, plan.SetAside?[index][w.Index], index);
        foreach (var h in hashers) h.Begin();
        FileReader reader;
        try { reader = new FileReader(src); }
```

#### `windows/src/Offload.Core/OffloadEngine.cs`

Najít (1/3):

```csharp
    int errorCount;
    long readBytes;
    DestWriter[] writers = [];
    OffloadPlan? plan;

    static readonly System.Text.RegularExpressions.Regex DateFormat = new(@"^\d{4}-\d{2}-\d{2}\z");
```

Nahradit:

```csharp
    int errorCount;
    long readBytes;
    DestWriter[] writers = [];
    long[]? planned;
    OffloadPlan? plan;

    static readonly System.Text.RegularExpressions.Regex DateFormat = new(@"^\d{4}-\d{2}-\d{2}\z");
```

Najít (2/3):

```csharp
        };
    }

    void CheckOptions()
    {
        if (Options.ChunkSize <= 0 || Options.ChunkSize % 4096 != 0 || Options.ChunkSize > 1 << 30)
```

Nahradit:

```csharp
        };
    }

    /// A repair ("Zkopírovat znovu", UI návrh 4): the checks of an append (options, source, the earlier backup and its
    /// destinations), then what RepairPlanner finds. `RepairNothing` when every file is good everywhere.
    public RepairPlan PlanRepair(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default)
    {
        CheckOptions();
        source = CheckSource(source);
        FullRoots(cardPaths);
        CheckNotAnOffload(source);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
        var r = RepairPlanner.Plan(source, cardPaths, linked.Token);
        CheckDestinations(source, r.DestRoots);
        return r;
    }

    /// The plan of a repair: only the files that failed, only onto the destinations where they failed (FAT32 and the
    /// space checked there), generation N in the same card folders.
    public OffloadPlan PrepareRepair(RepairPlan r)
    {
        var scan = new SourceScan { Root = r.Source, Files = r.Files, Dirs = r.Dirs, Skipped = [] };
        var places = new List<SpacePlace>();
        for (int d = 0; d < r.DestRoots.Count; d++)
        {
            var written = r.Files.Where((_, i) => r.Write[i][d]).ToList();
            if (Fat32Problem(NativeIO.FilesystemType(r.DestRoots[d]), written) is string fat)
                throw OffloadError.Preflight(PreflightCode.Fat32, $"{r.DestRoots[d]}: {fat}");
            long card = written.Count == 0 ? 0 : SpaceEstimator.CardBytes(written.Sum(f => f.Size), written.Count);
            places.Add(new SpacePlace(r.DestRoots[d], HistoryText.DiskName(r.DestRoots[d]), new SpaceNeed(card, 0, SpaceEstimator.ReportBytes(0))));
        }
        var (problems, lines) = SpacePlanner.CheckPlaces(places, FreeBytes);
        if (problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Blocking) is { } blocking)
            throw OffloadError.Preflight(PreflightCode.NoSpace, blocking.Text);
        return new OffloadPlan
        {
            Source = r.Source, Scan = scan, Date = r.Date, CardName = r.CardName, DestRoots = r.DestRoots,
            Mode = PlanMode.Repair, Generation = r.Generation, Space = lines, SpaceWarnings = problems.Select(p => p.Text).ToList(),
            Hashes = HashKinds.FromNames(r.HashAlgorithms), Write = r.Write, Expected = r.Expected, SetAside = r.SetAside,
        };
    }

    void CheckOptions()
    {
        if (Options.ChunkSize <= 0 || Options.ChunkSize % 4096 != 0 || Options.ChunkSize > 1 << 30)
```

Najít (3/3):

```csharp
        {
            return new ProgressSnapshot(phase, plan?.Scan.TotalBytes ?? 0, plan?.Scan.Files.Count ?? 0, fileIndex, currentFile,
                Interlocked.Read(ref readBytes), writers.Select(w => w.Written).ToArray(), writers.Select(w => w.Verified).ToArray(), errorCount,
                Pauses.IsPaused, Pauses.IsHolding);
        }
    }
```

Nahradit:

```csharp
        {
            return new ProgressSnapshot(phase, plan?.Scan.TotalBytes ?? 0, plan?.Scan.Files.Count ?? 0, fileIndex, currentFile,
                Interlocked.Read(ref readBytes), writers.Select(w => w.Written).ToArray(), writers.Select(w => w.Verified).ToArray(), errorCount,
                Pauses.IsPaused, Pauses.IsHolding, planned);
        }
    }
```

#### `windows/src/Offload.Core/OffloadReport.cs`

Najít (1/5):

```csharp
    [JsonPropertyName("sourceError")] public string? SourceError { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<string> Destinations { get; init; }
    [JsonPropertyName("errors")] public required IReadOnlyList<string?> Errors { get; init; }
}

public sealed partial class OffloadReport
{
    [JsonPropertyName("tool")] public string Tool { get; init; } = "offload";
```

Nahradit:

```csharp
    [JsonPropertyName("sourceError")] public string? SourceError { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<string> Destinations { get; init; }
    [JsonPropertyName("errors")] public required IReadOnlyList<string?> Errors { get; init; }
    /// Repair only (Windows from 0.5.0): per destination, true = a new copy was written, false = the good copy there was
    /// only read back. The Mac ignores it.
    [JsonPropertyName("rewritten")] public IReadOnlyList<bool>? Rewritten { get; init; }
}

/// Repair: a copy that did not match the card, moved aside before the new copy (never deleted).
public sealed record SetAsideCopy(
    [property: JsonPropertyName("destination")] int Destination,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("movedTo")] string MovedTo);

public sealed partial class OffloadReport
{
    [JsonPropertyName("tool")] public string Tool { get; init; } = "offload";
```

Najít (2/5):

```csharp
    /// leave the pause out; the Mac ignores the keys (its speeds then include it).
    [JsonPropertyName("copyPausedSeconds")] public double? CopyPausedSeconds { get; set; }
    [JsonPropertyName("verifyPausedSeconds")] public double? VerifyPausedSeconds { get; set; }

    /// The hashes of this copy: `hashAlgorithms`, else the one `hashAlgorithm` (reports without the key, the Mac).
    [JsonIgnore] public IReadOnlyList<string> Algorithms => HashAlgorithms is { Count: > 0 } all ? all : [HashAlgorithm];
```

Nahradit:

```csharp
    /// leave the pause out; the Mac ignores the keys (its speeds then include it).
    [JsonPropertyName("copyPausedSeconds")] public double? CopyPausedSeconds { get; set; }
    [JsonPropertyName("verifyPausedSeconds")] public double? VerifyPausedSeconds { get; set; }
    /// "repair" for "Zkopírovat znovu" (Windows from 0.5.0); absent for a full copy and an append. The Mac ignores it.
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    /// Repair: the non-matching copies moved aside (`_REPORT/neshoda_NNNN/…`); absent when none.
    [JsonPropertyName("setAside")] public IReadOnlyList<SetAsideCopy>? SetAside { get; set; }

    public const string RepairKind = "repair";
    [JsonIgnore] public bool IsRepair => Kind == RepairKind;

    /// The hashes of this copy: `hashAlgorithms`, else the one `hashAlgorithm` (reports without the key, the Mac).
    [JsonIgnore] public IReadOnlyList<string> Algorithms => HashAlgorithms is { Count: > 0 } all ? all : [HashAlgorithm];
```

Najít (3/5):

```csharp
            SourceError = sourceErrors[i],
            Destinations = destResults.Select(r => r[i].State.Wire()).ToList(),
            Errors = destResults.Select(r => r[i].Error).ToList(),
        }).ToList();
        var cardPaths = plan.CardPaths;
        var dests = plan.DestRoots.Select((root, d) => new ReportDestination
```

Nahradit:

```csharp
            SourceError = sourceErrors[i],
            Destinations = destResults.Select(r => r[i].State.Wire()).ToList(),
            Errors = destResults.Select(r => r[i].Error).ToList(),
            Rewritten = plan.Write?[i].ToList(),
        }).ToList();
        var cardPaths = plan.CardPaths;
        var dests = plan.DestRoots.Select((root, d) => new ReportDestination
```

Najít (4/5):

```csharp
            Skipped = plan.Scan.Skipped,
            Generation = plan.Generation > 1 ? plan.Generation : null,
            Project = plan.Project,
            CopyPausedSeconds = Seconds(copyPaused),
            VerifyPausedSeconds = Seconds(verifyPaused),
            HashAlgorithms = HashKinds.Normalize(plan.Hashes).HasFlag(HashKind.Md5) ? HashKinds.Names(plan.Hashes) : null,
```

Nahradit:

```csharp
            Skipped = plan.Scan.Skipped,
            Generation = plan.Generation > 1 ? plan.Generation : null,
            Project = plan.Project,
            Kind = plan.Mode == PlanMode.Repair ? RepairKind : null,
            CopyPausedSeconds = Seconds(copyPaused),
            VerifyPausedSeconds = Seconds(verifyPaused),
            HashAlgorithms = HashKinds.Normalize(plan.Hashes).HasFlag(HashKind.Md5) ? HashKinds.Names(plan.Hashes) : null,
```

Najít (5/5):

```csharp
                continue;
            }
            var (earlier, missed) = HashListAndMissed(hashList, cardPath, Files.Select(f => f.Path), algorithm);
            NativeIO.WriteFileReplacing(hashList, [.. earlier, .. missed, .. lines]);
        }
    }

    /// `<card>/_REPORT/<card name>.xxh64` (and `.md5` when the ASC MHL history has MD5) gets the lines of every path the
    /// history records but the list lacks (a hash list whose write failed, step 3 of Write) — the record files that may
    /// be rewritten. Used by an append with nothing new (fix round 2, item 6b); an append with new files does the same
```

Nahradit:

```csharp
                continue;
            }
            var (earlier, missed) = HashListAndMissed(hashList, cardPath, Files.Select(f => f.Path), algorithm);
            NativeIO.WriteFileReplacing(hashList, [.. earlier, .. missed, .. NotIn(earlier, lines)]);
        }
    }

    /// The lines of `lines` the list does not have word for word yet (a repair's file is listed already with the hash
    /// the card gave then — the list keeps one line per path and hash).
    static byte[] NotIn(byte[] earlier, byte[] lines)
    {
        var have = Encoding.UTF8.GetString(earlier).Split('\n').Select(l => l.TrimEnd('\r')).ToHashSet(StringComparer.Ordinal);
        var keep = Encoding.UTF8.GetString(lines).Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !have.Contains(l));
        return Encoding.UTF8.GetBytes(string.Concat(keep.Select(l => l + "\n")));
    }

    /// `<card>/_REPORT/<card name>.xxh64` (and `.md5` when the ASC MHL history has MD5) gets the lines of every path the
    /// history records but the list lacks (a hash list whose write failed, step 3 of Write) — the record files that may
    /// be rewritten. Used by an append with nothing new (fix round 2, item 6b); an append with new files does the same
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~RepairPlannerTests|FullyQualifiedName~RepairEngineTests|FullyQualifiedName~AppendPlannerTests|FullyQualifiedName~AppendEngineTests"`

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

- Úkol 3 — pozastavení celé úlohy: `OffloadJob.Pause/Resume/IsPaused`, `JobStatus.Paused` / `PauseHeld` / `PausedTotal` (jen u běžící úlohy); 2. fáze se zastaví mezi klipy (rozpracovaný klip analýzy nebo proxy doběhne, ffmpeg se nikdy neuspává, takže hlídač zaseknutí pauzu nevidí — test s limitem 2 s a pauzou 3 s) a před PDF a denním reportem; zámek dne ani historie se během pauzy nedrží (test), zámek složek doplnění zůstává (test); zrušení během pauzy (i zavření aplikace) skončí hned jako Zrušeno; `JobProgress`: během pauzy rychlost 0, bez odhadu a bez „Úzkého hrdla“ (`BrakeMeter.Reset`), po Pokračovat se měří od obnovení, texty „Pozastavuje se“ / „Pozastaví se po proxy klipu A003…“ / „Pozastaveno“; `OffloadOptions.ProxyStallTimeout` (jen testy)
- Úkol 2 — pozastavení v jádru, 1. fáze (nová věc 9): `PauseGate` (Pozastavit / Pokračovat, čas pozastavení s falešnými hodinami v testech); kopie čeká před každým souborem a před každým 16MB blokem (zapisovače doběhnou a stojí, soubory `.offload-part` zůstanou otevřené), zpětné čtení před každým souborem a blokem (`Hashing.HashFile(…, beforeBlock)`); `ProgressSnapshot.Paused` / `PauseHeld`; zrušení během pauzy skončí hned jako zrušení (rozpracované soubory se smažou); disk odpojený během pauzy = chyba zápisu po Pokračovat jen na tom cíli; report `copyPausedSeconds` / `verifyPausedSeconds` (chybí, když se nepozastavovalo), rychlosti v TXT, HTML a na kartě bez pauzy, TXT řádek `Paused:`, HTML „pozastaveno · mimo rychlosti“
- Úkol 1 — návrh nového vzhledu `docs/ui-navrh/` je v repu jako závazný podklad (README = zadání, `mockup/*.html` = přesné barvy, rozměry a texty, `screeny/*.png` = náhledy); README doplněno o rozhodnutí uživatele z 2026-10-07 (tmavé i světlé téma podle Windows, systémové písmo, nové věci 1–4, 6, 7, 9 a 10, přepínač projektů místo presetů, „Zkopírovat znovu“ jen nesouhlasné soubory na postižený disk)
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 4 — oprava v jádru („Zkopírovat znovu“, nová věc 4): `RepairPlanner` vezme z reportů na cílech soubory, jejichž nejnovější záznam selhal, a naplánuje je jen na cíle, kde selhaly — dobrá kopie se nikdy nepřepíše, jen se v téže generaci znovu přečte a porovná se zaznamenaným hashem; kontroly dřívější zálohy sdílí s doplněním (`AppendPlanner.CheckBackup`: složky, reporty, řetěz ASC MHL, stejné generace); soubor na kartě musí mít velikost a čas jako v záznamu (jinak „Oprava zastavena — nic se nezkopírovalo…“), chybějící dobrá kopie zastaví opravu jako doplnění; nesouhlasná kopie se před novou přesune do `_REPORT\neshoda_NNNN\<cesta>` (nic se nemaže ani nepřepisuje, obsazené místo = odmítnutí); engine: `PlanMode.Repair`, maska zápisu po cílech (`OffloadPlan.Write`, `DestWriter.Keep`), očekávané hashe (karta, která se teď čte jinak = „card differs from the backup“, soubor zůstane chybný), místo a FAT32 jen tam, kam se píše, `ProgressSnapshot.PlannedBytes`; report generace N `kind: "repair"`, `files[].rewritten`, `setAside`; ASC MHL generace N na všech cílech (přečtená dobrá kopie `verified`, nová `original`, komentář „Offload oprava: …“), seznam `.xxh64` bez opakovaných řádků, HTML „oprava (generace N)“; generace bez analýzy klipů už v `CardRecord` nepřepíše analyzovaný klip; po opravě jde doplnění jako generace N+1
- Úkol 3 — pozastavení celé úlohy: `OffloadJob.Pause/Resume/IsPaused`, `JobStatus.Paused` / `PauseHeld` / `PausedTotal` (jen u běžící úlohy); 2. fáze se zastaví mezi klipy (rozpracovaný klip analýzy nebo proxy doběhne, ffmpeg se nikdy neuspává, takže hlídač zaseknutí pauzu nevidí — test s limitem 2 s a pauzou 3 s) a před PDF a denním reportem; zámek dne ani historie se během pauzy nedrží (test), zámek složek doplnění zůstává (test); zrušení během pauzy (i zavření aplikace) skončí hned jako Zrušeno; `JobProgress`: během pauzy rychlost 0, bez odhadu a bez „Úzkého hrdla“ (`BrakeMeter.Reset`), po Pokračovat se měří od obnovení, texty „Pozastavuje se“ / „Pozastaví se po proxy klipu A003…“ / „Pozastaveno“; `OffloadOptions.ProxyStallTimeout` (jen testy)
- Úkol 2 — pozastavení v jádru, 1. fáze (nová věc 9): `PauseGate` (Pozastavit / Pokračovat, čas pozastavení s falešnými hodinami v testech); kopie čeká před každým souborem a před každým 16MB blokem (zapisovače doběhnou a stojí, soubory `.offload-part` zůstanou otevřené), zpětné čtení před každým souborem a blokem (`Hashing.HashFile(…, beforeBlock)`); `ProgressSnapshot.Paused` / `PauseHeld`; zrušení během pauzy skončí hned jako zrušení (rozpracované soubory se smažou); disk odpojený během pauzy = chyba zápisu po Pokračovat jen na tom cíli; report `copyPausedSeconds` / `verifyPausedSeconds` (chybí, když se nepozastavovalo), rychlosti v TXT, HTML a na kartě bez pauzy, TXT řádek `Paused:`, HTML „pozastaveno · mimo rychlosti“
- Úkol 1 — návrh nového vzhledu `docs/ui-navrh/` je v repu jako závazný podklad (README = zadání, `mockup/*.html` = přesné barvy, rozměry a texty, `screeny/*.png` = náhledy); README doplněno o rozhodnutí uživatele z 2026-10-07 (tmavé i světlé téma podle Windows, systémové písmo, nové věci 1–4, 6, 7, 9 a 10, přepínač projektů místo presetů, „Zkopírovat znovu“ jen nesouhlasné soubory na postižený disk)
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: repair core - copy only the failed files to the failed destinations (plan 9, task 4)

RepairPlanner takes the files whose newest record failed and plans them only where they failed;
it shares the append checks (CheckBackup) and the append commit (generation N on every destination,
reports _NNNN, MHL generation, chain, hash list last). A good copy is only read back (MHL "verified"),
a non-matching one is moved to _REPORT/neshoda_NNNN, never deleted. The card must read as recorded.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Append\AppendPlanner.cs windows\src\Offload.Core\Append\RepairPlanner.cs windows\src\Offload.Core\CardRecord.cs windows\src\Offload.Core\DestWriter.cs windows\src\Offload.Core\HtmlReport.cs windows\src\Offload.Core\Messages.cs windows\src\Offload.Core\Mhl.cs windows\src\Offload.Core\Models.cs windows\src\Offload.Core\OffloadEngine.Run.cs windows\src\Offload.Core\OffloadEngine.cs windows\src\Offload.Core\OffloadReport.cs windows\tests\Offload.Core.Tests\RepairEngineTests.cs windows\tests\Offload.Core.Tests\RepairPlannerTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 5: Oprava jako úloha fronty a v historii

„Zkopírovat znovu“ spustí úlohu `A001 · oprava` se stejnou kopií nastavení jako dokončená úloha (`JobConfigs.Repair`).
Úloha drží stejný zámek složek jako doplnění, zapíše report, PDF, seznamy klipů a denní report, historii doplní o
generaci `repair` a úspěch spočítá znovu z reportů. `RepairRules.Offer` rozhodne, kdy se tlačítko nabízí (Review Focus 3
a 4: karta není ve čtečce, souběžná oprava).

**Files:**
- Create: `windows/src/Offload.Core/Desktop/RepairRules.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.Core/Desktop/JobConfigs.cs`
- Modify: `windows/src/Offload.Core/History/HistoryEntry.cs`
- Modify: `windows/src/Offload.Core/OffloadJob.cs`
- Test: `windows/tests/Offload.Core.Tests/RepairJobTests.cs` (nový)

**Interfaces:**
- Consumes (úkol 4): `OffloadEngine.PlanRepair`, `PrepareRepair`, `PreflightCode.RepairNothing`, `OffloadReport.IsRepair`; `AppendLock` (stávající).
- Produces:
  - `JobConfig.RepairInto` (`IReadOnlyList<string>?`, složky karty na cílech).
  - `JobConfigs.Repair(JobConfig original, OffloadPlan plan)` → `JobConfig` (kopie nastavení, `Verify = true`, bez analýzy a proxy).
  - `public static class RepairRules` — `bool Offer(JobPhase phase, OffloadReport? report, bool cardPresent, bool repairPending)`, `string Title(string cardName)` („A001 · oprava“), `bool SameBackup(IReadOnlyList<string> a, IReadOnlyList<string> b)`.
  - `HistoryGeneration.Repair` (`"repair"`).

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/RepairJobTests.cs` (nový soubor)

```csharp
using Offload.Core.Append;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// "Zkopírovat znovu" as a queue job: the repair after a job with a non-matching file, history and outputs consistent.
public class RepairJobTests
{
    /// A full job whose copy of A001C002.mov on d2 is changed before the read-back (screen 03).
    static OffloadJob FailedJob(TempDir dir, string history, bool dayReport = false)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 60_000, 2);
        var options = AppendFixtures.Options();
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")])
        {
            Date = "2026-10-07", AnalyzeClips = false, HistoryFolder = history, Project = "Film X",
            Outputs = new OutputSettings { DayReport = dayReport }, Notify = new Notify.NotifySettings { Windows = false },
        }, options);
        options.BeforeVerify = () => RepairFixtures.Corrupt(Layout.Join(job.Plan!.CardPaths[1], RepairFixtures.Bad));
        job.Run();
        return job;
    }

    [Fact]
    public void Repair_FixesTheCard_HistoryEntryGoodAgain_DayReportMade()
    {
        using var dir = new TempDir();
        var history = dir.Sub("history");
        var failed = FailedJob(dir, history, dayReport: true);
        Assert.Equal((JobPhase.Done, false), (failed.Status().Phase, failed.Status().CopySucceeded));
        var store = new HistoryStore(history);
        var before = Assert.Single(store.Load());
        Assert.Equal((false, 1), (before.Success, before.FailedFiles!.Value));

        var repair = new OffloadJob(JobConfigs.Repair(failed.Config, failed.Plan!), AppendFixtures.Options());
        repair.Run();

        var s = repair.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.Equal((PlanMode.Repair, 2, "Film X"), (repair.Plan!.Mode, repair.Plan.Generation, repair.Plan.Project));
        var entry = Assert.Single(store.Load());
        Assert.True(entry.Success);
        Assert.Null(entry.FailedFiles);
        Assert.Equal([(1, HistoryGeneration.Full), (2, HistoryGeneration.Repair)], entry.Generations.Select(g => (g.Seq, g.Kind)));
        Assert.Equal((before.Files, before.Fingerprint, before.Bytes), (entry.Files, entry.Fingerprint, entry.Bytes));
        Assert.Equal("Film X", entry.Project);
        Assert.All(repair.DayReports, p => Assert.True(File.Exists(p)));
        Assert.Equal(2, repair.DayReports.Count);
    }

    [Fact]
    public void NothingToRepair_DoneWithNote()
    {
        using var dir = new TempDir();
        var failed = FailedJob(dir, dir.Sub("history"));
        new OffloadJob(JobConfigs.Repair(failed.Config, failed.Plan!), AppendFixtures.Options()).Run();

        var again = new OffloadJob(JobConfigs.Repair(failed.Config, failed.Plan!), AppendFixtures.Options());
        again.Run();

        var s = again.Status();
        Assert.Equal((JobPhase.Done, Msg.RepairNothing), (s.Phase, s.Note));
        Assert.Null(again.Report);
    }

    // An append or another repair of the same backup holds the folders: the repair is refused at once, nothing written.
    [Fact]
    public void FoldersBusy_Refused()
    {
        using var dir = new TempDir();
        var failed = FailedJob(dir, dir.Sub("history"));
        var paths = failed.Plan!.CardPaths;
        var trees = paths.Select(AppendFixtures.Tree).ToList();
        var repair = new OffloadJob(JobConfigs.Repair(failed.Config, failed.Plan), AppendFixtures.Options());
        // A mutex is owned per thread: hold it here, run the job on another thread.
        using (AppendLock.Acquire(paths))
        {
            var t = new Thread(() => repair.Run());
            t.Start();
            t.Join();
        }

        Assert.Equal(JobPhase.Failed, repair.Status().Phase);
        Assert.Contains("právě doplňuje jiná úloha", repair.Status().Error);
        for (int d = 0; d < trees.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
    }

    [Fact]
    public void Config_TheJobsOwnCopy_AlwaysVerified_NoStage2()
    {
        var original = new JobConfig(@"E:\", [@"D:\A", @"F:\B"])
        {
            Verify = false, AnalyzeClips = true, Proxy = new ProxySettings { Enabled = true }, Project = "Film X",
            Outputs = new OutputSettings { ClipListAle = true }, Notify = new Notify.NotifySettings { Windows = false },
            Tools = new ExternalTools { Ffmpeg = @"C:\ff\ffmpeg.exe" },
        };
        var plan = new OffloadPlan
        {
            Source = @"E:\", Date = "2026-10-07", CardName = "A001", DestRoots = [@"D:\A", @"F:\B"], Project = "Film X",
            Scan = new SourceScan { Root = @"E:\", Files = [], Dirs = [], Skipped = [] },
        };

        var c = JobConfigs.Repair(original, plan);

        Assert.Equal(@"E:\", c.Source);
        Assert.Equal([@"D:\A\2026-10-07\A001", @"F:\B\2026-10-07\A001"], c.RepairInto);
        Assert.True(c.Verify);
        Assert.False(c.AnalyzeClips);
        Assert.False(c.Proxy.Enabled);
        Assert.True(c.AllowSameDevice);
        Assert.Equal("Film X", c.Project);
        Assert.True(c.Outputs.ClipListAle);
        Assert.NotSame(original.Outputs, c.Outputs);
        Assert.False(c.Notify.Windows);
        Assert.NotSame(original.Notify, c.Notify);
        Assert.Equal(@"C:\ff\ffmpeg.exe", c.Tools.Ffmpeg);
    }

    [Theory]
    [InlineData(JobPhase.Done, true, true, false, true)]
    [InlineData(JobPhase.Failed, true, true, false, true)]       // stage 2 failed after a copy with a failed file
    [InlineData(JobPhase.Cancelled, true, true, false, false)]   // a cancelled copy: a new full copy, not a repair
    [InlineData(JobPhase.Copying, true, true, false, false)]
    [InlineData(JobPhase.Done, false, true, false, false)]       // nothing failed
    [InlineData(JobPhase.Done, true, false, false, false)]       // the card is not in its drive
    [InlineData(JobPhase.Done, true, true, true, false)]         // a repair is already queued or running
    public void Offer_Table(JobPhase phase, bool withFailure, bool cardPresent, bool pending, bool offered)
    {
        var report = Reports.Sample(@"D:\dst\2026-10-07\A001", destError: withFailure ? "HASH MISMATCH: dest 1 != source 2" : null);
        Assert.Equal(offered, RepairRules.Offer(phase, report, cardPresent, pending));
        Assert.False(RepairRules.Offer(JobPhase.Done, null, true, false));
    }

    [Fact]
    public void Title_AndSameBackup()
    {
        Assert.Equal("A001 · oprava", RepairRules.Title("A001"));
        Assert.True(RepairRules.SameBackup([@"D:\A\2026-10-07\A001\"], [@"F:\B\2026-10-07\A001", @"d:\a\2026-10-07\a001"]));
        Assert.False(RepairRules.SameBackup([@"D:\A\2026-10-07\A001"], [@"D:\A\2026-10-07\A001_2"]));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~RepairJobTests"`

Expected: FAIL — build selže — `CS0117: JobConfig neobsahuje definici pro RepairInto` (a `RepairRules`, `JobConfigs.Repair`).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/Desktop/JobConfigs.cs`

Najít (1/1):

```csharp
        };
    }

    /// Spec 2.2: only the new files, into the card folders of the earlier backup — with the profile of the project that
    /// backup was made in (AppendRules.ProfileFor), recording that project even when it has been deleted since.
    public static JobConfig Append(AppSettings s, string source, HistoryEntry entry)
```

Nahradit:

```csharp
        };
    }

    /// "Zkopírovat znovu" (UI návrh 4) after `original` ended with failed files: only those, only where they failed, into
    /// the same card folders — with the job's own copy of the settings (its project, outputs and notifications; a switch
    /// of project since changes nothing). Always verified; no clip analysis or proxies (the clips were analysed with the
    /// backup).
    public static JobConfig Repair(JobConfig original, OffloadPlan plan) =>
        new(original.Source, plan.DestRoots.ToList())
        {
            AllowSameDevice = true,   // the destinations are fixed by the backup
            RepairInto = plan.CardPaths,
            Verify = true,
            AnalyzeClips = false,
            Proxy = new ProxySettings(),
            Tools = SettingsStore.Clone(original.Tools),
            Outputs = SettingsStore.Clone(original.Outputs),
            Project = plan.Project ?? original.Project,
            Notify = SettingsStore.Clone(original.Notify),
            RecordHistory = original.RecordHistory,
            HistoryFolder = original.HistoryFolder,
        };

    /// Spec 2.2: only the new files, into the card folders of the earlier backup — with the profile of the project that
    /// backup was made in (AppendRules.ProfileFor), recording that project even when it has been deleted since.
    public static JobConfig Append(AppSettings s, string source, HistoryEntry entry)
```

#### `windows/src/Offload.Core/Desktop/RepairRules.cs` (nový soubor)

```csharp
namespace Offload.Core.Desktop;

/// "Zkopírovat znovu" on a job card (UI návrh 4), without a window.
public static class RepairRules
{
    /// Offered on a job that ended (not cancelled) with files that are not on every destination — while the card it was
    /// copied from is in its drive (a folder is always there) and no repair of the same card folders is queued or running.
    public static bool Offer(JobPhase phase, OffloadReport? report, bool cardPresent, bool repairPending) =>
        phase is JobPhase.Done or JobPhase.Failed && report is { Success: false } r && r.FailedFiles.Count > 0 && cardPresent && !repairPending;

    /// The job card's title: "A001 · oprava".
    public static string Title(string cardName) => $"{cardName} · oprava";

    /// Two jobs work on the same backup: any card folder in common (any case, trailing slash).
    public static bool SameBackup(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Any(x => b.Any(y => string.Equals(x.TrimEnd('\\'), y.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)));
}
```

#### `windows/src/Offload.Core/History/HistoryEntry.cs`

Najít (1/1):

```csharp
{
    public const string Full = "full";
    public const string Append = "append";

    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = Full;
```

Nahradit:

```csharp
{
    public const string Full = "full";
    public const string Append = "append";
    /// "Zkopírovat znovu": the files that failed, copied again where they failed (Windows from 0.5.0).
    public const string Repair = "repair";

    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = Full;
```

#### `windows/src/Offload.Core/OffloadJob.cs`

Najít (1/6):

```csharp
    public OutputSettings Outputs { get; set; } = new();
    /// The project the job was started in (spec 4.1), null = "Bez projektu": into history.json and the report.
    public string? Project { get; set; }
    /// Where the end of the job is announced (spec 4.3): the copy taken when the job was started.
    public Notify.NotifySettings Notify { get; set; } = new();
}
```

Nahradit:

```csharp
    public OutputSettings Outputs { get; set; } = new();
    /// The project the job was started in (spec 4.1), null = "Bez projektu": into history.json and the report.
    public string? Project { get; set; }
    /// "Zkopírovat znovu" (UI návrh 4): repair the files that failed in these card folders (`<root>\<date>\<card>` of the
    /// backup), each only where it failed (RepairPlanner); DestRoots, CardName and Date are taken from them.
    public IReadOnlyList<string>? RepairInto { get; set; }
    /// Where the end of the job is announced (spec 4.3): the copy taken when the job was started.
    public Notify.NotifySettings Notify { get; set; } = new();
}
```

Najít (2/6):

```csharp
    public OffloadPlan Prepare()
    {
        if (Plan is { } done) return done;
        if (Config.AppendInto is { Count: > 0 } into)
        {
            var a = appendPlan = Engine.PlanAppend(Config.Source, into);
```

Nahradit:

```csharp
    public OffloadPlan Prepare()
    {
        if (Plan is { } done) return done;
        if (Config.RepairInto is { Count: > 0 } broken)
        {
            var repair = Engine.PrepareRepair(Engine.PlanRepair(Config.Source, broken));
            repair.Project = EarlierProject(broken, Config.Project);   // the backup's project, as an append (M4)
            return Plan = repair;
        }
        if (Config.AppendInto is { Count: > 0 } into)
        {
            var a = appendPlan = Engine.PlanAppend(Config.Source, into);
```

Najít (3/6):

```csharp
            // An append holds its card folders until stage 1 ends (fix round 1, item 1) and plans under that hold — a
            // plan made before (the CLI prints one first) is made again, another append may have finished meanwhile.
            // Released in `finally`, so the NothingNew path below still holds them while it heals the hash list.
            folders = Config.AppendInto is { Count: > 0 } into ? AppendLock.Acquire(into) : null;
            if (folders != null)
            {
                Plan = null;
```

Nahradit:

```csharp
            // An append holds its card folders until stage 1 ends (fix round 1, item 1) and plans under that hold — a
            // plan made before (the CLI prints one first) is made again, another append may have finished meanwhile.
            // Released in `finally`, so the NothingNew path below still holds them while it heals the hash list.
            // A repair takes the same lock: an append and a repair never number the same generation.
            folders = (Config.RepairInto ?? Config.AppendInto) is { Count: > 0 } into ? AppendLock.Acquire(into) : null;
            if (folders != null)
            {
                Plan = null;
```

Najít (4/6):

```csharp
                Update(s => s with { Phase = JobPhase.Cancelled });
                return cancelled;
            }
            RecordHistory(plan, report, log);
            if (!Config.AnalyzeClips && !Config.Proxy.Enabled)
            {
                WriteClipLists(plan, log);   // the clips found in the files, without metadata (spec 3.3)
                WritePdfReports(report, plan, log);
```

Nahradit:

```csharp
                Update(s => s with { Phase = JobPhase.Cancelled });
                return cancelled;
            }
            if (plan.Mode == PlanMode.Repair) RecordRepair(plan, report, log);
            else RecordHistory(plan, report, log);
            // A repair rewrites files whose clips were analysed with the backup: no stage 2, only its PDF and the lists.
            if (plan.Mode == PlanMode.Repair || (!Config.AnalyzeClips && !Config.Proxy.Enabled))
            {
                WriteClipLists(plan, log);   // the clips found in the files, without metadata (spec 3.3)
                WritePdfReports(report, plan, log);
```

Najít (5/6):

```csharp
            Update(s => s with { Phase = JobPhase.Analyzing });
            return true;
        }
        catch (OffloadError e) when (e.Code == PreflightCode.NothingNew)
        {
            // Not an error: the earlier backup already has every file of the card — history still learns the card as it is.
```

Nahradit:

```csharp
            Update(s => s with { Phase = JobPhase.Analyzing });
            return true;
        }
        catch (OffloadError e) when (e.Code == PreflightCode.RepairNothing)
        {
            // Not an error: every file is good everywhere (another repair or an append got there first).
            Update(s => s with { Phase = JobPhase.Done, Note = e.Message });
            return false;
        }
        catch (OffloadError e) when (e.Code == PreflightCode.NothingNew)
        {
            // Not an error: the earlier backup already has every file of the card — history still learns the card as it is.
```

Najít (6/6):

```csharp
        }
    }

    /// "Nic nového" (minors M17): the entry of that backup learns the card as it is now — no new generation.
    void RefreshHistory(AppendPlan a, Action<string>? log)
    {
```

Nahradit:

```csharp
        }
    }

    /// A repair adds its generation to the entry of the card folders it repaired; whether the card is now backed up is
    /// read from the reports on the destinations (R4) — every failed path of every folder — so a repair that fixed the
    /// last file makes the entry good again. The card's files, fingerprint and reels stay as they were (nothing new).
    void RecordRepair(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        try
        {
            var store = Store(log);
            var cardPaths = plan.CardPaths;
            if (EntryOf(store, cardPaths) is not { } found)
            {
                log?.Invoke("history: the repaired backup is not in history — nothing recorded");
                return;
            }
            int failed = cardPaths.SelectMany(p => CardRecord.Load(p).FailedFiles).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            store.Update(found.Id, existing =>
            {
                if (existing == null) return null;
                existing.Finished = report.Finished;
                existing.Success = failed == 0;
                existing.FailedFiles = failed == 0 ? null : failed;
                existing.Verified &= report.Verified;
                existing.Generations.RemoveAll(g => g.Seq == plan.Generation);
                existing.Generations.Add(new HistoryGeneration
                {
                    Seq = plan.Generation, Kind = HistoryGeneration.Repair, Finished = report.Finished,
                    Files = plan.Scan.Files.Count, Bytes = plan.Scan.TotalBytes, Verified = report.Verified,
                });
                return existing;
            });
        }
        catch (Exception e)
        {
            log?.Invoke($"history: {e.Message}");
        }
    }

    /// "Nic nového" (minors M17): the entry of that backup learns the card as it is now — no new generation.
    void RefreshHistory(AppendPlan a, Action<string>? log)
    {
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~RepairJobTests"`

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

- Úkol 4 — oprava v jádru („Zkopírovat znovu“, nová věc 4): `RepairPlanner` vezme z reportů na cílech soubory, jejichž nejnovější záznam selhal, a naplánuje je jen na cíle, kde selhaly — dobrá kopie se nikdy nepřepíše, jen se v téže generaci znovu přečte a porovná se zaznamenaným hashem; kontroly dřívější zálohy sdílí s doplněním (`AppendPlanner.CheckBackup`: složky, reporty, řetěz ASC MHL, stejné generace); soubor na kartě musí mít velikost a čas jako v záznamu (jinak „Oprava zastavena — nic se nezkopírovalo…“), chybějící dobrá kopie zastaví opravu jako doplnění; nesouhlasná kopie se před novou přesune do `_REPORT\neshoda_NNNN\<cesta>` (nic se nemaže ani nepřepisuje, obsazené místo = odmítnutí); engine: `PlanMode.Repair`, maska zápisu po cílech (`OffloadPlan.Write`, `DestWriter.Keep`), očekávané hashe (karta, která se teď čte jinak = „card differs from the backup“, soubor zůstane chybný), místo a FAT32 jen tam, kam se píše, `ProgressSnapshot.PlannedBytes`; report generace N `kind: "repair"`, `files[].rewritten`, `setAside`; ASC MHL generace N na všech cílech (přečtená dobrá kopie `verified`, nová `original`, komentář „Offload oprava: …“), seznam `.xxh64` bez opakovaných řádků, HTML „oprava (generace N)“; generace bez analýzy klipů už v `CardRecord` nepřepíše analyzovaný klip; po opravě jde doplnění jako generace N+1
- Úkol 3 — pozastavení celé úlohy: `OffloadJob.Pause/Resume/IsPaused`, `JobStatus.Paused` / `PauseHeld` / `PausedTotal` (jen u běžící úlohy); 2. fáze se zastaví mezi klipy (rozpracovaný klip analýzy nebo proxy doběhne, ffmpeg se nikdy neuspává, takže hlídač zaseknutí pauzu nevidí — test s limitem 2 s a pauzou 3 s) a před PDF a denním reportem; zámek dne ani historie se během pauzy nedrží (test), zámek složek doplnění zůstává (test); zrušení během pauzy (i zavření aplikace) skončí hned jako Zrušeno; `JobProgress`: během pauzy rychlost 0, bez odhadu a bez „Úzkého hrdla“ (`BrakeMeter.Reset`), po Pokračovat se měří od obnovení, texty „Pozastavuje se“ / „Pozastaví se po proxy klipu A003…“ / „Pozastaveno“; `OffloadOptions.ProxyStallTimeout` (jen testy)
- Úkol 2 — pozastavení v jádru, 1. fáze (nová věc 9): `PauseGate` (Pozastavit / Pokračovat, čas pozastavení s falešnými hodinami v testech); kopie čeká před každým souborem a před každým 16MB blokem (zapisovače doběhnou a stojí, soubory `.offload-part` zůstanou otevřené), zpětné čtení před každým souborem a blokem (`Hashing.HashFile(…, beforeBlock)`); `ProgressSnapshot.Paused` / `PauseHeld`; zrušení během pauzy skončí hned jako zrušení (rozpracované soubory se smažou); disk odpojený během pauzy = chyba zápisu po Pokračovat jen na tom cíli; report `copyPausedSeconds` / `verifyPausedSeconds` (chybí, když se nepozastavovalo), rychlosti v TXT, HTML a na kartě bez pauzy, TXT řádek `Paused:`, HTML „pozastaveno · mimo rychlosti“
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 5 — oprava jako úloha: `JobConfig.RepairInto`, `JobConfigs.Repair` (vlastní kopie nastavení úlohy, která selhala — projekt, výstupy, oznámení; vždy s ověřením, bez analýzy a proxy); úloha drží stejný zámek složek jako doplnění (doplnění a oprava téže zálohy nikdy naráz, „Do této zálohy právě doplňuje jiná úloha…“), zapíše report, PDF, seznamy klipů a denní report; historie: k záznamu zálohy přidá generaci `kind: "repair"` a úspěch spočítá z reportů na cílech (poslední opravený soubor = záznam zase „ověřeno“), soubory, otisk a cívky karty se nemění; „Nic k opravě — všechny soubory jsou na všech cílech v pořádku.“ = Hotovo; `RepairRules.Offer` (jen hotová nebo selhaná, ne zrušená úloha s chybnými soubory, karta ve čtečce, žádná oprava téže zálohy ve frontě), titulek „A001 · oprava“
- Úkol 4 — oprava v jádru („Zkopírovat znovu“, nová věc 4): `RepairPlanner` vezme z reportů na cílech soubory, jejichž nejnovější záznam selhal, a naplánuje je jen na cíle, kde selhaly — dobrá kopie se nikdy nepřepíše, jen se v téže generaci znovu přečte a porovná se zaznamenaným hashem; kontroly dřívější zálohy sdílí s doplněním (`AppendPlanner.CheckBackup`: složky, reporty, řetěz ASC MHL, stejné generace); soubor na kartě musí mít velikost a čas jako v záznamu (jinak „Oprava zastavena — nic se nezkopírovalo…“), chybějící dobrá kopie zastaví opravu jako doplnění; nesouhlasná kopie se před novou přesune do `_REPORT\neshoda_NNNN\<cesta>` (nic se nemaže ani nepřepisuje, obsazené místo = odmítnutí); engine: `PlanMode.Repair`, maska zápisu po cílech (`OffloadPlan.Write`, `DestWriter.Keep`), očekávané hashe (karta, která se teď čte jinak = „card differs from the backup“, soubor zůstane chybný), místo a FAT32 jen tam, kam se píše, `ProgressSnapshot.PlannedBytes`; report generace N `kind: "repair"`, `files[].rewritten`, `setAside`; ASC MHL generace N na všech cílech (přečtená dobrá kopie `verified`, nová `original`, komentář „Offload oprava: …“), seznam `.xxh64` bez opakovaných řádků, HTML „oprava (generace N)“; generace bez analýzy klipů už v `CardRecord` nepřepíše analyzovaný klip; po opravě jde doplnění jako generace N+1
- Úkol 3 — pozastavení celé úlohy: `OffloadJob.Pause/Resume/IsPaused`, `JobStatus.Paused` / `PauseHeld` / `PausedTotal` (jen u běžící úlohy); 2. fáze se zastaví mezi klipy (rozpracovaný klip analýzy nebo proxy doběhne, ffmpeg se nikdy neuspává, takže hlídač zaseknutí pauzu nevidí — test s limitem 2 s a pauzou 3 s) a před PDF a denním reportem; zámek dne ani historie se během pauzy nedrží (test), zámek složek doplnění zůstává (test); zrušení během pauzy (i zavření aplikace) skončí hned jako Zrušeno; `JobProgress`: během pauzy rychlost 0, bez odhadu a bez „Úzkého hrdla“ (`BrakeMeter.Reset`), po Pokračovat se měří od obnovení, texty „Pozastavuje se“ / „Pozastaví se po proxy klipu A003…“ / „Pozastaveno“; `OffloadOptions.ProxyStallTimeout` (jen testy)
- Úkol 2 — pozastavení v jádru, 1. fáze (nová věc 9): `PauseGate` (Pozastavit / Pokračovat, čas pozastavení s falešnými hodinami v testech); kopie čeká před každým souborem a před každým 16MB blokem (zapisovače doběhnou a stojí, soubory `.offload-part` zůstanou otevřené), zpětné čtení před každým souborem a blokem (`Hashing.HashFile(…, beforeBlock)`); `ProgressSnapshot.Paused` / `PauseHeld`; zrušení během pauzy skončí hned jako zrušení (rozpracované soubory se smažou); disk odpojený během pauzy = chyba zápisu po Pokračovat jen na tom cíli; report `copyPausedSeconds` / `verifyPausedSeconds` (chybí, když se nepozastavovalo), rychlosti v TXT, HTML a na kartě bez pauzy, TXT řádek `Paused:`, HTML „pozastaveno · mimo rychlosti“
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: repair as a queue job, history generation "repair" (plan 9, task 5)

The repair job takes its own copy of the finished job's settings, holds the same folder lock as an
append, writes report, PDF, clip lists and the day report, and adds a "repair" generation to the
history entry with success recomputed from the reports on the destinations.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Desktop\JobConfigs.cs windows\src\Offload.Core\Desktop\RepairRules.cs windows\src\Offload.Core\History\HistoryEntry.cs windows\src\Offload.Core\OffloadJob.cs windows\tests\Offload.Core.Tests\RepairJobTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 6: Data nové karty úlohy v jádru (nové věci 1–3)

Vše, co nová karta úlohy ukazuje, spočítá `JobCard` bez okna: tabulka rychlostí po cílech, ukazatel fází, řádek
„Soubor 10 / 24“, souhrn Kopie · Ověření · Proxy · Celkem (bez pauz), seznam nesouhlasných souborů po discích, titulky
karet podle stavu, náhledy a věta „Nejpomalejší článek“. `QueueSummary` dá pilulku horní lišty a popisek tlačítka
Start. `Format.BytesCs3` píše velikosti na tři platné číslice jako mockup.

**Files:**
- Create: `windows/src/Offload.Core/Desktop/JobCard.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.Core/Desktop/JobProgress.cs`
- Modify: `windows/src/Offload.Core/Format.cs`
- Test: `windows/tests/Offload.Core.Tests/JobCardTests.cs` (nový)

**Interfaces:**
- Consumes: `JobProgress`, `JobStatus` (úkol 3), `OffloadReport` (úkoly 2, 4), `ChainElement`, `Brake` (stávající HW diagnostika).
- Produces (`Offload.Core.Desktop`):
  - `enum JobCardKind { Waiting, Running, Done, Problem, Failed, Cancelled }`, `enum StepState { Done, Active, Pending }`.
  - `record TransferRow(string Name, int? Number, double Share, string RateText)`, `record PhaseStep(int Number, string Label, StepState State, double Fill)` (+ `Text`), `record SummaryField(string Label, string Value)`, `record FileOnDisk(string Name, bool Ok, string Text)`, `record FailedFileRow(string Path, IReadOnlyList<FileOnDisk> Disks)`, `record CardHeadline(JobCardKind Kind, string Title, string Sub)`, `record JobCardFacts(int Files, int Clips, long Bytes, string? Camera, bool Append)`.
  - `static class JobCard` — `DiskName(string root)`, `Rate(double bytesPerSecond, bool paused = false)`, `Sub(JobCardFacts)`, `Rows(JobProgress p, IReadOnlyList<string> destNames)`, `Steps(JobStatus s, JobConfig config, double phaseProgress)`, `FileLine(JobStatus s)` → `(string Caption, string Value)`, `Remaining(JobPhase, double? phaseSeconds, double? totalSeconds)`, `Duration(TimeSpan)`, `Summary(OffloadReport r, TimeSpan? elapsed)`, `Problem(string state, string? error)`, `FailedRows(OffloadReport r, IReadOnlyList<string> destNames)` (cesty s `\`), `MoreFiles(int total, int shown)`, `Headline(string card, JobStatus s, OffloadReport? r, IReadOnlyList<string> destNames)`, `SafeText(int files, int disks, string hash)`, `ProblemText(OffloadReport, IReadOnlyList<string>)`, `Thumbs(OffloadReport r, int max = 5)` → `(IReadOnlyList<(string Clip, byte[] Jpeg)> Thumbs, int More)`, `BrakeSentence(Brake?, IReadOnlyList<ChainElement>, IReadOnlyList<string>)`.
  - `static class QueueSummary` — `Count(IEnumerable<(JobPhase Phase, bool Paused)>)` → `(int Running, int Paused, int Waiting)`, `Pill(int running, int paused, int waiting)`, `StartLabel(IEnumerable<JobPhase>)`.
  - `JobProgress.Elapsed` (`TimeSpan?`, bez pauz; null, když úloha nikdy neběžela); `Format.BytesCs3(long)`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/JobCardTests.cs` (nový soubor)

```csharp
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

/// The new job card (UI návrh, nové věci 1–4): speed table, phase indicator, summary fields, failed files, headlines.
public class JobCardTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    static readonly string[] Disks = ["RAID_A", "SHUTTLE_B"];

    static ProgressSnapshot Snap(CopyPhase phase, long read, long[] written, long[] verified, long[]? planned = null, int file = 9, bool paused = false) =>
        new(phase, 1_000_000_000, 24, file, "XDROOT/Clip/A035C010_261007_R2KQ.mxf", read, written, verified, 0, paused, paused, planned);

    [Fact]
    public void Sizes_ThreeSignificantDigits()
    {
        Assert.Equal("1,70 GB", Format.BytesCs3(1_700_000_000));
        Assert.Equal("14,2 GB", Format.BytesCs3(14_200_000_000));
        Assert.Equal("412 GB", Format.BytesCs3(412_000_000_000));
        Assert.Equal("7,20 TB", Format.BytesCs3(7_200_000_000_000));
        Assert.Equal("1,00 GB", Format.BytesCs3(999_600_000));
        Assert.Equal("850 B", Format.BytesCs3(850));
    }

    [Fact]
    public void Sub_ClipsSizeCamera_AppendSameDestinations()
    {
        Assert.Equal("24 klipů · 1,70 GB · ALEXA 35", JobCard.Sub(new JobCardFacts(30, 24, 1_700_000_000, "ALEXA 35", false)));
        Assert.Equal("2 nové klipy · 14,2 GB · stejné cíle", JobCard.Sub(new JobCardFacts(4, 2, 14_200_000_000, "FX6", true)));
        Assert.Equal("3 soubory · 850 MB", JobCard.Sub(new JobCardFacts(3, 0, 850_000_000, null, false)));
    }

    [Fact]
    public void Rows_Copying_CardThenEveryDestination()
    {
        var p = new JobProgress(T0);
        p.Update(new JobStatus { Phase = JobPhase.Copying, Copy = Snap(CopyPhase.Copy, 0, [0, 0], [0, 0]) }, T0.AddSeconds(0.5));
        p.Update(new JobStatus { Phase = JobPhase.Copying, Copy = Snap(CopyPhase.Copy, 380_000_000, [380_000_000, 370_000_000], [0, 0]) }, T0.AddSeconds(1.5));

        var rows = JobCard.Rows(p, Disks);

        Assert.Equal(["Čtení karty", "RAID_A", "SHUTTLE_B"], rows.Select(r => r.Name));
        Assert.Equal([null, 1, 2], rows.Select(r => r.Number));
        Assert.Equal([0.38, 0.38, 0.37], rows.Select(r => Math.Round(r.Share, 2)));
        Assert.Equal(["380 MB/s", "380 MB/s", "370 MB/s"], rows.Select(r => r.RateText));
    }

    [Fact]
    public void Rows_Verifying_NoCardRow_Paused_Dash_RepairReadBackOnly()
    {
        var p = new JobProgress(T0);
        p.Update(new JobStatus { Phase = JobPhase.Verifying, Copy = Snap(CopyPhase.Verify, 1_000_000_000, [1_000_000_000, 1_000_000_000], [500_000_000, 250_000_000]) }, T0);
        Assert.Equal(["RAID_A", "SHUTTLE_B"], JobCard.Rows(p, Disks).Select(r => r.Name));
        Assert.Equal([0.5, 0.25], JobCard.Rows(p, Disks).Select(r => r.Share));

        p.Update(new JobStatus { Phase = JobPhase.Copying, Paused = true, Copy = Snap(CopyPhase.Copy, 10, [10, 10], [0, 0], paused: true) }, T0.AddSeconds(2));
        Assert.All(JobCard.Rows(p, Disks), r => Assert.Equal("—", r.RateText));

        // A repair: RAID_A only reads its good copy back — nothing to write there.
        var repair = new JobProgress(T0);
        repair.Update(new JobStatus { Phase = JobPhase.Copying, Copy = Snap(CopyPhase.Copy, 30_000, [0, 30_000], [0, 0], planned: [0, 60_000]) }, T0);
        var rows = JobCard.Rows(repair, Disks);
        Assert.Equal((1.0, "jen ověření"), (rows[1].Share, rows[1].RateText));
        Assert.Equal(0.5, rows[2].Share);
        Assert.Empty(JobCard.Rows(new JobProgress(T0), Disks));
    }

    static JobConfig Config(bool verify = true, bool analyze = true, bool proxy = true) =>
        new(@"E:\", [@"D:\RAID_A"]) { Verify = verify, AnalyzeClips = analyze, Proxy = new ProxySettings { Enabled = proxy } };

    static string Steps(JobStatus s, JobConfig c, double progress = 0.38) =>
        string.Join(" | ", JobCard.Steps(s, c, progress).Select(x => $"{x.Text}:{x.State}:{x.Fill.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"));

    [Fact]
    public void Steps_FromThePhase_OnlyThePhasesThisJobDoes()
    {
        var copying = new JobStatus { Phase = JobPhase.Copying };
        Assert.Equal("1 · Kopírování:Active:0.38 | 2 · Ověření:Pending:0 | 3 · Proxy:Pending:0 | 4 · Report:Pending:0", Steps(copying, Config()));
        Assert.Equal("1 · Kopírování:Active:0.38 | 2 · Ověření:Pending:0 | 3 · Analýza:Pending:0 | 4 · Report:Pending:0", Steps(copying, Config(proxy: false)));
        Assert.Equal("1 · Kopírování:Active:0.38 | 2 · Report:Pending:0", Steps(copying, Config(verify: false, analyze: false, proxy: false)));
        Assert.Equal("1 · Kopírování:Active:0.38 | 2 · Ověření:Pending:0 | 3 · Proxy:Pending:0 | 4 · Report:Pending:0",
            Steps(new JobStatus { Phase = JobPhase.Flushing }, Config()));
        Assert.Equal("1 · Kopírování:Done:1 | 2 · Ověření:Active:0.38 | 3 · Proxy:Pending:0 | 4 · Report:Pending:0",
            Steps(new JobStatus { Phase = JobPhase.Verifying }, Config()));
        // The first report at the end of stage 1 belongs to the copy, not to step 4.
        Assert.Equal("1 · Kopírování:Done:1 | 2 · Ověření:Active:1 | 3 · Proxy:Pending:0 | 4 · Report:Pending:0",
            Steps(new JobStatus { Phase = JobPhase.Reporting }, Config()));
        Assert.Equal("1 · Kopírování:Done:1 | 2 · Ověření:Done:1 | 3 · Proxy:Active:0.38 | 4 · Report:Pending:0",
            Steps(new JobStatus { Phase = JobPhase.Analyzing, CopySucceeded = true }, Config()));
        Assert.Equal("1 · Kopírování:Done:1 | 2 · Ověření:Done:1 | 3 · Proxy:Done:1 | 4 · Report:Active:0.38",
            Steps(new JobStatus { Phase = JobPhase.Reporting, CopySucceeded = true }, Config()));
        Assert.Equal("1 · Kopírování:Done:1 | 2 · Ověření:Done:1 | 3 · Proxy:Done:1 | 4 · Report:Done:1", Steps(new JobStatus { Phase = JobPhase.Done }, Config()));
        Assert.Equal("1 · Kopírování:Pending:0 | 2 · Ověření:Pending:0 | 3 · Proxy:Pending:0 | 4 · Report:Pending:0", Steps(new JobStatus(), Config()));
        Assert.Empty(JobCard.Steps(new JobStatus { Phase = JobPhase.Failed }, Config(), 0));
        // A repair: no stage 2.
        var repair = Config();
        repair.RepairInto = [@"D:\RAID_A\2026-10-07\A001"];
        Assert.Equal("1 · Kopírování:Active:0.38 | 2 · Ověření:Pending:0 | 3 · Report:Pending:0", Steps(copying, repair));
    }

    [Fact]
    public void FileLine_And_Remaining()
    {
        Assert.Equal(("Soubor 10 / 24", "A035C010_261007_R2KQ.mxf"),
            JobCard.FileLine(new JobStatus { Phase = JobPhase.Copying, Copy = Snap(CopyPhase.Copy, 0, [0], [0]) }));
        Assert.Equal(("Ověřování", "500 MB / 2,00 GB"),
            JobCard.FileLine(new JobStatus { Phase = JobPhase.Verifying, Copy = Snap(CopyPhase.Verify, 0, [0, 0], [250_000_000, 250_000_000]) }));
        Assert.Equal(("Proxy 3 / 24", "A035C003 · 43 %"),
            JobCard.FileLine(new JobStatus { Phase = JobPhase.Proxies, ClipIndex = 2, ClipCount = 24, ClipName = "A035C003", ClipFraction = 0.426 }));

        Assert.Equal("zbývá ~0:02 kopie · ~6:00 celkem", JobCard.Remaining(JobPhase.Copying, 2, 360));
        Assert.Equal("zbývá ~0:09 ověření", JobCard.Remaining(JobPhase.Verifying, 9, null));
        Assert.Equal("zbývá ~1:00 celkem", JobCard.Remaining(JobPhase.Proxies, null, 60));
        Assert.Null(JobCard.Remaining(JobPhase.Analyzing, null, null));
        Assert.Equal(["45 s", "6 min 12 s", "1 h 05 min"],
            new[] { 45, 372, 3900 }.Select(s => JobCard.Duration(TimeSpan.FromSeconds(s))));
    }

    static OffloadReport Report(bool verified = true) => new()
    {
        CardName = "A035_ARRI", Date = "2026-10-07", Source = @"E:\", SourceFilesystem = "exFAT",
        Started = T0, CopyFinished = T0.AddSeconds(10), Finished = T0.AddSeconds(15), Verified = verified,
        TotalFiles = 2, TotalBytes = 6_020_000_000,
        Destinations =
        [
            new ReportDestination { Root = @"D:\RAID_A", CardPath = @"D:\RAID_A\2026-10-07\A035_ARRI", Filesystem = "NTFS" },
            new ReportDestination { Root = @"F:\SHUTTLE_B", CardPath = @"F:\SHUTTLE_B\2026-10-07\A035_ARRI", Filesystem = "exFAT" },
        ],
        Files = [], Skipped = [],
    };

    static ReportFile File(string path, string d1, string d2, string? e2 = null, string? source = null) =>
        new() { Path = path, Size = 1, Destinations = [d1, d2], Errors = [null, e2], SourceError = source };

    [Fact]
    public void Summary_FourFields()
    {
        var r = Report();
        r.Proxy = new ProxySettings { Enabled = true };
        r.Clips =
        [
            new ClipEntry { Clip = new Clip { Name = "C001", Path = "C001.mxf", Segments = ["C001.mxf"] }, ProxyStatus = ProxyStatus.Done },
            new ClipEntry { Clip = new Clip { Name = "C002", Path = "C002.mxf", Segments = ["C002.mxf"] }, ProxyStatus = ProxyStatus.Failed },
            new ClipEntry { Clip = new Clip { Name = "S001", Path = "SUB/S001.mp4", Segments = ["SUB/S001.mp4"] }, ProxyStatus = ProxyStatus.Skipped },
        ];
        Assert.Equal([("Kopie", "602 MB/s"), ("Ověření", "1204 MB/s"), ("Proxy", "1 / 2"), ("Celkem", "6 min 12 s")],
            JobCard.Summary(r, TimeSpan.FromSeconds(372)).Select(f => (f.Label, f.Value)));
        var plain = Report(verified: false);
        Assert.Equal([("Kopie", "602 MB/s"), ("Ověření", "vypnuto"), ("Proxy", "vypnuto"), ("Celkem", "15 s")],
            JobCard.Summary(plain, null).Select(f => (f.Label, f.Value)));
    }

    [Fact]
    public void FailedRows_PerDestination_WhyInTwoWords()
    {
        var r = Report();
        var report = new OffloadReport
        {
            CardName = r.CardName, Date = r.Date, Source = r.Source, SourceFilesystem = r.SourceFilesystem, Verified = true,
            Destinations = r.Destinations, Skipped = [],
            Files =
            [
                File("XDROOT/Clip/B012C001.MXF", "verified", "verified"),
                File("XDROOT/Clip/B012C015_261007_9QW1.MXF", "verified", "failed", "HASH MISMATCH: dest 1 != source 2"),
                File("XDROOT/Clip/B012C016.MXF", "verified", "failed", "Zařízení není připojeno."),
                File("XDROOT/Clip/B012C017.MXF", "failed", "failed", "source: read failed", source: "read failed"),
            ],
        };

        var rows = JobCard.FailedRows(report, Disks);

        Assert.Equal(3, rows.Count);
        Assert.Equal(@"XDROOT\Clip\B012C015_261007_9QW1.MXF", rows[0].Path);
        Assert.Equal(["RAID_A ✓", "SHUTTLE_B ✕ hash"], rows[0].Disks.Select(d => d.Text));
        Assert.Equal(["RAID_A ✓", "SHUTTLE_B ✕ zápis"], rows[1].Disks.Select(d => d.Text));
        Assert.Equal(["RAID_A ✕ karta", "SHUTTLE_B ✕ karta"], rows[2].Disks.Select(d => d.Text));
        Assert.Equal(["hash", "velikost", "čtení", "zrušeno", "karta", "zápis", "nezkopírováno"],
            new[] { ("failed", "MD5 MISMATCH: x"), ("failed", "size mismatch: 1 != 2"), ("failed", "verify: gone"), ("failed", "source: cancelled"),
                    ("failed", "source: read failed"), ("failed", "boom"), ("pending", (string?)null) }.Select(x => JobCard.Problem(x.Item1, x.Item2)));
        Assert.Equal("… a další 3 soubory v reportu", JobCard.MoreFiles(8, 5));
        Assert.Null(JobCard.MoreFiles(5, 5));
    }

    [Fact]
    public void Headlines_EveryState()
    {
        var ok = Report();
        ok = WithFiles(ok, File("a", "verified", "verified"), File("b", "verified", "verified"));
        var h = JobCard.Headline("A035_ARRI", new JobStatus { Phase = JobPhase.Done, SafeToEject = true, CopySucceeded = true }, ok, Disks);
        Assert.Equal((JobCardKind.Done, "A035_ARRI — kartu lze vysunout", "Všechny 2 soubory jsou na 2 discích a ověřené (xxHash64)."), (h.Kind, h.Title, h.Sub));

        var bad = WithFiles(Report(), File("a", "verified", "verified"), File("XDROOT/Clip/B012C015.MXF", "verified", "failed", "HASH MISMATCH: 1 != 2"));
        h = JobCard.Headline("B012_FX6", new JobStatus { Phase = JobPhase.Done, CopySucceeded = false }, bad, Disks);
        Assert.Equal((JobCardKind.Problem, "B012_FX6 — kartu zatím nevysunujte", "1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku."),
            (h.Kind, h.Title, h.Sub));

        var unverified = WithFiles(Report(verified: false), File("a", "written", "written"));
        Assert.Equal(JobCardKind.Problem, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Done, CopySucceeded = true }, unverified, Disks).Kind);
        Assert.Equal(("A — offload selhal", "Cíl nenalezen: D:\\"),
            JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, Error = "Cíl nenalezen: D:\\" }, null, Disks) is var f ? (f.Title, f.Sub) : default);
        Assert.Equal(JobCardKind.Failed, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, Error = "x" }, null, Disks).Kind);
        Assert.Equal((JobCardKind.Done, "A — záloha je úplná", Msg.NothingNew),
            JobCard.Headline("A", new JobStatus { Phase = JobPhase.Done, Note = Msg.NothingNew }, null, Disks) is var n ? (n.Kind, n.Title, n.Sub) : default);
        Assert.Equal(JobCardKind.Cancelled, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Cancelled }, null, Disks).Kind);
        Assert.Equal(JobCardKind.Waiting, JobCard.Headline("A", new JobStatus(), null, Disks).Kind);
        Assert.Equal(JobCardKind.Running, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Proxies }, null, Disks).Kind);
        // Stage 2 failed after a good copy: still safe to eject (the error shows as an orange line).
        Assert.Equal(JobCardKind.Done, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, SafeToEject = true, Error = "x" }, ok, Disks).Kind);
    }

    static OffloadReport WithFiles(OffloadReport r, params ReportFile[] files) => new()
    {
        CardName = r.CardName, Date = r.Date, Source = r.Source, SourceFilesystem = r.SourceFilesystem, Verified = r.Verified,
        Destinations = r.Destinations, Skipped = [], Files = files, TotalFiles = files.Length,
    };

    [Fact]
    public void Texts_Plurals()
    {
        Assert.Equal("Soubor je na 1 disku a je ověřený (xxHash64).", JobCard.SafeText(1, 1, "xxHash64"));
        Assert.Equal("Všech 24 souborů je na 2 discích a ověřených (xxHash64 + MD5).", JobCard.SafeText(24, 2, "xxHash64 + MD5"));
        var two = WithFiles(Report(), File("a", "failed", "verified", "x"), File("b", "verified", "failed", "y"), File("c", "verified", "failed", "z"));
        Assert.Equal("S kartou nesouhlasí: RAID_A 1 soubor, SHUTTLE_B 2 soubory.", JobCard.ProblemText(two, Disks));
        var card = WithFiles(Report(), File("a", "failed", "failed", "source: x", source: "x"));
        Assert.Equal("1 soubor se nepodařilo přečíst z karty.", JobCard.ProblemText(card, Disks));
    }

    [Fact]
    public void Thumbs_FirstFive_MiddleFrame_AndTheRest()
    {
        var r = Report();
        r.Clips = Enumerable.Range(1, 24).Select(i => new ClipEntry
        {
            Clip = new Clip { Name = $"C{i:000}", Path = $"C{i:000}.mxf", Segments = [$"C{i:000}.mxf"] },
            Thumbnails = [new Thumbnail([1], "a"), new Thumbnail([2], "b"), new Thumbnail([3], "c")],
        }).ToList();

        var (thumbs, more) = JobCard.Thumbs(r);

        Assert.Equal(["C001", "C002", "C003", "C004", "C005"], thumbs.Select(t => t.Clip));
        Assert.All(thumbs, t => Assert.Equal([2], t.Jpeg));
        Assert.Equal(19, more);
        Assert.Equal(0, JobCard.Thumbs(Report()).More);
    }

    [Fact]
    public void BrakeSentence_CardOrDisk()
    {
        var chain = ChainView.Build(
            [new RoleLink("Karta", new DriveLink("E:\\", StorageBus.Usb, "reader", new UsbLink("PCI\\HC", "x", "R#5", 5, 5, UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, false))),
             new RoleLink("Cíl 1", new DriveLink("D:\\", StorageBus.Nvme, "SSD", null)),
             new RoleLink("Cíl 2", new DriveLink("F:\\", StorageBus.Sata, "HDD", null))], [400, 400, 400]);
        Assert.Equal("Nejpomalejší článek: čtečka karty (USB 3 (5 Gb/s)). Disky by zvládly víc.", JobCard.BrakeSentence(new Brake(0, 400, 0.5), chain, Disks));
        Assert.Equal("Nejpomalejší článek: čtečka karty (USB 3 (5 Gb/s) — linka je plná). Disky by zvládly víc.", JobCard.BrakeSentence(new Brake(0, 440, 0.97), chain, Disks));
        Assert.Equal("Nejpomalejší článek: disk SHUTTLE_B. Karta by šla číst rychleji.", JobCard.BrakeSentence(new Brake(2, 150, null), chain, Disks));
        Assert.Null(JobCard.BrakeSentence(null, chain, Disks));
    }

    [Fact]
    public void QueuePill_StartLabel()
    {
        var counts = QueueSummary.Count([(JobPhase.Copying, false), (JobPhase.Waiting, false), (JobPhase.Done, false), (JobPhase.Proxies, true)]);
        Assert.Equal((1, 1, 1), counts);
        Assert.Equal("Běží 1 offload · 1 ve frontě", QueueSummary.Pill(1, 0, 1));
        Assert.Equal("Běží 2 offloady · 1 pozastavený · 3 ve frontě", QueueSummary.Pill(2, 1, 3));
        Assert.Equal("Pozastaveno: 1 offload", QueueSummary.Pill(0, 1, 0));
        Assert.Null(QueueSummary.Pill(0, 0, 0));
        Assert.Equal("Zařadit do fronty", QueueSummary.StartLabel([JobPhase.Verifying, JobPhase.Done]));
        Assert.Equal("Spustit offload", QueueSummary.StartLabel([JobPhase.Proxies, JobPhase.Done]));
    }

    [Fact]
    public void Elapsed_WithoutThePauses()
    {
        var p = new JobProgress(T0);
        p.Update(new JobStatus(), T0);
        Assert.Null(p.Elapsed);
        p.Update(new JobStatus { Phase = JobPhase.Copying }, T0.AddSeconds(10));
        p.Update(new JobStatus { Phase = JobPhase.Proxies, PausedTotal = TimeSpan.FromSeconds(30) }, T0.AddSeconds(300));
        Assert.Null(p.Elapsed);
        p.Update(new JobStatus { Phase = JobPhase.Done, PausedTotal = TimeSpan.FromSeconds(30) }, T0.AddSeconds(412));
        Assert.Equal(TimeSpan.FromSeconds(372), p.Elapsed);
        p.Update(new JobStatus { Phase = JobPhase.Done, PausedTotal = TimeSpan.FromSeconds(30) }, T0.AddSeconds(999));
        Assert.Equal(TimeSpan.FromSeconds(372), p.Elapsed);   // the first finished tick counts
        var seenFinished = new JobProgress(T0);
        seenFinished.Update(new JobStatus { Phase = JobPhase.Done }, T0.AddSeconds(5));
        Assert.Null(seenFinished.Elapsed);                     // never seen running: the report's times count
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~JobCardTests"`

Expected: FAIL — build selže — `CS0103: Název JobCard v aktuálním kontextu neexistuje` (a `QueueSummary`, `Format.BytesCs3`, `JobProgress.Elapsed`).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/Desktop/JobCard.cs` (nový soubor)

```csharp
using System.Globalization;
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

/// How a job card looks (UI návrh, obr. 2 a 3): running, waiting, done (green), with failed files (yellow), failed before
/// or without a copy (red), cancelled (grey).
public enum JobCardKind { Waiting, Running, Done, Problem, Failed, Cancelled }

/// One row of the speed table: "Čtení karty" or a destination (with its number), the share done and the speed.
public sealed record TransferRow(string Name, int? Number, double Share, string RateText);

public enum StepState { Done, Active, Pending }

/// One segment of the phase indicator: "1 · Kopírování".
public sealed record PhaseStep(int Number, string Label, StepState State, double Fill)
{
    public string Text => $"{Number} · {Label}";
}

/// One number of a finished card: "Kopie" / "602 MB/s".
public sealed record SummaryField(string Label, string Value);

/// One destination of a file that did not make it: "RAID_A ✓", "SHUTTLE_B ✕ hash".
public sealed record FileOnDisk(string Name, bool Ok, string Text);

public sealed record FailedFileRow(string Path, IReadOnlyList<FileOnDisk> Disks);

/// The big line of a finished card and the sentence under it.
public sealed record CardHeadline(JobCardKind Kind, string Title, string Sub);

/// What the card header says under the name: the files or clips, the size and the camera.
public sealed record JobCardFacts(int Files, int Clips, long Bytes, string? Camera, bool Append);

/// The texts and numbers of the new job card (UI návrh, nové věci 1–4) — pure functions, the window only binds them.
public static class JobCard
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// The name a destination is shown with: its folder's name ("RAID_A"), a drive root as it is ("E:\").
    public static string DiskName(string root) => Path.GetFileName(root.TrimEnd('\\')) is { Length: > 0 } n ? n : root;

    /// "661 MB/s"; a paused job shows a dash.
    public static string Rate(double bytesPerSecond, bool paused = false) =>
        paused ? "—" : (bytesPerSecond / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s";

    /// "24 klipů · 1,70 GB · ALEXA 35", an append "2 nové klipy · 14,2 GB · stejné cíle".
    public static string Sub(JobCardFacts f)
    {
        var what = f.Clips > 0
            ? f.Append ? $"{f.Clips} {Texts.Plural(f.Clips, "nový klip", "nové klipy", "nových klipů")}" : Texts.Clips(f.Clips)
            : f.Append ? $"{f.Files} {Texts.Plural(f.Files, "nový soubor", "nové soubory", "nových souborů")}" : $"{f.Files} {Texts.Plural(f.Files, "soubor", "soubory", "souborů")}";
        var parts = new List<string> { what, Format.BytesCs3(f.Bytes) };
        if (f.Append) parts.Add("stejné cíle");
        else if (f.Camera is { Length: > 0 } camera) parts.Add(camera);
        return string.Join(" · ", parts);
    }

    /// The speed table while the card is read and written or read back: "Čtení karty" (copy only), then every destination.
    public static IReadOnlyList<TransferRow> Rows(JobProgress p, IReadOnlyList<string> destNames)
    {
        var s = p.Status;
        if (s.Copy is not { } c || s.Phase is not (JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying)) return [];
        bool verifying = s.Phase == JobPhase.Verifying;
        var rows = new List<TransferRow>();
        if (!verifying)
            rows.Add(new TransferRow("Čtení karty", null, Share(c.ReadBytes, c.TotalBytes), Rate(p.ReadRate, s.Paused)));
        for (int d = 0; d < destNames.Count; d++)
        {
            long done = verifying ? At(c.VerifiedBytes, d) : At(c.WrittenBytes, d);
            long of = verifying ? c.TotalBytes : c.Planned(d);
            double rate = d < p.DestRates.Length ? p.DestRates[d] : 0;
            // A destination a repair only reads back has nothing to write: full, and no speed while the others copy.
            rows.Add(new TransferRow(destNames[d], d + 1, of == 0 ? 1 : Share(done, of), of == 0 && !verifying ? "jen ověření" : Rate(rate, s.Paused)));
        }
        return rows;
    }

    static long At(long[] values, int i) => i < values.Length ? values[i] : 0;

    static double Share(long done, long of) => of <= 0 ? 0 : Math.Clamp((double)done / of, 0, 1);

    /// The phase indicator: Kopírování · Ověření · Proxy (or Analýza) · Report — the phases this job does, numbered in a
    /// row; done ones full, the active one as far as it got. Empty for a finished job that did not end Done.
    public static IReadOnlyList<PhaseStep> Steps(JobStatus s, JobConfig config, double phaseProgress)
    {
        var labels = new List<string> { "Kopírování" };
        if (config.Verify) labels.Add("Ověření");
        bool stage2 = config.RepairInto == null && (config.Proxy.Enabled || config.AnalyzeClips);
        if (stage2) labels.Add(config.Proxy.Enabled ? "Proxy" : "Analýza");
        labels.Add("Report");
        if (s.Phase is JobPhase.Failed or JobPhase.Cancelled) return [];
        int active = s.Phase switch
        {
            JobPhase.Waiting => -1,
            JobPhase.Copying or JobPhase.Flushing => 0,
            JobPhase.Verifying => 1,
            // The first report is written at the end of stage 1 (before the clips): the copy's last step, full.
            JobPhase.Reporting when s.CopySucceeded == null => config.Verify ? 1 : 0,
            JobPhase.Analyzing or JobPhase.Proxies => stage2 ? labels.Count - 2 : labels.Count - 1,
            JobPhase.Reporting => labels.Count - 1,
            _ => labels.Count,   // Done
        };
        bool stage1Report = s.Phase == JobPhase.Reporting && s.CopySucceeded == null;
        return labels.Select((label, i) =>
        {
            var state = i < active ? StepState.Done : i == active ? StepState.Active : StepState.Pending;
            double fill = state switch
            {
                StepState.Done => 1,
                StepState.Active => stage1Report ? 1 : Math.Clamp(phaseProgress, 0, 1),
                _ => 0,
            };
            return new PhaseStep(i + 1, label, state, fill);
        }).ToList();
    }

    /// "Soubor 10 / 24" + the file's name, "Ověřování" + how much, "Proxy 3 / 24" + the clip — under the speed table.
    public static (string Caption, string Value) FileLine(JobStatus s)
    {
        var c = s.Copy;
        return s.Phase switch
        {
            JobPhase.Copying when c != null => ($"Soubor {Math.Min(c.FileIndex + 1, c.TotalFiles)} / {c.TotalFiles}", Path.GetFileName(c.CurrentFile)),
            JobPhase.Flushing => ("Zápis cache disků", ""),
            JobPhase.Verifying when c != null => ("Ověřování", $"{Format.BytesCs3(c.VerifiedBytes.Sum())} / {Format.BytesCs3(c.TotalBytes * Math.Max(1, c.VerifiedBytes.Length))}"),
            JobPhase.Analyzing => ($"Klip {s.ClipIndex + 1} / {s.ClipCount}", s.ClipName),
            JobPhase.Proxies => ($"Proxy {s.ClipIndex + 1} / {s.ClipCount}", $"{s.ClipName} · {(s.ClipFraction * 100).ToString("0", CultureInfo.InvariantCulture)} %"),
            JobPhase.Reporting => ("Report", ""),
            _ => (s.Phase.Label(), ""),
        };
    }

    /// "zbývá ~0:02 kopie · ~0:06 celkem" — the phase's ETA and, when known, the whole job's; null without an ETA.
    public static string? Remaining(JobPhase phase, double? phaseSeconds, double? totalSeconds)
    {
        if (phaseSeconds is not double t) return totalSeconds is double only ? $"zbývá ~{JobProgress.EtaText(only)} celkem" : null;
        var what = phase switch { JobPhase.Copying => "kopie", JobPhase.Verifying => "ověření", _ => phase.Label().ToLower(Cs) };
        var text = $"zbývá ~{JobProgress.EtaText(t)} {what}";
        return totalSeconds is double total && total > t ? $"{text} · ~{JobProgress.EtaText(total)} celkem" : text;
    }

    /// "6 min 12 s", "45 s", "1 h 05 min".
    public static string Duration(TimeSpan t)
    {
        long s = (long)Math.Round(Math.Max(0, t.TotalSeconds));
        if (s < 60) return $"{s} s";
        if (s < 3600) return $"{s / 60} min {s % 60} s";
        return $"{s / 3600} h {s / 60 % 60:00} min";
    }

    /// Kopie · Ověření · Proxy · Celkem of a finished job. `elapsed`: the job's own time without pauses (JobProgress);
    /// null = from the report.
    public static IReadOnlyList<SummaryField> Summary(OffloadReport r, TimeSpan? elapsed)
    {
        var proxy = r.Proxy is { Enabled: true }
            ? $"{r.Clips.Count(c => c.ProxyStatus == ProxyStatus.Done)} / {r.Clips.Count(c => !(c.ProxyStatus == ProxyStatus.Skipped && c.ProxyNote == null))}"
            : "vypnuto";
        return
        [
            new("Kopie", Format.Rate(r.TotalBytes, r.CopySeconds)),
            new("Ověření", r.Verified ? Format.Rate(r.TotalBytes, r.VerifySeconds) : "vypnuto"),
            new("Proxy", proxy),
            new("Celkem", Duration(elapsed ?? (r.Finished - r.Started - TimeSpan.FromSeconds(r.PausedSeconds)))),
        ];
    }

    /// Why a copy failed, in two words: hash, velikost, čtení, karta, zrušeno, zápis.
    public static string Problem(string state, string? error) => error switch
    {
        _ when state == "pending" => "nezkopírováno",
        { } e when e.StartsWith("HASH MISMATCH", StringComparison.Ordinal) || e.StartsWith("MD5 MISMATCH", StringComparison.Ordinal) => "hash",
        { } e when e.StartsWith("size mismatch", StringComparison.Ordinal) => "velikost",
        { } e when e.StartsWith("verify:", StringComparison.Ordinal) => "čtení",
        { } e when e.Contains("cancelled", StringComparison.Ordinal) => "zrušeno",
        { } e when e.StartsWith("source:", StringComparison.Ordinal) => "karta",
        _ => "zápis",
    };

    /// The files that are not on every destination, each with its state per destination (newest report of the job).
    public static IReadOnlyList<FailedFileRow> FailedRows(OffloadReport r, IReadOnlyList<string> destNames) =>
        // The path with Windows separators, like the path preview ("CLIP\A001C007.mxf"); the report keeps "/".
        r.FailedFiles.Select(f => new FailedFileRow(f.Path.Replace('/', '\\'), destNames.Select((name, d) =>
        {
            var state = d < f.Destinations.Count ? f.Destinations[d] : "pending";
            bool ok = f.SourceError == null && (state == "verified" || (!r.Verified && state == "written"));
            var why = f.SourceError != null ? "karta" : Problem(state, d < f.Errors.Count ? f.Errors[d] : null);
            return new FileOnDisk(name, ok, ok ? $"{name} ✓" : $"{name} ✕ {why}");
        }).ToList())).ToList();

    /// "… a další 3 soubory" under the first `shown` rows; null when all are shown.
    public static string? MoreFiles(int total, int shown) =>
        total > shown ? $"… a {Texts.Plural(total - shown, "další", "další", "dalších")} {total - shown} {Texts.Plural(total - shown, "soubor", "soubory", "souborů")} v reportu" : null;

    /// The headline of a finished job (and of a waiting / running one: just its kind). `files`: the files the job copied.
    public static CardHeadline Headline(string card, JobStatus s, OffloadReport? r, IReadOnlyList<string> destNames)
    {
        if (s.Phase == JobPhase.Waiting) return new(JobCardKind.Waiting, card, "Čeká ve frontě");
        if (!s.Phase.IsFinished()) return new(JobCardKind.Running, card, "");
        if (s.Phase == JobPhase.Cancelled)
            return new(JobCardKind.Cancelled, $"{card} — zrušeno", s.SafeToEject
                ? "Kopie je hotová a ověřená, zrušilo se jen zpracování po kopii."
                : "Karta není kompletně zálohovaná — zkopírujte ji znovu celou.");
        if (r == null)
            return s.Phase == JobPhase.Done
                ? new(JobCardKind.Done, $"{card} — záloha je úplná", s.Note ?? Msg.NothingNew)
                : new(JobCardKind.Failed, $"{card} — offload selhal", s.Error ?? "Kopie se nepodařila.");
        if (!r.Success) return new(JobCardKind.Problem, $"{card} — kartu zatím nevysunujte", ProblemText(r, destNames));
        if (!r.Verified) return new(JobCardKind.Problem, $"{card} — zkopírováno bez ověření", "Bez ověření není jisté, že data na disku jsou v pořádku.");
        return new(JobCardKind.Done, $"{card} — kartu lze vysunout", SafeText(r.TotalFiles, r.Destinations.Count, HashKinds.Label(HashKinds.FromNames(r.Algorithms))));
    }

    /// "Všech 24 souborů je na 2 discích a ověřených (xxHash64)."
    public static string SafeText(int files, int disks, string hash)
    {
        var on = $"na {disks} {Texts.Plural(disks, "disku", "discích", "discích")}";
        return files switch
        {
            1 => $"Soubor je {on} a je ověřený ({hash}).",
            >= 2 and <= 4 => $"Všechny {files} soubory jsou {on} a ověřené ({hash}).",
            _ => $"Všech {files} souborů je {on} a ověřených ({hash}).",
        };
    }

    /// "1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku."
    public static string ProblemText(OffloadReport r, IReadOnlyList<string> destNames)
    {
        var failed = r.FailedFiles;
        int fromCard = failed.Count(f => f.SourceError != null);
        var perDisk = destNames.Select((name, d) => (Name: name, Count: failed.Count(f => f.SourceError == null && !Good(r, f, d)))).ToList();
        var bad = perDisk.Where(p => p.Count > 0).ToList();
        var good = perDisk.Where(p => p.Count == 0).Select(p => p.Name).ToList();
        var parts = new List<string>();
        if (fromCard > 0) parts.Add($"{Files(fromCard)} se nepodařilo přečíst z karty.");
        if (bad.Count == 1) parts.Add($"{Files(bad[0].Count)} na disku {bad[0].Name} nesouhlasí s kartou.");
        else if (bad.Count > 1) parts.Add($"S kartou nesouhlasí: {string.Join(", ", bad.Select(b => $"{b.Name} {Files(b.Count)}"))}.");
        if (bad.Count > 0 && good.Count > 0 && fromCard == 0) parts.Add($"Na {Join(good)} je vše v pořádku.");
        return string.Join(" ", parts);
    }

    static bool Good(OffloadReport r, ReportFile f, int d) =>
        d < f.Destinations.Count && (f.Destinations[d] == "verified" || (!r.Verified && f.Destinations[d] == "written"));

    static string Files(int n) => $"{n} {Texts.Plural(n, "soubor", "soubory", "souborů")}";

    static string Join(IReadOnlyList<string> names) => names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Count - 1))} a {names[^1]}";

    /// The first `max` clips with a thumbnail (the middle frame when there are three) and how many clips are left.
    public static (IReadOnlyList<(string Clip, byte[] Jpeg)> Thumbs, int More) Thumbs(OffloadReport r, int max = 5)
    {
        var clips = r.Clips.Where(c => !c.Clip.IsCameraProxy).ToList();
        var thumbs = clips.Where(c => c.Thumbnails.Count > 0).Take(max)
            .Select(c => (c.Clip.Name, c.Thumbnails[c.Thumbnails.Count == 3 ? 1 : 0].Jpeg)).ToList();
        return (thumbs, thumbs.Count == 0 ? 0 : clips.Count - thumbs.Count);
    }

    /// "Nejpomalejší článek: čtečka karty (USB 3 (5 Gb/s)). Disky by zvládly víc." — from the brake while copying.
    public static string? BrakeSentence(Brake? brake, IReadOnlyList<ChainElement> chain, IReadOnlyList<string> destNames)
    {
        if (brake is not { } b || b.Element < 0 || b.Element >= chain.Count) return null;
        var e = chain[b.Element];
        var link = e.Link != null ? $" ({e.Link}{(b.LinkFull ? " — linka je plná" : "")})" : "";
        if (b.Element == 0)
            return $"Nejpomalejší článek: čtečka karty{link}. {(destNames.Count == 1 ? "Disk by zvládl víc." : "Disky by zvládly víc.")}";
        var name = b.Element - 1 < destNames.Count ? destNames[b.Element - 1] : e.Role;
        return $"Nejpomalejší článek: disk {name}{link}. Karta by šla číst rychleji.";
    }
}

/// The pill in the top bar and the queue tab's number (UI návrh: „● Běží 1 offload · 1 ve frontě“).
public static class QueueSummary
{
    public static (int Running, int Paused, int Waiting) Count(IEnumerable<(JobPhase Phase, bool Paused)> jobs)
    {
        int running = 0, paused = 0, waiting = 0;
        foreach (var (phase, isPaused) in jobs)
        {
            if (phase == JobPhase.Waiting) waiting++;
            else if (!phase.IsFinished()) { if (isPaused) paused++; else running++; }
        }
        return (running, paused, waiting);
    }

    /// Null when nothing is running, paused or waiting.
    public static string? Pill(int running, int paused, int waiting)
    {
        var parts = new List<string>();
        if (running > 0) parts.Add($"Běží {running} {Offloads(running)}");
        if (paused > 0) parts.Add(running > 0 ? $"{paused} {Texts.Plural(paused, "pozastavený", "pozastavené", "pozastavených")}" : $"Pozastaveno: {paused} {Offloads(paused)}");
        if (waiting > 0) parts.Add($"{waiting} ve frontě");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    static string Offloads(int n) => Texts.Plural(n, "offload", "offloady", "offloadů");

    /// The start button says "Zařadit do fronty" while a card waits for or is in its copy — a new one then waits.
    public static string StartLabel(IEnumerable<JobPhase> phases) =>
        phases.Any(p => p is JobPhase.Waiting or JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying) ? "Zařadit do fronty" : "Spustit offload";
}
```

#### `windows/src/Offload.Core/Desktop/JobProgress.cs`

Najít (1/2):

```csharp
public sealed class JobProgress
{
    readonly DateTime started;
    DateTime lastTime;
    long lastBytes;
    long[] lastDest = [];
```

Nahradit:

```csharp
public sealed class JobProgress
{
    readonly DateTime started;
    DateTime? firstActive, ended;
    DateTime lastTime;
    long lastBytes;
    long[] lastDest = [];
```

Najít (2/2):

```csharp

    public void AttachBrakeMeter(BrakeMeter meter) => brakes = meter;

    public bool IsRunning => Status.Phase != JobPhase.Waiting && !Status.Phase.IsFinished();

    public void Update(JobStatus s, DateTime now)
    {
        if (s.Paused)
        {
            // Nothing moves: no rate and no ETA, the brake forgets its window; after Pokračovat the rates are measured
```

Nahradit:

```csharp

    public void AttachBrakeMeter(BrakeMeter meter) => brakes = meter;

    /// How long the job took, without its pauses — from the first tick it ran to the first tick it was finished; null
    /// before (the "Celkem" of the finished card).
    public TimeSpan? Elapsed
    {
        get
        {
            // Never seen running (the first tick found it finished): the report's times say it instead.
            if (firstActive is not DateTime a || ended is not DateTime e || a == e) return null;
            var t = e - a - Status.PausedTotal;
            return t > TimeSpan.Zero ? t : TimeSpan.Zero;
        }
    }

    public bool IsRunning => Status.Phase != JobPhase.Waiting && !Status.Phase.IsFinished();

    public void Update(JobStatus s, DateTime now)
    {
        if (firstActive == null && s.Phase != JobPhase.Waiting) firstActive = now;
        if (ended == null && s.Phase.IsFinished()) ended = now;
        if (s.Paused)
        {
            // Nothing moves: no rate and no ETA, the brake forgets its window; after Pokračovat the rates are measured
```

#### `windows/src/Offload.Core/Format.cs`

Najít (1/1):

```csharp
        return v.ToString(v < 10 ? "0.#" : "0", Cs) + " " + Units[i];
    }

    public static string Rate(long bytes, double seconds) =>
        seconds > 0 ? (bytes / seconds / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "-";
}
```

Nahradit:

```csharp
        return v.ToString(v < 10 ? "0.#" : "0", Cs) + " " + Units[i];
    }

    /// Three significant digits, as the new window shows sizes (UI návrh): "1,70 GB", "14,2 GB", "412 GB", "850 MB".
    public static string BytesCs3(long b)
    {
        double v = b;
        int i = 0;
        while (v >= 1000 && i < Units.Length - 1) { v /= 1000; i++; }
        if (i == 0) return $"{b} B";
        if (Math.Round(v, 2) < 10) return Math.Round(v, 2).ToString("0.00", Cs) + " " + Units[i];
        if (Math.Round(v, 1) < 100) return Math.Round(v, 1).ToString("0.0", Cs) + " " + Units[i];
        if (Math.Round(v) < 1000 || i == Units.Length - 1) return Math.Round(v).ToString("0", Cs) + " " + Units[i];
        return (v / 1000).ToString("0.00", Cs) + " " + Units[i + 1];   // 999,6 MB → 1,00 GB
    }

    public static string Rate(long bytes, double seconds) =>
        seconds > 0 ? (bytes / seconds / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "-";
}
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~JobCardTests"`

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

- Úkol 5 — oprava jako úloha: `JobConfig.RepairInto`, `JobConfigs.Repair` (vlastní kopie nastavení úlohy, která selhala — projekt, výstupy, oznámení; vždy s ověřením, bez analýzy a proxy); úloha drží stejný zámek složek jako doplnění (doplnění a oprava téže zálohy nikdy naráz, „Do této zálohy právě doplňuje jiná úloha…“), zapíše report, PDF, seznamy klipů a denní report; historie: k záznamu zálohy přidá generaci `kind: "repair"` a úspěch spočítá z reportů na cílech (poslední opravený soubor = záznam zase „ověřeno“), soubory, otisk a cívky karty se nemění; „Nic k opravě — všechny soubory jsou na všech cílech v pořádku.“ = Hotovo; `RepairRules.Offer` (jen hotová nebo selhaná, ne zrušená úloha s chybnými soubory, karta ve čtečce, žádná oprava téže zálohy ve frontě), titulek „A001 · oprava“
- Úkol 4 — oprava v jádru („Zkopírovat znovu“, nová věc 4): `RepairPlanner` vezme z reportů na cílech soubory, jejichž nejnovější záznam selhal, a naplánuje je jen na cíle, kde selhaly — dobrá kopie se nikdy nepřepíše, jen se v téže generaci znovu přečte a porovná se zaznamenaným hashem; kontroly dřívější zálohy sdílí s doplněním (`AppendPlanner.CheckBackup`: složky, reporty, řetěz ASC MHL, stejné generace); soubor na kartě musí mít velikost a čas jako v záznamu (jinak „Oprava zastavena — nic se nezkopírovalo…“), chybějící dobrá kopie zastaví opravu jako doplnění; nesouhlasná kopie se před novou přesune do `_REPORT\neshoda_NNNN\<cesta>` (nic se nemaže ani nepřepisuje, obsazené místo = odmítnutí); engine: `PlanMode.Repair`, maska zápisu po cílech (`OffloadPlan.Write`, `DestWriter.Keep`), očekávané hashe (karta, která se teď čte jinak = „card differs from the backup“, soubor zůstane chybný), místo a FAT32 jen tam, kam se píše, `ProgressSnapshot.PlannedBytes`; report generace N `kind: "repair"`, `files[].rewritten`, `setAside`; ASC MHL generace N na všech cílech (přečtená dobrá kopie `verified`, nová `original`, komentář „Offload oprava: …“), seznam `.xxh64` bez opakovaných řádků, HTML „oprava (generace N)“; generace bez analýzy klipů už v `CardRecord` nepřepíše analyzovaný klip; po opravě jde doplnění jako generace N+1
- Úkol 3 — pozastavení celé úlohy: `OffloadJob.Pause/Resume/IsPaused`, `JobStatus.Paused` / `PauseHeld` / `PausedTotal` (jen u běžící úlohy); 2. fáze se zastaví mezi klipy (rozpracovaný klip analýzy nebo proxy doběhne, ffmpeg se nikdy neuspává, takže hlídač zaseknutí pauzu nevidí — test s limitem 2 s a pauzou 3 s) a před PDF a denním reportem; zámek dne ani historie se během pauzy nedrží (test), zámek složek doplnění zůstává (test); zrušení během pauzy (i zavření aplikace) skončí hned jako Zrušeno; `JobProgress`: během pauzy rychlost 0, bez odhadu a bez „Úzkého hrdla“ (`BrakeMeter.Reset`), po Pokračovat se měří od obnovení, texty „Pozastavuje se“ / „Pozastaví se po proxy klipu A003…“ / „Pozastaveno“; `OffloadOptions.ProxyStallTimeout` (jen testy)
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 6 — data nové karty úlohy v jádru (nové věci 1–3): `JobCard.Rows` (tabulka rychlostí „Čtení karty“ + každý cíl: název, podíl, MB/s; při ověření jen cíle; cíl, který oprava jen čte, „jen ověření“; pozastaveno „—“), `JobCard.Steps` (ukazatel fází Kopírování · Ověření · Proxy/Analýza · Report jen z fází, které úloha dělá; první report na konci 1. fáze patří kopii), `JobCard.Summary` (Kopie · Ověření · Proxy x / y · Celkem bez pauz — `JobProgress.Elapsed`), `JobCard.FailedRows` a `Problem` (soubor × cíl: „RAID_A ✓“, „SHUTTLE_B ✕ hash / velikost / čtení / zápis / karta / zrušeno“), titulky karet (`Headline`: „— kartu lze vysunout“, „— kartu zatím nevysunujte“ + „1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku.“, „— zkopírováno bez ověření“, „— offload selhal“, „— zrušeno“, „— záloha je úplná“), `FileLine` („Soubor 10 / 24“), `Remaining` („zbývá ~0:02 kopie · ~0:06 celkem“), `Thumbs` (prvních 5 klipů, prostřední snímek, „+N“), `BrakeSentence` („Nejpomalejší článek: čtečka karty (…). Disky by zvládly víc.“), `QueueSummary` (pilulka „Běží 1 offload · 1 ve frontě“, „Zařadit do fronty“ / „Spustit offload“); `Format.BytesCs3` (tři platné číslice: „1,70 GB“, „14,2 GB“, „412 GB“)
- Úkol 5 — oprava jako úloha: `JobConfig.RepairInto`, `JobConfigs.Repair` (vlastní kopie nastavení úlohy, která selhala — projekt, výstupy, oznámení; vždy s ověřením, bez analýzy a proxy); úloha drží stejný zámek složek jako doplnění (doplnění a oprava téže zálohy nikdy naráz, „Do této zálohy právě doplňuje jiná úloha…“), zapíše report, PDF, seznamy klipů a denní report; historie: k záznamu zálohy přidá generaci `kind: "repair"` a úspěch spočítá z reportů na cílech (poslední opravený soubor = záznam zase „ověřeno“), soubory, otisk a cívky karty se nemění; „Nic k opravě — všechny soubory jsou na všech cílech v pořádku.“ = Hotovo; `RepairRules.Offer` (jen hotová nebo selhaná, ne zrušená úloha s chybnými soubory, karta ve čtečce, žádná oprava téže zálohy ve frontě), titulek „A001 · oprava“
- Úkol 4 — oprava v jádru („Zkopírovat znovu“, nová věc 4): `RepairPlanner` vezme z reportů na cílech soubory, jejichž nejnovější záznam selhal, a naplánuje je jen na cíle, kde selhaly — dobrá kopie se nikdy nepřepíše, jen se v téže generaci znovu přečte a porovná se zaznamenaným hashem; kontroly dřívější zálohy sdílí s doplněním (`AppendPlanner.CheckBackup`: složky, reporty, řetěz ASC MHL, stejné generace); soubor na kartě musí mít velikost a čas jako v záznamu (jinak „Oprava zastavena — nic se nezkopírovalo…“), chybějící dobrá kopie zastaví opravu jako doplnění; nesouhlasná kopie se před novou přesune do `_REPORT\neshoda_NNNN\<cesta>` (nic se nemaže ani nepřepisuje, obsazené místo = odmítnutí); engine: `PlanMode.Repair`, maska zápisu po cílech (`OffloadPlan.Write`, `DestWriter.Keep`), očekávané hashe (karta, která se teď čte jinak = „card differs from the backup“, soubor zůstane chybný), místo a FAT32 jen tam, kam se píše, `ProgressSnapshot.PlannedBytes`; report generace N `kind: "repair"`, `files[].rewritten`, `setAside`; ASC MHL generace N na všech cílech (přečtená dobrá kopie `verified`, nová `original`, komentář „Offload oprava: …“), seznam `.xxh64` bez opakovaných řádků, HTML „oprava (generace N)“; generace bez analýzy klipů už v `CardRecord` nepřepíše analyzovaný klip; po opravě jde doplnění jako generace N+1
- Úkol 3 — pozastavení celé úlohy: `OffloadJob.Pause/Resume/IsPaused`, `JobStatus.Paused` / `PauseHeld` / `PausedTotal` (jen u běžící úlohy); 2. fáze se zastaví mezi klipy (rozpracovaný klip analýzy nebo proxy doběhne, ffmpeg se nikdy neuspává, takže hlídač zaseknutí pauzu nevidí — test s limitem 2 s a pauzou 3 s) a před PDF a denním reportem; zámek dne ani historie se během pauzy nedrží (test), zámek složek doplnění zůstává (test); zrušení během pauzy (i zavření aplikace) skončí hned jako Zrušeno; `JobProgress`: během pauzy rychlost 0, bez odhadu a bez „Úzkého hrdla“ (`BrakeMeter.Reset`), po Pokračovat se měří od obnovení, texty „Pozastavuje se“ / „Pozastaví se po proxy klipu A003…“ / „Pozastaveno“; `OffloadOptions.ProxyStallTimeout` (jen testy)
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: job card data in Core - speeds per destination, phases, summary, failed files (plan 9, task 6)

JobCard computes what the new job card shows without a window: the speed table per destination,
the phase indicator, "Soubor 10 / 24", the summary without pauses, the failed files per disk, the
headline by state, thumbnails and the slowest link. QueueSummary gives the top bar pill.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Desktop\JobCard.cs windows\src\Offload.Core\Desktop\JobProgress.cs windows\src\Offload.Core\Format.cs windows\tests\Offload.Core.Tests\JobCardTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 7: Odhad času (nová věc 10)

`RateMemory` si v paměti (jen v této relaci aplikace) pamatuje, co opravdu naměřila: rychlost kopie a ověření poslední
úlohy od 200 MB a čas 2. fáze na sekundu záznamu (zvlášť pro analýzu a pro proxy s daným kodekem, výškou a tokem).
Z toho dá odhad startu „odhad ~1:30 vč. proxy“ a u běžící kopie „~6:00 celkem“. Když kterákoli část chybí, odhad
není — zadání: „pokud nejde spolehlivě, zobrazit jen velikost“.

**Files:**
- Create: `windows/src/Offload.Core/Desktop/TimeEstimate.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.Core/Desktop/JobProgress.cs`
- Test: `windows/tests/Offload.Core.Tests/TimeEstimateTests.cs` (nový)

**Interfaces:**
- Consumes: `OffloadReport` (`CopySeconds`, `VerifySeconds`), `Clip`, `ProxySettings`, `JobProgress` (úkol 6).
- Produces:
  - `public sealed class RateMemory` — `const long MinBytes = 200_000_000`, `double? CopyRate`, `double? VerifyRate`, `static string? PostKey(bool analyze, ProxySettings proxy)`, `void Learn(OffloadReport r, bool analyze, ProxySettings proxy, TimeSpan? stage2)`, `static double ClipSeconds(IEnumerable<Clip>)`, `TimeSpan? Estimate(long bytes, IReadOnlyList<Clip> clips, bool verifyOn, bool analyze, ProxySettings proxy)`, `double? Remaining(JobPhase phase, double? phaseEta, long bytes, IReadOnlyList<Clip> clips, bool verifyOn, bool analyze, ProxySettings proxy)`, `static string? StartText(TimeSpan? estimate, bool proxies)`.
  - `JobProgress.Stage2Elapsed` (`TimeSpan?`, bez pauz).

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/TimeEstimateTests.cs` (nový soubor)

```csharp
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// "odhad ~0:08 vč. proxy" (UI návrh 10): only from what this session measured; any unknown part = no estimate.
public class TimeEstimateTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    /// 1 GB copied in 10 s (100 MB/s) and read back in 5 s (200 MB/s); one 60 s clip.
    static OffloadReport Report(bool verified = true, long bytes = 1_000_000_000) => new()
    {
        CardName = "A001", Date = "2026-10-07", Source = @"E:\", SourceFilesystem = "exFAT",
        Started = T0, CopyFinished = T0.AddSeconds(10), Finished = T0.AddSeconds(15), Verified = verified, TotalBytes = bytes,
        Destinations = [], Files = [], Skipped = [],
        Clips = [new ClipEntry { Clip = new Clip { Name = "C001", Path = "C001.mov", Segments = ["C001.mov"] }, Info = new ClipInfo { Duration = 60 } }],
    };

    static readonly ProxySettings Proxy = new() { Enabled = true };
    static readonly ProxySettings Off = new();

    /// A clip that the careful rate (MOV 50 Mb/s) makes 120 s long.
    static readonly Clip TwoMinutes = new() { Name = "C002", Path = "C002.mov", Segments = ["C002.mov"], Bytes = 750_000_000 };

    [Fact]
    public void NothingLearned_NoEstimate_SizeOnly()
    {
        var m = new RateMemory();
        Assert.Null(m.Estimate(1_000_000_000, [], verifyOn: false, analyze: false, Off));
        Assert.Null(RateMemory.StartText(null, proxies: false));
    }

    [Fact]
    public void CopyVerifyAndProxies_FromTheLastJob()
    {
        var m = new RateMemory();
        m.Learn(Report(), analyze: true, Proxy, stage2: TimeSpan.FromSeconds(30));   // 0.5 s of work per second of clip

        Assert.Equal(100e6, m.CopyRate);
        Assert.Equal(200e6, m.VerifyRate);
        // 2 GB: 20 s copy + 10 s read-back + 120 s clip × 0.5
        Assert.Equal(TimeSpan.FromSeconds(90), m.Estimate(2_000_000_000, [TwoMinutes], verifyOn: true, analyze: true, Proxy));
        Assert.Equal(TimeSpan.FromSeconds(20), m.Estimate(2_000_000_000, [], verifyOn: false, analyze: false, Off));
        Assert.Equal("odhad ~1:30 vč. proxy", RateMemory.StartText(TimeSpan.FromSeconds(90), proxies: true));
        Assert.Equal("odhad ~0:20", RateMemory.StartText(TimeSpan.FromSeconds(20), proxies: false));
    }

    // Proxies of another height, or analysis only, cost differently: not known = no estimate.
    [Fact]
    public void OtherStage2Settings_NoEstimate()
    {
        var m = new RateMemory();
        m.Learn(Report(), analyze: true, Proxy, stage2: TimeSpan.FromSeconds(30));
        Assert.Null(m.Estimate(1_000_000_000, [TwoMinutes], true, true, new ProxySettings { Enabled = true, Height = 720 }));
        Assert.Null(m.Estimate(1_000_000_000, [TwoMinutes], true, true, Off));   // analysis only
        Assert.NotNull(m.Estimate(1_000_000_000, [], true, true, Off));          // no clips: no stage 2 to estimate
    }

    [Fact]
    public void VerifyNotMeasured_OrTinyJob_NoEstimate()
    {
        var m = new RateMemory();
        m.Learn(Report(verified: false), analyze: false, Off, null);
        Assert.Null(m.Estimate(1_000_000_000, [], verifyOn: true, analyze: false, Off));
        Assert.NotNull(m.Estimate(1_000_000_000, [], verifyOn: false, analyze: false, Off));
        var tiny = new RateMemory();
        tiny.Learn(Report(bytes: 50_000_000), analyze: false, Off, null);
        Assert.Null(tiny.CopyRate);
    }

    [Fact]
    public void Remaining_WhileCopyingOrVerifying()
    {
        var m = new RateMemory();
        m.Learn(Report(), analyze: true, Proxy, stage2: TimeSpan.FromSeconds(30));
        Assert.Equal(2 + 5 + 60, m.Remaining(JobPhase.Copying, 2, 1_000_000_000, [TwoMinutes], true, true, Proxy));
        Assert.Equal(2 + 60, m.Remaining(JobPhase.Verifying, 2, 1_000_000_000, [TwoMinutes], true, true, Proxy));
        Assert.Null(m.Remaining(JobPhase.Proxies, null, 1_000_000_000, [TwoMinutes], true, true, Proxy));
        Assert.Null(new RateMemory().Remaining(JobPhase.Copying, 2, 1_000_000_000, [], true, false, Off));
    }

    [Fact]
    public void Stage2Elapsed_WithoutItsPauses()
    {
        var p = new JobProgress(T0);
        p.Update(new JobStatus { Phase = JobPhase.Copying, PausedTotal = TimeSpan.FromSeconds(5) }, T0);
        p.Update(new JobStatus { Phase = JobPhase.Analyzing, PausedTotal = TimeSpan.FromSeconds(5) }, T0.AddSeconds(100));
        Assert.Null(p.Stage2Elapsed);
        p.Update(new JobStatus { Phase = JobPhase.Done, PausedTotal = TimeSpan.FromSeconds(25) }, T0.AddSeconds(200));
        Assert.Equal(TimeSpan.FromSeconds(80), p.Stage2Elapsed);
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~TimeEstimateTests"`

Expected: FAIL — build selže — `CS0246: Typ nebo název oboru názvů RateMemory se nenašel` (a `Stage2Elapsed`).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/Desktop/JobProgress.cs`

Najít (1/3):

```csharp
public sealed class JobProgress
{
    readonly DateTime started;
    DateTime? firstActive, ended;
    DateTime lastTime;
    long lastBytes;
    long[] lastDest = [];
```

Nahradit:

```csharp
public sealed class JobProgress
{
    readonly DateTime started;
    DateTime? firstActive, ended, stage2Start;
    TimeSpan pausedBeforeStage2;
    DateTime lastTime;
    long lastBytes;
    long[] lastDest = [];
```

Najít (2/3):

```csharp

    public void AttachBrakeMeter(BrakeMeter meter) => brakes = meter;

    /// How long the job took, without its pauses — from the first tick it ran to the first tick it was finished; null
    /// before (the "Celkem" of the finished card).
    public TimeSpan? Elapsed
```

Nahradit:

```csharp

    public void AttachBrakeMeter(BrakeMeter meter) => brakes = meter;

    /// How long stage 2 (clips, proxies, PDF, day report) took without its pauses; null without a stage 2 or before the end
    /// (what RateMemory learns the next start's estimate from).
    public TimeSpan? Stage2Elapsed
    {
        get
        {
            if (stage2Start is not DateTime a || ended is not DateTime e) return null;
            var t = e - a - (Status.PausedTotal - pausedBeforeStage2);
            return t > TimeSpan.Zero ? t : TimeSpan.Zero;
        }
    }

    /// How long the job took, without its pauses — from the first tick it ran to the first tick it was finished; null
    /// before (the "Celkem" of the finished card).
    public TimeSpan? Elapsed
```

Najít (3/3):

```csharp
    {
        if (firstActive == null && s.Phase != JobPhase.Waiting) firstActive = now;
        if (ended == null && s.Phase.IsFinished()) ended = now;
        if (s.Paused)
        {
            // Nothing moves: no rate and no ETA, the brake forgets its window; after Pokračovat the rates are measured
```

Nahradit:

```csharp
    {
        if (firstActive == null && s.Phase != JobPhase.Waiting) firstActive = now;
        if (ended == null && s.Phase.IsFinished()) ended = now;
        if (stage2Start == null && s.Phase is JobPhase.Analyzing or JobPhase.Proxies)
        {
            stage2Start = now;
            pausedBeforeStage2 = s.PausedTotal;
        }
        if (s.Paused)
        {
            // Nothing moves: no rate and no ETA, the brake forgets its window; after Pokračovat the rates are measured
```

#### `windows/src/Offload.Core/Desktop/TimeEstimate.cs` (nový soubor)

```csharp
using Offload.Core.Space;

namespace Offload.Core.Desktop;

/// "odhad ~0:08 vč. proxy" (UI návrh, nová věc 10): how long a start takes, from what the jobs of this session really took
/// — the copy and read-back speed of the last job big enough to measure, and the time stage 2 took per second of clip
/// with the same stage-2 settings. Memory only (a new session starts without an estimate). When any part a start needs
/// is not known the estimate is null and the window shows only the size: a guess would be worse than nothing.
public sealed class RateMemory
{
    /// Jobs smaller than this are too short to measure a speed (the start-up cost would dominate).
    public const long MinBytes = 200_000_000;

    double? copy, verify;
    readonly Dictionary<string, double> post = new(StringComparer.Ordinal);

    /// Bytes per second of the copy and of the read-back, null until a job measured them.
    public double? CopyRate => copy;
    public double? VerifyRate => verify;

    /// What stage 2 costs depends on what it does: analysis only, or proxies of one codec, height and bitrate.
    public static string? PostKey(bool analyze, ProxySettings proxy) =>
        proxy.Enabled ? $"proxy|{proxy.Codec.Wire()}|{proxy.Height}|{proxy.BitrateMbps}" : analyze ? "analysis" : null;

    /// A finished job: `stage2` is how long its stage 2 ran without pauses (JobProgress.Stage2Elapsed), null without one.
    public void Learn(OffloadReport r, bool analyze, ProxySettings proxy, TimeSpan? stage2)
    {
        if (r.TotalBytes >= MinBytes && r.CopySeconds > 0 && r.Success) copy = r.TotalBytes / r.CopySeconds;
        if (r.TotalBytes >= MinBytes && r.Verified && r.VerifySeconds > 0) verify = r.TotalBytes / r.VerifySeconds;
        double clipSeconds = r.Clips.Where(c => !c.Clip.IsCameraProxy).Sum(c => c.Info?.Duration ?? 0);
        if (stage2 is TimeSpan t && t > TimeSpan.Zero && clipSeconds > 0 && PostKey(analyze, proxy) is string key) post[key] = t.TotalSeconds / clipSeconds;
    }

    /// The seconds of footage on a card before it is read: from the clips' sizes at a careful (low) data rate, as the
    /// space check estimates them (SpaceEstimator.SourceMbps) — the estimate leans long.
    public static double ClipSeconds(IEnumerable<Clip> clips) =>
        clips.Where(c => !c.IsCameraProxy).Sum(c => c.Bytes * 8.0 / (SpaceEstimator.SourceMbps(c) * 1e6));

    /// The whole job before it starts, or null when a part is not known.
    public TimeSpan? Estimate(long bytes, IReadOnlyList<Clip> clips, bool verifyOn, bool analyze, ProxySettings proxy)
    {
        if (copy is not double c) return null;
        double seconds = bytes / c;
        if (verifyOn)
        {
            if (verify is not double v) return null;
            seconds += bytes / v;
        }
        if (PostKey(analyze, proxy) is string key && clips.Count > 0)
        {
            if (!post.TryGetValue(key, out var perSecond)) return null;
            seconds += ClipSeconds(clips) * perSecond;
        }
        return TimeSpan.FromSeconds(seconds);
    }

    /// The rest of a running job while it copies or reads back: the phase's own ETA, then the phases after it. Null when
    /// one of them is not known (the card then says only the phase's ETA).
    public double? Remaining(JobPhase phase, double? phaseEta, long bytes, IReadOnlyList<Clip> clips, bool verifyOn, bool analyze, ProxySettings proxy)
    {
        if (phaseEta is not double eta || phase is not (JobPhase.Copying or JobPhase.Verifying)) return null;
        double rest = eta;
        if (phase == JobPhase.Copying && verifyOn)
        {
            if (verify is not double v) return null;
            rest += bytes / v;
        }
        if (PostKey(analyze, proxy) is string key && clips.Count > 0)
        {
            if (!post.TryGetValue(key, out var perSecond)) return null;
            rest += ClipSeconds(clips) * perSecond;
        }
        return rest;
    }

    /// The right side of the start bar: "odhad ~8:00 vč. proxy", "odhad ~2:10", or null (only the size shows).
    public static string? StartText(TimeSpan? estimate, bool proxies) =>
        estimate is TimeSpan t ? $"odhad ~{JobProgress.EtaText(t.TotalSeconds)}{(proxies ? " vč. proxy" : "")}" : null;
}
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~TimeEstimateTests"`

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

- Úkol 6 — data nové karty úlohy v jádru (nové věci 1–3): `JobCard.Rows` (tabulka rychlostí „Čtení karty“ + každý cíl: název, podíl, MB/s; při ověření jen cíle; cíl, který oprava jen čte, „jen ověření“; pozastaveno „—“), `JobCard.Steps` (ukazatel fází Kopírování · Ověření · Proxy/Analýza · Report jen z fází, které úloha dělá; první report na konci 1. fáze patří kopii), `JobCard.Summary` (Kopie · Ověření · Proxy x / y · Celkem bez pauz — `JobProgress.Elapsed`), `JobCard.FailedRows` a `Problem` (soubor × cíl: „RAID_A ✓“, „SHUTTLE_B ✕ hash / velikost / čtení / zápis / karta / zrušeno“), titulky karet (`Headline`: „— kartu lze vysunout“, „— kartu zatím nevysunujte“ + „1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku.“, „— zkopírováno bez ověření“, „— offload selhal“, „— zrušeno“, „— záloha je úplná“), `FileLine` („Soubor 10 / 24“), `Remaining` („zbývá ~0:02 kopie · ~0:06 celkem“), `Thumbs` (prvních 5 klipů, prostřední snímek, „+N“), `BrakeSentence` („Nejpomalejší článek: čtečka karty (…). Disky by zvládly víc.“), `QueueSummary` (pilulka „Běží 1 offload · 1 ve frontě“, „Zařadit do fronty“ / „Spustit offload“); `Format.BytesCs3` (tři platné číslice: „1,70 GB“, „14,2 GB“, „412 GB“)
- Úkol 5 — oprava jako úloha: `JobConfig.RepairInto`, `JobConfigs.Repair` (vlastní kopie nastavení úlohy, která selhala — projekt, výstupy, oznámení; vždy s ověřením, bez analýzy a proxy); úloha drží stejný zámek složek jako doplnění (doplnění a oprava téže zálohy nikdy naráz, „Do této zálohy právě doplňuje jiná úloha…“), zapíše report, PDF, seznamy klipů a denní report; historie: k záznamu zálohy přidá generaci `kind: "repair"` a úspěch spočítá z reportů na cílech (poslední opravený soubor = záznam zase „ověřeno“), soubory, otisk a cívky karty se nemění; „Nic k opravě — všechny soubory jsou na všech cílech v pořádku.“ = Hotovo; `RepairRules.Offer` (jen hotová nebo selhaná, ne zrušená úloha s chybnými soubory, karta ve čtečce, žádná oprava téže zálohy ve frontě), titulek „A001 · oprava“
- Úkol 4 — oprava v jádru („Zkopírovat znovu“, nová věc 4): `RepairPlanner` vezme z reportů na cílech soubory, jejichž nejnovější záznam selhal, a naplánuje je jen na cíle, kde selhaly — dobrá kopie se nikdy nepřepíše, jen se v téže generaci znovu přečte a porovná se zaznamenaným hashem; kontroly dřívější zálohy sdílí s doplněním (`AppendPlanner.CheckBackup`: složky, reporty, řetěz ASC MHL, stejné generace); soubor na kartě musí mít velikost a čas jako v záznamu (jinak „Oprava zastavena — nic se nezkopírovalo…“), chybějící dobrá kopie zastaví opravu jako doplnění; nesouhlasná kopie se před novou přesune do `_REPORT\neshoda_NNNN\<cesta>` (nic se nemaže ani nepřepisuje, obsazené místo = odmítnutí); engine: `PlanMode.Repair`, maska zápisu po cílech (`OffloadPlan.Write`, `DestWriter.Keep`), očekávané hashe (karta, která se teď čte jinak = „card differs from the backup“, soubor zůstane chybný), místo a FAT32 jen tam, kam se píše, `ProgressSnapshot.PlannedBytes`; report generace N `kind: "repair"`, `files[].rewritten`, `setAside`; ASC MHL generace N na všech cílech (přečtená dobrá kopie `verified`, nová `original`, komentář „Offload oprava: …“), seznam `.xxh64` bez opakovaných řádků, HTML „oprava (generace N)“; generace bez analýzy klipů už v `CardRecord` nepřepíše analyzovaný klip; po opravě jde doplnění jako generace N+1
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 7 — odhad času (nová věc 10): `RateMemory` si v paměti (jen tato relace) pamatuje rychlost kopie a zpětného čtení poslední úlohy od 200 MB a čas 2. fáze na sekundu záznamu zvlášť pro „jen analýza“ a pro proxy daného kodeku, výšky a datového toku (`JobProgress.Stage2Elapsed` bez pauz); odhad startu = kopie + ověření + délka klipů (z velikosti opatrným datovým tokem jako kontrola místa) × čas na sekundu — když některá část chybí, odhad není a lišta ukáže jen velikost; „odhad ~1:30 vč. proxy“, u běžící kopie „~6:00 celkem“ (`Remaining`)
- Úkol 6 — data nové karty úlohy v jádru (nové věci 1–3): `JobCard.Rows` (tabulka rychlostí „Čtení karty“ + každý cíl: název, podíl, MB/s; při ověření jen cíle; cíl, který oprava jen čte, „jen ověření“; pozastaveno „—“), `JobCard.Steps` (ukazatel fází Kopírování · Ověření · Proxy/Analýza · Report jen z fází, které úloha dělá; první report na konci 1. fáze patří kopii), `JobCard.Summary` (Kopie · Ověření · Proxy x / y · Celkem bez pauz — `JobProgress.Elapsed`), `JobCard.FailedRows` a `Problem` (soubor × cíl: „RAID_A ✓“, „SHUTTLE_B ✕ hash / velikost / čtení / zápis / karta / zrušeno“), titulky karet (`Headline`: „— kartu lze vysunout“, „— kartu zatím nevysunujte“ + „1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku.“, „— zkopírováno bez ověření“, „— offload selhal“, „— zrušeno“, „— záloha je úplná“), `FileLine` („Soubor 10 / 24“), `Remaining` („zbývá ~0:02 kopie · ~0:06 celkem“), `Thumbs` (prvních 5 klipů, prostřední snímek, „+N“), `BrakeSentence` („Nejpomalejší článek: čtečka karty (…). Disky by zvládly víc.“), `QueueSummary` (pilulka „Běží 1 offload · 1 ve frontě“, „Zařadit do fronty“ / „Spustit offload“); `Format.BytesCs3` (tři platné číslice: „1,70 GB“, „14,2 GB“, „412 GB“)
- Úkol 5 — oprava jako úloha: `JobConfig.RepairInto`, `JobConfigs.Repair` (vlastní kopie nastavení úlohy, která selhala — projekt, výstupy, oznámení; vždy s ověřením, bez analýzy a proxy); úloha drží stejný zámek složek jako doplnění (doplnění a oprava téže zálohy nikdy naráz, „Do této zálohy právě doplňuje jiná úloha…“), zapíše report, PDF, seznamy klipů a denní report; historie: k záznamu zálohy přidá generaci `kind: "repair"` a úspěch spočítá z reportů na cílech (poslední opravený soubor = záznam zase „ověřeno“), soubory, otisk a cívky karty se nemění; „Nic k opravě — všechny soubory jsou na všech cílech v pořádku.“ = Hotovo; `RepairRules.Offer` (jen hotová nebo selhaná, ne zrušená úloha s chybnými soubory, karta ve čtečce, žádná oprava téže zálohy ve frontě), titulek „A001 · oprava“
- Úkol 4 — oprava v jádru („Zkopírovat znovu“, nová věc 4): `RepairPlanner` vezme z reportů na cílech soubory, jejichž nejnovější záznam selhal, a naplánuje je jen na cíle, kde selhaly — dobrá kopie se nikdy nepřepíše, jen se v téže generaci znovu přečte a porovná se zaznamenaným hashem; kontroly dřívější zálohy sdílí s doplněním (`AppendPlanner.CheckBackup`: složky, reporty, řetěz ASC MHL, stejné generace); soubor na kartě musí mít velikost a čas jako v záznamu (jinak „Oprava zastavena — nic se nezkopírovalo…“), chybějící dobrá kopie zastaví opravu jako doplnění; nesouhlasná kopie se před novou přesune do `_REPORT\neshoda_NNNN\<cesta>` (nic se nemaže ani nepřepisuje, obsazené místo = odmítnutí); engine: `PlanMode.Repair`, maska zápisu po cílech (`OffloadPlan.Write`, `DestWriter.Keep`), očekávané hashe (karta, která se teď čte jinak = „card differs from the backup“, soubor zůstane chybný), místo a FAT32 jen tam, kam se píše, `ProgressSnapshot.PlannedBytes`; report generace N `kind: "repair"`, `files[].rewritten`, `setAside`; ASC MHL generace N na všech cílech (přečtená dobrá kopie `verified`, nová `original`, komentář „Offload oprava: …“), seznam `.xxh64` bez opakovaných řádků, HTML „oprava (generace N)“; generace bez analýzy klipů už v `CardRecord` nepřepíše analyzovaný klip; po opravě jde doplnění jako generace N+1
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: time estimate from the rates measured in this session (plan 9, task 7)

RateMemory keeps the copy and read-back rates of the last job from 200 MB on and the stage 2 time
per second of footage per analysis/proxy setting; the estimate is shown only when every part is known.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Desktop\JobProgress.cs windows\src\Offload.Core\Desktop\TimeEstimate.cs windows\tests\Offload.Core.Tests\TimeEstimateTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 8: Štítek kamery, typ připojení a dlaždice disku (nová věc 6)

Prověření karty (už běží mimo UI vlákno) nově přečte jeden klip a určí kameru: video přes ffprobe s limitem 5 s
a automatický výběr LUTu, RAW podle formátu. `ConnectionLabel` z HW diagnostiky (`DriveLinks`) dá krátký typ
připojení, `DestinationTile` texty a pruh místa dlaždice cíle. Na kartu se nic nezapisuje.

**Files:**
- Create: `windows/src/Offload.Core/Desktop/CardFacts.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Modify: `windows/src/Offload.Core/ClipDetector.cs`
- Modify: `windows/src/Offload.Core/History/CardInspector.cs`
- Test: `windows/tests/Offload.Core.Tests/CardFactsTests.cs` (nový)

**Interfaces:**
- Consumes: `CardInsight`, `CardInspector`, `ClipDetector`, `LutResolver`, `LutCatalog`, `ExternalTools`, `DriveLink`, `SpaceCheck` (`VolumeDemand`, `SpaceProblem`, `SpaceSeverity`) — stávající; `Format.BytesCs3` (úkol 6).
- Produces:
  - `public sealed record CameraLabel(string? Camera, string? Curve)` — `string? Text` („ALEXA 35 · LogC4“), `static string? Model(string?)`, `static string ShortCurve(string)`.
  - `public static class CameraHints` — `ProbeTimeout` (5 s), `CameraLabel? Probe(CardInsight insight, ExternalTools resolvedTools, LutCatalog catalog, CancellationToken ct, …)`.
  - `CardInsight.Camera` (`CameraLabel?`).
  - `public static class ConnectionLabel` — `string? Short(DriveLink link, bool card)`.
  - `public static class DestinationTile` — `FreeText(long free, long? total, string filesystem, string? connection)`, `CompactFree(long free)`, `Bar(long total, long free, long need)` → `(double Used, double Need)`, `Need(IEnumerable<VolumeDemand>, string volume)`, `Severity(IReadOnlyList<SpaceProblem>, string label)`.
  - `FFProbe.Probe(string ffprobe, string path, TimeSpan timeout)`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/CardFactsTests.cs` (nový soubor)

```csharp
using Offload.Core.Desktop;
using Offload.Core.Hardware;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.Core.Tests;

/// UI návrh, nová věc 6: the camera label of a card and how cards and disks are connected; the destination tile.
public class CardFactsTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    static CardInsight Insight(params Clip[] clips) => new() { Source = @"E:\", Clips = clips };

    static Clip Video(string path, bool proxy = false) =>
        new() { Name = Path.GetFileNameWithoutExtension(path), Path = path, Format = ClipFormat.Video, Segments = [path], IsCameraProxy = proxy };

    [Fact]
    public void Label_ModelAndCurve()
    {
        Assert.Equal("ALEXA 35 · LogC4", new CameraLabel("ALEXA 35", "LogC4").Text);
        Assert.Equal("ALEXA 35", new CameraLabel("ALEXA 35", null).Text);
        Assert.Equal("S-Log3", new CameraLabel(null, "S-Log3").Text);
        Assert.Null(new CameraLabel(" ", null).Text);
        Assert.Equal("ALEXA 35", CameraLabel.Model("ARRI ALEXA 35"));
        Assert.Equal("ILME-FX6V", CameraLabel.Model("Sony ILME-FX6V"));
        Assert.Equal("RED", CameraLabel.Model("RED"));
        Assert.Equal(["LogC4", "S-Log3", "Film Gen 5", "C-Log3", "Log3G10"],
            new[] { "ARRI LogC4", "S-Log3", "Blackmagic Film Generation 5", "Canon Log 3", "Log3G10" }.Select(CameraLabel.ShortCurve));
    }

    [Fact]
    public void Hints_ArriFromFfprobe_RawByFormat_ProxySkipped()
    {
        var arri = Insight(Video("SUB/A035C001.mp4", proxy: true), Video("A035C001_261007_R2KQ.mxf"));
        string? asked = null;
        var label = CameraHints.Probe(arri, new ExternalTools(), Cat, CancellationToken.None, p => { asked = p; return new ClipInfo { Camera = "ARRI ALEXA 35" }; });
        Assert.Equal("ALEXA 35 · LogC4", label!.Text);
        Assert.Equal(@"E:\A035C001_261007_R2KQ.mxf", asked);   // the first clip that is not a camera proxy

        var braw = Insight(new Clip { Name = "A001", Path = "A001.braw", Format = ClipFormat.Braw, Segments = ["A001.braw"] });
        Assert.Equal("Blackmagic RAW", CameraHints.Probe(braw, new ExternalTools(), Cat, CancellationToken.None, _ => throw new InvalidOperationException())!.Text);
    }

    [Fact]
    public void Hints_UnknownOrUnreadable_NoLabel()
    {
        var clip = Insight(Video("C0001.MP4"));
        Assert.Null(CameraHints.Probe(clip, new ExternalTools(), Cat, CancellationToken.None, _ => throw new ToolError("ffprobe", -1, "neodpověděl do 5 s — ukončen")));
        Assert.Null(CameraHints.Probe(clip, new ExternalTools(), Cat, CancellationToken.None, _ => new ClipInfo()));
        Assert.Null(CameraHints.Probe(Insight(), new ExternalTools(), Cat, CancellationToken.None, _ => new ClipInfo { Camera = "X" }));
        Assert.Null(CameraHints.Probe(new CardInsight { Source = @"E:\", Error = "x", Clips = [Video("a.mov")] }, new ExternalTools(), Cat, CancellationToken.None,
            _ => new ClipInfo { Camera = "X" }));
        Assert.Equal("FX6", CameraHints.Probe(clip, new ExternalTools(), Cat, CancellationToken.None, _ => new ClipInfo { Camera = "FX6" })!.Text);
    }

    static DriveLink Usb(UsbLinkSpeed speed) =>
        new("E:\\", StorageBus.Usb, "reader", new UsbLink("PCI\\HC", "x", "R#5", 5, 5, speed, speed, true, false));

    [Fact]
    public void Connection_InAWordOrTwo()
    {
        Assert.Equal("čtečka USB 3 · 5 Gb/s", ConnectionLabel.Short(Usb(UsbLinkSpeed.Super), card: true));
        Assert.Equal("USB 3 · 10 Gb/s", ConnectionLabel.Short(Usb(UsbLinkSpeed.SuperPlus), card: false));
        Assert.Equal("USB 2.0", ConnectionLabel.Short(Usb(UsbLinkSpeed.High), card: false));
        Assert.Equal("SD čtečka", ConnectionLabel.Short(new DriveLink("E:\\", StorageBus.Sd, "", null), card: true));
        Assert.Equal("NVMe", ConnectionLabel.Short(new DriveLink("D:\\", StorageBus.Nvme, "", null), card: false));
        Assert.Equal("síť", ConnectionLabel.Short(new DriveLink("Z:\\", StorageBus.Network, "", null), card: false));
        Assert.Null(ConnectionLabel.Short(new DriveLink("D:\\", StorageBus.Unknown, "", null), card: false));
    }

    [Fact]
    public void DestinationTile_TextsAndBar()
    {
        Assert.Equal("7,20 TB volných z 8 TB · NTFS · NVMe", DestinationTile.FreeText(7_200_000_000_000, 8_000_000_000_000, "NTFS", "NVMe"));
        Assert.Equal("1,92 TB volno · exFAT", DestinationTile.FreeText(1_920_000_000_000, null, "exFAT", null));
        Assert.Equal("7,20 TB volno", DestinationTile.CompactFree(7_200_000_000_000));
        static (double, double) Bar(long total, long free, long need) =>
            DestinationTile.Bar(total, free, need) is var b ? (Math.Round(b.Used, 3), Math.Round(b.Need, 3)) : default;
        Assert.Equal((0.1, 0.02), Bar(8_000_000_000_000, 7_200_000_000_000, 160_000_000_000));
        Assert.Equal((0.5, 0.01), Bar(1000, 500, 1));            // a tiny need still shows
        Assert.Equal((0.9, 0.1), Bar(1000, 100, 5000));          // never past the free space
        Assert.Equal((0.0, 0.0), Bar(0, 0, 10));
        VolumeDemand D(string v, long n) => new(v, "x", new SpaceNeed(n, 0, 0));
        Assert.Equal(30, DestinationTile.Need([D("A", 10), D("a", 20), D("B", 99)], "A"));
        var problems = new List<SpaceProblem>
        {
            new(SpaceSeverity.Warning, "RAID_A (D:)", SpaceNeed.None, 0, "w"),
            new(SpaceSeverity.Blocking, "RAID_A (D:), SHUTTLE_B (D:)", SpaceNeed.None, 0, "b"),
        };
        Assert.Equal(SpaceSeverity.Blocking, DestinationTile.Severity(problems, "SHUTTLE_B (D:)"));
        Assert.Equal(SpaceSeverity.Blocking, DestinationTile.Severity(problems, "RAID_A (D:)"));
        Assert.Null(DestinationTile.Severity(problems, "OTHER (E:)"));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CardFactsTests"`

Expected: FAIL — build selže — `CS0246: Typ nebo název oboru názvů CameraLabel se nenašel` (a `ConnectionLabel`, `DestinationTile`).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/ClipDetector.cs`

Najít (1/1):

```csharp
    public static ClipInfo Probe(string ffprobe, string path) =>
        Parse(ToolProcess.Run(ffprobe, ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", path]));

    public static ClipInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
```

Nahradit:

```csharp
    public static ClipInfo Probe(string ffprobe, string path) =>
        Parse(ToolProcess.Run(ffprobe, ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", path]));

    /// The same with a time limit (a card that was just put in must never hold its inspection): ToolError after it.
    public static ClipInfo Probe(string ffprobe, string path, TimeSpan timeout) =>
        Parse(ToolProcess.Run(ffprobe, ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", path], timeout: timeout));

    public static ClipInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
```

#### `windows/src/Offload.Core/Desktop/CardFacts.cs` (nový soubor)

```csharp
using Offload.Core.Hardware;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.Core.Desktop;

/// "ALEXA 35 · LogC4" (UI návrh, nová věc 6): the camera and its log curve, from one clip of the card.
public sealed record CameraLabel(string? Camera, string? Curve)
{
    /// "ALEXA 35 · LogC4", "ALEXA 35", "S-Log3", or null when nothing is known.
    public string? Text
    {
        get
        {
            var parts = new[] { Camera, Curve }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
    }

    static readonly string[] Makers = ["Blackmagic Design", "Blackmagic", "ARRI", "Sony", "Canon", "Panasonic", "FUJIFILM", "Fujifilm", "DJI", "RED"];

    /// The model without its maker when the model says enough on its own: "ARRI ALEXA 35" → "ALEXA 35".
    public static string? Model(string? camera)
    {
        if (string.IsNullOrWhiteSpace(camera)) return null;
        var c = camera.Trim();
        foreach (var m in Makers)
            if (c.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase) && c.Length - m.Length > 3) return c[(m.Length + 1)..].Trim();
        return c;
    }

    /// The curve of a built-in LUT as people say it: "ARRI LogC4" → "LogC4", "Blackmagic Film Generation 5" → "Film Gen 5",
    /// "Canon Log 3" → "C-Log3".
    public static string ShortCurve(string curve)
    {
        var c = curve.Trim();
        if (c.StartsWith("ARRI ", StringComparison.Ordinal)) c = c["ARRI ".Length..];
        c = c.Replace("Blackmagic Film Generation ", "Film Gen ", StringComparison.Ordinal);
        if (c.StartsWith("Canon Log ", StringComparison.Ordinal)) c = "C-Log" + c["Canon Log ".Length..];
        return c;
    }
}

/// The camera label of a card at insertion (off the UI thread, reading only): the first clip of the card that is not a
/// camera proxy. A video clip is asked with ffprobe (at most 5 s) and the automatic LUT choice names its curve; a RAW
/// clip is named by its format (its decoder is too slow for a card that was just put in).
public static class CameraHints
{
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// `probe`: ffprobe of a file (tests pass a fake); null = FFProbe with ProbeTimeout when the tools have ffprobe.
    public static CameraLabel? Probe(CardInsight insight, ExternalTools resolvedTools, LutCatalog catalog, CancellationToken ct,
        Func<string, ClipInfo?>? probe = null)
    {
        if (insight.Error != null || insight.Clips.FirstOrDefault(c => !c.IsCameraProxy) is not { } clip) return null;
        string? raw = clip.Format switch
        {
            ClipFormat.Braw => "Blackmagic RAW",
            ClipFormat.R3d => "RED",
            ClipFormat.Arriraw => "ARRIRAW",
            ClipFormat.CanonRaw => "Canon RAW",
            _ => null,
        };
        if (raw != null) return new CameraLabel(raw, null);
        if (clip.Format != ClipFormat.Video) return null;
        ct.ThrowIfCancellationRequested();
        var path = Layout.Join(insight.Source, clip.Path);
        probe ??= resolvedTools.Ffprobe is string ffprobe ? p => FFProbe.Probe(ffprobe, p, ProbeTimeout) : null;
        ClipInfo? info;
        try
        {
            info = probe?.Invoke(path);
        }
        catch (Exception e) when (e is ToolError or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            info = null;   // a clip ffprobe cannot read: the label just stays empty
        }
        var lut = LutResolver.Resolve(clip, info, path, new ProxySettings { LutMode = LutMode.Auto }, catalog);
        var curve = catalog.Find(lut.Id)?.Curve is string c ? CameraLabel.ShortCurve(c) : null;
        var label = new CameraLabel(CameraLabel.Model(info?.Camera), curve);
        return label.Text == null ? null : label;
    }
}

/// How a disk is connected, in a word or two (UI návrh, nová věc 6): from the HW diagnostics (`DriveLinks`).
public static class ConnectionLabel
{
    /// "čtečka USB 3 · 5 Gb/s", "USB 2.0", "NVMe", "SATA", "SD čtečka", "síť" — or null when unknown. Thunderbolt is not
    /// told apart by Windows: a disk behind it shows by its bus (NVMe, USB).
    public static string? Short(DriveLink link, bool card)
    {
        string? bus = link.Usb is { } u ? Usb(u.Link) : link.Bus switch
        {
            StorageBus.Sd or StorageBus.Mmc => card ? "SD čtečka" : "SD",
            StorageBus.Nvme => "NVMe",
            StorageBus.Sata => "SATA",
            StorageBus.Network => "síť",
            StorageBus.Raid => "RAID",
            StorageBus.Sas => "SAS",
            StorageBus.Scsi => "SCSI",
            StorageBus.Virtual => "virtuální disk",
            StorageBus.Usb => "USB",
            _ => null,
        };
        if (bus == null) return null;
        return card && link.Bus == StorageBus.Usb ? $"čtečka {bus}" : bus;
    }

    static string? Usb(UsbLinkSpeed s) => s switch
    {
        UsbLinkSpeed.Low or UsbLinkSpeed.Full => "USB 1.1",
        UsbLinkSpeed.High => "USB 2.0",
        UsbLinkSpeed.Super => "USB 3 · 5 Gb/s",
        UsbLinkSpeed.SuperPlus => "USB 3 · 10 Gb/s",
        _ => "USB",
    };
}

/// The destination tile (UI návrh obr. 1): its free-space line and the space bar.
public static class DestinationTile
{
    /// "7,20 TB volných z 8 TB · NTFS · NVMe".
    public static string FreeText(long free, long? total, string filesystem, string? connection)
    {
        var parts = new List<string> { total is long t && t > 0 ? $"{Format.BytesCs3(free)} volných z {Format.BytesCs(t)}" : $"{Format.BytesCs3(free)} volno" };
        if (filesystem.Length > 0) parts.Add(filesystem);
        if (connection != null) parts.Add(connection);
        return string.Join(" · ", parts);
    }

    /// The compact list while a copy runs: "7,20 TB volno".
    public static string CompactFree(long free) => $"{Format.BytesCs3(free)} volno";

    /// The bar: grey = used, blue = what the cards ticked now need there (with proxies and the report); yellow / red with
    /// the space warning / problem. Shares of the capacity, the need cut to what is free.
    public static (double Used, double Need) Bar(long total, long free, long need)
    {
        if (total <= 0) return (0, 0);
        double used = Math.Clamp((double)(total - free) / total, 0, 1);
        double want = Math.Clamp((double)Math.Max(0, need) / total, 0, 1 - used);
        // A need too small to see still shows as a sliver.
        if (need > 0 && want < 0.01) want = Math.Min(0.01, 1 - used);
        return (used, want);
    }

    /// What the cards ticked now need on one volume (their demands from SpaceCheck, summed).
    public static long Need(IEnumerable<VolumeDemand> demands, string volume) =>
        demands.Where(d => string.Equals(d.Volume, volume, StringComparison.OrdinalIgnoreCase)).Sum(d => d.Need.Total);

    /// How the bar is coloured on this volume: the space check's verdict for it, if any.
    public static SpaceSeverity? Severity(IReadOnlyList<SpaceProblem> problems, string label) =>
        problems.Where(p => p.Label.Split(", ").Contains(label, StringComparer.OrdinalIgnoreCase)).Select(p => (SpaceSeverity?)p.Severity).Max();
}
```

#### `windows/src/Offload.Core/History/CardInspector.cs`

Najít (1/1):

```csharp
    public NameSuggestion? Name { get; init; }
    /// "Na kartě je víc cívek: A001, A002" when the clips disagree.
    public string? NameNote { get; init; }
    public string? Error { get; init; }

    /// A card emptied in the camera after the backup: history still knows it (Identical, everything "Removed").
```

Nahradit:

```csharp
    public NameSuggestion? Name { get; init; }
    /// "Na kartě je víc cívek: A001, A002" when the clips disagree.
    public string? NameNote { get; init; }
    /// "ALEXA 35 · LogC4" from one clip (CameraHints, UI návrh 6); null when unknown or not looked at.
    public CameraLabel? Camera { get; init; }
    public string? Error { get; init; }

    /// A card emptied in the camera after the backup: history still knows it (Identical, everything "Removed").
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CardFactsTests"`

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

- Úkol 7 — odhad času (nová věc 10): `RateMemory` si v paměti (jen tato relace) pamatuje rychlost kopie a zpětného čtení poslední úlohy od 200 MB a čas 2. fáze na sekundu záznamu zvlášť pro „jen analýza“ a pro proxy daného kodeku, výšky a datového toku (`JobProgress.Stage2Elapsed` bez pauz); odhad startu = kopie + ověření + délka klipů (z velikosti opatrným datovým tokem jako kontrola místa) × čas na sekundu — když některá část chybí, odhad není a lišta ukáže jen velikost; „odhad ~1:30 vč. proxy“, u běžící kopie „~6:00 celkem“ (`Remaining`)
- Úkol 6 — data nové karty úlohy v jádru (nové věci 1–3): `JobCard.Rows` (tabulka rychlostí „Čtení karty“ + každý cíl: název, podíl, MB/s; při ověření jen cíle; cíl, který oprava jen čte, „jen ověření“; pozastaveno „—“), `JobCard.Steps` (ukazatel fází Kopírování · Ověření · Proxy/Analýza · Report jen z fází, které úloha dělá; první report na konci 1. fáze patří kopii), `JobCard.Summary` (Kopie · Ověření · Proxy x / y · Celkem bez pauz — `JobProgress.Elapsed`), `JobCard.FailedRows` a `Problem` (soubor × cíl: „RAID_A ✓“, „SHUTTLE_B ✕ hash / velikost / čtení / zápis / karta / zrušeno“), titulky karet (`Headline`: „— kartu lze vysunout“, „— kartu zatím nevysunujte“ + „1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku.“, „— zkopírováno bez ověření“, „— offload selhal“, „— zrušeno“, „— záloha je úplná“), `FileLine` („Soubor 10 / 24“), `Remaining` („zbývá ~0:02 kopie · ~0:06 celkem“), `Thumbs` (prvních 5 klipů, prostřední snímek, „+N“), `BrakeSentence` („Nejpomalejší článek: čtečka karty (…). Disky by zvládly víc.“), `QueueSummary` (pilulka „Běží 1 offload · 1 ve frontě“, „Zařadit do fronty“ / „Spustit offload“); `Format.BytesCs3` (tři platné číslice: „1,70 GB“, „14,2 GB“, „412 GB“)
- Úkol 5 — oprava jako úloha: `JobConfig.RepairInto`, `JobConfigs.Repair` (vlastní kopie nastavení úlohy, která selhala — projekt, výstupy, oznámení; vždy s ověřením, bez analýzy a proxy); úloha drží stejný zámek složek jako doplnění (doplnění a oprava téže zálohy nikdy naráz, „Do této zálohy právě doplňuje jiná úloha…“), zapíše report, PDF, seznamy klipů a denní report; historie: k záznamu zálohy přidá generaci `kind: "repair"` a úspěch spočítá z reportů na cílech (poslední opravený soubor = záznam zase „ověřeno“), soubory, otisk a cívky karty se nemění; „Nic k opravě — všechny soubory jsou na všech cílech v pořádku.“ = Hotovo; `RepairRules.Offer` (jen hotová nebo selhaná, ne zrušená úloha s chybnými soubory, karta ve čtečce, žádná oprava téže zálohy ve frontě), titulek „A001 · oprava“
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 8 — štítek kamery a typ připojení (nová věc 6) a dlaždice disku: `CameraHints.Probe` při prověření karty (mimo UI vlákno, jen čtení): první klip, který není proxy kamery — video přes ffprobe s limitem 5 s (`FFProbe.Probe(…, timeout)`) a automatickým výběrem LUTu (křivka vestavěného LUTu), RAW podle formátu („Blackmagic RAW“, „RED“, „ARRIRAW“, „Canon RAW“); `CardInsight.Camera` → „ALEXA 35 · LogC4“ (`CameraLabel.Model` bez výrobce, `ShortCurve`: LogC4, S-Log3, Film Gen 5, C-Log3); `ConnectionLabel.Short` z `DriveLinks` („čtečka USB 3 · 5 Gb/s“, „USB 2.0“, „SD čtečka“, „NVMe“, „SATA“, „síť“; Thunderbolt Windows nerozliší); `DestinationTile` („7,20 TB volných z 8 TB · NTFS · NVMe“, „7,20 TB volno“, pruh obsazeno / potřeba vybraných karet, barva podle kontroly místa)
- Úkol 7 — odhad času (nová věc 10): `RateMemory` si v paměti (jen tato relace) pamatuje rychlost kopie a zpětného čtení poslední úlohy od 200 MB a čas 2. fáze na sekundu záznamu zvlášť pro „jen analýza“ a pro proxy daného kodeku, výšky a datového toku (`JobProgress.Stage2Elapsed` bez pauz); odhad startu = kopie + ověření + délka klipů (z velikosti opatrným datovým tokem jako kontrola místa) × čas na sekundu — když některá část chybí, odhad není a lišta ukáže jen velikost; „odhad ~1:30 vč. proxy“, u běžící kopie „~6:00 celkem“ (`Remaining`)
- Úkol 6 — data nové karty úlohy v jádru (nové věci 1–3): `JobCard.Rows` (tabulka rychlostí „Čtení karty“ + každý cíl: název, podíl, MB/s; při ověření jen cíle; cíl, který oprava jen čte, „jen ověření“; pozastaveno „—“), `JobCard.Steps` (ukazatel fází Kopírování · Ověření · Proxy/Analýza · Report jen z fází, které úloha dělá; první report na konci 1. fáze patří kopii), `JobCard.Summary` (Kopie · Ověření · Proxy x / y · Celkem bez pauz — `JobProgress.Elapsed`), `JobCard.FailedRows` a `Problem` (soubor × cíl: „RAID_A ✓“, „SHUTTLE_B ✕ hash / velikost / čtení / zápis / karta / zrušeno“), titulky karet (`Headline`: „— kartu lze vysunout“, „— kartu zatím nevysunujte“ + „1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku.“, „— zkopírováno bez ověření“, „— offload selhal“, „— zrušeno“, „— záloha je úplná“), `FileLine` („Soubor 10 / 24“), `Remaining` („zbývá ~0:02 kopie · ~0:06 celkem“), `Thumbs` (prvních 5 klipů, prostřední snímek, „+N“), `BrakeSentence` („Nejpomalejší článek: čtečka karty (…). Disky by zvládly víc.“), `QueueSummary` (pilulka „Běží 1 offload · 1 ve frontě“, „Zařadit do fronty“ / „Spustit offload“); `Format.BytesCs3` (tři platné číslice: „1,70 GB“, „14,2 GB“, „412 GB“)
- Úkol 5 — oprava jako úloha: `JobConfig.RepairInto`, `JobConfigs.Repair` (vlastní kopie nastavení úlohy, která selhala — projekt, výstupy, oznámení; vždy s ověřením, bez analýzy a proxy); úloha drží stejný zámek složek jako doplnění (doplnění a oprava téže zálohy nikdy naráz, „Do této zálohy právě doplňuje jiná úloha…“), zapíše report, PDF, seznamy klipů a denní report; historie: k záznamu zálohy přidá generaci `kind: "repair"` a úspěch spočítá z reportů na cílech (poslední opravený soubor = záznam zase „ověřeno“), soubory, otisk a cívky karty se nemění; „Nic k opravě — všechny soubory jsou na všech cílech v pořádku.“ = Hotovo; `RepairRules.Offer` (jen hotová nebo selhaná, ne zrušená úloha s chybnými soubory, karta ve čtečce, žádná oprava téže zálohy ve frontě), titulek „A001 · oprava“
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: camera label, connection type and destination tile texts (plan 9, task 8)

The card inspection reads one clip (ffprobe, 5 s at most, read only) for "ALEXA 35 · LogC4";
ConnectionLabel shortens the HW diagnostics link ("čtečka USB 3 · 5 Gb/s", "NVMe"), DestinationTile
gives "7,20 TB volných z 8 TB · NTFS · NVMe" and the space bar.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\ClipDetector.cs windows\src\Offload.Core\Desktop\CardFacts.cs windows\src\Offload.Core\History\CardInspector.cs windows\tests\Offload.Core.Tests\CardFactsTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

### Task 9: Texty levého sloupce, prázdné fronty a dnešní historie (nová věc 7)

Čisté funkce pro to, co levý sloupec a prázdná fronta ukazují (`SetupTexts`), a dnešek a dny historie z
`history.json` (`HistoryView`). Okno je v úkolech 13, 14 a 16 jen naváže.

**Files:**
- Create: `windows/src/Offload.Core/Desktop/HistoryView.cs`
- Create: `windows/src/Offload.Core/Desktop/SetupTexts.cs`
- Modify: `docs/VYVOJ-WINDOWS.md`
- Test: `windows/tests/Offload.Core.Tests/SetupTextsTests.cs` (nový)

**Interfaces:**
- Consumes: `SettingsProfile`, `ProxySettings`, `LutCatalog`, `LutResolver`, `HashKinds`, `HistoryEntry` (stávající); `CameraLabel` (úkol 8); `Format.BytesCs3` (úkol 6).
- Produces:
  - `public sealed record AfterCopyRow(string Label, string Value, bool On, bool Warn = false)`.
  - `public static class SetupTexts` — `CodecShort(ProxyCodec)`, `LutShort(ProxySettings, LutCatalog?, CameraLabel?)`, `AfterCopy(SettingsProfile, LutCatalog?, CameraLabel?)`, `AfterCopyLine(SettingsProfile)`, `const string VerifyOffWarning`, `StartSummary(int cards, int disks, long? bytes)` → `(string Counts, string? Size)`, `NoCard(bool jobsRunning)` → `(string Title, string Text)`, `Compact(bool hasSources, bool hasJobs)`, `PathPreview(string date, string? cardName)`, `QueueEmpty(IReadOnlyList<string> cards, int disks, bool verify)` → `(string Title, string Text)`, `Flow(int disks, bool verify, bool analyze, bool proxy)`.
  - `public sealed record HistoryRow(string Id, string CardName, string Size, string Time, string Status, HistoryTone Tone, string? Project)`, `HistoryDay(string Title, string Summary, IReadOnlyList<HistoryRow> Rows)` (`HistoryTone` je stávající, `History/HistoryText.cs`).
  - `public static class HistoryView` — `const int MaxEntries = 300`, `Row(HistoryEntry)`, `Summary(IReadOnlyList<HistoryEntry>)`, `HistoryDay? Today(IEnumerable<HistoryEntry>, DateTime nowLocal)`, `IReadOnlyList<HistoryDay> Days(IEnumerable<HistoryEntry>, DateTime nowLocal)`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/SetupTextsTests.cs` (nový soubor)

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

/// The left column (UI návrh obr. 1–3) and the right column's today (nová věc 7).
public class SetupTextsTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    [Fact]
    public void AfterCopy_FourRows_AsTheDesign()
    {
        var p = new SettingsProfile { Proxy = new ProxySettings { Enabled = true } };
        var rows = SetupTexts.AfterCopy(p, Cat, new CameraLabel("ALEXA 35", "LogC4"));
        Assert.Equal([("Ověření zpětným čtením", "xxHash64", true), ("Analýza klipů a náhledy", "3 snímky / klip", true),
                      ("Proxy", "1080p · H.264 · LogC4 → 709", true), ("Report", "PDF · HTML · ASC MHL · CSV", true)],   // CSV is on by default
            rows.Select(r => (r.Label, r.Value, r.On)));
        Assert.All(rows, r => Assert.False(r.Warn));

        var off = new SettingsProfile { Verify = false, AnalyzeClips = false, Hash = "both", Outputs = new Outputs.OutputSettings { ClipListCsv = false, ClipListAle = true } };
        rows = SetupTexts.AfterCopy(off, Cat, null);
        Assert.Equal(("vypnuto", false, true), (rows[0].Value, rows[0].On, rows[0].Warn));
        Assert.Equal(("vypnuto", false), (rows[1].Value, rows[1].On));
        Assert.Equal(("vypnuto", false), (rows[2].Value, rows[2].On));
        Assert.Equal("PDF · HTML · ASC MHL · ALE", rows[3].Value);
    }

    [Fact]
    public void LutShort_EveryMode()
    {
        Assert.Equal("LUT podle kamery", SetupTexts.LutShort(new ProxySettings(), Cat, null));
        Assert.Equal("S-Log3 → 709", SetupTexts.LutShort(new ProxySettings(), Cat, new CameraLabel("FX6", "S-Log3")));
        Assert.Equal("LogC3 → 709", SetupTexts.LutShort(new ProxySettings { LutMode = LutMode.Builtin, LutBuiltin = "arri-logc3-awg3" }, Cat, null));
        Assert.Equal("bez LUTu", SetupTexts.LutShort(new ProxySettings { LutMode = LutMode.None }, Cat, null));
        Assert.Equal("vlastní LUT", SetupTexts.LutShort(new ProxySettings { LutMode = LutMode.Custom, LutPath = @"C:\a.cube" }, Cat, null));
        Assert.Equal(["H.264", "H.265", "ProRes Proxy", "ProRes LT", "ProRes 422", "ProRes HQ"], Enum.GetValues<ProxyCodec>().Select(SetupTexts.CodecShort));
    }

    [Fact]
    public void Compact_Line_StartBar_Empty()
    {
        var p = new SettingsProfile { Proxy = new ProxySettings { Enabled = true } };
        Assert.Equal("Ověření xxHash64 · analýza a náhledy · proxy 1080p H.264 · report PDF, HTML, MHL", SetupTexts.AfterCopyLine(p));
        Assert.Equal("Bez ověření · report PDF, HTML, MHL", SetupTexts.AfterCopyLine(new SettingsProfile { Verify = false, AnalyzeClips = false }));
        Assert.Equal(("1 karta → 2 disky", " · 1,70 GB"), SetupTexts.StartSummary(1, 2, 1_700_000_000));
        Assert.Equal(("3 karty → 1 disk", (string?)null), SetupTexts.StartSummary(3, 1, null));
        Assert.Equal("Vyberte kartu", SetupTexts.StartSummary(0, 2, 5).Counts);
        Assert.Equal(("Vložte kartu do čtečky", "Objeví se tady automaticky."), SetupTexts.NoCard(false));
        Assert.Equal("Vložte další kartu", SetupTexts.NoCard(true).Title);
        Assert.True(SetupTexts.Compact(hasSources: false, hasJobs: true));
        Assert.False(SetupTexts.Compact(hasSources: true, hasJobs: true));
        Assert.False(SetupTexts.Compact(hasSources: false, hasJobs: false));
        Assert.Equal(@"…\2026-10-07\A035_ARRI\", SetupTexts.PathPreview("2026-10-07", "A035_ARRI"));
        Assert.Equal(@"…\2026-10-07\<název karty>\", SetupTexts.PathPreview("2026-10-07", null));
    }

    [Fact]
    public void QueueEmpty_AndFlow()
    {
        Assert.Equal(("Připraveno — A035_ARRI na 2 disky",
                "Karta se přečte jednou, zapíše na oba disky zároveň a každý soubor se zpětně ověří. Další kartu můžete vložit kdykoli — zařadí se do fronty."),
            SetupTexts.QueueEmpty(["A035_ARRI"], 2, verify: true));
        Assert.Equal("Připraveno — 3 karty na 1 disk", SetupTexts.QueueEmpty(["A", "B", "C"], 1, true).Title);
        Assert.Contains("zapíše na všechny 3 disky zároveň; zpětné ověření je vypnuté.", SetupTexts.QueueEmpty(["A"], 3, verify: false).Text);
        Assert.Equal("Zatím žádný offload", SetupTexts.QueueEmpty([], 2, true).Title);
        Assert.Equal(["Karta", "2 disky naráz", "Ověření", "Proxy", "Report"], SetupTexts.Flow(2, true, true, true));
        Assert.Equal(["Karta", "1 disk", "Analýza", "Report"], SetupTexts.Flow(1, false, true, false));
    }

    static HistoryEntry Entry(string name, DateTime finishedLocal, long bytes, bool success = true, bool verified = true, int? failed = null) => new()
    {
        Id = name, CardName = name, Finished = finishedLocal.ToUniversalTime(), Bytes = bytes, Success = success, Verified = verified, FailedFiles = failed,
        Destinations = [new HistoryDestination { Root = @"D:\A" }, new HistoryDestination { Root = @"F:\B" }],
    };

    [Fact]
    public void Today_RowsAndSummary_AsTheDesign()
    {
        var now = new DateTime(2026, 10, 7, 20, 0, 0, DateTimeKind.Local);
        var entries = new[]
        {
            Entry("SND_DAY03", now.Date.AddHours(14).AddMinutes(5), 38_000_000_000),
            Entry("A034_ARRI", now.Date.AddHours(18).AddMinutes(12), 256_000_000_000),
            Entry("B011_FX6", now.Date.AddHours(16).AddMinutes(47), 118_000_000_000),
            Entry("YESTERDAY", now.Date.AddHours(-2), 1_000_000_000),
        };

        var today = HistoryView.Today(entries, now)!;

        Assert.Equal("3 karty · 412 GB · vše ověřeno", today.Summary);
        Assert.Equal([("A034_ARRI", "256 GB", "18:12", "✓ 2 kopie ověřeny"), ("B011_FX6", "118 GB", "16:47", "✓ 2 kopie ověřeny"),
                      ("SND_DAY03", "38,0 GB", "14:05", "✓ 2 kopie ověřeny")],
            today.Rows.Select(r => (r.CardName, r.Size, r.Time, r.Status)));
        Assert.Equal(["Dnes", "Včera"], HistoryView.Days(entries, now).Select(d => d.Title));
        Assert.Null(HistoryView.Today([entries[3]], now));
    }

    [Fact]
    public void Rows_ErrorsAndUnverified_OlderDays()
    {
        var now = new DateTime(2026, 10, 7, 20, 0, 0, DateTimeKind.Local);
        var entries = new[]
        {
            Entry("A", now.AddHours(-1), 2_000_000_000, success: false, failed: 3),
            Entry("B", now.AddHours(-2), 16_000_000_000, verified: false),
            Entry("C", now.AddDays(-3), 1_000_000_000),
        };
        var days = HistoryView.Days(entries, now);
        Assert.Equal(["Dnes", "4. 10. 2026"], days.Select(d => d.Title));
        Assert.Equal("2 karty · 18,0 GB · 1 karta s chybami · 1 neověřená", days[0].Summary);
        Assert.Equal([("✕ s chybami (3 soubory)", HistoryTone.Error), ("! neověřeno", HistoryTone.Warning)],
            days[0].Rows.Select(r => (r.Status, r.Tone)));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~SetupTextsTests"`

Expected: FAIL — build selže — `CS0103: Název SetupTexts v aktuálním kontextu neexistuje` (a `HistoryView`).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/Desktop/HistoryView.cs` (nový soubor)

```csharp
using System.Globalization;
using Offload.Core.History;

namespace Offload.Core.Desktop;

/// One card in "DNES" and in "Historie karet": name, size, time, "✓ 2 kopie ověřeny".
public sealed record HistoryRow(string Id, string CardName, string Size, string Time, string Status, HistoryTone Tone, string? Project);

/// One day of the history tab: "Dnes", "Včera", "6. 10. 2026" with its summary.
public sealed record HistoryDay(string Title, string Summary, IReadOnlyList<HistoryRow> Rows);

/// Today's history in the right column (UI návrh, nová věc 7) — from history.json as the window already loads it.
public static class HistoryView
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// The history tab shows at most this many entries (history.json keeps up to 5 000).
    public const int MaxEntries = 300;

    public static HistoryRow Row(HistoryEntry e)
    {
        int copies = e.Destinations.Count;
        var (status, tone) = !e.Success
            ? ($"✕ s chybami ({e.FailedFiles ?? 0} {Texts.Plural(e.FailedFiles ?? 0, "soubor", "soubory", "souborů")})", HistoryTone.Error)
            : !e.Verified ? ("! neověřeno", HistoryTone.Warning)
            : ($"✓ {copies} {Texts.Plural(copies, "kopie ověřena", "kopie ověřeny", "kopií ověřeno")}", HistoryTone.Ok);
        return new HistoryRow(e.Id, e.CardName, Format.BytesCs3(e.Bytes), e.Finished.ToLocalTime().ToString("H:mm", Cs), status, tone, e.Project);
    }

    /// "3 karty · 412 GB · vše ověřeno", "2 karty · 18,0 GB · 1 karta s chybami", "1 karta · 2,00 GB · 1 neověřená".
    public static string Summary(IReadOnlyList<HistoryEntry> entries)
    {
        int n = entries.Count;
        var parts = new List<string> { $"{n} {Texts.Plural(n, "karta", "karty", "karet")}", Format.BytesCs3(entries.Sum(e => e.Bytes)) };
        int failed = entries.Count(e => !e.Success), unverified = entries.Count(e => e.Success && !e.Verified);
        if (failed == 0 && unverified == 0) parts.Add("vše ověřeno");
        if (failed > 0) parts.Add($"{failed} {Texts.Plural(failed, "karta", "karty", "karet")} s chybami");
        if (unverified > 0) parts.Add($"{unverified} {Texts.Plural(unverified, "neověřená", "neověřené", "neověřených")}");
        return string.Join(" · ", parts);
    }

    /// The cards finished today (local time), newest first; null when there are none (the section hides).
    public static HistoryDay? Today(IEnumerable<HistoryEntry> entries, DateTime nowLocal) =>
        Days(entries, nowLocal).FirstOrDefault(d => d.Title == "Dnes");

    /// Every day with a card, newest first (at most MaxEntries cards in all): "Dnes", "Včera", then "6. 10. 2026".
    public static IReadOnlyList<HistoryDay> Days(IEnumerable<HistoryEntry> entries, DateTime nowLocal)
    {
        var today = nowLocal.Date;
        return entries.OrderByDescending(e => e.Finished).Take(MaxEntries)
            .GroupBy(e => e.Finished.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var list = g.ToList();
                var title = g.Key == today ? "Dnes" : g.Key == today.AddDays(-1) ? "Včera" : g.Key.ToString("d. M. yyyy", Cs);
                return new HistoryDay(title, Summary(list), list.Select(Row).ToList());
            })
            .ToList();
    }
}
```

#### `windows/src/Offload.Core/Desktop/SetupTexts.cs` (nový soubor)

```csharp
namespace Offload.Core.Desktop;

/// One row of "3 · Po zkopírování": the green tick (or grey ring) and the value on the right; `Warn`: the yellow row of
/// switched-off verification.
public sealed record AfterCopyRow(string Label, string Value, bool On, bool Warn = false);

/// The left column's texts (UI návrh obr. 1–3), without a window.
public static class SetupTexts
{
    /// The codec as the summary says it: "H.264", "H.265", "ProRes Proxy".
    public static string CodecShort(ProxyCodec c) => c switch
    {
        ProxyCodec.H264 => "H.264",
        ProxyCodec.Hevc => "H.265",
        ProxyCodec.ProresProxy => "ProRes Proxy",
        ProxyCodec.ProresLT => "ProRes LT",
        ProxyCodec.Prores422 => "ProRes 422",
        _ => "ProRes HQ",
    };

    /// "LogC4 → 709" for a built-in LUT or the camera of the card (automatic), else what the mode says.
    public static string LutShort(ProxySettings p, LutCatalog? catalog, CameraLabel? camera) => LutResolver.EffectiveMode(p) switch
    {
        LutMode.None => "bez LUTu",
        LutMode.Custom => "vlastní LUT",
        LutMode.Builtin => catalog?.Find(p.LutBuiltin)?.Curve is string curve ? $"{CameraLabel.ShortCurve(curve)} → 709" : "vestavěný LUT",
        _ => camera?.Curve is string c ? $"{c} → 709" : "LUT podle kamery",
    };

    /// The four rows: Ověření zpětným čtením · Analýza klipů a náhledy · Proxy · Report.
    public static IReadOnlyList<AfterCopyRow> AfterCopy(SettingsProfile p, LutCatalog? catalog, CameraLabel? camera)
    {
        var report = "PDF · HTML · ASC MHL" + (p.Outputs.ClipListCsv ? " · CSV" : "") + (p.Outputs.ClipListAle ? " · ALE" : "");
        return
        [
            new("Ověření zpětným čtením", p.Verify ? HashKinds.Label(HashKinds.FromSetting(p.Hash)) : "vypnuto", p.Verify, Warn: !p.Verify),
            new("Analýza klipů a náhledy", p.AnalyzeClips ? "3 snímky / klip" : "vypnuto", p.AnalyzeClips),
            new("Proxy", p.Proxy.Enabled ? $"{p.Proxy.Height}p · {CodecShort(p.Proxy.Codec)} · {LutShort(p.Proxy, catalog, camera)}" : "vypnuto", p.Proxy.Enabled),
            new("Report", report, true),
        ];
    }

    /// Step 3 while a copy runs, in one line: "Ověření xxHash64 · analýza a náhledy · proxy 1080p H.264 · report PDF, HTML, MHL".
    public static string AfterCopyLine(SettingsProfile p) => string.Join(" · ", new[]
    {
        p.Verify ? $"Ověření {HashKinds.Label(HashKinds.FromSetting(p.Hash))}" : "Bez ověření",
        p.AnalyzeClips ? "analýza a náhledy" : null,
        p.Proxy.Enabled ? $"proxy {p.Proxy.Height}p {CodecShort(p.Proxy.Codec)}" : null,
        "report PDF, HTML, MHL",
    }.Where(x => x != null));

    public const string VerifyOffWarning = "Bez ověření není jisté, že data na disku jsou v pořádku.";

    /// "1 karta → 2 disky" (bold in the bar) and " · 1,70 GB" (null while a size is not known); nothing ticked: "Vyberte kartu".
    public static (string Counts, string? Size) StartSummary(int cards, int disks, long? bytes)
    {
        if (cards == 0) return ("Vyberte kartu", null);
        var counts = $"{cards} {Texts.Plural(cards, "karta", "karty", "karet")} → {disks} {Texts.Plural(disks, "disk", "disky", "disků")}";
        return (counts, bytes is long b && b > 0 ? $" · {Format.BytesCs3(b)}" : null);
    }

    /// The empty step 1: "Vložte kartu do čtečky" / "Vložte další kartu" while offloads run.
    public static (string Title, string Text) NoCard(bool jobsRunning) => jobsRunning
        ? ("Vložte další kartu", "Objeví se tady sama a zařadí se za běžící offload.")
        : ("Vložte kartu do čtečky", "Objeví se tady automaticky.");

    /// The left column shrinks (steps 2 and 3 compact) while there is no card and the queue has jobs (obr. 2, 3).
    public static bool Compact(bool hasSources, bool hasJobs) => !hasSources && hasJobs;

    /// "…\2026-10-07\A035_ARRI\" (Windows separators), "…\2026-10-07\<název karty>\" without one card.
    public static string PathPreview(string date, string? cardName) => $@"…\{date}\{cardName ?? "<název karty>"}\";

    /// The empty queue: "Připraveno — A035_ARRI na 2 disky" with a ticked card, else "Zatím žádný offload".
    public static (string Title, string Text) QueueEmpty(IReadOnlyList<string> cards, int disks, bool verify)
    {
        if (cards.Count == 0 || disks == 0) return ("Zatím žádný offload", "Vyberte kartu a cíle vlevo a stiskněte Ctrl+Enter.");
        var what = cards.Count == 1 ? cards[0] : $"{cards.Count} {Texts.Plural(cards.Count, "karta", "karty", "karet")}";
        var where = $"{disks} {Texts.Plural(disks, "disk", "disky", "disků")}";
        var write = disks switch
        {
            1 => "zapíše na disk",
            2 => "zapíše na oba disky zároveň",
            _ => $"zapíše na {Texts.Plural(disks, "", $"všechny {disks} disky", $"všech {disks} disků")} zároveň",
        };
        var check = verify ? " a každý soubor se zpětně ověří" : "; zpětné ověření je vypnuté";
        return ($"Připraveno — {what} na {where}",
            $"Karta se přečte jednou, {write}{check}. Další kartu můžete vložit kdykoli — zařadí se do fronty.");
    }

    /// The five icons of the empty queue: Karta → 2 disky naráz → Ověření → Proxy → Report (only what the profile does).
    public static IReadOnlyList<string> Flow(int disks, bool verify, bool analyze, bool proxy)
    {
        var steps = new List<string> { "Karta", disks <= 1 ? "1 disk" : $"{disks} {Texts.Plural(disks, "disk", "disky", "disků")} naráz" };
        if (verify) steps.Add("Ověření");
        if (proxy) steps.Add("Proxy");
        else if (analyze) steps.Add("Analýza");
        steps.Add("Report");
        return steps;
    }
}
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~SetupTextsTests"`

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

- Úkol 8 — štítek kamery a typ připojení (nová věc 6) a dlaždice disku: `CameraHints.Probe` při prověření karty (mimo UI vlákno, jen čtení): první klip, který není proxy kamery — video přes ffprobe s limitem 5 s (`FFProbe.Probe(…, timeout)`) a automatickým výběrem LUTu (křivka vestavěného LUTu), RAW podle formátu („Blackmagic RAW“, „RED“, „ARRIRAW“, „Canon RAW“); `CardInsight.Camera` → „ALEXA 35 · LogC4“ (`CameraLabel.Model` bez výrobce, `ShortCurve`: LogC4, S-Log3, Film Gen 5, C-Log3); `ConnectionLabel.Short` z `DriveLinks` („čtečka USB 3 · 5 Gb/s“, „USB 2.0“, „SD čtečka“, „NVMe“, „SATA“, „síť“; Thunderbolt Windows nerozliší); `DestinationTile` („7,20 TB volných z 8 TB · NTFS · NVMe“, „7,20 TB volno“, pruh obsazeno / potřeba vybraných karet, barva podle kontroly místa)
- Úkol 7 — odhad času (nová věc 10): `RateMemory` si v paměti (jen tato relace) pamatuje rychlost kopie a zpětného čtení poslední úlohy od 200 MB a čas 2. fáze na sekundu záznamu zvlášť pro „jen analýza“ a pro proxy daného kodeku, výšky a datového toku (`JobProgress.Stage2Elapsed` bez pauz); odhad startu = kopie + ověření + délka klipů (z velikosti opatrným datovým tokem jako kontrola místa) × čas na sekundu — když některá část chybí, odhad není a lišta ukáže jen velikost; „odhad ~1:30 vč. proxy“, u běžící kopie „~6:00 celkem“ (`Remaining`)
- Úkol 6 — data nové karty úlohy v jádru (nové věci 1–3): `JobCard.Rows` (tabulka rychlostí „Čtení karty“ + každý cíl: název, podíl, MB/s; při ověření jen cíle; cíl, který oprava jen čte, „jen ověření“; pozastaveno „—“), `JobCard.Steps` (ukazatel fází Kopírování · Ověření · Proxy/Analýza · Report jen z fází, které úloha dělá; první report na konci 1. fáze patří kopii), `JobCard.Summary` (Kopie · Ověření · Proxy x / y · Celkem bez pauz — `JobProgress.Elapsed`), `JobCard.FailedRows` a `Problem` (soubor × cíl: „RAID_A ✓“, „SHUTTLE_B ✕ hash / velikost / čtení / zápis / karta / zrušeno“), titulky karet (`Headline`: „— kartu lze vysunout“, „— kartu zatím nevysunujte“ + „1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku.“, „— zkopírováno bez ověření“, „— offload selhal“, „— zrušeno“, „— záloha je úplná“), `FileLine` („Soubor 10 / 24“), `Remaining` („zbývá ~0:02 kopie · ~0:06 celkem“), `Thumbs` (prvních 5 klipů, prostřední snímek, „+N“), `BrakeSentence` („Nejpomalejší článek: čtečka karty (…). Disky by zvládly víc.“), `QueueSummary` (pilulka „Běží 1 offload · 1 ve frontě“, „Zařadit do fronty“ / „Spustit offload“); `Format.BytesCs3` (tři platné číslice: „1,70 GB“, „14,2 GB“, „412 GB“)
```

Nahradit:

```markdown

## 2026-10-07 — nový vzhled (UI podle docs/ui-navrh)

- Úkol 9 — texty levého sloupce a dnešní historie (nová věc 7): `SetupTexts.AfterCopy` (krok 3: Ověření zpětným čtením `xxHash64` / „vypnuto“ žlutě, Analýza klipů a náhledy `3 snímky / klip`, Proxy `1080p · H.264 · LogC4 → 709` — křivka z LUTu nebo z kamery vybrané karty, Report `PDF · HTML · ASC MHL` + zapnuté seznamy klipů), `AfterCopyLine` (krok 3 během kopie na jeden řádek), `StartSummary` („1 karta → 2 disky · 1,70 GB“), `NoCard`, `Compact`, `PathPreview` („…\2026-10-07\A035_ARRI\“), `QueueEmpty` („Připraveno — A035_ARRI na 2 disky“ + věta podle počtu disků a ověření) a `Flow` (Karta → 2 disky naráz → Ověření → Proxy → Report jen z toho, co projekt dělá); `HistoryView` z `history.json`: „DNES“ („3 karty · 412 GB · vše ověřeno“, řádek „A034_ARRI · 256 GB · 18:12 · ✓ 2 kopie ověřeny“, „✕ s chybami (3 soubory)“, „! neověřeno“) a dny pro záložku Historie karet („Dnes“, „Včera“, „4. 10. 2026“; nejvýš 300 karet)
- Úkol 8 — štítek kamery a typ připojení (nová věc 6) a dlaždice disku: `CameraHints.Probe` při prověření karty (mimo UI vlákno, jen čtení): první klip, který není proxy kamery — video přes ffprobe s limitem 5 s (`FFProbe.Probe(…, timeout)`) a automatickým výběrem LUTu (křivka vestavěného LUTu), RAW podle formátu („Blackmagic RAW“, „RED“, „ARRIRAW“, „Canon RAW“); `CardInsight.Camera` → „ALEXA 35 · LogC4“ (`CameraLabel.Model` bez výrobce, `ShortCurve`: LogC4, S-Log3, Film Gen 5, C-Log3); `ConnectionLabel.Short` z `DriveLinks` („čtečka USB 3 · 5 Gb/s“, „USB 2.0“, „SD čtečka“, „NVMe“, „SATA“, „síť“; Thunderbolt Windows nerozliší); `DestinationTile` („7,20 TB volných z 8 TB · NTFS · NVMe“, „7,20 TB volno“, pruh obsazeno / potřeba vybraných karet, barva podle kontroly místa)
- Úkol 7 — odhad času (nová věc 10): `RateMemory` si v paměti (jen tato relace) pamatuje rychlost kopie a zpětného čtení poslední úlohy od 200 MB a čas 2. fáze na sekundu záznamu zvlášť pro „jen analýza“ a pro proxy daného kodeku, výšky a datového toku (`JobProgress.Stage2Elapsed` bez pauz); odhad startu = kopie + ověření + délka klipů (z velikosti opatrným datovým tokem jako kontrola místa) × čas na sekundu — když některá část chybí, odhad není a lišta ukáže jen velikost; „odhad ~1:30 vč. proxy“, u běžící kopie „~6:00 celkem“ (`Remaining`)
- Úkol 6 — data nové karty úlohy v jádru (nové věci 1–3): `JobCard.Rows` (tabulka rychlostí „Čtení karty“ + každý cíl: název, podíl, MB/s; při ověření jen cíle; cíl, který oprava jen čte, „jen ověření“; pozastaveno „—“), `JobCard.Steps` (ukazatel fází Kopírování · Ověření · Proxy/Analýza · Report jen z fází, které úloha dělá; první report na konci 1. fáze patří kopii), `JobCard.Summary` (Kopie · Ověření · Proxy x / y · Celkem bez pauz — `JobProgress.Elapsed`), `JobCard.FailedRows` a `Problem` (soubor × cíl: „RAID_A ✓“, „SHUTTLE_B ✕ hash / velikost / čtení / zápis / karta / zrušeno“), titulky karet (`Headline`: „— kartu lze vysunout“, „— kartu zatím nevysunujte“ + „1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku.“, „— zkopírováno bez ověření“, „— offload selhal“, „— zrušeno“, „— záloha je úplná“), `FileLine` („Soubor 10 / 24“), `Remaining` („zbývá ~0:02 kopie · ~0:06 celkem“), `Thumbs` (prvních 5 klipů, prostřední snímek, „+N“), `BrakeSentence` („Nejpomalejší článek: čtečka karty (…). Disky by zvládly víc.“), `QueueSummary` (pilulka „Běží 1 offload · 1 ve frontě“, „Zařadit do fronty“ / „Spustit offload“); `Format.BytesCs3` (tři platné číslice: „1,70 GB“, „14,2 GB“, „412 GB“)
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN9`:

```text
Windows: left column texts, empty queue and today's history in Core (plan 9, task 9)

SetupTexts gives step 3 (after copy) rows, the start summary, the path preview, the empty queue
flow; HistoryView gives DNES and the days of the "Historie karet" tab from history.json.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Desktop\HistoryView.cs windows\src\Offload.Core\Desktop\SetupTexts.cs windows\tests\Offload.Core.Tests\SetupTextsTests.cs
git commit -F .git/COMMIT_MSG_PLAN9
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (zbude jen `?? windows/docs/superpowers/plans/…` plánu, pokud ještě není v repu).

---

Pokračování: úkoly 10–19 jsou v `windows/docs/superpowers/plans/2026-10-07-offload-windows-9-ui-2.md`.
