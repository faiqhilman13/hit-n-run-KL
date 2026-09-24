"""
KL asset pipeline core (per the asset-production handoff).

Style: chunky, faceted, flat-shaded PS2-era cartoon forms. Colour comes from ONE compact
palette atlas texture (kl_palette.png): every face's UVs point at the centre of its
colour cell, so each asset is a single material that Unity's toon shader can use.

Characters are ONE skinned mesh on a Unity-Humanoid-named skeleton in T-pose. Every
primitive is assigned to a bone (or blended between two at a joint), and tagged so
expression shape keys can move brows/mouth/eyes.

Coordinates while modelling: Blender Z up, character/vehicle FRONT = -Y, character's
left = +X. Export settings in export_fbx() turn that into Unity +Z forward, +Y up.
"""
import bpy
import bmesh
import math
import os
from mathutils import Vector, Matrix, Euler, Quaternion

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
UNITY_OUT = os.path.join(PROJECT, "Assets", "Models", "KL")
HANDOFF = r"C:\Users\User\Downloads\kl-asset-production-handoff\asset-production-handoff"
DELIVERABLES = os.path.join(HANDOFF, "deliverables")
# assets the handoff asked for go to its deliverables; the rest of the cast (family and
# townsfolk restyled to match) gets the same deliverable layout inside the project
HANDOFF_IDS = {"chr_aiman", "chr_mei", "chr_ravi", "veh_delivery_bike", "veh_taxi", "veh_food_truck",
               "env_shared_street_kit", "env_chowkit_market", "env_kampung_baru", "env_brickfields"}
EXTRA_ASSETS = os.path.join(PROJECT, "Tools", "kl_assets")


def asset_dir(asset_id):
    return os.path.join(DELIVERABLES if asset_id in HANDOFF_IDS else EXTRA_ASSETS, asset_id)
ATLAS_NAME = "kl_palette.png"

