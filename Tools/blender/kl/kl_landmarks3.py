"""
KL landmarks kit 3 (env_kl_landmarks3): the real-KL landmarks, rebuilt against reference photos
(Tools/references/landmarks) and fitted to their real OSM footprints (Tools/klmap/landmark_footprint.py).

Conventions as the other kits: front = -Y while authoring (turned 180 at assembly so it faces Unity +Z); origin at
the patch anchor, ground at z = 0. Unlike kits 1 and 2 these are modelled at GAME scale (real metres x 0.65 across;
heights x 0.65 for the towers, a little more for the low heritage buildings so they hold their own beside the
real-height streets), so the game places them unscaled (CityBuilder.LandmarkFit (0, 0, yaw)).

Physics: the game makes a MeshCollider from each module's distance version (*_LOD1), so what you can stand on is
what is drawn. No COL_ boxes.

Cartoon style: flat palette paint, bold shapes, painted (modelled) trim; no textures.
"""
import math
import random
import bmesh
from mathutils import Vector

import kl_core as K

TAU = math.tau


def lod():
    """True while kl_build makes the distance version (drop fine trim)."""
    return K.LOD_CULL > 0


# ------------------------------------------------------------------------------ outline helpers
def area2(pts):
    return sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1] for i in range(len(pts)))


def ccw(pts):
    pts = [tuple(p) for p in pts]
    return pts if area2(pts) > 0 else list(reversed(pts))


def offset(pts, d, limit=3.0):
    """Mitred offset of a CCW outline (d > 0 grows it)."""
    pts = ccw(pts)
    n = len(pts)
    out = []
    for i in range(n):
        p0, p1, p2 = Vector(pts[i - 1]), Vector(pts[i]), Vector(pts[(i + 1) % n])
        e0, e1 = (p1 - p0), (p2 - p1)
        if e0.length < 1e-6 or e1.length < 1e-6:
            out.append(tuple(p1))
            continue
        n0 = Vector((e0.y, -e0.x)).normalized()
        n1 = Vector((e1.y, -e1.x)).normalized()
        b = n0 + n1
        if b.length < 1e-6:
            b = n0
        b.normalize()
        k = 1.0 / max(0.2, b.dot(n0))
        k = min(k, limit)
        q = p1 + b * d * k
        out.append((q.x, q.y))
    return out


def circle(r, n, cx=0.0, cy=0.0, a0=0.0):
    return [(cx + math.cos(a0 + i / n * TAU) * r, cy + math.sin(a0 + i / n * TAU) * r) for i in range(n)]


def moved(pts, dx, dy):
    return [(x + dx, y + dy) for x, y in pts]


def scaled(pts, k, cx=0.0, cy=0.0):
    return [(cx + (x - cx) * k, cy + (y - cy) * k) for x, y in pts]


def star8(R, per=6, c=0.6, rho=0.29):
    """The Petronas plan: an 8-point star (two squares) with a round lobe filling each inner corner."""
    def sq(t):
        t = (t % (math.pi / 2)) - math.pi / 4
        return R * math.cos(math.pi / 4) / math.cos(t)

    def arc(t):
        best = 0.0
        for k in range(8):
            a = math.pi / 8 + k * math.pi / 4
            d = t - a
            cc, rr = c * R, rho * R
            disc = (cc * math.cos(d)) ** 2 - (cc * cc - rr * rr)
            if disc >= 0:
                best = max(best, cc * math.cos(d) + math.sqrt(disc))
        return best

    pts = []
    for k in range(8):
        for j in range(per):
            t = k * math.pi / 4 + j * (math.pi / 4) / per
            r = max(sq(t), sq(t - math.pi / 4), arc(t))
            pts.append((math.cos(t) * r, math.sin(t) * r))
    return pts


def star_n(n, r_out, r_in, a0=0.0):
    """An n-pointed star outline (alternate radii)."""
    return [(math.cos(a0 + i / (2 * n) * TAU) * (r_out if i % 2 == 0 else r_in),
             math.sin(a0 + i / (2 * n) * TAU) * (r_out if i % 2 == 0 else r_in)) for i in range(2 * n)]


def rect(cx, cy, w, d):
    return [(cx - w / 2, cy - d / 2), (cx + w / 2, cy - d / 2), (cx + w / 2, cy + d / 2), (cx - w / 2, cy + d / 2)]


# ------------------------------------------------------------------------------ bmesh primitives
def _face(tmp, vs, expect):
    f = tmp.faces.new(vs)
    f.normal_update()
    if f.normal.dot(Vector(expect)) < 0:
        f.normal_flip()
    return f


def prism(m, pts, z0, z1, color, top=True, bottom=False, cap_color=None, smooth=False):
    """Vertical extrusion of a 2D outline from z0 to z1 (walls, a top cap, optionally the bottom)."""
    pts = ccw(pts)
    tmp = bmesh.new()
    lo = [tmp.verts.new((x, y, z0)) for x, y in pts]
    hi = [tmp.verts.new((x, y, z1)) for x, y in pts]
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        tmp.faces.new((lo[i], lo[j], hi[j], hi[i]))
    caps = []
    if top and cap_color is None:
        caps.append(tmp.faces.new(hi))
    if bottom:
        caps.append(tmp.faces.new(list(reversed(lo))))
    if caps:
        bmesh.ops.triangulate(tmp, faces=caps, quad_method='BEAUTY', ngon_method='EAR_CLIP')
    m._merge(tmp, color, smooth=smooth)
    if top and cap_color is not None:
        cap(m, pts, z1, cap_color)


def cap(m, pts, z, color, down=False):
    pts = ccw(pts)
    tmp = bmesh.new()
    vs = [tmp.verts.new((x, y, z)) for x, y in pts]
    f = tmp.faces.new(list(reversed(vs)) if down else vs)
    bmesh.ops.triangulate(tmp, faces=[f], quad_method='BEAUTY', ngon_method='EAR_CLIP')
    m._merge(tmp, color)


def loft(m, rings, color, close_top=True, close_bottom=False, smooth=False):
    """Skin a stack of outlines [(pts, z), ...] (same vertex count) into one surface."""
    tmp = bmesh.new()
    vr = [[tmp.verts.new((x, y, z)) for x, y in ccw(pts)] for pts, z in rings]
    n = len(vr[0])
    for a, b in zip(vr, vr[1:]):
        for i in range(n):
            j = (i + 1) % n
            tmp.faces.new((a[i], a[j], b[j], b[i]))
    caps = []
    if close_top:
        caps.append(tmp.faces.new(vr[-1]))
    if close_bottom:
        caps.append(tmp.faces.new(list(reversed(vr[0]))))
    if caps:
        bmesh.ops.triangulate(tmp, faces=caps, quad_method='BEAUTY', ngon_method='EAR_CLIP')
    m._merge(tmp, color, smooth=smooth)


def band(m, pts, z, h, out, color, top=False):
    """A ledge round an outline: an outer wall `out` proud of it from z to z + h, with its underside (and top)."""
    inner = ccw(pts)
    outer = offset(inner, out)
    tmp = bmesh.new()
    n = len(inner)
    il = [tmp.verts.new((x, y, z)) for x, y in inner]
    ol = [tmp.verts.new((x, y, z)) for x, y in outer]
    oh = [tmp.verts.new((x, y, z + h)) for x, y in outer]
    ih = [tmp.verts.new((x, y, z + h)) for x, y in inner] if top else None
    for i in range(n):
        j = (i + 1) % n
        tmp.faces.new((ol[i], ol[j], oh[j], oh[i]))                    # outer wall (CCW outline: faces out)
        tmp.faces.new((il[j], ol[j], ol[i], il[i]))                    # underside
        if top:
            tmp.faces.new((ih[i], oh[i], oh[j], ih[j]))
    for f in tmp.faces:
        f.normal_update()
    # undersides must face down, tops up
    for f in tmp.faces:
        c = f.calc_center_median()
        if abs(f.normal.z) > 0.5:
            want = 1 if c.z > z + h * 0.5 else -1
            if f.normal.z * want < 0:
                f.normal_flip()
    m._merge(tmp, color)


