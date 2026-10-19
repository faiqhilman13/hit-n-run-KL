"""Author seamless, unlit character textile/PBR textures for the four local studies.

Run with ordinary Python (Pillow + NumPy), not Blender. These are original vector
leaf motifs and synthetic material measurements, never projections of references.
The resulting PNGs are consumed directly by portable Principled materials.
"""
from pathlib import Path
import argparse
import json
import math

import numpy as np
from PIL import Image, ImageDraw


VERSION = 4
DEFAULT_OUT = Path(__file__).resolve().parent / 'textures' / 'local_styles'
STYLES = {
    'A': dict(label='Warm animated film', orange=(198, 74, 22), leaf=(105, 42, 27),
              gold=(244, 178, 80), vein=(238, 153, 71), green=(39, 65, 44),
              stripe=(104, 120, 70), pin=(151, 153, 98), skin=(189, 126, 86),
              hair=(31, 27, 24), hat=(30, 29, 26), leather=(101, 56, 30),
              cloth_grain=.007, cloth_normal=.20, cloth_rough=.78, skin_normal=.045,
              skin_rough=.58, leather_normal=.18, scale=1.0),
    'B': dict(label='Graphic comic', orange=(201, 73, 22), leaf=(92, 33, 28),
              gold=(240, 164, 65), vein=(227, 127, 40), green=(29, 58, 45),
              stripe=(96, 112, 65), pin=(149, 150, 89), skin=(190, 124, 82),
              hair=(32, 25, 24), hat=(31, 29, 27), leather=(112, 58, 30),
              cloth_grain=.001, cloth_normal=.06, cloth_rough=.84, skin_normal=0.,
              skin_rough=.68, leather_normal=.04, scale=1.09),
    'C': dict(label='Handmade miniature', orange=(178, 66, 24), leaf=(103, 37, 28),
              gold=(226, 158, 66), vein=(216, 135, 58), green=(36, 60, 41),
              stripe=(97, 113, 64), pin=(145, 145, 94), skin=(183, 129, 93),
              hair=(37, 30, 25), hat=(35, 33, 28), leather=(107, 62, 34),
              cloth_grain=.025, cloth_normal=.38, cloth_rough=.88, skin_normal=.12,
              skin_rough=.78, leather_normal=.18, scale=1.0),
    'D': dict(label='Grounded stylized', orange=(177, 66, 24), leaf=(112, 48, 31),
              gold=(224, 159, 87), vein=(226, 166, 99), green=(38, 65, 48),
              stripe=(93, 111, 72), pin=(139, 146, 99), skin=(178, 122, 85),
              hair=(29, 26, 24), hat=(29, 30, 28), leather=(88, 51, 29),
              cloth_grain=.017, cloth_normal=.28, cloth_rough=.83, skin_normal=.075,
              skin_rough=.62, leather_normal=.20, scale=.85),
}


def bezier(a, b, c, d, count=24):
    t = np.linspace(0., 1., count)[:, None]
    return (1-t)**3*np.array(a)+3*(1-t)**2*t*np.array(b)+3*(1-t)*t*t*np.array(c)+t**3*np.array(d)


def transform(points, center, angle, scale, offset=(0., 0.)):
    p = np.asarray(points, dtype=float) * scale
    cs, sn = math.cos(angle), math.sin(angle)
    return np.column_stack((p[:, 0]*cs-p[:, 1]*sn, p[:, 0]*sn+p[:, 1]*cs)) + np.array(center) + np.array(offset)


def draw_sprig(draw, n, center, angle, scale, fill, vein, fine=False, offset=(0., 0.)):
    """One intentionally designed curving branch with seven tapered leaves."""
    def xy(points):
        return [tuple(p) for p in transform(points, center, angle, scale, offset)*n]

    stem = bezier((-.018, .252), (.053, .11), (-.049, -.072), (.018, -.25), 45)
    draw.line(xy(stem), fill=fill, width=max(1, round(n*.0055*scale)), joint='curve')
    stations = [(.155, -1), (.10, 1), (.018, -1), (-.045, 1), (-.12, -1), (-.17, 1)]
    for i, (height, side) in enumerate(stations):
        root = np.array((.012*math.sin(height*13), height))
        tip = root + (side*(.102 if i < 4 else .082), -.10 if i < 4 else -.078)
        axis = tip-root
        perp = np.array((-axis[1], axis[0]))
        perp = perp/np.linalg.norm(perp)*(.022 if i < 4 else .018)
        upper = bezier(root, root+axis*.29+perp, root+axis*.77+perp*.87, tip, 20)
        lower = bezier(tip, root+axis*.73-perp*.82, root+axis*.22-perp*.75, root, 20)
        draw.polygon(xy(np.concatenate((upper, lower))), fill=fill)
        vein_end = root+axis*.83
        draw.line(xy([root+axis*.05, vein_end]), fill=vein, width=max(1, round(n*.0016*scale)))
        if fine:
            for t in (.34, .53, .69):
                middle = root+axis*t
                for sign in (-1, 1):
                    draw.line(xy([middle, middle+axis*.11+perp*sign*.58]),
                              fill=vein, width=max(1, round(n*.0007*scale)))
    root, tip = np.array((.017, -.215)), np.array((.027, -.315))
    upper = bezier(root, (-.022, -.248), (.002, -.285), tip)
    lower = bezier(tip, (.048, -.273), (.047, -.236), root)
    draw.polygon(xy(np.concatenate((upper, lower))), fill=fill)
    draw.line(xy([root, tip*.94]), fill=vein, width=max(1, round(n*.0015*scale)))


