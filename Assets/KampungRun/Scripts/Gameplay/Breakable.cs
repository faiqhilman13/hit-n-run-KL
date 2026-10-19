using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Smashable street furniture. Crates shatter into coins; lamp posts and barriers
    /// topple over. Both add a little "saman" heat when you do it in front of people, and draw a
    /// crowd's attention. Street furniture comes back a while later, out of sight (the way a
    /// Hit &amp; Run street is always full of things to smash), so the city never runs dry; mission
    /// props (anything with an onBroken) stay broken.
    /// </summary>
    public class Breakable : MonoBehaviour
    {
        public enum Kind { Shatter, Topple }
        public Kind kind;
        public int coins;
        public System.Action onBroken;
        public bool respawn = true;
        bool _broken;
        Vector3 _pos;
        Quaternion _rot;

        static readonly List<(Breakable b, float at)> Down = new List<(Breakable, float)>();
        static Respawner _runner;
        const float RespawnAfter = 90f;
        static readonly string[] Smashes = { "PRANG!", "KRAK!", "BEDEBUK!", "PANG!" };

        public static Breakable Make(GameObject go, Kind kind, int coins)
        {
            var b = go.AddComponent<Breakable>();
            b.kind = kind;
            b.coins = coins;
            b._pos = go.transform.position;
            b._rot = go.transform.rotation;
            if (kind == Kind.Shatter && go.GetComponent<Collider>() == null) ModelFactory.AddBoundsCollider(go);
            return b;
        }

        public void Hit(Vector3 from, float force, bool byPlayer)
        {
            if (_broken) return;
            _broken = true;
            if (onBroken != null) respawn = false;
            onBroken?.Invoke();
            if (byPlayer) SamanMeter.I?.AddHeat(kind == Kind.Shatter ? 3f : 5f);
            Pedestrian.React(transform.position, 14f, Pedestrian.Stir.Commotion);
            if (kind == Kind.Shatter)
            {
                Fx.Burst(transform.position + Vector3.up * 0.5f, new Color(0.55f, 0.38f, 0.22f), 10, 5f);
                if (byPlayer) Fx.Word(transform.position + Vector3.up * 1.4f, Smashes[Random.Range(0, Smashes.Length)]);
                for (int i = 0; i < coins; i++)
                    Pickup.SpawnCoin(transform.position + Vector3.up * 0.8f, true);
                ProcAudio.Play(ProcAudio.Smash, transform.position, 0.7f, Random.Range(0.9f, 1.2f));
                if (respawn) Retire();
                else Destroy(gameObject);
            }
            else
            {
                var rb = gameObject.AddComponent<Rigidbody>();
                rb.mass = 60f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;     // it's falling right in front of the camera
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                Vector3 dir = (transform.position - from);
                dir.y = 0;
                dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Random.insideUnitSphere;
                rb.AddForceAtPosition(dir * force * 40f, transform.position + Vector3.up * 4f, ForceMode.Impulse);
                ProcAudio.Play(ProcAudio.Clang, transform.position, 0.7f, Random.Range(0.8f, 1.1f));
                if (byPlayer) Fx.Word(transform.position + Vector3.up * 2f, "KLANG!");
                if (respawn) Invoke(nameof(Retire), 20f);
                else Destroy(gameObject, 20f);
                for (int i = 0; i < coins; i++) Pickup.SpawnCoin(transform.position + Vector3.up, true);
            }
        }

        /// <summary>Off the street until it can come back unseen.</summary>
        void Retire()
        {
            var rb = GetComponent<Rigidbody>();
            if (rb != null && kind == Kind.Topple) Destroy(rb);
            gameObject.SetActive(false);
            Down.Add((this, Time.time + RespawnAfter));
            if (_runner == null) _runner = new GameObject("BreakableRespawner").AddComponent<Respawner>();
        }

        void Restore()
        {
            CancelInvoke(nameof(Unignore));
            Unignore();
            transform.SetPositionAndRotation(_pos, _rot);
            _broken = false;
            gameObject.SetActive(true);
        }

        /// <summary>Puts things back once nobody is looking: far from the camera, or behind it.</summary>
        class Respawner : MonoBehaviour
        {
            float _check;

            void Update()
            {
                if ((_check -= Time.deltaTime) > 0f) return;
                _check = 2f;
                var cam = Camera.main;
                if (cam == null) return;
                var eye = cam.transform.position;
                for (int i = Down.Count - 1; i >= 0; i--)
                {
                    var (b, at) = Down[i];
                    if (b == null) { Down.RemoveAt(i); continue; }
                    if (Time.time < at) continue;
                    float d2 = (b._pos - eye).sqrMagnitude;
                    if (d2 < 60f * 60f) continue;
                    var vp = cam.WorldToViewportPoint(b._pos);
                    if (d2 < 160f * 160f && vp.z > 0f && vp.x > -0.15f && vp.x < 1.15f && vp.y > -0.15f && vp.y < 1.15f) continue;
                    Down.RemoveAt(i);
                    b.Restore();
                }
            }
        }

        Vehicle _smashedBy;
        int _smashStep = -1;

        /// <summary>
        /// A car ran into it. Fast enough and it's smashed or knocked flying, and the car keeps most of its
        /// speed rather than stopping dead as if it hit a wall (true: the car shouldn't take crash damage
        /// either). Called from both sides of the contact, so it's safe to call twice in the same step.
        /// </summary>
        public bool SmashedBy(Vehicle v, float rel)
        {
            if (_smashedBy == v && _smashStep == Time.frameCount) return true;
            if (_broken || rel <= 4f) return false;
            _smashedBy = v;
            _smashStep = Time.frameCount;
            Hit(v.Body.position, rel, v.PlayerInside);
            // give the car back what the contact took from it (a little less: you feel the thump)
            v.Body.linearVelocity = v.PreStepVelocity * (kind == Kind.Shatter ? 0.9f : 0.8f);
            v.Body.angularVelocity = v.PreStepAngular;
            if (kind == Kind.Topple && gameObject.activeInHierarchy)
            {
                // and don't let the falling post catch on the same car again
                _ignored.Clear();
                foreach (var mine in GetComponentsInChildren<Collider>())
                    foreach (var theirs in v.GetComponentsInChildren<Collider>())
                    {
                        Physics.IgnoreCollision(mine, theirs, true);
                        _ignored.Add((mine, theirs));
                    }
                Invoke(nameof(Unignore), 1.2f);
            }
            return true;
        }

        readonly List<(Collider a, Collider b)> _ignored = new List<(Collider, Collider)>();

        void Unignore()
        {
            foreach (var (a, b) in _ignored)
                if (a && b) Physics.IgnoreCollision(a, b, false);
            _ignored.Clear();
        }

        void OnCollisionEnter(Collision c)
        {
            var v = c.rigidbody ? c.rigidbody.GetComponent<Vehicle>() : null;
            if (v != null) SmashedBy(v, c.relativeVelocity.magnitude);
        }
    }
}
