"""
Build one MPFB character's body (no clothes yet) from specs.py, save a master .blend and render
review stills. Run with Blender 5.2 (MPFB extension installed):

  blender --background --factory-startup --python-exit-code 1 --python Tools/mpfb_characters/build_body.py -- chr_pakmat [--out DIR]

--factory-startup still loads enabled extensions from the user's preferences only if we don't pass it,
so this script enables MPFB itself.
"""
import sys, os, math, json, importlib

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import specs
import looks
importlib.reload(specs)
importlib.reload(looks)


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    cid = a[0] if a else "chr_pakmat"
    out = os.path.join(HERE, "out", cid)
    if "--out" in a:
        out = a[a.index("--out") + 1]
    return cid, out


def mpfb():
    import addon_utils
    addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
    from bl_ext.user_default.mpfb.services.humanservice import HumanService
    return HumanService


def build(cid):
    spec = specs.SPECS[cid]
    HumanService = mpfb()
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)

    info = HumanService._create_default_human_info_dict()
    info["name"] = cid
    info["phenotype"] = spec["phenotype"]
    targets = dict(specs.STYLIZE)
    for k, v in spec.get("targets", {}).items():
        targets[k] = targets.get(k, 0.0) + v
    info["targets"] = [{"target": k, "value": v} for k, v in targets.items()]
    info["rig"] = "game_engine"
    info["eyes"] = "high-poly.mhclo"
    info["eyelashes"] = "eyelashes01.mhclo"
    info["eyebrows"] = spec.get("eyebrows", "eyebrow001.mhclo")
    info["teeth"] = "teeth_base.mhclo"
    info["tongue"] = "tongue01.mhclo"
    info["hair"] = spec.get("hair", "")
    info["skin_mhmat"] = spec.get("skin", "")
    info["skin_material_type"] = "ENHANCED"
    settings = HumanService.get_default_deserialization_settings()
    settings["subdiv_levels"] = 0
    body = HumanService.deserialize_from_dict(info, settings)

    rig = body.parent if body.parent else body
    bpy.context.view_layer.update()
    toon = spec.get("toon")
    if toon:
        import export_unity
        looks.toonify(body, rig, head_scale=toon.get("head", 1.3), eye_scale=toon.get("eyes", 1.35),
                      smooth_face=toon.get("smooth", 6), expressions=export_unity.expressions)
        bpy.context.view_layer.update()
    # scale the whole character (rig and all) to the authored standing height
    top = max((body.matrix_world @ Vector(c)).z for c in body.bound_box)
    s = spec["height_m"] / max(0.01, top)
    rig.scale = (s, s, s)
    bpy.context.view_layer.update()
    body["head_k"] = (toon.get("head", 1.0) if toon else 1.0) * s
    style(body, spec)
    return body, rig


def style(body, spec):
    toon = spec.get("toon") or {}
    looks.stylized_skin(body, spec.get("skin_tone", (0.55, 0.37, 0.25)), detail=toon.get("skin_detail", 0.25))
    for o in body.parent.children if body.parent else []:
        n = o.name.lower()
        if any(k in n for k in ("eyebrow", "eyelash")) or (spec.get("hair") and spec["hair"].split(".")[0] in n):
            looks.tint(o, spec.get("hair_color", (0.03, 0.025, 0.02)))
        elif "high-poly" in n or "low-poly" in n:
            if toon:
                looks.toon_eyes(o, os.path.join(HERE, "textures", "eyes_toon.png"))
            else:
                looks.iris(o)
    if toon.get("blush"):
        k = float(body.get("head_k", 1.0))
        looks.blush(body, looks.cheek_points(body.parent, k), 0.04 * k, strength=toon["blush"])
    if toon.get("no_lashes"):
        for o in list(body.parent.children):
            if "eyelash" in o.name.lower():
                bpy.data.objects.remove(o, do_unlink=True)
    if toon.get("solid_brows"):
        brow = next((o for o in body.parent.children if "eyebrow" in o.name.lower()), None)
        if brow:
            looks.brows(body, brow, spec.get("hair_color", (0.03, 0.025, 0.02)))
    if spec.get("moustache"):
        ob, lm = looks.moustache(body, spec.get("hair_color", (0.03, 0.025, 0.02)))
        print("LANDMARKS", {k: tuple(round(x, 4) for x in v) for k, v in lm.items()})


def frame(objs):
    lo = Vector((1e9, 1e9, 1e9)); hi = -lo
    for o in objs:
        if o.type != 'MESH' or o.hide_render:
            continue
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    return lo, hi


FACE_Z = 0.88


def render(out, tag):
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE'
    scn.render.resolution_x, scn.render.resolution_y = 900, 1200
    scn.view_settings.view_transform = 'AgX'
    world = bpy.data.worlds.new("studio"); scn.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.44, 0.48, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.35
    for name, loc, energy, size in [("key", (2.2, -3.0, 3.2), 260, 2.5), ("fill", (-3.0, -2.0, 1.8), 90, 3.0), ("rim", (0.5, 3.0, 2.8), 180, 2.0)]:
        L = bpy.data.lights.new(name, 'AREA'); L.energy = energy; L.size = size
        o = bpy.data.objects.new(name, L); scn.collection.objects.link(o); o.location = loc
        o.rotation_euler = (Vector((0, 0, 1.0)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    lo, hi = frame(scn.objects)
    h = hi.z - lo.z
    cam_d = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cam_d); scn.collection.objects.link(cam); scn.camera = cam
    shots = {
        "front": (0, h * 0.5, h * 1.9, 50, h * 0.52),
        "threequarter": (-35, h * 0.5, h * 1.9, 50, h * 0.52),
        "profile": (-90, h * 0.5, h * 1.9, 50, h * 0.52),
        "face": (-20, h * FACE_Z, h * 0.62, 85, h * (FACE_Z + 0.02)),
    }
    paths = []
    for name, (yaw, aim_z, dist, lens, eye_z) in shots.items():
        cam_d.lens = lens
        a = math.radians(yaw)
        target = Vector((0, 0, aim_z))
        cam.location = target + Vector((math.sin(a) * dist, -math.cos(a) * dist, eye_z - aim_z))
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(out, f"{tag}_{name}.png")
        bpy.ops.render.render(write_still=True)
        paths.append(scn.render.filepath)
    return paths


def main():
    cid, out = args()
    os.makedirs(out, exist_ok=True)
    body, rig = build(cid)
    stats = {"objects": {}, "height_m": specs.SPECS[cid]["height_m"]}
    for o in bpy.data.objects:
        if o.type == 'MESH':
            stats["objects"][o.name] = {"verts": len(o.data.vertices), "tris": sum(len(p.vertices) - 2 for p in o.data.polygons)}
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, f"{cid}_body.blend"))
    stats["renders"] = render(out, "body")
    with open(os.path.join(out, "body_stats.json"), "w") as f:
        json.dump(stats, f, indent=2)
    print("BUILD_OK", json.dumps(stats["objects"]))


if __name__ == "__main__":
    main()
