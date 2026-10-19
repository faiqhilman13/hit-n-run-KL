"""Portable material direction for the four locally sculpted Pak Mat variants.

Called inside Blender 5.2: apply_materials('A', mesh_objects, {name: role}).
All visible surface detail is standard Principled + packed image textures. No
reference images, generated image projections, baked illumination or toon shader
nodes are used. Geometric sculpting/pose remains local_geometry's responsibility.
"""
from pathlib import Path
import json
import math

import bpy


TEXTURE_ROOT = Path(__file__).resolve().parent/'textures'/'local_styles'
MATERIAL_VERSION = 4
STYLES = {
    'A': dict(label='Warm animated film', batik_uv=(5.3, 2.6), sarong_uv=(6.0, 3.1),
              skin=(189, 126, 86), hair=(31, 27, 24), shirt=(198, 74, 22),
              sarong=(39, 65, 44), leather=(101, 56, 30), skin_rough=.58,
              eye_rough=.32, iris=(117, 70, 28), cloth_sheen=.015),
    'B': dict(label='Graphic comic', batik_uv=(3.3, 1.55), sarong_uv=(5.3, 2.7),
              skin=(190, 124, 82), hair=(32, 25, 24), shirt=(201, 73, 22),
              sarong=(29, 58, 45), leather=(112, 58, 30), skin_rough=.68,
              eye_rough=.41, iris=(108, 59, 25), cloth_sheen=.0),
    'C': dict(label='Handmade miniature', batik_uv=(4.75, 2.35), sarong_uv=(5.8, 3.0),
              skin=(183, 129, 93), hair=(37, 30, 25), shirt=(178, 66, 24),
              sarong=(36, 60, 41), leather=(107, 62, 34), skin_rough=.78,
              eye_rough=.38, iris=(99, 63, 29), cloth_sheen=.035),
    'D': dict(label='Grounded stylized', batik_uv=(5.7, 2.85), sarong_uv=(6.8, 3.7),
              skin=(178, 122, 85), hair=(29, 26, 24), shirt=(177, 66, 24),
              sarong=(38, 65, 48), leather=(88, 51, 29), skin_rough=.62,
              eye_rough=.30, iris=(94, 56, 26), cloth_sheen=.025),
}

ALIASES = {
    'shirt': 'batik', 'shirt_body': 'batik', 'sleeve': 'batik', 'collar': 'batik',
    'pants': 'sarong', 'skirt': 'sarong', 'sarung': 'sarong',
    'hat': 'songkok', 'cap': 'songkok', 'moustache': 'hair', 'mustache': 'hair',
    'eyebrow': 'hair', 'eyebrows': 'hair', 'brows': 'hair',
    'eye': 'eye_white', 'eyewhite': 'eye_white', 'sclera': 'eye_white',
    'shoe': 'sandal', 'sandals': 'sandal', 'leather': 'sandal',
    'teeth_upper': 'teeth', 'nails': 'nail', 'lip': 'lips',
    'skin_face': 'skin', 'face': 'skin', 'ear': 'skin', 'hands': 'skin', 'foot': 'skin',
    'seam': 'batik_trim', 'shirt_trim': 'batik_trim', 'shirt_seam': 'batik_trim',
    'sarong_seam': 'sarong_trim', 'placket': 'batik_trim', 'button': 'button',
}
TEXTURED = {'skin': 'skin', 'batik': 'batik', 'sarong': 'sarong',
            'songkok': 'songkok', 'sandal': 'leather'}


def set_input(node, name, value):
    for socket in node.inputs:
        if socket.name == name:
            socket.default_value = value
            return True
    return False


def srgb_linear(color):
    def linear(x):
        x = x/255.
        return x/12.92 if x <= .04045 else ((x+.055)/1.055)**2.4
    return tuple(linear(float(c)) for c in color)+(1.,)


def canonical_role(role):
    role = str(role).lower().replace(' ', '_').replace('-', '_')
    return ALIASES.get(role, role)


