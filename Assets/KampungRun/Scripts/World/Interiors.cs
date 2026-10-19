using System.Collections.Generic;
using KampungRun.Arch;
using UnityEngine;
using UnityEngine.Rendering;

namespace KampungRun
{
    /// <summary>
    /// Streams the walk-in rooms' contents (Arch/Interior.cs) round the player: furniture (one mesh per room, lit as an
    /// interior), its solids (on "Ignore Raycast", so the camera passes through tables and shelves; the walls it stops at
    /// are the buildings' own colliders), and a shopkeeper behind the counter. The rooms' shells are part of the streets'
    /// near detail (ArchCity), so from outside you always see into an open shop; what's in it arrives as you come near.
    /// Also answers "is this point in a room?" for the chase camera.
    /// </summary>
    public class Interiors : MonoBehaviour
    {
        public static Interiors I { get; private set; }

        /// <summary>Rooms whose middle is this close to the player are furnished (a little further and they let go).</summary>
        public static float BuildRadius = 45f, ReleaseRadius = 60f;
        /// <summary>Shopkeepers stand behind their counters when you're this close (a few at a time).</summary>
        public static float KeeperRadius = 26f;
        public static int MaxKeepers = 5;

        const float Cell = 24f;

        class Built
        {
            public GameObject go;
            public Mesh mesh;
            public bool hasSpot;
            public Spot spot;
            public NPC keeper;
        }

        readonly List<RoomSpec> _rooms = new List<RoomSpec>();
        readonly Dictionary<Vector2Int, List<RoomSpec>> _grid = new Dictionary<Vector2Int, List<RoomSpec>>();
        readonly Dictionary<RoomSpec, Built> _built = new Dictionary<RoomSpec, Built>();
        readonly List<RoomSpec> _queue = new List<RoomSpec>();
        readonly List<RoomSpec> _drop = new List<RoomSpec>();
        readonly List<SolidBox> _solids = new List<SolidBox>(64);
        ArchMesh _mesh;
        float _scanT;
        int _keepers, _keeperSeq;
        Vector3 _at;

        public IReadOnlyList<RoomSpec> Rooms => _rooms;
        public int Furnished => _built.Count;
        public int KeeperCount => _keepers;

        public static Interiors Create(Transform parent)
        {
            var go = new GameObject("Interiors");
            go.transform.SetParent(parent, false);
            I = go.AddComponent<Interiors>();
            if (GameManager.WebLite) { BuildRadius = 32f; ReleaseRadius = 42f; MaxKeepers = 2; KeeperRadius = 20f; }
            return I;
        }

        void OnDestroy() { if (I == this) I = null; }

