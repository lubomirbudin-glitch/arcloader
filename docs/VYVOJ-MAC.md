# Offload pro Mac — vývoj

Historie Mac verze (složka [`mac/`](../mac/)). Nejnovější nahoře. Každá změna Mac verze sem přidá záznam
ve stejném commitu; když se tím Mac a Windows v něčem rozejdou, upraví se i [ROZDILY.md](ROZDILY.md).

Formát záznamu: `datum — co se změnilo (commit / verze)`.

## Naplánováno

- **Ověřování během proxy jako volba u každé kopie (zadání uživatele 2026-10-11, obě verze):** přepínač
  „Verify during proxy render“ v kroku 3 „After copy“ levého sloupce (klikací řádek jako ostatní volby po kopii), ne jen
  v Nastavení → Verification; výchozí hodnota z nastavení/projektu (`verifyInProxy`), změna platí pro další přidanou kartu.
  Vypnuto nebo bez proxy → zpětné čtení po kopii jako dosud. CLI `--classic-verify` (vypne). Smysl: kopie naplno, po ní
  „Safe to eject“, ověření všech disků celé během renderu proxy (disky jinak stojí), „Safe to format“ až po něm — u karty 9
  (855 GB) odhad 68 min místo 91. Ověření se nikdy nedělí mezi disky (každý disk se čte celý). Mac: pořadí 42–50 → 51 →
  61 → 65–70 (build D5).

- **Aplikace jen anglicky (zadání uživatele 2026-10-10, obě verze; Windows 0.9.0):** celá aplikace bude **natrvalo jen
  anglicky** — čeština zmizí ze všeho, co uživatel vidí: GUI, hlášky, poznámky u klipů, texty kontroly před startem,
  oznámení (systémová i ntfy), reporty (HTML/PDF/TXT karty, denní report, report ověření, souhrnný report, lidsky čitelné
  sloupce seznamů klipů), nápověda, výstup a chyby CLI, hlášky `braw-decode`, návod a readme v balíčku. Vývojová
  dokumentace v `docs/` a komentáře v kódu zůstávají česky (nejsou součástí aplikace).
  - **Nesmí se změnit** (formát na disku sdílený s Windows a se staršími zálohami, `docs/FORMATY.md`): klíče a hodnoty
    enumů v JSON, ASC MHL, `.xxh64`/`.md5`, manifest, klíče `history.json`, klíče a hodnoty `settings.json`; jména
    uvnitř zálohy (`_REPORT`, `ascmhl`, `_PROXY`, `neshoda_NNNN`, jména souborů reportů, `<karta>_2`); názvy sloupců
    CSV/ALE, které čtou nástroje (lidsky čitelné hodnoty už anglicky). Čtení musí dál přijmout všechno, co zapsaly
    starší verze, včetně českého volného textu ve starých reportech (např. poznámky) — ten se ukáže tak, jak je.
  - **Formátování nezávislé na jazyku systému** (obdoba `CultureInfo.InvariantCulture` — na Macu `Locale(identifier:
    "en_US_POSIX")` / pevný formát, nikdy `Locale.current`): datum `2026-10-10`, čas `14:14:28`, datum a čas
    `2026-10-10 14:14:28`, desetinná tečka (`1.70 GB`, `23.976`), velikosti `GB`/`MB` jako dosud, délky `0:01:32`,
    rychlosti `602 MB/s`; v HTML `<html lang="en">`.
  - **Místní složky:** kopie reportů do `~/Documents/Offload reports/<datum>/<karta>/` (dosud `Offload reporty`),
    souhrnný report do `~/Documents/Offload reports/summary/<datum>_<čas>_summary.html/.pdf`. Čtení (záložní zdroj
    souhrnného reportu, `.offload-origin`) se musí dívat **i do staré složky `Offload reporty`**. Klíč nastavení pro
    složku zůstává.
  - **Slovník pojmů** (stejné slovo pro stejnou věc v obou verzích, např. Safe to eject / Safe to format, Append,
    Copy again, Verify backup, Day report, Summary report): [ui-text-glossary.md](ui-text-glossary.md).
  - **Přejmenované soubory v balíčku:** `CTI-ME.txt` → `READ-ME.txt`, návod `Offload-navod.pdf` → `Offload-manual.pdf`
    (Windows: zdroj `windows/package/navod/navod.html` → `windows/package/manual/manual.html` + `render.ps1`). Mac build
    má přibalit odpovídající anglický readme a návod se stejnými jmény.

