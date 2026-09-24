"""Character meshes built on the shared humanoid skeleton (see kl_humanoid)."""
import math
import random
from mathutils import Vector

import kl_core as K

B = lambda **w: w  # bone weight dict helper
DETAIL = 1.7       # facet multiplier for round parts: smooth Hit & Run-style forms
HEAD_SCALE = 1.22  # Hit & Run proportions: big heads on short bodies


def proportions(height=1.72, head=0.30, hip_frac=0.535, shoulder_w=0.17, arm_len=0.62, foot_len=0.26, hip_w=0.1):
    head = head * HEAD_SCALE
    foot_len = foot_len * 1.12               # big cartoon shoes
    top = height - 0.04                      # hair adds the last few cm
    head_z = top - head
    neck_z = head_z - 0.05
    shoulder_z = neck_z - 0.07
    hip_z = height * hip_frac
    return dict(top_z=top, head_z=head_z, neck_z=neck_z, shoulder_z=shoulder_z, hip_z=hip_z,
                knee_z=hip_z * 0.53, ankle_z=0.09, hip_x=hip_w, shoulder_x=shoulder_w, arm_len=arm_len,
                foot_len=foot_len, head=head)


# ------------------------------------------------------------------------------ smooth hoses
def _vface(m, verts, color, smooth=True):
    try:
        f = m.bm.faces.new(verts)
    except ValueError:
        return None
    f.smooth = smooth
    u = K.pal_uv(color)
    for lp in f.loops:
        lp[m.uv].uv = u
    return f


def hose(m, pts, radii, color_fn, bones_fn, seg=14, ref=(0, 1, 0), caps=True, tag=None):
    """One smooth, continuous tube through pts (limbs, torso): radii[i] = (r_a, r_b) where r_b
    lies along `ref` (depth). color_fn(t) / bones_fn(t) give each ring's colour and bone
    weights (t = 0..1 along the tube); round domed caps close both ends. No segment seams,
    no bands - the Hit & Run rubber-hose look."""
    seg = max(8, int(seg * m.detail / DETAIL))
    pts = [Vector(p) for p in pts]
    n = len(pts)
    ref = Vector(ref)
    total = sum((pts[i + 1] - pts[i]).length for i in range(n - 1)) or 1.0
    acc, ts = 0.0, [0.0]
    for i in range(n - 1):
        acc += (pts[i + 1] - pts[i]).length
        ts.append(acc / total)
    rings = []
    ring_t = []

    def frame(i):
        a = pts[max(0, i - 1)]
        b = pts[min(n - 1, i + 1)]
        tng = (b - a).normalized()
        side = ref.cross(tng)
        if side.length < 1e-4:
            side = Vector((1, 0, 0)).cross(tng)
        side.normalize()
        dep = tng.cross(side).normalized()
        return tng, side, dep

    def ring(c, side, dep, ra, rb, t):
        vs = []
        for k in range(seg):
            a = k / seg * math.tau
            v = m.bm.verts.new(c + side * math.cos(a) * ra + dep * math.sin(a) * rb)
            m.weights.append((v, bones_fn(t)))
            if tag:
                m.tags.setdefault(tag, []).append(v)
            vs.append(v)
        return vs

    for i in range(n):
        tng, side, dep = frame(i)
        ra, rb = radii[i]
        rings.append(ring(pts[i], side, dep, ra, rb, ts[i]))
        ring_t.append(ts[i])
    if caps:   # domes: two shrinking rings + a pole at each end
        for end in (0, 1):
            i = 0 if end == 0 else n - 1
            tng, side, dep = frame(i)
            d = -tng if end == 0 else tng
            ra, rb = radii[i]
            extra = []
            for f in (0.72, 0.38):
                h = math.sqrt(1 - f * f) * min(ra, rb) * 0.9
                extra.append(ring(pts[i] + d * h, side, dep, ra * f, rb * f, ring_t[i]))
            pole = m.bm.verts.new(pts[i] + d * min(ra, rb) * 0.9)
            m.weights.append((pole, bones_fn(ring_t[i])))
            if tag:
                m.tags.setdefault(tag, []).append(pole)
            col = color_fn(ring_t[i])
            chain = [rings[i]] + extra
            for a_, b_ in zip(chain, chain[1:]):
                for k in range(seg):
                    q = [a_[k], a_[(k + 1) % seg], b_[(k + 1) % seg], b_[k]]
                    _vface(m, q if end == 1 else list(reversed(q)), col)
            last = chain[-1]
            for k in range(seg):
                tri = [last[k], last[(k + 1) % seg], pole]
                _vface(m, tri if end == 1 else list(reversed(tri)), col)
    for i in range(n - 1):
        col = color_fn((ring_t[i] + ring_t[i + 1]) / 2)
        a_, b_ = rings[i], rings[i + 1]
        for k in range(seg):
            _vface(m, [a_[k], a_[(k + 1) % seg], b_[(k + 1) % seg], b_[k]], col)


def recolor_tagged(m, tag, fn):
    """Repaint faces whose verts all carry `tag`: fn(face_centre) -> colour or None (keep).
    Used for painted-on prints (batik diamonds, sarong checks) instead of stuck-on geometry."""
    vs = set(m.tags.get(tag, []))
    if not vs:
        return
    for f in m.bm.faces:
        if all(v in vs for v in f.verts):
            c = f.calc_center_median()
            col = fn(c)
            if col:
                u = K.pal_uv(col)
                for lp in f.loops:
                    lp[m.uv].uv = u


# ------------------------------------------------------------------------------ shared parts
def toon_mesher(detail=None):
    """Characters: smooth-shaded, rounded primitives with length-subdivided limbs so the
    blended joint weights bend them like rubber hose (Simpsons-style), not like hinges."""
    return K.Mesher(detail=detail or DETAIL, smooth=True, lsegs=3)


def eye_layout(hz, hr, eye_r):
    """Big bulging cartoon eyes that touch at the bridge of the nose."""
    er = max(eye_r, hr * 0.34)
    return er * 0.98, -hr * 0.93 - er * 0.28, hz + hr * 0.2, er


