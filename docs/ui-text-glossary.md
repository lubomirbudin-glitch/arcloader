# mmMover (formerly ArcLoader and Offload) — English-only UI: rules and glossary (user decision 2026-10-10: "Jen angličtina")

The whole application becomes **English only, permanently** — Czech disappears from everything a user sees: GUI,
messages, clip notes, preflight texts, notifications (Windows + ntfy), reports (HTML/PDF/TXT, day report, verify report,
summary report, clip lists' human-readable columns), CLI help/output/errors, braw-decode messages, the manual and the
readme in the zip. Developer docs in `docs/` and code comments stay as they are (they are not part of the app).

## Product name (user decision 2026-10-11, Windows 0.11.0; 0.10.x was ArcLoader, ≤ 0.9.x Offload)
- The product is **mmMover** (exact spelling; lower case `mmmover` only for the CLI command and the repo). Window titles, About, tray and
  ntfy titles, dialogs, CLI help, reports (headers/footers), manual and READ-ME use it. The Mac app is still called Offload until its rename.
- Backup-internal names keep the old spelling (`offload_report.json`, `.offload-part`, …) — see below and docs/FORMATY.md.

## Must NOT change (on-disk format shared with the Mac and with older backups — docs/FORMATY.md)
- JSON keys and enum values, ASC MHL, `.xxh64`/`.md5`, manifest, history.json keys, settings.json keys and values.
- Names inside a backup: `_REPORT`, `ascmhl`, `_PROXY`, `neshoda_NNNN`, report file names, `<karta>_2`.
- Reading must accept everything older versions wrote, including Czech free-text fields in old reports (e.g. notes) —
  they are shown as they are.
- Machine-readable CSV/ALE column names that tools parse stay as they are (check FORMATY); human-readable values become English.

## Formatting
- Dates `2026-10-10`, times `14:14:28`, date-time `2026-10-10 14:14:28`; numbers with a decimal point (`1.70 GB`,
  `23.976`); use `CultureInfo.InvariantCulture` (or an explicit en-US/invariant formatter) — never the PC's culture.
- Sizes `GB`/`MB` as now; durations `0:01:32`; speeds `602 MB/s`.
- `<html lang="en">`.

## Local folders (not part of a backup)
- Local report copies: `Documents\mmMover reports\<date>\<card>\` (was `ArcLoader reports` in 0.10.x, `Offload reports` before, and `Offload reporty` before that); the summary report goes to
  `Documents\mmMover reports\summary\<date>_<time>_summary.html/.pdf`. Reading (summary fallback, `.offload-origin`)
  must ALSO look in the old `ArcLoader reports`, `Offload reports` and `Offload reporty` folders. The settings key for the folder stays.
- Zip contents: `READ-ME.txt` (was `CTI-ME.txt`), `mmMover-manual.pdf` (was `ArcLoader-manual.pdf`, `Offload-manual.pdf`, `Offload-navod.pdf`), manual source
  `windows/package/manual/manual.html` (was `navod/navod.html`) + `render.ps1`.

## Glossary (use consistently)
| Czech | English |
|---|---|
| Karty / karta | Cards / card |
| Cíle / cíl | Destinations / destination |
| Zdroj | Source |
| Po zkopírování | After copy |
| Spustit / Start | Start |
| Fronta | Queue |
| Kopíruji / Kopírování | Copying |
| Ověřuji / Ověření | Verifying / Verification |
| Ověřeno | Verified |
| Lze vyjmout | Safe to eject |
| Lze formátovat | Safe to format |
| Neformátovat | Do not format |
| Vysunout kartu | Eject card |
| Pozastavit / Pozastavuji (dokončuji …) / Pozastaveno | Pause / Pausing (finishing …) / Paused |
| Pokračovat | Resume |
| Zrušit | Cancel |
| Dokončit (přerušenou zálohu) | Finish (interrupted backup) |
| Doplnit (jen nové klipy) | Append (new clips only) |
| Zkopírovat znovu | Copy again |
| Ověřit zálohu | Verify backup |
| Karta už zálohovaná | Card already backed up |
| Historie karet / Dnes | Card history / Today |
| Denní report | Day report |
| Souhrnný report | Summary report |
| Report ověření | Verification report |
| Seznam klipů | Clip list |
| Náhledy | Thumbnails |
| Proxy | Proxies |
| Stopáž | Duration |
| Snímací fps / přehrávání | Capture fps / playback |
| První / poslední klapka | First / last slate |
| Kontrolní součet | Checksum |
| Neshoda / nesouhlasí | Mismatch |
| Chybí | Missing |
| Nečitelný | Unreadable |
| Varování / upozornění | Warning |
| Poznámka | Note |
| Nastavení | Settings |
| Projekt | Project |
| Struktura složek | Folder structure |
| Dekodéry RAW | RAW decoders |
| Stáhnout… | Download… |
| nenalezeno | not found |
| Vlastní soubor | custom file |
| Bez LUTu | No LUT |
| Oznámení | Notifications |
| Aktualizace | Updates |
| Hardware / test USB portů | Hardware / USB port test |
| brzdí: D1 | bottleneck: D1 |
| Ukázkový režim | Demo mode |
| Obsahová kontrola | Content check |
| Volné místo | Free space |
| Přeskočeno | Skipped |
| Hotovo | Done |
| Selhalo / chyba | Failed / error |
