using System;
using System.Collections.Generic;
using KampungRun.Arch;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace KampungRun
{
    // The objective types that aren't "drive to the marker" or "pick up the shiny things": the missions pass of
    // 20 Oct 2026 (docs/AGENT_HANDOFF.md). Each one has a single clear verb - drive clean, carry carefully,
    // photograph, haggle, sneak, search, hide, ram, escort, defend, brawl, chase on foot, fight a boss - and reads
    // the world the game already builds (rooms, pavements, roads, signals, the Meter Saman) without touching it.

    /// <summary>A slow spin and bob, so a mission item reads as "take me".</summary>
    public class MissionSpin : MonoBehaviour
    {
        Vector3 _home;
        float _seed;
        void Start() { _home = transform.position; _seed = Random.value * 6f; }
        void Update()
        {
            transform.position = _home + Vector3.up * (0.25f + Mathf.Sin((Time.time + _seed) * 2.5f) * 0.12f);
            transform.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
        }
    }

    /// <summary>Shared look of the MegaMaju men and the cendol-crazed crowd.</summary>
    public static class MissionLooks
    {
        static Color C(float r, float g, float b) => new Color(r, g, b);

        /// <summary>MegaMaju security: black suit, red tie (chr_townman's palette slots).</summary>
        public static readonly Dictionary<string, Color> Suit = new Dictionary<string, Color>
        {
            ["BatikBlue"] = C(0.11f, 0.11f, 0.13f), ["Pants"] = C(0.13f, 0.13f, 0.15f), ["White"] = C(0.12f, 0.12f, 0.14f),
            ["Pastel2"] = C(0.85f, 0.14f, 0.14f), ["Pastel3"] = C(0.85f, 0.14f, 0.14f),
        };
        /// <summary>MegaMaju promoters: red vest, yellow cap.</summary>
        public static readonly Dictionary<string, Color> Promoter = new Dictionary<string, Color>
        {
            ["BatikBlue"] = C(0.86f, 0.16f, 0.16f), ["Pants"] = C(0.2f, 0.2f, 0.24f), ["White"] = C(0.86f, 0.16f, 0.16f),
            ["Pastel2"] = C(0.98f, 0.82f, 0.18f), ["Pastel3"] = C(0.98f, 0.82f, 0.18f),
        };
        /// <summary>Cendol Ajaib drinkers: a minty-green tinge and a dopey grin.</summary>
        public static readonly Dictionary<string, Color> Zombie = new Dictionary<string, Color>
        {
            ["Skin"] = C(0.62f, 0.8f, 0.52f), ["BatikBlue"] = C(0.45f, 0.72f, 0.42f), ["White"] = C(0.72f, 0.88f, 0.62f),
            ["Pastel2"] = C(0.4f, 0.65f, 0.38f), ["Pastel3"] = C(0.55f, 0.78f, 0.48f),
        };
        /// <summary>A snatch thief (mat ragut): black jacket, red helmet-hair.</summary>
        public static readonly Dictionary<string, Color> Thief = new Dictionary<string, Color>
        {
            ["BatikBlue"] = C(0.16f, 0.16f, 0.2f), ["White"] = C(0.16f, 0.16f, 0.2f), ["Pants"] = C(0.25f, 0.3f, 0.55f),
            ["Pastel2"] = C(0.85f, 0.2f, 0.15f),
        };
        public static readonly string[] ZombieMoans = { "Cennndol...", "Nak... cendol...", "Cendol... ajaib...", "Sedapnyaaa...", "Patuh... patuh..." };

        /// <summary>The way a point lies from another, Manglish compass ("sebelah utara").</summary>
        public static string Compass(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            if (a < 0) a += 360f;
            string[] n = { "utara", "timur laut", "timur", "tenggara", "selatan", "barat daya", "barat", "barat laut" };
            return n[Mathf.RoundToInt(a / 45f) % 8];
        }

        /// <summary>An open spot on the ground near c (not a roof, not behind a wall from c), or c itself.</summary>
        public static Vector3 Open(Vector3 c, float r, int seed, float minR = 0f)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < 40; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = Mathf.Lerp(minR, r, (float)rng.NextDouble());
                var p = c + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                if (!Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 60f, Goon.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.point.y - c.y) > 1.3f) continue;
                if (Physics.Linecast(c + Vector3.up * 1.1f, hit.point + Vector3.up * 1.1f, Goon.SightMask, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.CheckSphere(hit.point + Vector3.up * 1f, 0.6f, Goon.SightMask, QueryTriggerInteraction.Ignore)) continue;
                return hit.point;
            }
            return c;
        }

        public static Color Health(float v) => Color.Lerp(new Color(0.85f, 0.2f, 0.16f), new Color(0.35f, 0.7f, 0.3f), v);
    }

    // ===================================================================================================== driving

    /// <summary>
    /// Drive somewhere without breaking the law: Inspektor Rosli's brainwashed officers book you for anything.
    /// Running a red, knocking someone down, a big crash or speeding for a few seconds is a saman; three and you fail.
    /// </summary>
    public class CleanDriveObjective : Objective
    {
        readonly Vector3 _pos;
        readonly int _allowed;
        readonly float _limit;
        int _strikes, _redBase;
        float _crashCool, _speedT, _grace;
        Vehicle _sub;
        MissionManager _m;
        public int Strikes => _strikes;

        public CleanDriveObjective(Vector3 pos, string text, float time, int allowed = 3, float limitKmh = 70f)
        { _pos = pos; this.text = text; timeLimit = time; _allowed = allowed; _limit = limitKmh; }

        public override Vector3? Target => _pos;
        public override string Progress => $"Saman: {_strikes}/{_allowed}";

        public override void Begin(MissionManager m)
        {
            _m = m;
            _strikes = 0;
            _speedT = 0f;
            _grace = 1.5f;
            _redBase = TrafficSignals.PlayerRedRuns;
            m.PedHit += OnPed;
        }

        void OnPed(Pedestrian p)
        {
            if (P != null && P.Driving && Vector3.Distance(p.transform.position, P.Focus) < 12f) Strike("Langgar orang!");
        }

        void OnCrash(Vehicle v, Collision c, float rel)
        {
            if (rel > 9f && _crashCool <= 0f) { _crashCool = 2f; Strike("Accident!"); }
        }

        public void Strike(string why)
        {
            if (_grace > 0f || failReason != null) return;
            _strikes++;
            HUD.I?.Toast($"SAMAN! {why}  ({_strikes}/{_allowed})");
            if (P != null) Fx.Word(P.Focus + Vector3.up * 3f, "SAMAN!");
            ProcAudio.Play2D(ProcAudio.Fail, 0.35f, 1.4f);
            if (_strikes >= _allowed) failReason = $"Kena saman {_allowed} kali! Hutang saman makin panjang...";
        }

        public override bool Tick(MissionManager m, float dt)
        {
            _crashCool -= dt;
            _grace -= dt;
            if (P.vehicle != _sub)
            {
                if (_sub) _sub.OnCrash -= OnCrash;
                _sub = P.vehicle;
                if (_sub) _sub.OnCrash += OnCrash;
            }
            if (TrafficSignals.PlayerRedRuns > _redBase) { _redBase = TrafficSignals.PlayerRedRuns; Strike("Langgar lampu merah!"); }
            if (P.Driving && P.vehicle.SpeedKmh > _limit)
            {
                _speedT += dt;
                MissionOverlay.Hint($"Perlahan sikit! Had laju {_limit:F0} km/j");
                if (_speedT > 2.5f) { _speedT = -1.5f; Strike("Bawa laju!"); }
            }
            else _speedT = Mathf.Max(0f, _speedT - dt);
            MissionOverlay.Meter($"SAMAN {_strikes}/{_allowed}  -  had laju {_limit:F0}, ikut lampu isyarat", 1f - _strikes / (float)_allowed,
                MissionOverlay_Color(_strikes));
            if (!P.Driving) return false;
            var d = P.Focus - _pos;
            d.y *= 0.3f;
            return d.magnitude < 9f;
        }

        static Color MissionOverlay_Color(int s) => s == 0 ? new Color(0.35f, 0.7f, 0.3f) : s == 1 ? new Color(0.95f, 0.7f, 0.2f) : new Color(0.85f, 0.2f, 0.16f);

        public override void End(MissionManager m)
        {
            if (_sub) _sub.OnCrash -= OnCrash;
            _sub = null;
            if (_m != null) _m.PedHit -= OnPed;
        }
    }

    /// <summary>
    /// Carry something that spills (teh tarik in a plastic bag, a pot of bubur lambuk, mee hailam soup): crashes, hard
    /// braking, flinging round corners and big landings slop it out. Arrive with something left, before the timer.
    /// </summary>
    public class FragileObjective : Objective
    {
        readonly Vector3 _pos;
        readonly string _cargo;
        readonly Color _colour;
        float _hp, _sample, _splashT, _airVy;
        Vector3 _vel0;
        bool _air, _primed;
        Vehicle _sub;
        public float Integrity => _hp;
        /// <summary>Tests: spill this much now.</summary>
        public void Spill(float amount) => Slop(amount);

        public FragileObjective(Vector3 pos, string text, string cargo, float time, Color colour)
        { _pos = pos; this.text = text; _cargo = cargo; timeLimit = time; _colour = colour; }

        public override Vector3? Target => _pos;
        public override string Progress => $"{_cargo}: {Mathf.CeilToInt(Mathf.Max(0, _hp))}%";

        public override void Begin(MissionManager m)
        {
            _hp = 100f;
            _primed = false;
            _air = false;
        }

        void OnCrash(Vehicle v, Collision c, float rel) => Slop((rel - 3f) * 3.5f);

        void Slop(float amount)
        {
            if (amount <= 0f || failReason != null) return;
            _hp -= amount;
            if (P != null && P.Driving)
                for (int i = 0; i < 3; i++)
                    Fx.Puff(P.vehicle.transform.position + Vector3.up * 1.2f, _colour, Random.insideUnitSphere * 2f + Vector3.up * 2f, 0.3f, 0.8f);
            if (_splashT <= 0f && amount > 2f)
            {
                _splashT = 1.2f;
                if (P != null) Fx.Word(P.Focus + Vector3.up * 2.5f, "TUMPAH!");
                ProcAudio.Play2D(ProcAudio.Bubble, 0.5f, 0.7f);
            }
            if (_hp <= 0f) failReason = $"{_cargo} tumpah habis!";
        }

        public override bool Tick(MissionManager m, float dt)
        {
            _splashT -= dt;
            if (P.vehicle != _sub)
            {
                if (_sub) _sub.OnCrash -= OnCrash;
                _sub = P.vehicle;
                if (_sub) _sub.OnCrash += OnCrash;
                _primed = false;
            }
            if (P.Driving)
            {
                var v = P.vehicle;
                var vel = v.Body.linearVelocity;
                _sample += dt;
                if (!_primed) { _primed = true; _vel0 = vel; _sample = 0f; }
                else if (_sample >= 0.1f)
                {
                    var acc = (vel - _vel0) / _sample;
                    float wasFwd = Vector3.Dot(_vel0, v.transform.forward);
                    _vel0 = vel;
                    float lon = Vector3.Dot(acc, v.transform.forward), lat = Mathf.Abs(Vector3.Dot(acc, v.transform.right));
                    if (lon < -12f && wasFwd > 2f) { Slop(Mathf.Min(25f, (-lon - 12f) * _sample * 2.5f)); MissionOverlay.Hint("Brek perlahan-lahan!"); }
                    if (lat > 15f) { Slop((lat - 15f) * _sample * 2.5f); MissionOverlay.Hint("Belok perlahan!"); }
                    _sample = 0f;
                }
                if (!v.grounded) { _air = true; _airVy = Mathf.Min(_airVy, vel.y); }
                else if (_air) { _air = false; if (_airVy < -4f) Slop((-_airVy - 4f) * 6f); _airVy = 0f; }
            }
            MissionOverlay.Meter($"{_cargo.ToUpper()}  {Mathf.CeilToInt(Mathf.Max(0, _hp))}%  -  jangan brek mengejut", _hp / 100f, MissionLooks.Health(_hp / 100f));
            if (!P.Driving) return false;
            var d = P.Focus - _pos;
            d.y *= 0.3f;
            return d.magnitude < 9f;
        }

        public override void End(MissionManager m)
        {
            if (_sub) _sub.OnCrash -= OnCrash;
            _sub = null;
        }
    }

    /// <summary>
    /// Ram a getaway car a number of times before it reaches where it's going (each solid hit slows it and knocks
    /// something loose). Not a wreck-it mission: what counts is the hits, and the clock is the car's own route.
    /// </summary>
    public class RamObjective : Objective
    {
        readonly string _car, _dest;
        readonly List<Vector3> _stops;
        readonly int _need;
        readonly Dictionary<string, Color> _paint;
        Vehicle _t;
        RouteDriver _d;
        int _hits;
        float _cool, _lost;
        bool _started, _stopped;
        public Vehicle TargetCar => _t;
        public int Hits => _hits;

        public RamObjective(string text, string car, List<Vector3> stops, int hits, string dest, Dictionary<string, Color> paint = null)
        { this.text = text; _car = car; _stops = stops; _need = hits; _dest = dest; _paint = paint; }

        public override Vector3? Target => _t ? _t.transform.position : (Vector3?)null;
        public override float? TargetHealth => 1f - _hits / (float)_need;
        public override string Progress => _lost > 0f ? $"HILANG! {Mathf.CeilToInt(6f - _lost)}" : $"Rempuh: {_hits}/{_need}";

        public override void Begin(MissionManager m)
        {
            _hits = 0; _lost = 0f; _started = false; _stopped = false;
            var route = GameManager.I.City.roads.Route(_stops);
            var dir = route.Count > 1 ? route[1] - route[0] : Vector3.forward;
            dir.y = 0;
            _t = VehicleSpawner.Spawn(_car, route[0] + Vector3.up * 0.6f, Quaternion.LookRotation(dir), VehicleRole.MissionTarget, m.transform);
            if (_paint != null) ModelFactory.Recolor(_t.gameObject, _paint);
            _t.noEnter = true;
            _t.maxHealth = _t.health = 99999f;
            _d = new RouteDriver(route.GetRange(1, route.Count - 1), 14f);
            _d.speedOverride = () =>
            {
                if (!_started || _stopped) return 0f;
                float gap = Vector3.Distance(P.Focus, _t.transform.position);
                return Mathf.Max(6f, 16f - _hits * 1.8f) * (gap > 60f ? 0.6f : 1f);
            };
            _t.driver = _d;
            _t.OnCrash += OnCrash;
            SamanMeter.I.suppressed = true;
        }

        void OnCrash(Vehicle v, Collision c, float rel)
        {
            var other = c.rigidbody ? c.rigidbody.GetComponent<Vehicle>() : null;
            if (other != null && other.PlayerInside && rel > 6f) RegisterHit();
        }

        /// <summary>One solid hit (tests call it directly).</summary>
        public void RegisterHit()
        {
            if (_cool > 0f || _stopped) return;
            _cool = 0.9f;
            _hits++;
            var at = _t.transform.position + Vector3.up * 1.5f;
            Fx.Word(at, _hits >= _need ? "BERHENTI!" : "DUSH!");
            Fx.Burst(at, new Color(0.95f, 0.8f, 0.3f), 10, 6f);
            ChaseCamera.I?.Shake(0.3f);
            HUD.I?.Toast(_hits >= _need ? "Dia dah tersadai!" : $"Rempuh lagi! ({_hits}/{_need})");
        }

        public override bool Tick(MissionManager m, float dt)
        {
            _cool -= dt;
            if (_t == null) { failReason = "Kereta tu dah hilang!"; return false; }
            float d = Vector3.Distance(P.Focus, _t.transform.position);
            if (!_started) { if (d < 50f) { _started = true; HUD.I?.Toast("Dia dah lari! Rempuh dia!"); } return false; }
            if (_hits >= _need)
            {
                _stopped = true;
                Fx.Smoke(_t.transform.position + Vector3.up);
                return true;
            }
            if (d > 110f) _lost += dt; else _lost = 0f;
            if (_lost > 6f) failReason = "Kau hilang jejak!";
            if (_d.Finished) failReason = $"Dia dah sampai {_dest}!";
            MissionOverlay.Hint($"Rempuh {_need - _hits} kali lagi sebelum dia sampai {_dest}");
            return false;
        }

        public override void End(MissionManager m)
        {
            SamanMeter.I.suppressed = false;
            if (_t) { _t.OnCrash -= OnCrash; Object.Destroy(_t.gameObject, 6f); }
        }
    }

    // ===================================================================================================== camera

    /// <summary>A robot mynah sitting somewhere high, watching. Get too close (or noisy) and it flits to another perch.</summary>
    public class PerchedBird : MonoBehaviour
    {
        public List<Vector3> perches;
        public int at;
        public bool Flying => _fly < 1f;
        Vector3 _from, _to;
        float _fly = 1f, _seed;

        public static PerchedBird Spawn(List<Vector3> perches, int at, Transform parent)
        {
            var go = ModelFactory.Spawn("Prop_BurungKamera", perches[at], Quaternion.identity, parent, "PerchedMynah");
            go.transform.localScale = Vector3.one * 2.2f;
            var b = go.AddComponent<PerchedBird>();
            b.perches = perches;
            b.at = at;
            b._to = perches[at];
            b._seed = Random.value * 10f;
            return b;
        }

        public void Spook(Vector3 away)
        {
            if (Flying) return;
            // the perch furthest from what spooked it
            int best = at;
            float bd = -1f;
            for (int i = 0; i < perches.Count; i++)
            {
                if (i == at) continue;
                float d = Vector3.Distance(perches[i], away);
                if (d > bd) { bd = d; best = i; }
            }
            _from = transform.position;
            at = best;
            _to = perches[best];
            _fly = 0f;
            Fx.Feathers(transform.position, new Color(0.3f, 0.3f, 0.35f), 8);
            ProcAudio.Play(ProcAudio.Flutter, transform.position, 0.8f, 1.2f);
            Fx.Word(transform.position + Vector3.up, "KWEK!");
        }

        void Update()
        {
            float t = Time.time + _seed;
            if (_fly < 1f)
            {
                _fly = Mathf.Min(1f, _fly + Time.deltaTime / 2.5f);
                var p = Vector3.Lerp(_from, _to, Mathf.SmoothStep(0, 1, _fly)) + Vector3.up * Mathf.Sin(_fly * Mathf.PI) * 9f;
                var dir = p - transform.position;
                transform.position = p;
                if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z).normalized + Vector3.up * 0.01f);
                return;
            }
            // perched: a bob and a head-turn, a camera eye that follows you
            transform.position = _to + Vector3.up * Mathf.Abs(Mathf.Sin(t * 3f)) * 0.08f;
            var pc = PlayerController.I;
            if (pc != null)
            {
                var to = pc.Focus - transform.position; to.y = 0;
                if (to.sqrMagnitude > 0.1f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 2f);
            }
        }
    }

    /// <summary>
    /// Photograph things (robot mynahs as proof, Datuk Mega handing out cendol). Frame the subject in the middle of the
    /// screen, on foot, at a sensible distance, and hold it steady for a second. Mynahs flit off if you crowd them;
    /// people may have bodyguards who mustn't see you.
    /// </summary>
    public class PhotoObjective : Objective
    {
        public class Subject
        {
            public Transform t;
            public string name;
            public bool done;
            public PerchedBird bird;
            public float height = 0f;
        }

        readonly Action<PhotoObjective, MissionManager> _setup;
        readonly int _need;
        public readonly List<Subject> subjects = new List<Subject>();
        public readonly List<Goon> guards = new List<Goon>();
        public readonly List<GameObject> spawned = new List<GameObject>();
        public float minRange = 5f, maxRange = 40f, spookRange = 9f;
        public string caughtText = "Pengawal nampak kau!";
        float _steady, _sus;
        int _got;
        Subject _aim;
        public int Got => _got;
        public float Suspicion => _sus;

        public PhotoObjective(string text, int need, Action<PhotoObjective, MissionManager> setup)
        { this.text = text; _need = need; _setup = setup; }

        public override string Progress => $"Gambar: {_got}/{_need}";
        public override Vector3? Target
        {
            get
            {
                Subject best = null;
                float bd = float.MaxValue;
                foreach (var s in subjects)
                {
                    if (s.done || s.t == null) continue;
                    float d = (s.t.position - P.Focus).sqrMagnitude;
                    if (d < bd) { bd = d; best = s; }
                }
                return best != null ? best.t.position : (Vector3?)null;
            }
        }

        public override void Begin(MissionManager m)
        {
            End(m);
            _got = 0; _steady = 0f; _sus = 0f;
            _setup(this, m);
        }

        /// <summary>Is subject s framed, in range and in plain sight from the camera?</summary>
        public bool Framed(Subject s, out float dist)
        {
            dist = 0f;
            var cam = Camera.main;
            if (cam == null || s.t == null || (s.bird != null && s.bird.Flying)) return false;
            var p = s.t.position + Vector3.up * s.height;
            dist = Vector3.Distance(P.Focus, p);
            if (dist < minRange || dist > maxRange) return false;
            var vp = cam.WorldToViewportPoint(p);
            if (vp.z <= 0f || Mathf.Abs(vp.x - 0.5f) > 0.22f || Mathf.Abs(vp.y - 0.55f) > 0.28f) return false;
            return !Physics.Linecast(cam.transform.position, p, Goon.SightMask, QueryTriggerInteraction.Ignore);
        }

        public override bool Tick(MissionManager m, float dt)
        {
            // bodyguards: being seen fills the meter
            bool seen = false;
            foreach (var g in guards)
            {
                if (g == null || g.Down) continue;
                bool s = g.CanSee(P.Focus, P.Driving ? 1.4f : 1f);
                g.alert = s;
                if (s) { seen = true; g.FaceTowards(P.transform.position); }
            }
            if (guards.Count > 0)
            {
                _sus = Mathf.Clamp01(_sus + (seen ? dt * 0.55f : -dt * 0.25f));
                MissionOverlay.Meter("PENGAWAL DATUK " + (seen ? "- DIA NAMPAK KAU!" : ""), _sus, new Color(0.85f, 0.2f, 0.16f));
                if (_sus >= 1f) { failReason = caughtText; return false; }
            }

            // birds flit off when you crowd them
            foreach (var s in subjects)
            {
                if (s.done || s.bird == null || s.bird.Flying) continue;
                float d = Vector3.Distance(P.Focus, s.t.position);
                bool noisy = P.Driving ? d < spookRange * 2.4f : P.Sprinting ? d < spookRange * 1.6f : d < spookRange;
                if (noisy) { s.bird.Spook(P.Focus); HUD.I?.Toast("Burung tu terbang! Jangan dekat sangat."); }
            }

            if (P.Driving)
            {
                _steady = 0f;
                MissionOverlay.Hint("Turun dari kereta untuk ambil gambar");
                return _got >= _need;
            }
            Subject aim = null;
            float aimDist = 0f;
            foreach (var s in subjects)
                if (!s.done && Framed(s, out var d)) { aim = s; aimDist = d; break; }
            if (aim != null)
            {
                if (aim != _aim) _steady = 0f;
                _aim = aim;
                _steady += dt;
                MissionOverlay.Viewfinder(_steady, $"{aim.name}  {aimDist:F0} m  -  tahan...");
                if (_steady >= 1f) Snap(aim);
            }
            else
            {
                _aim = null;
                _steady = Mathf.Max(0f, _steady - dt * 2f);
                var t = Target;
                if (t.HasValue)
                {
                    float d = Vector3.Distance(t.Value, P.Focus);
                    if (d < maxRange + 10f)
                    {
                        MissionOverlay.Viewfinder(0f, d < minRange ? "Terlalu dekat! Undur sikit" : "Pusing kamera - letak sasaran di tengah");
                    }
                }
            }
            return _got >= _need;
        }

        public void Snap(Subject s)
        {
            s.done = true;
            _got++;
            _steady = 0f;
            MissionOverlay.Flash();
            ProcAudio.Play2D(ProcAudio.Blip, 0.8f, 2.2f);
            Fx.Word(s.t.position + Vector3.up * 1.5f, "KLIK!");
            HUD.I?.Toast($"Gambar {s.name}! ({_got}/{_need})");
            if (s.bird != null) { Object.Destroy(s.bird.gameObject, 0.4f); }
        }

        public override void End(MissionManager m)
        {
            foreach (var s in subjects) if (s.bird) Object.Destroy(s.bird.gameObject);
            foreach (var g in guards) if (g) Object.Destroy(g.gameObject);
            foreach (var go in spawned) if (go) Object.Destroy(go);
            subjects.Clear();
            guards.Clear();
            spawned.Clear();
        }
    }

    // ===================================================================================================== talk

    /// <summary>
    /// Tawar-menawar: haggle a stallholder down to what you can pay. Each round pick a line: sweet talk keeps them
    /// happy, a bluff only lands when they're in a good mood, a lowball works once, pretending to walk away works
    /// when you're close. Run out of their patience and you're chased off; accept above budget and you can't pay.
    /// </summary>
    public class HaggleObjective : Objective
    {
        readonly string _npcKey, _item;
        readonly int _start, _budget;
        NPC _npc;
        int _price, _patience, _round;
        bool _open, _done, _sweetUsed, _lowUsed;
        public int Price => _price;
        public int Patience => _patience;
        public bool Open => _open;
        const int MaxPatience = 5;

        public HaggleObjective(string npcKey, string text, string item, int startPrice, int budget)
        { _npcKey = npcKey; this.text = text; _item = item; _start = startPrice; _budget = budget; }

        public override Vector3? Target => !_open && _npc ? _npc.transform.position : (Vector3?)null;
        public override string Progress => _open ? $"{_item}: RM{_price}  (bajet RM{_budget})   Sabar: {new string('*', Mathf.Max(0, _patience))}" : null;

        string Who => _npc ? _npc.displayName : "Peniaga";

        public override void Begin(MissionManager m)
        {
            _price = _start; _patience = 4; _round = 0;
            _open = _done = _sweetUsed = _lowUsed = false;
            _npc = m.Npc(_npcKey);
            if (_npc == null) { _done = true; return; }
            _npc.hasMission = true;
            _npc.onTalk = _ => OpenHaggle();
        }

        /// <summary>Start haggling (talking to the stallholder does this).</summary>
        public void OpenHaggle()
        {
            if (_open) return;
            _open = true;
            _npc.hasMission = false;
            Dialogue.Say(new[]
            {
                new Line(Who, $"{_item}? RM{_start}. Harga MegaMaju, bukan aku yang naikkan."),
                new Line("", $"Bajet kau RM{_budget}. Tawar sampai dapat!"),
            }, Round);
        }

        void Round()
        {
            if (_done || failReason != null || HUD.I == null) return;
            int low = Mathf.Max(1, _budget - 3);
            HUD.I.Menu($"TAWAR: {_item} RM{_price}  (bajet RM{_budget})", new List<string>
            {
                "\"Kak, cantiknya kakak hari ni...\"",
                $"\"Kedai hujung sana jual RM{_budget} je tau.\"",
                $"\"RM{low}! Mahal gila la!\"",
                "\"Takpe la, saya cari kedai lain...\"",
                $"\"Okay, deal. RM{_price}.\"",
            }, Choose, () => Choose(3));
        }

        /// <summary>Pick option i (0 sweet talk, 1 bluff, 2 lowball, 3 walk away, 4 accept). Tests call it directly.</summary>
        public void Choose(int i)
        {
            if (_done || failReason != null) return;
            if (HUD.I != null && HUD.I.MenuOpen) HUD.I.CloseMenu();
            _round++;
            int gap = Mathf.Max(0, _price - _budget);
            string reply;
            switch (i)
            {
                case 0:
                    if (_sweetUsed) { reply = "Ayat sama je dari tadi. Bosan la."; _patience -= 1; }
                    else { reply = "Hehe, pandai mulut kau. Okay, kurang sikit."; _patience = Mathf.Min(MaxPatience, _patience + 1); _price -= Mathf.Max(1, gap / 8); _sweetUsed = true; }
                    break;
                case 1:
                    if (_patience >= 3) { reply = "Ish... yelah, yelah. Aku tak nak kalah dengan kedai hujung tu."; _price -= Mathf.Max(1, Mathf.RoundToInt(gap * 0.4f)); _patience -= 1; _sweetUsed = false; }
                    else { reply = "Tipu! Kedai hujung tu adik ipar aku punya!"; _patience -= 2; }
                    break;
                case 2:
                    if (!_lowUsed && _patience >= 3) { reply = "Ya Allah, rugi aku macam ni! ...okay, aku turunkan sikit."; _price -= Mathf.Max(1, gap / 2); _patience -= 2; _lowUsed = true; }
                    else { reply = "Kau nak aku tutup kedai ke?!"; _patience -= 2; }
                    break;
                case 3:
                    if (gap <= 3 || _patience >= 5) { reply = $"Eh, eh, nanti dulu! Okay la, RM{_budget}. Untuk kau je."; _price = Mathf.Min(_price, _budget); }
                    else { reply = "Pergi la. Ramai lagi nak beli."; _patience -= 1; }
                    break;
                default:
                    if (_price <= _budget) { _done = true; reply = $"Deal! RM{_price}. Datang lagi ya!"; }
                    else { failReason = $"Duit tak cukup! Bajet RM{_budget} je."; reply = "Mana duitnya?"; }
                    break;
            }
            _price = Mathf.Max(_price, 1);
            if (!_done && failReason == null && _price <= _budget) { _done = true; reply += $" Okay, RM{_price}. Ambil la!"; }
            if (!_done && failReason == null && _patience <= 0) failReason = $"{Who} dah hilang sabar dan halau kau!";
            if (!_done && failReason == null && _round >= 9) failReason = $"{Who} dah tutup kedai. Lama sangat!";
            Barks.Say(_npc ? _npc.transform : null, _npcKey, reply, false);
            if (_done)
            {
                ProcAudio.Play2D(ProcAudio.Coin, 0.7f);
                Dialogue.Say(new[] { new Line(Who, reply) });
            }
            else if (failReason == null) Dialogue.Say(new[] { new Line(Who, reply) }, Round);
        }

        public override bool Tick(MissionManager m, float dt)
        {
            if (_open && !_done) MissionOverlay.Meter($"SABAR {Who.ToUpper()}", _patience / (float)MaxPatience, MissionLooks.Health(_patience / (float)MaxPatience));
            return _done && !Dialogue.Showing;
        }

        public override void End(MissionManager m)
        {
            if (_npc) { _npc.hasMission = false; _npc.onTalk = null; }
            if (HUD.I != null && HUD.I.MenuOpen && _open && !_done) HUD.I.CloseMenu();
        }
    }

    // ===================================================================================================== stealth

    /// <summary>One guard of a stealth section: where they walk, how far they see, what they say.</summary>
    public class GuardSpec
    {
        public Vector3[] route;
        public string model = "chr_townman", name = "Pengawal";
        public Dictionary<string, Color> paint = MissionLooks.Suit;
        public float range = 11f, halfAngle = 48f, speed = 1.3f, pause = 2f;
        public string[] chatter = { "Bos suruh jaga...", "Cendol ni bau pelik.", "Bila nak balik ni?" };
        public string spotLine = "Siapa tu?!";
    }

    /// <summary>
    /// Sneak in on foot: MegaMaju guards walk their beats with a painted sight cone. Their seeing you fills the
    /// suspicion meter (fast when you sprint or stand close; slowly when you're in disguise); full is KANTOI. Unaware
    /// guards can be knocked out from behind. Reach each goal (any order); some need you to stand there a moment.
    /// </summary>
    public class SneakObjective : Objective
    {
        readonly List<Vector3> _goals;
        readonly string _goalModel, _holdVerb;
        readonly float _hold;
        readonly List<GuardSpec> _specs;
        public Dictionary<string, Color> disguise;
        public string disguiseName, caughtText = "KANTOI! MegaMaju nampak kau.";
        public bool takedowns = true;
        /// <summary>Set dressing for the scene (a parked van, a stack of crates); torn down with the step.</summary>
        public Action<MissionManager, List<GameObject>> dressing;
        readonly List<GameObject> _dress = new List<GameObject>();
        public readonly List<Goon> guards = new List<Goon>();
        readonly List<GameObject> _goalObjs = new List<GameObject>();
        readonly List<bool> _gotFlags = new List<bool>();
        int _got;
        float _sus, _holdT, _shoutT;
        bool _disguised;
        public float Suspicion => _sus;
        public int Got => _got;
        public IReadOnlyList<Vector3> Goals => _goals;
        public bool Taken(int i) => i < _gotFlags.Count && _gotFlags[i];

        public SneakObjective(string text, List<Vector3> goals, string goalModel, List<GuardSpec> guards, float hold = 0f, string holdVerb = "Ambil")
        { this.text = text; _goals = goals; _goalModel = goalModel; _specs = guards; _hold = hold; _holdVerb = holdVerb; }

        public override string Progress => _goals.Count > 1 ? $"{_got}/{_goals.Count}" : null;
        public override Vector3? Target
        {
            get
            {
                int bi = -1;
                float bd = float.MaxValue;
                for (int i = 0; i < _goals.Count; i++)
                {
                    if (_gotFlags[i]) continue;
                    float d = (_goals[i] - P.Focus).sqrMagnitude;
                    if (d < bd) { bd = d; bi = i; }
                }
                return bi >= 0 ? _goals[bi] : (Vector3?)null;
            }
        }

        public override void Begin(MissionManager m)
        {
            End(m);
            _got = 0; _sus = 0f; _holdT = 0f;
            _gotFlags.Clear();
            foreach (var g in _goals)
            {
                _gotFlags.Add(false);
                GameObject go = null;
                if (!string.IsNullOrEmpty(_goalModel))
                {
                    go = ModelFactory.Spawn(_goalModel, g + Vector3.up * 0.05f, Quaternion.Euler(0, Random.Range(0, 360), 0), m.transform, "SneakGoal");
                    go.AddComponent<MissionSpin>();
                }
                _goalObjs.Add(go);
            }
            foreach (var s in _specs)
            {
                var r = s.route;
                var yaw = r.Length > 1 ? Quaternion.LookRotation(Flat(r[1] - r[0]) + Vector3.forward * 0.001f).eulerAngles.y : 0f;
                var g = Goon.Spawn(s.model, s.name, r[0], yaw, m.transform, s.paint);
                g.mode = r.Length > 1 ? Goon.Mode.Patrol : Goon.Mode.Idle;
                g.points.AddRange(r);
                g.speed = s.speed;
                g.pause = s.pause;
                g.sightRange = s.range;
                g.sightHalfAngle = s.halfAngle;
                g.hp = 2;
                g.chatter = s.chatter;
                g.chatterEvery = 9f;
                var spec = s;
                g.onHit = (gg, dir, f) => OnGuardHit(gg, spec);
                g.ShowCone();
                guards.Add(g);
            }
            dressing?.Invoke(m, _dress);
            if (disguise != null && P != null)
            {
                var rig = P.GetComponentInChildren<CharacterRig>();
                if (rig) ModelFactory.Recolor(rig.gameObject, disguise);
                _disguised = true;
                HUD.I?.Toast($"Menyamar: {disguiseName}. Jalan biasa je - jangan lari!");
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void OnGuardHit(Goon g, GuardSpec s)
        {
            var to = Flat(P.transform.position - g.transform.position);
            bool behind = Vector3.Dot(g.transform.forward, to.normalized) < 0.2f;
            if (takedowns && !g.alert && behind)
            {
                g.KnockDown(Flat(g.transform.forward) * 1.5f + Vector3.up * 2f);
                g.HideCone();
                Fx.Word(g.transform.position + Vector3.up * 2f, "SENYAP!");
                HUD.I?.Toast("Pengawal pengsan. Senyap-senyap!");
            }
            else
            {
                _sus = Mathf.Min(1f, _sus + 0.45f);
                g.alert = true;
                g.Say(s.spotLine, 1f);
            }
        }

        public override bool Tick(MissionManager m, float dt)
        {
            _shoutT -= dt;
            bool seen = false;
            foreach (var g in guards)
            {
                if (g == null || g.Down) continue;
                float d = Vector3.Distance(g.Eye, P.Focus);
                bool loud = P.Driving || P.Sprinting;
                // noise: running (or an engine) close behind makes them turn round
                if (loud && d < 7f) g.FaceTowards(P.transform.position);
                bool s = g.CanSee(P.Focus, P.Driving ? 1.6f : P.Sprinting ? 1.25f : 1f);
                g.alert = s;
                if (!s) continue;
                seen = true;
                float rate = 0.45f + 1.4f * Mathf.Clamp01(1f - d / g.sightRange);
                if (loud) rate *= 2f;
                if (_disguised && !loud && d > 2.6f) rate *= 0.1f;     // just another MegaMaju worker...
                _sus += rate * dt;
                g.FaceTowards(P.transform.position);
                if (_shoutT <= 0f && _sus > 0.45f) { _shoutT = 4f; g.Say(_disguised ? "Eh, kau ni pekerja baru ke?" : "Hoi! Siapa tu?", 1f); }
            }
            if (!seen) _sus = Mathf.Max(0f, _sus - dt * 0.22f);
            _sus = Mathf.Min(1f, _sus);
            MissionOverlay.Meter(seen ? "SYAK WASANGKA - DIA NAMPAK KAU!" : "SYAK WASANGKA", _sus, new Color(0.85f, 0.2f, 0.16f));
            if (_sus >= 1f) { failReason = caughtText; return false; }
            if (P.Sprinting && !P.Driving) MissionOverlay.Hint("Lari = bising. Jalan perlahan dekat pengawal.");
            else if (_disguised) MissionOverlay.Hint($"Menyamar sebagai {disguiseName}: jangan lari, jangan rapat.");
            else MissionOverlay.Hint("Elak kon kuning. Tumbuk dari belakang untuk pengsankan pengawal.");

            // goals
            bool atGoal = false;
            for (int i = 0; i < _goals.Count; i++)
            {
                if (_gotFlags[i]) continue;
                var d = P.Focus - Vector3.up - _goals[i];
                d.y *= 0.4f;
                if (P.Driving || d.magnitude > 1.9f) continue;
                atGoal = true;
                _holdT += dt;
                if (_hold > 0f) MissionOverlay.Viewfinder(_holdT / _hold, $"{_holdVerb}... jangan bergerak");
                if (_holdT >= _hold) Collect(i);
                break;
            }
            if (!atGoal) _holdT = 0f;
            return _got >= _goals.Count;
        }

        /// <summary>Take goal i (tests call it directly).</summary>
        public void Collect(int i)
        {
            if (_gotFlags[i]) return;
            _gotFlags[i] = true;
            _got++;
            _holdT = 0f;
            if (_goalObjs[i]) { Fx.Burst(_goalObjs[i].transform.position + Vector3.up * 0.5f, new Color(0.95f, 0.85f, 0.4f), 8, 4f); Object.Destroy(_goalObjs[i]); }
            ProcAudio.Play2D(ProcAudio.Coin, 0.6f, 1.2f);
            HUD.I?.Toast(_goals.Count > 1 ? $"{_holdVerb}! ({_got}/{_goals.Count})" : $"{_holdVerb}!");
        }

        public override void End(MissionManager m)
        {
            foreach (var g in guards) if (g) Object.Destroy(g.gameObject);
            guards.Clear();
            foreach (var o in _goalObjs) if (o) Object.Destroy(o);
            _goalObjs.Clear();
            foreach (var o in _dress) if (o) Object.Destroy(o);
            _dress.Clear();
            if (_disguised && P != null && !P.Driving) P.SetCharacter(P.def.id);
            _disguised = false;
        }
    }

    /// <summary>
    /// Search the rooms round a place for something hidden: every candidate shop or house wears a small beacon, but
    /// only one has it. Step inside and look round for a couple of seconds; after two misses the locals start pointing.
    /// </summary>
    public class SearchObjective : Objective
    {
        class Cand { public RoomSpec room; public Vector3 door, inside; public bool searched; public GameObject beacon; }

        readonly Vector3 _center;
        readonly float _radius;
        readonly int _count, _seed;
        readonly Predicate<InteriorKind> _kinds;
        readonly string _item, _model;
        readonly string[] _misses;
        readonly List<Cand> _c = new List<Cand>();
        int _prize, _missed;
        float _searchT, _foundT;
        bool _found, _hinted;
        GameObject _itemGo;
        const float SearchTime = 2.2f;
        public int Candidates => _c.Count;
        public bool Found => _found;

        static readonly string[] DefaultMisses =
        {
            "Tak ada. Cuma kain pelikat dengan kalendar lama.", "Kosong. Ada kucing tidur je.", "Bukan sini. Tauke pandang pelik.",
            "Tak jumpa. Bau minyak angin je.", "Tiada apa-apa... kecuali stok Milo 1998.",
        };

        public SearchObjective(string text, Vector3 center, float radius, int count, Predicate<InteriorKind> kinds, string item, string model, int seed, string[] misses = null)
        { this.text = text; _center = center; _radius = radius; _count = count; _kinds = kinds; _item = item; _model = model; _seed = seed; _misses = misses ?? DefaultMisses; }

        public override string Progress => _found ? "JUMPA!" : $"Dah geledah: {_c.FindAll(c => c.searched).Count}/{_c.Count}";
        public override Vector3? Target
        {
            get
            {
                Cand best = null;
                float bd = float.MaxValue;
                foreach (var c in _c)
                {
                    if (c.searched) continue;
                    float d = (c.door - P.Focus).sqrMagnitude;
                    if (d < bd) { bd = d; best = c; }
                }
                return best != null ? best.door : (Vector3?)null;
            }
        }

        public override void Begin(MissionManager m)
        {
            End(m);
            _found = _hinted = false;
            _missed = 0; _searchT = 0f; _foundT = 0f;
            var rooms = Interiors.I != null ? Interiors.I.Rooms : null;
            var pool = new List<Cand>();
            if (rooms != null)
                foreach (var r in rooms)
                {
                    if (!_kinds(r.kind)) continue;
                    if ((r.Centre - _center).sqrMagnitude > _radius * _radius) continue;
                    bool hasDoor = false;
                    float mid = 0f;
                    foreach (var o in r.openings) if (o.door && o.wall == 0) { hasDoor = true; mid = (o.c0 + o.c1) * 0.5f; break; }
                    if (!hasDoor) continue;
                    pool.Add(new Cand { room = r, door = r.f.P(mid, 0.2f, 1.2f), inside = r.f.P(r.W * 0.5f, 0.2f, -r.D * 0.5f) });
                }
            pool.Sort((a, b) => (a.room.Centre - _center).sqrMagnitude.CompareTo((b.room.Centre - _center).sqrMagnitude));
            foreach (var c in pool)
            {
                bool clash = false;
                foreach (var k in _c) if (Vector3.Distance(k.door, c.door) < 6f) clash = true;
                if (clash) continue;
                _c.Add(c);
                if (_c.Count >= _count) break;
            }
            if (_c.Count < 2)
            {
                // no walk-in rooms here (interiors off): search spots on the pavement instead
                _c.Clear();
                var rng = new System.Random(_seed);
                foreach (var s in GameManager.I.City.itemSpots)
                    if ((s - _center).sqrMagnitude < _radius * _radius && rng.NextDouble() < 0.5 && _c.Count < _count)
                        _c.Add(new Cand { door = s, inside = s });
            }
            var rnd = new System.Random(_seed);
            _prize = _c.Count > 1 ? 1 + rnd.Next(_c.Count - 1) : 0;     // never the nearest one to where the job starts
            foreach (var c in _c) c.beacon = m.Beacon(c.door, 0.3f);
        }

        Cand Here()
        {
            var p = P.transform.position;
            foreach (var c in _c)
            {
                if (c.searched) continue;
                if (c.room != null ? c.room.Contains(p, -0.1f) : Vector3.Distance(p, c.inside) < 2.2f) return c;
            }
            return null;
        }

        public override bool Tick(MissionManager m, float dt)
        {
            if (_found) { _foundT += dt; return _foundT > 1f; }
            var c = P.Driving ? null : Here();
            if (c != null)
            {
                _searchT += dt;
                MissionOverlay.Viewfinder(_searchT / SearchTime, "Menggeledah...");
                if (_searchT >= SearchTime) Search(c);
            }
            else
            {
                _searchT = 0f;
                MissionOverlay.Hint(P.Driving ? "Turun dan masuk ke dalam kedai/rumah untuk menggeledah" : $"Cari {_item}: masuk setiap kedai/rumah yang bertanda");
            }
            return false;
        }

        /// <summary>Search candidate i (tests call it directly; i = -1 searches the right one).</summary>
        public void DebugSearch(int i) => Search(i < 0 ? _c[_prize] : _c[i]);
        public Vector3 DoorOf(int i) => _c[i].door;
        public Vector3 InsideOf(int i) => _c[i].inside;
        public int PrizeIndex => _prize;

        void Search(Cand c)
        {
            c.searched = true;
            _searchT = 0f;
            if (c.beacon) Object.Destroy(c.beacon);
            if (_c.IndexOf(c) == _prize)
            {
                _found = true;
                var at = P.transform.position + P.transform.forward * 0.8f + Vector3.up * 0.9f;
                if (!string.IsNullOrEmpty(_model)) { _itemGo = ModelFactory.Spawn(_model, at, Quaternion.identity, m_root, "Found"); _itemGo.AddComponent<MissionSpin>(); }
                Fx.Burst(at, new Color(1f, 0.85f, 0.3f), 14, 5f);
                Fx.Word(at + Vector3.up, "JUMPA!");
                ProcAudio.Play2D(ProcAudio.Fanfare, 0.5f, 1.3f);
                HUD.I?.Toast($"JUMPA! {_item}");
                return;
            }
            _missed++;
            HUD.I?.Toast(_misses[(_missed - 1) % _misses.Length]);
            ProcAudio.Play2D(ProcAudio.Blip, 0.4f, 0.7f);
            if (_missed >= 2 && !_hinted && _prize < _c.Count)
            {
                _hinted = true;
                var dir = MissionLooks.Compass(P.transform.position, _c[_prize].door);
                Dialogue.Say(new[] { new Line("Orang Kampung", $"Cari {_item}? Tadi aku nampak orang MegaMaju keluar masuk sebelah {dir} sana.") });
                if (_c[_prize].beacon) _c[_prize].beacon.transform.localScale = new Vector3(4f, 30f, 4f);
            }
        }

        Transform m_root => GameManager.I != null && GameManager.I.Missions != null ? GameManager.I.Missions.transform : null;

        public override void End(MissionManager m)
        {
            foreach (var c in _c) if (c.beacon) Object.Destroy(c.beacon);
            _c.Clear();
            if (_itemGo) Object.Destroy(_itemGo, 1.5f);
        }
    }

    /// <summary>
    /// The police are after you and won't give up on the open road: the meter only drains while you're hidden in a
    /// shop or house with no patrol car close by. Getting caught isn't escaping.
    /// </summary>
    public class HideObjective : Objective
    {
        float _scan, _nearPolice = 999f, _lastHeat;
        Vector3? _door;
        bool _hidden;
        public bool Hidden => _hidden;

        public HideObjective(string text) { this.text = text; }
        public override Vector3? Target => _door;

        public override void Begin(MissionManager m)
        {
            SamanMeter.I.ForceWanted();
            _lastHeat = 100f;
            _scan = 0f;
            _door = null;
        }

        public override bool Tick(MissionManager m, float dt)
        {
            var sm = SamanMeter.I;
            if (!sm.Wanted)
            {
                if (_lastHeat > 25f) { failReason = "Kena tangkap! Itu bukan menyorok."; return false; }
                return true;
            }
            if ((_scan -= dt) <= 0f)
            {
                _scan = 0.5f;
                _nearPolice = 999f;
                foreach (var v in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
                    if (v.role == VehicleRole.Police) _nearPolice = Mathf.Min(_nearPolice, Vector3.Distance(v.transform.position, P.Focus));
                var r = Interiors.I != null ? Interiors.I.Nearest(P.transform.position, x => HasDoor(x)) : null;
                if (r != null && (r.Centre - P.Focus).sqrMagnitude < 90f * 90f) _door = DoorOf(r); else _door = null;
            }
            _hidden = !P.Driving && Interiors.RoomAt(P.transform.position) != null && _nearPolice > 10f;
            if (_hidden)
            {
                sm.heat -= 16f * dt;
                MissionOverlay.Hint("Menyorok... jangan keluar dulu!");
                _door = null;
            }
            else
            {
                if (sm.heat < 65f) sm.heat = 65f;     // out on the street they keep finding you
                MissionOverlay.Hint(_nearPolice <= 10f && !P.Driving && Interiors.RoomAt(P.transform.position) != null
                    ? "Polis terlalu dekat - mereka nampak kau masuk!" : "Masuk ke dalam kedai atau rumah untuk menyorok!");
            }
            _lastHeat = sm.heat;
            MissionOverlay.Meter(_hidden ? "POLIS CARI KAU - menyorok" : "POLIS CARI KAU", sm.heat / 100f, new Color(0.25f, 0.35f, 0.8f));
            return false;
        }

        public override void End(MissionManager m)
        {
            // only a skipped or abandoned step leaves the chase running
            if (SamanMeter.I != null && SamanMeter.I.Wanted && failReason == null) SamanMeter.I.ClearAll();
        }

        static bool HasDoor(RoomSpec r) { foreach (var o in r.openings) if (o.door && o.wall == 0) return true; return false; }
        static Vector3 DoorOf(RoomSpec r)
        {
            foreach (var o in r.openings) if (o.door && o.wall == 0) return r.f.P((o.c0 + o.c1) * 0.5f, 0.2f, 1.2f);
            return r.Centre;
        }
    }

    // ===================================================================================================== on foot

    /// <summary>
    /// Walk someone across town on foot (Adik to the surau): they follow you at their own pace and won't get in a car,
    /// cars on the road can knock them down, and MegaMaju promoters keep trying to press free cendol on them -
    /// punch the promoters away before they get close. Three slips and the mission's lost.
    /// </summary>
    public class EscortObjective : Objective
    {
        readonly Vector3 _dest;
        readonly string _model, _name;
        readonly int _waves;
        int _hearts, _spawned;
        float _spawnT, _farT, _waitT;
        public Goon Ward { get; private set; }
        public readonly List<Goon> promoters = new List<Goon>();
        public int Hearts => _hearts;
        public Vector3 Dest => _dest;

        public EscortObjective(string text, string model, string name, Vector3 dest, int promoterCount)
        { this.text = text; _model = model; _name = name; _dest = dest; _waves = promoterCount; }

        public override Vector3? Target => Ward && Vector3.Distance(Ward.transform.position, P.Focus) > 16f ? Ward.transform.position : _dest;
        public override string Progress => $"{_name}: {new string('*', Mathf.Max(0, _hearts))}";

        public override void Begin(MissionManager m)
        {
            End(m);
            _hearts = 3; _spawned = 0; _spawnT = 6f; _farT = 0f;
            var at = P.transform.position + P.transform.right * 1.5f;
            Ward = Goon.Spawn(_model, _name, at, P.transform.eulerAngles.y, m.transform);
            Ward.mode = Goon.Mode.Follow;
            Ward.seek = P.transform;
            Ward.stopDist = 2.2f;
            Ward.speed = 3.6f;
            Ward.invulnerable = true;
            Ward.downTime = 2f;
            Ward.chatter = new[] { "Along, nak aiskrim...", "Jauh lagi ke?", "Adik penat...", "Along jalan laju sangat!" };
            Ward.chatterEvery = 10f;
            Ward.onHit = (g, d, f) => g.Say("Aduh! Along jahat! Adik bagitau Mak!", 1f);
            Ward.onRunOver = (g, v) => Slip($"{_name} kena langgar! Hati-hati lintas jalan!", true);
        }

        void Slip(string why, bool knock)
        {
            if (failReason != null) return;
            _hearts--;
            HUD.I?.Toast(why);
            ProcAudio.Play2D(ProcAudio.Fail, 0.4f, 1.2f);
            if (knock && Ward) { Ward.invulnerable = false; Ward.KnockDown(Vector3.up * 4f + Ward.transform.forward * -2f); Ward.invulnerable = true; }
            if (_hearts <= 0) failReason = $"{_name} dah minum Cendol Ajaib... misi gagal.";
        }

        public override bool Tick(MissionManager m, float dt)
        {
            if (Ward == null) { failReason = $"{_name} hilang!"; return false; }
            float d = Vector3.Distance(Ward.transform.position, P.transform.position);
            if (P.Driving)
            {
                Ward.mode = Goon.Mode.Idle;
                MissionOverlay.Hint($"{_name} tak nak naik kereta. Jalan kaki!");
            }
            else if (d > 14f)
            {
                Ward.mode = Goon.Mode.Idle;
                if ((_waitT -= dt) <= 0f) { _waitT = 5f; Ward.Say("Along! Tunggu la!", 1f); }
                MissionOverlay.Hint($"Jangan tinggal {_name}!");
            }
            else { Ward.mode = Goon.Mode.Follow; MissionOverlay.Hint("Tumbuk promoter cendol sebelum dia sampai. Lintas di zebra!"); }
            if (d > 40f) _farT += dt; else _farT = 0f;
            if (_farT > 8f) { failReason = $"{_name} sesat!"; return false; }

            // the promoters
            _spawnT -= dt;
            promoters.RemoveAll(g => g == null);
            int alive = promoters.FindAll(g => !g.Down).Count;
            if (_spawned < _waves && alive < 2 && _spawnT <= 0f && d < 16f)
            {
                _spawnT = 7f;
                _spawned++;
                var fwd = (_dest - Ward.transform.position); fwd.y = 0;
                var around = Ward.transform.position + Quaternion.Euler(0, Random.Range(-70f, 70f), 0) * (fwd.sqrMagnitude > 1 ? fwd.normalized : Vector3.forward) * 18f;
                var at = MissionLooks.Open(around, 4f, Random.Range(0, 9999));
                var g = Goon.Spawn("chr_townman", "Promoter", at, 0f, m.transform, MissionLooks.Promoter);
                g.mode = Goon.Mode.Seek;
                g.seek = Ward.transform;
                g.stopDist = 0.8f;
                g.speed = 2.3f;
                g.hp = 1;
                g.chatter = new[] { "Adik! Cendol percuma!", "Minum sikit je, sedap!", "Cendol Ajaib! Free!" };
                g.chatterEvery = 4f;
                g.onDown = gg => { Fx.Word(gg.transform.position + Vector3.up * 2f, "PERGI!"); Object.Destroy(gg.gameObject, 4f); };
                promoters.Add(g);
                HUD.I?.Toast("Promoter cendol datang!");
            }
            foreach (var g in promoters)
            {
                if (g == null || g.Down) continue;
                if (Vector3.Distance(g.transform.position, Ward.transform.position) < 1.3f)
                {
                    Fx.Puff(Ward.transform.position + Vector3.up * 1.2f, new Color(0.45f, 0.75f, 0.42f), Vector3.up, 0.5f, 1f);
                    Ward.Say("Hehe... cendol... sedap...", 1f);
                    Object.Destroy(g.gameObject);
                    Slip($"{_name} terminum Cendol Ajaib!", false);
                }
            }
            MissionOverlay.Meter(_name.ToUpper(), _hearts / 3f, new Color(0.9f, 0.45f, 0.6f));
            return Vector3.Distance(Ward.transform.position, _dest) < 6f;
        }

        public override void End(MissionManager m)
        {
            if (Ward) Object.Destroy(Ward.gameObject);
            Ward = null;
            foreach (var g in promoters) if (g) Object.Destroy(g.gameObject);
            promoters.Clear();
        }
    }

    /// <summary>
    /// Hold a spot (a pot of bubur lambuk, Aunty's stall) against a cendol-crazed crowd: they shuffle in from all sides
    /// to tip Cendol Ajaib into it. Punch or kick them and they come to their senses. Last out the clock.
    /// </summary>
    public class DefendObjective : Objective
    {
        readonly Vector3 _center;
        readonly float _duration;
        readonly string _prop, _what;
        float _hp, _t, _spawnT;
        int _cured;
        GameObject _propGo;
        public readonly List<Goon> crowd = new List<Goon>();
        public float Health => _hp;
        public float Remaining => _duration - _t;
        public int Cured => _cured;

        public DefendObjective(string text, Vector3 center, float duration, string what, string propModel)
        { this.text = text; _center = center; _duration = duration; _what = what; _prop = propModel; }

        public override Vector3? Target
        {
            get
            {
                Goon best = null;
                float bd = float.MaxValue;
                foreach (var g in crowd)
                {
                    if (g == null || g.Down || g.tag != 0) continue;
                    float d = (g.transform.position - _center).sqrMagnitude;
                    if (d < bd) { bd = d; best = g; }
                }
                return best ? best.transform.position : _center;
            }
        }
        public override string Progress => $"Tahan lagi {Mathf.CeilToInt(Mathf.Max(0, Remaining))}s   Sedar: {_cured}";

        public override void Begin(MissionManager m)
        {
            End(m);
            _hp = 100f; _t = 0f; _spawnT = 2f; _cured = 0;
            if (!string.IsNullOrEmpty(_prop))
            {
                _propGo = ModelFactory.Spawn(_prop, Goon.Ground(_center), Quaternion.identity, m.transform, "Defend_" + _what);
                if (_prop == "Prop_TehTarik") _propGo.transform.localScale = Vector3.one * 3.2f;
            }
        }

        public override bool Tick(MissionManager m, float dt)
        {
            _t += dt;
            _spawnT -= dt;
            crowd.RemoveAll(g => g == null);
            int alive = crowd.FindAll(g => !g.Down && g.tag == 0).Count;
            if (_spawnT <= 0f && alive < 6 && _t < _duration - 4f)
            {
                _spawnT = Mathf.Max(1.4f, 3.4f - _t * 0.04f);
                var a = Random.Range(0f, 360f);
                var at = MissionLooks.Open(_center + Quaternion.Euler(0, a, 0) * Vector3.forward * 22f, 5f, Random.Range(0, 99999));
                if ((at - _center).sqrMagnitude < 8f * 8f) at = _center + Quaternion.Euler(0, a, 0) * Vector3.forward * 20f;
                var g = Goon.Spawn(Random.value < 0.5f ? "chr_townman" : "chr_townaunty", "Zombi", at, a + 180f, m.transform, MissionLooks.Zombie);
                g.mode = Goon.Mode.Seek;
                g.seekPoint = _center;
                g.stopDist = 0.5f;
                g.speed = 1.3f + _t * 0.012f;
                g.hp = 1;
                g.downTime = 2.5f;
                g.chatter = MissionLooks.ZombieMoans;
                g.chatterEvery = 5f;
                g.onDown = gg =>
                {
                    if (gg.tag != 0) return;
                    gg.tag = 1;
                    _cured++;
                    Fx.Word(gg.transform.position + Vector3.up * 2f, "SEDAR!");
                    ModelFactory.Recolor(gg.gameObject, new Dictionary<string, Color> { ["Skin"] = new Color(0.8f, 0.58f, 0.4f) });
                };
                g.onUp = gg =>
                {
                    gg.Say("Eh? Kenapa aku kat sini?", 0.8f);
                    gg.chatter = null;
                    gg.mode = Goon.Mode.Seek;
                    gg.seek = null;
                    gg.seekPoint = gg.transform.position + (gg.transform.position - _center).normalized * 40f;
                    gg.invulnerable = true;
                    Object.Destroy(gg.gameObject, 9f);
                };
                crowd.Add(g);
            }
            foreach (var g in crowd)
            {
                if (g == null || g.Down || g.tag != 0) continue;
                var d = g.transform.position - _center; d.y = 0;
                if (d.magnitude < 1.7f)
                {
                    _hp -= 14f;
                    Fx.Puff(_center + Vector3.up * 1.2f, new Color(0.45f, 0.75f, 0.42f), Vector3.up * 2f, 0.6f, 1.2f);
                    Fx.Word(_center + Vector3.up * 2.2f, "PLOP!");
                    HUD.I?.Toast($"Cendol masuk {_what}!");
                    ProcAudio.Play2D(ProcAudio.Bubble, 0.6f, 0.6f);
                    g.tag = 2;
                    Object.Destroy(g.gameObject);
                }
            }
            MissionOverlay.Meter($"{_what.ToUpper()}  {Mathf.CeilToInt(Mathf.Max(0, _hp))}%", _hp / 100f, MissionLooks.Health(_hp / 100f));
            MissionOverlay.Hint("Tumbuk / tendang zombi cendol sebelum sampai!");
            if (_hp <= 0f) { failReason = $"{_what} dah penuh Cendol Ajaib!"; return false; }
            return _t >= _duration;
        }

        public override void End(MissionManager m)
        {
            foreach (var g in crowd) if (g) Object.Destroy(g.gameObject);
            crowd.Clear();
            if (_propGo) Object.Destroy(_propGo, 0.5f);
        }
    }

    /// <summary>
    /// A square full of cendol zombies: kick some sense into every one of them. They mob you once they notice you,
    /// and enough of them at once will knock you flat. Get floored three times and it's over. (Cars don't cure.)
    /// </summary>
    public class BrawlObjective : Objective
    {
        readonly Vector3 _center;
        readonly int _count;
        int _cured, _kos;
        float _grace;
        public readonly List<Goon> crowd = new List<Goon>();
        public int Cured => _cured;
        public int Knockouts => _kos;

        public BrawlObjective(string text, Vector3 center, int count) { this.text = text; _center = center; _count = count; }

        public override string Progress => $"Pulih: {_cured}/{_count}   Jatuh: {_kos}/3";
        public override Vector3? Target
        {
            get
            {
                Goon best = null;
                float bd = float.MaxValue;
                foreach (var g in crowd)
                {
                    if (g == null || g.tag == 1) continue;
                    float d = (g.transform.position - P.Focus).sqrMagnitude;
                    if (d < bd) { bd = d; best = g; }
                }
                return best ? best.transform.position : (Vector3?)null;
            }
        }

        public override void Begin(MissionManager m)
        {
            End(m);
            _cured = 0; _kos = 0; _grace = 2f;
            for (int i = 0; i < _count; i++)
            {
                var at = MissionLooks.Open(_center, 16f, 7000 + i * 31, 3f);
                var g = Goon.Spawn(i % 3 == 0 ? "chr_townaunty" : i % 3 == 1 ? "chr_townman" : "chr_pakcik", "Zombi", at, i * 47f, m.transform, MissionLooks.Zombie);
                g.mode = Goon.Mode.Patrol;
                g.points.Add(at);
                g.points.Add(MissionLooks.Open(at, 6f, 8000 + i));
                g.speed = 0.9f;
                g.pause = 2f;
                g.hp = 2;
                g.chatter = MissionLooks.ZombieMoans;
                g.chatterEvery = 6f;
                g.onRunOver = (gg, v) => { gg.downTime = 3f; gg.tag = 2; };
                g.onDown = gg =>
                {
                    if (gg.tag == 2) return;                  // a car knocked them over: still cendol-crazy
                    gg.tag = 1;
                    _cured++;
                    gg.downTime = 2.5f;
                    Fx.Word(gg.transform.position + Vector3.up * 2f, "SEDAR!");
                    ModelFactory.Recolor(gg.gameObject, new Dictionary<string, Color> { ["Skin"] = new Color(0.8f, 0.58f, 0.4f) });
                    HUD.I?.Toast($"Pulih! ({_cured}/{_count})");
                };
                g.onUp = gg =>
                {
                    if (gg.tag == 2) { gg.tag = 0; gg.downTime = 0f; gg.hp = 2; return; }
                    gg.mode = Goon.Mode.Idle;
                    gg.invulnerable = true;
                    gg.chatter = null;
                    gg.Say("Eh... apa aku buat ni? Terima kasih, dik!", 0.8f);
                };
                crowd.Add(g);
            }
        }

        public override bool Tick(MissionManager m, float dt)
        {
            _grace -= dt;
            bool close = false;
            foreach (var g in crowd)
            {
                if (g == null || g.Down || g.tag == 1) continue;
                float d = Vector3.Distance(g.transform.position, P.transform.position);
                if (d < 15f && !P.Driving) { g.mode = Goon.Mode.Seek; g.seek = P.transform; g.stopDist = 1.0f; g.speed = 2.5f; }
                if (d < 1.4f) close = true;
            }
            if (close && _grace <= 0f && !P.Driving)
            {
                // a mob grabbing at you: every so often one of them gets you down
                _grace = 2.2f;
                var push = -P.transform.forward * 4f + Vector3.up * 5f;
                P.Knockdown(push);
                _kos++;
                HUD.I?.Toast($"Kena kerumun! ({_kos}/3)");
                if (_kos >= 3) { failReason = "Pengsan dikerumun zombi cendol!"; return false; }
            }
            if (P.Driving) MissionOverlay.Hint("Turun! Penawar kena tendang, bukan langgar.");
            else MissionOverlay.Hint("Tumbuk / tendang zombi. Jangan biar mereka kepung kau!");
            MissionOverlay.Meter($"PULIH {_cured}/{_count}", _cured / (float)_count, new Color(0.35f, 0.7f, 0.3f));
            return _cured >= _count;
        }

        public override void End(MissionManager m)
        {
            foreach (var g in crowd) if (g) Object.Destroy(g.gameObject);
            crowd.Clear();
        }
    }

    /// <summary>
    /// A snatch thief legs it down the pavements and five-foot ways, tipping crates behind him. Catch up and punch him
    /// down (he takes a few). Too far behind for too long and he's gone. In a car he cuts through where you can't.
    /// </summary>
    public class FootChaseObjective : Objective
    {
        readonly Vector3 _start;
        readonly string _name, _model;
        readonly Dictionary<string, Color> _paint;
        float _far, _crateT;
        bool _running;
        readonly List<GameObject> _crates = new List<GameObject>();
        public Goon Runner { get; private set; }

        public FootChaseObjective(string text, Vector3 start, string name, string model = "chr_townman", Dictionary<string, Color> paint = null)
        { this.text = text; _start = start; _name = name; _model = model; _paint = paint ?? MissionLooks.Thief; }

        public override Vector3? Target => Runner ? Runner.transform.position : (Vector3?)null;
        public override float? TargetHealth => Runner ? Mathf.Clamp01(Runner.hp / 4f) : (float?)null;
        public override string Progress => _far > 0f ? $"DIA LARI! {Mathf.CeilToInt(6f - _far)}" : null;

        public override void Begin(MissionManager m)
        {
            End(m);
            _far = 0f; _running = false; _crateT = 4f;
            Runner = Goon.Spawn(_model, _name, _start, 0f, m.transform, _paint);
            Runner.hp = 4;
            Runner.speed = 5.4f;
            Runner.mode = Goon.Mode.Idle;
            Runner.points.AddRange(RunPath(_start, 700f, 4242));
            Runner.onHit = (g, d, f) => { if (g.hp > 1) g.Say("Adoi! Lepaskan aku!", 1f); };
        }

        /// <summary>A long run along the pavement loops, hopping across the street to the next block now and then.</summary>
        public static List<Vector3> RunPath(Vector3 from, float length, int seed)
        {
            var city = GameManager.I.City;
            var rng = new System.Random(seed);
            var pts = new List<Vector3>();
            WalkZone zone = null;
            float best = float.MaxValue, s = 0f;
            foreach (var z in city.walkZones)
            {
                float zs = z.Closest(from);
                float d = (z.PointAt(zs) - from).sqrMagnitude;
                if (d < best) { best = d; zone = z; s = zs; }
            }
            if (zone == null) { pts.Add(from); return pts; }
            int dir = rng.Next(2) == 0 ? 1 : -1;
            float run = 0f, sinceHop = 0f;
            var last = zone.PointAt(s);
            WalkZone prev = null;
            while (run < length && pts.Count < 400)
            {
                s = zone.Wrap(s + dir * 4f);
                var p = zone.PointAt(s, 0f);
                run += Vector3.Distance(p, last);
                sinceHop += 4f;
                last = p;
                pts.Add(Goon.Ground(p));
                if (sinceHop > 50f + (float)rng.NextDouble() * 40f)
                {
                    // the next block over: a pavement loop whose edge is within a road's width of here
                    WalkZone hop = null;
                    float hs = 0f, hd = 26f * 26f;
                    foreach (var z in city.walkZones)
                    {
                        if (z == zone || z == prev) continue;
                        if (!z.bounds.Overlaps(new Rect(p.x - 30f, p.z - 30f, 60f, 60f))) continue;
                        float zs = z.Closest(p);
                        float d = (z.PointAt(zs) - p).sqrMagnitude;
                        if (d < hd && d > 4f) { hd = d; hop = z; hs = zs; }
                    }
                    if (hop != null) { prev = zone; zone = hop; s = hs; dir = rng.Next(2) == 0 ? 1 : -1; sinceHop = 0f; }
                }
            }
            return pts;
        }

        public override bool Tick(MissionManager m, float dt)
        {
            if (Runner == null) { failReason = $"{_name} hilang!"; return false; }
            if (Runner.Down) { Runner.Say("Ampun bang! Ampun! Nah, ambil balik!", 1f); return true; }
            float d = Vector3.Distance(P.Focus, Runner.transform.position + Vector3.up);
            if (!_running)
            {
                if (d < 16f) { _running = true; Runner.mode = Goon.Mode.Path; Runner.Say("Lari!!", 1f); HUD.I?.Toast($"Kejar {_name}!"); }
                return false;
            }
            // keep it tense: he finds another gear when you're on his heels, eases off when you drop back
            Runner.speed = P.Driving ? 7.5f : d < 5f && P.Sprinting ? 6.3f : d > 24f ? 4.2f : 5.4f;
            if (Runner.pointIndex >= Runner.points.Count - 6)
                Runner.points.AddRange(RunPath(Runner.points[Runner.points.Count - 1], 300f, Random.Range(0, 99999)));
            if ((_crateT -= dt) <= 0f && d < 14f && d > 3f)
            {
                _crateT = 5f;
                var at = Runner.transform.position - Runner.transform.forward * 1.2f;
                var go = ModelFactory.Spawn("Prop_Crate", Goon.Ground(at), Quaternion.Euler(0, Random.Range(0, 360), 0), m.transform, "ChaseCrate");
                Breakable.Make(go, Breakable.Kind.Shatter, 1).respawn = false;
                _crates.Add(go);
                Runner.Say("Ambik ni!", 1f);
            }
            if (P.Driving) MissionOverlay.Hint("Lorong sempit - kejar dengan kaki!");
            else MissionOverlay.Hint("Pecut (Shift) dan tumbuk dia!");
            if (d > 50f) _far += dt; else _far = Mathf.Max(0f, _far - dt);
            if (_far > 6f) failReason = $"{_name} dah lesap!";
            return false;
        }

        public override void End(MissionManager m)
        {
            if (Runner) Object.Destroy(Runner.gameObject, 2f);
            Runner = null;
            foreach (var c in _crates) if (c) Object.Destroy(c);
            _crates.Clear();
        }
    }

    // ===================================================================================================== boss

    /// <summary>
    /// The Mega-Mynah: Datuk Mega's giant robot bird. It circles, marks you with a red ring and dives; dodge the dive
    /// and it's stuck dazed in the ground - that's when punches (or a car) hurt it. Three phases, faster each time.
    /// </summary>
    public class MegaMynah : MonoBehaviour, IPunchable
    {
        public enum State { Orbit, Mark, Dive, Dazed, Rise, Dead }
        public State state = State.Orbit;
        public Vector3 center;
        public int phase = 1, hp;
        public const int HpPerPhase = 4;
        public System.Action onKnockout;
        public int playerKOs;
        float _t, _ang;
        Vector3 _mark, _from;
        int _dives;
        GameObject _ring;

        public static MegaMynah Spawn(Vector3 center, Transform parent)
        {
            var go = ModelFactory.Spawn("Prop_BurungKamera", center + Vector3.up * 10f, Quaternion.identity, parent, "MegaMynah");
            go.transform.localScale = Vector3.one * 4.5f;
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Character;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.55f;                 // x4.5 scale: about 2.5 m round
            var b = go.AddComponent<MegaMynah>();
            b.center = center;
            b.hp = HpPerPhase;
            return b;
        }

        public Vector3 Mark => _mark;
        public void DebugDaze() { state = State.Dazed; _t = 0f; transform.position = Goon.Ground(transform.position) + Vector3.up * 1.2f; }

        void Update()
        {
            if (GameManager.I != null && !GameManager.I.Playing) return;
            var pc = PlayerController.I;
            float dt = Time.deltaTime;
            _t += dt;
            switch (state)
            {
                case State.Orbit:
                    _ang += dt * (25f + phase * 10f);
                    var want = center + Quaternion.Euler(0, _ang, 0) * Vector3.forward * 14f + Vector3.up * (9f + Mathf.Sin(_t * 2f));
                    transform.position = Vector3.Lerp(transform.position, want, dt * 2f);
                    Face(pc != null ? pc.transform.position : center);
                    if (_t > 5f - phase * 0.9f && pc != null) { state = State.Mark; _t = 0f; _mark = Goon.Ground(pc.transform.position); ShowRing(true); Fx.Word(transform.position, "KRAAAK!"); ProcAudio.Play(ProcAudio.Siren, transform.position, 0.4f, 1.6f); }
                    break;
                case State.Mark:
                    // hang over the mark (tracking you a little) while the ring warns you
                    // it tracks you a little at first, then commits (so a sidestep at the last moment works)
                    if (pc != null && _t < 0.6f) _mark = Vector3.Lerp(_mark, Goon.Ground(pc.transform.position), dt * 0.4f);
                    transform.position = Vector3.Lerp(transform.position, _mark + Vector3.up * 11f, dt * 3f);
                    if (_ring) _ring.transform.position = _mark + Vector3.up * 0.15f;
                    if (_t > 1.5f - phase * 0.1f) { state = State.Dive; _t = 0f; _from = transform.position; }
                    break;
                case State.Dive:
                    float k = Mathf.Clamp01(_t / 0.45f);
                    transform.position = Vector3.Lerp(_from, _mark + Vector3.up * 1.2f, k * k);
                    if (k >= 1f) Impact(pc);
                    break;
                case State.Dazed:
                    if (Mathf.Repeat(_t, 0.6f) < dt) Fx.Stars(transform.position + Vector3.up * 2.4f, 5, 3f);
                    transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y + dt * 40f, Mathf.Sin(_t * 6f) * 12f);
                    if (_t > 3.6f) { state = State.Rise; _t = 0f; _from = transform.position; }
                    break;
                case State.Rise:
                    transform.position = Vector3.Lerp(_from, center + Vector3.up * 9f, Mathf.Clamp01(_t / 1.2f));
                    if (_t > 1.2f) { state = State.Orbit; _t = 0f; }
                    break;
                case State.Dead:
                    transform.position += Vector3.down * dt * 2f;
                    break;
            }
        }

        void Face(Vector3 p)
        {
            var to = p - transform.position; to.y = 0;
            if (to.sqrMagnitude > 0.1f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 4f);
        }

        void ShowRing(bool on)
        {
            if (on && _ring == null)
            {
                _ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _ring.name = "DiveMark";
                Destroy(_ring.GetComponent<Collider>());
                _ring.transform.localScale = new Vector3(6.4f, 0.02f, 6.4f);
                var mr = _ring.GetComponent<MeshRenderer>();
                mr.sharedMaterial = LatMaterials.Beam(new Color(1f, 0.15f, 0.1f, 0.9f));
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (_ring) _ring.SetActive(on);
        }

        void Impact(PlayerController pc)
        {
            ShowRing(false);
            Fx.Ring(_mark, 9f, 20, 0.6f);
            Fx.Burst(_mark + Vector3.up, new Color(0.5f, 0.45f, 0.4f), 18, 7f);
            ChaseCamera.I?.Shake(0.6f);
            ProcAudio.Play(ProcAudio.Crash, _mark, 1f, 0.6f);
            Pedestrian.React(_mark, 30f, Pedestrian.Stir.Crash);
            if (pc != null && !pc.Driving && Vector3.Distance(pc.transform.position, _mark) < 3.3f)
            {
                var away = pc.transform.position - _mark; away.y = 0;
                pc.Knockdown((away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 7f + Vector3.up * 6f);
                playerKOs++;
                HUD.I?.Toast($"Kena patuk! ({playerKOs}/3)");
            }
            _dives++;
            // phase 2 and 3: it shakes it off and goes again once before it's dazed
            if (phase >= 2 && _dives % 2 == 1 && pc != null)
            {
                state = State.Mark; _t = 0f; _mark = Goon.Ground(pc.transform.position); ShowRing(true);
                Fx.Word(transform.position + Vector3.up * 3f, "LAGI!");
                return;
            }
            state = State.Dazed;
            _t = 0f;
            Fx.Word(transform.position + Vector3.up * 3f, "PENING!");
        }

        public void Punched(Vector3 dir, float force, bool heavy)
        {
            if (state == State.Dead) return;
            if (state != State.Dazed) { Fx.Word(transform.position + Vector3.up * 2f, "TING!"); ProcAudio.Play(ProcAudio.Clang, transform.position, 0.6f, 1.4f); return; }
            Damage(heavy ? 2 : 1);
        }

        public void Damage(int n)
        {
            if (state == State.Dead) return;
            hp -= n;
            Fx.Burst(transform.position + Vector3.up * 1.5f, new Color(0.3f, 0.3f, 0.35f), 10, 6f);
            Fx.Word(transform.position + Vector3.up * 3f, n > 1 ? "BEDEBUK!" : "PRANG!");
            ProcAudio.Play(ProcAudio.Smash, transform.position, 0.8f, 0.8f);
            ChaseCamera.I?.Shake(0.25f);
            HitStop.Do(0.07f);
            if (hp > 0) return;
            if (phase >= 3)
            {
                state = State.Dead;
                Fx.Burst(transform.position, new Color(0.95f, 0.6f, 0.2f), 30, 10f);
                Fx.Word(transform.position + Vector3.up * 4f, "KABOOM!");
                ProcAudio.Play(ProcAudio.Crash, transform.position, 1f, 0.5f);
                for (int i = 0; i < 20; i++) Pickup.SpawnCoin(transform.position + Vector3.up, true);
                onKnockout?.Invoke();
                Destroy(gameObject, 3f);
                return;
            }
            phase++;
            hp = HpPerPhase;
            state = State.Rise;
            _t = 0f;
            _from = transform.position;
            HUD.I?.BigMessage($"FASA {phase}", phase == 3 ? "Dia makin marah!" : "Mega-Mynah bangun semula!");
        }

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            var v = rb ? rb.GetComponent<Vehicle>() : null;
            if (v != null && v.PlayerInside && state == State.Dazed && rb.linearVelocity.magnitude > 6f) Damage(2);
        }

        void OnDestroy() { if (_ring) Destroy(_ring); }
    }

    public class BossObjective : Objective
    {
        readonly Vector3 _center;
        bool _dead;
        public MegaMynah Boss { get; private set; }

        public BossObjective(string text, Vector3 center) { this.text = text; _center = center; }

        public override Vector3? Target => Boss ? Boss.transform.position : (Vector3?)null;
        public override float? TargetHealth => Boss ? ((3 - Boss.phase) * MegaMynah.HpPerPhase + Mathf.Max(0, Boss.hp)) / (3f * MegaMynah.HpPerPhase) : 0f;
        public override string Progress => Boss ? $"Fasa {Boss.phase}/3   Jatuh: {Boss.playerKOs}/3" : null;

        public override void Begin(MissionManager m)
        {
            End(m);
            _dead = false;
            Boss = MegaMynah.Spawn(_center, m.transform);
            Boss.onKnockout = () => _dead = true;
            SamanMeter.I.suppressed = true;
        }

        public override bool Tick(MissionManager m, float dt)
        {
            if (Boss == null && !_dead) { failReason = "Mega-Mynah lari!"; return false; }
            if (Boss != null)
            {
                if (Boss.playerKOs >= 3) { failReason = "Adik dipatuk Mega-Mynah tiga kali!"; return false; }
                var s = Boss.state;
                MissionOverlay.Hint(s == MegaMynah.State.Mark ? "LARI dari bulatan merah!" : s == MegaMynah.State.Dazed ? "SEKARANG! Tumbuk / tendang dia!" : "Tunggu dia menjunam, kemudian serang");
            }
            return _dead;
        }

        public override void End(MissionManager m)
        {
            SamanMeter.I.suppressed = false;
            if (Boss) Object.Destroy(Boss.gameObject);
            Boss = null;
        }
    }
}
