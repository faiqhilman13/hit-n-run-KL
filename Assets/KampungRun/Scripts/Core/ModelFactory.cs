using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Spawns Blender models. The FBX files face -Z after import, so each model is put
    /// under a wrapper object and flipped 180 degrees: the wrapper's +Z is "forward".
    /// </summary>
    public static class ModelFactory
    {
        public static GameObject Spawn(string model, Vector3 pos, Quaternion rot, Transform parent = null, string name = null)
        {
            var root = new GameObject(name ?? model);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, rot);
            var prefab = GameAssets.I.Model(model);
            if (prefab != null)
            {
                var inst = Object.Instantiate(prefab, root.transform);
                inst.name = "Model";
                inst.transform.localPosition = Vector3.zero;
                // KL handoff assets are exported facing +Z; the legacy procedural ones face -Z
                // (keep the importer's root rotation: single-mesh FBX roots carry the axis correction)
                inst.transform.localRotation = IsKL(model) ? prefab.transform.localRotation : Quaternion.Euler(0, 180, 0);
                if (IsKL(model)) SetupProxies(root);
                if (model.StartsWith("chr_")) root.AddComponent<SeatedCharacterScale>();
            }
            return root;
        }

        public static bool IsKL(string model) =>
            model.StartsWith("chr_") || model.StartsWith("veh_") || model.StartsWith("env_");

        /// <summary>KL assets carry a COL_* proxy mesh: hide it and turn it into a box collider
        /// on the root (the visible mesh never acts as the physics shape).</summary>
        static void SetupProxies(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("COL_")) continue;
                var mf = t.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh)
                {
                    var b = mf.sharedMesh.bounds;
                    var m = root.transform.worldToLocalMatrix * t.localToWorldMatrix;
                    var box = root.AddComponent<BoxCollider>();
                    var c = m.MultiplyPoint3x4(b.center);
                    var s = m.MultiplyVector(b.size);
                    box.center = c;
                    box.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                    box.enabled = false;   // callers enable it (props) or read it (vehicles)
                    box.isTrigger = false;
                }
                t.gameObject.SetActive(false);
            }
        }

        /// <summary>Enable the collider built from a KL asset's COL_ proxy (if any).</summary>
        public static BoxCollider UseProxyCollider(GameObject root)
        {
            var bc = root.GetComponent<BoxCollider>();
            if (bc) bc.enabled = true;
            return bc;
        }

        /// <summary>Swap Blender materials (by palette name, e.g. "Batik") for new colours.</summary>
        public static void Recolor(GameObject go, IDictionary<string, Color> swaps)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                // KL models share one atlas material: repaint named palette cells instead
                if (KLPalette.IsKL(r.sharedMaterial))
                {
                    r.sharedMaterial = KLPalette.Swap(r.sharedMaterial, swaps);
                    continue;
                }
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string key = mats[i].name.Replace("Lat_", "").Replace(" (Instance)", "");
                    if (swaps.TryGetValue(key, out var c))
                    {
                        mats[i] = LatMaterials.Get(c);
                        changed = true;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        public static Bounds LocalBounds(GameObject go)
        {
            var inv = go.transform.worldToLocalMatrix;
            bool first = true;
            var b = new Bounds();
            void Include(Bounds mb, Matrix4x4 m)
            {
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; }
                    else b.Encapsulate(p);
                }
            }
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Include(mf.sharedMesh.bounds, inv * mf.transform.localToWorldMatrix);
            }
            // Humanoid bodies have no MeshFilter. Measure the actual pose in renderer
            // space so player capsules follow the new adult and child proportions.
            foreach (var sk in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (sk.sharedMesh == null) continue;
                var baked = new Mesh();
                sk.BakeMesh(baked, true); // compensate scale; the matrix below applies it once
                Include(baked.bounds, inv * sk.transform.localToWorldMatrix);
                if (Application.isPlaying) Object.Destroy(baked);
                else Object.DestroyImmediate(baked);
            }
            return b;
        }

        public static BoxCollider AddBoundsCollider(GameObject go, float shrink = 0f, float minY = float.NegativeInfinity)
        {
            var b = LocalBounds(go);
            if (b.min.y < minY)
            {
                float top = b.max.y;
                b.min = new Vector3(b.min.x, minY, b.min.z);
                b.max = new Vector3(b.max.x, top, b.max.z);
            }
            var bc = go.AddComponent<BoxCollider>();
            bc.center = b.center;
            bc.size = b.size - Vector3.one * shrink * 2f;
            return bc;
        }

        public static Transform Find(GameObject root, string child)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == child) return t;
            return null;
        }
    }

    /// <summary>Procedural primitives with smoothed-normal UV3 so the ink outline works.</summary>
    public static class Shapes
    {
        static Mesh _cube;

        public static Mesh Cube
        {
            get
            {
                if (_cube != null) return _cube;
                var verts = new List<Vector3>();
                var normals = new List<Vector3>();
                var uv3 = new List<Vector3>();
                var tris = new List<int>();
                Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var n in dirs)
                {
                    Vector3 a = n.y != 0 ? Vector3.forward : Vector3.up;
                    Vector3 u = Vector3.Cross(n, a);
                    Vector3 w = Vector3.Cross(n, u);
                    int b = verts.Count;
                    Vector3[] c = { -u - w, u - w, u + w, -u + w };
                    foreach (var k in c)
                    {
                        var p = (n + k) * 0.5f;
                        verts.Add(p);
                        normals.Add(n);
                        uv3.Add(p.normalized);
                    }
                    tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
                _cube = new Mesh { name = "LatCube" };
                _cube.SetVertices(verts);
                _cube.SetNormals(normals);
                _cube.SetUVs(3, uv3);
                _cube.SetTriangles(tris, 0);
                // make sure winding faces outward
                var t = _cube.triangles;
                var vv = _cube.vertices;
                if (Vector3.Dot(Vector3.Cross(vv[t[1]] - vv[t[0]], vv[t[2]] - vv[t[0]]), normals[t[0]]) < 0)
                {
                    for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                    _cube.triangles = t;
                }
                _cube.RecalculateBounds();
                return _cube;
            }
        }

        public static GameObject Box(string name, Vector3 center, Vector3 size, Color color, Transform parent,
            bool collider = true, float outline = 2.2f, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = Cube;
            go.AddComponent<MeshRenderer>().sharedMaterial = LatMaterials.Get(color, outline);
            if (collider) go.AddComponent<BoxCollider>();
            return go;
        }
    }
}
