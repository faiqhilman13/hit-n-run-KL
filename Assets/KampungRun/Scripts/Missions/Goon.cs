using System;
using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>Something the player's punches and kicks land on (PlayerController.Attack) besides the townsfolk.</summary>
    public interface IPunchable
    {
        void Punched(Vector3 dir, float force, bool heavy);
    }

    /// <summary>
    /// A mission character run by the objective that owns it: MegaMaju guards on patrol (with a sight cone painted
    /// on the ground), cendol zombies shambling at a stall, a snatch thief legging it down the five-foot ways, the
    /// kid you're walking to the surau. Moves by itself along the ground (no physics body to push about), animates
    /// through the shared CharacterRig, can be punched, kicked or knocked flying by a car, and stays down or gets up.
    /// </summary>
    public class Goon : MonoBehaviour, IPunchable
    {
        public enum Mode { Idle, Patrol, Seek, Path, Follow }

        public Mode mode = Mode.Idle;
        public float speed = 1.5f;
        public string displayName = "";
        /// <summary>Patrol: walk these in a loop, pausing at each. Path: run along them once.</summary>
        public List<Vector3> points = new List<Vector3>();
        public int pointIndex;
        public float pause = 1.5f;
        /// <summary>Seek / Follow: who to walk at (or the fixed point when null).</summary>
        public Transform seek;
        public Vector3 seekPoint;
        public float stopDist = 1.1f;
        public bool faceAway;
        /// <summary>Hits it takes to go down (a car always does it).</summary>
        public int hp = 1;
        /// <summary>0: stays down for good; otherwise gets back up after this long.</summary>
        public float downTime;
        public bool Down { get; private set; }
        public bool invulnerable;
        /// <summary>Sight: 0 range = blind.</summary>
        public float sightRange, sightHalfAngle = 50f;
        public bool alert;                 // a guard who has noticed something (cone turns red)
        public Action<Goon> onDown;
        public Action<Goon, Vector3, float> onHit;
        public Action<Goon, Vehicle> onRunOver;
        public Action<Goon> onUp;
        public string[] chatter;           // said now and then as they walk
        public float chatterEvery = 7f;
        /// <summary>For the objective's own book-keeping (cured, delivered, already counted...).</summary>
        public int tag;

        CharacterRig _rig;
        Vector3 _vel, _lastPos;
        float _downT, _pauseT, _sayT, _hitCool, _stagger, _faceYaw;
        bool _hasYaw, _coneAlert;
        GameObject _cone;
        Material _coneMat;
        static readonly Color ConeCalm = new Color(1f, 0.85f, 0.25f, 0.55f), ConeAlert = new Color(1f, 0.2f, 0.12f, 0.7f);
        public static readonly List<Goon> All = new List<Goon>();

        public static int GroundMask => ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle | 1 << Layers.Ward | 1 << 2 | 1 << Layers.Detail);
        public static int SightMask => ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle | 1 << Layers.Ward | 1 << 2 | 1 << Layers.Detail);

        public static Goon Spawn(string model, string name, Vector3 pos, float yaw, Transform parent, Dictionary<string, Color> paint = null)
        {
            pos = Ground(pos);
            var go = ModelFactory.Spawn(model, pos, Quaternion.Euler(0, yaw, 0), parent, "Goon_" + name);
            if (paint != null) ModelFactory.Recolor(go, paint);
            var rig = go.AddComponent<CharacterRig>();
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Character;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.isTrigger = true;
            cap.radius = 0.5f;
            cap.height = 1.8f;
            cap.center = new Vector3(0, 0.9f, 0);
            var g = go.AddComponent<Goon>();
            g._rig = rig;
            g.displayName = name;
            g._lastPos = pos;
            return g;
        }

        /// <summary>The ground under a point (walkable surfaces only), or the point itself.</summary>
        public static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out var hit, 8f, GroundMask, QueryTriggerInteraction.Ignore)) return hit.point;
            return p;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public Vector3 Eye => transform.position + Vector3.up * 1.55f;

        /// <summary>Can this goon see point p (in range, in the cone, nothing solid in the way)?</summary>
        public bool CanSee(Vector3 p, float rangeMult = 1f)
        {
            if (Down || sightRange <= 0f) return false;
            var to = p - Eye;
            float d = to.magnitude;
            if (d > sightRange * rangeMult) return false;
            var flat = new Vector3(to.x, 0, to.z);
            if (flat.magnitude > 0.5f && Vector3.Angle(transform.forward, flat) > sightHalfAngle) return false;
            return !Physics.Linecast(Eye, p, SightMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Paint the sight cone on the ground (guards).</summary>
        public void ShowCone()
        {
            if (_cone != null || sightRange <= 0f) return;
            _cone = new GameObject("SightCone");
            _cone.transform.SetParent(transform, false);
            _cone.transform.localPosition = Vector3.up * 0.12f;
            var mf = _cone.AddComponent<MeshFilter>();
            var mr = _cone.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            const int seg = 14;
            var v = new Vector3[seg + 2];
            var tri = new int[seg * 3];
            // the beacon shader fades and bands by object-space height: give the apex y -1 and the rim y 0.3 and
            // squash the object flat, so the bands ripple outwards from the guard like a radar sweep
            _cone.transform.localScale = new Vector3(1f, 0.03f, 1f);
            v[0] = new Vector3(0, -1f, 0);
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(-sightHalfAngle, sightHalfAngle, i / (float)seg) * Mathf.Deg2Rad;
                v[i + 1] = new Vector3(Mathf.Sin(a) * sightRange, 0.3f, Mathf.Cos(a) * sightRange);
            }
            for (int i = 0; i < seg; i++) { tri[i * 3] = 0; tri[i * 3 + 1] = i + 1; tri[i * 3 + 2] = i + 2; }
            var m = new Mesh { name = "Cone", vertices = v, triangles = tri };
            m.RecalculateBounds();
            mf.sharedMesh = m;
            _coneMat = LatMaterials.Beam(ConeCalm);
            mr.sharedMaterial = _coneMat;
        }

        public void HideCone() { if (_cone) Destroy(_cone); _cone = null; }

        public void Say(string text, float volume = 0.85f)
        {
            if (string.IsNullOrEmpty(text)) return;
            Barks.Say(transform, "goon_" + displayName, text, false, 2.3f, volume);
        }

        public void FaceTowards(Vector3 p)
        {
            var to = p - transform.position; to.y = 0;
            if (to.sqrMagnitude > 0.01f) { _faceYaw = Quaternion.LookRotation(to).eulerAngles.y; _hasYaw = true; }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (GameManager.I != null && !GameManager.I.Playing) { if (_rig) _rig.speed = 0; return; }
            _hitCool -= dt;
            if (_coneMat && _coneAlert != alert) { _coneAlert = alert; _coneMat.SetColor("_Color", alert ? ConeAlert : ConeCalm); }
            if (_cone) _cone.SetActive(!Down);

            if (Down)
            {
                // the hop and slide of a knockdown, then lie there (or get up)
                _vel.y -= 20f * dt;
                var p = transform.position + _vel * dt;
                var g = Ground(p);
                if (p.y <= g.y) { p.y = g.y; _vel = Vector3.MoveTowards(new Vector3(_vel.x, 0, _vel.z), Vector3.zero, 8f * dt); }
                transform.position = p;
                if (_rig) _rig.speed = 0f;
                if (downTime > 0f && (_downT -= dt) <= 0f) GetUp();
                return;
            }

            Vector3 want = transform.position;
            float spd = speed;
            switch (mode)
            {
                case Mode.Patrol:
                    if (points.Count == 0) break;
                    if (_pauseT > 0f) { _pauseT -= dt; spd = 0f; break; }
                    want = points[pointIndex % points.Count];
                    if (Flat(want - transform.position).magnitude < 0.6f)
                    {
                        pointIndex = (pointIndex + 1) % points.Count;
                        _pauseT = pause;
                        // look the way the next leg goes while pausing (a guard scanning the street)
                        FaceTowards(points[pointIndex % points.Count]);
                    }
                    break;
                case Mode.Path:
                    if (pointIndex >= points.Count) { spd = 0f; break; }
                    want = points[pointIndex];
                    if (Flat(want - transform.position).magnitude < 1.2f) pointIndex++;
                    break;
                case Mode.Seek:
                case Mode.Follow:
                    want = seek ? seek.position : seekPoint;
                    if (Flat(want - transform.position).magnitude < stopDist)
                    {
                        spd = 0f;
                        // a bodyguard stands with his back to the boss, watching the crowd
                        FaceTowards(faceAway ? transform.position * 2f - want : want);
                    }
                    break;
                default:
                    spd = 0f;
                    break;
            }
            if (_stagger > 0f) { _stagger -= dt; spd *= 0.25f; }

            var to = Flat(want - transform.position);
            float d = to.magnitude;
            Vector3 step = Vector3.zero;
            if (spd > 0f && d > 0.05f)
            {
                step = to / d * Mathf.Min(spd * dt, d);
                _faceYaw = Quaternion.LookRotation(to).eulerAngles.y;
                _hasYaw = true;
            }
            var np = transform.position + step;
            var ground = Ground(np);
            if (Mathf.Abs(ground.y - np.y) < 1.4f) np.y = Mathf.MoveTowards(np.y, ground.y, 6f * dt + Mathf.Abs(ground.y - np.y) * 0.5f);
            transform.position = np;
            if (_hasYaw)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, _faceYaw, 0), dt * 8f);
            if (_rig)
            {
                _rig.speed = dt > 0f ? Flat(transform.position - _lastPos).magnitude / dt : 0f;
                _rig.grounded = true;
            }
            _lastPos = transform.position;

            if (chatter != null && chatter.Length > 0 && (_sayT -= dt) <= 0f)
            {
                _sayT = chatterEvery * UnityEngine.Random.Range(0.7f, 1.4f);
                var pc = PlayerController.I;
                if (pc != null && (pc.Focus - transform.position).sqrMagnitude < 30f * 30f)
                    Say(chatter[UnityEngine.Random.Range(0, chatter.Length)]);
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        public void Punched(Vector3 dir, float force, bool heavy)
        {
            if (Down || _hitCool > 0f) return;
            _hitCool = 0.15f;
            onHit?.Invoke(this, dir, force);
            if (invulnerable) return;
            hp -= heavy || force >= 10f ? 2 : 1;
            ProcAudio.Play(ProcAudio.Aduh, transform.position, 0.6f, UnityEngine.Random.Range(0.85f, 1.3f));
            if (hp <= 0) KnockDown(Flat(dir).normalized * (2.4f + force * 0.08f) + Vector3.up * 2.6f);
            else
            {
                _stagger = 0.6f;
                _rig?.Trigger("Hit");
                transform.position += Flat(dir).normalized * 0.4f;
            }
        }

        public void KnockDown(Vector3 vel)
        {
            if (Down) return;
            Down = true;
            hp = 0;
            _vel = vel;
            _downT = downTime;
            _rig?.Tumble(downTime > 0f ? Mathf.Min(downTime, 2.5f) : 2.5f);
            Fx.Stars(transform.position + Vector3.up * 1.4f, 8, 6f);
            onDown?.Invoke(this);
        }

        public void GetUp()
        {
            Down = false;
            hp = 1;
            _vel = Vector3.zero;
            onUp?.Invoke(this);
        }

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            var v = rb ? rb.GetComponent<Vehicle>() : null;
            if (v == null || Down) return;
            float sp = rb.linearVelocity.magnitude;
            if (sp < 4f) return;
            onRunOver?.Invoke(this, v);
            if (invulnerable) return;
            KnockDown(rb.linearVelocity * 0.45f + Vector3.up * (3.5f + sp * 0.15f));
            ProcAudio.Play(ProcAudio.Aduh, transform.position, 0.8f, 0.9f);
        }
    }
}
