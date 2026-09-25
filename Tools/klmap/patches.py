"""
Build the real-KL patches of the game map from OpenStreetMap.

For every patch in layout.PATCHES this makes, in game coordinates:
  meshes     road surface, lane dashes, pavements, land (paving / grass / park / forest / pitch), kerb walls,
             river water + embankments + bed, flyover decks / barriers / pillars, buildings (walls + roofs)
  graph      the drivable road network (one-way streets and roundabouts directed), plus the points where
             it meets the surrounding grid roads
  rings      pavement loops for pedestrians
  trees      park and forest trees
  anchors    where our landmark models stand, and road-side places for missions
and writes Assets/KampungRun/Resources/KLMap/<patch>.bytes (see write_patch for the format), plus a
top-down preview PNG per patch in Tools/klmap/out/.

    uv run --no-project --with shapely --with mapbox-earcut --with numpy --with pillow python patches.py [patch ...]
"""
import math, os, random, struct, sys
from collections import defaultdict
import numpy as np
import mapbox_earcut as earcut
from shapely import affinity
from shapely.geometry import box, LineString, Point, Polygon, MultiPolygon, GeometryCollection, MultiLineString
from shapely.geometry.polygon import orient
from shapely.ops import unary_union
from shapely.strtree import STRtree

import layout as L
from osm import OSM, ROAD_CLASSES, levels

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
RES = os.path.normpath(os.path.join(HERE, '..', '..', 'Assets', 'KampungRun', 'Resources', 'KLMap'))

TOP = 0.18            # pavement / land top = CityBuilder.G (road surface is y = 0)
BED = -3.4            # river bed
WATER_Y = -2.4
SLOPE = 0.075         # flyover ramp gradient
LAYER_H = 7.0         # deck height per bridge layer
WALK_W = 3.2          # pavement width along roads
RIVER_W = {'Gombak': 18.0, 'Klang': 22.0}

# facade textures (CityBuilder.Facades): one texture tile is one window bay x one storey, so
# wall UVs are in bays (u) and storeys (v)
BAY, FLOOR = 3.6, 3.4         # punched windows (fac_win)
GLASS_BAY = 3.0               # curtain wall (fac_glass)
SHOP_H, SHOPS = 4.2, 4        # ground-floor shopfront band; fac_shop is four 3.6 m shops wide
LOBBY_H = 5.0                 # tower lobby band (fac_lobby)

# landmark models that replace the real buildings around their anchor: real name -> (model, clear radius m game)
LANDMARKS = {
    'MasjidJamek': ('env_lm2_masjid_jamek', 26), 'SultanAbdulSamad': ('Bld_SultanAbdulSamad', 58), 'PasarSeni': ('Bld_PasarSeni', 36),
    'Merdeka118': ('env_lm2_merdeka118', 42), 'StadiumMerdeka': ('env_lm2_stadium_merdeka', 72), 'MasjidNegara': ('env_lm_masjid_negara', 62),
    'KLTower': ('Bld_MenaraKL', 22), 'Petronas': ('Bld_MenaraKembar', 56), 'KLSentral': ('env_lm_kl_sentral', 44),
    'MuziumNegara': ('env_lm_muzium_negara', 30), 'TuguNegara': ('env_lm2_tugu_negara', 26),
}
# named places with no model of their own (the real buildings / open ground stay)
PLACES = ['Dataran', 'Petaling', 'KLRailway', 'Perdana', 'KLCCPark', 'Pavilion', 'Brickfields', 'StadiumNegara', 'Maybank', 'Dayabumi']
RAIL_H = {'monorail': 8.0, 'light_rail': 10.0}


# ------------------------------------------------------------------ mesh accumulation
class Mesh:
    def __init__(self):
        self.v, self.n, self.t, self.uv = [], [], [], []
        self.has_uv = False

    def tri(self, a, b, c, normal=None, uv=None):
        """Add a triangle; wound so Unity sees its front on the side of `normal` (or of its own cross product)."""
        a, b, c = np.array(a, float), np.array(b, float), np.array(c, float)
        ua, ub, uc = uv if uv is not None else ((0.0, 0.0),) * 3
        cr = np.cross(b - a, c - a)          # Unity's face normal for (a, b, c)
        if normal is not None and np.dot(cr, normal) < 0:
            b, c = c, b
            ub, uc = uc, ub
            cr = -cr
        ln = np.linalg.norm(cr)
        if ln < 1e-9: return
        nn = cr / ln if normal is None else np.array(normal, float) / np.linalg.norm(normal)
        i = len(self.v)
        self.v += [a, b, c]
        self.n += [nn, nn, nn]
        self.t += [i, i + 1, i + 2]
        self.uv += [ua, ub, uc]
        if uv is not None: self.has_uv = True

    def quad(self, a, b, c, d, normal, uv=None):
        if uv is None:
            self.tri(a, b, c, normal)
            self.tri(a, c, d, normal)
        else:
            self.tri(a, b, c, normal, (uv[0], uv[1], uv[2]))
            self.tri(a, c, d, normal, (uv[0], uv[2], uv[3]))

    def flat(self, geom, y, up=True):
        """Triangulated horizontal polygon(s) at height y."""
        for poly in polys(geom):
            poly = orient(poly, 1.0)
            rings = [np.array(poly.exterior.coords[:-1])] + [np.array(r.coords[:-1]) for r in poly.interiors]
            rings = [r for r in rings if len(r) >= 3]
            if not rings: continue
            verts = np.concatenate(rings)
            ends = np.cumsum([len(r) for r in rings]).astype(np.uint32)
            idx = earcut.triangulate_float64(verts, ends)
            nrm = (0, 1, 0) if up else (0, -1, 0)
            for k in range(0, len(idx), 3):
                a, b, c = verts[idx[k]], verts[idx[k + 1]], verts[idx[k + 2]]
                self.tri((a[0], y, a[1]), (b[0], y, b[1]), (c[0], y, c[1]), nrm)

    def walls(self, geom, y0, y1, outward=True, test=None):
        """Vertical walls round polygon rings. outward: facing out of the polygon. test(midpoint, out_normal) -> bool keeps a wall."""
        for poly in polys(geom):
            poly = orient(poly, 1.0)
            for ring in [poly.exterior] + list(poly.interiors):
                c = list(ring.coords)
                for (x0, z0), (x1, z1) in zip(c, c[1:]):
                    dx, dz = x1 - x0, z1 - z0
                    ln = math.hypot(dx, dz)
                    if ln < 0.05: continue
                    out = np.array((dz / ln, 0, -dx / ln))          # right of travel = out of the polygon
                    if not outward: out = -out
                    if test is not None and not test(((x0 + x1) / 2, (z0 + z1) / 2), out): continue
                    self.quad((x0, y0, z0), (x1, y0, z1), (x1, y1, z1), (x0, y1, z0), out)

    def facade(self, geom, y0, y1, bay, storey, per_tile=1, u_off=0.0):
        """Outward walls round the polygon(s) with facade UVs: u counts window bays along each wall (a whole
        number of them, so no window is cut at a corner), v counts storeys up from y0."""
        rows = max(1, round((y1 - y0) / storey))
        for poly in polys(geom):
            poly = orient(poly, 1.0)
            for ring in [poly.exterior] + list(poly.interiors):
                c = list(ring.coords)
                for (x0, z0), (x1, z1) in zip(c, c[1:]):
                    dx, dz = x1 - x0, z1 - z0
                    ln = math.hypot(dx, dz)
                    if ln < 0.05: continue
                    out = np.array((dz / ln, 0, -dx / ln))
                    nb = max(1, round(ln / bay)) if ln > bay * 0.6 else ln / bay
                    ua, ub = u_off, u_off + nb / per_tile
                    self.quad((x0, y0, z0), (x1, y0, z1), (x1, y1, z1), (x0, y1, z0), out,
                              uv=((ua, 0.0), (ub, 0.0), (ub, rows), (ua, rows)))

    def extend(self, other):
        base = len(self.v)
        self.v += other.v
        self.n += other.n
        self.t += [base + i for i in other.t]
        self.uv += other.uv
        self.has_uv = self.has_uv or other.has_uv


