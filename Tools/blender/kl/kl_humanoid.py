"""
Skinned humanoid characters for the KL game.

Skeleton: Unity-Humanoid-friendly bone names, T-pose rest (arms along +/-X), feet on the
ground at the origin, front = -Y. Mesh: one faceted mesh on the palette atlas, rigidly
weighted per body part with 50/50 blends at joint caps (the classic PS2-era approach -
clean bends, no rubbery skin). Face: brows/eyes/mouth are tagged geometry driven by
shape keys (Grin, Alarm, Determined, Blink).
"""
import bpy
import math
from mathutils import Vector, Matrix, Euler, Quaternion

import kl_core as K

# ------------------------------------------------------------------------------ skeleton
def skeleton(p):
    """Bone (name, head, tail, parent) from proportions dict p."""
    hz, kz, az = p["hip_z"], p["knee_z"], p["ankle_z"]
    sz, nz, hdz, top = p["shoulder_z"], p["neck_z"], p["head_z"], p["top_z"]
    hx, sx, ax = p["hip_x"], p["shoulder_x"], p["arm_len"]
    fl = p["foot_len"]
    bs = p.get("bag_side", 1)
    bones = [
        ("Hips", (0, 0, hz), (0, 0, hz + 0.1), None),
        ("Spine", (0, 0, hz + 0.1), (0, 0, hz + (sz - hz) * 0.5), "Hips"),
        ("Chest", (0, 0, hz + (sz - hz) * 0.5), (0, 0, nz - 0.02), "Spine"),
        ("Neck", (0, 0, nz - 0.02), (0, 0, hdz), "Chest"),
        ("Head", (0, 0, hdz), (0, 0, top), "Neck"),
        # accessory bones: bag (Aiman's satchel, Mei's woven bag) and a dangling piece
        # (Aiman's key cord, Mei's tea towel). bag_side: +1 = character's left hip.
        ("Satchel", (bs * (hx + 0.12), 0.02, hz - 0.02), (bs * (hx + 0.12), 0.02, hz - 0.14), "Hips"),
        ("KeyCord", (-bs * (hx + 0.04), -0.11, hz - 0.03), (-bs * (hx + 0.04), -0.11, hz - 0.15), "Hips"),
    ]
    for side, s in (("Left", 1), ("Right", -1)):
        bones += [
            (f"{side}Shoulder", (s * 0.04, 0, sz - 0.02), (s * sx, 0, sz), "Chest"),
            (f"{side}UpperArm", (s * sx, 0, sz), (s * (sx + ax * 0.45), 0, sz), f"{side}Shoulder"),
            (f"{side}LowerArm", (s * (sx + ax * 0.45), 0, sz), (s * (sx + ax * 0.86), 0, sz), f"{side}UpperArm"),
            (f"{side}Hand", (s * (sx + ax * 0.86), 0, sz), (s * (sx + ax * 1.02), 0, sz), f"{side}LowerArm"),
            (f"{side}UpperLeg", (s * hx, 0, hz - 0.02), (s * hx, 0, kz), "Hips"),
            (f"{side}LowerLeg", (s * hx, 0, kz), (s * hx, 0, az), f"{side}UpperLeg"),
            (f"{side}Foot", (s * hx, 0, az), (s * hx, -fl * 0.55, 0.03), f"{side}LowerLeg"),
            (f"{side}Toes", (s * hx, -fl * 0.55, 0.03), (s * hx, -fl * 0.9, 0.03), f"{side}Foot"),
        ]
    return bones


def build_armature(name, p, collection):
    arm_data = bpy.data.armatures.new(name + "_Rig")
    arm = bpy.data.objects.new(name + "_Rig", arm_data)
    collection.objects.link(arm)
    bpy.context.view_layer.update()
    for o in [o for o in bpy.context.view_layer.objects if o is not None]:
        o.select_set(False)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones
    for bname, h, t, parent in skeleton(p):
        b = eb.new(bname)
        b.head, b.tail = Vector(h), Vector(t)
        b.roll = 0.0
        if parent:
            b.parent = eb[parent]
            b.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    arm.show_in_front = True
    return arm


