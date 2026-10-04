# Offload pro Windows

Port Mac aplikace Offload (viz [`../mac/`](../mac/), [`../mac/README.md`](../mac/README.md)) na Windows 10/11 x64, .NET 10.
Kopie karty na více disků najednou, ověření zpětným čtením (xxHash64), report a ASC MHL —
ve stejném formátu jako Mac verze, takže kopii z Windows ověří Mac a naopak.
Ve vývoji — stav a historie v [`../docs/VYVOJ-WINDOWS.md`](../docs/VYVOJ-WINDOWS.md), rozdíly proti Macu
v [`../docs/ROZDILY.md`](../docs/ROZDILY.md).

## Stav

| Část | Stav |
|---|---|
| Jádro + CLI (`bin\offload.exe`) | hotovo (plán 1) |
| Analýza klipů, proxy, RAW, PDF | plán 2 |
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
```

Na Windows se čte i zapisuje mimo systémovou cache (`FILE_FLAG_NO_BUFFERING`), po zápisu
`FlushFileBuffers`; ověření tak čte data z disku, ne z RAM. Svazky, které to neumí (některé síťové),
přejdou na běžný zápis a report to uvede.

## Naměřeno

(doplnit po ostrém testu: karta, čtečka, disky, MB/s)

Každá změna Windows verze přidá záznam do `../docs/VYVOJ-WINDOWS.md` ve stejném commitu.
