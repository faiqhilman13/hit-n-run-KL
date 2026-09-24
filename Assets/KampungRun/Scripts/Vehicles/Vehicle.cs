using System;
using UnityEngine;

namespace KampungRun
{
    public interface IDriver
    {
        void Drive(Vehicle v, out float throttle, out float steer, out bool handbrake);
    }

    public enum VehicleRole { Traffic, Player, Police, MissionTarget, Parked }

    /// <summary>
    /// Arcade vehicle: raycast suspension on a Rigidbody, strong lateral grip that lets go
    /// on the handbrake, auto-righting. Plus health, driver slot and crash events.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Vehicle : MonoBehaviour
    {
        [Header("Identity")]
        public string displayName = "Kereta";
        public string carId;
        public bool noEnter;
        public float halfLength = 2.2f;
        public VehicleRole role = VehicleRole.Parked;
        public IDriver driver;
        public bool PlayerInside => role == VehicleRole.Player;

        [Header("Handling")]
        public float maxSpeed = 30f;
        public float acceleration = 14f;
        public float brakeDecel = 22f;
        public float reverseSpeed = 9f;
        public float turnRate = 115f;
        public float grip = 9f;
        public float driftGrip = 1.4f;
        public float suspensionRest = 0.34f;

        [Header("Health")]
        public float maxHealth = 100f;
        public float health = 100f;
        public bool Wrecked => health <= 0f;

        public event Action<Vehicle> OnWrecked;
        public event Action<Vehicle, Collision, float> OnCrash;

        public Rigidbody Body { get; private set; }
        public float SpeedKmh => Body ? Body.linearVelocity.magnitude * 3.6f : 0f;
        public float ForwardSpeed => Body ? Vector3.Dot(Body.linearVelocity, transform.forward) : 0f;

        Transform[] _wheels = new Transform[4];
        Vector3[] _wheelRest = new Vector3[4];
        Quaternion[] _wheelRot = new Quaternion[4];
        float _wheelRadius = 0.38f;
        float _spin;
        float _upsideDownTime;
        float _smokeTimer;
        float _k, _c;
        public bool grounded;

        public float lastThrottle, lastSteer;
        public bool lastHandbrake;

        /// <summary>Steering as the wheels show it: eased toward the input so nothing snaps.</summary>
        public float SteerVisual { get; private set; }
        public Transform[] Wheels => _wheels;
        public float WheelRadius => _wheelRadius;
        public Transform RearWheelT => _rearWheel;
        public VehicleVisuals Visuals { get; private set; }

        public void Setup(float mass)
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = mass;
            Body.linearDamping = 0.05f;
            Body.angularDamping = 1.5f;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int i = 0; i < 4; i++)
            {
                _wheels[i] = ModelFactory.Find(gameObject, names[i]);
                if (_wheels[i])
                {
                    _wheelRest[i] = transform.InverseTransformPoint(_wheels[i].position);
                    _wheelRot[i] = Quaternion.Inverse(transform.rotation) * _wheels[i].rotation;
                    var r = _wheels[i].GetComponent<Renderer>();
                    if (r) _wheelRadius = r.bounds.extents.y;
                }
            }
            // FBX wheel naming uses Blender's -Y front; make sure [0],[1] are the front pair.
            if (_wheels[0] && _wheelRest[0].z < _wheelRest[2].z)
            {
                (_wheels[0], _wheels[2]) = (_wheels[2], _wheels[0]);
                (_wheels[1], _wheels[3]) = (_wheels[3], _wheels[1]);
                (_wheelRest[0], _wheelRest[2]) = (_wheelRest[2], _wheelRest[0]);
                (_wheelRest[1], _wheelRest[3]) = (_wheelRest[3], _wheelRest[1]);
                (_wheelRot[0], _wheelRot[2]) = (_wheelRot[2], _wheelRot[0]);
                (_wheelRot[1], _wheelRot[3]) = (_wheelRot[3], _wheelRot[1]);
            }

            SetupTwoWheeler();
            // Hit & Run cars (and the kapcai) bring a Body part: animate it, the doors, lamps and props
            if (ModelFactory.Find(gameObject, "Body") != null && (ModelFactory.Find(gameObject, "Seat_Driver") != null ||
                                                                   ModelFactory.Find(gameObject, "Seat_Rider") != null))
            {
                Visuals = gameObject.AddComponent<VehicleVisuals>();
                Visuals.Setup(this);
            }

