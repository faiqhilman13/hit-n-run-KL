using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    public class Mission
    {
        public string id, title, giver;
        public Line[] intro, outro;
        public List<Objective> objectives = new List<Objective>();
        public int reward = 50;
        public bool isRace;
        public string startCar;       // spawn this car next to the player when the mission starts
    }

    /// <summary>One step of a mission. Tick returns true once complete.</summary>
    public abstract class Objective
    {
        public string text;
        public float timeLimit;       // 0 = no timer
        public string failReason;     // set to fail the mission
        public bool debugForceDone;   // tests: skip this step
        public virtual Vector3? Target => null;
        public virtual string Progress => null;
        public virtual float? TargetHealth => null;
        public virtual void Begin(MissionManager m) { }
        public abstract bool Tick(MissionManager m, float dt);
        public virtual void End(MissionManager m) { }
        protected static PlayerController P => PlayerController.I;
    }

    public class TalkObjective : Objective
    {
        readonly string _npc;
        readonly Line[] _lines;
        bool _done, _talking;
        NPC _target;

        public TalkObjective(string npc, string text, params Line[] lines) { _npc = npc; this.text = text; _lines = lines; }
        public override Vector3? Target => _target ? _target.transform.position : (Vector3?)null;

        public override void Begin(MissionManager m)
        {
            _target = m.Npc(_npc);
            if (_target == null) { _done = true; return; }
            _target.gameObject.SetActive(true);
            _target.onTalk = _ =>
            {
                if (_talking) return;
                _talking = true;
                Dialogue.Say(_lines, () => _done = true);
            };
            _target.hasMission = true;
        }

        public override bool Tick(MissionManager m, float dt) => _done;

        public override void End(MissionManager m)
        {
            if (_target) { _target.hasMission = false; _target.onTalk = null; }
        }
    }

    public class DialogueObjective : Objective
    {
        readonly Line[] _lines;
        bool _done;
        public DialogueObjective(params Line[] lines) { _lines = lines; text = ""; }
        public override void Begin(MissionManager m) => Dialogue.Say(_lines, () => _done = true);
        public override bool Tick(MissionManager m, float dt) => _done;
    }

    public class GoToObjective : Objective
    {
        readonly Vector3 _pos;
        readonly float _radius;
        readonly bool _needCar;
        public GoToObjective(Vector3 pos, string text, float time = 0, bool needCar = false, float radius = 7f)
        { _pos = pos; this.text = text; timeLimit = time; _needCar = needCar; _radius = needCar ? Mathf.Max(radius, 9f) : radius; }   // a car pulls up at the kerb, not on it
        public override Vector3? Target => _pos;

        public override bool Tick(MissionManager m, float dt)
        {
            if (_needCar && !P.Driving) return false;
            var d = P.Focus - _pos;
            d.y *= 0.3f;
            return d.magnitude < _radius;
        }
    }

    public class GetInCarObjective : Objective
    {
        public GetInCarObjective(string text = "Naik kereta") { this.text = text; }
        public override Vector3? Target => P && !P.Driving && GameManager.I.LastCar ? GameManager.I.LastCar.transform.position : (Vector3?)null;
        public override bool Tick(MissionManager m, float dt) => P.Driving;
    }

    /// <summary>Pick up N things scattered around (nasi lemak, parts, marbles...).</summary>
    public class CollectObjective : Objective
    {
        readonly List<Vector3> _spots;
        readonly string _model;
        readonly List<Pickup> _items = new List<Pickup>();
        int _got, _need;

        public CollectObjective(string text, string model, List<Vector3> spots, float time = 0)
        { this.text = text; _model = model; _spots = spots; timeLimit = time; _need = spots.Count; }

        public override string Progress => $"{_got}/{_need}";

        public override Vector3? Target
        {
            get
            {
                Pickup best = null;
                float bd = float.MaxValue;
                foreach (var i in _items)
                {
                    if (!i) continue;
                    float d = (i.transform.position - P.Focus).sqrMagnitude;
                    if (d < bd) { bd = d; best = i; }
                }
                return best ? best.transform.position : (Vector3?)null;
            }
        }

        public override void Begin(MissionManager m)
        {
            foreach (var s in _spots)
            {
                var go = ModelFactory.Spawn(_model, s, Quaternion.identity, m.transform, "MissionItem");
                go.transform.localScale = Vector3.one * 1.6f;
                var p = Pickup.Setup(go, Pickup.Kind.MissionItem, 1);
                p.magnet = false;
                p.onCollected = _ => { _got++; HUD.I.Toast($"{_got}/{_need}"); };
                _items.Add(p);
                m.Beacon(s, 0.35f);
            }
        }

        public override bool Tick(MissionManager m, float dt) => _got >= _need;

        public override void End(MissionManager m)
        {
            foreach (var i in _items) if (i) Object.Destroy(i.gameObject);
        }
    }

    /// <summary>Wreck a target vehicle (H&amp;R "destroy" missions).</summary>
    public class DestroyObjective : Objective
    {
        readonly string _car;
        readonly Vector3 _spawn;
        readonly float _healthMult, _speed;
        readonly Dictionary<string, Color> _paint;
        public Vehicle target;

        public DestroyObjective(string text, string car, Vector3 spawn, float time, float healthMult = 1f, float speed = 18f, Dictionary<string, Color> paint = null)
        { this.text = text; _car = car; _spawn = spawn; timeLimit = time; _healthMult = healthMult; _speed = speed; _paint = paint; }

        public override Vector3? Target => target ? target.transform.position : (Vector3?)null;
        public override float? TargetHealth => target ? target.health / target.maxHealth : (float?)null;

        public override void Begin(MissionManager m)
        {
            var node = GameManager.I.City.roads.Nearest(_spawn);
            var face = Quaternion.identity;
            if (node.links.Count > 0)
            {
                var dir = node.links[0].pos - node.pos; dir.y = 0;
                if (dir.sqrMagnitude > 0.01f) face = Quaternion.LookRotation(dir);
            }
            target = VehicleSpawner.Spawn(_car, node.pos + Vector3.up * 0.6f, face, VehicleRole.MissionTarget, m.transform);
            if (_paint != null) ModelFactory.Recolor(target.gameObject, _paint);
            target.maxHealth = target.health = target.maxHealth * _healthMult;
            target.noEnter = true;
            target.driver = new FleeDriver(GameManager.I.City.roads) { cruise = _speed };
            SamanMeter.I.suppressed = true;
        }

        public override bool Tick(MissionManager m, float dt) => target == null || target.Wrecked;

        public override void End(MissionManager m)
        {
            SamanMeter.I.suppressed = false;
            if (target) Object.Destroy(target.gameObject, 6f);
        }
    }

    /// <summary>Tail a car without losing it (or getting too close and spooking it).</summary>
    public class FollowObjective : Objective
    {
        readonly string _car;
        readonly List<Vector3> _stops;
        readonly float _maxDist;
        readonly Dictionary<string, Color> _paint;
        Vehicle _target;
        RouteDriver _driver;
        float _lostTime;
        bool _started;

        /// <param name="stops">where the car goes; it drives the streets between them</param>
        public FollowObjective(string text, string car, List<Vector3> stops, float maxDist = 70f, Dictionary<string, Color> paint = null)
        { this.text = text; _car = car; _stops = stops; _maxDist = maxDist; _paint = paint; }

        public override Vector3? Target => _target ? _target.transform.position : (Vector3?)null;
        public override string Progress => _lostTime > 0 ? $"HILANG! {Mathf.CeilToInt(5f - _lostTime)}" : null;

        public override void Begin(MissionManager m)
        {
            var route = GameManager.I.City.roads.Route(_stops);
            var start = route[0];
            var dir = (route.Count > 1 ? route[1] - start : Vector3.forward);
            dir.y = 0;
            _target = VehicleSpawner.Spawn(_car, start + Vector3.up * 0.6f, Quaternion.LookRotation(dir), VehicleRole.MissionTarget, m.transform);
            if (_paint != null) ModelFactory.Recolor(_target.gameObject, _paint);
            _target.noEnter = true;
            _target.maxHealth = _target.health = 9999f;
            _driver = new RouteDriver(route.GetRange(1, route.Count - 1), 13f);
            // rubber band: slow down if the player falls behind
            _driver.speedOverride = () =>
            {
                float d = Vector3.Distance(P.Focus, _target.transform.position);
                if (!_started) return 0f; // wait for the player to show up
                return d > 45f ? 7f : d < 15f ? 15f : 12.5f;
            };
            _target.driver = _driver;
        }

        public override bool Tick(MissionManager m, float dt)
        {
            if (_target == null) { failReason = "Kereta tu dah hilang!"; return false; }
            if (!_started)
            {
                if (Vector3.Distance(P.Focus, _target.transform.position) < 45f) { _started = true; HUD.I.Toast("Dia dah gerak! Ikut!"); }
                return false;
            }
            float d = Vector3.Distance(P.Focus, _target.transform.position);
            if (d > _maxDist) _lostTime += dt; else _lostTime = 0f;
            if (_lostTime > 5f) failReason = "Kau hilang jejak!";
            return _driver.Finished;
        }

        public override void End(MissionManager m) { if (_target) Object.Destroy(_target.gameObject, 4f); }
    }

    /// <summary>Checkpoint street race against AI drivers; must finish first.</summary>
    public class RaceObjective : Objective
    {
        readonly List<Vector3> _checkpoints;
        readonly int _laps;
        readonly string[] _rivals;
        readonly List<Vehicle> _opponents = new List<Vehicle>();
        readonly List<RouteDriver> _drivers = new List<RouteDriver>();
        readonly List<List<int>> _marks = new List<List<int>>();    // per rival: where each checkpoint falls on its route
        int _next;
        int _total;

        public RaceObjective(string text, List<Vector3> checkpoints, int laps, params string[] rivals)
        { this.text = text; _checkpoints = checkpoints; _laps = laps; _rivals = rivals; }

        public override Vector3? Target => _next < _total ? _checkpoints[_next % _checkpoints.Count] : (Vector3?)null;
        public override string Progress => $"CP {Mathf.Min(_next + 1, _total)}/{_total}   Kedudukan: {Place()}/{_opponents.Count + 1}";

        public override void Begin(MissionManager m)
        {
            var roads = GameManager.I.City.roads;
            _total = _checkpoints.Count * _laps;
            // line the rivals up on the street behind the start, the way the course comes in
            var lead = roads.Route(new List<Vector3> { _checkpoints[_checkpoints.Count - 1], _checkpoints[0] });
            var start = P.Focus;
            int at = 0;
            for (int j = 1; j < lead.Count; j++)
                if ((lead[j] - start).sqrMagnitude < (lead[at] - start).sqrMagnitude) at = j;
            for (int i = 0; i < _rivals.Length; i++)
            {
                var pos = Back(lead, at, 8f + i * 7f, out var fwd);
                pos += Vector3.Cross(Vector3.up, fwd) * (i % 2 == 0 ? -1.3f : 1.3f) + Vector3.up * 0.6f;
                var v = VehicleSpawner.Spawn(_rivals[i], pos, Quaternion.LookRotation(fwd), VehicleRole.MissionTarget, m.transform);
                v.noEnter = true;
                var stops = new List<Vector3> { pos };
                for (int l = 0; l < _laps; l++) stops.AddRange(_checkpoints);
                var marks = new List<int>();
                var d = new RouteDriver(roads.Route(stops, false, marks, 1.2f + (i % 2) * 2f), 15f + i * 1.5f) { arriveRadius = 8f };
                // rubber-band the AI so races stay close
                var vv = v;
                float f = 1f + 0.04f * i;
                int ri = _marks.Count;
                d.speedOverride = () =>
                {
                    float gap = Vector3.Distance(vv.transform.position, P.Focus);
                    int done = Passed(ri);
                    bool ahead = done > _next || (done == _next && Dist(vv.transform.position) < Dist(P.Focus));
                    return (ahead ? (gap > 40 ? 13f : 18f) : (gap > 40 ? 30f : 22f)) * f;
                };
                v.driver = d;
                _opponents.Add(v);
                _drivers.Add(d);
                _marks.Add(marks);
            }
            SamanMeter.I.suppressed = true;
            m.RaceBeacons(_checkpoints);
        }

        /// <summary>The point dist metres back along a route from index i, and the way it faces.</summary>
        static Vector3 Back(List<Vector3> route, int i, float dist, out Vector3 fwd)
        {
            fwd = Vector3.forward;
            if (route.Count < 2) return route.Count > 0 ? route[0] : Vector3.zero;
            i = Mathf.Clamp(i, 1, route.Count - 1);
            while (true)
            {
                var seg = route[i] - route[i - 1];
                seg.y = 0;
                float len = seg.magnitude;
                if (len > 0.01f) fwd = seg / len;
                if (dist <= len || i == 1) return route[i] - fwd * Mathf.Min(dist, len);
                dist -= len;
                i--;
            }
        }

        /// <summary>How many checkpoints rival i has been through.</summary>
        int Passed(int i)
        {
            int n = 0;
            var marks = _marks[i];
            while (n < marks.Count && marks[n] < _drivers[i].index) n++;
            return n;
        }

        float Dist(Vector3 p) => _next < _total ? Vector3.Distance(p, _checkpoints[_next % _checkpoints.Count]) : 0;

        int Place()
        {
            int place = 1;
            float me = _next * 10000f - Dist(P.Focus);
            for (int i = 0; i < _drivers.Count; i++)
            {
                if (!_opponents[i]) continue;
                int done = Passed(i);
                float them = done * 10000f - (done < _total ? Vector3.Distance(_opponents[i].transform.position, _checkpoints[done % _checkpoints.Count]) : 0);
                if (them > me) place++;
            }
            return place;
        }

        public override bool Tick(MissionManager m, float dt)
        {
            if (!P.Driving) { text = "Naik kereta! Perlumbaan tengah berjalan!"; }
            else text = "Menang perlumbaan!";
            var cp = _checkpoints[_next % _checkpoints.Count];
            if (P.Driving && Vector3.Distance(P.Focus, cp) < 11f)
            {
                _next++;
                ProcAudio.Play2D(ProcAudio.Blip, 0.6f, 1.2f);
                m.RaceBeacons(_checkpoints, _next);
            }
            foreach (var d in _drivers)
                if (d.Finished && _next < _total) { failReason = "Kalah! Orang lain sampai dulu."; return false; }
            return _next >= _total;
        }

        public override void End(MissionManager m)
        {
            SamanMeter.I.suppressed = false;
            foreach (var o in _opponents) if (o) Object.Destroy(o.gameObject, 3f);
        }
    }

    /// <summary>Smash things: burung kamera, cendol crates, MegaMaju billboards.</summary>
    public class SmashObjective : Objective
    {
        readonly List<Vector3> _spots;
        readonly string _kind; // "bird" | "crate"
        readonly List<GameObject> _things = new List<GameObject>();
        int _done;

        public SmashObjective(string text, string kind, List<Vector3> spots, float time = 0)
        { this.text = text; _kind = kind; _spots = spots; timeLimit = time; }

        public override string Progress => $"{_done}/{_spots.Count}";

        public override Vector3? Target
        {
            get
            {
                GameObject best = null; float bd = float.MaxValue;
                foreach (var t in _things)
                {
                    if (!t) continue;
                    float d = (t.transform.position - P.Focus).sqrMagnitude;
                    if (d < bd) { bd = d; best = t; }
                }
                return best ? best.transform.position : (Vector3?)null;
            }
        }

        public override void Begin(MissionManager m)
        {
            foreach (var s in _spots)
            {
                if (_kind == "bird")
                {
                    var b = BurungKamera.Spawn(null, s + Vector3.up * 1.2f, m.transform);
                    b.onSmashed = _ => _done++;
                    _things.Add(b.gameObject);
                }
                else
                {
                    var go = ModelFactory.Spawn("Prop_CendolCrate", s, Quaternion.Euler(0, Random.Range(0, 360), 0), m.transform, "CendolCrate");
                    var br = Breakable.Make(go, Breakable.Kind.Shatter, 2);
                    br.onBroken = () => _done++;
                    _things.Add(go);
                }
                m.Beacon(s, 0.35f);
            }
        }

        public override bool Tick(MissionManager m, float dt) => _done >= _spots.Count;

        public override void End(MissionManager m)
        {
            foreach (var t in _things) if (t) Object.Destroy(t);
        }
    }

    /// <summary>Police are after you: shake them off (and optionally reach a place).</summary>
    public class EvadeObjective : Objective
    {
        public EvadeObjective(string text) { this.text = text; }
        public override void Begin(MissionManager m) => SamanMeter.I.ForceWanted();
        public override bool Tick(MissionManager m, float dt) => !SamanMeter.I.Wanted;
    }
}
