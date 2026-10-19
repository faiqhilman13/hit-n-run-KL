# Pak Mat local Blender studies — final visual review

**Decision: accepted for choosing a static art direction after repair 3.** The four exported meshes provide a usable comparison of proportions, facial treatment and pattern scale. This is not acceptance as a finished production character, an exact reconstruction of the Scenario references, or a replacement for the current game cast. The user's preferred direction remains undecided.

## Evidence reviewed

The final [comparison](comparison.png), [D profile](renders/D/profile.png), [A three-quarter](renders/A/threequarter.png) and [C three-quarter](renders/C/threequarter.png) were inspected after repair 3. The preceding review also covered all four front and profile views and the face comparison. This review did not run Blender, Unity, animation or gameplay tests.

## Visible repairs and acceptance

- The cap now seats against the head rather than appearing suspended above a narrow scalp wedge.
- The former deep notches between the sleeves and torso are resolved. The shoulder silhouette is continuous in the inspected views.
- A visible neck connects the head to the shirt opening. The collar no longer has the earlier ragged, stepped V-cut.
- The abrupt cuff-like ledge at the wrists is removed in the final A/C views and D profile. Hands remain simplified but connect to the forearms.
- The foot-to-sole gap is closed. D's profile now shows the foot supported by a solid sandal sole; A/C confirm the repaired contact from three-quarter views.
- The moustache, songkok, orange patterned shirt, green checked wrap sarong and brown sandals preserve Pak Mat's identity. The sarong overlap reads as a wrap rather than trousers.

No further severe detached-part or open-join defect was found in those final views. The repaired areas are sufficient for this bounded static comparison.

## How the four alternatives read

| Study | Visible distinction | Remaining limit |
| --- | --- | --- |
| A — Warm animated film | Rounded head and torso, open almond-shaped eyes and medium-scale pattern | Facial anatomy and fabric drape remain simpler than the animated-film reference quality implied by the label. |
| B — Graphic comic | Squarer torso/face treatment, wedge nose, rounder graphic eyes and larger batik motifs | The portable PBR render does not provide a finished two/three-tone comic shader. |
| C — Handmade miniature | Largest head, compact body, broader features and heavier eyelids | Tactile surface detail is subtle at gallery scale; the result still reads primarily as a smooth digital sculpt. |
| D — Grounded stylized | Smaller head/features, longer limbs and taller adult proportions | Neck, arms and facial planes remain simplified. It does not match the anatomical maturity or cloth detail of Scenario reference A. |

The differences are in mesh proportions and facial construction as well as pattern scale; these are not merely colour swaps. Material contrast between the directions is less pronounced than the silhouette contrast.

## Production limits

The remaining straight neck/limb forms, simplified palms/toes, sparse shirt folds and relatively rigid sarong should guide refinement of the selected direction. The reference images have richer cheek/lid anatomy, more natural cloth behavior and more convincing material separation. The present studies do not establish equivalent polish.

These are dense, unrigged study meshes. Source/export manifests and fresh-import statistics agree on **660,712 triangles for A, 623,384 for B, 769,344 for C and 542,084 for D**, with 23 meshes, 12 materials and approximately 1.66 m standing height per variant. Current GLB hashes match both the export manifests and the corresponding render statistics. Those checks confirm artifact identity and roundtrip consistency; they do not prove runtime suitability.

Production work would require an appropriate topology/LOD budget, rigging, expressions, deformation checks, final materials and review inside the game. No runtime performance, animation quality, full surface audit or final user aesthetic approval is claimed. The current game assets are unchanged by this exploration.
