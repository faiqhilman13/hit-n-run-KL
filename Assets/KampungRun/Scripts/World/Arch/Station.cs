using UnityEngine;

namespace KampungRun.Arch
{
    /// <summary>
    /// An elevated KL rail station over its line: a side platform each side of the beams (or girders) on rows of
    /// columns, a barrel-vault zinc roof spanning the lot, an advert fascia along each side under the eaves (the
    /// station name is lettered on it in the game), railings and the yellow platform-edge line.
    /// </summary>
    public class StationSpec : IArchItem
    {
        public Vector3 centre;          // on the line between the two tracks, at the running surface
        public Vector3 along;           // horizontal
        public float platform;          // platform floor height (world y)
        public float inner, width;      // a platform's inner edge from the centre line, and its width
        public float length;
        public bool monorail;
        public int seed;
        public Vector2 Where() => new Vector2(centre.x, centre.z);
        public void Build(ArchMesh m, bool detail) => Station.Build(this, m, detail);

        public float?[] gaps = new float?[2];   // where a linkway meets each side's wall (along, from the centre); [0] = -across side

        public Vector3 Across => Vector3.Cross(Vector3.up, along.normalized);

        /// <summary>The stretches of a side's outer wall, along the station (the linkway's gap left out).</summary>
        public System.Collections.Generic.IEnumerable<(float, float)> WallRuns(int side)
        {
            float L = length * 0.5f;
            var gp = gaps[side > 0 ? 1 : 0];
            if (gp == null) { yield return (-L, L); yield break; }
            float g0 = gp.Value - Station.LinkWidth * 0.5f, g1 = gp.Value + Station.LinkWidth * 0.5f;
            if (g0 > -L + 0.1f) yield return (-L, g0);
            if (g1 < L - 0.1f) yield return (g1, L);
        }
        public float Outer => inner + width;
        public float Eaves => platform + 3.5f;
    }

    /// <summary>The way up from the pavement: a straight stair along the kerb under a canopy, then a covered
    /// linkway across to the platform's outer edge.</summary>
    public class StationLinkSpec : IArchItem
    {
        public Vector3 bottom, top;     // the stair: foot on the pavement, head at platform height
        public Vector3 end;             // where the linkway meets the platform (platform height)
        public Vector2 Where() => new Vector2(top.x, top.z);
        public void Build(ArchMesh m, bool detail) => Station.Link(this, m, detail);
    }

    public static class Station
    {
        public const float StairWidth = 2.4f, LinkWidth = 2.6f, Slab = 0.45f;
        static readonly Color32 Concrete = ArchMesh.Col(0.84f, 0.82f, 0.78f), Tile = ArchMesh.Col(0.86f, 0.80f, 0.70f);
        static readonly Color32 Zinc = ArchMesh.Col(0.66f, 0.71f, 0.76f), ZincUnder = ArchMesh.Col(0.88f, 0.86f, 0.82f);
        static readonly Color32 Steel = ArchMesh.Col(0.42f, 0.52f, 0.62f), Yellow = ArchMesh.Col(0.98f, 0.82f, 0.12f);
        public static readonly Color32[] Bands =
        {
            ArchMesh.Col(0.42f, 0.74f, 0.18f), ArchMesh.Col(0.86f, 0.12f, 0.14f), ArchMesh.Col(0.10f, 0.42f, 0.76f),
            ArchMesh.Col(0.96f, 0.56f, 0.08f), ArchMesh.Col(0.56f, 0.20f, 0.62f),
        };

