using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// KL's elevated stations on the real lines (Resources/KLMap/stations.json, from Tools/klmap/stations.py): each
    /// is snapped onto its line, gets platforms either side of the tracks under a barrel-vault roof with its name on
    /// the fascia, and a stair up from the nearest pavement with a covered linkway across to a platform. All of it
    /// can be walked: you can go up and wait for the train.
    /// </summary>
    public partial class CityBuilder
    {
        [System.Serializable] class StationJson { public string name, code, kind, patch; public float[] centre; }
        [System.Serializable] class StationList { public StationJson[] items; }

        readonly List<(bool monorail, Vector3[] top)> _railTops = new List<(bool, Vector3[])>();
        readonly List<Vector2[]> _pavements = new List<Vector2[]>();
        public readonly List<(string name, Vector3 platform)> stations = new List<(string, Vector3)>();

        void BuildStations()
        {
            var ta = Resources.Load<TextAsset>("KLMap/stations");
            if (ta == null) return;
            var list = JsonUtility.FromJson<StationList>("{\"items\":" + ta.text + "}");
            int built = 0;
            foreach (var st in list.items)
            {
                bool mono = st.kind == "monorail";
                var c = new Vector3(st.centre[0], 0f, st.centre[1]);
                if (!Nearest(mono, c, 45f, -1, out var q, out var tan, out int line)) continue;
                var across = Vector3.Cross(Vector3.up, tan);
                // the other track: the nearest point on another line of the same kind, beside this one
                float span = 0f;
                var mid = q;
                if (Twin(mono, q, tan, out var q2))
                {
                    span = Mathf.Abs(Vector3.Dot(q2 - q, across));
                    mid = (q + q2) * 0.5f;
                }
                float half = span * 0.5f;
                var spec = new Arch.StationSpec
                {
                    centre = mid, along = tan, monorail = mono,
                    platform = mono ? mid.y + 0.3f : mid.y + 1.0f,
                    inner = mono ? half + 1.5f : half + 1.75f,
                    width = mono ? 3.6f : 4.2f,
                    length = mono ? 42f : 58f,
                    seed = Mathf.Abs(st.name.GetHashCode()) % 997,
                };
                _arch.Add(spec);
                int links = 0;
                foreach (int side in new[] { -1, 1 })
                    if (StationLink(spec, side)) links++;
                StationSolids(spec);
                StationName(spec, st.name, st.code);
                stations.Add((st.name, mid + Vector3.up * (spec.platform - mid.y)));
                built++;
                Debug.Log($"[Rail] station {st.code} {st.name}: span {span:F1} m, {links} stairs");
            }
            Debug.Log($"[Rail] {built} stations");
        }

        /// <summary>The nearest point on an elevated line of a kind (running surface), its direction, its index.</summary>
        bool Nearest(bool mono, Vector3 p, float within, int skip, out Vector3 at, out Vector3 tangent, out int line)
        {
            at = default; tangent = Vector3.forward; line = -1;
            float best = within * within;
            for (int l = 0; l < _railTops.Count; l++)
            {
                if (l == skip || _railTops[l].monorail != mono) continue;
                var pts = _railTops[l].top;
                for (int i = 0; i + 1 < pts.Length; i++)
                {
                    Vector3 a = pts[i], b = pts[i + 1];
                    var ab = new Vector2(b.x - a.x, b.z - a.z);
                    var ap = new Vector2(p.x - a.x, p.z - a.z);
                    float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    float d = (ap - ab * t).sqrMagnitude;
                    if (d < best) { best = d; at = Vector3.Lerp(a, b, t); tangent = new Vector3(ab.x, 0f, ab.y).normalized; line = l; }
                }
            }
            return line >= 0;
        }

        /// <summary>The other track beside q (on any line of the kind, the same one too when the two directions were
        /// joined into one): a point 1.2 to 9 m across from q and within 3 m of it along.</summary>
        bool Twin(bool mono, Vector3 q, Vector3 tan, out Vector3 at)
        {
            at = default;
            var across = Vector3.Cross(Vector3.up, tan);
            float best = float.MaxValue;
            foreach (var (m, pts) in _railTops)
            {
                if (m != mono) continue;
                for (int i = 0; i + 1 < pts.Length; i++)
                {
                    Vector3 a = pts[i], b = pts[i + 1];
                    var ab = b - a; ab.y = 0f;
                    if (Mathf.Abs(Vector3.Dot(ab.normalized, tan)) < 0.8f) continue;          // parallel tracks only
                    // where this segment crosses the line through q across the tracks
                    float da = Vector3.Dot(a - q, tan), db = Vector3.Dot(b - q, tan);
                    if (da * db > 0f && Mathf.Min(Mathf.Abs(da), Mathf.Abs(db)) > 3f) continue;
                    float t = Mathf.Abs(db - da) < 1e-4f ? 0f : Mathf.Clamp01(-da / (db - da));
                    var p = Vector3.Lerp(a, b, t);
                    float off = Mathf.Abs(Vector3.Dot(p - q, across));
                    if (off < 1.2f || off > 9f || off >= best) continue;
                    best = off; at = p;
                }
            }
            return best < float.MaxValue;
        }

        void StationSolids(Arch.StationSpec s)
        {
            var a = s.along.normalized;
            var x = s.Across;
            var rot = Quaternion.LookRotation(a, Vector3.up);
            var g = new Vector3(s.centre.x, 0f, s.centre.z);
            foreach (int side in new[] { -1, 1 })
            {
                float cx = side * (s.inner + s.width * 0.5f);
                // the slab (walkable top at the platform floor) and the outer wall
                _ground.ColliderOnly(g + x * cx + Vector3.up * (s.platform - Arch.Station.Slab * 0.5f), new Vector3(s.width, Arch.Station.Slab, s.length), rot);
                // (left open where the linkway comes in)
                foreach (var (z0, z1) in s.WallRuns(side))
                    _ground.ColliderOnly(g + x * (side * (s.Outer + 0.1f)) + a * ((z0 + z1) * 0.5f) + Vector3.up * (s.platform + 0.58f), new Vector3(0.3f, 1.15f, z1 - z0), rot);
                // the columns
                int cols = Mathf.Max(2, Mathf.RoundToInt(s.length / 11f));
                for (int c = 0; c < cols; c++)
                {
                    float z = Mathf.Lerp(-s.length * 0.5f + 2f, s.length * 0.5f - 2f, c / (float)(cols - 1));
                    float h = s.platform - Arch.Station.Slab;
                    _ground.ColliderOnly(g + x * cx + a * z + Vector3.up * h * 0.5f, new Vector3(0.8f, h, 0.8f), rot);
                }
            }
        }

        /// <summary>The station's name on each side's fascia, in big white letters, with its line code.</summary>
        void StationName(Arch.StationSpec s, string name, string code)
        {
            var x = s.Across;
            foreach (int side in new[] { -1, 1 })
            {
                var host = new GameObject("StationName_" + name);
                host.transform.SetParent(_props, false);
                var face = x * side;
                host.transform.position = new Vector3(s.centre.x, 0f, s.centre.z) + face * (s.Outer + 0.4f) + Vector3.up * (s.Eaves - 0.45f);
                // LetterSign turns its text 180 degrees: the host faces into the station so the letters face out
                host.transform.rotation = Quaternion.LookRotation(-face, Vector3.up);
                LetterSign(host, "STESEN " + name.ToUpperInvariant() + (string.IsNullOrEmpty(code) ? "" : "   " + code), Vector3.zero, 0.075f, Color.white);
            }
        }

        /// <summary>A stair up from the nearest pavement on one side, and a linkway across to the platform.</summary>
        bool StationLink(Arch.StationSpec s, int side)
        {
            var a = s.along.normalized;
            var x = s.Across * side;
            var g = new Vector3(s.centre.x, 0f, s.centre.z);
            var edgeMid = g + x * s.Outer;
            // the pavement point nearest the platform's outer edge, out on this side, 6 to 26 m away
            float best = float.MaxValue;
            Vector3 foot = default, tan = default;
            foreach (var ring in _pavements)
                for (int i = 0; i < ring.Length; i++)
                {
                    var p = new Vector3(ring[i].x, 0f, ring[i].y);
                    float out_ = Vector3.Dot(p - g, x) - s.Outer, alongOff = Vector3.Dot(p - g, a);
                    if (out_ < 5f || out_ > 26f || Mathf.Abs(alongOff) > s.length * 0.5f + 10f) continue;
                    float d = out_ + Mathf.Abs(alongOff) * 0.5f;
                    if (d >= best) continue;
                    var n = new Vector3(ring[(i + 1) % ring.Length].x, 0f, ring[(i + 1) % ring.Length].y) - p;
                    if (n.sqrMagnitude < 0.01f) continue;
                    best = d; foot = p; tan = n.normalized;
                }
            if (best == float.MaxValue) return false;
            // the flight runs along the pavement, rising towards the station's middle
            if (Vector3.Dot(tan, g - foot) < 0f) tan = -tan;
            float rise = s.platform - G, run = rise / 0.62f;
            var bottom = foot - tan * (run * 0.5f) + Vector3.up * G;
            var top = foot + tan * (run * 0.5f) + Vector3.up * s.platform;
            // the linkway meets the platform's outer edge square on, inside the platform's length
            float az = Mathf.Clamp(Vector3.Dot(top - g, a), -s.length * 0.5f + 2f, s.length * 0.5f - 2f);
            s.gaps[side > 0 ? 1 : 0] = az;
            var end = g + a * az + x * s.Outer + Vector3.up * s.platform;
            var lk = new Vector3(end.x - top.x, 0f, end.z - top.z);
            if (lk.magnitude > 30f) return false;
            _arch.Add(new Arch.StationLinkSpec { bottom = bottom, top = top, end = end });
            // solids: the flight (a ramp), the linkway's slab, parapets both sides
            var fl = top - bottom;
            var flat = new Vector3(fl.x, 0f, fl.z);
            var rot = Quaternion.LookRotation(fl.normalized, Vector3.up);
            _ground.ColliderOnly((bottom + top) * 0.5f - (rot * Vector3.up) * 0.2f, new Vector3(Arch.Station.StairWidth, 0.4f, fl.magnitude), rot);
            var sideV = Vector3.Cross(Vector3.up, flat.normalized);
            foreach (int k in new[] { -1, 1 })
                _ground.ColliderOnly((bottom + top) * 0.5f + sideV * k * (Arch.Station.StairWidth * 0.5f + 0.1f) + Vector3.up * 0.5f, new Vector3(0.2f, 1.2f, fl.magnitude), rot);
            if (lk.magnitude > 0.3f)
            {
                var ld = lk.normalized;
                var lrot = Quaternion.LookRotation(ld, Vector3.up);
                float L = lk.magnitude + 1.2f;
                var lc = top - ld * 1.2f + ld * (L * 0.5f);
                _ground.ColliderOnly(new Vector3(lc.x, s.platform - Arch.Station.Slab * 0.5f, lc.z), new Vector3(Arch.Station.LinkWidth, Arch.Station.Slab, L), lrot);
                var ls = Vector3.Cross(Vector3.up, ld);
                foreach (int k in new[] { -1, 1 })
                    _ground.ColliderOnly(new Vector3(lc.x, s.platform + 0.55f, lc.z) + ls * k * (Arch.Station.LinkWidth * 0.5f), new Vector3(0.15f, 1.1f, L), lrot);
            }
            return true;
        }
    }
}
