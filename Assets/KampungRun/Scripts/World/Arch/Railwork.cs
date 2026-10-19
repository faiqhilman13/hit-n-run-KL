using UnityEngine;

namespace KampungRun.Arch
{
    /// <summary>
    /// A pier under KL's elevated rail. The monorail's is a squat rectangular column (often wrapped in an
    /// advert, a red one with a billboard more often than not) under a crosshead that carries both beams; the
    /// LRT's is a round column under a hammerhead as wide as the box girder.
    /// </summary>
    public class RailPierSpec : IArchItem
    {
        public Vector3 foot;            // on the ground under the guideway's middle
        public Vector3 along;           // the line's direction (horizontal)
        public float top;               // the guideway's underside (height above the foot)
        public float span;              // monorail: distance between the two beams' centres (0 = one beam)
        public bool monorail;
        public int seed;
        public Vector2 Where() => new Vector2(foot.x, foot.z);
        public void Build(ArchMesh m, bool detail) => Railwork.Pier(this, m, detail);
    }

    /// <summary>The running surface along one stretch of guideway: the LRT's two rails on a plinth with the
    /// reaction rail between them, or the rubber-tyre tracks along the top of a monorail beam.</summary>
    public class RailTrackSpec : IArchItem
    {
        public Vector3 a, b;            // on the guideway's top surface, along its centre line
        public bool monorail;
        public Vector2 Where() { var c = (a + b) * 0.5f; return new Vector2(c.x, c.z); }
        public void Build(ArchMesh m, bool detail) => Railwork.Track(this, m, detail);
    }

    public static class Railwork
    {
        public static readonly Color32 Concrete = ArchMesh.Col(0.80f, 0.77f, 0.72f), ConcreteDark = ArchMesh.Col(0.62f, 0.60f, 0.57f);
        static readonly Color32 Steel = ArchMesh.Col(0.55f, 0.57f, 0.60f), Rubber = ArchMesh.Col(0.36f, 0.35f, 0.34f);
        static readonly Color32[] Wraps =
        {
            ArchMesh.Col(0.86f, 0.12f, 0.12f), ArchMesh.Col(0.98f, 0.76f, 0.10f), ArchMesh.Col(0.12f, 0.46f, 0.80f),
            ArchMesh.Col(0.10f, 0.62f, 0.40f), ArchMesh.Col(0.94f, 0.42f, 0.10f),
        };

        /// <summary>A picture facing `want`, its bottom edge centred on `bottom`, never mirrored.</summary>
        static void Poster(ArchMesh m, Vector3 bottom, Vector3 want, float w, float h, ArchTex tex, float u0 = 0f, float u1 = 1f)
        {
            var right = Vector3.Cross(want, Vector3.up).normalized * (w * 0.5f);     // the viewer's right
            var up = Vector3.up * h;
            m.QuadFacing(bottom - right, bottom + right, bottom + right + up, bottom - right + up, want, ArchMesh.Col(1f, 1f, 1f), tex, u0, 0f, u1, 1f);
        }

        public static void Pier(RailPierSpec p, ArchMesh m, bool detail)
        {
            var along = p.along.normalized;
            var across = Vector3.Cross(Vector3.up, along);
            if (p.monorail) MonorailPier(p, m, detail, along, across);
            else LrtPier(p, m, detail, along, across);
        }

