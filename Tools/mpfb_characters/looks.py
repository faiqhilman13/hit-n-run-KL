"""
The stylized look on top of an MPFB human (runs inside Blender, after HumanService has built it):

- skin: MPFB's photo skin map is kept only as a soft detail layer (pores, lips, creases) over the
  character's own painted tone, with a little subsurface glow; no photo colour survives;
- hair, brows, lashes: tinted toward the spec's colour, alpha kept;
- eyes: dark brown irises;
- facial landmarks from the face's midline profile, so attachments (a moustache now, later a
  songkok, collar) sit on THIS face rather than at hand-tuned numbers;
- shells: geometry cut from the body's own surface and pushed out (a moustache here; clothes
  later), so they follow the anatomy exactly and inherit its skin weights.
"""
import bpy, bmesh
from mathutils import Vector


# ---------------------------------------------------------------- materials
def _nodes(mat):
    mat.use_nodes = True
    nt = mat.node_tree
    return nt, nt.nodes, nt.links


def find_image(mat):
    if not mat or not mat.use_nodes:
        return None
    best = None
    for n in mat.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image:
            nm = (n.image.name + n.label + n.name).lower()
            if "normal" in nm or "bump" in nm or "nm" == nm[-2:]:
                continue
            if best is None or "diffuse" in nm or "albedo" in nm:
                best = n.image
    return best


