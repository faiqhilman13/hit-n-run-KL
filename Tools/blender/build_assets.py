"""
Kampung Run: KL - procedural asset builder.

Builds Lat-style ("Kampung Boy") characters, cars and KL landmarks out of chunky
primitives and exports each one as an FBX into Assets/Models.

Run headless:
  blender -b --factory-startup -P Tools/blender/build_assets.py -- [--preview]

Conventions (the Unity side relies on these):
  * Everything faces -Y in Blender (Blender's "front" view).
  * Characters: root empty "<Name>" with children Torso, Head, ArmL, ArmR, LegL, LegR.
    Each child's origin sits on its joint so Unity can swing it procedurally.
  * Cars: root empty with children Body, Wheel_FL, Wheel_FR, Wheel_RL, Wheel_RR.
  * Materials are named "Lat_<Colour>" and carry a flat base colour; Unity swaps
    them for the LatInk shader while keeping the colour.
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Matrix, Vector, Euler

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(PROJECT, "Assets", "Models")
PREVIEW_DIR = os.path.join(PROJECT, "Tools", "previews")
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

# --------------------------------------------------------------------------------------
# Palette - soft, slightly faded tones like Lat's coloured books.
# --------------------------------------------------------------------------------------
PALETTE = {
    # Kampung Boy x Simpsons: bright, saturated, flat cartoon colours
    "Skin":        (0.87, 0.60, 0.38),
    "SkinDark":    (0.66, 0.44, 0.28),
    "Stubble":     (0.70, 0.54, 0.44),
    "Hair":        (0.08, 0.07, 0.10),
    "GreyHair":    (0.82, 0.82, 0.86),
    "Ink":         (0.05, 0.05, 0.07),
    "EyeWhite":    (1.00, 1.00, 1.00),
    "White":       (0.98, 0.97, 0.94),
    "Batik":       (0.98, 0.55, 0.12),
    "BatikDark":   (0.78, 0.22, 0.10),
    "BatikBlue":   (0.20, 0.45, 0.88),
    "Sarong":      (0.16, 0.58, 0.42),
    "SarongBand":  (0.08, 0.32, 0.22),
    "SarongRed":   (0.82, 0.18, 0.22),
    "Kurung":      (0.98, 0.42, 0.62),
    "Tudung":      (0.20, 0.50, 0.95),
    "Tshirt":      (0.95, 0.26, 0.20),
    "TshirtRed":   (0.98, 0.45, 0.10),
    "Shorts":      (0.18, 0.34, 0.86),
    "Pants":       (0.35, 0.30, 0.26),
    "Songkok":     (0.10, 0.08, 0.10),
    "Slipper":     (0.15, 0.62, 0.98),
    "Mouth":       (0.50, 0.08, 0.12),
    "Glass":       (0.45, 0.72, 0.92),
    "Tyre":        (0.12, 0.12, 0.14),
    "Hubcap":      (0.82, 0.84, 0.86),
    "Headlight":   (1.00, 0.97, 0.70),
    "Taillight":   (0.95, 0.18, 0.12),
    "ArchDark":    (0.10, 0.09, 0.12),
    "Plate":       (0.98, 0.98, 0.95),
    "CarBlue":     (0.20, 0.48, 0.90),
    "CarRed":      (0.95, 0.20, 0.16),
    "CarYellow":   (1.00, 0.82, 0.15),
    "CarGreen":    (0.25, 0.72, 0.35),
    "CarPink":     (0.98, 0.45, 0.70),
    "CarBlack":    (0.12, 0.12, 0.16),
    "PoliceBlue":  (0.12, 0.22, 0.62),
    "Wood":        (0.62, 0.38, 0.18),
    "WoodDark":    (0.40, 0.22, 0.10),
    "Zinc":        (0.65, 0.70, 0.75),
    "Attap":       (0.62, 0.45, 0.22),
    "RoofRed":     (0.86, 0.28, 0.18),
    "RoofBlue":    (0.22, 0.42, 0.80),
    "RoofGreen":   (0.18, 0.55, 0.35),
    "Plaster":     (0.98, 0.96, 0.88),
    "Pastel1":     (1.00, 0.82, 0.40),
    "Pastel2":     (0.45, 0.86, 0.72),
    "Pastel3":     (1.00, 0.58, 0.45),
    "Pastel4":     (0.68, 0.60, 0.98),
    "Pastel5":     (0.55, 0.78, 1.00),
    "Pastel6":     (1.00, 0.70, 0.85),
    "HouseBlue":   (0.35, 0.65, 0.95),
    "HouseGreen":  (0.45, 0.82, 0.45),
    "HouseYellow": (1.00, 0.85, 0.35),
    "HousePink":   (1.00, 0.62, 0.72),
    "Steel":       (0.78, 0.82, 0.88),
    "SteelDark":   (0.48, 0.52, 0.60),
    "Brick":       (0.85, 0.40, 0.25),
    "Dome":        (1.00, 0.98, 0.92),
    "Gold":        (1.00, 0.78, 0.20),
    "Leaf":        (0.35, 0.72, 0.25),
    "LeafDark":    (0.20, 0.52, 0.20),
    "Trunk":       (0.55, 0.38, 0.22),
    "Plastic":     (0.20, 0.55, 0.95),
    "PlasticRed":  (0.95, 0.25, 0.20),
    "Canvas":      (0.98, 0.94, 0.80),
    "Awning1":     (0.95, 0.30, 0.25),
    "Awning2":     (0.20, 0.62, 0.40),
    "Awning3":     (0.25, 0.45, 0.90),
    "Awning4":     (1.00, 0.72, 0.15),
    "Coconut":     (0.45, 0.62, 0.20),
    "Lamp":        (1.00, 0.94, 0.55),
    "Chicken":     (0.98, 0.95, 0.88),
    "Comb":        (0.95, 0.15, 0.15),
    "Beak":        (1.00, 0.70, 0.10),
    "Cat":         (0.98, 0.65, 0.25),
    "Water":       (0.30, 0.62, 0.90),
    "Satay":       (0.62, 0.30, 0.12),
    "Ember":       (1.00, 0.45, 0.10),
    "SkyBldg1":    (0.62, 0.72, 0.92),
    "SkyBldg2":    (0.72, 0.66, 0.90),
    "SkyBldg3":    (0.58, 0.80, 0.86),
    "SkyWindow":   (0.92, 0.96, 1.00),
}

_materials = {}


def mat(name):
    if name in _materials:
        return _materials[name]
    m = bpy.data.materials.get("Lat_" + name) or bpy.data.materials.new("Lat_" + name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    c = PALETTE[name]
    bsdf.inputs["Base Color"].default_value = (c[0], c[1], c[2], 1.0)
    bsdf.inputs["Roughness"].default_value = 1.0
    m.diffuse_color = (c[0], c[1], c[2], 1.0)
    _materials[name] = m
    return m


# --------------------------------------------------------------------------------------
# bmesh "piece" builder: every part is a list of primitives merged into one mesh.
# --------------------------------------------------------------------------------------
class Part:
    """Collects primitives into one mesh. Each primitive is built in its own temporary
    bmesh (so bevels only touch that primitive) and then appended."""

    def __init__(self):
        self.bm = bmesh.new()
        self.mats = []

    def _mi(self, matname):
        if matname not in self.mats:
            self.mats.append(matname)
        return self.mats.index(matname)

    def _commit(self, tmp, matname, smooth):
        mi = self._mi(matname)
        for f in tmp.faces:
            f.material_index = mi
            f.smooth = smooth
        me = bpy.data.meshes.new("_tmp")
        tmp.to_mesh(me)
        tmp.free()
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)

    @staticmethod
    def _matrix(loc, size=(1, 1, 1), rot=(0, 0, 0)):
        return (Matrix.Translation(Vector(loc)) @ Euler(rot).to_matrix().to_4x4()
                @ Matrix.Diagonal((size[0], size[1], size[2], 1.0)))

    def sphere(self, loc, size, matname, rot=(0, 0, 0), seg=14, rings=9):
        tmp = bmesh.new()
        bmesh.ops.create_uvsphere(tmp, u_segments=seg, v_segments=rings, radius=1.0,
                                  matrix=self._matrix(loc, size, rot))
        self._commit(tmp, matname, True)

    def box(self, loc, size, matname, rot=(0, 0, 0), bevel=0.0, taper=1.0):
        """size = full extents. taper < 1 shrinks the top face (cabins / roofs)."""
        tmp = bmesh.new()
        bmesh.ops.create_cube(tmp, size=1.0)
        for v in tmp.verts:
            if v.co.z > 0:
                v.co.x *= taper
                v.co.y *= taper
        bmesh.ops.transform(tmp, matrix=Matrix.Diagonal((size[0], size[1], size[2], 1.0)), verts=tmp.verts)
        if bevel > 0:
            bmesh.ops.bevel(tmp, geom=list(tmp.edges), offset=bevel, segments=2, affect='EDGES',
                            profile=0.5, clamp_overlap=True)
        bmesh.ops.transform(tmp, matrix=self._matrix(loc, rot=rot), verts=tmp.verts)
        self._commit(tmp, matname, bevel > 0)

    def cyl(self, loc, radius, depth, matname, rot=(0, 0, 0), seg=16, r2=None, smooth=True):
        tmp = bmesh.new()
        bmesh.ops.create_cone(tmp, cap_ends=True, cap_tris=False, segments=seg,
                              radius1=radius, radius2=radius if r2 is None else r2,
                              depth=depth, matrix=self._matrix(loc, rot=rot))
        self._commit(tmp, matname, smooth)

    def finish(self, name, origin, parent=None):
        bmesh.ops.translate(self.bm, vec=-Vector(origin), verts=self.bm.verts)
        me = bpy.data.meshes.new(name)
        self.bm.normal_update()
        self.bm.to_mesh(me)
        self.bm.free()
        for mn in self.mats:
            me.materials.append(mat(mn))
        ob = bpy.data.objects.new(name, me)
        ob.location = origin
        bpy.context.scene.collection.objects.link(ob)
        if parent:
            ob.parent = parent
        return ob


def empty(name, loc=(0, 0, 0)):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    e.empty_display_size = 0.3
    bpy.context.scene.collection.objects.link(e)
    return e


def clear_scene():
    # only this scene's objects: safe to run inside a live Blender session
    for ob in list(bpy.context.scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)


def export(root, filename):
    os.makedirs(OUT_DIR, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    for c in root.children_recursive:
        c.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = os.path.join(OUT_DIR, filename + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'EMPTY', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False,
        bake_anim=False, path_mode='AUTO')
    print("EXPORTED", path)


# --------------------------------------------------------------------------------------
# Characters
# --------------------------------------------------------------------------------------
def curly_hair(p, head_c, head_r, rng, matname="Hair", coverage=0.55, blob=0.30, mop=1.0):
    """Lat's trademark curly mop: two layers of little lumps over the top/back of the head
    (a dense inner layer for volume and a bumpy outer layer for the silhouette)."""
    n = int(64 * mop)
    for layer in (0, 1):
        for i in range(n):
            t = (i + 0.5) / n
            phi = math.acos(1 - t * (1 + coverage))  # polar angle from the top
            theta = i * 2.39996 + layer * 1.1 + rng.uniform(-0.25, 0.25)
            d = Vector((math.sin(phi) * math.cos(theta), math.sin(phi) * math.sin(theta), math.cos(phi)))
            # keep the face clear (front is -Y), leave a fringe line on the forehead
            if d.y < -0.3 and d.z < 0.62:
                continue
            lift = (1.0 if layer == 0 else 1.12) + rng.uniform(0, 0.06) * mop
            r = head_r * lift
            c = Vector(head_c) + d * r
            s = head_r * blob * (0.85 if layer == 0 else 0.62) * rng.uniform(0.8, 1.2)
            p.sphere(c, (s, s, s * 0.9), matname, seg=8, rings=6)


def scalloped_hair(p, head_c, head_r, rng, matname="Hair", radii=(1.08, 1.05, 0.9), lift=0.28, back=0.12,
                   bumps=18, bump=0.34, ring_elev=(0.15, 0.75), top_bumps=6):
    """Kampung Boy x Simpsons hair: ONE solid cartoon mass (so the outline reads as a
    single bold shape) with a scalloped, curly rim - Lat's mop drawn with Simpsons clarity."""
    hc = Vector(head_c) + Vector((0, head_r * back, head_r * lift))
    rx, ry, rz = radii[0] * head_r, radii[1] * head_r, radii[2] * head_r
    p.sphere(hc, (rx, ry, rz), matname, seg=24, rings=14)
    for i in range(bumps):
        a = i / bumps * math.tau + rng.uniform(-0.1, 0.1)
        d = Vector((math.cos(a), math.sin(a), 0))
        if d.y < -0.55:          # keep the face open
            continue
        el = rng.uniform(*ring_elev)
        pos = hc + Vector((d.x * rx * math.cos(el), d.y * ry * math.cos(el), rz * math.sin(el) - rz * 0.1))
        sz = head_r * bump * rng.uniform(0.85, 1.15)
        p.sphere(pos, (sz, sz, sz), matname, seg=12, rings=8)
    for i in range(top_bumps):
        a = i / max(top_bumps, 1) * math.tau
        pos = hc + Vector((math.cos(a) * rx * 0.45, math.sin(a) * ry * 0.45 + ry * 0.1, rz * 0.82))
        sz = head_r * bump * 0.95
        p.sphere(pos, (sz, sz, sz), matname, seg=12, rings=8)


