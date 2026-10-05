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

- **Mimo plán — zbývá i po něm:**
  - Blackmagic RAW Film Gen 4: zatím bez LUTu (provizorně, jako Windows) — rozhodne uživatel; platí pro obě verze.
  - Nastavení nástrojů: řádek „braw-decode“ ukazuje „nalezeno“ i bez knihovny Blackmagic RAW (stav BRAW je vidět z poznámky
    u klipu) — otevřené i na Windows.
  - Ověření se vzorkem `.braw` a ostrý test s kartou, REDline a art-cmd — ruční, postup v README-START (oddíly 6–7).
  - Náhledy R3D/ARRI bez proxy, zrušení `ffprobe`, selhání kopie proxy v souhrnu CLI — otevřené i na Windows (návrh, oddíl 4).

## 2026-10-05 — dorovnání s Windows (plán mac-parity), průběh

Plán: [`mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md`](../mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md)

- Úkol 3 — report při zrušeném nebo vypnutém ověření nepočítá zapsané/ověřené soubory jako chybné (B1); test tvaru JSON a čtení reportu z Windows
- Úkol 2 — preflight jako Windows: kontrola voleb (blok, buffery, datum), odmítnutí dvakrát zadaného cíle a zdroje, který je sám offload (B2–B4)
- Úkol 1 — testovací target `OffloadKitTests` (XCTest, `swift test`), pomocníci pro dočasné složky, náhodná data a ffmpeg klipy

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
