"""
KL environment kit, P1 districts: env_kampung_baru + env_brickfields.

Same conventions as kl_env (P0): 1 unit = 1 m, authored with front/north = -Y and turned
180 at assembly so it imports facing Unity +Z; buildings sit on a 5 m / 10 m grid with their
origin at the ground centre; every solid module carries a COL_ box proxy. Both districts
reuse the shared road + kerb modules - these kits only add what makes each district read
differently (triptych left and centre panels).
"""
import math
import random
from mathutils import Vector

import kl_core as K
from kl_env import _slab, _tri_panel, flat_poly, SLAB


# ============================================================================ Kampung Baru
def kb_house(wall="kb_teal", trim="kb_mint", roof="zinc_red", w=8.0, seed=1):
    """Timber kampung house on stilts: front gable with carved fascia, verandah with a
    baluster railing and stairs, louvred windows, zinc roof. Facade toward -Y."""
    rng = random.Random(seed)
    m = K.Mesher()
    stilt_h, wall_h = 1.2, 3.0
    fz = stilt_h + 0.2                               # floor top
    by0, by1 = -2.0, 3.6                             # house body (y)
    vy0 = -3.6                                       # verandah front edge
    # stilts on concrete pads
    for x in (-w / 2 + 0.3, 0, w / 2 - 0.3):
        for y in (vy0 + 0.3, by0, (by0 + by1) / 2, by1 - 0.3):
            m.box((x, y, 0.08), (0.4, 0.4, 0.16), "concrete")
            m.box((x, y, stilt_h / 2 + 0.1), (0.2, 0.2, stilt_h), "timber_dark")
    m.box((0, (vy0 + by1) / 2, stilt_h + 0.1), (w, by1 - vy0, 0.2), "timber")                 # floor deck
    for y in range(int(vy0) + 1, int(by1)):                                                    # joist ends
        m.box((w / 2 + 0.02, y, stilt_h + 0.08), (0.06, 0.12, 0.14), "timber_dark")
    # walls + horizontal plank lines
    byc, bd = (by0 + by1) / 2, by1 - by0
    m.box((0, byc, fz + wall_h / 2), (w - 0.4, bd, wall_h), wall)
    for k in range(1, 10):
        z = fz + k * wall_h / 10
        m.box((0, by0 - 0.012, z), (w - 0.4, 0.02, 0.03), f"{wall}_dark" if wall == "kb_teal" else "timber_dark")
        for s in (1, -1):
            m.box((s * (w / 2 - 0.19), byc, z), (0.02, bd, 0.03), "timber_dark")
    for s in (1, -1):                                                                          # corner posts
        m.box((s * (w / 2 - 0.2), by0, fz + wall_h / 2), (0.18, 0.18, wall_h + 0.1), trim)
    # front: door + two louvred windows
    m.box((0, by0 - 0.03, fz + 1.05), (1.0, 0.06, 2.1), "timber_dark")
    m.box((0, by0 - 0.04, fz + 2.2), (1.1, 0.06, 0.1), trim)
    for x in (-w / 2 + 1.5, w / 2 - 1.5):
        z = fz + 1.6
        m.box((x, by0 - 0.03, z), (1.4, 0.06, 1.5), trim)
        for s in (1, -1):
            m.box((x + s * 0.33, by0 - 0.06, z), (0.6, 0.04, 1.3), wall)
            for k in range(7):
                m.box((x + s * 0.33, by0 - 0.09, z - 0.55 + k * 0.18), (0.56, 0.03, 0.05), trim)
    for s in (1, -1):                                                                          # side windows
        for y in (by0 + 1.3, by1 - 1.3):
            m.box((s * (w / 2 - 0.18), y, fz + 1.6), (0.06, 1.2, 1.4), trim)
            m.box((s * (w / 2 - 0.15), y, fz + 1.6), (0.04, 1.0, 1.2), "window_dark")
    # verandah railing: posts, rails, balusters (gap for the stairs)
    ry = vy0 + 0.1
    for x in (-w / 2 + 0.1, -0.7, 0.7, w / 2 - 0.1):
        m.box((x, ry, fz + 0.5), (0.12, 0.12, 1.0), trim)
        m.box((x, ry, fz + 2.6), (0.14, 0.14, 3.2), trim)                                     # verandah roof posts
    for x0, x1 in ((-w / 2 + 0.1, -0.7), (0.7, w / 2 - 0.1)):
        m.box(((x0 + x1) / 2, ry, fz + 0.95), (x1 - x0, 0.1, 0.08), trim)
        m.box(((x0 + x1) / 2, ry, fz + 0.15), (x1 - x0, 0.1, 0.06), trim)
        n = int((x1 - x0) / 0.22)
        for i in range(n):
            m.box((x0 + (i + 0.5) * (x1 - x0) / n, ry, fz + 0.55), (0.06, 0.05, 0.75), "white")
    for s in (1, -1):
        m.box((s * (w / 2 - 0.1), (vy0 + by0) / 2, fz + 0.95), (0.08, by0 - vy0, 0.08), trim)
    # stairs down from the verandah gap
    for k in range(5):
        z = fz - (k + 1) * (fz / 5.5)
        m.box((0, vy0 - 0.3 - k * 0.3, z + 0.06), (1.3, 0.34, 0.12), "timber")
    for s in (1, -1):
        m.tube((s * 0.68, vy0, fz + 0.9), (s * 0.68, vy0 - 1.7, 0.9), 0.04, 0.04, trim, seg=4)
        m.tube((s * 0.68, vy0, fz), (s * 0.68, vy0 - 1.7, 0.0), 0.08, 0.08, "timber_dark", seg=4)
    # main roof: gable facing front (ridge along Y), deep eaves, carved fascia boards
    rz = fz + wall_h
    span, rh = w + 1.2, 2.8
    ang = math.atan2(rh, span / 2)
    ln = math.hypot(span / 2, rh)
    roof_y0, roof_d = byc + 0.2, bd + 1.4
    for s in (1, -1):                                                                          # two roof slopes
        # rotating +X down about Y is a positive angle, so the right slope (s=+1) uses +ang
        m.box((s * span / 4, roof_y0, rz + rh / 2), (ln, roof_d, 0.12), roof, rot=(0, s * ang, 0))
        n = int(roof_d / 0.6)
        for k in range(n):                                                                     # corrugation ribs, eave-to-ridge
            y = roof_y0 - roof_d / 2 + (k + 0.5) * roof_d / n
            m.box((s * span / 4 + s * 0.07 * math.sin(ang), y, rz + rh / 2 + 0.07 * math.cos(ang)), (ln, 0.06, 0.05),
                  "zinc_dark", rot=(0, s * ang, 0))
    m.box((0, roof_y0, rz + rh + 0.02), (0.3, roof_d, 0.14), "zinc_dark")                    # ridge cap
    # front gable wall + fascia (white V boards with a crossed finial)
    gy = by0 - 0.02
    import bmesh
    tmp = bmesh.new()
    vs = [tmp.verts.new(v) for v in ((-w / 2 + 0.2, gy, rz), (w / 2 - 0.2, gy, rz), (0, gy, rz + rh - 0.2),
                                     (-w / 2 + 0.2, gy + 0.1, rz), (w / 2 - 0.2, gy + 0.1, rz), (0, gy + 0.1, rz + rh - 0.2))]
    tmp.faces.new((vs[0], vs[1], vs[2]))
    tmp.faces.new((vs[5], vs[4], vs[3]))
    for i, j in ((0, 1), (1, 2), (2, 0)):
        tmp.faces.new((vs[i], vs[j], vs[j + 3], vs[i + 3]))
    bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
    m._merge(tmp, wall)
    for s in (1, -1):                                                                          # carved fascia + crossed finial
        m.box((s * span / 4, roof_y0 - roof_d / 2 - 0.02, rz + rh / 2 + 0.05), (ln, 0.08, 0.3), "white", rot=(0, s * ang, 0))
        m.box((s * 0.18, roof_y0 - roof_d / 2 - 0.02, rz + rh + 0.3), (0.08, 0.08, 0.9), "white", rot=(0, s * 0.45, 0))
    m.box((0, gy - 0.05, rz + rh * 0.35), (0.8, 0.05, 0.5), trim)                              # gable vent
    for k in range(4):
        m.box((0, gy - 0.08, rz + rh * 0.35 - 0.18 + k * 0.12), (0.7, 0.03, 0.04), "timber_dark")
    # verandah lean-to roof
    m.box((0, (vy0 + by0) / 2 - 0.2, rz + 0.1), (w + 0.6, by0 - vy0 + 1.0, 0.1), roof, rot=(0.18, 0, 0))
    # a potted plant and a pair of slippers on the verandah
    m.cyl((w / 2 - 0.7, vy0 + 0.6, fz + 0.2), 0.22, 0.18, 0.4, "pot_terracotta", seg=8)
    m.ball((w / 2 - 0.7, vy0 + 0.6, fz + 0.65), (0.35, 0.35, 0.35), "leaf", seg=6, rings=4)
    for k in range(2):
        m.box((-0.35 + k * 0.18, by0 - 0.3, fz + 0.02), (0.1, 0.25, 0.03), "slipper_blue")
    return m