def image_node(tree, path, colorspace, location):
    path = Path(path)
    if not path.is_file():
        raise FileNotFoundError(f'Missing authored material texture: {path}. Run make_local_textures.py with ordinary Python first.')
    # Separate datablocks keep colorspace metadata deterministic if the caller
    # retains a scene from another material pass.
    image = bpy.data.images.load(str(path), check_existing=True)
    image.colorspace_settings.name = colorspace
    # Ensure embedded glTF images are fully decoded before packing/export.
    if image.size[0] > 0:
        _ = image.pixels[0]
    if not image.packed_file:
        image.pack()
    node = tree.nodes.new('ShaderNodeTexImage')
    node.name = node.label = path.stem
    node.image = image
    node.interpolation = 'Linear'
    node.extension = 'REPEAT'
    node.location = location
    return node


def make_material(variant, role, root, radial_iris=True):
    p = STYLES[variant]
    material = bpy.data.materials.new(f'LOCAL_{variant}_{role}_v{MATERIAL_VERSION}')
    material.use_nodes = True
    material.use_backface_culling = False
    material['local_variant'] = variant
    material['semantic_role'] = role
    material['surface_contract'] = 'Opaque Principled; original unlit image maps; tangent-space normals'
    nt = material.node_tree
    nt.nodes.clear()
    output = nt.nodes.new('ShaderNodeOutputMaterial')
    output.location = (650, 150)
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.location = (300, 150)
    nt.links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
    set_input(bsdf, 'Metallic', 0.)
    set_input(bsdf, 'Roughness', .72)
    set_input(bsdf, 'Alpha', 1.)
    set_input(bsdf, 'IOR', 1.45)
    set_input(bsdf, 'Specular IOR Level', .30)
    set_input(bsdf, 'Coat Weight', 0.)
    set_input(bsdf, 'Transmission Weight', 0.)
    # Standard glTF has no equivalent of Blender random-walk skin scattering.
    # Keeping this off guarantees roundtripped materials retain the same finish.
    set_input(bsdf, 'Subsurface Weight', 0.)

    trim_shirt = tuple(int(c*.80) for c in p['shirt'])
    trim_sarong = tuple(int(c*.83) for c in p['sarong'])
    colors = {
        'skin': p['skin'], 'batik': p['shirt'], 'sarong': p['sarong'],
        'batik_trim': trim_shirt, 'sarong_trim': trim_sarong,
        'hair': p['hair'], 'songkok': (29, 29, 27), 'eye_white': (238, 226, 200),
        'iris': p['iris'], 'pupil': (17, 13, 10), 'mouth': (65, 23, 21),
        'lips': (139, 72, 50), 'teeth': (233, 215, 182), 'tongue': (158, 74, 65),
        'sandal': p['leather'], 'sole': (48, 35, 24),
        'nail': tuple(int(c*.73+t*.27) for c, t in zip(p['skin'], (234, 188, 148))),
        'button': (122, 67, 34), 'stitch': (166, 118, 73),
    }
    if role not in colors:
        raise ValueError(f'Unknown material role {role!r}; assign a semantic role before rendering.')
    rgba = srgb_linear(colors[role])
    set_input(bsdf, 'Base Color', rgba)
    material.diffuse_color = rgba
    roughness = {'hair': .73 if variant in {'B', 'C'} else .62,
                 'eye_white': p['eye_rough'], 'iris': p['eye_rough']+.05,
                 'pupil': p['eye_rough']+.04, 'mouth': .76, 'teeth': .48,
                 'lips': .63, 'tongue': .66, 'nail': .55,
                 'batik_trim': .84, 'sarong_trim': .85, 'sole': .88,
                 'button': .62, 'stitch': .84}
    set_input(bsdf, 'Roughness', roughness.get(role, .78))
    if role in {'eye_white', 'iris', 'pupil'}:
        set_input(bsdf, 'Specular IOR Level', .36)
        set_input(bsdf, 'IOR', 1.38)
    if role in {'batik', 'sarong', 'batik_trim', 'sarong_trim', 'songkok'}:
        # Blender's glTF exporter writes Sheen Tint but does not multiply a
        # fractional Sheen Weight into it. Encode intensity in the tint instead
        # so a GLB roundtrip cannot turn restrained fabric sheen into white fuzz.
        sheen = p['cloth_sheen']
        set_input(bsdf, 'Sheen Weight', 1. if sheen else 0.)
        set_input(bsdf, 'Sheen Tint', (sheen, sheen, sheen, 1.))
        set_input(bsdf, 'Sheen Roughness', .65)
    if role == 'skin':
        set_input(bsdf, 'Specular IOR Level', .23 if variant != 'D' else .28)

    texture_role = TEXTURED.get(role)
    paths = {}
    if texture_role or (role == 'iris' and radial_iris):
        uv = nt.nodes.new('ShaderNodeUVMap')
        uv.name = uv.label = 'Explicit garment / part UV'
        uv.uv_map = 'MaterialUV'
        uv.location = (-1000, 200)
        texture_role = texture_role or 'iris'
        basepath = root/variant/f'{texture_role}_basecolor.png'
        base = image_node(nt, basepath, 'sRGB', (-730, 400))
        nt.links.new(uv.outputs['UV'], base.inputs['Vector'])
        nt.links.new(base.outputs['Color'], bsdf.inputs['Base Color'])
        paths['basecolor'] = str(basepath)
        if texture_role != 'iris':
            ormpath = root/variant/f'{texture_role}_orm.png'
            packed = image_node(nt, ormpath, 'Non-Color', (-730, 100))
            nt.links.new(uv.outputs['UV'], packed.inputs['Vector'])
            split = nt.nodes.new('ShaderNodeSeparateColor')
            split.mode = 'RGB'
            split.location = (-380, 100)
            nt.links.new(packed.outputs['Color'], split.inputs['Color'])
            nt.links.new(split.outputs['Green'], bsdf.inputs['Roughness'])
            # Blue is uniformly zero: these are all dielectric materials.
            nt.links.new(split.outputs['Blue'], bsdf.inputs['Metallic'])
            normalpath = root/variant/f'{texture_role}_normal.png'
            normal_image = image_node(nt, normalpath, 'Non-Color', (-730, -220))
            nt.links.new(uv.outputs['UV'], normal_image.inputs['Vector'])
            normal = nt.nodes.new('ShaderNodeNormalMap')
            normal.space = 'TANGENT'
            normal.uv_map = 'MaterialUV'
            normal.location = (-180, -190)
            normal.inputs['Strength'].default_value = (
                {'A': .10, 'B': .0, 'C': .15, 'D': .08}[variant]
                if role == 'skin' else 1.)
            nt.links.new(normal_image.outputs['Color'], normal.inputs['Color'])
            nt.links.new(normal.outputs['Normal'], bsdf.inputs['Normal'])
            paths.update(orm=str(ormpath), normal=str(normalpath))
    material['texture_files'] = json.dumps(paths)
    return material, paths


