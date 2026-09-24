using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Puts a humanoid's hips on a car seat. The sitting clip lowers the hips by a fixed amount, but
    /// every character (and every car) is a different height, so after the Animator has posed the
    /// body each frame this lifts / lowers the model until the hip bone sits on the seat marker.
    /// weight blends it in and out (0 = off) while the character is getting in or out, so they
    /// climb up onto a Hilux seat and settle down into a Kancil one.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class SeatFit : MonoBehaviour
    {
        public Transform seat;          // hip-height marker (Seat_Driver)
        [Range(0, 1)] public float weight;

        Transform _model, _hips;
        Vector3 _baseLocal;
        float _offset;

        void Awake()
        {
            var a = GetComponentInChildren<Animator>();
            if (a && a.isHuman) _hips = a.GetBoneTransform(HumanBodyBones.Hips);
            _model = a ? a.transform : null;          // the posed model (moved up/down as a whole)
            if (_model) _baseLocal = _model.localPosition;
        }

        void LateUpdate()
        {
            if (!_model || !_hips) return;
            float want = 0f;
            if (seat && weight > 0.001f)
            {
                // hip height without last frame's correction, then how far it is from the cushion
                float up = Vector3.Dot(_hips.position - transform.position, transform.up) - _offset;
                float target = Vector3.Dot(seat.position - transform.position, transform.up) + 0.02f;
                want = (target - up) * Mathf.Clamp01(weight);
            }
            _offset = want;
            _model.localPosition = _baseLocal + _model.parent.InverseTransformDirection(transform.up) * want;
        }

        public void Clear()
        {
            weight = 0f;
            seat = null;
            _offset = 0f;
            if (_model) _model.localPosition = _baseLocal;
        }
    }
}
