# Stream B — výstupy pro produkci, část 1 (denní report + seznam klipů CSV/ALE)

Stav k 2026-10-08, větev `worktree-agent-a93bb0791e95c9fcf` (fast-forward na `main` 48b483f, pak vlastní commity).
Práce byla **ukončena předčasně** termínem koordinátora (12 min) — hotová a otestovaná je jen první stavební část.

## Hotovo (commitnuto, testy zelené)

- `Timecode.swift` — přesný drop-frame jako Windows `Timecode.cs` (plán 7, úkol 6): `nominal` (zaokrouhlení
  na sudou jako .NET `Math.Round`), `isDropFrame` (`;` jen u 29.97/59.94), `add` počítá drop-frame jako kamera
  (bez `;00/;01`, u 59.94 `;00–;03`, kromě každé desáté minuty), `length(frames:fps:)` = `HH:MM:SS:FF` v nominálních
  snímcích (sloupec Duration v CSV). Popisky náhledů tím jen zpřesní. Test `TimecodeDropFrameTests` = všechny řádky
  Windows `TimecodeEncoderTests.Add` + `Length_NominalFrames`; `TimecodeTests`, `FramePositionTests`, `ThumbnailTests`
  zelené.
- Výchozí stav: `swift build && swift test` = 272 testů, 0 selhání, 8 přeskočených (6× BRAW, protože není nastavené
  `OFFLOAD_BRAW_SAMPLE`; se vzorkem `/Applications/Blackmagic RAW/Blackmagic RAW Speed Test.app/Contents/Resources/profile.braw`
  by jich bylo 3 jako ve VYVOJ-MAC).

## Nehotovo (nic z toho není v commitu)

Denní report (funkce 7) ani seznamy klipů CSV/ALE (funkce 8) na Macu **nejsou**. ROZDILY řádky „Denní report“
a „Seznam klipů CSV / Avid ALE“ zůstávají ⏳. Návrh připravený k implementaci (zdroj: FORMATY „Denní report“, „Seznam
klipů“; Windows `CardRecord.cs`, `Outputs/ClipList.cs`, `Outputs/DayReport.cs`, `Outputs/DayReportService.cs`, testy
`CardRecordTests`, `ClipListTests`, `DayReportTests`, `DayReportServiceTests`):

1. **`CardRecord.swift`** (nový) — čte `_REPORT/offload_report.json` + všechna `offload_report_NNNN.json`
   (generace = `generation` v JSON, jinak NNNN), slučuje soubory a klipy (cesty bez ohledu na velikost písmen,
   nejnovější generace vyhrává; generace bez klipů dostane klipy z `ClipDetector.detect` jejích souborů a nikdy
   nepřepíše popsaný klip), `verified` po souborech přes sloupec cíle této složky (`ColumnOf`, jinak všechny sloupce),
   `failedFiles`, `project` (první neprázdný). Tolerance jako Windows: `clips` chybí/`null`/`[null]`, report z Macu
   i z Windows (klíče, které Mac model nezná — `generation`, `project`, `files[].md5`, `clips[].info.reel`,
   `hashAlgorithms` — číst přímo z JSON slovníku, takže to bude fungovat i po sloučení streamů A/C/P beze změny).
   Doporučení: JSONSerialization → kontrola `files`/`destinations` („chybí seznam souborů nebo cílů“) → doplnit
   chybějící `tool`/`version`/`hashAlgorithm`/`skipped` → `OffloadReport` přes JSONDecoder (data ISO 8601 i se
   zlomky sekund) → klipy jednotlivě „lossy“ (`[Lossy<ClipEntry>]`).
   **Kolize se streamem A:** Windows plán 6 staví doplnění na `CardRecord` — pokud A vytvořil vlastní `CardRecord`,
   sjednotit na jeden typ (API podle Windows).
