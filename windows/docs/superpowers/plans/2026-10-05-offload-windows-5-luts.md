# Offload — plán 5: vestavěné LUTy (Windows + sdílený generátor)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Proxy a náhledy z logových záznamů vypadají přirozeně bez hledání LUTu: 11 vestavěných LUTů Log → Rec.709 (sdílených s Macem) a automatický výběr podle metadat klipu, se zálohou ze seznamu a vlastním `.cube` jako dosud.

**Architecture:** Python generátor (`shared/luts/generate.py`, colour-science 0.4.6) spočítá `.cube` soubory a katalog `luts.json`; výsledky se commitují, aplikace Python nepotřebuje. V Core nové `Luts.cs` (katalog, `LutUse`, `LutResolver` — čistá pravidla nad metadaty + čtení Sony XML); `ProxySettings` dostane režim LUTu; renderer volí LUT po klipech; RAW dekodéry dostanou „chci log“ podle volby; report zapíše použitý LUT u klipu. CLI `--lut auto|none|<id>|<cesta>`, GUI rozbalovací seznam.

**Tech Stack:** Python 3.10 + colour-science 0.4.6 + numpy (jen generátor), C# / .NET 10, WPF, xUnit, ffmpeg.

**Spec:** `windows/docs/superpowers/specs/2026-10-05-offload-luts-design.md` (+ `2026-10-04-offload-windows-design.md` oddíl 6).

## Global Constraints

