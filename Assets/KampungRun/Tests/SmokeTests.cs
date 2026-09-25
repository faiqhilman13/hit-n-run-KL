using System.Collections;
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
    /// End-to-end smoke test: boots the real game scene, walks, punches, drives,
    /// starts a mission and loads the night level, saving screenshots along the way
    /// to Tools/previews so the art can be checked without opening the editor.
    /// </summary>
    public class SmokeTests
    {
        static string ShotDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "previews"));

        class ScriptedDriver : IDriver
        {
            public float throttle = 1f, steer;
            public void Drive(Vehicle v, out float t, out float s, out bool hb) { t = throttle; s = steer; hb = false; }
        }

        static void Shot(string name)
        {
            var cam = Camera.main;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var hud = Object.FindAnyObjectByType<Canvas>();
            RenderMode old = RenderMode.ScreenSpaceOverlay;
            if (hud) { old = hud.renderMode; hud.renderMode = RenderMode.ScreenSpaceCamera; hud.worldCamera = cam; hud.planeDistance = 1f; }
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            if (hud) hud.renderMode = old;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(ShotDir);
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        // The filler streets south of the old town (Jalan Hang Tuah / Bukit Petaling): plain grid
        // avenues, away from the real-KL districts, for tests that need a straight road.
        static Vector3 South(int i, int k, float dx = 0f, float dz = 0f, float y = 0.6f) =>
            new Vector3(CityBuilder.RoadX(i) + dx, y, CityBuilder.RoadZ(k) + dz);

        static WalkZone Box(Vector3 c, float half) => WalkZone.FromRect(new Rect(c.x - half, c.z - half, half * 2f, half * 2f), 0f);

        static Transform FindDeep(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        /// <summary>Headroom above point p: how far the topmost body surface on the vertical through
        /// p (the roof skin) sits above it, less the skin thickness. Negative = the roof cuts the head.
        /// If the topmost surface is far below p the vertical passes through a window, not the roof:
        /// that point is skipped (returns 9).</summary>
        static float RoofGap(Vehicle v, Vector3 p, Vector3 up, float floorH)
        {
            float topT = -99f;
            foreach (var mf in v.GetComponentsInChildren<MeshFilter>())
            {
                // the roof is part of the body shell
                var n = mf.name;
                if (!mf.sharedMesh || n != "Body") continue;
                var m = mf.transform.localToWorldMatrix;
                var vs = mf.sharedMesh.vertices;
                var tris = mf.sharedMesh.triangles;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(vs[tris[i]]), b = m.MultiplyPoint3x4(vs[tris[i + 1]]), c = m.MultiplyPoint3x4(vs[tris[i + 2]]);
                    // Moller-Trumbore on the whole vertical line (t signed along up)
                    Vector3 e1 = b - a, e2 = c - a, pv = Vector3.Cross(up, e2);
                    float det = Vector3.Dot(e1, pv);
                    if (Mathf.Abs(det) < 1e-9f) continue;
                    Vector3 tv = p - a;
                    float u = Vector3.Dot(tv, pv) / det;
                    if (u < 0 || u > 1) continue;
                    Vector3 qv = Vector3.Cross(tv, e1);
                    float w = Vector3.Dot(up, qv) / det;
                    if (w < 0 || u + w > 1) continue;
                    topT = Mathf.Max(topT, Vector3.Dot(e2, qv) / det);
                }
            }
            // the roof can only cut the head above the head bone; a surface lower than that is a sill
            // seen past the side glass
            return topT < -0.15f || Vector3.Dot(p, up) + topT < floorH ? 9f : topT - 0.03f;
        }

        static string StateName(Animator a)
        {
            var st = a.GetCurrentAnimatorStateInfo(0);
            foreach (var n in new[] { "Loco", "Ride", "Sit", "Mount", "Dismount", "Hit", "Knock" }) if (st.IsName(n)) return n;
            return st.shortNameHash.ToString();
        }

        static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
        }

        static void CloseBoxes()
        {
            // skip any dialogue/menus so the test can drive the game directly
            GameInput.Locked = false;
        }

        static void Skip() { HUD.I.DebugSkipDialogue(); GameInput.Locked = false; }

        static void MoveCar(Vehicle v, Vector3 to)
        {
            v.Body.position = to + Vector3.up * 0.8f;
            v.transform.position = to + Vector3.up * 0.8f;
            v.Body.linearVelocity = Vector3.zero;
        }

        /// <summary>Drive one objective to completion the way a player would (mostly).</summary>
        static IEnumerator Solve(GameManager gm, Objective o)
        {
            var p = gm.Player;
            switch (o)
            {
                case TalkObjective _:
                    var t = o.Target;
                    Assert.IsTrue(t.HasValue, "talk target missing");
                    NPC best = null;
                    foreach (var n in gm.Missions.npcs.Values)
                        if (best == null || Vector3.Distance(n.transform.position, t.Value) < Vector3.Distance(best.transform.position, t.Value)) best = n;
                    best.Interact(p);
                    yield return null;
                    Skip();
                    break;
                case GetInCarObjective _:
                    if (gm.LastCar == null) gm.SummonCar("saga", p.transform.position + Vector3.right * 4, Quaternion.identity);
                    if (!p.Driving) p.EnterVehicle(gm.LastCar, true);
                    break;
                case GoToObjective _:
                    if (!p.Driving && gm.LastCar != null && !gm.LastCar.Wrecked) p.EnterVehicle(gm.LastCar, true);
                    if (p.Driving) MoveCar(p.vehicle, o.Target.Value);
                    else p.Teleport(o.Target.Value, Quaternion.identity);
                    break;
                case CollectObjective _:
                    foreach (var pk in gm.Missions.GetComponentsInChildren<Pickup>()) if (pk.kind == Pickup.Kind.MissionItem) pk.Collect();
                    break;
                case DestroyObjective d:
                    yield return null;
                    Assert.IsNotNull(d.target, "destroy target missing");
                    d.target.Damage(99999f);
                    break;
                case SmashObjective _:
                    foreach (var b in gm.Missions.GetComponentsInChildren<BurungKamera>()) b.Smash();
                    foreach (var b in gm.Missions.GetComponentsInChildren<Breakable>()) b.Hit(b.transform.position + Vector3.forward, 5f, true);
                    break;
                case EvadeObjective _:
                    yield return null;
                    Assert.IsTrue(SamanMeter.I.Wanted, "evade objective should make you wanted");
                    SamanMeter.I.ClearAll();
                    break;
                default:
                    // follow / race / dialogue: those need real driving, just exercise begin/end
                    yield return new WaitForSeconds(0.5f);
                    o.debugForceDone = true;
                    break;
            }
            yield return null;
            Skip();
        }

        static IEnumerator RunMission(GameManager gm, Mission m)
        {
            gm.Missions.StartMission(m);
            Skip();
            int guard = 0;
            while (gm.Missions.Active && gm.Missions.Current == m && guard++ < 40)
            {
                var o = gm.Missions.Step;
                Assert.IsNull(o.failReason, $"'{m.title}' failed: {o.failReason}");
                yield return Solve(gm, o);
                yield return null;
                yield return null;
            }
            Skip();
            Assert.IsFalse(gm.Missions.Active && gm.Missions.Current == m, $"mission '{m.title}' got stuck");
        }

        [UnityTest]
        public IEnumerator AIBehaves()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(20);
            Skip();
            var gm = GameManager.I;
            var cars = new System.Collections.Generic.List<Vehicle>();
            foreach (var v in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) if (v.role == VehicleRole.Traffic) cars.Add(v);
            var carStart = new System.Collections.Generic.Dictionary<Vehicle, Vector3>();
            foreach (var v in cars) carStart[v] = v.transform.position;
            var peds = new System.Collections.Generic.List<Pedestrian>(Pedestrian.All);
            var pedStart = new System.Collections.Generic.Dictionary<Pedestrian, Vector3>();
            foreach (var p in peds) pedStart[p] = p.transform.position;

            // a scripted follow-mission style car on a real route
            var route = new System.Collections.Generic.List<Vector3>
            {
                South(12, 3, 0, 0, 0.3f),
                South(12, 1, 0, 0, 0.3f),
                South(13, 1, 0, 0, 0.3f),
            };
            var start = South(10, 3, 0, 0, 0.9f);
            var follower = VehicleSpawner.Spawn("van", start, Quaternion.LookRotation(Vector3.right), VehicleRole.MissionTarget);
            var rd = new RouteDriver(route, 13f);
            follower.driver = rd;

            yield return Seconds(12f * CityBuilder.WorldScale);
            int moved = 0, flipped = 0, alive = 0;
            foreach (var v in cars)
            {
                if (!v) continue;
                alive++;
                if (Vector3.Distance(carStart[v], v.transform.position) > 20f) moved++;
                if (Vector3.Dot(v.transform.up, Vector3.up) < 0.5f) flipped++;
            }
            int pedMoved = 0, pedAlive = 0;
            foreach (var p in peds)
            {
                if (!p) continue;
                pedAlive++;
                if (Vector3.Distance(pedStart[p], p.transform.position) > 3f) pedMoved++;
            }
            Debug.Log($"[AI] traffic moved {moved}/{alive} flipped={flipped}  peds moved {pedMoved}/{pedAlive}  route idx={rd.index}/{route.Count}");
            Shot("11_traffic");
            Assert.Greater(moved, alive / 2, "most traffic should be driving");
            Assert.LessOrEqual(flipped, 2, "traffic keeps flipping over");
            Assert.Greater(pedMoved, pedAlive / 2, "most pedestrians should be walking");

            yield return Seconds(14f * CityBuilder.WorldScale);
            Debug.Log($"[AI] route idx after 26s={rd.index}/{route.Count} pos={follower.transform.position}");
            Assert.GreaterOrEqual(rd.index, 2, "route-following car didn't make its way along the route");

            // police chase
            SamanMeter.I.ForceWanted();
            var player = gm.Player;
            float nearest = float.MaxValue;
            int police = 0;
            bool shot = false;
            float end = Time.time + 45f;   // arrival time depends on where the patrol spawns (the map is wider now)
            float reforce = 0f;
            while (Time.time < end && nearest > 12f)
            {
                // keep the player wanted for the whole window: the test is about pursuit, and heat
                // otherwise cools off while the patrol is still driving across the map
                if ((reforce -= Time.deltaTime) <= 0f) { SamanMeter.I.ForceWanted(); reforce = 1f; }
                foreach (var v in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
                    if (v.role == VehicleRole.Police) { police = Mathf.Max(police, 1); nearest = Mathf.Min(nearest, Vector3.Distance(v.transform.position, player.Focus)); }
                if (!shot && nearest < 30f) { Shot("12_police"); shot = true; }
                yield return null;
            }
            Debug.Log($"[AI] police={police} nearest={nearest:F1}m bust={SamanMeter.I.BustProgress:F2} wanted={SamanMeter.I.Wanted}");
            Assert.Greater(police, 0, "no police spawned");
            Assert.Less(nearest, 25f, "police never reached the player");
        }

        [UnityTest]
        public IEnumerator PlayThroughAllLevels()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(20);
            var gm = GameManager.I;
            for (int level = 1; level <= GameData.LastLevel; level++)
            {
                Skip();
                Assert.AreEqual(level, GameState.Level, "wrong level loaded");
                var story = new System.Collections.Generic.List<Mission>(gm.Missions.story);
                Assert.AreEqual(GameData.MissionsPerLevel, story.Count);
                var race = gm.Missions.race;
                yield return RunMission(gm, race);
                Assert.IsTrue(GameState.Data.raceDone[level], $"race of level {level} not recorded");
                for (int i = 0; i < story.Count; i++)
                {
                    Debug.Log($"[Play] L{level} M{i + 1}: {story[i].title}");
                    yield return RunMission(gm, story[i]);
                    Assert.AreEqual(i + 1, GameState.Data.missionsDone[level], $"mission {story[i].title} not recorded");
                    yield return Frames(3);
                    Skip();
                    yield return Frames(3);
                }
                if (level < 4) Assert.AreEqual(level + 1, GameState.Level, "next level did not load");
            }
            Assert.IsTrue(GameState.Data.finale, "finale not reached");
            Debug.Log($"[Play] finished! coins={GameState.Coins} percent={GameState.Percent}");
        }

        /// <summary>Tahap 5: Aiman walks to his bike, mounts, rides, dismounts; then a car wreck
        /// throws him out in an arc instead of teleporting him to the kerb.</summary>
        [UnityTest]
        public IEnumerator VehicleTransitions()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 5;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);
            var gm = GameManager.I;
            Skip();
            var p = gm.Player;
            Assert.AreEqual("aiman", p.def.id);
            var rig = p.GetComponentInChildren<CharacterRig>();
            Assert.IsTrue(rig && rig.Humanoid, "Aiman should use the humanoid Animator");
            var bike = gm.LastCar;
            Assert.IsTrue(bike && bike.TwoWheeler, "family vehicle should be the delivery bike");
            yield return Seconds(1f);

            // stand a few metres off and get on the proper way
            p.Teleport(bike.transform.position + bike.transform.right * 3.5f + Vector3.up * 0.3f, bike.transform.rotation);
            yield return Frames(3);
            p.EnterVehicle(bike);
            Assert.IsTrue(p.Transitioning && !p.Driving, "entering should animate, not teleport");
            yield return Seconds(0.45f);
            Shot("20_bike_mount");
            yield return Seconds(1.2f);
            Assert.IsTrue(p.Driving, "never finished mounting");
            Assert.IsTrue(rig.gameObject.activeInHierarchy, "rider should stay visible on the bike");
            var drv = new ScriptedDriver();
            bike.driver = drv;
            yield return Seconds(2f);
            Shot("21_bike_riding");
            drv.throttle = -1f;
            yield return Seconds(1.5f);
            drv.throttle = 0f;
            yield return Seconds(1f);
            p.ExitVehicle(false);
            Assert.IsTrue(p.Transitioning, "dismount should animate");
            yield return Seconds(0.3f);
            Shot("22_bike_dismount");
            yield return Seconds(0.8f);
            Assert.IsFalse(p.Transitioning || p.Driving, "dismount never finished");

            // a car, wrecked while driving: thrown clear, knocked flat, door flies off
            var car = gm.SummonCar("saga", p.transform.position + p.transform.forward * 6f, p.transform.rotation);
            yield return Seconds(0.5f);
            p.EnterVehicle(car, true);
            car.driver = new ScriptedDriver();
            yield return Seconds(1.5f);
            var inCar = car.transform.position;
            car.Damage(9999f);
            yield return Seconds(0.25f);
            Assert.IsFalse(p.Driving, "wreck should throw the player out");
            // Hit & Run cars tear their real hinged door off; legacy models fling a stand-in panel
            bool flung = GameObject.Find("FlungDoor") != null;
            foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                if (rb.name.StartsWith("Door_") && rb.transform.parent == null) flung = true;
            Assert.IsTrue(flung, "door should fly off");
            Shot("23_wreck_bail");
            yield return Seconds(1.6f);
            Assert.Greater(Vector3.Distance(p.transform.position, inCar), 1.5f, "player should land clear of the wreck");
            Assert.Greater(p.transform.position.y, -1f, "player fell through the world");
        }

        /// <summary>The family and townsfolk restyled as KL skinned characters: the player, the
        /// family NPCs and the pedestrians are Humanoid rigs; costumes repaint the palette.</summary>
        [UnityTest]
        public IEnumerator CastRestyled()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);
            var gm = GameManager.I;
            Skip();
            var p = gm.Player;
            var rig = p.GetComponentInChildren<CharacterRig>();
            Assert.IsTrue(rig && rig.Humanoid, "Pak Mat should be a KL humanoid");
            foreach (var key in new[] { "maksom", "along", "adik", "tokketua", "anneh", "auntypasar", "inspektor", "pakciktaksi" })
            {
                var n = gm.Missions.npcs[key];
                Assert.IsTrue(n.GetComponentInChildren<CharacterRig>().Humanoid, $"NPC {key} is not a KL humanoid");
            }
            int humanoidPeds = 0;
            foreach (var ped in Pedestrian.All)
                if (ped && ped.GetComponentInChildren<CharacterRig>() && ped.GetComponentInChildren<CharacterRig>().Humanoid) humanoidPeds++;
            Debug.Log($"[Cast] {humanoidPeds}/{Pedestrian.All.Count} pedestrians are KL humanoids");
            Assert.AreEqual(Pedestrian.All.Count, humanoidPeds, "some pedestrians still use legacy models");
            // palette-swap costume: the batik and sarong cells get repainted on this instance only
            var costume = GameData.Costumes.Find(c => c.id == "pakmat_raya");
            var smr = p.GetComponentInChildren<SkinnedMeshRenderer>();
            var before = smr.sharedMaterial;
            ModelFactory.Recolor(p.gameObject, costume.swaps);
            Assert.AreNotSame(before, smr.sharedMaterial, "costume did not swap the material");
            Assert.IsTrue(smr.sharedMaterial.GetTexture("_BaseMap").name.StartsWith("kl_palette~"), "costume did not repaint the palette");

            var cc = ChaseCamera.I;
            var home = gm.City.places["Home"];
            p.Teleport(home + new Vector3(3.5f, 0.2f, 1.5f), Quaternion.Euler(0, -90, 0));
            cc.yaw = -60f; cc.pitch = 10f; cc.distance = 9f;
            yield return Seconds(1.5f);
            Shot("40_family_home");
            cc.distance = 3.2f; cc.pitch = 6f; cc.yaw = p.transform.eulerAngles.y + 160f;
            yield return Seconds(0.8f);
            Shot("43_pakmat_closeup");
            // a street crowd in the city
            var shops = CityBuilder.BlockCenter(11, 2);
            p.Teleport(new Vector3(CityBuilder.RoadX(11) + 7.5f, 0.3f, shops.z - 4f), Quaternion.Euler(0, 20, 0));
            for (int i = 0; i < 8; i++)
                PedestrianSpawner.Spawn(p.transform.position + new Vector3(Random.Range(-3f, 3f), 0, Random.Range(3f, 12f)),
                    WalkZone.FromRect(new Rect(p.transform.position.x - 8, p.transform.position.z, 16, 16), 0f), gm.transform);
            cc.yaw = 20f; cc.pitch = 8f; cc.distance = 7f;
            yield return Seconds(1.5f);
            Shot("41_townsfolk_crowd");
            // the villain: frame him, not the player
            var datukAt = p.transform.position + p.transform.right * 4f;
            var datuk = NPC.Spawn("datuk_test", "Datuk Mega", "chr_datukmega", datukAt,
                Quaternion.LookRotation(-p.transform.forward), gm.transform);
            p.Teleport(datukAt - p.transform.right * 6f - p.transform.forward * 3f, p.transform.rotation);
            cc.target = datuk.transform; cc.targetBody = null;
            cc.yaw = datuk.transform.eulerAngles.y + 200f; cc.pitch = 6f; cc.distance = 4.2f;
            yield return Seconds(1f);
            Shot("42_datuk_mega");
            cc.target = p.transform;
            Assert.IsTrue(datuk.GetComponentInChildren<CharacterRig>().Humanoid);
        }

        /// <summary>Phone controls: the overlay shows in the world, hides for menus, and the
        /// virtual stick walks the player (and steers a car).</summary>
        [UnityTest]
        public IEnumerator TouchControlsWork()
        {
            TouchControls.ForceOn = true;
            try
            {
                GameState.Ephemeral = true;
                GameState.Data = new SaveData();
                GameManager.ForceLevel = 1;
                SceneManager.LoadScene("KampungRun");
                yield return Frames(30);
                var gm = GameManager.I;
                Skip();
                yield return Frames(3);
                Assert.IsTrue(TouchInput.Active, "touch overlay should be active");
                var jump = GameObject.Find("Btn_Jump");
                Assert.IsNotNull(jump, "jump button missing");
                Assert.IsTrue(jump.activeInHierarchy, "jump button hidden in the world");
                Assert.AreEqual(CursorLockMode.None, Cursor.lockState, "touch play must not lock the pointer");
                var p = gm.Player;
                var start = p.transform.position;
                TouchControls.DebugStick = new Vector2(0, 1);
                yield return Seconds(1.5f);
                Shot("60_touch_controls_walking");
                TouchControls.DebugStick = Vector2.zero;
                Assert.Greater(Vector3.Distance(start, p.transform.position), 2f, "virtual stick did not move the player");
                // driving: the car-only buttons swap in, the stick is the throttle
                p.EnterVehicle(gm.LastCar, true);
                yield return Frames(3);
                Assert.IsTrue(GameObject.Find("Btn_Horn") != null && GameObject.Find("Btn_Horn").activeInHierarchy, "horn button missing when driving");
                Assert.IsFalse(GameObject.Find("Btn_Punch") != null && GameObject.Find("Btn_Punch").activeInHierarchy, "punch button showing in a car");
                var carStart = gm.LastCar.transform.position;
                TouchControls.DebugStick = new Vector2(0, 1);
                yield return Seconds(2f);
                Shot("61_touch_controls_driving");
                TouchControls.DebugStick = Vector2.zero;
                Assert.Greater(Vector3.Distance(carStart, gm.LastCar.transform.position), 8f, "virtual stick did not drive the car");
            }
            finally { TouchControls.ForceOn = false; TouchControls.DebugStick = Vector2.zero; }
        }

        /// <summary>The Lake Gardens side of the map: KL Sentral, Muzium Negara, Perdana Botanical
        /// Gardens and Masjid Negara (env_kl_landmarks kit), plus a pedestrian mid-stride.</summary>
        [UnityTest]
        public IEnumerator LandmarksTour()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);
            var gm = GameManager.I;
            Skip();
            var p = gm.Player;
            var cc = ChaseCamera.I;
            foreach (var key in new[] { "KLSentral", "MuziumNegara", "TamanPerdana", "MasjidNegara" })
                Assert.IsTrue(gm.City.places.ContainsKey(key), $"{key} missing");
            IEnumerator Look(string name, Vector3 at, float yaw, float dist, float pitch)
            {
                p.Teleport(at, Quaternion.Euler(0, yaw, 0));
                cc.yaw = yaw; cc.pitch = pitch; cc.distance = dist;
                yield return Seconds(1.2f);
                Shot(name);
            }
            // each place sits at the kerb facing its landmark: look that way over the player's head
            float Facing(string key) => gm.City.facings.TryGetValue(key, out var f) ? f.eulerAngles.y : 0f;
            yield return Look("50_kl_sentral", gm.City.places["KLSentral"] + Vector3.up * 0.2f, Facing("KLSentral"), 30f, 12f);
            yield return Look("51_muzium_negara", gm.City.places["MuziumNegara"] + Vector3.up * 0.2f, Facing("MuziumNegara"), 22f, 12f);
            yield return Look("52_taman_perdana", gm.City.places["TamanPerdana"] + Vector3.up * 0.2f, Facing("TamanPerdana"), 22f, 20f);
            yield return Look("53_masjid_negara", gm.City.places["MasjidNegara"] + Vector3.up * 0.2f, Facing("MasjidNegara"), 34f, 12f);
            // a townsperson walking: the new soft-skinned, bouncy walk cycle up close
            var ped = PedestrianSpawner.Spawn(p.transform.position + p.transform.right * 2.5f,
                Box(p.transform.position, 30f), gm.transform, "chr_townman");
            yield return Seconds(1.5f);
            cc.target = ped.transform; cc.targetBody = null; cc.distance = 3.5f; cc.pitch = 4f; cc.yaw = ped.transform.eulerAngles.y + 90f;
            cc.SnapBehind();
            yield return Seconds(0.8f);
            cc.yaw = ped.transform.eulerAngles.y + 90f;
            yield return Frames(4);
            Shot("54_walk_cycle");
            cc.target = p.transform;
        }

        /// <summary>P1 handoff content in the game: drive the KL taxi and food truck (wheels,
        /// suspension, collision proxy), then tour Kampung Baru, Brickfields + the LRT and
        /// Chow Kit for screenshots.</summary>
        [UnityTest]
        public IEnumerator P1DistrictsAndVehicles()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 5;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);
            var gm = GameManager.I;
            Skip();
            var p = gm.Player;
            Assert.IsTrue(gm.City.places.ContainsKey("Brickfields"), "Brickfields block missing");
            Assert.IsTrue(gm.City.places.ContainsKey("LRT"), "LRT missing");
            var cc = ChaseCamera.I;
            foreach (var id in new[] { "teksi", "foodtruck" })
            {
                var start = South(12, 1, 2.5f, 8f);
                var v = gm.SummonCar(id, start, Quaternion.identity);
                yield return Seconds(0.6f);
                p.EnterVehicle(v, true);
                var drv = new ScriptedDriver();
                v.driver = drv;
                var from = v.transform.position;
                yield return Seconds(3f);
                Shot($"30_drive_{id}");
                float moved = Vector3.Distance(from, v.transform.position);
                Debug.Log($"[P1] {id} moved {moved:F1} m, {v.SpeedKmh:F0} km/h, grounded={v.grounded}, y={v.transform.position.y:F2}");
                Assert.Greater(moved, 12f, $"{id} did not drive");
                Assert.IsTrue(v.grounded, $"{id} suspension not touching the road");
                Assert.Greater(Vector3.Dot(v.transform.up, Vector3.up), 0.9f, $"{id} not upright");
                p.ExitVehicle(false, true);
                yield return Frames(3);
            }
            // tour: stand the player somewhere and frame each district
            IEnumerator Look(string name, Vector3 at, float yaw, float dist, float pitch)
            {
                p.Teleport(at, Quaternion.Euler(0, yaw, 0));
                cc.yaw = yaw; cc.pitch = pitch; cc.distance = dist;
                yield return Seconds(1.2f);
                Shot(name);
            }
            var kb = CityBuilder.BlockCenter(CityBuilder.Layout.kampung.c0, CityBuilder.Layout.kampung.r0 + 3);
            yield return Look("31_kampung_baru", kb + new Vector3(0, 0.3f, 27f), 150f, 16f, 14f);
            var bf = gm.City.places["Brickfields"];
            yield return Look("32_brickfields", bf + Vector3.up * 0.2f, gm.City.facings["Brickfields"].eulerAngles.y, 15f, 9f);
            var lrt = gm.City.places["LRT"];
            yield return Look("33_lrt_river", new Vector3(lrt.x + 40f, 0.3f, CityBuilder.RoadZ(16)), -100f, 22f, 8f);
            yield return Look("34_chowkit", gm.City.places["ChowKit"] + new Vector3(-10f, 0.1f, 0), 110f, 14f, 12f);
            var home = gm.City.places["Home"];
            yield return Look("35_home_kb_house", home + new Vector3(2f, 0.2f, 4f), -120f, 12f, 12f);
            // Mei and Ravi (skinned KL NPCs) at their Chow Kit spots
            var mei = gm.Missions.npcs["mei"].transform;
            var ravi = gm.Missions.npcs["ravi"].transform;
            Assert.IsTrue(mei.GetComponentInChildren<CharacterRig>().Humanoid, "Mei should be a humanoid KL character");
            Assert.IsTrue(ravi.GetComponentInChildren<CharacterRig>().Humanoid, "Ravi should be a humanoid KL character");
            yield return Look("37_mei_chowkit", mei.position + mei.forward * 2.6f + mei.right * 0.8f, mei.eulerAngles.y + 200f, 4.5f, 8f);
            yield return Look("38_ravi_chowkit", ravi.position + ravi.forward * 2.6f + ravi.right * 0.8f, ravi.eulerAngles.y + 200f, 4.5f, 8f);

            // look check (handoff P0 #6): Aiman on the delivery bike down the wet Chow Kit street,
            // market on the kerb, a taxi ahead - compare with references/approved/gameplay-concept.png
            var ck = gm.City.places["ChowKit"];
            var bikeAt = new Vector3(ck.x - 24f, 0.5f, ck.z + 4.6f);         // eastbound lane (drive on the left)
            var bike = gm.SummonCar("bike", bikeAt, Quaternion.Euler(0, 90, 0));
            var taxi = VehicleSpawner.Spawn("teksi", bikeAt + new Vector3(15f, 0.2f, -0.6f), Quaternion.Euler(0, 90, 0), VehicleRole.Parked);
            yield return Seconds(0.6f);
            p.EnterVehicle(bike, true);
            var slow = new ScriptedDriver { throttle = 0.35f };
            bike.driver = slow;
            cc.distance = 5.0f; cc.pitch = 6f; cc.yaw = 80f;
            yield return Seconds(1.6f);
            slow.throttle = 0f;
            yield return Seconds(0.4f);
            Shot("36_look_check_chowkit");
            Object.Destroy(taxi.gameObject);
        }

        class TurnDriver : IDriver
        {
            public float throttle = 1f, steer = 0.8f;
            public bool handbrake;
            public void Drive(Vehicle v, out float t, out float s, out bool hb) { t = throttle; s = steer; hb = handbrake; }
        }

        /// <summary>The Hit & Run-style Malaysian cars: a showroom line-up, then each one driven hard
        /// through a turn and a handbrake slide - body roll, visible driver, doors, lamps, smoke.</summary>
        [UnityTest]
        public IEnumerator HitAndRunCars()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 2;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);
            var gm = GameManager.I;
            Skip();
            var p = gm.Player;
            var cc = ChaseCamera.I;
            foreach (var t in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) Object.Destroy(t.gameObject);
            var ids = new[] { "myvi", "saga", "kancil", "van", "hilux", "kapcai", "teksi", "polis" };
            // showroom: parked in a row along an avenue
            var row = South(12, 2, 2.5f, 12f);
            var parked = new System.Collections.Generic.List<Vehicle>();
            for (int i = 0; i < ids.Length; i++)
            {
                var v = VehicleSpawner.Spawn(ids[i], row + new Vector3(0, 0, i * 6.2f), Quaternion.Euler(0, 0, 0), VehicleRole.Parked);
                Assert.IsNotNull(v.Visuals, $"{ids[i]} should have the Hit & Run visuals (Body + seat)");
                parked.Add(v);
            }
            yield return Seconds(1.5f);
            p.Teleport(row + new Vector3(-9f, 0, 10f), Quaternion.Euler(0, 90, 0));
            cc.target = p.transform; cc.targetBody = null;
            cc.yaw = 60f; cc.pitch = 16f; cc.distance = 16f;
            yield return Seconds(0.8f);
            Shot("70_car_lineup");
            cc.yaw = 120f; cc.distance = 12f;
            yield return Seconds(0.8f);
            Shot("71_car_lineup_rear");
            // get into the Myvi the normal way: walk up, door swings open, sit at the wheel
            p.Teleport(parked[0].transform.position + parked[0].transform.right * 3f, Quaternion.identity);
            yield return Frames(3);
            p.EnterVehicle(parked[0]);
            yield return Seconds(0.45f);
            cc.yaw = 250f; cc.distance = 6f; cc.pitch = 10f;
            yield return Seconds(0.2f);
            Shot("72_myvi_door_open");
            for (float t = 0; t < 4f && !(p.Driving && !p.Transitioning); t += Time.deltaTime) yield return null;
            Assert.IsTrue(p.Driving, "player should be in the Myvi");
            Shot("73_myvi_driver_seated");
            p.ExitVehicle(false, true);
            foreach (var v in parked) Object.Destroy(v.gameObject);
            yield return Frames(3);

            foreach (var id in ids)
            {
                var start = South(12, 1, 2.5f, 8f);
                var v = gm.SummonCar(id, start, Quaternion.identity);
                yield return Seconds(0.5f);
                p.EnterVehicle(v, true);
                var drv = new TurnDriver { steer = 0f };
                v.driver = drv;
                yield return Seconds(1.6f);
                // driving pose: feet rest on the floor pan, never poke out underneath. Measured in the
                // body's own frame (the seat rides the sprung body), so a crash nose-dive doesn't count.
                // The seat marker sits ~0.27 m above the cabin floor.
                var anim = p.GetComponentInChildren<Animator>();
                var seat = FindDeep(v.transform, "Seat_Driver");
                if (anim && anim.isHuman && seat && !v.TwoWheeler)
                    foreach (var foot in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                    {
                        // the seat's axes carry the importer's Z-up correction: use whichever one points up
                        var bodyUp = seat.up;
                        foreach (var ax in new[] { seat.forward, -seat.forward, seat.right, -seat.right, -seat.up })
                            if (Vector3.Dot(ax, v.transform.up) > Vector3.Dot(bodyUp, v.transform.up)) bodyUp = ax;
                        float above = Vector3.Dot(anim.GetBoneTransform(foot).position - seat.position, bodyUp) + 0.27f;
                        Debug.Log($"[HR] {id}: {foot} {above:F2} m above the cabin floor, state={StateName(anim)}");
                        Assert.Greater(above, -0.02f, $"{id}: {foot} through the floor");
                    }
                drv.steer = 0.9f;                      // hard corner: body roll
                yield return Seconds(0.45f);
                Shot($"74_roll_{id}");
                drv.handbrake = true;                  // slide: smoke + skids
                yield return Seconds(0.5f);
                Shot($"75_slide_{id}");
                drv.handbrake = false; drv.steer = 0f; drv.throttle = -1f;   // brake: nose dive + brake lamps
                yield return Seconds(0.35f);
                Shot($"76_brake_{id}");
                Debug.Log($"[HR] {id}: {v.SpeedKmh:F0} km/h grounded={v.grounded} up={Vector3.Dot(v.transform.up, Vector3.up):F2}");
                Assert.Greater(Vector3.Dot(v.transform.up, Vector3.up), 0.6f, $"{id} flipped over");
                p.ExitVehicle(false, true);
                Object.Destroy(v.gameObject);
                yield return Frames(3);
            }
        }

        /// <summary>The life-size city: an aerial view and street-level views of each district, and a
        /// budget check on how much the map spawns.</summary>
        [UnityTest]
        public IEnumerator CityOverview()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);
            var gm = GameManager.I;
            Skip();
            var p = gm.Player;
            var cc = ChaseCamera.I;
            int renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;
            int colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length;
            Debug.Log($"[City] {CityBuilder.NX}x{CityBuilder.NZ} blocks of {CityBuilder.Block} m, roads {CityBuilder.Road} m, " +
                      $"map {gm.City.bounds.size.x:F0}x{gm.City.bounds.size.z:F0} m; renderers={renderers} colliders={colliders} places={gm.City.places.Count}");
            // what the renderers are (a browser culls every one of them each frame)
            var byOwner = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var t = r.transform;
                string owner = t.root.name;
                for (var q = t; q.parent != null; q = q.parent) if (q.parent.parent == null || q.parent.name == "Props" || q.parent.name == "StreetKit") { owner = q.parent.name + "/" + (q.parent.name == "Props" || q.parent.name == "StreetKit" ? q.name.Split(' ')[0] : q.name); break; }
                byOwner.TryGetValue(owner, out int c);
                byOwner[owner] = c + 1;
            }
            var owners = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>(byOwner);
            owners.Sort((a, b) => b.Value.CompareTo(a.Value));
            var top = new System.Text.StringBuilder();
            for (int i = 0; i < Mathf.Min(25, owners.Count); i++) top.Append($" {owners[i].Key}={owners[i].Value}");
            Debug.Log("[City] renderers by owner:" + top);
            // every named place is on the map, and not buried in a building
            foreach (var kv in gm.City.places)
                Assert.IsTrue(gm.City.bounds.Contains(new Vector3(kv.Value.x, 0, kv.Value.z)), $"{kv.Key} is off the map");

            // aerial: the whole city from the south-west, high up
            cc.enabled = false;
            RenderSettings.fog = false;                             // the whole city, not the haze
            var cam = Camera.main.transform;
            float clip = Camera.main.farClipPlane;
            Camera.main.farClipPlane = 6000f;
            cam.position = new Vector3(-1500f, 1100f, -1900f);
            cam.LookAt(new Vector3(0f, 0f, 0f));
            yield return Frames(3);
            Shot("95_city_aerial");
            cam.position = new Vector3(0f, 2250f, 0f);
            cam.rotation = Quaternion.Euler(90f, 0f, 0f);
            yield return Frames(3);
            Shot("95_city_top");
            // the districts from above, to compare with the satellite view (Tools/klmap)
            foreach (var (name, x, z, h) in new[] { ("kotalama", 58f, -58f, 900f), ("east", 700f, 520f, 1000f), ("west", -600f, -350f, 1000f), ("north", -300f, 820f, 900f) })
            {
                cam.position = new Vector3(x, h, z);
                cam.rotation = Quaternion.Euler(90f, 0f, 0f);
                yield return Frames(3);
                Shot($"95_top_{name}");
            }
            // every real-KL landmark from the air, framed on its model
            var landmarks = new System.Collections.Generic.List<Transform>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("Landmark_")) landmarks.Add(t);
            foreach (var t in landmarks)
            {
                if (!t) continue;
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                cam.position = b.center + new Vector3(-0.55f, 0.5f, -0.7f).normalized * Mathf.Max(60f, size * 1.4f);
                cam.LookAt(b.center);
                yield return Frames(3);
                Debug.Log($"[City] {t.name}: {b.size.x:F0} x {b.size.y:F0} x {b.size.z:F0} m at {b.center}");
                Shot("97_" + t.name.Substring("Landmark_".Length).ToLower());
            }
            // each real-KL district straight down, no HUD (Tools/klmap/compare.py puts the satellite view beside it)
            var camera = Camera.main;
            foreach (var lp in CityBuilder.Layout.patches)
            {
                var rect = Rect.MinMaxRect(CityBuilder.RoadX(lp.c0) + CityBuilder.Road * 0.5f, CityBuilder.RoadZ(lp.r0) + CityBuilder.Road * 0.5f,
                                           CityBuilder.RoadX(lp.c1 + 1) - CityBuilder.Road * 0.5f, CityBuilder.RoadZ(lp.r1 + 1) - CityBuilder.Road * 0.5f);
                bool ortho = camera.orthographic;
                float size = camera.orthographicSize;
                camera.orthographic = true;
                camera.orthographicSize = rect.height * 0.5f;
                cam.position = new Vector3(rect.center.x, 1200f, rect.center.y);
                cam.rotation = Quaternion.Euler(90f, 0f, 0f);
                int w = 1024, h = Mathf.RoundToInt(1024 * rect.height / rect.width);
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                foreach (var cv in canvases) cv.enabled = false;
                camera.aspect = w / (float)h;
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                yield return Frames(2);
                if (RenderPipeline.SupportsRenderRequest(camera, req)) RenderPipeline.SubmitRenderRequest(camera, req);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(ShotDir, $"95_ortho_{lp.name}.png"), tex.EncodeToPNG());
                Object.Destroy(tex);
                rt.Release();
                foreach (var cv in canvases) cv.enabled = true;
                camera.ResetAspect();
                camera.orthographic = ortho;
                camera.orthographicSize = size;
            }
            // the filler districts' flyovers and roundabouts
            foreach (var kv in gm.City.places)
            {
                if (!kv.Key.StartsWith("Flyover") && !kv.Key.StartsWith("Bulatan")) continue;
                bool fly = kv.Key.StartsWith("Flyover");
                cam.position = kv.Value + (fly ? new Vector3(-70f, 55f, -90f) : new Vector3(-38f, 42f, -48f));
                cam.LookAt(kv.Value);
                yield return Frames(3);
                Shot("98_" + kv.Key.ToLower());
            }
            Camera.main.farClipPlane = clip;
            RenderSettings.fog = true;
            cc.enabled = true;

            var views = new (string name, string place, float yaw, float pitch, float dist)[]
            {
                ("street_bukitbintang", "BukitBintang", 60f, 10f, 9f),
                ("street_chowkit", "ChowKit", 200f, 10f, 9f),
                ("kampung_home", "PlayerSpawn", 250f, 12f, 9f),
                ("landmark_towers", "Towers", 0f, 5f, 12f),
                ("landmark_merdeka118", "Merdeka118", 180f, 4f, 12f),
                ("landmark_batu", "BatuCaves", 180f, 6f, 12f),
                ("landmark_stadium", "StadiumMerdeka", 180f, 12f, 12f),
                ("landmark_masjidnegara", "MasjidNegara", 180f, 6f, 12f),
                ("dataran", "Dataran", 250f, 8f, 10f),
            };
            foreach (var (name, place, yaw, pitch, dist) in views)
            {
                if (!gm.City.places.TryGetValue(place, out var at)) { Debug.LogWarning($"[City] no place {place}"); continue; }
                // the real-KL places sit at the kerb facing their landmark: look that way
                float look = CityBuilder.InsidePatch(at) && gm.City.facings.TryGetValue(place, out var f) ? f.eulerAngles.y : yaw;
                p.Teleport(at + Vector3.up * 0.3f, Quaternion.Euler(0, look + 180f, 0));
                cc.target = p.transform; cc.targetBody = null; cc.yaw = look; cc.pitch = pitch; cc.distance = dist;
                yield return Seconds(1.2f);
                Shot($"96_{name}");
            }
            // the river promenade from a bridge over the Gombak
            var bridge = Vector3.zero;
            foreach (var kv in gm.City.places) if (kv.Key.StartsWith("Bridge")) { bridge = kv.Value; break; }
            p.Teleport(bridge + new Vector3(0, 0.3f, 0), Quaternion.identity);
            cc.yaw = 0f; cc.pitch = 12f; cc.distance = 10f;
            yield return Seconds(1.2f);
            Shot("96_river_promenade");
        }

        /// <summary>Every place a mission sends you to can be driven to from every other one, both ways
        /// (one-way streets, roundabouts, flyovers and the joins between the real-KL streets and the grid).</summary>
        [UnityTest]
        public IEnumerator MissionPlacesConnect()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(20);
            var gm = GameManager.I;
            Skip();
            var roads = gm.City.roads;
            var keys = new[] { "HomeYard", "Surau", "Padang", "Mamak", "ChowKit", "Masjid", "Dataran", "PasarSeni", "Pasar", "Merdeka118",
                               "Towers", "KLTower", "BukitBintang", "KLSentral", "MasjidNegara", "TuguNegara", "Brickfields" };
            var missing = new System.Collections.Generic.List<string>();
            float longest = 0f;
            string longestPair = "";
            foreach (var a in keys)
                foreach (var b in keys)
                {
                    if (a == b) continue;
                    Assert.IsTrue(gm.City.places.ContainsKey(a), $"no place {a}");
                    var path = roads.Path(roads.Nearest(gm.City.places[a]), roads.Nearest(gm.City.places[b]));
                    if (path == null) { missing.Add($"{a}->{b}"); continue; }
                    float len = 0f;
                    for (int i = 1; i < path.Count; i++) len += Vector3.Distance(path[i - 1].pos, path[i].pos);
                    if (len > longest) { longest = len; longestPair = $"{a}->{b}"; }
                }
            Debug.Log($"[Routes] {keys.Length * (keys.Length - 1) - missing.Count} of {keys.Length * (keys.Length - 1)} drives connect; longest {longestPair} {longest:F0} m" +
                      (missing.Count > 0 ? "; missing " + string.Join(", ", missing) : ""));
            Assert.IsEmpty(missing, "places you can't drive between: " + string.Join(", ", missing));
        }

        /// <summary>Downtown KL is busy (the crowd follows the player around the city blocks) while the
        /// kampung keeps its quieter crowd. Runs with the browser build's numbers.</summary>
        [UnityTest]
        public IEnumerator CityCrowd()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);
            var gm = GameManager.I;
            Skip();
            gm.pedestrianCount = 80; gm.kampungDensity = 0.0045f; gm.cityDensity = 0.05f; gm.crowdCap = 170;     // WebGL desktop settings
            var p = gm.Player;
            var cc = ChaseCamera.I;
            int Near(Vector3 at)
            {
                int n = 0;
                foreach (var ped in Pedestrian.All) if (ped && Vector3.Distance(ped.transform.position, at) < 80f) n++;
                return n;
            }
            var spots = new[]
            {
                ("dataran", gm.City.places["Dataran"] + Vector3.up * 0.2f, true),
                ("petaling", gm.City.places["Pasar"] + Vector3.up * 0.2f, true),
                ("chowkit", gm.City.places["ChowKit"] + Vector3.up * 0.2f, true),
                ("kampung", new Vector3(CityBuilder.RoadX(9), 0.3f, CityBuilder.RoadZ(17)), false),
            };
            int city = 99, kampung = 0;
            foreach (var (name, at, isCity) in spots)
            {
                p.Teleport(at, Quaternion.identity);
                cc.target = p.transform; cc.targetBody = null; cc.yaw = 30f; cc.pitch = 14f; cc.distance = 9f;
                yield return Seconds(10f);
                int n = Near(at);
                Debug.Log($"[Crowd] {name}: {n} people within 80 m ({Pedestrian.All.Count} alive)");
                Shot($"90_crowd_{name}");
                if (isCity) city = Mathf.Min(city, n); else kampung = n;
            }
            Assert.Greater(city, 25, "downtown KL should be busy");
            Assert.Greater(kampung, 0, "the kampung keeps its own people");
            Assert.Less(kampung, city / 2, "the kampung stays quieter than downtown");
        }

        /// <summary>Every playable character fits under every car's roof: the top of the head (from the
        /// skinned mesh itself) stays below the roof skin straight above it.</summary>
        [UnityTest]
        public IEnumerator DriversFitUnderRoof()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);
            var gm = GameManager.I;
            Skip();
            var p = gm.Player;
            // no traffic and no crowd: a car ramming the test car, or a pedestrian caught under it, throws the reading
            gm.trafficCount = 0;
            gm.cityDensity = 0f;
            foreach (var t in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) Object.Destroy(t.gameObject);
            var worst = "";
            float worstGap = 9f;
            foreach (var cid in GameData.Characters.Keys)
            {
                p.SetCharacter(cid);
                yield return Frames(2);
                foreach (var id in new[] { "myvi", "saga", "kancil", "van", "hilux", "teksi", "polis" })
                {
                    var start = South(12, 1, 2.5f, 8f);
                    foreach (var ped in new System.Collections.Generic.List<Pedestrian>(Pedestrian.All))
                        if (ped && Vector3.Distance(ped.transform.position, start) < 15f) Object.Destroy(ped.gameObject);
                    p.Teleport(start + Vector3.right * 9f, Quaternion.identity);   // stand clear of where the car lands
                    yield return Frames(2);
                    var v = gm.SummonCar(id, start, Quaternion.identity);
                    yield return Seconds(0.4f);
                    p.EnterVehicle(v, true);
                    v.driver = new TurnDriver { throttle = 0f, steer = 0f };
                    Assert.IsTrue(p.Driving, $"{cid} never got into the {id}");
                    yield return Seconds(0.8f);
                    var up = v.transform.up;
                    // the crown of the head: every skinned vertex within 15 cm of the highest one
                    var verts = new System.Collections.Generic.List<Vector3>();
                    float best = -99f;
                    var baked = new Mesh();
                    foreach (var smr in p.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        if (!smr.enabled || !smr.gameObject.activeInHierarchy || smr.name.Contains("LOD1")) continue;
                        smr.BakeMesh(baked, true);
                        var m = smr.transform.localToWorldMatrix;
                        foreach (var vert in baked.vertices)
                        {
                            var w = m.MultiplyPoint3x4(vert);
                            verts.Add(w);
                            best = Mathf.Max(best, Vector3.Dot(w, up));
                        }
                    }
                    Object.Destroy(baked);
                    var headBone = p.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Head).position;
                    var crown = verts.FindAll(w => Vector3.ProjectOnPlane(w - headBone, up).magnitude < 0.08f && Vector3.Dot(w - headBone, up) > 0f);
                    int step = Mathf.Max(1, crown.Count / 80);
                    float gap = 9f; Vector3 at = Vector3.zero;
                    for (int k = 0; k < crown.Count; k += step)
                    {
                        float g = RoofGap(v, crown[k], up, Vector3.Dot(headBone, up));
                        if (g < gap) { gap = g; at = crown[k]; }
                    }
                    Debug.Log($"[ROOF] {cid} in {id}: {gap * 100f:F0} cm headroom at={v.transform.InverseTransformPoint(at)} state={StateName(p.GetComponentInChildren<Animator>())}");
                    // where the body is, in the car's frame
                    var an = p.GetComponentInChildren<Animator>();
                    if (an && an.isHuman)
                    {
                        string Bone(HumanBodyBones hb) { var t = an.GetBoneTransform(hb); return t ? v.transform.InverseTransformPoint(t.position).ToString("F2") : "-"; }
                        Debug.Log($"[ROOF]   {cid} bones: hips={Bone(HumanBodyBones.Hips)} head={Bone(HumanBodyBones.Head)} lfoot={Bone(HumanBodyBones.LeftFoot)} " +
                                  $"model at {v.transform.InverseTransformPoint(an.transform.position):F2} scale={an.transform.lossyScale.y:F2}");
                    }
                    // close-up of the roofline from the driver's side, level with the head
                    var cc = ChaseCamera.I;
                    cc.enabled = false;
                    var head = p.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Head).position;
                    var cam = Camera.main.transform;
                    cam.position = head + v.transform.right * 2.6f + up * 0.35f - v.transform.forward * 0.4f;
                    cam.LookAt(head + up * 0.1f);
                    Shot($"roof_{id}_{cid}");
                    cc.enabled = true;
                    if (gap < worstGap) { worstGap = gap; worst = $"{cid} in {id}"; }
                    p.ExitVehicle(false, true);
                    Object.Destroy(v.gameObject);
                    yield return Frames(3);
                }
            }
            Debug.Log($"[ROOF] tightest: {worst} {worstGap * 100f:F0} cm");
            Assert.Greater(worstGap, 0.03f, $"{worst}: head pokes through the roof");
            Assert.Less(worstGap, 3f, $"{worst}: no roof found above the head");
        }

        /// <summary>H&amp;R bopping: punch the same townsperson a dozen times - they stagger and fall
        /// close by, get back up, and never go flying or vanish.</summary>
        [UnityTest]
        public IEnumerator PunchPedestrianForever()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 2;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);
            var gm = GameManager.I;
            Skip();
            var p = gm.Player;
            var cc = ChaseCamera.I;
            var at = CityBuilder.BlockCenter(11, 2) + new Vector3(-CityBuilder.Block * 0.5f + 1.8f, 0.2f, 0);   // the west pavement
            var ped = PedestrianSpawner.Spawn(at, Box(at, 20f), gm.transform, "chr_townman");
            ped.enabled = true;
            yield return Frames(3);
            var start = ped.transform.position;
            float maxDist = 0f;
            for (int i = 0; i < 12; i++)
            {
                var pp = ped.transform.position;
                p.Teleport(pp - Vector3.forward * 1.1f, Quaternion.LookRotation(Vector3.forward));
                yield return Frames(2);
                p.DebugPunch(1);
                yield return Seconds(0.35f);
                maxDist = Mathf.Max(maxDist, Vector3.Distance(start, ped.transform.position));
                if (i == 3) { cc.target = p.transform; cc.yaw = 60f; cc.distance = 5f; cc.pitch = 12f; Shot("80_bop_stagger"); }
                if (i == 8) Shot("81_bop_down");
            }
            Assert.IsTrue(ped != null && ped.gameObject.activeInHierarchy, "pedestrian should survive endless bopping");
            Debug.Log($"[Bop] furthest from start after 12 punches: {maxDist:F1} m");
            Assert.Less(maxDist, 14f, "punches shouldn't send people flying");
        }

        /// <summary>The two new east columns: tour the new landmarks for screenshots.</summary>
        [UnityTest]
        public IEnumerator EastSideLandmarks()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 2;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);
            var gm = GameManager.I;
            Skip();
            var p = gm.Player;
            var cc = ChaseCamera.I;
            Assert.AreEqual(18, CityBuilder.NX);
            foreach (var place in new[] { "BatuCaves", "TuguNegara", "Pavilion", "Merdeka118", "StadiumMerdeka", "TheanHou", "IstanaNegara", "Masjid" })
            {
                Assert.IsTrue(gm.City.places.ContainsKey(place), $"{place} missing");
                var at = gm.City.places[place];
                // the real-KL places sit at the kerb facing their landmark; the outlying ones on the road north of it
                bool real = CityBuilder.InsidePatch(at);
                float yaw = real ? gm.City.facings[place].eulerAngles.y : 180f;
                p.Teleport(at + (real ? Vector3.up * 0.2f : new Vector3(0, 0.2f, 2f)), Quaternion.Euler(0, yaw + 180f, 0));
                cc.target = p.transform; cc.targetBody = null;
                cc.yaw = yaw; cc.pitch = place == "Merdeka118" ? 22f : 14f;
                cc.distance = place == "Merdeka118" ? 40f : 24f;
                yield return Seconds(1f);
                Shot($"9_{place}");
            }
        }

        /// <summary>Drop a car on every road segment and bridge; it must settle on top of the
        /// visible road surface (y ~ 0), never half-sunk into it.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator CarsRideOnRoadSurface()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);
            var gm = GameManager.I;
            Skip();
            var spots = new System.Collections.Generic.List<(Vector3 p, float yaw)>();
            for (int i = 0; i <= CityBuilder.NX; i++)
                for (int k = 0; k <= CityBuilder.NZ; k++)
                {
                    // grid roads only run between cells that aren't the same real-KL district
                    // (flyover spans are checked on their decks below, roundabouts on their ring)
                    bool ns = k < CityBuilder.NZ && CityBuilder.NSRoad(i, k) && !CityBuilder.OnFlyover(true, i, k);
                    bool ew = i < CityBuilder.NX && CityBuilder.EWRoad(k, i) && !CityBuilder.OnFlyover(false, i, k);
                    bool junction = ns || ew || (k > 0 && CityBuilder.NSRoad(i, k - 1)) || (i > 0 && CityBuilder.EWRoad(k, i - 1));
                    if (!junction) continue;
                    var n = new Vector3(CityBuilder.RoadX(i), 0, CityBuilder.RoadZ(k));
                    if (CityBuilder.IsRoundabout(i, k)) spots.Add((n + new Vector3(17.5f, 0.22f, 0f), 0f));   // on the ring (its deck is 0.22 up)
                    else spots.Add((n, 0f));                                                                // intersection
                    foreach (float d in new[] { 27f, 63f })
                    {
                        if (ew) spots.Add((n + new Vector3(d, 0, 2.5f), 90f));   // E-W segments (bridges too)
                        if (ns) spots.Add((n + new Vector3(2.5f, 0, d), 0f));   // N-S segments
                    }
                }
            int gridSpots = spots.Count;
            // the real-KL streets: a spread of their graph nodes (flyover decks and ramps too), the car
            // lined up with the street. Their surface is at the node's height.
            var picked = new System.Collections.Generic.List<Vector3>();
            foreach (var sp in spots) picked.Add(sp.p);          // (junctions on a patch's edge count as its nodes too)
            foreach (var node in gm.City.roads.nodes)
            {
                if (node.links.Count == 0 || (!CityBuilder.InsidePatch(node.pos) && node.pos.y < 0.5f)) continue;
                bool clash = false;
                foreach (var q in picked) if ((q - node.pos).sqrMagnitude < 40f * 40f) { clash = true; break; }
                if (clash) continue;
                picked.Add(node.pos);
                var dir = node.links[0].pos - node.pos; dir.y = 0;
                spots.Add((node.pos, dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir).eulerAngles.y : 0f));
            }
            Debug.Log($"[Sink] {gridSpots} grid spots, {spots.Count - gridSpots} real-KL street spots");
            // clear traffic so no test car lands on a passing one
            foreach (var t in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) Object.Destroy(t.gameObject);
            gm.enabled = false;
            yield return Frames(2);
            var bad = new System.Collections.Generic.List<string>();
            var rides = new System.Collections.Generic.List<(float ride, Vector3 at)>();
            int shots = 0;
            // in batches: a thousand parked Sagas at once is a physics test of its own
            for (int b0 = 0; b0 < spots.Count; b0 += 300)
            {
                var cars = new System.Collections.Generic.List<(Vehicle v, Vector3 at)>();
                for (int j = b0; j < Mathf.Min(spots.Count, b0 + 300); j++)
                {
                    var s = spots[j];
                    var v = VehicleSpawner.Spawn("saga", s.p + Vector3.up * 1.2f, Quaternion.Euler(0, s.yaw, 0), VehicleRole.Parked);
                    v.driver = null;
                    cars.Add((v, s.p));
                }
                yield return Seconds(2.5f);
                foreach (var (v, at) in cars)
                {
                    // ride height over the surface right under where it came to rest
                    var cp = v.transform.position;
                    float ground = at.y;
                    if (Physics.Raycast(cp + Vector3.up * 3f, Vector3.down, out var under, 12f, ~(1 << Layers.Vehicle), QueryTriggerInteraction.Ignore))
                        ground = under.point.y;
                    float ride = cp.y - ground;
                    rides.Add((ride, at));
                    if (Mathf.Abs(ride) > 0.08f && shots < 6)
                    {
                        // look at what it is perched on
                        Debug.Log($"[Sink] off: {at} car at {v.transform.position} up={v.transform.up} moved {Vector3.Distance(new Vector3(at.x, 0, at.z), new Vector3(v.transform.position.x, 0, v.transform.position.z)):F1} m");
                        var cam = Camera.main.transform;
                        ChaseCamera.I.enabled = false;
                        cam.position = v.transform.position + new Vector3(-7f, 4f, -7f);
                        cam.LookAt(v.transform.position);
                        Shot($"99_sink_{shots++}");
                        ChaseCamera.I.enabled = true;
                    }
                }
                foreach (var (v, at) in cars) Object.DestroyImmediate(v.gameObject);
                yield return null;
            }
            // every car should settle at the same ride height above the street as on an ordinary road tile
            var ys = rides.ConvertAll(c => c.ride);
            ys.Sort();
            float median = ys[ys.Count / 2], worst = ys[0];
            for (int ri = 0; ri < rides.Count; ri++)
            {
                var (ride, at) = rides[ri];
                // grid roads are flat; a real-KL street can leave a car a little tilted on a kerb or ramp foot
                if (Mathf.Abs(ride - median) > (ri < gridSpots ? 0.08f : 0.15f))
                {
                    string what = "";
                    foreach (var h in Physics.OverlapBox(at + Vector3.up * 1f, new Vector3(1.2f, 1f, 2.6f), Quaternion.identity, ~0,
                                 QueryTriggerInteraction.Ignore))
                        what += $" [{h.transform.root.name}/{h.transform.parent?.name}/{h.name} {h.GetType().Name} top={h.bounds.max.y:F2}]";
                    // and what the car came to rest on
                    if (Physics.Raycast(at + Vector3.up * (ride + 3f), Vector3.down, out var rest, ride + 6f, ~(1 << Layers.Vehicle), QueryTriggerInteraction.Ignore))
                        what += $" resting on {rest.collider.transform.parent?.name}/{rest.collider.name} at y={rest.point.y:F2}";
                    bad.Add($"{at} ride={ride:F2}{what}");
                }
            }
            // and at speed: flat out along the whole E-W avenue north of the old town (over both
            // rivers). Ride height must hold at top speed - downforce used to bottom the suspension out.
            foreach (var id in new[] { "saga", "basmini", "bike" })
            {
                int avenue = CityBuilder.Layout.north_row;
                var start = new Vector3(CityBuilder.RoadX(0) + 8f, 0.6f, CityBuilder.RoadZ(avenue) + 2.5f);
                var car = VehicleSpawner.Spawn(id, start, Quaternion.Euler(0, 90, 0), VehicleRole.Player);
                car.driver = new ScriptedDriver();
                yield return Seconds(1f);                                  // settle from the drop
                float restY = car.transform.position.y, minY = 9f, minX = 0f, topSpeed = 0f;
                float endX = CityBuilder.RoadX(CityBuilder.NX) - 8f, t0 = Time.time;
                while (car.transform.position.x < endX && Time.time - t0 < 60f * CityBuilder.WorldScale)
                {
                    var cp = car.transform.position;
                    if (cp.y < minY) { minY = cp.y; minX = cp.x; }
                    topSpeed = Mathf.Max(topSpeed, car.SpeedKmh);
                    yield return null;
                }
                Debug.Log($"[Sink] {id} flat out: rest y={restY:F2}, min y={minY:F2} at x={minX:F0}, top {topSpeed:F0} km/h, reached x={car.transform.position.x:F0}/{endX:F0}");
                if (minY < restY - 0.1f) bad.Add($"{id} sank to y={minY:F2} (rest {restY:F2}) at {topSpeed:F0} km/h");
                if (car.transform.position.x < endX - 1f) bad.Add($"{id} didn't make it across the map");
                Object.DestroyImmediate(car.gameObject);
            }
            Debug.Log($"[Sink] {rides.Count} spots, median ride={median:F2}, worst={worst:F2}, off={bad.Count}\n" + string.Join("\n", bad));
            Assert.IsEmpty(bad, "cars sink into the road at: " + string.Join("; ", bad));
        }

        [UnityTest]
        public IEnumerator BootWalkPunchDriveMission()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(30);

            var gm = GameManager.I;
            Assert.IsNotNull(gm, "GameManager missing");
            Assert.IsTrue(gm.InWorld, "level did not load");
            Assert.IsNotNull(gm.Player, "no player");
            Assert.Greater(Pedestrian.All.Count, 20, "not enough pedestrians");
            Assert.Greater(Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None).Length, 10, "not enough traffic");
            Assert.Greater(gm.Missions.npcs.Count, 8, "NPC cast missing");
            Debug.Log($"[Smoke] peds={Pedestrian.All.Count} npcs={gm.Missions.npcs.Count} places={gm.City.places.Count}");

            yield return Seconds(1.5f);
            Shot("01_level1_home");

            // player should be standing on the ground, not falling through
            var p = gm.Player;
            float y0 = p.transform.position.y;
            yield return Seconds(1f);
            Assert.Greater(p.transform.position.y, -1f, "player fell through the world");
            Assert.Less(Mathf.Abs(p.transform.position.y - y0), 1.5f, "player not grounded");

            // punch a pedestrian placed right in front of us
            var ped = PedestrianSpawner.Spawn(p.transform.position + p.transform.forward * 1.1f, Box(p.transform.position, 20f), gm.transform);
            yield return Frames(3);
            var pedStart = ped.transform.position;
            p.DebugPunch(3);
            yield return Seconds(0.8f);
            // H&R bopping: a punch staggers them back a step or topples them on the spot - no launch
            float pedMoved = Vector3.Distance(ped.transform.position, pedStart);
            Assert.Greater(pedMoved, 0.2f, "punch did not knock the pedestrian");
            Assert.Less(pedMoved, 6f, "punch sent the pedestrian flying");
            Shot("02_punch");

            // drive the family car
            var car = gm.LastCar;
            Assert.IsNotNull(car, "family car missing");
            p.EnterVehicle(car, true);
            var drv = new ScriptedDriver();
            car.driver = drv;
            var start = car.transform.position;
            yield return Seconds(3f);
            Shot("03_driving");
            float moved = Vector3.Distance(start, car.transform.position);
            Debug.Log($"[Smoke] car moved {moved:F1}m speed={car.SpeedKmh:F0}km/h grounded={car.grounded}");
            Assert.Greater(moved, 12f, "car did not drive");
            Assert.Greater(car.transform.position.y, -1f, "car fell through the world");
            drv.throttle = 0.3f; drv.steer = 1f;
            yield return Seconds(1.5f);
            Assert.Greater(Vector3.Dot(car.transform.up, Vector3.up), 0.5f, "car flipped over while turning");
            drv.throttle = -1f; drv.steer = 0;
            yield return Seconds(1.5f);
            p.ExitVehicle(false, true);
            yield return Frames(5);
            Assert.IsFalse(p.Driving);

            // start the first mission directly
            gm.Missions.StartMission(gm.Missions.story[0]);
            CloseBoxes();
            yield return Frames(5);
            Assert.IsTrue(gm.Missions.Active, "mission didn't start");
            Assert.IsNotNull(gm.Missions.Step, "mission has no objective");
            yield return Seconds(0.5f);
            Shot("04_mission");
            gm.Missions.Abort();

            // bird's-eye view of the whole map
            var cc = ChaseCamera.I;
            var pivot = new GameObject("pivot").transform;
            pivot.position = new Vector3(0, 0, 0);
            cc.target = pivot; cc.targetBody = null; cc.distance = 1300f; cc.pitch = 50f; cc.yaw = 20f;
            Camera.main.farClipPlane = 4000f;
            RenderSettings.fogEndDistance = 3500f; RenderSettings.fogStartDistance = 2500f;
            yield return Frames(3);
            Shot("05_map_overview");
            cc.distance = 90f; cc.pitch = 15f; cc.yaw = 60f;
            pivot.position = gm.City.places["Towers"] + Vector3.up * 20f;
            yield return Frames(3);
            Shot("06_towers");
            pivot.position = gm.City.places["Dataran"] + Vector3.up * 5f; cc.distance = 60f; cc.yaw = -90f;
            yield return Frames(3);
            Shot("07_dataran");
            pivot.position = gm.City.places["Pasar"] + Vector3.up * 3f; cc.distance = 40f; cc.yaw = 90f;
            yield return Frames(3);
            Shot("08_pasar");
            // street-level views of the new art
            pivot.position = gm.City.places["Mamak"] + new Vector3(20f, 2.5f, 2f); cc.distance = 14f; cc.pitch = 6f; cc.yaw = 165f;
            yield return Frames(3);
            Shot("13_street_bukitbintang");
            pivot.position = gm.City.places["HomeYard"] + new Vector3(0, 1.2f, 0); cc.distance = 12f; cc.pitch = 10f; cc.yaw = -120f;
            yield return Frames(3);
            Shot("14_kampung");
            pivot.position = gm.City.places["Mamak"] + new Vector3(0, 1.5f, 0); cc.distance = 14f; cc.pitch = 8f; cc.yaw = 200f;
            yield return Frames(3);
            Shot("15_mamak");

            // other levels load (night finale)
            gm.LoadLevel(4);
            CloseBoxes();
            yield return Seconds(1.5f);
            Assert.IsTrue(gm.InWorld);
            Assert.AreEqual("adik", gm.Player.def.id);
            Shot("09_level4_night");
            gm.LoadLevel(3);
            CloseBoxes();
            yield return Seconds(1f);
            Shot("10_level3_sunset");
        }
    }
}