def pane_strip(m, pts, z0, z1, out, color, every=1, gap=0.0):
    """Painted window panes on the faces of an outline: one flat quad per edge (a hair proud of the wall)."""
    inner = ccw(pts)
    outer = offset(inner, out)
    n = len(inner)
    tmp = bmesh.new()
    for i in range(0, n, every):
        j = (i + 1) % n
        a, b = Vector(outer[i]), Vector(outer[j])
        if gap > 0:
            d = (b - a)
            if d.length <= 2 * gap:
                continue
            a, b = a + d.normalized() * gap, b - d.normalized() * gap
        vs = [tmp.verts.new((a.x, a.y, z0)), tmp.verts.new((b.x, b.y, z0)), tmp.verts.new((b.x, b.y, z1)), tmp.verts.new((a.x, a.y, z1))]
        tmp.faces.new(vs)
    m._merge(tmp, color)


def arch_window(m, x, y, z, w, h, color, frame=None, depth=0.12, rot=0.0, pointed=False, seg=8):
    """A flat arched opening (painted) in a wall facing -Y (rot turns it about Z): rectangle + half-round head,
    optionally with a frame colour behind it."""
    def put(dx, dy, dz):
        c, s = math.cos(rot), math.sin(rot)
        return (x + dx * c - dy * s, y + dx * s + dy * c, z + dz)
    pts = [(-w / 2, 0), (w / 2, 0), (w / 2, h - w / 2)]
    for i in range(1, seg):
        a = i / seg * math.pi
        if pointed:
            # two arcs meeting at a point (an ogee-ish Moorish head)
            t = i / seg
            px = w / 2 * math.cos(a)
            pz = h - w / 2 + w * 0.62 * math.sin(a) ** 0.8 * (1 - 0.15 * abs(math.cos(a)))
            pts.append((px, pz))
        else:
            pts.append((w / 2 * math.cos(a), h - w / 2 + w / 2 * math.sin(a)))
    pts.append((-w / 2, h - w / 2))
    if frame:
        sx, sz = (w + 0.5) / w, (h + 0.3) / h
        fp = [(px * sx, pz * sz - 0.1) for px, pz in pts]
        m.poly_extrude(fp, depth * 0.6, frame, loc=put(0, -depth * 0.3, 0), rot=(0, 0, rot))
    m.poly_extrude(pts, depth, color, loc=put(0, -depth * 0.8, 0), rot=(0, 0, rot))


def onion(m, c, r, color, tip="gold", seg=12, neck=0.55, height=1.15, finial=True):
    """A Moorish onion dome on a short drum: bulging out above its base, drawn to a point."""
    c = Vector(c)
    prof = [(0.80, 0.0), (0.92, 0.12), (1.0, 0.35), (0.98, 0.55), (0.86, 0.78), (0.64, 1.0), (0.38, 1.18), (0.16, 1.34), (0.0, 1.46)]
    rings = []
    for k, (rk, zk) in enumerate(prof[:-1]):
        rings.append((circle(r * rk, seg, c.x, c.y), c.z + r * zk * height))
    loft(m, rings, color, close_top=False)
    # the point
    tmp = bmesh.new()
    top = tmp.verts.new((c.x, c.y, c.z + r * prof[-1][1] * height))
    ring = [tmp.verts.new((x, y, rings[-1][1])) for x, y in ccw(rings[-1][0])]
    for i in range(seg):
        tmp.faces.new((ring[i], ring[(i + 1) % seg], top))
    m._merge(tmp, color)
    if finial and not lod():
        zt = c.z + r * prof[-1][1] * height
        m.tube((c.x, c.y, zt - r * 0.05), (c.x, c.y, zt + r * 0.55), r * 0.05, r * 0.03, tip, seg=6)
        m.ball((c.x, c.y, zt + r * 0.25), (r * 0.09,) * 3, tip, seg=8, rings=5)


def dome(m, c, r, color, seg=16, squash=1.0, rings=6):
    """A half-round dome on z = c.z."""
    c = Vector(c)
    rs = []
    for k in range(rings):
        a = k / rings * (math.pi / 2)
        rs.append((circle(r * math.cos(a), seg, c.x, c.y), c.z + r * squash * math.sin(a)))
    loft(m, rs, color, close_top=True, smooth=True)


def chhatri(m, c, r, h, post="white", roof="white", tip="gold", n=6, seg=10):
    """A little open pavilion of posts under a dome (the Mughal kiosks on Moorish roofs)."""
    c = Vector(c)
    m.cyl(c + Vector((0, 0, 0.12 * r)), r * 1.1, r * 1.1, 0.24 * r, post, seg=seg)
    if not lod():
        for k in range(n):
            a = k / n * TAU
            p = c + Vector((math.cos(a) * r * 0.85, math.sin(a) * r * 0.85, 0))
            m.tube(p + Vector((0, 0, 0.2 * r)), p + Vector((0, 0, h)), r * 0.09, r * 0.09, post, seg=5)
    else:
        m.cyl(c + Vector((0, 0, h / 2)), r * 0.85, r * 0.85, h, post, seg=seg)
    m.cyl(c + Vector((0, 0, h + 0.08 * r)), r * 1.15, r * 1.15, 0.16 * r, post, seg=seg)
    onion(m, c + Vector((0, 0, h + 0.16 * r)), r * 0.95, roof, tip=tip, seg=seg)


def flag_malaysia(m, x, y, z, w, h, rot=0.0, thick=0.08):
    """The Jalur Gemilang hanging from a pole at (x, y): 14 red and white stripes, the blue canton with the yellow
    crescent and star (z = the top edge, w along +X)."""
    c, s = math.cos(rot), math.sin(rot)

    def put(dx, dz):
        return (x + dx * c, y + dx * s, z - dz)
    n = 14 if not lod() else 7
    for i in range(n):
        m.box(put(w / 2, (i + 0.5) * h / n), (w, thick, h / n + 0.01), "flag_red" if i % 2 == 0 else "white", rot=(0, 0, rot))
    cw, ch = w * 0.5, h * 8 / 14
    m.box(put(cw / 2, ch / 2), (cw, thick * 1.6, ch), "flag_blue", rot=(0, 0, rot))
    if not lod():
        m.cyl(put(cw * 0.42, ch * 0.5), ch * 0.34, ch * 0.34, thick * 2.4, "gold", seg=14, rot=(math.pi / 2, 0, rot))
        m.cyl(put(cw * 0.5, ch * 0.5), ch * 0.28, ch * 0.28, thick * 2.8, "flag_blue", seg=14, rot=(math.pi / 2, 0, rot))
        m.poly_extrude(star_n(7, ch * 0.22, ch * 0.09, math.pi / 2), thick * 3.0, "gold", loc=put(cw * 0.72, ch * 0.5), rot=(0, 0, rot))


# ============================================================================ Petronas Twin Towers + Suria KLCC
PT_H = 294.0          # 452 m x 0.65
PT_R = 16.0           # star radius at the foot (the plan is ~ 50 m across)
PT_X, PT_Y = 35.5, -4.0  # tower centres (authoring), from the OSM footprint at yaw 140


