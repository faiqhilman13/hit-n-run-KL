# Runs INSIDE the user's live Blender: builds a KL character in its own scene (no export)
# and renders a face close-up for review.  Edit ASSET below or set via exec globals.
import bpy, sys, os, importlib, math
KL = r"C:\Users\User\PROJECTS\kampung-game\KampungRun\Tools\blender\kl"
if KL not in sys.path:
    sys.path.insert(0, KL)
import kl_core as K, kl_humanoid as H, kl_characters as C
for mod in (K, H, C):
    importlib.reload(mod)
from mathutils import Vector

ASSET = globals().get("ASSET", "chr_aiman")
builder = {"chr_aiman": C.build_aiman}[ASSET]
scene, cols = K.fresh_scene(ASSET + "_live")
img = K.write_atlas(os.path.join(K.UNITY_OUT, K.ATLAS_NAME))
mat = K.atlas_material(img)
m, p = builder()
ob, weights, tags = m.to_object(ASSET, mat, cols["EXPORT"])
arm = H.build_armature(ASSET, p, cols["EXPORT"])
H.skin(ob, arm, weights)
H.shape_keys(ob, tags, C.FACE_KEYS)
H.author_clips(arm, ASSET)

# face close-up render
cam_data = bpy.data.cameras.new("FaceCam")
cam = bpy.data.objects.new("FaceCam", cam_data)
scene.collection.objects.link(cam)
head_z = p["head_z"] + p["head"] * 0.45
cam.location = (0.25, -0.9, head_z + 0.05)
cam.rotation_euler = (Vector((0, 0, head_z)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 85
scene.camera = cam
scene.render.engine = 'BLENDER_WORKBENCH'
sh = scene.display.shading
sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_object_outline = True
scene.render.resolution_x, scene.render.resolution_y = 700, 700
scene.render.filepath = r"C:\Users\User\PROJECTS\kampung-game\KampungRun\Tools\previews\live_face.png"
bpy.ops.render.render(write_still=True)
print("face rendered")
