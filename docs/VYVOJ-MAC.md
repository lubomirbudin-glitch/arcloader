# Offload pro Mac — vývoj

Historie Mac verze (složka [`mac/`](../mac/)). Nejnovější nahoře. Každá změna Mac verze sem přidá záznam
ve stejném commitu; když se tím Mac a Windows v něčem rozejdou, upraví se i [ROZDILY.md](ROZDILY.md).

Formát záznamu: `datum — co se změnilo (commit / verze)`.

## Naplánováno

- **Souhrnný report z vybraných záloh (zadání uživatele 2026-10-09, obě verze):** v historii karet označit libovolný
  počet záloh (karet, i z různých dnů a disků) a vytvořit z nich **jeden souhrnný report** (HTML + PDF, bez náhledů):
  natáčecí den (dny), za každou kartu i celkem: počet klipů a souborů, **velikost**, **stopáž** (součet délek klipů),
  **čas první a poslední klapky** = začátek **prvního a posledního nahraného klipu** (rozhodnutí uživatele
  2026-10-09; pořadí podle času záznamu z metadat kamery, jinak podle čísla klipu `A001C001…`; čas = startovní TC
  klipu, u ALEXY TC denní doby), kamery, stav ověření a **na kterých discích je co zálohované** (disk, cesta,
  ověřeno ano/ne, kopie reportu na počítači). Zdroj dat: `history.json` + reporty `offload_report*.json` (z disků, nebo
  z lokální kopie `Offload reporty`, když disk už odešel). Uložit lokálně a volitelně na vybraný disk.

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

- **Nový vzhled — rozhodnuto (uživatel 2026-10-09):** Mac dostane nové UI podle Windows verze (včetně klikacích voleb
  kroku 3 z Windows 0.5.2) — podmínka „až po schválení Windows verze“ (D.25) je tím splněná; úkoly nového vzhledu
  v plánu parity 3 nemusí čekat. Vzor: [`docs/ui-navrh/`](ui-navrh/), Windows plán 9 a `windows/src/Offload.App`.

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
  - **Ruční kontroly GUI z dorovnání 0.2.0** (aplikace se během vývoje nespouštěla; od 2026-10-08 běží ostře, ale tyhle
    případy se cíleně nezkoušely):
    - úkol 11: zdroj-složka bez „Vysunout kartu“; zrušení úlohy ve frontě → oranžové „Zrušeno“, bez zvuku Basso,
      „Vyčistit hotové“; ⌘Q během kopie → Pokračovat / Ukončit, žádný `.offload-part`; odpojený cíl → „Disk není připojen“
      do 5 s; chybějící LUT → červeně, start neaktivní; jiná karta v téže čtečce → bez tlačítka vysunutí;
    - úkol 17: ⌘U otevře okno Hardware, druhé ⌘U ho jen přenese dopředu; měření a Zastavit; název portu zůstane po
      restartu; měření neaktivní během kopie; ⌘Q během měření; „Čekám, až doběhne předchozí test…“; řetěz a oranžové
      „Úzké hrdlo“ na kartě úlohy; řádek „Poslední test“;
    - úkoly 19/20: zrušení uprostřed renderu BRAW proxy — bez pádu, bez `.part`; barvy proxy vizuálně v QuickTime Playeru
      a Resolve;
    - úkoly 21/24: výběr LUTu — `.cube` z 0.1.0 → „Vlastní…“; nabídka auto / 11 vestavěných / Bez LUT / Vlastní…; zrušený
      „Vlastní…“ ponechá volbu; vestavěný LUT přežije restart; výběr složky pro proxy, červená hláška po odpojení, křížek
      ji smaže; kontrola odpojení těsně před startem; ostrý offload se složkou → řádky v reportu;
    - úkol 25: ikona v Docku, ⌘⇥ a Finderu; 11 LUTů v *Proxy › LUT*.

## 2026-10-09 — dorovnání 2 (plány 6–10), průběh

Plán [`mac/docs/superpowers/plans/2026-10-08-offload-mac-parity-2.md`](../mac/docs/superpowers/plans/2026-10-08-offload-mac-parity-2.md)
(úkoly 32–55) a `-3` (56–70); nejnovější úkol nahoře. Rozpracovaný kód paralelních proudů (`mac/docs/parita-0.3/`) se
slučuje s plánem úkol po úkolu — co plánu odpovídá, zůstává; co se liší, nahrazuje verze plánu (zprávy commitů).

