# Offload pro Mac — vývoj

Historie Mac verze (složka [`mac/`](../mac/)). Nejnovější nahoře. Každá změna Mac verze sem přidá záznam
ve stejném commitu; když se tím Mac a Windows v něčem rozejdou, upraví se i [ROZDILY.md](ROZDILY.md).

Formát záznamu: `datum — co se změnilo (commit / verze)`.

## Naplánováno

- Složka pro proxy navíc (`proxy.extraFolder`, `--proxy-folder`) — jako ve Windows verzi, doplnit do plánu parity.

- Mac `braw-decode`: (1) `--info` má vydat `gamma`, `gamut` a `colorScienceGen` (atributy `gama`, `gamt`, `csgn` přes
  `CloneClipProcessingAttributes`) — viz [FORMATY.md](FORMATY.md); pipeline nastavit **před** `OpenClip` (SDK čte konfiguraci
  při prvním otevření klipu); pravidlo LUTu podle gamy jako `LutResolver.Braw` na Windows (Film Gen 5 → BMD LUT, ostatní
  gamy bez LUTu, gama neuvedena → předpoklad Film Gen 5). (2) Náhledy BRAW: počáteční snímek `--start` zaokrouhlit
  (`BrawBackend.StartFrame`), ne useknout — přesný zlomek 24000/1001 jinak vyjde o snímek dřív než SDK float 23.976.
  Windows to má od plánu 3.

- Vestavěné LUTy (sdílené `shared/luts/`) a automatický výběr podle kamery — stejná pravidla jako
  `windows/src/Offload.Core/Luts.cs` (`LutResolver`); přibalit `shared/luts` do `Offload.app/Contents/Resources/luts`;
  klíče `proxy.lutMode`, `proxy.lutBuiltin`, `proxy.lutFallback` a u klipu `lut` ([FORMATY.md](FORMATY.md));
  pro zdroje v plném rozsahu přidat před vestavěný LUT předpočet `lutrgb` (jako Windows). Je to navazující práce **po**
  plánu parity [`mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md`](../mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md);
  návrh [`2026-10-05-offload-luts-design.md`](../windows/docs/superpowers/specs/2026-10-05-offload-luts-design.md).

- **Podklady pro sestavení na Macu jsou připravené (2026-10-05):** návrh
  [`mac/docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md`](../mac/docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md),
  plán po úkolech s testy [`mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md`](../mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md)
  a návod jak začít [`mac/docs/superpowers/plans/README-START.md`](../mac/docs/superpowers/plans/README-START.md).
  Pokrývá 3 snímky na klip, pravidla B1–B4, opravy z vývoje Windows verze, české texty, vysunutí jen kopírované karty,
  USB diagnostiku (IOKit), `offload ports`, okno Hardware a řetěz připojení. LUTy přijdou v plánu 5 (společný návrh
  [`2026-10-05-offload-luts-design.md`](../windows/docs/superpowers/specs/2026-10-05-offload-luts-design.md)).

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
