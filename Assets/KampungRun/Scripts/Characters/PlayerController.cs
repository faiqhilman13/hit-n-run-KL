using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    public interface IInteractable
    {
        string Prompt { get; }
        Vector3 Position { get; }
        float Range { get; }
        bool CanInteract { get; }
        void Interact(PlayerController p);
    }

    public static class Interactables
    {
        public static readonly List<IInteractable> All = new List<IInteractable>();
    }

    /// <summary>
    /// The playable family member. On foot: walk/run, jump + double jump, a three-hit
    /// punch combo, a kick and a jump-stomp (kick in mid-air). Press E near any car to
    /// get in - if someone's driving it, they get politely (not really) evicted.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController I { get; private set; }

        public CharacterDef def;
        public Vehicle vehicle;
        public bool Driving => vehicle != null;
        public Vector3 Focus => Driving ? vehicle.transform.position : transform.position + Vector3.up;
        public Vector3 Velocity => Driving ? vehicle.Body.linearVelocity : _vel;
        public float Speed => Velocity.magnitude;
        public bool frozen;

        CharacterController _cc;
        CharacterRig _rig;
        GameObject _model;
        Vector3 _vel;
        float _yaw;
        int _jumps;
        float _attackCooldown;
        int _combo;
        float _comboTimer;
        bool _stomping;
        float _knockTime;
        Vector3 _knockVel;
        float _groundedGrace;
        AudioSource _engine;
        IInteractable _focusInteract;
        Vehicle _focusVehicle;

        void Awake()
        {
            I = this;
            _cc = GetComponent<CharacterController>();
            _cc.height = 1.7f;
            _cc.radius = 0.35f;
            _cc.center = new Vector3(0, 0.87f, 0);
            _cc.stepOffset = 0.45f;
            _cc.slopeLimit = 50f;
            gameObject.layer = Layers.Character;
        }

        public void SetCharacter(string id)
        {
            def = GameData.Characters[id];
            if (_model) Destroy(_model);
            // the body hangs off a "juice" pivot that squashes, leans and flips without touching the animation
            if (_juice == null)
            {
                _juice = new GameObject("Juice").transform;
                _juice.SetParent(transform, false);
            }
            ResetJuice();
            _model = ModelFactory.Spawn(def.model, transform.position, transform.rotation, _juice, "Body");
            _model.transform.localPosition = Vector3.zero;
            _model.transform.localRotation = Quaternion.identity;
            var costume = GameData.Costumes.Find(c => c.id == GameState.Costume(id));
            if (costume != null) ModelFactory.Recolor(_model, costume.swaps);
            foreach (var t in _model.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Character;
            _rig = _model.AddComponent<CharacterRig>();
            // the hero is always posed, even for a frame the camera misses (tucked in a car, swinging
            // round) - otherwise the body pops back to whatever pose it last had on screen
            var anim = _model.GetComponentInChildren<Animator>();
            if (anim) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            float h = ModelFactory.LocalBounds(_model).size.y;
            _cc.height = Mathf.Max(1.0f, h * 0.95f);
            _cc.center = new Vector3(0, _cc.height * 0.5f + 0.02f, 0);
            _cc.radius = h < 1.3f ? 0.28f : 0.35f;
            if (_head == null) _head = gameObject.AddComponent<HeadLook>();
            else { Destroy(_head); _head = gameObject.AddComponent<HeadLook>(); }   // re-bind to the new body's bones
            if (!Driving && !Transitioning) FootCamera();
        }

        HeadLook _head;

        /// <summary>
        /// The on-foot camera, measured against Hit &amp; Run: close behind and a little above, looking almost level,
        /// with the character big in the lower part of the frame. Scaled to the character (Adik is small).
        /// </summary>
        void FootCamera(bool snap = false)
        {
            var cam = ChaseCamera.I;
            if (cam == null) return;
            float s = Mathf.Clamp(_cc.height / 1.62f, 0.62f, 1.3f);
            cam.target = transform;
            cam.targetBody = null;
            cam.distance = ChaseCamera.FootDistance * Mathf.Lerp(1f, s, 0.7f);
            cam.height = ChaseCamera.FootHeight * s;
            cam.pitch = ChaseCamera.FootPitch;
            if (snap) cam.SnapBehind();
        }

        public void Teleport(Vector3 pos, Quaternion rot)
        {
            if (Driving) ExitVehicle(false, true);
            if (Transitioning) EndTransition();
            _cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            _yaw = rot.eulerAngles.y;
            _vel = Vector3.zero;
            _stomping = false;
            _stompHang = 0f;
            _airMinVy = 0f;                  // (a fall before the jump isn't a landing after it)
            _launched = false;
            ResetJuice();
            _cc.enabled = true;
            FootCamera(true);
        }

        void Update()
        {
            if (GameManager.I != null && !GameManager.I.Playing) { HUD.I?.Prompt(null); return; }
            float dt = Time.deltaTime;
            if (Transitioning) { UpdateTransition(dt); HUD.I?.Prompt(null); return; }
            if (Driving) UpdateDriving(dt);
            else UpdateOnFoot(dt);
            UpdateInteractFocus();

            // fell in the river (or somewhere silly)? H&R-style: pop back onto the road
            if (Focus.y < -1.4f) _lowTime += dt; else _lowTime = 0f;
            if (_lowTime > 1.5f) RescueToRoad();
        }

        float _lowTime;

        void RescueToRoad()
        {
            _lowTime = 0f;
            var roads = GameManager.I.City.roads;
            var node = roads.Nearest(Focus);
            var next = node.links.Count > 0 ? node.links[0] : node;
            var dir = next.pos - node.pos; dir.y = 0;
            var rot = Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir : Vector3.forward);
            HUD.I?.Toast("Basah kuyup! Dah tarik keluar dari sungai.");
            if (Driving)
            {
                var v = vehicle;
                v.Body.linearVelocity = Vector3.zero;
                v.Body.angularVelocity = Vector3.zero;
                v.Body.position = roads.LanePoint(node.pos, next.pos, 0.2f) + Vector3.up * 1f;
                v.Body.rotation = rot;
                v.transform.SetPositionAndRotation(v.Body.position, rot);
                v.Damage(10f);
            }
            else Teleport(node.pos + Vector3.up * 0.3f, rot);
        }

        // ---------------------------------------------------------------- on foot
        // The feel borrows from the platformers that got it right (Mario 64, Hit & Run): a quick but not
        // instant start and stop, a skid when you turn back on yourself, a lean into corners; jumps you can cut
        // short, a buffered jump button and a moment's grace off a ledge, a faster fall than rise, a big double
        // jump with a flip, and a squash, a puff of dust and a thud when you land.
        const float AccelTime = 0.14f, StopTime = 0.09f, AirControl = 0.5f;
        const float JumpBoost = 1.25f, DoubleBoost = 1.4f;                     // x def.jump
        const float RiseGravity = 1.6f, CutGravity = 2.6f, FallGravity = 2.2f, MaxFall = 26f;
        const float FlipTime = 0.42f, StompHang = 0.2f;

        Transform _juice;
        float _jumpBuffer, _skidT, _stepPhase, _lean, _flipT, _spinT, _squash, _squashV, _airMinVy, _bumpT, _bumpCheck, _stompHang;
        float _idleT, _chatterT = 20f, _gazeT;
        Vector3 _lungeOffset;
        bool _wasGrounded = true, _jumpCut, _launched;
        static readonly Collider[] Near = new Collider[64];

        public bool Grounded { get; private set; }
        public bool Sprinting { get; private set; }
        /// <summary>Which way we're running (zero when standing), for the camera.</summary>
        public Vector3 MoveDir { get; private set; }

        void UpdateOnFoot(float dt)
        {
            bool grounded = _cc.isGrounded;
            Grounded = grounded;
            if (grounded) _groundedGrace = 0.12f; else _groundedGrace -= dt;

            if (_knockTime > 0f)
            {
                _knockTime -= dt;
                _knockVel += Physics.gravity * dt;
                _knockVel.x *= 1f - dt * 2f; _knockVel.z *= 1f - dt * 2f;
                _cc.Move(_knockVel * dt);
                if (_model && !(_rig && _rig.Humanoid)) _model.transform.localRotation = Quaternion.Euler(-80f * Mathf.Clamp01(_knockTime * 2f), 0, 0);
                if (_knockTime <= 0f && _model) _model.transform.localRotation = Quaternion.identity;
                _wasGrounded = grounded;
                return;
            }

            Vector2 input = frozen ? Vector2.zero : GameInput.Move;
            var cam = ChaseCamera.I ? ChaseCamera.I.transform : transform;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 wish = Vector3.ClampMagnitude(fwd * input.y + right * input.x, 1f);
            float stick = wish.magnitude;
            Vector3 dir = stick > 0.001f ? wish / stick : Vector3.zero;

            // full stick jogs, Shift sprints; a light touch walks
            float jog = def.run * 0.9f, dash = def.run * 1.22f;
            bool sprint = !frozen && (GameInput.Sprint || debugSprint) && stick > 0.3f;
            float speed = stick < 0.15f ? 0f : sprint ? dash : Mathf.Lerp(def.walk * 0.45f, jog, Mathf.InverseLerp(0.15f, 1f, stick));
            if (_attackCooldown > 0.15f) speed *= 0.35f;
            if (_bumpT > 0f) { _bumpT -= dt; speed *= 0.55f; }

            var flatVel = new Vector3(_vel.x, 0f, _vel.z);
            float cur = flatVel.magnitude;
            Sprinting = sprint && grounded && cur > jog * 0.9f;

            // turning back on yourself at a run: skid to a stop in a puff of dust, then off the other way
            if (grounded && _skidT <= 0f && cur > 4.5f && dir != Vector3.zero && Vector3.Dot(flatVel / cur, dir) < -0.5f)
            {
                _skidT = 0.2f;
                ProcAudio.Play(ProcAudio.Skid, transform.position, 0.3f, Random.Range(0.9f, 1.1f));
            }
            Vector3 target;
            float rate;
            if (_skidT > 0f)
            {
                _skidT -= dt;
                target = Vector3.zero;
                rate = jog / 0.18f;
                if (Random.value < dt * 30f) Fx.Dust(transform.position + flatVel * 0.05f);
            }
            else
            {
                target = dir * speed;
                bool faster = target.sqrMagnitude > flatVel.sqrMagnitude;
                rate = faster ? Mathf.Max(speed, jog) / AccelTime : jog / StopTime;
                if (!grounded) rate *= AirControl;
            }
            flatVel = Vector3.MoveTowards(flatVel, target, rate * dt);
            _vel.x = flatVel.x;
            _vel.z = flatVel.z;

            // face the way we're going, leaning into the turn
            float prevYaw = _yaw;
            if (_skidT <= 0f && dir != Vector3.zero)
                _yaw = Mathf.MoveTowardsAngle(_yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, (grounded ? 900f : 540f) * dt);
            transform.rotation = Quaternion.Euler(0, _yaw, 0);
            float yawRate = Mathf.DeltaAngle(prevYaw, _yaw) / Mathf.Max(dt, 1e-4f);
            _lean = Mathf.Lerp(_lean, grounded ? Mathf.Clamp(-yawRate * flatVel.magnitude * 0.0028f, -14f, 14f) : 0f, dt * 10f);
            MoveDir = flatVel.sqrMagnitude > 0.25f ? flatVel.normalized : Vector3.zero;

            // jumping: a buffered press, ledge grace, cut-short hops, and the big flipping double jump
            if (!frozen && GameInput.JumpDown) _jumpBuffer = 0.15f; else _jumpBuffer -= dt;
            _debugHold -= dt;
            bool held = !frozen && (GameInput.Handbrake || _debugHold > 0f);    // (the jump button, held)
            if (grounded && _vel.y < 0)
            {
                if (!_wasGrounded) Land(-_airMinVy);
                if (_stomping) { Stomp(); _vel.y = 4.5f; _jumps = 1; }            // the slam bounces you up a little
                else { _vel.y = -2f; _jumps = 0; }
                _airMinVy = 0f;
            }
            if (_jumpBuffer > 0f && !_stomping && _stompHang <= 0f && (_groundedGrace > 0f || _jumps < 2))
            {
                _jumpBuffer = 0f;
                bool first = _groundedGrace > 0f;
                _vel.y = def.jump * (first ? JumpBoost : DoubleBoost);
                _jumps = first ? 1 : 2;
                _jumpCut = !first;                                                // the double jump always goes full height
                _groundedGrace = 0f;
                if (first)
                {
                    _squash = -0.16f;                                             // stretch up off the ground
                    ProcAudio.Play(ProcAudio.Whoosh, transform.position, 0.35f, 1f);
                    if (flatVel.sqrMagnitude > 4f) Fx.Dust(transform.position);
                }
                else
                {
                    _flipT = FlipTime;
                    Quip(Joy, 0.3f);
                    Fx.Ring(transform.position, 2.2f, 8, 0.28f);
                    ProcAudio.Play(ProcAudio.Whoosh, transform.position, 0.45f, 1.35f);
                }
            }
            if (!held && !_jumpCut && _vel.y > 0f && _jumps > 0) { _jumpCut = true; _vel.y *= 0.55f; }
            if (_stompHang > 0f)
            {
                // the ground pound: hang in the air for a beat, spinning, then slam down
                _stompHang -= dt;
                _vel = Vector3.zero;
                if (_stompHang <= 0f) { _stomping = true; _vel.y = -20f; }
            }
            else
            {
                if (_vel.y <= 0f) _launched = false;
                float g = _stomping ? 3.2f : _vel.y > 0f ? (held || _launched ? RiseGravity : CutGravity) : FallGravity;
                _vel.y = Mathf.Max(_vel.y + Physics.gravity.y * g * dt, -MaxFall);
            }
            if (!grounded) _airMinVy = Mathf.Min(_airMinVy, _vel.y);
            _wasGrounded = grounded;
            _cc.Move(_vel * dt);

            Footsteps(dt, grounded, flatVel.magnitude);
            UpdateJuice(dt);

            // attacks
            _attackCooldown -= dt;
            _quipT -= dt;
            _comboTimer -= dt;
            if (_comboTimer <= 0f) _combo = 0;
            if (!frozen && _attackCooldown <= 0f && _stompHang <= 0f)
            {
                if (GameInput.PunchDown) Punch();
                else if (GameInput.KickDown)
                {
                    if (!grounded && _groundedGrace <= 0f) { if (!_stomping) BeginStomp(); }
                    else Kick();
                }
            }

            BumpPeople(flatVel);
            Gaze(dt);
            Chatter(dt, flatVel.magnitude, grounded);

            if (_rig)
            {
                _rig.speed = flatVel.magnitude;
                _rig.grounded = grounded || _groundedGrace > 0f;
            }

            if (transform.position.y < -12f) GameManager.I.Respawn();
        }

        /// <summary>Footfalls in time with the stride; a scuff of dust when sprinting.</summary>
        void Footsteps(float dt, bool grounded, float speed)
        {
            if (!grounded || speed < 0.8f) { _stepPhase = 0.6f; return; }
            _stepPhase += speed * dt / (speed > 5f ? 1.55f : 1.0f);
            if (_stepPhase < 1f) return;
            _stepPhase -= 1f;
            ProcAudio.Play(ProcAudio.Step, transform.position, Mathf.Lerp(0.1f, 0.26f, speed / 9f), Random.Range(0.85f, 1.15f));
            if (Sprinting && Random.value < 0.6f) Fx.Dust(transform.position - MoveDir * 0.3f);
        }

        /// <summary>Touch down: the bigger the drop, the bigger the squash, the dust and the thud.</summary>
        void Land(float impact)
        {
            if (impact < 3.5f) return;
            float k = Mathf.Clamp01((impact - 3.5f) / 14f);
            _squash = 0.1f + k * 0.28f;
            _squashV = 0f;
            ProcAudio.Play(ProcAudio.Land, transform.position, 0.22f + k * 0.5f, Random.Range(0.92f, 1.08f));
            if (k > 0.3f) Fx.Ring(transform.position, 1.6f + k * 3f, 6 + (int)(k * 8), 0.3f + k * 0.15f);
            else { Fx.Dust(transform.position + transform.right * 0.25f); Fx.Dust(transform.position - transform.right * 0.25f); }
            if (k > 0.55f) { ChaseCamera.I?.Shake(0.1f + k * 0.15f); Quip(Ouch, 0.5f); }
        }

        /// <summary>Squash and stretch (a spring), the lean, the double-jump flip and the ground-pound spin.</summary>
        void UpdateJuice(float dt)
        {
            if (_juice == null) return;
            _squashV += (-_squash * 320f - _squashV * 20f) * dt;
            _squash += _squashV * dt;
            float sq = Mathf.Clamp(_squash, -0.3f, 0.4f);
            _juice.localScale = new Vector3(1f + sq * 0.5f, 1f - sq, 1f + sq * 0.5f);
            float pitch = 0f, spin = 0f;
            if (_flipT > 0f)
            {
                _flipT -= dt;
                float k = 1f - Mathf.Clamp01(_flipT / FlipTime);
                pitch = (1f - (1f - k) * (1f - k)) * 360f;                         // ease-out forward flip
            }
            if (_spinT > 0f)
            {
                _spinT -= dt;
                spin = (1f - Mathf.Clamp01(_spinT / StompHang)) * 360f;
            }
            var rot = Quaternion.Euler(pitch, spin, _lean);
            // turn about the middle of the body, not the feet
            var mid = Vector3.up * _cc.height * 0.5f;
            _lungeOffset = Vector3.Lerp(_lungeOffset, Vector3.zero, 1f - Mathf.Exp(-dt * 16f));
            _juice.localRotation = rot;
            _juice.localPosition = mid - rot * mid + transform.InverseTransformVector(_lungeOffset);
        }

        void ResetJuice()
        {
            _squash = _squashV = _lean = _flipT = _spinT = 0f;
            _lungeOffset = Vector3.zero;
            if (_juice == null) return;
            _juice.localPosition = Vector3.zero;
            _juice.localRotation = Quaternion.identity;
            _juice.localScale = Vector3.one;
        }

        public void DebugPunch(int times) { for (int i = 0; i < times; i++) Punch(); }
        public void DebugKick() => Kick();
        /// <summary>Tests and captures: press jump (held for a full-height jump), ground-pound, sprint.</summary>
        public void DebugJump() { _jumpBuffer = 0.15f; _debugHold = 0.45f; }
        public void DebugStomp() { if (!_cc.isGrounded && !_stomping && _stompHang <= 0f) BeginStomp(); }
        public bool debugSprint;
        float _debugHold;

        void Punch()
        {
            _combo = (_combo % 3) + 1;
            _comboTimer = 0.6f;
            _attackCooldown = _combo == 3 ? 0.45f : 0.22f;
            Lunge();
            _rig?.Punch(_combo);
            // third hit is the big one
            Attack(transform.position + Vector3.up * 1.1f + transform.forward * 0.8f, 0.85f, _combo == 3 ? 11f : 6f, _combo == 3 ? 18f : 8f, _combo == 3);
        }

        void Kick()
        {
            _attackCooldown = 0.4f;
            Lunge();
            _rig?.Kick();
            Attack(transform.position + Vector3.up * 0.6f + transform.forward * 0.9f, 1f, 13f, 14f, true);
        }

        void BeginStomp()
        {
            _stompHang = StompHang;
            _spinT = StompHang;
            _flipT = 0f;
            _vel = Vector3.zero;
            ProcAudio.Play(ProcAudio.Whoosh, transform.position, 0.5f, 0.7f);
        }

        void Stomp()
        {
            _stomping = false;
            _attackCooldown = 0.3f;
            _squash = 0.35f;
            _squashV = 0f;
            Fx.Ring(transform.position, 6f, 16, 0.5f);
            ChaseCamera.I?.Shake(0.45f);
            Fx.Word(transform.position + Vector3.up * 2f, "DEBUK!");
            Quip(Fight, 0.3f);
            ProcAudio.Play(ProcAudio.Land, transform.position, 0.9f, 0.75f);
            HitStop.Do(0.06f);
            Attack(transform.position + Vector3.up * 0.4f, 3f, 12f, 20f, true);
            Pedestrian.React(transform.position, 14f, Pedestrian.Stir.Commotion);
            Pigeons.Startle(transform.position, 16f);
        }

        /// <summary>
        /// Lock on, Arkham-style: turn to face the best thing to hit in front of you (people, smashables, birds,
        /// animals, toys) and, if it's just out of reach, close the gap. The body jumps forward at once so the
        /// hit lands this frame; the model eases after it so it reads as a lunge, not a teleport.
        /// </summary>
        void Lunge()
        {
            var t = FindTarget(2.8f, 70f, out var at);
            if (t == null) return;
            var to = at - transform.position;
            to.y = 0f;
            float d = to.magnitude;
            if (d < 0.05f) return;
            _yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, _yaw, 0);
            if (d > 1.15f && _cc.enabled)
            {
                var before = transform.position;
                _cc.Move(to / d * Mathf.Min(d - 1.0f, 1.3f));
                _lungeOffset += before - transform.position;
            }
        }

        Transform FindTarget(float range, float cone, out Vector3 at)
        {
            at = default;
            Vector2 input = GameInput.Move;
            var camT = ChaseCamera.I ? ChaseCamera.I.transform : transform;
            var f = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
            var face = input.sqrMagnitude > 0.04f ? (f * input.y + Vector3.Cross(Vector3.up, f) * input.x).normalized : transform.forward;
            int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up, range, Near, ~0, QueryTriggerInteraction.Collide);
            Transform best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = Near[i];
                if (c.transform.IsChildOf(transform)) continue;
                Component hit = c.GetComponentInParent<Pedestrian>();
                float bias = 0f;
                if (hit == null) hit = c.GetComponentInParent<Breakable>();
                if (hit == null) hit = c.GetComponentInParent<BurungKamera>();
                if (hit == null) hit = c.GetComponentInParent<Critter>();
                if (hit == null && c.attachedRigidbody && !c.attachedRigidbody.isKinematic && c.attachedRigidbody.mass < 30f) { hit = c.attachedRigidbody; bias = 0.5f; }
                if (hit == null) continue;
                var to = hit.transform.position - transform.position;
                to.y = 0f;
                float d = to.magnitude;
                float ang = d > 0.01f ? Vector3.Angle(face, to) : 0f;
                if (ang > cone) continue;
                float score = d + ang * 0.025f + bias;
                if (score < bestScore) { bestScore = score; best = hit.transform; }
            }
            if (best != null) at = best.position;
            return best;
        }

        void Attack(Vector3 center, float radius, float force, float damage, bool heavy = false)
        {
            bool hitSomething = false;
            var seen = new HashSet<Object>();
            int n = Physics.OverlapSphereNonAlloc(center, radius, Near, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = Near[i];
                if (c.transform.IsChildOf(transform)) continue;
                var dir = (c.transform.position - transform.position); dir.y = 0; dir.Normalize();

                var ped = c.GetComponentInParent<Pedestrian>();
                if (ped) { if (seen.Add(ped)) { ped.Hit(dir, force, true); hitSomething = true; } continue; }
                var br = c.GetComponentInParent<Breakable>();
                if (br) { if (seen.Add(br)) { br.Hit(transform.position, force, true); hitSomething = true; } continue; }
                var cam = c.GetComponentInParent<BurungKamera>();
                if (cam) { if (seen.Add(cam)) { cam.Smash(); hitSomething = true; HitStop.Do(0.06f); } continue; }
                var critter = c.GetComponentInParent<Critter>();
                if (critter) { if (seen.Add(critter)) { critter.Kicked(dir, force); hitSomething = true; if (critter.isChicken) Quip(Sorry, 0.35f); } continue; }
                var body = c.attachedRigidbody;
                var v = body ? body.GetComponent<Vehicle>() : null;
                if (v)
                {
                    if (!seen.Add(v)) continue;
                    v.Body.AddForceAtPosition(dir * force * 120f, center, ForceMode.Impulse);
                    v.Damage(damage * 0.25f);
                    SamanMeter.I?.AddHeat(v.role == VehicleRole.Police ? 25f : 3f);
                    hitSomething = true;
                }
                else if (body && !body.isKinematic && seen.Add(body))
                {
                    // loose things (balls, toppled lamps, flung doors) fly off the boot
                    body.AddForce((dir * 0.55f + Vector3.up * 0.32f) * force * Mathf.Clamp(body.mass, 0.4f, 8f), ForceMode.Impulse);
                    hitSomething = true;
                }
            }
            if (hitSomething)
            {
                ProcAudio.Play(ProcAudio.Punch, center, 0.8f, Random.Range(0.85f, 1.15f));
                if (heavy) ProcAudio.Play(ProcAudio.Thwack, center, 0.6f, Random.Range(0.9f, 1.1f));
                Fx.Stars(center + transform.forward * 0.25f, heavy ? 10 : 6, heavy ? 9f : 6f);
                Fx.Word(center + Vector3.up * 0.8f);
                ChaseCamera.I?.Shake(heavy ? 0.26f : 0.15f);
                HitStop.Do(heavy ? 0.085f : 0.045f);
                if (heavy) Quip(Fight, 0.2f);
            }
            else ProcAudio.Play(ProcAudio.Whoosh, center, 0.3f, 1.4f);
        }

        /// <summary>Run into someone and they stumble aside and tell you off (nobody is a ghost).</summary>
        void BumpPeople(Vector3 flatVel)
        {
            float speed = flatVel.magnitude;
            if (speed < 2.2f || (_bumpCheck -= Time.deltaTime) > 0f) return;
            _bumpCheck = 0.08f;
            var ahead = transform.position + Vector3.up * 0.9f + flatVel / speed * 0.45f;
            int n = Physics.OverlapSphereNonAlloc(ahead, 0.45f, Near, 1 << Layers.Character, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var ped = Near[i].GetComponentInParent<Pedestrian>();
                if (ped != null && ped.Bump(flatVel / speed, speed))
                {
                    _bumpT = 0.3f;
                    ProcAudio.Play(ProcAudio.Punch, ahead, 0.25f, 0.6f);
                    break;
                }
            }
        }

        /// <summary>Glance at whoever is close by (people and the cast), the way anyone walking down a street does.</summary>
        void Gaze(float dt)
        {
            if (_head == null || (_gazeT -= dt) > 0f) return;
            _gazeT = Random.Range(0.4f, 0.8f);
            Transform best = null;
            float bd = 5.5f * 5.5f;
            foreach (var p in Pedestrian.All)
            {
                if (p == null) continue;
                var to = p.transform.position - transform.position;
                float d = to.sqrMagnitude;
                if (d < bd && Vector3.Dot(to, transform.forward) > 0f) { bd = d; best = p.transform; }
            }
            if (best != null) _head.LookAt(best); else _head.Clear();
        }

        static readonly string[] IdleLines = { "Hmm...", "Panas betul KL ni.", "Lapar pulak...", "Nak buat apa ni?", "Hmm, mana nak pergi?" };

        // the heroes talk while they play, the way Hit & Run's cast never stop quipping: a few words each,
        // in their own voice, now and then (never every time, never two on top of each other)
        static readonly Dictionary<string, string[]> JoyLines = new Dictionary<string, string[]>
        {
            ["pakmat"] = new[] { "Hup!", "Masih kuat lagi!", "Hehe!" },
            ["maksom"] = new[] { "Hup!", "Eh, boleh lagi!", "Wah!" },
            ["along"] = new[] { "Steady!", "Yeehaa!", "Senang je!" },
            ["adik"] = new[] { "Yeay!", "Wiii!", "Hehehe!" },
            ["aiman"] = new[] { "Fuh!", "Power!", "Jom!" },
        };
        static readonly Dictionary<string, string[]> OuchLines = new Dictionary<string, string[]>
        {
            ["pakmat"] = new[] { "Adoi, pinggang...", "Ya Allah!", "Uish!" },
            ["maksom"] = new[] { "Astaghfirullah!", "Aduh, lutut...", "Uish!" },
            ["along"] = new[] { "Uish, sakit!", "Takpe, takpe.", "Adoi!" },
            ["adik"] = new[] { "Aduh!", "Huhu...", "Tak sakit pun!" },
            ["aiman"] = new[] { "Adoi!", "Uish!", "Okay, okay..." },
        };
        static readonly string[] FightLines = { "Ambik ni!", "Rasakan!", "Hiyaa!", "Jangan main-main!" };
        static readonly string[] SorryLines = { "Maaf, ayam!", "Sorry, sorry!", "Eh, tak sengaja!" };
        enum QuipKind { Joy, Ouch, Fight, Sorry }
        const QuipKind Joy = QuipKind.Joy, Ouch = QuipKind.Ouch, Fight = QuipKind.Fight, Sorry = QuipKind.Sorry;
        float _quipT;

        void Quip(QuipKind kind, float chance)
        {
            if (_quipT > 0f || def == null || Random.value > chance) return;
            string[] lines = kind == QuipKind.Fight ? FightLines : kind == QuipKind.Sorry ? SorryLines
                : (kind == QuipKind.Joy ? JoyLines : OuchLines).TryGetValue(def.id, out var own) ? own : kind == QuipKind.Joy ? JoyLines["pakmat"] : OuchLines["pakmat"];
            if (Barks.Say(transform, def.name, lines[Random.Range(0, lines.Length)], false, 2.1f, 0.8f)) _quipT = 4f;
        }

        /// <summary>Left standing a while, the hero mutters to themselves (once in a while, never in a mission dialogue).</summary>
        void Chatter(float dt, float speed, bool grounded)
        {
            _chatterT -= dt;
            if (speed > 0.3f || !grounded) { _idleT = 0f; return; }
            _idleT += dt;
            if (_idleT < 9f || _chatterT > 0f) return;
            if (Barks.Say(transform, def.name, IdleLines[Random.Range(0, IdleLines.Length)], true, 2.1f, 0.7f)) _chatterT = 30f;
        }

        /// <summary>Hit by a car: tumble and drop some coins, like H&amp;R.</summary>
        public void Knockdown(Vector3 impulse)
        {
            if (Driving || Transitioning || _knockTime > 0f) return;
            _knockTime = 1.2f;
            _knockVel = impulse;
            _stomping = false;
            _stompHang = 0f;
            ResetJuice();
            _rig?.Tumble(1.2f);
            ProcAudio.Play(ProcAudio.Aduh, transform.position, 0.8f);
            Fx.Word(transform.position + Vector3.up * 2f, "ADUH!");
            int drop = Mathf.Min(GameState.Coins, 5);
            GameState.AddCoins(-drop);
            for (int i = 0; i < drop; i++) Pickup.SpawnCoin(transform.position + Vector3.up, true);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // trampolines (awnings, market canopies): land on one and it fires you up, Mario-style
            if (!Driving && hit.normal.y > 0.6f && _vel.y < -1.5f)
            {
                var bouncy = hit.collider.GetComponentInParent<Bouncy>();
                if (bouncy != null)
                {
                    _vel.y = bouncy.power;
                    _jumps = 1;                                                    // a double jump still to come
                    _jumpCut = true;
                    _launched = true;                                              // the full height, button or not
                    _airMinVy = 0f;
                    _stomping = false;
                    _squash = -0.2f;
                    _groundedGrace = 0f;
                    bouncy.Boing();
                    Quip(Joy, 0.3f);
                    return;
                }
            }
            // push loose things around (a ball at your feet gets dribbled along)
            var rb = hit.rigidbody;
            if (rb && !rb.isKinematic && rb.GetComponent<Vehicle>() == null && hit.moveDirection.y > -0.3f)
                rb.AddForce(hit.moveDirection * (rb.mass < 2f ? 1.6f + new Vector2(_vel.x, _vel.z).magnitude * 0.35f : 3f), ForceMode.Impulse);
            // speeding car hits us
            var v = rb ? rb.GetComponent<Vehicle>() : null;
            if (v && v.Body.linearVelocity.magnitude > 7f)
            {
                var dir = v.Body.linearVelocity.normalized;
                Knockdown(dir * v.Body.linearVelocity.magnitude * 0.7f + Vector3.up * 6f);
            }
        }

        // ---------------------------------------------------------------- interaction
        void UpdateInteractFocus()
        {
            _focusInteract = null;
            _focusVehicle = null;
            if (frozen) { HUD.I?.Prompt(null); return; }
            if (Driving)
            {
                HUD.I?.Prompt(null);
                if (GameInput.InteractDown && vehicle.ForwardSpeed < 8f) ExitVehicle(false);
                return;
            }
            float best = float.MaxValue;
            foreach (var it in Interactables.All)
            {
                if (it == null || !it.CanInteract) continue;
                float d = Vector3.Distance(it.Position, transform.position);
                if (d < it.Range && d < best) { best = d; _focusInteract = it; }
            }
            if (_focusInteract == null)
            {
                foreach (var c in Physics.OverlapSphere(transform.position, 3.8f, 1 << Layers.Vehicle))
                {
                    var v = c.attachedRigidbody ? c.attachedRigidbody.GetComponent<Vehicle>() : null;
                    if (v == null || v.Wrecked || v.role == VehicleRole.Police || v.noEnter) continue;
                    float d = Vector3.Distance(v.transform.position, transform.position);
                    if (d < best) { best = d; _focusVehicle = v; }
                }
            }
            if (_focusInteract != null) HUD.I?.Prompt($"[E] {_focusInteract.Prompt}");
            else if (_focusVehicle != null) HUD.I?.Prompt($"[E] Naik {_focusVehicle.displayName}");
            else HUD.I?.Prompt(null);

            if (GameInput.InteractDown)
            {
                if (_focusInteract != null) _focusInteract.Interact(this);
                else if (_focusVehicle != null) EnterVehicle(_focusVehicle);
            }
        }

        // ---------------------------------------------------------------- getting in / out
        // H&R teleported you; we walk to the door (right-hand drive), duck in, or swing a leg
        // over the bike. Wrecks throw you out in an arc and knock you flat.
        // Cars (with a seat) get the full sequence, the way people really get in: walk to the door,
        // turn round as it swings open, back down onto the edge of the seat, then swing the legs in
        // and face the road; getting out mirrors it (swing out, stand up, step away).
        enum Trans { None, ToDoor, Boarding, Alighting, CarTurn, CarSit, CarSwingIn, CarSwingOut, CarStand }
        float _yawFrom, _yawTo;
        Vector3 _doorOutside, _doorEdge;

        bool Hollow(Vehicle v) => !v.TwoWheeler && v.Visuals != null && v.Visuals.Seat != null;
        SeatFit _fit;
        SeatFit Fit(Vehicle v, float w)
        {
            if (_fit == null) { _fit = GetComponent<SeatFit>(); if (_fit == null) _fit = gameObject.AddComponent<SeatFit>(); }
            _fit.seat = v.Visuals != null ? v.Visuals.Seat : null;
            _fit.weight = w;
            return _fit;
        }
        float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        Vector3 EdgeRoot(Vehicle v) => _doorEdge - v.transform.up * SitDrop;

        void Step(Trans next, float dur, Vector3 to, float yawTo)
        {
            _trans = next;
            _transT = 0f;
            _transDur = dur;
            _transFrom = transform.position;
            _transTo = to;
            _yawFrom = transform.eulerAngles.y;
            _yawTo = yawTo;
        }
        Trans _trans;
        Vehicle _transVehicle;
        float _transT, _transDur;
        Vector3 _transFrom, _transTo;
        Quaternion _transRotFrom, _transRotTo;
        public bool Transitioning => _trans != Trans.None;

        Vector3 DoorPoint(Vehicle v, float side = 1f) =>
            v.transform.position + v.transform.right * side * (v.TwoWheeler ? 0.75f : 1.45f) - v.transform.forward * (v.TwoWheeler ? 0.2f : 0.1f);

        // riders stay on show; so do drivers of the Hit & Run cars (they have a seat), SHAR-style
        bool Visible(Vehicle v) => v.TwoWheeler || (v.Visuals != null && v.Visuals.Seat != null);
        const float SitDrop = 0.6f;   // seat marker (hip height) -> character root in the sitting clip
        public const float RideDrop = 0.5f;  // bike saddle top -> character root in the riding clip
        int _doorSide = 1;

        /// <summary>Where the character's root goes when sitting in / riding v (world space).</summary>
        Vector3 SeatPoint(Vehicle v)
        {
            if (v.Visuals != null && v.Visuals.Seat != null)
                return v.Visuals.Seat.position - v.transform.up * (v.TwoWheeler ? RideDrop : SitDrop);
            return v.transform.TransformPoint(Visible(v) ? v.seatLocal : Vector3.up * 0.5f);
        }

        public void EnterVehicle(Vehicle v, bool instant = false)
        {
            if (v == null || Driving || Transitioning) return;
            ResetJuice();
            _head?.Clear();
            _stomping = false;
            _stompHang = 0f;
            if (v.role == VehicleRole.Traffic || v.role == VehicleRole.MissionTarget)
            {
                // carjack! the driver hops out and runs off yelling
                var seated = v.Visuals != null && v.Visuals.Seat != null ? v.Visuals.Seat.Find("Driver") : null;
                if (seated) Destroy(seated.gameObject);
                v.Visuals?.OpenDoor(1, true, 0.8f);
                var side = v.transform.right * 2.2f;
                PedestrianSpawner.SpawnFleeing(v.transform.position + side, v.transform.rotation);
                SamanMeter.I?.AddHeat(20f);
                v.role = VehicleRole.Parked;
                v.driver = null;
            }
            if (instant) { CommitEnter(v); return; }
            _transVehicle = v;
            v.driver = null;
            if (Hollow(v))
            {
                v.Visuals.DoorPoints(1, out var outR, out var edgeR);
                v.Visuals.DoorPoints(-1, out var outL, out var edgeL);
                _doorSide = Vector3.Distance(outL, transform.position) + 0.5f < Vector3.Distance(outR, transform.position) ? -1 : 1;
                _doorOutside = _doorSide > 0 ? outR : outL;
                _doorEdge = _doorSide > 0 ? edgeR : edgeL;
                var dest = _doorOutside;
                dest.y = transform.position.y;
                _transFrom = transform.position;
                _transTo = dest;
                _transRotFrom = transform.rotation;
                var toCar = Vector3.ProjectOnPlane(-v.transform.right * _doorSide, Vector3.up);
                _transRotTo = Quaternion.LookRotation(toCar);
                _transDur = Mathf.Clamp(Vector3.Distance(_transFrom, _transTo) / def.walk, 0.15f, 0.9f);
                _transT = 0f;
                _trans = Trans.ToDoor;
                _cc.enabled = false;
                _vel = Vector3.zero;
                return;
            }
            // pick the nearer side, the driver's (right) side unless the other is clearly closer
            var door = DoorPoint(v, 1f);
            _doorSide = 1;
            if (Vector3.Distance(DoorPoint(v, -1f), transform.position) + 0.5f < Vector3.Distance(door, transform.position)) { door = DoorPoint(v, -1f); _doorSide = -1; }
            door.y = transform.position.y;
            _transFrom = transform.position;
            _transTo = door;
            _transRotFrom = transform.rotation;
            var look = Vector3.ProjectOnPlane(v.transform.position - door, Vector3.up).normalized + Vector3.ProjectOnPlane(v.transform.forward, Vector3.up) * 0.8f;
            _transRotTo = Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : transform.forward);
            _transDur = Mathf.Clamp(Vector3.Distance(_transFrom, _transTo) / def.walk, 0.12f, 0.7f);
            _transT = 0f;
            _trans = Trans.ToDoor;
            _cc.enabled = false;
            _vel = Vector3.zero;
        }

        void CommitEnter(Vehicle v)
        {
            _trans = Trans.None;
            _transVehicle = null;
            vehicle = v;
            v.role = VehicleRole.Player;
            _knockTime = 0f;                      // a knockdown still playing out ends once seated
            v.driver = new PlayerDriver();
            _cc.enabled = false;
            transform.SetParent(v.transform, true);
            if (Visible(v) && !v.TwoWheeler)
            {
                // sit at the wheel, parented to the seat so we lean and bounce with the body
                transform.SetParent(v.Visuals.Seat, true);
                transform.SetPositionAndRotation(SeatPoint(v), v.transform.rotation);
                if (_model) _model.SetActive(true);
                if (_rig) { _rig.riding = false; _rig.sitting = true; _rig.speed = 0; _rig.SettleInSeat(false); }
                Fit(v, 1f);
                v.Visuals.CloseDoor(_doorSide, true, 0.2f);
            }
            else if (Visible(v))
            {
                // ride on the part of the bike that leans (its saddle, or the leaning model root), so
                // the rider tips into corners with it instead of staying bolt upright
                transform.SetLocalPositionAndRotation(v.seatLocal, Quaternion.identity);
                var lean = v.Visuals != null && v.Visuals.Seat != null ? v.Visuals.Seat : v.transform.Find("Model");
                if (v.Visuals != null && v.Visuals.Seat != null)
                    transform.SetPositionAndRotation(SeatPoint(v), v.transform.rotation);
                if (lean) transform.SetParent(lean, true);
                if (_model) _model.SetActive(true);
                if (_rig) { _rig.riding = true; _rig.sitting = true; _rig.speed = 0; _rig.SettleInSeat(true); }
            }
            else
            {
                transform.SetLocalPositionAndRotation(Vector3.up * 0.5f, Quaternion.identity);
                if (_model) _model.SetActive(false);
            }
            if (_model) _model.transform.localScale = Vector3.one;
            var cam = ChaseCamera.I;
            cam.target = v.transform;
            cam.targetBody = v.Body;
            // Hit & Run framing: about 5 m off the back of the car, aimed a little below its roof, so it sits in
            // the lower part of the frame (ChaseCamera raises the aim and tips down the road with speed)
            var box = v.GetComponent<BoxCollider>();
            float top = box ? box.center.y + box.size.y * 0.5f : 1.5f;
            cam.distance = v.halfLength + 4.9f;
            cam.height = top * 0.86f;
            _engine = ProcAudio.Loop(ProcAudio.Engine, v.transform, 0.18f);
            ProcAudio.Play(ProcAudio.Blip, v.transform.position, 0.3f);
            GameManager.I.OnPlayerEnteredVehicle(v);
        }

        /// <summary>Get out. forced + wrecked = thrown clear; forced alone (busted) still steps out.</summary>
        public void ExitVehicle(bool forced, bool instant = false)
        {
            var v = vehicle;
            if (v == null) return;
            bool bail = forced && v.Wrecked && !instant;
            Vector3 seatWorld = SeatPoint(v);
            vehicle = null;
            transform.SetParent(null, true);
            float sideSign = 1f;
            // Malaysian cars are right-hand drive: hop out on the right
            Vector3 exit = DoorPoint(v, 1f) + Vector3.up * 0.3f;
            if (Blocked(exit)) { exit = DoorPoint(v, -1f) + Vector3.up * 0.3f; sideSign = -1f; }
            if (Blocked(exit)) exit = v.transform.position + Vector3.up * 2.6f;
            _yaw = v.transform.eulerAngles.y;
            var rot = Quaternion.Euler(0, _yaw, 0);
            bool hollowOut = !bail && !instant && Hollow(v);
            if (!hollowOut && _fit) _fit.Clear();
            if (_model) { _model.SetActive(true); _model.transform.localScale = Vector3.one; }
            if (_rig) { _rig.riding = false; _rig.sitting = hollowOut; }
            v.role = VehicleRole.Parked;
            v.driver = null;
            if (_engine) Destroy(_engine.gameObject);
            ResetJuice();
            FootCamera();

            if (bail)
            {
                // thrown out of the wreck: launch from the seat, tumble, land flat
                transform.SetPositionAndRotation(seatWorld + v.transform.right * sideSign * 0.6f + Vector3.up * 0.4f, rot);
                _cc.enabled = true;
                _knockTime = 1.4f;
                _knockVel = v.Body.linearVelocity * 0.45f + v.transform.right * sideSign * 5f + Vector3.up * 7.5f;
                _rig?.Tumble(1.4f);
                VoiceSynth.Bark(def.name, "Alamak", transform.position);
                Fx.Word(transform.position + Vector3.up * 2f, "ALAMAK!");
                ChaseCamera.I?.Shake(0.35f);
                if (!v.TwoWheeler) FlingDoor(v, sideSign);
            }
            else if (instant)
            {
                transform.SetPositionAndRotation(exit, rot);
                _vel = v.Body.linearVelocity * 0.3f;
                _cc.enabled = true;
            }
            else if (hollowOut)
            {
                // the driver's (right-hand) door, or the other one if something's in the way
                int side = sideSign > 0 ? 1 : -1;
                v.Visuals.DoorPoints(side, out _doorOutside, out _doorEdge);
                _doorSide = side;
                transform.SetPositionAndRotation(seatWorld, v.transform.rotation);
                _transVehicle = v;
                _cc.enabled = false;
                _vel = Vector3.zero;
                v.Visuals.OpenDoor(side, true, 1.6f);
                ProcAudio.Play(ProcAudio.Blip, v.transform.position, 0.25f, 0.8f);
                // swing the legs out toward the door, turning to face it
                Step(Trans.CarSwingOut, 0.5f, EdgeRoot(v), v.transform.eulerAngles.y + 90f * side);
                _transT = -0.18f;                                            // let the door get going first
            }
            else
            {
                // climb out: from the seat to the kerb side
                _transFrom = seatWorld;
                _transTo = exit;
                _transRotFrom = rot;
                var look = Vector3.ProjectOnPlane(exit - v.transform.position, Vector3.up).normalized * 0.6f + v.transform.forward;
                _transRotTo = Quaternion.LookRotation(Vector3.ProjectOnPlane(look, Vector3.up));
                transform.SetPositionAndRotation(_transFrom, rot);
                _transDur = v.TwoWheeler ? 0.55f : 0.45f;
                _transT = 0f;
                _trans = Trans.Alighting;
                _transVehicle = v;
                _cc.enabled = false;
                _vel = Vector3.zero;
                if (!Visible(v) && _model) _model.transform.localScale = new Vector3(1f, 0.55f, 1f);
                v.Visuals?.OpenDoor(sideSign > 0 ? 1 : -1, true, 0.75f);
                _rig?.Trigger("Dismount");
                ProcAudio.Play(ProcAudio.Blip, v.transform.position, 0.25f, 0.8f);
            }
            GameManager.I.OnPlayerExitedVehicle(v);
        }

        bool Blocked(Vector3 p) => Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.4f, 0.3f,
            ~(1 << Layers.Pickup | 1 << Layers.Character), QueryTriggerInteraction.Ignore);

        void FlingDoor(Vehicle v, float side)
        {
            // KL vehicles have real hinged doors: the driver's (right-hand drive) door tears off
            var real = ModelFactory.Find(v.gameObject, side > 0 ? "Door_FR" : "Door_FL");
            if (real != null && real.GetComponent<MeshFilter>())
            {
                real.SetParent(null, true);
                real.gameObject.layer = 0;
                var mf = real.GetComponent<MeshFilter>().sharedMesh;
                var bc = real.gameObject.AddComponent<BoxCollider>();
                bc.center = mf.bounds.center; bc.size = mf.bounds.size;
                var body = real.gameObject.AddComponent<Rigidbody>();
                body.mass = 20f;
                body.linearVelocity = v.Body.linearVelocity * 0.5f + v.transform.right * side * 7f + Vector3.up * 5f;
                body.angularVelocity = new Vector3(Random.Range(-8f, 8f), Random.Range(-4f, 4f), Random.Range(-8f, 8f));
                ProcAudio.Play(ProcAudio.Punch, real.position, 0.9f, 0.6f);
                Destroy(real.gameObject, 8f);
                return;
            }
            // legacy cars: a panel in the car's own paint
            Color paint = new Color(0.8f, 0.3f, 0.25f);
            var mr = v.GetComponentInChildren<MeshRenderer>();
            if (mr && mr.sharedMaterial && mr.sharedMaterial.HasProperty("_BaseColor")) paint = mr.sharedMaterial.GetColor("_BaseColor");
            var door = new GameObject("FlungDoor");
            door.transform.SetPositionAndRotation(v.transform.position + v.transform.right * side * 1.05f + Vector3.up * 0.8f, v.transform.rotation);
            door.AddComponent<MeshFilter>().sharedMesh = Shapes.Cube;
            door.AddComponent<MeshRenderer>().sharedMaterial = LatMaterials.Get(paint);
            door.transform.localScale = new Vector3(0.08f, 0.75f, 1.05f);
            door.AddComponent<BoxCollider>();
            var rb = door.AddComponent<Rigidbody>();
            rb.mass = 20f;
            rb.linearVelocity = v.Body.linearVelocity * 0.5f + v.transform.right * side * 7f + Vector3.up * 5f;
            rb.angularVelocity = new Vector3(Random.Range(-8f, 8f), Random.Range(-4f, 4f), Random.Range(-8f, 8f));
            ProcAudio.Play(ProcAudio.Punch, door.transform.position, 0.9f, 0.6f);
            Destroy(door, 8f);
        }

        void EndTransition()
        {
            _trans = Trans.None;
            _transVehicle = null;
            if (_model) { _model.SetActive(true); _model.transform.localScale = Vector3.one; }
        }

        void UpdateTransition(float dt)
        {
            var v = _transVehicle;
            bool leaving = _trans == Trans.Alighting || _trans == Trans.CarSwingOut || _trans == Trans.CarStand;
            if (v == null || (!leaving && (v.Wrecked || v.PlayerInside)))
            {
                // car got wrecked or vanished while we walked over: give up
                EndTransition();
                _cc.enabled = true;
                return;
            }
            _transT += dt;
            float k = Mathf.Clamp01(Mathf.Max(0f, _transT) / _transDur);
            float e = k * k * (3f - 2f * k);
            switch (_trans)
            {
                case Trans.ToDoor when Hollow(v):
                {
                    transform.SetPositionAndRotation(Vector3.Lerp(_transFrom, _transTo, k), Quaternion.Slerp(_transRotFrom, _transRotTo, e));
                    if (_rig) { _rig.speed = def.walk; _rig.grounded = true; }
                    if (k >= 1f)
                    {
                        if (_rig) _rig.speed = 0f;
                        v.Visuals.OpenDoor(_doorSide);
                        ProcAudio.Play(ProcAudio.Blip, v.transform.position, 0.25f, 0.7f);
                        // turn round (through the car's rear) so the back faces the seat
                        var p0 = transform.position;
                        Step(Trans.CarTurn, 0.38f, p0, transform.eulerAngles.y - 180f * _doorSide);
                    }
                    break;
                }
                case Trans.CarTurn:
                {
                    transform.rotation = Quaternion.Euler(0, Mathf.Lerp(_yawFrom, _yawTo, e), 0);
                    if (_rig) _rig.speed = 0f;
                    if (k >= 1f)
                    {
                        // back down onto the edge of the seat: the sitting pose blends in on the way
                        if (_rig) _rig.sitting = true;
                        Step(Trans.CarSit, 0.55f, EdgeRoot(v), _yawTo);
                    }
                    break;
                }
                case Trans.CarSit:
                {
                    Fit(v, e);                                                        // rise / settle onto the cushion
                    // sink toward the seat with a slight lean back: height eases in late, like sitting on a chair
                    var p = Vector3.Lerp(_transFrom, _transTo, e);
                    p.y = Mathf.Lerp(_transFrom.y, _transTo.y, k * k);
                    transform.SetPositionAndRotation(p, Quaternion.Euler(0, _yawTo, 0));
                    if (k >= 1f)
                    {
                        // swing the legs in and face the road
                        Step(Trans.CarSwingIn, 0.5f, SeatPoint(v), v.transform.eulerAngles.y);
                        _yawTo = _yawFrom + Mathf.DeltaAngle(_yawFrom, _yawTo);
                    }
                    break;
                }
                case Trans.CarSwingIn:
                {
                    Fit(v, 1f);
                    var seat = SeatPoint(v);
                    transform.SetPositionAndRotation(Vector3.Lerp(_transFrom, seat, e),
                        Quaternion.Euler(0, Mathf.Lerp(_yawFrom, _yawTo, e), 0));
                    if (k >= 1f) CommitEnter(v);
                    break;
                }
                case Trans.CarSwingOut:
                {
                    if (_transT < 0f) break;                                          // door still opening
                    var edge = EdgeRoot(v);
                    transform.SetPositionAndRotation(Vector3.Lerp(_transFrom, edge, e),
                        Quaternion.Euler(0, Mathf.LerpAngle(_yawFrom, _yawTo, e), 0));
                    if (k >= 1f)
                    {
                        // plant the feet and stand up, stepping out past the door
                        if (_rig) _rig.sitting = false;
                        var outside = _doorOutside + v.transform.right * _doorSide * 0.35f;
                        outside.y = v.transform.position.y + 0.05f;
                        if (Physics.Raycast(outside + Vector3.up * 1.5f, Vector3.down, out var hit, 3f,
                                ~(1 << Layers.Vehicle | 1 << Layers.Character | 1 << Layers.Pickup), QueryTriggerInteraction.Ignore))
                            outside.y = hit.point.y + 0.02f;
                        Step(Trans.CarStand, 0.55f, outside, _yawTo);
                    }
                    break;
                }
                case Trans.CarStand:
                {
                    if (_fit) _fit.weight = 1f - Mathf.Clamp01(k * 1.6f);             // up off the seat
                    // the body rises first, then the step away
                    var p = Vector3.Lerp(_transFrom, _transTo, Mathf.Clamp01((k - 0.2f) / 0.8f));
                    p.y = Mathf.Lerp(_transFrom.y, _transTo.y, Mathf.Sqrt(k)) + Mathf.Sin(k * Mathf.PI) * 0.06f;
                    transform.SetPositionAndRotation(p, Quaternion.Euler(0, _yawTo, 0));
                    if (_rig) _rig.speed = k > 0.5f ? def.walk * 0.5f : 0f;
                    if (k >= 1f)
                    {
                        EndTransition();
                        _yaw = transform.eulerAngles.y;
                        _cc.enabled = true;
                        if (_fit) _fit.Clear();
                        v.Visuals.CloseDoor(_doorSide, true, 0.15f);
                    }
                    break;
                }
                case Trans.ToDoor:
                {
                    transform.SetPositionAndRotation(Vector3.Lerp(_transFrom, _transTo, k), Quaternion.Slerp(_transRotFrom, _transRotTo, e));
                    if (_rig) { _rig.speed = def.walk; _rig.grounded = true; }
                    if (k >= 1f)
                    {
                        _trans = Trans.Boarding;
                        _transT = 0f;
                        _transFrom = transform.position;
                        _transRotFrom = transform.rotation;
                        _transDur = v.TwoWheeler ? 0.6f : 0.45f;
                        if (_rig) { _rig.speed = 0f; _rig.Trigger("Mount"); }
                        v.Visuals?.OpenDoor(_doorSide);
                        ProcAudio.Play(ProcAudio.Blip, v.transform.position, 0.25f, v.TwoWheeler ? 1.2f : 0.7f);
                    }
                    break;
                }
                case Trans.Boarding:
                {
                    // bike: hop up onto the seat; car: duck and slide in through the door
                    var seat = SeatPoint(v);
                    var p = Vector3.Lerp(_transFrom, seat, e) + Vector3.up * Mathf.Sin(k * Mathf.PI) * (v.TwoWheeler ? 0.35f : 0.15f);
                    var fwd = Quaternion.LookRotation(Vector3.ProjectOnPlane(v.transform.forward, Vector3.up));
                    transform.SetPositionAndRotation(p, Quaternion.Slerp(_transRotFrom, fwd, e));
                    if (!Visible(v) && _model) _model.transform.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.55f, e), 1f);
                    if (k >= 1f) CommitEnter(v);
                    break;
                }
                case Trans.Alighting:
                {
                    var p = Vector3.Lerp(_transFrom, _transTo, e) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.25f;
                    transform.SetPositionAndRotation(p, Quaternion.Slerp(_transRotFrom, _transRotTo, e));
                    if (!Visible(v) && _model) _model.transform.localScale = new Vector3(1f, Mathf.Lerp(0.55f, 1f, e), 1f);
                    if (k >= 1f)
                    {
                        EndTransition();
                        _yaw = transform.eulerAngles.y;
                        _cc.enabled = true;
                    }
                    break;
                }
            }
        }

        void UpdateDriving(float dt)
        {
            if (vehicle == null) return;
            if (_engine)
            {
                float s = Mathf.Abs(vehicle.ForwardSpeed) / vehicle.maxSpeed;
                _engine.pitch = 0.8f + s * 1.6f + Mathf.Abs(vehicle.lastThrottle) * 0.15f;
            }
            if (GameInput.HornDown)
            {
                ProcAudio.Play(ProcAudio.Horn, vehicle.transform.position, 0.6f);
                // some jump out of the way, some turn round and give you an earful
                Pedestrian.React(vehicle.transform.position, 16f, Pedestrian.Stir.Horn);
                Pigeons.Startle(vehicle.transform.position, 18f);
            }
            if (GameInput.ResetCarDown) vehicle.Flip();
            if (vehicle.Wrecked)
            {
                HUD.I?.Toast("Kereta rosak! Keluar!");
                ExitVehicle(true);
            }
            NearMisses(dt);
        }

        float _nearMissCheck;

        /// <summary>Tearing past people on the pavement: they leap back and shout after you (GTA's best street detail).</summary>
        void NearMisses(float dt)
        {
            if ((_nearMissCheck -= dt) > 0f) return;
            _nearMissCheck = 0.15f;
            var vel = vehicle.Body.linearVelocity;
            vel.y = 0f;
            if (vel.sqrMagnitude < 9f * 9f) return;
            var p = vehicle.transform.position;
            Pigeons.Startle(p, 9f);
            int n = Physics.OverlapSphereNonAlloc(p, 5f, Near, 1 << Layers.Character, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var ped = Near[i].GetComponentInParent<Pedestrian>();
                if (ped != null) ped.NearMiss(p, vel);
            }
        }
    }
}
