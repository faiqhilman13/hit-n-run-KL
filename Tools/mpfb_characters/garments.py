"""
Clothes for an MPFB character, made the way a character artist would kitbash them, but in code:

- shirts are cut from the body's own surface (so they start fitted to THIS body), puffed out, then
  dropped onto the body with Blender's cloth solver for a few dozen frames: the folds, the hang over a
  belly and the sleeves' weight are real drape, not painted;
- wrap garments (sarong, kain) are tubes sized from the body's actual cross-sections, draped the same way
  with the waistband pinned;
- hats are fitted to the measured oval of the head; sandals to the footprint.

Everything ends skinned to the same rig (weights copied from the nearest body surface).
Runs inside Blender after build_body.build().
"""
import math, os
import bpy, bmesh
from mathutils import Vector, Matrix

import looks

HERE = os.path.dirname(os.path.abspath(__file__))
TEX = os.path.join(HERE, "..", "scenario_characters", "textures", "local_styles", "D")


# ---------------------------------------------------------------- rig + body helpers
def bone(rig, name):
    b = rig.data.bones[name]
    return rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local


ARM_BONES = ("upperarm", "lowerarm", "hand", "thumb", "index", "middle", "ring", "pinky")


def body_points(body, no_arms=False):
    """The body's vertices in world space (optionally without the arms: in the A-pose they hang beside the hips)."""
    me = looks.evaluated_mesh(body)
    mw = body.matrix_world
    if not no_arms:
        return [mw @ v.co for v in me.vertices]
    arm = {g.index for g in body.vertex_groups if g.name.startswith(ARM_BONES)}
    out = []
    for v in me.vertices:
        w = sum(g.weight for g in v.groups if g.group in arm)
        if w < 0.3:
            out.append(mw @ v.co)
    return out


def finish(ob, body, rig, solidify=0.0035, subdiv=1, clamp=True):
    """Weights from the nearest body surface, a little thickness, the rig."""
    for g in body.vertex_groups:
        if g.name not in ob.vertex_groups:
            ob.vertex_groups.new(name=g.name)
    dt = ob.modifiers.new("weights", 'DATA_TRANSFER')
    dt.object = body; dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}
    dt.vert_mapping = 'POLYINTERP_NEAREST'; dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.datalayout_transfer(modifier=dt.name)
    bpy.ops.object.modifier_apply(modifier=dt.name)
    if solidify:
        so = ob.modifiers.new("thickness", 'SOLIDIFY'); so.thickness = solidify; so.offset = 1.0
        so.use_even_offset = False; so.thickness_clamp = 1.0 if clamp else 0.0; so.use_quality_normals = True
        so.use_rim = True
    if subdiv:
        sd = ob.modifiers.new("subdiv", 'SUBSURF'); sd.levels = subdiv; sd.render_levels = subdiv
    am = ob.modifiers.new("Armature", 'ARMATURE'); am.object = rig
    ob.parent = rig
    ob.matrix_parent_inverse = rig.matrix_world.inverted()
    return ob


def new_object(name, me):
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


# ---------------------------------------------------------------- materials
OWN_TEX = os.path.join(HERE, "textures")


def _img(name):
    for d in (OWN_TEX, TEX):
        p = os.path.join(d, name)
        if os.path.exists(p):
            return bpy.data.images.load(p, check_existing=True)
    return None


