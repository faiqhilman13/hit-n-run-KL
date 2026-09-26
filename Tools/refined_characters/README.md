# Refined KL character sources

This is the editable Blender source and evidence package for the replacement human cast. The four approved imagegen sheets are retained under `docs/character-concepts/round-01`; their cropped reference views and measurements are under each family asset's `ref` folder. NPC designs extend the family direction with the game's existing costumes and roles.

The sources use metres, Blender +Z up / -Y forward, the existing 23-bone Humanoid contract, 15 authored action takes and four expression keys. Each delivered FBX contains two skinned LODs. The custom character palette and Unity CharacterSoft shader are separate from the city palette and LatInk shader.

| Asset | Character |
| --- | --- |
| chr_pakmat | Pak Mat |
| chr_maksom | Mak Som |
| chr_along | Along |
| chr_adik | Adik |
| chr_aiman | Aiman |
| chr_mei | Mei |
| chr_ravi | Ravi |
| chr_townman | Town man |
| chr_townaunty | Town aunty |
| chr_pakcik | Pakcik |
| chr_kid | Neighbourhood kid |
| chr_polis | Police officer |
| chr_datukmega | Datuk Mega |

Each `<asset>/` folder contains `<asset>_master.blend`, a packed palette, an `exports` folder with FBX and manifests, and a repeatable `build/06_rig.py` entry point. The master separates references, editable source, delivery meshes, rig and sockets. Covered anatomy is retained in the hidden HIGH source and omitted from the game mesh to avoid cloth intersections.

From the project root, regenerate staged sources with:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --threads 4 --python-exit-code 1 --python Tools/refined_characters/build_characters.py --
```

Append one or more asset IDs to build a subset. By default this stages files here; `--publish` also copies exports to the existing `Assets/Models/KL/chr_*.fbx` paths. Preserve the Unity `.meta` files and GUIDs. `Tools/blender/kl/kl_build.py` routes future character regeneration through this refined source; environment and vehicle builders remain separate.

Technical verification, including reimporting each FBX into Blender:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --threads 4 --python-exit-code 1 --python Tools/refined_characters/review_characters.py -- --root Tools/refined_characters --budget 80000 --no-render --roundtrip --out Tools/refined_characters/review/final-technical
```

The 80,000-triangle LOD0 ceiling is a project choice for this refined pass, not an original design constraint. LOD1 targets 28% of LOD0. Use `--full --gates` instead of `--no-render` for studio turnarounds, action poses and calibrated front/profile render evidence. Existing review folders retain earlier attempts for comparison.

Technical checks and visual reference correspondence are different results. `refs/family_final_comparisons.md` records measured silhouette deviations without treating a valid rig or export as proof of an exact concept match. These models interpret the sheets; occluded anatomy, garment interiors, hair depth and the extra nine cast designs are inferred. Clothing is skinned rather than simulated, and the existing locomotion and combat choreography is retained.

Original game FBXs are preserved in `review/original-fbx`; original Blender sources are in `review/baseline-sources`. The comparison renders use the same studio setup for both versions. Delivery previews and Unity verification live under `docs/character-concepts/round-02`.

## What is stored in Git

The repository includes the builders, reference sheets/crops, palette textures, manifests and final review evidence. Live character FBXs are versioned once under `Assets/Models/KL`. Staged duplicate FBXs, generated `.blend` masters, development probes, older review attempts, logs and original-model backup folders remain local. Rebuild staged masters/exports with the command above on a fresh checkout, or copy the existing local masters if you need the exact saved Blender files. The Windows preview also remains local under the project's existing `Builds/` ignore rule.
