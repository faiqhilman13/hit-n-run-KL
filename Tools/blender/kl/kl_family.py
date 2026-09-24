"""
The family (Pak Mat, Mak Som, Along, Adik) and the townsfolk (town man, town aunty, pakcik,
kid, polis, Datuk Mega), rebuilt in the KL handoff style so the whole cast matches Aiman,
Mei and Ravi: faceted primitives on the shared Humanoid skeleton, one palette material,
rigid-per-part skinning, Grin / Alarm / Determined / Blink face keys (+ moustache).

Every recolourable garment uses its own palette cell (see kl_core.PALETTE) so the game can
repaint it per instance: costumes, and variety for the pedestrian templates.
Coordinates: front = -Y, character's left = +X, feet at z = 0.
"""
import math
import random
from mathutils import Vector

import kl_core as K
import kl_characters as C
from kl_characters import (B, DETAIL, proportions, face, head_shape, spiky_hair, limbs, torso, glasses, FACE_KEYS,
                           toon_mesher, eye_layout, muzzle_front)


# ------------------------------------------------------------------------------ helpers
def shell_fns(p, width, depth, belly, hem):
    """Surface of torso()'s smooth body: returns on_shell(x, z, side, lift) -> (point, yaw),
    bone_at(z) and shell(z) -> (x radius, y radius), for buttons, ties, badges and trims."""
    sz, hipz = p["shoulder_z"], p["hip_z"]
    mid = hipz + (sz - hipz) * 0.5

    def shell(z):
        w, d, _ = C.torso_at(p, width, depth, belly, z)
        return w, d

    def on_shell(x, z, side=-1, lift=0.004):
        w, d, fy = C.torso_at(p, width, depth, belly, z)
        u = max(-0.95, min(0.95, x / w))
        y = d * math.sqrt(1 - u * u)
        slope = d * u / (w * math.sqrt(1 - u * u))
        return Vector((x, fy + side * (y + lift), z)), math.atan(slope) * -side

    def bone_at(z):
        return B(Spine=1) if z <= mid else B(Chest=1)

    return on_shell, bone_at, shell


def motifs(m, p, sh, zs, xfs, colors, size=0.05, rng=None, both=True):
    """Batik: a bold border band round the shirt near the hem and a thinner one across the
    chest, painted flat onto the smooth body (clean shapes that read at gameplay distance)."""
    z_lo, z_hi = min(zs), max(zs)
    bands = [(z_lo - 0.01, z_lo + 0.05, colors[0]), (z_hi - 0.03, z_hi - 0.005, colors[0])]

    def paint(c):
        for lo, hi, col in bands:
            if lo <= c.z <= hi:
                return col
        return None
    C.recolor_tagged(m, "torso", paint)


def flare(m, p, color, top, bottom, width, bones_hips=0.45, taper=1.15, depth_f=0.85):
    """A skirt / tunic / samping panel that follows the legs: one flared tube per side,
    blended between the hips and that thigh so it swings with the stride."""
    hx = p["hip_x"]
    for s, side in ((1, "Left"), (-1, "Right")):
        m.tube(Vector((s * hx * 0.55, 0, top)), Vector((s * hx * 1.05, 0, bottom)), width, width * taper, color, seg=16,
               bones={"Hips": bones_hips, f"{side}UpperLeg": 1 - bones_hips}, squash=(1, depth_f))


def leg_bands(m, p, pants_r, zs, color, ul_only=False):
    """Woven check bands painted round sarong-wrapped legs."""
    for side in ("Left", "Right"):
        def paint(c, zs=zs):
            if ul_only and c.z < p["knee_z"]:
                return None
            return color if any(z - 0.004 <= c.z <= z + 0.034 for z in zs) else None
        C.recolor_tagged(m, f"leg_{side}", paint)


def songkok(m, hz, hr, color="songkok", h=0.13):
    """Black velvet songkok: an oval, slightly tapered cap sitting on the crown."""
    m.cyl((0, hr * 0.06, hz + hr * 0.74 + h / 2), hr * 0.98, hr * 0.94, h, color, seg=12, rot=(0.08, 0, 0),
          scale_xy=(1, 0.9), bones=B(Head=1))


