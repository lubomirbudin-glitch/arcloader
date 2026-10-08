# Offload pro Mac — vývoj

Historie Mac verze (složka [`mac/`](../mac/)). Nejnovější nahoře. Každá změna Mac verze sem přidá záznam
ve stejném commitu; když se tím Mac a Windows v něčem rozejdou, upraví se i [ROZDILY.md](ROZDILY.md).

Formát záznamu: `datum — co se změnilo (commit / verze)`.

## Naplánováno

- **Dorovnání s Windows verzí — podklady pro Mac (2026-10-05, doplněno o BRAW po Windows plánu 3):** návrh
  [`mac/docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md`](../mac/docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md),
  plán po úkolech s testy [`mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md`](../mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md)
  (31 úkolů, pořadí 1–25, 27–31, 26) a návod jak začít [`mac/docs/superpowers/plans/README-START.md`](../mac/docs/superpowers/plans/README-START.md).
  Pokrývá všechno, co Macu podle [ROZDILY.md](ROZDILY.md) chybí: 3 snímky na klip, B1–B4 (report při zrušeném ověření, zdroj
  s `_REPORT`/`_PROXY`/`ascmhl`, dvakrát zadaný cíl, kontrola voleb), české texty, vysunutí jen kopírované karty, GUI bez
  blokování na discích, USB diagnostiku (IOKit), `offload ports`, okno Hardware, barvy proxy BT.709, hlídač ffmpeg, složku pro
  proxy, vestavěné LUTy s výběrem podle kamery, ikonu a Blackmagic RAW jako Windows (gama → LUT, BRAW jen s knihovnou, limit
  `--info`, české poznámky, náhledy pod hlídačem). Průběh zapisují úkoly plánu níže; úkol 26 tuto odrážku smaže.

- **Karty a bezpečnost dat (Windows plán 6, 2026-10-06):** návrh
  [`windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md`](../windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md)
  §2 a §8. Mac převezme: **historii** (`history.json` v Application Support ve stejném formátu — FORMATY.md „Místní
  soubory“; klíč karty = sériové číslo souborového systému + kapacita, ověřit, že u FAT/exFAT vyjde na Macu totéž
  `fsSerial` jako na Windows; řádek u karty a dialog „Karty už jsou zálohované“ ve `Views.swift`); **doplnění** (plánovač
  doplnění v OffloadKit, `MHL.swift` generace N + nahrazení řetězu, `Report.swift` klíč `generation` a jména `_NNNN`,
  zámek složky karty, `offload append`); **název z klipů** (vzory `ReelNames`, další v řadě, zástupný text); **ověření
  zálohy** (čtení MHL všech generací a hashů, report `_verify_<stamp>Z` s výjimkou pro MHL bez ignorování `_REPORT`,
  menu); **kontrolu místa** (odhad a plán po svazcích, tabulka toků, přepočet před proxy); **české hlášky** (`Msg`
  a kódy kontrol v `Engine.swift` a `main.swift`).

- **Výstupy pro produkci a střih (Windows plán 7, 2026-10-07):** návrh
  [`windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md`](../windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md)
  §3 a §8. Mac převezme: **denní report** (`<datum>/_REPORT/<datum>_day_report.html/.pdf` a seznam klipů dne, tvorba po
  úloze v `Job.swift`, stejné sloupce a texty — FORMATY.md „Denní report“); **seznamy klipů** CSV/ALE stejných sloupců
  (FORMATY.md „Seznam klipů“); **proxy pro přelinkování** (`Proxy.swift`: `reel_name` ve stopě TC, všechny zvukové
  stopy, cívka tag → název → složka, klíč `clips[].info.reel`); **MD5** (`Hash.swift` + CommonCrypto MD5, volba
  xxh64/both, `hashAlgorithms`, `files[].md5`, `<karta>.md5`, MHL `<md5>`, `verify` z MHL)); **jména karet** `_REPORT`, `_PROXY`, `ascmhl` s `_` na konci a prázdná `<karta>\_REPORT` hned při začátku kopie (D1); seznam ALE bez BOM a volba doplnění: `<karta>.md5` je třetí soubor, který doplnění přepíše (jako `.xxh64`).

- **Nastavení, oznámení, distribuce (Windows plán 8, 2026-10-07):** návrh
  [`windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md`](../windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md)
  §4 a §8. Mac převezme: **projekty** (`projects` / `activeProject` v `UserDefaults`, stejné chování přepínače — během
  startu zamčený, úloha ve frontě má vlastní kopii nastavení; `project` v reportu a historii, doplnění podle projektu
  dřívější zálohy a „Bez projektu“, když už neexistuje, další v řadě po projektech, projekt v hlavičce denního
  reportu); **vlastní pravidla LUTů** (`lutRules` v `LutResolver` před automatikou, `lut.source = rule`, cíl „Bez LUT“
  dává `none`, pravidla neznámého typu zachovat a přeskočit, `cameraSerial` ze Sony XML a QuickTime tagů);
  **`braw-decode --gen5`** v C++ (jen Film Gen 4; klon atributů klipu, `ColorScienceGen` = 5, `colorScienceGenOriginal`
  v `--info`; neověřeno bez vzorku Gen 4); **oznámení** (texty `JobOutcome`, ntfy JSON POST s pevným textem bez cest;
  úloha, která skončí v 1. fázi chybou, oznámí hned při konci kopie); **kontrolu aktualizací** (formát manifestu,
  limity velikosti, síťové cesty se neotevírají; vlastní soubor pro Mac rozhodne Mac). Podpis má Mac vlastní
  (`codesign` + notarizace).