- Úkol 32 — hash jádro: XXH3-128 (kanonický hex), XXH3-64 bloků, MD5 přes CryptoKit, registr hashů ASC MHL (xxh64, md5,
  sha1, xxh3, xxh128, c4); nahrazuje registr proudu A (`HashKind`, `HashAlgo.create`, MD5/SHA-1 přes zastaralé CommonCrypto),
  jeho testovací vektory zůstaly; `hashFile` počítá vždy xxh64 i xxh128 (`MultiHasher`)

## 2026-10-09 — build „Offload C“ (0.2.0C): rychlá analýza ARRIRAW, report i na Macu, disky do cílů samy

- **Analýza ARRIRAW za sekundy:** `art-cmd export` jen z prvního snímku (`--start 0 --duration 1`, limit 180 s) — celý
  export četl metadata každého snímku, tedy celý ~120GB klip (karta A_0009: přes 15 min). Počet snímků z ffprobe
  (`duration_ts` z indexu MXF) nebo z počtu souborů u sekvence `.ari`; ověřeno na vzorcích ALEXA 35 a Mini (165 / 240
  snímků, TC, kamera, LogC4/LogC3 stejné jako z celého exportu). ffprobe má limit 120 s; když u MXF selže, zkusí se art-cmd.
- **Kopie reportu na Macu:** `~/Documents/Offload reporty/<datum>/<karta>/` (`_REPORT` + `ascmhl`) hned po ověření
  a po každém přepisu reportu (i PDF); disky se jen čtou, selhání jen v logu (`LocalReports.swift`; v XCTestu vypnuté).
- **Připojené disky samy do cílů** (přepínač „Připojené disky automaticky do cílů“, výchozí zapnuto): zapisovatelný
  externí disk, který nevypadá jako karta a není zdrojem, se přidá se stejnou podsložkou jako ostatní cíle (např.
  `/Volumes/TSOTB_5/3ND`); odpojený disk z cílů zmizí; ručně odebraný disk se znovu nepřidá (`ignoredVolumes`).
  Cíle mimo /Volumes (složka na Macu) se nemění. Karty ve čtečkách se do zdrojů označují jako dosud.

## 2026-10-08/09 — první ostré nasazení, opravy (build „Offload b“, 0.2.0b)

Ostrý offload karet ALEXA 35 (ARRIRAW 4.6K Open Gate, karty až 855 GB) na Samsung T7. Opravy podle toho, co se ukázalo:

- **PDF report** se vejde na A4 na šířku — tiskové styly za základními, buňky se zalamují, menší náhledy; když je obsah
  i tak širší, stránka se před tiskem zmenší (`138cdba`).
- **Zelené snímky v proxy:** hardwarové dekódování H.264/HEVC (VideoToolbox) vypnuto — vracelo zelené snímky bez chyby;
  kódování zůstává hardwarové; softwarové dekódování bylo na i9 rychlejší (4K H.264 3,2 s proti 7,4 s) (`138cdba`).
- **ARRIRAW proxy:** procesy s prioritou user-initiated místo utility (v aplikaci 1,4 snímku/s, z terminálu 11,6);
  `--desqueeze` jen u anamorfních klipů (bez něj 25,7 snímku/s); průběh během art-cmd odhadem z velikosti mezisouboru
  (0–80 %), dřív stálo celé dekódování na 0 % (`3a970b5`).
- **HTML report** do šířky 1600 px, menší náhledy, dlouhé názvy a cesty se zalamují — vidět všechny sloupce (`572f059`).
- **Vlastní LUT místo vestavěného:** `~/Library/Application Support/Offload/luts/<id>.cube` nahradí vestavěný LUT se
  stejným id (v nabídce „(vlastní soubor)“) — nainstalované oficiální LUTy ARRI LogC4 a LogC3; vestavěný LogC4 zvedal
  černou (62 proti 23) (`572f059`).
- `scripts/build-app.sh <štítek>` postaví `build/Offload-<štítek>.app` vedle běžného buildu (`572f059`).
- Sloučeno z GitHubu (Windows do 0.5.2) a rozpracované dorovnání s Windows plány 6–8 (paralelní proudy, jen základy:
  hashe MD5/SHA-1/xxh3/xxh128/C4, čtení a zápis generací ASC MHL, české hlášky `Msg`, odhad místa, drop-frame
  timecode) — poznámky a návrh dalšího postupu v [`mac/docs/parita-0.3/`](../mac/docs/parita-0.3/). Proud P (cívka v proxy,
  všechny zvukové stopy, `7ce73ca`) je ve větvi `worktree-agent-acbfd248aeb877f96`, do main zatím nesloučen.
- Zjištění: zpomalení kopie na ~400 MB/s způsobil jeden z T7 (stejný model, vlastní řadič 10 Gb/s — nejspíš zahřátí);
  analýza ARRIRAW trvá minuty na klip (úkol výše); PDF vzniká až na konci úlohy (ponecháno).

