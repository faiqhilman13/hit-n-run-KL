"""Extra OpenStreetMap areas for the real-KL patches: car parks and building sites (what fills the gaps between
downtown buildings seen from above).
   uv run --no-project --with requests python fetch_extra.py"""
import json, requests, sys
from collections import Counter
S, N, W, E = 3.117, 3.176, 101.678, 101.724
bb = f'{S},{W},{N},{E}'
q = f'''
[out:json][timeout:180];
(
  way["amenity"="parking"]({bb});
  relation["amenity"="parking"]({bb});
  way["landuse"~"^(construction|brownfield|railway)$"]({bb});
  way["amenity"~"^(fuel|marketplace)$"]({bb});
);
out body geom;
'''
for url in ['https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter']:
    try:
        r = requests.post(url, data={'data': q}, timeout=240, headers={'User-Agent': 'KampungRun-map/1.0'})
        r.raise_for_status()
        d = r.json()
        break
    except Exception as e:
        print('failed', url, e, file=sys.stderr)
else:
    sys.exit(1)
json.dump(d, open('kl_osm_extra.json', 'w'))
c = Counter()
for el in d['elements']:
    t = el.get('tags', {})
    c[t.get('amenity') or t.get('landuse') or el['type']] += 1
for k, v in c.most_common(): print(v, k)
