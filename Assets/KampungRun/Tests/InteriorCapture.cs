using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace KampungRun.Tests
{
    /// <summary>
    /// Walk-in interiors: boots level 1 and visits Pak Mat's house, three Chow Kit shops (kedai runcit, kedai kopi,
    /// barber), a filler shop row and the mamak. At each it grabs stills from the game camera: outside the door,
    /// walking through, and two or three views inside. Written to Tools/promo_frames/x_interiors/&lt;tag&gt;/ with a
    /// log of where the player ended up (so a blocked doorway shows in the numbers too). Explicit.
    /// </summary>
    [Explicit, Category("Promo")]
    public class InteriorCapture
    {
        const int W = 1280, H = 800, FPS = 30;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "x_interiors"));

        RenderTexture _rt;
        Texture2D _tex;
        string _dir;
        readonly StringBuilder _log = new StringBuilder();

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Seconds(float s) => Frames(Mathf.RoundToInt(s * FPS));

        void Grab(string name)
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
            File.WriteAllBytes(Path.Combine(_dir, name + ".jpg"), _tex.EncodeToJPG(90));
            var p = GameManager.I.Player.transform.position;
            _log.AppendLine($"{name}: player {p.x:F2} {p.y:F2} {p.z:F2}  cam {cam.transform.position}  {1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime):F0} fps");
        }

        static void Aim(Vector3 eye, Vector3 at, float fov = 55f)
        {
            var cam = Camera.main;
            ChaseCamera.I.enabled = false;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            cam.fieldOfView = fov;
        }

        float _fov0;

        void GameCam(float yaw)
        {
            var cc = ChaseCamera.I;
            cc.enabled = true;
            Camera.main.fieldOfView = _fov0;
            cc.target = GameManager.I.Player.transform;
            cc.SnapBehind();
            cc.yaw = yaw;
        }

        /// <summary>Stand the player at a spot facing a way and let the game camera settle behind them.</summary>
        IEnumerator Stand(Vector3 at, float yaw, float settle = 1.2f)
        {
            var p = GameManager.I.Player;
            p.Teleport(at + Vector3.up * 0.15f, Quaternion.Euler(0f, yaw, 0f));
            yield return Frames(3);
            GameCam(yaw);
            yield return Seconds(settle);
        }

        /// <summary>Walk the player forward with the virtual stick for a while.</summary>
        static IEnumerator Walk(float seconds, float amount = 0.55f)
        {
            TouchControls.DebugStick = new Vector2(0f, amount);
            yield return Seconds(seconds);
            TouchControls.DebugStick = Vector2.zero;
            yield return Seconds(0.4f);
        }

        [UnityTest] public IEnumerator Before() => Shoot("before");
        [UnityTest] public IEnumerator After() => Shoot("after");

        /// <summary>The same street views with the walk-in rooms and the street dressing switched off (the city as it was)
        /// and on (lived-in), and the dressed streets at night: Tools/promo_frames/x_interiors/street_{before,after,night}.</summary>
        [UnityTest] public IEnumerator StreetBefore() => Street("street_before", 1, false);
        [UnityTest] public IEnumerator StreetAfter() => Street("street_after", 1, true);
        [UnityTest] public IEnumerator StreetNight() => Street("street_night", 4, true);

        IEnumerator Street(string tag, int level, bool lived)
        {
            Arch.Interior.Enabled = lived;
            Arch.StreetDress.Enabled = lived;
            Time.captureFramerate = 0;
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = level;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(40);
            HUD.I.DebugSkipDialogue();
            GameInput.Locked = false;
            foreach (var c in Object.FindObjectsByType<Canvas>()) c.enabled = false;
            Time.captureFramerate = FPS;
            _rt = new RenderTexture(W, H, 24);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            _dir = Path.Combine(Root, tag);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            yield return Frames(10);
            var city = GameManager.I.City;
            var p = GameManager.I.Player;
            var ck = city.places["ChowKit"] - new Vector3(0f, 0f, 24.5f);
            var bc = city.places["Mamak"] - new Vector3(-8f, 0f, 16f);
            var home = city.places["Home"];
            var views = new (string name, Vector3 eye, Vector3 at, float fov)[]
            {
                ("1_chowkit_row", ck + new Vector3(12f, 1.8f, 26.5f), ck + new Vector3(-4f, 2.3f, 16.5f), 58f),
                ("2_chowkit_fiveway", ck + new Vector3(9f, 1.6f, 18.2f), ck + new Vector3(-6f, 1.4f, 17.6f), 62f),
                ("3_mamak_row", bc + new Vector3(24f, 1.8f, 24f), bc + new Vector3(5f, 2.3f, 16f), 58f),
                ("4_back_lane", bc + new Vector3(4f, 1.7f, -3.2f), bc + new Vector3(-10f, 2.0f, -6.6f), 62f),
                ("5_kampung_street", home + new Vector3(6f, 1.7f, 16f), home + new Vector3(-12f, 2.2f, 0f), 58f),
                ("6_home_yard", home + new Vector3(2f, 1.7f, -9f), home + new Vector3(-12f, 2.0f, 0f), 60f),
                ("7_mamak_fiveway", bc + new Vector3(19.5f, 1.6f, 19.8f), bc + new Vector3(4f, 1.5f, 17.6f), 62f),
                ("8_home_gate", home + new Vector3(-1.5f, 1.7f, 4.5f), home + new Vector3(-12f, 2.2f, -0.5f), 62f),
            };
            foreach (var v in views)
            {
                p.Teleport(v.eye - Vector3.up * 1.5f - Vector3.ProjectOnPlane(v.at - v.eye, Vector3.up).normalized * 2.5f, Quaternion.LookRotation(Vector3.ProjectOnPlane(v.at - v.eye, Vector3.up)));
                yield return Seconds(1.2f);
                Aim(v.eye, v.at, v.fov);
                yield return Frames(4);
                Grab(v.name);
            }
            File.WriteAllText(Path.Combine(_dir, "log.txt"), _log.ToString());
            ChaseCamera.I.enabled = true;
            Time.captureFramerate = 0;
            Arch.Interior.Enabled = true;
            Arch.StreetDress.Enabled = true;
        }

        IEnumerator Shoot(string tag)
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
            _dir = Path.Combine(Root, tag);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            _fov0 = Camera.main.fieldOfView;
            yield return Frames(10);
            var city = GameManager.I.City;

            // ---- Pak Mat's house (HomeBlock: the house at block centre + (-4, 0), facing +x; Home is centre + (8, 0))
            {
                var c = city.places["Home"] - new Vector3(8f, 0f, 0f);
                var hc = new Vector3(c.x - 4f, c.y, c.z);
                var rot = Quaternion.Euler(0f, 90f, 0f);
                Vector3 L(float x, float y, float z) => hc + rot * new Vector3(x, y, z);
                const float floor = 1.5f;
                Aim(L(3.5f, 2.0f, 12f), L(0f, 2.3f, 1.6f), 50f);
                yield return Frames(4);
                Grab("home_1_outside");
                yield return Stand(L(0f, 0f, 6.4f), 270f, 1.0f);              // the foot of the stair, facing the house
                Grab("home_2_stair");
                yield return Walk(2.4f);                                       // up the stair and in through the door
                Grab("home_3_doorway");
                yield return Walk(1.0f);
                Grab("home_4_walkin");
                yield return Stand(L(0.6f, floor, -0.2f), 270f + 30f);
                Grab("home_5_inside_gamecam");
                Aim(L(3.3f, floor + 2.1f, 1.3f), L(-2.0f, floor + 0.9f, -2.2f), 70f);
                yield return Frames(3);
                Grab("home_6_living_wide");
                Aim(L(-3.3f, floor + 2.1f, -2.9f), L(2.0f, floor + 0.9f, 1.0f), 70f);
                yield return Frames(3);
                Grab("home_7_living_back");
                Aim(L(1.9f, 1.2f + 1.75f, -3.8f), L(-1.0f, 1.2f + 0.5f, -6.1f), 72f);
                yield return Frames(3);
                Grab("home_8_kitchen");
                yield return Stand(L(1.0f, 1.2f, -5.0f), 270f);
                Grab("home_9_kitchen_gamecam");
                yield return Stand(L(0f, floor, -1.2f), 90f, 1.0f);           // and back out of the front door
                yield return Walk(1.6f);
                Grab("home_10_walkout");
            }

            // ---- Chow Kit: the north row (ChowKitBlock), lots 5 m wide centred on x = -15 + 5i, shopfront at z + 17
            {
                var c = city.places["ChowKit"] - new Vector3(0f, 0f, 24.5f);
                foreach (var (lot, name) in new[] { (1, "runcit"), (3, "kopi"), (5, "barber") })
                {
                    float x = c.x - 15f + lot * 5f;
                    Vector3 P(float dx, float y, float z) => new Vector3(x + dx, c.y + y, c.z + z);
                    Aim(P(1.8f, 1.9f, 25f), P(0f, 1.8f, 15f), 50f);
                    yield return Frames(4);
                    Grab($"ck_{name}_1_outside");
                    yield return Stand(P(0f, 0f, 19.3f), 180f, 1.0f);
                    Grab($"ck_{name}_2_approach");
                    yield return Walk(1.6f);
                    Grab($"ck_{name}_3_walkin");
                    yield return Stand(P(0.3f, 0f, 14.5f), 180f + 25f);
                    Grab($"ck_{name}_4_inside_gamecam");
                    Aim(P(2.0f, 2.9f, 16.6f), P(-1.4f, 1.0f, 10.0f), 72f);
                    yield return Frames(3);
                    Grab($"ck_{name}_5_inside_wide");
                    Aim(P(-1.8f, 2.6f, 9.6f), P(1.0f, 1.2f, 17.0f), 72f);
                    yield return Frames(3);
                    Grab($"ck_{name}_6_inside_out");
                }
            }

            // ---- the mamak (MamakBlock: the restaurant at block centre + (-8, 9); Mamak is centre + (-8, 16))
            {
                var m = city.places["Mamak"] - new Vector3(0f, 0f, 7f);
                Aim(m + new Vector3(4f, 2.2f, 14f), m + new Vector3(0f, 1.5f, 0f), 55f);
                yield return Frames(4);
                Grab("mamak_1_outside");
                yield return Stand(m + new Vector3(0f, 0f, 8f), 180f, 1.0f);
                Grab("mamak_2_approach");
                yield return Walk(1.8f);
                Grab("mamak_3_walkin");
                Aim(m + new Vector3(4.5f, 2.8f, 3.5f), m + new Vector3(-2f, 0.8f, -3f), 70f);
                yield return Frames(3);
                Grab("mamak_4_inside_wide");

                // the filler shop row on the same block (ArchShopRow at centre + (12, 11.5): lots at x 7, 12, 17)
                var bc = city.places["Mamak"] - new Vector3(-8f, 0f, 16f);
                for (int i = 0; i < 3; i++)
                {
                    float x = bc.x + 7f + i * 5f;
                    Aim(new Vector3(x + 1.5f, bc.y + 1.9f, bc.z + 26f), new Vector3(x, bc.y + 1.6f, bc.z + 14f), 50f);
                    yield return Frames(3);
                    Grab($"row_{i}_1_outside");
                    yield return Stand(new Vector3(x, bc.y, bc.z + 19.6f), 180f, 0.8f);
                    yield return Walk(1.5f);
                    Grab($"row_{i}_2_walkin");
                }
            }

            // ---- the general case: the nearest few open shops of other kinds, and a neighbour's house
            var ints = Interiors.I;
            if (ints != null)
            {
                var from = city.places["Mamak"];
                var seen = new System.Collections.Generic.HashSet<Arch.InteriorKind>();
                foreach (var kind in new[] { Arch.InteriorKind.Hardware, Arch.InteriorKind.Textile, Arch.InteriorKind.Gold, Arch.InteriorKind.Kopi, Arch.InteriorKind.Runcit, Arch.InteriorKind.House })
                {
                    var r = ints.Nearest(kind == Arch.InteriorKind.House ? city.places["Home"] + new Vector3(30f, 0f, 30f) : from, x => x.kind == kind && x.shell);
                    if (r == null || !seen.Add(kind)) continue;
                    _log.AppendLine($"general {kind}: room {r.W:F1} x {r.D:F1} x {r.H:F1} at {r.Centre}");
                    yield return Stand(r.f.P(r.W * (kind == Arch.InteriorKind.Textile ? 0.25f : 0.5f), 0f, -r.D * 0.3f), Quaternion.LookRotation(-r.f.n).eulerAngles.y, 1.0f);
                    Grab($"gen_{kind}_1_gamecam");
                    Aim(r.f.P(r.W * 0.85f, r.H * 0.78f, -0.25f), r.f.P(r.W * 0.3f, 0.8f, -r.D * 0.85f), 72f);
                    yield return Frames(3);
                    Grab($"gen_{kind}_2_wide");
                    Aim(r.f.P(r.W * 0.5f + 1.2f, 1.9f, 7.5f), r.f.P(r.W * 0.5f, 1.5f, -r.D * 0.5f), 50f);
                    yield return Frames(3);
                    Grab($"gen_{kind}_3_outside");
                }
                _log.AppendLine($"interiors: {ints.Rooms.Count} rooms registered, {ints.Furnished} furnished now, {ints.KeeperCount} keepers");
                int inRooms = 0, peds = 0;
                foreach (var ped in Pedestrian.All)
                {
                    if (ped == null) continue;
                    peds++;
                    var r = Interiors.RoomAt(ped.transform.position, -0.3f);
                    if (r != null && r.shell) { inRooms++; _log.AppendLine($"  pedestrian in a {r.kind} at {ped.transform.position}"); }
                }
                _log.AppendLine($"pedestrians inside rooms: {inRooms} of {peds}");

                // ---- frame time at the Chow Kit doorway, furnished and not (rough: batch-mode rendering)
                var ck = city.places["ChowKit"] - new Vector3(0f, 0f, 24.5f);
                yield return Stand(new Vector3(ck.x - 10f, ck.y, ck.z + 19.3f), 180f, 1.5f);
                Time.captureFramerate = 0;
                float t0 = Time.realtimeSinceStartup;
                yield return Frames(120);
                float withMs = (Time.realtimeSinceStartup - t0) / 120f * 1000f;
                ints.gameObject.SetActive(false);
                yield return Frames(10);
                t0 = Time.realtimeSinceStartup;
                yield return Frames(120);
                float withoutMs = (Time.realtimeSinceStartup - t0) / 120f * 1000f;
                ints.gameObject.SetActive(true);
                Time.captureFramerate = FPS;
                _log.AppendLine($"frame time at the Chow Kit doorway: {withMs:F2} ms furnished, {withoutMs:F2} ms with the furniture hidden");
            }

            File.WriteAllText(Path.Combine(_dir, "log.txt"), _log.ToString());
            ChaseCamera.I.enabled = true;
            TouchControls.ForceOn = false;
            Time.captureFramerate = 0;
            Debug.Log($"[Interiors] wrote {_dir}\n{_log}");
        }
    }
}
