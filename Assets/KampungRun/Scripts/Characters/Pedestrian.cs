using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Orang ramai: townsfolk who stroll the pavements, stop to chat, panic at horns.
    /// Punches and kicks work like Hit &amp; Run: a jab makes them stagger back a step, a big hit
    /// knocks them flat for a moment, they get up grumbling - and you can keep bopping them for as
    /// long as you like (even while they're down). Cars still send them tumbling.
    /// </summary>
    public class Pedestrian : MonoBehaviour
    {
        enum State { Walk, Idle, Flee, Tumble, Down, Stagger, Grumble }

        public static readonly List<Pedestrian> All = new List<Pedestrian>();
        public Rect zone;
        public float walkSpeed = 1.4f;

        State _state = State.Walk;
        Vector3 _target;
        float _timer;
        Vector3 _vel;
        CharacterRig _rig;
        Transform _model;
        float _spin, _yaw;
        bool _spinning;                 // car hits cartwheel; punches use the knockdown animation
        float _hitCooldown;
        int _combo, _coinsGiven;        // staggers in a row (the third one floors them), coins shaken loose
        float _comboT;
        // far away (a few pixels in the haze) we keep walking but stop drawing and animating,
        // so a busy downtown crowd stays cheap
        const float DrawDist = 170f;
        Renderer[] _renderers;
        Animator _anim;
        bool _hidden;
        float _drawCheck, _farDt;
        static readonly string[] Shouts = { "WOI!", "ADUH!", "APA NI?!", "HOI!", "MAK AI!", "ALAMAK!" };

        void Awake()
        {
            All.Add(this);
            _rig = GetComponentInChildren<CharacterRig>();
            _model = transform.Find("Model");
        }

        void OnDestroy() => All.Remove(this);

        /// <summary>Just strolling (not fleeing, knocked about or grumbling) - free to be recycled.</summary>
        public bool Idle => _state == State.Walk || _state == State.Idle;

        /// <summary>Crowd recycling: pop onto another block's sidewalk and carry on walking.</summary>
        public void Relocate(Vector3 pos, Rect newZone)
        {
            zone = newZone;
            transform.position = pos;
            _vel = Vector3.zero;
            _state = State.Walk;
            if (_rig) { _rig.panicking = false; _rig.waving = false; }
            PickTarget();
            _drawCheck = 0f;
        }

        void UpdateDraw()
        {
            _drawCheck = Random.Range(0.4f, 0.6f);
            var cam = Camera.main;
            if (!cam) return;
            bool hide = (cam.transform.position - transform.position).sqrMagnitude > DrawDist * DrawDist;
            if (hide == _hidden) return;
            _hidden = hide;
            if (_renderers == null)
            {
                // only the parts that are showing now (costume bits switched off stay off)
                _renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(), r => r.enabled);
                _anim = GetComponentInChildren<Animator>();
            }
            foreach (var r in _renderers) if (r) r.enabled = !hide;
            if (_anim) _anim.enabled = !hide;
        }

        void Start()
        {
            walkSpeed = Random.Range(1.1f, 1.7f);
            PickTarget();
        }

        void PickTarget()
        {
            // walk to a random point on the pavement ring of our block
            float inset = 1.5f;
            var r = new Rect(zone.x + inset, zone.y + inset, zone.width - inset * 2, zone.height - inset * 2);
            int side = Random.Range(0, 4);
            float t = Random.value;
            Vector2 p = side switch
            {
                0 => new Vector2(Mathf.Lerp(r.xMin, r.xMax, t), r.yMin),
                1 => new Vector2(Mathf.Lerp(r.xMin, r.xMax, t), r.yMax),
                2 => new Vector2(r.xMin, Mathf.Lerp(r.yMin, r.yMax, t)),
                _ => new Vector2(r.xMax, Mathf.Lerp(r.yMin, r.yMax, t)),
            };
            // go via the nearest corner if it's on another side, so we stay on the ring
            Vector2 cur = new Vector2(transform.position.x, transform.position.z);
            bool sameSide = Mathf.Abs(cur.x - p.x) < 1f || Mathf.Abs(cur.y - p.y) < 1f;
            if (!sameSide)
            {
                var corner = new Vector2(Mathf.Abs(cur.x - r.xMin) < Mathf.Abs(cur.x - r.xMax) ? r.xMin : r.xMax,
                                         Mathf.Abs(cur.y - r.yMin) < Mathf.Abs(cur.y - r.yMax) ? r.yMin : r.yMax);
                // snap the corner onto our current edge direction
                if (Mathf.Abs(cur.x - r.xMin) < 1.5f || Mathf.Abs(cur.x - r.xMax) < 1.5f) corner.x = cur.x;
                else corner.y = cur.y;
                p = corner;
            }
            _target = new Vector3(p.x, transform.position.y, p.y);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if ((_drawCheck -= dt) <= 0f) UpdateDraw();
            if (_hidden)
            {
                // out of sight: walk on in coarse steps, four times a second
                _farDt += dt;
                if (_farDt < 0.25f) return;
                dt = _farDt;
                _farDt = 0f;
            }
            if (_hitCooldown > 0) _hitCooldown -= dt;
            if (_comboT > 0) _comboT -= dt; else _combo = 0;
            switch (_state)
            {
                case State.Stagger:
                    // a short, heavy slide back along the ground, then carry on
                    _timer -= dt;
                    _vel = Vector3.MoveTowards(_vel, Vector3.zero, dt * 9f);
                    Slide(_vel * dt);
                    if (_rig) _rig.speed = 0;
                    if (_timer <= 0) Recover();
                    break;
                case State.Grumble:
                    // stand there shaking a fist at you for a moment
                    _timer -= dt;
                    if (_rig) { _rig.speed = 0; _rig.waving = true; }
                    var threat = PlayerController.I ? PlayerController.I.transform.position : transform.position;
                    var look = Flat(threat - transform.position);
                    if (look.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), dt * 6f);
                    if (_timer <= 0) { if (_rig) _rig.waving = false; _state = State.Walk; PickTarget(); }
                    break;
                case State.Walk:
                    Move(_target, walkSpeed, dt);
                    if (Flat(transform.position - _target).magnitude < 0.6f)
                    {
                        if (Random.value < 0.3f) { _state = State.Idle; _timer = Random.Range(2f, 6f); }
                        PickTarget();
                    }
                    break;
                case State.Idle:
                    _timer -= dt;
                    if (_rig) _rig.speed = 0;
                    if (_timer <= 0) _state = State.Walk;
                    break;
                case State.Flee:
                    _timer -= dt;
                    Move(_target, 6f, dt);
                    if (_rig) _rig.panicking = true;
                    if (Flat(transform.position - _target).magnitude < 1f) PickFleeTarget();
                    if (_timer <= 0) { _state = State.Walk; if (_rig) _rig.panicking = false; PickTarget(); }
                    break;
                case State.Tumble:
                    _vel += Physics.gravity * 1.3f * dt;
                    var next = transform.position + _vel * dt;
                    if (_vel.y < 0 && Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 0.5f - _vel.y * dt + 0.05f, ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle), QueryTriggerInteraction.Ignore))
                    {
                        next.y = hit.point.y;
                        _vel = Flat(_vel) * 0.4f;
                        if (_vel.magnitude < 1.5f) { _state = State.Down; _timer = _rig && _rig.Humanoid ? 1.5f : 1.6f; }
                        else _vel.y = _vel.magnitude * 0.4f;   // bounce
                        Fx.Dust(next);
                    }
                    else if (Physics.Raycast(transform.position + Vector3.up, _vel.normalized, out var wall, _vel.magnitude * dt + 0.4f, ~(1 << Layers.Character | 1 << Layers.Pickup), QueryTriggerInteraction.Ignore))
                    {
                        _vel = Vector3.Reflect(_vel, wall.normal) * 0.4f;
                        next = transform.position;
                    }
                    transform.position = next;
                    if (_spinning || !(_rig && _rig.Humanoid))
                    {
                        _spin += dt * (_spinning ? 420f : 540f);
                        transform.rotation = Quaternion.Euler(-_spin, _yaw, 0);
                    }
                    else transform.rotation = Quaternion.Euler(0, _yaw, 0);
                    if (transform.position.y < -10) Destroy(gameObject);
                    break;
                case State.Down:
                    _timer -= dt;
                    if (_rig && _rig.Humanoid) transform.rotation = Quaternion.Euler(0, _yaw, 0);   // the clip lies them down
                    else transform.rotation = Quaternion.Slerp(Quaternion.Euler(-90, _yaw, 0), Quaternion.Euler(0, _yaw, 0),
                        1f - _timer / 1.6f < 0.6f ? 0f : (1f - _timer / 1.6f - 0.6f) / 0.4f);
                    if (_rig) _rig.speed = 0;
                    if (_timer <= 0)
                    {
                        transform.rotation = Quaternion.Euler(0, _yaw, 0);
                        _spinning = false;
                        Recover();
                    }
                    break;
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        /// <summary>Move along the ground, stopping at walls and following steps.</summary>
        void Slide(Vector3 step)
        {
            if (step.sqrMagnitude < 1e-8f) return;
            if (Physics.Raycast(transform.position + Vector3.up * 0.8f, step.normalized, step.magnitude + 0.35f,
                    ~(1 << Layers.Character | 1 << Layers.Pickup), QueryTriggerInteraction.Ignore)) return;
            var p = transform.position + step;
            if (Physics.Raycast(p + Vector3.up * 1.0f, Vector3.down, out var hit, 2f,
                    ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle), QueryTriggerInteraction.Ignore) &&
                hit.point.y < transform.position.y + 0.4f)
                p.y = hit.point.y;
            transform.position = p;
        }

        /// <summary>Back on their feet: usually a grumble at you, sometimes a short dash away.</summary>
        void Recover()
        {
            if (Random.value < 0.6f)
            {
                _state = State.Grumble;
                _timer = Random.Range(1.0f, 1.8f);
                Fx.Word(transform.position + Vector3.up * 2.2f, Grumbles[Random.Range(0, Grumbles.Length)]);
            }
            else
            {
                _state = State.Flee;
                _timer = Random.Range(1.5f, 2.5f);
                PickFleeTarget();
            }
        }

        static readonly string[] Grumbles = { "KURANG AJAR!", "HEI!", "SAKIT LAH!", "APA MASALAH KAU?", "JAGA KAU!" };

        void Move(Vector3 target, float speed, float dt)
        {
            var to = Flat(target - transform.position);
            if (to.sqrMagnitude > 0.01f)
            {
                var step = to.normalized * Mathf.Min(speed * dt, to.magnitude);
                if (Physics.Raycast(transform.position + Vector3.up * 0.8f, to.normalized, step.magnitude + 0.5f,
                        ~(1 << Layers.Character | 1 << Layers.Pickup), QueryTriggerInteraction.Ignore))
                {
                    if (_state == State.Walk) PickTarget(); else PickFleeTarget();
                    if (_rig) _rig.speed = 0;
                    return;
                }
                var p = transform.position + step;
                if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle), QueryTriggerInteraction.Ignore))
                {
                    if (hit.point.y > transform.position.y + 0.5f)
                    {
                        // something tall in the way: turn around
                        if (_state == State.Walk) PickTarget(); else PickFleeTarget();
                        return;
                    }
                    p.y = hit.point.y;
                }
                transform.position = p;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), dt * 8f);
            }
            if (_rig) _rig.speed = speed;
        }

        void PickFleeTarget()
        {
            var threat = PlayerController.I ? PlayerController.I.Focus : transform.position;
            var away = Flat(transform.position - threat).normalized;
            if (away.sqrMagnitude < 0.1f) away = Random.insideUnitSphere;
            var p = transform.position + Flat(away + Random.insideUnitSphere * 0.5f).normalized * 12f;
            // stay inside our block
            p.x = Mathf.Clamp(p.x, zone.xMin + 1, zone.xMax - 1);
            p.z = Mathf.Clamp(p.z, zone.yMin + 1, zone.yMax - 1);
            _target = p;
        }

        public void Flee(float time)
        {
            if (_state == State.Tumble || _state == State.Down || _state == State.Stagger) return;
            if (_rig) _rig.waving = false;
            _state = State.Flee;
            _timer = time;
            PickFleeTarget();
        }

        /// <summary>
        /// Punched / kicked / stomped (H&amp;R style): dir = away from the attacker, power = hit
        /// strength (jab ~6, combo finisher ~11, kick ~13). Light hits stagger them a step; the
        /// third in a row, big hits, and anything that lands while they're down knock them over
        /// with a small hop - never a launch. Always accepted, so the bopping never stops.
        /// </summary>
        public void Hit(Vector3 dir, float power, bool byPlayer)
        {
            if (_hitCooldown > 0f) return;
            _hitCooldown = 0.12f;
            dir = Flat(dir).sqrMagnitude > 0.01f ? Flat(dir).normalized : -transform.forward;
            _yaw = Quaternion.LookRotation(-dir).eulerAngles.y;          // face the attacker as they reel
            transform.rotation = Quaternion.Euler(0, _yaw, 0);
            if (_rig) { _rig.panicking = false; _rig.waving = false; }
            _combo++;
            _comboT = 1.2f;
            bool down = _state == State.Down || (_state == State.Tumble && !_spinning);
            bool floor = down || power >= 10f || _combo >= 3;
            if (!floor)
            {
                _state = State.Stagger;
                _timer = 0.45f;
                _vel = dir * (1.6f + power * 0.12f);
                _rig?.Trigger("Hit");
            }
            else
            {
                _combo = 0;
                _spinning = false;
                _state = State.Tumble;
                // a small hop and slide: they topple over right there, a metre or two away
                _vel = dir * (down ? 1.2f : 2.2f + power * 0.08f) + Vector3.up * (down ? 1.8f : 2.6f);
                if (_rig) _rig.Tumble(2.2f);
            }
            ProcAudio.Play(ProcAudio.Aduh, transform.position, 0.6f, Random.Range(0.85f, 1.35f));
            if (Random.value < 0.6f) Fx.Word(transform.position + Vector3.up * 2.2f, Shouts[Random.Range(0, Shouts.Length)]);
            if (byPlayer) SamanMeter.I?.AddHeat(down ? 3f : floor ? 8f : 5f);
            ScareAround(transform.position, 8f);
            // H&R: bopping people shakes coins loose (a few per person, so it can't be farmed forever)
            if (byPlayer && _coinsGiven < 4 && Random.value < 0.45f) { _coinsGiven++; Pickup.SpawnCoin(transform.position + Vector3.up, true); }
            GameManager.I?.Missions?.NotifyPedHit(this);
        }

        /// <summary>Hit by a car: a proper tumble through the air (cartwheeling), then a lie-down.</summary>
        public void Knock(Vector3 impulse, bool byPlayer)
        {
            if (_state == State.Tumble && _spinning) return;
            _state = State.Tumble;
            _spinning = true;
            _combo = 0;
            impulse = Vector3.ClampMagnitude(impulse, 14f);
            _yaw = transform.eulerAngles.y;
            _vel = impulse;
            if (_vel.y < 3f) _vel.y = 3f + impulse.magnitude * 0.2f;
            if (_rig) { _rig.panicking = false; _rig.Tumble(2.5f); }
            ProcAudio.Play(ProcAudio.Aduh, transform.position, 0.7f, Random.Range(0.8f, 1.3f));
            Fx.Word(transform.position + Vector3.up * 2.2f, Shouts[Random.Range(0, Shouts.Length)]);
            if (byPlayer) SamanMeter.I?.AddHeat(impulse.magnitude > 12f ? 22f : 12f);
            ScareAround(transform.position, 10f);
            // H&R: knocking people about shakes a coin loose sometimes
            if (byPlayer && Random.value < 0.5f) Pickup.SpawnCoin(transform.position + Vector3.up, true);
            GameManager.I?.Missions?.NotifyPedHit(this);
        }

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null) return;
            var v = rb.GetComponent<Vehicle>();
            if (v == null) return;
            float speed = rb.linearVelocity.magnitude;
            if (speed < 3.5f) { Flee(4f); return; }
            Knock(rb.linearVelocity * 0.55f + Vector3.up * (3.5f + speed * 0.18f), v.PlayerInside);
        }

        public static void ScareAround(Vector3 pos, float radius)
        {
            foreach (var p in All)
                if (p && Vector3.Distance(p.transform.position, pos) < radius) p.Flee(Random.Range(3f, 6f));
        }
    }

    public static class PedestrianSpawner
    {
        static readonly string[] Models = { "chr_townman", "chr_townaunty", "chr_pakcik", "chr_kid", "chr_townman", "chr_townaunty" };
        static readonly Color[] Shirts =
        {
            new Color(0.25f, 0.42f, 0.62f), new Color(0.85f, 0.55f, 0.62f), new Color(0.9f, 0.8f, 0.35f), new Color(0.4f, 0.62f, 0.4f),
            new Color(0.8f, 0.3f, 0.25f), new Color(0.72f, 0.7f, 0.85f), new Color(0.95f, 0.93f, 0.88f), new Color(0.88f, 0.62f, 0.52f),
        };
        static readonly Color[] Skins = { new Color(0.8f, 0.58f, 0.4f), new Color(0.62f, 0.42f, 0.28f), new Color(0.9f, 0.72f, 0.56f), new Color(0.5f, 0.34f, 0.22f) };

        public static Pedestrian Spawn(Vector3 pos, Rect zone, Transform parent, string model = null)
        {
            model ??= Models[Random.Range(0, Models.Length)];
            var go = ModelFactory.Spawn(model, pos, Quaternion.Euler(0, Random.Range(0, 360), 0), parent, "Orang");
            ModelFactory.Recolor(go, new Dictionary<string, Color>
            {
                ["BatikBlue"] = Shirts[Random.Range(0, Shirts.Length)],
                ["Pastel3"] = Shirts[Random.Range(0, Shirts.Length)],
                ["White"] = Shirts[Random.Range(0, Shirts.Length)],
                ["Pastel2"] = Shirts[Random.Range(0, Shirts.Length)],
                ["Skin"] = Skins[Random.Range(0, Skins.Length)],
            });
            go.AddComponent<CharacterRig>();
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Character;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.isTrigger = true;
            cap.radius = 0.5f;
            cap.height = 1.8f;
            cap.center = new Vector3(0, 0.9f, 0);
            var p = go.AddComponent<Pedestrian>();
            p.zone = zone;
            return p;
        }

        public static void SpawnFleeing(Vector3 pos, Quaternion rot)
        {
            var zone = new Rect(pos.x - 30, pos.z - 30, 60, 60);
            var p = Spawn(pos, zone, GameManager.I.transform, "chr_townman");
            p.Flee(6f);
            Fx.Word(pos + Vector3.up * 2.2f, "KERETA AKU!");
            Object.Destroy(p.gameObject, 20f);
        }
    }
}
