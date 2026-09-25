"""Print the flyover vertex heights near a game point (debugging steep ramp starts).
    uv run --no-project --with shapely --with mapbox-earcut --with numpy --with pillow python probe_ramp.py KotaLama -252 246"""
import sys, math
from shapely.geometry import Point
import layout as L
from osm import OSM
from patches import PatchBuild

name, x, z = sys.argv[1], float(sys.argv[2]), float(sys.argv[3])
p = next(q for q in L.PATCHES if q.name == name)
pb = PatchBuild(OSM(), p)
pb.classify_roads(); pb.solve_elevations(); pb.build_water(); pb.build_roads()
q = Point(x, z)
for r in pb.fly:
    if r['g'].distance(q) > 40: continue
    cs = list(r['g'].coords)
    print(f"way {r['id']} {r['cls']} w={r['w']} layer={r['tags'].get('layer')} bridge={r['tags'].get('bridge')} dist={r['g'].distance(q):.1f}")
    s = 0.0
    for i, ((cx, cz), h) in enumerate(zip(cs, r['h'])):
        if i: s += math.dist(cs[i - 1], cs[i])
        print(f"   {i:2d} s={s:6.1f} ({cx:7.1f},{cz:7.1f}) h={h:5.2f} ground_node={r['nodes'][i] in pb.ground_nodes}")
