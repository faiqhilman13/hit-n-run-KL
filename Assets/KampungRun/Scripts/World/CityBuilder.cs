using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// "Kuala Lumpur-ish": a compressed, made-up district mixing kampung, river and city.
    ///
    ///   col 0-1 : Kampung Sungai Kecil (stilt houses, palms, surau, the family home)
    ///   col 2   : Sungai (the river) - east-west roads cross on bridges
    ///   col 3-6 : the city - shophouses, mamak, masjid, pasar malam, condos,
    ///             Menara Kembar (NE) and Menara KL on its forest hill (SE)
    ///   col 7-8 : the east side - Batu Caves, Tugu Negara, Pavilion, Merdeka 118 by
    ///             Stadium Merdeka, Thean Hou temple, Istana Negara
    ///
    /// Everything is generated from a seed so the layout is identical every run.
    /// </summary>
    public class CityBuilder
    {
        public const int NX = 9, NZ = 6;
        public const float Block = 44f, Road = 10f, Pitch = Block + Road;
        public const int RiverCol = 2;
        public static readonly float X0 = -NX * Pitch * 0.5f, Z0 = -NZ * Pitch * 0.5f;

        public enum Zone { Kampung, River, Shops, Condo, Masjid, Mamak, Pasar, Towers, Park, KLTower, Home, Surau, Padang, Dataran, Dealer, BukitBintang, PasarSeni, ChowKit, Brickfields, KLSentral, MuziumNegara, PerdanaGardens, MasjidNegara,
            BatuCaves, TuguNegara, Pavilion, Merdeka118, StadiumMerdeka, TheanHou, IstanaNegara }

        public class City
        {
            public Transform root;
            public RoadNetwork roads = new RoadNetwork();
            public readonly Dictionary<string, Vector3> places = new Dictionary<string, Vector3>();
            public readonly Dictionary<string, Quaternion> facings = new Dictionary<string, Quaternion>();
            public readonly List<Rect> walkZones = new List<Rect>();      // pedestrian sidewalk rings (block rects)
            public readonly List<Vector3> coinSpots = new List<Vector3>();
            public readonly List<Vector3> itemSpots = new List<Vector3>(); // mission collectible candidates
            public Bounds bounds;
        }

        readonly System.Random _rng = new System.Random(1957); // Lat's first comic year-ish
        City _city;
        StaticBatcher _ground;
        Transform _props, _kitRoot;

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        int RI(int a, int b) => _rng.Next(a, b);

        public static Vector3 BlockCenter(int col, int row) => new Vector3(X0 + col * Pitch + Pitch * 0.5f, 0, Z0 + row * Pitch + Pitch * 0.5f);
        public static float RoadX(int i) => X0 + i * Pitch + Pitch * 0.5f - Pitch * 0.5f;
        public static float RoadZ(int k) => Z0 + k * Pitch;

        static Zone ZoneAt(int col, int row)
        {
            if (col == RiverCol) return Zone.River;
            if (col < RiverCol)
            {
                if (col == 1 && row == 2) return Zone.Home;
                if (col == 0 && row == 4) return Zone.Surau;
                if (col == 0 && row == 1) return Zone.Padang;
                return Zone.Kampung;
            }
            Zone[,] city =
            {
                // col:   3             4             5             6                  7                   8
                { Zone.ChowKit, Zone.Condo, Zone.Towers, Zone.Park,          Zone.BatuCaves,     Zone.Park },    // row 0 (north)
                { Zone.Dataran, Zone.Dealer, Zone.Condo, Zone.BukitBintang,  Zone.TuguNegara,    Zone.Condo }, // row 1
                { Zone.Masjid, Zone.Shops,  Zone.Shops,  Zone.BukitBintang,  Zone.Pavilion,      Zone.Shops }, // row 2
                { Zone.Mamak,  Zone.Pasar,  Zone.PasarSeni, Zone.Shops,      Zone.Merdeka118,    Zone.StadiumMerdeka }, // row 3
                { Zone.Brickfields, Zone.KLSentral, Zone.Shops, Zone.Shops,  Zone.TheanHou,      Zone.Shops }, // row 4 (Brickfields: KL handoff P1 kit)
                { Zone.MuziumNegara, Zone.PerdanaGardens, Zone.MasjidNegara, Zone.KLTower, Zone.IstanaNegara, Zone.Condo }, // row 5 (south): the Lake Gardens side
            };
            // rows in the table run north->south; world row 0 is the south edge
            return city[NZ - 1 - row, col - 3];
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

            BuildRoads();
            for (int c = 0; c < NX; c++)
                for (int r = 0; r < NZ; r++)
                    BuildBlock(c, r, ZoneAt(c, r));
            BuildRiver();
            BuildLrt();
            BuildSkyline();
            BuildMonorail();
            BuildPhoneBooths();
            BuildStreetLife();
            BuildBorder();
            _ground.Build(root, "Ground");

            float hx = NX * Pitch * 0.5f + 20, hz = NZ * Pitch * 0.5f + 20;
            _city.bounds = new Bounds(Vector3.zero, new Vector3(hx * 2, 200, hz * 2));
            return _city;
        }

        // ------------------------------------------------------------------ roads
        /// <summary>Place a KL street-kit module (static, no physics - colliders are separate boxes).</summary>
        GameObject Kit(string id, Vector3 pos, float yaw, float lengthScale = 1f)
        {
            var go = ModelFactory.Spawn(id, pos, Quaternion.Euler(0, yaw, 0), _kitRoot);
            go.transform.localScale = new Vector3(1f, 1f, lengthScale);
            go.isStatic = true;
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        static bool IsChowKit(int col, int row) => col == 3 && row == NZ - 1;

        void BuildRoads()
        {
            float riverL = X0 + RiverCol * Pitch + Road * 0.5f, riverR = riverL + Block;
            float xMin = RoadX(0) - Road * 0.5f, xMax = RoadX(NX) + Road * 0.5f;
            float zMin = RoadZ(0) - Road * 0.5f, zMax = RoadZ(NZ) + Road * 0.5f;
            _kitRoot = new GameObject("StreetKit").transform;
            _kitRoot.SetParent(_city.root, false);
            float seg = Block / 4f;                 // four stretched 10 m tiles per 44 m segment
            float stretch = seg / 10f;

            // --- intersections: crossroads inside, T-junctions on the edge, corners at the map corners
            for (int i = 0; i <= NX; i++)
                for (int k = 0; k <= NZ; k++)
                {
                    var p = new Vector3(RoadX(i), 0, RoadZ(k));
                    bool w = i == 0, e = i == NX, s = k == 0, n = k == NZ;
                    if ((w || e) && (s || n))
                        Kit("env_road_corner", p, w && s ? 0 : e && s ? -90 : e && n ? 180 : 90);
                    else if (w) Kit("env_road_t", p, 0);
                    else if (e) Kit("env_road_t", p, 180);
                    else if (s) Kit("env_road_t", p, -90);
                    else if (n) Kit("env_road_t", p, 90);
                    else Kit("env_road_cross", p, 0);
                }

            // --- north-south segments (they also wall the river, so their collider goes deep)
            for (int i = 0; i <= NX; i++)
            {
                float x = RoadX(i);
                _ground.ColliderOnly(new Vector3(x, -1.5f, 0), new Vector3(Road, 3f, zMax - zMin));
                for (int k = 0; k < NZ; k++)
                    for (int t = 0; t < 4; t++)
                    {
                        float z = RoadZ(k) + Road * 0.5f + seg * (t + 0.5f);
                        bool city = i > RiverCol;
                        bool chowkit = (i == 3 || i == 4) && k == NZ - 1;
                        string id = chowkit ? "env_road_straight_wet" : city && (t == 0 || t == 3) ? "env_crosswalk"
                            : !city ? "env_kb_lane" : "env_road_straight";
                        Kit(id, new Vector3(x, 0, z), 0, stretch);
                    }
            }
            // --- east-west segments: split around the river, a bridge across it
            for (int k = 0; k <= NZ; k++)
            {
                float z = RoadZ(k);
                _ground.ColliderOnly(new Vector3((xMin + riverL) * 0.5f, -1.5f, z), new Vector3(riverL - xMin, 3f, Road));
                _ground.ColliderOnly(new Vector3((riverR + xMax) * 0.5f, -1.5f, z), new Vector3(xMax - riverR, 3f, Road));
                for (int i = 0; i < NX; i++)
                    for (int t = 0; t < 4; t++)
                    {
                        float x = RoadX(i) + Road * 0.5f + seg * (t + 0.5f);
                        bool chowkit = i == 3 && (k == NZ - 1 || k == NZ);
                        Kit(chowkit ? "env_road_straight_wet" : i < RiverCol ? "env_kb_lane" : "env_road_straight",
                            new Vector3(x, 0, z), 90, stretch);
                    }
                // bridge deck under the road tiles + railings + pier
                float bx = (riverL + riverR) * 0.5f;
                // deck top flush with the other road colliders (y = 0) so cars ride on the drawn tiles
                _ground.Box(new Vector3(bx, -0.55f, z), new Vector3(Block + 0.2f, 0.5f, Road), LatMaterials.Pal.Wall, false, 1.4f);
                _ground.ColliderOnly(new Vector3(bx, -0.5f, z), new Vector3(Block + 0.2f, 1f, Road));
                foreach (float side in new[] { -1f, 1f })
                {
                    _ground.Box(new Vector3(bx, 0.55f, z + side * (Road * 0.5f - 0.2f)), new Vector3(Block, 1.1f, 0.35f), LatMaterials.Pal.Rail, true, 2f);
                    for (int p = 0; p < 7; p++)
                        _ground.Box(new Vector3(riverL + 3 + p * (Block - 6) / 6f, 0.7f, z + side * (Road * 0.5f - 0.2f)), new Vector3(0.6f, 1.4f, 0.6f), LatMaterials.Pal.Wall, false, 2f);
                }
                _ground.Box(new Vector3(bx, -1.8f, z), new Vector3(3f, 2f, Road * 0.7f), LatMaterials.Pal.Wall, true, 2f);
                _city.places[$"Bridge{k}"] = new Vector3(bx, 0.2f, z);
            }

            // road graph: every grid intersection, linked to its neighbours
            var grid = new RoadNetwork.Node[NX + 1, NZ + 1];
            for (int i = 0; i <= NX; i++)
                for (int k = 0; k <= NZ; k++)
                    grid[i, k] = _city.roads.Add(new Vector3(RoadX(i), 0, RoadZ(k)));
            for (int i = 0; i <= NX; i++)
                for (int k = 0; k <= NZ; k++)
                {
                    if (i < NX) RoadNetwork.Link(grid[i, k], grid[i + 1, k]);
                    if (k < NZ) RoadNetwork.Link(grid[i, k], grid[i, k + 1]);
                }
            _city.roads.laneOffset = Road * 0.25f;

            // coins along some roads, H&R style trails
            for (int n = 0; n < 22; n++)
            {
                bool alongX = _rng.NextDouble() < 0.5;
                int line = alongX ? RI(0, NZ + 1) : RI(0, NX + 1);
                float start = alongX ? R(xMin + 20, xMax - 60) : R(zMin + 20, zMax - 60);
                float lane = _rng.NextDouble() < 0.5 ? -Road * 0.25f : Road * 0.25f;
                for (int c = 0; c < 5; c++)
                {
                    var p = alongX ? new Vector3(start + c * 4f, 1f, RoadZ(line) + lane) : new Vector3(RoadX(line) + lane, 1f, start + c * 4f);
                    _city.coinSpots.Add(p);
                }
            }
        }

        /// <summary>Kerb + sidewalk strips all round a block (striped KL kerbs), 3 m deep.</summary>
        void Kerbs(Vector3 c, string edge = "env_curb_edge")
        {
            float h = Block * 0.5f, seg = Block / 4f, stretch = seg / 10f;
            for (int t = 0; t < 4; t++)
            {
                float a = -h + seg * (t + 0.5f);
                Kit(edge, c + new Vector3(-h + 1.5f, 0, a), 0, stretch);     // west side (road to -X)
                Kit(edge, c + new Vector3(h - 1.5f, 0, a), 180, stretch);    // east
                Kit(edge, c + new Vector3(a, 0, h - 1.5f), 90, stretch);     // north
                Kit(edge, c + new Vector3(a, 0, -h + 1.5f), -90, stretch);   // south
            }
        }

        // ------------------------------------------------------------------ river + border
        void BuildRiver()
        {
            float x = BlockCenter(RiverCol, 0).x;
            float zLen = NZ * Pitch + 60;
            _ground.Box(new Vector3(x, -3.4f, 0), new Vector3(Block + 2, 2f, zLen), LatMaterials.Pal.Water, true, 0f);
            _city.places["River"] = new Vector3(x, -2.4f, 0);

            // riverside railings on both banks, with gaps where the bridges cross
            float rl = x - Block * 0.5f, rr = x + Block * 0.5f;
            float edge = NZ * Pitch * 0.5f + Road * 0.5f + 14f; // out to the border wall
            AddRail(rl, rr, -edge, RoadZ(0) - Road * 0.5f);
            for (int k = 1; k <= NZ; k++) AddRail(rl, rr, RoadZ(k - 1) + Road * 0.5f, RoadZ(k) - Road * 0.5f);
            AddRail(rl, rr, RoadZ(NZ) + Road * 0.5f, edge);

            // a few floating things so the river reads as water
            for (int i = 0; i < 12; i++)
                _ground.Box(new Vector3(x + R(-15, 15), -2.35f, R(-zLen * 0.45f, zLen * 0.45f)), new Vector3(R(1, 3), 0.05f, 0.15f), LatMaterials.Pal.RoadLine, false, 0f,
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
            // pavement strip around the map (split at the river)
            float rl = X0 + RiverCol * Pitch + Road * 0.5f, rr = rl + Block;
            foreach (float sz in new[] { -1f, 1f })
            {
                float z = sz * (hz + strip * 0.5f);
                _ground.Box(new Vector3((-hx - strip + rl) * 0.5f, -1.4f, z), new Vector3(rl - (-hx - strip), 3.2f, strip), pave, true, 1.2f);
                _ground.Box(new Vector3((rr + hx + strip) * 0.5f, -1.4f, z), new Vector3(hx + strip - rr, 3.2f, strip), pave, true, 1.2f);
            }
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
                float d = (i % 2 == 0 ? wz : wx) + 70f;
                var go = Prop("Prop_Skyline", dir * d, a + 180f, false);
                go.transform.localScale = new Vector3(3.6f, 1.3f, 1f);
                var go2 = Prop("Prop_Skyline", dir * (d + 50f) + Quaternion.Euler(0, a, 0) * Vector3.right * 90f, a + 180f, false);
                go2.transform.localScale = new Vector3(3f, 1.8f, 1f);
            }
        }

        // ------------------------------------------------------------------ blocks
        void BuildBlock(int col, int row, Zone zone)
        {
            var c = BlockCenter(col, row);
            if (zone == Zone.River) return;
            bool kampung = col < RiverCol;
            Color top = kampung ? LatMaterials.Pal.Grass : LatMaterials.Pal.Pavement;
            if (zone == Zone.Park || zone == Zone.KLTower || zone == Zone.PerdanaGardens || zone == Zone.BatuCaves ||
                zone == Zone.TuguNegara || zone == Zone.IstanaNegara) top = LatMaterials.Pal.Park;
            // block: striped kerbs + 3 m sidewalks from the kit, the inside filled with grass or paving
            _ground.ColliderOnly(c + new Vector3(0, G - 1.6f, 0), new Vector3(Block, 3.2f, Block));
            _ground.Box(c + new Vector3(0, G - 1.6f, 0), new Vector3(Block - 5.9f, 3.2f, Block - 5.9f), top, false, 0f);
            _ground.Box(c + new Vector3(0, -1.6f, 0), new Vector3(Block, 3.0f, Block), LatMaterials.Pal.Wall, false, 0f);
            Kerbs(c, zone == Zone.Brickfields ? "env_bf_curb_edge" : "env_curb_edge");
            _city.walkZones.Add(new Rect(c.x - 20.5f, c.z - 20.5f, 41f, 41f));

            switch (zone)
            {
                case Zone.Kampung: KampungBlock(c); break;
                case Zone.Home: HomeBlock(c); break;
                case Zone.Surau: SurauBlock(c); break;
                case Zone.Padang: PadangBlock(c); break;
                case Zone.Shops: ShopBlock(c, true); break;
                case Zone.ChowKit: ChowKitBlock(c); break;
                case Zone.Condo: CondoBlock(c); break;
                case Zone.Masjid: MasjidBlock(c); break;
                case Zone.Mamak: MamakBlock(c); break;
                case Zone.Pasar: PasarBlock(c); break;
                case Zone.Towers: TowersBlock(c); break;
                case Zone.Park: ParkBlock(c); break;
                case Zone.KLTower: KLTowerBlock(c); break;
                case Zone.Dataran: DataranBlock(c); break;
                case Zone.Dealer: DealerBlock(c); break;
                case Zone.BukitBintang: ShopBlock(c, true); if (!_city.places.ContainsKey("BukitBintang")) _city.places["BukitBintang"] = c + new Vector3(-21, G, 0); break;
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
            if (!kampung && zone != Zone.Park && zone != Zone.KLTower && zone != Zone.Dataran && zone != Zone.ChowKit &&
                zone != Zone.Brickfields && zone != Zone.PerdanaGardens && zone != Zone.BatuCaves && zone != Zone.StadiumMerdeka)
                StreetLamps(c);
        }

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
            go.isStatic = true;
            if (collider) ModelFactory.AddBoundsCollider(go, 0.05f);
            return go;
        }

        GameObject Tree(string model, Vector3 pos, float scale = 1f)
        {
            var go = ModelFactory.Spawn(model, pos, Quaternion.Euler(0, R(0, 360), 0), _props);
            go.transform.localScale = Vector3.one * scale;
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
            _city.places["Home"] = c + new Vector3(8, G, 0);
            _city.facings["Home"] = Quaternion.Euler(0, 90, 0);
            _city.places["HomeVerandah"] = c + new Vector3(3.2f, G, -2.8f);   // at the foot of the stairs
            _city.places["HomeCar"] = c + new Vector3(12, G + 0.3f, 7);
            _city.facings["HomeCar"] = Quaternion.Euler(0, 90, 0); // nose toward the road
            _city.places["HomeYard"] = c + new Vector3(10, G, -6);
            _city.places["PlayerSpawn"] = c + new Vector3(9, G + 0.1f, 2);
            _city.facings["PlayerSpawn"] = Quaternion.Euler(0, 90, 0);
        }

        void SurauBlock(Vector3 c)
        {
            Prop("Bld_Surau", c + new Vector3(0, G, 2), 180, true);
            Scatter(c, 6, new[] { "Prop_Palm", "Prop_Palm2" }, new List<Vector2> { new Vector2(0, 2) }, 9f);
            _city.places["Surau"] = c + new Vector3(0, G, -6);
        }

        void PadangBlock(Vector3 c)
        {
            // kampung football field: two goal frames made of posts
            foreach (float s in new[] { -1f, 1f })
            {
                var gz = c.z + s * 15f;
                _ground.Box(new Vector3(c.x - 3, 1.2f, gz), new Vector3(0.2f, 2.4f, 0.2f), LatMaterials.Pal.RoadLine, true);
                _ground.Box(new Vector3(c.x + 3, 1.2f, gz), new Vector3(0.2f, 2.4f, 0.2f), LatMaterials.Pal.RoadLine, true);
                _ground.Box(new Vector3(c.x, 2.4f, gz), new Vector3(6.2f, 0.2f, 0.2f), LatMaterials.Pal.RoadLine, true);
            }
            _ground.Box(new Vector3(c.x, 0.21f, c.z), new Vector3(30f, 0.02f, 0.25f), LatMaterials.Pal.RoadLine, false, 0f);
            _city.places["Padang"] = c + Vector3.up * G;
            Tree("Prop_RainTree", c + new Vector3(-18, G, 18));
            Tree("Prop_RainTree", c + new Vector3(18, G, -18));
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
                bike.isStatic = false;
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
                crate.isStatic = false;
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
                cr.isStatic = false;
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
            _city.places["ChowKit"] = c + new Vector3(0, G, 24.5f);
            _city.facings["ChowKit"] = Quaternion.Euler(0, 90, 0);
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
                    lamp.isStatic = false;
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
            _city.places["Brickfields"] = c + new Vector3(0, G, 24.5f);
            _city.facings["Brickfields"] = Quaternion.Euler(0, 90, 0);
        }

        /// <summary>
        /// LRT over the Klang river (triptych rail edge): viaduct segments from the P1 kit with
        /// the piers standing in the water (never on a bridge deck), and a two-car train shuttling
        /// along it.
        /// </summary>
        void BuildLrt()
        {
            float x = BlockCenter(RiverCol, 0).x, baseY = -2.4f;       // water surface
            void Seg(float z, float scale)
            {
                Kit("env_bf_rail_viaduct", new Vector3(x, baseY, z), 0, scale);
                foreach (float s in new[] { -1f, 1f })                   // piers are solid
                    _ground.ColliderOnly(new Vector3(x, baseY + 3.3f, z + s * 6f * scale), new Vector3(1.6f, 6.6f, 1.6f));
            }
            for (int k = 0; k <= NZ; k++)
            {
                float rz = RoadZ(k);
                Seg(rz, 1.1f);                                            // spans the bridge: piers at +/-6.6 m, clear of the deck
                if (k == NZ) break;
                Seg(rz + 19f, 0.8f);
                Seg(rz + 35f, 0.8f);
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
            sh.a = new Vector3(x - 1.8f, railY, RoadZ(0) + 8f);
            sh.b = new Vector3(x - 1.8f, railY, RoadZ(NZ) - 2f);
            sh.speed = 11f;
            sh.pause = 4f;
            train.position = sh.a;
            _city.places["LRT"] = new Vector3(x, railY, 0);
        }

        /// <summary>Far skyline backdrop cards (P1 kit) beyond the west and north map edges.</summary>
        void BuildSkyline()
        {
            float hx = NX * Pitch * 0.5f + Road * 0.5f, hz = NZ * Pitch * 0.5f + Road * 0.5f;
            foreach (float z in new[] { -120f, 0f, 120f })
                Kit("env_kb_skyline", new Vector3(-hx - 90f, 0, z), 90, 1f).transform.localScale = Vector3.one * 1.3f;
            foreach (float x in new[] { -110f, 0f, 110f })
                Kit("env_kb_skyline", new Vector3(x, 0, hz + 90f), 180, 1f).transform.localScale = Vector3.one * 1.3f;
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
            var hub = KitProp("env_lm_kl_sentral", c + new Vector3(0, G, -3f), 0);
            LetterSign(hub, "KL SENTRAL", new Vector3(0, 8.4f, 11.5f), 0.16f, new Color(0.7f, 0.1f, 0.1f));
            for (int i = 0; i < 6; i++)
                ModelFactory.UseProxyCollider(Prop("env_bollard", c + new Vector3(-12.5f + i * 5f, G, 17.2f), 0, false));
            foreach (float x in new[] { -17f, 17f })
                KitProp("env_planter", c + new Vector3(x, G, 16.5f), 0);
            _city.itemSpots.Add(c + new Vector3(0, G + 0.6f, 16f));
            _city.places["KLSentral"] = c + new Vector3(0, G, 24.5f);
            _city.facings["KLSentral"] = Quaternion.Euler(0, 90, 0);
        }

        /// <summary>Muzium Negara on its podium behind a lawn with hibiscus beds and palms.</summary>
        void MuziumNegaraBlock(Vector3 c)
        {
            var mu = KitProp("env_lm_muzium_negara", c + new Vector3(0, G, -3f), 0);
            LetterSign(mu, "MUZIUM NEGARA", new Vector3(0, 7.55f, 7.75f), 0.1f, new Color(0.35f, 0.2f, 0.08f));
            foreach (float x in new[] { -11f, 11f })
            {
                KitProp("env_pbg_flowerbed", c + new Vector3(x, G, 11f), 0);
                KitProp("env_palm", c + new Vector3(x * 1.55f, G, 12f), R(0, 360));
            }
            SignPost("MUZIUM NEGARA", c + new Vector3(0, G, 17.5f), 0);
            _city.itemSpots.Add(c + new Vector3(-6, G + 0.6f, 12f));
            _city.places["MuziumNegara"] = c + new Vector3(0, G, 24.5f);
            _city.facings["MuziumNegara"] = Quaternion.Euler(0, 90, 0);
        }

        /// <summary>Perdana Botanical Gardens: lake with lotus and a footbridge, a wakaf pavilion,
        /// bougainvillea pergola, hibiscus beds, orchid arch, fountain, benches, big shady trees.</summary>
        void PerdanaGardensBlock(Vector3 c)
        {
            var lakeAt = c + new Vector3(-3f, G, -2f);
            Prop("env_pbg_lake", lakeAt, 0, false);
            Prop("env_pbg_footbridge", lakeAt + new Vector3(12.5f, 0, 0), 0, false);
            KitProp("env_pbg_gazebo", c + new Vector3(-14.5f, G, 13.5f), 0);
            Prop("env_pbg_pergola", c + new Vector3(15.5f, G, -8f), 0, false);
            KitProp("env_pbg_fountain", c + new Vector3(14f, G, 12.5f), 0);
            Prop("env_pbg_orchid_arch", c + new Vector3(-15.5f, G, -14f), 90, false);
            foreach (float x in new[] { -6f, 2f })
                KitProp("env_pbg_flowerbed", c + new Vector3(x, G, 16.5f), 0);
            foreach (var b in new[] { new Vector3(-10, 0, 10), new Vector3(4, 0, 10.5f), new Vector3(-18, 0, -4), new Vector3(8, 0, -14) })
                KitProp("env_pbg_bench", c + b + Vector3.up * G, Mathf.Atan2(-b.x, -b.z) * Mathf.Rad2Deg);
            foreach (var t in new[] { new Vector3(-17, 0, 3), new Vector3(17, 0, 3), new Vector3(-6, 0, -17), new Vector3(3, 0, 17.5f) })
                KitProp("env_kb_rain_tree", c + t + Vector3.up * G, R(0, 360), R(0.9f, 1.15f));
            KitProp("env_palm", c + new Vector3(10, G, 16), 0);
            SignPost("TAMAN BOTANI PERDANA", c + new Vector3(-6f, G, 19f), 0);
            _city.itemSpots.Add(c + new Vector3(-14.5f, G + 1.6f, 13.5f));
            _city.itemSpots.Add(c + new Vector3(15.5f, G + 0.6f, -8f));
            _city.places["TamanPerdana"] = c + new Vector3(0, G, 24.5f);
            _city.facings["TamanPerdana"] = Quaternion.Euler(0, 90, 0);
        }

        /// <summary>Masjid Negara: star-roofed prayer hall, reflecting pools and the minaret.</summary>
        void MasjidNegaraBlock(Vector3 c)
        {
            KitProp("env_lm_masjid_negara", c + new Vector3(0, G, 0), 0);
            // the minaret is outside the hall proxy: give it its own collider
            _ground.ColliderOnly(c + new Vector3(-13.5f, G + 18f, 13.5f), new Vector3(2.6f, 36f, 2.6f));
            SignPost("MASJID NEGARA", c + new Vector3(8f, G, 19.2f), 0);
            _city.places["MasjidNegara"] = c + new Vector3(0, G, 24.5f);
            _city.facings["MasjidNegara"] = Quaternion.Euler(0, 90, 0);
        }

        // ------------------------------------------------------------------ east side landmarks (env_kl_landmarks2)
        /// <summary>A landmark filling its block, a lettered sign post on the south pavement, a
        /// mission item spot and a named place (+ facing) for missions and fast travel.</summary>
        void Landmark(Vector3 c, string model, string place, string sign, float yaw)
        {
            KitProp(model, c + new Vector3(0, G, 0), yaw);
            SignPost(sign, c + new Vector3(12f, G, 19.2f), 0);
            foreach (float x in new[] { -17f, 17f }) KitProp("env_palm", c + new Vector3(x, G, 17.5f), R(0, 360));
            _city.itemSpots.Add(c + new Vector3(-12f, G + 0.6f, 16f));
            _city.places[place] = c + new Vector3(0, G, 24.5f);
            _city.facings[place] = Quaternion.Euler(0, 90, 0);
        }

        /// <summary>Batu Caves: the limestone hill, the golden statue and the rainbow stairs up to
        /// the cave. The stairs are climbable (a ramp collider under the steps).</summary>
        void BatuCavesBlock(Vector3 c)
        {
            Landmark(c, "env_lm2_batu_caves", "BatuCaves", "BATU CAVES", 0);
            // stairs run from the plaza (front, +Z in the world after the kit's turn) up into the hill
            float run = 12f, rise = 17.5f, len = Mathf.Sqrt(run * run + rise * rise);
            var ramp = new GameObject("BatuStairsRamp");
            ramp.transform.SetParent(_props, false);
            ramp.transform.position = c + new Vector3(0, G + rise * 0.5f, 3f);
            ramp.transform.rotation = Quaternion.Euler(Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0, 0);
            ramp.AddComponent<BoxCollider>().size = new Vector3(6f, 0.3f, len);
            // statue pedestal is solid
            _ground.ColliderOnly(c + new Vector3(9.5f, G + 7f, 10.5f), new Vector3(5f, 14f, 5f));
        }

        /// <summary>Stadium Merdeka: an open bowl you can drive into - only the outer wall and the
        /// floodlight towers are solid, with a gap on the south side as the tunnel entrance.</summary>
        void StadiumBlock(Vector3 c)
        {
            Prop("env_lm2_stadium_merdeka", c + new Vector3(0, G, 0), 0, false);
            float rx = 13 * 1.47f, ry = 16 * 1.47f;
            int n = 28;
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * rx, 0, Mathf.Sin(a0) * ry);
                var p1 = new Vector3(Mathf.Cos(a1) * rx, 0, Mathf.Sin(a1) * ry);
                var mid = (p0 + p1) * 0.5f;
                if (mid.z > ry * 0.9f) continue;                        // the entrance gap (north side in the kit = world south)
                var go = new GameObject("StadiumWall");
                go.transform.SetParent(_props, false);
                go.transform.position = c + mid + Vector3.up * (G + 3.3f);
                go.transform.rotation = Quaternion.LookRotation(p1 - p0);
                go.AddComponent<BoxCollider>().size = new Vector3(1.2f, 6.6f, (p1 - p0).magnitude + 0.2f);
            }
            SignPost("STADIUM MERDEKA", c + new Vector3(12f, G, 20.5f), 0);
            _city.itemSpots.Add(c + new Vector3(0, G + 0.6f, 0));
            _city.places["StadiumMerdeka"] = c + new Vector3(0, G, 24.5f);
            _city.facings["StadiumMerdeka"] = Quaternion.Euler(0, 90, 0);
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
            KitProp("env_lm2_masjid_jamek", c + new Vector3(2, G, 0), -90);
            _city.places["Masjid"] = c + new Vector3(-16, G, 0);
            Tree("Prop_Palm", c + new Vector3(-16, G, 14));
            Tree("Prop_Palm2", c + new Vector3(-16, G, -14));
            Tree("Prop_Palm", c + new Vector3(16, G, 16));
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
            _city.places["Mamak"] = c + new Vector3(-8, G, 16);
            _city.places["MamakCounter"] = c + new Vector3(-8, G, 4.3f);
            _city.facings["MamakCounter"] = Quaternion.identity;
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
            _city.places["Pasar"] = c + new Vector3(0, G, 0);
            var gate = Prop("Prop_ChinatownGate", c + new Vector3(-19.5f, G, 0), 90, false);
            foreach (float gz in new[] { -6.5f, 6.5f })
            {
                var gc = gate.AddComponent<CapsuleCollider>(); gc.center = new Vector3(gz, 3, 0); gc.radius = 0.5f; gc.height = 6;
            }
            _city.places["PasarGate"] = c + new Vector3(-24, G, 0);
        }

        void TowersBlock(Vector3 c)
        {
            var t = Prop("Bld_MenaraKembar", c + new Vector3(0, G, -2), 180, false, 0.82f);
            // colliders: podium + two tower shafts
            var pod = t.AddComponent<BoxCollider>(); pod.center = new Vector3(0, 6, -6); pod.size = new Vector3(50, 12, 28);
            foreach (float sx in new[] { -14f, 14f })
            {
                var cap = t.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(sx, 60, 0); cap.radius = 8.5f; cap.height = 120;
            }
            _city.places["Towers"] = c + new Vector3(0, G, -21);
        }

        void ParkBlock(Vector3 c)
        {
            _ground.Box(c + new Vector3(0, 0.45f, 0), new Vector3(10, 0.5f, 10), LatMaterials.Pal.Wall, true);
            _ground.Box(c + new Vector3(0, 0.55f, 0), new Vector3(9, 0.5f, 9), LatMaterials.Pal.Water, false, 0f);
            _ground.Box(c + new Vector3(0, 1.6f, 0), new Vector3(0.6f, 2.4f, 0.6f), LatMaterials.Pal.RoadLine, true);
            Scatter(c, 10, new[] { "Prop_RainTree", "Prop_Palm" }, new List<Vector2> { Vector2.zero }, 9f);
            _city.places["Park"] = c + new Vector3(0, G, -8);
        }

        void KLTowerBlock(Vector3 c)
        {
            // Bukit Nanas: a raised mound of forest with the tower on top
            _ground.Box(c + new Vector3(0, 0.9f, 0), new Vector3(34, 1.6f, 34), LatMaterials.Pal.Park, true, 1.4f);
            _ground.Box(c + new Vector3(0, 1.8f, 0), new Vector3(22, 1.6f, 22), LatMaterials.Pal.Park, true, 1.4f);
            var t = Prop("Bld_MenaraKL", c + new Vector3(0, 2.6f, 0), 0, false, 0.9f);
            var cap = t.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 40, 0); cap.radius = 3.5f; cap.height = 80;
            var bc = t.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 1.5f, 0); bc.size = new Vector3(16, 3, 16);
            var used = new List<Vector2> { Vector2.zero };
            for (int i = 0; i < 14; i++)
            {
                var a = i / 14f * Mathf.PI * 2f;
                var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(12, 18);
                Tree(i % 3 == 0 ? "Prop_Palm" : "Prop_RainTree", c + new Vector3(p.x, i % 2 == 0 ? 1.7f : 0.2f, p.y), R(0.9f, 1.3f));
            }
            _city.places["KLTower"] = c + new Vector3(0, 3.4f, -12);
        }

        void DataranBlock(Vector3 c)
        {
            // Dataran Merdeka: the padang, the tall flagpole, and the Sultan Abdul Samad building
            _ground.Box(c + new Vector3(7, 0.22f, 0), new Vector3(28, 0.06f, 40), LatMaterials.Pal.Park, false, 0f);
            Prop("Bld_SultanAbdulSamad", c + new Vector3(-14, G, 0), 90, true);
            var pole = Prop("Prop_Flagpole", c + new Vector3(8, G, 0), 0, false);
            var cap = pole.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 10, 0); cap.radius = 0.4f; cap.height = 20;
            var bc = pole.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.4f, 0); bc.size = new Vector3(5, 0.8f, 5);
            _city.places["Dataran"] = c + new Vector3(8, G, -8);
            _city.places["DataranRoad"] = c + new Vector3(21, G, 0);
            Sign("DATARAN MERDEKA", c + new Vector3(20.5f, 2.2f, -12), 90);
            Tree("Prop_RainTree", c + new Vector3(18, G, 18));
            Tree("Prop_RainTree", c + new Vector3(18, G, -18));
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
            _city.places["Dealer"] = dealer.transform.position;
            // clothes shop on the south side
            var shop = new GameObject("KedaiBaju");
            shop.transform.SetParent(_props, false);
            shop.transform.SetPositionAndRotation(c + new Vector3(8, G, -19f), Quaternion.Euler(0, 180, 0));
            shop.AddComponent<ClothesShop>();
            Sign("KEDAI BAJU", c + new Vector3(8, 4.2f, -17.6f), 180);
            _city.places["ClothesShop"] = shop.transform.position;
        }

        void PasarSeniBlock(Vector3 c)
        {
            Prop("Bld_PasarSeni", c + new Vector3(0, G, -4), 180, true);
            _city.places["PasarSeni"] = c + new Vector3(0, G, -18);
            Sign("PASAR SENI", c + new Vector3(0, 11.5f, -13.4f), 180);
            ShopRow(c, -8, true, true, 1);
            ShopRow(c, 8, true, true, 0);
        }

        void BuildMonorail()
        {
            // Bukit Bintang monorail: pillars down the middle of road line 6, a beam on top,
            // and a little train shuttling up and down it.
            float x = RoadX(6);
            float z0 = RoadZ(0), z1 = RoadZ(NZ);
            var steel = new Color(0.82f, 0.82f, 0.8f);
            for (float z = z0; z <= z1; z += 14.5f)
            {
                float gz = Mathf.Repeat(z - Z0 + 10f, Pitch);
                if (gz < 20f) continue; // keep intersections clear
                _ground.Box(new Vector3(x, 4f, z), new Vector3(1.1f, 8f, 1.1f), steel, true, 2f);
                _ground.Box(new Vector3(x, 8.1f, z), new Vector3(3.2f, 0.6f, 1.6f), steel, false, 2f);
            }
            _ground.Box(new Vector3(x, 8.8f, (z0 + z1) * 0.5f), new Vector3(1.0f, 0.9f, z1 - z0 + 10f), steel, false, 1.8f);
            var train = new GameObject("Monorail").transform;
            train.SetParent(_city.root, false);
            var body = new Color(0.55f, 0.75f, 0.45f);
            for (int i = 0; i < 2; i++)
            {
                Shapes.Box("Car" + i, new Vector3(0, 10.6f, i * 11f), new Vector3(2.8f, 2.8f, 10.5f), body, train, false);
                Shapes.Box("Win" + i, new Vector3(0, 11f, i * 11f), new Vector3(2.9f, 1.0f, 9f), new Color(0.3f, 0.45f, 0.55f), train, false);
            }
            train.position = new Vector3(x, 0, z0);
            var mt = train.gameObject.AddComponent<Shuttle>();
            mt.a = new Vector3(x, 0, z0 + 5);
            mt.b = new Vector3(x, 0, z1 - 20);
            mt.speed = 9f;
            _city.places["Monorail"] = new Vector3(x, 0, 0);
        }

        void BuildPhoneBooths()
        {
            void Booth(string key, Vector3 pos, float yaw)
            {
                var b = Prop("Prop_PhoneBooth", pos, yaw, true);
                b.AddComponent<PhoneBooth>();
                _city.places["Phone_" + key] = pos;
            }
            var home = BlockCenter(1, 2);
            Booth("Home", home + new Vector3(18, G, -12), 90);
            Booth("Mamak", BlockCenter(3, 2) + new Vector3(4, G, 19.5f), 0);
            Booth("Dataran", BlockCenter(3, 4) + new Vector3(19.5f, G, 8), 90);
            Booth("Towers", BlockCenter(5, 5) + new Vector3(-12, G, 19.5f), 0);
            Booth("KLTower", BlockCenter(6, 0) + new Vector3(-19.5f, G, 16), -90);
            Booth("Surau", BlockCenter(0, 4) + new Vector3(12, G, -18), 180);
        }

        /// <summary>District name for the HUD, H&amp;R-style area callouts.</summary>
        public static string District(Vector3 p)
        {
            int col = Mathf.FloorToInt((p.x - X0) / Pitch), row = Mathf.FloorToInt((p.z - Z0) / Pitch);
            if (col < 0 || col >= NX || row < 0 || row >= NZ) return "Pinggir KL";
            if (col < RiverCol) return row == 4 ? "Kampung Baru - Surau" : "Kampung Baru";
            if (col == RiverCol) return "Sungai Klang";
            switch (ZoneAt(col, row))
            {
                case Zone.Towers: case Zone.Park: return "KLCC";
                case Zone.KLTower: return "Bukit Nanas";
                case Zone.Dataran: return "Dataran Merdeka";
                case Zone.Masjid: return "Masjid Jamek";
                case Zone.Pasar: return "Petaling Street";
                case Zone.PasarSeni: return "Pasar Seni";
                case Zone.BukitBintang: return "Bukit Bintang";
                case Zone.Mamak: return "Jalan Tun Perak";
                case Zone.Dealer: return "Jalan TAR";
                case Zone.ChowKit: return "Chow Kit";
                case Zone.Brickfields: return "Brickfields";
                case Zone.KLSentral: return "KL Sentral";
                case Zone.MuziumNegara: return "Muzium Negara";
                case Zone.PerdanaGardens: return "Taman Botani Perdana";
                case Zone.MasjidNegara: return "Masjid Negara";
                case Zone.BatuCaves: return "Batu Caves";
                case Zone.TuguNegara: return "Tugu Negara";
                case Zone.Pavilion: return "Bukit Bintang - Pavilion";
                case Zone.Merdeka118: return "Merdeka 118";
                case Zone.StadiumMerdeka: return "Stadium Merdeka";
                case Zone.TheanHou: return "Thean Hou";
                case Zone.IstanaNegara: return "Istana Negara";
            }
            if (col >= 7) return row >= 3 ? "Setapak" : "Cheras";
            if (row >= 4) return col >= 5 ? "Ampang" : "Chow Kit";
            if (row <= 1) return col >= 5 ? "Bukit Bintang" : "Brickfields";
            return "Jalan TAR";
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
        /// Street life: traffic lights, bus stops, Jalur Gemilang bunting, satay carts,
        /// angsana trees, and chickens/cats pottering about.
        /// </summary>
        void BuildStreetLife()
        {
            // traffic lights on the city intersections (two opposite corners each)
            for (int i = RiverCol + 1; i <= NX; i++)
                for (int k = 0; k <= NZ; k++)
                {
                    var n = new Vector3(RoadX(i), G, RoadZ(k));
                    if (Mathf.Abs(n.x) > 200 || Mathf.Abs(n.z) > 170) continue;
                    TrafficLight(n + new Vector3(Road * 0.5f + 1.2f, 0, Road * 0.5f + 1.2f), -90);
                    TrafficLight(n + new Vector3(-Road * 0.5f - 1.2f, 0, -Road * 0.5f - 1.2f), 90);
                }
            // bus stops + a bench and bin on some block edges
            for (int col = RiverCol + 1; col < NX; col++)
                for (int row = 0; row < NZ; row++)
                {
                    if (_rng.NextDouble() > 0.45) continue;
                    var c = BlockCenter(col, row);
                    bool east = _rng.NextDouble() < 0.5;
                    var pos = c + new Vector3(east ? 20.6f : -20.6f, G, R(-6, 6));
                    Prop("Prop_BusStop", pos, east ? 90 : -90, true);
                    Prop("Prop_Bin", pos + new Vector3(0, 0, 3.2f), 0, true);
                }
            // bunting across the streets around Petaling Street, Bukit Bintang and Dataran
            foreach (var (col, row) in new[] { (4, 2), (4, 3), (5, 2), (6, 3), (6, 4), (3, 4), (5, 3) })
            {
                var c = BlockCenter(col, row);
                Prop("Prop_Bunting", new Vector3(c.x, G, RoadZ(row)), 90, false);
                Prop("Prop_Bunting", new Vector3(RoadX(col), G, c.z), 0, false);
            }
            // hawker satay carts with umbrellas
            foreach (var key in new[] { "Pasar", "Mamak", "Padang", "Dataran", "PasarSeni", "BukitBintang" })
                if (_city.places.TryGetValue(key, out var p))
                {
                    // jitter the spot, but never into a carriageway (some places sit right at a block edge)
                    var at = p;
                    for (int t = 0; t < 12; t++)
                    {
                        at = p + new Vector3(R(-3, 3), 0, R(-3, 3));
                        if (!OnRoad(at)) break;
                        at = p + (BlockCenter(Mathf.FloorToInt((p.x - X0) / Pitch), Mathf.FloorToInt((p.z - Z0) / Pitch)) - p).normalized * 3f;
                    }
                    var cart = Prop("Prop_SatayCart", at, R(0, 360), false);
                    var bc = cart.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.6f, 0); bc.size = new Vector3(1.8f, 1.2f, 0.9f);
                }
            // chickens in the kampung, cats in the city
            for (int col = 0; col < RiverCol; col++)
                for (int row = 0; row < NZ; row++)
                {
                    var c = BlockCenter(col, row);
                    int n = RI(2, 5);
                    for (int i = 0; i < n; i++) Critter("Prop_Ayam", c + new Vector3(R(-15, 15), G, R(-15, 15)), true);
                }
            for (int i = 0; i < 12; i++)
            {
                var c = BlockCenter(RI(RiverCol + 1, NX), RI(0, NZ));
                Critter("Prop_Kucing", c + new Vector3(R(-18, 18), G, (_rng.NextDouble() < 0.5 ? 1 : -1) * 19.5f), false);
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
                foreach (float a in new[] { -14f, 14f })
                {
                    var lamp = Prop("env_lamp_post", c + dir * 21.1f + side * a + Vector3.up * G, yaw, false);
                    lamp.isStatic = false;
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
