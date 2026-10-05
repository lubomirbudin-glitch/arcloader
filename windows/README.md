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
| RAW | R3D/ARRI: kód hotový, neověřeno se skutečnými nástroji (REDline, art-cmd nejsou nainstalované); BRAW: plán 3 |
| braw-decode | plán 3 |
| HW diagnostika USB | hotovo (CLI `offload ports`, v GUI okno Hardware a kontrola na vyžádání) |
| GUI `Offload.exe` | hotovo (čeká ruční test s kartou) |

## Build a testy

```powershell
cd windows
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\build.ps1   # → build\Offload\Offload.exe, build\Offload\bin\offload.exe, build\Offload-win-x64.zip
```

Balíček `build\Offload-win-x64.zip` obsahuje složku `Offload` (`Offload.exe` + `bin\` s `offload.exe`, `ffmpeg.exe`, `ffprobe.exe`);
stačí ho rozbalit kamkoli a spustit `Offload.exe`. Neobsahuje nic, co by se muselo instalovat.

## Aplikace (GUI)

Spuštění: `Offload\Offload.exe` (jeden soubor, .NET v něm je; první start trvá déle, protože se rozbalí nativní knihovny WPF).
Aplikace není podepsaná, takže při prvním spuštění Windows ukáže „Windows ochránil váš počítač“ —
klikněte na „Další informace“ → „Přesto spustit“.

- **Karty:** karta vložená do čtečky se objeví sama a je zaškrtnutá; složku lze přidat tlačítkem „Složka…“ nebo přetažením
  složky na `Offload.exe`. Název složky v cíli lze upravit.
- **Cíle:** tlačítkem „Přidat…“ (pamatují se; zobrazí volné místo a souborový systém, u disku exFAT/FAT32 aplikace upozorní „Doporučeno NTFS“).
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

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

## Použití

```powershell
offload E:\ -d D:\Zaloha -d F:\Zaloha          # karta E: na dva disky, ověření, report
offload verify D:\Zaloha\2026-10-04\A001        # znovu ověřit hotovou kopii
offload bench --read E:\ --write D:\ --gb 2     # rychlost karty a disků
offload ports                                 # porty, kde a jak rychle jsou disky připojené, doporučení
offload ports --measure --card E:\ --dest D:\   # + změřit skutečnou rychlost
offload E:\ -d D:\Zaloha -d F:\Zaloha --proxy --lut look.cube --frame-aspect 1.85   # + proxy s LUT a výřezem
offload E:\ -d D:\Zaloha --proxy --proxy-folder G:\Proxy   # proxy navíc do G:\Proxy\<datum>\<karta>```

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
