# Local character studies and mesh comparison renderer

The current four Pak Mat studies are **locally authored Blender geometry and original texture maps**. Scenario concept images inform the art direction; the actual meshes are not Scenario-generated. These are static design studies, without production rigs or game replacement. None of these scripts changes the game's assets.

## Local authoring flow

- `local_geometry.py`: `build_variant('A'|'B'|'C'|'D')` creates a fresh character scene and returns mesh objects, object-name material roles and design metadata.
- `local_materials.py`: `apply_materials(id, objects, roles)` assigns portable image-based Principled materials and returns JSON-safe material/UV metadata.
- `make_local_textures.py`: authors the original repeating leafy batik, green plaid, cotton weave, skin/clay grain, leather, songkok and iris pigment maps. It uses ordinary Python with Pillow and NumPy. It never edits, projects or samples the reference images.
- `build_local_variants.py`: combines geometry and materials, checks mesh data, normalizes standing height to 1.66 m, saves packed master blends and exports real textured GLBs with provenance and geometry statistics.
- `render_variants.py`: imports the exported files in fresh scenes and renders them under consistent studio lighting. The final images therefore test the delivered GLB materials, not only their Blender source.

The four material directions use different motif scales and finishes: A has soft matte skin and smooth cloth; B has larger graphic leaf sprigs and restrained surface noise; C has tactile clay, woven cloth and leather grain; D has quieter, smaller batik and fine cotton/skin detail. Geometry carries the accompanying proportion and sculpting changes.

Generate or regenerate textures from the project root, then build and render:

```powershell
python Tools/scenario_characters/make_local_textures.py --size 1024

& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' `
  --background --factory-startup --threads 4 `
  --python Tools/scenario_characters/build_local_variants.py -- `
  --out docs/character-concepts/round-03

& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' `
  --background --factory-startup --threads 4 `
  --python Tools/scenario_characters/render_variants.py -- `
  --manifest docs/character-concepts/round-03/variants.json `
  --out renders --samples 64
```

`--only A` on the builder allows a targeted rebuild and retains existing manifest entries for the other IDs. `--clay` makes diagnostic geometry renders possible before material review. Do not present diagnostic clay/smoke files as final variants.

Each `assets/<id>` folder contains `pak_mat_<id>_master.blend`, `pak_mat_<id>.glb` and `asset-manifest.json`. The root `variants.json` feeds the comparison renderer. Texture sources are under `textures/local_styles`, with a generator/version manifest and 64 PNG maps across A–D.

### Material and UV contract

`roles` maps each mesh object name to `skin`, `batik`, `sarong`, `hair`, `songkok`, `eye_white`, `iris`, `pupil`, `mouth`, `teeth`, `sandal`, `sole` or `nail`; common aliases and optional trim/lip/button/stitch roles are also accepted. Unknown roles fail explicitly. Each object is one semantic surface. The current character builder does not use an existing multi-material facial atlas.

Garment `UVMap` uses circumference U (front U=0.5, corrected back seam) and shared garment-height V, including sleeves/collars and wrap overlap panels. Materials preserve the original UVs and copy/tile `MaterialUV`. If another input lacks UVs, a cylindrical fallback is recorded in metadata for inspection. Iris disks declare `radial_iris_uv=True` and use centered disk UVs; otherwise irises receive a constant warm brown to avoid misplaced pigment.

Base color images are sRGB. ORM and OpenGL tangent-space normal maps are linear/Non-Color. ORM has unoccluded R=1, roughness in G and dielectric metallic B=0. No directional light, shadow, AO or corneal highlight is baked into any image. All image textures are packed before save/export; alpha is opaque. There are no Blender-only procedural/toon shader nodes or random-walk skin scattering. The standard PBR appearance depends on the importing viewer's lighting and color management.

Blender's glTF exporter does not multiply fractional Principled Sheen Weight into exported sheen color. To retain subtle cloth sheen, the module puts its intensity into Sheen Tint and sets Weight to one. The roundtrip was checked to preserve the supplied tint, rather than an unintended white/full-strength sheen. Final A uses `KHR_materials_sheen.sheenColorFactor=[0.015,0.015,0.015]`; C/D use 0.035/0.025. Face normal strength is deliberately limited to 0.10/0/0.15/0.08 for A/B/C/D so microtexture does not turn into facial ribs. The exported glTF also uses the supported specular and IOR extensions.

## Generic mesh renderer

`render_variants.py` can also import other actual GLB/glTF/FBX files in Blender 5.2, one fresh scene per variant. It does not generate character geometry or call Scenario. `_smoke/` contains diagnostic tests on real existing game geometry and early local studies, separate from final assets.

