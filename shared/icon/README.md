# Ikona mmMoveru

Ikona aplikace pro Windows i Mac (varianta E, vybraná uživatelem 2026-10-12): tři petrolejové šipky (chevrony) jedoucí
doprava, zleva průhlednější, na zaobleném čtverci s grafitovým přechodem — barvy webu mmmover.video. Názvy souborů
(`Offload.ico`, `Offload.iconset`, `Offload-1024.png`) zůstávají kvůli odkazům z projektů Windows a Mac. Všechny soubory tady jsou commitnuté a kreslí je skript `make_icon.py` (Pillow) — ikona se needituje ručně, mění se skript.

| Soubor | K čemu je |
|---|---|
| `mmMover.svg` | vektorová předloha stejné ikony (favicon webu, tisk) |
| `make_icon.py` | generátor (kreslí ve 4096 px a zmenšuje, aby hrany byly hladké) |
| `Offload-1024.png` | předloha 1024 × 1024 |
| `Offload.ico` | ikona pro Windows (16, 20, 24, 32, 40, 48, 64, 128 a 256 px) |
| `Offload.iconset/` | velikosti pro macOS (16 až 512 @1x i @2x) |

## Přegenerování

Potřeba je Python 3 a Pillow (`pip install pillow`).

```
python make_icon.py            # přepíše soubory v této složce
python make_icon.py <složka>   # zapíše je jinam
python make_icon.py --preview  # navíc preview.png: velikosti, jak je uvidí lidé, na tmavém i světlém pozadí (necommituje se)
```

Výstup je deterministický: stejný skript a stejná verze Pillow dají bajtově stejné soubory.

## Kde se používá

- **Windows** — `Offload.ico` je ikona `Offload.exe` (GUI) i `bin\offload.exe` (CLI) přes `<ApplicationIcon>` v `.csproj` a zároveň ikona oken (titulek, hlavní panel) jako prostředek WPF. Do projektů se nekopíruje, odkazuje se sem.
- **macOS** — `.icns` se vyrobí z iconsetu: `iconutil -c icns Offload.iconset` (vznikne `Offload.icns`).
