# Kampung Run: KL — agent handoff

Snapshot: 26 September 2026. File identity was checked at approximately 10:47 MYT (02:47 UTC); the implementation was subsequently committed and pushed to `origin/main`.

## Copy-paste briefing

Continue the existing Unity game in `C:\Users\User\PROJECTS\kampung-game\KampungRun`. Read `docs/AGENT_HANDOFF.md` before changing it. The latest completed task replaced all 13 human character assets with refined, rigged Blender models based on the four approved family concept sheets and matching NPC designs. The replacements are integrated into the saved game, have a successful Windows preview build, and are pushed to `origin/main` at implementation commit `4194bb3`. Existing seating/scene/promo work was preserved in a separate preceding commit, `a0fd323`. (Since 7 Oct 2026 the buildings, houses, plants and bridges are modelled by the architecture library: see the update at the end; the user asked for that and it supersedes the old box style.) Read the known limitations before claiming exact concept likeness or full-game verification. Two build-hook leftovers and generated Blender/player artifacts remain local. Use `RefinedCharacterBuild.Windows` for a build of the current scene, because the ordinary project build commands regenerate the scene and animator controller.

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

The user approved all four generated family sheets, then requested polished Blender characters to replace the clunky human models, **including NPCs**. At the time they liked the buildings and wanted their style retained; on 7 Oct 2026 they asked for better buildings, bridges, shops, houses, grass and trees (see the architecture library update). The character pass kept the environment and vehicle art pipeline and existing gameplay choreography. The user has not yet provided a final aesthetic review of the delivered 3D cast.

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

## Update — driver hands, KLCC promo shot, web build 1.9.0 (26 Sep 2026, afternoon)

A follow-up session acted on user feedback about the promo ("KLCC view too zoomed in, one tower", "hands going through the steering wheels") and published the refined cast to itch.

- **Hands on the wheel.** `SeatFit` places the hips, then runs a two-bone arm IK. It puts each palm on the rim at 9 and 3 (`SeatFit.GripClock`), after `SeatedCharacterScale`.
  - The hands follow the wheel for 50° each way, then slip round it.
  - `VehicleVisuals.WheelGrip` supplies the rim points.
  - Steering right turns the wheel clockwise from the driver's view. It used to turn the other way.
  - Verified for Pak Mat, Mak Som, Along and Adik in a Saga, Kancil, Hilux and Alphard using `PromoCapture.RecordDriverHands`. It writes x-ray close-ups with the car body hidden to `Tools/promo_frames/x_hands`, plus a bone-position log.
  - Adik is too short to reach the rim: her hands stop just short and the wheel sits in front of her face.
  - `CastRestyled`, `VehicleTransitions` and `DriversFitUnderRoof` still pass.
- **Promo.**
  - Landmark orbits place the camera directly; the chase camera's wall check had pulled it against a Petronas tower.
  - KLCC is framed across the tower pair from over KLCC Park (yaw 300–340).
  - Orbit fog handling is guarded and restored in `finally`.
  - Promo v3.1 was re-recorded with the refined cast.
- **Web build without regeneration.** Use `RefinedCharacterBuild.WebGL` (batch: `-buildTarget WebGL`). It shares `ProjectBuilder.WebGLPlayer()` settings with `BuildWebGL`, skips `RebuildEverything`, and hash-checks the scene and controller. It produced itch **1.9.0** (`Builds/WebGL`, 41 MB).
- **Compiling without regeneration.** Plain `Unity.exe -batchmode -nographics -projectPath . -quit` compiles without touching the scene; `ProjectBuilder.BatchSetup` regenerates it.
- **Web performance.** The refined cast cost frame rate in the browser (`Tools/web_bench.py`, headless Chrome, RTX 3080):

  | Spot | 1.9.0 | 1.9.0 without peds | 1.8.0 |
  | --- | --- | --- | --- |
  | Title | 236 fps | — | ~238 fps |
  | Kampung home | 49 fps | 72 fps | not measured |
  | Dataran | 60 fps | 72 fps | 80–110 fps (downtown) |
  | Chow Kit | 32 fps | 41 fps | ~57 fps |

  WebGL skins characters on the CPU, and LOD0 is 55–80k triangles (LOD1 15–22k), so crowds and drivers now dominate. Two fixes are on the table if the user wants the frame rate back:
  - a much lighter crowd/driver LOD from the Blender pipeline;
  - web-only LOD bias and crowd density.