        static void MonorailPier(RailPierSpec p, ArchMesh m, bool detail, Vector3 along, Vector3 across)
        {
            float capH = 1.15f, colTop = p.top - capH;
            var f = new Frame(p.foot, -across, Vector3.up, along);          // (n = cross(up, r), as Frame.Edge)
            float hx = 0.78f, hz = 0.58f;
            var rng = new System.Random(p.seed);
            bool wrapped = rng.NextDouble() < 0.55;
            var wrap = Wraps[rng.Next(Wraps.Length)];
            // the column: a plinth, the shaft, and the advert wrapped round its upper half
            m.Box(f, -hx - 0.12f, hx + 0.12f, 0f, 0.5f, -hz - 0.12f, hz + 0.12f, ConcreteDark, Faces.All & ~Faces.Bottom);
            float w0 = wrapped ? colTop * 0.38f : colTop;
            m.Box(f, -hx, hx, 0.5f, w0, -hz, hz, Concrete, ArchTex.Concrete, Faces.Front | Faces.Back | Faces.Left | Faces.Right, detail ? 2f : 0f);
            if (wrapped)
            {
                m.Box(f, -hx - 0.03f, hx + 0.03f, w0, colTop, -hz - 0.03f, hz + 0.03f, wrap, Faces.Left | Faces.Right);
                var bill = rng.NextDouble() < 0.5 ? ArchTex.Billboard0 : ArchTex.Billboard1;
                // the poster on the faces the traffic sees (front and back), a band of colour above and below it
                foreach (float s in new[] { -1f, 1f })
                {
                    float z = s * (hz + 0.03f);
                    float y0 = w0 + 0.35f, y1 = colTop - 0.35f;
                    var a = f.P(-hx - 0.03f, w0, z); var b = f.P(hx + 0.03f, w0, z);
                    m.QuadFacing(a, b, f.P(hx + 0.03f, y0, z), f.P(-hx - 0.03f, y0, z), along * s, wrap, ArchTex.White);
                    m.QuadFacing(f.P(-hx - 0.03f, y1, z), f.P(hx + 0.03f, y1, z), f.P(hx + 0.03f, colTop, z), f.P(-hx - 0.03f, colTop, z), along * s, wrap, ArchTex.White);
                    Poster(m, p.foot + along * z + Vector3.up * y0, along * s, 2f * hx + 0.06f, y1 - y0, bill, 0.25f, 0.75f);
                }
            }
            // the crosshead: out to under both beams, chamfered underneath
            float w1 = Mathf.Max(1.6f, p.span * 0.5f + 1.05f);
            var cf = new Frame(p.foot + Vector3.up * colTop, -across, Vector3.up, along);
            var outline = new[] { new Vector2(-hx, 0f), new Vector2(hx, 0f), new Vector2(w1, capH * 0.55f), new Vector2(w1, capH), new Vector2(-w1, capH), new Vector2(-w1, capH * 0.55f) };
            m.Extrude(cf.Shift(0f, 0f, -0.75f), outline, 0f, 1.5f, Concrete, ArchTex.White, 0f, ArchMesh.Shade(Concrete, 0.9f));
            if (!detail) return;
            // the steel finger joints where the beams meet over the pier
            foreach (float s in p.span > 0.1f ? new[] { -p.span * 0.5f, p.span * 0.5f } : new[] { 0f })
                m.Box(cf, s - 0.62f, s + 0.62f, capH, capH + 0.12f, -0.6f, 0.6f, Steel, Faces.All & ~Faces.Bottom);
        }

        static void LrtPier(RailPierSpec p, ArchMesh m, bool detail, Vector3 along, Vector3 across)
        {
            float capH = 1.5f, colTop = p.top - capH;
            Flora.Limb(m, p.foot, p.foot + Vector3.up * colTop, 0.92f, 0.86f, detail ? 10 : 5, Concrete, detail ? ArchTex.Concrete : ArchTex.White, 3f);
            var f = new Frame(p.foot + Vector3.up * colTop, -across, Vector3.up, along);
            float w0 = 1.1f, w1 = 3.7f, dz = 0.95f;
            var outline = new[] { new Vector2(-w0, 0f), new Vector2(w0, 0f), new Vector2(w1, capH * 0.6f), new Vector2(w1, capH), new Vector2(-w1, capH), new Vector2(-w1, capH * 0.6f) };
            m.Extrude(f.Shift(0f, 0f, -dz), outline, 0f, 2f * dz, Concrete, ArchTex.White, 0f, ArchMesh.Shade(Concrete, 0.9f));
            if (!detail) return;
            m.Box(new Frame(p.foot, -across, Vector3.up, along), -1.15f, 1.15f, 0f, 0.4f, -1.15f, 1.15f, ConcreteDark, Faces.All & ~Faces.Bottom);
            foreach (float x in new[] { -2.4f, 2.4f })
                m.Box(f, x - 0.4f, x + 0.4f, capH, capH + 0.14f, -0.45f, 0.45f, ArchMesh.Col(0.3f, 0.3f, 0.32f));
        }