## Run

Create a manifest beside the downloaded asset folder. Paths in the manifest, and a relative `--out` path, resolve against the manifest's directory:

```json
[
  {
    "id": "A",
    "label": "Warm animated film",
    "path": "assets/A/model.glb",
    "front_rotation_degrees": 0
  },
  {
    "id": "B",
    "label": "Graphic comic",
    "path": "assets/B/model.glb",
    "front_rotation_degrees": 180
  }
]
```

These paths illustrate the schema; use files that actually exist. From the KampungRun project:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' `
  --background --factory-startup --threads 4 `
  --python Tools/scenario_characters/render_variants.py -- `
  --manifest docs/character-concepts/round-03/variants.json `
  --out renders --samples 64
```

GLB is preferred for self-contained standard PBR materials. For a quick first inspection, use `--samples 16 --resolution 512`. Optional `--engine cycles` uses CPU Cycles and denoising; default is Eevee. `--no-render` validates imports and saves packed scenes/statistics without rendering PNGs.

## Output and presentation

Each ID has `<id>_studio.blend`, `front.png`, `threequarter.png`, `profile.png`, `face.png`, and `stats.json`. `render-manifest.json` records the complete set, labels, source hashes, cameras and warnings. The saved scene opens on the three-quarter camera. All four cameras remain editable.

- Standing height is normalized to 1.66 metres; the lowest mesh point is at Z=0, with horizontal bounds centred at the origin.
- `front_rotation_degrees` rotates imported content around Blender +Z after import. Zero assumes the model faces Blender -Y; inspect the first renders to choose the correct yaw. It does not tilt or repose the mesh.
- Every full-body view and variant uses a shared orthographic scale calculated from all input bounds. Wide poses expand the window for the entire set, keeping relative size consistent and hands inside the frame. Adding variants can therefore alter the framing; rerun the complete comparison manifest for matching outputs.
- Face views use a fixed upper-body window, preserving differences in head proportion rather than enlarging every head to the same size.
- Lighting is a neutral studio with a warm key, cooler fill and soft rim. Backgrounds are opaque. AgX color management and render settings are identical across the comparison.
- Imported material nodes are preserved by default. Available external/embedded images are packed into the saved blend; missing images are listed in warnings. Linked glTF buffers and external textures must remain alongside their source file for the initial import.
- Imported armatures are displayed in their bind/rest pose at frame 1. The renderer does not create a common arm pose or alter body proportions beyond uniform scaling.

## Explicit import overrides

Two optional manifest properties are available when the input's conventions require them:

```json
"force_opaque": true,
"exclude_objects": ["Exact_LOD1_Object_Name"]
```

`force_opaque` disconnects Principled BSDF Alpha links and sets Alpha to 1. Use it only when image alpha is known to hold metadata instead of transparency; it is disabled by default and recorded in the stats. It does not translate other custom shaders. `exclude_objects` hides exact named objects before measuring/rendering; a missing name is an error. Otherwise all renderable imported meshes are included, and detected nonzero LOD names trigger a warning about possible overlap.

## Verification and limits

Tested with Blender 5.2.1 LTS. A low-resolution, four-sample GLB smoke pass imported the existing Pak Mat mesh with its colors intact, loaded/packed its embedded texture, normalized it to 1.65999997 m with floor 0, and produced all four PNGs plus the saved blend and stats. The GLB fixture contains real existing character geometry, exported locally only for the test.

The local-material technical smoke test used the actual locally sculpted A study: 23 mesh objects and 11 assigned materials exported and reimported successfully. Clothing retained all three image textures, linked base color/roughness/normal inputs, explicit `texCoord=1`, packed 1024px images and opaque alpha. `_smoke/material-roundtrip/material-check.json` records the result; `_smoke/check_material_roundtrip.py` reruns it against the early local clay source. This validates material transport, not acceptance of that early geometry.

The FBX path also imports and renders, but the existing game's FBX relies on a custom palette convention (alpha contains gloss metadata), and Blender's generic importer reconstructs that shader incorrectly. Its smoke results are diagnostic, not evidence of faithful FBX custom-shader translation. Standard PBR GLBs avoid this known fixture issue. Newly downloaded Scenario outputs still require visual inspection for orientation, texture/material fidelity, complete anatomy and source-generation defects.

Geometry statistics describe the imported renderable mesh, not game readiness. The renderer does not retopologize, rig, repair anatomical errors, invent hidden geometry, benchmark performance, or authorize replacing the game's current assets. Review warnings and actual PNGs before accepting a variant.
