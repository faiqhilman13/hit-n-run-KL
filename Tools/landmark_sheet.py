"""Reference-vs-game sheets for the landmarks: for each landmark, a row of real photos
(Tools/references/landmarks/<name>/) above rows of the game captures (Tools/promo_frames/landmarks/<set>/,
from Tests/LandmarkCapture), one row per capture set.
    uv run --no-project --with pillow python Tools/landmark_sheet.py <out_dir> <set> [<set> ...] [-- name ...]
"""
import os
import sys
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REFS = os.path.join(HERE, 'references', 'landmarks')
CAPS = os.path.join(HERE, 'promo_frames', 'landmarks')
VIEWS = ['street', 'near', 'air', 'air2']
TW, TH = 400, 225


def fit(im, w, h):
    im = im.convert('RGB')
    k = min(w / im.width, h / im.height)
    im = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))))
    out = Image.new('RGB', (w, h), (24, 24, 28))
    out.paste(im, ((w - im.width) // 2, (h - im.height) // 2))
    return out


def sheet(name, sets, out):
    refdir = os.path.join(REFS, name)
    refs = sorted(f for f in os.listdir(refdir) if f.lower().endswith(('.jpg', '.jpeg', '.png'))) if os.path.isdir(refdir) else []
    rows = 1 + len(sets)
    im = Image.new('RGB', (TW * 4, TH * rows + 24 * rows), (255, 255, 255))
    dr = ImageDraw.Draw(im)
    dr.text((6, 6), f'{name}: reference', fill=(0, 0, 0))
    for i, f in enumerate(refs[:4]):
        im.paste(fit(Image.open(os.path.join(refdir, f)), TW, TH), (i * TW, 24))
    for r, s in enumerate(sets, 1):
        y = r * (TH + 24)
        dr.text((6, y + 6), f'{name}: game ({s})', fill=(0, 0, 0))
        for c, v in enumerate(VIEWS):
            p = os.path.join(CAPS, s, f'{name}_{v}.jpg')
            if os.path.exists(p):
                im.paste(fit(Image.open(p), TW, TH), (c * TW, y + 24))
    os.makedirs(out, exist_ok=True)
    path = os.path.join(out, f'{name}.jpg')
    im.save(path, quality=85)
    return path


if __name__ == '__main__':
    args = sys.argv[1:]
    names = []
    if '--' in args:
        i = args.index('--')
        args, names = args[:i], args[i + 1:]
    out, sets = args[0], args[1:]
    if not names:
        names = sorted({f.rsplit('_', 1)[0] for f in os.listdir(os.path.join(CAPS, sets[-1])) if f.endswith('_street.jpg')})
    for n in names:
        print(sheet(n, sets, out))
