"""
Build KL handoff assets.

  blender -b --factory-startup -P Tools/blender/kl/kl_build.py -- chr_aiman [more ids...]

For each asset: build in its own scene -> save .blend -> export FBX (+ atlas PNG) into
both the handoff's deliverables/<id>/ and the Unity project (Assets/Models/KL) ->
render a three-quarter preview + front/side/back contact sheet -> write NOTES.md stats.
"""
import bpy
import os
import sys
import math
import json
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib
import kl_core as K
import kl_humanoid as H
import kl_characters as C
import kl_vehicles as V
import kl_env as E
import kl_env_p1 as E1
import kl_family as F
import kl_landmarks as L
import kl_cars as CARS
import kl_landmarks2 as L2
for mod in (K, H, C, V, E, E1, F, L, CARS, L2):
    importlib.reload(mod)
E.KIT.update(E1.KIT_P1)
E.KIT.update(L.KIT_LANDMARKS)
E.KIT.update(L2.KIT_LANDMARKS2)

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def deliv(asset_id, *parts):
    d = os.path.join(K.asset_dir(asset_id), *parts)
    os.makedirs(os.path.dirname(d) if os.path.splitext(d)[1] else d, exist_ok=True)
    return d


# ------------------------------------------------------------------------------ previews
def render_view(scene, target_objs, yaw_deg, path, res=(700, 900), pitch=12, action=None, frame=0, fill=1.15):
    pts = []
    for o in target_objs:
        if o.type == 'MESH':
            dg = bpy.context.evaluated_depsgraph_get()
            eo = o.evaluated_get(dg)
            pts += [eo.matrix_world @ Vector(c) for c in eo.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    center = (lo + hi) / 2
    size = max(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z) * fill
    cam_data = bpy.data.cameras.get("KLPreviewCam") or bpy.data.cameras.new("KLPreviewCam")
    cam = bpy.data.objects.get("KLPreviewCam") or bpy.data.objects.new("KLPreviewCam", cam_data)
    if cam.name not in scene.objects:
        scene.collection.objects.link(cam)
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size
    yaw, pit = math.radians(yaw_deg), math.radians(pitch)
    d = Vector((math.sin(yaw) * math.cos(pit), -math.cos(yaw) * math.cos(pit), math.sin(pit)))
    cam.location = center + d * size * 3
    cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    scene.render.engine = 'BLENDER_WORKBENCH'
    sh = scene.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'TEXTURE'
    sh.show_object_outline = True
    sh.object_outline_color = (0, 0, 0)
    sh.show_cavity = False
    sh.show_backface_culling = True
    scene.display.render_aa = '8'
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("KLWorld")
    scene.world.color = (0.86, 0.8, 0.68)
    scene.frame_set(frame)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def contact_sheet(paths, out):
    imgs = [bpy.data.images.load(p, check_existing=False) for p in paths]
    w, h = imgs[0].size
    sheet = np.zeros((h, w * len(imgs), 4), dtype=np.float32)
    for i, im in enumerate(imgs):
        a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
        sheet[:, i * w:(i + 1) * w, :] = a
    out_img = bpy.data.images.new("contact", w * len(imgs), h)
    out_img.pixels = sheet.ravel()
    out_img.filepath_raw = out
    out_img.file_format = 'PNG'
    out_img.save()
    for im in imgs:
        bpy.data.images.remove(im)


# ------------------------------------------------------------------------------ characters
CHARACTER_BUILDERS = {"chr_aiman": (C.build_aiman, C.FACE_KEYS), "chr_mei": (C.build_mei, C.MEI_KEYS),
                      "chr_ravi": (C.build_ravi, C.RAVI_KEYS)}
CHARACTER_BUILDERS.update({cid: (fn, F.FAMILY_KEYS) for cid, fn in F.BUILDERS.items()})


def character_lod(mesh_ob, arm, collection, asset_id, ratio=0.3):
    """Skinned LOD1: copy the mesh, drop shape keys, collapse-decimate it (never across colour
    cells, so the palette UVs stay clean) and put it back on the armature."""
    me = mesh_ob.data.copy()
    lod = bpy.data.objects.new(f"{asset_id}_LOD1", me)
    collection.objects.link(lod)
    lod.parent = arm
    if lod.data.shape_keys:
        lod.shape_key_clear()
    for vg in mesh_ob.vertex_groups:
        lod.vertex_groups.new(name=vg.name)
    dec = lod.modifiers.new("dec", 'DECIMATE')
    dec.decimate_type = 'COLLAPSE'
    dec.ratio = ratio
    dec.delimit = {'UV'}
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    new = bpy.data.meshes.new_from_object(lod.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    lod.modifiers.clear()
    lod.data = new
    bpy.data.meshes.remove(me)
    mod = lod.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    print("LOD1", asset_id, len(new.polygons), "faces")
    return lod


def build_character(asset_id):
    # Human art is owned by the measured, refined pipeline. Keep this historical
    # build entry point working so ordinary asset rebuilds cannot restore the
    # superseded primitive cast. Vehicle/environment builders below are unchanged.
    refined_dir = os.path.join(K.PROJECT, 'Tools', 'refined_characters')
    if refined_dir not in sys.path:
        sys.path.insert(0, refined_dir)
    import build_characters as refined
    return refined.build(asset_id, publish=True)


def build_legacy_character(asset_id):
    """Archived builder retained for source comparisons; not a delivery path."""
    builder, keys = CHARACTER_BUILDERS[asset_id]
    # the FBX exporter bakes *every* action in the file: drop earlier characters' clips
    for act in list(bpy.data.actions):
        bpy.data.actions.remove(act)
    scene, cols = K.fresh_scene(asset_id)
    atlas_path = deliv(asset_id, "textures", K.ATLAS_NAME)
    img = K.write_atlas(atlas_path)
    mat = K.atlas_material(img)
    m, p = builder()
    tris = m.tris()
    mesh_ob, weights, tags = m.to_object(asset_id, mat, cols["EXPORT"])
    arm = H.build_armature(asset_id, p, cols["EXPORT"])
    H.skin(mesh_ob, arm, H.blend_weights(p, weights, mesh_ob))
    H.shape_keys(mesh_ob, tags, keys)
    clips = H.author_clips(arm, asset_id)
    arm.name = asset_id  # the FBX root node carries the asset id
    # export from the T-pose rest: the FBX node transforms become Unity's default pose, which
    # the Humanoid avatar is built from (author_clips leaves the last clip - sit - applied)
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)
    scene.frame_set(0)
    bpy.context.view_layer.update()
    # ---- distance version: a decimated copy on the same skeleton (no face keys - too far to
    # read them). Unity builds a LODGroup from the _LOD0 / _LOD1 names, so crowds stay cheap.
    lod = character_lod(mesh_ob, arm, cols["EXPORT"], asset_id)
    mesh_ob.name = f"{asset_id}_LOD0"
    # ---- save source + export
    blend = deliv(asset_id, f"{asset_id}.blend")
    fbx = deliv(asset_id, f"{asset_id}.fbx")
    K.export_fbx([arm, mesh_ob, lod], fbx, armature=True, anims=True)
    unity_fbx = os.path.join(K.UNITY_OUT, f"{asset_id}.fbx")
    K.export_fbx([arm, mesh_ob, lod], unity_fbx, armature=True, anims=True, path_mode="STRIP")
    K.write_atlas(os.path.join(K.UNITY_OUT, K.ATLAS_NAME))
    # ---- previews: idle pose front/side/back + three-quarter, and a T-pose check
    idle = bpy.data.actions.get(f"{asset_id}|idle")
    arm.animation_data.action = idle
    views = []
    for yaw, nm in ((0, "front"), (90, "side"), (180, "back")):
        views.append(render_view(scene, [mesh_ob], yaw, deliv(asset_id, "previews", f"_{nm}.png"), frame=10))
    contact_sheet(views, deliv(asset_id, "previews", "front-side-back.png"))
    for v in views:
        os.remove(v)
    render_view(scene, [mesh_ob], 35, deliv(asset_id, "previews", "three-quarter.png"), res=(800, 1000), frame=10)
    for clip, fr in (("walk", 8), ("run", 5), ("ride", 0), ("punch", 4)):
        arm.animation_data.action = bpy.data.actions.get(f"{asset_id}|{clip}")
        render_view(scene, [mesh_ob], 60, deliv(asset_id, "previews", f"pose-{clip}.png"), res=(600, 700), frame=fr)
    arm.animation_data.action = None
    scene.frame_set(0)
    bpy.ops.wm.save_as_mainfile(filepath=blend, copy=True)
    dims = mesh_ob.dimensions
    stats = dict(asset=asset_id, triangles=tris, materials=len(mesh_ob.data.materials), bones=len(arm.data.bones),
                 shape_keys=[k.name for k in mesh_ob.data.shape_keys.key_blocks][1:], clips=[c.split("|")[1] for c in clips],
                 dimensions_m=[round(dims.x, 3), round(dims.y, 3), round(dims.z, 3)], blender=bpy.app.version_string)
    with open(deliv(asset_id, "stats.json"), "w") as f:
        json.dump(stats, f, indent=1)
    print("STATS", json.dumps(stats))
    return stats


# ------------------------------------------------------------------------------ vehicles / props
VEHICLE_BUILDERS = {"veh_delivery_bike": V.delivery_bike, "veh_taxi": V.taxi, "veh_food_truck": V.food_truck}


def bbox(objs):
    pts = []
    for o in objs:
        if o.type == "MESH":
            pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


# ------------------------------------------------------------------------------ LODs
LOD_MIN_TRIS = 250          # modules smaller than this don't get a distance version


def lod_meshers(parts_fn):
    """Rebuild an asset's parts in LOD mode (fewer facets, sub-15 cm detail dropped)."""
    K.LOD_DETAIL, K.LOD_CULL = 0.45, 0.15
    try:
        return {name: (m, origin, parent) for name, m, origin, parent in parts_fn()}
    finally:
        K.LOD_DETAIL, K.LOD_CULL = 1.0, 0.0


def add_lod(ob, lod_mesher, origin, mat, cols, parent):
    """ob becomes <name>_LOD0 and gets a sibling <name>_LOD1 (Unity builds a LODGroup from
    the suffixes). Returns the LOD1 triangle count."""
    base = ob.name
    tris = lod_mesher.tris()
    lod, _, _ = lod_mesher.to_object(base + "_LOD1", mat, cols["EXPORT"], origin=origin, parent=parent, turn180=True)
    ob.name = base + "_LOD0"
    return lod, tris


def build_assembly(asset_id, parts_fn, hit_and_run=False):
    scene, cols = K.fresh_scene(asset_id)
    img = K.write_atlas(deliv(asset_id, "textures", K.ATLAS_NAME))
    mat = K.atlas_material(img)
    if hit_and_run:
        # smooth Hit & Run cars: Body stays one rolling part (no LOD split, its doors/lamps are children)
        root, objs, tris = CARS.assemble(asset_id, parts_fn(), mat, cols)
        body = bpy.data.objects["Body"]
        lod_tris = None
    else:
        root, objs, tris = V.assemble(asset_id, parts_fn(), mat, cols)
        # distance version of the body shell (wheels/doors/hatches stay single-LOD moving parts)
        lods = lod_meshers(parts_fn)
        body = bpy.data.objects["Body"]
        lod, lod_tris = add_lod(body, lods["Body"][0], None, mat, cols, root)
        objs.append(lod)
    bpy.context.view_layer.update()
    visible = [o for o in objs if o.type == "MESH" and not o.name.startswith("COL_") and not o.name.endswith("_LOD1")]
    export = [root] + objs
    K.export_fbx(export, deliv(asset_id, f"{asset_id}.fbx"))
    K.export_fbx(export, os.path.join(K.UNITY_OUT, f"{asset_id}.fbx"), path_mode="STRIP")
    K.write_atlas(os.path.join(K.UNITY_OUT, K.ATLAS_NAME))
    views = []
    # rigid assets are turned to face Blender +Y (Unity +Z), so the front view looks from +Y
    for yaw, nm in ((180, "front"), (270, "side"), (0, "back")):
        views.append(render_view(scene, visible, yaw, deliv(asset_id, "previews", f"_{nm}.png"), res=(800, 700)))
    contact_sheet(views, deliv(asset_id, "previews", "front-side-back.png"))
    for v in views:
        os.remove(v)
    render_view(scene, visible, 215, deliv(asset_id, "previews", "three-quarter.png"), res=(1000, 800), pitch=20)
    render_view(scene, visible, 145, deliv(asset_id, "previews", "three-quarter-rear.png"), res=(1000, 800), pitch=20)
    bpy.ops.wm.save_as_mainfile(filepath=deliv(asset_id, f"{asset_id}.blend"), copy=True)
    lo, hi = bbox(visible)
    size = hi - lo
    stats = dict(asset=asset_id, triangles=tris, lod1_body_triangles=lod_tris,
                 lod0_body_triangles=sum(len(p.vertices) - 2 for p in body.data.polygons), materials=1,
                 parts=[o.name for o in objs],
                 dimensions_m=dict(length=round(size.y, 3), width=round(size.x, 3), height=round(size.z, 3)),
                 blender=bpy.app.version_string)
    with open(deliv(asset_id, "stats.json"), "w") as f:
        json.dump(stats, f, indent=1)
    print("STATS", json.dumps(stats))
    return stats


KITS = {
    "env_shared_street_kit": ["env_road_straight", "env_road_corner", "env_road_t", "env_road_cross", "env_crosswalk",
                              "env_curb_edge", "env_curb_corner", "env_chowkit_shop_a", "env_chowkit_shop_b",
                              "env_chowkit_shop_c", "env_chowkit_shop_d", "env_chowkit_shop_e", "env_awning_teal",
                              "env_awning_green", "env_awning_red", "env_awning_blue", "env_lamp_post", "env_wire_pole",
                              "env_planter", "env_crate", "env_crate_stack", "env_market_umbrella", "env_stall_counter",
                              "env_signboard_blank", "env_stool_red", "env_stool_blue", "env_bollard", "env_palm"],
    "env_kampung_baru": ["env_kb_house_teal", "env_kb_house_mint", "env_kb_house_blue", "env_kb_house_small",
                         "env_kb_banana_plant", "env_kb_flower_pot", "env_kb_flower_pot_white", "env_kb_shrub",
                         "env_kb_rain_tree", "env_kb_fence", "env_kb_lane", "env_kb_skyline"],
    "env_brickfields": ["env_bf_shopfront_yellow", "env_bf_shopfront_pink", "env_bf_shopfront_blue", "env_bf_flower_stall",
                        "env_bf_lamp", "env_bf_curb_edge", "env_bf_rail_viaduct", "env_bf_lrt_car"],
    "env_kl_landmarks": ["env_lm_kl_sentral", "env_lm_muzium_negara", "env_lm_masjid_negara", "env_pbg_lake",
                         "env_pbg_footbridge", "env_pbg_gazebo", "env_pbg_pergola", "env_pbg_flowerbed", "env_pbg_orchid_arch",
                         "env_pbg_bench", "env_pbg_fountain"],
    "env_kl_landmarks2": ["env_lm2_batu_caves", "env_lm2_merdeka118", "env_lm2_tugu_negara", "env_lm2_istana_negara",
                          "env_lm2_thean_hou", "env_lm2_masjid_jamek", "env_lm2_pavilion", "env_lm2_stadium_merdeka"],
    "env_chowkit_market": ["env_market_canopy_blue", "env_market_canopy_red", "env_market_canopy_teal",
                           "env_market_canopy_yellow", "env_crate_oranges", "env_traffic_barrier", "env_tarp_stack",
                           "env_road_straight_wet"],
}


# flat tiles and far-distance cards don't need a distance version
NO_LOD = {"env_road_straight", "env_road_straight_wet", "env_road_corner", "env_road_t", "env_road_cross", "env_crosswalk",
          "env_curb_edge", "env_curb_corner", "env_bf_curb_edge", "env_kb_lane", "env_kb_skyline",
          "env_kb_fence", "env_pbg_lake"}   # the fence is all slats: its only sensible distance version is the same mesh
HERO_MODULES = {"env_chowkit_shop_a", "env_market_canopy_blue", "env_road_corner", "env_stall_counter",
                "env_kb_house_teal", "env_kb_banana_plant", "env_kb_rain_tree", "env_kb_skyline",
                "env_bf_shopfront_yellow", "env_bf_flower_stall", "env_bf_lamp", "env_bf_rail_viaduct", "env_bf_lrt_car",
                "env_lm_kl_sentral", "env_lm_muzium_negara", "env_lm_masjid_negara", "env_pbg_gazebo", "env_pbg_pergola",
                "env_pbg_fountain"} | set(L2.KIT_LANDMARKS2)


def build_kit(kit_id):
    scene, cols = K.fresh_scene(kit_id)
    img = K.write_atlas(deliv(kit_id, "textures", K.ATLAS_NAME))
    mat = K.atlas_material(img)
    K.write_atlas(os.path.join(K.UNITY_OUT, K.ATLAS_NAME))
    unity_dir = os.path.join(K.UNITY_OUT, "env")
    table, visible_all = [], []
    x = 0.0
    for mid in KIT_MODULES(kit_id):
        root, objs, tris = V.assemble(mid, E.kit_parts(mid), mat, cols)
        lod_tris = None
        if tris >= LOD_MIN_TRIS and mid not in NO_LOD:
            geo = bpy.data.objects[f"{mid}_geo"]
            lm = lod_meshers(lambda: E.kit_parts(mid))[f"{mid}_geo"][0]
            lod, lod_tris = add_lod(geo, lm, None, mat, cols, root)
            objs.append(lod)
        bpy.context.view_layer.update()
        export = [root] + objs
        K.export_fbx(export, deliv(kit_id, "fbx", f"{mid}.fbx"))
        K.export_fbx(export, os.path.join(unity_dir, f"{mid}.fbx"), path_mode="STRIP")
        vis = [o for o in objs if not o.name.startswith("COL_") and not o.name.endswith("_LOD1")]
        lo, hi = bbox(vis)
        size = hi - lo
        table.append(dict(id=mid, triangles=tris, lod1_triangles=lod_tris,
                          size_m=[round(size.x, 2), round(size.y, 2), round(size.z, 2)],
                          collision=any(o.name.startswith("COL_") for o in objs)))
        # lay modules out in a row for the .blend + preview sheet
        w = max(size.x, 3.0)
        root.location.x = x + w / 2
        x += w + 1.5
        visible_all += vis
    bpy.context.view_layer.update()
    render_view(scene, visible_all, 25, deliv(kit_id, "previews", "kit-sheet.png"), res=(2400, 900), pitch=28, fill=1.02)
    # close-up front views of the hero modules (turned assets face +Y in Blender -> yaw 180)
    for mid in KIT_MODULES(kit_id):
        if mid in HERO_MODULES:
            objs = [o for o in bpy.data.objects[mid].children_recursive if o.type == "MESH" and not o.name.startswith("COL_")
                    and not o.name.endswith("_LOD1")]
            render_view(scene, objs, 200, deliv(kit_id, "previews", f"{mid}.png"), res=(900, 800), pitch=18)
    bpy.ops.wm.save_as_mainfile(filepath=deliv(kit_id, f"{kit_id}.blend"), copy=True)
    stats = dict(asset=kit_id, modules=table, materials=1, blender=bpy.app.version_string)
    with open(deliv(kit_id, "stats.json"), "w") as f:
        json.dump(stats, f, indent=1)
    print("STATS", kit_id, len(table), "modules")


def KIT_MODULES(kit_id):
    return KITS[kit_id]


if __name__ == "__main__":
    for aid in ARGS:
        if aid in CHARACTER_BUILDERS:
            build_character(aid)
        elif aid in VEHICLE_BUILDERS:
            build_assembly(aid, VEHICLE_BUILDERS[aid])
        elif aid in CARS.BUILDERS:
            build_assembly(aid, CARS.BUILDERS[aid], hit_and_run=True)
        elif aid in KITS:
            build_kit(aid)
        else:
            print("unknown asset", aid)
