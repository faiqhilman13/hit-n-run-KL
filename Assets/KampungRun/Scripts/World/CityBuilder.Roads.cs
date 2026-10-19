using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KampungRun
{
    /// <summary>
    /// The grid roads' surface, painted the way KL paints them, and the junctions' signals and crossings:
    ///  - asphalt with worn and patched stretches, tar seams, cracks, IWK manholes and TNB covers, drain grates in
    ///    the gutter and the monsoon drain's slab covers along the pavement;
    ///  - white broken centre lines that go double and solid near a junction, dashed lane lines, double yellow
    ///    no-parking lines along the kerbs, yellow zig-zags each side of a zebra, stop lines, turn arrows for the
    ///    lanes, yellow box junctions at the busy signalled crossroads, tactile paving where people cross;
    ///  - signal poles at every approach of a signalled junction (a mast arm over the road and a head on the pole,
    ///    green men for the crossings), whose lamps are lit by TrafficSignals' clock in the SignalLamp shader;
    ///  - the zebra crossings registered with the pavement loops either side, so people can cross.
    /// It's all flat palette-coloured quads merged into a mesh or two per 232 m square (one draw call each, small
    /// details on the Detail layer so they drop out with distance), plus the lamps in one mesh per square. No dice
    /// are drawn from the city's generator: every choice is hashed from where it is.
    /// </summary>
    public partial class CityBuilder
    {
        // the street kit's own cells, so the grid roads keep their colour
        static readonly Color Asphalt = new Color(0.25f, 0.26f, 0.30f);
        static readonly Color PaintWhite = new Color(0.97f, 0.97f, 0.95f), PaintYellow = new Color(0.98f, 0.80f, 0.10f);
        static readonly Color TarDark = new Color(0.205f, 0.215f, 0.25f), TarLight = new Color(0.30f, 0.31f, 0.345f), Seam = new Color(0.14f, 0.145f, 0.16f);
        static readonly Color Iron = new Color(0.2f, 0.2f, 0.22f), IronRim = new Color(0.36f, 0.36f, 0.38f), GrateDark = new Color(0.07f, 0.07f, 0.08f);
        static readonly Color DrainSlab = new Color(0.66f, 0.66f, 0.63f), Tactile = new Color(0.96f, 0.78f, 0.18f);
        static readonly Color PoleGrey = new Color(0.34f, 0.36f, 0.4f), HeadBlack = new Color(0.09f, 0.09f, 0.1f), BoardWhite = new Color(0.93f, 0.93f, 0.9f);

        const float XingNear = 11.5f, XingFar = 15.5f, StopLine = 17f;
        const float Ypatch = 0.004f, Yseam = 0.007f, Ypaint = 0.012f, Ycover = 0.016f;

        // what BuildRoads left for us to paint (the street kit used to draw these), and BuildStreetLife's signals
        readonly List<(bool ns, int i, int k)> _citySegs = new List<(bool, int, int)>();
        readonly Dictionary<(int, int), (bool n, bool e, bool s, bool w)> _cityBoxes = new Dictionary<(int, int), (bool, bool, bool, bool)>();
        readonly HashSet<(int, int)> _signalled = new HashSet<(int, int)>();
        readonly List<Vector3> _lampSpots = new List<Vector3>();     // the lots' breakable lamp posts (StreetLamps)
        int _lampsAdded;
        readonly System.Diagnostics.Stopwatch _swFurn = new System.Diagnostics.Stopwatch(), _swLamp = new System.Diagnostics.Stopwatch();
        Paint _paint;

        // ------------------------------------------------------------------ the decal mesh builder
        /// <summary>Flat palette-coloured quads and triangles, one mesh per 232 m square (and one more for small
        /// details, on the Detail layer), plus the signal lamps (vertex-coded for the SignalLamp shader).</summary>
        class Paint
        {
            class Part
            {
                public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
                public readonly List<Vector2> uv = new List<Vector2>();
                public readonly List<Color> c = new List<Color>();
                public readonly List<int> t = new List<int>();
            }
            readonly Dictionary<(int, int, int), Part> _parts = new Dictionary<(int, int, int), Part>();
            public int quads;
            // (ColorPalette keys on Color32, which hashes slowly; a quarter of a million lookups adds up)
            readonly Dictionary<(Color, int), Vector2> _uv = new Dictionary<(Color, int), Vector2>();

            Vector2 Uv(Color c, int surf)
            {
                if (!_uv.TryGetValue((c, surf), out var uv)) _uv[(c, surf)] = uv = ColorPalette.UV(c, surf);
                return uv;
            }

            Part P(Vector3 at, int kind)
            {
                int cx = Mathf.FloorToInt((at.x - X0) / MergeChunk), cz = Mathf.FloorToInt((at.z - Z0) / MergeChunk);
                if (!_parts.TryGetValue((cx, cz, kind), out var p)) _parts[(cx, cz, kind)] = p = new Part();
                return p;
            }

            /// <summary>kind 0 = road paint, 1 = small details, 2 = signal lamps (lamp: the vertex colour code).</summary>
            public void Poly(Color col, int surf, int kind, Vector3 facing, params Vector3[] pts)
            {
                if (pts.Length < 3) return;
                var nrm = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]);
                bool flip = Vector3.Dot(nrm, facing) < 0f;
                var p = P(pts[0], kind);
                int b = p.v.Count;
                var uv = kind == 2 ? Vector2.zero : Uv(col, surf);
                var n = facing.normalized;
                for (int i = 0; i < pts.Length; i++) { p.v.Add(pts[i]); p.n.Add(n); p.uv.Add(uv); p.c.Add(kind == 2 ? col : Color.white); }
                for (int i = 1; i < pts.Length - 1; i++)
                {
                    p.t.Add(b);
                    if (flip) { p.t.Add(b + i + 1); p.t.Add(b + i); }
                    else { p.t.Add(b + i); p.t.Add(b + i + 1); }
                }
                quads++;
            }

            /// <summary>A band lying on the ground from a to b (plan), `w` wide, at height y.</summary>
            public void Band(Vector3 a, Vector3 b, float w, float y, Color col, int surf = 0, int kind = 0)
            {
                var d = b - a; d.y = 0f;
                if (d.sqrMagnitude < 1e-6f) return;
                var side = Vector3.Cross(Vector3.up, d.normalized) * (w * 0.5f);
                a.y = y; b.y = y;
                Poly(col, surf, kind, Vector3.up, a - side, a + side, b + side, b - side);
            }

            /// <summary>An upright box (no bottom), turned by yaw about its centre.</summary>
            public void Box(Vector3 c, Vector3 size, float yawDeg, Color col, int kind, int surf = 0)
            {
                var q = Quaternion.Euler(0f, yawDeg, 0f);
                Vector3 hx = q * Vector3.right * (size.x * 0.5f), hy = Vector3.up * (size.y * 0.5f), hz = q * Vector3.forward * (size.z * 0.5f);
                Poly(col, surf, kind, Vector3.up, c + hy - hx - hz, c + hy - hx + hz, c + hy + hx + hz, c + hy + hx - hz);
                Poly(col, surf, kind, hx, c + hx - hy - hz, c + hx - hy + hz, c + hx + hy + hz, c + hx + hy - hz);
                Poly(col, surf, kind, -hx, c - hx - hy - hz, c - hx - hy + hz, c - hx + hy + hz, c - hx + hy - hz);
                Poly(col, surf, kind, hz, c + hz - hy - hx, c + hz - hy + hx, c + hz + hy + hx, c + hz + hy - hx);
                Poly(col, surf, kind, -hz, c - hz - hy - hx, c - hz - hy + hx, c - hz + hy + hx, c - hz + hy - hx);
            }

            /// <summary>A thin bar from a to b (any direction): two crossed strips, seen from anywhere.</summary>
            public void Bar(Vector3 a, Vector3 b, float w, Color col, int kind)
            {
                var d = (b - a).normalized;
                var s1 = Vector3.Cross(d, Mathf.Abs(d.y) > 0.9f ? Vector3.right : Vector3.up).normalized * (w * 0.5f);
                var s2 = Vector3.Cross(d, s1).normalized * (w * 0.5f);
                Poly(col, 0, kind, Vector3.Cross(s1, d), a - s1, b - s1, b + s1, a + s1);
                Poly(col, 0, kind, -Vector3.Cross(s1, d), a - s1, a + s1, b + s1, b - s1);
                Poly(col, 0, kind, Vector3.Cross(s2, d), a - s2, b - s2, b + s2, a + s2);
                Poly(col, 0, kind, -Vector3.Cross(s2, d), a - s2, a + s2, b + s2, b - s2);
            }

            /// <summary>A flat disc standing up, facing `facing` (sign faces).</summary>
            public void Plate(Vector3 c, Vector3 facing, float r, int segs, Color col, int kind)
            {
                var right = Vector3.Cross(Vector3.up, facing).normalized;
                var pts = new Vector3[segs];
                for (int i = 0; i < segs; i++)
                {
                    float a = i * Mathf.PI * 2f / segs;
                    pts[i] = c + right * Mathf.Cos(a) * r + Vector3.up * Mathf.Sin(a) * r;
                }
                Poly(col, 0, kind, facing, pts);
            }

            /// <summary>A band from a to b lying in the plane facing `facing` (lettering on signs).</summary>
            public void Band2(Vector3 a, Vector3 b, float w, Vector3 facing, Color col, int kind)
            {
                var d = (b - a).normalized;
                var side = Vector3.Cross(facing, d).normalized * (w * 0.5f);
                Poly(col, 0, kind, facing, a - side, a + side, b + side, b - side);
            }

            public void Disc(Vector3 c, float r, int segs, float y, Color col, int surf, int kind)
            {
                var pts = new Vector3[segs];
                for (int i = 0; i < segs; i++)
                {
                    float a = i * Mathf.PI * 2f / segs;
                    pts[i] = new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r);
                }
                Poly(col, surf, kind, Vector3.up, pts);
            }

            public void Build(Transform parent)
            {
                var root = new GameObject("RoadPaint").transform;
                root.SetParent(parent, false);
                Material lamp = null;
                foreach (var kv in _parts)
                {
                    var (cx, cz, kind) = kv.Key;
                    var p = kv.Value;
                    var m = new Mesh { name = $"RoadPaint_{cx}_{cz}_{kind}", indexFormat = p.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                    m.SetVertices(p.v);
                    m.SetNormals(p.n);
                    if (kind == 2) m.SetColors(p.c); else m.SetUVs(0, p.uv);
                    m.SetTriangles(p.t, 0);
                    m.RecalculateBounds();
                    MeshKeep.Upload(m);
                    var go = new GameObject(m.name) { layer = kind == 1 ? Layers.Detail : Layers.World };
                    go.transform.SetParent(root, false);
                    go.isStatic = true;
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var mr = go.AddComponent<MeshRenderer>();
                    if (kind == 2)
                    {
                        if (lamp == null)
                        {
                            var sh = Resources.Load<Shader>("SignalLamp");
                            lamp = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Unlit")) { name = "SignalLamps" };
                        }
                        mr.sharedMaterial = lamp;
                    }
                    else mr.sharedMaterial = ColorPalette.Material;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
                ColorPalette.Apply();
            }
        }

        /// <summary>A road's own frame: s along it from a junction's centre, t across it (+t on the left of +s,
        /// which is the side traffic heading +s keeps to: Malaysia drives on the left).</summary>
        struct Frame
        {
            public Vector3 o, s, t;
            public Frame(Vector3 origin, Vector3 along) { o = origin; o.y = 0f; s = along.normalized; t = new Vector3(-s.z, 0f, s.x); }
            public Vector3 P(float ss, float tt, float y = 0f) => o + s * ss + t * tt + Vector3.up * y;
        }

        static Vector3 ArmDir(int arm) => arm == 0 ? Vector3.forward : arm == 1 ? Vector3.right : arm == 2 ? Vector3.back : Vector3.left;

        static System.Random Dice(float x, float z, int salt) => new System.Random(Mathf.RoundToInt(x * 3.7f) * 73856093 ^ Mathf.RoundToInt(z * 5.3f) * 19349663 ^ salt * 83492791);
        static float RR(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        // ------------------------------------------------------------------ signals (BuildStreetLife's dice)
        /// <summary>A signalled junction: the poles and lamps go up in PaintRoads, with everything else.</summary>
        void Signalise(int i, int k) => _signalled.Add((i, k));

        // ------------------------------------------------------------------ painting it all
        void PaintRoads()
        {
            TrafficSignals.Clear();
            _paint = new Paint();
            _lampsAdded = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            // junction controls first (segments need to know what's at their ends)
            var ends = new Dictionary<(int, int), JunctionControl>();
            foreach (var kv in _cityBoxes)
            {
                var (i, k) = kv.Key;
                var node = _gridNodes[i, k];
                if (node == null || IsRoundabout(i, k)) continue;
                var (n, e, s, w) = kv.Value;
                int arms = (n ? 1 : 0) + (e ? 1 : 0) + (s ? 1 : 0) + (w ? 1 : 0);
                if (arms < 3) continue;
                var pos = new Vector3(RoadX(i), 0f, RoadZ(k));
                float h = Hash01(pos.x, pos.z, 911);
                var j = new JunctionControl
                {
                    pos = pos, signal = _signalled.Contains((i, k)), offset = Mathf.Round(h * 255f) / 255f,
                    stopDist = StopLine, boxRadius = Road * 0.5f,
                };
                node.control = j;
                TrafficSignals.AddGrid(i, k, j);
                ends[(i, k)] = j;
            }
            // and the other junctions drivers give way at: the grid ones off the city roads, the real-KL streets' crossroads
            foreach (var node in _city.roads.nodes)
            {
                if (node.control != null || !node.junction || node.links.Count < 3 || node.pos.y > 0.5f) continue;
                int gi = Mathf.RoundToInt((node.pos.x - X0) / Pitch), gk = Mathf.RoundToInt((node.pos.z - Z0) / Pitch);
                bool gridNode = gi >= 0 && gk >= 0 && gi <= NX && gk <= NZ && _gridNodes[gi, gk] == node;
                if (gridNode && IsRoundabout(gi, gk)) continue;
                if (NearRoundabout(node.pos, RingOuter + 2f)) continue;     // the ring's own nodes
                node.control = new JunctionControl { pos = node.pos, stopDist = 9.5f, boxRadius = gridNode ? 5f : 6f };
                TrafficSignals.Junctions.Add(node.control);
            }

            var laps = new System.Text.StringBuilder();
            void Lap(string what) { laps.Append($" {what} {clock.ElapsedMilliseconds}"); clock.Restart(); }
            Lap("controls");
            foreach (var (ns, i, k) in _citySegs) PaintSegment(ns, i, k, ends);
            Lap("streets");
            foreach (var kv in _cityBoxes) PaintBox(kv.Key.Item1, kv.Key.Item2, kv.Value, ends);
            foreach (var kv in ends) if (kv.Value.signal) SignalPoles(kv.Key.Item1, kv.Key.Item2, kv.Value);
            Lap("boxes");
            PaintPatches();
            Lap("patches");
            int linked = LinkCrossings();
            Lap("crossings");

            _paint.Build(_city.root);
            Lap("meshes");
            var clockGo = new GameObject("TrafficSignals");
            clockGo.transform.SetParent(_city.root, false);
            clockGo.AddComponent<TrafficSignals.SignalClock>();
            Debug.Log($"[Roads] painted {_citySegs.Count} streets, {_cityBoxes.Count} junction boxes: {_paint.quads} quads; " +
                      $"{_signalled.Count} signalled junctions, {TrafficSignals.Crossings.Count} crossings ({linked} joining two pavements), " +
                      $"{TrafficSignals.Junctions.Count} junctions to give way at; {_lampsAdded} lamp posts, {_city.parkingBays.Count} parking bays, " +
                      $"{_city.motoBays.Count} motorcycle bays; ms:{laps} (furniture {_swFurn.ElapsedMilliseconds}, of which lamp props {_swLamp.ElapsedMilliseconds})");
        }

        /// <summary>Is this end of a city segment a crossroads with a zebra, stop line and arrows?</summary>
        bool CrossingEnd(int i, int k, Dictionary<(int, int), JunctionControl> ends) => ends.ContainsKey((i, k));

        /// <summary>One block-long stretch of city road between two junctions.</summary>
        void PaintSegment(bool ns, int i, int k, Dictionary<(int, int), JunctionControl> ends)
        {
            var a = new Vector3(RoadX(i), 0f, RoadZ(k));
            var b = ns ? new Vector3(RoadX(i), 0f, RoadZ(k + 1)) : new Vector3(RoadX(i + 1), 0f, RoadZ(k));
            var f = new Frame(a, b - a);
            float L = Pitch;
            var rnd = Dice(a.x + (ns ? 0.5f : 0f), a.z + (ns ? 0f : 0.5f), 5);
            var pt = _paint;

            // the junction (and its kind) at each end; arm index of this road at that junction
            (int, int) ja = (i, k), jb = ns ? (i, k + 1) : (i + 1, k);
            int armAtA = ns ? 0 : 1, armAtB = ns ? 2 : 3;
            bool xa = CrossingEnd(ja.Item1, ja.Item2, ends), xb = CrossingEnd(jb.Item1, jb.Item2, ends);
            bool ra = IsRoundabout(ja.Item1, ja.Item2), rb = IsRoundabout(jb.Item1, jb.Item2);
            float s0 = Road * 0.5f, s1 = L - Road * 0.5f;

            // asphalt
            pt.Poly(Asphalt, SurfaceKinds.Asphalt, 0, Vector3.up, f.P(s0, -10f), f.P(s0, 10f), f.P(s1, 10f), f.P(s1, -10f));

            // wear: patched and resurfaced stretches, a trench across, seams, cracks near the kerbs
            float yy = Ypatch;
            int patches = rnd.Next(2, 6);
            for (int n = 0; n < patches; n++)
            {
                float ps = RR(rnd, s0 + 2f, s1 - 6f), pl = RR(rnd, 2f, 9f), pw = RR(rnd, 1.2f, 4.8f), ptc = RR(rnd, -9.5f + pw * 0.5f, 9.5f - pw * 0.5f);
                bool lane = rnd.NextDouble() < 0.3;
                if (lane) { pw = 4.6f; ptc = rnd.NextDouble() < 0.5 ? -7.5f : 7.5f; pl = RR(rnd, 10f, 30f); }
                var col = rnd.NextDouble() < 0.55 ? TarDark : TarLight;
                pt.Poly(col, SurfaceKinds.Asphalt, 0, Vector3.up, f.P(ps, ptc - pw * 0.5f, yy), f.P(ps, ptc + pw * 0.5f, yy),
                        f.P(Mathf.Min(ps + pl, s1), ptc + pw * 0.5f, yy), f.P(Mathf.Min(ps + pl, s1), ptc - pw * 0.5f, yy));
                yy += 0.0004f;
            }
            if (rnd.NextDouble() < 0.4)
            {
                float ts = RR(rnd, s0 + 20f, s1 - 20f);
                pt.Band(f.P(ts, -10f), f.P(ts, 10f), RR(rnd, 0.6f, 1.1f), yy, TarDark, SurfaceKinds.Asphalt);
            }
            if (rnd.NextDouble() < 0.35)
                foreach (float st in new[] { -5.3f, 5.3f })
                    pt.Band(f.P(s0, st), f.P(s1, st), 0.1f, Yseam, Seam, 0, 1);
            int cracks = rnd.Next(0, 2);
            for (int n = 0; n < cracks; n++)
            {
                float cs = RR(rnd, s0 + 4f, s1 - 8f), ct = (rnd.NextDouble() < 0.5 ? -1f : 1f) * RR(rnd, 6.5f, 9.2f);
                var p0 = f.P(cs, ct);
                int bits = rnd.Next(3, 7);
                for (int m = 0; m < bits; m++)
                {
                    var p1 = p0 + f.s * RR(rnd, 0.5f, 1.4f) + f.t * RR(rnd, -0.5f, 0.5f);
                    if (Mathf.Abs(Vector3.Dot(p1 - f.o, f.t)) > 9.5f) break;
                    pt.Band(p0, p1, 0.08f, Yseam, Seam, 0, 1);
                    p0 = p1;
                }
            }

            // centre line: double solid near a crossroads (and the roundabouts), broken white between
            float ca = xa ? StopLine + 24f : ra ? 45f : s0, cb = xb ? L - StopLine - 24f : rb ? L - 45f : s1;
            float c0 = xa ? XingFar + 0.6f : ra ? 28f : s0, c1 = xb ? L - XingFar - 0.6f : rb ? L - 28f : s1;
            foreach (float ct in new[] { -0.17f, 0.17f })
            {
                if (ca > s0) pt.Band(f.P(c0, ct), f.P(Mathf.Min(ca, s1), ct), 0.12f, Ypaint, PaintWhite);
                if (cb < s1) pt.Band(f.P(Mathf.Max(cb, s0), ct), f.P(c1, ct), 0.12f, Ypaint, PaintWhite);
            }
            Dashes(f, 0f, ca + 2f, cb - 2f, 3.5f, 5.5f, 0.15f, PaintWhite);

            // lane lines: dashed, solid up to the stop line
            foreach (float lt in new[] { -5f, 5f })
            {
                // traffic heading -s (to A) uses t < 0; to B, t > 0
                bool toA = lt < 0f;
                float la = s0, lb = s1;
                if (xa) { if (toA) { pt.Band(f.P(StopLine, lt), f.P(StopLine + 15f, lt), 0.13f, Ypaint, PaintWhite); la = StopLine + 18f; } else la = XingFar + 3f; }
                if (xb) { if (!toA) { pt.Band(f.P(L - StopLine - 15f, lt), f.P(L - StopLine, lt), 0.13f, Ypaint, PaintWhite); lb = L - StopLine - 18f; } else lb = L - XingFar - 3f; }
                if (ra) la = 30f;
                if (rb) lb = L - 30f;
                Dashes(f, lt, la, lb, 3f, 4.5f, 0.13f, PaintWhite);
            }

            // the kerbs: double yellow (or one, here and there), zig-zags by the crossings, grates in the gutter
            bool single = Hash01(a.x, a.z, ns ? 61 : 67) < 0.25f;
            foreach (float side in new[] { -1f, 1f })
            {
                var skips = new List<Vector2>();                 // s-ranges with no kerb lines: a patch street comes in
                foreach (var q in _streetJoins)
                {
                    float qs = Vector3.Dot(q - f.o, f.s), qt = Vector3.Dot(q - f.o, f.t);
                    if (qs > s0 - 6f && qs < s1 + 6f && qt * side > 6f && qt * side < 26f) skips.Add(new Vector2(qs - 8f, qs + 8f));
                }
                float za = xa ? XingFar + 12f : ra ? 28f : s0, zb = xb ? L - XingFar - 12f : rb ? L - 28f : s1;
                // a bus stop on this side: a yellow bay with BAS in it, and no other kerb lines along it
                foreach (var (bpos, _) in _city.busStops)
                {
                    float bs = Vector3.Dot(bpos - f.o, f.s), bt = Vector3.Dot(bpos - f.o, f.t);
                    if (bt * side < 10f || bt * side > 13f) continue;
                    float b0 = Mathf.Max(bs - 8f, za), b1 = Mathf.Min(bs + 8f, zb);
                    if (b1 - b0 < 9f) continue;
                    BusBay(f, b0, b1, side);
                    skips.Add(new Vector2(b0 - 0.3f, b1 + 0.3f));
                }
                // marked parking bays down the kerb of some shopping streets (cars park in them: GameManager)
                if (ParkingSide(f, side) && Hash01(a.x + side * 3f, a.z, ns ? 71 : 73) < 0.8f)
                    ParkingBays(f, side, za + 1f, zb - 1f, skips);
                foreach (float off in single ? new[] { 9.2f } : new[] { 9.05f, 9.3f })
                    Runs(f, side * off, za, zb, skips, 0.1f, PaintYellow);
                // zig-zags from the crossing back up the approach (and on away from it)
                if (xa) ZigZag(f, side * 8.9f, XingFar + 0.5f, XingFar + 12f);
                if (xb) ZigZag(f, side * 8.9f, L - XingFar - 12f, L - XingFar - 0.5f);
                // gully grates in the gutter
                for (float gs = s0 + 6f + RR(rnd, 0f, 4f); gs < s1 - 4f; gs += RR(rnd, 13f, 18f))
                {
                    if (xa && gs < XingFar + 1f || xb && gs > L - XingFar - 1f || InSkip(gs, skips)) continue;
                    Grate(f, gs, side);
                }
                // the monsoon drain's covers down the pavement (only where there is one: a block, not a patch or river)
                if (PavementSide(f, side, ns, i, k))
                {
                    DrainCovers(f, side, xa || ra ? XingFar + 1.2f : s0 + 2f, xb || rb ? L - XingFar - 1.2f : s1 - 2f, skips);
                    _swFurn.Start();
                    Furniture(f, side, xa, xb, ra, rb, skips);
                    _swFurn.Stop();
                }
            }
            OverheadLines(f, a);

            // manholes and covers
            int holes = rnd.Next(1, 4);
            for (int n = 0; n < holes; n++)
            {
                float hs = RR(rnd, StopLine + 8f, L - StopLine - 8f);
                float ht = (rnd.NextDouble() < 0.5 ? -1f : 1f) * (rnd.NextDouble() < 0.5 ? 2.5f : 7.5f) + RR(rnd, -0.6f, 0.6f);
                Manhole(f.P(hs, ht), f, rnd.NextDouble() < 0.7);
            }
            if (rnd.NextDouble() < 0.6)
            {
                float us = RR(rnd, s0 + 10f, s1 - 10f), ut = (rnd.NextDouble() < 0.5 ? -1f : 1f) * 8.3f;
                UtilityCover(f, us, ut);
            }

            // the approaches to each crossroads: zebra, stop line, arrows
            if (xa) Approach(new Frame(a, b - a), ja, armAtA, ends[ja]);
            if (xb) Approach(new Frame(b, a - b), jb, armAtB, ends[jb]);
            if (ra) GiveWay(new Frame(a, b - a), 28.5f);
            if (rb) GiveWay(new Frame(b, a - b), 28.5f);
        }

        /// <summary>Shops, flats and offices on this side of the road (where people park along the kerb).</summary>
        bool ParkingSide(Frame f, float side)
        {
            var probe = f.P(Pitch * 0.5f, side * (Road * 0.5f + 5f));
            int col = Mathf.FloorToInt((probe.x - X0) / Pitch), row = Mathf.FloorToInt((probe.z - Z0) / Pitch);
            if (col < 0 || row < 0 || col >= NX || row >= NZ || IsKampungCell(col, row)) return false;
            var z = ZoneAt(col, row);
            return z == Zone.Shops || z == Zone.ChowKit || z == Zone.Mamak || z == Zone.Pasar || z == Zone.Brickfields || z == Zone.Condo || z == Zone.Office || z == Zone.Dealer;
        }

        /// <summary>A row of parking bays in the kerb lane (traffic keeps to the inner lane), each one a spot a car can be parked in.</summary>
        void ParkingBays(Frame f, float side, float from, float to, List<Vector2> skips)
        {
            const float bay = 5.6f;
            float t0 = side * 7.45f, t1 = side * 9.95f;
            float runStart = -1f, s = from;
            var added = new List<Vector2>();
            for (; s + bay <= to; s += bay)
            {
                bool clear = true;
                foreach (var r in skips) if (s + bay > r.x && s < r.y) { clear = false; break; }
                if (!clear)
                {
                    if (runStart >= 0f) { _paint.Band(f.P(runStart, t0), f.P(s, t0), 0.12f, Ypaint, PaintWhite); added.Add(new Vector2(runStart, s)); runStart = -1f; }
                    continue;
                }
                if (runStart < 0f) { runStart = s; _paint.Band(f.P(s, t0), f.P(s, t1), 0.12f, Ypaint, PaintWhite); }
                _paint.Band(f.P(s + bay, t0), f.P(s + bay, t1), 0.12f, Ypaint, PaintWhite);
                var centre = f.P(s + bay * 0.5f, side * 8.75f);
                if (Hash01(centre.x, centre.z, 831) < 0.14f)
                {
                    // a motorcycle bay: yellow, and a row of kapcai nosed out into the road
                    _paint.Band(f.P(s + 0.3f, side * 8.75f), f.P(s + bay - 0.3f, side * 8.75f), 0.1f, Ypaint + 0.0005f, PaintYellow);
                    // (the bikes themselves come and go with the player, like the parked cars: GameManager)
                    int bikes = 3 + (Hash01(centre.x, centre.z, 833) < 0.5f ? 1 : 0);
                    var nose = (-f.t * side * 0.87f + f.s * side * 0.5f).normalized;
                    _city.motoBays.Add((f.P(s + 0.9f, side * 8.9f), f.P(s + bay - 0.9f, side * 8.9f), Mathf.Atan2(nose.x, nose.z) * Mathf.Rad2Deg, bikes));
                    continue;
                }
                _city.parkingBays.Add((centre, Quaternion.LookRotation(f.s * side)));
            }
            if (runStart >= 0f) { _paint.Band(f.P(runStart, t0), f.P(s, t0), 0.12f, Ypaint, PaintWhite); added.Add(new Vector2(runStart, s)); }
            skips.AddRange(added);
        }

        /// <summary>The bus stop's bay: a yellow box in the kerb lane with BAS painted in it, readable as you drive up.</summary>
        void BusBay(Frame f, float a, float b, float side)
        {
            float t0 = side * 7.3f, t1 = side * 9.9f, bs = (a + b) * 0.5f;
            _paint.Band(f.P(a, t0), f.P(b, t0), 0.15f, Ypaint, PaintYellow);
            _paint.Band(f.P(a, t1), f.P(b, t1), 0.15f, Ypaint, PaintYellow);
            _paint.Band(f.P(a, t0), f.P(a, t1), 0.15f, Ypaint, PaintYellow);
            _paint.Band(f.P(b, t0), f.P(b, t1), 0.15f, Ypaint, PaintYellow);
            // the letters: "up" the way traffic goes, left to right across the lane as the driver sees it
            var up = f.s * side;
            var right = f.t * side;          // (was mirrored: B and S came out backwards, read right to left)
            var origin = f.P(bs, side * 8.6f) - right * 1.35f - up * 0.9f;
            const float lw = 0.7f, lh = 1.8f, sw = 0.14f;
            void Stroke(int letter, float x0, float y0, float x1, float y1)
            {
                var o = origin + right * (letter * 1.0f);
                _paint.Band(o + right * (x0 * lw) + up * (y0 * lh), o + right * (x1 * lw) + up * (y1 * lh), sw, Ypaint, PaintYellow);
            }
            // B
            Stroke(0, 0f, 0f, 0f, 1f); Stroke(0, 0f, 1f, 0.8f, 1f); Stroke(0, 0f, 0.5f, 0.85f, 0.5f); Stroke(0, 0f, 0f, 0.9f, 0f);
            Stroke(0, 0.8f, 0.5f, 0.8f, 1f); Stroke(0, 0.9f, 0f, 0.9f, 0.5f);
            // A
            Stroke(1, 0f, 0f, 0.5f, 1f); Stroke(1, 1f, 0f, 0.5f, 1f); Stroke(1, 0.25f, 0.45f, 0.75f, 0.45f);
            // S
            Stroke(2, 0f, 1f, 1f, 1f); Stroke(2, 0f, 0.5f, 0f, 1f); Stroke(2, 0f, 0.5f, 1f, 0.5f); Stroke(2, 1f, 0f, 1f, 0.5f); Stroke(2, 0f, 0f, 1f, 0f);
        }

        // ------------------------------------------------------------------ street furniture
        static readonly Color RailGreen = new Color(0.14f, 0.42f, 0.27f), BollardYellow = new Color(0.98f, 0.78f, 0.12f), SignBlue = new Color(0.1f, 0.32f, 0.72f);
        static readonly Color SignRed = new Color(0.86f, 0.12f, 0.12f), SignWhite = new Color(0.97f, 0.97f, 0.95f), SignBack = new Color(0.55f, 0.57f, 0.6f);
        static readonly Color CableBlack = new Color(0.08f, 0.08f, 0.09f), Concrete = new Color(0.68f, 0.68f, 0.66f);
        static readonly Color[] BannerColours = { new Color(0.86f, 0.14f, 0.16f), new Color(0.12f, 0.36f, 0.78f), new Color(0.98f, 0.78f, 0.1f), new Color(0.14f, 0.6f, 0.3f), new Color(0.95f, 0.45f, 0.1f) };

        /// <summary>
        /// One pavement's furniture along a city street: guard railings funnelling people to the zebras, bollards at
        /// the corners, the blue crossing sign on the approach, a speed limit or a no-parking sign mid-block.
        /// Small pieces on the Detail layer (gone with distance); posts and rails solid where cars could hit them.
        /// </summary>
        void Furniture(Frame f, float side, bool xa, bool xb, bool ra, bool rb, List<Vector2> skips)
        {
            bool lite = GameManager.WebLite;
            float L = Pitch;
            float tr = side * 10.38f;
            // guard railings each side of the crossings, from the stop line back up the street
            for (int end = 0; end < 2; end++)
            {
                if (end == 0 ? !xa : !xb) continue;
                float r0 = end == 0 ? StopLine : L - StopLine - 12.5f, r1 = r0 + 12.5f;
                if (InSkip((r0 + r1) * 0.5f, skips)) continue;
                for (float s = r0; s <= r1 + 0.01f; s += lite ? 4.1f : 2.08f)
                    _paint.Box(f.P(s, tr, G + 0.55f), new Vector3(0.11f, 1.1f, 0.11f), 0f, RailGreen, 1);
                foreach (float h in new[] { 1.05f, 0.55f })
                    _paint.Bar(f.P(r0, tr, G + h), f.P(r1, tr, G + h), 0.1f, h > 1f ? BollardYellow : RailGreen, 1);
                var mid = f.P((r0 + r1) * 0.5f, tr, G + 0.5f);
                _ground.ColliderOnly(mid, Mathf.Abs(f.s.x) > 0.5f ? new Vector3(12.5f, 1f, 0.12f) : new Vector3(0.12f, 1f, 12.5f));
            }
            // bollards round the corner at each crossing end
            if (!lite)
                for (int end = 0; end < 2; end++)
                {
                    if (end == 0 ? !xa : !xb) continue;
                    foreach (float o in new[] { 0.45f, 1.5f })
                    {
                        // on the corner, along the kerb of the street round it
                        var at = f.P(end == 0 ? 10.55f : L - 10.55f, side * (10.5f + o), G);
                        _paint.Box(at + Vector3.up * 0.45f, new Vector3(0.28f, 0.9f, 0.28f), 45f, BollardYellow, 1);
                        _paint.Box(at + Vector3.up * 0.66f, new Vector3(0.3f, 0.16f, 0.3f), 45f, HeadBlack, 1);
                        _paint.Box(at + Vector3.up * 0.93f, new Vector3(0.22f, 0.07f, 0.22f), 45f, HeadBlack, 1);
                        _ground.ColliderOnly(at + Vector3.up * 0.42f, new Vector3(0.24f, 0.84f, 0.24f));
                    }
                }
            // lamp posts every 30 m or so down the kerb, wherever the lots' own lamps leave a gap
            for (float s = Road * 0.5f + 14f; s < L - Road * 0.5f - 10f && !lite; s += 30f)
            {
                var at = f.P(s, side * 10.9f, G);
                if (InSkip(s, skips) || NearLamp(at, 13f)) continue;
                var toRoad = -f.t * side;
                _swLamp.Start();
                var lamp = Prop("env_lamp_post", at, Mathf.Atan2(toRoad.x, toRoad.z) * Mathf.Rad2Deg, false);
                _swLamp.Stop();
                foreach (var tr2 in lamp.GetComponentsInChildren<Transform>(true)) tr2.gameObject.layer = Layers.Detail;
                var cap = lamp.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0, 3.5f, 0); cap.radius = 0.18f; cap.height = 7;
                _lampSpots.Add(at);
                _lampsAdded++;
            }
            // the traffic on this side of the road heads +s if side > 0 (it keeps left), so its signs face -s
            var face = -f.s * side;
            float ts = side * 10.8f;
            // pedestrian crossing ahead: the blue square sign, on the approach to each zebra this side's traffic meets
            if (side > 0 ? xb : xa)
            {
                float s = side > 0 ? L - XingFar - 20f : XingFar + 20f;
                if (!InSkip(s, skips)) SignPost(f.P(s, ts, G), face, 1);
            }
            // mid-block: a speed limit (the main roads) or no parking (most of the rest)
            float hm = Hash01(f.o.x + side * 7f, f.o.z, 811);
            if (hm < 0.75f)
            {
                float s = L * 0.5f + side * 14f;
                if (!InSkip(s, skips)) SignPost(f.P(s, ts, G), face, hm < 0.35f ? 2 : 3);
            }
        }

        /// <summary>A sign on a post: 1 pedestrian crossing (blue square), 2 speed limit 50, 3 no parking.</summary>
        void SignPost(Vector3 foot, Vector3 face, int kind)
        {
            var right = Vector3.Cross(Vector3.up, face).normalized;
            // a bit bigger than life, Springfield style: you can read it from the car
            const float k = 1.5f;
            _paint.Box(foot + Vector3.up * 1.5f, new Vector3(0.13f, 3f, 0.13f), 0f, SignBack, 1);
            _ground.ColliderOnly(foot + Vector3.up * 1.5f, new Vector3(0.16f, 3f, 0.16f));
            var c = foot + Vector3.up * 2.85f + face * 0.08f;
            var right0 = right;
            right = right0 * k;
            var upk = Vector3.up * k;
            if (kind == 1)
            {
                // blue square, white triangle, a walking figure
                _paint.Poly(SignBack, 0, 1, -face, c - right * 0.36f - upk * 0.36f - face * 0.01f, c + right * 0.36f - upk * 0.36f - face * 0.01f,
                            c + right * 0.36f + upk * 0.36f - face * 0.01f, c - right * 0.36f + upk * 0.36f - face * 0.01f);
                _paint.Poly(SignBlue, 0, 1, face, c - right * 0.35f - upk * 0.35f, c + right * 0.35f - upk * 0.35f, c + right * 0.35f + upk * 0.35f, c - right * 0.35f + upk * 0.35f);
                var w = c + face * 0.005f;
                _paint.Poly(SignWhite, 0, 1, face, w - right * 0.27f - upk * 0.22f, w + right * 0.27f - upk * 0.22f, w + upk * 0.26f);
                var fg = w + face * 0.005f;
                _paint.Poly(HeadBlack, 0, 1, face, fg - right * 0.03f - upk * 0.12f, fg + right * 0.03f - upk * 0.12f, fg + right * 0.03f + upk * 0.03f, fg - right * 0.03f + upk * 0.03f);
                _paint.Plate(fg + upk * 0.08f, face, 0.035f * k, 8, HeadBlack, 1);
                _paint.Poly(HeadBlack, 0, 1, face, fg - upk * 0.1f - right * 0.02f, fg - upk * 0.1f + right * 0.02f, fg - upk * 0.19f - right * 0.07f, fg - upk * 0.19f - right * 0.1f);
                _paint.Poly(HeadBlack, 0, 1, face, fg - upk * 0.1f - right * 0.02f, fg - upk * 0.1f + right * 0.02f, fg - upk * 0.19f + right * 0.09f, fg - upk * 0.19f + right * 0.06f);
                return;
            }
            _paint.Plate(c - face * 0.012f, -face, 0.37f * k, 16, SignBack, 1);
            _paint.Plate(c, face, 0.37f * k, 16, SignRed, 1);
            if (kind == 2)
            {
                _paint.Plate(c + face * 0.005f, face, 0.29f * k, 16, SignWhite, 1);
                // "50"
                var d = c + face * 0.01f;
                void Seg(float x0, float y0, float x1, float y1) => _paint.Band2(d + right * x0 + upk * y0, d + right * x1 + upk * y1, 0.035f * k, face, HeadBlack, 1);
                Seg(-0.17f, 0.12f, -0.04f, 0.12f); Seg(-0.155f, 0.12f, -0.155f, 0.01f); Seg(-0.17f, 0.01f, -0.05f, 0.01f);
                Seg(-0.05f, 0.01f, -0.05f, -0.12f); Seg(-0.17f, -0.12f, -0.04f, -0.12f);
                Seg(0.03f, 0.12f, 0.16f, 0.12f); Seg(0.03f, -0.12f, 0.16f, -0.12f); Seg(0.045f, 0.12f, 0.045f, -0.12f); Seg(0.145f, 0.12f, 0.145f, -0.12f);
            }
            else
            {
                // no parking: blue inside the red ring, a red slash
                _paint.Plate(c + face * 0.005f, face, 0.29f * k, 16, SignBlue, 1);
                _paint.Band2(c + face * 0.01f + (right - upk) * 0.2f, c + face * 0.01f + (upk - right) * 0.2f, 0.07f * k, face, SignRed, 1);
            }
        }

        /// <summary>
        /// Older streets (shophouses, Chow Kit, the markets) get the overhead clutter: concrete poles down one
        /// pavement with wires slung along between them and now and then across, and a banner over the road.
        /// </summary>
        void OverheadLines(Frame f, Vector3 key)
        {
            if (GameManager.WebLite) return;
            float h = Hash01(key.x + f.s.x * 3f, key.z + f.s.z * 3f, 821);
            bool OldSide(float side)
            {
                var probe = f.P(Pitch * 0.5f, side * (Road * 0.5f + 5f));
                int col = Mathf.FloorToInt((probe.x - X0) / Pitch), row = Mathf.FloorToInt((probe.z - Z0) / Pitch);
                if (col < 0 || row < 0 || col >= NX || row >= NZ) return false;
                var z = ZoneAt(col, row);
                return z == Zone.Shops || z == Zone.ChowKit || z == Zone.Mamak || z == Zone.Pasar || z == Zone.Brickfields || IsKampungCell(col, row);
            }
            float pside = OldSide(1f) ? 1f : OldSide(-1f) ? -1f : 0f;
            if (pside == 0f || h > 0.7f) return;
            const float poleH = 8.2f;
            var tops = new List<Vector3>();
            for (float s = Road * 0.5f + 8f; s < Pitch - Road * 0.5f - 6f; s += 26f)
            {
                var foot = f.P(s, pside * 10.85f, G);
                _paint.Box(foot + Vector3.up * poleH * 0.5f, new Vector3(0.24f, poleH, 0.24f), 0f, Concrete, 0, SurfaceKinds.Concrete);
                _paint.Box(foot + Vector3.up * (poleH - 0.5f), Mathf.Abs(f.s.x) > 0.5f ? new Vector3(0.12f, 0.12f, 1.6f) : new Vector3(1.6f, 0.12f, 0.12f), 0f, Concrete, 1);
                _ground.ColliderOnly(foot + Vector3.up * 2f, new Vector3(0.3f, 4f, 0.3f));
                tops.Add(foot + Vector3.up * (poleH - 0.45f));
            }
            // wires along the street, sagging between the poles
            for (int n = 0; n + 1 < tops.Count; n++)
                foreach (float o in new[] { -0.7f, 0f, 0.7f })
                    Sag(tops[n] + f.t * o, tops[n + 1] + f.t * o, 0.55f, 0.035f);
            // and across the road to the far side's buildings, at every other pole
            for (int n = 0; n < tops.Count; n += 2)
            {
                var far = tops[n] - f.t * pside * 21.5f + Vector3.down * 0.8f;
                Sag(tops[n], far, 1.1f, 0.035f);
            }
            // a banner over the road (a festival, a sale, the local wakil rakyat)
            if (h < 0.3f && tops.Count >= 2)
            {
                var mid = (tops[0] + tops[1]) * 0.5f;
                var a = mid + Vector3.down * 0.6f;
                var b = a - f.t * pside * 21.5f;
                var c = (a + b) * 0.5f + Vector3.down * 0.4f;
                var col = BannerColours[Mathf.FloorToInt(Hash01(key.x, key.z, 823) * BannerColours.Length) % BannerColours.Length];
                var face = f.s;
                var across = (b - a); across.y = 0f;
                var u = across.normalized;
                var bl = c - u * 4.2f - Vector3.up * 0.6f;
                var br = c + u * 4.2f - Vector3.up * 0.6f;
                foreach (var fc in new[] { face, -face })
                {
                    var off = fc * 0.01f;
                    _paint.Poly(col, 0, 1, fc, bl + off, br + off, br + Vector3.up * 1.2f + off, bl + Vector3.up * 1.2f + off);
                    _paint.Poly(SignWhite, 0, 1, fc, bl + off * 2f + Vector3.up * 0.25f + u * 0.5f, br + off * 2f + Vector3.up * 0.25f - u * 0.5f,
                                br + off * 2f + Vector3.up * 0.95f - u * 0.5f, bl + off * 2f + Vector3.up * 0.95f + u * 0.5f);
                    // a line of "lettering"
                    for (float x = 0.8f; x < 7.6f; x += 0.55f)
                    {
                        var lp = bl + off * 3f + u * x + Vector3.up * 0.4f;
                        _paint.Poly(col, 0, 1, fc, lp, lp + u * 0.36f, lp + u * 0.36f + Vector3.up * 0.4f, lp + Vector3.up * 0.4f);
                    }
                }
                Sag(a, b, 0.4f, 0.03f);
            }
        }

        /// <summary>A slung wire from a to b (sagging `sag` metres in the middle), in a few straight pieces.</summary>
        void Sag(Vector3 a, Vector3 b, float sag, float w)
        {
            const int pieces = 5;
            var prev = a;
            for (int i = 1; i <= pieces; i++)
            {
                float t = i / (float)pieces;
                var p = Vector3.Lerp(a, b, t) + Vector3.down * sag * 4f * t * (1f - t);
                _paint.Bar(prev, p, w, CableBlack, 1);
                prev = p;
            }
        }

        bool NearLamp(Vector3 p, float r)
        {
            foreach (var q in _lampSpots) if ((q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z) < r * r) return true;
            return false;
        }

        static bool InSkip(float s, List<Vector2> skips)
        {
            foreach (var r in skips) if (s > r.x && s < r.y) return true;
            return false;
        }

        /// <summary>Is there a block's kerbed pavement on this side of the segment (where the kit's kerbs and the drain run)?</summary>
        bool PavementSide(Frame f, float side, bool ns, int i, int k)
        {
            // the cell on that side
            var probe = f.P(Pitch * 0.5f, side * (Road * 0.5f + 5f));
            int col = Mathf.FloorToInt((probe.x - X0) / Pitch), row = Mathf.FloorToInt((probe.z - Z0) / Pitch);
            if (col < 0 || row < 0 || col >= NX || row >= NZ) return false;
            return ZoneAt(col, row) != Zone.Patch && RiverAt(col, row) == null;
        }

        void Dashes(Frame f, float t, float from, float to, float dash, float gap, float w, Color col)
        {
            for (float s = from; s + dash <= to; s += dash + gap) _paint.Band(f.P(s, t), f.P(s + dash, t), w, Ypaint, col);
        }

        /// <summary>A solid line from `from` to `to`, broken over the skipped ranges.</summary>
        void Runs(Frame f, float t, float from, float to, List<Vector2> skips, float w, Color col)
        {
            float s = from;
            var cuts = new List<Vector2>(skips);
            cuts.Sort((x, y) => x.x.CompareTo(y.x));
            foreach (var c in cuts)
            {
                if (c.y <= s || c.x >= to) continue;
                if (c.x > s) _paint.Band(f.P(s, t), f.P(Mathf.Min(c.x, to), t), w, Ypaint, col);
                s = Mathf.Max(s, c.y);
            }
            if (s < to) _paint.Band(f.P(s, t), f.P(to, t), w, Ypaint, col);
        }

        void ZigZag(Frame f, float t, float from, float to)
        {
            int zigs = Mathf.Max(2, Mathf.RoundToInt((to - from) / 1.8f));
            float step = (to - from) / zigs, amp = 0.45f * Mathf.Sign(t);
            var prev = f.P(from, t - amp * 0.5f);
            for (int z = 1; z <= zigs; z++)
            {
                var next = f.P(from + step * z, t + (z % 2 == 1 ? amp * 0.5f : -amp * 0.5f));
                _paint.Band(prev, next, 0.13f, Ypaint, PaintYellow);
                prev = next;
            }
        }

        /// <summary>A gully grate in the gutter on one side: an iron frame and its slots.</summary>
        void Grate(Frame f, float s, float side)
        {
            float t0 = side * 9.42f, t1 = side * 9.93f;
            _paint.Poly(IronRim, 0, 1, Vector3.up, f.P(s - 0.45f, t0, Ycover), f.P(s - 0.45f, t1, Ycover), f.P(s + 0.45f, t1, Ycover), f.P(s + 0.45f, t0, Ycover));
            for (int n = 0; n < 6; n++)
            {
                float ss = s - 0.33f + n * 0.13f;
                _paint.Poly(GrateDark, 0, 1, Vector3.up, f.P(ss - 0.035f, t0 + side * 0.07f, Ycover + 0.001f), f.P(ss - 0.035f, t1 - side * 0.07f, Ycover + 0.001f),
                            f.P(ss + 0.035f, t1 - side * 0.07f, Ycover + 0.001f), f.P(ss + 0.035f, t0 + side * 0.07f, Ycover + 0.001f));
            }
        }

        /// <summary>The covered monsoon drain under the pavement's edge: concrete slabs with a slot each.</summary>
        void DrainCovers(Frame f, float side, float from, float to, List<Vector2> skips)
        {
            float y = G + 0.007f, t0 = side * 10.2f, t1 = side * 10.62f;
            float s = from;
            while (s < to - 0.6f)
            {
                float e = Mathf.Min(to, s + 1.2f);
                var mid = f.P((s + e) * 0.5f, 0f);
                if (!InSkip((s + e) * 0.5f, skips) && !NearRoundabout(mid, RingOuter + 4f))
                {
                    _paint.Poly(DrainSlab, SurfaceKinds.Concrete, 1, Vector3.up, f.P(s + 0.03f, t0, y), f.P(s + 0.03f, t1, y), f.P(e - 0.03f, t1, y), f.P(e - 0.03f, t0, y));
                    float m = (s + e) * 0.5f, tm = side * 10.41f;
                    _paint.Poly(GrateDark, 0, 1, Vector3.up, f.P(m - 0.3f, tm - 0.035f, y + 0.001f), f.P(m - 0.3f, tm + 0.035f, y + 0.001f),
                                f.P(m + 0.3f, tm + 0.035f, y + 0.001f), f.P(m + 0.3f, tm - 0.035f, y + 0.001f));
                }
                s = e;
            }
        }

        /// <summary>An IWK manhole (round, ribbed) or a square telecoms cover, sitting in the lane.</summary>
        void Manhole(Vector3 at, Frame f, bool round)
        {
            if (round)
            {
                _paint.Disc(at, 0.46f, 14, Ycover, IronRim, 0, 1);
                _paint.Disc(at, 0.38f, 14, Ycover + 0.001f, Iron, 0, 1);
                foreach (float o in new[] { -0.2f, 0f, 0.2f })
                    _paint.Band(at + f.t * o - f.s * 0.3f, at + f.t * o + f.s * 0.3f, 0.05f, Ycover + 0.002f, IronRim, 0, 1);
            }
            else
            {
                Vector3 c(float u, float v) => at + f.s * u + f.t * v + Vector3.up * Ycover;
                _paint.Poly(IronRim, 0, 1, Vector3.up, c(-0.45f, -0.45f), c(-0.45f, 0.45f), c(0.45f, 0.45f), c(0.45f, -0.45f));
                Vector3 d(float u, float v) => c(u, v) + Vector3.up * 0.001f;
                _paint.Poly(Iron, 0, 1, Vector3.up, d(-0.38f, -0.38f), d(-0.38f, 0.38f), d(0.38f, 0.38f), d(0.38f, -0.38f));
            }
        }

        void UtilityCover(Frame f, float s, float t)
        {
            Vector3 c(float u, float v, float y) => f.P(s + u, t + v, y);
            _paint.Poly(IronRim, 0, 1, Vector3.up, c(-0.5f, -0.32f, Ycover), c(-0.5f, 0.32f, Ycover), c(0.5f, 0.32f, Ycover), c(0.5f, -0.32f, Ycover));
            _paint.Poly(Iron, 0, 1, Vector3.up, c(-0.44f, -0.26f, Ycover + 0.001f), c(-0.44f, 0.26f, Ycover + 0.001f), c(-0.02f, 0.26f, Ycover + 0.001f), c(-0.02f, -0.26f, Ycover + 0.001f));
            _paint.Poly(Iron, 0, 1, Vector3.up, c(0.02f, -0.26f, Ycover + 0.001f), c(0.02f, 0.26f, Ycover + 0.001f), c(0.44f, 0.26f, Ycover + 0.001f), c(0.44f, -0.26f, Ycover + 0.001f));
        }

        /// <summary>
        /// An arm of a crossroads, in a frame from the junction's centre out along the arm: the zebra right outside
        /// the box, the stop line behind it on the incoming half (t &lt; 0), the turn arrows for the two lanes, and
        /// the crossing registered with the junction.
        /// </summary>
        void Approach(Frame f, (int, int) jk, int arm, JunctionControl j)
        {
            // zebra: bars along the road, the whole width
            for (float t = -9.25f; t < 9.3f; t += 1.0f)
                _paint.Poly(PaintWhite, 0, 0, Vector3.up, f.P(XingNear, t - 0.27f, Ypaint), f.P(XingNear, t + 0.27f, Ypaint), f.P(XingFar, t + 0.27f, Ypaint), f.P(XingFar, t - 0.27f, Ypaint));
            // stop line over the incoming half
            _paint.Band(f.P(StopLine, -9.5f), f.P(StopLine, -0.3f), 0.45f, Ypaint, PaintWhite);
            // tactile paving where people step off (block pavements only)
            foreach (float side in new[] { -1f, 1f })
                if (PavementSide(f, side, false, 0, 0))
                {
                    float y = G + 0.008f, t0 = side * 10.22f, t1 = side * 10.85f;
                    _paint.Poly(Tactile, 0, 1, Vector3.up, f.P(XingNear + 0.3f, t0, y), f.P(XingNear + 0.3f, t1, y), f.P(XingFar - 0.3f, t1, y), f.P(XingFar - 0.3f, t0, y));
                }

            // arrows: the kerb lane turns left (and goes straight on), the inner lane turns right
            var (n, e, s, w) = _cityBoxes[jk];
            bool Has(int a) => a == 0 ? n : a == 1 ? e : a == 2 ? s : w;
            bool straight = Has((arm + 2) % 4), left = Has((arm + 1) % 4), right = Has((arm + 3) % 4);
            int outer = left ? (straight ? 3 : 1) : 0, inner = right ? (straight ? 4 : 2) : 0;   // 0 straight, 1 left, 2 right, 3 left+straight, 4 right+straight
            foreach (float at in new[] { StopLine + 6f, StopLine + 26f })
            {
                Arrow(f, at, -7.5f, outer);
                Arrow(f, at, -2.5f, inner);
            }

            // the crossing itself
            var c = new Crossing
            {
                centre = f.P((XingNear + XingFar) * 0.5f, 0f), across = f.t, halfWidth = Road * 0.5f, junction = j, arm = arm,
            };
            j.crossings[arm] = c;
            TrafficSignals.Crossings.Add(c);
        }

        /// <summary>A Malaysian lane arrow lying in the lane, its tail at s (pointing at the junction, -s).</summary>
        void Arrow(Frame f, float s, float t, int kind)
        {
            // local (u forward toward the junction, v to the driver's left); the driver heads -s, their left is -t
            Vector3 P(float u, float v) => f.P(s - u, t - v, Ypaint);
            var up = Vector3.up;
            void Q(float u0, float v0, float u1, float v1) => _paint.Poly(PaintWhite, 0, 0, up, P(u0, v0), P(u0, v1), P(u1, v1), P(u1, v0));
            void Head(float u, float v, float du, float dv)       // a triangle pointing along (du, dv) from (u, v)
            {
                float px = -dv, py = du;                            // across the head
                _paint.Poly(PaintWhite, 0, 0, up, P(u + px * 0.55f, v + py * 0.55f), P(u - px * 0.55f, v - py * 0.55f), P(u + du * 1.3f, v + dv * 1.3f));
            }
            bool goes = kind == 0 || kind == 3 || kind == 4;
            float shaftEnd = goes ? 3.4f : 2.2f;
            Q(0f, -0.14f, shaftEnd, 0.14f);
            if (goes) Head(shaftEnd, 0f, 1f, 0f);
            float bu = goes ? 2.2f : 1.7f;                         // the turn's branch (near the head when it also goes on)
            if (kind == 1 || kind == 3)
            {
                Q(bu, 0f, bu + 0.35f, 0.95f);                       // to the left
                Head(bu + 0.175f, 0.95f, 0f, 1f);
            }
            if (kind == 2 || kind == 4)
            {
                Q(bu, -0.95f, bu + 0.35f, 0f);
                Head(bu + 0.175f, -0.95f, 0f, -1f);
            }
        }

        /// <summary>Give way to the roundabout: a double broken line and the triangle.</summary>
        void GiveWay(Frame f, float at)
        {
            foreach (float o in new[] { 0f, 0.5f })
                for (float t = -9.4f; t < -0.6f; t += 1.2f)
                    _paint.Band(f.P(at + o, t), f.P(at + o, t + 0.6f), 0.18f, Ypaint, PaintWhite);
            var tip = f.P(at + 2.5f, -5f, Ypaint);
            var b0 = f.P(at + 6.5f, -6.4f, Ypaint);
            var b1 = f.P(at + 6.5f, -3.6f, Ypaint);
            _paint.Poly(PaintWhite, 0, 0, Vector3.up, tip, b0, b1);
            var c = (tip + b0 + b1) / 3f + Vector3.up * 0.0006f;
            _paint.Poly(Asphalt, SurfaceKinds.Asphalt, 0, Vector3.up, Vector3.Lerp(c, tip, 0.55f), Vector3.Lerp(c, b0, 0.55f), Vector3.Lerp(c, b1, 0.55f));
        }

        /// <summary>A junction's box: asphalt, the kerb lines round its closed sides, lane lines straight through
        /// where nothing crosses, and the yellow criss-cross at the busy signalled ones.</summary>
        void PaintBox(int i, int k, (bool n, bool e, bool s, bool w) arms, Dictionary<(int, int), JunctionControl> ends)
        {
            var c = new Vector3(RoadX(i), 0f, RoadZ(k));
            float h = Road * 0.5f;
            _paint.Poly(Asphalt, SurfaceKinds.Asphalt, 0, Vector3.up, c + new Vector3(-h, 0, -h), c + new Vector3(-h, 0, h), c + new Vector3(h, 0, h), c + new Vector3(h, 0, -h));
            bool[] has = { arms.n, arms.e, arms.s, arms.w };
            int count = (arms.n ? 1 : 0) + (arms.e ? 1 : 0) + (arms.s ? 1 : 0) + (arms.w ? 1 : 0);
            // closed sides: the kerb lines carry on across
            for (int a = 0; a < 4; a++)
            {
                if (has[a]) continue;
                var f = new Frame(c, ArmDir((a + 1) % 4));           // along the closed side
                foreach (float off in new[] { 9.05f, 9.3f })
                    _paint.Band(f.P(-h, off), f.P(h, off), 0.1f, Ypaint, PaintYellow);
            }
            // straight through (two opposite arms): the lines don't stop
            if (count == 2 && (arms.n && arms.s || arms.e && arms.w))
            {
                var f = new Frame(c, arms.n ? Vector3.forward : Vector3.right);
                Dashes(f, 0f, -h, h, 3.5f, 5.5f, 0.15f, PaintWhite);
                Dashes(f, -5f, -h, h, 3f, 4.5f, 0.13f, PaintWhite);
                Dashes(f, 5f, -h, h, 3f, 4.5f, 0.13f, PaintWhite);
            }
            // yellow box at a busy crossroads
            if (ends.TryGetValue((i, k), out var j) && j.signal && count >= 3 && Hash01(c.x, c.z, 917) < 0.7f)
            {
                float e = h - 0.6f, wd = 0.22f;
                var corners = new[] { c + new Vector3(-e, 0, -e), c + new Vector3(-e, 0, e), c + new Vector3(e, 0, e), c + new Vector3(e, 0, -e) };
                for (int q = 0; q < 4; q++) _paint.Band(corners[q], corners[(q + 1) % 4], wd, Ypaint, PaintYellow);
                for (float o = -2f * e + 2.6f; o < 2f * e - 1f; o += 2.6f)
                {
                    // x - z = o and x + z = o, clipped to the box
                    float z0 = Mathf.Max(-e, -e - o), z1 = Mathf.Min(e, e - o);
                    if (z1 - z0 > 0.5f) _paint.Band(c + new Vector3(z0 + o, 0, z0), c + new Vector3(z1 + o, 0, z1), wd, Ypaint + 0.0005f, PaintYellow);
                    z0 = Mathf.Max(-e, o - e); z1 = Mathf.Min(e, o + e);
                    if (z1 - z0 > 0.5f) _paint.Band(c + new Vector3(o - z0, 0, z0), c + new Vector3(o - z1, 0, z1), wd, Ypaint + 0.0008f, PaintYellow);
                }
            }
        }

        // ------------------------------------------------------------------ signal poles and lamps
        /// <summary>
        /// Every approach of a signalled junction gets a pole on its near-side kerb, just past the stop line: a head
        /// on the pole and one on a mast arm over the lanes, both facing the oncoming traffic, and a green-man head
        /// facing over the crossing (with a short post on the far kerb facing back).
        /// </summary>
        void SignalPoles(int i, int k, JunctionControl j)
        {
            var c = j.pos;
            for (int arm = 0; arm < 4; arm++)
            {
                if (j.crossings[arm] == null) continue;           // a city arm (not a flyover ramp or a kampung lane)
                var f = new Frame(c, ArmDir(arm));
                int group = arm == 0 || arm == 2 ? 0 : 1;
                var foot = f.P(StopLine - 0.7f, -10.75f, G);
                const float pole = 6.1f;
                _ground.Box(foot + Vector3.up * pole * 0.5f, new Vector3(0.18f, pole, 0.18f), PoleGrey, false, 1.5f);
                _ground.ColliderOnly(foot + Vector3.up * 2.2f, new Vector3(0.3f, 4.4f, 0.3f));
                for (int band = 0; band < 4; band++)                 // the striped sleeve at its foot
                    _ground.Box(foot + Vector3.up * (0.15f + band * 0.25f), new Vector3(0.24f, 0.25f, 0.24f), band % 2 == 0 ? PaintYellow : HeadBlack, false, 1.5f);
                // mast arm out over the lanes
                var armEnd = f.P(StopLine - 0.7f, -3.2f, G + pole - 0.25f);
                var armStart = foot + Vector3.up * (pole - 0.25f);
                var mid = (armStart + armEnd) * 0.5f;
                _ground.Box(mid, Vec(f.t, 0.14f, Vector3.Distance(armStart, armEnd), 0.14f), PoleGrey, false, 1.5f);
                SignalHead(f.P(StopLine - 0.7f, -6.2f, G + pole - 1.1f), f.s, f.t, group, j.offset, false);
                SignalHead(f.P(StopLine - 0.7f, -10.75f, G + 3.1f) + f.s * 0.28f, f.s, f.t, group, j.offset, false);
                // green men: on this pole looking over the road, and a post on the far kerb looking back
                SignalHead(foot + Vector3.up * 2.25f + f.t * 0.24f, f.t, f.s, group, j.offset, true);
                var far = f.P(StopLine - 0.7f, 10.75f, G);
                _ground.Box(far + Vector3.up * 1.5f, new Vector3(0.14f, 3f, 0.14f), PoleGrey, false, 1.5f);
                _ground.ColliderOnly(far + Vector3.up * 1.5f, new Vector3(0.25f, 3f, 0.25f));
                SignalHead(far + Vector3.up * 2.25f - f.t * 0.2f, -f.t, f.s, group, j.offset, true);
            }
        }

        /// <summary>A box size given in a frame (along `x` = across, then up, then along the other horizontal).</summary>
        static Vector3 Vec(Vector3 alongX, float wX, float lenAlong, float wZ)
        {
            // boxes are axis aligned: the long side goes along whichever world axis alongX is
            return Mathf.Abs(alongX.x) > 0.5f ? new Vector3(lenAlong, wX, wZ) : new Vector3(wZ, wX, lenAlong);
        }

        /// <summary>A signal head: black housing on a white backboard, visors, and three lamps (red top) or the two
        /// men, all facing `facing`. `side` is the horizontal across it.</summary>
        void SignalHead(Vector3 centre, Vector3 facing, Vector3 side, int group, float offset, bool ped)
        {
            // a touch bigger than life, so they read from the driving camera
            float hh = ped ? 0.7f : 1.34f, hw = ped ? 0.36f : 0.47f, d = ped ? 0.22f : 0.34f;
            bool alongX = Mathf.Abs(facing.x) > 0.5f;
            Vector3 Size(float w, float h, float dep) => alongX ? new Vector3(dep, h, w) : new Vector3(w, h, dep);
            _ground.Box(centre, Size(hw, hh, d), HeadBlack, false, 1.5f);
            _ground.Box(centre - facing * (d * 0.5f + 0.02f), Size(hw + 0.2f, hh + 0.2f, 0.04f), BoardWhite, false, 1.5f);
            var front = centre + facing * (d * 0.5f + 0.006f);
            Color Code(float role) => new Color(role, group, offset, 1f);
            if (!ped)
            {
                for (int l = 0; l < 3; l++)
                {
                    float y = 0.42f - l * 0.42f;
                    var lc = front + Vector3.up * y;
                    // visor over the lamp
                    _ground.Box(lc + Vector3.up * 0.19f + facing * 0.11f, Size(0.38f, 0.035f, 0.22f), HeadBlack, false, 1.5f);
                    var pts = new Vector3[12];
                    for (int q = 0; q < 12; q++)
                    {
                        float a = q * Mathf.PI * 2f / 12f;
                        pts[q] = lc + side * Mathf.Cos(a) * 0.165f + Vector3.up * Mathf.Sin(a) * 0.165f;
                    }
                    _paint.Poly(Code(l == 0 ? 0f : l == 1 ? 0.25f : 0.5f), 0, 2, facing, pts);
                }
                return;
            }
            // the men: a red one standing (top), a green one walking (bottom)
            void Rect(Vector3 at, float w, float h, float role) =>
                _paint.Poly(Code(role), 0, 2, facing, at - side * w * 0.5f - Vector3.up * h * 0.5f, at + side * w * 0.5f - Vector3.up * h * 0.5f,
                            at + side * w * 0.5f + Vector3.up * h * 0.5f, at - side * w * 0.5f + Vector3.up * h * 0.5f);
            var red = front + Vector3.up * 0.15f;
            Rect(red + Vector3.up * 0.085f, 0.05f, 0.05f, 0.75f);
            Rect(red, 0.07f, 0.1f, 0.75f);
            Rect(red - Vector3.up * 0.09f - side * 0.02f, 0.025f, 0.08f, 0.75f);
            Rect(red - Vector3.up * 0.09f + side * 0.02f, 0.025f, 0.08f, 0.75f);
            var grn = front - Vector3.up * 0.15f;
            Rect(grn + Vector3.up * 0.085f + side * 0.01f, 0.05f, 0.05f, 1f);
            Rect(grn, 0.06f, 0.1f, 1f);
            _paint.Poly(Code(1f), 0, 2, facing, grn - Vector3.up * 0.04f - side * 0.02f, grn - Vector3.up * 0.04f + side * 0.01f,
                        grn - Vector3.up * 0.13f - side * 0.06f, grn - Vector3.up * 0.13f - side * 0.09f);
            _paint.Poly(Code(1f), 0, 2, facing, grn - Vector3.up * 0.04f - side * 0.01f, grn - Vector3.up * 0.04f + side * 0.02f,
                        grn - Vector3.up * 0.13f + side * 0.08f, grn - Vector3.up * 0.13f + side * 0.05f);
        }

        // ------------------------------------------------------------------ crossings join the pavement loops
        /// <summary>Find the pavement loop that reaches each end of each crossing (the loops run 3 m in from the kerb).</summary>
        int LinkCrossings()
        {
            int linked = 0;
            var near = new List<WalkZone>();
            foreach (var c in TrafficSignals.Crossings)
            {
                // the loops anywhere near this crossing (then each look-out point only tries those)
                near.Clear();
                float reach = c.halfWidth + 9f;
                foreach (var z in _city.walkZones)
                    if (!z.kampung && c.centre.x > z.bounds.xMin - reach && c.centre.x < z.bounds.xMax + reach && c.centre.z > z.bounds.yMin - reach && c.centre.z < z.bounds.yMax + reach)
                        near.Add(z);
                for (int side = -1; side <= 1; side += 2)
                {
                    WalkZone best = null;
                    float bd = 1.6f * 1.6f, bu = 0f;
                    // the loops run a metre or three in from the kerb: look along the crossing's line out past it
                    for (float o = 1f; o <= 5.01f; o += 0.5f)
                    {
                        var p = c.centre + c.across * side * (c.halfWidth + o);
                        foreach (var z in near)
                        {
                            if (p.x < z.bounds.xMin - 3f || p.x > z.bounds.xMax + 3f || p.z < z.bounds.yMin - 3f || p.z > z.bounds.yMax + 3f) continue;
                            float u = z.Closest(p);
                            var q = z.PointAt(u, 0f);
                            float d = (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z);
                            if (d < bd) { bd = d; best = z; bu = u; }
                        }
                    }
                    if (side < 0) { c.za = best; c.ua = bu; } else { c.zb = best; c.ub = bu; }
                }
                if (c.za != null && c.zb != null && c.za != c.zb)
                {
                    c.za.crossings.Add(c);
                    c.zb.crossings.Add(c);
                    linked++;
                }
                else { c.za = null; c.zb = null; }
            }
            return linked;
        }
    }
}
