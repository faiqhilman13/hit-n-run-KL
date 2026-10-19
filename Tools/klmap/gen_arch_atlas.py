"""
The painted detail textures of the architecture library (Scripts/World/Arch): one 2048 x 3072 sheet of
8 x 12 cells of 256 px, imported by Unity as a Texture2DArray (one slice per cell, top-left first), so
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


# ------------------------------------------------------------------------------------- interiors (Interior.cs)
PRODUCTS = [(222, 60, 60), (60, 140, 220), (250, 200, 50), (80, 180, 90), (240, 130, 40), (170, 90, 200), (250, 250, 250), (240, 110, 150)]


def c_goods():
    """Shelf fronts for walk-in shops, two strips (top half groceries: packets, tins, bottles, rice; bottom
    half snacks and sundries), each strip two rows of goods standing on a shelf edge. The shelf is modelled."""
    im, d = cell((248, 240, 220))
    r = random.Random(57)
    for strip in range(2):
        y0, y1 = strip * S // 2, (strip + 1) * S // 2
        for row in range(2):
            ry1 = y0 + (y1 - y0) * (row + 1) // 2 - 8
            x = 6
            while x < S - 24:
                kind = r.randint(0, 3)
                col = r.choice(PRODUCTS)
                if kind == 0:                                   # a box or packet
                    bw, bh = r.randint(26, 44), r.randint(60, 100)
                    d.rectangle([x, ry1 - bh, x + bw, ry1], fill=col, outline=INK, width=3)
                    d.rectangle([x + 5, ry1 - bh + 12, x + bw - 5, ry1 - bh + 28], fill=(255, 250, 235))
                elif kind == 1:                                 # two tins
                    bw, bh = r.randint(22, 30), r.randint(36, 50)
                    for k in range(2):
                        xx = x + k * (bw + 3)
                        d.rectangle([xx, ry1 - bh, xx + bw, ry1], fill=col, outline=INK, width=3)
                        d.line([(xx, ry1 - bh * 0.6), (xx + bw, ry1 - bh * 0.6)], fill=(255, 255, 255), width=4)
                    bw = bw * 2 + 3
                elif kind == 2:                                 # a bottle (kicap, sauce, oil)
                    bw, bh = r.randint(20, 26), r.randint(80, 106)
                    d.rectangle([x, ry1 - bh * 0.7, x + bw, ry1], fill=col, outline=INK, width=3)
                    d.rectangle([x + bw * 0.3, ry1 - bh, x + bw * 0.7, ry1 - bh * 0.7], fill=col, outline=INK, width=3)
                    d.rectangle([x + 3, ry1 - bh * 0.5, x + bw - 3, ry1 - bh * 0.3], fill=(255, 250, 235))
                else:                                           # a sack of rice / a bag of keropok
                    bw, bh = r.randint(40, 60), r.randint(54, 84)
                    d.rounded_rectangle([x, ry1 - bh, x + bw, ry1], radius=10, fill=col if strip else (236, 226, 196), outline=INK, width=3)
                    d.ellipse([x + bw * 0.25, ry1 - bh * 0.7, x + bw * 0.75, ry1 - bh * 0.3], fill=(255, 255, 255) if strip else col)
                x += bw + r.randint(3, 8)
            d.rectangle([0, ry1, S, ry1 + 8], fill=(150, 104, 64))
    return im


def c_fridge():
    """A drinks fridge door: cans and bottles on wire shelves behind glass, a red header."""
    im, d = cell((236, 236, 240))
    d.rectangle([0, 0, S, 70], fill=(206, 36, 40))
    f = font('impact.ttf', 50)
    t = 'MINUMAN SEJUK'
    d.text(((S - d.textlength(t, font=f)) / 2, 6), t, font=f, fill=(255, 255, 255))
    clip_glass(im, (24, 84, S - 24, S - 24), (200, 228, 246), (150, 196, 230), glint=False)
    r = random.Random(58)
    for k in range(4):
        y = int(84 + (k + 1) * (S - 108) / 4)
        x = 34
        while x < S - 60:
            col = r.choice(PRODUCTS)
            if r.random() < 0.5:
                d.rectangle([x, y - 50, x + 22, y - 4], fill=col, outline=INK, width=3)
            else:
                d.rectangle([x, y - 66, x + 22, y - 4], fill=col, outline=INK, width=3)
                d.rectangle([x + 7, y - 80, x + 15, y - 66], fill=col, outline=INK, width=2)
            x += 26
        d.line([(24, y), (S - 24, y)], fill=(180, 186, 196), width=5)
    glints(d, 24, 84, S - 24, S - 24, a=0.2, col=(236, 246, 255), wide=0.08, thin=0.03)
    d.rectangle([S - 46, 200, S - 34, 330], fill=(170, 170, 176), outline=INK, width=3)   # the handle
    d.rectangle([20, 80, S - 20, S - 20], outline=INK, width=6)
    return im


def c_photos():
    """Four framed pictures (2 x 2) for a Malay home's walls: the wedding portrait, a graduation, the Kaabah,
    and a calligraphy panel."""
    im, d = cell((236, 226, 200))
    h = S // 2
    frames = [(150, 104, 60), (190, 150, 60), (40, 40, 44), (120, 70, 40)]
    for k in range(4):
        x0, y0 = (k % 2) * h, (k // 2) * h
        d.rectangle([x0 + 6, y0 + 6, x0 + h - 6, y0 + h - 6], fill=frames[k], outline=INK, width=4)
        ix0, iy0, ix1, iy1 = x0 + 26, y0 + 26, x0 + h - 26, y0 + h - 26
        if k == 0:                                              # the wedding: two figures in songket
            d.rectangle([ix0, iy0, ix1, iy1], fill=(176, 214, 230))
            for cx, col, hat in ((ix0 + 66, (236, 196, 70), True), (ix1 - 66, (236, 120, 160), False)):
                d.ellipse([cx - 26, iy0 + 40, cx + 26, iy0 + 92], fill=(214, 170, 130), outline=INK, width=3)
                if hat:
                    d.rectangle([cx - 28, iy0 + 30, cx + 28, iy0 + 52], fill=(40, 40, 44))
                else:
                    d.chord([cx - 30, iy0 + 30, cx + 30, iy0 + 90], 180, 360, fill=(250, 246, 236))
                d.polygon([(cx - 50, iy1), (cx - 30, iy0 + 96), (cx + 30, iy0 + 96), (cx + 50, iy1)], fill=col, outline=INK)
        elif k == 1:                                            # a graduation: mortarboard, scroll
            d.rectangle([ix0, iy0, ix1, iy1], fill=(120, 150, 196))
            cx = (ix0 + ix1) // 2
            d.ellipse([cx - 32, iy0 + 50, cx + 32, iy0 + 112], fill=(214, 170, 130), outline=INK, width=3)
            d.polygon([(cx - 56, iy0 + 46), (cx, iy0 + 26), (cx + 56, iy0 + 46), (cx, iy0 + 64)], fill=(30, 30, 34))
            d.polygon([(cx - 70, iy1), (cx - 44, iy0 + 116), (cx + 44, iy0 + 116), (cx + 70, iy1)], fill=(30, 30, 34))
            d.rectangle([cx + 20, iy1 - 56, cx + 80, iy1 - 40], fill=(250, 246, 230), outline=INK, width=2)
        elif k == 2:                                            # the Kaabah under a night sky
            d.rectangle([ix0, iy0, ix1, iy1], fill=(36, 50, 96))
            d.rectangle([ix0, iy1 - 50, ix1, iy1], fill=(220, 214, 200))
            cx = (ix0 + ix1) // 2
            d.rectangle([cx - 52, iy0 + 64, cx + 52, iy1 - 40], fill=(22, 22, 24))
            d.rectangle([cx - 52, iy0 + 84, cx + 52, iy0 + 96], fill=(214, 176, 70))
        else:                                                   # calligraphy, gold on green
            d.rectangle([ix0, iy0, ix1, iy1], fill=(26, 96, 60))
            cx, cy = (ix0 + ix1) // 2, (iy0 + iy1) // 2
            d.ellipse([cx - 70, cy - 70, cx + 70, cy + 70], outline=(232, 196, 90), width=5)
            for a in range(5):
                x = cx - 48 + a * 24
                d.arc([x - 14, cy - 40, x + 14, cy + 30], 200, 340, fill=(232, 196, 90), width=6)
                d.line([(x, cy - 44), (x, cy + 10)], fill=(232, 196, 90), width=5)
    return im


def c_screens():
    """Four little pictures (2 x 2): a TV showing a cartoon, a TV showing football, a calendar, a drinks menu."""
    im, d = cell((30, 30, 34))
    h = S // 2
    # the news: a presenter at the desk, a map behind, the ticker along the bottom
    x0, y0 = 0, 0
    d.rectangle([x0 + 8, y0 + 8, x0 + h - 8, y0 + h - 8], fill=(40, 90, 170))
    d.ellipse([x0 + 140, y0 + 30, x0 + 236, y0 + 126], fill=(70, 140, 210), outline=(150, 200, 240), width=4)
    d.rectangle([x0 + 40, y0 + 150, x0 + 150, y0 + 200], fill=(30, 40, 70))
    d.ellipse([x0 + 66, y0 + 70, x0 + 124, y0 + 128], fill=(222, 176, 128), outline=INK, width=3)
    d.chord([x0 + 62, y0 + 62, x0 + 128, y0 + 110], 180, 360, fill=(40, 30, 30))
    d.polygon([(x0 + 52, y0 + 160), (x0 + 70, y0 + 126), (x0 + 120, y0 + 126), (x0 + 138, y0 + 160)], fill=(200, 50, 60), outline=INK)
    d.rectangle([x0 + 8, y0 + h - 52, x0 + h - 8, y0 + h - 8], fill=(220, 40, 40))
    d.text((x0 + 16, y0 + h - 46), 'BERITA  KL', font=font('impact.ttf', 30), fill=(255, 255, 255))
    # football: a striped pitch, the centre circle, a score bug
    x0, y0 = h, 0
    d.rectangle([x0 + 8, y0 + 8, x0 + h - 8, y0 + h - 8], fill=(60, 150, 70))
    for k in range(4):
        d.rectangle([x0 + 8 + k * 60, y0 + 8, x0 + 38 + k * 60, y0 + h - 8], fill=(70, 162, 80))
    d.line([(x0 + h // 2, y0 + 8), (x0 + h // 2, y0 + h - 8)], fill=(240, 240, 240), width=4)
    d.ellipse([x0 + h // 2 - 40, y0 + h // 2 - 40, x0 + h // 2 + 40, y0 + h // 2 + 40], outline=(240, 240, 240), width=4)
    d.rectangle([x0 + 16, y0 + 16, x0 + 130, y0 + 44], fill=(20, 30, 90))
    d.text((x0 + 22, y0 + 18), 'MAS 2-1', font=font('arialbd.ttf', 22), fill=(255, 255, 255))
    # a calendar
    x0, y0 = 0, h
    d.rectangle([x0 + 8, y0 + 8, x0 + h - 8, y0 + h - 8], fill=(252, 250, 242))
    d.rectangle([x0 + 8, y0 + 8, x0 + h - 8, y0 + 100], fill=(206, 40, 40))
    d.text((x0 + 30, y0 + 30), 'OKTOBER', font=font('impact.ttf', 44), fill=(255, 236, 120))
    for r_ in range(5):
        for c_ in range(7):
            n = 1 + r_ * 7 + c_
            if n > 31:
                continue
            col = (206, 40, 40) if c_ == 0 else INK
            d.text((x0 + 22 + c_ * 32, y0 + 112 + r_ * 26), str(n), font=font('arial.ttf', 18), fill=col)
    # a drinks menu
    x0, y0 = h, h
    d.rectangle([x0 + 8, y0 + 8, x0 + h - 8, y0 + h - 8], fill=(24, 70, 44))
    f = font('arialbd.ttf', 22)
    for k, t in enumerate(['TEH TARIK  1.80', 'KOPI O     1.50', 'MILO AIS   2.50', 'ROTI CANAI 1.20', 'NASI LEMAK 3.00', 'MEE GORENG 5.00']):
        d.text((x0 + 22, y0 + 24 + k * 36), t, font=f, fill=(250, 246, 220))
    return im


def c_floor():
    """[T] Shop and house floors: large square tiles in two tones, a thin grout line, flecks of terrazzo."""
    im, d = cell((238, 238, 238))
    q = S // 4
    r = random.Random(61)
    for y in range(4):
        for x in range(4):
            d.rectangle([x * q, y * q, (x + 1) * q, (y + 1) * q], fill=(246, 246, 246) if (x + y) % 2 == 0 else (222, 222, 222))
            for _ in range(10):
                fx, fy = x * q + r.randint(8, q - 8), y * q + r.randint(8, q - 8)
                d.ellipse([fx - 3, fy - 3, fx + 3, fy + 3], fill=(200, 200, 200))
    for k in range(5):
        d.line([(k * q, 0), (k * q, S)], fill=(176, 176, 176), width=4)
        d.line([(0, k * q), (S, k * q)], fill=(176, 176, 176), width=4)
    return im


def c_rugs():
    """Four textiles (2 x 2): a prayer mat (sejadah), a woven mengkuang mat, a red carpet, a batik cloth."""
    im, d = cell()
    h = S // 2
    # sejadah: green field, the mihrab arch, a red border
    x0, y0 = 0, 0
    d.rectangle([x0, y0, x0 + h, y0 + h], fill=(160, 30, 40))
    d.rectangle([x0 + 18, y0 + 18, x0 + h - 18, y0 + h - 18], fill=(30, 110, 70))
    d.rectangle([x0 + 40, y0 + 90, x0 + h - 40, y0 + h - 40], fill=(40, 130, 84), outline=(232, 196, 90), width=5)
    d.pieslice([x0 + 40, y0 + 40, x0 + h - 40, y0 + 140], 180, 360, fill=(40, 130, 84), outline=(232, 196, 90), width=5)
    d.ellipse([x0 + h // 2 - 16, y0 + 150, x0 + h // 2 + 16, y0 + 182], outline=(232, 196, 90), width=4)
    # mengkuang: a woven check
    x0, y0 = h, 0
    for yy in range(0, h, 16):
        for xx in range(0, h, 16):
            col = (222, 196, 140) if (xx // 16 + yy // 16) % 2 == 0 else (196, 160, 100)
            if (xx // 64 + yy // 64) % 2 == 1 and (xx // 16 + yy // 16) % 2 == 0:
                col = (180, 60, 60)
            d.rectangle([x0 + xx, y0 + yy, x0 + xx + 16, y0 + yy + 16], fill=col)
    # a red carpet with a border and medallion
    x0, y0 = 0, h
    d.rectangle([x0, y0, x0 + h, y0 + h], fill=(150, 30, 40))
    d.rectangle([x0 + 16, y0 + 16, x0 + h - 16, y0 + h - 16], outline=(232, 196, 90), width=6)
    d.ellipse([x0 + h // 2 - 60, y0 + h // 2 - 60, x0 + h // 2 + 60, y0 + h // 2 + 60], outline=(232, 196, 90), width=6)
    d.ellipse([x0 + h // 2 - 24, y0 + h // 2 - 24, x0 + h // 2 + 24, y0 + h // 2 + 24], fill=(30, 60, 120))
    # batik: blue ground, flowers
    x0, y0 = h, h
    d.rectangle([x0, y0, x0 + h, y0 + h], fill=(40, 80, 150))
    rr = random.Random(62)
    for _ in range(9):
        cx, cy = x0 + rr.randint(30, h - 30), y0 + rr.randint(30, h - 30)
        for a in range(6):
            ang = a * math.pi / 3
            px, py = cx + math.cos(ang) * 18, cy + math.sin(ang) * 18
            d.ellipse([px - 12, py - 12, px + 12, py + 12], fill=(250, 196, 60), outline=INK, width=2)
        d.ellipse([cx - 9, cy - 9, cx + 9, cy + 9], fill=(220, 60, 70))
    return im


def c_menus():
    """Two boards, top and bottom: a mamak / kopitiam price board and a barber's price list with its pole."""
    im, d = cell((40, 40, 44))
    f1, f2 = font('impact.ttf', 40), font('arialbd.ttf', 24)
    h = S // 2
    d.rectangle([6, 6, S - 6, h - 6], fill=(250, 214, 60), outline=INK, width=4)
    d.text((24, 14), 'MENU HARI INI', font=f1, fill=(190, 30, 30))
    for k, t in enumerate(['NASI LEMAK   3.00', 'ROTI TELUR   2.00', 'TEH TARIK    1.80', 'MAGGI GORENG 5.50']):
        d.text((28, 70 + k * 40), t, font=f2, fill=INK)
    d.rectangle([6, h + 6, S - 6, S - 6], fill=(248, 246, 238), outline=INK, width=4)
    d.text((24, h + 14), 'GUNTING RAMBUT', font=f1, fill=(30, 70, 150))
    for k, t in enumerate(['DEWASA      RM 12', 'KANAK-KANAK RM 8', 'CUKUR       RM 5', 'SYAMPU      RM 6']):
        d.text((28, h + 70 + k * 40), t, font=f2, fill=INK)
    return im


