using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KampungRun.Arch
{
    /// <summary>
    /// The modelled buildings of the whole map, by 116 m city cell. Every cell gets a distance version of its
    /// buildings at load (same massing, roofs and signs, flat windows; one draw each). Cells near the camera are
    /// rebuilt at full detail a few milliseconds a frame, nearest first, and swapped in; cells left behind drop back
    /// to the distance version. The full detail of the whole map would be a few hundred megabytes; this keeps a
    /// ring of it round the player.
    /// </summary>
    public class ArchCity : MonoBehaviour
    {
        public static ArchCity I { get; private set; }

        public const float CellSize = 116f;
        /// <summary>Cells whose centre is this close to the camera get full detail (a little further and they let go).</summary>
        public static float NearRadius = 190f;
        /// <summary>Time a frame for building detail (ms).</summary>
        public static float Budget = 2.5f;

        class Cell
        {
            public Vector2Int key;
            public Vector2 centre;
            public readonly List<IArchItem> specs = new List<IArchItem>();
            public MeshRenderer far;
            public GameObject near;
            public ArchMesh building;     // detail under construction
            public int next;              // next spec to build
        }

        readonly Dictionary<Vector2Int, Cell> _cells = new Dictionary<Vector2Int, Cell>();
        readonly List<Cell> _queue = new List<Cell>();
        Transform _root;
        Vector2 _origin;
        float _scanT;
        public int Buildings { get; private set; }
        public int DetailCells { get; private set; }

        public static ArchCity Create(Transform parent, Vector2 origin)
        {
            var go = new GameObject("Architecture");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<ArchCity>();
            c._root = go.transform;
            c._origin = origin;
            I = c;
            ArchKit.Init();
            return c;
        }

        Vector2Int KeyOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt((p.x - _origin.x) / CellSize), Mathf.FloorToInt((p.y - _origin.y) / CellSize));

        public void Add(IArchItem s)
        {
            var k = KeyOf(s.Where());
            if (!_cells.TryGetValue(k, out var c))
                _cells[k] = c = new Cell { key = k, centre = _origin + new Vector2((k.x + 0.5f) * CellSize, (k.y + 0.5f) * CellSize) };
            c.specs.Add(s);
            Buildings++;
        }

        /// <summary>Build every cell's distance version (call once all the buildings are in).</summary>
        public void Finish()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var m = new ArchMesh();
            int tris = 0;
            foreach (var c in _cells.Values)
            {
                m.Clear();
                foreach (var s in c.specs) s.Build(m, false);
                tris += m.T.Count / 3;
                var mesh = m.ToMesh($"ArchFar_{c.key.x}_{c.key.y}");
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                c.far = Renderer($"Far_{c.key.x}_{c.key.y}", mesh);
            }
            Debug.Log($"[Arch] {Buildings} buildings in {_cells.Count} cells; distance versions {tris} triangles in {clock.ElapsedMilliseconds} ms");
        }

        MeshRenderer Renderer(string name, Mesh mesh)
        {
            var go = new GameObject(name) { layer = Layers.World };
            go.transform.SetParent(_root, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ArchKit.Material;
            r.shadowCastingMode = ShadowCastingMode.On;
            return r;
        }

        /// <summary>Build the detail round a spot straight away (spawns, teleports), so nothing pops in.</summary>
        public void Prime(Vector3 at)
        {
            Scan(at);
            foreach (var c in _queue) { while (c.next < c.specs.Count) Step(c); Swap(c); }
            _queue.Clear();
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if ((_scanT -= Time.unscaledDeltaTime) <= 0f) { _scanT = 0.25f; Scan(cam.transform.position); }
            if (_queue.Count == 0) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (_queue.Count > 0 && clock.Elapsed.TotalMilliseconds < Budget)
            {
                var c = _queue[0];
                Step(c);
                if (c.next >= c.specs.Count) { Swap(c); _queue.RemoveAt(0); }
            }
        }

        /// <summary>Queue the cells that should be detailed (nearest first) and let go of the ones that shouldn't.</summary>
        void Scan(Vector3 cam)
        {
            var p = new Vector2(cam.x, cam.z);
            float letGo = (NearRadius + 50f) * (NearRadius + 50f), want = NearRadius * NearRadius;
            foreach (var c in _cells.Values)
            {
                float d = (c.centre - p).sqrMagnitude;
                if (c.near != null && d > letGo)
                {
                    Destroy(c.near.GetComponent<MeshFilter>().sharedMesh);
                    Destroy(c.near);
                    c.near = null;
                    if (c.far) c.far.enabled = true;
                    DetailCells--;
                }
                else if (c.near == null && d < want && !_queue.Contains(c)) _queue.Add(c);
            }
            // drop queued cells we've moved away from, nearest first
            _queue.RemoveAll(c => (c.centre - p).sqrMagnitude > letGo && c.next == 0);
            _queue.Sort((a, b) => (a.centre - p).sqrMagnitude.CompareTo((b.centre - p).sqrMagnitude));
        }

        void Step(Cell c)
        {
            c.building ??= new ArchMesh();
            if (c.next < c.specs.Count) c.specs[c.next++].Build(c.building, true);
        }

        void Swap(Cell c)
        {
            if (c.building == null) return;
            var mesh = c.building.ToMesh($"ArchNear_{c.key.x}_{c.key.y}");
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            c.near = Renderer($"Near_{c.key.x}_{c.key.y}", mesh).gameObject;
            if (c.far) c.far.enabled = false;
            c.building = null;
            c.next = 0;
            DetailCells++;
        }
    }
}
