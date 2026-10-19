"""
Turn a dressed MPFB master (build_character.py) into the game asset:

  1. drop MPFB's hidden helper geometry; bake the modelling targets into the base mesh;
  2. facial expression shape keys the game drives (Grin, Alarm, Determined, Blink) from MPFB's FACS units;
  3. delete the skin no one will ever see (under the shirt and sarong), so it can't poke through in motion;
  4. apply the garments' modifiers at game density; drop the eyes' transparent cornea shell;
  5. join everything opaque into one skinned mesh, unwrap it once and bake every Blender material into a
     single colour map and a single tangent-space normal map (one material, one draw call); the alpha cards
     (brows, lashes) stay a second mesh with their own maps;
  6. a lighter LOD1 (decimated, no expressions);
  7. FBX with the game_engine rig, Unity axes, plus the textures beside it.

  blender --background --python-exit-code 1 --python Tools/mpfb_characters/export_unity.py -- chr_pakmat [--out DIR] [--tex 4096]
"""
import sys, os, json, math, shutil
import bpy, bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

EXPRESSIONS = {
    # FACS-ish units from MPFB's targets/expression/units/<race>/, mixed into the four keys the game drives
    "Grin": {"mouth-corner-puller": 0.9, "mouth-elevation": 0.25, "mouth-parling": 0.2,
             "eye-left-slit": 0.35, "eye-right-slit": 0.35, "nose-left-elevation": 0.2, "nose-right-elevation": 0.2},
    "Alarm": {"eye-left-opened-up": 0.9, "eye-right-opened-up": 0.9, "eyebrows-left-up": 0.85, "eyebrows-right-up": 0.85,
              "mouth-open": 0.25, "mouth-depression-retraction": 0.3},
    "Determined": {"eyebrows-left-down": 0.8, "eyebrows-right-down": 0.8, "mouth-compression": 0.6,
                   "eye-left-slit": 0.25, "eye-right-slit": 0.25, "nose-left-dilatation": 0.3, "nose-right-dilatation": 0.3},
    "Blink": {"eye-left-closure": 1.0, "eye-right-closure": 1.0},
}


# The game's importer maps each humanoid bone to the transform of the same name (Hips -> Hips ...)
UNITY_NAMES = {
    "pelvis": "Hips", "spine_01": "Spine", "spine_02": "Chest", "neck_01": "Neck", "head": "Head",
    "clavicle_l": "LeftShoulder", "upperarm_l": "LeftUpperArm", "lowerarm_l": "LeftLowerArm", "hand_l": "LeftHand",
    "clavicle_r": "RightShoulder", "upperarm_r": "RightUpperArm", "lowerarm_r": "RightLowerArm", "hand_r": "RightHand",
    "thigh_l": "LeftUpperLeg", "calf_l": "LeftLowerLeg", "foot_l": "LeftFoot", "ball_l": "LeftToes",
    "thigh_r": "RightUpperLeg", "calf_r": "RightLowerLeg", "foot_r": "RightFoot", "ball_r": "RightToes",
}


def t_pose(rig, meshes, body):
    """MPFB rests in an A-pose; the game's humanoid clips assume a T-pose rest. Swing the arms out level,
    bake that into every mesh (shape keys carried along), and make it the rest pose."""
    from mathutils import Matrix
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='POSE')
    for side, sign in (("l", 1.0), ("r", -1.0)):
        for name in (f"upperarm_{side}", f"lowerarm_{side}", f"hand_{side}"):
            pb = rig.pose.bones[name]
            bpy.context.view_layer.update()
            cur = (pb.tail - pb.head).normalized()
            want = Vector((sign, 0.0, 0.0))
            R = cur.rotation_difference(want).to_matrix().to_4x4()
            h = pb.head.copy()
            pb.matrix = Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ pb.matrix
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    for ob in meshes:
        ev = ob.evaluated_get(dg).to_mesh()
        posed = [v.co.copy() for v in ev.vertices]
        ob.evaluated_get(dg).to_mesh_clear()
        if len(posed) != len(ob.data.vertices):
            print("TPOSE_SKIP", ob.name); continue
        keys = ob.data.shape_keys
        if keys:
            basis = [v.co.copy() for v in keys.key_blocks[0].data]
            for kb in keys.key_blocks:
                for i, d in enumerate(kb.data):
                    d.co = posed[i] + (d.co - basis[i])
        for i, v in enumerate(ob.data.vertices):
            v.co = posed[i]
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='POSE')
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode='OBJECT')


