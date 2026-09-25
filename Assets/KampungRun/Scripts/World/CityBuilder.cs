using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Kuala Lumpur at Hit &amp; Run scale: people, cars and shophouses life-sized, and the districts round
    /// the big landmarks rebuilt from the real city.
    ///
    /// The map is a road grid (NX x NZ cells of 96 m blocks with 20 m roads) laid over a compressed copy
    /// of real KL (Tools/klmap/layout.py). Each cell is one of:
    ///   patch    real-KL streets, buildings, rivers, parks and flyovers from OpenStreetMap (KLPatch),
    ///            round Dataran Merdeka / Masjid Jamek / Merdeka 118 (Kota Lama), the Lake Gardens,
    ///            KL Sentral + Brickfields, Bukit Nanas, KLCC and Bukit Bintang
    ///   river    the Gombak and Klang channels either side of Kampung Baru; they run on into Kota Lama
    ///            and meet at Masjid Jamek, as they really do
    ///   kampung  Kampung Baru: stilt houses, the family home, the surau, the padang
    ///   filler   four 44 m lots round a back lane, from the district generators below (shophouses,
    ///            condos, office towers, Chow Kit...), or one of the outlying landmarks
    ///
    /// Everything is generated from a seed so the layout is identical every run.
    /// </summary>
    public partial class CityBuilder
    {
        public const int NX = 18, NZ = 20;                     // = Resources/KLMap/layout.json (nx, nz)
        public const float Lot = 44f, Lane = 8f;                // a lot = one district generator's patch
        public const float Block = Lot * 2f + Lane, Road = 20f, Pitch = Block + Road;
        public const float LandmarkScale = 2f;                  // outlying landmark models + their block layout
        /// <summary>How much bigger the city is than the old compressed one (54 m pitch): mission
        /// timers, search radii and traffic ranges scale by this.</summary>
        public const float WorldScale = Pitch / 54f;
        public static readonly float X0 = -NX * Pitch * 0.5f, Z0 = -NZ * Pitch * 0.5f;

        public enum Zone { Kampung, River, Shops, Condo, Masjid, Mamak, Pasar, Towers, Park, KLTower, Home, Surau, Padang, Dataran, Dealer, BukitBintang, PasarSeni, ChowKit, Brickfields, KLSentral, MuziumNegara, PerdanaGardens, MasjidNegara,
            BatuCaves, TuguNegara, Pavilion, Merdeka118, StadiumMerdeka, TheanHou, IstanaNegara, Patch, Office }

        public class City
        {
            public Transform root;
            public RoadNetwork roads = new RoadNetwork();
            public readonly Dictionary<string, Vector3> places = new Dictionary<string, Vector3>();
            public readonly Dictionary<string, Quaternion> facings = new Dictionary<string, Quaternion>();
            public readonly List<WalkZone> walkZones = new List<WalkZone>();   // pavement loops people stroll round
            public readonly List<Vector3> coinSpots = new List<Vector3>();
            public readonly List<Vector3> itemSpots = new List<Vector3>(); // mission collectible candidates
            public Bounds bounds;

            /// <summary>The nearest point on a pavement loop to p (where people stand), optionally
            /// some metres along the loop from there.</summary>
            public Vector3 Sidewalk(Vector3 p, float along = 0f)
            {
                WalkZone best = null;
                float bd = float.MaxValue, bs = 0f;
                foreach (var z in walkZones)
                {
                    if (z.bounds.xMin - 60 > p.x || z.bounds.xMax + 60 < p.x || z.bounds.yMin - 60 > p.z || z.bounds.yMax + 60 < p.z) continue;
                    float s = z.Closest(p);
                    var q = z.PointAt(s, p.y);
                    float d = (q - p).sqrMagnitude;
                    if (d < bd) { bd = d; best = z; bs = s; }
                }
                return best != null ? best.PointAt(bs + along, 0.18f) : p;
            }
        }

        readonly System.Random _rng = new System.Random(1957); // Lat's first comic year-ish
        City _city;
        StaticBatcher _ground;
        Transform _props, _kitRoot;

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        int RI(int a, int b) => _rng.Next(a, b);

        public static Vector3 BlockCenter(int col, int row) => new Vector3(X0 + col * Pitch + Pitch * 0.5f, 0, Z0 + row * Pitch + Pitch * 0.5f);
        public static float RoadX(int i) => X0 + i * Pitch;
        public static float RoadZ(int k) => Z0 + k * Pitch;

        // ------------------------------------------------------------------ layout (Tools/klmap/layout.py)
        [System.Serializable] public class LayoutPatch { public string name; public int c0, r0, c1, r1; }
        [System.Serializable] public class LayoutRiver { public string name; public int col, r0, r1; public float width; }
        [System.Serializable] public class LayoutKampung { public int c0, c1, r0, r1; }
        [System.Serializable]
        public class LayoutFile
        {
            public float K, pitch, road;
            public int nx, nz, gombak, north_row;
            public LayoutPatch[] patches;
            public LayoutRiver[] rivers;
            public LayoutKampung kampung;
        }

        static LayoutFile _layout;
        public static LayoutFile Layout
        {
            get
            {
                if (_layout == null)
                {
                    _layout = JsonUtility.FromJson<LayoutFile>(Resources.Load<TextAsset>("KLMap/layout").text);
                    if (_layout.nx != NX || _layout.nz != NZ || !Mathf.Approximately(_layout.pitch, Pitch))
                        Debug.LogError($"[KLMap] layout.json is {_layout.nx}x{_layout.nz} @ {_layout.pitch} m, CityBuilder is {NX}x{NZ} @ {Pitch} m");
                }
                return _layout;
            }
        }

        public static LayoutPatch PatchAt(int col, int row)
        {
            foreach (var p in Layout.patches)
                if (col >= p.c0 && col <= p.c1 && row >= p.r0 && row <= p.r1) return p;
            return null;
        }

        public static LayoutRiver RiverAt(int col, int row)
        {
            foreach (var r in Layout.rivers)
                if (col == r.col && row >= r.r0 && row <= r.r1) return r;
            return null;
        }

        public static bool IsKampungCell(int col, int row)
        {
            var k = Layout.kampung;
            return col >= k.c0 && col <= k.c1 && row >= k.r0 && row <= k.r1;
        }

        public static bool InsidePatch(Vector3 p) => PatchAt(Mathf.FloorToInt((p.x - X0) / Pitch), Mathf.FloorToInt((p.z - Z0) / Pitch)) != null;

        /// <summary>
        /// The filler cells, north row (19) first, columns 0..17. '.' = a patch, river or kampung cell.
        ///   S shops  C condos  O office towers  M mamak  D car dealer  X Chow Kit market  L park
        ///   V Batu Caves  I Istana Negara  T Thean Hou
        /// NW: Chow Kit / Jalan TAR / Titiwangsa; NE: Jalan Ampang; E: Jalan Sultan Ismail, Imbi, Pudu, TRX;
        /// SE: Jalan Hang Tuah / Bukit Petaling.
        /// </summary>
        static readonly string[] FillerMap =
        {
            "VCSOSCL....OCS....",   // 19
            "ISXXSMO....COS....",   // 18
            "CDSXSOS....SOC....",   // 17
            "LSCSMSO...........",   // 16
            "COSSSDS.......OOCO",   // 15
            "SSOCSSO.......OCOO",   // 14
            "..................",   // 13
            "..................",   // 12
            "..................",   // 11
            "..................",   // 10
            "..................",   // 9
            "..............OCOS",   // 8
            "..............COOC",   // 7
            "..............SOLC",   // 6
            "..............OCSO",   // 5
            ".........SCOCSCOSC",   // 4
            ".........CSLSCOSCO",   // 3
            ".........SOSCSSTLC",   // 2
            ".........CSOSCCSOS",   // 1
            ".........SCSLSOCSC",   // 0
        };

        static Zone ZoneAt(int col, int row)
        {
            if (PatchAt(col, row) != null) return Zone.Patch;
            if (RiverAt(col, row) != null) return Zone.River;
            if (IsKampungCell(col, row))
            {
                // the original kampung, cell for cell: home one in from the Klang, surau and padang on the Gombak side
                int kc = col - Layout.kampung.c0, kr = row - Layout.kampung.r0;
                if (kc == 1 && kr == 2) return Zone.Home;
                if (kc == 0 && kr == 4) return Zone.Surau;
                if (kc == 0 && kr == 1) return Zone.Padang;
                return Zone.Kampung;
            }
            char ch = FillerMap[NZ - 1 - row][col];
            switch (ch)
            {
                case 'C': return Zone.Condo;
                case 'O': return Zone.Office;
                case 'M': return Zone.Mamak;
                case 'D': return Zone.Dealer;
                case 'X': return Zone.ChowKit;
                case 'L': return Zone.Park;
                case 'V': return Zone.BatuCaves;
                case 'I': return Zone.IstanaNegara;
                case 'T': return Zone.TheanHou;
                default: return Zone.Shops;
            }
        }

        public City Build(Transform parent)
        {
            _city = new City();
            var root = new GameObject("City").transform;
            root.SetParent(parent, false);
            _city.root = root;
            _props = new GameObject("Props").transform;
            _props.SetParent(root, false);
            _ground = new StaticBatcher();
            _districts.Clear();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var times = new System.Text.StringBuilder();
            void Lap(string what) { times.Append($" {what} {clock.ElapsedMilliseconds}ms"); clock.Restart(); }

            var patches = new List<KLPatch>();
            foreach (var lp in Layout.patches)
            {
                var p = KLPatch.Load(lp.name);
                if (p != null) patches.Add(p);
            }
            Lap("load");
            CollectWater(patches);
            BuildRoads();
            Lap("roads");
            foreach (var p in patches) BuildPatch(p);           // first: its landmarks claim the named places
            Lap("patches");
            for (int c = 0; c < NX; c++)
                for (int r = 0; r < NZ; r++)
                {
                    var z = ZoneAt(c, r);
                    if (z != Zone.Patch) BuildBlock(c, r, z);
                }
            Lap("blocks");
            foreach (var rv in Layout.rivers) BuildRiver(rv);
            BuildLrt();
            BuildSkyline();
            BuildPhoneBooths();
            BuildStreetLife();
            BuildBorder();
            BuildParapets();
            _ground.Build(root, "Ground");
            Lap("ground");
            MergeStatics();
            Lap("merge");
            Debug.Log("[City] build times:" + times);

            float hx = NX * Pitch * 0.5f + 20, hz = NZ * Pitch * 0.5f + 20;
            _city.bounds = new Bounds(Vector3.zero, new Vector3(hx * 2, 400, hz * 2));
            return _city;
        }

        // ------------------------------------------------------------------ real-KL patches
        static readonly Dictionary<string, Color> PatchColors = new Dictionary<string, Color>
        {
            ["road"] = LatMaterials.Pal.Road, ["flyroad"] = LatMaterials.Pal.Road, ["mark_white"] = new Color(0.97f, 0.97f, 0.94f),
            ["walk"] = LatMaterials.Pal.Pavement, ["kerb"] = new Color(0.6f, 0.6f, 0.58f), ["land_paving"] = new Color(0.84f, 0.79f, 0.7f),
            ["land_park"] = LatMaterials.Pal.Park, ["land_lawn"] = LatMaterials.Pal.Park, ["land_forest"] = new Color(0.3f, 0.56f, 0.26f), ["land_pitch"] = new Color(0.46f, 0.78f, 0.34f),
            ["pond"] = LatMaterials.Pal.Water, ["water"] = LatMaterials.Pal.Water, ["bank"] = LatMaterials.Pal.Wall, ["bed"] = new Color(0.26f, 0.3f, 0.32f),
            ["deck"] = LatMaterials.Pal.Wall, ["rail"] = LatMaterials.Pal.Rail, ["barrier"] = new Color(0.9f, 0.88f, 0.84f),
            ["pillar"] = new Color(0.78f, 0.76f, 0.72f), ["guideway"] = new Color(0.88f, 0.87f, 0.83f),
            ["tree_trunk"] = new Color(0.45f, 0.3f, 0.2f), ["tree_leaf_a"] = new Color(0.24f, 0.55f, 0.22f),
            ["tree_leaf_b"] = new Color(0.36f, 0.65f, 0.27f), ["tree_leaf_c"] = new Color(0.28f, 0.6f, 0.36f),
            ["bld_pink"] = new Color(0.95f, 0.7f, 0.7f), ["bld_yellow"] = new Color(0.98f, 0.87f, 0.52f), ["bld_mint"] = new Color(0.66f, 0.88f, 0.74f),
            ["bld_blue"] = new Color(0.62f, 0.78f, 0.94f), ["bld_orange"] = new Color(0.98f, 0.7f, 0.46f), ["bld_cream"] = new Color(0.96f, 0.91f, 0.78f),
            ["bld_white"] = new Color(0.95f, 0.95f, 0.92f), ["bld_lilac"] = new Color(0.8f, 0.72f, 0.92f), ["bld_glass_blue"] = new Color(0.42f, 0.58f, 0.78f),
            ["bld_glass_teal"] = new Color(0.36f, 0.64f, 0.68f), ["bld_glass_grey"] = new Color(0.52f, 0.58f, 0.66f), ["bld_concrete"] = new Color(0.76f, 0.74f, 0.7f),
            ["roof_concrete"] = new Color(0.6f, 0.6f, 0.58f), ["roof_terracotta"] = new Color(0.76f, 0.38f, 0.26f), ["roof_green"] = new Color(0.32f, 0.6f, 0.44f),
            ["land_parking"] = new Color(0.5f, 0.51f, 0.54f), ["land_forecourt"] = new Color(0.74f, 0.74f, 0.72f), ["land_site"] = LatMaterials.Pal.Dirt,
            ["canopy"] = new Color(0.93f, 0.93f, 0.9f), ["post"] = new Color(0.7f, 0.7f, 0.72f),
        };
        static readonly HashSet<string> NoCollide = new HashSet<string> { "mark_white", "water", "tree_leaf_a", "tree_leaf_b", "tree_leaf_c" };
        static readonly HashSet<string> CastShadows = new HashSet<string> { "flyroad", "deck", "barrier", "pillar", "guideway", "canopy", "post", "tree_leaf_a", "tree_leaf_b", "tree_leaf_c" };

        /// <summary>
        /// Landmark models in the patches, fitted to their real footprint and height (x the map scale). The models'
        /// own proportions are cartoon (a stubby Sultan Abdul Samad, a sky-high minaret), so the two are fitted
        /// separately; 0 keeps the model's proportions.
        /// </summary>
        static readonly Dictionary<string, (float footprint, float height, float yaw)> LandmarkFit = new Dictionary<string, (float, float, float)>
        {
            // (yaws line the models up with their real footprints: Tools/klmap/probe_landmark_axes.py)
            ["MasjidJamek"] = (46f, 0f, -90f), ["SultanAbdulSamad"] = (92f, 44f, 101f), ["PasarSeni"] = (56f, 0f, 99f),
            ["Merdeka118"] = (0f, 440f, 0f), ["StadiumMerdeka"] = (140f, 30f, 23f), ["MasjidNegara"] = (96f, 58f, 22f),
            ["KLTower"] = (0f, 272f, 0f), ["Petronas"] = (0f, 294f, 140f), ["KLSentral"] = (118f, 80f, -28f),
            ["MuziumNegara"] = (70f, 26f, 0f), ["TuguNegara"] = (44f, 0f, 0f),
        };

        static readonly Dictionary<string, Texture2D> _facadeTex = new Dictionary<string, Texture2D>();

        /// <summary>Facade texture (Tools/klmap/gen_facades.py): one tile = one window bay x one storey.</summary>
        static Texture2D FacadeTex(string name)
        {
            if (!_facadeTex.TryGetValue(name, out var t) || t == null)
            {
                t = Resources.Load<Texture2D>("KLMap/Tex/" + name);
                if (t != null) { t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4; }
                _facadeTex[name] = t;
            }
            return t;
        }

        /// <summary>The material for one patch mesh: facades (win_/gls_ + wall colour, shop, lobby) are textured.</summary>
        static Material PatchMaterial(string key)
        {
            Texture2D tex = null;
            Color col = Color.white;
            int surface = SurfaceKinds.None;
            float gloss = 0f;
            if (key.StartsWith("win_") || key.StartsWith("gls_"))
            {
                bool glass = key.StartsWith("gls_");
                col = PatchColors.TryGetValue("bld_" + key.Substring(4), out var wc) ? wc : Color.white;
                tex = FacadeTex(glass ? "fac_glass" : "fac_win");
                surface = glass ? SurfaceKinds.None : SurfaceKinds.Classify(col);
                gloss = glass ? 0.45f : 0f;
            }
            else if (key == "shop") tex = FacadeTex("fac_shop");
            else if (key == "lobby") { tex = FacadeTex("fac_lobby"); col = new Color(0.94f, 0.95f, 0.97f); gloss = 0.3f; }
            if (tex != null) return LatMaterials.GetTextured(col, tex, surface, gloss);
            if (!PatchColors.TryGetValue(key, out col)) col = key.StartsWith("win_") || key.StartsWith("gls_") ? Color.white : new Color(0.85f, 0.8f, 0.75f);
            return LatMaterials.Get(col, 0f, key == "mark_white" ? SurfaceKinds.None : -1);
        }

        static bool IsBuildingMesh(string key) =>
            key.StartsWith("bld_") || key.StartsWith("roof_") || key.StartsWith("win_") || key.StartsWith("gls_") || key == "shop" || key == "lobby";

        /// <summary>Anchor name -> the place keys missions and the HUD use, and the district name.</summary>
        static readonly Dictionary<string, (string[] places, string district)> AnchorPlaces = new Dictionary<string, (string[], string)>
        {
            ["MasjidJamek"] = (new[] { "Masjid" }, "Masjid Jamek"), ["SultanAbdulSamad"] = (new[] { "SultanAbdulSamad" }, "Dataran Merdeka"),
            ["Dataran"] = (new[] { "Dataran", "DataranRoad" }, "Dataran Merdeka"), ["PasarSeni"] = (new[] { "PasarSeni" }, "Pasar Seni"),
            ["Petaling"] = (new[] { "Pasar", "PasarGate" }, "Petaling Street"), ["Merdeka118"] = (new[] { "Merdeka118" }, "Merdeka 118"),
            ["StadiumMerdeka"] = (new[] { "StadiumMerdeka" }, "Stadium Merdeka"), ["StadiumNegara"] = (new string[0], "Stadium Negara"),
            ["MasjidNegara"] = (new[] { "MasjidNegara" }, "Masjid Negara"), ["KLRailway"] = (new[] { "KLRailway" }, "Stesen Keretapi KL"),
            ["Maybank"] = (new string[0], "Jalan Tun Perak"), ["Dayabumi"] = (new string[0], "Dayabumi"),
            ["TuguNegara"] = (new[] { "TuguNegara" }, "Tugu Negara"), ["Perdana"] = (new[] { "TamanPerdana" }, "Taman Botani Perdana"),
            ["KLSentral"] = (new[] { "KLSentral" }, "KL Sentral"), ["MuziumNegara"] = (new[] { "MuziumNegara" }, "Muzium Negara"),
            ["Brickfields"] = (new[] { "Brickfields" }, "Brickfields"), ["KLTower"] = (new[] { "KLTower" }, "Bukit Nanas"),
            ["Petronas"] = (new[] { "Towers" }, "KLCC"), ["KLCCPark"] = (new[] { "Park" }, "KLCC"),
            ["Pavilion"] = (new[] { "Pavilion", "BukitBintang" }, "Bukit Bintang"),
        };

        static readonly List<(string patch, string name, Vector2 pos)> _districts = new List<(string, string, Vector2)>();

        void BuildPatch(KLPatch p)
        {
            var root = new GameObject("Patch_" + p.name).transform;
            root.SetParent(_city.root, false);
            var solids = new List<CombineInstance>();
            foreach (var md in p.meshes)
            {
                var mesh = new Mesh { name = p.name + "_" + md.key };
                if (md.v.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = md.v;
                mesh.normals = md.n;
                if (md.uv != null) mesh.uv = md.uv;
                mesh.triangles = md.t;
                mesh.RecalculateBounds();
                var go = new GameObject(md.key);
                go.transform.SetParent(root, false);
                go.isStatic = true;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = PatchMaterial(md.key);
                mr.shadowCastingMode = IsBuildingMesh(md.key) || CastShadows.Contains(md.key) ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                if (!NoCollide.Contains(md.key)) solids.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.identity });
            }
            PatchTrees(p, root, solids);
            if (solids.Count > 0)
            {
                var cm = new Mesh { name = p.name + "_collider", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                cm.CombineMeshes(solids.ToArray(), true, true, false);
                var cgo = new GameObject("Collider");
                cgo.transform.SetParent(root, false);
                cgo.AddComponent<MeshCollider>().sharedMesh = cm;
            }

            // the street graph, joined to the grid roads round the patch
            var nodes = new RoadNetwork.Node[p.nodes.Length];
            for (int i = 0; i < p.nodes.Length; i++) nodes[i] = _city.roads.Add(p.nodes[i], p.junction[i]);
            foreach (var e in p.edges)
            {
                if (e.dir == 0) RoadNetwork.Link(nodes[e.a], nodes[e.b]);
                else if (e.dir > 0) RoadNetwork.LinkOneWay(nodes[e.a], nodes[e.b]);
                else RoadNetwork.LinkOneWay(nodes[e.b], nodes[e.a]);
            }
            int joined = 0;
            foreach (int li in p.links)
            {
                var q = p.nodes[li];
                float dW = Mathf.Abs(q.x - p.rect.xMin), dE = Mathf.Abs(q.x - p.rect.xMax), dS = Mathf.Abs(q.z - p.rect.yMin), dN = Mathf.Abs(q.z - p.rect.yMax);
                float m = Mathf.Min(Mathf.Min(dW, dE), Mathf.Min(dS, dN));
                RoadNetwork.Node g;
                if (m == dW) g = GridAttach(new Vector3(p.rect.xMin - Road * 0.5f, 0, q.z), true);
                else if (m == dE) g = GridAttach(new Vector3(p.rect.xMax + Road * 0.5f, 0, q.z), true);
                else if (m == dS) g = GridAttach(new Vector3(q.x, 0, p.rect.yMin - Road * 0.5f), false);
                else g = GridAttach(new Vector3(q.x, 0, p.rect.yMax + Road * 0.5f), false);
                if (g == null) continue;
                RoadNetwork.Link(nodes[li], g);
                _streetJoins.Add(q);
                nodes[li].junction = true;
                joined++;
            }

            // pavement loops, mission pickup spots along them, coin trails along the streets
            for (int ri = 0; ri < p.rings.Count; ri++)
            {
                var ring = p.rings[ri];
                var z = new WalkZone(ring) { busy = p.ringKinds[ri] == 1 ? 2.5f : 1f };
                _city.walkZones.Add(z);
                for (float s = 20f; s < z.perimeter; s += 55f) _city.itemSpots.Add(z.PointAt(s, G + 0.6f));
            }
            for (int n = 0; n < p.edges.Length / 12; n++)
            {
                var e = p.edges[RI(0, p.edges.Length)];
                Vector3 a = p.nodes[e.a], b = p.nodes[e.b];
                if (a.y > 0.5f || b.y > 0.5f || Vector3.Distance(a, b) < 14f) continue;
                for (int k = 0; k < 4; k++) _city.coinSpots.Add(Vector3.Lerp(a, b, 0.2f + k * 0.2f) + Vector3.up);
            }

            // landmarks and named places
            foreach (var a in p.anchors)
            {
                var pos = new Vector3(a.pos.x, G, a.pos.y);
                var road = new Vector3(a.place.x, G, a.place.y);
                if (a.name == "Stall")
                {
                    // a night-market stall, facing the middle of the pedestrian street
                    var face = road - pos;
                    KitProp(a.model, pos, face.sqrMagnitude > 0.01f ? Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg : 0f);
                    continue;
                }
                if (!string.IsNullOrEmpty(a.model)) PatchLandmark(a.name, a.model, pos);
                if (AnchorPlaces.TryGetValue(a.name, out var info))
                {
                    // at the kerb on the landmark's side of the street: people stand there, cars pull up by it
                    var toward = pos - road; toward.y = 0;
                    var at = SidewalkNear(road + Vector3.ClampMagnitude(toward, 9f));
                    foreach (var key in info.places)
                    {
                        Place(key, at);
                        var look = pos - at; look.y = 0;
                        Face(key, look.sqrMagnitude > 1f ? Quaternion.LookRotation(look) : Quaternion.identity);
                    }
                    _districts.Add((p.name, info.district, a.pos));
                }
            }
            if (p.name == "KotaLama" && _city.places.TryGetValue("Pasar", out var pasar))
            {
                // the Chinatown arch over the top of Petaling Street
                var gate = Prop("Prop_ChinatownGate", SidewalkNear(pasar), 0, false);
                SetStatic(gate, true);
            }
            Debug.Log($"[KLMap] {p.name}: {p.meshes.Count} meshes, {p.nodes.Length} road nodes ({joined}/{p.links.Length} joined to the grid), " +
                      $"{p.rings.Count} pavement loops, {p.anchors.Count} anchors");
        }

        // ------------------------------------------------------------------ patch trees
        static Mesh _trunkMesh;
        static Mesh[] _crownMeshes;

        /// <summary>
        /// The park, forest and street trees of a patch (the file holds only where they stand): a six-sided trunk
        /// and a lumpy low-poly crown, the pipeline's cartoon tree, merged into one mesh per leaf shade.
        /// </summary>
        void PatchTrees(KLPatch p, Transform root, List<CombineInstance> solids)
        {
            if (p.trees.Count == 0) return;
            if (_trunkMesh == null) BuildTreeTemplates();
            int seed = 17;
            foreach (char ch in p.name) seed = seed * 31 + ch;
            var rng = new System.Random(seed);
            var trunks = new List<CombineInstance>();
            var leaves = new[] { new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>() };
            foreach (var t in p.trees)
            {
                float sc = t.z;
                int kind = Mathf.RoundToInt(t.w);
                float trunkH = (kind != 2 ? 2.6f : 4.2f) * sc, rt = 0.32f * sc;
                trunks.Add(new CombineInstance { mesh = _trunkMesh, transform = Matrix4x4.TRS(new Vector3(t.x, G, t.y), Quaternion.identity, new Vector3(rt, trunkH + 0.6f, rt)) });
                int shade = kind == 2 ? 2 : rng.NextDouble() < 0.55 ? 0 : 1;
                float rx = (kind == 0 ? 2.4f : 1.9f) * sc * (0.9f + 0.25f * (float)rng.NextDouble());
                float ry = rx * (kind == 0 ? 0.72f : 0.9f);
                var at = new Vector3(t.x, G + trunkH + ry * 0.8f, t.y);
                leaves[shade].Add(new CombineInstance
                {
                    mesh = _crownMeshes[rng.Next(_crownMeshes.Length)],
                    transform = Matrix4x4.TRS(at, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), new Vector3(rx, ry, rx)),
                });
            }
            Mesh Merge(string key, List<CombineInstance> parts, bool shadows)
            {
                if (parts.Count == 0) return null;
                var m = new Mesh { name = p.name + "_" + key, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.CombineMeshes(parts.ToArray(), true, true, false);
                var go = new GameObject(key);
                go.transform.SetParent(root, false);
                go.isStatic = true;
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = PatchMaterial(key);
                mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                return m;
            }
            var trunk = Merge("tree_trunk", trunks, false);
            if (trunk != null) solids.Add(new CombineInstance { mesh = trunk, transform = Matrix4x4.identity });
            Merge("tree_leaf_a", leaves[0], true);
            Merge("tree_leaf_b", leaves[1], true);
            Merge("tree_leaf_c", leaves[2], true);
        }

        static void BuildTreeTemplates()
        {
            // trunk: a unit hexagonal prism (radius 1, height 1), sides only, flat-shaded
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tri = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * Mathf.PI / 3f, a1 = (i + 1) * Mathf.PI / 3f;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var nn = ((p0 + p1) * 0.5f).normalized;
                int k = v.Count;
                v.AddRange(new[] { p0, p1, p1 + Vector3.up, p0 + Vector3.up });
                n.AddRange(new[] { nn, nn, nn, nn });
                tri.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            }
            _trunkMesh = new Mesh { name = "PatchTreeTrunk" };
            _trunkMesh.SetVertices(v); _trunkMesh.SetNormals(n); _trunkMesh.SetTriangles(tri, 0);
            _trunkMesh.RecalculateBounds();

            // crowns: an icosahedron with its corners pushed in and out a little, four variations
            float g = (1f + Mathf.Sqrt(5f)) / 2f;
            var ico = new[]
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0), new Vector3(0, -1, g), new Vector3(0, 1, g),
                new Vector3(0, -1, -g), new Vector3(0, 1, -g), new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
            };
            int[] faces = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var rng = new System.Random(1957);
            _crownMeshes = new Mesh[4];
            for (int c = 0; c < _crownMeshes.Length; c++)
            {
                var pts = new Vector3[ico.Length];
                for (int i = 0; i < ico.Length; i++) pts[i] = ico[i].normalized * (1f + ((float)rng.NextDouble() - 0.5f) * 0.24f);
                var cv = new List<Vector3>();
                var cn = new List<Vector3>();
                var ct = new List<int>();
                for (int f = 0; f < faces.Length; f += 3)
                {
                    Vector3 a = pts[faces[f]], b = pts[faces[f + 1]], d = pts[faces[f + 2]];
                    var nn = Vector3.Cross(b - a, d - a).normalized;
                    if (Vector3.Dot(nn, a + b + d) < 0) { var tmp = b; b = d; d = tmp; nn = -nn; }
                    int k = cv.Count;
                    cv.AddRange(new[] { a, b, d });
                    cn.AddRange(new[] { nn, nn, nn });
                    ct.AddRange(new[] { k, k + 1, k + 2 });
                }
                var m = new Mesh { name = "PatchTreeCrown" + c };
                m.SetVertices(cv); m.SetNormals(cn); m.SetTriangles(ct, 0);
                m.RecalculateBounds();
                _crownMeshes[c] = m;
            }
        }

        void PatchLandmark(string name, string model, Vector3 pos)
        {
            if (!LandmarkFit.TryGetValue(name, out var fit)) return;
            var go = ModelFactory.Spawn(model, pos, Quaternion.Euler(0, fit.yaw, 0), _props);
            go.isStatic = true;
            var b = ModelFactory.LocalBounds(go);
            float foot = Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z)), tall = Mathf.Max(0.01f, b.size.y);
            float s = fit.footprint > 0 ? fit.footprint / foot : fit.height / tall;        // across
            float sy = fit.height > 0 ? fit.height / tall : s;                            // up
            go.transform.localScale = new Vector3(s, sy, s);
            go.name = "Landmark_" + name;
            switch (name)
            {
                case "Petronas":
                {
                    // podium + the two shafts (model units, scaled with it)
                    var pod = go.AddComponent<BoxCollider>(); pod.center = new Vector3(0, 6, -6); pod.size = new Vector3(50, 12, 28);
                    foreach (float sx in new[] { -14f, 14f })
                    {
                        var cap = go.AddComponent<CapsuleCollider>();
                        cap.center = new Vector3(sx, 60, 0); cap.radius = 8.5f; cap.height = 120;
                    }
                    break;
                }
                case "KLTower":
                {
                    var cap = go.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 40, 0); cap.radius = 3.5f; cap.height = 80;
                    var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 1.5f, 0); bc.size = new Vector3(16, 3, 16);
                    break;
                }
                case "StadiumMerdeka":
                {
                    // an open bowl you can drive into: only its outer wall is solid (gap for the tunnel)
                    float rx = b.size.x * s * 0.47f, rz = b.size.z * s * 0.47f;
                    var turn = Quaternion.Euler(0, fit.yaw, 0);                 // the bowl is turned with the model
                    int n = 40;
                    for (int i = 0; i < n; i++)
                    {
                        float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                        var p0 = new Vector3(Mathf.Cos(a0) * rx, 0, Mathf.Sin(a0) * rz);
                        var p1 = new Vector3(Mathf.Cos(a1) * rx, 0, Mathf.Sin(a1) * rz);
                        var mid = (p0 + p1) * 0.5f;
                        if (mid.z > rz * 0.9f) continue;                  // the tunnel entrance
                        p0 = turn * p0; p1 = turn * p1; mid = turn * mid;
                        var w = new GameObject("StadiumWall");
                        w.transform.SetParent(_props, false);
                        w.transform.position = pos + mid + Vector3.up * 6f;
                        w.transform.rotation = Quaternion.LookRotation(p1 - p0);
                        w.AddComponent<BoxCollider>().size = new Vector3(2f, 12f, (p1 - p0).magnitude + 0.2f);
                    }
                    break;
                }
                default:
                    if (model.StartsWith("env_")) ModelFactory.UseProxyCollider(go);
                    else ModelFactory.AddBoundsCollider(go, 0.05f);
                    break;
            }
        }

        /// <summary>The nearest pavement-loop point to p (for signs, booths and carts beside a road place).</summary>
        Vector3 SidewalkNear(Vector3 p) => _city.Sidewalk(p);

        // ------------------------------------------------------------------ roads
        /// <summary>Place a KL street-kit module (static, no physics - colliders are separate boxes).</summary>
        GameObject Kit(string id, Vector3 pos, float yaw, float lengthScale = 1f, float widthScale = 1f)
        {
            var go = ModelFactory.Spawn(id, pos, Quaternion.Euler(0, yaw, 0), _kitRoot);
            go.transform.localScale = new Vector3(widthScale, 1f, lengthScale);
            SetStatic(go, true);
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        // ------------------------------------------------------------------ water (for bridges)
        readonly List<Vector2[]> _waterRings = new List<Vector2[]>();
        readonly List<Rect> _waterRects = new List<Rect>();

        void CollectWater(List<KLPatch> patches)
        {
            _waterRings.Clear();
            _waterRects.Clear();
            foreach (var p in patches) _waterRings.AddRange(p.water);
            foreach (var rv in Layout.rivers)
            {
                float x = BlockCenter(rv.col, 0).x;
                // from the road the channel enters under up to the far side of the last road it passes
                _waterRects.Add(Rect.MinMaxRect(x - rv.width * 0.5f, RoadZ(rv.r0) + Road * 0.5f, x + rv.width * 0.5f, RoadZ(rv.r1 + 1) + Road * 0.5f + 16f));
            }
        }

        bool IsWet(float x, float z)
        {
            foreach (var r in _waterRects) if (r.Contains(new Vector2(x, z))) return true;
            foreach (var ring in _waterRings) if (KLPatch.Inside(ring, x, z)) return true;
            return false;
        }

        // ------------------------------------------------------------------ roads
        /// <summary>A grid road segment is left out where both its sides are the same real-KL patch
        /// (the patch has its own streets there).</summary>
        static bool SamePatch(int c0, int r0, int c1, int r1)
        {
            var a = PatchAt(c0, r0);
            return a != null && a == PatchAt(c1, r1);
        }

        /// <summary>The north-south road on grid line i between rows k and k+1.</summary>
        public static bool NSRoad(int i, int k) => i >= 0 && i <= NX && k >= 0 && k < NZ && !(i > 0 && i < NX && SamePatch(i - 1, k, i, k));
        /// <summary>The east-west road on grid line k between columns i and i+1.</summary>
        public static bool EWRoad(int k, int i) => k >= 0 && k <= NZ && i >= 0 && i < NX && !(k > 0 && k < NZ && SamePatch(i, k - 1, i, k));

        static bool KampungOrRiver(int c, int r) => c < 0 || c >= NX || r < 0 || r >= NZ || IsKampungCell(c, r) || RiverAt(c, r) != null;
        // narrow kampung lanes: roads between kampung (and river) cells, touching the kampung
        static bool NSKampung(int i, int k) => KampungOrRiver(i - 1, k) && KampungOrRiver(i, k) && (IsKampungCell(i - 1, k) || IsKampungCell(i, k));
        static bool EWKampung(int k, int i) => (k == NZ || IsKampungCell(i, k)) && (k == 0 || IsKampungCell(i, k - 1));

        readonly Dictionary<(bool ns, int i, int k), List<RoadNetwork.Node>> _segChains = new Dictionary<(bool, int, int), List<RoadNetwork.Node>>();
        RoadNetwork.Node[,] _gridNodes;

        void BuildRoads()
        {
            _kitRoot = new GameObject("StreetKit").transform;
            _kitRoot.SetParent(_city.root, false);
            const int tiles = 8;                    // stretched 10 m tiles per block-long segment
            float seg = Block / tiles, stretch = seg / 10f;
            float half = Road * 0.5f;
            _segChains.Clear();

            // One stretch of road. City streets are two 2-lane tiles side by side (four lanes, the
            // tiles' edge lines meeting as a centre divide); kampung roads are a single narrow lane
            // tile with grass verges.
            void Straight(string id, Vector3 p, float yaw, bool kampung)
            {
                var across = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                if (kampung)
                {
                    Kit(id, p, yaw, stretch);
                    var size = Mathf.Approximately(yaw, 0f) ? new Vector3(Road, 0.02f, seg) : new Vector3(seg, 0.02f, Road);
                    _ground.Box(p + Vector3.up * 0.01f, size, LatMaterials.Pal.Grass, false, 0f);
                }
                else
                    foreach (float s in new[] { -1f, 1f }) Kit(id, p + across * s * half * 0.5f, yaw, stretch);
            }

            // --- intersections: the tile follows which of the four arms are there
            _gridNodes = new RoadNetwork.Node[NX + 1, NZ + 1];
            for (int i = 0; i <= NX; i++)
                for (int k = 0; k <= NZ; k++)
                {
                    bool overNS = UnderFlyover(i, k, true), overEW = UnderFlyover(i, k, false);
                    bool n = NSRoad(i, k) && !overNS, s = NSRoad(i, k - 1) && !overNS, e = EWRoad(k, i) && !overEW, w = EWRoad(k, i - 1) && !overEW;
                    int arms = (n ? 1 : 0) + (s ? 1 : 0) + (e ? 1 : 0) + (w ? 1 : 0);
                    if (arms == 0) continue;
                    var p = new Vector3(RoadX(i), 0, RoadZ(k));
                    bool kampung = (!n || NSKampung(i, k)) && (!s || NSKampung(i, k - 1)) && (!e || EWKampung(k, i)) && (!w || EWKampung(k, i - 1));
                    float sc = kampung ? 1f : Road / 10f;
                    string id; float yaw;
                    if (arms == 4) { id = "env_road_cross"; yaw = 0; }
                    else if (arms == 3) { id = "env_road_t"; yaw = !w ? 0 : !e ? 180 : !s ? -90 : 90; }
                    else if (arms == 2 && n && s) { id = "env_road_straight"; yaw = 0; }
                    else if (arms == 2 && e && w) { id = "env_road_straight"; yaw = 90; }
                    else if (arms == 2) { id = "env_road_corner"; yaw = n && e ? 0 : e && s ? -90 : s && w ? 180 : 90; }
                    else { id = "env_road_straight"; yaw = n || s ? 0 : 90; }
                    if (id == "env_road_corner")
                    {
                        // the kit's corner tile has its arms to +Z and +X at yaw 0
                        yaw = n && e ? 0 : e && s ? 90 : s && w ? 180 : -90;
                    }
                    Kit(id, p, yaw, sc, sc);
                    if (kampung) _ground.Box(p + Vector3.up * 0.01f, new Vector3(Road, 0.02f, Road), LatMaterials.Pal.Grass, false, 0f);
                    RoadColliders(p - new Vector3(half, 0, 0), p + new Vector3(half, 0, 0), true);
                    _gridNodes[i, k] = _city.roads.Add(p);
                }

            // --- north-south segments
            for (int i = 0; i <= NX; i++)
                for (int k = 0; k < NZ; k++)
                {
                    if (!NSRoad(i, k) || OnFlyover(true, i, k)) continue;
                    float x = RoadX(i);
                    bool kampung = NSKampung(i, k);
                    bool city = !kampung;
                    for (int t = 0; t < tiles; t++)
                    {
                        float z = RoadZ(k) + half + seg * (t + 0.5f);
                        string id = city && (t == 0 || t == tiles - 1) ? "env_crosswalk" : kampung ? "env_kb_lane" : "env_road_straight";
                        Straight(id, new Vector3(x, 0, z), 0, kampung);
                    }
                    RoadColliders(new Vector3(x, 0, RoadZ(k) + half), new Vector3(x, 0, RoadZ(k + 1) - half), false);
                    Chain(true, i, k, _gridNodes[i, k], _gridNodes[i, k + 1]);
                }
            // --- east-west segments
            for (int k = 0; k <= NZ; k++)
                for (int i = 0; i < NX; i++)
                {
                    if (!EWRoad(k, i) || OnFlyover(false, i, k)) continue;
                    float z = RoadZ(k);
                    bool kampung = EWKampung(k, i);
                    for (int t = 0; t < tiles; t++)
                    {
                        float x = RoadX(i) + half + seg * (t + 0.5f);
                        Straight(kampung ? "env_kb_lane" : t == 0 || t == tiles - 1 ? "env_crosswalk" : "env_road_straight",
                            new Vector3(x, 0, z), 90, kampung);
                    }
                    RoadColliders(new Vector3(RoadX(i) + half, 0, z), new Vector3(RoadX(i + 1) - half, 0, z), true);
                    Chain(false, i, k, _gridNodes[i, k], _gridNodes[i + 1, k]);
                }
            _city.roads.laneOffset = 2.6f;          // the inner lane each way (kampung lanes are 5 m too)
            BuildFlyovers();
            BuildRoundabouts();

            // coins along some roads, H&R style trails
            var segs = new List<(bool ns, int i, int k)>(_segChains.Keys);
            for (int n = 0; n < 60 && segs.Count > 0; n++)
            {
                var (ns, i, k) = segs[RI(0, segs.Count)];
                float lane = _rng.NextDouble() < 0.5 ? -2.6f : 2.6f;
                float start = R(half + 8, Block - 20);
                for (int c = 0; c < 5; c++)
                {
                    var p = ns ? new Vector3(RoadX(i) + lane, 1f, RoadZ(k) + start + c * 4f) : new Vector3(RoadX(i) + start + c * 4f, 1f, RoadZ(k) + lane);
                    _city.coinSpots.Add(p);
                }
            }
        }

        void Chain(bool ns, int i, int k, RoadNetwork.Node a, RoadNetwork.Node b)
        {
            if (a == null || b == null) return;
            RoadNetwork.Link(a, b);
            _segChains[(ns, i, k)] = new List<RoadNetwork.Node> { a, b };
        }

        /// <summary>A node on the grid road at p (splitting the segment), for a patch street to join.</summary>
        RoadNetwork.Node GridAttach(Vector3 p, bool ns)
        {
            int i = ns ? Mathf.RoundToInt((p.x - X0) / Pitch) : Mathf.FloorToInt((p.x - X0) / Pitch);
            int k = ns ? Mathf.FloorToInt((p.z - Z0) / Pitch) : Mathf.RoundToInt((p.z - Z0) / Pitch);
            // right by an intersection: use it
            int ji = Mathf.RoundToInt((p.x - X0) / Pitch), jk = Mathf.RoundToInt((p.z - Z0) / Pitch);
            if (ji >= 0 && ji <= NX && jk >= 0 && jk <= NZ && _gridNodes[ji, jk] != null &&
                Vector2.Distance(new Vector2(p.x, p.z), new Vector2(RoadX(ji), RoadZ(jk))) < Road * 0.75f)
                return _gridNodes[ji, jk];
            if (!_segChains.TryGetValue((ns, i, k), out var chain)) return null;
            float key = ns ? p.z : p.x;
            for (int n = 0; n < chain.Count - 1; n++)
            {
                float a = ns ? chain[n].pos.z : chain[n].pos.x, b = ns ? chain[n + 1].pos.z : chain[n + 1].pos.x;
                if ((key - a) * (key - b) > 0) continue;
                if (Mathf.Abs(key - a) < 2f) return chain[n];
                if (Mathf.Abs(key - b) < 2f) return chain[n + 1];
                var node = _city.roads.Split(chain[n], chain[n + 1], ns ? new Vector3(RoadX(i), 0, p.z) : new Vector3(p.x, 0, RoadZ(k)));
                chain.Insert(n + 1, node);
                return node;
            }
            return null;
        }

        /// <summary>Colliders under a stretch of grid road: solid ground where it's dry, a bridge deck
        /// (with parapets) where a river runs under it.</summary>
        void RoadColliders(Vector3 a, Vector3 b, bool alongX)
        {
            float len = Vector3.Distance(a, b);
            if (len < 0.1f) return;
            var dir = (b - a) / len;
            var across = alongX ? Vector3.forward : Vector3.right;
            int n = Mathf.CeilToInt(len);
            bool wetRun = false;
            float runStart = 0f;
            for (int s = 0; s <= n; s++)
            {
                float t = Mathf.Min(s, len);
                var p = a + dir * t;
                bool wet = s < n && (IsWet(p.x, p.z) || IsWet(p.x + across.x * 7f, p.z + across.z * 7f) || IsWet(p.x - across.x * 7f, p.z - across.z * 7f));
                if (s == 0) { wetRun = wet; continue; }
                if (wet != wetRun || s == n)
                {
                    RoadRun(a + dir * runStart, a + dir * t, wetRun, alongX);
                    wetRun = wet;
                    runStart = t;
                }
            }
        }

        void RoadRun(Vector3 p0, Vector3 p1, bool wet, bool alongX)
        {
            var c = (p0 + p1) * 0.5f;
            float L = Vector3.Distance(p0, p1) + 0.02f;
            if (!wet)
            {
                _ground.ColliderOnly(c + Vector3.down * 1.5f, alongX ? new Vector3(L, 3f, Road) : new Vector3(Road, 3f, L));
                return;
            }
            // bridge: deck + underside; the parapets along both edges go in last (BuildParapets), with
            // gaps where a real-KL street runs onto the bridge
            var deck = alongX ? new Vector3(L, 1f, Road) : new Vector3(Road, 1f, L);
            _ground.ColliderOnly(c + Vector3.down * 0.5f, deck);
            _ground.Box(c + Vector3.down * 0.6f, alongX ? new Vector3(L, 0.5f, Road) : new Vector3(Road, 0.5f, L), LatMaterials.Pal.Wall, false, 1.4f);
            foreach (float side in new[] { -1f, 1f })
            {
                var off = (alongX ? Vector3.forward : Vector3.right) * side * (Road * 0.5f - 0.2f);
                _parapets.Add((p0 + off, p1 + off));
            }
        }

        readonly List<(Vector3 a, Vector3 b)> _parapets = new List<(Vector3, Vector3)>();
        readonly List<Vector3> _streetJoins = new List<Vector3>();

        void BuildParapets()
        {
            foreach (var (a, b) in _parapets)
            {
                float len = Vector3.Distance(a, b);
                if (len < 0.1f) continue;
                var dir = (b - a) / len;
                bool alongX = Mathf.Abs(dir.x) > 0.5f;
                // in pieces, skipping any within 7 m of a street that joins here
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 2f));
                float piece = len / n;
                float runStart = -1f;
                for (int i = 0; i <= n; i++)
                {
                    bool open = i < n && Blocked(a + dir * (piece * (i + 0.5f)));
                    if (i < n && !open) { if (runStart < 0f) runStart = piece * i; continue; }
                    if (runStart >= 0f)
                    {
                        float from = runStart, to = piece * i;
                        var c = a + dir * ((from + to) * 0.5f);
                        _ground.Box(c + Vector3.up * 0.55f, alongX ? new Vector3(to - from, 1.1f, 0.35f) : new Vector3(0.35f, 1.1f, to - from), LatMaterials.Pal.Rail, true, 2f);
                        runStart = -1f;
                    }
                }
            }
            bool Blocked(Vector3 p)
            {
                foreach (var j in _streetJoins)
                    if ((j.x - p.x) * (j.x - p.x) + (j.z - p.z) * (j.z - p.z) < 7f * 7f) return true;
                return false;
            }
        }

        /// <summary>Kerb + sidewalk strips all round a block (striped KL kerbs), 3 m deep.</summary>
        void Kerbs(Vector3 c, string edge = "env_curb_edge")
        {
            float h = Block * 0.5f, seg = Block / 8f, stretch = seg / 10f;
            void Piece(Vector3 at, float yaw)
            {
                if (!NearRoundabout(at, RingOuter + 4f)) Kit(edge, at, yaw, stretch);
            }
            for (int t = 0; t < 8; t++)
            {
                float a = -h + seg * (t + 0.5f);
                Piece(c + new Vector3(-h + 1.5f, 0, a), 0);      // west side (road to -X)
                Piece(c + new Vector3(h - 1.5f, 0, a), 180);     // east
                Piece(c + new Vector3(a, 0, h - 1.5f), 90);      // north
                Piece(c + new Vector3(a, 0, -h + 1.5f), -90);    // south
            }
        }

        // ------------------------------------------------------------------ river channels + border
        /// <summary>A straight river channel up a column: the water, River-of-Life promenades on both
        /// banks between the roads, railings and walls. Grid roads cross it on bridges.</summary>
        void BuildRiver(LayoutRiver rv)
        {
            float x = BlockCenter(rv.col, 0).x;
            float w = rv.width;
            float z0 = RoadZ(rv.r0) + Road * 0.5f, z1 = RoadZ(rv.r1 + 1) + Road * 0.5f + 14f;
            _ground.Box(new Vector3(x, -3.4f, (z0 + z1) * 0.5f), new Vector3(w + 2, 2f, z1 - z0), LatMaterials.Pal.Water, true, 0f);
            Place("River", new Vector3(x, -2.4f, (z0 + z1) * 0.5f));
            float wl = x - w * 0.5f, wr = x + w * 0.5f;                 // water edges
            float bl = x - Block * 0.5f, br = x + Block * 0.5f;         // road kerbs
            float pw = (Block - w) * 0.5f;                              // promenade width
            for (int k = rv.r0; k <= rv.r1; k++)
            {
                float a = RoadZ(k) + Road * 0.5f, b = RoadZ(k + 1) - Road * 0.5f, zc = (a + b) * 0.5f;
                foreach (float s in new[] { -1f, 1f })
                {
                    float px = s < 0 ? (bl + wl) * 0.5f : (wr + br) * 0.5f;
                    _ground.Box(new Vector3(px, G - 1.6f, zc), new Vector3(pw, 3.2f, b - a), LatMaterials.Pal.Pavement, true, 1.2f);
                    _ground.Box(new Vector3(px + s * pw * 0.2f, G + 0.01f, zc), new Vector3(pw * 0.45f, 0.02f, b - a - 6f), LatMaterials.Pal.Grass, false, 0f);
                    _city.walkZones.Add(WalkZone.FromRect(new Rect(px - pw * 0.5f + 0.5f, a + 1f, pw - 1f, b - a - 2f), 1f));
                    for (int i = 0; i < 5; i++)
                    {
                        float pz = a + 10f + i * (b - a - 20f) / 4f;
                        KitProp(i % 2 == 0 ? "env_kb_rain_tree" : "env_palm", new Vector3(px + s * pw * 0.2f, G, pz), R(0, 360), R(0.9f, 1.15f));
                        if (i % 2 == 0) KitProp("env_pbg_bench", new Vector3(px - s * pw * 0.2f, G, pz + 4f), s < 0 ? 90 : -90);
                        var lamp = Prop("env_lamp_post", new Vector3((s < 0 ? wl : wr) - s * 1.2f, G, pz + 8f), s < 0 ? 90 : -90, false);
                        var cap = lamp.AddComponent<CapsuleCollider>();
                        cap.center = new Vector3(0, 3.5f, 0); cap.radius = 0.18f; cap.height = 7;
                    }
                }
                AddRail(wl, wr, a, b);
                Place($"Bridge{k}", new Vector3(x, 0.2f, RoadZ(k)));
            }
            Place($"Bridge{rv.r1 + 1}", new Vector3(x, 0.2f, RoadZ(rv.r1 + 1)));
            // the channel walls down to the water
            foreach (float s in new[] { -1f, 1f })
                _ground.Box(new Vector3(x + s * (w * 0.5f + 0.5f), -1.9f, (z0 + z1) * 0.5f), new Vector3(1f, 3.6f, z1 - z0), LatMaterials.Pal.Wall, true, 1.2f);
            // a few floating things so the river reads as water
            for (int i = 0; i < 10; i++)
                _ground.Box(new Vector3(x + R(-w * 0.35f, w * 0.35f), -2.35f, R(z0 + 5, z1 - 5)), new Vector3(R(1, 3), 0.05f, 0.15f), LatMaterials.Pal.RoadLine, false, 0f,
                    Quaternion.Euler(0, R(0, 180), 0));
        }

        void AddRail(float rl, float rr, float z0, float z1)
        {
            if (z1 - z0 < 1f) return;
            foreach (float x in new[] { rl + 0.25f, rr - 0.25f })
                _ground.Box(new Vector3(x, -0.65f, (z0 + z1) * 0.5f), new Vector3(0.5f, 3.5f, z1 - z0), LatMaterials.Pal.Rail, true, 2f);
        }

        void BuildBorder()
        {
            float hx = NX * Pitch * 0.5f + Road * 0.5f, hz = NZ * Pitch * 0.5f + Road * 0.5f;
            const float strip = 14f;
            var pave = LatMaterials.Pal.Pavement;
            // pavement strip around the map
            foreach (float sz in new[] { -1f, 1f })
                _ground.Box(new Vector3(0, -1.4f, sz * (hz + strip * 0.5f)), new Vector3(hx * 2 + strip * 2, 3.2f, strip), pave, true, 1.2f);
            foreach (float sx in new[] { -1f, 1f })
                _ground.Box(new Vector3(sx * (hx + strip * 0.5f), -1.4f, 0), new Vector3(strip, 3.2f, hz * 2), pave, true, 1.2f);

            // tall invisible walls + a visible low wall
            float wx = hx + strip, wz = hz + strip;
            var wall = LatMaterials.Pal.Wall;
            _ground.Box(new Vector3(0, 1f, wz), new Vector3(wx * 2, 2f, 1f), wall, true, 2f);
            _ground.Box(new Vector3(0, 1f, -wz), new Vector3(wx * 2, 2f, 1f), wall, true, 2f);
            _ground.Box(new Vector3(wx, 1f, 0), new Vector3(1f, 2f, wz * 2), wall, true, 2f);
            _ground.Box(new Vector3(-wx, 1f, 0), new Vector3(1f, 2f, wz * 2), wall, true, 2f);
            var inv = new GameObject("InvisibleWalls").transform;
            inv.SetParent(_city.root, false);
            void Inv(Vector3 c, Vector3 s) { var bc = inv.gameObject.AddComponent<BoxCollider>(); bc.center = c; bc.size = s; }
            Inv(new Vector3(0, 20, wz), new Vector3(wx * 2, 40, 1));
            Inv(new Vector3(0, 20, -wz), new Vector3(wx * 2, 40, 1));
            Inv(new Vector3(wx, 20, 0), new Vector3(1, 40, wz * 2));
            Inv(new Vector3(-wx, 20, 0), new Vector3(1, 40, wz * 2));

            // barriers across the road ends
            for (int i = 0; i <= NX; i++)
                foreach (float s in new[] { -1f, 1f })
                    Prop("Prop_Barrier", new Vector3(RoadX(i), 0, s * (hz + 1f)), 0, true);
            for (int k = 0; k <= NZ; k++)
                foreach (float s in new[] { -1f, 1f })
                    Prop("Prop_Barrier", new Vector3(s * (hx + 1f), 0, RoadZ(k)), 90, true);

            // distant skyline "cut-outs" like the background of a drawing
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                float d = (i % 2 == 0 ? wz : wx) + 120f;
                var go = Prop("Prop_Skyline", dir * d, a + 180f, false);
                go.transform.localScale = new Vector3(7.5f, 2.4f, 1f);
                var go2 = Prop("Prop_Skyline", dir * (d + 90f) + Quaternion.Euler(0, a, 0) * Vector3.right * 200f, a + 180f, false);
                go2.transform.localScale = new Vector3(6f, 3.2f, 1f);
            }
        }

        // ------------------------------------------------------------------ blocks
        /// <summary>Landmarks own their whole block (laid out at LandmarkScale); everything else is
        /// four lots of district buildings.</summary>
        static bool IsLandmark(Zone z) => z switch
        {
            Zone.KLSentral or Zone.MuziumNegara or Zone.MasjidNegara or Zone.PerdanaGardens or Zone.BatuCaves or
            Zone.TuguNegara or Zone.Pavilion or Zone.Merdeka118 or Zone.StadiumMerdeka or Zone.TheanHou or
            Zone.IstanaNegara or Zone.Towers or Zone.KLTower or Zone.Dataran or Zone.Masjid or Zone.Padang => true,
            _ => false,
        };

        /// <summary>What goes on each lot of a district block: the district's special building on
        /// one lot (north lots come first, so named places land on the main road), the rest filler.</summary>
        static Zone LotZone(Zone z, int lot)
        {
            switch (z)
            {
                case Zone.Home: return lot == 1 ? Zone.Home : Zone.Kampung;           // north-east: faces the river road
                case Zone.Surau: return lot == 0 ? Zone.Surau : Zone.Kampung;
                case Zone.Mamak: return lot == 0 ? Zone.Mamak : Zone.Shops;
                case Zone.Pasar: return lot == 0 || lot == 1 ? Zone.Pasar : Zone.Shops; // Petaling Street runs through
                case Zone.PasarSeni: return lot == 3 ? Zone.PasarSeni : Zone.Shops;
                case Zone.Dealer: return lot == 0 ? Zone.Dealer : Zone.Shops;
                default: return z;                                                   // shops, condos, kampung, Chow Kit...
            }
        }

        // lot order: north-west, north-east, south-west, south-east
        static readonly Vector2[] LotOffsets =
        {
            new Vector2(-1, 1), new Vector2(1, 1), new Vector2(-1, -1), new Vector2(1, -1),
        };

        void BuildBlock(int col, int row, Zone zone)
        {
            var c = BlockCenter(col, row);
            if (zone == Zone.River) return;
            bool kampung = IsKampungCell(col, row);
            bool landmark = IsLandmark(zone);
            Color top = kampung ? LatMaterials.Pal.Grass : LatMaterials.Pal.Pavement;
            if (zone == Zone.Park || zone == Zone.KLTower || zone == Zone.PerdanaGardens || zone == Zone.BatuCaves ||
                zone == Zone.TuguNegara || zone == Zone.IstanaNegara || zone == Zone.Padang) top = LatMaterials.Pal.Park;
            // block: striped kerbs + 3 m sidewalks from the kit, the inside filled with grass or paving
            _ground.ColliderOnly(c + new Vector3(0, G - 1.6f, 0), new Vector3(Block, 3.2f, Block));
            _ground.Box(c + new Vector3(0, G - 1.6f, 0), new Vector3(Block - 5.9f, 3.2f, Block - 5.9f), top, false, 0f);
            _ground.Box(c + new Vector3(0, -1.6f, 0), new Vector3(Block, 3.0f, Block), LatMaterials.Pal.Wall, false, 0f);
            Kerbs(c, zone == Zone.Brickfields ? "env_bf_curb_edge" : "env_curb_edge");

            if (landmark)
            {
                _city.walkZones.Add(WalkZone.FromRect(new Rect(c.x - Block * 0.5f + 1.5f, c.z - Block * 0.5f + 1.5f, Block - 3f, Block - 3f), 1.5f));
                _ls = LandmarkScale;
                BuildZone(c, zone);
                _ls = 1f;
                return;
            }

            // the back lanes between the four lots: a sealed lorong in town, a sandy path in the kampung
            var laneColor = kampung ? LatMaterials.Pal.Dirt : LatMaterials.Pal.Road;
            _ground.Box(c + new Vector3(0, G + 0.01f, 0), new Vector3(Lane, 0.02f, Block - 6f), laneColor, false, 0f);
            _ground.Box(c + new Vector3(0, G + 0.012f, 0), new Vector3(Block - 6f, 0.02f, Lane), laneColor, false, 0f);
            for (int lot = 0; lot < 4; lot++)
            {
                var lc = c + new Vector3(LotOffsets[lot].x, 0, LotOffsets[lot].y) * ((Lot + Lane) * 0.5f);
                if (RoundaboutLot(col, row, lot))
                {
                    RoundaboutGarden(lc, col + (LotOffsets[lot].x > 0 ? 1 : 0), row + (LotOffsets[lot].y > 0 ? 1 : 0));
                    continue;
                }
                var wz = WalkZone.FromRect(new Rect(lc.x - 20.5f, lc.z - 20.5f, 41f, 41f));
                wz.kampung = kampung;
                _city.walkZones.Add(wz);
                var lz = LotZone(zone, lot);
                BuildZone(lc, lz);
                if (!kampung && lz != Zone.Park && lz != Zone.ChowKit && lz != Zone.Brickfields)
                    StreetLamps(lc);
            }
        }

        void BuildZone(Vector3 c, Zone zone)
        {
            switch (zone)
            {
                case Zone.Kampung: KampungBlock(c); break;
                case Zone.Home: HomeBlock(c); break;
                case Zone.Surau: SurauBlock(c); break;
                case Zone.Padang: PadangBlock(c); break;
                case Zone.Shops: ShopBlock(c, true); break;
                case Zone.ChowKit: ChowKitBlock(c); break;
                case Zone.Condo: CondoBlock(c); break;
                case Zone.Office: OfficeBlock(c); break;
                case Zone.Masjid: MasjidBlock(c); break;
                case Zone.Mamak: MamakBlock(c); break;
                case Zone.Pasar: PasarBlock(c); break;
                case Zone.Towers: TowersBlock(c); break;
                case Zone.Park: ParkBlock(c); break;
                case Zone.KLTower: KLTowerBlock(c); break;
                case Zone.Dataran: DataranBlock(c); break;
                case Zone.Dealer: DealerBlock(c); break;
                case Zone.BukitBintang: ShopBlock(c, true); Place("BukitBintang", c + new Vector3(-21, G, 0)); break;
                case Zone.PasarSeni: PasarSeniBlock(c); break;
                case Zone.Brickfields: BrickfieldsBlock(c); break;
                case Zone.KLSentral: KLSentralBlock(c); break;
                case Zone.MuziumNegara: MuziumNegaraBlock(c); break;
                case Zone.PerdanaGardens: PerdanaGardensBlock(c); break;
                case Zone.MasjidNegara: MasjidNegaraBlock(c); break;
                case Zone.BatuCaves: BatuCavesBlock(c); break;
                case Zone.TuguNegara: Landmark(c, "env_lm2_tugu_negara", "TuguNegara", "TUGU NEGARA", 0); break;
                case Zone.Pavilion: Landmark(c, "env_lm2_pavilion", "Pavilion", "PAVILION", 0); break;
                case Zone.Merdeka118: Landmark(c, "env_lm2_merdeka118", "Merdeka118", "MERDEKA 118", 0); break;
                case Zone.StadiumMerdeka: StadiumBlock(c); break;
                case Zone.TheanHou: Landmark(c, "env_lm2_thean_hou", "TheanHou", "TOKONG THEAN HOU", 0); break;
                case Zone.IstanaNegara: Landmark(c, "env_lm2_istana_negara", "IstanaNegara", "ISTANA NEGARA", 0); break;
            }
        }

        // layout scale: 1 on a lot, LandmarkScale while a landmark lays out its block
        float _ls = 1f;
        /// <summary>A block-layout offset (x, z scaled by the layout scale; height as given).</summary>
        Vector3 V(float x, float y, float z) => new Vector3(x * _ls, y, z * _ls);

        /// <summary>Named places are first-come: a district's special lot sets them, filler lots
        /// of the same kind (Chow Kit, Brickfields...) don't move them.</summary>
        void Place(string key, Vector3 p) { if (!_city.places.ContainsKey(key)) _city.places[key] = p; }
        void Face(string key, Quaternion q) { if (!_city.facings.ContainsKey(key)) _city.facings[key] = q; }

        const float G = 0.18f; // block top height = kerb height of the street kit

        /// <summary>True inside a road band (plus a 1.2 m kerb margin) - props must not stand there.</summary>
        static bool OnRoad(Vector3 p)
        {
            float gx = Mathf.Repeat(p.x - X0 + Road * 0.5f, Pitch), gz = Mathf.Repeat(p.z - Z0 + Road * 0.5f, Pitch);
            const float margin = 1.2f;
            return gx < Road + margin || gx > Pitch - margin || gz < Road + margin || gz > Pitch - margin;
        }

        GameObject Prop(string model, Vector3 pos, float yaw, bool collider, float scale = 1f)
        {
            var go = ModelFactory.Spawn(model, pos, Quaternion.Euler(0, yaw, 0), _props);
            go.transform.localScale = Vector3.one * scale;
            SetStatic(go, true);
            if (collider) ModelFactory.AddBoundsCollider(go, 0.05f);
            // small clutter (crates, stools, bollards, pots...) is only drawn near the camera
            var b = ModelFactory.LocalBounds(go);
            if (Mathf.Max(b.size.x, b.size.y, b.size.z) * scale < 3.2f)
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layers.Detail;
            return go;
        }

        GameObject Tree(string model, Vector3 pos, float scale = 1f)
        {
            var go = ModelFactory.Spawn(model, pos, Quaternion.Euler(0, R(0, 360), 0), _props);
            go.transform.localScale = Vector3.one * scale;
            SetStatic(go, true);
            var cap = go.AddComponent<CapsuleCollider>();
            cap.radius = model == "Prop_RainTree" || model == "Prop_Angsana" ? 0.55f : 0.32f;
            cap.height = 6f;
            cap.center = new Vector3(0, 3f, 0);
            return go;
        }

        void Scatter(Vector3 c, int count, string[] models, List<Vector2> avoid, float avoidR, float range = 17f)
        {
            for (int i = 0; i < count; i++)
            {
                for (int tries = 0; tries < 12; tries++)
                {
                    var p = new Vector2(R(-range, range), R(-range, range));
                    bool ok = true;
                    foreach (var a in avoid) if ((a - p).sqrMagnitude < avoidR * avoidR) { ok = false; break; }
                    if (!ok) continue;
                    avoid.Add(p);
                    Tree(models[RI(0, models.Length)], c + new Vector3(p.x, G, p.y), R(0.85f, 1.2f));
                    break;
                }
            }
        }

        static readonly string[] KbHouses = { "env_kb_house_teal", "env_kb_house_mint", "env_kb_house_blue", "env_kb_house_small" };

        /// <summary>A KL kit prop that uses its own COL_ proxy (trees, pots, stalls).</summary>
        GameObject KitProp(string model, Vector3 pos, float yaw, float scale = 1f)
        {
            var go = Prop(model, pos, yaw, false, scale);
            ModelFactory.UseProxyCollider(go);
            return go;
        }

        void KampungBlock(Vector3 c)
        {
            var houses = KbHouses;
            var used = new List<Vector2>();
            foreach (var off in new[] { new Vector2(-9, -9), new Vector2(9, -9), new Vector2(-9, 9), new Vector2(9, 9) })
            {
                if (_rng.NextDouble() < 0.25) continue;
                var p = off + new Vector2(R(-1.5f, 1.5f), R(-1.5f, 1.5f));
                // face the nearest road
                float yaw = Mathf.Abs(off.x) > Mathf.Abs(off.y) ? (off.x > 0 ? 90 : -90) : (off.y > 0 ? 0 : 180);
                if (_rng.NextDouble() < 0.5) yaw = off.y > 0 ? 0 : 180;
                float hy = yaw + R(-6, 6);
                KitProp(houses[RI(0, houses.Length)], c + new Vector3(p.x, G, p.y), hy);
                // a pot of flowers by the stairs and a banana clump beside the house
                var front = Quaternion.Euler(0, hy, 0);
                KitProp(_rng.NextDouble() < 0.5 ? "env_kb_flower_pot" : "env_kb_flower_pot_white",
                    c + new Vector3(p.x, G, p.y) + front * new Vector3(1.4f, 0, 5.6f), R(0, 360));
                KitProp("env_kb_banana_plant", c + new Vector3(p.x, G, p.y) + front * new Vector3(-5.6f, 0, R(-2, 3)), R(0, 360));
                used.Add(p);
                _city.itemSpots.Add(c + new Vector3(p.x + R(-6, 6), G + 0.6f, p.y + (off.y > 0 ? 8 : -8)));
            }
            // picket fences along the inside of the sidewalk, with gaps for the paths
            foreach (float sz in new[] { -1f, 1f })
                for (int i = 0; i < 6; i++)
                {
                    if (i == 1 || i == 4) continue;
                    KitProp("env_kb_fence", c + new Vector3(-13.75f + i * 5.5f, G, sz * 18.7f), sz > 0 ? 0 : 180);
                }
            foreach (var t in new[] { new Vector2(0, 0), new Vector2(R(-4, 4), R(-4, 4)) })
            {
                if (_rng.NextDouble() < 0.5) continue;
                KitProp("env_kb_rain_tree", c + new Vector3(t.x, G, t.y), R(0, 360), R(0.85f, 1.1f));
                used.Add(t);
            }
            for (int i = 0; i < RI(3, 6); i++)
            {
                var p = new Vector2(R(-16, 16), R(-16, 16));
                bool ok = true;
                foreach (var a in used) if ((a - p).sqrMagnitude < 36f) { ok = false; break; }
                if (!ok) continue;
                KitProp(_rng.NextDouble() < 0.4 ? "env_palm" : _rng.NextDouble() < 0.5 ? "env_kb_shrub" : "env_kb_banana_plant",
                    c + new Vector3(p.x, G, p.y), R(0, 360), R(0.9f, 1.15f));
                used.Add(p);
            }
            for (int i = 0; i < 4; i++) _city.coinSpots.Add(c + new Vector3(R(-15, 15), 1f, R(-15, 15)));
        }

        void HomeBlock(Vector3 c)
        {
            // Rumah Pak Mat: facing east toward the river road
            var home = KitProp("env_kb_house_teal", c + new Vector3(-4, G, 0), 90);
            home.name = "RumahPakMat";
            KitProp("env_kb_rain_tree", c + new Vector3(-8, G, 13), R(0, 360), 1.2f);
            KitProp("env_palm", c + new Vector3(10, G, -14), 0);
            KitProp("env_palm", c + new Vector3(12, G, 12), 70);
            KitProp("env_kb_banana_plant", c + new Vector3(-14, G, -12), 30);
            KitProp("env_kb_banana_plant", c + new Vector3(-16, G, -6), 200);
            KitProp("env_kb_flower_pot", c + new Vector3(2.4f, G, 1.6f), 0);
            KitProp("env_kb_flower_pot_white", c + new Vector3(2.4f, G, -1.6f), 0);
            // the verandah/stairs of a 90deg house point +X
            Place("Home", c + new Vector3(8, G, 0));
            Face("Home", Quaternion.Euler(0, 90, 0));
            Place("HomeVerandah", c + new Vector3(3.2f, G, -2.8f));   // at the foot of the stairs
            Place("HomeCar", c + new Vector3(12, G + 0.3f, 7));
            Face("HomeCar", Quaternion.Euler(0, 90, 0)); // nose toward the road
            Place("HomeYard", c + new Vector3(10, G, -6));
            Place("PlayerSpawn", c + new Vector3(9, G + 0.1f, 2));
            Face("PlayerSpawn", Quaternion.Euler(0, 90, 0));
        }

        void SurauBlock(Vector3 c)
        {
            Prop("Bld_Surau", c + new Vector3(0, G, 2), 180, true);
            Scatter(c, 6, new[] { "Prop_Palm", "Prop_Palm2" }, new List<Vector2> { new Vector2(0, 2) }, 9f);
            Place("Surau", c + new Vector3(0, G, -6));
        }

        void PadangBlock(Vector3 c)
        {
            // kampung football field: a full-size pitch (real 7.3 m goals), halfway line and touchlines
            float len = 30f * _ls, wid = 22f * _ls;
            foreach (float s in new[] { -1f, 1f })
            {
                var gz = c.z + s * len * 0.5f;
                _ground.Box(new Vector3(c.x - 3.66f, 1.22f, gz), new Vector3(0.2f, 2.44f, 0.2f), LatMaterials.Pal.RoadLine, true);
                _ground.Box(new Vector3(c.x + 3.66f, 1.22f, gz), new Vector3(0.2f, 2.44f, 0.2f), LatMaterials.Pal.RoadLine, true);
                _ground.Box(new Vector3(c.x, 2.44f, gz), new Vector3(7.5f, 0.2f, 0.2f), LatMaterials.Pal.RoadLine, true);
                _ground.Box(new Vector3(c.x + s * wid * 0.5f, 0.21f, c.z), new Vector3(0.25f, 0.02f, len), LatMaterials.Pal.RoadLine, false, 0f);
            }
            _ground.Box(new Vector3(c.x, 0.21f, c.z), new Vector3(wid, 0.02f, 0.25f), LatMaterials.Pal.RoadLine, false, 0f);
            Place("Padang", c + Vector3.up * G);
            foreach (var t in new[] { new Vector2(-18, 18), new Vector2(18, -18), new Vector2(-18, -18), new Vector2(18, 18), new Vector2(-19, 0), new Vector2(19, 3) })
                Tree("Prop_RainTree", c + V(t.x, G, t.y), R(1f, 1.3f));
        }

        void ShopRow(Vector3 c, float along, bool north, bool alongX, int variant)
        {
            // rows sit on the north/south (or east/west) edges, shopfront facing the road
            float d = 11.5f;
            Vector3 pos;
            float yaw;
            if (alongX) { pos = c + new Vector3(along, G, north ? d : -d); yaw = north ? 0 : 180; }
            else { pos = c + new Vector3(north ? d : -d, G, along); yaw = north ? 90 : -90; }
            var shop = Prop(variant == 0 ? "Bld_ShopRowA" : variant == 1 ? "Bld_ShopRowB" : "Bld_ShopRowC", pos, yaw, true);
            var front = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            var sideDir = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            ShopSigns(shop);
            // kapcai parked on the five-foot way (kick them over!)
            int bikes = RI(0, 3);
            for (int b = 0; b < bikes; b++)
            {
                var bike = Prop(_rng.NextDouble() < 0.5 ? "Prop_Kapcai" : "Prop_Kapcai2", pos + front * 7.8f + sideDir * R(-6.5f, 6.5f), yaw + 90 + R(-15, 15), false);
                SetStatic(bike, false);
                ModelFactory.AddBoundsCollider(bike, 0.05f);
                Breakable.Make(bike, Breakable.Kind.Topple, 1);
            }
            _city.itemSpots.Add(pos + front * 7.5f + Vector3.up * 0.6f);
            // crates by the shop fronts (breakable, spill coins)
            if (_rng.NextDouble() < 0.6)
            {
                var side = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                var cp = pos + front * 7.2f + side * R(-6, 6);
                var crate = Prop("Prop_Crate", cp, R(0, 90), false);
                SetStatic(crate, false);
                Breakable.Make(crate, Breakable.Kind.Shatter, 3);
            }
        }

        void ShopBlock(Vector3 c, bool both)
        {
            bool alongX = _rng.NextDouble() < 0.6;
            ShopRow(c, -8, true, alongX, RI(0, 3));
            ShopRow(c, 8, true, alongX, RI(0, 3));
            ShopRow(c, -8, false, alongX, RI(0, 3));
            ShopRow(c, 8, false, alongX, RI(0, 3));
            // back lane with a few trees
            var used = new List<Vector2>();
            for (int i = 0; i < 2; i++)
            {
                var p = alongX ? new Vector3(R(-15, 15), G, R(-3, 3)) : new Vector3(R(-3, 3), G, R(-15, 15));
                Tree("Prop_RainTree", c + p, 0.7f);
            }
            _city.coinSpots.Add(c + new Vector3(alongX ? R(-15, 15) : 0, 1f, alongX ? 0 : R(-15, 15)));
        }

        static readonly string[] KitShops = { "env_chowkit_shop_a", "env_chowkit_shop_b", "env_chowkit_shop_c", "env_chowkit_shop_d", "env_chowkit_shop_e" };
        static readonly string[] KitAwnings = { "env_awning_teal", "env_awning_green", "env_awning_red", "env_awning_blue" };
        static readonly string[] KitCanopies = { "env_market_canopy_blue", "env_market_canopy_red", "env_market_canopy_teal", "env_market_canopy_yellow" };

        /// <summary>One kit shophouse: facade faces `yaw`, with an awning and a hand-lettered sign.</summary>
        void KitShop(Vector3 center, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            var shop = Prop(KitShops[RI(0, KitShops.Length)], center, yaw, false);
            ModelFactory.UseProxyCollider(shop);
            var front = rot * Vector3.forward;
            Prop(KitAwnings[RI(0, KitAwnings.Length)], center + front * 5.0f + Vector3.up * 3.35f, yaw, false);
            var go = new GameObject("ShopSign");
            go.transform.SetParent(shop.transform, false);
            go.transform.localPosition = new Vector3(0, 3.25f, 3.3f);
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var tm = go.AddComponent<TextMesh>();
            tm.text = ShopNames[(_signIdx++ * 7 + RI(0, 3)) % ShopNames.Length];
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = 0.06f;
            tm.fontSize = 56;
            tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(0.12f, 0.1f, 0.1f);
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = LatMaterials.SignText(tm.font);
            _city.itemSpots.Add(center + front * 6.3f + Vector3.up * 0.6f);
        }

        /// <summary>
        /// Chow Kit (vertical slice): kit shophouses on all four sides, a covered produce
        /// market on the kerbs, hawker stalls, umbrellas, crates and wires - built only from
        /// the KL street kit + Chow Kit market modules. The surrounding roads are wet.
        /// </summary>
        void ChowKitBlock(Vector3 c)
        {
            // shophouse rows: 7 along north and south, 3 on east and west (corners stay open)
            for (int i = 0; i < 7; i++)
            {
                float x = -15f + i * 5f;
                KitShop(c + new Vector3(x, G, 13.9f), 0);
                KitShop(c + new Vector3(x, G, -13.9f), 180);
            }
            for (int i = 0; i < 3; i++)
            {
                float z = -5f + i * 5f;
                KitShop(c + new Vector3(13.9f, G, z), 90);
                KitShop(c + new Vector3(-13.9f, G, z), -90);
            }
            // produce canopies along the north and east kerbs, facing the road
            for (int i = 0; i < 7; i++)
            {
                var can = Prop(KitCanopies[i % 4], c + new Vector3(-16.5f + i * 5.5f, G, 20.4f), 0, false);
                ModelFactory.UseProxyCollider(can);
                _city.itemSpots.Add(can.transform.position + new Vector3(0, 1.2f, 1.2f));
            }
            for (int i = 0; i < 3; i++)
            {
                var can = Prop(KitCanopies[(i + 1) % 4], c + new Vector3(20.4f, G, -7f + i * 7f), 90, false);
                ModelFactory.UseProxyCollider(can);
            }
            // hawker stalls with umbrellas and stools on the south sidewalk
            for (int i = 0; i < 3; i++)
            {
                var p = c + new Vector3(-12f + i * 12f, G, -20.3f);
                ModelFactory.UseProxyCollider(Prop("env_stall_counter", p, 180, false));
                ModelFactory.UseProxyCollider(Prop("env_market_umbrella", p + new Vector3(2.2f, 0, 0.2f), R(0, 360), false));
                for (int k = 0; k < 3; k++)
                    Prop(k % 2 == 0 ? "env_stool_red" : "env_stool_blue", p + new Vector3(-1.5f + k * 1.2f, 0, -1.2f), R(0, 360), false);
            }
            // crates, planters, bollards, tarps - the clutter that makes a KL market street
            for (int i = 0; i < 8; i++)
            {
                var p = c + new Vector3(R(-18, 18), G, (_rng.NextDouble() < 0.5 ? 1 : -1) * R(18.8f, 19.3f));
                var cr = Prop(_rng.NextDouble() < 0.5 ? "env_crate_stack" : "env_crate_oranges", p, R(0, 360), false);
                SetStatic(cr, false);
                ModelFactory.UseProxyCollider(cr);
                Breakable.Make(cr, Breakable.Kind.Shatter, 3);
            }
            foreach (float sx in new[] { -1f, 1f })
            {
                ModelFactory.UseProxyCollider(Prop("env_planter", c + new Vector3(sx * 20.3f, G, sx * 20.3f), 0, false));
                ModelFactory.UseProxyCollider(Prop("env_tarp_stack", c + new Vector3(sx * 5f, G, 0), R(0, 360), false));
                ModelFactory.UseProxyCollider(Prop("env_traffic_barrier", c + new Vector3(sx * 3f, G, sx * 8f), 0, false));
            }
            for (int i = 0; i < 6; i++)
                ModelFactory.UseProxyCollider(Prop("env_bollard", c + new Vector3(-21.4f, G, -18f + i * 7f), 0, false));
            // overhead-wire poles along the west kerb, strung with black cables
            Vector3? prevTop = null;
            for (int i = 0; i < 4; i++)
            {
                var p = c + new Vector3(-21.3f, G, -15f + i * 10f);
                ModelFactory.UseProxyCollider(Prop("env_wire_pole", p, 90, false));
                var top = p + Vector3.up * 7.55f;
                if (prevTop.HasValue)
                    foreach (float off in new[] { -0.8f, 0.8f })
                    {
                        var a = prevTop.Value + new Vector3(off, 0, 0);
                        var b = top + new Vector3(off, 0, 0);
                        var mid = (a + b) * 0.5f - Vector3.up * 0.5f;       // a little sag
                        Shapes.Box("Wire", (a + mid) * 0.5f, new Vector3(0.04f, 0.04f, Vector3.Distance(a, mid)), LatMaterials.Ink, _props,
                            false, 0.8f, Quaternion.LookRotation(mid - a));
                        Shapes.Box("Wire", (mid + b) * 0.5f, new Vector3(0.04f, 0.04f, Vector3.Distance(mid, b)), LatMaterials.Ink, _props,
                            false, 0.8f, Quaternion.LookRotation(b - mid));
                    }
                prevTop = top;
            }
            Place("ChowKit", c + new Vector3(0, G, 24.5f));
            Face("ChowKit", Quaternion.Euler(0, 90, 0));
        }

        static readonly string[] BfShops = { "env_bf_shopfront_yellow", "env_bf_shopfront_pink", "env_bf_shopfront_blue" };
        static readonly string[] BfNames =
        {
            "KEDAI BUNGA LAKSHMI", "RESTORAN DAUN PISANG", "KEDAI SARI MEENA", "KEDAI EMAS VEL", "MUTHU TEKSTIL",
            "KEDAI MUZIK RAJA", "APPAM CORNER", "THOSAI 24 JAM", "REMPAH RATUS", "KEDAI GELANG DEVI",
        };
        int _bfIdx;

        /// <summary>One Brickfields arcaded shopfront from the P1 kit, with a hand-lettered sign.</summary>
        void BfShop(Vector3 center, float yaw)
        {
            var shop = KitProp(BfShops[RI(0, BfShops.Length)], center, yaw);
            var go = new GameObject("ShopSign");
            go.transform.SetParent(shop.transform, false);
            go.transform.localPosition = new Vector3(0, 3.1f, 3.02f);
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var tm = go.AddComponent<TextMesh>();
            tm.text = BfNames[_bfIdx++ % BfNames.Length];
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = 0.055f;
            tm.fontSize = 56;
            tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(0.55f, 0.08f, 0.08f);
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = LatMaterials.SignText(tm.font);
            _city.itemSpots.Add(center + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 6.3f + Vector3.up * 0.6f);
        }

        /// <summary>
        /// Brickfields (triptych centre panel): arcaded shopfronts with painted pillars and brass
        /// bells on all four sides, garland/flower stalls and ornamental lamps on the checker-tiled
        /// five-foot way, bollards along the kerb. The LRT runs along the river next door.
        /// </summary>
        void BrickfieldsBlock(Vector3 c)
        {
            for (int i = 0; i < 7; i++)
            {
                float x = -15f + i * 5f;
                BfShop(c + new Vector3(x, G, 13.9f), 0);
                BfShop(c + new Vector3(x, G, -13.9f), 180);
            }
            for (int i = 0; i < 3; i++)
            {
                float z = -5f + i * 5f;
                BfShop(c + new Vector3(13.9f, G, z), 90);
                BfShop(c + new Vector3(-13.9f, G, z), -90);
            }
            // flower stalls on the north + south sidewalks, facing the road
            foreach (float sz in new[] { -1f, 1f })
                for (int i = 0; i < 3; i++)
                {
                    var st = KitProp("env_bf_flower_stall", c + new Vector3(-12f + i * 12f + (sz > 0 ? 0 : 6f), G, sz * 20.2f), sz > 0 ? 0 : 180);
                    _city.itemSpots.Add(st.transform.position + new Vector3(0, 1.2f, sz * 1.4f));
                }
            // ornamental double lamps on the kerb line; they topple like the ordinary ones
            for (int i = 0; i < 4; i++)
            {
                float yaw = i * 90f;
                var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                var side = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                foreach (float a in new[] { -17f, -6f, 6f, 17f })
                {
                    var lamp = Prop("env_bf_lamp", c + dir * 21.0f + side * a + Vector3.up * G, yaw, false);
                    SetStatic(lamp, false);
                    var cap = lamp.AddComponent<CapsuleCollider>();
                    cap.center = new Vector3(0, 2.8f, 0); cap.radius = 0.2f; cap.height = 5.6f;
                    Breakable.Make(lamp, Breakable.Kind.Topple, 1);
                }
            }
            for (int i = 0; i < 7; i++)
            {
                ModelFactory.UseProxyCollider(Prop("env_bollard", c + new Vector3(21.3f, G, -15f + i * 5f), 0, false));
                ModelFactory.UseProxyCollider(Prop("env_bollard", c + new Vector3(-21.3f, G, -15f + i * 5f), 0, false));
            }
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    KitProp(_rng.NextDouble() < 0.5 ? "env_kb_flower_pot" : "env_kb_flower_pot_white", c + new Vector3(sx * 19.6f, G, sz * 19.6f), 0);
            Place("Brickfields", c + new Vector3(0, G, 24.5f));
            Face("Brickfields", Quaternion.Euler(0, 90, 0));
        }

        /// <summary>
        /// LRT over the Klang river (triptych rail edge): viaduct segments from the P1 kit with
        /// the piers standing in the water (never on a bridge deck), and a two-car train shuttling
        /// along it.
        /// </summary>
        void BuildLrt()
        {
            var klang = Layout.rivers[Layout.rivers.Length - 1];
            float x = BlockCenter(klang.col, 0).x, baseY = -2.4f;       // water surface
            void Seg(float z, float scale)
            {
                Kit("env_bf_rail_viaduct", new Vector3(x, baseY, z), 0, scale);
                foreach (float s in new[] { -1f, 1f })                   // piers are solid
                    _ground.ColliderOnly(new Vector3(x, baseY + 3.3f, z + s * 6f * scale), new Vector3(1.6f, 6.6f, 1.6f));
            }
            const float bridgeSpan = 1.9f;                               // 38 m: piers at +/-11.4 m, clear of the deck
            for (int k = klang.r0; k <= klang.r1 + 1; k++)
            {
                float rz = RoadZ(k);
                Seg(rz, bridgeSpan);
                if (k == klang.r1 + 1) break;
                float from = rz + 10f * bridgeSpan, to = RoadZ(k + 1) - 10f * bridgeSpan;
                int n = Mathf.Max(1, Mathf.RoundToInt((to - from) / 20f));
                float len = (to - from) / n;
                for (int i = 0; i < n; i++) Seg(from + len * (i + 0.5f), len / 20f);
            }
            var train = new GameObject("LRT").transform;
            train.SetParent(_city.root, false);
            for (int i = 0; i < 2; i++)
            {
                var car = ModelFactory.Spawn("env_bf_lrt_car", Vector3.zero, Quaternion.identity, train, "LRTCar" + i);
                car.transform.localPosition = new Vector3(0, 0, -i * 12.3f);
            }
            float railY = baseY + 8.4f;
            var sh = train.gameObject.AddComponent<Shuttle>();
            sh.a = new Vector3(x - 1.8f, railY, RoadZ(klang.r0) + 8f);
            sh.b = new Vector3(x - 1.8f, railY, RoadZ(klang.r1 + 1) - 2f);
            sh.speed = 11f;
            sh.pause = 4f;
            train.position = sh.a;
            Place("LRT", new Vector3(x, railY, (RoadZ(klang.r0) + RoadZ(klang.r1 + 1)) * 0.5f));
        }

        /// <summary>Far skyline backdrop cards (P1 kit) beyond the west and north map edges.</summary>
        void BuildSkyline()
        {
            float hx = NX * Pitch * 0.5f + Road * 0.5f, hz = NZ * Pitch * 0.5f + Road * 0.5f;
            for (int i = -2; i <= 2; i++)
                Kit("env_kb_skyline", new Vector3(-hx - 160f, 0, i * hz * 0.45f), 90, 1f).transform.localScale = Vector3.one * 2.2f;
            for (int i = -3; i <= 3; i++)
                Kit("env_kb_skyline", new Vector3(i * hx * 0.3f, 0, hz + 160f), 180, 1f).transform.localScale = Vector3.one * 2.2f;
        }

        // ------------------------------------------------------------------ landmarks (env_kl_landmarks kit)
        /// <summary>Hand-lettered text on a landmark's blank sign board (local position on the model).</summary>
        void LetterSign(GameObject on, string text, Vector3 local, float size, Color ink)
        {
            var go = new GameObject("Sign_" + text);
            go.transform.SetParent(on.transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = size;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            tm.color = ink;
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = LatMaterials.SignText(tm.font);
        }

        /// <summary>A freestanding blank sign board on two legs, lettered in Unity.</summary>
        void SignPost(string text, Vector3 pos, float yaw)
        {
            var board = Prop("env_signboard_blank", pos + Vector3.up * 1.6f, yaw, false);
            LetterSign(board, text, new Vector3(0, 0, 0.09f), 0.05f, new Color(0.15f, 0.12f, 0.1f));
            var rot = Quaternion.Euler(0, yaw, 0);
            foreach (float x in new[] { -1.2f, 1.2f })
                Shapes.Box("SignLeg", pos + rot * new Vector3(x, 0.6f, 0), new Vector3(0.1f, 1.2f, 0.1f), LatMaterials.Ink, _props, true, 1f);
        }

        /// <summary>KL Sentral: the transit hub facing the north road, taxi bays and planters.</summary>
        void KLSentralBlock(Vector3 c)
        {
            var hub = KitProp("env_lm_kl_sentral", c + V(0, G, -3f), 0, _ls);
            LetterSign(hub, "KL SENTRAL", new Vector3(0, 8.4f, 11.5f), 0.16f, new Color(0.7f, 0.1f, 0.1f));
            for (int i = 0; i < 12; i++)
                ModelFactory.UseProxyCollider(Prop("env_bollard", c + V(-13.75f + i * 2.5f, G, 17.2f), 0, false));
            foreach (float x in new[] { -17f, 17f })
                KitProp("env_planter", c + V(x, G, 16.5f), 0);
            _city.itemSpots.Add(c + V(0, G + 0.6f, 16f));
            Place("KLSentral", c + V(0, G, 24.5f));
            Face("KLSentral", Quaternion.Euler(0, 90, 0));
        }

        /// <summary>Muzium Negara on its podium behind a lawn with hibiscus beds and palms.</summary>
        void MuziumNegaraBlock(Vector3 c)
        {
            var mu = KitProp("env_lm_muzium_negara", c + V(0, G, -3f), 0, _ls);
            LetterSign(mu, "MUZIUM NEGARA", new Vector3(0, 7.55f, 7.75f), 0.1f, new Color(0.35f, 0.2f, 0.08f));
            foreach (float x in new[] { -11f, 11f })
            {
                KitProp("env_pbg_flowerbed", c + V(x, G, 11f), 0);
                KitProp("env_palm", c + V(x * 1.55f, G, 12f), R(0, 360));
            }
            SignPost("MUZIUM NEGARA", c + V(0, G, 17.5f), 0);
            _city.itemSpots.Add(c + V(-6, G + 0.6f, 12f));
            Place("MuziumNegara", c + V(0, G, 24.5f));
            Face("MuziumNegara", Quaternion.Euler(0, 90, 0));
        }

        /// <summary>Perdana Botanical Gardens: lake with lotus and a footbridge, a wakaf pavilion,
        /// bougainvillea pergola, hibiscus beds, orchid arch, fountain, benches, big shady trees.</summary>
        void PerdanaGardensBlock(Vector3 c)
        {
            var lakeAt = c + V(-3f, G, -2f);
            Prop("env_pbg_lake", lakeAt, 0, false, _ls);
            Prop("env_pbg_footbridge", lakeAt + new Vector3(12.5f * _ls, 0, 0), 0, false, _ls);
            KitProp("env_pbg_gazebo", c + V(-14.5f, G, 13.5f), 0, 1.3f);
            Prop("env_pbg_pergola", c + V(15.5f, G, -8f), 0, false, 1.3f);
            KitProp("env_pbg_fountain", c + V(14f, G, 12.5f), 0, 1.4f);
            Prop("env_pbg_orchid_arch", c + V(-15.5f, G, -14f), 90, false);
            foreach (float x in new[] { -6f, 2f })
                KitProp("env_pbg_flowerbed", c + V(x, G, 16.5f), 0);
            foreach (var b in new[] { new Vector3(-10, 0, 10), new Vector3(4, 0, 10.5f), new Vector3(-18, 0, -4), new Vector3(8, 0, -14) })
                KitProp("env_pbg_bench", c + b * _ls + Vector3.up * G, Mathf.Atan2(-b.x, -b.z) * Mathf.Rad2Deg);
            foreach (var t in new[] { new Vector3(-17, 0, 3), new Vector3(17, 0, 3), new Vector3(-6, 0, -17), new Vector3(3, 0, 17.5f),
                         new Vector3(-19, 0, -19), new Vector3(19, 0, -19), new Vector3(-20, 0, 12), new Vector3(20, 0, 19), new Vector3(0, 0, -20) })
                KitProp("env_kb_rain_tree", c + t * _ls + Vector3.up * G, R(0, 360), R(1.1f, 1.4f));
            KitProp("env_palm", c + V(10, G, 16), 0);
            SignPost("TAMAN BOTANI PERDANA", c + V(-6f, G, 19f), 0);
            _city.itemSpots.Add(c + V(-14.5f, G + 1.6f, 13.5f));
            _city.itemSpots.Add(c + V(15.5f, G + 0.6f, -8f));
            Place("TamanPerdana", c + V(0, G, 24.5f));
            Face("TamanPerdana", Quaternion.Euler(0, 90, 0));
        }

        /// <summary>Masjid Negara: star-roofed prayer hall, reflecting pools and the minaret.</summary>
        void MasjidNegaraBlock(Vector3 c)
        {
            KitProp("env_lm_masjid_negara", c + V(0, G, 0), 0, _ls);
            // the minaret is outside the hall proxy: give it its own collider
            _ground.ColliderOnly(c + V(-13.5f, G + 18f * _ls, 13.5f), new Vector3(2.6f, 36f, 2.6f) * _ls);
            SignPost("MASJID NEGARA", c + V(8f, G, 19.2f), 0);
            Place("MasjidNegara", c + V(0, G, 24.5f));
            Face("MasjidNegara", Quaternion.Euler(0, 90, 0));
        }

        // ------------------------------------------------------------------ east side landmarks (env_kl_landmarks2)
        /// <summary>A landmark filling its block, a lettered sign post on the south pavement, a
        /// mission item spot and a named place (+ facing) for missions and fast travel.</summary>
        void Landmark(Vector3 c, string model, string place, string sign, float yaw)
        {
            KitProp(model, c + V(0, G, 0), yaw, _ls);
            SignPost(sign, c + V(12f, G, 20.5f), 0);
            // a row of palms along the front, the grounds' corners planted with rain trees
            foreach (float x in new[] { -19f, -11f, 11f, 19f }) KitProp("env_palm", c + V(x, G, 19.5f), R(0, 360), R(1f, 1.2f));
            foreach (float x in new[] { -20f, 20f }) KitProp("env_kb_rain_tree", c + V(x, G, -20f), R(0, 360), R(1.1f, 1.3f));
            _city.itemSpots.Add(c + V(-12f, G + 0.6f, 18.5f));
            Place(place, c + V(0, G, 24.5f));
            Face(place, Quaternion.Euler(0, 90, 0));
        }

        /// <summary>Batu Caves: the limestone hill, the golden statue and the rainbow stairs up to
        /// the cave. The stairs are climbable (a ramp collider under the steps).</summary>
        void BatuCavesBlock(Vector3 c)
        {
            Landmark(c, "env_lm2_batu_caves", "BatuCaves", "BATU CAVES", 0);
            // stairs run from the plaza (front, +Z in the world after the kit's turn) up into the hill
            float run = 12f * _ls, rise = 17.5f * _ls, len = Mathf.Sqrt(run * run + rise * rise);
            var ramp = new GameObject("BatuStairsRamp");
            ramp.transform.SetParent(_props, false);
            ramp.transform.position = c + V(0, G + rise * 0.5f, 3f);
            ramp.transform.rotation = Quaternion.Euler(Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0, 0);
            ramp.AddComponent<BoxCollider>().size = new Vector3(6f * _ls, 0.3f, len);
            // statue pedestal is solid
            _ground.ColliderOnly(c + V(9.5f, G + 7f * _ls, 10.5f), new Vector3(5f, 14f, 5f) * _ls);
        }

        /// <summary>Stadium Merdeka: an open bowl you can drive into - only the outer wall and the
        /// floodlight towers are solid, with a gap on the south side as the tunnel entrance.</summary>
        void StadiumBlock(Vector3 c)
        {
            Prop("env_lm2_stadium_merdeka", c + V(0, G, 0), 0, false, _ls);
            float rx = 13 * 1.47f * _ls, ry = 16 * 1.47f * _ls;
            int n = 40;
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * rx, 0, Mathf.Sin(a0) * ry);
                var p1 = new Vector3(Mathf.Cos(a1) * rx, 0, Mathf.Sin(a1) * ry);
                var mid = (p0 + p1) * 0.5f;
                if (mid.z > ry * 0.9f) continue;                        // the entrance gap (north side in the kit = world south)
                var go = new GameObject("StadiumWall");
                go.transform.SetParent(_props, false);
                go.transform.position = c + mid + Vector3.up * (G + 3.3f * _ls);
                go.transform.rotation = Quaternion.LookRotation(p1 - p0);
                go.AddComponent<BoxCollider>().size = new Vector3(1.2f * _ls, 6.6f * _ls, (p1 - p0).magnitude + 0.2f);
            }
            SignPost("STADIUM MERDEKA", c + V(12f, G, 20.5f), 0);
            _city.itemSpots.Add(c + V(0, G + 0.6f, 0));
            Place("StadiumMerdeka", c + V(0, G, 24.5f));
            Face("StadiumMerdeka", Quaternion.Euler(0, 90, 0));
        }

        void CondoBlock(Vector3 c)
        {
            var models = new[] { "Bld_CondoA", "Bld_CondoB", "Bld_CondoC", "Bld_CondoD" };
            Prop(models[RI(0, 4)], c + new Vector3(-8, G, -8), 0, true);
            Prop(models[RI(0, 4)], c + new Vector3(9, G, 9), 90, true);
            if (_rng.NextDouble() < 0.6) Billboard(c + new Vector3(14, G, -15), _rng.NextDouble() < 0.5 ? 180 : 90);
            Tree("Prop_Angsana", c + new Vector3(10, G, -12), 0.9f);
            Tree("Prop_Angsana", c + new Vector3(-12, G, 12), 0.9f);
            _city.itemSpots.Add(c + new Vector3(12, G + 0.6f, -2));
        }

        void MasjidBlock(Vector3 c)
        {
            KitProp("env_lm2_masjid_jamek", c + V(2, G, 0), -90, _ls);
            Place("Masjid", c + V(-16, G, 0));
            Tree("Prop_Palm", c + V(-16, G, 14));
            Tree("Prop_Palm2", c + V(-16, G, -14));
            Tree("Prop_Palm", c + V(16, G, 16));
            Tree("Prop_Palm2", c + V(16, G, -16));
            Tree("Prop_Palm", c + V(-19, G, 4), 1.2f);
            Tree("Prop_Palm2", c + V(-19, G, -4), 1.2f);
        }

        void MamakBlock(Vector3 c)
        {
            // Restoran Mamak on the north-west corner, open to the road; shops on the south side
            var m = Prop("Bld_Mamak", c + new Vector3(-8, G, 9), 0, false);
            // colliders only on the posts + counter so you can walk in
            foreach (var x in new[] { -5.6f, 0f, 5.6f })
                foreach (var z in new[] { -4.2f, 4.2f })
                {
                    var cap = m.AddComponent<CapsuleCollider>();
                    cap.center = new Vector3(x, 1.7f, z); cap.radius = 0.15f; cap.height = 3.4f;
                }
            var counter = m.AddComponent<BoxCollider>();
            counter.center = new Vector3(0, 1.1f, -3.6f); counter.size = new Vector3(7, 2.2f, 1.3f);
            Place("Mamak", c + new Vector3(-8, G, 16));
            Place("MamakCounter", c + new Vector3(-8, G, 4.3f));
            Face("MamakCounter", Quaternion.identity);
            ShopRow(c, -8, false, true, 0);
            ShopRow(c, 8, false, true, 1);
            Prop("Bld_ShopRowB", c + new Vector3(12, G, 11.5f), 0, true);
            Sign("RESTORAN\nMAMAK 24 JAM", c + new Vector3(-8, 4.6f, 13.6f), 0);
        }

        void PasarBlock(Vector3 c)
        {
            ShopRow(c, -8, true, true, 0);
            ShopRow(c, 8, true, true, 1);
            ShopRow(c, -8, false, true, 1);
            ShopRow(c, 8, false, true, 0);
            for (int i = 0; i < 8; i++)
                foreach (float s in new[] { -1f, 1f })
                {
                    var p = c + new Vector3(-15.5f + i * 4.4f, G, s * 2.6f);
                    var can = Prop("Prop_PasarCanopy", p, s > 0 ? 180 : 0, false);
                    var bc = can.AddComponent<BoxCollider>();
                    bc.center = new Vector3(0, 0.5f, 0); bc.size = new Vector3(2.6f, 1f, 1.3f);
                    _city.itemSpots.Add(p + Vector3.up * 1.2f);
                }
            Place("Pasar", c + new Vector3(0, G, 0));
            var gate = Prop("Prop_ChinatownGate", c + new Vector3(-19.5f, G, 0), 90, false);
            foreach (float gz in new[] { -6.5f, 6.5f })
            {
                var gc = gate.AddComponent<CapsuleCollider>(); gc.center = new Vector3(gz, 3, 0); gc.radius = 0.5f; gc.height = 6;
            }
            Place("PasarGate", c + new Vector3(-24, G, 0));
        }

        void TowersBlock(Vector3 c)
        {
            var t = Prop("Bld_MenaraKembar", c + V(0, G, -2), 180, false, 0.82f * _ls);
            // colliders: podium + two tower shafts
            var pod = t.AddComponent<BoxCollider>(); pod.center = new Vector3(0, 6, -6); pod.size = new Vector3(50, 12, 28);
            foreach (float sx in new[] { -14f, 14f })
            {
                var cap = t.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(sx, 60, 0); cap.radius = 8.5f; cap.height = 120;
            }
            Place("Towers", c + V(0, G, -21));
        }

        void ParkBlock(Vector3 c)
        {
            _ground.Box(c + new Vector3(0, 0.45f, 0), new Vector3(10, 0.5f, 10), LatMaterials.Pal.Wall, true);
            _ground.Box(c + new Vector3(0, 0.55f, 0), new Vector3(9, 0.5f, 9), LatMaterials.Pal.Water, false, 0f);
            _ground.Box(c + new Vector3(0, 1.6f, 0), new Vector3(0.6f, 2.4f, 0.6f), LatMaterials.Pal.RoadLine, true);
            Scatter(c, 10, new[] { "Prop_RainTree", "Prop_Palm" }, new List<Vector2> { Vector2.zero }, 9f);
            Place("Park", c + new Vector3(0, G, -8));
        }

        void KLTowerBlock(Vector3 c)
        {
            // Bukit Nanas: a raised mound of forest with the tower on top
            _ground.Box(c + V(0, 0.9f, 0), new Vector3(34 * _ls, 1.6f, 34 * _ls), LatMaterials.Pal.Park, true, 1.4f);
            _ground.Box(c + V(0, 1.8f, 0), new Vector3(22 * _ls, 1.6f, 22 * _ls), LatMaterials.Pal.Park, true, 1.4f);
            var t = Prop("Bld_MenaraKL", c + V(0, 2.6f, 0), 0, false, 0.9f * _ls);
            var cap = t.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 40, 0); cap.radius = 3.5f; cap.height = 80;
            var bc = t.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 1.5f, 0); bc.size = new Vector3(16, 3, 16);
            var used = new List<Vector2> { Vector2.zero };
            for (int i = 0; i < 26; i++)
            {
                var a = i / 26f * Mathf.PI * 2f;
                var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(12, 19);
                bool onHill = Mathf.Abs(p.x) < 16.5f && Mathf.Abs(p.y) < 16.5f;          // the forest on the lower terrace
                Tree(i % 3 == 0 ? "Prop_Palm" : "Prop_RainTree", c + V(p.x, onHill ? 1.7f : G, p.y), R(1f, 1.4f));
            }
            Place("KLTower", c + V(0, 3.4f, -12));
        }

        void DataranBlock(Vector3 c)
        {
            // Dataran Merdeka: the padang, the tall flagpole, and the Sultan Abdul Samad building
            _ground.Box(c + V(7, 0.22f, 0), new Vector3(28 * _ls, 0.06f, 40 * _ls), LatMaterials.Pal.Park, false, 0f);
            Prop("Bld_SultanAbdulSamad", c + V(-14, G, 0), 90, true, _ls);
            var pole = Prop("Prop_Flagpole", c + V(8, G, 0), 0, false, _ls);
            var cap = pole.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 10, 0); cap.radius = 0.4f; cap.height = 20;
            var bc = pole.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.4f, 0); bc.size = new Vector3(5, 0.8f, 5);
            Place("Dataran", c + V(8, G, -8));
            Place("DataranRoad", c + V(21, G, 0));
            Sign("DATARAN MERDEKA", c + V(20.5f, 2.2f, -12), 90);
            foreach (float z in new[] { -19f, -9f, 9f, 19f })
                Tree("Prop_RainTree", c + V(19.5f, G, z), R(1.1f, 1.3f));
        }

        void DealerBlock(Vector3 c)
        {
            ShopRow(c, -8, true, true, 0);
            ShopRow(c, 8, true, true, 1);
            ShopRow(c, -8, false, true, 1);
            ShopRow(c, 8, false, true, 0);
            // car dealer: open forecourt on the north side
            var dealer = new GameObject("KedaiKereta");
            dealer.transform.SetParent(_props, false);
            dealer.transform.SetPositionAndRotation(c + new Vector3(-8, G, 19f), Quaternion.identity);
            dealer.AddComponent<CarDealer>();
            Sign("KEDAI KERETA\nTERPAKAI", c + new Vector3(-8, 4.4f, 17.6f), 0);
            Place("Dealer", dealer.transform.position);
            // clothes shop on the south side
            var shop = new GameObject("KedaiBaju");
            shop.transform.SetParent(_props, false);
            shop.transform.SetPositionAndRotation(c + new Vector3(8, G, -19f), Quaternion.Euler(0, 180, 0));
            shop.AddComponent<ClothesShop>();
            Sign("KEDAI BAJU", c + new Vector3(8, 4.2f, -17.6f), 180);
            Place("ClothesShop", shop.transform.position);
        }

        void PasarSeniBlock(Vector3 c)
        {
            Prop("Bld_PasarSeni", c + new Vector3(0, G, -4), 180, true);
            Place("PasarSeni", c + new Vector3(0, G, -18));
            Sign("PASAR SENI", c + new Vector3(0, 11.5f, -13.4f), 180);
            ShopRow(c, -8, true, true, 1);
            ShopRow(c, 8, true, true, 0);
        }

        static readonly string[] ShopNames =
        {
            "KEDAI RUNCIT AH SENG", "GUNTING RAMLI", "NASI KANDAR BESTARI", "FARMASI SIHAT", "KEDAI EMAS KL",
            "KOPITIAM CHONG", "BENGKEL MOTOR MAT", "KAIN SITI", "KEDAI BUKU ILMU", "APAM BALIK 88",
            "ROTI BAKAR KAK NAH", "LAUNDRI BERSIH", "CENDOL MEGAMAJU", "KEDAI JAM WONG", "SERBANEKA RM2",
            "KEDAI HANDPHONE", "MEE REBUS PAKCIK", "TEH TARIK CORNER", "KEDAI BASIKAL", "UBAT TRADISIONAL",
        };
        int _signIdx;

        /// <summary>Hand-lettered signboards over each of the three shops in a row.</summary>
        void ShopSigns(GameObject shop)
        {
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("ShopSign");
                go.transform.SetParent(shop.transform, false);
                // board sits at Blender (x, -5.46, 3.35); the model is flipped 180 in its wrapper
                go.transform.localPosition = new Vector3(-(i - 1) * 5f, 3.35f, 5.49f);
                go.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var tm = go.AddComponent<TextMesh>();
                tm.text = ShopNames[(_signIdx++ * 7 + RI(0, 3)) % ShopNames.Length];
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.characterSize = 0.07f;
                tm.fontSize = 56;
                tm.fontStyle = FontStyle.Bold;
                tm.color = new Color(0.98f, 0.97f, 0.9f);
                tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                go.GetComponent<MeshRenderer>().sharedMaterial = LatMaterials.SignText(tm.font);
            }
        }

        void Billboard(Vector3 pos, float yaw)
        {
            var b = Prop("Prop_Billboard", pos, yaw, false);
            foreach (float x in new[] { -3f, 3f })
            {
                var cap = b.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(x, 3, -0.3f); cap.radius = 0.2f; cap.height = 6;
            }
            var go = new GameObject("Ad");
            go.transform.SetParent(b.transform, false);
            go.transform.localPosition = new Vector3(1.2f, 6.9f, 0.22f);
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var tm = go.AddComponent<TextMesh>();
            tm.text = "MINUM\nCENDOL AJAIB!\n- MegaMaju -";
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.11f;
            tm.fontSize = 60;
            tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(0.8f, 0.1f, 0.15f);
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = LatMaterials.SignText(tm.font);
        }

        /// <summary>
        /// Office towers (Jalan Sultan Ismail, Jalan Ampang, TRX): a podium with shops and a glass tower
        /// on it, set back from the road, a crown and plant on the roof. Plain boxes, batched.
        /// </summary>
        void OfficeBlock(Vector3 c)
        {
            var glass = new[] { new Color(0.42f, 0.58f, 0.78f), new Color(0.36f, 0.64f, 0.68f), new Color(0.52f, 0.58f, 0.66f), new Color(0.3f, 0.45f, 0.62f), new Color(0.82f, 0.84f, 0.86f) };
            var podC = new[] { new Color(0.9f, 0.86f, 0.78f), new Color(0.82f, 0.8f, 0.76f), new Color(0.95f, 0.93f, 0.88f) };
            float pod = R(6f, 12f);
            var podCol = podC[RI(0, podC.Length)];
            // a glass lobby podium and a curtain-wall tower (the facade textures the real-KL districts use)
            _ground.TexBox(c + new Vector3(0, G + pod * 0.5f, 0), new Vector3(34f, pod, 34f), podCol, FacadeTex("fac_lobby"), new Vector2(3.6f, 5f), true, 1.6f, 0.3f);
            float tw = R(16f, 24f), td = R(16f, 24f), th = R(45f, 150f);
            var g = glass[RI(0, glass.Length)];
            var tc = c + new Vector3(R(-3f, 3f), 0, R(-3f, 3f));
            _ground.TexBox(tc + new Vector3(0, G + pod + th * 0.5f, 0), new Vector3(tw, th, td), g, FacadeTex("fac_glass"), new Vector2(3f, 3.4f), true, 1.6f, 0.45f);
            // a few storey bands so the tower reads in the distance
            for (float y = pod + 18f; y < pod + th - 8f; y += R(24f, 34f))
                _ground.Box(tc + new Vector3(0, G + y, 0), new Vector3(tw + 0.3f, 0.6f, td + 0.3f), podCol, false, 0f);
            // crown / setback
            float ch = R(4f, 12f);
            _ground.Box(tc + new Vector3(0, G + pod + th + ch * 0.5f, 0), new Vector3(tw * 0.7f, ch, td * 0.7f), new Color(g.r * 0.85f, g.g * 0.85f, g.b * 0.85f), true, 1.6f);
            if (_rng.NextDouble() < 0.5)
                _ground.Box(tc + new Vector3(0, G + pod + th + ch + 6f, 0), new Vector3(0.4f, 12f, 0.4f), LatMaterials.Pal.Rail, false, 0f);
            // roof plant on the podium, a planter and a tree out front
            for (int i = 0; i < 3; i++)
                _ground.Box(c + new Vector3(R(-14f, 14f), G + pod + 1f, R(-14f, -10f)), new Vector3(3f, 2f, 3f), new Color(0.7f, 0.72f, 0.74f), false, 1.2f);
            Tree("Prop_Angsana", c + new Vector3(R(-12f, 12f), G, -19f), R(0.8f, 1f));
            _city.itemSpots.Add(c + new Vector3(0, G + 0.6f, -19.5f));
        }

        /// <summary>District name for the HUD, H&amp;R-style area callouts.</summary>
        public static string District(Vector3 p)
        {
            int col = Mathf.FloorToInt((p.x - X0) / Pitch), row = Mathf.FloorToInt((p.z - Z0) / Pitch);
            if (col < 0 || col >= NX || row < 0 || row >= NZ) return "Pinggir KL";
            var patch = PatchAt(col, row);
            if (patch != null)
            {
                // the nearest named place in the patch
                string best = null;
                float bd = float.MaxValue;
                foreach (var (pn, name, pos) in _districts)
                {
                    if (pn != patch.name) continue;
                    float d = (pos - new Vector2(p.x, p.z)).sqrMagnitude;
                    if (d < bd) { bd = d; best = name; }
                }
                return best ?? patch.name;
            }
            if (IsKampungCell(col, row)) return row == Layout.kampung.r0 + 4 ? "Kampung Baru - Surau" : "Kampung Baru";
            var river = RiverAt(col, row);
            if (river != null) return "Sungai " + river.name;
            switch (ZoneAt(col, row))
            {
                case Zone.ChowKit: return "Chow Kit";
                case Zone.BatuCaves: return "Batu Caves";
                case Zone.IstanaNegara: return "Istana Negara";
                case Zone.TheanHou: return "Thean Hou";
            }
            int g = Layout.gombak;
            if (row >= Layout.north_row)
            {
                if (col < g) return row >= NZ - 2 ? "Titiwangsa" : col >= g - 3 ? "Jalan Tuanku Abdul Rahman" : "Chow Kit";
                return col >= NX - 4 ? "Jalan Sultan Ismail" : "Jalan Ampang";
            }
            if (col >= NX - 4) return row >= 5 ? "Imbi" : "Tun Razak Exchange";
            return row >= 3 ? "Jalan Hang Tuah" : "Bukit Petaling";
        }

        void BuildPhoneBooths()
        {
            void Booth(string key, Vector3 pos, float yaw)
            {
                var b = Prop("Prop_PhoneBooth", pos, yaw, true);
                b.AddComponent<PhoneBooth>();
                _city.places["Phone_" + key] = pos;
            }
            var k = Layout.kampung;
            float e = Block * 0.5f - 2.2f;                              // on the sidewalk, just inside the kerb
            Booth("Home", BlockCenter(k.c0 + 1, k.r0 + 2) + new Vector3(e, G, 14), 90);
            Booth("Surau", BlockCenter(k.c0, k.r0 + 4) + new Vector3(-26, G, -e), 180);
            foreach (var key in new[] { "Mamak", "Dataran", "Towers", "KLTower" })
                if (_city.places.TryGetValue(key, out var at))
                    Booth(key, InsidePatch(at) ? _city.Sidewalk(at, 6f) : at + new Vector3(3f, 0, 3f), 0);
        }

        /// <summary>
        /// Street life: traffic lights, bus stops, Jalur Gemilang bunting, satay carts,
        /// angsana trees, and chickens/cats pottering about.
        /// </summary>
        void BuildStreetLife()
        {
            // traffic lights on the city intersections of the grid (two opposite corners each)
            for (int i = 0; i <= NX; i++)
                for (int k = 0; k <= NZ; k++)
                {
                    if (_gridNodes[i, k] == null || _gridNodes[i, k].links.Count < 3) continue;
                    if (KampungOrRiver(i - 1, k - 1) && KampungOrRiver(i, k - 1) && KampungOrRiver(i - 1, k) && KampungOrRiver(i, k)) continue;
                    if (_rng.NextDouble() < 0.35) continue;
                    var n = new Vector3(RoadX(i), G, RoadZ(k));
                    TrafficLight(n + new Vector3(Road * 0.5f + 1.2f, 0, Road * 0.5f + 1.2f), -90);
                    TrafficLight(n + new Vector3(-Road * 0.5f - 1.2f, 0, -Road * 0.5f - 1.2f), 90);
                }
            // bus stops + a bench and bin on some filler block edges
            for (int col = 0; col < NX; col++)
                for (int row = 0; row < NZ; row++)
                {
                    var zone = ZoneAt(col, row);
                    if (zone == Zone.Patch || zone == Zone.River || IsKampungCell(col, row) || IsLandmark(zone)) continue;
                    if (_rng.NextDouble() > 0.45) continue;
                    var c = BlockCenter(col, row);
                    bool east = _rng.NextDouble() < 0.5;
                    var pos = c + new Vector3(east ? Block * 0.5f - 1.4f : -Block * 0.5f + 1.4f, G, (_rng.NextDouble() < 0.5 ? 1 : -1) * R(12, 34));
                    Prop("Prop_BusStop", pos, east ? 90 : -90, true);
                    Prop("Prop_Bin", pos + new Vector3(0, 0, 3.2f), 0, true);
                }
            // bunting across the streets round Chow Kit and Jalan TAR
            foreach (var (col, row) in new[] { (3, 17), (3, 18), (2, 18), (4, 17), (5, 16), (1, 15) })
            {
                var c = BlockCenter(col, row);
                if (EWRoad(row, col)) Prop("Prop_Bunting", new Vector3(c.x, G, RoadZ(row)), 90, false);
                if (NSRoad(col, row)) Prop("Prop_Bunting", new Vector3(RoadX(col), G, c.z), 0, false);
            }
            // hawker satay carts with umbrellas
            foreach (var key in new[] { "Pasar", "Mamak", "Padang", "Dataran", "PasarSeni", "BukitBintang", "Masjid" })
                if (_city.places.TryGetValue(key, out var p))
                {
                    Vector3 at;
                    if (InsidePatch(p)) at = _city.Sidewalk(p, R(-14, -4));
                    else
                    {
                        // jitter the spot, but never into a carriageway (some places sit right at a block edge)
                        at = p;
                        for (int t = 0; t < 12; t++)
                        {
                            at = p + new Vector3(R(-3, 3), 0, R(-3, 3));
                            if (!OnRoad(at)) break;
                            at = p + (BlockCenter(Mathf.FloorToInt((p.x - X0) / Pitch), Mathf.FloorToInt((p.z - Z0) / Pitch)) - p).normalized * 3f;
                        }
                    }
                    var cart = Prop("Prop_SatayCart", at, R(0, 360), false);
                    var bc = cart.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.6f, 0); bc.size = new Vector3(1.8f, 1.2f, 0.9f);
                }
            // chickens in the kampung, cats in the city
            for (int col = 0; col < NX; col++)
                for (int row = 0; row < NZ; row++)
                {
                    if (!IsKampungCell(col, row)) continue;
                    var c = BlockCenter(col, row);
                    int n = RI(5, 10);
                    for (int i = 0; i < n; i++) Critter("Prop_Ayam", c + new Vector3(R(-40, 40), G, R(-40, 40)), true);
                }
            for (int i = 0; i < 40; i++)
            {
                int col = RI(0, NX), row = RI(0, NZ);
                var zone = ZoneAt(col, row);
                if (zone == Zone.Patch || zone == Zone.River || IsKampungCell(col, row)) continue;
                var c = BlockCenter(col, row);
                Critter("Prop_Kucing", c + new Vector3(R(-40, 40), G, (_rng.NextDouble() < 0.5 ? 1 : -1) * (Block * 0.5f - 2f)), false);
            }
        }

        void TrafficLight(Vector3 pos, float yaw)
        {
            var t = Prop("Prop_TrafficLight", pos, yaw, false);
            var cap = t.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, 2.2f, 0); cap.radius = 0.12f; cap.height = 4.4f;
        }

        void Critter(string model, Vector3 pos, bool chicken)
        {
            var go = ModelFactory.Spawn(model, pos, Quaternion.Euler(0, R(0, 360), 0), _props, chicken ? "Ayam" : "Kucing");
            var cr = go.AddComponent<Critter>();
            cr.isChicken = chicken;
            cr.range = chicken ? 8f : 5f;
            cr.speed = chicken ? 1.1f : 0.8f;
        }

        void StreetLamps(Vector3 c)
        {
            for (int i = 0; i < 4; i++)
            {
                float yaw = i * 90f;
                var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                var side = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                foreach (float a in new[] { i % 2 == 0 ? -14f : 14f })       // one a side, staggered
                {
                    var lamp = Prop("env_lamp_post", c + dir * 21.1f + side * a + Vector3.up * G, yaw, false);
                    SetStatic(lamp, false);
                    foreach (var t in lamp.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layers.Detail;
                    var cap = lamp.AddComponent<CapsuleCollider>();
                    cap.center = new Vector3(0, 3.5f, 0); cap.radius = 0.18f; cap.height = 7;
                    Breakable.Make(lamp, Breakable.Kind.Topple, 1);
                }
            }
        }

        void Sign(string text, Vector3 pos, float yaw)
        {
            var go = new GameObject("Sign_" + text.Split('\n')[0]);
            go.transform.SetParent(_props, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw + 180f, 0));
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.18f;
            tm.fontSize = 48;
            tm.fontStyle = FontStyle.Bold;
            tm.color = LatMaterials.Ink;
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = LatMaterials.SignText(tm.font);
            // a paper board behind it
            Shapes.Box("Board", new Vector3(0, 0, 0.08f), new Vector3(text.Length > 12 ? 6.5f : 5f, text.Contains("\n") ? 2.2f : 1.3f, 0.1f),
                new Color(0.98f, 0.9f, 0.55f), go.transform, false, 1.6f);
        }
    }
}