def banana_plant(seed=5):
    rng = random.Random(seed)
    m = K.Mesher()
    for i in range(3):                                                                         # pseudo-stems
        base = Vector((rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), 0))
        top = base + Vector((rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2), rng.uniform(2.0, 2.8)))
        m.tube(base, top, 0.14, 0.1, "grass_dark", seg=6)
        for k in range(4):                                                                     # big paddle leaves
            a = k / 4 * math.tau + i + rng.uniform(-0.3, 0.3)
            d = Vector((math.cos(a), math.sin(a), 0))
            mid = top + d * 0.9 + Vector((0, 0, 0.35))
            tip = top + d * 1.8 + Vector((0, 0, -0.3))
            col = "banana_leaf" if (i + k) % 2 else "leaf"
            m.tube(top, mid, 0.34, 0.4, col, seg=4, squash=(1, 0.08), twist=a + math.pi / 2)
            m.tube(mid, tip, 0.4, 0.05, col, seg=4, squash=(1, 0.08), twist=a + math.pi / 2)
    return m


def flower_pot(flower="flower_red", seed=2):
    rng = random.Random(seed)
    m = K.Mesher()
    m.cyl((0, 0, 0.3), 0.36, 0.28, 0.6, "pot_terracotta", seg=10)
    m.cyl((0, 0, 0.61), 0.4, 0.4, 0.06, "pot_terracotta", seg=10)
    m.ball((0, 0, 1.0), (0.55, 0.55, 0.5), "leaf", seg=8, rings=6)
    for i in range(10):
        a, r = rng.uniform(0, math.tau), rng.uniform(0.2, 0.5)
        m.ball((math.cos(a) * r, math.sin(a) * r, 1.0 + rng.uniform(0.1, 0.45)), (0.1, 0.1, 0.08), flower, seg=6, rings=4)
    return m