def spiky_hair(p, head_c, head_r, matname="Hair", spikes=9):
    """Bart-style spiky crown."""
    for i in range(spikes):
        a = i / spikes * math.tau
        d = Vector((math.cos(a) * 0.62, math.sin(a) * 0.62 + 0.1, 0))
        base = Vector(head_c) + Vector((d.x * head_r, d.y * head_r, head_r * 0.62))
        tip_dir = Vector((d.x * 0.35, d.y * 0.35, 1)).normalized()
        rot = Vector((0, 0, 1)).rotation_difference(tip_dir).to_euler()
        p.cyl(base + tip_dir * head_r * 0.28, head_r * 0.24, head_r * 0.6, matname, rot=rot, r2=0.0, seg=10)
    p.sphere(Vector(head_c) + Vector((0, head_r * 0.08, head_r * 0.45)), (head_r * 0.92, head_r * 0.9, head_r * 0.45), matname)


def build_character(name, spec, seed=1):
    rng = random.Random(seed)
    s = spec.get("scale", 1.0)
    leg = spec.get("leg", 0.72) * s
    torso_h = spec.get("torso", 0.62) * s
    belly = spec.get("belly", 1.0)
    width = spec.get("width", 0.42) * s
    head_r = spec.get("head", 0.26) * s * 1.25
    skin = spec.get("skin", "Skin")
    shirt = spec.get("shirt", "Batik")
    lower = spec.get("lower", "Sarong")
    lower_kind = spec.get("lower_kind", "sarong")   # sarong | pants | shorts | kurung | dress
    hair = spec.get("hair", "curly")                # curly | songkok | tudung | tall_tudung | pigtails | spiky | cap | bald
    arm_len = spec.get("arm", 0.62) * s

    root = empty(name)
    hip_z = leg
    shoulder_z = hip_z + torso_h * 0.9
    neck_z = hip_z + torso_h
    head_z = neck_z + head_r * 1.02

    # --- Torso: a Simpsons-style rounded tube, belly bulging forward ------------------
    t = Part()
    tw = width * 0.5
    t.cyl((0, 0, hip_z + torso_h * 0.5), tw, torso_h * 0.8, shirt, seg=20)
    t.sphere((0, 0, hip_z + torso_h * 0.9), (tw, tw * 0.9, torso_h * 0.18), shirt, seg=20, rings=10)   # shoulders
    t.sphere((0, 0, hip_z + torso_h * 0.12), (tw, tw * 0.95, torso_h * 0.2), shirt, seg=20, rings=10)
    if belly > 1.05:
        t.sphere((0, -tw * 0.25, hip_z + torso_h * 0.36), (tw * belly, tw * belly * 0.95, torso_h * 0.36 * belly), shirt,
                 seg=20, rings=12)
    front_y = -tw * (belly + 0.25 if belly > 1.05 else 1.0)   # front of the belly
    t.cyl((0, 0, neck_z - 0.02), width * 0.13, torso_h * 0.2, skin, seg=12)                           # neck
    if spec.get("collar"):
        t.cyl((0, 0, neck_z - torso_h * 0.05), width * 0.2, 0.06, spec["collar"], seg=14)
    if shirt in ("Batik", "BatikBlue"):
        motif = "BatikDark" if shirt == "Batik" else "PoliceBlue"
        for i in range(20):
            a = rng.uniform(-2.4, 2.4)
            z = hip_z + torso_h * rng.uniform(0.2, 0.8)
            big = belly > 1.05 and abs(z - hip_z - torso_h * 0.36) < torso_h * 0.25
            r = tw * (belly if big else 1.0) * 0.97
            pnt = Vector((math.sin(a) * r, -math.cos(a) * r - (tw * 0.25 if big else 0), z))
            t.sphere(pnt, (0.038, 0.014, 0.038), motif, rot=(0, 0, a), seg=10, rings=6)
    if shirt == "CarBlack":  # Datuk's suit: white shirt V + red tie
        t.box((0, front_y * 0.98, hip_z + torso_h * 0.7), (width * 0.22, 0.02, torso_h * 0.35), "White")
        t.box((0, front_y * 1.0, hip_z + torso_h * 0.6), (width * 0.08, 0.03, torso_h * 0.4), "CarRed")
    if shirt == "PoliceBlue":  # badge + belt
        t.sphere((tw * 0.4, front_y * 0.97, hip_z + torso_h * 0.72), (0.05, 0.015, 0.06), "CarYellow")
        t.cyl((0, 0, hip_z + torso_h * 0.05), tw * max(belly * 0.9, 1.0) * 1.02, 0.06, "Ink", seg=20)
    # lower body
    if lower_kind in ("sarong", "kurung", "dress"):
        length = leg * (0.78 if lower_kind != "dress" else 0.45)
        flare = 1.15 if lower_kind == "dress" else 0.9
        t.cyl((0, 0, hip_z - length * 0.5 + 0.02), tw * 1.02, length, lower, r2=tw * flare, seg=20)
        if lower_kind == "sarong":
            for f in (0.45,):
                t.cyl((0, 0, hip_z - length * f), tw * 1.04 - f * tw * 0.1, leg * 0.05, "SarongBand", seg=20)
    else:
        t.sphere((0, 0, hip_z), (tw * 1.02, tw * 0.98, width * 0.22), lower, seg=18, rings=10)
    t.finish("Torso", (0, 0, hip_z), root)

    # --- Head: big googly Simpsons eyes + Lat's big round nose ---------------------------
    h = Part()
    hc = Vector((0, 0, head_z))
    h.sphere(hc, (head_r * 0.95, head_r * 0.92, head_r * 1.1), skin, seg=28, rings=18)
    if spec.get("stubble"):
        h.sphere(hc + Vector((0, -head_r * 0.48, -head_r * 0.52)), (head_r * 0.7, head_r * 0.5, head_r * 0.46), "Stubble",
                 seg=22, rings=14)
    for sx in (-1, 1):  # ears
        h.sphere(hc + Vector((sx * head_r * 0.93, 0.02, -head_r * 0.08)), (head_r * 0.12, head_r * 0.1, head_r * 0.2), skin)
    # eyes: big white balls touching, small black pupils
    er = head_r * 0.34
    for sx in (-1, 1):
        ec = hc + Vector((sx * head_r * 0.3, -head_r * 0.72, head_r * 0.22))
        h.sphere(ec, (er, er, er * 1.05), "EyeWhite", seg=18, rings=12)
        h.sphere(ec + Vector((-sx * er * 0.12, -er * 0.93, 0)), (er * 0.22, er * 0.1, er * 0.24), "Ink", seg=10, rings=6)
        if spec.get("sleepy"):
            h.sphere(ec + Vector((0, -er * 0.05, er * 0.42)), (er * 1.07, er * 1.07, er * 0.72), skin, seg=18, rings=10)
        if spec.get("lashes"):
            for k in (-1, 0, 1):
                h.box(ec + Vector((k * er * 0.35, -er * 0.6, er * 0.95)), (0.012, 0.012, er * 0.35), "Ink", rot=(0.4, k * 0.3, 0))
    # Lat's nose: a big round bulb hanging between and below the eyes
    nose = spec.get("nose", 1.0)
    h.sphere(hc + Vector((0, -head_r * 1.02, -head_r * 0.12)), (head_r * 0.24 * nose, head_r * 0.28 * nose, head_r * 0.22 * nose),
             skin, seg=18, rings=12)
    # mouth: a wide cartoon grin with an overbite
    mz = -head_r * 0.62
    # a U-shaped smile: dark mouth with upturned corners, teeth under the top lip
    h.sphere(hc + Vector((0, -head_r * 0.84, mz)), (head_r * 0.34, head_r * 0.12, head_r * 0.12), "Mouth", seg=16, rings=10)
    for sx in (-1, 1):
        h.sphere(hc + Vector((sx * head_r * 0.34, -head_r * 0.78, mz + head_r * 0.09)), (head_r * 0.09, head_r * 0.09, head_r * 0.07),
                 "Mouth", seg=10, rings=6)
    h.box(hc + Vector((0, -head_r * 0.93, mz + head_r * 0.07)), (head_r * 0.42, head_r * 0.05, head_r * 0.06), "EyeWhite")
    if spec.get("moustache"):
        mc = "GreyHair" if spec.get("grey") else "Hair"
        for sx in (-1, 1):
            h.sphere(hc + Vector((sx * head_r * 0.2, -head_r * 1.0, -head_r * 0.36)), (head_r * 0.26, head_r * 0.1, head_r * 0.09),
                     mc, rot=(0, sx * 0.3, 0), seg=12, rings=8)
    if spec.get("glasses"):
        lens = "Ink" if spec.get("shades") else "Glass"
        for sx in (-1, 1):
            ec = hc + Vector((sx * head_r * 0.3, -head_r * 0.72 - er * 0.95, head_r * 0.22))
            if spec.get("shades"):
                h.cyl(ec, er * 0.95, 0.03, lens, rot=(math.pi / 2, 0, 0), seg=18)
            else:  # thin rims only, so the big eyes still show
                for k in range(16):
                    ang = k / 16 * math.tau
                    h.sphere(ec + Vector((math.cos(ang) * er * 1.02, -0.01, math.sin(ang) * er * 1.02)), (0.018,) * 3, "Ink", seg=6, rings=4)
        h.box(hc + Vector((0, -head_r * 0.72 - er * 0.97, head_r * 0.26)), (head_r * 0.2, 0.02, 0.025), "Ink")

    hair_col = "GreyHair" if spec.get("grey") else "Hair"
    mop = spec.get("mop", 1.0)
    if hair == "curly":
        scalloped_hair(h, hc, head_r, rng, hair_col, bump=0.32 * mop, bumps=18, radii=(1.02 * mop, 1.0, 0.8 * mop),
                       lift=0.36 + 0.1 * (mop - 1), back=0.26)
    elif hair == "pigtails":
        scalloped_hair(h, hc, head_r, rng, hair_col, bump=0.26, bumps=14, radii=(1.02, 1.0, 0.9), lift=0.36, back=0.22)
        for sx in (-1, 1):  # two big curly puffs with ribbons
            pc = hc + Vector((sx * head_r * 1.12, head_r * 0.15, head_r * 0.3))
            h.sphere(pc, (head_r * 0.36, head_r * 0.34, head_r * 0.4), hair_col, seg=16, rings=10)
            h.sphere(pc + Vector((-sx * head_r * 0.28, 0, head_r * 0.1)), (head_r * 0.1, head_r * 0.1, head_r * 0.1), "EyeWhite")
    elif hair == "spiky":
        spiky_hair(h, hc, head_r, hair_col)
    elif hair == "songkok":
        for sx in (-1, 1):  # tufts of curly hair poking out at the sides
            for k in range(3):
                h.sphere(hc + Vector((sx * head_r * 0.85, head_r * (0.25 - k * 0.2), head_r * 0.32)), (head_r * 0.18,) * 3,
                         hair_col, seg=10, rings=6)
        h.cyl(hc + Vector((0, 0, head_r * 0.82)), head_r * 0.98, head_r * 0.6, "Songkok", r2=head_r * 0.9, seg=28)
    elif hair == "cap":
        h.cyl(hc + Vector((0, 0, head_r * 0.8)), head_r * 1.0, head_r * 0.45, "PoliceBlue", r2=head_r * 1.08, seg=28)
        h.box(hc + Vector((0, -head_r * 0.95, head_r * 0.62)), (head_r * 1.3, head_r * 0.6, 0.04), "Ink", rot=(0.25, 0, 0))
        h.sphere(hc + Vector((0, -head_r * 0.96, head_r * 0.92)), (0.06, 0.02, 0.06), "CarYellow")
    elif hair in ("tudung", "tall_tudung"):
        tud = spec.get("tudung", "Tudung")
        tall = 1.55 if hair == "tall_tudung" else 1.12   # Mak Som's is a proud Marge-height tower
        h.sphere(hc + Vector((0, head_r * 0.28, head_r * (0.12 + (tall - 1.1) * 0.9))),
                 (head_r * 1.1, head_r * 1.0, head_r * tall), tud, seg=28, rings=18)
        h.cyl(hc + Vector((0, head_r * 0.15, -head_r * 1.0)), head_r * 1.12, head_r * 0.75, tud, r2=head_r * 0.8, seg=24)
        h.sphere(hc + Vector((0, -head_r * 0.2, -head_r * 0.62)), (head_r * 0.2, head_r * 0.1, head_r * 0.2), "CarYellow")
    elif hair == "bald":
        for sx in (-1, 1):
            h.sphere(hc + Vector((sx * head_r * 0.85, head_r * 0.2, head_r * 0.15)), (head_r * 0.22, head_r * 0.35, head_r * 0.25),
                     hair_col, seg=12, rings=8)
    h.finish("Head", (0, 0, neck_z), root)

    # --- Arms: short sleeves, skinny tubes, round cartoon hands -------------------------
    for side, sx in (("L", 1), ("R", -1)):
        a = Part()
        ax = sx * (tw * max(belly * 0.9, 1.0) + width * 0.08)
        sleeve = spec.get("sleeve", shirt)
        long_sleeve = lower_kind == "kurung" or shirt == "CarBlack" or spec.get("long_sleeve")
        a.cyl((ax, 0, shoulder_z - arm_len * (0.4 if long_sleeve else 0.18)), width * 0.13,
              arm_len * (0.8 if long_sleeve else 0.36), sleeve, seg=14)
        a.cyl((ax, 0, shoulder_z - arm_len * 0.6), width * 0.075, arm_len * 0.62, skin, seg=12)
        a.sphere((ax, -0.01, shoulder_z - arm_len * 0.95), (width * 0.13, width * 0.1, width * 0.14), skin, seg=14, rings=10)
        a.sphere((ax - sx * width * 0.08, -width * 0.08, shoulder_z - arm_len * 0.9), (width * 0.05, width * 0.05, width * 0.07),
                 skin, seg=8, rings=6)
        a.finish("Arm" + side, (ax, 0, shoulder_z), root)

    # --- Legs + rounded cartoon shoes / slippers ------------------------------------------
    for side, sx in (("L", 1), ("R", -1)):
        l = Part()
        lx = sx * width * 0.22
        if lower_kind == "pants":
            l.cyl((lx, 0, hip_z - leg * 0.45), width * 0.14, leg * 0.85, lower, seg=12)
        elif lower_kind == "shorts":
            l.cyl((lx, 0, hip_z - leg * 0.18), width * 0.16, leg * 0.34, lower, seg=12)
            l.cyl((lx, 0, hip_z - leg * 0.6), width * 0.08, leg * 0.6, skin, seg=12)
        else:
            l.cyl((lx, 0, hip_z - leg * 0.75), width * 0.08, leg * 0.45, skin, seg=12)
        feet = spec.get("feet", "Slipper")
        if feet == "Slipper":
            l.box((lx, -0.05 * s, 0.025), (width * 0.26, width * 0.58, 0.05), feet, bevel=0.02)
            l.sphere((lx, -0.05 * s - width * 0.12, 0.07), (width * 0.12, width * 0.2, 0.05), skin, seg=12, rings=8)
            l.box((lx, -0.02 * s, 0.07), (width * 0.26, width * 0.06, 0.03), "Ink")
        else:  # rounded Simpsons shoes
            l.sphere((lx, -0.06 * s, 0.06), (width * 0.16, width * 0.3, 0.07), feet, seg=14, rings=8)
        l.finish("Leg" + side, (lx, 0, hip_z), root)

    return root


