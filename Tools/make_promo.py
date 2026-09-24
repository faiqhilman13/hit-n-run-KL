"""Cut the promo video from the frames Tests/PromoCapture.cs recorded.

  uv run --no-project --with numpy --with pillow --with imageio-ffmpeg python Tools/make_promo.py

Makes Tools/promo_out/kampung_run_promo_16x9.mp4 (1920x1080, X / LinkedIn / YouTube) and
kampung_run_promo_9x16.mp4 (1080x1920, Reels / TikTok / Shorts): title card, gameplay shots
with pop-in captions, landmark name stamps, end card with the itch.io link, and an original
synthesised kompang-beat soundtrack (no licensed music).
"""
import os
import math
import subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
import imageio_ffmpeg

HERE = os.path.dirname(os.path.abspath(__file__))
FRAMES = os.path.join(HERE, "promo_frames")
OUT = os.path.join(HERE, "promo_out")
FPS = 30
URL = "mrguts13.itch.io/kampung-run-kl"
FONT_TITLE = "C:/Windows/Fonts/impact.ttf"
FONT_CAP = "C:/Windows/Fonts/seguibl.ttf"

RED, TEAL, YELLOW, INK, WHITE = (216, 50, 42), (19, 144, 143), (255, 214, 58), (31, 26, 26), (255, 255, 255)

# (shot folder, first frame, last frame (None = all), caption, landmark stamp)
SHOTS = [
    ("01_cruise", 0, None, "Drive a Myvi through KL (badly)", None),
    ("02_drift", 0, None, "Drift it. Smoke it. Skid it.", None),
    ("03_lineup", 0, None, "Myvi  -  Saga  -  Kancil  -  Alphard  -  Hilux  -  Kapcai", None),
    ("04_door", 0, None, "Doors swing. Drivers sit. Everything bounces.", None),
    ("05_bop", 0, None, "Bop the pakciks - they always get back up", None),
    ("06_kapcai", 0, None, "Weave through traffic on a kapcai", None),
    ("07_batu", 0, None, None, "BATU CAVES"),
    ("08_merdeka118", 0, None, None, "MERDEKA 118"),
    ("09_klcc", 0, 54, None, "KLCC"),
    ("10_theanhou", 0, None, None, "THEAN HOU"),
    ("11_jamek", 0, None, None, "MASJID JAMEK"),
    ("12_istana", 0, None, None, "ISTANA NEGARA"),
    ("13_night", 0, None, "Day to night", None),
]
TITLE_S, END_S = 2.6, 4.2


def font(path, size):
    return ImageFont.truetype(path, size)


def load_shot(name, a, b):
    d = os.path.join(FRAMES, name)
    fs = sorted(f for f in os.listdir(d) if f.endswith(".jpg"))
    fs = fs[a:b if b is not None else len(fs)]
    return [os.path.join(d, f) for f in fs]


def ease_back(t):
    """Overshooting ease-out for pop-in animations."""
    t = max(0.0, min(1.0, t))
    c = 1.70158
    return 1 + (c + 1) * (t - 1) ** 3 + c * (t - 1) ** 2


def outlined(draw, xy, text, f, fill, stroke=8, stroke_fill=INK, anchor="mm"):
    draw.text(xy, text, font=f, fill=fill, stroke_width=stroke, stroke_fill=stroke_fill, anchor=anchor)