- **Odkazy na stažení dekodérů (zadání uživatele 2026-10-10, ve Windows 0.7.2):** když chybí nástroj
  pro RAW, ukázat odkaz na stránku výrobce — v nastavení nástrojů tlačítko „Stáhnout…“ a v poznámce klipu bez proxy
  „— ke stažení: <adresa>“: Blackmagic RAW https://www.blackmagicdesign.com/products/blackmagicraw, REDCINE-X PRO
  (REDline) https://www.reddigitalcinema.com/download/redcine-x-pro-mac, ARRI Reference Tool (art-cmd)
  https://www.arri.com/en/learn/camera-systems/tools/arri-reference-tool.

- **Snímací fps z metadat prvního snímku u každého klipu (zadání uživatele 2026-10-10, obě verze, chyba):** report
  u karty A_0012_1F75 (ALEXA 35, natočeno 35 fps) uvádí u všech klipů 24 fps — bere se jen projektová / přehrávací
  rychlost souboru (`nativePictureEssenceDescriptor.file.sampleRate` = 24/1), ne snímací (`frameBasedMetadata` →
  `Sensor State.sensorSampleRate` = 35/1 v prvním snímku). U **každého klipu** číst metadata prvního snímku a v reportu
  (HTML/PDF/JSON, seznamy klipů, denní a souhrnný report) uvádět **snímací fps** a, když se liší, i přehrávací
  („35 fps · přehrávání 24“); délku klipu brát z počtu snímků a přehrávací rychlosti (stopáž ve střižně) a označit
  ji tak. Totéž ověřit u BRAW (`sensorRate` / GetSensorRate), RED (record vs. project fps) a Sony/Canon (S&Q,
  `captureFps` v XML/metadatech). Nový klíč JSON doplnit do `docs/FORMATY.md` pro obě verze.

- **Pauza zálohy (zadání uživatele 2026-10-09, obě verze):** tlačítko Pozastavit nechá **doběhnout rozpracovaný dílčí
  krok** — soubor při kopii a ověření, klip při analýze a renderu proxy — a teprve pak úlohu zastaví (žádný napůl
  zapsaný soubor, žádný přerušený render). Pokračovat **plynule naváže** dalším souborem / klipem, bez opakování hotové
  práce a bez nového spuštění úlohy; stav „pozastavuji (dokončuji …)“ → „pozastaveno“ musí být vidět. Windows od 0.5.0
  pozastavuje mezi 16MB bloky a mezi klipy 2. fáze — sladit s tímto zadáním (pozastavení po dokončení souboru/klipu).

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

## 2026-10-10 — English UI and reports (user decision)

Rozhodnutí uživatele 2026-10-10: Mac verze je **jen anglicky**, bez české volby. Anglicky jsou hlášky kontrol
(`Msg`, `PreflightCode` beze změny), texty karet úloh a fronty (`Texts`, `JobProgress`, fáze úlohy, vysunutí karty),
poznámky u klipů (proxy, dekodéry RAW, důvody LUTů), hardware (role „Card“ / „Destination 1“, doporučení, řetěz
připojení, porty), problémy ASC MHL, CLI („Error: …“, `offload ports`, stavový řádek „bottleneck: D1“) a HTML/PDF
report (`lang="en"`, datum „Oct 10, 2026 14:05:12“, 24h). TXT report byl anglicky už dřív. Velikost ve větě:
`formatBytesShort` („3.2 GB“, dřív `formatBytesCs` „3,2 GB“). Klíče JSON, názvy souborů a struktura (FORMATY.md)
se nemění; texty v JSON (`proxyNote`, `probeError`, `lut.reason`, `lut.name` „no LUT“) jsou anglicky. Report s českým
důvodem „vypnuto v nastavení“ (starší Mac / Windows) se v HTML pořád pozná jako vypnutý LUT; uložený test hardwaru
s rolí „Karta“ se pořád pozná jako karta. Slovník pro GUI a další úkoly: [`mac/docs/en-glossary.md`](../mac/docs/en-glossary.md).
GUI (`Sources/OffloadApp`) přechází na angličtinu zvlášť (nový vzhled); pro něj jsou v OffloadKit konstanty
`RoleLink.card`, `RoleLink.destination(n)` a `ChainView.bottleneckPrefix` (dřívější GUI barvilo řádek podle „Úzké hrdlo“). Testy hlídají anglické texty stejně přísně
(přesné řetězce, stejné kódy).

