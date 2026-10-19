"""
Seamless Malaysian batik prints for the cast's clothes, drawn from scratch (no reference images):
bunga raya (hibiscus) heads with veined petals and a stamen, leaf sprays, spiral tendrils (awan larat),
dotted fill (isen-isen), and the wax crackle (retak) that shows where the dye seeped through. Every
motif is drawn nine times, wrapped across the edges, so the tile repeats without a seam.

  python Tools/mpfb_characters/make_batik.py            # writes textures/batik_<palette>_{basecolor,normal}.png

Palettes are (ground, deep motif, bright motif, outline/cream) in sRGB.
"""
import math, os, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "textures")
SIZE = 2048

PALETTES = {
    "pakmat":  ((196, 72, 34), (98, 24, 22), (240, 168, 52), (250, 226, 184)),     # burnt orange, maroon, gold
    "teal":    ((20, 104, 112), (8, 46, 58), (236, 196, 92), (232, 240, 226)),
    "rose":    ((214, 104, 128), (122, 32, 64), (250, 214, 120), (252, 236, 236)),
    "navy":    ((30, 48, 104), (12, 18, 48), (226, 146, 54), (236, 232, 214)),
    "green":   ((58, 118, 70), (20, 52, 34), (238, 196, 70), (238, 238, 214)),
    "purple":  ((104, 60, 140), (44, 22, 70), (240, 180, 90), (240, 230, 240)),
}


def wrap_draw(fn, x, y):
    """Draw a motif at (x, y) and its eight wrapped copies."""
    for dx in (-SIZE, 0, SIZE):
        for dy in (-SIZE, 0, SIZE):
            fn(x + dx, y + dy)


def petal(d, cx, cy, ang, length, width, fill, outline, vein):
    pts = []
    for i in range(25):
        t = i / 24
        # a broad rounded hibiscus petal with a ruffled tip
        r = length * t
        w = width * math.sin(math.pi * t) ** 0.7 * (1 + 0.08 * math.sin(t * 40))
        pts.append((r, w))
    poly = [(cx + math.cos(ang) * r - math.sin(ang) * w, cy + math.sin(ang) * r + math.cos(ang) * w) for r, w in pts]
    poly += [(cx + math.cos(ang) * r + math.sin(ang) * w, cy + math.sin(ang) * r - math.cos(ang) * w) for r, w in reversed(pts)]
    d.polygon(poly, fill=fill, outline=outline)
    d.line(poly + [poly[0]], fill=outline, width=5)
    # veins fanning from the centre
    for k in (-0.35, 0, 0.35):
        a = ang + k * 0.5
        d.line([(cx, cy), (cx + math.cos(a) * length * 0.82, cy + math.sin(a) * length * 0.82)], fill=vein, width=3)


def hibiscus(d, x, y, s, pal, rng):
    ground, deep, bright, cream = pal
    base = rng.uniform(0, math.tau)
    for i in range(5):
        petal(d, x, y, base + i * math.tau / 5, 110 * s, 62 * s, bright if i % 2 else _mix(bright, cream, 0.25), deep, deep)
    d.ellipse([x - 22 * s, y - 22 * s, x + 22 * s, y + 22 * s], fill=deep)
    # the stamen: a curved stalk out of the flower, dotted at the end
    a = base + 0.3
    pts = [(x + math.cos(a + t * 0.4) * 140 * s * t, y + math.sin(a + t * 0.4) * 140 * s * t) for t in np.linspace(0, 1, 12)]
    d.line(pts, fill=cream, width=int(7 * s))
    for k in range(5):
        px, py = pts[-1]
        d.ellipse([px - 9 * s + k * 6 * s * math.cos(a + 1.6), py - 9 * s + k * 6 * s * math.sin(a + 1.6),
                   px + 9 * s + k * 6 * s * math.cos(a + 1.6), py + 9 * s + k * 6 * s * math.sin(a + 1.6)], fill=bright, outline=deep)


def leaf(d, x, y, ang, length, pal):
    ground, deep, bright, cream = pal
    pts = []
    for i in range(17):
        t = i / 16
        pts.append((length * t, length * 0.22 * math.sin(math.pi * t)))
    poly = [(x + math.cos(ang) * a - math.sin(ang) * b, y + math.sin(ang) * a + math.cos(ang) * b) for a, b in pts]
    poly += [(x + math.cos(ang) * a + math.sin(ang) * b, y + math.sin(ang) * a - math.cos(ang) * b) for a, b in reversed(pts)]
    d.polygon(poly, fill=deep, outline=cream)
    d.line(poly + [poly[0]], fill=cream, width=3)
    d.line([(x, y), (x + math.cos(ang) * length * 0.9, y + math.sin(ang) * length * 0.9)], fill=cream, width=2)


