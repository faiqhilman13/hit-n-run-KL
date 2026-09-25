"""
The KL map layout: a road grid over a compressed copy of real Kuala Lumpur.

Game coordinates: X east, Z north, metres. The grid is the one CityBuilder uses:
    RoadX(i) = X0 + i*PITCH, RoadZ(k) = Z0 + k*PITCH, a block interior = [RoadX(c)+ROAD/2, RoadX(c+1)-ROAD/2].
Real coordinates: metres east/north of Masjid Jamek (see osm.py), mapped into the game by
    X = fx + K*x,  Z = fz + K*y
per frame. The heritage core (Old Town, Lake Gardens, KL Sentral/Brickfields) shares the MAIN frame,
so its patches line up across the grid roads between them and the rivers run through continuously.
KL Tower, KLCC and Bukit Bintang have their own frames, pulled in closer (the gaps between districts
are filler, not real).

Cell kinds: patch (real OSM content), river (a straight grid channel), kampung (the existing kampung
lots), filler (the existing district generators), landmark (one of our landmark blocks on its own).
"""
from dataclasses import dataclass, field

K = 0.65                 # real -> game scale
PITCH, ROAD = 116.0, 20.0
BLOCK = PITCH - ROAD
NX, NZ = 18, 20
X0, Z0 = -NX * PITCH / 2, -NZ * PITCH / 2

G = 7                    # the Gombak river column
R0 = 5                   # bottom row of the Old Town patch (9 rows: R0 .. R0+8)
RN = R0 + 9              # first row north of the Old Town (the kampung rows start here)

# real crossing points of the two river arms on the Old Town's north edge (solve_frame.py)
GOMBAK_X, NORTH_Y = -214.0, 374.0


def road_x(i): return X0 + i * PITCH
def road_z(k): return Z0 + k * PITCH
def col_center(c): return road_x(c) + PITCH / 2
def row_center(r): return road_z(r) + PITCH / 2


def cell_rect(c0, r0, c1, r1):
    """Interior game rect of the cells c0..c1 x r0..r1 (inclusive), between the bounding grid roads."""
    return (road_x(c0) + ROAD / 2, road_z(r0) + ROAD / 2, road_x(c1 + 1) - ROAD / 2, road_z(r1 + 1) - ROAD / 2)


@dataclass
class Frame:
    fx: float
    fz: float

    def to_game(self, x, y): return (self.fx + K * x, self.fz + K * y)
    def to_real(self, X, Z): return ((X - self.fx) / K, (Z - self.fz) / K)


# the MAIN frame: the Gombak at its column centre and the Old Town's north edge on the grid road
MAIN = Frame(fx=col_center(G) - K * GOMBAK_X, fz=road_z(RN) - ROAD / 2 - K * NORTH_Y)


def frame_at(real_xy, game_xy):
    """A frame that puts the real point real_xy at the game point game_xy."""
    return Frame(fx=game_xy[0] - K * real_xy[0], fz=game_xy[1] - K * real_xy[1])


@dataclass
class Patch:
    name: str
    cells: tuple            # (c0, r0, c1, r1) inclusive
    frame: Frame
    rivers: bool = False    # keep the real rivers (only where they join grid channels / the map edge)
    note: str = ''
    ground: str = 'paving'  # what open land with no park / car park / building on it is
    infill: tuple = ()      # (min, max) storeys of the buildings filling empty lots; () = leave them open

    @property
    def rect(self): return cell_rect(*self.cells)

    @property
    def real_window(self):
        x0, z0, x1, z1 = self.rect
        a = self.frame.to_real(x0, z0)
        b = self.frame.to_real(x1, z1)
        return (a[0], a[1], b[0], b[1])


# real landmark positions (m from Masjid Jamek), from the OSM probe
REAL = {
    'MasjidJamek': (25, -3), 'Dataran': (-197, -17), 'SultanAbdulSamad': (-113, -50), 'PasarSeni': (0, -374),
    'Petaling': (240, -560), 'Merdeka118': (582, -796), 'StadiumMerdeka': (574, -1055), 'StadiumNegara': (817, -899),
    'MasjidNegara': (-419, -779), 'Maybank': (436, -170), 'KLRailway': (-233, -1050), 'Dayabumi': (-146, -499),
    'KLTower': (918, 438), 'Petronas': (1807, 1002), 'KLCCPark': (2055, 730), 'Pavilion': (1974, 25),
    'KLSentral': (-966, -1610), 'MuziumNegara': (-903, -1238), 'TuguNegara': (-1297, 59), 'Perdana': (-1000, -520),
    'Brickfields': (-1167, -2090),
}

PATCHES = [
    # the heritage core, all in the MAIN frame
    Patch('KotaLama', (G - 2, R0, G + 6, R0 + 8), MAIN, rivers=True, infill=(2, 7),
          note='Dataran Merdeka, Masjid Jamek at the confluence, Pasar Seni, Petaling Street, Merdeka 118, the stadiums, Masjid Negara'),
    Patch('TamanTasik', (G - 7, R0, G - 3, R0 + 8), MAIN, ground='park', note='Lake Gardens: Perdana Botanical Gardens, Tugu Negara'),
    Patch('KLSentral', (G - 7, R0 - 5, G + 1, R0 - 1), MAIN, rivers=True, infill=(3, 14),
          note='KL Sentral, Brickfields, Muzium Negara; the Klang heading south'),
    # the Golden Triangle side, each on its own frame (closer together than in real life)
    Patch('BukitNanas', (G + 4, RN, G + 6, RN + 2), frame_at(REAL['KLTower'], (col_center(G + 5), row_center(RN + 1))), ground='forest',
          note='KL Tower on its forest hill'),
    Patch('KLCC', (G + 7, RN + 2, G + 10, RN + 5), frame_at((1900, 870), (road_x(G + 9), road_z(RN + 4))), infill=(8, 30),
          note='Petronas Towers, Suria, KLCC Park'),
    Patch('BukitBintang', (G + 7, R0 + 4, G + 10, R0 + 8), frame_at((1850, -60), (road_x(G + 9), road_z(R0 + 6) + 30)), infill=(4, 24),
          note='Pavilion, Jalan Bukit Bintang, Jalan Alor'),
]


def cell_kind(c, r):
    """What fills grid cell (c, r) when it is not in a patch."""
    for p in PATCHES:
        c0, r0, c1, r1 = p.cells
        if c0 <= c <= c1 and r0 <= r <= r1: return ('patch', p.name)
    if r >= RN:
        if c == G: return ('river', 'Gombak')
        if c == G + 3: return ('river', 'Klang')
        if c in (G + 1, G + 2): return ('kampung', None)
    return ('filler', None)


if __name__ == '__main__':
    print(f'map {NX}x{NZ} cells = {NX * PITCH:.0f} x {NZ * PITCH:.0f} m')
    for p in PATCHES:
        w = p.real_window
        print(f'{p.name:12s} cells={p.cells} game rect=({p.rect[0]:.0f},{p.rect[1]:.0f})-({p.rect[2]:.0f},{p.rect[3]:.0f}) '
              f'real window x[{w[0]:.0f},{w[2]:.0f}] y[{w[1]:.0f},{w[3]:.0f}]')
    for n in ('MasjidJamek', 'Dataran', 'MasjidNegara', 'Merdeka118', 'StadiumMerdeka', 'KLSentral', 'TuguNegara'):
        print(n, [round(v) for v in MAIN.to_game(*REAL[n])])
