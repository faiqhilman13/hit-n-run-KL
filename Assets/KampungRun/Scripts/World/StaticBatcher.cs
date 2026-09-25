using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KampungRun
{
    /// <summary>
    /// Merges thousands of simple boxes (roads, kerbs, lane dashes, walls, towers) into one mesh per
    /// colour per 464 m square, so the procedural city renders in a few draw calls and what is behind
    /// the camera still gets culled. Boxes can carry a tiling facade texture on their sides.
    /// </summary>
    public class StaticBatcher
    {
        public const float ChunkSize = 464f;

        class Chunk
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector3> s = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
            public float outline, gloss;
            public Texture tex;
        }

        static Vector3[] _cv, _cn, _cs;
        static int[] _ct;

        readonly Dictionary<(Color, float, Texture, float, int, int), Chunk> _chunks = new Dictionary<(Color, float, Texture, float, int, int), Chunk>();
        readonly List<(Vector3 c, Vector3 size, Quaternion r)> _colliders = new List<(Vector3, Vector3, Quaternion)>();

        /// <summary>An invisible physics box (kit meshes are visual only).</summary>
        public void ColliderOnly(Vector3 center, Vector3 size) => _colliders.Add((center, size, Quaternion.identity));

        public void Box(Vector3 center, Vector3 size, Color color, bool collider = true, float outline = 2.2f, Quaternion? rotation = null) =>
            Add(center, size, color, collider, outline, rotation ?? Quaternion.identity, null, default, 0f);

        /// <summary>An upright box whose sides are painted with a tiling facade texture, one tile = `tile`
        /// metres (a window bay by a storey), fitted so each side shows whole bays and storeys.</summary>
        public void TexBox(Vector3 center, Vector3 size, Color color, Texture tex, Vector2 tile, bool collider = true, float outline = 2.2f, float gloss = 0f) =>
            Add(center, size, color, collider, outline, Quaternion.identity, tex, tile, gloss);

        void Add(Vector3 center, Vector3 size, Color color, bool collider, float outline, Quaternion rot, Texture tex, Vector2 tile, float gloss)
        {
            // flat colours share the chunk's palette mesh (one draw call); textured boxes keep their own
            int cx = Mathf.FloorToInt(center.x / ChunkSize), cz = Mathf.FloorToInt(center.z / ChunkSize);
            var key = tex == null ? (Color.clear, 0f, (Texture)null, 0f, cx, cz) : (color, outline, tex, gloss, cx, cz);
            if (!_chunks.TryGetValue(key, out var ch)) _chunks[key] = ch = new Chunk { outline = outline, tex = tex, gloss = gloss };
            var paint = tex == null ? ColorPalette.UV(color) : Vector2.zero;
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
                if (tex == null) { ch.uv.Add(paint); continue; }
                var n = _cn[i];
                if (Mathf.Abs(n.y) > 0.5f) { ch.uv.Add(new Vector2(0.01f, 0.5f)); continue; }   // roof / underside: the plain wall colour
                var p = Vector3.Scale(_cv[i], size);
                var along = new Vector3(-n.z, 0, n.x);
                float width = Mathf.Abs(Vector3.Dot(size, along));
                float bays = Mathf.Max(1f, Mathf.Round(width / tile.x)), rows = Mathf.Max(1f, Mathf.Round(size.y / tile.y));
                ch.uv.Add(new Vector2((Vector3.Dot(p, along) / width + 0.5f) * bays, (p.y / size.y + 0.5f) * rows));
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
                mesh.SetUVs(0, ch.uv);
                mesh.SetUVs(3, ch.s);
                mesh.SetTriangles(ch.t, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(mesh.name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ch.tex != null
                    ? LatMaterials.GetTextured(kv.Key.Item1, ch.tex, ch.gloss > 0f ? SurfaceKinds.None : SurfaceKinds.Classify(kv.Key.Item1), ch.gloss)
                    : ColorPalette.Material;
                go.isStatic = true;
            }
            ColorPalette.Apply();
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
