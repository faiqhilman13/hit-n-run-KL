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
    /// The city's baked shade (CityShade) round Petaling Street and its neighbours: each view rendered three
    /// times in the same frame, with the shade off (a), on (b), and on with the world's real-time shadows off
    /// (c, Hit &amp; Run style), to Tools/promo_frames/x_shade/, plus the map beside a straight-down view of the
    /// same square (00_*). Explicit: only by name.
    /// </summary>
    [Explicit, Category("Promo")]
    public class ShadeCapture
    {
        const int W = 1280, H = 720;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "x_shade"));
        static int Solid => ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle | 1 << 2);
        const float FlyDeck = 7.1f;          // the flyover place sits this far over the road (CityBuilder FlyH + 0.3)

        RenderTexture _rt;
        Texture2D _tex;
        Camera _cam;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        void Grab(string name)
        {
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _rt };
            if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
            else { _cam.targetTexture = _rt; _cam.Render(); _cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(Root, name + ".jpg"), _tex.EncodeToJPG(90));
        }

        static Vector3 Ground(Vector3 at)
        {
            if (Physics.Raycast(at + Vector3.up * 20f, Vector3.down, out var hit, 40f, Solid, QueryTriggerInteraction.Ignore)) at.y = hit.point.y;
            return at;
        }

        /// <summary>The way a street runs past a spot: the direction with the longest clear view both ways at eye height.</summary>
        static Vector3 Axis(Vector3 at)
        {
            float Clear(Vector3 dir) => Physics.Raycast(at + Vector3.up * 1.7f, dir, out var h, 150f, Solid, QueryTriggerInteraction.Ignore) ? h.distance : 150f;
            var best = Vector3.forward;
            float bestLen = 0f, bestFwd = 0f;
            for (int a = 0; a < 180; a += 5)
            {
                var d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                float f = Clear(d), b = Clear(-d);
                if (f + b > bestLen) { bestLen = f + b; best = f >= b ? d : -d; bestFwd = Mathf.Max(f, b); }
            }
            Debug.Log($"[Shade] street at {at} runs {best} ({bestLen:F0} m clear, {bestFwd:F0} m ahead)");
            return best;
        }

        /// <summary>A camera spot nudged sideways out of anything solid (a five-foot-way column, a wall).</summary>
        static Vector3 Free(Vector3 eye, Vector3 look)
        {
            var side = Vector3.Cross(Vector3.up, (look - eye).normalized);
            for (float d = 0f; d < 6f; d += 0.5f)
                foreach (float sgn in new[] { 1f, -1f })
                {
                    var p = eye + side * d * sgn;
                    if (!Physics.CheckSphere(p, 0.7f, Solid, QueryTriggerInteraction.Ignore)) return p;
                }
            return eye;
        }

        IEnumerator View(string name, Vector3 eye, Vector3 look, float fov = 55f)
        {
            eye = Free(eye, look);
            _cam.fieldOfView = fov;
            _cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            foreach (var q in new List<Pedestrian>(Pedestrian.All))
                if (q && (q.transform.position - eye).sqrMagnitude < 5f * 5f) Object.Destroy(q.gameObject);
            Arch.ArchCity.I?.Prime(eye);
            yield return Frames(4);
            var root = GameManager.I.City.root;
            // a: as the game was; b: with the shade; c: the shade with the world's real-time shadows off (Hit & Run casts none)
            CityShade.Enable(false);
            Grab(name + "_a");
            CityShade.Enable(true);
            Grab(name + "_b");
            CityShade.WorldShadows(root, false);
            Grab(name + "_c");
            CityShade.WorldShadows(root, true);
        }

        /// <summary>The baked map round a spot beside a straight-down view of the same square, to check they line up.</summary>
        void MapCheck(string name, Vector3 at, float size)
        {
            var snap = CityShade.Snapshot(new Rect(at.x - size * 0.5f, at.z - size * 0.5f, size, size), 720);
            File.WriteAllBytes(Path.Combine(Root, name + "_map.png"), snap.EncodeToPNG());
            Object.Destroy(snap);
            bool ortho = _cam.orthographic;
            float os = _cam.orthographicSize;
            _cam.orthographic = true;
            _cam.orthographicSize = size * 0.5f;
            _cam.transform.SetPositionAndRotation(new Vector3(at.x, 400f, at.z), Quaternion.Euler(90f, 0f, 0f));
            float far = _cam.farClipPlane;
            _cam.farClipPlane = 800f;
            var rt = _rt;
            _rt = new RenderTexture(720, 720, 24);
            var tex = _tex;
            _tex = new Texture2D(720, 720, TextureFormat.RGB24, false);
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _rt };
            if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, 720, 720), 0, 0);
            _tex.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(Root, name + "_top.jpg"), _tex.EncodeToJPG(90));
            Object.Destroy(_rt);
            Object.Destroy(_tex);
            _rt = rt;
            _tex = tex;
            _cam.orthographic = ortho;
            _cam.orthographicSize = os;
            _cam.farClipPlane = far;
        }

        [UnityTest]
        public IEnumerator PetalingShade()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(40);
            HUD.I.DebugSkipDialogue();
            GameInput.Locked = false;
            foreach (var c in Object.FindObjectsByType<Canvas>()) c.enabled = false;
            _rt = new RenderTexture(W, H, 24);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            Directory.CreateDirectory(Root);
            var p = GameManager.I.Player;
            ChaseCamera.I.enabled = false;
            _cam = Camera.main;
            var city = GameManager.I.City;
            Assert.IsTrue(CityShade.Map != null && CityShade.On, "the shade was baked");

            // Petaling Street: the player at the arch end, the street running away from the camera
            var pasar = Ground(city.places["Pasar"]);
            var axis = Axis(pasar);
            var side = Vector3.Cross(Vector3.up, axis);
            p.Teleport(pasar + axis * 4f + Vector3.up * 0.2f, Quaternion.LookRotation(axis));
            yield return Frames(20);
            MapCheck("00_petaling", pasar + axis * 40f, 240f);
            yield return View("01_petaling_chase", pasar - axis * 2.5f + Vector3.up * 2.6f, pasar + axis * 22f + Vector3.up * 1.2f);
            yield return View("02_petaling_eye", pasar + axis * 8f + side * 2.5f + Vector3.up * 1.65f, pasar + axis * 60f + Vector3.up * 2.5f, 62f);
            yield return View("03_petaling_low", pasar - axis * 22f + side * 9f + Vector3.up * 16f, pasar + axis * 18f);
            yield return View("04_kotalama_high", pasar - axis * 130f + side * 70f + Vector3.up * 95f, pasar + axis * 40f);

            // a car in the traffic: its paint must come out the same with the shade on (it opts out)
            Vehicle car = null;
            foreach (var v in Object.FindObjectsByType<Vehicle>())
                if (v && v != p.vehicle && (car == null || (v.transform.position - pasar).sqrMagnitude < (car.transform.position - pasar).sqrMagnitude)) car = v;
            if (car != null)
            {
                var cp = car.transform.position;
                yield return View("08_car", cp + car.transform.right * 4.5f - car.transform.forward * 2f + Vector3.up * 1.6f, cp + Vector3.up * 0.7f);
            }

            // round the corner: Masjid Jamek, Dataran Merdeka, Pasar Seni
            // and further out, to check the bake holds up elsewhere: the kampung, Chow Kit, KLCC
            foreach (var (key, tag) in new[] { ("Masjid", "05_jamek"), ("Dataran", "06_dataran"), ("PasarSeni", "07_pasarseni"),
                                               ("Home", "09_kampung"), ("ChowKit", "10_chowkit"), ("Towers", "11_klcc") })
            {
                if (!city.places.TryGetValue(key, out var at)) continue;
                at = Ground(at);
                var ax = Axis(at);
                var sd = Vector3.Cross(Vector3.up, ax);
                p.Teleport(at + ax * 3f + Vector3.up * 0.2f, Quaternion.LookRotation(ax));
                yield return Frames(10);
                yield return View(tag, at - ax * 5f + sd * 1.5f + Vector3.up * 3.2f, at + ax * 30f + Vector3.up * 2f);
            }
            // Chow Kit's modelled rows: along the north row's awnings, and from above (the roofs the awnings bounce you onto)
            if (city.places.TryGetValue("ChowKit", out var ck))
            {
                ck = Ground(ck);
                p.Teleport(ck + Vector3.up * 0.2f, Quaternion.Euler(0f, 90f, 0f));
                yield return Frames(10);
                yield return View("20_chowkit_row", ck + new Vector3(-20f, 1.8f, 1.5f), ck + new Vector3(2f, 4f, -6f), 62f);
                yield return View("21_chowkit_air", ck + new Vector3(-38f, 30f, 14f), ck + new Vector3(0f, 0f, -24.5f), 55f);
            }
            // the green city: the Lake Gardens, the forest round the KL Tower, and the whole of it from high up
            if (city.places.TryGetValue("TamanPerdana", out var lake))
                yield return View("22_lakegardens", lake + new Vector3(-45f, 22f, -55f), lake + new Vector3(0f, 2f, 0f), 58f);
            if (city.places.TryGetValue("KLTower", out var tower))
                yield return View("23_bukitnanas", tower + new Vector3(-70f, 32f, -60f), tower + new Vector3(0f, 8f, 0f), 58f);
            yield return View("24_city_high", new Vector3(-330f, 95f, -470f), new Vector3(-60f, 0f, -120f), 60f);
            // the filler districts (FillerMap row 19: Chow Kit side): shops, condos, offices
            var shops = CityBuilder.BlockCenter(2, 19);
            var condo = CityBuilder.BlockCenter(1, 19);
            var office = CityBuilder.BlockCenter(3, 19);
            yield return View("12_filler_shops", shops + new Vector3(-30f, 2.2f, -53f), shops + new Vector3(10f, 4f, -38f), 60f);
            yield return View("13_filler_condo", condo + new Vector3(-40f, 3f, -55f), condo + new Vector3(0f, 15f, -20f), 62f);
            yield return View("14_filler_office", office + new Vector3(-40f, 2.5f, -55f), office + new Vector3(0f, 30f, -10f), 64f);
            yield return View("15_filler_aerial", shops + new Vector3(-70f, 55f, -100f), shops + new Vector3(0f, 0f, 0f), 55f);
            // a flyover from below and from its deck; Pak Mat's house from the yard
            if (city.places.TryGetValue("Flyover0", out var fly))
            {
                yield return View("16_flyover_under", fly + new Vector3(-18f, -FlyDeck + 1.8f, -16f), fly + new Vector3(0f, -2f, 0f), 66f);
                yield return View("17_flyover_deck", fly + new Vector3(0f, 1.6f, 0f) + Vector3.forward * -30f, fly + new Vector3(0f, 1.5f, 30f), 66f);
            }
            if (city.places.TryGetValue("Home", out var home))
            {
                p.Teleport(home + Vector3.up * 0.2f, Quaternion.Euler(0f, -90f, 0f));
                yield return Frames(10);
                yield return View("18_home", home + new Vector3(6f, 1.8f, -6f), home + new Vector3(-12f, 3f, 0f), 64f);
                yield return View("19_kampung_air", home + new Vector3(30f, 30f, -40f), home + new Vector3(-20f, 0f, 10f), 55f);
            }
            Debug.Log("[Shade] captured to " + Root);
        }
    }
}
