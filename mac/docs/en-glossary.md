# Offload for Mac — English glossary (Czech → English)

Since 2026-10-10 the Mac app is **English only**: app texts, messages, clip notes, the CLI and the reports
(HTML / PDF / TXT). Windows stays Czech (docs/ROZDILY.md). Use these terms everywhere — app, CLI, reports — so one
thing always has one name. British spelling (recognised, analysing, cancelled, colour), 24-hour times.

Code identifiers, JSON keys, file and folder names (`_REPORT`, `_PROXY`, `ascmhl`, `offload_report.json` …) never
change (docs/FORMATY.md); only human texts inside them are English.

## Things

| Czech | English | Notes |
|---|---|---|
| karta | card | "1 card", "3 cards" |
| zdroj | source | |
| cíl | destination | role label "Destination 1"; short "D1" in rates and the CLI status line |
| cíle (sekce) | Destinations | |
| záloha | backup | |
| složka karty | card folder | `<destination>/<date>/<card>` |
| složka pro proxy | folder for proxies | the extra proxy folder (`--proxy-folder`) |
| disk / svazek | drive / volume | "drive" in sentences, "volume" when it is about the file system |
| čtečka | reader | card reader |
| řadič | controller | "USB controller XHC1" |
| linka | link | "the link is full" |
| port | port | "port 6 USB 3", "port 3 USB 2.0 only" |
| klip | clip | "1 clip", "4 clips" |
| soubor | file | "1 file", "2 files" |
| kontrolní součet | checksum | the list `<card>.xxh64` = checksum list |
| report | report | |
| proxy | proxy / proxies | |
| proxy kamery | camera proxy | |
| náhledy | thumbnails | |
| vestavěný LUT | built-in LUT | |
| vlastní LUT | custom LUT | |
| záložní LUT | fallback LUT | |
| bez LUT | no LUT | |
| výřez (frame lines) | frame lines | |
| varování | warning | "no warnings", "1 warning", "2 warnings" |
| úzké hrdlo / brzdí | bottleneck / limited by | |

## Actions and states

| Czech | English |
|---|---|
| Kopírovat / kopie / kopírování | Copy / copy / Copying |
| Ověřit / ověření / ověřování | Verify / verification / Verifying |
| ověřeno | verified |
| zapsáno | written |
| nezpracováno | pending |
| Ověřit zálohu | Verify backup |
| Zkopírovat znovu | Copy again |
| doplnit zálohu / doplnění | append to the backup / append |
| oprava | repair |
| celá kopie | full copy |
| Čeká | Waiting |
| Zápis cache disků | Flushing disk caches |
| Analýza klipů | Analysing clips |
| Render proxy | Rendering proxies |
| Hotovo | Done |
| Chyba | Error |
| Zrušeno | Cancelled |
| přeskočeno | skipped |
| nepodporováno | unsupported |
| selhalo | failed |
| kartu lze vysunout | safe to eject |
| kartu lze formátovat | safe to format |
| Vysunout kartu | Eject card |
| karta vysunuta | Card ejected |
| zbývá 0:10 | 0:10 left |
| Spustit offload | Start offload |
| měření | measurement / measuring |
| doporučení | advice |

## Message patterns

| Czech | English |
|---|---|
| X nenalezen | X not found |
| X nejde přečíst | Cannot read X |
| Na cíl nejde zapisovat | Cannot write to the destination |
| Na cíli X není dost místa: potřeba …, volno … | Not enough space on X: needs …, free … |
| Složka pro proxy není dostupná: … | Folder for proxies is not available: … |
| kamera nerozpoznána — záložní LUT | camera not recognised — fallback LUT |
| … — doplnit nejde | … — cannot append |
| … — opravit nejde | … — cannot repair |
| Chyba: … (CLI) | Error: … |
| Varování: … (CLI) | Warning: … |
| Poznámka: … (CLI) | Note: … |

## Report and clip-note texts

| Czech | English |
|---|---|
| OVĚŘENO / ZKOPÍROVÁNO — NEOVĚŘENO / CHYBA (badge) | VERIFIED / COPIED — NOT VERIFIED / FAILED |
| neprovedeno | not run |
| Vynechané systémové soubory | Skipped system files |
| Všechny soubory | All files |
| automaticky podle kamery | auto by camera |
| vypnuto v nastavení (LUT reason) | off in settings (`LutUse.offInSettings`) |
| vybraný v nastavení | selected in settings |
| (vlastní soubor) | (own file) |
| není log | not log |
| předpoklad LogC4 | assuming LogC4 |
| ffmpeg se zasekl (60 s bez nového snímku) | ffmpeg stalled (60 s without a new frame) |
| proxy nevznikla | proxy not created |
| žádná ověřená kopie | no verified copy |
| kopie na X selhala | copy to X failed |

## Shared constants in OffloadKit (use them instead of literals)

- Roles: `RoleLink.card` ("Card"), `RoleLink.destination(n)` ("Destination n"), `RoleLink.isCardRole` (also accepts
  the old "Karta").
- Bottleneck line of the copy chain: `ChainView.bottleneckPrefix` ("Bottleneck: ").
- Job phases: `JobStatus.Phase.label`; counts: `Texts.cards/destinations/files/clips/warnings`.
- Messages: `Msg.*`, `Texts.*`, `QueueRules.otherCardText/inUseText`, `CardEject.problem`, `ProxyPreflight.*`.

## Formats

- Sizes in sentences: `formatBytesShort` — "3.2 GB", "412 GB" (decimal point); tables: `formatBytes` — "2.50 GB".
- Dates in reports: "Oct 10, 2026 14:05:12" (en_US_POSIX, 24-hour); the TXT report keeps ISO 8601.
- Rates: "120 MB/s"; durations "1:02:03" / "2:05".
