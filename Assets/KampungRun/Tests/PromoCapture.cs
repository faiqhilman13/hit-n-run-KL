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

        /// <summary>Where a real-KL landmark stands (its model's footprint centre, at street level) and how tall it is.</summary>
        static Vector3 Landmark(string name, out float height)
        {
            var go = GameObject.Find("Landmark_" + name);
            height = 0f;
            if (!go) { Debug.LogWarning($"[Promo] no landmark {name}"); return Vector3.zero; }
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            height = b.size.y;
            return new Vector3(b.center.x, 0f, b.center.z);
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
                var at = GameManager.I.City.places["Padang"] + new Vector3(0f, 0.9f, 0f);      // the open padang
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

        /// <summary>
        /// Drives a real road route the way the traffic AI steers, so a promo drive stays on the street
        /// instead of ending in a lamp post. The director can pull the handbrake mid-shot.
        /// </summary>
        class RoadDrv : IDriver
        {
            public readonly RouteDriver route;
            public bool handbrake;
            public RoadDrv(List<Vector3> pts, float speed, int next) { route = new RouteDriver(pts, speed) { arriveRadius = 7f, index = next }; }
            public void Drive(Vehicle v, out float t, out float s, out bool hb)
            {
                route.Drive(v, out t, out s, out hb);
                if (handbrake) { hb = true; t = Mathf.Max(t, 0.5f); }
            }
        }

        static Vector3 J(int i, int k) => GameManager.I.City.roads.Nearest(new Vector3(CityBuilder.RoadX(i), 0f, CityBuilder.RoadZ(k)), true).pos;

        /// <summary>The point `along` metres down a route, the way it faces there and the index of the point after it.</summary>
        static Vector3 Along(List<Vector3> pts, float along, out Vector3 fwd, out int next)
        {
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                if (acc + seg >= along)
                {
                    fwd = pts[i] - pts[i - 1]; fwd.y = 0; fwd.Normalize();
                    next = i;
                    return Vector3.Lerp(pts[i - 1], pts[i], (along - acc) / Mathf.Max(seg, 0.01f));
                }
                acc += seg;
            }
            fwd = Vector3.forward; next = pts.Count - 1;
            return pts[pts.Count - 1];
        }

        /// <summary>How far down a route its first raised point (the foot of a flyover) is.</summary>
        static float RampAlong(List<Vector3> pts)
        {
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                acc += Vector3.Distance(pts[i - 1], pts[i]);
                if (pts[i].y > 0.5f) return acc;
            }
            return acc * 0.5f;
        }

        /// <summary>A route resampled every 4 m with a sideways sine on it: lane to lane on a kapcai.</summary>
        static List<Vector3> Weave(List<Vector3> pts, float amp, float wavelength)
        {
            var o = new List<Vector3>();
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                var d = b - a; d.y = 0;
                float len = d.magnitude;
                if (len < 0.01f) continue;
                var left = new Vector3(-d.z, 0, d.x) / len;
                for (float s = 0f; s < len; s += 4f, acc += 4f)
                    o.Add(Vector3.Lerp(a, b, s / len) + left * Mathf.Sin(acc / wavelength * Mathf.PI * 2f) * amp);
            }
            o.Add(pts[pts.Count - 1]);
            return o;
        }

        /// <summary>No traffic anywhere near the route (a promo drive never meets anyone head-on).</summary>
        static void ClearRoute(List<Vector3> pts, float radius, Vehicle keep)
        {
            foreach (var v in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
            {
                if (v == keep || v.role != VehicleRole.Traffic) continue;
                foreach (var q in pts)
                    if ((v.transform.position - q).sqrMagnitude < radius * radius) { Object.Destroy(v.gameObject); break; }
            }
        }

        /// <summary>Put the player in a car `along` metres down a route, driving it at `speed`.</summary>
        IEnumerator DriveRoute(string car, List<Vector3> pts, float along, float speed, System.Action<Vehicle, RoadDrv> ready)
        {
            var gm = GameManager.I;
            var at = Along(pts, along, out var fwd, out int next);
            ClearRoute(pts, 16f, null);
            var v = gm.SummonCar(car, at + Vector3.up * 0.6f, Quaternion.LookRotation(fwd));
            yield return Frames(12);
            gm.Player.EnterVehicle(v, true);
            var d = new RoadDrv(pts, speed, next);
            v.driver = d;
            ready(v, d);
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
            var roads = gm.City.roads;

            // --- 0. the real KL from the air: over Masjid Jamek where the rivers meet, on toward Merdeka 118
            {
                var jamek = Landmark("MasjidJamek", out _);
                var m118 = Landmark("Merdeka118", out float m118H);
                cc.enabled = false;
                var cam = Camera.main.transform;
                float fs = RenderSettings.fogStartDistance, fe = RenderSettings.fogEndDistance;
                RenderSettings.fogStartDistance = fs * 2.5f; RenderSettings.fogEndDistance = fe * 2.5f;
                Vector3 a0 = jamek + new Vector3(-230f, 175f, 190f), a1 = jamek + new Vector3(70f, 150f, -40f);
                Vector3 l0 = jamek, l1 = m118 + Vector3.up * m118H * 0.18f;
                yield return Shot("00_aerial", 5.5f, t =>
                {
                    float e = t * t * (3f - 2f * t);
                    cam.position = Vector3.Lerp(a0, a1, e);
                    cam.LookAt(Vector3.Lerp(l0, l1, e));
                });
                RenderSettings.fogStartDistance = fs; RenderSettings.fogEndDistance = fe;
                cc.enabled = true;
            }

            // --- 1. cruise: a red Myvi up the Jalan Kuching flyover (it follows the road, like traffic does)
            {
                var route = roads.Route(new List<Vector3> { J(3, 14), J(3, 19) });
                Vehicle myvi = null; RoadDrv drv = null;
                yield return DriveRoute("myvi", route, 100f, 17f, (v, d) => { myvi = v; drv = d; });
                cc.distance = 6.5f; cc.pitch = 12f;
                cc.SnapBehind();
                yield return Frames(40);                                     // get up to speed off camera
                yield return Shot("01_cruise", 5.5f, t => ClearRoute(route, 16f, myvi));
                p.ExitVehicle(false, true);
                Object.Destroy(myvi.gameObject);
                yield return Frames(3);
            }

            // --- 2. drift round a bulatan: handbrake pulses on the ring, smoke and skid marks
            {
                var hub = new Vector3(CityBuilder.RoadX(10), 0f, CityBuilder.RoadZ(1));
                var route = roads.Route(new List<Vector3> { J(10, 0), hub + new Vector3(0f, 0.22f, -17.5f), hub + new Vector3(-17.5f, 0.22f, 0f),
                                                            hub + new Vector3(0f, 0.22f, 17.5f), J(11, 1) });
                Debug.Log($"[Promo] drift route {route.Count} points from {route[0]} to {route[route.Count - 1]}");
                Vehicle car = null; RoadDrv drv = null;
                yield return DriveRoute("myvi", route, 72f, 13f, (v, d) => { car = v; drv = d; });
                cc.distance = 8f; cc.pitch = 22f;
                cc.SnapBehind();
                yield return Frames(24);
                float ringT = 0f;
                yield return Shot("02_drift", 5.5f, t =>
                {
                    ClearRoute(route, 16f, car);
                    var flat = car.transform.position - hub; flat.y = 0;
                    bool onRing = flat.magnitude < 23f;
                    if (onRing) ringT += 1f / FPS;
                    drv.handbrake = onRing && ringT % 0.9f < 0.45f;
                    cc.pitch = 22f;
                });
                drv.handbrake = false;
                Debug.Log($"[Promo] drift ended at {car.transform.position} (ring centre {hub})");
                p.ExitVehicle(false, true);
                Object.Destroy(car.gameObject);
                yield return Frames(3);
            }

            // --- 3. showroom dolly along the Malaysian cars (under the Sungai Besi flyover)
            var ids = new[] { "myvi", "saga", "kancil", "van", "hilux", "kapcai", "teksi", "polis" };
            var row = new Vector3(CityBuilder.RoadX(12) + 2.5f, 0.6f, CityBuilder.RoadZ(2) + 12f);
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
            var bopAt = CityBuilder.BlockCenter(11, 3) + new Vector3(-CityBuilder.Block * 0.5f + 1.8f, 0.2f, 4f);   // the west sidewalk
            var ped = PedestrianSpawner.Spawn(bopAt, WalkZone.FromRect(new Rect(bopAt.x - 20, bopAt.z - 20, 40, 40), 0f), gm.transform, "chr_pakcik");
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

            // --- 6. kapcai weaving lane to lane down a Chow Kit street, under the Jalan Kuching flyover
            {
                var route = Weave(roads.Route(new List<Vector3> { J(0, 16), J(6, 16) }), 1.3f, 30f);
                Vehicle bike = null; RoadDrv drv = null;
                yield return DriveRoute("kapcai", route, 288f, 14f, (v, d) => { bike = v; drv = d; });
                cc.distance = 4.8f; cc.pitch = 10f;
                cc.SnapBehind();
                yield return Frames(40);
                yield return Shot("06_kapcai", 4.5f, t => ClearRoute(route, 14f, bike));
                p.ExitVehicle(false, true);
                Object.Destroy(bike.gameObject);
            }

            // --- 7. landmarks fly-arounds. The outlying landmark blocks face +Z with their place marker on
            // the road in front; the real-KL ones are framed on their models.
            const float L = CityBuilder.LandmarkScale;                                   // landmarks are laid out and scaled by this
            Vector3 C(string place, float back = 24.5f) => gm.City.places[place] - new Vector3(0, 0, back * L);
            p.Teleport(CityBuilder.BlockCenter(0, 0) + Vector3.up * 0.3f, Quaternion.identity);   // out of shot (on the map)
            yield return Orbit("07_batu", C("BatuCaves") + new Vector3(0, 0, 16f * L), 3.0f, 30f * L, 14f, 150f, 50f, 7f * L);
            var m118b = Landmark("Merdeka118", out float m118Hb);
            yield return Orbit("08_merdeka118", m118b, 3.0f, m118Hb * 0.62f, 2f, 200f, -40f, m118Hb * 0.3f);
            var klcc = Landmark("Petronas", out float klccH);
            yield return Orbit("09_klcc", klcc, 3.0f, klccH * 0.95f, 16f, 215f, 40f, klccH * 0.35f);
            var negara = Landmark("MasjidNegara", out float negaraH);
            yield return Orbit("10_masjidnegara", negara, 2.8f, 120f, 20f, 150f, 45f, negaraH * 0.25f);
            var jamekB = Landmark("MasjidJamek", out float jamekH);
            yield return Orbit("11_jamek", jamekB, 2.6f, 60f, 12f, 60f, 45f, jamekH * 0.4f);
            var tower = Landmark("KLTower", out float towerH);
            yield return Orbit("12_kltower", tower, 2.8f, towerH * 0.75f, 4f, 240f, -40f, towerH * 0.34f);

            // ================================================================ night, Adik's Kancil over the Jalan Ampang flyover
            yield return LoadLevel(4);
            gm = GameManager.I;
            p = gm.Player;
            cc = ChaseCamera.I;
            {
                var route = gm.City.roads.Route(new List<Vector3> { J(11, 15), J(14, 15), J(16, 15), J(18, 15) });   // over the flyover, not round it
                Debug.Log($"[Promo] night route {route.Count} points from {route[0]} to {route[route.Count - 1]}");
                Vehicle kancil = null; RoadDrv drv = null;
                // (the avenue goes round Bukit Nanas first: start just short of the flyover, whatever the way there)
                yield return DriveRoute("kancil", route, RampAlong(route) - 25f, 15f, (v, d) => { kancil = v; drv = d; });
                cc.distance = 6f; cc.pitch = 10f;
                cc.SnapBehind();
                yield return Frames(45);
                Debug.Log($"[Promo] night start {kancil.transform.position}");
                yield return Shot("13_night", 4.5f, t => ClearRoute(route, 16f, kancil));
                Debug.Log($"[Promo] night end {kancil.transform.position}");
            }

            Time.captureFramerate = 0;
            _rt.Release();
            Object.Destroy(_tex);
        }
    }
}