def cropped_hair(m, hz, hr, color="hair"):
    """Short back-and-sides under a cap: nape block, sideburns, a mass at the back."""
    m.ball((0, hr * 0.38, hz + hr * 0.25), (hr * 1.0, hr * 0.72, hr * 0.78), color, seg=8, rings=5, bones=B(Head=1))
    m.box((0, hr * 0.55, hz - hr * 0.15), (hr * 1.6, hr * 0.6, hr * 0.8), color, taper=(0.85, 0.8), bevel=0.02, bones=B(Head=1))
    for s in (1, -1):
        m.box((s * hr * 0.9, -hr * 0.1, hz + hr * 0.02), (0.035, hr * 0.5, hr * 0.5), color, bones=B(Head=1))


def curly_mop(m, hz, hr, rng, color="hair", n=30, puff=0.3):
    """Big curly mop (Along, the town man): a round mass covered in curls, clear of the face."""
    cen = Vector((0, hr * 0.22, hz + hr * 0.42))
    m.ball(cen, (hr * 1.1, hr * 1.0, hr * 0.88), color, seg=9, rings=6, bones=B(Head=1))
    m.box((0, hr * 0.55, hz - hr * 0.12), (hr * 1.6, hr * 0.6, hr * 0.8), color, taper=(0.85, 0.8), bevel=0.02, bones=B(Head=1))
    for s in (1, -1):
        m.box((s * hr * 0.9, -hr * 0.1, hz + hr * 0.02), (0.035, hr * 0.5, hr * 0.5), color, bones=B(Head=1))
    for i in range(n):
        a = rng.uniform(0, math.tau)
        el = rng.uniform(-0.1, 1.2)
        d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el))).normalized()
        if d.y < -0.2 and d.z < 0.55:
            continue
        c = cen + Vector((d.x * hr * 1.05, d.y * hr * 0.95, d.z * hr * 0.85))
        m.ball(c, (hr * puff,) * 3, color, seg=6, rings=4, bones=B(Head=1))
    for i in range(5):                                                         # curly fringe
        x = (i - 2) * hr * 0.36
        m.ball((x, -hr * 0.62, hz + hr * (0.78 - 0.04 * abs(i - 2))), (hr * 0.26,) * 3, color, seg=6, rings=4, bones=B(Head=1))


def pigtails(m, hz, hr, color="hair", tie="cord_red"):
    """Adik: rounded cap of hair, blunt fringe, two bunches tied high with red bobbles."""
    m.ball((0, hr * 0.14, hz + hr * 0.36), (hr * 1.12, hr * 1.08, hr * 0.96), color, seg=14, rings=10, bones=B(Head=1))
    m.box((0, hr * 0.6, hz - hr * 0.1), (hr * 1.7, hr * 0.55, hr * 0.8), color, taper=(0.85, 0.8), bevel=0.02, bones=B(Head=1))
    for i in range(5):
        x = (i - 2) * hr * 0.34
        base = Vector((x, -hr * 0.62, hz + hr * 0.92))
        m.tube(base, base + Vector((x * 0.1, -0.05, -0.08)), 0.05, 0.02, color, seg=4, bones=B(Head=1), twist=0.4)
    for s in (1, -1):                                                           # small bunches hanging down
        knot = Vector((s * hr * 1.05, hr * 0.3, hz + hr * 0.42))
        m.ball(knot, (0.032, 0.032, 0.032), tie, seg=6, rings=4, bones=B(Head=1))
        m.ball(knot + Vector((s * 0.035, 0.01, -0.06)), (0.04, 0.035, 0.06), color, seg=6, rings=5, bones=B(Head=1))
        m.tube(knot + Vector((s * 0.04, 0.01, -0.08)), knot + Vector((s * 0.055, 0.02, -0.19)), 0.032, 0.01, color, seg=5,
               bones=B(Head=1))


