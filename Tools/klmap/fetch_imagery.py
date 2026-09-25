"""Satellite reference for downtown KL: Esri World Imagery tiles stitched into one image.
   uv run --no-project --with pillow --with requests python fetch_imagery.py"""
import math, os, io, time
import requests
from PIL import Image

S, N, W, E = 3.117, 3.176, 101.678, 101.724     # KL Sentral/Thean Hou .. Chow Kit/Titiwangsa, Lake Gardens .. KLCC/TRX
Z = 16

def tile(lat, lon, z):
    n = 2 ** z
    x = (lon + 180) / 360 * n
    y = (1 - math.asinh(math.tan(math.radians(lat))) / math.pi) / 2 * n
    return x, y

x0, y0 = tile(N, W, Z)
x1, y1 = tile(S, E, Z)
tx0, ty0, tx1, ty1 = int(x0), int(y0), int(x1), int(y1)
os.makedirs('tiles', exist_ok=True)
img = Image.new('RGB', ((tx1 - tx0 + 1) * 256, (ty1 - ty0 + 1) * 256))
sess = requests.Session()
sess.headers['User-Agent'] = 'KampungRun-reference/1.0'
for ty in range(ty0, ty1 + 1):
    for tx in range(tx0, tx1 + 1):
        fn = f'tiles/{Z}_{tx}_{ty}.jpg'
        if not os.path.exists(fn):
            url = f'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{Z}/{ty}/{tx}'
            r = sess.get(url, timeout=30)
            r.raise_for_status()
            open(fn, 'wb').write(r.content)
            time.sleep(0.05)
        img.paste(Image.open(fn), ((tx - tx0) * 256, (ty - ty0) * 256))
# crop to the exact bbox
cx0, cy0 = int((x0 - tx0) * 256), int((y0 - ty0) * 256)
cx1, cy1 = int((x1 - tx0) * 256), int((y1 - ty0) * 256)
img = img.crop((cx0, cy0, cx1, cy1))
img.save('kl_satellite_z16.jpg', quality=90)
print(img.size, 'tiles', (tx1 - tx0 + 1) * (ty1 - ty0 + 1))
