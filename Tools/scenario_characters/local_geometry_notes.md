# Pak Mat local geometry studies

`local_geometry.build_variant(id)` creates a fresh Blender scene and returns
`objects`, `roles` (object name to semantic material role), and `metadata`.
The geometry is authored locally. Scenario images guide the design language;
they are not meshes, textures, or substitutes for rendered output.

All four static studies stand 1.66 m tall, use metres and Z up, and face -Y.
The same identity remains: middle-aged Malaysian father, moustache, songkok,
short-sleeved batik shirt, checked wrap sarong and sandals.

| Study | Inferred head count | Head-height target | Main geometry differences |
|---|---:|---:|---|
| A — Warm animated film | 4.5 | 0.369 m | Rounded cheek/jowl surface, medium almond eyes, pear-shaped torso, tapered arms. |
| B — Graphic comic | 4.75 | 0.349 m | Squarer head cross-section, wedge nose, stronger brow/moustache shapes, broader shoulder line. |
| C — Handmade miniature | 4.0 | 0.415 m | Broad compact head/body, larger hands/feet, half-lidded eyes, fuller cheeks and nose. |
| D — Grounded stylized | 5.75 | 0.289 m | Smaller head/eyes/nose, longer arms and lower body, restrained facial exaggeration. |

These head counts are authored direction targets, not measurements claimed from
the generated perspective references. `references/B.png` guides B and
`references/C.png` guides C. The image named `references/A.png` turned out more
grounded than its prompt, so it guides D; warm A is an independent interpretation
of the approved identity sheet. Side and back anatomy, fingers, facial depth,
cloth interiors and exact fold placement are inferred throughout.

The source uses sampled surfaces and curve lofts, then voxel unions for continuous
head/neck/ears, garment shoulders, arms/hands, and feet. Hands have unequal curled
fingers and a thumb web. Eyes have shaped apertures, eyelid rims, and irises clipped
behind the lids. The cap intersects the crown. The shirt has a fitted chest inset
and tapered collar leaves; the sarong has an offset overlapping front panel.
The forearm tapers into the interior of the palm so its closing cap is hidden.
A fused plantar pad meets each sandal's flat footbed; the sole has bevelled edges.
Concealed upper arms and the under-shirt sarong roll are omitted from the studies
to prevent skin or fastening geometry penetrating the visible garment.

All meshes have semantic material roles. Garment `UVMap` uses circumference U
(front U=0.5, corrected back seam) and normalized garment-height V. Shirt, sleeves
and collar share a coordinate window; sarong panels share theirs. Iris UVs map
each disc to a centered 0–1 square and carry `radial_iris_uv=True`.

These are high-density, unrigged art-direction studies. They do not include
production retopology, animation, collision, LODs, cloth simulation or Unity
integration. The existing game builders and game asset files are not modified.
Final appearance must be judged from the exported meshes' matched Blender
renders; successful geometry generation alone is not visual acceptance.
