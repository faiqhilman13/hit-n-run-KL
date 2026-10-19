using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// A zebra crossing over one arm of a junction: the two kerbs, the pavement loops either side (pedestrians
    /// walk from one loop over to the other here), and how many people are out on the road right now (cars wait
    /// for them). At a signalled junction people cross on the green man; elsewhere when no car is coming.
    /// </summary>
    public class Crossing
    {
        public Vector3 centre;          // middle of the zebra, on the road
        public Vector3 across;          // unit, from side a to side b
        public float halfWidth;         // road edge to the middle
        public JunctionControl junction;
        public int arm;                 // which arm of the junction it crosses (0 N, 1 E, 2 S, 3 W)
        public WalkZone za, zb;         // the pavement loops at either end (null: nobody walks over)
        public float ua, ub;            // where on those loops it starts
        public int busy;                // people on the road now
        public float lastUsed = -99f;

        /// <summary>The kerb-side spot to wait at on side a (sign -1) or b (+1).</summary>
        public Vector3 Kerb(int side, float lane = 0f) => centre + across * side * (halfWidth + 0.7f) + Vector3.Cross(Vector3.up, across) * lane;
        /// <summary>A point on the road at the edge on side a (-1) or b (+1).</summary>
        public Vector3 Edge(int side, float lane = 0f) => centre + across * side * (halfWidth - 0.3f) + Vector3.Cross(Vector3.up, across) * lane;
    }

    /// <summary>
    /// What a driver needs to know about a junction: is it signalled (and its phase), where the stop lines and
    /// the box are, and the crossings over its arms. Grid junctions all have one; so do the bigger junctions in
    /// the real-KL streets (no signals or crossings there, just give way).
    /// </summary>
    public class JunctionControl
    {
        public Vector3 pos;
        public bool signal;
        public float offset;            // 0..1 of the cycle, so neighbouring junctions don't all change together
        public float stopDist = 17f;    // centre to the stop lines
        public float boxRadius = 10f;   // centre to the edge of the box
        public bool grid;
        public readonly Crossing[] crossings = new Crossing[4];
        /// <summary>The real-KL streets' junctions: crossings by the neighbouring node of the street they're on.</summary>
        public Dictionary<RoadNetwork.Node, Crossing> byNode;

        /// <summary>The crossing on the street to/from `nbr` (arm: its grid arm, for grid junctions).</summary>
        public Crossing CrossingOn(RoadNetwork.Node nbr, int arm)
        {
            if (byNode != null) return nbr != null && byNode.TryGetValue(nbr, out var c) ? c : null;
            return arm >= 0 ? crossings[arm] : null;
        }

        public static int ArmOf(Vector3 fromCentre) =>
            Mathf.Abs(fromCentre.x) > Mathf.Abs(fromCentre.z) ? (fromCentre.x > 0f ? 1 : 3) : (fromCentre.z > 0f ? 0 : 2);

        /// <summary>The light facing traffic coming in on this arm.</summary>
        public TrafficSignals.Light LightFor(int arm) => signal ? TrafficSignals.State(this, arm == 0 || arm == 2) : TrafficSignals.Light.Green;

        public bool WalkNow(out float left) { left = 0f; return signal && TrafficSignals.Walk(this, out left); }
    }

    /// <summary>
    /// The city's traffic signals: one clock for all of them (the lamps' shader reads the same numbers), a phase
    /// each way, then an all-red with the green man for pedestrians on every arm. Plus the counters the road
    /// capture logs (cars that stopped at a red, people who crossed, reds the player ran).
    /// </summary>
    public static class TrafficSignals
    {
        public enum Light { Green, Amber, Red }

        public const float GreenTime = 11f, AmberTime = 3f, AllRed = 1.5f, WalkTime = 7f;
        public static float HalfCycle => GreenTime + AmberTime + AllRed;
        public static float Cycle => 2f * HalfCycle + WalkTime + AllRed;

        public static readonly List<JunctionControl> Junctions = new List<JunctionControl>();
        public static readonly List<Crossing> Crossings = new List<Crossing>();
        static JunctionControl[,] _grid;

        // counters
        public static int StoppedAtRed, RanAmber, Yielded, WaitedForPeople, PedsStarted, PedsCrossed, PlayerRedRuns;

        public static float Clock => Time.time;

        public static void Clear()
        {
            Junctions.Clear();
            Crossings.Clear();
            _grid = new JunctionControl[CityBuilder.NX + 1, CityBuilder.NZ + 1];
            StoppedAtRed = RanAmber = Yielded = WaitedForPeople = PedsStarted = PedsCrossed = PlayerRedRuns = 0;
        }

        public static void AddGrid(int i, int k, JunctionControl j)
        {
            if (_grid == null) Clear();
            j.grid = true;
            _grid[i, k] = j;
            Junctions.Add(j);
        }

        public static JunctionControl GridAt(int i, int k) =>
            _grid != null && i >= 0 && k >= 0 && i <= CityBuilder.NX && k <= CityBuilder.NZ ? _grid[i, k] : null;

        /// <summary>The grid junction whose box p is in or near (within r of the centre).</summary>
        public static JunctionControl GridNear(Vector3 p, float r)
        {
            int i = Mathf.RoundToInt((p.x - CityBuilder.X0) / CityBuilder.Pitch), k = Mathf.RoundToInt((p.z - CityBuilder.Z0) / CityBuilder.Pitch);
            var j = GridAt(i, k);
            if (j == null) return null;
            float dx = p.x - j.pos.x, dz = p.z - j.pos.z;
            return dx * dx + dz * dz < r * r ? j : null;
        }

        static float T(JunctionControl j) => Mathf.Repeat(Clock + j.offset * Cycle, Cycle);

        public static Light State(JunctionControl j, bool ns)
        {
            float t = T(j) - (ns ? 0f : HalfCycle);
            if (t >= 0f && t < GreenTime) return Light.Green;
            if (t >= GreenTime && t < GreenTime + AmberTime) return Light.Amber;
            return Light.Red;
        }

        /// <summary>The green man is lit (and how long he has left).</summary>
        public static bool Walk(JunctionControl j, out float left)
        {
            float t = T(j) - 2f * HalfCycle;
            left = WalkTime - t;
            return t >= 0f && t < WalkTime;
        }

        /// <summary>For the capture log: the junction near p, its lights each way.</summary>
        public static string Describe(Vector3 p)
        {
            var j = GridNear(p, 20f);
            if (j == null) return "none";
            if (!j.signal) return "unsignalled";
            Walk(j, out float left);
            return $"NS {State(j, true)} EW {State(j, false)} walk {(Walk(j, out _) ? $"on {left:F1}s" : "off")}";
        }

        public static string Stats() =>
            $"signals {Junctions.FindAll(j => j.signal).Count}/{Junctions.Count} junctions, crossings {Crossings.Count} " +
            $"(linked {Crossings.FindAll(c => c.za != null && c.zb != null).Count}); stopped at red {StoppedAtRed}, ran amber {RanAmber}, " +
            $"gave way {Yielded}, waited for people {WaitedForPeople}; people started crossing {PedsStarted}, crossed {PedsCrossed}; player reds {PlayerRedRuns}";

        /// <summary>Pushes the clock to the lamps' shader every frame, and watches the player for running reds.</summary>
        public class SignalClock : MonoBehaviour
        {
            static readonly int ClockId = Shader.PropertyToID("_SignalClock"), PhaseId = Shader.PropertyToID("_SignalPhase");
            JunctionControl _in;
            int _inArm;
            bool _inRed;

            void Awake() => Shader.SetGlobalVector(PhaseId, new Vector4(GreenTime, AmberTime, AllRed, WalkTime));

            void Update()
            {
                Shader.SetGlobalFloat(ClockId, Clock);
                WatchPlayer();
            }

            /// <summary>Into a signalled box against a red (not amber), at speed: the Meter Saman notices.</summary>
            void WatchPlayer()
            {
                var pc = PlayerController.I;
                if (pc == null || !pc.Driving || pc.vehicle == null) { _in = null; return; }
                var pos = pc.vehicle.transform.position;
                var j = GridNear(pos, 40f);
                if (j == null || !j.signal) { _in = null; return; }
                var d = pos - j.pos; d.y = 0f;
                float dist = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z));
                if (dist > j.boxRadius + 0.5f)
                {
                    // outside the box: remember which arm we're on and what it shows, for when we cross into it
                    _in = j;
                    _inArm = JunctionControl.ArmOf(d);
                    _inRed = j.LightFor(_inArm) == Light.Red && Vector3.Dot(pc.vehicle.Body.linearVelocity, -d.normalized) > 4f;
                    return;
                }
                if (_in == j && _inRed)
                {
                    PlayerRedRuns++;
                    if (SamanMeter.I != null) SamanMeter.I.AddHeat(14f);
                    Fx.Word(pos + Vector3.up * 3f, "LAMPU MERAH!");
                }
                _in = null;
                _inRed = false;
            }
        }
    }
}