def petronas_tower(m, cx, cy, bustle_dir):
    H, R = PT_H, PT_R
    # the star shaft in six setbacks, then the round crown and the pinnacle (fractions of the full height)
    tiers = [(0.0, 0.585, 1.0), (0.585, 0.655, 0.93), (0.655, 0.72, 0.85), (0.72, 0.755, 0.76),
             (0.755, 0.785, 0.66), (0.785, 0.81, 0.55)]
    base = star8(R, per=6 if not lod() else 3)
    step = 6.5 if not lod() else 0
    for z0f, z1f, k in tiers:
        z0, z1 = z0f * H, z1f * H
        pts = moved(scaled(base, k), cx, cy)
        prism(m, pts, z0, z1, "petronas_glass")
        # stainless sun-shade fins (every couple of floors) and a deeper ledge at each setback
        if step:
            z = z0 + 2.8
            while z < z1 - 1.5:
                band(m, pts, z, 0.75, 0.38, "petronas_steel")
                z += step
        band(m, pts, z1 - 1.3, 1.3, 0.75, "petronas_steel", top=True)
    # the crown: round drums ringed with columns
    zc = 0.81 * H
    drums = [(0.42, 3.5), (0.36, 3.0), (0.30, 2.6), (0.24, 2.4)]
    for rf, h in drums:
        r = rf * R
        m.cyl((cx, cy, zc + h / 2), r, r * 0.97, h, "petronas_glass", seg=24 if not lod() else 12)
        m.cyl((cx, cy, zc + h - 0.25), r * 1.08, r * 1.08, 0.5, "petronas_steel", seg=24 if not lod() else 12)
        if not lod():
            for i in range(16):
                a = i / 16 * TAU
                m.box((cx + math.cos(a) * r * 1.02, cy + math.sin(a) * r * 1.02, zc + h / 2), (0.45, 0.45, h), "petronas_steel",
                      rot=(0, 0, a))
        zc += h
    # pinnacle: a fluted cone, the ball, rings and the mast
    m.cyl((cx, cy, zc + 5), 0.2 * R, 0.08 * R, 10, "petronas_steel", seg=16 if not lod() else 8)
    zc += 10
    top = H
    m.tube((cx, cy, zc - 1), (cx, cy, top), 0.075 * R, 0.022 * R, "petronas_steel", seg=10 if not lod() else 6)
    zb = zc + (top - zc) * 0.28
    m.ball((cx, cy, zb), (0.13 * R,) * 3, "petronas_steel", seg=14 if not lod() else 8, rings=10 if not lod() else 6, smooth=True)
    for f, rr in ((0.12, 0.11), (0.45, 0.075), (0.62, 0.06), (0.78, 0.045)):
        z = zc + (top - zc) * f
        m.cyl((cx, cy, z), rr * R, rr * R, 0.7, "petronas_steel", seg=12 if not lod() else 6)
    # the bustle: a round annexe of 44 floors on the Jalan Ampang side, stepping back at its top
    bx, by = cx + bustle_dir[0], cy + bustle_dir[1]
    br = 7.6
    bt = [(0.0, 0.40, 1.0), (0.40, 0.425, 0.86), (0.425, 0.44, 0.68)]
    for z0f, z1f, k in bt:
        pts = circle(br * k, 20 if not lod() else 10, bx, by)
        prism(m, pts, z0f * H, z1f * H, "petronas_glass")
        if step:
            z = z0f * H + 2.8
            while z < z1f * H - 1.5:
                band(m, pts, z, 0.75, 0.32, "petronas_steel")
                z += step
        band(m, pts, z1f * H - 1.2, 1.2, 0.6, "petronas_steel", top=True)
    m.cyl((bx, by, 0.44 * H + 2.5), br * 0.4, br * 0.15, 5, "petronas_steel", seg=12)


def skybridge(m):
    """The double-decker skybridge at levels 41-42 and its two-hinged arch down to level 29."""
    H, R = PT_H, PT_R
    zb = 0.376 * H
    face = PT_X - R * 0.93 + 1.0
    y = PT_Y
    # two decks: glass walls under steel roofs
    for k, z in enumerate((zb, zb + 3.2)):
        m.box((0, y, z + 1.3), (2 * face, 4.6, 2.6), "petronas_glass")
        m.box((0, y, z + 2.75), (2 * face, 5.0, 0.5), "petronas_steel")
        m.box((0, y, z - 0.05), (2 * face, 5.0, 0.3), "petronas_steel")
    if not lod():
        for i in range(-6, 7):
            m.box((i * face / 6.5, y, zb + 3.0), (0.25, 5.1, 6.0), "petronas_steel")
    # the arch: legs from under the middle of the bridge out and down to each tower
    zl = 0.264 * H
    for s in (1, -1):
        for dy in (-1.8, 1.8):
            m.tube((0.0, y + dy * 0.6, zb - 0.2), (s * (face + 1.5), y + dy, zl), 0.55, 0.55, "petronas_steel", seg=8)
        m.ball((s * (face + 1.2), y, zl), (1.1, 2.6, 1.1), "petronas_steel", seg=8, rings=5)
    m.ball((0, y, zb - 0.6), (1.4, 2.4, 1.0), "petronas_steel", seg=8, rings=5)


SURIA = [(80, -25), (58, -8), (51, -10), (48, -14.5), (24, -16.5), (20, -11), (-20, -11), (-24, -16.5), (-48, -14.5), (-51, -10),
         (-58, -7), (-80, -23), (-58, -45), (-78.5, -68), (-84, -75.5), (-50.5, -108)]


def suria(m):
    """Suria KLCC: the mall wrapped round the towers' foot, its curved glass front to the park."""
    # the front to the park: a concave glass curve between the two big corner wings
    front = []
    for i in range(1, 12):
        t = i / 12
        x = -44 + 86 * t
        y = -108 + 25 * math.sin(t * math.pi) ** 0.8
        front.append((x, y))
    pts = SURIA + front + [(48.5, -110), (82, -75.5), (78.5, -69), (72, -62), (68, -55), (68, -44), (73, -32)]
    H1, H2 = 9.0, 17.5
    prism(m, pts, 0.0, H1, "cream")
    inner = offset(pts, -5.0)
    prism(m, inner, H1, H2, "cream", cap_color="concrete")
    # glass bands and white ledges
    band(m, pts, 1.0, 3.6, 0.12, "petronas_glass")
    band(m, pts, H1 - 0.8, 0.8, 0.5, "white", top=True)
    band(m, inner, H1 + 1.6, 4.2, 0.12, "petronas_glass")
    band(m, inner, H2 - 0.6, 0.6, 0.4, "white", top=True)
    # the park front is all glass, framed in white, under a curved canopy
    front_w = [(x, y + 0.2) for x, y in front]
    for i in range(len(front_w) - 1):
        (x0, y0), (x1, y1) = front_w[i], front_w[i + 1]
        a = math.atan2(y1 - y0, x1 - x0)
        L = math.hypot(x1 - x0, y1 - y0)
        mx, my = (x0 + x1) / 2, (y0 + y1) / 2
        m.box((mx, my - 0.15, H2 * 0.5), (L + 0.1, 0.3, H2 - 1.2), "petronas_glass", rot=(0, 0, a))
        m.box((x0, y0 - 0.3, H2 * 0.5), (0.5, 0.5, H2), "white", rot=(0, 0, a))
        m.box((mx, my - 2.0, 6.5), (L + 0.2, 4.0, 0.5), "white", rot=(0, 0, a))
    # the esplanade steps down to the park
    m.box((0, -100, 0.2), (60, 6, 0.4), "concrete")