CHARACTERS = {
    # The family (Keluarga Pak Mat)
    "Char_PakMat": dict(shirt="Batik", lower="Sarong", lower_kind="sarong", hair="songkok", moustache=True, stubble=True,
                        sleepy=True, belly=1.45, width=0.5, nose=1.3, head=0.27, collar="Batik"),
    "Char_MakSom": dict(shirt="Kurung", lower="Kurung", lower_kind="kurung", hair="tall_tudung", sleeve="Kurung",
                        lashes=True, width=0.42, belly=1.1, nose=1.05, head=0.26),
    "Char_Along": dict(shirt="TshirtRed", lower="Shorts", lower_kind="shorts", hair="curly", mop=1.3,
                       scale=0.92, width=0.36, head=0.27, nose=1.1, sleeve="TshirtRed"),
    "Char_Adik": dict(shirt="Tshirt", lower="Tshirt", lower_kind="dress", hair="pigtails", lashes=True,
                      scale=0.68, width=0.34, head=0.3, nose=0.95, arm=0.58),
    # Townsfolk templates (recoloured at runtime in Unity)
    "Char_TownMan": dict(shirt="BatikBlue", lower="Pants", lower_kind="pants", hair="curly", mop=0.8, stubble=True,
                         width=0.40, nose=1.15, feet="Hair"),
    "Char_TownAunty": dict(shirt="Pastel3", lower="SarongRed", lower_kind="kurung", hair="tudung", lashes=True,
                           tudung="Pastel2", width=0.44, belly=1.3, nose=1.0),
    "Char_Pakcik": dict(shirt="White", lower="Sarong", lower_kind="sarong", hair="songkok", moustache=True, grey=True,
                        glasses=True, sleepy=True, width=0.38, nose=1.35),
    "Char_Kid": dict(shirt="White", lower="Shorts", lower_kind="shorts", hair="spiky",
                     scale=0.6, width=0.30, head=0.3),
    "Char_Polis": dict(shirt="PoliceBlue", lower="PoliceBlue", lower_kind="pants", hair="cap", stubble=True,
                       moustache=True, width=0.46, belly=1.25, feet="Hair", long_sleeve=True),
    # The villain: Datuk Mega of MegaMaju Berhad - huge belly, dark suit, shades
    "Char_DatukMega": dict(shirt="CarBlack", lower="CarBlack", lower_kind="pants", hair="songkok", moustache=True,
                           glasses=True, shades=True, stubble=True, belly=1.7, width=0.54, nose=1.45, head=0.28,
                           feet="Hair"),
}


