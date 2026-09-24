import bpy, mathutils
names = VIEW_NAMES
scene = bpy.context.scene
for area in bpy.context.window.screen.areas:
    if area.type == 'VIEW_3D':
        region = next(r for r in area.regions if r.type == 'WINDOW')
        with bpy.context.temp_override(area=area, region=region):
            bpy.ops.object.select_all(action='DESELECT')
            for n in names:
                root = scene.objects.get(n)
                if root:
                    root.select_set(True)
                    for c in root.children_recursive: c.select_set(True)
            bpy.ops.view3d.view_axis(type='FRONT')
            r3d = area.spaces.active.region_3d
            r3d.view_perspective = 'PERSP'
            r3d.view_rotation = mathutils.Euler((VIEW_PITCH, 0, VIEW_YAW)).to_quaternion()
            bpy.ops.view3d.view_selected()
            bpy.ops.object.select_all(action='DESELECT')
print("framed")
