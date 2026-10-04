# Offload

Kopírování paměťových karet z kamery na více disků najednou, s ověřením, reportem a volitelnými proxy s LUT. macOS 13+.

## Jak to funguje

1. **Kopie** – karta se čte **jen jednou** (velké bloky, bez page cache), každý blok jde paralelně na všechny cíle
   a zároveň do xxHash64. Rychlost = nejpomalejší článek (karta nebo nejpomalejší disk).
2. **Ověření** – po `F_FULLFSYNC` se každý soubor přečte z každého disku zpět (bez cache) a porovná se zdrojovým hashem.
   Teprve potom aplikace hlásí „kartu lze vysunout“.
3. **Report** – `<root>/<YYYY-MM-DD>/<karta>/_REPORT/`: HTML (náhledy, metadata, TC), JSON, TXT, seznam xxh64;
   `ascmhl/` = ASC MHL v2 (ověřitelné `ascmhl` / Silverstack / ShotPut …).
4. **Proxy (volitelné)** – po ověření, čte se z disku (karta už není potřeba): scale → LUT (.cube) → burn-in název + TC
   → H.264/HEVC (VideoToolbox) nebo ProRes Proxy/LT do `_PROXY/`, s TC stopou pro relink. Kopie na všechny cíle.

Rozpracované soubory mají název `.<jméno>.offload-part`, nedokončená kopie tedy nikdy nevypadá jako hotová.
Na kartu se nikdy nezapisuje. Během offloadu se nespí.

## Formáty a dekodéry

| Formát | Proxy přes | Potřeba |
|---|---|---|
| ProRes, H.264/H.265, XAVC, XF-AVC, DNx, AVC-Intra (MOV/MP4/MXF) | ffmpeg | přibaleno |
| Blackmagic RAW | `braw-decode` → ffmpeg | Blackmagic RAW Player, SDK nebo DaVinci Resolve |
| RED R3D | REDline → ProRes LT → ffmpeg | REDCINE-X PRO |
| ARRIRAW, ARRIRAW HDE, ARRICORE (MXF, .ari) | art-cmd → ProRes LT (log) → ffmpeg | ARRI Reference Tool CMD – nainstalováno v `~/Library/Application Support/Offload/arri/` (odkaz v `…/Offload/bin/art-cmd`) |
| Canon Cinema RAW Light, Sony X-OCN | – | na macOS Intel neexistuje CLI dekodér; zkopíruje a ověří se, proxy se přeskočí |

S LUT se RAW dekóduje do logu (RED: IPP2 Log3G10/RWG; ARRI: podle názvu LUT – „LogC4“/„LogC3“ – jinak nativní log klipu z metadat).
ARRIRAW v MXF pozná aplikace automaticky (ffmpeg ho nečte → zeptá se art-cmd).

Testovací materiál (mimo git): `TestMedia/ARRIRAW` – ukázky ARRI (ALEXA 35 Xtreme ARRIRAW, ALEXA Mini HDE)
z veřejného FTP ARRI; `TestMedia/LUT` – oficiální ARRI LogC4 a LogC3 LUT balíčky.
Bez LUT se použije look z kamery.

## Build

```sh
scripts/build-app.sh          # → build/Offload.app (+ CLI uvnitř)
```

`Tools/bin/` (ignorováno gitem) musí obsahovat `ffmpeg` a `ffprobe` (statické x86_64 buildy z evermeet.cx);
`braw-decode` se zbuilduje automaticky z `Tools/braw-decode/` (hlavičky BRAW SDK, licence Blackmagic je v nich).

## CLI

```sh
offload /Volumes/A001 -d /Volumes/RAID_A -d /Volumes/SHUTTLE_B [--proxy --lut look.cube --proxy-codec h264]
offload verify /Volumes/RAID_A/2026-10-03/A001
offload bench --read /Volumes/A001 --write /Volumes/RAID_A --write /Volumes/SHUTTLE_B
```
