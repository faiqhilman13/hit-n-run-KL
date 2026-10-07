"""
The painted detail textures of the architecture library (Scripts/World/Arch): one 2048 x 2048 sheet of
8 x 8 cells of 256 px, imported by Unity as a Texture2DArray (one slice per cell, top-left first), so
buildings can tile any cell (roof tiles, planks, breeze blocks) without bleeding into its neighbours.

Hit & Run's way of painting (see docs/AGENT_HANDOFF.md, the SHAR remaster study): flat fills, thin dark
outlines, one shade step, glass with diagonal glints, grime as flat drips, and a very light grain. Cells
marked [T] are painted near-white so the vertex colour tints them (shutters, planks, tiles, breeze blocks).
The order here is the ArchTex enum in Scripts/World/Arch/ArchTex.cs: keep the two in step.

    uv run --no-project --with pillow --with numpy python Tools/klmap/gen_arch_atlas.py
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, '..', '..', 'Assets', 'KampungRun', 'Resources', 'KLMap', 'arch_atlas.png'))
S = 512                      # drawn at 2x, scaled down at the end
N = 8
INK = (38, 32, 44)
FONTS = 'C:/Windows/Fonts/'

rng = random.Random(1957)


def font(name, size):
    try:
        return ImageFont.truetype(FONTS + name, size)
    except OSError:
        return ImageFont.load_default()


def cell(fill=(255, 255, 255)):
    im = Image.new('RGB', (S, S), fill)
    return im, ImageDraw.Draw(im)


def rect(d, x0, y0, x1, y1, fill=None, outline=INK, w=4):
    d.rectangle([x0, y0, x1, y1], fill=fill, outline=outline, width=w if outline else 0)


def glints(d, x0, y0, x1, y1, a=0.35, col=(255, 255, 255), wide=0.16, thin=0.05, gap=0.12):
    """Hit & Run glass: two parallel diagonal streaks, a wide one and a thin one."""
    w, h = x1 - x0, y1 - y0
    for frac, width in ((a, wide), (a + wide + gap, thin)):
        cx = x0 + w * frac
        pts = [(cx, y1), (cx + w * width, y1), (cx + w * width + h * 0.55, y0), (cx + h * 0.55, y0)]
        d.polygon(pts, fill=col)


def clip_glass(im, box, base_top, base_bot, glint=True, glint_col=(236, 246, 255), a=0.18):
    x0, y0, x1, y1 = box
    g = Image.new('RGB', (x1 - x0, y1 - y0))
    gd = ImageDraw.Draw(g)
    for y in range(y1 - y0):
        t = y / max(1, y1 - y0 - 1)
        gd.line([(0, y), (x1 - x0, y)], fill=tuple(int(base_top[k] + (base_bot[k] - base_top[k]) * t) for k in range(3)))
    if glint:
        glints(gd, 0, 0, x1 - x0, y1 - y0, a=a, col=glint_col)
    im.paste(g, (x0, y0))


def grain(im, amount=4, seed=0):
    a = np.asarray(im).astype(np.int16)
    r = np.random.default_rng(seed).integers(-amount, amount + 1, size=a.shape[:2])[..., None]
    return Image.fromarray(np.clip(a + r, 0, 255).astype(np.uint8))


# ------------------------------------------------------------------------------------------- cells
def c_white():
    return cell()[0]


def c_glass():
    im, d = cell()
    clip_glass(im, (0, 0, S, S), (150, 198, 236), (96, 150, 206))
    d.rectangle([0, 0, S, 18], fill=(70, 104, 150))          # the reveal's shadow along the head
    return im


def c_glass_dark():
    im, d = cell()
    clip_glass(im, (0, 0, S, S), (92, 128, 168), (52, 80, 118), glint_col=(150, 182, 214), a=0.3)
    return im


def shop_goods(d, x0, y0, x1, y1, seed):
    r = random.Random(seed)
    d.rectangle([x0, y0, x1, y1], fill=(250, 238, 200))
    shelves = 4
    for s in range(shelves):
        sy = y0 + (y1 - y0) * (s + 1) / (shelves + 1)
        d.rectangle([x0, sy, x1, sy + 8], fill=(140, 96, 60))
        x = x0 + 10
        while x < x1 - 30:
            bw = r.randint(18, 46)
            bh = r.randint(26, 70)
            col = r.choice([(222, 60, 60), (60, 140, 220), (250, 200, 50), (80, 180, 90), (240, 130, 40), (170, 90, 200), (250, 250, 250)])
            d.rectangle([x, sy - bh, x + bw, sy], fill=col, outline=INK, width=3)
            x += bw + r.randint(4, 12)


def c_glass_shop():
    im, d = cell()
    shop_goods(d, 0, 0, S, S, 3)
    over = Image.new('RGB', (S, S), (255, 255, 255))
    od = ImageDraw.Draw(over)
    glints(od, 0, 0, S, S, a=0.25, col=(0, 0, 0))
    m = over.convert('L').point(lambda v: 90 if v < 128 else 0)
    im.paste(Image.new('RGB', (S, S), (235, 245, 255)), (0, 0), m)
    return im


def c_louvre():
    im, d = cell((238, 238, 238))
    for leaf in range(2):
        x0, x1 = leaf * S // 2 + 6, (leaf + 1) * S // 2 - 6
        rect(d, x0, 6, x1, S - 6, fill=(240, 240, 240), w=6)
        rect(d, x0 + 26, 30, x1 - 26, S - 30, fill=(214, 214, 214), w=4)
        y = 40
        while y < S - 40:
            d.line([(x0 + 28, y), (x1 - 28, y)], fill=INK, width=4)
            d.line([(x0 + 28, y + 5), (x1 - 28, y + 5)], fill=(250, 250, 250), width=6)
            y += 26
    return im


def c_panel_shutter():
    im, d = cell((236, 236, 236))
    for leaf in range(2):
        x0, x1 = leaf * S // 2 + 6, (leaf + 1) * S // 2 - 6
        rect(d, x0, 6, x1, S - 6, fill=(240, 240, 240), w=6)
        for k in range(3):
            py0 = 30 + k * (S - 60) / 3 + 8
            py1 = 30 + (k + 1) * (S - 60) / 3 - 8
            rect(d, x0 + 30, py0, x1 - 30, py1, fill=(222, 222, 222), w=4)
            d.line([(x0 + 38, py1 - 6), (x1 - 38, py1 - 6)], fill=(205, 205, 205), width=6)
    return im


def c_roller():
    im, d = cell((206, 208, 212))
    y = 0
    while y < S:
        d.line([(0, y), (S, y)], fill=(150, 152, 160), width=4)
        d.line([(0, y + 4), (S, y + 4)], fill=(232, 234, 238), width=4)
        y += 22
    d.rectangle([0, S - 40, S, S], fill=(160, 162, 170), outline=INK, width=4)
    d.rectangle([S // 2 - 40, S - 30, S // 2 + 40, S - 18], fill=(90, 90, 100))
    for k in range(5):                                        # grime off the pavement
        x = rng.randint(0, S)
        d.rectangle([x, S - 70 - rng.randint(0, 40), x + rng.randint(20, 60), S - 40], fill=(188, 186, 186))
    return im


def c_folding():
    im, d = cell((238, 238, 238))
    leaves = 4
    for k in range(leaves):
        x0, x1 = k * S / leaves + 3, (k + 1) * S / leaves - 3
        rect(d, x0, 4, x1, S - 4, fill=(242, 242, 242), w=5)
        rect(d, x0 + 18, 30, x1 - 18, S * 0.45, fill=(222, 222, 222), w=4)
        rect(d, x0 + 18, S * 0.5, x1 - 18, S - 30, fill=(222, 222, 222), w=4)
    return im


def c_door():
    im, d = cell((236, 236, 236))
    rect(d, 40, 8, S - 40, S, fill=(240, 240, 240), w=6)
    for r in range(2):
        for c in range(2):
            x0 = 70 + c * (S - 140) / 2 + 6
            y0 = 40 + r * (S - 80) / 2 + 6
            rect(d, x0, y0, x0 + (S - 140) / 2 - 12, y0 + (S - 80) / 2 - 12, fill=(222, 222, 222), w=4)
    d.ellipse([S - 110, S // 2 - 12, S - 86, S // 2 + 12], fill=(200, 170, 60), outline=INK, width=3)
    return im


def c_door_glass():
    im, d = cell((150, 154, 160))
    clip_glass(im, (36, 36, S - 36, S - 20), (170, 210, 236), (110, 160, 210))
    d = ImageDraw.Draw(im)
    rect(d, 36, 36, S - 36, S - 20, outline=INK, w=5)
    d.line([(S // 2, 36), (S // 2, S - 20)], fill=(150, 154, 160), width=16)
    d.rectangle([60, S // 2 - 8, S // 2 - 30, S // 2 + 8], fill=(200, 204, 210), outline=INK, width=3)
    d.rectangle([S // 2 + 30, S // 2 - 8, S - 60, S // 2 + 8], fill=(200, 204, 210), outline=INK, width=3)
    f = font('arialbd.ttf', 30)
    d.text((80, S // 2 + 30), 'TOLAK', font=f, fill=(200, 40, 40))
    d.text((S // 2 + 50, S // 2 + 30), 'TARIK', font=f, fill=(200, 40, 40))
    return im


def c_breeze():
    """1960s-70s ventilation blocks: a grid of squares with circle and diamond holes."""
    im, d = cell((240, 240, 240))
    n = 4
    b = S / n
    for r in range(n):
        for c in range(n):
            x0, y0 = c * b, r * b
            rect(d, x0 + 3, y0 + 3, x0 + b - 3, y0 + b - 3, fill=(244, 244, 244), w=4)
            cx, cy = x0 + b / 2, y0 + b / 2
            hole = (64, 62, 72)
            if (r + c) % 2 == 0:
                d.ellipse([cx - b * 0.3, cy - b * 0.3, cx + b * 0.3, cy + b * 0.3], fill=hole)
                d.ellipse([cx - b * 0.1, cy - b * 0.1, cx + b * 0.1, cy + b * 0.1], fill=(244, 244, 244))
            else:
                d.polygon([(cx, cy - b * 0.34), (cx + b * 0.34, cy), (cx, cy + b * 0.34), (cx - b * 0.34, cy)], fill=hole)
                d.polygon([(cx, cy - b * 0.12), (cx + b * 0.12, cy), (cx, cy + b * 0.12), (cx - b * 0.12, cy)], fill=(244, 244, 244))
    return im


def c_grille():
    im, d = cell()
    clip_glass(im, (0, 0, S, S), (84, 112, 150), (54, 80, 118), glint_col=(130, 160, 196))
    d = ImageDraw.Draw(im)
    bar = (246, 242, 232)
    for k in range(1, 6):
        x = k * S / 6
        d.line([(x, 0), (x, S)], fill=bar, width=10)
    d.line([(0, S * 0.5), (S, S * 0.5)], fill=bar, width=10)
    for k in range(6):                                         # the curls Malaysian grilles love
        cx = (k + 0.5) * S / 6
        d.arc([cx - 30, S * 0.5 - 60, cx + 30, S * 0.5], 0, 180, fill=bar, width=8)
        d.arc([cx - 30, S * 0.5, cx + 30, S * 0.5 + 60], 180, 360, fill=bar, width=8)
    rect(d, 0, 0, S - 1, S - 1, outline=bar, w=14)
    return im


def c_ac():
    im, d = cell((228, 230, 232))
    rect(d, 6, 30, S - 6, S - 30, fill=(236, 238, 240), w=6)
    cx, cy, r = S * 0.66, S / 2, S * 0.27
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(70, 72, 80), outline=INK, width=5)
    for k in range(8):
        a = k * math.pi / 4
        d.line([(cx, cy), (cx + math.cos(a) * r, cy + math.sin(a) * r)], fill=(150, 152, 160), width=6)
    for k in range(4):
        rr = r * (k + 1) / 4
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], outline=(150, 152, 160), width=4)
    for y in range(int(S * 0.3), int(S * 0.72), 22):
        d.line([(40, y), (S * 0.34, y)], fill=(160, 162, 170), width=8)
    d.rectangle([60, S - 28, 140, S - 8], fill=(200, 196, 190))           # a drip stain
    return im


def c_tiles():
    """Clay roof tiles in rows: scalloped ends, a shade step under each row."""
    im, d = cell((236, 236, 236))
    rows, cols = 6, 6
    h, w = S / rows, S / cols
    for r in range(rows):
        y = r * h
        d.rectangle([0, y, S, y + h * 0.28], fill=(206, 206, 206))
        for c in range(cols + 1):
            x = c * w + (w / 2 if r % 2 else 0)
            d.arc([x - w / 2, y + h * 0.2, x + w / 2, y + h * 1.25], 180, 360, fill=INK, width=4)
            d.line([(x, y + h * 0.72), (x, y + h)], fill=(196, 196, 196), width=3)
        d.line([(0, y + h - 2), (S, y + h - 2)], fill=INK, width=4)
    return im


def c_zinc():
    im, d = cell((240, 240, 240))
    w = S / 10
    for k in range(10):
        x = k * w
        d.rectangle([x, 0, x + w * 0.3, S], fill=(214, 214, 214))
        d.rectangle([x + w * 0.5, 0, x + w * 0.62, S], fill=(252, 252, 252))
        d.line([(x, 0), (x, S)], fill=(176, 176, 176), width=3)
    for k in range(4):                                          # rust runs
        x = rng.randint(0, S)
        y = rng.randint(0, S - 120)
        d.rectangle([x, y, x + 10, y + rng.randint(40, 140)], fill=(206, 190, 176))
    return im


def c_brick():
    im, d = cell((242, 242, 242))
    rows = 10
    h = S / rows
    for r in range(rows):
        y = r * h
        off = (S / 8) if r % 2 else 0
        for c in range(-1, 5):
            x = c * S / 4 + off
            rect(d, x + 3, y + 3, x + S / 4 - 3, y + h - 3, fill=(228, 228, 228), outline=(170, 170, 170), w=3)
    return im


def c_planks_h():
    """Lapped timber boards: each board darker toward its lower edge."""
    im, d = cell((240, 240, 240))
    n = 8
    h = S / n
    for r in range(n):
        y = r * h
        d.rectangle([0, y + h * 0.7, S, y + h], fill=(220, 220, 220))
        d.line([(0, y + h - 2), (S, y + h - 2)], fill=INK, width=4)
        if rng.random() < 0.5:
            x = rng.randint(30, S - 30)
            d.ellipse([x - 8, y + h * 0.35, x + 8, y + h * 0.55], outline=(190, 190, 190), width=3)
    return im


def c_planks_v():
    im, d = cell((240, 240, 240))
    n = 6
    w = S / n
    for c in range(n):
        x = c * w
        d.rectangle([x + w - 16, 0, x + w, S], fill=(214, 214, 214))
        d.line([(x + w - 16, 0), (x + w - 16, S)], fill=INK, width=4)
    return im


def c_ornament():
    """Plaster relief for pediments: a sunburst medallion with scrolls either side."""
    im, d = cell((240, 240, 240))
    cx, cy = S / 2, S * 0.55
    for k in range(13):
        a = math.pi + k * math.pi / 12
        d.line([(cx, cy), (cx + math.cos(a) * S * 0.36, cy + math.sin(a) * S * 0.36)], fill=(200, 200, 200), width=10)
    d.pieslice([cx - S * 0.38, cy - S * 0.38, cx + S * 0.38, cy + S * 0.38], 180, 360, outline=INK, width=6)
    d.ellipse([cx - 50, cy - 50, cx + 50, cy + 50], fill=(226, 226, 226), outline=INK, width=6)
    for sx in (-1, 1):
        x = cx + sx * S * 0.32
        d.arc([x - 60, cy - 10, x + 60, cy + 110], 0 if sx > 0 else 180, 180 if sx > 0 else 360, fill=INK, width=6)
    d.line([(20, cy + 2), (S - 20, cy + 2)], fill=INK, width=6)
    return im


def c_rail():
    """A balcony railing read as one panel: rails and balusters against a dark gap."""
    im, d = cell((70, 72, 82))
    d.rectangle([0, 0, S, 34], fill=(242, 242, 242), outline=INK, width=4)
    d.rectangle([0, S - 30, S, S], fill=(242, 242, 242), outline=INK, width=4)
    for k in range(9):
        x = k * S / 8
        d.rectangle([x - 9, 30, x + 9, S - 30], fill=(242, 242, 242), outline=INK, width=3)
    return im


def c_curtain():
    im, d = cell()
    for r in range(2):
        for c in range(2):
            x0, y0 = c * S // 2, r * S // 2
            clip_glass(im, (x0, y0, x0 + S // 2, y0 + S // 2), (124, 168, 214), (74, 116, 168), a=0.1 + 0.3 * ((r + c) % 2))
    d = ImageDraw.Draw(im)
    for k in (0, S // 2, S - 1):
        d.line([(k, 0), (k, S)], fill=(58, 64, 76), width=12)
        d.line([(0, k), (S, k)], fill=(58, 64, 76), width=12)
    return im


def c_kerawang():
    """Malay carved timber panel: a band of leafy cut-outs over doors and windows."""
    im, d = cell((240, 240, 240))
    hole = (70, 58, 50)
    for k in range(4):
        cx = (k + 0.5) * S / 4
        cy = S / 2
        d.ellipse([cx - 22, cy - 22, cx + 22, cy + 22], fill=hole)
        for a in range(4):
            ang = a * math.pi / 2 + math.pi / 4
            px, py = cx + math.cos(ang) * 52, cy + math.sin(ang) * 52
            d.polygon([(cx + math.cos(ang) * 26, cy + math.sin(ang) * 26),
                       (px + math.cos(ang + 1.3) * 22, py + math.sin(ang + 1.3) * 22),
                       (px + math.cos(ang) * 18, py + math.sin(ang) * 18),
                       (px + math.cos(ang - 1.3) * 22, py + math.sin(ang - 1.3) * 22)], fill=hole)
    rect(d, 4, 4, S - 4, S - 4, outline=INK, w=8)
    return im


def c_vent():
    im, d = cell((236, 236, 236))
    rect(d, 6, 6, S - 6, S - 6, fill=(220, 220, 220), w=6)
    for y in range(40, S - 30, 34):
        d.polygon([(30, y), (S - 30, y), (S - 30, y + 18), (30, y + 26)], fill=(244, 244, 244), outline=INK)
    return im


def c_concrete():
    """Off-white render with a few flat drips under the sills (Hit & Run's grime, kept sparse)."""
    im, d = cell((244, 244, 244))
    for k in range(5):
        x = rng.randint(10, S - 60)
        w = rng.randint(16, 50)
        h = rng.randint(60, 220)
        d.rectangle([x, 0, x + w, h], fill=(232, 232, 232))
        d.ellipse([x, h - w / 2, x + w, h + w / 2], fill=(232, 232, 232))
    return im


SIGN_STYLES = [  # board, text, edge
    ((24, 24, 28), (238, 196, 70), (238, 196, 70)),
    ((178, 30, 34), (250, 214, 90), (250, 214, 90)),
    ((22, 104, 66), (250, 250, 240), (250, 214, 90)),
    ((250, 214, 60), (190, 30, 30), (190, 30, 30)),
    ((248, 246, 238), (30, 70, 150), (30, 70, 150)),
    ((30, 70, 150), (250, 250, 250), (250, 214, 90)),
    ((236, 120, 40), (255, 255, 255), (120, 40, 20)),
    ((120, 30, 80), (255, 230, 120), (255, 230, 120)),
]

SIGNS = [
    ('大华茶室', 'KEDAI KOPI TAI WAH'),
    ('永兴五金', 'KEDAI PERKAKAS WING HING'),
    ('福安药行', 'KEDAI UBAT FOOK ON'),
    ('和记布庄', 'KEDAI KAIN WOH KEE'),
    (None, 'RESTORAN SELERA KITA'),
    (None, 'KEDAI RUNCIT AH SENG'),
    ('新记饭店', 'RESTORAN SUN KEE'),
    ('万兴金铺', 'KEDAI EMAS MAN HING'),
    (None, 'FARMASI SEJAHTERA'),
    (None, 'NASI KANDAR BESTARI'),
    (None, 'KEDAI GUNTING RAMBUT'),
    ('合成杂货', 'KEDAI RUNCIT HUP SENG'),
    (None, 'PERABOT MURAH'),
    (None, 'KEDAI JAHIT LAILA'),
    ('南兴酒家', 'RESTORAN NAM HING'),
    (None, 'TEKSTIL MURNI'),
]


def sign(d, x0, y0, x1, y1, han, rumi, style):
    board, text, edge = style
    d.rectangle([x0, y0, x1, y1], fill=board, outline=INK, width=4)
    d.rectangle([x0 + 8, y0 + 8, x1 - 8, y1 - 8], outline=edge, width=4)
    w, h = x1 - x0, y1 - y0
    if han:
        f = font('msyhbd.ttc', int(h * 0.56))
        tw = d.textlength(han, font=f)
        sp = (w * 0.84 - tw) / max(1, len(han) - 1)
        x = x0 + w * 0.08
        for ch in han:                                          # spread the characters across the board
            d.text((x + 3, y0 + h * 0.04 + 3), ch, font=f, fill=INK)
            d.text((x, y0 + h * 0.04), ch, font=f, fill=text)
            x += d.textlength(ch, font=f) + sp
        f2 = font('arialbd.ttf', int(h * 0.17))
        tw2 = d.textlength(rumi, font=f2)
        d.text((x0 + (w - tw2) / 2, y1 - h * 0.27), rumi, font=f2, fill=text)
    else:
        size = int(h * 0.5)
        f = font('impact.ttf', size)
        while d.textlength(rumi, font=f) > w * 0.88 and size > 10:
            size -= 2
            f = font('impact.ttf', size)
        tw = d.textlength(rumi, font=f)
        tx, ty = x0 + (w - tw) / 2, y0 + (h - size * 1.2) / 2
        d.text((tx + 4, ty + 4), rumi, font=f, fill=INK)
        d.text((tx, ty), rumi, font=f, fill=text)


def c_signs(k):
    im, d = cell()
    for j in range(4):
        han, rumi = SIGNS[(k * 4 + j) % len(SIGNS)]
        style = SIGN_STYLES[(k * 4 + j * 3) % len(SIGN_STYLES)]
        sign(d, 0, j * S // 4, S - 1, (j + 1) * S // 4 - 1, han, rumi, style)
    return im


PLAQUES = ['1928', '1931', '1936', '1948', '1952', '1957', '1963', '1926']


def c_plaques():
    """Eight year plaques (2 across x 4 down), each in its own moulded frame, for pediments."""
    im, d = cell((240, 240, 240))
    f = font('georgiab.ttf', 84)
    for j in range(4):
        y0 = j * S // 4
        for half in range(2):
            x0 = half * S // 2
            rect(d, x0 + 8, y0 + 8, x0 + S // 2 - 8, y0 + S // 4 - 8, fill=(232, 232, 232), w=5)
            t = PLAQUES[j * 2 + half]
            tw = d.textlength(t, font=f)
            x = x0 + (S // 2 - tw) / 2
            d.text((x + 3, y0 + 18 + 3), t, font=f, fill=(150, 150, 150))
            d.text((x, y0 + 18), t, font=f, fill=INK)
    return im


def c_kopitiam():
    im, d = cell((250, 240, 214))
    d.rectangle([0, 0, S, S * 0.28], fill=(240, 226, 190))
    for k in range(3):                                          # menu boards
        x = 30 + k * 160
        d.rectangle([x, 30, x + 130, 110], fill=(30, 60, 40), outline=INK, width=4)
        for l in range(3):
            d.line([(x + 14, 52 + l * 20), (x + 110, 52 + l * 20)], fill=(240, 240, 220), width=5)
    for k in range(2):                                          # round marble tables, stools
        cx = 130 + k * 250
        d.ellipse([cx - 80, S * 0.62, cx + 80, S * 0.72], fill=(246, 246, 246), outline=INK, width=4)
        d.rectangle([cx - 8, S * 0.67, cx + 8, S * 0.95], fill=(120, 80, 50))
        for sx in (-110, 110):
            d.ellipse([cx + sx - 26, S * 0.8, cx + sx + 26, S * 0.86], fill=(200, 50, 50), outline=INK, width=3)
            d.rectangle([cx + sx - 5, S * 0.83, cx + sx + 5, S * 0.97], fill=(90, 70, 50))
    d.ellipse([S / 2 - 70, 120, S / 2 + 70, 150], outline=INK, width=5)   # ceiling fan
    return im


def c_hardware():
    im, d = cell((236, 226, 206))
    r = random.Random(7)
    for y in (60, 200, 340):
        d.line([(0, y), (S, y)], fill=(120, 90, 60), width=6)
        x = 20
        while x < S - 40:
            kind = r.randint(0, 2)
            col = r.choice([(220, 60, 50), (60, 120, 200), (250, 190, 40), (80, 160, 80), (170, 170, 180)])
            if kind == 0:                                       # a bucket
                d.polygon([(x, y + 20), (x + 50, y + 20), (x + 42, y + 90), (x + 8, y + 90)], fill=col, outline=INK)
            elif kind == 1:                                     # a coil of hose
                d.ellipse([x, y + 14, x + 60, y + 74], outline=col, width=12)
            else:                                               # brooms
                d.line([(x + 10, y + 10), (x + 10, y + 110)], fill=(150, 110, 70), width=8)
                d.polygon([(x - 6, y + 110), (x + 26, y + 110), (x + 34, y + 130), (x - 14, y + 130)], fill=col, outline=INK)
            x += 70
    return im


def c_textile():
    im, d = cell((240, 232, 216))
    r = random.Random(11)
    for row in range(3):
        y = 40 + row * 150
        x = 10
        while x < S - 20:
            col = r.choice([(200, 40, 70), (40, 110, 190), (240, 180, 40), (70, 160, 110), (150, 70, 170), (240, 120, 150), (250, 250, 250)])
            w = r.randint(36, 60)
            d.rectangle([x, y, x + w, y + 120], fill=col, outline=INK, width=3)
            d.line([(x + w * 0.5, y), (x + w * 0.5, y + 120)], fill=tuple(max(0, v - 30) for v in col), width=4)
            x += w + 6
    return im


def c_goldshop():
    im, d = cell((120, 26, 30))
    for row in range(2):
        y = 80 + row * 200
        d.rectangle([20, y, S - 20, y + 140], fill=(250, 246, 232), outline=INK, width=4)
        for k in range(7):
            x = 50 + k * 62
            d.ellipse([x, y + 30, x + 36, y + 66], outline=(232, 186, 50), width=7)
            d.line([(x + 18, y + 70), (x + 18, y + 120)], fill=(232, 186, 50), width=6)
    return im


def c_awning(col):
    im, d = cell((250, 250, 248))
    w = S / 8
    for k in range(0, 8, 2):
        d.rectangle([k * w, 0, (k + 1) * w, S], fill=col)
    d.rectangle([0, S - 70, S, S], fill=col)
    for k in range(8):                                          # the scalloped valance
        d.pieslice([k * w, S - 100, (k + 1) * w, S - 40], 0, 180, fill=(250, 250, 248) if k % 2 else col)
    d.line([(0, S - 70), (S, S - 70)], fill=INK, width=4)
    return im


def c_curtained():
    im = c_glass()
    d = ImageDraw.Draw(im)
    for side in (0, 1):
        x0 = 0 if side == 0 else S - 150
        d.rectangle([x0, 0, x0 + 150, S], fill=(236, 218, 170))
        for k in range(5):
            x = x0 + 15 + k * 28
            d.line([(x, 0), (x, S)], fill=(210, 186, 130), width=8)
    return im


def c_lit():
    im, d = cell((255, 214, 120))
    d.rectangle([0, 0, S, S * 0.12], fill=(240, 180, 90))
    d.ellipse([S / 2 - 40, 30, S / 2 + 40, 80], fill=(255, 250, 220))
    return im


def c_attap():
    """Nipa-palm thatch: overlapping fringed rows."""
    im, d = cell((236, 236, 236))
    rows = 7
    h = S / rows
    for r in range(rows):
        y = r * h
        for x in range(0, S, 10):
            d.line([(x, y), (x + rng.randint(-4, 4), y + h * 1.05)], fill=(200, 200, 200), width=3)
        d.line([(0, y + h - 3), (S, y + h - 3)], fill=INK, width=5)
    return im


def c_posters():
    im, d = cell((236, 232, 222))
    f2 = font('arialbd.ttf', 26)
    items = [('DIJUAL', (220, 40, 40)), ('DISEWA', (30, 90, 170)), ('PINJAMAN?', (40, 140, 60)), ('SEDOT TANDAS', (240, 140, 20))]
    for k, (t, col) in enumerate(items):
        x0 = 20 + (k % 2) * 250
        y0 = 20 + (k // 2) * 250
        d.rectangle([x0, y0, x0 + 220, y0 + 220], fill=(255, 252, 240), outline=INK, width=4)
        size = 46
        f = font('impact.ttf', size)
        while d.textlength(t, font=f) > 196 and size > 16:
            size -= 2
            f = font('impact.ttf', size)
        d.text((x0 + 14, y0 + 20), t, font=f, fill=col)
        d.text((x0 + 14, y0 + 140), '012-' + str(3000000 + k * 1234567)[-7:], font=f2, fill=INK)
    return im


def c_billboard(k):
    texts = [('TEH TARIK BOS', (190, 30, 30), (250, 214, 60)), ('KOPI JANTAN', (40, 30, 26), (240, 200, 120))]
    t, bg, fg = texts[k]
    im, d = cell(bg)
    f = font('impact.ttf', 92)
    tw = d.textlength(t, font=f)
    while tw > S * 0.92:
        f = font('impact.ttf', f.size - 4)
        tw = d.textlength(t, font=f)
    d.text(((S - tw) / 2 + 5, S * 0.32 + 5), t, font=f, fill=INK)
    d.text(((S - tw) / 2, S * 0.32), t, font=f, fill=fg)
    d.text((40, S * 0.72), 'SEDAP GILA!', font=font('arialbd.ttf', 40), fill=(255, 255, 255))
    d.rectangle([0, 0, S - 1, S - 1], outline=INK, width=8)
    return im


def c_steps():
    im, d = cell((236, 236, 236))
    cols = [(60, 140, 200), (240, 240, 240), (230, 90, 80), (240, 240, 240)]
    for r in range(4):
        for c in range(4):
            x0, y0 = c * S / 4, r * S / 4
            d.rectangle([x0, y0, x0 + S / 4, y0 + S / 4], fill=cols[(r + c) % 4], outline=INK, width=3)
            d.ellipse([x0 + 30, y0 + 30, x0 + S / 4 - 30, y0 + S / 4 - 30], outline=(250, 250, 250), width=5)
    return im


FAR_GLASS = (74, 88, 108)


def c_win_far():
    """Distance version of a storey: one window in its bay (the wall is tinted, the glass reads dark)."""
    im, d = cell()
    d.rectangle([S * 0.3, S * 0.22, S * 0.7, S * 0.8], fill=FAR_GLASS)
    d.polygon([(S * 0.4, S * 0.8), (S * 0.5, S * 0.8), (S * 0.62, S * 0.22), (S * 0.52, S * 0.22)], fill=(118, 136, 156))
    d.rectangle([S * 0.26, S * 0.8, S * 0.74, S * 0.86], fill=(236, 236, 236))
    d.rectangle([S * 0.3, S * 0.22, S * 0.7, S * 0.27], fill=(52, 60, 74))
    return im


def c_ribbon_far():
    """Distance version of a storey of ribbon windows between concrete bands."""
    im, d = cell()
    d.rectangle([0, S * 0.18, S, S * 0.74], fill=FAR_GLASS)
    for k in range(5):
        d.rectangle([k * S / 4 - 6, S * 0.18, k * S / 4 + 6, S * 0.74], fill=(60, 66, 78))
    d.polygon([(S * 0.1, S * 0.74), (S * 0.2, S * 0.74), (S * 0.34, S * 0.18), (S * 0.24, S * 0.18)], fill=(118, 136, 156))
    return im


def c_shop_far():
    """Distance version of a shop front under its five-foot way: a signboard and the lit shop."""
    im, d = cell((70, 64, 70))
    d.rectangle([0, 0, S, S * 0.2], fill=(186, 40, 38))
    for k in range(4):
        d.rectangle([40 + k * 110, S * 0.05, 110 + k * 110, S * 0.15], fill=(246, 206, 80))
    d.rectangle([S * 0.08, S * 0.32, S * 0.92, S * 0.98], fill=(232, 210, 160))
    shop_goods(d, int(S * 0.12), int(S * 0.36), int(S * 0.88), int(S * 0.96), 9)
    d.rectangle([0, S * 0.2, S * 0.06, S], fill=(236, 236, 236))
    d.rectangle([S * 0.94, S * 0.2, S, S], fill=(236, 236, 236))
    return im


def wrapped(d, box, draw):
    """Draw a shape at box and wherever it wraps round the tile, so the cell tiles seamlessly."""
    x0, y0, x1, y1 = box
    for ox in (-S, 0, S):
        for oy in (-S, 0, S):
            if x1 + ox < 0 or x0 + ox > S or y1 + oy < 0 or y0 + oy > S: continue
            draw(d, (x0 + ox, y0 + oy, x1 + ox, y1 + oy))


def c_leaves():
    """Foliage for tree crowns (tinted by the vertex colour): overlapping clusters of leaves, lit from above,
    each leaf a flat almond with a darker edge. Tiles seamlessly."""
    im, d = cell((206, 206, 206))
    r = random.Random(21)
    for k in range(260):
        cx, cy = r.uniform(0, S), r.uniform(0, S)
        ln, wd = r.uniform(26, 46), r.uniform(12, 20)
        a = r.uniform(0, math.pi)
        tone = r.choice([176, 196, 214, 232, 246, 250])
        pts = []
        for t in range(12):
            u = t / 11 * 2 * math.pi
            px, py = math.cos(u) * ln / 2, math.sin(u) * wd / 2 * (1 - 0.35 * math.cos(u))
            pts.append((cx + px * math.cos(a) - py * math.sin(a), cy + px * math.sin(a) + py * math.cos(a)))
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]

        def leaf(dd, b, pts=pts, tone=tone, xs=xs, ys=ys):
            ox, oy = b[0] - min(xs), b[1] - min(ys)
            q = [(x + ox, y + oy) for x, y in pts]
            dd.polygon(q, fill=(tone, tone, tone), outline=(140, 140, 140))
            mx = sum(x for x, _ in q) / len(q); my = sum(y for _, y in q) / len(q)
            dd.line([q[0], (mx, my), q[6]], fill=(max(0, tone - 40),) * 3, width=2)
        wrapped(d, (min(xs), min(ys), max(xs), max(ys)), leaf)
    return im


def c_frond():
    """A palm frond along u (base at u = 0), v across: leaflets sweeping forward from the midrib to both edges."""
    im, d = cell((96, 96, 96))
    d.rectangle([0, S * 0.47, S, S * 0.53], fill=(214, 206, 170))
    for k in range(34):
        x = k * S / 32 - 10
        for side in (-1, 1):
            tip = (x + S * 0.16, S * 0.5 + side * S * 0.49)
            d.polygon([(x, S * 0.5 + side * S * 0.02), (x + 10, S * 0.5 + side * S * 0.02), (tip[0] + 6, tip[1]), (tip[0] - 4, tip[1])],
                      fill=(236, 236, 236), outline=(150, 150, 150))
    return im


def c_bark():
    """Rain-tree bark: long vertical fissures, a lighter ridge each side."""
    im, d = cell((214, 214, 214))
    r = random.Random(5)
    for k in range(40):
        x = r.uniform(0, S)
        y0, ln = r.uniform(0, S), r.uniform(60, 220)
        w = r.uniform(4, 9)
        for oy in (-S, 0, S):
            d.rectangle([x, y0 + oy, x + w, y0 + ln + oy], fill=(150, 150, 150))
            d.rectangle([x + w, y0 + oy, x + w + 3, y0 + ln + oy], fill=(236, 236, 236))
    return im


def c_palm_trunk():
    """Coconut trunk: the rings the old fronds leave, close together, rough between."""
    im, d = cell((206, 206, 206))
    r = random.Random(8)
    for k in range(12):
        y = k * S / 12 + r.uniform(-3, 3)
        d.rectangle([0, y, S, y + 9], fill=(150, 150, 150))
        d.rectangle([0, y + 9, S, y + 14], fill=(236, 236, 236))
    for k in range(30):
        x, y = r.uniform(0, S), r.uniform(0, S)
        d.line([(x, y), (x + r.uniform(-6, 6), y + r.uniform(10, 26))], fill=(178, 178, 178), width=3)
    return im


def c_banana():
    """A banana leaf along v (stalk at v = 0): pale midrib, fine veins out to the edges, the wind's tears."""
    im, d = cell((222, 222, 222))
    d.rectangle([S * 0.47, 0, S * 0.53, S], fill=(250, 250, 236))
    for k in range(40):
        y = k * S / 38
        for side in (-1, 1):
            d.line([(S * 0.5, y), (S * 0.5 + side * S * 0.5, y + S * 0.18)], fill=(196, 196, 196), width=3)
    r = random.Random(3)
    for k in range(7):                                         # tears through the blade
        y = r.uniform(0, S)
        side = r.choice((-1, 1))
        d.line([(S * 0.5 + side * S * 0.08, y), (S * 0.5 + side * S * 0.5, y + S * 0.18)], fill=(70, 80, 60), width=4)
    return im


def blossoms(colour, centre, n, seed):
    im, d = cell((84, 140, 70))
    r = random.Random(seed)
    for k in range(160):                                       # leaves under the flowers
        x, y = r.uniform(0, S), r.uniform(0, S)
        tone = r.choice([(70, 124, 58), (100, 160, 80), (120, 176, 92)])
        wrapped(d, (x - 14, y - 9, x + 14, y + 9), lambda dd, b, tone=tone: dd.ellipse(b, fill=tone, outline=(50, 90, 44)))
    for k in range(n):
        x, y = r.uniform(0, S), r.uniform(0, S)
        for p in range(5):
            a = p * 2 * math.pi / 5 + r.uniform(0, 1)
            px, py = x + math.cos(a) * 9, y + math.sin(a) * 9
            wrapped(d, (px - 8, py - 8, px + 8, py + 8), lambda dd, b: dd.ellipse(b, fill=colour, outline=tuple(int(c * 0.7) for c in colour)))
        wrapped(d, (x - 4, y - 4, x + 4, y + 4), lambda dd, b: dd.ellipse(b, fill=centre))
    return im


def c_hazard():
    """Kerb paint: alternating yellow and black blocks, the way KL marks its flyover and junction kerbs."""
    im, d = cell((250, 206, 40))
    for k in range(0, 8, 2):
        d.rectangle([k * S / 8, 0, (k + 1) * S / 8, S], fill=(34, 32, 36))
    return im


ROADSIGNS = [('PUSAT BANDAR', 'CITY CENTRE'), ('KLCC', 'JALAN AMPANG'), ('PETALING JAYA', 'FEDERAL HIGHWAY'), ('SENTUL', 'JALAN IPOH'),
             ('JALAN TUN RAZAK', 'TITIWANGSA'), ('CHERAS', 'JALAN LOKE YEW'), ('KL SENTRAL', 'BRICKFIELDS'), ('BANGSAR', 'JALAN DAMANSARA')]


def c_roadsigns(k):
    """Overhead direction signs: white on motorway green, an arrow, two lines each (four signs a cell)."""
    im, d = cell((40, 120, 70))
    f1, f2 = font('arialbd.ttf', 40), font('arialbd.ttf', 24)
    for j in range(4):
        top, bot = ROADSIGNS[(k * 4 + j) % len(ROADSIGNS)]
        y0 = j * S // 4
        d.rectangle([4, y0 + 4, S - 5, y0 + S // 4 - 5], fill=(32, 112, 64), outline=(250, 250, 250), width=5)
        d.text((24, y0 + 14), top, font=f1, fill=(255, 255, 255))
        d.text((24, y0 + 64), bot, font=f2, fill=(255, 255, 255))
        ax = S - 70
        d.polygon([(ax, y0 + 96), (ax, y0 + 50), (ax - 14, y0 + 50), (ax + 10, y0 + 22), (ax + 34, y0 + 50), (ax + 20, y0 + 50), (ax + 20, y0 + 96)],
                  fill=(255, 255, 255))
    return im


CELLS = [
    # row 0
    ('WHITE', c_white), ('GLASS', c_glass), ('GLASS_DARK', c_glass_dark), ('GLASS_SHOP', c_glass_shop),
    ('LOUVRE', c_louvre), ('PANEL_SHUTTER', c_panel_shutter), ('ROLLER', c_roller), ('FOLDING', c_folding),
    # row 1
    ('DOOR', c_door), ('DOOR_GLASS', c_door_glass), ('BREEZE', c_breeze), ('GRILLE', c_grille),
    ('AC', c_ac), ('TILES', c_tiles), ('ZINC', c_zinc), ('BRICK', c_brick),
    # row 2
    ('PLANKS_H', c_planks_h), ('PLANKS_V', c_planks_v), ('ORNAMENT', c_ornament), ('RAIL', c_rail),
    ('CURTAIN', c_curtain), ('KERAWANG', c_kerawang), ('VENT', c_vent), ('CONCRETE', c_concrete),
    # row 3
    ('SIGNS0', lambda: c_signs(0)), ('SIGNS1', lambda: c_signs(1)), ('SIGNS2', lambda: c_signs(2)), ('SIGNS3', lambda: c_signs(3)),
    ('PLAQUES', c_plaques), ('KOPITIAM', c_kopitiam), ('HARDWARE', c_hardware), ('TEXTILE', c_textile),
    # row 4
    ('GOLDSHOP', c_goldshop), ('CURTAINED', c_curtained), ('LIT', c_lit), ('AWNING_RED', lambda: c_awning((206, 46, 46))),
    ('AWNING_GREEN', lambda: c_awning((40, 140, 80))), ('AWNING_BLUE', lambda: c_awning((40, 100, 190))),
    ('AWNING_ORANGE', lambda: c_awning((236, 128, 32))), ('ATTAP', c_attap),
    # row 5
    ('POSTERS', c_posters), ('BILLBOARD0', lambda: c_billboard(0)), ('BILLBOARD1', lambda: c_billboard(1)), ('STEPS', c_steps),
    ('WIN_FAR', c_win_far), ('RIBBON_FAR', c_ribbon_far), ('SHOP_FAR', c_shop_far),
    # row 6: plants
    ('LEAVES', c_leaves), ('FROND', c_frond), ('BARK', c_bark), ('PALM_TRUNK', c_palm_trunk), ('BANANA', c_banana),
    ('BOUGAINVILLEA', lambda: blossoms((236, 62, 150), (255, 240, 200), 70, 31)),
    ('FRANGIPANI', lambda: blossoms((252, 250, 240), (250, 214, 80), 50, 32)),
    # bridges and roads
    ('HAZARD', c_hazard), ('ROADSIGNS0', lambda: c_roadsigns(0)), ('ROADSIGNS1', lambda: c_roadsigns(1)),
]


def main():
    sheet = Image.new('RGB', (S * N, S * N), (255, 0, 255))
    for i, (name, fn) in enumerate(CELLS):
        im = fn().convert('RGB')
        if name != 'WHITE':
            im = grain(im, 3, i)
        sheet.paste(im, ((i % N) * S, (i // N) * S))
    sheet = sheet.resize((256 * N, 256 * N), Image.LANCZOS)
    sheet.save(OUT)
    print(OUT, len(CELLS), 'cells')
    names = ', '.join(f'{n}' for n, _ in CELLS)
    print('enum order:', names)


if __name__ == '__main__':
    main()