# --------------------------------------------------------------------------------------
# Vehicles
# --------------------------------------------------------------------------------------
def build_car(name, kind, color):
    root = empty(name)
    b = Part()
    if kind == "hatch":  # Kancil-ish: tiny, tall, bubbly
        L, W, H = 3.3, 1.55, 0.62
        b.box((0, 0, 0.62), (W, L, H), color, bevel=0.14)
        b.box((0, 0.25, 1.22), (W * 0.92, L * 0.62, 0.62), color, bevel=0.12, taper=0.86)
        b.box((0, 0.25, 1.24), (W * 0.94, L * 0.5, 0.42), "Glass", taper=0.86)
        wheel_y, wheel_x, wr = 1.05, 0.72, 0.36
    elif kind == "sedan":  # Saga-ish
        L, W, H = 4.3, 1.7, 0.6
        b.box((0, 0, 0.62), (W, L, H), color, bevel=0.14)
        b.box((0, 0.15, 1.18), (W * 0.9, L * 0.48, 0.58), color, bevel=0.12, taper=0.84)
        b.box((0, 0.15, 1.2), (W * 0.92, L * 0.38, 0.4), "Glass", taper=0.84)
        wheel_y, wheel_x, wr = 1.35, 0.8, 0.38
    elif kind == "taxi":
        L, W, H = 4.3, 1.7, 0.6
        b.box((0, 0, 0.62), (W, L, H * 0.5), "White", bevel=0.1)
        b.box((0, 0, 0.8), (W * 1.01, L * 1.01, H * 0.45), color, bevel=0.1)
        b.box((0, 0.15, 1.18), (W * 0.9, L * 0.48, 0.58), "White", bevel=0.12, taper=0.84)
        b.box((0, 0.15, 1.2), (W * 0.92, L * 0.38, 0.4), "Glass", taper=0.84)
        b.box((0, 0.15, 1.55), (0.6, 0.25, 0.2), "CarYellow", bevel=0.04)  # TEKSI sign
        wheel_y, wheel_x, wr = 1.35, 0.8, 0.38
    elif kind == "bus":  # pink bas mini
        L, W, H = 7.5, 2.3, 2.3
        b.box((0, 0, 1.55), (W, L, H), color, bevel=0.22)
        b.box((0, -0.2, 1.95), (W * 1.01, L * 0.9, 0.7), "Glass")
        b.box((0, -L * 0.5, 1.85), (W * 0.85, 0.1, 0.9), "Glass")
        b.box((0, 0, 0.75), (W * 1.02, L * 1.0, 0.25), "White", bevel=0.05)  # stripe
        wheel_y, wheel_x, wr = 2.6, 1.05, 0.48
    elif kind == "van":
        L, W, H = 4.8, 1.9, 1.6
        b.box((0, 0, 1.25), (W, L, H), color, bevel=0.18)
        b.box((0, -L * 0.5 + 0.05, 1.6), (W * 0.85, 0.12, 0.6), "Glass")
        wheel_y, wheel_x, wr = 1.6, 0.88, 0.42
    elif kind == "police":
        L, W, H = 4.4, 1.75, 0.6
        b.box((0, 0, 0.62), (W, L, H), "White", bevel=0.14)
        b.box((0, 0, 0.72), (W * 1.01, L * 0.6, H * 0.3), color, bevel=0.04)
        b.box((0, 0.15, 1.18), (W * 0.9, L * 0.48, 0.58), "White", bevel=0.12, taper=0.84)
        b.box((0, 0.15, 1.2), (W * 0.92, L * 0.38, 0.4), "Glass", taper=0.84)
        b.box((-0.25, 0.15, 1.55), (0.45, 0.25, 0.18), "PoliceBlue", bevel=0.04)
        b.box((0.25, 0.15, 1.55), (0.45, 0.25, 0.18), "Taillight", bevel=0.04)
        wheel_y, wheel_x, wr = 1.4, 0.82, 0.38
    else:
        raise ValueError(kind)
    front = -L * 0.5
    # headlights (front = -Y) & taillights
    for sx in (-1, 1):
        b.sphere((sx * W * 0.32, front + 0.02, 0.72 if kind not in ("bus", "van") else 0.9),
                 (0.16, 0.08, 0.13), "Headlight", seg=10, rings=6)
        b.box((sx * W * 0.34, -front - 0.02, 0.75 if kind not in ("bus", "van") else 1.0), (0.3, 0.08, 0.14),
              "Taillight")
    # bumpers
    b.box((0, front - 0.05, 0.45), (W * 0.98, 0.18, 0.18), "SteelDark", bevel=0.05)
    b.box((0, -front + 0.05, 0.45), (W * 0.98, 0.18, 0.18), "SteelDark", bevel=0.05)
    big = kind in ("bus", "van")
    # wheel arches: dark recesses behind each tyre
    for sy in (-1, 1):
        b.cyl((0, sy * wheel_y, wr), wr * 1.22, W * 1.01, "ArchDark", rot=(0, math.pi / 2, 0), seg=18)
    # grille between the headlights + number plates front and back
    gz = 0.68 if not big else 0.95
    b.box((0, front - 0.01, gz), (W * 0.36, 0.06, 0.16), "ArchDark", bevel=0.02)
    for yy in (front - 0.15, -front + 0.15):
        b.box((0, yy, 0.45), (0.52, 0.03, 0.14), "Plate")
        for i in range(4):  # plate letters as ink ticks
            b.box((-0.18 + i * 0.12, yy - 0.02 * (1 if yy < 0 else -1), 0.45), (0.05, 0.01, 0.08), "Ink")
    if not big:
        # door seams, handles and side mirrors
        for sx in (-1, 1):
            x = sx * W * 0.505
            b.box((x, 0.05, 0.64), (0.01, 0.03, 0.5), "Ink")
            b.box((x, -L * 0.18, 0.62), (0.01, 0.03, 0.46), "Ink")
            b.box((x, -L * 0.05, 0.8), (0.03, 0.16, 0.04), "SteelDark")
            b.box((sx * W * 0.53, -L * 0.2, 1.02), (0.14, 0.08, 0.1), color if kind != "taxi" else "White", bevel=0.02)
    if kind == "bus":
        # window pillars, a folding door and the route board over the windscreen
        for i in range(7):
            b.box((W * 0.506, -L * 0.4 + i * L * 0.13, 1.95), (0.02, 0.1, 0.72), color)
            b.box((-W * 0.506, -L * 0.4 + i * L * 0.13, 1.95), (0.02, 0.1, 0.72), color)
        b.box((W * 0.51, -L * 0.36, 1.35), (0.02, 0.8, 1.9), "Glass")
        b.box((0, front - 0.02, 2.55), (W * 0.7, 0.06, 0.28), "CarYellow")
    if kind == "van":
        b.box((W * 0.505, 0.2, 1.3), (0.01, 0.03, 1.2), "Ink")  # sliding door seam
        b.cyl((W * 0.52, -0.6, 1.4), 0.32, 0.02, "CarYellow", rot=(0, math.pi / 2, 0), seg=16)  # MegaMaju logo
        b.cyl((-W * 0.52, -0.6, 1.4), 0.32, 0.02, "CarYellow", rot=(0, math.pi / 2, 0), seg=16)
    b.finish("Body", (0, 0, 0), root)
    for nm, sx, sy in (("Wheel_FL", -1, -1), ("Wheel_FR", 1, -1), ("Wheel_RL", -1, 1), ("Wheel_RR", 1, 1)):
        w = Part()
        c = (sx * wheel_x, sy * wheel_y, wr)
        w.cyl(c, wr, wr * 0.75, "Tyre", rot=(0, math.pi / 2, 0), seg=16)
        w.cyl((c[0] + sx * wr * 0.38, c[1], c[2]), wr * 0.5, 0.04, "Hubcap", rot=(0, math.pi / 2, 0), seg=12)
        w.finish(nm, c, root)
    return root


CARS = {
    "Car_Kancil": ("hatch", "CarYellow"),
    "Car_Saga": ("sedan", "CarBlue"),
    "Car_Kereta": ("sedan", "CarGreen"),
    "Car_Teksi": ("taxi", "CarRed"),
    "Car_BasMini": ("bus", "CarPink"),
    "Car_VanHitam": ("van", "CarBlack"),
    "Car_Polis": ("police", "PoliceBlue"),
}


# --------------------------------------------------------------------------------------
# Buildings & props (single mesh each, origin at ground centre)
# --------------------------------------------------------------------------------------
def single(name, build):
    root = empty(name)
    p = Part()
    build(p)
    p.finish("Mesh", (0, 0, 0), root)
    return root


def kl_tower(p):
    p.cyl((0, 0, 1.5), 9, 3, "Plaster", seg=20)
    p.cyl((0, 0, 40), 3.2, 76, "Plaster", r2=2.4, seg=16)
    p.sphere((0, 0, 80), (9, 9, 6.5), "Steel", seg=20, rings=12)
    p.cyl((0, 0, 80), 9.6, 1.2, "Glass", seg=20)
    p.cyl((0, 0, 86), 5, 3, "Plaster", r2=3, seg=16)
    p.cyl((0, 0, 100), 1.2, 26, "SteelDark", r2=0.15, seg=8)


