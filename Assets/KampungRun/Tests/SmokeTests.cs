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
                new Vector3(CityBuilder.RoadX(4), 0.3f, CityBuilder.RoadZ(4)),
                new Vector3(CityBuilder.RoadX(4), 0.3f, CityBuilder.RoadZ(2)),
                new Vector3(CityBuilder.RoadX(5), 0.3f, CityBuilder.RoadZ(2)),
            };
            var start = new Vector3(CityBuilder.RoadX(2), 0.9f, CityBuilder.RoadZ(4));
            var follower = VehicleSpawner.Spawn("van", start, Quaternion.LookRotation(Vector3.right), VehicleRole.MissionTarget);
            var rd = new RouteDriver(route, 13f);
            follower.driver = rd;

            yield return Seconds(12f);
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

            yield return Seconds(14f);
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
            var shops = CityBuilder.BlockCenter(4, 2);
            p.Teleport(new Vector3(CityBuilder.RoadX(4) + 7.5f, 0.3f, shops.z - 4f), Quaternion.Euler(0, 20, 0));
            for (int i = 0; i < 8; i++)
                PedestrianSpawner.Spawn(p.transform.position + new Vector3(Random.Range(-3f, 3f), 0, Random.Range(3f, 12f)),
                    new Rect(p.transform.position.x - 8, p.transform.position.z, 16, 16), gm.transform);
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
            // each place sits on the road north of its block; look back south at the building
            yield return Look("50_kl_sentral", gm.City.places["KLSentral"] + new Vector3(-6f, 0.2f, 2f), 170f, 26f, 12f);
            yield return Look("51_muzium_negara", gm.City.places["MuziumNegara"] + new Vector3(-6f, 0.3f, -9f), 160f, 11f, 16f);
            yield return Look("52_taman_perdana", gm.City.places["TamanPerdana"] + new Vector3(0f, 0.2f, -8f), 200f, 22f, 26f);
            yield return Look("53_masjid_negara", gm.City.places["MasjidNegara"] + new Vector3(4f, 0.2f, 2f), 190f, 30f, 14f);
            // a townsperson walking: the new soft-skinned, bouncy walk cycle up close
            var ped = PedestrianSpawner.Spawn(p.transform.position + p.transform.right * 2.5f,
                new Rect(p.transform.position.x - 30, p.transform.position.z - 30, 60, 60), gm.transform, "chr_townman");
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
                var start = new Vector3(CityBuilder.RoadX(4) + 2.5f, 0.6f, CityBuilder.RoadZ(1) + 8f);
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
            var kb = CityBuilder.BlockCenter(0, 3);
            yield return Look("31_kampung_baru", kb + new Vector3(0, 0.3f, 27f), 150f, 16f, 14f);
            var bf = gm.City.places["Brickfields"];
            yield return Look("32_brickfields", bf + new Vector3(-6f, 0.1f, 0.5f), 200f, 15f, 9f);
            var lrt = gm.City.places["LRT"];
            yield return Look("33_lrt_river", new Vector3(lrt.x + 26f, 0.3f, CityBuilder.RoadZ(2)), -100f, 22f, 8f);
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
            var row = new Vector3(CityBuilder.RoadX(3) + 2.5f, 0.6f, CityBuilder.RoadZ(2) + 6f);
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
            yield return Seconds(1.2f);
            Assert.IsTrue(p.Driving, "player should be in the Myvi");
            Shot("73_myvi_driver_seated");
            p.ExitVehicle(false, true);
            foreach (var v in parked) Object.Destroy(v.gameObject);
            yield return Frames(3);

            foreach (var id in ids)
            {
                var start = new Vector3(CityBuilder.RoadX(1) + 2.5f, 0.6f, CityBuilder.RoadZ(1) + 8f);
                var v = gm.SummonCar(id, start, Quaternion.identity);
                yield return Seconds(0.5f);
                p.EnterVehicle(v, true);
                var drv = new TurnDriver { steer = 0f };
                v.driver = drv;
                yield return Seconds(1.6f);
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
            var at = CityBuilder.BlockCenter(4, 2) + new Vector3(-19f, 0.2f, 0);
            var ped = PedestrianSpawner.Spawn(at, new Rect(at.x - 20, at.z - 20, 40, 40), gm.transform, "chr_townman");
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
            Assert.AreEqual(9, CityBuilder.NX);
            foreach (var place in new[] { "BatuCaves", "TuguNegara", "Pavilion", "Merdeka118", "StadiumMerdeka", "TheanHou", "IstanaNegara", "Masjid" })
            {
                Assert.IsTrue(gm.City.places.ContainsKey(place), $"{place} missing");
                var at = gm.City.places[place];
                p.Teleport(at + new Vector3(0, 0.2f, 2f), Quaternion.Euler(0, 180, 0));
                cc.target = p.transform; cc.targetBody = null;
                cc.yaw = place == "Masjid" ? 90f : 180f; cc.pitch = place == "Merdeka118" ? 22f : 14f;
                cc.distance = place == "Merdeka118" ? 40f : 24f;
                yield return Seconds(1f);
                Shot($"9_{place}");
            }
        }

        /// <summary>Drop a car on every road segment and bridge; it must settle on top of the
        /// visible road surface (y ~ 0), never half-sunk into it.</summary>
        [UnityTest]
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
                    var n = new Vector3(CityBuilder.RoadX(i), 0, CityBuilder.RoadZ(k));
                    spots.Add((n, 0f));                                                  // intersection
                    foreach (float d in new[] { 9f, 27f, 45f })
                    {
                        if (i < CityBuilder.NX) spots.Add((n + new Vector3(d, 0, 2.5f), 90f));   // E-W segments (bridges too)
                        if (k < CityBuilder.NZ) spots.Add((n + new Vector3(2.5f, 0, d), 0f));   // N-S segments
                    }
                }
            // clear traffic so no test car lands on a passing one
            foreach (var t in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) Object.Destroy(t.gameObject);
            gm.enabled = false;
            yield return Frames(2);
            var bad = new System.Collections.Generic.List<string>();
            var cars = new System.Collections.Generic.List<(Vehicle v, Vector3 at)>();
            foreach (var s in spots)
            {
                var v = VehicleSpawner.Spawn("saga", s.p + Vector3.up * 1.2f, Quaternion.Euler(0, s.yaw, 0), VehicleRole.Parked);
                v.driver = null;
                cars.Add((v, s.p));
            }
            yield return Seconds(2.5f);
            // every car should settle at the same ride height as on an ordinary road tile
            var ys = cars.ConvertAll(c => c.v.transform.position.y);
            ys.Sort();
            float median = ys[ys.Count / 2], worst = ys[0];
            foreach (var (v, at) in cars)
            {
                float y = v.transform.position.y;
                Object.DestroyImmediate(v.gameObject);
                if (Mathf.Abs(y - median) > 0.08f)
                {
                    string what = "";
                    foreach (var h in Physics.OverlapBox(at + Vector3.up * 1f, new Vector3(1.2f, 1f, 2.6f), Quaternion.identity, ~0,
                                 QueryTriggerInteraction.Ignore))
                        what += $" [{h.transform.root.name}/{h.transform.parent?.name}/{h.name} {h.GetType().Name} top={h.bounds.max.y:F2}]";
                    bad.Add($"{at} y={y:F2}{what}");
                }
            }
            // and at speed: flat out along the whole E-W avenue (over the bridge). Ride height
            // must hold at top speed - downforce used to bottom the suspension out.
            foreach (var id in new[] { "saga", "basmini", "bike" })
            {
                var start = new Vector3(CityBuilder.RoadX(0) + 8f, 0.6f, CityBuilder.RoadZ(3) + 2.5f);
                var car = VehicleSpawner.Spawn(id, start, Quaternion.Euler(0, 90, 0), VehicleRole.Player);
                car.driver = new ScriptedDriver();
                yield return Seconds(1f);                                  // settle from the drop
                float restY = car.transform.position.y, minY = 9f, minX = 0f, topSpeed = 0f;
                float endX = CityBuilder.RoadX(CityBuilder.NX) - 8f, t0 = Time.time;
                while (car.transform.position.x < endX && Time.time - t0 < 30f)
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
            Debug.Log($"[Sink] {cars.Count} spots, median y={median:F2}, worst y={worst:F2}, off={bad.Count}\n" + string.Join("\n", bad));
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
            var ped = PedestrianSpawner.Spawn(p.transform.position + p.transform.forward * 1.1f, new Rect(p.transform.position.x - 20, p.transform.position.z - 20, 40, 40), gm.transform);
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
            cc.target = pivot; cc.targetBody = null; cc.distance = 330f; cc.pitch = 50f; cc.yaw = 20f;
            RenderSettings.fogEndDistance = 1200f; RenderSettings.fogStartDistance = 900f;
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