## 2026-10-06 — verze 0.2.0: dorovnání s Windows (plán mac-parity)

Plán: [`mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md`](../mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md)
(návrh [`mac/docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md`](../mac/docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md)).
Všech 31 úkolů hotovo; co zbývá, je v oddílu „Naplánováno“ nahoře.

Odchylky od plánu a zjištění (podrobně ve zprávách commitů):

- MacBook Pro 16,1 (Intel, macOS 26): USB 2 a USB 3 polovina USB-C zásuvky leží na různých řadičích (XHC1 ↔ XHC2/XHC3) —
  skener je páruje podle `UsbProtocolCompanion`, jinak by hlásil falešné „jen USB 2.0“ (úkol 14).
- `HwTest.sameVolume`: cíl, který ještě neexistuje, se posuzuje podle nejbližší existující složky — nová složka na kartě
  dřív prošla jako jiný svazek (úkol 16).
- ffprobe 9 vypisuje video stopu dvakrát, když má MOV stopu timecode — test HEVC proxy chce `hvc1` u každého výpisu (úkol 7);
  `readabilityHandler` se na konci výstupu nuluje (jinak nástroj po skončení točil CPU).
- `renamex_np(RENAME_EXCL)` vrací na exFAT (fskit, macOS 26) `ENOTSUP` → záložní kontrola existence těsně před `rename`
  (úkol 20).
- ARRIRAW bez LUTu (oprava `3b43352`): art-cmd odmítne `--target-colorspace` spolu s `--embedded-look`, skončí kódem 0
  a nic nezapíše → bez LUTu jen `--embedded-look`; Windows má stejnou chybu (viz „Naplánováno“).
- ALEXA 35 neumí výstup LogC3: LogC3 LUT (vlastní nebo vestavěný) na klipu z ALEXA 35 → klip bez proxy, v poznámce důvod
  od art-cmd. art-cmd chce výstup široký aspoň 360 px.
- DaVinci Resolve má `BlackmagicRawAPI.framework` v `Contents/Frameworks`, ne `Contents/Libraries` (úkol 27).


- Úkol 26 — verze 0.2.0, README (testy, Hardware, LUTy a složka pro proxy), ROZDILY a FORMATY podle stavu po dorovnání. Závěrečné ověření (MacBook Pro 16,1, macOS 26, se vzorkem `profile.braw`): `swift build -c release && swift test && scripts/build-app.sh` zelené — 272 testů, 0 selhání, 3 přeskočené (`testFramesAcrossBatchesInOrder` chce vzorek ≥ 10 snímků; dva testy USB bez připojeného USB svazku); Info.plist `CFBundleShortVersionString` 0.2.0 a `CFBundleIconFile` Offload; `offload --version` → 0.2.0, `offload luts` → 11 řádků. Křížové ověření s kopií z Windows a `ascmhl verify` neproběhlo (viz „Naplánováno“)
- Úkol 31 — BRAW: test celé cesty se vzorkem (OFFLOAD_BRAW_SAMPLE: analýza s gamou, LUT podle gamy, tři náhledy, proxy 540p), README „Vzorek BRAW“, ROZDILY (BRAW na obou stejně). Ověřeno se vzorkem `profile.braw` z Blackmagic RAW Speed Test (URSA Mini Pro 4.6K, 4608×2592, 60 fps, 4 snímky, gama Blackmagic Design Film, gamut Blackmagic Design, generace 4): `swift test --filter Braw` 34 testů, přeskočen jen `testFramesAcrossBatchesInOrder` (chce ≥ 10 snímků); `offload … --proxy` → tři náhledy (00:00:00:00 / :02 / :03), proxy 1920×1080 H.264 BT.709 se 4 snímky, „LUT: bez LUT (Blackmagic RAW: Blackmagic Design Film Gen 4 (bez vestavěného LUTu))“, v JSON info.gamma/gamut/colorScienceGen; s `--lut bmd-film-gen5` proxy s LUTem bez chyby. Zbývá: vzorek Film Gen 5 (automatický LUT) a delší klip (dávky po 8 snímcích), vizuální srovnání s Blackmagic RAW Playerem, BRAW bez knihovny na skutečném Macu
- Úkol 30 — náhledy BRAW pod hlídačem jako proxy (zaseknutý dekodér se ukončí, klip náhled nedostane, úloha jede dál); po prvním zaseknutí nebo limitu --info se další snímky klipu nezkoušejí a log to řekne; když ffmpeg skončí dřív, pumpa zavře i výstup dekodéru (braw-decode skončí sám s kódem 0, odříznutý dekodér se neposuzuje); dekodér, který selže, zatímco ffmpeg čte, je chyba klipu s českou poznámkou
- Úkol 29 — BRAW jen s knihovnou Blackmagic RAW (bez ní se klip jen zkopíruje a ověří, nápověda jako Windows); braw-decode --info s limitem 60 s (zaseknutá knihovna úlohu nezasekne, poznámka „neodpověděl do 60 s — ukončen“); kódy 3/4/5 → česká poznámka u klipu, původní hláška braw-decode do logu; ToolProcess čte stderr celé i u nástroje, který hned skončí
- Úkol 28 — BRAW: gama, gamut a generace barev z braw-decode v metadatech klipu (JSON info.gamma/gamut/colorScienceGen jako Windows); LUT jen pro Blackmagic Design Film Gen 5, Video / Extended Video a Film Gen 4 bez LUTu (ani záložního), bez údaje o gamě předpoklad Film Gen 5
- Úkol 27 — braw-decode jako Windows: --info vydává gamma, gamut a colorScienceGen (atributy klipu, nečitelný atribut = varování na stderr), pipeline před OpenClip, hlášky kódů 3/4 jmenují načtenou knihovnu, továrna přes QueryInterface, zavřená roura = kód 0 s hláškou (SIGPIPE ignorován — dřív proces zabil signál), přísné argumenty (kód 2); BrawLibrary se stejnými složkami jako dekodér. Ověřeno s knihovnou 4.3 (Player i Resolve): QueryInterface továrny funguje (kód 4, ne 3); DaVinci Resolve má framework v `Contents/Frameworks`, ne `Contents/Libraries` — opraveno v dekodéru i BrawLibrary; vzorek (URSA Mini Pro 4.6K): gama „Blackmagic Design Film“, gamut „Blackmagic Design“, colorScienceGen 4, typy podle hlavičky (bez varování); CPU pipeline před OpenClip beze změny rychlosti (4 snímky 4,6K: 0,40 s před i po)
- Úkol 25 — balíček: vestavěné LUTy v Offload.app/Contents/Resources/luts (CLI je najde z Resources/bin), ikona aplikace Offload.icns z shared/icon/Offload.iconset (iconutil), CFBundleIconFile
- Úkol 24 — GUI: výběr LUTu (automaticky / vestavěný / bez / vlastní) se záložním LUTem „Když kameru nepoznám:“, „Kopírovat proxy i do složky“; nedostupná složka nebo chybějící vlastní LUT blokuje start (ověřuje se na pozadí každých 5 s i těsně před startem, nejvýš 5 s)
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

