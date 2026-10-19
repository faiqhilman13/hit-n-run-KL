using UnityEngine;

namespace KampungRun.Arch
{
    /// <summary>
    /// The lived-in layer of the streets (GTA-style density at street level): what spills out of a shop onto its five-foot
    /// way (crates of produce, a rack of snacks, plastic stools and a kopitiam table, a barber's pole, buckets and brooms,
    /// an A-frame menu, potted plants, a kapcai parked by the columns), the clutter of the back lanes (bins, gas tanks,
    /// laundry poles, crates, a water heater), lamp-lit windows upstairs, and a kampung house's yard (a clothesline of
    /// sarongs, a kapcai under the eaves, the water jar, a porch lamp). Modelled with the near-detail version only (ArchCity
    /// streams it), seeded from the building's own seed, no physics (it's dressing: the five-foot way stays walkable).
    /// </summary>
    public static class StreetDress
    {
        /// <summary>Off: the streets as they were (captures compare the two).</summary>
        public static bool Enabled = true;

        static Color32 C(float r, float g, float b, float gloss = 0f) => ArchMesh.Col(r, g, b, gloss);
        static readonly Color32 White = C(1f, 1f, 1f), Steel = C(0.72f, 0.74f, 0.78f, 0.5f), Black = C(0.14f, 0.14f, 0.16f), Tyre = C(0.12f, 0.12f, 0.13f);
        static readonly Color32 Crate = C(0.86f, 0.30f, 0.22f), CrateB = C(0.22f, 0.50f, 0.80f), CrateY = C(0.96f, 0.76f, 0.20f), Wood = C(0.62f, 0.44f, 0.28f);
        static readonly Color32 Pot = C(0.76f, 0.40f, 0.26f), Leaf = C(0.20f, 0.46f, 0.20f), LeafLight = C(0.42f, 0.70f, 0.30f);
        static readonly Color32[] Brights = { C(0.86f, 0.22f, 0.20f), C(0.20f, 0.46f, 0.82f), C(0.98f, 0.78f, 0.20f), C(0.24f, 0.66f, 0.38f), C(0.94f, 0.50f, 0.18f), C(0.62f, 0.34f, 0.74f), C(0.96f, 0.96f, 0.94f) };
        static readonly Color32[] Produce = { C(0.34f, 0.70f, 0.26f), C(0.96f, 0.56f, 0.14f), C(0.86f, 0.20f, 0.16f), C(0.98f, 0.84f, 0.24f), C(0.56f, 0.30f, 0.62f) };

        static int Hash(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2246822519u ^ (uint)salt * 3266489917u;
                h ^= h >> 13; h *= 668265263u; h ^= h >> 16;
                return (int)(h & 0x7fffffff);
            }
        }

        // ------------------------------------------------------------------------------------ pieces
        static void Box(ArchMesh m, in Frame f, float x0, float x1, float y0, float y1, float z0, float z1, Color32 c, ArchTex t = ArchTex.White, Faces faces = Faces.All & ~Faces.Bottom) =>
            m.Box(f, x0, x1, y0, y1, z0, z1, c, t, faces);

