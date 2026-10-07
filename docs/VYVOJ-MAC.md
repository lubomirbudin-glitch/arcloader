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

- **Mimo plán — zbývá i po něm:**
  - Blackmagic RAW Film Gen 4: zatím bez LUTu (provizorně, jako Windows) — rozhodne uživatel; platí pro obě verze.
  - Nastavení nástrojů: řádek „braw-decode“ ukazuje „nalezeno“ i bez knihovny Blackmagic RAW (stav BRAW je vidět z poznámky
    u klipu) — otevřené i na Windows.
  - Ověření se vzorkem `.braw` a ostrý test s kartou, REDline a art-cmd — ruční, postup v README-START (oddíly 6–7).
  - Náhledy R3D/ARRI bez proxy, zrušení `ffprobe`, selhání kopie proxy v souhrnu CLI — otevřené i na Windows (návrh, oddíl 4).

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