def masjid(p):
    p.box((0, 0, 3.5), (22, 16, 7), "Brick")
    for x in (-7, 0, 7):
        p.box((x, -8.05, 2.4), (3.0, 0.2, 4.8), "Plaster")  # white arches
    p.box((0, 0, 7.4), (23, 17, 0.8), "Plaster")
    p.cyl((0, 0, 9.5), 4.5, 3.5, "Plaster", seg=20)
    p.sphere((0, 0, 11.5), (5.2, 5.2, 5.8), "Dome", seg=20, rings=12)
    p.cyl((0, 0, 18), 0.25, 2.5, "CarYellow", seg=6)
    for x, y in ((-9, -6), (9, -6), (-9, 6), (9, 6)):
        p.sphere((x, y, 9.6), (2.2, 2.2, 2.6), "Dome", seg=14, rings=8)
    for x in (-13, 13):
        p.cyl((x, -6, 8), 1.1, 16, "Brick", seg=12)
        p.cyl((x, -6, 13), 1.3, 0.8, "Plaster", seg=12)
        p.sphere((x, -6, 16.6), (1.4, 1.4, 1.8), "Dome", seg=12, rings=8)


def mamak_stall(p):
    p.box((0, 0, 0.08), (12, 9, 0.16), "Plaster")
    for x in (-5.6, 0, 5.6):
        for y in (-4.2, 4.2):
            p.cyl((x, y, 1.7), 0.12, 3.4, "Steel", seg=8)
    p.box((0, 0, 3.6), (12.6, 9.6, 0.5), "Zinc", taper=0.85)
    p.box((0, 3.6, 1.1), (7, 1.2, 2.0), "Plaster")  # counter / kitchen
    p.box((0, 3.6, 2.2), (7, 1.3, 0.2), "SteelDark")
    for i, (x, y) in enumerate(((-3.5, -2), (0, -2), (3.5, -2), (-3.5, 1), (3.5, 1))):
        c = "Plastic" if i % 2 == 0 else "PlasticRed"
        p.cyl((x, y, 0.75), 0.75, 0.08, c, seg=14)
        p.cyl((x, y, 0.4), 0.06, 0.7, c, seg=6)
        for a in range(4):
            ang = a * math.pi / 2 + 0.4
            p.cyl((x + math.cos(ang) * 1.1, y + math.sin(ang) * 1.1, 0.25), 0.25, 0.5,
                  "PlasticRed" if c == "Plastic" else "Plastic", seg=10, r2=0.2)


def palm(p, rng):
    h = rng.uniform(7, 10)
    lean = rng.uniform(-0.25, 0.25)
    segs = 6
    top = Vector((0, 0, 0))
    for i in range(segs):
        z0 = h * i / segs
        top = Vector((math.sin(lean) * z0 * 0.4, 0, z0 + h / segs / 2))
        p.cyl(top, 0.28 - i * 0.02, h / segs + 0.05, "Trunk", seg=8)
    crown = Vector((math.sin(lean) * h * 0.4, 0, h))
    for i in range(8):
        a = i * math.pi * 2 / 8 + rng.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), 0))
        p.sphere(crown + d * 1.8 + Vector((0, 0, -0.5)), (2.3, 0.45, 0.12), "Leaf" if i % 2 else "LeafDark",
                 rot=(0, 0.45, a), seg=10, rings=5)
    for i in range(3):
        a = i * 2.1
        p.sphere(crown + Vector((math.cos(a) * 0.35, math.sin(a) * 0.35, -0.4)), (0.25, 0.25, 0.28), "Coconut",
                 seg=8, rings=6)


def rain_tree(p, rng):
    h = rng.uniform(4.5, 6)
    p.cyl((0, 0, h / 2), 0.45, h, "Trunk", r2=0.3, seg=10)
    for i in range(7):
        a = i * 0.9
        r = rng.uniform(1.6, 2.8)
        p.sphere((math.cos(a) * r, math.sin(a) * r, h + rng.uniform(-0.3, 1.2)),
                 (2.4, 2.4, 1.7), "Leaf" if i % 2 else "LeafDark", seg=12, rings=8)


def banana(p, rng):
    p.cyl((0, 0, 1.3), 0.2, 2.6, "Coconut", seg=8)
    for i in range(6):
        a = i * math.pi / 3
        p.sphere((math.cos(a) * 1.0, math.sin(a) * 1.0, 2.7), (1.3, 0.35, 0.08), "Leaf", rot=(0, -0.6, a),
                 seg=10, rings=5)


def street_lamp(p):
    p.cyl((0, 0, 3), 0.1, 6, "SteelDark", seg=8)
    p.box((0, -0.8, 6), (0.12, 1.7, 0.12), "SteelDark")
    p.sphere((0, -1.6, 5.9), (0.3, 0.45, 0.18), "Lamp", seg=10, rings=6)


def crate(p):
    p.box((0, 0, 0.45), (0.9, 0.9, 0.9), "Wood", bevel=0.03)
    for z in (0.15, 0.75):
        p.box((0, 0, z), (0.94, 0.94, 0.1), "WoodDark")


def pasar_canopy(p):
    for x in (-1.4, 1.4):
        for y in (-1.4, 1.4):
            p.cyl((x, y, 1.2), 0.05, 2.4, "Steel", seg=6)
    p.box((0, 0, 2.6), (3.2, 3.2, 0.6), "Canvas", taper=0.2)
    p.box((0, 0, 0.85), (2.6, 1.3, 0.1), "WoodDark")
    for i in range(5):
        p.sphere((-1 + i * 0.5, 0, 1.0), (0.18, 0.18, 0.12), ["CarRed", "CarYellow", "Leaf", "Pastel3", "Coconut"][i],
                 seg=8, rings=5)


def surau(p):
    p.box((0, 0, 1.0), (8, 8, 2.0), "WoodDark")
    p.box((0, 0, 3.0), (7.6, 7.6, 2.4), "White")
    p.box((0, 0, 5.2), (8.6, 8.6, 2.0), "CarGreen", taper=0.1)
    p.sphere((0, 0, 6.5), (0.8, 0.8, 1.0), "CarYellow", seg=10, rings=6)


def coin(p):
    p.cyl((0, 0, 0), 0.35, 0.08, "CarYellow", rot=(math.pi / 2, 0, 0), seg=16)
    p.cyl((0, 0, 0), 0.24, 0.1, "Batik", rot=(math.pi / 2, 0, 0), seg=16)


def teh_tarik(p):
    p.cyl((0, 0, 0.3), 0.22, 0.55, "Glass", r2=0.28, seg=14)
    p.cyl((0, 0, 0.42), 0.26, 0.28, "Batik", r2=0.28, seg=14)
    p.cyl((0, 0, 0.6), 0.28, 0.06, "White", seg=14)


def bungkus(p):
    # nasi lemak bungkus: a little paper-wrapped pyramid
    p.box((0, 0, 0.25), (0.6, 0.6, 0.5), "Leaf", taper=0.35, bevel=0.04)
    p.box((0, 0, 0.3), (0.63, 0.63, 0.1), "Canvas")


def barrier(p):
    p.box((0, 0, 0.5), (4, 0.5, 1.0), "White", bevel=0.05)
    for x in (-1.5, -0.5, 0.5, 1.5):
        p.box((x, -0.26, 0.5), (0.45, 0.04, 0.9), "CarRed", rot=(0, 0.6, 0))


def skyline_card(p, rng):
    # distant flat "cut-out" buildings for the horizon, like a background drawing
    x = -60.0
    while x < 60:
        w = rng.uniform(6, 14)
        h = rng.uniform(15, 55)
        col = rng.choice(["SkyBldg1", "SkyBldg2", "SkyBldg3"])
        p.box((x + w / 2, 0, h / 2), (w, 3, h), col)
        # rows of little windows facing the city
        for z in range(3, int(h) - 2, 4):
            p.box((x + w / 2, -1.55, z), (w * 0.8, 0.1, 1.2), "SkyWindow")
        if rng.random() < 0.3:
            p.cyl((x + w / 2, 0, h + 3), 0.2, 6, "SteelDark", seg=6)
        x += w + rng.uniform(0, 2)


def burung_kamera(p):
    # robot mynah: round black body, yellow beak, big camera-lens eye, little propeller
    p.sphere((0, 0, 0), (0.45, 0.55, 0.4), "CarBlack", seg=12, rings=8)
    p.sphere((0, -0.5, 0.25), (0.28, 0.28, 0.28), "CarBlack", seg=10, rings=6)
    p.box((0, -0.8, 0.22), (0.14, 0.3, 0.1), "CarYellow", taper=0.4)
    p.cyl((0, -0.7, 0.35), 0.12, 0.12, "Glass", rot=(math.pi / 2, 0, 0), seg=12)
    p.cyl((0, -0.77, 0.35), 0.06, 0.04, "Taillight", rot=(math.pi / 2, 0, 0), seg=10)
    for sx in (-1, 1):
        p.sphere((sx * 0.45, 0.05, 0.05), (0.08, 0.35, 0.22), "SteelDark", rot=(0, sx * 0.4, 0), seg=8, rings=5)
    p.cyl((0, 0, 0.5), 0.03, 0.25, "Steel", seg=6)
    p.box((0, 0, 0.64), (0.9, 0.08, 0.03), "Steel")


def cendol_crate(p):
    p.box((0, 0, 0.45), (0.9, 0.9, 0.9), "CarGreen", bevel=0.03)
    p.box((0, -0.46, 0.5), (0.6, 0.02, 0.35), "White")
    p.cyl((0, -0.47, 0.5), 0.12, 0.02, "CarGreen", rot=(math.pi / 2, 0, 0), seg=10)


def phone_booth(p):
    p.box((0, 0, 1.2), (1.1, 1.1, 2.4), "CarYellow", bevel=0.04)
    p.box((0, -0.56, 1.35), (0.8, 0.04, 1.4), "Glass")
    p.box((0, 0, 2.5), (1.2, 1.2, 0.2), "CarRed")
    p.box((0, 0.3, 1.4), (0.3, 0.2, 0.45), "SteelDark")