## 2026-10-10 — dorovnání 2 (plány 6–10), průběh

Plán [`mac/docs/superpowers/plans/2026-10-08-offload-mac-parity-2.md`](../mac/docs/superpowers/plans/2026-10-08-offload-mac-parity-2.md)
(úkoly 32–55) a `-3` (56–70); nejnovější úkol nahoře. Rozpracovaný kód paralelních proudů (`mac/docs/parita-0.3/`) se
slučuje s plánem úkol po úkolu — co plánu odpovídá, zůstává; co se liší, nahrazuje verze plánu (zprávy commitů).

- Úkol 51 — **zúžený rozsah (rozhodnutí uživatele 2026-10-11)**: vytažený dopředu kvůli Verify-in-Proxy (úkoly 52–60
  zatím vynechané, verze zůstává 0.3.0) a jen v tom, co úkoly 65–68 potřebují: kopie počítá v jednom čtení karty
  k xxh64 i XXH3-64 každého bloku (`SourceHasher.blocks` přes `MultiHasher` bez XXH3-128, blok = `chunkSize`;
  `OffloadEngine.blocks(i)` / `fileBlocks`; soubor nedočtený do konce bloky nemá). Report, ASC MHL, `.xxh64`, GUI a CLI
  beze změny. Z plánu úkolu 51 se **nedělá**: XXH3-128 u každého souboru (`files[].xxh128`, MHL `<xxh128>`), MD5 /
  `hash: both`, `--hash`, `bench --hash md5`, volba „Checksum“ v GUI; proto zůstává otevřená i poznámka úkolu 47
  (doplnění do zálohy s MD5 z Windows doplní jen xxh64)
- Úkol 50 — verze 0.3.0 (plán 6): FORMATY (verze, `volumeSerial` Macu, místní soubory a zámek historie, report
  ověření a `.xxh64` Mac od 0.3.0), ROZDILY (verze, jazyk, řádky plánu 6), README (historie, Append, Verify backup, místo,
  `offload append`, `verify [--report]`, `--name auto`, `--no-history`, `--dry-run`). Závěrečné ověření:
  `swift build -c release` a `swift test` zelené — 452 testů, 6 přeskočených, 0 selhání; `offload --version` → 0.3.0.
  `scripts/build-app.sh` se nespouštěl (zadání). Křížové ověření s Windows 0.6.0+ (verify/append obousměrně) neproběhlo —
  Windows není po ruce; `ascmhl create` 1.2 na kopii složky doplněné Macem (generace 1 + 2) prošel
