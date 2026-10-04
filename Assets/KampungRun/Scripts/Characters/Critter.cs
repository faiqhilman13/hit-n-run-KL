using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Ayam and kucing: potter about near home, peck/sit, cluck and mew to themselves, and scatter
    /// squawking when the player or a car comes close. Kick a chicken (or clip one with a car) and it
    /// tumbles off in a burst of feathers - no heat, it's only a chicken - but kampung chickens stick
    /// together: bully them for long enough and the whole flock comes for you (with apologies to Hyrule's
    /// cuccos). Cats are never caught: swing at one and it springs clear and lands on its feet.
    /// </summary>
    public class Critter : MonoBehaviour
    {
        public float range = 7f;
        public float speed = 1.2f;
        public bool isChicken = true;
        Vector3 _home, _target;
        float _wait, _panic, _bob;

        // kicked: a flapping, tumbling arc (or a cat's leap)
        bool _flying;
        Vector3 _flyVel;
        float _spin, _kickCooldown, _voiceT;
        float _peckT;
        bool _airborne;                 // up in the air mid-revenge

        // the flock's revenge, shared by every chicken
        static int _abuse, _pecks;
        static float _abuseUntil, _revengeUntil;
        static bool _floored;
        static bool Revenge => Time.time < _revengeUntil;

        static readonly Color White = new Color(0.97f, 0.95f, 0.9f);
        static readonly Color Brown = new Color(0.72f, 0.42f, 0.2f);
        const int Ground = ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle);

        void Awake()
        {
            // something for a kick to find and a car to clip: a small trigger that moves with the animal
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var box = new GameObject("Hitbox");
            box.layer = 2;                      // Ignore Raycast: never in the way of the world's own queries
            box.transform.SetParent(transform, false);
            var sc = box.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.38f;
            sc.center = new Vector3(0f, 0.3f, 0f);
        }

        void Start()
        {
            _home = transform.position;
            _voiceT = Random.Range(2f, 14f);
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
            _kickCooldown -= dt;
            var p = PlayerController.I;
            if (_flying) { Fly(dt); return; }
            if (isChicken && Revenge && p != null && !p.Driving && (p.transform.position - transform.position).sqrMagnitude < 50f * 50f)
            {
                _airborne = true;
                Attack(p, dt);
                return;
            }
            if (_airborne)
            {
                // the revenge is over (or you hid in a car): flutter back down to earth and wander home
                _airborne = false;
                _flying = true;
                _flyVel = Vector3.zero;
                return;
            }
            Voice(p, dt);
            if (p != null)
            {
                float d = Vector3.Distance(p.Focus, transform.position);
                if (d < (p.Driving ? 9f : 3f) && _panic <= 0)
                {
                    _panic = 1.5f;
                    var away = transform.position - p.Focus; away.y = 0;
                    _target = transform.position + away.normalized * 6f;
                    if (isChicken)
                    {
                        Fx.Word(transform.position + Vector3.up, "KOKOK!");
                        ProcAudio.PlayAt(ProcAudio.Cluck, transform.position, 0.35f, Random.Range(1.1f, 1.35f), 0.9f, 35f);
                    }
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
            if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, Ground, QueryTriggerInteraction.Ignore))
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

        /// <summary>Clucks and mews to themselves, now and then, when you're close enough to hear.</summary>
        void Voice(PlayerController p, float dt)
        {
            if ((_voiceT -= dt) > 0f) return;
            _voiceT = isChicken ? Random.Range(5f, 14f) : Random.Range(12f, 28f);
            if (p == null || (p.Focus - transform.position).sqrMagnitude > (isChicken ? 20f * 20f : 14f * 14f)) return;
            if (isChicken) ProcAudio.PlayAt(ProcAudio.Cluck, transform.position, 0.22f, Random.Range(0.85f, 1.2f), 0.95f, 30f);
            else ProcAudio.PlayAt(ProcAudio.Meow, transform.position, 0.2f, Random.Range(0.9f, 1.3f), 0.95f, 25f);
        }

        /// <summary>
        /// Kicked, punched or stamped near (force: jab ~6, kick ~13). A chicken tumbles off flapping in a
        /// cloud of feathers; a cat was never there - it springs away and lands on its feet.
        /// </summary>
        public void Kicked(Vector3 dir, float force)
        {
            if (_kickCooldown > 0f) return;
            _kickCooldown = 0.25f;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : -transform.forward;
            var at = transform.position + Vector3.up * 0.35f;
            _flying = true;
            _spin = 0f;
            transform.GetChild(0).localPosition = Vector3.zero;
            if (isChicken)
            {
                _flyVel = dir * Mathf.Clamp(2.5f + force * 0.4f, 3f, 9f) + Vector3.up * Mathf.Clamp(3.5f + force * 0.25f, 4f, 7.5f);
                Fx.Feathers(at, White, 10);
                Fx.Feathers(at, Brown, 5);
                Fx.Word(at + Vector3.up * 0.8f, Random.value < 0.5f ? "KOKOK!" : "KETAK!");
                ProcAudio.PlayAt(ProcAudio.Cluck, at, 0.6f, Random.Range(1.25f, 1.5f), 0.8f, 45f);
                ProcAudio.PlayAt(ProcAudio.Flutter, at, 0.45f, Random.Range(0.9f, 1.15f), 0.8f, 35f);
                if (!Revenge) Abused();
            }
            else
            {
                // too quick for you: a high leap clear, landing the right way up
                var pc = PlayerController.I;
                var away = pc != null ? transform.position - pc.transform.position : dir;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : dir;
                _flyVel = (away + Random.insideUnitSphere * 0.3f).normalized * 4.5f + Vector3.up * 6.5f;
                _flyVel.y = 6.5f;
                Fx.Word(at + Vector3.up * 0.6f, "HSSS!");
                ProcAudio.PlayAt(ProcAudio.Meow, at, 0.55f, 1.45f, 0.8f, 40f);
                Fx.Dust(transform.position);
            }
        }

        void Fly(float dt)
        {
            // a chicken flaps (half the pull of gravity); a cat just falls
            _flyVel += Physics.gravity * (isChicken ? 0.45f : 1.1f) * dt;
            if (isChicken) _flyVel.y = Mathf.Max(_flyVel.y, -4f);
            var pos = transform.position;
            var next = pos + _flyVel * dt;
            // walls stop it, and it slides down them
            var flat = new Vector3(_flyVel.x, 0f, _flyVel.z);
            if (flat.sqrMagnitude > 0.01f && Physics.Raycast(pos + Vector3.up * 0.3f, flat.normalized, out var wall, flat.magnitude * dt + 0.3f, Ground, QueryTriggerInteraction.Ignore))
            {
                _flyVel = Vector3.Reflect(flat, wall.normal) * 0.3f + Vector3.up * _flyVel.y;
                next = new Vector3(pos.x, next.y, pos.z);
            }
            if (_flyVel.y <= 0f && Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var hit, 0.5f - _flyVel.y * dt + 0.05f, Ground, QueryTriggerInteraction.Ignore))
            {
                // down: shake it off and leg it
                next.y = hit.point.y;
                _flying = false;
                transform.position = next;
                var yaw = flat.sqrMagnitude > 0.01f ? Quaternion.LookRotation(flat).eulerAngles.y : transform.eulerAngles.y;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                _panic = 2.2f;
                _target = next + (flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward) * 6f;
                Fx.Dust(next);
                if (isChicken) Fx.Feathers(next + Vector3.up * 0.3f, White, 4);
                return;
            }
            transform.position = next;
            if (next.y < -10f) { transform.position = _home; _flying = false; return; }
            if (isChicken)
            {
                // tumbling end over end
                _spin += dt * 720f;
                transform.rotation = Quaternion.Euler(-_spin, transform.eulerAngles.y, 0f);
            }
            else if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), dt * 12f);
        }

        void OnTriggerEnter(Collider other)
        {
            // a car clips it
            if (_flying) return;
            var rb = other.attachedRigidbody;
            if (rb == null || rb.isKinematic) return;
            var v = rb.GetComponent<Vehicle>();
            if (v == null) return;
            float speed = rb.linearVelocity.magnitude;
            if (speed < 3f) return;
            var dir = rb.linearVelocity + (transform.position - rb.position).normalized * speed * 0.5f;
            Kicked(dir, Mathf.Min(speed * 0.7f, 16f));
        }

        // ------------------------------------------------------------------ the flock's revenge
        static void Abused()
        {
            if (Time.time > _abuseUntil) _abuse = 0;
            _abuse++;
            _abuseUntil = Time.time + 15f;
            if (_abuse < 7) return;
            _abuse = 0;
            _pecks = 0;
            _floored = false;
            _revengeUntil = Time.time + 10f;
            var pc = PlayerController.I;
            if (pc == null) return;
            Fx.Word(pc.transform.position + Vector3.up * 2.6f, "AYAM MENGAMUK!", new Color(0.95f, 0.3f, 0.15f), 1.6f);
            ProcAudio.Play2D(ProcAudio.Flutter, 0.6f, 0.8f);
            ChaseCamera.I?.Shake(0.2f);
        }

        /// <summary>Revenge: fly at the player, flapping, and peck.</summary>
        void Attack(PlayerController p, float dt)
        {
            var chest = p.transform.position + Vector3.up * (1f + Mathf.Sin(Time.time * 7f + _home.x) * 0.35f);
            var to = chest - transform.position;
            float d = to.magnitude;
            if (d > 0.7f)
            {
                var step = to / d * Mathf.Min(d, (d > 8f ? 9f : 6f) * dt);
                transform.position += step;
                var flat = new Vector3(to.x, 0f, to.z);
                if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), dt * 10f);
                transform.GetChild(0).localPosition = Vector3.up * Mathf.Abs(Mathf.Sin(Time.time * 26f)) * 0.08f;
            }
            if ((_peckT -= dt) > 0f) return;
            _peckT = Random.Range(0.5f, 1.1f);
            if (d < 1.4f)
            {
                Fx.Feathers(transform.position + Vector3.up * 0.3f, White, 3);
                ProcAudio.PlayAt(ProcAudio.Cluck, transform.position, 0.45f, Random.Range(1.3f, 1.6f), 0.7f, 30f);
                ChaseCamera.I?.Shake(0.08f);
                _pecks++;
                if (!_floored && _pecks >= 14)
                {
                    // the punchline: down you go
                    _floored = true;
                    var away = p.transform.position - transform.position;
                    away.y = 0f;
                    p.Knockdown(away.normalized * 3f + Vector3.up * 3f);
                    _revengeUntil = Mathf.Min(_revengeUntil, Time.time + 2f);
                }
            }
            else if (Random.value < 0.3f) ProcAudio.PlayAt(ProcAudio.Flutter, transform.position, 0.25f, Random.Range(0.9f, 1.2f), 0.9f, 25f);
        }
    }
}
