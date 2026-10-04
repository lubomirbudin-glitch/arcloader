# Offload pro Mac — vývoj

Historie Mac verze (složka [`mac/`](../mac/)). Nejnovější nahoře. Každá změna Mac verze sem přidá záznam
ve stejném commitu; když se tím Mac a Windows v něčem rozejdou, upraví se i [ROZDILY.md](ROZDILY.md).

Formát záznamu: `datum — co se změnilo (commit / verze)`.

## Naplánováno

- **3 snímky na klip v reportu** (první, prostřední, poslední frame) místo jednoho náhledu ve 30 % délky —
  na žádost uživatele, aby Mac i Windows reporty vypadaly stejně. Změna v `Proxy.swift`, `Job.swift`,
  `Report.swift`, `HTMLReport.swift`; navrženo ve specifikaci Windows verze (oddíl 6.5a). Sestavit a ověřit
  se musí na Macu.

- **Totéž chování jako ve Windows verzi** (zavedeno při závěrečné kontrole plánu 1 Windows, viz
  [ROZDILY.md](ROZDILY.md)): report při zrušeném ověření (`verified` = false) nepočítá už zapsané/ověřené
  soubory jako chybné; odmítnout zdroj, který obsahuje `_REPORT`/`_PROXY`/`ascmhl`; odmítnout dvakrát zadaný
  cíl; ověřit volby (velikost bloku, počet bufferů, tvar data) před kopií.

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
