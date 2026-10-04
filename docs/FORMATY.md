# Offload — společný formát na disku

Tohle je **smlouva mezi Mac a Windows verzí**. Co tu je, musí obě verze zapisovat stejně, aby šlo kopii
udělanou jednou verzí ověřit druhou (`offload verify`) i nástroji třetích stran (`ascmhl`, Silverstack, ShotPut).
Změna čehokoli tady = změna v obou verzích + záznam v obou `VYVOJ-*.md` + v [ROZDILY.md](ROZDILY.md).

## Struktura složek

```
<cíl>/<YYYY-MM-DD>/<karta>/          karta = jmenovka svazku (nebo název složky), volitelně přepsaná
    …soubory karty se stejnou strukturou…
    _REPORT/
        offload_report.json
        offload_report.txt
        <karta>_report.html
        <karta>_report.pdf
        <karta>.xxh64
    ascmhl/
        0001_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl
        ascmhl_chain.xml
    _PROXY/                           jen když se renderují proxy
        <klip>.mov
```

- Stejná karta podruhé týž den → `<karta>_2`, `<karta>_3` … (nikdy se nepřepíše starší offload).
- Rozpracovaný soubor: `.<jméno>.offload-part` ve stejné složce; po dokončeném zápisu a flushi (před ověřením
  zpětným čtením) se přejmenuje. Konečný název tedy znamená „plně zapsáno“, nikoli „ověřeno“ — ověřeno je
  teprve to, co report označí `verified` (a „kartu lze vysunout“).
  Nedokončená kopie tak nikdy nevypadá jako hotová.
- Reporty se zapisují přes `.<jméno>.tmp` a přejmenování.
- Datum a čas změny souborů na cíli = jako na kartě.
- Na kartu se nikdy nezapisuje.

## `<karta>.xxh64`

Jeden řádek na soubor, UTF-8 bez BOM, konce řádků `\n`:

```
<xxh64 jako 16 malých hex číslic><dvě mezery><relativní cesta s />
```

Čtečka (`offload verify`) toleruje i `\r\n`.

## `offload_report.json`

- UTF-8 bez BOM, odsazené, **klíče seřazené**, názvy v camelCase (jako Swift `Codable`).
- Data: ISO 8601 v UTC na sekundy, např. `2026-10-04T08:00:00Z`.
- Vlastnosti s hodnotou `null` se vynechávají; `null` uvnitř polí (např. `errors`) zůstává.
- Relativní cesty vždy s `/`.

| Klíč | Typ | Význam |
|---|---|---|
| `tool` | string | `offload` |
| `version` | string | `0.1.0` (Mac), `0.1.0-win` (Windows) |
| `hashAlgorithm` | string | `xxh64` |
| `cardName`, `date`, `source`, `sourceFilesystem` | string | |
| `started`, `copyFinished`, `finished` | datum | |
| `verified` | bool | proběhlo ověření zpětným čtením |
| `totalFiles`, `totalBytes` | číslo | |
| `destinations[]` | objekt | `root`, `cardPath`, `filesystem`, `filesOK`, `filesFailed`, volitelně `unbuffered` (jen `false`, jen Windows) |
| `files[]` | objekt | `path`, `size`, `modified`, `xxh64`?, `sourceError`?, `destinations[]` (stav pro každý cíl), `errors[]` (chyba nebo `null` pro každý cíl) |
| `skipped[]` | string | vynechané systémové položky |
| `clips[]` | objekt | `clip` {`name`, `path`, `format`, `segments`, `bytes`, `isCameraProxy`}, `info`?, `probeError`?, `proxyStatus`, `proxyPath`?, `proxyNote`?, `proxySeconds`? |
| `proxy` | objekt? | nastavení proxy (`enabled`, `height`, `codec`, `bitrateMbps`, `lutPath`, `burnFilename`, `burnTimecode`, `audio`, `skipCameraProxies`, `copyToAllDestinations`, `frameAspect`, `frameScale`) |

Hodnoty výčtů:

- stav souboru na cíli: `pending`, `written`, `verified`, `failed`
- `clip.format`: `video`, `braw`, `r3d`, `arriraw`, `canonRaw`, `unknown`
- `proxyStatus`: `none`, `skipped`, `unsupported`, `done`, `failed`
- `proxy.codec`: `h264`, `hevc`, `proresProxy`, `proresLT`, `prores422`, `prores422HQ`

Náhledy klipů (JPEG) se do JSON neukládají — jsou jen v HTML a PDF.

## ASC MHL v2

- `urn:ASC:MHL:v2.0`, první generace (`0001_…`), hash `xxh64` pro každý soubor,
  `action="original"` (zapsáno/ověřeno) nebo `"failed"`.
- Ignorované vzory: `.DS_Store`, `ascmhl`, `ascmhl/`, `_REPORT`, `_PROXY` + vynechané systémové soubory.
- `ascmhl_chain.xml` s C4 ID souboru MHL (`c4` + base58(SHA-512), doplněno `1` na 88 znaků).
- Ověřitelné nástrojem `ascmhl verify <složka karty>`.