def petronas():
    m = K.Mesher()
    bustle = (-3.0, 20.5)            # toward Jalan Ampang (+Y), a little in
    petronas_tower(m, PT_X, PT_Y, (bustle[0], bustle[1]))
    petronas_tower(m, -PT_X, PT_Y, (-bustle[0], bustle[1]))
    skybridge(m)
    suria(m)
    # the towers' own entrance podium on the Jalan Ampang side
    prism(m, rect(0, 18, 40, 14), 0, 6.5, "cream", cap_color="concrete")
    band(m, rect(0, 18, 40, 14), 5.8, 0.7, 0.4, "petronas_steel", top=True)
    m.box((0, 25.6, 3.0), (18, 0.3, 4.6), "petronas_glass")
    m.box((0, 28.5, 6.4), (22, 6, 0.5), "petronas_steel")
    for x in (-10, -4, 4, 10):
        m.tube((x, 31, 0), (x, 31, 6.2), 0.25, 0.25, "petronas_steel", seg=8)
    return m


# ============================================================================ Menara KL
KT_H = 274.0          # 421 m x 0.65


def menara_kl():
    """Menara KL on Bukit Nanas: the tapering white shaft, the faceted head (glass observation deck and revolving
    restaurant over a fluted bowl, under a cap) and the long antenna; the 12-point-star foyer at its foot with the
    muqarnas-hooded entrance."""
    m = K.Mesher()
    H = KT_H
    lo = lod()
    # foyer: a 12-point star in plan
    foy = star_n(12, 19.0, 15.5, a0=math.pi / 12)
    prism(m, foy, 0, 7.0, "white", cap_color="concrete")
    band(m, foy, 1.2, 3.4, 0.12, "glass")
    band(m, foy, 6.3, 0.7, 0.45, "gold", top=True)
    inner = star_n(12, 14.0, 11.5, a0=math.pi / 12)
    prism(m, inner, 7.0, 10.5, "white", cap_color="concrete")
    band(m, inner, 7.8, 2.0, 0.1, "glass")
    # the entrance: a tall pointed portal with a ribbed hood (front, -Y)
    m.box((0, -19.4, 5.0), (9.0, 3.0, 10.0), "white")
    arch_window(m, 0, -20.9, 0.0, 5.6, 8.4, "glass", frame="gold", pointed=True)
    if not lo:
        for i in range(7):
            a = (i + 0.5) / 7 * math.pi
            m.box((math.cos(a) * 3.2, -20.95, 6.2 + math.sin(a) * 2.6), (0.35, 0.2, 1.2), "gold", rot=(0, a - math.pi / 2, 0))
    # the shaft: a tapering cylinder with the lift core stripe
    z0, z1 = 10.5, 0.565 * H
    segs = 20 if not lo else 10
    m.cyl((0, 0, (z0 + z1) / 2), 6.8, 4.6, z1 - z0, "white", seg=segs)
    if not lo:
        m.box((0, -5.3, (z0 + z1) / 2), (1.6, 1.2, z1 - z0 - 4), "glass", rot=(-(6.8 - 4.6) / (z1 - z0), 0, 0))
        for k in range(6):
            z = z0 + 18 + k * 21
            r = 6.8 - (6.8 - 4.6) * (z - z0) / (z1 - z0)
            m.cyl((0, 0, z), r + 0.25, r + 0.25, 0.6, "white", seg=segs)
    # the head
    zh = z1
    rings = [(4.6, 0.0), (6.0, 4.0), (9.0, 9.0), (12.0, 14.0), (13.6, 18.0)]
    loft(m, [(circle(r, 24 if not lo else 12), zh + dz) for r, dz in rings], "white", close_top=False)
    if not lo:
        # the bowl's ribs (an Islamic muqarnas look, painted gold)
        for i in range(24):
            a = i / 24 * TAU
            p0 = Vector((math.cos(a) * 5.0, math.sin(a) * 5.0, zh + 1.2))
            p1 = Vector((math.cos(a) * 13.4, math.sin(a) * 13.4, zh + 17.6))
            m.tube(p0, p1, 0.22, 0.3, "gold", seg=5)
    zd = zh + 18.0
    m.cyl((0, 0, zd + 0.4), 14.2, 14.2, 0.8, "white", seg=24 if not lo else 12)
    m.cyl((0, 0, zd + 4.2), 14.0, 14.6, 6.8, "glass", seg=24 if not lo else 12)          # observation deck
    m.cyl((0, 0, zd + 8.0), 15.0, 15.0, 0.8, "white", seg=24 if not lo else 12)
    m.cyl((0, 0, zd + 11.6), 14.6, 13.6, 6.4, "dome_blue", seg=24 if not lo else 12)     # the restaurant
    m.cyl((0, 0, zd + 15.2), 14.0, 14.0, 0.8, "white", seg=24 if not lo else 12)
    if not lo:
        for i in range(24):
            a = i / 24 * TAU
            m.box((math.cos(a) * 14.4, math.sin(a) * 14.4, zd + 4.2), (0.3, 0.3, 6.8), "white", rot=(0, 0, a))
    # the cap: a shallow cone, the drum and the mast
    zt = zd + 15.6
    loft(m, [(circle(r, 24 if not lo else 12), zt + dz) for r, dz in ((13.6, 0), (10.5, 3.0), (6.0, 6.0), (3.6, 7.2))], "white")
    m.cyl((0, 0, zt + 11), 3.2, 2.8, 7.6, "white", seg=12)
    m.cyl((0, 0, zt + 15.2), 3.6, 3.6, 0.8, "gold", seg=12)
    m.cyl((0, 0, zt + 20), 2.0, 1.6, 9.0, "white", seg=10)
    m.tube((0, 0, zt + 24), (0, 0, H), 1.1, 0.15, "steel", seg=8)
    if not lo:
        for f in (0.25, 0.5, 0.72):
            z = zt + 24 + (H - zt - 24) * f
            m.cyl((0, 0, z), 1.4 - f, 1.4 - f, 0.8, "white", seg=8)
        m.ball((0, 0, H), (0.4, 0.4, 0.4), "lamp_red", seg=8, rings=5)
    return m


# ============================================================================ Sultan Abdul Samad + Dataran flagpole
SAS_TOWER_X = 9.0     # the clock tower along the front (authoring), from the OSM footprint at yaw 281


def sas_arcade(m, x0, x1, y, z, h, n, pointed=False):
    """A verandah of arches on the front face (y), painted dark in a white surround."""
    w = (x1 - x0) / n
    for i in range(n):
        x = x0 + (i + 0.5) * w
        arch_window(m, x, y, z + 0.35, w * 0.62, h - 0.9, "window_dark", frame="white", pointed=pointed, seg=8 if not lod() else 4)


def sas_wing(m, x0, x1, y0, y1, z_top, floors=2):
    """A two-storey brick range with white bands, front arcades and a hipped roof behind a parapet."""
    w, d = x1 - x0, y1 - y0
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    prism(m, rect(cx, cy, w, d), 0, z_top, "brick_red", cap_color="concrete_dark")
    fh = z_top / floors
    for f in range(floors):
        z = f * fh
        band(m, rect(cx, cy, w, d), z + fh - 0.55, 0.55, 0.22, "white", top=True)
        if not lod():
            band(m, rect(cx, cy, w, d), z + 0.0, 0.5, 0.18, "white")
        n = max(2, int(round(w / 3.1)))
        sas_arcade(m, x0 + 0.4, x1 - 0.4, y0, z, fh - 0.55, n, pointed=True)
    # parapet with little white posts
    prism(m, offset(rect(cx, cy, w, d), 0.15), z_top, z_top + 0.9, "white", top=False)
    m.prism((cx, cy, z_top), (w - 1.0, d - 1.0, 2.4), "copper_dark")


