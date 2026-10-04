using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// A trampoline: land on a shop awning or a market canopy and it fires you back up, Mario-style, with
    /// a double jump still in hand - the way up to the rooftops (PlayerController reads `power` when it
    /// lands on one). A boing, a ring of dust, and the canvas squashes and wobbles if it has a renderer of
    /// its own (merged scenery can't move, so the box usually sits apart from the awning it covers).
    /// </summary>
    public class Bouncy : MonoBehaviour
    {
        public float power = 11f;        // launch speed (m/s)
        public Transform visual;         // squashes when bounced (optional)
        Vector3 _rest;
        float _t = -1f;

        void Awake() => enabled = false; // only ticks while wobbling

        public void Boing()
        {
            var pc = PlayerController.I;
            var at = pc != null ? pc.transform.position : transform.position;
            ProcAudio.Play(ProcAudio.Boing, at, 0.75f, Random.Range(0.92f, 1.1f));
            Fx.Ring(at + Vector3.up * 0.05f, 3.5f, 9, 0.32f);
            if (visual == null) return;
            if (_t < 0f) _rest = visual.localScale;
            _t = 0f;
            enabled = true;
        }

        void Update()
        {
            if (visual == null || _t < 0f) { enabled = false; return; }
            _t += Time.deltaTime;
            float w = Mathf.Exp(-_t * 6f) * Mathf.Cos(_t * 28f) * 0.22f;
            visual.localScale = new Vector3(_rest.x * (1f + w * 0.35f), _rest.y * (1f - w), _rest.z * (1f + w * 0.35f));
            if (_t > 1f) { visual.localScale = _rest; _t = -1f; enabled = false; }
        }
    }
}
