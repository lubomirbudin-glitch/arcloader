# Offload — společný formát na disku

Tohle je **smlouva mezi Mac a Windows verzí**. Co tu je, musí obě verze zapisovat stejně, aby šlo kopii
udělanou jednou verzí ověřit druhou (`offload verify`) i nástroji třetích stran (`ascmhl`, Silverstack, ShotPut).
Změna čehokoli tady = změna v obou verzích + záznam v obou `VYVOJ-*.md` + v [ROZDILY.md](ROZDILY.md).

## Struktura složek

```
<cíl>/<YYYY-MM-DD>/_REPORT/          denní report (Windows od 0.3.0, Mac naplánováno) — viz „Denní report“
    <YYYY-MM-DD>_day_report.html
    <YYYY-MM-DD>_day_report.pdf
    <YYYY-MM-DD>_clips.csv           seznam klipů dne (když je CSV zapnuté)
    <YYYY-MM-DD>_clips.ale           seznam klipů dne pro Avid (když je ALE zapnuté)
<cíl>/<YYYY-MM-DD>/<karta>/          karta = jmenovka svazku (nebo název složky), volitelně přepsaná
    …soubory karty se stejnou strukturou…
    _REPORT/
        offload_report.json
        offload_report.txt
        <karta>_report.html
        <karta>_report.pdf
        <karta>.xxh64
        <karta>.md5                   jen s xxh64 + MD5 (Windows od 0.3.0)
        <karta>_clips.csv             seznam klipů (Windows od 0.3.0)
        <karta>_clips.ale             seznam klipů pro Avid (Windows od 0.3.0, když je zapnutý)
        offload_report_0002.json      jen po doplnění (generace 2, 3 …): report jen nových souborů
        offload_report_0002.txt
        <karta>_report_0002.html
        <karta>_report_0002.pdf
        neshoda_0002/<cesta na kartě>  jen po opravě (Windows od 0.5.0): nesouhlasná kopie odložená před novou
        <karta>_verify_<YYYY-MM-DD_HHMMSS>Z.html   jen po ověření zálohy s reportem (viz „Report ověření“)
        <karta>_verify_<YYYY-MM-DD_HHMMSS>Z.pdf
        offload_verify_<YYYY-MM-DD_HHMMSS>Z.json
    ascmhl/
        0001_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl
        0002_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl     jen po doplnění nebo opravě
        ascmhl_chain.xml
    _PROXY/                           jen když se renderují proxy
        <složka klipu>/<klip>.mov     Windows od 0.5.1: složky jako na kartě (idle/1.mov, PRIVATE/M4ROOT/CLIP/C0001.mov)
        <klip>.mov                    Mac a Windows do 0.5.0: všechny proxy přímo v _PROXY
```

- Stejná karta podruhé týž den → `<karta>_2`, `<karta>_3` … (nikdy se nepřepíše starší offload).
- Rozpracovaný soubor: `.<jméno>.offload-part` ve stejné složce; po dokončeném zápisu a flushi (před ověřením
  zpětným čtením) se přejmenuje. Konečný název tedy znamená „plně zapsáno“, nikoli „ověřeno“ — ověřeno je
  teprve to, co report označí `verified` (a „kartu lze vysunout“).
  Nedokončená kopie tak nikdy nevypadá jako hotová.
- Reporty se zapisují přes `.<jméno>.tmp` a přejmenování.
- Datum a čas změny souborů na cíli = jako na kartě.
- Na kartu se nikdy nezapisuje.
- **Proxy ve složkách jako na kartě** (Windows od 0.5.1): proxy klipu `<složka>/<klip>.<ext>` je `_PROXY/<složka>/<klip>.mov`
  (klip v kořeni karty `_PROXY/<klip>.mov`; sekvence `.ari` je složka — proxy leží vedle ní, pojmenovaná po ní; R3D ve
  složce `.RDC`). Klipy stejného jména v různých složkách (`idle/1.mp4`, `listening/1.mp4`) tak dostanou stejně
  pojmenované proxy a přelinkují se podle jména. `_2`, `_3` … (a poznámka „Proxy přejmenována…“) jen při shodě ve stejné
  složce (`A.mov` + `A.mxf`) nebo s proxy, která tam už je. Složky kamery (`PRIVATE/M4ROOT/CLIP`, `XDROOT/Clip`,
  `CONTENTS/CLIPS001/…`) se zachovají také. **Starší zálohy** (Mac, Windows do 0.5.0) mají proxy přímo v `_PROXY`; nic se
  nepřesouvá. Doplnění pozná uspořádání zálohy z jejích reportů: plochá = některý klip z podsložky má `proxyPath`
  přímo v `_PROXY/` a žádný ve složce; pak i nové proxy jdou přímo do `_PROXY` se starým pravidlem `_2` (jedna záloha =
  jedno uspořádání). Bez proxy v záloze (nebo jen klipy v kořeni karty) = složky jako na kartě. Oprava proxy nerenderuje.