- **Převzít z Windows verze — HTML reporty (2026-10-08):** (1) lišta jen pro obrazovku nahoře v HTML reportu karty, denním
  i ověření: tlačítko „Uložit jako PDF“ (`window.print()`), nápověda „v tisku zvolte „Uložit jako PDF““ a odkaz „Otevřít PDF
  report“ na PDF vedle HTML; v `@media print` skrytá; (2) tisk z prohlížeče: `@page{size:A4 landscape;margin:10mm}`,
  světlé barvy i z tmavého režimu, `tr{break-inside:avoid}`; (3) tabulka klipů se vejde do okna: `table-layout:fixed` +
  `<colgroup>` v %, `overflow-wrap:anywhere`, náhledy `flex:1 1 0;max-width:128px`, v úzkém okně skrýt Formát/TC/Audio.
  Podrobnosti ve [VYVOJ-WINDOWS.md](VYVOJ-WINDOWS.md), sekce „2026-10-08 — report: uložení do PDF“.
- **Převzít z Windows 0.5.1 — proxy (2026-10-08):** (1) `_PROXY/<složka klipu>/<klip>.mov` (složky jako na kartě, i ve
  složce pro proxy navíc), `_2` jen ve stejné složce, doplnění do ploché zálohy zůstane ploché (detekce z `proxyPath`
  v reportech); (2) `proxy.skipLighterSources` (výchozí `true`, jen u proxy H.264/HEVC — s proxy ProRes vždy proxy — a jen když se nepoužije LUT ani ořez): H.264/HEVC originál ne větší než proxy s tokem ≤ tok proxy
  bez proxy, `skipped` + poznámka „bez proxy — originál je lehčí (X Mb/s ≤ Y Mb/s)“, náhledy ano, místo bez proxy;
  originál větší než proxy ji dostane vždy; (3) HTML: odkaz „hotovo“ na proxy, sloupec Proxy bez dělení slov.
  Podrobnosti a pravidla ve [FORMATY.md](FORMATY.md) a [VYVOJ-WINDOWS.md](VYVOJ-WINDOWS.md), sekce „2026-10-08 — proxy“.

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
