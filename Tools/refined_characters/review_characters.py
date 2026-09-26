"""Independent, repeatable Blender review and delivery checks for the KL cast.

Run with Blender 5.2 --background --python this_file -- --baseline, or --full.
The script only writes review evidence; it never saves over a character source.
Geometry, animation, UVs, weights and materials are loaded from the delivered .blend.
"""
from pathlib import Path
import argparse
import json
import math
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parent.parent
DEFAULT_ASSETS = ["chr_pakmat", "chr_maksom", "chr_along", "chr_adik",
                  "chr_townman", "chr_townaunty", "chr_pakcik", "chr_kid", "chr_polis", "chr_datukmega",
                  "chr_aiman", "chr_mei", "chr_ravi"]
LABELS = {"chr_pakmat": "Pak Mat", "chr_maksom": "Mak Som", "chr_along": "Along",
          "chr_adik": "Adik", "chr_townman": "Town man", "chr_townaunty": "Town aunty",
          "chr_pakcik": "Pakcik", "chr_kid": "Neighbourhood kid", "chr_polis": "Polis",
          "chr_datukmega": "Datuk Mega", "chr_aiman": "Aiman", "chr_mei": "Mei", "chr_ravi": "Ravi"}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def source_path(asset, source_dir, baseline=False):
    if baseline:
        return HERE / "review" / "baseline-sources" / (asset + ".blend")
    if source_dir:
        root = Path(source_dir)
        if not root.is_absolute():
            root = PROJECT/root
        for candidate in [root / asset / (asset + '_master.blend'), root / (asset + '_master.blend'), root / (asset + '.blend')]:
            if candidate.exists():
                return candidate
        return root / asset / (asset + ".blend")
    return PROJECT / "Tools" / "kl_assets" / asset / (asset + ".blend")


def load_asset(path, asset, lods=False):
    with bpy.data.libraries.load(str(path), link=False) as (src, dest):
        dest.objects = [name for name in src.objects
                        if name == asset or name.startswith(asset + '.') or name.startswith(asset + "_LOD") or name == asset + "_Rig"]
        dest.actions = [name for name in src.actions if name.startswith(asset + '|')]
    objs = [o for o in dest.objects if o]
    for obj in objs:
        bpy.context.scene.collection.objects.link(obj)
        obj.hide_render = obj.type == 'MESH' and 'LOD1' in obj.name
        obj.hide_viewport = False
    arms = [o for o in objs if o.type == 'ARMATURE']
    meshes = [o for o in objs if o.type == 'MESH' and ('LOD0' in o.name or (lods and 'LOD1' in o.name))]
    if not arms or not meshes:
        raise RuntimeError(f"Missing character armature/LOD0 in {path}; got {[o.name for o in objs]}")
    arm = arms[0]
    # A copied .blend may have a relative palette path. Resolve its declared texture
    # against the source asset and the game's canonical texture location.
    for img in bpy.data.images:
        if img.source != 'FILE' or img.packed_file:
            continue
        resolved = Path(bpy.path.abspath(img.filepath, library=img.library))
        filename = resolved.name
        candidates = [resolved, path.parent/'textures'/filename,
                      PROJECT/'Tools'/'kl_assets'/asset/'textures'/filename,
                      PROJECT/'Assets'/'Models'/'KL'/filename]
        if filename == 'kl_palette.png':
            candidates.append(PROJECT/'Assets'/'Models'/'KL'/'kl_palette.png')
        for candidate in candidates:
            if candidate.is_file():
                img.filepath = str(candidate)
                img.reload()
                break
    for mesh in meshes:
        if mesh.data.shape_keys:
            for key in mesh.data.shape_keys.key_blocks:
                key.value = 0
    neutral(arm, 72)
    return arm, meshes, objs


def neutral(arm, lower=72):
    if arm.animation_data:
        arm.animation_data.action = None
        for track in arm.animation_data.nla_tracks:
            track.mute = True
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
    for side, sign in (("Left", 1), ("Right", -1)):
        pb = arm.pose.bones.get(side + "UpperArm")
        if pb:
            rest = pb.bone.matrix_local.to_3x3()
            rot = Matrix.Rotation(math.radians(sign * lower), 3, 'Y')
            pb.rotation_quaternion = (rest.inverted() @ rot @ rest).to_quaternion()
    bpy.context.view_layer.update()