## `<karta>.xxh64`

Jeden řádek na soubor, UTF-8 bez BOM, konce řádků `\n`:

```
<xxh64 jako 16 malých hex číslic><dvě mezery><relativní cesta s />
```

Čtečka (`offload verify`) toleruje i `\r\n`.

Po doplnění (Windows od 0.2.0) se soubor nahradí atomicky (dočasné jméno + `ReplaceFile`): původní řádky zůstanou
bajt po bajtu, za nimi přijdou řádky nových souborů (a řádky, které se z dřívějšího doplnění nedostaly do seznamu,
ale MHL je zná). Starší `offload verify` (Mac i Windows 0.1.0), který čte jen tenhle seznam, tak ověří i doplněné soubory.

## `<karta>.md5` (Windows od 0.3.0, Mac naplánováno)

Jen když kopie počítala i MD5 (volba „xxHash64 + MD5“, `--hash both`; MD5 nikdy není samotné — `<karta>.xxh64` je vždy).
Formát `md5sum`: jeden řádek na soubor, UTF-8 bez BOM, `\n`:

```
<md5 jako 32 malých hex číslic><dvě mezery><relativní cesta s />
```

Po doplnění se prodlouží stejně jako `<karta>.xxh64` — je to třetí soubor záznamu, který doplnění přepíše (po `.xxh64` a řetězu MHL; dřívější bajty zůstanou, soubor se nahradí přes ReplaceFile, ostatní dřívější soubory zůstanou beze změny). Doplnění počítá stejné součty jako dřívější záloha (podle jejího
`hashAlgorithms`), ne podle aktuální volby.

## `offload_report.json`

- UTF-8 bez BOM, odsazené, **klíče seřazené**, názvy v camelCase (jako Swift `Codable`).
- Data: ISO 8601 v UTC na sekundy, např. `2026-10-04T08:00:00Z`.
- Vlastnosti s hodnotou `null` se vynechávají; `null` uvnitř polí (např. `errors`) zůstává.
- Relativní cesty vždy s `/`.

| Klíč | Typ | Význam |
|---|---|---|
| `tool` | string | `offload` |
| `version` | string | `0.1.0` (Mac), `0.5.1-win` (Windows) |
| `generation` | číslo? | jen Windows od 0.2.0 a jen v reportu doplnění nebo opravy (`offload_report_NNNN.json`): číslo generace ASC MHL; chybí = 1 (celá kopie) |
| `kind` | string? | jen Windows od 0.5.0: `repair` u opravy („Zkopírovat znovu“); chybí u celé kopie a doplnění. Mac ignoruje |
| `copyPausedSeconds`, `verifyPausedSeconds` | číslo? | jen Windows od 0.5.0: sekundy, které kopie / zpětné čtení stály pozastavené (od Pozastavit do Pokračovat); chybí, když se nepozastavovalo. Rychlosti a doby v TXT, HTML a PDF je odečítají; Mac ignoruje |
| `setAside[]` | objekt? | jen Windows od 0.5.0 a jen u opravy: `destination` (index cíle), `path` (cesta na kartě), `movedTo` (`_REPORT/neshoda_NNNN/<path>`) — nesouhlasné kopie přesunuté před novou; chybí, když žádná nebyla |
| `project` | string? | jen Windows od 0.4.0: projekt, ve kterém se karta kopírovala; chybí bez projektu. Doplnění zapíše projekt dřívější zálohy, ne projekt zobrazený v aplikaci. Denní report ho ukáže, když ho mají všechny karty dne stejný |
| `hashAlgorithm` | string | `xxh64` (vždy, i s MD5 — Mac ho jen zobrazí) |
| `hashAlgorithms` | string[]? | jen Windows od 0.3.0 a jen když nejsou jen xxh64: `["xxh64", "md5"]` |
| `cardName`, `date`, `source`, `sourceFilesystem` | string | |
| `started`, `copyFinished`, `finished` | datum | |
| `verified` | bool | proběhlo ověření zpětným čtením |
| `totalFiles`, `totalBytes` | číslo | |
| `destinations[]` | objekt | `root`, `cardPath`, `filesystem`, `filesOK`, `filesFailed`, volitelně `unbuffered` (jen `false`, jen Windows) |
| `files[]` | objekt | `path`, `size`, `modified`, `xxh64`?, `md5`? (32 malých hex, jen s MD5, Windows od 0.3.0), `sourceError`? (též `card changed while paused` — karta se během pauzy vyměnila nebo vytáhla, zbylé soubory se nekopírovaly), `destinations[]` (stav pro každý cíl), `errors[]` (chyba nebo `null` pro každý cíl; anglicky — HTML je zobrazí česky), `rewritten[]`? (jen oprava, Windows od 0.5.0: pro každý cíl `true` = nová kopie, `false` = dobrá kopie jen znovu přečtená) |
| `skipped[]` | string | vynechané systémové položky |
| `clips[]` | objekt | `clip` {`name`, `path`, `format`, `segments`, `bytes`, `isCameraProxy`}, `info`?, `probeError`?, `proxyStatus`, `proxyPath`? (relativní k složce karty, např. `_PROXY/idle/1.mov` — viz „Proxy ve složkách jako na kartě“), `proxyNote`?, `proxySeconds`?, `lut`? {`source`, `id`, `name`, `reason`} (použitý LUT a proč; Windows od plánu 5, Mac zatím ignoruje) |
| `proxy` | objekt? | nastavení proxy (`enabled`, `height`, `codec`, `bitrateMbps`, `lutPath`, `lutMode` (`auto`/`builtin`/`custom`/`none`), `lutBuiltin`, `lutFallback`, `burnFilename`, `burnTimecode`, `audio`, `skipCameraProxies`, `skipLighterSources` (jen Windows od 0.5.1), `copyToAllDestinations`, `frameAspect`, `frameScale`; jen Windows, volitelně `extraFolder`, od 0.4.0 `lutRules` a `brawGen4ToGen5` — viz níže) |
| `proxyEncoder` | string? | jen Windows: enkodér ffmpeg, kterým se renderovaly proxy (`h264_nvenc`, `libx264`, `prores_ks` …); chybí, když se žádná proxy nevyrenderovala |

