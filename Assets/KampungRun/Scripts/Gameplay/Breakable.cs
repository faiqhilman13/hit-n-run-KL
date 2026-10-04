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

        void OnCollisionEnter(Collision c)
        {
            if (_broken) return;
            var v = c.rigidbody ? c.rigidbody.GetComponent<Vehicle>() : null;
            if (v != null && c.relativeVelocity.magnitude > 4f)
                Hit(c.rigidbody.position, c.relativeVelocity.magnitude, v.PlayerInside);
        }
    }
}