def rename_bones(rig, meshes):
    for old, new in UNITY_NAMES.items():
        if old in rig.data.bones:
            rig.data.bones[old].name = new
    for ob in meshes:                       # (in case the rename didn't reach the vertex groups)
        for old, new in UNITY_NAMES.items():
            g = ob.vertex_groups.get(old)
            if g and not ob.vertex_groups.get(new):
                g.name = new


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    cid = a[0] if a else "chr_pakmat"
    out = os.path.join(HERE, "out", cid, "export")
    tex = 4096
    if "--out" in a: out = a[a.index("--out") + 1]
    if "--tex" in a: tex = int(a[a.index("--tex") + 1])
    return cid, out, tex


def mpfb():
    import addon_utils
    addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
    from bl_ext.user_default.mpfb.services.targetservice import TargetService
    return TargetService


def keep_masked(body):
    """Delete the vertices the MASK modifier hides (MPFB's fitting helpers), keeping shape keys intact."""
    m = next((m for m in body.modifiers if m.type == 'MASK'), None)
    if not m:
        return
    g = body.vertex_groups[m.vertex_group].index
    bm = bmesh.new(); bm.from_mesh(body.data)
    dl = bm.verts.layers.deform.active
    kill = [v for v in bm.verts if (dl and v[dl].get(g, 0.0) > 0.5) == m.invert_vertex_group]
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(body.data); bm.free()
    body.modifiers.remove(m)


def expressions(body, race="asian"):
    if body.data.shape_keys and "Blink" in body.data.shape_keys.key_blocks:
        return                                  # made at build time (before the toon warp) already
    TS = mpfb()
    TS.bake_targets(body)                                  # the stylized face becomes the base
    body.shape_key_add(name="Basis", from_mix=False)
    root = os.path.join(os.environ["APPDATA"], "Blender Foundation", "Blender", "5.2", "extensions", "user_default",
                        "mpfb", "data", "targets", "expression", "units", race)
    units = sorted({u for e in EXPRESSIONS.values() for u in e})
    keys = {u: TS.load_target(body, os.path.join(root, u + ".target.gz"), weight=0.0, name="unit_" + u) for u in units}
    for name, mix in EXPRESSIONS.items():
        for u, k in keys.items():
            k.value = mix.get(u, 0.0)
        body.shape_key_add(name=name, from_mix=True)
    for k in keys.values():
        body.shape_key_remove(k)
    for k in body.data.shape_keys.key_blocks:
        k.value = 0.0


def evaluated_copy(ob):
    """The object's mesh with its modifiers applied (all but the armature), in world space."""
    arm = [m for m in ob.modifiers if m.type == 'ARMATURE']
    for m in arm: m.show_viewport = False
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    for m in arm: m.show_viewport = True
    return me


def bake_modifiers(ob, subdiv=None):
    """Apply the garment's modifiers (game density: subdivision capped), keep the armature."""
    for m in ob.modifiers:
        if m.type == 'SUBSURF' and subdiv is not None:
            m.levels = min(m.levels, subdiv)
            if m.levels == 0:
                m.show_viewport = False
    me = evaluated_copy(ob)
    old = ob.data
    ob.data = me
    for m in list(ob.modifiers):
        if m.type != 'ARMATURE':
            ob.modifiers.remove(m)
    bpy.data.meshes.remove(old)