Hodnoty výčtů:

- stav souboru na cíli: `pending`, `written`, `verified`, `failed`
- `clip.format`: `video`, `braw`, `r3d`, `arriraw`, `canonRaw`, `unknown`
- `proxyStatus`: `none`, `skipped`, `unsupported`, `done`, `failed`. Windows od 0.5.1: `skipped` s `proxyNote`
  „bez proxy — originál je lehčí (5,2 Mb/s ≤ 10 Mb/s)“ = proxy se nedělala, protože by nebyla lehčí než originál (viz
  `proxy.skipLighterSources`); není to chyba (HTML „bez proxy“ šedě). Nová hodnota výčtu nepřibyla, takže to Mac přečte.
- `proxy.codec`: `h264`, `hevc`, `proresProxy`, `proresLT`, `prores422`, `prores422HQ`
- `proxy.lutMode`: `auto`, `builtin`, `custom`, `none`. Chybí-li (starší soubor): s neprázdným `lutPath` → `custom`
  (vlastní LUT jako dřív), bez `lutPath` → `auto` se záložním „bez LUT“. `lutFallback` je id vestavěného LUTu nebo `null`.
- `clips[].lut.source`: `auto` (rozpoznaná kamera), `fallback` (kamera nerozpoznaná, záložní LUT), `builtin` (vestavěný
  LUT vybraný v nastavení), `custom` (vlastní `.cube`), `rule` (Windows od 0.4.0: vlastní pravidlo, `reason` např. „pravidlo 2: kamera obsahuje
  „FX6““; `id` je id vestavěného LUTu, u vlastního `.cube` `null`), `none` (bez LUTu — důvod v `reason`; i pravidlo s cílem
  „Bez LUT“ dává `none`, ne `rule`). `id` je id vestavěného LUTu, u `custom` a `none` `null`.
- `clips[].info.gamma`, `gamut`, `colorScienceGen` (volitelné, zatím jen Windows): u Blackmagic RAW gama, gamut a generace
  barevné vědy, ve kterých byl klip natočený (z `braw-decode --info`, např. `"Blackmagic Design Film"`, `5`). Podle nich se
  volí LUT. Mac je zatím nezapisuje a při čtení ignoruje.
- `clips[].info.reel` (volitelné, Windows od 0.3.0): cívka klipu — tag kamery (`reel_name`, `tape_name`, `reel` z ffprobe),
  jinak cívka z názvu klipu (`A001C003…` → `A001`), jinak název složky karty. Je i v proxy (stopa timecodu). Mac ignoruje.
- `clips[].info.cameraSerial` (volitelné, Windows od 0.4.0): sériové číslo těla kamery — Sony `M01.XML` (`Device serialNo`),
  ffprobe `com.apple.quicktime.camera.identifier` / `camera_serial_number`; ARRI a BRAW zatím ne (ověří vzorky). Mac ignoruje.
- `clips[].info.colorScienceGenOriginal` (volitelné, Windows od 0.4.0): u BRAW Film dekódovaného s převodem Gen 4 → Gen 5
  generace, ve které byl klip natočený (`colorScienceGen` je pak účinná, `5`); chybí, když se převod nepodařil (klip se
  dekódoval tak, jak byl natočený). Mac ignoruje.

Náhledy klipů (JPEG) se do JSON neukládají — jsou jen v HTML a PDF.

