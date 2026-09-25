"""Downtown KL from OpenStreetMap (Overpass): roads, rail, rivers, parks, buildings, landmarks.
   uv run --no-project --with requests python fetch_osm.py"""
import json, requests, sys
S, N, W, E = 3.117, 3.176, 101.678, 101.724
bb = f'{S},{W},{N},{E}'
q = f'''
[out:json][timeout:180];
(
  way["highway"~"^(motorway|trunk|primary|secondary|tertiary|unclassified|residential|living_street|motorway_link|trunk_link|primary_link|secondary_link|tertiary_link|service|pedestrian)$"]({bb});
  way["railway"~"^(rail|light_rail|monorail|subway)$"]({bb});
  way["waterway"~"^(river|canal|stream)$"]({bb});
  way["natural"="water"]({bb});
  relation["natural"="water"]({bb});
  way["leisure"~"^(park|garden|pitch|stadium|golf_course)$"]({bb});
  relation["leisure"~"^(park|garden)$"]({bb});
  way["landuse"~"^(grass|forest|cemetery|recreation_ground|residential|commercial|retail)$"]({bb});
  relation["landuse"~"^(forest|cemetery)$"]({bb});
  way["natural"~"^(wood|scrub)$"]({bb});
  way["building"]({bb});
  relation["building"]({bb});
  node["place"~"^(suburb|neighbourhood|quarter)$"]({bb});
  node["tourism"~"^(attraction|museum)$"]({bb});
  way["tourism"~"^(attraction|museum)$"]({bb});
  way["amenity"="place_of_worship"]({bb});
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
json.dump(d, open('kl_osm.json', 'w'))
from collections import Counter
c = Counter()
for el in d['elements']:
    t = el.get('tags', {})
    k = 'building' if 'building' in t else t.get('highway') and 'hw:' + t['highway'] or t.get('railway') and 'rail:' + t['railway'] \
        or t.get('waterway') and 'water:' + t['waterway'] or t.get('natural') or t.get('leisure') or t.get('landuse') or t.get('place') or t.get('tourism') or el['type']
    c[k] += 1
for k, v in c.most_common(): print(v, k)
