# Offload pro Windows

Port Mac aplikace Offload (viz [`../mac/`](../mac/), [`../mac/README.md`](../mac/README.md)) na Windows 10/11 x64, .NET 10.
Kopie karty na více disků najednou, ověření zpětným čtením (xxHash64), report a ASC MHL —
ve stejném formátu jako Mac verze, takže kopii z Windows ověří Mac a naopak.
Ve vývoji — stav a historie v [`../docs/VYVOJ-WINDOWS.md`](../docs/VYVOJ-WINDOWS.md), rozdíly proti Macu
v [`../docs/ROZDILY.md`](../docs/ROZDILY.md).

## Stav

| Část | Stav |
|---|---|
| Jádro + CLI (`bin\offload.exe`) | hotovo (plán 1) — ostrý test na skutečné kartě a měření rychlostí zatím chybí |
| Analýza klipů, 3 snímky, proxy, PDF | hotovo (plán 2) — QSV/AMF a ostrý test na médiu neověřeno |
| RAW | R3D/ARRI: kód hotový, neověřeno se skutečnými nástroji (REDline, art-cmd nejsou nainstalované); BRAW: hotovo (plán 3), čeká ověření se vzorkem .braw / kartou |
| BRAW dekodér (C#, Blackmagic RAW SDK) | hotovo (plán 3, `bin\braw-decode.exe`) — ověřeno jen s knihovnou z DaVinci Resolve, dekódování snímků a rozložení VARIANT (gama/gamut) čeká na vzorek .braw; Film Gen 4 zatím bez LUTu (provizorně); v GUI v seznamu nástrojů řádek „braw-decode“ ukazuje „nalezeno“ i bez knihovny `BlackmagicRawAPI.dll` (BRAW je pak jen zkopírovaný a ověřený, důvod je v poznámce u klipu) |
| HW diagnostika USB | hotovo (CLI `offload ports`, v GUI okno Hardware a kontrola na vyžádání) |
| GUI `Offload.exe` | hotovo (čeká ruční test s kartou) |
| Vestavěné LUTy (plán 5) | hotovo (čeká ruční test s klipy z kamer) |

## Build a testy

```powershell
cd windows
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\build.ps1   # → build\Offload\Offload.exe, build\Offload\bin\offload.exe, build\Offload-win-x64.zip
```

Balíček `build\Offload-win-x64.zip` obsahuje složku `Offload` (`Offload.exe` + `bin\` s `offload.exe`, `braw-decode.exe`, `ffmpeg.exe`, `ffprobe.exe`);
stačí ho rozbalit kamkoli a spustit `Offload.exe`. Neobsahuje nic, co by se muselo instalovat. Knihovnu `BlackmagicRawAPI.dll`
balíček neobsahuje — bez ní se BRAW jen zkopíruje a ověří, proxy a náhledy se přeskočí.

### Vzorek BRAW (testy dekodéru Blackmagic RAW)

Dekodér `bin\braw-decode.exe` potřebuje knihovnu `BlackmagicRawAPI.dll` — stačí nainstalovaný DaVinci Resolve,
nebo zdarma „Blackmagic RAW“ (Player + SDK) z https://www.blackmagicdesign.com/products/blackmagicraw.
Testy, které potřebují skutečný klip, se bez vzorku přeskočí. Vzorek:

1. Na https://www.blackmagicdesign.com/products/blackmagicraw (nebo https://www.blackmagicdesign.com/event/blackmagicrawinstaller,
   „Download RAW Samples“) stáhnout jeden ukázkový klip (zdarma, formulář se jménem a e-mailem), rozbalit.
2. Nastavit proměnnou na plnou cestu ke klipu a pustit testy:

       setx OFFLOAD_BRAW_SAMPLE "D:\Vzorky\BRAW\A001_01011200_C001.braw"
       # nové okno PowerShellu
       dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~Braw"

   (Jméno souboru je příklad; ukázkové klipy z 12K kamery mají stovky MB až jednotky GB.) Test celé cesty
   (`Offload_ProbeThumbnailsProxy`: analýza, tři náhledy, proxy 540p) potřebuje navíc ffmpeg a ffprobe.

**Ověření se vzorkem — co udělat, až klip bude** (na tomto PC zatím žádný .braw není, nic z toho neproběhlo):

1. `setx OFFLOAD_BRAW_SAMPLE "<cesta ke klipu>.braw"`, nové okno PowerShellu, ve složce `windows`:
   `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~Braw"` — očekává se vše PASS a **0 přeskočených** testů
   (přeskočený test znamená, že chybí proměnná, knihovna `BlackmagicRawAPI.dll`, ffmpeg nebo ffprobe).
2. Stále ve složce `windows`: `build\Offload\bin\braw-decode.exe --info --scale eighth $env:OFFLOAD_BRAW_SAMPLE`
   (po `scripts\build.ps1`; nebo `tools\bin\braw-decode.exe`). **Nejdřív zkontrolovat stderr:** nečekaný typ VARIANT
   u atributu se vypíše tam (např. `braw-decode: gamut: clip attribute 'gamt' is a number, expected a string`) a klíč
   v JSON pak chybí; potichu chybí klíč jen tehdy, když selže samotné volání `GetClipAttribute`. Pak očima zkontrolovat
   JSON: `gamma` (např. „Blackmagic Design Film“) a `gamut` jsou vyplněné, `colorScienceGen` je číslo (5 u Gen 5),
   `width`/`height` odpovídají zmenšení, `frames`, `fps`, `timecode`, `camera` dávají smysl. Chybí-li `gamma`/`gamut`
   s hláškou na stderr, je typ VARIANT jiný, než SDK dokumentuje — porovnat s IDL SDK; bez hlášky atribut SDK u klipu
   nevrátilo. Do opravy se LUT chová jako dosud (gama neuvedena → předpoklad Film Gen 5).
3. `offload copy <složka se vzorkem> <cíl> --proxy`: v reportu klip se **třemi snímky**, LUT „Blackmagic RAW: Blackmagic Design
   Film Gen 5“ (u klipu v Gen 5), proxy `.mov` se vizuálně shoduje s Blackmagic RAW Playerem s LUTem Film → Rec.709
   (barvy, kontrast, orientace, bez posunu o snímek; první/prostřední/poslední snímek v reportu odpovídají časům).
4. Nainstaluje-li uživatel SDK nebo Player, ověřit, že `BrawLibrary.Find` najde jejich cestu (pořadí: vedle exe, SDK, Player, Resolve);
   rovněž klip z kamery s gamou Video (bez LUTu) nebo Film Gen 4 (rozhodnout o LUTu).

Při vývoji (GUI/CLI z `bin\Debug`) se `braw-decode.exe` hledá ve `windows\tools\bin`:
`dotnet publish src\Offload.BrawDecode\Offload.BrawDecode.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:DebugType=none -o tools\bin`.

## Aplikace (GUI)

Spuštění: `Offload\Offload.exe` (jeden soubor, .NET v něm je; první start trvá déle, protože se rozbalí nativní knihovny WPF).
Aplikace není podepsaná, takže při prvním spuštění Windows ukáže „Windows ochránil váš počítač“ —
klikněte na „Další informace“ → „Přesto spustit“.

- **Karty:** karta vložená do čtečky se objeví sama a je zaškrtnutá; složku lze přidat tlačítkem „Složka…“ nebo přetažením
  složky na `Offload.exe`. Název složky v cíli lze upravit.
- **Cíle:** tlačítkem „Přidat…“ (pamatují se; zobrazí volné místo a souborový systém, u disku exFAT/FAT32 aplikace upozorní „Doporučeno NTFS“).
- **Proxy:** „Kopírovat proxy i do složky“ → „Vybrat…“: každá hotová proxy se kromě `_PROXY` na cílech zkopíruje
  i do `<složka>\<datum>\<karta>\` (nic se nepřepíše, shodné jméno dostane `_2`, `_3`…); křížek volbu zruší.
  Když složka není dostupná (odpojený disk), start nejde a v sekci Proxy je červeně „Složka pro proxy není dostupná“.
- **LUT:** v sekci Proxy seznam „Automaticky podle kamery“ (výchozí) / vestavěné LUTy / „Bez LUT“ / „Vlastní…“ (`.cube`).
  „Když kameru nepoznám:“ je záložní LUT pro klipy, které automatika nepozná (výchozí Bez LUT). Automaticky se pozná:
  Sony podle sidecaru `M01.XML` (jen S-Log3 s S-Gamut3.Cine nebo S-Gamut3, jiná gama = bez LUTu), ARRI podle modelu
  kamery (ALEXA 35 → LogC4, ostatní ALEXA/AMIRA → LogC3), ARRIRAW podle metadat, Blackmagic RAW (BRAW) podle gamy klipu (Film Gen 5 → vestavěný LUT; Film starší generace, Video, Extended Video a další gamy = bez LUTu; bez údaje o gamě se předpokládá Film Gen 5),
  RED → Log3G10. Canon, Panasonic, DJI a Fuji se zatím nepoznají (chybí vzorové klipy), D-Log M vestavěný LUT nemá —
  takové klipy dostanou záložní LUT. U ARRI jde o předpoklad pro ProRes: klip nahraný přímo v Rec.709 by LUT dostal
  neprávem, pak zvolte „Bez LUT“. Report ukáže u každého klipu použitý LUT a důvod. Na vzorcích z kamer se ještě
  ověří rozsah: R3D a ARRIRAW (přes ProRes/MXF mezisoubor) a ProRes z ALEXA 35 (LogC4) se berou jako legální video.
- **Spuštění:** tlačítko „Spustit offload“ nebo Ctrl+Enter. Každá karta má v pravém panelu kartu úlohy: průběh, rychlosti,
  odhad času, řetěz připojení (karta → řadič → disk) a „Úzké hrdlo“. Karty se kopírují za sebou, proxy předchozí karty
  se renderují souběžně s kopií další.
- **Po dokončení:** zelený pruh, „Vysunout kartu“ (karta ve čtečce se vysune jako médium, čtečka zůstane; interní disky
  a složky se nevysouvají nikdy; vysune se jen ta karta, která se kopírovala — po výměně karty ve čtečce tlačítko zmizí),
  „Report“ (HTML z prvního připojeného cíle, PDF je vedle) a „Průzkumník“. Zrušená úloha ukáže oranžové
  „Zrušeno — karta není kompletně zálohovaná.“. Upozornění: zvuk a blikání tlačítka v hlavním panelu.
- **Hardware:** kontrola USB portů se nespouští při startu, jen na vyžádání — menu „Nástroje → Prověřit USB porty…“ (Ctrl+U)
  nebo tlačítko „Prověřit…“ v sekci Hardware. Okno Hardware ukazuje připojení, doporučení, měření rychlosti
  (s potvrzením) a USB porty s vlastními názvy. Přepínač `--no-hw-prompt` se stále přijímá, ale nic nedělá.
- **Soubory:** nastavení `%APPDATA%\Offload\settings.json`, log `%APPDATA%\Offload\app.log`, poslední test HW `hwtest.json`
  (složku lze přesměrovat proměnnou `OFFLOAD_SETTINGS_DIR`).

### Checklist ručního testu (s kartou)

Vše níže zatím nikdo neověřil na skutečné kartě — GUI je jinak zkoušené jen automaticky a na snímcích.

- [ ] 1. Vložit kartu do čtečky → objeví se v „Karty“ a je zaškrtnutá
- [ ] 2. Přidat dva cíle na různých discích
- [ ] 3. Spustit (Ctrl+Enter) → průběh, rychlosti, řetěz připojení a „Úzké hrdlo“
- [ ] 4. Kartu vysunujte až po zeleném pruhu: „Vysunout kartu“ → karta zmizí ze seznamu, čtečka zůstane; vložit další
      kartu → objeví se (skutečné vysunutí je zatím nevyzkoušené — nejdůležitější bod testu)
- [ ] 5. „Report“ otevře HTML report, PDF je vedle
- [ ] 6. Během kopie zavřít okno → dotaz → „Ano“ → aplikace se zavře, v cílech nezůstanou `.offload-part`.
      Soubory `.offload-part` jsou skryté: v Průzkumníku zapněte „Zobrazit → Skryté položky“, nebo použijte `dir /a /s`
      v cílové složce. Karta, jejíž kopie se zavřením přerušila, **není zálohovaná** — offloadujte ji znovu celou.
- [ ] 7. Okno Hardware (Ctrl+U): pojmenovat port, změřit rychlost — před měřením se aplikace zeptá; z karty se jen čte,
      na každý cílový disk se zapíše a hned smaže dočasný soubor 1 GB (během kopírování měření nejde spustit)
- [ ] 8. Proxy → „Kopírovat proxy i do složky“ → „Vybrat…“ (složka na jiném disku) → offload s proxy → proxy jsou
      v `_PROXY` na cílech i v `<složka>\<datum>\<karta>\`; po odpojení toho disku je start zablokovaný červeným textem
- [ ] 9. Proxy z log klipu má přirozené barvy; v reportu je u klipu LUT a důvod
- [ ] 10. BRAW klip: proxy, tři snímky v reportu, LUT podle gamy v reportu (viz „Vzorek BRAW“ výše)

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

## Použití

```powershell
offload E:\ -d D:\Zaloha -d F:\Zaloha          # karta E: na dva disky, ověření, report
offload verify D:\Zaloha\2026-10-04\A001        # znovu ověřit hotovou kopii
offload bench --read E:\ --write D:\ --gb 2     # rychlost karty a disků
offload ports                                 # porty, kde a jak rychle jsou disky připojené, doporučení
offload ports --measure --card E:\ --dest D:\   # + změřit skutečnou rychlost
offload E:\ -d D:\Zaloha -d F:\Zaloha --proxy --lut look.cube --frame-aspect 1.85   # + proxy s LUT a výřezem
offload E:\ -d D:\Zaloha --proxy --proxy-folder G:\Proxy   # proxy navíc do G:\Proxy\<datum>\<karta>\
offload E:\ -d D:\Zaloha --proxy --lut sony-slog3-sgamut3   # vestavěný LUT podle id
offload luts                                                # seznam vestavěných LUTů (id — název)
```

`--lut auto|none|<id>|<cesta.cube>` (výchozí `auto`): `auto` vybere LUT podle kamery každého klipu, `none` žádný,
`<id>` vestavěný LUT, cesta s `.cube` nebo lomítkem vlastní LUT. Seznam id vypíše `offload luts` — samostatný příkaz,
protože `copy` vyžaduje kartu a cíl, takže `copy --lut list` by neprošlo parsováním. `--lut-fallback <id|none>` je LUT
pro klipy, které `auto` nepozná (výchozí `none`). Neznámé id skončí chybou (exit 1). Vestavěné LUTy jsou v balíčku
ve složce `luts\` vedle `Offload.exe` (11 souborů `.cube` + `luts.json`); chybějící složka nic neshodí, `auto` pak
LUT nepoužije.

`--proxy-folder <složka>` (jen s `--proxy`): každá hotová proxy se kromě `_PROXY` na cílech zkopíruje i do
`<složka>\<datum>\<karta>\` (stejný název souboru; když už tam soubor je, uloží se jako `_2`, `_3`… — nic se nepřepíše).
Neexistující složka zastaví start chybou (exit 1); když složka zmizí během úlohy (odpojený disk), proxy zůstanou na cílech
a u klipu je v reportu poznámka „kopie do … selhala“.

`offload ports` pojmenuje každý USB konektor jedním popiskem, který platí všude (řádek disku, seznam portů,
doporučení): „port 6“ (číslo USB 2 poloviny konektoru), port za hubem „port 7 › 2“, při více řadičích s řadičem
„Intel USB 3.20 xHCI · port 3“. U portu je písmeno disku, který v něm je („← E:“). Cíl na stejném svazku jako karta
se neměří (na kartu se nikdy nezapisuje); Ctrl+C při `--measure` dokončí měření aktuálního zařízení a smaže jeho
dočasný soubor.

Stavový řádek `offload copy` ukazuje, který článek brzdí („brzdí: D1“, „brzdí: card“, „(linka plná)“) — podle toho,
kolik bloků čeká na zápis na každý disk, ne podle okamžité rychlosti (karta i disky jedou v kopii stejným tempem).
Seznam „Připojení:“ nad ním ukazuje, co je co („D1 = Cíl 1: WD Elements 25A2 — USB 3 (5 Gb/s), port 6“).

Na Windows se čte i zapisuje mimo systémovou cache (`FILE_FLAG_NO_BUFFERING`), po zápisu
`FlushFileBuffers`; ověření tak čte data z disku, ne z RAM. Svazky, které to neumí (některé síťové),
přejdou na běžný zápis a report to uvede.

PDF tiskne Microsoft Edge v režimu bez okna (je ve Windows vždy). ffmpeg/ffprobe jsou v `bin\` vedle `offload.exe` (build skript je vezme z `windows\tools\bin`, jinak z PATH —
na tomto PC build gyan.dev, licence GPL; při dalším šíření je třeba přiložit licenci a zdroj ffmpeg).

## ASC MHL a `ascmhl` na Windows

Nástroj `ascmhl` 1.0.1 má příkaz `verify` jen ve skriptu `ascmhl-debug` (`python -m ascmhl` nemá vstupní bod).
Na Windows hlásí falešné „new/missing file“, protože porovnává cesty s `\` s cestami s `/` v MHL (MHL je
v tom správně). Po pouhé normalizaci oddělovačů ověřil všechny hashe složek vytvořených na Windows.
Plné `ascmhl verify` proto pouštějte na macOS/Linuxu; na Windows použijte `offload verify`.

## Ruční test (checklist)

Ostrý test na skutečné kartě:

- [ ] kopie ze skutečné karty na 2 disky
- [ ] `offload verify` na hotovou kopii
- [ ] Ctrl+C během kopie a během ověření — nezůstane žádný `.offload-part`, report říká „neověřeno“
- [ ] vytažení karty uprostřed kopie
- [ ] vytažení cílového disku uprostřed kopie
- [ ] naměřené rychlosti zapsat do „Naměřeno“

## Naměřeno

(doplnit po ostrém testu: karta, čtečka, disky, MB/s)

Každá změna Windows verze přidá záznam do `../docs/VYVOJ-WINDOWS.md` ve stejném commitu.