`proxy.lutRules` (pole, volitelné, Windows od 0.4.0; chybí, když žádné není): vlastní pravidla LUTů v pořadí priority,
každé `{ "match": "camera" | "serial" | "reel" | "folder", "value": "…", "lut": "<id vestavěného>" | "none" }` nebo
s `"lutPath": "<cesta k .cube>"` místo `lut` (právě jedno z nich). `camera` = model obsahuje hodnotu, `serial` = sériové
číslo se rovná, `reel` = cívka začíná hodnotou, `folder` = `<karta>/<složka klipu>` odpovídá vzoru s `*` a `?` (bez
ohledu na velikost písmen). Platí jen v režimu `lutMode: auto`, před rozpoznáním kamery; první shoda rozhoduje, i s cílem
`none`. Známé pravidlo s neplatným obsahem (prázdná `value`, `lut` i `lutPath` zároveň nebo ani jedno) se při načtení
zahodí. Pravidlo s neznámým `match` (z novější verze) a položka jiného tvaru se **zachovají beze změny** a při hledání
se přeskočí; pořadová čísla v hlášeních (`pravidlo 2`) se počítají i přes ně. `proxy.brawGen4ToGen5` (bool, chybí =
`false`, Windows od 0.4.0): BRAW Film Gen 4 dekódovat jako Gen 5 (`braw-decode --gen5`); týká se jen gamy Blackmagic
Design Film, Video a Extended Video zůstanou. **Neověřeno** — čeká na vzorek .braw Gen 4. Oba klíče jsou i v `settings.json`.

`proxy.extraFolder` (string, volitelný, zatím jen Windows): složka, do které se každá hotová proxy kopíruje navíc,
jako `<extraFolder>\<datum>\<karta>\<složka klipu>\<klip>.mov` — stejné podsložky jako v `_PROXY` (před 0.5.1 a u ploché
zálohy bez nich; při shodě jména ve složce `_2`, `_3`…). Chybí, když není nastavená. Stejný klíč
je v sekci `proxy` souboru `settings.json`. Mac ho zatím nezná a ignoruje (neznámé klíče se přeskakují).

`proxy.skipLighterSources` (bool, chybí = `true`, Windows od 0.5.1; i v `settings.json`, CLI `--proxy-always` = `false`):
klip bez proxy, když je originál H.264 nebo HEVC (`info.codec` začíná `h264` / `hevc`), není větší než proxy v žádné ose
(výška ≤ `height`, šířka ≤ `height` × 16/9) a jeho datový tok videa (ffprobe: `bit_rate` proudu, jinak kontejneru minus
zvukové proudy) je nejvýš datový tok proxy (H.264/HEVC `bitrateMbps`; ProRes Proxy 45, LT 102, 422 147, HQ 220 Mb/s ×
(výška/1080)²). Ostatní kodeky (ProRes, DNx, MJPEG, MPEG-2, RAW) a originály větší než proxy — i s nízkým tokem —
proxy dostanou vždy; neznámý tok nebo rozměr = proxy. Náhledy klipu vznikají dál, kontrola místa s jeho proxy nepočítá.
Mac klíč nezná a ignoruje ho (renderuje vše).

## ASC MHL v2

- `urn:ASC:MHL:v2.0`, první generace (`0001_…`), hash `xxh64` pro každý soubor,
  `action="original"` (zapsáno/ověřeno) nebo `"failed"`. S MD5 (Windows od 0.3.0) má `<hash>` navíc prvek
  `<md5 action=… hashdate=…>` se stejným `action` a `hashdate`.
- Ignorované vzory: `.DS_Store`, `ascmhl`, `ascmhl/`, `_REPORT`, `_PROXY` + vynechané systémové soubory.
- `ascmhl_chain.xml` s C4 ID souboru MHL (`c4` + base58(SHA-512), doplněno `1` na 88 znaků).
- Ověřitelné nástrojem `ascmhl verify <složka karty>`.

### Doplnění (Windows od 0.2.0, Mac naplánováno)

Karta, na kterou kamera po offloadu natočila další klipy, se doplní do **stejné** složky karty na všech cílech
(`offload append`, v GUI zaškrtávátko „Doplnit jen nové klipy do dřívější zálohy“):

- zkopírují se jen nové soubory; existující soubory ani dřívější reporty se nikdy nepřepíšou (soubor, který se mezitím
  na cíli objeví, kopii zastaví);
- nová generace ASC MHL `NNNN_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl` obsahuje **jen nové soubory** (`action="original"`,
  `<process>transfer</process>`, `<comment>Offload doplnění: 12 nových souborů z E:\</comment>`, `<ignore>` stejné jako
  generace 1); `ascmhl_chain.xml` se nahradí (dočasné jméno + `ReplaceFile`): dřívější záznamy `<hashlist>` mají stejné
  `sequencenr`, cestu a C4 (zapíšou se znovu, ne bajt po bajtu) a přibude nový záznam s číslem N a C4 nového souboru;
- report doplnění `_REPORT/offload_report_NNNN.json/.txt`, `<karta>_report_NNNN.html/.pdf` (NNNN = generace MHL, na všech
  cílech stejná; dřívější doplnění nikdy nepřepíše) s klíčem `generation`; `<karta>.xxh64` se rozšíří;
