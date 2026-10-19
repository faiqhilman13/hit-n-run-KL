# Pak Mat — four local Blender design studies

Status: **four local Blender design studies built and rendered for selection**. Scenario generated three reference images for **13 CU each / 39 CU total**, then rejected the fourth because the team's free-plan generation limit is 50 CU. No Scenario 3D generation was launched and no plan was upgraded. The user explicitly chose: **“Build the variants locally in Blender instead.”**

Confirmed scope: **Faiqhilman97’s Organization → Default Project** (`team_3x1GhFd1Xhc6VYjwTNXbohCm` / `proj_65fEAfifv7xVSJfnoZE3EVnE`). The user explicitly replied “yeah confirm.”

Gemini 3.1 Flash (`model_google-gemini-3-1-flash`) produced the references. Tripo 3.1 was investigated but never used. Exact Scenario prompts, job/asset IDs, charges and quota error are retained in `image-runs.json`; discovery details are saved in `model-discovery.json`; the original brief is in `brief.json`. The prior catalog estimate is superseded by these actual charges and the local fallback.

Final geometry, original texture patterns, exports and renders are authored locally in Blender 5.2.1. The Scenario images are visual references only. `references/A.png` turned out relatively grounded, so it guides **D**, despite its initial prompt. B and C guide their corresponding variants. Metric proportions, unseen backs, hidden anatomy and garment construction are interpreted for this exploration, not measured source reconstructions.

The user requested different designs of one existing character, then clarified that they want the actual rendered character asset and its art style. The intended delivery is therefore distinct 3D mesh variants with renders from those meshes, not just illustrated turnaround sheets or descriptions. Pak Mat is the selected subject.

## Identity held across variants

Middle-aged Malaysian father, warm medium-brown skin, black moustache, short black hair, black songkok, burnt-orange floral short-sleeved batik shirt, dark green checked sarong, brown sandals, generous belly, friendly and slightly weary expression. Target standing height 1.66 m. The sarong is a continuous wrap garment, not trousers. Hands and limbs should have continuous, natural joins.

Reference: `../round-01/a-pak-mat-sheet-v1.png`. Use individual existing character crops from `Tools/refined_characters/chr_pakmat/ref/` as generation inputs; the multi-panel sheet is for review rather than a single-object reconstruction input.

## Four initial directions

| ID | Direction | Geometry and surface treatment |
| --- | --- | --- |
| A | Warm animated film | Approximately 4.5 heads tall; pear-shaped torso; connected rounded shoulders, expressive almond eyes, soft sculpted cheeks; matte skin and supple cloth folds. |
| B | Graphic comic | Approximately 4.75 heads tall; squarer shoulders, wedge nose, angular cheek planes, graphic moustache; restrained contours and crisp two/three-tone shading with bold readable batik shapes. |
| C | Handmade miniature | Approximately 4 heads tall; compact bean-shaped torso, broad hands and feet, slightly irregular sculpted cheeks; subtle clay surface, visibly woven garments, rounded seams and tactile sandals. |
| D | Grounded stylized | Approximately 5.75 heads tall; restrained eyes and nose, mature lids/cheeks, weighty belly, more natural shoulder/hand transitions; cotton weave, sewn seams and a naturally hanging sarong. |

These are alternatives in sculpt, proportion and material style. Clothing identity and colour families stay consistent; palette swaps alone do not count as a new direction.

## Local build and presentation

1. Author four distinct local mesh designs with original batik, plaid and material texture maps, guided by the reference images.
2. Save editable Blender masters and export self-contained standard PBR GLBs.
3. Import the GLBs into fresh Blender scenes to check the actual delivered meshes and textures. Normalize scale and use matched camera framing, studio lighting and background.
4. Render three-quarter, front, profile and face-detail views; assemble a labeled comparison and local gallery.
5. Retain provenance and measurable export/roundtrip statistics. These static studies do not include a production rig, animation, LODs or runtime acceptance.

