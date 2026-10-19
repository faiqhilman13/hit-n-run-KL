"""
KL landmarks kit 2 (env_kl_landmarks2): eight more logo-free, cartoon takes on KL icons for the
two new east columns of the map. Same conventions as kl_landmarks (front = -Y while authoring,
turned 180 at assembly so it faces Unity +Z; origin at the ground centre; COL_ box proxies;
blank sign boards - the game letters them). Every module fits inside one 38 m city block.

  Batu Caves        - limestone hill, the giant golden statue, the rainbow staircase
  Merdeka 118       - the faceted supertall with its needle spire
  Tugu Negara       - bronze soldiers on a granite plinth over a reflecting pool
  Istana Negara     - the palace: white wings, golden domes, the gate with its guard boxes
  Thean Hou Temple  - three-tier red-and-gold temple with jade roofs and lanterns
  Masjid Jamek      - red brick with white bands, onion domes, chhatri minarets
  Pavilion          - the Bukit Bintang mall: glass drum, stepped canopy, crystal fountain
  Stadium Merdeka   - the open-air stadium bowl with its floodlight towers
"""
import math
import random
from mathutils import Vector

import kl_core as K


def oval_ring(m, rx, ry, z, h, thick, color, n=36, gap=False):
    """An open oval wall (stadium stands): n boxes following the ellipse. gap leaves the
    front (-Y) open as the tunnel entrance."""
    for i in range(n):
        a0, a1 = i / n * math.tau, (i + 1) / n * math.tau
        p0 = Vector((math.cos(a0) * rx, math.sin(a0) * ry, 0))
        p1 = Vector((math.cos(a1) * rx, math.sin(a1) * ry, 0))
        mid = (p0 + p1) / 2
        if gap and mid.y < -ry * 0.9:
            continue
        d = p1 - p0
        m.box((mid.x, mid.y, z), (d.length + 0.15, thick, h), color, rot=(0, 0, math.atan2(d.y, d.x)))


def ring_of(m, n, r, z, fn):
    for i in range(n):
        a = i / n * math.tau
        fn(m, Vector((math.cos(a) * r, math.sin(a) * r, z)), a)


def onion_dome(m, c, r, color, tip="gold", seg=14):
    """Bulbous dome: a squashed ball, a pinched neck and a finial."""
    c = Vector(c)
    m.cyl(c + Vector((0, 0, r * 0.15)), r * 0.9, r * 0.9, r * 0.3, color, seg=seg)
    m.ball(c + Vector((0, 0, r * 0.8)), (r, r, r * 0.95), color, seg=seg, rings=10, smooth=True)
    m.cyl(c + Vector((0, 0, r * 1.75)), r * 0.25, r * 0.05, r * 0.6, color, seg=10)
    m.ball(c + Vector((0, 0, r * 2.1)), (r * 0.1,) * 3, tip, seg=8, rings=6)
    m.tube(c + Vector((0, 0, r * 2.1)), c + Vector((0, 0, r * 2.5)), r * 0.03, r * 0.02, tip, seg=6)