        public static void Track(RailTrackSpec t, ArchMesh m, bool detail)
        {
            var d = t.b - t.a;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 0.2f || !detail) return;
            var f = new Frame(t.a, Vector3.Cross(Vector3.up, d / len), Vector3.up, d / len);
            // x across, z along: f.P(x, y, z); heights follow a straight line from a to b
            void Strip(float x0, float x1, float y, Color32 c, float h = 0f)
            {
                Vector3 P(float x, float z, float yy) => f.P(x, yy + (t.b.y - t.a.y) * (z / len), z);
                m.QuadFacing(P(x0, 0f, y + h), P(x1, 0f, y + h), P(x1, len, y + h), P(x0, len, y + h), Vector3.up, c, ArchTex.White);
                if (h > 0f)
                {
                    m.QuadFacing(P(x0, 0f, y), P(x0, len, y), P(x0, len, y + h), P(x0, 0f, y + h), -f.r, ArchMesh.Shade(c, 0.8f), ArchTex.White);
                    m.QuadFacing(P(x1, 0f, y), P(x1, len, y), P(x1, len, y + h), P(x1, 0f, y + h), f.r, ArchMesh.Shade(c, 0.8f), ArchTex.White);
                }
            }
            if (t.monorail)
            {
                // the tyres' dark track along the top, a lighter guide face either side
                Strip(-0.42f, 0.42f, 0.012f, Rubber);
                return;
            }
            // the LRT: a plinth, the two rails, the aluminium reaction rail between them
            Strip(-1.25f, 1.25f, 0.0f, ConcreteDark, 0.08f);
            foreach (float x in new[] { -0.72f, 0.72f }) Strip(x - 0.04f, x + 0.04f, 0.08f, Steel, 0.14f);
            Strip(-0.22f, 0.22f, 0.08f, ArchMesh.Col(0.75f, 0.76f, 0.78f), 0.05f);
        }

        public const float CarLength = 12.4f;

