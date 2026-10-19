"""Build Pak Mat design studies locally, then export self-contained real meshes.

Run using Blender --background --factory-startup --python this_file -- --out ...
The static designs are an art-direction exploration, not production rigged assets.
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import sys

import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from local_geometry import build_variant
from render_variants import bounds, mesh_stats, normalize, texture_stats

LABELS = {'A': 'Warm animated film', 'B': 'Graphic comic',
          'C': 'Handmade miniature', 'D': 'Grounded stylized'}


def write_json(path, data):
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding='utf-8')


def validate_meshes(objects):
    failures = []
    for obj in objects:
        if obj.type != 'MESH':
            continue
        if not obj.data.vertices or not obj.data.polygons:
            failures.append(f'{obj.name}: empty mesh')
        if any(not math.isfinite(v) for vert in obj.data.vertices for v in vert.co):
            failures.append(f'{obj.name}: non-finite coordinates')
    if failures:
        raise RuntimeError('\n'.join(failures))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', required=True)
    parser.add_argument('--only', choices=list(LABELS), nargs='+', default=list(LABELS))
    parser.add_argument('--clay', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    manifest = []
    for identifier in args.only:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.context.scene.unit_settings.system = 'METRIC'
        bpy.context.scene.unit_settings.scale_length = 1
        bpy.context.preferences.filepaths.save_version = 0
        result = build_variant(identifier)
        objects = result['objects']
        meshes = [o for o in objects if o.type == 'MESH' and not o.hide_render]
        if not meshes:
            raise RuntimeError(f'{identifier}: builder produced no meshes')
        validate_meshes(meshes)
        material_metadata = {}
        if args.clay:
            mat = bpy.data.materials.new('ReviewClay')
            mat.diffuse_color = (.32, .35, .4, 1)
            mat.use_nodes = True
            shader = mat.node_tree.nodes.get('Principled BSDF')
            shader.inputs['Base Color'].default_value = mat.diffuse_color
            shader.inputs['Roughness'].default_value = .8
            for obj in meshes:
                obj.data.materials.clear()
                obj.data.materials.append(mat)
        else:
            from local_materials import apply_materials
            material_metadata = apply_materials(identifier, objects, result['roles'])
        normalization = normalize(objects, meshes, 0)
        bpy.context.view_layer.update()
        # Apply presentation transform into mesh objects for a clean metre-scaled export.
        for obj in meshes:
            matrix = obj.matrix_world.copy()
            obj.parent = None
            obj.matrix_world = matrix
        bpy.ops.object.select_all(action='DESELECT')
        for obj in meshes:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        for obj in list(bpy.context.scene.objects):
            if obj.type == 'EMPTY' and not obj.children:
                bpy.data.objects.remove(obj, do_unlink=True)
        folder = out/'assets'/identifier
        folder.mkdir(parents=True, exist_ok=True)
        source = folder/f'pak_mat_{identifier}_master.blend'
        glb = folder/f'pak_mat_{identifier}.glb'
        textures, warnings = texture_stats(source)
        missing = [r['name'] for r in textures if not r['loaded']]
        if missing:
            raise RuntimeError(f'Missing texture data: {missing}')
        scene = bpy.context.scene
        scene['asset_scope'] = 'Static art-direction study; no rig, animation or game integration.'
        scene['variant_id'], scene['variant_label'] = identifier, LABELS[identifier]
        scene['provenance'] = 'Locally authored geometry and texture patterns; Scenario images used as visual references only.'
        bpy.ops.wm.save_as_mainfile(filepath=str(source))
        bpy.ops.export_scene.gltf(filepath=str(glb), export_format='GLB',
            use_selection=True, export_apply=True, export_animations=False,
            export_cameras=False, export_lights=False, export_yup=True)
        if not glb.is_file() or glb.stat().st_size < 1024:
            raise RuntimeError(f'Export missing or unexpectedly small: {glb}')
        with glb.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        stats = mesh_stats(meshes)
        write_json(folder/'asset-manifest.json', dict(id=identifier, label=LABELS[identifier],
            blender_version=bpy.app.version_string, source_blend=str(source), glb=str(glb),
            glb_sha256=digest, source='Local Blender geometry and original texture maps',
            scope='Static design study; unrigged, no animations, no runtime replacement',
            inferred='Back, hidden anatomy, garment construction and all metric proportions interpreted for this study.',
            normalization=normalization, geometry=result.get('metadata', {}),
            material_authoring=material_metadata, textures=textures, warnings=warnings, **stats))
        manifest.append(dict(id=identifier, label=LABELS[identifier],
            path=glb.relative_to(out).as_posix(), front_rotation_degrees=0))
        print('LOCAL_VARIANT_EXPORTED', identifier, stats['total_triangles'], str(glb), flush=True)
    suffix = '-clay' if args.clay else ''
    path = out/f'variants{suffix}.json'
    # A partial rebuild retains other existing manifest entries without rebuilding them.
    previous = json.loads(path.read_text(encoding='utf-8')) if path.exists() else []
    combined = {r['id']: r for r in previous}
    combined.update({r['id']: r for r in manifest})
    write_json(path, [combined[i] for i in LABELS if i in combined])


if __name__ == '__main__':
    main()