def sas_tower(m, x, y, s, h_shaft, dome_r, clock=False):
    """A square brick tower with white bands, an open belfry/kiosk stage and a copper onion dome."""
    prism(m, rect(x, y, s, s), 0, h_shaft, "brick_red", cap_color="white")
    for z in range(3, int(h_shaft), 3 if not lod() else 6):
        band(m, rect(x, y, s, s), z, 0.45, 0.2, "white")
    band(m, rect(x, y, s, s), h_shaft - 0.8, 0.8, 0.45, "white", top=True)
    if clock:
        for k in range(4):
            a = k * math.pi / 2
            dx, dy = -math.sin(a), -math.cos(a)
            cxk, cyk = x + dx * (s / 2 + 0.05), y + dy * (s / 2 + 0.05)
            m.cyl((cxk, cyk, h_shaft - 3.4), s * 0.36, s * 0.36, 0.25, "white", seg=16, rot=(math.pi / 2, 0, -a))
            m.cyl((cxk + dx * 0.1, cyk + dy * 0.1, h_shaft - 3.4), s * 0.3, s * 0.3, 0.2, "charcoal", seg=16, rot=(math.pi / 2, 0, -a))
            if not lod():
                m.box((cxk + dx * 0.25, cyk + dy * 0.25, h_shaft - 2.6), (0.18, 0.1, s * 0.22), "gold", rot=(0, 0, -a))
                m.box((cxk + dx * 0.25 - dy * 0.5, cyk + dy * 0.25 + dx * 0.5, h_shaft - 3.4), (s * 0.18, 0.1, 0.18), "gold", rot=(0, 0, -a))
    # the octagonal stage of arches
    z = h_shaft
    st = s * 0.8
    octa = circle(st / 2 / math.cos(math.pi / 8), 8, x, y, math.pi / 8)
    prism(m, octa, z, z + s * 0.55, "white")
    if not lod():
        for k in range(8):
            a = math.pi / 8 + k * math.pi / 4 + math.pi / 8
            r = st / 2 + 0.06
            arch_window(m, x + math.cos(a - math.pi / 8) * r * 0.0 + math.cos(a) * r, y + math.sin(a) * r, z + 0.4,
                        st * 0.28, s * 0.42, "window_dark", rot=a + math.pi / 2, seg=6)
    band(m, octa, z + s * 0.55, 0.5, 0.35, "white", top=True)
    onion(m, (x, y, z + s * 0.55 + 0.5), dome_r, "copper", tip="gold", seg=14 if not lod() else 8)


def sultan_abdul_samad():
    """The Sultan Abdul Samad Building on Jalan Raja: a long two-storey Moorish range of red brick banded in white,
    arcaded verandahs on both floors, the 41 m clock tower in the middle under a copper onion dome, and two smaller
    domed towers framing it; little copper-domed kiosks along the roof."""
    m = K.Mesher()
    tx = SAS_TOWER_X
    zw = 13.0
    # the front range along Jalan Raja (front -Y), with wings back at each end
    sas_wing(m, -58, 34, -6.5, 4.5, zw)
    sas_wing(m, -58, -38, 4.5, 16.0, zw)
    sas_wing(m, 22, 34, 4.5, 23.5, zw)
    sas_wing(m, -20, 0, 4.5, 12.0, zw - 1.5)
    # the clock tower projects forward of the front
    prism(m, rect(tx, -8.5, 11.0, 6.0), 0, zw + 1.0, "brick_red", cap_color="white")
    band(m, rect(tx, -8.5, 11.0, 6.0), zw + 0.2, 0.8, 0.3, "white", top=True)
    arch_window(m, tx, -11.6, 0.3, 4.6, 8.0, "window_dark", frame="white", pointed=True)
    arch_window(m, tx, -11.6, 8.8, 4.0, 4.0, "window_dark", frame="white", pointed=True)
    sas_tower(m, tx, -6.0, 7.2, 28.0, 4.0, clock=True)
    # the two side towers with their domes (at each end of the front)
    for x in (-36.0, 28.5):
        prism(m, rect(x, -7.5, 7.5, 4.5), 0, zw + 0.6, "brick_red", cap_color="white")
        sas_tower(m, x, -5.0, 5.4, zw + 7.0, 2.9)
    # little copper domed kiosks along the roof line
    for x in (-52, -44, -28, -12, 0, 18):
        chhatri(m, (x, -5.6, zw + 0.9), 0.95, 2.0, post="white", roof="copper", seg=8)
    # the porch in front of the clock tower
    prism(m, rect(tx, -12.5, 9.0, 2.8), 0, 5.0, "white", cap_color="white")
    for x in (-3.2, 0, 3.2):
        arch_window(m, tx + x, -13.95, 0.0, 2.2, 4.4, "window_dark", seg=6)
    return m


def dataran_flagpole():
    """The 95 m flagpole at the south end of Dataran Merdeka: a round stepped plinth, the pole and the
    Jalur Gemilang."""
    m = K.Mesher()
    for k, (r, h) in enumerate(((7.0, 0.5), (5.6, 0.5), (4.2, 0.6))):
        m.cyl((0, 0, 0.25 + k * 0.5 + h / 2 - 0.25), r, r, h, "marble" if k != 1 else "concrete", seg=24)
    m.cyl((0, 0, 2.1), 2.4, 2.4, 1.2, "white", seg=16)
    m.tube((0, 0, 2.6), (0, 0, 62.0), 0.55, 0.22, "steel", seg=12)
    m.ball((0, 0, 62.3), (0.45, 0.45, 0.45), "gold", seg=10, rings=6)
    flag_malaysia(m, 0.25, 0, 61.4, 9.6, 4.8, thick=0.12)
    return m


# ============================================================================ Pasar Seni (Central Market)
PS_CX = 14.5          # the hall's middle (authoring), from the OSM footprint at yaw 99


