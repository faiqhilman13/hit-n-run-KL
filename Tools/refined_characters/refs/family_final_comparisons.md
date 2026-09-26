# Final family reference comparison snapshot


Exact world registration: metres, Z up, front -Y, orthographic front azimuth 0 and profile azimuth 270. Reference source scale and ground stay fixed. Models are posed using measured character-specific neutral arm angles; images are never fitted by their silhouette bounds.

The table measures reference approximation; it is **not a claim of final asset acceptance or exact reconstruction**. Individual JSON files record the blockout silhouette criteria: IoU >= 0.85 and every sampled band width within 5% of reference height. The stricter forms gate, face accuracy, material fidelity, topology, deformation, animation and Unity integration each require separate evidence. Failed views remain failures.

| Character | View | IoU | Height error | Largest width/depth error |
|---|---|---:|---:|---|
| Pak Mat | front | 0.8965 | -5.5 mm | -9.3 cm at z=0.915 m |
| Pak Mat | side | 0.8950 | -5.5 mm | -3.5 cm at z=0.915 m |
| Mak Som | front | 0.9184 | -2.8 mm | +7.9 cm at z=1.177 m |
| Mak Som | side | 0.8826 | -8.5 mm | -11.9 cm at z=1.183 m |
| Along | front | 0.8181 | -2.7 mm | -9.7 cm at z=0.086 m |
| Along | side | 0.8011 | -2.7 mm | -4.3 cm at z=0.086 m |
| Adik | front | 0.7546 | -3.9 mm | +28.8 cm at z=0.280 m |
| Adik | side | 0.8171 | +5.8 mm | -10.9 cm at z=1.062 m |

## Reference limits and inferred information

- The approved sheets are generated concepts, not surveyed orthographic blueprints. Front proportions take priority; profile panels guide depth and rear panels guide coverage.
- Alpha boundaries have about 1–2 pixels of uncertainty, approximately 2–6 mm depending on the character. Pale fabric and ground shadows required explicit analytic cleanup; untouched source sheets are preserved beside the mattes.
- The profile horizontal origin is inferred from the pelvis/ankle axis. Total profile widths are independent of that origin choice. Do not excuse large depth or deformation errors as camera ambiguity.
- View-to-view hair, facial contour, cloth patterns and sandal straps vary. Adik's profile crown is about 4 source pixels below the front crown. No unsupported per-view score ceiling has been claimed.
- Hidden anatomy, garment interiors and physical cloth thickness are inferred. Each asset brief lists the details.

Full comparison images, exact camera registrations, per-band measurements and missing/extra silhouette coverage are saved with this snapshot. Rendered deformation defects require repair even where a silhouette score happens to pass.

Detail folder: `<character>/review/final_world_comparison/`. Contact sheet: `family_final_comparisons.png`.
