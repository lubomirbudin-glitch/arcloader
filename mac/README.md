# ArcLoader pro Mac (zatím jako Offload.app)

Kopírování paměťových karet z kamery na více disků najednou, s ověřením, reportem a volitelnými proxy s LUT. macOS 13+.

**Jazyk:** aplikace, hlášky, CLI, poznámky u klipů i reporty (HTML/PDF/TXT) jsou od 2026-10-10 **jen anglicky**
(rozhodnutí uživatele; Windows verze zůstává česky). Termíny: [`docs/en-glossary.md`](docs/en-glossary.md) (česky → anglicky).
Tento README a vývojové dokumenty zůstávají česky.

Toto je Mac verze projektu; všechny příkazy níže se spouštějí ze složky `mac/`. Windows verze je v
[`../windows/`](../windows/), rozdíly v [`../docs/ROZDILY.md`](../docs/ROZDILY.md), historie Mac verze v
[`../docs/VYVOJ-MAC.md`](../docs/VYVOJ-MAC.md) — každá změna Mac verze tam přidá záznam ve stejném commitu.

Návrh dorovnání s Windows: [`docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md`](docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md),
plán po úkolech: [`docs/superpowers/plans/2026-10-05-offload-mac-parity.md`](docs/superpowers/plans/2026-10-05-offload-mac-parity.md)
(hotový ve verzi 0.2.0; ruční testy s hardwarem v [`README-START.md`](docs/superpowers/plans/README-START.md), oddíl 7).
Verze 0.3.0 = plán 6 dorovnání 2 ([`2026-10-08-offload-mac-parity-2.md`](docs/superpowers/plans/2026-10-08-offload-mac-parity-2.md),
úkoly 32–50): historie karet, doplnění nových klipů, název z klipů, Ověřit zálohu, kontrola místa.
Verze 0.7.0 = plán 10 ([`2026-10-08-offload-mac-parity-3.md`](docs/superpowers/plans/2026-10-08-offload-mac-parity-3.md),
úkoly 65–70, s úkoly 51 zúženým a 61): **Verify-in-Proxy**, Pozastavit, „Finish interrupted backup“, obsahová kontrola
dekodérem. Úkoly 52–60 a 62–64 zatím vynechané.

## Jak to funguje

1. **Kopie** – karta se čte **jen jednou** (velké bloky, bez page cache), každý blok jde paralelně na všechny cíle
   a zároveň do xxHash64. Rychlost = nejpomalejší článek (karta nebo nejpomalejší disk).
2. **Ověření** – po `F_FULLFSYNC` se každý soubor přečte z každého disku zpět (bez cache) a porovná se zdrojovým hashem.
   Karta má dva stavy: **Safe to eject** (vše zapsané) a **Safe to format** (vše ověřené na všech discích a výsledek zapsaný).
   **Verify-in-Proxy** (od 0.7.0, výchozí zapnuto, řádek „Verify during proxy render“ v kroku 3, CLI `--classic-verify`
   vypne): s proxy se zpětné čtení po kopii nedělá — karta je „Safe to eject“ hned po kopii, ffmpeg čte klipy primárního
   disku přes místní HTTP tap Offloadu (127.0.0.1) a každý přečtený 16MiB blok se porovná s XXH3-64 z karty, RAW (BRAW,
   R3D, ARRIRAW) se před SDK přečte jednou z disku, ostatní disky se čtou celé na pozadí, chybějící bloky dočte dorovnání;
   „Safe to format“ až po ověření všech disků a zápisu výsledku (ASC MHL, reporty, `.xxh64`). Soubor, který macOS drží
   v paměti (`mincore`), se z tapu nikdy neověří — ověří ho zpětné čtení s poznámkou v reportu. Bez proxy nebo vypnuto:
   zpětné čtení po kopii jako dřív. Vadu souboru z kamery (dekodér hlásí poškozená data, kopie přitom sedí s kartou) karta
   ukáže žlutě a před formátováním chce potvrzení „Checked — the card can be formatted“.
3. **Report** – `<root>/<YYYY-MM-DD>/<karta>/_REPORT/`: HTML (náhledy, metadata, TC), JSON, TXT, seznam xxh64;
   `ascmhl/` = ASC MHL v2 (ověřitelné `ascmhl` / Silverstack / ShotPut …).
4. **Proxy (volitelné)** – po ověření, čte se z disku (karta už není potřeba): scale → LUT (.cube) → burn-in název + TC
   → H.264/HEVC (VideoToolbox) nebo ProRes Proxy/LT do `_PROXY/<složka klipu>/<klip>.mov` (složky jako na kartě; `_2`
   jen při shodě jmen ve stejné složce, s poznámkou), pro přelinkování v Resolve/Premiere: TC stopa s cívkou
   (`reel_name`: tag kamery, jinak z názvu klipu `A001C003…` → `A001`, jinak složka karty) a všechny zvukové stopy zdroje.
   Doplnění do starší zálohy s proxy přímo v `_PROXY/` zůstane ploché (nic se nepřesouvá). Kopie na všechny cíle.

