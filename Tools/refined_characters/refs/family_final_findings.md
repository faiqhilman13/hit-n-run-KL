# Family concept comparison findings

Snapshot: the family masters from `build-release.log` (13 completed characters), followed by `build-release-budget.log` (Pak Mat, Mak Som and Town Aunty), both verified complete with `Blender quit`. The release changes garment tessellation; the budget update uses 0.014 m spacing for those three assets. Renders were refreshed on 2026-09-26 at 02:15:30–02:15:47 UTC. This snapshot supersedes `build-delivery.log`, `build-final.log` and all earlier candidate comparisons. The measured renders use the source sheet's original metre scale and ground line. Front camera azimuth is 0; profile is 270. Neutral arm lowering is Pak Mat 68 degrees, Mak Som 68 degrees, Along 71 degrees, Adik 61 degrees. No silhouette fitting or independent image rescaling was used.

The generated sheets remain design references, and the models are interpretations. This record does not claim exact reconstruction or complete asset acceptance.

| Character | Front IoU | Profile IoU | Remaining measured differences |
|---|---:|---:|---|
| Pak Mat | 0.8965 | 0.8950 | Upper-arm/sleeve span is 9.3 cm narrower at z=0.915 m. Profile belly and sarong depth are substantially closer than the first build. |
| Mak Som | 0.9184 | 0.8826 | Tudung width near the neck is 7.9 cm wider in front; profile crown depth is 10.7 cm narrower and neck/rear-drape depth 11.9 cm narrower. |
| Along | 0.8181 | 0.8011 | Shorts width is about 8.3 cm larger near z=0.415 m; the front shoe span is 9.7 cm narrower near z=0.086 m. Hair contour, hands and leg shapes also differ. |
| Adik | 0.7546 | 0.8171 | Shoulder span is 14.0 cm wider at z=0.617 m, pigtail span 10.1 cm wider at z=0.954 m, and profile crown depth 10.9 cm narrower. Hands extend below the source fingertip line; the large width difference near z=0.280 m includes those hands and must not be interpreted as dress width. |

Overall height deviations are 2.7–8.5 mm, depending on view. Alpha-mask edge uncertainty is about 1–2 source pixels (roughly 2–6 mm). The large discrepancies above exceed that uncertainty; they are not explained by camera registration.

Compared with the preceding staged build, the severe inward garment collapse below the armpits on Along and Adik is absent in these final neutral renders. Mak Som now has a rear scarf connection, but its straight/angular construction and profile contour differ from the source's continuous soft drape. Current neutral renders alone cannot establish that all animation poses deform correctly; the separate pose/engine audit is required.

Compared with the superseded delivery snapshot, garment tessellation changes each view's IoU by less than 0.0012. The silhouette discrepancies above remain. Only Pak Mat's profile meets the configured blockout silhouette gate; all other views fail either silhouette overlap or width-band tolerances. This gate is not a full asset-quality or animation-deformation acceptance test.

The delivery build also unions each trouser pelvis and both leg surfaces. Along's front render now shows a continuous crotch join rather than the previous separate ball overlap. Along and Kid retain the reviewed short-trouser surfaces at 6,100 and 4,756 triangles. A later NPC-only topology repair removes collapse decimation from the seven long-trouser variants, using uniform 0.016 m cells (0.017 m for Ravi, Polis and Datuk Mega to meet the 12,000-triangle trouser limit). Isolated validation found 7,304–11,112 triangles for those surfaces, one connected manifold surface each, preserved untagged shoes/arms, and normalized weights with at most three influences. The complete rebuilt NPC pose audit remains separate. This change does not alter these family masters or their recorded hashes, and no inflated reference score is claimed. See `pants_probe/uniform_trouser_reports.json` for the final isolated geometry checks.

All reports use the unchanged original concepts and cleaned analytic masks. The supplied concepts are inconsistent in smaller details: curl placement, floral prints, sandal straps, facial contour and the crown height of Adik's side view. Profile ground-axis placement is inferred from pelvis/ankle alignment; total profile depths are independent of that horizontal origin. No unmeasured per-view ceiling or lowered tolerance is claimed.

The other nine cast/NPC designs are adaptations of the approved family style. They have no supplied orthographic reference sheets, so this family-only comparison provides no measured likeness claim for them.

Evidence: `family_final_comparisons.md`, `family_final_comparisons.json`, `family_final_comparisons.png`, and each character's `review/final_world_comparison/` directory.

## Delivery source identity

These SHA-256 hashes identify the actual master files loaded for this snapshot. Each per-character `render-registration.json` records its render timestamp and source path; the aggregate JSON repeats those fields.

| Character | Master .blend SHA-256 |
|---|---|
| Pak Mat | `d544e66422d1bd7930eb8235f54ed9ecb4cf22f67e72de9fc6fd1066612a1f69` |
| Mak Som | `4173b92f0fbcfc0b68b1ebdcbdc0fa4c8a346a9b0641fc41ed28970660a41e99` |
| Along | `4b55da4278acdaa667e812ff60eb6def2c32ec9c439c343537353cfbc051409b` |
| Adik | `62871a098bed3026d3170b7ceb26c63f4f4afa0bd61db781137be39292672c45` |