# ------------------------------------------------------------------------------ palette
# Saturated flat colours sampled/judged from references/approved. Order = atlas cell.
PALETTE = [
    ("skin", (0.80, 0.49, 0.25)), ("skin_shadow", (0.62, 0.35, 0.18)), ("hair", (0.07, 0.06, 0.07)),
    ("eye_white", (0.98, 0.97, 0.94)), ("pupil", (0.05, 0.05, 0.06)), ("mouth", (0.35, 0.07, 0.08)),
    ("teeth", (0.98, 0.98, 0.95)), ("brow", (0.08, 0.06, 0.06)),
    ("teal", (0.07, 0.55, 0.58)), ("teal_dark", (0.04, 0.40, 0.43)), ("cream", (0.93, 0.89, 0.78)),
    ("charcoal", (0.20, 0.19, 0.22)), ("charcoal_dark", (0.13, 0.12, 0.14)), ("sneaker", (0.11, 0.13, 0.19)),
    ("sole", (0.90, 0.86, 0.74)), ("olive", (0.38, 0.37, 0.18)),
    ("olive_dark", (0.26, 0.25, 0.12)), ("strap", (0.18, 0.14, 0.12)), ("cord_red", (0.85, 0.12, 0.12)),
    ("metal", (0.62, 0.64, 0.66)), ("metal_dark", (0.34, 0.35, 0.38)), ("rubber", (0.10, 0.10, 0.11)),
    ("headlamp", (1.00, 0.97, 0.80)), ("amber", (1.00, 0.55, 0.08)),
    ("tail_red", (0.90, 0.10, 0.08)), ("glass", (0.45, 0.62, 0.78)), ("seat_black", (0.09, 0.09, 0.10)),
    ("pale_panel", (0.94, 0.90, 0.78)), ("coral", (0.93, 0.36, 0.30)), ("navy", (0.13, 0.20, 0.42)),
    ("ochre", (0.93, 0.74, 0.30)), ("ochre_check", (0.80, 0.58, 0.20)),
    ("taxi_yellow", (0.98, 0.80, 0.08)), ("taxi_green", (0.07, 0.42, 0.20)), ("truck_red", (0.86, 0.12, 0.10)),
    ("awning_white", (0.96, 0.94, 0.88)), ("asphalt", (0.25, 0.26, 0.30)), ("lane_white", (0.97, 0.97, 0.95)),
    ("lane_yellow", (0.98, 0.80, 0.10)), ("curb_black", (0.10, 0.10, 0.11)),
    ("curb_white", (0.95, 0.95, 0.93)), ("pavement", (0.80, 0.72, 0.60)), ("tile_red", (0.78, 0.36, 0.26)),
    ("shop_yellow", (0.98, 0.84, 0.25)), ("shop_teal", (0.10, 0.62, 0.68)), ("shop_blue", (0.16, 0.36, 0.78)),
    ("shop_green", (0.14, 0.60, 0.30)), ("shop_red", (0.86, 0.20, 0.18)),
    ("shop_pink", (0.95, 0.50, 0.62)), ("shop_orange", (0.97, 0.55, 0.15)), ("shutter_red", (0.62, 0.12, 0.10)),
    ("window_dark", (0.12, 0.14, 0.20)), ("plaster", (0.96, 0.93, 0.84)), ("roof_red", (0.74, 0.22, 0.16)),
    ("wood", (0.60, 0.38, 0.18)), ("wood_dark", (0.38, 0.22, 0.10)),
    ("leaf", (0.24, 0.65, 0.18)), ("leaf_dark", (0.12, 0.45, 0.14)), ("trunk", (0.48, 0.32, 0.18)),
    ("pot_terracotta", (0.82, 0.40, 0.18)), ("crate_red", (0.86, 0.16, 0.14)), ("crate_blue", (0.12, 0.34, 0.86)),
    ("crate_yellow", (0.98, 0.82, 0.10)), ("crate_green", (0.10, 0.62, 0.36)), ("fruit_orange", (1.00, 0.55, 0.05)),
    ("fruit_green", (0.45, 0.80, 0.15)), ("fruit_red", (0.90, 0.12, 0.12)), ("banana", (0.98, 0.86, 0.20)),
    ("umb_red", (0.92, 0.14, 0.12)), ("umb_yellow", (0.99, 0.82, 0.08)), ("umb_blue", (0.12, 0.30, 0.86)),
    ("umb_green", (0.12, 0.62, 0.30)), ("lamp_grey", (0.36, 0.38, 0.44)), ("lamp_glow", (1.00, 0.93, 0.55)),
    ("steel", (0.72, 0.75, 0.78)), ("concrete", (0.70, 0.70, 0.68)), ("concrete_dark", (0.52, 0.52, 0.52)),
    ("tarp_blue", (0.15, 0.45, 0.85)), ("barrier_red", (0.92, 0.18, 0.14)), ("wet_asphalt", (0.18, 0.20, 0.26)),
    ("stool_red", (0.90, 0.14, 0.12)), ("stool_blue", (0.14, 0.30, 0.86)), ("bollard", (0.12, 0.12, 0.14)),
    ("bollard_band", (0.98, 0.84, 0.18)), ("wire_black", (0.06, 0.06, 0.07)), ("sign_blank", (0.96, 0.95, 0.90)),
    ("kurung_pink", (0.97, 0.45, 0.62)), ("tudung_blue", (0.18, 0.46, 0.92)), ("batik_orange", (0.98, 0.55, 0.14)),
    ("batik_dark", (0.70, 0.22, 0.08)), ("sarong_green", (0.12, 0.55, 0.38)), ("songkok", (0.10, 0.08, 0.10)),
    ("tshirt_orange", (0.98, 0.45, 0.12)), ("shorts_blue", (0.18, 0.34, 0.86)), ("dress_red", (0.92, 0.24, 0.18)),
    ("slipper_blue", (0.14, 0.58, 0.95)), ("police_blue", (0.12, 0.20, 0.55)), ("gold", (0.98, 0.78, 0.18)),
    ("grey_hair", (0.78, 0.78, 0.80)), ("suit_black", (0.12, 0.12, 0.15)), ("tie_red", (0.84, 0.10, 0.12)),
    ("white", (0.97, 0.97, 0.95)), ("aircon", (0.86, 0.88, 0.90)),
    # --- P1: Mei / Ravi
    ("coral_dark", (0.78, 0.26, 0.22)), ("hair_grey", (0.19, 0.19, 0.23)), ("lens", (0.80, 0.88, 0.92)),
    ("pen_blue", (0.10, 0.22, 0.70)), ("sandal_brown", (0.36, 0.20, 0.11)), ("towel", (0.92, 0.88, 0.78)),
    # --- P1: vehicles
    ("tyre_hub", (0.55, 0.56, 0.58)), ("bumper_black", (0.14, 0.14, 0.16)), ("interior", (0.22, 0.24, 0.30)),
    ("mustard", (0.95, 0.72, 0.10)), ("pot_steel", (0.66, 0.68, 0.72)),
    # --- P1: Kampung Baru
    ("kb_teal", (0.14, 0.56, 0.54)), ("kb_teal_dark", (0.08, 0.38, 0.38)), ("kb_mint", (0.55, 0.80, 0.66)),
    ("zinc_red", (0.62, 0.20, 0.16)), ("zinc_dark", (0.42, 0.14, 0.12)), ("timber", (0.66, 0.44, 0.24)),
    ("timber_dark", (0.44, 0.27, 0.13)), ("kb_blue", (0.30, 0.50, 0.82)), ("banana_leaf", (0.36, 0.72, 0.22)),
    ("flower_red", (0.94, 0.20, 0.14)), ("flag_red", (0.86, 0.12, 0.14)), ("flag_blue", (0.08, 0.14, 0.52)),
    ("skyline_blue", (0.56, 0.66, 0.82)), ("skyline_dark", (0.38, 0.48, 0.66)),
    # --- P1: Brickfields
    ("bf_yellow", (0.98, 0.82, 0.30)), ("bf_blue", (0.12, 0.24, 0.72)), ("bf_red", (0.82, 0.16, 0.16)),
    ("marigold", (1.00, 0.62, 0.05)), ("jasmine", (0.99, 0.98, 0.90)), ("magenta", (0.90, 0.20, 0.60)),
    ("tile_cream", (0.93, 0.84, 0.66)), ("tile_brown", (0.72, 0.46, 0.28)), ("brass", (0.86, 0.62, 0.20)),
    ("train_white", (0.95, 0.96, 0.97)), ("train_red", (0.84, 0.12, 0.16)), ("train_window", (0.20, 0.30, 0.46)),
    ("grass", (0.42, 0.70, 0.26)), ("grass_dark", (0.30, 0.56, 0.20)), ("frangipani", (0.99, 0.95, 0.80)),
    # --- family + townsfolk garments: one cell per recolourable garment so a runtime palette
    #     swap (costumes, pedestrian variety) only ever touches that garment
    ("tshirt_red", (0.90, 0.20, 0.16)), ("kid_shirt", (0.96, 0.95, 0.90)), ("kid_shorts", (0.24, 0.36, 0.62)),
    ("batik_blue", (0.20, 0.42, 0.70)), ("batik_blue_dark", (0.10, 0.22, 0.45)), ("pants_khaki", (0.62, 0.55, 0.40)),
    ("pastel_lilac", (0.74, 0.62, 0.86)), ("pastel_mint", (0.60, 0.86, 0.76)), ("sarong_red", (0.72, 0.18, 0.20)),
    ("sarong_check", (0.10, 0.36, 0.26)), ("baju_white", (0.96, 0.95, 0.90)), ("kurung_skirt", (0.86, 0.36, 0.52)),
    ("dress_trim", (0.99, 0.97, 0.90)), ("kurung_flower", (0.99, 0.88, 0.92)), ("sarong_red_check", (0.50, 0.10, 0.14)),
    # --- Hit & Run-style cars: one recolourable paint cell (+ a second tone), chrome, tyres, lamps
    ("car_paint", (0.86, 0.14, 0.12)), ("car_paint2", (0.96, 0.96, 0.94)), ("chrome", (0.86, 0.88, 0.92)),
    ("car_glass", (0.30, 0.44, 0.58)), ("tyre", (0.13, 0.13, 0.15)), ("tyre_wall", (0.19, 0.19, 0.21)),
    ("rim_silver", (0.78, 0.80, 0.84)), ("rim_dark", (0.24, 0.25, 0.28)), ("well_black", (0.07, 0.07, 0.08)),
    ("trim_black", (0.10, 0.10, 0.12)), ("grille_black", (0.08, 0.08, 0.09)), ("lens_clear", (0.92, 0.95, 0.98)),
    ("lamp_red", (0.95, 0.12, 0.10)), ("lamp_amber", (1.00, 0.62, 0.10)), ("lamp_white", (1.00, 0.99, 0.92)),
    ("plate_black", (0.07, 0.07, 0.08)), ("plate_white", (0.96, 0.96, 0.96)), ("seat_grey", (0.36, 0.37, 0.42)),
    ("dash_grey", (0.20, 0.21, 0.24)), ("carpet", (0.16, 0.16, 0.18)), ("disc_steel", (0.55, 0.56, 0.58)),
    ("caliper_red", (0.80, 0.14, 0.12)), ("bed_liner", (0.14, 0.14, 0.15)), ("fresh_green", (0.30, 0.82, 0.30)),
    ("tasbih_brown", (0.45, 0.24, 0.10)), ("bike_paint", (0.10, 0.32, 0.80)), ("bike_paint2", (0.94, 0.94, 0.92)),
    ("police_white", (0.96, 0.96, 0.97)), ("police_navy", (0.08, 0.14, 0.42)), ("siren_red", (1.00, 0.12, 0.12)),
    ("siren_blue", (0.12, 0.35, 1.00)), ("sticker_yellow", (1.00, 0.84, 0.10)),
    # --- landmarks 2: Batu Caves, Tugu Negara, Thean Hou, Istana Negara...
    ("limestone", (0.78, 0.74, 0.66)), ("limestone_dark", (0.56, 0.52, 0.46)), ("bronze", (0.52, 0.38, 0.20)),
    ("jade_green", (0.10, 0.52, 0.36)), ("marble", (0.95, 0.93, 0.88)), ("brick_red", (0.72, 0.26, 0.18)),
    ("stand_blue", (0.20, 0.36, 0.72)), ("pitch_green", (0.30, 0.66, 0.24)),
]
PAL_INDEX = {name: i for i, (name, _) in enumerate(PALETTE)}
# painted surface textures (Tools/gen_surfaces.py order): which cells get grass strokes,
# mortar lines, timber boards... in the Hit & Run shader
SURF = dict(grass=1, asphalt=2, paving=3, brick=4, timber=5, plaster=6, concrete=7, leaves=8, water=9, roof=10, floortile=11)
SURFACE = {
    "grass": "grass", "grass_dark": "grass",
    "asphalt": "asphalt", "wet_asphalt": "asphalt", "curb_black": "concrete",
    "pavement": "paving", "tile_cream": "floortile", "tile_brown": "floortile", "tile_red": "paving",
    "roof_red": "roof", "zinc_red": "roof", "zinc_dark": "roof",
    "wood": "timber", "wood_dark": "timber", "timber": "timber", "timber_dark": "timber",
    "kb_teal": "timber", "kb_teal_dark": "timber", "kb_mint": "timber", "kb_blue": "timber",
    "plaster": "plaster", "shop_yellow": "plaster", "shop_teal": "plaster", "shop_blue": "plaster", "shop_green": "plaster",
    "shop_red": "plaster", "shop_pink": "plaster", "shop_orange": "plaster", "bf_yellow": "plaster", "bf_blue": "plaster",
    "bf_red": "plaster", "skyline_blue": "plaster", "skyline_dark": "plaster",
    "concrete": "concrete", "concrete_dark": "concrete", "train_white": "concrete",
    "leaf": "leaves", "leaf_dark": "leaves", "banana_leaf": "leaves", "trunk": "timber",
    "limestone": "concrete", "limestone_dark": "concrete", "brick_red": "brick", "marble": "plaster",
    "pitch_green": "grass", "jade_green": "roof",
}
# atlas alpha = gloss for the Hit & Run shader (car paint, chrome, glass and lamps shine;
# everything else - skin, cloth, tyres, buildings - stays matte)
GLOSSY = {"car_paint": 1.0, "car_paint2": 1.0, "chrome": 1.0, "car_glass": 1.0, "rim_silver": 0.9, "lens_clear": 1.0,
          "lamp_red": 0.9, "lamp_amber": 0.9, "lamp_white": 0.9, "bike_paint": 1.0, "bike_paint2": 1.0,
          "police_white": 1.0, "police_navy": 1.0, "taxi_yellow": 1.0, "taxi_green": 1.0, "truck_red": 0.8,
          "glass": 0.9, "metal": 0.6, "steel": 0.6, "headlamp": 0.9, "tail_red": 0.8, "disc_steel": 0.5,
          "siren_red": 0.8, "siren_blue": 0.8, "trim_black": 0.35}
