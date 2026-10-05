# Vestavěné LUTy Offloadu

Jedenáct 3D LUTů (`.cube`, 33³), které převádějí záznam v log křivce konkrétní kamery do Rec.709 (BT.1886), aby proxy a náhledy vypadaly použitelně. Soubory a `luts.json` (katalog `{id, name, maker, file, curve, gamut}`) jsou sdílené pro Mac i Windows a jsou commitnuté; generuje je `generate.py`.

## Přehled

| id | Kamera | Křivka | Gamut | Šedá 18 % (kód / 1023) |
|---|---|---|---|---|
| `arri-logc3-awg3` | ARRI | LogC3 | ARRI Wide Gamut 3 | 0,391007 |
| `arri-logc4-awg4` | ARRI | LogC4 | ARRI Wide Gamut 4 | 0,278396 |
| `sony-slog3-sgamut3cine` | Sony | S-Log3 | S-Gamut3.Cine | 0,410557 |
| `sony-slog3-sgamut3` | Sony | S-Log3 | S-Gamut3 | 0,410557 |
| `bmd-film-gen5` | Blackmagic Design | Film Gen 5 | Blackmagic Wide Gamut | 0,383562 |
| `red-log3g10-rwg` | RED | Log3G10 | REDWideGamutRGB | 0,333333 |
| `canon-clog2-cinemagamut` | Canon | Canon Log 2 | Cinema Gamut | 0,398255 |
| `canon-clog3-cinemagamut` | Canon | Canon Log 3 | Cinema Gamut | 0,343389 |
| `panasonic-vlog-vgamut` | Panasonic | V-Log | V-Gamut | 0,423311 |
| `dji-dlog-dgamut` | DJI | D-Log | DJI D-Gamut | 0,398765 |
| `fujifilm-flog2-fgamut` | Fujifilm | F-Log2 | F-Gamut | 0,391007 |

Šedé hodnoty u ARRI, Sony, RED a Panasonicu hlídají testy proti bílým knihám výrobců (tolerance 0,003).

## Konvence vstupu

LUT očekává RGB tak, jak ho dodá ffmpeg po roztažení videa v legálním rozsahu (kód = x·876 + 64, 10bitově). Zdroje v plném rozsahu (např. výstup dekodéru BRAW, `color_range=pc`) aplikace před vestavěným LUTem přepočítá; vlastní `.cube` zůstává beze změny.

## Tonální křivka

Log se dekóduje na scénově lineární hodnoty, gamut se převede do BT.709 (CAT02) a pak se použije tónování (konstanty v `generate.py`):

- 18 % šedá se zobrazí na kód výstupu `GREY_CODE = 0.42`,
- od display-lineární hodnoty `KNEE = 0.5` začíná měkké dotočení světel (exponenciální rameno do 1,0),
- výstup se kóduje inverzí `GAMMA = 2.4` (BT.1886).

## Generování a testy

Z této složky:

```powershell
python -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt
.venv\Scripts\python generate.py
.venv\Scripts\python -m unittest test_generate.py
```

Na macOS stejně, jen `.venv/bin/python`. Generování je deterministické (stejné bajty, konce řádků LF — viz `.gitattributes` v kořeni repa). Test `test_committed_files_match_generator` selže, pokud se commitnuté soubory liší od výstupu generátoru; po změně `generate.py` proto soubory znovu vygenerujte a commitněte.

## Proč ne oficiální LUTy výrobců

Oficiální LUTy mají vlastní licence, které nedovolují volné přibalení do aplikace. Tyto LUTy jsou vlastní výpočet a nejsou kopií ničeho od výrobců.

## Zdroj

Křivky a gamuty počítá [colour-science](https://www.colour-science.org/) 0.4.6 (BSD-3); implementace vychází z bílých knih výrobců.
