# Jak začít na Macu — Offload pro Mac podle Windows verze

Návod pro vývojáře nebo Claude Code **na Macu bez dalšího kontextu**: stačí repo. Windows PC Swift nesestaví — všechno
v plánu se překládá, testuje a ověřuje až tady. Windows kód číst netřeba; návrh a plán říkají přesně co, jak a kde.

## 1. Předpoklady

| Co | Verze / zdroj | Proč |
|---|---|---|
| macOS | 13.5 nebo novější (Xcode 15 vyžaduje 13.5+); Apple Silicon i Intel | aplikace cílí na macOS 13+ |
| **Xcode** (plné, ne jen Command Line Tools) | 15.0+ (Swift 5.9), funguje i Xcode 16 | `swift test` potřebuje XCTest; Command Line Tools ho nemají |
| Homebrew | https://brew.sh | ffmpeg |
| **ffmpeg + ffprobe** | `brew install ffmpeg` (≥ 6; `setparams` s barvami potřebuje ≥ 4.4) | náhledy, proxy, testy (bez ffmpeg se přeskočí) |
| **Blackmagic RAW** | zdarma Blackmagic RAW Player nebo SDK (https://www.blackmagicdesign.com/products/blackmagicraw), nebo DaVinci Resolve | **sestavení** `braw-decode` SDK nepotřebuje (hlavičky jsou v `mac/Tools/braw-decode/`); **za běhu** a pro testy BRAW je potřeba `BlackmagicRawAPI.framework` z Playeru / SDK / Resolve |
| vzorek `.braw` | stránka výše → „Download RAW Samples“ (zdarma, formulář) | testy dekodéru se skutečným klipem (oddíl 6) |
| volitelně REDCINE-X PRO (REDline), ARRI Reference Tool (art-cmd) | weby RED a ARRI | R3D a ARRIRAW proxy; bez nich se klipy jen zkopírují a ověří |
| volitelně `pip install ascmhl` | Python 3 | křížové ověření MHL |
| Python 3 + `colour-science` / Pillow | **k sestavení netřeba** | jen pro přegenerování `shared/luts/*.cube` nebo ikony (`shared/luts/README.md`, `shared/icon/README.md`) |

Jednorázově:

```sh
sudo xcode-select -s /Applications/Xcode.app
sudo xcodebuild -license accept
xcodebuild -runFirstLaunch
brew install ffmpeg
```

Ověření: `swift --version` (≥ 5.9), `xcrun --find xctest` (cesta existuje), `ffmpeg -version`, `ffprobe -version`,
`ls -d "/Applications/Blackmagic RAW/Blackmagic RAW Player.app/Contents/Frameworks/BlackmagicRawAPI.framework"` (nebo SDK / Resolve).

**`swift test` spouštějte v přihlášené grafické relaci** (Terminál na Macu), ne přes SSH: PDF report a některé testy používají
`NSWindow`/WebKit, které bez okenního serveru selžou nebo visí.

## 2. Repo, build a testy

`origin/main` obsahuje `mac/`, `windows/`, `docs/` a `shared/` (od 2026-10-05). Tyto podklady (plán úkolů 27–31) musí být na
GitHubu taky — ověřte, že plán `mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md` má „Task 31“; jinak požádejte
uživatele o push nebo o název větve.

```sh
git clone https://github.com/lubomirbudin-glitch/offload.git
cd offload/mac               # všechny příkazy plánu se spouštějí odsud
swift build                  # stav 0.1.0 se musí sestavit ještě před úkolem 1
Tools/braw-decode/build.sh   # → Tools/bin/braw-decode (po každé změně braw-decode.cpp znovu)
swift test                   # po úkolu 1; do té doby „no tests found“
scripts/build-app.sh         # → build/Offload.app (CLI v Contents/Resources/bin/offload)
open build/Offload.app       # GUI; CLI bez balíčku: swift run offload --help
```

Při prvním čtení karty se macOS zeptá na přístup k vyměnitelným svazkům — povolit (jinak Offload kartu nepřečte).

`Tools/bin/` je v `.gitignore`. Pro vývoj a testy stačí Homebrew ffmpeg (najde se v `/opt/homebrew/bin`, na Intelu
`/usr/local/bin`); do balíčku pro jiný
Mac patří **statický** ffmpeg/ffprobe pro správnou architekturu v `Tools/bin/` (Homebrew build je dynamicky linkovaný —
`build-app.sh` jinak jen varuje). `build-app.sh` sestaví `braw-decode`, jen když v `Tools/bin` chybí.

## 3. Co číst

1. [`docs/ROZDILY.md`](../../../../docs/ROZDILY.md) — co Macu chybí oproti Windows (závazný seznam).
2. Návrh [`../specs/2026-10-05-offload-mac-parity-design.md`](../specs/2026-10-05-offload-mac-parity-design.md): oddíly 1–10
   (dorovnání, macOS technika, nejistoty API v oddílu 9), „Doplněk 2026-10-05“ D.1–D.12 (LUTy, složka pro proxy, barvy, hlídač
   ffmpeg, ikona) a „Doplněk 2“ D.13–D.20 (Blackmagic RAW po Windows plánu 3; přehled převzatých bodů v D.13).
3. Plán [`2026-10-05-offload-mac-parity.md`](2026-10-05-offload-mac-parity.md) — 31 úkolů s kódem a testy.
4. Podle potřeby [`docs/FORMATY.md`](../../../../docs/FORMATY.md) (formát na disku — kopii z Macu musí ověřit Windows a naopak)
   a [`docs/VYVOJ-MAC.md`](../../../../docs/VYVOJ-MAC.md) (sem řádek za každý úkol).

## 4. Pořadí úkolů a milníky

**Pořadí: 1 → 25, pak 27 → 31, nakonec 26** (26 uzavírá dokumentaci a verzi 0.2.0 a má vidět i BRAW). Po každém úkolu se
všechno sestaví a `swift test` projde — aplikace je kdykoli spustitelná; milníky říkají, kdy má smysl ji zkoušet s kartou.

| Milník | Po úkolu | Co už funguje |
|---|---|---|
| M0 | — | stávající 0.1.0: kopie, ověření, report, proxy, BRAW se starým `braw-decode` |
| M1 | 8 | testovací target; B1–B4; zrušení úloh; tři snímky na klip; české texty karty úlohy — CLI pro ostrý test |
| M2 | 11 | GUI bezpečné: vysunutí jen kopírované karty, disky mimo hlavní vlákno — **první ruční test s kartou** (oddíl 5, body 1–6) |
| M3 | 17 | HW diagnostika: `offload ports`, okno Hardware ⌘U, řetěz a „brzdí“ při kopii (oddíl 5, body 7–8) |
| M4 | 25 | barvy BT.709, hlídač ffmpeg, složka pro proxy, vestavěné LUTy v CLI i GUI, ikona (oddíl 5, body 9–12) |
| M5 | 31 | BRAW jako Windows: LUT podle gamy, BRAW jen s knihovnou, limit `--info`, české poznámky, náhledy pod hlídačem (oddíl 6) |
| M6 | 26 | verze 0.2.0, README, ROZDILY, FORMATY; závěrečné `swift build -c release && swift test && scripts/build-app.sh` |

Obsah: 1–17 dorovnání (snímky, B1–B4, fronta, texty, vysunutí, HW diagnostika přes IOKit/DiskArbitration); 18 barvy, 19 hlídač
ffmpeg, 20 složka pro proxy, 21–22 vestavěné LUTy, 23 CLI, 24 GUI, 25 balíček; 27 `braw-decode` (C++), 28 LUT podle gamy,
29 knihovna / limit / poznámky, 30 náhledy BRAW a pumpa, 31 BRAW se vzorkem a dokumentace; 26 verze 0.2.0.

## 5. Jak plán vykonávat

- Úkol po úkolu: test → `swift test --filter …` (musí selhat nebo se nesestavit) → implementace → test projde → `swift build`
  (kde je uvedeno i `scripts/build-app.sh`, `Tools/braw-decode/build.sh`) → ruční kontrola, je-li v úkolu → řádek do
  `docs/VYVOJ-MAC.md` (případně `ROZDILY.md`, `FORMATY.md`) → commit. Po každém úkolu celé `swift test`, ne jen filtr.
- Nesoulad s plánem (jiná vlastnost IOKit, jiné chování API) **neobcházet úpravou testu** — opravit kód, zjištění do zprávy
  commitu a, mění-li chování, do návrhu. Kroky „Ověřit na Macu“ (spec oddíl 9, D.10, D.19) mají výsledek zapsaný do commitu.
- Úkoly 18–22 a 28–30 mění kód starších úkolů (`Proxy.swift`, `RawBackends.swift`, `Job.swift`, `Tools.swift`) — proto přísně
  v pořadí; kde plán říká „celý soubor / celá funkce“, nahrazuje se celé.
- Claude Code: plán je psaný pro `superpowers:executing-plans` / `subagent-driven-development`. Commity: krátká anglická zpráva
  jako v plánu; trailer `Co-Authored-By` podle vlastních pravidel.

## 6. Vzorek BRAW a testy dekodéru

Testy BRAW se bez předpokladů přeskočí (`XCTSkip`), nic neselže. Co který potřebuje (plán, úvod Doplňku 2):

- **nic** — falešný `braw-decode` a „ffmpeg“ jsou shell skripty: `BrawLibraryTests`, `BrawBackendTests`, `ToolProcessTests`,
  `BrawFeederTests`, BRAW testy v `LutResolverTests`;
- **sestavený `Tools/bin/braw-decode`** — `BrawDecodeTests.testBadCommandLineIsExit2`;
- **+ knihovna** (Player / SDK / Resolve) — kód 4 s knihovnou v hlášce, UTF-8 cesta;
- **+ vzorek** — `--info` s gamou, snímky RGB48, zavřená roura; s ffmpeg i `BrawSampleTests` (analýza, LUT, 3 náhledy, proxy 540p).

Postup se vzorkem:

1. Stáhnout jeden ukázkový klip („Download RAW Samples“; stovky MB až jednotky GB), rozbalit.
2. `Tools/braw-decode/build.sh`, pak `OFFLOAD_BRAW_SAMPLE="$HOME/Vzorky/A001_….braw" swift test --filter Braw` → vše PASS
   a **0 přeskočených** (přeskočený = chybí proměnná, knihovna, `braw-decode` nebo ffmpeg).
3. `Tools/bin/braw-decode --info --scale eighth "$OFFLOAD_BRAW_SAMPLE"` — **nejdřív stderr** (varování `clip attribute '…'` =
   jiný typ hodnoty, než hlavička SDK uvádí; klíč pak v JSON chybí), pak JSON: `gamma` (např. „Blackmagic Design Film“), `gamut`,
   `colorScienceGen` (5 u Gen 5), `width`/`height` po zmenšení, `frames`, `fps`, `timecode`, `camera`.
4. `swift run offload <složka se vzorkem> -d <cíl> --proxy` → v reportu tři snímky, „LUT: Blackmagic Film Gen 5 → Rec.709
   (Blackmagic RAW: Blackmagic Design Film Gen 5)“ u klipu v Gen 5; proxy se vizuálně shoduje s Blackmagic RAW Playerem s LUTem
   Film → Rec.709 (barvy, orientace, bez posunu o snímek).
5. Je-li po ruce klip s gamou Video (má být bez LUTu) nebo z kamery Film Gen 4 (rozhodnout o LUTu, oddíl 8) — vyzkoušet taky.

## 7. Co otestovat ručně se skutečným hardwarem

Mac se čtečkou karet, karta ze záznamu (ideálně FAT32 i exFAT), dva cílové disky (aspoň jeden USB):

1. **Karta se objeví** v „Karty“ zaškrtnutá; odškrtnutá zůstane odškrtnutá i po připojení jiného disku.
2. **Kopie na 2 disky** (⌘↩): průběh, rychlosti, řetěz připojení („Karta … — USB 3 (5 Gb/s), port N …“, „Úzké hrdlo: …“),
   zelený pruh „Všechny soubory jsou na 2 discích a ověřené — kartu lze vysunout.“
3. **Report**: tři náhledy u každého klipu s TC (HTML i PDF); „Report“ otevře HTML i po odpojení prvního cíle.
4. **Vysunutí karty** až po zeleném pruhu → karta zmizí, čtečka zůstane; **jiná** karta v téže čtečce → u staré úlohy tlačítko
   není; ručně přidaná složka ho nemá nikdy.
5. **Zrušení** druhé úlohy ve frontě → „Zrušeno“, oranžový text, bez chybového zvuku. **⌘Q během kopie** → dotaz → „Ukončit“ →
   `find <cíl> -name '.*offload-part'` nic nenajde (přerušená karta není zálohovaná — offloadovat znovu celou).
6. **USB diagnostika** ⌘U: připojení a doporučení odpovídají `system_profiler SPUSBDataType`; pojmenovat port; „Změřit
   rychlost…“ a „Zastavit“ (na cíli žádný `.offload-bench-*`, ani po ⌘Q během měření); během kopie měření nejde.
7. **CLI**: `offload ports`, `offload ports --card /Volumes/<karta> --dest /Volumes/<SSD> --measure` (Ctrl+C uprostřed),
   `offload /Volumes/<karta> -d … -d …` → „brzdí: D2“ u pomalejšího disku.
8. **Křížově**: `offload verify` na kartu zkopírovanou Windows verzí a naopak; `ascmhl verify <složka karty>`.
9. **LUTy** (Sony S-Log3 s `…M01.XML`, Sony Rec.709, ARRI, BRAW): proxy a náhledy z logu vypadají jako Rec.709, Rec.709 klip bez
   LUTu; v reportu „LUT: … (důvod)“; nastavení z 0.1.0 s vlastním `.cube` zůstane „Vlastní…“.
10. **Složka pro proxy**: proxy i v `<složka>/<datum>/<karta>/`, druhý offload → `_2`; odpojený disk → červeně „Složka pro proxy
    není dostupná: …“, start neaktivní.
11. **Barvy**: proxy v QuickTime Playeru i Resolve jako originál; `ffprobe -show_streams` → `color_space=bt709`, `color_range=tv`.
12. **Ikona** v Docku, ⌘⇥ a Finderu; `Offload.app/Contents/Resources/bin/offload luts` → 11 řádků.
13. **BRAW** (karta nebo složka s `.braw`): s knihovnou proxy, tři snímky, LUT podle gamy; **bez knihovny** (Mac bez Playeru / SDK /
    Resolve, nebo dočasně přejmenovaný `BlackmagicRawAPI.framework` — po testu vrátit) klip jen zkopírovaný a ověřený
    s poznámkou „Blackmagic RAW: chybí braw-decode nebo knihovna Blackmagic RAW …“.

Výsledky (model Macu, macOS, čtečka, disky, rychlosti, co nesedělo) zapsat do `docs/VYVOJ-MAC.md`.

## 8. Otevřené otázky

- **Film Gen 4** — BRAW klip ve Film Gen 4 zatím **bez LUTu** (provizorně, jako Windows); rozhodne uživatel (spec D.15).
- **Ověří jen vzorek `.braw`** (na Windows ani na Macu zatím neproběhlo): typy hodnot `gama`/`gamt`/`csgn`, dekódování snímků,
  kód 0 po zavřené rouře se skutečným SDK, celá cesta s proxy; rychlost CPU pipeline po přesunu před `OpenClip` (spec D.19 ř. 5).
- **Ověřit s knihovnou:** `QueryInterface` továrny na Macu (D.19 ř. 1 — jinak kontrolu vypustit), cesty k frameworku u Playeru,
  SDK a Resolve (D.19 ř. 2).
- **Nejistoty API** z návrhu: oddíl 9 (IOKit, DiskArbitration, UUID svazků, SF Symbol), D.10 (VideoToolbox značky, `renamex_np`
  na exFAT, pumpa dekodéru, `iconutil`, CLI přes symlink).
- **Stejně otevřené jako Windows:** v nastavení nástrojů ukazuje řádek „braw-decode“ „nalezeno“ i bez knihovny (stav BRAW je
  vidět z poznámky u klipu); ostrý test na skutečné kartě a se skutečnými REDline / art-cmd.
