"""The long axis of each landmark's real footprint (the biggest OSM building / pitch at its anchor), as a
Unity yaw that lines a model's long side up with it. Compare with CityBuilder.LandmarkFit.
    uv run --no-project --with shapely python probe_landmark_axes.py"""
import math
from shapely.geometry import Point
import layout as L
from osm import OSM

o = OSM()
for name in ('MasjidJamek', 'SultanAbdulSamad', 'PasarSeni', 'Merdeka118', 'StadiumMerdeka', 'MasjidNegara', 'KLTower',
             'Petronas', 'KLSentral', 'MuziumNegara', 'TuguNegara'):
    x, y = L.REAL[name]
    p = Point(x, y)
    cands = [f['poly'] for f in o.buildings + o.pitches if f['poly'].distance(p) < 25 and f['poly'].area > 300]
    if not cands:
        print(f'{name:18s} no footprint'); continue
    g = max(cands, key=lambda q: q.area)
    r = g.minimum_rotated_rectangle
    cs = list(r.exterior.coords)
    e = max(((cs[i], cs[i + 1]) for i in range(4)), key=lambda ab: math.dist(*ab))
    ang = math.degrees(math.atan2(e[1][1] - e[0][1], e[1][0] - e[0][0])) % 180      # math angle of the long axis
    side = min(math.dist(cs[i], cs[i + 1]) for i in range(4))
    # Unity yaw that turns local +X onto that axis: (cos yaw, -sin yaw) = (cos a, sin a)
    print(f'{name:18s} footprint {math.dist(*e):5.0f} x {side:4.0f} m  long axis {ang:5.1f} deg from east  -> yaw for a model long in X: {(-ang) % 360:5.1f} (or {(180 - ang) % 360:5.1f}),'
          f' long in Z: {(90 - ang) % 360:5.1f} (or {(270 - ang) % 360:5.1f})')
