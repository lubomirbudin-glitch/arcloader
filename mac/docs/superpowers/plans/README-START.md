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
že `main` obsahuje složky `mac/`, `windows/` a `docs/`; jinak požádat uživatele o push nebo o název větve.

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
   co a proč, přesné texty, macOS technika (IOKit, DiskArbitration), oddíl 9 „Nejistoty API“.
3. Plán: [`2026-10-05-offload-mac-parity.md`](2026-10-05-offload-mac-parity.md) — 18 úkolů s kódem a testy.
4. Podle potřeby: [`docs/FORMATY.md`](../../../../docs/FORMATY.md) (formát na disku — nesmí se změnit),
   [`docs/VYVOJ-MAC.md`](../../../../docs/VYVOJ-MAC.md) (kam psát záznam o každé změně).

## 4. Jak plán vykonávat

- **Úkol po úkolu, v pořadí 1 → 18.** Každý úkol: napsat test → `swift test --filter …` (musí selhat nebo se
  nesestavit) → implementovat → test projde → `swift build` (a kde je uvedeno `scripts/build-app.sh`) → ruční
  kontrola, je-li v úkolu → řádek do `docs/VYVOJ-MAC.md` (případně `docs/ROZDILY.md`) → commit.
- Nesoulad s plánem (jiný název IOKit vlastnosti, jiné chování API) **neobcházet úpravou testu** — opravit kód,
  zapsat zjištění do zprávy commitu a, mění-li to chování, do návrhu.
- Úkol 14 začíná ověřením IORegistry (`ioreg`, `system_profiler`, `diskutil`) — výstupy zkráceně do commitu.
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

Výsledky (model Macu, verze macOS, čtečka, disky, naměřené rychlosti, co nesedělo) zapsat do `docs/VYVOJ-MAC.md`.
