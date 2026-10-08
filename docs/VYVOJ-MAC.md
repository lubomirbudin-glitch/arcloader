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

- **Dorovnání 2 s Windows 0.6.0 — plány 6–10 a dodatky 0.5.x (2026-10-08):** návrh — oddíl „Doplněk 3“ (D.21–D.28)
  v [`mac/docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md`](../mac/docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md),
  plán po úkolech s kódem a testy [`mac/docs/superpowers/plans/2026-10-08-offload-mac-parity-2.md`](../mac/docs/superpowers/plans/2026-10-08-offload-mac-parity-2.md)
  (úkoly 32–55: karty a bezpečnost dat, výstupy pro produkci a střih) a
  [`mac/docs/superpowers/plans/2026-10-08-offload-mac-parity-3.md`](../mac/docs/superpowers/plans/2026-10-08-offload-mac-parity-3.md)
  (úkoly 56–70: nastavení, oznámení, distribuce; Pozastavit, Zkopírovat znovu, nový vzhled; Verify-in-Proxy), pořadí a milníky
  v [`README-START`](../mac/docs/superpowers/plans/README-START.md) (oddíl 4). Začíná po úkolu 26 (0.2.0). Pokrývá všechno, co tu
  dřív stálo jako „Karty a bezpečnost dat“, „Výstupy pro produkci a střih“, „Nastavení, oznámení, distribuce“, „Nový vzhled“
  a „Převzít z Windows“ (HTML reporty 2026-10-08, proxy 0.5.1, Verify-in-Proxy 0.6.0) — mapa v D.28. Rozhodnutí oproti dřívějším
  odrážkám: ověření během proxy přes **HTTP tap pro ffmpeg** jako Windows, ne AVFoundation (D.23.7); „ověřeno“ z čtení jen
  s důkazem `mincore`, že soubor není v cache (D.23.1); sériové číslo karty z boot sektoru (D.23.2); zámky `flock` (D.23.4);
  oznámení `UNUserNotificationCenter` (D.23.5); vlastní manifest aktualizací `offload-update-mac.json` a notarizace s podpisem
  všech binárek (D.24); nový vzhled až po schválení Windows verze (D.25). Úkol 70 tuto odrážku smaže.

- **Mimo plán — zbývá i po obou plánech** (žádný úkol to neřeší):
  - Blackmagic RAW Film Gen 4: zda klip ve Film Gen 4 **bez** převodu dostane LUT — rozhodne uživatel (převod `--gen5` přinese
    úkol 58, výchozí vypnuto, neověřeno bez vzorku Gen 4; stejně jako Windows).
  - Nastavení nástrojů: řádek „braw-decode“ ukazuje „nalezeno“ i bez knihovny Blackmagic RAW — otevřené i na Windows.
  - Ověření se vzorkem `.braw`, ostrý test s kartou, REDline a art-cmd — ruční, postup v README-START (oddíly 6–7); úkoly
    jen říkají, co zkusit.
  - Náhledy R3D/ARRI bez proxy, zrušení `ffprobe`, selhání kopie proxy v souhrnu CLI — otevřené i na Windows.
  - Zámky Macu a Windows se navzájem nevidí: Mac a PC, které zároveň doplňují do téže sdílené zálohy, se nevylučují (jako dva PC;
    spec D.23.4) — bez sdíleného zámku na cíli to vyřešit nejde a ten FORMATY nemá.
  - Ukázkový režim `--demo` (Windows plán 9, pomůcka pro snímky) — pro Mac se neplánuje.
  - Chyby parseru CLI (`swift-argument-parser`) zůstanou anglicky — knihovna nemá lokalizaci.
  - Apple Developer ID (placený účet) pro notarizaci — rozhodne uživatel; bez něj build-app.sh podepisuje ad hoc jako dosud.
  - Mimo rozsah i na Windows: import historie z existujících `_REPORT` na cílech, token ntfy, Telegram, ověření během proxy na
    síťových cílech, hashování na GPU.

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
