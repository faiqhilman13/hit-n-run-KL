"""
KL landmarks kit (env_kl_landmarks): stylised, logo-free takes on KL Sentral, Muzium Negara,
Masjid Negara and Perdana Botanical Gardens, in the same palette / faceted cartoon style and
conventions as the street kit (front = -Y while authoring, turned 180 at assembly so it faces
Unity +Z; origin at the ground centre; COL_ box proxies; signs are blank - the game letters
them in Unity).
"""
import math
import random
import bmesh
from mathutils import Vector

import kl_core as K
from kl_env import _tri_panel, flat_poly


def tri(m, a, b, c, color, thick=0.08):
    _tri_panel(m, Vector(a), Vector(b), Vector(c), color, thick)


def wave_roof(m, x0, x1, y0, y1, base, amp, n, color, thick=0.4):
    """A continuous roof surface rolling along X (two full waves), with a thickness and a
    slight fall toward the front so the eaves read from the street."""
    import bmesh as bm_
    tmp = bm_.new()
    def h(t, y):
        return base + amp * math.sin(t * math.pi * 2) + (y - y0) / (y1 - y0) * 0.8
    top, bot = [], []
    for i in range(n + 1):
        t = i / n
        x = x0 + (x1 - x0) * t
        top.append((tmp.verts.new((x, y0, h(t, y0))), tmp.verts.new((x, y1, h(t, y1)))))
        bot.append((tmp.verts.new((x, y0, h(t, y0) - thick)), tmp.verts.new((x, y1, h(t, y1) - thick))))
    for i in range(n):
        (a0, a1), (b0, b1) = top[i], top[i + 1]
        (c0, c1), (d0, d1) = bot[i], bot[i + 1]
        tmp.faces.new((a0, b0, b1, a1))           # top
        tmp.faces.new((c1, d1, d0, c0))           # underside
        tmp.faces.new((c0, d0, b0, a0))           # front edge
        tmp.faces.new((a1, b1, d1, c1))           # back edge
    for (a0, a1), (c0, c1) in ((top[0], bot[0]), (top[-1], bot[-1])):
        tmp.faces.new((a0, a1, c1, c0))
    bm_.ops.recalc_face_normals(tmp, faces=tmp.faces)
    m._merge(tmp, color)


# ============================================================================ KL Sentral
def kl_sentral():
    """Transit hub: long glass-fronted hall under a wavy white roof, cantilevered entrance
    canopy with a sign board, taxi-bay canopies, and an office tower behind."""
    m = K.Mesher()
    W, D = 34.0, 22.0
    m.box((0, 0, 0.5), (W + 2, D + 4, 1.0), "concrete")                                    # podium
    for k in range(4):                                                                       # front steps
        m.box((0, -D / 2 - 2.3 - k * 0.45, 0.5 - k * 0.12 - 0.06), (16, 0.45, 1.0 - k * 0.24), "concrete_dark")
    m.box((0, 0, 1 + 4.5), (W, D, 9), "plaster")                                            # hall
    fy = -D / 2 - 0.05
    m.box((0, fy, 5.5), (W - 2, 0.1, 8), "glass")                                           # curtain wall
    for i in range(13):
        m.box((-W / 2 + 1 + i * (W - 2) / 12, fy - 0.08, 5.5), (0.22, 0.12, 8), "steel")
    for z in (3.5, 6.5, 9.2):
        m.box((0, fy - 0.08, z), (W - 2, 0.12, 0.18), "steel")
    # wavy roof: one continuous rolling surface, overhanging the front
    wave_roof(m, -W / 2 - 1.5, W / 2 + 1.5, -D / 2 - 6.5, D / 2 + 1.5, 11.0, 1.3, 32, "white")
    for x in (-W / 2 + 1, W / 2 - 1):                                                        # roof props
        for y in (-D / 2 + 1, D / 2 - 1):
            m.tube((x, y, 10), (x, y, 11.5), 0.3, 0.3, "steel", seg=8)
    # entrance canopy + sign board
    m.box((0, -D / 2 - 3.2, 6.2), (14, 6.5, 0.4), "white", rot=(-0.06, 0, 0))
    for x in (-6.5, 6.5):
        m.tube((x, -D / 2 - 6, 1), (x, -D / 2 - 6, 6.1), 0.22, 0.22, "steel", seg=8)
    m.box((0, fy - 0.2, 8.4), (14, 0.35, 1.8), "shop_red")                                  # sign under the roof
    m.box((0, fy - 0.4, 8.4), (13.2, 0.06, 1.4), "sign_blank")
    # taxi / drop-off canopies either side of the entrance
    for s in (1, -1):
        m.box((s * 12, -D / 2 - 4.5, 4.2), (8, 5, 0.25), "steel")
        for x in (s * 9, s * 15):
            m.tube((x, -D / 2 - 6.5, 1), (x, -D / 2 - 6.5, 4.1), 0.12, 0.12, "steel", seg=6)
    # office tower behind the hall
    tx, ty = 9.0, 5.0
    m.box((tx, ty, 10 + 18), (10, 10, 36), "skyline_blue")
    for z in range(12, 46, 3):
        m.box((tx, ty - 5.04, z), (9.6, 0.08, 1.2), "glass")
        m.box((tx - 5.04, ty, z), (0.08, 9.6, 1.2), "glass")
    m.box((tx, ty, 46.5), (11, 11, 1.0), "white")
    m.box((tx, ty, 48), (6, 6, 2), "steel")
    return m