def apply_pose(arm, asset, name, frame):
    neutral(arm, 0)
    action = bpy.data.actions.get(asset + "|" + name)
    if action is None:
        return False
    arm.animation_data_create()
    arm.animation_data.action = action
    if hasattr(action, 'slots') and action.slots:
        arm.animation_data.action_slot = action.slots[0]
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    return True


def bounds(meshes):
    dg = bpy.context.evaluated_depsgraph_get()
    points = []
    for obj in meshes:
        ev = obj.evaluated_get(dg)
        points.extend(ev.matrix_world @ Vector(p) for p in ev.bound_box)
    lo = Vector([min(p[k] for p in points) for k in range(3)])
    hi = Vector([max(p[k] for p in points) for k in range(3)])
    return lo, hi


def material(name, color, roughness=.65):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    node = mat.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value = (*color, 1)
    node.inputs['Roughness'].default_value = roughness
    mat.diffuse_color = (*color, 1)
    return mat


def studio(engine='eevee'):
    scene = bpy.context.scene
    if engine == 'eevee':
        try:
            scene.render.engine = 'BLENDER_EEVEE_NEXT'
        except TypeError:
            scene.render.engine = 'BLENDER_EEVEE'
    else:
        scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new('ReviewWorld')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.28, .31, .36, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .5
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.exposure = 0
    scene.view_settings.gamma = 1
    if engine == 'workbench':
        sh = scene.display.shading
        sh.light = 'STUDIO'
        sh.color_type = 'TEXTURE'
        sh.show_shadows = True
        sh.show_cavity = True
        sh.cavity_type = 'BOTH'
        sh.curvature_ridge_factor = .65
        sh.curvature_valley_factor = .55
        sh.show_object_outline = False
        sh.background_type = 'WORLD'
        scene.world.color = (.12, .15, .18)
        scene.display.render_aa = '16'
    floor_mat = material('ReviewFloor', (.13, .16, .20))
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -.025))
    floor = bpy.context.object
    floor.name = 'REVIEW_Floor'
    floor.data.materials.append(floor_mat)
    for name, pos, energy, size, color in [
        ('Key', (-4, -6, 8), 1250, 6, (1, .90, .80)),
        ('Fill', (6, -3, 4), 850, 5, (.72, .85, 1)),
        ('Rim', (2, 4, 7), 1550, 4, (1, .89, .70)),
    ]:
        data = bpy.data.lights.new('Review' + name, 'AREA')
        data.energy = energy
        data.shape = 'DISK'
        data.size = size
        data.color = color
        obj = bpy.data.objects.new(data.name, data)
        scene.collection.objects.link(obj)
        obj.location = pos
        obj.rotation_euler = (Vector((0, 0, .8)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    return scene


def render(meshes, out, yaw=20, pitch=8, resolution=(640, 800), fixed_height=None):
    scene = bpy.context.scene
    lo, hi = bounds(meshes)
    center = (lo + hi) * .5
    if fixed_height:
        center.z = fixed_height * .5
    yaw, pitch = math.radians(yaw), math.radians(pitch)
    direction = Vector((math.sin(yaw)*math.cos(pitch), -math.cos(yaw)*math.cos(pitch), math.sin(pitch)))
    cam = bpy.data.objects.get('REVIEW_Camera')
    if not cam:
        data = bpy.data.cameras.new('REVIEW_Camera')
        cam = bpy.data.objects.new(data.name, data)
        scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    aspect = resolution[0]/resolution[1]
    span = hi-lo
    cam.data.ortho_scale = max((fixed_height or span.z)*1.15, (span.x*abs(math.cos(yaw))+span.y*abs(math.sin(yaw)))*1.12/aspect)
    cam.location = center + direction * 20
    cam.rotation_euler = (center-cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = resolution
    out = Path(out)
    out.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(out)
    bpy.ops.render.render(write_still=True)
    return out


def compose(paths, out, cols):
    ims = [bpy.data.images.load(str(p), check_existing=False) for p in paths]
    w, h = ims[0].size
    rows = math.ceil(len(ims)/cols)
    arr = np.zeros((h*rows, w*cols, 4), dtype=np.float32)
    arr[:, :, :] = (.13, .16, .20, 1)
    for idx, im in enumerate(ims):
        row, col = divmod(idx, cols)
        pix = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
        arr[(rows-row-1)*h:(rows-row)*h, col*w:(col+1)*w, :] = pix
    sheet = bpy.data.images.new('ReviewSheet', width=w*cols, height=h*rows)
    sheet.pixels.foreach_set(arr.ravel())
    sheet.filepath_raw = str(out)
    sheet.file_format = 'PNG'
    sheet.save()
    for im in ims + [sheet]:
        bpy.data.images.remove(im)


def expression_sheet(arm, meshes, asset, out):
    """Render actual exported morph keys at value one, then restore neutral."""
    out=Path(out);out.mkdir(parents=True,exist_ok=True)
    neutral(arm,72)
    lo,hi=bounds(meshes)
    center=Vector((0,0,hi.z-.235))
    cam=bpy.data.objects.get('REVIEW_Camera')
    if not cam:
        cam=bpy.data.objects.new('REVIEW_Camera',bpy.data.cameras.new('REVIEW_Camera'))
        bpy.context.scene.collection.objects.link(cam)
    cam.location=center+Vector((1,-8,.15))
    cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO';cam.data.ortho_scale=.72
    scene=bpy.context.scene;scene.camera=cam
    scene.render.resolution_x=480;scene.render.resolution_y=500
    names=['Neutral','Grin','Alarm','Determined','Blink'];paths=[]
    for name in names:
        for ob in meshes:
            if ob.data.shape_keys:
                for key in ob.data.shape_keys.key_blocks:key.value=1 if key.name==name else 0
        bpy.context.view_layer.update()
        path=out/(name.lower()+'.png');scene.render.filepath=str(path)
        bpy.ops.render.render(write_still=True);paths.append(path)
    compose(paths,out/'expressions.png',cols=len(names))
    for ob in meshes:
        if ob.data.shape_keys:
            for key in ob.data.shape_keys.key_blocks:key.value=0
    (out/'order.json').write_text(json.dumps(dict(asset=asset,left_to_right=names,morph_value=1),indent=2),encoding='utf-8')


def gameplay_strip(paths,out):
    """Same world scale as lineup: a 1.72 m adult occupies approximately 128 px."""
    w,h=128,171
    arr=np.ones((h*2,w*len(paths),4),dtype=np.float32)
    for idx,path in enumerate(paths):
        im=bpy.data.images.load(str(path),check_existing=False)
        im.scale(w,h)
        pix=np.array(im.pixels[:],dtype=np.float32).reshape(h,w,4)
        arr[h:,idx*w:(idx+1)*w,:]=pix
        grey=pix.copy()
        luminance=pix[:,:,0]*.2126+pix[:,:,1]*.7152+pix[:,:,2]*.0722
        grey[:,:,:3]=luminance[:,:,None]
        arr[:h,idx*w:(idx+1)*w,:]=grey
        bpy.data.images.remove(im)
    sheet=bpy.data.images.new('GameplayReadability',width=w*len(paths),height=h*2)
    sheet.pixels.foreach_set(arr.ravel());sheet.filepath_raw=str(out);sheet.file_format='PNG';sheet.save()
    bpy.data.images.remove(sheet)


def validate(arm, meshes, asset, budget=35000):
    neutral(arm, 0)
    records = []
    errors = []
    for obj in meshes:
        me = obj.data
        me.calc_loop_triangles()
        groups = {g.index: g.name for g in obj.vertex_groups}
        bones = set(arm.data.bones.keys())
        unweighted, unnormalised, overfour, invalid = [], [], [], []
        for v in me.vertices:
            weights = [(groups[g.group], g.weight) for g in v.groups if g.weight > .00001]
            if not weights:
                unweighted.append(v.index)
            if weights and abs(sum(w for _, w in weights)-1) > .001:
                unnormalised.append(v.index)
            if len(weights) > 4:
                overfour.append(v.index)
            if any(n not in bones for n, _ in weights):
                invalid.append(v.index)
        ntri = len(me.loop_triangles)
        zero_area = sum(p.area < 1e-10 for p in me.polygons)
        record = dict(mesh=obj.name, vertices=len(me.vertices), triangles=ntri,
                      uv_layers=len(me.uv_layers), materials=len(me.materials),
                      unweighted_vertices=len(unweighted), unnormalised_vertices=len(unnormalised),
                      over_four_influences=len(overfour), invalid_bone_weights=len(invalid),
                      zero_area_faces=zero_area, smooth_faces=sum(p.use_smooth for p in me.polygons),
                      positive_scale=all(v > 0 for v in obj.scale),
                      armature_modifiers=sum(m.type == 'ARMATURE' and m.object == arm for m in obj.modifiers))
        records.append(record)
        for condition, message in [(bool(unweighted), 'unweighted vertices'), (bool(unnormalised), 'unnormalised weights'),
            (bool(overfour), 'more than four weights'), (bool(invalid), 'missing bones'), (not me.uv_layers, 'missing UVs'),
            (ntri > budget and 'LOD0' in obj.name, 'LOD0 triangle budget exceeded'),
            (not record['positive_scale'], 'non-positive scale'), (not record['armature_modifiers'], 'missing armature modifier')]:
            if condition:
                errors.append(obj.name + ': ' + message)
    lo, hi = bounds([o for o in meshes if 'LOD0' in o.name])
    actions = [a for a in bpy.data.actions if a.name.startswith(asset + '|')]
    images = []
    used_materials = {mat for obj in meshes for mat in obj.data.materials if mat}
    for mat in used_materials:
        if mat.node_tree:
            for node in mat.node_tree.nodes:
                if node.type == 'TEX_IMAGE' and node.image:
                    img = node.image
                    resolved = Path(bpy.path.abspath(img.filepath, library=img.library))
                    valid = bool(img.packed_file or resolved.is_file())
                    images.append(dict(name=img.name, path=str(resolved), packed=bool(img.packed_file), resolves=valid))
                    if not valid:
                        errors.append('Missing texture dependency: ' + img.name)
    return dict(asset=asset, passed=not errors, errors=errors, meshes=records,
                bones=list(arm.data.bones.keys()), bone_count=len(arm.data.bones),
                height_m=round(hi.z-lo.z, 5), ground_min_m=round(lo.z, 5),
                target_lod0_budget=budget, textures=images,
                shape_keys={o.name: list(o.data.shape_keys.key_blocks.keys()) if o.data.shape_keys else [] for o in meshes},
                clips={a.name.split('|')[-1]:list(a.frame_range) for a in actions})


def roundtrip(asset, path, expected, out, engine, render_output=True):
    reset()
    bpy.ops.import_scene.fbx(filepath=str(path), automatic_bone_orientation=False, use_anim=True)
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not arms:
        return dict(asset=asset, passed=False, errors=['No armature imported'], file=str(path))
    arm = arms[0]
    neutral(arm, 0)
    for o in meshes:
        o.hide_render = 'LOD1' in o.name
    lod0 = [o for o in meshes if 'LOD0' in o.name]
    report = validate(arm, meshes, asset, expected['target_lod0_budget'])
    report['file'] = str(path)
    report['imported_action_count'] = len(bpy.data.actions)
    report['imported_action_names'] = [a.name for a in bpy.data.actions]
    imported_clips = {a.name.split('|')[-1]: a for a in bpy.data.actions}
    report['clip_length_errors_frames'] = {}
    for name, frame_range in expected['clips'].items():
        action = imported_clips.get(name)
        if not action:
            report['errors'].append('Missing action: ' + name)
            continue
        delta = abs((action.frame_range[1]-action.frame_range[0]) - (frame_range[1]-frame_range[0]))
        report['clip_length_errors_frames'][name] = round(delta, 5)
        if delta > .05:
            report['errors'].append('Animation length changed: ' + name)
    missing = sorted(set(expected['bones']) - set(report['bones']))
    hdev = abs(report['height_m']-expected['height_m'])/max(expected['height_m'], .001)
    report['height_relative_error'] = hdev
    if missing:
        report['errors'].append('Missing skeleton bones: ' + ', '.join(missing))
    if hdev > .01:
        report['errors'].append('Height changed more than 1 percent')
    before_tris = {r['mesh']: r['triangles'] for r in expected['meshes']}
    for record in report['meshes']:
        if record['mesh'] in before_tris and record['triangles'] != before_tris[record['mesh']]:
            report['errors'].append(record['mesh'] + ': triangle count changed')
    if len(bpy.data.actions) < len(expected['clips']):
        report['errors'].append('Animation actions missing')
    report['passed'] = not report['errors']
    if render_output:
        studio(engine)
        neutral(arm, 72)
        render(lod0, out / asset / 'roundtrip.png', yaw=25, resolution=(512, 640))
    return report


def export_path(source, asset):
    for candidate in [source.parent/'exports'/(asset + '.fbx'), source.with_suffix('.fbx')]:
        if candidate.exists():
            return candidate
    raise FileNotFoundError('Missing FBX delivery for ' + asset)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--baseline', action='store_true')
    parser.add_argument('--full', action='store_true')
    parser.add_argument('--assets', nargs='+', default=DEFAULT_ASSETS)
    parser.add_argument('--source-dir', '--root', dest='source_dir', default=str(HERE))
    parser.add_argument('--out', default=str(HERE/'review'))
    parser.add_argument('--engine', choices=['eevee', 'workbench'], default='eevee')
    parser.add_argument('--no-roundtrip', action='store_true')
    parser.add_argument('--roundtrip', action='store_true', help='Check FBX even without --full; may combine with --no-render.')
    parser.add_argument('--gates', action='store_true', help='Add orthographic transparent front and side for silhouette measurement.')
    parser.add_argument('--refresh', action='store_true', help='Replace selected assets in an existing full review and rebuild its contact sheets.')
    parser.add_argument('--no-render', action='store_true')
    parser.add_argument('--budget', type=int, default=70000)
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    out = Path(args.out)
    if not out.is_absolute():
        out = PROJECT/out
    out.mkdir(parents=True, exist_ok=True)
    reports, thumbs, roundtrips = [], [], []
    for asset in args.assets:
        reset()
        path = source_path(asset, args.source_dir, args.baseline)
        arm, meshes, objs = load_asset(path, asset, lods=True)
        report = validate(arm, meshes, asset, args.budget)
        report['source'] = str(path)
        reports.append(report)
        print('VALIDATION', json.dumps(report), flush=True)
        if args.no_render:
            if (args.full or args.roundtrip) and not args.no_roundtrip:
                roundtrips.append(roundtrip(asset, export_path(path, asset), report, out/'roundtrip', args.engine, False))
            continue
        studio(args.engine)
        lod0 = [o for o in meshes if 'LOD0' in o.name]
        neutral(arm, 72)
        target = out / ('before-details' if args.baseline else 'after-details') / asset
        target.mkdir(parents=True, exist_ok=True)
        thumbs.append(render(lod0, target/'threequarter.png', yaw=20, resolution=(480, 640), fixed_height=2.0))
        if args.gates:
            bpy.context.scene.render.film_transparent = True
            bpy.data.objects['REVIEW_Floor'].hide_render = True
            gate_cameras = {}
            for yaw, view in [(0, 'front'), (270, 'side')]:
                render(lod0, target/(view+'-alpha.png'), yaw=yaw, pitch=0, resolution=(600,800), fixed_height=2.0)
                cam = bpy.context.scene.camera
                center = cam.location + cam.rotation_euler.to_quaternion() @ Vector((0,0,-20))
                gate_cameras[view] = dict(target_center_m=list(center), vertical_ortho_scale_m=cam.data.ortho_scale,
                                          yaw_degrees=yaw, elevation_degrees=0)
            bpy.context.scene.render.film_transparent = False
            bpy.data.objects['REVIEW_Floor'].hide_render = False
            (target/'camera-contract.json').write_text(json.dumps(dict(
                projection='orthographic', source_axis_up='+Z', source_forward='-Y',
                yaw_degrees=dict(front=0, side=270), elevation_degrees=0,
                cameras=gate_cameras,
                camera_center_z_m=1.0, vertical_ortho_scale_m=2.3,
                resolution_pixels=[600,800], pixels_per_m=800/2.3,
                ground_y_pixel=800/2+1.0*800/2.3,
                pose='rest skeleton with upper arms lowered 72 degrees; all other bones identity'),indent=2),encoding='utf-8')
        if args.full:
            views = [render(lod0, target/(name+'.png'), yaw=yaw, pitch=0, resolution=(480, 640), fixed_height=2.0)
                     for yaw, name in [(0,'front'),(270,'side'),(180,'back')]]
            compose(views + [thumbs[-1]], target/'turnaround.png', cols=4)
            poses = []
            for name, frame in [('walk',8),('run',5),('ride',0),('punch',4),('wave',6),('kick',7)]:
                if apply_pose(arm, asset, name, frame):
                    poses.append(render(lod0, target/(name+'.png'), yaw=30, resolution=(480,640), fixed_height=2.0))
            if poses:
                compose(poses, target/'poses.png', cols=3)
            if asset=='chr_pakmat':
                expression_sheet(arm,lod0,asset,target/'expressions')
        if (args.full or args.roundtrip) and not args.no_roundtrip:
            roundtrips.append(roundtrip(asset, export_path(path, asset), report, out/'roundtrip', args.engine))
    if args.refresh and not args.baseline and (out/'validation.json').exists():
        previous=json.loads((out/'validation.json').read_text(encoding='utf-8'))['assets']
        merged={record['asset']:record for record in previous}
        merged.update({record['asset']:record for record in reports})
        reports=list(merged.values())
        args.assets=[record['asset'] for record in reports]
        if not args.no_render:
            thumbs=[out/'after-details'/asset/'threequarter.png' for asset in args.assets]
        if roundtrips and (out/'roundtrip.json').exists():
            earlier=json.loads((out/'roundtrip.json').read_text(encoding='utf-8'))['assets']
            merged={record['asset']:record for record in earlier}
            merged.update({record['asset']:record for record in roundtrips})
            roundtrips=list(merged.values())
    if thumbs:
        compose(thumbs, out/('before.png' if args.baseline else 'after.png'), cols=min(5,len(thumbs)))
        gameplay_strip(thumbs,out/('before-gameplay-strip.png' if args.baseline else 'gameplay-strip.png'))
        if len(thumbs) >= 4:
            compose(thumbs[:4], out/('before-family.png' if args.baseline else 'after-family.png'), cols=4)
            baseline_family=HERE/'review'/'before-family.png'
            if not args.baseline and baseline_family.exists() and args.assets[:4] == DEFAULT_ASSETS[:4]:
                compose([baseline_family,out/'after-family.png'],out/'before-after-family.png',cols=1)
        if len(thumbs)>4 and args.assets[:4]==DEFAULT_ASSETS[:4]:
            compose(thumbs[4:],out/('before-npcs.png' if args.baseline else 'after-npcs.png'),cols=5)
            (out/'lineup-order.json').write_text(json.dumps(dict(
                family=[dict(id=asset,label=LABELS.get(asset,asset)) for asset in args.assets[:4]],
                npcs=[dict(id=asset,label=LABELS.get(asset,asset)) for asset in args.assets[4:]],
                order='left to right, top to bottom'),indent=2),encoding='utf-8')
    prefix = 'baseline' if args.baseline else 'validation'
    result = dict(passed=all(r['passed'] for r in reports),
                  scope='Technical geometry, skinning, texture and export checks; visual/reference acceptance is separate.',
                  assets=reports)
    (out/(prefix+'.json')).write_text(json.dumps(result, indent=2), encoding='utf-8')
    if roundtrips:
        (out/'roundtrip.json').write_text(json.dumps(dict(passed=all(r['passed'] for r in roundtrips), assets=roundtrips), indent=2), encoding='utf-8')
    print('REVIEW_COMPLETE', str(out), flush=True)
    if not result['passed'] or any(not r['passed'] for r in roundtrips):
        raise SystemExit(1)


if __name__ == '__main__':
    main()
