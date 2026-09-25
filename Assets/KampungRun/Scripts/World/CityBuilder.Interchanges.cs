using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// KL's flyovers and roundabouts in the filler districts (the real-KL patches bring their own from
    /// OpenStreetMap): avenues that climb on embankment ramps and cross the side streets on a viaduct, and
    /// bulatan with the black-and-white striped kerb round a planted island, driven clockwise.
    /// </summary>
    public partial class CityBuilder
    {
        /// <summary>Flyover corridors: the road on grid line `line` (a north-south road if ns) climbs out of
        /// junction `from`, stays up over the junctions between, and comes down into junction `to`.</summary>
        static readonly (bool ns, int line, int from, int to)[] Flyovers =
        {
            (false, 2, 11, 15),     // Sungai Besi side: over three streets
            (true, 3, 15, 18),      // Jalan Kuching, past Chow Kit
            (false, 7, 14, 18),     // Jalan Tun Razak side
            (false, 15, 14, 16),    // Jalan Ampang
        };

        /// <summary>Roundabouts, at these grid junctions.</summary>
        static readonly (int i, int k)[] Roundabouts = { (2, 15), (10, 1), (16, 6), (12, 18) };

        const float FlyH = 6.8f;                 // deck surface over the cross streets
        const float FlyHalf = 9.6f;              // half width of the flyover, parapets included
        const float DeckDepth = 1.4f;
        const float RingOuter = 25f, RingIsland = 10f, RingLane = 17.5f;

        /// <summary>Does a flyover along ns carry the road over junction (i, k)?</summary>
        public static bool UnderFlyover(int i, int k, bool ns)
        {
            foreach (var f in Flyovers)
            {
                int line = ns ? i : k, at = ns ? k : i;
                if (f.ns == ns && f.line == line && at > f.from && at < f.to) return true;
            }
            return false;
        }

        /// <summary>Is the grid segment (ns, i, k) a flyover ramp or viaduct (not a ground road)?</summary>
        public static bool OnFlyover(bool ns, int i, int k)
        {
            foreach (var f in Flyovers)
            {
                int line = ns ? i : k, at = ns ? k : i;
                if (f.ns == ns && f.line == line && at >= f.from && at < f.to) return true;
            }
            return false;
        }

        public static bool IsRoundabout(int i, int k)
        {
            foreach (var r in Roundabouts) if (r.i == i && r.k == k) return true;
            return false;
        }

        /// <summary>Within r of a roundabout's centre (kerbs and corner lots make way for the ring).</summary>
        static bool NearRoundabout(Vector3 p, float r)
        {
            foreach (var (i, k) in Roundabouts)
            {
                float dx = p.x - RoadX(i), dz = p.z - RoadZ(k);
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        /// <summary>The lot of block (col, row) that the ring of a roundabout cuts into, if any (lot order NW, NE, SW, SE).</summary>
        static bool RoundaboutLot(int col, int row, int lot)
        {
            // the lot's outer corner is the junction it touches
            int i = col + (LotOffsets[lot].x > 0 ? 1 : 0), k = row + (LotOffsets[lot].y > 0 ? 1 : 0);
            return IsRoundabout(i, k);
        }

        // ------------------------------------------------------------------ geometry helper
        /// <summary>Static custom meshes (ramps, decks, the roundabout disc) as one mesh per colour,
        /// optionally with a mesh collider for the lot.</summary>
        class Geo
        {
            class Part { public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public readonly List<int> t = new List<int>(); }
            readonly Dictionary<Color, Part> _parts = new Dictionary<Color, Part>();

            Part P(Color c)
            {
                if (!_parts.TryGetValue(c, out var p)) _parts[c] = p = new Part();
                return p;
            }

            /// <summary>A flat quad seen from the side `facing` points to.</summary>
            public void Quad(Color c, Vector3 a, Vector3 b, Vector3 d, Vector3 e, Vector3 facing)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, d - a), facing) < 0) { var t = b; b = e; e = t; }
                var n = Vector3.Cross(b - a, d - a).normalized;
                if (n.sqrMagnitude < 0.5f) n = facing.normalized;
                var p = P(c);
                int k = p.v.Count;
                p.v.Add(a); p.v.Add(b); p.v.Add(d); p.v.Add(e);
                for (int i = 0; i < 4; i++) p.n.Add(n);
                p.t.Add(k); p.t.Add(k + 1); p.t.Add(k + 2);
                p.t.Add(k); p.t.Add(k + 2); p.t.Add(k + 3);
            }

            public void Tri(Color c, Vector3 a, Vector3 b, Vector3 d, Vector3 facing)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, d - a), facing) < 0) { var t = b; b = d; d = t; }
                var n = Vector3.Cross(b - a, d - a).normalized;
                var p = P(c);
                int k = p.v.Count;
                p.v.Add(a); p.v.Add(b); p.v.Add(d);
                for (int i = 0; i < 3; i++) p.n.Add(n);
                p.t.Add(k); p.t.Add(k + 1); p.t.Add(k + 2);
            }

            /// <summary>A box along a to b (both at their own heights), `width` across, `height` up from the line.</summary>
            public void Beam(Color c, Vector3 a, Vector3 b, float width, float height, float below = 0f)
            {
                var d = b - a; d.y = 0;
                var side = Vector3.Cross(Vector3.up, d.normalized) * (width * 0.5f);
                var up = Vector3.up * height;
                var dn = Vector3.down * below;
                Quad(c, a - side + up, b - side + up, b + side + up, a + side + up, Vector3.up);
                Quad(c, a - side + dn, b - side + dn, b - side + up, a - side + up, -side);
                Quad(c, a + side + dn, b + side + dn, b + side + up, a + side + up, side);
            }

            public GameObject Build(Transform parent, string name, bool collider, bool shadows)
            {
                var root = new GameObject(name);
                root.transform.SetParent(parent, false);
                root.isStatic = true;
                var solids = new List<CombineInstance>();
                foreach (var kv in _parts)
                {
                    var m = new Mesh { name = name + "_" + ColorUtility.ToHtmlStringRGB(kv.Key), indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    m.SetVertices(kv.Value.v);
                    m.SetNormals(kv.Value.n);
                    m.SetTriangles(kv.Value.t, 0);
                    m.RecalculateBounds();
                    var go = new GameObject(name + "_part");
                    go.transform.SetParent(root.transform, false);
                    go.isStatic = true;
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = LatMaterials.Get(kv.Key, 0f, -1);
                    mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    solids.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
                }
                if (collider && solids.Count > 0)
                {
                    var cm = new Mesh { name = name + "_collider", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    cm.CombineMeshes(solids.ToArray(), true, true, false);
                    root.AddComponent<MeshCollider>().sharedMesh = cm;
                }
                return root;
            }
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // ------------------------------------------------------------------ flyovers
        void BuildFlyovers()
        {
            var road = LatMaterials.Pal.Road;
            var wall = LatMaterials.Pal.Wall;
            var rail = LatMaterials.Pal.Rail;
            var pier = new Color(0.78f, 0.76f, 0.72f);
            var paint = new Color(0.97f, 0.97f, 0.94f);
            foreach (var f in Flyovers)
            {
                var solid = new Geo();
                var marks = new Geo();
                float J(int j) => f.ns ? RoadZ(j) : RoadX(j);
                float lineAt = f.ns ? RoadX(f.line) : RoadZ(f.line);
                // corridor frame: s along the road, t across it (+t = east of a north-south road, north of an east-west one)
                Vector3 W(float s, float t, float y) => f.ns ? new Vector3(lineAt + t, y, s) : new Vector3(s, y, lineAt + t);
                Vector3 across = f.ns ? Vector3.right : Vector3.forward;
                Vector3 along = f.ns ? Vector3.forward : Vector3.right;
                float up0 = J(f.from) + Road * 0.5f, up1 = J(f.from + 1) - Road * 0.5f;
                float dn0 = J(f.to - 1) + Road * 0.5f, dn1 = J(f.to) - Road * 0.5f;
                float H(float s) => s <= up0 || s >= dn1 ? 0f : s < up1 ? FlyH * Smooth((s - up0) / (up1 - up0))
                                  : s <= dn0 ? FlyH : FlyH * Smooth((dn1 - s) / (dn1 - dn0));

                // stations along the corridor: close together on the ramps, so the vertical curve is smooth
                var ss = new List<float>();
                for (float s = up0; s < up1; s += 4f) ss.Add(s);
                for (float s = up1; s < dn0; s += 12f) ss.Add(s);
                for (float s = dn0; s < dn1; s += 4f) ss.Add(s);
                ss.Add(dn1);

                for (int n = 0; n < ss.Count - 1; n++)
                {
                    float s0 = ss[n], s1 = ss[n + 1], h0 = H(s0), h1 = H(s1);
                    // carriageway
                    solid.Quad(road, W(s0, -FlyHalf, h0), W(s1, -FlyHalf, h1), W(s1, FlyHalf, h1), W(s0, FlyHalf, h0), Vector3.up);
                    foreach (float sd in new[] { -1f, 1f })
                    {
                        // parapet
                        solid.Beam(rail, W(s0, sd * (FlyHalf - 0.2f), h0), W(s1, sd * (FlyHalf - 0.2f), h1), 0.4f, 1.0f);
                        bool raised = s0 >= up1 - 0.01f && s1 <= dn0 + 0.01f;
                        if (!raised)
                            // embankment retaining wall down to the ground
                            solid.Quad(wall, W(s0, sd * FlyHalf, -0.3f), W(s1, sd * FlyHalf, -0.3f), W(s1, sd * FlyHalf, h1), W(s0, sd * FlyHalf, h0), across * sd);
                        else
                            // deck fascia
                            solid.Quad(wall, W(s0, sd * FlyHalf, h0 - DeckDepth), W(s1, sd * FlyHalf, h1 - DeckDepth), W(s1, sd * FlyHalf, h1), W(s0, sd * FlyHalf, h0), across * sd);
                        // edge line
                        marks.Quad(paint, W(s0, sd * (FlyHalf - 1.2f), h0 + 0.02f), W(s1, sd * (FlyHalf - 1.2f), h1 + 0.02f),
                                   W(s1, sd * (FlyHalf - 1.4f), h1 + 0.02f), W(s0, sd * (FlyHalf - 1.4f), h0 + 0.02f), Vector3.up);
                    }
                    if (s0 >= up1 - 0.01f && s1 <= dn0 + 0.01f)
                        solid.Quad(wall, W(s0, -FlyHalf, h0 - DeckDepth), W(s1, -FlyHalf, h1 - DeckDepth), W(s1, FlyHalf, h1 - DeckDepth), W(s0, FlyHalf, h0 - DeckDepth), Vector3.down);
                }
                // solid ground under the embankments (nothing that ends up inside one falls out of the world)
                RoadColliders(W(up0, 0, 0), W(up1, 0, 0), !f.ns);
                RoadColliders(W(dn0, 0, 0), W(dn1, 0, 0), !f.ns);
                // abutments where the embankments meet the first and last span
                solid.Quad(wall, W(up1, -FlyHalf, -0.3f), W(up1, FlyHalf, -0.3f), W(up1, FlyHalf, FlyH - DeckDepth), W(up1, -FlyHalf, FlyH - DeckDepth), along);
                solid.Quad(wall, W(dn0, -FlyHalf, -0.3f), W(dn0, FlyHalf, -0.3f), W(dn0, FlyHalf, FlyH - DeckDepth), W(dn0, -FlyHalf, FlyH - DeckDepth), -along);
                // centre dashes
                for (float s = up0 + 4f; s < dn1 - 4f; s += 7.5f)
                {
                    float a = s, b = s + 3f;
                    marks.Quad(paint, W(a, -0.12f, H(a) + 0.02f), W(b, -0.12f, H(b) + 0.02f), W(b, 0.12f, H(b) + 0.02f), W(a, 0.12f, H(a) + 0.02f), Vector3.up);
                }

                // viaduct spans between raised junctions: piers in the middle of the old road, open ground below
                for (int j = f.from + 1; j < f.to - 1; j++)
                {
                    float a = J(j) + Road * 0.5f, b = J(j + 1) - Road * 0.5f;
                    int piers = Mathf.Max(2, Mathf.RoundToInt((b - a) / 24f));
                    for (int q = 0; q < piers; q++)
                    {
                        float s = Mathf.Lerp(a + 6f, b - 6f, piers == 1 ? 0.5f : q / (float)(piers - 1));
                        var foot = W(s, 0f, 0f);
                        _ground.Box(foot + Vector3.up * (FlyH - DeckDepth) * 0.5f, f.ns ? new Vector3(2.4f, FlyH - DeckDepth, 2.2f) : new Vector3(2.2f, FlyH - DeckDepth, 2.4f), pier, true, 1.4f);
                        _ground.Box(foot + Vector3.up * (FlyH - DeckDepth - 0.6f), f.ns ? new Vector3(FlyHalf * 1.6f, 1.2f, 2.6f) : new Vector3(2.6f, 1.2f, FlyHalf * 1.6f), pier, false, 1.4f);
                    }
                    // the space under the flyover: sealed ground (the kind of place a pasar malam sets up)
                    var mid = W((a + b) * 0.5f, 0f, 0f);
                    _ground.Box(mid + Vector3.down * 0.05f, f.ns ? new Vector3(Road, 0.1f, b - a) : new Vector3(b - a, 0.1f, Road), LatMaterials.Pal.Road, false, 0f);
                    RoadColliders(W(a, 0, 0), W(b, 0, 0), !f.ns);
                    if (_rng.NextDouble() < 0.7)
                    {
                        float s = R(a + 10f, b - 10f);
                        Prop("Prop_SatayCart", W(s, R(-6f, 6f), 0f), R(0, 360), false);
                        Prop("env_market_umbrella", W(s + 3f, R(-6f, 6f), 0f), 0, false);
                    }
                }
                // lamps along the raised deck
                for (float s = up1 + 6f; s < dn0; s += 30f)
                    foreach (float sd in new[] { -1f, 1f })
                    {
                        var lamp = Prop("env_lamp_post", W(s, sd * (FlyHalf - 0.25f), FlyH + 1.0f), (f.ns ? 0f : 90f) + (sd > 0 ? 180f : 0f), false);
                        var cap = lamp.AddComponent<CapsuleCollider>();
                        cap.center = new Vector3(0, 3.5f, 0); cap.radius = 0.18f; cap.height = 7;
                    }

                solid.Build(_city.root, $"Flyover_{(f.ns ? "NS" : "EW")}{f.line}_{f.from}", true, true);
                marks.Build(_city.root, $"FlyoverMarks_{(f.ns ? "NS" : "EW")}{f.line}_{f.from}", false, false);

                // the road graph: up the ramp, over the cross streets, down the far ramp
                var chain = new List<RoadNetwork.Node> { f.ns ? _gridNodes[f.line, f.from] : _gridNodes[f.from, f.line] };
                for (float s = up0 + 3f; s < dn1 - 2f; s += up1 - 0.5f < s && s < dn0 ? 16f : 10f)
                    chain.Add(_city.roads.Add(W(s, 0f, H(s)), false));
                chain.Add(f.ns ? _gridNodes[f.line, f.to] : _gridNodes[f.to, f.line]);
                for (int n = 0; n < chain.Count - 1; n++)
                    if (chain[n] != null && chain[n + 1] != null) RoadNetwork.Link(chain[n], chain[n + 1]);
                Place($"Flyover{_flyoverCount++}", W((up1 + dn0) * 0.5f, 0f, FlyH + 0.3f));
            }
        }

        int _flyoverCount;

        // ------------------------------------------------------------------ roundabouts
        void BuildRoundabouts()
        {
            var road = LatMaterials.Pal.Road;
            var paint = new Color(0.97f, 0.97f, 0.94f);
            var grass = LatMaterials.Pal.Park;
            var black = new Color(0.12f, 0.12f, 0.13f);
            var white = new Color(0.96f, 0.96f, 0.94f);
            const float top = 0.22f, isle = 0.55f;
            const int seg = 48;
            foreach (var (i, k) in Roundabouts)
            {
                var c = new Vector3(RoadX(i), 0, RoadZ(k));
                var g = new Geo();
                Vector3 Ring(float r, float a, float y) => c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                for (int n = 0; n < seg; n++)
                {
                    float a0 = n * Mathf.PI * 2f / seg, a1 = (n + 1) * Mathf.PI * 2f / seg;
                    // the carriageway: a raised disc (a gentle lip where the arms run onto it)
                    g.Quad(road, Ring(RingIsland, a0, top), Ring(RingOuter, a0, top), Ring(RingOuter, a1, top), Ring(RingIsland, a1, top), Vector3.up);
                    g.Quad(road, Ring(RingOuter, a0, top), Ring(RingOuter + 2.5f, a0, -0.02f), Ring(RingOuter + 2.5f, a1, -0.02f), Ring(RingOuter, a1, top), Vector3.up);
                    // the island: grass on top, the striped kerb round it
                    g.Tri(grass, c + Vector3.up * isle, Ring(RingIsland, a0, isle), Ring(RingIsland, a1, isle), Vector3.up);
                    var outward = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0, Mathf.Sin((a0 + a1) * 0.5f));
                    g.Quad(n % 2 == 0 ? black : white, Ring(RingIsland, a0, top - 0.05f), Ring(RingIsland, a1, top - 0.05f), Ring(RingIsland, a1, isle), Ring(RingIsland, a0, isle), outward);
                }
                var marks = new Geo();
                for (int n = 0; n < 28; n++)
                {
                    float a0 = n * Mathf.PI * 2f / 28f, a1 = a0 + Mathf.PI * 2f / 28f * 0.45f;
                    marks.Quad(paint, Ring(RingLane - 0.12f, a0, top + 0.015f), Ring(RingLane + 0.12f, a0, top + 0.015f), Ring(RingLane + 0.12f, a1, top + 0.015f), Ring(RingLane - 0.12f, a1, top + 0.015f), Vector3.up);
                }
                g.Build(_city.root, $"Bulatan_{i}_{k}", true, false);
                marks.Build(_city.root, $"BulatanMarks_{i}_{k}", false, false);
                // the island: a fountain, palms, and the flag
                KitProp("env_pbg_fountain", c + Vector3.up * isle, 0, 1.4f);
                for (int n = 0; n < 6; n++)
                {
                    float a = n * Mathf.PI / 3f + 0.3f;
                    Tree("Prop_Palm", c + new Vector3(Mathf.Cos(a) * 6.8f, isle, Mathf.Sin(a) * 6.8f), R(0.9f, 1.15f));
                }
                Prop("Prop_Flagpole", c + new Vector3(0f, isle, -8.4f), 0, true);
                Place($"Bulatan{i}_{k}", c + new Vector3(RingLane, top, 0));

                // traffic goes round clockwise (Malaysia drives on the left); the arms join the ring where they meet it
                var hub = _gridNodes[i, k];
                if (hub == null) continue;
                var ring = new RoadNetwork.Node[8];
                for (int n = 0; n < 8; n++) ring[n] = _city.roads.Add(Ring(RingLane, n * Mathf.PI / 4f, top), true);
                for (int n = 0; n < 8; n++) RoadNetwork.LinkOneWay(ring[n], ring[(n + 7) % 8]);
                foreach (var arm in new List<RoadNetwork.Node>(hub.links))
                {
                    var d = arm.pos - hub.pos;
                    int at = ((Mathf.RoundToInt(Mathf.Atan2(d.z, d.x) / (Mathf.PI / 4f)) % 8) + 8) % 8;
                    RoadNetwork.Unlink(hub, arm);
                    RoadNetwork.Link(ring[at], arm);
                }
            }
        }

        /// <summary>A corner lot a roundabout's ring cuts into: a garden of palms and flowers instead of buildings.</summary>
        void RoundaboutGarden(Vector3 lc, int i, int k)
        {
            var hub = new Vector3(RoadX(i), 0, RoadZ(k));
            _ground.Box(lc + new Vector3(0, G + 0.01f, 0), new Vector3(Lot - 2f, 0.02f, Lot - 2f), LatMaterials.Pal.Park, false, 0f);
            var used = new List<Vector2>();
            for (int n = 0; n < 7; n++)
            {
                var p = lc + new Vector3(R(-17f, 17f), G, R(-17f, 17f));
                if (Vector3.Distance(new Vector3(p.x, 0, p.z), hub) < RingOuter + 5f) continue;
                Tree(n % 3 == 0 ? "Prop_RainTree" : "Prop_Palm", p, R(0.85f, 1.15f));
            }
            var bed = lc + (lc - hub).normalized * 6f;
            KitProp("env_pbg_flowerbed", new Vector3(bed.x, G, bed.z), R(0, 360));
            var wz = WalkZone.FromRect(new Rect(lc.x - 20.5f, lc.z - 20.5f, 41f, 41f));
            _city.walkZones.Add(wz);
        }
    }
}
