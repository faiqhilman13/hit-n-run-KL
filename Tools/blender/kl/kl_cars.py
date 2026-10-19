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

    def colr(q):   # colour may be a function of the face centre (two-tone liveries)
        return color(sum((v.co for v in q), Vector()) / len(q)) if callable(color) else color

    for i in range(ns):
        for j in range(ny):
            q = [grid_o[i][j], grid_o[i][j + 1], grid_o[i + 1][j + 1], grid_o[i + 1][j]]
            _face(m, quad(q), colr(q), smooth)
            if thick > 0:
                qi = [grid_i[i][j], grid_i[i + 1][j], grid_i[i + 1][j + 1], grid_i[i][j + 1]]
                _face(m, quad(qi), inner or colr(qi), smooth)
    if thick > 0:   # rim around the slab
        border = ([(0, j) for j in range(ny + 1)] + [(i, ny) for i in range(1, ns + 1)] +
                  [(ns, j) for j in range(ny - 1, -1, -1)] + [(i, 0) for i in range(ns - 1, 0, -1)])
        for k in range(len(border)):
            (i0, j0), (i1, j1) = border[k], border[(k + 1) % len(border)]
            q = [grid_o[i0][j0], grid_i[i0][j0], grid_i[i1][j1], grid_o[i1][j1]]
            _face(m, quad(q), colr(q), False)


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
    two = callable(color_fn) and color_fn.__code__.co_argcount >= 2     # colour per (profile segment, sector)
    for i in range(n):
        A, B = rings[i], rings[(i + 1) % len(rings)]
        colr = color_fn(i) if callable(color_fn) and not two else color_fn
        for k in range(seg):
            if two:
                colr = color_fn(i, k)
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


# ============================================================================== small geometry
def _frame(n, up=None):
    """(u, v, n): u runs left-to-right as seen by someone facing the surface, v up it."""
    n = Vector(n).normalized()
    up = Vector(up) if up is not None else (Vector((0, 0, 1)) if abs(n.z) < 0.92 else Vector((0, -1, 0)))
    u = up.cross(n)
    if u.length < 1e-6:
        u = Vector((1, 0, 0))
    u.normalize()
    return u, n.cross(u).normalized(), n


def quad(m, c, u, v, color, smooth=False):
    """Flat quad centred on c with half-extent vectors u, v; faces u x v."""
    c = Vector(c)
    vs = [m.bm.verts.new(c - u - v), m.bm.verts.new(c + u - v), m.bm.verts.new(c + u + v), m.bm.verts.new(c - u + v)]
    return _face(m, vs, color, smooth)


def disc_on(m, c, n, r, color, seg=12, rx=None):
    """Flat disc (or ellipse, rx) facing n."""
    if m.cull > 0 and 2 * r < m.cull:
        return
    seg = max(6, int(seg * K.LOD_DETAIL))
    u, v, n = _frame(n)
    rx = rx or r
    c = Vector(c)
    vs = [m.bm.verts.new(c + u * rx * math.cos(k / seg * math.tau) + v * r * math.sin(k / seg * math.tau)) for k in range(seg)]
    mid = m.bm.verts.new(c)
    for k in range(seg):
        _face(m, [vs[k], vs[(k + 1) % seg], mid], color, False)


def ring_on(m, c, n, r0, r1, color, seg=16):
    """Flat annulus facing n (bezels, tyre lettering bands)."""
    if m.cull > 0 and 2 * r1 < m.cull:
        return
    seg = max(6, int(seg * K.LOD_DETAIL))
    u, v, n = _frame(n)
    c = Vector(c)
    a = [m.bm.verts.new(c + (u * math.cos(k / seg * math.tau) + v * math.sin(k / seg * math.tau)) * r0) for k in range(seg)]
    b = [m.bm.verts.new(c + (u * math.cos(k / seg * math.tau) + v * math.sin(k / seg * math.tau)) * r1) for k in range(seg)]
    for k in range(seg):
        j = (k + 1) % seg
        _face(m, [a[k], a[j], b[j], b[k]], color, False)


def bar(m, p0, p1, w, t, color, n=None):
    """A flat strip (box) from p0 to p1, w wide across, t thick along n."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    if d.length < 1e-6 or m._small(d.length, w, t):
        return
    x = d.normalized()
    n = Vector(n).normalized() if n is not None else (Vector((0, 0, 1)) if abs(x.z) < 0.9 else Vector((0, 1, 0)))
    y = n.cross(x).normalized()
    z = x.cross(y)
    R = Matrix((x, y, z)).transposed()
    m.box((p0 + p1) / 2, (d.length, w, t), color, rot=R)


# ------------------------------------------------------------------------------ text (number plates, liveries)
_TEXT = {}


def text_tris(txt, bold=0.04):
    """Triangles of txt in Blender's built-in font, centred, normalised to cap height 1."""
    key = (txt, bold)
    if key in _TEXT:
        return _TEXT[key]
    cu = bpy.data.curves.new("__kl_txt", 'FONT')
    cu.body = txt
    cu.align_x, cu.align_y = 'CENTER', 'CENTER'
    cu.size = 1.0
    cu.resolution_u = 2
    cu.offset = bold
    ob = bpy.data.objects.new("__kl_txt", cu)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    tris = []
    for f in bm.faces:
        f.normal_update()
        if abs(f.normal.z) < 0.5:
            continue                      # (no extrusion, but be safe)
        t = [(v.co.x, v.co.y) for v in f.verts]
        tris.append(t if f.normal.z > 0 else list(reversed(t)))
    bm.free()
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.curves.remove(cu)
    bpy.data.meshes.remove(me)
    xs = [p[0] for t in tris for p in t]
    ys = [p[1] for t in tris for p in t]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    h = max(1e-6, max(ys) - min(ys))
    out = ([[((x - cx) / h, (y - cy) / h) for x, y in t] for t in tris], (max(xs) - min(xs)) / h)
    _TEXT[key] = out
    return out


def put_text(m, txt, c, n, height, color, max_w=None, up=None, place=None, bold=0.04):
    """Flat text at c facing n, letters `height` tall (squeezed to max_w). place(p) may move each
    vertex (to wrap it onto a curved panel)."""
    if m.cull > 0:
        return
    tris, aspect = text_tris(txt, bold)
    h = height
    sx = 1.0
    if max_w and aspect * h > max_w:
        sx = max_w / (aspect * h)
    u, v, n = _frame(n, up)
    c = Vector(c)
    for t in tris:
        vs = []
        for x, y in t:
            p = c + u * x * h * sx + v * y * h
            vs.append(m.bm.verts.new(place(p) if place else p))
        _face(m, vs, color, False)


def plate(m, c, w=0.44, h=0.12, facing=-1, text="WXY 1234", frame="trim_black"):
    """Malaysian plate: white characters on black, one line, in a thin surround."""
    c = Vector(c)
    n = Vector((0, facing, 0))
    m.box(c, (w + 0.02, 0.012, h + 0.02), frame)
    m.box(c + n * 0.004, (w, 0.012, h), "plate_black")
    put_text(m, text, c + n * 0.0105, n, h * 0.6, "plate_white", max_w=w * 0.9)


def side_text(m, sh, txt, y, z, height, side, color, off=0.012, max_w=None):
    """Livery lettering wrapped onto the side of shell sh, centred at (y, z), reading front to back
    on either side as a passer-by sees it."""
    n = Vector((side, 0, 0))

    def place(p):
        q = shell_s(sh, p.y, p.z)
        return sh.point(p.y, q, side) + sh.normal(p.y, q, side) * off
    put_text(m, txt, Vector((side, y, z)), n, height, color, max_w=max_w, place=place, bold=0.06)


def runs_clear(spec, y0, y1, z, pad=0.03):
    """[y0, y1] minus the wheel arches at height z: the pieces a side strip / moulding can run along."""
    cuts = []
    zc = spec.wr + spec.arch_lift
    R = spec.wr + spec.arch_gap + pad
    for yc in (spec.fy, spec.ry):
        if z < zc + R:
            hc = math.sqrt(max(0.0, R * R - (z - zc) ** 2)) if z > zc else R
            cuts.append((yc - hc, yc + hc))
    out, a = [], y0
    for c0, c1 in sorted(cuts):
        if c1 <= a or c0 >= y1:
            continue
        if c0 > a + 0.02:
            out.append((a, c0))
        a = max(a, c1)
    if y1 > a + 0.02:
        out.append((a, y1))
    return out


def side_strip(m, sh, spec, y0, y1, s0, s1, color, side, off=0.008, thick=0.0, per_m=6):
    """A moulding / pinstripe along the flank, broken where it meets the wheel arches."""
    z = sh.point((y0 + y1) / 2, (s0 + s1) / 2, side).z
    for a, b in runs_clear(spec, y0, y1, z):
        patch(m, sh, a, b, s0, s1, color, side, off=off, ny=max(1, int((b - a) * per_m)), ns=1, thick=thick)


SEAMS = False     # the cartoon look keeps panels clean: only the doors show their shut lines


def seam(m, sh, y0, y1, s0, s1, side, color="well_black", off=0.0025, ny=4, ns=1):
    """A shut line / panel gap: a thin dark strip painted on the shell (off in the clean cartoon look)."""
    if not SEAMS:
        return
    patch(m, sh, y0, y1, s0, s1, color, side, off=off, ny=ny, ns=ns)


def shell_top_z(sh, x, y):
    """Height of the top surface of shell sh above (x, y) (bisection on the cross-section)."""
    lo, hi = 0.0, 1.0          # s: 0 = widest line, 1 = top centre; |x| falls as s rises
    for _ in range(24):
        mid = (lo + hi) / 2
        if abs(sh.point(y, mid, 1).x) > abs(x):
            lo = mid
        else:
            hi = mid
    return sh.point(y, (lo + hi) / 2, 1).z


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
    of the sheet metal, seen through the openings. interior may be a function of the centre."""
    import bmesh as bm_
    bm.faces.ensure_lookup_table()
    dead = [f for f in bm.faces if not keep(f.calc_center_median(), f.normal)]
    bm_.ops.delete(bm, geom=dead, context='FACES')
    if interior:
        uv = bm.loops.layers.uv.active or bm.loops.layers.uv[0]
        for f in list(bm.faces):
            c = f.calc_center_median()
            if interior_fn and not interior_fn(c):
                continue
            u = K.pal_uv(interior(c) if callable(interior) else interior)
            # its own vertices (a face over the same verts would be the same face to bmesh)
            nf = bm.faces.new([bm.verts.new(v.co) for v in reversed(f.verts)])
            nf.smooth = f.smooth
            for lp in nf.loops:
                lp[uv].uv = u
    return bm


# ============================================================================== wheels
def tyre(m, c, r, w, side, ri, seg=24, blocks=False):
    """Fat cartoon tyre at hub c (axis X): ballooning sidewalls, a soft round shoulder, a flat tread. The
    outer radius stays exactly r (the game reads the wheel radius from the wheel mesh). blocks: chunky tread
    blocks painted round the shoulders (off-road)."""
    a = w / 2
    prof = [(ri * 1.01, -a + 0.01), (ri + (r - ri) * 0.5, -a - 0.016), (r - 0.04, -a - 0.008), (r - 0.012, -a + 0.012),
            (r, -a + 0.04), (r, a - 0.04), (r - 0.012, a - 0.012), (r - 0.04, a + 0.008), (ri + (r - ri) * 0.5, a + 0.016),
            (ri * 1.01, a - 0.01)]

    def col(i, k):
        if i in (0, 1, 7, 8):
            return "tyre_wall"
        if i in (3, 5) and blocks:
            return "well_black" if k % 2 else "tyre"
        if i == 9:
            return "rim_dark"
        return "tyre"
    revolve(m, c, prof, col, seg=seg, side=side)


def rim(m, c, r, w, side, ri, style="alloy", spokes=5, paint="car_paint", seg=20):
    """Cartoon rim at hub c, outer face toward side, inside radius ri: a dark well behind, a bold lip, then
    chunky spokes (alloy / off-road) or a dished steel face with a big hubcap (steel / steel_paint)."""
    c = Vector(c)
    X = Vector((side, 0, 0))
    face = w / 2 - 0.018
    dark = style in ("offroad", "alloy_dark")
    rimc = "rim_dark" if dark else "rim_silver"
    # the dark well behind the spokes (closed, so you never see through the wheel) and the rim lip
    disc(m, c, ri * 0.98, "well_black", side, axial=face - 0.06, seg=seg)
    revolve(m, c, [(ri * 0.99, face - 0.06), (ri * 0.99, face - 0.01), (ri * 1.0, face + 0.002), (ri * 0.88, face + 0.006)],
            lambda i: ["rim_dark", rimc, rimc][min(i, 2)], seg=seg, side=side, closed=False)
    if style in ("steel", "steel_paint"):
        # pressed steel dish with four bold slots, then the cap
        revolve(m, c, [(ri * 0.88, face + 0.004), (ri * 0.8, face - 0.004), (ri * 0.55, face + 0.004), (ri * 0.38, face + 0.012)],
                lambda i: "rim_silver", seg=seg, side=side, closed=False)
        for k in range(4):
            a = k / 4 * math.tau + math.pi / 4
            d = Vector((0, math.cos(a), math.sin(a)))
            quad(m, c + X * (face + 0.0) + d * ri * 0.68, Vector((0, -d.z, d.y)) * 0.05 * -side, d * 0.03, "well_black")
        if style == "steel_paint":    # a fat painted centre cap
            revolve(m, c, [(ri * 0.4, face + 0.01), (ri * 0.36, face + 0.034), (ri * 0.24, face + 0.044)],
                    lambda i: paint, seg=16, side=side, closed=False)
            disc(m, c, ri * 0.24, paint, side, axial=face + 0.044, seg=12)
            disc(m, c, ri * 0.1, "chrome", side, axial=face + 0.0445, seg=8)
        else:                          # a big shiny chrome dome
            revolve(m, c, [(ri * 0.86, face + 0.004), (ri * 0.78, face + 0.026), (ri * 0.5, face + 0.044), (ri * 0.2, face + 0.052)],
                    lambda i: "chrome", seg=seg, side=side, closed=False)
            disc(m, c, ri * 0.2, "chrome", side, axial=face + 0.052, seg=12)
            disc(m, c, ri * 0.1, "rim_dark", side, axial=face + 0.0525, seg=8)
        return
    # cast alloy: a few chunky, dished spokes and a big centre cap
    n = spokes if spokes <= 6 else 6
    hub_r = ri * 0.3
    for k in range(n):
        a = k / n * math.tau + 0.3
        d = Vector((0, math.cos(a), math.sin(a)))
        tside = Vector((0, -d.z, d.y))
        w0, w1 = (ri * 0.22, ri * 0.15) if style != "offroad" else (ri * 0.28, ri * 0.22)
        p0 = c + X * (face + 0.016) + d * hub_r * 0.8
        p1 = c + X * (face - 0.004) + d * ri * 0.92
        _slab(m, [p0 - tside * w0, p0 + tside * w0, p1 + tside * w1, p1 - tside * w1], X * -0.022, rimc, side)
    m.cyl(c + X * (face + 0.012), hub_r, hub_r * 0.9, 0.036, rimc, seg=14, rot=AX)
    disc(m, c, hub_r * 0.62, "chrome", side, axial=face + 0.031, seg=12)


def _slab(m, corners, depth, color, side):
    """Extrude a quad (4 points, outer face) by the vector depth into a closed slab."""
    if m.cull > 0:
        a, b, c_, d = corners
        if min((a - b).length, (b - c_).length) < m.cull:
            return
    top = [m.bm.verts.new(p) for p in corners]
    bot = [m.bm.verts.new(p + depth) for p in corners]
    order = (lambda q: q) if side > 0 else (lambda q: list(reversed(q)))
    f = _face(m, order(top), color, False)
    if f is not None:
        f.normal_update()
    if f is not None and f.normal.dot(depth) > 0:     # make sure the outer face looks outward
        f.normal_flip()
    fb = _face(m, order(list(reversed(bot))), color, False)
    if fb is not None:
        fb.normal_update()
    if fb is not None and fb.normal.dot(depth) < 0:
        fb.normal_flip()
    for i in range(4):
        j = (i + 1) % 4
        q = [top[i], bot[i], bot[j], top[j]]
        fq = _face(m, q, color, False)
        if fq is not None:
            fq.normal_update()
            mid = sum((v.co for v in q), Vector()) / 4
            cen = sum((v.co for v in top + bot), Vector()) / 8
            if fq.normal.dot(mid - cen) < 0:
                fq.normal_flip()


def car_wheel(parts, name, x, y, r, w, style, spokes=5, knobbly=False, paint="car_paint", rim_frac=0.64):
    """Wheel_XX (tyre: always drawn, carries the wheel-radius contract) + Rim_XX child (rim, spokes,
    brake: becomes Rim_XX_LOD0 / _LOD1)."""
    side = 1 if x > 0 else -1
    c = (x, y, r)
    ri = r * rim_frac
    w = w * 1.15                      # chunky cartoon tyres (the radius - the physics contract - is unchanged)
    tm = K.Mesher(1.0)
    tyre(tm, c, r, w, side, ri, seg=20, blocks=knobbly)
    if knobbly:   # chunky off-road blocks standing proud of the tread
        n = max(10, int(22 * K.LOD_DETAIL))
        for k in range(n):
            a = k / n * math.tau
            for lane in (-1, 1):
                off = lane * w * 0.22 + (0.025 if k % 2 else -0.025)
                p = Vector(c) + Vector((off * side, (r + 0.01) * math.cos(a), (r + 0.01) * math.sin(a)))
                tm.box(p, (w * 0.34, 0.075, 0.03), "tyre", rot=(a + math.pi / 2, 0, 0))
    parts.append((name, tm, c, None))
    rm = K.Mesher(1.0)
    rim(rm, c, r, w, side, ri, style=style, spokes=spokes, paint=paint)
    parts.append(("Rim_" + name[-2:], rm, c, name))


# ============================================================================== cabin bits
def steering_wheel(parts, hub, radius, tilt, parent="Body", pad="dash_grey"):
    """Steering wheel (pivot on the hub) plus an axis marker. tilt: column angle from
    horizontal (radians); the wheel faces the driver (+Y). The rim (radius 0.17, tube 0.018) is the
    widest part of the mesh: VehicleVisuals reads the grip radius from it, so nothing else reaches out
    that far."""
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
    # three broad flat spokes and a padded centre with a plain round badge
    for a in (0.0, math.pi, -math.pi / 2):
        r0, r1 = 0.045, radius - 0.012
        d = R @ Vector((math.cos(a), 0, math.sin(a)))
        M = R @ Matrix.Rotation(-a, 3, 'Y')
        sw.box(hub + d * (r0 + r1) / 2 - axis * 0.004, (r1 - r0, 0.016, 0.05 if a < 0 else 0.04), "dash_grey", rot=M)
    rbox(sw, hub + axis * 0.012, (0.13, 0.05, 0.1), pad, rot=(-tilt, 0, 0), r=0.7, segs=2)
    disc_on(sw, hub + axis * 0.0375, axis, 0.018, "chrome", seg=10)
    parts.append(("SteeringWheel", sw, tuple(hub), parent))
    parts.append(("SteeringAxis", None, tuple(hub + axis * 0.1), "SteeringWheel"))
    col = K.Mesher(1.0)
    col.tube(hub - axis * 0.02, hub - axis * 0.13, 0.032, 0.04, "trim_black", seg=10)   # column shroud (on Body)
    # indicator / wiper stalks either side of the column
    for sgn in (1, -1):
        p = hub - axis * 0.07 + (R @ Vector((sgn * 0.04, 0, 0)))
        col.tube(p, p + R @ Vector((sgn * 0.11, 0.0, 0.01)), 0.006, 0.005, "trim_black", seg=5)
    return col


def seat(m, x, y, z, w=0.46, back=0.55, color="seat_grey", bolster=None, recline=0.22, headrest=True):
    """Bucket seat: bolstered cushion and backrest, a headrest on two posts, a dark base."""
    bol = bolster or color
    rbox(m, (x, y, z), (w * 0.66, 0.48, 0.11), color, r=0.5, segs=1)
    for sgn in (-1, 1):
        rbox(m, (x + sgn * w * 0.4, y, z + 0.02), (w * 0.2, 0.48, 0.15), bol, r=0.6, segs=1)
    m.box((x, y + 0.02, z - 0.1), (w * 0.8, 0.4, 0.08), "trim_black")
    R = Matrix.Rotation(-recline, 3, 'X')
    up = R @ Vector((0, 0, 1))
    base = Vector((x, y + 0.24, z))
    rbox(m, base + up * back / 2, (w * 0.66, 0.11, back), color, rot=(-recline, 0, 0), r=0.5, segs=1)
    for sgn in (-1, 1):
        rbox(m, base + up * back * 0.46 + Vector((sgn * w * 0.4, -0.02, 0)), (w * 0.2, 0.15, back * 0.9), bol,
             rot=(-recline, 0, 0), r=0.6, segs=1)
    if headrest:
        rbox(m, base + up * (back + 0.1), (w * 0.52, 0.11, 0.17), bol, rot=(-recline, 0, 0), r=0.7, segs=2)


def bench(m, y, z, width, back=0.52, color="seat_grey", bolster=None, recline=0.2, heads=2):
    bol = bolster or color
    rbox(m, (0, y, z), (width, 0.46, 0.14), color, r=0.5, segs=1)
    R = Matrix.Rotation(-recline, 3, 'X')
    up = R @ Vector((0, 0, 1))
    base = Vector((0, y + 0.23, z))
    rbox(m, base + up * back / 2, (width, 0.12, back), color, rot=(-recline, 0, 0), r=0.5, segs=1)
    for k in range(heads):
        x = (k - (heads - 1) / 2) * width / heads
        rbox(m, base + up * (back + 0.1) + Vector((x, 0, 0)), (0.26, 0.09, 0.13), bol, rot=(-recline, 0, 0), r=0.6, segs=2)
    for sgn in (-1, 1):   # door-side armrest bolsters
        rbox(m, (sgn * (width / 2 - 0.04), y + 0.05, z + 0.1), (0.08, 0.36, 0.08), bol, r=0.6, segs=1)


def dashboard(m, s, top="dash_grey", trim="trim_black", accent="rim_silver"):
    """A chunky cartoon dash: a soft slab with a padded top, an instrument hood with two big gauges for the
    driver (on the right, -X), a centre stack with a radio, a console with a ball-topped gear lever. It stays
    behind the windscreen base (dash_y - 0.02) and keeps its face at dash_y + 0.17."""
    W, dy, dz, sx = s.width, s.dash_y, s.dash_z, s.seat_x
    face = dy + 0.17
    rbox(m, (0, dy + 0.075, dz - 0.01), (W - 0.22, 0.19, 0.2), top, r=0.7, segs=2)
    rbox(m, (0, dy + 0.06, dz + 0.085), (W - 0.3, 0.15, 0.03), "charcoal", r=0.8, segs=1)        # padded top
    rbox(m, (-sx, dy + 0.1, dz + 0.105), (0.34, 0.14, 0.085), trim, r=0.6, segs=2)               # instrument hood
    gn = Vector((0, 1, 0.45)).normalized()
    for k in (-1, 1):
        g = Vector((-sx + k * 0.075, dy + 0.172, dz + 0.11))
        disc_on(m, g, gn, 0.045, "white", seg=14)
        ring_on(m, g + gn * 0.001, gn, 0.045, 0.054, accent, seg=14)
        bar(m, g + gn * 0.003, g + gn * 0.003 + Vector((k * 0.026, 0, 0.014)), 0.007, 0.002, "tail_red", n=gn)
    rbox(m, (0, face - 0.03, dz - 0.1), (0.26, 0.1, 0.3), top, r=0.5, segs=1)                    # centre stack
    rbox(m, (0, face + 0.022, dz - 0.03), (0.2, 0.014, 0.07), trim, r=0.3, segs=1)                # radio
    m.box((0, face + 0.03, dz - 0.03), (0.1, 0.004, 0.03), "lens")
    for k in (-1, 1):
        disc_on(m, (k * 0.1, face + 0.003, dz + 0.05), (0, 1, 0), 0.03, "well_black", seg=10)    # round vents
    cy = s.seat_y - 0.1                                                                            # console + lever
    rbox(m, (0, cy, s.floor_z + 0.14), (0.2, 0.75, 0.14), top, r=0.5, segs=1)
    m.tube((0, cy - 0.2, s.floor_z + 0.22), (0, cy - 0.22, s.floor_z + 0.38), 0.01, 0.01, accent, seg=6)
    m.ball((0, cy - 0.22, s.floor_z + 0.4), (0.03, 0.03, 0.03), trim, seg=8, rings=5)


def rear_mirror(m, p):
    p = Vector(p)
    m.tube(p, p + Vector((0, 0.03, -0.05)), 0.006, 0.006, "trim_black", seg=5)
    rbox(m, p + Vector((0, 0.035, -0.08)), (0.2, 0.025, 0.055), "trim_black", r=0.6, segs=1)
    m.box(p + Vector((0, 0.049, -0.08)), (0.18, 0.004, 0.04), "chrome")


def mirror_arm(m, p, side, color="car_paint", size=1.0):
    """Door mirror: a black sail at the window corner, an arm, a rounded housing in `color` with an
    amber repeater strip, and the (chrome-tinted) glass facing back."""
    p = Vector(p)
    s = size
    rbox(m, p + Vector((side * 0.012, 0.02, 0)), (0.03, 0.09 * s, 0.07 * s), "trim_black", r=0.5, segs=1)
    hc = p + Vector((side * 0.13 * s, 0.035, 0.04 * s))
    bar(m, p + Vector((side * 0.02, 0.02, 0.0)), hc - Vector((side * 0.05 * s, 0, 0.02)), 0.035 * s, 0.022 * s, "trim_black")
    rbox(m, hc, (0.15 * s, 0.07 * s, 0.1 * s), color, r=0.8, segs=2)
    rbox(m, hc + Vector((0, 0.036 * s, 0)), (0.13 * s, 0.01, 0.08 * s), "chrome", r=0.6, segs=1)
    rbox(m, hc + Vector((side * 0.02 * s, -0.035 * s, -0.025 * s)), (0.09 * s, 0.008, 0.016 * s), "lamp_amber", r=0.6, segs=1)


def wiper(m, p0, p1, n):
    """A wiper resting on the glass from pivot p0 toward p1 (n: glass normal, outward): one chunky black blade."""
    p0, p1, n = Vector(p0), Vector(p1), Vector(n).normalized()
    m.cyl(p0 + n * 0.012, 0.018, 0.016, 0.024, "trim_black", seg=8, rot=n.to_track_quat('Z', 'Y'))
    bar(m, p0 + n * 0.016, p1 + n * 0.012, 0.022, 0.014, "trim_black", n=n)


def exhaust(m, p, r=0.032, length=0.16):
    """Tail pipe ending at p, pointing back (+Y): chrome tip with a dark mouth, a hanger."""
    p = Vector(p)
    m.cyl(p - Vector((0, length / 2, 0)), r, r * 1.05, length, "chrome", seg=12, rot=(math.pi / 2, 0, 0))
    disc_on(m, p + Vector((0, 0.001, 0)), (0, 1, 0), r * 0.78, "well_black", seg=12)
    m.cyl(p - Vector((0, length + 0.1, 0)), r * 0.8, r * 0.8, 0.2, "metal_dark", seg=8, rot=(math.pi / 2, 0, 0))


def fuel_cap(m, sh, y, s_, side, r=0.06, color="car_paint"):
    p = sh.point(y, s_, side)
    n = sh.normal(y, s_, side)
    disc_on(m, p + n * 0.003, n, r + 0.006, "well_black", seg=14)
    disc_on(m, p + n * 0.0045, n, r, color, seg=14)
    disc_on(m, p + n * 0.005 + Vector((0, -side * 0.0, 0)) + (Vector((0, 1, 0)) * (r * 0.6)), n, 0.008, "well_black", seg=6)


def handle_on(m, sh, y, s_, side, color="trim_black", length=0.12, off=0.008):
    """Door pull: a dark recess with a handle bar over it."""
    p = sh.point(y, s_, side)
    n = sh.normal(y, s_, side)
    u = Vector((0, 1, 0))
    v = n.cross(u).normalized()
    quad(m, p + n * (off + 0.002), u * length * 0.55 * (1 if side < 0 else -1), v * 0.022, "well_black")
    rbox(m, p + n * (off + 0.012), (0.018, length, 0.026), color, r=0.6, segs=1)


def grille(m, c, w, h, facing=-1, bars=4, bar_color="trim_black", back="grille_black", frame=None, vbars=0, depth=0.04):
    """A flat grille on a front face: dark back, horizontal (and vertical) bars, optional surround."""
    c = Vector(c)
    n = Vector((0, facing, 0))
    m.box(c, (w, depth, h), back)
    for k in range(bars):
        z = c.z - h / 2 + h * (k + 0.5) / bars
        rbox(m, (c.x, c.y + facing * depth * 0.5, z), (w * 0.96, 0.014, h / bars * 0.38), bar_color, r=0.5, segs=1)
    for k in range(vbars):
        x = c.x - w / 2 + w * (k + 0.5) / vbars
        m.box((x, c.y + facing * depth * 0.45, c.z), (0.01, 0.012, h * 0.94), bar_color)
    if frame:
        for zz in (c.z - h / 2, c.z + h / 2):
            rbox(m, (c.x, c.y + facing * depth * 0.55, zz), (w + 0.03, 0.02, 0.022), frame, r=0.5, segs=1)
        for xx in (c.x - w / 2, c.x + w / 2):
            rbox(m, (xx, c.y + facing * depth * 0.55, c.z), (0.022, 0.02, h + 0.03), frame, r=0.5, segs=1)


def honeycomb(m, c, w, h, facing=-1, cell=0.045, color="rim_dark", back="well_black"):
    """Lower intake: a dark mouth with two bold bars across it."""
    c = Vector(c)
    m.box(c, (w, 0.02, h), back)
    for k in (-1, 1):
        rbox(m, (c.x, c.y + facing * 0.014, c.z + k * h * 0.22), (w * 0.94, 0.014, h * 0.16), color, r=0.6, segs=1)


def badge(m, c, facing=-1, kind="oval", size=1.0):
    """A plain, made-up maker's badge (no real marque)."""
    c = Vector(c)
    n = Vector((0, facing, 0))
    if kind == "oval":
        disc_on(m, c, n, 0.032 * size, "chrome", seg=14, rx=0.055 * size)
        disc_on(m, c + n * 0.002, n, 0.022 * size, "rim_dark", seg=12, rx=0.042 * size)
        disc_on(m, c + n * 0.003, n, 0.012 * size, "chrome", seg=10, rx=0.012 * size)
    else:   # shield
        m.prism(c, (0.09 * size, 0.012, 0.1 * size), "chrome", rot=(math.pi / 2, 0, math.pi if facing < 0 else 0))
        disc_on(m, c + n * 0.008 + Vector((0, 0, 0.035 * size)), n, 0.016 * size, "tail_red", seg=8)


