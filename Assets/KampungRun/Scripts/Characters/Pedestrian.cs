using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Orang ramai: townsfolk who stroll the pavements, stop to chat, panic at horns.
    /// Punches and kicks work like Hit &amp; Run: a jab makes them stagger back a step, a big hit
    /// knocks them flat for a moment, they get up grumbling - and you can keep bopping them for as
    /// long as you like (even while they're down). Cars still send them tumbling.
    ///
    /// And they're people, not props: each has a voice and a type (pakcik, aunty, office type, kid); they
    /// walk side by side rather than in single file, turn their heads to watch you go by, say hello, stop
    /// to natter in pairs, stand and stare at a commotion, leap back from a car that nearly hits them, and
    /// tell you off if you barge into them.
    /// </summary>
    public class Pedestrian : MonoBehaviour
    {
        enum State { Walk, Idle, Flee, Tumble, Down, Stagger, Grumble, Chat, Watch }

        /// <summary>Things that make a street turn its head.</summary>
        public enum Stir { Horn, Commotion, Crash }

        public static readonly List<Pedestrian> All = new List<Pedestrian>();
        public WalkZone zone;
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
        // far away (a few pixels in the haze) we keep walking but stop drawing and animating, and further
        // than a street or two the body is posed less often: a busy downtown crowd stays cheap (a browser
        // animates and skins every one of them on its single thread)
        static readonly float DrawDist = Application.platform == RuntimePlatform.WebGLPlayer ? 110f : 170f;
        const float FullRateDist = 30f;
        const float LookDist = 24f;
        Renderer[] _renderers;
        Animator _anim;
        bool _hidden;
        float _drawCheck, _farDt, _animDt;
        int _animStep = 1;
        int _animPhase;
        static readonly string[] Shouts = { "WOI!", "ADUH!", "APA NI?!", "HOI!", "MAK AI!", "ALAMAK!" };

        // who they are and what they're up to
        string _arch = "man";
        VoiceSynth.Profile _voice;
        string _voiceKey = "man0";
        HeadLook _look;
        float _lane;                    // how far off the loop's centre line they walk (no conga lines)
        float _socialCheck, _greetCooldown, _barkCooldown, _bumpCooldown, _nearMissCooldown, _waveT;
        bool _mild;                     // a bump or a near miss: shrug it off rather than shout "KURANG AJAR!"
        Pedestrian _partner;            // who they're chatting to
        Vector3 _chatSpot, _watchAt;
        float _chatLine;

        void Awake()
        {
            All.Add(this);
            _animPhase = Random.Range(0, 4);         // spread the throttled crowd's poses over the frames
            _rig = GetComponentInChildren<CharacterRig>();
            _model = transform.Find("Model");
            _look = GetComponent<HeadLook>();
        }

        void OnDestroy()
        {
            LeaveChat();
            All.Remove(this);
        }

        /// <summary>A voice and a type: "pakcik", "aunty", "kid" or "man".</summary>
        public void SetPersona(string archetype)
        {
            _arch = archetype;
            int variant = Random.Range(0, 3);
            _voice = VoiceSynth.Townsperson(archetype, variant);
            _voiceKey = archetype + variant;
        }

        /// <summary>Just strolling (not fleeing, knocked about or grumbling) - free to be recycled.</summary>
        public bool Idle => _state == State.Walk || _state == State.Idle || _state == State.Chat || _state == State.Watch;

        /// <summary>Crowd recycling: pop onto another block's sidewalk and carry on walking.</summary>
        public void Relocate(Vector3 pos, WalkZone newZone)
        {
            LeaveChat();
            zone = newZone;
            transform.position = pos;
            _vel = Vector3.zero;
            _state = State.Walk;
            if (_rig) { _rig.panicking = false; _rig.waving = false; }
            _look?.Clear();
            PickTarget();
            _drawCheck = 0f;
        }

        void UpdateDraw()
        {
            _drawCheck = Random.Range(0.4f, 0.6f);
            var cam = Camera.main;
            if (!cam) return;
            float d2 = (cam.transform.position - transform.position).sqrMagnitude;
            bool hide = d2 > DrawDist * DrawDist;
            if (_renderers == null)
            {
                // only the parts that are showing now (costume bits switched off stay off)
                _renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(), r => r.enabled);
                _anim = GetComponentInChildren<Animator>();
            }
            // posed every frame close up, every other frame down the street, every fourth beyond
            _animStep = hide || _state != State.Walk && _state != State.Idle && _state != State.Chat ? 1 : d2 < FullRateDist * FullRateDist ? 1 : d2 < 70f * 70f ? 2 : 4;
            if (_anim) _anim.enabled = !hide && _animStep == 1;
            // heads only turn close up, where the body is posed every frame
            if (_look) _look.enabled = !hide && d2 < LookDist * LookDist;
            if (hide == _hidden) return;
            _hidden = hide;
            foreach (var r in _renderers) if (r) r.enabled = !hide;
        }

        void LateUpdate()
        {
            // the throttled animator is stepped by hand (with all the time since its last pose)
            if (_hidden || _anim == null || _animStep == 1) { _animDt = 0f; return; }
            _animDt += Time.deltaTime;
            if ((Time.frameCount + _animPhase) % _animStep != 0) return;
            _anim.Update(_animDt);
            _animDt = 0f;
        }

        void Start()
        {
            // (a kid's legs are half the length: at 1.1-1.8 m/s they trot, any faster and they'd always be running)
            walkSpeed = _arch == "kid" ? Random.Range(1.1f, 1.8f) : Random.Range(1.1f, 1.7f);
            _lane = Random.Range(-0.9f, 0.9f);
            _greetCooldown = Random.Range(0f, 20f);
            if (_voice == null) SetPersona(_arch);
            PickTarget();
        }

        // walking round the pavement loop: where we are on it, where we're heading, and the next corner
        float _ringU, _goalU, _wayU, _wayT;
        int _ringDir = 1;
        bool _wayIsGoal = true;

        void PickTarget()
        {
            // a random spot on our block's pavement loop, going round the short way
            if (zone == null) { _target = transform.position; return; }
            _ringU = zone.Closest(transform.position);
            _goalU = zone.Random();
            _ringDir = zone.Along(_ringU, _goalU, 1) <= zone.perimeter * 0.5f ? 1 : -1;
            NextWaypoint();
        }

        void NextWaypoint()
        {
            float corner = zone.NextCorner(_ringU, _ringDir);
            _wayIsGoal = zone.Along(_ringU, _goalU, _ringDir) <= zone.Along(_ringU, corner, _ringDir);
            _wayU = _wayIsGoal ? _goalU : corner;
            _target = OnLane(_wayU, transform.position.y);
            _wayT = 0f;
        }

        /// <summary>A point on the loop, shifted sideways onto this person's own lane of the pavement.</summary>
        Vector3 OnLane(float u, float y)
        {
            var p = zone.PointAt(u, y);
            var d = zone.PointAt(u + 0.5f, y) - zone.PointAt(u - 0.5f, y);
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return p;
            d.Normalize();
            return p + new Vector3(d.z, 0f, -d.x) * _lane;
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
            _greetCooldown -= dt;
            _barkCooldown -= dt;
            _bumpCooldown -= dt;
            _nearMissCooldown -= dt;
            if (_waveT > 0f && (_waveT -= dt) <= 0f && _rig && _state != State.Grumble) _rig.waving = false;
            if (!_hidden && (_socialCheck -= dt) <= 0f) Social();
            // gestures belong to standing about and talking; anything else drops them
            if (_rig && _rig.gesture >= 0 && _state != State.Chat && _state != State.Watch && _state != State.Grumble) _rig.gesture = -1;
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
                    if (_rig) { _rig.speed = 0; _rig.gesture = (int)CharacterRig.Gesture.Angry; }
                    var threat = PlayerController.I ? PlayerController.I.transform.position : transform.position;
                    var look = Flat(threat - transform.position);
                    if (look.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), dt * 6f);
                    if (_timer <= 0) { if (_rig) { _rig.waving = false; _rig.gesture = -1; } _state = State.Walk; PickTarget(); }
                    break;
                case State.Walk:
                    Move(_target, walkSpeed, dt);
                    _wayT += dt;
                    if (_wayT > 25f) { PickTarget(); break; }              // stuck on something: head somewhere else
                    if (Flat(transform.position - _target).magnitude < 0.6f)
                    {
                        if (!_wayIsGoal && zone != null) { _ringU = _wayU; NextWaypoint(); break; }   // round the corner
                        // arrived: sometimes stop for a natter with someone passing, sometimes just stand a while
                        float r = Random.value;
                        if (r < 0.35f && TryStartChat()) break;
                        if (r < 0.6f) { _state = State.Idle; _timer = Random.Range(2f, 6f); }
                        PickTarget();
                    }
                    break;
                case State.Idle:
                    _timer -= dt;
                    if (_rig) _rig.speed = 0;
                    if (_timer <= 0) _state = State.Walk;
                    break;
                case State.Chat:
                    _timer -= dt;
                    if (_partner == null || _timer <= 0f) { EndChat(); break; }
                    if (Flat(transform.position - _chatSpot).sqrMagnitude > 0.09f) Move(_chatSpot, walkSpeed, dt);
                    else
                    {
                        // face each other and natter: the odd gesture, a line of gossip if you're close enough to hear
                        if (_rig) _rig.speed = 0;
                        var face = Flat(_partner.transform.position - transform.position);
                        if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(face), dt * 5f);
                        if ((_chatLine -= dt) <= 0f)
                        {
                            _chatLine = Random.Range(3.5f, 8f);
                            var pc = PlayerController.I;
                            if (pc != null && (pc.transform.position - transform.position).sqrMagnitude < 14f * 14f)
                                Say(Barks.Pick(Barks.Chatter), true);
                        }
                    }
                    break;
                case State.Watch:
                    // stop and stare at whatever happened
                    _timer -= dt;
                    if (_rig) _rig.speed = 0;
                    var at = Flat(_watchAt - transform.position);
                    if (at.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(at), dt * 4f);
                    if (_timer <= 0f) { _state = State.Walk; _look?.Clear(); PickTarget(); }
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

        bool Calm => _state == State.Walk || _state == State.Idle || _state == State.Chat || _state == State.Watch;

        /// <summary>Say something: a bubble over their head in their own voice.</summary>
        bool Say(string line, bool ambient, bool excited = false) =>
            Barks.Say(transform, _voice, _voiceKey, line, ambient, _arch == "kid" ? 1.95f : 2.45f, 0.85f, excited);

        /// <summary>Notice the player: heads turn as you pass, and now and then someone says hello.</summary>
        void Social()
        {
            _socialCheck = Random.Range(0.4f, 0.7f);
            var pc = PlayerController.I;
            if (pc == null || !_look || !_look.enabled) return;
            var to = pc.transform.position - transform.position;
            float d2 = to.sqrMagnitude;
            if (_state == State.Walk || _state == State.Idle)
            {
                if (d2 < 7f * 7f && !pc.Driving) _look.LookAt(pc.transform);
                else _look.Clear();
            }
            if (!(_state == State.Walk || _state == State.Idle) || pc.Driving || _greetCooldown > 0f) return;
            if (d2 > 3.6f * 3.6f || pc.Speed > 4.5f || Vector3.Dot(transform.forward, to) < 0f) return;
            _greetCooldown = Random.Range(45f, 90f);
            if (Random.value > 0.45f) return;
            var name = pc.def != null ? pc.def.name : null;
            if (Say(Barks.Pick(_arch == "kid" ? Barks.GreetKid : Barks.Greet, name), true) && _rig && Random.value < 0.5f)
            {
                _rig.waving = true;
                _waveT = 1.2f;
            }
        }

        // ------------------------------------------------------------------ chatting in pairs
        bool TryStartChat()
        {
            if (_hidden || _partner != null) return false;
            Pedestrian best = null;
            float bd = 7f * 7f;
            foreach (var p in All)
            {
                // same pavement loop only (never across a road to natter)
                if (p == this || p == null || p._hidden || p._partner != null || p.zone != zone || !(p._state == State.Walk || p._state == State.Idle)) continue;
                float d2 = (p.transform.position - transform.position).sqrMagnitude;
                if (d2 < bd) { bd = d2; best = p; }
            }
            if (best == null) return false;
            float time = Random.Range(10f, 28f);
            var mid = (transform.position + best.transform.position) * 0.5f;
            var axis = Flat(best.transform.position - transform.position);
            axis = axis.sqrMagnitude < 0.01f ? transform.right : axis.normalized;
            BeginChat(best, mid - axis * 0.55f, time);
            best.BeginChat(this, mid + axis * 0.55f, time);
            return true;
        }

        void BeginChat(Pedestrian partner, Vector3 spot, float time)
        {
            _partner = partner;
            _chatSpot = spot;
            _state = State.Chat;
            _timer = time;
            _chatLine = Random.Range(1f, 4f);
            if (_rig)
            {
                // talking with their hands: explaining, laughing at their own story, or pointing and laughing at the other one
                _rig.panicking = false;
                float g = Random.value;
                _rig.gesture = (int)(g < 0.5f ? CharacterRig.Gesture.Talk : g < 0.85f ? CharacterRig.Gesture.Talk2 : CharacterRig.Gesture.PointLaugh);
            }
            _look?.LookAt(partner.transform);
        }

        void EndChat()
        {
            LeaveChat();
            _state = State.Walk;
            _look?.Clear();
            PickTarget();
        }

        /// <summary>Break off a conversation (the other one wanders off too).</summary>
        void LeaveChat()
        {
            var p = _partner;
            _partner = null;
            if (p == null || p._partner != this) return;
            p._partner = null;
            if (p._state == State.Chat) { p._state = State.Walk; p._look?.Clear(); p.PickTarget(); }
        }

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
            if (_mild)
            {
                // only barged into (or nearly run over): a look, then on their way
                _mild = false;
                _state = State.Grumble;
                _timer = Random.Range(0.6f, 1.0f);
                return;
            }
            if (Random.value < 0.6f)
            {
                _state = State.Grumble;
                _timer = Random.Range(1.0f, 1.8f);
                var g = Grumbles[Random.Range(0, Grumbles.Length)];
                Fx.Word(transform.position + Vector3.up * 2.2f, g);
                VoiceSynth.SayAt(_voice, _voiceKey, g, transform.position + Vector3.up * 1.6f, 0.8f, true);
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
                    if (_state == State.Walk) PickTarget(); else if (_state == State.Flee) PickFleeTarget();
                    if (_rig) _rig.speed = 0;
                    return;
                }
                var p = transform.position + step;
                if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle), QueryTriggerInteraction.Ignore))
                {
                    if (hit.point.y > transform.position.y + 0.5f)
                    {
                        // something tall in the way: turn around
                        if (_state == State.Walk) PickTarget(); else if (_state == State.Flee) PickFleeTarget();
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
            // stay round our block
            if (zone != null)
            {
                p.x = Mathf.Clamp(p.x, zone.bounds.xMin + 1, zone.bounds.xMax - 1);
                p.z = Mathf.Clamp(p.z, zone.bounds.yMin + 1, zone.bounds.yMax - 1);
            }
            _target = p;
        }

        public void Flee(float time)
        {
            if (_state == State.Tumble || _state == State.Down || _state == State.Stagger) return;
            LeaveChat();
            if (_rig) _rig.waving = false;
            _look?.Clear();
            _state = State.Flee;
            _timer = time;
            PickFleeTarget();
        }

        /// <summary>
        /// Walked into by the player: stumble out of the way and say something about it. False if they're
        /// busy (fleeing, knocked about) or were only just bumped.
        /// </summary>
        public bool Bump(Vector3 dir, float speed)
        {
            if (_bumpCooldown > 0f || !Calm) return false;
            _bumpCooldown = 1.2f;
            LeaveChat();
            var pc = PlayerController.I;
            var side = Vector3.Cross(Vector3.up, dir);
            if (pc != null && Vector3.Dot(side, transform.position - pc.transform.position) < 0f) side = -side;
            _state = State.Stagger;
            _timer = 0.35f;
            _mild = true;
            _vel = (side * 0.8f + dir * 0.6f) * Mathf.Clamp(speed * 0.45f, 1.4f, 3.2f);
            _rig?.Trigger("Hit");
            if (pc != null) _look?.LookAt(pc.transform);
            if (_barkCooldown <= 0f) { _barkCooldown = 6f; Say(Barks.Pick(Barks.Bumped), false, true); }
            return true;
        }

        /// <summary>A car tore past within a few metres: leap back off the kerb and shout after it.</summary>
        public void NearMiss(Vector3 carPos, Vector3 carVel)
        {
            if (!Calm || _nearMissCooldown > 0f) return;
            _nearMissCooldown = 3f;
            LeaveChat();
            var side = Vector3.Cross(Vector3.up, carVel.normalized);
            if (Vector3.Dot(side, transform.position - carPos) < 0f) side = -side;
            _yaw = Quaternion.LookRotation(Flat(carPos - transform.position).sqrMagnitude > 0.01f ? Flat(carPos - transform.position) : transform.forward).eulerAngles.y;
            transform.rotation = Quaternion.Euler(0, _yaw, 0);
            _state = State.Stagger;
            _timer = 0.4f;
            _mild = Random.value < 0.5f;
            _vel = side * 3.2f;
            _rig?.Trigger("Hit");
            if (_barkCooldown <= 0f) { _barkCooldown = 5f; Say(Barks.Pick(Barks.NearMiss), false, true); }
        }

        /// <summary>
        /// Something happened here (a horn blast, a punch-up, a crash): close by, most people scatter; further
        /// off they stop and stare, and one or two say what they think of it.
        /// </summary>
        public static void React(Vector3 pos, float radius, Stir stir)
        {
            float r2 = radius * radius;
            int talkers = 0;
            foreach (var p in All)
            {
                if (p == null || p._hidden) continue;
                float d2 = (p.transform.position - pos).sqrMagnitude;
                if (d2 < r2) p.OnStir(pos, Mathf.Sqrt(d2), stir, ref talkers);
            }
        }

        void OnStir(Vector3 pos, float d, Stir stir, ref int talkers)
        {
            if (!Calm) return;
            float flee = stir == Stir.Horn ? (d < 6f ? 0.7f : 0.2f) : stir == Stir.Commotion ? (d < 5f ? 0.65f : 0.2f) : (d < 8f ? 0.6f : 0.3f);
            if (Random.value < flee) { Flee(Random.Range(2.5f, 5f)); return; }
            LeaveChat();
            _state = State.Watch;
            _timer = Random.Range(2f, 4.5f);
            _watchAt = pos;
            if (_rig)
            {
                // everyone reacts in their own way: arms folded, hands on hips, phone out to film it, pointing
                // and laughing; kids cheer the chaos on; a horn just gets you told off
                float g = Random.value;
                var react = stir == Stir.Horn ? (g < 0.5f ? CharacterRig.Gesture.Angry : CharacterRig.Gesture.WatchHips)
                    : _arch == "kid" && g < 0.5f ? CharacterRig.Gesture.Cheer
                    : g < 0.32f ? CharacterRig.Gesture.Film : g < 0.52f ? CharacterRig.Gesture.WatchCross
                    : g < 0.72f ? CharacterRig.Gesture.WatchHips : CharacterRig.Gesture.PointLaugh;
                _rig.gesture = (int)react;
            }
            _look?.LookAt(pos + Vector3.up * 0.8f);
            if (talkers < 2 && _barkCooldown <= 0f && Random.value < 0.5f)
            {
                talkers++;
                _barkCooldown = 5f;
                Say(Barks.Pick(stir == Stir.Horn ? Barks.Horned : Barks.Watch), false);
            }
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
            LeaveChat();
            _mild = false;
            _look?.Clear();
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
            if (Random.value < 0.6f)
            {
                var s = Shouts[Random.Range(0, Shouts.Length)];
                Fx.Word(transform.position + Vector3.up * 2.2f, s);
                VoiceSynth.SayAt(_voice, _voiceKey, s, transform.position + Vector3.up * 1.6f, 0.75f, true);
            }
            if (byPlayer) SamanMeter.I?.AddHeat(down ? 3f : floor ? 8f : 5f);
            React(transform.position, 16f, Stir.Commotion);
            // H&R: bopping people shakes coins loose (a few per person, so it can't be farmed forever)
            if (byPlayer && _coinsGiven < 4 && Random.value < 0.45f) { _coinsGiven++; Pickup.SpawnCoin(transform.position + Vector3.up, true); }
            GameManager.I?.Missions?.NotifyPedHit(this);
        }

        /// <summary>Hit by a car: a proper tumble through the air (cartwheeling), then a lie-down.</summary>
        public void Knock(Vector3 impulse, bool byPlayer)
        {
            if (_state == State.Tumble && _spinning) return;
            LeaveChat();
            _mild = false;
            _look?.Clear();
            _state = State.Tumble;
            _spinning = true;
            _combo = 0;
            impulse = Vector3.ClampMagnitude(impulse, 14f);
            _yaw = transform.eulerAngles.y;
            _vel = impulse;
            if (_vel.y < 3f) _vel.y = 3f + impulse.magnitude * 0.2f;
            if (_rig) { _rig.panicking = false; _rig.Tumble(2.5f); }
            ProcAudio.Play(ProcAudio.Aduh, transform.position, 0.7f, Random.Range(0.8f, 1.3f));
            var s = Shouts[Random.Range(0, Shouts.Length)];
            Fx.Word(transform.position + Vector3.up * 2.2f, s);
            VoiceSynth.SayAt(_voice, _voiceKey, s, transform.position + Vector3.up * 1.6f, 0.9f, true);
            if (byPlayer) SamanMeter.I?.AddHeat(impulse.magnitude > 12f ? 22f : 12f);
            React(transform.position, 22f, Stir.Crash);
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

        public static Pedestrian Spawn(Vector3 pos, WalkZone zone, Transform parent, string model = null)
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
            go.AddComponent<HeadLook>();
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Character;
            // two bones a vertex is plenty for a townsperson, and halves the skinning a browser does on its CPU
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.quality = SkinQuality.Bone2;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.isTrigger = true;
            cap.radius = 0.5f;
            cap.height = 1.8f;
            cap.center = new Vector3(0, 0.9f, 0);
            var p = go.AddComponent<Pedestrian>();
            p.zone = zone;
            p.SetPersona(model == "chr_pakcik" ? "pakcik" : model == "chr_townaunty" ? "aunty" : model == "chr_kid" ? "kid" : "man");
            return p;
        }

        public static void SpawnFleeing(Vector3 pos, Quaternion rot)
        {
            var zone = WalkZone.FromRect(new Rect(pos.x - 30, pos.z - 30, 60, 60), 0f);
            var p = Spawn(pos, zone, GameManager.I.transform, "chr_townman");
            p.Flee(6f);
            Fx.Word(pos + Vector3.up * 2.2f, "KERETA AKU!");
            VoiceSynth.SayAt(VoiceSynth.Townsperson("man", 1), "man1", "KERETA AKU!", pos + Vector3.up * 1.6f, 1f, true);
            Object.Destroy(p.gameObject, 20f);
        }
    }
}