def pill(img, cx, cy, text, f, scale=1.0, fg=INK, bg=YELLOW, pad=(36, 18)):
    """Caption: a chunky yellow pill with a black border and a drop shadow (H&R-comic style)."""
    tmp = ImageDraw.Draw(img)
    l, t, r, b = tmp.textbbox((0, 0), text, font=f, anchor="lt")
    w, h = (r - l) + pad[0] * 2, (b - t) + pad[1] * 2
    layer = Image.new("RGBA", (w + 24, h + 24), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.rounded_rectangle((12, 16, w + 12, h + 16), radius=h // 2, fill=(0, 0, 0, 120))
    d.rounded_rectangle((4, 4, w + 4, h + 4), radius=h // 2, fill=bg, outline=INK, width=6)
    d.text((4 + pad[0] - l, 4 + pad[1] - t), text, font=f, fill=fg)
    if scale != 1.0:
        layer = layer.resize((max(1, int(layer.width * scale)), max(1, int(layer.height * scale))), Image.LANCZOS)
    img.alpha_composite(layer, (int(cx - layer.width / 2), int(cy - layer.height / 2)))


def stamp(img, x, y, text, f, t):
    """Landmark name: a tilted red stamp that thumps in."""
    k = ease_back(t / 0.25)
    layer = Image.new("RGBA", (1100, 260), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.text((40, 60), text, font=f, fill=WHITE, stroke_width=10, stroke_fill=INK)
    d.text((40, 60), text, font=f, fill=RED if len(text) % 2 else TEAL, stroke_width=0)
    d.text((44, 36), "KUALA LUMPUR", font=font(FONT_CAP, 34), fill=WHITE, stroke_width=5, stroke_fill=INK)
    layer = layer.rotate(4, resample=Image.BICUBIC, expand=True)
    s = max(0.01, 0.6 + 0.4 * k)
    layer = layer.resize((int(layer.width * s), int(layer.height * s)), Image.LANCZOS)
    a = min(1.0, t / 0.12)
    if a < 1:
        layer.putalpha(layer.getchannel("A").point(lambda v: int(v * a)))
    img.alpha_composite(layer, (x, y))


def logo(img, cx, cy, size, t=1.0, sub=None):
    """KAMPUNG RUN: KL wordmark with a bouncy scale-in."""
    s = ease_back(t / 0.45) if t < 1 else 1.0
    f = font(FONT_TITLE, int(size * max(0.05, s)))
    d = ImageDraw.Draw(img)
    a, b = "KAMPUNG RUN", ": KL"
    wa = d.textlength(a, font=f)
    wb = d.textlength(b, font=f)
    x0 = cx - (wa + wb) / 2
    d.text((x0 + 8, cy + 10), a + b, font=f, fill=INK, anchor="lm")                         # drop shadow
    d.text((x0, cy), a, font=f, fill=RED, stroke_width=max(2, int(size * 0.07)), stroke_fill=INK, anchor="lm")
    d.text((x0 + wa, cy), b, font=f, fill=TEAL, stroke_width=max(2, int(size * 0.07)), stroke_fill=INK, anchor="lm")
    if sub and t > 0.35:
        fs = font(FONT_CAP, int(size * 0.3))
        outlined(d, (cx, cy + size * 0.85), sub, fs, WHITE, stroke=max(3, int(size * 0.05)))


def backdrop(frame, W, H):
    """A cover-scaled, blurred, slightly darkened copy of a frame (cards + the vertical cut)."""
    small = frame.resize((max(1, W // 8), max(1, H // 8)), Image.BILINEAR)
    fw, fh = frame.size
    sc = max(W / fw, H / fh)
    small = frame.resize((max(1, int(fw * sc / 8)), max(1, int(fh * sc / 8))), Image.BILINEAR)
    small = small.filter(ImageFilter.GaussianBlur(3))
    big = small.resize((small.width * 8, small.height * 8), Image.BILINEAR)
    l, t = (big.width - W) // 2, (big.height - H) // 2
    big = big.crop((l, t, l + W, t + H))
    return Image.blend(big, Image.new("RGB", (W, H), (20, 20, 40)), 0.35)


# ============================================================================ soundtrack
def soundtrack(seconds, sr=44100):
    """Original upbeat kompang-flavoured groove, 118 bpm: frame-drum thumps and slaps, shaker,
    a bouncy bass and a pentatonic pluck melody. Deterministic, royalty-free by construction."""
    rng = np.random.default_rng(118)
    n = int(seconds * sr)
    out = np.zeros(n)
    bpm = 118.0
    beat = 60.0 / bpm
    t_all = np.arange(n) / sr

    def add(start, sig, gain=1.0):
        i = int(start * sr)
        if i >= n:
            return
        j = min(n, i + len(sig))
        out[i:j] += sig[:j - i] * gain

    def env(length, attack=0.002, decay=0.2):
        t = np.arange(int(length * sr)) / sr
        return np.minimum(1, t / attack) * np.exp(-t / decay)

    def thump(f0=110):
        L = 0.35
        t = np.arange(int(L * sr)) / sr
        f = f0 * np.exp(-t * 9) + 48
        return np.sin(2 * np.pi * np.cumsum(f) / sr) * env(L, 0.001, 0.12)

    def slap(bright=1.0):
        L = 0.12
        w = rng.standard_normal(int(L * sr))
        w = np.convolve(w, np.ones(3) / 3, mode="same") * bright + np.diff(np.concatenate([[0], w])) * (1 - bright) * 0.6
        t = np.arange(len(w)) / sr
        ring = np.sin(2 * np.pi * 380 * t) * 0.5
        return (w * 0.6 + ring) * env(L, 0.0005, 0.035)

    def shaker():
        L = 0.06
        w = np.diff(np.concatenate([[0], rng.standard_normal(int(L * sr))]))
        return w * env(L, 0.004, 0.02)

    def pluck(freq, L=0.35, bright=0.5):
        t = np.arange(int(L * sr)) / sr
        s = np.sin(2 * np.pi * freq * t) + bright * 0.5 * np.sin(4 * np.pi * freq * t) + bright * 0.25 * np.sin(6 * np.pi * freq * t)
        return s * env(L, 0.003, L * 0.35)

    def bass(freq, L):
        t = np.arange(int(L * sr)) / sr
        s = np.sign(np.sin(2 * np.pi * freq * t)) * 0.35 + np.sin(2 * np.pi * freq * t) * 0.65
        return s * env(L, 0.005, L * 0.7)

    notes = {"C": 261.63, "D": 293.66, "E": 329.63, "G": 392.0, "A": 440.0}
    penta = [notes[k] for k in "CDEGA"] + [notes[k] * 2 for k in "CDEGA"]
    chords = [(130.81, [0, 2, 4]), (98.0, [3, 5, 7]), (110.0, [4, 6, 8]), (87.31, [0, 3, 5])]   # C, G, Am, F roots
    motif = [(0, 7), (0.5, 6), (1.0, 4), (1.5, 5), (2.0, 7), (2.75, 8), (3.25, 6)]
    bars = int(seconds / (beat * 4)) + 1
    for bar in range(bars):
        t0 = bar * beat * 4
        root, _ = chords[bar % 4]
        intro = bar < 1
        # frame drums: thump on 1 & 3 (+ pickup), slaps on 2 & 4, kompang-style syncopated taps
        for b in (0, 2):
            add(t0 + b * beat, thump(), 0.9)
        add(t0 + 3.5 * beat, thump(90), 0.5)
        for b in (1, 3):
            add(t0 + b * beat, slap(), 0.55)
        for off in (0.75, 1.75, 2.5, 3.25, 3.75):
            add(t0 + off * beat, slap(0.3), 0.25)
        for k in range(8):
            add(t0 + k * beat / 2, shaker(), 0.18 if k % 2 else 0.1)
        if intro:
            continue
        # bass: root on the beat, octave bounce on the offbeat
        for k in range(4):
            add(t0 + k * beat, bass(root, beat * 0.45), 0.35)
            add(t0 + (k + 0.5) * beat, bass(root * 2, beat * 0.25), 0.18)
        # melody: the motif, nudged up or down each bar so it doesn't loop dead-straight
        shift = [0, 1, -1, 2][bar % 4]
        for (b, deg) in motif:
            if rng.random() < 0.15:
                continue
            idx = max(0, min(len(penta) - 1, deg + shift - 3))
            add(t0 + b * beat, pluck(penta[idx], 0.32, 0.6), 0.22)
    # end: a final hit and a fade
    end = seconds - 1.8
    add(end, thump(80), 1.0)
    add(end, slap(), 0.8)
    fade = np.clip((seconds - t_all) / 1.6, 0, 1)
    fade *= np.clip(t_all / 0.15, 0, 1)
    out *= fade
    out = np.tanh(out * 1.3) / np.tanh(1.3)
    out /= max(1e-9, np.abs(out).max()) / 0.89
    return (np.stack([out, out], axis=1) * 32767).astype(np.int16)


def write_wav(path, pcm, sr=44100):
    import wave
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.tobytes())


# ============================================================================ frames
def timeline():
    """[(kind, payload, t_in_segment, seg_len)] per output frame."""
    frames = []
    n = int(TITLE_S * FPS)
    for i in range(n):
        frames.append(("title", None, i / FPS, TITLE_S))
    for name, a, b, cap, stp in SHOTS:
        files = load_shot(name, a, b)
        L = len(files) / FPS
        for i, f in enumerate(files):
            frames.append(("shot", (f, cap, stp), i / FPS, L))
    n = int(END_S * FPS)
    for i in range(n):
        frames.append(("end", None, i / FPS, END_S))
    return frames


def render_16x9(kind, payload, t, L, cache):
    W, H = 1920, 1080
    if kind == "title":
        bg = cache.setdefault("title_bg", backdrop(Image.open(load_shot("03_lineup", 60, 61)[0]).convert("RGB"), W, H))
        img = bg.convert("RGBA")
        logo(img, W / 2, H / 2 - 60, 210, t, "A Hit & Run-style game... set in KL")
        if t > 1.1:
            pill(img, W / 2, H - 170, "I grew up loving it, so I made my own", font(FONT_CAP, 44), ease_back((t - 1.1) / 0.3))
        return img.convert("RGB")
    if kind == "end":
        bg = cache.setdefault("end_bg", backdrop(Image.open(load_shot("09_klcc", 30, 31)[0]).convert("RGB"), W, H))
        img = bg.convert("RGBA")
        logo(img, W / 2, H / 2 - 170, 190, t)
        d = ImageDraw.Draw(img)
        if t > 0.4:
            outlined(d, (W / 2, H / 2 + 20), "FREE  -  PLAY IN YOUR BROWSER", font(FONT_TITLE, 86), YELLOW, stroke=7)
        if t > 0.8:
            pill(img, W / 2, H / 2 + 175, URL, font(FONT_CAP, 58), ease_back((t - 0.8) / 0.3), fg=WHITE, bg=RED)
        if t > 1.3:
            outlined(d, (W / 2, H / 2 + 300), "desktop & phone", font(FONT_CAP, 40), WHITE, stroke=5)
        return img.convert("RGB")
    f, cap, stp = payload
    img = Image.open(f).convert("RGBA")
    if cap:
        k = ease_back(t / 0.3) if t < 0.3 else 1.0
        out_t = L - t
        if out_t < 0.2:
            k *= out_t / 0.2
        if k > 0.02:
            pill(img, W / 2, H - 120, cap, font(FONT_CAP, 52), k)
    if stp:
        stamp(img, 70, 60, stp, font(FONT_TITLE, 120), t)
    # small corner bug so clips stay credited when reshared
    d = ImageDraw.Draw(img)
    d.text((W - 40, 44), "KAMPUNG RUN: KL", font=font(FONT_TITLE, 44), fill=WHITE, stroke_width=4, stroke_fill=INK, anchor="rm")
    return img.convert("RGB")


def render_9x16(kind, payload, t, L, cache):
    W, H = 1080, 1920
    if kind in ("title", "end"):
        src = cache.setdefault(kind + "_v_src", Image.open(load_shot("03_lineup" if kind == "title" else "09_klcc",
                                                                         60 if kind == "title" else 30, None)[0]).convert("RGB"))
        img = cache.setdefault(kind + "_v_bg", backdrop(src, W, H)).convert("RGBA")
        if kind == "title":
            logo(img, W / 2, H / 2 - 160, 130, t, "A Hit & Run-style game")
            d = ImageDraw.Draw(img)
            if t > 0.35:
                outlined(d, (W / 2, H / 2 + 60), "...set in KL", font(FONT_CAP, 64), YELLOW, stroke=6)
            if t > 1.1:
                pill(img, W / 2, H / 2 + 300, "I grew up loving it, so I made my own", font(FONT_CAP, 38),
                     ease_back((t - 1.1) / 0.3))
        else:
            logo(img, W / 2, H / 2 - 330, 130, t)
            d = ImageDraw.Draw(img)
            if t > 0.4:
                outlined(d, (W / 2, H / 2 - 120), "FREE", font(FONT_TITLE, 150), YELLOW, stroke=9)
                outlined(d, (W / 2, H / 2 + 20), "PLAY IN YOUR BROWSER", font(FONT_TITLE, 78), WHITE, stroke=6)
            if t > 0.8:
                pill(img, W / 2, H / 2 + 200, URL, font(FONT_CAP, 42), ease_back((t - 0.8) / 0.3), fg=WHITE, bg=RED)
            if t > 1.3:
                outlined(d, (W / 2, H / 2 + 330), "link in bio", font(FONT_CAP, 48), WHITE, stroke=5)
        return img.convert("RGB")
    f, cap, stp = payload
    frame = Image.open(f).convert("RGB")
    img = backdrop(frame, W, H).convert("RGBA")
    # the gameplay, big: crop the middle of the 16:9 frame to 4:5 and fill the width
    fw, fh = frame.size
    cw = int(fh * 0.8)
    crop = frame.crop(((fw - cw) // 2, 0, (fw - cw) // 2 + cw, fh)).resize((W, int(W / 0.8)), Image.LANCZOS)
    y0 = (H - crop.height) // 2 + 40
    img.alpha_composite(crop.convert("RGBA"), (0, y0))
    d = ImageDraw.Draw(img)
    d.rectangle((0, y0 - 6, W, y0), fill=INK)
    d.rectangle((0, y0 + crop.height, W, y0 + crop.height + 6), fill=INK)
    logo(img, W / 2, y0 / 2 + 10, 84)
    if cap:
        k = ease_back(t / 0.3) if t < 0.3 else 1.0
        if L - t < 0.2:
            k *= (L - t) / 0.2
        if k > 0.02:
            cf = font(FONT_CAP, 40 if len(cap) < 36 else 30)
            pill(img, W / 2, y0 + crop.height + (H - y0 - crop.height) / 2, cap, cf, k)
    if stp:
        stamp(img, 30, y0 + 30, stp, font(FONT_TITLE, 96), t)
    return img.convert("RGB")


def encode(path, W, H, render, frames, wav):
    ff = imageio_ffmpeg.get_ffmpeg_exe()
    cmd = [ff, "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
           "-i", wav, "-c:v", "libx264", "-preset", "medium", "-crf", "19", "-pix_fmt", "yuv420p", "-movflags", "+faststart",
           "-c:a", "aac", "-b:a", "192k", "-shortest", path]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    cache = {}
    for i, (kind, payload, t, L) in enumerate(frames):
        img = render(kind, payload, t, L, cache)
        p.stdin.write(img.tobytes())
        if i % 150 == 0:
            print(f"  {os.path.basename(path)}: frame {i}/{len(frames)}", flush=True)
    p.stdin.close()
    p.wait()
    print("wrote", path)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    frames = timeline()
    seconds = len(frames) / FPS
    print(f"{len(frames)} frames = {seconds:.1f} s")
    wav = os.path.join(OUT, "soundtrack.wav")
    write_wav(wav, soundtrack(seconds + 0.5))
    encode(os.path.join(OUT, "kampung_run_promo_16x9.mp4"), 1920, 1080, render_16x9, frames, wav)
    encode(os.path.join(OUT, "kampung_run_promo_9x16.mp4"), 1080, 1920, render_9x16, frames, wav)
