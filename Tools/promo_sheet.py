"""Contact sheet of recorded promo shots (every Nth frame), to check a shot before it goes in the cut.
    uv run --no-project --with pillow python Tools/promo_sheet.py 01_cruise 02_drift ... """
import os, sys
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
FRAMES = os.path.join(HERE, 'promo_frames')
for shot in sys.argv[1:]:
    d = os.path.join(FRAMES, shot)
    fs = sorted(f for f in os.listdir(d) if f.endswith('.jpg'))
    picks = [fs[int(i * (len(fs) - 1) / 7)] for i in range(8)]
    tw, th = 480, 270
    sheet = Image.new('RGB', (tw * 4, th * 2), (0, 0, 0))
    for k, f in enumerate(picks):
        im = Image.open(os.path.join(d, f)).resize((tw, th))
        ImageDraw.Draw(im).text((8, 6), f'{shot} {f}', fill=(255, 255, 0))
        sheet.paste(im, ((k % 4) * tw, (k // 4) * th))
    out = os.path.join(HERE, 'promo_out', f'sheet_{shot}.jpg')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    sheet.save(out, quality=85)
    print(out)