CELLS = 16          # 16 x 16 cells
CELL_PX = 8         # 128 x 128 px atlas


def pal_uv(name):
    i = PAL_INDEX[name]
    cx, cy = i % CELLS, i // CELLS
    return ((cx + 0.5) / CELLS, 1.0 - (cy + 0.5) / CELLS)


def write_atlas(path):
    size = CELLS * CELL_PX
    img = bpy.data.images.get("kl_palette") or bpy.data.images.new("kl_palette", size, size, alpha=True)
    if img.size[0] != size:
        img.scale(size, size)
    img.alpha_mode = 'CHANNEL_PACKED'     # alpha is gloss data, not transparency
    px = [0.0] * (size * size * 4)
    for name, rgb in PALETTE:
        i = PAL_INDEX[name]
        cx, cy = i % CELLS, i // CELLS
        for y in range(CELL_PX):
            for x in range(CELL_PX):
                gx = cx * CELL_PX + x
                gy = size - 1 - (cy * CELL_PX + y)   # image rows go bottom-up
                o = (gy * size + gx) * 4
                # atlas stores display colours; mark as sRGB image
                px[o:o + 4] = [rgb[0], rgb[1], rgb[2], cell_alpha(name)]
    img.pixels = px
    img.filepath_raw = path
    img.file_format = 'PNG'
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save()
    write_palette_table()
    return img


