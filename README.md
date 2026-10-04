# Offload

Kopírování paměťových karet z kamery na více disků najednou: karta se přečte jednou, zapíše se na všechny
cíle, každý soubor se zpětně přečte a ověří (xxHash64). Výsledkem je report (HTML, PDF, JSON, TXT, ASC MHL),
volitelně proxy s LUT.

Projekt má dvě verze, každou ve vlastní složce:

| Verze | Složka | Technologie | Stav |
|---|---|---|---|
| **Mac** | [`mac/`](mac/) | Swift, SwiftUI, macOS 13+ | 0.1.0 |
| **Windows** | [`windows/`](windows/) | C# / .NET 10, WPF, Windows 10/11 | ve vývoji |

Obě verze zapisují stejné výstupy na disk — kopii z Macu ověří Windows a naopak.

## Dokumentace

- [docs/ROZDILY.md](docs/ROZDILY.md) — co umí která verze, čím se liší, co čemu odpovídá
- [docs/FORMATY.md](docs/FORMATY.md) — společný formát na disku (struktura složek, report, `.xxh64`, ASC MHL)
- [docs/VYVOJ-MAC.md](docs/VYVOJ-MAC.md) — historie Mac verze
- [docs/VYVOJ-WINDOWS.md](docs/VYVOJ-WINDOWS.md) — historie Windows verze
- [mac/README.md](mac/README.md), [mac/docs/](mac/docs/) — popis a návod k Mac verzi
- [windows/README.md](windows/README.md) — Windows verze; návrh a plány v `windows/docs/superpowers/`

## Pravidla pro změny

- Změna jedné verze → záznam do jejího `docs/VYVOJ-*.md` ve stejném commitu.
- Rozejdou-li se tím verze (funkce, chování) → úprava `docs/ROZDILY.md`.
- Změna formátu na disku → změna v obou verzích + `docs/FORMATY.md`.
