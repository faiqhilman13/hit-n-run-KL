"""Hand-painted-style surface textures for the Hit & Run look (grass, asphalt, paving, brick,
timber, plaster, concrete, leaves, water, roof tiles, floor tiles).

Each is a seamless 256x256 greyscale *detail* map (mid-grey = no change): the shader multiplies
it onto the surface's own palette colour, so every building keeps its colour (and runtime
recolours still work) but gains painted strokes, mortar lines, planks, cracks and blotches.

  uv run --no-project --with numpy --with pillow python Tools/gen_surfaces.py
Writes Assets/KampungRun/Textures/Surfaces/surf_NN_<name>.png + a preview sheet.
"""
import os
import numpy as np
from PIL import Image

N = 256
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Assets", "KampungRun", "Textures", "Surfaces")
rng = np.random.default_rng(7)

# order = surface id (1-based) used by the shader and the palette
NAMES = ["grass", "asphalt", "paving", "brick", "timber", "plaster", "concrete", "leaves", "water", "roof", "floortile"]


def noise(scale, seed=None):
    """Seamless band-limited noise in [0,1]: white noise filtered in frequency space."""
    r = np.random.default_rng(seed) if seed is not None else rng
    w = r.standard_normal((N, N))
    f = np.fft.fft2(w)
    ky = np.fft.fftfreq(N)[:, None]
    kx = np.fft.fftfreq(N)[None, :]
    k = np.sqrt(kx * kx + ky * ky) * N
    filt = np.exp(-(k / scale) ** 2)
    out = np.real(np.fft.ifft2(f * filt))
    out -= out.min()
    return out / max(out.max(), 1e-9)


def stroke(img, x, y, dx, dy, width, value, alpha):
    """A soft paint stroke (wraps around the tile edges)."""
    L = int(max(abs(dx), abs(dy)) * 1.5) + 1
    for i in range(L):
        t = i / max(L - 1, 1)
        cx, cy = x + dx * t, y + dy * t
        taper = np.sin(t * np.pi) ** 0.6
        rr = max(0.6, width * taper)
        x0, x1 = int(cx - rr - 1), int(cx + rr + 2)
        y0, y1 = int(cy - rr - 1), int(cy + rr + 2)
        for yy in range(y0, y1):
            for xx in range(x0, x1):
                d = np.hypot(xx - cx, yy - cy)
                if d <= rr:
                    a = alpha * (1 - d / (rr + 1e-6)) ** 0.5
                    img[yy % N, xx % N] = img[yy % N, xx % N] * (1 - a) + value * a


