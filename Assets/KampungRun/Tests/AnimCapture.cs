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
    /// The motion library in the game: the family on Dataran Merdeka with a camera alongside, through the
    /// gaits (walk, jog, run, sprint, skid), the air (jump, flip, ground-pound, landing), the combo and the
    /// punt, a fidget, then a crowd reacting to a ground-pound. Frames (30 fps) go to
    /// Tools/promo_frames/x_anim/, with segments.txt listing where each part starts. Explicit: only by name.
    /// </summary>
    [Explicit, Category("Promo")]
    public class AnimCapture
    {
        const int W = 1280, H = 720, FPS = 30;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "x_anim"));

        RenderTexture _rt;
        Texture2D _tex;
        int _n;
        Camera _cam;
        Vector3 _camPos;
        System.Text.StringBuilder _seg;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        void Grab()
        {
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _rt };
            if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
            else { _cam.targetTexture = _rt; _cam.Render(); _cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(Root, $"{_n++:D5}.jpg"), _tex.EncodeToJPG(88));
        }

        Vector3 _center;

        /// <summary>The camera stands in the middle of the ring the player runs round, so it always sees them side on.</summary>
        Vector3? _lookAt;

        void Film(PlayerController p, float height, bool snap = false)
        {
            var target = _lookAt ?? p.transform.position + Vector3.up * height;
            var eye = _center + Vector3.up * (height + 0.25f);
            if (snap) _camPos = eye;
            _cam.transform.position = eye;
            var look = Quaternion.LookRotation(target - eye);
            _cam.transform.rotation = snap ? look : Quaternion.Slerp(_cam.transform.rotation, look, 1f - Mathf.Exp(-Time.deltaTime * 12f));
        }

        IEnumerator Rec(PlayerController p, float seconds, System.Action<float> each = null, float height = 0.8f)
        {
            int frames = Mathf.RoundToInt(seconds * FPS);
            for (int i = 0; i < frames; i++)
            {
                each?.Invoke(i / (float)frames);
                if (_clear) ClearStage();
                yield return null;
                Film(p, height);
                Grab();
            }
        }

        bool _clear = true;

        /// <summary>Keep strollers off the stage (the spawner keeps topping the crowd up).</summary>
        void ClearStage()
        {
            foreach (var q in new List<Pedestrian>(Pedestrian.All))
                if (q && (q.transform.position - _center).sqrMagnitude < 26f * 26f) Object.Destroy(q.gameObject);
        }

        /// <summary>The nearest patch of open ground (nothing standing within the ring) to a place.</summary>
        static Vector3 FindOpen(Vector3 near, float radius)
        {
            int mask = ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << 2);
            var best = near;
            float bestD = float.MaxValue;
            var hits = new Collider[64];
            for (float x = -90f; x <= 90f; x += 6f)
            for (float z = -90f; z <= 90f; z += 6f)
            {
                var at = near + new Vector3(x, 0f, z);
                if (!Physics.Raycast(at + Vector3.up * 30f, Vector3.down, out var g, 60f, mask, QueryTriggerInteraction.Ignore)) continue;
                at = g.point;
                if (Mathf.Abs(at.y - near.y) > 1.5f) continue;
                int n = Physics.OverlapCapsuleNonAlloc(at + Vector3.up * (radius + 0.4f), at + Vector3.up * (radius + 3f), radius, hits, mask, QueryTriggerInteraction.Ignore);
                if (n > 0) continue;
                // and level ground all round
                bool flat = true;
                for (int k = 0; k < 8 && flat; k++)
                {
                    var o = at + Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * radius;
                    flat = Physics.Raycast(o + Vector3.up * 5f, Vector3.down, out var h, 10f, mask, QueryTriggerInteraction.Ignore) && Mathf.Abs(h.point.y - at.y) < 0.25f;
                }
                if (!flat) continue;
                float d = (at - near).sqrMagnitude;
                if (d < bestD) { bestD = d; best = at; }
            }
            return best;
        }

        /// <summary>Round the ring, anticlockwise (or back the other way).</summary>
        Vector3 Tangent(PlayerController p, bool back = false)
        {
            var r = p.transform.position - _center;
            r.y = 0f;
            var t = Vector3.Cross(Vector3.up, r.normalized);
            // steer back onto the ring if they drift
            t += -r.normalized * (r.magnitude - Ring) * 0.35f;
            return back ? -t : t;
        }

        const float Ring = 7f;

        static void Stick(Vector3 worldDir, float mag)
        {
            var cam = Camera.main.transform;
            var f = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            var r = Vector3.Cross(Vector3.up, f);
            worldDir.y = 0f;
            worldDir.Normalize();
            TouchControls.DebugStick = new Vector2(Vector3.Dot(worldDir, r), Vector3.Dot(worldDir, f)) * mag;
        }

        void Mark(string what) { _seg.AppendLine($"{_n} {what}"); Debug.Log($"[Anim] {_n} {what}"); }

        [UnityTest]
        public IEnumerator AnimShowcase()
        {
            Time.captureFramerate = 0;
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(40);
            HUD.I.DebugSkipDialogue();
            GameInput.Locked = false;
            TouchControls.ForceOn = true;
            foreach (var c in Object.FindObjectsByType<Canvas>()) c.enabled = false;
            Time.captureFramerate = FPS;
            _rt = new RenderTexture(W, H, 24);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            Directory.CreateDirectory(Root);
            _seg = new System.Text.StringBuilder();
            var p = GameManager.I.Player;
            var chase = ChaseCamera.I;
            _cam = Camera.main;
            chase.enabled = false;
            yield return Frames(10);

            var city = GameManager.I.City;
            var origin = city.places.ContainsKey("Dataran") ? city.places["Dataran"] : p.transform.position;
            Vector3 Ground(Vector3 at)
            {
                if (Physics.Raycast(at + Vector3.up * 20f, Vector3.down, out var hit, 40f,
                        ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle | 1 << 2), QueryTriggerInteraction.Ignore))
                    at.y = hit.point.y;
                return at;
            }
            // an empty square: no strollers wandering through the shots
            foreach (var q in new List<Pedestrian>(Pedestrian.All)) if (q) Object.Destroy(q.gameObject);
            _center = FindOpen(Ground(origin), Ring + 3f);
            Debug.Log($"[Anim] stage at {_center} ({(_center - origin).magnitude:F0} m from {origin})");
            _cam.fieldOfView = 34f;

            string[] cast = { "pakmat", "maksom", "along", "adik", "aiman" };
            for (int ci = 0; ci < cast.Length; ci++)
            {
                string id = cast[ci];
                if (!GameData.Characters.ContainsKey(id)) continue;
                p.SetCharacter(id);
                var start = Ground(_center + Vector3.right * Ring);
                p.Teleport(start + Vector3.up * 0.2f, Quaternion.LookRotation(Vector3.forward));
                chase.enabled = false;
                Film(p, 0.8f, true);
                yield return Frames(15);
                Mark($"{id} idle");
                yield return Rec(p, 1.6f);
                Mark($"{id} walk");
                yield return Rec(p, 2.6f, t => Stick(Tangent(p), 0.32f));
                Mark($"{id} jog");
                yield return Rec(p, 2.4f, t => Stick(Tangent(p), 0.9f));
                Mark($"{id} sprint");
                p.debugSprint = true;
                yield return Rec(p, 2.2f, t => Stick(Tangent(p), 1f));
                p.debugSprint = false;
                Mark($"{id} skid");
                yield return Rec(p, 0.9f, t => Stick(Tangent(p, true), 0.9f));
                TouchControls.DebugStick = Vector2.zero;
                yield return Rec(p, 0.9f);
                if (ci > 0) continue;

                // the full set of moves for the first of the family
                Mark($"{id} jump+flip");
                p.DebugJump();
                yield return Rec(p, 0.38f, t => Stick(Tangent(p), 0.5f), 1.1f);
                p.DebugJump();
                yield return Rec(p, 1.6f, t => Stick(Tangent(p), 0.4f), 1.1f);
                TouchControls.DebugStick = Vector2.zero;
                yield return Rec(p, 0.6f);
                Mark($"{id} pound");
                p.DebugJump();
                yield return Rec(p, 0.45f, null, 1.1f);
                p.DebugStomp();
                yield return Rec(p, 2.0f, null, 1.0f);
                Mark($"{id} combo");
                for (int i = 0; i < 3; i++) { p.DebugPunch(1); yield return Rec(p, i < 2 ? 0.3f : 0.6f); }
                Mark($"{id} kick");
                p.DebugKick();
                yield return Rec(p, 1.0f);
                Mark($"{id} fidget");
                var rig = p.GetComponentInChildren<CharacterRig>();
                rig?.Fidget(1);
                yield return Rec(p, 3.0f);
                rig?.Fidget(2);
                yield return Rec(p, 2.4f);
            }

            // a crowd round a ground-pound: they turn and stare, film it, fold their arms, point and laugh
            {
                p.SetCharacter("pakmat");
                chase.enabled = false;
                var here = Ground(_center);
                _clear = false;
                p.Teleport(here + Vector3.up * 0.2f, Quaternion.LookRotation(Vector3.forward));
                chase.enabled = false;
                var zone = WalkZone.FromRect(new Rect(here.x - 15f, here.z - 15f, 30f, 30f), 0f);
                string[] who = { "chr_townman", "chr_townaunty", "chr_pakcik", "chr_kid", "chr_mei", "chr_ravi", "chr_townaunty" };
                var crowd = new List<Pedestrian>();
                for (int i = 0; i < who.Length; i++)
                {
                    var spot = here + Quaternion.Euler(0f, -60f + i * 20f, 0f) * Vector3.forward * 4.2f;
                    crowd.Add(PedestrianSpawner.Spawn(Ground(spot), zone, GameManager.I.transform, who[i]));
                }
                // film from behind and above the player, the crowd ahead
                _center = here - Vector3.forward * 4.5f + Vector3.up * 0.8f;
                _cam.fieldOfView = 50f;
                Film(p, 1.2f, true);
                yield return Frames(20);
                Mark("crowd");
                yield return Rec(p, 1.0f, null, 1.2f);
                p.DebugJump();
                yield return Rec(p, 0.45f, null, 1.2f);
                p.DebugStomp();
                yield return Rec(p, 1.6f, null, 1.2f);
                // then round in front of the crowd, looking at their faces
                Mark("crowd close");
                var mid = Vector3.zero;
                int alive = 0;
                foreach (var q in crowd) if (q) { mid += q.transform.position; alive++; }
                mid = alive > 0 ? mid / alive : here + Vector3.forward * 4f;
                _center = here + Vector3.forward * 1.2f + Vector3.up * 0.5f;
                _lookAt = mid + Vector3.up * 1.0f;
                _cam.fieldOfView = 55f;
                Film(p, 1.2f, true);
                yield return Rec(p, 4.0f, null, 1.2f);
                _lookAt = null;
            }

            File.WriteAllText(Path.Combine(Root, "segments.txt"), _seg.ToString());
            TouchControls.DebugStick = Vector2.zero;
            TouchControls.ForceOn = false;
            Time.captureFramerate = 0;
            chase.enabled = true;
        }
    }
}
