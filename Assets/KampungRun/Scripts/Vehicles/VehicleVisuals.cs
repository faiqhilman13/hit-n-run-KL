using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Everything that makes a Hit & Run-style car feel alive, on top of Vehicle's physics:
    /// the body rolls into corners, dives under braking, squats and bounces on landings and
    /// shakes at idle; the steering wheel turns; doors swing open and slam shut (with a bounce)
    /// when someone gets in or out; brake / reverse / head lamps light up; the mirror ornament
    /// and the antenna swing on springs; the exhaust puffs; tyres smoke and leave skid marks
    /// when sliding; landings kick up dust and crashes throw sparks.
    /// Works with the part names the Blender car builder exports (see kl_cars.py).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class VehicleVisuals : MonoBehaviour
    {
        /// <summary>Set by the level look: lamps burn brighter at night.</summary>
        public static bool Night;

        Vehicle _v;
        Transform _body;
        Vector3 _bodyLocalPos;
        Quaternion _bodyRel;

        // suspension springs (degrees / metres)
        float _roll, _rollV, _pitch, _pitchV, _heave, _heaveV;
        Vector3 _lastVel;
        Vector3 _accLocal;
        bool _wasGrounded = true;

        Transform _wheel;          // steering wheel
        Quaternion _wheelRest;
        Vector3 _wheelAxis;        // in the wheel's parent space, pointing back at the driver
        float _wheelRadius;        // of the rim, in world metres

        class Door
        {
            public Transform t;
            public Quaternion rest;
            public Vector3 restPos;
            public int side;       // +1 vehicle right, -1 left
            public bool front, slide;
            public float x, vx, target, closeAt = -1f;
        }
        readonly List<Door> _doors = new List<Door>();

        Renderer[] _head, _brake, _reverse;
        MaterialPropertyBlock _mpb;
        float _brakeGlow, _revGlow;

        Transform _orn, _ant;
        Quaternion _ornRest, _antRest;
        Vector2 _ornA, _ornW, _antA, _antW;

        Transform _exhaust;
        float _puffT, _skidT, _smokeT;
        readonly Vector3[] _lastSkid = new Vector3[2];
        readonly bool[] _skidding = new bool[2];

        public Transform Seat { get; private set; }

        static Material _glass;
        static Material GlassMaterial
        {
            get
            {
                if (_glass == null && GameAssets.I.glass != null) _glass = new Material(GameAssets.I.glass) { name = "CarGlass" };
                return _glass;
            }
        }

        /// <summary>World-space door geometry for boarding: the point just outside the doorway on
        /// `side` (standing height = the car's ground), and the doorway's edge at seat height.</summary>
        public bool DoorPoints(int side, out Vector3 outside, out Vector3 edge)
        {
            outside = edge = transform.position;
            if (Seat == null) return false;
            var box = GetComponent<BoxCollider>();
            float halfW = box ? box.size.x * 0.5f : 0.85f;
            var seatL = transform.InverseTransformPoint(Seat.position);
            var d = FindDoor(side, true);
            float z = seatL.z;
            if (d != null && d.t) z = Mathf.Lerp(z, transform.InverseTransformPoint(d.t.GetComponent<Renderer>() ?
                d.t.GetComponent<Renderer>().bounds.center : d.t.position).z, 0.35f);
            outside = transform.TransformPoint(new Vector3(side * (halfW + 0.55f), 0f, z - 0.05f));
            edge = transform.TransformPoint(new Vector3(side * (halfW - 0.22f), seatL.y, seatL.z + 0.05f));
            return true;
        }

        public void Setup(Vehicle v)
        {
            _v = v;
            _body = ModelFactory.Find(gameObject, "Body");
            if (_body)
            {
                _bodyLocalPos = transform.InverseTransformPoint(_body.position);
                _bodyRel = Quaternion.Inverse(transform.rotation) * _body.rotation;
            }
            _wheel = ModelFactory.Find(gameObject, "SteeringWheel");
            var axis = ModelFactory.Find(gameObject, "SteeringAxis");
            if (_wheel)
            {
                _wheelRest = _wheel.localRotation;
                var a = axis ? axis.position - _wheel.position : _wheel.parent.forward;
                _wheelAxis = _wheel.parent.InverseTransformDirection(a).normalized;
                // the rim is the widest part of the wheel mesh: its centreline is 0.17 of its 0.188 m reach (kl_cars.py)
                var mf = _wheel.GetComponent<MeshFilter>();
                var ext = mf && mf.sharedMesh ? mf.sharedMesh.bounds.extents : Vector3.one * 0.188f;
                _wheelRadius = Mathf.Max(ext.x, ext.y, ext.z) * Mathf.Abs(_wheel.lossyScale.x) * (0.17f / 0.188f);
            }
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                bool slide = t.name.StartsWith("SlideDoor");
                if (!t.name.StartsWith("Door_") && !slide) continue;
                var lp = transform.InverseTransformPoint(t.GetComponent<Renderer>() ? t.GetComponent<Renderer>().bounds.center : t.position);
                _doors.Add(new Door
                {
                    t = t, rest = t.localRotation, restPos = t.localPosition, slide = slide,
                    side = lp.x >= 0 ? 1 : -1, front = slide ? false : t.name.Length > 5 && t.name[5] == 'F',
                });
            }
            // windows are separate "Glass" parts: draw them see-through so the driver and seats show
            if (GlassMaterial != null)
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (r.name.Contains("Glass"))
                    {
                        r.sharedMaterial = GlassMaterial;
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
            _head = Lamps("HeadLights");
            _brake = Lamps("BrakeLights");
            _reverse = Lamps("ReverseLights");
            _mpb = new MaterialPropertyBlock();
            _orn = ModelFactory.Find(gameObject, "Ornament");
            if (_orn) _ornRest = _orn.localRotation;
            _ant = ModelFactory.Find(gameObject, "Antenna");
            if (_ant) _antRest = _ant.localRotation;
            _exhaust = ModelFactory.Find(gameObject, "FX_Exhaust");
            Seat = ModelFactory.Find(gameObject, "Seat_Driver") ?? ModelFactory.Find(gameObject, "Seat_Rider");
            _v.OnCrash += Crash;
            SetLamps(_head, Night ? 1.2f : 0.15f);
            SetLamps(_brake, 0.15f);
            SetLamps(_reverse, 0f);
        }

        Renderer[] Lamps(string n)
        {
            var t = ModelFactory.Find(gameObject, n);
            return t ? t.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        }

        void SetLamps(Renderer[] rs, float emit)
        {
            if (rs == null) return;
            foreach (var r in rs)
            {
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat("_Emit", emit);
                r.SetPropertyBlock(_mpb);
            }
        }

        // ---------------------------------------------------------------- doors
        /// <summary>Swing a door open (side +1 = driver's / right side). Front door unless front = false.</summary>
        public void OpenDoor(int side, bool front = true, float closeAfter = -1f)
        {
            var d = FindDoor(side, front);
            if (d == null) return;
            d.target = 1f;
            d.closeAt = closeAfter > 0 ? Time.time + closeAfter : -1f;
        }

        public void CloseDoor(int side, bool front = true, float delay = 0f)
        {
            var d = FindDoor(side, front);
            if (d == null) return;
            if (delay <= 0f) d.target = 0f;
            else d.closeAt = Time.time + delay;
        }

        Door FindDoor(int side, bool front)
        {
            Door best = null;
            foreach (var d in _doors)
            {
                if (d.side != side) continue;
                if (d.front == front) return d;
                best ??= d;
            }
            return best;
        }

        void UpdateDoors(float dt)
        {
            foreach (var d in _doors)
            {
                if (!d.t || d.t.parent == null || !d.t.IsChildOf(transform)) continue;   // torn off in a wreck
                if (d.closeAt > 0 && Time.time >= d.closeAt) { d.target = 0f; d.closeAt = -1f; }
                // bouncy spring: opens with a little overshoot, slams shut with a rebound
                float k = d.target > 0 ? 90f : 160f, c = d.target > 0 ? 9f : 7f;
                d.vx += (k * (d.target - d.x) - c * d.vx) * dt;
                d.x += d.vx * dt;
                if (d.x < 0f)
                {
                    if (d.vx < -1.2f) Slam(d);
                    d.x = -d.x * 0.3f;
                    d.vx = -d.vx * 0.25f;
                }
                if (d.slide)
                {
                    // slide back along the rail, stepping out first
                    float o = Mathf.Clamp01(d.x * 4f), b = Mathf.Clamp01(d.x);
                    d.t.localPosition = d.restPos + d.t.parent.InverseTransformVector(
                        transform.right * d.side * 0.09f * o - transform.forward * 0.85f * b);
                }
                else
                {
                    float ang = -d.side * 68f * d.x;
                    d.t.localRotation = Quaternion.AngleAxis(ang, d.t.parent.InverseTransformDirection(transform.up)) * d.rest;
                }
            }
        }

        void Slam(Door d)
        {
            ProcAudio.Play(ProcAudio.Punch, d.t.position, 0.35f, 1.6f);
            _rollV += d.side * 40f;
            _heaveV -= 0.25f;
        }

        // ---------------------------------------------------------------- crashes
        void Crash(Vehicle v, Collision c, float rel)
        {
            if (rel < 5f || c.contactCount == 0) return;
            var p = c.GetContact(0).point;
            Fx.Burst(p, new Color(1f, 0.85f, 0.3f), Mathf.Clamp((int)(rel * 0.8f), 4, 16), 5f);
            var local = transform.InverseTransformDirection(c.GetContact(0).normal);
            _rollV += -local.x * rel * 6f;
            _pitchV += local.z * rel * 5f;
            _heaveV += 0.3f;
        }

        // ---------------------------------------------------------------- per frame
        void FixedUpdate()
        {
            if (_v == null || _v.Body == null) return;
            float dt = Time.fixedDeltaTime;
            var vel = _v.Body.linearVelocity;
            var acc = (vel - _lastVel) / dt;
            _lastVel = vel;
            // low-pass so contact jitter doesn't rattle the body
            _accLocal = Vector3.Lerp(_accLocal, transform.InverseTransformDirection(acc), dt * 10f);

            // landing: squash, dust, a thud
            if (_v.grounded && !_wasGrounded && acc.y > 12f)
            {
                _heaveV -= Mathf.Clamp(acc.y * 0.012f, 0.1f, 0.9f);
                _pitchV += Random.Range(-20f, 20f);
                foreach (var w in _v.Wheels) if (w) Fx.Dust(w.position - Vector3.up * _v.WheelRadius * 0.9f);
                if (_v.PlayerInside) ChaseCamera.I?.Shake(Mathf.Clamp01(acc.y / 120f) * 0.25f);
            }
            _wasGrounded = _v.grounded;

            // cartoon suspension: body leans out of turns, dives under braking, squats on the gas
            float rollT = Mathf.Clamp(_accLocal.x * 0.9f, -9f, 9f);
            float pitchT = Mathf.Clamp(-_accLocal.z * 0.55f, -6f, 7f);
            if (!_v.grounded) { rollT *= 0.2f; pitchT = 3f; }
            Spring(ref _roll, ref _rollV, rollT, 70f, 7f, dt);
            Spring(ref _pitch, ref _pitchV, pitchT, 70f, 7f, dt);
            Spring(ref _heave, ref _heaveV, 0f, 90f, 6f, dt);
            _heave = Mathf.Clamp(_heave, -0.12f, 0.1f);

            UpdateDoors(dt);
            UpdateDangly(dt);
            UpdateFx(dt);
        }

        static void Spring(ref float x, ref float v, float target, float k, float c, float dt)
        {
            v += (k * (target - x) - c * v) * dt;
            x += v * dt;
        }

        void UpdateDangly(float dt)
        {
            // ornament: a pendulum pushed around by the car's acceleration
            var a = new Vector2(_accLocal.z, -_accLocal.x);
            _ornW += (-_ornA * 28f - _ornW * 1.6f + a * 2.2f) * dt;
            _ornA += _ornW * dt;
            _ornA = Vector2.ClampMagnitude(_ornA, 55f);
            // antenna: stiffer whip, also flicked by bumps
            _antW += (-_antA * 160f - _antW * 3f + a * 5f + new Vector2(_heaveV * 40f, 0)) * dt;
            _antA += _antW * dt;
            _antA = Vector2.ClampMagnitude(_antA, 30f);
        }

        void UpdateFx(float dt)
        {
            float fs = _v.ForwardSpeed;
            float speed = Mathf.Abs(fs);
            bool engineOn = _v.role == VehicleRole.Player || _v.role == VehicleRole.Traffic || _v.role == VehicleRole.Police ||
                            _v.role == VehicleRole.MissionTarget;

            // lamps
            bool braking = (_v.lastThrottle < -0.05f && fs > 0.8f) || (_v.lastThrottle > 0.05f && fs < -0.8f) ||
                           (_v.lastHandbrake && speed > 1f && engineOn);
            bool reversing = fs < -0.4f && _v.lastThrottle < -0.05f;
            float bg = braking ? 1.6f : (engineOn ? 0.3f : 0.1f);
            float rg = reversing ? 1.4f : 0f;
            if (!Mathf.Approximately(bg, _brakeGlow)) { _brakeGlow = bg; SetLamps(_brake, bg); }
            if (!Mathf.Approximately(rg, _revGlow)) { _revGlow = rg; SetLamps(_reverse, rg); }

            if (_v.Wrecked) return;
            // exhaust: lazy puffs at idle, a stream when you floor it
            if (_exhaust && engineOn)
            {
                _puffT -= dt;
                bool gas = _v.lastThrottle > 0.4f && speed < 14f;
                if (_puffT <= 0f)
                {
                    _puffT = gas ? 0.07f : 0.7f;
                    var back = -transform.forward;
                    Fx.Puff(_exhaust.position, new Color(0.72f, 0.72f, 0.74f), back * (gas ? 2.5f : 0.8f) + Vector3.up * 0.4f,
                        gas ? 0.22f : 0.14f, gas ? 0.5f : 0.8f);
                }
            }
            // tyres: smoke + skid marks when sliding sideways, handbraking or doing a burnout
            var right = transform.right;
            float lat = Vector3.Dot(_v.Body.linearVelocity, right);
            bool burnout = _v.lastThrottle > 0.9f && speed < 6f && _v.PlayerInside && _v.grounded && Mathf.Abs(_accLocal.z) > 4f;
            bool slide = _v.grounded && ((Mathf.Abs(lat) > 3.5f && speed > 4f) || (_v.lastHandbrake && speed > 5f && engineOn) || burnout);
            var wheels = _v.Wheels;
            int rearA = _v.TwoWheeler ? 1 : 2;
            for (int i = 0; i < 2; i++)
            {
                var w = _v.TwoWheeler ? _v.RearWheelT : (rearA + i < wheels.Length ? wheels[rearA + i] : null);
                if (w == null) { _skidding[i] = false; continue; }
                var contact = w.position - transform.up * (_v.WheelRadius * 0.97f);
                if (slide)
                {
                    if (_skidding[i] && (contact - _lastSkid[i]).sqrMagnitude > 0.09f)
                    {
                        Fx.Skid(_lastSkid[i], contact, _v.TwoWheeler ? 0.1f : 0.2f);
                        _lastSkid[i] = contact;
                    }
                    else if (!_skidding[i]) _lastSkid[i] = contact;
                    _skidding[i] = true;
                }
                else _skidding[i] = false;
                if (_v.TwoWheeler) break;
            }
            if (slide)
            {
                _smokeT -= dt;
                if (_smokeT <= 0f)
                {
                    _smokeT = 0.05f;
                    int n = _v.TwoWheeler ? 1 : 2;
                    for (int i = 0; i < n; i++)
                    {
                        var w = _v.TwoWheeler ? _v.RearWheelT : wheels[rearA + i];
                        if (!w) continue;
                        // a billowing cluster: a few overlapping puffs of slightly different greys
                        for (int k = 0; k < 2; k++)
                            Fx.Puff(w.position - transform.up * _v.WheelRadius * 0.5f + Random.insideUnitSphere * 0.25f,
                                Color.Lerp(new Color(0.96f, 0.96f, 0.95f), new Color(0.78f, 0.78f, 0.8f), Random.value),
                                Vector3.up * 1.1f - transform.forward * 1.2f + Random.insideUnitSphere * 0.8f,
                                Random.Range(0.55f, 0.85f), Random.Range(0.8f, 1.2f));
                    }
                }
                if (_v.PlayerInside && Random.value < dt * 6f) ProcAudio.Play(ProcAudio.Blip, transform.position, 0.12f, 0.45f);
            }
            // damaged: a wisp of smoke from under the bonnet
            if (_v.health < _v.maxHealth * 0.4f)
            {
                _skidT -= dt;
                if (_skidT <= 0f)
                {
                    _skidT = 0.3f;
                    Fx.Puff(transform.TransformPoint(0, 1.0f, _v.halfLength * 0.6f), new Color(0.4f, 0.4f, 0.42f),
                        Vector3.up * 1.4f, 0.3f, 1.2f);
                }
            }
        }

        void LateUpdate()
        {
            if (_v == null) return;
            float t = Time.time;
            bool engineOn = _v.role == VehicleRole.Player || _v.role == VehicleRole.Traffic || _v.role == VehicleRole.Police ||
                            _v.role == VehicleRole.MissionTarget;
            float idle = engineOn && !_v.Wrecked ? Mathf.Clamp01(1f - Mathf.Abs(_v.ForwardSpeed) / 3f) : 0f;
            if (_body && !_v.TwoWheeler)
            {
                // engine idle: a fast little shudder
                float shake = idle * (Mathf.Sin(t * 41f) * 0.35f + Mathf.Sin(t * 27f) * 0.2f);
                var rot = Quaternion.Euler(_pitch, 0f, _roll + shake);
                _body.rotation = transform.rotation * rot * _bodyRel;
                _body.position = transform.TransformPoint(_bodyLocalPos + Vector3.up * (_heave + idle * 0.004f * Mathf.Sin(t * 33f)));
            }
            if (_wheel)
                _wheel.localRotation = Quaternion.AngleAxis(WheelTurn * WheelSpin, _wheelAxis) * _wheelRest;
            if (_orn)
                _orn.localRotation = Quaternion.AngleAxis(_ornA.x, _orn.parent.InverseTransformDirection(transform.right)) *
                                     Quaternion.AngleAxis(_ornA.y, _orn.parent.InverseTransformDirection(transform.forward)) * _ornRest;
            if (_ant)
                _ant.localRotation = Quaternion.AngleAxis(-_antA.x, _ant.parent.InverseTransformDirection(transform.right)) *
                                     Quaternion.AngleAxis(_antA.y, _ant.parent.InverseTransformDirection(transform.forward)) * _antRest;
        }

        public static void SetNight(bool night) => Night = night;

        /// <summary>How far the steering wheel is turned: degrees clockwise as the driver sees it (steering right is +).</summary>
        float WheelTurn => _v.SteerVisual * 140f;

        /// <summary>
        /// Which way AngleAxis about the (toward-the-driver) column turns the wheel clockwise for the driver: a
        /// positive angle about an axis runs clockwise seen from the axis tip, and the driver sits at the tip.
        /// </summary>
        const float WheelSpin = 1f;

        /// <summary>
        /// Where a hand holds the steering wheel: `clock` is the spot on the rim as the driver sees it with the
        /// wheel straight (9.5 = half past nine), turned with the wheel up to maxTurn degrees (past that the
        /// hands slip round it). toDriver is the column axis pointing back at the driver, outward points from
        /// the hub to the grip. False if the car has no steering wheel.
        /// </summary>
        public bool WheelGrip(float clock, float maxTurn, out Vector3 pos, out Vector3 toDriver, out Vector3 outward)
        {
            pos = toDriver = outward = Vector3.zero;
            if (_wheel == null || _v == null) return false;
            toDriver = _wheel.parent.TransformDirection(_wheelAxis).normalized;
            var up = Vector3.ProjectOnPlane(transform.up, toDriver).normalized;
            var right = Vector3.Cross(toDriver, up);                                       // the driver's right, on the rim plane
            float a = (clock / 12f * 360f + Mathf.Clamp(WheelTurn, -maxTurn, maxTurn)) * Mathf.Deg2Rad;
            outward = up * Mathf.Cos(a) + right * Mathf.Sin(a);
            pos = _wheel.position + outward * _wheelRadius;
            return true;
        }

        void OnDestroy()
        {
            if (_v != null) _v.OnCrash -= Crash;
        }
    }
}