def pasar_seni():
    """Central Market: the Art Deco hall in powder blue and white - a long two-storey block with fluted pilasters,
    the stepped central gable with its three tall windows and the clock, ribbed corner towers, a sawtooth roof
    behind."""
    m = K.Mesher()
    cx = PS_CX
    W, D, H = 78.0, 40.0, 12.0
    body = rect(cx, 3.0, W, D)
    prism(m, body, 0, H, "deco_blue", cap_color="concrete")
    band(m, body, 0, 1.0, 0.25, "white")
    band(m, body, H - 1.2, 1.2, 0.35, "white", top=True)
    band(m, body, 4.6, 0.6, 0.25, "white", top=True)
    # fluted white pilasters and tall window bays along the front and the ends
    n = 13
    for i in range(n + 1):
        x = cx - W / 2 + i * W / n
        m.box((x, -17.2, H / 2), (1.0, 0.8, H + 0.6), "white")
        if not lod():
            for dx in (-0.3, 0.0, 0.3):
                m.box((x + dx, -17.65, H / 2), (0.12, 0.1, H - 1.0), "deco_blue")
        if i < n:
            xm = x + W / n / 2
            m.box((xm, -17.1, 2.6), (3.2, 0.3, 3.2), "window_dark")
            m.box((xm, -17.1, 8.4), (3.2, 0.3, 4.6), "glass")
            if not lod():
                for k in range(3):
                    m.box((xm, -17.3, 6.6 + k * 1.6), (3.3, 0.12, 0.14), "white")
    # the stepped central frontispiece: three tall windows, the clock, a crest of setbacks
    fx, fy = cx, -18.5
    for k, (w, h) in enumerate(((22.0, 15.5), (15.0, 18.0), (9.0, 20.5))):
        m.box((fx, fy + k * 0.4, h / 2), (w, 3.4 - k * 0.6, h), "white" if k != 1 else "deco_blue")
    for dx in (-3.6, 0, 3.6):
        m.box((fx + dx, fy - 1.8, 9.0), (2.4, 0.3, 11.0), "glass")
        if not lod():
            for k in range(5):
                m.box((fx + dx, fy - 1.95, 4.5 + k * 2.2), (2.5, 0.1, 0.16), "white")
    m.cyl((fx, fy - 0.9, 17.6), 1.5, 1.5, 0.3, "white", seg=16, rot=(math.pi / 2, 0, 0))
    m.cyl((fx, fy - 1.05, 17.6), 1.2, 1.2, 0.3, "charcoal", seg=16, rot=(math.pi / 2, 0, 0))
    m.box((fx, fy - 1.9, 13.6), (12.0, 0.4, 1.4), "sign_blank")                 # the CENTRAL MARKET board
    m.box((fx, fy - 2.2, 4.0), (10.0, 1.6, 0.4), "white")                       # entrance canopy
    m.box((fx, fy - 1.85, 1.8), (6.0, 0.2, 3.4), "window_dark")
    # ribbed Art Deco corner towers
    for x in (cx - W / 2 + 2.5, cx + W / 2 - 2.5):
        prism(m, rect(x, -14.5, 6.0, 6.0), 0, H + 4.0, "white", cap_color="deco_blue")
        for k in range(3):
            prism(m, rect(x, -14.5, 6.0 - k * 1.4, 6.0 - k * 1.4), H + 4.0 + k * 1.2, H + 5.2 + k * 1.2,
                  "deco_blue" if k % 2 == 0 else "white")
        if not lod():
            for dx in (-1.6, 0, 1.6):
                m.box((x + dx, -17.55, H / 2 + 2), (0.5, 0.2, H + 2.0), "deco_blue")
    # the sawtooth roof lights
    if True:
        for i in range(6):
            x = cx - W / 2 + 8 + i * (W - 16) / 5
            m.prism((x, 4.0, H), (5.0, D - 8, 2.6), "white", rot=(0, 0, math.pi / 2))
    # the covered way (Laluan Kasturi) along the front
    m.box((cx - 10, -20.8, 4.6), (46, 3.6, 0.4), "deco_blue")
    if not lod():
        for i in range(8):
            m.tube((cx - 32 + i * 6.3, -22.4, 0), (cx - 32 + i * 6.3, -22.4, 4.4), 0.15, 0.15, "white", seg=6)
    return m


# ============================================================================ Petaling Street gate + canopy
def petaling_gate():
    """The Chinatown gate over Jalan Petaling: red pillars on white bases, a green glazed-tile roof in two tiers
    with upswept eaves and a gold ridge, the name board and lanterns. Spans 14 m along X."""
    m = K.Mesher()
    span = 13.0
    for s in (1, -1):
        x = s * span / 2
        m.box((x, 0, 0.5), (1.8, 1.8, 1.0), "marble")
        m.cyl((x, 0, 4.2), 0.55, 0.55, 6.6, "bf_red", seg=12)
        m.box((x, 0, 7.6), (1.4, 1.4, 0.4), "gold")
    m.box((0, 0, 7.9), (span + 2.0, 1.1, 1.0), "bf_red")
    m.box((0, 0, 8.6), (span + 1.0, 0.8, 0.5), "gold")
    m.box((0, -0.55, 7.0), (7.0, 0.3, 1.8), "gold")                     # the name board
    m.box((0, -0.72, 7.0), (6.4, 0.1, 1.4), "bf_red")
    for tier, (z, w, d, h) in enumerate(((9.2, span + 4.0, 3.6, 1.5), (11.0, span * 0.6, 2.6, 1.3))):
        m.box((0, 0, z - 0.3), (w - 1.0, d - 1.4, 0.6), "bf_red")
        m.prism((0, 0, z), (w, d, h), "jade_green")
        m.box((0, 0, z + h + 0.1), (w * 0.9, 0.35, 0.35), "gold")
        if not lod():
            for s in (1, -1):
                for t in (1, -1):
                    m.tube((s * (w / 2 - 0.3), t * (d / 2 - 0.2), z + 0.1), (s * (w / 2 + 0.8), t * (d / 2 + 0.4), z + 1.0),
                           0.18, 0.06, "jade_green", seg=5)
                m.ball((s * w * 0.44, 0, z + h + 0.5), (0.35, 0.2, 0.45), "gold", seg=8, rings=5)
    if not lod():
        for x in (-4.5, -1.5, 1.5, 4.5):
            m.ball((x, -0.9, 6.4), (0.45, 0.45, 0.55), "lantern_red", seg=10, rings=7)
            m.cyl((x, -0.9, 6.95), 0.22, 0.22, 0.12, "gold", seg=8)
            m.tube((x, -0.9, 7.0), (x, -0.9, 7.4), 0.03, 0.03, "gold", seg=4)
    return m


def petaling_canopy():
    """One 10 m bay of the Petaling Street canopy: green translucent-look barrel roof on steel portal frames over the
    street (14 m across X; runs along Y). Modules join end to end."""
    m = K.Mesher()
    span, L, z0 = 14.0, 10.0, 9.0
    n = 8 if not lod() else 4
    # the barrel: a shallow arc of panels
    for i in range(n):
        a0, a1 = math.pi * (0.15 + 0.7 * i / n), math.pi * (0.15 + 0.7 * (i + 1) / n)
        p0 = Vector((math.cos(a0) * span / 2 / math.cos(math.pi * 0.15), 0, math.sin(a0) * 2.8))
        p1 = Vector((math.cos(a1) * span / 2 / math.cos(math.pi * 0.15), 0, math.sin(a1) * 2.8))
        mid = (p0 + p1) / 2
        ang = math.atan2(p1.z - p0.z, p1.x - p0.x)
        m.box((mid.x, 0, z0 + mid.z), ((p1 - p0).length + 0.05, L, 0.18), "canopy_green", rot=(0, -ang, 0))
    # the portal frames at each end
    for y in (-L / 2 + 0.2, L / 2 - 0.2):
        for s in (1, -1):
            m.box((s * span * 0.5, y, z0 / 2 + 0.6), (0.35, 0.35, z0 + 1.2), "lamp_green")
        m.box((0, y, z0 + 0.9), (span + 0.4, 0.3, 0.35), "lamp_green")
    for s in (1, -1):
        m.box((s * span * 0.5, 0, z0 + 0.95), (0.3, L, 0.3), "lamp_green")
    return m


KIT_LANDMARKS3 = {
    "env_lm3_petronas": (petronas, None),
    "env_lm3_menara_kl": (menara_kl, None),
    "env_lm3_sultan_abdul_samad": (sultan_abdul_samad, None),
    "env_lm3_dataran_flagpole": (dataran_flagpole, None),
    "env_lm3_pasar_seni": (pasar_seni, None),
    "env_lm3_petaling_gate": (petaling_gate, None),
    "env_lm3_petaling_canopy": (petaling_canopy, None),
}


# ============================================================================ KL Railway Station
def moorish_range(m, x0, x1, y0, y1, h, floors=2, wall="white", trim="cream", chhatris=True, front=True, back=False):
    """A plastered Moorish range: arcaded floors of pointed arches, a crenellated parapet, kiosks along the top."""
    w, d = x1 - x0, y1 - y0
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    r = rect(cx, cy, w, d)
    prism(m, r, 0, h, wall, cap_color="concrete")
    fh = h / floors
    for f in range(floors):
        band(m, r, f * fh + fh - 0.5, 0.5, 0.2, trim, top=True)
        n = max(2, int(round(w / 3.4)))
        for side, yy, rot in ((front, y0, 0.0), (back, y1, math.pi)):
            if not side:
                continue
            for i in range(n):
                x = x0 + (i + 0.5) * w / n
                arch_window(m, x, yy, f * fh + 0.4, w / n * 0.62, fh - 1.1, "window_dark", frame=trim, pointed=True, rot=rot,
                            seg=8 if not lod() else 4)
    if not lod():
        n = int(w / 1.6)
        for i in range(n):
            x = x0 + (i + 0.5) * w / n
            m.box((x, y0 - 0.05, h + 0.45), (0.8, 0.4, 0.9), wall)
            if back:
                m.box((x, y1 + 0.05, h + 0.45), (0.8, 0.4, 0.9), wall)
    if chhatris:
        k = max(1, int(w / 9))
        for i in range(k):
            x = x0 + (i + 0.5) * w / k
            chhatri(m, (x, (y1 - 0.9) if (back and not front) else (y0 + 0.9), h), 0.8, 1.8, post=wall, roof=wall, tip="gold", seg=8)


