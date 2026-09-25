"""
Facade textures for the real-KL buildings (patches.py gives their walls UVs in window bays x storeys):
  fac_win    one 3.6 m bay x one 3.4 m storey of plastered wall with a two-pane window (tinted by the wall colour)
  fac_glass  one 3.0 m x 3.4 m curtain-wall panel with its floor spandrel (tinted by the glass colour)
  fac_shop   four 3.6 m shopfronts x the 4.2 m ground floor: signboards, awnings, shop windows and shutters
  fac_lobby  one 3.6 m bay x the 5 m glass lobby under a tower
Painted, Hit & Run style: flat colours with soft shading and a little grain. Written to
Assets/KampungRun/Resources/KLMap/Tex/.
    uv run --no-project --with numpy --with pillow python gen_facades.py
"""
import os, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, '..', '..', 'Assets', 'KampungRun', 'Resources', 'KLMap', 'Tex'))
os.makedirs(OUT, exist_ok=True)
rng = random.Random(1957)


def grain(img, amount=6, seed=1):
    a = np.asarray(img).astype(np.float32)
    n = np.random.default_rng(seed).normal(0, amount, a.shape[:2])[..., None]
    return Image.fromarray(np.clip(a + n, 0, 255).astype(np.uint8))


def vgrad(d, box, top, bottom):
    x0, y0, x1, y1 = box
    for y in range(y0, y1):
        t = (y - y0) / max(1, y1 - y0 - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        d.line([(x0, y), (x1 - 1, y)], fill=c)


def glass(d, box, top=(64, 92, 132), bottom=(118, 150, 186), streak=(170, 196, 222)):
    """A pane of glass: dark at the top, lighter below, and two diagonal sky reflections (kept inside the pane)."""
    x0, y0, x1, y1 = [int(v) for v in box]
    w, h = x1 - x0, y1 - y0
    pane = Image.new('RGB', (w, h))
    pd = ImageDraw.Draw(pane)
    vgrad(pd, (0, 0, w, h), top, bottom)
    for k in range(max(2, w // 5)):
        xa = int(w * 0.18) + k
        pd.line([(xa, h - 1), (xa + int(h * 0.55), 0)], fill=streak)
    for k in range(max(1, w // 12)):
        xa = int(w * 0.55) + k
        pd.line([(xa, h - 1), (xa + int(h * 0.55), 0)], fill=streak)
    d._image.paste(pane, (x0, y0))


def win():
    S = 256
    img = Image.new('RGB', (S, S), (242, 240, 234))
    d = ImageDraw.Draw(img)
    # floor slab line at the top and a faint shadow under it
    d.rectangle([0, 0, S, 8], fill=(222, 219, 212))
    d.rectangle([0, 9, S, 12], fill=(232, 229, 222))
    # the window: ~1.9 m wide, ~1.8 m tall, sill at 0.95 m
    wx0, wx1 = int(S * 0.23), int(S * 0.77)
    wy0, wy1 = int(S * 0.2), int(S * 0.72)
    d.rectangle([wx0 - 7, wy0 - 7, wx1 + 7, wy1 + 7], fill=(252, 252, 250))          # frame
    glass(d, (wx0, wy0, wx1, wy1))
    mid = (wx0 + wx1) // 2
    d.rectangle([mid - 4, wy0, mid + 4, wy1], fill=(252, 252, 250))                    # mullion
    d.rectangle([wx0, int(wy0 + (wy1 - wy0) * 0.3) - 3, wx1, int(wy0 + (wy1 - wy0) * 0.3) + 3], fill=(252, 252, 250))  # transom
    d.rectangle([wx0 - 14, wy1 + 7, wx1 + 14, wy1 + 17], fill=(214, 210, 202))         # sill
    d.rectangle([wx0 - 14, wy1 + 17, wx1 + 14, wy1 + 21], fill=(200, 196, 188))        # its shadow
    # a split air-con unit under some windows gives the tile a KL look
    d.rectangle([int(S * 0.62), wy1 + 30, int(S * 0.86), wy1 + 58], fill=(236, 236, 234))
    d.line([int(S * 0.64), wy1 + 40, int(S * 0.84), wy1 + 40], fill=(190, 190, 188), width=2)
    d.line([int(S * 0.64), wy1 + 48, int(S * 0.84), wy1 + 48], fill=(190, 190, 188), width=2)
    img = grain(img.filter(ImageFilter.SMOOTH), 4, 2)
    img.save(os.path.join(OUT, 'fac_win.png'))


def glass_wall():
    S = 256
    img = Image.new('RGB', (S, S), (200, 210, 222))
    d = ImageDraw.Draw(img)
    spandrel = int(S * 0.2)
    # vision glass above the spandrel (v runs up the wall: the image bottom is the floor)
    glass(d, (0, 0, S, S - spandrel), top=(150, 172, 198), bottom=(212, 226, 240), streak=(236, 244, 252))
    vgrad(d, (0, S - spandrel, S, S), (120, 132, 148), (104, 116, 132))
    d.rectangle([0, S - spandrel - 5, S, S - spandrel], fill=(236, 238, 240))             # slab edge
    d.rectangle([0, 0, 5, S], fill=(236, 238, 240))                                        # mullions
    d.rectangle([S - 3, 0, S, S], fill=(236, 238, 240))
    img = grain(img.filter(ImageFilter.SMOOTH), 3, 3)
    img.save(os.path.join(OUT, 'fac_glass.png'))


SIGNS = [(206, 44, 44), (242, 196, 36), (40, 140, 80), (40, 96, 190), (230, 110, 30), (150, 60, 150), (30, 150, 160), (220, 70, 120)]
AWNINGS = [((214, 60, 50), (246, 240, 230)), ((40, 120, 70), (240, 236, 220)), ((236, 180, 40), (250, 244, 225)), ((50, 90, 170), (240, 240, 240))]


def shop():
    W, H = 1024, 256
    img = Image.new('RGB', (W, H), (238, 232, 220))
    d = ImageDraw.Draw(img)
    cols = rng.sample(SIGNS, 4)
    for i in range(4):
        x0, x1 = i * 256, (i + 1) * 256
        # pillar of the five-foot way between shops
        d.rectangle([x0, 0, x0 + 14, H], fill=(226, 220, 206))
        d.rectangle([x0 + 14, 0, x0 + 17, H], fill=(196, 190, 176))
        sx0, sx1 = x0 + 22, x1 - 8
        # signboard with "lettering"
        sign = cols[i]
        d.rectangle([sx0, 10, sx1, 62], fill=sign)
        d.rectangle([sx0, 10, sx1, 14], fill=tuple(min(255, c + 40) for c in sign))
        lx = sx0 + 16
        while lx < sx1 - 30:
            w = rng.randint(10, 22)
            d.rectangle([lx, 26, lx + w, 46], fill=(250, 248, 240) if sum(sign) < 450 else (40, 36, 34))
            lx += w + rng.randint(5, 9)
        # awning: striped or plain
        top, bot = AWNINGS[(i + rng.randint(0, 3)) % 4]
        for k, x in enumerate(range(sx0, sx1, 16)):
            d.rectangle([x, 66, min(sx1, x + 15), 92], fill=top if k % 2 == 0 else bot)
        d.polygon([(sx0, 92), (sx1, 92), (sx1 - 4, 100), (sx0 + 4, 100)], fill=tuple(int(c * 0.8) for c in top))
        # the front: shop window + door, or a half-open roller shutter
        fy0, fy1 = 104, H - 6
        kind = (i + 1) % 3
        if kind == 2:
            d.rectangle([sx0, fy0, sx1, fy1], fill=(60, 58, 60))                        # dark interior
            for y in range(fy0, fy0 + 70, 6):                                            # shutter half down
                d.rectangle([sx0, y, sx1, y + 4], fill=(176, 178, 182))
            for k in range(6):                                                           # goods on crates
                gx = sx0 + 12 + k * 36
                d.rectangle([gx, fy1 - 44, gx + 28, fy1 - 4], fill=rng.choice(SIGNS))
        else:
            dx0 = sx0 + (sx1 - sx0) * (0.62 if kind == 0 else 0.08)
            dx1 = dx0 + (sx1 - sx0) * 0.3
            glass(d, (sx0, fy0, sx1, fy1), top=(80, 100, 120), bottom=(150, 170, 186), streak=(200, 214, 226))
            for k in range(4):                                                           # things in the window
                gx = sx0 + 10 + k * 26
                if gx + 20 > dx0 and kind == 0: break
                d.rectangle([gx, fy1 - 60 + (k % 2) * 12, gx + 18, fy1 - 10], fill=rng.choice(SIGNS))
            d.rectangle([int(dx0), fy0 + 8, int(dx1), fy1], fill=(96, 70, 50))           # door
            d.rectangle([int(dx0) + 8, fy0 + 18, int(dx1) - 8, fy1 - 60], fill=(150, 170, 186))
            d.rectangle([sx0, fy0, sx1, fy0 + 5], fill=(236, 232, 224))                  # frame
        d.rectangle([sx0, H - 6, sx1, H], fill=(170, 164, 152))                          # kerb of the five-foot way
    img = grain(img.filter(ImageFilter.SMOOTH), 4, 4)
    img.save(os.path.join(OUT, 'fac_shop.png'))


def lobby():
    S = 256
    img = Image.new('RGB', (S, S), (230, 232, 234))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, S, 26], fill=(210, 214, 218))                                     # canopy fascia
    d.rectangle([0, 26, S, 34], fill=(150, 156, 164))
    glass(d, (10, 40, S - 10, S - 8), top=(110, 132, 156), bottom=(176, 196, 214), streak=(220, 232, 242))
    d.rectangle([S // 2 - 3, 40, S // 2 + 3, S - 8], fill=(90, 96, 104))                 # frame
    d.rectangle([10, int(S * 0.55), S - 10, int(S * 0.55) + 4], fill=(90, 96, 104))
    d.rectangle([0, S - 8, S, S], fill=(120, 120, 124))                                  # step
    img = grain(img.filter(ImageFilter.SMOOTH), 3, 5)
    img.save(os.path.join(OUT, 'fac_lobby.png'))


win()
glass_wall()
shop()
lobby()
# a contact sheet to eyeball them
sheet = Image.new('RGB', (1024 + 3 * 256 + 40, 256), (255, 255, 255))
x = 0
for n in ('fac_win', 'fac_glass', 'fac_lobby', 'fac_shop'):
    im = Image.open(os.path.join(OUT, n + '.png'))
    sheet.paste(im, (x, 0))
    x += im.width + 10
sheet.save(os.path.join(HERE, 'out', 'facades.png'))
print('facades ->', OUT)