def shrub(seed=4):
    rng = random.Random(seed)
    m = K.Mesher()
    for i in range(6):
        a = i / 6 * math.tau
        m.ball((math.cos(a) * 0.45, math.sin(a) * 0.45, 0.45 + rng.uniform(0, 0.25)), (0.5, 0.5, 0.45),
               "leaf" if i % 2 else "leaf_dark", seg=7, rings=5)
    m.ball((0, 0, 0.85), (0.55, 0.55, 0.5), "leaf", seg=7, rings=5)
    return m


def rain_tree(seed=9):
    """Big shady angsana/rain tree: short trunk, forked limbs, a wide flat-topped canopy."""
    rng = random.Random(seed)
    m = K.Mesher()
    m.tube((0, 0, 0), (0, 0, 2.6), 0.45, 0.35, "trunk", seg=8)
    tops = []
    for i in range(4):
        a = i / 4 * math.tau + 0.4
        t = Vector((math.cos(a) * 1.8, math.sin(a) * 1.8, 4.6))
        m.tube((0, 0, 2.4), t, 0.3, 0.16, "trunk", seg=6)
        tops.append(t)
    for i in range(14):
        a, r = rng.uniform(0, math.tau), rng.uniform(0.5, 3.6)
        c = Vector((math.cos(a) * r, math.sin(a) * r, 5.4 + rng.uniform(-0.3, 0.6)))
        m.ball(c, (1.7, 1.7, 1.0), "leaf" if i % 3 else "leaf_dark", seg=8, rings=5)
    return m


