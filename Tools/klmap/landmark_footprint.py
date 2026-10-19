"""Real OSM footprints round a landmark, in the landmark model's authoring frame (kl_landmarks*.py: metres at game
scale, front = -Y, origin at the anchor), for a model placed at the anchor with a given Unity yaw.
    uv run --no-project --with shapely python landmark_footprint.py <Anchor> <yaw> [radius_m_real] [min_area]
Unity local (ux, uz) of a world offset (gx, gz) at yaw t: ux = gx cos t - gz sin t, uz = gx sin t + gz cos t; the
authoring frame is turned 180 (front -Y): (ax, ay) = (-ux, -uz)."""
import math, sys
from shapely.geometry import Point
import layout as L
from osm import OSM


def local(gx, gz, yaw):
    t = math.radians(yaw)
    ux, uz = gx * math.cos(t) - gz * math.sin(t), gx * math.sin(t) + gz * math.cos(t)
    return -ux, -uz


if __name__ == '__main__':
    name, yaw = sys.argv[1], float(sys.argv[2])
    rad = float(sys.argv[3]) if len(sys.argv) > 3 else 120
    amin = float(sys.argv[4]) if len(sys.argv) > 4 else 300
    o = OSM()
    x, y = L.REAL[name]
    p = Point(x, y)
    for f in sorted(o.buildings + o.pitches, key=lambda f: f['poly'].distance(p)):
        g = f['poly']
        if g.distance(p) > rad: break
        if g.area < amin: continue
        g = g.simplify(1.5)
        pts = [local((qx - x) * L.K, (qy - y) * L.K, yaw) for qx, qy in list(g.exterior.coords)[:-1]]
        t = f['tags']
        print(f"# {t.get('name', '')} {t.get('building', t.get('leisure', ''))} lv {t.get('building:levels', '')} h {t.get('height', '')}"
              f" area(game) {g.area * L.K * L.K:.0f}")
        print('[' + ', '.join(f'({a:.1f}, {b:.1f})' for a, b in pts) + ']')
