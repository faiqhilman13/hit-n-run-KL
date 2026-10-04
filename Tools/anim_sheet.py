"""Review strips for the motion library (Tools/anim_review, written by AnimationStudio.ReviewBatch).

    uv run --no-project --with pillow python Tools/anim_sheet.py [clip ...] [--views side,front] [--video]

One image per clip: a row per character and view, the clip's frames left to right. With --video, each
clip's 30 fps frames (rendered with -animVideo 1) become an MP4 per character (needs imageio-ffmpeg).
"""
import os
import sys
from collections import defaultdict

from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "anim_review")


def sheet(clip, views):
    folder = os.path.join(ROOT, clip)
    rows = defaultdict(list)
    for f in sorted(os.listdir(folder)):
        if not f.endswith(".png"):
            continue
        cid, view, _ = f[:-4].rsplit("_", 2)
        if view in views:
            rows[(cid, view)].append(os.path.join(folder, f))
    if not rows:
        return None
    keys = sorted(rows, key=lambda k: (k[0], views.index(k[1])))
    w, h = Image.open(rows[keys[0]][0]).size
    cols = max(len(v) for v in rows.values())
    scale = min(1.0, 2400 / (w * cols))
    tw, th = int(w * scale), int(h * scale)
    out = Image.new("RGB", (tw * cols, th * len(keys)), (30, 30, 30))
    draw = ImageDraw.Draw(out)
    for r, k in enumerate(keys):
        for c, path in enumerate(rows[k]):
            out.paste(Image.open(path).convert("RGB").resize((tw, th)), (c * tw, r * th))
        draw.text((4, r * th + 4), f"{k[0]} {k[1]}", fill=(20, 20, 20))
    path = os.path.join(ROOT, f"_{clip}.jpg")
    out.save(path, quality=88)
    return path


def video(clip):
    import imageio.v2 as imageio
    folder = os.path.join(ROOT, clip)
    groups = defaultdict(list)
    for f in sorted(os.listdir(folder)):
        if f.endswith(".png"):
            cid, view, _ = f[:-4].rsplit("_", 2)
            groups[(cid, view)].append(os.path.join(folder, f))
    made = []
    for (cid, view), files in groups.items():
        path = os.path.join(ROOT, f"_{clip}_{cid}_{view}.mp4")
        with imageio.get_writer(path, fps=30, codec="libx264", quality=8, macro_block_size=8) as w:
            for f in files:
                w.append_data(imageio.imread(f))
        made.append(path)
    return made


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    views = ["side", "front"]
    for a in sys.argv[1:]:
        if a.startswith("--views="):
            views = a.split("=", 1)[1].split(",")
    clips = args or sorted(d for d in os.listdir(ROOT) if os.path.isdir(os.path.join(ROOT, d)))
    for c in clips:
        if "--video" in sys.argv:
            for p in video(c):
                print(p)
        else:
            print(sheet(c, views))