def picket_fence(length=5.0):
    m = K.Mesher()
    for x in (-length / 2, length / 2):
        m.box((x, 0, 0.6), (0.14, 0.14, 1.2), "timber_dark")
        m.box((x, 0, 1.25), (0.2, 0.2, 0.1), "white")
    for z in (0.35, 0.9):
        m.box((0, 0.06, z), (length, 0.05, 0.08), "timber")
    n = int(length / 0.2)
    for i in range(n):
        x = -length / 2 + (i + 0.5) * length / n
        m.box((x, 0.1, 0.55), (0.1, 0.03, 1.0), "white")
        m.prism((x, 0.1, 1.05), (0.03, 0.1, 0.1), "white", rot=(0, 0, math.pi / 2))
    return m


def kb_lane():
    """10 m narrow kampung lane: 6 m of patched asphalt between grass verges (no kerbs),
    drainage channels either side. Same tile footprint as env_road_straight."""
    m = K.Mesher()
    m.box((0, 0, -SLAB / 2), (6.0, 10, SLAB), "asphalt")
    for s in (1, -1):
        m.box((s * 4.0, 0, (0.04 - SLAB) / 2), (2.0, 10, SLAB + 0.04), "grass")                  # verge (top 0.04)
        m.box((s * 3.1, 0, 0.0), (0.3, 10, 0.02), "concrete_dark")                             # drain
        m.box((s * 2.8, 0, 0.006), (0.12, 10, 0.012), "lane_white")
    rng = random.Random(8)
    for i in range(4):                                                                         # patch repairs
        x, y, w, h = rng.uniform(-2, 2), rng.uniform(-4, 4), rng.uniform(0.3, 0.7), rng.uniform(0.25, 0.6)
        flat_poly(m, [(x - w, y - h), (x + w, y - h), (x + w, y + h), (x - w, y + h)], 0.004, "concrete_dark")
    return m


def skyline_backdrop(seed=12):
    """Far-distance city backdrop card (80 m wide): stylised towers with window bands,
    a pair of stepped twin towers and a spired telecom tower. Flat (2 m deep) - it stands
    at the edge of the playable map, never close to the camera."""
    rng = random.Random(seed)
    m = K.Mesher()
    x = -40
    while x < 40:
        wdt = rng.uniform(5, 9)
        h = rng.uniform(22, 48)
        col = rng.choice(["skyline_blue", "skyline_dark"])
        m.box((x + wdt / 2, 0, h / 2), (wdt, 2, h), col)
        for k in range(int(h / 3)):
            m.box((x + wdt / 2, -1.02, 2 + k * 3), (wdt * 0.8, 0.04, 0.5), "glass")
        x += wdt + rng.uniform(0.5, 2.5)
    for s in (-1, 1):                                                                          # twin towers
        cx = 8 + s * 5
        z = 0
        for i, (r, hh) in enumerate(((3.2, 40), (2.6, 18), (2.0, 10), (1.3, 6))):
            m.cyl((cx, 0, z + hh / 2), r, r * 0.95, hh, "steel" if i % 2 == 0 else "skyline_blue", seg=10)
            z += hh
        m.tube((cx, 0, z), (cx, 0, z + 12), 0.4, 0.05, "steel", seg=6)
    m.box((8, 0, 44), (5, 1.6, 3), "steel")                                                    # skybridge
    tx = -22                                                                                   # telecom tower
    m.cyl((tx, 0, 32), 1.6, 1.0, 64, "concrete", seg=10)
    m.ball((tx, 0, 62), (4, 4, 3), "skyline_blue", seg=10, rings=6)
    m.tube((tx, 0, 64), (tx, 0, 80), 0.5, 0.08, "white", seg=6)
    return m


# ============================================================================ Brickfields
def _arch(m, cx, y, z_spring, r, color, thick=0.35, depth=0.4, n=9):
    """Semicircular arch ring made of faceted voussoirs."""
    for i in range(n):
        a0, a1 = i / n * math.pi, (i + 1) / n * math.pi
        p0 = Vector((cx + math.cos(a0) * r, y, z_spring + math.sin(a0) * r))
        p1 = Vector((cx + math.cos(a1) * r, y, z_spring + math.sin(a1) * r))
        mid = (p0 + p1) / 2
        ang = math.atan2(p1.z - p0.z, p1.x - p0.x)
        m.box(mid, ((p1 - p0).length + 0.03, depth, thick), color, rot=(0, -ang, 0))