# ============================================================================ Batu Caves
def batu_caves():
    """A lumpy limestone hill with the cave mouth, the giant golden statue in front, and the
    272 rainbow-painted steps climbing up to the cave."""
    m = K.Mesher()
    rng = random.Random(11)
    # the hill: overlapping squashed rocks, dark at the base
    for i in range(18):
        x, y = rng.uniform(-14, 14), rng.uniform(3, 16)
        r = rng.uniform(6, 10)
        m.ball((x, y, rng.uniform(6, 16)), (r, r * 0.8, r * rng.uniform(0.9, 1.4)), "limestone", seg=10, rings=7, smooth=True)
    for i in range(10):
        x = rng.uniform(-16, 16)
        m.ball((x, rng.uniform(6, 14), 2), (rng.uniform(4, 7), 4, 3), "limestone_dark", seg=9, rings=6, smooth=True)
    for i in range(14):                                                                     # jungle on the ledges
        m.ball((rng.uniform(-15, 15), rng.uniform(4, 15), rng.uniform(14, 24)), (rng.uniform(1.5, 3),) * 3, "leaf",
               seg=8, rings=6, smooth=True)
    # cave mouth at the top of the stairs
    m.ball((0, 3.2, 18.5), (4.2, 1.6, 3.6), "window_dark", seg=12, rings=8, smooth=True)
    # the rainbow staircase: coloured flights from the plaza up to the cave
    rainbow = ["bf_red", "marigold", "crate_yellow", "leaf", "shop_teal", "bf_blue", "magenta"]
    n = 34
    for k in range(n):
        t = k / n
        y = -9 + t * 12
        z = 0.25 + t * 17.5
        m.box((0, y, z), (6, 0.42, 0.55), rainbow[(k // 3) % len(rainbow)])
    for s in (1, -1):                                                                      # handrails
        m.tube((s * 3.1, -9, 1.2), (s * 3.1, 3, 18.8), 0.08, 0.08, "gold", seg=6)
        m.box((s * 3.4, -3, 9.0), (0.5, math.hypot(12, 17.5), 0.9), "limestone_dark", rot=(math.atan(17.5 / 12), 0, 0))
    # plaza + the giant golden statue beside the stairs
    m.box((0, -12, 0.15), (30, 10, 0.3), "concrete")
    sx, sy = -9.5, -10.5
    m.box((sx, sy, 1.2), (5, 5, 2.4), "marble")                                            # pedestal
    m.box((sx, sy, 2.6), (4.2, 4.2, 0.4), "gold")
    g = "gold"
    m.ball((sx, sy, 7.5), (2.2, 1.6, 3.6), g, seg=14, rings=10, smooth=True)              # body
    m.ball((sx, sy, 5.0), (2.5, 1.9, 1.8), g, seg=14, rings=8, smooth=True)               # robe
    m.tube((sx - 0.7, sy, 3.0), (sx - 0.8, sy, 5.2), 0.8, 0.9, g, seg=10)                  # legs
    m.tube((sx + 0.7, sy, 3.0), (sx + 0.8, sy, 5.2), 0.8, 0.9, g, seg=10)
    m.ball((sx, sy, 11.8), (1.3, 1.2, 1.4), g, seg=12, rings=9, smooth=True)              # head
    m.cyl((sx, sy, 13.6), 0.9, 0.4, 1.8, g, seg=12)                                        # crown
    m.ball((sx, sy - 1.1, 11.8), (0.18, 0.1, 0.14), "bronze", seg=6, rings=4)
    m.tube((sx + 2.0, sy - 0.2, 9.6), (sx + 3.4, sy - 1.4, 7.0), 0.45, 0.4, g, seg=8)     # arm with the spear
    m.tube((sx + 3.4, sy - 1.6, 2.6), (sx + 3.4, sy - 1.6, 16.5), 0.14, 0.12, g, seg=8)
    m.prism((sx + 3.4, sy - 1.6, 16.8), (0.9, 0.2, 1.4), g, rot=(0, 0, math.pi / 2))
    m.tube((sx - 2.0, sy - 0.2, 9.6), (sx - 2.8, sy - 1.4, 7.4), 0.45, 0.4, g, seg=8)
    # a shrine gate at the foot of the stairs
    for s in (1, -1):
        m.box((s * 4.2, -9.8, 2.2), (0.9, 0.9, 4.4), "marble")
    m.box((0, -9.8, 4.8), (9.6, 1.2, 0.9), "bf_red")
    m.box((0, -9.8, 5.5), (8, 1.0, 0.5), "gold")
    return m


# ============================================================================ Merdeka 118
def merdeka_118():
    """The supertall: a faceted glass shaft of stacked, slightly twisted diamond-panel tiers
    narrowing to a crown and a long needle spire. Cartoon-compressed to ~140 m."""
    m = K.Mesher()
    m.box((0, 0, 3), (30, 30, 6), "concrete")                                               # podium mall
    m.box((0, -15.05, 3.2), (26, 0.1, 4.4), "glass")
    for i in range(8):
        m.box((-13 + i * 3.7, -15.1, 3.2), (0.3, 0.12, 4.6), "steel")
    tiers = 14
    for k in range(tiers):
        t = k / tiers
        z0 = 6 + k * 8.6
        w = 20 * (1 - 0.55 * t ** 1.3)
        twist = k * 0.035
        m.box((0, 0, z0 + 4.3), (w, w, 8.6), "skyline_blue", rot=(0, 0, math.pi / 4 + twist))
        # facets: the glass diamond panels catch the light
        for s in (1, -1):
            m.box((s * w * 0.36, -w * 0.36, z0 + 4.3), (w * 0.5, 0.1, 7.6), "glass", rot=(0, 0, s * math.pi / 4 + twist))
        m.box((0, 0, z0 + 8.4), (w * 1.02, w * 1.02, 0.35), "white", rot=(0, 0, math.pi / 4 + twist))
    top = 6 + tiers * 8.6
    m.cyl((0, 0, top + 3), 5, 2.5, 6, "white", seg=8)                                       # crown
    m.tube((0, 0, top + 6), (0, 0, top + 38), 1.0, 0.12, "steel", seg=8)                   # the needle
    m.ball((0, 0, top + 38.4), (0.35, 0.35, 0.35), "lamp_red", seg=8, rings=6)
    return m


# ============================================================================ Tugu Negara
def tugu_negara():
    """National Monument: a group of bronze soldiers raising the flag on a granite plinth,
    over a long reflecting pool with fountains, flanked by a crescent colonnade."""
    m = K.Mesher()
    m.box((0, 0, 0.2), (34, 30, 0.4), "concrete")
    # reflecting pool with fountain jets
    m.box((0, -7, 0.5), (22, 12, 0.4), "concrete_dark")
    m.box((0, -7, 0.72), (21, 11, 0.06), "tarp_blue")
    for i in range(7):
        x = -9 + i * 3
        m.tube((x, -7, 0.75), (x, -7, 2.2), 0.08, 0.02, "glass", seg=6)
    # plinth
    m.box((0, 6, 1.6), (10, 8, 2.4), "concrete_dark")
    m.box((0, 6, 3.6), (8, 6, 1.6), "limestone")
    # soldiers: seven chunky bronze figures in a pyramid, the flag on top
    b = "bronze"
    figs = [(-3, 5, 0, 1.0), (3, 5, 0, 1.0), (-1.6, 6.5, 0.4, 1.1), (1.6, 6.5, 0.4, 1.1), (0, 5.2, 0.8, 1.2),
            (-3.2, 7.6, 0, 0.9), (3.2, 7.6, 0, 0.9)]
    for (x, y, lift, sc) in figs:
        z = 4.4 + lift
        m.tube((x, y, z), (x, y, z + 1.4 * sc), 0.45 * sc, 0.5 * sc, b, seg=8)             # legs / body
        m.ball((x, y, z + 2.0 * sc), (0.6 * sc, 0.45 * sc, 0.8 * sc), b, seg=10, rings=8, smooth=True)
        m.ball((x, y - 0.05, z + 2.95 * sc), (0.32 * sc,) * 3, b, seg=8, rings=6, smooth=True)
        m.cyl((x, y - 0.05, z + 3.2 * sc), 0.42 * sc, 0.36 * sc, 0.18 * sc, b, seg=8)       # helmet
        m.tube((x + 0.5 * sc, y, z + 2.4 * sc), (x + 0.9 * sc, y - 0.5 * sc, z + 1.4 * sc), 0.14 * sc, 0.12 * sc, b, seg=6)
    m.tube((0, 5.2, 7.8), (0.3, 5.1, 12.5), 0.07, 0.07, b, seg=6)                          # flag pole
    m.box((1.4, 5.1, 11.8), (2.2, 0.08, 1.3), "flag_red", rot=(0, 0.08, 0))
    m.box((1.4, 5.05, 11.8), (0.9, 0.06, 0.6), "flag_blue")
    # crescent colonnade behind
    for i in range(11):
        a = math.pi * (0.1 + 0.8 * i / 10)
        x, y = math.cos(a) * 14, 4 + math.sin(a) * 9
        m.box((x, y, 2.4), (0.8, 0.8, 4.4), "white")
    for i in range(10):
        a0, a1 = math.pi * (0.1 + 0.8 * i / 10), math.pi * (0.1 + 0.8 * (i + 1) / 10)
        p0 = Vector((math.cos(a0) * 14, 4 + math.sin(a0) * 9, 4.8))
        p1 = Vector((math.cos(a1) * 14, 4 + math.sin(a1) * 9, 4.8))
        m.tube(p0, p1, 0.5, 0.5, "white", seg=4)
        m.ball((p0 + p1) / 2 + Vector((0, 0, 0.9)), (0.8, 0.8, 0.9), "gold", seg=10, rings=7, smooth=True)
    return m


# ============================================================================ Istana Negara
def istana_negara():
    """Istana Negara: the cream palace - the great gold dome patterned in chevrons on a drum of windows over the
    centre, a second gold dome below it on the porch, little gold domes on the side pavilions, dark grey hipped roofs
    on the wings, and the grand arched gate with its gold domes."""
    m = K.Mesher()
    lo = K.LOD_CULL > 0
    m.box((0, 4, 0.4), (36, 20, 0.8), "concrete")
    m.box((0, 8, 0.85), (34, 12, 0.1), "grass")

    def gold_dome(c, r):
        onion_dome(m, c, r, "gold")
        if not lo:
            c = Vector(c)
            for k in range(12):
                a = k / 12 * math.tau
                p0 = c + Vector((math.cos(a) * r * 0.92, math.sin(a) * r * 0.92, r * 0.25))
                p1 = c + Vector((math.cos(a) * r * 0.35, math.sin(a) * r * 0.35, r * 1.55))
                m.tube(p0, p1, r * 0.05, r * 0.03, "brass", seg=4)
    for s in (1, -1):
        m.box((s * 11, 6, 4.8), (12, 9, 8.2), "cream")
        for i in range(5):                                                                  # arched windows, gold frames
            x = s * (6.2 + i * 2.3)
            m.box((x, 1.42, 3.0), (1.6, 0.25, 3.6), "gold")
            m.box((x, 1.35, 3.0), (1.2, 0.25, 3.2), "window_dark")
            m.ball((x, 1.35, 4.7), (0.6, 0.15, 0.6), "window_dark", seg=10, rings=6)
        m.box((s * 11, 6, 9.0), (12.6, 9.6, 0.4), "white")
        m.prism((s * 11, 6, 9.2), (12.6, 9.6, 2.6), "lamp_grey")                           # dark hipped roofs

        gold_dome((s * 16.5, 2.0, 9.0), 1.3)
    # the centre: a tall cream block, the drum of windows and the great dome
    m.box((0, 6, 7), (11, 11, 12.6), "cream")
    m.box((0, 0.45, 6.0), (4.0, 0.3, 7.0), "gold")
    m.box((0, 0.35, 6.0), (3.2, 0.3, 6.4), "window_dark")
    m.cyl((0, 6, 14.3), 5.0, 5.0, 2.6, "white", seg=18)
    if not lo:
        for k in range(12):
            a = k / 12 * math.tau
            m.box((math.cos(a) * 5.02, 6 + math.sin(a) * 5.02, 14.3), (1.2, 0.15, 1.6), "window_dark", rot=(0, 0, a + math.pi / 2))
    m.cyl((0, 6, 15.75), 5.3, 5.3, 0.3, "gold", seg=18)
    gold_dome((0, 6, 15.9), 4.6)
    # the porch with the second dome
    m.box((0, -0.5, 4.6), (8, 4, 8.4), "cream")
    gold_dome((0, -0.5, 8.8), 2.4)
    for s in (1, -1):
        gold_dome((s * 4.6, 2.5, 13.0), 1.4)
    # front gate: a big gold-framed arch between dark pylons with gold domes, guard boxes
    gy = -8.5
    for s in (1, -1):
        m.box((s * 5, gy, 3.5), (2.0, 1.6, 7), "charcoal")
        gold_dome((s * 5, gy, 7.0), 0.9)
        m.box((s * 8.5, gy - 0.5, 1.6), (1.8, 1.8, 3.2), "cream")
        m.box((s * 8.5, gy - 1.42, 1.5), (1.1, 0.1, 2.2), "window_dark")
        gold_dome((s * 8.5, gy - 0.5, 3.2), 0.8)
    m.box((0, gy, 6.6), (8.6, 1.2, 1.0), "cream")
    if not lo:
        for k in range(9):
            a = math.pi * k / 8
            m.box((math.cos(a) * 3.6, gy - 0.65, 3.4 + math.sin(a) * 2.6), (0.9, 0.2, 0.5), "gold", rot=(0, -a + math.pi / 2, 0))
    for i in range(9):                                                                      # gate bars
        m.tube((-4 + i, gy, 0.3), (-4 + i, gy, 3.2), 0.06, 0.06, "gold", seg=6)
    m.box((0, gy, 3.3), (8.6, 0.12, 0.2), "gold")
    return m


# ============================================================================ Thean Hou
def thean_hou():
    """Thean Hou Temple: three tiers of red-pillared halls on a white-balustraded terrace, roofs of orange-gold glazed
    tile with sharply upswept eaves and dragon ridges, red lanterns everywhere."""
    m = K.Mesher()
    lo = K.LOD_CULL > 0
    m.box((0, 2, 1.0), (32, 26, 2.0), "marble")                                              # terrace
    for k in range(6):
        m.box((0, -11.4 - k * 0.5, 1.0 - k * 0.17 - 0.08), (10, 0.5, 2.0 - k * 0.34), "marble")
    # white balustrades round the terrace
    for s in (1, -1):
        m.box((s * 10.5, -10.8, 2.5), (11, 0.4, 1.0), "white")
        m.box((s * 15.8, 2, 2.5), (0.4, 25, 1.0), "white")
    m.box((0, 14.8, 2.5), (31, 0.4, 1.0), "white")
    if not lo:
        for x in range(-15, 16, 2):
            if abs(x) > 5:
                m.box((x, -10.8, 2.6), (0.35, 0.5, 1.3), "white")

    def roof(z, w, d, h):
        m.box((0, 2, z), (w, d, 0.4), "bf_red")
        m.prism((0, 2, z + 0.2), (w + 1.5, d + 2.5, h), "temple_orange")
        m.box((0, 2 - (d + 2.5) / 2, z + 0.25), (w + 1.5, 0.3, 0.35), "gold")
        for s in (1, -1):                                                                  # upswept eave corners
            for t in (1, -1):
                m.tube((s * (w / 2 + 0.2), 2 + t * (d / 2 + 0.8), z + 0.3),
                       (s * (w / 2 + 2.0), 2 + t * (d / 2 + 2.0), z + 2.0), 0.3, 0.08, "temple_orange", seg=6)
        m.box((0, 2, z + h + 0.25), (w * 0.9, 0.5, 0.5), "gold")                           # ridge
        for s in (1, -1):
            m.ball((s * w * 0.45, 2, z + h + 0.8), (0.5, 0.3, 0.7), "jade_green", seg=8, rings=6)  # ridge dragons
            m.tube((s * w * 0.45, 2, z + h + 1.2), (s * w * 0.52, 2, z + h + 2.0), 0.2, 0.05, "gold", seg=6)
        if not lo:
            m.ball((0, 2, z + h + 0.9), (0.6, 0.6, 0.6), "gold", seg=8, rings=6)
    # hall 1
    m.box((0, 2, 5.0), (22, 16, 6.0), "bf_red")
    m.box((0, -5.95, 5.2), (21, 0.2, 2.0), "white")
    for i in range(8):
        m.tube((-10.5 + i * 3, -6.4, 2.0), (-10.5 + i * 3, -6.4, 8.0), 0.35, 0.35, "bf_red", seg=10)
        m.ball((-10.5 + i * 3, -6.8, 7.2), (0.45, 0.45, 0.55), "lantern_red", seg=10, rings=7)   # lanterns
        m.cyl((-10.5 + i * 3, -6.8, 7.8), 0.25, 0.25, 0.12, "gold", seg=8)
    m.box((0, -6.05, 4.5), (4, 0.2, 4.6), "gold")                                           # doors
    roof(8.2, 24, 18, 3.2)
    m.box((0, 2, 12.9), (15, 11, 3.0), "bf_red")
    m.box((0, -3.6, 12.9), (14, 0.2, 1.2), "white")
    roof(14.4, 17, 13, 2.6)
    m.box((0, 2, 18.2), (9, 7, 2.4), "bf_red")
    roof(19.4, 11, 9, 2.4)
    m.cyl((0, 2, 22.8), 0.6, 0.2, 1.8, "gold", seg=8)
    # side pavilions with their own little roofs
    for s in (1, -1):
        m.box((s * 13, 8, 4.0), (5, 5, 4.0), "bf_red")
        m.prism((s * 13, 8, 6.0), (6.5, 6.5, 2.0), "temple_orange")
        m.cyl((s * 13, 8, 8.4), 0.4, 0.1, 1.0, "gold", seg=6)
    # strings of lanterns across the forecourt
    if not lo:
        for y in (-13.5, -16.5):
            for i in range(9):
                m.ball((-12 + i * 3, y, 5.4), (0.4, 0.4, 0.5), "lantern_red", seg=8, rings=6)
            m.tube((-13, y, 5.9), (13, y, 5.9), 0.03, 0.03, "wire_black", seg=4)
        for x in (-13, 13):
            for y in (-13.5, -16.5):
                m.tube((x, y, 0), (x, y, 6.0), 0.12, 0.12, "bf_red", seg=6)
    return m


# ============================================================================ Masjid Jamek
def masjid_jamek():
    """Masjid Jamek at the confluence: a prayer hall of red brick banded in white behind a deep white arcade of
    cusped arches, three white onion domes (the middle one biggest), two tall banded minarets with chhatri tops,
    rows of little domed turrets, and the stepped half-round terrace down to the water at the point."""
    m = K.Mesher()
    lo = K.LOD_CULL > 0
    m.box((0, 2, 0.4), (32, 26, 0.8), "marble")
    # the stepped terrace to the confluence (behind the qibla wall, +Y)
    for k in range(4):
        m.cyl((0, 14.5 + k * 0.6, 0.6 - k * 0.15), 9.0 - k * 0.8, 9.0 - k * 0.8, 0.3, "marble", seg=16)
    # prayer hall: brick banded in white
    m.box((0, 7, 4.6), (18, 12, 7.6), "brick_red")
    for z in (2.2, 4.6, 7.0, 8.3):
        m.box((0, 7, z), (18.2, 12.2, 0.35), "white")
    # the white arcade round the front and sides
    for side in ((0, -1.0, 24, 0.0), (-11.5, 6, 14, math.pi / 2), (11.5, 6, 14, -math.pi / 2)):
        cx, cy, L, rot = side
        c, s = math.cos(rot), math.sin(rot)
        n = int(L / 2.4)
        m.box((cx, cy, 2.6), (L, 2.2, 5.2) if rot == 0 else (2.2, L, 5.2), "white")
        m.box((cx, cy, 5.4), (L + 0.4, 2.6, 0.5) if rot == 0 else (2.6, L + 0.4, 0.5), "brick_red")
        for i in range(n):
            t = -L / 2 + (i + 0.5) * L / n
            x, y = cx + t * c, cy + t * s
            ox, oy = (0, -1.12) if rot == 0 else ((-1.12, 0) if rot > 0 else (1.12, 0))
            m.box((x + ox, y + oy, 1.9), (1.3, 0.12, 3.0) if rot == 0 else (0.12, 1.3, 3.0), "window_dark")
            m.ball((x + ox, y + oy, 3.4), (0.65, 0.1, 0.75) if rot == 0 else (0.1, 0.65, 0.75), "window_dark", seg=10, rings=6)
            if not lo:
                m.ball((x, y, 5.9), (0.35, 0.35, 0.5), "white", seg=8, rings=5)
    # domes
    onion_dome(m, (0, 7, 8.4), 3.6, "white")
    for s in (1, -1):
        onion_dome(m, (s * 6.4, 7, 8.4), 2.3, "white")
    # the two tall minarets (front corners) and smaller turrets (back corners)
    for s in (1, -1):
        x, y = s * 12.5, -3.0
        m.cyl((x, y, 8.5), 1.15, 0.95, 17, "brick_red", seg=10)
        for z in range(2, 17, 2):
            m.cyl((x, y, z), 1.2 - z * 0.012, 1.2 - z * 0.012, 0.45, "white", seg=10)
        m.cyl((x, y, 17.2), 1.5, 1.5, 0.4, "white", seg=10)
        for k in range(6):                                                                  # chhatri posts
            a = k / 6 * math.tau
            m.tube((x + math.cos(a) * 1.0, y + math.sin(a) * 1.0, 17.4), (x + math.cos(a) * 1.0, y + math.sin(a) * 1.0, 19.0),
                   0.1, 0.1, "white", seg=6)
        onion_dome(m, (x, y, 19.0), 1.2, "white")
        x2, y2 = s * 9.5, 13.0
        m.cyl((x2, y2, 5.5), 0.8, 0.7, 11, "brick_red", seg=8)
        for z in (3, 6, 9, 10.8):
            m.cyl((x2, y2, z), 0.85, 0.85, 0.3, "white", seg=8)
        onion_dome(m, (x2, y2, 11.0), 0.9, "white")
    return m


# ============================================================================ Pavilion
def pavilion():
    """The Bukit Bintang mall: a curved glass drum between two stacked boxes, a big stepped
    entrance canopy and the crystal fountain in the plaza out front."""
    m = K.Mesher()
    m.box((0, 0, 0.15), (36, 34, 0.3), "pavement")
    for s in (1, -1):                                                                       # side blocks
        m.box((s * 11, 6, 9), (13, 20, 18), "white")
        for z in range(3, 18, 3):
            m.box((s * 11, -4.06, z), (12.4, 0.1, 1.2), "glass")
        m.box((s * 11, -4.2, 16.5), (10, 0.3, 2), "sign_blank")
    m.cyl((0, 6, 12), 9, 9, 24, "glass", seg=24)                                            # glass drum
    for z in range(2, 24, 3):
        m.cyl((0, 6, z), 9.1, 9.1, 0.3, "steel", seg=24)
    m.cyl((0, 6, 24.4), 9.6, 9.6, 0.8, "white", seg=24)
    # stepped canopy over the entrance
    for k in range(3):
        m.box((0, -4 - k * 1.6, 8 - k * 1.8), (16 - k * 3, 3.4, 0.4), "white", rot=(-0.05, 0, 0))
    for x in (-6, 6):
        m.tube((x, -7.5, 0.3), (x, -7.5, 4.3), 0.25, 0.25, "steel", seg=8)
    # the crystal fountain: tiered bowls of glass on a stepped plinth
    fy = -12
    m.cyl((0, fy, 0.5), 4.2, 4.4, 1.0, "marble", seg=18)
    m.cyl((0, fy, 1.02), 3.9, 3.9, 0.05, "tarp_blue", seg=18)
    for k, (r, z) in enumerate(((2.4, 2.2), (1.6, 4.0), (0.9, 5.6))):
        m.cyl((0, fy, z), r, r * 0.4, 0.8, "glass", seg=14)
        m.tube((0, fy, z - 1.4 if k else 1.0), (0, fy, z), 0.3, 0.25, "glass", seg=8)
    m.ball((0, fy, 6.5), (0.5, 0.5, 0.8), "glass", seg=10, rings=7, smooth=True)
    return m


# ============================================================================ Stadium Merdeka
def stadium_merdeka():
    """Stadium Merdeka: the open oval bowl - stands striped blue, red, yellow and white round the red running track
    and the pitch - the main grandstand under its scalloped roof of red-and-white awnings, and the floodlight masts."""
    m = K.Mesher()
    rx, ry = 13, 16
    m.box((0, 0, 0.15), (36, 36, 0.3), "concrete")
    for k, (col, sc, z) in enumerate((("tile_red", 1.0, 0.32), ("pitch_green", 0.8, 0.36))):
        m.cyl((0, 0, z), rx * sc, rx * sc, 0.08, col, seg=28, scale_xy=(1, ry / rx))
    m.box((0, 0, 0.42), (rx * 0.9, ry * 1.1, 0.02), "pitch_green")
    m.box((0, 0, 0.44), (0.12, ry * 1.1, 0.02), "white")
    stripes = ["stand_blue", "bf_red", "crate_yellow", "white"]
    for t in range(7):                                                                     # stepped seating
        r = 1.02 + t * 0.06
        n = 36
        for i in range(n):
            a0, a1 = i / n * math.tau, (i + 1) / n * math.tau
            p0 = Vector((math.cos(a0) * rx * r, math.sin(a0) * ry * r, 0))
            p1 = Vector((math.cos(a1) * rx * r, math.sin(a1) * ry * r, 0))
            mid = (p0 + p1) / 2
            if mid.y < -ry * r * 0.9:
                continue
            d = p1 - p0
            col = stripes[(i // 3) % 4] if t < 5 else "white"
            m.box((mid.x, mid.y, 0.5 + t * 0.45), (d.length + 0.15, 0.9, 1.0 + t * 0.9), col, rot=(0, 0, math.atan2(d.y, d.x)))
    oval_ring(m, rx * 1.47, ry * 1.47, 3.3, 6.6, 0.6, "white", n=28, gap=True)
    # the main grandstand (west): a roof of scalloped awnings striped red and white
    gx = -rx * 1.32
    m.box((gx, 0, 7.6), (4.5, 16, 0.4), "white")
    for i in range(8):
        y = -7 + i * 2.0
        m.ball((gx - 2.3, y, 7.2), (0.6, 1.0, 0.9), "bf_red" if i % 2 == 0 else "white", seg=8, rings=4)
        m.box((gx, y, 7.85), (4.6, 1.0, 0.12), "bf_red" if i % 2 == 0 else "white")
    for y in (-7, -2, 3, 7):
        m.tube((gx + 1.8, y, 3.0), (gx + 1.8, y, 7.5), 0.18, 0.18, "steel", seg=6)
    m.box((gx - 1.0, 0, 8.6), (0.3, 6.0, 1.0), "sign_blank")
    for sx in (1, -1):
        for sy in (1, -1):
            x, y = sx * rx * 1.3, sy * ry * 1.25
            m.tube((x, y, 0.3), (x, y, 20), 0.35, 0.25, "steel", seg=8)
            m.box((x, y, 20.8), (3, 0.6, 2), "steel", rot=(0, 0, math.atan2(-y, -x) + math.pi / 2))
            m.box((x, y - 0.32 * sy, 20.8), (2.6, 0.1, 1.6), "lamp_glow", rot=(0, 0, math.atan2(-y, -x) + math.pi / 2))
    return m


KIT_LANDMARKS2 = {
    "env_lm2_batu_caves": (batu_caves, ((0, 10, 9), (34, 14, 18))),
    "env_lm2_merdeka118": (merdeka_118, ((0, 0, 30), (30, 30, 60))),
    "env_lm2_tugu_negara": (tugu_negara, ((0, 6, 2.2), (10, 8, 4.4))),
    "env_lm2_istana_negara": (istana_negara, ((0, 6, 5), (34, 10, 10))),
    "env_lm2_thean_hou": (thean_hou, ((0, 2, 4), (24, 18, 8))),
    "env_lm2_masjid_jamek": (masjid_jamek, ((0, 5, 4), (18, 14, 8))),
    "env_lm2_pavilion": (pavilion, ((0, 6, 9), (36, 20, 18))),
    "env_lm2_stadium_merdeka": (stadium_merdeka, None),      # hollow bowl: walls get colliders in the game
}