def moustache(m, hz, hr, color="hair", size=1.0):
    fy = muzzle_front(hr)                     # sits on the muzzle, over the mouth
    mz = hz - hr * 0.36
    m.box((0, fy - 0.006, mz), (0.12 * size, 0.04, 0.045 * size), color, taper=(0.75, 1), bevel=0.01, bones=B(Head=1),
          tag="moustache")
    for s in (1, -1):
        m.tube(Vector((s * 0.045 * size, fy - 0.006, mz - 0.01)), Vector((s * 0.08 * size, fy + 0.01, mz - 0.05)),
               0.02 * size, 0.009, color, seg=4, bones=B(Head=1), tag="moustache")


def tudung(m, p, hz, hr, color, drape=0.22, rim=None):
    """Tudung: wraps the head and neck, frames the face with a soft rim, and drapes over
    the shoulders and chest. The face stays clear (ears hidden)."""
    rim = rim or color
    # wrap sits back on the head so the whole face (brows to chin) stays open
    m.ball((0, hr * 0.36, hz + hr * 0.2), (hr * 1.16, hr * 1.08, hr * 1.18), color, seg=10, rings=7, bones=B(Head=1))
    m.ball((0, -hr * 0.5, hz + hr * 0.62), (hr * 0.86, hr * 0.34, hr * 0.3), color, seg=16, rings=10, rot=(-0.35, 0, 0),
           bones=B(Head=1))                                                       # band across the forehead
    for s_ in (1, -1):                                                           # cheek panels down to the chin
        m.ball((s_ * hr * 0.86, -hr * 0.18, hz - hr * 0.08), (hr * 0.2, hr * 0.62, hr * 0.84), color, seg=14, rings=10,
               bones=B(Head=1))
    n = 28                                                                       # one smooth rim framing the face
    e = lambda a: Vector((math.cos(a) * hr * 0.86, -hr * 0.66 - 0.12 * hr * max(0.0, math.sin(a)),
                          hz - hr * 0.12 + math.sin(a) * hr * 1.0))
    ring = [e(i / n * math.tau) for i in range(n + 1)]
    C.hose(m, ring, [(0.03, 0.03)] * len(ring), lambda t: rim, lambda t: B(Head=1), seg=10, ref=(0, 1, 0))
    # chin wrap + drape over the shoulders and chest
    m.tube((0, 0, p["neck_z"] + 0.04), (0, 0, p["head_z"] + 0.02), hr * 1.0, hr * 1.02, color, seg=10, bones=B(Neck=1))
    top, bot = p["neck_z"] + 0.02, p["shoulder_z"] - drape
    m.tube((0, 0, top), (0, 0, bot), hr * 1.05, p["shoulder_x"] * 1.55, color, seg=12, bones=B(Chest=1), squash=(1, 0.72))


def police_cap(m, hz, hr, color="police_blue"):
    m.cyl((0, hr * 0.04, hz + hr * 0.92), hr * 1.02, hr * 1.22, 0.1, color, seg=12, rot=(0.06, 0, 0), bones=B(Head=1))
    m.cyl((0, hr * 0.04, hz + hr * 0.78), hr * 1.0, hr * 1.0, 0.05, "bumper_black", seg=12, bones=B(Head=1))
    m.box((0, -hr * 1.0, hz + hr * 0.68), (hr * 1.45, hr * 0.75, 0.025), "bumper_black", rot=(0.35, 0, 0), bones=B(Head=1))
    m.box((0, -hr * 1.1, hz + hr * 0.95), (0.05, 0.012, 0.05), "gold", rot=(0, math.pi / 4, 0), bones=B(Head=1))


def belt(m, sh, z, color="bumper_black", buckle="gold"):
    on_shell, bone_at, shell = sh
    xr, yr = shell(z)
    m.tube((0, 0, z), (0, 0, z + 0.05), xr * 1.03, xr * 1.03, color, seg=10, bones=B(Hips=1), squash=(1, yr / xr))
    c, _ = on_shell(0, z + 0.025, lift=0.012)
    m.box(c, (0.07, 0.014, 0.05), buckle, bones=B(Hips=1))


