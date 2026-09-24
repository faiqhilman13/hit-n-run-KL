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
    /// Records the promo video's gameplay: a scripted "director" plays real game shots with the
    /// clock locked to 30 fps (Time.captureFramerate) and writes every frame as a JPG under
    /// Tools/promo_frames/&lt;shot&gt;/. Tools/make_promo.py cuts them into the finished videos.
    /// Explicit: only runs when asked for by name (-testFilter ...PromoCapture).
    /// </summary>
    [Explicit, Category("Promo")]
    public class PromoCapture
    {
        const int W = 1920, H = 1080, FPS = 30;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames"));

        RenderTexture _rt;
        Texture2D _tex;
        string _dir;
        int _n;

        class Drv : IDriver
        {
            public float throttle = 1f, steer;
            public bool handbrake;
            public void Drive(Vehicle v, out float t, out float s, out bool hb) { t = throttle; s = steer; hb = handbrake; }
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static void Skip() { HUD.I.DebugSkipDialogue(); GameInput.Locked = false; }

        void Grab()
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
            File.WriteAllBytes(Path.Combine(_dir, $"{_n++:D5}.jpg"), _tex.EncodeToJPG(93));
        }

        /// <summary>Record a shot: perFrame(t 0..1) runs before each captured frame.</summary>
        IEnumerator Shot(string name, float seconds, System.Action<float> perFrame = null)
        {
            _dir = Path.Combine(Root, name);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            _n = 0;
            int frames = Mathf.RoundToInt(seconds * FPS);
            for (int i = 0; i < frames; i++)
            {
                perFrame?.Invoke(i / (float)frames);
                yield return null;
                Grab();
            }
            Debug.Log($"[Promo] {name}: {frames} frames");
        }

        IEnumerator LoadLevel(int level)
        {
            Time.captureFramerate = 0;
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = level;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(40);
            Skip();
            // no HUD in the promo: the captions go on in the edit
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            Time.captureFramerate = FPS;
            yield return Frames(10);
        }

        void ClearLane(float z, float halfWidth)
        {
            foreach (var v in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
                if (v.role == VehicleRole.Traffic && Mathf.Abs(v.transform.position.z - z) < halfWidth) Object.Destroy(v.gameObject);
        }

        Transform Pivot(Vector3 at)
        {
            var t = new GameObject("PromoPivot").transform;
            t.position = at;
            return t;
        }

        IEnumerator Orbit(string name, Vector3 focus, float seconds, float dist, float pitch, float yaw0, float sweep, float height = 0f)
        {
            var cc = ChaseCamera.I;
            var piv = Pivot(focus + Vector3.up * height);
            cc.target = piv; cc.targetBody = null; cc.height = 0f;
            cc.distance = dist; cc.pitch = pitch; cc.yaw = yaw0;
            cc.SnapBehind();
            cc.yaw = yaw0;
            yield return Frames(3);
            yield return Shot(name, seconds, t => { cc.yaw = yaw0 + sweep * t; cc.pitch = pitch; cc.distance = dist; });
            Object.Destroy(piv.gameObject);
            cc.height = 1.6f;
        }

        /// <summary>Frame-by-frame look at getting in and out of a car (for checking the motion).</summary>
        [UnityTest]
        public IEnumerator RecordCarEntry()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            yield return LoadLevel(1);
            var gm = GameManager.I;
            var p = gm.Player;
            var cc = ChaseCamera.I;
            foreach (var id in new[] { "myvi", "hilux" })
            {
                var at = CityBuilder.BlockCenter(0, 1) + new Vector3(0f, 0.9f, 0f);      // the open padang
                foreach (var t in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
                    if (Vector3.Distance(t.transform.position, at) < 25f) Object.Destroy(t.gameObject);
                var car = VehicleSpawner.Spawn(id, at, Quaternion.Euler(0, 0, 0), VehicleRole.Parked);
                yield return Frames(40);
                p.Teleport(car.transform.position + car.transform.right * 4f - car.transform.forward * 1.5f, Quaternion.identity);
                var piv = Pivot(car.transform.position + Vector3.up * 1.0f + car.transform.right * 0.8f);
                cc.target = piv; cc.targetBody = null; cc.height = 0f;
                cc.distance = 6.5f; cc.pitch = 12f; cc.yaw = -60f;
                cc.SnapBehind(); cc.yaw = -60f;
                yield return Frames(5);
                bool started = false;
                yield return Shot($"x_{id}_in", 3.2f, t =>
                {
                    cc.target = piv; cc.targetBody = null; cc.height = 0f;
                    cc.yaw = -60f; cc.distance = 5.2f; cc.pitch = 10f;
                    if (!started) { p.EnterVehicle(car); started = true; }
                });
                Assert.IsTrue(p.Driving, $"{id}: never got in");
                yield return Frames(20);
                bool left = false;
                yield return Shot($"x_{id}_out", 2.6f, t =>
                {
                    cc.target = piv; cc.targetBody = null; cc.height = 0f;
                    cc.yaw = -60f; cc.distance = 5.2f; cc.pitch = 10f;
                    if (!left) { p.ExitVehicle(false); left = true; }
                });
                Assert.IsFalse(p.Driving || p.Transitioning, $"{id}: never got out");
                Object.Destroy(piv.gameObject);
                cc.height = 1.6f;
                Object.Destroy(car.gameObject);
                yield return Frames(3);
            }
            Time.captureFramerate = 0;
        }

        [UnityTest]
        public IEnumerator RecordPromo()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);

            // ================================================================ morning, Pak Mat
            yield return LoadLevel(1);
            var gm = GameManager.I;
            var p = gm.Player;
            var cc = ChaseCamera.I;
            float laneZ = CityBuilder.RoadZ(3) + 2.5f;
            ClearLane(CityBuilder.RoadZ(3), 8f);

            // --- 1. cruise: a red Myvi down the avenue, weaving a little
            var myvi = gm.SummonCar("myvi", new Vector3(CityBuilder.RoadX(3) + 6f, 0.6f, laneZ), Quaternion.Euler(0, 90, 0));
            yield return Frames(15);
            p.EnterVehicle(myvi, true);
            var drv = new Drv();
            myvi.driver = drv;
            cc.distance = 6.5f;
            cc.SnapBehind();
            yield return Frames(45);                                     // get up to speed off camera
            yield return Shot("01_cruise", 4.2f, t => drv.steer = Mathf.Sin(t * Mathf.PI * 2f) * 0.18f);

            // --- 2. drift: handbrake flick, smoke and skid marks
            cc.pitch = 20f;
            yield return Shot("02_drift", 3.0f, t =>
            {
                drv.steer = t < 0.55f ? 1f : -0.5f;
                drv.handbrake = t > 0.08f && t < 0.6f;
                drv.throttle = 1f;
            });
            drv.handbrake = false;
            p.ExitVehicle(false, true);
            Object.Destroy(myvi.gameObject);
            yield return Frames(3);

            // --- 3. showroom dolly along the Malaysian cars
            var ids = new[] { "myvi", "saga", "kancil", "van", "hilux", "kapcai", "teksi", "polis" };
            var row = new Vector3(CityBuilder.RoadX(4) + 2.5f, 0.6f, CityBuilder.RoadZ(2) + 5f);
            foreach (var t in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
                if (Vector3.Distance(t.transform.position, row) < 60f) Object.Destroy(t.gameObject);
            var parked = new List<Vehicle>();
            for (int i = 0; i < ids.Length; i++)
                parked.Add(VehicleSpawner.Spawn(ids[i], row + new Vector3(0, 0, i * 5.6f), Quaternion.Euler(0, 180, 0), VehicleRole.Parked));
            yield return Frames(40);
            var piv = Pivot(row + Vector3.up * 0.8f);
            cc.target = piv; cc.targetBody = null; cc.height = 0f;
            cc.distance = 8.5f; cc.pitch = 9f; cc.yaw = -60f;
            cc.SnapBehind(); cc.yaw = -60f;
            yield return Frames(3);
            yield return Shot("03_lineup", 5.0f, t =>
            {
                piv.position = row + new Vector3(0, 0.8f, t * 5.6f * (ids.Length - 1));
                cc.yaw = -60f + 25f * t; cc.pitch = 9f; cc.distance = 8.5f;
            });
            cc.height = 1.6f;
            Object.Destroy(piv.gameObject);

            // --- 4. walk up, door swings open, climb in (side-on)
            var door = parked[0];
            p.Teleport(door.transform.position + door.transform.right * 3.2f - door.transform.forward * 0.6f, Quaternion.identity);
            cc.target = p.transform; cc.targetBody = null;
            cc.distance = 5.5f; cc.pitch = 8f;
            cc.yaw = door.transform.eulerAngles.y - 90f;
            cc.SnapBehind(); cc.yaw = door.transform.eulerAngles.y - 115f;
            yield return Frames(5);
            bool entered = false;
            yield return Shot("04_door", 3.6f, t =>
            {
                cc.yaw = door.transform.eulerAngles.y - 115f; cc.distance = 5.5f; cc.pitch = 8f;
                if (!entered && t > 0.05f) { p.EnterVehicle(door); entered = true; }
                if (p.Driving) cc.target = door.transform;
            });
            p.ExitVehicle(false, true);
            foreach (var v in parked) if (v) Object.Destroy(v.gameObject);
            yield return Frames(3);

            // --- 5. bopping: jab, jab, big one, kick - they topple and get back up
            var bopAt = CityBuilder.BlockCenter(4, 3) + new Vector3(-19f, 0.2f, 4f);
            var ped = PedestrianSpawner.Spawn(bopAt, new Rect(bopAt.x - 20, bopAt.z - 20, 40, 40), gm.transform, "chr_pakcik");
            p.Teleport(bopAt - Vector3.forward * 1.1f, Quaternion.LookRotation(Vector3.forward));
            cc.target = p.transform; cc.targetBody = null;
            cc.distance = 4.6f; cc.pitch = 8f; cc.yaw = 60f;
            cc.SnapBehind(); cc.yaw = 60f;
            yield return Frames(20);
            float nextHit = 0.08f;
            int hits = 0;
            yield return Shot("05_bop", 5.0f, t =>
            {
                cc.yaw = 60f; cc.distance = 4.6f; cc.pitch = 8f;
                if (ped && t >= nextHit && hits < 7)
                {
                    var to = ped.transform.position - p.transform.position; to.y = 0;
                    if (to.magnitude > 1.4f) p.Teleport(ped.transform.position - to.normalized * 1.1f, Quaternion.LookRotation(to));
                    else p.transform.rotation = Quaternion.LookRotation(to.normalized);
                    if (hits == 4 || hits == 6) p.DebugKick(); else p.DebugPunch(1);
                    hits++;
                    nextHit += hits == 3 || hits == 5 ? 0.22f : 0.08f;
                }
            });
            if (ped) Object.Destroy(ped.gameObject);

            // --- 6. kapcai weaving between lanes, leaning into every turn
            float bikeZ = CityBuilder.RoadZ(4) + 2.5f;
            ClearLane(CityBuilder.RoadZ(4), 8f);
            var bike = gm.SummonCar("kapcai", new Vector3(CityBuilder.RoadX(3) + 6f, 0.6f, bikeZ), Quaternion.Euler(0, 90, 0));
            yield return Frames(15);
            p.EnterVehicle(bike, true);
            var bd = new Drv { throttle = 0.9f };
            bike.driver = bd;
            cc.distance = 4.8f; cc.pitch = 10f;
            cc.SnapBehind();
            yield return Frames(40);
            yield return Shot("06_kapcai", 4.0f, t => bd.steer = Mathf.Sin(t * Mathf.PI * 4f) * 0.75f);
            p.ExitVehicle(false, true);
            Object.Destroy(bike.gameObject);

            // --- 7. landmarks fly-arounds (landmark blocks face +Z; their place marker is on the road in front)
            Vector3 C(string place, float back = 24.5f) => gm.City.places[place] - new Vector3(0, 0, back);
            p.Teleport(CityBuilder.BlockCenter(0, 0) + Vector3.up * 0.3f, Quaternion.identity);   // out of shot (on the map)
            yield return Orbit("07_batu", C("BatuCaves") + new Vector3(0, 0, 16f), 3.0f, 30f, 14f, 150f, 50f, 7f);
            yield return Orbit("08_merdeka118", C("Merdeka118"), 3.0f, 120f, 2f, 200f, -40f, 55f);
            yield return Orbit("09_klcc", gm.City.places["Towers"] + new Vector3(0, 0, 21f), 3.0f, 150f, 16f, 215f, 40f, 75f);
            yield return Orbit("10_theanhou", C("TheanHou"), 2.6f, 38f, 12f, 145f, 45f, 7f);
            yield return Orbit("11_jamek", gm.City.places["Masjid"] + new Vector3(18f, 0, 0), 2.6f, 36f, 10f, 60f, 45f, 6f);
            yield return Orbit("12_istana", C("IstanaNegara"), 2.6f, 40f, 10f, 205f, -45f, 7f);

            // ================================================================ night, Adik's Kancil
            yield return LoadLevel(4);
            gm = GameManager.I;
            p = gm.Player;
            cc = ChaseCamera.I;
            ClearLane(CityBuilder.RoadZ(3), 8f);
            var kancil = gm.SummonCar("kancil", new Vector3(CityBuilder.RoadX(3) + 6f, 0.6f, laneZ), Quaternion.Euler(0, 90, 0));
            yield return Frames(15);
            p.EnterVehicle(kancil, true);
            var kd = new Drv();
            kancil.driver = kd;
            cc.distance = 6f; cc.pitch = 10f;
            cc.SnapBehind();
            yield return Frames(45);
            yield return Shot("13_night", 3.5f, t =>
            {
                kd.steer = t > 0.55f && t < 0.8f ? 0.9f : 0f;
                kd.handbrake = t > 0.6f && t < 0.78f;
            });

            Time.captureFramerate = 0;
            _rt.Release();
            Object.Destroy(_tex);
        }
    }
}
