"""
KL environment kit (P0): env_shared_street_kit + env_chowkit_market.

Conventions (after import into Unity):
  * 1 unit = 1 m, road modules are 10 m x 10 m, origin at the tile CENTRE, driving
    surface at y = 0 (asphalt slab 0.3 m thick below it).
  * "north" = Unity +Z. Straight roads run north-south. Corner connects north + east.
    T-junction opens north, south and east (closed side = west).
  * Kerb/sidewalk edge: 10 m long (along Z) x 3 m deep, road side on -X, top at y = 0.18.
  * Props: origin at ground centre, front = +Z.
Authored here with north/front = -Y (turned 180 at assembly like the vehicles).
"""
import math
import random
from mathutils import Vector

import kl_core as K

SLAB = 0.3


def _slab(m, color="asphalt"):
    m.box((0, 0, -SLAB / 2), (10, 10, SLAB), color)


def _dash_ns(m, x, color="lane_white", length=3.0, gap=2.0, w=0.18):
    y = -5 + gap / 2 + length / 2
    while y < 5:
        m.box((x, y, 0.006), (w, length, 0.012), color)
        y += length + gap


def road_straight(wet=False):
    m = K.Mesher()
    _slab(m, "wet_asphalt" if wet else "asphalt")
    for s in (1, -1):
        m.box((s * 4.55, 0, 0.006), (0.2, 10, 0.012), "lane_yellow")        # yellow edge lines
    _dash_ns(m, 0)
    if wet:  # puddles with bright reflections: single upward faces (no thickness, so the
        rng = random.Random(4)          # inverted-hull outline never wraps them in black)
        for i in range(5):
            x, y = rng.uniform(-3.5, 3.5), rng.uniform(-4, 4)
            rx, ry = rng.uniform(0.5, 1.1), rng.uniform(0.8, 1.7)
            flat_poly(m, [(x + math.cos(a) * rx, y + math.sin(a) * ry) for a in
                          (k / 9 * math.tau + rng.uniform(-0.15, 0.15) for k in range(9))], 0.004, "glass")
            w, ln = 0.08, rng.uniform(0.5, min(1.2, ry * 1.4))
            flat_poly(m, [(x + 0.2 - w, y - ln / 2), (x + 0.2 + w, y - ln / 2), (x + 0.2 + w, y + ln / 2), (x + 0.2 - w, y + ln / 2)],
                      0.006, rng.choice(["tail_red", "lamp_glow", "headlamp"]))
    return m


def flat_poly(m, pts, z, color):
    """One upward-facing n-gon lying on the road (decals: puddles, reflections)."""
    import bmesh
    tmp = bmesh.new()
    vs = [tmp.verts.new((x, y, z)) for x, y in pts]
    f = tmp.faces.new(vs)
    f.normal_update()
    if f.normal.z < 0:
        f.normal_flip()
    m._merge(tmp, color)


def road_cross():
    m = K.Mesher()
    _slab(m)
    for s in (1, -1):                           # stop lines on the four approaches
        m.box((s * 2.3, s * 4.7, 0.006), (4.4, 0.3, 0.012), "lane_white")
        m.box((s * 4.7, -s * 2.3, 0.006), (0.3, 4.4, 0.012), "lane_white")
    return m


def road_t():
    """Opens north (-Y here), south and east (+X here); closed west gets an edge line."""
    m = K.Mesher()
    _slab(m)
    m.box((-4.55, 0, 0.006), (0.2, 10, 0.012), "lane_yellow")
    for s in (1, -1):
        m.box((s * 2.3, s * 4.7, 0.006), (4.4, 0.3, 0.012), "lane_white")
    m.box((4.7, 2.3, 0.006), (0.3, 4.4, 0.012), "lane_white")
    return m


def road_corner():
    """Connects north (-Y) and east (+X): the outer SW corner gets a curved yellow edge."""
    m = K.Mesher()
    _slab(m)
    # arc centred on the inner corner (x=+5, y=-5), from the north edge round to the east edge
    cx, cy, r = 5.0, -5.0, 9.55
    steps = 12
    for i in range(steps):
        a0 = math.pi / 2 + i / steps * (math.pi / 2)
        a1 = a0 + (math.pi / 2) / steps
        p0 = Vector((cx + math.cos(a0) * r, cy + math.sin(a0) * r, 0.006))
        p1 = Vector((cx + math.cos(a1) * r, cy + math.sin(a1) * r, 0.006))
        mid = (p0 + p1) / 2
        ang = math.atan2(p1.y - p0.y, p1.x - p0.x)
        m.box(mid, ((p1 - p0).length + 0.05, 0.2, 0.012), "lane_yellow", rot=(0, 0, ang))
    return m


