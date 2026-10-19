using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// A train running up and down an elevated line (a polyline on the running surface): it eases out of the
    /// end of the line, cruises, eases into the other end, waits, and comes back. The two cars follow the
    /// curve separately so they bend round corners.
    /// </summary>
    public class RailTrain : MonoBehaviour
    {
        public Vector3[] pts;
        public float speed = 13f, pause = 7f;
        public Transform[] cars;          // front first, each with its pivot at its middle
        public float carSpacing = 12.9f;
        float[] _acc;
        float _len, _s, _wait;
        int _dir = 1;

        public void Init(float startFraction)
        {
            _acc = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) _acc[i] = _acc[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            _len = _acc[pts.Length - 1];
            _s = Mathf.Lerp(Margin, _len - Margin, startFraction);
            Place();
        }

        float Margin => carSpacing * 0.5f * cars.Length + 1f;

        void Update()
        {
            if (_acc == null) return;
            if (_wait > 0f) { _wait -= Time.deltaTime; return; }
            float lo = Margin, hi = _len - Margin;
            if (hi <= lo) return;
            // ease near the ends (a station stop's braking), never quite stopping before the end
            float toEnd = _dir > 0 ? hi - _s : _s - lo;
            float v = speed * Mathf.Clamp(toEnd / 40f, 0.12f, 1f);
            _s += _dir * v * Time.deltaTime;
            if (_s >= hi || _s <= lo) { _s = Mathf.Clamp(_s, lo, hi); _dir = -_dir; _wait = pause; }
            Place();
        }

        void Place()
        {
            for (int c = 0; c < cars.Length; c++)
            {
                // the leading car is the one at the front in the direction of travel
                float off = (c - (cars.Length - 1) * 0.5f) * carSpacing;
                float s = _s + off;
                var p = At(s, out var t);
                cars[c].SetPositionAndRotation(p, Quaternion.LookRotation(t, Vector3.up));
            }
        }

        Vector3 At(float s, out Vector3 tangent)
        {
            s = Mathf.Clamp(s, 0f, _len);
            int i = 1;
            while (i < pts.Length - 1 && _acc[i] < s) i++;
            var a = pts[i - 1]; var b = pts[i];
            float seg = Mathf.Max(1e-4f, _acc[i] - _acc[i - 1]);
            tangent = b - a;
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.forward;
            return Vector3.Lerp(a, b, (s - _acc[i - 1]) / seg);
        }
    }
}
