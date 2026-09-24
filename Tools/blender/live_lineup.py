# Runs INSIDE the user's live Blender (via blender-mcp). Builds the character + car
# line-up in its own scene so the user's other scenes are untouched.
import bpy, sys
path = r"C:\Users\User\PROJECTS\kampung-game\KampungRun\Tools\blender\build_assets.py"
ns = {"__name__": "kampung_assets", "__file__": path}
exec(compile(open(path, encoding="utf-8").read(), path, "exec"), ns)

scene = bpy.data.scenes.get("KampungRun_Assets") or bpy.data.scenes.new("KampungRun_Assets")
bpy.context.window.scene = scene
ns["clear_scene"]()
x = 0.0
for i, (name, spec) in enumerate(ns["CHARACTERS"].items()):
    r = ns["build_character"](name, spec, seed=i + 3)
    r.location.x = x
    x += 1.4
for j, (name, (kind, color)) in enumerate(ns["CARS"].items()):
    r = ns["build_car"](name, kind, color)
    r.location = (j * 5.5 - 2, 7.0, 0)
# frame everything in the viewport, solid view with material colours + outlines
for area in bpy.context.window.screen.areas:
    if area.type == 'VIEW_3D':
        sp = area.spaces.active
        sp.shading.type = 'SOLID'
        sp.shading.color_type = 'MATERIAL'
        sp.shading.show_object_outline = True
        sp.shading.show_cavity = True
        region = next(r for r in area.regions if r.type == 'WINDOW')
        with bpy.context.temp_override(area=area, region=region):
            bpy.ops.object.select_all(action='SELECT')
            bpy.ops.view3d.view_axis(type='FRONT')
            bpy.ops.view3d.view_all()
            bpy.ops.object.select_all(action='DESELECT')
        sp.region_3d.view_rotation.rotate(__import__("mathutils").Euler((0.35, 0, 0.25)))
print("built", len(scene.objects), "objects")