def crosswalk():
    m = road_straight()
    for i in range(9):                           # zebra bars across the road
        x = -4 + i * 1.0
        m.box((x, 0, 0.012), (0.55, 3.2, 0.012), "lane_white")
    return m


def curb_edge():
    """Sidewalk strip: road side on -X. Black/white striped kerb face, tiled paving top."""
    m = K.Mesher()
    top = 0.18
    m.box((0, 0, (top - SLAB) / 2), (3, 10, top + SLAB), "pavement")
    for i in range(10):                          # kerb blocks alternating black / white
        m.box((-1.42, -4.5 + i, top / 2 + 0.005), (0.18, 0.98, top + 0.012),
              "curb_black" if i % 2 == 0 else "curb_white")
    for i in range(1, 10):                       # paving joints
        m.box((0.1, -5 + i, top + 0.003), (2.8, 0.04, 0.006), "tile_red")
    m.box((1.46, 0, top + 0.003), (0.08, 10, 0.006), "tile_red")
    return m


def curb_corner():
    """3 x 3 m sidewalk corner square (road on -X and -Y sides here)."""
    m = K.Mesher()
    top = 0.18
    m.box((0, 0, (top - SLAB) / 2), (3, 3, top + SLAB), "pavement")
    m.box((-1.42, 0.09, top / 2 + 0.005), (0.18, 2.82, top + 0.012), "curb_white")
    m.box((0.09, -1.42, top / 2 + 0.005), (2.82, 0.18, top + 0.012), "curb_black")
    m.box((-1.42, -1.42, top / 2 + 0.005), (0.18, 0.18, top + 0.012), "curb_black")
    return m


def shophouse_shell(wall="shop_yellow", trim="shop_teal", shutter="shutter_red"):
    """5 m wide x 10 m deep pre-war shophouse, facade toward -Y (front). Upper floor
    overhangs the five-foot way on columns; louvred shutters, balcony planters, parapet."""
    m = K.Mesher()
    w, d, h1, h2 = 5.0, 10.0, 3.6, 3.4
    fy = -d / 2
    # ground floor set back 1.8 m (five-foot way), upper floor flush with the columns
    m.box((0, 0.9, h1 / 2), (w, d - 1.8, h1), wall)
    m.box((0, 0, h1 + h2 / 2), (w, d, h2), wall)
    m.box((0, fy + 0.05, h1 + 0.2), (w + 0.1, 0.35, 0.4), trim)                 # overhang band
    m.box((0, fy + 0.05, h1 + h2 + 0.25), (w + 0.2, 0.5, 0.5), trim)            # cornice
    m.box((0, fy + 0.2, h1 + h2 + 0.9), (w, 0.3, 0.8), wall)                    # parapet
    m.prism((0, fy + 0.2, h1 + h2 + 1.3), (1.6, 0.3, 0.6), trim, rot=(math.pi / 2, 0, 0))
    m.box((0, 0.5, h1 + h2 + 0.3), (w, d - 1, 0.6), "roof_red", taper=(1.0, 0.85))
    for s in (1, -1):                                                           # columns
        m.box((s * (w / 2 - 0.25), fy + 0.25, h1 / 2), (0.5, 0.5, h1), trim)
    # shopfront: open dark interior with a counter, fluorescent light, and a sign board
    m.box((0, fy + 1.82, h1 * 0.45), (w - 1.2, 0.08, h1 * 0.8), "window_dark")
    m.box((0, fy + 1.6, 0.5), (w - 1.6, 0.4, 1.0), "plaster")
    m.box((0, fy + 1.78, h1 - 0.35), (w - 0.8, 0.12, 0.5), "sign_blank")       # signboard (text in Unity)
    m.box((0, fy + 1.2, h1 - 0.05), (1.4, 0.12, 0.06), "lamp_glow")            # tube light under the arcade
    # upper floor: two louvred-shutter windows with frames and planters on the sill
    for x in (-1.2, 1.2):
        z = h1 + h2 * 0.5
        m.box((x, fy - 0.02, z), (1.2, 0.08, 1.9), "plaster")
        for s in (1, -1):
            m.box((x + s * 0.28, fy - 0.07, z), (0.52, 0.06, 1.7), shutter)
            for k in range(6):
                m.box((x + s * 0.28, fy - 0.11, z - 0.7 + k * 0.28), (0.5, 0.03, 0.05), "wood_dark")
        m.box((x, fy - 0.2, h1 + 0.35), (1.1, 0.4, 0.3), "pot_terracotta", taper=(0.9, 0.9))
        for k in range(3):
            m.ball((x - 0.35 + k * 0.35, fy - 0.2, h1 + 0.65), (0.28, 0.24, 0.3), "leaf" if k % 2 else "leaf_dark", seg=6, rings=4)
    return m