## Update — driving camera (1.10.0) and on-foot feel + living streets (1.11.0) (4 Oct 2026)

The user asked for the driving camera and handling to match Hit & Run. Then they said on-foot play "isn't really fun", the world "feels quite empty and sparse" and the characters "feel hollow". Both passes take their cues from classic games: Hit & Run, Mario 64, Zelda and Arkham. **The leaked SHAR source was declined: never use it. Measure footage only.**

- **Driving (1.10.0).** `ChaseCamera` drive* fields were calibrated against Hit & Run PC footage: the camera hardly pulls back with speed but tips down. `Vehicle.cornerGrip` 0.4 lets cars slide about 10° in hard corners. `PromoCapture.RecordDriveFeel` re-measures this.
- **On-foot movement** (`PlayerController`, the on-foot block):
  - Starts in 0.14 s and stops in 0.09 s, skids when you turn back, leans into turns.
  - Full stick jogs at 0.9× `def.run`; Shift sprints at 1.22×.
  - Jumps: 0.15 s buffer, 0.12 s ledge grace, variable height, and a ×1.4 double jump with a flip. Gravity is 1.6 rising, 2.6 when cut, 2.2 falling.
  - Kick in the air ground-pounds. Punch and kick lunge onto the nearest target (2.8 m, 70°).
  - `HitStop`, squash and stretch on a `Juice` transform, footsteps, bumping into people, glancing at passers-by, and per-character quips.
  - Test hooks: `DebugJump`, `DebugStomp`, `debugSprint`.
- **On-foot camera** (`ChaseCamera`): 3.9 m back, 1.55 m up, 9° pitch, scaled by the character's height.
  - Swings back behind the direction of travel after 1 s without look input.
  - Sprint widens the field of view by 7°.
  - Follows jumps lazily, but always keeps you in frame.
- **People:**
  - Pedestrians have a type and a voice (`VoiceSynth.Townsperson`) and walk in lanes rather than single file.
  - They turn their heads (`HeadLook`), say hello, chat in pairs on the same pavement, and stop to stare at a commotion.
  - They leap back from near misses and complain when barged. Lines appear in `Barks` speech bubbles.
  - Named NPCs watch you, face you within 4 m and call you over when they have a job; stallholders cry their wares.
  - `StreetLife` adds a satay hawker at every cart and four kids playing football on the kampung padang (`Football`).
- **Animals and the world:**
  - Pigeon flocks (`Pigeons`, one mesh per flock) scatter when startled.
  - Kicked chickens tumble off in feathers; seven kicks in 15 s brings the flock's revenge. Cats dodge.
  - Synthesised ambience beds (`Ambience`) are mixed by `CityBuilder.Greenery`, crowd size and night.
  - Breakables respawn out of sight after 90 s. Mission props (anything with `onBroken`) stay broken.
  - Coin pickups climb a major scale; the HUD coin counter pops.
- **Trampolines and street dressing** (`CityBuilder.StreetLife.cs`):
  - Chow Kit awnings and canopies and the Petaling Street stall canopies are `Bouncy`, with coins above and on the roofs. Bounce boxes sit apart from the prop so it still merges.
  - `DressPatches` lines the real-KL patches' pavements with crates, kapcai, pots, bins and stools. It has its own RNG, so nothing else moves: 854 props, capped at 260 smashable.
- **Checking it:** `OnFootCapture.OnFootStills` / `OnFootRun` (Explicit) write to `Tools/promo_frames/x_onfoot`. The run also logs stall-bounce heights and ambience loudness against the music. Smoke tests must run without `-nographics`.

## Update — character animation overhaul (4 Oct 2026, evening; itch 1.12.0)

The user said the characters "feel like fat stick men" and asked for more fluid motion with a bigger range of movement. The old clips were 16- and 12-frame sine swings on `chr_aiman.fbx`.