def fabric(name, base, normal=None, orm=None, scale=4.0, tint=None, sheen=0.0, roughness=None, normal_strength=0.6):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    N, L = mat.node_tree.nodes, mat.node_tree.links
    bsdf = N["Principled BSDF"]
    tc = N.new("ShaderNodeTexCoord"); mp = N.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (scale, scale, scale)
    L.new(tc.outputs["UV"], mp.inputs[0])

    def tex(fn, non_color=False):
        img = _img(fn)
        if not img:
            return None
        if non_color:
            img.colorspace_settings.name = 'Non-Color'
        t = N.new("ShaderNodeTexImage"); t.image = img
        L.new(mp.outputs[0], t.inputs[0])
        return t

    b = tex(base)
    if b:
        col = b.outputs[0]
        if tint:
            m = N.new("ShaderNodeMix"); m.data_type = 'RGBA'; m.blend_type = 'MULTIPLY'; m.inputs[0].default_value = 1
            c = N.new("ShaderNodeRGB"); c.outputs[0].default_value = (*tint, 1)
            L.new(col, m.inputs[6]); L.new(c.outputs[0], m.inputs[7]); col = m.outputs[2]
        L.new(col, bsdf.inputs["Base Color"])
    if normal:
        n = tex(normal, True)
        if n:
            nm = N.new("ShaderNodeNormalMap"); nm.inputs["Strength"].default_value = normal_strength
            L.new(n.outputs[0], nm.inputs["Color"]); L.new(nm.outputs[0], bsdf.inputs["Normal"])
    if orm:
        o = tex(orm, True)
        if o:
            sep = N.new("ShaderNodeSeparateColor"); L.new(o.outputs[0], sep.inputs[0])
            L.new(sep.outputs[1], bsdf.inputs["Roughness"])
    if roughness is not None:
        for l in list(bsdf.inputs["Roughness"].links):
            L.remove(l)
        bsdf.inputs["Roughness"].default_value = roughness
    if sheen:
        bsdf.inputs["Sheen Weight"].default_value = sheen
        bsdf.inputs["Sheen Roughness"].default_value = 0.4
    return mat


# ---------------------------------------------------------------- cloth draping
def drape(ob, body, pin_group=None, frames=36, mass=0.25, stiffness=12.0, shrink=0.0, pressure=0.0, bending=0.15, self_collide=False, relax=6, under=()):
    """Drop a garment onto the body with the cloth solver and keep the settled shape. The cloth lands on a
    smoothed stand-in of the body (fabric bridges over nipples, navel and the like, as it does on a person)."""
    col = body.modifiers.get("Collision") or body.modifiers.new("Collision", 'COLLISION')
    body.collision.thickness_outer = 0.006
    body.collision.cloth_friction = 1.0
    # and anything already worn underneath (a shirt worn out over the sarong lands on the sarong)
    under_cols = []
    for u in under:
        uc = u.modifiers.new("Collision", 'COLLISION'); u.collision.thickness_outer = 0.004
        under_cols.append((u, uc))
    cl = ob.modifiers.new("Cloth", 'CLOTH')
    s = cl.settings
    s.quality = 8
    s.mass = mass
    s.tension_stiffness = stiffness; s.compression_stiffness = stiffness; s.shear_stiffness = stiffness * 0.5
    s.bending_stiffness = bending
    s.air_damping = 2.0
    s.shrink_min = shrink
    if pin_group:
        s.vertex_group_mass = pin_group
    if pressure:
        s.use_pressure = True; s.uniform_pressure_force = pressure
    c = cl.collision_settings
    c.use_collision = True; c.distance_min = 0.004; c.use_self_collision = self_collide; c.self_distance_min = 0.003
    c.collision_quality = 4
    cl.point_cache.frame_start = 1; cl.point_cache.frame_end = frames
    scn = bpy.context.scene
    scn.frame_start, scn.frame_end = 1, frames
    for f in range(1, frames + 1):
        scn.frame_set(f)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=cl.name)
    scn.frame_set(1)
    body.modifiers.remove(col)
    for u, uc in under_cols:
        u.modifiers.remove(uc)
    if relax:
        # fabric bridges over small bumps (nipples, navel): relax the settled cloth, keeping its volume
        bm = bmesh.new(); bm.from_mesh(ob.data)
        inner = [v for v in bm.verts if not v.is_boundary]
        for _ in range(relax):
            bmesh.ops.smooth_laplacian_vert(bm, verts=inner, lambda_factor=0.5, lambda_border=0.0,
                                            use_x=True, use_y=True, use_z=True, preserve_volume=True)
        bm.normal_update()
        for v in inner:
            v.co += v.normal * 0.002
        bm.to_mesh(ob.data); bm.free()