def minaret(m, x, y, h, r, wall="white", trim="cream", dome_col="white"):
    """A slim Moorish tower: octagonal shaft in stages, a balcony, a chhatri and an onion dome."""
    octa = circle(r, 8, x, y, math.pi / 8)
    prism(m, octa, 0, h, wall)
    for z in (h * 0.35, h * 0.7):
        band(m, octa, z, 0.5, 0.25, trim, top=True)
    band(m, octa, h, 0.5, 0.5, trim, top=True)
    chhatri(m, (x, y, h + 0.5), r * 0.95, r * 1.9, post=wall, roof=dome_col, seg=10)


def kl_railway():
    """The old Kuala Lumpur Railway Station (1910): long white Moorish ranges with arcaded verandahs, rows of little
    domed kiosks (chhatris) along the parapets, slim minaret towers and bigger domed pavilions at the corners; the
    platform roofs between the two ranges. Front (-Y) faces Jalan Sultan Hishamuddin (yaw 287)."""
    m = K.Mesher()
    H = 11.0
    moorish_range(m, -89, 44, -12.2, 3.0, H, floors=2, chhatris=False)
    moorish_range(m, -59, 17.6, -21.0, -12.2, H + 1.5, floors=2)
    for x in (-58, -43, -1.4, 9.7, 18.8):
        minaret(m, x, -22.5 if -59 <= x <= 17.6 else -13.5, H + 7.0, 1.6)
    for x, y in ((-89, -11.5), (44, -11.5), (-89, 2.0), (44, 2.0)):
        prism(m, rect(x, y, 6.0, 6.0), 0, H + 3.0, "white", cap_color="white")
        band(m, rect(x, y, 6.0, 6.0), H + 2.6, 0.5, 0.35, "cream", top=True)
        onion(m, (x, y, H + 3.1), 2.6, "white", seg=12 if not lod() else 8)
        if not lod():
            for k in range(4):
                a = k * math.pi / 2 + math.pi / 4
                chhatri(m, (x + math.cos(a) * 3.1, y + math.sin(a) * 3.1, H + 3.0), 0.55, 1.3, seg=6)
    # the big central porch with its dome
    prism(m, rect(-21, -24.5, 12, 6), 0, H + 4, "white", cap_color="white")
    arch_window(m, -21, -27.6, 0.0, 6.4, 9.5, "window_dark", frame="cream", pointed=True)
    onion(m, (-21, -24.5, H + 4.1), 3.4, "white", seg=14 if not lod() else 8)
    # the platform roofs (two long gabled sheds on posts) and the far range
    for y in (10.5, 23.5):
        m.box((-14, y, 9.0), (118, 12.5, 0.4), "concrete_dark")
        m.prism((-14, y, 9.2), (118, 13.5, 2.2), "zinc_red")
        if not lod():
            for i in range(14):
                for dy in (-5.5, 5.5):
                    m.tube((-72 + i * 8.9, y + dy, 0), (-72 + i * 8.9, y + dy, 9.0), 0.2, 0.2, "steel", seg=6)
    m.box((-14, 17, 0.5), (118, 4, 1.0), "concrete")
    moorish_range(m, -73.8, 48.0, 30.8, 37.0, H - 2.0, floors=2, chhatris=True, front=False, back=True)
    return m


# ============================================================================ Stadium Negara
def stadium_negara():
    """Stadium Negara (1962): the round indoor arena - a drum ringed with slim white fins over a glass band, a broad
    overhanging ring roof and a shallow dome with a lantern at the crown."""
    m = K.Mesher()
    R = 31.0
    lo = lod()
    seg = 40 if not lo else 20
    m.cyl((0, 0, 0.3), R + 3.5, R + 3.5, 0.6, "concrete", seg=seg)
    prism(m, circle(R, seg), 0.6, 9.0, "cream")
    band(m, circle(R, seg), 1.2, 3.0, 0.15, "glass")
    band(m, circle(R, seg), 6.0, 2.2, 0.12, "glass")
    if not lo:
        for i in range(40):
            a = i / 40 * TAU
            m.box((math.cos(a) * (R + 0.6), math.sin(a) * (R + 0.6), 5.0), (0.5, 1.2, 9.0), "white", rot=(0, 0, a))
    loft(m, [(circle(R + 4.5, seg), 9.0), (circle(R + 4.5, seg), 10.0), (circle(R - 2, seg), 12.5)], "white", close_top=False)
    cap(m, circle(R + 4.5, seg), 9.0, "white", down=True)
    dome(m, (0, 0, 12.0), R - 2, "maybank_grey", seg=seg, squash=0.32, rings=6 if not lo else 3)
    m.cyl((0, 0, 12.0 + (R - 2) * 0.32 + 1.0), 4.0, 3.6, 2.4, "white", seg=16)
    m.cyl((0, 0, 12.0 + (R - 2) * 0.32 + 2.6), 4.6, 4.6, 0.6, "white", seg=16)
    for k in range(4):
        a = k * math.pi / 2 - math.pi / 2
        c, s = math.cos(a), math.sin(a)
        m.box((c * (R + 4), s * (R + 4), 4.2), (8.0 if abs(s) > 0.5 else 5.0, 5.0 if abs(s) > 0.5 else 8.0, 0.5), "white")
        m.box((c * (R + 0.3), s * (R + 0.3), 2.0), (5.0 if abs(s) > 0.5 else 0.4, 0.4 if abs(s) > 0.5 else 5.0, 3.6), "window_dark")
    return m


# ============================================================================ Menara Maybank
def maybank():
    """Menara Maybank on Jalan Tun Perak: a white tower on a diamond plan whose two halves rise to different heights
    and are cut off on the slant like the hilt of a keris, with ribbon windows, on a podium."""
    m = K.Mesher()
    H = 159.0                        # 244 m x 0.65
    lo = lod()
    pod = [(0.2, 38.4), (-35.2, 1.5), (-10.6, -22.0), (-19.4, -31.2), (2.3, -51.4), (22.7, -29.9), (13.5, -21.1), (37.1, 3.3)]
    prism(m, pod, 0, 10.0, "maybank_grey", cap_color="concrete")
    band(m, pod, 1.0, 3.4, 0.12, "glass")
    band(m, pod, 9.4, 0.6, 0.4, "white", top=True)
    s = 19.0
    halves = [([(0, s), (-s, 0), (0, -s)], H, H - 22.0, "white"),
              ([(0, s), (0, -s), (s, 0)], H - 18.0, H - 34.0, "maybank_grey")]
    for pts, ztop_hi, ztop_lo, col in halves:
        pts = ccw(moved(pts, 1.0, 2.0))
        prism(m, pts, 10.0, ztop_lo, col, top=False)
        tmp = bmesh.new()
        lo_v = [tmp.verts.new((x, y, ztop_lo)) for x, y in pts]
        far = max(range(3), key=lambda i: abs(pts[i][0] - 1.0))
        hi_v = [tmp.verts.new((x, y, ztop_hi if i == far else ztop_lo + (ztop_hi - ztop_lo) * 0.35)) for i, (x, y) in enumerate(pts)]
        for i in range(3):
            j = (i + 1) % 3
            tmp.faces.new((lo_v[i], lo_v[j], hi_v[j], hi_v[i]))
        tmp.faces.new(hi_v)
        m._merge(tmp, col)
        if not lo:
            z = 14.0
            while z < ztop_lo - 2:
                band(m, pts, z, 1.4, 0.12, "petronas_glass")
                z += 4.2
    m.box((1.0, 2.0, H / 2 - 4), (1.2, 2 * s - 2, H - 30), "white")
    m.box((-6.5, -11.0, H - 34), (8.0, 0.4, 5.0), "crate_yellow", rot=(0, 0, math.pi / 4))
    return m