# ============================================================================ Muzium Negara
def muzium_negara():
    """Malay-palace museum: raised podium and wide steps, cream colonnade, two big colourful
    mosaic murals flanking the entrance, and a two-tier roof with upswept ridge horns."""
    m = K.Mesher()
    W, D = 32.0, 12.0
    m.box((0, 0, 0.6), (W + 2, D + 4, 1.2), "concrete")
    for k in range(5):
        m.box((0, -D / 2 - 2.2 - k * 0.4, 0.6 - k * 0.12 - 0.06), (10, 0.4, 1.2 - k * 0.24), "concrete_dark")
    m.box((0, 0, 1.2 + 3.2), (W, D, 6.4), "plaster")
    fy = -D / 2
    for i in range(14):                                                                      # colonnade
        x = -W / 2 + 1 + i * (W - 2) / 13
        m.box((x, fy - 1.3, 1.2 + 3.1), (0.6, 0.6, 6.2), "cream")
    m.box((0, fy - 1.3, 7.55), (W, 0.8, 0.5), "cream")
    # mosaic murals either side of the entrance
    cols = ["bf_red", "gold", "bf_blue", "leaf", "tile_brown", "shop_teal", "marigold", "white"]
    rng = random.Random(3)
    for s in (1, -1):
        cx = s * 9.5
        m.box((cx, fy - 0.06, 4.0), (9.4, 0.12, 4.8), "timber_dark")
        for i in range(8):
            for j in range(4):
                m.box((cx - 4.1 + i * 1.17, fy - 0.14, 2.2 + j * 1.2), (1.1, 0.06, 1.12), rng.choice(cols))
    # entrance: tall doorway with gold frame
    m.box((0, fy - 0.05, 3.6), (3.6, 0.1, 4.8), "wood_dark")
    m.box((0, fy - 0.1, 6.1), (4.4, 0.12, 0.3), "gold")
    for s in (1, -1):
        m.box((s * 2.0, fy - 0.1, 3.6), (0.3, 0.12, 5.0), "gold")
    # two-tier Malay roof: a wide lower roof and a steep upper gable, both with upswept horns
    def gable(z0, span, depth, h, color, horn):
        ang = math.atan2(h, span / 2)
        ln = math.hypot(span / 2, h)
        for s in (1, -1):
            m.box((s * span / 4, 0, z0 + h / 2), (ln, depth, 0.3), color, rot=(0, s * ang, 0))
        m.box((0, 0, z0 + h + 0.1), (0.5, depth + 0.6, 0.35), "timber_dark")                 # ridge
        for sy in (1, -1):                                                                    # horns at the ridge ends
            base = Vector((0, sy * (depth / 2 + 0.1), z0 + h))
            m.tube(base, base + Vector((0, sy * horn * 0.6, horn * 0.5)), 0.35, 0.2, "timber_dark", seg=6)
            m.tube(base + Vector((0, sy * horn * 0.6, horn * 0.5)), base + Vector((0, sy * horn * 0.9, horn * 1.2)), 0.2, 0.06,
                   "timber_dark", seg=6)
            # triangular gable end wall
            y = sy * depth / 2
            tri(m, (-span / 2 + 0.6, y, z0 + 0.1), (span / 2 - 0.6, y, z0 + 0.1), (0, y, z0 + h - 0.2), "cream", 0.15)
    gable(7.6, W + 1.5, D + 4.0, 3.2, "roof_red", 2.0)
    gable(10.3, W * 0.55, D + 1.5, 5.5, "roof_red", 3.0)
    # a band of carved timber under the upper roof
    m.box((0, 0, 10.35), (W * 0.52, D + 1.2, 0.5), "timber")
    return m