def stylized_skin(body, tone, blush=(0.70, 0.30, 0.25), detail=0.25):
    """Painted skin: tone x (soft detail from the MPFB map), redder where the map is redder (lips, cheeks)."""
    old = body.active_material
    img = find_image(old)
    mat = bpy.data.materials.new(body.name.split(".")[0] + "_skin")
    nt, N, L = _nodes(mat)
    N.clear()
    out = N.new("ShaderNodeOutputMaterial")
    bsdf = N.new("ShaderNodeBsdfPrincipled")
    L.new(bsdf.outputs[0], out.inputs[0])
    toneN = N.new("ShaderNodeRGB"); toneN.outputs[0].default_value = (*tone, 1)
    col = toneN.outputs[0]
    if img:
        tex = N.new("ShaderNodeTexImage"); tex.image = img
        bw = N.new("ShaderNodeRGBToBW"); L.new(tex.outputs[0], bw.inputs[0])
        # luminance around 1.0 so the tone stays the tone; detail sets how much the map's shading shows
        mr = N.new("ShaderNodeMapRange")
        mr.inputs["From Min"].default_value = 0.25; mr.inputs["From Max"].default_value = 0.95
        mr.inputs["To Min"].default_value = 1.0 - detail; mr.inputs["To Max"].default_value = 1.0 + detail * 0.45
        L.new(bw.outputs[0], mr.inputs["Value"])
        mul = N.new("ShaderNodeMix"); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs[0].default_value = 1.0
        L.new(col, mul.inputs[6]); L.new(mr.outputs[0], mul.inputs[7])
        col = mul.outputs[2]
        # redness: where the photo is redder than it is green (lips, cheeks, knuckles) lean toward blush
        sep = N.new("ShaderNodeSeparateColor"); L.new(tex.outputs[0], sep.inputs[0])
        red = N.new("ShaderNodeMath"); red.operation = 'SUBTRACT'; L.new(sep.outputs[0], red.inputs[0]); L.new(sep.outputs[1], red.inputs[1])
        k = N.new("ShaderNodeMapRange"); k.inputs["From Min"].default_value = 0.08; k.inputs["From Max"].default_value = 0.3
        k.inputs["To Max"].default_value = 0.55
        L.new(red.outputs[0], k.inputs["Value"])
        bl = N.new("ShaderNodeMix"); bl.data_type = 'RGBA'; bl.blend_type = 'MULTIPLY'
        b = N.new("ShaderNodeRGB"); b.outputs[0].default_value = (*[min(1, c / max(tone) * 1.0) for c in blush], 1)
        L.new(k.outputs[0], bl.inputs[0]); L.new(col, bl.inputs[6]); L.new(b.outputs[0], bl.inputs[7])
        col = bl.outputs[2]
    L.new(col, bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.52
    bsdf.inputs["Subsurface Weight"].default_value = 0.12
    bsdf.inputs["Subsurface Radius"].default_value = (1.0, 0.45, 0.25)
    bsdf.inputs["Subsurface Scale"].default_value = 0.012
    bsdf.inputs["Specular IOR Level"].default_value = 0.35
    body.data.materials.clear()
    body.data.materials.append(mat)
    for poly in body.data.polygons:          # MPFB uses several slots; everything is skin now
        poly.material_index = 0
    return mat


def tint(obj, color, keep_detail=0.6, roughness=0.55):
    """Recolour a hair/brow/lash/eye material: colour x detail of its own map; alpha untouched."""
    for slot in obj.material_slots:
        mat = slot.material
        if not mat:
            continue
        nt, N, L = _nodes(mat)
        bsdf = next((n for n in N if n.type == 'BSDF_PRINCIPLED'), None)
        if not bsdf:
            continue
        src = bsdf.inputs["Base Color"].links[0].from_socket if bsdf.inputs["Base Color"].is_linked else None
        c = N.new("ShaderNodeRGB"); c.outputs[0].default_value = (*color, 1)
        if src:
            bw = N.new("ShaderNodeRGBToBW"); L.new(src, bw.inputs[0])
            mr = N.new("ShaderNodeMapRange"); mr.inputs["To Min"].default_value = 1 - keep_detail; mr.inputs["To Max"].default_value = 1 + keep_detail * 0.5
            L.new(bw.outputs[0], mr.inputs["Value"])
            m = N.new("ShaderNodeMix"); m.data_type = 'RGBA'; m.blend_type = 'MULTIPLY'; m.inputs[0].default_value = 1
            L.new(c.outputs[0], m.inputs[6]); L.new(mr.outputs[0], m.inputs[7])
            L.new(m.outputs[2], bsdf.inputs["Base Color"])
        else:
            L.new(c.outputs[0], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = roughness


def iris(eyes, color=(0.16, 0.08, 0.035)):
    """MPFB eye: keep the white, darken the iris (it's the saturated part of the map)."""
    for slot in eyes.material_slots:
        mat = slot.material
        nt, N, L = _nodes(mat)
        bsdf = next((n for n in N if n.type == 'BSDF_PRINCIPLED'), None)
        if not bsdf or not bsdf.inputs["Base Color"].is_linked:
            continue
        src = bsdf.inputs["Base Color"].links[0].from_socket
        hsv = N.new("ShaderNodeSeparateColor"); hsv.mode = 'HSV'; L.new(src, hsv.inputs[0])
        # saturation picks out the iris; whites stay white
        k = N.new("ShaderNodeMapRange"); k.inputs["From Min"].default_value = 0.12; k.inputs["From Max"].default_value = 0.35
        L.new(hsv.outputs[1], k.inputs["Value"])
        c = N.new("ShaderNodeRGB"); c.outputs[0].default_value = (*color, 1)
        dark = N.new("ShaderNodeMix"); dark.data_type = 'RGBA'; dark.blend_type = 'MULTIPLY'; dark.inputs[0].default_value = 1
        bw = N.new("ShaderNodeRGBToBW"); L.new(src, bw.inputs[0])
        L.new(c.outputs[0], dark.inputs[6]); L.new(bw.outputs[0], dark.inputs[7])
        m = N.new("ShaderNodeMix"); m.data_type = 'RGBA'
        L.new(k.outputs[0], m.inputs[0]); L.new(src, m.inputs[6]); L.new(dark.outputs[2], m.inputs[7])
        L.new(m.outputs[2], bsdf.inputs["Base Color"])


# ---------------------------------------------------------------- landmarks
def evaluated_mesh(body):
    """The body as shown: shape targets applied, MPFB's hidden helper geometry masked away."""
    dg = bpy.context.evaluated_depsgraph_get()
    return body.evaluated_get(dg).to_mesh()


def head_k(body):
    """How much bigger than a realistic MPFB head this one is (the toon pass and height scaling): face
    measurements in metres are multiplied by it."""
    return float(body.get("head_k", 1.0))


def face_profile(body, half_width=0.006):
    """The face's midline profile, top to bottom: [(z, y)] (y is forward-negative: the face looks down -Y)."""
    mw = body.matrix_world
    pts = []
    me = evaluated_mesh(body)
    front = {}
    for v in me.vertices:
        w = mw @ v.co
        if abs(w.x) < half_width:
            # the frontmost point in each 1.5 mm slice (the midline also runs down the back of the head)
            k = round(w.z / 0.0015)
            if k not in front or w.y < front[k][1]:
                front[k] = (w.z, w.y)
    pts = sorted(front.values(), reverse=True)
    return pts


def landmarks(body):
    """Nose tip, subnasale, upper lip, mouth line and chin from the midline profile (world z / y)."""
    k = head_k(body)
    prof = face_profile(body, 0.006 * k)
    top = prof[0][0]
    # the face: the frontmost stretch of the head (from 6 cm under the crown down a head's depth)
    face = [(z, y) for z, y in prof if top - 0.34 * k < z < top - 0.06]
    tip = min(face, key=lambda p: p[1])
    # (only the face itself: a slice with no front vertex would report the back of the head)
    near = [(z, y) for z, y in face if y < tip[1] + 0.045 * k]
    # a 5-point median along the profile: nostril-rim and lid vertices otherwise pass for landmarks
    near.sort(reverse=True)
    near = [(near[i][0], sorted(p[1] for p in near[max(0, i - 2):i + 3])[min(2, len(near[max(0, i - 2):i + 3]) // 2)])
            for i in range(len(near))]

    def extreme(z_hi, z_lo, deepest):
        pts = [p for p in near if z_lo <= p[0] <= z_hi]
        return (max if deepest else min)(pts, key=lambda p: p[1])

    sub = extreme(tip[0] - 0.009 * k, tip[0] - 0.028 * k, True)           # deepest point under the nose
    lip = extreme(sub[0] - 0.006 * k, sub[0] - 0.025 * k, False)          # front of the upper lip
    mouth = extreme(lip[0] - 0.002 * k, lip[0] - 0.02 * k, True)          # where the lips meet
    return {"tip": tip, "subnasale": sub, "upper_lip": lip, "mouth": mouth, "top": (top, 0.0)}


# ---------------------------------------------------------------- shells
def shell_from_faces(body, name, pick, thickness=0.005, offset=0.0015, smooth=4, subdiv=1, keep_largest=True):
    """A new object from the body faces pick(center_world, normal_world) accepts, pushed out along normals."""
    mw = body.matrix_world
    bm = bmesh.new()
    bm.from_mesh(evaluated_mesh(body))
    bm.faces.ensure_lookup_table()
    keep = set()
    for f in bm.faces:
        c = mw @ f.calc_center_median()
        n = (mw.to_3x3() @ f.normal).normalized()
        if pick(c, n):
            keep.add(f.index)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    if keep_largest:
        islands, seen = [], set()
        for f in bm.faces:
            if f in seen:
                continue
            stack, isl = [f], []
            seen.add(f)
            while stack:
                g = stack.pop(); isl.append(g)
                for e in g.edges:
                    for h in e.link_faces:
                        if h not in seen:
                            seen.add(h); stack.append(h)
            islands.append(isl)
        if len(islands) > 1:
            big = max(islands, key=len)
            bmesh.ops.delete(bm, geom=[f for isl in islands if isl is not big for f in isl], context='FACES')
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    # same parent and placement as the body (the mesh is in the body's local space)
    ob.parent = body.parent
    ob.matrix_parent_inverse = body.matrix_parent_inverse.copy()
    ob.matrix_basis = body.matrix_basis.copy()
    # keep the body's skin weights: same vertex groups, copied by nearest surface
    for g in body.vertex_groups:
        ob.vertex_groups.new(name=g.name)
    dt = ob.modifiers.new("weights", 'DATA_TRANSFER')
    dt.object = body; dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}
    dt.vert_mapping = 'POLYINTERP_NEAREST'; dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.datalayout_transfer(modifier=dt.name)
    bpy.ops.object.modifier_apply(modifier=dt.name)
    if smooth:
        sm = ob.modifiers.new("smooth", 'SMOOTH'); sm.iterations = smooth; sm.factor = 0.5
    so = ob.modifiers.new("thickness", 'SOLIDIFY'); so.thickness = thickness; so.offset = 1.0; so.use_even_offset = False
    so.thickness_clamp = 1.0; so.use_quality_normals = True       # no spikes at folds (the lip curls into the mouth)
    if offset:
        # lift it off the skin a little before thickening
        disp = ob.modifiers.new("lift", 'DISPLACE'); disp.strength = offset; disp.mid_level = 0.0
        ob.modifiers.move(len(ob.modifiers) - 1, 0)
    if subdiv:
        sd = ob.modifiers.new("subdiv", 'SUBSURF'); sd.levels = subdiv; sd.render_levels = subdiv
    arm = next((o for o in [body.parent] if o and o.type == 'ARMATURE'), None)
    if arm:
        am = ob.modifiers.new("Armature", 'ARMATURE'); am.object = arm
    return ob


def moustache(body, color, width=0.042, droop=0.012):
    lm = landmarks(body)
    k = head_k(body)
    width, droop = width * k, droop * k
    z_top = lm["subnasale"][0] - 0.0015 * k
    z_bot = lm["upper_lip"][0] + 0.0015 * k          # stops above the lip: you still see the mouth
    front_y = lm["upper_lip"][1] + 0.022 * k

    def pick(c, n):
        if n.y > -0.25 or c.y > front_y:
            return False
        ax = abs(c.x)
        if ax > width:
            return False
        # a walrus-ish arc: full height at the middle, dipping toward the corners
        t = ax / width
        hi = z_top - t * t * 0.004 * k
        lo = z_bot - t * droop
        return lo < c.z < hi

    ob = shell_from_faces(body, body.name.split(".")[0] + ".moustache", pick, thickness=0.011 * k, offset=0.002 * k, smooth=10, subdiv=2)
    mat = bpy.data.materials.new("moustache")
    nt, N, L = _nodes(mat)
    bsdf = N["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = 0.6
    # fine strands: a stretched noise as bump
    tc = N.new("ShaderNodeTexCoord"); mp = N.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (900, 60, 900)
    nz = N.new("ShaderNodeTexNoise"); nz.inputs["Detail"].default_value = 6
    bump = N.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.6
    L.new(tc.outputs["Object"], mp.inputs[0]); L.new(mp.outputs[0], nz.inputs[0]); L.new(nz.outputs[0], bump.inputs["Height"])
    L.new(bump.outputs[0], bsdf.inputs["Normal"])
    ob.data.materials.append(mat)
    return ob, lm


# ---------------------------------------------------------------- toon pass (Upin & Ipin-ish warmth)
def _smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def head_parts(rig):
    """Objects that ride the head: eyes, brows, lashes, teeth, tongue, scalp hair."""
    return [o for o in rig.children if o.type == 'MESH' and not o.name.endswith(".body")]


def toonify(body, rig, head_scale=1.3, eye_scale=1.35, smooth_face=6, expressions=None):
    """Push MPFB's realistic head toward a warm cartoon: a bigger head (scaled about the neck, fading down
    the neck so the join stays soft), bigger eyes and sockets, and the face's small realistic forms relaxed
    away (that micro-anatomy is what reads as uncanny on a big-eyed face). Targets are baked first."""
    import addon_utils
    addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
    from bl_ext.user_default.mpfb.services.targetservice import TargetService
    TargetService.bake_targets(body)
    if expressions:
        expressions(body)
    bpy.context.view_layer.update()
    neck_b = rig.data.bones["neck_01"]
    mw_r = rig.matrix_world
    pivot = mw_r @ neck_b.head_local.lerp(neck_b.tail_local, 0.35)
    head_top_z = max((body.matrix_world @ v.co).z for v in body.data.vertices)
    eyes = next((o for o in head_parts(rig) if "high-poly" in o.name or "low-poly" in o.name), None)
    centres = []
    if eyes:
        ew = [eyes.matrix_world @ v.co for v in eyes.data.vertices]
        for side in (1, -1):
            pts = [p for p in ew if p.x * side > 0]
            c = sum(pts, Vector()) / len(pts)
            r = max((p - c).length for p in pts)
            centres.append((c, r))

    def head_weight(ob, v):
        g = ob.vertex_groups.get("head")
        if not g:
            return 0.0
        try:
            return g.weight(v.index)
        except RuntimeError:
            return 0.0

    def warp(p, w, rigid_eye):
        # head: everything weighted to the head bone grows about the neck pivot
        q = pivot + (p - pivot) * (1 + (head_scale - 1) * w)
        # eyes: the eyeballs themselves scale whole; the lids and sockets round them fade out
        for c, r in centres:
            cq = pivot + (c - pivot) * head_scale
            if rigid_eye:
                if (p - c).length < r * 1.2:
                    q = cq + (q - cq) * eye_scale
            else:
                f = 1.0 - _smoothstep(r * 0.9, r * 3.6, (q - cq).length)     # a long, soft fade: no fold rings
                q = cq + (q - cq) * (1 + (eye_scale - 1) * f)
        return q

    def move(ob, rigid_eye=False):
        mw = ob.matrix_world
        inv = mw.inverted()
        ws = [_smoothstep(0.15, 0.85, head_weight(ob, v)) if ob is body else 1.0 for v in ob.data.vertices]
        # every shape key goes through the same warp, so expressions stay fitted to the new face
        layers = [kb.data for kb in ob.data.shape_keys.key_blocks] if ob.data.shape_keys else []
        for layer in layers:
            for i, d in enumerate(layer):
                d.co = inv @ warp(mw @ d.co, ws[i], rigid_eye)
        for i, v in enumerate(ob.data.vertices):
            v.co = inv @ warp(mw @ v.co, ws[i], rigid_eye)

    move(body)
    for o in head_parts(rig):
        rigid = o is eyes or "eyelash" in o.name
        move(o, rigid_eye=rigid)
    if smooth_face:
        _relax_face(body, smooth_face)
    return pivot


def _relax_face(body, iterations):
    """Volume-preserving relax of the face only (front of the head), softening creases and micro-forms."""
    g = body.vertex_groups.get("head")
    bm = bmesh.new(); bm.from_mesh(body.data)
    dl = bm.verts.layers.deform.active
    mw = body.matrix_world
    if not g or not dl:
        bm.free(); return
    face = [v for v in bm.verts if v[dl].get(g.index, 0) > 0.9 and not v.is_boundary]
    before = [v.co.copy() for v in bm.verts]
    for _ in range(iterations):
        bmesh.ops.smooth_laplacian_vert(bm, verts=face, lambda_factor=0.35, lambda_border=0.0,
                                        use_x=True, use_y=True, use_z=True, preserve_volume=True)
    delta = [v.co - b for v, b in zip(bm.verts, before)]
    bm.to_mesh(body.data); bm.free()
    if body.data.shape_keys:                 # carry the same softening into every expression key
        for kb in body.data.shape_keys.key_blocks:
            for i, d in enumerate(kb.data):
                d.co += delta[i]


def toon_eyes(eyes, tex_path):
    """Swap the eye texture for the cartoon one (same layout)."""
    img = bpy.data.images.load(tex_path, check_existing=True)
    for slot in eyes.material_slots:
        for n in slot.material.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image and "eye" in n.image.name.lower():
                n.image = img


def brows(body, brow_obj, color, grow=0.006, thickness=0.004, gap=0.011, lift=0.0045):
    """Bold cartoon brows: the face skin under MPFB's brow card (grown a few millimetres all round) pushed out
    into a solid shell, like the moustache; the hair card itself is deleted."""
    from mathutils.kdtree import KDTree
    pts = [brow_obj.matrix_world @ v.co for v in brow_obj.data.vertices]
    kd = KDTree(len(pts))
    for i, p in enumerate(pts):
        kd.insert(p, i)
    kd.balance()
    k = head_k(body)
    def pick(c, n):
        return n.y < 0.3 and abs(c.x) > gap * k and kd.find(c)[2] < grow * k     # two brows, not one
    ob = shell_from_faces(body, body.name.split(".")[0] + ".brows", pick, thickness=thickness * k, offset=0.0012 * k,
                          smooth=10, subdiv=2, keep_largest=False)
    for v in ob.data.vertices:                 # a touch higher: open, friendly, not frowning over the eyes
        v.co.z += lift * k / max(1e-6, ob.matrix_world.to_scale().z)
    mat = bpy.data.materials.new("brows_solid"); mat.use_nodes = True
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1); b.inputs["Roughness"].default_value = 0.65
    ob.data.materials.append(mat)
    bpy.data.objects.remove(brow_obj, do_unlink=True)
    return ob


def blush(body, centres, radius, color=(0.86, 0.36, 0.30), strength=0.38):
    """Painted cheek blush: soft round patches mixed into the skin colour around the given world points."""
    mat = body.active_material
    N, L = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(n for n in N if n.type == 'BSDF_PRINCIPLED')
    src = bsdf.inputs["Base Color"].links[0].from_socket
    geo = N.new("ShaderNodeNewGeometry")
    fac = None
    for c in centres:
        d = N.new("ShaderNodeVectorMath"); d.operation = 'DISTANCE'
        L.new(geo.outputs["Position"], d.inputs[0]); d.inputs[1].default_value = tuple(c)
        mr = N.new("ShaderNodeMapRange"); mr.inputs["From Min"].default_value = 0.0
        mr.inputs["From Max"].default_value = radius; mr.inputs["To Min"].default_value = strength; mr.inputs["To Max"].default_value = 0.0
        mr.interpolation_type = 'SMOOTHSTEP'
        L.new(d.outputs["Value"], mr.inputs["Value"])
        if fac is None:
            fac = mr.outputs[0]
        else:
            mx = N.new("ShaderNodeMath"); mx.operation = 'MAXIMUM'
            L.new(fac, mx.inputs[0]); L.new(mr.outputs[0], mx.inputs[1]); fac = mx.outputs[0]
    mix = N.new("ShaderNodeMix"); mix.data_type = 'RGBA'; mix.blend_type = 'MIX'
    c = N.new("ShaderNodeRGB"); c.outputs[0].default_value = (*color, 1)
    L.new(fac, mix.inputs[0]); L.new(src, mix.inputs[6]); L.new(c.outputs[0], mix.inputs[7])
    L.new(mix.outputs[2], bsdf.inputs["Base Color"])


def cheek_points(rig, k):
    """Where the cheeks are: under and a little outside each eye."""
    eyes = next((o for o in rig.children if "high-poly" in o.name or "low-poly" in o.name), None)
    if not eyes:
        return []
    ew = [eyes.matrix_world @ v.co for v in eyes.data.vertices]
    out = []
    for side in (1, -1):
        pts = [p for p in ew if p.x * side > 0]
        c = sum(pts, Vector()) / len(pts)
        out.append(c + Vector((0.016 * k * side, -0.006 * k, -0.05 * k)))
    return out
