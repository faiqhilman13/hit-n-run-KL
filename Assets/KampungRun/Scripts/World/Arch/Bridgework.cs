using System.Collections.Generic;
using UnityEngine;

namespace KampungRun.Arch
{
    /// <summary>
    /// The dressing of KL's flyovers, modelled into the city cells (their physics stays the deck builder's job):
    /// a concrete parapet on the New Jersey profile with a steel rail on top and its kerb painted in yellow and
    /// black blocks, the deck edge's fascia and drip lip.
    /// </summary>
    public class ParapetRun : IArchItem
    {
        public Vector3[] pts;           // the deck edge at road level, in order
        public bool[] raised;           // per segment: on the viaduct (fascia under it) or on the embankment
        public Vector3 outward;         // horizontal, away from the carriageway
        public float deckDepth = 1.4f;

        public Vector2 Where() { var m = pts[pts.Length / 2]; return new Vector2(m.x, m.z); }
        public void Build(ArchMesh m, bool detail) => Bridgework.Parapet(this, m, detail);
    }

    /// <summary>A hammerhead pier under a flyover deck.</summary>
    public class PierSpec : IArchItem
    {
        public Vector3 foot;            // on the ground under the deck's middle
        public Vector3 along;           // the deck's direction (horizontal)
        public float top;               // the deck's underside (height above the foot)
        public float capWidth = 15f;    // across the deck
        public Vector2 Where() => new Vector2(foot.x, foot.z);
        public void Build(ArchMesh m, bool detail) => Bridgework.Pier(this, m, detail);
    }

    /// <summary>An overhead sign gantry across a carriageway: two posts, a truss, green direction signs.</summary>
    public class GantrySpec : IArchItem
    {
        public Vector3 centre;          // middle of the road, at road level
        public Vector3 across;          // horizontal, across the road
        public float halfWidth = 9f;
        public int signs;               // which signs (0..7)
        public Vector2 Where() => new Vector2(centre.x, centre.z);
        public void Build(ArchMesh m, bool detail) => Bridgework.Gantry(this, m, detail);
    }

    /// <summary>
    /// A jejantas: KL's covered footbridge over a main road. A stair down each side along the pavement, a deck with
    /// railings, a curved zinc roof on posts, an advert along the side the traffic sees.
    /// </summary>
    public class JejantasSpec : IArchItem
    {
        public Vector3 a, b;            // the two ends of the span (pavement points either side of the road, at ground level)
        public float height = 6.2f;     // deck walking surface
        public int seed;
        public Vector2 Where() { var c = (a + b) * 0.5f; return new Vector2(c.x, c.z); }
        public void Build(ArchMesh m, bool detail) => Bridgework.Jejantas(this, m, detail);
    }

    public static class Bridgework
    {
        static readonly Color32 Concrete = ArchMesh.Col(0.84f, 0.83f, 0.80f), ConcreteDark = ArchMesh.Col(0.70f, 0.69f, 0.66f);
        static readonly Color32 Steel = ArchMesh.Col(0.62f, 0.66f, 0.70f), White = ArchMesh.Col(1f, 1f, 1f);
        static readonly Color32 Green = ArchMesh.Col(0.24f, 0.56f, 0.42f), Blue = ArchMesh.Col(0.24f, 0.44f, 0.70f);

        // the New Jersey barrier, across (u, out from the carriageway) and up (v): kerb, steep foot, slope, upright face
        static readonly Vector2[] Profile = { new Vector2(-0.44f, 0f), new Vector2(-0.44f, 0.1f), new Vector2(-0.36f, 0.32f), new Vector2(-0.2f, 0.92f), new Vector2(0.04f, 0.92f), new Vector2(0.04f, 0f) };