2. **`Outputs/ClipList.swift`** — `OutputSettings` (`dayReport` true, `clipListCsv` true, `clipListAle` false),
   řádky (bez proxy kamery), CSV (BOM, CRLF, RFC 4180, sloupce dle FORMATY, `=+-@` beze změny), ALE (bez BOM, CRLF,
   tabulátory, soubor na fps `<název>_clips_<fps>.ale`, klip bez fps k nejčastějšímu, bez fps `FPS 25`,
   `VIDEO_FORMAT` 1080/720/CUSTOM, `End` přes `Timecode.add`, `Tracks` V+A1…A8), zápis přes `.<jméno>.tmp`, každý
   soubor zvlášť (první chyba až nakonec), zastaralé ALE smazat, vypnutý-ale-existující seznam přepsat. Cívka:
   tag `info.reel` → vzor jména (Blackmagic `A001_…_C001`, RED `A001_C001`, ARRI/Sony/Canon `A001C001`) → složka karty
   (po sloučení A/P použít jejich `ReelNames`/`ReelResolver`).
3. **`Outputs/DayReport.swift`** — model dne (podsložky kromě `_REPORT` a skrytých, řazení jako Finder, karta = má
   `_REPORT/` nebo `ascmhl/`, jinak „jiná složka (bez reportu)“), HTML přesně podle Windows `DayReport.Html`
   (texty, třídy, lišta „Uložit jako PDF“ — jen titulek odkazu bez Edge), Problémy, LUTy, `checkDay` (kořen svazku,
   složka bez karty s reportem, karta `_REPORT` → české chyby), zápis HTML → seznamy dne → PDF (WebKit; chyby jen
   do logu). PDF: `PDFReport.swift` rozšířit o `writeHTML(_:to:timeout:)` (render už umí A4 na šířku).
4. **`Outputs/DayReportService.swift`** — jeden zapisovatel na den: zámek v procesu + `flock` na
   `$TMPDIR/offload-day-<sha1(realpath)>.lock` (GUI + CLI), čekání 2 min, zrušená úloha 5 s, hláška „Denní report ve
   složce … právě vytváří jiná úloha — zkuste to za chvíli.“; `dayFolderOf` (karta = její den).
5. **Úloha (`Job.swift`, malý háček)** — `JobConfig.outputs`; seznamy klipů v `rewriteReports` a bez stage 2 před PDF;
   denní report každého cíle na konci (i zrušená a selhaná kopie, jako Windows `DayReportNext`: `runCopy` vrátí
   `true` a `runPost` udělá jen denní report — kvůli PDF na hlavním vlákně a aby další karta nečekala);
   `dayReports`, `dayReportPending`; `AppModel.hasBusyJobs` musí čekat i na `dayReportPending`.
6. **Engine** — prázdný `<karta>/_REPORT` hned na začátku kopie (rozpracovaná karta = „bez reportu (probíhá, nebo
   byl přerušen)“); **Layout** — jména karet `_REPORT`, `_PROXY`, `ascmhl` dostanou `_` na konec.
7. **CLI** — `offload day-report <složka> [--no-clip-list] [--ale]` („Denní report: …“, „Chyba: …“, kód 0/1),
   `copy --no-clip-list --ale --no-day-report` a řádky „Denní report: …“ po „Report: …“.
8. **GUI** — Nástroje › „Denní report…“ (výběr složky dne mimo hlavní vlákno, otevře HTML, chyba „Denní report se
   nepodařilo vytvořit: …“), přepínače „Seznam klipů CSV“, „Seznam klipů pro Avid (ALE)“, „Denní report po každé
   kartě“ ve Struktuře a kontrole, `outputs` v `AppSettings`.

## Odchylky (plánované, k potvrzení)

- Hlášky s cestou `<cíl>\<datum>` by na Macu měly mít `/` (`<cíl>/<datum>`); titulek odkazu „Otevřít PDF report“ bez
  zmínky o Edge.
- Zámek dne na Macu = `flock` souboru v `$TMPDIR` (Windows pojmenovaný mutex) — ROZDILY „Mac žádný z těchto zámků
  nezná“ by se pro denní report změnilo.

## Čeká na ruční ověření (až bude hotovo)

- Import CSV do DaVinci Resolve a ALE do Avidu; PDF denního reportu vizuálně; GUI (nikdy nespuštěno).

## Místa kolizí při slučování

- Tento commit mění jen `Timecode.swift` (vlastní nový test) — kolize nečekám.
- Budoucí práce: `CardRecord` (stream A), `PDFReport.swift` (A — report ověření), `AppSettings.swift` (C — projekty),
  `main.swift` (A — `append`, C — `--project`), `Engine.swift`/`Job.swift` (A — doplnění).