def _collider(body, iterations=8, inflate=0.006):
    me = looks.evaluated_mesh(body).copy()
    me.transform(body.matrix_world)
    ob = new_object("collider_proxy", me)
    bm = bmesh.new(); bm.from_mesh(me)
    # volume-preserving (plain smoothing shrinks the belly and chest, and the body pokes through the cloth)
    for _ in range(iterations):
        bmesh.ops.smooth_laplacian_vert(bm, verts=bm.verts, lambda_factor=0.4, lambda_border=0.0,
                                        use_x=True, use_y=True, use_z=True, preserve_volume=True)
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * inflate
    bm.to_mesh(me); bm.free()
    return ob


# ---------------------------------------------------------------- shirt
def shirt(body, rig, name="shirt", sleeve=0.5, hem_drop=0.09, puff=0.02, collar=True, buttons=True, tint=None,
          v_depth=0.075, button_color=(0.85, 0.8, 0.7), under=(), pattern=None, pattern_scale=3.0):
    """A loose short-sleeved shirt worn out over the waist (batik kemeja): open V neck, collar, buttons."""
    neck, _ = bone(rig, "neck_01")
    thigh, _ = bone(rig, "thigh_l")
    cut_z = thigh.z + 0.01                      # above the crotch: one opening, not two leg holes
    hem_z = thigh.z - hem_drop                  # the hem is then let down to here
    neck_z = neck.z - 0.004
    arms = {s: bone(rig, f"upperarm_{s}") for s in ("l", "r")}
    clav = {s: bone(rig, f"clavicle_{s}")[1] for s in ("l", "r")}
    axis_y = neck.y

    def pick(c, n):
        if c.z < cut_z:
            return False
        side = "l" if c.x > 0 else "r"
        h, t = arms[side]
        if abs(c.x) > abs(clav[side].x) - 0.01:
            # on the arm: keep the top of it, as far as the sleeve reaches
            axis = t - h
            u = (c - h).dot(axis) / axis.length_squared
            return u < sleeve
        if c.z > neck_z:
            return False
        # the open V at the front of the neck
        if c.y < axis_y and c.z > neck_z - v_depth and abs(c.x) < (c.z - (neck_z - v_depth)) * 0.42:
            return False
        return True

    ob = _cut(body, name, pick)
    # fabric, not skin: smooth the body's detail away (chest, navel), then give it some air
    pelvis, _ = bone(rig, "pelvis")
    # a little more air over the hips: the hem hangs free of the body
    _push(ob, lambda p: puff + (0.02 if p.z < pelvis.z + 0.12 else 0.0))
    # held at the yoke (neck and shoulders), letting go gradually down the chest and back
    _pin(ob, "pin", lambda p: (max(0.0, min(1.0, (p.z - (neck_z - 0.11)) / 0.06))) if abs(p.x) < abs(clav["l"].x) + 0.03 else 0.0)
    drape(ob, body, "pin", frames=24, mass=0.2, stiffness=15.0, bending=0.6, under=under)
    if collar:
        _collar(ob, neck, neck_z - v_depth - 0.01)
    if buttons:
        _buttons(ob, neck_z - v_depth, hem_z + 0.05)
    ob["hem_z"] = hem_z
    finish(ob, body, rig, solidify=0.004)
    if pattern:
        ob.data.materials.append(fabric(name + "_batik", f"batik_{pattern}_basecolor.png", f"batik_{pattern}_normal.png", None,
                                        scale=pattern_scale, sheen=0.25, roughness=0.78, normal_strength=0.35))
    else:
        ob.data.materials.append(fabric(name + "_batik", "batik_basecolor.png", "batik_normal.png", "batik_orm.png", scale=5.0, tint=tint, sheen=0.25))
    btn = bpy.data.materials.new(name + "_buttons"); btn.use_nodes = True
    bs = btn.node_tree.nodes["Principled BSDF"]
    bs.inputs["Base Color"].default_value = (*button_color, 1); bs.inputs["Roughness"].default_value = 0.25
    bs.inputs["Coat Weight"].default_value = 0.5
    ob.data.materials.append(btn)
    return ob


