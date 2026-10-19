# MPFB character pipeline (stylized-adult cast)

The new cast is built in Blender 5.2 on MPFB (the MakeHuman extension), whose base mesh, skins, eyes, brows and rig
are CC0. Everything is scripted and rebuildable. Pak Mat (`chr_pakmat`) is the first character in the game this way.

| Script | What it does |
| --- | --- |
| `specs.py` | One entry per character: MPFB phenotype sliders, shape targets, skin tone, hair, outfit. `STYLIZE` is the shared look (head about 12% bigger, eyes about 20% bigger, softer jaw, flat nipples). |
| `build_body.py` | Body only, with renders: `out/<id>/body_*.png`. |
| `looks.py` | Painted skin over MPFB's skin map, hair and eye tints, face landmarks from the midline profile, and moustache shells cut from the face. |
| `garments.py` | Clothes cut from the body and dropped onto it with Blender's cloth solver: shirt (V neck, lofted collar, buttons), wrap skirt sized from real cross-sections, songkok fitted to the head, sandals from the footprint, cropped scalp hair. |
| `make_batik.py` | Seamless 2048 px batik prints (hibiscus, leaves, tendrils, dotted fill, wax crackle) with normals, in six colourways under `textures/`. |
| `build_character.py` | Body, looks and clothes put together into `out/<id>/<id>_master.blend`, plus renders (`dressed_*.png`). |
| `export_unity.py` | The game asset: see below. |

```powershell
$B = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
python Tools/mpfb_characters/make_batik.py
& $B --background --python-exit-code 1 --python Tools/mpfb_characters/build_character.py -- chr_pakmat
& $B --background --python-exit-code 1 --python Tools/mpfb_characters/export_unity.py -- chr_pakmat --tex 4096
```

Copy `out/<id>/export/<id>.fbx` over `Assets/Models/KL/<id>.fbx`, keeping its `.meta`. Copy `<id>_mat.png`,
`<id>_mat_mask.png`, `<id>_brows.png` and `<id>_lashes.png` into `Assets/Models/KL/<id>_tex/`. Copy the textures
first, so the importer sees the folder when the FBX arrives.

## Export steps (`export_unity.py`)

1. **Bake the face.** MPFB's modelling targets are baked into the base mesh. Then the four expression shape keys
   the game drives (`Grin`, `Alarm`, `Determined`, `Blink`) are mixed from MPFB's FACS units. This happens before
   anything deletes vertices, because MPFB target files index the full base mesh.
2. **Remove what's never seen.** MPFB's masked helper geometry goes. So does skin under the clothes: anything a
   ray along the normal reaches cloth from within 9 cm, or inside a wrap skirt's tube (all four horizontal rays
   hit it). The result is eroded by two rings so nothing opens up at hems.
3. **Apply garment modifiers at game density.** Subdivision is capped at 1 for the songkok and moustache and 0
   elsewhere. The sarong's leg weights are blended left/right, with some moved to the pelvis, so it swings as a
   tube instead of splitting into trouser legs. The eyes' transparent cornea is dropped.
4. **T-pose and rename.** The arms swing level and that becomes the rest pose, carrying the shape keys along.
   The game's humanoid clips assume a T-pose. Bones are renamed to the importer's humanoid names (`pelvis` →
   `Hips`, `ball_l` → `LeftToes`, and so on); the fingers keep MPFB names and the importer maps them.
5. **Join and bake.** Everything opaque is joined into `<id>_LOD0`, unwrapped once, with face islands scaled
   ×1.9. Cycles bakes colour, roughness, AO and a recolour mask.
   - `<id>_mat.png` = colour × soft AO in RGB. Gloss goes in alpha, because CharacterSoft reads gloss from
     alpha (≤ 0.5 is matte) and has no normal or AO maps.
   - `<id>_mat_mask.png`: R = shirt, G = sarong, B = songkok. `AtlasRecolor` uses it for costumes.
6. **Cards.** The brows and lashes become `<id>_cards_LOD0`, alpha-tested, casting no shadow.
7. **LOD1.** About 28% of the triangles, without expressions.

Pak Mat: LOD0 62.8k triangles, LOD1 17.6k, cards 0.6k, one 4096 atlas.

## Unity side

- `ModelImportPipeline`: a `chr_<id>_tex` folder beside the FBX means "own textures". Each material gets
  `<tex>/<material>.png` as `_BaseMap` and `<material>_mask.png` as `_MaskMap`. Brow and lash materials get alpha
  clip and no culling. The humanoid skeleton is rebuilt from the file and the fingers are mapped.
- `CharacterSoft`: has an `_ALPHATEST_ON` toggle and `_Cull` for the cards, plus a hidden `_MaskMap`. The CBUFFER
  is unchanged, so the shared LatInk passes still match.
- `AtlasRecolor` (called by `ModelFactory.Recolor`): costume swaps on own-atlas characters shift the masked region
  in HSV, keeping the print's pattern.
- `CharacterArtValidation.Run(ids, output)` can be called from a live Editor. Own-atlas characters skip the
  own-takes and palette checks and test the mask recolour instead.
- `Tests/PakMatCapture` (Explicit) takes the same five in-game shots `Before`/`After` into
  `Tools/promo_frames/x_pakmat/`.