- Úkol 49 — GUI plánu 6 v novém vzhledu (jako Windows 0.9): karta v kroku 1 se prohlédne mimo hlavní vlákno
  (`CardInspector`, jedna prohlídka na cestu, žádost během běhu = ještě jednou, výsledek zahozený pro zdroj, který zmizel,
  jiná identita karty ve slotu = znovu); pod kartou řádek historie v barvě tónu („Checking whether the card is already backed
  up…“ dokud historie a prohlídka neskončí), u karty počet klipů a velikost; tlačítko „Append“ (jen svazek s novými
  soubory a dosažitelnou zálohou, nápověda „Append new clips only to the earlier backup (12 files · 3.2 GB)“) — zaškrtnuté
  místo pole názvu ukáže „Folder: 2026-10-06/A001 on 2 destinations“ a úloha jde do složek zálohy (krok 2 smí být prázdný),
  karta úlohy „A001 · append“, „Nothing new“ zeleně jen při Safe to format; pole „Folder name“ má zástupný text z klipů /
  další v řadě (`CardNaming.resolve` — co je vidět, to se zkopíruje) a šedou nápovědu, v Nastavení › Folders „Suggest the card
  name from the clips“; dialog „Card(s) already backed up“ (Copy again in full / Skip backed-up / Cancel); hlášky místa pod
  Startem (červená blokuje, oranžová varuje; vybrané karty z prohlídky, jinak obsazené místo svazku, fronta jako zbytek
  běžících úloh, svazek a jméno cíle z probíhající kontroly cílů); Start čeká na prohlídku vybraných karet nejvýš 10 s;
  „Verify backup“ v horní liště, Tools › Verify Backup… ⇧⌘O a v Nastavení › Verify („Save a verification report“, i v
  dialogu) — každá karta je položka kopírovací fronty (`VerifyJob`, `VerifyJobView` v OffloadKit/Desktop, test), karta ve
  frontě s průběhem, výsledkem, varováními, poznámkou o reportu mimo kartu, „Verification report“, „Show in Finder“,
  „Cancel“; po skončení úlohy se historie načte znovu; před ukončením aplikace se čeká nejvýš 10 s na zápis historie (pak
  log). Pravidla bez UI v OffloadKit (`BackedUpPrompt`, `AppendRules`, `CardNaming`, `SpaceCheck`, testy `CardRulesTests`).
  GUI se nespouštělo (jen build) — ruční kontrola README-START oddíl 7 body 14–18 s kartou čeká
- Úkol 48 — kontrola místa po svazcích (`SpaceEstimator`/`SpacePlanner` z proudu A, teď zapojené): karta + 64 KiB na
  soubor + 64 MiB, proxy z odhadu (délka z velikosti a typického toku, ProRes podle výšky, AAC/PCM), report 32 MiB + 1 MiB
  na klip, složka pro proxy jako vlastní místo; dva cíle na jednom svazku se sečtou; `OffloadEngine.prepare(extra:)`
  a `checkSpace` (i u doplnění pro nové soubory) — blokuje `noSpace` dřív, než se cokoli zapíše, méně než 5 % rezervy =
  `spaceWarnings` (úloha je dá do `JobStatus.warning`, CLI „Warning: …“ a pokračuje); před renderem přepočet ze skutečných
  délek — nevejde-li se, proxy se nerenderují (`skipped` s poznámkou, varování, kopie zůstane ověřená); CLI `--dry-run`
  vypíše `Space: …` za každý svazek. Ověřeno CLI na obrazu disku: dva cíle na jednom svazku → součet v jednom řádku, tři →
  „Not enough space on D1, second (D1), third (D1): needs 314 MB (cards 204 MB, report 110 MB), free 312 MB.“
- Úkol 47 — doplnění: zámek složek před plánem (obsazená = `appendBusy` hned, nic se nezapíše), plán pod zámkem, jen
  nové soubory do stejných složek karty bez přepsání (`DestWriter` u doplnění přejmenuje `.offload-part` přes
  `renamex_np` RENAME_EXCL; soubor, který se mezitím objeví, zůstane a kopie je `failed` „file appeared meanwhile“),
  generace N s projektem dřívější zálohy, nic nového = Hotovo s poznámkou + doplnění chybějících řádků .xxh64 + obnova
  záznamu historie, neověřené dřívější soubory = nelze formátovat (varování), historie dostane generaci `append`
  (sjednocený seznam souborů, karta teď, projekt beze změny); zrušené doplnění zapíše generaci s `failed` u nedokončených
  a historii ne. Proxy: jména obsazená v `_PROXY` všech cílů, render i kopie proxy na další cíle nikdy nepřepíšou
  (`renameNoReplace`, `copyFile(replace: false)`) — platí i pro celou kopii. CLI `offload append <zdroj> [--into …]`
  (bez `--into` složky z historie), volby fáze 2 sdílené s `copy` (`StageTwo`), společné spuštění úlohy a kód návratu jako
  Windows (`JobRun`); `JobStatus.note`, `errorCode`. Odchylka od plánu: report doplnění do zálohy s MD5 (z Windows)
  nepíše `hashAlgorithms` s md5 — Mac zatím MD5 při kopii nepočítá (úkol 51), takže by v reportu chyběly hodnoty.
  Ověřeno CLI na obrazech disků (FAT32 karta, dva exFAT cíle): `offload append` → generace 2, `offload_report_0002.*`,
  `ascmhl/0002_…`, `.xxh64` prodloužený, všech 10 dřívějších souborů bajtově stejných (`shasum -c`), podruhé „Nothing
  new — the backup is complete.“ (kód 0), `offload verify` dne → 3 soubory OK, `ascmhl create` (kopie složky) přes obě
  generace projde. Doplnění odmítlo zálohu, u níž se na jednom cíli změnil čas souboru (konflikt, nic nezkopírováno).
  Windows `verify`/`append` téže složky neověřeno (Windows není po ruce). Oprava úkolu 43: shoda s historií porovnává
  časy v .NET ticks z `timespec` (APFS má nanosekundy, seznam v historii 100 ns — přes `Date` občas „změněná“ karta)