        public static void Parapet(ParapetRun p, ArchMesh m, bool detail)
        {
            var o = p.outward.normalized;
            for (int i = 0; i + 1 < p.pts.Length; i++)
            {
                Vector3 a = p.pts[i], b = p.pts[i + 1];
                float len = Vector3.Distance(a, b);
                if (len < 0.05f) continue;
                Vector3 At(Vector3 q, Vector2 uv) => q + o * uv.x + Vector3.up * uv.y;
                int faces = detail ? Profile.Length - 1 : 0;
                if (!detail)
                {
                    // a slab: the face the road sees, the top, the outer face
                    m.QuadFacing(At(a, new Vector2(-0.4f, 0f)), At(b, new Vector2(-0.4f, 0f)), At(b, new Vector2(-0.3f, 0.92f)), At(a, new Vector2(-0.3f, 0.92f)), -o, Concrete, ArchTex.White);
                    m.QuadFacing(At(a, new Vector2(-0.3f, 0.92f)), At(b, new Vector2(-0.3f, 0.92f)), At(b, new Vector2(0.04f, 0.92f)), At(a, new Vector2(0.04f, 0.92f)), Vector3.up, Concrete, ArchTex.White);
                }
                for (int k = 0; k < faces; k++)
                {
                    Vector2 u0 = Profile[k], u1 = Profile[k + 1];
                    // the profile runs clockwise (up the road side, over the top, down the back): its outside is (-dv, du)
                    var n2 = new Vector2(-(u1.y - u0.y), u1.x - u0.x);
                    var col = k == 0 ? White : k >= 3 ? Concrete : ArchMesh.Shade(Concrete, 0.94f);
                    var tex = k == 0 ? ArchTex.Hazard : ArchTex.White;
                    m.QuadFacing(At(a, u0), At(b, u0), At(b, u1), At(a, u1), o * n2.x + Vector3.up * n2.y, col, tex, 0f, 0f, len / 1.2f, 1f);
                }
                if (p.raised != null && i < p.raised.Length && p.raised[i])
                {
                    // the deck's edge: fascia below the parapet's outer face, a drip lip at its foot, the girder underneath
                    float d = p.deckDepth;
                    m.QuadFacing(At(a, new Vector2(0.04f, 0f)), At(b, new Vector2(0.04f, 0f)), At(b, new Vector2(0.04f, -d + 0.2f)), At(a, new Vector2(0.04f, -d + 0.2f)), o, Concrete, ArchTex.White);
                    if (detail)
                    {
                        m.QuadFacing(At(a, new Vector2(0.04f, -d + 0.2f)), At(b, new Vector2(0.04f, -d + 0.2f)), At(b, new Vector2(0.2f, -d)), At(a, new Vector2(0.2f, -d)), o - Vector3.up, ConcreteDark, ArchTex.White);
                        m.QuadFacing(At(a, new Vector2(0.2f, -d)), At(b, new Vector2(0.2f, -d)), At(b, new Vector2(-1.6f, -d)), At(a, new Vector2(-1.6f, -d)), Vector3.down, ConcreteDark, ArchTex.White);
                        // a box girder rib a little in from the edge
                        m.QuadFacing(At(a, new Vector2(-2.6f, -d)), At(b, new Vector2(-2.6f, -d)), At(b, new Vector2(-2.6f, -d - 0.7f)), At(a, new Vector2(-2.6f, -d - 0.7f)), o, ConcreteDark, ArchTex.White);
                        m.QuadFacing(At(a, new Vector2(-2.6f, -d - 0.7f)), At(b, new Vector2(-2.6f, -d - 0.7f)), At(b, new Vector2(-4.4f, -d - 0.7f)), At(a, new Vector2(-4.4f, -d - 0.7f)), Vector3.down, ConcreteDark, ArchTex.White);
                        m.QuadFacing(At(a, new Vector2(-4.4f, -d)), At(b, new Vector2(-4.4f, -d)), At(b, new Vector2(-4.4f, -d - 0.7f)), At(a, new Vector2(-4.4f, -d - 0.7f)), -o, ConcreteDark, ArchTex.White);
                    }
                }
                if (!detail) continue;
                // the steel rail on top, on posts every couple of metres
                var r0 = At(a, new Vector2(-0.08f, 1.22f)); var r1 = At(b, new Vector2(-0.08f, 1.22f));
                Rod(m, r0, r1, 0.06f, Steel);
                int posts = Mathf.Max(1, Mathf.RoundToInt(len / 2.4f));
                for (int q = 0; q < posts; q++)
                {
                    var c = Vector3.Lerp(a, b, q / (float)posts);
                    Rod(m, At(c, new Vector2(-0.08f, 0.92f)), At(c, new Vector2(-0.08f, 1.25f)), 0.05f, Steel);
                }
            }
        }