def wrist_watch(m, p, color="metal", face_col="white", side=1):
    sz = p["shoulder_z"]
    wx = side * (p["shoulder_x"] + p["arm_len"] * 0.86)
    bone = "LeftLowerArm" if side > 0 else "RightLowerArm"
    m.tube((wx - side * 0.05, 0, sz), (wx - side * 0.02, 0, sz), 0.056, 0.056, color, seg=6, bones={bone: 1})
    m.box((wx - side * 0.035, 0, sz + 0.054), (0.035, 0.035, 0.012), face_col, bones={bone: 1})


# ------------------------------------------------------------------------------ the family
def build_pakmat(seed=41):
    """Pak Mat: the dad. Easy-going, big belly, orange batik shirt, green check sarong,
    black songkok, moustache, sleepy lids, sandals. ~1.66 m."""
    rng = random.Random(seed)
    p = proportions(height=1.66, head=0.32, shoulder_w=0.2, arm_len=0.56, hip_w=0.11, hip_frac=0.5, foot_len=0.26)
    m = toon_mesher()
    hz, hr = head_shape(m, p, jaw=1.08)
    face(m, p, nose=1.45, mouth_w=0.1, lids=True, brow_w=0.08)
    cropped_hair(m, hz, hr)
    songkok(m, hz, hr)
    moustache(m, hz, hr)
    width, depth, belly = 0.2, 0.14, 1.35
    hem = p["hip_z"] - 0.1
    torso(m, p, "batik_orange", collar="batik_orange", belly=belly, width=width, depth=depth, hem_z=hem, pants="sarong_green")
    sh = shell_fns(p, width, depth, belly, hem)
    motifs(m, p, sh, [hem + 0.06 + k * 0.1 for k in range(5)], [-0.7, -0.35, 0.0, 0.35, 0.7], ["batik_dark", "gold"], rng=rng)
    pr = (0.125, 0.13)
    limbs(m, p, sleeve="batik_orange", sleeve_len=0.3, sleeve_r=0.074, roll="batik_dark", pants="sarong_green", pants_r=pr,
          arm_r=0.055, sandals=True, sandal="sandal_brown")
    leg_bands(m, p, pr, [p["hip_z"] - 0.2, p["knee_z"] + 0.08, p["knee_z"] - 0.12, p["ankle_z"] + 0.18], "sarong_check")
    c, _ = sh[0](0.03, p["hip_z"] - 0.06, lift=0.02)                           # sarong knot at the waist
    m.box(c, (0.09, 0.03, 0.09), "sarong_green", bevel=0.01, bones=B(Hips=1))
    return m, p


def build_maksom(seed=43):
    """Mak Som: the mum. Pink baju kurung with a flower print, blue tudung, blue slippers. ~1.57 m."""
    rng = random.Random(seed)
    p = proportions(height=1.57, head=0.3, shoulder_w=0.165, arm_len=0.52, hip_w=0.1, foot_len=0.23)
    m = toon_mesher()
    hz, hr = head_shape(m, p, jaw=0.9)
    face(m, p, eye_r=0.045, nose=0.95, mouth_w=0.085, ears=False, lashes=True, brow_w=0.065)
    tudung(m, p, hz, hr, "tudung_blue", drape=0.26)
    width, depth, belly = 0.175, 0.125, 1.12
    hem = p["hip_z"] - 0.08
    torso(m, p, "kurung_pink", belly=belly, width=width, depth=depth, hem_z=hem, pants="kurung_skirt")
    sh = shell_fns(p, width, depth, belly, hem)
    motifs(m, p, sh, [hem + 0.08 + k * 0.1 for k in range(3)], [-0.6, 0.0, 0.6], ["kurung_flower", "gold"], size=0.04, rng=rng)
    flare(m, p, "kurung_pink", p["hip_z"] + 0.02, p["knee_z"] + 0.12, 0.13, taper=1.2)           # long tunic
    pr = (0.12, 0.13)
    limbs(m, p, sleeve="kurung_pink", sleeve_r=0.06, long_sleeve=True, pants="kurung_skirt", pants_r=pr, arm_r=0.047,
          hand=1.15, sandals=True, sandal="slipper_blue")
    return m, p


