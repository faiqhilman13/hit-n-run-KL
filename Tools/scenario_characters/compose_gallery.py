"""Lay out actual Blender renders for comparison; does not alter their contents."""
from pathlib import Path
import argparse
import html
import json

from PIL import Image, ImageDraw, ImageFont

LABELS = {'A': ('Warm animated film', 'Rounded proportions / expressive face'),
          'B': ('Graphic comic', 'Angular forms / bold pattern'),
          'C': ('Handmade miniature', 'Compact silhouette / tactile finish'),
          'D': ('Grounded stylized', 'Adult proportions / restrained features')}


def font(size, bold=False):
    base = Path('C:/Windows/Fonts')
    choices = ['segoeuib.ttf', 'arialbd.ttf'] if bold else ['segoeui.ttf', 'arial.ttf']
    for name in choices:
        if (base/name).exists():
            return ImageFont.truetype(str(base/name), size)
    return ImageFont.load_default(size=size)


def compose(root, view, output):
    face = view == 'face'
    width, gap, pad = 420, 20, 32
    height = 420 if face else 560
    canvas = Image.new('RGB', (4*width+3*gap+2*pad, height+210), '#eeece7')
    draw = ImageDraw.Draw(canvas)
    draw.text((pad, 20), 'PAK MAT / FOUR LOCAL BLENDER DESIGNS', font=font(29, True), fill='#242624')
    draw.text((pad, 65), f'{"Face details" if face else "Matched three-quarter views"} · Actual exported meshes · Same scale, camera and lighting', font=font(19), fill='#646762')
    for n, (identifier, (label, desc)) in enumerate(LABELS.items()):
        x, y = pad+n*(width+gap), 112
        image = Image.open(root/'renders'/identifier/f'{view}.png').convert('RGB')
        image = image.resize((width, height), Image.Resampling.LANCZOS)
        canvas.paste(image, (x, y))
        draw.text((x, y+height+12), f'{identifier}  {label}', font=font(23, True), fill='#242624')
        draw.text((x, y+height+46), desc, font=font(16), fill='#646762')
    canvas.save(root/output)


def gallery(root):
    cards = []
    for identifier, (label, desc) in LABELS.items():
        manifest = json.loads((root/'assets'/identifier/'asset-manifest.json').read_text(encoding='utf-8'))
        stats = f"{manifest['total_triangles']:,} triangles · static study · 1.66 m"
        cards.append(f'''<article><header><span>{identifier}</span><div><h2>{html.escape(label)}</h2><p>{html.escape(desc)}</p></div></header>
<a class="zoom" href="renders/{identifier}/threequarter.png"><img src="renders/{identifier}/threequarter.png" alt="Pak Mat {label}, rendered from the actual local Blender mesh"></a>
<p class="stats">{stats}</p><footer><a href="assets/{identifier}/pak_mat_{identifier}.glb" download>3D mesh (.glb)</a><a href="assets/{identifier}/pak_mat_{identifier}_master.blend" download>Blender source</a><a href="renders/{identifier}/{identifier}_studio.blend" download>Lit scene</a></footer></article>''')
    document = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Pak Mat — four Blender designs</title><style>
*{box-sizing:border-box}body{margin:0;background:#eeece7;color:#282d29;font:16px/1.5 system-ui,sans-serif;padding:36px clamp(20px,4vw,72px)}.intro{max-width:800px}.eyebrow{font-size:12px;font-weight:750;letter-spacing:.16em}h1{font-size:clamp(30px,3vw,48px);letter-spacing:-.045em;margin:8px 0}p{color:#626760;margin:4px 0 20px}nav{display:flex;gap:8px;margin:28px 0 22px;flex-wrap:wrap}button{font:inherit;border:1px solid #bdc1b8;background:transparent;padding:10px 18px;border-radius:5px;cursor:pointer}button[aria-pressed=true]{background:#263f34;color:white;border-color:#263f34}.grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:20px}article{min-width:0;background:#f7f6f2;border:1px solid #d3d5cc}header{display:flex;gap:13px;align-items:center;padding:20px 16px;min-height:120px}header span{font-size:32px;font-weight:750;color:#365544}h2{font-size:19px;line-height:1.2;margin:0 0 6px}header p{font-size:13px;margin:0}img{display:block;width:100%;height:auto}footer{display:flex;flex-wrap:wrap;gap:10px 16px;padding:0 16px 20px}a{color:#315b47;font-size:13px;text-underline-offset:3px}.stats{font-size:12px;padding:12px 16px;margin:0 0 4px}.note{font-size:14px;max-width:1000px;margin-top:24px}.face img{aspect-ratio:1;object-fit:contain}.links{display:flex;gap:24px;margin:28px 0}@media(max-width:1100px){.grid{grid-template-columns:repeat(2,minmax(0,1fr))}}@media(max-width:560px){.grid{grid-template-columns:1fr}}
</style><main><div class="intro"><div class="eyebrow">KAMPUNG RUN KL / CHARACTER LOOK DEVELOPMENT</div><h1>One Pak Mat. Four directions.</h1><p>Compare the actual local Blender models. Clothing identity stays consistent while proportions, facial forms and surface finishes change.</p></div><nav aria-label="Render view">'''
    for view, label in [('threequarter','Three-quarter'),('front','Front'),('profile','Profile'),('face','Face detail')]:
        document += f'<button data-view="{view}" aria-pressed="{str(view=="threequarter").lower()}">{label}</button>'
    document += '</nav><section class="grid">'+''.join(cards)+'</section>'
    document += '''<p class="note">These are static design studies: real meshes and original texture patterns, with editable Blender sources. They are not rigged or substituted into the game. Scenario produced three visual references before its credit limit; the meshes and every view shown here were built and rendered locally. B uses shaped planes and bold PBR materials; a production cel shader would be a later runtime decision.</p><div class="links"><a href="comparison.png">Full-body comparison PNG</a><a href="face-comparison.png">Face comparison PNG</a><a href="README.md">Build and provenance notes</a></div></main><script>
document.querySelectorAll('button[data-view]').forEach(button=>button.addEventListener('click',()=>{const view=button.dataset.view;document.querySelectorAll('button[data-view]').forEach(b=>b.setAttribute('aria-pressed',String(b===button)));document.querySelector('.grid').classList.toggle('face',view==='face');document.querySelectorAll('article').forEach((card,i)=>{const url=`renders/${'ABCD'[i]}/${view}.png`;card.querySelector('img').src=url;card.querySelector('.zoom').href=url;});}));
</script></html>'''
    (root/'index.html').write_text(document, encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    root = Path(parser.parse_args().folder).resolve()
    compose(root, 'threequarter', 'comparison.png')
    compose(root, 'face', 'face-comparison.png')
    gallery(root)
    print(root/'index.html')


if __name__ == '__main__':
    main()
