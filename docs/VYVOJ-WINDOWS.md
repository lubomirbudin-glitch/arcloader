# Offload pro Windows — vývoj

Historie Windows verze (složka [`windows/`](../windows/)). Nejnovější nahoře. Každá změna Windows verze sem
přidá záznam ve stejném commitu; když se tím Mac a Windows v něčem rozejdou, upraví se i [ROZDILY.md](ROZDILY.md).

Podrobný návrh: [`windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md`](../windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md)
Plány: [`windows/docs/superpowers/plans/`](../windows/docs/superpowers/plans/)

## Plány

| Plán | Obsah | Stav |
|---|---|---|
| 1 | jádro kopírování, ověření, reporty JSON/TXT/HTML/xxh64/MHL, CLI | rozpracováno |
| 2 | analýza klipů, 3 snímky na klip, proxy (ffmpeg, NVENC/QSV/AMF), REDline, art-cmd, PDF | naplánováno |
| 3 | `braw-decode.exe` (C++, Blackmagic RAW SDK pro Windows) | naplánováno |
| HW | test USB portů při startu, doporučení zapojení, propustnost článků HW při kopii, `offload ports` | naplánováno |
| 4 | WPF GUI, detekce a vysunutí karet, balíček | naplánováno |

## 2026-10-04 — plán 1 (jádro + CLI), průběh

Implementace po úkolech; každý úkol napsal subagent a zkontroloval nezávislý reviewer.
- Úkol 14 — OffloadJob s stavem pro CLI a aplikaci
- Úkol 13 — Verifier: opětovné ověření offloadu proti seznamu xxh64 v _REPORT
- Úkol 12 — engine: jedno čtení karty rozvětvené na hasher a všechny cíle, flush, ověření po jednom vlákně na cíl, zápis reportu

- Úkol 11 — Engine preflight (kontroly svazků, místa, Windows názvů, FAT32)
- Úkol 10 — kopírovací pipeline: buffer pool, hasher zdroje a zapisovač cíle (nebufferovaný zápis, `.offload-part`, ověření zpětným čtením)
- Úkol 9 — HTML report a zápis reportu
- Úkol 8 — ASC MHL v2 se seznamem a C4 řetězem (`165d68b`)
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