**What was wrong:**
- The walk and run were tuned for stride speeds of 2.55 and 6.5 m/s. The cast's legs are only 0.52 m from hip to ankle, so planted feet slid at 2.4 m/s walking and 4.1 m/s running.
- Pedestrians walked as a half-blend of walk and idle.
- Humanoid retargeting sank some feet into the ground: Along by 12 cm, the kids by 3–5 cm.

**The motion library** is authored in C# (`Assets/KampungRun/Editor/Animation`) and baked to Humanoid clips in `Assets/KampungRun/Animation/Clips` (56 clips, about 15 MB after curve thinning).
- `AnimRig` poses the reference skeleton (chr_aiman):
  - The spine and arms use forward kinematics, with rotations about the rest-pose body axes.
  - The legs use two-bone IK with heel–toe roll and hinge-axis knees, so a high knee never flips.
  - Hands can reach targets by IK; blends slide the target rather than mixing rotations, so elbows never kink.
  - The result is read back with `HumanPoseHandler`.
- `AnimLibrary` holds a gait generator for idle, walk, jog, run and sprint: planted feet, hip bob, sway and swivel, counter-rotating shoulders, trailing arms and wrists, and a stabilised head.
  - Walks swing the foot on a Bézier arc that leaves and lands at stance speed.
  - Jog, run and sprint follow a runner's stride through key positions (`RunSwing`): the push-off leg trails out behind (`rearZ`, `rearLift`), the heel folds up under the seat (`tuckZ`), the knee drives through (`driveZ`), the foot reaches out in front, then paws back down.
  - Their stance is shifted behind the hips, so the push-off happens behind the body.
  - The first version swung both feet up in front of the body; the user said it looked like riding a bicycle.
- `AnimLibrary.Actions`:
  - air poses: rise, apex, fall and flail;
  - flip, pound, pound_land, land and skid;
  - a three-hit combo (punch, punch2, punch3), a punt kick, hit and knockdown;
  - wave, talk, talk2, cheer, angry, watch_cross, watch_hips, film, point_laugh, fan and panic;
  - five idle fidgets.
- `AnimLibrary.Styles` gives each style its own idle, walk, jog, run and sprint:
  - heavy: Pak Mat and Datuk Mega;
  - lady: Mak Som, the town aunty and Mei;
  - kid: Adik, Along and the kid;
  - elder: the pakcik.
  Skirted styles take shorter strides so the cloth holds together.
- All gaits share one speed table (`Gaits` in `CharacterRig.cs`): walk 1.15, jog 3.0, run 5.5 and sprint 8.5 m/s for the reference legs.
- Poses stay within Unity's **default** humanoid muscle ranges. Arms reach only about 60° above horizontal, so overhead poses add a collarbone shrug, and a thigh extends only 50° behind. The bake report prints `LIMIT:` for anything over, and Unity clamps those on playback.
  - The avatars and the seated/riding FBX takes (sit, ride, mount, dismount) are unchanged.
  - On these chibi bodies the `Chest` bone is at belly height (0.92 m) while the shoulders are at 1.17 m, and the head is 0.44 m tall. Aim hand targets from the shoulder line (`Chest()` in the library does this).

**Controller.** `HumanControllerBuilder` rebuilds `KL_Human.controller` **in place**, keeping its GUID, so the scene and `GameAssets` references hold.
- Locomotion is a blend by Style, then by Speed. Air is a blend by VelY.
- Other states: Flip, Pound, PoundLand, Land, Skid, Punch/Punch2/Punch3 (chosen by `Combo`), Kick, Hit, Knockdown, Ride, Mount, Dismount, Sit, Wave, Panic, Gesture (`GestureId`) and Fidget (`FidgetId`).
- To bake and build in one step, run `-executeMethod KampungRun.EditorTools.HumanControllerBuilder.BakeAndBuild`.

**Runtime (`CharacterRig`):**
- Speed is normalised by each character's leg length. Brisk walkers stay in the walk, played faster, and `LocoSpeed` matches the stride up to 2.4×.
- Style comes from the avatar name.
- A grounding fix evaluates the idle once at spawn and lifts the hips by the measured sink whenever the animator poses them. It's guarded against throttled or culled frames.
- Idle characters fidget every 7–15 s.
- The player gets a `lively` spring layer: lean into starts and stops, arms that overshoot, a nod on landing, and the satchel and key cord swinging.