            // Body collider sits above the wheels so the suspension does the supporting.
            // KL assets bring a COL_ proxy (already turned into a disabled box on the root).
            var proxy = GetComponent<BoxCollider>();
            Bounds b;
            if (proxy != null) b = new Bounds(proxy.center, proxy.size);
            else
            {
                var body = ModelFactory.Find(gameObject, "Body");
                b = ModelFactory.LocalBounds(body ? body.gameObject : gameObject);
                b = TransformBounds(body ? body : transform, b);
            }
            float bottom = Mathf.Max(b.min.y, _wheelRadius * 1.15f);
            var box = proxy != null ? proxy : gameObject.AddComponent<BoxCollider>();
            box.enabled = true;
            box.center = new Vector3(b.center.x, (bottom + b.max.y) * 0.5f, b.center.z);
            box.size = new Vector3(b.size.x, b.max.y - bottom, b.size.z);
            box.sharedMaterial = SlideMaterial;
            halfLength = b.extents.z;

            Body.centerOfMass = new Vector3(0, bottom * 0.8f, 0);
            float perWheel = mass * 9.81f / 4f;
            _k = perWheel / (suspensionRest * 0.5f);
            _c = 2f * Mathf.Sqrt(_k * mass / 4f) * 0.6f;
            gameObject.layer = Layers.Vehicle;
            foreach (Transform t in GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Vehicle;
        }

        // ---------------------------------------------------------------- two-wheelers
        public bool TwoWheeler { get; private set; }
        /// <summary>Where the rider's root goes when they are visible on the vehicle (bikes).</summary>
        public Vector3 seatLocal = new Vector3(0, 0.27f, -0.2f);
        Transform _steering, _frontWheel, _rearWheel, _leanRoot;
        Quaternion _steerRest, _fwRest, _rwRest, _leanRest = Quaternion.identity;
        float _lean, _leanV;

        /// <summary>KL bikes have FrontWheel/RearWheel/Steering parts. Suspension still uses four
        /// rays (two either side of each hub) so arcade handling stays stable, while the visible
        /// bike steers, spins its wheels and leans into corners.</summary>
        void SetupTwoWheeler()
        {
            if (_wheels[0] != null) return;
            _frontWheel = ModelFactory.Find(gameObject, "FrontWheel");
            _rearWheel = ModelFactory.Find(gameObject, "RearWheel");
            if (!_frontWheel || !_rearWheel) return;
            TwoWheeler = true;
            _steering = ModelFactory.Find(gameObject, "Steering");
            _leanRoot = transform.Find("Model");
            if (_leanRoot) _leanRest = _leanRoot.localRotation;   // the importer's axis correction - lean on top of it
            // rest orientations relative to the vehicle (KL parts keep Blender-style local axes)
            _steerRest = _steering ? Quaternion.Inverse(transform.rotation) * _steering.rotation : Quaternion.identity;
            _fwRest = Quaternion.Inverse(transform.rotation) * _frontWheel.rotation;
            _rwRest = Quaternion.Inverse(transform.rotation) * _rearWheel.rotation;
            var r = _frontWheel.GetComponent<Renderer>();
            if (r) _wheelRadius = r.bounds.extents.y;
            var f = transform.InverseTransformPoint(_frontWheel.position);
            var b = transform.InverseTransformPoint(_rearWheel.position);
            const float lat = 0.28f;
            _wheelRest[0] = f + Vector3.right * lat; _wheelRest[1] = f - Vector3.right * lat;
            _wheelRest[2] = b + Vector3.right * lat; _wheelRest[3] = b - Vector3.right * lat;
            for (int i = 0; i < 4; i++) _anchorOnly[i] = true;
        }

        readonly bool[] _anchorOnly = new bool[4];