- Vestavěné LUTy jsou v `shared/luts/` (repo root), společné pro Mac i Windows; `.cube` + `luts.json` commitnuté, deterministické; `shared/luts/.venv/` v `.gitignore`.
- Konvence vstupu LUTu: RGB, jak ho dá ffmpeg po `format=rgb48le` z videa v legálním rozsahu (kód = `x·876 + 64`). Plnorozsahové zdroje (BRAW rgb48 z dekodéru, video s `color_range=pc`) se před **vestavěným** LUTem přepočítají `lutrgb` (`x = (v·1023 − 64)/876`). Vlastní `.cube` beze změny jako dosud.
- Výchozí režim „Automaticky podle kamery“; záloha výchozí „Bez LUT“. Starý soubor nastavení jen s `lutPath` = vlastní LUT (žádná změna chování pro stávající uživatele s vlastním LUTem).
- Chybějící složka `luts` nikdy nic neshodí: vestavěné LUTy nejsou v nabídce, automatika vrátí „bez LUT“ s důvodem.
- Texty česky (viz spec); názvy LUTů z `luts.json`.
- Formáty: nový volitelný klíč klipu `lut` a klíče `proxy.lutMode/lutBuiltin/lutFallback` — zapsat do `docs/FORMATY.md`; Mac je ignoruje, dokud je nedoplní (zapsat do `docs/VYVOJ-MAC.md` „Naplánováno“ a `docs/ROZDILY.md`).
- UI vlákno nikdy nečeká na disk; bez nových NuGet balíčků; `mac/` kód se nemění.
- Každý commit: řádek nahoře v nové sekci `## 2026-10-05 — plán 5 (vestavěné LUTy), průběh` v `docs/VYVOJ-WINDOWS.md` (nad sekcí „složka pro proxy“), bez SHA; poslední řádek zprávy přesně `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Klip z kamery, kterou neznáme, nebo nahraný v Rec.709 (Sony s rec709 gamou)** → žádný vestavěný LUT (jinak by vyšel přepálený/vybledlý obraz), důvod v reportu. Test: Task 3 `Sony_Rec709_NoLut`, `Unknown_UsesFallback`.
2. **Uživatel s vlastním LUTem z dřívějška** → po aktualizaci se dál používá jeho `.cube` (ne automatika). Test: Task 4 `OldSettings_LutPath_IsCustom`.
3. **BRAW (plný rozsah) a legální video dávají stejnou šedou** → přepočet rozsahu před vestavěným LUTem. Test: Task 5 `FullRangeSource_PreMapped`, `Proxy_SLog3Grey_Is042`.
4. **Chybějící `luts` složka / poškozený `luts.json`** → žádný pád, „bez LUT“ s důvodem. Test: Task 2 `Catalog_MissingOrBroken_Empty`, Task 3 `Auto_NoCatalog_NoLut`.
5. **RED/ARRIRAW s vestavěným LUTem** → dekodér vydá log (IPP2/Log3G10, AWG/LogC), ne Rec.709 (jinak dvojí převod). Test: Task 5 `Raw_LogRequested_ForBuiltinLut`.

---

### Task 1: Generátor a sdílené LUTy

**Files:**
- Create: `shared/luts/generate.py`, `shared/luts/test_generate.py`, `shared/luts/requirements.txt`, `shared/luts/README.md`, `shared/luts/.gitignore`
- Create (generated): `shared/luts/*.cube` (11), `shared/luts/luts.json`

**Interfaces:**
- Produces: soubory a `luts.json` = pole `{id, name, maker, file, curve, gamut}`; id: `arri-logc3-awg3`, `arri-logc4-awg4`, `sony-slog3-sgamut3cine`, `sony-slog3-sgamut3`, `bmd-film-gen5`, `red-log3g10-rwg`, `canon-clog2-cinemagamut`, `canon-clog3-cinemagamut`, `panasonic-vlog-vgamut`, `dji-dlog-dgamut`, `fujifilm-flog2-fgamut`.

- [ ] **Step 1: Prostředí**

```powershell
cd shared\luts
python -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt
```

`shared/luts/requirements.txt`:

```
colour-science==0.4.6
numpy>=1.24,<3
```

`shared/luts/.gitignore`:

```
.venv/
__pycache__/
```

- [ ] **Step 2: Testy (padající — generate.py ještě neexistuje)**

`shared/luts/test_generate.py`:

```python
"""Properties every built-in LUT must have. Run: .venv\\Scripts\\python -m unittest test_generate.py"""
import filecmp
import os
import tempfile
import unittest

import numpy as np

import generate as gen

# Camera code value (code / 1023) of 18 % grey, from the vendors' white papers.
REFERENCE_GREY = {
    "arri-logc3-awg3": 0.391007,
    "arri-logc4-awg4": 0.278396,
    "sony-slog3-sgamut3cine": 0.410557,
    "sony-slog3-sgamut3": 0.410557,
    "red-log3g10-rwg": 0.333333,
    "panasonic-vlog-vgamut": 0.423311,
}


def grey_input(p, linear=0.18):
    return gen.code_to_video(gen.encode(p, np.array([linear, linear, linear])))


class Properties(unittest.TestCase):
    def test_grey_is_042_and_neutral(self):
        for p in gen.PROFILES:
            out = gen.transform(p, grey_input(p))
            self.assertTrue(np.all(np.abs(out - 0.42) <= 0.01), (p["id"], out))
            self.assertLessEqual(out.max() - out.min(), 0.002, p["id"])

    def test_black_stays_black(self):
        for p in gen.PROFILES:
            out = gen.transform(p, grey_input(p, 0.0))
            self.assertLessEqual(out.max(), 0.02, (p["id"], out))

    def test_neutral_axis_is_neutral_and_monotonic(self):
        for p in gen.PROFILES:
            stops = 0.18 * 2.0 ** np.arange(-8, 6.5, 0.5)
            outs = np.array([gen.transform(p, grey_input(p, s)) for s in stops])
            self.assertTrue(np.all(outs.max(axis=1) - outs.min(axis=1) <= 0.002), p["id"])
            self.assertTrue(np.all(np.diff(outs[:, 1]) >= -1e-9), p["id"])

    def test_six_stops_over_grey_reaches_white(self):
        for p in gen.PROFILES:
            out = gen.transform(p, grey_input(p, 0.18 * 2 ** 6))
            self.assertGreaterEqual(out.min(), 0.98, (p["id"], out))

    def test_whole_cube_in_range(self):
        for p in gen.PROFILES:
            out = gen.transform(p, gen.grid())
            self.assertFalse(np.isnan(out).any(), p["id"])
            self.assertGreaterEqual(out.min(), 0.0, p["id"])
            self.assertLessEqual(out.max(), 1.0, p["id"])

    def test_grey_code_matches_white_papers(self):
        for p in gen.PROFILES:
            if p["id"] in REFERENCE_GREY:
                code = float(gen.encode(p, np.array([0.18]))[0])
                self.assertAlmostEqual(code, REFERENCE_GREY[p["id"]], delta=0.003, msg=p["id"])


class Files(unittest.TestCase):
    def test_deterministic(self):
        with tempfile.TemporaryDirectory() as a, tempfile.TemporaryDirectory() as b:
            gen.write_all(a)
            gen.write_all(b)
            for name in os.listdir(a):
                self.assertTrue(filecmp.cmp(os.path.join(a, name), os.path.join(b, name), shallow=False), name)

    def test_committed_files_match_generator(self):
        with tempfile.TemporaryDirectory() as fresh:
            gen.write_all(fresh)
            for name in os.listdir(fresh):
                committed = os.path.join(gen.HERE, name)
                self.assertTrue(os.path.exists(committed), f"{name} missing — run generate.py")
                self.assertTrue(filecmp.cmp(os.path.join(fresh, name), committed, shallow=False), f"{name} is stale")

    def test_cube_header_and_size(self):
        text = gen.cube_text(gen.PROFILES[0]).splitlines()
        self.assertIn("LUT_3D_SIZE 33", text)
        self.assertEqual(sum(1 for line in text if line and line[0].isdigit()), 33 ** 3)


if __name__ == "__main__":
    unittest.main()
```

Run: `.venv\Scripts\python -m unittest test_generate.py` → FAIL (ModuleNotFoundError: generate).

- [ ] **Step 3: Generátor**

`shared/luts/generate.py` (ověřeno prototypem 2026-10-05 — všech 9 testů prošlo):

```python
"""Offload built-in LUTs: camera log -> Rec.709 (BT.1886) with a soft highlight roll-off.

Run (from this folder):
    python -m venv .venv
    .venv\\Scripts\\python -m pip install -r requirements.txt      (macOS: .venv/bin/python …)
    .venv\\Scripts\\python generate.py
Writes the .cube files and luts.json next to this script. Deterministic: the same inputs give the same bytes.
"""
import json
import os
import sys

import numpy as np
import colour

HERE = os.path.dirname(os.path.abspath(__file__))
SIZE = 33           # LUT_3D_SIZE
GREY_CODE = 0.42    # output code value of 18 % grey
KNEE = 0.5          # display-linear value where the highlight shoulder starts
GAMMA = 2.4         # BT.1886 display
COLOUR_VERSION = "0.4.6"

# kind: "log" -> colour.log_decoding / log_encoding, "oetf" -> colour.oetf_inverse / oetf
PROFILES = [
    dict(id="arri-logc3-awg3", file="ARRI_LogC3_AWG3_to_Rec709.cube", maker="ARRI",
         name="ARRI LogC3 / AWG3 → Rec.709", kind="log", curve="ARRI LogC3", gamut="ARRI Wide Gamut 3"),
    dict(id="arri-logc4-awg4", file="ARRI_LogC4_AWG4_to_Rec709.cube", maker="ARRI",
         name="ARRI LogC4 / AWG4 → Rec.709", kind="log", curve="ARRI LogC4", gamut="ARRI Wide Gamut 4"),
    dict(id="sony-slog3-sgamut3cine", file="Sony_SLog3_SGamut3Cine_to_Rec709.cube", maker="Sony",
         name="Sony S-Log3 / S-Gamut3.Cine → Rec.709", kind="log", curve="S-Log3", gamut="S-Gamut3.Cine"),
    dict(id="sony-slog3-sgamut3", file="Sony_SLog3_SGamut3_to_Rec709.cube", maker="Sony",
         name="Sony S-Log3 / S-Gamut3 → Rec.709", kind="log", curve="S-Log3", gamut="S-Gamut3"),
    dict(id="bmd-film-gen5", file="Blackmagic_FilmGen5_WideGamutGen5_to_Rec709.cube", maker="Blackmagic Design",
         name="Blackmagic Film Gen 5 → Rec.709", kind="oetf", curve="Blackmagic Film Generation 5",
         gamut="Blackmagic Wide Gamut"),
    dict(id="red-log3g10-rwg", file="RED_Log3G10_RWG_to_Rec709.cube", maker="RED",
         name="RED Log3G10 / REDWideGamutRGB → Rec.709", kind="log", curve="Log3G10", gamut="REDWideGamutRGB"),
    dict(id="canon-clog2-cinemagamut", file="Canon_CLog2_CinemaGamut_to_Rec709.cube", maker="Canon",
         name="Canon Log 2 / Cinema Gamut → Rec.709", kind="log", curve="Canon Log 2", gamut="Cinema Gamut"),
    dict(id="canon-clog3-cinemagamut", file="Canon_CLog3_CinemaGamut_to_Rec709.cube", maker="Canon",
         name="Canon Log 3 / Cinema Gamut → Rec.709", kind="log", curve="Canon Log 3", gamut="Cinema Gamut"),
    dict(id="panasonic-vlog-vgamut", file="Panasonic_VLog_VGamut_to_Rec709.cube", maker="Panasonic",
         name="Panasonic V-Log / V-Gamut → Rec.709", kind="log", curve="V-Log", gamut="V-Gamut"),
    dict(id="dji-dlog-dgamut", file="DJI_DLog_DGamut_to_Rec709.cube", maker="DJI",
         name="DJI D-Log / D-Gamut → Rec.709", kind="log", curve="D-Log", gamut="DJI D-Gamut"),
    dict(id="fujifilm-flog2-fgamut", file="Fujifilm_FLog2_FGamut_to_Rec709.cube", maker="Fujifilm",
         name="Fujifilm F-Log2 / F-Gamut → Rec.709", kind="log", curve="F-Log2", gamut="F-Gamut"),
]


def video_to_code(x):
    """LUT input (ffmpeg RGB after expanding legal range) -> full-range normalised 10-bit code value."""
    return (np.asarray(x, dtype=np.float64) * 876.0 + 64.0) / 1023.0


def code_to_video(c):
    """Inverse of video_to_code: a camera code value (code / 1023) as the LUT sees it."""
    return (np.asarray(c, dtype=np.float64) * 1023.0 - 64.0) / 876.0


def decode(p, code):
    with np.errstate(invalid="ignore", divide="ignore"):   # piecewise curves evaluate both branches
        if p["kind"] == "log":
            return colour.log_decoding(code, function=p["curve"])
        return colour.oetf_inverse(code, function=p["curve"])


def encode(p, linear):
    with np.errstate(invalid="ignore", divide="ignore"):
        if p["kind"] == "log":
            return colour.log_encoding(linear, function=p["curve"])
        return colour.oetf(linear, function=p["curve"])


def tone(linear):
    """Scene-linear Rec.709 -> display code value: exposure, soft shoulder, BT.1886 inverse."""
    g = (GREY_CODE ** GAMMA) / 0.18
    x = np.maximum(linear, 0.0) * g
    with np.errstate(over="ignore"):
        shoulder = KNEE + (1.0 - KNEE) * (1.0 - np.exp(-(x - KNEE) / (1.0 - KNEE)))
    y = np.where(x <= KNEE, x, shoulder)
    return np.clip(y, 0.0, 1.0) ** (1.0 / GAMMA)


def transform(p, rgb):
    """LUT input RGB (…, 3) -> output RGB (…, 3)."""
    lin = decode(p, video_to_code(rgb))
    lin709 = colour.RGB_to_RGB(lin, colour.RGB_COLOURSPACES[p["gamut"]], colour.RGB_COLOURSPACES["ITU-R BT.709"],
                               chromatic_adaptation_transform="CAT02")
    return tone(lin709)


def grid():
    """Cube order: red changes fastest, then green, then blue."""
    axis = np.linspace(0.0, 1.0, SIZE)
    b, g, r = np.meshgrid(axis, axis, axis, indexing="ij")
    return np.stack([r, g, b], axis=-1).reshape(-1, 3)


def cube_text(p):
    out = transform(p, grid())
    lines = [
        f'TITLE "{p["name"]}"',
        f"# Offload built-in LUT, generated by shared/luts/generate.py (colour-science {COLOUR_VERSION})",
        f"# {p['curve']} / {p['gamut']} -> ITU-R BT.709, BT.1886; 18% grey -> {GREY_CODE}; soft shoulder from {KNEE}",
        "# Input: RGB as ffmpeg delivers it after expanding legal-range video (code = x*876 + 64)",
        f"LUT_3D_SIZE {SIZE}",
        "DOMAIN_MIN 0.0 0.0 0.0",
        "DOMAIN_MAX 1.0 1.0 1.0",
    ]
    lines += [f"{v[0]:.6f} {v[1]:.6f} {v[2]:.6f}" for v in out]
    return "\n".join(lines) + "\n"


def catalog():
    return [dict(id=p["id"], name=p["name"], maker=p["maker"], file=p["file"], curve=p["curve"], gamut=p["gamut"])
            for p in PROFILES]


def write_all(folder):
    os.makedirs(folder, exist_ok=True)
    for p in PROFILES:
        with open(os.path.join(folder, p["file"]), "w", encoding="utf-8", newline="\n") as f:
            f.write(cube_text(p))
    with open(os.path.join(folder, "luts.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(catalog(), f, ensure_ascii=False, indent=2)
        f.write("\n")


if __name__ == "__main__":
    if colour.__version__ != COLOUR_VERSION:
        sys.exit(f"colour-science {COLOUR_VERSION} required (found {colour.__version__}) — see requirements.txt")
    write_all(sys.argv[1] if len(sys.argv) > 1 else HERE)
```

- [ ] **Step 4: Vygenerovat a otestovat**

```powershell
.venv\Scripts\python generate.py
.venv\Scripts\python -m unittest test_generate.py
```

Expected: 11 `.cube` + `luts.json` v `shared\luts`; 9 testů OK, bez varování (RuntimeWarning z colour potlačené `errstate`). Do `.gitattributes` repa přidat `shared/luts/*.cube text eol=lf` a `shared/luts/luts.json text eol=lf` (determinismus bajtů po checkoutu na Windows) — ověřit, že `test_committed_files_match_generator` projde i po `git add` + nové kopii (`git stash`/`git checkout` není nutné; stačí, že soubory mají LF).

- [ ] **Step 5: README**

`shared/luts/README.md` — česky: co LUTy dělají; tabulka id → kamera, křivka, gamut, šedá (kód z bílé knihy); konvence vstupu (legální rozsah, plný rozsah se přepočítá v aplikaci); tonální křivka (konstanty z generate.py); jak generovat a testovat (příkazy výše, macOS varianta `.venv/bin/python`); proč ne oficiální LUTy (licence); zdroj: colour-science 0.4.6 (BSD-3) — implementace podle bílých knih výrobců.

- [ ] **Step 6: Commit**

Nová sekce VYVOJ `## 2026-10-05 — plán 5 (vestavěné LUTy), průběh`, řádek: `- Úkol 1 — generátor vestavěných LUTů (11 kamer Log → Rec.709, měkké dotočení světel) a sdílené soubory .cube pro Mac i Windows`.

```powershell
git add shared .gitattributes docs
git commit -m "LUTs: generator and 11 built-in Log to Rec.709 LUTs (shared)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Katalog LUTů v Core

**Files:**
- Create: `windows/src/Offload.Core/Luts.cs` (část: `BuiltinLut`, `LutCatalog`)
- Test: `windows/tests/Offload.Core.Tests/LutCatalogTests.cs`

**Interfaces:**
- Produces: `record BuiltinLut(string Id, string Name, string Maker, string File, string Curve, string Gamut)` (+ `Path` celé cesty); `LutCatalog(string? folder, IReadOnlyList<BuiltinLut> luts)` s `Folder`, `Luts`, `Find(string id) -> BuiltinLut?`; `static LutCatalog.Load(string? folder) -> LutCatalog` (nikdy nevyhodí; chybějící/poškozené → prázdný); `static LutCatalog.DefaultFolder -> string?` (exe\luts; při vývoji `shared\luts` nahoru od exe); `static LutCatalog.Default` (lazy, `Load(DefaultFolder)`); `static bool LutCatalog.IsValidCube(string path)` (hlavička `LUT_3D_SIZE n`, právě n³ řádků s čísly).

- [ ] **Step 1: Padající testy**

```csharp
using Offload.Core;

namespace Offload.Core.Tests;

public class LutCatalogTests
{
    [Fact]
    public void RepoCatalog_Has11ValidLuts()
    {
        var cat = LutCatalog.Load(LutCatalog.DefaultFolder);
        Assert.NotNull(cat.Folder);
        Assert.Equal(11, cat.Luts.Count);
        Assert.All(cat.Luts, l => Assert.True(LutCatalog.IsValidCube(l.Path), l.Id));
        Assert.Equal("Sony S-Log3 / S-Gamut3.Cine → Rec.709", cat.Find("sony-slog3-sgamut3cine")!.Name);
        Assert.Null(cat.Find("nope"));
    }

    // Review focus 4
    [Fact]
    public void Catalog_MissingOrBroken_Empty()
    {
        using var dir = new TempDir();
        Assert.Empty(LutCatalog.Load(Path.Combine(dir.Path, "missing")).Luts);
        Assert.Empty(LutCatalog.Load(null).Luts);
        File.WriteAllText(Path.Combine(dir.Path, "luts.json"), "{not json");
        Assert.Empty(LutCatalog.Load(dir.Path).Luts);
        File.WriteAllText(Path.Combine(dir.Path, "luts.json"), """[{"id":"x","name":"X","maker":"M","file":"x.cube","curve":"c","gamut":"g"}]""");
        Assert.Empty(LutCatalog.Load(dir.Path).Luts);   // listed file does not exist
    }

    [Fact]
    public void IsValidCube()
    {
        using var dir = new TempDir();
        var ok = Path.Combine(dir.Path, "ok.cube");
        File.WriteAllLines(ok, ["TITLE \"t\"", "LUT_3D_SIZE 2", "0 0 0", "1 0 0", "0 1 0", "1 1 0", "0 0 1", "1 0 1", "0 1 1", "1 1 1"]);
        Assert.True(LutCatalog.IsValidCube(ok));
        var bad = Path.Combine(dir.Path, "bad.cube");
        File.WriteAllLines(bad, ["LUT_3D_SIZE 2", "0 0 0"]);
        Assert.False(LutCatalog.IsValidCube(bad));
        Assert.False(LutCatalog.IsValidCube(Path.Combine(dir.Path, "missing.cube")));
    }
}
```

Run: `dotnet test --filter "FullyQualifiedName~LutCatalogTests"` → build FAIL.

- [ ] **Step 2: Implementace**

`windows/src/Offload.Core/Luts.cs` (první část; Task 3 přidá další typy do stejného souboru):

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Offload.Core;

/// One built-in LUT from shared/luts/luts.json.
public sealed record BuiltinLut(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("maker")] string Maker,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("curve")] string Curve,
    [property: JsonPropertyName("gamut")] string Gamut)
{
    [JsonIgnore] public string Path { get; init; } = "";
}

/// The built-in LUTs shipped next to the app (Offload\luts) or, in a development build, shared\luts in the repo.
public sealed class LutCatalog(string? folder, IReadOnlyList<BuiltinLut> luts)
{
    public string? Folder { get; } = folder;
    public IReadOnlyList<BuiltinLut> Luts { get; } = luts;

    public BuiltinLut? Find(string? id) => id == null ? null : Luts.FirstOrDefault(l => l.Id == id);

    static readonly Lazy<LutCatalog> DefaultCatalog = new(() => Load(DefaultFolder));
    public static LutCatalog Default => DefaultCatalog.Value;

    public static string? DefaultFolder
    {
        get
        {
            var exeDir = AppContext.BaseDirectory.TrimEnd('\\');
            foreach (var candidate in new[] { System.IO.Path.Combine(exeDir, "luts"), System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exeDir) ?? exeDir, "luts") })
                if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "luts.json"))) return candidate;
            if (!ExternalTools.IsDevBuild(AppContext.BaseDirectory)) return null;
            var d = exeDir;
            for (int i = 0; i < 8 && System.IO.Path.GetDirectoryName(d) is string parent; i++)
            {
                d = parent;
                var repo = System.IO.Path.Combine(d, "shared", "luts");
                if (System.IO.File.Exists(System.IO.Path.Combine(repo, "luts.json"))) return repo;
            }
            return null;
        }
    }

    /// Never throws: a missing or broken folder gives an empty catalog; entries whose file is missing are left out.
    public static LutCatalog Load(string? folder)
    {
        try
        {
            if (folder == null) return new(null, []);
            var json = System.IO.Path.Combine(folder, "luts.json");
            if (!System.IO.File.Exists(json)) return new(null, []);
            var list = JsonSerializer.Deserialize<List<BuiltinLut>>(System.IO.File.ReadAllText(json)) ?? [];
            var ok = list.Where(l => !string.IsNullOrWhiteSpace(l.Id) && !string.IsNullOrWhiteSpace(l.File))
                .Select(l => l with { Path = System.IO.Path.Combine(folder, l.File) })
                .Where(l => System.IO.File.Exists(l.Path)).ToList();
            return new(ok.Count > 0 ? folder : null, ok);
        }
        catch (Exception)
        {
            return new(null, []);
        }
    }

    public static bool IsValidCube(string path)
    {
        try
        {
            int size = 0, rows = 0;
            foreach (var raw in System.IO.File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("LUT_3D_SIZE", StringComparison.Ordinal))
                    size = int.Parse(line["LUT_3D_SIZE".Length..].Trim(), CultureInfo.InvariantCulture);
                else if (line.Length > 0 && (char.IsDigit(line[0]) || line[0] is '-' or '.')) rows++;
            }
            return size >= 2 && rows == size * size * size;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
```

- [ ] **Step 3: Testy + celá sada, Commit**

Run: `dotnet test --filter "FullyQualifiedName~LutCatalogTests"` → PASS (3). Celá sada zelená.
Řádek VYVOJ: `- Úkol 2 — katalog vestavěných LUTů (hledá Offload\luts, při vývoji shared\luts; chybějící nebo poškozený nic neshodí)`.

```powershell
git add windows docs
git commit -m "Windows: built-in LUT catalog" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Automatický výběr LUTu (pravidla)

**Files:**
- Modify: `windows/src/Offload.Core/Luts.cs` (přidat `LutMode`, `LutUse`, `SonyXml`, `LutResolver`)
- Modify: `windows/src/Offload.Core/Media.cs` (`ClipInfo.ColorRange`), `windows/src/Offload.Core/ClipDetector.cs` (číst `color_range`)
- Test: `windows/tests/Offload.Core.Tests/LutResolverTests.cs`

**Interfaces:**
- Produces: `enum LutMode { Auto, Builtin, Custom, None }` (JSON `"auto"|"builtin"|"custom"|"none"` přes `JsonStringEnumMemberName`); `sealed record LutUse(string Source, string? Id, string Name, string Reason)` s `[JsonIgnore] string? File` (cesta `.cube`, nikdy do JSON), JSON klíče `source`, `id`, `name`, `reason`; `static LutUse.None(string reason)`; `SonyXml.Read(string clipPath) -> (string? Gamma, string? Primaries)?`; `LutResolver.Resolve(Clip clip, ClipInfo? info, string clipFullPath, ProxySettings s, LutCatalog cat) -> LutUse`; `ClipInfo.ColorRange` (`"tv"|"pc"|null`, JSON `colorRange`).
- `LutUse.Source`: `"auto"`, `"builtin"`, `"custom"`, `"none"`, `"fallback"`.

- [ ] **Step 1: Padající testy**

```csharp
using Offload.Core;

namespace Offload.Core.Tests;

public class LutResolverTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    static Clip VideoClip(string path = "XDROOT/Clip/C0001.MP4") =>
        new() { Name = Path.GetFileNameWithoutExtension(path), Path = path, Format = ClipFormat.Video, Segments = [path], Bytes = 1 };

    static string SonyXml(TempDir dir, string clipRel, string gamma, string primaries)
    {
        var clip = Path.Combine(dir.Path, clipRel.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(clip)!);
        File.WriteAllText(clip, "x");
        var xml = Path.Combine(Path.GetDirectoryName(clip)!, Path.GetFileNameWithoutExtension(clip) + "M01.XML");
        File.WriteAllText(xml, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <NonRealTimeMeta xmlns="urn:schemas-professionalDisc:nonRealTimeMeta:ver.2.10">
              <AcquisitionRecord><Group name="CameraUnitMetadataSet">
                <Item name="CaptureGammaEquation" value="{gamma}"/>
                <Item name="CaptureColorPrimaries" value="{primaries}"/>
              </Group></AcquisitionRecord>
            </NonRealTimeMeta>
            """);
        return clip;
    }

    static ProxySettings Auto(string? fallback = null) => new() { Enabled = true, LutMode = LutMode.Auto, LutFallback = fallback };

    [Theory]
    [InlineData("s-log3-cine", "S-Gamut3Cine", "sony-slog3-sgamut3cine")]
    [InlineData("s-log3", "S-Gamut3", "sony-slog3-sgamut3")]
    [InlineData("s-log3-cine", "", "sony-slog3-sgamut3cine")]
    public void Sony_SLog3_FromXml(string gamma, string primaries, string id)
    {
        using var dir = new TempDir();
        var clip = SonyXml(dir, "XDROOT/Clip/C0001.MP4", gamma, primaries);
        var use = LutResolver.Resolve(VideoClip(), new ClipInfo(), clip, Auto(), Cat);
        Assert.Equal(("auto", id), (use.Source, use.Id));
        Assert.Contains(gamma, use.Reason);
        Assert.True(File.Exists(use.File));
    }

    // Review focus 1
    [Fact]
    public void Sony_Rec709_NoLut()
    {
        using var dir = new TempDir();
        var clip = SonyXml(dir, "PRIVATE/M4ROOT/CLIP/C0002.MP4", "rec709", "rec709");
        var use = LutResolver.Resolve(VideoClip("PRIVATE/M4ROOT/CLIP/C0002.MP4"), new ClipInfo(), clip, Auto("sony-slog3-sgamut3cine"), Cat);
        Assert.Equal("none", use.Source);
        Assert.Null(use.File);
        Assert.Contains("rec709", use.Reason);
    }

    [Theory]
    [InlineData("ARRI ALEXA 35", "arri-logc4-awg4")]
    [InlineData("ALEXA Mini LF", "arri-logc3-awg3")]
    [InlineData("AMIRA", "arri-logc3-awg3")]
    public void Arri_FromCameraModel(string camera, string id)
    {
        var use = LutResolver.Resolve(VideoClip("A001C001.mov"), new ClipInfo { Camera = camera }, @"Z:\none\A001C001.mov", Auto(), Cat);
        Assert.Equal(id, use.Id);
    }

    [Fact]
    public void Arriraw_UsesLogEncoding()
    {
        var clip = new Clip { Name = "A1", Path = "A1/A1.ari", Format = ClipFormat.Arriraw, Segments = ["A1/A1.ari"], Bytes = 1 };
        Assert.Equal("arri-logc4-awg4", LutResolver.Resolve(clip, new ClipInfo { LogEncoding = "LogC4" }, @"Z:\A1", Auto(), Cat).Id);
        Assert.Equal("arri-logc3-awg3", LutResolver.Resolve(clip, new ClipInfo { Camera = "ALEXA Mini" }, @"Z:\A1", Auto(), Cat).Id);
    }

    [Fact]
    public void Braw_And_R3d()
    {
        var braw = new Clip { Name = "B", Path = "B.braw", Format = ClipFormat.Braw, Segments = ["B.braw"], Bytes = 1 };
        var r3d = new Clip { Name = "R", Path = "R.RDC/R_001.R3D", Format = ClipFormat.R3d, Segments = ["R.RDC/R_001.R3D"], Bytes = 1 };
        Assert.Equal("bmd-film-gen5", LutResolver.Resolve(braw, null, @"Z:\B.braw", Auto(), Cat).Id);
        Assert.Equal("red-log3g10-rwg", LutResolver.Resolve(r3d, null, @"Z:\R", Auto(), Cat).Id);
    }

    // Review focus 1
    [Fact]
    public void Unknown_UsesFallback()
    {
        var clip = VideoClip("DCIM/100CANON/MVI_0001.MP4");
        var none = LutResolver.Resolve(clip, new ClipInfo { Camera = "Canon EOS R5" }, @"Z:\x.mp4", Auto(), Cat);
        Assert.Equal("none", none.Source);
        Assert.Equal("kamera nerozpoznána", none.Reason);
        var fb = LutResolver.Resolve(clip, new ClipInfo(), @"Z:\x.mp4", Auto("canon-clog3-cinemagamut"), Cat);
        Assert.Equal(("fallback", "canon-clog3-cinemagamut"), (fb.Source, fb.Id));
        Assert.Equal("kamera nerozpoznána — záložní LUT", fb.Reason);
    }

    [Fact]
    public void Modes_Builtin_Custom_None()
    {
        var clip = VideoClip();
        var b = LutResolver.Resolve(clip, null, @"Z:\x", new ProxySettings { LutMode = LutMode.Builtin, LutBuiltin = "dji-dlog-dgamut" }, Cat);
        Assert.Equal(("builtin", "dji-dlog-dgamut", "vybraný v nastavení"), (b.Source, b.Id, b.Reason));
        var c = LutResolver.Resolve(clip, null, @"Z:\x", new ProxySettings { LutMode = LutMode.Custom, LutPath = @"C:\luts\show.cube" }, Cat);
        Assert.Equal(("custom", null, "show.cube", @"C:\luts\show.cube"), (c.Source, c.Id, c.Name, c.File));
        var n = LutResolver.Resolve(clip, null, @"Z:\x", new ProxySettings { LutMode = LutMode.None }, Cat);
        Assert.Equal(("none", "bez LUT"), (n.Source, n.Name));
        // No mode stored (older settings / code): a LutPath means custom, otherwise automatic.
        Assert.Equal("custom", LutResolver.Resolve(clip, null, @"Z:\x", new ProxySettings { LutPath = @"C:\l.cube" }, Cat).Source);
    }

    // Review focus 4
    [Fact]
    public void Auto_NoCatalog_NoLut()
    {
        using var dir = new TempDir();
        var clip = SonyXml(dir, "XDROOT/Clip/C0001.MP4", "s-log3-cine", "S-Gamut3Cine");
        var use = LutResolver.Resolve(VideoClip(), null, clip, Auto(), LutCatalog.Load(null));
        Assert.Equal("none", use.Source);
        Assert.Equal("vestavěné LUTy chybí", use.Reason);
    }

    [Fact]
    public void ClipDetector_ReadsColorRange()
    {
        var info = FFProbe.Parse("""{"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"color_range":"pc"}],"format":{}}""");
        Assert.Equal("pc", info.ColorRange);
    }
}
```

(Pokud se třída s `Parse` jmenuje jinak než `FFProbe`, použít skutečný název z `ClipDetector.cs` — ověřit grepem `public static ClipInfo Parse`.)

Run → build FAIL.

- [ ] **Step 2: Implementace**

Do `Media.cs` (`ClipInfo`) přidat:

```csharp
    /// "tv" (legal) or "pc" (full) as ffprobe reports it; full-range sources need a pre-map before built-in LUTs.
    [JsonPropertyName("colorRange")] public string? ColorRange { get; set; }
```

V parseru ffprobe (`ClipDetector.cs`, větev videa) přidat `info.ColorRange = Str(v, "color_range");`.

Do `Luts.cs` přidat:

```csharp
[JsonConverter(typeof(JsonStringEnumConverter<LutMode>))]
public enum LutMode
{
    [JsonStringEnumMemberName("auto")] Auto,
    [JsonStringEnumMemberName("builtin")] Builtin,
    [JsonStringEnumMemberName("custom")] Custom,
    [JsonStringEnumMemberName("none")] None,
}

/// The LUT a clip got, and why — goes into the report (key "lut"). File is the .cube to apply (never serialised).
public sealed record LutUse(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("reason")] string Reason)
{
    [JsonIgnore] public string? File { get; init; }
    [JsonIgnore] public bool IsBuiltin => Id != null;

    public static LutUse None(string reason) => new("none", null, "bez LUT", reason);
}

/// Sony XAVC sidecar "<clip>M01.XML" (NonRealTimeMeta): the gamma and primaries the camera recorded in.
public static class SonyXml
{
    public static (string? Gamma, string? Primaries)? Read(string clipFullPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(clipFullPath);
            if (dir == null) return null;
            var xml = Path.Combine(dir, Path.GetFileNameWithoutExtension(clipFullPath) + "M01.XML");
            if (!System.IO.File.Exists(xml)) return null;
            var doc = System.Xml.Linq.XDocument.Load(xml);
            string? Item(string name) => doc.Descendants()
                .FirstOrDefault(e => (string?)e.Attribute("name") == name)?.Attribute("value")?.Value;
            return (Item("CaptureGammaEquation"), Item("CaptureColorPrimaries"));
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// Which LUT a clip gets (spec: luts design §3). Pure rules over metadata; never throws.
public static class LutResolver
{
    public static LutMode EffectiveMode(ProxySettings s) => s.LutMode ?? (s.LutPath != null ? LutMode.Custom : LutMode.Auto);

    public static LutUse Resolve(Clip clip, ClipInfo? info, string clipFullPath, ProxySettings s, LutCatalog cat)
    {
        switch (EffectiveMode(s))
        {
            case LutMode.None:
                return LutUse.None("vypnuto v nastavení");
            case LutMode.Custom:
                return s.LutPath is string path
                    ? new LutUse("custom", null, System.IO.Path.GetFileName(path), "vlastní LUT") { File = path }
                    : LutUse.None("vlastní LUT není vybraný");
            case LutMode.Builtin:
                return Builtin(cat, s.LutBuiltin, "builtin", "vybraný v nastavení") ?? LutUse.None("vestavěný LUT nenalezen");
        }
        if (cat.Luts.Count == 0) return LutUse.None("vestavěné LUTy chybí");
        var (id, reason) = Detect(clip, info, clipFullPath);
        if (id == "") return LutUse.None(reason);                     // recognised, but not log footage
        if (id != null) return Builtin(cat, id, "auto", reason) ?? LutUse.None("vestavěný LUT nenalezen");
        return Builtin(cat, s.LutFallback, "fallback", "kamera nerozpoznána — záložní LUT") ?? LutUse.None("kamera nerozpoznána");
    }

    static LutUse? Builtin(LutCatalog cat, string? id, string source, string reason) =>
        cat.Find(id) is { } lut ? new LutUse(source, lut.Id, lut.Name, reason) { File = lut.Path } : null;

    /// (id, reason): id null = not recognised, "" = recognised as non-log (no LUT).
    internal static (string? Id, string Reason) Detect(Clip clip, ClipInfo? info, string clipFullPath)
    {
        switch (clip.Format)
        {
            case ClipFormat.Braw: return ("bmd-film-gen5", "Blackmagic RAW (gama Film Gen 5)");
            case ClipFormat.R3d: return ("red-log3g10-rwg", "RED R3D (IPP2 Log3G10)");
            case ClipFormat.Arriraw:
                return RawBackends.ColorSpace(null, info ?? new ClipInfo()).Contains("LogC4")
                    ? ("arri-logc4-awg4", "ARRIRAW (LogC4)") : ("arri-logc3-awg3", "ARRIRAW (LogC3)");
        }
        if (SonyXml.Read(clipFullPath) is var (gamma, primaries))
        {
            var g = (gamma ?? "").ToLowerInvariant();
            var p = (primaries ?? "").ToLowerInvariant();
            if (g.StartsWith("s-log3", StringComparison.Ordinal))
            {
                bool cine = p.Contains("cine") || (p.Length == 0 && g.EndsWith("cine", StringComparison.Ordinal));
                return (cine ? "sony-slog3-sgamut3cine" : "sony-slog3-sgamut3", $"Sony XML: {gamma} / {primaries}".TrimEnd(' ', '/'));
            }
            if (g.Length > 0) return ("", $"Sony XML: {gamma} (není log)");
        }
        var camera = (info?.Camera ?? "").ToUpperInvariant();
        if (camera.Contains("ALEXA") || camera.Contains("AMIRA"))
            return camera.Contains("35") ? ("arri-logc4-awg4", "ARRI kamera (předpoklad LogC4)") : ("arri-logc3-awg3", "ARRI kamera (předpoklad LogC3)");
        return (null, "kamera nerozpoznána");
    }
}
```

(`ProxySettings.LutMode/LutBuiltin/LutFallback` přidá Task 4 — v tomto úkolu je přidat rovnou s JSON klíči podle Task 4 Step 2, aby testy šly přeložit; Task 4 pak doplní migraci, preflight a testy nastavení.)

- [ ] **Step 3: Testy + celá sada, Commit**

Run: `dotnet test --filter "FullyQualifiedName~LutResolverTests"` → PASS. Celá sada zelená.
Řádek VYVOJ: `- Úkol 3 — automatický výběr LUTu: Sony z XML vedle klipu (Rec.709 bez LUTu), ARRI podle modelu, ARRIRAW podle metadat, BRAW, RED, jinak záložní LUT`.

```powershell
git add windows docs
git commit -m "Windows: automatic LUT choice per clip" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Nastavení, kontrola před startem, report

**Files:**
- Modify: `windows/src/Offload.Core/Media.cs` (`ProxySettings` + `ClipEntry.Lut`), `Desktop/AppSettings.cs` (Normalize, ProxyPreflight), `HtmlReport.cs`
- Modify: `docs/FORMATY.md`
- Test: `SettingsStoreTests.cs`, `HtmlReportTests.cs` (přidat)

**Interfaces:**
- Produces: `ProxySettings.LutMode (LutMode?, "lutMode")`, `LutBuiltin (string?, "lutBuiltin")`, `LutFallback (string?, "lutFallback")` — všechny `JsonIgnore(WhenWritingNull)`; `ClipEntry.Lut (LutUse?, "lut", WhenWritingNull)`; `ProxyPreflight.LutInUse` jen pro režim Custom.

- [ ] **Step 1: Padající testy**

Do `SettingsStoreTests`:

```csharp
    // Review focus 2
    [Fact]
    public void OldSettings_LutPath_IsCustom()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, """{"proxy":{"enabled":true,"lutPath":"C:\\luts\\show.cube"}}""");
        Assert.Equal(LutMode.Custom, s.Proxy.LutMode);
        var fresh = LoadJson(dir, """{"proxy":{"enabled":true}}""");
        Assert.Equal(LutMode.Auto, fresh.Proxy.LutMode);
        Assert.Null(fresh.Proxy.LutFallback);
    }

    [Fact]
    public void LutMode_RoundTrip_AndUnknownBuiltinDropped()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var s = new AppSettings();
        s.Proxy.LutMode = LutMode.Builtin;
        s.Proxy.LutBuiltin = "dji-dlog-dgamut";
        s.Proxy.LutFallback = "sony-slog3-sgamut3cine";
        SettingsStore.Save(s, path);
        Assert.Contains("\"lutMode\": \"builtin\"", File.ReadAllText(path));
        var back = SettingsStore.Load(path);
        Assert.Equal((LutMode.Builtin, "dji-dlog-dgamut", "sony-slog3-sgamut3cine"), (back.Proxy.LutMode, back.Proxy.LutBuiltin, back.Proxy.LutFallback));
        var bad = LoadJson(dir, """{"proxy":{"lutMode":"builtin","lutBuiltin":"nope","lutFallback":"nope"}}""");
        Assert.Null(bad.Proxy.LutBuiltin);
        Assert.Null(bad.Proxy.LutFallback);
    }

    [Fact]
    public void ProxyPreflight_MissingLut_BlocksOnlyInCustomMode()
    {
        var auto = new ProxySettings { Enabled = true, LutMode = LutMode.Auto, LutPath = @"Z:\gone.cube" };
        Assert.Null(ProxyPreflight.LutInUse(auto, analyzeClips: true));
        var custom = new ProxySettings { Enabled = true, LutMode = LutMode.Custom, LutPath = @"Z:\gone.cube" };
        Assert.Equal(@"Z:\gone.cube", ProxyPreflight.LutInUse(custom, analyzeClips: true));
    }
```

(`Normalize` zahazuje neznámá id podle `LutCatalog.Default` — test spoléhá na katalog z repa, který je při běhu testů dostupný.)

Do `HtmlReportTests` (nebo nový test u reportu):

```csharp
    [Fact]
    public void Clip_ShowsLut_AndJsonKey()
    {
        var report = Reports.Sample();   // existing helper in the tests — use whatever builds a minimal report there
        report.Clips = [new ClipEntry
        {
            Clip = new Clip { Name = "C0001", Path = "C0001.MP4", Format = ClipFormat.Video, Segments = ["C0001.MP4"], Bytes = 1 },
            Lut = new LutUse("auto", "sony-slog3-sgamut3cine", "Sony S-Log3 / S-Gamut3.Cine → Rec.709", "Sony XML: s-log3-cine / S-Gamut3Cine"),
        }];
        var html = HtmlReport.Render(report);    // existing entry point
        Assert.Contains("LUT: Sony S-Log3 / S-Gamut3.Cine → Rec.709 (Sony XML: s-log3-cine / S-Gamut3Cine)", html);
        var json = report.ToJson();              // existing JSON writer
        Assert.Contains("\"lut\"", json);
        Assert.Contains("\"source\": \"auto\"", json);
    }
```

(Názvy pomocníků `Reports.Sample`, `HtmlReport.Render`, `ToJson` přizpůsobit existujícímu kódu testů reportu — najít v `HtmlReportTests.cs`/`ReportTests.cs`; tvrzení zůstávají.)

Run → FAIL.

- [ ] **Step 2: Implementace**

`ProxySettings` (Media.cs):

```csharp
    /// Which LUT proxies and thumbnails get (null in older files: a lutPath means custom, otherwise auto).
    [JsonPropertyName("lutMode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LutMode? LutMode { get; set; }
    /// Built-in LUT id for mode "builtin".
    [JsonPropertyName("lutBuiltin"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LutBuiltin { get; set; }
    /// Built-in LUT id used in mode "auto" when the camera is not recognised; null = no LUT.
    [JsonPropertyName("lutFallback"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LutFallback { get; set; }
```

`ClipEntry` (Media.cs):

```csharp
    /// The LUT applied to this clip's proxy and thumbnails, and why.
    [JsonPropertyName("lut"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LutUse? Lut { get; set; }
```

`SettingsStore.Normalize` (za řádek s `LutPath`):

```csharp
        p.LutMode ??= p.LutPath != null ? LutMode.Custom : LutMode.Auto;
        if (LutCatalog.Default.Find(p.LutBuiltin) == null) p.LutBuiltin = null;
        if (LutCatalog.Default.Find(p.LutFallback) == null) p.LutFallback = null;
        if (p.LutMode == LutMode.Builtin && p.LutBuiltin == null) p.LutMode = LutMode.Auto;
```

`ProxyPreflight.LutInUse`:

```csharp
    /// The custom .cube that must exist before a start (only in custom mode — built-in LUTs ship with the app).
    public static string? LutInUse(ProxySettings p, bool analyzeClips) =>
        (p.Enabled || analyzeClips) && LutResolver.EffectiveMode(p) == LutMode.Custom ? p.LutPath : null;
```

`HtmlReport`: ve sloupci klipu (za `noteHtml`) přidat řádek `<div class="note">LUT: {Esc(c.Lut.Name)} ({Esc(c.Lut.Reason)})</div>`, jen když `c.Lut is { Source: not "none" }`; u `none` s důvodem jiným než „vypnuto v nastavení“ `LUT: bez LUT ({reason})`. Souhrnný řádek proxy: místo `LUT: {soubor}` → podle režimu: `LUT: automaticky podle kamery` / název vestavěného / soubor vlastního / `bez LUT`.

`docs/FORMATY.md`: klíče `proxy.lutMode`, `proxy.lutBuiltin`, `proxy.lutFallback` a klip `lut` = `{source, id, name, reason}` (Windows od plánu 5; Mac je zatím ignoruje).

- [ ] **Step 3: Testy + celá sada, Commit**

Řádek VYVOJ: `- Úkol 4 — režim LUTu v nastavení (starý vlastní LUT zůstává), kontrola chybějícího LUTu jen pro vlastní, použitý LUT a důvod u klipu v reportu`.

```powershell
git add windows docs
git commit -m "Windows: LUT mode in settings and report" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Renderování s LUTem po klipech

**Files:**
- Modify: `windows/src/Offload.Core/FilterGraph.cs`, `ProxyRenderer.cs`, `FrameSource.cs`, `RawBackends.cs`, `OffloadJob.cs`
- Test: `ProxyRendererTests.cs`, `RawBackendsTests.cs`, `StageTwoTests.cs` (upravit volání + nové testy)

**Interfaces:**
- Consumes: `LutResolver.Resolve`, `LutUse`, `LutCatalog.Default`.
- Produces:
  - `FilterGraph.Thumbnail(string? lutFile, bool premap)`, `FilterGraph.Proxy(ProxySettings s, int outHeight, bool alreadyCropped, string? lutFile, bool premap, bool hasFont, string? labelFile, string? timecode, string? rate)`; `FilterGraph.PreMap` (konstanta s `lutrgb`).
  - `FrameSource.FullRange` (bool, default false; BRAW feeder = true).
  - `record RawLook(bool Log, string? ArriColorSpace)`; `IRawBackend.Source(string path, ClipInfo info, ProxySettings settings, RawLook look, ExternalTools tools, string workDir, double? singleFrameAt)`; `R3dBackend.Args(…, RawLook look)` místo `s.LutPath`; `ArriBackend.Args(…, RawLook look, …)`.
  - `ProxyRenderer.Thumbnails(Clip, string cardPath, ClipInfo, LutUse? lut, Func<bool>? isCancelled)`; `ProxyRenderer.Render(Clip, string cardPath, ClipInfo, LutUse? lut, string output, Action<double>, Func<bool>)`; `ProxyRenderer.LookFor(LutUse? lut, ClipInfo info) -> RawLook`.

- [ ] **Step 1: Padající testy**

`ProxyRendererTests` (nové):

```csharp
    // Review focus 3
    [Fact]
    public void FullRangeSource_PreMapped()
    {
        Assert.Equal("scale=-2:180:flags=bicubic,format=rgb48le," + FilterGraph.PreMap + ",lut3d=file=lut-a.cube:interp=tetrahedral,format=yuvj420p",
            FilterGraph.Thumbnail("lut-a.cube", premap: true));
        Assert.Equal("scale=-2:180:flags=bicubic,format=rgb48le,lut3d=file=look.cube:interp=tetrahedral,format=yuvj420p",
            FilterGraph.Thumbnail("look.cube", premap: false));
        Assert.Equal("scale=-2:180:flags=bicubic,format=yuvj420p", FilterGraph.Thumbnail(null, premap: true));
        Assert.Contains("(val*1023/65535-64)/876*65535", FilterGraph.PreMap);
    }

    // Review focus 3 — real ffmpeg: an S-Log3 grey clip with its Sony XML renders a proxy whose grey is ~0.42.
    [FfmpegFact]
    public void Proxy_SLog3Grey_Is042()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var clipDir = Path.Combine(card, "XDROOT", "Clip");
        Directory.CreateDirectory(clipDir);
        var clipPath = Path.Combine(clipDir, "C0001.MP4");
        // S-Log3 grey = 10-bit code 420 → 8-bit legal Y ≈ 105; neutral chroma.
        Ffmpeg.Run(["-f", "lavfi", "-i", "color=c=0x696969:s=320x180:d=1:r=25", "-vf", "format=yuv420p,lutyuv=y=105:u=128:v=128",
            "-c:v", "libx264", "-color_range", "tv", clipPath]);
        File.WriteAllText(Path.Combine(clipDir, "C0001M01.XML"),
            """<NonRealTimeMeta><Item name="CaptureGammaEquation" value="s-log3-cine"/><Item name="CaptureColorPrimaries" value="S-Gamut3Cine"/></NonRealTimeMeta>""");
        var settings = new ProxySettings { Enabled = true, Height = 180, LutMode = LutMode.Auto, BurnFilename = false, BurnTimecode = false };
        using var r = new ProxyRenderer(settings, Tools());
        var clip = new Clip { Name = "C0001", Path = "XDROOT/Clip/C0001.MP4", Format = ClipFormat.Video, Segments = ["XDROOT/Clip/C0001.MP4"], Bytes = 1 };
        var info = r.Probe(clip, card);
        var lut = LutResolver.Resolve(clip, info, clipPath, settings, LutCatalog.Default);
        Assert.Equal("sony-slog3-sgamut3cine", lut.Id);
        var outPath = Path.Combine(dir.Path, "p.mov");
        r.Render(clip, card, info, lut, outPath, _ => { }, () => false);
        var y = Ffmpeg.MeanLuma(outPath);   // helper: ffmpeg signalstats YAVG of the first frame, 8-bit legal
        var code = (y - 16) / 219.0;        // back to 0..1
        Assert.InRange(code, 0.39, 0.45);
    }
```

(`FfmpegFact`, `Tools()` a pomocník pro spuštění ffmpeg existují v testech plánu 2 — použít jejich skutečné názvy; `Ffmpeg.MeanLuma` doplnit jako malý pomocník přes `signalstats`: `ffmpeg -i p.mov -vf signalstats,metadata=print:key=lavfi.signalstats.YAVG -frames:v 1 -f null -` a přečíst číslo z výstupu.)

`RawBackendsTests` (upravit stávající volání na nový podpis a přidat):

```csharp
    // Review focus 5
    [Fact]
    public void Raw_LogRequested_ForBuiltinLut()
    {
        var info = new ClipInfo { Height = 4320 };
        var log = R3dBackend.Args(@"E:\A.R3D", @"C:\t", info, new ProxySettings { Height = 1080 }, new RawLook(Log: true, null));
        Assert.Contains("--gammaCurve", log);
        var rec709 = R3dBackend.Args(@"E:\A.R3D", @"C:\t", info, new ProxySettings { Height = 1080 }, new RawLook(Log: false, null));
        Assert.DoesNotContain("--gammaCurve", rec709);
        var (arri, _, _) = ArriBackend.Args("in", "out", new ClipInfo(), new ProxySettings(), new RawLook(true, "AWG4/D65/LogC4"), "p.json");
        Assert.Contains("AWG4/D65/LogC4", arri);
        var (arri709, _, _) = ArriBackend.Args("in", "out", new ClipInfo(), new ProxySettings(), new RawLook(false, null), "p.json");
        Assert.Contains("Rec.709/D65/BT.1886", arri709);
    }
```

`StageTwoTests` (nový):

```csharp
    [FfmpegFact]
    public void Report_RecordsLutPerClip()
    {
        // Reuse the existing stage-two fixture that offloads a synthetic video card with AnalyzeClips = true;
        // add a Sony XML next to its clip (s-log3-cine / S-Gamut3Cine) before the job runs.
        // Assert: report.Clips[0].Lut is { Source: "auto", Id: "sony-slog3-sgamut3cine" } and the JSON on disk contains "\"lut\"".
    }
```

(Napsat test plně podle existujícího pomocníka ve `StageTwoTests` — tvrzení výše jsou závazná.)

Run → build FAIL.

- [ ] **Step 2: Implementace**

`FilterGraph`:

```csharp
    /// Full-range RGB (BRAW decoder output, video tagged color_range=pc) → the legal-expanded convention the
    /// built-in LUTs expect (code = x·876 + 64). On rgb48le: v16 → ((v16/65535·1023 − 64)/876)·65535, clipped.
    public const string PreMap =
        "lutrgb=r='clip((val*1023/65535-64)/876*65535,0,65535)':g='clip((val*1023/65535-64)/876*65535,0,65535)':b='clip((val*1023/65535-64)/876*65535,0,65535)'";

    static string Lut(string file, bool premap) =>
        "format=rgb48le," + (premap ? PreMap + "," : "") + $"lut3d=file={file}:interp=tetrahedral";

    public static string Thumbnail(string? lutFile, bool premap) =>
        string.Join(",", new[] { "scale=-2:180:flags=bicubic", lutFile != null ? Lut(lutFile, premap) : null, "format=yuvj420p" }.Where(x => x != null));
```

a v `Proxy(...)` nahradit `bool lut` parametry `string? lutFile, bool premap` a `if (lut) vf.Add(Lut);` → `if (lutFile != null) vf.Add(Lut(lutFile, premap));`. Konstantu `LutFile = "look.cube"` ponechat pro vlastní LUT.

`FrameSource`: `public bool FullRange { get; init; }`; v `BrawBackend.Source` nastavit `FullRange = true`.

`RawBackends`: `public sealed record RawLook(bool Log, string? ArriColorSpace);` rozhraní `IRawBackend.Source(..., ProxySettings settings, RawLook look, ExternalTools tools, ...)`; v R3D `if (look.Log) args.AddRange([...IPP2 Log3G10...])`; v ARRI `if (look.Log) args.AddRange(["--target-colorspace", look.ArriColorSpace ?? ColorSpace(null, c)]); else args.AddRange(["--target-colorspace", "Rec.709/D65/BT.1886", "--embedded-look"]);`. BRAW a Canon `look` ignorují (BRAW dekoduje vždy v gamě klipu).

`ProxyRenderer`:
- konstruktor: místo jednoho `hasLut` jen zkopíruje vlastní LUT (je-li režim Custom a soubor existuje) do `look.cube` jako dosud; vestavěné LUTy kopíruje líně: `string? PrepareLut(LutUse? lut)` → `null` pro `null`/`File == null`; vlastní → `FilterGraph.LutFile`; vestavěný → `lut-{Id}.cube` (zkopírovat z `lut.File` do `workDir`, jednou na id).
- `LookFor(LutUse? lut, ClipInfo info)`: `Log = lut?.File != null`; `ArriColorSpace` = `"AWG4/D65/LogC4"` pro `arri-logc4-awg4`, `"AWG3/D65/LogC3"` pro `arri-logc3-awg3`, pro vlastní `RawBackends.ColorSpace(lut.File, info)`, jinak `null`.
- `Thumbnails(clip, cardPath, info, LutUse? lut, isCancelled)`: `var file = PrepareLut(lut); var src = SourceFor(..., LookFor(lut, info)); var vf = FilterGraph.Thumbnail(file, premap: lut?.IsBuiltin == true && (src.FullRange || info.ColorRange == "pc"));` (filtr počítat až po `SourceFor`, protože premap závisí na zdroji).
- `Render(clip, cardPath, info, LutUse? lut, output, progress, isCancelled)`: obdobně.
- `SourceFor(clip, cardPath, info, look, singleFrameAt, forRender)` předá `look` backendu.

`OffloadJob.AnalyzeAndRender`: po úspěšné sondě klipu `entries[i].Lut = LutResolver.Resolve(entries[i].Clip, info, Layout.Join(cardPaths[d], entries[i].Clip.Path), Config.Proxy, LutCatalog.Default);` (pro klipy bez sondy/chybou `null`); `TryThumbnails(renderer, clip, cardPaths[d], info, entries[i].Lut, …)`; náhledy z hotové proxy (RAW) `lut: null` (proxy už LUT má); render `renderer.Render(clip, cardPaths[d], info, entries[i].Lut, …)`. Pozn.: Sony XML se čte z **cíle** (karta už může být vysunutá) — XML je na kartě vedle klipu, takže se zkopírovalo s ním.

Přizpůsobit všechna existující volání v testech (`ProxyRendererTests`, `RawBackendsTests`, `StageTwoTests`) novým podpisům se stejnými tvrzeními (vlastní LUT = `new LutUse("custom", null, "look.cube", "vlastní LUT") { File = … }`).

- [ ] **Step 3: Testy + celá sada, Commit**

Run: `dotnet test --filter "FullyQualifiedName~ProxyRendererTests|FullyQualifiedName~RawBackendsTests|FullyQualifiedName~StageTwoTests"` → PASS; celá sada zelená, bez varování.
Řádek VYVOJ: `- Úkol 5 — proxy a náhledy s LUTem vybraným pro každý klip; BRAW a plnorozsahové video přepočtené na konvenci vestavěných LUTů; RED a ARRIRAW vydají log, když se LUT použije`.

```powershell
git add windows docs
git commit -m "Windows: per-clip LUT in proxies and thumbnails" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: CLI a balíček

**Files:**
- Modify: `windows/src/Offload.Cli/CopyCommand.cs`, `windows/scripts/build.ps1`, `windows/README.md`
- Test: `windows/tests/Offload.Core.Tests/LutOptionTests.cs` (čistá funkce v Core)

**Interfaces:**
- Produces: `LutOption.Parse(string? value, string? fallback, LutCatalog cat) -> (LutMode Mode, string? Builtin, string? Path, string? Fallback, string? Error)` v Core (`Luts.cs`); CLI `--lut auto|none|<id>|<cesta.cube>` (výchozí auto), `--lut-fallback <id|none>`; `build.ps1` kopíruje `shared\luts\*.cube` + `luts.json` do `build\Offload\luts\`.

- [ ] **Step 1: Padající testy**

```csharp
using Offload.Core;

namespace Offload.Core.Tests;

public class LutOptionTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    [Fact]
    public void Parse()
    {
        Assert.Equal((LutMode.Auto, null, null, null, null), LutOption.Parse(null, null, Cat));
        Assert.Equal((LutMode.Auto, null, null, "dji-dlog-dgamut", null), LutOption.Parse("auto", "dji-dlog-dgamut", Cat));
        Assert.Equal((LutMode.None, null, null, null, null), LutOption.Parse("none", null, Cat));
        Assert.Equal((LutMode.Builtin, "sony-slog3-sgamut3", null, null, null), LutOption.Parse("sony-slog3-sgamut3", null, Cat));
        Assert.Equal(LutMode.Custom, LutOption.Parse(@"C:\luts\show.cube", null, Cat).Mode);
        Assert.Equal(@"C:\luts\show.cube", LutOption.Parse(@"C:\luts\show.cube", null, Cat).Path);
        Assert.StartsWith("Unknown LUT", LutOption.Parse("nope", null, Cat).Error);
        Assert.StartsWith("Unknown fallback LUT", LutOption.Parse("auto", "nope", Cat).Error);
        Assert.Null(LutOption.Parse("auto", "none", Cat).Fallback);
    }
}
```

- [ ] **Step 2: Implementace**

V `Luts.cs`:

```csharp
/// `--lut` / `--lut-fallback` of the CLI: auto, none, a built-in id, or a path to a .cube (anything with ".cube" or a path separator).
public static class LutOption
{
    public static (LutMode Mode, string? Builtin, string? Path, string? Fallback, string? Error) Parse(string? value, string? fallback, LutCatalog cat)
    {
        string? fb = null;
        if (fallback != null && !fallback.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            if (cat.Find(fallback) == null) return (LutMode.Auto, null, null, null, $"Unknown fallback LUT '{fallback}'. Built-in: {Ids(cat)}");
            fb = fallback;
        }
        if (value == null || value.Equals("auto", StringComparison.OrdinalIgnoreCase)) return (LutMode.Auto, null, null, fb, null);
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return (LutMode.None, null, null, fb, null);
        if (value.EndsWith(".cube", StringComparison.OrdinalIgnoreCase) || value.Contains('\\') || value.Contains('/'))
            return (LutMode.Custom, null, value, fb, null);
        return cat.Find(value) != null
            ? (LutMode.Builtin, value, null, fb, null)
            : (LutMode.Auto, null, null, fb, $"Unknown LUT '{value}'. Use auto, none, a .cube path or one of: {Ids(cat)}");
    }

    static string Ids(LutCatalog cat) => string.Join(", ", cat.Luts.Select(l => l.Id));
}
```

`CopyCommand`: popis `--lut` → `"LUT for proxies and thumbnails: auto (by camera, default), none, a built-in id (offload copy --lut list prints them) or a .cube path."`; nová volba `--lut-fallback`; při parsování: `var (mode, builtin, path, fb, err) = LutOption.Parse(r.GetValue(lut), r.GetValue(lutFallback), LutCatalog.Default); if (err != null) throw new ArgumentException(err);` vlastní cestu přes `CliPath.Full` a existence jako dosud (`LUT not found: …`); nastavit `LutMode`, `LutBuiltin`, `LutPath`, `LutFallback` v `ProxySettings`. Hodnota `--lut list` vypíše `id — název` všech vestavěných LUTů a skončí s 0 (bez kopírování). Výpis plánu: `LUT: auto (fallback …)` / název.

`build.ps1` (za publikování aplikace):

```powershell
# Built-in LUTs (shared with the Mac version).
$luts = Join-Path (Split-Path -Parent $root) 'shared\luts'
New-Item -ItemType Directory -Force (Join-Path $out 'luts') | Out-Null
Copy-Item (Join-Path $luts '*.cube'), (Join-Path $luts 'luts.json') (Join-Path $out 'luts')
```

`README.md`: volby `--lut`, `--lut-fallback`, `--lut list`; složka `luts\` v balíčku.

- [ ] **Step 3: Testy, ruční ověření CLI, Commit**

```powershell
dotnet test --filter "FullyQualifiedName~LutOptionTests"
dotnet build
src\Offload.Cli\bin\Debug\net10.0-windows\offload.exe copy --lut list
```

Expected: 11 řádků `id — název`, exit 0. Celá sada zelená. `scripts\build.ps1` → `build\Offload\luts\` má 11 `.cube` + `luts.json`.
Řádek VYVOJ: `- Úkol 6 — CLI --lut auto|none|<id>|<cesta>, --lut-fallback, --lut list; LUTy v balíčku`.

```powershell
git add windows docs
git commit -m "Windows: --lut auto|none|id|path and bundled LUTs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: GUI — výběr LUTu

**Files:**
- Modify: `windows/src/Offload.App/ProxyOptionsViewModel.cs`, `SetupPanel.xaml`, `MainViewModel.Setup.cs` (LutProblem jen pro vlastní — plyne z Task 4 `LutInUse`)

**Interfaces:**
- Produces: `ProxyOptionsViewModel.LutChoices` (`IReadOnlyList<Option<string>>`: `"auto"` „Automaticky podle kamery“, vestavěné id → název, `"none"` „Bez LUT“, `"custom"` „Vlastní…“), `LutChoice` (Option), `FallbackChoices` (`"none"` „Bez LUT“ + vestavěné), `Fallback`, `ShowFallback` (režim auto), `ShowCustom` (režim custom), stávající `LutName`/`PickLutCommand` pro vlastní. AutomationId: `LutChoice`, `LutFallbackChoice`.

- [ ] **Step 1: ViewModel**

Seznamy se staví z `LutCatalog.Default` (načte se jednou, mimo UI vlákno při startu: `MainViewModel` zavolá `Task.Run(() => LutCatalog.Default)` a po dokončení `Proxy.CatalogLoaded()` → `RaiseMany(nameof(LutChoices), nameof(FallbackChoices), nameof(LutChoice), nameof(Fallback))`; do té doby seznamy obsahují jen auto/none/custom).
Výběr `"custom"` bez uloženého `LutPath` hned otevře dialog (`PickLut`); zrušení dialogu vrátí předchozí režim. Výběr vestavěného → `LutMode = Builtin`, `LutBuiltin = id`. Každá změna → `Changed(...)` (uloží nastavení, přepočítá `LutProblem`/`ToolsWarning`).

- [ ] **Step 2: XAML**

V `SetupPanel.xaml` nahradit řádek „LUT“ (Grid.Row 4) za:

```xml
                        <TextBlock Grid.Row="4" Text="LUT" Style="{StaticResource Caption}" VerticalAlignment="Center" />
                        <StackPanel Grid.Row="4" Grid.Column="1" Margin="0,2">
                            <ComboBox ItemsSource="{Binding LutChoices}" SelectedItem="{Binding LutChoice}" DisplayMemberPath="Label"
                                      AutomationProperties.AutomationId="LutChoice" />
                            <DockPanel Margin="0,4,0,0" Visibility="{Binding ShowFallback, Converter={StaticResource BoolToVisible}}">
                                <TextBlock DockPanel.Dock="Top" Text="Když kameru nepoznám:" Style="{StaticResource Caption}" />
                                <ComboBox ItemsSource="{Binding FallbackChoices}" SelectedItem="{Binding Fallback}" DisplayMemberPath="Label"
                                          AutomationProperties.AutomationId="LutFallbackChoice" />
                            </DockPanel>
                            <DockPanel Margin="0,4,0,0" Visibility="{Binding ShowCustom, Converter={StaticResource BoolToVisible}}">
                                <Button DockPanel.Dock="Right" Style="{StaticResource SmallButton}" Content="Vybrat…" Command="{Binding PickLutCommand}" />
                                <TextBlock Text="{Binding LutName}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" ToolTip="{Binding LutPath}" />
                            </DockPanel>
                        </StackPanel>
```

(Odstranit tlačítko „✕ bez LUT“ — „Bez LUT“ je teď v seznamu. Přidat `LutPath` do VM pro tooltip.)

- [ ] **Step 3: Ověření**

`dotnet build` bez varování; celá sada. **GUI spouštět jen po domluvě s uživatelem** (je u PC) — jinak popsat v reportu, co ověřit ručně: výchozí „Automaticky podle kamery“ + „Když kameru nepoznám: Bez LUT“; vestavěný LUT v seznamu; „Vlastní…“ otevře dialog a zobrazí soubor; chybějící vlastní LUT → červená hláška a neaktivní start; režim se pamatuje po restartu.
Řádek VYVOJ: `- Úkol 7 — GUI: výběr LUTu (automaticky / vestavěný / bez / vlastní) a záložní LUT`.

```powershell
git add windows docs
git commit -m "Windows GUI: LUT choice (auto, built-in, none, custom) and fallback" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Dokumentace a Mac

**Files:**
- Modify: `docs/ROZDILY.md`, `docs/VYVOJ-MAC.md`, `docs/VYVOJ-WINDOWS.md` (tabulka plánů: 5 → hotovo), `windows/README.md` (GUI část), spec LUTů (stav → „hotovo na Windows“)

- [ ] **Step 1: Texty**

- ROZDILY: řádek „Vestavěné LUTy + automatický výběr podle kamery“ — Mac ⏳ (naplánováno), Windows ✅; záměrné rozdíly: Canon/Panasonic/DJI/Fuji se zatím nepoznají automaticky (chybí vzorky), D-Log M bez LUTu.
- VYVOJ-MAC „Naplánováno“: „Vestavěné LUTy (sdílené `shared/luts/`) a automatický výběr podle kamery — stejná pravidla jako `windows/src/Offload.Core/Luts.cs` (`LutResolver`), přibalit `shared/luts` do `Offload.app/Contents/Resources/luts`, klíče `proxy.lutMode/lutBuiltin/lutFallback` a klip `lut` (FORMATY.md).“
- README Windows: v GUI části „LUT: Automaticky podle kamery“ + co poznává; ruční test: ukázkové klipy z kamer.

- [ ] **Step 2: Commit**

```powershell
git add docs windows
git commit -m "Docs: built-in LUTs on Windows, planned for the Mac" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
