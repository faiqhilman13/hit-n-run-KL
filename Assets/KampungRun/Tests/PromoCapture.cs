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

        /// <summary>PROMO_ONLY=01_family,07_police re-records just those shots (the rest still play, unrecorded, so every shot starts the same way).</summary>
        static readonly HashSet<string> Only = string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PROMO_ONLY")) ? null
            : new HashSet<string>(System.Environment.GetEnvironmentVariable("PROMO_ONLY").Split(','));

        /// <summary>Record a shot: perFrame(t 0..1) runs before each captured frame.</summary>
        IEnumerator Shot(string name, float seconds, System.Action<float> perFrame = null)
        {
            bool rec = Only == null || Only.Contains(name);
            _dir = Path.Combine(Root, name);
            if (rec)
            {
                if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
                Directory.CreateDirectory(_dir);
            }
            _n = 0;
            int frames = Mathf.RoundToInt(seconds * FPS);
            for (int i = 0; i < frames; i++)
            {
                perFrame?.Invoke(i / (float)frames);
                yield return null;
                if (rec) Grab();
            }
            Debug.Log($"[Promo] {name}: {frames} frames{(rec ? "" : " (not recorded)")}");
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

        /// <summary>How far down a route the point nearest p is.</summary>
        static float AlongOf(List<Vector3> pts, Vector3 p)
        {
            float acc = 0f, best = float.MaxValue, at = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 a = pts[i - 1], b = pts[i];
                float seg = Vector3.Distance(a, b);
                float u = seg > 0.01f ? Mathf.Clamp01(Vector3.Dot(p - a, b - a) / (seg * seg)) : 0f;
                float d = (Vector3.Lerp(a, b, u) - p).sqrMagnitude;
                if (d < best) { best = d; at = acc + u * seg; }
                acc += seg;
            }
            return at;
        }

        /// <summary>A route shifted sideways (+ = right, toward the middle of the road).</summary>
        static List<Vector3> Offset(List<Vector3> pts, float right)
        {
            var o = new List<Vector3>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                var d = pts[Mathf.Min(pts.Count - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]; d.y = 0;
                o.Add(d.sqrMagnitude < 0.01f ? pts[i] : pts[i] + new Vector3(d.z, 0f, -d.x).normalized * right);
            }
            return o;
        }

        /// <summary>
        /// The camera riding along with a car, `off` degrees round from behind it (negative = the driver's
        /// side, -180 = head-on), easing from off0 to off1 over the shot. The yaw is smoothed so steering
        /// wobble doesn't shake the picture. Returns the per-frame director for Shot().
        /// </summary>
        static System.Action<float> Track(Vehicle car, float off0, float off1, float dist, float pitch, float height, System.Action<float> also = null)
        {
            var cc = ChaseCamera.I;
            cc.target = car.transform; cc.targetBody = null; cc.height = height;
            cc.distance = dist; cc.pitch = pitch;
            cc.SnapBehind();
            float yaw = car.transform.eulerAngles.y + off0;
            cc.yaw = yaw;
            return t =>
            {
                float e = t * t * (3f - 2f * t);
                yaw = Mathf.LerpAngle(yaw, car.transform.eulerAngles.y + Mathf.Lerp(off0, off1, e), 0.1f);
                cc.yaw = yaw; cc.distance = dist; cc.pitch = pitch;
                also?.Invoke(t);
            };
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator RecordPromo()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);

            // The promo is about the family and KL: the real city from the air, the family at home, each of
            // them out driving at their own time of day, the fun of it (drifts, the polis, borrowed cars,
            // bopped pakciks), then the landmarks. Recorded level by level; Tools/make_promo.py puts it in order.

            // ================================================================ morning: Pak Mat's day
            yield return LoadLevel(1);
            var gm = GameManager.I;
            var p = gm.Player;
            var cc = ChaseCamera.I;
            var roads = gm.City.roads;

            // --- the real KL from the air: over Masjid Jamek where the rivers meet, on toward Merdeka 118
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

            // --- the family at home in Kampung Baru: Pak Mat, Mak Som, Along and Adik out front of the rumah
            {
                var home = gm.City.places["Home"];                                     // the yard in front of the verandah (the house faces +X)
                // a family snapshot: Mak and Ayah in the middle, Adik in front of Ayah, Along hanging back
                p.Teleport(home + new Vector3(-1.7f, 0.1f, -0.55f), Quaternion.Euler(0, 90, 0));
                var spots = new Dictionary<string, Vector3>
                {
                    ["maksom"] = home + new Vector3(-1.7f, 0f, 0.6f),
                    ["along"] = home + new Vector3(-2.2f, 0f, 1.65f),
                    ["adik"] = home + new Vector3(-1.0f, 0f, -1.3f),
                };
                var centre = home + new Vector3(-1.7f, 0f, -0.55f);
                foreach (var kv in spots)
                {
                    var n = gm.Missions.Npc(kv.Key);
                    if (!n) { Debug.LogWarning($"[Promo] no npc {kv.Key}"); continue; }
                    n.enabled = false;                                                  // (it would reset the waving)
                    var marker = n.transform.Find("MissionMarker");
                    if (marker) marker.gameObject.SetActive(false);
                    n.transform.SetPositionAndRotation(kv.Value, Quaternion.Euler(0, 90, 0));
                    centre += kv.Value;
                }
                centre /= spots.Count + 1;
                var piv = Pivot(new Vector3(centre.x, home.y + 1.3f, centre.z));
                cc.target = piv; cc.targetBody = null; cc.height = 0f;
                cc.distance = 4.3f; cc.pitch = 3f;
                cc.SnapBehind(); cc.yaw = 258f;
                yield return Frames(20);
                CharacterRig RigOf(string key) => gm.Missions.Npc(key) ? gm.Missions.Npc(key).GetComponent<CharacterRig>() : null;
                CharacterRig adikRig = RigOf("adik"), maksomRig = RigOf("maksom");
                yield return Shot("01_family", 4.5f, t =>
                {
                    cc.yaw = 258f + 24f * t; cc.distance = 4.3f - 0.6f * t; cc.pitch = 3f;
                    // Adik waves at the camera, then Mak joins in
                    if (adikRig) adikRig.waving = t > 0.08f;
                    if (maksomRig) maksomRig.waving = t > 0.3f;
                });
                Object.Destroy(piv.gameObject);
                cc.height = 1.6f;
            }

            // --- Pak Mat in the family Saga past Dataran Merdeka and Sultan Abdul Samad, seen from the driver's side
            {
                Vector3 Kerb(string key) => roads.Nearest(gm.City.places[key], true).pos;
                var route = roads.Route(new List<Vector3> { Kerb("Dataran"), Kerb("Masjid"), Kerb("PasarSeni") });
                Debug.Log($"[Promo] pakmat route {route.Count} points from {route[0]} to {route[route.Count - 1]}");
                Vehicle saga = null;
                yield return DriveRoute("saga", route, 8f, 11f, (v, d) => saga = v);
                cc.distance = 7f; cc.pitch = 12f;
                cc.SnapBehind();
                yield return Frames(45);
                yield return Shot("02_pakmat", 4.2f, Track(saga, -155f, -140f, 8f, 8f, 1.1f, t => ClearRoute(route, 16f, saga)));
                p.ExitVehicle(false, true);
                Object.Destroy(saga.gameObject);
                cc.height = 1.6f;
                yield return Frames(3);
            }

            // --- drift round a bulatan: handbrake pulses on the ring, smoke and skid marks
            {
                var hub = new Vector3(CityBuilder.RoadX(10), 0f, CityBuilder.RoadZ(1));
                var route = roads.Route(new List<Vector3> { J(10, 0), hub + new Vector3(0f, 0.22f, -17.5f), hub + new Vector3(-17.5f, 0.22f, 0f),
                                                            hub + new Vector3(0f, 0.22f, 17.5f), J(11, 1) });
                Vehicle car = null; RoadDrv drv = null;
                yield return DriveRoute("myvi", route, 72f, 13f, (v, d) => { car = v; drv = d; });
                cc.distance = 8f; cc.pitch = 22f;
                cc.SnapBehind();
                yield return Frames(24);
                float ringT = 0f;
                yield return Shot("06_drift", 5.0f, t =>
                {
                    ClearRoute(route, 16f, car);
                    var flat = car.transform.position - hub; flat.y = 0;
                    bool onRing = flat.magnitude < 23f;
                    if (onRing) ringT += 1f / FPS;
                    drv.handbrake = onRing && ringT % 0.9f < 0.45f;
                    cc.pitch = 22f;
                });
                drv.handbrake = false;
                p.ExitVehicle(false, true);
                Object.Destroy(car.gameObject);
                yield return Frames(3);
            }

            // --- the polis on your tail down the avenue: camera out ahead, looking back at the chase
            {
                var route = roads.Route(new List<Vector3> { J(1, 14), J(7, 14) });
                Debug.Log($"[Promo] police route {route.Count} points from {route[0]} to {route[route.Count - 1]}");
                Vehicle car = null; RoadDrv drv = null;
                yield return DriveRoute("saga", route, 120f, 19f, (v, d) => { car = v; drv = d; });
                drv.route.avoid = false;                                                // nothing brakes: the polis are right behind
                cc.distance = 7f; cc.pitch = 12f;
                cc.SnapBehind();
                yield return Frames(40);                                                // up to speed first
                float a = AlongOf(route, car.transform.position);
                var cops = new List<Vehicle>();
                // one on your bumper, one coming up the middle to box you in
                foreach (var (lane, back, speed) in new[] { (route, 7.5f, 19.3f), (Offset(route, 2.4f), 13f, 19.9f) })
                {
                    var at = Along(lane, a - back, out var fwd, out int next);
                    var cop = VehicleSpawner.Spawn("polis", at + Vector3.up * 0.5f, Quaternion.LookRotation(fwd), VehicleRole.Police, gm.transform);
                    var cd = new RoadDrv(lane, speed, next);
                    cd.route.avoid = false;
                    cop.driver = cd;
                    cop.Body.linearVelocity = fwd * car.Body.linearVelocity.magnitude;  // already flat out
                    cops.Add(cop);
                }
                yield return Frames(2);
                yield return Shot("07_police", 4.5f, Track(car, -160f, -150f, 9.5f, 6f, 1.0f, t => ClearRoute(route, 16f, car)));
                Debug.Log($"[Promo] police gaps {Vector3.Distance(car.transform.position, cops[0].transform.position):F1} {Vector3.Distance(car.transform.position, cops[1].transform.position):F1}");
                foreach (var c in cops) if (c) Object.Destroy(c.gameObject);
                p.ExitVehicle(false, true);
                Object.Destroy(car.gameObject);
                cc.height = 1.6f;
                yield return Frames(3);
            }

            // --- borrow a car: the driver bails out ("KERETA AKU!") and off you go
            {
                var away = roads.Route(new List<Vector3> { J(12, 1), J(12, 3) });
                var at = Along(away, 30f, out var fwd, out int next);
                foreach (var t in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
                {
                    float d = Vector3.Distance(t.transform.position, at);
                    if (d < 25f || (d < 70f && t.role == VehicleRole.Traffic)) Object.Destroy(t.gameObject);
                }
                var victim = VehicleSpawner.Spawn("myvi", at + Vector3.up * 0.6f, Quaternion.LookRotation(fwd), VehicleRole.Traffic, gm.transform);
                ModelFactory.Recolor(victim.gameObject, new Dictionary<string, Color> { ["car_paint"] = new Color(0.98f, 0.8f, 0.12f) });
                VehicleSpawner.AddDriver(victim);
                victim.driver = null;                                                   // stopped at the kerb
                yield return Frames(30);
                var right = Vector3.Cross(Vector3.up, fwd);
                p.Teleport(at + right * 3.4f + fwd * 0.4f + Vector3.up * 0.2f, Quaternion.LookRotation(-right));
                cc.target = p.transform; cc.targetBody = null; cc.height = 1.6f;
                cc.distance = 5.8f; cc.pitch = 9f;
                float camYaw = victim.transform.eulerAngles.y - 115f;
                cc.SnapBehind(); cc.yaw = camYaw;
                yield return Frames(5);
                bool asked = false, off = false;
                yield return Shot("08_carjack", 5.0f, t =>
                {
                    ClearRoute(away, 16f, victim);
                    if (!off) { cc.yaw = camYaw; cc.distance = 5.8f; cc.pitch = 9f; }
                    if (!asked && t > 0.04f) { p.EnterVehicle(victim); asked = true; }
                    if (p.Driving && !off)
                    {
                        // in, and away up the street (the chase camera swings round behind)
                        victim.driver = new RoadDrv(away, 11f, next);
                        off = true;
                    }
                });
                Debug.Log($"[Promo] carjack: driving {p.Driving}, car at {victim.transform.position}");
                p.ExitVehicle(false, true);
                Object.Destroy(victim.gameObject);
                SamanMeter.I?.ClearAll();
                yield return Frames(3);
            }

            // --- bopping: jab, jab, big one, kick - they topple and get back up
            {
                var bopAt = CityBuilder.BlockCenter(11, 3) + new Vector3(-CityBuilder.Block * 0.5f + 1.8f, 0.2f, 4f);   // the west sidewalk
                var ped = PedestrianSpawner.Spawn(bopAt, WalkZone.FromRect(new Rect(bopAt.x - 20, bopAt.z - 20, 40, 40), 0f), gm.transform, "chr_pakcik");
                p.Teleport(bopAt - Vector3.forward * 1.1f, Quaternion.LookRotation(Vector3.forward));
                cc.target = p.transform; cc.targetBody = null;
                cc.distance = 4.6f; cc.pitch = 8f; cc.yaw = 60f;
                cc.SnapBehind(); cc.yaw = 60f;
                yield return Frames(20);
                float nextHit = 0.08f;
                int hits = 0;
                yield return Shot("09_bop", 4.6f, t =>
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
                SamanMeter.I?.ClearAll();
            }

            // --- the landmarks, framed on their models (the outlying blocks face +Z, their marker on the road in front)
            {
                const float L = CityBuilder.LandmarkScale;
                Vector3 C(string place, float back = 24.5f) => gm.City.places[place] - new Vector3(0, 0, back * L);
                p.Teleport(CityBuilder.BlockCenter(0, 0) + Vector3.up * 0.3f, Quaternion.identity);   // out of shot (on the map)
                var m118 = Landmark("Merdeka118", out float m118H);
                yield return Orbit("10_merdeka118", m118, 2.8f, m118H * 0.62f, 2f, 200f, -40f, m118H * 0.3f);
                var klcc = Landmark("Petronas", out float klccH);
                yield return Orbit("11_klcc", klcc, 2.8f, klccH * 0.95f, 16f, 215f, 40f, klccH * 0.35f);
                var jamek = Landmark("MasjidJamek", out float jamekH);
                yield return Orbit("12_jamek", jamek, 2.6f, 60f, 12f, 60f, 45f, jamekH * 0.4f);
                var negara = Landmark("MasjidNegara", out float negaraH);
                yield return Orbit("13_masjidnegara", negara, 2.6f, 120f, 20f, 150f, 45f, negaraH * 0.25f);
                var tower = Landmark("KLTower", out float towerH);
                yield return Orbit("14_kltower", tower, 2.6f, towerH * 0.75f, 4f, 240f, -40f, towerH * 0.34f);
                yield return Orbit("15_batu", C("BatuCaves") + new Vector3(0, 0, 16f * L), 2.6f, 30f * L, 14f, 150f, 50f, 7f * L);
            }

            // ================================================================ midday: Mak Som's Myvi up the Jalan Kuching flyover
            yield return LoadLevel(2);
            gm = GameManager.I; p = gm.Player; cc = ChaseCamera.I;
            {
                var route = gm.City.roads.Route(new List<Vector3> { J(3, 14), J(3, 19) });
                Vehicle car = null;
                yield return DriveRoute("kereta", route, 100f, 17f, (v, d) => car = v);
                cc.distance = 6.5f; cc.pitch = 12f;
                cc.SnapBehind();
                yield return Frames(40);
                // from behind as she climbs, round to her side at the top
                yield return Shot("03_maksom", 4.2f, Track(car, -5f, -115f, 7.5f, 10f, 1.3f, t => ClearRoute(route, 16f, car)));
            }

            // ================================================================ sunset: Along on his kapcai, weaving under the flyover
            yield return LoadLevel(3);
            gm = GameManager.I; p = gm.Player; cc = ChaseCamera.I;
            {
                var route = Weave(gm.City.roads.Route(new List<Vector3> { J(0, 16), J(6, 16) }), 1.3f, 30f);
                Vehicle bike = null;
                yield return DriveRoute("kapcai", route, 288f, 14f, (v, d) => bike = v);
                cc.distance = 4.8f; cc.pitch = 10f;
                cc.SnapBehind();
                yield return Frames(40);
                yield return Shot("04_along", 4.2f, Track(bike, -155f, -135f, 5.5f, 7f, 0.9f, t => ClearRoute(route, 14f, bike)));
            }

            // ================================================================ night: Adik's Kancil over the Jalan Ampang flyover
            yield return LoadLevel(4);
            gm = GameManager.I; p = gm.Player; cc = ChaseCamera.I;
            {
                var route = gm.City.roads.Route(new List<Vector3> { J(11, 15), J(14, 15), J(16, 15), J(18, 15) });   // over the flyover, not round it
                Vehicle kancil = null;
                // (the avenue goes round Bukit Nanas first: start just short of the flyover, whatever the way there)
                yield return DriveRoute("kancil", route, RampAlong(route) - 25f, 15f, (v, d) => kancil = v);
                cc.distance = 6f; cc.pitch = 10f;
                cc.SnapBehind();
                yield return Frames(45);
                // from behind as the Kancil climbs, round to Adik at the wheel (KLCC is lost in the night fog from up here)
                yield return Shot("05_adik", 4.2f, Track(kancil, -5f, -140f, 7f, 7f, 1.2f, t => ClearRoute(route, 16f, kancil)));
            }

            Time.captureFramerate = 0;
            _rt.Release();
            Object.Destroy(_tex);
        }
    }
}
