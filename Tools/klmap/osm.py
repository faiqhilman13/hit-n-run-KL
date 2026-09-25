"""OpenStreetMap features of downtown KL as shapely geometry in real metres (x east, y north of Masjid Jamek)."""
import json, math, os
from shapely.geometry import LineString, Polygon, Point
from shapely.validation import make_valid

HERE = os.path.dirname(__file__)
LAT0, LON0 = 3.1489, 101.6955            # Masjid Jamek
MX = 111320 * math.cos(math.radians(3.1465))
MY = 110574

ROAD_CLASSES = {
    # class: (width in game metres both ways, lanes each way) - roads keep life-size widths
    'motorway': (17.0, 3), 'trunk': (16.0, 3), 'primary': (14.0, 2), 'secondary': (12.0, 2), 'tertiary': (10.0, 1),
    'unclassified': (8.0, 1), 'residential': (7.5, 1), 'living_street': (6.5, 1),
    'motorway_link': (8.0, 1), 'trunk_link': (8.0, 1), 'primary_link': (8.0, 1), 'secondary_link': (7.5, 1), 'tertiary_link': (7.5, 1),
}


def xy(p): return ((p['lon'] - LON0) * MX, (p['lat'] - LAT0) * MY)


def _poly(pts):
    if len(pts) < 3: return None
    g = Polygon(pts)
    if not g.is_valid: g = make_valid(g)
    return g if not g.is_empty else None


class OSM:
    def __init__(self, path=os.path.join(HERE, 'kl_osm.json')):
        d = json.load(open(path))
        self.roads, self.rails, self.rivers, self.water, self.parks, self.forest, self.pitches = [], [], [], [], [], [], []
        self.buildings, self.cemetery = [], []
        self.lawns = []              # open grass: padangs, recreation grounds (no trees planted on them)
        self.landuse = []            # (kind, poly): residential / commercial / retail zones, for infill heights
        self.pedestrian = []         # pedestrian streets and squares (Petaling Street's market is one)
        self.parking, self.sites, self.fuel, self.markets = [], [], [], []
        for el in d['elements']:
            t = el.get('tags', {})
            g = el.get('geometry')
            if not g:
                if el['type'] == 'relation' and el.get('members'):
                    # multipolygon relations: take the outer rings
                    for m in el['members']:
                        if m.get('role') == 'outer' and m.get('geometry'):
                            self._area(t, [xy(p) for p in m['geometry']], el['id'])
                continue
            pts = [xy(p) for p in g]
            hw = t.get('highway')
            if hw == 'pedestrian' and len(pts) > 1:
                if t.get('area') == 'yes':
                    p = _poly(pts)
                    if p is not None: self.pedestrian.append(dict(poly=p, tags=t))
                else:
                    self.pedestrian.append(dict(line=LineString(pts), tags=t))
                continue
            if hw in ROAD_CLASSES and t.get('area') != 'yes':
                self.roads.append(dict(id=el['id'], cls=hw, line=LineString(pts), nodes=el.get('nodes', []), tags=t))
                continue
            if t.get('railway') in ('light_rail', 'monorail', 'rail') and len(pts) > 1:
                self.rails.append(dict(kind=t['railway'], line=LineString(pts), tags=t))
                continue
            if t.get('waterway') in ('river', 'canal') and len(pts) > 1:
                self.rivers.append(dict(name=t.get('name', ''), line=LineString(pts)))
                continue
            self._area(t, pts, el['id'])
        # car parks, building sites, petrol stations, markets (fetch_extra.py)
        extra = os.path.join(HERE, 'kl_osm_extra.json')
        if os.path.exists(extra):
            for el in json.load(open(extra))['elements']:
                t = el.get('tags', {})
                rings = [el['geometry']] if el.get('geometry') else                         [m['geometry'] for m in el.get('members', []) if m.get('role') == 'outer' and m.get('geometry')]
                for g in rings:
                    p = _poly([xy(q) for q in g])
                    if p is None: continue
                    a, lu = t.get('amenity'), t.get('landuse')
                    if a == 'parking' and t.get('parking', 'surface') in ('surface', 'lane', 'street_side'): self.parking.append(dict(poly=p, tags=t))
                    elif lu in ('construction', 'brownfield'): self.sites.append(dict(poly=p, tags=t))
                    elif a == 'fuel': self.fuel.append(dict(poly=p, tags=t))
                    elif a == 'marketplace': self.markets.append(dict(poly=p, tags=t))

    def _area(self, t, pts, oid):
        if 'building' in t:
            p = _poly(pts)
            if p is not None: self.buildings.append(dict(id=oid, poly=p, tags=t))
            return
        if t.get('natural') == 'water' and t.get('water') not in ('river',):
            p = _poly(pts)
            if p is not None and p.area > 60: self.water.append(dict(poly=p, tags=t))
            return
        lei, lu, nat = t.get('leisure'), t.get('landuse'), t.get('natural')
        if lu in ('grass', 'recreation_ground') and lei not in ('park', 'garden'):
            p = _poly(pts)
            if p is not None: self.lawns.append(dict(poly=p, tags=t))
        elif lei in ('park', 'garden'):
            p = _poly(pts)
            if p is not None: self.parks.append(dict(poly=p, tags=t))
        elif lu == 'forest' or nat in ('wood', 'scrub'):
            p = _poly(pts)
            if p is not None: self.forest.append(dict(poly=p, tags=t))
        elif lei in ('pitch', 'stadium'):
            p = _poly(pts)
            if p is not None: self.pitches.append(dict(poly=p, tags=t))
        elif lu == 'cemetery':
            p = _poly(pts)
            if p is not None: self.cemetery.append(dict(poly=p, tags=t))
        elif lu in ('residential', 'commercial', 'retail'):
            p = _poly(pts)
            if p is not None: self.landuse.append((lu, p))


def levels(tags):
    for k in ('building:levels', 'levels'):
        v = tags.get(k)
        if v:
            try: return float(str(v).split(';')[0])
            except ValueError: pass
    h = tags.get('height')
    if h:
        try: return float(str(h).replace('m', '').strip()) / 3.5
        except ValueError: pass
    return None
