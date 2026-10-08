# Offload — Verify-in-Proxy (návrh pro Windows, plán 10)

*2026-10-08. Zdroj: specifikace uživatele „Offload — Verify-in-Proxy: technická specifikace“ (Claude Docs,
https://claude.ai/artifact/TsRmoMDbhmT1Vac5gx4EZK). Tento dokument ji přebírá věcně a doplňuje rozhodnutí, jak ji
napojit na současný kód (0.5.1). Kde se liší, platí rozhodnutí v oddílu R.*

## 1. Cíl

Ověřit cílový disk během tvorby proxy, takže samostatné ověřovací čtení primárního cíle odpadá. Klasický postup čte data
třikrát (karta, ověření cíle, proxy z cíle); verify-in-proxy dvakrát. U 1 TB karty a disku 1 GB/s ušetří ~17 min.

Cíle:
- kartu číst právě jednou, zapisovat na N cílů současně (už dnes);
- hash zdroje počítat za běhu kopie (už dnes);
- cíl A (primární) ověřit ze stejného čtení, které dělá proxy dekodér;
- další cíle ověřovat souběžně na pozadí, bez zpomalení proxy;
- navíc hlásit obsahovou vadu souboru zjištěnou dekodérem;
- výstup ve standardu ASC MHL.

Mimo rozsah v1: hashování na GPU; opravy poškozených souborů (mimo existující „Zkopírovat znovu“); síťové cíle (NAS,
cloud) se ověřují klasicky.

## 2. Pojmy a stavy

Karta smí jít k formátování teprve tehdy, když jsou všechny cíle bitově ověřené. „Lze vyjmout“ a „Lze formátovat“ se
v UI nikdy nespojují.

| Stav | Úroveň | Kdy nastane |
|---|---|---|
| `copied` | soubor × cíl | zápis dokončen, flush/fsync proběhl, hash zdroje známý |
| `verified` | soubor × cíl | všechny bloky přečtené z fyzického cíle odpovídají hashi zdroje |
| `content_ok` | soubor | dekodér prošel celý soubor bez chyby |
| `content_warn` | soubor | dekodér hlásil chybu, ale bitově kopie sedí (vada už na kartě) |
| `failed` | soubor × cíl | nesoulad hashe nebo chyba I/O |
| `safe_to_eject` | karta | všechny soubory `copied` na všech cílech |
| `safe_to_format` | karta | všechny soubory `verified` na všech cílech a žádný `failed` |

Pojmy: **blok** = úsek 16 MiB (poslední kratší); **blokový hash** XXH3-64; **souborový hash** XXH128 (do MHL, viz R2);
**tap** = místo v proxy pipeline, kde se bajty načtené z cíle předávají hasheru; **primární cíl** = cíl, ze kterého se
tvoří proxy a který se ověřuje přes tap.

## 3. Architektura

Karta se čte jednou a zapisuje na všechny cíle. Proxy se tvoří z primárního cíle a stejné čtení ho ověří. Sekundární cíle
se ověřují souběžně na pozadí. Formátování se odemkne, až jsou ověřené všechny cíle.

Komponenty (mapování na kód v R1): SourceReader, TargetWriter (jeden na cíl, zápis bez cache, flush, atomické
přejmenování, manifest), ProxyQueue (přijímá soubory `copied` na primárním cíli), TapReader (režim A/B), BlockVerifier
(porovnává bloky s manifestem, hlídá chybějící rozsahy), BackgroundVerifier (sekundární cíle a soubory bez proxy, jedno
vlákno na fyzický disk), StateStore (stavy souborů a karet, zdroj pravdy pro UI, MHL a report).

## 4. Fáze 1 — kopie s fan-out a hashováním za běhu

1. Čtení zdroje sekvenčně po 16 MiB, ring buffer, obejití page cache; zdroj se hashuje jen tady.
2. Hash za běhu: pro každý blok XXH3-64 a průběžně XXH128 (a xxh64, R2) celého souboru, ve vlákně čtení.
3. Zápis: jedno vlákno na cíl, zápis bez page cache. Pomalejší cíl brzdí ring buffer, ne ostatní cíle.
4. Dokončení souboru: zápis do dočasného jména, flush, přejmenování, flush adresáře; teprve pak `copied`.
5. Manifest: po každém souboru řádek do `offload-manifest.jsonl` na každém cíli (R4): relativní cesta, velikost, mtime
   zdroje, XXH128, pole blokových hashů.

Pořadí souborů: video nejdřív (proxy může začít co nejdřív); sidecar (XML, RMD, zvuk, stills) v pauzách mezi velkými
soubory; proxy fronta přijme soubor hned po `copied` na primárním cíli, nečeká na celou kartu.

Navázání po přerušení: manifest obsahuje blokové hashe, při restartu se částečný soubor ověří po blocích a pokračuje se
od prvního chybějícího bloku (R6).

## 5. Fáze 2 — verify-in-proxy

Každý bajt, který proxy dekodér načte z primárního cíle, projde blokovým hasherem. Když všechny bloky souboru sedí
s manifestem, soubor je `verified` bez samostatného čtení.

Podmínka správnosti: čtení musí jít z fyzického disku, ne z page cache. Proto zápis bez cache už ve fázi 1 a před
otevřením pro proxy soubor vyhodit z cache.

**Režim A — ffmpeg s vlastním I/O** (v Offloadu ffmpeg běží jako proces, R3): tap čte z cíle po zarovnaných úsecích a
každý úsek předá `BlockVerifier.Feed(offset, data)`; seek je povolený (bitmapa bloků, částečné bloky se skládají podle
offsetu); blok se uzavře, jakmile má všechny bajty; opakované čtení ověřeného bloku se ignoruje, nesoulad při opakování =
chyba I/O.

**Režim B — SDK bez vlastního I/O** (BRAW, R3D, ARRIRAW): vyhodit soubor z cache; Offload ho přečte sekvenčně běžným
čtením a hashuje bloky (tím ověří a nahraje do RAM); SDK pak čte z page cache; soubor větší než volná RAM po oknech
(např. 4 GiB) se SDK v těsném závěsu. Který dekodér umí režim A, rozhoduje registr kodeků; ostatní automaticky B.

Rozhraní:
```
BlockVerifier(manifestEntry)
  Feed(offset, data)
  MissingRanges() -> ranges
  Finish() -> Verified | Mismatch(blockIndex) | Incomplete
```

## 6. Fáze 3 — dorovnání, další cíle, fallback

Co tap nepokryje, se dočte cíleně; nic se nečte celé znovu.
- Dorovnání primárního cíle: po proxy `MissingRanges()` → chybějící bloky (hlavička, index na konci kontejneru,
  přeskočené stopy) se přečtou bez cache a ověří; pak `Finish()`.
- Sekundární cíle: klasické čtení bez cache souběžně s proxy, jedno ověřovací vlákno na fyzický disk, nižší priorita I/O.
- Soubory bez proxy (zvuk, stills, sidecar, klipy vyřazené uživatelem, „bez proxy — originál je lehčí“): klasické čtení
  na všech cílech včetně primárního, v pauzách mezi proxy.
- Proxy vypnuté pro celou kartu: mechanismus se vypne, ověřuje se klasicky (úspora jen díky fan-out).
- Primární cíl = nejrychlejší cíl (měřeno za běhu kopie); uživatel ho může přepsat.

## 7. Obsahová verifikace dekodérem

Hash odhalí chybu kopie, dekodér vadný soubor z kamery; hlásí se odděleně.
- Dekodér musí projít každý snímek (proxy nepřeskakuje snímky ani nedekóduje jen klíčové).
- Sbírají se: chyby dekódování, chybějící/zdvojené časové značky, nesoulad počtu snímků proti metadatům, předčasný konec.
- Výsledek `content_ok` nebo `content_warn` s typem chyby a timecodem první vadné pozice.
- `content_warn` nepočítá jako selhání kopie a neblokuje `safe_to_format`, ale před formátováním vyžaduje výslovné
  potvrzení (vadný soubor se možná dá zachránit nástrojem výrobce přímo z karty).
- Zvukové stopy se dekódují také, pokud je proxy obsahuje.

## 8. Platforma (Windows)

Čtení bez cache: `FILE_FLAG_NO_BUFFERING` (zarovnané buffery a offsety na velikost sektoru). Vyhození z cache: zavřít
všechny handly a otevřít s `FILE_FLAG_NO_BUFFERING`; standby list vyčistí až zavření posledního handlu. Režim B: číst
normálně po zavření handlu z fáze 1. **Hlavní pravidlo: zapisovat bez cache už ve fázi 1**
(`FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH`), pak je vyhazování z cache jen pojistka. Zarovnání: buffery 16 MiB
zarovnané na 4 KiB; poslední blok číst do zarovnaného bufferu a oříznout. Kontrola za běhu: na začátku relace změřit, zda
čtení „bez cache“ dává rychlost disku, ne RAM; když ne, cíl přejde na klasické ověření a zapíše se to do reportu.

## 9. Výstupy

- **ASC MHL**: generace na každém cíli v `ascmhl/` s `xxh128` (+ `xxh64`, R2). Do MHL jen soubor `verified` na daném cíli;
  `failed` se nezapíše a je v reportu. Proces ověření jako `<process>` s metodou („verify-in-proxy“ / „read-back“).
  `offload-manifest.jsonl` zůstává vedle (R4), MHL ho nenahrazuje.
- **Report (PDF + JSON)**: karta, cíle, počet souborů a objem, čas kopie, čas do `safe_to_format`, metoda ověření pro
  každý cíl, seznam `content_warn` s timecodem, seznam `failed`.
- **UI**: řádek karty ukazuje dva stavy „Lze vyjmout“ a „Lze formátovat“ (druhý až po ověření všech cílů); řádek souboru
  tři tečky (kopie, bitové ověření pro každý cíl, obsahové ověření); průběh proxy a ověření primárního cíle jako jeden
  pruh; `content_warn` žlutý štítek s timecodem a tlačítkem náhledu místa vady.

## 10. Chybové stavy

Při pochybnosti spadnout na klasické ověření; nikdy neoznačit soubor jako ověřený bez důkazu z disku.

| Situace | Reakce |
|---|---|
| Nesoulad blokového hashe na cíli | blok znovu bez cache; sedí → varování o nestabilním čtení; nesedí → `failed` (oprava jen přes „Zkopírovat znovu“, R7) |
| Nesoulad na více cílech ve stejném bloku | podezření na zdroj: přečíst blok z karty znovu, porovnat, do reportu |
| Proxy spadne uprostřed souboru | ověřené bloky platí, zbytek dorovná fáze 3 |
| Uživatel zruší proxy | dorovnání chybějících bloků klasickým čtením |
| Cíl odpojen během kopie | cíl `failed` pro rozpracované soubory, ostatní pokračují; po připojení navázání podle manifestu |
| Karta vyjmuta před `safe_to_eject` | kopie se zastaví, rozpracované soubory zůstanou dočasné; po vložení navázání |
| Kontrola čtení bez cache selže | cíl přejde na klasické ověření celé karty |
| Pád aplikace | při startu se načte manifest a stavy se obnoví; bloky bez záznamu se ověří znovu |

## 11. Testy a přijetí

Správnost: překlopený bit na cíli (mimo Offload) se odhalí v režimu A i B; překlopený bit v bloku, který dekodér nečte
(index na konci), odhalí dorovnání; zkrácený soubor se odhalí; **test cache**: po kopii se změní bajt přímo na disku
(zápis mimo cache) — ověření ho musí najít; náhodné pořadí čtení dá stejný výsledek jako sekvenční; vadný soubor z kamery
(uříznutý snímek) dá `verified` + `content_warn`; přerušení na každé fázi a obnovení vede ke stejnému výsledku.

Výkon: čas do `safe_to_format` s jedním cílem a proxy nejvýš o 5 % delší než samotná proxy; při dvou cílech ověření B
nezpomalí proxy o víc než 5 %; režim B nenačte z disku víc než 1,05 × velikost souboru (čítače I/O).

## R. Rozhodnutí pro implementaci ve Windows verzi

- **R1 Mapování na kód.** Stávající `OffloadEngine` (čtení zdroje, `SourceHasher`, `DestWriter`, `Pauses`), `OffloadJob`
  (stage 1/2, proxy), `ProxyRenderer` (ffmpeg proces, feeder pro RAW, stall watchdog), `BackupState`/`CardRecord`
  (sdílená definice „ověřeno“), `Mhl`/`MhlReader`. Komponenty z oddílu 3 se mapují na tyto třídy nebo nové v
  `Offload.Core/VerifyInProxy/`; žádný paralelní druhý engine.
- **R2 Hash.** Souborový hash do MHL `xxh128` **a zároveň `xxh64`** (rozhodnutí uživatele): `.xxh64` seznam zůstává, aby
  starší Offload a Mac verze zálohu dál ověřily. MD5 režim (`both`) dál navíc. Blokové hashe XXH3-64 jen v manifestu.
  .NET: `System.IO.Hashing.XxHash128/XxHash3` (už v BCL, žádný nový NuGet; pokud by nebyl, rozhodnout v plánu).
- **R3 Režim A s ffmpeg procesem.** Offload nevolá libavformat, ale ffmpeg CLI. Tap = lokální HTTP server Offloadu
  (`127.0.0.1`, náhodný port, jednorázový token v cestě) s podporou `Range`, který čte soubor z primárního cíle bez cache
  a každý úsek předá `BlockVerifier`; ffmpeg dostane `http://127.0.0.1:<port>/<token>/<cesta>` (seekovatelné). Registr
  kodeků určí, které vstupy jdou přes HTTP (MOV/MP4/MXF s ffmpeg dekodérem); RAW přes stávající feeder = režim B.
  Ověřit v plánu na ffmpeg 8.1: seek, `moov` na konci, MXF index, výkon proti čtení z disku.
- **R4 Manifest.** `_REPORT/offload-manifest.jsonl` (do `_REPORT`, ne do kořene karty — `_REPORT` je v ignore vzorech
  MHL a „Ověřit zálohu“). Formát doplnit do FORMATY; Mac ho ignoruje.
- **R5 Stavy v UI a vysunutí.** `JobStatus.SafeToEject` dnes znamená „ověřeno“. Nově dva stavy: `SafeToEject` = vše
  `copied` (zapsáno + flush) na všech cílech; `SafeToFormat` = vše `verified`, žádný `failed`. Tlačítko „Vysunout kartu“
  se smí nabídnout po `SafeToEject`, ale karta a historie musí jasně ukázat, že formátovat zatím nelze; „Lze formátovat“
  (zelená) až po `SafeToFormat`. Všechny dosavadní zelené verdikty („kartu lze vysunout“, „záloha je úplná“) se přemapují
  na `SafeToFormat`, aby se nic falešně nezelenalo (sdílená definice z plánu 9 R1/R2 zůstává zdrojem pravdy pro
  „ověřeno“). Oznámení „hotovo“ až při `SafeToFormat`.
- **R6 Navázání po přerušení** v rozsahu: rozpracovaný soubor na cíli (dočasné jméno) se ověří po blocích z manifestu a
  pokračuje od prvního chybějícího bloku; kompletní soubory se nekopírují znovu. Napojit na doplnění (append) z plánu 6 —
  nevytvářet druhý mechanismus.
- **R7 Žádné automatické přepisování na cíli.** Spec říká „blok se přepíše ze zdroje“; ve Windows verzi platí pravidlo
  plánů 6–9 „nikdy nepřepsat existující soubor na cíli“ → nesoulad = `failed` + nabídka „Zkopírovat znovu“ (existující
  opravná úloha, odkládá vadnou kopii do `neshoda_NNNN`).
- **R8 Zápis bez cache** (`NO_BUFFERING | WRITE_THROUGH`) jen na lokální disky; síťové cíle a cíle, kde kontrola čtení bez
  cache selže, ověřují klasicky. Výkon: zarovnané 16 MiB buffery (stávající pool), poslední blok zarovnat a oříznout
  (`SetFileInformationByHandle` EndOfFile).
- **R9 Obsahová verifikace** z ffmpeg: `-xerror` ne (zastavil by proxy); místo toho výstup chyb dekodéru
  (`-err_detect`, stderr řádky, `-progress` počet snímků proti `nb_frames`/délce) → `content_warn` s typem a TC.
- **R10 Rozsah v1**: Windows, lokální disky, režim A pro MOV/MP4/MXF přes ffmpeg, režim B pro BRAW/R3D/ARRIRAW.
  `content_warn` blokuje formátování jen potvrzením. Mac jen zápis do VYVOJ-MAC.
- **R11 Výchozí stav**: verify-in-proxy zapnuté, když jsou zapnuté proxy a ověření; volba v Nastavení → Ověření
  („Ověřit primární disk během proxy“) a CLI `--classic-verify` pro starý způsob.
- **R12 Sériové číslo svazku cíle** zapsat do reportu u každého cíle (řeší parkovaný nález z plánu 9: prohození písmen
  disků → falešně zelený denní report).