**Gameplay hooks:**
- `PlayerController` drives velY, skidding, the flip, the pound and the hard landing.
- Pedestrians talk with their hands in chats. When something happens they react by filming it, folding their arms, putting their hands on their hips, or pointing and laughing; kids cheer, and a horn gets told off. Kids' walk speed is now 1.1–1.8 m/s.
- NPCs wave hello, stallholders gesture while calling out, and satay men fan the coals. The football kids cheer goals.
- `HeadLook` now rebinds after a body swap. It used to throw MissingReference after `SetCharacter`.

**Measured with the review tool** (`AnimationStudio.ReviewBatch`, which prints sinking and planted-foot sliding per character):
- Reference walk/run sliding: 2.44/4.07 m/s before, 0.07/0.44 m/s after. The remainder is heel–ball rolling in the metric.
- Along's feet sank 12 cm before, 8 cm before the runtime fix, and are lifted by it now.
- Round trip (authored pose → baked clip → playback): within about 1 cm for almost every clip.

**Tools:**
- Bake only: `AnimationStudio.BakeBatch`.
- Renders: `AnimationStudio.ReviewBatch [-animClips a,b] [-animChars chr_x,...] [-animFrames 10] [-animNoBake 1] [-animOld 1]` writes to `Tools/anim_review` (gitignored). Make sheets with `uv run --no-project --with pillow python Tools/anim_sheet.py`.
- Round trip: `AnimationStudio.RoundTripBatch`.
- Muscle ranges: `AnimDiagnostics.Limits` / `.Sweep`.
- In-game capture: `AnimCapture.AnimShowcase` (Explicit). It runs the family round a ring on Dataran (moves, then styles) and films a crowd, writing to `Tools/promo_frames/x_anim` with `segments.txt`.

**Verified:** SmokeTests 16/16 and `CharacterArtValidation` 731 checks with 0 failing (run to a temp folder, so the round-02 evidence is untouched).

**Known limits:**
- The sprint's back leg runs a few degrees past the thigh-extension limit; the kid sprint runs about 10° past.
- Long skirts still balloon at a full sprint.
- Elders' hands-behind-the-back doesn't fit the default arm ranges on these round bodies, so the pakcik folds his hands in front.
- Web frame rate is unchanged within run-to-run noise (`RefinedCharacterBuild.WebGL`, 42 MB; `Tools/web_bench.py`, RTX 3080):

  | Spot | 1.11.0 | After |
  | --- | --- | --- |
  | Kampung home | 42.7 fps | 41.3–43.6 fps |
  | Dataran | 45.9 fps | 51.1 fps |
  | Chow Kit | 27.5 fps | 28.2 fps |
  | Petaling (`spot=Pasar`) | 37.5 fps | 33.9–39.0 fps |

  Pedestrians remain the main cost: Petaling runs at 64 fps with `nopeds`.

## Update — baked "painted" shade, Hit & Run style (7 Oct 2026)

The user downloaded a fan remaster of Hit & Run ("SHAR Remastered" v1.0 by Muckluck, a Lucas Mod Launcher `.lmlm`) and asked whether its characters, skeleton or art could be used. **None of it can ship**: it is Simpsons IP painted over Radical's 2003 models, and our game is public. It also contains no characters: it holds 29 Pure3D files of Level 1 scenery. Treat it like the leaked source: study the technique only, keep any extraction out of the project.

**What the study showed.** Every world surface in Hit & Run uses an unlit shader multiplied by baked vertex colour. That colour is dark at the foot of walls, in corners and down lanes. 85% of its meshes carry baked gradients, and the median vertex brightness is 0.65. The user asked to try that look at Petaling Street.

**CityShade** (`Scripts/World/CityShade.cs`, `Resources/CityShadeBake.shader`):
- **Height map.** After `CityBuilder.Build` has merged the statics, every static renderer is drawn from straight above into a float height map with `CommandBuffer.DrawRenderer` and an orthographic view. The highest surface wins the depth test. Texels are 1 m over the whole map (2168×2400), or 2 m on phones.
- **Sky pass.** A GPU pass works out how much sky each texel sees at street level (0.48 m):
  - It searches the horizon in 16 directions out to 36 m and takes cos² per slice.
  - Under cover (an awning, a deck, a tree crown, or inside a building), the sky straight up is lost, and only things taller than the cover block the sides.
  - A 3×3 tent blur smooths the result into an R8 map, `_CityShade`.