def muzzle_front(hr):
    return -hr * 1.04


def face(m, p, skin="skin", brow_w=0.075, eye_r=0.046, look=(0, 0), grin=True, nose=1.0, mouth_w=0.1,
         ears=True, lids=False, lashes=False, brow="brow", muzzle=None):
    """Simpsons-flavoured cartoon face: two big white eyes touching over a long bulb nose,
    tiny pupils, thin slab brows, and a rounded muzzle carrying the mouth with an overbite
    teeth strip. Everything sits proud of the head so it survives the gameplay camera."""
    nose *= HEAD_SCALE
    mouth_w *= HEAD_SCALE
    brow_w *= HEAD_SCALE
    hz = p["head_z"] + p["head"] * 0.42
    hr = p["head"] * 0.5
    ex, ey, ez, er = eye_layout(hz, hr, eye_r)
    for s, side in ((1, "L"), (-1, "R")):
        ec = Vector((s * ex, ey, ez))
        m.ball(ec, (er, er * 0.9, er * 1.12), "eye_white", seg=10, rings=8, bones=B(Head=1), tag=f"eye_{side}")
        m.ball(ec + Vector((-s * er * 0.12 + look[0], -er * 0.86, -er * 0.05 + look[1])),
               (er * 0.21, er * 0.08, er * 0.25), "pupil", seg=8, rings=6, bones=B(Head=1), tag=f"eye_{side}")
        m.box(ec + Vector((s * er * 0.1, -er * 0.3, er * 1.3)), (brow_w, 0.022, 0.02), brow,
              rot=(0.25, s * -0.1, 0), bevel=0.004, bones=B(Head=1), tag=f"brow_{side}")
        if lids:    # heavy upper lid: sleepy / easy-going look (moves with the eye keys)
            m.ball(ec + Vector((0, -er * 0.06, er * 0.42)), (er * 1.04, er * 0.95, er * 0.74), skin,
                   seg=10, rings=6, bones=B(Head=1), tag=f"eye_{side}")
        if lashes:  # two flicked lashes at the outer corner
            for k in range(2):
                base = ec + Vector((s * er * 0.86, -er * 0.3, er * (0.72 - k * 0.34)))
                m.tube(base, base + Vector((s * 0.024, -0.004, 0.015 - k * 0.006)), 0.008, 0.003, "pupil", seg=4,
                       bones=B(Head=1), tag=f"eye_{side}")
    # long bulb nose hanging from where the eyes meet
    m.ball((0, ey - er * 0.55 - 0.03 * nose, ez - er * 0.95), (0.03 * nose, 0.052 * nose, 0.033 * nose), skin, seg=10,
           rings=7, bones=B(Head=1))
    # muzzle (stubble-coloured on the dads) carrying the mouth and an overbite
    mz = hz - hr * 0.46
    fy = muzzle_front(hr)
    m.ball((0, fy + hr * 0.42, mz), (hr * 0.66, hr * 0.42, hr * 0.4), muzzle or skin, seg=12, rings=8, bones=B(Head=1))
    m.box((0, fy + 0.006, mz - hr * 0.06), (mouth_w, 0.03, 0.03 if grin else 0.014), "mouth", taper=(1.3, 1), rot=(0.2, 0, 0),
          bones=B(Head=1), tag="mouth")
    if grin:
        m.box((0, fy - 0.002, mz - hr * 0.06 + 0.01), (mouth_w * 0.9, 0.015, 0.013), "teeth", rot=(0.2, 0, 0),
              bones=B(Head=1), tag="teeth")
    for s in (1, -1) if ears else ():  # big round cartoon ears (hidden under a tudung)
        m.ball((s * hr * 1.0, 0.01, hz - hr * 0.02), (0.03, 0.045, 0.06), skin, seg=8, rings=6, bones=B(Head=1))
        m.ball((s * hr * 1.03, 0.0, hz - hr * 0.02), (0.012, 0.024, 0.036), "skin_shadow", seg=6, rings=4, bones=B(Head=1))


def head_shape(m, p, skin="skin", jaw=1.0):
    """Rounded cartoon head: a tall smooth cranium and a soft jaw under the muzzle."""
    hz = p["head_z"] + p["head"] * 0.42
    hr = p["head"] * 0.5
    m.ball((0, hr * 0.05, hz + hr * 0.12), (hr * 1.0, hr * 0.98, hr * 1.12), skin, seg=12, rings=9, bones=B(Head=1))
    m.ball((0, -hr * 0.1, hz - hr * 0.4), (hr * 0.8 * jaw, hr * 0.8, hr * 0.55), skin, seg=12, rings=8, bones=B(Head=1))
    m.tube((0, 0, p["neck_z"] - 0.04), (0, 0, p["head_z"] + 0.04), 0.058, 0.056, skin, seg=8, bones=B(Neck=1), lsegs=1)
    return hz, hr


def spiky_hair(m, p, hz, hr, rng, color="hair", spikes=22, length=0.09, fringe=True):
    """Aiman's hair: a big chunky mass of faceted tufts, flicking up and back, with
    sideburns and a nape - reads as one bold black silhouette."""
    cen = Vector((0, hr * 0.3, hz + hr * 0.45))
    m.ball(cen, (hr * 1.08, hr * 0.95, hr * 0.85), color, seg=8, rings=5, bones=B(Head=1))
    m.box((0, hr * 0.55, hz - hr * 0.1), (hr * 1.7, hr * 0.7, hr * 0.9), color, taper=(0.85, 0.8), bevel=0.02,
          bones=B(Head=1))                                                       # back of the head / nape
    for s in (1, -1):                                                            # sideburns
        m.box((s * hr * 0.9, -hr * 0.1, hz + hr * 0.02), (0.035, hr * 0.5, hr * 0.55), color, bones=B(Head=1))
    for i in range(spikes):
        a = i / spikes * math.tau + rng.uniform(-0.15, 0.15)
        el = rng.uniform(0.15, 1.0)
        d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el) * 0.85 + 0.25, math.sin(el) + 0.35)).normalized()
        if d.y < -0.25 and d.z < 0.75:
            continue  # keep the face clear
        base = cen + Vector((d.x * hr * 0.9, d.y * hr * 0.9, d.z * hr * 0.72))
        tip = base + d * length * rng.uniform(0.8, 1.35)
        m.tube(base, tip, 0.06, 0.012, color, seg=4, bones=B(Head=1), twist=rng.uniform(0, 1))
    if fringe:  # chunky fringe tufts over the forehead
        for i in range(4):
            x = (i - 1.5) * hr * 0.42
            base = Vector((x, -hr * 0.5, hz + hr * 0.82))
            tip = base + Vector((x * 0.25, -0.08, -0.02 + abs(x) * 0.2))
            m.tube(base, tip, 0.055, 0.01, color, seg=4, bones=B(Head=1), twist=0.4)