def _smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def _chain(t, joints):
    """joints: [(position, bone_before, bone_after, half_width)] in increasing t.
    Returns {bone: weight} with a smoothstep blend across each joint."""
    for pos, a, b, w in joints:
        if t <= pos - w:
            return {a: 1.0}
        if t < pos + w:
            k = _smooth((t - (pos - w)) / (2 * w))
            return {a: 1 - k, b: k}
    return {joints[-1][2]: 1.0}


def blend_weights(p, weights, mesh_ob):
    """Soft skinning for the cartoon cast. Primitives are authored rigid (one bone, or 50/50
    at a joint cap); here every limb / torso vertex is re-weighted by where it sits along its
    bone chain, blending across shoulders, elbows, wrists, hips, knees, ankles and the waist.
    With the length-subdivided limb tubes this bends arms and legs like rubber hoses instead
    of folding them like hinges. Intentional mixes (skirt flares 45/55) and accessories are
    left alone."""
    verts = mesh_ob.data.vertices
    sx, al = p["shoulder_x"], p["arm_len"]
    elbow, wrist = sx + al * 0.45, sx + al * 0.86
    hz, kz, az, sz = p["hip_z"], p["knee_z"], p["ankle_z"], p["shoulder_z"]
    mid = hz + (sz - hz) * 0.5
    out = []
    for vi, bones in weights:
        co = verts[vi].co
        keys = set(bones)
        vals = sorted(bones.values())
        rigid = len(keys) == 1 or (len(keys) == 2 and abs(vals[0] - 0.5) < 1e-6)
        side = "Left" if co.x > 0 else "Right"
        arm = {f"{side}UpperArm", f"{side}LowerArm", f"{side}Hand"}
        leg = {f"{side}UpperLeg", f"{side}LowerLeg"}
        new = None
        if rigid and keys & arm and keys <= arm | {"Chest"}:
            new = _chain(abs(co.x), [(sx + 0.02, "Chest", f"{side}UpperArm", 0.045),
                                     (elbow, f"{side}UpperArm", f"{side}LowerArm", 0.06),
                                     (wrist, f"{side}LowerArm", f"{side}Hand", 0.025)])
        elif rigid and keys & leg and keys <= leg | {"Hips"} and co.z > az + 0.02:
            new = _chain(-co.z, [(-(hz - 0.03), "Hips", f"{side}UpperLeg", 0.05),
                                 (-kz, f"{side}UpperLeg", f"{side}LowerLeg", 0.065)])
        elif rigid and len(keys) == 1 and keys <= {"Hips", "Spine", "Chest"}:
            new = _chain(co.z, [(hz + 0.1, "Hips", "Spine", 0.05), (mid, "Spine", "Chest", 0.07)])
        out.append((vi, new if new else bones))
    return out


def skin(mesh_ob, arm, weights):
    for bone in arm.data.bones:
        mesh_ob.vertex_groups.new(name=bone.name)
    for vi, bones in weights:
        for bname, w in bones.items():
            mesh_ob.vertex_groups[bname].add([vi], w, 'REPLACE')
    mesh_ob.parent = arm
    mod = mesh_ob.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm


# ------------------------------------------------------------------------------ face keys
def shape_keys(mesh_ob, tags, keys):
    """keys: {KeyName: [(tag, op, value), ...]} with ops: move (vec), scale (vec, about tag centroid),
    tilt (radians: inner ends of brows down/up)."""
    me = mesh_ob.data
    mesh_ob.shape_key_add(name="Basis", from_mix=False)
    base = [v.co.copy() for v in me.vertices]
    for kname, ops in keys.items():
        sk = mesh_ob.shape_key_add(name=kname, from_mix=False)
        sk.value = 0.0  # Blender 5 creates new keys switched on
        for tag, op, val in ops:
            idx = tags.get(tag, [])
            if not idx:
                continue
            cen = sum((base[i] for i in idx), Vector()) / len(idx)
            for i in idx:
                co = base[i].copy()
                if op == "move":
                    co += Vector(val)
                elif op == "scale":
                    co = cen + Vector((co - cen)[j] * val[j] for j in range(3))
                elif op == "tilt":  # rotate about Y through centroid (brow angle)
                    r = Matrix.Rotation(val, 3, 'Y')
                    co = cen + r @ (co - cen)
                sk.data[i].co = co