# ============================================================================ Masjid Negara
def masjid_negara():
    """National mosque: white arcaded prayer hall under an 18-point folded star roof in blue,
    reflecting pools, and a tall pencil minaret with a small umbrella cap."""
    m = K.Mesher()
    m.box((0, 0, 0.3), (34, 34, 0.6), "concrete")
    for s in (1, -1):                                                                        # reflecting pools
        flat_poly(m, [(-15, s * 12.5), (15, s * 12.5), (15, s * 15.8), (-15, s * 15.8)], 0.63, "glass")
    m.box((0, 0, 0.6 + 2.6), (22, 22, 5.2), "white")
    for side in range(4):                                                                    # arcades all round
        ang = side * math.pi / 2
        for i in range(9):
            t = -9.6 + i * 2.4
            x, y = math.cos(ang) * 11.1 - math.sin(ang) * t, math.sin(ang) * 11.1 + math.cos(ang) * t
            m.box((x, y, 2.3), (1.4 if side % 2 == 0 else 0.35, 0.35 if side % 2 == 0 else 1.4, 3.2), "window_dark")
    # folded star roof: 18 ridges up to an apex, valleys between them
    n, R, apex, rim, valley = 18, 13.0, 13.5, 6.4, 7.6
    for i in range(n):
        a0 = i / n * math.tau
        a1 = (i + 0.5) / n * math.tau
        a2 = (i + 1) / n * math.tau
        p0 = (math.cos(a0) * R, math.sin(a0) * R, rim)                  # star points (low)
        pv = (math.cos(a1) * R * 0.78, math.sin(a1) * R * 0.78, valley)  # valleys (tucked in, higher)
        p2 = (math.cos(a2) * R, math.sin(a2) * R, rim)
        top = (0, 0, apex)
        tri(m, top, p0, pv, "bf_blue", 0.12)
        tri(m, top, pv, p2, "shop_teal", 0.12)
    m.tube((0, 0, apex - 0.2), (0, 0, apex + 2.2), 0.25, 0.04, "gold", seg=8)
    # minaret
    mx, my = 13.5, -13.5
    m.cyl((mx, my, 0.9), 2.2, 2.2, 0.6, "white", seg=12)
    m.tube((mx, my, 1.0), (mx, my, 36), 1.25, 1.05, "white", seg=12)
    for z in (10, 20, 30):
        m.cyl((mx, my, z), 1.5, 1.5, 0.5, "bf_blue", seg=12)
    m.cyl((mx, my, 36.2), 1.8, 1.8, 0.6, "white", seg=12)
    for i in range(12):                                                                      # little umbrella cap
        a0, a1 = i / 12 * math.tau, (i + 1) / 12 * math.tau
        tri(m, (mx, my, 39.2), (mx + math.cos(a0) * 2.3, my + math.sin(a0) * 2.3, 36.6),
            (mx + math.cos(a1) * 2.3, my + math.sin(a1) * 2.3, 36.6), "bf_blue", 0.1)
    m.tube((mx, my, 39), (mx, my, 42), 0.15, 0.03, "gold", seg=6)
    return m