def limbs(m, p, sleeve="teal", sleeve_len=0.45, skin="skin", pants="charcoal", shoe="sneaker", sole="sole",
          pants_r=(0.095, 0.088), arm_r=0.052, hand=1.3, sandals=False, sleeve_r=0.07, cuff=None,
          roll=None, pant_cuff=None, sandal="wood_dark", long_sleeve=False, shorts=False, leg_skin="skin"):
    """Hit & Run limbs: each arm and leg is one smooth hose (sleeve / trouser painted on with a
    little lip at the hem), chunky four-fingered mitt hands and big rounded shoes."""
    sz, sx, al = p["shoulder_z"], p["shoulder_x"], p["arm_len"]
    hand *= 1.18
    arm_r *= 1.12
    for s, side in ((1, "Left"), (-1, "Right")):
        up, lo, hd = f"{side}UpperArm", f"{side}LowerArm", f"{side}Hand"
        x0, xe, xw = s * (sx - 0.04), s * (sx + al * 0.45), s * (sx + al * 0.86)
        ts = 1.0 if long_sleeve else max(0.12, min(0.95, sleeve_len / 0.86))
        # sample along the arm, with a doubled ring at the sleeve hem (step + lip)
        tv = sorted(set([0.0, 0.1, 0.22, 0.34, 0.46, 0.52, 0.6, 0.72, 0.86, 1.0] +
                        ([ts - 0.02, ts - 0.001, ts + 0.001] if ts < 0.99 else [])))
        pts, radii = [], []
        for t in tv:
            x = x0 + (xw - x0) * t
            r = arm_r * (1.08 - 0.12 * math.sin(min(1.0, t / 0.6) * math.pi * 0.5) - 0.1 * t)   # beefy upper arm, taper
            if long_sleeve:
                r = max(r * 1.2, sleeve_r * (1.0 - 0.18 * t))
            elif t < ts:
                r = max(r * 1.18, sleeve_r * 1.06) * (1.12 if t >= ts - 0.021 else 1.0)          # sleeve + hem lip
            pts.append((x, 0, sz))
            radii.append((r, r * 0.95))
        elbow_t = (abs(xe) - abs(x0)) / (abs(xw) - abs(x0))

        def arm_col(t, ts=ts):
            if long_sleeve:
                return cuff if (cuff and t > 0.93) else sleeve
            if t < ts:
                return roll if (roll and t > ts - 0.08) else sleeve
            return skin
        hose(m, pts, radii, arm_col, lambda t, e=elbow_t, u=up, l=lo: {u: 1} if t < e else {l: 1}, seg=14, ref=(0, 1, 0))
        # shoulder cap: rounds the join into the torso
        m.ball((s * (sx - 0.035), 0, sz - 0.012), (sleeve_r * 1.08, sleeve_r * 1.02, sleeve_r * 1.05), sleeve, seg=12, rings=9,
               bones={up: 0.5, "Chest": 0.5}, smooth=True)
        # four-fingered cartoon mitt: fat palm, three sausage fingers, a thumb
        wr = Vector((xw, 0, sz))
        hc = wr + Vector((s * 0.05 * hand, 0, 0))
        m.ball(hc, (0.056 * hand, 0.034 * hand, 0.05 * hand), skin, seg=12, rings=9, bones={hd: 1}, smooth=True)
        for k in range(3):
            base = hc + Vector((s * 0.03 * hand, 0, (k - 1) * 0.03 * hand))
            tip = base + Vector((s * 0.052 * hand, 0, (k - 1) * 0.006 * hand))
            hose(m, [base, tip], [(0.017 * hand, 0.016 * hand)] * 2, lambda t: skin, lambda t: {hd: 1}, seg=9, ref=(0, 1, 0))
        tb = hc + Vector((0.0, -0.03 * hand, 0.016 * hand))
        hose(m, [tb, tb + Vector((s * 0.026 * hand, -0.03 * hand, 0.012 * hand))], [(0.018 * hand, 0.017 * hand)] * 2,
             lambda t: skin, lambda t: {hd: 1}, seg=9, ref=(1, 0, 0))
    hz, kz, az = p["hip_z"], p["knee_z"], p["ankle_z"]
    hx, fl = p["hip_x"], p["foot_len"]
    for s, side in ((1, "Left"), (-1, "Right")):
        ul, ll, ft, to = f"{side}UpperLeg", f"{side}LowerLeg", f"{side}Foot", f"{side}Toes"
        top, knee, ank = hz + 0.02, kz, az + 0.02
        knee_t = (top - knee) / (top - ank)
        short_t = knee_t * 0.8 if shorts else 2.0
        tv = sorted(set([round(k / 22, 4) for k in range(23)] + [knee_t] +
                        ([short_t - 0.02, short_t - 0.001, short_t + 0.001] if shorts else [])))
        pts, radii = [], []
        for t in tv:
            r = pants_r[0] + (pants_r[1] - pants_r[0]) * t
            if t >= short_t:
                r = pants_r[0] * (0.62 - 0.14 * (t - short_t))                            # bare shin
            elif shorts and t >= short_t - 0.021:
                r *= 1.1
            pts.append((s * hx, 0, top + (ank - top) * t))
            radii.append((r, r * 0.95))

        def leg_col(t, st=short_t):
            if t >= st:
                return leg_skin
            if pant_cuff and t > 0.9:
                return pant_cuff
            return pants
        hose(m, pts, radii, leg_col, lambda t, k=knee_t, a=ul, b=ll: {a: 1} if t < k else {b: 1}, seg=14, ref=(0, 1, 0),
             tag=f"leg_{side}")
        if sandals:
            m.ball((s * hx, -fl * 0.3, 0.05), (0.06, fl * 0.5, 0.045), "skin", seg=12, rings=8, bones={ft: 1}, smooth=True)
            for k in range(3):   # little toes
                m.ball((s * hx + (k - 1) * 0.022, -fl * 0.72, 0.04), (0.014, 0.018, 0.014), "skin", seg=8, rings=6,
                       bones={to: 1}, smooth=True)
            m.ball((s * hx, -fl * 0.3, 0.012), (0.07, fl * 0.58, 0.014), sandal, seg=14, rings=6, bones={ft: 0.6, to: 0.4},
                   smooth=True)
            m.box((s * hx, -fl * 0.42, 0.05), (0.12, 0.05, 0.025), sandal, bevel=0.01, bones={ft: 1})
        else:
            # big round cartoon shoe: puffy upper, bulbous toe, soft sole
            m.ball((s * hx, -fl * 0.2, 0.075), (0.072, fl * 0.5, 0.065), shoe, seg=14, rings=10, bones={ft: 1}, smooth=True)
            m.ball((s * hx, -fl * 0.6, 0.055), (0.07, fl * 0.28, 0.05), shoe, seg=12, rings=8, bones={to: 1}, smooth=True)
            m.ball((s * hx, -fl * 0.32, 0.018), (0.078, fl * 0.62, 0.022), sole, seg=14, rings=6, bones={ft: 0.6, to: 0.4},
                   smooth=True)