def _motif(m, c, color_a="marigold", color_b="magenta"):
    """Painted flower rosette on a pillar: a diamond with four petals."""
    c = Vector(c)
    m.box(c, (0.2, 0.03, 0.2), color_a, rot=(0, math.pi / 4, 0))
    for dx, dz in ((0.14, 0), (-0.14, 0), (0, 0.14), (0, -0.14)):
        m.ball(c + Vector((dx, -0.01, dz)), (0.06, 0.02, 0.06), color_b, seg=6, rings=4)


def bf_shopfront(wall="bf_yellow", pillar="bf_blue", trim="bf_red", seed=1):
    """Brickfields five-foot-way shopfront (5 m x 10 m, 2 storeys): arched arcade on painted
    pillars with flower motifs, hanging brass bells, tiled walkway, arched upper windows."""
    rng = random.Random(seed)
    m = K.Mesher()
    w, d, h1, h2 = 5.0, 10.0, 4.0, 3.6
    fy = -d / 2
    m.box((0, 1.0, h1 / 2), (w, d - 2.0, h1), wall)                                            # ground floor (set back)
    m.box((0, 0, h1 + h2 / 2), (w, d, h2), wall)
    # tiled five-foot way (checker)
    for i in range(10):
        for j in range(4):
            m.box((-w / 2 + 0.25 + i * 0.5, fy + 0.25 + j * 0.5, 0.01), (0.5, 0.5, 0.02),
                  "tile_cream" if (i + j) % 2 else "tile_brown")
    # pillars with motifs, arch between them
    for s in (1, -1):
        px = s * (w / 2 - 0.3)
        m.box((px, fy + 0.3, h1 / 2), (0.6, 0.6, h1), pillar)
        m.box((px, fy + 0.3, 0.2), (0.75, 0.75, 0.4), trim)
        m.box((px, fy + 0.3, h1 - 0.25), (0.75, 0.75, 0.3), trim)
        for k in range(3):
            _motif(m, (px, fy - 0.01, 0.9 + k * 0.9))
    _arch(m, 0, fy + 0.3, h1 - 1.9, (w - 1.2) / 2, trim, thick=0.3, depth=0.62)
    m.box((0, fy + 0.3, h1 - 0.35), (w - 1.2, 0.6, 0.7), wall)                                 # spandrel
    for s in (1, -1):
        m.box((s * (w / 2 - 1.0), fy + 0.3, h1 - 1.05), (0.8, 0.6, 1.0), wall)
    # hanging brass bells under the arch and a shopfront
    for x in (-1.0, 0.0, 1.0):
        m.tube((x, fy + 0.6, h1 - 0.9), (x, fy + 0.6, h1 - 1.5), 0.01, 0.01, "wire_black", seg=3)
        m.cyl((x, fy + 0.6, h1 - 1.65), 0.1, 0.22, 0.3, "brass", seg=10)
        m.ball((x, fy + 0.6, h1 - 1.82), (0.05, 0.05, 0.05), "brass", seg=6, rings=4)
    m.box((0, fy + 2.02, h1 * 0.42), (w - 1.4, 0.08, h1 * 0.72), "window_dark")
    m.box((0, fy + 1.9, h1 - 0.9), (w - 1.2, 0.12, 0.55), "sign_blank")
    # upper floor: two arched windows with shutters, cornice, scalloped parapet
    for x in (-1.2, 1.2):
        z = h1 + 1.6
        m.box((x, fy - 0.03, z), (1.1, 0.08, 1.8), "window_dark")
        _arch(m, x, fy - 0.05, z + 0.9, 0.55, trim, thick=0.14, depth=0.12, n=7)
        for s in (1, -1):
            m.box((x + s * 0.72, fy - 0.08, z), (0.36, 0.05, 1.7), pillar)
        m.box((x, fy - 0.12, z - 1.0), (1.4, 0.24, 0.1), trim)
    m.box((0, fy + 0.05, h1 + h2 + 0.2), (w + 0.2, 0.5, 0.4), trim)
    for i in range(5):
        m.cyl((-2 + i, fy + 0.2, h1 + h2 + 0.55), 0.45, 0.45, 0.25, wall, seg=10, rot=(math.pi / 2, 0, 0))
    m.box((0, fy + 0.2, h1 + h2 + 0.5), (w, 0.25, 0.3), wall)
    m.box((0, 0.5, h1 + h2 + 0.3), (w, d - 1, 0.6), "roof_red", taper=(1.0, 0.85))
    for s in (1, -1):                                                                          # upper pilasters
        m.box((s * (w / 2 - 0.12), fy - 0.02, h1 + h2 / 2), (0.24, 0.1, h2), pillar)
    return m