def cell_alpha(name):
    """Atlas alpha: 128..255 = gloss (paint, chrome, glass), 1..15 = painted surface id, 0 = plain."""
    if name in GLOSSY:
        return (128 + round(GLOSSY[name] * 127)) / 255.0
    if name in SURFACE:
        return SURF[SURFACE[name]] / 255.0
    return 0.0


def write_palette_table():
    """kl_palette.json for the game: colour name -> atlas cell + RGB, so costumes and
    pedestrian variety can repaint named cells of a per-instance palette texture."""
    import json
    out = os.path.join(PROJECT, "Assets", "KampungRun", "Resources", "kl_palette.json")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    data = {"cells": CELLS, "cellPx": CELL_PX,
            "entries": [{"name": n, "index": PAL_INDEX[n], "r": c[0], "g": c[1], "b": c[2], "gloss": GLOSSY.get(n, 0.0), "alpha": cell_alpha(n)}
                        for n, c in PALETTE]}
    with open(out, "w") as f:
        json.dump(data, f, indent=0)


def atlas_material(img):
    m = bpy.data.materials.get("M_KL_Atlas") or bpy.data.materials.new("M_KL_Atlas")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.get("KLAtlas") or nt.nodes.new("ShaderNodeTexImage")
    tex.name = "KLAtlas"
    tex.image = img
    tex.interpolation = 'Closest'
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 1.0
    return m


