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
import warnings

import numpy as np

warnings.filterwarnings("ignore", message=".*Matplotlib.*", category=Warning)   # optional plotting dependency, unused here
import colour  # noqa: E402

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
        f'TITLE "{p["name"].replace("→", "to")}"',
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