def _collar(ob, neck, low_z, stand=0.018, fall=0.036, segs=72):
    """A clean collar: the neckline (round the back and down both sides of the V) resampled into a smooth
    curve, then lofted up (the stand) and folded out over the shirt (the fall, opening into lapels at the front)."""
    me = ob.data
    bm = bmesh.new(); bm.from_mesh(me)
    pts = [v.co.copy() for e in bm.edges if e.is_boundary and all(v.co.z > low_z for v in e.verts)
           and all(abs(v.co.x - neck.x) < 0.14 for v in e.verts) for v in e.verts]
    if len(pts) < 8:
        bm.free(); return
    # order round the neck: angle from the front (0 at the V's bottom on the left, round the back, to the right)
    def ang(p):
        a = math.atan2(p.x - neck.x, -(p.y - neck.y))     # 0 = straight ahead
        return a if a >= 0 else a + 2 * math.pi
    pts.sort(key=ang)
    a0, a1 = ang(pts[0]), ang(pts[-1])
    curve = []
    for i in range(segs + 1):
        t = a0 + (a1 - a0) * i / segs
        near = sorted(pts, key=lambda p: abs(ang(p) - t))[:6]   # average the boundary points nearest this angle
        curve.append(sum(near, Vector()) / len(near))
    for _ in range(4):                                          # and smooth the curve itself
        curve = [curve[0]] + [(curve[i - 1] + curve[i] * 2 + curve[i + 1]) / 4 for i in range(1, len(curve) - 1)] + [curve[-1]]
    def out_dir(co):
        d = Vector((co.x - neck.x, co.y - neck.y, 0))
        if co.y < neck.y:
            d.y *= 0.3
        return d.normalized() if d.length > 1e-5 else Vector((1, 0, 0))
    rows = []
    for c in curve:
        o = out_dir(c)
        front = max(0.0, min(1.0, (neck.y - c.y) / 0.05))       # lapels at the front lie flatter, longer
        rows.append([c + o * -0.002,
                     c + Vector((0, 0, stand * (1 - 0.5 * front))) + o * 0.003,
                     c + Vector((0, 0, stand * 0.6)) + o * (fall * 0.55),
                     c + o * (fall * (1 + 0.5 * front)) + Vector((0, 0, -fall * 0.55))])
    vs = [[bm.verts.new(p) for p in r] for r in rows]
    for i in range(len(vs) - 1):
        for j in range(3):
            f = bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
            f.smooth = True
    bm.to_mesh(me); bm.free()


def _buttons(ob, top_z, bottom_z, spacing=0.075, radius=0.0062):
    """A placket's worth of buttons, sat on the draped front (found by casting at the shirt from in front)."""
    from mathutils.bvhtree import BVHTree
    bm = bmesh.new(); bm.from_mesh(ob.data)
    tree = BVHTree.FromBMesh(bm)
    mat_index = 1
    z = top_z - 0.012
    while z > bottom_z:
        hit, normal, _, _ = tree.ray_cast(Vector((0.0, -1.0, z)), Vector((0, 1, 0)))
        if hit:
            n = normal if normal.y < 0 else -normal
            rot = n.to_track_quat('Z', 'Y').to_matrix().to_4x4()
            m = Matrix.Translation(hit + n * 0.0015) @ rot
            res = bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=radius, radius2=radius * 0.85, depth=0.0025, matrix=m)
            for v in res["verts"]:
                for f in v.link_faces:
                    f.material_index = mat_index
        z -= spacing
    bm.to_mesh(ob.data); bm.free()


