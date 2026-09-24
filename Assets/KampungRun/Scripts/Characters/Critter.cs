using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Ayam and kucing: potter about near home, peck/sit, and scatter squawking when the
    /// player or a car comes close. Kick one and it flaps off (no heat - it's only a chicken).
    /// </summary>
    public class Critter : MonoBehaviour
    {
        public float range = 7f;
        public float speed = 1.2f;
        public bool isChicken = true;
        Vector3 _home, _target;
        float _wait, _panic, _bob;

        void Start()
        {
            _home = transform.position;
            Pick();
        }

        void Pick()
        {
            var r = Random.insideUnitCircle * range;
            _target = _home + new Vector3(r.x, 0, r.y);
            _wait = Random.Range(0.5f, 3f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var p = PlayerController.I;
            if (p != null)
            {
                float d = Vector3.Distance(p.Focus, transform.position);
                if (d < (p.Driving ? 9f : 3f) && _panic <= 0)
                {
                    _panic = 1.5f;
                    var away = transform.position - p.Focus; away.y = 0;
                    _target = transform.position + away.normalized * 6f;
                    if (isChicken) Fx.Word(transform.position + Vector3.up, "KOKOK!");
                }
            }
            _panic -= dt;
            float spd = _panic > 0 ? speed * 4f : speed;
            if (_wait > 0 && _panic <= 0)
            {
                _wait -= dt;
                // pecking / tail flick
                _bob += dt * (isChicken ? 9f : 2f);
                transform.localRotation = Quaternion.Euler(isChicken ? Mathf.Max(0, Mathf.Sin(_bob)) * 35f : 0, transform.eulerAngles.y, 0);
                return;
            }
            var to = _target - transform.position; to.y = 0;
            if (to.magnitude < 0.3f) { Pick(); return; }
            var step = to.normalized * spd * dt;
            var pos = transform.position + step;
            if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle), QueryTriggerInteraction.Ignore))
            {
                if (hit.point.y > transform.position.y + 0.5f) { Pick(); return; }
                pos.y = hit.point.y;
            }
            transform.position = pos;
            // little hop while walking
            float hop = Mathf.Abs(Mathf.Sin(Time.time * (_panic > 0 ? 20f : 10f))) * (isChicken ? 0.06f : 0.02f);
            transform.GetChild(0).localPosition = Vector3.up * hop;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), dt * 10f);
        }
    }
}
