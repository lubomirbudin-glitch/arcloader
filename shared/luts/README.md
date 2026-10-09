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
- pata (toe) `TOE = 0.025`: display-lineární hodnota x → x² / (x + TOE) stáhne stíny do černé — střední tóny a světla
  se skoro nemění (nad TOE je sklon ≈ 1), expozice se zvedne tak, aby šedá zůstala na 0,42,
- od display-lineární hodnoty `KNEE = 0.5` začíná měkké dotočení světel (exponenciální rameno do 1,0),
- výstup se kóduje inverzí `GAMMA = 2.4` (BT.1886).

Pata je od 2026-10-09. Dřív výstup kódovala jen mocnina 1/2,4, která žádné stíny do černé nestáhne: šum kamery
(~6 clon pod šedou) vycházel na 7 % a dvě clony pod šedou na 24 %, takže obraz proti oficiálnímu LUTu ARRI působil
vybledle (ostré nasazení Macu: nejtmavší hodnota snímku 62 proti 23). Teď (u všech 11 LUTů stejně, hodnoty výstupu 0–1):

| Scéna | bez paty | s patou |
|---|---|---|
| černá kamery (kód z bílé knihy, např. 95/1023) | 0,000 | 0,000 |
| šum kamery, 6 clon pod šedou | 0,074 | 0,028 |
| 4 clony pod šedou | 0,132 | 0,082 |
| 2 clony pod šedou | 0,236 | 0,203 |
| 18 % šedá | 0,420 | 0,420 |
| 2 clony nad šedou | 0,748 | 0,783 |

Testy hlídají, že kód černé z bílých knih výrobců (LogC3, LogC4, S-Log3, Film Gen 5, Log3G10, C-Log2/3, V-Log, D-Log,
F-Log2) i šum kamery (6 clon pod šedou) vyjdou nejvýš na 3 % — přesně i po interpolaci kostky 33³, jak ji použije
ffmpeg (šum nejvýš 4,5 %) —, že dvě clony pod šedou zůstanou viditelná tmavá šedá (0,15–0,23) a že šedá i dotočení světel
platí dál.

## Vlastní soubor místo vestavěného LUTu

Oficiální LUTy výrobců (např. ARRI LogC4 → Rec.709) nesmí být v aplikaci přibalené, ale každý je může použít na svém
počítači: soubor `<id>.cube` (id z tabulky nahoře, např. `arri-logc4-awg4.cube`) ve složce

- Mac: `~/Library/Application Support/Offload/luts/`,
- Windows: `%APPDATA%\Offload\luts\` (s proměnnou `OFFLOAD_SETTINGS_DIR` ve složce `luts` pod ní),

nahradí vestavěný LUT se stejným id v aplikaci i v příkazové řádce. V seznamu LUTů má název „(vlastní soubor)“ a report
ho uvede u klipu v názvu i důvodu LUTu. Dostane stejný vstup jako vestavěný LUT (viz „Konvence vstupu“). Platí jen platný
3D `.cube`; jiné názvy souborů ve složce se ignorují. Aplikace soubor načte při spuštění.

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
