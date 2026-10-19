"""
Cartoon eye texture in MPFB's eye-texture layout (two eyeballs on one sheet): clean whites, an iris ~1.6x the
realistic one with a warm two-tone ring, a big pupil and a soft white catchlight. Upin & Ipin-style friendly eyes.

  python Tools/mpfb_characters/make_eyes.py [iris r,g,b]   -> textures/eyes_toon.png
"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(os.environ["APPDATA"], "Blender Foundation", "Blender", "5.2", "extensions", ".user", "user_default",
                   "mpfb", "data", "eyes", "materials", "brown_eye.png")


def make(iris=(92, 52, 26), scale=1.8, out=os.path.join(HERE, "textures", "eyes_toon.png")):
    im = Image.open(SRC).convert("RGBA")
    a = np.asarray(im).astype(np.float32)
    w, h = im.size
    lum = a[..., :3].mean(-1)
    # the two irises: dark, saturated discs either side of the sheet's diagonal
    yy, xx = np.mgrid[0:h, 0:w]
    dark = (lum < 110) & (a[..., 3] > 200)
    eyes = []
    for side in (xx > yy, xx <= yy):          # (the eyes sit either side of the main diagonal)
        m = dark & side
        cy, cx = yy[m].mean(), xx[m].mean()
        r = np.sqrt(m.sum() / np.pi)
        eyes.append((cx, cy, r))
    # whites: lift and clean the sclera (no veins), keep alpha
    sclera = (a[..., 3] > 200) & ~dark
    a[sclera, :3] = a[sclera, :3] * 0.15 + np.array([244, 240, 234]) * 0.85
    img = Image.fromarray(a.clip(0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    deep = tuple(int(c * 0.55) for c in iris)
    light = tuple(min(255, int(c * 1.6)) for c in iris)
    for cx, cy, r in eyes:
        R = r * scale
        d.ellipse([cx - R * 1.04, cy - R * 1.04, cx + R * 1.04, cy + R * 1.04], fill=(30, 18, 12, 255))   # dark limbal ring
        d.ellipse([cx - R, cy - R, cx + R, cy + R], fill=deep + (255,))
        d.ellipse([cx - R * 0.82, cy - R * 0.72, cx + R * 0.82, cy + R * 0.92], fill=iris + (255,))
        d.ellipse([cx - R * 0.55, cy - R * 0.2, cx + R * 0.7, cy + R * 0.85], fill=light + (255,))          # warm lower glow
        P = R * 0.5
        d.ellipse([cx - P, cy - P, cx + P, cy + P], fill=(14, 9, 7, 255))
        C = R * 0.24
        d.ellipse([cx - R * 0.42 - C, cy - R * 0.42 - C, cx - R * 0.42 + C, cy - R * 0.42 + C], fill=(255, 255, 255, 255))
        c2 = R * 0.1
        d.ellipse([cx + R * 0.3 - c2, cy + R * 0.32 - c2, cx + R * 0.3 + c2, cy + R * 0.32 + c2], fill=(255, 255, 255, 230))
    img = img.filter(ImageFilter.GaussianBlur(0.8))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    img.save(out)
    print("wrote", out, [tuple(round(v) for v in e) for e in eyes])


if __name__ == "__main__":
    col = tuple(int(x) for x in sys.argv[1].split(",")) if len(sys.argv) > 1 else (92, 52, 26)
    make(col)
