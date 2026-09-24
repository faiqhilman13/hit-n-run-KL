using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>Intersections on the city grid and the roads between them.</summary>
    public class RoadNetwork
    {
        public class Node
        {
            public int id;
            public Vector3 pos;
            public readonly List<Node> links = new List<Node>();
        }

        public readonly List<Node> nodes = new List<Node>();
        public float laneOffset = 3.2f; // Malaysia drives on the LEFT

        public Node Add(Vector3 p)
        {
            var n = new Node { id = nodes.Count, pos = p };
            nodes.Add(n);
            return n;
        }

        public static void Link(Node a, Node b)
        {
            if (!a.links.Contains(b)) a.links.Add(b);
            if (!b.links.Contains(a)) b.links.Add(a);
        }

        public Node Nearest(Vector3 p)
        {
            Node best = null;
            float bd = float.MaxValue;
            foreach (var n in nodes)
            {
                float d = (n.pos - p).sqrMagnitude;
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        public Node NextFrom(Node current, Node previous)
        {
            if (current.links.Count == 0) return current;
            // prefer not to U-turn
            var options = new List<Node>();
            foreach (var l in current.links) if (l != previous) options.Add(l);
            if (options.Count == 0) return previous;
            // mild bias to go straight
            if (previous != null)
            {
                Vector3 inDir = (current.pos - previous.pos).normalized;
                foreach (var o in options)
                    if (Vector3.Dot((o.pos - current.pos).normalized, inDir) > 0.9f && Random.value < 0.45f) return o;
            }
            return options[Random.Range(0, options.Count)];
        }

        /// <summary>A point in the left-hand lane of the road from a to b.</summary>
        public Vector3 LanePoint(Vector3 a, Vector3 b, float t)
        {
            Vector3 dir = (b - a);
            dir.y = 0;
            dir.Normalize();
            Vector3 left = new Vector3(-dir.z, 0, dir.x);
            return Vector3.Lerp(a, b, t) + left * laneOffset;
        }

        public Node RandomNode() => nodes[Random.Range(0, nodes.Count)];

        public Node RandomNodeAwayFrom(Vector3 p, float minDist, float maxDist = 9999f)
        {
            for (int i = 0; i < 40; i++)
            {
                var n = RandomNode();
                float d = Vector3.Distance(n.pos, p);
                if (d >= minDist && d <= maxDist) return n;
            }
            return RandomNode();
        }
    }
}