def lamp_lens(m, c, size, color, facing=-1, r=0.5, ribs=0, rib_color="lens_clear", bezel=None):
    """A lamp lens (rounded box) on a flat face, with optional ribs and a bezel behind it."""
    c = Vector(c)
    if bezel:
        rbox(m, c - Vector((0, facing * 0.012, 0)), (size[0] + 0.03, size[1], size[2] + 0.03), bezel, r=0.35, segs=1)
    rbox(m, c, size, color, r=r, segs=1)
    for k in range(ribs):
        x = c.x - size[0] / 2 + size[0] * (k + 0.5) / ribs
        m.box((x, c.y + facing * size[1] * 0.52, c.z), (0.008, 0.004, size[2] * 0.8), rib_color)


def ornament(parts, hang, parent="Body", kind="tasbih"):
    """Something swinging from the rear-view mirror (pivot at the hanging point)."""
    o = K.Mesher(1.0)
    hang = Vector(hang)
    if kind == "tasbih":
        for k in range(10):
            a = k / 10 * math.tau
            p = hang + Vector((0.035 * math.sin(a), 0, -0.07 - 0.07 * (1 - math.cos(a)) * 0.5))
            o.ball(p, (0.011, 0.011, 0.011), "tasbih_brown", seg=6, rings=4)
        o.ball(hang + Vector((0, 0, -0.16)), (0.012, 0.012, 0.022), "gold", seg=6, rings=4)
        o.tube(hang, hang + Vector((0, 0, -0.06)), 0.002, 0.002, "tasbih_brown", seg=4)
    else:   # little pine-tree air freshener
        o.tube(hang, hang + Vector((0, 0, -0.06)), 0.002, 0.002, "white", seg=4)
        o.prism(hang + Vector((0, 0, -0.16)), (0.06, 0.006, 0.1), "fresh_green", rot=(math.pi / 2, 0, 0))
    parts.append(("Ornament", o, tuple(hang), parent))


def antenna(parts, base, length=0.55, parent="Body", lean=0.35):
    a = K.Mesher(1.0)
    base = Vector(base)
    tip = base + Vector((0, math.sin(lean) * length, math.cos(lean) * length))
    a.cyl(base + Vector((0, 0, 0.01)), 0.018, 0.014, 0.03, "trim_black", seg=10)
    a.tube(base, tip, 0.005, 0.003, "chrome", seg=5)
    a.ball(tip, (0.009, 0.009, 0.009), "trim_black", seg=6, rings=4)
    parts.append(("Antenna", a, tuple(base), parent))


def shark_fin(m, p, length=0.16):
    p = Vector(p)
    m.poly_extrude([(-length / 2, 0), (length / 2, 0), (length * 0.3, 0.05), (-length * 0.1, 0.065)], 0.05, "trim_black",
                   loc=p, rot=(0, 0, math.pi / 2))


# ============================================================================== optional parts (variety)
# A busy street mustn't look like clones. Optional parts are exported switched off (ModelImportPipeline) and
# VehicleVariety (Scripts/Vehicles) turns some on from a seed. Naming: Var_<Group>_<Option>_<Where>[_LOD0];
# per group the game picks none or one option, and every node of that option comes on together. _LOD0 parts
# vanish with the rest of the full-detail car at a distance; the others stay (roof racks, loads, boxes).
import random as _random


def var_part(ctx, group, option, where, fn, parent="Body", near_only=False):
    m = K.Mesher(1.0)
    fn(m)
    if m.tris() == 0:
        return
    name = f"Var_{group}_{option}_{where}" + ("_LOD0" if near_only else "")
    ctx["parts"].append((name, m, None, parent))


def roof_rack(m, cab, y0, y1, lift=0.05, color="rim_silver"):
    """A chunky old-school roof rack: two side rails on four feet, three cross bars."""
    xr = cab.params((y0 + y1) / 2)[0] * 0.62
    zt = lambda y, x: shell_top_z(cab, x, y)
    for x in (xr, -xr):
        for y in (y0, y1):
            z = zt(y, x)
            rbox(m, (x, y, z + lift / 2), (0.05, 0.06, lift + 0.02), "trim_black", r=0.5, segs=1)
        m.tube((x, y0, zt(y0, x) + lift), (x, y1, zt(y1, x) + lift), 0.018, 0.018, color, seg=8)
    for k in range(3):
        y = y0 + (y1 - y0) * (k + 0.5) / 3
        z = max(zt(y, xr), zt(y, 0)) + lift
        m.tube((-xr, y, z), (xr, y, z), 0.014, 0.014, color, seg=8)


def mud(m, sh, spec, side, y0, y1, seed, s_lo=-0.75, s_hi=-0.2, n=14, colors=("olive_dark", "bronze")):
    """Dust and mud splashed along the bottom of a flank (and behind the wheels): irregular little patches."""
    rnd = _random.Random(seed)
    for k in range(n):
        y = rnd.uniform(y0, y1)
        ln = rnd.uniform(0.05, 0.22)
        top = rnd.uniform(s_lo + 0.1, s_hi)
        z = sh.point(y, top, side).z
        for a, b in runs_clear(spec, y - ln / 2, y + ln / 2, z, pad=0.02):
            patch(m, sh, a, b, s_lo, top, rnd.choice(colors), side, off=0.0035 + 0.0004 * k, ny=1, ns=2)


def door_mud(ctx, name, side, y0, y1, seed):
    sh, spec = ctx["shell"], ctx["spec"]
    var_part(ctx, "Dirt", "a", name.replace("_", ""), lambda m: mud(m, sh, spec, side, y0 + 0.02, y1 - 0.02, seed, s_lo=-0.42,
                                                                     s_hi=-0.15, n=6, colors=("olive_dark", "bronze")),
             parent=name, near_only=True)


def body_mud(ctx, ys, seed):
    """Var_Dirt for the body: the flanks outside the doors, and the tail."""
    sh, spec = ctx["shell"], ctx["spec"]

    def f(m):
        for side in (1, -1):
            for k, (a, b) in enumerate(ys):
                mud(m, sh, spec, side, a, b, seed + k * 7 + (side > 0) * 31)
    var_part(ctx, "Dirt", "a", "Body", f, near_only=True)


def p_sticker(m, p, n, size=0.09):
    """The red-on-white 'P' (probationary driver) square, or any square sticker."""
    p, n = Vector(p), Vector(n).normalized()
    u, v, _ = _frame(n)
    quad(m, p, u * size / 2, v * size / 2, "white")
    put_text(m, "P", p + n * 0.001, n, size * 0.75, "tail_red", bold=0.08)


def round_sticker(m, p, n, r=0.05, ring="gold", fill="police_navy"):
    disc_on(m, p, n, r, ring, seg=14)
    disc_on(m, Vector(p) + Vector(n).normalized() * 0.001, n, r * 0.78, fill, seg=14)
    put_text(m, "KL", Vector(p) + Vector(n).normalized() * 0.002, n, r * 0.7, "white", bold=0.06)


def shelf_stuff(m, c):
    """A tissue box with a pink cover and a little stuffed bear: what rides on a Malaysian parcel shelf."""
    c = Vector(c)
    rbox(m, c + Vector((0.18, 0, 0.04)), (0.24, 0.12, 0.08), "white", r=0.4, segs=1)
    rbox(m, c + Vector((0.18, 0, 0.085)), (0.25, 0.13, 0.02), "kurung_pink", r=0.4, segs=1)
    b = c + Vector((-0.2, 0, 0))
    m.ball(b + Vector((0, 0, 0.06)), (0.07, 0.06, 0.065), "ochre", seg=10, rings=6)
    m.ball(b + Vector((0, 0, 0.15)), (0.055, 0.05, 0.05), "ochre", seg=10, rings=6)
    for sx in (-1, 1):
        m.ball(b + Vector((sx * 0.04, 0, 0.2)), (0.02, 0.015, 0.02), "ochre_check", seg=6, rings=4)


def hubcap(m, c, r, w, side, ri):
    """A full plastic wheel trim (old cars): silver dish with bold slots."""
    c = Vector(c)
    X = Vector((side, 0, 0))
    face = w * 1.15 / 2 - 0.018
    revolve(m, c, [(ri * 1.02, face + 0.012), (ri * 0.9, face + 0.03), (ri * 0.45, face + 0.05), (ri * 0.15, face + 0.056)],
            lambda i: "rim_silver", seg=20, side=side, closed=False)
    disc(m, c, ri * 0.15, "rim_silver", side, axial=face + 0.056, seg=10)
    for k in range(6):
        a = k / 6 * math.tau
        d = Vector((0, math.cos(a), math.sin(a)))
        quad(m, c + X * (face + 0.044) + d * ri * 0.62, Vector((0, -d.z, d.y)) * 0.045 * -side, d * 0.022, "rim_dark")


def wheel_vars(ctx, group, option, fn_for_wheel):
    s = ctx["spec"]
    for name, x, y in (("Wheel_FL", s.track / 2, s.fy), ("Wheel_FR", -s.track / 2, s.fy),
                       ("Wheel_RL", s.track / 2, s.ry), ("Wheel_RR", -s.track / 2, s.ry)):
        var_part(ctx, group, option, name[-2:], lambda m, x=x, y=y: fn_for_wheel(m, (x, y, s.wr), 1 if x > 0 else -1),
                 parent=name, near_only=True)


def side_stripes(ctx, doors, y0, y1, z0, z1, color, option):
    """Decal stripes (between heights z0 and z1) along both flanks, carried across the doors:
    doors = [(door part name, y0, y1, side)]."""
    sh, spec = ctx["shell"], ctx["spec"]

    def strip(m, a, b, side, off):
        ym = (a + b) / 2
        side_strip(m, sh, spec, a, b, shell_s(sh, ym, z0), shell_s(sh, ym, z1), color, side, off=off)

    def body(m):
        for side in (1, -1):
            a = y0
            for _, d0, d1, _s in sorted([d for d in doors if d[3] == side], key=lambda d: d[1]) + [(None, y1, y1, side)]:
                if d0 > a + 0.02:
                    strip(m, a, d0, side, 0.004)
                a = max(a, d1)
    var_part(ctx, "Sticker", option, "Body", body)
    for name, d0, d1, side in doors:
        var_part(ctx, "Sticker", option, name.replace("_", ""),
                 lambda m, d0=d0, d1=d1, side=side: strip(m, d0 + 0.014, d1 - 0.014, side, 0.012), parent=name)


def door_name(key, side):
    return {"F": "Door_F", "R": "Door_R", "S": "SlideDoor_"}[key] + ("L" if side > 0 else "R")


def curtains(m, x, y0, y1, z0, z1, color="kurung_flower", fold="cream"):
    """Pleated curtains drawn back to both ends of a side window (seen from outside through the glass)."""
    for a, b in ((y0, y0 + (y1 - y0) * 0.22), (y1 - (y1 - y0) * 0.22, y1)):
        n = max(2, int((b - a) / 0.04))
        for k in range(n):
            yy = a + (b - a) * (k + 0.5) / n
            m.box((x - math.copysign(0.012 * (k % 2), x), yy, (z0 + z1) / 2), (0.02, (b - a) / n * 1.05, z1 - z0),
                  color if k % 2 else fold)
    m.box((x, (y0 + y1) / 2, z1 + 0.015), (0.025, y1 - y0, 0.03), "white")       # rail


# ============================================================================== generic car
class CarSpec:
    """Numbers that describe a car. All lengths in metres, front = -Y."""

    def __init__(self, **kw):
        self.__dict__.update(kw)

    def get(self, k, d=None):
        return self.__dict__.get(k, d)