        /// <summary>A square rod from a to b.</summary>
        static void Rod(ArchMesh m, Vector3 a, Vector3 b, float w, Color32 col)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return;
            d /= len;
            var s1 = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized * (w * 0.5f);
            var s2 = Vector3.Cross(d, s1).normalized * (w * 0.5f);
            m.QuadFacing(a + s1 + s2, b + s1 + s2, b + s1 - s2, a + s1 - s2, s1, col, ArchTex.White);
            m.QuadFacing(a - s1 + s2, b - s1 + s2, b - s1 - s2, a - s1 - s2, -s1, col, ArchTex.White);
            m.QuadFacing(a + s2 + s1, b + s2 + s1, b + s2 - s1, a + s2 - s1, s2, col, ArchTex.White);
            m.QuadFacing(a - s2 + s1, b - s2 + s1, b - s2 - s1, a - s2 - s1, -s2, col, ArchTex.White);
        }

        public static void Pier(PierSpec p, ArchMesh m, bool detail)
        {
            var along = p.along.normalized;
            var across = Vector3.Cross(Vector3.up, along);
            float capH = 1.6f, colTop = p.top - capH;
            if (detail) Flora.Limb(m, p.foot, p.foot + Vector3.up * colTop, 0.95f, 0.85f, 8, Concrete, ArchTex.Concrete, 3f);
            else Flora.Limb(m, p.foot, p.foot + Vector3.up * colTop, 0.95f, 0.85f, 4, Concrete, ArchTex.White);
            // the hammerhead: narrow where it sits on the column, the deck's width at the top
            var f = new Frame(p.foot + Vector3.up * colTop, across, Vector3.up, along);
            float w0 = 1.3f, w1 = p.capWidth * 0.5f, dz = 1.1f;
            var outline = new[] { new Vector2(-w0, 0f), new Vector2(w0, 0f), new Vector2(w1, capH * 0.55f), new Vector2(w1, capH), new Vector2(-w1, capH), new Vector2(-w1, capH * 0.55f) };
            m.Extrude(f.Shift(0f, 0f, -dz), outline, 0f, 2f * dz, Concrete, ArchTex.White, 0f, ArchMesh.Shade(Concrete, 0.9f));
            if (!detail) return;
            // bearings under the girders
            foreach (float x in new[] { -w1 + 1.6f, -1.2f, 1.2f, w1 - 1.6f })
                m.Box(f, x - 0.35f, x + 0.35f, capH, capH + 0.18f, -0.5f, 0.5f, ArchMesh.Col(0.3f, 0.3f, 0.32f));
        }

        public static void Gantry(GantrySpec g, ArchMesh m, bool detail)
        {
            var x = g.across.normalized;
            var z = Vector3.Cross(x, Vector3.up);
            float hw = g.halfWidth, h = 6.4f;
            foreach (float s in new[] { -1f, 1f })
            {
                var foot = g.centre + x * (s * (hw + 0.6f));
                Flora.Limb(m, foot, foot + Vector3.up * (h + 1.4f), 0.22f, 0.2f, detail ? 6 : 4, Steel, ArchTex.White);
            }
            var f = new Frame(g.centre - x * (hw + 0.8f) + Vector3.up * h, x, Vector3.up, z);
            m.Box(f, 0f, 2f * hw + 1.6f, 1.0f, 1.25f, -0.25f, 0.25f, Steel, detail ? Faces.All : Faces.Front | Faces.Top | Faces.Bottom);
            m.Box(f, 0f, 2f * hw + 1.6f, -0.05f, 0.2f, -0.25f, 0.25f, Steel, detail ? Faces.All : Faces.Front | Faces.Bottom);
            // two signs facing the traffic coming (+z of the frame faces the approaching cars)
            for (int k = 0; k < 2; k++)
            {
                int sgn = (g.signs + k) % 8, cell = sgn / 4, row = sgn % 4;
                float x0 = 1.2f + k * (hw + 0.2f), x1 = x0 + hw - 1.2f;
                var tex = cell == 0 ? ArchTex.RoadSigns0 : ArchTex.RoadSigns1;
                // a board each way, so both carriageways read their sign
                m.Box(f, x0, x1, -1.9f, 1.1f, -0.4f, 0.4f, Green, Faces.Left | Faces.Right | Faces.Top | Faces.Bottom);
                float v0 = 1f - (row + 1) / 4f, v1 = 1f - row / 4f;
                m.Quad(f.P(x0, -1.9f, 0.4f), f.P(x1, -1.9f, 0.4f), f.P(x1, 1.1f, 0.4f), f.P(x0, 1.1f, 0.4f), White, tex, 0f, v0, 1f, v1);
                m.Quad(f.P(x1, -1.9f, -0.4f), f.P(x0, -1.9f, -0.4f), f.P(x0, 1.1f, -0.4f), f.P(x1, 1.1f, -0.4f), White, tex, 0f, v0, 1f, v1);
            }
        }

