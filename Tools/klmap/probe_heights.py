"""How tall is downtown KL in the OSM data? Per patch: building count, how many carry levels/height tags,
the level distribution, and the share of the land the building footprints cover."""
from collections import Counter
from shapely.geometry import box
from shapely import affinity
from shapely.ops import unary_union
import layout as L
from osm import OSM, levels

osm = OSM()
for p in L.PATCHES:
    x0, z0, x1, z1 = p.real_window
    win = box(x0, z0, x1, z1)
    bs = [b for b in osm.buildings if b['poly'].intersects(win)]
    tagged = [levels(b['tags']) for b in bs if levels(b['tags']) is not None]
    big = [b for b in bs if b['poly'].area > 1500]
    big_tagged = [b for b in big if levels(b['tags']) is not None]
    cover = unary_union([b['poly'].buffer(0) for b in bs]).intersection(win).area / win.area
    hist = Counter()
    for lv in tagged:
        hist['1-4' if lv <= 4 else '5-9' if lv < 10 else '10-19' if lv < 20 else '20-39' if lv < 40 else '40+'] += 1
    types = Counter(b['tags'].get('building', 'yes') for b in bs)
    print(f"{p.name:12s} n={len(bs):5d} tagged={len(tagged):4d} big(>1500m2)={len(big):4d} big_tagged={len(big_tagged):4d} cover={cover:.2f} "
          f"levels={dict(hist)} types={types.most_common(6)}")