- Úkol 46 — plánovač doplnění (`AppendPlanner`): jedna záloha (`<kořen>/<datum>/<karta>` všude stejně), dostupné
  složky s čitelnými reporty a nepoškozeným řetězem ASC MHL stejné délky, nejvyšší číslo reportu = poslední generace;
  karta proti každé záloze (velikost, čas, FAT tolerance karty i cíle), soubor dobrý jen na části cílů, cizí soubor na
  cíli, selhaný soubor na disku, chybějící soubor, jiná karta, obsazené dočasné jméno `.…offload-part`; neověřené dřívější
  soubory (jediná definice `CardRecord.readBack`), součty a projekt dřívější zálohy; jen čte (test: nic nezapíše). Texty
  konfliktů anglicky podle Windows („different size or date (card 1.2 GB · 10-06 09:14, RAID …)“, disk = jméno svazku)
- Úkol 45 — Ověřit zálohu (`BackupVerifier`): všechny generace ASC MHL a hashe xxh64, md5, sha1, xxh3, xxh128, c4
  (každý soubor čtený jednou s F_NOCACHE, velikost zvlášť), záloha bez MHL proti .xxh64/.md5 (i CRLF), nejnovější záznam
  „failed“ ověřený proti starší platné generaci, nedokončená generace nikdy „v pořádku“ (oranžově, CLI kód 1), soubory
  navíc mimo `_REPORT`/`_PROXY`/`ascmhl` a ignore vzory MHL (přesunuté z `MHLReader` do `BackupVerifier.ignored`), složka
  dne = každá karta; report ověření (`VerifyReport`, anglické texty Windows `VerifyTexts`, CSS sdílené s reportem karty
  přes `HTMLReport.styles`, PDF přes nové `PDFReport.writeHTML`) do `_REPORT` karty, u cizího MHL vedle karty, nic do
  `ascmhl/`, HTML a JSON jen jako nové soubory; CLI `verify <karta|den> [--report]` (report jen s volbou, jako Windows),
  strojové řádky, souhrn, průběh; starý `Verifier` smazán. Ověřeno CLI na obrazech disků: karta i den OK s HTML/PDF/JSON,
  poškozený bajt (`dd conv=notrunc`) → MISMATCH a kód 1; `ascmhl create` (1.2, na kopii složky) po reportu ověření projde.
  Zjištění (není z tohoto úkolu): na cíli exFAT (fskit) vznikají k souborům i složkám AppleDouble `._*` (rozšířené atributy
  macOS) — `ascmhl diff` je hlásí jako nové soubory; Offload je přeskakuje (`Scanner`), ignore vzory MHL je neznají.
  Report ověření z Windows 0.6.0 a záloha ze Silverstacku/ShotPutu neověřeny (nejsou po ruce)