        public static void Build(StationSpec s, ArchMesh m, bool detail)
        {
            var a = s.along.normalized;
            var x = s.Across;
            float L = s.length * 0.5f, P = s.platform;
            var band = Bands[s.seed % Bands.Length];
            // the frame: origin on the centre line at ground level; r = -across (Frame.Edge handedness), so frame x = -across
            var f = new Frame(new Vector3(s.centre.x, 0f, s.centre.z), -x, Vector3.up, a);
            for (int side = -1; side <= 1; side += 2)
            {
                // frame x runs along -across: this side's platform from x0 to x1
                float x0 = -side * s.inner, x1 = -side * s.Outer;
                float lo = Mathf.Min(x0, x1), hi = Mathf.Max(x0, x1);
                // slab, its edges, the floor tiles and the yellow line
                m.Box(f, lo, hi, P - Slab, P, -L, L, Concrete, Faces.All & ~Faces.Top);
                m.Box(f, lo, hi, P - Slab, P, -L, L, Tile, ArchTex.Floor, Faces.Top, detail ? 1.2f : 0f);
                float yl0 = side > 0 ? x0 - 0.5f : x0, yl1 = side > 0 ? x0 : x0 + 0.5f;
                if (detail) m.Box(f, Mathf.Min(yl0, yl1), Mathf.Max(yl0, yl1), P, P + 0.015f, -L, L, Yellow, Faces.Top);
                // the outer wall: a solid upstand with railings over it
                float wx0 = side > 0 ? x1 : x1 - 0.2f, wx1 = side > 0 ? x1 + 0.2f : x1;
                foreach (var (z0, z1) in s.WallRuns(side))
                {
                    m.Box(f, Mathf.Min(wx0, wx1), Mathf.Max(wx0, wx1), P, P + 0.6f, z0, z1, Concrete, Faces.All & ~Faces.Bottom);
                    if (detail)
                        m.Box(f, Mathf.Min(wx0, wx1) + 0.05f, Mathf.Max(wx0, wx1) - 0.05f, P + 0.6f, P + 1.15f, z0, z1, Steel, ArchTex.Rail, Faces.Left | Faces.Right, 1.0f);
                }
                // the columns under it, and the pier cross-beams
                int cols = Mathf.Max(2, Mathf.RoundToInt(s.length / 11f));
                for (int c = 0; c < cols; c++)
                {
                    float z = Mathf.Lerp(-L + 2f, L - 2f, c / (float)(cols - 1));
                    float cx = (lo + hi) * 0.5f;
                    m.Box(f, cx - 0.4f, cx + 0.4f, 0f, P - Slab, z - 0.4f, z + 0.4f, Concrete, detail ? ArchTex.Concrete : ArchTex.White,
                          Faces.Front | Faces.Back | Faces.Left | Faces.Right, detail ? 2f : 0f);
                    // the roof posts above the outer wall
                    float px = side > 0 ? x1 + 0.1f : x1 - 0.1f;
                    if (detail) m.Box(f, px - 0.12f, px + 0.12f, P + 0.6f, s.Eaves, z - 0.12f, z + 0.12f, Steel);
                }
                // the fascia: a band of advert colour under the eaves, posters along it (the name is lettered in the game)
                float fx = side > 0 ? x1 + 0.35f : x1 - 0.35f;
                var want = x * side;          // world outward on this side
                var fa = new Vector3(s.centre.x, 0f, s.centre.z) + x * side * (s.Outer + 0.35f);
                m.QuadFacing(fa - a * (L + 1.5f) + Vector3.up * (s.Eaves - 1.2f), fa + a * (L + 1.5f) + Vector3.up * (s.Eaves - 1.2f),
                             fa + a * (L + 1.5f) + Vector3.up * (s.Eaves + 0.3f), fa - a * (L + 1.5f) + Vector3.up * (s.Eaves + 0.3f), want, band, ArchTex.White);
                m.QuadFacing(fa - a * (L + 1.5f) + Vector3.up * (s.Eaves - 1.2f), fa + a * (L + 1.5f) + Vector3.up * (s.Eaves - 1.2f),
                             fa + a * (L + 1.5f) + Vector3.up * (s.Eaves + 0.3f), fa - a * (L + 1.5f) + Vector3.up * (s.Eaves + 0.3f), -want, ArchMesh.Shade(band, 0.8f), ArchTex.White);
                m.QuadFacing(fa - a * (L + 1.5f) + Vector3.up * (s.Eaves - 1.2f), fa + a * (L + 1.5f) + Vector3.up * (s.Eaves - 1.2f),
                             fa + a * (L + 1.5f) - want * 0.4f + Vector3.up * (s.Eaves - 1.2f), fa - a * (L + 1.5f) - want * 0.4f + Vector3.up * (s.Eaves - 1.2f), Vector3.down, ArchMesh.Shade(band, 0.7f), ArchTex.White);
                if (detail)
                    foreach (float pz in new[] { -L * 0.62f, L * 0.62f })
                    {
                        var right = Vector3.Cross(want, Vector3.up).normalized * 3.4f;
                        var b0 = fa + want * 0.02f + a * pz + Vector3.up * (s.Eaves - 1.1f);
                        m.QuadFacing(b0 - right, b0 + right, b0 + right + Vector3.up * 1.3f, b0 - right + Vector3.up * 1.3f, want, ArchMesh.Col(1f, 1f, 1f),
                                     (s.seed + side) % 2 == 0 ? ArchTex.Billboard0 : ArchTex.Billboard1);
                    }
            }
            // the roof: a shallow barrel vault from fascia to fascia, zinc above and painted soffit below
            float half = s.Outer + 0.6f, rise = s.monorail ? 2.2f : 1.6f;
            int segs = detail ? 10 : 4;
            for (int i = 0; i < segs; i++)
            {
                float t0 = i / (float)segs, t1 = (i + 1) / (float)segs;
                float u0 = Mathf.Lerp(-half, half, t0), u1 = Mathf.Lerp(-half, half, t1);
                float h0 = s.Eaves + 0.3f + rise * (1f - (2f * t0 - 1f) * (2f * t0 - 1f)), h1 = s.Eaves + 0.3f + rise * (1f - (2f * t1 - 1f) * (2f * t1 - 1f));
                var c = new Vector3(s.centre.x, 0f, s.centre.z);
                Vector3 A = c + x * u0 + Vector3.up * h0 - a * (L + 2f), B = c + x * u1 + Vector3.up * h1 - a * (L + 2f);
                Vector3 C = c + x * u1 + Vector3.up * h1 + a * (L + 2f), D = c + x * u0 + Vector3.up * h0 + a * (L + 2f);
                m.QuadFacing(A, B, C, D, Vector3.up, Zinc, detail ? ArchTex.Zinc : ArchTex.White, 0f, 0f, 1f, (2f * L + 4f) / 1.5f);
                m.QuadFacing(A, B, C, D, Vector3.down, ZincUnder, ArchTex.White);
                // the gable ends of the vault, closed with a painted panel down to the fascia line
                foreach (int e in new[] { -1, 1 })
                {
                    var o = a * e * (L + 2f);
                    m.QuadFacing(c + x * u0 + Vector3.up * (s.Eaves + 0.3f) + o, c + x * u1 + Vector3.up * (s.Eaves + 0.3f) + o,
                                 c + x * u1 + Vector3.up * h1 + o, c + x * u0 + Vector3.up * h0 + o, a * e, band, ArchTex.White);
                }
            }
        }