def kad_lat(p):
    p.box((0, 0, 0), (0.9, 0.06, 1.2), "White", bevel=0.02)
    p.box((0, -0.035, 0), (0.75, 0.02, 1.05), "CarRed")
    p.sphere((0, -0.05, 0.15), (0.2, 0.05, 0.22), "Skin", seg=10, rings=6)
    p.sphere((0, -0.05, 0.38), (0.24, 0.05, 0.12), "Hair", seg=10, rings=6)


def sultan_abdul_samad(p):
    # long colonial-Moorish front: brick with white bands, arches, copper domes, clock tower
    p.box((0, 0, 5), (40, 12, 10), "Brick")
    for z in (3.5, 7.0):
        p.box((0, 0, z), (40.4, 12.4, 0.4), "Plaster")
    for i in range(9):
        x = -16 + i * 4
        p.box((x, -6.1, 2.2), (2.0, 0.2, 3.6), "WoodDark")
        p.box((x, -6.1, 5.6), (1.6, 0.2, 2.2), "WoodDark")
    p.box((0, 0, 10.4), (41, 13, 0.8), "Plaster")
    # clock tower
    p.box((0, -3, 15), (6, 6, 10), "Brick")
    p.box((0, -6.05, 16), (3, 0.2, 3), "White")
    p.cyl((0, -6.1, 16), 1.2, 0.1, "Ink", rot=(math.pi / 2, 0, 0), seg=16)
    p.cyl((0, -3, 20.8), 3.6, 1.6, "Plaster", seg=16)
    p.sphere((0, -3, 22.5), (3.2, 3.2, 3.8), "CarGreen", seg=16, rings=10)
    p.cyl((0, -3, 27), 0.15, 2.5, "CarYellow", seg=6)
    for x in (-17, 17):
        p.cyl((x, -3, 12), 2.2, 3, "Brick", seg=12)
        p.sphere((x, -3, 14.2), (2.2, 2.2, 2.6), "CarGreen", seg=12, rings=8)


def flagpole(p):
    p.cyl((0, 0, 0.4), 2.5, 0.8, "Plaster", seg=16)
    p.cyl((0, 0, 20), 0.18, 40, "Steel", r2=0.1, seg=8)
    # Jalur Gemilang, simplified: red/white stripes + blue canton
    for i in range(6):
        p.box((1.8, 0, 37.6 - i * 0.4), (3.2, 0.05, 0.4), "CarRed" if i % 2 == 0 else "White")
    p.box((0.9, -0.03, 37.0), (1.4, 0.05, 1.4), "PoliceBlue")
    p.sphere((0.8, -0.07, 37.0), (0.35, 0.02, 0.35), "CarYellow", seg=10, rings=6)


def chinatown_gate(p):
    for x in (-6.5, 6.5):
        p.cyl((x, 0, 3), 0.45, 6, "CarRed", seg=12)
        p.box((x, 0, 0.3), (1.4, 1.4, 0.6), "Plaster")
    p.box((0, 0, 6.3), (15, 1.2, 0.8), "CarRed")
    p.box((0, 0, 7.4), (8, 0.3, 1.4), "CarYellow")
    p.box((0, 0, 8.4), (17, 3.0, 0.9), "CarGreen", taper=0.7)
    p.box((0, 0, 9.2), (9, 2.0, 0.8), "CarGreen", taper=0.6)
    for x in (-3, 3):
        p.sphere((x, -0.8, 6.0), (0.45, 0.45, 0.6), "CarRed", seg=10, rings=6)  # lanterns


def pasar_seni(p):
    # Central Market: pale blue art-deco hall
    p.box((0, 0, 5), (30, 18, 10), "BatikBlue")
    p.box((0, 0, 10.4), (30.4, 18.4, 0.8), "White")
    p.box((0, -9.1, 7), (8, 0.3, 6), "White")
    p.box((0, -9.2, 7.5), (6, 0.2, 3.5), "Glass")
    for x in (-11, -6, 6, 11):
        p.box((x, -9.1, 5), (1.2, 0.3, 10), "White")
    p.box((0, -9.1, 1.6), (4, 0.3, 3.2), "WoodDark")


def prism(p, loc, size, matname, rot=(0, 0, 0)):
    """Triangular prism (gable roof): ridge along X, size = (length, depth, height)."""
    tmp = bmesh.new()
    L, D, H = size[0] / 2, size[1] / 2, size[2]
    pts = []
    for x in (-L, L):
        pts.append(tmp.verts.new((x, -D, 0)))
        pts.append(tmp.verts.new((x, D, 0)))
        pts.append(tmp.verts.new((x, 0, H)))
    a0, a1, a2, b0, b1, b2 = pts
    for f in ((a0, a2, a1), (b0, b1, b2), (a0, a1, b1, b0), (a1, a2, b2, b1), (a2, a0, b0, b2)):
        tmp.faces.new(f)
    bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
    bmesh.ops.transform(tmp, matrix=Part._matrix(loc, rot=rot), verts=tmp.verts)
    p._commit(tmp, matname, False)


def kampung_house(p, wall="HouseBlue", roof="RoofRed", trim="White"):
    """Rumah kampung: painted timber walls on stilts, a big gable roof with carved
    'tebar layar' gable boards, open shutters, a front serambi with railings and pots."""
    for x in (-3.2, -1.1, 1.1, 3.2):                       # stilts on little concrete pads
        for y in (-2.4, 0, 2.4):
            p.cyl((x, y, 0.8), 0.14, 1.6, "WoodDark", seg=8)
            p.box((x, y, 0.08), (0.45, 0.45, 0.16), "Plaster")
    p.box((0, 0, 1.75), (7.2, 5.6, 0.3), "WoodDark")        # floor platform
    p.box((0, 0, 3.1), (6.8, 5.2, 2.4), wall)               # walls
    for z in (1.95, 4.25):                                  # trim bands
        p.box((0, 0, z), (6.9, 5.3, 0.14), trim)
    for x in (-3.4, 3.4):                                    # corner posts
        for y in (-2.6, 2.6):
            p.box((x, y, 3.1), (0.18, 0.18, 2.4), trim)
    # windows: dark opening, white frame, two shutters swung open
    def window(cx, cy, face_y, sideways=False):
        size = (1.0, 0.08, 1.15) if not sideways else (0.08, 1.0, 1.15)
        frame = (1.25, 0.1, 1.4) if not sideways else (0.1, 1.25, 1.4)
        p.box((cx, cy, 3.2), frame, trim)
        p.box((cx, cy + (face_y * 0.02 if not sideways else 0), 3.2), size, "WoodDark")
        for s in (-1, 1):
            if not sideways:
                p.box((cx + s * 0.85, cy + face_y * 0.15, 3.2), (0.5, 0.06, 1.15), roof, rot=(0, 0, s * 0.5))
            else:
                p.box((cx + face_y * 0.15, cy + s * 0.85, 3.2), (0.06, 0.5, 1.15), roof, rot=(0, 0, s * 0.5))
    for x in (-2.2, 2.2):
        window(x, -2.62, -1)
    window(0, 2.62, 1)
    for y in (-1.2, 1.2):
        window(3.42, y, 1, True)
        window(-3.42, y, -1, True)
    p.box((0, -2.64, 3.0), (1.0, 0.08, 2.0), "Wood")        # front door
    # the big gable roof with overhang + ridge beam
    prism(p, (0, 0, 4.3), (8.6, 7.0, 2.6), roof)
    p.box((0, 0, 6.92), (8.8, 0.25, 0.22), "WoodDark")
    # tebar layar: carved gable boards at both ends (white panel + sunburst slats)
    for sx in (-1, 1):
        x = sx * 4.28
        prism(p, (x, 0, 4.35), (0.06, 6.2, 2.35), trim)
        for k in range(7):
            ang = -0.9 + k * 0.3
            p.box((x + sx * 0.04, math.sin(ang) * 1.1, 5.2 + math.cos(ang) * 0.6), (0.03, 0.08, 1.2), "Wood", rot=(ang, 0, 0))
    # serambi (front verandah) with a lower roof, railings and stairs
    p.box((0, -3.9, 1.75), (4.2, 2.4, 0.25), "WoodDark")
    prism(p, (0, -3.9, 3.9), (4.8, 3.0, 1.1), roof, rot=(0, 0, 0))
    for x in (-1.9, 1.9):
        p.cyl((x, -4.9, 2.9), 0.1, 2.2, trim, seg=8)
    for k in range(9):
        x = -2.0 + k * 0.5
        if abs(x) < 0.6:
            continue
        p.box((x, -5.0, 2.25), (0.07, 0.07, 0.7), trim)
    p.box((-1.3, -5.0, 2.6), (1.5, 0.1, 0.08), trim)
    p.box((1.3, -5.0, 2.6), (1.5, 0.1, 0.08), trim)
    for i in range(4):
        p.box((0, -5.5 - i * 0.35, 1.45 - i * 0.4), (1.2, 0.35, 0.12), "Wood")
    for x in (-1.6, 1.6):                                   # flower pots
        p.cyl((x, -5.6, 0.2), 0.25, 0.4, "Brick", r2=0.3, seg=10)
        p.sphere((x, -5.6, 0.6), (0.35, 0.35, 0.3), "Leaf", seg=10, rings=6)
    # tempayan (water jar) by the stairs
    p.sphere((1.2, -6.4, 0.35), (0.35, 0.35, 0.38), "Brick", seg=12, rings=8)