- pořadí zápisu: reporty `_NNNN` → soubor generace MHL → řetěz (bod potvrzení) → `<karta>.xxh64` naposled; přerušení před
  řetězem nechá reporty bez generace MHL a další doplnění to odmítne;
- generace = nejvyšší číslo v řetězu a v reportech + 1; kdyby se počet reportů a generací MHL lišil (nedokončené
  doplnění, `ascmhl create` jiného nástroje), nebo se lišil mezi cíli, Offload nic nekopíruje a složku je třeba opravit
  ručně;
- doplnění odmítne: nedostupný dřívější cíl; chybějící report nebo ASC MHL; poškozenou historii MHL (změněná generace,
  soubor mimo řetěz); kartu, na které není ani jeden soubor dřívější zálohy; soubor na kartě, který má v záloze jinou
  velikost nebo čas (tolerance 2 s, na FAT celé hodiny); soubor na cíli, který žádný report nezná;
- dvě doplnění do téže složky karty najednou nejdou: pojmenovaný mutex `Local\Offload-append-<sha256(svazek|cesta)>`
  na každou složku karty, bez čekání (druhé skončí „Do této zálohy právě doplňuje jiná úloha …“); celá kopie zámek nebere.

### Oprava — „Zkopírovat znovu“ (Windows od 0.5.0, Mac naplánováno)

Soubory, jejichž **nejnovější** záznam v reportech na cílech selhal, se znovu zkopírují z karty — jen na cíle, kde
selhaly — jako další generace ve stejné složce karty (jen GUI, tlačítko na kartě úlohy):

- kontroly zálohy a pořadí zápisu jsou stejné jako u doplnění (složky, reporty, neporušený řetěz MHL, stejný počet
  generací na všech cílech, zámek `Local\Offload-append-…`); generace N = max + 1 na **všech** cílech;
- **zaznamenaný hash se nikdy nemění**: očekávaný xxh64 je z nejnovější generace, která není oprava (celá kopie nebo
  doplnění; z opravy jen pro soubor, který žádná jiná generace nehashovala). Karta se před jakýmkoli zápisem přečte a
  porovná; jiný obsah než při záloze = celá oprava odmítnuta bez zápisu (nový hash je jen v české hlášce). Změní-li se
  karta až mezi plánováním a kopírováním, nová kopie se zahodí, nesouhlasná se nepřesune a soubor zůstane `failed` s
  `sourceError` `card differs from the backup: xxh64 <nový> != recorded <zaznamenaný>`; v reportech, MHL a seznamech
  hashů je pořád zaznamenaný hash;
- dobrá kopie na ostatních cílech se nepřepíše, jen přečte a porovná se zaznamenaným hashem; dobrá kopie, jejíž nejnovější
  záznam nebyl přečten zpět (zrušené ověření, záloha bez ověření), se jen přečte (karta se pro ni nepotřebuje);
  oprava záloh bez ověření se odmítne;
- karta ve čtečce musí být ta, ze které se kopírovalo (identita svazku); jiná = odmítnutí bez zápisu;
- nesouhlasná kopie, která na cíli je, se před novou přesune do `_REPORT/neshoda_NNNN/<cesta>` (nikdy se nesmaže ani
  nepřepíše; je-li místo obsazené, použije se první volné `neshoda_NNNN_2`, `_3`, …; obsadí-li se až po plánování,
  soubor na tom cíli selže s `set aside: …` a nic se nepřepíše);
- generace MHL N obsahuje opravované soubory na všech cílech: nová kopie `action="original"`, přečtená dobrá kopie
  `action="verified"` (`hashdate` = čas opravy), `<comment>Offload oprava: 1 soubor znovu z E:\</comment>`;
- report `offload_report_NNNN.*` s `kind: "repair"`, `rewritten`, `setAside`; `<karta>.xxh64` (a `.md5`) se přepíše bez
  opakovaných řádků; nic k opravě = nic se nezapíše.

## Report ověření (Windows od 0.2.0)

„Ověřit zálohu“ (GUI) a `offload verify --report` zapisují jen do složky `_REPORT`, nikdy do `ascmhl/` — `_REPORT` je
v ignore vzorech MHL, takže další `ascmhl verify` projde. **Výjimka:** MHL z jiného programu (Silverstack, ShotPut,
`ascmhl` bez našeho ignore) `_REPORT` v ignore vzorech nemusí mít; pak by report v kartě udělal z kopie „soubor navíc“,
a proto se uloží do `_REPORT` vedle složky karty (`<cíl>\<datum>\_REPORT\<karta>_verify_….html/.pdf/.json`) a aplikace to
řekne („Report ověření je uložený mimo složku karty …“). Do karty se nezapisuje nikdy.

- `<karta>_verify_<YYYY-MM-DD_HHMMSS>Z.html` a `.pdf` (čas začátku ověření v UTC);
- `offload_verify_<YYYY-MM-DD_HHMMSS>Z.json` (mimo složku karty `<karta>_verify_<YYYY-MM-DD_HHMMSS>Z.json`) — UTF-8 bez BOM,
  seřazené klíče, data ISO 8601 UTC na sekundy:

| Klíč | Typ | Význam |
|---|---|---|
| `tool`, `version` | string | `offload`, `0.2.0-win` a novější |
| `cardPath` | string | ověřená složka karty |
| `started`, `finished` | datum | |
| `basis` | string | `ascmhl` (všechny generace) nebo `hashList` (jen `<karta>.xxh64` / `.md5`) |
| `generations` | číslo | počet přečtených generací MHL (u `hashList` 0) |
| `hashAlgorithms` | string[] | např. `["xxh64"]`; čtou se `xxh64`, `md5`, `sha1`, `xxh3`, `xxh128`, `c4` |
| `totalFiles`, `totalBytes`, `ok` | číslo | soubory v záznamu, jejich velikost, kolik sedí |
| `changed[]`, `missing[]`, `extra[]` | string[] | relativní cesty s `/`; `extra` = soubory, které záznam nezná (mimo `_REPORT`, `_PROXY`, `ascmhl` a ignore vzory MHL) |
| `failedAtCopy[]` | string[] | soubory, které selhaly už při kopii a nemají žádný platný starší záznam |
| `failedSuperseded[]` | string[] | nejnovější záznam je `failed`, ale starší generace je platná — soubor sedí podle ní |
| `unreadable[]` | objekt[] | `{path, error}` |
| `chainProblems[]` | string[] | česky: změněná nebo chybějící generace, chybějící řetěz |
| `success` | bool | nic nesedí špatně, nechybí, není nečitelné a nic nezůstalo `failedAtCopy` (soubory navíc výsledek neovlivní) |

## Seznam klipů — `_clips.csv`, `_clips.ale` (Windows od 0.3.0, Mac naplánováno)

Odvozené soubory: přepisují se celé (dočasné jméno + nahrazení) při každém zápisu reportu, po doplnění pokrývají všechny
generace. Doplnění mu přepíše stejně jako ostatní odvozené soubory; závod s doplněním z jiného procesu (druhé okno, CLI) při přepisu seznamu ve stage 2 se neřeší — vyhraje poslední zápis a další zápis reportu seznam obnoví. Jeden řádek na klip (proxy nahrané kamerou — složky `SUB`, `PROXY` — se neuvádějí); bez analýzy klipů jen
klipy nalezené v souborech, metadata prázdná.

**CSV** (`<karta>_clips.csv`, `<datum>_clips.csv`): UTF-8 **s** BOM, oddělovač čárka, konce řádků CRLF, RFC 4180 (pole
s čárkou, uvozovkou nebo koncem řádku v uvozovkách, uvozovky zdvojené), hlavička vždy, sloupce v tomto pořadí:
`Clip Name`, `File Name` (hlavní soubor s příponou), `Path` (relativní, `/`), `Card` (složka karty), `Camera`, `Reel`,
`Start TC` (jak ho dá zdroj, `;` u drop-frame; prázdné bez TC), `Duration` (`HH:MM:SS:FF` v nominálních snímcích; bez fps
`H:MM:SS`), `FPS` (invariantně, `23.976`, `25`, `29.97`), `Resolution` (`3840x2160`), `Codec` (u RAW formát, např.
`Blackmagic RAW`), `LUT` (název použitého LUTu), `Size (bytes)`, `XXH64` a `MD5` (hlavního souboru; u vícedílných klipů
prvního dílu; MD5 prázdné bez MD5). Hodnoty začínající `=`, `+`, `-`, `@` se neupravují.

**ALE** (`<karta>_clips.ale`, `<datum>_clips.ale`): UTF-8 **bez** BOM (starší verze Avidu mohou diakritiku přečíst špatně), CRLF, tabulátory (tabulátor a konec řádku uvnitř
hodnoty → mezera). Hlavička `Heading` / `FIELD_DELIM TABS` / `VIDEO_FORMAT` (nejčastější výška: `1080`, `720`, jinak
`CUSTOM`) / `AUDIO_FORMAT 48khz` / `FPS`, pak `Column` se sloupci `Name, Tape, Start, End, Tracks, Source File, Camera,
LUT, Resolution, Codec, XXH64, MD5` a `Data`. `Tape` = cívka, `End` = začátek + délka (výlučně, drop-frame počítá jako
kamera), bez TC začátek `00:00:00:00`, `Tracks` = `V` + `A1…An` podle zvukových kanálů (nejvýš 8). ALE má jedno FPS:
smíšené fps → soubor na každé, `<název>_clips_25.ale`, `<název>_clips_23.976.ale` (klip bez fps patří k nejčastějšímu;
když fps nezná žádný klip, hlavička `FPS 25`). ALE, které se při novém zápisu už nevytváří (změnily se frekvence), se smaže — jen když ALE je zapnuté nebo už existuje. Seznam, který je vypnutý, ale už je ve složce (CSV i ALE, u ALE všechny soubory frekvencí), se při dalším zápisu přesto přepíše (odvozený soubor: jinak by ukazoval jen dřívější generace); nikdy se nemaže a nově se nevytvoří.