def build_along(seed=47):
    """Along: the teenage son. Red tee, blue knee shorts, big curly mop, sneakers. ~1.64 m."""
    rng = random.Random(seed)
    p = proportions(height=1.64, head=0.3, shoulder_w=0.175, arm_len=0.55, hip_w=0.095, foot_len=0.26)
    m = toon_mesher()
    hz, hr = head_shape(m, p, jaw=0.95)
    face(m, p, nose=1.1, mouth_w=0.1)
    curly_mop(m, hz, hr, rng, n=34)
    torso(m, p, "tshirt_red", collar=None, width=0.17, depth=0.115, hem_z=p["hip_z"] - 0.1, pants="shorts_blue")
    m.tube((0, 0, p["neck_z"] - 0.025), (0, 0, p["neck_z"] + 0.005), 0.068, 0.066, "white", seg=8, bones=B(Chest=1))
    limbs(m, p, sleeve="tshirt_red", sleeve_len=0.26, sleeve_r=0.064, pants="shorts_blue", pants_r=(0.1, 0.09), arm_r=0.047,
          shorts=True)
    m.tube((-(p["shoulder_x"] + p["arm_len"] * 0.8), 0, p["shoulder_z"]),
           (-(p["shoulder_x"] + p["arm_len"] * 0.86), 0, p["shoulder_z"]), 0.05, 0.05, "umb_yellow", seg=6,
           bones=B(RightLowerArm=1))                                            # sweatband
    return m, p


def build_adik(seed=53):
    """Adik: the little sister. Red tee-dress with a white collar and hem, pigtails with
    red bobbles, blue slippers, extra-big head and eyes. ~1.12 m."""
    rng = random.Random(seed)
    p = proportions(height=1.12, head=0.3, shoulder_w=0.115, arm_len=0.36, hip_w=0.068, hip_frac=0.46, foot_len=0.17)
    p["neck_z"] = p["head_z"] - 0.035
    p["shoulder_z"] = p["neck_z"] - 0.05
    m = toon_mesher()
    hz, hr = head_shape(m, p, jaw=0.85)
    face(m, p, eye_r=0.052, nose=0.7, mouth_w=0.075, lashes=True, brow_w=0.06)
    pigtails(m, hz, hr)
    hem = p["hip_z"] - 0.02
    torso(m, p, "dress_red", width=0.125, depth=0.095, hem_z=hem, pants="dress_red")
    m.tube((0, 0, p["neck_z"] - 0.02), (0, 0, p["neck_z"] + 0.005), 0.06, 0.058, "dress_trim", seg=8, bones=B(Chest=1))
    flare(m, p, "dress_red", p["hip_z"] + 0.04, p["knee_z"] + 0.03, 0.085, taper=1.45)
    for s, side in ((1, "Left"), (-1, "Right")):                                 # white hem band
        z = p["knee_z"] + 0.045
        m.tube(Vector((s * p["hip_x"] * 1.03, 0, z)), Vector((s * p["hip_x"] * 1.04, 0, z - 0.025)), 0.085 * 1.43,
               0.085 * 1.46, "dress_trim", seg=8, bones={"Hips": 0.45, f"{side}UpperLeg": 0.55}, squash=(1, 0.85))
    limbs(m, p, sleeve="dress_red", sleeve_len=0.22, sleeve_r=0.048, pants="dress_red", pants_r=(0.07, 0.065), arm_r=0.036,
          hand=0.95, shorts=True, sandals=True, sandal="slipper_blue")
    return m, p


