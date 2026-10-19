"""Render real mesh variants in matched Blender 5.2 studio scenes.

blender -b -t 4 -P render_variants.py -- --manifest variants.json --out renders
Only imported mesh assets are presented. No proxy characters are generated.
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import re
import sys

import bpy
from mathutils import Vector

HEIGHT_M = 1.66
VIEWS = {'front': (0., 0.), 'threequarter': (32., 6.), 'profile': (90., 0.), 'face': (10., 2.)}


def write_json(path, data):
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding='utf-8')


def resolve(path, base):
    p = Path(path).expanduser()
    return (p if p.is_absolute() else base / p).resolve()


def read_manifest(path):
    entries = json.loads(path.read_text(encoding='utf-8-sig'))
    if not isinstance(entries, list) or not entries:
        raise ValueError('Manifest must be a nonempty JSON array.')
    variants, ids = [], set()
    for entry in entries:
        identifier = str(entry['id'])
        if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,79}', identifier):
            raise ValueError(f'Unsafe variant id: {identifier!r}')
        if identifier in ids:
            raise ValueError(f'Duplicate variant id: {identifier}')
        ids.add(identifier)
        source = resolve(entry['path'], path.parent)
        if source.suffix.lower() not in {'.glb', '.gltf', '.fbx'} or not source.is_file():
            raise ValueError(f'Missing or unsupported mesh asset: {source}')
        angle = float(entry.get('front_rotation_degrees', 0.))
        if not math.isfinite(angle):
            raise ValueError(f'Non-finite front rotation: {identifier}')
        opaque = entry.get('force_opaque', False)
        excluded = entry.get('exclude_objects', [])
        if not isinstance(opaque, bool) or not isinstance(excluded, list) or not all(isinstance(n, str) for n in excluded):
            raise ValueError('force_opaque must be a JSON boolean; exclude_objects must be an array of exact object names.')
        variants.append(dict(id=identifier, label=str(entry.get('label', identifier)),
                             path=str(source), front_rotation_degrees=angle,
                             force_opaque=opaque, exclude_objects=excluded))
    return variants


def import_asset(path, variant):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if path.suffix.lower() in {'.glb', '.gltf'}:
        bpy.ops.import_scene.gltf(filepath=str(path))
    else:
        bpy.ops.import_scene.fbx(filepath=str(path), use_image_search=True)
    imported = list(bpy.context.scene.objects)
    missing = set(variant['exclude_objects']) - {o.name for o in imported}
    if missing:
        raise ValueError(f'Excluded object names not found in {path.name}: {sorted(missing)}')
    for obj in imported:
        if obj.type in {'CAMERA', 'LIGHT'} or obj.name in variant['exclude_objects']:
            obj.hide_render = True
        # A comparison shows bind/rest geometry, rather than a randomly selected
        # imported action. The source file is never altered.
        if obj.type == 'ARMATURE':
            obj.data.pose_position = 'REST'
    bpy.context.scene.frame_set(1)
    meshes = [o for o in imported if o.type == 'MESH' and not o.hide_render]
    if not meshes:
        raise ValueError(f'No renderable mesh objects in {path}')
    if variant['force_opaque']:
        for mat in {m for ob in meshes for m in ob.data.materials if m}:
            mat.diffuse_color = (*mat.diffuse_color[:3], 1)
            if mat.use_nodes:
                for node in mat.node_tree.nodes:
                    if node.type == 'BSDF_PRINCIPLED':
                        alpha = node.inputs.get('Alpha')
                        if alpha:
                            for link in list(alpha.links):
                                mat.node_tree.links.remove(link)
                            alpha.default_value = 1
    return imported, meshes


def bounds(meshes):
    graph = bpy.context.evaluated_depsgraph_get()
    points = [obj.evaluated_get(graph).matrix_world @ Vector(corner)
              for obj in meshes for corner in obj.evaluated_get(graph).bound_box]
    low = Vector([min(p[i] for p in points) for i in range(3)])
    high = Vector([max(p[i] for p in points) for i in range(3)])
    if not all(math.isfinite(v) for v in (*low, *high)):
        raise ValueError('Imported mesh has non-finite bounds.')
    return low, high


def normalize(imported, meshes, angle):
    root = bpy.data.objects.new('PRESENTATION_Normalization', None)
    bpy.context.scene.collection.objects.link(root)
    members = set(imported)
    for obj in imported:
        if obj.parent not in members:
            matrix = obj.matrix_world.copy()
            obj.parent = root
            obj.matrix_world = matrix
    root.rotation_euler.z = math.radians(angle)
    bpy.context.view_layer.update()
    raw_low, raw_high = bounds(meshes)
    raw_height = raw_high.z - raw_low.z
    if raw_height <= 1e-8:
        raise ValueError('Imported geometry has zero standing height.')
    factor = HEIGHT_M / raw_height
    root.scale = (factor,) * 3
    bpy.context.view_layer.update()
    low, high = bounds(meshes)
    root.location = (-.5 * (low.x + high.x), -.5 * (low.y + high.y), -low.z)
    bpy.context.view_layer.update()
    low, high = bounds(meshes)
    return dict(source_rotated_bounds_m=[list(raw_low), list(raw_high)],
                uniform_scale_factor=factor, bounds_m=[list(low), list(high)],
                height_m=high.z-low.z, floor_z_m=low.z,
                front_rotation_degrees=angle, front_axis='-Y', up_axis='+Z',
                pose='Imported frame 1; any armatures shown in rest/bind pose')


def texture_stats(source):
    records, warnings = [], []
    for image in bpy.data.images:
        if image.source != 'FILE':
            continue
        declared = image.filepath
        resolved = Path(bpy.path.abspath(declared)) if declared else None
        if not image.packed_file and (not resolved or not resolved.is_file()):
            # glTF image URIs and FBX relative references commonly live alongside
            # the asset. Resolve only exact conventional paths, never substitute
            # an unrelated image with a similar filename.
            filename = Path(declared.replace('\\', '/')).name
            candidates = [source.parent / declared.lstrip('/\\'), source.parent / filename,
                          source.parent / 'textures' / filename]
            for candidate in candidates:
                if candidate.is_file():
                    image.filepath = str(candidate.resolve())
                    image.reload()
                    resolved = candidate.resolve()
                    break
        # Embedded GLB images may still be lazily decoded after import. Touch a
        # pixel without copying the entire texture before reporting it missing.
        if not image.has_data:
            try:
                if len(image.pixels):
                    _ = image.pixels[0]
            except (RuntimeError, IndexError):
                pass
        loaded = bool(image.has_data and image.size[0] > 0 and image.size[1] > 0)
        if loaded and not image.packed_file:
            try:
                image.pack()
            except RuntimeError as exc:
                warnings.append(f'Could not pack texture {image.name}: {exc}')
        if not loaded:
            warnings.append(f'Missing/unloaded texture: {image.name} ({declared})')
        records.append(dict(name=image.name, declared_path=declared,
                            resolved_path=str(resolved) if resolved else None,
                            size=list(image.size), color_space=image.colorspace_settings.name,
                            packed=bool(image.packed_file), loaded=loaded))
    return records, warnings


def mesh_stats(meshes):
    graph = bpy.context.evaluated_depsgraph_get()
    records, used_materials = [], {}
    for obj in meshes:
        evaluated = obj.evaluated_get(graph)
        mesh = evaluated.to_mesh()
        try:
            mesh.calc_loop_triangles()
            records.append(dict(name=obj.name, vertices=len(mesh.vertices),
                                polygons=len(mesh.polygons), triangles=len(mesh.loop_triangles),
                                uv_layers=len(mesh.uv_layers),
                                material_slots=[m.name if m else None for m in obj.data.materials]))
        finally:
            evaluated.to_mesh_clear()
        for mat in obj.data.materials:
            if mat:
                used_materials[mat.name] = dict(name=mat.name, uses_nodes=mat.use_nodes,
                    node_types=sorted({n.bl_idname for n in mat.node_tree.nodes}) if mat.use_nodes else [],
                    diffuse_color=list(mat.diffuse_color))
    return dict(meshes=records, total_vertices=sum(x['vertices'] for x in records),
                total_triangles=sum(x['triangles'] for x in records),
                materials=list(used_materials.values()))


def studio(samples, engine):
    scene = bpy.context.scene
    if engine == 'cycles':
        scene.render.engine = 'CYCLES'
        scene.cycles.device = 'CPU'
        scene.cycles.samples = samples
        scene.cycles.use_denoising = True
        scene.cycles.use_adaptive_sampling = True
    else:
        try:
            scene.render.engine = 'BLENDER_EEVEE'
        except (TypeError, ValueError):
            scene.render.engine = 'BLENDER_EEVEE_NEXT'
        if hasattr(scene, 'eevee') and hasattr(scene.eevee, 'taa_render_samples'):
            scene.eevee.taa_render_samples = samples
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.image_settings.color_depth = '8'
    scene.render.film_transparent = False
    scene.render.resolution_percentage = 100
    scene.render.use_file_extension = True
    scene.render.use_stamp = False
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.exposure = 0
    scene.view_settings.gamma = 1
    scene.world = bpy.data.worlds.new('STUDIO_World')
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get('Background')
    background.inputs['Color'].default_value = (.32, .34, .38, 1)
    background.inputs['Strength'].default_value = .35
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -.002))
    floor = bpy.context.object
    floor.name = 'STUDIO_Floor'
    mat = bpy.data.materials.new('STUDIO_NeutralFloor')
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (.24, .255, .28, 1)
    shader.inputs['Roughness'].default_value = .8
    floor.data.materials.append(mat)
    for name, position, power, size, color in [
        ('Key', (-3.5, -4.5, 5.5), 850, 4, (1, .91, .82)),
        ('Fill', (4, -3, 3), 500, 5, (.80, .88, 1)),
        ('Rim', (2, 3, 4.5), 1000, 3, (1, .94, .87)),
    ]:
        data = bpy.data.lights.new('STUDIO_'+name, 'AREA')
        data.energy, data.shape, data.size, data.color = power, 'DISK', size, color
        obj = bpy.data.objects.new(data.name, data)
        scene.collection.objects.link(obj)
        obj.location = position
        obj.rotation_euler = (Vector((0, 0, .9))-obj.location).to_track_quat('-Z', 'Y').to_euler()
    for name in VIEWS:
        obj = bpy.data.objects.new('STUDIO_Camera_'+name, bpy.data.cameras.new('STUDIO_Camera_'+name))
        scene.collection.objects.link(obj)
        obj.data.type = 'ORTHO'
        obj.data.clip_start, obj.data.clip_end = .01, 100
    return scene


def camera_view(name, body_scale, resolution):
    scene = bpy.context.scene
    cam = bpy.data.objects.get('STUDIO_Camera_'+name)
    if cam is None or cam.type != 'CAMERA':
        raise RuntimeError(f'Missing render camera for {name}')
    yaw, pitch = [math.radians(a) for a in VIEWS[name]]
    direction = Vector((math.sin(yaw)*math.cos(pitch), -math.cos(yaw)*math.cos(pitch), math.sin(pitch)))
    target = Vector((0, 0, HEIGHT_M*.83 if name == 'face' else HEIGHT_M*.49))
    cam.location = target + direction*12
    cam.rotation_euler = (target-cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.ortho_scale = .78 if name == 'face' else body_scale
    scene.camera = cam
    scene.render.resolution_x = resolution if name == 'face' else round(resolution*.75)
    scene.render.resolution_y = resolution
    return dict(yaw_degrees=VIEWS[name][0], elevation_degrees=VIEWS[name][1],
                target_m=list(target), vertical_window_m=cam.data.ortho_scale,
                resolution=[scene.render.resolution_x, scene.render.resolution_y])


def shared_body_scale(records):
    # All variants and all full-body angles use the same framing. Wide A/T poses
    # expand this window for every variant instead of silently cropping hands.
    width = 0.
    for record in records:
        lo, hi = record['normalization']['bounds_m']
        for name, (yaw, _) in VIEWS.items():
            if name == 'face':
                continue
            theta = math.radians(yaw)
            width = max(width, (hi[0]-lo[0])*abs(math.cos(theta)) + (hi[1]-lo[1])*abs(math.sin(theta)))
    return max(HEIGHT_M*1.25, width*1.12/.75)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--out', required=True, help='Relative paths resolve beside the manifest.')
    parser.add_argument('--samples', type=int, default=64)
    parser.add_argument('--resolution', type=int, default=1024, help='Image height; full-body width is 75%% of this.')
    parser.add_argument('--engine', choices=['eevee', 'cycles'], default='eevee')
    parser.add_argument('--views', nargs='+', choices=list(VIEWS), default=list(VIEWS), help='Render only these views during a quick review.')
    parser.add_argument('--no-render', action='store_true', help='Import, normalize, pack and save scenes/stats only.')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if args.samples < 1 or args.resolution < 64:
        raise ValueError('Samples must be positive; resolution must be at least 64.')
    manifest = Path(args.manifest).expanduser().resolve()
    variants = read_manifest(manifest)
    out = resolve(args.out, manifest.parent)
    out.mkdir(parents=True, exist_ok=True)
    records = []
    # Save each independently imported scene once, then use the full set's bounds
    # to assign identical camera windows before rendering the saved scenes.
    for variant in variants:
        source = Path(variant['path'])
        folder = out/variant['id']
        folder.mkdir(parents=True, exist_ok=True)
        blend = folder/(variant['id']+'_studio.blend')
        imported, meshes = import_asset(source, variant)
        normalization = normalize(imported, meshes, variant['front_rotation_degrees'])
        textures, warnings = texture_stats(source)
        if variant['force_opaque']:
            warnings.append('Explicit force_opaque override: imported Principled Alpha links removed and Alpha set to 1.')
        geometry = mesh_stats(meshes)
        if len([o for o in meshes if re.search(r'LOD[1-9]', o.name, re.I)]) > 0:
            warnings.append('Nonzero LOD names found: all imported renderable meshes are shown; inspect for duplicate overlapping LODs.')
        if not textures:
            warnings.append('No image textures found; imported material nodes/colors are retained.')
        with source.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        record = dict(**variant, source_sha256=digest,
                      normalization=normalization, **geometry, textures=textures, warnings=warnings,
                      blend=str(blend), renders={}, render_engine=args.engine, samples=args.samples)
        studio(args.samples, args.engine)
        bpy.context.scene['variant_id'], bpy.context.scene['variant_label'] = variant['id'], variant['label']
        bpy.context.scene['source_mesh_path'] = str(source)
        bpy.context.scene['presentation_notes'] = 'Actual imported geometry; uniform scale and yaw; explicit material/object overrides recorded in stats.json.'
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(blend))
        records.append(record)
        print('VARIANT_PREPARED', variant['id'], geometry['total_triangles'], flush=True)
    common_scale = shared_body_scale(records)
    for record in records:
        bpy.ops.wm.open_mainfile(filepath=record['blend'])
        folder = Path(record['blend']).parent
        cameras = {name: camera_view(name, common_scale, args.resolution) for name in VIEWS}
        if not args.no_render:
            for name in args.views:
                camera_view(name, common_scale, args.resolution)
                path = folder/(name+'.png')
                bpy.context.scene.render.filepath = str(path)
                if bpy.context.scene.camera is None:
                    raise RuntimeError('Render requires an active camera.')
                bpy.ops.render.render(write_still=True)
                if not path.is_file() or path.stat().st_size == 0:
                    raise RuntimeError(f'Render did not produce a PNG: {path}')
                record['renders'][name] = str(path)
        record['cameras'] = cameras
        camera_view('threequarter', common_scale, args.resolution)
        bpy.context.scene.render.filepath = str(folder/'threequarter.png')
        bpy.ops.wm.save_as_mainfile(filepath=record['blend'])
        write_json(folder/'stats.json', record)
        print('VARIANT_COMPLETE', record['id'], flush=True)
    write_json(out/'render-manifest.json', dict(blender_version=bpy.app.version_string,
        input_manifest=str(manifest), standing_height_m=HEIGHT_M, body_vertical_window_m=common_scale,
        background='Opaque neutral studio', materials='Imported nodes preserved except explicit per-variant overrides; available images packed',
        rendered=not args.no_render, variants=records))


if __name__ == '__main__':
    main()