        static Vector2Int KeyOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));

        public void Add(List<RoomSpec> rooms) { foreach (var r in rooms) Add(r); }

        public void Add(RoomSpec r)
        {
            _rooms.Add(r);
            var k = KeyOf(r.Centre);
            if (!_grid.TryGetValue(k, out var list)) _grid[k] = list = new List<RoomSpec>();
            list.Add(r);
        }

        /// <summary>The room a point stands in (null outside). margin grows the rooms (negative shrinks them).</summary>
        public static RoomSpec RoomAt(Vector3 p, float margin = 0f)
        {
            if (I == null) return null;
            var k = KeyOf(p);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (I._grid.TryGetValue(new Vector2Int(k.x + dx, k.y + dz), out var list))
                        foreach (var r in list)
                            if (r.Contains(p, margin)) return r;
            return null;
        }

        /// <summary>The registered room nearest a point (for tests and captures).</summary>
        public RoomSpec Nearest(Vector3 p, System.Predicate<RoomSpec> match = null)
        {
            RoomSpec best = null;
            float bd = float.MaxValue;
            foreach (var r in _rooms)
            {
                if (match != null && !match(r)) continue;
                float d = (r.Centre - p).sqrMagnitude;
                if (d < bd) { bd = d; best = r; }
            }
            return best;
        }

        /// <summary>Furnish everything round a spot at once (spawns, teleports).</summary>
        public void Prime(Vector3 at)
        {
            _at = at;
            Scan();
            foreach (var r in _queue) if (!_built.ContainsKey(r)) Build(r);
            _queue.Clear();
            Keepers();
        }

        void Update()
        {
            var pc = PlayerController.I;
            var cam = Camera.main;
            if (pc != null) _at = pc.transform.position;
            else if (cam != null) _at = cam.transform.position;
            else return;
            if ((_scanT -= Time.unscaledDeltaTime) <= 0f) { _scanT = 0.3f; Scan(); Keepers(); }
            // one room a frame (two when the nearest is close: you're about to walk in)
            for (int n = 0; n < 2 && _queue.Count > 0; n++)
            {
                var r = _queue[0];
                _queue.RemoveAt(0);
                if (!_built.ContainsKey(r)) Build(r);
                if (_queue.Count == 0 || (_queue[0].Centre - _at).sqrMagnitude > 15f * 15f) break;
            }
        }

        void Scan()
        {
            float want = BuildRadius * BuildRadius, letGo = ReleaseRadius * ReleaseRadius;
            _drop.Clear();
            foreach (var kv in _built)
                if ((kv.Key.Centre - _at).sqrMagnitude > letGo) _drop.Add(kv.Key);
            foreach (var r in _drop) Release(r);
            _queue.Clear();
            var k = KeyOf(_at);
            int reach = Mathf.CeilToInt(BuildRadius / Cell) + 1;
            for (int dx = -reach; dx <= reach; dx++)
                for (int dz = -reach; dz <= reach; dz++)
                    if (_grid.TryGetValue(new Vector2Int(k.x + dx, k.y + dz), out var list))
                        foreach (var r in list)
                            if (!_built.ContainsKey(r) && (r.Centre - _at).sqrMagnitude < want) _queue.Add(r);
            _queue.Sort((a, b) => (a.Centre - _at).sqrMagnitude.CompareTo((b.Centre - _at).sqrMagnitude));
        }

        void Build(RoomSpec r)
        {
            _mesh ??= new ArchMesh();
            _mesh.Clear();
            _solids.Clear();
            bool keeper = Interior.Furnish(r, _mesh, _solids, out var spot);
            var b = new Built { hasSpot = keeper, spot = spot };
            var go = new GameObject("Interior_" + r.kind);
            go.transform.SetParent(transform, false);
            b.go = go;
            if (_mesh.Count > 0)
            {
                b.mesh = _mesh.ToMesh("Interior_" + r.kind);
                b.mesh.RecalculateBounds();
                MeshKeep.Upload(b.mesh);
                go.AddComponent<MeshFilter>().sharedMesh = b.mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ArchKit.Material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            foreach (var s in _solids)
            {
                var c = new GameObject("Solid") { layer = 2 };
                c.transform.SetParent(go.transform, false);
                c.transform.SetPositionAndRotation(s.centre, s.rot);
                c.AddComponent<BoxCollider>().size = s.size;
            }
            _built[r] = b;
        }

        void Release(RoomSpec r)
        {
            if (!_built.TryGetValue(r, out var b)) return;
            if (b.keeper != null) { Destroy(b.keeper.gameObject); _keepers--; }
            if (b.mesh != null) Destroy(b.mesh);
            if (b.go != null) Destroy(b.go);
            _built.Remove(r);
        }

        // ---------------------------------------------------------------------------------- shopkeepers
        struct Who { public string name, model, line; public string[] calls; }

        static Who KeeperOf(RoomSpec r)
        {
            bool alt = (r.seed & 1) == 0;
            switch (r.kind)
            {
                case InteriorKind.Runcit:
                    return alt ? new Who { name = "Ah Seng", model = "chr_townman", line = "Nak beli apa, dik? Gula, roti, telur... semua ada. Ais krim pun ada!", calls = new[] { "Mari, mari!", "Roti baru sampai!" } }
                               : new Who { name = "Kak Ros", model = "chr_townaunty", line = "Ambil bakul tu, dik. Hari ni minyak masak murah sikit.", calls = new[] { "Telur segar!", "Mari, mari!" } };
                case InteriorKind.Kopi:
                    return alt ? new Who { name = "Uncle Lim", model = "chr_pakcik", line = "Kopi-O satu? Duduk dulu, nanti saya hantar. Roti bakar pun ada.", calls = new[] { "Kopi panas!", "Duduk, duduk!" } }
                               : new Who { name = "Kak Yah", model = "chr_townaunty", line = "Nasi lemak tinggal sikit je ni. Nak bungkus ke makan sini?", calls = new[] { "Nasi lemak!", "Teh tarik!" } };
                case InteriorKind.Barber:
                    return new Who { name = alt ? "Abang Din" : "Raju", model = "chr_townman", line = "Potong rambut? Duduk, duduk. Sepuluh minit siap, macam artis!", calls = new[] { "Gunting rambut!", "Tak payah tunggu!" } };
                case InteriorKind.Hardware:
                    return new Who { name = alt ? "Uncle Wong" : "Pak Hamid", model = alt ? "chr_townman" : "chr_pakcik", line = "Cari apa, boss? Paku, cat, paip... semua ada. Tukul pun ada.", calls = null };
                case InteriorKind.Textile:
                    return new Who { name = alt ? "Kak Laila" : "Puan Meena", model = "chr_townaunty", line = "Kain baru sampai! Nak buat baju raya? Saya ukur sekarang.", calls = null };
                case InteriorKind.Gold:
                    return new Who { name = alt ? "Mr. Man" : "Puan Lee", model = alt ? "chr_townman" : "chr_townaunty", line = "Emas 916, harga hari ini bagus. Nak tengok rantai?", calls = null };
                default:
                    return new Who { name = "Tauke", model = "chr_townman", line = "Selamat datang!" };
            }
        }

        void Keepers()
        {
            foreach (var kv in _built)
            {
                var b = kv.Value;
                if (!b.hasSpot) continue;
                float d2 = (b.spot.pos - _at).sqrMagnitude;
                if (b.keeper == null && d2 < KeeperRadius * KeeperRadius && _keepers < MaxKeepers)
                {
                    var who = KeeperOf(kv.Key);
                    var face = b.spot.facing;
                    face.y = 0f;
                    var npc = NPC.Spawn("keeper" + (++_keeperSeq), who.name, who.model, b.spot.pos, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.forward), b.go.transform);
                    npc.idleLine = who.line;
                    npc.calls = who.calls;
                    b.keeper = npc;
                    _keepers++;
                }
                else if (b.keeper != null && d2 > (KeeperRadius + 8f) * (KeeperRadius + 8f))
                {
                    Destroy(b.keeper.gameObject);
                    b.keeper = null;
                    _keepers--;
                }
            }
        }
    }
}
