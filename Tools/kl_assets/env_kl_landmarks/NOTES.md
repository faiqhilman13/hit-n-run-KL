# env_kl_landmarks

**Status:** exported, Unity round trip PASSED

## Files
- `env_kl_landmarks.blend` - editable source (collections EXPORT / COLLISION / REFERENCE)
- `env_kl_landmarks.fbx` - export (Forward +Z / Up +Y in Unity, 1 unit = 1 m)
- `textures/kl_palette.png` - shared flat-colour atlas (point-filtered in Unity)
- `previews/` - three-quarter + front/side/back (Blender Workbench), `unity-import.png` - in-engine proof

## Blender stats (from the build script)
```json
{
 "asset": "env_kl_landmarks",
 "modules": [
  {
   "id": "env_lm_kl_sentral",
   "triangles": 1168,
   "lod1_triangles": 1080,
   "size_m": [
    37.0,
    31.0,
    49.06
   ],
   "collision": true
  },
  {
   "id": "env_lm_muzium_negara",
   "triangles": 1380,
   "lod1_triangles": 1348,
   "size_m": [
    34.0,
    19.96,
    19.48
   ],
   "collision": true
  },
  {
   "id": "env_lm_masjid_negara",
   "triangles": 1156,
   "lod1_triangles": 972,
   "size_m": [
    34.0,
    34.0,
    42.0
   ],
   "collision": true
  },
  {
   "id": "env_pbg_lake",
   "triangles": 1046,
   "lod1_triangles": null,
   "size_m": [
    30.95,
    19.0,
    0.41
   ],
   "collision": false
  },
  {
   "id": "env_pbg_footbridge",
   "triangles": 600,
   "lod1_triangles": 120,
   "size_m": [
    2.42,
    10.02,
    2.26
   ],
   "collision": false
  },
  {
   "id": "env_pbg_gazebo",
   "triangles": 184,
   "lod1_triangles": null,
   "size_m": [
    5.8,
    5.8,
    6.4
   ],
   "collision": true
  },
  {
   "id": "env_pbg_pergola",
   "triangles": 1404,
   "lod1_triangles": 960,
   "size_m": [
    4.7,
    8.65,
    3.64
   ],
   "collision": false
  },
  {
   "id": "env_pbg_flowerbed",
   "triangles": 1172,
   "lod1_triangles": 934,
   "size_m": [
    6.0,
    2.0,
    1.45
   ],
   "collision": true
  },
  {
   "id": "env_pbg_orchid_arch",
   "triangles": 1860,
   "lod1_triangles": 1554,
   "size_m": [
    4.22,
    0.64,
    3.24
   ],
   "collision": false
  },
  {
   "id": "env_pbg_bench",
   "triangles": 72,
   "lod1_triangles": null,
   "size_m": [
    1.8,
    0.48,
    1.0
   ],
   "collision": true
  },
  {
   "id": "env_pbg_fountain",
   "triangles": 394,
   "lod1_triangles": 106,
   "size_m": [
    6.2,
    6.2,
    3.5
   ],
   "collision": true
  }
 ],
 "materials": 1,
 "blender": "5.2.1 LTS"
}
```

## Unity import (actually tested)
```
Unity 6000.6.2f1 / URP - kit round trip for env_kl_landmarks
  env_lm_kl_sentral            size 37.00 x 49.06 x 31.00 m, min.y -0.06, collision COL proxy, LODGroup 2 levels: LOD0 1168 tris @0.18 / LOD1 1080 tris @0
  env_lm_muzium_negara         size 34.00 x 19.48 x 19.96 m, min.y -0.06, collision COL proxy, LODGroup 2 levels: LOD0 1380 tris @0.18 / LOD1 1348 tris @0
  env_lm_masjid_negara         size 34.00 x 42.00 x 34.00 m, min.y 0.00, collision COL proxy, LODGroup 2 levels: LOD0 1156 tris @0.18 / LOD1 972 tris @0
  env_pbg_lake                 size 30.95 x 0.42 x 19.00 m, min.y -0.08, collision none (walk-through), no LOD
  env_pbg_footbridge           size 2.42 x 2.26 x 10.02 m, min.y 0.19, collision none (walk-through), LODGroup 2 levels: LOD0 600 tris @0.18 / LOD1 120 tris @0
  env_pbg_gazebo               size 5.80 x 6.40 x 5.80 m, min.y 0.00, collision COL proxy, no LOD
  env_pbg_pergola              size 4.70 x 3.64 x 8.65 m, min.y 0.00, collision none (walk-through), LODGroup 2 levels: LOD0 1404 tris @0.18 / LOD1 960 tris @0
  env_pbg_flowerbed            size 6.00 x 1.45 x 2.00 m, min.y 0.00, collision COL proxy, LODGroup 2 levels: LOD0 1172 tris @0.18 / LOD1 934 tris @0
  env_pbg_orchid_arch          size 4.22 x 3.24 x 0.64 m, min.y -0.32, collision none (walk-through), LODGroup 2 levels: LOD0 1860 tris @0.18 / LOD1 1554 tris @0
  env_pbg_bench                size 1.80 x 1.01 x 0.48 m, min.y -0.01, collision COL proxy, no LOD
  env_pbg_fountain             size 6.20 x 3.50 x 6.20 m, min.y 0.00, collision COL proxy, LODGroup 2 levels: LOD0 394 tris @0.18 / LOD1 106 tris @0
Material: M_KL_Atlas shader=KampungRun/LatInk palette=kl_palette
Facing: modules are authored front = -Y and turned at assembly; fronts face Unity +Z (the render looks from +Z).
Proof render: unity-import.png (11 modules imported and placed, viewed from the front)
```