# ------------------------------------------------------------------------------ poses/clips
def R(x=0, y=0, z=0):
    """World-axis rotation (degrees) applied X then Y then Z... composed as Rz @ Ry @ Rx."""
    return (Matrix.Rotation(math.radians(z), 3, 'Z') @ Matrix.Rotation(math.radians(y), 3, 'Y')
            @ Matrix.Rotation(math.radians(x), 3, 'X'))


def arm_rot(side, lower=75, forward=0, twist=0):
    """Upper arm from T-pose: drop it by `lower` degrees, then swing forward."""
    s = 1 if side == "Left" else -1
    return Matrix.Rotation(math.radians(-forward), 3, 'X') @ Matrix.Rotation(math.radians(s * lower), 3, 'Y') @ \
        Matrix.Rotation(math.radians(twist * s), 3, 'X')


def elbow_rot(side, bend):
    s = 1 if side == "Left" else -1
    return Matrix.Rotation(math.radians(-s * bend), 3, 'Z')


def reach_rot(side, forward=90, lower=0):
    """Upper arm pointing forward (punch / handlebars) from T-pose."""
    s = 1 if side == "Left" else -1
    return Matrix.Rotation(math.radians(s * lower), 3, 'Y') @ Matrix.Rotation(math.radians(-s * forward), 3, 'Z')


def pose(**kw):
    """Build a pose dict. Keys are bone names -> 3x3 world-axis rotations; 'hips' -> offset."""
    return kw


def idle_arms(p=None, lower=76, fwd=4, elbow=12):
    p = p or {}
    p.update(LeftUpperArm=arm_rot("Left", lower, fwd), RightUpperArm=arm_rot("Right", lower, fwd),
             LeftLowerArm=elbow_rot("Left", elbow), RightLowerArm=elbow_rot("Right", elbow))
    return p


def legs(p, l_swing=0, r_swing=0, l_knee=0, r_knee=0, l_foot=0, r_foot=0):
    p.update(LeftUpperLeg=R(x=-l_swing), RightUpperLeg=R(x=-r_swing),
             LeftLowerLeg=R(x=l_knee), RightLowerLeg=R(x=r_knee),
             LeftFoot=R(x=-l_foot), RightFoot=R(x=-r_foot))
    return p


def ride_pose(bounce=0.0):
    p = idle_arms({}, 0, 0, 0)
    p.update(LeftUpperArm=reach_rot("Left", 70, 35), RightUpperArm=reach_rot("Right", 70, 35),
             LeftLowerArm=elbow_rot("Left", 25), RightLowerArm=elbow_rot("Right", 25),
             Spine=R(x=10), Chest=R(x=6), Head=R(x=-10))
    legs(p, 80, 80, 78, 78, -10, -10)
    p["hips"] = (0, 0, -0.34 + bounce)
    return p


def hand_flop(p, l=12, r=12):
    """Wrists: positive = droop (limp cartoon hands)."""
    p["LeftHand"] = R(y=l)
    p["RightHand"] = R(y=-r)
    return p


def sample(length, fn, step=2):
    """Key a clip every `step` frames from a pose function of the frame number."""
    return [(f, fn(f)) for f in range(0, length + 1, step)]