def build_car(spec, body_fn):
    """Common assembly: body shell (+ arches), greenhouse, doors, wheels, interior, lamps.
    body_fn(ctx) adds the model-specific details. Parts:
      Body    the shells (and roof - the roof tests read it), pillars, door openings
      Detail  everything small on and in the car (becomes Detail_LOD0 / Detail_LOD1)"""
    s = spec
    parts = []
    core = K.Mesher(2.0)
    fine = K.Mesher(1.0)
    shell = Shell(s.stations)
    cab = Shell(s.cab_stations)
    _BUILD["body_holes"], _BUILD["cab_holes"] = [], []
    _BUILD["breaks_y"], _BUILD["breaks_s"] = [], []
    _BUILD["glass"] = K.Mesher(2.0)

    # details first: doors and windows register the openings the shells need
    ctx = dict(parts=parts, body=fine, core=core, shell=shell, cab=cab, spec=s)
    _BUILD["belt_color"] = "car_paint" if s.get("belt_s") is not None else None
    body_fn(ctx)

    def in_cab_footprint(c):
        if not (cab.y0 + 0.03 < c.y < cab.y1 - 0.03):
            return False
        return abs(c.x) < cab.params(c.y)[0] * 0.97

    # ---- body shell with arches cut out, door openings and the cabin floor opened up
    sm = K.Mesher(2.0)
    shell_mesh(sm, shell, ny=s.get("ny", 36), ns=14, color_fn=s.body_color, y_breaks=tuple(s.y_breaks) + tuple(_BUILD["breaks_y"]),
               s_breaks=tuple(s.s_breaks) + tuple(sorted(set(round(v, 4) for v in _BUILD["breaks_s"]))))
    arches = []
    for (x, y) in ((s.track / 2, s.fy), (-s.track / 2, s.fy), (s.track / 2, s.ry), (-s.track / 2, s.ry)):
        arches.append((math.copysign(s.width / 2 + 0.05, x), y, s.wr + s.arch_lift, s.wr + s.arch_gap, 0.42))
    bm = cut_arches(sm, arches)

    lod = K.LOD_CULL > 0          # building the distance stand-in: one closed, opaque shell, no cabin inside

    def keep_body(c, n):
        if lod:
            return True
        # doorways open the side skin only: the wheel-well roof (the arch cut, facing in / down) stays
        if abs(n.x) > 0.25 and abs(c.x) > shell.params(c.y)[0] - 0.12 and any(in_hole(shell, c, h) for h in _BUILD["body_holes"]):
            return False
        # the top of the tub under the cabin: open, so the cabin is one hollow space
        if n.z > 0.35 and in_cab_footprint(c) and c.z > shell.params(c.y)[2]:
            return False
        return True
    trim_in = s.get("trim_color", "interior")
    open_up(bm, keep_body, None if lod else trim_in, lambda c: cab.y0 - 0.15 < c.y < cab.y1 + 0.15 and c.z > s.floor_z)
    merge_bm(core, bm)

    # ---- greenhouse (cabin): bottom half removed (it sat inside the body), windows cut out
    cm = K.Mesher(2.0)
    cab_fn = s.cab_color
    if s.get("belt_s") is not None:      # body colour on the cabin band below the window line
        cab_fn = lambda c, f=s.cab_color, b=s.belt_s: "car_paint" if shell_s(cab, c.y, c.z) < b else f(c)
    shell_mesh(cm, cab, ny=24, ns=10, color_fn=cab_fn, y_breaks=s.cab_breaks)

    def keep_cab(c, n):
        if c.z < cab.params(c.y)[2] + 0.004:
            return False
        return lod or not any(in_hole(cab, c, h) for h in _BUILD["cab_holes"])
    head = s.get("headliner", "aircon")
    open_up(cm.bm, keep_cab, None if lod else (lambda c: head if c.z > s.roof_z - 0.18 else trim_in))
    if lod:   # windows painted on: dark glass where the openings would be
        paint_faces(cm.bm, lambda c: any(in_hole(cab, c, h) for h in _BUILD["cab_holes"]), "car_glass")
    merge_bm(core, cm.bm)
    if lod:
        return lod_standin(parts, core, fine, s)
    parts.append(("Glass", _BUILD["glass"], None, "Body"))

    # ---- interior: seats, dash, column, driver marker (front seats sit low: cartoon heads are big)
    iz = s.floor_z
    sc, sb = s.get("seat_color", "seat_grey"), s.get("seat_bolster")
    for x in (-s.seat_x, s.seat_x):
        seat(fine, x, s.seat_y, iz + 0.14, color=sc, bolster=sb)
    if s.get("rear_seat", True):
        by = s.seat_y + 0.85
        bw = s.width - 0.3
        if by + 0.35 > s.ry - (s.wr + s.arch_gap):       # it sits between the rear wheel wells: keep out of them
            bw = min(bw, 2 * (s.width / 2 + 0.05 - 0.42) - 0.06)
        bench(fine, by, iz + 0.28, bw, color=sc, bolster=sb)
    dashboard(fine, s, top=s.get("dash_color", "dash_grey"))
    fine.box((0, s.seat_y - 0.2 + s.get("carpet_shift", 0.0), iz + 0.06), (s.width - 0.2, s.get("carpet_len", 1.6), 0.04), "carpet")
    hub = Vector((-s.seat_x, s.dash_y + 0.2, s.dash_z + 0.16))
    colm = steering_wheel(parts, hub, 0.17, 0.42)
    merge_bm(fine, colm.bm)
    parts.append(("Seat_Driver", None, (-s.seat_x, s.seat_y - 0.02, iz + 0.21), "Body"))

    # ---- wheels
    for name, x, y in (("Wheel_FL", s.track / 2, s.fy), ("Wheel_FR", -s.track / 2, s.fy),
                       ("Wheel_RL", s.track / 2, s.ry), ("Wheel_RR", -s.track / 2, s.ry)):
        car_wheel(parts, name, x, y, s.wr, s.ww, s.wheel_style, spokes=s.get("spokes", 5), knobbly=s.get("knobbly", False),
                  paint=s.get("cap_paint", "car_paint"))

    # body first (pivot at axle height, centre) so its children can attach
    parts.insert(0, ("Body", core, (0, (s.fy + s.ry) / 2, s.wr + 0.1), None))
    parts.insert(1, ("Detail", fine, None, "Body"))
    col = K.Mesher()
    L = shell.y1 - shell.y0
    if s.get("col_box"):          # (centre, size) when the shell isn't the whole vehicle (a lorry's bed)
        col.box(*s.col_box, "rubber")
    else:
        col.box((0, (shell.y0 + shell.y1) / 2, (s.floor_z + s.roof_z) / 2 + 0.05), (s.width, L, s.roof_z - s.floor_z), "rubber")
    parts.append((f"COL_{s.id}", col, None, "__collision__"))
    return parts


def paint_faces(bm, pred, color):
    """Re-point the UVs of every face whose centre satisfies pred at palette cell `color`."""
    uv = bm.loops.layers.uv.active or bm.loops.layers.uv[0]
    u = K.pal_uv(color)
    for f in bm.faces:
        if pred(f.calc_center_median()):
            for lp in f.loops:
                lp[uv].uv = u


def low_wheel(m, c, r, w, side, rim="rim_silver"):
    """Distance tyre: a plain 10-sided drum with a rim-coloured face."""
    c = Vector(c)
    revolve(m, c, [(r * 0.62, -w / 2), (r * 0.92, -w / 2 - 0.005), (r, -w / 2 + 0.02), (r, w / 2 - 0.02),
                   (r * 0.92, w / 2 + 0.005), (r * 0.62, w / 2)], lambda i: "tyre", seg=22, side=side, closed=False)
    disc(m, c, r * 0.64, rim, side, axial=w / 2 - 0.01, seg=22)
    disc(m, c, r * 0.64, "rim_dark", -side, axial=w / 2 - 0.01, seg=22)


def lod_standin(parts, core, fine, s):
    """LOD mode: fold the shell, the closed door skins and the coarse outside detail into one "Body"
    (it becomes Body_LOD1, the whole car seen from afar) and give each wheel a plain tyre."""
    keep = []
    for name, m, origin, parent in parts:
        if m is not None and (name.startswith("Door_") or name.startswith("SlideDoor_")) and not name.endswith("_Glass"):
            merge_bm(core, m.bm)
        elif m is None or name in ("RoofSign", "LightBar", "HeadLights", "BrakeLights", "ReverseLights"):
            keep.append((name, m, origin, parent))
    merge_bm(core, fine.bm)
    out = [("Body", core, (0, (s.fy + s.ry) / 2, s.wr + 0.1), None)] + keep
    for name, x, y in (("Wheel_FL", s.track / 2, s.fy), ("Wheel_FR", -s.track / 2, s.fy),
                       ("Wheel_RL", s.track / 2, s.ry), ("Wheel_RR", -s.track / 2, s.ry)):
        wm = K.Mesher(1.0)
        low_wheel(wm, (x, y, s.wr), s.wr, s.ww, 1 if x > 0 else -1,
                  rim="rim_dark" if s.wheel_style in ("offroad", "alloy_dark") else "rim_silver")
        out.append((name, wm, (x, y, s.wr), None))
    return out


def door(ctx, name, side, y0, y1, zsill_s, zbelt_s, win_top_s, frame=True, glass_front=None, glass_rear=None,
         handle=True, color="car_paint", hinge_front=True, sliding=False, frame_color="trim_black"):
    """Door skin cut from the body shell (y0..y1, s from sill to belt) plus the window above the
    belt on the cabin shell. Parent = Body; pivot at the hinge (front edge, belt height).
    The body underneath is painted dark in the door opening, so the skin's inset edges leave
    a panel gap and an open door shows a dark doorway. color may be a function of the point."""
    sh, cab, s = ctx["shell"], ctx["cab"], ctx["spec"]
    d = K.Mesher(2.0)
    g = 0.012
    # the door skin runs from the sill right up to where the cabin starts (no slit under the glass)
    ztop = max(shell_s(sh, y, cab.params(y)[2] + 0.02) for y in (y0, (y0 + y1) / 2, y1))
    zbelt_s = max(zbelt_s, min(0.98, ztop))
    inner = s.get("door_card", "interior")
    # the skin, with the wheel arch booleaned out where the door reaches over it
    zc, R = s.wr + s.arch_lift, s.wr + s.arch_gap
    skin = K.Mesher(2.0)
    patch(skin, sh, y0 + g, y1 - g, zsill_s, zbelt_s, color, side, off=0.008, ny=4, ns=5, thick=0.035, inner=inner)
    hit = [yc for yc in (s.fy, s.ry) if y0 < yc + R and y1 > yc - R]
    if hit:
        merge_bm(d, cut_arches(skin, [(math.copysign(s.width / 2 + 0.05, side), yc, zc, R + 0.004, 0.42) for yc in hit]))
    else:
        merge_bm(d, skin.bm)

    def edge_s0(y):     # where a door edge's shut line starts: the sill, or the top of an arch it crosses
        lo = zsill_s - 0.02
        for yc in hit:
            if abs(y - yc) < R + g:
                za = zc + math.sqrt(max(0.0, (R + g) ** 2 - (y - yc) ** 2))
                lo = max(lo, shell_s(sh, y, za))
        return lo
    gf = glass_front or (lambda s_: y0 + 0.03)
    gr = glass_rear or (lambda s_: y1 - 0.03)
    s_bot = s_waist(cab, (y0 + y1) / 2)
    if frame:
        frame_ring(d, cab, gf, gr, s_bot, win_top_s, side, 0.025, frame_color)
    # the window glass moves with the door but is drawn see-through (its own part)
    gm = K.Mesher(2.0)
    patch(gm, cab, gf, gr, s_bot, win_top_s, "car_glass", side, off=0.016, ny=3, ns=4)
    # openings: the doorway in the body, the window frame's area in the cabin
    # the doorway is cut a little inside the skin's outline, so a closed door hides it completely (the
    # light trim inside would show through a gap); the gap itself is painted on as a dark shut line
    _BUILD["body_holes"].append((lambda q, v=y0 + g + 0.006: v, lambda q, v=y1 - g - 0.006: v, zsill_s + 0.02,
                                 zbelt_s + 0.01, side))
    _BUILD["cab_holes"].append((lambda q: gf(q) - 0.025, lambda q: gr(q) + 0.025, -1.0, win_top_s + 0.05, side))
    _BUILD["breaks_y"] += [y0 + g + 0.006, y1 - g - 0.006]     # shell sample lines on the doorway's edges
    _BUILD["breaks_s"] += [zsill_s + 0.02, zbelt_s + 0.01]
    fm = ctx["body"]
    for ya, yb in ((y0 - 0.002, y0 + g + 0.004), (y1 - g - 0.004, y1 + 0.002)):
        patch(fm, sh, ya, yb, edge_s0((ya + yb) / 2), zbelt_s, "well_black", side, off=0.0062, ny=1, ns=4)
    zs = sh.point((y0 + y1) / 2, zsill_s, side).z
    for a, b in runs_clear(s, y0 + g, y1 - g, zs, pad=0.0):
        patch(fm, sh, a, b, zsill_s - 0.025, zsill_s + 0.004, "well_black", side, off=0.0062, ny=4, ns=1)
    if handle:
        handle_on(d, sh, y1 - 0.14, zbelt_s - 0.16, side, "chrome" if s.get("chrome_handles") else
                  (s.get("handle_color") or "trim_black"))
    # inside: an armrest and a pull on the door card
    ym = (y0 + y1) / 2
    zi = sh.point(ym, zbelt_s - 0.3, side)
    ni = sh.normal(ym, zbelt_s - 0.3, side)
    rbox(d, zi - ni * 0.075, (0.07, (y1 - y0) * 0.5, 0.05), s.get("seat_bolster") or "dash_grey", r=0.5, segs=1)
    hp = sh.point(y0 + 0.02, zbelt_s, side)
    ctx["parts"].append((name, d, tuple(hp), "Body"))
    ctx["parts"].append((name + "_Glass", gm, tuple(hp), name))
    # B-pillar behind a front door: a painted post from the sill up into the roof (the closed
    # doors cover its edges, open ones show it standing between the openings)
    if name.startswith("Door_F") and s.get("b_pillar", True):
        b = ctx["core"]
        patch(b, sh, y1 - 0.03, y1 + 0.06, zsill_s, 1.0, color, side, off=0.004, ny=1, ns=4, thick=0.05, inner=inner)
        patch(b, cab, y1 - 0.03, y1 + 0.06, -0.05, 1.0, s.get("pillar_color") or s.cab_color(Vector((0, 0, 2))), side, off=0.004,
              ny=1, ns=6, thick=0.05, inner=inner)
    return d


def s_waist(sh, y, dz=0.03):
    """The s parameter just above the cabin shell's waist (its widest line) at station y."""
    return max(1e-4, shell_s(sh, y, sh.params(y)[2] + dz))


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
    if s0 < 0.12:     # "at the waist": s is very non-linear up a tall greenhouse, so find it from the height
        s0 = s_waist(sh, (f0(0.3) + f1(0.3)) / 2)
    if frame:
        frame_ring(m, sh, f0, f1, s0, s1, side, fw, frame)
    patch(_BUILD["glass"], sh, f0, f1, s0, s1, "car_glass", side, off=0.014, ny=ny, ns=ns)
    _BUILD["cab_holes"].append((f0, f1, s0, s1, side))


def lamp_pod(m, c, radii, lens, rim="chrome", rot=(0, 0, 0)):
    ellip(m, c, (radii[0] * 1.12, radii[1] * 1.05, radii[2] * 1.12), rim, seg=12, rings=6, rot=rot)
    ellip(m, Vector(c) + Vector((0, -radii[1] * 0.25, 0)), radii, lens, seg=12, rings=6, rot=rot)


def lights_part(ctx, name, fn):
    """A lamp group as its own part (the game lights it up), parented to Body."""
    lm = K.Mesher(1.0)
    fn(lm)
    ctx["parts"].append((name, lm, None, "Body"))


def roof_part(ctx, name, fn):
    """Roof furniture (taxi sign, police light bar) as its own part, so the roof tests measure the
    real roof skin of Body and not the sign standing on it."""
    rm = K.Mesher(1.0)
    fn(rm)
    ctx["parts"].append((name, rm, None, "Body"))


def two_tone(lower="car_paint", upper="car_paint2", split=0.62, sill=None):
    """Body colour function: upper / lower paint split at height `split` (z), dark sills below `sill`."""
    def f(c):
        if sill is not None and c.z < sill:
            return "trim_black"
        return upper if c.z > split else lower
    return f


