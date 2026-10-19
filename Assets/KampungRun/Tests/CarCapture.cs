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
    /// The same in-game shots of every car, for comparing vehicle art passes: three-quarter front, three-quarter
    /// rear, side, a wheel close-up, the driver seated (through the side window and through the windscreen) and
    /// one driving shot from the game camera. Written to Tools/promo_frames/x_cars/&lt;tag&gt;/&lt;car&gt;_*.png.
    /// Explicit: only when asked for by name (Before against the old models, After against the new ones).
    /// </summary>
    [Explicit, Category("Promo")]
    public class CarCapture
    {
        const int W = 1400, H = 900, FPS = 30;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "x_cars"));

        static readonly string[] Ids = { "saga", "myvi", "kancil", "hilux", "van", "teksi", "polis", "kapcai", "iriz", "mpv", "lorry",
                                                 "foodtruck", "bike", "basmini" };

        RenderTexture _rt;
        Texture2D _tex;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Seconds(float s) => Frames(Mathf.RoundToInt(s * FPS));

        class HoldDriver : IDriver
        {
            public float throttle, steer;
            public void Drive(Vehicle v, out float t, out float s, out bool hb) { t = throttle; s = steer; hb = false; }
        }

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
            File.WriteAllBytes(file, _tex.EncodeToPNG());
        }

        static void Aim(Vector3 eye, Vector3 at, float fov = 35f)
        {
            var cam = Camera.main;
            ChaseCamera.I.enabled = false;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            cam.fieldOfView = fov;
        }

        static Bounds CarBounds(Vehicle v)
        {
            var b = new Bounds(v.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in v.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r.GetComponentInParent<Animator>() != null) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        static void Settle(Vehicle v, Vector3 pos, Quaternion rot)
        {
            v.Body.position = pos; v.transform.SetPositionAndRotation(pos, rot);
            v.Body.linearVelocity = Vector3.zero; v.Body.angularVelocity = Vector3.zero;
        }

        [UnityTest] public IEnumerator Before() => Shoot("before");
        [UnityTest] public IEnumerator Street() => ShootStreet("after");
        [UnityTest] public IEnumerator After() => Shoot("after");

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
            var gm = GameManager.I;
            gm.trafficCount = 0;
            gm.cityDensity = 0f;
            foreach (var t in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) Object.Destroy(t.gameObject);
            Time.captureFramerate = FPS;
            _rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var dir = Path.Combine(Root, tag);
            Directory.CreateDirectory(dir);
            yield return Frames(10);

            var p = gm.Player;
            var cc = ChaseCamera.I;
            var cam = Camera.main;
            float fov0 = cam.fieldOfView;
            var args = System.Environment.GetCommandLineArgs();
            int ia = System.Array.IndexOf(args, "-carIds");
            var ids = ia >= 0 && ia + 1 < args.Length ? args[ia + 1].Split(',') : Ids;
            // a straight filler avenue south of the old town (the same one the smoke tests use)
            var spot = new Vector3(CityBuilder.RoadX(12) + 2.5f, 0.6f, CityBuilder.RoadZ(2) + 12f);
            var rot = Quaternion.identity;
            foreach (var id in ids)
            {
                foreach (var ped in new List<Pedestrian>(Pedestrian.All))
                    if (ped && Vector3.Distance(ped.transform.position, spot) < 25f) Object.Destroy(ped.gameObject);
                foreach (var t in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) Object.Destroy(t.gameObject);
                p.Teleport(spot + Vector3.right * 12f + Vector3.forward * 6f, Quaternion.identity);
                yield return Frames(2);
                var v = VehicleSpawner.Spawn(id, spot + Vector3.up * 0.4f, rot, VehicleRole.Parked);
                yield return Seconds(1.2f);
                var b = CarBounds(v);
                var c = b.center;
                var f = v.transform.forward; var r = v.transform.right; var u = Vector3.up;
                float L = Mathf.Max(b.size.x, b.size.z), Hh = b.size.y;
                float d = L * 1.55f + 1.2f;
                Debug.Log($"[CarCapture] {id} bounds {b.size:F2}");
                // wheels on the road? (gap between each tyre's lowest point and the ground under it)
                foreach (var w in v.Wheels)
                {
                    if (!w) continue;
                    var wr = w.GetComponent<Renderer>();
                    if (!wr) continue;
                    var low = new Vector3(wr.bounds.center.x, wr.bounds.min.y, wr.bounds.center.z);
                    if (Physics.Raycast(low + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, ~(1 << Layers.Vehicle | 1 << Layers.Character)))
                        Debug.Log($"[CarCapture] {id} {w.name}: tyre bottom {low.y - hit.point.y:F3} m above {hit.collider.name}");
                }

                Aim(c + (f * 0.75f + r * 0.62f).normalized * d + u * (Hh * 0.55f + 0.6f), c, 32f);
                yield return Frames(3);
                Grab(Path.Combine(dir, $"{id}_1_front34.png"));
                Aim(c + (-f * 0.75f - r * 0.62f).normalized * d + u * (Hh * 0.55f + 0.6f), c, 32f);
                yield return Frames(3);
                Grab(Path.Combine(dir, $"{id}_2_rear34.png"));
                Aim(c + r * d * 0.95f + u * 0.25f, c, 32f);
                yield return Frames(3);
                Grab(Path.Combine(dir, $"{id}_3_side.png"));
                // a front wheel, low and close
                var wheel = ModelFactory.Find(v.gameObject, "Wheel_FR") ?? ModelFactory.Find(v.gameObject, "Wheel_FL") ??
                            ModelFactory.Find(v.gameObject, "FrontWheel");
                if (wheel)
                {
                    var wp = wheel.GetComponent<Renderer>() ? wheel.GetComponent<Renderer>().bounds.center : wheel.position;
                    float s = Vector3.Dot(wp - c, r) >= 0 ? 1f : -1f;
                    Aim(wp + r * s * 1.6f + f * 0.9f + u * 0.35f, wp, 30f);
                    yield return Frames(3);
                    Grab(Path.Combine(dir, $"{id}_4_wheel.png"));
                }

                // driver at the wheel (instant boarding, as the roof test does)
                p.EnterVehicle(v, true);
                v.driver = new HoldDriver();
                yield return Seconds(1.6f);
                var anim = p.GetComponentInChildren<Animator>();
                var head = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head).position : c + u * 0.6f;
                float side = Vector3.Dot(head - c, r) >= 0 ? 1f : -1f;
                if (v.TwoWheeler)
                {
                    Aim(head + r * 2.2f + f * 1.4f + u * 0.1f, head - u * 0.45f, 34f);
                    yield return Frames(3);
                    Grab(Path.Combine(dir, $"{id}_5_driver_side.png"));
                    Aim(head + f * 2.4f - r * 0.6f + u * 0.2f, head - u * 0.4f, 34f);
                }
                else
                {
                    Aim(head + r * side * 1.9f + f * 0.55f + u * 0.12f, head - u * 0.22f, 36f);
                    yield return Frames(3);
                    Grab(Path.Combine(dir, $"{id}_5_driver_side.png"));
                    Aim(head + f * 2.6f - r * side * 0.35f + u * 0.25f, head - u * 0.3f, 30f);
                }
                yield return Frames(3);
                Grab(Path.Combine(dir, $"{id}_6_driver_front.png"));

                // driving away from the game camera
                cc.enabled = true;
                cam.fieldOfView = fov0;
                v.driver = new HoldDriver { throttle = 0.55f, steer = 0.12f };
                yield return Seconds(2.2f);
                Grab(Path.Combine(dir, $"{id}_7_drive.png"));
                v.driver = new HoldDriver { throttle = 0f };
                p.ExitVehicle(false, true);
                yield return Frames(4);
                Object.Destroy(v.gameObject);
                yield return Frames(4);
            }
            cc.enabled = true;
            TouchControls.ForceOn = false;
            Time.captureFramerate = 0;
            Debug.Log($"[CarCapture] wrote {dir}");
        }

        static readonly Color[] Paints =
        {
            new Color(0.84f, 0.1f, 0.1f), new Color(0.18f, 0.38f, 0.78f), new Color(0.98f, 0.8f, 0.12f), new Color(0.2f, 0.62f, 0.34f),
            new Color(0.94f, 0.94f, 0.93f), new Color(0.62f, 0.64f, 0.68f), new Color(0.12f, 0.12f, 0.14f), new Color(0.98f, 0.52f, 0.14f),
            new Color(0.55f, 0.3f, 0.7f), new Color(0.4f, 0.75f, 0.9f), new Color(0.95f, 0.55f, 0.7f),
        };
        static readonly string[] Recolorable = { "saga", "myvi", "kancil", "hilux", "van", "iriz", "mpv", "lorry" };

        /// <summary>A lived-in street: both kerbs lined with parked cars (random paint and optional parts), traffic
        /// rolling down the middle, the player's car among it. Game camera, a raised view, a kerb view, a long view.</summary>
        IEnumerator ShootStreet(string tag)
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
            var gm = GameManager.I;
            gm.trafficCount = 0;
            gm.cityDensity = 0f;
            foreach (var t in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) Object.Destroy(t.gameObject);
            Time.captureFramerate = FPS;
            _rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var dir = Path.Combine(Root, tag);
            Directory.CreateDirectory(dir);
            yield return Frames(10);
            var p = gm.Player;
            var cc = ChaseCamera.I;
            var cam = Camera.main;
            float fov0 = cam.fieldOfView;
            float x0 = CityBuilder.RoadX(12), z0 = CityBuilder.RoadZ(1) + 14f;
            foreach (var ped in new List<Pedestrian>(Pedestrian.All))
                if (ped && Mathf.Abs(ped.transform.position.x - x0) < 14f) Object.Destroy(ped.gameObject);
            var ids = new[] { "saga", "myvi", "kancil", "kapcai", "hilux", "iriz", "teksi", "mpv", "kancil", "saga", "myvi", "kapcai",
                              "lorry", "kancil", "iriz", "myvi", "saga", "mpv", "kancil", "myvi", "van", "teksi", "saga", "kapcai",
                              "foodtruck", "iriz", "myvi", "basmini" };
            int n = 0;
            float Ground(float x, float z) =>
                Physics.Raycast(new Vector3(x, 30f, z), Vector3.down, out var hit, 60f, ~(1 << Layers.Vehicle | 1 << Layers.Character)) ? hit.point.y : 0f;
            for (int i = 0; i < ids.Length; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;                 // left-hand traffic: the -x kerb faces +z
                float z = z0 + (i / 2) * 6.6f + (side > 0 ? 2.5f : 0f);
                float x = x0 + side * 8.0f;
                var rot = Quaternion.Euler(0, side < 0 ? 0f : 180f, 0);
                var v = VehicleSpawner.Spawn(ids[i], new Vector3(x, Ground(x, z) + 0.05f, z), rot, VehicleRole.Parked);
                if (System.Array.IndexOf(Recolorable, ids[i]) >= 0)
                    ModelFactory.Recolor(v.gameObject, new Dictionary<string, Color> { ["car_paint"] = Paints[(i * 7 + 3) % Paints.Length] });
                VehicleVariety.Apply(v.gameObject, 1000 + i * 37, 0.6f);
                n++;
            }
            // traffic in the near lanes, the player in a Saga behind it
            var moving = new List<Vehicle>();
            string[] trafficIds = { "myvi", "kancil", "teksi", "kapcai", "mpv" };
            for (int i = 0; i < trafficIds.Length; i++)
            {
                float z = z0 + 6f + i * 9f;
                float x = x0 - 3.2f + (i % 2) * 0.6f;
                var v = VehicleSpawner.Spawn(trafficIds[i], new Vector3(x, Ground(x, z) + 0.3f, z), Quaternion.identity, VehicleRole.Traffic);
                if (System.Array.IndexOf(Recolorable, trafficIds[i]) >= 0)
                    ModelFactory.Recolor(v.gameObject, new Dictionary<string, Color> { ["car_paint"] = Paints[(i * 5 + 1) % Paints.Length] });
                VehicleSpawner.AddDriver(v);
                v.driver = new HoldDriver { throttle = 0.35f };
                moving.Add(v);
            }
            var car = VehicleSpawner.Spawn("saga", new Vector3(x0 - 3.2f, Ground(x0 - 3.2f, z0 - 10f) + 0.3f, z0 - 10f), Quaternion.identity, VehicleRole.Parked);
            ModelFactory.Recolor(car.gameObject, GameData.Car("saga").swaps);
            yield return Seconds(0.6f);
            p.EnterVehicle(car, true);
            car.driver = new HoldDriver { throttle = 0.45f };
            cc.enabled = true;
            cam.fieldOfView = fov0;
            yield return Seconds(2.5f);
            Grab(Path.Combine(dir, "street_1_gamecam.png"));
            foreach (var v in moving) if (v) v.driver = new HoldDriver { throttle = 0f };
            car.driver = new HoldDriver { throttle = 0f };
            yield return Seconds(1.5f);
            var mid = new Vector3(x0, Ground(x0, z0 + 30f), z0 + 30f);
            Aim(mid + new Vector3(13f, 7f, -26f), mid + new Vector3(0, 0, 6f), 42f);
            yield return Frames(3);
            Grab(Path.Combine(dir, "street_2_raised.png"));
            Aim(new Vector3(x0 - 5.6f, mid.y + 1.3f, z0 - 4f), new Vector3(x0 - 7.6f, mid.y + 0.7f, z0 + 24f), 40f);
            yield return Frames(3);
            Grab(Path.Combine(dir, "street_3_kerb.png"));
            Aim(mid + new Vector3(3f, 14f, -95f), mid + new Vector3(0, 0, 10f), 30f);
            yield return Frames(3);
            Grab(Path.Combine(dir, "street_4_far.png"));
            Debug.Log($"[CarCapture] street: {n} parked, {moving.Count} moving");
            cc.enabled = true;
            TouchControls.ForceOn = false;
            Time.captureFramerate = 0;
        }
    }
}
