"""mmMover app icon (option E, user 2026-10-12): three petrol chevrons moving right on a graphite tile.
Drawn at 4096 px and downscaled for smooth edges; writes a 1024 master PNG, a Windows .ico and a macOS iconset.

Usage: python make_icon.py [out_dir] [--preview]
  out_dir    defaults to the folder of this script (shared/icon), so a plain run regenerates the committed files
  --preview  also writes preview.png (the sizes people actually see, on dark and light) - not committed
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFilter

S = 4096                      # drawing resolution
_ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
PREVIEW = "--preview" in sys.argv[1:]
OUT = _ARGS[0] if _ARGS else os.path.dirname(os.path.abspath(__file__))


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def tile():
    """Rounded square with a vertical graphite gradient (the website's colours; Windows 11 / macOS style)."""
    top, bottom = (44, 54, 56), (26, 33, 35)
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        gd.line([(0, y), (S, y)], fill=lerp(top, bottom, y / (S - 1)) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([M, M, S - M, S - M], radius=int(S * 0.22), fill=255)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)
    return out


M = int(S * 0.06)             # margin around the tile


def at(x, y):
    """A point of the 1024 design grid (icon option E, 2026-10-12) on the tile inside the margin."""
    f = (S - 2 * M) / 1024
    return (M + x * f, M + y * f)


def chevrons(img):
    """Three chevrons moving right, fading in: the last one solid petrol (mmMover icon, option E)."""
    f = (S - 2 * M) / 1024
    w = int(96 * f)
    for dx, colour, alpha in ((0, (63, 155, 157), 0.35), (170, (79, 169, 171), 0.65), (340, (127, 209, 210), 1.0)):
        layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        pts = [at(230 + dx, 300), at(420 + dx, 512), at(230 + dx, 724)]
        fill = colour + (255,)
        d.line(pts, fill=fill, width=w, joint="curve")
        for px, py in (pts[0], pts[-1]):
            d.ellipse([px - w / 2, py - w / 2, px + w / 2, py + w / 2], fill=fill)
        if alpha < 1.0:
            a = layer.getchannel("A").point(lambda v, k=alpha: int(v * k))
            layer.putalpha(a)
        img.alpha_composite(layer)


def main():
    os.makedirs(OUT, exist_ok=True)
    img = tile()
    chevrons(img)
    master = img.resize((1024, 1024), Image.LANCZOS)
    master.save(os.path.join(OUT, "Offload-1024.png"))
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    master.save(os.path.join(OUT, "Offload.ico"), sizes=[(s, s) for s in sizes])
    iconset = os.path.join(OUT, "Offload.iconset")
    os.makedirs(iconset, exist_ok=True)
    for base in (16, 32, 128, 256, 512):
        master.resize((base, base), Image.LANCZOS).save(os.path.join(iconset, f"icon_{base}x{base}.png"))
        master.resize((base * 2, base * 2), Image.LANCZOS).save(os.path.join(iconset, f"icon_{base}x{base}@2x.png"))
    if PREVIEW:
        save_preview(master)


def save_preview(master):
    # preview sheet: the sizes people actually see
    sheet = Image.new("RGBA", (1024 + 48 * 2 + 256 + 64 + 32 + 16 + 120, 1100), (40, 40, 40, 255))
    x = 24
    for s in (1024, 256, 64, 32, 16):
        im = master.resize((s, s), Image.LANCZOS)
        sheet.alpha_composite(im, (x, 24))
        x += s + 24
    light = Image.new("RGBA", (400, 120), (243, 243, 243, 255))
    lx = 16
    for s in (64, 32, 16):
        light.alpha_composite(master.resize((s, s), Image.LANCZOS), (lx, 16))
        lx += s + 24
    sheet.alpha_composite(light, (24 + 1024 + 24, 400))
    sheet.save(os.path.join(OUT, "preview.png"))


if __name__ == "__main__":
    main()
