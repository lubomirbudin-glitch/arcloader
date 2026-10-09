# Offload pro Mac — vývoj

Historie Mac verze (složka [`mac/`](../mac/)). Nejnovější nahoře. Každá změna Mac verze sem přidá záznam
ve stejném commitu; když se tím Mac a Windows v něčem rozejdou, upraví se i [ROZDILY.md](ROZDILY.md).

Formát záznamu: `datum — co se změnilo (commit / verze)`.

## Naplánováno

- **Zrychlit analýzu klipů ARRIRAW (zadání uživatele 2026-10-09, priorita):** u karty A_0009 (855 GB, 7 klipů
  ARRIRAW 4.6K OG) trvala analýza přes 15 minut ještě před prvním proxy. Podezření: `art-cmd export` (probe v
  `ARRIRAWBackend`, volaný i z `reclassifyUndecodableMXF`) vypisuje metadata **každého snímku**, takže čte celý ~120GB
  klip, a to jen kvůli metadatům klipu a TC prvního snímku. Postup: (1) změřit `art-cmd export` s `--duration 1` proti
  bez něj na velkém klipu; (2) když platí, exportovat jen 1 snímek (případně `--start 0 --duration 1`) a ověřit, že
  rozlišení, fps, počet snímků (`clipDuration`), TC, kamera, log a squeeze zůstanou stejné; (3) zkontrolovat, že se
  klip neprobuje dvakrát (ffprobe → reclassify → probe); (4) ffprobe na ARRIRAW MXF má mít časový limit. Cíl: analýza
  karty v řádu sekund na klip. Totéž ověřit na Windows (`RawBackends.cs`).

- **Kopie reportu i na počítači (zadání uživatele 2026-10-09):** každý report karty (`_REPORT/` — HTML, PDF, JSON,
  TXT, `.xxh64`, případně `.md5`, CSV/ALE — a `ascmhl/`) se po dokončení úlohy zkopíruje i lokálně, např.
  `~/Documents/Offload reporty/<datum>/<karta>/`, a při každém přepisu reportu (analýza, proxy) se kopie obnoví.
  Důvod: disky s materiálem se odevzdávají dál a report pak na place chybí (stalo se u disků 1 a 2). Selhání lokální
  kopie nesmí shodit úlohu (jen poznámka). Platí pro obě verze (Windows: `%USERPROFILE%\Documents\Offload reporty\`).

- **Automatické načtení disků (zadání uživatele 2026-10-08):** externí disky se po připojení samy přidají do **cílů**
  a čtečky karet (resp. karty v nich) do **zdrojů** — dnes se cíle vybírají ručně a zdroje se označují jen podle
  struktury karty. Rozlišit čtečku od disku (USB třída / IOKit, typ svazku, struktura kamerové karty), nikdy nepřidat
  jako cíl svazek, který vypadá jako karta, ani interní disk; přidaný cíl jde odebrat a zapamatovat si odebrání.

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
  „Rozhodnutí (2026-10-07, závazné)“ v hlavičce plánu 9). **Uživatel 2026-10-09 rozhodl: Mac dostane nové UI podle Windows verze** (včetně klikacích voleb kroku 3 z Windows 0.5.2;
  podklady `docs/ui-navrh/`, Windows plán 9 a `windows/src/Offload.App` jako vzor) — převzít: rozvržení (horní lišta, kroky
  1–3, karty úloh po stavech, stránka Nastavení, dnes a historie karet), přepínač **projektů** místo presetů, tmavé
  i světlé téma podle systému; **Pozastavit** (brána mezi 16MB bloky kopie a zpětného čtení a mezi klipy 2. fáze, žádný
  proces se neuspává, zámek dne se během pauzy nedrží, report `copyPausedSeconds` / `verifyPausedSeconds`);
  **Zkopírovat znovu** (oprava jako generace ASC MHL N: jen selhané soubory, jen na cíle, kde selhaly, dobrá kopie
  `verified`, nesouhlasná do `_REPORT/neshoda_NNNN/`, `kind: "repair"`, `rewritten`, `setAside`, historie
  `generations[].kind = repair` — FORMATY.md „Oprava“); štítek kamery z prvního klipu a typ připojení (Mac pozná
  i Thunderbolt); odhad času jen z rychlostí naměřených v relaci.

- **Mimo plán — zbývá (stav po verzi 0.2.0):**
  - Blackmagic RAW Film Gen 4: Windows od 0.4.0 umí převod na Gen 5 (`--gen5`, výchozí vypnuto, neověřeno — čeká vzorek
    Gen 4); Mac zatím bez LUTu (provizorně) — rozhodne uživatel (spec D.15). Na Macu je vzorek Gen 4 (`profile.braw`
    z Blackmagic RAW Speed Test).
  - Nastavení nástrojů: řádek „braw-decode“ ukazuje „nalezeno“ i bez knihovny Blackmagic RAW (stav BRAW je vidět z poznámky
    u klipu) — otevřené i na Windows.
  - Náhledy R3D/ARRI bez proxy, zrušení `ffprobe`, selhání kopie proxy v souhrnu CLI — otevřené i na Windows (návrh, oddíl 4).
  - Windows: ARRIRAW bez LUTu má stále chybu opravenou na Macu v `3b43352` — `windows/src/Offload.Core/RawBackends.cs`
    (ř. 381) předává art-cmd `--target-colorspace` spolu s `--embedded-look`, art-cmd to odmítne, skončí kódem 0 a nic
    nezapíše (klip bez proxy a náhledů). Na Windows straně neopraveno.
  - **Chybějící vzorky:** BRAW Film Gen 5 (automatický LUT), BRAW klip s ≥ 10 snímky (`testFramesAcrossBatchesInOrder`,
    dávky po 8), BRAW s gamou Video; vizuální srovnání BRAW proxy s Blackmagic RAW Playerem; R3D se skutečným REDline;
    skutečné Sony `…M01.XML` z kamery.
  - **Ruční test se skutečným hardwarem** (README-START, oddíl 7): skutečná karta ve čtečce (FAT32 i exFAT, vysunutí, jiná
    karta v téže čtečce) a USB disky — HDD, aby „brzdí“ ukázalo pomalejší disk ze správného důvodu, SSD 10 Gb/s (linka
    „USB 3 (10 Gb/s a víc)“, `testUsbVolumesResolveToASocket` s USB diskem), týž disk přes USB 2 (varování), čtečka za USB 3
    hubem / dokem, UAS, Thunderbolt disk (`volumeIsInternal`), SMB svazek, Apple Silicon (`usb-drd*`); BRAW bez knihovny
    na Macu bez Playeru / Resolve; křížové ověření s kopií z Windows (`offload verify` oběma směry) a `ascmhl verify`
    (`ascmhl` tu není nainstalovaný).
  - **GUI nikdy nespuštěné** (během dorovnání se aplikace nespouštěla — na Macu se pracovalo); zkontrolovat ručně:
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
