"""Hit & Run-style Malaysian cars: Myvi, classic Saga, Kancil, Alphard, Hilux double cab,
kapcai (+ teksi and police variants on the same bodies).

Bodies are smooth lofts: stations along the car's length give a rounded superellipse
cross-section (half width, bottom, widest line, top, roundness), interpolated with monotone
cubics so the shell has no kinks, then smooth-shaded. Wheel arches are boolean-cut out of
the shell. Windows, lamps, door skins, trims and decals are *conformal patches*: the same
surface function sampled over a sub-rectangle and pushed out along its normal, so they hug
the body exactly and have crisp edges.

Parts (all names are what Unity looks for):
  Body            everything that rolls/pitches on the suspension (pivot at axle height)
    Door_FL/FR/RL/RR  hinged at their front edge (children of Body)
    SteeringWheel     pivot on the hub; child empty SteeringAxis marks the column axis
    HeadLights / BrakeLights / ReverseLights   lamp lenses, lit by the game
    Antenna, Ornament  springy bits (pivot at their base / hanging point)
    Seat_Driver       empty: where the driver's hips go
    FX_Exhaust        empty: tail pipe exit
  Wheel_FL/FR/RL/RR   pivots on the hubs (children of the root, like before)
  COL_<id>            collision proxy
Front = -Y while modelling; right-hand drive, so the driver sits on the right (-X).
"""
import math
import bpy
import bmesh
from mathutils import Vector, Matrix

import kl_core as K

AX = (0, math.pi / 2, 0)


