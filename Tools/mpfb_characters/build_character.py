"""
Build a dressed MPFB character from specs.py: body (build_body), stylized looks (looks), clothes
(garments), then save the master .blend and render review stills.

  blender --background --python-exit-code 1 --python Tools/mpfb_characters/build_character.py -- chr_pakmat
"""
import sys, os, json, importlib

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import specs, looks, garments, build_body
for m in (specs, looks, garments, build_body):
    importlib.reload(m)


def dress(body, rig, spec):
    hair = next((o for o in rig.children if spec.get("hair") and spec["hair"].split(".")[0] in o.name), None)
    made = []
    for item in spec.get("outfit", []):
        kind = item["kind"]
        kw = {k: v for k, v in item.items() if k != "kind"}
        if kind == "shirt":
            made.append(garments.shirt(body, rig, **kw))
        elif kind == "wrap":
            over = next((m for m in made if "hem_z" in m), None)
            made.append(garments.wrap_skirt(body, rig, over=over, **kw))
        elif kind == "songkok":
            made.append(garments.songkok(body, rig, hair=hair, **kw))
        elif kind == "sandals":
            made += garments.sandals(body, rig, **kw)
        print("DRESSED", kind)
    return made


def main():
    cid, out = build_body.args()
    os.makedirs(out, exist_ok=True)
    spec = specs.SPECS[cid]
    body, rig = build_body.build(cid)
    dress(body, rig, spec)
    stats = {"objects": {}}
    dg = bpy.context.evaluated_depsgraph_get()
    for o in bpy.data.objects:
        if o.type == 'MESH':
            me = o.evaluated_get(dg).to_mesh()
            stats["objects"][o.name] = sum(len(p.vertices) - 2 for p in me.polygons)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, f"{cid}_master.blend"))
    stats["renders"] = build_body.render(out, "dressed")
    with open(os.path.join(out, "stats.json"), "w") as f:
        json.dump(stats, f, indent=2)
    print("BUILD_OK", json.dumps(stats["objects"]))


if __name__ == "__main__":
    main()
