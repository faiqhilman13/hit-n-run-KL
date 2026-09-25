using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// The drivable road graph: the grid intersections and the real-KL streets in the patches
    /// (with their bends as extra nodes). Links are directed: a two-way street links both ways,
    /// a one-way street or a roundabout only the way you may drive.
    /// </summary>
    public class RoadNetwork
    {
        public class Node
        {
            public int id;
            public Vector3 pos;
            public bool junction = true;                  // a real intersection (traffic slows for these)
            public readonly List<Node> links = new List<Node>();   // where you can drive to next
        }

        public readonly List<Node> nodes = new List<Node>();
        public float laneOffset = 3.2f; // Malaysia drives on the LEFT

        public Node Add(Vector3 p, bool junction = true)
        {
            var n = new Node { id = nodes.Count, pos = p, junction = junction };
            nodes.Add(n);
            _grid = null;
            return n;
        }

        public static void Link(Node a, Node b)
        {
            if (a == b) return;
            if (!a.links.Contains(b)) a.links.Add(b);
            if (!b.links.Contains(a)) b.links.Add(a);
        }

        public static void LinkOneWay(Node a, Node b)
        {
            if (a != b && !a.links.Contains(b)) a.links.Add(b);
        }

        public static void Unlink(Node a, Node b)
        {
            a.links.Remove(b);
            b.links.Remove(a);
        }

        public static bool TwoWay(Node a, Node b) => a.links.Contains(b) && b.links.Contains(a);

        /// <summary>Put a new node on the street a-b at p (keeping which ways it runs).</summary>
        public Node Split(Node a, Node b, Vector3 p)
        {
            bool ab = a.links.Contains(b), ba = b.links.Contains(a);
            var n = Add(p, false);
            a.links.Remove(b); b.links.Remove(a);
            if (ab) { LinkOneWay(a, n); LinkOneWay(n, b); }
            if (ba) { LinkOneWay(b, n); LinkOneWay(n, a); }
            return n;
        }

        // ------------------------------------------------------------ spatial lookup
        Dictionary<Vector2Int, List<Node>> _grid;
        const float Cell = 60f;

        void BuildGrid()
        {
            _grid = new Dictionary<Vector2Int, List<Node>>();
            foreach (var n in nodes)
            {
                var k = new Vector2Int(Mathf.FloorToInt(n.pos.x / Cell), Mathf.FloorToInt(n.pos.z / Cell));
                if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<Node>();
                l.Add(n);
            }
        }

        /// <summary>The node nearest p (in plan; road-level ones preferred unless you're up on a deck;
        /// groundOnly: never a flyover deck).</summary>
        public Node Nearest(Vector3 p, bool groundOnly = false)
        {
            if (_grid == null) BuildGrid();
            Node best = null;
            float bd = float.MaxValue;
            var c = new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
            int foundAt = -1;
            for (int r = 0; r <= 60; r++)
            {
                if (foundAt >= 0 && r > foundAt + 1) break;       // one ring past the first hit: a closer one may sit over a cell edge
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        if (!_grid.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var l)) continue;
                        foreach (var n in l)
                        {
                            if (n.links.Count == 0 || groundOnly && n.pos.y > 0.5f) continue;
                            float dy = Mathf.Abs(n.pos.y - p.y);
                            float d = (n.pos.x - p.x) * (n.pos.x - p.x) + (n.pos.z - p.z) * (n.pos.z - p.z) + dy * dy * 4f;
                            if (d < bd) { bd = d; best = n; }
                        }
                    }
                if (best != null && foundAt < 0) foundAt = r;
            }
            return best ?? (nodes.Count > 0 ? nodes[0] : null);
        }

        public Node NextFrom(Node current, Node previous)
        {
            if (current.links.Count == 0) return current;
            // prefer not to U-turn
            var options = new List<Node>();
            foreach (var l in current.links) if (l != previous) options.Add(l);
            if (options.Count == 0) return current.links[0];
            if (options.Count == 1) return options[0];
            // mild bias to go straight
            if (previous != null)
            {
                Vector3 inDir = Flat(current.pos - previous.pos).normalized;
                foreach (var o in options)
                    if (Vector3.Dot(Flat(o.pos - current.pos).normalized, inDir) > 0.9f && Random.value < 0.45f) return o;
            }
            return options[Random.Range(0, options.Count)];
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        /// <summary>A point in the left-hand lane of the road from a to b (the middle of a one-way street).</summary>
        public Vector3 LanePoint(Vector3 a, Vector3 b, float t, bool twoWay = true)
        {
            Vector3 dir = (b - a);
            dir.y = 0;
            dir.Normalize();
            Vector3 left = new Vector3(-dir.z, 0, dir.x);
            return Vector3.Lerp(a, b, t) + left * (twoWay ? laneOffset : 0.8f);
        }

        public Node RandomNode()
        {
            for (int i = 0; i < 20; i++)
            {
                var n = nodes[Random.Range(0, nodes.Count)];
                if (n.links.Count > 0) return n;
            }
            return nodes[Random.Range(0, nodes.Count)];
        }

        public Node RandomNodeAwayFrom(Vector3 p, float minDist, float maxDist = 9999f)
        {
            if (maxDist < 5000f)
            {
                // the map is big: look only in the spatial cells within reach
                if (_grid == null) BuildGrid();
                int r = Mathf.CeilToInt(maxDist / Cell);
                int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
                for (int i = 0; i < 120; i++)
                {
                    if (!_grid.TryGetValue(new Vector2Int(cx + Random.Range(-r, r + 1), cz + Random.Range(-r, r + 1)), out var l) || l.Count == 0) continue;
                    var n = l[Random.Range(0, l.Count)];
                    float d = Vector3.Distance(Flat(n.pos), Flat(p));
                    if (n.links.Count > 0 && d >= minDist && d <= maxDist && n.pos.y < 0.5f) return n;
                }
            }
            for (int i = 0; i < 60; i++)
            {
                var n = RandomNode();
                float d = Vector3.Distance(Flat(n.pos), Flat(p));
                if (d >= minDist && d <= maxDist && n.pos.y < 0.5f) return n;
            }
            return RandomNode();
        }

        // ------------------------------------------------------------ routing
        /// <summary>Shortest drivable way from a to b (A*), as nodes; null if there is none.</summary>
        public List<Node> Path(Node a, Node b)
        {
            if (a == null || b == null) return null;
            var open = new SortedSet<(float f, int id)>();
            var g = new Dictionary<Node, float> { [a] = 0f };
            var from = new Dictionary<Node, Node>();
            open.Add((Vector3.Distance(a.pos, b.pos), a.id));
            var byId = nodes;
            while (open.Count > 0)
            {
                var top = open.Min;
                open.Remove(top);
                var n = byId[top.id];
                if (n == b)
                {
                    var path = new List<Node> { b };
                    while (from.TryGetValue(path[path.Count - 1], out var p)) path.Add(p);
                    path.Reverse();
                    return path;
                }
                float gn = g[n];
                foreach (var m in n.links)
                {
                    float gm = gn + Vector3.Distance(n.pos, m.pos);
                    if (g.TryGetValue(m, out var old) && old <= gm) continue;
                    if (g.ContainsKey(m)) open.Remove((old + Vector3.Distance(m.pos, b.pos), m.id));
                    g[m] = gm;
                    from[m] = n;
                    open.Add((gm + Vector3.Distance(m.pos, b.pos), m.id));
                }
            }
            return null;
        }

        /// <summary>
        /// A drivable route through the given stops, as a dense list of points along the roads in the
        /// left-hand lane (for scripted drivers and race rivals). Stops snap to the nearest road nodes;
        /// marks (if given) gets the index in the result of each stop after the first.
        /// </summary>
        public List<Vector3> Route(IList<Vector3> stops, bool loop = false, List<int> marks = null, float lane = 2.4f)
        {
            var way = new List<Node>();
            int n = stops.Count;
            for (int i = 0; i < (loop ? n : n - 1); i++)
            {
                Node a = Nearest(stops[i]), b = Nearest(stops[(i + 1) % n]);
                var path = Path(a, b) ?? new List<Node> { a, b };        // no way through: drive straight at it
                foreach (var node in path)
                    if (way.Count == 0 || way[way.Count - 1] != node) way.Add(node);
                marks?.Add(way.Count - 1);
            }
            var pts = new List<Vector3>(way.Count);
            for (int j = 0; j < way.Count; j++)
            {
                var d = Flat(way[Mathf.Min(way.Count - 1, j + 1)].pos - way[Mathf.Max(0, j - 1)].pos);
                bool twoWay = j + 1 < way.Count ? TwoWay(way[j], way[j + 1]) : j > 0 && TwoWay(way[j - 1], way[j]);
                if (d.sqrMagnitude < 0.01f || !twoWay) { pts.Add(way[j].pos); continue; }
                d.Normalize();
                pts.Add(way[j].pos + new Vector3(-d.z, 0f, d.x) * lane);
            }
            return pts;
        }
    }
}