# ============================================================================ Perdana Botanical Gardens
def pbg_lake():
    """30 x 20 m garden lake: water surface, stone edging, lotus pads and blooms."""
    m = K.Mesher()
    rng = random.Random(8)
    pts = [(math.cos(a) * 15 * (1 + 0.06 * math.sin(3 * a)), math.sin(a) * 10 * (1 + 0.08 * math.cos(2 * a)))
           for a in (k / 28 * math.tau for k in range(28))]
    flat_poly(m, pts, 0.02, "glass")
    for i in range(28):                                                                      # stone edge
        a, b = Vector((*pts[i], 0.1)), Vector((*pts[(i + 1) % 28], 0.1))
        mid = (a + b) / 2
        ang = math.atan2(b.y - a.y, b.x - a.x)
        m.box(mid, ((b - a).length + 0.1, 0.6, 0.35), "concrete", rot=(0, 0, ang))
    for i in range(14):                                                                      # lotus
        x, y = rng.uniform(-11, 11), rng.uniform(-6.5, 6.5)
        m.cyl((x, y, 0.06), 0.55, 0.55, 0.04, "leaf", seg=10)
        if i % 3 == 0:
            m.ball((x + 0.15, y, 0.2), (0.16, 0.16, 0.14), "magenta", seg=6, rings=4)
    return m


def pbg_footbridge():
    """10 m arched timber footbridge with red rails (spans the lake narrows)."""
    m = K.Mesher()
    n = 10
    for i in range(n):
        y0, y1 = -5 + i, -4 + i
        z0, z1 = 0.25 + 1.2 * math.sin((y0 + 5) / 10 * math.pi), 0.25 + 1.2 * math.sin((y1 + 5) / 10 * math.pi)
        mid = Vector((0, (y0 + y1) / 2, (z0 + z1) / 2))
        ang = math.atan2(z1 - z0, 1.0)
        m.box(mid, (2.4, 1.03, 0.15), "timber", rot=(ang, 0, 0))
        for s in (1, -1):
            m.box(mid + Vector((s * 1.15, 0, 0.95)), (0.1, 1.03, 0.1), "bf_red", rot=(ang, 0, 0))
            m.box(mid + Vector((s * 1.15, 0, 0.45)), (0.12, 0.12, 1.0), "bf_red")
    return m


def pbg_gazebo():
    """Wakaf: Malay garden pavilion - raised timber platform, four posts, pointed hip roof."""
    m = K.Mesher()
    m.box((0, 0, 0.35), (4.6, 4.6, 0.25), "timber")
    for x in (-2.1, 2.1):
        for y in (-2.1, 2.1):
            m.box((x, y, 0.2), (0.3, 0.3, 0.4), "concrete")
            m.box((x, y, 1.9), (0.22, 0.22, 3.0), "timber_dark")
    for i in range(4):                                                                       # hip roof
        a0, a1 = i / 4 * math.tau + math.pi / 4, (i + 1) / 4 * math.tau + math.pi / 4
        r = 4.1
        tri(m, (0, 0, 5.6), (math.cos(a0) * r, math.sin(a0) * r, 3.3), (math.cos(a1) * r, math.sin(a1) * r, 3.3), "zinc_red", 0.12)
    m.tube((0, 0, 5.5), (0, 0, 6.4), 0.1, 0.02, "timber_dark", seg=6)
    for s in (1, -1):                                                                        # benches
        m.box((s * 1.7, 0, 0.75), (0.5, 3.4, 0.08), "wood")
    return m


def pbg_pergola():
    """8 m timber pergola draped in magenta bougainvillea."""
    m = K.Mesher()
    rng = random.Random(5)
    for y in (-3.5, -1.2, 1.2, 3.5):
        for x in (-1.5, 1.5):
            m.box((x, y, 1.4), (0.25, 0.25, 2.8), "white")
    for x in (-1.5, 1.5):
        m.box((x, 0, 2.9), (0.2, 8.4, 0.25), "timber")
    for i in range(12):
        m.box((0, -3.9 + i * 0.71, 3.08), (3.8, 0.12, 0.14), "timber")
    for i in range(22):
        x, y = rng.uniform(-1.9, 1.9), rng.uniform(-4, 4)
        m.ball((x, y, 3.25 + rng.uniform(-0.1, 0.1)), (0.5, 0.5, 0.3), "magenta" if i % 3 else "leaf", seg=7, rings=4)
        if i % 4 == 0:
            m.ball((x, y, 2.7), (0.2, 0.2, 0.45), "magenta", seg=6, rings=4)
    return m


