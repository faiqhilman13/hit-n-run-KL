using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// KL's elevated rail in the real-KL patches: the monorail's twin beams and the LRT's box girders come from
    /// OpenStreetMap as patch meshes ("guideway"); this models what holds them up and what runs on them.
    /// The patch writes every pier as a physics-only box ("pillar_col"), flyover and rail alike. The flyover
    /// ones are modelled by Arch.PierSpec and keep their boxes; the rail ones were never drawn (the guideways
    /// floated over invisible columns), so here each becomes an Arch.RailPierSpec with a solid that matches
    /// what is drawn. The monorail's two beams share one pier and crosshead, as they do in KL.
    /// </summary>
    public partial class CityBuilder
    {
        readonly List<(bool monorail, Vector3[] pts)> _railLines = new List<(bool, Vector3[])>();

        /// <summary>Monorail beam / LRT deck top above the patch's rail line (patches.py RAIL_H and build_rails).</summary>
        static float RunningTop(bool monorail, float lineY) => monorail ? lineY + 1.5f : lineY;

        void PatchPillars(KLPatch p, KLPatch.MeshData md, List<CombineInstance> solids)
        {
            // group the boxes' vertices: each pier's are written together
            var groups = new List<(int from, int to)>();
            int start = 0;
            for (int i = 1; i <= md.v.Length; i++)
            {
                if (i < md.v.Length)
                {
                    var d = md.v[i] - md.v[start];
                    if (d.x * d.x + d.z * d.z < 36f) continue;
                }
                groups.Add((start, i));
                start = i;
            }
            var keepTris = new List<int>();
            var keep = new bool[md.v.Length];
            var piers = new List<(Vector3 foot, float top, float half, bool monorail, Vector3 along)>();
            foreach (var (from, to) in groups)
            {
                Vector3 c = Vector3.zero;
                int nb = 0;
                float top = 0f;
                for (int i = from; i < to; i++)
                {
                    top = Mathf.Max(top, md.v[i].y);
                    if (md.v[i].y < 0.05f) { c += md.v[i]; nb++; }
                }
                if (nb == 0) continue;
                c /= nb;
                float half = 0f;
                for (int i = from; i < to; i++) if (md.v[i].y < 0.05f) half = Mathf.Max(half, Mathf.Abs(md.v[i].x - c.x), Mathf.Abs(md.v[i].z - c.z));
                bool flyover = false;
                foreach (var (pos, _, _, _) in p.piers)
                    if ((new Vector2(c.x, c.z) - pos).sqrMagnitude < 2.25f) { flyover = true; break; }
                if (flyover)
                {
                    for (int i = from; i < to; i++) keep[i] = true;
                    continue;
                }
                // the rail line it stands under: its kind and heading
                bool mono = half < 0.85f;
                var along = Vector3.forward;
                float best = 25f;
                foreach (var r in p.rails)
                    for (int k = 0; k + 1 < r.pts.Length; k++)
                    {
                        Vector3 a = r.pts[k], b = r.pts[k + 1];
                        var ab = new Vector2(b.x - a.x, b.z - a.z);
                        var ac = new Vector2(c.x - a.x, c.z - a.z);
                        float t = Mathf.Clamp01(Vector2.Dot(ac, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                        float dd = (ac - ab * t).sqrMagnitude;
                        if (dd < best) { best = dd; mono = r.kind == "monorail"; along = new Vector3(ab.x, 0f, ab.y).normalized; }
                    }
                piers.Add((new Vector3(c.x, 0f, c.z), top, half, mono, along));
            }
            // the flyover piers keep the patch's boxes
            for (int i = 0; i + 2 < md.t.Length; i += 3)
                if (keep[md.t[i]] && keep[md.t[i + 1]] && keep[md.t[i + 2]]) { keepTris.Add(md.t[i]); keepTris.Add(md.t[i + 1]); keepTris.Add(md.t[i + 2]); }
            if (keepTris.Count > 0)
            {
                var km = new Mesh { name = p.name + "_flyover_piers", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                km.vertices = md.v;
                km.triangles = keepTris.ToArray();
                solids.Add(new CombineInstance { mesh = km, transform = Matrix4x4.identity });
            }

            // the monorail's twin beams: the two lines' piers stand side by side; one pier carries both
            var used = new bool[piers.Count];
            for (int i = 0; i < piers.Count; i++)
            {
                if (used[i]) continue;
                var a = piers[i];
                int twin = -1;
                if (a.monorail)
                {
                    float bd = 7f * 7f;
                    for (int j = i + 1; j < piers.Count; j++)
                    {
                        if (used[j] || !piers[j].monorail) continue;
                        var off = piers[j].foot - a.foot;
                        if (off.sqrMagnitude < bd && Mathf.Abs(Vector3.Dot(off, a.along)) < 3f) { bd = off.sqrMagnitude; twin = j; }
                    }
                }
                Vector3 foot = a.foot;
                float span = 0f, top = a.top;
                if (twin >= 0)
                {
                    used[twin] = true;
                    var b = piers[twin];
                    var across = Vector3.Cross(Vector3.up, a.along);
                    span = Mathf.Abs(Vector3.Dot(b.foot - a.foot, across));
                    foot = (a.foot + b.foot) * 0.5f;
                    top = Mathf.Min(a.top, b.top);
                }
                // the drawn guideway's underside: the monorail beam sits on the crosshead, the LRT girder on the hammerhead
                float under = a.monorail ? top + 1.4f : top;
                var spec = new Arch.RailPierSpec
                {
                    foot = foot, along = a.along, top = under, span = span, monorail = a.monorail,
                    seed = (int)(Hash01(foot.x, foot.z, 41) * 100000f),
                };
                _arch.Add(spec);
                // its solid: the column, turned with the line
                var rot = Quaternion.LookRotation(a.along, Vector3.up);
                float colTop = under - (a.monorail ? 1.15f : 1.5f);
                var size = a.monorail ? new Vector3(1.56f, colTop, 1.16f) : new Vector3(1.8f, colTop, 1.8f);
                _ground.ColliderOnly(foot + Vector3.up * colTop * 0.5f, size, rot);
            }
        }

        static IEnumerable<(bool, Vector3[])> RailRuns(KLPatch p)
        {
            foreach (var r in p.rails)
                if (r.pts.Length >= 2) yield return (r.kind == "monorail", r.pts);
        }

        /// <summary>After every patch: the running surface along the lines, and the trains. Lines cut at a patch's
        /// edge are joined back up with the next patch's.</summary>
        void FinishRails()
        {
            // the rail lines of patches with no piers (none written) still get their track
            var lines = JoinLines(_railLines);
            int trains = 0;
            foreach (var (mono, pts) in lines)
            {
                var top = new Vector3[pts.Length];
                for (int i = 0; i < pts.Length; i++) top[i] = new Vector3(pts[i].x, RunningTop(mono, pts[i].y), pts[i].z);
                for (int i = 0; i + 1 < top.Length; i++)
                {
                    // long stretches are split so each piece streams with its own city cell
                    float len = Vector3.Distance(top[i], top[i + 1]);
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / 40f));
                    for (int k = 0; k < n; k++)
                        _arch.Add(new Arch.RailTrackSpec { a = Vector3.Lerp(top[i], top[i + 1], k / (float)n), b = Vector3.Lerp(top[i], top[i + 1], (k + 1) / (float)n), monorail = mono });
                }
                float total = 0f;
                for (int i = 0; i + 1 < top.Length; i++) total += Vector3.Distance(top[i], top[i + 1]);
                if (total < 90f) continue;
                var go = new GameObject(mono ? "MonorailTrain" : "LrtTrain");
                go.transform.SetParent(_city.root, false);
                var tr = go.AddComponent<RailTrain>();
                tr.pts = top;
                tr.cars = new Transform[2];
                tr.carSpacing = Arch.Railwork.CarLength + 0.5f;
                int seed = 7919 * (trains + 1);
                for (int c = 0; c < 2; c++)
                {
                    var car = new GameObject("Car" + c);
                    car.transform.SetParent(go.transform, false);
                    car.AddComponent<MeshFilter>().sharedMesh = Arch.Railwork.TrainCar(mono, seed, c);
                    var mr = car.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = Arch.ArchKit.Material;
                    tr.cars[c] = car.transform;
                }
                tr.speed = mono ? 12f : 15f;
                tr.Init(Hash01(top[0].x, top[0].z, 43));
                trains++;
            }
            Debug.Log($"[Rail] {lines.Count} lines, {trains} trains");
        }

        /// <summary>Joins polylines of the same kind whose ends meet (within 2.5 m), so trains run the whole line.</summary>
        static List<(bool, Vector3[])> JoinLines(List<(bool monorail, Vector3[] pts)> raw)
        {
            var open = new List<(bool monorail, List<Vector3> pts)>();
            foreach (var (m, p) in raw) open.Add((m, new List<Vector3>(p)));
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < open.Count && !merged; i++)
                    for (int j = i + 1; j < open.Count && !merged; j++)
                    {
                        if (open[i].monorail != open[j].monorail) continue;
                        var a = open[i].pts; var b = open[j].pts;
                        bool Near(Vector3 x, Vector3 y) => (new Vector2(x.x - y.x, x.z - y.z)).sqrMagnitude < 6.25f;
                        List<Vector3> joined = null;
                        if (Near(a[a.Count - 1], b[0])) { joined = new List<Vector3>(a); joined.AddRange(b.GetRange(1, b.Count - 1)); }
                        else if (Near(b[b.Count - 1], a[0])) { joined = new List<Vector3>(b); joined.AddRange(a.GetRange(1, a.Count - 1)); }
                        else if (Near(a[a.Count - 1], b[b.Count - 1])) { var rb = new List<Vector3>(b); rb.Reverse(); joined = new List<Vector3>(a); joined.AddRange(rb.GetRange(1, rb.Count - 1)); }
                        else if (Near(a[0], b[0])) { var ra = new List<Vector3>(a); ra.Reverse(); joined = ra; joined.AddRange(b.GetRange(1, b.Count - 1)); }
                        if (joined == null) continue;
                        open[i] = (open[i].monorail, joined);
                        open.RemoveAt(j);
                        merged = true;
                    }
            }
            var outp = new List<(bool, Vector3[])>();
            foreach (var (m, p) in open) outp.Add((m, p.ToArray()));
            return outp;
        }
    }
}