def shophouse(p, colors, awnings=("Awning1", "Awning2", "Awning3")):
    """A row of three pre-war shophouses: coloured facades, shuttered windows,
    decorative parapets, signboards, striped awnings, half-open roller shutters."""
    for i, c in enumerate(colors):
        x = (i - 1) * 5.0
        p.box((x, 0, 4.0), (4.9, 10.0, 8.0), c)
        p.box((x, 0.5, 8.3), (5.0, 9.0, 0.6), "RoofRed", taper=0.9)
        prism(p, (x, 0.5, 8.55), (5.0, 9.0, 1.2), "RoofRed")
        # parapet + little pediment on the facade
        p.box((x, -5.02, 8.25), (4.9, 0.2, 0.7), "Plaster")
        prism(p, (x, -5.05, 8.6), (1.8, 0.2, 0.7), "Plaster", rot=(math.pi / 2, 0, 0))
        # upper floor: three tall shuttered windows with white frames
        for wx in (-1.5, 0, 1.5):
            p.box((x + wx, -5.05, 5.6), (1.0, 0.12, 1.9), "White")
            p.box((x + wx, -5.1, 5.6), (0.8, 0.1, 1.7), awnings[i % len(awnings)])
            p.box((x + wx, -5.16, 5.6), (0.05, 0.05, 1.7), "White")
            p.box((x + wx, -5.18, 4.62), (0.95, 0.25, 0.1), "White")     # sill
        # aircon unit
        p.box((x + 1.7, -5.2, 7.25), (0.8, 0.35, 0.5), "Steel", bevel=0.03)
        # ground floor: dark shop interior with a half-open ribbed roller shutter
        p.box((x, -5.02, 1.4), (3.8, 0.12, 2.8), "WoodDark")
        p.box((x, -5.08, 2.35), (3.8, 0.12, 0.9), "Steel")
        for k in range(5):
            p.box((x, -5.15, 1.95 + k * 0.18), (3.8, 0.05, 0.03), "SteelDark")
        p.box((x - 1.2, -5.3, 0.45), (0.9, 0.5, 0.9), "Canvas")          # goods on display
        p.sphere((x - 1.2, -5.3, 1.0), (0.4, 0.25, 0.2), "CarRed", seg=8, rings=5)
        # signboard (text added in Unity) + striped awning
        p.box((x, -5.3, 3.35), (4.6, 0.3, 0.75), "White")
        p.box((x, -5.36, 3.35), (4.4, 0.2, 0.62), awnings[(i + 1) % len(awnings)])
        for k in range(6):
            col = awnings[i % len(awnings)] if k % 2 == 0 else "White"
            p.box((x - 2.1 + k * 0.84, -7.2, 2.7), (0.84, 1.6, 0.08), col, rot=(-0.4, 0, 0))
        # five-foot way arch pillars
        p.box((x - 2.4, -5.9, 1.4), (0.4, 0.4, 2.8), "Plaster")
    p.box((7.4, -5.9, 1.4), (0.4, 0.4, 2.8), "Plaster")
    p.box((0, -5.9, 2.85), (15.0, 1.9, 0.15), "Plaster")
    # the back lane side: small windows, drain pipes, water tanks, back doors
    for i in range(3):
        x = (i - 1) * 5.0
        for wx in (-1.2, 1.2):
            for z in (2.0, 5.6):
                p.box((x + wx, 5.05, z), (0.9, 0.1, 1.1), "Glass")
                p.box((x + wx, 5.08, z - 0.6), (1.0, 0.15, 0.1), "White")
        p.cyl((x + 2.2, 5.1, 4.0), 0.08, 8.0, "SteelDark", seg=6)
        p.box((x, 5.04, 1.1), (1.0, 0.1, 2.2), "WoodDark")
        p.cyl((x - 1.5, 3.5, 9.3), 0.6, 1.1, "Steel", seg=12)


def condo(p, rng, color):
    """Mid-rise flats: coloured slab, window bands, little balconies, rooftop water tank."""
    floors = rng.randint(8, 15)
    w, d = rng.uniform(13, 17), rng.uniform(12, 16)
    h = floors * 3.2
    p.box((0, 0, h / 2), (w, d, h), color)
    for f in range(1, floors + 1):
        z = f * 3.2 - 1.6
        for sy in (-1, 1):
            p.box((0, sy * d / 2, z + 0.2), (w * 0.86, 0.1, 1.3), "Glass")
        for sx in (-1, 1):
            p.box((sx * w / 2, 0, z + 0.2), (0.1, d * 0.8, 1.3), "Glass")
        p.box((0, 0, f * 3.2), (w + 0.4, d + 0.4, 0.22), "Plaster")
        for k in range(3):                                       # balconies
            bx = -w * 0.3 + k * w * 0.3
            p.box((bx, -d / 2 - 0.6, z - 0.9), (2.4, 1.2, 0.15), "Plaster")
            p.box((bx, -d / 2 - 1.15, z - 0.45), (2.4, 0.08, 0.8), "White")
            if rng.random() < 0.4:                                # laundry!
                p.box((bx + rng.uniform(-0.6, 0.6), -d / 2 - 1.2, z - 0.35), (0.5, 0.04, 0.6),
                      rng.choice(["CarRed", "Pastel5", "CarYellow", "Pastel6"]))
    p.cyl((w * 0.25, d * 0.2, h + 1.4), 1.3, 2.4, "Steel", seg=16)       # water tank
    p.box((w * 0.25, d * 0.2, h + 0.1), (2.8, 2.8, 0.2), "SteelDark")
    p.box((-w * 0.2, -d * 0.15, h + 1.0), (3.5, 3.5, 2.0), "Plaster")


def twin_towers(p):
    """Menara Kembar: stepped silver towers with window bands, pinnacles and the skybridge."""
    def tower(cx):
        z = 0.0
        tiers = [(9.0, 40), (8.3, 28), (7.4, 18), (6.4, 12), (5.2, 9), (4.0, 7), (2.8, 5)]
        for r, h in tiers:
            p.cyl((cx, 0, z + h / 2), r, h, "Steel", seg=24)
            for k in range(int(h / 3.5)):
                p.cyl((cx, 0, z + 1.5 + k * 3.5), r * 1.01, 0.9, "Glass", seg=24)
            for a in range(8):                                     # vertical fins
                ang = a / 8 * math.tau
                p.box((cx + math.cos(ang) * r, math.sin(ang) * r, z + h / 2), (0.5, 0.5, h), "SteelDark",
                      rot=(0, 0, ang))
            p.cyl((cx, 0, z + h - 0.3), r * 1.05, 0.6, "SteelDark", seg=24)
            z += h
        for k, (rr, hh) in enumerate([(1.6, 3), (1.1, 3), (0.7, 3)]):
            p.cyl((cx, 0, z + hh / 2), rr, hh, "Steel", seg=16)
            p.sphere((cx, 0, z + hh), (rr * 1.3,) * 3, "Steel", seg=12, rings=8)
            z += hh
        p.cyl((cx, 0, z + 9), 0.35, 18, "Steel", r2=0.05, seg=8)
    tower(-14)
    tower(14)
    p.box((0, 0, 58), (20, 3.2, 3.2), "SteelDark", bevel=0.2)
    p.box((0, 0, 58), (20.2, 3.3, 1.2), "Glass")
    for sx in (-1, 1):
        p.cyl((sx * 5, 0, 50), 0.4, 18, "SteelDark", rot=(0, sx * 0.55, 0), seg=6)
    p.box((0, 6, 6), (50, 28, 12), "Plaster", bevel=0.4)
    p.box((0, -8.1, 3), (30, 0.3, 5), "Glass")
    for x in range(-22, 23, 4):
        p.box((x, -8.2, 6), (0.4, 0.3, 12), "Plaster")


# ---------------------------------------------------------------------- street life props
def chicken(p):
    p.sphere((0, 0.05, 0.3), (0.2, 0.26, 0.2), "Chicken", seg=12, rings=8)
    p.sphere((0, -0.2, 0.52), (0.11, 0.11, 0.12), "Chicken", seg=10, rings=6)
    p.box((0, -0.3, 0.52), (0.06, 0.08, 0.04), "Beak", taper=0.4)
    p.box((0, -0.2, 0.66), (0.03, 0.12, 0.08), "Comb")
    p.sphere((0, 0.3, 0.42), (0.1, 0.12, 0.16), "Chicken", rot=(-0.5, 0, 0), seg=8, rings=6)
    for sx in (-1, 1):
        p.sphere((sx * 0.06, -0.28, 0.55), (0.025, 0.02, 0.025), "Ink", seg=6, rings=4)
        p.cyl((sx * 0.07, 0.05, 0.08), 0.02, 0.16, "Beak", seg=6)


def cat(p):
    p.sphere((0, 0.05, 0.22), (0.14, 0.3, 0.14), "Cat", seg=12, rings=8)
    p.sphere((0, -0.3, 0.34), (0.13, 0.12, 0.12), "Cat", seg=12, rings=8)
    for sx in (-1, 1):
        p.box((sx * 0.07, -0.3, 0.47), (0.06, 0.04, 0.1), "Cat", taper=0.1)
        p.sphere((sx * 0.05, -0.41, 0.36), (0.03, 0.02, 0.035), "Ink", seg=6, rings=4)
        for sy in (-0.15, 0.2):
            p.cyl((sx * 0.07, sy, 0.08), 0.035, 0.16, "Cat", seg=6)
    p.cyl((0, 0.45, 0.35), 0.03, 0.4, "Cat", rot=(0.6, 0, 0), seg=6)


def kapcai(p, color="CarRed"):
    """Parked kapcai (underbone motorbike), Malaysia's national vehicle."""
    for y in (-0.6, 0.6):
        p.cyl((0, y, 0.3), 0.3, 0.1, "Tyre", rot=(0, math.pi / 2, 0), seg=16)
        p.cyl((0, y, 0.3), 0.14, 0.12, "Hubcap", rot=(0, math.pi / 2, 0), seg=10)
    p.box((0, 0.1, 0.55), (0.3, 0.9, 0.35), color, bevel=0.06)
    p.box((0, 0.35, 0.8), (0.28, 0.6, 0.12), "Ink", bevel=0.03)          # seat
    p.box((0, -0.5, 0.75), (0.25, 0.35, 0.5), color, bevel=0.05)         # front fairing
    p.box((0, -0.62, 1.05), (0.7, 0.05, 0.05), "SteelDark")              # handlebar
    p.sphere((0, -0.72, 0.85), (0.08, 0.05, 0.08), "Headlight", seg=8, rings=6)
    p.box((0, 0.75, 0.75), (0.35, 0.3, 0.25), "Plastic", bevel=0.03)     # delivery box