def leaf_pattern(style, size):
    """Two interlocking diagonal branches; boundary copies make a seamless tile."""
    p = STYLES[style]
    aa = 2
    n = size*aa
    im = Image.new('RGB', (n, n), p['orange'])
    d = ImageDraw.Draw(im)
    motifs = [((.235, .24), math.radians(-31), .88, p['leaf']),
              ((.735, .73), math.radians(31), .86, p['gold'])]
    if style == 'D':
        motifs += [((.77, .23), math.radians(-65), .44, p['gold']),
                   ((.21, .76), math.radians(65), .42, p['leaf'])]
    for center, angle, scale, fill in motifs:
        # The internal leaf veins are tinted toward the cloth base; they do not
        # imply lighting or emboss the dye into the surface.
        vein = tuple(int(a*.58+b*.42) for a, b in zip(fill, p['orange']))
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                draw_sprig(d, n, center, angle, scale*p['scale'], fill, vein,
                           fine=style in {'C', 'D'}, offset=(dx, dy))
    return np.asarray(im.resize((size, size), Image.Resampling.LANCZOS), dtype=np.float32)/255.


def fields(size, seed):
    """Periodic fields, with the same values/slope at every repeat boundary."""
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)/size
    rng = np.random.default_rng(seed)
    broad = np.zeros((size, size), np.float32)
    fine = np.zeros_like(broad)
    for frequency, amplitude, output in [(3, .43, broad), (7, .22, broad),
                                          (13, .15, broad), (29, .1, broad),
                                          (47, .43, fine), (89, .31, fine),
                                          (149, .20, fine)]:
        phase = rng.uniform(0, 2*math.pi)
        output += amplitude*np.sin(2*math.pi*(x*frequency+y*(frequency//2+1))+phase)*np.cos(2*math.pi*y*(frequency+3)-phase*.7)
    # Orthogonal and diagonal yarns, not shading. Multiplication creates the
    # alternating over-under points of a small twill weave.
    warp = np.sin(2*math.pi*x*160)
    weft = np.sin(2*math.pi*y*160)
    weave = .35*warp+.35*weft+.22*warp*weft+.08*np.sin(2*math.pi*(x-y)*80)
    return x, y, broad, fine, weave


def periodic_band(position, center, width, feather=.002):
    distance = np.abs((position-center+.5) % 1. - .5)
    return np.clip((width*.5-distance)/feather+.5, 0., 1.)


def plaid_pattern(style, size):
    p = STYLES[style]
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)/size
    base = np.broadcast_to(np.array(p['green'], dtype=float)/255., (size, size, 3)).copy()
    stripe = np.array(p['stripe'])/255.
    pin = np.array(p['pin'])/255.
    # Unequal broad/narrow bands keep the plaid from reading like graph paper.
    for coord, strength in ((x, .74), (y, .65)):
        main = periodic_band(coord, .27, .19)
        secondary = periodic_band(coord, .75, .060)
        mask = np.maximum(main, secondary*.72)*strength
        base = base*(1-mask[..., None])+stripe*mask[..., None]
    # Woven intersections brighten because both warp and weft are olive yarn.
    crossing = periodic_band(x, .27, .19)*periodic_band(y, .27, .19)
    base = base*(1-crossing[..., None]*.30)+stripe*crossing[..., None]*.30
    for coord in (x, y):
        for center in (.158, .382, .706, .794):
            line = periodic_band(coord, center, .0055, .0015)
            base = base*(1-line[..., None]*.57)+pin*line[..., None]*.57
        dark = periodic_band(coord, .51, .013)
        base *= 1-dark[..., None]*.23
    return base


def normal_from_height(height, amount):
    dx = (np.roll(height, -1, 1)-np.roll(height, 1, 1))*.5*amount
    dy = (np.roll(height, -1, 0)-np.roll(height, 1, 0))*.5*amount
    normal = np.stack((-dx, dy, np.ones_like(dx)), axis=-1)
    normal /= np.maximum(np.linalg.norm(normal, axis=-1, keepdims=True), 1e-6)
    return normal*.5+.5


def save_image(path, values):
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.clip(np.round(values*255), 0, 255).astype(np.uint8)).save(path, compress_level=8)


def orm(roughness):
    return np.stack((np.ones_like(roughness), np.clip(roughness, .02, 1.), np.zeros_like(roughness)), axis=-1)


def make_style(style, out, size=1024):
    p = STYLES[style]
    root = out/style
    x, y, broad, fine, weave = fields(size, ord(style)*79)
    texture_metadata = {}

    def triplet(role, base, rough, height, normal_amount):
        paths = {}
        for suffix, pixels in [('basecolor', base), ('orm', orm(rough)),
                               ('normal', normal_from_height(height, normal_amount))]:
            path = root/f'{role}_{suffix}.png'
            save_image(path, pixels)
            paths[suffix] = str(path.relative_to(out))
        texture_metadata[role] = paths

    modulation = (weave*.65+fine*.15+broad*.20)*p['cloth_grain']
    cloth_height = weave*.70+fine*.10+broad*.035
    for role, base in [('batik', leaf_pattern(style, size)), ('sarong', plaid_pattern(style, size))]:
        # Dye and yarn variation have no relation to a light direction.
        color = np.clip(base*(1+modulation[..., None]), 0., 1.)
        rough = p['cloth_rough']+weave*.022+fine*.012
        triplet(role, color, rough, cloth_height, p['cloth_normal'])

    skin_base = np.broadcast_to(np.array(p['skin'])/255., (size, size, 3)).copy()
    amplitude = {'A': .006, 'B': .0, 'C': .017, 'D': .014}[style]
    skin_base *= 1+(fine*.40+broad*.22)[..., None]*amplitude
    skin_rough = p['skin_rough']+fine*.014+broad*.012
    # Fine pores/fingermarks for miniature C; only a very subtle pore field for D.
    clay = fine*.65+broad*.18+np.sin(x*2*math.pi*101)*np.sin(y*2*math.pi*97)*.05
    triplet('skin', skin_base, skin_rough, clay, p['skin_normal'])

    leather_base = np.broadcast_to(np.array(p['leather'])/255., (size, size, 3)).copy()
    leather_base *= 1+(fine*.32+broad*.30)[..., None]*({'A': .045, 'B': .006, 'C': .10, 'D': .08}[style])
    leather_height = np.abs(fine)*.80+broad*.075
    triplet('leather', leather_base, .70+fine*.04, leather_height, p['leather_normal'])

    hat_base = np.broadcast_to(np.array(p['hat'])/255., (size, size, 3)).copy()
    hat_base *= 1+weave[..., None]*({'A': .025, 'B': .005, 'C': .09, 'D': .07}[style])
    triplet('songkok', hat_base, .86+weave*.022, weave*.7, p['cloth_normal']*.75)

    # The iris is a flat radial pigment map. Corneal highlights are solely from
    # the studio lights, never painted into the color image.
    rx, ry = (x-.5)*2, (y-.5)*2
    radius = np.sqrt(rx*rx+ry*ry)
    theta = np.arctan2(ry, rx)
    fiber = (np.sin(theta*61+radius*8)*.50+np.sin(theta*113-radius*11)*.22+np.sin(theta*157+radius*24)*.16)
    iris_base = np.array({'A': (117, 70, 28), 'B': (108, 59, 25), 'C': (99, 63, 29), 'D': (94, 56, 26)}[style])/255.
    iris = np.broadcast_to(iris_base, (size, size, 3)).copy()
    iris *= 1+fiber[..., None]*(.18 if style != 'B' else .045)
    amber = np.exp(-((radius-.48)/.18)**2)*.22
    iris += amber[..., None]*np.array((.11, .045, .005))
    limbus = np.clip((radius-.81)/.17, 0, 1)*.66
    iris *= 1-limbus[..., None]
    save_image(root/'iris_basecolor.png', iris)
    texture_metadata['iris'] = {'basecolor': str((root/'iris_basecolor.png').relative_to(out))}
    return dict(label=p['label'], maps=texture_metadata, authored_palette_srgb=p,
                size=size, note='Original seamless textile designs. No baked lights/shadows/AO; ORM.R=1, ORM.B=0.')


def generate(out=DEFAULT_OUT, size=1024, styles=('A', 'B', 'C', 'D')):
    out = Path(out).resolve()
    manifest = dict(generator_version=VERSION, color_space='basecolor: sRGB; ORM/normal: linear',
                    normal_convention='OpenGL +Y tangent space', variants={})
    for style in styles:
        manifest['variants'][style] = make_style(style, out, size)
        print(f'AUTHORED_TEXTURES {style} {size}px', flush=True)
    (out/'texture-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=DEFAULT_OUT)
    parser.add_argument('--size', type=int, default=1024)
    args = parser.parse_args()
    if args.size < 256 or args.size > 4096:
        parser.error('--size must be between 256 and 4096')
    generate(args.out, args.size)