# ------------------------------------------------------------------------------------- the streets pass (KL signage)
# More shop signs: Malay, Chinese, Tamil and English, the way a KL row mixes them (Brickfields, Masjid India, Chow Kit)
SIGNS2 = [   # (in the order of Interior.SignShop's kinds, sign & 15: kopi, hardware, runcit, textile, kopi, runcit, kopi, gold,
             #  runcit, kopi, barber, runcit, hardware, textile, kopi, textile)
    ('நியூ சரவணா', 'RESTORAN NEW SARAVANAA', 'ta'),
    (None, 'BENGKEL MOTOR AZMAN', None),
    (None, 'KEDAI TELEFON BIMBIT', None),
    ('ஸ்ரீ கணேஷ் புடவை', 'SRI GANESH SAREES', 'ta'),
    ('金龙茶餐室', 'KEDAI KOPI KUM LOONG', 'zh'),
    (None, 'BAKERI ROTI MAJU', None),
    (None, 'NASI KANDAR PELITA 24 JAM', None),
    ('ராஜா ஜுவல்லர்ஸ்', 'RAJA JEWELLERS', 'ta'),
    (None, 'KLINIK DR. RAHMAN', None),
    ('海南鸡饭', 'NASI AYAM HAINAN', 'zh'),
    (None, 'BARBER ABANG', None),
    ('光明眼镜', 'KONG MING OPTICAL', 'zh'),
    ('成发电器', 'KEDAI ELEKTRIK SENG HUAT', 'zh'),
    (None, 'KEDAI DOBI EXPRESS', None),
    ('福记烧腊', 'FOOK KEE ROAST', 'zh'),
    (None, 'BUTIK TUDUNG AISYAH', None),
]
SIGN_STYLES2 = [  # board, text, edge
    ((250, 214, 60), (24, 90, 50), (24, 90, 50)),
    ((24, 90, 50), (250, 230, 120), (250, 230, 120)),
    ((190, 30, 40), (255, 255, 255), (255, 214, 80)),
    ((255, 255, 255), (200, 30, 30), (30, 60, 150)),
    ((30, 60, 150), (255, 255, 255), (250, 214, 60)),
    ((240, 120, 30), (255, 255, 255), (90, 30, 10)),
    ((110, 30, 120), (255, 220, 110), (255, 220, 110)),
    ((20, 20, 24), (255, 70, 70), (255, 70, 70)),
]