B uses designed planes and bold portable materials. A production two/three-tone cel shader remains a later runtime decision.

## Files and reproduction

- `comparison.png` and `face-comparison.png`: matched renders of the actual exported meshes.
- `index.html`: gallery with view switching and mesh/source links.
- `assets/<ID>/pak_mat_<ID>.glb`: textured static mesh.
- `assets/<ID>/pak_mat_<ID>_master.blend`: editable source with packed textures.
- `assets/<ID>/asset-manifest.json`: source/export metadata, geometry statistics and SHA-256.
- `renders/<ID>/<ID>_studio.blend`: fresh GLB import with studio lights and four cameras.
- `renders/<ID>/{threequarter,front,profile,face}.png`: actual mesh renders.
- `renders/render-manifest.json`: import, texture, scale and camera verification.

Run from the `KampungRun` repository:

```powershell
python Tools/scenario_characters/make_local_textures.py
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --threads 4 --python-exit-code 1 --python Tools/scenario_characters/build_local_variants.py -- --out docs/character-concepts/round-03
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --threads 4 --python-exit-code 1 --python Tools/scenario_characters/render_variants.py -- --manifest docs/character-concepts/round-03/variants.json --out renders --samples 64 --resolution 1280
python Tools/scenario_characters/compose_gallery.py docs/character-concepts/round-03
python Tools/scenario_characters/verify_local_delivery.py docs/character-concepts/round-03
```

The implementation lives in `Tools/scenario_characters/`. Blender sources remain local under the project's existing `*.blend` ignore rule. This exploration has not been committed or pushed. Earlier completed game work and the previous remote push are recorded in `../../AGENT_HANDOFF.md`.

The gallery is also served locally at `http://127.0.0.1:8768/index.html` while the preview process runs. It uses only this folder's files and can be opened directly from `index.html` if the server stops. The front/face/three-quarter controls were checked in the in-app browser; the three-quarter view was left open for review.

## Verification and limits

The GLBs are reimported into fresh Blender scenes before rendering. The final set has sixteen PNGs at 1280 px high, using 64-sample Eevee and identical full-body and face camera windows. `verification.json` records binary GLB checks, embedded textures, export/import hash and triangle agreement, loaded packed images, 1.66 m scale, camera equality, output dimensions and local gallery links. `visual-review.md` records the independent visual assessment.

Three focused repair rounds corrected cap seating, collar/shoulder construction, concealed skin and sarong penetration, the wrist transition and sandal footbed contact. Original 1024 px fabric/skin/leather maps were authored locally; skin grain and cloth sheen were reduced after visual and export review. These are interpretations of the reference directions, with simplified faces/necks and subtle material differences. They are suitable for choosing a direction, not a claim of finished film-quality sculpture or exact Scenario-reference fidelity.

The meshes are deliberately dense static sculpts. Retopology, rigging, skinning, animation, LODs, facial controls and runtime testing remain necessary after the user's selection. Production character replacement is not part of this round.

## Review criteria written before generation

- Exactly one complete adult character, with two arms and two legs, unobscured hands/feet and no accidental floating or merged parts.
- Recognizable Pak Mat identity and all five clothing elements: songkok, moustache, orange batik shirt, green wrap sarong and brown sandals.
- Clear shape/material distinction between A, B, C and D; identity-preserving art exploration rather than colour-only changes.
- Coherent front/profile/back volume and no severe texture projections or geometry defects visible in the review views.
- Consistent framing, standing height, pose and lighting so presentation differences do not determine the user's choice.

Review each output against these criteria. Fix a specific failure from the approved baseline; do not repeatedly regenerate unrelated aspects. No more than three repair rounds. The user's preference determines the direction to refine next.

Current game assets are not replaced by this exploration. Other local edits to gameplay, build and promo files belong to ongoing work and must remain untouched.