def bus_stop(p):
    p.box((0, 0, 0.05), (4.2, 1.8, 0.1), "Plaster")
    for x in (-1.9, 1.9):
        p.cyl((x, 0.6, 1.3), 0.06, 2.6, "SteelDark", seg=8)
    p.box((0, 0.7, 1.3), (4.0, 0.06, 2.0), "Glass")
    p.box((0, 0.1, 2.65), (4.4, 1.9, 0.12), "Awning3", bevel=0.04)
    p.box((0, 0.35, 0.5), (3.2, 0.5, 0.08), "Wood")
    p.box((2.4, -0.4, 1.5), (0.06, 0.06, 3.0), "SteelDark")
    p.cyl((2.4, -0.4, 3.0), 0.35, 0.05, "CarYellow", rot=(math.pi / 2, 0, 0), seg=16)


def traffic_light(p):
    p.cyl((0, 0, 2.2), 0.09, 4.4, "SteelDark", seg=8)
    p.box((0, -0.9, 4.3), (0.1, 1.9, 0.1), "SteelDark")
    p.box((0, -1.7, 3.9), (0.35, 0.3, 1.0), "Ink", bevel=0.04)
    for k, c in enumerate(("Taillight", "CarYellow", "CarGreen")):
        p.sphere((0, -1.87, 4.22 - k * 0.3), (0.1, 0.05, 0.1), c, seg=10, rings=6)


def satay_cart(p):
    p.box((0, 0, 0.8), (1.8, 0.8, 0.6), "Wood", bevel=0.03)
    for x in (-0.7, 0.7):
        p.cyl((x, -0.45, 0.3), 0.3, 0.08, "Tyre", rot=(0, math.pi / 2, 0), seg=12)
    p.box((0, 0, 1.15), (1.6, 0.5, 0.12), "SteelDark")
    p.box((0, 0, 1.23), (1.4, 0.35, 0.06), "Ember")
    for k in range(9):
        p.box((-0.6 + k * 0.15, 0, 1.3), (0.03, 0.45, 0.05), "Satay")
    p.cyl((0, 0.3, 2.3), 0.04, 2.6, "Steel", seg=6)
    p.cyl((0, 0.3, 3.3), 1.5, 0.5, "Awning1", r2=0.05, seg=12)            # hawker umbrella
    p.cyl((0, 0.3, 3.07), 1.52, 0.05, "White", seg=12)
    for x in (-1.5, 1.5):
        p.cyl((x, 1.4, 0.25), 0.2, 0.5, "PlasticRed", r2=0.16, seg=10)    # stools


def bunting(p):
    """Jalur Gemilang bunting strung across a street (14 m)."""
    for i in range(22):
        x = -7 + i * (14 / 21)
        z = 5.6 - math.sin(i / 21 * math.pi) * 0.9
        col = ["CarRed", "White", "PoliceBlue", "CarYellow"][i % 4]
        p.box((x, 0, z - 0.25), (0.35, 0.02, 0.45), col, taper=0.1)
    p.cyl((0, 0, 5.45), 0.015, 14, "Ink", rot=(0, math.pi / 2, 0), seg=4)


def bin_(p):
    p.cyl((0, 0, 0.45), 0.32, 0.9, "CarGreen", r2=0.36, seg=12)
    p.cyl((0, 0, 0.93), 0.4, 0.08, "SteelDark", seg=12)


def bench(p):
    p.box((0, 0, 0.45), (1.8, 0.5, 0.08), "Wood")
    p.box((0, 0.22, 0.75), (1.8, 0.06, 0.5), "Wood")
    for x in (-0.8, 0.8):
        p.box((x, 0, 0.22), (0.08, 0.45, 0.44), "SteelDark")


def billboard(p):
    for x in (-3, 3):
        p.cyl((x, 0.3, 3), 0.15, 6, "SteelDark", seg=8)
    p.box((0, 0, 6.8), (9, 0.3, 3.8), "White")
    p.box((0, -0.16, 6.8), (8.6, 0.05, 3.4), "CarYellow")                 # MegaMaju ad (text in Unity)
    p.cyl((-3, -0.2, 6.8), 1.2, 0.05, "CarGreen", rot=(math.pi / 2, 0, 0), seg=18)   # a cendol bowl logo
    p.sphere((-3, -0.25, 7.1), (0.9, 0.05, 0.5), "Pastel2", seg=14, rings=6)


def angsana(p, rng):
    """Big round roadside angsana tree with a chunky cartoon canopy."""
    p.cyl((0, 0, 2.2), 0.35, 4.4, "Trunk", r2=0.25, seg=10)
    p.sphere((0, 0, 5.2), (3.0, 3.0, 2.0), "Leaf", seg=18, rings=12)
    for i in range(6):
        a = i / 6 * math.tau
        p.sphere((math.cos(a) * 2.2, math.sin(a) * 2.2, 4.8 + rng.uniform(-0.2, 0.5)), (1.4, 1.4, 1.1),
                 "LeafDark" if i % 2 else "Leaf", seg=12, rings=8)
    for i in range(5):                                                    # yellow blossoms
        a = rng.uniform(0, math.tau)
        p.sphere((math.cos(a) * 2.5, math.sin(a) * 2.5, 5.6 + rng.uniform(0, 0.8)), (0.3,) * 3, "CarYellow", seg=8, rings=6)


def build_all():
    rng = random.Random(7)
    exported = []

    for i, (name, spec) in enumerate(CHARACTERS.items()):
        clear_scene()
        root = build_character(name, spec, seed=i + 3)
        export(root, name)
        exported.append(name)

    for name, (kind, color) in CARS.items():
        clear_scene()
        root = build_car(name, kind, color)
        export(root, name)
        exported.append(name)

    props = {
        "Bld_KampungHouse": lambda p: kampung_house(p, wall="HouseBlue", roof="RoofRed"),
        "Bld_KampungHouse2": lambda p: kampung_house(p, wall="HouseGreen", roof="RoofBlue"),
        "Bld_KampungHouse3": lambda p: kampung_house(p, wall="HouseYellow", roof="RoofGreen"),
        "Bld_KampungHouse4": lambda p: kampung_house(p, wall="HousePink", roof="Zinc"),
        "Bld_ShopRowA": lambda p: shophouse(p, ["Pastel1", "Pastel2", "Pastel3"], ("Awning1", "Awning2", "Awning3")),
        "Bld_ShopRowB": lambda p: shophouse(p, ["Pastel4", "Pastel5", "Pastel6"], ("Awning4", "Awning1", "Awning2")),
        "Bld_ShopRowC": lambda p: shophouse(p, ["HouseYellow", "Pastel6", "HouseBlue"], ("Awning2", "Awning4", "Awning3")),
        "Bld_MenaraKembar": twin_towers,
        "Bld_MenaraKL": kl_tower,
        "Bld_Masjid": masjid,
        "Bld_Mamak": mamak_stall,
        "Bld_Surau": surau,
        "Bld_CondoA": lambda p: condo(p, random.Random(1), "Pastel2"),
        "Bld_CondoB": lambda p: condo(p, random.Random(2), "Pastel4"),
        "Bld_CondoC": lambda p: condo(p, random.Random(3), "Pastel1"),
        "Bld_CondoD": lambda p: condo(p, random.Random(4), "Pastel6"),
        "Prop_Palm": lambda p: palm(p, random.Random(11)),
        "Prop_Palm2": lambda p: palm(p, random.Random(12)),
        "Prop_RainTree": lambda p: rain_tree(p, random.Random(13)),
        "Prop_Banana": lambda p: banana(p, random.Random(14)),
        "Prop_StreetLamp": street_lamp,
        "Prop_Crate": crate,
        "Prop_PasarCanopy": pasar_canopy,
        "Prop_Coin": coin,
        "Prop_TehTarik": teh_tarik,
        "Prop_Bungkus": bungkus,
        "Prop_Barrier": barrier,
        "Prop_Skyline": lambda p: skyline_card(p, random.Random(21)),
        "Prop_BurungKamera": burung_kamera,
        "Prop_CendolCrate": cendol_crate,
        "Prop_PhoneBooth": phone_booth,
        "Prop_KadLat": kad_lat,
        "Bld_SultanAbdulSamad": sultan_abdul_samad,
        "Prop_Flagpole": flagpole,
        "Prop_ChinatownGate": chinatown_gate,
        "Bld_PasarSeni": pasar_seni,
        "Prop_Ayam": chicken,
        "Prop_Kucing": cat,
        "Prop_Kapcai": lambda p: kapcai(p, "CarRed"),
        "Prop_Kapcai2": lambda p: kapcai(p, "CarBlue"),
        "Prop_BusStop": bus_stop,
        "Prop_TrafficLight": traffic_light,
        "Prop_SatayCart": satay_cart,
        "Prop_Bunting": bunting,
        "Prop_Bin": bin_,
        "Prop_Bench": bench,
        "Prop_Billboard": billboard,
        "Prop_Angsana": lambda p: angsana(p, random.Random(31)),
    }
    for name, fn in props.items():
        clear_scene()
        root = single(name, fn)
        export(root, name)
        exported.append(name)
    return exported


# --------------------------------------------------------------------------------------
# Preview render (Workbench + outline) so we can eyeball proportions without Unity.
# --------------------------------------------------------------------------------------
def preview():
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    clear_scene()
    x = 0.0
    for i, (name, spec) in enumerate(CHARACTERS.items()):
        r = build_character(name, spec, seed=i + 3)
        r.location.x = x
        x += 1.3
    for j, (name, (kind, color)) in enumerate(list(CARS.items())[:4]):
        r = build_car(name, kind, color)
        r.location = (j * 5.0, 6.0, 0)
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (5.5, -11, 4.2)
    cam.rotation_euler = (math.radians(75), 0, math.radians(0))
    cam_data.lens = 30
    scene.camera = cam
    scene.render.engine = 'BLENDER_WORKBENCH'
    shading = scene.display.shading
    shading.light = 'STUDIO'
    shading.color_type = 'MATERIAL'
    shading.show_object_outline = True
    shading.object_outline_color = (0, 0, 0)
    shading.show_cavity = True
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    scene.render.filepath = os.path.join(PREVIEW_DIR, "lineup.png")
    world = bpy.data.worlds.new("W") if not scene.world else scene.world
    scene.world = world
    world.color = (0.95, 0.92, 0.84)
    bpy.ops.render.render(write_still=True)
    print("PREVIEW", scene.render.filepath)


if __name__ == "__main__":
    if "--preview" in ARGS:
        preview()
    else:
        names = build_all()
        print("DONE", len(names), "assets")