def torso_stations(p, width, depth, belly):
    """(z, half-width, half-depth, forward offset) rows of the smooth body, bottom to top."""
    hz, sz, nz = p["hip_z"], p["shoulder_z"], p["neck_z"]
    mid = hz + (sz - hz) * 0.5
    z0 = hz - 0.13
    width *= 1.06
    st = [   # (z, half-width, half-depth, forward offset of the belly)
        [z0, width * 0.86, depth * 0.95, 0.0],
        [hz - 0.05, width * 0.98, depth * 1.02, 0.0],
        [hz + 0.06, width * belly * 1.02, depth * (1 + (belly - 1) * 0.55) * 1.05, -(belly - 1) * depth * 0.35],
        [mid, width * belly * 0.99, depth * (1 + (belly - 1) * 0.55), -(belly - 1) * depth * 0.25],
        [sz - 0.07, width * 0.98, depth * 0.98, 0.0],
        [sz - 0.015, width * 0.9, depth * 0.9, 0.0],
        [sz + 0.02, width * 0.62, depth * 0.72, 0.0],
        [nz + 0.01, 0.075, 0.07, 0.0],
    ]
    st = [r for r in st if r[0] >= z0]
    st.sort(key=lambda r: r[0])
    return st


def torso_at(p, width, depth, belly, z):
    """Body cross-section at height z: (half-width, half-depth, forward offset)."""
    st = torso_stations(p, width, depth, belly)
    if z <= st[0][0]:
        return st[0][1], st[0][2], st[0][3]
    for a, b in zip(st, st[1:]):
        if z <= b[0]:
            f = (z - a[0]) / (b[0] - a[0])
            return tuple(a[j] + (b[j] - a[j]) * f for j in (1, 2, 3))
    return st[-1][1], st[-1][2], st[-1][3]


def torso(m, p, shirt, undershirt=None, open_front=False, belly=1.0, width=0.17, depth=0.12, hem_z=None, collar=None,
          pants="charcoal", pockets=None):
    """One smooth pear-shaped body from the crotch to the neck (pants painted below the hem),
    belly pushed forward, round sloping shoulders - no seams between shirt halves."""
    hz, sz, nz = p["hip_z"], p["shoulder_z"], p["neck_z"]
    hem = hem_z if hem_z is not None else hz - 0.08
    mid = hz + (sz - hz) * 0.5
    st = torso_stations(p, width, depth, belly)
    width *= 1.06
    # extra rings exactly at the hem so the shirt/pants line is straight (+ a tiny shirt lip)
    zs = [r[0] for r in st]
    if zs[0] < hem < zs[-1]:
        i = next(k for k in range(len(zs) - 1) if zs[k] <= hem <= zs[k + 1])
        f = (hem - zs[i]) / (zs[i + 1] - zs[i])
        mixed = [st[i][j] + (st[i + 1][j] - st[i][j]) * f for j in range(4)]
        lo = list(mixed); lo[0] = hem - 0.002
        hi = list(mixed); hi[0] = hem + 0.002; hi[1] *= 1.03; hi[2] *= 1.03
        st = st[:i + 1] + [lo, hi] + st[i + 1:]
    # resample to ~2.5 cm rings (smooth silhouette + room for painted prints)
    dense = []
    for a_, b_ in zip(st, st[1:]):
        n = max(1, int(round((b_[0] - a_[0]) / 0.025)))
        for k in range(n):
            f = k / n
            dense.append([a_[j] + (b_[j] - a_[j]) * f for j in range(4)])
    dense.append(st[-1])
    st = dense
    zs = [r[0] for r in st]
    pts = [Vector((0, r[3], r[0])) for r in st]
    radii = [(r[1], r[2]) for r in st]
    # hose() hands us arc-length t; map it back to height through the station table
    arcs = [0.0]
    for a, b in zip(pts, pts[1:]):
        arcs.append(arcs[-1] + (b - a).length)

    def z_at(t):
        d = t * arcs[-1]
        for k in range(len(arcs) - 1):
            if d <= arcs[k + 1] or k == len(arcs) - 2:
                f = (d - arcs[k]) / max(1e-9, arcs[k + 1] - arcs[k])
                return zs[k] + (zs[k + 1] - zs[k]) * f
        return zs[-1]

    col = lambda t: pants if z_at(t) < hem else shirt

    def bones(t):
        z = z_at(t)
        return B(Hips=1) if z < hz + 0.06 else (B(Spine=1) if z <= mid else B(Chest=1))
    hose(m, pts, radii, col, bones, seg=26, ref=(0, 1, 0), tag="torso")
    if undershirt and open_front:
        fy = -torso_at(p, width / 1.06, depth, belly, (hem + nz) * 0.5)[1] - 0.004
        m.box((0, fy, (hem + nz) * 0.5 + 0.02), (width * 0.56, 0.02, nz - hem - 0.08), undershirt, taper=(0.85, 1),
              bones=B(Spine=0.5, Chest=0.5), bevel=0.008)
        for s in (1, -1):
            m.box((s * width * 0.33, fy - 0.004, (hem + nz) * 0.5 + 0.03), (0.035, 0.02, nz - hem - 0.07), shirt,
                  rot=(0, s * 0.05, 0), bones=B(Spine=0.5, Chest=0.5), bevel=0.008)
    if collar:
        for s in (1, -1):
            m.box((s * 0.065, -0.05, nz - 0.005), (0.09, 0.055, 0.03), collar, rot=(0.55, 0, s * 0.45), bones=B(Chest=1),
                  bevel=0.01)
    if pockets:
        for s in (1, -1):
            m.box((s * width * 0.45, -torso_at(p, width / 1.06, depth, belly, sz - 0.12)[1] - 0.004, sz - 0.12),
                  (0.075, 0.012, 0.08), pockets, bones=B(Chest=1), bevel=0.005)


