# Refined human cast — Unity verification

Final verification completed on 26 September 2026 with Unity 6000.6.2f1. All 13 imports and the final seven-model topology update passed their validation checks. Both gameplay batches passed 3/3 tests. The 35 hero/car combinations had at least approximately 6 cm of roof clearance; the final Aiman-only repeat also passed all seven cars and scale-restoration checks.

The Windows player built successfully at `Builds/RefinedCharacters/Windows/KampungRunKL.exe` (207 MB, zero errors). Its two warnings are Unity render-pipeline package `Deprecated.cs` partial-class naming warnings. The saved scene, shared controller, SeatFit, VehicleVisuals and PromoCapture remained byte-identical across final verification and the build, as recorded in the hash reports.

The 13 `chr_*.fbx` assets retain their paths and GUIDs. Their humanoid clips retain their original Unity internal IDs, so the existing shared animator controller keeps its references. The importer maps the chest, shoulders and toes explicitly as well as the required humanoid bones.

`KampungRun/CharacterSoft` and `kl_character_palette.png` provide smooth character lighting and the approved palette. Buildings, roads and vehicles keep the existing material pipeline. Costume and pedestrian swaps preserve the character atlas, including its highlight values; changing skin also derives a matching shadow colour unless an explicit shadow colour was supplied.

`ModelFactory.LocalBounds` now measures skinned geometry. The `SeatedCharacterScale` companion fits the displayed character beneath each car's real roof before the existing `SeatFit` aligns the hips and hands. Standing and motorcycle poses use the authored scale.

## Evidence

- `validation.txt`: import, humanoid mapping, shared clip references, LODs, palette variants, shader compilation and sampled running/sitting pose assertions for all 13 models.
- `trouser-update/validation.txt`: the same checks repeated on the final seven long-trouser models after their knee topology was refined; the other six models remained unchanged. Their final pose images also replace the corresponding images in this directory.
- `chr_*-idle.png`, `chr_*-run.png`, `chr_*-sit.png`: baked sampled poses rendered by Unity in an isolated, unsaved studio scene.
- `gameplay-results.xml`: real-scene `CastRestyled`, `VehicleTransitions` and `DriversFitUnderRoof` tests.
- `final-targeted-gameplay-results.xml`: refreshed cast/transition tests and Aiman's seven car fits after the final trouser update; `gameplay-results.xml` preserves the preceding 35-pair result for all five heroes.
- `gameplay/`: actual gameplay screenshots of the family, crowd, villain, vehicle transitions and 35 hero/car roof measurements.
- `windows-build.txt` and `preserved-assets-build.json`: build result and before/after SHA-256 values for the saved scene, controller, SeatFit, VehicleVisuals and PromoCapture. These hashes cover the build interval.
- `preserved-assets-final-verification.json`: comparison against the snapshot taken before final geometry verification; all five files remained unchanged.

## Reproduce

Run from the `KampungRun` project directory with Unity CLI on PATH:

```powershell
unity run . --timeout 300 --format json -- -executeMethod KampungRun.EditorTools.CharacterArtValidation.Run -logFile Logs/refined-final-validation.log
unity test . --mode PlayMode --filter 'KampungRun.Tests.SmokeTests.CastRestyled;KampungRun.Tests.SmokeTests.VehicleTransitions;KampungRun.Tests.SmokeTests.DriversFitUnderRoof' --output docs/character-concepts/round-02/unity/gameplay-results.xml --timeout 480 --format json -- -logFile Logs/refined-final-gameplay.log
unity run . --timeout 900 --format json -- -buildTarget Win64 -executeMethod KampungRun.EditorTools.RefinedCharacterBuild.Windows -logFile Logs/refined-windows-build.log
```

The dedicated build entry writes `Builds/RefinedCharacters/Windows/KampungRunKL.exe` from the current saved scene. It does not regenerate the scene or humanoid controller.