def flower_stall(seed=3):
    """Garland and cut-flower stall: slatted table of buckets, a rail of hanging jasmine and
    marigold garlands, a small canopy."""
    rng = random.Random(seed)
    m = K.Mesher()
    m.box((0, 0, 0.75), (2.4, 1.0, 0.08), "wood")
    for x in (-1.1, 1.1):
        for y in (-0.4, 0.4):
            m.box((x, y, 0.37), (0.08, 0.08, 0.74), "wood_dark")
    cols = ["marigold", "jasmine", "flower_red", "magenta", "marigold", "jasmine"]
    for i, c in enumerate(cols):
        x = -0.95 + i * 0.38
        for y in (-0.22, 0.22):
            m.cyl((x, y, 0.94), 0.15, 0.12, 0.3, "steel", seg=10)
            for k in range(5):
                a = k / 5 * math.tau
                m.ball((x + math.cos(a) * 0.08, y + math.sin(a) * 0.08, 1.16 + rng.uniform(0, 0.08)), (0.08, 0.08, 0.07),
                       c, seg=4, rings=3)
            m.ball((x, y, 1.22), (0.08, 0.08, 0.07), c, seg=4, rings=3)
    # garland rail + strings of blossoms
    for x in (-1.2, 1.2):
        m.tube((x, -0.55, 0), (x, -0.55, 2.4), 0.03, 0.03, "metal_dark", seg=4)
    m.tube((-1.2, -0.55, 2.3), (1.2, -0.55, 2.3), 0.025, 0.025, "metal_dark", seg=4)
    for i in range(9):
        x = -1.0 + i * 0.25
        n = 6 + (i % 3)
        for k in range(n):
            m.ball((x, -0.55, 2.2 - k * 0.12), (0.06, 0.06, 0.06),
                   "jasmine" if (k + i) % 3 else ("marigold" if i % 2 else "flower_red"), seg=4, rings=3)
    m.box((0, 0.05, 2.55), (2.8, 1.5, 0.05), "bf_red", rot=(0.12, 0, 0))                          # canopy
    m.box((0, -0.72, 2.45), (2.8, 0.04, 0.22), "bf_red")
    return m


def bf_lamp():
    """Ornamental double street lamp: blue fluted post with brass lanterns."""
    m = K.Mesher()
    m.cyl((0, 0, 0.35), 0.35, 0.28, 0.7, "bf_blue", seg=8)
    m.cyl((0, 0, 0.72), 0.22, 0.22, 0.06, "brass", seg=8)
    m.tube((0, 0, 0.7), (0, 0, 5.6), 0.12, 0.09, "bf_blue", seg=8)
    for z in (1.6, 3.2, 4.8):
        m.cyl((0, 0, z), 0.14, 0.14, 0.08, "brass", seg=8)
    for s in (1, -1):
        m.tube((0, 0, 5.2), (s * 0.7, 0, 5.7), 0.05, 0.05, "bf_blue", seg=6)
        m.tube((s * 0.7, 0, 5.7), (s * 0.75, 0, 5.45), 0.04, 0.04, "bf_blue", seg=6)
        m.cyl((s * 0.75, 0, 5.2), 0.2, 0.14, 0.4, "lamp_glow", seg=8)
        m.cyl((s * 0.75, 0, 5.45), 0.08, 0.26, 0.14, "brass", seg=8)
        m.ball((s * 0.75, 0, 5.55), (0.05, 0.05, 0.08), "brass", seg=6, rings=4)
    m.ball((0, 0, 5.75), (0.12, 0.12, 0.2), "brass", seg=8, rings=5)
    return m