# ------------------------------------------------------------------------------ townsfolk
def build_townman(seed=61):
    """Town man template: blue batik shirt, khaki trousers, curly hair, sneakers. ~1.70 m.
    Recoloured per pedestrian (batik_blue / pants_khaki / skin)."""
    rng = random.Random(seed)
    p = proportions(height=1.7, head=0.3, shoulder_w=0.19, arm_len=0.57, hip_w=0.1, foot_len=0.26)
    m = toon_mesher()
    hz, hr = head_shape(m, p)
    face(m, p, nose=1.15, mouth_w=0.095)
    curly_mop(m, hz, hr, rng, n=18, puff=0.24)
    width, depth, belly = 0.185, 0.13, 1.08
    hem = p["hip_z"] - 0.1
    torso(m, p, "batik_blue", collar="batik_blue", belly=belly, width=width, depth=depth, hem_z=hem, pants="pants_khaki")
    sh = shell_fns(p, width, depth, belly, hem)
    motifs(m, p, sh, [hem + 0.06 + k * 0.1 for k in range(5)], [-0.66, -0.22, 0.22, 0.66], ["batik_blue_dark", "white"],
           size=0.045, rng=rng)
    limbs(m, p, sleeve="batik_blue", sleeve_len=0.3, sleeve_r=0.07, pants="pants_khaki", pants_r=(0.1, 0.092), arm_r=0.05)
    return m, p


def build_townaunty(seed=67):
    """Town aunty template: lilac baju kurung over a red check sarong skirt, mint tudung,
    slippers, comfortable middle. ~1.56 m."""
    rng = random.Random(seed)
    p = proportions(height=1.56, head=0.3, shoulder_w=0.17, arm_len=0.52, hip_w=0.105, foot_len=0.23)
    m = toon_mesher()
    hz, hr = head_shape(m, p, jaw=0.95)
    face(m, p, eye_r=0.044, nose=1.0, mouth_w=0.09, ears=False, lashes=True)
    tudung(m, p, hz, hr, "pastel_mint", drape=0.2)
    width, depth, belly = 0.18, 0.13, 1.28
    hem = p["hip_z"] - 0.08
    torso(m, p, "pastel_lilac", belly=belly, width=width, depth=depth, hem_z=hem, pants="sarong_red")
    flare(m, p, "pastel_lilac", p["hip_z"] + 0.02, p["knee_z"] + 0.24, 0.12, taper=1.08)
    pr = (0.125, 0.13)
    limbs(m, p, sleeve="pastel_lilac", sleeve_r=0.062, long_sleeve=True, pants="sarong_red", pants_r=pr, arm_r=0.05,
          hand=1.15, sandals=True, sandal="slipper_blue")
    leg_bands(m, p, pr, [p["knee_z"] - 0.06, p["knee_z"] - 0.2, p["ankle_z"] + 0.14], "sarong_red_check")
    return m, p


def build_pakcik(seed=71):
    """Pakcik (elder) template: white baju melayu with a green check samping round the hips,
    songkok, round glasses, grey moustache, sleepy lids. ~1.63 m."""
    rng = random.Random(seed)
    p = proportions(height=1.63, head=0.3, shoulder_w=0.175, arm_len=0.54, hip_w=0.1, foot_len=0.25)
    m = toon_mesher()
    hz, hr = head_shape(m, p, jaw=1.0)
    face(m, p, nose=1.35, mouth_w=0.085, lids=True, brow="grey_hair")
    cropped_hair(m, hz, hr, "grey_hair")
    songkok(m, hz, hr)
    glasses(m, hz, hr, 0.046, color="metal_dark")
    moustache(m, hz, hr, "grey_hair", size=0.9)
    width, depth = 0.175, 0.125
    hem = p["hip_z"] - 0.14
    torso(m, p, "baju_white", collar="baju_white", width=width, depth=depth, hem_z=hem, pants="baju_white")
    sh = shell_fns(p, width, depth, 1.0, hem)
    for k in range(3):                                                         # baju melayu front buttons
        c, _ = sh[0](0.0, p["neck_z"] - 0.05 - k * 0.06, lift=0.008)
        m.ball(c, (0.01, 0.006, 0.01), "gold", seg=6, rings=4, bones=B(Chest=1))
    flare(m, p, "sarong_green", p["hip_z"] + 0.06, p["knee_z"] + 0.12, 0.13, taper=1.1)        # samping
    for s, side in ((1, "Left"), (-1, "Right")):
        for z in (p["hip_z"] - 0.05, p["knee_z"] + 0.2):
            m.tube(Vector((s * p["hip_x"] * 0.8, 0, z)), Vector((s * p["hip_x"] * 0.83, 0, z - 0.025)), 0.13 * 1.08, 0.13 * 1.1,
                   "sarong_check", seg=8, bones={"Hips": 0.45, f"{side}UpperLeg": 0.55}, squash=(1, 0.85))
    limbs(m, p, sleeve="baju_white", sleeve_r=0.064, long_sleeve=True, pants="baju_white", pants_r=(0.1, 0.095), arm_r=0.048,
          sandals=True, sandal="sandal_brown")
    return m, p