# ============================================================================== MYVI
def myvi(livery=None):
    """veh_myvi: 2nd-gen Myvi (the orange SE). Short, tall 5-door hatch with wheels pushed to the
    corners: flat upright nose with a small grille slot, huge swept headlamps climbing the bonnet
    edges toward the A-pillars, a big trapezoid lower intake (honeycomb mesh) with round fog lamps,
    a wedge window line, blacked-out pillars, a roof spoiler with a high brake light, a rear wiper,
    gunmetal 5-spoke alloys, a black-and-grey cloth interior.
    Dimensions follow the real car (3.69 x 1.67 x 1.55 m, 2.44 m wheelbase), lightly cartooned.
    livery="polis": the patrol car - white with a navy band and a red pinstripe, POLIS lettering,
    a light bar on the roof."""
    W = 1.68
    pol = livery == "polis"
    s = CarSpec(id="veh_polis" if pol else "veh_myvi", width=W, track=1.44, wr=0.31, ww=0.21, fy=-1.2, ry=1.24, arch_lift=0.02,
                arch_gap=0.05, wheel_style="alloy_dark", spokes=5, floor_z=0.3, roof_z=1.59, seat_x=0.36, seat_y=0.02,
                dash_y=-0.66, dash_z=0.86, b_pillar=True, seat_color="seat_grey", seat_bolster="charcoal", door_card="dash_grey",
                headliner="aircon", handle_color="car_paint")
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
                      (-0.34, 0.775, 0.88, 0.99, 1.56, 3.6, 4.6, 2.0),
                      (1.36, 0.765, 0.88, 1.03, 1.57, 3.6, 4.6, 2.0),
                      (1.58, 0.74, 0.88, 1.04, 1.52, 3.4, 4.0, 2.0),
                      (1.80, 0.64, 0.88, 1.04, 1.12, 3.0, 2.6, 2.0)]
    s.y_breaks, s.s_breaks, s.cab_breaks = (-1.7, 1.7), (-0.55,), ()

    if pol:
        def body_color(c):
            if c.z < 0.3:
                return "trim_black"
            if abs(c.x) > 0.6 and 0.36 < c.z < 0.6:
                return "police_navy"
            if abs(c.x) > 0.6 and 0.6 <= c.z < 0.635:
                return "siren_red"
            return "car_paint"

        def paint(c):
            if 0.36 < c.z < 0.6:
                return "police_navy"
            if 0.6 <= c.z < 0.635:
                return "siren_red"
            return "car_paint"
    else:
        def body_color(c):
            return "trim_black" if c.z < 0.3 else "car_paint"
        paint = "car_paint"
    s.body_color = body_color
    # blacked-out pillars between the window line and a body-colour roof
    s.cab_color = lambda c: "car_paint" if c.z > 1.49 else "trim_black"
    s.belt_s = 0.04

    def details(ctx):
        b, sh, cab, parts = ctx["body"], ctx["shell"], ctx["cab"], ctx["parts"]
        ws_edge = lambda q: -0.94 + q * 0.58          # the A-pillar: where the windscreen ends at height q
        doors = {}
        for side in (1, -1):
            doors["F", side] = door(ctx, "Door_FL" if side > 0 else "Door_FR", side, -0.86, 0.3, -0.5, 0.2, 0.52,
                                    glass_front=lambda q: ws_edge(q) + 0.09, glass_rear=lambda q: 0.24, color=paint)
            doors["R", side] = door(ctx, "Door_RL" if side > 0 else "Door_RR", side, 0.32, 1.14, -0.5, 0.2, 0.52,
                                    glass_front=lambda q: 0.38, glass_rear=lambda q: 1.06 - q * 0.08, color=paint)
            glass(b, cab, 1.16, lambda q: 1.46 - q * 0.1, 0.05, 0.46, side, ny=3, ns=3)            # rear quarter
            mirror_arm(b, sh.point(-0.94, 0.42, side) + Vector((0, 0.02, 0.05)), side, color="trim_black" if pol else "car_paint")
            patch(b, sh, -1.28, -1.2, 0.2, 0.28, "lamp_amber", side, off=0.006, ny=1, ns=1)       # side repeater
            side_strip(b, sh, s, -1.25, 1.55, -0.5, -0.38, "trim_black" if pol else "car_paint", side, off=0.012, thick=0.03)
            for key, (y0, y1) in (("F", (-0.86, 0.3)), ("R", (0.32, 1.14))):                     # SE skirt on the doors
                side_strip(doors[key, side], sh, s, y0 + 0.014, y1 - 0.014, -0.5, -0.38, "trim_black" if pol else "car_paint",
                           side, off=0.02, thick=0.03)
            # shut lines: bonnet edges on the shoulders, bumper splits ahead of / behind the arches
            seam(b, sh, -1.72, -1.0, 0.78, 0.795, side, ny=6)
            seam(b, sh, -1.73, -1.72, 0.55, 1.0, side, ny=1, ns=3)
            seam(b, sh, -1.58, -1.57, -0.45, 0.42, side, ny=1, ns=4)
            seam(b, sh, 1.62, 1.63, -0.45, 0.45, side, ny=1, ns=4)
            seam(b, sh, -1.0, -0.99, 0.78, 1.0, side, ny=1, ns=3)
        fuel_cap(b, sh, 1.38, 0.32, 1, r=0.055, color="car_paint")
        # windscreen + hatch glass
        for side in (1, -1):
            glass(b, cab, lambda q: -1.02 + (1 - q) * 0.02, ws_edge, 0.02, 1.0, side, ny=4, ns=4)
            glass(b, cab, lambda q: 1.77 - q * 0.2, 1.82, 0.46, 1.0, side, ny=2, ns=3)
        wn = Vector((0, -0.5, 0.86)).normalized()
        for x0, x1 in ((0.5, -0.06), (-0.06, -0.62)):
            wiper(b, (x0, -0.99, 0.985), (x1, -0.93, 1.03), wn)
        # rear wiper on the hatch glass
        wiper(b, (0, 1.79, 1.18), (0.42, 1.73, 1.34), Vector((0, 0.9, 0.3)))
        # roof spoiler over the hatch + high brake light
        rz = cab.point(1.5, 1.0, 1).z
        for side in (1, -1):
            patch(b, cab, 1.3, 1.62, 0.9, 1.0, "car_paint", side, off=0.02, ny=3, ns=2, thick=0.03)
        # front: small grille slot + badge, big trapezoid lower intake with honeycomb mesh,
        # round fog lamps in black pods at the bumper corners, plate on the bumper
        fz = -1.84
        rbox(b, (0, fz, 0.63), (0.5, 0.05, 0.075), "grille_black", r=0.6, segs=1)
        b.box((0, fz - 0.026, 0.63), (0.46, 0.006, 0.012), "chrome")
        badge(b, (0, fz - 0.03, 0.635), kind="oval")
        honeycomb(b, (0, fz - 0.005, 0.395), 0.9, 0.18, cell=0.05)
        rbox(b, (0, fz - 0.012, 0.395), (0.98, 0.02, 0.21), "trim_black", r=0.25, segs=1)
        for x in (0.62, -0.62):
            rbox(b, (x, fz + 0.02, 0.4), (0.2, 0.05, 0.14), "grille_black", r=0.5, segs=1)
            disc_on(b, (x, fz - 0.008, 0.4), (0, -1, 0), 0.05, "lens_clear", seg=12)
            ring_on(b, (x, fz - 0.009, 0.4), (0, -1, 0), 0.05, 0.058, "chrome", seg=12)
        plate(b, (0, fz - 0.03, 0.54), w=0.42, h=0.1, text="WPD 5521" if pol else "WXY 1234")
        # rear: hatch outline and garnish, plate, bumper reflectors and a black diffuser strip
        ry_ = 1.86
        for x in (0.5, -0.5):
            b.box((x, ry_, 0.74), (0.006, 0.01, 0.52), "well_black")
        b.box((0, ry_, 0.48), (1.0, 0.01, 0.006), "well_black")
        rbox(b, (0, ry_ + 0.005, 0.76), (0.5, 0.02, 0.06), "chrome", r=0.5, segs=1)
        badge(b, (0, ry_ + 0.016, 0.86), facing=1, kind="oval", size=0.8)
        plate(b, (0, ry_ + 0.016, 0.6), facing=1, text="WPD 5521" if pol else "WXY 1234")
        rbox(b, (0, ry_ - 0.02, 0.3), (1.2, 0.05, 0.06), "trim_black", r=0.5, segs=1)
        exhaust(b, (0.45, 1.9, 0.28), r=0.035)
        parts.append(("FX_Exhaust", None, (0.45, 1.92, 0.28), "Body"))
        for side in (1, -1):
            # swept headlamp housings: from the front corner up the bonnet edge toward the A-pillar
            patch(b, sh, -1.83, lambda q: -1.62 + max(0.0, q - 0.2) * 0.62, 0.08, 0.82, "trim_black", side, off=0.006,
                  ny=5, ns=4)

        def heads(lm):
            for side in (1, -1):
                patch(lm, sh, -1.825, lambda q: -1.64 + max(0.0, q - 0.22) * 0.58, 0.12, 0.8, "lens_clear", side,
                      off=0.014, ny=5, ns=4)
                for k, (y, q) in enumerate(((-1.76, 0.42), (-1.64, 0.58))):   # twin projector bowls
                    p = sh.point(y, q, side) + sh.normal(y, q, side) * 0.016
                    n = sh.normal(y, q, side)
                    disc_on(lm, p, n, 0.05, "chrome", seg=12)
                    disc_on(lm, p + n * 0.003, n, 0.028, "lamp_white", seg=10)
                # LED light guide along the bottom of the lamp
                patch(lm, sh, -1.8, -1.66, 0.16, 0.2, "lamp_white", side, off=0.017, ny=2, ns=1)
        lights_part(ctx, "HeadLights", heads)
        for side in (1, -1):    # amber indicator segment at the lamp's outer corner (unlit)
            patch(b, sh, -1.8, -1.72, 0.14, 0.26, "lamp_amber", side, off=0.018, ny=1, ns=1)

        def tails(lm):
            for side in (1, -1):
                # tall tail lamps standing up the rear corners beside the hatch, with inner light bars
                rbox(lm, (side * 0.64, 1.82, 0.93), (0.15, 0.1, 0.36), "lamp_red", r=0.5, segs=1)
                for k in range(3):
                    lm.box((side * 0.64, 1.872, 0.85 + k * 0.08), (0.11, 0.004, 0.02), "tail_red")
                rbox(lm, (side * 0.66, 1.845, 0.33), (0.14, 0.03, 0.04), "lamp_red", r=0.5, segs=1)   # bumper reflector
            rbox(lm, (0, 1.63, rz + 0.005), (0.22, 0.03, 0.03), "lamp_red", r=0.5, segs=1)            # high brake light
        lights_part(ctx, "BrakeLights", tails)
        for side in (1, -1):
            rbox(b, (side * 0.64, 1.81, 0.79), (0.15, 0.1, 0.07), "lamp_amber", r=0.5, segs=1)

        def rev(lm):
            for side in (1, -1):
                rbox(lm, (side * 0.64, 1.835, 0.72), (0.12, 0.08, 0.06), "lamp_white", r=0.5, segs=1)
        lights_part(ctx, "ReverseLights", rev)
        rear_mirror(b, (0, -0.72, 1.5))
        ornament(parts, (0, -0.68, 1.4), kind="freshener")
        # ---- optional parts (VehicleVariety)
        spans = {"F": (-0.86, 0.3), "R": (0.32, 1.14)}
        body_mud(ctx, [(-1.8, -0.88), (1.16, 1.84)], 2011)
        for (key, side) in doors:
            door_mud(ctx, door_name(key, side), side, *spans[key], 2011 + (10 if key == "F" else 20) + (side > 0))
        if not pol:
            var_part(ctx, "Rack", "a", "Roof", lambda m: roof_rack(m, cab, -0.2, 1.15))
            pr, nr = cab.point(1.74, 0.62, -1), cab.normal(1.74, 0.62, -1)
            var_part(ctx, "Sticker", "p", "Rear", lambda m: p_sticker(m, pr + nr * 0.02, nr))
            pc, nc = cab.point(1.74, 0.62, 1), cab.normal(1.74, 0.62, 1)
            var_part(ctx, "Sticker", "club", "Rear", lambda m: round_sticker(m, pc + nc * 0.02, nc, fill="shop_red"))
            side_stripes(ctx, [(door_name(k, sd), *spans[k], sd) for (k, sd) in doors], -1.7, 1.8, 0.5, 0.54,
                         "sticker_yellow", "stripe")
            var_part(ctx, "Shelf", "a", "Rear", lambda m: shelf_stuff(m, (0, 1.45, 1.03)), near_only=True)
        if pol:
            for side in (1, -1):
                side_text(doors["F", side], sh, "POLIS", -0.27, 0.48, 0.13, side, "white", off=0.022, max_w=0.9)
                side_text(doors["R", side], sh, "999", 0.73, 0.48, 0.09, side, "white", off=0.022, max_w=0.4)
            put_text(b, "POLIS", Vector((0, 1.873, 0.68)), Vector((0, 1, 0)), 0.06, "police_navy", max_w=0.3)
            put_text(b, "POLIS", Vector((0, -1.42, 0.9)), Vector((0, 0, 1)), 0.12, "police_navy", max_w=0.55,
                     up=Vector((0, 1, 0)), place=lambda p: Vector((p.x, p.y, shell_top_z(sh, p.x, p.y) + 0.005)))

            def bar_(m):
                y, z = 0.1, 1.575
                m.box((0, y, z + 0.012), (1.12, 0.26, 0.025), "trim_black")
                for x in (0.42, -0.42):
                    rbox(m, (x, y, z), (0.06, 0.3, 0.025), "rubber", r=0.5, segs=1)
                rbox(m, (0, y, z + 0.06), (1.1, 0.24, 0.075), "trim_black", r=0.5, segs=1)
                for k in range(4):
                    x = 0.17 + k * 0.105
                    rbox(m, (x, y, z + 0.07), (0.095, 0.25, 0.07), "siren_red", r=0.5, segs=1)
                    rbox(m, (-x, y, z + 0.07), (0.095, 0.25, 0.07), "siren_blue", r=0.5, segs=1)
                rbox(m, (0, y, z + 0.07), (0.2, 0.25, 0.07), "lens_clear", r=0.4, segs=1)
                rbox(m, (0, y, z + 0.112), (1.12, 0.22, 0.012), "chrome", r=0.5, segs=1)
            roof_part(ctx, "LightBar", bar_)
        else:
            antenna(parts, (0, 1.2, 1.54), length=0.36)
    return build_car(s, details)


def polis():
    """veh_polis: police patrol car on the Myvi body: white with a navy band, POLIS lettering, light bar."""
    return myvi(livery="polis")


# ============================================================================== SAGA (classic)
def saga(livery=None):
    """veh_saga: the 1985 Saga 1.3 (Lancer Fiore-based): a crisp 80s wedge - low flat bonnet,
    flush rectangular headlamps either side of a black slatted grille with a small badge,
    black wrap-round bumpers with rubber inserts, flat creased flanks, thin pillars round a big
    glasshouse with a chrome drip rail, a long flat boot, steel wheels with body-colour centre
    caps, a beige velour interior. 4.08 x 1.62 x 1.36 m.
    livery="teksi": the KL budget taxi - red below the waist, white above, roof sign, lettering."""
    W = 1.64
    teksi = livery == "teksi"
    s = CarSpec(id="veh_teksi" if teksi else "veh_saga", width=W, track=1.4, wr=0.29, ww=0.18, fy=-1.22, ry=1.16,
                arch_lift=0.0, arch_gap=0.05, wheel_style="steel_paint", floor_z=0.24, roof_z=1.48, seat_x=0.36, seat_y=-0.02,
                dash_y=-0.62, dash_z=0.8, seat_color="towel", seat_bolster="sandal_brown", door_card="pale_panel",
                headliner="cream", dash_color="dash_grey")
    s.stations = [(-2.05, 0.74, 0.28, 0.46, 0.58, 5.0, 3.6, 3.6),
                  (-2.03, 0.80, 0.26, 0.50, 0.64, 8.0, 5.0, 5.0),
                  (-1.96, 0.815, 0.25, 0.54, 0.68, 9.0, 6.0, 6.0),
                  (-1.30, 0.82, 0.25, 0.58, 0.76, 9.0, 6.0, 6.0),
                  (-0.64, 0.82, 0.25, 0.60, 0.82, 9.0, 6.0, 6.0),
                  (1.30, 0.82, 0.25, 0.62, 0.86, 9.0, 6.0, 6.0),
                  (1.98, 0.815, 0.26, 0.62, 0.86, 9.0, 6.0, 5.0),
                  (2.05, 0.78, 0.30, 0.62, 0.84, 6.0, 4.0, 3.6)]
    s.cab_stations = [(-0.68, 0.72, 0.78, 0.83, 0.86, 4.0, 2.4, 2.0),
                      (-0.52, 0.76, 0.78, 0.84, 1.08, 4.4, 4.0, 2.0),
                      (-0.12, 0.745, 0.78, 0.85, 1.48, 4.6, 8.0, 2.0),
                      (0.86, 0.745, 0.78, 0.86, 1.49, 4.6, 8.0, 2.0),
                      (1.12, 0.74, 0.78, 0.86, 1.2, 4.4, 5.0, 2.0),
                      (1.36, 0.70, 0.78, 0.86, 0.9, 4.0, 2.4, 2.0)]
    s.y_breaks, s.s_breaks, s.cab_breaks = (-1.96, 1.98), (-0.45, 0.2), ()
    split = 0.665                                     # teksi: red below, white above
    if teksi:
        s.body_color = two_tone("car_paint", "car_paint2", split, sill=0.3)
        s.cab_color = lambda c: "car_paint2"
        paint = two_tone("car_paint", "car_paint2", split)
        s.cap_paint = "car_paint2"
    else:
        s.body_color = lambda c: "trim_black" if c.z < 0.3 else "car_paint"
        s.cab_color = lambda c: "car_paint"
        paint = "car_paint"

    def details(ctx):
        b, core, sh, cab, parts = ctx["body"], ctx["core"], ctx["shell"], ctx["cab"], ctx["parts"]
        ws_edge = lambda q: -0.56 + q * 0.42
        doors = {}
        for side in (1, -1):
            doors["F", side] = door(ctx, "Door_FL" if side > 0 else "Door_FR", side, -0.66, 0.3, -0.42, 0.2, 0.62,
                                    glass_front=lambda q: ws_edge(q) + 0.06, glass_rear=lambda q: 0.24, color=paint)
            doors["R", side] = door(ctx, "Door_RL" if side > 0 else "Door_RR", side, 0.32, 1.1, -0.42, 0.2, 0.62,
                                    glass_front=lambda q: 0.38, glass_rear=lambda q: 1.06 - q * 0.16, color=paint)
            mirror_arm(b, sh.point(-0.6, 0.5, side) + Vector((0, 0.02, 0.03)), side, color="trim_black", size=0.9)
            # the flank crease + black rubbing strip that wraps into the bumpers (and on the doors)
            side_strip(b, sh, s, -1.96, 1.98, 0.14, 0.17, "trim_black", side, off=0.008)
            side_strip(b, sh, s, -1.96, 1.98, -0.3, -0.18, "trim_black", side, off=0.012, thick=0.02)
            for key in ("F", "R"):
                y0, y1 = (-0.66, 0.3) if key == "F" else (0.32, 1.1)
                dk = doors[key, side]
                side_strip(dk, sh, s, y0 + 0.014, y1 - 0.014, 0.14, 0.17, "trim_black", side, off=0.0125)
                side_strip(dk, sh, s, y0 + 0.014, y1 - 0.014, -0.3, -0.18, "trim_black", side, off=0.02, thick=0.02)
                side_strip(dk, sh, s, y0 + 0.014, y1 - 0.014, -0.265, -0.215, "chrome", side, off=0.0215)
            # chrome drip rail along the roof edge, a chrome strip under the side glass
            patch(b, cab, -0.42, 1.08, 0.675, 0.695, "chrome", side, off=0.006, ny=10, ns=1)
            # front wing / bumper and rear quarter shut lines, sill seam
            seam(b, sh, -1.86, -1.85, -0.35, 0.6, side, ns=4, ny=1)
            seam(b, sh, 1.88, 1.89, -0.35, 0.6, side, ns=4, ny=1)
            # side marker lamps on the front wings
            patch(b, sh, -1.8, -1.72, 0.04, 0.1, "lamp_amber", side, off=0.006, ny=1, ns=1)
            patch(b, sh, 1.86, 1.94, 0.04, 0.1, "lamp_red", side, off=0.006, ny=1, ns=1)
            # bonnet and boot lid shut lines on the flat top
            seam(b, sh, -1.95, -0.7, 0.79, 0.805, side, ny=10)
            seam(b, sh, -1.95, -1.94, 0.79, 1.0, side, ny=1, ns=3)
            seam(b, sh, 1.42, 1.99, 0.79, 0.805, side, ny=6)
            seam(b, sh, 1.42, 1.43, 0.79, 1.0, side, ny=1, ns=3)
            seam(b, sh, -0.71, -0.7, 0.79, 1.0, side, ny=1, ns=3)
        fuel_cap(b, sh, 1.55, 0.32, -1, r=0.055, color="car_paint2" if teksi else "car_paint")
        for side in (1, -1):
            glass(b, cab, lambda q: -0.66 + (1 - q) * 0.02, ws_edge, 0.02, 1.0, side, ny=4, ns=4)
            glass(b, cab, lambda q: 1.3 - q * 0.42, 1.34, 0.02, 1.0, side, ny=3, ns=4)
        # scuttle panel and wipers at the base of the windscreen
        rbox(b, (0, -0.71, 0.8), (1.36, 0.1, 0.03), "trim_black", r=0.5, segs=1)
        wn = Vector((0, -0.62, 0.78)).normalized()
        for x0, x1 in ((0.52, -0.08), (-0.04, -0.62)):
            wiper(b, (x0, -0.66, 0.825), (x1, -0.6, 0.875), wn)
        # front: flat slanted panel, black slatted grille with a chrome surround, small shield badge
        fz = -2.0
        grille(b, (0, fz, 0.56), 0.7, 0.13, bars=4, frame="chrome", vbars=0, depth=0.06)
        badge(b, (0, fz - 0.045, 0.565), kind="shield", size=0.9)
        # black bumpers wrapping round with a rubber insert strip and overriders
        for y, f in ((-2.08, -1), (2.08, 1)):
            rbox(b, (0, y, 0.38), (W + 0.04, 0.14, 0.15), "trim_black", r=0.45, segs=2)
            rbox(b, (0, y + f * 0.07, 0.4), (W - 0.1, 0.012, 0.035), "rim_dark", r=0.5, segs=1)
            for x in (0.45, -0.45):
                rbox(b, (x, y + f * 0.075, 0.36), (0.07, 0.04, 0.13), "rubber", r=0.6, segs=1)
            rbox(b, (0, y - f * 0.02, 0.27), (W - 0.2, 0.1, 0.05), "well_black", r=0.4, segs=1)   # valance
        plate(b, (0, -2.16, 0.38), w=0.44, h=0.11, text="HWA 2316" if teksi else "BKD 7621")
        # rear: boot lid edge, black garnish panel with the plate, boot lock
        b.box((0, 2.055, 0.84), (1.3, 0.02, 0.02), "trim_black")
        rbox(b, (0, 2.06, 0.64), (0.42, 0.02, 0.17), "trim_black", r=0.3, segs=1)
        for k in range(4):
            b.box((0, 2.072, 0.58 + k * 0.035), (0.4, 0.004, 0.008), "rim_dark")
        plate(b, (0, 2.08, 0.62), facing=1, w=0.36, h=0.1, text="HWA 2316" if teksi else "BKD 7621")
        b.cyl((0, 2.07, 0.76), 0.014, 0.014, 0.012, "chrome", seg=8, rot=(math.pi / 2, 0, 0))
        exhaust(b, (0.5, 2.18, 0.26), r=0.03)
        parts.append(("FX_Exhaust", None, (0.5, 2.2, 0.26), "Body"))
        # amber indicators in the front bumper corners (unlit)
        for x in (0.72, -0.72):
            lamp_lens(b, (x, -2.14, 0.4), (0.16, 0.03, 0.06), "lamp_amber", r=0.4)
        # headlamp bezels (chrome surround, dark bucket) - the lit glass is in HeadLights
        for x in (0.56, -0.56):
            rbox(b, (x, fz + 0.005, 0.57), (0.38, 0.05, 0.17), "chrome", r=0.15, segs=1)
            rbox(b, (x, fz - 0.005, 0.57), (0.35, 0.05, 0.14), "well_black", r=0.15, segs=1)

        def heads(lm):
            for x in (0.56, -0.56):   # big flush rectangular headlamps: a bold lens with a bright bulb in it
                rbox(lm, (x, fz - 0.026, 0.57), (0.33, 0.012, 0.125), "lens_clear", r=0.3, segs=1)
                rbox(lm, (x, fz - 0.033, 0.57), (0.12, 0.006, 0.08), "lamp_white", r=0.5, segs=1)
        lights_part(ctx, "HeadLights", heads)

        def tails(lm):
            for x in (0.56, -0.56):
                rbox(lm, (x, 2.04, 0.66), (0.44, 0.06, 0.16), "lamp_red", r=0.3, segs=1)
                rbox(lm, (x * 1.26, 2.05, 0.66), (0.1, 0.05, 0.15), "lamp_amber", r=0.2, segs=1)
        lights_part(ctx, "BrakeLights", tails)
        for x in (0.56, -0.56):   # dark bezels round the tail lamps
            rbox(b, (x * 1.06, 2.03, 0.66), (0.58, 0.05, 0.18), "rim_dark", r=0.15, segs=1)

        def rev(lm):
            for x in (0.3, -0.3):
                rbox(lm, (x, 2.05, 0.66), (0.1, 0.05, 0.13), "lamp_white", r=0.2, segs=1)
        lights_part(ctx, "ReverseLights", rev)
        rear_mirror(b, (0, -0.42, 1.4))
        ornament(parts, (0, -0.4, 1.3), kind="tasbih")
        antenna(parts, (0.74, -1.25, 0.78), length=0.7, lean=0.25)
        # ---- optional parts (VehicleVariety)
        spans = {"F": (-0.66, 0.3), "R": (0.32, 1.1)}
        body_mud(ctx, [(-1.95, -0.68), (1.12, 1.98)], 1985)
        for (key, side) in doors:
            door_mud(ctx, door_name(key, side), side, *spans[key], 1985 + (10 if key == "F" else 20) + (side > 0))
        if not teksi:
            var_part(ctx, "Rack", "a", "Roof", lambda m: roof_rack(m, cab, -0.05, 0.8))
            pr, nr = cab.point(1.2, 0.5, -1), cab.normal(1.2, 0.5, -1)
            var_part(ctx, "Sticker", "p", "Rear", lambda m: p_sticker(m, pr + nr * 0.02, nr))
            pc, nc = cab.point(1.2, 0.5, 1), cab.normal(1.2, 0.5, 1)
            var_part(ctx, "Sticker", "club", "Rear", lambda m: round_sticker(m, pc + nc * 0.02, nc))
            side_stripes(ctx, [(door_name(k, sd), *spans[k], sd) for (k, sd) in doors], -1.9, 1.95, 0.6, 0.64, "white", "stripe")
            var_part(ctx, "Shelf", "a", "Rear", lambda m: shelf_stuff(m, (0, 1.16, 0.86)), near_only=True)
            wheel_vars(ctx, "Hubcap", "a", lambda m, c, sd: hubcap(m, c, s.wr, s.ww, sd, s.wr * 0.64))
            var_part(ctx, "Primer", "a", "Bonnet", lambda m: [patch(m, sh, -1.93, -0.73, 0.8, 1.0, "seat_grey", sd, off=0.004,
                                                                     ny=6, ns=2) for sd in (1, -1)])
        if teksi:
            # door lettering, a fleet number on the rear doors, the roof sign
            for side in (1, -1):
                side_text(doors["F", side], sh, "TEKSI", -0.18, 0.47, 0.11, side, "white", off=0.014, max_w=0.7)
                side_text(doors["R", side], sh, "BAJET 23", 0.71, 0.47, 0.06, side, "white", off=0.014, max_w=0.6)
                side_strip(b, sh, s, -1.96, 1.98, 0.19, 0.205, "trim_black", side, off=0.006)

            def sign(m):
                y, z = 0.18, 1.49
                m.box((0, y, z + 0.015), (0.62, 0.26, 0.03), "trim_black")
                for x in (0.22, -0.22):
                    rbox(m, (x, y, z + 0.005), (0.08, 0.2, 0.03), "rubber", r=0.5, segs=1)
                rbox(m, (0, y, z + 0.11), (0.58, 0.2, 0.16), "plate_white", r=0.35, segs=2)
                for f in (-1, 1):
                    m.box((0, y + f * 0.101, z + 0.11), (0.5, 0.004, 0.12), "white")
                    put_text(m, "TEKSI", Vector((0, y + f * 0.104, z + 0.11)), Vector((0, f, 0)), 0.085, "lamp_red",
                             max_w=0.44)
                m.box((0, y, z + 0.195), (0.6, 0.22, 0.012), "lamp_red")
            roof_part(ctx, "RoofSign", sign)
    return build_car(s, details)