# ------------------------------------------------------------------------------ Aiman
def build_aiman(seed=11):
    """chr_aiman: 1.72 m playable hero (see approved/aiman-character-sheet.png)."""
    rng = random.Random(seed)
    p = proportions(height=1.72, head=0.32, shoulder_w=0.19, arm_len=0.58)
    m = toon_mesher()
    hz, hr = head_shape(m, p)
    face(m, p)
    spiky_hair(m, p, hz, hr, rng)
    torso(m, p, "teal", undershirt="cream", open_front=True, collar="teal", pockets="teal_dark",
          hem_z=p["hip_z"] - 0.12, width=0.2, depth=0.135)
    limbs(m, p, sleeve="teal", sleeve_len=0.3, sleeve_r=0.068)
    # crossbody satchel strap: right shoulder -> left hip, front and back
    sz = p["shoulder_z"]
    for fy in (-0.135, 0.135):
        top = Vector((-0.1, fy * 0.9, sz + 0.02))
        mid = Vector((0.02, fy, sz - 0.2))
        low = Vector((0.2, fy * 0.95, p["hip_z"] - 0.02))
        m.tube(top, mid, 0.028, 0.028, "strap", seg=4, bones=B(Chest=1), squash=(1, 0.45))
        m.tube(mid, low, 0.028, 0.028, "strap", seg=4, bones=B(Spine=1), squash=(1, 0.45))
    # satchel on the left hip (own bone so it can swing)
    bc = Vector((p["hip_x"] + 0.14, 0.02, p["hip_z"] - 0.1))
    m.box(bc, (0.1, 0.26, 0.2), "olive", bevel=0.012, bones=B(Satchel=1))
    m.box(bc + Vector((0.052, 0, 0.04)), (0.012, 0.26, 0.13), "olive_dark", bones=B(Satchel=1))
    for s in (1, -1):
        m.box(bc + Vector((0.06, s * 0.07, 0.0)), (0.008, 0.025, 0.05), "strap", bones=B(Satchel=1))
    # key on a red cord at the right front belt loop
    kc = Vector((-p["hip_x"] - 0.04, -0.11, p["hip_z"] - 0.03))
    m.tube(kc, kc - Vector((0, 0, 0.1)), 0.006, 0.006, "cord_red", seg=4, bones=B(KeyCord=1))
    m.ball(kc - Vector((0, 0, 0.11)), (0.014, 0.004, 0.014), "metal", seg=6, rings=4, bones=B(KeyCord=1))
    m.box(kc - Vector((0, 0, 0.15)), (0.012, 0.006, 0.05), "metal", bones=B(KeyCord=1))
    return m, p


# ------------------------------------------------------------------------------ Mei
def glasses(m, hz, hr, eye_r, color="hair", n=12):
    """Round wire frames in front of the bulging eyes, bridge + temple arms to the ears."""
    ex, ey, ez, er = eye_layout(hz, hr, eye_r)
    r = er * 1.22
    fy = ey - er * 0.78
    for s in (1, -1):
        c = Vector((s * ex, fy, ez))
        for i in range(n):
            a0, a1 = i / n * math.tau, (i + 1) / n * math.tau
            m.tube(c + Vector((math.cos(a0) * r, 0, math.sin(a0) * r)), c + Vector((math.cos(a1) * r, 0, math.sin(a1) * r)),
                   0.0065, 0.0065, color, seg=4, bones=B(Head=1), tag="glasses")
        m.tube(c + Vector((s * r, 0, 0.004)), Vector((s * hr * 0.98, 0.0, hz + hr * 0.12)), 0.006, 0.006, color, seg=4,
               bones=B(Head=1))


