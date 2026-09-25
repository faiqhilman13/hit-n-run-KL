using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KampungRun
{
    public partial class CityBuilder
    {
        const float MergeChunk = Pitch * 2f;        // 232 m squares

        // GameObject.isStatic is an editor flag (always false in a player build), so the city keeps its own list
        readonly HashSet<GameObject> _static = new HashSet<GameObject>();

        /// <summary>Mark a prop as never moving (it may be merged into the city's static meshes).</summary>
        void SetStatic(GameObject go, bool on)
        {
            go.isStatic = on;
            if (on) _static.Add(go); else _static.Remove(go);
        }

        /// <summary>
        /// The life-size city is tens of thousands of props and street-kit pieces that never move. Merge their
        /// meshes into one per material per 232 m square (per layer and shadow mode, so the clutter still
        /// culls early and shadows stay as they were), and drop the originals' renderers: a browser has one
        /// thread to cull and draw with. Colliders stay on the original objects. Only the LOD0 meshes are
        /// kept (the kit's LOD1s are barely lighter). Breakables, critters, signs, landmarks and anything
        /// with a script keep their own renderers.
        /// </summary>
        void MergeStatics()
        {
            var groups = new Dictionary<(int cx, int cz, Material mat, int layer, ShadowCastingMode shadows), List<(CombineInstance ci, Vector2 uv, Material own)>>();
            var palette = ColorPalette.Material;
            var done = new HashSet<Component>();
            int merged = 0, kept = 0;
            var why = new Dictionary<string, int>();
            foreach (var holder in new[] { _props, _kitRoot })
                foreach (Transform prop in holder)
                {
                    var reason = KeepReason(prop.gameObject);
                    if (reason != null) { kept++; why.TryGetValue(reason, out int w); why[reason] = w + 1; continue; }
                    foreach (var lod in prop.GetComponentsInChildren<LODGroup>(true))
                    {
                        var lods = lod.GetLODs();
                        for (int l = 1; l < lods.Length; l++)
                            foreach (var r in lods[l].renderers) if (r) { done.Add(r); var mf = r.GetComponent<MeshFilter>(); if (mf) done.Add(mf); }
                        done.Add(lod);
                    }
                    foreach (var mr in prop.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (mr.GetComponent<TextMesh>()) { mr.gameObject.layer = Layers.Detail; continue; }
                        if (done.Contains(mr) || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                        var mf = mr.GetComponent<MeshFilter>();
                        var mesh = mf ? mf.sharedMesh : null;
                        if (mesh == null || !mesh.isReadable) { kept++; continue; }
                        var mats = mr.sharedMaterials;
                        var c = mr.bounds.center;
                        int cx = Mathf.FloorToInt((c.x - X0) / MergeChunk), cz = Mathf.FloorToInt((c.z - Z0) / MergeChunk);
                        for (int sm = 0; sm < mesh.subMeshCount && sm < mats.Length; sm++)
                        {
                            if (mats[sm] == null) continue;
                            // flat colours all go in the chunk's palette mesh; textured materials keep their own
                            bool plain = ColorPalette.Plain(mats[sm], out var col, out int surf);
                            var key = (cx, cz, plain ? palette : mats[sm], mr.gameObject.layer, mr.shadowCastingMode);
                            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<(CombineInstance, Vector2, Material)>();
                            list.Add((new CombineInstance { mesh = plain ? Painted(mesh, ColorPalette.UV(col, surf)) : mesh, subMeshIndex = sm, transform = mr.transform.localToWorldMatrix },
                                      Vector2.zero, mats[sm]));
                        }
                        done.Add(mr);
                        done.Add(mf);
                        merged++;
                    }
                }
            var root = new GameObject("MergedStatics").transform;
            root.SetParent(_city.root, false);
            foreach (var kv in groups)
            {
                var (cx, cz, mat, layer, shadows) = kv.Key;
                // keep each merged mesh under ~1M vertices
                var parts = kv.Value;
                int start = 0;
                while (start < parts.Count)
                {
                    int verts = 0, end = start;
                    while (end < parts.Count && (end == start || verts + parts[end].ci.mesh.vertexCount < 1_000_000)) verts += parts[end++].ci.mesh.vertexCount;
                    var slice = parts.GetRange(start, end - start);
                    MergedMesh(slice, $"Merged_{cx}_{cz}_{(mat == palette ? "palette" : mat.name)}", mat, layer, shadows, root, null);
                    start = end;
                }
            }
            foreach (var c in done) if (c) Object.Destroy(c);
            foreach (var m in _painted.Values) Object.Destroy(m);
            _painted.Clear();
            ColorPalette.Apply();
            var reasons = new System.Text.StringBuilder();
            foreach (var kv in why) reasons.Append($" {kv.Key}={kv.Value}");
            Debug.Log($"[City] merged {merged} static props into {root.childCount} meshes ({groups.Count} groups); kept {kept} as they are:{reasons}");
        }

        static Mesh Combine(List<(CombineInstance ci, Vector2 uv, Material own)> parts, string name)
        {
            int verts = 0;
            var cis = new CombineInstance[parts.Count];
            for (int i = 0; i < parts.Count; i++) { cis[i] = parts[i].ci; verts += parts[i].ci.mesh.vertexCount; }
            var m = new Mesh { name = name, indexFormat = verts > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.CombineMeshes(cis, true, true, false);
            m.RecalculateBounds();
            return m;
        }

        static void MergedMesh(List<(CombineInstance ci, Vector2 uv, Material own)> parts, string name, Material mat, int layer, ShadowCastingMode shadows, Transform root, Mesh m)
        {
            m ??= Combine(parts, name);
            m.UploadMeshData(true);          // GPU copy only: the CPU side is not needed again
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(root, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows;
        }

        readonly Dictionary<(Mesh, Vector2), Mesh> _painted = new Dictionary<(Mesh, Vector2), Mesh>();

        /// <summary>A copy of mesh with every vertex's UV on one palette colour (cached: props share meshes).</summary>
        Mesh Painted(Mesh mesh, Vector2 uv)
        {
            if (_painted.TryGetValue((mesh, uv), out var m)) return m;
            m = Object.Instantiate(mesh);
            var uvs = new Vector2[m.vertexCount];
            for (int i = 0; i < uvs.Length; i++) uvs[i] = uv;
            m.uv = uvs;
            _painted[(mesh, uv)] = m;
            return m;
        }

        /// <summary>Why a prop keeps its own renderers (null: it can be merged).</summary>
        string KeepReason(GameObject go)
        {
            if (go.name.StartsWith("Landmark_")) return "landmark";
            if (!_static.Contains(go)) return "moves";
            if (go.GetComponentInChildren<Rigidbody>()) return "rigidbody";
            if (go.GetComponentInChildren<ParticleSystem>() || go.GetComponentInChildren<Light>() || go.GetComponentInChildren<SkinnedMeshRenderer>()) return "fx";
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (mb) return mb.GetType().Name;   // Breakable, Critter, PhoneBooth, ...
            return null;
        }
    }
}
