# Kampung Run: KL — agent handoff

Snapshot: 26 September 2026. File identity was checked at approximately 10:47 MYT (02:47 UTC); the implementation was subsequently committed and pushed to `origin/main`.

## Copy-paste briefing

Continue the existing Unity game in `C:\Users\User\PROJECTS\kampung-game\KampungRun`. Read `docs/AGENT_HANDOFF.md` before changing it. The latest completed task replaced all 13 human character assets with refined, rigged Blender models based on the four approved family concept sheets and matching NPC designs. The replacements are integrated into the saved game, have a successful Windows preview build, and are pushed to `origin/main` at implementation commit `4194bb3`. Existing seating/scene/promo work was preserved in a separate preceding commit, `a0fd323`. Preserve the existing building/environment style. Read the known limitations before claiming exact concept likeness or full-game verification. Two build-hook leftovers and generated Blender/player artifacts remain local. Use `RefinedCharacterBuild.Windows` for a build of the current scene, because the ordinary project build commands regenerate the scene and animator controller.

## Project and user direction

| Item | Current state |
| --- | --- |
| Game | **Kampung Run: KL**, a cartoon Kuala Lumpur open-world driving/action game inspired by *The Simpsons: Hit & Run* |
| Actual Unity/Git root | `C:\Users\User\PROJECTS\kampung-game\KampungRun` (one directory below the outer workspace) |
| Engine | Unity **6000.6.2f1**, URP |
| Art tooling | Blender **5.2.1 LTS**, procedural Python builders; imagegen concept sheets |
| Main scene | `Assets/Scenes/KampungRun.unity` |
| Remote | [faiqhilman13/hit-n-run-KL](https://github.com/faiqhilman13/hit-n-run-KL), branch `main` |
| Implementation commit | [`4194bb3`](https://github.com/faiqhilman13/hit-n-run-KL/commit/4194bb3) — `Replace the KL human cast with refined Blender characters` |
| Preserved baseline commit | [`a0fd323`](https://github.com/faiqhilman13/hit-n-run-KL/commit/a0fd323) — `Preserve existing driver grip and scene baseline` |
| Previous remote base | `319cd41` — `Promo v3: the family, KL and driving fun (no car lineup)` |
| Delivery status | Character replacements integrated and pushed; Windows preview built locally; this handoff is a follow-up documentation commit |

The user approved all four generated family sheets, then requested polished Blender characters to replace the clunky human models, **including NPCs**. They liked the buildings and wanted their style retained. The character pass kept the environment and vehicle art pipeline and existing gameplay choreography. The user has not yet provided a final aesthetic review of the delivered 3D cast.

The [approved sheets and prompts](character-concepts/round-01/README.md) are for Pak Mat, Mak Som, Along and Adik. The nine other designs extend that direction using the existing characters' roles and clothing; they do not have separately approved orthographic sheets.

## Existing game systems

These features predate this character pass and were not all replayed or revalidated during it. The [project README](../README.md) provides the overview; [GameData.cs](../Assets/KampungRun/Scripts/Core/GameData.cs) and [MissionBook.cs](../Assets/KampungRun/Scripts/Missions/MissionBook.cs) define the five current chapters. The README's earlier four-level summary was corrected during handoff preparation.

- **Five playable chapters**, each with five story missions and a street race: the four family chapters across morning, afternoon, sunset and night, followed by Aiman's **Tahap 5: Penghantar Chow Kit**, using a delivery motorcycle in an after-rain setting. Mei and Ravi feature in the fifth chapter's MegaMaju Ekspres story.
- MegaMaju Berhad/Datuk Mega story involving mind-control Cendol Ajaib and robot mynah birds.
- On-foot movement, sprinting, double jump, punch combos, kicks, vehicle entry/exit and traffic knockdowns.
- Timed driving, collecting, car destruction/tailing, checkpoints, races and police pursuits; the Meter Saman wanted/fine system.
- Pedestrians, left-hand traffic, police, shops, costumes, phone booths, coins, cards, camera birds and breakable objects.
- A roughly 2.1 × 2.3 km compressed KL map with OSM-derived streets and landmarks, procedural districts, a minimap, UI, sound effects and music.
- Myvi, Saga, Kancil, Alphard/van, Hilux, taxi and police cars, plus a kapcai motorcycle. Vehicle visuals include doors, suspension/body motion, lights and visible drivers.

Keyboard essentials: WASD movement/driving, mouse camera, Shift sprint, Space jump/handbrake, E interact/enter/exit, Q punch, F kick, H horn, R recover flipped car, Esc pause. Full mappings are in the project README.

## What this task completed

### 1. Rebuilt the complete human cast

All 13 existing `Assets/Models/KL/chr_*.fbx` paths now contain the refined models. Existing Unity asset GUIDs were retained.

| Asset ID | Character |
| --- | --- |
| `chr_pakmat` | Pak Mat |
| `chr_maksom` | Mak Som |
| `chr_along` | Along |
| `chr_adik` | Adik |
| `chr_aiman` | Aiman |
| `chr_mei` | Mei |
| `chr_ravi` | Ravi |
| `chr_townman` | Town man |
| `chr_townaunty` | Town aunty |
| `chr_pakcik` | Pakcik |
| `chr_kid` | Neighbourhood kid |
| `chr_polis` | Police officer |
| `chr_datukmega` | Datuk Mega |

The models have rounded facial volumes, fitted eyes/lids/brows, noses and mouths, sculpted hair and scarves, connected clothing surfaces, patterned garments, hands and footwear. The underlying movement and combat choreography was retained.

Each source is under `Tools/refined_characters/<asset>/`, containing `<asset>_master.blend`, packed palette textures, an exported FBX, manifests and a rerunnable `build/06_rig.py`. Family folders also contain cropped references and measurements. Masters separate reference, source, delivery, rig, collision and socket collections. Covered anatomy is retained in the hidden source and omitted from the game mesh to avoid intersections.

Technical contract:

- Metres; Blender +Z up / -Y forward, imported to Unity +Z forward.
- Existing 23-bone humanoid skeleton and 15 action takes: `_tpose`, `idle`, `walk`, `run`, `panic`, `jump`, `punch`, `kick`, `ride`, `mount`, `dismount`, `sit`, `knockdown`, `hit`, `wave`.
- Four expression shape keys: `Grin`, `Alarm`, `Determined`, `Blink`, plus Basis.
- Two skinned LODs. LOD0: **55,608–79,208 triangles**; LOD1: **15,569–22,178**. The chosen LOD0 ceiling is 80,000; it has not been established as a mobile/WebGL performance budget.
- Normalized skin weights, at most four influences per vertex; UVs and palette textures included.

### 2. Integrated the art into Unity

| File | Change and purpose |
| --- | --- |
| [ModelImportPipeline.cs](../Assets/KampungRun/Editor/ModelImportPipeline.cs) | Character-specific material/palette setup; explicit humanoid mappings including chest, shoulders and toes; preserved internal animation clip IDs so the existing controller retains references. |
| [CharacterSoft.shader](../Assets/KampungRun/Shaders/CharacterSoft.shader) | Soft, matte character lighting. World/vehicle materials continue through the existing `LatInk` pipeline. |
| [Character palette](../Assets/Models/KL/kl_character_palette.png) | Dedicated human palette compatible with existing palette-cell swaps. |
| [KLPalette.cs](../Assets/KampungRun/Scripts/Core/KLPalette.cs) | Costume/NPC variants copy the character atlas, preserve unswapped colours and alpha metadata, and derive matching skin shadows unless explicitly overridden. |
| [ModelFactory.cs](../Assets/KampungRun/Scripts/Core/ModelFactory.cs) | Measures skinned bounds and attaches the seated-scale companion to spawned characters. |
| [SeatedCharacterScale.cs](../Assets/KampungRun/Scripts/Characters/SeatedCharacterScale.cs) | Fits a seated character beneath the actual car roof, before existing `SeatFit` places hips/hands; restores authored scale on exit and retains full scale on motorcycles. |
| [CharacterArtValidation.cs](../Assets/KampungRun/Editor/CharacterArtValidation.cs) | Import/avatar/clip/LOD/material/palette/pose verification and baked Unity proof images. |
| [SmokeTests.cs](../Assets/KampungRun/Tests/SmokeTests.cs) | Refined-cast recognition, actual roof-clearance checks and scale-restoration assertions. |
| [RefinedCharacterBuild.cs](../Assets/KampungRun/Editor/RefinedCharacterBuild.cs) | Builds the existing saved scene without regenerating it or the controller; verifies five protected file hashes. |

Seated fitting runs in this order: `VehicleVisuals` (100), `SeatedCharacterScale` (150), existing `SeatFit` (200). Preserve that order when modifying vehicle seating.

A stale imported avatar hierarchy was repaired during integration. The current importer keeps Unity's imported skeleton while applying explicit mappings; it does not automatically reconstruct every possible future skeleton/wrapper change.

### 3. Made the source pipeline repeatable

| Source | Responsibility |
| --- | --- |
| `Tools/blender/kl/kl_refined.py` | Body/face/clothing construction, character specifications and skin weighting |
| `Tools/blender/kl/kl_refined_hair.py` | Hair, curls, pigtails and scarf geometry |
| `Tools/blender/kl/kl_refined_pants.py` | Connected trouser pelvis and legs |
| `Tools/refined_characters/build_characters.py` | Masters, rigs, LODs, animation, exports and optional publishing |
| `Tools/refined_characters/review_characters.py` | Studio images, technical checks, clean FBX roundtrip and pose review |
| `Tools/refined_characters/reference_prep.py` | Reference crops, masks, measurements and briefs |
| `Tools/refined_characters/compare_references.py` | Calibrated front/profile comparison at actual world scale |
| `Tools/blender/kl/kl_build.py` | Routes character builds through the refined builder; legacy character function remains available; environment/vehicle builders retained |

See the [source README](../Tools/refined_characters/README.md) before regenerating assets. Generated masters can be overwritten by a rebuild; preserve any manual Blender edits before running it.

## Current playable build and visual evidence

**Latest verified Windows preview, local only:** `Builds/RefinedCharacters/Windows/KampungRunKL.exe`. Keep its entire adjacent build folder, including `_Data`, DLLs and runtime files. The build report records **207 MB, zero errors and two warnings**. The warnings concern Unity render-pipeline package `Deprecated.cs` partial-class naming. The executable is not uploaded to GitHub; a fresh checkout can build it using the command below.

In Unity, open the main scene and press Play. To reproduce this preview, use **Kampung Run → Build Refined Character Preview (Windows)**. Existing builds elsewhere in `Builds/`, WebGL outputs and published browser versions were not refreshed in this pass and may show older characters.

| Evidence | Location |
| --- | --- |
| Delivery overview | [round-02 README](character-concepts/round-02/README.md) |
| Family, before/after | [family](character-concepts/round-02/after-family.png), [original above / refined below](character-concepts/round-02/before-after-family.png) |
| Other nine characters | [NPC/cast lineup](character-concepts/round-02/after-npcs.png) |
| Full cast and small-scale check | [all 13](character-concepts/round-02/after.png), [colour/grayscale strip](character-concepts/round-02/gameplay-strip.png) |
| Actual expression morphs | [Pak Mat expression strip](character-concepts/round-02/pakmat-expressions.png) |
| Blender review | [findings](character-concepts/round-02/blender-review.md), `Tools/refined_characters/review/final/` |
| Unity and gameplay evidence | [verification README](character-concepts/round-02/unity/README.md), `docs/character-concepts/round-02/unity/gameplay/` |
| Installed asset hashes/GUIDs | [published-assets.json](character-concepts/round-02/published-assets.json) |

Canonical current Blender renders/reports are under `review/final`; earlier candidate/probe folders are retained for diagnosis and must not be mistaken for the delivery.

## Verification completed and its limits

| Check | Result |
| --- | --- |
| Blender source and clean FBX reimport | All 13 passed geometry/rig/weight/UV/texture/action checks. |
| Blender visual samples | Front, side, back, three-quarter and six action poses per character inspected. Earlier sleeve/knee ridges were resolved in those samples. |
| Unity art validation | All 13 passed; final seven long-trouser updates were revalidated separately, with the other six unchanged. |
| Main gameplay batch | **3/3 passed:** `CastRestyled`, `VehicleTransitions`, `DriversFitUnderRoof`. Roof checks covered five heroes × seven cars = 35 combinations. |
| Final targeted batch | **3/3 passed** after the final trouser update, including Aiman's seven car fits. |
| Seating | Approximately 6 cm minimum tested roof clearance; exact authored-scale restoration checked for car exits and motorcycle transitions. |
| Windows build | Successful; zero errors, two package warnings. |
| Protected files | Five files byte-identical across final verification/build. |
| Handoff freshness check | All 13 current FBX hashes and GUIDs still match the published manifest; all five protected files still match the final snapshot; executable exists. |

Evidence: [main test XML](character-concepts/round-02/unity/gameplay-results.xml), [final test XML](character-concepts/round-02/unity/final-targeted-gameplay-results.xml), [build report](character-concepts/round-02/unity/windows-build.txt), [protected-file hashes](character-concepts/round-02/unity/preserved-assets-final-verification.json).

This was a character integration verification, **not a complete campaign replay, every-frame animation audit or platform/performance benchmark**. The roof test uses instant boarding; it does not establish that every frame of every animated door-entry sequence is perfect. The standalone build was produced successfully; this report does not claim a separate exhaustive playthrough of that executable. Handoff preparation only reread evidence and checked file identity; it did not rerun Unity or Blender.

## Known art limitations and lessons for the next agent

The models interpret the concept sheets. Technical validation does not establish exact likeness or final user approval. Hair, hands, scarves and clothing details are simplified. The measured [family comparison findings](../Tools/refined_characters/refs/family_final_findings.md) show substantial remaining contour differences, especially Along and Adik. Only Pak Mat's profile met the configured strict silhouette gate; other views failed overlap or width tolerances. NPCs have no measured source-sheet likeness claim.

Clothing is skinned, not simulated. Retained animation choreography may still feel stylized or stiff even after the mesh improvements. Any further motion polish is a separate task requiring in-game review.

**Do not reintroduce aggressive LOD0 clothing decimation.** Long skinny triangles looked acceptable in neutral poses but produced ribs/spikes when elbows and knees bent. Final garments use uniform tessellation without decimation: 0.008 m child spacing, 0.014 m for Pak Mat/Mak Som/Town Aunty, 0.012 m for other adults. Long trousers use 0.016 m, or 0.017 m for Ravi/Polis/Datuk. Along/Kid shorts retain their reviewed earlier construction. LOD1 is still decimated for distance.

Garment decals must follow the same weights as their supporting surface; Mak Som's hem exposed this issue. Adik's hem colour is cut into the fused surface rather than added as overlapping trim. Preserve these repairs when changing outfits.

Unity batchmode proof images bake skinned meshes because unsimulated renderer buffers can be stale. Use the existing validation method rather than judging an arbitrary headless render. For bounds code, preserve the tested relationship between `BakeMesh(..., true)` and renderer transforms; an earlier scale investigation found that blindly compensating twice gives incorrect results.

## Working-tree preservation and build hazards

The implementation, live FBXs, reference assets, final evidence and repeatable source scripts are now committed and pushed. A fresh checkout of `main` includes the refined game assets. It does **not** include generated `.blend` masters, the Windows player, staged duplicate FBXs, development probes or original-model backup folders. These remain on this machine under the existing project rules and the new `Tools/refined_characters/.gitignore`. Regenerate masters/exports from the scripts, or copy/archive the exact local artifacts when moving machines. Earlier review attempts and logs also remain local; final evidence is in Git.

These five files were already modified when the character task began, were intentionally preserved during the art work, and were then committed unchanged in baseline commit `a0fd323` when the user requested the push:

- `Assets/Scenes/KampungRun.unity`
- `Assets/KampungRun/Animation/KL_Human.controller`
- `Assets/KampungRun/Scripts/Characters/SeatFit.cs`
- `Assets/KampungRun/Scripts/Vehicles/VehicleVisuals.cs`
- `Assets/KampungRun/Tests/PromoCapture.cs`

The recorded hashes cover the final verification/build interval, not a retained task-start snapshot. Do not attribute their pre-existing changes to the character work or reset them. The `SeatFit`/`VehicleVisuals` pair adds hand IK, wheel grip positions and corrected wheel rotation; it is needed for the tested driver presentation. Scene/controller differences from the prior remote base are serialization-only. `PromoCapture` changes are optional capture/test tooling, including landmark framing and driver-hand inspection.

Optional promo capture routines were not part of the successful gameplay test batch. Their existing edge cases include division by zero if an orbit is run with zero fog-start distance, and incomplete camera/fog/grip cleanup if capture is interrupted.

Two leftovers are deliberately **not committed** and remain untouched locally:

- `ProjectSettings/ProjectSettings.asset`: one `preloadedAssets` entry, GUID `052faaac586de48259a63d0c4782560b`, points to the existing `Assets/InputSystem_Actions.inputactions`. It matches the existing project-wide actions configuration. Input System build-hook code and timestamps strongly indicate this was retained after an interrupted build; it does not introduce new control bindings.
- `Assets/Resources.meta`: identifies an empty folder left by Performance Testing's temporary build-info/settings generation and cleanup. No other asset references that folder GUID.

These files were outside the five-file preservation check. The remote tree therefore omits those two local build-hook leftovers; it retains the tested gameplay code, scene and models.

**Ordinary `ProjectBuilder.BuildWindows`, `BuildWebGL`, and “Rebuild Everything” can regenerate the scene/controller.** Use the dedicated refined preview builder when the intent is to package the current saved game. It hashes the five protected files and reports preservation. Preserve `.fbx.meta` GUIDs and animation clip IDs when replacing character exports.

Original character FBXs are retained in `Tools/refined_characters/review/original-fbx`; original Blender sources are in `review/baseline-sources`. These are recovery references, not the live game assets.

## Commands for continuation

Run from the actual Unity project root. Blender executable: `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`. `unity` CLI is on PATH. Read the installed `unity-cli` skill before using that CLI in a new agent session.

Stage one character without publishing it:

```powershell
Set-Location 'C:\Users\User\PROJECTS\kampung-game\KampungRun'
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --threads 4 --python-exit-code 1 --python Tools/refined_characters/build_characters.py -- chr_pakmat
```

Omit the asset ID to rebuild all 13. `--publish` copies staged exports to live Unity asset paths. Review staged output before publishing a new art iteration.

Technical export checks:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --threads 4 --python-exit-code 1 --python Tools/refined_characters/review_characters.py -- --root Tools/refined_characters --budget 80000 --no-render --roundtrip --out Tools/refined_characters/review/final-technical
```

For full studio evidence, use `--full --gates` in place of `--no-render` and select an output folder for the new iteration.

Unity validation, gameplay checks and current-scene build:

```powershell
unity run . --timeout 300 --format json -- -executeMethod KampungRun.EditorTools.CharacterArtValidation.Run -logFile Logs/refined-final-validation.log
unity test . --mode PlayMode --filter 'KampungRun.Tests.SmokeTests.CastRestyled;KampungRun.Tests.SmokeTests.VehicleTransitions;KampungRun.Tests.SmokeTests.DriversFitUnderRoof' --output docs/character-concepts/round-02/unity/gameplay-results.xml --timeout 480 --format json -- -logFile Logs/refined-final-gameplay.log
unity run . --timeout 900 --format json -- -buildTarget Win64 -executeMethod KampungRun.EditorTools.RefinedCharacterBuild.Windows -logFile Logs/refined-windows-build.log
```

These commands can overwrite reports/build outputs. Use new output/log paths if retaining this snapshot as a baseline.

## Suggested continuation

Start by viewing the current family/NPC renders and the game itself, then act on the user's next feedback. If they want closer concept matching, use the measured family deviations to guide shape edits. If they want smoother motion, review full animation sequences in gameplay. Before targeting browsers/mobile or increasing crowd density, profile the refined models and LOD behavior. Preserve the city style, rig/clip identity and current gameplay state throughout.