- Úkol 44 — úloha i CLI zapisují historii (záznam „unfinished“ od startu celé kopie, na konci nahrazený; zrušená
  kopie se smaže; „ověřeno“ z `BackupState` = jediná definice; zaneprázdněná historie = 60 s čekání, pak zápis na pozadí
  v pořadí s varováním `Msg.historyDelayed`, které po zápisu zmizí; `HistoryRecorder.drain` pro aplikaci a CLI), CLI řádek
  historie s plnými cestami před plánem, `--no-history`, `--name auto` (cívka z klipů, jinak jmenovka). Historie se pod
  XCTestem nikdy nezapíše do skutečné Application Support (bez `OFFLOAD_SETTINGS_DIR` vypnutá). Oprava: `offload copy --help`
  (i `offload --help`) psalo „Error: CommandError(… helpRequested …)“ — nápověda se od swift-argument-parser 1.5 vrací jako
  příkaz, jehož `run()` vyhodí žádost o nápovědu; teď ji vypíše parser. Nový `OFFLOAD_REPORTS_DIR` přesměruje místní kopie
  reportů (ruční zkoušky CLI do dočasných složek). Ověřeno CLI na obrazech disků (karta FAT32 jako kořen svazku, dva cíle
  exFAT, `hdiutil attach -nobrowse` do dočasné složky): `--name auto` → A001, podruhé řádek „Backed up on … — verified, safe to
  format“, `fsSerial` z boot sektoru obrazu, `history/<id>.files.gz` čte systémový `gzip -dc`
- Úkol 43 — shoda karty s historií (stejná / nové soubory / změněná / jiná karta, FAT tolerance celých hodin), texty
  řádku historie anglicky podle Windows `HistoryText` („Backed up on 2026-10-06 08:15 to Zaloha (RAID) — verified, safe to
  format“; datum `yyyy-MM-dd HH:mm` místně; Mac `diskName` = „složka (svazek)“, kořen svazku jen jméno), název karty
  z klipů a „další v řadě“ (`ReelNames`, `ReelSuggestion`), prohlídka karty `CardInspector` (jeden zrušitelný sken,
  výměna karty během skenu = chyba, nečitelný svazek = chyba, ne složka; `appendReachable` jen u svazku);
  `Scanner.scan(isCancelled:)`
- Úkol 42 — historie karet: history.json ve formátu Windows (verze 1, 5 000 záznamů, poškozený → .corrupt-…, vyšší
  verze jen číst) pod flock zámkem, otisk karty (SHA-256, .NET ticks, ordinální řazení v UTF-16), history/<id>.files.gz
  (gzip kolem raw DEFLATE, čte i soubor z `gzip -n` / .NET GZipStream — fixture z `gzip -n`, skutečný soubor z Windows
  čeká); cesta seznamu vždy z `id`; prázdný seznam = platný gzip
- Úkol 41 — zápis generace: reporty (jména generace; u generace ≥ 2 jen nové, všechna jména se zkontrolují dřív, než
  se zapíše první) → MHL → řetěz → seznamy součtů; .xxh64/.md5 jen prodloužit (dřívější bajty zůstanou, chybějící
  `\n` se doplní, chybějící řádky z MHL, bez opakování), commit výsledku ověření (MHL nejdřív, idempotentní,
  verifyPending nikdy), healHashList; PDF úlohy se jmenuje podle generace. Pozor na Swift: „\r\n“ je jeden Character,
  `split(separator: "\n")` ho nerozdělí — seznamy čte `OffloadReport.listLines` (opraveno i v CLI `verify`, kde by
  seznam s CRLF vypadal jako chybějící soubory)
- Úkol 40 — ASC MHL: čtení všech generací už bylo (`MHLReader`, XMLDocument s jmennými prostory, problémy anglicky,
  previousPath), přibylo `ignoresReportFolder` a vzory ignore (gitwildmatch, `MHLReader.ignored`, úkol 45 přesune);
  zápis generace N podle Windows `Mhl.Write`: generace 1 nikdy přes řetěz, jen N = poslední + 1, ignore z generace 1,
  xxh128 a md5, akce opravy (`MHL.entries`), metoda ověření v komentáři („ · verification: …“), `hasGeneration`,
  `IOError(message:)`. Fixture `Fixtures/ascmhl-two-generations` je ručně složená bajt po bajtu podle Windows
  `Mhl.cs` (generace 1 + doplnění, xxh64 + xxh128, čas s posunem), ne zachycená z Windows — skutečný výstup Windows
  čeká; test nad ní ověří i hashe souborů a doplnění generace 3 Macem. Ověřeno `ascmhl` 1.2 (pip ve venv; příkaz
  `verify` v 1.2 není, ověřuje `create`): generace 1+2 z Macu, kopie z CLI a fixture — vše „verified OK“, změněný
  soubor = „hash mismatch“, kód 11. Pozor: ascmhl porovnává vzor `ascmhl` i s cestou nad kartou — složka karty
  pod složkou jménem `ascmhl` se celá ignoruje