        public static void Link(StationLinkSpec k, ArchMesh m, bool detail)
        {
            // the stair: a sloped flight with painted treads, stringers, a canopy on posts
            var run = k.top - k.bottom;
            var dir = new Vector3(run.x, 0f, run.z).normalized;
            var side = Vector3.Cross(Vector3.up, dir) * (StairWidth * 0.5f);
            float rise = run.y, len = new Vector3(run.x, 0f, run.z).magnitude;
            m.QuadFacing(k.bottom - side, k.bottom + side, k.top + side, k.top - side, Vector3.up, Concrete, ArchTex.Steps, 0f, 0f, 1f, Mathf.Max(1f, rise / 0.17f / 8f));
            m.QuadFacing(k.bottom - side, k.bottom + side, k.top + side, k.top - side, Vector3.down, ArchMesh.Shade(Concrete, 0.8f), ArchTex.White);
            foreach (int s in new[] { -1, 1 })
            {
                var o = side * s * 1.04f;
                // stringer: the side of the flight, a solid parapet a metre high
                m.QuadFacing(k.bottom + o - Vector3.up * 0.4f, k.top + o - Vector3.up * 0.4f, k.top + o + Vector3.up * 1.0f, k.bottom + o + Vector3.up * 1.0f, side * s, Concrete, ArchTex.White);
                m.QuadFacing(k.bottom + o - Vector3.up * 0.4f, k.top + o - Vector3.up * 0.4f, k.top + o + Vector3.up * 1.0f, k.bottom + o + Vector3.up * 1.0f, -side * s, ArchMesh.Shade(Concrete, 0.9f), ArchTex.White);
                if (!detail) continue;
                // canopy posts every few metres
                for (float t = 0.15f; t < 1f; t += 0.35f)
                {
                    var p = Vector3.Lerp(k.bottom, k.top, t) + o * 1.02f;
                    Flora.Limb(m, p, p + Vector3.up * 3.0f, 0.07f, 0.07f, 4, Steel, ArchTex.White);
                }
            }
            // the canopy over the flight
            {
                var up = Vector3.up * 3.0f;
                var w = side * 1.25f;
                m.QuadFacing(k.bottom - w + up, k.bottom + w + up, k.top + w + up, k.top - w + up, Vector3.up, Zinc, ArchTex.Zinc, 0f, 0f, 1f, len / 1.5f);
                m.QuadFacing(k.bottom - w + up, k.bottom + w + up, k.top + w + up, k.top - w + up, Vector3.down, ZincUnder, ArchTex.White);
            }
            // the linkway from the stair head to the platform
            var l = k.end - k.top;
            l.y = 0f;
            float ll = l.magnitude;
            if (ll < 0.3f) return;
            var ld = l / ll;
            var ls = Vector3.Cross(Vector3.up, ld) * (LinkWidth * 0.5f);
            var a0 = k.top - ld * 1.2f; var a1 = k.end;
            var lf = new Frame(a0, -Vector3.Cross(Vector3.up, ld), Vector3.up, ld);
            float L = ll + 1.2f;
            m.Box(lf, -LinkWidth * 0.5f, LinkWidth * 0.5f, -Slab, 0f, 0f, L, Concrete, Faces.All & ~Faces.Top);
            m.Box(lf, -LinkWidth * 0.5f, LinkWidth * 0.5f, -Slab, 0f, 0f, L, Tile, ArchTex.Floor, Faces.Top, detail ? 1.2f : 0f);
            foreach (int s in new[] { -1, 1 })
            {
                float xs = s * (LinkWidth * 0.5f);
                m.Box(lf, Mathf.Min(xs, xs - s * 0.15f), Mathf.Max(xs, xs - s * 0.15f), 0f, 1.05f, 0f, L, Steel, ArchTex.Rail, Faces.Left | Faces.Right | Faces.Top, 1.0f);
                if (detail) for (float z = 0.5f; z < L; z += 4f) m.Box(lf, xs - 0.07f, xs + 0.07f, 1.05f, 3.0f, z - 0.07f, z + 0.07f, Steel);
            }
            // its roof: a little vault
            for (int i = 0; i < 4; i++)
            {
                float t0 = i / 4f, t1 = (i + 1) / 4f;
                float u0 = Mathf.Lerp(-1.6f, 1.6f, t0), u1 = Mathf.Lerp(-1.6f, 1.6f, t1);
                float h0 = 3.0f + 0.6f * (1f - (2f * t0 - 1f) * (2f * t0 - 1f)), h1 = 3.0f + 0.6f * (1f - (2f * t1 - 1f) * (2f * t1 - 1f));
                Vector3 A = lf.P(u0, h0, 0f), B = lf.P(u1, h1, 0f), C = lf.P(u1, h1, L), D = lf.P(u0, h0, L);
                m.QuadFacing(A, B, C, D, Vector3.up, Zinc, ArchTex.Zinc, 0f, 0f, 1f, L / 1.5f);
                m.QuadFacing(A, B, C, D, Vector3.down, ZincUnder, ArchTex.White);
            }
            // a column under the stair head
            m.Box(new Frame(new Vector3(k.top.x, 0f, k.top.z), -Vector3.Cross(Vector3.up, ld), Vector3.up, ld), -0.35f, 0.35f, 0f, k.top.y - Slab, -0.35f, 0.35f, Concrete,
                  Faces.Front | Faces.Back | Faces.Left | Faces.Right);
        }
    }
}
