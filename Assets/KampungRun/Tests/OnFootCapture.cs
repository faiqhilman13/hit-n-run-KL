using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace KampungRun.Tests
{
    /// <summary>
    /// A look at the street on foot: stills from the squares and markets (what's around you, who's about,
    /// the pigeons, the hawkers) and a scripted run that plays the on-foot moves - jog, sprint, jump and
    /// flip, ground-pound, punch, bump into people, scatter pigeons, bounce up the stall canopies - written
    /// frame by frame (30 fps) to Tools/promo_frames/x_onfoot/. Explicit: only when asked for by name.
    /// </summary>
    [Explicit, Category("Promo")]
    public class OnFootCapture
    {
        const int W = 1280, H = 720, FPS = 30;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "x_onfoot"));

        RenderTexture _rt;
        Texture2D _tex;
        int _n;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Seconds(float s) => Frames(Mathf.RoundToInt(s * FPS));

        void Grab(string file)
        {
            var cam = Camera.main;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = _rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(file, _tex.EncodeToJPG(90));
        }

        IEnumerator Boot(int level)
        {
            Time.captureFramerate = 0;
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = level;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(40);
            HUD.I.DebugSkipDialogue();
            GameInput.Locked = false;
            // the virtual stick only drives the player with the touch layer on; the HUD stays out of shot
            TouchControls.ForceOn = true;
            foreach (var c in Object.FindObjectsByType<Canvas>()) c.enabled = false;
            Time.captureFramerate = FPS;
            _rt = new RenderTexture(W, H, 24);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            Directory.CreateDirectory(Root);
            yield return Frames(10);
        }

        static Vector3 Walkway(string place, float along = 0f)
        {
            var city = GameManager.I.City;
            return city.Sidewalk(city.places[place], along);
        }

        static string Census(Vector3 at)
        {
            int peds = 0, npcs = 0;
            foreach (var p in Pedestrian.All) if (p && (p.transform.position - at).sqrMagnitude < 30f * 30f) peds++;
            foreach (var n in Object.FindObjectsByType<NPC>()) if ((n.transform.position - at).sqrMagnitude < 30f * 30f) npcs++;
            return $"people within 30 m {peds}, named {npcs}";
        }

        /// <summary>How loud each sound bed is (RMS of the clip x the source volume), against the music.</summary>
        static void Loudness()
        {
            float Rms(AudioClip c)
            {
                if (c == null) return 0f;
                var data = new float[c.samples * c.channels];
                c.GetData(data, 0);
                double sum = 0;
                foreach (var x in data) sum += x * x;
                return Mathf.Sqrt((float)(sum / Mathf.Max(1, data.Length)));
            }
            var music = GameObject.Find("Music");
            if (music != null) { var m = music.GetComponent<AudioSource>(); Debug.Log($"[OnFoot] music rms {Rms(m.clip):F3} x vol {m.volume:F2} = {Rms(m.clip) * m.volume:F3}"); }
            var amb = GameObject.Find("Ambience");
            if (amb != null)
                foreach (var src in amb.GetComponents<AudioSource>())
                    Debug.Log($"[OnFoot] {src.clip.name} rms {Rms(src.clip):F3} x vol {src.volume:F2} = {Rms(src.clip) * src.volume:F3}");
        }

        /// <summary>Stills: stand at each square and look along the pavement, then from a little higher up.</summary>
        [UnityTest]
        public IEnumerator OnFootStills()
        {
            yield return Boot(1);
            var p = GameManager.I.Player;
            var cc = ChaseCamera.I;
            var dir = Path.Combine(Root, "stills");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            foreach (var place in new[] { "Dataran", "Pasar", "ChowKit", "BukitBintang", "Towers", "Masjid", "PasarSeni", "Home", "Mamak", "KLSentral" })
            {
                if (!GameManager.I.City.places.ContainsKey(place)) continue;
                var at = Walkway(place);
                var ahead = Walkway(place, 6f) - at;
                ahead.y = 0f;
                var face = ahead.sqrMagnitude > 0.01f ? Quaternion.LookRotation(ahead) : Quaternion.identity;
                p.Teleport(at + Vector3.up * 0.3f, face);
                cc.target = p.transform;
                cc.SnapBehind();
                yield return Seconds(4f);          // let the street carry on round us
                Grab(Path.Combine(dir, place + "_a.jpg"));
                // and from over the shoulder, a little higher, turned to the street
                cc.yaw += 60f;
                cc.pitch = 24f;
                yield return Seconds(1.5f);
                Grab(Path.Combine(dir, place + "_b.jpg"));
                Debug.Log($"[OnFoot] {place}: {Census(at)}");
                Loudness();
            }
            TouchControls.ForceOn = false;
            Time.captureFramerate = 0;
        }

        /// <summary>
        /// The moves, recorded: a jog through Dataran with the crowd, a sprint into the pigeons, jump + flip,
        /// a ground-pound in front of onlookers, a punch, a barge, then Petaling Street's stall canopies.
        /// </summary>
        [UnityTest]
        public IEnumerator OnFootRun()
        {
            yield return Boot(1);
            var p = GameManager.I.Player;
            var cc = ChaseCamera.I;
            var dir = Path.Combine(Root, "run");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            _n = 0;
            IEnumerator Rec(float seconds, System.Action<float> each = null)
            {
                int frames = Mathf.RoundToInt(seconds * FPS);
                for (int i = 0; i < frames; i++)
                {
                    each?.Invoke(i / (float)frames);
                    yield return null;
                    Grab(Path.Combine(dir, $"{_n++:D5}.jpg"));
                }
            }
            // (on touch a full stick is a sprint: 0.9 jogs)
            void Stick(Vector3 worldDir, float mag = 0.9f)
            {
                // the stick is read relative to the camera
                var cam = Camera.main.transform;
                var f = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                var r = Vector3.Cross(Vector3.up, f);
                worldDir.y = 0f;
                worldDir.Normalize();
                TouchControls.DebugStick = new Vector2(Vector3.Dot(worldDir, r), Vector3.Dot(worldDir, f)) * mag;
            }

            // 1. Dataran: jog along the pavement past people
            var a = Walkway("Dataran", -20f);
            var b = Walkway("Dataran", 10f);
            var run = b - a; run.y = 0f;
            p.Teleport(a + Vector3.up * 0.3f, Quaternion.LookRotation(run));
            cc.target = p.transform;
            cc.SnapBehind();
            yield return Seconds(1f);
            yield return Rec(4f, t => Stick(Walkway("Dataran", Mathf.Lerp(-18f, 14f, t)) - p.transform.position));
            // 2. sprint, jump, double jump flip
            yield return Rec(1.2f, t => Stick(p.transform.forward, 1f));
            p.DebugJump();
            yield return Rec(0.35f, t => Stick(p.transform.forward, 1f));
            p.DebugJump();
            yield return Rec(1.3f, t => Stick(p.transform.forward, 1f));
            TouchControls.DebugStick = Vector2.zero;
            yield return Rec(0.6f);
            // 3. the pigeons: put a flock in front and run through it
            var flockAt = p.transform.position + p.transform.forward * 7f;
            var flock = Pigeons.Spawn(flockAt, 14, 3.5f, GameManager.I.transform);
            yield return Rec(1.2f);
            yield return Rec(2.2f, t => Stick(flockAt - p.transform.position + p.transform.forward));
            TouchControls.DebugStick = Vector2.zero;
            yield return Rec(1.8f);
            // 4. a ground-pound among a few onlookers, then a punch, then a barge through them
            {
                var here = p.transform.position;
                var fwd = p.transform.forward; fwd.y = 0f; fwd.Normalize();
                var zone = WalkZone.FromRect(new Rect(here.x - 15f, here.z - 15f, 30f, 30f), 0f);
                var crowd = new List<Pedestrian>();
                string[] who = { "chr_townman", "chr_townaunty", "chr_pakcik", "chr_kid" };
                for (int i = 0; i < 4; i++)
                {
                    var spot = here + Quaternion.Euler(0f, -60f + i * 40f, 0f) * fwd * 4.5f;
                    var q = PedestrianSpawner.Spawn(spot, zone, GameManager.I.transform, who[i]);
                    crowd.Add(q);
                }
                cc.SnapBehind();
                yield return Rec(1.2f);
                p.DebugJump();
                yield return Rec(0.45f);
                p.DebugStomp();
                yield return Rec(2.4f);
                Pedestrian mark = null;
                foreach (var q in crowd) if (q != null && q.Idle) { mark = q; break; }
                if (mark == null) mark = crowd[0];
                var to = mark.transform.position - p.transform.position; to.y = 0f;
                p.Teleport(mark.transform.position - to.normalized * 1.6f + Vector3.up * 0.1f, Quaternion.LookRotation(to));
                cc.SnapBehind();
                yield return Rec(0.5f);
                for (int i = 0; i < 3; i++) { p.DebugPunch(1); yield return Rec(0.35f); }
                yield return Rec(1.6f);
                // 5. and walk straight through the rest of them
                var through = Vector3.zero;
                foreach (var q in crowd) if (q != null && q != mark) through += q.transform.position;
                through = through / 3f - p.transform.position; through.y = 0f;
                yield return Rec(2.5f, t => Stick(through, 0.9f));
                TouchControls.DebugStick = Vector2.zero;
                yield return Rec(1.2f);
            }
            // 6. Petaling Street: bounce along the stall canopies
            var stalls = new List<Bouncy>(Object.FindObjectsByType<Bouncy>());
            var pasar = GameManager.I.City.places["Pasar"];
            stalls.Sort((x, y) => (x.transform.position - pasar).sqrMagnitude.CompareTo((y.transform.position - pasar).sqrMagnitude));
            if (stalls.Count > 0)
            {
                // run up, jump, double jump at the top, drift over the canopy and let go: it should bounce you
                var top = stalls[0].GetComponent<BoxCollider>().bounds;
                var c = top.center; c.y = 0f;
                var side = Walkway("Pasar", 0f);
                var toS = c - new Vector3(side.x, 0f, side.z);
                toS.Normalize();
                float half = Mathf.Max(top.extents.x, top.extents.z);
                var start = c - toS * (half + 5f);
                if (Physics.Raycast(start + Vector3.up * 10f, Vector3.down, out var gh, 20f,
                        ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle | 1 << 2), QueryTriggerInteraction.Ignore))
                    start.y = gh.point.y;
                Debug.Log($"[OnFoot] stall canopy top {top.max.y - start.y:F2} m above the street, {half:F1} m across");
                p.Teleport(start + Vector3.up * 0.2f, Quaternion.LookRotation(toS));
                cc.SnapBehind();
                yield return Rec(0.6f);
                int phase = 0;
                float maxY = start.y;
                yield return Rec(5f, t =>
                {
                    var flat = p.transform.position; flat.y = 0f;
                    float edge = (flat - c).magnitude - half;
                    maxY = Mathf.Max(maxY, p.transform.position.y);
                    if (phase == 0) { Stick(toS); if (edge < 2.8f) { p.DebugJump(); phase = 1; } }
                    else if (phase == 1) { Stick(toS); if (p.Velocity.y < 1.5f) { p.DebugJump(); phase = 2; } }
                    else if (phase == 2) { if (edge < -half * 0.4f) phase = 3; else Stick(toS); }
                    else TouchControls.DebugStick = Vector2.zero;
                });
                TouchControls.DebugStick = Vector2.zero;
                Debug.Log($"[OnFoot] stall bounce: highest {maxY - start.y:F1} m above the street (phase {phase})");
            }
            // 7. the kampung: chickens
            var home = GameManager.I.City.places["HomeYard"];
            Critter hen = null;
            float best = float.MaxValue;
            foreach (var c in Object.FindObjectsByType<Critter>())
                if (c.isChicken && (c.transform.position - home).sqrMagnitude < best) { best = (c.transform.position - home).sqrMagnitude; hen = c; }
            if (hen != null)
            {
                var hp = hen.transform.position;
                p.Teleport(hp - Vector3.forward * 1.4f + Vector3.up * 0.2f, Quaternion.LookRotation(Vector3.forward));
                cc.SnapBehind();
                yield return Rec(0.5f);
                p.DebugKick();
                yield return Rec(2.5f);
            }
            // 8. the padang: the kids' kickabout, then join in
            if (GameManager.I.City.places.TryGetValue("Padang", out var pad))
            {
                p.Teleport(pad + new Vector3(25f, 0.2f, 0f), Quaternion.LookRotation(Vector3.left));
                cc.SnapBehind();
                yield return Rec(4f);
                var ball = GameObject.Find("Bola");
                if (ball != null)
                {
                    yield return Rec(3f, t =>
                    {
                        var to = ball.transform.position - p.transform.position; to.y = 0f;
                        if (to.magnitude > 1.2f) Stick(to, 1f); else { TouchControls.DebugStick = Vector2.zero; }
                    });
                    TouchControls.DebugStick = Vector2.zero;
                    p.DebugKick();
                    yield return Rec(2.5f);
                    Debug.Log($"[OnFoot] football: ball at {ball.transform.position - pad}");
                }
            }
            TouchControls.DebugStick = Vector2.zero;
            TouchControls.ForceOn = false;
            Time.captureFramerate = 0;
            Debug.Log($"[OnFoot] run: {_n} frames");
        }

        static Pedestrian Nearest(Vector3 at, float within)
        {
            Pedestrian best = null;
            float bd = within * within;
            foreach (var q in Pedestrian.All)
            {
                if (q == null || !q.Idle) continue;
                float d = (q.transform.position - at).sqrMagnitude;
                if (d < bd) { bd = d; best = q; }
            }
            return best;
        }
    }
}