def teksi():
    """veh_teksi: the red-and-white KL budget taxi on the classic Saga body, roof sign."""
    return saga(livery="teksi")


# ============================================================================== KANCIL
def kancil():
    """veh_kancil: the tiny, tall, round-eyed city hatch. Short wheelbase, 12-inch steel wheels
    with chrome hubcaps, round headlamps in chrome rings either side of a slim grille, black
    bumpers, a rubbing strip, vertical tail lamps, a rear wiper, a grey cloth interior."""
    W = 1.5
    s = CarSpec(id="veh_kancil", width=W, track=1.3, wr=0.28, ww=0.16, fy=-0.98, ry=1.0, arch_lift=0.01, arch_gap=0.05,
                wheel_style="steel", floor_z=0.28, roof_z=1.56, seat_x=0.32, seat_y=0.05, dash_y=-0.55, dash_z=0.8,
                seat_color="seat_grey", seat_bolster="dash_grey", door_card="seat_grey", headliner="cream")
    s.stations = [(-1.54, 0.6, 0.28, 0.50, 0.66, 3.0, 2.4, 2.4),
                  (-1.48, 0.72, 0.24, 0.52, 0.76, 3.6, 3.0, 3.0),
                  (-1.2, 0.75, 0.23, 0.56, 0.86, 4.0, 3.0, 3.0),
                  (0.8, 0.755, 0.23, 0.6, 0.92, 4.0, 3.2, 3.0),
                  (1.4, 0.74, 0.24, 0.6, 0.92, 3.6, 3.0, 2.8),
                  (1.56, 0.62, 0.3, 0.6, 0.86, 3.0, 2.4, 2.4)]
    s.cab_stations = [(-0.9, 0.56, 0.8, 0.85, 0.9, 2.6, 2.0, 2.0),
                      (-0.6, 0.66, 0.8, 0.88, 1.4, 3.2, 3.0, 2.0),
                      (-0.2, 0.68, 0.8, 0.88, 1.56, 3.6, 3.6, 2.0),
                      (1.2, 0.68, 0.8, 0.88, 1.56, 3.6, 3.6, 2.0),
                      (1.46, 0.62, 0.8, 0.86, 1.34, 3.0, 3.0, 2.0),
                      (1.52, 0.5, 0.8, 0.84, 0.94, 2.4, 2.0, 2.0)]
    s.y_breaks, s.s_breaks, s.cab_breaks = (-1.4, 1.45), (-0.5,), ()
    s.body_color = lambda c: "trim_black" if c.z < 0.31 else "car_paint"
    s.cab_color = lambda c: "car_paint"
    s.rear_seat = True

    def details(ctx):
        b, sh, cab, parts = ctx["body"], ctx["shell"], ctx["cab"], ctx["parts"]
        doors = {}
        for side in (1, -1):
            doors[side] = door(ctx, "Door_FL" if side > 0 else "Door_FR", side, -0.56, 0.5, -0.45, 0.2, 0.7,
                               glass_front=lambda q: -0.5 + q * 0.2)
            glass(b, cab, 0.6, 1.36, 0.04, 0.6, side, ny=3, ns=3)
            mirror_arm(b, sh.point(-0.54, 0.35, side) + Vector((0, 0, 0.03)), side, color="trim_black", size=0.85)
            side_strip(b, sh, s, -1.45, 1.5, -0.2, -0.12, "trim_black", side, off=0.01, thick=0.02)
            side_strip(doors[side], sh, s, -0.546, 0.486, -0.2, -0.12, "trim_black", side, off=0.018, thick=0.02)
            # shut lines: bonnet, front wing / bumper, rear quarter, hatch sides; side indicator
            seam(b, sh, -1.4, -0.62, 0.74, 0.755, side, ny=6)
            seam(b, sh, -1.41, -1.4, 0.74, 1.0, side, ny=1, ns=3)
            seam(b, sh, -0.63, -0.62, 0.74, 1.0, side, ny=1, ns=3)
            seam(b, sh, 1.42, 1.43, -0.45, 0.55, side, ny=1, ns=4)
            patch(b, sh, -0.9, -0.84, 0.05, 0.12, "lamp_amber", side, off=0.006, ny=1, ns=1)
        fuel_cap(b, sh, 1.15, 0.3, 1, r=0.05, color="car_paint")
        glass(b, cab, lambda q: -0.86 + (1 - q) * 0.02, -0.6, 0.02, 1.0, 1, ny=4, ns=3)
        glass(b, cab, lambda q: -0.86 + (1 - q) * 0.02, -0.6, 0.02, 1.0, -1, ny=4, ns=3)
        for side in (1, -1):
            glass(b, cab, 1.36, 1.5, 0.02, 1.0, side, ny=2, ns=3)
        wn = Vector((0, -0.45, 0.89)).normalized()
        for x0, x1 in ((0.42, -0.06), (-0.06, -0.52)):
            wiper(b, (x0, -0.82, 0.9), (x1, -0.78, 0.95), wn)
        wiper(b, (0, 1.5, 1.0), (0.36, 1.47, 1.18), Vector((0, 0.95, 0.2)))
        # front: slim grille between the eyes, badge, black bumper with plate, little lower slot
        grille(b, (0, -1.535, 0.58), 0.5, 0.07, bars=2, frame="chrome", depth=0.05)
        badge(b, (0, -1.565, 0.58), kind="oval", size=0.8)
        for y, f in ((-1.56, -1), (1.58, 1)):
            rbox(b, (0, y, 0.36), (W - 0.02, 0.1, 0.14), "trim_black", r=0.6, segs=2)
            rbox(b, (0, y + f * 0.05, 0.38), (W - 0.12, 0.01, 0.03), "rim_dark", r=0.5, segs=1)
        rbox(b, (0, -1.6, 0.27), (0.6, 0.04, 0.04), "well_black", r=0.5, segs=1)
        plate(b, (0, -1.62, 0.37), w=0.36, text="PKN 660")
        # rear: hatch seams, handle, plate in a recess, exhaust
        for x in (0.42, -0.42):
            b.box((x, 1.565, 0.72), (0.006, 0.01, 0.34), "well_black")
        rbox(b, (0, 1.57, 0.58), (0.44, 0.02, 0.15), "trim_black", r=0.3, segs=1)
        plate(b, (0, 1.585, 0.58), w=0.36, facing=1, text="PKN 660")
        rbox(b, (0, 1.57, 0.72), (0.18, 0.02, 0.03), "chrome", r=0.5, segs=1)
        exhaust(b, (0.4, 1.62, 0.26), r=0.028, length=0.12)
        parts.append(("FX_Exhaust", None, (0.4, 1.65, 0.26), "Body"))
        for x in (0.5, -0.5):   # chrome rings and dark buckets round the round headlamps
            ring_on(b, (x, -1.565, 0.66), (0, -1, 0), 0.105, 0.125, "chrome", seg=16)
            disc_on(b, (x, -1.555, 0.66), (0, -1, 0), 0.12, "well_black", seg=16)
            rbox(b, (x * 1.26, -1.51, 0.5), (0.1, 0.04, 0.05), "lamp_amber", r=0.5, segs=1)

        def heads(lm):
            for x in (0.5, -0.5):   # big round eyes: domed lens over a reflector and bulb
                ellip(lm, (x, -1.565, 0.66), (0.104, 0.03, 0.104), "lens_clear", seg=14, rings=6)
                disc_on(lm, (x, -1.597, 0.66), (0, -1, 0.0), 0.05, "lamp_white", seg=10)
                for k in range(3):
                    lm.box((x, -1.596, 0.62 + k * 0.04), (0.17, 0.004, 0.006), "rim_silver")
        lights_part(ctx, "HeadLights", heads)

        def tails(lm):
            for side in (1, -1):
                patch(lm, sh, 1.44, 1.56, 0.0, 0.5, "lamp_red", side, off=0.012, ny=1, ns=2)
                patch(lm, sh, 1.47, 1.53, 0.1, 0.4, "tail_red", side, off=0.016, ny=1, ns=2)
        lights_part(ctx, "BrakeLights", tails)
        for side in (1, -1):
            patch(b, sh, 1.44, 1.56, -0.18, -0.02, "lamp_amber", side, off=0.012, ny=1, ns=1)

        def rev(lm):
            for side in (1, -1):
                patch(lm, sh, 1.5, 1.56, -0.2, -0.02, "lamp_white", side, off=0.014, ny=1, ns=1)
        lights_part(ctx, "ReverseLights", rev)
        # ---- optional parts (VehicleVariety)
        body_mud(ctx, [(-1.5, -0.58), (0.52, 1.5)], 1994)
        for side in (1, -1):
            door_mud(ctx, door_name("F", side), side, -0.56, 0.5, 1994 + (side > 0))
        var_part(ctx, "Rack", "a", "Roof", lambda m: roof_rack(m, cab, -0.3, 1.1))
        pr, nr = cab.point(1.43, 0.55, -1), cab.normal(1.43, 0.55, -1)
        var_part(ctx, "Sticker", "p", "Rear", lambda m: p_sticker(m, pr + nr * 0.02, nr))
        pc, nc = cab.point(1.43, 0.55, 1), cab.normal(1.43, 0.55, 1)
        var_part(ctx, "Sticker", "club", "Rear", lambda m: round_sticker(m, pc + nc * 0.02, nc, fill="tshirt_orange"))
        side_stripes(ctx, [(door_name("F", sd), -0.56, 0.5, sd) for sd in (1, -1)], -1.45, 1.5, 0.62, 0.66, "white", "stripe")
        var_part(ctx, "Shelf", "a", "Rear", lambda m: shelf_stuff(m, (0, 1.32, 0.88)), near_only=True)
        var_part(ctx, "Primer", "a", "Bonnet", lambda m: [patch(m, sh, -1.4, -0.63, 0.74, 1.0, "seat_grey", sd, off=0.004,
                                                                 ny=6, ns=2) for sd in (1, -1)])
        rear_mirror(b, (0, -0.62, 1.47))
        ornament(parts, (0, -0.58, 1.38), kind="tasbih")
        antenna(parts, (-0.6, -0.9, 0.9), length=0.62, lean=0.2)
    return build_car(s, details)


# ============================================================================== ALPHARD
def alphard():
    """veh_alphard: a 4th-gen-style luxury MPV - a huge, tall slab: short sloping bonnet, enormous
    glasshouse on blacked-out pillars under a body-colour 'floating' roof, a chrome line along the
    window base that kicks up at the rear, power sliding doors, slim sharp headlamps over a big
    grille of vertical chrome fins, a light bar across the tailgate, twin-spoke 18s, beige leather
    captain's chairs. 4.99 x 1.85 x 1.94 m, 3.0 m wheelbase."""
    W = 1.86
    s = CarSpec(id="veh_alphard", width=W, track=1.6, wr=0.37, ww=0.24, fy=-1.5, ry=1.5, arch_lift=0.02, arch_gap=0.05,
                wheel_style="alloy", spokes=10, floor_z=0.4, roof_z=1.94, seat_x=0.42, seat_y=-0.38, dash_y=-1.08,
                dash_z=0.98, chrome_handles=True, seat_color="towel", seat_bolster="sandal_brown", door_card="pale_panel",
                headliner="cream", rear_seat=False)
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
            side_strip(b, sh, s, -2.1, 2.35, -0.44, -0.38, "chrome", side, off=0.012)               # rocker chrome
            # shut lines: bonnet shoulders and front edge, bumper splits, the tailgate edges
            seam(b, sh, -2.3, -1.66, 0.8, 0.815, side, ny=6)
            seam(b, sh, -2.31, -2.3, 0.8, 1.0, side, ny=1, ns=3)
            seam(b, sh, -2.06, -2.05, -0.45, 0.55, side, ny=1, ns=4)
            seam(b, sh, 2.42, 2.43, -0.3, 0.9, side, ny=1, ns=5)
            patch(b, sh, -1.95, -1.88, 0.3, 0.36, "lamp_amber", side, off=0.006, ny=1, ns=1)       # side repeater
        fuel_cap(b, sh, 1.95, 0.42, 1, r=0.065, color="car_paint")
        for side in (1, -1):
            glass(b, cab, lambda q: -1.78 + (1 - q) * 0.02, ws_edge, 0.02, 1.0, side, ny=4, ns=4)
            glass(b, cab, 2.36, 2.52, 0.3, 1.0, side, ny=2, ns=3)
        wn = Vector((0, -0.55, 0.83)).normalized()
        for x0, x1 in ((0.6, -0.1), (-0.1, -0.78)):
            wiper(b, (x0, -1.72, 1.24), (x1, -1.66, 1.29), wn)
        wiper(b, (0, 2.49, 1.42), (0.5, 2.47, 1.6), Vector((0, 1, 0.15)))
        # front: big grille of vertical chrome fins under a chrome brow, bumper corner vents
        fz = -2.5                                            # the flat front face
        rbox(b, (0, fz + 0.01, 0.6), (1.2, 0.04, 0.48), "grille_black", r=0.2, segs=1)
        for k in range(15):
            x = -0.56 + k * 0.08
            b.box((x, fz - 0.008, 0.6), (0.016, 0.02, 0.42), "chrome")
            b.box((x + 0.04, fz - 0.002, 0.6), (0.008, 0.012, 0.42), "rim_dark")
        rbox(b, (0, fz - 0.01, 0.855), (1.26, 0.03, 0.03), "chrome", r=0.4, segs=1)
        rbox(b, (0, fz - 0.01, 0.345), (1.2, 0.03, 0.025), "chrome", r=0.4, segs=1)
        badge(b, (0, fz - 0.03, 0.8), kind="oval", size=1.2)
        for x in (0.74, -0.74):
            honeycomb(b, (x, fz + 0.005, 0.4), 0.22, 0.14, cell=0.045)
            rbox(b, (x, fz, 0.5), (0.05, 0.03, 0.24), "lamp_white", r=0.5, segs=1)              # vertical LED slots
        plate(b, (0, fz - 0.02, 0.27), text="VIP 88")
        rbox(b, (0, 2.5, 1.06), (1.4, 0.04, 0.05), "chrome", r=0.5, segs=1)                    # tailgate chrome bar
        badge(b, (0, 2.53, 1.0), facing=1, kind="oval")
        plate(b, (0, 2.52, 0.8), facing=1, text="VIP 88")
        rbox(b, (0, 2.48, 0.36), (1.5, 0.06, 0.06), "trim_black", r=0.5, segs=1)
        for x in (0.62, -0.62):
            exhaust(b, (x, 2.52, 0.32), r=0.04, length=0.14)
        parts.append(("FX_Exhaust", None, (0.62, 2.56, 0.32), "Body"))
        shark_fin(b, (0, 1.9, 1.93))

        def heads(lm):
            for side in (1, -1):
                # slim, sharp headlamps set at the top corners, three LED dots + an eyebrow
                x = side * 0.6
                for k in range(3):
                    rbox(lm, (x - side * 0.1 + side * k * 0.08, fz - 0.012, 0.92), (0.05, 0.015, 0.04), "lamp_white", r=0.5,
                         segs=1)
                rbox(lm, (x, fz - 0.012, 0.97), (0.36, 0.015, 0.015), "lamp_white", r=0.5, segs=1)      # LED eyebrow
        lights_part(ctx, "HeadLights", heads)
        for side in (1, -1):
            rbox(b, (side * 0.6, fz, 0.93), (0.42, 0.03, 0.11), "trim_black", r=0.4, segs=1)           # lamp housings
            rbox(b, (side * 0.8, fz - 0.005, 0.9), (0.06, 0.03, 0.05), "lamp_amber", r=0.5, segs=1)

        def tails(lm):
            rbox(lm, (0, 2.51, 1.16), (1.5, 0.03, 0.05), "lamp_red", r=0.5, segs=1)                  # full-width light bar
            for side in (1, -1):
                patch(lm, sh, 2.36, 2.49, 0.3, 0.8, "lamp_red", side, off=0.012, ny=1, ns=3)
                patch(lm, sh, 2.39, 2.46, 0.45, 0.7, "tail_red", side, off=0.016, ny=1, ns=2)
            rbox(lm, (0, 2.46, 1.92), (0.5, 0.04, 0.03), "lamp_red", r=0.5, segs=1)
        lights_part(ctx, "BrakeLights", tails)

        def rev(lm):
            for side in (1, -1):
                patch(lm, sh, 2.42, 2.49, -0.25, -0.05, "lamp_white", side, off=0.012, ny=1, ns=1)
        lights_part(ctx, "ReverseLights", rev)
        # second row: two captain's chairs with armrests, a fold-down table in between
        for x in (-0.44, 0.44):
            seat(b, x, 0.55, s.floor_z + 0.2, w=0.56, back=0.62, color="towel", bolster="sandal_brown", recline=0.3)
            for sgn in (-1, 1):
                rbox(b, (x + sgn * 0.31, 0.6, s.floor_z + 0.36), (0.06, 0.4, 0.05), "sandal_brown", r=0.6, segs=1)
        bench(b, 1.55, s.floor_z + 0.3, 1.04, color="towel", bolster="sandal_brown", heads=3)
        # ---- optional parts (VehicleVariety)
        body_mud(ctx, [(-2.4, -1.12), (1.02, 2.45)], 2023)
        for side in (1, -1):
            door_mud(ctx, door_name("F", side), side, -1.1, -0.3, 2023 + (side > 0))
            door_mud(ctx, door_name("S", side), side, -0.28, 1.0, 2033 + (side > 0))
            var_part(ctx, "Curtain", "a", "Slide" + ("L" if side > 0 else "R"),
                     lambda m, sd=side: curtains(m, sd * 0.8, -0.18, 0.9, 1.27, 1.78), parent=door_name("S", side), near_only=True)
        var_part(ctx, "Curtain", "a", "Rear", lambda m: [curtains(m, sd * 0.8, 1.08, 2.26, 1.27, 1.78) for sd in (1, -1)],
                 near_only=True)
        pc, nc = cab.point(2.47, 0.6, 1), cab.normal(2.47, 0.6, 1)
        var_part(ctx, "Sticker", "club", "Rear", lambda m: round_sticker(m, pc + nc * 0.02, nc, fill="gold", ring="suit_black"))
        rear_mirror(b, (0, -1.15, 1.86))
        ornament(parts, (0, -1.1, 1.76), kind="tasbih")
        antenna(parts, (0, 1.95, 1.96), length=0.08, lean=1.2)   # a stub on the fin
    return build_car(s, details)


