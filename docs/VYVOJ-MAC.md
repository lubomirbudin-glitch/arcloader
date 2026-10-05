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

- Úkol 23 — CLI: --lut auto|none|<id>|<cesta> (bez volby automaticky podle kamery), --lut-fallback, offload luts, --proxy-folder (neexistující složka s --proxy = chyba před startem, bez --proxy jen poznámka)
- Oprava (mimo plán) — ARRIRAW bez LUTu: art-cmd odmítne `--target-colorspace` spolu s `--embedded-look` („only valid for embedded looks with DRT LUTs“), skončí kódem 0 a nic nezapíše → klip neměl proxy ani náhledy („no output“). Bez LUTu teď jen `--embedded-look` (vzhled z kamery = Rec.709; klip bez vzhledu dá LogC); prázdná cesta k LUTu = bez LUTu (i `--lut ""`); když art-cmd nevytvoří výstup, chyba cituje jeho řádky [error] (art-cmd píše log na stdout). Ověřeno se skutečným art-cmd na ALEXA 35 i ALEXA Mini. Windows `ArriBackend.Args` má stejnou chybu
- Úkol 22 — LUT pro každý klip v proxy i náhledech; BRAW a plnorozsahové video přepočtené na konvenci vestavěných LUTů (lutrgb), RED a ARRIRAW vydají log jen s LUTem; v reportu u klipu „LUT: … (důvod)“ a klíč lut, v řádku Proxy způsob výběru LUTu
- Úkol 21 — vestavěné LUTy: katalog (Offload.app/Contents/Resources/luts, při vývoji shared/luts), výběr LUTu podle klipu (Sony XML, ARRI výrobce + model, ARRIRAW, BRAW, RED, záložní LUT), nastavení lutMode/lutBuiltin/lutFallback (starý lutPath = vlastní LUT), ffprobe čte color_range a výrobce + model; chybějící LUT blokuje start jen u vlastního
- Úkol 20 — složka pro proxy navíc (jádro): každá hotová proxy i do <složka>/<datum>/<karta>/ (při shodě jména _2, _3…, nikdy nepřepíše, nejvýš 1000 jmen), nedostupná složka nebo chyba kopie = jen poznámka u klipu; v reportu „· také ve složce …“
- Úkol 19 — ffmpeg se nezasekne navždy (jako Windows): VideoToolbox dekódování jen pro H.264/HEVC; render proxy bez pokroku 60 s (nový snímek nebo data z dekodéru RAW; do prvního pokroku 180 s) se ukončí (SIGTERM, nejvýš 10 s, pak SIGKILL); po hardwarovém dekódování jedno opakování softwarově, jinak poznámka „ffmpeg se zasekl (60 s bez nového snímku); proxy nevznikla“; zrušení se neopakuje; opakování a jeho výsledek v logu
- Úkol 18 — barvy proxy výslovně BT.709 (matice, legální rozsah, značky ve VideoToolbox/ProRes i v MOV), náhledy JPEG výslovně BT.601 v plném rozsahu (JFIF); filter graph jako samostatný testovaný modul
- Úkol 17 — GUI Hardware: menu Nástroje › Prověřit USB porty… (⌘U) jen na vyžádání, okno Hardware (připojení, doporučení, měření s dotazem a okamžitým zastavením, USB porty s vlastními názvy), sekce Hardware s posledním testem, řetěz připojení a úzké hrdlo na kartě úlohy
- Úkol 16 — CLI: offload ports [--measure] (připojení, USB porty, měření, doporučení; Ctrl+C smaže dočasný soubor), řetěz připojení před kopií a „brzdí: Dn“ ve stavovém řádku; cíl, který ještě neexistuje, se posuzuje podle svazku nejbližší existující složky (nová složka na kartě = karta)
- Úkol 15 — test HW: role, rychlý test, měření (karta jen čtení, cíl zápis + čtení 1 GB), cíl na svazku karty se neměří, zrušení uprostřed smaže dočasný soubor, výsledek atomicky v hwtest.json
- Úkol 14 — USB topologie z IORegistry (řadiče, porty, USB 2/3 páry, huby, rychlost linky) a kde sedí svazek (DiskArbitration: sběrnice, produkt, port, UAS, Thunderbolt, síť); ověřeno na MacBook Pro 16,1 (2019, Intel), macOS 26.7.1 — USB 2 a USB 3 polovina USB-C zásuvky leží na různých řadičích (XHC1 ↔ XHC2/XHC3), skener je páruje podle `UsbProtocolCompanion`
- Úkol 13 — HW model: kde sedí disk (sběrnice, port, linka, UAS), doporučení zapojení (texty jako Windows), řetěz připojení s % linky, součtem na řadiči a úzkým hrdlem
- Úkol 12 — HW model: rychlosti linek a popisky, USB zásuvky (dvojčata USB 2/3, poloviny USB 3 hubu, neznámý typ ≠ „jen USB 2.0“), jeden popisek portu všude
- Úkol 11 — GUI: karta úlohy s českým množným číslem a oranžovým „Zrušeno — …“, zrušená úloha není chyba, vysunutí jen kopírované karty mimo hlavní vlákno, cíle a LUT se zjišťují na pozadí, ukončení během kopie se ptá a počká na úklid
- Úkol 10 — nastavení se čte po sekcích (vadná sekce ani nový klíč nesmažou cíle), kontrola LUT před startem, sken svazků mimo hlavní vlákno a výběr nové karty podle cesty
- Úkol 9 — pravidla vysunutí: jen karta, která se kopírovala (UUID svazku), nikdy složka, síťový ani interní disk, ne dokud z karty kopíruje jiná úloha; report z prvního cíle, který ho má
- Úkol 8 — české texty s množným číslem, průběh karty úlohy (rychlosti, odhad, fáze, tón; zrušená úloha není chyba) a brzda podle zásoby bloků — v OffloadKit s testy
- Úkol 7 — report: tři snímky na klip s popiskem TC (HTML i PDF), R3D/ARRI z hotové proxy; proxy zůstane hotová i při selhání kopie na další cíl
- Úkol 6 — tři snímky z ffmpeg (přesný prostřední, poslední i u delšího zvuku, zrušitelné) a z braw-decode (zaokrouhlený start)
- Úkol 5 — timecode a pozice tří snímků (první, prostřední o půl framu dřív, poslední od konce video stopy); burn-in názvu s „%“ už nespadne
- Úkol 4 — úloha zrušená ve frontě hlásí „Zrušeno“ a nic nekopíruje; plán a report úlohy za zámkem (čtení z hlavního vlákna); testovací háček pro deterministické zrušení
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