def ensure_material_uv(obj, role, variant):
    """Copy authored UVs, then tile the copies. Source UVMap is untouched."""
    mesh = obj.data
    base = mesh.uv_layers.get('UVMap') or mesh.uv_layers.active
    fallback = base is None
    if base is None:
        base = mesh.uv_layers.new(name='UVMap')
        coords = [obj.matrix_world @ v.co for v in mesh.vertices]
        low = min(v.z for v in coords)
        height = max(max(v.z for v in coords)-low, .0001)
        for polygon in mesh.polygons:
            us = [math.atan2(coords[mesh.loops[i].vertex_index].x,
                             -coords[mesh.loops[i].vertex_index].y)/(2*math.pi)+.5
                  for i in polygon.loop_indices]
            crosses = max(us)-min(us) > .5
            for i, u in zip(polygon.loop_indices, us):
                v = coords[mesh.loops[i].vertex_index]
                base.data[i].uv = (u+int(crosses and u < .5), (v.z-low)/height)
    layer = mesh.uv_layers.get('MaterialUV') or mesh.uv_layers.new(name='MaterialUV')
    scale = {'batik': STYLES[variant]['batik_uv'],
             'sarong': STYLES[variant]['sarong_uv'],
             'songkok': (3., 1.5), 'sandal': (2.0, 2.0),
             'skin': (2.0, 2.0)}.get(role, (1., 1.))
    for index, loop in enumerate(base.data):
        layer.data[index].uv = (loop.uv.x*scale[0], loop.uv.y*scale[1])
    layer.active_render = True
    mesh.uv_layers.active = layer
    return dict(source=base.name, material_uv='MaterialUV', scale=list(scale), fallback_cylindrical=fallback)