def hide_covered(body, covers, margin_rings=2, reach=0.09, wrap_reach=0.3):
    """Delete body faces that sit right under a garment (a ray out along the normal hits cloth within reach),
    eroded by a couple of rings so nothing opens up at hems, cuffs and the collar."""
    FOUR = (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0)))
    wraps = []
    trees = []
    for c in covers:
        r = wrap_reach if any(k in c.name for k in ("sarong", "kain", "skirt")) else reach
        me = evaluated_copy(c)
        me.transform(c.matrix_world)
        bm = bmesh.new(); bm.from_mesh(me)
        tree = BVHTree.FromBMesh(bm)
        trees.append((tree, r))
        if r == wrap_reach:
            wraps.append(tree)
        bm.free(); bpy.data.meshes.remove(me)
    mw = body.matrix_world
    bm = bmesh.new(); bm.from_mesh(body.data)
    bm.faces.ensure_lookup_table()
    covered = set()
    for f in bm.faces:
        c = mw @ f.calc_center_median()
        n = (mw.to_3x3() @ f.normal).normalized()
        if any(t.ray_cast(c + n * 0.001, n, r)[0] is not None for t, r in trees):
            covered.add(f)
        elif any(all(t.ray_cast(c, d, 0.7)[0] is not None for d in FOUR) for t in wraps):
            covered.add(f)                     # inside a wrap skirt's tube (the legs, both of them)
    for _ in range(margin_rings):
        edge = {f for f in covered if any(g not in covered for e in f.edges for g in e.link_faces)}
        covered -= edge
    bmesh.ops.delete(bm, geom=list(covered), context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.to_mesh(body.data); bm.free()
    return len(covered)


def skirt_weights(ob, k=0.85, to_hips=0.3):
    """A wrap skirt (sarong, kain) is one tube, not trouser legs: blend each left/right leg weight toward their
    average (so a stride swings the tube rather than splitting it round each leg), fold the feet into the
    calves, and hand some of the leg weight to the pelvis."""
    pairs = [("thigh_l", "thigh_r"), ("calf_l", "calf_r")]
    feet = {"foot_l": "calf_l", "ball_l": "calf_l", "foot_r": "calf_r", "ball_r": "calf_r"}
    names = {g.name: g.index for g in ob.vertex_groups}
    for n in ("pelvis",) + tuple(x for p in pairs for x in p):
        if n not in names:
            names[n] = ob.vertex_groups.new(name=n).index
    groups = ob.vertex_groups
    for v in ob.data.vertices:
        w = {groups[g.group].name: g.weight for g in v.groups}
        for f, c in feet.items():
            if f in w:
                w[c] = w.get(c, 0) + w.pop(f)
        leg = 0.0
        for a, b in pairs:
            wa, wb = w.get(a, 0.0), w.get(b, 0.0)
            avg = (wa + wb) / 2
            w[a], w[b] = wa + (avg - wa) * k, wb + (avg - wb) * k
            leg += w[a] + w[b]
        shift = leg * to_hips
        for a, b in pairs:
            w[a] *= 1 - to_hips; w[b] *= 1 - to_hips
        w["pelvis"] = w.get("pelvis", 0) + shift
        total = sum(w.values()) or 1.0
        for name in list(feet):
            if name in names:
                groups[names[name]].remove([v.index])
        for name, val in w.items():
            if name in names:
                groups[names[name]].add([v.index], val / total, 'REPLACE')


def drop_cornea(eyes):
    """MPFB eyes carry a transparent outer shell; baked opaque it would cover the iris."""
    mat = eyes.material_slots[0].material if eyes.material_slots else None
    img = next((n.image for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image), None) if mat else None
    if not img or img.channels < 4:
        return
    w, h = img.size
    px = list(img.pixels)
    bm = bmesh.new(); bm.from_mesh(eyes.data)
    uv = bm.loops.layers.uv.active
    kill = []
    for f in bm.faces:
        u = sum((l[uv].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
        x, y = int((u.x % 1) * (w - 1)), int((u.y % 1) * (h - 1))
        if px[(y * w + x) * 4 + 3] < 0.5:
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bm.to_mesh(eyes.data); bm.free()


def explicit_uvmap(ob, name="UVMap"):
    """Make every material read its UVs from the named layer (so a second, bake layer can be active)."""
    for slot in ob.material_slots:
        m = slot.material
        if not m or not m.use_nodes:
            continue
        N, L = m.node_tree.nodes, m.node_tree.links
        uvn = N.new("ShaderNodeUVMap"); uvn.uv_map = name
        for n in list(N):
            if n.type == 'TEX_COORD':
                for l in list(n.outputs["UV"].links):
                    L.new(uvn.outputs[0], l.to_socket)
            if n.type == 'TEX_IMAGE' and not n.inputs["Vector"].is_linked:
                L.new(uvn.outputs[0], n.inputs["Vector"])


def bake(ob, out, size, cid):
    """One colour map and one normal map for everything on ob, on a fresh non-overlapping 'BakeUV'."""
    scn = bpy.context.scene
    scn.render.engine = 'CYCLES'
    scn.cycles.samples = 4
    scn.cycles.device = 'CPU'
    bake_uv = ob.data.uv_layers.new(name="BakeUV")
    ob.data.uv_layers.active = bake_uv
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002, area_weight=0.0, scale_to_bounds=False)
    # the face gets twice the texels: scale its islands up before packing
    bpy.ops.uv.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    _favour_head(ob, "BakeUV")
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(margin=0.002, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    maps = {}
    for kind, btype in (("albedo", 'DIFFUSE'), ("rough", 'ROUGHNESS'), ("ao", 'AO')):
        img = bpy.data.images.new(f"{cid}_{kind}", size, size, alpha=False)
        if kind != "albedo":
            img.colorspace_settings.name = 'Non-Color'
        for slot in ob.material_slots:
            m = slot.material
            N = m.node_tree.nodes
            t = N.get("__bake__") or N.new("ShaderNodeTexImage"); t.name = "__bake__"
            t.image = img
            N.active = t
            t.select = True
        if btype == 'DIFFUSE':
            scn.render.bake.use_pass_direct = False; scn.render.bake.use_pass_indirect = False; scn.render.bake.use_pass_color = True
            bpy.ops.object.bake(type='DIFFUSE', margin=8, uv_layer="BakeUV")
        else:
            scn.cycles.samples = 64 if btype == 'AO' else 4
            bpy.ops.object.bake(type=btype, margin=8, uv_layer="BakeUV")
        path = os.path.join(out, f"{cid}_{kind}.png")
        img.filepath_raw = path; img.file_format = 'PNG'; img.save()
        maps[kind] = path
        print("BAKED", kind, path)
    # eyeballs sit inside the lids and teeth inside the mouth: AO would darken them to slits and holes
    maps["noao"] = bake_mask(ob, out, size, cid, suffix="_noao",
                             colour=lambda n: (1, 1, 1) if any(k in n for k in ("high-poly", "low-poly", "teeth", "tongue", "eye")) else (0, 0, 0))
    return combine(maps, out, cid)


def bake_mask(ob, out, size, cid, colour=None, suffix="_mat_mask"):
    """Recolour mask for runtime costumes: R = the shirt/baju, G = the sarong/kain, B = the songkok.
    (AtlasRecolor shifts just those regions toward a costume's colours.)"""
    scn = bpy.context.scene
    img = bpy.data.images.new(f"{cid}{suffix}", size, size, alpha=False)
    img.colorspace_settings.name = 'Non-Color'
    originals = [s.material for s in ob.material_slots]
    for s in ob.material_slots:
        n = s.material.name.lower()
        col = colour(n) if colour else (1, 0, 0) if any(k in n for k in ("batik", "shirt", "baju")) and "button" not in n else               (0, 1, 0) if any(k in n for k in ("sarong", "kain")) else (0, 0, 1) if "songkok" in n else (0, 0, 0)
        m = bpy.data.materials.new("mask_" + n); m.use_nodes = True
        N = m.node_tree.nodes; N.clear()
        o = N.new("ShaderNodeOutputMaterial"); e = N.new("ShaderNodeEmission"); e.inputs[0].default_value = (*col, 1)
        m.node_tree.links.new(e.outputs[0], o.inputs[0])
        t = N.new("ShaderNodeTexImage"); t.image = img; N.active = t
        s.material = m
    scn.cycles.samples = 1
    bpy.ops.object.bake(type='EMIT', margin=8, uv_layer="BakeUV")
    path = os.path.join(out, f"{cid}{suffix}.png")
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    for s, m in zip(ob.material_slots, originals):
        s.material = m
    return path


def combine(maps, out, cid):
    """The game's CharacterSoft shader has no normal or AO maps and reads gloss from the base map's alpha
    (alpha <= 0.5 matte, 0.5..1 increasingly glossy). Fold AO into the colour and roughness into the alpha."""
    import numpy as np
    def load(p):
        im = bpy.data.images.load(p, check_existing=False)
        a = np.array(im.pixels[:], dtype=np.float32).reshape(im.size[1], im.size[0], 4)
        bpy.data.images.remove(im)
        return a
    alb, rough, ao = load(maps["albedo"]), load(maps["rough"]), load(maps["ao"])
    noao = load(maps["noao"])[..., :1] if "noao" in maps else 0.0
    occl = 0.7 + 0.3 * np.clip(ao[..., :1], 0, 1) ** 1.3                   # soft cavities, never black
    occl = occl * (1 - noao) + noao
    rgb = alb[..., :3] * occl
    gloss = np.clip((0.62 - rough[..., 0]) / 0.45, 0, 1)                    # roughness 0.62+ = matte
    a = np.where(gloss > 0.02, (128 + gloss * 127) / 255.0, 0.45)
    outp = np.concatenate([rgb, a[..., None]], -1)
    img = bpy.data.images.new(f"{cid}_mat", alb.shape[1], alb.shape[0], alpha=True)
    img.pixels = outp.ravel().tolist()
    path = os.path.join(out, f"{cid}_mat.png")
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    return {"albedo": path}


def _favour_head(ob, uv_name, scale=1.9):
    """Faces weighted to the head bone get bigger UV islands (more texels on the face)."""
    head = ob.vertex_groups.get("Head") or ob.vertex_groups.get("head")
    if not head:
        return
    bm = bmesh.new(); bm.from_mesh(ob.data)
    dl = bm.verts.layers.deform.active
    lay = bm.loops.layers.uv[uv_name]
    faces = [f for f in bm.faces if dl and sum(v[dl].get(head.index, 0) for v in f.verts) / len(f.verts) > 0.6]
    # grow each connected island of those faces about its own centre
    seen = set()
    for f in faces:
        if f in seen: continue
        isl, stack = [], [f]; seen.add(f)
        fs = set(faces)
        while stack:
            g = stack.pop(); isl.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h in fs and h not in seen and all((l[lay].uv - m[lay].uv).length < 1e-6 for l in g.loops for m in h.loops if l.vert == m.vert):
                        seen.add(h); stack.append(h)
        c = sum((l[lay].uv for g in isl for l in g.loops), Vector((0, 0))) / sum(len(g.loops) for g in isl)
        for g in isl:
            for l in g.loops:
                l[lay].uv = c + (l[lay].uv - c) * scale
    bm.to_mesh(ob.data); bm.free()


def final_material(ob, maps, name):
    mat = bpy.data.materials.new(name); mat.use_nodes = True
    N, L = mat.node_tree.nodes, mat.node_tree.links
    b = N["Principled BSDF"]
    a = N.new("ShaderNodeTexImage"); a.image = bpy.data.images.load(maps["albedo"], check_existing=True)
    L.new(a.outputs[0], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.6
    ob.data.materials.clear(); ob.data.materials.append(mat)
    for p in ob.data.polygons: p.material_index = 0
    mat.name = name


def only_uv(ob, keep):
    for u in list(ob.data.uv_layers):
        if u.name != keep:
            ob.data.uv_layers.remove(u)
    ob.data.uv_layers[0].name = "UVMap"


def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = o.data.name = name
    return o


def main():
    cid, out, size = args()
    os.makedirs(out, exist_ok=True)
    master = os.path.join(HERE, "out", cid, f"{cid}_master.blend")
    bpy.ops.wm.open_mainfile(filepath=master)
    rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    body = next(o for o in meshes if o.name.endswith(".body"))
    cards = [o for o in meshes if any(k in o.name for k in ("eyebrow", "eyelash"))]
    eyes = next((o for o in meshes if "high-poly" in o.name or "low-poly" in o.name), None)
    garments = [o for o in meshes if o not in cards and o is not body and o is not eyes
                and not any(k in o.name for k in ("teeth", "tongue"))]
    covers = [o for o in garments if any(k in o.name for k in ("batik", "shirt", "sarong", "kain", "baju"))]
    # (targets and expressions first: MPFB's target files index the full base mesh, helpers included)
    expressions(body)
    keep_masked(body)
    hidden = hide_covered(body, covers)
    print("HIDDEN_FACES", hidden)
    for o in garments:
        bake_modifiers(o, subdiv=1 if any(k in o.name for k in ("songkok", "moustache")) else 0)
    for o in garments:
        if any(k in o.name for k in ("sarong", "kain", "skirt")):
            skirt_weights(o)
    if eyes:
        drop_cornea(eyes)
    t_pose(rig, meshes, body)
    rename_bones(rig, meshes)
    opaque = [body] + [o for o in meshes if o not in cards and o is not body]
    for o in opaque + cards:
        explicit_uvmap(o)
    lod0 = join(opaque, f"{cid}_LOD0")
    maps = bake(lod0, out, size, cid)
    maps["mask"] = bake_mask(lod0, out, size, cid)
    only_uv(lod0, "BakeUV")
    final_material(lod0, maps, f"{cid}_mat")
    # the alpha cards: their own maps, copied beside the fbx
    card_maps = {}
    if cards:
        cobj = join(cards, f"{cid}_cards_LOD0")
        for slot in cobj.material_slots:
            m = slot.material
            kind = "brows" if "eyebrow" in m.name else "lashes"
            img = next((n.image for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.channels == 4), None)
            m.name = f"{cid}_{kind}"
            if img:
                dst = os.path.join(out, f"{cid}_{kind}.png")
                shutil.copyfile(bpy.path.abspath(img.filepath), dst)
                card_maps[m.name] = dst
        only_uv(cobj, "UVMap")
    # LOD1: no expressions, about a quarter of the triangles
    lod1 = lod0.copy(); lod1.data = lod0.data.copy(); lod1.name = lod1.data.name = f"{cid}_LOD1"
    bpy.context.scene.collection.objects.link(lod1)
    bpy.context.view_layer.objects.active = lod1
    lod1.shape_key_clear()
    dec = lod1.modifiers.new("decimate", 'DECIMATE'); dec.ratio = 0.28
    lod1.modifiers.move(len(lod1.modifiers) - 1, 0)
    bpy.ops.object.modifier_apply(modifier=dec.name)
    stats = {o.name: len([t for p in o.data.polygons for t in range(len(p.vertices) - 2)]) for o in bpy.data.objects if o.type == 'MESH'}
    stats["shape_keys"] = [k.name for k in lod0.data.shape_keys.key_blocks] if lod0.data.shape_keys else []
    stats["bones"] = [b.name for b in rig.data.bones]
    stats["maps"] = maps; stats["card_maps"] = card_maps
    # export: rig + LOD0 + LOD1 + cards
    import importlib
    sys.path.insert(0, os.path.join(HERE, "..", "blender", "kl"))
    objs = [rig] + [o for o in bpy.data.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    fbx = os.path.join(out, f"{cid}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', global_scale=1.0,
        axis_forward='Z', axis_up='Y', bake_space_transform=False,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=True,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        armature_nodetype='NULL', use_armature_deform_only=True, bake_anim=False,
        path_mode='STRIP', embed_textures=False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, f"{cid}_export.blend"))
    with open(os.path.join(out, "export_stats.json"), "w") as f:
        json.dump(stats, f, indent=2)
    print("EXPORT_OK", json.dumps({k: v for k, v in stats.items() if k not in ("bones",)}))


if __name__ == "__main__":
    main()
