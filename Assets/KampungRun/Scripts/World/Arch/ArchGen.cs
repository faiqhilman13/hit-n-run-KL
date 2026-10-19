using System.Collections.Generic;
using UnityEngine;

namespace KampungRun.Arch
{
    /// <summary>Picks the generator for a building, and gives physics its solid shape.</summary>
    public static class ArchGen
    {
        public static void Build(BuildingSpec s, ArchMesh m, bool detail)
        {
            switch (s.kind)
            {
                case BuildingKind.Shop:
                case BuildingKind.House:
                    Shophouse.Build(s, m, detail);
                    break;
                default:
                    Block.Build(s, m, detail);
                    break;
            }
        }

        /// <summary>
        /// The building's solid for physics: its footprint stood up to the roof, except a shophouse's five-foot way,
        /// which is cut out of the ground floor (you can walk it) leaving its columns.
        /// </summary>
        public static void Collider(BuildingSpec s, List<Vector3> v, List<int> t)
        {
            float top = s.height + (s.flatRoof ? 0.05f : 0.6f);
            if (s.kind == BuildingKind.Shop && Interior.RoomsOf(s).Count > 0 && Shophouse.Arcade(s, out int sf, out float sa, out float sg))
            {
                ShopWithRooms(s, sf, sa, sg, top, v, t);
                return;
            }
            if ((s.kind == BuildingKind.Shop || s.kind == BuildingKind.House) && Shophouse.Arcade(s, out int front, out float A, out float G) && A > 0f)
            {
                var cut = CutFront(s.ring, front, A);
                Prism(cut, s.y0, s.y0 + G, v, t);
                Prism(s.ring, s.y0 + G, s.y0 + top, v, t);
                // the columns at the front corners
                Vector2 p0 = s.ring[front], p1 = s.ring[(front + 1) % s.ring.Length];
                var d = (p1 - p0).normalized;
                var inward = new Vector2(-d.y, d.x);
                foreach (var c in new[] { p0 + d * 0.25f + inward * 0.25f, p1 - d * 0.25f + inward * 0.25f })
                    Prism(new[] { c + new Vector2(-0.25f, -0.25f), c + new Vector2(0.25f, -0.25f), c + new Vector2(0.25f, 0.25f), c + new Vector2(-0.25f, 0.25f) },
                          s.y0, s.y0 + G, v, t);
                return;
            }
            Prism(s.ring, s.y0, s.y0 + top, v, t);
        }

        /// <summary>
        /// A shophouse with walk-in shops: the floors above solid down to the shop ceilings; at street level a shell
        /// (thin walls round the footprint behind the five-foot way, the shopfront wall with its doorways open) and each
        /// room's own walls, so the open shops are hollow and the closed bays stay out of reach.
        /// </summary>
        static void ShopWithRooms(BuildingSpec s, int front, float A, float G, float top, List<Vector3> v, List<int> t)
        {
            var ring = s.ring;
            int n = ring.Length;
            var rooms = Interior.RoomsOf(s);
            float ceil = s.y0 + G - 0.15f;
            Prism(ring, ceil, s.y0 + top, v, t);
            var ground = A > 0f ? CutFront(ring, front, A) : ring;
            for (int i = 0; i < n; i++)
                if (i != front) Solid.EdgeWall(v, t, ground[i], ground[(i + 1) % n], s.y0 - 0.3f, ceil + 0.05f, 0.2f);
            // the shopfront wall, Interior.Wall thick, with each room's doorways left open
            Vector2 p0 = ring[front], p1 = ring[(front + 1) % n];
            var f = Frame.Edge(p0, p1, s.y0);
            float W = (p1 - p0).magnitude;
            _gaps.Clear();
            foreach (var r in rooms)
            {
                float rx = Vector3.Dot(r.f.o - f.o, f.r);
                foreach (var o in r.openings)
                {
                    if (o.wall != 0 || !o.door) continue;
                    float g0 = rx + o.c0 + (o.folding ? 0.3f : 0f), g1 = rx + o.c1 - (o.folding ? 0.3f : 0f);
                    _gaps.Add((g0, g1, o.y1));
                }
            }
            _gaps.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            float run = 0f, z0 = -A - Interior.Wall, z1 = -A;
            foreach (var (g0, g1, gy) in _gaps)
            {
                if (g0 > run) Solid.Box(v, t, f, run, g0, -0.3f, G - 0.1f, z0, z1);
                Solid.Box(v, t, f, g0, g1, gy + 0.02f, G - 0.1f, z0, z1);
                run = g1;
            }
            if (W > run) Solid.Box(v, t, f, run, W, -0.3f, G - 0.1f, z0, z1);
            foreach (var r in rooms) Interior.RoomWalls(r, v, t, false);
            if (A > 0f)
            {
                var d = (p1 - p0).normalized;
                var inward = new Vector2(-d.y, d.x);
                foreach (var c in new[] { p0 + d * 0.25f + inward * 0.25f, p1 - d * 0.25f + inward * 0.25f })
                    Prism(new[] { c + new Vector2(-0.25f, -0.25f), c + new Vector2(0.25f, -0.25f), c + new Vector2(0.25f, 0.25f), c + new Vector2(-0.25f, 0.25f) },
                          s.y0, s.y0 + G, v, t);
            }
        }

        static readonly List<(float, float, float)> _gaps = new List<(float, float, float)>(4);

        /// <summary>The footprint with its front edge pushed back by depth (along the two side edges).</summary>
        public static Vector2[] CutFront(Vector2[] ring, int front, float depth)
        {
            int n = ring.Length;
            var r = (Vector2[])ring.Clone();
            Vector2 p0 = ring[front], p1 = ring[(front + 1) % n];
            var d = (p1 - p0).normalized;
            var inward = new Vector2(-d.y, d.x);
            Vector2 prev = ring[(front + n - 1) % n], next = ring[(front + 2) % n];
            var a = (prev - p0); float la = a.magnitude; a /= Mathf.Max(1e-4f, la);
            var b = (next - p1); float lb = b.magnitude; b /= Mathf.Max(1e-4f, lb);
            r[front] = p0 + a * Mathf.Min(la * 0.9f, depth / Mathf.Max(0.2f, Vector2.Dot(a, inward)));
            r[(front + 1) % n] = p1 + b * Mathf.Min(lb * 0.9f, depth / Mathf.Max(0.2f, Vector2.Dot(b, inward)));
            return r;
        }

        static readonly List<int> _tri = new List<int>(32);

        public static void Prism(IList<Vector2> ring, float y0, float y1, List<Vector3> v, List<int> t)
        {
            int n = ring.Count, b = v.Count;
            for (int i = 0; i < n; i++) { v.Add(new Vector3(ring[i].x, y0, ring[i].y)); v.Add(new Vector3(ring[i].x, y1, ring[i].y)); }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int a0 = b + i * 2, a1 = a0 + 1, c0 = b + j * 2, c1 = c0 + 1;
                // outward-facing (counter-clockwise ring seen from above)
                t.Add(a0); t.Add(a1); t.Add(c1);
                t.Add(a0); t.Add(c1); t.Add(c0);
            }
            ArchMesh.Triangulate(ring, _tri);
            for (int i = 0; i + 2 < _tri.Count; i += 3) { t.Add(b + _tri[i] * 2 + 1); t.Add(b + _tri[i + 2] * 2 + 1); t.Add(b + _tri[i + 1] * 2 + 1); }
        }
    }
}
