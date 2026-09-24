using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Smashable street furniture. Crates shatter into coins; lamp posts and barriers
    /// topple over. Both add a little "saman" heat when you do it in front of people.
    /// </summary>
    public class Breakable : MonoBehaviour
    {
        public enum Kind { Shatter, Topple }
        public Kind kind;
        public int coins;
        public System.Action onBroken;
        bool _broken;

        public static Breakable Make(GameObject go, Kind kind, int coins)
        {
            var b = go.AddComponent<Breakable>();
            b.kind = kind;
            b.coins = coins;
            if (kind == Kind.Shatter && go.GetComponent<Collider>() == null) ModelFactory.AddBoundsCollider(go);
            return b;
        }

        public void Hit(Vector3 from, float force, bool byPlayer)
        {
            if (_broken) return;
            _broken = true;
            onBroken?.Invoke();
            if (byPlayer) SamanMeter.I?.AddHeat(kind == Kind.Shatter ? 3f : 5f);
            if (kind == Kind.Shatter)
            {
                Fx.Burst(transform.position + Vector3.up * 0.5f, new Color(0.55f, 0.38f, 0.22f), 10, 5f);
                for (int i = 0; i < coins; i++)
                    Pickup.SpawnCoin(transform.position + Vector3.up * 0.8f, true);
                ProcAudio.Play(ProcAudio.Smash, transform.position, 0.7f, Random.Range(0.9f, 1.2f));
                Destroy(gameObject);
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
                Destroy(gameObject, 20f);
                for (int i = 0; i < coins; i++) Pickup.SpawnCoin(transform.position + Vector3.up, true);
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