def build_kid(seed=73):
    """Kid template: white school-style shirt, blue shorts, spiky hair, sneakers. ~1.15 m."""
    rng = random.Random(seed)
    p = proportions(height=1.15, head=0.29, shoulder_w=0.12, arm_len=0.37, hip_w=0.07, hip_frac=0.47, foot_len=0.18)
    p["neck_z"] = p["head_z"] - 0.035
    p["shoulder_z"] = p["neck_z"] - 0.05
    m = toon_mesher()
    hz, hr = head_shape(m, p, jaw=0.88)
    face(m, p, eye_r=0.05, nose=0.8, mouth_w=0.08)
    spiky_hair(m, p, hz, hr, rng, spikes=18, length=0.06)
    torso(m, p, "kid_shirt", collar="kid_shirt", width=0.13, depth=0.1, hem_z=p["hip_z"] - 0.06, pants="kid_shorts")
    limbs(m, p, sleeve="kid_shirt", sleeve_len=0.24, sleeve_r=0.05, pants="kid_shorts", pants_r=(0.075, 0.07), arm_r=0.036,
          hand=0.95, shorts=True)
    return m, p


def build_polis(seed=79):
    """Polis: stocky officer in a dark-blue uniform, peaked cap with badge, moustache, belt,
    epaulettes, black shoes. ~1.76 m."""
    rng = random.Random(seed)
    p = proportions(height=1.76, head=0.31, shoulder_w=0.21, arm_len=0.58, hip_w=0.11, foot_len=0.28)
    m = toon_mesher()
    hz, hr = head_shape(m, p, jaw=1.12)
    face(m, p, nose=1.2, mouth_w=0.095, brow_w=0.085)
    cropped_hair(m, hz, hr)
    police_cap(m, hz, hr)
    moustache(m, hz, hr)
    width, depth, belly = 0.2, 0.14, 1.2
    hem = p["hip_z"] - 0.04
    torso(m, p, "police_blue", collar="police_blue", belly=belly, width=width, depth=depth, hem_z=hem, pants="police_blue",
          pockets="police_blue")
    sh = shell_fns(p, width, depth, belly, hem)
    belt(m, sh, p["hip_z"] - 0.02)
    c, yaw = sh[0](0.1, p["shoulder_z"] - 0.1, lift=0.012)
    m.box(c, (0.05, 0.01, 0.06), "gold", rot=(0, 0, yaw), bones=B(Chest=1))                   # badge
    for s in (1, -1):                                                           # epaulettes
        m.box((s * (p["shoulder_x"] - 0.02), 0, p["shoulder_z"] + 0.055), (0.12, 0.08, 0.02), "bumper_black", bones=B(Chest=1))
    limbs(m, p, sleeve="police_blue", sleeve_r=0.07, long_sleeve=True, pants="police_blue", pants_r=(0.105, 0.098), arm_r=0.053,
          shoe="bumper_black", sole="bumper_black")
    return m, p


