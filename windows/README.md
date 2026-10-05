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
| GUI `Offload.exe` | plán 4 |

## Build a testy

```powershell
cd windows
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\build.ps1   # → build\Offload\bin\offload.exe
```

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

## Použití

```powershell
offload E:\ -d D:\Zaloha -d F:\Zaloha          # karta E: na dva disky, ověření, report
offload verify D:\Zaloha\2026-10-04\A001        # znovu ověřit hotovou kopii
offload bench --read E:\ --write D:\ --gb 2     # rychlost karty a disků
offload E:\ -d D:\Zaloha -d F:\Zaloha --proxy --lut look.cube --frame-aspect 1.85   # + proxy s LUT a výřezem
```

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
