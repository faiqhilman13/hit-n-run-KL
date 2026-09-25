using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KampungRun
{
    /// <summary>
    /// Boots the whole game from an (almost) empty scene: builds KL, sets up the camera,
    /// light and post, shows the title screen and loads levels.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        [Tooltip("Skip the title screen and jump straight into this level (0 = show title).")]
        public int autoStartLevel;
        public static int ForceLevel; // set by tests before the scene loads
        public int trafficCount = 45;
        public int pedestrianCount = 200;               // map-wide spread; also sets the kampung's per-lot crowd
        public float cityCrowd = 1f;                    // map-wide spread: city lots get this many times the kampung crowd
        public float cityNear = 12f;                    // ...and the city lots around the player are topped up to this
        public int crowdCap = 320;                      // most pedestrians alive at once

        public CityBuilder.City City { get; private set; }
        public MissionManager Missions { get; private set; }
        public PlayerController Player { get; private set; }
        public Vehicle LastCar { get; private set; }
        public bool InWorld { get; private set; }
        public bool Paused { get; private set; }
        public bool Playing => InWorld && !Paused && (HUD.I == null || !HUD.I.MenuOpen);

        Transform _levelRoot, _trafficRoot;
        readonly List<Vehicle> _traffic = new List<Vehicle>();
        Vehicle _summoned;
        Light _sun;
        Material _sky;
        Camera _cam;
        float _trafficTimer;

        /// <summary>Capture the mouse for mouse-look - never on touch screens (no pointer lock there).</summary>
        static void LockCursor()
        {
            if (TouchInput.Active || TouchControls.Wanted) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>Browser build: lighter crowds and traffic, shorter shadows, and a lower render
        /// scale on phones so it stays smooth in a tab.</summary>
        /// <summary>Phone browser: skip the extras (traffic drivers) that cost the most.</summary>
        public static bool WebLite { get; private set; }

        void WebTuning()
        {
            if (Application.platform != RuntimePlatform.WebGLPlayer) return;
            bool phone = Application.isMobilePlatform;
            WebLite = phone;
            Application.targetFrameRate = -1;                       // browsers pace frames themselves
            trafficCount = phone ? 16 : 30;
            pedestrianCount = phone ? 52 : 100;
            cityNear = phone ? 12f : 24f;
            crowdCap = phone ? 90 : 240;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = phone ? 45f : 70f;
                urp.renderScale = phone ? 0.75f : 1f;
                urp.msaaSampleCount = 1;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void KampungReady();
#else
        static void KampungReady() { }
#endif

        System.Collections.IEnumerator SignalReady()
        {
            // the loading page stays up until the city is built and the first frame has drawn
            yield return null;
            yield return new WaitForEndOfFrame();
            KampungReady();
        }

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            WebTuning();
            ProcAudio.Init();
            GameState.Load();
            SetupRendering();
            new GameObject("HUD").AddComponent<HUD>().transform.SetParent(transform, false);
            var sm = new GameObject("SamanMeter").AddComponent<SamanMeter>();
            sm.transform.SetParent(transform, false);
            City = new CityBuilder().Build(transform);
        }

        void Start()
        {
            StartCoroutine(SignalReady());
            if (ForceLevel > 0) { int l = ForceLevel; ForceLevel = 0; LoadLevel(l); }
            else if (autoStartLevel > 0) LoadLevel(autoStartLevel);
            else TitleScreen();
        }

        // ------------------------------------------------------------ rendering setup
        void SetupRendering()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                _cam = cgo.AddComponent<Camera>();
                cgo.AddComponent<AudioListener>();
            }
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.farClipPlane = 1700f;                              // Merdeka 118 from across town
            var cull = new float[32];
            cull[Layers.Detail] = Application.platform == RuntimePlatform.WebGLPlayer ? 150f : 200f;
            _cam.layerCullDistances = cull;
            _cam.fieldOfView = 62f;
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            if (_cam.GetComponent<ChaseCamera>() == null) _cam.gameObject.AddComponent<ChaseCamera>();

            _sun = FindAnyObjectByType<Light>();
            if (_sun == null || _sun.type != LightType.Directional)
            {
                _sun = new GameObject("Matahari").AddComponent<Light>();
                _sun.type = LightType.Directional;
            }
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            // Hit & Run painted textures on the world (grass, asphalt, brick, timber...)
            if (GameAssets.I.surfaces != null) Shader.SetGlobalTexture("_SurfaceArray", GameAssets.I.surfaces);
            Shader.SetGlobalFloat("_SurfaceStrength", GameAssets.I.surfaces != null ? 1f : 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;

            // post: soft vignette + film grain = old printed page
            var volGo = new GameObject("PostVolume");
            volGo.transform.SetParent(transform, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.16f);
            vig.smoothness.Override(0.5f);
            vig.color.Override(new Color(0.1f, 0.1f, 0.25f));
            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(4f);
            ca.contrast.Override(6f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.None);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0f);
            vol.sharedProfile = profile;
        }

        void ApplyLevelLook(LevelDef l)
        {
            _sun.transform.rotation = Quaternion.Euler(l.sunEuler);
            _sun.color = l.sun;
            _sun.intensity = l.number == 4 ? 0.8f : 1.1f;
            _cam.backgroundColor = l.fog;
            RenderSettings.fogColor = l.fog;
            // the life-size city is ~2x the old one: push the haze out with it
            RenderSettings.fogStartDistance = l.fogStart * 1.5f;
            RenderSettings.fogEndDistance = l.fogEnd * 1.6f;
            RenderSettings.ambientLight = l.paper;

            // cartoon sky
            if (_sky == null && GameAssets.I.sky != null) _sky = new Material(GameAssets.I.sky) { name = "CartoonSky" };
            if (_sky != null)
            {
                _sky.SetColor("_Zenith", l.zenith);
                _sky.SetColor("_Horizon", l.fog);
                _sky.SetColor("_Ground", Color.Lerp(l.fog, new Color(0.45f, 0.6f, 0.4f), 0.4f));
                _sky.SetColor("_CloudColor", l.cloud);
                _sky.SetColor("_CloudShade", l.cloudShade);
                _sky.SetFloat("_CloudCover", l.cloudCover);
                _sky.SetFloat("_Stars", l.stars);
                _sky.SetVector("_SunDir", -_sun.transform.forward);
                _sky.SetColor("_SunColor", l.number == 4 ? new Color(0.95f, 0.95f, 0.85f) : new Color(1f, 0.93f, 0.55f));
                RenderSettings.skybox = _sky;
                _cam.clearFlags = CameraClearFlags.Skybox;
            }

            // Kampung Boy x Simpsons: bold flat cel colours, clean outlines, and Lat's pen
            // hatching kept only inside cast shadows.
            Shader.SetGlobalVector("_LatStyle", new Vector4(0.55f, 0f, 0.25f, 0.12f));
            Shader.SetGlobalVector("_LatStyle2", new Vector4(0f, 1.05f, 0f, 0f));
            Shader.SetGlobalFloat("_LatStyleSet", 1f);
            // every LatInk material reads these globals (alpha 1 = "set")
            var paper = l.paper; paper.a = 1f;
            var shadow = l.shadowTint; shadow.a = 1f;
            Shader.SetGlobalColor("_LatPaper", paper);
            Shader.SetGlobalColor("_LatShadow", shadow);

            // Hit & Run look: smooth soft-lit shading with a sky/ground hemisphere ambient,
            // no ink outlines. Night levels dim the ambient with the level's paper tone.
            VehicleVisuals.Night = l.number == 4;
            float k = l.paper.grayscale;
            Shader.SetGlobalFloat("_ShadeMode", 1f);
            Shader.SetGlobalFloat("_ShadeOutline", 0f);
            Shader.SetGlobalColor("_ShadeSky", Color.Lerp(l.fog, l.zenith, 0.3f) * 0.58f * k + new Color(0.06f, 0.06f, 0.06f) * k);
            Shader.SetGlobalColor("_ShadeGround", Color.Lerp(l.shadowTint, new Color(0.55f, 0.6f, 0.4f), 0.4f) * 0.4f * k);
        }

        // ------------------------------------------------------------ menus
        void TitleScreen()
        {
            InWorld = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // orbit the camera around the towers behind the menu
            var cc = ChaseCamera.I;
            var pivot = new GameObject("TitlePivot").transform;
            pivot.position = City.places.TryGetValue("Towers", out var t) ? t + new Vector3(-60, 30, -80) : Vector3.up * 30;
            cc.target = pivot;
            cc.targetBody = null;
            cc.distance = 60f;
            cc.pitch = 12f;
            cc.yaw = 200f;
            ApplyLevelLook(GameData.Levels[Mathf.Clamp(GameState.Level, 1, GameData.LastLevel)]);
            ProcAudio.PlayMusic(0);
            ShowTitleMenu();
        }

        void ShowTitleMenu()
        {
            var opts = new List<string> { "Main Baru" };
            bool save = GameState.HasSave;
            if (save) opts.Add($"Sambung - Tahap {GameState.Level} ({GameState.Percent}%)");
            opts.Add("Pilih Tahap");
            opts.Add("Kawalan");
            if (Application.platform != RuntimePlatform.WebGLPlayer) opts.Add("Keluar");   // nothing to quit to in a browser tab
            HUD.I.Menu("KAMPUNG RUN: KL", opts, i =>
            {
                string o = opts[i];
                if (o == "Main Baru") { GameState.NewGame(); PlayIntro(); }
                else if (o.StartsWith("Sambung")) LoadLevel(GameState.Level);
                else if (o == "Pilih Tahap") LevelSelect(ShowTitleMenu);
                else if (o == "Kawalan") ShowControls(ShowTitleMenu);
                else Application.Quit();
            }, ShowTitleMenu);
        }

        void PlayIntro()
        {
            Dialogue.Say(new[]
            {
                new Line("", "Kuala Lumpur. Kota raya yang sibuk, panas, dan penuh dengan kereta."),
                new Line("", "Di tengah-tengahnya, sebuah kampung kecil masih bertahan: Kampung Baru."),
                new Line("", "Di situ tinggal Pak Mat, Mak Som, Along dan Adik..."),
                new Line("", "...dan mereka tak tahu yang sesuatu yang PELIK sedang berlaku di KL."),
            }, () => LoadLevel(1));
        }

        void LevelSelect(System.Action back)
        {
            var opts = new List<string>();
            int max = 1;
            for (int l = 1; l <= GameData.LastLevel; l++) if (l == 1 || GameState.Data.missionsDone[l - 1] >= GameData.MissionsPerLevel) max = l;
            max = Mathf.Max(max, GameState.Level);
            for (int l = 1; l <= max; l++) opts.Add(GameData.Levels[l].title + " - " + GameData.Levels[l].subtitle);
            HUD.I.Menu("PILIH TAHAP", opts, i => LoadLevel(i + 1), back);
        }

        void ShowControls(System.Action back)
        {
            if (TouchInput.Active)
            {
                Dialogue.Say(new[]
                {
                    new Line("KAWALAN", "Ibu jari kiri: gerak (tolak sampai hujung untuk lari). Leret sebelah kanan: pusing kamera."),
                    new Line("KAWALAN", "LOMPAT (tekan dua kali: lompat berganda). TUMBUK (combo 3 kali). TENDANG - di udara = hentak!"),
                    new Line("KAWALAN", "E: naik/turun kereta & cakap. Dalam kereta: ibu jari kiri pandu, BREK, HON, BALIK kalau terbalik."),
                    new Line("KAWALAN", "II: rehat. Jangan langgar orang sangat... METER SAMAN penuh = polis kejar!"),
                }, back);
                return;
            }
            Dialogue.Say(new[]
            {
                new Line("KAWALAN", "Jalan: WASD / stik kiri.  Kamera: tetikus / stik kanan.  Lari: Shift / LB."),
                new Line("KAWALAN", "Lompat: Space / A (tekan dua kali untuk lompat berganda)."),
                new Line("KAWALAN", "Tumbuk: Q / klik kiri / B (combo 3 kali).  Tendang: F / klik kanan / X.  Tendang di udara = hentak!"),
                new Line("KAWALAN", "Naik/turun kereta & cakap: E / Y.  Hon: H.  Brek tangan: Space.  Terbalik? R."),
                new Line("KAWALAN", "Jangan langgar orang sangat... METER SAMAN penuh = polis kejar!"),
            }, back);
        }

        void PauseMenu()
        {
            Paused = true;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var opts = new List<string> { "Sambung", "Pilih Tahap", "Kawalan" };
            if (Missions != null && Missions.Active) opts.Add("Batal Misi");
            opts.Add("Simpan & Menu Utama");
            HUD.I.Menu($"REHAT  -  {GameState.Percent}% siap", opts, i =>
            {
                string o = opts[i];
                if (o == "Sambung") Resume();
                else if (o == "Pilih Tahap") LevelSelect(PauseMenu);
                else if (o == "Kawalan") ShowControls(PauseMenu);
                else if (o == "Batal Misi") { Missions.Abort(); Resume(); }
                else { GameState.Save(); Resume(); UnloadLevel(); TitleScreen(); }
            }, Resume);
        }

        void Resume()
        {
            Paused = false;
            Time.timeScale = 1f;
            if (InWorld) LockCursor();
        }

        void Update()
        {
            if (InWorld && GameInput.PauseDown && !HUD.I.MenuOpen && !Dialogue.Showing) PauseMenu();
            if (!Playing) return;
            // clicking back into the window re-captures the mouse
            if (Cursor.lockState != CursorLockMode.Locked && UnityEngine.InputSystem.Mouse.current != null &&
                UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
                LockCursor();
            ManageTraffic();
            ManageCrowd();
        }

        // ------------------------------------------------------------ levels
        public void LoadLevel(int level)
        {
            Resume();
            UnloadLevel();
            GameState.Data.level = level;
            GameState.Save();
            var def = GameData.Levels[level];
            ApplyLevelLook(def);

            _levelRoot = new GameObject("Level" + level).transform;
            _levelRoot.SetParent(transform, false);
            _trafficRoot = new GameObject("Traffic").transform;
            _trafficRoot.SetParent(_levelRoot, false);

            // player
            var pgo = new GameObject("Player");
            pgo.transform.SetParent(_levelRoot, false);
            pgo.AddComponent<CharacterController>();
            Player = pgo.AddComponent<PlayerController>();
            Player.SetCharacter(def.player);
            Player.Teleport(City.places["PlayerSpawn"], City.facings["PlayerSpawn"]);

            // family car by the house
            SummonCar(def.familyCar, City.places["HomeCar"], City.facings["HomeCar"]);

            // missions + NPCs
            var mgo = new GameObject("Missions");
            mgo.transform.SetParent(_levelRoot, false);
            Missions = mgo.AddComponent<MissionManager>();
            var book = MissionBook.Build(level, City, Missions, _levelRoot);
            Missions.Setup(book.story, book.race);

            SpawnCollectibles(level);
            SpawnPedestrians();
            for (int i = 0; i < trafficCount; i++) SpawnTrafficCar(true);

            InWorld = true;
            LockCursor();
            HUD.I.BigMessage(def.title, $"Bermain sebagai {def.subtitle}");
            ProcAudio.Play2D(ProcAudio.Fanfare, 0.6f);
            ProcAudio.PlayMusic(level);
            if (book.levelIntro != null) Dialogue.Say(book.levelIntro);
        }

        void UnloadLevel()
        {
            InWorld = false;
            SamanMeter.I?.ClearAll();
            if (Player && Player.Driving) Player.ExitVehicle(true, true);
            if (_levelRoot) Destroy(_levelRoot.gameObject);
            _traffic.Clear();
            Interactables.All.RemoveAll(i => i == null || (i is Object o && o == null) || (i is NPC));
            foreach (var p in Pedestrian.All.ToArray()) if (p) Destroy(p.gameObject);
            Player = null;
            Missions = null;
            LastCar = null;
            _summoned = null;
        }

        public void LevelComplete()
        {
            int lvl = GameState.Level;
            ProcAudio.Play2D(ProcAudio.Fanfare, 1f, 0.8f);
            HUD.I.BigMessage("TAHAP SELESAI!", GameData.Levels[lvl].title);
            if (lvl >= GameData.LastLevel)
            {
                GameState.Data.finale = true;
                GameState.Save();
                Dialogue.Say(MissionBook.Ending, () =>
                {
                    HUD.I.BigMessage("TAMAT", $"Terima kasih kerana bermain!  {GameState.Percent}% siap");
                    // free roam continues; collectibles remain to find
                    Missions.RefreshGivers();
                });
                return;
            }
            GameState.Data.level = lvl + 1;
            GameState.Save();
            Dialogue.Say(new[] { new Line("", $"Seterusnya... {GameData.Levels[lvl + 1].title}") }, () => LoadLevel(lvl + 1));
        }

        public void Respawn()
        {
            if (Player == null) return;
            Player.Teleport(City.places["PlayerSpawn"], City.facings["PlayerSpawn"]);
        }

        public void ResetForRetry(Vector3 pos, Quaternion rot)
        {
            SamanMeter.I.ClearAll();
            Player.Teleport(pos, rot);
            if (LastCar == null || LastCar.Wrecked || Vector3.Distance(LastCar.transform.position, pos) > 40f)
                SummonCar(GameData.Levels[GameState.Level].familyCar, pos + rot * Vector3.right * 4f + Vector3.up * 0.5f, rot);
            else LastCar.Repair();
        }

        /// <summary>Spawn one of the player's cars (phone booth, dealer, mission start).</summary>
        public Vehicle SummonCar(string carId, Vector3 pos, Quaternion rot)
        {
            if (_summoned && (Player == null || Player.vehicle != _summoned)) Destroy(_summoned.gameObject);
            // don't drop it on top of something
            if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out var hit, 12f, ~(1 << Layers.Character | 1 << Layers.Pickup), QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y + 0.4f;
            _summoned = VehicleSpawner.Spawn(carId, pos, rot, VehicleRole.Parked, _levelRoot);
            LastCar = _summoned;
            Fx.Burst(pos + Vector3.up, new Color(0.9f, 0.85f, 0.7f), 12, 4f);
            return _summoned;
        }

        public void OnPlayerEnteredVehicle(Vehicle v)
        {
            LastCar = v;
            if (_traffic.Remove(v)) { } // carjacked traffic becomes ours
        }

        public void OnPlayerExitedVehicle(Vehicle v) => LastCar = v;

        // ------------------------------------------------------------ population
        void SpawnCollectibles(int level)
        {
            var rng = new System.Random(level * 97);
            var spots = new List<Vector3>(City.itemSpots);
            // Kad Lat cards
            for (int i = 0; i < GameData.CardsPerLevel && spots.Count > 0; i++)
            {
                int k = rng.Next(spots.Count);
                var pos = spots[k] + Vector3.up * 0.8f;
                spots.RemoveAt(k);
                string id = $"L{level}_card{i}";
                if (GameState.Data.cards.Contains(id)) continue;
                var go = ModelFactory.Spawn("Prop_KadLat", pos, Quaternion.identity, _levelRoot, "KadLat");
                var p = Pickup.Setup(go, Pickup.Kind.Card, 1);
                p.id = id;
                p.magnet = false;
            }
            // Burung Kamera
            for (int i = 0; i < GameData.CamerasPerLevel && spots.Count > 0; i++)
            {
                int k = rng.Next(spots.Count);
                var pos = spots[k] + Vector3.up * 2.2f;
                spots.RemoveAt(k);
                string id = $"L{level}_cam{i}";
                if (GameState.Data.cameras.Contains(id)) continue;
                BurungKamera.Spawn(id, pos, _levelRoot);
            }
            // coins
            var coinRoot = new GameObject("Coins").transform;
            coinRoot.SetParent(_levelRoot, false);
            foreach (var c in City.coinSpots) Pickup.SpawnCoin(c, false, coinRoot);
        }

        Transform _pedRoot;
        float _crowdTimer;

        static Vector3 SidewalkPoint(Rect z)
        {
            float t = Random.value;
            var inset = new Rect(z.x + 1.5f, z.y + 1.5f, z.width - 3f, z.height - 3f);
            Vector2 p = Random.Range(0, 4) switch
            {
                0 => new Vector2(Mathf.Lerp(inset.xMin, inset.xMax, t), inset.yMin),
                1 => new Vector2(Mathf.Lerp(inset.xMin, inset.xMax, t), inset.yMax),
                2 => new Vector2(inset.xMin, Mathf.Lerp(inset.yMin, inset.yMax, t)),
                _ => new Vector2(inset.xMax, Mathf.Lerp(inset.yMin, inset.yMax, t)),
            };
            return new Vector3(p.x, 0.2f, p.y);
        }

        static bool Kampung(Rect z) => CityBuilder.District(new Vector3(z.center.x, 0f, z.center.y)).StartsWith("Kampung");

        void SpawnPedestrians()
        {
            var root = new GameObject("Orang").transform;
            root.SetParent(_levelRoot, false);
            _pedRoot = root;
            var zones = City.walkZones;
            // pedestrianCount sets the kampung's per-block crowd; the KL city blocks east of the
            // river are busier streets and get cityCrowd times as many people per block
            var weights = new float[zones.Count];
            float sum = 0f;
            for (int i = 0; i < zones.Count; i++)
            {
                weights[i] = Kampung(zones[i]) ? 1f : cityCrowd;
                sum += weights[i];
            }
            int total = Mathf.RoundToInt(pedestrianCount * sum / Mathf.Max(1, zones.Count));
            for (int i = 0; i < total; i++)
            {
                float pick = Random.value * sum;
                int zi = 0;
                while (zi < zones.Count - 1 && pick > weights[zi]) { pick -= weights[zi]; zi++; }
                var z = zones[zi];
                PedestrianSpawner.Spawn(SidewalkPoint(z), z, root);
            }
        }

        /// <summary>H&amp;R-busy streets without thousands of people: the city blocks around the player
        /// are topped up to cityNear x the kampung crowd, using townsfolk left far behind (or new ones,
        /// up to crowdCap). People only ever appear out of the camera's view.</summary>
        void ManageCrowd()
        {
            if (_pedRoot == null || Player == null || (_crowdTimer -= Time.deltaTime) > 0f) return;
            _crowdTimer = 0.5f;
            const float Near = 130f, Far = 170f;
            var focus = Player.Focus;
            var zones = City.walkZones;
            float baseD = pedestrianCount / (float)Mathf.Max(1, zones.Count);
            var counts = new Dictionary<Rect, int>();
            var spare = new List<Pedestrian>();
            foreach (var ped in Pedestrian.All)
            {
                if (ped == null) continue;
                counts.TryGetValue(ped.zone, out int n);
                counts[ped.zone] = n + 1;
                // only city folk get moved about - the kampung keeps its own people
                if (ped.Idle && !Kampung(ped.zone) && Flat(ped.transform.position - focus) > Far && !OnScreen(ped.transform.position)) spare.Add(ped);
            }
            spare.Sort((a, b) => Flat(b.transform.position - focus).CompareTo(Flat(a.transform.position - focus)));
            int moves = 0, si = 0;
            // fill the closest lots first, so the street you're on is the busy one
            var near = zones.FindAll(z => Flat(new Vector3(z.center.x, 0f, z.center.y) - focus) < Near);
            near.Sort((a, b) => Flat(new Vector3(a.center.x, 0f, a.center.y) - focus).CompareTo(Flat(new Vector3(b.center.x, 0f, b.center.y) - focus)));
            foreach (var z in near)
            {
                if (moves >= 14) break;
                counts.TryGetValue(z, out int have);
                int want = Mathf.RoundToInt(Kampung(z) ? baseD : baseD * cityNear);
                for (; have < want && moves < 14; have++)
                {
                    // a sidewalk spot the camera can't see
                    Vector3 pos = Vector3.zero;
                    bool found = false;
                    for (int tries = 0; tries < 10 && !found; tries++) { pos = SidewalkPoint(z); found = !InView(pos); }
                    if (!found) break;
                    if (si < spare.Count) { counts[spare[si].zone]--; spare[si++].Relocate(pos, z); }
                    else if (Pedestrian.All.Count < crowdCap) PedestrianSpawner.Spawn(pos, z, _pedRoot);
                    else break;
                    moves++;
                }
                counts[z] = have;
            }
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        /// <summary>Would the player see someone appear here? On screen, close enough to make out
        /// (under 90 m), and not hidden behind a building.</summary>
        bool InView(Vector3 p)
        {
            if (!OnScreen(p) || _cam == null) return false;
            var eye = _cam.transform.position;
            var head = p + Vector3.up * 1.4f;
            if ((head - eye).sqrMagnitude > 90f * 90f) return false;
            const int solid = ~((1 << Layers.Character) | (1 << Layers.Pickup) | (1 << Layers.Vehicle) | (1 << Layers.Detail));
            return !Physics.Linecast(eye, head, solid, QueryTriggerInteraction.Ignore);
        }

        bool OnScreen(Vector3 p)
        {
            if (_cam == null) return false;
            var vp = _cam.WorldToViewportPoint(p + Vector3.up);
            return vp.z > 0f && vp.z < 160f && vp.x > -0.15f && vp.x < 1.15f && vp.y > -0.15f && vp.y < 1.15f;
        }

        void SpawnTrafficCar(bool anywhere)
        {
            var roads = City.roads;
            var p = Player ? Player.Focus : Vector3.zero;
            var node = anywhere ? roads.RandomNodeAwayFrom(p, 30f) : roads.RandomNodeAwayFrom(p, 120f, 340f);
            var next = node.links[Random.Range(0, node.links.Count)];
            var pos = roads.LanePoint(node.pos, next.pos, 0.3f) + Vector3.up * 0.6f;
            if (Physics.CheckSphere(pos + Vector3.up, 3f, 1 << Layers.Vehicle)) return;
            var dir = next.pos - node.pos;
            var v = VehicleSpawner.SpawnTraffic(pos, Quaternion.LookRotation(dir), _trafficRoot);
            v.driver = new TrafficDriver(roads, node) { cruise = Random.Range(9f, 13f) };
            _traffic.Add(v);
        }

        void ManageTraffic()
        {
            _trafficTimer -= Time.deltaTime;
            if (_trafficTimer > 0) return;
            _trafficTimer = 1f;
            var p = Player ? Player.Focus : Vector3.zero;
            for (int i = _traffic.Count - 1; i >= 0; i--)
            {
                var v = _traffic[i];
                if (v == null) { _traffic.RemoveAt(i); continue; }
                bool far = Vector3.Distance(v.transform.position, p) > 400f;
                bool dead = v.Wrecked && Vector3.Distance(v.transform.position, p) > 60f;
                bool fell = v.transform.position.y < -1.5f; // in the river or off the map
                if (v.role != VehicleRole.Traffic && v.role != VehicleRole.Parked) { _traffic.RemoveAt(i); continue; }
                if (far || dead || fell)
                {
                    Destroy(v.gameObject);
                    _traffic.RemoveAt(i);
                }
            }
            if (_traffic.Count < trafficCount) SpawnTrafficCar(false);
        }
    }
}