def build_datukmega(seed=83):
    """Datuk Mega, the villain: enormous belly in a black suit, red tie, songkok, dark shades,
    big moustache, gold watch. ~1.70 m."""
    rng = random.Random(seed)
    p = proportions(height=1.7, head=0.33, shoulder_w=0.22, arm_len=0.57, hip_w=0.12, hip_frac=0.49, foot_len=0.28)
    m = toon_mesher()
    hz, hr = head_shape(m, p, jaw=1.15)
    face(m, p, nose=1.5, mouth_w=0.1, brow_w=0.09)
    cropped_hair(m, hz, hr)
    songkok(m, hz, hr)
    moustache(m, hz, hr, size=1.2)
    ex, ey, ez, er = eye_layout(hz, hr, 0.046)
    fy = ey - er * 0.85
    for s in (1, -1):                                                           # shades over the eyes
        m.box((s * ex, fy, ez), (er * 2.1, 0.02, er * 1.5), "bumper_black", bevel=0.01, bones=B(Head=1))
        m.tube((s * (ex + er), fy + 0.01, ez + 0.01), (s * hr * 0.98, 0.0, hz + hr * 0.14), 0.007, 0.007, "bumper_black",
               seg=4, bones=B(Head=1))
    width, depth, belly = 0.22, 0.16, 1.55
    hem = p["hip_z"] - 0.12
    torso(m, p, "suit_black", collar="suit_black", belly=belly, width=width, depth=depth, hem_z=hem, pants="suit_black")
    sh = shell_fns(p, width, depth, belly, hem)
    on_shell = sh[0]
    c, _ = on_shell(0, p["neck_z"] - 0.1, lift=0.006)                          # shirt V + tie
    m.box(c, (0.1, 0.012, 0.16), "white", taper=(1.0, 1.0), bones=B(Chest=1))
    for k, (z, w) in enumerate(((p["neck_z"] - 0.05, 0.035), (p["neck_z"] - 0.15, 0.045), (p["neck_z"] - 0.27, 0.05))):
        c, yaw = on_shell(0, z, lift=0.012 + 0.004 * k)
        m.box(c, (w, 0.012, 0.11), "tie_red", rot=(0, 0, yaw), bones=sh[1](z))
    for s in (1, -1):                                                           # lapels
        c, yaw = on_shell(s * 0.07, p["neck_z"] - 0.13, lift=0.008)
        m.box(c, (0.05, 0.01, 0.2), "suit_black", rot=(0, s * 0.35, yaw), bones=B(Chest=1))
    for k in range(2):                                                          # jacket buttons
        c, _ = on_shell(0.05, p["hip_z"] + 0.08 + k * 0.12, lift=0.01)
        m.ball(c, (0.014, 0.006, 0.014), "gold", seg=6, rings=4, bones=B(Spine=1))
    limbs(m, p, sleeve="suit_black", sleeve_r=0.078, long_sleeve=True, pants="suit_black", pants_r=(0.12, 0.11), arm_r=0.058,
          hand=1.35, shoe="bumper_black", sole="bumper_black")
    wrist_watch(m, p, "gold", "gold")
    return m, p


FAMILY_KEYS = {k: list(v) for k, v in FACE_KEYS.items()}
FAMILY_KEYS["Grin"] += [("moustache", "move", (0, 0, 0.007)), ("moustache", "scale", (1.12, 1, 1))]
FAMILY_KEYS["Alarm"] += [("moustache", "move", (0, 0, -0.006))]
FAMILY_KEYS["Determined"] += [("moustache", "scale", (0.95, 1, 1.05))]

BUILDERS = {
    "chr_pakmat": build_pakmat, "chr_maksom": build_maksom, "chr_along": build_along, "chr_adik": build_adik,
    "chr_townman": build_townman, "chr_townaunty": build_townaunty, "chr_pakcik": build_pakcik, "chr_kid": build_kid,
    "chr_polis": build_polis, "chr_datukmega": build_datukmega,
}