# ------------------------------------------------------------------------------ mesher
# LOD build mode (set by kl_build while generating *_LOD1 meshes): round parts get fewer
# facets and any primitive whose second-largest dimension is under LOD_CULL metres (slats,
# trims, louvres, motifs...) is dropped - detail the camera can't resolve at distance.
LOD_DETAIL = 1.0
LOD_CULL = 0.0


class Mesher:
    """Accumulates faceted primitives into one bmesh with palette UVs, per-vertex bone
    weights and tags (for shape keys / separate objects)."""

    def __init__(self, detail=1.0, smooth=False, lsegs=1):
        self.detail = detail * LOD_DETAIL   # multiplies round-primitive segment counts (triangle budget control)
        self.smooth = smooth                # smooth-shade everything (rounded cartoon characters)
        self.lsegs = lsegs                  # default length subdivisions for tubes (so limbs can bend)
        self.cull = LOD_CULL
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.weights = []   # (BMVert, {bone: w})
        self.tags = {}      # tag -> [BMVert]

    def _merge(self, tmp, color, bones=None, tag=None, smooth=False):
        vmap = {}
        for v in tmp.verts:
            nv = self.bm.verts.new(v.co)
            vmap[v] = nv
            if bones:
                self.weights.append((nv, bones))
            if tag:
                self.tags.setdefault(tag, []).append(nv)
        u = pal_uv(color)
        for f in tmp.faces:
            try:
                nf = self.bm.faces.new([vmap[v] for v in f.verts])
            except ValueError:
                continue
            nf.smooth = smooth or self.smooth
            for loop in nf.loops:
                loop[self.uv].uv = u
        tmp.free()

    def _small(self, *dims):
        if self.cull <= 0:
            return False
        d = sorted(abs(x) for x in dims)
        return d[1] < self.cull

    def _n(self, n):
        # 4-sided pieces (hair tufts, straps) stay square; everything round gets more facets
        return n if n <= 4 else max(5, int(round(n * self.detail)))

    @staticmethod
    def M(loc, size=(1, 1, 1), rot=(0, 0, 0)):
        r = rot if isinstance(rot, (Quaternion, Matrix)) else Euler(rot)
        rm = r.to_matrix().to_4x4() if not isinstance(r, Matrix) else r.to_4x4()
        return Matrix.Translation(Vector(loc)) @ rm @ Matrix.Diagonal((size[0], size[1], size[2], 1.0))

    def box(self, loc, size, color, rot=(0, 0, 0), bones=None, tag=None, taper=(1.0, 1.0), bevel=0.0):
        if self._small(*size):
            return
        if self.cull > 0:
            bevel = 0.0
        tmp = bmesh.new()
        bmesh.ops.create_cube(tmp, size=1.0)
        for v in tmp.verts:
            if v.co.z > 0:
                v.co.x *= taper[0]
                v.co.y *= taper[1]
        bmesh.ops.transform(tmp, matrix=Matrix.Diagonal((size[0], size[1], size[2], 1)), verts=tmp.verts)
        if bevel > 0:
            bmesh.ops.bevel(tmp, geom=list(tmp.edges), offset=bevel, segments=1, affect='EDGES', clamp_overlap=True)
        bmesh.ops.transform(tmp, matrix=self.M(loc, rot=rot), verts=tmp.verts)
        self._merge(tmp, color, bones, tag)

    def ball(self, loc, radii, color, seg=8, rings=6, rot=(0, 0, 0), bones=None, tag=None, smooth=False):
        if self._small(*(2 * r for r in radii)):
            return
        seg, rings = max(4, self._n(seg)), max(3, self._n(rings))
        tmp = bmesh.new()
        bmesh.ops.create_uvsphere(tmp, u_segments=seg, v_segments=rings, radius=1.0, matrix=self.M(loc, radii, rot))
        self._merge(tmp, color, bones, tag, smooth)

    def cyl(self, loc, r1, r2, depth, color, seg=8, rot=(0, 0, 0), bones=None, tag=None, scale_xy=(1, 1)):
        if self._small(2 * max(r1, r2) * scale_xy[0], 2 * max(r1, r2) * scale_xy[1], depth):
            return
        seg = max(4, self._n(seg))
        tmp = bmesh.new()
        bmesh.ops.create_cone(tmp, cap_ends=True, cap_tris=False, segments=seg, radius1=r1, radius2=r2, depth=depth,
                              matrix=Matrix.Diagonal((scale_xy[0], scale_xy[1], 1, 1)))
        bmesh.ops.transform(tmp, matrix=self.M(loc, rot=rot), verts=tmp.verts)
        self._merge(tmp, color, bones, tag)

    def tube(self, p0, p1, r0, r1, color, seg=8, bones=None, tag=None, squash=(1, 1), twist=0.0, lsegs=None):
        """Tube from p0 to p1 (limbs, poles). lsegs > 1 adds rings along the length so a
        blended-weight limb bends smoothly instead of folding like a hinge."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        q = Vector((0, 0, 1)).rotation_difference(d.normalized())
        q = q @ Quaternion((0, 0, 1), twist)
        if self._small(2 * max(r0, r1) * squash[0], 2 * max(r0, r1) * squash[1], d.length):
            return
        seg = max(4, self._n(seg)) if seg > 4 else seg
        tmp = bmesh.new()
        bmesh.ops.create_cone(tmp, cap_ends=True, cap_tris=False, segments=seg, radius1=r0, radius2=r1, depth=d.length,
                              matrix=Matrix.Diagonal((squash[0], squash[1], 1, 1)))
        n = self.lsegs if lsegs is None else lsegs
        if n > 1 and seg > 4 and self.cull <= 0:
            side = [e for e in tmp.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > 1e-6]
            bmesh.ops.subdivide_edges(tmp, edges=side, cuts=n - 1, use_grid_fill=True)
        bmesh.ops.transform(tmp, matrix=Matrix.Translation((p0 + p1) / 2) @ q.to_matrix().to_4x4(), verts=tmp.verts)
        self._merge(tmp, color, bones, tag)

    def prism(self, loc, size, color, rot=(0, 0, 0), bones=None, tag=None):
        """Triangular prism, ridge along X: size = (length, depth, height)."""
        if self._small(*size):
            return
        tmp = bmesh.new()
        L, D, H = size[0] / 2, size[1] / 2, size[2]
        vs = [tmp.verts.new(c) for c in ((-L, -D, 0), (-L, D, 0), (-L, 0, H), (L, -D, 0), (L, D, 0), (L, 0, H))]
        a0, a1, a2, b0, b1, b2 = vs
        for f in ((a0, a2, a1), (b0, b1, b2), (a0, a1, b1, b0), (a1, a2, b2, b1), (a2, a0, b0, b2)):
            tmp.faces.new(f)
        bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
        bmesh.ops.transform(tmp, matrix=self.M(loc, rot=rot), verts=tmp.verts)
        self._merge(tmp, color, bones, tag)

    def poly_extrude(self, pts2d, depth, color, loc=(0, 0, 0), rot=(0, 0, 0), bones=None, tag=None):
        """Extrude a 2D outline (in XZ plane) along Y by depth (spiky hair, signs...)."""
        tmp = bmesh.new()
        front = [tmp.verts.new((x, -depth / 2, z)) for x, z in pts2d]
        back = [tmp.verts.new((x, depth / 2, z)) for x, z in pts2d]
        tmp.faces.new(front)
        tmp.faces.new(list(reversed(back)))
        n = len(pts2d)
        for i in range(n):
            j = (i + 1) % n
            tmp.faces.new((front[i], front[j], back[j], back[i]))
        bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
        bmesh.ops.transform(tmp, matrix=self.M(loc, rot=rot), verts=tmp.verts)
        self._merge(tmp, color, bones, tag)

    def tris(self):
        return sum(len(f.verts) - 2 for f in self.bm.faces)

    def to_object(self, name, material, collection=None, origin=None, parent=None, turn180=False):
        if turn180:  # rigid assets are authored front = -Y but must face +Y to import as Unity +Z
            for v in self.bm.verts:
                v.co.x, v.co.y = -v.co.x, -v.co.y
            if origin is not None:
                origin = (-origin[0], -origin[1], origin[2])
        if origin is not None:  # pivot: geometry relative to origin, object placed at origin
            bmesh.ops.translate(self.bm, vec=-Vector(origin), verts=self.bm.verts)
        self.bm.normal_update()
        me = bpy.data.meshes.new(name)
        self.bm.verts.index_update()
        index_of = {v: v.index for v in self.bm.verts}
        weights = [(index_of[v], b) for v, b in self.weights]
        tags = {t: [index_of[v] for v in vs] for t, vs in self.tags.items()}
        self.bm.to_mesh(me)
        self.bm.free()
        me.materials.append(material)
        ob = bpy.data.objects.new(name, me)
        (collection or bpy.context.scene.collection).objects.link(ob)
        world = Vector(origin) if origin is not None else Vector()
        ob["kl_world_origin"] = list(world)
        if parent is not None:  # parents are never rotated, so local = world - parent world
            ob.parent = parent
            world = world - Vector(parent.get("kl_world_origin", (0, 0, 0)))
        ob.location = world
        ob["kl_tags"] = {t: len(v) for t, v in tags.items()}
        return ob, weights, tags


# ------------------------------------------------------------------------------ scene
def fresh_scene(name):
    """Work in a scene of our own so a live Blender session isn't disturbed."""
    if not bpy.app.background and bpy.context.window:   # live session: our own scene, switch to it
        sc = bpy.data.scenes.get(name) or bpy.data.scenes.new(name)
        bpy.context.window.scene = sc
    else:
        # headless batch (Blender 5 still reports a window here): reuse and rename the scene and
        # clear every object, so the next asset's parts keep their exact names (no ".001")
        sc = bpy.context.scene
        sc.name = name
        for ob in list(bpy.data.objects):
            bpy.data.objects.remove(ob, do_unlink=True)
    for ob in list(sc.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for c in list(sc.collection.children):
        sc.collection.children.unlink(c)
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0
    cols = {}
    for cname in ("EXPORT", "COLLISION", "REFERENCE", "LIGHTS_CAMERA"):
        c = bpy.data.collections.get(f"{name}_{cname}") or bpy.data.collections.new(f"{name}_{cname}")
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        sc.collection.children.link(c)
        cols[cname] = c
    # 1 m reference cube (never exported)
    cube = bpy.data.objects.new("Ref_1m_Cube", bpy.data.meshes.new("Ref_1m"))
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((1.5, 0, 0.5)))
    bm.to_mesh(cube.data)
    bm.free()
    cols["REFERENCE"].objects.link(cube)
    cube.display_type = "WIRE"
    cube.hide_render = True
    return sc, cols


def export_fbx(objects, path, armature=False, anims=False, path_mode="COPY"):
    """Unity: forward +Z, up +Y, 1 unit = 1 m. No 'apply transform' for armatures."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.view_layer.update()
    for o in [o for o in bpy.context.view_layer.objects if o is not None]:
        o.select_set(False)
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True,
        object_types={'ARMATURE', 'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', global_scale=1.0,
        axis_forward='Z', axis_up='Y', bake_space_transform=False,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=False,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        armature_nodetype='NULL', use_armature_deform_only=True,
        bake_anim=anims, bake_anim_use_all_actions=anims, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        path_mode=path_mode, embed_textures=False)
    print("FBX", path)
