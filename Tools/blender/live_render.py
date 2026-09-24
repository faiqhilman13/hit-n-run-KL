import bpy, mathutils, math
names = VIEW_NAMES
out = r"C:\Users\User\PROJECTS\kampung-game\KampungRun\Tools\previews\OUTNAME.png"
scene = bpy.context.scene
pts = []
for n in names:
    root = scene.objects.get(n)
    if not root: continue
    for o in [root] + list(root.children_recursive):
        if o.type == 'MESH':
            pts += [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
lo = mathutils.Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = mathutils.Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
center = (lo + hi) / 2
size = max(hi.x - lo.x, hi.z - lo.z) * 1.15
cam_data = bpy.data.cameras.get("PreviewCam") or bpy.data.cameras.new("PreviewCam")
cam = scene.objects.get("PreviewCam") or bpy.data.objects.new("PreviewCam", cam_data)
if cam.name not in scene.objects: scene.collection.objects.link(cam)
cam_data.lens = 50
yaw = math.radians(VIEW_YAW)
dist = size / (2 * math.tan(cam_data.angle / 2)) * 1.05
dirv = mathutils.Vector((math.sin(yaw), -math.cos(yaw), 0.35)).normalized()
cam.location = center + dirv * dist
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
scene.camera = cam
scene.render.engine = 'BLENDER_WORKBENCH'
sh = scene.display.shading
sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'; sh.show_object_outline = True; sh.object_outline_color = (0, 0, 0); sh.show_cavity = True
scene.render.resolution_x, scene.render.resolution_y = 1400, 800
scene.render.film_transparent = False
if scene.world is None: scene.world = bpy.data.worlds.new("PaperWorld")
scene.world.color = (0.95, 0.92, 0.84)
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
print("rendered", out)
