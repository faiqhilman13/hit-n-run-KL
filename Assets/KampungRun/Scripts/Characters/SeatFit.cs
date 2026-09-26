using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Puts a humanoid's hips on a car seat and its hands on the steering wheel. The sitting clip lowers the
    /// hips by a fixed amount, but every character (and every car) is a different height, so after the
    /// Animator has posed the body each frame this lifts / lowers the model until the hip bone sits on the
    /// seat marker. weight blends it in and out (0 = off) while the character is getting in or out, so they
    /// climb up onto a Hilux seat and settle down into a Kancil one.
    /// The clip's arms hold a wheel-shaped nothing in front of the chest, which lands in (or through) the real
    /// wheel wherever it happens to be, so once seated a two-bone IK puts each palm on the rim (GripClock) and
    /// the hands turn with the wheel.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class SeatFit : MonoBehaviour
    {
        public Transform seat;          // hip-height marker (Seat_Driver)
        [Range(0, 1)] public float weight;

        /// <summary>Hands on the wheel (off = the sitting clip's own arms).</summary>
        public static bool GripWheel = true;

        /// <summary>Where the left hand holds the rim, as a clock position (the right hand mirrors it).</summary>
        public static float GripClock = 9f;

        Transform _model, _hips;
        Vector3 _baseLocal;
        float _offset;

        Transform _upperL, _foreL, _handL, _upperR, _foreR, _handR;
        float _palm, _grip;
        Transform _gripSeat;
        VehicleVisuals _car;

        void Awake() => Bind();

        /// <summary>Find the posed model and its hips (again, after the character has been swapped).</summary>
        void Bind()
        {
            var a = GetComponentInChildren<Animator>();
            bool human = a && a.isHuman;
            _hips = human ? a.GetBoneTransform(HumanBodyBones.Hips) : null;
            _model = a ? a.transform : null;          // the posed model (moved up/down as a whole)
            if (_model) _baseLocal = _model.localPosition;
            _offset = 0f;
            _upperL = human ? a.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null;
            _foreL = human ? a.GetBoneTransform(HumanBodyBones.LeftLowerArm) : null;
            _handL = human ? a.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            _upperR = human ? a.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
            _foreR = human ? a.GetBoneTransform(HumanBodyBones.RightLowerArm) : null;
            _handR = human ? a.GetBoneTransform(HumanBodyBones.RightHand) : null;
            // wrist to knuckles (bone lengths don't depend on the pose)
            var knuckle = human ? a.GetBoneTransform(HumanBodyBones.RightMiddleProximal) : null;
            _palm = knuckle && _handR ? Vector3.Distance(knuckle.position, _handR.position)
                  : _foreR && _handR ? Vector3.Distance(_foreR.position, _handR.position) * 0.35f : 0.08f;
        }

        void LateUpdate()
        {
            // the player swaps bodies between levels (and costumes): follow the new one
            if (!_model || !_hips) Bind();
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

            // hands on the wheel once sat down; let go at once when getting out
            bool seated = GripWheel && seat && weight > 0.98f;
            _grip = Mathf.MoveTowards(_grip, seated ? 1f : 0f, Time.deltaTime * (seated ? 3f : 10f));
            if (_grip <= 0.001f || !seat) return;
            if (_gripSeat != seat) { _gripSeat = seat; _car = seat.GetComponentInParent<VehicleVisuals>(); }
            if (!_car) return;
            Grip(_upperL, _foreL, _handL, GripClock, -1f);
            Grip(_upperR, _foreR, _handR, 12f - GripClock, 1f);
        }

        void Grip(Transform upper, Transform fore, Transform hand, float clock, float side)
        {
            // the hands turn the wheel 50 degrees each way, then slip round it (further, the top hand covers the face)
            if (!upper || !fore || !hand || !_car.WheelGrip(clock, 50f, out var rim, out var toDriver, out var outward)) return;
            var shoulder = upper.position;
            // the palm on the rim: the wrist sits back down the reach by a palm's length, a touch outside the rim
            var reach = (rim - shoulder).normalized;
            var wrist = rim - reach * _palm * 0.9f + outward * 0.012f + toDriver * 0.01f;
            // elbows out to the side and down
            var car = _car.transform;
            var hint = shoulder + car.right * (side * 0.45f) - car.up * 0.5f;
            TwoBone(upper, fore, hand, wrist, hint, _grip);
        }

        /// <summary>Two-bone IK: turn a (upper arm) and b (forearm) so c (the hand) reaches target, the elbow bent toward hint.</summary>
        static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint, float w)
        {
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float lab = Vector3.Distance(pa, pb), lbc = Vector3.Distance(pb, pc);
            var d = target - pa;
            if (lab < 1e-4f || lbc < 1e-4f || d.sqrMagnitude < 1e-8f) return;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(lab - lbc) + 1e-3f, (lab + lbc) * 0.999f);
            var dn = d.normalized;
            // the elbow, by the law of cosines along the reach, pushed out toward the hint
            float x = (lab * lab - lbc * lbc + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, lab * lab - x * x));
            var pole = Vector3.ProjectOnPlane(hint - pa, dn);
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(pb - pa, dn);
            var elbow = pa + dn * x + pole.normalized * h;
            Quaternion ra = a.rotation, rb = b.rotation;
            a.rotation = Quaternion.FromToRotation(pb - pa, elbow - pa) * ra;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, pa + dn * dist - b.position) * b.rotation;
            if (w < 0.999f)
            {
                var sb = b.rotation;
                a.rotation = Quaternion.Slerp(ra, a.rotation, w);
                b.rotation = Quaternion.Slerp(rb, sb, w);
            }
        }

        public void Clear()
        {
            weight = 0f;
            seat = null;
            _offset = 0f;
            _grip = 0f;
            if (_model) _model.localPosition = _baseLocal;
        }
    }
}
