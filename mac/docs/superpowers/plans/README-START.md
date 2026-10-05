# Jak začít na Macu — dorovnání Offload pro Mac s Windows verzí

Tahle práce se dělá **na Macu** (Windows PC Swift nesestaví). Kdo ji dělá — vývojář nebo Claude Code — nepotřebuje
znát Windows kód: vše potřebné je v návrhu a plánu níže.

## 1. Předpoklady

| Co | Verze / zdroj | Proč |
|---|---|---|
| macOS | 13.5 nebo novější (Xcode 15 vyžaduje 13.5+) | aplikace cílí na macOS 13+ |
| **Xcode** (plné, ne jen Command Line Tools) | 15.0+ (Swift 5.9), funguje i Xcode 16 | `swift test` potřebuje XCTest, který samotné Command Line Tools nemají; plné Xcode nástroje příkazové řádky obsahuje (`xcode-select --install` netřeba) |
| ffmpeg + ffprobe | **`brew install ffmpeg`** (doporučeno; statické buildy z evermeet.cx jsou jen x86_64 — na Apple Silicon jen přes Rosettu) | testy náhledů a proxy (bez ffmpeg se přeskočí), aplikace |
| Blackmagic RAW | k **sestavení** `braw-decode` není SDK potřeba (hlavičky a dispatch jsou v `mac/Tools/braw-decode/`); za **běhu** je potřeba knihovna z Blackmagic RAW Player / SDK / DaVinci Resolve | BRAW náhledy a proxy |
| volitelně | `pip install ascmhl` | křížové ověření MHL |
| Python 3 | **k sestavení není potřeba** | vestavěné LUTy (`shared/luts/*.cube`, `luts.json`) i ikona (`shared/icon/Offload.iconset`) jsou v repu commitnuté; Python (`colour-science`, resp. Pillow) potřebuje jen ten, kdo mění `shared/luts/generate.py` nebo `shared/icon/make_icon.py` a soubory přegeneruje — návod v `shared/luts/README.md` a `shared/icon/README.md` |

Jednorázové nastavení Xcode:

```sh
sudo xcode-select -s /Applications/Xcode.app
sudo xcodebuild -license accept
xcodebuild -runFirstLaunch
```

Ověření: `swift --version` (≥ 5.9), `xcrun --find xctest` (cesta existuje), `ffmpeg -version`.

**`swift test` spouštějte v přihlášené grafické relaci** (Terminál na Macu), ne přes SSH: PDF report a některé testy
používají `NSWindow`/WebKit, které bez okenního serveru selžou nebo visí.

## 2. Repo a první build

**Podmínka:** tato práce musí být nejdřív odeslaná na GitHub (větev `main` po sloučení) — bez toho klon `mac/`
neobsahuje (`origin/main` je zatím `7341e44`, tedy stav před přesunem do `mac/` a bez Windows práce). Ověřit na GitHubu,
že `main` obsahuje složky `mac/`, `windows/`, `docs/` a `shared/` (`scripts/build-app.sh` bere z `../shared` LUTy a ikonu,
testy LUTů čtou `shared/luts`); jinak požádat uživatele o push nebo o název větve.

```sh
git clone https://github.com/lubomirbudin-glitch/offload.git
cd offload
git checkout main          # nebo větev, kterou uživatel určí
git pull
cd mac
swift build
swift test                 # po úkolu 1 plánu; do té doby „no tests found“
scripts/build-app.sh       # → build/Offload.app (+ CLI v Contents/Resources/bin/offload)
```

Všechny příkazy plánu se spouštějí ze složky `mac/`.

## 3. Co číst a v jakém pořadí

1. [`docs/ROZDILY.md`](../../../../docs/ROZDILY.md) — co Macu chybí oproti Windows (závazný seznam).
2. Návrh: [`../specs/2026-10-05-offload-mac-parity-design.md`](../specs/2026-10-05-offload-mac-parity-design.md) —
   co a proč, přesné texty, macOS technika (IOKit, DiskArbitration), oddíl 9 „Nejistoty API“; na konci oddíl
   „Doplněk 2026-10-05“ (LUTy, složka pro proxy, barvy, ikona, hlídání ffmpeg) s vlastními nejistotami D.10.
3. Plán: [`2026-10-05-offload-mac-parity.md`](2026-10-05-offload-mac-parity.md) — 26 úkolů s kódem a testy:
   1–17 dorovnání (snímky, B1–B4, fronta, texty, vysunutí, HW diagnostika), **18–25 doplněk** (spec oddíl „Doplněk
   2026-10-05“): 18 barvy proxy BT.709 a náhledy JFIF, 19 ochrana proti zaseknutí ffmpeg, 20 složka pro proxy navíc,
   21 vestavěné LUTy (katalog, výběr podle kamery, nastavení), 22 LUT v proxy/náhledech/reportu, 23 CLI (`--lut`,
   `--lut-fallback`, `offload luts`, `--proxy-folder`), 24 GUI (výběr LUTu, složka pro proxy), 25 LUTy a ikona v balíčku;
   **26** dokumentace a verze 0.2.0 (dřívější úkol 18).
4. Podle potřeby: [`docs/FORMATY.md`](../../../../docs/FORMATY.md) (formát na disku — nesmí se změnit),
   [`docs/VYVOJ-MAC.md`](../../../../docs/VYVOJ-MAC.md) (kam psát záznam o každé změně).

## 4. Jak plán vykonávat