def pbg_flowerbed(flower="flower_red"):
    """6 x 2 m raised bed of hibiscus shrubs (the national flower)."""
    m = K.Mesher()
    rng = random.Random(11)
    m.box((0, 0, 0.25), (6, 2, 0.5), "concrete")
    m.box((0, 0, 0.52), (5.7, 1.7, 0.06), "trunk")
    for i in range(7):
        x = -2.5 + i * 0.83
        m.ball((x, rng.uniform(-0.3, 0.3), 0.95), (0.5, 0.5, 0.45), "leaf" if i % 2 else "leaf_dark", seg=7, rings=5)
        for k in range(3):
            m.ball((x + rng.uniform(-0.35, 0.35), rng.uniform(-0.55, 0.55), 1.1 + rng.uniform(0, 0.25)), (0.14, 0.14, 0.1),
                   flower, seg=6, rings=4)
    return m


def pbg_orchid_arch():
    """Orchid garden arch: an arc of blooms in magenta and white over a path."""
    m = K.Mesher()
    rng = random.Random(4)
    n = 16
    for i in range(n + 1):
        a = i / n * math.pi
        p = Vector((math.cos(a) * 1.8, 0, math.sin(a) * 2.6))
        m.ball(p, (0.32, 0.32, 0.32), "leaf_dark", seg=6, rings=4)
        for k in range(2):
            m.ball(p + Vector((rng.uniform(-0.25, 0.25), -0.2, rng.uniform(-0.2, 0.2))), (0.12, 0.08, 0.1),
                   "magenta" if (i + k) % 2 else "jasmine", seg=6, rings=4)
    for s in (1, -1):
        m.box((s * 1.8, 0, 0.2), (0.6, 0.6, 0.4), "pot_terracotta")
    return m


def pbg_bench():
    m = K.Mesher()
    m.box((0, 0, 0.45), (1.8, 0.45, 0.07), "wood")
    m.box((0, 0.22, 0.8), (1.8, 0.07, 0.4), "wood")
    for x in (-0.75, 0.75):
        m.box((x, 0, 0.22), (0.08, 0.45, 0.45), "bollard")
        m.box((x, 0.22, 0.6), (0.08, 0.07, 0.5), "bollard")
    return m


def pbg_fountain():
    """Tiered fountain: stone basin, two bowls, water jets."""
    m = K.Mesher()
    m.cyl((0, 0, 0.3), 3.0, 3.1, 0.6, "concrete", seg=16)
    flat_poly(m, [(math.cos(a) * 2.7, math.sin(a) * 2.7) for a in (k / 16 * math.tau for k in range(16))], 0.58, "glass")
    m.tube((0, 0, 0.5), (0, 0, 2.2), 0.35, 0.25, "concrete", seg=10)
    m.cyl((0, 0, 1.6), 1.5, 1.2, 0.3, "concrete", seg=14)
    m.cyl((0, 0, 2.4), 0.8, 0.6, 0.25, "concrete", seg=12)
    m.tube((0, 0, 2.5), (0, 0, 3.5), 0.12, 0.05, "glass", seg=8)
    for i in range(8):
        a = i / 8 * math.tau
        m.tube((math.cos(a) * 0.5, math.sin(a) * 0.5, 2.5), (math.cos(a) * 1.4, math.sin(a) * 1.4, 1.8), 0.06, 0.04, "glass",
               seg=6)
    return m


KIT_LANDMARKS = {
    "env_lm_kl_sentral": (kl_sentral, ((0, 0, 5.5), (34, 22, 11))),
    "env_lm_muzium_negara": (muzium_negara, ((0, 0, 4.5), (32, 12, 9))),
    "env_lm_masjid_negara": (masjid_negara, ((0, 0, 3.0), (22, 22, 6))),
    "env_pbg_lake": (pbg_lake, None),
    "env_pbg_footbridge": (pbg_footbridge, None),
    "env_pbg_gazebo": (pbg_gazebo, ((0, 0, 1.8), (4.6, 4.6, 3.6))),
    "env_pbg_pergola": (pbg_pergola, None),
    "env_pbg_flowerbed": (pbg_flowerbed, ((0, 0, 0.5), (6, 2, 1.0))),
    "env_pbg_orchid_arch": (pbg_orchid_arch, None),
    "env_pbg_bench": (pbg_bench, ((0, 0, 0.45), (1.8, 0.5, 0.9))),
    "env_pbg_fountain": (pbg_fountain, ((0, 0, 0.6), (6, 6, 1.2))),
}