        public static void Jejantas(JejantasSpec j, ArchMesh m, bool detail)
        {
            var rng = new System.Random(j.seed);
            var dir = j.b - j.a; dir.y = 0f;
            float span = dir.magnitude;
            dir /= span;
            var side = Vector3.Cross(Vector3.up, dir);
            float H = j.height, half = 1.3f;
            var roof = rng.NextDouble() < 0.5 ? Blue : Green;
            var frame = new Frame(j.a, dir, Vector3.up, -side);   // x along the span, z sideways
            // the deck and its sides
            m.Box(frame, 0f, span, H - 0.45f, H, -half, half, ConcreteDark, detail ? Faces.All : Faces.Front | Faces.Back | Faces.Bottom | Faces.Top);
            if (detail)
            {
                foreach (float s in new[] { -1f, 1f })
                {
                    m.Box(frame, 0f, span, H, H + 1.1f, s * half - 0.03f, s * half + 0.03f, Steel, ArchTex.Rail, Faces.Front | Faces.Back, 1.1f);
                    // the advert board along the side the cars see
                    var ad = rng.NextDouble() < 0.5 ? ArchTex.Billboard0 : ArchTex.Billboard1;
                    float a0 = span * 0.25f, a1 = span * 0.75f;
                    // (read left to right from the road below: from that side, left is the far end)
                    m.Rect(new Frame(frame.P(s > 0 ? a1 : a0, 0f, s * (half + 0.05f)), s > 0 ? -dir : dir, Vector3.up, s > 0 ? -side : side), 0f, a1 - a0, H - 1.6f, H - 0.45f, 0f, White, ad);
                }
            }
            // posts and a curved roof
            int bays = Mathf.Max(2, Mathf.RoundToInt(span / 3.2f));
            for (int k = 0; k <= bays && detail; k++)
                foreach (float s in new[] { -1f, 1f })
                {
                    var p = frame.P(span * k / bays, H, s * (half - 0.05f));
                    Rod(m, p, p + Vector3.up * 2.5f, 0.1f, Steel);
                }
            int arc = detail ? 6 : 3;
            for (int k = 0; k < arc; k++)
            {
                float a0 = Mathf.PI * k / arc, a1 = Mathf.PI * (k + 1) / arc;
                float z0 = Mathf.Cos(a0) * (half + 0.25f), z1 = Mathf.Cos(a1) * (half + 0.25f);
                float y0 = H + 2.5f + Mathf.Sin(a0) * 0.7f, y1 = H + 2.5f + Mathf.Sin(a1) * 0.7f;
                var n = (Vector3.up * Mathf.Sin((a0 + a1) * 0.5f) + (-side) * Mathf.Cos((a0 + a1) * 0.5f)).normalized;
                m.QuadFacing(frame.P(-0.3f, y0, z0), frame.P(span + 0.3f, y0, z0), frame.P(span + 0.3f, y1, z1), frame.P(-0.3f, y1, z1), n, roof, ArchTex.Zinc, 0f, 0f, span / 1.2f, 0.6f);
                m.QuadFacing(frame.P(-0.3f, y0, z0), frame.P(span + 0.3f, y0, z0), frame.P(span + 0.3f, y1, z1), frame.P(-0.3f, y1, z1), -n, ArchMesh.Shade(roof, 0.7f), ArchTex.White);
            }
            // the stairs: down along the pavement from each end, a landing at the top
            foreach (var (end, back) in new[] { (j.a, -dir), (j.b, dir) })
            {
                var stairDir = side;                                   // along the pavement
                Stair(m, end, stairDir, H, detail);
            }
        }