def _even(ob, iterations=25, factor=0.5):
    """Even out the spacing of the vertices along the surface (tangential relaxation): MPFB's mesh is dense
    round the nipples and navel, and the cloth solver knots a cluster of tiny faces. The shape is kept."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    inner = [v for v in bm.verts if not v.is_boundary and v.link_edges]
    for _ in range(iterations):
        bm.normal_update()
        moves = []
        for v in inner:
            avg = sum((e.other_vert(v).co for e in v.link_edges), Vector()) / len(v.link_edges)
            d = avg - v.co
            d -= v.normal * d.dot(v.normal)
            moves.append((v, d * factor))
        for v, d in moves:
            v.co += d
    bm.to_mesh(ob.data); bm.free()


def _relax(ob, iterations):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    inner = [v for v in bm.verts if not v.is_boundary]
    for _ in range(iterations):
        bmesh.ops.smooth_laplacian_vert(bm, verts=inner, lambda_factor=0.5, lambda_border=0.0,
                                        use_x=True, use_y=True, use_z=True, preserve_volume=True)
    bm.to_mesh(ob.data); bm.free()


def _let_down(ob, cut_z, hem_z, rows=3):
    """Extend the bottom opening straight down to the hem (flaring a touch), in a few rows for the cloth to drape."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    edges = [e for e in bm.edges if e.is_boundary and all(v.co.z < cut_z + 0.03 for v in e.verts)]
    if not edges:
        bm.free(); return
    centre = sum((v.co for e in edges for v in e.verts), Vector()) / (2 * len(edges))
    step = (cut_z - hem_z) / rows
    for r in range(rows):
        ret = bmesh.ops.extrude_edge_only(bm, edges=edges)
        nv = [v for v in ret["geom"] if isinstance(v, bmesh.types.BMVert)]
        for v in nv:
            out = Vector((v.co.x - centre.x, v.co.y - centre.y, 0))
            v.co += Vector((0, 0, -step)) + (out.normalized() * 0.006 if out.length > 1e-5 else Vector())
        edges = [e for e in ret["geom"] if isinstance(e, bmesh.types.BMEdge) and all(v in nv for v in e.verts)]
    bm.to_mesh(ob.data); bm.free()