# ============================================================================== maths
def _monotone(xs, ys):
    """Fritsch-Carlson monotone cubic through (xs, ys): smooth, never overshoots."""
    n = len(xs)
    if n == 1:
        return lambda x: ys[0]
    d = [(ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]) for i in range(n - 1)]
    m = [d[0]] + [0.0 if d[i - 1] * d[i] <= 0 else (d[i - 1] + d[i]) / 2 for i in range(1, n - 1)] + [d[-1]]
    for i in range(n - 1):
        if d[i] == 0:
            m[i] = m[i + 1] = 0.0
        else:
            a, b = m[i] / d[i], m[i + 1] / d[i]
            s = a * a + b * b
            if s > 9:
                t = 3 / math.sqrt(s)
                m[i], m[i + 1] = t * a * d[i], t * b * d[i]

    def f(x):
        if x <= xs[0]:
            return ys[0]
        if x >= xs[-1]:
            return ys[-1]
        i = max(0, min(n - 2, next(k for k in range(n - 1) if x <= xs[k + 1])))
        h = xs[i + 1] - xs[i]
        t = (x - xs[i]) / h
        t2, t3 = t * t, t * t * t
        return ((2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h * m[i] +
                (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h * m[i + 1])
    return f


def spow(v, e):
    return math.copysign(abs(v) ** e, v)


class Shell:
    """Rounded loft. stations: list of (y, w, zb, zm, zt, n_side, n_top, n_bot) front to back.
    s in [-1, 1] runs bottom centre -> widest line (s=0) -> top centre; side -1 = right (-X)."""

    def __init__(self, stations, cx=0.0):
        st = sorted(stations, key=lambda r: r[0])
        self.y0, self.y1 = st[0][0], st[-1][0]
        ys = [r[0] for r in st]
        self.f = [_monotone(ys, [r[k] for r in st]) for k in range(1, 8)]
        self.cx = cx

    def params(self, y):
        return [f(y) for f in self.f]

    def point(self, y, s, side):
        w, zb, zm, zt, ns, nt, nb = self.params(y)
        th = s * math.pi / 2
        c, sn = max(0.0, math.cos(th)), math.sin(th)
        x = side * max(w, 1e-4) * c ** (2.0 / ns)
        if sn >= 0:
            z = zm + (zt - zm) * sn ** (2.0 / nt)
        else:
            z = zm - (zm - zb) * (-sn) ** (2.0 / nb)
        return Vector((self.cx + x, y, z))

    def normal(self, y, s, side):
        e = 1e-3
        dy = self.point(min(self.y1, y + e), s, side) - self.point(max(self.y0, y - e), s, side)
        ds = self.point(y, min(1.0, s + e), side) - self.point(y, max(-1.0, s - e), side)
        n = dy.cross(ds) * side
        if n.length < 1e-9:
            w, zb, zm, zt = self.params(y)[:4]
            n = self.point(y, s, side) - Vector((self.cx, y, zm))
        return n.normalized()


# ============================================================================== raw faces
def _face(m, verts, color, smooth):
    try:
        f = m.bm.faces.new(verts)
    except ValueError:
        return None
    f.smooth = smooth
    u = K.pal_uv(color)
    for lp in f.loops:
        lp[m.uv].uv = u
    return f


def _lin(a, b, n):
    return [a + (b - a) * i / n for i in range(n + 1)]


def shell_mesh(m, sh, ny=36, ns=12, color_fn=None, color="car_paint", smooth=True, y_breaks=(), s_breaks=()):
    """Closed smooth shell. y_breaks / s_breaks are extra sample lines so colour regions
    (sills, bumpers, two-tone) get straight edges."""
    D = K.LOD_DETAIL
    ny, ns = max(8, int(ny * D)), max(4, int(ns * D))
    ys = sorted(set([round(v, 5) for v in _lin(sh.y0, sh.y1, ny)] + [b for b in y_breaks if sh.y0 < b < sh.y1]))
    ss = sorted(set([round(v, 5) for v in _lin(-1.0, 1.0, ns)] + [b for b in s_breaks if -1 < b < 1]))
    rings = []
    for y in ys:
        ring = [m.bm.verts.new(sh.point(y, s, -1)) for s in ss]
        ring += [m.bm.verts.new(sh.point(y, s, 1)) for s in reversed(ss[1:-1])]
        rings.append(ring)
    R = len(rings[0])

    def col(pts):
        if not color_fn:
            return color
        c = sum((v.co for v in pts), Vector()) / len(pts)
        return color_fn(c) or color

    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        for j in range(R):
            q = [a[j], a[(j + 1) % R], b[(j + 1) % R], b[j]]
            _face(m, q, col(q), smooth)
    for ring, rev in ((rings[0], True), (rings[-1], False)):
        c = m.bm.verts.new(sum((v.co for v in ring), Vector()) / R)
        for j in range(R):
            tri = [ring[j], ring[(j + 1) % R], c]
            _face(m, list(reversed(tri)) if rev else tri, col(tri), smooth)
    return ys, ss


def patch(m, sh, y0, y1, s0, s1, color, side, off=0.006, ny=6, ns=4, thick=0.0, smooth=True, inner=None):
    """Conformal patch on shell sh. y0 / y1 may be functions of s (slanted pillars).
    thick > 0 closes it into a slab (door skins)."""
    D = K.LOD_DETAIL
    # dense enough that the flat facets never dip under the (finer) shell they sit on
    ny, ns = max(1, int(round(ny * 2.2 * D))), max(1, int(round(ns * 2.2 * D)))
    fy0 = y0 if callable(y0) else (lambda s, v=y0: v)
    fy1 = y1 if callable(y1) else (lambda s, v=y1: v)
    if m.cull > 0:
        mid = (s0 + s1) / 2
        a = sh.point(fy0(mid), mid, side)
        b = sh.point(fy1(mid), mid, side)
        c = sh.point((fy0(s0) + fy1(s0)) / 2, s0, side)
        d = sh.point((fy0(s1) + fy1(s1)) / 2, s1, side)
        if min((a - b).length, (c - d).length) < m.cull:
            return
    grid_o, grid_i = [], []
    for i in range(ns + 1):
        s = s0 + (s1 - s0) * i / ns
        rowo, rowi = [], []
        a, b = fy0(s), fy1(s)
        for j in range(ny + 1):
            y = a + (b - a) * j / ny
            p = sh.point(y, s, side)
            n = sh.normal(y, s, side)
            rowo.append(m.bm.verts.new(p + n * off))
            if thick > 0:
                rowi.append(m.bm.verts.new(p + n * (off - thick)))
        grid_o.append(rowo)
        grid_i.append(rowi)
    flip = side < 0

    def quad(q):
        return list(reversed(q)) if flip else q

    for i in range(ns):
        for j in range(ny):
            q = [grid_o[i][j], grid_o[i][j + 1], grid_o[i + 1][j + 1], grid_o[i + 1][j]]
            _face(m, quad(q), color, smooth)
            if thick > 0:
                qi = [grid_i[i][j], grid_i[i + 1][j], grid_i[i + 1][j + 1], grid_i[i][j + 1]]
                _face(m, quad(qi), inner or color, smooth)
    if thick > 0:   # rim around the slab
        border = ([(0, j) for j in range(ny + 1)] + [(i, ny) for i in range(1, ns + 1)] +
                  [(ns, j) for j in range(ny - 1, -1, -1)] + [(i, 0) for i in range(ns - 1, 0, -1)])
        for k in range(len(border)):
            (i0, j0), (i1, j1) = border[k], border[(k + 1) % len(border)]
            q = [grid_o[i0][j0], grid_i[i0][j0], grid_i[i1][j1], grid_o[i1][j1]]
            _face(m, quad(q), color, False)


def revolve(m, center, profile, color_fn, seg=16, side=1, smooth=True, closed=True, rot=None, double=False):
    """Surface of revolution about the X axis. profile: [(radius, axial)], axial > 0 points
    outward (toward `side`). color_fn(i) -> colour of profile segment i."""
    seg = max(6, int(seg * K.LOD_DETAIL))
    c = Vector(center)
    R = rot or Matrix.Identity(3)
    rings = []
    for (r, a) in profile:
        ring = []
        for k in range(seg):
            t = k / seg * math.tau
            p = Vector((a * side, r * math.cos(t), r * math.sin(t)))
            ring.append(m.bm.verts.new(c + R @ p))
        rings.append(ring)
    n = len(rings) if closed else len(rings) - 1
    for i in range(n):
        A, B = rings[i], rings[(i + 1) % len(rings)]
        colr = color_fn(i) if callable(color_fn) else color_fn
        for k in range(seg):
            q = [A[k], A[(k + 1) % seg], B[(k + 1) % seg], B[k]]
            _face(m, q if side > 0 else list(reversed(q)), colr, smooth)
            if double:
                _face(m, list(reversed(q)) if side > 0 else q, colr, smooth)


def disc(m, center, r, color, side, axial=0.0, seg=16):
    """Flat disc facing `side` along X."""
    seg = max(6, int(seg * K.LOD_DETAIL))
    c = Vector(center) + Vector((axial * side, 0, 0))
    vs = [m.bm.verts.new(c + Vector((0, r * math.cos(k / seg * math.tau), r * math.sin(k / seg * math.tau))))
          for k in range(seg)]
    mid = m.bm.verts.new(c)
    for k in range(seg):
        tri = [vs[k], vs[(k + 1) % seg], mid]
        _face(m, tri if side > 0 else list(reversed(tri)), color, False)


def rbox(m, loc, size, color, rot=(0, 0, 0), r=0.3, segs=2):
    """Rounded box (bevelled with several segments, smooth shaded)."""
    if m._small(*size):
        return
    tmp = bmesh.new()
    bmesh.ops.create_cube(tmp, size=1.0)
    bmesh.ops.transform(tmp, matrix=Matrix.Diagonal((size[0], size[1], size[2], 1)), verts=tmp.verts)
    off = min(size) * 0.5 * r
    if off > 1e-4 and m.cull <= 0:
        bmesh.ops.bevel(tmp, geom=list(tmp.edges), offset=off, segments=segs, affect='EDGES', clamp_overlap=True,
                        profile=0.5)
    bmesh.ops.transform(tmp, matrix=m.M(loc, rot=rot), verts=tmp.verts)
    m._merge(tmp, color, smooth=True)


def ellip(m, loc, radii, color, seg=12, rings=8, rot=(0, 0, 0)):
    m.ball(loc, radii, color, seg=seg, rings=rings, rot=rot, smooth=True)


def merge_bm(m, bm, sharp_deg=38.0):
    """Copy a bmesh (with a UV layer) into mesher m, marking hard creases sharp."""
    uv = bm.loops.layers.uv.active or (bm.loops.layers.uv[0] if len(bm.loops.layers.uv) else None)
    vmap = {v: m.bm.verts.new(v.co) for v in bm.verts}
    for f in bm.faces:
        try:
            nf = m.bm.faces.new([vmap[v] for v in f.verts])
        except ValueError:
            continue
        nf.smooth = f.smooth
        for lo, ln in zip(f.loops, nf.loops):
            ln[m.uv].uv = lo[uv].uv if uv else (0.5, 0.5)
    m.bm.edges.ensure_lookup_table()
    lim = math.radians(sharp_deg)
    for e in m.bm.edges:
        if len(e.link_faces) == 2 and e.link_faces[0].normal.angle(e.link_faces[1].normal, 0) > lim:
            e.smooth = False


_TMP_MAT = None


def _tmp_mat():
    global _TMP_MAT
    if _TMP_MAT is None or _TMP_MAT.name not in bpy.data.materials:
        _TMP_MAT = bpy.data.materials.new("__kl_tmp")
    return _TMP_MAT


def cut_arches(sm, arches):
    """Boolean the wheel arches out of shell mesher sm. arches: [(x_side, y, z, r, depth)].
    Returns a bmesh (caller merges it). The cut surfaces come out 'well_black'."""
    ob, _, _ = sm.to_object("__kl_shell", _tmp_mat())
    cutters = []
    for (sx, y, z, r, depth) in arches:
        cm = K.Mesher()
        # cylinder along X sitting on the body side, reaching `depth` inward
        x_out = sx * 2.0
        x_in = sx * (abs(sx) - depth) / max(abs(sx), 1e-6) if abs(sx) > depth else 0.0
        cx = (x_out + x_in) / 2
        tmp = bmesh.new()
        bmesh.ops.create_cone(tmp, cap_ends=True, cap_tris=False, segments=max(12, int(28 * K.LOD_DETAIL)),
                              radius1=r, radius2=r, depth=abs(x_out - x_in))
        bmesh.ops.transform(tmp, matrix=Matrix.Translation((cx, y, z)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'),
                            verts=tmp.verts)
        cm._merge(tmp, "well_black", smooth=True)
        co, _, _ = cm.to_object("__kl_cut", _tmp_mat())
        co.hide_render = True
        cutters.append(co)
    for co in cutters:
        mod = ob.modifiers.new("cut", 'BOOLEAN')
        mod.operation = 'DIFFERENCE'
        mod.solver = 'EXACT'
        mod.object = co
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    for o in [ob] + cutters:
        d = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(d)
    bpy.data.meshes.remove(me)
    return bm


# ============================================================================== hollow cabin
# While a car is being built, doors and windows register the openings they need: build_car then
# deletes those faces from the body / cabin shells (so you can see - and climb - inside), lines
# the inside with interior-coloured faces, and puts all the glass into a separate "Glass" part
# the game draws see-through.
_BUILD = {"body_holes": [], "cab_holes": [], "glass": None}


def shell_s(sh, y, z):
    """Inverse of Shell.point for the height: the s parameter at height z on station y."""
    w, zb, zm, zt, ns, nt, nb = sh.params(y)
    if z >= zm:
        v = min(1.0, max(0.0, (z - zm) / max(1e-6, zt - zm)))
        return math.asin(v ** (nt / 2.0)) * 2 / math.pi
    v = min(1.0, max(0.0, (zm - z) / max(1e-6, zm - zb)))
    return -math.asin(v ** (nb / 2.0)) * 2 / math.pi


def in_hole(sh, c, hole, margin=0.0):
    f0, f1, s0, s1, side = hole
    if c.x * side < -0.02:
        return False
    q = shell_s(sh, c.y, c.z)
    if not (s0 - margin <= q <= s1 + margin):
        return False
    return f0(q) - margin <= c.y <= f1(q) + margin


def open_up(bm, keep, interior=None, interior_fn=None):
    """Delete faces where keep(centre, normal) is False; then (optionally) add a flipped,
    interior-coloured copy of the remaining faces that interior_fn(centre) selects - the inside
    of the sheet metal, seen through the openings."""
    import bmesh as bm_
    bm.faces.ensure_lookup_table()
    dead = [f for f in bm.faces if not keep(f.calc_center_median(), f.normal)]
    bm_.ops.delete(bm, geom=dead, context='FACES')
    if interior:
        uv = bm.loops.layers.uv.active or bm.loops.layers.uv[0]
        u = K.pal_uv(interior)
        for f in list(bm.faces):
            if interior_fn and not interior_fn(f.calc_center_median()):
                continue
            # its own vertices (a face over the same verts would be the same face to bmesh)
            nf = bm.faces.new([bm.verts.new(v.co) for v in reversed(f.verts)])
            nf.smooth = f.smooth
            for lp in nf.loops:
                lp[uv].uv = u
    return bm


# ============================================================================== shared bits
def wheel(m, c, r, w, side, style="alloy", rim_frac=0.64, spokes=5, knobbly=False):
    """Detailed wheel at hub c, axis X, outer face toward `side`.
    style: alloy (spoked silver), steel (painted steel + chrome hubcap), offroad (dark 6-spoke)."""
    c = Vector(c)
    ri = r * rim_frac
    b = 0.022
    # tyre: rounded shoulders, bulging sidewall
    prof = [(ri * 0.98, -w / 2), (r * 0.82, -w / 2 - 0.012), (r - b, -w / 2 + 0.004), (r, -w / 2 + b),
            (r, w / 2 - b), (r - b, w / 2 - 0.004), (r * 0.82, w / 2 + 0.012), (ri * 0.98, w / 2)]
    cols = ["tyre_wall", "tyre", "tyre", "tyre", "tyre", "tyre_wall", "tyre_wall", "rim_dark"]
    revolve(m, c, prof, lambda i: cols[i], seg=22, side=side)
    if knobbly:  # chunky off-road tread blocks
        n = max(10, int(26 * K.LOD_DETAIL))
        for k in range(n):
            a = k / n * math.tau
            for lane in (-1, 1):
                off = lane * w * 0.24 + (0.03 if k % 2 else -0.03)
                p = c + Vector((off * side, (r + 0.012) * math.cos(a), (r + 0.012) * math.sin(a)))
                m.box(p, (w * 0.34, 0.07, 0.035), "tyre", rot=(a + math.pi / 2, 0, 0))
    face_x = w / 2 - 0.02
    if style in ("steel", "steel_paint"):
        cap = "car_paint" if style == "steel_paint" else "chrome"
        # painted steel wheel with a big chrome hubcap and vent slots
        revolve(m, c, [(ri, face_x - 0.03), (ri * 0.97, face_x), (ri * 0.8, face_x + 0.006)],
                lambda i: "rim_silver", seg=20, side=side, closed=False)
        if cap == "chrome":
            revolve(m, c, [(ri * 0.8, face_x + 0.006), (ri * 0.72, face_x + 0.03), (ri * 0.45, face_x + 0.045),
                           (ri * 0.18, face_x + 0.05)], lambda i: "chrome", seg=20, side=side, closed=False)
            disc(m, c, ri * 0.18, "chrome", side, axial=face_x + 0.05)
        else:   # plain steel wheel with vent holes and a small painted centre cap
            revolve(m, c, [(ri * 0.8, face_x + 0.006), (ri * 0.5, face_x + 0.02), (ri * 0.36, face_x + 0.03)],
                    lambda i: "rim_silver", seg=20, side=side, closed=False)
            revolve(m, c, [(ri * 0.36, face_x + 0.03), (ri * 0.3, face_x + 0.06), (ri * 0.12, face_x + 0.07)],
                    lambda i: cap, seg=16, side=side, closed=False)
            disc(m, c, ri * 0.12, cap, side, axial=face_x + 0.07)
        for k in range(6):
            a = k / 6 * math.tau
            m.box(c + Vector(((face_x + 0.038) * side, ri * 0.6 * math.cos(a), ri * 0.6 * math.sin(a))),
                  (0.006, 0.026, 0.012), "rim_dark", rot=(a, 0, 0))
    else:
        rim_col = "rim_dark" if style in ("offroad", "alloy_dark") else "rim_silver"
        # barrel + lip
        revolve(m, c, [(ri, -w * 0.4), (ri, face_x - 0.01), (ri * 1.04, face_x), (ri * 0.96, face_x + 0.008)],
                lambda i: ["rim_dark", rim_col, rim_col][min(i, 2)], seg=20, side=side, closed=False, double=True)
        # brake disc + caliper behind the spokes
        m.cyl(c + Vector((side * (face_x - 0.07), 0, 0)), ri * 0.72, ri * 0.72, 0.018, "disc_steel", seg=16, rot=AX)
        m.box(c + Vector((side * (face_x - 0.055), ri * 0.35, ri * 0.55)), (0.04, ri * 0.4, ri * 0.28), "caliper_red",
              rot=(-0.55, 0, 0))
        # dished spokes
        n = spokes
        for k in range(n):
            a = k / n * math.tau + 0.3
            dirv = Vector((0, math.cos(a), math.sin(a)))
            p0 = c + Vector((side * (face_x + 0.012), 0, 0)) + dirv * ri * 0.2
            p1 = c + Vector((side * (face_x - 0.006), 0, 0)) + dirv * ri * 0.97
            wid = ri * (0.26 if style == "offroad" else 0.2)
            mid = (p0 + p1) / 2
            ln = (p1 - p0).length
            m.box(mid, (0.022, ln, wid), rim_col, rot=(a - math.pi / 2 + math.pi / 2, 0, 0))
        m.cyl(c + Vector((side * (face_x + 0.012), 0, 0)), ri * 0.24, ri * 0.2, 0.03, rim_col, seg=14, rot=AX)
        disc(m, c, ri * 0.14, "chrome", side, axial=face_x + 0.028)
        for k in range(5):   # lug nuts
            a = k / 5 * math.tau
            m.cyl(c + Vector((side * (face_x + 0.03), ri * 0.17 * math.cos(a), ri * 0.17 * math.sin(a))),
                  0.008, 0.008, 0.012, "chrome", seg=6, rot=AX)


def steering_wheel(parts, hub, radius, tilt, parent="Body"):
    """Steering wheel (pivot on the hub) plus an axis marker. tilt: column angle from
    horizontal (radians); the wheel faces the driver (+Y)."""
    R = Matrix.Rotation(-tilt, 3, 'X')           # rim plane leans back with the column
    axis = (R @ Vector((0, 1, 0))).normalized()   # toward the driver
    sw = K.Mesher(2.0)
    hub = Vector(hub)
    n = 18
    for i in range(n):
        a0, a1 = i / n * math.tau, (i + 1) / n * math.tau
        p0 = hub + R @ Vector((math.cos(a0) * radius, 0, math.sin(a0) * radius))
        p1 = hub + R @ Vector((math.cos(a1) * radius, 0, math.sin(a1) * radius))
        sw.tube(p0, p1, 0.018, 0.018, "trim_black", seg=6)
    for a in (math.pi * 0.0, math.pi, -math.pi / 2):
        p = hub + R @ Vector((math.cos(a) * radius * 0.92, 0, math.sin(a) * radius * 0.92))
        sw.tube(hub, p, 0.014, 0.012, "dash_grey", seg=5)
    sw.cyl(hub, 0.055, 0.05, 0.05, "trim_black", seg=12, rot=(math.pi / 2 - tilt, 0, 0))
    parts.append(("SteeringWheel", sw, tuple(hub), parent))
    parts.append(("SteeringAxis", None, tuple(hub + axis * 0.1), "SteeringWheel"))
    col = K.Mesher()
    col.tube(hub + axis * 0.02, hub - axis * 0.28, 0.03, 0.035, "trim_black", seg=8)   # column shroud (on Body)
    return col


def seat(m, x, y, z, w=0.46, back=0.55, color="seat_grey", recline=0.22):
    rbox(m, (x, y, z), (w, 0.48, 0.12), color, r=0.5)
    rbox(m, (x, y + 0.24, z + back / 2), (w, 0.12, back), color, rot=(-recline, 0, 0), r=0.5)
    rbox(m, (x, y + 0.3, z + back + 0.08), (w * 0.52, 0.1, 0.16), color, rot=(-recline, 0, 0), r=0.6)


def mirror_arm(m, p, side, color="car_paint", size=1.0):
    """Wing mirror: small pod on a stub, glass facing back."""
    p = Vector(p)
    m.tube(p, p + Vector((side * 0.06 * size, 0.01, 0.02)), 0.018, 0.015, "trim_black", seg=6)
    c = p + Vector((side * 0.12 * size, 0.02, 0.04))
    ellip(m, c, (0.075 * size, 0.045 * size, 0.055 * size), color, seg=10, rings=6)
    m.box(c + Vector((0, 0.038 * size, 0)), (0.11 * size, 0.012, 0.075 * size), "car_glass")


def plate(m, c, w=0.44, h=0.12, facing=-1):
    """Malaysian plate: black with white characters (blocks, no real text)."""
    c = Vector(c)
    m.box(c, (w, 0.012, h), "plate_black")
    xs = [-0.15, -0.1, -0.05, 0.03, 0.07, 0.11, 0.15]
    for x in xs:
        m.box(c + Vector((x * w / 0.44, facing * 0.008, 0)), (0.022 * w / 0.44, 0.006, h * 0.55), "plate_white")


def ornament(parts, hang, parent="Body", kind="tasbih"):
    """Something swinging from the rear-view mirror (pivot at the hanging point)."""
    o = K.Mesher(2.0)
    hang = Vector(hang)
    if kind == "tasbih":
        for k in range(10):
            a = k / 10 * math.tau
            p = hang + Vector((0.035 * math.sin(a), 0, -0.07 - 0.07 * (1 - math.cos(a)) * 0.5))
            o.ball(p, (0.011, 0.011, 0.011), "tasbih_brown", seg=6, rings=4)
        o.ball(hang + Vector((0, 0, -0.16)), (0.012, 0.012, 0.022), "gold", seg=6, rings=4)
    else:   # little pine-tree air freshener
        o.tube(hang, hang + Vector((0, 0, -0.06)), 0.002, 0.002, "white", seg=4)
        o.prism(hang + Vector((0, 0, -0.16)), (0.06, 0.006, 0.1), "fresh_green", rot=(math.pi / 2, 0, 0))
    parts.append(("Ornament", o, tuple(hang), parent))


def antenna(parts, base, length=0.55, parent="Body", lean=0.35):
    a = K.Mesher(2.0)
    base = Vector(base)
    tip = base + Vector((0, math.sin(lean) * length, math.cos(lean) * length))
    a.cyl(base + Vector((0, 0, 0.01)), 0.018, 0.014, 0.03, "trim_black", seg=8)
    a.tube(base, tip, 0.005, 0.003, "chrome", seg=5)
    a.ball(tip, (0.009, 0.009, 0.009), "trim_black", seg=6, rings=4)
    parts.append(("Antenna", a, tuple(base), parent))


# ============================================================================== generic car
class CarSpec:
    """Numbers that describe a car. All lengths in metres, front = -Y."""

    def __init__(self, **kw):
        self.__dict__.update(kw)


def build_car(spec, body_fn):
    """Common assembly: body shell (+ arches), greenhouse, doors, wheels, interior, lamps.
    body_fn(ctx) adds the model-specific details and returns nothing."""
    s = spec
    parts = []
    body = K.Mesher(2.0)
    shell = Shell(s.stations)
    cab = Shell(s.cab_stations)
    _BUILD["body_holes"], _BUILD["cab_holes"] = [], []
    _BUILD["glass"] = K.Mesher(2.0)

    # details first: doors and windows register the openings the shells need
    ctx = dict(parts=parts, body=body, shell=shell, cab=cab, spec=s)
    _BUILD["belt_color"] = "car_paint" if s.__dict__.get("belt_s") is not None else None
    col_extra = body_fn(ctx)

    def in_cab_footprint(c):
        if not (cab.y0 + 0.03 < c.y < cab.y1 - 0.03):
            return False
        return abs(c.x) < cab.params(c.y)[0] * 0.97

    # ---- body shell with arches cut out, door openings and the cabin floor opened up
    sm = K.Mesher(2.0)
    shell_mesh(sm, shell, ny=s.__dict__.get("ny", 40), ns=14, color_fn=s.body_color, y_breaks=s.y_breaks,
               s_breaks=s.s_breaks)
    arches = []
    for (x, y) in ((s.track / 2, s.fy), (-s.track / 2, s.fy), (s.track / 2, s.ry), (-s.track / 2, s.ry)):
        arches.append((math.copysign(s.width / 2 + 0.05, x), y, s.wr + s.arch_lift, s.wr + s.arch_gap, 0.42))
    bm = cut_arches(sm, arches)

    def keep_body(c, n):
        if any(in_hole(shell, c, h) for h in _BUILD["body_holes"]):
            return False
        # the top of the tub under the cabin: open, so the cabin is one hollow space
        if n.z > 0.35 and in_cab_footprint(c) and c.z > shell.params(c.y)[2]:
            return False
        return True
    open_up(bm, keep_body, "interior",
            lambda c: cab.y0 - 0.15 < c.y < cab.y1 + 0.15 and c.z > s.floor_z)
    merge_bm(body, bm)

    # ---- greenhouse (cabin): bottom half removed (it sat inside the body), windows cut out
    cm = K.Mesher(2.0)
    cab_fn = s.cab_color
    if s.__dict__.get("belt_s") is not None:      # body colour on the cabin band below the window line
        cab_fn = lambda c, f=s.cab_color, b=s.belt_s: "car_paint" if shell_s(cab, c.y, c.z) < b else f(c)
    shell_mesh(cm, cab, ny=26, ns=10, color_fn=cab_fn, y_breaks=s.cab_breaks)

    def keep_cab(c, n):
        if c.z < cab.params(c.y)[2] + 0.004:
            return False
        return not any(in_hole(cab, c, h) for h in _BUILD["cab_holes"])
    open_up(cm.bm, keep_cab, "interior")
    merge_bm(body, cm.bm)
    parts.append(("Glass", _BUILD["glass"], None, "Body"))

    # ---- interior: seats, dash, column, driver marker
    iz = s.floor_z
    # front seats sit low (cartoon heads are big: keep them clear of the roof)
    for x in (-s.seat_x, s.seat_x):
        seat(body, x, s.seat_y, iz + 0.2, color=s.__dict__.get("seat_color", "seat_grey"))
    if s.__dict__.get("rear_seat", True):
        rbox(body, (0, s.seat_y + 0.85, iz + 0.28), (s.width - 0.3, 0.46, 0.14), s.__dict__.get("seat_color", "seat_grey"),
             r=0.5)
        rbox(body, (0, s.seat_y + 1.08, iz + 0.6), (s.width - 0.3, 0.12, 0.52), s.__dict__.get("seat_color", "seat_grey"),
             rot=(-0.2, 0, 0), r=0.5)
    rbox(body, (0, s.dash_y, s.dash_z), (s.width - 0.22, 0.34, 0.2), "dash_grey", r=0.6)
    rbox(body, (-s.seat_x, s.dash_y + 0.1, s.dash_z + 0.12), (0.3, 0.1, 0.1), "trim_black", r=0.5)   # binnacle
    body.box((0, s.seat_y - 0.2, iz + 0.12), (s.width - 0.2, 1.6, 0.04), "carpet")
    hub = Vector((-s.seat_x, s.dash_y + 0.2, s.dash_z + 0.16))
    colm = steering_wheel(parts, hub, 0.17, 0.42)
    merge_bm(body, _bm_of(colm))
    parts.append(("Seat_Driver", None, (-s.seat_x, s.seat_y - 0.02, iz + 0.27), "Body"))

    # ---- wheels
    for name, x, y in (("Wheel_FL", s.track / 2, s.fy), ("Wheel_FR", -s.track / 2, s.fy),
                       ("Wheel_RL", s.track / 2, s.ry), ("Wheel_RR", -s.track / 2, s.ry)):
        wm = K.Mesher(2.0)
        wheel(wm, (x, y, s.wr), s.wr, s.ww, 1 if x > 0 else -1, style=s.wheel_style,
              spokes=s.__dict__.get("spokes", 5), knobbly=s.__dict__.get("knobbly", False))
        parts.append((name, wm, (x, y, s.wr), None))

    # body first (pivot at axle height, centre) so its children can attach
    parts.insert(0, ("Body", body, (0, (s.fy + s.ry) / 2, s.wr + 0.1), None))
    col = K.Mesher()
    L = shell.y1 - shell.y0
    col.box((0, (shell.y0 + shell.y1) / 2, (s.floor_z + s.roof_z) / 2 + 0.05), (s.width, L, s.roof_z - s.floor_z), "rubber")
    parts.append((f"COL_{s.id}", col, None, "__collision__"))
    return parts


def _bm_of(m):
    """Take a mesher's bmesh (for merging into another mesher)."""
    bm = m.bm
    return bm


def door(ctx, name, side, y0, y1, zsill_s, zbelt_s, win_top_s, frame=True, glass_front=None, glass_rear=None,
         handle=True, color="car_paint", hinge_front=True, sliding=False):
    """Door skin cut from the body shell (y0..y1, s from sill to belt) plus the window above the
    belt on the cabin shell. Parent = Body; pivot at the hinge (front edge, belt height).
    The body underneath is painted dark in the door opening, so the skin's inset edges leave
    a panel gap and an open door shows a dark doorway."""
    sh, cab, s = ctx["shell"], ctx["cab"], ctx["spec"]
    d = K.Mesher(2.0)
    g = 0.012
    # the door skin runs from the sill right up to where the cabin starts (no slit under the glass)
    ztop = max(shell_s(sh, y, cab.params(y)[2] + 0.02) for y in (y0, (y0 + y1) / 2, y1))
    zbelt_s = max(zbelt_s, min(0.98, ztop))
    patch(d, sh, y0 + g, y1 - g, zsill_s, zbelt_s, color, side, off=0.008, ny=5, ns=5, thick=0.035, inner="interior")
    gf = glass_front or (lambda s_: y0 + 0.03)
    gr = glass_rear or (lambda s_: y1 - 0.03)
    if frame:
        frame_ring(d, cab, gf, gr, 0.04, win_top_s, side, 0.025)
    # the window glass moves with the door but is drawn see-through (its own part)
    gm = K.Mesher(2.0)
    patch(gm, cab, gf, gr, 0.04, win_top_s, "car_glass", side, off=0.016, ny=3, ns=3)
    # openings: the doorway in the body, the window frame's area in the cabin
    _BUILD["body_holes"].append((lambda q, v=y0: v, lambda q, v=y1: v, zsill_s + 0.02, zbelt_s + 0.01, side))
    _BUILD["cab_holes"].append((lambda q: gf(q) - 0.025, lambda q: gr(q) + 0.025, -1.0, win_top_s + 0.05, side))
    if handle:
        hp = sh.point(y1 - 0.14, zbelt_s - 0.18, side)
        hn = sh.normal(y1 - 0.14, zbelt_s - 0.18, side)
        d.box(hp + hn * 0.016, (0.02, 0.12, 0.03), "chrome" if s.__dict__.get("chrome_handles") else "trim_black")
    hp = sh.point(y0 + 0.02, zbelt_s, side)
    ctx["parts"].append((name, d, tuple(hp), "Body"))
    ctx["parts"].append((name + "_Glass", gm, tuple(hp), name))
    # B-pillar behind a front door: a painted post from the sill up into the roof (the closed
    # doors cover its edges, open ones show it standing between the openings)
    if name.startswith("Door_F") and s.__dict__.get("b_pillar", True):
        b = ctx["body"]
        patch(b, sh, y1 - 0.03, y1 + 0.06, zsill_s, 1.0, color, side, off=0.004, ny=1, ns=4, thick=0.05, inner="interior")
        patch(b, cab, y1 - 0.03, y1 + 0.06, -0.05, 1.0, s.__dict__.get("pillar_color") or s.cab_color(Vector((0, 0, 2))), side, off=0.004, ny=1, ns=6,
              thick=0.05, inner="interior")
    return d


def frame_ring(m, sh, f0, f1, s0, s1, side, fw=0.03, color="trim_black"):
    """A rubber window surround: four strips round the opening (open in the middle)."""
    # the bottom rail is ~2.5 cm tall in real height (s is far from linear near the waist)
    ym = (f0(0.3) + f1(0.3)) / 2
    s_lo = shell_s(sh, ym, sh.point(ym, s0, 1).z - 0.025)
    s_lo = min(s_lo, s0 - 0.005)
    belt = _BUILD.get("belt_color") or color      # the panel under the rubber: paint on cars with a body-colour waist
    patch(m, sh, lambda q: f0(q) - fw, lambda q: f1(q) + fw, s0 - 0.04, s_lo, belt, side, off=0.007, ny=4, ns=1)
    patch(m, sh, lambda q: f0(q) - fw, lambda q: f1(q) + fw, s_lo, s0, color, side, off=0.009, ny=4, ns=1)
    if s1 < 0.999:
        patch(m, sh, lambda q: f0(q) - fw, lambda q: f1(q) + fw, s1, min(1.0, s1 + 0.04), color, side, off=0.007, ny=4, ns=1)
    patch(m, sh, lambda q: f0(q) - fw, f0, s0, s1, color, side, off=0.007, ny=1, ns=4)
    patch(m, sh, f1, lambda q: f1(q) + fw, s0, s1, color, side, off=0.007, ny=1, ns=4)


def glass(m, sh, y0, y1, s0, s1, side, frame="trim_black", fw=0.03, ny=3, ns=3):
    """Window: a hole in the cabin shell, a rubber surround, and see-through glass (Glass part)."""
    f0 = y0 if callable(y0) else (lambda q, v=y0: v)
    f1 = y1 if callable(y1) else (lambda q, v=y1: v)
    if frame:
        frame_ring(m, sh, f0, f1, s0, s1, side, fw, frame)
    patch(_BUILD["glass"], sh, f0, f1, s0, s1, "car_glass", side, off=0.014, ny=ny, ns=ns)
    _BUILD["cab_holes"].append((f0, f1, s0, s1, side))


def lamp_pod(m, c, radii, lens, rim="chrome", rot=(0, 0, 0)):
    ellip(m, c, (radii[0] * 1.12, radii[1] * 1.05, radii[2] * 1.12), rim, seg=14, rings=8, rot=rot)
    ellip(m, Vector(c) + Vector((0, -radii[1] * 0.25, 0)), radii, lens, seg=14, rings=8, rot=rot)


def lights_part(ctx, name, fn):
    """A lamp group as its own part (the game lights it up), parented to Body."""
    lm = K.Mesher(2.0)
    fn(lm)
    ctx["parts"].append((name, lm, None, "Body"))


# ============================================================================== MYVI
def myvi():
    """veh_myvi: 2nd-gen Myvi (the orange SE). Short, tall 5-door hatch with wheels pushed to the
    corners: flat upright nose with a small grille slot, huge swept headlamps climbing the bonnet
    edges toward the A-pillars, a big trapezoid lower intake with round fog lamps, a wedge window
    line, blacked-out pillars, a roof spoiler, gunmetal 5-spoke alloys.
    Dimensions follow the real car (3.69 x 1.67 x 1.55 m, 2.44 m wheelbase), lightly cartooned."""
    W = 1.68
    s = CarSpec(id="veh_myvi", width=W, track=1.44, wr=0.31, ww=0.21, fy=-1.2, ry=1.24, arch_lift=0.02, arch_gap=0.05,
                wheel_style="alloy_dark", spokes=5, floor_z=0.3, roof_z=1.56, seat_x=0.36, seat_y=0.02, dash_y=-0.66,
                dash_z=0.86, b_pillar=True)
    s.pillar_color = "trim_black"
    #            y      half-w  zb    zm    zt    n_side n_top n_bot
    s.stations = [(-1.845, 0.70, 0.26, 0.48, 0.62, 3.2, 2.6, 2.4),
                  (-1.815, 0.79, 0.22, 0.50, 0.70, 4.2, 3.2, 3.4),
                  (-1.70, 0.82, 0.21, 0.54, 0.78, 5.0, 3.4, 4.0),
                  (-1.30, 0.835, 0.21, 0.58, 0.88, 5.4, 3.4, 4.2),
                  (-1.00, 0.835, 0.21, 0.60, 0.95, 5.4, 3.6, 4.2),
                  (0.40, 0.835, 0.21, 0.62, 1.00, 5.4, 3.8, 4.2),
                  (1.45, 0.83, 0.22, 0.64, 1.04, 5.2, 3.6, 4.0),
                  (1.76, 0.81, 0.24, 0.64, 1.04, 4.4, 3.2, 3.4),
                  (1.855, 0.72, 0.30, 0.64, 1.00, 3.2, 2.6, 2.4)]
    # greenhouse: fast windscreen from the A-pillar base, long flat roof, near-upright hatch
    s.cab_stations = [(-1.06, 0.70, 0.88, 0.94, 0.97, 3.0, 2.4, 2.0),
                      (-0.80, 0.77, 0.88, 0.97, 1.22, 3.4, 3.4, 2.0),
                      (-0.34, 0.775, 0.88, 0.99, 1.53, 3.6, 4.6, 2.0),
                      (1.36, 0.765, 0.88, 1.03, 1.54, 3.6, 4.6, 2.0),
                      (1.58, 0.74, 0.88, 1.04, 1.49, 3.4, 4.0, 2.0),
                      (1.80, 0.64, 0.88, 1.04, 1.12, 3.0, 2.6, 2.0)]
    s.y_breaks, s.s_breaks, s.cab_breaks = (-1.7, 1.7), (-0.55,), ()

    def body_color(c):
        if c.z < 0.3:
            return "trim_black"                                   # sills / under-bumper
        return "car_paint"
    s.body_color = body_color
    # blacked-out pillars between the window line and a body-colour roof
    s.cab_color = lambda c: "car_paint" if c.z > 1.46 else "trim_black"
    s.belt_s = 0.04

    def details(ctx):
        b, sh, cab, parts = ctx["body"], ctx["shell"], ctx["cab"], ctx["parts"]
        ws_edge = lambda q: -0.94 + q * 0.58          # the A-pillar: where the windscreen ends at height q
        for side in (1, -1):
            door(ctx, "Door_FL" if side > 0 else "Door_FR", side, -0.86, 0.3, -0.5, 0.2, 0.52,
                 glass_front=lambda q: ws_edge(q) + 0.09, glass_rear=lambda q: 0.24)
            door(ctx, "Door_RL" if side > 0 else "Door_RR", side, 0.32, 1.14, -0.5, 0.2, 0.52,
                 glass_front=lambda q: 0.38, glass_rear=lambda q: 1.06 - q * 0.08)
            glass(b, cab, 1.16, lambda q: 1.46 - q * 0.1, 0.05, 0.46, side, ny=3, ns=3)            # rear quarter
            mirror_arm(b, sh.point(-0.94, 0.42, side) + Vector((0, 0.02, 0.05)), side)
            patch(b, sh, -1.28, -1.2, 0.2, 0.28, "lamp_amber", side, off=0.006, ny=1, ns=1)       # side repeater
            patch(b, sh, -1.25, 1.55, -0.5, -0.38, "car_paint", side, off=0.012, ny=10, ns=1, thick=0.03)  # SE side skirt
        # windscreen + hatch glass
        for side in (1, -1):
            glass(b, cab, lambda q: -1.02 + (1 - q) * 0.02, ws_edge, 0.02, 1.0, side, ny=4, ns=4)
            glass(b, cab, lambda q: 1.77 - q * 0.2, 1.82, 0.46, 1.0, side, ny=2, ns=3)
        # roof spoiler over the hatch + high brake light
        rz = cab.point(1.5, 1.0, 1).z
        for side in (1, -1):
            patch(b, cab, 1.3, 1.62, 0.9, 1.0, "car_paint", side, off=0.02, ny=3, ns=2, thick=0.03)
        rbox(b, (0, 1.63, rz), (0.22, 0.03, 0.03), "lamp_red", r=0.5)
        # front: small grille slot + chrome badge oval, big trapezoid lower intake with mesh bars,
        # round fog lamps in black pods at the bumper corners, plate on the bumper
        fz = -1.84
        rbox(b, (0, fz, 0.63), (0.5, 0.05, 0.075), "grille_black", r=0.6)
        ellip(b, (0, fz - 0.03, 0.635), (0.07, 0.015, 0.035), "chrome", seg=12, rings=6)
        tr = lambda z: 0.36 + (0.5 - z) * 0.5                   # intake widens toward the bottom
        for k, z in enumerate((0.32, 0.37, 0.42, 0.47)):
            b.box((0, fz + 0.005, z), (tr(z) * 2, 0.03, 0.035), "grille_black")
        rbox(b, (0, fz - 0.01, 0.395), (0.95, 0.03, 0.2), "grille_black", r=0.25)
        for k in range(3):
            b.box((0, fz - 0.025, 0.33 + k * 0.05), (0.88, 0.01, 0.012), "trim_black")
        for x in (0.62, -0.62):
            rbox(b, (x, fz + 0.02, 0.4), (0.2, 0.05, 0.14), "grille_black", r=0.5)
            ellip(b, (x, fz - 0.015, 0.4), (0.05, 0.015, 0.05), "lens_clear", seg=12, rings=6)
        plate(b, (0, fz - 0.025, 0.54), w=0.42, h=0.1)
        plate(b, (0, 1.87, 0.58), facing=1)
        for x in (0.25, -0.3):                                    # wipers at the windscreen base
            b.box((x, -0.98, 0.99), (0.52, 0.02, 0.015), "trim_black", rot=(0, 0.05, 0.18))
        b.box((0, 1.845, 0.76), (0.22, 0.02, 0.03), "chrome")       # hatch handle
        b.cyl((0.45, 1.83, 0.28), 0.035, 0.035, 0.12, "chrome", seg=10, rot=(math.pi / 2, 0, 0))
        parts.append(("FX_Exhaust", None, (0.45, 1.9, 0.28), "Body"))
        for side in (1, -1):
            # swept headlamp housings: from the front corner up the bonnet edge toward the A-pillar
            patch(b, sh, -1.83, lambda q: -1.62 + max(0.0, q - 0.2) * 0.62, 0.08, 0.82, "trim_black", side, off=0.006,
                  ny=5, ns=4)

        def heads(lm):
            for side in (1, -1):
                patch(lm, sh, -1.825, lambda q: -1.64 + max(0.0, q - 0.22) * 0.58, 0.12, 0.8, "lens_clear", side,
                      off=0.014, ny=5, ns=4)
                for k, (y, q) in enumerate(((-1.76, 0.42), (-1.64, 0.58))):   # twin projector bowls
                    p = sh.point(y, q, side) + sh.normal(y, q, side) * 0.012
                    ellip(lm, p, (0.05, 0.012, 0.045), "chrome", seg=12, rings=6)
                    ellip(lm, p + sh.normal(y, q, side) * 0.006, (0.028, 0.008, 0.028), "lamp_white", seg=10, rings=6)
                patch(lm, sh, -1.8, -1.72, 0.14, 0.26, "lamp_amber", side, off=0.018, ny=1, ns=1)
        lights_part(ctx, "HeadLights", heads)

        def tails(lm):
            for side in (1, -1):
                # tall tail lamps standing up the rear corners beside the hatch
                rbox(lm, (side * 0.64, 1.82, 0.93), (0.15, 0.1, 0.36), "lamp_red", r=0.5)
                rbox(lm, (side * 0.66, 1.845, 0.33), (0.14, 0.03, 0.04), "lamp_red", r=0.5)   # bumper reflector
        lights_part(ctx, "BrakeLights", tails)

        def rev(lm):
            for side in (1, -1):
                rbox(lm, (side * 0.64, 1.835, 0.83), (0.1, 0.1, 0.08), "lamp_white", r=0.5)
        lights_part(ctx, "ReverseLights", rev)
        ornament(parts, (0, -0.62, 1.4), kind="freshener")
        antenna(parts, (0, 1.2, 1.54), length=0.36)
    return build_car(s, details)


# ============================================================================== SAGA (classic)
def saga():
    """veh_saga: the 1985 Saga 1.3 (Lancer Fiore-based): a crisp 80s wedge - low flat bonnet,
    flush rectangular headlamps either side of a black slatted grille with a small badge,
    black wrap-round bumpers, flat creased flanks, thin pillars round a big glasshouse, a
    long flat boot, steel wheels with body-colour centre caps. 4.08 x 1.62 x 1.36 m."""
    W = 1.64
    s = CarSpec(id="veh_saga", width=W, track=1.4, wr=0.29, ww=0.18, fy=-1.22, ry=1.16, arch_lift=0.0, arch_gap=0.05,
                wheel_style="steel_paint", floor_z=0.28, roof_z=1.38, seat_x=0.36, seat_y=-0.02, dash_y=-0.62, dash_z=0.8,
                seat_color="interior")
    s.stations = [(-2.05, 0.74, 0.28, 0.46, 0.58, 5.0, 3.6, 3.6),
                  (-2.03, 0.80, 0.26, 0.50, 0.64, 8.0, 5.0, 5.0),
                  (-1.96, 0.815, 0.25, 0.54, 0.68, 9.0, 6.0, 6.0),
                  (-1.30, 0.82, 0.25, 0.58, 0.76, 9.0, 6.0, 6.0),
                  (-0.64, 0.82, 0.25, 0.60, 0.82, 9.0, 6.0, 6.0),
                  (1.30, 0.82, 0.25, 0.62, 0.86, 9.0, 6.0, 6.0),
                  (1.98, 0.815, 0.26, 0.62, 0.86, 9.0, 6.0, 5.0),
                  (2.05, 0.78, 0.30, 0.62, 0.84, 6.0, 4.0, 3.6)]
    s.cab_stations = [(-0.68, 0.72, 0.78, 0.83, 0.86, 4.0, 2.4, 2.0),
                      (-0.52, 0.76, 0.78, 0.84, 1.02, 4.4, 4.0, 2.0),
                      (-0.12, 0.745, 0.78, 0.85, 1.35, 4.6, 8.0, 2.0),
                      (0.86, 0.745, 0.78, 0.86, 1.36, 4.6, 8.0, 2.0),
                      (1.12, 0.74, 0.78, 0.86, 1.14, 4.4, 5.0, 2.0),
                      (1.36, 0.70, 0.78, 0.86, 0.9, 4.0, 2.4, 2.0)]
    s.y_breaks, s.s_breaks, s.cab_breaks = (-1.96, 1.98), (-0.45, 0.2), ()
    s.body_color = lambda c: "trim_black" if c.z < 0.3 else "car_paint"
    s.cab_color = lambda c: "car_paint"

    def details(ctx):
        b, sh, cab, parts = ctx["body"], ctx["shell"], ctx["cab"], ctx["parts"]
        ws_edge = lambda q: -0.56 + q * 0.42
        for side in (1, -1):
            door(ctx, "Door_FL" if side > 0 else "Door_FR", side, -0.66, 0.3, -0.42, 0.2, 0.62,
                 glass_front=lambda q: ws_edge(q) + 0.06, glass_rear=lambda q: 0.24)
            door(ctx, "Door_RL" if side > 0 else "Door_RR", side, 0.32, 1.1, -0.42, 0.2, 0.62,
                 glass_front=lambda q: 0.38, glass_rear=lambda q: 1.06 - q * 0.16)
            mirror_arm(b, sh.point(-0.62, 0.5, side) + Vector((0, 0.02, 0.03)), side, color="trim_black", size=0.9)
            # the flank crease + black lower moulding that wraps into the bumpers
            patch(b, sh, -1.96, 1.98, 0.14, 0.17, "trim_black", side, off=0.008, ny=12, ns=1)
            patch(b, sh, -1.96, 1.98, -0.3, -0.18, "trim_black", side, off=0.012, ny=12, ns=1, thick=0.02)
        for side in (1, -1):
            glass(b, cab, lambda q: -0.66 + (1 - q) * 0.02, ws_edge, 0.02, 1.0, side, ny=4, ns=4)
            glass(b, cab, lambda q: 1.3 - q * 0.42, 1.34, 0.02, 1.0, side, ny=3, ns=4)
        # front: flat slanted panel, black slatted grille between the lamps, small shield badge
        fz = -2.0
        rbox(b, (0, fz, 0.56), (0.72, 0.06, 0.14), "grille_black", r=0.2)
        for k in range(3):
            b.box((0, fz - 0.035, 0.515 + k * 0.045), (0.68, 0.012, 0.016), "trim_black")
        b.prism((0, fz - 0.045, 0.6), (0.09, 0.02, 0.1), "chrome", rot=(math.pi / 2, 0, math.pi))    # badge
        # black bumpers wrapping round, with amber corners at the front and the plate
        for y, f in ((-2.08, -1), (2.08, 1)):
            rbox(b, (0, y, 0.38), (W + 0.04, 0.14, 0.15), "trim_black", r=0.45)
        plate(b, (0, -2.16, 0.38), w=0.44, h=0.11)
        plate(b, (0, 2.06, 0.6), facing=1)
        b.box((0, 2.055, 0.84), (1.3, 0.02, 0.02), "trim_black")                             # boot lid edge
        for x in (0.28, -0.28):
            b.box((x, -0.62, 0.84), (0.46, 0.02, 0.015), "trim_black", rot=(0, 0.04, 0.12))
        b.cyl((0.5, 2.1, 0.26), 0.032, 0.032, 0.14, "chrome", seg=10, rot=(math.pi / 2, 0, 0))
        parts.append(("FX_Exhaust", None, (0.5, 2.18, 0.26), "Body"))

        def heads(lm):
            for x in (0.56, -0.56):   # big flush rectangular headlamps, ribbed lens
                rbox(lm, (x, fz + 0.005, 0.57), (0.36, 0.05, 0.16), "trim_black", r=0.15)
                rbox(lm, (x, fz - 0.02, 0.57), (0.33, 0.02, 0.13), "lens_clear", r=0.2)
                for k in range(4):
                    lm.box((x - 0.12 + k * 0.08, fz - 0.032, 0.57), (0.012, 0.006, 0.11), "rim_silver")
            for x in (0.72, -0.72):
                rbox(lm, (x, -2.13, 0.38), (0.16, 0.03, 0.06), "lamp_amber", r=0.4)
        lights_part(ctx, "HeadLights", heads)

        def tails(lm):
            for x in (0.56, -0.56):
                rbox(lm, (x, 2.04, 0.66), (0.44, 0.06, 0.16), "lamp_red", r=0.2)
                rbox(lm, (x * 1.26, 2.05, 0.66), (0.1, 0.05, 0.15), "lamp_amber", r=0.2)
        lights_part(ctx, "BrakeLights", tails)

        def rev(lm):
            for x in (0.3, -0.3):
                rbox(lm, (x, 2.05, 0.66), (0.1, 0.05, 0.13), "lamp_white", r=0.2)
        lights_part(ctx, "ReverseLights", rev)
        ornament(parts, (0, -0.5, 1.28), kind="tasbih")
        antenna(parts, (0.74, -1.25, 0.78), length=0.7, lean=0.25)
    return build_car(s, details)


# ============================================================================== KANCIL
def kancil():
    """veh_kancil: the tiny, tall, round-eyed city hatch. Short wheelbase, 12-inch steel
    wheels, round headlamps in a flat face, a stubby little tail."""
    W = 1.5
    s = CarSpec(id="veh_kancil", width=W, track=1.3, wr=0.28, ww=0.16, fy=-0.98, ry=1.0, arch_lift=0.01, arch_gap=0.05,
                wheel_style="steel", floor_z=0.28, roof_z=1.5, seat_x=0.32, seat_y=0.05, dash_y=-0.55, dash_z=0.8)
    s.stations = [(-1.54, 0.6, 0.28, 0.50, 0.66, 3.0, 2.4, 2.4),
                  (-1.48, 0.72, 0.24, 0.52, 0.76, 3.6, 3.0, 3.0),
                  (-1.2, 0.75, 0.23, 0.56, 0.86, 4.0, 3.0, 3.0),
                  (0.8, 0.755, 0.23, 0.6, 0.92, 4.0, 3.2, 3.0),
                  (1.4, 0.74, 0.24, 0.6, 0.92, 3.6, 3.0, 2.8),
                  (1.56, 0.62, 0.3, 0.6, 0.86, 3.0, 2.4, 2.4)]
    s.cab_stations = [(-0.9, 0.56, 0.8, 0.85, 0.9, 2.6, 2.0, 2.0),
                      (-0.6, 0.66, 0.8, 0.88, 1.36, 3.2, 3.0, 2.0),
                      (-0.2, 0.68, 0.8, 0.88, 1.5, 3.6, 3.6, 2.0),
                      (1.2, 0.68, 0.8, 0.88, 1.5, 3.6, 3.6, 2.0),
                      (1.46, 0.62, 0.8, 0.86, 1.3, 3.0, 3.0, 2.0),
                      (1.52, 0.5, 0.8, 0.84, 0.94, 2.4, 2.0, 2.0)]
    s.y_breaks, s.s_breaks, s.cab_breaks = (-1.4, 1.45), (-0.5,), ()
    s.body_color = lambda c: "trim_black" if c.z < 0.31 else "car_paint"
    s.cab_color = lambda c: "car_paint"
    s.rear_seat = True

    def details(ctx):
        b, sh, cab, parts = ctx["body"], ctx["shell"], ctx["cab"], ctx["parts"]
        for side in (1, -1):
            door(ctx, "Door_FL" if side > 0 else "Door_FR", side, -0.56, 0.5, -0.45, 0.2, 0.7,
                 glass_front=lambda q: -0.5 + q * 0.2)
            glass(b, cab, 0.6, 1.36, 0.04, 0.6, side, ny=3, ns=3)
            mirror_arm(b, sh.point(-0.54, 0.35, side) + Vector((0, 0, 0.03)), side, size=0.85)
        glass(b, cab, lambda q: -0.86 + (1 - q) * 0.02, -0.6, 0.02, 1.0, 1, ny=4, ns=3)
        glass(b, cab, lambda q: -0.86 + (1 - q) * 0.02, -0.6, 0.02, 1.0, -1, ny=4, ns=3)
        for side in (1, -1):
            glass(b, cab, 1.36, 1.5, 0.02, 1.0, side, ny=2, ns=3)
        rbox(b, (0, -1.535, 0.58), (0.56, 0.06, 0.07), "grille_black", r=0.6)
        for y, f in ((-1.56, -1), (1.58, 1)):
            rbox(b, (0, y, 0.36), (W - 0.02, 0.1, 0.14), "trim_black", r=0.6)
        plate(b, (0, -1.62, 0.37), w=0.36)
        plate(b, (0, 1.57, 0.58), w=0.36, facing=1)
        b.box((0.24, -0.76, 0.93), (0.42, 0.02, 0.015), "trim_black", rot=(0, 0.04, 0.15))
        b.cyl((0.4, 1.58, 0.26), 0.028, 0.028, 0.1, "chrome", seg=10, rot=(math.pi / 2, 0, 0))
        parts.append(("FX_Exhaust", None, (0.4, 1.65, 0.26), "Body"))

        def heads(lm):
            for x in (0.5, -0.5):   # big round eyes
                lamp_pod(lm, (x, -1.5, 0.66), (0.11, 0.05, 0.11), "lens_clear", rim="car_paint", rot=(0, 0, 0))
                ellip(lm, (x, -1.56, 0.66), (0.045, 0.01, 0.045), "lamp_white", seg=10, rings=6)
                rbox(lm, (x * 1.26, -1.51, 0.5), (0.1, 0.04, 0.05), "lamp_amber", r=0.5)
        lights_part(ctx, "HeadLights", heads)

        def tails(lm):
            for side in (1, -1):
                patch(lm, sh, 1.44, 1.56, 0.0, 0.5, "lamp_red", side, off=0.012, ny=1, ns=2)
        lights_part(ctx, "BrakeLights", tails)

        def rev(lm):
            for side in (1, -1):
                patch(lm, sh, 1.5, 1.56, -0.2, -0.02, "lamp_white", side, off=0.012, ny=1, ns=1)
        lights_part(ctx, "ReverseLights", rev)
        ornament(parts, (0, -0.5, 1.36), kind="tasbih")
        antenna(parts, (-0.6, -0.9, 0.9), length=0.62, lean=0.2)
    return build_car(s, details)


# ============================================================================== ALPHARD
def alphard():
    """veh_alphard: the latest (4th-gen) Alphard - a huge, tall slab MPV: short sloping bonnet,
    enormous glasshouse on blacked-out pillars under a body-colour 'floating' roof, a chrome line
    along the window base that kicks up at the rear, sliding side door, slim sharp headlamps over
    a big grille of vertical chrome fins, tail lamps across the tailgate, multi-spoke 18s.
    4.99 x 1.85 x 1.94 m, 3.0 m wheelbase."""
    W = 1.86
    s = CarSpec(id="veh_alphard", width=W, track=1.6, wr=0.37, ww=0.24, fy=-1.5, ry=1.5, arch_lift=0.02, arch_gap=0.05,
                wheel_style="alloy", spokes=12, floor_z=0.4, roof_z=1.94, seat_x=0.42, seat_y=-0.38, dash_y=-1.08,
                dash_z=0.98, chrome_handles=True, seat_color="seat_black")
    s.stations = [(-2.5, 0.86, 0.30, 0.62, 1.02, 6.0, 4.0, 4.0),
                  (-2.47, 0.90, 0.26, 0.64, 1.08, 7.0, 4.5, 5.0),
                  (-2.25, 0.925, 0.25, 0.66, 1.13, 8.0, 5.0, 5.0),
                  (-1.75, 0.925, 0.25, 0.68, 1.18, 8.0, 5.0, 5.0),
                  (1.9, 0.925, 0.25, 0.72, 1.22, 8.0, 5.0, 5.0),
                  (2.4, 0.91, 0.27, 0.72, 1.22, 7.0, 4.4, 4.4),
                  (2.5, 0.84, 0.32, 0.72, 1.18, 4.0, 3.0, 3.0)]
    s.cab_stations = [(-1.8, 0.84, 1.1, 1.17, 1.21, 5.0, 2.4, 2.0),
                      (-1.5, 0.89, 1.1, 1.19, 1.52, 7.0, 4.0, 2.0),
                      (-0.95, 0.90, 1.1, 1.2, 1.9, 7.0, 8.0, 2.0),
                      (2.25, 0.90, 1.1, 1.22, 1.93, 7.0, 8.0, 2.0),
                      (2.44, 0.87, 1.1, 1.22, 1.86, 6.0, 5.0, 2.0),
                      (2.5, 0.80, 1.1, 1.22, 1.3, 4.0, 3.0, 2.0)]
    s.y_breaks, s.s_breaks, s.cab_breaks = (-2.25, 2.4), (-0.55,), ()
    s.body_color = lambda c: "trim_black" if c.z < 0.34 else "car_paint"
    s.cab_color = lambda c: "car_paint" if c.z > 1.82 else "trim_black"   # floating roof, black pillars
    s.belt_s = 0.04
    s.pillar_color = "trim_black"

    def details(ctx):
        b, sh, cab, parts = ctx["body"], ctx["shell"], ctx["cab"], ctx["parts"]
        ws_edge = lambda q: -1.62 + q * 0.64
        for side in (1, -1):
            door(ctx, "Door_FL" if side > 0 else "Door_FR", side, -1.1, -0.3, -0.5, 0.3, 0.62,
                 glass_front=lambda q: max(-1.06, ws_edge(q) + 0.12), glass_rear=lambda q: -0.4)
            glass(b, cab, lambda q: ws_edge(q) + 0.12, -1.12, 0.04, 0.5, side, ny=2, ns=2)          # front quarter light
            # the big power sliding door (it slides back along the rail in the game)
            door(ctx, "SlideDoor_L" if side > 0 else "SlideDoor_R", side, -0.28, 1.0, -0.5, 0.3, 0.62,
                 glass_front=lambda q: -0.2, glass_rear=lambda q: 0.92)
            glass(b, cab, 1.06, lambda q: 2.3 - q * 0.08, 0.04, 0.6, side, ny=4, ns=3)            # rear quarter
            # chrome beltline: along the window base, kicking up at the D-pillar
            patch(b, cab, -1.6, 2.2, 0.026, 0.04, "chrome", side, off=0.02, ny=12, ns=1)
            patch(b, cab, 2.0, 2.34, 0.03, 0.2, "chrome", side, off=0.02, ny=2, ns=1)
            patch(b, sh, 1.0, 2.3, 0.12, 0.15, "trim_black", side, off=0.01, ny=4, ns=1)          # slide rail groove
            mirror_arm(b, sh.point(-1.62, 0.55, side) + Vector((0, 0.02, 0.06)), side, color="chrome", size=1.2)
            patch(b, sh, -2.1, 2.35, -0.44, -0.38, "chrome", side, off=0.012, ny=10, ns=1)        # rocker chrome
        for side in (1, -1):
            glass(b, cab, lambda q: -1.78 + (1 - q) * 0.02, ws_edge, 0.02, 1.0, side, ny=4, ns=4)
            glass(b, cab, 2.36, 2.52, 0.3, 1.0, side, ny=2, ns=3)
        # front: slim bonnet, big grille of vertical chrome fins under a chrome brow, bumper vents
        fz = -2.5                                            # the flat front face
        rbox(b, (0, fz + 0.01, 0.6), (1.2, 0.04, 0.48), "grille_black", r=0.2)
        for k in range(15):
            x = -0.56 + k * 0.08
            b.box((x, fz - 0.008, 0.6), (0.016, 0.02, 0.42), "chrome")
        rbox(b, (0, fz - 0.01, 0.855), (1.26, 0.03, 0.03), "chrome", r=0.4)
        for x in (0.74, -0.74):
            rbox(b, (x, fz, 0.5), (0.05, 0.03, 0.24), "lamp_white", r=0.5)                    # vertical LED slots
        plate(b, (0, fz - 0.02, 0.3))
        rbox(b, (0, 2.5, 1.06), (1.4, 0.04, 0.05), "chrome", r=0.5)                          # tailgate chrome bar
        plate(b, (0, 2.52, 0.8), facing=1)
        for x in (0.3, -0.34):
            b.box((x, -1.7, 1.21), (0.6, 0.02, 0.015), "trim_black", rot=(0, 0.04, 0.12))
        for x in (0.62, -0.62):
            b.cyl((x, 2.48, 0.32), 0.04, 0.04, 0.12, "chrome", seg=10, rot=(math.pi / 2, 0, 0))
        parts.append(("FX_Exhaust", None, (0.62, 2.56, 0.32), "Body"))

        def heads(lm):
            for side in (1, -1):
                # slim, sharp headlamps set at the top corners, three LED dots + an eyebrow
                x = side * 0.6
                rbox(lm, (x, fz, 0.93), (0.4, 0.03, 0.1), "trim_black", r=0.4)                  # slim lamp housing
                for k in range(3):
                    rbox(lm, (x - side * 0.1 + side * k * 0.08, fz - 0.012, 0.92), (0.05, 0.015, 0.04), "lamp_white", r=0.5)
                rbox(lm, (x, fz - 0.012, 0.97), (0.36, 0.015, 0.015), "lamp_white", r=0.5)          # LED eyebrow
                rbox(lm, (side * 0.8, fz + 0.01, 0.9), (0.06, 0.03, 0.05), "lamp_amber", r=0.5)
        lights_part(ctx, "HeadLights", heads)

        def tails(lm):
            rbox(lm, (0, 2.51, 1.16), (1.5, 0.03, 0.05), "lamp_red", r=0.5)                  # full-width light bar
            for side in (1, -1):
                patch(lm, sh, 2.36, 2.49, 0.3, 0.8, "lamp_red", side, off=0.012, ny=1, ns=3)
            rbox(lm, (0, 2.46, 1.92), (0.5, 0.04, 0.03), "lamp_red", r=0.5)
        lights_part(ctx, "BrakeLights", tails)

        def rev(lm):
            for side in (1, -1):
                patch(lm, sh, 2.42, 2.49, -0.25, -0.05, "lamp_white", side, off=0.012, ny=1, ns=1)
        lights_part(ctx, "ReverseLights", rev)
        ornament(parts, (0, -0.98, 1.78), kind="tasbih")
        antenna(parts, (0, 1.9, 1.93), length=0.12, lean=1.2)   # shark-fin stub
    return build_car(s, details)


# ============================================================================== HILUX
def hilux():
    """veh_hilux: a lifted double-cab 4x4 - bull bar, snorkel, roll bar over the tray,
    spare tyre in the back, knobbly mud tyres on dark 6-spoke rims, side steps."""
    W = 1.86
    s = CarSpec(id="veh_hilux", width=W, track=1.6, wr=0.42, ww=0.3, fy=-1.62, ry=1.52, arch_lift=0.06, arch_gap=0.08,
                wheel_style="offroad", spokes=6, knobbly=True, floor_z=0.62, roof_z=2.02, seat_x=0.42, seat_y=-0.35,
                dash_y=-0.98, dash_z=1.2, seat_color="interior")
    s.stations = [(-2.56, 0.74, 0.56, 0.84, 1.04, 3.0, 2.4, 2.4),
                  (-2.48, 0.9, 0.52, 0.86, 1.14, 4.0, 3.0, 3.0),
                  (-2.0, 0.93, 0.5, 0.9, 1.26, 5.0, 4.0, 4.0),
                  (-0.9, 0.93, 0.5, 0.92, 1.3, 6.0, 5.0, 4.0),
                  (0.62, 0.93, 0.5, 0.92, 1.3, 6.0, 5.0, 4.0)]
    # tray: separate shell behind the cab
    s.cab_stations = [(-1.42, 0.62, 1.22, 1.28, 1.32, 3.0, 2.2, 2.0),
                      (-1.0, 0.82, 1.22, 1.3, 1.84, 4.0, 3.0, 2.0),
                      (-0.6, 0.84, 1.22, 1.3, 2.02, 5.0, 5.0, 2.0),
                      (0.5, 0.84, 1.22, 1.3, 2.02, 5.0, 5.0, 2.0),
                      (0.62, 0.8, 1.22, 1.3, 1.9, 4.0, 3.6, 2.0)]
    s.y_breaks, s.s_breaks, s.cab_breaks = (-2.4,), (-0.5,), ()
    s.body_color = lambda c: "trim_black" if c.z < 0.58 else "car_paint"
    s.cab_color = lambda c: "car_paint"

    def details(ctx):
        b, sh, cab, parts = ctx["body"], ctx["shell"], ctx["cab"], ctx["parts"]
        # tray behind the cab (its own shell so the arch can be cut too)
        tray = Shell([(0.66, 0.9, 0.52, 0.9, 1.3, 7.0, 7.0, 4.0), (2.5, 0.9, 0.52, 0.9, 1.3, 7.0, 7.0, 4.0),
                      (2.56, 0.86, 0.56, 0.9, 1.26, 5.0, 5.0, 3.0)])
        tm = K.Mesher(2.0)
        shell_mesh(tm, tray, ny=12, ns=14, color_fn=lambda c: "trim_black" if c.z < 0.58 else "car_paint")
        merge_bm(b, cut_arches(tm, [(W / 2 + 0.05, s.ry, s.wr + s.arch_lift, s.wr + s.arch_gap, 0.42),
                                    (-W / 2 - 0.05, s.ry, s.wr + s.arch_lift, s.wr + s.arch_gap, 0.42)]))
        b.box((0, 1.6, 1.305), (1.62, 1.72, 0.02), "bed_liner")                                   # tray floor (on top)
        # spare tyre lying in the tray
        spare = K.Mesher(2.0)
        wheel(spare, (0, 0, 0), 0.4, 0.26, 1, style="offroad", spokes=6)
        sb = _bm_of(spare)
        bmesh.ops.transform(sb, matrix=Matrix.Translation((0, 1.95, 1.46)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'),
                            verts=sb.verts)
        merge_bm(b, sb, sharp_deg=60)
        # roll bar + tailgate handle + tail lamps on the tray corners
        for x in (0.8, -0.8):
            b.tube((x, 0.78, 1.3), (x * 0.9, 0.8, 1.92), 0.035, 0.035, "chrome", seg=8)
        b.tube((-0.72, 0.8, 1.92), (0.72, 0.8, 1.92), 0.035, 0.035, "chrome", seg=8)
        b.box((0, 2.58, 1.12), (0.22, 0.02, 0.04), "trim_black")
        for side in (1, -1):
            door(ctx, "Door_FL" if side > 0 else "Door_FR", side, -0.98, -0.12, -0.5, 0.35, 0.7,
                 glass_front=lambda q: -0.9 + q * 0.3)
            door(ctx, "Door_RL" if side > 0 else "Door_RR", side, -0.1, 0.6, -0.5, 0.35, 0.7,
                 glass_rear=lambda q: 0.52)
            mirror_arm(b, sh.point(-0.96, 0.5, side) + Vector((0, 0, 0.06)), side, color="trim_black", size=1.2)
            # side step, fender flares
            rbox(b, (side * (W / 2 + 0.08), -0.36, 0.5), (0.2, 1.8, 0.05), "trim_black", r=0.5)
            for y in (s.fy, s.ry):
                a = K.Mesher(2.0)
                revolve(a, (side * (W / 2 + 0.03), y, s.wr + s.arch_lift), [(s.wr + 0.14, -0.08), (s.wr + 0.16, 0.02),
                        (s.wr + 0.08, 0.04)], lambda i: "trim_black", seg=24, side=side, closed=False)
                # keep only the upper half of the flare
                for f in list(a.bm.faces):
                    if f.calc_center_median().z < s.wr + s.arch_lift + 0.02:
                        a.bm.faces.remove(f)
                merge_bm(b, a.bm, sharp_deg=70)
        # snorkel up the right A-pillar
        b.tube((-0.95, -1.35, 1.05), (-0.95, -1.2, 1.9), 0.05, 0.05, "trim_black", seg=10)
        rbox(b, (-0.95, -1.24, 1.96), (0.12, 0.16, 0.1), "trim_black", r=0.5)
        glass(b, cab, lambda q: -1.36 + (1 - q) * 0.08, lambda q: -0.98 - q * 0.05, 0.02, 1.0, 1, ny=4, ns=3)
        glass(b, cab, lambda q: -1.36 + (1 - q) * 0.08, lambda q: -0.98 - q * 0.05, 0.02, 1.0, -1, ny=4, ns=3)
        # bull bar + grille + roof light bar
        rbox(b, (0, -2.5, 0.92), (1.2, 0.06, 0.36), "grille_black", r=0.3)
        for k in range(3):
            rbox(b, (0, -2.53, 0.82 + k * 0.1), (1.14, 0.03, 0.035), "chrome", r=0.5)
        for x in (0.45, -0.45):
            b.tube((x, -2.66, 0.45), (x, -2.62, 1.14), 0.04, 0.04, "chrome", seg=8)
        b.tube((-0.55, -2.64, 1.1), (0.55, -2.64, 1.1), 0.04, 0.04, "chrome", seg=8)
        b.tube((-0.9, -2.64, 0.62), (0.9, -2.64, 0.62), 0.045, 0.045, "chrome", seg=8)
        rbox(b, (0, -2.58, 0.55), (W, 0.14, 0.14), "trim_black", r=0.5)
        plate(b, (0, -2.72, 0.5))
        plate(b, (0, 2.6, 0.82), facing=1)
        rbox(b, (0, -0.8, 2.07), (1.3, 0.14, 0.08), "trim_black", r=0.5)
        for k in range(6):
            rbox(b, (-0.5 + k * 0.2, -0.87, 2.07), (0.12, 0.02, 0.05), "lamp_white", r=0.5)
        for x in (0.3, -0.34):
            b.box((x, -1.3, 1.36), (0.58, 0.02, 0.015), "trim_black", rot=(0, 0.04, 0.12))
        b.cyl((0.6, 2.4, 0.5), 0.04, 0.04, 0.2, "chrome", seg=10, rot=(math.pi / 2, 0, 0))
        parts.append(("FX_Exhaust", None, (0.6, 2.52, 0.5), "Body"))

        def heads(lm):
            for side in (1, -1):
                patch(lm, sh, -2.5, -2.3, 0.15, 0.7, "lens_clear", side, off=0.012, ny=2, ns=3)
                patch(lm, sh, -2.49, -2.43, 0.3, 0.55, "lamp_white", side, off=0.016, ny=1, ns=1)
                patch(lm, sh, -2.36, -2.3, 0.2, 0.4, "lamp_amber", side, off=0.016, ny=1, ns=1)
        lights_part(ctx, "HeadLights", heads)

        def tails(lm):
            for side in (1, -1):
                rbox(lm, (side * 0.86, 2.56, 1.02), (0.1, 0.04, 0.36), "lamp_red", r=0.4)
        lights_part(ctx, "BrakeLights", tails)

        def rev(lm):
            for side in (1, -1):
                rbox(lm, (side * 0.86, 2.57, 0.84), (0.09, 0.03, 0.08), "lamp_white", r=0.4)
        lights_part(ctx, "ReverseLights", rev)
        ornament(parts, (0, -0.9, 1.84), kind="freshener")
        antenna(parts, (0.9, -1.3, 1.3), length=0.9, lean=0.15)
    return build_car(s, details)


# ============================================================================== variants
def teksi():
    """veh_teksi: the red-and-white KL budget taxi on the classic Saga body, roof sign."""
    parts = saga()
    for name, m, origin, parent in parts:
        if name == "Body":
            rbox(m, (0, 0.2, 1.46), (0.56, 0.22, 0.16), "plate_white", r=0.5)
            rbox(m, (0, 0.2, 1.37), (0.6, 0.26, 0.03), "trim_black", r=0.5)
            m.box((0, 0.085, 1.46), (0.42, 0.01, 0.07), "lamp_red")
            m.box((0, 0.315, 1.46), (0.42, 0.01, 0.07), "lamp_red")
    for i, (name, m, origin, parent) in enumerate(parts):
        if name == "COL_veh_saga":
            parts[i] = ("COL_veh_teksi", m, origin, parent)
    return parts


def polis():
    """veh_polis: police patrol car on the Myvi body: navy/white livery, light bar."""
    parts = myvi()
    for name, m, origin, parent in parts:
        if name == "Body":
            rbox(m, (0, 0.1, 1.575), (1.1, 0.26, 0.07), "trim_black", r=0.5)
            rbox(m, (0.3, 0.1, 1.63), (0.44, 0.22, 0.1), "siren_red", r=0.5)
            rbox(m, (-0.3, 0.1, 1.63), (0.44, 0.22, 0.1), "siren_blue", r=0.5)
    for i, (name, m, origin, parent) in enumerate(parts):
        if name == "COL_veh_myvi":
            parts[i] = ("COL_veh_polis", m, origin, parent)
    return parts


# ============================================================================== KAPCAI
def kapcai():
    """veh_kapcai: the everyday underbone bike - leg shield, chunky seat, carrier rack,
    chrome muffler, wire-spoke wheels with drum brakes. Parts match the bike rig:
    Body, Steering (bars + forks), FrontWheel (on Steering), RearWheel."""
    parts = []
    wr, ww = 0.3, 0.09
    fz, rz = -0.66, 0.6
    b = K.Mesher(2.0)
    # smooth body cowl under the seat
    cowl = Shell([(-0.1, 0.1, 0.46, 0.6, 0.72, 2.6, 2.4, 2.4), (0.2, 0.17, 0.4, 0.62, 0.8, 2.8, 2.6, 2.6),
                  (0.6, 0.16, 0.5, 0.66, 0.8, 2.6, 2.4, 2.2), (0.82, 0.08, 0.6, 0.68, 0.76, 2.2, 2.0, 2.0)])
    shell_mesh(b, cowl, ny=16, ns=10, color_fn=lambda c: "bike_paint2" if c.z < 0.54 else "bike_paint")
    # leg shield (curved)
    shield = Shell([(-0.5, 0.05, 0.3, 0.6, 0.94, 2.4, 2.2, 2.0), (-0.44, 0.24, 0.28, 0.6, 0.96, 3.0, 2.4, 2.2),
                    (-0.36, 0.25, 0.3, 0.6, 0.92, 3.0, 2.4, 2.2), (-0.3, 0.05, 0.34, 0.6, 0.86, 2.4, 2.2, 2.0)])
    shell_mesh(b, shield, ny=10, ns=10, color="bike_paint")
    # underbone frame, floor, engine, muffler
    b.tube((0, -0.4, 0.8), (0, -0.05, 0.4), 0.05, 0.05, "bike_paint", seg=8)
    rbox(b, (0, -0.12, 0.36), (0.28, 0.44, 0.06), "trim_black", r=0.5)
    rbox(b, (0, 0.08, 0.3), (0.22, 0.3, 0.22), "disc_steel", r=0.4)                          # engine
    b.cyl((0.1, 0.08, 0.3), 0.08, 0.08, 0.06, "rim_silver", seg=12, rot=AX)                   # clutch cover
    b.tube((-0.08, -0.06, 0.24), (-0.14, 0.3, 0.2), 0.03, 0.03, "chrome", seg=8)
    b.tube((-0.14, 0.3, 0.22), (-0.16, 0.78, 0.3), 0.055, 0.045, "chrome", seg=12)            # muffler
    rbox(b, (-0.15, 0.5, 0.26), (0.02, 0.22, 0.08), "trim_black", r=0.5)                      # heat guard
    # seat + grab rail + carrier rack + tail lamp + plate
    rbox(b, (0, 0.34, 0.84), (0.27, 0.62, 0.1), "seat_black", r=0.7)
    b.tube((0.13, 0.55, 0.84), (0.1, 0.9, 0.86), 0.012, 0.012, "chrome", seg=6)
    b.tube((-0.13, 0.55, 0.84), (-0.1, 0.9, 0.86), 0.012, 0.012, "chrome", seg=6)
    b.box((0, 0.84, 0.86), (0.28, 0.24, 0.02), "trim_black")
    rbox(b, (0, 0.94, 0.72), (0.14, 0.05, 0.06), "lamp_red", r=0.5)
    plate(b, (0, 0.98, 0.58), w=0.22, h=0.12, facing=1)
    b.box((0, 0.85, 0.62), (0.16, 0.3, 0.03), "bike_paint", rot=(0.3, 0, 0))                   # rear fender
    # chain + swing arm + rear shocks
    b.box((0.1, 0.35, 0.3), (0.02, 0.56, 0.05), "trim_black")
    for x in (0.12, -0.12):
        b.tube((x, 0.0, 0.3), (x, rz, wr), 0.02, 0.02, "trim_black", seg=6)
        b.tube((x, 0.5, 0.36), (x, 0.42, 0.72), 0.025, 0.025, "chrome", seg=6)
        b.tube((x, 0.49, 0.42), (x, 0.44, 0.62), 0.035, 0.035, "sticker_yellow", seg=8)        # spring
    for s in (1, -1):
        rbox(b, (s * 0.2, -0.02, 0.28), (0.12, 0.04, 0.03), "rubber", r=0.5)                    # footpegs
    b.tube((0.08, 0.05, 0.24), (0.2, 0.12, 0.02), 0.015, 0.015, "trim_black", seg=4)           # side stand
    parts.append(("Body", b, (0, 0, wr + 0.05), None))
    parts.append(("Seat_Rider", None, (0, 0.28, 0.9), "Body"))
    parts.append(("FX_Exhaust", None, (-0.16, 0.84, 0.3), "Body"))

    head = Vector((0, -0.46, 0.9))
    st = K.Mesher(2.0)
    for x in (0.07, -0.07):
        st.tube(head + Vector((x, 0, 0.06)), Vector((x, fz + 0.04, wr + 0.12)), 0.026, 0.026, "chrome", seg=8)
        st.tube(Vector((x, fz + 0.03, wr + 0.14)), Vector((x, fz, wr)), 0.034, 0.034, "trim_black", seg=8)
    rbox(st, (0, fz - 0.03, wr + 0.26), (0.13, 0.46, 0.05), "bike_paint", rot=(0.18, 0, 0), r=0.7)   # front fender
    cowl2 = Shell([(-0.62, 0.05, 0.98, 1.06, 1.14, 2.4, 2.4, 2.2), (-0.56, 0.17, 0.96, 1.06, 1.16, 2.8, 2.6, 2.4),
                   (-0.36, 0.17, 0.98, 1.06, 1.14, 2.8, 2.6, 2.4), (-0.32, 0.06, 1.0, 1.06, 1.12, 2.4, 2.2, 2.2)])
    shell_mesh(st, cowl2, ny=8, ns=10, color="bike_paint")
    st.tube(Vector((-0.34, -0.42, 1.1)), Vector((0.34, -0.42, 1.1)), 0.016, 0.016, "chrome", seg=6)
    for s in (1, -1):
        st.tube(Vector((s * 0.24, -0.42, 1.1)), Vector((s * 0.36, -0.42, 1.1)), 0.028, 0.028, "rubber", seg=8)
        st.tube(Vector((s * 0.16, -0.44, 1.12)), Vector((s * 0.24, -0.34, 1.13)), 0.006, 0.006, "chrome", seg=4)  # levers
        st.tube(Vector((s * 0.2, -0.44, 1.12)), Vector((s * 0.3, -0.44, 1.22)), 0.008, 0.008, "trim_black", seg=4)
        mc = Vector((s * 0.31, -0.44, 1.26))
        st.cyl(mc, 0.05, 0.05, 0.015, "trim_black", seg=10, rot=(math.pi / 2, 0, 0))
        st.cyl(mc + Vector((0, 0.009, 0)), 0.04, 0.04, 0.004, "car_glass", seg=10, rot=(math.pi / 2, 0, 0))
    parts.append(("Steering", st, tuple(head), None))

    def lamps(lm):
        lamp_pod(lm, (0, -0.64, 1.02), (0.075, 0.035, 0.06), "lens_clear", rim="bike_paint")
        for s in (1, -1):
            ellip(lm, (s * 0.15, -0.6, 1.04), (0.028, 0.025, 0.02), "lamp_amber", seg=8, rings=5)
    hl = K.Mesher(2.0)
    lamps(hl)
    parts.append(("HeadLights", hl, None, "Steering"))

    def spoke_wheel(m, c, r, w):
        c = Vector(c)
        revolve(m, c, [(r * 0.8, -w / 2), (r - 0.018, -w / 2), (r, -w / 2 + 0.018), (r, w / 2 - 0.018),
                       (r - 0.018, w / 2), (r * 0.8, w / 2)], lambda i: "tyre", seg=22, side=1)
        revolve(m, c, [(r * 0.8, -w * 0.35), (r * 0.79, w * 0.35)], lambda i: "rim_silver", seg=22, side=1, closed=False)
        m.cyl(c, 0.075, 0.075, w * 0.9, "disc_steel", seg=14, rot=AX)                          # drum brake hub
        n = max(8, int(18 * K.LOD_DETAIL))
        for k in range(n):
            a = k / n * math.tau
            sx = 0.03 if k % 2 else -0.03
            p0 = c + Vector((sx, 0.07 * math.cos(a + 0.4), 0.07 * math.sin(a + 0.4)))
            p1 = c + Vector((sx * 0.3, r * 0.79 * math.cos(a), r * 0.79 * math.sin(a)))
            m.tube(p0, p1, 0.0035, 0.0035, "chrome", seg=4)

    fw = K.Mesher(2.0)
    spoke_wheel(fw, (0, fz, wr), wr, ww)
    parts.append(("FrontWheel", fw, (0, fz, wr), "Steering"))
    rw = K.Mesher(2.0)
    spoke_wheel(rw, (0, rz, wr), wr, ww * 1.15)
    parts.append(("RearWheel", rw, (0, rz, wr), None))
    col = K.Mesher()
    col.box((0, 0.0, 0.58), (0.5, 1.85, 0.9), "rubber")
    parts.append(("COL_veh_kapcai", col, None, "__collision__"))
    return parts


BUILDERS = {"veh_myvi": myvi, "veh_saga": saga, "veh_kancil": kancil, "veh_alphard": alphard, "veh_hilux": hilux,
            "veh_teksi": teksi, "veh_polis": polis, "veh_kapcai": kapcai}


# ============================================================================== assembly
def assemble(asset_id, parts, mat, cols):
    """Like kl_vehicles.assemble, plus empties (mesher None) and nested parents."""
    root = bpy.data.objects.new(asset_id, None)
    root.empty_display_size = 0.3
    root["kl_world_origin"] = [0, 0, 0]
    cols["EXPORT"].objects.link(root)
    made = {}
    tris = 0
    for name, m, origin, parent in parts:
        if m is None:
            e = bpy.data.objects.new(name, None)
            e.empty_display_size = 0.05
            cols["EXPORT"].objects.link(e)
            o = Vector((-origin[0], -origin[1], origin[2]))            # turned to face +Y like the meshes
            e["kl_world_origin"] = list(o)
            par = made.get(parent, root) if parent else root
            e.parent = par
            e.location = o - Vector(par.get("kl_world_origin", (0, 0, 0)))
            made[name] = e
            continue
        tris += m.tris() if parent != "__collision__" else 0
        if parent == "__collision__":
            ob, _, _ = m.to_object(name, mat, cols["COLLISION"], origin=origin, parent=root, turn180=True)
            ob.display_type = 'WIRE'
            ob.hide_render = True
        else:
            par = made.get(parent, root) if parent else root
            ob, _, _ = m.to_object(name, mat, cols["EXPORT"], origin=origin, parent=par, turn180=True)
        made[name] = ob
    return root, list(made.values()), tris