# ============================================================================== HILUX
def hilux():
    """veh_hilux: a lifted double-cab 4x4 - bull bar, snorkel, roll bar over the tray, roof rails,
    spare tyre in the back, knobbly mud tyres on dark 6-spoke rims, side steps, mud flaps."""
    W = 1.86
    s = CarSpec(id="veh_hilux", width=W, track=1.6, wr=0.42, ww=0.3, fy=-1.62, ry=1.52, arch_lift=0.06, arch_gap=0.08,
                wheel_style="offroad", spokes=6, knobbly=True, floor_z=0.62, roof_z=2.02, seat_x=0.42, seat_y=-0.35,
                dash_y=-0.98, dash_z=1.2, seat_color="seat_grey", seat_bolster="charcoal", door_card="dash_grey",
                headliner="aircon")
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
        b, core, sh, cab, parts = ctx["body"], ctx["core"], ctx["shell"], ctx["cab"], ctx["parts"]
        # tray behind the cab (its own shell so the arch can be cut too)
        tray = Shell([(0.66, 0.9, 0.52, 0.9, 1.3, 7.0, 7.0, 4.0), (2.5, 0.9, 0.52, 0.9, 1.3, 7.0, 7.0, 4.0),
                      (2.56, 0.86, 0.56, 0.9, 1.26, 5.0, 5.0, 3.0)])
        tm = K.Mesher(2.0)
        shell_mesh(tm, tray, ny=12, ns=14, color_fn=lambda c: "trim_black" if c.z < 0.58 else "car_paint")
        merge_bm(core, cut_arches(tm, [(W / 2 + 0.05, s.ry, s.wr + s.arch_lift, s.wr + s.arch_gap, 0.42),
                                       (-W / 2 - 0.05, s.ry, s.wr + s.arch_lift, s.wr + s.arch_gap, 0.42)]))
        b.box((0, 1.6, 1.305), (1.62, 1.72, 0.02), "bed_liner")                                   # tray floor (on top)
        for k in range(7):                                                                          # liner ribs
            b.box((-0.6 + k * 0.2, 1.6, 1.318), (0.05, 1.66, 0.008), "trim_black")
        for side in (1, -1):                                                                        # bed rails + caps
            rbox(b, (side * 0.86, 1.6, 1.31), (0.09, 1.86, 0.035), "trim_black", r=0.5, segs=1)
        # spare tyre lying in the tray
        spare = K.Mesher(1.0)
        tyre(spare, (0, 0, 0), 0.4, 0.26, 1, 0.4 * 0.64, seg=20, blocks=True)
        rim(spare, (0, 0, 0), 0.4, 0.26, 1, 0.4 * 0.64, style="offroad", spokes=6)
        bmesh.ops.transform(spare.bm, matrix=Matrix.Translation((0, 1.95, 1.46)) @ Matrix.Rotation(-math.pi / 2, 4, 'Y'),
                            verts=spare.bm.verts)
        merge_bm(b, spare.bm, sharp_deg=60)
        # roll bar + tailgate handle and seams + tow hitch
        for x in (0.8, -0.8):
            b.tube((x, 0.78, 1.3), (x * 0.9, 0.8, 1.92), 0.035, 0.035, "chrome", seg=10)
        b.tube((-0.72, 0.8, 1.92), (0.72, 0.8, 1.92), 0.035, 0.035, "chrome", seg=10)
        b.tube((-0.72, 0.8, 1.62), (0.72, 0.8, 1.62), 0.025, 0.025, "chrome", seg=8)
        rbox(b, (0, 2.585, 1.12), (0.22, 0.02, 0.05), "trim_black", r=0.5, segs=1)
        b.box((0, 2.575, 1.27), (1.5, 0.01, 0.006), "well_black")
        put_text(b, "4X4", Vector((0, 2.578, 1.0)), Vector((0, 1, 0)), 0.08, "trim_black", max_w=0.3)
        rbox(b, (0, 2.66, 0.5), (0.1, 0.2, 0.08), "metal_dark", r=0.4, segs=1)
        b.ball((0, 2.76, 0.56), (0.03, 0.03, 0.03), "chrome", seg=8, rings=5)
        for side in (1, -1):
            door(ctx, "Door_FL" if side > 0 else "Door_FR", side, -0.98, -0.12, -0.5, 0.35, 0.7,
                 glass_front=lambda q: -0.9 + q * 0.3)
            door(ctx, "Door_RL" if side > 0 else "Door_RR", side, -0.1, 0.6, -0.5, 0.35, 0.7,
                 glass_rear=lambda q: 0.52)
            mirror_arm(b, sh.point(-0.96, 0.5, side) + Vector((0, 0, 0.06)), side, color="trim_black", size=1.2)
            # side step, fender flares, mud flaps
            rbox(b, (side * (W / 2 + 0.08), -0.36, 0.5), (0.2, 1.8, 0.05), "trim_black", r=0.5, segs=1)
            for k in range(5):
                b.box((side * (W / 2 + 0.08), -1.1 + k * 0.36, 0.527), (0.16, 0.18, 0.008), "metal_dark")
            for y in (s.fy, s.ry):
                a = K.Mesher(1.0)
                revolve(a, (side * (W / 2 + 0.03), y, s.wr + s.arch_lift), [(s.wr + 0.14, -0.08), (s.wr + 0.16, 0.02),
                        (s.wr + 0.08, 0.04)], lambda i: "trim_black", seg=24, side=side, closed=False)
                for f in list(a.bm.faces):   # keep only the upper half of the flare
                    if f.calc_center_median().z < s.wr + s.arch_lift + 0.02:
                        a.bm.faces.remove(f)
                merge_bm(b, a.bm, sharp_deg=70)
                # bolts round the flare
                for k in range(5):
                    ang = math.pi * (0.15 + 0.7 * k / 4)
                    p = Vector((side * (W / 2 + 0.075), y - math.cos(ang) * (s.wr + 0.15), s.wr + s.arch_lift +
                                math.sin(ang) * (s.wr + 0.15)))
                    disc_on(b, p, (side, 0, 0), 0.01, "chrome", seg=6)
                fy_ = y + (s.wr + 0.1)
                rbox(b, (side * (W / 2 - 0.05), fy_, 0.42), (0.26, 0.02, 0.28), "rubber", r=0.3, segs=1)
            # shut lines: bonnet shoulders and front edge, tray / cab gap
            seam(b, sh, -2.4, -1.42, 0.7, 0.715, side, ny=6)
            seam(b, sh, -2.41, -2.4, 0.7, 1.0, side, ny=1, ns=3)
            patch(b, sh, -1.86, -1.8, 0.2, 0.28, "lamp_amber", side, off=0.006, ny=1, ns=1)
            # roof rails
            for yy in (-0.55, 0.45):
                rbox(b, (side * 0.7, yy, 2.04), (0.05, 0.06, 0.05), "trim_black", r=0.5, segs=1)
            rbox(b, (side * 0.7, -0.05, 2.08), (0.04, 1.1, 0.03), "rim_silver", r=0.5, segs=1)
        fuel_cap(b, tray, 1.0, 0.4, 1, r=0.065, color="car_paint")
        # snorkel up the right A-pillar
        b.tube((-0.95, -1.35, 1.05), (-0.95, -1.2, 1.9), 0.05, 0.05, "trim_black", seg=10)
        rbox(b, (-0.95, -1.24, 1.96), (0.12, 0.16, 0.1), "trim_black", r=0.5, segs=1)
        for k in range(3):
            b.box((-0.95, -1.325, 1.94 + k * 0.025), (0.1, 0.004, 0.008), "rim_dark")
        glass(b, cab, lambda q: -1.36 + (1 - q) * 0.08, lambda q: -0.98 - q * 0.05, 0.02, 1.0, 1, ny=4, ns=3)
        glass(b, cab, lambda q: -1.36 + (1 - q) * 0.08, lambda q: -0.98 - q * 0.05, 0.02, 1.0, -1, ny=4, ns=3)
        wn = Vector((0, -0.6, 0.8)).normalized()
        for x0, x1 in ((0.6, -0.08), (-0.08, -0.72)):
            wiper(b, (x0, -1.32, 1.36), (x1, -1.26, 1.42), wn)
        # bull bar + grille + roof light bar
        grille(b, (0, -2.575, 0.92), 1.16, 0.3, bars=3, bar_color="chrome", frame="chrome", depth=0.04)
        badge(b, (0, -2.605, 0.93), kind="oval", size=1.3)
        for x in (0.45, -0.45):
            b.tube((x, -2.66, 0.45), (x, -2.62, 1.14), 0.04, 0.04, "chrome", seg=10)
        b.tube((-0.55, -2.64, 1.1), (0.55, -2.64, 1.1), 0.04, 0.04, "chrome", seg=10)
        b.tube((-0.9, -2.64, 0.62), (0.9, -2.64, 0.62), 0.045, 0.045, "chrome", seg=10)
        for x in (0.75, -0.75):     # spot lamps on the bar (unlit)
            b.cyl((x, -2.69, 0.72), 0.07, 0.065, 0.06, "trim_black", seg=12, rot=(math.pi / 2, 0, 0))
            disc_on(b, (x, -2.721, 0.72), (0, -1, 0), 0.058, "lens_clear", seg=12)
        rbox(b, (0, -2.58, 0.55), (W, 0.14, 0.14), "trim_black", r=0.5, segs=1)
        plate(b, (0, -2.72, 0.5), text="JHR 4471")
        plate(b, (0, 2.6, 0.82), facing=1, text="JHR 4471")
        rbox(b, (0, 2.6, 0.62), (W - 0.1, 0.12, 0.1), "trim_black", r=0.5, segs=1)                  # rear step bumper
        rbox(b, (0, -0.8, 2.07), (1.3, 0.14, 0.08), "trim_black", r=0.5, segs=1)
        for k in range(6):
            rbox(b, (-0.5 + k * 0.2, -0.875, 2.07), (0.12, 0.02, 0.05), "lens_clear", r=0.5, segs=1)
        exhaust(b, (0.6, 2.5, 0.5), r=0.04, length=0.2)
        parts.append(("FX_Exhaust", None, (0.6, 2.52, 0.5), "Body"))

        def heads(lm):
            for side in (1, -1):
                patch(lm, sh, -2.5, -2.3, 0.15, 0.7, "lens_clear", side, off=0.012, ny=2, ns=3)
                patch(lm, sh, -2.49, -2.43, 0.3, 0.55, "lamp_white", side, off=0.016, ny=1, ns=1)
                patch(lm, sh, -2.42, -2.36, 0.3, 0.55, "chrome", side, off=0.015, ny=1, ns=1)
        lights_part(ctx, "HeadLights", heads)
        for side in (1, -1):
            patch(b, sh, -2.36, -2.3, 0.2, 0.4, "lamp_amber", side, off=0.016, ny=1, ns=1)

        def tails(lm):
            for side in (1, -1):
                rbox(lm, (side * 0.86, 2.56, 1.02), (0.1, 0.04, 0.36), "lamp_red", r=0.4, segs=1)
                for k in range(3):
                    lm.box((side * 0.86, 2.582, 0.92 + k * 0.1), (0.08, 0.004, 0.03), "tail_red")
        lights_part(ctx, "BrakeLights", tails)
        for side in (1, -1):
            rbox(b, (side * 0.86, 2.565, 1.24), (0.09, 0.03, 0.07), "lamp_amber", r=0.4, segs=1)

        def rev(lm):
            for side in (1, -1):
                rbox(lm, (side * 0.86, 2.57, 0.8), (0.09, 0.03, 0.08), "lamp_white", r=0.4, segs=1)
        lights_part(ctx, "ReverseLights", rev)
        # ---- optional parts (VehicleVariety)
        def hilux_mud(m):
            for side in (1, -1):
                mud(m, sh, s, side, -2.45, -1.0, 2005 + (side > 0), s_lo=-0.8, s_hi=0.0, n=12)
                mud(m, tray, s, side, 0.7, 2.5, 2015 + (side > 0), s_lo=-0.8, s_hi=0.0, n=14)
        var_part(ctx, "Dirt", "a", "Body", hilux_mud, near_only=True)
        for side in (1, -1):
            door_mud(ctx, door_name("F", side), side, -0.98, -0.12, 2025 + (side > 0))
            door_mud(ctx, door_name("R", side), side, -0.1, 0.6, 2035 + (side > 0))

        def crates(m):
            cols = ("crate_red", "crate_blue", "crate_yellow", "crate_green")
            for k, (x, y, z) in enumerate(((-0.45, 0.95, 0), (0.0, 0.95, 0), (0.45, 0.95, 0), (-0.22, 0.95, 1), (0.22, 0.95, 1),
                                           (-0.45, 1.32, 0), (0.45, 1.32, 0))):
                c = Vector((x, y, 1.42 + z * 0.25))
                rbox(m, c, (0.42, 0.34, 0.24), cols[k % 4], r=0.2, segs=1)
                for j in range(3):
                    m.ball(c + Vector(((j - 1) * 0.12, 0, 0.13)), (0.06, 0.06, 0.05), "fruit_green" if k % 2 else "fruit_orange",
                           seg=8, rings=5)
        var_part(ctx, "Load", "crates", "Tray", crates)

        def gas(m):
            for k, (x, y) in enumerate(((-0.5, 0.95), (-0.2, 0.95), (0.1, 0.95), (-0.5, 1.25), (-0.2, 1.25))):
                m.cyl((x, y, 1.6), 0.13, 0.13, 0.55, "shop_teal" if k % 2 else "shop_red", seg=12)
                m.cyl((x, y, 1.9), 0.06, 0.04, 0.08, "metal", seg=8)
        var_part(ctx, "Load", "gas", "Tray", gas)
        side_stripes(ctx, [(door_name(k, sd), *((-0.98, -0.12) if k == "F" else (-0.1, 0.6)), sd) for k in ("F", "R")
                           for sd in (1, -1)], -2.4, 0.6, 1.04, 1.09, "white", "stripe")
        rear_mirror(b, (0, -0.98, 1.95))
        ornament(parts, (0, -0.94, 1.86), kind="freshener")
        antenna(parts, (0.9, -1.3, 1.3), length=0.9, lean=0.15)
    return build_car(s, details)


# ============================================================================== IRIZ-STYLE HATCH
def iriz():
    """veh_iriz: a sporty 5-door city hatch (in the spirit of the Iriz, no real badges): low pointed nose,
    big swept headlamps joined by a chrome 'wing' bar over a black grille, a rising window line, blacked-out
    pillars, roof spoiler, silver 5-spoke alloys. 3.9 x 1.72 x 1.55 m, 2.55 m wheelbase."""
    W = 1.72
    s = CarSpec(id="veh_iriz", width=W, track=1.46, wr=0.31, ww=0.2, fy=-1.24, ry=1.31, arch_lift=0.02, arch_gap=0.05,
                wheel_style="alloy", spokes=5, floor_z=0.3, roof_z=1.47, seat_x=0.37, seat_y=0.0, dash_y=-0.72, dash_z=0.84,
                seat_color="seat_grey", seat_bolster="charcoal", door_card="dash_grey", headliner="aircon", handle_color="car_paint")
    s.pillar_color = "trim_black"
    s.stations = [(-1.95, 0.66, 0.30, 0.46, 0.58, 3.0, 2.4, 2.4),
                  (-1.9, 0.80, 0.24, 0.48, 0.66, 4.0, 3.0, 3.2),
                  (-1.65, 0.84, 0.22, 0.53, 0.76, 5.0, 3.4, 4.0),
                  (-1.1, 0.855, 0.22, 0.58, 0.88, 5.4, 3.6, 4.2),
                  (0.4, 0.855, 0.22, 0.60, 0.94, 5.4, 3.8, 4.2),
                  (1.5, 0.85, 0.24, 0.62, 0.98, 5.0, 3.6, 4.0),
                  (1.85, 0.80, 0.27, 0.62, 0.98, 4.2, 3.0, 3.2),
                  (1.95, 0.70, 0.33, 0.62, 0.94, 3.0, 2.4, 2.4)]
    s.cab_stations = [(-1.15, 0.70, 0.84, 0.90, 0.93, 3.0, 2.4, 2.0),
                      (-0.85, 0.76, 0.84, 0.92, 1.16, 3.4, 3.4, 2.0),
                      (-0.25, 0.77, 0.84, 0.94, 1.46, 3.6, 4.6, 2.0),
                      (1.05, 0.76, 0.84, 0.97, 1.47, 3.6, 4.6, 2.0),
                      (1.55, 0.72, 0.84, 0.98, 1.36, 3.4, 4.0, 2.0),
                      (1.88, 0.60, 0.84, 0.98, 1.04, 3.0, 2.6, 2.0)]
    s.y_breaks, s.s_breaks, s.cab_breaks = (-1.65, 1.85), (-0.55,), ()
    s.body_color = lambda c: "trim_black" if c.z < 0.3 else "car_paint"
    s.cab_color = lambda c: "car_paint" if c.z > 1.39 else "trim_black"
    s.belt_s = 0.04

    def details(ctx):
        b, sh, cab, parts = ctx["body"], ctx["shell"], ctx["cab"], ctx["parts"]
        ws_edge = lambda q: -1.0 + q * 0.68
        doors = {}
        spans = {"F": (-0.9, 0.32), "R": (0.34, 1.2)}
        for side in (1, -1):
            doors["F", side] = door(ctx, door_name("F", side), side, *spans["F"], -0.5, 0.2, 0.52,
                                    glass_front=lambda q: ws_edge(q) + 0.09, glass_rear=lambda q: 0.26)
            doors["R", side] = door(ctx, door_name("R", side), side, *spans["R"], -0.5, 0.2, 0.52,
                                    glass_front=lambda q: 0.4, glass_rear=lambda q: 1.08 - q * 0.22)
            glass(b, cab, lambda q: 1.22 - q * 0.04, lambda q: 1.5 - q * 0.22, 0.05, 0.4, side, ny=3, ns=3)   # kinked quarter
            mirror_arm(b, sh.point(-0.98, 0.42, side) + Vector((0, 0.02, 0.05)), side, color="car_paint")
            patch(b, sh, -1.32, -1.25, 0.2, 0.27, "lamp_amber", side, off=0.006, ny=1, ns=1)
            side_strip(b, sh, s, -1.3, 1.6, -0.48, -0.4, "trim_black", side, off=0.01, thick=0.02)
        fuel_cap(b, sh, 1.45, 0.3, 1, r=0.055)
        for side in (1, -1):
            glass(b, cab, lambda q: -1.12 + (1 - q) * 0.02, ws_edge, 0.02, 1.0, side, ny=4, ns=4)
            glass(b, cab, lambda q: 1.8 - q * 0.28, 1.86, 0.4, 1.0, side, ny=2, ns=3)
        wn = Vector((0, -0.5, 0.86)).normalized()
        for x0, x1 in ((0.52, -0.06), (-0.06, -0.64)):
            wiper(b, (x0, -1.09, 0.95), (x1, -1.03, 1.0), wn)
        wiper(b, (0, 1.83, 1.12), (0.4, 1.78, 1.28), Vector((0, 0.9, 0.3)))
        for side in (1, -1):
            patch(b, cab, 1.25, 1.62, 0.9, 1.0, "car_paint", side, off=0.02, ny=3, ns=2, thick=0.03)
        fz = -1.95
        # the chrome 'wing' bar sweeping into the lamps, a slim upper grille, a wide black mouth with fog lamps
        rbox(b, (0, fz + 0.01, 0.6), (0.56, 0.05, 0.08), "grille_black", r=0.6, segs=1)
        rbox(b, (0, fz - 0.016, 0.645), (0.9, 0.02, 0.025), "chrome", r=0.6, segs=1)
        badge(b, (0, fz - 0.03, 0.6), kind="shield", size=0.8)
        honeycomb(b, (0, fz + 0.01, 0.39), 1.0, 0.17)
        for x in (0.66, -0.66):
            rbox(b, (x, fz + 0.03, 0.4), (0.18, 0.05, 0.12), "grille_black", r=0.5, segs=1)
            disc_on(b, (x, fz + 0.001, 0.4), (0, -1, 0), 0.045, "lens_clear", seg=12)
        plate(b, (0, fz - 0.02, 0.27), w=0.42, h=0.1, text="PKR 3388")
        for side in (1, -1):
            patch(b, sh, -1.94, lambda q: -1.72 + max(0.0, q - 0.25) * 0.5, 0.12, 0.62, "trim_black", side, off=0.006, ny=4, ns=3)

        def heads(lm):
            for side in (1, -1):
                patch(lm, sh, -1.935, lambda q: -1.74 + max(0.0, q - 0.27) * 0.46, 0.16, 0.6, "lens_clear", side, off=0.014,
                      ny=4, ns=3)
                p = sh.point(-1.86, 0.4, side)
                n = sh.normal(-1.86, 0.4, side)
                disc_on(lm, p + n * 0.017, n, 0.045, "chrome", seg=12)
                disc_on(lm, p + n * 0.02, n, 0.026, "lamp_white", seg=10)
        lights_part(ctx, "HeadLights", heads)

        def tails(lm):
            for side in (1, -1):    # wide tail lamps wrapping round the rear corners
                patch(lm, sh, 1.78, 1.94, 0.1, 0.45, "lamp_red", side, off=0.012, ny=2, ns=2)
            rbox(lm, (0, 1.6, cab.point(1.5, 1.0, 1).z + 0.025), (0.2, 0.03, 0.025), "lamp_red", r=0.5, segs=1)
        lights_part(ctx, "BrakeLights", tails)

        def rev(lm):
            for side in (1, -1):
                patch(lm, sh, 1.86, 1.94, 0.0, 0.1, "lamp_white", side, off=0.013, ny=1, ns=1)
        lights_part(ctx, "ReverseLights", rev)
        ry_ = 1.955
        for x in (0.48, -0.48):
            b.box((x, ry_, 0.72), (0.006, 0.01, 0.4), "well_black")
        badge(b, (0, ry_ + 0.012, 0.82), facing=1, kind="shield", size=0.7)
        plate(b, (0, ry_ + 0.012, 0.6), facing=1, text="PKR 3388")
        rbox(b, (0, ry_ - 0.02, 0.32), (1.2, 0.05, 0.06), "trim_black", r=0.5, segs=1)
        exhaust(b, (0.46, 1.99, 0.29), r=0.034)
        parts.append(("FX_Exhaust", None, (0.46, 2.01, 0.29), "Body"))
        rear_mirror(b, (0, -0.78, 1.44))
        ornament(parts, (0, -0.74, 1.34), kind="freshener")
        shark_fin(b, (0, 1.25, 1.52))
        # ---- optional parts (VehicleVariety)
        body_mud(ctx, [(-1.9, -0.92), (1.22, 1.92)], 2016)
        for (key, side) in doors:
            door_mud(ctx, door_name(key, side), side, *spans[key], 2016 + (10 if key == "F" else 20) + (side > 0))
        var_part(ctx, "Rack", "a", "Roof", lambda m: roof_rack(m, cab, -0.2, 1.05))
        pr, nr = cab.point(1.76, 0.62, -1), cab.normal(1.76, 0.62, -1)
        var_part(ctx, "Sticker", "p", "Rear", lambda m: p_sticker(m, pr + nr * 0.02, nr))
        pc, nc = cab.point(1.76, 0.62, 1), cab.normal(1.76, 0.62, 1)
        var_part(ctx, "Sticker", "club", "Rear", lambda m: round_sticker(m, pc + nc * 0.02, nc, fill="shop_blue"))
        side_stripes(ctx, [(door_name(k, sd), *spans[k], sd) for (k, sd) in doors], -1.8, 1.9, 0.44, 0.48, "white", "stripe")
        var_part(ctx, "Shelf", "a", "Rear", lambda m: shelf_stuff(m, (0, 1.5, 0.99)), near_only=True)
    return build_car(s, details)