def awning(color="shop_teal", width=5.0):
    """Sloped canvas awning, attaches to a facade (hinge line at origin top, sloping to -Y)."""
    m = K.Mesher()
    m.box((0, -0.8, -0.35), (width, 1.8, 0.08), color, rot=(0.4, 0, 0))
    m.box((0, -1.62, -0.72), (width, 0.06, 0.3), color)                         # front valance
    for s in (1, -1):
        m.tube((s * (width / 2 - 0.1), 0, 0), (s * (width / 2 - 0.1), -1.6, -0.7), 0.025, 0.025, "metal_dark", seg=4)
    return m


def lamp_post():
    m = K.Mesher()
    m.box((0, 0, 0.3), (0.5, 0.5, 0.6), "lamp_grey", bevel=0.04)
    m.tube((0, 0, 0.5), (0, 0, 7.0), 0.13, 0.1, "lamp_grey", seg=8)
    m.tube((0, 0, 6.9), (0, -1.6, 7.3), 0.07, 0.07, "lamp_grey", seg=6)
    m.box((0, -1.9, 7.2), (0.45, 0.9, 0.22), "lamp_grey", bevel=0.03)
    m.box((0, -1.9, 7.08), (0.35, 0.75, 0.04), "lamp_glow")
    return m


def wire_pole():
    m = K.Mesher()
    m.tube((0, 0, 0), (0, 0, 8.0), 0.16, 0.12, "concrete", seg=8)
    m.box((0, 0, 7.4), (1.8, 0.12, 0.12), "wood_dark")
    for x in (-0.8, -0.3, 0.3, 0.8):
        m.cyl((x, 0, 7.55), 0.05, 0.04, 0.16, "white", seg=6)
    m.box((0, 0.2, 5.0), (0.4, 0.3, 0.6), "metal_dark", bevel=0.03)             # transformer box
    return m


def planter(plant="leaf"):
    m = K.Mesher()
    m.box((0, 0, 0.4), (1.0, 1.0, 0.8), "pot_terracotta", taper=(1.15, 1.15), bevel=0.03)
    m.box((0, 0, 0.83), (1.1, 1.1, 0.08), "pot_terracotta")
    rng = random.Random(7)
    for i in range(7):                                                          # spiky leaves
        a = i / 7 * math.tau
        tip = Vector((math.cos(a) * 0.9, math.sin(a) * 0.9, 1.9 + rng.uniform(-0.2, 0.3)))
        m.tube((0, 0, 0.85), tip, 0.18, 0.02, plant if i % 2 else "leaf_dark", seg=4, squash=(1, 0.3),
               twist=a)
    m.tube((0, 0, 0.85), (0, 0, 2.2), 0.15, 0.02, "leaf", seg=4, squash=(1, 0.3))
    return m