def fit_font(d, name, text, size, width, minimum=10):
    f = font(name, size)
    while d.textlength(text, font=f) > width and size > minimum:
        size -= 2
        f = font(name, size)
    return f, size


def sign2(d, x0, y0, x1, y1, script, rumi, lang, style):
    """A signboard: the script line big across the top (Chinese or Tamil), the rumi (Malay/English) line under it."""
    board, text, edge = style
    d.rectangle([x0, y0, x1, y1], fill=board, outline=INK, width=4)
    d.rectangle([x0 + 8, y0 + 8, x1 - 8, y1 - 8], outline=edge, width=4)
    w, h = x1 - x0, y1 - y0
    if script:
        name = 'msyhbd.ttc' if lang == 'zh' else 'Nirmala.ttc'
        f, size = fit_font(d, name, script, int(h * (0.52 if lang == 'zh' else 0.36)), w * 0.86, 12)
        tw = d.textlength(script, font=f)
        tx = x0 + (w - tw) / 2
        ty = y0 + h * (0.04 if lang == 'zh' else 0.1)
        d.text((tx + 3, ty + 3), script, font=f, fill=INK)
        d.text((tx, ty), script, font=f, fill=text)
        f2, _ = fit_font(d, 'arialbd.ttf', rumi, int(h * 0.19), w * 0.9)
        tw2 = d.textlength(rumi, font=f2)
        d.text((x0 + (w - tw2) / 2, y1 - h * 0.29), rumi, font=f2, fill=text)
    else:
        f, size = fit_font(d, 'impact.ttf', rumi, int(h * 0.5), w * 0.88)
        tw = d.textlength(rumi, font=f)
        tx, ty = x0 + (w - tw) / 2, y0 + (h - size * 1.2) / 2
        d.text((tx + 4, ty + 4), rumi, font=f, fill=INK)
        d.text((tx, ty), rumi, font=f, fill=text)


