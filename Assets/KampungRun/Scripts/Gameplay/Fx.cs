using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Cartoon effects drawn with ink blobs: dust puffs, smoke, "POW!" bursts and
    /// comic sound-word popups ("DUSH!", "PANG!") like a newspaper strip.
    /// </summary>
    public class Fx : MonoBehaviour
    {
        static Fx _i;
        static Fx I
        {
            get
            {
                if (_i == null) _i = new GameObject("Fx").AddComponent<Fx>();
                return _i;
            }
        }

        class Blob
        {
            public Transform t;
            public Vector3 v;
            public float life, max, size;
            public bool gravity, puff;
        }

        // round puffs (smoke, exhaust, tyre smoke) share one low-poly sphere mesh
        static Mesh _sphere;
        static Mesh Sphere
        {
            get
            {
                if (_sphere == null)
                {
                    var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    _sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
                    Destroy(tmp);
                }
                return _sphere;
            }
        }
        readonly Stack<Transform> _puffPool = new Stack<Transform>();

        Transform GetPuff(Color c)
        {
            Transform t;
            if (_puffPool.Count > 0) { t = _puffPool.Pop(); t.gameObject.SetActive(true); }
            else
            {
                var go = new GameObject("puff");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Sphere;
                go.AddComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.layer = Layers.Pickup;
                t = go.transform;
            }
            t.GetComponent<MeshRenderer>().sharedMaterial = LatMaterials.Get(c, 0f, SurfaceKinds.None);
            return t;
        }

        /// <summary>Soft round puff that swells and fades (exhaust, tyre smoke, damage smoke).</summary>
        public static void Puff(Vector3 pos, Color c, Vector3 v, float size, float life)
        {
            if (I._blobs.Count > 260) return;
            var t = I.GetPuff(c);
            t.position = pos;
            t.rotation = Random.rotation;
            t.localScale = Vector3.one * size * 0.3f;
            I._blobs.Add(new Blob { t = t, v = v, life = life, max = life, size = size, puff = true });
        }

        // skid marks: flat dark strips laid along the ground, recycled oldest-first
        readonly Queue<Transform> _skids = new Queue<Transform>();
        const int MaxSkids = 220;

        public static void Skid(Vector3 from, Vector3 to, float width)
        {
            var i = I;
            Transform t;
            if (i._skids.Count >= MaxSkids) t = i._skids.Dequeue();
            else
            {
                t = Shapes.Box("skid", Vector3.zero, Vector3.one, new Color(0.16f, 0.16f, 0.17f), i.transform, false, 0f).transform;
                t.GetComponent<MeshRenderer>().sharedMaterial = LatMaterials.Get(new Color(0.16f, 0.16f, 0.17f), 0f, SurfaceKinds.None);
                t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t.gameObject.layer = Layers.Pickup;
            }
            var d = to - from;
            if (d.sqrMagnitude < 1e-4f) return;
            t.position = (from + to) * 0.5f + Vector3.up * 0.02f;
            t.rotation = Quaternion.LookRotation(new Vector3(d.x, 0, d.z).normalized == Vector3.zero ? Vector3.forward : new Vector3(d.x, 0, d.z).normalized);
            t.localScale = new Vector3(width, 0.005f, d.magnitude + 0.05f);
            i._skids.Enqueue(t);
        }

        readonly List<Blob> _blobs = new List<Blob>();
        readonly Stack<Transform> _pool = new Stack<Transform>();

        Transform Get(Color c)
        {
            Transform t;
            if (_pool.Count > 0) { t = _pool.Pop(); t.gameObject.SetActive(true); }
            else
            {
                t = Shapes.Box("fx", Vector3.zero, Vector3.one, c, transform, false, 1.5f).transform;
                t.gameObject.layer = Layers.Pickup;
            }
            t.GetComponent<MeshRenderer>().sharedMaterial = LatMaterials.Get(c, 1.5f, SurfaceKinds.None);
            return t;
        }

        void Spawn(Vector3 pos, Color c, Vector3 v, float size, float life, bool gravity)
        {
            var t = Get(c);
            t.position = pos;
            t.rotation = Random.rotation;
            t.localScale = Vector3.one * size;
            _blobs.Add(new Blob { t = t, v = v, life = life, max = life, size = size, gravity = gravity });
        }

        public static void Burst(Vector3 pos, Color c, int count, float speed)
        {
            for (int i = 0; i < count; i++)
                I.Spawn(pos, c, Random.insideUnitSphere * speed + Vector3.up * speed * 0.5f, Random.Range(0.12f, 0.3f), Random.Range(0.5f, 1.1f), true);
        }

        public static void Smoke(Vector3 pos)
        {
            Puff(pos + Random.insideUnitSphere * 0.4f, new Color(0.3f, 0.29f, 0.3f), Vector3.up * 2f + Random.insideUnitSphere * 0.5f, 0.6f, 1.4f);
        }

        public static void Dust(Vector3 pos)
        {
            Puff(pos + Random.insideUnitSphere * 0.2f, new Color(0.88f, 0.82f, 0.7f), Vector3.up * 0.8f + Random.insideUnitSphere, 0.35f, 0.7f);
        }

        static readonly string[] Words = { "DUSH!", "PANG!", "BUK!", "PAP!", "DEBUK!", "PRANG!" };

        /// <summary>Comic sound word that pops up and floats away.</summary>
        public static void Word(Vector3 pos, string word = null)
        {
            word ??= Words[Random.Range(0, Words.Length)];
            var go = new GameObject("Word");
            go.transform.SetParent(I.transform, false);
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = word;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = 0.12f;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(0.85f, 0.2f, 0.15f);
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            go.AddComponent<FloatingWord>();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _blobs.Count - 1; i >= 0; i--)
            {
                var b = _blobs[i];
                b.life -= dt;
                if (b.life <= 0)
                {
                    b.t.gameObject.SetActive(false);
                    (b.puff ? _puffPool : _pool).Push(b.t);
                    _blobs.RemoveAt(i);
                    continue;
                }
                if (b.gravity) b.v += Physics.gravity * dt;
                else b.v *= 1f - dt;
                b.t.position += b.v * dt;
                float k = b.life / b.max;
                if (b.puff)
                {
                    // pop up fast, drift and swell, then shrink away
                    float age = 1f - k;
                    float sz = age < 0.15f ? age / 0.15f : Mathf.Lerp(1f, 1.5f, (age - 0.15f) / 0.85f) * Mathf.Clamp01(k * 3f);
                    b.t.localScale = Vector3.one * b.size * sz;
                    b.v += Vector3.up * 0.6f * dt;
                    continue;
                }
                b.t.localScale = Vector3.one * b.size * (b.gravity ? k : (1.6f - k * 0.6f) * k * 1.8f);
            }
        }
    }

    public class FloatingWord : MonoBehaviour
    {
        float _t;

        void Update()
        {
            _t += Time.deltaTime;
            var cam = Camera.main;
            if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            transform.position += Vector3.up * Time.deltaTime * 1.2f;
            float s = _t < 0.12f ? _t / 0.12f * 1.4f : Mathf.Lerp(1.4f, 1f, (_t - 0.12f) * 5f);
            transform.localScale = Vector3.one * s;
            if (_t > 0.9f) Destroy(gameObject);
        }
    }
}
