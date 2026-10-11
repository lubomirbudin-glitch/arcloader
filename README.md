# ArcLoader

*(dříve Offload)*

**Bezpečné stahování kamerových karet na více disků najednou — s ověřením, reportem, ASC MHL a proxy s LUTem.**
Pro Windows a macOS. **Aplikace pro Windows, její reporty i uživatelský návod jsou v angličtině** (dokumentace repozitáře je česky;
Mac verze zatím česky, přechod na angličtinu je naplánován).

> *English summary:* ArcLoader (formerly Offload) copies camera cards to several drives at once. The card is read once and written to every
> destination. Every copy is verified by checksum (xxHash64 / XXH128, optionally MD5), and the job produces a report
> (HTML, PDF, JSON, TXT), ASC MHL v2 and, optionally, proxies with LUTs and burn-ins. It is aimed at DITs, data
> wranglers and camera assistants. The Windows (C#/.NET 10, WPF) and macOS (Swift, SwiftUI) versions write identical
> output, so either one verifies the other's backups. The Windows app, its reports and its manual are in English; the repository docs are in Czech
> (the Mac app is still Czech, switching it to English is planned). **Source available for viewing
> only — all rights reserved** (see [LICENSE](LICENSE)).

---

## Obsah

- [Stažení](#stažení)
- [Co ArcLoader dělá](#co-arcloader-dělá)
- [Jak zajišťuje bezpečnost dat](#jak-zajišťuje-bezpečnost-dat)
- [Co vznikne na disku](#co-vznikne-na-disku)
- [Podporované formáty a dekodéry](#podporované-formáty-a-dekodéry)
- [Verze a stav](#verze-a-stav)
- [Windows verze](#windows-verze)
- [Mac verze](#mac-verze)
- [Struktura repozitáře](#struktura-repozitáře)
- [Dokumentace](#dokumentace)
- [Vývoj — pravidla pro změny](#vývoj--pravidla-pro-změny)
- [Licence](#licence)

---

## Stažení

| | Windows 10/11 (x64) |
|---|---|
| **Stabilní verze** — poslední vydaná | [⬇️ ArcLoader-Windows.zip](https://github.com/lubomirbudin-glitch/arcloader/releases/download/stable/ArcLoader-Windows.zip) · [popis verze](https://github.com/lubomirbudin-glitch/arcloader/releases/tag/stable) |
| **Vývojová verze** — nejnovější build z vývoje | [⬇️ ArcLoader-Windows-dev.zip](https://github.com/lubomirbudin-glitch/arcloader/releases/download/dev/ArcLoader-Windows-dev.zip) · [popis verze](https://github.com/lubomirbudin-glitch/arcloader/releases/tag/dev) |
| Všechny verze (archiv) | [Releases](https://github.com/lubomirbudin-glitch/arcloader/releases) |

Odkazy se nemění a vždy vedou na nejnovější build daného kanálu. Rozbalte ZIP kamkoli a spusťte
`ArcLoader\ArcLoader.exe`, nic se neinstaluje. Mac verze se zatím sestavuje jen ze zdrojů (viz [Mac verze](#mac-verze)).
Staré odkazy na `Offload-Windows.zip` a `Offload-Windows-dev.zip` dál fungují (stejné soubory se nahrávají i pod starým názvem).
Hotové balíčky je dovoleno zdarma používat, viz [Licence](#licence).

## Co ArcLoader dělá

ArcLoader je nástroj pro kameraasistenty a DITy na place. Postará se o celý offload karty, od vložení do čtečky až po
okamžik, kdy je bezpečné kartu naformátovat.

- **Kopie na více disků najednou.** Karta se čte **jen jednou** velkými bloky a každý blok jde zároveň na všechny cíle
  a do výpočtu kontrolního součtu. Rychlost určuje nejpomalejší článek: karta, kabel, port nebo disk.
- **Ověření.** Každý soubor na každém disku se zpětně přečte bez cache a porovná se s kontrolním součtem ze
  zdroje. Windows verze umí i **Verify-in-Proxy**: primární disk se ověří čtením, které stejně probíhá kvůli proxy,
  a ostatní disky se ověřují na pozadí.
- **Dva stavy karty.** **"Safe to eject"** (lze vyjmout) znamená, že je všechno zkopírované. **"Safe to format"** (lze formátovat)
  znamená, že je všechno ověřené na všech discích. Až druhý stav je zelený, zazní zvuk a přijde oznámení.
- **Report** v HTML, PDF, JSON a TXT obsahuje:
  - souhrn, cíle a rychlosti;
  - **3 náhledy z každého klipu**, metadata kamery, timecode, objektiv, audio a velikost;
  - seznam všech souborů s kontrolními součty.
- **ASC MHL v2.** Zálohu ověří i nástroje třetích stran (`ascmhl`, Silverstack, ShotPut Pro).
- **Proxy (volitelné):**
  - zmenšení a LUT (vestavěný podle kamery, vlastní pravidla nebo vlastní `.cube`);
  - burn-in názvu a timecodu, frame lines;
  - kódování H.264/HEVC na grafické kartě (NVENC/QSV/AMF, VideoToolbox) nebo ProRes Proxy/LT;
  - stopa TC a reel name pro přelinkování v Resolve, Premiere a Avidu.
- **RAW:** Blackmagic RAW, RED R3D, ARRIRAW, ARRIRAW HDE a ARRICORE přes nástroje výrobců (viz níže).
- **Pro produkci a střih** (Windows):
  - denní report za celý natáčecí den;
  - seznam klipů CSV a Avid ALE;
  - MD5 navíc k xxHash.
- **Historie karet** (Windows):
  - aplikace pozná kartu, která už je zálohovaná;
  - umí **doplnit jen nové klipy** jako další generaci ASC MHL;
  - **Copy again** (zkopírovat znovu) opraví jen nesouhlasné soubory;
  - **Verify backup** (ověřit zálohu) ověří kdykoli později.
- **Hardware:**
  - test USB portů s doporučením, kam co zapojit;
  - během kopie ukazuje řetěz připojení a úzké hrdlo ("bottleneck: D1").
- **Pohodlí:**
  - automatické načtení disků a karet a fronta úloh;
  - kopie každého reportu i do počítače (`Documents\ArcLoader reports`);
  - jen ve Windows: projekty a předvolby, Pause / Resume (pozastavit / pokračovat), přesná kontrola volného místa předem a oznámení
    ve Windows i na telefon (ntfy).

Obě verze mají i **příkazovou řádku**, takže se dají použít ve skriptech. Mac (zatím pod starým názvem) umí `offload copy | verify | bench | ports | luts`,
Windows (`arcloader`) navíc `append` a `day-report`.

## Jak zajišťuje bezpečnost dat

- **Na kartu se nikdy nezapisuje.**
- Rozpracovaný soubor se jmenuje `.<jméno>.offload-part`. Konečný název dostane až po úplném zápisu a flushi, takže
  nedokončená kopie nikdy nevypadá jako hotová. Zápis jde přes `WRITE_THROUGH` (Windows) a `F_FULLFSYNC` (Mac).
- Ověřuje se **čtením z disku, ne z cache operačního systému**. Za ověřený se považuje jen soubor, jehož přečtený
  obsah prošel kontrolním součtem.
- Existující soubory na cíli se nikdy nepřepisují. Stejná karta podruhé týž den dostane `<karta>_2`. Výjimkou jsou jen
  seznamy kontrolních součtů a řetěz ASC MHL, které se při doplnění prodlužují.
- **Blokový manifest** (`offload-manifest.jsonl`) umožní po pádu nebo výpadku proudu **navázat** tam, kde kopie
  skončila.
- **Obsahová kontrola dekodérem** (Windows) najde poškozené snímky. Report je ukáže s timecodem a náhledem.
- Zaseknutý disk nebo dekodér úlohu nezablokuje: časové limity, hlídání zaseknutí a srozumitelná poznámka místo
  tichého selhání.

## Co vznikne na disku

```
<cíl>/<YYYY-MM-DD>/
    _REPORT/                       denní report a seznam klipů dne (Windows)
    <karta>/
        …soubory karty ve stejné struktuře jako na kartě…
        _REPORT/
            <karta>_report.html, .pdf
            offload_report.json, .txt
            <karta>.xxh64          (+ <karta>.md5)
            <karta>_clips.csv, .ale
            offload-manifest.jsonl
        ascmhl/                    ASC MHL v2 (generace 0001, 0002 … + ascmhl_chain.xml)
        _PROXY/                    proxy ve složkách jako na kartě
```

Přesná podoba všech souborů je ve společné specifikaci [docs/FORMATY.md](docs/FORMATY.md). Obě verze ji dodržují,
takže **kopii z Macu ověří Windows a naopak**.

## Podporované formáty a dekodéry

| Formát | Proxy a náhledy přes | Co je potřeba |
|---|---|---|
| ProRes, H.264, H.265/HEVC, XAVC, XF-AVC, DNxHD/HR, AVC-Intra (MOV, MP4, MXF) | ffmpeg | přibaleno v balíčku |
| Blackmagic RAW (`.braw`) | `braw-decode` (součást ArcLoaderu) → ffmpeg | knihovna Blackmagic RAW: [Blackmagic RAW Player / SDK](https://www.blackmagicdesign.com/products/blackmagicraw) nebo DaVinci Resolve |
| RED R3D | REDline → ProRes LT → ffmpeg | [REDCINE-X PRO](https://www.reddigitalcinema.com/downloads) |
| ARRIRAW, ARRIRAW HDE, ARRICORE (`.mxf`, `.ari`) | art-cmd → ProRes LT → ffmpeg | [ARRI Reference Tool](https://www.arri.com/en/learn/camera-systems/tools/arri-reference-tool) |
| Canon Cinema RAW Light, Sony X-OCN | — | zkopíruje se a ověří, proxy se přeskočí (dekodér pro příkazovou řádku neexistuje) |

Dekodéry RAW jsou proprietární a jejich licence nedovolují je přibalit. Když některý chybí, klipy se přesto
**zkopírují a ověří**, jen bez proxy. Aplikace pak ukáže odkaz na stažení.

**Vestavěné LUTy Log → Rec.709** pro ARRI LogC3/LogC4, Sony S-Log3 (S-Gamut3 a S-Gamut3.Cine), Blackmagic Film Gen 5,
RED Log3G10, Canon C-Log2/C-Log3, Panasonic V-Log, DJI D-Log a Fujifilm F-Log2. Aplikace je vybírá automaticky podle
metadat klipu. LUTy jsou vygenerované z publikovaných vzorců výrobců ([shared/luts/](shared/luts/)). Každý se dá
nahradit vlastním souborem.

## Verze a stav

| | Windows | Mac |
|---|---|---|
| Složka | [`windows/`](windows/) | [`mac/`](mac/) |
| Systém | Windows 10/11 x64 | macOS 13+ |
| Technologie | C# / .NET 10, WPF | Swift 5.9, SwiftUI |
| Verze | 0.7.x (0.8.0 ve vývoji) | 0.2.0C |
| Stav | funkčně napřed: historie karet, doplnění, Verify-in-Proxy, denní report, ALE, projekty, nový vzhled … | základ a opravy z prvního ostrého nasazení; dorovnání s Windows je naplánované (úkoly 32–70) |

Co přesně umí která verze, ukazuje tabulka v [docs/ROZDILY.md](docs/ROZDILY.md). Historie změn je
v [docs/VYVOJ-WINDOWS.md](docs/VYVOJ-WINDOWS.md) a [docs/VYVOJ-MAC.md](docs/VYVOJ-MAC.md).

Projekt je ve vývoji. Mac verze běží ostře na place (ALEXA 35, ARRIRAW 4.6K). Windows verze má přes 1 900
automatických testů, ale ruční testy se skutečnými kartami a nástroji výrobců ještě probíhají. Jejich seznam je
ve [windows/README.md](windows/README.md).

## Windows verze

**Požadavky:** Windows 10 nebo 11 (x64). Nic se neinstaluje, balíček je přenosný.

**Spuštění z balíčku:** stáhněte [stabilní verzi](https://github.com/lubomirbudin-glitch/arcloader/releases/download/stable/ArcLoader-Windows.zip), rozbalte ji kamkoli a spusťte `ArcLoader\ArcLoader.exe`. Anglický návod pro
uživatele je přiložený jako `ArcLoader-manual.pdf` a krátký úvod jako `READ-ME.txt`.

**Sestavení ze zdrojů** (.NET 10 SDK):

```powershell
cd windows
dotnet build Offload.sln
dotnet test tests\Offload.Core.Tests
powershell -ExecutionPolicy Bypass -File scripts\build.ps1    # → build\ArcLoader-win-x64-<verze>.zip
```

Podrobnosti o vydání verze, podepisování, kontrole aktualizací a ručních testech jsou ve
[windows/README.md](windows/README.md). Návrhy a plány jsou ve [windows/docs/superpowers/](windows/docs/superpowers/).

## Mac verze

**Požadavky:** macOS 13 nebo novější.

```sh
cd mac
scripts/build-app.sh          # → build/Offload.app (CLI uvnitř Contents/Resources/bin/offload)
swift test
```

Podrobnosti jsou v [mac/README.md](mac/README.md). Plány dorovnání s Windows jsou v
[mac/docs/superpowers/](mac/docs/superpowers/).

## Struktura repozitáře

```
docs/                 společná dokumentace obou verzí
    FORMATY.md        formát na disku — smlouva mezi Mac a Windows verzí
    ROZDILY.md        co umí která verze a čím se liší
    VYVOJ-MAC.md      historie Mac verze + naplánované úkoly
    VYVOJ-WINDOWS.md  historie Windows verze + naplánované úkoly
    ui-navrh/         návrh vzhledu (HTML makety, snímky)
shared/
    luts/             vestavěné LUTy (.cube), generátor generate.py a jeho testy
    icon/             ikona aplikace
windows/    (interní názvy projektů a jmenných prostorů — Offload.Core, Offload.App … — zůstávají staré, přejmenován je jen produkt)
    src/Offload.Core/       jádro: kopie, ověření, reporty, ASC MHL, proxy, RAW, historie …
    src/Offload.App/        GUI (WPF) — ArcLoader.exe
    src/Offload.Cli/        příkazová řádka — bin\arcloader.exe
    src/Offload.BrawDecode/ dekodér Blackmagic RAW — bin\braw-decode.exe
    tests/                  automatické testy (xUnit)
    package/                manual (anglický návod, HTML → PDF), READ-ME.txt
    scripts/                sestavení balíčku, vydání, podpis
    docs/superpowers/       návrhy (specs) a plány po úkolech
mac/
    Sources/OffloadKit/     jádro
    Sources/OffloadApp/     GUI (SwiftUI)
    Sources/offload/        příkazová řádka
    Tools/braw-decode/      dekodér Blackmagic RAW (C++)
    Tests/                  automatické testy (XCTest)
    docs/superpowers/       návrhy a plány
```

## Dokumentace

| Dokument | Obsah |
|---|---|
| [docs/FORMATY.md](docs/FORMATY.md) | struktura složek, report JSON/TXT, `.xxh64`, `.md5`, ASC MHL, manifest, denní report, seznamy klipů |
| [docs/ROZDILY.md](docs/ROZDILY.md) | porovnání funkcí Mac × Windows, technologie, záměrné rozdíly |
| [docs/VYVOJ-WINDOWS.md](docs/VYVOJ-WINDOWS.md) | co se ve Windows verzi změnilo, kdy a proč; naplánované úkoly |
| [docs/VYVOJ-MAC.md](docs/VYVOJ-MAC.md) | totéž pro Mac verzi |
| [windows/README.md](windows/README.md) | Windows: stav, sestavení, vydání, podpis, CLI, ruční testy |
| [windows/package/manual/manual.html](windows/package/manual/manual.html) | uživatelský návod, anglicky (zdroj `ArcLoader-manual.pdf` v balíčku) |
| [mac/README.md](mac/README.md) | Mac: jak to funguje, formáty, sestavení |
| [shared/luts/README.md](shared/luts/README.md) | jak vznikají vestavěné LUTy |
| [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) | komponenty třetích stran a jejich licence |

## Vývoj — pravidla pro změny

- Každá změna jedné verze přidá záznam do jejího `docs/VYVOJ-*.md` ve stejném commitu.
- Když se tím verze rozejdou ve funkci nebo chování, upraví se i `docs/ROZDILY.md`.
- Změna formátu na disku znamená změnu v obou verzích a v `docs/FORMATY.md`.
- Texty ve Windows aplikaci, v reportech a v návodu jsou anglicky (pojmy: [docs/ui-text-glossary.md](docs/ui-text-glossary.md)); Mac verze zatím česky. Na kartu se nikdy nezapisuje a existující soubory na cíli se nepřepisují.

## Licence

**© 2026 ProBudin. Všechna práva vyhrazena.**

Zdrojový kód je zveřejněný **pouze k nahlédnutí**. Bez předchozího písemného svolení autora ho není dovoleno
kopírovat, upravovat, šířit ani používat, a to včetně sestavení a spuštění. **Hotové balíčky** ze sekce
[Releases](https://github.com/lubomirbudin-glitch/arcloader/releases) je dovoleno zdarma stáhnout a používat tak, jak jsou;
nesmí se upravovat, prodávat ani šířit jako součást jiného produktu. Celé znění je v [LICENSE](LICENSE).
Komponenty třetích stran mají vlastní licence, viz [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Zájem o používání nebo spolupráci: ozvěte se přes GitHub ([lubomirbudin-glitch](https://github.com/lubomirbudin-glitch)).