def bf_curb_edge():
    """Brickfields variant of the kerb/sidewalk strip: same 3 x 10 m footprint and
    black/white kerb as env_curb_edge, with a cream/brown checker-tiled top."""
    m = K.Mesher()
    top = 0.18
    m.box((0, 0, (top - SLAB) / 2), (3, 10, top + SLAB), "tile_cream")
    for i in range(10):
        m.box((-1.42, -4.5 + i, top / 2 + 0.005), (0.18, 0.98, top + 0.012), "curb_black" if i % 2 == 0 else "curb_white")
    for i in range(5):
        for j in range(20):
            if (i + j) % 2:
                m.box((-1.1 + i * 0.55 + 0.27, -5 + j * 0.5 + 0.25, top + 0.004), (0.53, 0.48, 0.008), "tile_brown")
    return m


def rail_viaduct():
    """20 m elevated rail viaduct segment along Y: two T-piers, box-girder deck at 8 m,
    parapets and a running rail beam. Place end to end; the LRT car rides on top."""
    m = K.Mesher()
    L, W, deck_z = 20.0, 8.0, 8.0
    for y in (-6.0, 6.0):
        m.box((0, y, 0.3), (2.4, 2.4, 0.6), "concrete_dark")
        m.box((0, y, deck_z / 2), (1.4, 1.4, deck_z - 1.4), "concrete")
        m.box((0, y, deck_z - 1.1), (6.0, 1.8, 1.0), "concrete", taper=(1.3, 1.0))
    m.box((0, 0, deck_z - 0.3), (W, L, 0.9), "concrete", taper=(1.0, 1.0), bevel=0.1)
    m.box((0, 0, deck_z - 0.9), (W * 0.7, L, 0.4), "concrete_dark")
    for s in (1, -1):
        m.box((s * (W / 2 - 0.15), 0, deck_z + 0.5), (0.3, L, 0.8), "concrete")
        m.box((s * (W / 2 - 0.15), 0, deck_z + 0.92), (0.4, L, 0.08), "white")
        for y in range(-9, 10, 3):                                                             # catenary masts
            m.tube((s * (W / 2 - 0.4), y, deck_z + 0.15), (s * (W / 2 - 0.4), y, deck_z + 5.2), 0.08, 0.08, "steel", seg=6)
            m.tube((s * (W / 2 - 0.4), y, deck_z + 5.0), (s * 1.8, y, deck_z + 5.0), 0.04, 0.04, "steel", seg=4)
    for x in (-1.8, 1.8):
        m.box((x, 0, deck_z + 0.25), (0.6, L, 0.2), "concrete_dark")                           # track plinths
        for s in (1, -1):
            m.box((x + s * 0.35, 0, deck_z + 0.4), (0.1, L, 0.1), "steel")
    for x in (-1.8, 1.8):                                                                      # contact wires
        m.box((x, 0, deck_z + 4.95), (0.03, L, 0.03), "wire_black")
    return m


