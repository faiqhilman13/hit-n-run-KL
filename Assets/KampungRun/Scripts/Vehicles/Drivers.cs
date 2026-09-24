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
                                                "hilux", "van", "basmini", "foodtruck" };
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
            if (id == "kancil" || id == "saga" || id == "myvi" || id == "hilux" || id == "van")
                ModelFactory.Recolor(v.gameObject, new Dictionary<string, Color> { ["car_paint"] = Paints[Random.Range(0, Paints.Length)] });
            AddDriver(v);
            v.maxSpeed *= 0.6f;
            return v;
        }

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
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Vehicle;
        }
    }

    /// <summary>Shared steering maths for AI drivers.</summary>
    public abstract class AIDriver : IDriver
    {
        protected float stuckTime, reverseTime;
        public float speedScale = 1f;
        public bool avoid = true;

        public abstract void Drive(Vehicle v, out float throttle, out float steer, out bool handbrake);

        protected void SteerTo(Vehicle v, Vector3 target, float desiredSpeed, out float throttle, out float steer, out bool handbrake)
        {
            handbrake = false;
            Vector3 local = v.transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            steer = Mathf.Clamp(angle / 30f, -1f, 1f);
            float fs = v.ForwardSpeed;
            float speed = desiredSpeed * speedScale;
            // slow for sharp turns
            speed *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp01((Mathf.Abs(angle) - 15f) / 60f));

            if (avoid)
            {
                var origin = v.transform.position + Vector3.up * 0.8f + v.transform.forward * (v.halfLength + 1.3f);
                float look = 6f + Mathf.Max(0, fs) * 0.9f;
                if (Physics.SphereCast(origin, 1.1f, v.transform.forward, out var hit, look, (1 << Layers.Vehicle) | (1 << Layers.Character), QueryTriggerInteraction.Collide)
                    && hit.rigidbody != v.Body)
                    speed = Mathf.Min(speed, Mathf.Max(0f, (hit.distance - 3f) * 0.8f));
            }

            throttle = Mathf.Clamp((speed - fs) * 0.35f, -1f, 1f);
            if (speed < 0.5f && fs < 1f) throttle = 0f;

            // unstick: if we want to move but can't, back up while counter-steering
            if (reverseTime > 0f)
            {
                reverseTime -= Time.fixedDeltaTime;
                throttle = -1f;
                steer = -steer;
                return;
            }
            if (speed > 3f && Mathf.Abs(fs) < 0.6f) stuckTime += Time.fixedDeltaTime;
            else stuckTime = 0f;
            if (stuckTime > 2.2f) { stuckTime = 0f; reverseTime = 1.3f; }
        }
    }

    /// <summary>Cruises the road grid on the left-hand lane, picking random turns.</summary>
    public class TrafficDriver : AIDriver
    {
        readonly RoadNetwork _roads;
        RoadNetwork.Node _from, _to;
        public float cruise = 11f;

        public TrafficDriver(RoadNetwork roads, RoadNetwork.Node start = null)
        {
            _roads = roads;
            _from = start;
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
            }
            Vector3 flatPos = v.transform.position; flatPos.y = 0;
            float segLen = Vector3.Distance(_from.pos, _to.pos);
            float t = Mathf.Clamp01(Vector3.Dot(flatPos - _from.pos, (_to.pos - _from.pos).normalized) / segLen);
            // look-ahead on the lane
            Vector3 target = _roads.LanePoint(_from.pos, _to.pos, Mathf.Min(1f, t + 12f / segLen));
            if ((flatPos - _to.pos).magnitude < 10f)
            {
                var next = _roads.NextFrom(_to, _from);
                _from = _to;
                _to = next;
                target = _roads.LanePoint(_from.pos, _to.pos, 0.25f);
            }
            float nearEnd = Vector3.Distance(flatPos, _to.pos);
            float speed = nearEnd < 20f ? cruise * 0.6f : cruise;
            SteerTo(v, target, speed, out throttle, out steer, out handbrake);
        }
    }

    /// <summary>Rams the player. Aims slightly ahead of where you're going.</summary>
    public class PoliceDriver : AIDriver
    {
        public PoliceDriver() { avoid = false; }

        public override void Drive(Vehicle v, out float throttle, out float steer, out bool handbrake)
        {
            var p = PlayerController.I;
            if (p == null) { throttle = steer = 0; handbrake = true; return; }
            Vector3 target = p.Focus + p.Velocity * 0.6f;
            float dist = Vector3.Distance(v.transform.position, target);
            float speed = dist < 8f ? Mathf.Max(3f, p.Speed) : v.maxSpeed * 0.92f;
            SteerTo(v, target, speed, out throttle, out steer, out handbrake);
            if (dist > 25f && Mathf.Abs(steer) > 0.9f && v.ForwardSpeed > 15f) handbrake = true;
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
            var flat = v.transform.position; flat.y = 0;
            if (_to == null) { _from = _roads.Nearest(flat); _to = PickAway(_from, null); }
            if ((flat - _to.pos).magnitude < 11f)
            {
                var next = PickAway(_to, _from);
                _from = _to;
                _to = next;
            }
            var target = _roads.LanePoint(_from.pos, _to.pos, 0.85f);
            float d = PlayerController.I ? Vector3.Distance(PlayerController.I.Focus, flat) : 100f;
            SteerTo(v, target, d < 40f ? cruise * 1.15f : cruise, out throttle, out steer, out handbrake);
        }

        RoadNetwork.Node PickAway(RoadNetwork.Node at, RoadNetwork.Node prev)
        {
            var p = PlayerController.I ? PlayerController.I.Focus : Vector3.zero;
            RoadNetwork.Node best = null;
            float bestScore = float.MinValue;
            foreach (var l in at.links)
            {
                if (l == prev && at.links.Count > 1) continue;
                float score = Vector3.Distance(l.pos, p) + Random.Range(0f, 40f);
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