def clip_defs():
    """Cartoon clips (24 fps), written as functions of phase with offsets between body parts
    so everything overlaps: hips lead, chest counter-twists, arms and wrists lag behind,
    the head settles last. Walk (16 f, ~2.5 m/s) and run (12 f, ~6.5 m/s) strides match the
    game's movement speeds, and the Animator scales playback between them."""
    C = {}
    TAU = math.tau

    # ---------------------------------------------------------------- idle: breathe, shift weight, look around
    def idle_key(f):
        t = f / 96 * TAU
        br = math.sin(t * 2)
        p = idle_arms({}, 80 + 2 * br, 4, 14 + 4 * math.sin(t * 2 + 0.8))
        hand_flop(p, 14, 14)
        legs(p, 2, -2, 6 + 2 * math.sin(t), 6 - 2 * math.sin(t))
        p["Hips"] = R(y=1.5 * math.sin(t))
        p["Spine"] = R(x=2 + 1.5 * br)
        p["Chest"] = R(x=1 + 1.5 * br, z=2 * math.sin(t))
        p["Head"] = R(x=-2 + 2 * math.sin(t * 2 + 1), z=10 * math.sin(t))
        p["hips"] = (0.015 * math.sin(t), 0, -0.012 + 0.004 * br)
        return p
    C["idle"] = (96, True, sample(96, idle_key, 4))

    # ---------------------------------------------------------------- walk: bouncy Simpsons stroll
    def walk_key(f):
        ph = f / 16 * TAU
        s, c = math.sin(ph), math.cos(ph)
        p = {}
        legs(p, 30 * s, -30 * s,
             12 + 50 * max(0.0, c) ** 1.3, 12 + 50 * max(0.0, -c) ** 1.3,   # knees fold on the swing
             16 * s, -16 * s)                                                # heel strike / push off
        p["Hips"] = R(z=-8 * s, y=-3 * c)
        p["Spine"] = R(x=6, z=6 * s)
        p["Chest"] = R(x=3, z=5 * s)
        la = math.sin(ph - 0.35)                                             # arms lag the legs
        p["LeftUpperArm"] = arm_rot("Left", 74, -32 * la)
        p["RightUpperArm"] = arm_rot("Right", 74, 32 * la)
        p["LeftLowerArm"] = elbow_rot("Left", 16 + 30 * max(0.0, -math.sin(ph - 0.7)))
        p["RightLowerArm"] = elbow_rot("Right", 16 + 30 * max(0.0, math.sin(ph - 0.7)))
        hand_flop(p, 12 + 10 * math.sin(ph - 1.1), 12 - 10 * math.sin(ph - 1.1))
        p["Head"] = R(x=-3 + 3 * math.cos(2 * ph - 0.7), z=-3 * s)
        p["hips"] = (0.018 * math.sin(ph - 0.5), 0, 0.012 - 0.04 * abs(s))
        return p
    C["walk"] = (16, True, sample(16, walk_key, 2))

    # ---------------------------------------------------------------- run: airborne bounce, pumping arms
    def run_key(f):
        ph = f / 12 * TAU
        s, c = math.sin(ph), math.cos(ph)
        p = {}
        legs(p, 48 * s, -48 * s, 20 + 95 * max(0.0, c) ** 1.2, 20 + 95 * max(0.0, -c) ** 1.2, 20 * s, -20 * s)
        p["Hips"] = R(z=-12 * s)
        p["Spine"] = R(x=14, z=9 * s)
        p["Chest"] = R(x=6, z=7 * s)
        la = math.sin(ph - 0.25)
        p["LeftUpperArm"] = arm_rot("Left", 62, -50 * la)
        p["RightUpperArm"] = arm_rot("Right", 62, 50 * la)
        p["LeftLowerArm"] = elbow_rot("Left", 95 + 10 * la)
        p["RightLowerArm"] = elbow_rot("Right", 95 - 10 * la)
        hand_flop(p, 4, 4)
        p["Head"] = R(x=-12 + 4 * math.cos(2 * ph - 0.5), z=-4 * s)
        p["hips"] = (0.012 * math.sin(ph - 0.5), 0, 0.04 - 0.07 * abs(s))
        return p
    C["run"] = (12, True, sample(12, run_key, 1))

    # ---------------------------------------------------------------- panic: run with arms flailing overhead
    def panic_key(f):
        ph = f / 12 * TAU
        p = run_key(f)
        s = math.sin(ph)
        p["LeftUpperArm"] = arm_rot("Left", -70, 22 * s)
        p["RightUpperArm"] = arm_rot("Right", -70, -22 * s)
        p["LeftLowerArm"] = elbow_rot("Left", 30 + 25 * math.sin(ph * 2))
        p["RightLowerArm"] = elbow_rot("Right", 30 - 25 * math.sin(ph * 2))
        hand_flop(p, 25 * math.sin(ph * 2 - 0.6), -25 * math.sin(ph * 2 - 0.6))
        p["Spine"] = R(x=6, z=6 * s)
        p["Head"] = R(x=-16, z=16 * math.sin(ph * 2))
        return p
    C["panic"] = (12, True, sample(12, panic_key, 1))

    # ---------------------------------------------------------------- jump: anticipation -> stretch -> tuck -> land
    j0 = idle_key(0)
    j1 = idle_arms({}, 70, -35, 20); legs(j1, 40, 40, 72, 72, -20, -20); j1["Spine"] = R(x=22); j1["Head"] = R(x=-10)
    j1["hips"] = (0, 0, -0.15); hand_flop(j1, 20, 20)
    j2 = idle_arms({}, -60, 25, 10); legs(j2, 5, 5, 5, 5, -35, -35); j2["Spine"] = R(x=-6); j2["Head"] = R(x=-12)
    j2["hips"] = (0, 0, 0.04); hand_flop(j2, 5, 5)
    j3 = idle_arms({}, 20, 20, 50); legs(j3, 60, 35, 95, 60); j3["Spine"] = R(x=12); j3["Head"] = R(x=-4)
    j4 = idle_arms({}, 8, 10, 25); legs(j4, 25, 12, 28, 20, 12, 12); j4["Spine"] = R(x=6); hand_flop(j4, 25, 25)
    C["jump"] = (22, False, [(0, j0), (3, j1), (6, j2), (11, j3), (16, j4), (22, j4)])

    # ---------------------------------------------------------------- punch: wind-up, overshoot, settle
    def guard():
        g = idle_arms({}, 55, 35, 100); legs(g, 10, -8, 14, 10); g["Spine"] = R(x=6); hand_flop(g, 0, 0)
        return g
    p0 = guard()
    p1 = guard(); p1["RightUpperArm"] = arm_rot("Right", 50, -25); p1["RightLowerArm"] = elbow_rot("Right", 115)
    p1["Chest"] = R(z=-20); p1["Spine"] = R(x=2, z=-6); p1["Head"] = R(z=8)
    p2 = guard(); p2["RightUpperArm"] = reach_rot("Right", 96, 2); p2["RightLowerArm"] = elbow_rot("Right", 0)
    p2["Chest"] = R(z=28, x=6); p2["Spine"] = R(x=10, z=6); p2["Head"] = R(z=-6, x=4); p2["hips"] = (0, -0.05, -0.02)
    p2["LeftUpperArm"] = arm_rot("Left", 58, 10)
    p3 = guard(); p3["RightUpperArm"] = reach_rot("Right", 88, 5); p3["RightLowerArm"] = elbow_rot("Right", 12)
    p3["Chest"] = R(z=20, x=4); p3["Spine"] = R(x=8, z=4); p3["hips"] = (0, -0.03, -0.015)
    C["punch"] = (14, False, [(0, p0), (3, p1), (5, p2), (8, p3), (14, p0)])

    # ---------------------------------------------------------------- kick: chamber, snap, recover
    k0 = guard()
    k1 = idle_arms({}, 45, 30, 40); k1["RightUpperArm"] = arm_rot("Right", 45, -30)
    legs(k1, 12, 75, 14, 110); k1["Spine"] = R(x=-12); k1["Head"] = R(x=6); k1["hips"] = (0, 0, -0.03)
    k2 = idle_arms({}, 20, 15, 20); legs(k2, 12, 96, 12, 5, 0, -25); k2["Spine"] = R(x=-18); k2["Head"] = R(x=8)
    k2["hips"] = (0, 0.03, -0.03); hand_flop(k2, 20, 20)
    k3 = idle_arms({}, 30, 15, 25); legs(k3, 12, 80, 12, 20, 0, -15); k3["Spine"] = R(x=-12)
    C["kick"] = (16, False, [(0, k0), (4, k1), (7, k2), (9, k3), (16, k0)])

    # ---------------------------------------------------------------- ride / seated driving / mount / dismount
    def ride_key(f):
        ph = f / 24 * TAU
        p = ride_pose(0.015 * math.sin(ph))
        p["Head"] = R(x=-10 + 3 * math.sin(2 * ph - 0.5), z=4 * math.sin(ph))
        p["Spine"] = R(x=10 + 2 * math.sin(ph))
        p["LeftLowerArm"] = elbow_rot("Left", 25 + 4 * math.sin(ph))
        p["RightLowerArm"] = elbow_rot("Right", 25 - 4 * math.sin(ph))
        return p
    C["ride"] = (24, True, sample(24, ride_key, 3))
    m0 = idle_key(0)
    m1 = idle_arms({}, 50, 30, 30); m1["RightUpperLeg"] = R(y=55, x=-30); m1["RightLowerLeg"] = R(x=40)
    m1["Spine"] = R(x=14); m1["hips"] = (0, 0, -0.1)
    C["mount"] = (18, False, [(0, m0), (8, m1), (18, ride_key(0))])
    C["dismount"] = (18, False, [(0, ride_key(0)), (10, m1), (18, m0)])

    def sit_key(f):
        ph = f / 24 * TAU
        p = ride_pose(0)
        p["LeftUpperArm"] = reach_rot("Left", 75, 25); p["RightUpperArm"] = reach_rot("Right", 75, 25)
        p["Chest"] = R(z=5 * math.sin(ph))
        p["Head"] = R(x=-6, z=6 * math.sin(ph - 0.4))
        return p
    C["sit"] = (24, True, sample(24, sit_key, 4))

    # ---------------------------------------------------------------- knockdown: recoil, fall, bounce, get up
    d0 = idle_key(0)
    d1 = idle_arms({}, -40, 20, 30); d1["Spine"] = R(x=-25); d1["Head"] = R(x=-25); d1["Hips"] = R(x=-10)
    legs(d1, 10, -10, 25, 15); d1["hips"] = (0, 0, -0.04); hand_flop(d1, 30, 30)
    d2 = idle_arms({}, -70, 30, 20); d2["Hips"] = R(x=-55); legs(d2, 55, 35, 40, 20); d2["hips"] = (0, 0, -0.25)
    hand_flop(d2, 35, 35)
    d3 = idle_arms({}, -10, 0, 10); d3["Hips"] = R(x=-88); legs(d3, 20, 10, 15, 5); d3["hips"] = (0, 0, -0.75)
    d3["Head"] = R(x=10)
    d4 = idle_arms({}, 0, 0, 15); d4["Hips"] = R(x=-82); legs(d4, 35, 20, 30, 15); d4["hips"] = (0, 0, -0.66)
    d6 = dict(d3); d6["Head"] = R(x=-15, z=12)
    d7 = idle_arms({}, 50, -20, 10); d7["Hips"] = R(x=15); legs(d7, 95, 95, 120, 120); d7["hips"] = (0, 0, -0.5)
    d7["Spine"] = R(x=20)
    d8 = idle_arms({}, 60, 20, 20); legs(d8, 40, 40, 70, 70); d8["Spine"] = R(x=20); d8["hips"] = (0, 0, -0.15)
    C["knockdown"] = (52, False, [(0, d0), (3, d1), (9, d2), (15, d3), (19, d4), (23, d3), (34, d6), (40, d7), (46, d8),
                                  (52, d0)])

    # ---------------------------------------------------------------- hit: flinch back from a smack, then recover
    # (short, and retriggerable, so you can keep bopping someone H&R-style)
    h1 = idle_arms({}, 38, -32, 55); h1["Spine"] = R(x=-16, z=8); h1["Chest"] = R(x=-10, z=12); h1["Head"] = R(x=-26, z=-14)
    legs(h1, -8, 16, 10, 26, 0, -10); h1["hips"] = (0, 0.07, -0.035); hand_flop(h1, 35, 35)
    h2 = idle_arms({}, 62, -8, 30); h2["Spine"] = R(x=-5, z=3); h2["Chest"] = R(z=4); h2["Head"] = R(x=-6, z=8)
    legs(h2, -4, 8, 8, 14); h2["hips"] = (0, 0.035, -0.015); hand_flop(h2, 24, 24)
    C["hit"] = (16, False, [(0, idle_key(0)), (2, h1), (7, h2), (16, idle_key(0))])

    # ---------------------------------------------------------------- wave: big arm, floppy wrist, body sway
    def wave_key(f):
        ph = f / 24 * TAU
        p = idle_arms({}, 80, 4, 14)
        p["RightUpperArm"] = arm_rot("Right", -78, 12)
        p["RightLowerArm"] = elbow_rot("Right", 45 + 30 * math.sin(ph))
        p["RightHand"] = R(y=-18 * math.sin(ph - 0.8))
        p["LeftHand"] = R(y=14)
        p["Spine"] = R(z=4 * math.sin(ph))
        p["Chest"] = R(z=3 * math.sin(ph - 0.3))
        p["Head"] = R(x=-4, z=-6 * math.sin(ph - 0.5))
        p["hips"] = (0.01 * math.sin(ph), 0, -0.01)
        legs(p, 0, 0, 6, 6)
        return p
    C["wave"] = (24, True, sample(24, wave_key, 3))
    return C