def c_signs2(k):
    im, d = cell()
    for j in range(4):
        script, rumi, lang = SIGNS2[(k * 4 + j) % len(SIGNS2)]
        style = SIGN_STYLES2[(k * 4 + j * 3) % len(SIGN_STYLES2)]
        sign2(d, 0, j * S // 4, S - 1, (j + 1) * S // 4 - 1, script, rumi, lang, style)
    return im


BLADES = [('金龙', (190, 30, 40), (255, 220, 90)), ('药行', (250, 214, 60), (190, 30, 30)), ('酒家', (24, 90, 50), (255, 255, 255)),
          ('当铺', (30, 60, 150), (255, 255, 255)), ('HOTEL', (255, 255, 255), (200, 30, 30)), ('KLINIK', (255, 255, 255), (30, 120, 60)),
          ('旅店', (110, 30, 120), (255, 220, 110)), ('KARAOKE', (20, 20, 24), (255, 80, 160))]


def c_blades(k):
    """Vertical blade signs hung off the pilasters (four strips a cell), Chinese characters stacked or a word turned on its side."""
    im, d = cell((40, 40, 40))
    w = S // 4
    for j in range(4):
        t, bg, fg = BLADES[(k * 4 + j) % len(BLADES)]
        x0 = j * w
        d.rectangle([x0 + 2, 2, x0 + w - 3, S - 3], fill=bg, outline=INK, width=5)
        d.rectangle([x0 + 10, 10, x0 + w - 11, S - 11], outline=fg, width=3)
        if all(ord(c) > 0x2E80 for c in t):
            f = font('msyhbd.ttc', int(w * 0.68))
            n = len(t)
            for i, ch in enumerate(t):
                cy = 20 + (S - 40) * (i + 0.5) / n
                tw = d.textlength(ch, font=f)
                d.text((x0 + (w - tw) / 2 + 3, cy - w * 0.48 + 3), ch, font=f, fill=INK)
                d.text((x0 + (w - tw) / 2, cy - w * 0.48), ch, font=f, fill=fg)
        else:
            f, size = fit_font(d, 'impact.ttf', t, int(w * 0.62), S - 50, 12)
            tmp = Image.new('RGBA', (S, w), (0, 0, 0, 0))
            td = ImageDraw.Draw(tmp)
            tw = td.textlength(t, font=f)
            td.text(((S - tw) / 2 + 3, (w - size * 1.2) / 2 + 3), t, font=f, fill=INK)
            td.text(((S - tw) / 2, (w - size * 1.2) / 2), t, font=f, fill=fg)
            tmp = tmp.rotate(90, expand=True)
            im.paste(tmp, (x0, 0), tmp)
    return im


JKR = {
    'blue': [(('JALAN TUN PERAK', 'up'), ('MASJID JAMEK', 'left'), ('CHOW KIT', 'right')),
             (('BUKIT BINTANG', 'up'), ('KLCC', 'right'), ('PUDU', 'left')),
             (('JLN TUANKU ABDUL RAHMAN', 'up'), ('TITIWANGSA', 'right'), ('DATARAN MERDEKA', 'left')),
             (('BRICKFIELDS', 'up'), ('KL SENTRAL', 'left'), ('JLN TUN SAMBANTHAN', 'right'))],
    'green': [(('LEBUHRAYA PERSEKUTUAN', 'up'), ('PETALING JAYA', 'left'), ('KLANG', 'right')),
              (('MRR2', 'up'), ('AMPANG', 'right'), ('CHERAS', 'left')),
              (('DUKE', 'up'), ('SENTUL', 'left'), ('GOMBAK', 'right')),
              (('JALAN TUN RAZAK', 'up'), ('KLCC', 'left'), ('JALAN AMPANG', 'right'))],
}
JKR_BG = {'blue': (22, 72, 160), 'green': (24, 116, 66)}


def arrow(d, cx, cy, r, way, col):
    """A fat JKR arrow pointing up, left or right."""
    pts = [(0, -1), (0.6, -0.2), (0.25, -0.2), (0.25, 1), (-0.25, 1), (-0.25, -0.2), (-0.6, -0.2)]
    if way == 'left': pts = [(y, -x) for x, y in pts]
    elif way == 'right': pts = [(-y, x) for x, y in pts]
    d.polygon([(cx + x * r, cy + y * r) for x, y in pts], fill=col)


def jkr_board(d, y0, colour, rows):
    """One JKR overhead direction board (half a cell): three destinations with arrows, white on blue (city roads) or
    green (highways), route codes in a yellow shield."""
    bg = JKR_BG[colour]
    d.rectangle([0, y0, S, y0 + S // 2], fill=bg)
    d.rectangle([4, y0 + 4, S - 5, y0 + S // 2 - 5], fill=bg, outline=(255, 255, 255), width=6)
    for r, (t, way) in enumerate(rows):
        cy = y0 + 46 + r * 76
        arrow(d, 50, cy, 26, way, (255, 255, 255))
        f, size = fit_font(d, 'arialbd.ttf', t, 40 if r == 0 else 34, S - 130, 14)
        if t in ('MRR2', 'DUKE'):
            tw = d.textlength(t, font=f)
            d.rounded_rectangle([96, cy - 24, 96 + tw + 24, cy + 24], radius=10, fill=(250, 214, 40), outline=INK, width=3)
            d.text((108, cy - size * 0.6), t, font=f, fill=INK)
        else:
            d.text((98, cy - size * 0.6), t, font=f, fill=(255, 255, 255))
        if r == 0: d.line([(20, cy + 36), (S - 20, cy + 36)], fill=(255, 255, 255), width=3)


def c_jkr(colour):
    im, d = cell(JKR_BG[colour])
    for j in range(2): jkr_board(d, j * S // 2, colour, JKR[colour][j])
    return im


def c_jkr2():
    """The third board of each colour: blue on top, green below."""
    im, d = cell()
    jkr_board(d, 0, 'blue', JKR['blue'][2])
    jkr_board(d, S // 2, 'green', JKR['green'][2])
    return im


STREETS = ['JALAN TUN PERAK', 'JALAN PETALING', 'JALAN TUANKU ABDUL RAHMAN', 'JALAN ALOR', 'JALAN CHOW KIT', 'JALAN AMPANG',
           'JALAN TUN H.S. LEE', 'JALAN RAJA LAUT']


def c_streetnames():
    """Eight street-name boards (one a row): white on DBKL blue, a white rim, the postcode district small."""
    im, d = cell((30, 70, 150))
    h = S // 8
    for j, t in enumerate(STREETS):
        y0 = j * h
        d.rectangle([2, y0 + 2, S - 3, y0 + h - 3], fill=(26, 66, 150), outline=(255, 255, 255), width=4)
        f, _ = fit_font(d, 'arialbd.ttf', t, 34, S - 40, 12)
        tw = d.textlength(t, font=f)
        d.text(((S - tw) / 2, y0 + 6), t, font=f, fill=(255, 255, 255))
        f2 = font('arialbd.ttf', 14)
        t2 = '50' + str(100 + j * 37)[-3:] + ' KUALA LUMPUR'
        tw2 = d.textlength(t2, font=f2)
        d.text(((S - tw2) / 2, y0 + h - 22), t2, font=f2, fill=(220, 230, 255))
    return im


def c_mart():
    """A 24-hour convenience store: the striped fascia (top quarter: orange, green and red bands, the name in a white
    box), and the glass shopfront below with posters (bottom three quarters)."""
    im, d = cell((255, 255, 255))
    q = S // 4
    for k, col in enumerate(((246, 132, 30), (20, 140, 80), (220, 36, 40))):
        d.rectangle([0, 10 + k * 34, S, 10 + k * 34 + 26], fill=col)
    d.rounded_rectangle([S * 0.3, 8, S * 0.7, q - 8], radius=14, fill=(255, 255, 255), outline=INK, width=4)
    f = font('impact.ttf', 70)
    t = '24-7'
    tw = d.textlength(t, font=f)
    d.text(((S - tw) / 2, 14), t, font=f, fill=(220, 36, 40))
    d.line([(0, q), (S, q)], fill=INK, width=5)
    clip_glass(im, (0, q + 4, S, S), (170, 214, 236), (120, 170, 210))
    d = ImageDraw.Draw(im)
    for x in (S // 3, 2 * S // 3):
        d.rectangle([x - 5, q, x + 5, S], fill=(200, 200, 204), outline=INK, width=2)
    r = random.Random(24)
    for k in range(5):
        x, y = r.randint(10, S - 120), r.randint(q + 30, S - 160)
        col = r.choice([(250, 214, 60), (220, 36, 40), (40, 140, 200), (255, 255, 255)])
        d.rectangle([x, y, x + 100, y + 130], fill=col, outline=INK, width=3)
        d.text((x + 10, y + 10), r.choice(['SLURPEE', 'PROMO!', 'RM2.50', 'TOP-UP', 'ROTI']), font=font('arialbd.ttf', 20), fill=INK)
    d.text((24, S - 52), 'BUKA 24 JAM', font=font('arialbd.ttf', 34), fill=(20, 140, 80))
    return im


def c_petrol():
    """A petrol station: the canopy fascia (top quarter, teal and green bands with the brand), the price pylon (left
    half below) and the kedai's glass front (right half)."""
    im, d = cell((236, 246, 244))
    q = S // 4
    d.rectangle([0, 0, S, q], fill=(0, 150, 140))
    d.rectangle([0, q - 26, S, q - 8], fill=(150, 200, 40))
    f = font('impact.ttf', 66)
    t = 'PETROLIA'
    tw = d.textlength(t, font=f)
    d.text(((S - tw) / 2 + 3, 12 + 3), t, font=f, fill=INK)
    d.text(((S - tw) / 2, 12), t, font=f, fill=(255, 255, 255))
    x0, x1 = 10, S // 2 - 10
    d.rectangle([x0, q + 10, x1, S - 10], fill=(250, 250, 250), outline=INK, width=4)
    d.rectangle([x0 + 8, q + 18, x1 - 8, q + 100], fill=(0, 150, 140))
    d.ellipse([x0 + 70, q + 26, x1 - 70, q + 92], fill=(150, 200, 40), outline=INK, width=3)
    fl, fp = font('arialbd.ttf', 26), font('impact.ttf', 44)
    for k, (lab, price, col) in enumerate((('RON95', '2.05', (230, 190, 30)), ('RON97', '3.47', (40, 150, 60)), ('DIESEL', '2.15', (30, 30, 30)))):
        y = q + 120 + k * 80
        d.rectangle([x0 + 8, y, x0 + 110, y + 64], fill=col)
        d.text((x0 + 14, y + 18), lab, font=fl, fill=(255, 255, 255))
        d.rectangle([x0 + 118, y, x1 - 8, y + 64], fill=(20, 20, 20))
        d.text((x0 + 126, y + 6), price, font=fp, fill=(255, 70, 50))
    clip_glass(im, (S // 2 + 4, q + 10, S - 6, S - 10), (170, 214, 236), (120, 170, 210))
    d = ImageDraw.Draw(im)
    d.rectangle([S // 2 + 4, q + 10, S - 6, S - 10], outline=INK, width=4)
    d.rectangle([S * 3 // 4 - 40, q + 120, S * 3 // 4 + 40, S - 10], outline=(200, 200, 204), width=6)
    d.text((S // 2 + 20, q + 30), 'KEDAI MESRA', font=font('arialbd.ttf', 30), fill=(0, 130, 120))
    return im


def c_bunting():
    """Top half: the Jalur Gemilang (14 red and white stripes, the blue canton with the yellow crescent and star).
    Bottom half: two lamp-post banners side by side."""
    im, d = cell((255, 255, 255))
    h2 = S // 2
    sh = h2 / 14
    for k in range(14):
        d.rectangle([0, k * sh, S, (k + 1) * sh], fill=(204, 0, 1) if k % 2 == 0 else (255, 255, 255))
    d.rectangle([0, 0, S // 2, sh * 8], fill=(1, 0, 102))
    cx, cy, r = S * 0.16, sh * 4, sh * 3.1
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 204, 0))
    d.ellipse([cx - r * 0.72 + r * 0.32, cy - r * 0.8, cx + r * 0.88 + r * 0.32, cy + r * 0.8], fill=(1, 0, 102))
    sx, sy, R = S * 0.33, sh * 4, sh * 2.9
    pts = []
    for i in range(28):
        a = -math.pi / 2 + i * math.pi / 14
        rr = R if i % 2 == 0 else R * 0.5
        pts.append((sx + math.cos(a) * rr, sy + math.sin(a) * rr))
    d.polygon(pts, fill=(255, 204, 0))
    texts = [('SELAMAT', 'HARI', 'MERDEKA', (204, 0, 1), (255, 214, 0)), ('KL', 'BANDARAYA', 'MESRA', (30, 90, 170), (255, 255, 255))]
    for j, (a, b, c, bg, fg) in enumerate(texts):
        x0 = j * S // 2
        d.rectangle([x0 + 4, h2 + 4, x0 + S // 2 - 5, S - 5], fill=bg, outline=INK, width=4)
        for k, t in enumerate((a, b, c)):
            f, _ = fit_font(d, 'impact.ttf', t, 44, S // 2 - 30, 12)
            tw = d.textlength(t, font=f)
            d.text((x0 + (S // 2 - tw) / 2, h2 + 30 + k * 70), t, font=f, fill=fg)
    return im


BILLBOARDS2 = [('CONDO MEWAH', 'DARI RM 388K!', (30, 60, 150), (255, 255, 255), (250, 214, 60)),
               ('5G LAJU GILA', 'MEGAMAJU TELCO', (110, 30, 120), (255, 255, 255), (255, 120, 200)),
               ('MILO AIS', 'KUAT & CERGAS', (24, 100, 50), (255, 255, 255), (250, 214, 60)),
               ('JOM SHOPPING', 'SALE 70%', (220, 36, 40), (255, 255, 255), (255, 230, 120))]


def c_billboard2(k):
    t1, t2, bg, fg, hi = BILLBOARDS2[k]
    im, d = cell(bg)
    d.rectangle([0, S * 0.62, S, S], fill=tuple(max(0, v - 40) for v in bg))
    f, _ = fit_font(d, 'impact.ttf', t1, 96, S * 0.9)
    tw = d.textlength(t1, font=f)
    d.text(((S - tw) / 2 + 5, S * 0.18 + 5), t1, font=f, fill=INK)
    d.text(((S - tw) / 2, S * 0.18), t1, font=f, fill=fg)
    f2 = font('arialbd.ttf', 48)
    tw2 = d.textlength(t2, font=f2)
    d.text(((S - tw2) / 2, S * 0.72), t2, font=f2, fill=hi)
    d.rectangle([0, 0, S - 1, S - 1], outline=INK, width=8)
    return im


def c_mamak():
    """A mamak's big fascia (top half: green and yellow, the name, NASI KANDAR / ROTI CANAI / TEH TARIK, plates), and a
    4D kiosk and newspaper stand front (bottom half)."""
    im, d = cell((250, 214, 40))
    h2 = S // 2
    d.rectangle([0, 0, S, h2], fill=(20, 120, 60))
    d.rectangle([0, h2 - 40, S, h2], fill=(250, 214, 40))
    t = 'RESTORAN BISMILLAH'
    f, _ = fit_font(d, 'impact.ttf', t, 64, S - 30)
    tw = d.textlength(t, font=f)
    d.text(((S - tw) / 2 + 3, 20 + 3), t, font=f, fill=INK)
    d.text(((S - tw) / 2, 20), t, font=f, fill=(255, 230, 90))
    t2 = 'NASI KANDAR - ROTI CANAI - TEH TARIK'
    f2, _ = fit_font(d, 'arialbd.ttf', t2, 28, S - 20)
    tw2 = d.textlength(t2, font=f2)
    d.text(((S - tw2) / 2, 120), t2, font=f2, fill=(255, 255, 255))
    for k in range(3):
        cx = 100 + k * 160
        d.ellipse([cx - 50, 162, cx + 50, 200], fill=(255, 255, 255), outline=INK, width=3)
        d.ellipse([cx - 32, 166, cx + 32, 192], fill=[(230, 150, 40), (240, 200, 120), (180, 90, 40)][k])
    d.text((S // 2 - 90, h2 - 36), 'BUKA 24 JAM', font=font('arialbd.ttf', 28), fill=(20, 120, 60))
    d.rectangle([0, h2, S, S], fill=(236, 230, 220))
    for k, (t, bg) in enumerate((('4D', (220, 36, 40)), ('TOTO', (30, 90, 170)), ('DA MA CAI', (250, 160, 30)))):
        x0 = 12 + k * 166
        d.rectangle([x0, h2 + 12, x0 + 154, h2 + 96], fill=bg, outline=INK, width=4)
        ft, _ = fit_font(d, 'impact.ttf', t, 44, 140)
        tw = d.textlength(t, font=ft)
        d.text((x0 + (154 - tw) / 2, h2 + 24), t, font=ft, fill=(255, 255, 255))
    r = random.Random(4)
    for row in range(2):
        y = h2 + 120 + row * 66
        for k in range(8):
            x = 12 + k * 62
            d.rectangle([x, y, x + 54, y + 60], fill=(250, 250, 246), outline=INK, width=2)
            d.rectangle([x + 4, y + 4, x + 50, y + 18], fill=r.choice([(220, 36, 40), (30, 90, 170), (20, 20, 20), (20, 120, 60)]))
            for l in range(3): d.line([(x + 6, y + 26 + l * 10), (x + 48, y + 26 + l * 10)], fill=(150, 150, 150), width=3)
    return im


def c_busstop():
    """A bus stop's panels: the roof fascia with BAS and the stop name (top quarter), the route panel (left) and an
    advert panel (right)."""
    im, d = cell((236, 236, 236))
    q = S // 4
    d.rectangle([0, 0, S, q], fill=(220, 36, 40))
    d.text((20, 20), 'BAS', font=font('impact.ttf', 80), fill=(255, 255, 255))
    d.text((180, 30), 'HENTIAN', font=font('arialbd.ttf', 30), fill=(255, 255, 255))
    d.text((180, 66), 'JALAN TUN PERAK', font=font('arialbd.ttf', 30), fill=(255, 230, 120))
    d.rectangle([10, q + 10, S // 2 - 10, S - 10], fill=(255, 255, 255), outline=INK, width=4)
    for k in range(5):
        y = q + 40 + k * 64
        d.rounded_rectangle([24, y, 104, y + 44], radius=8, fill=[(220, 36, 40), (30, 90, 170), (20, 140, 80), (250, 160, 30), (110, 30, 120)][k])
        d.text((34, y + 6), ['U84', 'B114', '300', 'T407', 'GOKL'][k], font=font('arialbd.ttf', 26), fill=(255, 255, 255))
        d.line([(116, y + 22), (S // 2 - 30, y + 22)], fill=(80, 80, 80), width=6)
    d.rectangle([S // 2 + 10, q + 10, S - 10, S - 10], fill=(250, 214, 40), outline=INK, width=4)
    d.text((S // 2 + 30, q + 60), 'TEH', font=font('impact.ttf', 90), fill=(190, 30, 30))
    d.text((S // 2 + 30, q + 170), 'TARIK', font=font('impact.ttf', 90), fill=(190, 30, 30))
    d.text((S // 2 + 30, S - 80), 'BOS!', font=font('impact.ttf', 50), fill=INK)
    return im


def c_kerb_bw():
    """Kerb paint: black and white blocks (the city's kerbs); the yellow/black HAZARD is for junctions and stops."""
    im, d = cell((248, 248, 244))
    for k in range(0, 8, 2):
        d.rectangle([k * S / 8, 0, (k + 1) * S / 8, S], fill=(30, 30, 34))
    return im


def c_grime():
    """Weathered render [T]: near-white with grey rain streaks and mould hanging from the top (a storey's cornice or
    sill line), a damp band along the bottom. Tinted by the wall's paint, tiled one storey to a tile."""
    im, d = cell((250, 250, 250))
    r = random.Random(85)
    for k in range(16):
        x = r.randint(-10, S - 20)
        w = r.randint(10, 46)
        h = r.randint(60, 300)
        g = r.randint(196, 226)
        d.rectangle([x, 0, x + w, h], fill=(g, g, g))
        d.ellipse([x, h - w / 2, x + w, h + w / 2], fill=(g, g, g))
    for k in range(5):                                   # darker mould right under the ledge
        x = r.randint(0, S - 60)
        w = r.randint(30, 90)
        d.rectangle([x, 0, x + w, r.randint(14, 40)], fill=(178, 180, 176))
    d.rectangle([0, S - 26, S, S], fill=(222, 222, 220))
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
    # walk-in interiors (Interior.cs)
    ('GOODS', c_goods), ('FRIDGE', c_fridge), ('PHOTOS', c_photos), ('SCREENS', c_screens), ('FLOOR', c_floor),
    ('RUGS', c_rugs), ('MENUS', c_menus),
    # row 8: the streets pass, KL signage
    ('SIGNS4', lambda: c_signs2(0)), ('SIGNS5', lambda: c_signs2(1)), ('SIGNS6', lambda: c_signs2(2)), ('SIGNS7', lambda: c_signs2(3)),
    ('BLADES0', lambda: c_blades(0)), ('BLADES1', lambda: c_blades(1)), ('JKR_BLUE', lambda: c_jkr('blue')), ('JKR_GREEN', lambda: c_jkr('green')),
    # row 9
    ('JKR_MORE', c_jkr2), ('STREETNAMES', c_streetnames), ('MART', c_mart), ('PETROL', c_petrol),
    ('BUNTING', c_bunting), ('BILLBOARD2', lambda: c_billboard2(0)), ('BILLBOARD3', lambda: c_billboard2(1)), ('MAMAK', c_mamak),
    # row 10 (rows 10-11 are free)
    ('BILLBOARD4', lambda: c_billboard2(2)), ('BILLBOARD5', lambda: c_billboard2(3)), ('BUSSTOP', c_busstop), ('KERB_BW', c_kerb_bw), ('GRIME', c_grime),
]
ROWS = 12                     # = ArchImport's flipbook rows (8 across x 12 down, 96 slices; rows past the cells are blank)


def main():
    sheet = Image.new('RGB', (S * N, S * ROWS), (255, 0, 255))
    for i, (name, fn) in enumerate(CELLS):
        im = fn().convert('RGB')
        if name != 'WHITE':
            im = grain(im, 3, i)
        sheet.paste(im, ((i % N) * S, (i // N) * S))
    sheet = sheet.resize((256 * N, 256 * ROWS), Image.LANCZOS)
    sheet.save(OUT)
    print(OUT, len(CELLS), 'cells')
    names = ', '.join(f'{n}' for n, _ in CELLS)
    print('enum order:', names)


if __name__ == '__main__':
    main()