        /// <summary>A round thing lying along `axis` (a wheel, a gas tank on its side), capped.</summary>
        static void Drum(ArchMesh m, Vector3 c, Vector3 axis, float r, float len, int sides, Color32 col, Color32 cap)
        {
            axis.Normalize();
            var s1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            var s2 = Vector3.Cross(axis, s1);
            Vector3 a = c - axis * len * 0.5f, b = c + axis * len * 0.5f;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var d0 = s1 * Mathf.Cos(a0) + s2 * Mathf.Sin(a0);
                var d1 = s1 * Mathf.Cos(a1) + s2 * Mathf.Sin(a1);
                m.QuadFacing(a + d0 * r, b + d0 * r, b + d1 * r, a + d1 * r, d0 + d1, col, ArchTex.White);
                m.TriFacing(a, a + d0 * r, a + d1 * r, -axis, cap, ArchTex.White, Vector2.zero, Vector2.zero, Vector2.zero);
                m.TriFacing(b, b + d0 * r, b + d1 * r, axis, cap, ArchTex.White, Vector2.zero, Vector2.zero, Vector2.zero);
            }
        }

        /// <summary>A potted plant: a clay pot and a lumpy bush.</summary>
        public static void Plant(ArchMesh m, Vector3 at, float size, int seed)
        {
            m.Cylinder(at, 0.18f * size, 0.3f * size, 8, Pot);
            Flora.Puff(m, at + Vector3.up * (0.3f + 0.28f) * size, new Vector3(0.3f, 0.32f, 0.3f) * size, Leaf, LeafLight, seed, false);
        }

        /// <summary>A kapcai (a little step-through motorbike), parked on its stand: nose along `fwd`.</summary>
        public static void Kapcai(ArchMesh m, Vector3 at, Vector3 fwd, Color32 body, int seed)
        {
            fwd.y = 0f; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var f = new Frame(at, right, Vector3.up, -fwd);         // (n = up x r = -fwd: Box faces stay outward)
            // z runs backward along the bike
            Drum(m, at + fwd * 0.62f + Vector3.up * 0.26f, right, 0.26f, 0.1f, 10, Tyre, C(0.70f, 0.70f, 0.72f));
            Drum(m, at - fwd * 0.6f + Vector3.up * 0.26f, right, 0.26f, 0.11f, 10, Tyre, C(0.70f, 0.70f, 0.72f));
            Box(m, f, -0.12f, 0.12f, 0.25f, 0.5f, -0.5f, 0.45f, C(0.22f, 0.22f, 0.24f));                    // the step-through floor
            Box(m, f, -0.17f, 0.17f, 0.42f, 0.78f, 0.05f, 0.75f, body);                                      // the tail body
            Box(m, f, -0.15f, 0.15f, 0.78f, 0.86f, 0.0f, 0.62f, Black);                                      // the seat
            Box(m, f, -0.16f, 0.16f, 0.3f, 0.95f, -0.66f, -0.5f, body);                                      // the leg shield
            Box(m, f, -0.04f, 0.04f, 0.5f, 1.08f, -0.7f, -0.62f, Steel);                                     // the steering column
            Box(m, f, -0.34f, 0.34f, 1.04f, 1.1f, -0.72f, -0.64f, Black);                                    // the handlebar
            Box(m, f, -0.1f, 0.1f, 0.98f, 1.12f, -0.84f, -0.72f, body);                                      // the headlamp cowl
            Box(m, f, -0.06f, 0.06f, 1.0f, 1.08f, -0.86f, -0.84f, C(1f, 0.96f, 0.80f));
            if ((seed & 3) == 0) Box(m, f, -0.2f, 0.2f, 0.86f, 1.1f, 0.5f, 0.85f, C(0.86f, 0.86f, 0.84f));   // a top box
        }

        /// <summary>A plastic stool (or a stack of them).</summary>
        static void Stools(ArchMesh m, Vector3 at, int n, Color32 c)
        {
            for (int k = 0; k < n; k++)
            {
                var b = at + Vector3.up * (k * 0.09f);
                m.Cylinder(b, 0.17f, 0.42f, 8, c, ArchTex.White, k == n - 1);
            }
        }

        // ------------------------------------------------------------------------------------ shop fronts
        /// <summary>
        /// One bay's five-foot way (bay frame: x along the front, z out to the street; the way is z -A..0), or the strip of
        /// pavement in front of a lot without one. `kind` is its walk-in shop (Closed: shutters down).
        /// </summary>
        public static void ShopFront(ArchMesh m, in Frame fb, float bw, float A, float G, InteriorKind kind, int seed, bool awning = true)
        {
            if (!Enabled) return;
            var rng = new System.Random(Hash(seed, 5150));
            bool arcade = A > 0.5f;
            // the strip things stand on: against the shopfront under the five-foot way, else hard by the wall
            float z0 = arcade ? -A + 0.05f : 0.05f, z1 = arcade ? -A + 0.7f : 0.55f;
            float xl = arcade ? 0.55f : 0.15f, xr = bw - (arcade ? 0.55f : 0.15f);
            bool open = kind != InteriorKind.Closed;
            int side = rng.Next(2);                                     // the side the bigger spill goes
            float sx = side == 0 ? xl : xr - 0.9f;                       // a 0.9 m wide spot at one end
            float ox = side == 0 ? xr - 0.6f : xl;                       // a 0.6 m spot at the other
            switch (open ? kind : InteriorKind.Closed)
            {
                case InteriorKind.Runcit:
                    // crates of drinks stacked, a rack of snacks, a sack of onions
                    for (int k = 0; k < 3; k++) Box(m, fb, sx, sx + 0.45f, k * 0.28f, k * 0.28f + 0.27f, z0, z0 + 0.36f, Brights[rng.Next(4)]);
                    Box(m, fb, sx + 0.5f, sx + 0.9f, 0f, 1.25f, z0, z0 + 0.35f, Steel);
                    m.Rect(new Frame(fb.P(0f, 0f, z0 + 0.351f), fb.r, fb.u, fb.n), sx + 0.52f, sx + 0.88f, 0.1f, 1.2f, 0f, White, ArchTex.Goods);
                    m.Cylinder(fb.P(ox + 0.3f, 0f, z0 + 0.25f), 0.22f, 0.35f, 8, C(0.84f, 0.74f, 0.52f));
                    m.Cylinder(fb.P(ox + 0.3f, 0.35f, z0 + 0.25f), 0.18f, 0.08f, 8, Produce[rng.Next(Produce.Length)]);
                    break;
                case InteriorKind.Kopi:
                    // a marble table out on the five-foot way with its stools, a stack of spare stools, the A-frame menu
                    if (arcade)
                    {
                        float tx = bw * 0.5f + (side == 0 ? -1.1f : 1.1f), tz = -A * 0.5f;
                        m.Cylinder(fb.P(tx, 0f, tz), 0.05f, 0.72f, 6, Black, ArchTex.White, false);
                        m.Cylinder(fb.P(tx, 0.72f, tz), 0.38f, 0.04f, 12, C(0.96f, 0.96f, 0.94f, 0.6f));
                        var sc = Brights[rng.Next(2)];
                        Stools(m, fb.P(tx - 0.55f, 0f, tz), 1, sc);
                        Stools(m, fb.P(tx + 0.55f, 0f, tz), 1, sc);
                    }
                    Stools(m, fb.P(ox + 0.3f, 0f, z0 + 0.25f), 4, Brights[rng.Next(2)]);
                    AFrame(m, fb, side == 0 ? xr - 1.1f : xl + 0.7f, arcade ? -0.6f : 0.6f, ArchTex.Menus, 0.5f, 1f);
                    break;
                case InteriorKind.Barber:
                    for (int k = 0; k < 6; k++)
                        m.Cylinder(fb.P(xl + 0.1f, 0.25f + k * 0.26f, z0 + 0.15f), 0.08f, 0.26f, 8, k % 3 == 0 ? C(0.86f, 0.14f, 0.14f) : k % 3 == 1 ? White : C(0.18f, 0.32f, 0.78f));
                    m.Cylinder(fb.P(xl + 0.1f, 0f, z0 + 0.15f), 0.11f, 0.25f, 8, Steel);
                    AFrame(m, fb, xr - 0.5f, arcade ? -0.6f : 0.6f, ArchTex.Menus, 0f, 0.5f);
                    break;
                case InteriorKind.Hardware:
                    // buckets stacked, brooms in a bin, a ladder against the pier, coils of hose
                    for (int k = 0; k < 4; k++) m.Cylinder(fb.P(sx + 0.2f, k * 0.12f, z0 + 0.22f), 0.19f - k * 0.005f, 0.3f, 8, Brights[k % 4]);
                    m.Cylinder(fb.P(sx + 0.65f, 0f, z0 + 0.22f), 0.17f, 0.5f, 8, C(0.22f, 0.50f, 0.30f));
                    for (int k = 0; k < 5; k++) Box(m, fb, sx + 0.57f + k * 0.035f, sx + 0.59f + k * 0.035f, 0.5f, 1.4f, z0 + 0.2f, z0 + 0.22f, Wood);
                    Box(m, fb, ox + 0.05f, ox + 0.09f, 0f, 2.2f, z0 + 0.02f, z0 + 0.3f, C(0.86f, 0.70f, 0.20f));
                    Box(m, fb, ox + 0.45f, ox + 0.49f, 0f, 2.2f, z0 + 0.02f, z0 + 0.3f, C(0.86f, 0.70f, 0.20f));
                    for (int k = 0; k < 6; k++) Box(m, fb, ox + 0.09f, ox + 0.45f, 0.3f + k * 0.32f, 0.34f + k * 0.32f, z0 + 0.12f, z0 + 0.18f, C(0.86f, 0.70f, 0.20f));
                    break;
                case InteriorKind.Textile:
                    // rolls of cloth standing in a basket, a rail of batik
                    for (int k = 0; k < 6; k++) m.Cylinder(fb.P(sx + 0.15f + (k % 3) * 0.2f, 0f, z0 + 0.15f + (k / 3) * 0.2f), 0.08f, 1.2f + (k % 2) * 0.2f, 6, Brights[rng.Next(Brights.Length)]);
                    Box(m, fb, ox, ox + 0.04f, 0f, 1.7f, z0, z0 + 0.04f, Steel); Box(m, fb, ox + 0.56f, ox + 0.6f, 0f, 1.7f, z0, z0 + 0.04f, Steel);
                    Box(m, fb, ox, ox + 0.6f, 1.66f, 1.7f, z0, z0 + 0.04f, Steel);
                    for (int k = 0; k < 4; k++) Box(m, fb, ox + 0.05f + k * 0.13f, ox + 0.15f + k * 0.13f, 0.7f, 1.62f, z0 - 0.2f, z0 + 0.24f, White, ArchTex.Rugs);
                    break;
                case InteriorKind.Gold:
                    Plant(m, fb.P(xl + 0.25f, 0f, z0 + 0.25f), 1.1f, seed);
                    Plant(m, fb.P(xr - 0.25f, 0f, z0 + 0.25f), 1.1f, seed + 1);
                    break;
                default:
                    // shutters down: crates waiting for the morning, a plant, a parked bicycle's worth of clutter
                    if (rng.NextDouble() < 0.6)
                        for (int k = 0; k < 1 + rng.Next(3); k++)
                        {
                            var c = rng.NextDouble() < 0.5 ? Crate : CrateB;
                            Box(m, fb, sx + k * 0.05f, sx + 0.5f + k * 0.05f, k * 0.3f, k * 0.3f + 0.3f, z0, z0 + 0.38f, c);
                            if (k == 0 && rng.NextDouble() < 0.6) Box(m, fb, sx + 0.04f, sx + 0.46f, 0.3f, 0.36f, z0 + 0.04f, z0 + 0.34f, Produce[rng.Next(Produce.Length)], ArchTex.White, Faces.Top);
                        }
                    if (rng.NextDouble() < 0.5) Plant(m, fb.P(ox + 0.3f, 0f, z0 + 0.25f), 0.9f + (float)rng.NextDouble() * 0.4f, seed + 3);
                    break;
            }
            // a striped canvas awning out over the pavement (most open shops, some closed ones)
            if (awning && rng.NextDouble() < (open ? 0.7 : 0.3))
            {
                var tex = (ArchTex)((int)ArchTex.AwningRed + rng.Next(4));
                float top = arcade ? G - 0.02f : Mathf.Min(3.5f, G - 0.3f), drop = 0.7f, reach = arcade ? 1.35f : 1.2f;
                Vector3 a = fb.P(0.12f, top, 0.06f), b = fb.P(bw - 0.12f, top, 0.06f), c = fb.P(bw - 0.12f, top - drop, reach), d = fb.P(0.12f, top - drop, reach);
                m.Quad(d, c, b, a, White, tex, 0f, 0f, Mathf.Max(1f, Mathf.Round(bw / 1.6f)), 1f);
                m.Quad(a, b, c, d, C(0.80f, 0.78f, 0.74f), tex, 0f, 0f, Mathf.Max(1f, Mathf.Round(bw / 1.6f)), 1f);
                foreach (float x in new[] { 0.3f, bw - 0.3f })
                    m.Box(fb, x - 0.02f, x + 0.02f, top - drop * 0.6f, top, 0.06f, reach * 0.6f, Steel);
            }
            // goods hung from the five-foot way's ceiling over the shopfront
            if (arcade && open && kind != InteriorKind.Gold && kind != InteriorKind.Barber && rng.NextDouble() < 0.75)
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(bw / 0.6f), 4, 10);
                for (int k = 0; k < n; k++)
                {
                    float x = 0.6f + (bw - 1.2f) * k / (n - 1), z = -A + 0.35f;
                    float len = 0.25f + (float)rng.NextDouble() * 0.35f, y = G - 0.35f - len;
                    m.Box(fb, x - 0.005f, x + 0.005f, y + len, G - 0.15f, z - 0.005f, z + 0.005f, Black, ArchTex.White, Faces.Front | Faces.Right);
                    var col = kind == InteriorKind.Kopi ? C(0.82f, 0.58f, 0.30f) : Brights[rng.Next(Brights.Length)];
                    if (kind == InteriorKind.Hardware && k % 2 == 0) m.Cylinder(fb.P(x, y, z), 0.12f, len, 8, col);
                    else m.Box(fb, x - 0.12f, x + 0.12f, y, y + len, z - 0.06f, z + 0.06f, col, kind == InteriorKind.Textile ? ArchTex.Rugs : ArchTex.White);
                }
            }
            // a kapcai parked at the five-foot way's edge, nose out to the street
            if (arcade && rng.NextDouble() < 0.4)
            {
                float kx = rng.NextDouble() < 0.5 ? 0.95f : bw - 0.95f;
                Kapcai(m, fb.P(kx, 0f, -0.75f), fb.n, Brights[rng.Next(Brights.Length)], seed);
            }
            // a pot by a column
            if (arcade && rng.NextDouble() < 0.35) Plant(m, fb.P(0.62f, 0f, -0.35f), 0.8f, seed + 9);
            // a hanging shop sign under the five-foot way (a lightbox, its face lit)
            if (arcade && open && rng.NextDouble() < 0.55)
            {
                float hx = rng.NextDouble() < 0.5 ? 0.9f : bw - 0.9f;
                Box(m, fb, hx - 0.03f, hx + 0.03f, G - 0.62f, G - 0.14f, -0.9f, -0.86f, Steel);
                Box(m, fb, hx - 0.06f, hx + 0.06f, G - 1.3f, G - 0.62f, -1.5f, -0.25f, C(0.96f, 0.94f, 0.88f));
                bool was = m.Inside;
                m.Inside = true;                                            // a lightbox glows
                int s = rng.Next(16), cell = s / 4, row = s % 4;
                var tex = (ArchTex)((int)ArchTex.Signs0 + cell);
                float v0 = 1f - (row + 1) / 4f, v1 = 1f - row / 4f;
                foreach (float sgn in new[] { -1f, 1f })
                {
                    var sf = new Frame(fb.P(hx + sgn * 0.061f, G - 1.3f, sgn > 0 ? -1.5f : -0.25f), sgn > 0 ? fb.n : -fb.n, fb.u, sgn > 0 ? fb.r : -fb.r);
                    m.Quad(sf.P(0.03f, 0.04f, 0f), sf.P(1.22f, 0.04f, 0f), sf.P(1.22f, 0.64f, 0f), sf.P(0.03f, 0.64f, 0f), White, tex, 0f, v0, 1f, v1);
                }
                m.Inside = was;
            }
        }

        /// <summary>An A-frame signboard on the pavement (two boards leaning together), showing part of a picture.</summary>
        static void AFrame(ArchMesh m, in Frame fb, float x, float z, ArchTex tex, float v0, float v1)
        {
            float w = 0.6f, h = 0.95f, lean = 0.22f;
            var col = C(0.20f, 0.20f, 0.22f);
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 b0 = fb.P(x - w / 2, 0f, z + s * lean), b1 = fb.P(x + w / 2, 0f, z + s * lean), t0 = fb.P(x - w / 2, h, z), t1 = fb.P(x + w / 2, h, z);
                var nrm = (fb.n * s + Vector3.up * 0.25f).normalized;
                m.QuadFacing(b0, b1, t1, t0, nrm, White, tex, 0f, v0, 1f, v1);
                m.QuadFacing(b0, b1, t1, t0, -nrm, col, ArchTex.White);
            }
        }

        // ------------------------------------------------------------------------------------ back lanes
        /// <summary>The back of a lot on a lane (edge frame: x along the wall seen from the lane, z out into the lane).</summary>
        public static void BackLane(ArchMesh m, in Frame fe, float len, float G, float S, int levels, int seed)
        {
            if (!Enabled || len < 2.4f) return;
            var rng = new System.Random(Hash(seed, 9001));
            // a wheelie bin, its lid a shade darker
            float bx = 0.4f + (float)rng.NextDouble() * Mathf.Max(0.1f, len - 2.6f);
            var bin = rng.NextDouble() < 0.6 ? C(0.20f, 0.52f, 0.30f) : C(0.30f, 0.32f, 0.36f);
            Box(m, fe, bx, bx + 0.6f, 0f, 0.95f, 0.05f, 0.75f, bin);
            Box(m, fe, bx - 0.02f, bx + 0.62f, 0.95f, 1.02f, 0.02f, 0.8f, ArchMesh.Shade(bin, 0.8f));
            // a gas tank and crates by the back door, a water heater on the wall
            if (rng.NextDouble() < 0.7) m.Cylinder(fe.P(bx + 1.0f, 0f, 0.25f), 0.17f, 0.55f, 8, C(0.86f, 0.22f, 0.20f, 0.4f));
            if (rng.NextDouble() < 0.6)
                for (int k = 0; k < 2; k++) Box(m, fe, bx + 1.35f, bx + 1.85f, k * 0.3f, k * 0.3f + 0.3f, 0.05f, 0.45f, k == 0 ? CrateY : Crate);
            if (rng.NextDouble() < 0.5) Box(m, fe, len - 1.4f, len - 0.9f, 1.6f, 2.3f, 0f, 0.3f, C(0.92f, 0.92f, 0.90f));
            // a laundry pole out of an upstairs window, hung with the wash
            if (levels >= 2 && rng.NextDouble() < 0.7)
            {
                float y = G + S * 0.82f, x0 = 0.5f + (float)rng.NextDouble() * Mathf.Max(0.1f, len - 2.2f);
                Box(m, fe, x0, x0 + 0.03f, y, y + 0.03f, 0f, 1.6f, Steel);
                Box(m, fe, x0 + 1.2f, x0 + 1.23f, y, y + 0.03f, 0f, 1.6f, Steel);
                for (int k = 0; k < 4; k++)
                {
                    float z = 0.3f + k * 0.34f, hgt = 0.5f + (float)rng.NextDouble() * 0.4f;
                    Box(m, fe, x0 - 0.05f, x0 + 1.28f, y - hgt, y, z, z + 0.02f, Brights[rng.Next(Brights.Length)], ArchTex.White, Faces.Front | Faces.Back);
                }
            }
            // a cat's bowl and a pot of chillies on the step
            if (rng.NextDouble() < 0.5) Plant(m, fe.P(bx - 0.4f, 0f, 0.3f), 0.7f, seed + 21);
        }

        // ------------------------------------------------------------------------------------ windows
        /// <summary>A window lit from inside (a warm square at its back), for one in three or so of the upstairs ones.</summary>
        public static void LitWindow(ArchMesh m, in Frame f, Opening h, int seed, int salt)
        {
            if (!Enabled || h.depth < 0.05f) return;
            if (h.back != ArchTex.Curtained && h.back != ArchTex.Glass && h.back != ArchTex.Louvre && h.back != ArchTex.Grille) return;
            if (Hash(seed, salt) % 100 > 38) return;
            bool was = m.Inside;
            m.Inside = true;
            var tex = h.back == ArchTex.Glass || h.back == ArchTex.Grille ? ArchTex.Curtained : h.back;
            m.Rect(f, h.x0, h.x1, h.y0, h.y1, -h.depth + 0.004f, h.back == ArchTex.Louvre ? C(0.96f, 0.84f, 0.62f) : White, tex);
            m.Inside = was;
        }

        // ------------------------------------------------------------------------------------ kampung yards
        /// <summary>A kampung house's yard (house frame F: x across, z toward the front, a mirrored frame): a clothesline of
        /// sarongs to one side, a kapcai under the eaves, the tempayan by the stair, the porch lamp on.</summary>
        public static void Yard(ArchMesh m, in Frame F, float w, float y1, float y2, int seed)
        {
            if (!Enabled) return;
            var rng = new System.Random(Hash(seed, 4040));
            float side = rng.NextDouble() < 0.5 ? -1f : 1f;
            // the clothesline: two T-posts out to the side, the line, the wash
            float lx = side * (w / 2 + 2.2f);
            Vector3 p0 = F.P(lx, 0f, -2.6f), p1 = F.P(lx, 0f, 1.6f);
            foreach (var p in new[] { p0, p1 })
            {
                m.Cylinder(p, 0.04f, 1.9f, 4, C(0.62f, 0.46f, 0.30f), ArchTex.White, false);
            }
            var along = (p1 - p0).normalized;
            var across = Vector3.Cross(Vector3.up, along);
            for (int k = 0; k < 4; k++)
            {
                var c0 = Vector3.Lerp(p0, p1, 0.12f + k * 0.21f) + Vector3.up * 1.88f;
                float wd = 0.55f + (float)rng.NextDouble() * 0.25f, ht = 0.7f + (float)rng.NextDouble() * 0.5f;
                var col = Brights[rng.Next(Brights.Length)];
                Vector3 a = c0 - along * wd * 0.5f, b = c0 + along * wd * 0.5f;
                m.QuadFacing(a + Vector3.down * ht, b + Vector3.down * ht, b, a, across, col, k % 2 == 0 ? ArchTex.Rugs : ArchTex.White, 0.5f, 0f, 1f, 0.5f);
                m.QuadFacing(a + Vector3.down * ht, b + Vector3.down * ht, b, a, -across, ArchMesh.Shade(col, 0.85f), k % 2 == 0 ? ArchTex.Rugs : ArchTex.White, 0.5f, 0f, 1f, 0.5f);
            }
            m.QuadFacing(p0 + Vector3.up * 1.9f - across * 0.01f, p1 + Vector3.up * 1.9f - across * 0.01f, p1 + Vector3.up * 1.91f, p0 + Vector3.up * 1.91f, Vector3.up, C(0.9f, 0.9f, 0.9f), ArchTex.White);
            // a kapcai parked under the eaves on the other side, nose to the road
            if (rng.NextDouble() < 0.7) Kapcai(m, F.P(-side * (w / 2 + 0.9f), 0f, 1.0f), F.n, Brights[rng.Next(Brights.Length)], seed);
            // the tempayan (water jar) at the foot of the stair, its dipper
            m.Cylinder(F.P(1.2f, 0f, 5.6f), 0.28f, 0.55f, 10, C(0.46f, 0.28f, 0.20f, 0.4f));
            m.Cylinder(F.P(1.2f, 0.55f, 5.6f), 0.2f, 0.04f, 10, C(0.30f, 0.18f, 0.12f));
            // the porch lamp under the verandah roof, lit
            bool was = m.Inside;
            m.Inside = true;
            m.Cylinder(F.P(0f, y2 - 0.45f, 2.6f), 0.13f, 0.24f, 8, C(1f, 0.92f, 0.70f));
            m.Inside = was;
            m.Cylinder(F.P(0f, y2 - 0.21f, 2.6f), 0.015f, 0.5f, 4, Black, ArchTex.White, false);
            if (rng.NextDouble() < 0.6) Plant(m, F.P(-1.25f, y1, 3.25f), 0.8f, seed + 2);
        }

        /// <summary>The front fence and gate of a kampung yard (house frame, `front` the fence line's z), a pangkin (the
        /// wooden day-bed) under a tree's worth of shade, and a gate lamp that's lit.</summary>
        public static void Fence(ArchMesh m, in Frame F, float w, float front, int seed)
        {
            if (!Enabled) return;
            var rng = new System.Random(Hash(seed, 6060));
            var col = rng.NextDouble() < 0.6 ? C(0.97f, 0.97f, 0.94f) : C(0.40f, 0.62f, 0.52f);
            float half = w / 2 + 2.6f, gate = 0.85f;
            foreach (var (x0, x1) in new[] { (-half, -gate), (gate, half) })
            {
                Vector3 a = F.P(x0, 0f, front), b = F.P(x1, 0f, front);
                var out_ = F.n;
                m.QuadFacing(a, b, b + Vector3.up * 0.95f, a + Vector3.up * 0.95f, out_, col, ArchTex.Rail, 0f, 0f, Mathf.Abs(x1 - x0) / 0.9f, 1f);
                m.QuadFacing(a, b, b + Vector3.up * 0.95f, a + Vector3.up * 0.95f, -out_, ArchMesh.Shade(col, 0.9f), ArchTex.Rail, 0f, 0f, Mathf.Abs(x1 - x0) / 0.9f, 1f);
                for (float x = x0; x <= x1 + 0.01f; x += Mathf.Max(0.5f, (x1 - x0) / Mathf.Max(1, Mathf.Round((x1 - x0) / 2f))))
                    m.Cylinder(F.P(x, 0f, front), 0.06f, 1.1f, 4, col);
            }
            // the returns along the sides, a short way back
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 a = F.P(s * half, 0f, front), b = F.P(s * half, 0f, front - 3.5f);
                var o = F.r * s;
                m.QuadFacing(a, b, b + Vector3.up * 0.95f, a + Vector3.up * 0.95f, o, col, ArchTex.Rail, 0f, 0f, 3.5f / 0.9f, 1f);
                m.QuadFacing(a, b, b + Vector3.up * 0.95f, a + Vector3.up * 0.95f, -o, ArchMesh.Shade(col, 0.9f), ArchTex.Rail, 0f, 0f, 3.5f / 0.9f, 1f);
            }
            // gate posts with lamps on, the gate leaf swung open
            foreach (float s in new[] { -1f, 1f })
            {
                m.Cylinder(F.P(s * gate, 0f, front), 0.12f, 1.3f, 4, C(0.86f, 0.84f, 0.80f));
                bool was = m.Inside;
                m.Inside = true;
                m.Cylinder(F.P(s * gate, 1.3f, front), 0.1f, 0.2f, 6, C(1f, 0.94f, 0.72f));
                m.Inside = was;
            }
            Vector3 g0 = F.P(gate, 0.08f, front), g1 = F.P(gate, 0.08f, front - 1.4f);
            m.QuadFacing(g0, g1, g1 + Vector3.up * 0.9f, g0 + Vector3.up * 0.9f, F.r, col, ArchTex.Rail, 0f, 0f, 1.5f, 1f);
            m.QuadFacing(g0, g1, g1 + Vector3.up * 0.9f, g0 + Vector3.up * 0.9f, -F.r, col, ArchTex.Rail, 0f, 0f, 1.5f, 1f);
            // the pangkin: a low platform of planks on four legs, off to one side of the yard
            float px = (rng.NextDouble() < 0.5 ? -1f : 1f) * (w / 2 + 1.0f), pz = front - 2.2f;
            var pc = C(0.66f, 0.48f, 0.30f);
            var pf = new Frame(F.P(px, 0f, pz), Vector3.right, Vector3.up, Vector3.back);
            m.Box(pf, -0.9f, 0.9f, 0.4f, 0.48f, -0.6f, 0.6f, pc, ArchTex.PlanksH, Faces.All, 0.6f);
            foreach (var (lx, lz) in new[] { (-0.8f, -0.5f), (0.8f, -0.5f), (-0.8f, 0.5f), (0.8f, 0.5f) })
                m.Box(pf, lx - 0.05f, lx + 0.05f, 0f, 0.4f, lz - 0.05f, lz + 0.05f, ArchMesh.Shade(pc, 0.8f), Faces.All & ~Faces.Bottom);
            if ((seed & 1) == 0) m.Cylinder(F.P(px + 0.4f, 0.48f, pz), 0.07f, 0.1f, 6, C(0.95f, 0.95f, 0.92f));      // a cup of kopi left out
        }
    }
}