def _smooth(ob, iterations, factor):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    inner = [v for v in bm.verts if not v.is_boundary]
    for _ in range(iterations):
        bmesh.ops.smooth_vert(bm, verts=inner, factor=factor, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(ob.data); bm.free()


# ---------------------------------------------------------------- sarong / long skirt
def wrap_skirt(body, rig, name="sarong", top_above_hip=0.07, bottom_above_ankle=0.04, ease=0.035, flare=0.06,
               base="sarong_basecolor.png", normal="sarong_normal.png", orm="sarong_orm.png", tint=None, scale=3.0,
               over=None):
    """A tube from the waist to the ankles, as wide as the hips and legs need, draped. Worn under a shirt (over),
    it starts just beneath the shirt's hem, so the waistband (hidden anyway) never fights the shirt."""
    pelvis, _ = bone(rig, "pelvis")
    foot, _ = bone(rig, "foot_l")
    top_z = pelvis.z + top_above_hip
    if over is not None and "hem_z" in over:
        top_z = min(top_z, over["hem_z"] + 0.10)
    bot_z = foot.z + bottom_above_ankle
    pts = body_points(body, no_arms=True)
    rings, segs = [], 56
    zs = [top_z - (top_z - bot_z) * i / 14 for i in range(15)]
    rings_r = []
    cx, cy = 0.0, sum(p.y for p in pts if abs(p.z - top_z) < 0.02) / max(1, sum(1 for p in pts if abs(p.z - top_z) < 0.02))
    for i, z in enumerate(zs):
        sl = [p for p in pts if abs(p.z - z) < 0.012]
        # radius in every direction: the furthest body point that way (both legs together below the crotch)
        r_at = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            d = Vector((math.cos(a), math.sin(a), 0))
            r = max([((p.x - cx) * d.x + (p.y - cy) * d.y) for p in sl] or [0.15])
            r_at.append(r)
        # smooth the outline round the ring (no notch between the legs)
        sm = [sum(r_at[(k + j) % segs] for j in range(-2, 3)) / 5 for k in range(segs)]
        # cloth hangs straight down from the widest point above it (the hips, the belly), tapering a touch
        if rings_r:
            sm = [max(a, b * (1.0 - 0.012)) for a, b in zip(sm, rings_r[-1])]
        rings_r.append(sm)
        t = i / 14
        e = ease * (0.3 if i == 0 else 0.65 if i == 1 else 1.0)     # snug at the waistband (under the shirt)
        rings.append([Vector((cx + math.cos(2 * math.pi * k / segs) * (sm[k] + e + flare * t * t),
                              cy + math.sin(2 * math.pi * k / segs) * (sm[k] + e + flare * t * t), z)) for k in range(segs)])
    me = bpy.data.meshes.new(name)
    verts = [v for ring in rings for v in ring]
    faces = []
    for i in range(len(rings) - 1):
        for k in range(segs):
            a, b = i * segs + k, i * segs + (k + 1) % segs
            faces.append((a, b, b + segs, a + segs))
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.update()
    # cylindrical UVs: the plaid runs round and down
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            i, k = divmod(vi, segs)
            u = k / segs
            if k == 0 and any(me.loops[l].vertex_index % segs == segs - 1 for l in poly.loop_indices):
                u = 1.0
            uv.data[li].uv = (u * 2.0, 1 - i / 14)
    ob = new_object(name, me)
    _subdivide(ob, 1)
    _pin(ob, "pin", lambda p: p.z > top_z - 0.025)
    drape(ob, body, "pin", frames=24, mass=0.3, stiffness=15.0, bending=1.0)
    finish(ob, body, rig, solidify=0.004)
    ob.data.materials.append(fabric(name + "_cloth", base, normal, orm, scale=scale, tint=tint, sheen=0.15))
    return ob


# ---------------------------------------------------------------- songkok
def songkok(body, rig, hair=None, height=0.082, above_eyes=0.046):
    eyes = [o for o in rig.children if "high-poly" in o.name or "low-poly" in o.name]
    eye_z = (eyes[0].matrix_world @ Vector(sum((Vector(c) for c in eyes[0].bound_box), Vector()) / 8)).z if eyes else bone(rig, "head")[0].z + 0.08
    k = looks.head_k(body)
    z0 = eye_z + above_eyes * k
    height = height * (1 + (k - 1) * 0.5)          # a bigger head wears a taller songkok, not proportionally taller
    crown = max(p.z for p in body_points(body))
    height = max(height, crown - z0 + 0.012)       # and always clears the crown
    # fitted to the widest part of the head it covers (the back of the skull bulges out above the brow line)
    pts = [p for p in body_points(body) if z0 - 0.002 < p.z < z0 + 0.06 * k]
    cx = (max(p.x for p in pts) + min(p.x for p in pts)) / 2
    cy = (max(p.y for p in pts) + min(p.y for p in pts)) / 2
    rx = (max(p.x for p in pts) - min(p.x for p in pts)) / 2 + 0.009
    ry = (max(p.y for p in pts) - min(p.y for p in pts)) / 2 + 0.009
    bm = bmesh.new()
    segs, rows = 64, 6
    ring_verts = []
    for i in range(rows + 1):
        t = i / rows
        z = z0 + height * t
        k = 1.0 - 0.035 * t                      # very slightly narrower at the crown
        ring_verts.append([bm.verts.new((cx + math.cos(2 * math.pi * j / segs) * rx * k, cy + math.sin(2 * math.pi * j / segs) * ry * k, z)) for j in range(segs)])
    for i in range(rows):
        for j in range(segs):
            a, b = ring_verts[i][j], ring_verts[i][(j + 1) % segs]
            bm.faces.new((a, b, ring_verts[i + 1][(j + 1) % segs], ring_verts[i + 1][j]))
    top = bmesh.ops.contextual_create(bm, geom=ring_verts[-1])["faces"]
    bmesh.ops.inset_region(bm, faces=top, thickness=0.01, depth=0.004)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("songkok"); bm.to_mesh(me); bm.free()
    ob = new_object(body.name.split(".")[0] + ".songkok", me)
    bpy.context.view_layer.objects.active = ob
    bv = ob.modifiers.new("bevel", 'BEVEL'); bv.width = 0.006; bv.segments = 3; bv.limit_method = 'ANGLE'
    bpy.ops.object.modifier_apply(modifier=bv.name)
    ob.data.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.cylinder_project(direction='ALIGN_TO_OBJECT', scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    # the hat rides the head bone only
    hg = ob.vertex_groups.new(name="head"); hg.add(list(range(len(ob.data.vertices))), 1.0, 'REPLACE')
    sd = ob.modifiers.new("subdiv", 'SUBSURF'); sd.levels = 1; sd.render_levels = 2
    am = ob.modifiers.new("Armature", 'ARMATURE'); am.object = rig
    ob.parent = rig; ob.matrix_parent_inverse = rig.matrix_world.inverted()
    ob.data.materials.append(fabric("songkok_velvet", "songkok_basecolor.png", "songkok_normal.png", "songkok_orm.png", scale=2.0, sheen=0.8, tint=(0.35, 0.35, 0.37)))
    if hair:
        bpy.data.objects.remove(hair, do_unlink=True)      # replaced by the cropped scalp hair
    short_hair(body, rig, z0 + 0.012 * k)
    return ob


def short_hair(body, rig, below_z, color=(0.025, 0.02, 0.018), thickness=0.0035):
    """Close-cropped hair grown from the scalp itself: the sides and back of the head below the hat line,
    behind the temples and above the nape, leaving the ears out."""
    eyes = [o for o in rig.children if "high-poly" in o.name or "low-poly" in o.name]
    ec = eyes[0].matrix_world @ (sum((Vector(c) for c in eyes[0].bound_box), Vector()) / 8)
    head, _ = bone(rig, "head")
    k = looks.head_k(body)
    pts = [p for p in body_points(body) if abs(p.z - (ec.z + 0.01 * k)) < 0.006 * k]
    half = (max(p.x for p in pts) - min(p.x for p in pts)) / 2
    def pick(c, n):
        if c.z > below_z or c.z < ec.z - 0.075 * k:
            return False
        if c.y < ec.y + 0.03 * k:                      # nothing in front of the temples
            return False
        if c.z < ec.z - 0.01 * k and c.y < ec.y + 0.11 * k:    # below eye level only at the back (no beard)
            return False
        if abs(c.x - head.x) > half * 0.97 and ec.z - 0.045 * k < c.z < ec.z + 0.03 * k:   # the ears
            return False
        return n.z > -0.6
    ob = _cut(body, "scalp_hair", pick)
    _push(ob, 0.001)
    finish(ob, body, rig, solidify=thickness, subdiv=1)
    mat = bpy.data.materials.new("short_hair"); mat.use_nodes = True
    N, L = mat.node_tree.nodes, mat.node_tree.links
    bsdf = N["Principled BSDF"]; bsdf.inputs["Base Color"].default_value = (*color, 1); bsdf.inputs["Roughness"].default_value = 0.55
    tc = N.new("ShaderNodeTexCoord"); mp = N.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (600, 600, 60)
    nz = N.new("ShaderNodeTexNoise"); nz.inputs["Detail"].default_value = 8
    bump = N.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.5
    L.new(tc.outputs["Object"], mp.inputs[0]); L.new(mp.outputs[0], nz.inputs[0]); L.new(nz.outputs[0], bump.inputs["Height"])
    L.new(bump.outputs[0], bsdf.inputs["Normal"])
    ob.data.materials.append(mat)
    return ob


def _trim_above(ob, z):
    """Delete the hair the hat covers (it would poke through)."""
    mw = ob.matrix_world
    bm = bmesh.new(); bm.from_mesh(ob.data)
    kill = [f for f in bm.faces if (mw @ f.calc_center_median()).z > z]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bm.to_mesh(ob.data); bm.free()


# ---------------------------------------------------------------- sandals
def sandals(body, rig, sole=0.016, strap_color=(0.33, 0.19, 0.1)):
    """Slip-on sandals: soles from the footprint, one wide strap over the toes. Lifts the character by the sole."""
    rig.location.z += sole
    bpy.context.view_layer.update()
    pts = body_points(body)
    out = []
    for s in ("l", "r"):
        foot, _ = bone(rig, f"foot_{s}")
        ball, ball_t = bone(rig, f"ball_{s}")
        fp = [p for p in pts if p.z < sole + 0.03 and (p.x > 0) == (s == "l") and (p - foot).length < 0.3]
        # footprint outline in plan, a little wider
        bm = bmesh.new()
        for p in fp:
            bm.verts.new((p.x, p.y, 0))
        hull = bmesh.ops.convex_hull(bm, input=bm.verts)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_edges], context='VERTS')
        bmesh.ops.delete(bm, geom=[f for f in bm.faces], context='FACES_ONLY')
        c = sum((v.co for v in bm.verts), Vector()) / len(bm.verts)
        for v in bm.verts:
            v.co += (v.co - c).normalized() * 0.008
        bm.faces.new(sorted(bm.verts, key=lambda v: math.atan2(v.co.y - c.y, v.co.x - c.x)))
        f = bm.faces[:][0] if bm.faces else None
        ret = bmesh.ops.extrude_face_region(bm, geom=list(bm.faces))
        for v in [g for g in ret["geom"] if isinstance(g, bmesh.types.BMVert)]:
            v.co.z = sole
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new("sole"); bm.to_mesh(me); bm.free()
        so = new_object(f"{body.name.split('.')[0]}.sole_{s}", me)
        bpy.context.view_layer.objects.active = so
        bv = so.modifiers.new("bevel", 'BEVEL'); bv.width = 0.004; bv.segments = 2
        bpy.ops.object.modifier_apply(modifier=bv.name)
        so.data.uv_layers.new(name="UVMap")
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.cube_project(cube_size=0.3)
        bpy.ops.object.mode_set(mode='OBJECT')
        g = so.vertex_groups.new(name=f"foot_{s}"); g.add(list(range(len(so.data.vertices))), 1.0, 'REPLACE')
        so.parent = rig; so.matrix_parent_inverse = rig.matrix_world.inverted()
        so.matrix_parent_inverse = rig.matrix_world.inverted()
        am = so.modifiers.new("Armature", 'ARMATURE'); am.object = rig
        so.data.materials.append(fabric("sole_rubber", "leather_basecolor.png", "leather_normal.png", None, scale=3, tint=(0.25, 0.16, 0.1), roughness=0.75))
        out.append(so)
        # strap: the top of the foot between the ball of the foot and the instep
        mid = ball.lerp(foot, 0.45)
        def pick(c, n, s=s, ball=ball, mid=mid):
            return (c.x > 0) == (s == "l") and n.z > 0.2 and min(ball.y, mid.y) - 0.005 < c.y < max(ball.y, mid.y) + 0.005 and c.z < foot.z
        st = _cut(body, f"strap_{s}", pick)
        _push(st, 0.003)
        finish(st, body, rig, solidify=0.004, subdiv=1)
        st.data.materials.append(fabric("strap_leather", "leather_basecolor.png", "leather_normal.png", "leather_orm.png", scale=4, tint=(1.0, 0.9, 0.8)))
        out.append(st)
    return out


