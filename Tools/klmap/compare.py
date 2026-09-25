"""
Side by side: the satellite view of each real-KL district's area (Esri World Imagery, kl_satellite_z16.jpg)
and the game's straight-down render of the patch built from it (Tools/previews/95_ortho_<patch>.png,
written by the CityOverview test). Output: Tools/previews/compare_<patch>.jpg and compare_all.jpg.

    uv run --no-project --with pillow python compare.py
"""
import math, os
from PIL import Image, ImageDraw, ImageFont
import layout as L
from osm import LAT0, LON0, MX, MY

HERE = os.path.dirname(os.path.abspath(__file__))
PREV = os.path.normpath(os.path.join(HERE, '..', 'previews'))
S, N, W, E, Z = 3.117, 3.176, 101.678, 101.724, 16


def tile(lat, lon):
    n = 2 ** Z
    return (lon + 180) / 360 * n, (1 - math.asinh(math.tan(math.radians(lat))) / math.pi) / 2 * n


sat = Image.open(os.path.join(HERE, 'kl_satellite_z16.jpg'))
ox, oy = tile(N, W)


def sat_px(x, y):
    """Real metres from Masjid Jamek -> pixel in the satellite image."""
    tx, ty = tile(LAT0 + y / MY, LON0 + x / MX)
    return (tx - ox) * 256, (ty - oy) * 256


try:
    font = ImageFont.truetype('arialbd.ttf', 28)
except OSError:
    font = ImageFont.load_default()
rows = []
for p in L.PATCHES:
    game_path = os.path.join(PREV, f'95_ortho_{p.name}.png')
    if not os.path.exists(game_path): continue
    game = Image.open(game_path).convert('RGB')
    x0, y0, x1, y1 = p.real_window
    ax, ay = sat_px(x0, y1)            # north-west corner
    bx, by = sat_px(x1, y0)            # south-east corner
    real = sat.crop((int(ax), int(ay), int(bx), int(by))).resize(game.size, Image.LANCZOS)
    pair = Image.new('RGB', (game.width * 2 + 24, game.height + 56), (250, 248, 240))
    pair.paste(real, (0, 56))
    pair.paste(game, (game.width + 24, 56))
    d = ImageDraw.Draw(pair)
    d.text((10, 12), f'{p.name}: real KL (satellite)', fill=(30, 30, 30), font=font)
    d.text((game.width + 34, 12), 'Kampung Run: KL (in game)', fill=(30, 30, 30), font=font)
    pair.save(os.path.join(PREV, f'compare_{p.name}.jpg'), quality=88)
    rows.append(pair)
    print(p.name, 'ok')
if rows:
    w = max(r.width for r in rows)
    sheet = Image.new('RGB', (w, sum(r.height for r in rows)), (250, 248, 240))
    y = 0
    for r in rows:
        sheet.paste(r, (0, y))
        y += r.height
    sheet.thumbnail((2400, 99999))
    sheet.save(os.path.join(PREV, 'compare_all.jpg'), quality=85)