        /// <summary>A straight stair climbing `height` along `dir` to end at `top` (its landing), with cheeks and a handrail.</summary>
        static void Stair(ArchMesh m, Vector3 top, Vector3 dir, float height, bool detail)
        {
            const float w = 1.8f;
            int steps = Mathf.RoundToInt(height / 0.18f);
            float run = 0.27f, len = steps * run;
            var foot = top - dir * len;                                // where the stair starts on the pavement
            var across = Vector3.Cross(Vector3.up, dir);
            var f = new Frame(foot, dir, Vector3.up, -across);
            if (detail)
                for (int k = 0; k < steps; k++)
                {
                    float y = (k + 1) * height / steps, x0 = k * run;
                    m.Box(f, x0, x0 + run, y - height / steps, y, -w / 2, w / 2, k % 2 == 0 ? Concrete : ArchMesh.Shade(Concrete, 0.95f), Faces.Top | Faces.Left);
                }
            else m.QuadFacing(f.P(0f, 0f, -w / 2), f.P(len, height, -w / 2), f.P(len, height, w / 2), f.P(0f, 0f, w / 2), Vector3.up - dir, Concrete, ArchTex.White);
            // the stringers either side, and the underside
            foreach (float s in new[] { -1f, 1f })
                m.QuadFacing(f.P(0f, -0.3f, s * w / 2), f.P(len, height - 0.3f, s * w / 2), f.P(len, height + (detail ? 0.15f : 0.05f), s * w / 2), f.P(0f, 0.15f, s * w / 2),
                             s > 0 ? -across : across, ConcreteDark, ArchTex.White);
            m.QuadFacing(f.P(0f, -0.3f, -w / 2), f.P(len, height - 0.3f, -w / 2), f.P(len, height - 0.3f, w / 2), f.P(0f, -0.3f, w / 2), Vector3.down, ConcreteDark, ArchTex.White);
            if (!detail) return;
            foreach (float s in new[] { -1f, 1f })
                Rod(m, f.P(0f, 1.0f, s * (w / 2 - 0.05f)), f.P(len, height + 1.0f, s * (w / 2 - 0.05f)), 0.06f, Steel);
            // legs under the stair
            for (float x = len * 0.5f; x < len; x += len * 0.5f)
                foreach (float s in new[] { -1f, 1f })
                    Rod(m, f.P(x, 0f, s * (w / 2 - 0.2f)), f.P(x, x / len * height - 0.3f, s * (w / 2 - 0.2f)), 0.16f, Steel);
        }

        /// <summary>A jejantas' solid: the deck, the stairs as ramps, so you can walk over the road.</summary>
        public static void Collider(JejantasSpec j, List<Vector3> v, List<int> t)
        {
            var dir = j.b - j.a; dir.y = 0f;
            float span = dir.magnitude;
            dir /= span;
            var side = Vector3.Cross(Vector3.up, dir);
            float H = j.height, half = 1.3f;
            void Slab(Vector3 p0, Vector3 p1, Vector3 across, float w)
            {
                int b = v.Count;
                v.Add(p0 - across * w); v.Add(p1 - across * w); v.Add(p1 + across * w); v.Add(p0 + across * w);
                t.Add(b); t.Add(b + 3); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 1);         // both faces: walkable from above
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
            Slab(j.a + Vector3.up * H, j.b + Vector3.up * H, side, half);
            int steps = Mathf.RoundToInt(H / 0.18f);
            float len = steps * 0.27f;
            foreach (var end in new[] { j.a, j.b })
                Slab(end - side * len, end + Vector3.up * H, dir, 0.9f);
        }
    }
}