# ============================================================================== FAMILY MPV
def mpv():
    """veh_mpv: a family seven-seater MPV - the e-hailing favourite (Alza / Exora-style, generic): tall rounded
    nose, a long glasshouse with three rows of windows, roof rails, a chrome grille bar, silver alloys.
    4.4 x 1.72 x 1.66 m, 2.75 m wheelbase."""
    W = 1.72
    s = CarSpec(id="veh_mpv", width=W, track=1.48, wr=0.32, ww=0.21, fy=-1.38, ry=1.37, arch_lift=0.02, arch_gap=0.05,
                wheel_style="alloy", spokes=6, floor_z=0.32, roof_z=1.66, seat_x=0.38, seat_y=-0.15, dash_y=-0.86, dash_z=0.9,
                seat_color="seat_grey", seat_bolster="dash_grey", door_card="seat_grey", headliner="cream", chrome_handles=True)
    s.pillar_color = "trim_black"
    s.stations = [(-2.2, 0.68, 0.32, 0.50, 0.66, 3.0, 2.4, 2.4),
                  (-2.14, 0.80, 0.26, 0.52, 0.74, 4.0, 3.0, 3.2),
                  (-1.85, 0.85, 0.24, 0.57, 0.86, 5.0, 3.4, 4.0),
                  (-1.2, 0.86, 0.24, 0.62, 0.98, 5.6, 3.8, 4.2),
                  (1.7, 0.86, 0.25, 0.66, 1.04, 5.6, 3.8, 4.2),
                  (2.1, 0.83, 0.28, 0.66, 1.04, 4.6, 3.2, 3.4),
                  (2.2, 0.72, 0.34, 0.66, 1.00, 3.0, 2.4, 2.4)]
    s.cab_stations = [(-1.3, 0.72, 0.92, 0.98, 1.02, 3.0, 2.4, 2.0),
                      (-1.0, 0.80, 0.92, 1.00, 1.30, 3.4, 3.4, 2.0),
                      (-0.45, 0.81, 0.92, 1.02, 1.65, 3.8, 5.0, 2.0),
                      (1.8, 0.80, 0.92, 1.06, 1.66, 3.8, 5.0, 2.0),
                      (2.05, 0.76, 0.92, 1.06, 1.58, 3.4, 4.0, 2.0),
                      (2.2, 0.66, 0.92, 1.06, 1.16, 3.0, 2.6, 2.0)]
    s.y_breaks, s.s_breaks, s.cab_breaks = (-1.85, 2.1), (-0.55,), ()
    s.body_color = lambda c: "trim_black" if c.z < 0.32 else "car_paint"
    s.cab_color = lambda c: "car_paint" if c.z > 1.6 else "trim_black"
    s.belt_s = 0.04

    def details(ctx):
        b, sh, cab, parts = ctx["body"], ctx["shell"], ctx["cab"], ctx["parts"]
        ws_edge = lambda q: -1.12 + q * 0.66
        doors = {}
        spans = {"F": (-1.02, 0.12), "R": (0.14, 1.05)}
        for side in (1, -1):
            doors["F", side] = door(ctx, door_name("F", side), side, *spans["F"], -0.5, 0.22, 0.6,
                                    glass_front=lambda q: ws_edge(q) + 0.1, glass_rear=lambda q: 0.06)
            doors["R", side] = door(ctx, door_name("R", side), side, *spans["R"], -0.5, 0.22, 0.6,
                                    glass_front=lambda q: 0.2, glass_rear=lambda q: 0.98)
            glass(b, cab, 1.1, lambda q: 2.0 - q * 0.12, 0.05, 0.6, side, ny=4, ns=3)
            mirror_arm(b, sh.point(-1.1, 0.45, side) + Vector((0, 0.02, 0.06)), side, color="car_paint", size=1.05)
            side_strip(b, sh, s, -1.95, 2.0, -0.45, -0.38, "trim_black", side, off=0.01, thick=0.02)
            patch(b, sh, -1.55, -1.48, 0.25, 0.32, "lamp_amber", side, off=0.006, ny=1, ns=1)
            # roof rails
            for yy in (-0.3, 1.7):
                rbox(b, (side * 0.62, yy, shell_top_z(cab, 0.62, yy) + 0.02), (0.05, 0.08, 0.05), "trim_black", r=0.5, segs=1)
            rbox(b, (side * 0.62, 0.7, shell_top_z(cab, 0.62, 0.7) + 0.06), (0.045, 2.1, 0.035), "rim_silver", r=0.5, segs=1)
        fuel_cap(b, sh, 1.75, 0.3, 1, r=0.06)
        for side in (1, -1):
            glass(b, cab, lambda q: -1.28 + (1 - q) * 0.02, ws_edge, 0.02, 1.0, side, ny=4, ns=4)
            glass(b, cab, 2.06, 2.2, 0.3, 1.0, side, ny=2, ns=3)
        wn = Vector((0, -0.55, 0.83)).normalized()
        for x0, x1 in ((0.54, -0.06), (-0.06, -0.66)):
            wiper(b, (x0, -1.22, 1.03), (x1, -1.16, 1.08), wn)
        wiper(b, (0, 2.17, 1.22), (0.44, 2.13, 1.4), Vector((0, 1, 0.2)))
        fz = -2.2
        rbox(b, (0, fz + 0.01, 0.6), (0.7, 0.05, 0.12), "grille_black", r=0.4, segs=1)
        rbox(b, (0, fz - 0.016, 0.63), (0.74, 0.02, 0.03), "chrome", r=0.6, segs=1)
        badge(b, (0, fz - 0.03, 0.6), kind="oval", size=0.9)
        honeycomb(b, (0, fz + 0.02, 0.4), 0.9, 0.16)
        for x in (0.62, -0.62):
            rbox(b, (x, fz + 0.04, 0.42), (0.18, 0.05, 0.12), "grille_black", r=0.5, segs=1)
            disc_on(b, (x, fz + 0.011, 0.42), (0, -1, 0), 0.045, "lens_clear", seg=12)
        plate(b, (0, fz - 0.02, 0.28), text="VBN 2468")
        for side in (1, -1):
            patch(b, sh, -2.18, lambda q: -1.94 + max(0.0, q - 0.3) * 0.5, 0.15, 0.7, "trim_black", side, off=0.006, ny=4, ns=3)

        def heads(lm):
            for side in (1, -1):
                patch(lm, sh, -2.17, lambda q: -1.96 + max(0.0, q - 0.32) * 0.46, 0.2, 0.68, "lens_clear", side, off=0.014,
                      ny=4, ns=3)
                p = sh.point(-2.08, 0.45, side)
                n = sh.normal(-2.08, 0.45, side)
                disc_on(lm, p + n * 0.017, n, 0.05, "chrome", seg=12)
                disc_on(lm, p + n * 0.02, n, 0.03, "lamp_white", seg=10)
        lights_part(ctx, "HeadLights", heads)

        def tails(lm):
            for side in (1, -1):    # tall L-shaped lamps up the D-pillars
                patch(lm, sh, 2.06, 2.19, 0.1, 0.62, "lamp_red", side, off=0.012, ny=1, ns=3)
            rbox(lm, (0, 2.04, 1.62), (0.4, 0.04, 0.03), "lamp_red", r=0.5, segs=1)
        lights_part(ctx, "BrakeLights", tails)

        def rev(lm):
            for side in (1, -1):
                patch(lm, sh, 2.12, 2.19, -0.15, 0.05, "lamp_white", side, off=0.013, ny=1, ns=1)
        lights_part(ctx, "ReverseLights", rev)
        ry_ = 2.205
        rbox(b, (0, ry_, 0.86), (0.6, 0.02, 0.05), "chrome", r=0.5, segs=1)
        plate(b, (0, ry_ + 0.012, 0.64), facing=1, text="VBN 2468")
        rbox(b, (0, ry_ - 0.02, 0.34), (1.3, 0.05, 0.06), "trim_black", r=0.5, segs=1)
        exhaust(b, (0.5, 2.24, 0.3), r=0.034)
        parts.append(("FX_Exhaust", None, (0.5, 2.26, 0.3), "Body"))
        bench(b, 1.45, s.floor_z + 0.3, 0.9, color="seat_grey", bolster="dash_grey", heads=2)      # third row
        rear_mirror(b, (0, -0.95, 1.58))
        ornament(parts, (0, -0.9, 1.48), kind="tasbih")
        shark_fin(b, (0, 1.85, 1.66))
        # ---- optional parts (VehicleVariety)
        body_mud(ctx, [(-2.15, -1.04), (1.07, 2.15)], 2018)
        for (key, side) in doors:
            door_mud(ctx, door_name(key, side), side, *spans[key], 2018 + (10 if key == "F" else 20) + (side > 0))

        def pod(m):   # a roof luggage box on the rails
            y = 0.7
            z = shell_top_z(cab, 0, y) + 0.1
            rbox(m, (0, y, z + 0.11), (0.9, 1.5, 0.24), "trim_black", r=0.6, segs=2)
            rbox(m, (0, y, z + 0.235), (0.8, 1.38, 0.02), "charcoal", r=0.6, segs=1)
            for x in (0.42, -0.42):
                m.box((x, y, z - 0.02), (0.04, 0.9, 0.04), "rim_dark")
        var_part(ctx, "Rack", "pod", "Roof", pod)
        pr, nr = cab.point(2.12, 0.62, -1), cab.normal(2.12, 0.62, -1)
        var_part(ctx, "Sticker", "p", "Rear", lambda m: p_sticker(m, pr + nr * 0.02, nr))
        pc, nc = cab.point(2.12, 0.62, 1), cab.normal(2.12, 0.62, 1)
        var_part(ctx, "Sticker", "club", "Rear", lambda m: round_sticker(m, pc + nc * 0.02, nc, fill="shop_green"))
        var_part(ctx, "Curtain", "a", "Rear", lambda m: [curtains(m, sd * 0.76, 1.12, 1.98, 1.12, 1.52, color="tudung_blue",
                                                                   fold="aircon") for sd in (1, -1)], near_only=True)
        var_part(ctx, "Shelf", "a", "Rear", lambda m: shelf_stuff(m, (0, 1.95, 1.07)), near_only=True)
    return build_car(s, details)


