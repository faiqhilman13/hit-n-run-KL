using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Burung merpati: flocks of pigeons bobbing about the squares (Dataran Merdeka, Pasar Seni, the masjid,
    /// Bukit Bintang, the KLCC park...). Run through them and they burst up in a clatter of wings, wheel round
    /// overhead and settle again a little way off; a horn or a punch-up nearby sends them up too. It is the
    /// cheapest thing there is for making a square feel like a real place. Each flock is one small mesh,
    /// rebuilt each frame only while the camera is close, so a couple of hundred birds cost a dozen draws.
    /// </summary>
    public class Pigeons : MonoBehaviour
    {
        static readonly List<Pigeons> All = new List<Pigeons>();

        class Bird
        {
            public Vector3 pos, vel, goal;
            public float yaw, pitch, t, flap, peck;
            public int state;            // 0 on the ground, 1 flying off, 2 coming in to land
        }

        Bird[] _birds;
        Vector3 _home;
        float _radius;
        Mesh _mesh;
        MeshRenderer _mr;
        Vector3[] _v;
        bool _shown;
        float _coo, _flutterT;
        const int Ground = ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle);
        const float ShowDist = 110f;

        // ------------------------------------------------------------------ the shape of a pigeon
        // a plump octahedron body (tail out behind), a small head with a pointed beak, and a wing each side
        // that lies folded along the back on the ground and spreads to beat in the air
        static Vector3[][] _faces;     // body then head: eight faces each, wound to face outward
        static Vector2[] _faceUV;
        static Vector2 _wingUV;
        const int BodyFaces = 16, VertsPerBird = BodyFaces * 3 + 8 * 3;

        static Vector3[][] Octa(Vector3 c, Vector3 px, Vector3 nx, Vector3 py, Vector3 ny, Vector3 pz, Vector3 nz)
        {
            var f = new[]
            {
                new[] { pz, py, nx }, new[] { pz, py, px }, new[] { pz, ny, nx }, new[] { pz, ny, px },
                new[] { nz, py, nx }, new[] { nz, py, px }, new[] { nz, ny, nx }, new[] { nz, ny, px },
            };
            foreach (var t in f)
            {
                // Unity draws clockwise faces: the cross product must point out of the shape
                var n = Vector3.Cross(t[1] - t[0], t[2] - t[0]);
                if (Vector3.Dot(n, (t[0] + t[1] + t[2]) / 3f - c) < 0f) { var s = t[1]; t[1] = t[2]; t[2] = s; }
            }
            return f;
        }

        static void BuildTemplate()
        {
            if (_faces != null) return;
            var bc = new Vector3(0f, 0.14f, -0.05f);
            var body = Octa(bc, new Vector3(0.08f, 0.13f, -0.03f), new Vector3(-0.08f, 0.13f, -0.03f), new Vector3(0f, 0.215f, -0.04f),
                new Vector3(0f, 0.055f, -0.02f), new Vector3(0f, 0.16f, 0.1f), new Vector3(0f, 0.16f, -0.27f));
            var hc = new Vector3(0f, 0.255f, 0.115f);
            var head = Octa(hc, hc + new Vector3(0.045f, 0f, 0f), hc - new Vector3(0.045f, 0f, 0f), hc + new Vector3(0f, 0.045f, 0f),
                hc - new Vector3(0f, 0.05f, 0.02f), hc + new Vector3(0f, -0.012f, 0.075f), hc - new Vector3(0f, 0f, 0.045f));
            _faces = new Vector3[BodyFaces][];
            for (int i = 0; i < 8; i++) { _faces[i] = body[i]; _faces[8 + i] = head[i]; }
            var grey = ColorPalette.UV(new Color(0.63f, 0.65f, 0.72f));
            var belly = ColorPalette.UV(new Color(0.72f, 0.73f, 0.77f));
            var neck = ColorPalette.UV(new Color(0.33f, 0.42f, 0.44f));      // the green-purple sheen of the head and throat
            _faceUV = new Vector2[BodyFaces];
            for (int i = 0; i < 8; i++) _faceUV[i] = i == 2 || i == 3 || i == 6 || i == 7 ? belly : grey;
            for (int i = 8; i < BodyFaces; i++) _faceUV[i] = neck;
            _wingUV = ColorPalette.UV(new Color(0.48f, 0.5f, 0.57f));
            ColorPalette.Apply();
        }

        /// <summary>Put flocks in the city's squares and plazas (call once the city is built).</summary>
        public static void Populate(CityBuilder.City city, Transform parent)
        {
            var root = new GameObject("Pigeons").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(77);
            foreach (var (key, flocks, birds) in new[]
            {
                ("Dataran", 2, 16), ("PasarSeni", 1, 12), ("Masjid", 1, 14), ("MasjidNegara", 1, 12), ("Pasar", 1, 12),
                ("BukitBintang", 1, 12), ("ChowKit", 1, 10), ("Mamak", 1, 8), ("Towers", 2, 12), ("Park", 1, 12),
                ("KLSentral", 1, 10), ("Brickfields", 1, 10), ("LRT", 1, 8), ("TamanPerdana", 1, 10), ("Padang", 1, 8),
            })
            {
                if (!city.places.TryGetValue(key, out var at)) continue;
                for (int f = 0; f < flocks; f++)
                {
                    var off = new Vector3((float)rng.NextDouble() * 24f - 12f, 0f, (float)rng.NextDouble() * 24f - 12f) * f;
                    var p = CityBuilder.InsidePatch(at + off) ? city.Sidewalk(at + off, (float)rng.NextDouble() * 20f - 10f) : at + off;
                    if (!Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 60f, Ground, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.point.y > p.y + 1.5f) continue;            // that's a roof: leave it
                    Spawn(hit.point, birds, 5.5f, root);
                }
            }
        }

        public static Pigeons Spawn(Vector3 home, int count, float radius, Transform parent)
        {
            BuildTemplate();
            var go = new GameObject("Flock");
            go.transform.SetParent(parent, false);
            go.layer = Layers.Pickup;
            var f = go.AddComponent<Pigeons>();
            f._home = home;
            f._radius = radius;
            f._birds = new Bird[count];
            for (int i = 0; i < count; i++)
            {
                var b = new Bird { pos = f.SpotNear(home, radius), yaw = Random.Range(0f, 360f), t = Random.Range(0f, 3f), peck = Random.value * 6f };
                b.goal = b.pos;
                f._birds[i] = b;
            }
            f._mesh = new Mesh { name = "Flock" };
            f._mesh.MarkDynamic();
            f._v = new Vector3[count * VertsPerBird];
            var uv = new Vector2[f._v.Length];
            var tris = new int[f._v.Length];
            for (int i = 0; i < count; i++)
            {
                int o = i * VertsPerBird;
                for (int k = 0; k < BodyFaces; k++) for (int j = 0; j < 3; j++) uv[o + k * 3 + j] = _faceUV[k];
                for (int k = BodyFaces * 3; k < VertsPerBird; k++) uv[o + k] = _wingUV;
            }
            for (int i = 0; i < tris.Length; i++) tris[i] = i;
            f._mesh.vertices = f._v;
            f._mesh.uv = uv;
            f._mesh.triangles = tris;
            go.AddComponent<MeshFilter>().sharedMesh = f._mesh;
            f._mr = go.AddComponent<MeshRenderer>();
            f._mr.sharedMaterial = ColorPalette.Material;
            f._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            f._mr.receiveShadows = false;
            f._mr.enabled = false;
            All.Add(f);
            return f;
        }

        void OnDestroy() => All.Remove(this);

        /// <summary>Something loud or fast here: every bird within `radius` takes off.</summary>
        public static void Startle(Vector3 pos, float radius)
        {
            foreach (var f in All)
            {
                float reach = radius + f._radius + 15f;
                if ((f._home - pos).sqrMagnitude > reach * reach) continue;
                f.Scare(pos, radius);
            }
        }

        void Scare(Vector3 from, float radius)
        {
            int n = 0;
            foreach (var b in _birds)
            {
                if (b.state == 1) continue;
                if ((b.pos - from).sqrMagnitude > radius * radius) continue;
                TakeOff(b, from);
                n++;
            }
            if (n > 0 && _flutterT <= 0f)
            {
                _flutterT = 0.6f;
                ProcAudio.PlayAt(ProcAudio.Flutter, _home + Vector3.up, Mathf.Clamp(0.25f + n * 0.05f, 0.3f, 0.75f), Random.Range(0.85f, 1.05f), 0.9f, 60f);
            }
        }

        void TakeOff(Bird b, Vector3 from)
        {
            var away = b.pos - from;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Random.insideUnitSphere;
            b.state = 1;
            b.t = Random.Range(2.5f, 5f);
            b.vel = away * Random.Range(3f, 5f) + Vector3.up * Random.Range(4f, 6f);
            b.flap = Random.value * 6f;
            // wheel round somewhere above the square
            b.goal = _home + new Vector3(Random.Range(-14f, 14f), Random.Range(7f, 12f), Random.Range(-14f, 14f));
        }

        Vector3 SpotNear(Vector3 c, float r)
        {
            for (int i = 0; i < 4; i++)
            {
                var o = Random.insideUnitCircle * r;
                var p = c + new Vector3(o.x, 0f, o.y);
                if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 6f, Ground, QueryTriggerInteraction.Ignore) &&
                    Mathf.Abs(hit.point.y - c.y) < 0.6f)
                    return hit.point;
            }
            return c;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float dt = Time.deltaTime;
            _flutterT -= dt;
            bool show = (cam.transform.position - _home).sqrMagnitude < ShowDist * ShowDist;
            if (show != _shown) { _shown = show; _mr.enabled = show; }
            if (!show) return;

            // walk too close (or drive near) and they go up
            var pc = PlayerController.I;
            if (pc != null)
            {
                var p = pc.Driving ? pc.Focus : pc.transform.position;
                float scare = pc.Driving ? (pc.Speed > 4f ? 9f : 5f) : pc.Speed > 5f ? 4.5f : 2.6f;
                if ((p - _home).sqrMagnitude < (scare + _radius + 3f) * (scare + _radius + 3f)) Scare(p, scare);
                if ((_coo -= dt) <= 0f)
                {
                    _coo = Random.Range(4f, 9f);
                    if ((p - _home).sqrMagnitude < 18f * 18f && _birds[0].state == 0)
                        ProcAudio.PlayAt(ProcAudio.Coo, _home + Vector3.up * 0.3f, 0.3f, Random.Range(0.9f, 1.1f), 0.95f, 30f);
                }
            }

            var threat = pc != null ? (pc.Driving ? pc.Focus : pc.transform.position) : _home + Vector3.one * 999f;
            foreach (var b in _birds) Step(b, dt, threat);
            Draw();
        }

        void Step(Bird b, float dt, Vector3 threat)
        {
            b.t -= dt;
            switch (b.state)
            {
                case 0:
                    // potter about: a few quick steps, a peck, a look round
                    b.peck += dt;
                    if (b.t <= 0f)
                    {
                        b.t = Random.Range(0.8f, 3f);
                        var o = Random.insideUnitCircle * 0.8f;
                        b.goal = b.pos + new Vector3(o.x, 0f, o.y);
                        if ((b.goal - _home).sqrMagnitude > _radius * _radius) b.goal = Vector3.Lerp(b.pos, _home, 0.3f);
                        b.goal.y = b.pos.y;
                    }
                    var to = b.goal - b.pos;
                    to.y = 0f;
                    if (to.sqrMagnitude > 0.004f)
                    {
                        b.pos += to.normalized * Mathf.Min(to.magnitude, 0.45f * dt);
                        b.yaw = Mathf.MoveTowardsAngle(b.yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, dt * 360f);
                    }
                    b.pitch = Mathf.Max(0f, Mathf.Sin(b.peck * 5f)) > 0.85f ? 35f : 0f;     // a quick bob down to the ground
                    break;
                case 1:
                    // flying: climb away, then wheel about the square
                    b.flap += dt * 22f;
                    var want = (b.goal - b.pos);
                    var steer = want.normalized * 7f - b.vel;
                    b.vel += Vector3.ClampMagnitude(steer, 9f * dt * 1.5f);
                    b.pos += b.vel * dt;
                    if (want.sqrMagnitude < 4f) b.goal = _home + new Vector3(Random.Range(-14f, 14f), Random.Range(7f, 12f), Random.Range(-14f, 14f));
                    Face(b, dt);
                    if (b.t <= 0f)
                    {
                        // come back down somewhere away from whoever scared them
                        b.state = 2;
                        var c = _home;
                        var away = _home - threat;
                        away.y = 0f;
                        if (away.sqrMagnitude < 15f * 15f && away.sqrMagnitude > 0.01f) c += away.normalized * 4f;
                        b.goal = SpotNear(c, _radius);
                    }
                    break;
                case 2:
                    b.flap += dt * 14f;
                    var land = b.goal - b.pos;
                    float d = land.magnitude;
                    var desired = d > 0.01f ? land / d * Mathf.Min(6f, d * 1.6f + 0.6f) : Vector3.zero;
                    b.vel = Vector3.MoveTowards(b.vel, desired, dt * 10f);
                    b.pos += b.vel * dt;
                    Face(b, dt);
                    if (d < 0.15f)
                    {
                        b.pos = b.goal;
                        b.state = 0;
                        b.pitch = 0f;
                        b.t = Random.Range(0.5f, 2f);
                        b.vel = Vector3.zero;
                    }
                    break;
            }
        }

        static void Face(Bird b, float dt)
        {
            var flat = new Vector3(b.vel.x, 0f, b.vel.z);
            if (flat.sqrMagnitude > 0.01f) b.yaw = Mathf.MoveTowardsAngle(b.yaw, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, dt * 400f);
            b.pitch = Mathf.Clamp(-Mathf.Atan2(b.vel.y, Mathf.Max(flat.magnitude, 0.5f)) * Mathf.Rad2Deg, -40f, 40f) * 0.6f;
        }

        void Draw()
        {
            var bounds = new Bounds(_home, Vector3.one * 2f);
            for (int i = 0; i < _birds.Length; i++)
            {
                var b = _birds[i];
                var rot = Quaternion.Euler(b.pitch, b.yaw, 0f);
                var at = b.pos - transform.position;
                int o = i * VertsPerBird;
                for (int k = 0; k < BodyFaces; k++)
                {
                    var f = _faces[k];
                    _v[o++] = at + rot * f[0];
                    _v[o++] = at + rot * f[1];
                    _v[o++] = at + rot * f[2];
                }
                // wings: folded along the back on the ground, spread and beating in the air
                bool air = b.state != 0;
                float beat = air ? Mathf.Sin(b.flap) : 0f;
                for (int side = -1; side <= 1; side += 2)
                {
                    var rootF = new Vector3(side * 0.055f, 0.185f, 0.05f);
                    var rootB = new Vector3(side * 0.05f, 0.185f, -0.1f);
                    Vector3 tipF, tipB;
                    if (air)
                    {
                        float lift = beat * 0.2f, span = 0.34f - Mathf.Abs(beat) * 0.08f;
                        tipF = new Vector3(side * span, 0.185f + lift, -0.02f);
                        tipB = new Vector3(side * span * 0.9f, 0.185f + lift * 0.9f, -0.14f);
                    }
                    else
                    {
                        tipF = new Vector3(side * 0.07f, 0.19f, -0.2f);
                        tipB = new Vector3(side * 0.035f, 0.18f, -0.29f);
                    }
                    Vector3 a = at + rot * rootF, c = at + rot * rootB, tf = at + rot * tipF, tb = at + rot * tipB;
                    // a quad, both sides of the feathers
                    _v[o++] = a; _v[o++] = tf; _v[o++] = c;
                    _v[o++] = a; _v[o++] = c; _v[o++] = tf;
                    _v[o++] = c; _v[o++] = tf; _v[o++] = tb;
                    _v[o++] = c; _v[o++] = tb; _v[o++] = tf;
                }
                bounds.Encapsulate(b.pos);
            }
            _mesh.vertices = _v;
            _mesh.RecalculateNormals();
            bounds.Expand(1f);
            bounds.center -= transform.position;
            _mesh.bounds = bounds;
        }
    }
}