# ============================================================================ Dayabumi
def dayabumi():
    """Dayabumi: the white tower dressed in a screen of pointed Islamic arches, on an eight-point plan of two
    overlapping squares, with an arched crown, on its podium beside the river."""
    m = K.Mesher()
    H = 102.0                        # 157 m x 0.65
    lo = lod()
    pod = [(-13.7, -29.1), (-0.4, -20.5), (5.1, 11.1), (-9.2, 12.4), (-7.7, 20.3), (-27.6, 52.9), (-23.3, 57.9), (-17.5, 61.2),
           (27.1, -1.1), (23.3, -24.0), (37.5, -44.0), (34.1, -61.9), (24.3, -68.4), (29.1, -76.8), (-20.8, -67.3)]
    prism(m, pod, 0, 9.0, "white", cap_color="concrete")
    band(m, pod, 1.0, 3.2, 0.12, "glass")
    band(m, pod, 8.4, 0.6, 0.4, "sandstone", top=True)
    tx, ty = 8.0, -36.0
    R = 15.0
    plan = []
    for k in range(8):
        a = k * math.pi / 4
        plan.append((tx + math.cos(a) * R, ty + math.sin(a) * R))
        a2 = a + math.pi / 8
        plan.append((tx + math.cos(a2) * R * 0.8, ty + math.sin(a2) * R * 0.8))
    prism(m, plan, 9.0, H, "white", cap_color="concrete")
    if not lo:
        rows = int((H - 14) / 6.5)
        for row in range(rows):
            z = 11.0 + row * 6.5
            for i in range(16):
                (x0, y0), (x1, y1) = plan[i], plan[(i + 1) % 16]
                a = math.atan2(y1 - y0, x1 - x0)
                L = math.hypot(x1 - x0, y1 - y0)
                arch_window(m, (x0 + x1) / 2 + math.sin(a) * 0.05, (y0 + y1) / 2 - math.cos(a) * 0.05, z, L * 0.62, 5.4,
                            "petronas_glass", pointed=True, rot=a, seg=6)
    else:
        band(m, plan, 12, H - 18, 0.1, "petronas_glass")
    crown = scaled(plan, 0.85, tx, ty)
    prism(m, crown, H, H + 7.0, "white", cap_color="white")
    if not lo:
        for i in range(16):
            (x0, y0), (x1, y1) = crown[i], crown[(i + 1) % 16]
            a = math.atan2(y1 - y0, x1 - x0)
            L = math.hypot(x1 - x0, y1 - y0)
            arch_window(m, (x0 + x1) / 2 + math.sin(a) * 0.05, (y0 + y1) / 2 - math.cos(a) * 0.05, H + 0.6, L * 0.6, 5.6,
                        "window_dark", pointed=True, rot=a, seg=6)
    m.cyl((tx, ty, H + 8.0), 4.0, 2.0, 2.0, "white", seg=12)
    m.tube((tx, ty, H + 9), (tx, ty, H + 18), 0.4, 0.1, "steel", seg=6)
    return m


# ============================================================================ Pavilion
PAVILION = [(81.2, -26.1), (67.4, -18.0), (72.9, -12.1), (62.1, -2.4), (49.8, 2.5), (31.3, -17.3), (30.3, -41.8), (13.9, -41.4),
            (14.5, -31.5), (18.6, -21.9), (-9.1, -20.4), (-10.4, -41.7), (-27.7, -41.1), (-26.9, -27.5), (-32.1, -17.4),
            (-57.1, -16.0), (-58.6, -39.5), (-79.0, -42.4), (-130.1, -39.1), (-134.4, -23.6), (-101.5, 1.4), (-94.9, 7.9),
            (-38.8, 47.8), (-34.5, 55.7), (-1.5, 53.6), (25.6, 31.2), (54.5, 55.2), (100.4, 15.3)]


def pavilion_kl():
    """Pavilion Kuala Lumpur on Jalan Bukit Bintang: the long white mall in stacked bands of glass, the curved glass
    atrium front with its tiered canopy, and the crystal fountain in the plaza."""
    m = K.Mesher()
    lo = lod()
    H = 24.0
    body = offset(PAVILION, -1.0)
    prism(m, body, 0, H, "white", cap_color="concrete")
    for z in (4.5, 9.5, 14.5, 19.5):
        band(m, body, z - 2.8, 2.6, 0.12, "glass")
        band(m, body, z, 0.6, 0.35, "white", top=not lo)
    band(m, body, H - 0.8, 0.8, 0.5, "white", top=True)
    ax, ay = 4.5, -24.0
    n = 12 if not lo else 6
    arc = [(ax + math.cos(math.pi + i / n * math.pi) * 13, ay + math.sin(math.pi + i / n * math.pi) * 9) for i in range(n + 1)]
    for i in range(n):
        p0, p1 = arc[i], arc[i + 1]
        mx, my = (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2
        a = math.atan2(p1[1] - p0[1], p1[0] - p0[0])
        m.box((mx, my, H / 2 + 2), (math.hypot(p1[0] - p0[0], p1[1] - p0[1]) + 0.1, 0.4, H + 4), "petronas_glass", rot=(0, 0, a))
        m.box((p0[0], p0[1], H / 2 + 2), (0.5, 0.5, H + 4), "white", rot=(0, 0, a))
    cap(m, arc, H + 4, "white")
    for k in range(3):
        m.box((ax, ay - 10 - k * 1.8, 8.5 - k * 2.0), (24 - k * 5, 4.0, 0.5), "white")
    for x in (-6, 15):
        m.tube((x, -38, 0), (x, -38, 4.5), 0.3, 0.3, "steel", seg=8)
    fx, fy = ax, -46.0
    m.cyl((fx, fy, 0.5), 5.0, 5.2, 1.0, "marble", seg=20)
    m.cyl((fx, fy, 1.02), 4.6, 4.6, 0.06, "tarp_blue", seg=20)
    for k, (r, z) in enumerate(((2.8, 2.4), (1.9, 4.4), (1.1, 6.2))):
        m.cyl((fx, fy, z), r, r * 0.4, 0.9, "glass", seg=14)
        m.tube((fx, fy, z - 1.6 if k else 1.0), (fx, fy, z), 0.35, 0.3, "glass", seg=8)
    m.ball((fx, fy, 7.2), (0.6, 0.6, 0.9), "glass", seg=10, rings=7, smooth=True)
    m.box((ax, ay - 9.6, H + 1.5), (14, 0.4, 2.2), "sign_blank")
    return m


KIT_LANDMARKS3.update({
    "env_lm3_kl_railway": (kl_railway, None),
    "env_lm3_stadium_negara": (stadium_negara, None),
    "env_lm3_maybank": (maybank, None),
    "env_lm3_dayabumi": (dayabumi, None),
    "env_lm3_pavilion": (pavilion_kl, None),
})