def lrt_car():
    """One 12 m LRT car in white with a red belt stripe and dark window band; rounded cab
    front at -Y. Sits on the viaduct track (origin at rail level)."""
    m = K.Mesher()
    L, W, H = 12.0, 2.7, 3.3
    m.box((0, 0.4, 0.3 + H / 2), (W, L - 0.8, H), "train_white", bevel=0.2)
    m.box((0, -L / 2 + 0.6, 0.3 + H / 2 - 0.1), (W - 0.1, 1.2, H - 0.2), "train_white", taper=(0.85, 0.7), bevel=0.25)
    m.box((0, 0.4, 0.3 + H * 0.58), (W + 0.02, L - 1.0, 0.9), "train_window")
    m.box((0, -L / 2 + 0.25, 0.3 + H * 0.6), (W - 0.5, 0.1, 1.0), "train_window", rot=(-0.35, 0, 0))
    m.box((0, 0.4, 0.3 + H * 0.3), (W + 0.03, L - 0.9, 0.3), "train_red")
    m.box((0, -L / 2 + 0.3, 0.3 + H * 0.3), (W - 0.3, 0.2, 0.3), "train_red", rot=(-0.3, 0, 0))
    for s in (1, -1):
        for y in (-3.5, 0.5, 4.5):                                                             # doors
            m.box((s * (W / 2 + 0.015), y, 0.3 + H * 0.45), (0.02, 1.3, H * 0.75), "white")
            m.box((s * (W / 2 + 0.025), y, 0.3 + H * 0.58), (0.02, 1.1, 0.8), "train_window")
        round_ = -L / 2 + 0.2
        m.ball((s * 0.8, round_, 0.3 + H * 0.3), (0.16, 0.06, 0.1), "headlamp", seg=8, rings=5)
    for y in (-3.5, 3.5):                                                                      # bogies
        m.box((0, y, 0.18), (W * 0.7, 2.2, 0.36), "bumper_black")
        for s in (1, -1):
            m.cyl((s * 0.9, y, 0.22), 0.28, 0.28, 0.12, "metal_dark", seg=10, rot=(0, math.pi / 2, 0))
    m.box((0, 0.4, 0.3 + H + 0.12), (1.6, 2.4, 0.25), "aircon", bevel=0.04)
    m.box((0, 3.8, 0.3 + H + 0.35), (0.2, 1.4, 0.5), "metal_dark", rot=(0.4, 0, 0))              # pantograph
    return m


# id -> (builder, collision (center, size) or None)   (authoring coords, before the 180 turn)
KIT_P1 = {
    # Kampung Baru
    "env_kb_house_teal": (lambda: kb_house("kb_teal", "kb_mint", "zinc_red", seed=1), ((0, 0, 3.0), (8.0, 7.2, 6.0))),
    "env_kb_house_mint": (lambda: kb_house("kb_mint", "kb_teal", "zinc_red", seed=2), ((0, 0, 3.0), (8.0, 7.2, 6.0))),
    "env_kb_house_blue": (lambda: kb_house("kb_blue", "white", "zinc_dark", seed=3), ((0, 0, 3.0), (8.0, 7.2, 6.0))),
    "env_kb_house_small": (lambda: kb_house("kb_teal", "white", "zinc_red", w=6.0, seed=4), ((0, 0, 3.0), (6.0, 7.2, 6.0))),
    "env_kb_banana_plant": (banana_plant, ((0, 0, 1.2), (0.6, 0.6, 2.4))),
    "env_kb_flower_pot": (lambda: flower_pot("flower_red"), ((0, 0, 0.5), (0.8, 0.8, 1.0))),
    "env_kb_flower_pot_white": (lambda: flower_pot("frangipani", 6), ((0, 0, 0.5), (0.8, 0.8, 1.0))),
    "env_kb_shrub": (shrub, None),
    "env_kb_rain_tree": (rain_tree, ((0, 0, 1.5), (0.9, 0.9, 3.0))),
    "env_kb_fence": (picket_fence, ((0, 0.05, 0.6), (5.0, 0.2, 1.2))),
    "env_kb_lane": (kb_lane, None),
    "env_kb_skyline": (skyline_backdrop, None),
    # Brickfields
    "env_bf_shopfront_yellow": (lambda: bf_shopfront("bf_yellow", "bf_blue", "bf_red", 1), ((0, 1.0, 2.0), (5, 8.0, 4.0))),
    "env_bf_shopfront_pink": (lambda: bf_shopfront("shop_pink", "shop_teal", "bf_yellow", 2), ((0, 1.0, 2.0), (5, 8.0, 4.0))),
    "env_bf_shopfront_blue": (lambda: bf_shopfront("bf_blue", "bf_yellow", "bf_red", 3), ((0, 1.0, 2.0), (5, 8.0, 4.0))),
    "env_bf_flower_stall": (flower_stall, ((0, 0, 0.6), (2.4, 1.0, 1.2))),
    "env_bf_lamp": (bf_lamp, ((0, 0, 2.8), (0.5, 0.5, 5.6))),
    "env_bf_curb_edge": (bf_curb_edge, None),
    "env_bf_rail_viaduct": (rail_viaduct, ((0, 0, 4.0), (1.6, 20.0, 8.0))),
    "env_bf_lrt_car": (lrt_car, ((0, 0.2, 2.0), (2.7, 12.0, 3.6))),
}