- **Cost.** The bake takes about 85 ms in the editor (4,367 renderers).
- **In LatInk.** The Hit & Run branch reads the map in `CityShadeAt`:
  - Floors take the map where they stand, fading it out above 2.5 m, so roofs and flyover decks stay lit.
  - Walls read it 0.6 m in front of themselves, fading over their first 10 m, with a narrow darker band at their feet.
  - The shade multiplies all of the ambient light and 85% of the sun.
  - Shaded areas lean halfway toward the level's shadow hue.
- **Settings.** The knobs are `CityShade.Look` (strength, share of sun, wall-foot band, street level) and `CityShade.Tint`.
- **Opt-outs.** Cars opt out with rendering-layer bit 8 (`CityShade.NoShade`, set in `Vehicle.Setup`). Characters use CharacterSoft, so it never touches them.
- **Bench switches:** `noshade` turns the shade off, as the game was before. `noworldshadow` stops the world casting real-time shadows (Hit & Run's world casts none); characters and cars still cast theirs.
- **MergeStatics change.** It now disables the renderers it merged straight away; they are still destroyed at the end of the frame. This stops the bake drawing them twice.
- **Capture.** `ShadeCapture.PetalingShade` (Explicit) renders each view three ways in the same frame and writes them to `Tools/promo_frames/x_shade`. The three variants are before, with the shade, and with the shade but no world shadows. It also saves the map beside a straight-down render, for an alignment check.

**Measured** (`RefinedCharacterBuild.WebGL`, 42 MB; `Tools/web_bench.py`; RTX 3080). The shade itself costs no measurable frame rate. Turning off the world's real-time shadows gains about 10%. In the browser the bake takes 180–285 ms of the load.

| Spot | Before (`noshade`) | Shade | Shade + `noworldshadow` |
| --- | --- | --- | --- |
| Petaling (`spot=Pasar`) | 33.6 / 33.5 fps | 37.6 / 30.9 fps | 37.7 / 36.0 fps |
| Chow Kit | 26.0 fps | 27.4 fps | 29.1 fps |
| Dataran | — | 49.5 fps | — |

**Verified:** SmokeTests 16/16. The capture views round Petaling, Jamek, Dataran, Pasar Seni, the kampung, Chow Kit and KLCC show no artefacts. Traffic paint is unchanged with the shade on.

**Still open:** whether the world keeps its real-time shadows. Hit & Run casts none, and turning them off is the faster option; this was left for the user to choose. The default is the shade with the world's shadows still on.

## Update — the architecture library: modelled buildings, houses, plants and bridges (7 Oct 2026)

**The user's feedback:**
- The buildings "look like paint splattered on a rectangle". They asked to "do better with the buildings/bridges/stores/houses", the same way the animation rework went.
- Then: "work on the grass/plants/trees too, that shit is ass currently".

**Two causes:**
- **Splatter.** LatInk's world-space painted-surface noise pushed wall colours anywhere from ×0.35 to ×1.7.
- **Boxes.** Every building was a box carrying one repeated facade tile. Trees were single icosahedron balls on hex sticks. Grass was lime with dark blotches. The kit's banana plant mesh was empty.

**What replaced them:** `Scripts/World/Arch`, a C# library that models every building, house, plant and flyover dressing procedurally. It paints them the Hit & Run way, with flat vertex-colour paint over a painted detail sheet, and streams detail by city cell.

**Core**
- `ArchMesh` is the builder:
  - quads and boxes in facade frames;
  - wall panels with real cut-out openings, each with reveals and a painted back;
  - extrusions, caps, and ear-clipping triangulation;
  - smooth-shaded triangles for crowns and trunks.
- `Frame.Edge(p0, p1)` gives x along a footprint edge (left to right as seen from outside) and z pointing out of the wall. Footprints are counter-clockwise from above. Use `QuadFacing`/`TriFacing` when you're unsure of a face's winding.
- `ArchTex` lists the slices of `Resources/KLMap/arch_atlas.png`:
  - The sheet is generated by `Tools/klmap/gen_arch_atlas.py`: 8×8 cells of 256 px, imported as a Texture2DArray by `Editor/Arch/ArchImport.cs`.
  - The `CELLS` order in the script must match the enum.
  - The sheet holds glass, shutters, folding doors and roller doors; breeze blocks, grilles and AC units; clay tiles, zinc and planks; kerawang panels; 16 bilingual shop signs and year plaques; shop interiors and awnings; distance-version facade tiles; leaves, palm fronds, bark, banana leaves and blossoms; kerb hazard stripes and road signs.
- `ArchKit.Material` is a single LatInk material with the `_ARCH` variant. In that variant, base colour = vertex colour × the array slice, alpha = 1 − gloss, and the world-space surface mottling is off.
- `ArchCity` manages the map in 116 m cells:
  - Every item's distance version is built at load (about 470k triangles and 0.28 s for the whole map).
  - Cells within 190 m of the camera are rebuilt at full detail, at most 2.5 ms per frame, nearest first, and swapped in.
  - `Prime()` builds a cell synchronously; `PlayerController.Teleport` calls it.
  - Items implement `IArchItem` (`Where`, `Build(mesh, detail)`).

**Generators**
- **`Shophouse`:** pre-war, Art Deco and 1970s-modern lots.
  - Five-foot ways on columns, with tiled floors and red lanterns. The arcade runs through the party walls.
  - Recessed shopfronts (folding doors, roller shutters, or the shop open), breeze-block transoms and signboards.
  - Windows with architraves, sills and hoods. Cornices, pilasters, and dated Dutch or stepped pediments.
  - A clay-tile roof with fire walls, or a flat roof with a tank and AC units.
  - Fronts wider than 7.5 m split into bays.
  - `Arcade()` gives the five-foot way's geometry so physics can leave it walkable.
- **`Block`:** offices (curtain-wall or ribbon-window towers with a plant room on the roof); flats (balconies, AC units, laundry poles, a breeze-block stair core); civic buildings (portico); worship (arched windows, hip roof); sheds (zinc).
- **`KampungHouse`:** a rumah kampung.
  - Stilts on footings, planked walls, louvred windows under kerawang vents.
  - A verandah with a balustrade, and the tiled concrete stair.
  - A steep zinc gable with a carved gable board, white fascia and crossed finial. Sometimes a kitchen wing.
  - Same footprint, front and stair position as the kit house it replaces. Pak Mat's is teal (`wall = 0`).
- **`Flora`:** rain tree, angsana, coconut palm, frangipani, banana, shrub, bougainvillea, and grass tufts.
  - Crowns are lumpy puffs shaded dark below and light above, with painted leaves.
  - Patch lawns get tufts, about one per 70 m². Fillers get them through `LawnTufts`.
- **`Bridgework`:**
  - flyover parapets on the New Jersey profile, with a steel rail and yellow-black kerb, a fascia lip and a girder;
  - hammerhead piers;
  - sign gantries;
  - jejantas footbridges with stairs. You can climb them: `Bridgework.Collider` provides the ramps and deck.

**Data**
- **Real-KL patches.** `Tools/klmap/patches.py` with `ARCH = True` writes `KLP6`:
  - Per building: footprint, edge flags (1 street, 2 party, 4 pedestrian street, 8 river), height, levels, kind, colour and seed.
  - Then flyover deck edges and piers.
  - Patch meshes whose key ends in `_col` are physics only.
  - The building meshes (`win_`, `gls_`, `roof_`, `shop`, `lobby`) are no longer written.
- **Fillers.** `CityBuilder.ArchShopRow` replaces `Bld_ShopRow*` and keeps the old row's footprint, with the arcade where the old arcade stood. Also: `ArchBlock` (condos; office podium plus tower), `House` (in place of `env_kb_house_*`), `Tree` and `AddFlora` (in place of `Prop_RainTree`/`Angsana`/`Palm` and `env_kb_rain_tree`/`env_palm`/`env_kb_banana_plant`/`env_kb_shrub`), `LawnTufts`, and `Jejantas` (about 3 town blocks in 10).
- **Fillers' physics:** one MeshCollider, `FillerBuildings`, holding every modelled item's solid.
- **Seeds.** Item seeds come from item positions, so adding items doesn't shift `CityBuilder._rng`. `Tree()` still draws one number so the rest of the layout stays where it was.
- **Grass.** The grass texture (`Tools/gen_surfaces.py` `grass()`) was repainted into a narrow range. Refill the array with `SurfaceRepaint.Run`, which updates `SurfaceArray.asset` in place and keeps its GUID. ProjectBuilder's builder deletes and recreates the asset, so don't use it for this. `Pal.Grass`/`Park` are a little deeper.

**Tools**
- `ArchStudio.ReviewBatch` builds a test street (two shophouse rows, blocks, a kampung garden, a jejantas, a gantry and a flyover) and renders it to `Tools/arch_review`, along with `stats.txt` (triangles per item).
- `ShadeCapture.PetalingShade` (Explicit) now also captures the filler districts, a flyover and Pak Mat's house.

**Triangle budgets** (near / far, from stats.txt):

| Item | Near | Far |
| --- | --- | --- |
| Shophouse lot | ~700 | ~40 |
| 8-storey flats | ~11.7k | ~40 |
| 24-storey office | ~2.2k | ~40 |
| Kampung house | ~1.1k | ~120 |
| Rain tree | ~770 | 28 |
| Palm | ~550 | ~50 |
| Jejantas | 460 | 36 |

- **Chow Kit.** `ChowKitBlock` models four rows with `ArchShopRow(..., vary: false, flatRoof: true)`: 7 lots north and south, 3 east and west, all two storeys.
  - `BuildingSpec.flatRoof` gives a flat roof you can stand on, with the collider top at the roof.
  - `KitShop` keeps each lot's awning trampoline, item spot and coins. It also rolls the old kit-shop and sign dice, so the rest of the layout stays where it was.
  - The roof is 6.85 m up, within an awning bounce plus a double jump.

**What's left as it was:** the mamak, the surau, the landmarks, the street kit and props. About 2,000 TextMesh shop signs went with the old rows.

## Update — more trees: KL is a green city (7 Oct 2026, night)

The user's feedback after the architecture library was "MOREEEE TREEEEES, KL IS A VERY GREEN CITY". The city now has about 15,100 more trees: about 8,390 more in the real-KL patches and 6,706 new in the filler districts.

**Real-KL patches** (`Tools/klmap/patches.py`, rerun; it is offline and deterministic).

Trees in the patches went from 4,001 to 12,394:

| Patch | Before | After |
| --- | --- | --- |
| Kota Lama | 635 | 2,940 |
| Lake Gardens | 2,008 | 4,910 |
| KL Sentral | 418 | 1,889 |
| Bukit Nanas | 613 | 1,338 |
| KLCC | 267 | 787 |
| Bukit Bintang | 60 | 530 |

- **Street trees** (`build_street_trees`):
  - Every street 6.5 m or wider gets trees on both pavements, 0.8 m in from the kerb, every 9.5–12.5 m.
  - Each street gets one kind, picked from a hash of its name and its road class:
    - big roads: rain trees, palms or angsana;
    - mid roads: angsana or rain trees;
    - small roads: angsana, frangipani or rain trees.
  - The pavements along the grid roads round each patch get a row too.
  - Kept clear: within 11 m of junctions, patch corners, and where patch streets meet the grid; 6 m round named places; landmark clear zones; market canopies; flyover decks and elevated rail (`overhead()`).
- **Ground fill** (`build_trees`) uses a jittered grid with per-ground mixes (`TREE_MIX`):
  - forest: 7.5 m spacing, scale 1.05–1.7;
  - park: 10.5 m;
  - lawn: 12.5 m;
  - leftover downtown paving: 15 m, from `self.paving`, saved by `build_infill`.
- **Fit** (`tree_fitter`):
  - Every tree is sized so its crown (`CROWN`, radius at scale 1) clears the buildings and overhead decks round it.
  - A rain tree that won't fit becomes an angsana, then a frangipani, then nothing.
- **Walking rings** (`clear_walks`, run after `build_rings`):
  - People stroll the rings 1.6 m in from the kerb, and turn back at anything their forward ray hits. So no trunk collider may cross a ring.
  - A tree that's too close gets a slimmer trunk: a smaller scale, or the next smaller kind. Only about 70 are dropped.
- **Tree codes in KLP6:** 3 rain tree, 4 angsana, 5 palm, 6 frangipani. Codes 0–2 are the old in-game mixes. `CityBuilder.PatchTrees` maps both.

**Filler districts** (`Scripts/World/CityBuilder.Greenery.cs`, `BuildGreenery`).

It runs after `ArchColliders`, so physics can see everything that stands. It draws no dice from `_rng`: seeds come from `Hash01` of each position.
- **Street trees** go down all four pavements of every filler block (not Chow Kit or the landmarks):
  - 0.9 m in from the kerb, every 11 m, staying 8 m from the corners;
  - one kind to three blocks of a road: in town 45% rain tree, 35% angsana, 20% palm; in the kampung, mostly palms.
- **Lot fill** uses a jittered grid per lot:
  - kampung 7.5 m: palms, "fruit trees" (smaller angsana), bananas, rain trees;
  - parks 8.5 m;
  - surau 9 m;
  - condos 10 m;
  - offices 11 m;
  - shop back courts 12 m.
  - Left as they were: Pak Mat's yard, the mamak, the pasar and the dealer.
- **The checks a tree must pass, in order:**
  - Not under cover: a ray from above must reach the ground. A spot deep inside a building touches none of its walls, so this test is what keeps trees out of big podiums.
  - The trunk capsule is clear of anything solid, Detail layer included (lamps, stops, booths, bins, trunks).
  - Two crown boxes, one turned 45°, clear anything big (Detail excluded). Otherwise the tree shrinks, then becomes a smaller kind.
  - It keeps a gap from the trees the blocks already planted (`_floraAt`, recorded in `AddFlora`) and stays clear of named places (5 m) and coin and item spots (1.5 m).
- **Clear of the walk loops.** People walk 3 m in from the kerb, at the lot centre ±19 m; street trees stand 0.9 m in, and lot trees stay within ±17 m of the lot centre.
- **Forest belt** (`ForestBelt`): a 70 m strip of forest floor outside the border wall, with two staggered rows of big rain trees, angsana and palms. They are out of reach, so they have no colliders. It closes the views down the roads and the map's edge from the air.
- **Counts** (editor): 3,293 street trees, 1,753 in the lots and 1,660 round the edge. The pass takes about 140 ms.
- **WebLite (phones):** half the street trees, and the lot spacing is 1.4× wider.

**Cost.** Items went from 23,172 to 38,241. Distance versions went from 483k to 951k triangles, built in about 0.5 s in the editor.

**Verified:** SmokeTests 16/16 with the Chow Kit rows, the trees and the regenerated patches. `ShadeCapture.PetalingShade` has three new views: 22 Lake Gardens, 23 Bukit Nanas and 24 a high city view. Before/after sheets are in `Tools/promo_frames/city_rework`, which is gitignored.

**Measured** (`RefinedCharacterBuild.WebGL`, 42 MB; `Tools/web_bench.py`; RTX 3080). Frame rate is unchanged within run-to-run noise. Loading is about 0.4 s longer: the greenery pass takes 0.27–0.34 s in the browser, and the distance versions about 0.12 s more.

| Spot | Architecture only | With the trees |
| --- | --- | --- |
| Kampung home | 43.8 fps | 38.0 / 51.1 fps |
| Petaling (`spot=Pasar`) | 39.5 / 36.6 fps | 38.6 / 38.1 fps |
| Chow Kit | 31.2 / 27.7 fps | 26.8 / 31.4 fps |
| Dataran | 48.6 fps | 50.5 fps |
| KLCC (`spot=Towers`) | — | 76.0 fps |

The architecture library on its own also left frame rates where the shade work had them (see the table above).

**Released 7 Oct 2026:**
- **GitHub:** `50ca1ff`, the shade, the architecture library, Chow Kit and the trees in one commit.
- **itch:** html5 1.13.0 (build #2081752).
- **Re-run bench:** a Chrome bench re-run just before the push measured low: home 25 fps, Chow Kit 11 fps. The user was running Crusader Kings 3 and Defender was scanning the new build, so even CPU-only load steps took twice as long. The clean numbers are the table above.
