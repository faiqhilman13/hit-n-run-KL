"""
KL's rail stations inside the real-KL patches, for the game's elevated stations (CityBuilder.Stations).

Reads the station footprints from kl_osm.json, keeps the light-rail and monorail ones whose middle falls in a patch,
moves them into game coordinates with the patch's frame, and writes Assets/KampungRun/Resources/KLMap/stations.json:
    [{"name", "code", "kind": "monorail" | "light_rail", "patch", "ring": [[x, z], ...], "centre": [x, z]}]
The game snaps each one onto its elevated line; stations whose line isn't elevated in the game are skipped there.

    uv run --no-project --with shapely python stations.py
"""
import json, os
from shapely import affinity
from shapely.geometry import Polygon, Point

import layout as L
from osm import xy

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.normpath(os.path.join(HERE, '..', '..', 'Assets', 'KampungRun', 'Resources', 'KLMap'))


def main():
    d = json.load(open(os.path.join(HERE, 'kl_osm.json'), encoding='utf-8'))
    out = []
    for el in d['elements']:
        t = el.get('tags', {})
        if t.get('railway') != 'station' or t.get('station') not in ('light_rail', 'monorail'): continue
        g = el.get('geometry')
        if not g and el.get('members'):
            g = next((m['geometry'] for m in el['members'] if m.get('role') == 'outer' and m.get('geometry')), None)
        if not g or len(g) < 3: continue
        poly = Polygon([xy(p) for p in g]).buffer(0)
        if poly.is_empty: continue
        for p in L.PATCHES:
            m = [L.K, 0, 0, L.K, p.frame.fx, p.frame.fz]
            gp = affinity.affine_transform(poly, m)
            x0, z0, x1, z1 = p.rect
            c = gp.centroid
            if not (x0 <= c.x <= x1 and z0 <= c.y <= z1): continue
            ring = list(gp.exterior.coords)[:-1] if gp.geom_type == 'Polygon' else list(gp.convex_hull.exterior.coords)[:-1]
            name = t.get('name', '')
            code = name.split(' ')[0] if name[:2].isalpha() and any(ch.isdigit() for ch in name.split(' ')[0]) else ''
            plain = ' '.join(w for w in name.split(' ') if not (w[:2].isalpha() and any(ch.isdigit() for ch in w)))
            plain = plain.replace('(Monorail)', '').strip()
            out.append(dict(name=plain, code=code, kind=t['station'], patch=p.name,
                            ring=[[round(x, 2), round(z, 2)] for x, z in ring], centre=[round(c.x, 2), round(c.y, 2)]))
            print(f"{p.name:12s} {t['station']:10s} {code:10s} {plain}")
            break
    json.dump(out, open(os.path.join(RES, 'stations.json'), 'w'), indent=1)
    print(len(out), 'stations ->', os.path.join(RES, 'stations.json'))


main()
