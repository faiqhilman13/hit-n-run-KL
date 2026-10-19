using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    public static class VehicleSpawner
    {
        public static Vehicle Spawn(string carId, Vector3 pos, Quaternion rot, VehicleRole role, Transform parent = null)
        {
            var def = GameData.Car(carId);
            var go = ModelFactory.Spawn(def.model, pos, rot, parent, def.name);
            if (def.swaps != null) ModelFactory.Recolor(go, def.swaps);
            go.AddComponent<Rigidbody>();
            var v = go.AddComponent<Vehicle>();
            v.displayName = def.name;
            v.maxSpeed = def.maxSpeed;
            v.acceleration = def.accel;
            v.turnRate = def.turn;
            v.grip = def.grip;
            v.maxHealth = v.health = def.health;
            v.role = role;
            v.carId = carId;
            v.Setup(def.mass);
            return v;
        }

        static readonly string[] TrafficIds = { "kancil", "kancil", "saga", "myvi", "myvi", "myvi", "teksi", "teksi", "kapcai", "kapcai",
                                                "hilux", "van", "basmini", "foodtruck", "iriz", "iriz", "mpv", "mpv", "lorry" };
        static readonly Color[] Paints =
        {
            new Color(0.84f, 0.1f, 0.1f), new Color(0.18f, 0.38f, 0.78f), new Color(0.98f, 0.8f, 0.12f), new Color(0.2f, 0.62f, 0.34f),
            new Color(0.94f, 0.94f, 0.93f), new Color(0.62f, 0.64f, 0.68f), new Color(0.12f, 0.12f, 0.14f), new Color(0.98f, 0.52f, 0.14f),
            new Color(0.55f, 0.3f, 0.7f), new Color(0.4f, 0.75f, 0.9f), new Color(0.95f, 0.55f, 0.7f),
        };

        /// <summary>Random traffic car: a mix of models with random paint and a driver at the wheel.</summary>
        public static Vehicle SpawnTraffic(Vector3 pos, Quaternion rot, Transform parent)
        {
            var id = TrafficIds[Random.Range(0, TrafficIds.Length)];
            var v = Spawn(id, pos, rot, VehicleRole.Traffic, parent);
            if (Paintable(id))
                ModelFactory.Recolor(v.gameObject, new Dictionary<string, Color> { ["car_paint"] = Paints[Random.Range(0, Paints.Length)] });
            AddDriver(v);
            v.maxSpeed *= 0.6f;
            return v;
        }

        /// <summary>A random paint job (traffic and parked cars).</summary>
        public static void Repaint(Vehicle v) =>
            ModelFactory.Recolor(v.gameObject, new Dictionary<string, Color> { ["car_paint"] = Paints[Random.Range(0, Paints.Length)] });

        /// <summary>Models whose car_paint cell takes a random colour.</summary>
        public static bool Paintable(string id) => id == "kancil" || id == "saga" || id == "myvi" || id == "hilux" || id == "van" || id == "iriz" || id == "mpv";

        static readonly string[] DriverModels = { "chr_townman", "chr_townaunty", "chr_pakcik", "chr_townman" };

        /// <summary>A townsperson sitting at the wheel (Hit & Run shows every driver).</summary>
        public static void AddDriver(Vehicle v)
        {
            var seat = v.Visuals != null ? v.Visuals.Seat : null;
            if (seat == null || GameManager.WebLite) return;
            var model = DriverModels[Random.Range(0, DriverModels.Length)];
            var go = ModelFactory.Spawn(model, seat.position - v.transform.up * (v.TwoWheeler ? PlayerController.RideDrop : 0.6f),
                v.transform.rotation, null, "Driver");
            go.transform.SetParent(seat, true);
            var rig = go.AddComponent<CharacterRig>();
            rig.sitting = !v.TwoWheeler;
            rig.riding = v.TwoWheeler;
            if (!v.TwoWheeler) { var fit = go.AddComponent<SeatFit>(); fit.seat = seat; fit.weight = 1f; }
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Vehicle;
        }
    }

    /// <summary>Shared steering maths for AI drivers.</summary>
    public abstract class AIDriver : IDriver
    {
        protected float stuckTime, reverseTime;
        protected static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        public float speedScale = 1f;
        public bool avoid = true;
        // queued behind something that isn't going anywhere (a wreck, a parked car, a head-on in a junction):
        // how long we've waited, and a few seconds of edging round it on the side it leaves free
        float _blockedTime, _detourTime, _detourSide;
        float _patience = Random.Range(3.5f, 6f);
        /// <summary>Stopped on purpose: at a red, giving way, waiting for people on a crossing, or queued behind
        /// someone who is. Cars behind wait in the queue instead of pulling round (SteerTo).</summary>
        public bool Holding { get; protected set; }
        /// <summary>Last step's SteerTo found a holding car right in front.</summary>
        protected bool queuedBehindHolder;

        public abstract void Drive(Vehicle v, out float throttle, out float steer, out bool handbrake);

        protected void SteerTo(Vehicle v, Vector3 target, float desiredSpeed, out float throttle, out float steer, out bool handbrake)
        {
            handbrake = false;
            float dt = Time.fixedDeltaTime;
            queuedBehindHolder = false;
            Vector3 local = v.transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            steer = Mathf.Clamp(angle / 30f, -1f, 1f);
            float fs = v.ForwardSpeed;
            float speed = desiredSpeed * speedScale;
            // slow for sharp turns
            speed *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp01((Mathf.Abs(angle) - 15f) / 60f));

            bool blocked = false;
            if (avoid)
            {
                var origin = v.transform.position + Vector3.up * 0.8f + v.transform.forward * (v.halfLength + 1.3f);
                float look = 6f + Mathf.Max(0, fs) * 0.9f;
                if (Physics.SphereCast(origin, 1.1f, v.transform.forward, out var hit, look, (1 << Layers.Vehicle) | (1 << Layers.Character), QueryTriggerInteraction.Collide)
                    && hit.rigidbody != v.Body)
                {
                    // a car that's moving is a queue: wait in it. One that's standing still is in the way.
                    bool still = hit.rigidbody == null || hit.rigidbody.isKinematic || hit.rigidbody.linearVelocity.sqrMagnitude < 1f;
                    bool person = hit.collider.gameObject.layer == Layers.Character;
                    // a car waiting at a light (or in the queue for one) isn't in the way: it's the queue
                    if (still && !person && hit.rigidbody != null && hit.rigidbody.TryGetComponent<Vehicle>(out var ahead) &&
                        ahead.driver is AIDriver ad && ad.Holding && !ahead.Wrecked)
                    {
                        still = false;
                        queuedBehindHolder = true;
                    }
                    if (_detourTime > 0f && !person)
                        speed = Mathf.Min(speed, 4f);                 // edging past: keep rolling, slowly
                    else
                    {
                        speed = Mathf.Min(speed, Mathf.Max(0f, (hit.distance - 3f) * 0.8f));
                        blocked = still && Mathf.Abs(fs) < 0.8f;
                    }
                    if (_detourSide == 0f || _detourTime <= 0f)
                    {
                        // pass on whichever side of it has more room (the right, out into the road, when it's dead ahead)
                        float lx = v.transform.InverseTransformPoint(hit.collider.bounds.center).x;
                        _detourSide = lx > 0.4f ? -1f : 1f;
                    }
                }
            }

            // stalled behind something that won't move: after a little patience, pull round it
            _blockedTime = blocked ? _blockedTime + dt : 0f;
            if (_blockedTime > _patience && _detourTime <= 0f)
            {
                _blockedTime = 0f;
                _detourTime = 3.5f;
                reverseTime = 0.9f;                               // back off first so there's room to turn out
            }
            if (_detourTime > 0f)
            {
                _detourTime -= dt;
                steer = Mathf.Clamp(steer + _detourSide * 0.65f, -1f, 1f);
                speed = Mathf.Max(speed, 3f);
            }

            throttle = Mathf.Clamp((speed - fs) * 0.35f, -1f, 1f);
            if (speed < 0.5f && fs < 1f) throttle = 0f;

            // unstick: if we want to move but can't, back up while counter-steering
            if (reverseTime > 0f)
            {
                reverseTime -= dt;
                throttle = -1f;
                steer = -steer;
                return;
            }
            if (speed > 3f && Mathf.Abs(fs) < 0.6f) stuckTime += dt;
            else stuckTime = 0f;
            if (stuckTime > 2.2f) { stuckTime = 0f; reverseTime = 1.3f; }
        }
    }

    /// <summary>
    /// Cruises the road grid on the left-hand lane, picking random turns. Stops at the stop line on red (and on
    /// amber if it can stop in time), gives way at junctions without signals (to anyone in the box or coming from
    /// the right), waits for people on the zebra it's about to cross, and doesn't drive into a box it can't get
    /// out of. The next turn is picked a street ahead, so it knows which crossing it will drive over.
    /// </summary>
    public class TrafficDriver : AIDriver
    {
        readonly RoadNetwork _roads;
        RoadNetwork.Node _from, _to, _next;
        float _resnap;
        public float cruise = 11f;
        // waiting at the junction ahead: how long, and whether the counters have had this stop yet
        float _holdT, _checkT, _exitT;
        bool _countedStop, _countedAmber, _giveWay, _exitBlocked;
        static readonly Collider[] Near = new Collider[16];

        public TrafficDriver(RoadNetwork roads, RoadNetwork.Node start = null)
        {
            _roads = roads;
            _from = start;
        }

        void Advance()
        {
            var prev = _from;
            _from = _to;
            _to = _next ?? _roads.NextFrom(_to, prev);
            _next = _roads.NextFrom(_to, _from);
            _holdT = 0f;
            _countedStop = _countedAmber = false;
        }

        public override void Drive(Vehicle v, out float throttle, out float steer, out bool handbrake)
        {
            if (_to == null)
            {
                _from ??= _roads.Nearest(v.transform.position);
                // head to the neighbour most in front of us
                float best = -2f;
                foreach (var l in _from.links)
                {
                    float d = Vector3.Dot((l.pos - _from.pos).normalized, v.transform.forward);
                    if (d > best) { best = d; _to = l; }
                }
                _to ??= _from;
                _next = _roads.NextFrom(_to, _from);
            }
            // everything in plan: flyover decks are just higher nodes
            Vector3 flatPos = Flat(v.transform.position), from = Flat(_from.pos), to = Flat(_to.pos);
            float segLen = Mathf.Max(0.5f, Vector3.Distance(from, to));
            float t = Mathf.Clamp01(Vector3.Dot(flatPos - from, (to - from).normalized) / segLen);
            // shunted off our street (a crash, a detour, a police car handed back to traffic): rejoin the nearest
            // road rather than cutting across the block to the old one
            _resnap -= Time.fixedDeltaTime;
            if (_resnap <= 0f && (flatPos - Vector3.Lerp(from, to, t)).sqrMagnitude > 16f * 16f)
            {
                _resnap = 2f;                                   // (and no re-entry: we may be far from any road)
                _from = _roads.Nearest(v.transform.position);
                _to = null;
                Holding = false;
                Drive(v, out throttle, out steer, out handbrake);
                return;
            }
            bool twoWay = RoadNetwork.TwoWay(_from, _to);
            // look-ahead on the lane (into the next street once this one is nearly done)
            Vector3 target = _roads.LanePoint(from, to, Mathf.Min(1f, t + 12f / segLen), twoWay);
            float arrive = _to.junction ? 9f : 6f;
            if ((flatPos - to).magnitude < arrive || t >= 0.999f)
            {
                Advance();
                from = Flat(_from.pos); to = Flat(_to.pos);
                target = _roads.LanePoint(from, to, 0.35f, RoadNetwork.TwoWay(_from, _to));
            }
            // ease off for intersections and sharp bends, not every bend node on a curving street
            float nearEnd = Vector3.Distance(flatPos, to);
            float speed = cruise;
            if (nearEnd < 20f && _to.junction) speed = cruise * 0.6f;
            else if (nearEnd < 14f && _to.links.Count > 0)
            {
                var after = _next ?? (_to.links[0] == _from && _to.links.Count > 1 ? _to.links[1] : _to.links[0]);
                float turn = Vector3.Angle(to - from, Flat(after.pos) - to);
                if (turn > 35f) speed = cruise * 0.65f;
            }
            bool holding = HoldAt(v, flatPos, from, out float limit);
            speed = Mathf.Min(speed, limit);
            SteerTo(v, target, speed, out throttle, out steer, out handbrake);
            Holding = holding || queuedBehindHolder && v.ForwardSpeed < 2f;
            if (holding && v.ForwardSpeed < 1.2f) { handbrake = true; throttle = 0f; }
        }

        /// <summary>
        /// Should we wait at the junction we're heading for, and if so how fast may we go (easing down to stop with
        /// our nose on the stop line)? Signals, giving way, people on the crossings, and a full exit.
        /// </summary>
        bool HoldAt(Vehicle v, Vector3 pos, Vector3 from, out float limit)
        {
            limit = float.MaxValue;
            // the junction ahead: the node we're heading for, or the one just past it when that's only a bend in
            // the street close before a junction (the real-KL streets have plenty)
            var j = _to.control;
            var approach = _from;
            var exit = _next;
            if (j == null && _next != null && _next.control != null && (Flat(_next.pos) - Flat(_to.pos)).sqrMagnitude < 16f * 16f)
            {
                j = _next.control;
                approach = _to;
                exit = null;
            }
            if (j == null) return false;
            from = Flat(approach.pos);
            var jp = Flat(j.pos);
            float dist = Vector3.Distance(pos, jp);
            float stopAt = j.stopDist + v.halfLength + 0.4f;
            // well away, or already over the line (in the box: get out of it)
            if (dist > stopAt + 45f || dist < stopAt - 1.5f) { _holdT = 0f; return false; }
            float gap = dist - stopAt;
            float dt = Time.fixedDeltaTime;
            int arm = JunctionControl.ArmOf(from - jp);
            bool hold = false, red = false;

            if (j.signal)
            {
                var light = j.LightFor(arm);
                if (light == TrafficSignals.Light.Red) hold = red = true;
                else if (light == TrafficSignals.Light.Amber)
                {
                    float fs = Mathf.Max(0f, v.ForwardSpeed);
                    if (gap > fs * fs / (2f * 4.5f) - 0.5f) hold = red = true;          // room to stop: stop
                    else if (!_countedAmber) { _countedAmber = true; TrafficSignals.RanAmber++; }
                }
            }
            else if (gap < 14f)
            {
                // give way: anyone in the box, or coming in on our right (we keep left, so they have the road)
                if ((_checkT -= dt) <= 0f)
                {
                    _checkT = 0.2f;
                    _giveWay = BoxBusy(v, j, jp, (jp - pos).normalized);
                }
                if (_giveWay && _holdT < 4f) hold = true;              // (not for ever: four of us at a crossroads)
                if (hold && !_countedStop && v.ForwardSpeed < 0.6f) { _countedStop = true; TrafficSignals.Yielded++; }
            }

            // people on the zebra we're about to cross (this arm's), or the one on the street we're turning into
            if (!hold && gap < 25f)
            {
                var c1 = j.CrossingOn(approach, arm);
                var c2 = exit != null ? j.CrossingOn(exit, JunctionControl.ArmOf(Flat(exit.pos) - jp)) : null;
                if ((c1 != null && c1.busy > 0 || c2 != null && c2 != c1 && c2.busy > 0) && _holdT < 20f)
                {
                    hold = true;
                    if (!_countedStop && v.ForwardSpeed < 0.6f) { _countedStop = true; TrafficSignals.WaitedForPeople++; }
                }
            }

            // a box junction: don't go in unless there's room on the far side
            if (!hold && j.grid && gap < 8f && exit != null)
            {
                if ((_exitT -= dt) <= 0f)
                {
                    _exitT = 0.25f;
                    _exitBlocked = ExitBlocked(v, j, jp, Flat(exit.pos));
                }
                if (_exitBlocked && _holdT < 8f) hold = true;
            }

            if (!hold) { _holdT = 0f; return false; }
            _holdT += dt;
            if (red && !_countedStop && v.ForwardSpeed < 0.6f) { _countedStop = true; TrafficSignals.StoppedAtRed++; }
            // ease down to rest with the nose on the line
            limit = Mathf.Max(0f, Mathf.Sqrt(2f * 3.2f * Mathf.Max(0f, gap)) - 0.8f);
            return true;
        }

        static readonly int VehicleMask = 1 << Layers.Vehicle;

        /// <summary>Someone moving through the box, or coming at it fast from our right.</summary>
        static bool BoxBusy(Vehicle me, JunctionControl j, Vector3 jp, Vector3 heading)
        {
            var right = new Vector3(heading.z, 0f, -heading.x);
            int n = Physics.OverlapSphereNonAlloc(jp + Vector3.up, j.boxRadius + 22f, Near, VehicleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = Near[i].attachedRigidbody;
                if (rb == null || rb == me.Body) continue;
                var rel = Flat(rb.position - jp);
                float d = rel.magnitude;
                if (d < j.boxRadius * 0.9f) { if (rb.linearVelocity.sqrMagnitude > 0.5f) return true; continue; }
                if (d < 0.1f) continue;
                float closing = -Vector3.Dot(Flat(rb.linearVelocity), rel / d);
                if (closing > 3f && Vector3.Dot(rel / d, right) > 0.7f && d / closing < 4f) return true;
            }
            return false;
        }

        /// <summary>Is the street we're turning into jammed just past the junction?</summary>
        static bool ExitBlocked(Vehicle me, JunctionControl j, Vector3 jp, Vector3 nextPos)
        {
            var dir = nextPos - jp;
            if (dir.sqrMagnitude < 1f) return false;
            dir.Normalize();
            var left = new Vector3(-dir.z, 0f, dir.x);
            var spot = jp + dir * (j.boxRadius + 8f) + left * 2.6f + Vector3.up;
            int n = Physics.OverlapSphereNonAlloc(spot, 3.2f, Near, VehicleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = Near[i].attachedRigidbody;
                if (rb != null && rb != me.Body && rb.linearVelocity.sqrMagnitude < 4f) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Hunts the player down the roads (A* over the road graph, replanned as you move), and rams you
    /// once it has you in sight. Aims slightly ahead of where you're going.
    /// </summary>
    public class PoliceDriver : AIDriver
    {
        readonly RoadNetwork _roads;
        List<Vector3> _route;
        int _index;
        float _replan, _sightCheck;
        bool _inSight;
        const float RamDist = 28f, SightDist = 90f;

        public PoliceDriver(RoadNetwork roads = null)
        {
            _roads = roads ?? GameManager.I?.City?.roads;
            avoid = false;
        }

        public override void Drive(Vehicle v, out float throttle, out float steer, out bool handbrake)
        {
            var p = PlayerController.I;
            if (p == null) { throttle = steer = 0; handbrake = true; return; }
            float dt = Time.fixedDeltaTime;
            Vector3 target = p.Focus + p.Velocity * 0.6f;
            float dist = Vector3.Distance(v.transform.position, target);

            // can we see them? (buildings are on the World layer; a quick line check a few times a second)
            if ((_sightCheck -= dt) <= 0f)
            {
                _sightCheck = 0.3f;
                _inSight = dist < SightDist && !Physics.Linecast(v.transform.position + Vector3.up * 1.2f, p.Focus + Vector3.up * 0.4f,
                    1 << Layers.World, QueryTriggerInteraction.Ignore);
            }

            if (_roads == null || dist < RamDist || _inSight)
            {
                // in sight: straight for them
                _route = null;
                float speed = dist < 8f ? Mathf.Max(3f, p.Speed) : v.maxSpeed * 0.92f;
                SteerTo(v, target, speed, out throttle, out steer, out handbrake);
                if (dist > 25f && Mathf.Abs(steer) > 0.9f && v.ForwardSpeed > 15f) handbrake = true;
                return;
            }

            // out of sight: follow the streets to where they are
            if ((_replan -= dt) <= 0f || _route == null || _index >= _route.Count)
            {
                _replan = 1.5f;
                _route = _roads.Route(new[] { v.transform.position, p.Focus }, lane: 1.2f);
                _index = 0;
            }
            var pos = Flat(v.transform.position);
            // drop the points we've already passed (or were never going to reach after a shunt)
            while (_index < _route.Count - 1)
            {
                Vector3 a = Flat(_route[_index]), b = Flat(_route[_index + 1]);
                bool reached = (pos - a).sqrMagnitude < 10f * 10f;
                bool passed = Vector3.Dot(a - pos, b - a) < 0f && (pos - a).sqrMagnitude < 30f * 30f;
                if (!reached && !passed) break;
                _index++;
            }
            var aim = _route[Mathf.Min(_index, _route.Count - 1)];
            SteerTo(v, aim, v.maxSpeed * 0.92f, out throttle, out steer, out handbrake);
            if (Mathf.Abs(steer) > 0.9f && v.ForwardSpeed > 18f) handbrake = true;     // swing round the corner
        }
    }

    /// <summary>Mission target: races around the grid, turning away from the player.</summary>
    public class FleeDriver : AIDriver
    {
        readonly RoadNetwork _roads;
        RoadNetwork.Node _from, _to;
        public float cruise = 20f;

        public FleeDriver(RoadNetwork roads) { _roads = roads; }

        public override void Drive(Vehicle v, out float throttle, out float steer, out bool handbrake)
        {
            var flat = Flat(v.transform.position);
            if (_to == null) { _from = _roads.Nearest(v.transform.position); _to = PickAway(_from, null); }
            if ((flat - Flat(_to.pos)).magnitude < 11f)
            {
                var next = PickAway(_to, _from);
                _from = _to;
                _to = next;
            }
            // look a little way down the street (patch streets bend every few metres)
            Vector3 a = Flat(_from.pos), b = Flat(_to.pos);
            float len = Mathf.Max(0.5f, Vector3.Distance(a, b));
            float t = Mathf.Clamp01(Vector3.Dot(flat - a, (b - a) / len) / len + 10f / len);
            var target = _roads.LanePoint(a, b, t, RoadNetwork.TwoWay(_from, _to));
            float d = PlayerController.I ? Vector3.Distance(Flat(PlayerController.I.Focus), flat) : 100f;
            SteerTo(v, target, d < 40f ? cruise * 1.15f : cruise, out throttle, out steer, out handbrake);
        }

        RoadNetwork.Node PickAway(RoadNetwork.Node at, RoadNetwork.Node prev)
        {
            var p = PlayerController.I ? PlayerController.I.Focus : Vector3.zero;
            Vector3 toPlayer = Flat(p - at.pos);
            float playerDist = toPlayer.magnitude;
            RoadNetwork.Node best = null;
            float bestScore = float.MinValue;
            foreach (var l in at.links)
            {
                if (l == prev && at.links.Count > 1) continue;
                // look one street further on: a turn that leads to a dead end or straight back is a bad turn
                float onward = Vector3.Distance(Flat(l.pos), Flat(p));
                foreach (var m in l.links)
                    if (m != at) onward = Mathf.Max(onward, Vector3.Distance(Flat(m.pos), Flat(p)));
                if (l.links.Count == 0 || l.links.Count == 1 && l.links[0] == at) onward -= 60f;
                float score = Vector3.Distance(Flat(l.pos), Flat(p)) * 0.5f + onward * 0.5f + Random.Range(0f, 25f);
                // never turn towards the player when they're close behind
                if (playerDist < 80f && Vector3.Dot(Flat(l.pos - at.pos).normalized, toPlayer / Mathf.Max(0.1f, playerDist)) > 0.6f) score -= 80f;
                if (score > bestScore) { bestScore = score; best = l; }
            }
            return best ?? at;
        }
    }

    /// <summary>Drives a fixed list of points (follow missions, races).</summary>
    public class RouteDriver : AIDriver
    {
        public readonly List<Vector3> route;
        public int index;
        public float cruise = 14f;
        public bool loop;
        public float arriveRadius = 9f;
        public bool Finished => index >= route.Count;
        public System.Func<float> speedOverride;

        public RouteDriver(List<Vector3> route, float cruise)
        {
            this.route = route;
            this.cruise = cruise;
        }

        public override void Drive(Vehicle v, out float throttle, out float steer, out bool handbrake)
        {
            if (Finished) { throttle = -0.3f; steer = 0; handbrake = true; return; }
            var flat = v.transform.position; flat.y = route[index].y;
            // knocked wide of a point (a crash, a jostle in the race)? if it's already behind us, carry on to
            // the next rather than turning round for it
            while (index + 1 < route.Count && (flat - route[index]).magnitude >= arriveRadius)
            {
                Vector3 a = Flat(route[index]), b = Flat(route[index + 1]), here = Flat(flat);
                if (Vector3.Dot(a - here, b - a) >= 0f || (here - a).sqrMagnitude > 35f * 35f) break;
                index++;
            }
            if ((flat - route[index]).magnitude < arriveRadius)
            {
                index++;
                if (index >= route.Count)
                {
                    if (loop) index = 0;
                    else { throttle = 0; steer = 0; handbrake = true; return; }
                }
            }
            float speed = speedOverride != null ? speedOverride() : cruise;
            SteerTo(v, route[index], speed, out throttle, out steer, out handbrake);
        }
    }

    /// <summary>Reads the player's controller.</summary>
    public class PlayerDriver : IDriver
    {
        public void Drive(Vehicle v, out float throttle, out float steer, out bool handbrake)
        {
            throttle = GameInput.Throttle;
            steer = GameInput.Steer;
            handbrake = GameInput.Handbrake;
        }
    }
}
