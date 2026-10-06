# Offload pro Mac

Kopírování paměťových karet z kamery na více disků najednou, s ověřením, reportem a volitelnými proxy s LUT. macOS 13+.

Toto je Mac verze projektu; všechny příkazy níže se spouštějí ze složky `mac/`. Windows verze je v
[`../windows/`](../windows/), rozdíly v [`../docs/ROZDILY.md`](../docs/ROZDILY.md), historie Mac verze v
[`../docs/VYVOJ-MAC.md`](../docs/VYVOJ-MAC.md) — každá změna Mac verze tam přidá záznam ve stejném commitu.

Návrh dorovnání s Windows: [`docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md`](docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md),
plán po úkolech: [`docs/superpowers/plans/2026-10-05-offload-mac-parity.md`](docs/superpowers/plans/2026-10-05-offload-mac-parity.md)
(hotový ve verzi 0.2.0; ruční testy s hardwarem v [`README-START.md`](docs/superpowers/plans/README-START.md), oddíl 7).

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
| Blackmagic RAW | `braw-decode` → ffmpeg (LUT podle gamy klipu) | Blackmagic RAW Player, SDK nebo DaVinci Resolve (bez knihovny se BRAW jen zkopíruje a ověří) |
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

### Vzorek BRAW (testy dekodéru Blackmagic RAW)

`Tools/braw-decode/build.sh` sestaví `Tools/bin/braw-decode` (stačí Xcode; `scripts/build-app.sh` ho sestaví jen když chybí —
po změně `braw-decode.cpp` spustit ručně). Za běhu potřebuje knihovnu `BlackmagicRawAPI.framework` — Blackmagic RAW Player
nebo SDK (zdarma, https://www.blackmagicdesign.com/products/blackmagicraw) nebo DaVinci Resolve. Testy se skutečným klipem:

```sh
OFFLOAD_BRAW_SAMPLE="$HOME/Vzorky/A001_01011200_C001.braw" swift test --filter Braw   # 0 přeskočených
```

Ukázkové klipy: stránka Blackmagic RAW → „Download RAW Samples“ (formulář, zdarma; stovky MB až jednotky GB).

## CLI

```sh
offload /Volumes/A001 -d /Volumes/RAID_A -d /Volumes/SHUTTLE_B [--proxy --lut look.cube --proxy-codec h264]
offload verify /Volumes/RAID_A/2026-10-03/A001
offload bench --read /Volumes/A001 --write /Volumes/RAID_A --write /Volumes/SHUTTLE_B
```

## Testy

```sh
swift test                    # XCTest (Tests/OffloadKitTests); ffmpeg testy se bez ffmpeg přeskočí
```

## Hardware (USB porty)

- Aplikace: menu **Nástroje › Prověřit USB porty…** (⌘U) nebo tlačítko v sekci *Hardware* — kde jsou karta a disky
  připojené, rychlost linky, doporučení zapojení, volitelné měření (z karty se jen čte, na cíl se zapíše a smaže 1 GB),
  vlastní názvy portů. Při startu se nic nespouští.
- Během kopie karta úlohy ukazuje řetěz připojení s rychlostí, % linky a úzkým hrdlem.
- CLI: `offload ports [--measure] [--card /Volumes/A001] [--dest /Volumes/RAID]`; `offload copy` ukáže připojení
  a ve stavovém řádku „brzdí: D1“.
- Omezení: zařízení 10 Gb/s běžící na 5 Gb/s se nepozná (macOS neprozradí schopnost bez BOS deskriptoru);
  fyzickou polohu portu macOS nezná — porty si pojmenujte.

## LUTy, proxy a složka pro proxy

- Proxy i náhledy v reportu dostanou LUT podle kamery každého klipu: 11 vestavěných LUTů Log → Rec.709
  (`Offload.app/Contents/Resources/luts`, sdílené s Windows ve `shared/luts`). Aplikace: *Proxy › LUT* — Automaticky podle
  kamery (+ „Když kameru nepoznám:“), vestavěný LUT, Bez LUT, Vlastní… (`.cube`). Použitý LUT a důvod je v reportu u klipu.
- CLI: `offload luts` (seznam id), `offload copy … --proxy --lut auto|none|<id>|<cesta.cube> [--lut-fallback <id>|none]`;
  bez `--lut` platí `auto`.
- Složka pro proxy navíc: *Proxy › Kopírovat proxy i do složky* nebo `--proxy-folder <složka>` — každá proxy i do
  `<složka>/<datum>/<karta>/` (při shodě jména `_2`, `_3`…, nic se nepřepíše); nedostupná složka zablokuje start.
- Proxy jsou BT.709 v legálním rozsahu a tak i označené, náhledy JPEG BT.601 (JFIF). VideoToolbox dekóduje jen H.264/HEVC;
  render proxy bez pokroku 60 s (nový snímek nebo data z dekodéru RAW; do prvního pokroku 180 s) se ukončí — po hardwarovém
  dekódování se jednou zkusí znovu softwarově, jinak proxy selže s poznámkou „ffmpeg se zasekl (60 s bez nového snímku);
  proxy nevznikla“ (po neúspěšném opakování „… i bez hardwarového dekódování; proxy nevznikla“).
- Python k sestavení netřeba: `.cube` i ikona jsou v repu. Jen kdo mění `shared/luts/generate.py` nebo
  `shared/icon/make_icon.py`, potřebuje Python (návod v `shared/luts/README.md` a `shared/icon/README.md`).
