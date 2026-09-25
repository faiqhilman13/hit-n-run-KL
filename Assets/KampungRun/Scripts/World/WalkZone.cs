using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// A pavement loop people stroll round: the sidewalk ring of a block (a rectangle on the grid
    /// lots, any shape in the real-KL patches). Positions along it are a distance s round the ring.
    /// </summary>
    public class WalkZone
    {
        public readonly Vector2[] pts;
        readonly float[] _cum;              // distance round the ring at each vertex
        public readonly float perimeter;
        public readonly Rect bounds;
        public readonly Vector2 center;
        public bool kampung;
        public float busy = 1f;        // crowd multiplier: pedestrian streets and markets draw more people

        public WalkZone(IList<Vector2> ring)
        {
            pts = new Vector2[ring.Count];
            for (int i = 0; i < ring.Count; i++) pts[i] = ring[i];
            _cum = new float[pts.Length + 1];
            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < pts.Length; i++)
            {
                _cum[i + 1] = _cum[i] + Vector2.Distance(pts[i], pts[(i + 1) % pts.Length]);
                xMin = Mathf.Min(xMin, pts[i].x); xMax = Mathf.Max(xMax, pts[i].x);
                yMin = Mathf.Min(yMin, pts[i].y); yMax = Mathf.Max(yMax, pts[i].y);
                sum += pts[i];
            }
            perimeter = Mathf.Max(0.01f, _cum[pts.Length]);
            bounds = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            center = sum / Mathf.Max(1, pts.Length);
        }

        /// <summary>A lot's sidewalk ring: the rect inset by the pavement's middle.</summary>
        public static WalkZone FromRect(Rect r, float inset = 1.5f) => new WalkZone(new[]
        {
            new Vector2(r.xMin + inset, r.yMin + inset), new Vector2(r.xMax - inset, r.yMin + inset),
            new Vector2(r.xMax - inset, r.yMax - inset), new Vector2(r.xMin + inset, r.yMax - inset),
        });

        public float Wrap(float s) => Mathf.Repeat(s, perimeter);

        /// <summary>The point s along the ring (y = pavement height).</summary>
        public Vector3 PointAt(float s, float y = 0.2f)
        {
            s = Wrap(s);
            int i = Segment(s);
            float len = _cum[i + 1] - _cum[i];
            float t = len > 1e-4f ? (s - _cum[i]) / len : 0f;
            var p = Vector2.Lerp(pts[i], pts[(i + 1) % pts.Length], t);
            return new Vector3(p.x, y, p.y);
        }

        int Segment(float s)
        {
            int lo = 0, hi = pts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_cum[mid] <= s) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>Distance round the ring of the point nearest p.</summary>
        public float Closest(Vector3 p)
        {
            var q = new Vector2(p.x, p.z);
            float best = float.MaxValue, bestS = 0f;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = (a + ab * t - q).sqrMagnitude;
                if (d < best) { best = d; bestS = _cum[i] + (_cum[i + 1] - _cum[i]) * t; }
            }
            return bestS;
        }

        /// <summary>The next corner strictly past s going round in direction dir (+1 / -1), as a distance round the ring.</summary>
        public float NextCorner(float s, int dir)
        {
            s = Wrap(s);
            int i = Segment(s);
            if (dir > 0) return _cum[i + 1] - s < 0.05f ? _cum[(i + 1) % pts.Length + 1] : _cum[i + 1];
            return s - _cum[i] < 0.05f ? (i == 0 ? _cum[pts.Length - 1] : _cum[i - 1]) : _cum[i];
        }

        /// <summary>How far it is from a to b going round in direction dir.</summary>
        public float Along(float a, float b, int dir) => dir > 0 ? Wrap(b - a) : Wrap(a - b);

        public float Random() => UnityEngine.Random.value * perimeter;

        public bool Contains(Vector3 p)
        {
            float x = p.x, y = p.z;
            if (!bounds.Contains(new Vector2(x, y))) return false;
            bool inside = false;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                if ((pts[i].y > y) != (pts[j].y > y) && x < (pts[j].x - pts[i].x) * (y - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x)
                    inside = !inside;
            return inside;
        }
    }
}
