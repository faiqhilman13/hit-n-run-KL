using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Bola sepak: a kickabout on the kampung padang. A handful of kids chase the ball up and down, hoof it at
    /// the goals and shout for a pass. Walk on and you're in the game - run into the ball to dribble, kick to
    /// shoot - and if you score, the kids go wild and the coins come out. (Every great open world has
    /// somewhere you can just muck about; this is ours.)
    /// </summary>
    public class Football : MonoBehaviour
    {
        class Kid
        {
            public Transform t;
            public CharacterRig rig;
            public HeadLook look;
            public VoiceSynth.Profile voice;
            public string voiceKey;
            public int team;                 // 0 shoots at +z, 1 at -z
            public Vector3 spot;             // where they hang about when it isn't their turn
            public float kickT, speed;
        }

        Vector3 _centre;
        float _len, _wid;
        Rigidbody _ball;
        readonly List<Kid> _kids = new List<Kid>();
        float _resetT = -1f, _shoutT = 4f, _cheerT;
        bool _playerTouched;
        const float GoalHalf = 3.6f, Bar = 2.44f;

        static readonly string[] Calls = { "Hantar sini!", "Pass! Pass!", "Woi, aku free!", "Tembak la!", "Lari, lari!", "Offside tu!", "Jaga dia!" };
        static readonly string[] PlayerCalls = { "Abang, pass la!", "Main sekali!", "Bagi bola tu!", "Wah, pandai!" };

        public static Football Spawn(Vector3 centre, float length, float width, Transform parent)
        {
            var go = new GameObject("Football");
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<Football>();
            f._centre = centre;
            f._len = length;
            f._wid = width;
            f._ball = MakeBall(centre + Vector3.up * 0.4f, go.transform);
            // two little teams in whatever shirts they turned up in
            for (int i = 0; i < 4; i++)
            {
                int team = i % 2;
                float side = team == 0 ? -1f : 1f;
                var spot = centre + new Vector3((i < 2 ? -1f : 1f) * width * 0.22f, 0f, side * length * 0.18f);
                var model = ModelFactory.Spawn("chr_kid", spot, Quaternion.Euler(0f, team == 0 ? 0f : 180f, 0f), go.transform, "Budak");
                ModelFactory.Recolor(model, new Dictionary<string, Color>
                {
                    ["BatikBlue"] = team == 0 ? new Color(0.85f, 0.22f, 0.2f) : new Color(0.95f, 0.85f, 0.25f),
                    ["Pastel3"] = team == 0 ? new Color(0.85f, 0.22f, 0.2f) : new Color(0.95f, 0.85f, 0.25f),
                });
                foreach (var tr in model.GetComponentsInChildren<Transform>()) tr.gameObject.layer = Layers.Character;
                var kid = new Kid
                {
                    t = model.transform,
                    rig = model.AddComponent<CharacterRig>(),
                    look = model.AddComponent<HeadLook>(),
                    voice = VoiceSynth.Townsperson("kid", i % 3),
                    voiceKey = "kid" + (i % 3),
                    team = team,
                    spot = spot,
                    speed = Random.Range(3.4f, 4.2f),
                };
                kid.look.LookAt(f._ball.transform);
                f._kids.Add(kid);
            }
            return f;
        }

        /// <summary>A football: white panels and black pentagons on a once-split icosahedron, bouncy, light.</summary>
        static Rigidbody MakeBall(Vector3 at, Transform parent)
        {
            var go = new GameObject("Bola");
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            const float r = 0.19f;
            float p = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var ico = new List<Vector3>
            {
                new Vector3(-1, p, 0), new Vector3(1, p, 0), new Vector3(-1, -p, 0), new Vector3(1, -p, 0),
                new Vector3(0, -1, p), new Vector3(0, 1, p), new Vector3(0, -1, -p), new Vector3(0, 1, -p),
                new Vector3(p, 0, -1), new Vector3(p, 0, 1), new Vector3(-p, 0, -1), new Vector3(-p, 0, 1),
            };
            for (int i = 0; i < ico.Count; i++) ico[i] = ico[i].normalized;
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var white = ColorPalette.UV(new Color(0.96f, 0.96f, 0.94f));
            var black = ColorPalette.UV(new Color(0.12f, 0.12f, 0.14f));
            ColorPalette.Apply();
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var mid = (a + b + c).normalized;
                // the corners of the icosahedron become the black pentagons
                bool dark = false;
                foreach (var v in ico) if (Vector3.Dot(mid, v) > 0.93f) dark = true;
                var uv = dark ? black : white;
                // Unity draws clockwise faces: the face must point out of the ball
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), mid) < 0f) { var s = b; b = c; c = s; }
                verts.Add(a * r); verts.Add(b * r); verts.Add(c * r);
                uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
            }
            for (int i = 0; i < faces.Length; i += 3)
            {
                Vector3 a = ico[faces[i]], b = ico[faces[i + 1]], c = ico[faces[i + 2]];
                Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
                Tri(a, ab, ca); Tri(b, bc, ab); Tri(c, ca, bc); Tri(ab, bc, ca);
            }
            var mesh = new Mesh { name = "Bola" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            var tris = new int[verts.Count];
            for (int i = 0; i < tris.Length; i++) tris[i] = i;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ColorPalette.Material;
            var col = go.AddComponent<SphereCollider>();
            col.radius = r;
            col.sharedMaterial = new PhysicsMaterial("Bola") { bounciness = 0.6f, dynamicFriction = 0.45f, staticFriction = 0.5f, bounceCombine = PhysicsMaterialCombine.Maximum };
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.45f;
            rb.linearDamping = 0.25f;
            rb.angularDamping = 0.8f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return rb;
        }

        void Update()
        {
            if (_ball == null) return;
            var cam = Camera.main;
            float dt = Time.deltaTime;
            var b = _ball.position;
            var pc = PlayerController.I;
            bool near = cam != null && (cam.transform.position - _centre).sqrMagnitude < 140f * 140f;
            if (!near)
            {
                // nobody watching: the game pauses where it is
                if (!_ball.isKinematic) _ball.isKinematic = true;
                return;
            }
            if (_ball.isKinematic) _ball.isKinematic = false;

            // the player joins in by touching the ball
            if (pc != null && !pc.Driving && (pc.transform.position - b).sqrMagnitude < 1.6f * 1.6f) _playerTouched = true;

            // goal?
            float dz = b.z - _centre.z;
            if (_resetT < 0f && Mathf.Abs(dz) > _len * 0.5f && Mathf.Abs(b.x - _centre.x) < GoalHalf && b.y < _centre.y + Bar)
                Goal(dz > 0f ? 0 : 1);
            // out of play (or stuck in a hedge): back to the middle after a moment
            else if (_resetT < 0f && (Mathf.Abs(dz) > _len * 0.5f + 4f || Mathf.Abs(b.x - _centre.x) > _wid * 0.5f + 6f || b.y < _centre.y - 3f))
                _resetT = 2f;
            if (_resetT >= 0f && (_resetT -= dt) < 0f)
            {
                _ball.linearVelocity = Vector3.zero;
                _ball.angularVelocity = Vector3.zero;
                _ball.position = _centre + Vector3.up * 0.5f;
                _ball.transform.position = _ball.position;
                _playerTouched = false;
            }

            // each team sends its nearest kid after the ball; the rest drift with play
            Kid chaseA = Nearest(0, b), chaseB = Nearest(1, b);
            foreach (var k in _kids)
            {
                k.kickT -= dt;
                bool chasing = k == chaseA || k == chaseB;
                var goal = _centre + Vector3.forward * (k.team == 0 ? 1f : -1f) * (_len * 0.5f + 0.5f);
                Vector3 want;
                float speed;
                if (chasing && _resetT < 0f)
                {
                    // come round behind the ball, so the kick goes goalwards
                    var toGoal = goal - b; toGoal.y = 0f; toGoal.Normalize();
                    want = b - toGoal * 0.55f;
                    speed = k.speed;
                    var at = b - k.t.position; at.y = 0f;
                    if (at.magnitude < 0.85f && k.kickT <= 0f && b.y < k.t.position.y + 0.8f) Kick(k, toGoal);
                }
                else
                {
                    // hold position, shifted up and down with the ball
                    want = k.spot + new Vector3((b.x - _centre.x) * 0.3f, 0f, (b.z - _centre.z) * 0.45f);
                    speed = k.speed * 0.6f;
                }
                Run(k, want, speed, dt);
            }

            // shouting for the ball (or at you, if you've nicked it)
            if ((_shoutT -= dt) <= 0f)
            {
                _shoutT = Random.Range(3f, 6f);
                var k = _kids[Random.Range(0, _kids.Count)];
                bool atPlayer = _playerTouched && pc != null && (pc.transform.position - k.t.position).sqrMagnitude < 20f * 20f;
                Barks.Say(k.t, k.voice, k.voiceKey, (atPlayer ? PlayerCalls : Calls)[Random.Range(0, (atPlayer ? PlayerCalls : Calls).Length)], true, 1.95f, 0.8f, true);
            }
            if (_cheerT > 0f)
            {
                _cheerT -= dt;
                foreach (var k in _kids) if (k.rig) k.rig.gesture = _cheerT > 0f ? (int)CharacterRig.Gesture.Cheer : -1;
            }
        }

        Kid Nearest(int team, Vector3 to)
        {
            Kid best = null;
            float bd = float.MaxValue;
            foreach (var k in _kids)
            {
                if (k.team != team) continue;
                float d = (k.t.position - to).sqrMagnitude;
                if (d < bd) { bd = d; best = k; }
            }
            return best;
        }

        void Run(Kid k, Vector3 want, float speed, float dt)
        {
            var to = want - k.t.position;
            to.y = 0f;
            float d = to.magnitude;
            float v = d > 0.3f ? Mathf.Min(speed, d * 3f) : 0f;
            if (v > 0f)
            {
                var p = k.t.position + to / d * v * dt;
                p.y = _centre.y;
                k.t.position = p;
                k.t.rotation = Quaternion.Slerp(k.t.rotation, Quaternion.LookRotation(to), dt * 8f);
            }
            else
            {
                var face = _ball.position - k.t.position; face.y = 0f;
                if (face.sqrMagnitude > 0.01f) k.t.rotation = Quaternion.Slerp(k.t.rotation, Quaternion.LookRotation(face), dt * 5f);
            }
            if (k.rig) k.rig.speed = v;
        }

        void Kick(Kid k, Vector3 toGoal)
        {
            k.kickT = 0.7f;
            k.rig?.Kick();
            var dir = Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f) * toGoal;
            float power = Random.Range(5f, 9f);
            _ball.linearVelocity = dir * power + Vector3.up * Random.Range(0.5f, 3f);
            _playerTouched = false;
            ProcAudio.Play(ProcAudio.Punch, _ball.position, 0.25f, 1.6f);
        }

        void Goal(int team)
        {
            _resetT = 2.5f;
            var at = _ball.position + Vector3.up * 1.2f;
            Fx.Word(at, "GOL!!!", new Color(0.95f, 0.75f, 0.1f), 1.8f);
            Fx.Stars(at, 12, 6f);
            ProcAudio.Play(ProcAudio.Fanfare, at, 0.5f, 1.1f);
            _cheerT = 2.5f;
            var k = _kids[Random.Range(0, _kids.Count)];
            if (_playerTouched)
            {
                // your goal: the kids can't believe it
                Barks.Say(k.t, k.voice, k.voiceKey, Barks.Pick(Barks.Cheer), false, 1.95f, 0.9f, true);
                for (int i = 0; i < 5; i++) Pickup.SpawnCoin(at, true);
            }
            else Barks.Say(k.t, k.voice, k.voiceKey, "GOOOL!", false, 1.95f, 0.9f, true);
            _playerTouched = false;
        }
    }
}