def tendril(d, x, y, ang, s, pal, rng):
    ground, deep, bright, cream = pal
    pts = []
    for i in range(60):
        t = i / 59
        r = 160 * s * t
        a = ang + t * 4.6
        pts.append((x + math.cos(a) * r * (1 - 0.55 * t), y + math.sin(a) * r * (1 - 0.55 * t)))
    d.line(pts, fill=cream, width=int(5 * s))
    for i in range(6, 56, 9):
        px, py = pts[i]
        leaf(d, px, py, ang + i * 0.09 + rng.uniform(-0.5, 0.5), 46 * s, pal)


def _mix(a, b, t):
    return tuple(int(a[i] * (1 - t) + b[i] * t) for i in range(3))


def crackle(img, pal, rng):
    """Retak: thin dark veins where the wax cracked and the dye crept in."""
    ground, deep, bright, cream = pal
    layer = Image.new("L", (SIZE, SIZE), 0)
    d = ImageDraw.Draw(layer)
    for _ in range(260):
        x, y = rng.uniform(0, SIZE), rng.uniform(0, SIZE)
        a = rng.uniform(0, math.tau)
        pts = [(x, y)]
        for _ in range(rng.randint(4, 12)):
            a += rng.uniform(-0.9, 0.9)
            x += math.cos(a) * rng.uniform(10, 40); y += math.sin(a) * rng.uniform(10, 40)
            pts.append((x, y))
        for dx in (-SIZE, 0, SIZE):
            for dy in (-SIZE, 0, SIZE):
                d.line([(px + dx, py + dy) for px, py in pts], fill=110, width=2)
    mask = np.asarray(layer.filter(ImageFilter.GaussianBlur(0.8)), dtype=np.float32)[..., None] / 255
    arr = np.asarray(img, dtype=np.float32)
    arr = arr * (1 - mask * 0.55) + np.array(deep, np.float32) * mask * 0.55
    return Image.fromarray(arr.clip(0, 255).astype(np.uint8))


def make(name, pal, seed=7):
    rng = random.Random(seed)
    ground, deep, bright, cream = pal
    img = Image.new("RGB", (SIZE, SIZE), ground)
    d = ImageDraw.Draw(img)
    # isen-isen: a fine field of dots over the ground
    for _ in range(5200):
        x, y, r = rng.uniform(0, SIZE), rng.uniform(0, SIZE), rng.uniform(2.5, 5)
        wrap_draw(lambda X, Y: d.ellipse([X - r, Y - r, X + r, Y + r], fill=_mix(ground, cream, 0.35)), x, y)
    # tendrils first (underneath), then the flowers on a jittered grid so it reads busy but even
    for _ in range(26):
        x, y, a, s = rng.uniform(0, SIZE), rng.uniform(0, SIZE), rng.uniform(0, math.tau), rng.uniform(0.8, 1.2)
        wrap_draw(lambda X, Y: tendril(d, X, Y, a, s, pal, random.Random(int(x * 7 + y))), x, y)
    n = 5
    for i in range(n):
        for j in range(n):
            x = (i + 0.5 + rng.uniform(-0.25, 0.25)) * SIZE / n + (j % 2) * SIZE / (2 * n)
            y = (j + 0.5 + rng.uniform(-0.25, 0.25)) * SIZE / n
            s = rng.uniform(1.0, 1.45)
            sub = random.Random(i * 31 + j)
            wrap_draw(lambda X, Y: hibiscus(d, X, Y, s, pal, random.Random(sub.random())), x, y)
            for k in range(3):
                a = rng.uniform(0, math.tau)
                lx, ly = x + math.cos(a) * 150 * s, y + math.sin(a) * 150 * s
                wrap_draw(lambda X, Y: leaf(d, X, Y, a, 90 * s, pal), lx, ly)
    img = crackle(img, pal, rng)
    # dyed cloth isn't vector-crisp: a hair of bleed
    img = img.filter(ImageFilter.GaussianBlur(0.7))
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, f"batik_{name}_basecolor.png"))
    # normal map: the waxed/dyed lines sit a hair proud of the weave, plus a fine cotton weave
    lum = np.asarray(img.convert("L"), dtype=np.float32) / 255
    edges = np.asarray(img.convert("L").filter(ImageFilter.FIND_EDGES).filter(ImageFilter.GaussianBlur(1.2)), dtype=np.float32) / 255
    yy, xx = np.mgrid[0:SIZE, 0:SIZE]
    weave = 0.5 + 0.25 * np.sin(xx * math.tau / 6) * np.sin(yy * math.tau / 6)
    h = edges * 0.8 + weave * 0.25 + lum * 0.1
    gx = np.roll(h, -1, 1) - np.roll(h, 1, 1)
    gy = np.roll(h, -1, 0) - np.roll(h, 1, 0)
    k = 2.2
    nx, ny, nz = -gx * k, gy * k, np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    nrm = np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5
    Image.fromarray((nrm * 255).astype(np.uint8)).save(os.path.join(OUT, f"batik_{name}_normal.png"))
    print("wrote", name)


if __name__ == "__main__":
    import sys
    names = sys.argv[1:] or list(PALETTES)
    for i, n in enumerate(names):
        make(n, PALETTES[n], seed=7 + i * 13)