def grid_lines(img, nx, ny, width, value, offset_rows=False, jitter=0.0):
    ys = np.mgrid[0:N, 0:N][0]
    xs = np.mgrid[0:N, 0:N][1]
    ch = N / ny
    row = (ys // ch).astype(int)
    xo = np.where((row % 2 == 1) & offset_rows, N / nx / 2, 0)
    fx = ((xs + xo) % (N / nx))
    fy = ys % ch
    mask = (fx < width) | (fy < width)
    img[mask] = img[mask] * 0.3 + value * 0.7
    return row, ((xs + xo) // (N / nx)).astype(int)


def cell_tint(img, rows, cols, amount, seed):
    r = np.random.default_rng(seed)
    table = r.uniform(-amount, amount, (64, 64))
    img += table[rows % 64, cols % 64]


def finish(img, lo=0.28, hi=0.72):
    img = np.clip(img, 0, 1)
    return lo + (hi - lo) * img


def grass():
    img = 0.5 + (noise(6) - 0.5) * 0.5 + (noise(24) - 0.5) * 0.25
    for _ in range(900):                              # little blade strokes
        x, y = rng.uniform(0, N, 2)
        v = rng.choice([0.2, 0.85, 0.3, 0.75])
        stroke(img, x, y, rng.uniform(-3, 3), rng.uniform(-9, -4), rng.uniform(0.8, 1.6), v, 0.55)
    for _ in range(14):                               # darker clover clumps
        x, y = rng.uniform(0, N, 2)
        for k in range(5):
            stroke(img, x + rng.uniform(-6, 6), y + rng.uniform(-6, 6), 1, 1, rng.uniform(3, 5), 0.25, 0.4)
    return finish(img, 0.22, 0.78)


def asphalt():
    img = 0.5 + (noise(5) - 0.5) * 0.35
    speck = rng.random((N, N))
    img[speck > 0.93] += 0.22
    img[speck < 0.05] -= 0.2
    for _ in range(4):                                # tar-patched cracks
        x, y = rng.uniform(0, N, 2)
        for k in range(12):
            dx, dy = rng.uniform(-12, 12, 2)
            stroke(img, x, y, dx, dy, 1.1, 0.12, 0.8)
            x, y = x + dx, y + dy
    for _ in range(3):                                # repair patches
        x, y = rng.uniform(0, N, 2)
        stroke(img, x, y, rng.uniform(10, 30), 2, rng.uniform(10, 18), 0.38, 0.35)
    return finish(img, 0.3, 0.72)


def paving():
    img = 0.55 + (noise(8) - 0.5) * 0.25
    rows, cols = grid_lines(img, 4, 4, 3, 0.18)
    cell_tint(img, rows, cols, 0.1, 11)
    ys = np.mgrid[0:N, 0:N][0] % 64
    xs = np.mgrid[0:N, 0:N][1] % 64
    img[(ys > 3) & (ys < 7)] += 0.08                  # soft bevel highlight on each slab
    img[(xs > 3) & (xs < 7)] += 0.06
    return finish(img, 0.26, 0.76)


def brick():
    img = 0.5 + (noise(10) - 0.5) * 0.3
    rows, cols = grid_lines(img, 4, 8, 4, 0.92, offset_rows=True)
    cell_tint(img, rows, cols, 0.14, 12)
    for _ in range(40):                               # chipped / sooty bricks
        x, y = rng.uniform(0, N, 2)
        stroke(img, x, y, rng.uniform(-6, 6), rng.uniform(-2, 2), rng.uniform(2, 4), 0.3, 0.3)
    return finish(img, 0.26, 0.8)


def timber():
    ys = np.mgrid[0:N, 0:N][0]
    img = 0.5 + (noise(4) - 0.5) * 0.2 + np.zeros((N, N))
    plank = N / 8
    row = (ys // plank).astype(int)
    img += np.random.default_rng(13).uniform(-0.1, 0.1, 16)[row % 16]
    # grain: stretched noise along X
    g = noise(40)
    g = np.roll(g, 0, axis=1)
    grain = np.repeat(g[:, ::8], 8, axis=1)[:, :N]
    img += (grain - 0.5) * 0.25
    img[(ys % plank) < 3] = 0.12                      # dark gaps between boards
    img[((ys % plank) >= 3) & ((ys % plank) < 5)] += 0.12
    for _ in range(10):                               # knots
        x, y = rng.uniform(0, N, 2)
        stroke(img, x, y, 3, 0.5, 2.5, 0.25, 0.6)
    # board ends staggered per row
    xs = np.mgrid[0:N, 0:N][1]
    ends = np.random.default_rng(14).integers(0, N, 16)
    img[((xs - ends[row % 16]) % N < 2)] = 0.18
    return finish(img, 0.24, 0.78)


def plaster():
    img = 0.5 + (noise(3) - 0.5) * 0.45 + (noise(14) - 0.5) * 0.18
    for _ in range(60):                               # broad roller strokes
        x, y = rng.uniform(0, N, 2)
        stroke(img, x, y, rng.uniform(-30, 30), rng.uniform(-6, 6), rng.uniform(4, 8), rng.choice([0.38, 0.62]), 0.18)
    for _ in range(3):                                # hairline cracks
        x, y = rng.uniform(0, N, 2)
        for k in range(8):
            dx, dy = rng.uniform(-8, 8), rng.uniform(2, 10)
            stroke(img, x, y, dx, dy, 0.7, 0.2, 0.7)
            x, y = x + dx, y + dy
    return finish(img, 0.34, 0.68)


def concrete():
    img = 0.5 + (noise(6) - 0.5) * 0.35
    speck = rng.random((N, N))
    img[speck > 0.96] -= 0.18
    grid_lines(img, 2, 2, 2, 0.25)
    for _ in range(6):                                # water stains running down
        x = rng.uniform(0, N)
        stroke(img, x, rng.uniform(0, N), rng.uniform(-2, 2), 60, rng.uniform(4, 9), 0.36, 0.25)
    return finish(img, 0.3, 0.72)


def leaves():
    img = 0.45 + (noise(5) - 0.5) * 0.3
    for _ in range(420):                              # overlapping leaf dabs, lit from above
        x, y = rng.uniform(0, N, 2)
        r = rng.uniform(3, 7)
        stroke(img, x, y, rng.uniform(-3, 3), rng.uniform(-3, 3), r, 0.25, 0.5)
        stroke(img, x - 1, y - 2, 1, 1, r * 0.55, 0.85, 0.45)
    return finish(img, 0.2, 0.82)


def water():
    img = 0.5 + (noise(4) - 0.5) * 0.3
    for _ in range(160):                              # wavy highlight dashes
        x, y = rng.uniform(0, N, 2)
        stroke(img, x, y, rng.uniform(8, 22), rng.uniform(-1, 1), rng.uniform(1, 2), 0.95, 0.55)
    for _ in range(90):
        x, y = rng.uniform(0, N, 2)
        stroke(img, x, y, rng.uniform(8, 18), rng.uniform(-1, 1), 1.2, 0.2, 0.35)
    return finish(img, 0.3, 0.8)


def roof():
    ys = np.mgrid[0:N, 0:N][0]
    xs = np.mgrid[0:N, 0:N][1]
    img = 0.5 + (noise(8) - 0.5) * 0.25 + np.zeros((N, N))
    rowh = N / 8
    row = (ys // rowh).astype(int)
    colw = N / 8
    xo = np.where(row % 2 == 1, colw / 2, 0)
    fx = ((xs + xo) % colw) / colw - 0.5                  # scalloped clay tiles
    fy = (ys % rowh) / rowh
    edge = fy > 0.72 + 0.18 * np.cos(fx * np.pi) ** 2
    img += (0.5 - fy) * 0.25
    img[edge] = 0.15
    cell_tint(img, row, ((xs + xo) // colw).astype(int), 0.1, 15)
    return finish(img, 0.24, 0.8)


def floortile():
    img = 0.55 + (noise(10) - 0.5) * 0.15
    rows, cols = grid_lines(img, 8, 8, 2, 0.3)
    img += np.where((rows + cols) % 2 == 0, 0.12, -0.12)
    return finish(img, 0.28, 0.76)


GEN = [grass, asphalt, paving, brick, timber, plaster, concrete, leaves, water, roof, floortile]

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    tiles = []
    for i, (name, fn) in enumerate(zip(NAMES, GEN)):
        g = fn()
        rgb = (np.stack([g, g, g], axis=-1) * 255).astype(np.uint8)
        path = os.path.join(OUT, f"surf_{i + 1:02d}_{name}.png")
        Image.fromarray(rgb).save(path)
        tiles.append(rgb)
        print("wrote", path)
    sheet = np.concatenate([np.concatenate(tiles[:6], axis=1),
                            np.concatenate(tiles[6:] + [np.full((N, N, 3), 128, np.uint8)], axis=1)], axis=0)
    Image.fromarray(sheet).save(os.path.join(HERE, "previews", "surface_textures.png"))
