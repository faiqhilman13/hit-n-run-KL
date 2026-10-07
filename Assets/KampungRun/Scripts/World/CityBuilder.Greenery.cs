using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// KL is a green city: rain trees and angsana arching over the roads, palm boulevards, the kampung under its
    /// coconut palms and fruit trees. The filler districts get the same as the real-KL patches (Tools/klmap/patches.py):
    ///  - street trees down every pavement, 0.9 m in from the kerb about every 11 m, one kind to a stretch of road;
    ///  - the open ground of the lots planted on a jittered grid: thick in the kampung and the parks, a few round
    ///    the flats and offices and in the shophouses' back courts.
    /// Each tree goes where its trunk is clear of what already stands there (lamps, stops, booths, stalls, other
    /// trunks) and is sized so its crown clears the buildings, decks and footbridges: a rain tree that won't fit
    /// becomes an angsana, then a frangipani, then nothing. Seeded from where they stand (no dice drawn), after
    /// everything else is built, so the rest of the city is exactly as it was.
    /// </summary>
    public partial class CityBuilder
    {
        // crown at scale 1 (Arch/Flora.cs): radius, and the heights it spans
        static void CrownOf(Arch.FloraKind k, out float radius, out float bottom, out float top)
        {
            switch (k)
            {
                case Arch.FloraKind.RainTree: radius = 4.9f; bottom = 3.0f; top = 6.6f; break;
                case Arch.FloraKind.Angsana: radius = 3.3f; bottom = 3.0f; top = 7.5f; break;
                case Arch.FloraKind.Palm: radius = 4.6f; bottom = 3.2f; top = 9.9f; break;
                case Arch.FloraKind.Frangipani: radius = 3.1f; bottom = 2.6f; top = 5.6f; break;
                default: radius = 1.2f; bottom = 0.3f; top = 2.0f; break;                    // banana, shrubs
            }
        }

        static float MinScale(Arch.FloraKind k) => k == Arch.FloraKind.RainTree ? 0.85f : k == Arch.FloraKind.Frangipani ? 0.6f : 0.75f;

        static Vector2 ScaleRange(Arch.FloraKind k) => k switch
        {
            Arch.FloraKind.RainTree => new Vector2(1.1f, 1.45f),
            Arch.FloraKind.Angsana => new Vector2(1.0f, 1.25f),
            Arch.FloraKind.Palm => new Vector2(0.95f, 1.25f),
            Arch.FloraKind.Frangipani => new Vector2(0.85f, 1.05f),
            Arch.FloraKind.Banana => new Vector2(0.9f, 1.2f),
            _ => new Vector2(0.9f, 1.2f),
        };

        static Arch.FloraKind Smaller(Arch.FloraKind k) => k switch
        {
            Arch.FloraKind.RainTree => Arch.FloraKind.Angsana,
            Arch.FloraKind.Palm => Arch.FloraKind.Angsana,
            Arch.FloraKind.Angsana => Arch.FloraKind.Frangipani,
            _ => Arch.FloraKind.Tufts,                                                       // (nothing smaller)
        };

        /// <summary>A number in [0, 1) from a spot and a salt: no dice drawn from the city's generator.</summary>
        static float Hash01(float x, float z, int salt)
        {
            unchecked
            {
                uint h = (uint)(Mathf.RoundToInt(x * 4f) * 73856093) ^ (uint)(Mathf.RoundToInt(z * 4f) * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
                return (h & 0xffffff) / 16777216f;
            }
        }

        static Arch.FloraKind PickKind(float u, (Arch.FloraKind kind, float weight)[] mix)
        {
            float sum = 0f;
            foreach (var m in mix) sum += m.weight;
            u *= sum;
            foreach (var m in mix) { u -= m.weight; if (u <= 0f) return m.kind; }
            return mix[mix.Length - 1].kind;
        }

        static readonly (Arch.FloraKind, float)[] TownStreets = { (Arch.FloraKind.RainTree, 45), (Arch.FloraKind.Angsana, 35), (Arch.FloraKind.Palm, 20) };
        static readonly (Arch.FloraKind, float)[] KampungStreets = { (Arch.FloraKind.Palm, 50), (Arch.FloraKind.RainTree, 30), (Arch.FloraKind.Angsana, 20) };
        // the lots: (mix, spacing in m)
        static readonly (Arch.FloraKind, float)[] KampungYard =
        {
            (Arch.FloraKind.Palm, 34), (Arch.FloraKind.Angsana, 26), (Arch.FloraKind.Banana, 18), (Arch.FloraKind.RainTree, 10),
            (Arch.FloraKind.Frangipani, 6), (Arch.FloraKind.Shrub, 6),
        };
        static readonly (Arch.FloraKind, float)[] ParkTrees = { (Arch.FloraKind.RainTree, 40), (Arch.FloraKind.Angsana, 30), (Arch.FloraKind.Palm, 15), (Arch.FloraKind.Frangipani, 15) };
        static readonly (Arch.FloraKind, float)[] FlatsGarden = { (Arch.FloraKind.Angsana, 40), (Arch.FloraKind.Palm, 30), (Arch.FloraKind.Frangipani, 20), (Arch.FloraKind.RainTree, 10) };
        static readonly (Arch.FloraKind, float)[] OfficePlaza = { (Arch.FloraKind.Palm, 45), (Arch.FloraKind.Angsana, 40), (Arch.FloraKind.Frangipani, 15) };
        static readonly (Arch.FloraKind, float)[] BackCourt = { (Arch.FloraKind.Angsana, 50), (Arch.FloraKind.Frangipani, 30), (Arch.FloraKind.Palm, 20) };
        static readonly (Arch.FloraKind, float)[] SurauGarden = { (Arch.FloraKind.Palm, 50), (Arch.FloraKind.Angsana, 30), (Arch.FloraKind.Frangipani, 20) };

        // physics masks for the checks: anything solid (the trunk's spot) / anything big (the crown)
        static int TrunkMask => ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle | 1 << 2);
        static int CrownMask => TrunkMask & ~(1 << Layers.Detail);

        readonly Dictionary<long, List<Vector2>> _greenGrid = new Dictionary<long, List<Vector2>>();
        readonly Dictionary<long, List<Vector3>> _greenSpots = new Dictionary<long, List<Vector3>>();   // (x, z, radius)
        readonly List<Vector2> _floraAt = new List<Vector2>();      // the blocks' own trees and plants (AddFlora)

        static long GreenKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        bool ClearOfTrees(Vector2 p, float r)
        {
            int gx = Mathf.FloorToInt(p.x / 6f), gz = Mathf.FloorToInt(p.y / 6f);
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    if (_greenGrid.TryGetValue(GreenKey(gx + i, gz + j), out var l))
                        foreach (var q in l) if ((q - p).sqrMagnitude < r * r) return false;
            return true;
        }

        void MarkTree(Vector2 p)
        {
            var k = GreenKey(Mathf.FloorToInt(p.x / 6f), Mathf.FloorToInt(p.y / 6f));
            if (!_greenGrid.TryGetValue(k, out var l)) _greenGrid[k] = l = new List<Vector2>();
            l.Add(p);
        }

        /// <summary>Near a coin, an item or a named place (missions stand there)? Spots are at most 6 m round.</summary>
        bool NearSpot(Vector3 p)
        {
            int gx = Mathf.FloorToInt(p.x / 6f), gz = Mathf.FloorToInt(p.z / 6f);
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    if (_greenSpots.TryGetValue(GreenKey(gx + i, gz + j), out var l))
                        foreach (var q in l)
                        {
                            float dx = q.x - p.x, dz = q.y - p.z;
                            if (dx * dx + dz * dz < q.z * q.z) return true;
                        }
            return false;
        }

        void MarkSpot(Vector3 p, float r)
        {
            var k = GreenKey(Mathf.FloorToInt(p.x / 6f), Mathf.FloorToInt(p.z / 6f));
            if (!_greenSpots.TryGetValue(k, out var l)) _greenSpots[k] = l = new List<Vector3>();
            l.Add(new Vector3(p.x, p.z, Mathf.Min(r, 6f)));
        }

        /// <summary>Does a crown of this kind and scale at p touch anything big (buildings, decks, footbridges)?</summary>
        static bool CrownClear(Vector3 p, Arch.FloraKind k, float s)
        {
            CrownOf(k, out float r, out float b, out float t);
            var c = p + Vector3.up * ((b + t) * 0.5f * s);
            var half = new Vector3(r * s * 0.8f, (t - b) * 0.5f * s, r * s * 0.8f);
            return !Physics.CheckBox(c, half, Quaternion.identity, CrownMask, QueryTriggerInteraction.Ignore) &&
                   !Physics.CheckBox(c, half, Quaternion.Euler(0f, 45f, 0f), CrownMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Plant the biggest tree that fits at p, starting from `kind` at `scale` (shrinking it, then trying the next
        /// smaller kind), its trunk `room` m clear of anything standing. False if nothing fits.
        /// </summary>
        bool Plant(Vector3 p, Arch.FloraKind kind, float scale, float room, List<Vector3> cv, List<int> ct)
        {
            // under cover: inside a building (a spot deep in one touches none of its walls), a deck, a footbridge
            if (Physics.Raycast(p + Vector3.up * 400f, Vector3.down, out var hit, 410f, CrownMask, QueryTriggerInteraction.Ignore) && hit.point.y > p.y + 0.4f)
                return false;
            if (Physics.CheckCapsule(p + Vector3.up * (room + 0.25f), p + Vector3.up * Mathf.Max(room + 0.3f, 3f), room, TrunkMask, QueryTriggerInteraction.Ignore))
                return false;
            for (var k = kind; k != Arch.FloraKind.Tufts; k = Smaller(k))
            {
                float s = k == kind ? scale : ScaleRange(k).y;
                for (int tries = 0; tries < 3 && s >= MinScale(k); tries++, s *= 0.85f)
                {
                    if (!CrownClear(p, k, s)) continue;
                    var f = new Arch.FloraSpec { pos = p, kind = k, scale = s, seed = Mathf.RoundToInt(p.x * 7.13f) * 73856093 ^ Mathf.RoundToInt(p.z * 5.31f) * 19349663 };
                    _arch.Add(f);
                    if (k != Arch.FloraKind.Banana && k != Arch.FloraKind.Shrub) Arch.Flora.Collider(f, cv, ct);
                    MarkTree(new Vector2(p.x, p.z));
                    return true;
                }
                if (k == Arch.FloraKind.Banana || k == Arch.FloraKind.Shrub) break;   // (low: the crown check is all they need)
            }
            return false;
        }

        void BuildGreenery()
        {
            Physics.SyncTransforms();
            _greenGrid.Clear();
            _greenSpots.Clear();
            foreach (var f in _floraAt) MarkTree(f);
            foreach (var p in _city.places.Values) MarkSpot(p, 5f);
            foreach (var p in _city.itemSpots) MarkSpot(p, 1.5f);
            foreach (var p in _city.coinSpots) MarkSpot(p, 1.5f);
            var cv = new List<Vector3>();
            var ct = new List<int>();
            int street = 0, lots = 0;
            bool lite = GameManager.WebLite;
            for (int col = 0; col < NX; col++)
                for (int row = 0; row < NZ; row++)
                {
                    var zone = ZoneAt(col, row);
                    if (zone == Zone.Patch || zone == Zone.River || zone == Zone.ChowKit || IsLandmark(zone)) continue;
                    street += StreetTreesRound(col, row, IsKampungCell(col, row), lite, cv, ct);
                }
            for (int col = 0; col < NX; col++)
                for (int row = 0; row < NZ; row++)
                {
                    var zone = ZoneAt(col, row);
                    if (zone == Zone.Patch || zone == Zone.River || IsLandmark(zone)) continue;
                    var c = BlockCenter(col, row);
                    for (int lot = 0; lot < 4; lot++)
                    {
                        var lc = c + new Vector3(LotOffsets[lot].x, 0, LotOffsets[lot].y) * ((Lot + Lane) * 0.5f);
                        lots += LotTrees(lc, RoundaboutLot(col, row, lot) ? Zone.Park : LotZone(zone, lot), lite, cv, ct);
                    }
                }
            int belt = ForestBelt(lite);
            if (ct.Count > 0)
            {
                var m = new Mesh { name = "Greenery_collider", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(cv);
                m.SetTriangles(ct, 0);
                var go = new GameObject("Greenery");
                go.transform.SetParent(_city.root, false);
                go.AddComponent<MeshCollider>().sharedMesh = m;
            }
            Debug.Log($"[Green] {street} street trees, {lots} in the lots, {belt} round the edge");
        }

        static readonly (Arch.FloraKind, float)[] BeltTrees = { (Arch.FloraKind.RainTree, 55), (Arch.FloraKind.Angsana, 30), (Arch.FloraKind.Palm, 15) };

        /// <summary>
        /// The green the city sits in, the way KL sits in its hills: forest floor and two staggered rows of big trees
        /// all round outside the border wall (out of reach, so no trunks to bump into), closing the long views down the
        /// roads and the edge of the map from the air.
        /// </summary>
        int ForestBelt(bool lite)
        {
            float hx = NX * Pitch * 0.5f + Road * 0.5f + 14f + 0.5f, hz = NZ * Pitch * 0.5f + Road * 0.5f + 14f + 0.5f;   // the wall
            const float depth = 70f;
            var floor = LatMaterials.Pal.Park * 0.82f;
            floor.a = 1f;
            foreach (float s in new[] { -1f, 1f })
            {
                _ground.Box(new Vector3(0f, -0.5f, s * (hz + depth * 0.5f)), new Vector3(hx * 2f + depth * 2f, 1f, depth), floor, false, 0f);
                _ground.Box(new Vector3(s * (hx + depth * 0.5f), -0.5f, 0f), new Vector3(depth, 1f, hz * 2f), floor, false, 0f);
            }
            int n = 0;
            float step = lite ? 16f : 11f;
            for (int row = 0; row < 2; row++)
            {
                float off = 7f + row * 11f;
                float x0 = -(hx + off), z0 = -(hz + off);
                // walk the ring of this row: four sides, the staggered row half a step along
                for (int side = 0; side < 4; side++)
                {
                    float len = side < 2 ? 2f * (hx + off) : 2f * (hz + off);
                    for (float a = (row == 0 ? 0f : step * 0.5f); a < len; a += step)
                    {
                        Vector3 p = side switch
                        {
                            0 => new Vector3(x0 + a, 0f, z0),               // south
                            1 => new Vector3(-x0 - a, 0f, -z0),             // north
                            2 => new Vector3(-x0, 0f, z0 + a),              // east
                            _ => new Vector3(x0, 0f, -z0 - a),              // west
                        };
                        p.x += (Hash01(p.x, p.z, 41) - 0.5f) * 4f;
                        p.z += (Hash01(p.x, p.z, 43) - 0.5f) * 4f;
                        var kind = PickKind(Hash01(p.x, p.z, 47), BeltTrees);
                        float sc = kind == Arch.FloraKind.RainTree ? Mathf.Lerp(1.35f, 1.9f, Hash01(p.x, p.z, 53))
                                 : kind == Arch.FloraKind.Angsana ? Mathf.Lerp(1.2f, 1.6f, Hash01(p.x, p.z, 53)) : Mathf.Lerp(1.0f, 1.3f, Hash01(p.x, p.z, 53));
                        _arch.Add(new Arch.FloraSpec { pos = p, kind = kind, scale = sc, seed = Mathf.RoundToInt(p.x * 7.13f) * 73856093 ^ Mathf.RoundToInt(p.z * 5.31f) * 19349663 });
                        n++;
                    }
                }
            }
            return n;
        }

        /// <summary>Street trees down the four pavements of a block (lit by the kerb, clear of the corners' crossings).</summary>
        int StreetTreesRound(int col, int row, bool kampung, bool lite, List<Vector3> cv, List<int> ct)
        {
            var c = BlockCenter(col, row);
            float h = Block * 0.5f, inset = 0.9f;
            int n = 0;
            for (int side = 0; side < 4; side++)
            {
                // the road this pavement runs along (both of its pavements, and three blocks of it, plant the same tree)
                bool ns = side < 2;
                int line = side == 0 ? col : side == 1 ? col + 1 : side == 2 ? row + 1 : row;
                int seg = (ns ? row : col) / 3;
                var kind = PickKind(Hash01(line * 17.3f, seg * 29.1f, ns ? 101 : 202), kampung ? KampungStreets : TownStreets);
                var range = ScaleRange(kind);
                Vector3 normal = side == 0 ? Vector3.left : side == 1 ? Vector3.right : side == 2 ? Vector3.forward : Vector3.back;
                Vector3 along = new Vector3(normal.z, 0f, -normal.x);
                float start = -40f + Hash01(c.x + side, c.z, 7) * 6f;
                for (float a = start; a <= 40f; a += 11f)
                {
                    float j = (Hash01(c.x + a, c.z + side, 11) - 0.5f) * 2f;
                    var p = c + normal * (h - inset) + along * (a + j) + Vector3.up * G;
                    if (lite && Hash01(p.x, p.z, 13) < 0.5f) continue;
                    // (OnRoad's kerb margin would take the tree pits: they stand by construction 0.9 m inside the kerb)
                    if (NearRoundabout(p, RingOuter + 6f) || NearSpot(p) || !ClearOfTrees(new Vector2(p.x, p.z), 6f)) continue;
                    float s = Mathf.Lerp(range.x, range.y, Hash01(p.x, p.z, 17));
                    if (Plant(p, kind, s, 1.3f, cv, ct)) n++;
                }
            }
            return n;
        }

        /// <summary>Trees over a lot's open ground, on a jittered grid; how thick depends on what the lot is.</summary>
        int LotTrees(Vector3 lc, Zone zone, bool lite, List<Vector3> cv, List<int> ct)
        {
            (Arch.FloraKind, float)[] mix;
            float spacing;
            switch (zone)
            {
                case Zone.Kampung: mix = KampungYard; spacing = 7.5f; break;
                case Zone.Park: mix = ParkTrees; spacing = 8.5f; break;
                case Zone.Surau: mix = SurauGarden; spacing = 9f; break;
                case Zone.Condo: mix = FlatsGarden; spacing = 10f; break;
                case Zone.Office: mix = OfficePlaza; spacing = 11f; break;
                case Zone.Shops: mix = BackCourt; spacing = 12f; break;
                default: return 0;               // Pak Mat's yard, the mamak, the pasar, the dealer: as they are
            }
            if (lite) spacing *= 1.4f;
            int n = 0;
            const float reach = 17f;             // inside the pavements, clear of the back lane and of the walk loop (lc +-19)
            for (float x = -reach; x <= reach; x += spacing)
                for (float z = -reach; z <= reach; z += spacing)
                {
                    float jx = (Hash01(lc.x + x, lc.z + z, 23) - 0.5f) * spacing * 0.8f;
                    float jz = (Hash01(lc.x + x, lc.z + z, 29) - 0.5f) * spacing * 0.8f;
                    var p = lc + new Vector3(Mathf.Clamp(x + jx, -reach, reach), G, Mathf.Clamp(z + jz, -reach, reach));
                    if (NearRoundabout(p, RingOuter + 5f) || NearSpot(p)) continue;
                    var kind = PickKind(Hash01(p.x, p.z, 31), mix);
                    bool low = kind == Arch.FloraKind.Banana || kind == Arch.FloraKind.Shrub;
                    if (!ClearOfTrees(new Vector2(p.x, p.z), low ? 2.5f : spacing * 0.55f)) continue;
                    var range = ScaleRange(kind);
                    float s = Mathf.Lerp(range.x, range.y, Hash01(p.x, p.z, 37));
                    if (zone == Zone.Kampung && kind == Arch.FloraKind.Angsana) s *= 0.8f;   // the fruit trees: rambutan, mango
                    if (Plant(p, kind, s, low ? 0.8f : 1.6f, cv, ct)) n++;
                }
            return n;
        }
    }
}
