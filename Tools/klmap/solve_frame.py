"""Pick the real->game scale k and the north edge of the Old Town patch so that the real Gombak and Klang
arms cross that edge exactly 3 grid pitches apart (the kampung's 2 blocks between two river columns)."""
import json, math
import numpy as np
d = json.load(open('kl_osm.json'))
mx = 111320 * math.cos(math.radians(3.1465)); my = 110574
def xy(p): return ((p['lon'] - 101.6955) * mx, (p['lat'] - 3.1489) * my)
rivers = {}
for el in d['elements']:
    t = el.get('tags', {})
    if t.get('waterway') == 'river' and el.get('geometry') and t.get('name') in ('Sungai Gombak', 'Sungai Kelang', 'Sungai Klang'):
        nm = 'Gombak' if 'Gombak' in t['name'] else 'Klang'
        rivers.setdefault(nm, []).append([xy(p) for p in el['geometry']])
def x_at(name, y):
    best = None
    for line in rivers[name]:
        for (x0, y0), (x1, y1) in zip(line, line[1:]):
            if (y0 - y) * (y1 - y) <= 0 and y0 != y1:
                x = x0 + (y - y0) / (y1 - y0) * (x1 - x0)
                if best is None or abs(x) < abs(best): best = x
    return best
PITCH = 116.0
for k in (0.6, 0.62, 0.65, 0.68, 0.7, 0.72):
    target = 3 * PITCH / k
    ys = np.arange(150, 700, 2.0)
    sep = [(y, x_at('Gombak', y), x_at('Klang', y)) for y in ys]
    sep = [(y, g, kl, kl - g) for y, g, kl in sep if g is not None and kl is not None]
    y, g, kl, s = min(sep, key=lambda r: abs(r[3] - target))
    print(f'k={k:.2f}: need {target:5.0f} m real apart -> north edge y={y:4.0f}  Gombak x={g:6.0f}  Klang x={kl:6.0f}  sep={s:5.0f}')
