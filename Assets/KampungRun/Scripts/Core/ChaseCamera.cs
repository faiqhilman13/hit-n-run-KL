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

        float _dist;
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
                // follow velocity heading when moving forward, else the car's nose
                float heading = target.eulerAngles.y;
                if (spd > 3f && Vector3.Dot(v, target.forward) > 0) heading = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                if (_lookIdle > 0.8f)
                {
                    yaw = Mathf.LerpAngle(yaw, heading, dt * Mathf.Lerp(1.5f, 4f, spd / 25f));
                    pitch = Mathf.Lerp(pitch, 12f, dt * 2f);
                }
                _dist = Mathf.Lerp(_dist, distance + spd * 0.12f, dt * 2f);
            }
            else
            {
                _dist = Mathf.Lerp(_dist, distance, dt * 3f);
            }

            _focus = Vector3.Lerp(_focus, target.position + Vector3.up * height, 1f - Mathf.Exp(-dt * 12f));
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
        public const int World = 0;
    }
}