def bob_hair(m, hz, hr, rng, color="hair"):
    """Mei's black bob: a big rounded crown, blunt chin-length sides with chunky flicked
    ends, a full back, and a side-swept fringe that stops above the brows."""
    m.ball((0, hr * 0.12, hz + hr * 0.38), (hr * 1.13, hr * 1.1, hr * 0.88), color, seg=9, rings=6, bones=B(Head=1))
    for s in (1, -1):  # rounded side masses that swell out and stop at the jaw
        m.ball((s * hr * 0.9, hr * 0.22, hz - hr * 0.1), (hr * 0.3, hr * 0.82, hr * 0.72), color, seg=8, rings=6,
               bones=B(Head=1))
        for k in range(3):  # blunt, slightly flicked ends
            y = -hr * 0.3 + k * hr * 0.55
            base = Vector((s * hr * 1.08, y, hz - hr * 0.66))
            m.tube(base, base + Vector((s * 0.035, 0.01, -0.07)), 0.045, 0.01, color, seg=4, bones=B(Head=1),
                   twist=rng.uniform(0, 1))
    m.ball((0, hr * 0.7, hz - hr * 0.08), (hr * 1.05, hr * 0.45, hr * 0.78), color, seg=9, rings=6, bones=B(Head=1))
    for k in range(4):  # nape tufts
        x = (k - 1.5) * hr * 0.5
        base = Vector((x, hr * 0.95, hz - hr * 0.7))
        m.tube(base, base + Vector((x * 0.1, 0.02, -0.07)), 0.05, 0.01, color, seg=4, bones=B(Head=1), twist=0.3)
    # side-swept fringe: from a parting on her right, sweeping across to the left temple
    part = Vector((-hr * 0.35, -hr * 0.55, hz + hr * 0.95))
    for k in range(5):
        tip = Vector((-hr * 0.55 + k * hr * 0.36, -hr * 0.98, hz + hr * (0.62 + 0.03 * abs(k - 2))))
        m.tube(part + Vector((k * 0.02, 0, 0)), tip, 0.06, 0.015, color, seg=4, bones=B(Head=1), twist=0.5)


def build_mei(seed=23):
    """chr_mei: 1.63 m stall-keeper (approved/mei-character-sheet.png): black bob, round
    glasses, coral shirt with rolled sleeves, navy work apron, woven market bag on her
    right shoulder, tea towel, rolled black trousers, dark sneakers."""
    rng = random.Random(seed)
    p = proportions(height=1.63, head=0.31, shoulder_w=0.165, arm_len=0.54, hip_w=0.095, foot_len=0.24)
    p["bag_side"] = -1
    m = toon_mesher(1.05)    # lots of small accessories: fewer facets keep her/him under 7k
    hz, hr = head_shape(m, p, jaw=0.92)
    eye_r = 0.044
    face(m, p, eye_r=eye_r, nose=0.8, mouth_w=0.085, brow_w=0.065)
    glasses(m, hz, hr, eye_r)
    bob_hair(m, hz, hr, rng)
    width, depth = 0.17, 0.115
    torso(m, p, "coral", collar="coral", hem_z=p["hip_z"] - 0.06, width=width, depth=depth)
    limbs(m, p, sleeve="coral", sleeve_len=0.3, sleeve_r=0.064, roll="coral_dark", pants="charcoal_dark",
          pants_r=(0.093, 0.085), pant_cuff="charcoal", arm_r=0.047, hand=1.2)
    sz, hipz, kz = p["shoulder_z"], p["hip_z"], p["knee_z"]
    fy = -depth - 0.012
    # --- apron: bib (Chest), waistband (Hips), skirt halves that follow the thighs
    m.box((0, fy, sz - 0.12), (0.19, 0.02, 0.2), "navy", taper=(0.9, 1), bones=B(Chest=1), tag="apron")
    m.tube((0, 0, hipz + 0.03), (0, 0, hipz + 0.08), width * 1.07, width * 1.07, "navy", seg=8, bones=B(Hips=1),
           squash=(1, depth / width * 1.1))
    for s, side in ((1, "Left"), (-1, "Right")):
        top, bot = hipz + 0.05, kz + 0.06
        m.box((s * 0.09, fy - 0.004, (top + bot) / 2), (0.175, 0.022, top - bot), "navy",
              bones={"Hips": 0.45, f"{side}UpperLeg": 0.55})
        m.box((s * 0.085, fy - 0.017, top - 0.2), (0.13, 0.008, 0.13), "navy", bones={"Hips": 0.45, f"{side}UpperLeg": 0.55})
    for (x, z, r) in ((0.05, sz - 0.08, 0.02), (-0.06, hipz - 0.05, 0.028), (0.1, hipz - 0.2, 0.022), (-0.02, sz - 0.18, 0.016)):
        m.ball((x, fy - 0.013, z), (r, 0.004, r * 0.7), "wood", seg=6, rings=4, rot=(0, x * 9, 0), bones=B(Hips=1) if z < hipz else B(Chest=1))
    for s in (1, -1):  # neck straps over the shoulders, down the back to the bow
        m.tube((s * 0.08, fy, sz - 0.02), (s * 0.06, 0.0, sz + 0.06), 0.012, 0.012, "navy", seg=4, bones=B(Chest=1))
        m.tube((s * 0.06, 0.0, sz + 0.06), (s * 0.08, depth + 0.01, sz - 0.02), 0.012, 0.012, "navy", seg=4, bones=B(Chest=1))
        m.box((s * 0.045, depth * 1.08 + 0.01, hipz + 0.06), (0.06, 0.02, 0.04), "navy", rot=(0, s * 0.4, 0), bones=B(Hips=1))
        m.box((s * 0.03, depth * 1.08 + 0.012, hipz - 0.02), (0.022, 0.015, 0.11), "navy", rot=(0, s * 0.15, 0), bones=B(Hips=1))
    # tea towel tucked into the waistband on her right
    tx = -0.13
    m.box((tx, fy - 0.03, hipz - 0.08), (0.09, 0.018, 0.26), "towel", bones=B(Hips=1))
    for dz in (-0.14, -0.17):
        m.box((tx, fy - 0.04, hipz + dz), (0.092, 0.006, 0.012), "cord_red", bones=B(Hips=1))
    # --- woven market bag on her right shoulder (Satchel bone), handles over the shoulder
    bc = Vector((-(p["hip_x"] + 0.2), 0.03, hipz + 0.02))
    m.box(bc, (0.13, 0.3, 0.28), "crate_green", taper=(1.1, 1.05), bones=B(Satchel=1))
    weave = ("crate_red", "crate_yellow", "fruit_green", "fruit_orange", "crate_blue", "crate_yellow")
    for i in range(3):
        for j in range(3):
            col = weave[(i * 2 + j) % len(weave)]
            m.box(bc + Vector((-0.068, -0.09 + i * 0.09, -0.09 + j * 0.09)), (0.006, 0.07, 0.07), col, bones=B(Satchel=1))
            m.box(bc + Vector((0.0 - 0.04 + i * 0.04, -0.153, -0.09 + j * 0.09)), (0.035, 0.006, 0.07),
                  weave[(i + j * 2 + 1) % len(weave)], bones=B(Satchel=1))
    for k in range(4):  # spring onions poking out
        base = bc + Vector((-0.02 + k * 0.012, -0.06 + k * 0.035, 0.12))
        m.tube(base, base + Vector((0.01, -0.02, 0.16 + k * 0.02)), 0.012, 0.008, "leaf" if k % 2 else "fruit_green", seg=4,
               bones=B(Satchel=1))
    shoulder = Vector((-p["shoulder_x"] + 0.02, 0, sz + 0.05))
    for dy in (-0.07, 0.07):
        mid = Vector((-(p["shoulder_x"] + 0.1), dy, hipz + 0.3))
        m.tube(shoulder + Vector((0, dy * 0.6, 0)), mid, 0.014, 0.014, "leaf_dark", seg=4, bones=B(Chest=1))
        m.tube(mid, bc + Vector((0, dy * 1.4, 0.14)), 0.014, 0.014, "leaf_dark", seg=4, bones=B(Satchel=1))
    # watch on her left wrist
    wx = p["shoulder_x"] + p["arm_len"] * 0.86
    m.tube((wx - 0.045, 0, sz), (wx - 0.015, 0, sz), 0.05, 0.05, "bollard", seg=6, bones=B(LeftLowerArm=1))
    return m, p


