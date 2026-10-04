# Offload pro Windows

Port Mac aplikace Offload (viz [`../mac/`](../mac/)) na Windows 10/11 x64, .NET 10. Ve vývoji —
stav a historie v [`../docs/VYVOJ-WINDOWS.md`](../docs/VYVOJ-WINDOWS.md), rozdíly proti Macu
v [`../docs/ROZDILY.md`](../docs/ROZDILY.md).

## Build a testy

```powershell
cd windows
dotnet test
```

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

Každá změna Windows verze přidá záznam do `../docs/VYVOJ-WINDOWS.md` ve stejném commitu.
