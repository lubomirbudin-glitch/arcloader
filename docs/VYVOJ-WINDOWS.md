# Offload pro Windows — vývoj

Historie Windows verze (složka [`windows/`](../windows/)). Nejnovější nahoře. Každá změna Windows verze sem
přidá záznam ve stejném commitu; když se tím Mac a Windows v něčem rozejdou, upraví se i [ROZDILY.md](ROZDILY.md).

Podrobný návrh: [`windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md`](../windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md)
Plány: [`windows/docs/superpowers/plans/`](../windows/docs/superpowers/plans/)

## Plány

| Plán | Obsah | Stav |
|---|---|---|
| 1 | jádro kopírování, ověření, reporty JSON/TXT/HTML/xxh64/MHL, CLI | hotovo (čeká ostrý test na kartě) |
| 2 | analýza klipů, 3 snímky na klip, proxy (ffmpeg, NVENC/QSV/AMF), REDline, art-cmd, PDF | hotovo (čeká ostrý test na skutečném médiu) |
| 3 | `braw-decode.exe` (C++, Blackmagic RAW SDK pro Windows) | naplánováno |
| HW | test USB portů při startu, doporučení zapojení, propustnost článků HW při kopii, `offload ports` | hotovo (CLI) |
| 4 | WPF GUI, detekce a vysunutí karet, balíček | naplánováno |

## 2026-10-05 — plán 4 (GUI), průběh

- Úkol 9 — Hardware v GUI: dotaz „Prověřit USB porty?“ při každém startu, okno Hardware (připojení, doporučení, měření s potvrzením, USB porty s vlastními názvy), sekce Hardware s posledním testem
- Úkol 8 — spuštění a fronta: karty úloh s průběhem, rychlostmi, odhadem času a řetězem připojení s úzkým hrdlem, zelený pruh a vysunutí karty, report a Průzkumník, zrušení, zavření okna během kopie, zvuk a blikání v hlavním panelu
- Úkol 7 — levý panel: karty (automaticky při vložení, ruční složka, název složky), cíle (volné místo, souborový systém, nepřipojený disk), datum a ověření, proxy, dekodéry
- Úkol 6 — projekt GUI Offload.App (WPF, Fluent, světlý/tmavý režim), okno se dvěma panely, build.ps1 přidá Offload.exe, skript pro ovládání a snímky GUI
- Úkol 5 — fronta: karty se kopírují za sebou, proxy předchozí karty se renderují souběžně s kopií další; zrušená nebo padající úloha frontu nezastaví
- Úkol 4 — karta úlohy v jádru: rychlosti a odhad času jako na Macu, průběh fáze, texty s českým množným číslem, úzké hrdlo; zrušená úloha ve frontě hlásí „Zrušeno“
- Úkol 3 — vysunutí: karta ze čtečky jako médium (čtečka zůstane), USB disk jako zařízení; zamčení svazku odhalí otevřené soubory
- Úkol 2 — svazky: nabídka karet (vyměnitelné a USB/SD disky, nikdy systémový ani cílový disk), „vypadá jako karta“ 1:1 s Macem, výběr po připojení/odpojení, argumenty aplikace
- Úkol 1 — nastavení aplikace (settings.json kompatibilní s Mac klíči, odolné vůči poškozeným sekcím), kontrola LUT a ffmpeg před startem

## 2026-10-05 — plán HW (USB diagnostika), průběh

