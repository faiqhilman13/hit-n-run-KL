"""Side-by-side sheet of the PakMatCapture Before/After shots: one row per shot, before left, after right."""
import os, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "promo_frames", "x_pakmat")
SHOTS = [("1_portrait", "Portrait"), ("2_fullbody", "Full body"), ("3_gamecam", "Game camera"),
         ("4_walking", "Walking"), ("5_driving", "At the wheel")]
W, H = 960, 600


def font(size, bold=False):
    for n in (("arialbd.ttf" if bold else "arial.ttf"), "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            pass
    return ImageFont.load_default()


def main(out=os.path.join(ROOT, "before_after.png"), shots=SHOTS):
    pad, head = 16, 70
    sheet = Image.new("RGB", (W * 2 + pad * 3, head + len(shots) * (H + pad + 34) + pad), (236, 234, 228))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 18), "PAK MAT IN GAME  —  before (round 02)   vs   after (MPFB stylized adult)", fill=(25, 25, 25), font=font(30, True))
    y = head
    for key, label in shots:
        for i, tag in enumerate(("before", "after")):
            p = os.path.join(ROOT, tag, key + ".png")
            if os.path.exists(p):
                sheet.paste(Image.open(p).convert("RGB").resize((W, H)), (pad + i * (W + pad), y + 30))
        d.text((pad, y + 4), f"{label}:  before", fill=(70, 70, 70), font=font(20))
        d.text((pad * 2 + W, y + 4), f"{label}:  after", fill=(70, 70, 70), font=font(20))
        y += H + pad + 34
    sheet.save(out)
    print(out)


if __name__ == "__main__":
    main()