def role_for(obj, roles):
    if isinstance(roles, dict):
        value = roles.get(obj.name)
        if value is None:
            # Also accept role -> object(s), useful for a hand-authored study.
            for key, members in roles.items():
                members = members if isinstance(members, (list, tuple, set)) else [members]
                if obj in members or obj.name in members:
                    value = key
                    break
        if value is not None:
            return canonical_role(value)
    value = obj.get('semantic_role', obj.get('role'))
    if value:
        return canonical_role(value)
    raise ValueError(f'Missing semantic material role for {obj.name!r}.')


def apply_materials(variant_id, objects, roles, *, texture_dir=None):
    """Assign and pack one surface material per semantic mesh object.

    objects: iterable of bpy mesh objects (or a name -> object dictionary).
    roles: {object.name: semantic_role}. Unknown roles fail explicitly.
    Returns JSON-safe material, texture and UV metadata for the build manifest.
    Existing geometry, normals, material-independent UVMap and pose are preserved.
    """
    variant = str(variant_id).upper()
    if variant not in STYLES:
        raise ValueError(f'Expected variant A, B, C or D, got {variant_id!r}')
    root = Path(texture_dir).resolve() if texture_dir else TEXTURE_ROOT
    if not (root/'texture-manifest.json').is_file():
        raise FileNotFoundError(f'Authored textures missing in {root}. Run make_local_textures.py first.')
    values = list(objects.values()) if isinstance(objects, dict) else list(objects)
    objects = [obj for obj in values if obj.type == 'MESH']
    if not objects:
        raise ValueError('No mesh objects supplied to apply_materials.')
    material_cache, material_metadata, assignments = {}, {}, []
    for obj in objects:
        role = role_for(obj, roles)
        # The radial pigment texture requires an explicitly declared disk UV.
        # Without that contract a brown iris is safer than a misplaced pupil.
        radial_iris = bool(obj.get('radial_iris_uv', False))
        cache_key = (role, radial_iris if role == 'iris' else False)
        if cache_key not in material_cache:
            mat, paths = make_material(variant, role, root, radial_iris)
            material_cache[cache_key] = mat
            material_metadata[mat.name] = dict(role=role, textures=paths,
                                                radial_iris=radial_iris if role == 'iris' else None)
        material = material_cache[cache_key]
        uv = ensure_material_uv(obj, role, variant)
        obj.data.materials.clear()
        obj.data.materials.append(material)
        for polygon in obj.data.polygons:
            polygon.material_index = 0
        obj['semantic_role'] = role
        obj['local_material_variant'] = variant
        assignments.append(dict(object=obj.name, role=role, material=material.name, uv=uv))
    return dict(version=MATERIAL_VERSION, variant=variant, style=STYLES[variant]['label'],
                texture_root=str(root), materials=material_metadata, objects=assignments,
                portable=True, maps='sRGB basecolor; linear ORM (R=1, G=roughness, B=0); OpenGL tangent normals',
                notes=['Authored repeating leaf sprigs and uneven-band plaid, not reference projections.',
                       'No directional lights, shadows, ambient occlusion or highlights baked into textures.',
                       'All materials opaque dielectric Principled. No render-only toon or procedural nodes.',
                       'UVMap preserved; MaterialUV copied and tiled explicitly; images packed.'])
