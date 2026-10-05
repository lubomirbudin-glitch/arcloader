"""Offload app icon: a memory card with a green "verified" check on a blue-teal tile.
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
    """Rounded square with a vertical gradient (Windows 11 / macOS style)."""
    top, bottom = (24, 92, 186), (10, 160, 160)
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        gd.line([(0, y), (S, y)], fill=lerp(top, bottom, y / (S - 1)) + (255,))
    mask = Image.new("L", (S, S), 0)
    m = int(S * 0.06)
    ImageDraw.Draw(mask).rounded_rectangle([m, m, S - m, S - m], radius=int(S * 0.22), fill=255)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)
    return out


def card(img):
    """White SD-style card: rounded body with the top-right corner cut, gold contacts."""
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = int(S * 0.24), int(S * 0.20), int(S * 0.70), int(S * 0.80)
    cut = int(S * 0.13)
    r = int(S * 0.045)
    body = [(x0 + r, y0), (x1 - cut, y0), (x1, y0 + cut), (x1, y1 - r), (x1 - r, y1),
            (x0 + r, y1), (x0, y1 - r), (x0, y0 + r)]
    # soft shadow
    sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(sh).polygon([(x + int(S * 0.015), y + int(S * 0.025)) for x, y in body], fill=(0, 30, 60, 110))
    img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(S * 0.02)))
    d.polygon(body, fill=(250, 252, 255, 255))
    # contacts
    cw, ch, gap = int(S * 0.045), int(S * 0.12), int(S * 0.022)
    cx = x0 + int(S * 0.06)
    cy = y0 + int(S * 0.07)
    for i in range(5):
        if cx + cw > x1 - cut + int(S * 0.02) and i > 0:
            break
        d.rounded_rectangle([cx, cy, cx + cw, cy + ch], radius=int(S * 0.008), fill=(232, 176, 58, 255))
        cx += cw + gap
    # label lines
    lx0, lx1 = x0 + int(S * 0.06), x1 - int(S * 0.06)
    for k, ly in enumerate((y0 + int(S * 0.33), y0 + int(S * 0.40))):
        d.rounded_rectangle([lx0, ly, lx1 - (int(S * 0.10) if k else 0), ly + int(S * 0.025)],
                            radius=int(S * 0.012), fill=(198, 210, 226, 255))


def badge(img):
    """Green circle with a white check: copied and verified."""
    d = ImageDraw.Draw(img)
    cx, cy, rad = int(S * 0.68), int(S * 0.70), int(S * 0.17)
    ring = int(S * 0.025)
    d.ellipse([cx - rad - ring, cy - rad - ring, cx + rad + ring, cy + rad + ring], fill=(255, 255, 255, 255))
    d.ellipse([cx - rad, cy - rad, cx + rad, cy + rad], fill=(34, 170, 84, 255))
    w = int(S * 0.045)
    pts = [(cx - rad * 0.50, cy + rad * 0.02), (cx - rad * 0.12, cy + rad * 0.40), (cx + rad * 0.55, cy - rad * 0.38)]
    d.line(pts, fill=(255, 255, 255, 255), width=w, joint="curve")
    for p in (pts[0], pts[-1]):
        d.ellipse([p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2], fill=(255, 255, 255, 255))


def main():
    os.makedirs(OUT, exist_ok=True)
    img = tile()
    card(img)
    badge(img)
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
