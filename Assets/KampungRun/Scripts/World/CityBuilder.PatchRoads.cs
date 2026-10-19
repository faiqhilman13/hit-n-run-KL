using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// The real-KL streets in the patches get painted too. Their files hold the road surface mesh and the street
    /// graph but no widths, so each street's width is measured off the surface (marching out from the graph line
    /// until it leaves the asphalt), and then it gets what the grid roads get, fitted to its width: centre and lane
    /// lines (one-way streets get their arrows), double yellow lines, gully grates, manholes, wear; and where a
    /// street meets a junction, a stop line, and a zebra on about half the arms (the crossings join the pavement
    /// loops either side, so people cross here too, and drivers wait for them and give way at the junction).
    /// </summary>
    public partial class CityBuilder
    {
        readonly List<(KLPatch p, RoadNetwork.Node[] nodes)> _patchRoads = new List<(KLPatch, RoadNetwork.Node[])>();

        /// <summary>Point-in-road lookups over a patch's road surface mesh (in plan).</summary>
        class RoadMask
        {
            const float Cell = 6f;
            readonly Vector3[] _v;
            readonly int[] _t;
            readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

            public RoadMask(KLPatch.MeshData md)
            {
                _v = md.v;
                _t = md.t;
                for (int i = 0; i < _t.Length; i += 3)
                {
                    Vector3 a = _v[_t[i]], b = _v[_t[i + 1]], c = _v[_t[i + 2]];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / Cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / Cell);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / Cell), z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / Cell);
                    if ((x1 - x0) * (z1 - z0) > 400) continue;          // (a degenerate sliver: never mind)
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            if (!_cells.TryGetValue(Key(x, z), out var l)) _cells[Key(x, z)] = l = new List<int>();
                            l.Add(i);
                        }
                }
            }

            public bool Inside(float x, float z)
            {
                if (!_cells.TryGetValue(Key(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell)), out var l)) return false;
                foreach (int i in l)
                {
                    Vector3 a = _v[_t[i]], b = _v[_t[i + 1]], c = _v[_t[i + 2]];
                    float d1 = (x - b.x) * (a.z - b.z) - (a.x - b.x) * (z - b.z);
                    float d2 = (x - c.x) * (b.z - c.z) - (b.x - c.x) * (z - c.z);
                    float d3 = (x - a.x) * (c.z - a.z) - (c.x - a.x) * (z - a.z);
                    bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                    if (!(neg && pos)) return true;
                }
                return false;
            }

            /// <summary>How far from p the asphalt goes in direction dir (up to max).</summary>
            public float Reach(Vector3 p, Vector3 dir, float max = 16f)
            {
                for (float d = 0.3f; d < max; d += 0.3f)
                    if (!Inside(p.x + dir.x * d, p.z + dir.z * d)) return d - 0.15f;
                return max;
            }
        }

        void PaintPatches()
        {
            int streets = 0, xings = 0;
            foreach (var (p, nodes) in _patchRoads) PaintPatch(p, nodes, ref streets, ref xings);
            Debug.Log($"[Roads] real-KL streets: {streets} stretches painted, {xings} zebras");
        }

        void PaintPatch(KLPatch p, RoadNetwork.Node[] nodes, ref int streets, ref int xings)
        {
            var md = p.meshes.Find(m => m.key == "road");
            if (md == null) return;
            var mask = new RoadMask(md);
            int n = p.nodes.Length, ne = p.edges.Length;
            var nbrs = new HashSet<int>[n];
            for (int i = 0; i < n; i++) nbrs[i] = new HashSet<int>();
            foreach (var e in p.edges) { nbrs[e.a].Add(e.b); nbrs[e.b].Add(e.a); }

            // each street's half-widths either side of its graph line
            var hl = new float[ne];
            var hr = new float[ne];
            for (int ei = 0; ei < ne; ei++)
            {
                var e = p.edges[ei];
                Vector3 a = p.nodes[e.a], b = p.nodes[e.b];
                hl[ei] = hr[ei] = -1f;
                if (a.y > 0.5f || b.y > 0.5f) continue;              // the flyover decks: not here
                var d = b - a; d.y = 0f;
                float len = d.magnitude;
                if (len < 2f) continue;
                d /= len;
                var left = new Vector3(-d.z, 0f, d.x);
                var ls = new List<float>();
                var rs = new List<float>();
                for (float f = 0.25f; f < 0.8f; f += 0.25f)
                {
                    var q = Vector3.Lerp(a, b, f);
                    if (!mask.Inside(q.x, q.z)) continue;
                    ls.Add(mask.Reach(q, left));
                    rs.Add(mask.Reach(q, -left));
                }
                if (ls.Count == 0) continue;
                ls.Sort(); rs.Sort();
                hl[ei] = ls[ls.Count / 2];
                hr[ei] = rs[rs.Count / 2];
            }
            // how big each junction is: the widest street through it
            var R = new float[n];
            for (int ei = 0; ei < ne; ei++)
            {
                if (hl[ei] < 0f) continue;
                var e = p.edges[ei];
                float w = Mathf.Max(hl[ei], hr[ei]) + 0.8f;
                if (nbrs[e.a].Count >= 3) R[e.a] = Mathf.Max(R[e.a], w);
                if (nbrs[e.b].Count >= 3) R[e.b] = Mathf.Max(R[e.b], w);
            }

            var need = new float[n];
            for (int ei = 0; ei < ne; ei++)
            {
                float L = hl[ei], Rt = hr[ei];
                if (L < 0f || L + Rt < 4.5f || L > 14f || Rt > 14f) continue;   // alleys and plazas: leave them be
                var e = p.edges[ei];
                Vector3 a = p.nodes[e.a], b = p.nodes[e.b];
                var f = new Frame(a, b - a);
                float len = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
                float w = L + Rt, tc = (L - Rt) * 0.5f;
                bool twoWay = e.dir == 0;
                var rnd = Dice(a.x + b.x * 0.5f, a.z + b.z * 0.5f, 77);

                // zebras: about half the arms of a crossroads, on streets wide enough to be worth it
                bool za = R[e.a] > 0f && w >= 6f && Hash01(a.x + b.x * 0.01f, a.z + b.z * 0.01f, 501) < 0.5f && len > R[e.a] + 9f;
                bool zb = R[e.b] > 0f && w >= 6f && Hash01(b.x + a.x * 0.01f, b.z + a.z * 0.01f, 501) < 0.5f && len > R[e.b] + 9f;
                float s0 = R[e.a] > 0f ? R[e.a] + (za ? 4.6f : 0.6f) : 0f;
                float s1 = R[e.b] > 0f ? len - R[e.b] - (zb ? 4.6f : 0.6f) : len;
                if (s1 - s0 < 2f) continue;
                streets++;

                // wear first (under the paint)
                if (rnd.NextDouble() < 0.35)
                {
                    float ps = RR(rnd, s0, Mathf.Max(s0, s1 - 4f)), pl = RR(rnd, 2f, 6f), pw = RR(rnd, 1.2f, Mathf.Min(4f, w * 0.6f)), pt = RR(rnd, -Rt + pw * 0.5f, L - pw * 0.5f);
                    _paint.Poly(rnd.NextDouble() < 0.5 ? TarDark : TarLight, SurfaceKinds.Asphalt, 0, Vector3.up,
                        f.P(ps, pt - pw * 0.5f, Ypatch), f.P(ps, pt + pw * 0.5f, Ypatch), f.P(Mathf.Min(ps + pl, s1), pt + pw * 0.5f, Ypatch), f.P(Mathf.Min(ps + pl, s1), pt - pw * 0.5f, Ypatch));
                }

                // lane lines
                if (twoWay)
                {
                    if (w >= 13f)
                    {
                        foreach (float o in new[] { -0.17f, 0.17f }) _paint.Band(f.P(s0, tc + o), f.P(s1, tc + o), 0.12f, Ypaint, PaintWhite);
                        Dashes(f, tc + w * 0.25f, s0 + 1f, s1 - 1f, 3f, 4.5f, 0.13f, PaintWhite);
                        Dashes(f, tc - w * 0.25f, s0 + 1f, s1 - 1f, 3f, 4.5f, 0.13f, PaintWhite);
                    }
                    else if (w >= 5.5f) Dashes(f, tc, s0 + 0.5f, s1 - 0.5f, 3f, 4.5f, 0.14f, PaintWhite);
                }
                else
                {
                    if (w >= 11f) { Dashes(f, tc + w / 6f, s0 + 1f, s1 - 1f, 3f, 4.5f, 0.13f, PaintWhite); Dashes(f, tc - w / 6f, s0 + 1f, s1 - 1f, 3f, 4.5f, 0.13f, PaintWhite); }
                    else if (w >= 7f) Dashes(f, tc, s0 + 1f, s1 - 1f, 3f, 4.5f, 0.13f, PaintWhite);
                    // one-way: arrows down the lanes now and then
                    if (s1 - s0 > 14f && Hash01(a.x, a.z, 513) < 0.6f)
                    {
                        bool fwd = e.dir > 0;
                        float at = (s0 + s1) * 0.5f;
                        var g = fwd ? new Frame(f.P(at + 2.4f, 0f), -f.s) : new Frame(f.P(at - 2.4f, 0f), f.s);
                        // (the arrow helper points at -s from its tail; turn the frame so it points the way traffic goes)
                        float lt = fwd ? -tc : tc;
                        if (w >= 7f) { Arrow(g, 0f, lt + w * 0.25f, 0); Arrow(g, 0f, lt - w * 0.25f, 0); }
                        else Arrow(g, 0f, lt, 0);
                    }
                }

                // kerbs: double yellow, and the grates in the gutter
                if (w >= 6f)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float edge = side > 0 ? L : -Rt;
                        foreach (float off in new[] { 0.45f, 0.7f })
                            _paint.Band(f.P(s0, edge - side * off), f.P(s1, edge - side * off), 0.1f, Ypaint, PaintYellow);
                        for (float gs = s0 + RR(rnd, 3f, 9f); gs < s1 - 1f; gs += RR(rnd, 13f, 19f))
                        {
                            float t0 = edge - side * 0.08f, t1 = edge - side * 0.32f;
                            _paint.Poly(IronRim, 0, 1, Vector3.up, f.P(gs - 0.45f, t0, Ycover), f.P(gs - 0.45f, t1, Ycover), f.P(gs + 0.45f, t1, Ycover), f.P(gs + 0.45f, t0, Ycover));
                            for (int k = 0; k < 5; k++)
                            {
                                float ss = gs - 0.3f + k * 0.15f;
                                _paint.Poly(GrateDark, 0, 1, Vector3.up, f.P(ss - 0.035f, t0 - side * 0.04f, Ycover + 0.001f), f.P(ss - 0.035f, t1 + side * 0.04f, Ycover + 0.001f),
                                            f.P(ss + 0.035f, t1 + side * 0.04f, Ycover + 0.001f), f.P(ss + 0.035f, t0 - side * 0.04f, Ycover + 0.001f));
                            }
                        }
                    }
                // a manhole, sometimes
                if (s1 - s0 > 10f && rnd.NextDouble() < 0.45)
                {
                    float hs = RR(rnd, s0 + 3f, s1 - 3f);
                    float ht = twoWay && w >= 5.5f ? tc + (rnd.NextDouble() < 0.5 ? -1f : 1f) * w * 0.25f : tc;
                    Manhole(f.P(hs, ht), f, rnd.NextDouble() < 0.7);
                }

                // the junction ends: zebra and stop line (on the incoming half; all of it on a one-way coming in)
                for (int end = 0; end < 2; end++)
                {
                    int jn = end == 0 ? e.a : e.b;
                    if (R[jn] <= 0f) continue;
                    bool zebra = end == 0 ? za : zb;
                    bool comingIn = twoWay || (end == 1 ? e.dir > 0 : e.dir < 0);
                    // frame from the junction out along this street
                    var g = end == 0 ? f : new Frame(b, a - b);
                    float gl = end == 0 ? L : Rt, gr = end == 0 ? Rt : L;     // (left/right swap when we look the other way)
                    float gtc = (gl - gr) * 0.5f;
                    float r0 = R[jn];
                    if (zebra)
                    {
                        for (float t = -gr + 0.5f; t < gl - 0.3f; t += 1.0f)
                            _paint.Poly(PaintWhite, 0, 0, Vector3.up, g.P(r0, t - 0.27f, Ypaint), g.P(r0, t + 0.27f, Ypaint), g.P(r0 + 3.6f, t + 0.27f, Ypaint), g.P(r0 + 3.6f, t - 0.27f, Ypaint));
                        var node = nodes[jn];
                        var other = nodes[end == 0 ? e.b : e.a];
                        if (node.control != null)
                        {
                            var c = new Crossing
                            {
                                centre = g.P(r0 + 1.8f, gtc), across = g.t, halfWidth = (gl + gr) * 0.5f, junction = node.control, arm = -1,
                            };
                            node.control.byNode ??= new Dictionary<RoadNetwork.Node, Crossing>();
                            node.control.byNode[other] = c;
                            TrafficSignals.Crossings.Add(c);
                            xings++;
                        }
                    }
                    if (comingIn)
                    {
                        float sl = r0 + (zebra ? 4.4f : 0.4f);
                        // incoming traffic is on its own left: our -t side, looking out from the junction
                        float t0 = twoWay ? -gr + 0.3f : -gr + 0.3f, t1 = twoWay ? gtc - 0.25f : gl - 0.3f;
                        if (t1 - t0 > 1f) _paint.Band(g.P(sl, t0), g.P(sl, t1), 0.4f, Ypaint, PaintWhite);
                    }
                    // drivers stop back from the junction here (far enough out to be seen before they turn in)
                    need[jn] = Mathf.Max(need[jn], Mathf.Max(7f, r0 + (zebra ? 4.4f : 0.4f)));
                }
            }
            for (int i = 0; i < n; i++)
            {
                var jc = nodes[i].control;
                if (jc == null || jc.grid || R[i] <= 0f) continue;
                if (need[i] > 0f) jc.stopDist = need[i];
                jc.boxRadius = Mathf.Max(4f, R[i] - 0.8f);
            }
        }
    }
}