- **Úkol po úkolu, v pořadí 1 → 26.** Každý úkol: napsat test → `swift test --filter …` (musí selhat nebo se
  nesestavit) → implementovat → test projde → `swift build` (a kde je uvedeno `scripts/build-app.sh`) → ruční
  kontrola, je-li v úkolu → řádek do `docs/VYVOJ-MAC.md` (případně `docs/ROZDILY.md`) → commit.
- Nesoulad s plánem (jiný název IOKit vlastnosti, jiné chování API) **neobcházet úpravou testu** — opravit kód,
  zapsat zjištění do zprávy commitu a, mění-li to chování, do návrhu.
- Úkol 14 začíná ověřením IORegistry (`ioreg`, `system_profiler`, `diskutil`) — výstupy zkráceně do commitu.
- Úkoly 18–22 mění kód z úkolů 5–11 (`Proxy.swift`, `Job.swift`, `HTMLReport.swift`, `AppSettings.swift`) a u tří
  starších testů výslovně jeden řádek (`ThumbnailTests` v 18, `SettingsStoreTests.testRoundTrip` v 21, pomocníci
  `ColorTests` v 22) — proto přísně v pořadí; úkol 22 nahrazuje konec `Proxy.swift` a celý `RawBackends.swift`.
- Doplněk D.10 (nejistoty) ověřit v úkolech, kde se na ně plán odkazuje (VideoToolbox značky 18, `renamex_np` na exFAT 20,
  rozsah VT dekodéru 22, iconset a CLI přes symlink 25).
- Claude Code: plán je psaný pro `superpowers:executing-plans` / `subagent-driven-development`; po každém úkolu
  `swift test` celý, ne jen filtr.
- Commity: krátká anglická zpráva jako v plánu; trailer `Co-Authored-By` podle vlastních pravidel.

## 5. Co musí uživatel otestovat ručně se skutečnou kartou

Na Macu se čtečkou karet, kartou ze záznamu (ideálně FAT32/exFAT), dvěma cílovými disky (aspoň jeden USB):

1. **Karta se objeví** v „Karty“ zaškrtnutá; odškrtnutá zůstane odškrtnutá i po připojení jiného disku.
2. **Kopie na 2 disky** (⌘↩): průběh, rychlosti, pod nimi řetěz připojení („Karta … — USB 3 (5 Gb/s), port N …“,
   „Úzké hrdlo: …“), zelený pruh „Všechny soubory jsou na 2 discích a ověřené — kartu lze vysunout.“
3. **Report**: tři náhledy u každého klipu s TC pod nimi (HTML i PDF vedle); „Report“ otevře HTML i když první cíl
   mezitím odpojíte (vezme se z druhého).
4. **Vysunout kartu** až po zeleném pruhu → karta zmizí, čtečka zůstane; vložit **jinou** kartu do téže čtečky →
   u staré úlohy tlačítko není. Ručně přidaná složka tlačítko nemá nikdy.
5. **Zrušení**: druhou úlohu ve frontě zrušit → „Zrušeno“, oranžový text, žádný chybový zvuk.
6. **⌘Q během kopie** → dotaz → „Ukončit“ → aplikace se sama zavře po úklidu; v cílech
   `find <cíl> -name '.*offload-part'` nic nenajde. Přerušená karta **není zálohovaná** — offloadovat znovu celou.
7. **⌘U** (Nástroje › Prověřit USB porty…): připojení a doporučení odpovídají skutečnosti (porovnat se
   `system_profiler SPUSBDataType`); pojmenovat port; „Změřit rychlost…“ → dotaz → měření → „Zastavit“ smaže
   dočasný soubor (`ls -a` na cíli bez `.offload-bench-*`); během kopie měření nejde; **⌘Q během měření** → po
   ukončení `ls -a` na cíli také bez `.offload-bench-*`.
8. **CLI**: `offload ports`, `offload ports --card /Volumes/<karta> --dest /Volumes/<SSD> --measure` (Ctrl+C uprostřed),
   `offload /Volumes/<karta> -d … -d …` → „brzdí: D2“ ve stavovém řádku u pomalejšího disku.
9. **Křížově**: `offload verify` na kartu zkopírovanou Windows verzí a naopak; `ascmhl verify <složka karty>`.
10. **LUTy** (klipy z kamer, ideálně Sony S-Log3 s `…M01.XML`, Sony v Rec.709, ARRI, BRAW): proxy a náhledy z logu
    vypadají jako Rec.709, Rec.709 klip zůstane bez LUTu; v reportu u každého klipu „LUT: … (důvod)“. Nastavení z verze
    0.1.0 s vlastním `.cube` ukáže po aktualizaci „Vlastní…“. Seznam má 11 vestavěných LUTů a „Když kameru nepoznám:“.
11. **Složka pro proxy**: proxy i v `<složka>/<datum>/<karta>/`; druhý offload téže karty → `_2`, nic se nepřepíše;
    odpojený disk se složkou → červeně „Složka pro proxy není dostupná: …“, start neaktivní.
12. **Barvy**: proxy otevřená v QuickTime Playeru a v Resolve má stejné barvy jako originál (červená není do oranžova);
    `ffprobe -show_streams <proxy>` → `color_space=bt709`, `color_range=tv`.
13. **Ikona** Offload.app v Docku, ⌘⇥ a Finderu (karta s fajfkou); `Offload.app/Contents/Resources/bin/offload luts` → 11 řádků.

Výsledky (model Macu, verze macOS, čtečka, disky, naměřené rychlosti, co nesedělo) zapsat do `docs/VYVOJ-MAC.md`.