def author_clips(arm, prefix):
    """Create one Blender action per clip. Rotations are world-axis at rest, converted
    into each bone's local frame: basis = B^-1 * Rw * B."""
    pbs = arm.pose.bones
    rest = {pb.name: pb.bone.matrix_local.to_3x3() for pb in pbs}
    for pb in pbs:
        pb.rotation_mode = 'QUATERNION'
    arm.animation_data_create()
    made = []
    for cname, (length, loop, keys) in clip_defs().items():
        act = bpy.data.actions.get(f"{prefix}|{cname}")
        if act:
            bpy.data.actions.remove(act)
        act = bpy.data.actions.new(f"{prefix}|{cname}")
        act.use_fake_user = True
        arm.animation_data.action = act
        for frame, pdict in keys:
            for pb in pbs:
                B = rest[pb.name]
                Rw = pdict.get(pb.name, Matrix.Identity(3))
                pb.rotation_quaternion = (B.inverted() @ Rw @ B).to_quaternion()
                pb.keyframe_insert("rotation_quaternion", frame=frame)
                if pb.name == "Hips":
                    off = Vector(pdict.get("hips", (0, 0, 0)))
                    pb.location = B.inverted() @ off
                    pb.keyframe_insert("location", frame=frame)
                else:
                    pb.location = (0, 0, 0)
        act["kl_loop"] = loop
        act.frame_range = (0, length)
        made.append(act.name)
    # "_tpose": a one-frame rest-pose take. It sorts first, and Unity builds the model's default
    # pose (and so the Humanoid avatar) from the first take's frame 0 - without it, that was
    # 'dismount', i.e. the crouched riding pose.
    act = bpy.data.actions.get(f"{prefix}|_tpose")
    if act:
        bpy.data.actions.remove(act)
    act = bpy.data.actions.new(f"{prefix}|_tpose")
    act.use_fake_user = True
    arm.animation_data.action = act
    for frame in (0, 1):
        for pb in pbs:
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            if pb.name == "Hips":
                pb.location = (0, 0, 0)
                pb.keyframe_insert("location", frame=frame)
    act["kl_loop"] = False
    act.frame_range = (0, 1)
    made.insert(0, act.name)
    # leave the rig in rest pose with no active action
    arm.animation_data.action = None
    for pb in pbs:
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
    return made
