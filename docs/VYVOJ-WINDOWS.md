# Offload pro Windows — vývoj

Historie Windows verze (složka [`windows/`](../windows/)). Nejnovější nahoře. Každá změna Windows verze sem
přidá záznam ve stejném commitu; když se tím Mac a Windows v něčem rozejdou, upraví se i [ROZDILY.md](ROZDILY.md).

Podrobný návrh: [`windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md`](../windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md)
Plány: [`windows/docs/superpowers/plans/`](../windows/docs/superpowers/plans/)

## Plány

| Plán | Obsah | Stav |
|---|---|---|
| 1 | jádro kopírování, ověření, reporty JSON/TXT/HTML/xxh64/MHL, CLI | hotovo (čeká ostrý test na kartě) |
| 2 | analýza klipů, 3 snímky na klip, proxy (ffmpeg, NVENC/QSV/AMF), REDline, art-cmd, PDF | naplánováno |
| 3 | `braw-decode.exe` (C++, Blackmagic RAW SDK pro Windows) | naplánováno |
| HW | test USB portů při startu, doporučení zapojení, propustnost článků HW při kopii, `offload ports` | naplánováno |
| 4 | WPF GUI, detekce a vysunutí karet, balíček | naplánováno |

## 2026-10-05 — plán 2 (klipy, proxy, PDF), průběh

- Úkol 9 — fáze 2 úlohy: analýza klipů, 3 snímky, proxy na všechny cíle, PDF; selhání nikdy nezmění výsledek kopie
- Úkol 8 — PDF report přes Microsoft Edge (headless), A4 na šířku, funguje i z příkazové řádky
- Úkol 7 — HTML report: tři snímky na klip s popisky TC, stránka A4 na šířku pro tisk
- Úkol 6 — renderer: 3 snímky na klip (první, prostřední, poslední) s popiskem TC, render proxy s LUT a burn-in, zrušení bez zbytků
- Úkol 5 — RAW backendy: Blackmagic RAW, RED (REDline), ARRIRAW (art-cmd), Canon RAW jen kopie; metadata a parametry testované bez nástrojů
- Úkol 4 — filter graph pro náhledy a proxy (ořez na frame lines, LUT, burn-in názvu a TC) jen s relativními jmény souborů
- Úkol 3 — timecode pro popisky náhledů, automatická volba enkodéru (NVENC → QSV → AMF → software)
- Úkol 2 — detekce klipů (video, R3D segmenty, ARRI sekvence, BRAW, Canon RAW, proxy kamery) a čtení metadat přes ffprobe
- Úkol 1 — hledání externích nástrojů (ffmpeg, REDline, art-cmd, braw-decode) a spouštění procesů

## 2026-10-04 — plán 1 (jádro + CLI), průběh

Implementace po úkolech; každý úkol napsal subagent a zkontroloval nezávislý reviewer.
- Závěrečná kontrola plánu 1 — opravy: JSON/TXT/HTML s konci řádků `\n`, nulování přesahu nebufferovaného zápisu, report při zrušeném ověření nepočítá hotové soubory jako chybné, odmítnutí zdroje, který je sám offload, dvakrát zadaného cíle a neplatných voleb (velikost bloku, buffery, datum), opravená dokumentace
- Úkol 17 — build skript scripts\build.ps1 (self-contained offload.exe), README, MHL ověřený nástrojem ascmhl (`becfbad`)
- Úkol 16 — CLI offload.exe: příkazy copy (výchozí), verify a bench, stavový řádek, Ctrl+C a návratové kódy (`e6fbcb1`, `a1fac4e`)
- Úkol 15 — Bench: měření rychlosti čtení karty, zápisu na disk a hashovací propustnosti (`355cfb8`)
- Úkol 14 — OffloadJob s stavem pro CLI a aplikaci (`87312a6`, `6408bdb`)
- Úkol 13 — Verifier: opětovné ověření offloadu proti seznamu xxh64 v _REPORT (`e6699f7`)
- Úkol 12 — engine: jedno čtení karty rozvětvené na hasher a všechny cíle, flush, ověření po jednom vlákně na cíl, zápis reportu (`bcbc69a`)
- Úkol 11 — Engine preflight (kontroly svazků, místa, Windows názvů, FAT32) (`e725f74`)
- Úkol 10 — kopírovací pipeline: buffer pool, hasher zdroje a zapisovač cíle (nebufferovaný zápis, `.offload-part`, ověření zpětným čtením) (`b34b51f`)
- Úkol 9 — HTML report a zápis reportu (`4d29657`)
- Úkol 8 — ASC MHL v2 se seznamem a C4 řetězem (`757e8dc`)
- Úkol 7 — `OffloadReport`: JSON kompatibilní s Mac (seřazené klíče, data UTC na sekundy, bez `null`),
  textový souhrn, seznam `.xxh64`; nové volitelné pole `unbuffered` u cíle (`28d1b11`)
- Úkol 6 — datové typy enginu a médií, názvy v JSON jako Swift `Codable` (`46b00bc`)
- Úkol 5 — hash souboru xxh64 čtený mimo cache (`0ed25a5`)
- Úkol 4 — scanner karty: přeskakuje systémové soubory Macu i Windows, hlásí kolize velikosti písmen
  a názvy nepovolené ve Windows (`036c6f5`)
- Úkol 3 — struktura cílových složek, čištění názvů karet pro Windows, `NAME_2` pro opakovanou kartu (`bb90ca4`)
- Úkol 2 — nativní I/O: `FILE_FLAG_NO_BUFFERING`, `FlushFileBuffers`, `MoveFileExW`, identita svazku,
  ochrana před uspáním, fallback na buffered I/O (`8683fc5`)
- Úkol 1 — kostra řešení .NET 10 (`Offload.sln`, Core, CLI, testy), formátování velikostí a rychlostí (`1eaf20d`)

## 2026-10-04 — přesun projektů do vlastních složek

- Mac projekt přesunut do `mac/`, Windows je v `windows/`, společná dokumentace v `docs/`
  (rozdíly, formát na disku, vývoj obou verzí).

## 2026-10-04 — návrh a plán 1

- Specifikace Windows verze (`7f00a60`): C# / .NET 10 + WPF, plná parita s Mac, výstupy na disku shodné
  s Mac verzí, CLI v `bin\offload.exe`, kontroly navíc (kolize velikosti písmen, nepovolené názvy, FAT32).
- Doplněno na žádost uživatele (`31faf13`): 3 snímky na klip (i na Macu), test USB portů při každém startu
  s doporučením zapojení, propustnost jednotlivých článků HW během kopie.
- Plán 1 — jádro + CLI, 17 úkolů (`31faf13`).
