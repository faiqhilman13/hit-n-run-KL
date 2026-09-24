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
            _model = ModelFactory.Spawn(def.model, transform.position, transform.rotation, transform, "Body");
            _model.transform.localPosition = Vector3.zero;
            _model.transform.localRotation = Quaternion.identity;
            var costume = GameData.Costumes.Find(c => c.id == GameState.Costume(id));
            if (costume != null) ModelFactory.Recolor(_model, costume.swaps);
            foreach (var t in _model.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Character;
            _rig = _model.AddComponent<CharacterRig>();
            float h = ModelFactory.LocalBounds(_model).size.y;
            _cc.height = Mathf.Max(1.0f, h * 0.95f);
            _cc.center = new Vector3(0, _cc.height * 0.5f + 0.02f, 0);
            _cc.radius = h < 1.3f ? 0.28f : 0.35f;
        }

        public void Teleport(Vector3 pos, Quaternion rot)
        {
            if (Driving) ExitVehicle(false, true);
            if (Transitioning) EndTransition();
            _cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            _yaw = rot.eulerAngles.y;
            _vel = Vector3.zero;
            _cc.enabled = true;
            if (ChaseCamera.I) { ChaseCamera.I.target = transform; ChaseCamera.I.targetBody = null; ChaseCamera.I.distance = 6.5f; ChaseCamera.I.SnapBehind(); }
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
        void UpdateOnFoot(float dt)
        {
            bool grounded = _cc.isGrounded;
            if (grounded) _groundedGrace = 0.12f; else _groundedGrace -= dt;

            if (_knockTime > 0f)
            {
                _knockTime -= dt;
                _knockVel += Physics.gravity * dt;
                _knockVel.x *= 1f - dt * 2f; _knockVel.z *= 1f - dt * 2f;
                _cc.Move(_knockVel * dt);
                if (_model && !(_rig && _rig.Humanoid)) _model.transform.localRotation = Quaternion.Euler(-80f * Mathf.Clamp01(_knockTime * 2f), 0, 0);
                if (_knockTime <= 0f && _model) _model.transform.localRotation = Quaternion.identity;
                return;
            }

            Vector2 input = frozen ? Vector2.zero : GameInput.Move;
            var cam = ChaseCamera.I ? ChaseCamera.I.transform : transform;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 wish = fwd * input.y + right * input.x;
            float speed = GameInput.Sprint ? def.run : Mathf.Lerp(def.walk, def.run, Mathf.Clamp01(input.magnitude - 0.85f) * 3f);
            if (_attackCooldown > 0.15f) speed *= 0.3f;
            Vector3 target = wish * speed;
            float accel = grounded ? 14f : 5f;
            _vel.x = Mathf.MoveTowards(_vel.x, target.x, accel * speed * dt);
            _vel.z = Mathf.MoveTowards(_vel.z, target.z, accel * speed * dt);
            if (wish.sqrMagnitude > 0.01f)
                _yaw = Mathf.MoveTowardsAngle(_yaw, Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg, 720f * dt);
            transform.rotation = Quaternion.Euler(0, _yaw, 0);

            if (grounded && _vel.y < 0)
            {
                if (_stomping) Stomp();
                _vel.y = -2f;
                _jumps = 0;
            }
            if (!frozen && GameInput.JumpDown && (_groundedGrace > 0f || _jumps < 2))
            {
                if (_groundedGrace <= 0f && _jumps == 0) _jumps = 1; // walked off a ledge: one jump left
                _vel.y = _jumps == 0 ? def.jump : def.jump * 0.85f;
                _jumps++;
                _groundedGrace = 0f;
                ProcAudio.Play(ProcAudio.Whoosh, transform.position, 0.4f, _jumps == 1 ? 1f : 1.3f);
            }
            _vel.y += Physics.gravity.y * (_stomping ? 3f : 1.6f) * dt;
            _cc.Move(_vel * dt);

            // attacks
            _attackCooldown -= dt;
            _comboTimer -= dt;
            if (_comboTimer <= 0f) _combo = 0;
            if (!frozen && _attackCooldown <= 0f)
            {
                if (GameInput.PunchDown) Punch();
                else if (GameInput.KickDown)
                {
                    if (!grounded && _groundedGrace <= 0f) { _stomping = true; _vel = new Vector3(0, -4f, 0); ProcAudio.Play(ProcAudio.Whoosh, transform.position, 0.5f, 0.7f); }
                    else Kick();
                }
            }

            if (_rig)
            {
                _rig.speed = new Vector2(_vel.x, _vel.z).magnitude;
                _rig.grounded = grounded || _groundedGrace > 0f;
            }

            if (transform.position.y < -12f) GameManager.I.Respawn();
        }

        public void DebugPunch(int times) { for (int i = 0; i < times; i++) Punch(); }
        public void DebugKick() => Kick();

        void Punch()
        {
            _combo = (_combo % 3) + 1;
            _comboTimer = 0.6f;
            _attackCooldown = _combo == 3 ? 0.45f : 0.22f;
            _rig?.Punch(_combo);
            // third hit is the big one
            Attack(transform.position + Vector3.up * 1.1f + transform.forward * 0.8f, 0.85f, _combo == 3 ? 11f : 6f, _combo == 3 ? 18f : 8f);
        }

        void Kick()
        {
            _attackCooldown = 0.4f;
            _rig?.Kick();
            Attack(transform.position + Vector3.up * 0.6f + transform.forward * 0.9f, 1f, 13f, 14f);
        }

        void Stomp()
        {
            _stomping = false;
            _attackCooldown = 0.3f;
            for (int i = 0; i < 10; i++) Fx.Dust(transform.position + Quaternion.Euler(0, i * 36, 0) * Vector3.forward * 1.2f);
            ChaseCamera.I?.Shake(0.4f);
            Fx.Word(transform.position + Vector3.up * 2f, "DEBUK!");
            Attack(transform.position + Vector3.up * 0.4f, 2.6f, 10f, 20f);
        }

        void Attack(Vector3 center, float radius, float force, float damage)
        {
            bool hitSomething = false;
            var seen = new HashSet<Object>();
            foreach (var c in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide))
            {
                if (c.transform.IsChildOf(transform)) continue;
                var dir = (c.transform.position - transform.position); dir.y = 0; dir.Normalize();

                var ped = c.GetComponentInParent<Pedestrian>();
                if (ped && seen.Add(ped)) { ped.Hit(dir, force, true); hitSomething = true; continue; }
                var br = c.GetComponentInParent<Breakable>();
                if (br && seen.Add(br)) { br.Hit(transform.position, force, true); hitSomething = true; continue; }
                var cam = c.GetComponentInParent<BurungKamera>();
                if (cam && seen.Add(cam)) { cam.Smash(); hitSomething = true; continue; }
                var v = c.attachedRigidbody ? c.attachedRigidbody.GetComponent<Vehicle>() : null;
                if (v && seen.Add(v))
                {
                    v.Body.AddForceAtPosition(dir * force * 120f, center, ForceMode.Impulse);
                    v.Damage(damage * 0.25f);
                    SamanMeter.I?.AddHeat(v.role == VehicleRole.Police ? 25f : 3f);
                    hitSomething = true;
                }
            }
            if (hitSomething)
            {
                ProcAudio.Play(ProcAudio.Punch, center, 0.8f, Random.Range(0.85f, 1.15f));
                Fx.Word(center + Vector3.up * 0.8f);
                ChaseCamera.I?.Shake(0.15f);
            }
            else ProcAudio.Play(ProcAudio.Whoosh, center, 0.3f, 1.4f);
        }

        /// <summary>Hit by a car: tumble and drop some coins, like H&amp;R.</summary>
        public void Knockdown(Vector3 impulse)
        {
            if (Driving || Transitioning || _knockTime > 0f) return;
            _knockTime = 1.2f;
            _knockVel = impulse;
            _rig?.Tumble(1.2f);
            ProcAudio.Play(ProcAudio.Aduh, transform.position, 0.8f);
            Fx.Word(transform.position + Vector3.up * 2f, "ADUH!");
            int drop = Mathf.Min(GameState.Coins, 5);
            GameState.AddCoins(-drop);
            for (int i = 0; i < drop; i++) Pickup.SpawnCoin(transform.position + Vector3.up, true);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // push loose things around
            var rb = hit.rigidbody;
            if (rb && !rb.isKinematic && rb.GetComponent<Vehicle>() == null && hit.moveDirection.y > -0.3f)
                rb.AddForce(hit.moveDirection * 3f, ForceMode.Impulse);
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
            v.driver = new PlayerDriver();
            _cc.enabled = false;
            transform.SetParent(v.transform, true);
            if (Visible(v) && !v.TwoWheeler)
            {
                // sit at the wheel, parented to the seat so we lean and bounce with the body
                transform.SetParent(v.Visuals.Seat, true);
                transform.SetPositionAndRotation(SeatPoint(v), v.transform.rotation);
                if (_model) _model.SetActive(true);
                if (_rig) { _rig.riding = false; _rig.sitting = true; _rig.speed = 0; }
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
                if (_rig) { _rig.riding = true; _rig.sitting = true; _rig.speed = 0; }
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
            cam.distance = v.halfLength * 2f + 4.5f;
            cam.height = 1.6f + v.halfLength * 0.15f;
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
            var cam = ChaseCamera.I;
            cam.target = transform;
            cam.targetBody = null;
            cam.distance = 6.5f;
            cam.height = 1.6f;

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
                Pedestrian.ScareAround(vehicle.transform.position, 14f);
            }
            if (GameInput.ResetCarDown) vehicle.Flip();
            if (vehicle.Wrecked)
            {
                HUD.I?.Toast("Kereta rosak! Keluar!");
                ExitVehicle(true);
            }
        }
    }
}