- Závěrečné opravy — jeden popisek portu všude (řádek disku, seznam portů, doporučení; port hubu jako „port 7 › 2“, řadič v názvu jen při více řadičích), řadiče podle identity místo názvu, USB 3 hub/dok = obě poloviny sloučené podle čísla portu, neznámý typ portu ≠ „jen USB 2.0“, síťový disk a české názvy sběrnic, sdílený hub + rada rozdělit čtečku a disky na různé řadiče, „brzdí“ ve stavovém řádku podle zásoby nezapsaných bloků (ne podle okamžité rychlosti) a před názvem souboru, `offload ports`: cíl na svazku karty se neměří ani nepřijme, Ctrl+C při měření dokončí aktuální zařízení a smaže jeho dočasný soubor, štítek písmene disku u portu, skenování pokračuje i při chybě jednoho řadiče (`2e25744`, `f185312`)
- Úkol 8 — CLI `offload ports [--measure]`, řetěz připojení a úzké hrdlo v `offload copy` (`1459a3d`)
- Úkol 7 — test HW: rychlý (topologie, linky) a s měřením (čtení karty, zápis + čtení cílů), uložení výsledku (`35bd724`)
- Úkol 6 — řetěz článků během kopie: rychlost, % linky, součet na řadiči, úzké hrdlo (`4e658c2`)
- Úkol 5 — doporučení: zařízení pod svými možnostmi (port vs. kabel), sdílený řadič, volné rychlé a pomalé porty, odhad rychlosti offloadu (`7ca2bf5`)
- Úkol 4 — kde sedí disk: sběrnice (USB/SATA/NVMe…), řadič, port, rychlost linky, UASP (`7dd7211`)
- Úkol 3 — topologie USB: řadiče, huby, porty, fyzické konektory (USB 2 + USB 3 dvojče), externí huby (`e4da377`)
- Úkol 2 — rychlost linky a schopnost zařízení (USB 2.0 / USB 3 5 Gb/s / 10 Gb/s+), oprava pasti „Speed=High“ u USB 3 (`e3f1ebd`)
- Úkol 1 — přístup ke stromu zařízení a USB hubům bez admin práv (cfgmgr32, IOCTL) (`5e2e89f`, `85965b9`)

## 2026-10-05 — plán 2 (klipy, proxy, PDF), průběh

- Závěrečná kontrola plánu 2 — opravy: přesný prostřední snímek, `%` v názvu, poslední snímek u delšího zvuku, enkodér v reportu, zrušitelné náhledy, R3D/ARRI bez falešné chyby náhledu, proxy zůstane hotová i při selhání kopie na další cíl, test HEVC a záložního enkodéru, `tools\bin` jen pro vývojový build, build skript přeskočí malé shimy z PATH (`cea9059`)
- Úkol 11 — balíček s ffmpeg/ffprobe, dokumentace (`ada13d7`, `833c53a`)
- Úkol 10 — CLI: volby proxy (--proxy, --lut, --proxy-codec, --frame-aspect …) a souhrn klipů (`4bbb154`, `5666d65`)
- Úkol 9 — fáze 2 úlohy: analýza klipů, 3 snímky, proxy na všechny cíle, PDF; selhání nikdy nezmění výsledek kopie (`c0f75c5`, `59ec55b`)
- Úkol 8 — PDF report přes Microsoft Edge (headless), A4 na šířku, funguje i z příkazové řádky (`0ce9acb`)
- Úkol 7 — HTML report: tři snímky na klip s popisky TC, stránka A4 na šířku pro tisk (`fd4d148`)
- Úkol 6 — renderer: 3 snímky na klip (první, prostřední, poslední) s popiskem TC, render proxy s LUT a burn-in, zrušení bez zbytků (`1fb9ecc`, `ad061fe`)
- Úkol 5 — RAW backendy: Blackmagic RAW, RED (REDline), ARRIRAW (art-cmd), Canon RAW jen kopie; metadata a parametry testované bez nástrojů (`c3f6f3a`)
- Úkol 4 — filter graph pro náhledy a proxy (ořez na frame lines, LUT, burn-in názvu a TC) jen s relativními jmény souborů (`479d570`)
- Úkol 3 — timecode pro popisky náhledů, automatická volba enkodéru (NVENC → QSV → AMF → software) (`ce95d57`)
- Úkol 2 — detekce klipů (video, R3D segmenty, ARRI sekvence, BRAW, Canon RAW, proxy kamery) a čtení metadat přes ffprobe (`abd7669`)
- Úkol 1 — hledání externích nástrojů (ffmpeg, REDline, art-cmd, braw-decode) a spouštění procesů (`c140b2b`)

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
