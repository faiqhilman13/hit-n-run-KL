"""Verify the generated design-study delivery against the actual GLB/PNG files."""
from pathlib import Path
from html.parser import HTMLParser
import argparse
import hashlib
import json
import struct

from PIL import Image


class Links(HTMLParser):
    def __init__(self):
        super().__init__()
        self.paths = []

    def handle_starttag(self, tag, attrs):
        for key, value in attrs:
            if key in {'src', 'href'} and value and not value.startswith(('http:', 'https:', '#')):
                self.paths.append(value)


def verify(root):
    render = json.loads((root/'renders/render-manifest.json').read_text(encoding='utf-8'))
    assert render['rendered'] is True
    result = dict(scope='Static mesh, texture, framing and file-delivery verification; not rig or game acceptance.',
                  blender_version=render['blender_version'], variants=[])
    camera_baseline = None
    for identifier in 'ABCD':
        folder = root/'assets'/identifier
        authored = json.loads((folder/'asset-manifest.json').read_text(encoding='utf-8'))
        imported = next(r for r in render['variants'] if r['id'] == identifier)
        path = folder/f'pak_mat_{identifier}.glb'
        data = path.read_bytes()
        magic, version, length = struct.unpack_from('<4sII', data)
        assert magic == b'glTF' and version == 2 and length == len(data)
        size, kind = struct.unpack_from('<I4s', data, 12)
        assert kind == b'JSON'
        document = json.loads(data[20:20+size])
        assert not any('uri' in b for b in document.get('buffers', [])), 'External buffer dependency'
        assert not document.get('skins') and not document.get('animations'), 'Study scope changed'
        images = document.get('images', [])
        assert images and all('bufferView' in img and 'uri' not in img for img in images), 'Unpacked texture'
        accessors = document['accessors']
        triangles = 0
        for mesh in document['meshes']:
            for primitive in mesh['primitives']:
                assert primitive.get('mode', 4) == 4
                assert 'POSITION' in primitive['attributes'] and 'NORMAL' in primitive['attributes']
                assert 'TEXCOORD_1' in primitive['attributes']
                count = accessors[primitive['indices']]['count']
                assert count % 3 == 0
                triangles += count//3
        digest = hashlib.sha256(data).hexdigest()
        assert digest == authored['glb_sha256'] == imported['source_sha256']
        assert triangles == authored['total_triangles'] == imported['total_triangles']
        assert abs(imported['normalization']['height_m']-1.66) < 1e-5
        assert abs(imported['normalization']['floor_z_m']) < 1e-5
        assert not imported['warnings'], imported['warnings']
        assert imported['textures'] and all(t['loaded'] and t['packed'] for t in imported['textures'])
        if camera_baseline is None:
            camera_baseline = imported['cameras']
        assert camera_baseline == imported['cameras'], 'Mismatched comparison framing'
        dimensions = {}
        for view in ['front', 'threequarter', 'profile', 'face']:
            png = root/'renders'/identifier/f'{view}.png'
            with Image.open(png) as image:
                image.verify()
            with Image.open(png) as image:
                dimensions[view] = list(image.size)
                assert list(image.size) == imported['cameras'][view]['resolution']
        for blend in [folder/f'pak_mat_{identifier}_master.blend', root/'renders'/identifier/f'{identifier}_studio.blend']:
            assert blend.is_file() and blend.stat().st_size > 10000
        result['variants'].append(dict(id=identifier, triangles=triangles, meshes=len(document['meshes']),
            embedded_images=len(images), materials=len(document['materials']),
            glb_bytes=len(data), sha256=digest, renders=dimensions, passed=True))
    parser = Links()
    parser.feed((root/'index.html').read_text(encoding='utf-8'))
    assert all((root/path).is_file() for path in parser.paths), 'Broken gallery asset link'
    result['gallery_local_links_verified'] = len(parser.paths)
    result['passed'] = True
    (root/'verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    verify(Path(parser.parse_args().folder).resolve())