# ---------------------------------------------------------------- mesh utilities
def _cut(body, name, pick):
    """Body faces pick() accepts, as a new object in world space (UVs kept)."""
    mw = body.matrix_world
    bm = bmesh.new(); bm.from_mesh(looks.evaluated_mesh(body))
    bm.transform(mw)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not pick(f.calc_center_median(), f.normal)], context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    for layer in list(bm.verts.layers.deform):
        pass
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for vg in list(me.attributes):
        pass
    ob = new_object(body.name.split(".")[0] + "." + name, me)
    ob.vertex_groups.clear()
    return ob


def _push(ob, dist, smooth_normals=8):
    """Move every vertex out by dist (a number, or a function of the world position) along a smoothed normal:
    raw normals are noisy in MPFB's dense spots (nipples, navel, armpits), and pushing along them blooms
    each cluster into a knot."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bm.normal_update()
    bm.verts.ensure_lookup_table()
    n = [v.normal.copy() for v in bm.verts]
    for _ in range(smooth_normals):
        n = [(n[v.index] + sum((n[e.other_vert(v).index] for e in v.link_edges), Vector())).normalized() for v in bm.verts]
    mw = ob.matrix_world
    for v in bm.verts:
        d = dist(mw @ v.co) if callable(dist) else dist
        v.co += n[v.index] * d
    bm.to_mesh(ob.data); bm.free()


def _pin(ob, group, test):
    """Pin weights: test(p) is True/False, or a 0..1 weight (a soft edge stops a crumple line where the pin ends)."""
    g = ob.vertex_groups.get(group) or ob.vertex_groups.new(name=group)
    mw = ob.matrix_world
    for v in ob.data.vertices:
        w = float(test(mw @ v.co))
        if w > 0.0:
            g.add([v.index], min(1.0, w), 'REPLACE')


def _subdivide(ob, cuts):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges, cuts=cuts, use_grid_fill=True)
    bm.to_mesh(ob.data); bm.free()
