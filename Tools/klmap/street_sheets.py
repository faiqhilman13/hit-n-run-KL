"""Contact sheets and reference-vs-game comparison sheets for the streets pass.

  python Tools/klmap/street_sheets.py contact <tag>            -> Tools/promo_frames/streets/<tag>_sheet_NN.jpg
  python Tools/klmap/street_sheets.py compare <tagA> <tagB>    -> before/after per spot
  python Tools/klmap/street_sheets.py refs <tag> spec.json     -> reference photo(s) beside game shots
"""
import json, os, sys, glob
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.join(os.path.dirname(__file__), '..', 'promo_frames', 'streets')
REFS = os.path.join(os.path.dirname(__file__), '..', 'references', 'streets')

def font(sz=22):
    for f in ('arialbd.ttf', 'arial.ttf', 'DejaVuSans-Bold.ttf'):
        try: return ImageFont.truetype(f, sz)
        except Exception: pass
    return ImageFont.load_default()

def fit(im, w, h):
    im = im.convert('RGB')
    r = min(w / im.width, h / im.height)
    im = im.resize((max(1, int(im.width * r)), max(1, int(im.height * r))), Image.LANCZOS)
    out = Image.new('RGB', (w, h), (24, 24, 28))
    out.paste(im, ((w - im.width) // 2, (h - im.height) // 2))
    return out

def label(im, text):
    d = ImageDraw.Draw(im)
    f = font(20)
    d.rectangle([0, 0, d.textlength(text, font=f) + 12, 28], fill=(0, 0, 0))
    d.text((6, 3), text, fill=(255, 255, 0), font=f)
    return im

def grid(paths, cols, w, h, out, labels=None):
    rows = (len(paths) + cols - 1) // cols
    sheet = Image.new('RGB', (cols * w, rows * h), (24, 24, 28))
    for i, p in enumerate(paths):
        if p is None: continue
        im = label(fit(Image.open(p), w, h), labels[i] if labels else os.path.basename(p))
        sheet.paste(im, ((i % cols) * w, (i // cols) * h))
    sheet.save(out, quality=88)
    print(out)

def contact(tag, per=12):
    files = sorted(glob.glob(os.path.join(ROOT, tag, '*.jpg')))
    for k in range(0, len(files), per):
        grid(files[k:k + per], 3, 640, 360, os.path.join(ROOT, f'{tag}_sheet_{k // per:02d}.jpg'))

def compare(a, b):
    out = os.path.join(ROOT, f'cmp_{a}_{b}')
    os.makedirs(out, exist_ok=True)
    names = sorted(os.path.basename(p) for p in glob.glob(os.path.join(ROOT, b, '*.jpg')))
    spots = sorted(set(n.rsplit('_', 1)[0] if n[:2].isdigit() and n.count('_') > 1 else n[:-4] for n in names))
    for s in spots:
        shots = [n for n in names if n.startswith(s)]
        paths, labels = [], []
        for n in shots:
            pa = os.path.join(ROOT, a, n)
            paths += [pa if os.path.exists(pa) else None, os.path.join(ROOT, b, n)]
            labels += [f'{a}: {n}', f'{b}: {n}']
        grid(paths, 2, 800, 450, os.path.join(out, s + '.jpg'), labels)

def refs(tag, spec):
    """spec: [{"name": "petaling", "refs": ["a.jpg", ...], "game": ["01_petaling_eye.jpg", ...]}, ...]"""
    out = os.path.join(ROOT, f'refs_{tag}')
    os.makedirs(out, exist_ok=True)
    for s in json.load(open(spec)):
        paths = [os.path.join(REFS, r) for r in s['refs']] + [os.path.join(ROOT, tag, g) for g in s['game']]
        labels = ['REF ' + r for r in s['refs']] + ['GAME ' + g for g in s['game']]
        cols = max(len(s['refs']), len(s['game']))
        rp = [os.path.join(REFS, r) for r in s['refs']] + [None] * (cols - len(s['refs']))
        gp = [os.path.join(ROOT, tag, g) for g in s['game']] + [None] * (cols - len(s['game']))
        rl = ['REF ' + r for r in s['refs']] + [''] * (cols - len(s['refs']))
        gl = ['GAME ' + g for g in s['game']] + [''] * (cols - len(s['game']))
        grid(rp + gp, cols, 720, 405, os.path.join(out, s['name'] + '.jpg'), rl + gl)

if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'contact': contact(sys.argv[2])
    elif cmd == 'compare': compare(sys.argv[2], sys.argv[3])
    elif cmd == 'refs': refs(sys.argv[2], sys.argv[3])