        void UpdateTwoWheelerVisuals(float dt, float forwardSpeed)
        {
            if (!TwoWheeler) return;
            var lean = _leanRoot ? _leanRoot.rotation * Quaternion.Inverse(_leanRest) : transform.rotation;
            var steer = Quaternion.Euler(0, SteerVisual * 28f, 0);
            if (_steering) _steering.rotation = lean * steer * _steerRest;
            _frontWheel.rotation = lean * steer * Quaternion.Euler(_spin, 0, 0) * _fwRest;
            _rearWheel.rotation = lean * Quaternion.Euler(_spin, 0, 0) * _rwRest;
            // lean into the turn (more at speed), with a little overshoot as you flick side to side
            float targetLean = -SteerVisual * Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 10f) * 30f;
            _leanV += ((targetLean - _lean) * 55f - _leanV * 9f) * dt;
            _lean += _leanV * dt;
            if (_leanRoot) _leanRoot.localRotation = Quaternion.Euler(0, 0, _lean) * _leanRest;
        }

        Bounds TransformBounds(Transform from, Bounds local)
        {
            var b = new Bounds(transform.InverseTransformPoint(from.TransformPoint(local.center)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                b.Encapsulate(transform.InverseTransformPoint(from.TransformPoint(c)));
            }
            return b;
        }

        static PhysicsMaterial _slide;

        static PhysicsMaterial SlideMaterial
        {
            get
            {
                if (_slide == null)
                    _slide = new PhysicsMaterial("CarBody")
                    {
                        dynamicFriction = 0.15f, staticFriction = 0.15f, bounciness = 0.25f,
                        frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Maximum
                    };
                return _slide;
            }
        }

        void FixedUpdate()
        {
            if (!Body) return;
            float dt = Time.fixedDeltaTime;
            float throttle = 0, steer = 0;
            bool handbrake = false;
            if (driver != null && !Wrecked) driver.Drive(this, out throttle, out steer, out handbrake);
            if (role == VehicleRole.Parked || (driver == null && role != VehicleRole.Player)) handbrake = true;
            lastThrottle = throttle; lastSteer = steer; lastHandbrake = handbrake;
            SteerVisual = Mathf.MoveTowards(SteerVisual, steer, dt * 4.5f);

            // --- suspension ---------------------------------------------------------------
            int groundedCount = 0;
            Vector3 normalSum = Vector3.zero;
            float rayLen = suspensionRest + _wheelRadius;
            for (int i = 0; i < 4; i++)
            {
                if (!_wheels[i] && !_anchorOnly[i]) continue;
                Vector3 anchor = transform.TransformPoint(_wheelRest[i] + Vector3.up * suspensionRest * 0.5f);
                if (Physics.Raycast(anchor, -transform.up, out var hit, rayLen, ~(1 << Layers.Vehicle | 1 << Layers.Pickup | 1 << Layers.Character), QueryTriggerInteraction.Ignore))
                {
                    float compression = rayLen - hit.distance;
                    float vel = Vector3.Dot(Body.GetPointVelocity(anchor), transform.up);
                    float f = Mathf.Max(0f, compression * _k - vel * _c);
                    // bump stop: past 60% travel the spring gets very stiff, so speed/downforce/landings
                    // can't sink the body into the road
                    float stop = compression - suspensionRest * 0.6f;
                    if (stop > 0f) f += stop * _k * 8f;
                    Body.AddForceAtPosition(transform.up * f, anchor);
                    groundedCount++;
                    normalSum += hit.normal;
                    SetWheelVisual(i, hit.distance);
                }
                else SetWheelVisual(i, rayLen);
            }
            grounded = groundedCount > 0;
            UpdateTwoWheelerVisuals(dt, Vector3.Dot(Body.linearVelocity, transform.forward));

            var v = Body.linearVelocity;
            if (grounded)
            {
                float traction = groundedCount / 4f;
                Vector3 n = normalSum.normalized;
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, n).normalized;
                Vector3 right = Vector3.ProjectOnPlane(transform.right, n).normalized;
                float fs = Vector3.Dot(v, fwd);

                // throttle / brake / reverse
                if (throttle > 0.05f)
                {
                    if (fs < -1f) Body.AddForce(fwd * brakeDecel * traction, ForceMode.Acceleration);
                    else if (fs < maxSpeed) Body.AddForce(fwd * acceleration * throttle * traction * Mathf.Lerp(1.4f, 0.6f, fs / maxSpeed), ForceMode.Acceleration);
                }
                else if (throttle < -0.05f)
                {
                    if (fs > 1f) Body.AddForce(-fwd * brakeDecel * traction * -throttle, ForceMode.Acceleration);
                    else if (fs > -reverseSpeed) Body.AddForce(fwd * acceleration * 0.7f * throttle * traction, ForceMode.Acceleration);
                }
                else
                {
                    Body.AddForce(-fwd * fs * 0.6f * traction, ForceMode.Acceleration); // rolling resistance
                }
                if (handbrake) Body.AddForce(-fwd * Mathf.Sign(fs) * Mathf.Min(Mathf.Abs(fs) / dt, 9f) * traction, ForceMode.Acceleration);

                // lateral grip
                float lat = Vector3.Dot(v, right);
                float g = handbrake ? driftGrip : grip;
                Body.AddForce(-right * lat * Mathf.Min(g, 1f / dt) * traction, ForceMode.Acceleration);

                // steering: drive yaw rate directly (arcade)
                float speedFactor = Mathf.Clamp01(Mathf.Abs(fs) / 5f) * Mathf.Lerp(1f, 0.55f, Mathf.Abs(fs) / maxSpeed);
                float targetYaw = steer * turnRate * Mathf.Deg2Rad * speedFactor * Mathf.Sign(fs) * (handbrake ? 1.35f : 1f);
                var av = Body.angularVelocity;
                float yawNow = Vector3.Dot(av, transform.up);
                float newYaw = Mathf.Lerp(yawNow, targetYaw, dt * 7f * traction);
                Body.angularVelocity = av + transform.up * (newYaw - yawNow);

                Body.AddForce(-n * Mathf.Min(Mathf.Abs(fs) * 0.2f, 3.5f), ForceMode.Acceleration); // downforce (capped)
                _spin += fs * dt / Mathf.Max(0.1f, _wheelRadius) * Mathf.Rad2Deg;
            }
            else
            {
                // airborne: gently level out so jumps land on the wheels
                Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
                Body.AddTorque(axis * 6f, ForceMode.Acceleration);
            }

            // auto-flip if stuck on the roof/side
            if (Vector3.Dot(transform.up, Vector3.up) < 0.35f && v.magnitude < 2.5f) _upsideDownTime += dt;
            else _upsideDownTime = 0f;
            if (_upsideDownTime > 1.6f) Flip();

            if (Wrecked)
            {
                _smokeTimer -= dt;
                if (_smokeTimer <= 0) { _smokeTimer = 0.18f; Fx.Smoke(transform.position + Vector3.up * 1.4f); }
            }
        }

        public void Flip()
        {
            _upsideDownTime = 0f;
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Body.position = Body.position + Vector3.up * 1.2f;
            Body.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
        }

        void SetWheelVisual(int i, float hitDist)
        {
            var w = _wheels[i];
            if (!w || TwoWheeler) return;
            float drop = hitDist - _wheelRadius - suspensionRest * 0.5f;
            Vector3 local = _wheelRest[i] - Vector3.up * Mathf.Clamp(drop, -0.2f, 0.25f);
            w.position = transform.TransformPoint(local);
            float steerAngle = i < 2 ? SteerVisual * 30f : 0f;
            w.rotation = transform.rotation * Quaternion.Euler(0, steerAngle, 0) * Quaternion.Euler(_spin, 0, 0) * _wheelRot[i];
        }

        public void Damage(float amount)
        {
            if (Wrecked || amount <= 0) return;
            health = Mathf.Max(0, health - amount);
            if (Wrecked)
            {
                Fx.Burst(transform.position + Vector3.up, LatMaterials.Ink, 14, 6f);
                ProcAudio.Play(ProcAudio.Crash, transform.position, 1f, 0.6f);
                OnWrecked?.Invoke(this);
            }
        }

        public void Repair() => health = maxHealth;

        void OnCollisionEnter(Collision c)
        {
            float rel = c.relativeVelocity.magnitude;
            if (rel < 3.5f) return;
            // hitting static world hurts less than car-on-car; big bus hurts more
            var other = c.rigidbody ? c.rigidbody.GetComponent<Vehicle>() : null;
            float mult = other ? Mathf.Clamp(other.Body.mass / Body.mass, 0.5f, 3f) : 0.6f;
            if (role == VehicleRole.Player) mult *= 0.55f; // be kind to the player
            Damage((rel - 3.5f) * 2.2f * mult);
            OnCrash?.Invoke(this, c, rel);
            if (rel > 6f) ProcAudio.Play(ProcAudio.Crash, c.GetContact(0).point, Mathf.Clamp01(rel / 25f), UnityEngine.Random.Range(0.8f, 1.2f));
        }
    }
}