5. **Historie karet** – `~/Library/Application Support/Offload/history.json` (formát Windows): karta vložená znovu ukáže
   „Backed up on … — verified, safe to format“ a před celou kopií se aplikace zeptá („Card already backed up“: Copy again in
   full / Skip backed-up / Cancel). Na kartě s novými klipy tlačítko **Append** doplní jen nové soubory do **stejné**
   složky karty jako další generaci ASC MHL (`offload_report_0002.*`, `ascmhl/0002_…`, `.xxh64` jen prodloužený — nic
   dřívějšího se nepřepíše). Prázdné pole „Folder name“ navrhne název z klipů (`A001C003…` → `A001`) nebo další v řadě.
6. **Ověřit zálohu** – „Verify backup“ v horní liště / Tools › Verify Backup… (⇧⌘O): složka karty na cíli nebo celý den,
   proti všem generacím ASC MHL (i ze Silverstacku/ShotPutu), bez MHL proti `.xxh64`/`.md5`; report ověření do `_REPORT`.
7. **Místo** – před startem po svazcích (karta, proxy z odhadu, report, fronta): červeně blokuje, oranžově varuje.

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
offload copy /Volumes/A001 -d /Volumes/RAID_A --name auto      # název složky z klipů (A001C003… → A001)
offload copy /Volumes/A001 -d /Volumes/RAID_A --dry-run         # plán a místo po svazcích, nic se nekopíruje
offload copy … --no-history                                     # historii karet nečte ani nezapisuje
offload append /Volumes/A001 [--into /Volumes/RAID_A/2026-10-03/A001 --into …]   # jen nové soubory, generace N
                                                                # i „Finish interrupted backup“: dokončí nepotvrzenou generaci
offload copy /Volumes/A001 -d … -d … --proxy --primary 2         # ověření během proxy na 2. cíli (výchozí: nejrychlejší)
offload copy /Volumes/A001 -d … --proxy --classic-verify         # zpětné čtení po kopii místo ověření během proxy
offload verify /Volumes/RAID_A/2026-10-03/A001 [--report]       # nebo složka dne = každá karta; kód 1 = problém
offload bench --read /Volumes/A001 --write /Volumes/RAID_A --write /Volumes/SHUTTLE_B
```

## Testy

```sh
swift test                    # XCTest (Tests/OffloadKitTests); ffmpeg testy se bez ffmpeg přeskočí
OFFLOAD_TEST_DEST=/Volumes/X swift test --filter CacheProbeTests   # kontrola cache (mincore) na daném disku
OFFLOAD_TAP_BENCH=1 swift test --filter TapFFmpegTests            # měření tapu (a s OFFLOAD_TEST_DEST čtení na pozadí)
```

## Hardware (USB porty)

- Aplikace (názvy v GUI jsou anglicky, viz slovník): menu **Nástroje › Prověřit USB porty…** (⌘U) nebo tlačítko v sekci *Hardware* — kde jsou karta a disky
  připojené, rychlost linky, doporučení zapojení, volitelné měření (z karty se jen čte, na cíl se zapíše a smaže 1 GB),
  vlastní názvy portů. Při startu se nic nespouští.
- Během kopie karta úlohy ukazuje řetěz připojení s rychlostí, % linky a úzkým hrdlem.
- CLI: `offload ports [--measure] [--card /Volumes/A001] [--dest /Volumes/RAID]`; `offload copy` ukáže připojení
  a ve stavovém řádku „bottleneck: D1“.
- Omezení: zařízení 10 Gb/s běžící na 5 Gb/s se nepozná (macOS neprozradí schopnost bez BOS deskriptoru);
  fyzickou polohu portu macOS nezná — porty si pojmenujte.

## LUTy, proxy a složka pro proxy

- Proxy i náhledy v reportu dostanou LUT podle kamery každého klipu: 11 vestavěných LUTů Log → Rec.709
  (`Offload.app/Contents/Resources/luts`, sdílené s Windows ve `shared/luts`). Aplikace: *Proxy › LUT* — Automaticky podle
  kamery (+ „Když kameru nepoznám:“), vestavěný LUT, Bez LUT, Vlastní… (`.cube`). Použitý LUT a důvod je v reportu u klipu.
- CLI: `offload luts` (seznam id), `offload copy … --proxy --lut auto|none|<id>|<cesta.cube> [--lut-fallback <id>|none]`;
  bez `--lut` platí `auto`.
- Složka pro proxy navíc: *Proxy › Kopírovat proxy i do složky* nebo `--proxy-folder <složka>` — každá proxy i do
  `<složka>/<datum>/<karta>/<složka klipu>/` (stejné podsložky jako v `_PROXY`; při shodě jména `_2`, `_3`…, nic se
  nepřepíše); nedostupná složka zablokuje start.
- Proxy jsou BT.709 v legálním rozsahu a tak i označené, náhledy JPEG BT.601 (JFIF). VideoToolbox dekóduje jen H.264/HEVC;
  render proxy bez pokroku 60 s (nový snímek nebo data z dekodéru RAW; do prvního pokroku 180 s) se ukončí — po hardwarovém
  dekódování se jednou zkusí znovu softwarově, jinak proxy selže s poznámkou „ffmpeg stalled (60 s without a new frame);
  proxy not created“ (po neúspěšném opakování „…, also with software decoding; proxy not created“).
- Python k sestavení netřeba: `.cube` i ikona jsou v repu. Jen kdo mění `shared/luts/generate.py` nebo
  `shared/icon/make_icon.py`, potřebuje Python (návod v `shared/luts/README.md` a `shared/icon/README.md`).
