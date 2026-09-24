using UnityEngine;

namespace KampungRun
{
    /// <summary>Moves back and forth between two points (the monorail train).</summary>
    public class Shuttle : MonoBehaviour
    {
        public Vector3 a, b;
        public float speed = 8f;
        public float pause = 3f;
        float _t, _wait;
        int _dir = 1;

        void Update()
        {
            if (_wait > 0) { _wait -= Time.deltaTime; return; }
            float len = Mathf.Max(1f, Vector3.Distance(a, b));
            _t += _dir * speed * Time.deltaTime / len;
            if (_t >= 1f || _t <= 0f)
            {
                _t = Mathf.Clamp01(_t);
                _dir = -_dir;
                _wait = pause;
            }
            // ease in/out at the stations
            float e = Mathf.SmoothStep(0f, 1f, _t);
            transform.position = Vector3.Lerp(a, b, e);
        }
    }
}