        /// <summary>
        /// One car of a two-car train (car 0 has its nose at the back, -z; car 1 at the front, +z), local space with
        /// +z forward and the origin on the running surface at the car's middle. The monorail straddles its beam
        /// (skirts down over its sides) in a full advert wrap; the LRT is white with Kelana Jaya blue and a red
        /// pinstripe.
        /// </summary>
        public static Mesh TrainCar(bool monorail, int seed, int c)
        {
            var m = new ArchMesh();
            var rng = new System.Random(seed);
            const float car = CarLength;
            var wrapA = Wraps[rng.Next(Wraps.Length)];
            var wrapB = Wraps[rng.Next(Wraps.Length)];
            var white = ArchMesh.Col(0.96f, 0.96f, 0.95f);
            var blue = ArchMesh.Col(0.13f, 0.33f, 0.68f);
            var red = ArchMesh.Col(0.82f, 0.14f, 0.16f);
            {
                float z0 = -car * 0.5f, z1 = car * 0.5f;
                var f = new Frame(Vector3.zero, Vector3.left, Vector3.up, Vector3.forward);     // (Frame.Edge handedness)
                float hw = monorail ? 1.35f : 1.33f;
                float y0 = monorail ? -1.05f : 0.95f, y1 = monorail ? 2.75f : 3.75f;
                var body = monorail ? (c == 0 ? wrapA : wrapB) : white;
                bool noseF = c == 1, noseB = c == 0;
                float nz1 = noseF ? z1 - 1.4f : z1, nz0 = noseB ? z0 + 1.4f : z0;
                // the body: sides, roof, the floor/skirt
                m.Box(f, -hw, hw, y0, y1, nz0, nz1, body, Faces.Left | Faces.Right | Faces.Top | Faces.Bottom);
                if (monorail)
                {
                    // the beam runs through the skirt: an inner slot so the train sits over it
                    m.Box(f, -0.48f, 0.48f, y0, 0.02f, nz0, nz1, ArchMesh.Col(0.2f, 0.2f, 0.22f), Faces.Left | Faces.Right | Faces.Top);
                }
                else
                {
                    m.Box(f, -hw - 0.01f, hw + 0.01f, y0 + 0.35f, y0 + 1.0f, nz0, nz1, blue, Faces.Left | Faces.Right);
                    m.Box(f, -hw - 0.02f, hw + 0.02f, y0 + 1.04f, y0 + 1.14f, nz0, nz1, red, Faces.Left | Faces.Right);
                    foreach (float bz in new[] { nz0 + 2.2f, nz1 - 2.2f })
                        m.Box(f, -1.1f, 1.1f, 0.0f, y0, bz - 1.1f, bz + 1.1f, ArchMesh.Col(0.18f, 0.18f, 0.2f));
                }
                // windows: a band on each side, broken by the doors
                float wy0 = y1 - 1.75f, wy1 = y1 - 0.55f;
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = s * (hw + 0.02f);
                    var side = new Frame(new Vector3(x, 0f, 0f), Vector3.forward * -s, Vector3.up, Vector3.right * s);
                    for (float z = nz0 + 0.6f; z + 1.6f < nz1 - 0.4f; z += 2.3f)
                    {
                        bool door = Mathf.Abs(z + 0.8f - (z0 + z1) * 0.5f) < 1.2f || Mathf.Abs(z + 0.8f - (z0 + car * 0.2f)) < 1.2f;
                        float a = z, b = z + 1.6f;
                        if (door)
                            m.QuadFacing(new Vector3(x, (monorail ? 0.05f : y0 + 0.1f), a), new Vector3(x, (monorail ? 0.05f : y0 + 0.1f), b), new Vector3(x, wy1, b), new Vector3(x, wy1, a),
                                         Vector3.right * s, ArchMesh.Col(0.78f, 0.8f, 0.84f), ArchTex.GlassDark);
                        else
                            m.QuadFacing(new Vector3(x, wy0, a), new Vector3(x, wy0, b), new Vector3(x, wy1, b), new Vector3(x, wy1, a), Vector3.right * s, ArchMesh.Col(1f, 1f, 1f), ArchTex.GlassDark);
                    }
                    if (monorail)
                    {
                        // the advert along the lower side
                        var bill = (c + s) % 2 == 0 ? ArchTex.Billboard0 : ArchTex.Billboard1;
                        float ya = y0 + 0.35f, yb = wy0 - 0.12f, za = nz0 + 1.0f, zb = nz1 - 1.0f;
                        Poster(m, new Vector3(x + s * 0.01f, ya, (za + zb) * 0.5f), Vector3.right * s, zb - za, yb - ya, bill);
                    }
                }
                // the noses: a raked windscreen over a rounded front; flat ends where the cars couple
                for (int e = 0; e < 2; e++)
                {
                    bool nose = e == 0 ? noseB : noseF;
                    float zf = e == 0 ? nz0 : nz1, sgn = e == 0 ? -1f : 1f;
                    if (!nose)
                    {
                        m.QuadFacing(new Vector3(-hw, y0, zf), new Vector3(hw, y0, zf), new Vector3(hw, y1, zf), new Vector3(-hw, y1, zf), Vector3.forward * sgn, ArchMesh.Shade(body, 0.85f), ArchTex.White);
                        continue;
                    }
                    float zt = zf + sgn * 1.4f, ym = y0 + (y1 - y0) * 0.45f;
                    // lower front (body colour), windscreen (dark glass) leaning back to the roof
                    m.QuadFacing(new Vector3(-hw, y0, zf), new Vector3(hw, y0, zf), new Vector3(hw * 0.85f, ym, zt), new Vector3(-hw * 0.85f, ym, zt), Vector3.forward * sgn, body, ArchTex.White);
                    m.QuadFacing(new Vector3(-hw * 0.85f, ym, zt), new Vector3(hw * 0.85f, ym, zt), new Vector3(hw, y1, zf), new Vector3(-hw, y1, zf), Vector3.forward * sgn + Vector3.up, ArchMesh.Col(1f, 1f, 1f), ArchTex.GlassDark);
                    foreach (int s in new[] { -1, 1 })
                    {
                        m.TriFacing(new Vector3(s * hw, y0, zf), new Vector3(s * hw * 0.85f, ym, zt), new Vector3(s * hw, y1, zf), Vector3.right * s, body, ArchTex.White, Vector2.zero, Vector2.zero, Vector2.zero);
                        // headlamps
                        m.QuadFacing(new Vector3(s * hw * 0.62f, y0 + 0.35f, zf + sgn * 0.62f), new Vector3(s * hw * 0.32f, y0 + 0.35f, zf + sgn * 0.62f),
                                     new Vector3(s * hw * 0.32f, y0 + 0.62f, zf + sgn * 0.8f), new Vector3(s * hw * 0.62f, y0 + 0.62f, zf + sgn * 0.8f),
                                     Vector3.forward * sgn, ArchMesh.Col(1f, 0.97f, 0.82f), ArchTex.White);
                    }
                    if (!monorail) m.QuadFacing(new Vector3(-hw, y0 + 0.1f, zf - sgn * 0.01f), new Vector3(hw, y0 + 0.1f, zf - sgn * 0.01f), new Vector3(hw * 0.95f, y0 + 0.55f, zf + sgn * 0.2f),
                                               new Vector3(-hw * 0.95f, y0 + 0.55f, zf + sgn * 0.2f), Vector3.forward * sgn, blue, ArchTex.White);
                }
            }
            var mesh = m.ToMesh((monorail ? "MonorailCar" : "LrtCar") + c);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