## Denní report (Windows od 0.3.0, Mac naplánováno)

`<cíl>/<YYYY-MM-DD>/_REPORT/<YYYY-MM-DD>_day_report.html/.pdf` + seznam klipů dne. Odvozené soubory (přepisují se celé,
poslední zápis vyhraje — i proti Macu, který by zapisoval do stejného dne). Karty dne = podsložky `<datum>/` kromě
`_REPORT` a skrytých; složka bez `_REPORT/offload_report.json` je v reportu „bez reportu (probíhá, nebo byl přerušen)“.
Vytváří se znovu po každé úloze na každém jejím cíli (i doplnění a zrušená úloha) a na vyžádání; jeden zapisovatel na den
(Windows: pojmenovaný mutex `Local\Offload-day-<sha1(klíč dne)>`, klíč viz „Pravidla“). `_REPORT` ve složce dne nemá `offload_report.json` ani
`.xxh64`, takže ho `verify` za kartu nepovažuje; sdílí ho report ověření cizí zálohy (`<karta>_verify_<stamp>Z.*`).

Pravidla (Windows od 0.3.0):

- Denní report nikdy nesahá na soubory ověření v téže `_REPORT` (`*_verify_*.json/.html/.pdf`) — zapisuje jen svoje názvy
  (`_day_report.html/.pdf`, `<datum>_clips.csv`, `<datum>_clips.ale` a `<datum>_clips_<fps>.ale`; stejně jako u karty smaže ALE, které se už nevytváří). Kořen disku, složka karty ani složka bez karty s reportem se za den nepovažují (česká chyba, nic se nezapíše).
- Jen podsložky s `_REPORT\` nebo `ascmhl\` jsou karty; ostatní (např. `SOUND`) mají řádek „jiná složka (bez reportu)“.
- `<karta>\_REPORT` vzniká hned na začátku kopie (prázdná), aby byla rozpracovaná nebo přerušená karta vidět; denní
  report ji ukáže jako „bez reportu (probíhá, nebo byl přerušen)“. Prázdný `_REPORT` ostatní funkce (ověření, doplnění,
  historie) berou jako kartu bez reportu.
- Jména karet `_REPORT`, `_PROXY` a `ascmhl` (v jakékoli velikosti písmen) dostanou na konec `_`, aby nesplynula se
  složkami nástroje.
- Zámek dne a zámek doplnění používají klíč „id svazku + cesta na něm“. Namapovaný disk, jeho cesta UNC, `subst`, junction
  a krátká jména 8.3 dávají jiné klíče, takže dva zapisovatelé přes různá pojmenování téhož místa se nevylučují (poslední
  zápis vyhraje, soubory se přepisují celé).
- Zrušená úloha čeká na obsazený den nejvýš 5 s na cíl, pak denní report přeskočí (do logu).

## settings.json (jen tento počítač)

- Neznámé klíče se při uložení zachovají (na horní úrovni, v `proxy` i v `tools`; i sekce `proxy` nebo `tools` s hodnotou
  z novější verze, kterou tahle verze nedokáže přečíst, zůstane beze změny) — Windows od 0.2.0 (verze 0.1.0 je smazala).
- `naming.suggestFromClips` (bool, výchozí `true`, Windows od 0.2.0): navrhovat název složky karty z názvů klipů.
- `hash` (`"xxh64"` | `"both"`, výchozí `"xxh64"`, Windows od 0.3.0): kontrolní součty kopie; `both` = xxh64 + MD5. Jiná
  hodnota (z novější verze) se zachová a čte jako `xxh64`.
- `outputs` (Windows od 0.3.0): `dayReport` (bool, `true`) — denní report po každé úloze; `clipListCsv` (bool, `true`),
  `clipListAle` (bool, `false`) — seznamy klipů. Neznámé klíče sekce se zachovají.
- Projekty (Windows od 0.4.0): klíče profilu na horní úrovni (`destRoots`, `verify`, `analyzeClips`, `hash`, `naming`,
  `outputs`, `notify`, `proxy`) jsou profil „Bez projektu“; `projects` je pole pojmenovaných profilů stejného tvaru
  s klíčem `name` (oříznutý, 1–60 znaků, jedinečný bez ohledu na velikost písmen, „Bez projektu“ vyhrazené);
  `activeProject` je jméno nebo `null` (jméno neexistujícího projektu se při čtení změní na `null`; zachová se jen
  jméno projektu uloženého beze změny — položka, kterou tato verze nečte — a platí „Bez projektu“; `activeProject`
  jiného typu než text se zachová beze změny). Duplicity se **nemažou**: platí první položka, další se stejným jménem a položky bez platného jména
  zůstanou v souboru beze změny a při uložení se zapíšou zpět za čitelné projekty (nic se neztratí ani nenásobí).
  `projects`, které není pole, se zachová beze změny, dokud se v aplikaci neuloží projekt. Společné zůstávají `tools`,
  `portNames` a `updates`.
- `notify` (Windows od 0.4.0, součást projektu): `windows` (bool, `true`) — oznámení Windows; `ntfy` { `enabled` (bool,
  `false`), `server` (string, `https://ntfy.sh`), `topic` (string, `""`; písmena bez diakritiky, číslice, `-` a `_`,
  nejvýš 64 znaků) } — bez přihlašovacího tokenu.