def icosphere():
    """A once-subdivided icosahedron (42 verts, 80 faces), unit radius."""
    t = (1 + 5 ** 0.5) / 2
    v = [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]
    v = [tuple(np.array(p) / np.linalg.norm(p)) for p in v]
    f = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
         (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]
    cache = {}

    def mid(a, b):
        k = (min(a, b), max(a, b))
        if k not in cache:
            p = (np.array(v[a]) + np.array(v[b])) / 2
            v.append(tuple(p / np.linalg.norm(p)))
            cache[k] = len(v) - 1
        return cache[k]
    return v, f          # 20 faces: a chunky cartoon canopy


def polys(g):
    if g is None or g.is_empty: return []
    if isinstance(g, Polygon): return [g]
    if isinstance(g, (MultiPolygon, GeometryCollection)):
        out = []
        for s in g.geoms: out += polys(s)
        return out
    return []


def lines(g):
    if g is None or g.is_empty: return []
    if isinstance(g, LineString): return [g]
    if isinstance(g, (MultiLineString, GeometryCollection)):
        out = []
        for s in g.geoms: out += lines(s)
        return out
    return []


# ------------------------------------------------------------------ one patch
class PatchBuild:
    def __init__(self, osm, patch):
        self.osm, self.p = osm, patch
        x0, z0, x1, z1 = patch.rect
        self.rect = box(x0, z0, x1, z1)
        self.m = [L.K, 0, 0, L.K, patch.frame.fx, patch.frame.fz]
        wx0, wz0, wx1, wz1 = patch.real_window
        self.win = box(wx0 - 80, wz0 - 80, wx1 + 80, wz1 + 80)
        self.meshes = defaultdict(Mesh)
        self.rng = random.Random(hash(patch.name) & 0xffff)

    def T(self, g): return affinity.affine_transform(g, self.m)

    # -------------------------------------------------------------- roads
    def classify_roads(self):
        osm = self.osm
        cand = [r for r in osm.roads if r['line'].intersects(self.win) and r['tags'].get('tunnel') not in ('yes', 'building_passage')
                and r['tags'].get('covered') != 'yes']
        for r in cand:
            r['g'] = self.T(r['line'])
            w = ROAD_CLASSES[r['cls']][0]
            if r['tags'].get('oneway') in ('yes', '1', '-1') and r['cls'] in ('trunk', 'primary', 'secondary', 'motorway'): w *= 0.72
            if r['tags'].get('junction') == 'roundabout': w = 9.0
            r['w'] = w
        ground_nodes = set()
        bridges = []
        for r in cand:
            if r['tags'].get('bridge') in ('yes', 'viaduct') and r['tags'].get('layer', '1') not in ('0', '-1'):
                bridges.append(r)
            else:
                ground_nodes.update(r['nodes'])
        # a bridge is a flyover if it passes over another road (not just over the river)
        ground_lines = [r['g'] for r in cand if r not in bridges]
        tree = STRtree(ground_lines) if ground_lines else None
        fly = []
        for r in bridges:
            over_road = False
            if tree is not None:
                for i in tree.query(r['g']):
                    gl = ground_lines[i]
                    inter = r['g'].intersection(gl)
                    if inter.is_empty: continue
                    # crossing somewhere other than its own ends
                    ends = [Point(r['g'].coords[0]), Point(r['g'].coords[-1])]
                    pts = [inter] if inter.geom_type == 'Point' else list(getattr(inter, 'geoms', [inter]))
                    for pt in pts:
                        if pt.geom_type == 'Point' and min(pt.distance(e) for e in ends) > 6: over_road = True
            big = r['cls'] in ('motorway', 'motorway_link', 'trunk', 'trunk_link') and r['g'].length > 45
            if over_road or big: fly.append(r)
            else: ground_nodes.update(r['nodes'])          # a river bridge: stays at road level
        # flyover centre lines get a vertex every 8 m (new ids), so their height can follow the ground
        for r in fly:
            cs, ns = list(r['g'].coords), list(r['nodes'])
            dc, dn = [cs[0]], [ns[0]]
            for i in range(len(cs) - 1):
                k = int(math.dist(cs[i], cs[i + 1]) // 8)
                for j in range(1, k + 1):
                    t = j / (k + 1)
                    dc.append((cs[i][0] + (cs[i + 1][0] - cs[i][0]) * t, cs[i][1] + (cs[i + 1][1] - cs[i][1]) * t))
                    dn.append(('d', r['id'], i, j))
                dc.append(cs[i + 1]); dn.append(ns[i + 1])
            r['g'], r['nodes'] = LineString(dc), dn
        self.roads = cand
        self.fly = fly
        self.ground = [r for r in cand if r not in fly]
        self.ground_nodes = ground_nodes

    def solve_elevations(self):
        """Node heights for flyovers: 0 where they touch the ground network, ramping up at SLOPE to LAYER_H*layer."""
        adj = defaultdict(list)
        cap = {}
        for r in self.fly:
            layer = max(1, int(r['tags'].get('layer', '1') or 1)) if str(r['tags'].get('layer', '1')).lstrip('-').isdigit() else 1
            h = LAYER_H * min(layer, 2)
            coords = list(r['g'].coords)
            for i, (a, b) in enumerate(zip(r['nodes'], r['nodes'][1:])):
                d = math.dist(coords[i], coords[i + 1])
                adj[a].append((b, d)); adj[b].append((a, d))
            for n in r['nodes']: cap[n] = max(cap.get(n, 0), h)
        # distance from the ground network
        import heapq
        dist = {}
        pq = []
        for n in adj:
            if n in self.ground_nodes:
                dist[n] = 0.0
                heapq.heappush(pq, (0.0, n))
        while pq:
            d, n = heapq.heappop(pq)
            if d > dist.get(n, 1e18): continue
            for m, w in adj[n]:
                nd = d + w
                if nd < dist.get(m, 1e18):
                    dist[m] = nd
                    heapq.heappush(pq, (nd, m))
        self.elev = {n: min(cap.get(n, LAYER_H), dist.get(n, 1e9) * SLOPE) for n in adj}

    def vertex_heights(self, r):
        """Per-vertex deck height along a flyover (ramps from its end nodes, capped), ramping down to the patch edge."""
        coords = list(r['g'].coords)
        s = [0.0]
        for a, b in zip(coords, coords[1:]): s.append(s[-1] + math.dist(a, b))
        total = s[-1]
        e0 = self.elev.get(r['nodes'][0], 0.0)
        e1 = self.elev.get(r['nodes'][-1], 0.0)
        layer = r['tags'].get('layer', '1')
        h = LAYER_H * (min(int(layer), 2) if str(layer).isdigit() and int(layer) > 0 else 1)
        edge = self.rect.exterior
        out = []
        for (x, z), si in zip(coords, s):
            y = min(h, e0 + si * SLOPE, e1 + (total - si) * SLOPE)
            # come down to road level at the patch edge (it meets a grid road there)
            y = min(y, max(0.0, edge.distance(Point(x, z)) - 4.0) * SLOPE * 1.6) if self.rect.contains(Point(x, z)) else 0.0
            out.append(max(0.0, y))
        # a street it crosses too low to pass under (the real ramp runs on beyond the bridge's end):
        # come down and meet it at grade instead
        if not hasattr(self, '_gtree'):
            self._glines = [g['g'] for g in self.roads if g not in self.fly]
            self._gtree = STRtree(self._glines) if self._glines else None
        line = r['g']
        if self._gtree is not None:
            for i in self._gtree.query(line):
                inter = line.intersection(self._glines[i])
                for pt in ([inter] if inter.geom_type == 'Point' else [g for g in getattr(inter, 'geoms', []) if g.geom_type == 'Point']):
                    sc = line.project(pt)
                    if min(sc, total - sc) < 6: continue
                    hc = np.interp(sc, s, out)
                    if hc < 4.5:
                        out = [min(y, max(0.0, abs(si - sc) - 8.0) * SLOPE) for y, si in zip(out, s)]
        return out

    def build_roads(self):
        # flyovers that never get properly off the ground (short, or brought down by the patch edge) are plain roads
        for r in list(self.fly):
            if max(self.vertex_heights(r)) < 2.5:
                self.fly.remove(r)
                self.ground.append(r)
        shapes, crossing = [], []
        river = self.river_band
        for r in self.ground:
            line = r['g']
            if not river.is_empty and line.intersects(river):
                inside = line.intersection(river).length
                if inside > max(RIVER_W.values()) * 1.8:
                    # runs along the river: keep it on the bank
                    line = line.difference(river.buffer(r['w'] / 2 + 0.6))
                    r['bank'] = True
                else:
                    crossing.append(line.buffer(r['w'] / 2, cap_style=2, join_style=1, quad_segs=4))
            if not line.is_empty: shapes.append(line.buffer(r['w'] / 2, cap_style=1, join_style=1, quad_segs=4))
        # the low ends of flyover ramps are road-level too
        self.fly_parts = []
        for r in self.fly:
            hs = self.vertex_heights(r)
            coords = list(r['g'].coords)
            r['h'] = hs
            low = [i for i, h in enumerate(hs) if h < 0.35]
            for i in low:
                seg_pts = [coords[j] for j in (i - 1, i, i + 1) if 0 <= j < len(coords) and hs[j] < 0.9]
                if len(seg_pts) >= 2: shapes.append(LineString(seg_pts).buffer(r['w'] / 2, cap_style=1, join_style=1, quad_segs=4))
        road = unary_union(shapes).intersection(self.rect) if shapes else Polygon()
        if not river.is_empty:
            decks = unary_union(crossing).intersection(river) if crossing else Polygon()
            road = road.difference(river).union(decks.intersection(self.rect))
            self.decks = decks
        else:
            self.decks = Polygon()
        self.road = road.buffer(0)

    # -------------------------------------------------------------- rivers / water
    def build_water(self):
        parts = []
        if self.p.rivers:
            for rv in self.osm.rivers:
                name = 'Gombak' if 'Gombak' in rv['name'] else 'Klang' if ('Kelang' in rv['name'] or 'Klang' in rv['name']) else None
                if name is None or not rv['line'].intersects(self.win): continue
                g = self.T(rv['line'])
                parts.append(g.buffer(RIVER_W[name] / 2, cap_style=2, join_style=1, quad_segs=6))
        river = unary_union(parts) if parts else Polygon()
        # extend the channels under the grid roads around the patch (they cross on bridges there)
        grown = self.rect.buffer(L.ROAD / 2 + 0.5, join_style=2)
        self.river_band = river.intersection(grown) if not river.is_empty else Polygon()
        self.river = river.intersection(self.rect) if not river.is_empty else Polygon()
        ponds = [self.T(w['poly']) for w in self.osm.water if w['poly'].intersects(self.win)]
        self.ponds = unary_union([p.buffer(0) for p in ponds]).intersection(self.rect).difference(self.river) if ponds else Polygon()

    # -------------------------------------------------------------- land, pavements, kerbs
    def build_land(self):
        rect = self.rect
        land = rect.difference(self.road).difference(self.river)
        self.land = land
        near_road = self.road.buffer(WALK_W, join_style=2)
        edge_band = rect.difference(rect.buffer(-WALK_W, join_style=2))
        # pedestrian streets and squares (Petaling Street's market): paving people walk, no buildings on it
        peds = []
        for f in self.osm.pedestrian:
            g = f.get('line') or f.get('poly')
            if not g.intersects(self.win): continue
            peds.append(self.T(g).buffer(3.6, cap_style=2, join_style=2) if 'line' in f else self.T(g).buffer(0))
        self.ped = unary_union(peds).intersection(land).buffer(0) if peds else Polygon()
        walk = land.intersection(near_road.union(edge_band).union(self.ped)).buffer(0)
        pond = self.ponds.intersection(land.difference(walk)).buffer(0)
        inner = land.difference(walk).difference(pond).buffer(0)
        self.walk = walk
        kinds = [('forest', self.osm.forest), ('pitch', self.osm.pitches), ('park', self.osm.parks), ('lawn', self.osm.lawns),
                 ('park', self.osm.cemetery), ('parking', self.osm.parking), ('forecourt', self.osm.fuel), ('site', self.osm.sites)]
        taken = Polygon()
        self.landkind = {}
        for kind, feats in kinds:
            g = unary_union([self.T(f['poly']).buffer(0) for f in feats if f['poly'].intersects(self.win)] or [Polygon()])
            piece = inner.intersection(g).difference(taken).buffer(0)
            if not piece.is_empty:
                self.landkind[kind] = unary_union([self.landkind.get(kind, Polygon()), piece])
                taken = unary_union([taken, piece])
        # the rest: open paving downtown, lawns in the Lake Gardens, forest on Bukit Nanas
        ground = self.p.ground
        rest = inner.difference(taken).buffer(0)
        self.landkind[ground] = unary_union([self.landkind.get(ground, Polygon()), rest])
        self.landkind['pond'] = pond
        # meshes
        self.meshes['road'].flat(self.road, 0.0)
        self.meshes['walk'].flat(self.walk, TOP)
        for kind in ('park', 'lawn', 'forest', 'pitch', 'parking', 'forecourt', 'site'):
            if kind in self.landkind: self.meshes['land_' + kind].flat(self.landkind[kind], TOP)
        # (open paving is laid after the infill buildings have taken their lots: build_infill)
        self.meshes['pond'].flat(pond, TOP - 0.05)
        river = self.river

        def is_bank(mid, out):
            p = Point(mid[0] + out[0] * 0.6, mid[1] + out[2] * 0.6)
            return river.contains(p)

        def is_kerb(mid, out):
            p = Point(mid[0] + out[0] * 0.6, mid[1] + out[2] * 0.6)
            return not river.contains(p) and self.rect.buffer(0.3).contains(p)
        self.meshes['kerb'].walls(land, 0.0, TOP, test=is_kerb)
        # the land's outer edge along the patch boundary faces the grid road: kerb down to road level
        self.meshes['kerb'].walls(land, 0.0, TOP, test=lambda mid, out: not self.rect.contains(Point(mid[0] + out[0] * 0.6, mid[1] + out[2] * 0.6)))
        if not river.is_empty:
            self.meshes['bank'].walls(land, BED, TOP, test=is_bank)
            water = self.river_band
            self.meshes['water'].flat(water, WATER_Y)
            self.meshes['bed'].flat(water.buffer(1.0), BED)
            # road-level decks where roads cross the river inside the patch
            deck = self.road.intersection(river).buffer(0)
            self.meshes['road'].flat(deck, 0.0)                 # (already in road, but the underside shows too)
            self.meshes['deck'].flat(deck, -0.6, up=False)
            self.meshes['deck'].walls(deck, -0.6, 0.0)
            self.bridge_rails(deck)

    def bridge_rails(self, deck):
        """Low parapets along the edges of road decks over the river."""
        for poly in polys(deck):
            for ring in [orient(poly, 1.0).exterior]:
                c = list(ring.coords)
                for (x0, z0), (x1, z1) in zip(c, c[1:]):
                    mid = Point((x0 + x1) / 2, (z0 + z1) / 2)
                    if not self.river.buffer(-0.5).contains(mid): continue      # only the sides over water
                    dx, dz = x1 - x0, z1 - z0
                    ln = math.hypot(dx, dz)
                    if ln < 0.3: continue
                    out = (dz / ln, 0, -dx / ln)
                    self.box_along((x0, 0.0, z0), (x1, 0.0, z1), 0.35, 1.0, 'rail', inset=0.2, out=out)

    def box_along(self, a, b, width, height, mesh, inset=0.0, out=None, y0=None):
        """A long box (parapet / beam) from a to b at their heights."""
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        d[1] = 0
        ln = np.linalg.norm(d)
        if ln < 0.05: return
        d /= ln
        side = np.array((d[2], 0, -d[0]))
        if out is not None and inset: a = a - np.array(out) * inset; b = b - np.array(out) * inset
        m = self.meshes[mesh]
        ya0 = a[1] if y0 is None else y0
        yb0 = b[1] if y0 is None else y0
        w = side * width / 2
        A0, A1 = a - w, a + w
        B0, B1 = b - w, b + w
        up = np.array((0, 1, 0))
        m.quad(A0 + up * (a[1] + height - A0[1]), B0 + up * (b[1] + height - B0[1]), B1 + up * (b[1] + height - B1[1]), A1 + up * (a[1] + height - A1[1]), (0, 1, 0))
        for P0, P1, nn in ((A0, B0, -side), (B1, A1, side)):
            m.quad(np.array((P0[0], ya0 if P0 is A0 else yb0, P0[2])), np.array((P1[0], yb0 if P1 is B0 else ya0, P1[2])),
                   np.array((P1[0], (b[1] if P1 is B0 else a[1]) + height, P1[2])), np.array((P0[0], (a[1] if P0 is A0 else b[1]) + height, P0[2])), nn)

    # -------------------------------------------------------------- flyovers
    def build_flyovers(self):
        ground_road = self.road
        # each deck's footprint: side-by-side carriageways (squeezed together by the map scale) share
        # one deck, with no parapet down the middle of it
        foot = []
        for r in self.fly:
            hs, cs = r['h'], list(r['g'].coords)
            parts = [LineString([cs[i], cs[i + 1]]).buffer((r['w'] + 1.0) / 2, cap_style=2)
                     for i in range(len(cs) - 1) if max(hs[i], hs[i + 1]) >= 0.35]
            foot.append(unary_union(parts) if parts else Polygon())
        for n, r in enumerate(self.fly):
            others = unary_union([f for m, f in enumerate(foot) if m != n and not f.is_empty] or [Polygon()]).buffer(-0.4)
            self._others = others
            hs = r['h']
            coords = list(r['g'].coords)
            w = r['w'] + 1.0
            # split into runs that are off the ground
            for i in range(len(coords) - 1):
                (x0, z0), (x1, z1) = coords[i], coords[i + 1]
                y0, y1 = hs[i], hs[i + 1]
                if max(y0, y1) < 0.35: continue
                if not (self.rect.contains(Point(x0, z0)) or self.rect.contains(Point(x1, z1))): continue
                # where the ramp leaves the road it starts flush with it (the low ends are road-level surface)
                self.deck_segment((x0, y0 if y0 >= 0.35 else 0.0, z0), (x1, y1 if y1 >= 0.35 else 0.0, z1), w)
            # pillars every ~24 m where the deck is up, not standing in a ground road
            ls = r['g']
            n = int(ls.length // 24)
            for k in range(1, n + 1):
                pt = ls.interpolate(k * 24)
                s = k * 24
                # height at s
                acc, y = 0.0, 0.0
                for i in range(len(coords) - 1):
                    seg = math.dist(coords[i], coords[i + 1])
                    if acc + seg >= s:
                        t = (s - acc) / max(seg, 1e-6)
                        y = hs[i] + (hs[i + 1] - hs[i]) * t
                        break
                    acc += seg
                if y < 3.0 or not self.rect.contains(pt) or ground_road.buffer(0.8).contains(pt) or self.river.contains(pt): continue
                self.pillar(pt.x, pt.y, y - 0.9, 1.8)

    def deck_segment(self, a, b, width):
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        dh = np.array((d[0], 0, d[2]))
        ln = np.linalg.norm(dh)
        if ln < 0.05: return
        dh /= ln
        side = np.array((dh[2], 0, -dh[0])) * width / 2
        top = self.meshes['flyroad']
        top.quad(a - side + (0, 0.02, 0), b - side + (0, 0.02, 0), b + side + (0, 0.02, 0), a + side + (0, 0.02, 0), (0, 1, 0))
        under = self.meshes['deck']
        dn = np.array((0, -0.9, 0))
        under.quad(a - side + dn, a + side + dn, b + side + dn, b - side + dn, (0, -1, 0))
        under.quad(a - side + dn, b - side + dn, b - side, a - side, -side)
        under.quad(a + side, b + side, b + side + dn, a + side + dn, side)
        # parapets (not where the edge runs over the next carriageway's deck)
        for s in (-1, 1):
            e0, e1 = a + side * s * 0.96, b + side * s * 0.96
            others = getattr(self, '_others', None)
            if others is not None and not others.is_empty and others.contains(Point((e0[0] + e1[0]) / 2, (e0[2] + e1[2]) / 2)): continue
            self.box_along(tuple(e0), tuple(e1), 0.35, 1.0, 'barrier')

    def pillar(self, x, z, top, size):
        m = self.meshes['pillar']
        h = size / 2
        c = [(x - h, z - h), (x + h, z - h), (x + h, z + h), (x - h, z + h)]
        for (x0, z0), (x1, z1) in zip(c, c[1:] + c[:1]):
            dx, dz = x1 - x0, z1 - z0
            ln = math.hypot(dx, dz)
            m.quad((x0, 0, z0), (x1, 0, z1), (x1, top, z1), (x0, top, z0), (dz / ln, 0, -dx / ln))
        m.quad((x - size, top, z - 1.2), (x + size, top, z - 1.2), (x + size, top, z + 1.2), (x - size, top, z + 1.2), (0, 1, 0))

    # -------------------------------------------------------------- lane markings
    def build_markings(self):
        junctions = self.junction_points()
        jt = STRtree(junctions) if junctions else None
        for r in self.ground:
            if r['w'] < 9.5 or r['tags'].get('oneway') in ('yes', '1', '-1') or r['tags'].get('junction') == 'roundabout': continue
            g = r['g'].intersection(self.rect.buffer(-2))
            for ln in lines(g):
                L_ = ln.length
                s = 3.0
                while s + 3.0 < L_:
                    p0, p1 = ln.interpolate(s), ln.interpolate(s + 3.0)
                    mid = ln.interpolate(s + 1.5)
                    s += 7.5
                    if jt is not None and any(junctions[i].distance(mid) < r['w'] * 0.9 for i in jt.query(mid.buffer(r['w']))): continue
                    if not self.road.buffer(-0.5).contains(mid): continue
                    self.flat_strip((p0.x, p0.y), (p1.x, p1.y), 0.22, 0.012, 'mark_white')

    def junction_points(self):
        cnt = defaultdict(int)
        pos = {}
        for r in self.ground:
            coords = list(r['g'].coords)
            for n, c in zip(r['nodes'], coords):
                cnt[n] += 1
                pos[n] = c
            cnt[r['nodes'][0]] += 1
            cnt[r['nodes'][-1]] += 1
        return [Point(pos[n]) for n, c in cnt.items() if c >= 3]

    def flat_strip(self, a, b, width, y, mesh):
        dx, dz = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dz)
        if ln < 0.05: return
        sx, sz = dz / ln * width / 2, -dx / ln * width / 2
        self.meshes[mesh].quad((a[0] - sx, y, a[1] - sz), (b[0] - sx, y, b[1] - sz), (b[0] + sx, y, b[1] + sz), (a[0] + sx, y, a[1] + sz), (0, 1, 0))

    # -------------------------------------------------------------- buildings
    PASTEL = ['bld_pink', 'bld_yellow', 'bld_mint', 'bld_blue', 'bld_orange', 'bld_cream', 'bld_white', 'bld_lilac']
    OFFICE = ['bld_glass_blue', 'bld_glass_teal', 'bld_glass_grey', 'bld_white', 'bld_cream', 'bld_concrete']

    def build_buildings(self):
        keep_out = unary_union([self.road.buffer(1.4, join_style=2), self.river.buffer(2.0), self.ponds, self.ped.buffer(0.6)]).buffer(0)
        clears = []
        for name, (model, rad) in LANDMARKS.items():
            if rad <= 0: continue
            x, z = self.p.frame.to_game(*L.REAL[name]) if name in L.REAL else (None, None)
            if x is not None and self.rect.contains(Point(x, z)): clears.append(Point(x, z).buffer(rad))
        clear = unary_union(clears) if clears else Polygon()
        self.clear = clear
        inner = self.rect.buffer(-1.0, join_style=2)
        self.building_polys = []
        self.canopies = []
        for b in self.osm.buildings:
            if not b['poly'].intersects(self.win): continue
            real_area = b['poly'].area
            g = self.T(b['poly']).simplify(0.35)
            if clear.intersects(g.centroid.buffer(4)): continue
            g = g.intersection(inner).difference(keep_out).buffer(0)
            ps = polys(g)
            if not ps: continue
            g = max(ps, key=lambda q: q.area)
            if g.area < 18: continue
            btype = b['tags'].get('building', 'yes')
            if btype == 'roof':
                # a canopy on posts (covered walkways, petrol station forecourts), not a solid block
                if g.area > 12: self.canopy(g)
                continue
            # skinny slivers left after cutting the roads out
            if g.area / max(g.length, 1) < 1.3: continue
            lv = levels(b['tags'])
            rng = random.Random(b['id'])
            if lv is None:
                if btype in ('house', 'detached', 'terrace', 'bungalow', 'semidetached_house'): lv = rng.choice([2, 2, 3])
                elif btype in ('apartments', 'residential'): lv = rng.randint(6, 18) if real_area > 700 else rng.randint(3, 6)
                elif btype in ('commercial', 'office'): lv = rng.randint(8, 24) if real_area > 1200 else rng.randint(3, 6)
                elif btype in ('retail',): lv = rng.randint(3, 6)
                elif btype in ('industrial', 'warehouse', 'roof', 'garage', 'shed', 'hut', 'kiosk'): lv = 1 if btype in ('roof', 'shed', 'hut', 'kiosk') else 2
                else:
                    lv = 2 + int(real_area > 150) + int(real_area > 400) + (rng.randint(1, 8) if real_area > 1500 else 0)
            worship = b['tags'].get('amenity') == 'place_of_worship' or btype in ('mosque', 'temple', 'church')
            self.add_building(g, max(1.0, float(lv)), btype, rng, worship)

    HOMES = ('house', 'detached', 'terrace', 'bungalow', 'semidetached_house', 'apartments', 'residential', 'dormitory', 'hotel',
             'school', 'university', 'college', 'hospital', 'government', 'public', 'civic', 'industrial', 'warehouse', 'garage', 'shed', 'hut')

    def add_building(self, g, lv, btype, rng, worship=False):
        """Walls (with window facades), roof and parapet of one building of lv storeys on footprint g."""
        h = 3.4 * lv if lv <= 4 else 13.6 + (lv - 4) * 3.4 * 0.62
        h = min(h, 190.0)
        y0, y1 = TOP, TOP + h
        if worship:
            wall, roof = 'bld_white', 'roof_green' if btype == 'mosque' else 'roof_terracotta'
            self.meshes[wall].walls(g, y0, y1)
            self.meshes[roof].flat(g, y1)
            self.building_polys.append((g, h, wall, roof))
            return
        if lv >= 12:
            colour = rng.choice(self.OFFICE[:3]) if rng.random() < 0.7 else rng.choice(self.OFFICE)
            roof = 'roof_concrete'
        elif lv >= 6:
            colour = rng.choice(['bld_cream', 'bld_white', 'bld_concrete', 'bld_glass_grey', 'bld_mint', 'bld_blue'])
            roof = 'roof_concrete'
        else:
            colour = rng.choice(self.PASTEL)
            roof = rng.choice(['roof_terracotta', 'roof_terracotta', 'roof_concrete'])
        name = colour[4:]
        glass = name.startswith('glass')
        # street level: a lobby under a tower, shopfronts under the shophouses and offices, windows on homes
        if lv >= 12:
            band, band_h = 'lobby', LOBBY_H
        elif btype not in self.HOMES:
            band, band_h = 'shop', SHOP_H
        else:
            band, band_h = None, 0.0
        if band and h < band_h + 1.5:
            self.meshes[band].facade(g, y0, y1, BAY, h, SHOPS if band == 'shop' else 1, rng.randint(0, SHOPS - 1) / SHOPS)
        else:
            if band:
                self.meshes[band].facade(g, y0, y0 + band_h, BAY, band_h, SHOPS if band == 'shop' else 1, rng.randint(0, SHOPS - 1) / SHOPS)
            key = ('gls_' if glass else 'win_') + name
            self.meshes[key].facade(g, y0 + band_h, y1, GLASS_BAY if glass else BAY, FLOOR)
        self.meshes[roof].flat(g, y1)
        # a parapet lip on flat roofs of taller buildings
        if lv >= 6: self.meshes['roof_concrete'].walls(g.buffer(-0.35, join_style=2), y1, y1 + 1.0, outward=False)
        self.building_polys.append((g, h, colour, roof))

    def canopy(self, g, h=4.6):
        """A flat roof slab on slim posts."""
        g = g.simplify(0.4)
        m = self.meshes['canopy']
        m.flat(g, TOP + h)
        m.flat(g, TOP + h - 0.35, up=False)
        m.walls(g, TOP + h - 0.35, TOP + h)
        inner = g.buffer(-0.5, join_style=2)
        ring = polys(inner)[0].exterior if polys(inner) else polys(g)[0].exterior
        posts = []
        for ln in [LineString(ring.coords)]:
            n = max(4, int(ln.length // 8))
            posts += [ln.interpolate(i * ln.length / n) for i in range(n)]
        for q in posts:
            self.post(q.x, q.y, TOP + h - 0.35, 0.3)
        self.canopies.append(g)

    def post(self, x, z, top, size):
        m = self.meshes['post']
        s = size / 2
        c = [(x - s, z - s), (x + s, z - s), (x + s, z + s), (x - s, z + s)]
        for (x0, z0), (x1, z1) in zip(c, c[1:] + c[:1]):
            dx, dz = x1 - x0, z1 - z0
            ln = math.hypot(dx, dz)
            m.quad((x0, TOP, z0), (x1, TOP, z1), (x1, top, z1), (x0, top, z0), (dz / ln, 0, -dx / ln))

    # -------------------------------------------------------------- infill: the gaps OSM leaves
    def build_infill(self):
        """Fill the empty downtown lots OSM has no buildings for with plain blocks lined up with the land
        around them (heights from the real buildings nearby), then lay the paving that is left."""
        ground = self.p.ground
        self.infill = 0
        free = self.landkind.get(ground, Polygon())
        if self.p.infill and ground == 'paving' and not free.is_empty:
            lo, hi = self.p.infill
            avoid = [g.buffer(3.0) for g, *_ in self.building_polys] + [c.buffer(2.0) for c in self.canopies] + [self.clear.buffer(6)]
            for name in PLACES + list(LANDMARKS):
                if name in L.REAL:
                    x, z = self.p.frame.to_game(*L.REAL[name])
                    avoid.append(Point(x, z).buffer(24))
            avoid += [self.T(f['poly']).buffer(2) for f in self.osm.markets if f['poly'].intersects(self.win)]
            lots = free.difference(unary_union(avoid)).buffer(-1.2, join_style=2).buffer(0)
            cents = [g.centroid for g, *_ in self.building_polys]
            heights = [h for _, h, *_ in self.building_polys]
            tree = STRtree(cents) if cents else None
            for poly in polys(lots):
                if poly.area < 160: continue
                mrr = poly.minimum_rotated_rectangle
                xs = list(mrr.exterior.coords)
                e = max(((xs[i], xs[i + 1]) for i in range(len(xs) - 1)), key=lambda ab: math.dist(*ab))
                ang = math.degrees(math.atan2(e[1][1] - e[0][1], e[1][0] - e[0][0]))
                c = poly.centroid
                rp = affinity.rotate(poly, -ang, origin=c)
                x0, z0, x1, z1 = rp.bounds
                rng = random.Random(int(c.x * 7 + c.y * 13) & 0xffffff)
                # storeys of the real buildings around (median), else the district's range
                near = [heights[i] for i in tree.query(c.buffer(90))] if tree is not None else []
                x = x0
                while x < x1 - 6:
                    w = rng.uniform(12, 22)
                    z = z0
                    while z < z1 - 6:
                        d = rng.uniform(12, 24)
                        cell = box(x, z, min(x + w, x1), min(z + d, z1))
                        piece = cell.intersection(rp)
                        if piece.area > 90 and piece.area > 0.72 * cell.area:
                            fp = box(*piece.bounds) if piece.area > 0.96 * cell.area else piece.simplify(0.5)
                            fp = affinity.rotate(fp, ang, origin=c).intersection(poly).buffer(0)
                            ps = polys(fp)
                            if ps:
                                fp = max(ps, key=lambda q: q.area)
                                if near:
                                    mid = sorted(near)[len(near) // 2]
                                    lv = (mid / 3.4 if mid <= 13.6 else 4 + (mid - 13.6) / (3.4 * 0.62)) * rng.uniform(0.6, 1.25)
                                else:
                                    lv = lo + (hi - lo) * rng.random() ** 2
                                lv = max(lo, min(hi, round(lv)))
                                self.add_building(fp, lv, 'yes' if lv < 12 else 'office', rng)
                                self.infill += 1
                        z += d + 3.0
                    x += w + 3.0
            free = free.difference(unary_union([g.buffer(0.01) for g, *_ in self.building_polys[-self.infill:]] or [Polygon()])) if self.infill else free
        if ground == 'paving' and not free.is_empty: self.meshes['land_paving'].flat(free, TOP)

    # -------------------------------------------------------------- car park bays
    def build_parking_marks(self):
        """White bay lines in the surface car parks: double rows of 2.5 m bays either side of a 6 m aisle."""
        lots = self.landkind.get('parking')
        if lots is None or lots.is_empty: return
        lots = lots.difference(unary_union([g.buffer(1.0) for g, *_ in self.building_polys] + [c.buffer(0.5) for c in self.canopies] or [Polygon()]))
        for poly in polys(lots.buffer(-1.2, join_style=2)):
            if poly.area < 250: continue
            mrr = poly.minimum_rotated_rectangle
            xs = list(mrr.exterior.coords)
            e = max(((xs[i], xs[i + 1]) for i in range(len(xs) - 1)), key=lambda ab: math.dist(*ab))
            ang = math.degrees(math.atan2(e[1][1] - e[0][1], e[1][0] - e[0][0]))
            c = poly.centroid
            rp = affinity.rotate(poly, -ang, origin=c)
            x0, z0, x1, z1 = rp.bounds
            count = 0
            zc = z0 + 5.0
            while zc < z1 - 4.0 and count < 260:
                x = x0 + 1.0
                while x < x1 - 1.0 and count < 260:
                    for za, zb in ((zc - 5.0, zc), (zc, zc + 5.0)):
                        seg = LineString([(x, za), (x, zb)])
                        if rp.contains(seg):
                            g = affinity.rotate(seg, ang, origin=c)
                            (ax, az), (bx, bz) = g.coords
                            self.flat_strip((ax, az), (bx, bz), 0.14, TOP + 0.012, 'mark_white')
                            count += 1
                    x += 2.5
                zc += 16.0

    # -------------------------------------------------------------- street trees
    def build_street_trees(self):
        """Rain trees along the pavements of the bigger roads, 0.8 m in from the kerb."""
        rng = random.Random(len(self.p.name) * 31)
        blocked = unary_union([g.buffer(1.8) for g, *_ in self.building_polys] + [c.buffer(1.0) for c in self.canopies] +
                              [self.clear, self.river.buffer(2.0), self.road.buffer(0.6)]).buffer(0)
        walk = self.walk.buffer(-0.3)
        junctions = self.junction_points()
        jt = STRtree(junctions) if junctions else None
        for r in self.ground:
            if r['w'] < 10: continue
            for side in (-1, 1):
                try:
                    off = r['g'].offset_curve(side * (r['w'] / 2 + 0.8), join_style='mitre')
                except Exception:
                    continue
                for ln in lines(off):
                    s = rng.uniform(4, 12)
                    while s < ln.length:
                        q = ln.interpolate(s)
                        s += rng.uniform(15, 22)
                        if not self.rect.contains(q) or not walk.contains(q) or blocked.contains(q): continue
                        if jt is not None and any(junctions[i].distance(q) < 14 for i in jt.query(q.buffer(14))): continue
                        self.trees.append((q.x, q.y, rng.uniform(0.8, 1.1), 2 if rng.random() < 0.6 else 1))

    # -------------------------------------------------------------- trees
    def build_trees(self):
        self.trees = []
        blocked = unary_union([self.road.buffer(2.5), self.river.buffer(3), self.ponds.buffer(1), self.clear] +
                              [g.buffer(2.0) for g, *_ in self.building_polys]).buffer(0)
        for kind, spacing, models in (('forest', 10.5, (0, 0, 1)), ('park', 16.0, (0, 1, 2))):
            area = self.landkind.get(kind)
            if area is None or area.is_empty: continue
            x0, z0, x1, z1 = area.bounds
            rng = random.Random(len(self.trees) + 7)
            for x in np.arange(x0, x1, spacing):
                for z in np.arange(z0, z1, spacing):
                    p = Point(x + rng.uniform(-spacing * 0.4, spacing * 0.4), z + rng.uniform(-spacing * 0.4, spacing * 0.4))
                    if area.contains(p) and not blocked.contains(p):
                        self.trees.append((p.x, p.y, rng.uniform(0.85, 1.35), rng.choice(models)))

    def tree_meshes(self):
        """Cartoon trees merged into the patch: a six-sided trunk and a lumpy low-poly canopy (two leaf shades)."""
        ico = icosphere()
        for i, (x, z, sc, kind) in enumerate(self.trees):
            rng = random.Random(i * 7919 + int(x * 13) + int(z * 7))
            trunk_h = (2.6 if kind != 2 else 4.2) * sc
            r_t = 0.32 * sc
            tm = self.meshes['tree_trunk']
            ring = [(x + math.cos(a) * r_t, z + math.sin(a) * r_t) for a in np.linspace(0, 2 * math.pi, 7)[:-1]]
            for (x0, z0), (x1, z1) in zip(ring, ring[1:] + ring[:1]):
                dx, dz = x1 - x0, z1 - z0
                ln = math.hypot(dx, dz)
                tm.quad((x0, TOP, z0), (x1, TOP, z1), (x1, TOP + trunk_h + 0.6, z1), (x0, TOP + trunk_h + 0.6, z0), (dz / ln, 0, -dx / ln))
            shade = 'tree_leaf_a' if rng.random() < 0.55 else 'tree_leaf_b'
            if kind == 2: shade = 'tree_leaf_c'
            rx = (2.4 if kind == 0 else 1.9) * sc * rng.uniform(0.9, 1.15)
            ry = rx * (0.72 if kind == 0 else 0.9)
            cy = TOP + trunk_h + ry * 0.8
            lm = self.meshes[shade]
            jit = [1.0 + rng.uniform(-0.12, 0.12) for _ in ico[0]]
            V = [(x + vx * rx * j, cy + vy * ry * j, z + vz * rx * j) for (vx, vy, vz), j in zip(ico[0], jit)]
            for a, b, c in ico[1]:
                va, vb, vc = V[a], V[b], V[c]
                mid = np.array(((va[0] + vb[0] + vc[0]) / 3 - x, (va[1] + vb[1] + vc[1]) / 3 - cy, (va[2] + vb[2] + vc[2]) / 3 - z))
                lm.tri(va, vb, vc, mid)

    # -------------------------------------------------------------- traffic graph
    def build_graph(self):
        """Nodes where roads meet, bends every ~16 m; directed where one-way; cut at the patch edge."""
        rect = self.rect
        nodes = {}          # key -> [x, y, z]
        edges = set()
        deg = defaultdict(int)
        for r in self.roads:
            for n in r['nodes']: deg[n] += 1
            deg[r['nodes'][0]] += 1
            deg[r['nodes'][-1]] += 1
        fly_h = {}
        for r in self.fly:
            for n, h in zip(r['nodes'], r['h']): fly_h[n] = max(fly_h.get(n, 0), h)
        links = []
        for r in self.roads:
            coords = list(r['g'].coords)
            ow = r['tags'].get('oneway')
            if r['tags'].get('junction') == 'roundabout' and ow is None: ow = 'yes'
            direction = 1 if ow in ('yes', '1', 'true') else -1 if ow == '-1' else 0
            hs = [h if h >= 0.35 else 0.0 for h in (r.get('h') or [0.0] * len(coords))]     # low ramp ends are road-level (flush)
            keep = []
            last = None
            for i, (n, c) in enumerate(zip(r['nodes'], coords)):
                essential = i == 0 or i == len(coords) - 1 or deg[n] >= 3
                if essential or last is None or math.dist(c, last) >= 16 or abs(hs[i] - hs[max(0, i - 1)]) > 1.0:
                    keep.append((n, c, hs[i]))
                    last = c
            for (na, ca, ha), (nb, cb, hb) in zip(keep, keep[1:]):
                pa, pb = Point(ca), Point(cb)
                ina, inb = rect.contains(pa), rect.contains(pb)
                if not ina and not inb: continue
                if ina and inb:
                    mid = Point((ca[0] + cb[0]) / 2, (ca[1] + cb[1]) / 2)
                    if max(ha, hb) < 1.0 and self.river.contains(mid) and not self.decks.buffer(1.0).contains(mid): continue
                    nodes[na] = (ca[0], ha, ca[1])
                    nodes[nb] = (cb[0], hb, cb[1])
                    edges.add((na, nb, direction))
                else:
                    # crossing the patch edge: a boundary node there, linked to the grid road beyond
                    inside, outside = (na, ca, ha), (nb, cb, hb)
                    if not ina: inside, outside = outside, inside
                    seg = LineString([inside[1], outside[1]])
                    hit = seg.intersection(rect.exterior)
                    if hit.is_empty: continue
                    hp = hit if hit.geom_type == 'Point' else list(hit.geoms)[0]
                    key = ('edge', round(hp.x, 1), round(hp.y, 1))
                    nodes[inside[0]] = (inside[1][0], inside[2], inside[1][1])
                    nodes[key] = (hp.x, 0.0, hp.y)
                    d = direction if inside[0] == na else -direction
                    edges.add((inside[0], key, d))
                    links.append(key)
        # every street must be drivable into and out of from the rest of the map: a one-way stub cut off at
        # its entry (by the river or the patch edge) would trap traffic and strand mission routes. The grid
        # is one hub joined both ways to the link nodes; streets outside its strongly connected part are
        # made two-way, and whatever still can't be reached (car parks, islands) is dropped
        hub = ('hub',)

        def reach(forward):
            adj = defaultdict(list)
            for a, b, d in edges:
                if d >= 0: (adj[a] if forward else adj[b]).append(b if forward else a)
                if d <= 0: (adj[b] if forward else adj[a]).append(a if forward else b)
            for k in links: adj[hub].append(k); adj[k].append(hub)
            seen, stack = {hub}, [hub]
            while stack:
                x = stack.pop()
                for y in adj[x]:
                    if y not in seen: seen.add(y); stack.append(y)
            return seen
        for _ in range(3):
            strong = reach(True) & reach(False)
            fixed = {(a, b, d if (a in strong and b in strong) else 0) for a, b, d in edges}
            if fixed == edges: break
            edges = fixed
        strong = reach(True) & reach(False)
        und = defaultdict(set)
        for a, b, d in edges: und[a].add(b); und[b].add(a)
        linkset = set(links)
        keep_nodes = {n for n in nodes if n in strong}
        order = [n for n in nodes if n in keep_nodes]
        index = {n: i for i, n in enumerate(order)}
        self.gnodes = [nodes[n] for n in order]
        self.gjunction = [1 if (len(und[n]) >= 3 or n in linkset) else 0 for n in order]
        self.gedges = [(index[a], index[b], d) for a, b, d in edges if a in index and b in index]
        self.glinks = [index[k] for k in links if k in index]

    # -------------------------------------------------------------- pedestrians
    def build_rings(self):
        self.rings = []
        for poly in polys(self.land):
            if poly.area < 250: continue
            inner = poly.buffer(-1.6, join_style=2)
            for q in polys(inner):
                q = q.simplify(0.8)
                if q.length > 40 and q.exterior is not None:
                    self.rings.append((list(q.exterior.coords)[:-1], 0))
        for poly in polys(self.ped):
            for q in polys(poly.buffer(-1.2, join_style=2)):
                q = q.simplify(0.6)
                if q.length > 30: self.rings.append((list(q.exterior.coords)[:-1], 1))

    # -------------------------------------------------------------- landmarks + places
    def build_anchors(self):
        self.anchors = []
        for name, model in [(n, LANDMARKS[n][0]) for n in LANDMARKS] + [(n, '') for n in PLACES]:
            if name not in L.REAL: continue
            x, z = self.p.frame.to_game(*L.REAL[name])
            if not self.rect.contains(Point(x, z)): continue
            # the nearest road-level graph node: where missions send you
            best, bd = None, 1e9
            for (nx, ny, nz), j in zip(self.gnodes, self.gjunction):
                if ny > 0.5: continue
                d = math.hypot(nx - x, nz - z)
                if d < bd: best, bd = (nx, nz), d
            self.anchors.append((name, model, x, z, best or (x, z)))
        # the night market: stalls down both sides of the pedestrian streets round Petaling Street
        if 'Petaling' in L.REAL:
            px, pz = self.p.frame.to_game(*L.REAL['Petaling'])
            near = Point(px, pz).buffer(170)
            rng = random.Random(118)
            kinds = ['env_market_canopy_red', 'env_market_canopy_blue', 'env_market_canopy_yellow', 'env_market_canopy_teal']
            for f in self.osm.pedestrian:
                if 'line' not in f: continue
                ln = self.T(f['line']).intersection(near).intersection(self.rect.buffer(-3))
                for seg in lines(ln):
                    s = 4.0
                    while s < seg.length - 4.0:
                        a, b = seg.interpolate(s - 0.5), seg.interpolate(s + 0.5)
                        dx, dz = b.x - a.x, b.y - a.y
                        l_ = math.hypot(dx, dz) or 1
                        for side in (-1, 1):
                            cx, cz = seg.interpolate(s).x + dz / l_ * 2.3 * side, seg.interpolate(s).y - dx / l_ * 2.3 * side
                            if self.ped.buffer(-0.5).contains(Point(cx, cz)):
                                # 'place' = a point the stall faces (the middle of the street)
                                self.anchors.append(('Stall', rng.choice(kinds), cx, cz, (seg.interpolate(s).x, seg.interpolate(s).y)))
                        s += 7.5

    # -------------------------------------------------------------- elevated rail (LRT deck, monorail beam)
    def build_rails(self):
        self.rail_lines = []
        reach = self.rect.buffer(L.ROAD / 2, join_style=2)            # meet the next patch's guideway mid-road
        for rl in self.osm.rails:
            kind = rl['kind']
            t = rl['tags']
            if kind not in RAIL_H or t.get('tunnel') == 'yes' or t.get('layer', '0').startswith('-'): continue
            if kind == 'light_rail' and t.get('bridge') not in ('yes', 'viaduct') and t.get('layer', '0') in ('0', ''): continue
            if not rl['line'].intersects(self.win): continue
            h = RAIL_H[kind]
            for ln in lines(self.T(rl['line']).intersection(reach)):
                if ln.length < 8: continue
                pts = [(x, h, z) for x, z in ln.coords]
                self.rail_lines.append((kind, pts))
                for (x0, y0, z0), (x1, y1, z1) in zip(pts, pts[1:]):
                    if kind == 'monorail':
                        self.box_along((x0, y0, z0), (x1, y1, z1), 1.3, 1.5, 'guideway')
                    else:
                        self.box_along((x0, y0 - 1.2, z0), (x1, y1 - 1.2, z1), 7.0, 1.2, 'guideway')
                        for sd in (-1, 1):
                            dx, dz = x1 - x0, z1 - z0
                            l_ = math.hypot(dx, dz) or 1
                            ox, oz = dz / l_ * 3.3 * sd, -dx / l_ * 3.3 * sd
                            self.box_along((x0 + ox, y0, z0 + oz), (x1 + ox, y1, z1 + oz), 0.3, 1.1, 'barrier')
                n = int(ln.length // 26)
                for k in range(1, n + 1):
                    pt = ln.interpolate(k * 26)
                    if not self.rect.contains(pt) or self.road.buffer(0.6).contains(pt) or self.river.contains(pt): continue
                    top = h - (1.4 if kind == 'monorail' else 1.2)
                    self.pillar(pt.x, pt.y, top, 1.4 if kind == 'monorail' else 1.9)

    # -------------------------------------------------------------- run
    def run(self):
        self.classify_roads()
        self.solve_elevations()
        self.build_water()
        self.build_roads()
        self.build_land()
        self.build_flyovers()
        self.build_markings()
        self.build_buildings()
        self.build_infill()
        self.build_parking_marks()
        self.build_trees()
        self.build_street_trees()
        # (tree meshes are built in the game from self.trees: CityBuilder.PatchTrees)
        self.build_graph()
        self.build_rings()
        self.build_anchors()
        self.build_rails()
        return self


# ------------------------------------------------------------------ output
def write_patch(pb, path):
    """
    Little-endian binary:
      'KLP5' | name(str) | rect 4f
      int nMeshes; per mesh: key(str) int nv, nv*3f pos, nv*3f normals, byte hasUV, [nv*2f uv], int nt, nt*int32 indices
      int nNodes; per node: 3f pos, byte junction | int nEdges; per edge: int a, int b, sbyte dir | int nLinks; int node...
      int nRings; per ring: int n, byte kind (0 pavement, 1 pedestrian street), n*2f | int nTrees; per tree: 3f (x, z, scale), byte kind
      int nAnchors; per anchor: name(str), model(str), 2f pos, 2f road place
      int nWater; per ring: int n, n*2f (river outlines, reaching under the surrounding grid roads)
      int nRails; per line: kind(str), int n, n*3f (elevated LRT / monorail centre lines)
    str = int byte length + utf-8
    """
    def s(x):
        b = x.encode('utf-8')
        return struct.pack('<i', len(b)) + b
    out = bytearray(b'KLP5')
    out += s(pb.p.name) + struct.pack('<4f', *pb.p.rect)
    meshes = [(k, m) for k, m in pb.meshes.items() if m.t]
    out += struct.pack('<i', len(meshes))
    for k, m in meshes:
        out += s(k)
        # share the vertices the triangles have in common (same position, normal and UV)
        cols = [np.array(m.v, np.float32), np.array(m.n, np.float32)] + ([np.array(m.uv, np.float32)] if m.has_uv else [])
        uniq, inv = np.unique(np.concatenate(cols, axis=1), axis=0, return_inverse=True)
        t = inv.reshape(-1)[np.array(m.t)].astype(np.int32)
        out += struct.pack('<i', len(uniq)) + np.ascontiguousarray(uniq[:, 0:3]).tobytes() + np.ascontiguousarray(uniq[:, 3:6]).tobytes()
        if m.has_uv: out += struct.pack('<B', 1) + np.ascontiguousarray(uniq[:, 6:8]).tobytes()
        else: out += struct.pack('<B', 0)
        out += struct.pack('<i', len(t)) + t.tobytes()
    out += struct.pack('<i', len(pb.gnodes))
    for (x, y, z), j in zip(pb.gnodes, pb.gjunction): out += struct.pack('<3fB', x, y, z, j)
    out += struct.pack('<i', len(pb.gedges))
    for a, b, d in pb.gedges: out += struct.pack('<iib', a, b, d)
    out += struct.pack('<i', len(pb.glinks))
    for i in pb.glinks: out += struct.pack('<i', i)
    out += struct.pack('<i', len(pb.rings))
    for ring, kind in pb.rings:
        out += struct.pack('<iB', len(ring), kind)
        for x, z in ring: out += struct.pack('<2f', x, z)
    out += struct.pack('<i', len(pb.trees))
    for x, z, sc, kind in pb.trees: out += struct.pack('<3fB', x, z, sc, kind)
    out += struct.pack('<i', len(pb.anchors))
    for name, model, x, z, (px, pz) in pb.anchors: out += s(name) + s(model) + struct.pack('<4f', x, z, px, pz)
    water = [list(q.simplify(0.6).exterior.coords)[:-1] for q in polys(pb.river_band)]
    out += struct.pack('<i', len(water))
    for ring in water:
        out += struct.pack('<i', len(ring))
        for x, z in ring: out += struct.pack('<2f', x, z)
    out += struct.pack('<i', len(pb.rail_lines))
    for kind, pts in pb.rail_lines:
        out += s(kind) + struct.pack('<i', len(pts))
        for x, y, z in pts: out += struct.pack('<3f', x, y, z)
    with open(path, 'wb') as f: f.write(out)
    return len(out)


MESH_COLORS = {
    'land_paving': (228, 222, 210), 'land_park': (160, 205, 130), 'land_forest': (100, 160, 90), 'land_pitch': (125, 190, 105),
    'walk': (214, 204, 188), 'kerb': (120, 120, 120), 'road': (88, 88, 94), 'mark_white': (250, 250, 250), 'pond': (90, 150, 215),
    'water': (60, 125, 210), 'deck': (150, 150, 150), 'rail': (230, 230, 220), 'flyroad': (70, 80, 150), 'barrier': (200, 200, 205),
    'pillar': (160, 160, 160), 'bank': (140, 140, 140), 'bed': (40, 70, 110), 'guideway': (215, 215, 210),
    'tree_leaf_a': (60, 140, 60), 'tree_leaf_b': (90, 165, 70), 'tree_leaf_c': (70, 150, 90), 'tree_trunk': (120, 80, 50),
    'land_parking': (120, 122, 128), 'land_lawn': (150, 200, 120), 'land_forecourt': (175, 175, 170), 'land_site': (196, 150, 100), 'canopy': (235, 235, 230),
    'post': (170, 170, 170), 'shop': (200, 120, 110), 'lobby': (150, 170, 190),
}


def mesh_color(key):
    if key in MESH_COLORS: return MESH_COLORS[key]
    h = abs(hash(key)) % 360
    import colorsys
    bld = key.startswith(('bld_', 'win_', 'gls_'))
    r, g, b = colorsys.hsv_to_rgb(h / 360, 0.35 if bld else 0.15, 0.8 if bld else 0.6)
    return (int(r * 255), int(g * 255), int(b * 255))


def preview(pb, path):
    """Top-down raster of the generated meshes (only faces pointing up), plus the graph and anchors."""
    from PIL import Image, ImageDraw
    x0, z0, x1, z1 = pb.p.rect
    S = 1.2
    img = Image.new('RGB', (int((x1 - x0) * S), int((z1 - z0) * S)), (255, 0, 255))
    dr = ImageDraw.Draw(img)
    P = lambda x, z: ((x - x0) * S, (z1 - z) * S)
    items = []
    for key, m in pb.meshes.items():
        if not m.t: continue
        v = np.array(m.v); n = np.array(m.n)
        for i in range(0, len(m.t), 3):
            a, b, c = m.t[i], m.t[i + 1], m.t[i + 2]
            if n[a][1] < 0.5: continue
            items.append((max(v[a][1], v[b][1], v[c][1]), key, v[a], v[b], v[c]))
    items.sort(key=lambda it: it[0])
    for y, key, a, b, c in items:
        col = mesh_color(key)
        if key.startswith(('bld_', 'roof_', 'win_', 'gls_')):
            f = max(0.45, 1.0 - y / 260.0)
            col = tuple(int(ch * f) for ch in col)
        dr.polygon([P(a[0], a[2]), P(b[0], b[2]), P(c[0], c[2])], fill=col)
    for x, z, sc, k in pb.trees: dr.ellipse([P(x - 2.2 * sc, z + 2.2 * sc), P(x + 2.2 * sc, z - 2.2 * sc)], fill=(40, 105, 40))
    for a, b, d in pb.gedges:
        (ax, ay, az), (bx, by, bz) = pb.gnodes[a], pb.gnodes[b]
        dr.line([P(ax, az), P(bx, bz)], fill=(255, 220, 0) if d == 0 else (255, 120, 0), width=1)
    for i in pb.glinks:
        x, y, z = pb.gnodes[i]
        dr.ellipse([P(x - 3, z + 3), P(x + 3, z - 3)], fill=(255, 0, 0))
    for name, model, x, z, (px, pz) in pb.anchors:
        dr.ellipse([P(x - 5, z + 5), P(x + 5, z - 5)], outline=(255, 0, 255), width=3)
        dr.text(P(x + 6, z), name, fill=(120, 0, 120))
        dr.line([P(x, z), P(px, pz)], fill=(255, 0, 255))
    img.save(path)


def write_layout(path):
    import json
    d = dict(K=L.K, pitch=L.PITCH, road=L.ROAD, nx=L.NX, nz=L.NZ, gombak=L.G, north_row=L.RN,
             patches=[dict(name=p.name, c0=p.cells[0], r0=p.cells[1], c1=p.cells[2], r1=p.cells[3]) for p in L.PATCHES],
             rivers=[dict(name='Gombak', col=L.G, r0=L.RN, r1=L.NZ - 1, width=RIVER_W['Gombak']),
                     dict(name='Klang', col=L.G + 3, r0=L.RN, r1=L.NZ - 1, width=RIVER_W['Klang'])],
             kampung=dict(c0=L.G + 1, c1=L.G + 2, r0=L.RN, r1=L.NZ - 1))
    with open(path, 'w') as f: json.dump(d, f, indent=1)


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(RES, exist_ok=True)
    write_layout(os.path.join(RES, 'layout.json'))
    only = set(sys.argv[1:])
    osm = OSM()
    for p in L.PATCHES:
        if only and p.name not in only: continue
        pb = PatchBuild(osm, p).run()
        size = write_patch(pb, os.path.join(RES, p.name + '.bytes'))
        preview(pb, os.path.join(OUT, p.name + '.png'))
        tris = sum(len(m.t) // 3 for m in pb.meshes.values())
        print(f'{p.name:12s} {size / 1e6:5.2f} MB  tris={tris:7d}  buildings={len(pb.building_polys):4d} (infill {pb.infill})  fly={len(pb.fly):3d}  '
              f'nodes={len(pb.gnodes):4d} edges={len(pb.gedges):4d} links={len(pb.glinks):3d} rings={len(pb.rings):3d} trees={len(pb.trees):4d} '
              f'anchors={[a[0] for a in pb.anchors]}')
