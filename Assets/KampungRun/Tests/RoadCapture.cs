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
    /// The roads up close: stills of a few grid junctions, a Chow Kit street, real-KL patch streets and a
    /// flyover, each from the driving camera and from higher up; then a junction watched for half a minute
    /// (a signal cycle, cars stopping and going) and a corner where people cross. Written to
    /// Tools/promo_frames/x_roads/&lt;tag&gt;/ with counts in the log ([Roads] lines). Explicit: only when
    /// asked for by name.
    /// </summary>
    [Explicit, Category("Promo"), Timeout(1800000)]
    public class RoadCapture
    {
        const int W = 1280, H = 720, FPS = 30;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "x_roads"));

        RenderTexture _rt;
        Texture2D _tex;
        string _dir;

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
            File.WriteAllBytes(Path.Combine(_dir, name + ".jpg"), _tex.EncodeToJPG(92));
        }

        static void Aim(Vector3 eye, Vector3 at, float fov = 55f)
        {
            var cam = Camera.main;
            ChaseCamera.I.enabled = false;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            cam.fieldOfView = fov;
        }

        static Vector3 Junction(int i, int k) => new Vector3(CityBuilder.RoadX(i), 0f, CityBuilder.RoadZ(k));

        [UnityTest] public IEnumerator Before() => Shoot("before");
        [UnityTest] public IEnumerator After() => Shoot("after");

        IEnumerator Shoot(string tag)
        {
            Time.captureFramerate = 0;
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            float t0 = Time.realtimeSinceStartup;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(40);
            Debug.Log($"[Roads] boot to level ~{Time.realtimeSinceStartup - t0:F1}s (incl. 40 frames)");
            HUD.I.DebugSkipDialogue();
            GameInput.Locked = false;
            TouchControls.ForceOn = true;
            foreach (var c in Object.FindObjectsByType<Canvas>()) c.enabled = false;
            _rt = new RenderTexture(W, H, 24);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            _dir = Path.Combine(Root, tag);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            yield return Frames(10);

            var gm = GameManager.I;
            var p = gm.Player;
            var cam = Camera.main;
            var cc = ChaseCamera.I;
            float fov0 = cam.fieldOfView;
            var roads = gm.City.roads;

            // frame rate over a few seconds of free play (uncapped time, real frames)
            yield return Fps("home");

            // ---- stills: driving camera (the player's car parked in the left lane) and a higher view
            var car = gm.LastCar;
            Time.captureFramerate = FPS;
            var spots = new List<(string name, Vector3 at, Vector3 dir)>
            {
                ("j1_12_3", Junction(12, 3), Vector3.forward),
                ("j2_15_6", Junction(15, 6), Vector3.right),
                ("j3_chowkit", Junction(3, 17), Vector3.forward),
                ("j4_11_1", Junction(11, 1), Vector3.left),
            };
            // real-KL streets: the nearest road node to a few places, looking down one of its streets
            foreach (var place in new[] { "Dataran", "BukitBintang", "Masjid" })
                if (gm.City.places.TryGetValue(place, out var pp))
                {
                    var n = roads.Nearest(pp, true);
                    if (n != null && n.links.Count > 0) spots.Add(("p_" + place, n.pos, (n.links[0].pos - n.pos).normalized));
                }
            // a flyover deck
            foreach (var n in roads.nodes)
                if (n.pos.y > 5f && n.links.Count > 0) { spots.Add(("fly", n.pos, (n.links[0].pos - n.pos).normalized)); break; }

            p.EnterVehicle(car, true);
            yield return Seconds(0.5f);
            foreach (var (name, at, d0) in spots)
            {
                var dir = new Vector3(d0.x, 0f, d0.z).normalized;
                var left = new Vector3(-dir.z, 0f, dir.x);
                // drive in from down the street, the way you would (the kerbside parking and the traffic fill in
                // out of sight ahead of you): wait a few seconds further back, looking away
                var backAt = at - dir * 150f + left * roads.laneOffset + Vector3.up * 0.8f;
                car.Body.position = backAt; car.transform.position = backAt;
                car.Body.linearVelocity = Vector3.zero;
                cc.enabled = false;
                Aim(backAt + Vector3.up * 300f, backAt + Vector3.up * 600f + Vector3.forward, 50f);
                yield return Seconds(9f);
                var carAt = at - dir * 34f + left * roads.laneOffset + Vector3.up * 0.8f;
                car.Body.position = carAt; car.transform.position = carAt;
                car.Body.rotation = Quaternion.LookRotation(dir); car.transform.rotation = Quaternion.LookRotation(dir);
                car.Body.linearVelocity = Vector3.zero; car.Body.angularVelocity = Vector3.zero;
                cc.enabled = true;
                cam.fieldOfView = fov0;
                cc.target = car.transform;
                cc.SnapBehind();
                yield return Seconds(2.5f);
                car.Body.linearVelocity = Vector3.zero;
                Grab(name + "_drive");
                Aim(at - dir * 30f - left * 18f + Vector3.up * 26f, at + dir * 4f, 50f);
                yield return Frames(3);
                Grab(name + "_high");
                Aim(at - dir * 14f + left * 9f + Vector3.up * 1.7f, at + dir * 6f - left * 2f + Vector3.up * 0.6f, 60f);
                yield return Frames(3);
                Grab(name + "_street");
            }
            p.ExitVehicle(false, true);
            yield return Seconds(1f);

            // ---- a junction watched for a while: who stops, who goes. The signalled one nearest 12,3 (12,3 itself
            // before there were signals, so the before shots line up)
            var j = Junction(12, 3);
            JunctionControl sig = null;
            foreach (var jc in TrafficSignals.Junctions)
            {
                if (!jc.signal || !jc.grid) continue;
                int xs = 0;
                foreach (var c in jc.crossings) if (c != null) xs++;
                if (xs < 4) continue;
                if (sig == null || (jc.pos - j).sqrMagnitude < (sig.pos - j).sqrMagnitude) sig = jc;
            }
            if (sig != null) j = sig.pos;
            Debug.Log($"[Roads] watching the junction at {j} ({(sig != null ? "signalled" : "no signals")})");
            p.Teleport(j + new Vector3(14f, 0.5f, 14f), Quaternion.identity);
            yield return Seconds(25f);                         // traffic arrives round us
            int maxStopped = 0, redStart = TrafficSignals.StoppedAtRed;
            float step = sig != null ? TrafficSignals.Cycle / 10f : 4f;
            for (int s = 0; s < 10; s++)
            {
                Aim(j + new Vector3(-34f, 30f, -34f), j, 48f);
                yield return Frames(2);
                int stopped = 0, near = 0, holding = 0;
                foreach (var v in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
                {
                    if (v.role != VehicleRole.Traffic) continue;
                    var d = v.transform.position - j; d.y = 0f;
                    if (d.magnitude > 45f) continue;
                    near++;
                    if (v.Body.linearVelocity.magnitude < 0.6f) stopped++;
                    if (v.driver is AIDriver ad && ad.Holding) holding++;
                }
                maxStopped = Mathf.Max(maxStopped, stopped);
                Debug.Log($"[Roads] junction t={s * step:F1}s traffic within 45 m {near}, stopped {stopped} (holding {holding}); {SignalState(j)}");
                Grab($"cycle_{s}");
                // and from a driver's eye on the south approach, looking at the stop line and the signals
                Aim(j + new Vector3(-4.5f, 2.2f, -46f), j + new Vector3(-2f, 2.5f, 0f), 50f);
                yield return Frames(2);
                Grab($"cycle_{s}_eye");
                yield return Seconds(step);
            }
            Debug.Log($"[Roads] junction: most stopped at once {maxStopped}; stops at red city-wide during the watch {TrafficSignals.StoppedAtRed - redStart}");
            yield return Census("after cycle");

            // ---- people at the crossings of the same junction: wait for the green man (if it has signals)
            var corner = j + new Vector3(0f, 0f, -13.5f);
            if (sig != null)
            {
                for (int w = 0; w < 400 && !sig.WalkNow(out float left0); w++) yield return Frames(3);
                yield return Seconds(1.5f);
            }
            for (int s = 0; s < 4; s++)
            {
                Aim(j + new Vector3(6f, 9f, -32f), corner + new Vector3(-2f, 0f, 2f), 55f);
                yield return Frames(2);
                int onX = 0;
                foreach (var c in TrafficSignals.Crossings) onX += c.busy;
                Debug.Log($"[Roads] peds shot {s}: people on crossings city-wide {onX}; {SignalState(j)}");
                Grab($"peds_{s}");
                Aim(j + new Vector3(-30f, 22f, -30f), j, 50f);
                yield return Frames(2);
                Grab($"peds_{s}_high");
                yield return Seconds(2f);
            }
            yield return Census("end");

            // ---- an unsignalled crossroads: people wait for a gap, cars give way
            JunctionControl plain = null;
            foreach (var jc in TrafficSignals.Junctions)
            {
                if (jc.signal || !jc.grid || jc.crossings[0] == null || jc.crossings[1] == null) continue;
                if (plain == null || (jc.pos - j).sqrMagnitude < (plain.pos - j).sqrMagnitude) plain = jc;
            }
            if (plain != null)
            {
                p.Teleport(plain.pos + new Vector3(14f, 0.5f, 14f), Quaternion.identity);
                yield return Seconds(12f);
                for (int s = 0; s < 3; s++)
                {
                    Aim(plain.pos + new Vector3(-30f, 22f, -30f), plain.pos, 50f);
                    yield return Frames(2);
                    Grab($"plain_{s}");
                    yield return Seconds(4f);
                }
                Debug.Log($"[Roads] unsignalled junction at {plain.pos}");
            }
            // ---- a bus stop (its bay, and anyone waiting) and a street with parking bays
            var city = gm.City;
            if (city.busStops.Count > 0)
            {
                var best = city.busStops[0];
                foreach (var b in city.busStops) if ((b.pos - j).sqrMagnitude < (best.pos - j).sqrMagnitude) best = b;
                p.Teleport(best.pos + best.facing * -6f + Vector3.up * 0.5f, Quaternion.identity);
                yield return Seconds(6f);
                var along = Vector3.Cross(Vector3.up, best.facing);
                Aim(best.pos + best.facing * 6f + along * -16f + Vector3.up * 5f, best.pos + best.facing * 2.5f + along * 2f, 50f);
                yield return Frames(2);
                Grab("busstop");
            }
            Debug.Log($"[Roads] bus stops {city.busStops.Count}, parking bays {city.parkingBays.Count}");
            if (city.parkingBays.Count > 0)
            {
                var bay = city.parkingBays[0];
                foreach (var b in city.parkingBays) if ((b.pos - j).sqrMagnitude < (bay.pos - j).sqrMagnitude) bay = b;
                p.Teleport(bay.pos + bay.rot * Vector3.right * -5f + Vector3.up * 0.5f, Quaternion.identity);
                yield return Seconds(1f);
                // the parked cars only appear out of sight: walk away and come back
                p.Teleport(bay.pos + bay.rot * Vector3.forward * 120f + Vector3.up * 0.5f, Quaternion.identity);
                Aim(bay.pos + Vector3.up * 300f, bay.pos + Vector3.up * 600f + Vector3.forward, 50f);   // (looking away)
                yield return Seconds(6f);
                int parked = 0;
                foreach (var v in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) if (v.role == VehicleRole.Parked) parked++;
                var fwd = bay.rot * Vector3.forward;
                var lane = bay.rot * Vector3.right;            // toward the road's middle... or the kerb; look from above anyway
                Aim(bay.pos - fwd * 22f + Vector3.up * 9f - lane * 6f, bay.pos + fwd * 6f, 50f);
                yield return Frames(2);
                Grab("parking");
                Debug.Log($"[Roads] parked cars around {parked}");
            }

            yield return Census("final");
            yield return Fps("junction");
            // what the new road surface and furniture cost to draw: the same spot without them
            var paint = GameObject.Find("RoadPaint");
            if (paint != null)
            {
                paint.SetActive(false);
                yield return Fps("junction without the road paint meshes");
                paint.SetActive(true);
            }

            cc.enabled = true;
            TouchControls.ForceOn = false;
            Time.captureFramerate = 0;
            Debug.Log($"[Roads] wrote {_dir}");
        }

        /// <summary>Whatever the junction's signal says (nothing before signals existed).</summary>
        static string SignalState(Vector3 j)
        {
            var t = System.Type.GetType("KampungRun.TrafficSignals, KampungRun");
            if (t == null) return "";
            var m = t.GetMethod("Describe", new[] { typeof(Vector3) });
            return m != null ? " signal " + m.Invoke(null, new object[] { j }) : "";
        }

        /// <summary>People standing in a carriageway right now, and any crossing/stop counters the game keeps.</summary>
        IEnumerator Census(string when)
        {
            int onRoad = 0, all = 0;
            foreach (var ped in Pedestrian.All)
            {
                if (!ped) continue;
                all++;
                if (OnRoad(ped.transform.position)) onRoad++;
            }
            string extra = "";
            var t = System.Type.GetType("KampungRun.TrafficSignals, KampungRun");
            var m = t?.GetMethod("Stats");
            if (m != null) extra = " " + m.Invoke(null, null);
            Debug.Log($"[Roads] {when}: pedestrians {all}, in a carriageway now {onRoad}{extra}");
            yield return null;
        }

        static bool OnRoad(Vector3 p)
        {
            float gx = Mathf.Repeat(p.x - CityBuilder.X0 + CityBuilder.Road * 0.5f, CityBuilder.Pitch);
            float gz = Mathf.Repeat(p.z - CityBuilder.Z0 + CityBuilder.Road * 0.5f, CityBuilder.Pitch);
            return gx < CityBuilder.Road || gz < CityBuilder.Road;
        }

        IEnumerator Fps(string where)
        {
            int prevCap = Time.captureFramerate;
            Time.captureFramerate = 0;
            int prevRate = Application.targetFrameRate, prevVsync = QualitySettings.vSyncCount;
            Application.targetFrameRate = -1;                  // uncapped, to see what the frame costs
            QualitySettings.vSyncCount = 0;
            ChaseCamera.I.enabled = true;
            yield return Frames(30);
            int n = 0;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 4f) { n++; yield return null; }
            float secs = Time.realtimeSinceStartup - start;
            Debug.Log($"[Roads] fps at {where}: {n / secs:F1} ({n} frames in {secs:F1}s, batch editor, uncapped)");
            Application.targetFrameRate = prevRate;
            QualitySettings.vSyncCount = prevVsync;
            Time.captureFramerate = prevCap;
        }
    }
}