- `updates` (Windows od 0.4.0, společné): `manifest` (string | `null`, výchozí `null` = kontrola vypnutá) — http(s) adresa
  nebo cesta k `offload-update.json` (`{"version": "0.4.0", "url": "…", "notes": "…"}`, UTF-8). Aplikace přečte nejvýš
  64 KB; `version` nejvýš 32 znaků, `url` nejvýš 1000, `notes` se zkrátí na 2000; řídicí a formátovací znaky ve verzi
  nebo adrese manifest vyřadí.

## Místní soubory (jen tento počítač, Mac převezme stejný tvar)

`%APPDATA%\Offload\history.json` + `%APPDATA%\Offload\history\<id>.files.gz` (Mac: `~/Library/Application Support/Offload/`)
— historie offloadů, nikam se neposílá (složku lze přesměrovat proměnnou `OFFLOAD_SETTINGS_DIR`). UTF-8 bez BOM,
odsazené, data ISO 8601 UTC na sekundy; zapisuje GUI i CLI (`--no-history` vypne) pod pojmenovaným mutexem
`Local\Offload-history`, přes dočasné jméno. Zapisuje se konec dokončené kopie nebo doplnění; zrušená kopie ne. Kořen
`{ "version": 1, "entries": [...] }`; vyšší neznámá verze = jen číst, nezapisovat. Nejvýš 5 000 záznamů (nejstarší
podle `finished` se smažou i se seznamem souborů). Poškozený soubor se přejmenuje na
`history.json.corrupt-<yyyyMMdd-HHmmss>` a začne se prázdný.

| Klíč záznamu | Typ | Význam |
|---|---|---|
| `id` | string | `20261006T081500Z-3f2a9c1b` (konec kopie v UTC + 8 hex) |
| `card` | objekt? | svazek karty: `identity` (`\\?\Volume{…}\|1A2B3C4D`, jen informace), `fsSerial`, `capacity`, `label`, `filesystem`; u ručně přidané složky `null` |
| `source`, `cardName`, `date` | string | zdroj, název složky karty, složka data |
| `project` | string? | projekt, ve kterém se karta kopírovala (Windows od 0.4.0), jinak `null`; doplnění ho nemění |
| `finished` | datum | konec poslední kopie / doplnění |
| `success`, `verified` | bool | všechny generace bez chyb / ověřené |
| `failedFiles` | číslo? | jen když `success` = false |
| `hashAlgorithms` | string[] | `["xxh64"]`, s MD5 `["xxh64", "md5"]` (Windows od 0.3.0) |
| `files`, `bytes` | číslo | sjednocení všech generací |
| `fingerprint`, `shape` | string | SHA-256 (hex) přes řádky seřazené podle cesty `relpath\tsize\tmtimeTicksUtc\n` resp. `relpath\tsize\n` |
| `fileList` | string | `history/<id>.files.gz` — gzip řádků `relpath\tsize\tmtimeTicksUtc\n` (seznam po všech generacích) |
| `destinations[]` | objekt | `root`, `cardPath` |
| `generations[]` | objekt | `seq`, `kind` (`full` / `append` / `repair` — Windows od 0.5.0), `finished`, `files`, `bytes`, `verified`; po opravě se `success` a `failedFiles` záznamu spočítají znovu z reportů na cílech |
| `reels[]` | string | cívky z názvů klipů (`A001`) |

Karta se pozná podle `fsSerial` + `capacity` (GUID svazku dává Windows podle čtečky, proto se k poznání nepoužije);
ručně přidaná složka podle `shape`. Doplnění zapíše do záznamu další generaci pod jedním držením zámku.

`%APPDATA%\Offload\update-check.json` (jen Windows, od 0.4.0): stav kontroly aktualizací — `lastCheck` (datum UTC
posledního čtení manifestu, i neúspěšného), `lastSource` (adresa manifestu, ze které se četlo; jiná adresa = číst hned),
`lastVersion`, `lastUrl`, `lastNotes` (co manifest naposledy řekl). Manifest se čte nejvýš jednou za 24 h (`lastCheck`
v budoucnosti = číst hned); poškozený soubor = číst znovu. Odkaz „Nová verze“ otevře jen adresu `https://…`; síťové
cesty (`\\server\…`, `file://server/…`, namapovaný síťový disk) se nikdy neotevírají, jen zobrazí; místní složku
a zip otevře Průzkumník výběrem (`/select`).