- **Nový vzhled (Windows plán 9, 2026-10-07, 0.5.0):** návrh [`docs/ui-navrh/`](ui-navrh/) (README = zadání;
  „Rozhodnutí (2026-10-07, závazné)“ v hlavičce plánu 9). Mac převezme **až po schválení Windows verze**: rozvržení (horní lišta, kroky
  1–3, karty úloh po stavech, stránka Nastavení, dnes a historie karet), přepínač **projektů** místo presetů, tmavé
  i světlé téma podle systému; **Pozastavit** (brána mezi 16MB bloky kopie a zpětného čtení a mezi klipy 2. fáze, žádný
  proces se neuspává, zámek dne se během pauzy nedrží, report `copyPausedSeconds` / `verifyPausedSeconds`);
  **Zkopírovat znovu** (oprava jako generace ASC MHL N: jen selhané soubory, jen na cíle, kde selhaly, dobrá kopie
  `verified`, nesouhlasná do `_REPORT/neshoda_NNNN/`, `kind: "repair"`, `rewritten`, `setAside`, historie
  `generations[].kind = repair` — FORMATY.md „Oprava“); štítek kamery z prvního klipu a typ připojení (Mac pozná
  i Thunderbolt); odhad času jen z rychlostí naměřených v relaci.

- **Mimo plán — zbývá i po něm:**
  - Blackmagic RAW Film Gen 4: Windows od 0.4.0 umí převod na Gen 5 (`--gen5`, výchozí vypnuto, neověřeno — čeká vzorek
    Gen 4); Mac zatím bez LUTu (provizorně) — rozhodne uživatel.
  - Nastavení nástrojů: řádek „braw-decode“ ukazuje „nalezeno“ i bez knihovny Blackmagic RAW (stav BRAW je vidět z poznámky
    u klipu) — otevřené i na Windows.
  - Ověření se vzorkem `.braw` a ostrý test s kartou, REDline a art-cmd — ruční, postup v README-START (oddíly 6–7).
  - Náhledy R3D/ARRI bez proxy, zrušení `ffprobe`, selhání kopie proxy v souhrnu CLI — otevřené i na Windows (návrh, oddíl 4).

- **Převzít z Windows verze — HTML reporty (2026-10-08):** (1) lišta jen pro obrazovku nahoře v HTML reportu karty, denním
  i ověření: tlačítko „Uložit jako PDF“ (`window.print()`), nápověda „v tisku zvolte „Uložit jako PDF““ a odkaz „Otevřít PDF
  report“ na PDF vedle HTML; v `@media print` skrytá; (2) tisk z prohlížeče: `@page{size:A4 landscape;margin:10mm}`,
  světlé barvy i z tmavého režimu, `tr{break-inside:avoid}`; (3) tabulka klipů se vejde do okna: `table-layout:fixed` +
  `<colgroup>` v %, `overflow-wrap:anywhere`, náhledy `flex:1 1 0;max-width:128px`, v úzkém okně skrýt Formát/TC/Audio.
  Podrobnosti ve [VYVOJ-WINDOWS.md](VYVOJ-WINDOWS.md), sekce „2026-10-08 — report: uložení do PDF“.

## 2026-10-04 — přesun do `mac/`

- Celý Mac projekt (`Package.swift`, `Sources/`, `Tools/`, `scripts/`, `docs/`, README) přesunut do složky
  `mac/` přes `git mv` — historie souborů zůstává. Kód beze změny; build se spouští ze složky `mac/`
  (`scripts/build-app.sh`), relativní cesty v něm i v hledání nástrojů platí dál.

## 2026-10-04 — verze 0.1.0 (commit `7341e44`)

První verze v gitu:

- kopie karty na více disků najednou, karta čtená jen jednou (bloky 16 MB, bez page cache, `F_NOCACHE`),
- ověření zpětným čtením z každého disku po `F_FULLFSYNC`, xxHash64; „kartu lze vysunout“ až po ověření,
- report `_REPORT/`: HTML s náhledy, metadaty a TC, JSON, TXT, seznam `.xxh64`, PDF (WebKit),
- ASC MHL v2 v `ascmhl/`,
- analýza klipů (ffprobe) a proxy: scale → LUT (.cube) → burn-in názvu a TC → H.264/HEVC (VideoToolbox)
  nebo ProRes do `_PROXY/`, s TC stopou; ořez na frame lines,
- RAW: Blackmagic RAW (`braw-decode`), RED R3D (REDline), ARRIRAW / HDE / ARRICORE (art-cmd);
  Canon RAW Light a Sony X-OCN jen kopie a ověření,
- SwiftUI aplikace: detekce karet, fronta, souběh kopie a renderu proxy, vysunutí karty,
- CLI `offload copy | verify | bench`,
- popis a ukázky v `mac/docs/` (popis.html, Offload-popis.pdf, overview-en.html, ukázkový report ARRI).
