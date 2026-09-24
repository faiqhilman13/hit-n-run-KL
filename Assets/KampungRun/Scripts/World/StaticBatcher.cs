using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KampungRun
{
    /// <summary>
    /// Merges thousands of simple boxes (roads, kerbs, lane dashes, walls) into one mesh
    /// per colour so the procedural city renders in a handful of draw calls.
    /// </summary>
    public class StaticBatcher
    {
        class Chunk
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector3> s = new List<Vector3>();
            public readonly List<int> t = new List<int>();
            public float outline;
        }

        static Vector3[] _cv, _cn, _cs;
        static int[] _ct;

        readonly Dictionary<(Color, float), Chunk> _chunks = new Dictionary<(Color, float), Chunk>();
        readonly List<(Vector3 c, Vector3 size, Quaternion r)> _colliders = new List<(Vector3, Vector3, Quaternion)>();

        /// <summary>An invisible physics box (kit meshes are visual only).</summary>
        public void ColliderOnly(Vector3 center, Vector3 size) => _colliders.Add((center, size, Quaternion.identity));

        public void Box(Vector3 center, Vector3 size, Color color, bool collider = true, float outline = 2.2f, Quaternion? rotation = null)
        {
            var key = (color, outline);
            if (!_chunks.TryGetValue(key, out var ch)) _chunks[key] = ch = new Chunk { outline = outline };
            var rot = rotation ?? Quaternion.identity;
            if (_cv == null)
            {
                var cube = Shapes.Cube;
                _cv = cube.vertices;
                _cn = cube.normals;
                var cs = new List<Vector3>();
                cube.GetUVs(3, cs);
                _cs = cs.ToArray();
                _ct = cube.triangles;
            }
            int b = ch.v.Count;
            for (int i = 0; i < _cv.Length; i++)
            {
                ch.v.Add(center + rot * Vector3.Scale(_cv[i], size));
                ch.n.Add(rot * _cn[i]);
                ch.s.Add(rot * _cs[i]); // corner direction for the outline hull
            }
            foreach (int i in _ct) ch.t.Add(b + i);
            if (collider) _colliders.Add((center, size, rot));
        }

        public void Build(Transform parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            foreach (var kv in _chunks)
            {
                var ch = kv.Value;
                var mesh = new Mesh { name = name + "_" + ColorUtility.ToHtmlStringRGB(kv.Key.Item1), indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(ch.v);
                mesh.SetNormals(ch.n);
                mesh.SetUVs(3, ch.s);
                mesh.SetTriangles(ch.t, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(mesh.name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = LatMaterials.Get(kv.Key.Item1, ch.outline);
                go.isStatic = true;
            }
            var colRoot = new GameObject("Colliders");
            colRoot.transform.SetParent(root.transform, false);
            foreach (var c in _colliders)
            {
                if (c.r == Quaternion.identity)
                {
                    var bc = colRoot.AddComponent<BoxCollider>();
                    bc.center = c.c;
                    bc.size = c.size;
                }
                else
                {
                    var g = new GameObject("RotCol");
                    g.transform.SetParent(colRoot.transform, false);
                    g.transform.SetLocalPositionAndRotation(c.c, c.r);
                    g.AddComponent<BoxCollider>().size = c.size;
                }
            }
            colRoot.isStatic = true;
        }
    }
}