# ------------------------------------------------------------------------------ Ravi
def build_ravi(seed=31):
    """chr_ravi: 1.68 m stocky shopkeeper (approved/ravi-character-sheet.png): wavy grey-black
    hair with a thinning crown, bushy moustache, big nose, checked ochre short-sleeve shirt
    over a white tee, pen in the pocket, wristwatch, dark slacks, brown sandals."""
    rng = random.Random(seed)
    p = proportions(height=1.68, head=0.31, shoulder_w=0.2, arm_len=0.56, hip_w=0.112, hip_frac=0.5, foot_len=0.27)
    m = toon_mesher(1.05)    # lots of small accessories: fewer facets keep her/him under 7k
    hz, hr = head_shape(m, p, jaw=1.08)
    face(m, p, eye_r=0.043, nose=1.6, mouth_w=0.09, brow_w=0.085)
    spiky_hair(m, p, hz, hr, rng, color="hair_grey", spikes=24, length=0.035, fringe=False)
    for k in range(3):  # a wavy quiff instead of Aiman's fringe
        x = (k - 1) * hr * 0.45
        base = Vector((x, -hr * 0.55, hz + hr * 0.78))
        m.tube(base, base + Vector((x * 0.3, -0.035, 0.05)), 0.06, 0.015, "hair_grey", seg=4, bones=B(Head=1), twist=0.2)
    m.ball((0, hr * 0.62, hz + hr * 1.02), (hr * 0.34, hr * 0.28, hr * 0.12), "skin", seg=8, rings=4, bones=B(Head=1),
           rot=(-0.6, 0, 0))                                                   # thinning crown
    # bushy moustache (tagged so expressions can lift / droop it)
    fy = muzzle_front(hr)
    mz = hz - hr * 0.36
    m.box((0, fy - 0.006, mz), (0.13, 0.045, 0.05), "hair_grey", taper=(0.75, 1), bevel=0.01, bones=B(Head=1), tag="moustache")
    for s in (1, -1):
        m.tube(Vector((s * 0.05, fy - 0.006, mz - 0.01)), Vector((s * 0.085, fy + 0.012, mz - 0.055)), 0.022, 0.01,
               "hair_grey", seg=4, bones=B(Head=1), tag="moustache")
    width, depth, belly = 0.2, 0.14, 1.15
    hem = p["hip_z"] - 0.12
    torso(m, p, "ochre", collar="ochre", belly=belly, width=width, depth=depth, hem_z=hem, pants="charcoal")
    sz, nz, hipz = p["shoulder_z"], p["neck_z"], p["hip_z"]
    mid = hipz + (sz - hipz) * 0.5

    def shell(z):
        w, d, _ = torso_at(p, width, depth, belly, z)
        return w, d

    def on_shell(x, z, side=-1, lift=0.004):
        """Point + yaw that sits flush on the front (side=-1) or back (+1) of the shirt."""
        w, d, fy = torso_at(p, width, depth, belly, z)
        u = max(-0.97, min(0.97, x / w))
        y = d * math.sqrt(1 - u * u)
        slope = d * u / (w * math.sqrt(1 - u * u))
        return Vector((x, fy + side * (y + lift), z)), math.atan(slope) * -side

    def bone_at(z):
        return B(Spine=1) if z <= mid else B(Chest=1)

    # checks: short flush strips following the shirt curve -> a plaid grid front and back
    for side in (-1, 1):
        for z in (hem + 0.06, hipz + 0.02, mid - 0.02, mid + 0.08, sz - 0.03):
            xr, _ = shell(z)
            n = 6
            for i in range(n):
                x = -xr * 0.82 + (i + 0.5) * xr * 1.64 / n
                c, yaw = on_shell(x, z, side)
                m.box(c, (xr * 1.64 / n + 0.004, 0.006, 0.02), "ochre_check", rot=(0, 0, yaw), bones=bone_at(z))
        for xf in (-0.62, -0.22, 0.22, 0.62):
            z = hem + 0.03
            while z < sz - 0.02:
                xr, _ = shell(z)
                c, yaw = on_shell(xf * xr, z, side)
                m.box(c, (0.02, 0.006, 0.052), "ochre_check", rot=(0, 0, yaw), bones=bone_at(z))
                z += 0.05
    c, _ = on_shell(0, nz - 0.05, lift=0.002)                           # white tee at the open collar
    m.box(c, (0.07, 0.012, 0.08), "white", taper=(0.3, 1), rot=(0, math.pi, 0), bones=B(Chest=1))
    for k in range(4):                                                  # buttons down the placket
        z = hipz - 0.06 + k * 0.1
        c, _ = on_shell(0.0, z, lift=0.008)
        m.ball(c, (0.011, 0.006, 0.011), "hair_grey", seg=6, rings=4, bones=bone_at(z))
    px, pz = 0.085, sz - 0.1                                            # breast pocket (his left) + blue pen
    c, yaw = on_shell(px, pz, lift=0.009)
    m.box(c, (0.075, 0.012, 0.08), "ochre_check", rot=(0, 0, yaw), bones=B(Chest=1))
    m.tube(c + Vector((0.02, -0.008, 0)), c + Vector((0.02, -0.008, 0.08)), 0.007, 0.007, "pen_blue", seg=4, bones=B(Chest=1))
    limbs(m, p, sleeve="ochre", sleeve_len=0.28, sleeve_r=0.072, roll="ochre_check", pants="charcoal",
          pants_r=(0.105, 0.1), arm_r=0.056, hand=1.3, sandals=True, sandal="sandal_brown")
    # wristwatch on the left wrist
    wx = p["shoulder_x"] + p["arm_len"] * 0.86
    m.tube((wx - 0.05, 0, sz), (wx - 0.02, 0, sz), 0.058, 0.058, "metal", seg=6, bones=B(LeftLowerArm=1))
    m.box((wx - 0.035, 0, sz + 0.055), (0.035, 0.035, 0.012), "white", bones=B(LeftLowerArm=1))
    return m, p