# ============================================================================== 3-TONNE LORRY
def lorry():
    """veh_lorry: the everyday 3-tonne lorry - a boxy cab-over cab with a near-flat face, big round-cornered
    windscreen, chunky bumper and steps, a long timber dropside deck behind a steel headboard, dark steel
    wheels. 6.6 x 2.0 x 2.5 m."""
    W = 2.0
    s = CarSpec(id="veh_lorry", width=W, track=1.66, wr=0.45, ww=0.3, fy=-2.3, ry=1.55, arch_lift=0.03, arch_gap=0.07,
                wheel_style="offroad", spokes=6, floor_z=1.05, roof_z=2.48, seat_x=0.45, seat_y=-2.45, dash_y=-2.98,
                dash_z=1.6, seat_color="seat_grey", seat_bolster="dash_grey", door_card="dash_grey", headliner="aircon",
                rear_seat=False, b_pillar=False, carpet_len=1.2, carpet_shift=0.25)
    s.stations = [(-3.25, 0.90, 0.60, 1.00, 1.40, 6.0, 4.0, 4.0),
                  (-3.2, 0.98, 0.55, 1.02, 1.48, 8.0, 5.0, 5.0),
                  (-3.0, 1.0, 0.55, 1.04, 1.52, 9.0, 6.0, 6.0),
                  (-1.65, 1.0, 0.55, 1.04, 1.52, 9.0, 6.0, 6.0),
                  (-1.58, 0.96, 0.60, 1.04, 1.48, 7.0, 5.0, 5.0)]
    s.cab_stations = [(-3.17, 0.92, 1.40, 1.47, 1.52, 5.0, 3.0, 2.0),
                      (-3.04, 0.97, 1.40, 1.48, 2.36, 7.0, 6.0, 2.0),
                      (-2.85, 0.98, 1.40, 1.49, 2.48, 7.0, 7.0, 2.0),
                      (-1.7, 0.98, 1.40, 1.49, 2.48, 7.0, 7.0, 2.0),
                      (-1.6, 0.92, 1.40, 1.49, 2.3, 5.0, 4.0, 2.0)]
    s.y_breaks, s.s_breaks, s.cab_breaks = (-3.0,), (-0.5,), ()
    s.body_color = lambda c: "trim_black" if c.z < 0.62 else "car_paint"
    s.cab_color = lambda c: "car_paint2" if c.z > 2.3 else "car_paint"
    s.col_box = ((0, 0.05, 1.5), (2.1, 6.6, 1.9))

    def details(ctx):
        b, core, sh, cab, parts = ctx["body"], ctx["core"], ctx["shell"], ctx["cab"], ctx["parts"]
        ws_edge = lambda q: -3.06 + q * 0.14
        for side in (1, -1):
            door(ctx, door_name("F", side), side, -2.98, -1.95, -0.5, 0.3, 0.75, glass_front=lambda q: -2.9 + q * 0.1,
                 glass_rear=lambda q: -2.05)
            glass(b, cab, -1.92, -1.72, 0.05, 0.75, side, ny=2, ns=3)                       # little rear side window
            mirror_arm(b, cab.point(-3.0, 0.3, side) + Vector((side * 0.02, 0.02, 0.0)), side, color="trim_black", size=1.6)
            # steps under the doors
            for k, z in enumerate((0.72, 0.45)):
                rbox(b, (side * (W / 2 - 0.02), -2.5, z), (0.16, 0.42, 0.05), "rim_dark", r=0.5, segs=1)
        for side in (1, -1):
            glass(b, cab, lambda q: -3.15 + (1 - q) * 0.02, ws_edge, 0.02, 1.0, side, ny=4, ns=4)
        wn = Vector((0, -0.9, 0.3)).normalized()
        for x0, x1 in ((0.7, 0.05), (-0.05, -0.75)):
            wiper(b, (x0, -3.11, 1.58), (x1, -3.1, 1.72), wn)
        # chunky front: grille slats, big bumper, square lamps (lit lens in HeadLights), plate
        fz = -3.25
        grille(b, (0, fz, 1.2), 1.1, 0.26, bars=4, bar_color="rim_silver", back="grille_black", depth=0.05)
        badge(b, (0, fz - 0.03, 1.38), kind="oval", size=1.4)
        rbox(b, (0, fz - 0.04, 0.68), (W + 0.06, 0.16, 0.2), "rim_dark", r=0.4, segs=2)
        plate(b, (0, fz - 0.125, 0.68), w=0.5, h=0.12, text="BKN 3311")
        for x in (0.75, -0.75):
            rbox(b, (x, fz + 0.01, 1.2), (0.28, 0.05, 0.24), "trim_black", r=0.3, segs=1)
            lamp_lens(b, (x * 1.18, fz - 0.06, 0.68), (0.14, 0.03, 0.08), "lamp_amber")

        def heads(lm):
            for x in (0.75, -0.75):
                rbox(lm, (x, fz - 0.02, 1.2), (0.24, 0.02, 0.2), "lens_clear", r=0.3, segs=1)
                rbox(lm, (x, fz - 0.032, 1.2), (0.1, 0.006, 0.1), "lamp_white", r=0.5, segs=1)
        lights_part(ctx, "HeadLights", heads)
        # roof: a sun visor over the windscreen and two marker lamps
        rbox(b, (0, -3.12, 2.42), (1.8, 0.16, 0.05), "car_paint2", r=0.5, segs=1)
        for x in (0.6, -0.6):
            rbox(b, (x, -3.05, 2.5), (0.12, 0.06, 0.05), "lamp_amber", r=0.5, segs=1)
        # chassis, fuel tank, the deck with timber dropsides, steel headboard, mudguards, under-run bars
        for x in (0.42, -0.42):
            b.box((x, 0.3, 0.75), (0.12, 6.0, 0.2), "rim_dark")
        b.cyl((-0.85, -0.9, 0.78), 0.22, 0.22, 0.9, "rim_silver", seg=14, rot=(math.pi / 2, 0, 0))
        deck_y0, deck_y1, dz = -1.45, 3.3, 1.12
        b.box((0, (deck_y0 + deck_y1) / 2, dz), (W + 0.1, deck_y1 - deck_y0, 0.1), "timber")
        for k in range(9):
            b.box((0, deck_y0 + 0.1 + k * 0.58, dz - 0.08), (W + 0.12, 0.08, 0.08), "rim_dark")      # cross members
        for side in (1, -1):
            x = side * (W / 2 + 0.02)
            for k in range(3):
                b.box((x, (deck_y0 + deck_y1) / 2, dz + 0.12 + k * 0.17), (0.06, deck_y1 - deck_y0, 0.14), "timber")
            for k in range(5):
                b.box((x * 1.01, deck_y0 + 0.05 + k * (deck_y1 - deck_y0 - 0.1) / 4, dz + 0.27), (0.08, 0.08, 0.56), "timber_dark")
            b.box((side * (W / 2 + 0.02), 0.2, 0.7), (0.05, 2.0, 0.06), "rim_dark")                # under-run bar
            a = K.Mesher(1.0)
            revolve(a, (side * (W / 2 - 0.02), s.ry, s.wr + s.arch_lift), [(s.wr + 0.12, -0.17), (s.wr + 0.12, 0.17)],
                    lambda i: "trim_black", seg=20, side=side, closed=False, double=True)
            for f in list(a.bm.faces):
                if f.calc_center_median().z < s.wr + 0.1 or f.calc_center_median().z > dz - 0.04:
                    a.bm.faces.remove(f)
            merge_bm(b, a.bm, sharp_deg=70)
        for k in range(3):                                                                           # tailgate boards
            b.box((0, deck_y1 + 0.02, dz + 0.12 + k * 0.17), (W + 0.08, 0.06, 0.14), "timber")
        b.box((0, deck_y0 + 0.03, dz + 0.55), (W + 0.1, 0.06, 0.95), "rim_silver")                  # headboard
        for k in range(4):
            b.box((0, deck_y0 + 0.07, dz + 0.2 + k * 0.22), (W, 0.03, 0.04), "metal_dark")
        rbox(b, (0, deck_y1 + 0.08, 0.86), (W, 0.12, 0.12), "rim_dark", r=0.3, segs=1)              # rear bumper
        plate(b, (0, deck_y1 + 0.15, 0.86), facing=1, w=0.5, h=0.12, text="BKN 3311")
        exhaust(b, (0.75, -1.4, 0.62), r=0.04, length=0.2)
        parts.append(("FX_Exhaust", None, (0.75, -1.3, 0.62), "Body"))

        def tails(lm):
            for x in (0.8, -0.8):
                rbox(lm, (x, deck_y1 + 0.15, 0.86), (0.22, 0.03, 0.1), "lamp_red", r=0.4, segs=1)
        lights_part(ctx, "BrakeLights", tails)

        def rev(lm):
            for x in (0.55, -0.55):
                rbox(lm, (x, deck_y1 + 0.15, 0.86), (0.08, 0.03, 0.08), "lamp_white", r=0.4, segs=1)
        lights_part(ctx, "ReverseLights", rev)
        rear_mirror(b, (0, -3.0, 2.35))
        ornament(parts, (0, -2.98, 2.25), kind="tasbih")
        # ---- optional parts (VehicleVariety): what's on the deck
        def sacks(m):
            for k in range(10):
                x = -0.55 + (k % 3) * 0.55
                y = deck_y0 + 0.5 + (k // 3) * 0.75
                rbox(m, (x, y, dz + 0.2 + (k % 2) * 0.02), (0.5, 0.7, 0.28), "sole" if k % 2 else "cream", r=0.7, segs=1)
        var_part(ctx, "Load", "sacks", "Deck", sacks)

        def tarp(m):
            rbox(m, (0, (deck_y0 + deck_y1) / 2 - 0.3, dz + 0.45), (W - 0.1, 3.4, 0.8), "tarp_blue", r=0.35, segs=2)
            for k in range(4):
                m.box((0, deck_y0 + 0.6 + k * 0.95, dz + 0.86), (W - 0.06, 0.03, 0.02), "trim_black")
        var_part(ctx, "Load", "tarp", "Deck", tarp)

        def fruit(m):
            cols = ("crate_red", "crate_blue", "crate_yellow", "crate_green")
            for k in range(12):
                x = -0.6 + (k % 3) * 0.6
                y = deck_y0 + 0.5 + (k // 3) * 0.5 + (k // 6) * 0.6
                c = Vector((x, y, dz + 0.2 + (k % 2) * 0.27))
                rbox(m, c, (0.5, 0.42, 0.26), cols[k % 4], r=0.2, segs=1)
        var_part(ctx, "Load", "crates", "Deck", fruit)
        side_stripes(ctx, [(door_name("F", sd), -2.98, -1.95, sd) for sd in (1, -1)], -3.2, -1.6, 1.15, 1.22, "white", "stripe")
    return build_car(s, details)


# ============================================================================== variants

# ============================================================================== KAPCAI
def kapcai():
    """veh_kapcai: the everyday underbone bike - leg shield with sticker stripes, chunky stitched seat,
    a wire basket on the front, carrier rack, finned engine with a kick-start, chain and sprockets,
    chrome muffler with a heat shield, wire-spoke wheels with drum brakes, round mirrors, a speedo.
    Parts match the bike rig: Body, Steering (bars + forks), FrontWheel (on Steering), RearWheel;
    Body_Detail / Steering_Detail carry the small stuff (LOD pairs)."""
    parts = []
    wr, ww = 0.3, 0.09
    fz, rz = -0.66, 0.6
    b = K.Mesher(2.0)          # Body: the big forms
    d = K.Mesher(1.0)          # Detail: small parts (LOD pair)
    # smooth body cowl under the seat
    cowl = Shell([(-0.1, 0.1, 0.46, 0.6, 0.72, 2.6, 2.4, 2.4), (0.2, 0.17, 0.4, 0.62, 0.8, 2.8, 2.6, 2.6),
                  (0.6, 0.16, 0.5, 0.66, 0.8, 2.6, 2.4, 2.2), (0.82, 0.08, 0.6, 0.68, 0.76, 2.2, 2.0, 2.0)])
    shell_mesh(b, cowl, ny=16, ns=10, color_fn=lambda c: "bike_paint2" if c.z < 0.54 else "bike_paint")
    for side in (1, -1):   # sticker stripes and a side badge on the cowl
        patch(d, cowl, 0.05, 0.7, -0.12, -0.05, "sticker_yellow", side, off=0.004, ny=6, ns=1)
        patch(d, cowl, 0.12, 0.66, -0.2, -0.16, "bike_paint", side, off=0.004, ny=6, ns=1)
        put_text(d, "KAPCAI 110", cowl.point(0.4, 0.3, side) + Vector((side * 0.008, 0, 0)), (side, 0, 0), 0.035, "white",
                 max_w=0.26, place=lambda p, sd=side: cowl.point(p.y, shell_s(cowl, p.y, p.z), sd) +
                 cowl.normal(p.y, shell_s(cowl, p.y, p.z), sd) * 0.006)
    # leg shield (curved) with a little storage pocket
    shield = Shell([(-0.5, 0.05, 0.3, 0.6, 0.94, 2.4, 2.2, 2.0), (-0.44, 0.24, 0.28, 0.6, 0.96, 3.0, 2.4, 2.2),
                    (-0.36, 0.25, 0.3, 0.6, 0.92, 3.0, 2.4, 2.2), (-0.3, 0.05, 0.34, 0.6, 0.86, 2.4, 2.2, 2.0)])
    shell_mesh(b, shield, ny=10, ns=10, color="bike_paint")
    for side in (1, -1):
        patch(d, shield, -0.47, -0.41, -0.5, 0.5, "bike_paint2", side, off=0.004, ny=2, ns=5)
    rbox(d, (0, -0.29, 0.72), (0.2, 0.05, 0.1), "trim_black", r=0.5, segs=1)
    # underbone frame, floor with rubber mat, engine, muffler
    b.tube((0, -0.4, 0.8), (0, -0.05, 0.4), 0.05, 0.05, "bike_paint", seg=8)
    rbox(b, (0, -0.12, 0.36), (0.28, 0.44, 0.06), "trim_black", r=0.5)
    for k in range(5):
        d.box((0, -0.3 + k * 0.09, 0.392), (0.24, 0.03, 0.006), "rubber")
    rbox(b, (0, 0.08, 0.3), (0.22, 0.3, 0.22), "disc_steel", r=0.4)                             # crankcase
    b.cyl((0.1, 0.08, 0.3), 0.08, 0.08, 0.06, "rim_silver", seg=12, rot=AX)                      # clutch cover
    disc_on(d, (0.131, 0.08, 0.3), (1, 0, 0), 0.03, "metal_dark", seg=10)
    # the cylinder lying forward under the floor, cooling fins
    b.tube((0, -0.02, 0.3), (0, -0.2, 0.24), 0.07, 0.065, "disc_steel", seg=10)
    for k in range(5):
        y = -0.06 - k * 0.03
        d.cyl((0, y, 0.29 - k * 0.01), 0.085, 0.085, 0.008, "metal", seg=12, rot=(math.pi / 2 + 0.32, 0, 0))
    d.cyl((0, -0.22, 0.24), 0.04, 0.04, 0.03, "metal_dark", seg=8, rot=(math.pi / 2, 0, 0))     # head cover
    d.tube((-0.11, 0.12, 0.34), (-0.16, 0.0, 0.22), 0.012, 0.012, "trim_black", seg=5)          # kick-start
    d.box((-0.17, -0.02, 0.22), (0.03, 0.08, 0.02), "rubber")
    b.tube((-0.08, -0.06, 0.24), (-0.14, 0.3, 0.2), 0.03, 0.03, "chrome", seg=8)
    b.tube((-0.14, 0.3, 0.22), (-0.16, 0.78, 0.3), 0.055, 0.045, "chrome", seg=12)               # muffler
    d.cyl((-0.16, 0.8, 0.3), 0.03, 0.03, 0.05, "metal_dark", seg=8, rot=(math.pi / 2, 0, 0))
    rbox(d, (-0.205, 0.5, 0.27), (0.02, 0.24, 0.085), "rim_silver", r=0.5, segs=1)                # heat shield
    for k in range(4):
        disc_on(d, (-0.216, 0.42 + k * 0.05, 0.27), (-1, 0, 0), 0.01, "well_black", seg=6)
    # seat with piping + grab rail + carrier rack + tail lamp + plate
    rbox(b, (0, 0.34, 0.84), (0.27, 0.62, 0.1), "seat_black", r=0.7)
    for side in (1, -1):
        d.box((side * 0.125, 0.34, 0.83), (0.008, 0.56, 0.01), "rim_dark")
    d.box((0, 0.18, 0.893), (0.2, 0.006, 0.006), "rim_dark")
    for side in (1, -1):
        d.tube((side * 0.13, 0.55, 0.84), (side * 0.1, 0.9, 0.86), 0.012, 0.012, "chrome", seg=6)
    d.tube((0.1, 0.9, 0.86), (-0.1, 0.9, 0.86), 0.012, 0.012, "chrome", seg=6)
    for k in range(4):   # carrier slats
        d.box((0, 0.74 + k * 0.05, 0.865), (0.26, 0.02, 0.012), "trim_black")
    rbox(d, (0, 0.94, 0.72), (0.14, 0.05, 0.06), "lamp_red", r=0.5, segs=1)
    for side in (1, -1):
        d.tube((side * 0.06, 0.93, 0.68), (side * 0.15, 0.95, 0.66), 0.008, 0.008, "trim_black", seg=4)
        d.ball((side * 0.16, 0.955, 0.66), (0.022, 0.02, 0.018), "lamp_amber", seg=8, rings=5)
    plate(d, (0, 0.98, 0.58), w=0.22, h=0.11, facing=1, text="WTK 812")
    b.box((0, 0.85, 0.62), (0.16, 0.3, 0.03), "bike_paint", rot=(0.3, 0, 0))                      # rear fender
    # chain + sprockets + swing arm + rear shocks
    d.box((0.1, 0.35, 0.33), (0.016, 0.5, 0.012), "trim_black")
    d.box((0.1, 0.35, 0.265), (0.016, 0.5, 0.012), "trim_black")
    d.cyl((0.1, 0.09, 0.3), 0.04, 0.04, 0.01, "metal_dark", seg=10, rot=AX)
    d.cyl((0.1, rz, wr), 0.085, 0.085, 0.01, "metal_dark", seg=14, rot=AX)
    rbox(d, (0.11, 0.35, 0.3), (0.012, 0.42, 0.06), "bike_paint2", r=0.5, segs=1)                  # chain guard
    for x in (0.12, -0.12):
        b.tube((x, 0.0, 0.3), (x, rz, wr), 0.02, 0.02, "trim_black", seg=6)
        b.tube((x, 0.5, 0.36), (x, 0.42, 0.72), 0.025, 0.025, "chrome", seg=6)
        for k in range(5):                                                                          # spring coils
            t = 0.42 + k * 0.045
            d.cyl((x, 0.49 - (t - 0.36) * 0.22, t), 0.036, 0.036, 0.012, "sticker_yellow", seg=8, rot=(0.2, 0, 0))
    for side in (1, -1):
        rbox(b, (side * 0.2, -0.02, 0.28), (0.12, 0.04, 0.03), "rubber", r=0.5)                      # footpegs
        d.tube((side * 0.14, 0.22, 0.3), (side * 0.2, 0.24, 0.32), 0.01, 0.01, "trim_black", seg=4)   # pillion pegs
    d.tube((0.08, 0.05, 0.24), (0.2, 0.12, 0.02), 0.015, 0.015, "trim_black", seg=4)               # side stand
    parts.append(("Body", b, (0, 0, wr + 0.05), None))
    parts.append(("Detail", d, None, "Body"))
    parts.append(("Seat_Rider", None, (0, 0.28, 0.9), "Body"))
    parts.append(("FX_Exhaust", None, (-0.16, 0.84, 0.3), "Body"))

    head = Vector((0, -0.46, 0.9))
    st = K.Mesher(2.0)
    sd = K.Mesher(1.0)
    for x in (0.07, -0.07):
        st.tube(head + Vector((x, 0, 0.06)), Vector((x, fz + 0.04, wr + 0.12)), 0.026, 0.026, "chrome", seg=8)
        st.tube(Vector((x, fz + 0.03, wr + 0.14)), Vector((x, fz, wr)), 0.034, 0.034, "trim_black", seg=8)
        for k in range(4):   # fork gaiters
            p = head + (Vector((x, fz + 0.04, wr + 0.12)) - head) * (0.62 + k * 0.05)
            sd.cyl(p + Vector((0, 0, 0)), 0.032, 0.032, 0.012, "rubber", seg=8, rot=(0.45, 0, 0))
    rbox(st, (0, fz - 0.03, wr + 0.26), (0.13, 0.46, 0.05), "bike_paint", rot=(0.18, 0, 0), r=0.7)   # front fender
    cowl2 = Shell([(-0.62, 0.05, 0.98, 1.06, 1.14, 2.4, 2.4, 2.2), (-0.56, 0.17, 0.96, 1.06, 1.16, 2.8, 2.6, 2.4),
                   (-0.36, 0.17, 0.98, 1.06, 1.14, 2.8, 2.6, 2.4), (-0.32, 0.06, 1.0, 1.06, 1.12, 2.4, 2.2, 2.2)])
    shell_mesh(st, cowl2, ny=8, ns=10, color="bike_paint")
    # speedo on top of the headlamp cowl, facing the rider
    sn = Vector((0, 1, 0.9)).normalized()
    disc_on(sd, (0, -0.44, 1.165), sn, 0.05, "trim_black", seg=12)
    disc_on(sd, (0, -0.439, 1.166), sn, 0.04, "white", seg=12)
    bar(sd, (0, -0.437, 1.167), (0.02, -0.44, 1.185), 0.004, 0.002, "tail_red", n=sn)
    st.tube(Vector((-0.34, -0.42, 1.1)), Vector((0.34, -0.42, 1.1)), 0.016, 0.016, "chrome", seg=6)
    for sgn in (1, -1):
        st.tube(Vector((sgn * 0.24, -0.42, 1.1)), Vector((sgn * 0.36, -0.42, 1.1)), 0.028, 0.028, "rubber", seg=8)
        for k in range(3):   # grip ribs
            sd.cyl((sgn * (0.27 + k * 0.03), -0.42, 1.1), 0.031, 0.031, 0.006, "trim_black", seg=8, rot=AX)
        sd.tube(Vector((sgn * 0.16, -0.44, 1.12)), Vector((sgn * 0.24, -0.34, 1.13)), 0.006, 0.006, "chrome", seg=4)  # levers
        rbox(sd, (sgn * 0.17, -0.43, 1.12), (0.05, 0.04, 0.04), "trim_black", r=0.5, segs=1)                      # switch pods
        sd.tube(Vector((sgn * 0.2, -0.44, 1.12)), Vector((sgn * 0.3, -0.44, 1.22)), 0.008, 0.008, "trim_black", seg=4)
        mc = Vector((sgn * 0.31, -0.44, 1.26))
        st.cyl(mc, 0.05, 0.05, 0.015, "trim_black", seg=10, rot=(math.pi / 2, 0, 0))
        sd.cyl(mc + Vector((0, 0.009, 0)), 0.04, 0.04, 0.004, "chrome", seg=10, rot=(math.pi / 2, 0, 0))
    # optional parts (VehicleVariety): a wire basket on the front, a delivery box on the rack, a little visor
    def basket(sd):
        bc = Vector((0, -0.8, 1.02))
        bw, bd, bh = 0.32, 0.22, 0.17
        for z in (0.0, bh * 0.5, bh):
            for (a, c) in (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))):
                sd.tube(bc + Vector((a[0] * bw / 2, a[1] * bd / 2, z)), bc + Vector((c[0] * bw / 2, c[1] * bd / 2, z)), 0.006,
                        0.006, "chrome", seg=4)
        for k in range(5):
            x = -bw / 2 + bw * k / 4
            for yy in (-bd / 2, bd / 2):
                sd.tube(bc + Vector((x, yy, 0)), bc + Vector((x, yy, bh)), 0.005, 0.005, "chrome", seg=4)
        for k in range(1, 3):
            y = -bd / 2 + bd * k / 3
            for xx in (-bw / 2, bw / 2):
                sd.tube(bc + Vector((xx, y, 0)), bc + Vector((xx, y, bh)), 0.005, 0.005, "chrome", seg=4)
        sd.box(bc + Vector((0, 0, 0.004)), (bw, bd, 0.008), "trim_black")
        for x in (0.08, -0.08):
            sd.tube(bc + Vector((x, bd / 2, 0)), Vector((x, -0.5, 0.86)), 0.008, 0.008, "trim_black", seg=5)
    bm_ = K.Mesher(1.0)
    basket(bm_)
    parts.append(("Var_Basket_a_Front", bm_, None, "Steering"))
    vz = K.Mesher(1.0)
    rbox(vz, (0, -0.6, 1.2), (0.26, 0.012, 0.14), "car_glass", rot=(-0.35, 0, 0), r=0.6, segs=2)
    parts.append(("Var_Visor_a_Glass", vz, None, "Steering"))
    for opt, colr, stripe in (("yellow", "umb_yellow", "shop_red"), ("red", "shop_red", "white"), ("blue", "umb_blue", "white")):
        bx = K.Mesher(1.0)
        rbox(bx, (0, 0.8, 1.08), (0.4, 0.4, 0.38), colr, r=0.25, segs=2)
        for sg in (1, -1):
            quad(bx, (sg * 0.201, 0.8, 1.1), Vector((0, -sg * 0.18, 0)), Vector((0, 0, 0.035)), stripe)
        rbox(bx, (0, 0.8, 1.275), (0.42, 0.42, 0.03), colr, r=0.4, segs=1)
        parts.append((f"Var_Box_{opt}_Rack", bx, None, "Body"))
    parts.append(("Steering", st, tuple(head), None))
    parts.append(("SteerDetail", sd, None, "Steering"))

    def lamps(lm):
        lamp_pod(lm, (0, -0.64, 1.02), (0.075, 0.035, 0.06), "lens_clear", rim="bike_paint")
        disc_on(lm, (0, -0.684, 1.02), (0, -1, 0), 0.03, "lamp_white", seg=10)
        for sgn in (1, -1):
            ellip(lm, (sgn * 0.15, -0.6, 1.04), (0.028, 0.025, 0.02), "lamp_amber", seg=8, rings=5)
    hl = K.Mesher(1.0)
    lamps(hl)
    parts.append(("HeadLights", hl, None, "Steering"))

    def spoke_wheel(name, c, r, w, parent):
        c = Vector(c)
        tm = K.Mesher(1.0)
        ri = r * 0.8
        tyre(tm, c, r, w, 1, ri, seg=22)
        parts.append((name, tm, tuple(c), parent))
        m = K.Mesher(1.0)
        revolve(m, c, [(ri, -w * 0.38), (ri * 0.98, w * 0.38)], lambda i: "rim_silver", seg=22, side=1, closed=False,
                double=True)
        m.cyl(c, 0.075, 0.075, w * 0.9, "disc_steel", seg=14, rot=AX)                          # drum brake hub
        for sgn in (1, -1):
            disc_on(m, c + Vector((sgn * w * 0.45 + sgn * 0.001, 0, 0)), (sgn, 0, 0), 0.05, "metal_dark", seg=10)
        n = max(8, int(18 * K.LOD_DETAIL))
        for k in range(n):
            a = k / n * math.tau
            sx = 0.03 if k % 2 else -0.03
            p0 = c + Vector((sx, 0.07 * math.cos(a + 0.4), 0.07 * math.sin(a + 0.4)))
            p1 = c + Vector((sx * 0.3, ri * 0.98 * math.cos(a), ri * 0.98 * math.sin(a)))
            m.tube(p0, p1, 0.0035, 0.0035, "chrome", seg=4)
        parts.append(("Rim_" + name[0], m, tuple(c), name))

    spoke_wheel("FrontWheel", (0, fz, wr), wr, ww, "Steering")
    spoke_wheel("RearWheel", (0, rz, wr), wr, ww * 1.15, None)
    col = K.Mesher()
    col.box((0, 0.0, 0.58), (0.5, 1.85, 0.9), "rubber")
    parts.append(("COL_veh_kapcai", col, None, "__collision__"))
    if K.LOD_CULL > 0:     # distance stand-in: the whole bike as one mesh (Body_LOD1)
        for name, m, origin, parent in parts:
            if m is not None and m is not b and parent != "__collision__" and name != "HeadLights" and not name.startswith("Var_"):
                merge_bm(b, m.bm)
        return [("Body", b, (0, 0, wr + 0.05), None)]
    return parts


BUILDERS = {"veh_myvi": myvi, "veh_saga": saga, "veh_kancil": kancil, "veh_alphard": alphard, "veh_hilux": hilux,
            "veh_teksi": teksi, "veh_polis": polis, "veh_kapcai": kapcai, "veh_iriz": iriz, "veh_mpv": mpv, "veh_lorry": lorry}


# ============================================================================== assembly
# Distance versions. The whole car swaps to one light stand-in far away: the LOD-mode rebuild (K.LOD_DETAIL /
# LOD_CULL) folds the shell, the shut doors and the coarse outside detail into one mesh with painted windows,
# exported as Body_LOD1 (a child of Body), and each wheel gets a plain tyre, Tyre_XX_LOD1 (a child of the wheel,
# so it still spins). ModelImportPipeline builds the car's LODGroup: every other renderer is LOD0, except the
# lamps and roof furniture, which stay drawn at any distance so lights still glow down the street.
LOD1_NAMES = {"Body": "Body_LOD1", "Wheel_FL": "Tyre_FL_LOD1", "Wheel_FR": "Tyre_FR_LOD1", "Wheel_RL": "Tyre_RL_LOD1",
              "Wheel_RR": "Tyre_RR_LOD1"}


def assemble(asset_id, parts, mat, cols, lod_parts=None):
    """Like kl_vehicles.assemble, plus empties (mesher None), nested parents and the LOD1 stand-ins from
    lod_parts (see LOD1_NAMES). Returns root, objects, LOD0 triangles; root["lod1_tris"] = what is drawn far away."""
    root = bpy.data.objects.new(asset_id, None)
    root.empty_display_size = 0.3
    root["kl_world_origin"] = [0, 0, 0]
    cols["EXPORT"].objects.link(root)
    made = {}
    objs = []
    tris = 0
    always = 0
    lods = {n: (m, o, p) for n, m, o, p in (lod_parts or []) if m is not None}
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
            objs.append(e)
            continue
        if parent == "__collision__":
            ob, _, _ = m.to_object(name, mat, cols["COLLISION"], origin=origin, parent=root, turn180=True)
            ob.display_type = 'WIRE'
            ob.hide_render = True
            made[name] = ob
            objs.append(ob)
            continue
        par = made.get(parent, root) if parent else root
        t = m.tris()
        tris += t
        if name in ALWAYS_DRAWN:
            always += t
        ob, _, _ = m.to_object(name, mat, cols["EXPORT"], origin=origin, parent=par, turn180=True)
        objs.append(ob)
        made[name] = ob
    lod1 = always
    for name, lname in LOD1_NAMES.items():
        if name in made and name in lods:
            lm, lo, _ = lods[name]
            lod1 += lm.tris()
            l1, _, _ = lm.to_object(lname, mat, cols["EXPORT"], origin=lo, parent=made[name], turn180=True)
            objs.append(l1)
    root["lod1_tris"] = lod1
    return root, objs, tris


ALWAYS_DRAWN = {"HeadLights", "BrakeLights", "ReverseLights", "LightBar", "RoofSign"}
