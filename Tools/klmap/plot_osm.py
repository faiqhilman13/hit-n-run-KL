import json, math
from PIL import Image, ImageDraw
from collections import Counter
d = json.load(open('kl_osm.json'))
S, N, W, E = 3.117, 3.176, 101.678, 101.724
lat0 = (S + N) / 2
mx = 111320 * math.cos(math.radians(lat0)); my = 110574
Wm, Hm = (E - W) * mx, (N - S) * my
sc = 0.3   # px per metre
img = Image.new('RGB', (int(Wm * sc), int(Hm * sc)), (238, 234, 222))
dr = ImageDraw.Draw(img)
def P(g): return [((p['lon'] - W) * mx * sc, (N - p['lat']) * my * sc) for p in g]
col = {'motorway': (200, 60, 60), 'trunk': (220, 120, 40), 'primary': (230, 170, 40), 'secondary': (200, 200, 60), 'tertiary': (150, 150, 150),
       'residential': (170, 170, 170), 'unclassified': (170, 170, 170)}
wid = {'motorway': 5, 'trunk': 5, 'primary': 4, 'secondary': 3, 'tertiary': 2, 'residential': 1, 'unclassified': 1}
stats = Counter()
for el in d['elements']:
    t = el.get('tags', {}); g = el.get('geometry')
    if not g: continue
    if t.get('leisure') in ('park', 'garden') or t.get('landuse') in ('forest', 'grass', 'cemetery') or t.get('natural') in ('wood', 'scrub'):
        if len(g) > 2: dr.polygon(P(g), fill=(170, 210, 150))
    if t.get('natural') == 'water' and len(g) > 2: dr.polygon(P(g), fill=(120, 170, 220))
for el in d['elements']:
    t = el.get('tags', {}); g = el.get('geometry')
    if not g: continue
    if 'building' in t and len(g) > 2:
        lv = t.get('building:levels'); h = t.get('height')
        try: lvl = float(lv) if lv else (float(h) / 3.5 if h else 0)
        except: lvl = 0
        stats['levels_tagged' if lvl else 'no_levels'] += 1
        c = (90, 90, 110) if lvl >= 20 else (140, 130, 130) if lvl >= 6 else (200, 170, 160)
        dr.polygon(P(g), fill=c)
for el in d['elements']:
    t = el.get('tags', {}); g = el.get('geometry')
    if not g: continue
    hw = t.get('highway', '')
    base = hw.replace('_link', '')
    if base in col:
        c = col[base]
        if t.get('bridge') == 'yes': c = (60, 60, 200); stats['bridge_' + base] += 1
        if t.get('tunnel') == 'yes': c = (60, 200, 60); stats['tunnel'] += 1
        if t.get('junction') == 'roundabout': c = (255, 0, 255); stats['roundabout'] += 1
        if t.get('oneway') == 'yes': stats['oneway'] += 1
        dr.line(P(g), fill=c, width=wid[base])
    if t.get('railway') in ('light_rail', 'monorail', 'rail') and t.get('tunnel') != 'yes':
        dr.line(P(g), fill=(0, 0, 0), width=2)
    if t.get('waterway') == 'river':
        dr.line(P(g), fill=(40, 100, 220), width=4)
img.save('osm_plot.png')
print(img.size, round(Wm), round(Hm), dict(stats))