MEI_KEYS = {
    "Smile": [("mouth", "scale", (1.3, 1, 1.5)), ("teeth", "scale", (1.3, 1, 1.2)), ("brow_L", "move", (0, 0, 0.01)),
              ("brow_R", "move", (0, 0, 0.01)), ("eye_R", "scale", (1.05, 1, 0.3))],                   # the cheeky wink
    "Exasperation": [("brow_L", "tilt", -0.4), ("brow_R", "tilt", 0.4), ("brow_L", "move", (0, 0, 0.014)),
                     ("brow_R", "move", (0, 0, 0.014)), ("mouth", "scale", (1.1, 1, 2.2)), ("mouth", "move", (0, 0, -0.008)),
                     ("teeth", "scale", (0.8, 1, 1)), ("eye_L", "scale", (1, 1, 0.85))],
    "Focus": [("brow_L", "tilt", 0.4), ("brow_R", "tilt", -0.4), ("brow_L", "move", (0, 0, -0.01)),
              ("brow_R", "move", (0, 0, -0.01)), ("eye_L", "scale", (1, 1, 0.65)), ("eye_R", "scale", (1, 1, 0.65)),
              ("mouth", "scale", (0.6, 1, 0.4)), ("teeth", "scale", (0.01, 1, 0.01))],
    "Blink": [("eye_L", "scale", (1, 1, 0.08)), ("eye_R", "scale", (1, 1, 0.08))],
}

RAVI_KEYS = {
    "Amused": [("mouth", "scale", (1.3, 1, 1.4)), ("teeth", "scale", (1.3, 1, 1.1)), ("moustache", "move", (0, 0, 0.008)),
               ("moustache", "scale", (1.12, 1, 1)), ("brow_L", "move", (0, 0, 0.01)), ("brow_R", "move", (0, 0, 0.01)),
               ("eye_L", "scale", (1, 1, 0.8)), ("eye_R", "scale", (1, 1, 0.8))],
    "Alarmed": [("brow_L", "move", (0, 0, 0.026)), ("brow_R", "move", (0, 0, 0.026)), ("brow_L", "tilt", -0.25),
                ("brow_R", "tilt", 0.25), ("eye_L", "scale", (1.15, 1, 1.35)), ("eye_R", "scale", (1.15, 1, 1.35)),
                ("mouth", "scale", (0.85, 1, 3.0)), ("mouth", "move", (0, 0, -0.014)), ("moustache", "move", (0, 0, -0.006))],
    "Suspicious": [("brow_L", "tilt", 0.5), ("brow_L", "move", (0, 0, -0.012)), ("brow_R", "move", (0, 0, 0.012)),
                   ("eye_L", "scale", (1, 1, 0.5)), ("eye_R", "scale", (1, 1, 0.7)), ("mouth", "scale", (0.6, 1, 0.5)),
                   ("mouth", "move", (0.012, 0, 0)), ("teeth", "scale", (0.01, 1, 0.01)), ("moustache", "move", (0.006, 0, -0.004))],
    "Blink": [("eye_L", "scale", (1, 1, 0.08)), ("eye_R", "scale", (1, 1, 0.08))],
}


FACE_KEYS = {
    "Grin": [("mouth", "scale", (1.35, 1, 1.6)), ("mouth", "move", (0, 0, 0.004)), ("teeth", "scale", (1.35, 1, 1.2)),
             ("brow_L", "move", (0, 0, 0.008)), ("brow_R", "move", (0, 0, 0.008))],
    "Alarm": [("brow_L", "move", (0, 0, 0.022)), ("brow_R", "move", (0, 0, 0.022)),
              ("eye_L", "scale", (1.15, 1, 1.3)), ("eye_R", "scale", (1.15, 1, 1.3)),
              ("mouth", "scale", (0.8, 1, 2.8)), ("mouth", "move", (0, 0, -0.012)), ("teeth", "move", (0, 0, 0.004))],
    "Determined": [("brow_L", "tilt", 0.45), ("brow_R", "tilt", -0.45), ("brow_L", "move", (0, 0, -0.008)),
                   ("brow_R", "move", (0, 0, -0.008)), ("eye_L", "scale", (1, 1, 0.7)), ("eye_R", "scale", (1, 1, 0.7)),
                   ("mouth", "scale", (0.8, 1, 0.45)), ("teeth", "scale", (0.01, 1, 0.01))],
    "Blink": [("eye_L", "scale", (1, 1, 0.08)), ("eye_R", "scale", (1, 1, 0.08))],
}
