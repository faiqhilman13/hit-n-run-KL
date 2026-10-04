using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Hit &amp; Run style camera: free orbit on foot, swings in behind the car when driving
    /// (with a manual look override that springs back), pulls out with speed.
    /// </summary>
    public class ChaseCamera : MonoBehaviour
    {
        public Transform target;
        public Rigidbody targetBody;   // set while driving
        public float distance = 6.5f;
        public float height = 1.6f;
        public float yaw, pitch = 14f;

        // Driving, measured against Hit & Run footage: the camera hardly drops back with speed (the car's back is
        // about 26% of the screen height parked, 22% flat out) but tips down the road, so at speed the horizon
        // sits about 78% up the screen and the car stays in the lower third.
        [Header("Driving")]
        public float drivePitch = 13f;          // at a standstill
        public float drivePitchFast = 21f;      // flat out
        public float drivePullBack = 0f;        // extra distance at full speed, as a fraction of `distance`
        public float driveRise = 0.5f;          // the aim point rises this much at full speed (m)
        public float driveFullSpeed = 28f;      // m/s

        // On foot, measured the same way: Homer stands about a third of the screen tall with his feet near
        // the bottom and the horizon a little above the middle - a camera ~3.8 m behind and ~2.2 m up,
        // looking almost level. (Distance/height scale with the character; PlayerController applies them.)
        public const float FootDistance = 3.9f, FootHeight = 1.55f, FootPitch = 9f;

        float _dist, _rise, _footPull, _fovKick, _baseFov;
        Camera _cam;
        float _lookIdle;
        Vector3 _focus;
        float _shake;
        LayerMask _mask;

        public static ChaseCamera I { get; private set; }

        void Awake()
        {
            I = this;
            _mask = ~(LayerMask.GetMask("Ignore Raycast") | (1 << Layers.Vehicle) | (1 << Layers.Character) | (1 << Layers.Pickup));
        }

        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        public void SnapBehind()
        {
            if (!target) return;
            yaw = target.eulerAngles.y;
            _focus = target.position + Vector3.up * height;
            _dist = distance;
            Place(1f);
        }

        void LateUpdate()
        {
            if (!target) return;
            float dt = Time.deltaTime;
            var look = GameInput.Look;
            bool driving = targetBody != null;

            if (look.sqrMagnitude > 0.0001f) _lookIdle = 0f; else _lookIdle += dt;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -5f, 60f);

            if (driving)
            {
                var v = targetBody.linearVelocity;
                v.y = 0;
                float spd = v.magnitude;
                float s = Mathf.Clamp01(spd / driveFullSpeed);
                // follow velocity heading when moving forward, else the car's nose
                float heading = target.eulerAngles.y;
                if (spd > 3f && Vector3.Dot(v, target.forward) > 0) heading = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                if (_lookIdle > 0.8f)
                {
                    yaw = Mathf.LerpAngle(yaw, heading, dt * Mathf.Lerp(1.5f, 4f, spd / 25f));
                    pitch = Mathf.Lerp(pitch, Mathf.Lerp(drivePitch, drivePitchFast, s), dt * 2f);
                }
                // the drop-back lags the speed, so a launch pulls the car away from the camera
                _dist = Mathf.Lerp(_dist, distance * (1f + drivePullBack * s), dt * 2.2f);
                _rise = Mathf.Lerp(_rise, driveRise * s, dt * 2f);
            }
            else
            {
                _dist = Mathf.Lerp(_dist, distance + _footPull, dt * 3f);
                _rise = Mathf.Lerp(_rise, 0f, dt * 3f);
            }

            // on foot behind the player: the platformer camera
            var player = PlayerController.I;
            bool onFoot = !driving && player != null && target == player.transform && !player.Driving;
            if (onFoot)
            {
                var mv = player.MoveDir;
                if (_lookIdle > 1f && mv != Vector3.zero)
                {
                    // Lakitu-style: leave the stick alone and it drifts round behind the way you're running
                    // (not when you run at the camera - then it lets you come to it)
                    float heading = Mathf.Atan2(mv.x, mv.z) * Mathf.Rad2Deg;
                    if (Mathf.Abs(Mathf.DeltaAngle(yaw, heading)) < 115f)
                        yaw = Mathf.LerpAngle(yaw, heading, dt * 1.3f * Mathf.Clamp01(player.Speed / 4f));
                }
                if (_lookIdle > 1.5f) pitch = Mathf.Lerp(pitch, FootPitch, dt * 1.5f);
                // sprinting: a wider lens and a step back, so the speed reads
                _fovKick = Mathf.Lerp(_fovKick, player.Sprinting ? 7f : 0f, dt * 4f);
                _footPull = Mathf.Lerp(_footPull, player.Sprinting ? 0.6f : 0f, dt * 3f);
            }
            else
            {
                _fovKick = Mathf.Lerp(_fovKick, 0f, dt * 4f);
                _footPull = Mathf.Lerp(_footPull, 0f, dt * 3f);
            }
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam != null)
            {
                if (_baseFov <= 0f) _baseFov = _cam.fieldOfView;
                if (_fovKick > 0.01f) _cam.fieldOfView = _baseFov + _fovKick;
                else if (_cam.fieldOfView != _baseFov && Mathf.Abs(_cam.fieldOfView - _baseFov) < 8f) _cam.fieldOfView = _baseFov;
            }

            // the aim point stays tight on a car (so it sits low in the frame at speed instead of drifting up)
            var want = target.position + Vector3.up * (height + _rise);
            float k = 1f - Mathf.Exp(-dt * (driving ? 30f : 12f));
            if (onFoot && !player.Grounded)
            {
                // in the air it follows you sideways at once but up and down lazily, so a jump lifts you up
                // the screen (and a long fall is still followed)
                float ky = 1f - Mathf.Exp(-dt * (want.y < _focus.y - 1.2f ? 9f : 2.5f));
                _focus = new Vector3(Mathf.Lerp(_focus.x, want.x, k), Mathf.Lerp(_focus.y, want.y, ky), Mathf.Lerp(_focus.z, want.z, k));
                // ...but never so far behind that you leave the frame (bouncing on a trampoline, say)
                _focus.y = Mathf.Clamp(_focus.y, want.y - 1.6f, want.y + 0.8f);
            }
            else _focus = Vector3.Lerp(_focus, want, k);
            Place(dt);
        }

        void Place(float dt)
        {
            var rot = Quaternion.Euler(pitch, yaw, 0);
            var dir = rot * Vector3.back;
            float d = _dist;
            if (Physics.SphereCast(_focus, 0.35f, dir, out var hit, _dist, _mask, QueryTriggerInteraction.Ignore))
                d = Mathf.Max(1.2f, hit.distance - 0.1f);
            transform.position = _focus + dir * d;
            transform.rotation = Quaternion.LookRotation(_focus - transform.position + Vector3.up * 0.3f);
            if (_shake > 0)
            {
                transform.position += Random.insideUnitSphere * _shake * 0.3f;
                _shake = Mathf.MoveTowards(_shake, 0, dt * 2.5f);
            }
        }
    }

    public static class Layers
    {
        // Builtin free user layers; names assigned by the project builder.
        public const int Vehicle = 8;
        public const int Character = 9;
        public const int Pickup = 10;
        public const int Detail = 11;       // street clutter: drawn only near the camera
        public const int World = 0;
    }
}