- Úkol 39 — dva stavy karty: Safe to eject (vše zapsané) × Safe to format (vše ověřené a zapsané); zelená (tón karty,
  zelený pruh, štítky `JobProgress.chips`), zvuk Glass (`JobProgress.chime`) a CLI 0 jen při Safe to format
  (`--no-verify` zůstává 0); `JobStatus.warning`; HTML odznak VERIFIED zeleně jen při `safeToFormat` (COPIED — VERIFYING,
  UNFINISHED) a věta Texts.safeToFormat / safeToEject pod hlavičkou; texty anglicky podle Windows `JobCard.cs`
  („Safe to eject“, „Do not format“, „Verifying…“); starší testy úkolů 8/11 čekají `safeToFormat`; GUI se nespouštělo
  (jen build), ruční kontrola se dvěma cíli a zvukem čeká
- Úkol 38 — CardRecord (všechny generace, reporty z Windows i Macu) a jediná definice „ověřeno“ (verifyPending/
  verifyIncomplete nikdy; cizí cesty = všechny sloupce; podle sériového čísla disku, R12); BackupState;
  `OffloadReport.isFailed` jako Windows `IsFailed` („written“ v ověřeném reportu = selhání, ne „neověřeno“ — testy plánu,
  které to čekaly jinak, pracují s neověřeným reportem); `SourceFile(relPath:size:modified:)`
- Úkol 37 — zámky historie, složky karty a dne přes flock v Application Support/Offload/locks (nikdy na cíli),
  složky v pevném pořadí bez čekání; `AppSupport.folder` (`OFFLOAD_SETTINGS_DIR`), `hwtest.json` tam taky; testy
  nastaví `OFFLOAD_SETTINGS_DIR` na dočasnou složku (`testSettingsDir`)
- Úkol 36 — svazek: sériové číslo FS z boot sektoru (exFAT/FAT/NTFS, jako Windows), záložně uuid:, kapacita,
  volumeSerial, klíč složky pro zámky, časy FAT s tolerancí celých hodin, .NET ticks; ověřeno: obrazy exFAT, FAT16
  a FAT32 pod dočasnou složkou — sériové číslo = bajty boot sektoru, kapacita = `diskutil` „Volume Total Space“;
  skutečné disky `/dev/rdisk*` jsou `root:operator` (bez sudo nečitelné → `uuid:`); srovnání s Windows čeká;
  cesty přes realpath (ne `resolvingSymlinksInPath`, ta ořízne `/private`), kořen svazku se odřízne jen jako skutečná
  předpona (systémový disk: statfs hlásí `/System/Volumes/Data`)
- Úkol 35 — nastavení zachová neznámé klíče a nečitelné sekce (projekty a pravidla z novější verze nezmizí); klíče
  naming, hash, outputs; v sekcích proxy/tools/naming/outputs se zachovají jen klíče, které tahle verze nezná —
  vymazaná volitelná hodnota (cesta k LUTu, nástroji) se ze starého JSON nevrátí, `portNames` se neslučuje
- Úkol 34 — hlášky kontrol a CLI česky (`Msg` v `Messages.swift`, `PreflightCode`, `OffloadError.code`), „Chyba: “,
  `formatBytesCs`; chyby parseru swift-argument-parser zůstávají anglicky; nahrazuje `Msg.swift` proudu A (texty
  stejné, `verifyHint` podle plánu = Windows, `Msg.bytes` → `formatBytesCs`, `Msg.count` bez použití vypuštěno)
- Úkol 33 — report čte i zapisuje sdílené klíče Windows 0.2–0.6 (generace, oprava, projekt, MD5, xxh128, ověření
  během proxy, pauza, odložené kopie); Mac nikdy nepíše unbuffered ani proxyEncoder; zápis souboru „jen nový“
  (renamex_np RENAME_EXCL, `FileExists`); report s verifyPending/verifyIncomplete nikdy není „lze formátovat“
  (`safeToEject` reportu → `safeToFormat`); `IO.writeFileNew`/`IO.renameNoReplace` proudu A nahrazeny verzí plánu
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
