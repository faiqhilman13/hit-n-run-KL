"""Render the planned game map (grid + real patches) to a PNG for review.
   uv run --no-project --with shapely --with pillow python preview_layout.py"""
from PIL import Image, ImageDraw, ImageFont
from shapely.geometry import box
from shapely import affinity
import layout as L
from osm import OSM, ROAD_CLASSES, levels

PX = 0.55   # pixels per game metre
W, H = int(L.NX * L.PITCH * PX), int(L.NZ * L.PITCH * PX)
img = Image.new('RGB', (W, H), (205, 200, 190))
dr = ImageDraw.Draw(img)


def P(x, z): return ((x - L.X0) * PX, (L.Z0 + L.NZ * L.PITCH - z) * PX)


def poly(g, fill, outline=None):
    if g.is_empty: return
    if g.geom_type in ('MultiPolygon', 'GeometryCollection'):
        for s in g.geoms: poly(s, fill, outline)
        return
    if g.geom_type != 'Polygon': return
    dr.polygon([P(x, z) for x, z in g.exterior.coords], fill=fill, outline=outline)
    for hole in g.interiors: dr.polygon([P(x, z) for x, z in hole.coords], fill=(205, 200, 190))


osm = OSM()
kind_col = {'filler': (222, 214, 196), 'kampung': (150, 200, 110), 'river': (120, 170, 220)}
# cells
for c in range(L.NX):
    for r in range(L.NZ):
        kind, name = L.cell_kind(c, r)
        if kind == 'patch': continue
        x0, z0, x1, z1 = L.cell_rect(c, r, c, r)
        dr.rectangle([P(x0, z1), P(x1, z0)], fill=kind_col[kind])
        if kind == 'river':
            cx = L.col_center(c)
            dr.rectangle([P(cx - 11, z1), P(cx + 11, z0)], fill=(60, 120, 200))
# patches
for p in L.PATCHES:
    x0, z0, x1, z1 = p.rect
    rect = box(x0, z0, x1, z1)
    dr.rectangle([P(x0, z1), P(x1, z0)], fill=(232, 226, 214))
    m = [L.K, 0, 0, L.K, p.frame.fx, p.frame.fz]
    wx0, wz0, wx1, wz1 = p.real_window
    win = box(wx0 - 50, wz0 - 50, wx1 + 50, wz1 + 50)
    T = lambda g: affinity.affine_transform(g, m).intersection(rect)
    for f in osm.parks + osm.cemetery:
        if f['poly'].intersects(win): poly(T(f['poly']), (170, 212, 140))
    for f in osm.forest:
        if f['poly'].intersects(win): poly(T(f['poly']), (110, 170, 100))
    for f in osm.pitches:
        if f['poly'].intersects(win): poly(T(f['poly']), (130, 190, 110))
    for f in osm.water:
        if f['poly'].intersects(win): poly(T(f['poly']), (80, 140, 210))
    if p.rivers:
        for rv in osm.rivers:
            if rv['line'].intersects(win) and any(n in rv['name'] for n in ('Gombak', 'Kelang', 'Klang')):
                poly(T(affinity.affine_transform(rv['line'], m).buffer(11, cap_style=2)) if False else affinity.affine_transform(rv['line'], m).buffer(11, cap_style=2).intersection(rect), (60, 120, 200))
    for rd in osm.roads:
        if not rd['line'].intersects(win) or rd['tags'].get('tunnel') == 'yes': continue
        w = ROAD_CLASSES[rd['cls']][0]
        g = affinity.affine_transform(rd['line'], m).buffer(w / 2, cap_style=2).intersection(rect)
        col = (120, 120, 150) if rd['tags'].get('bridge') == 'yes' else (95, 95, 100)
        poly(g, col)
    for b in osm.buildings:
        if not b['poly'].intersects(win): continue
        lv = levels(b['tags']) or 0
        col = (70, 70, 95) if lv >= 20 else (150, 120, 120) if lv >= 6 else (190, 150, 140)
        poly(T(b['poly']), col)
    dr.rectangle([P(x0, z1), P(x1, z0)], outline=(200, 40, 40), width=2)
    dr.text(P(x0 + 6, z1 - 6), p.name, fill=(200, 30, 30))
# grid roads (between cells that are not in the same patch)
def same_patch(a, b):
    ka, kb = L.cell_kind(*a), L.cell_kind(*b)
    return ka[0] == 'patch' and ka == kb
for i in range(L.NX + 1):
    for r in range(L.NZ):
        if 0 < i < L.NX and same_patch((i - 1, r), (i, r)): continue
        x = L.road_x(i)
        dr.rectangle([P(x - 10, L.road_z(r + 1) + 10), P(x + 10, L.road_z(r) - 10)], fill=(70, 70, 75))
for k in range(L.NZ + 1):
    for c in range(L.NX):
        if 0 < k < L.NZ and same_patch((c, k - 1), (c, k)): continue
        z = L.road_z(k)
        dr.rectangle([P(L.road_x(c) - 10, z + 10), P(L.road_x(c + 1) + 10, z - 10)], fill=(70, 70, 75))
# landmark labels
frames = {p.name: p for p in L.PATCHES}
def lab(name, real, patch):
    x, z = frames[patch].frame.to_game(*real)
    dr.ellipse([P(x - 6, z + 6), P(x + 6, z - 6)], fill=(255, 220, 0), outline=(0, 0, 0))
    dr.text((P(x, z)[0] + 6, P(x, z)[1] - 6), name, fill=(0, 0, 0))
for n, pt in [('MasjidJamek', 'KotaLama'), ('Dataran', 'KotaLama'), ('PasarSeni', 'KotaLama'), ('Merdeka118', 'KotaLama'),
              ('StadiumMerdeka', 'KotaLama'), ('MasjidNegara', 'KotaLama'), ('KLRailway', 'KotaLama'), ('TuguNegara', 'TamanTasik'),
              ('Perdana', 'TamanTasik'), ('KLSentral', 'KLSentral'), ('MuziumNegara', 'KLSentral'), ('Brickfields', 'KLSentral'),
              ('KLTower', 'BukitNanas'), ('Petronas', 'KLCC'), ('Pavilion', 'BukitBintang')]:
    lab(n, L.REAL[n], pt)
dr.text(P(L.col_center(L.G + 1) - 30, L.row_center(L.RN + 3)), 'KAMPUNG BARU', fill=(0, 80, 0))
img.save('layout_preview.png')
print(img.size)