def crate(color="crate_red", fruit=None):
    m = K.Mesher()
    m.box((0, 0, 0.3), (0.9, 0.6, 0.6), color, bevel=0.02)
    for s in (1, -1):
        m.box((s * 0.455, 0, 0.42), (0.02, 0.3, 0.12), "charcoal_dark")        # hand holes
    if fruit:
        for i in range(6):
            m.ball(((i % 3 - 1) * 0.26, (i // 3 - 0.5) * 0.26, 0.66), (0.14, 0.14, 0.13), fruit, seg=6, rings=4)
    return m


def crate_stack():
    m = K.Mesher()
    cols = ["crate_red", "crate_blue", "crate_yellow", "crate_green"]
    for i, (x, y, z) in enumerate(((0, 0, 0), (0.95, 0.05, 0), (0.45, 0, 0.62), (-0.2, 0.7, 0))):
        m.box((x, y, z + 0.3), (0.9, 0.6, 0.6), cols[i % 4], bevel=0.02)
        m.box((x + 0.455, y, z + 0.42), (0.02, 0.3, 0.12), "charcoal_dark")
    for i in range(5):
        m.ball((0.3 + (i % 3) * 0.24, 0.0 + (i // 3) * 0.22, 1.3), (0.13, 0.13, 0.12), "fruit_orange", seg=6, rings=4)
    return m


def market_umbrella():
    m = K.Mesher()
    m.cyl((0, 0, 0.1), 0.35, 0.4, 0.2, "concrete_dark", seg=8)
    m.tube((0, 0, 0.2), (0, 0, 2.9), 0.04, 0.04, "metal", seg=6)
    cols = ["umb_red", "umb_yellow", "umb_blue", "umb_green"] * 2
    r, h = 1.7, 0.55
    for i in range(8):                                                          # coloured gores
        a0, a1 = i / 8 * math.tau, (i + 1) / 8 * math.tau
        p0 = Vector((math.cos(a0) * r, math.sin(a0) * r, 2.35))
        p1 = Vector((math.cos(a1) * r, math.sin(a1) * r, 2.35))
        apex = Vector((0, 0, 2.35 + h))
        _tri_panel(m, apex, p0, p1, cols[i])
    m.ball((0, 0, 2.95), (0.07, 0.07, 0.07), "umb_yellow", seg=6, rings=4)
    return m


def _tri_panel(m, a, b, c, color, thick=0.04):
    import bmesh
    tmp = bmesh.new()
    n = (b - a).cross(c - a).normalized() * thick
    vs = [tmp.verts.new(v) for v in (a, b, c, a - n, b - n, c - n)]
    tmp.faces.new((vs[0], vs[1], vs[2]))
    tmp.faces.new((vs[5], vs[4], vs[3]))
    for i, j in ((0, 1), (1, 2), (2, 0)):
        tmp.faces.new((vs[i], vs[j], vs[j + 3], vs[i + 3]))
    bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
    m._merge(tmp, color)


def stall_counter():
    """Hawker stall: steel counter, glass display case with food, pots, gas tank."""
    m = K.Mesher()
    m.box((0, 0, 0.45), (2.2, 0.9, 0.9), "steel", bevel=0.02)
    m.box((0, 0, 0.93), (2.3, 1.0, 0.06), "metal")
    m.box((-0.45, 0.05, 1.3), (1.1, 0.7, 0.7), "glass")
    for k in range(4):
        m.ball((-0.8 + k * 0.23, 0.05, 1.07), (0.09, 0.09, 0.07), "fruit_orange", seg=6, rings=4)
    m.box((-0.45, 0.05, 1.67), (1.15, 0.75, 0.05), "shop_red")
    for x in (0.55, 0.9):
        m.cyl((x, 0.05, 1.12), 0.18, 0.18, 0.32, "metal", seg=10)
        m.cyl((x, 0.05, 1.29), 0.19, 0.19, 0.03, "metal_dark", seg=10)
    m.cyl((0.8, 0.6, 0.35), 0.18, 0.18, 0.7, "shop_red", seg=8)                # gas tank
    for s in (1, -1):
        m.cyl((s * 0.9, 0, 0.08), 0.08, 0.08, 0.1, "rubber", seg=8, rot=(0, math.pi / 2, 0))
    return m


def signboard_blank(color="shop_red"):
    m = K.Mesher()
    m.box((0, 0, 0), (3.0, 0.12, 0.8), color, bevel=0.02)
    m.box((0, -0.065, 0), (2.7, 0.02, 0.6), "sign_blank")
    return m


def stool(color="stool_red"):
    m = K.Mesher()
    m.cyl((0, 0, 0.44), 0.18, 0.2, 0.05, color, seg=10)
    for i in range(4):
        a = i / 4 * math.tau + 0.78
        m.tube((math.cos(a) * 0.14, math.sin(a) * 0.14, 0.42), (math.cos(a) * 0.2, math.sin(a) * 0.2, 0.0), 0.03, 0.03, color, seg=4)
    return m


def bollard():
    m = K.Mesher()
    m.cyl((0, 0, 0.45), 0.13, 0.13, 0.9, "bollard", seg=8)
    m.ball((0, 0, 0.9), (0.14, 0.14, 0.1), "bollard", seg=8, rings=4)
    m.cyl((0, 0, 0.72), 0.135, 0.135, 0.1, "bollard_band", seg=8)
    return m


def palm(seed=3):
    rng = random.Random(seed)
    m = K.Mesher()
    h = rng.uniform(7.5, 9.5)
    lean = rng.uniform(-0.12, 0.12)
    top = Vector((lean * h, 0, h))
    segs = 7
    for i in range(segs):
        a, b = top * (i / segs), top * ((i + 1) / segs)
        m.tube(a, b, 0.3 - i * 0.02, 0.28 - i * 0.02, "trunk", seg=6)
    for i in range(9):                                                          # fronds
        ang = i / 9 * math.tau + rng.uniform(-0.2, 0.2)
        mid = top + Vector((math.cos(ang) * 1.6, math.sin(ang) * 1.6, 0.4))
        tip = top + Vector((math.cos(ang) * 3.2, math.sin(ang) * 3.2, -0.9))
        m.tube(top, mid, 0.4, 0.35, "leaf" if i % 2 else "leaf_dark", seg=4, squash=(1, 0.15), twist=ang)
        m.tube(mid, tip, 0.35, 0.03, "leaf" if i % 2 else "leaf_dark", seg=4, squash=(1, 0.15), twist=ang)
    for i in range(4):
        m.ball(top + Vector((math.cos(i * 1.6) * 0.3, math.sin(i * 1.6) * 0.3, -0.3)), (0.2, 0.2, 0.22), "fruit_green", seg=6, rings=4)
    return m


# ---------------------------------------------------------------------- Chow Kit market
def market_canopy(color="umb_blue"):
    """4 m x 3 m covered stall: pole frame, sloped tarp, a string of warm bulbs, produce table."""
    m = K.Mesher()
    for x in (-1.9, 1.9):
        for y in (-1.4, 1.4):
            m.tube((x, y, 0), (x, y, 2.6 if y < 0 else 3.0), 0.04, 0.04, "metal_dark", seg=4)
    m.box((0, 0, 2.9), (4.2, 3.2, 0.06), color, rot=(0.13, 0, 0))
    m.box((0, -1.62, 2.45), (4.2, 0.05, 0.35), color)                           # valance
    m.box((0, 1.62, 2.8), (4.2, 0.05, 0.3), color)
    for i in range(6):                                                          # stall lights
        x = -1.6 + i * 0.64
        m.tube((x, -0.6, 2.8), (x, -0.6, 2.35), 0.008, 0.008, "wire_black", seg=3)
        m.ball((x, -0.6, 2.28), (0.08, 0.08, 0.1), "lamp_glow", seg=6, rings=4)
    # produce table with colour blocks of fruit and veg
    m.box((0, -0.4, 0.8), (3.6, 1.4, 0.08), "wood")
    for x in (-1.6, 1.6):
        m.box((x, -0.4, 0.4), (0.1, 1.2, 0.8), "wood_dark")
    produce = ["fruit_green", "fruit_orange", "fruit_red", "banana", "leaf", "fruit_orange"]
    for i, p in enumerate(produce):
        x = -1.45 + i * 0.58
        m.box((x, -0.4, 0.92), (0.52, 1.1, 0.16), "crate_green" if i % 2 else "crate_yellow")
        for k in range(4):
            m.ball((x + (k % 2 - 0.5) * 0.22, -0.4 + (k // 2 - 0.5) * 0.4, 1.08), (0.12, 0.14, 0.1), p, seg=6, rings=4)
    # bananas hanging from the frame
    for x in (-1.2, 1.2):
        for k in range(3):
            m.box((x + k * 0.12, -1.3, 2.1 - k * 0.1), (0.08, 0.08, 0.35), "banana", rot=(0, 0.3, 0))
    return m


def traffic_barrier():
    m = K.Mesher()
    m.box((0, 0, 0.45), (2.0, 0.45, 0.9), "barrier_red", taper=(0.9, 0.55), bevel=0.04)
    for i in range(3):
        m.box((-0.6 + i * 0.6, -0.14, 0.55), (0.3, 0.2, 0.25), "white", rot=(0.3, 0, 0))
    return m


def tarp_stack():
    m = K.Mesher()
    m.box((0, 0, 0.25), (1.6, 1.2, 0.5), "tarp_blue", taper=(0.85, 0.8), bevel=0.08)
    m.box((0, 0, 0.52), (1.3, 0.9, 0.06), "tarp_blue")
    m.tube((-0.8, -0.6, 0.3), (0.8, 0.6, 0.35), 0.015, 0.015, "cord_red", seg=4)
    return m


# id -> (builder, collision (center, size) or None)
KIT = {
    "env_road_straight": (road_straight, None),
    "env_road_straight_wet": (lambda: road_straight(True), None),
    "env_road_corner": (road_corner, None),
    "env_road_t": (road_t, None),
    "env_road_cross": (road_cross, None),
    "env_crosswalk": (crosswalk, None),
    "env_curb_edge": (curb_edge, None),
    "env_curb_corner": (curb_corner, None),
    "env_chowkit_shop_a": (lambda: shophouse_shell("shop_yellow", "shop_teal", "shutter_red"), ((0, 0.9, 1.8), (5, 8.2, 3.6))),
    "env_chowkit_shop_b": (lambda: shophouse_shell("shop_teal", "shop_yellow", "shutter_red"), ((0, 0.9, 1.8), (5, 8.2, 3.6))),
    "env_chowkit_shop_c": (lambda: shophouse_shell("shop_pink", "shop_blue", "shop_green"), ((0, 0.9, 1.8), (5, 8.2, 3.6))),
    "env_chowkit_shop_d": (lambda: shophouse_shell("shop_blue", "shop_yellow", "shutter_red"), ((0, 0.9, 1.8), (5, 8.2, 3.6))),
    "env_chowkit_shop_e": (lambda: shophouse_shell("shop_orange", "shop_green", "shop_teal"), ((0, 0.9, 1.8), (5, 8.2, 3.6))),
    "env_awning_teal": (lambda: awning("shop_teal"), None),
    "env_awning_green": (lambda: awning("shop_green"), None),
    "env_awning_red": (lambda: awning("shop_red"), None),
    "env_awning_blue": (lambda: awning("shop_blue"), None),
    "env_lamp_post": (lamp_post, ((0, 0, 3.5), (0.35, 0.35, 7))),
    "env_wire_pole": (wire_pole, ((0, 0, 4), (0.35, 0.35, 8))),
    "env_planter": (planter, ((0, 0, 0.45), (1.1, 1.1, 0.9))),
    "env_crate": (crate, ((0, 0, 0.3), (0.9, 0.6, 0.6))),
    "env_crate_oranges": (lambda: crate("crate_blue", "fruit_orange"), ((0, 0, 0.35), (0.9, 0.6, 0.7))),
    "env_crate_stack": (crate_stack, ((0.35, 0.2, 0.6), (1.9, 1.3, 1.2))),
    "env_market_umbrella": (market_umbrella, ((0, 0, 1.4), (0.3, 0.3, 2.8))),
    "env_stall_counter": (stall_counter, ((0, 0, 0.8), (2.3, 1.0, 1.6))),
    "env_signboard_blank": (signboard_blank, None),
    "env_stool_red": (lambda: stool("stool_red"), None),
    "env_stool_blue": (lambda: stool("stool_blue"), None),
    "env_bollard": (bollard, ((0, 0, 0.45), (0.3, 0.3, 0.9))),
    "env_palm": (palm, ((0, 0, 3), (0.6, 0.6, 6))),
    "env_market_canopy_blue": (lambda: market_canopy("umb_blue"), ((0, -0.4, 0.5), (3.6, 1.4, 1.0))),
    "env_market_canopy_red": (lambda: market_canopy("umb_red"), ((0, -0.4, 0.5), (3.6, 1.4, 1.0))),
    "env_market_canopy_teal": (lambda: market_canopy("shop_teal"), ((0, -0.4, 0.5), (3.6, 1.4, 1.0))),
    "env_market_canopy_yellow": (lambda: market_canopy("umb_yellow"), ((0, -0.4, 0.5), (3.6, 1.4, 1.0))),
    "env_traffic_barrier": (traffic_barrier, ((0, 0, 0.45), (2.0, 0.45, 0.9))),
    "env_tarp_stack": (tarp_stack, None),
}


def kit_parts(asset_id):
    """Parts for one module; the visible mesh is named <id>_geo (or <id>_LOD0/_LOD1 once
    kl_build adds a distance version), plus the COL_<id> box proxy."""
    builder, col = KIT[asset_id]
    parts = [(f"{asset_id}_geo", builder(), None, None)]   # unique names: a kit scene holds many modules
    if col:
        c = K.Mesher()
        c.box(col[0], col[1], "rubber")
        parts.append((f"COL_{asset_id}", c, None, "__collision__"))
    return parts
