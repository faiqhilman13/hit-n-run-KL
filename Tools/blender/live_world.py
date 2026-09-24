# Runs INSIDE the user's live Blender (via blender-mcp): builds a street-life showcase
# of the refined buildings + props in its own scene.
import bpy, random
path = r"C:\Users\User\PROJECTS\kampung-game\KampungRun\Tools\blender\build_assets.py"
ns = {"__name__": "kampung_assets", "__file__": path}
exec(compile(open(path, encoding="utf-8").read(), path, "exec"), ns)

scene = bpy.data.scenes.get("KampungRun_World") or bpy.data.scenes.new("KampungRun_World")
bpy.context.window.scene = scene
ns["clear_scene"]()
single = ns["single"]

def place(name, fn, loc, rot_z=0.0):
    r = single(name, fn)
    r.location = loc
    r.rotation_euler = (0, 0, rot_z)
    return r

place("House", lambda p: ns["kampung_house"](p, "HouseBlue", "RoofRed"), (-14, 0, 0))
place("House2", lambda p: ns["kampung_house"](p, "HouseYellow", "RoofGreen"), (-26, 3, 0))
place("Shops", lambda p: ns["shophouse"](p, ["Pastel1", "Pastel2", "Pastel3"]), (6, 2, 0))
place("Condo", lambda p: ns["condo"](p, random.Random(1), "Pastel6"), (30, 12, 0))
place("Chicken", ns["chicken"], (-12, -8, 0), 0.6)
place("Chicken2", ns["chicken"], (-11, -8.6, 0), -0.4)
place("Cat", ns["cat"], (2, -9, 0), 0.8)
place("Kapcai", lambda p: ns["kapcai"](p, "CarRed"), (1, -8.5, 0), 1.4)
place("Kapcai2", lambda p: ns["kapcai"](p, "CarBlue"), (3.5, -8.5, 0), 1.6)
place("Satay", ns["satay_cart"], (10, -9, 0))
place("BusStop", ns["bus_stop"], (18, -9, 0))
place("TL", ns["traffic_light"], (-4, -10, 0), 1.57)
place("Bunting", ns["bunting"], (6, -10.5, 0))
place("Bin", ns["bin_"], (14, -8.5, 0))
place("Bench", ns["bench"], (22, -8.5, 0))
place("Angsana", lambda p: ns["angsana"](p, random.Random(31)), (-4, 6, 0))
print("world showcase", len(scene.objects))
