using System.Collections.Generic;
using UnityEngine;

namespace KampungRun.Arch
{
    /// <summary>
    /// Everything that isn't a shophouse: office towers (podium, curtain wall or ribbon windows, a crown), flats
    /// (punched windows with air-con units and balconies, stair cores), civic buildings (a portico), places of
    /// worship (white, arched windows, a pitched roof) and sheds (zinc walls, roller doors). One storey of a facade is
    /// one wall with its openings, so a 40-storey tower stays a few thousand triangles.
    /// </summary>
    public static class Block
    {
        static readonly Color32[] Pastel =
        {
            ArchMesh.Col(0.96f, 0.76f, 0.78f), ArchMesh.Col(0.97f, 0.88f, 0.55f), ArchMesh.Col(0.70f, 0.87f, 0.76f), ArchMesh.Col(0.70f, 0.82f, 0.92f),
            ArchMesh.Col(0.97f, 0.79f, 0.64f), ArchMesh.Col(0.96f, 0.92f, 0.80f), ArchMesh.Col(0.95f, 0.95f, 0.92f), ArchMesh.Col(0.82f, 0.75f, 0.91f),
            ArchMesh.Col(0.46f, 0.62f, 0.82f), ArchMesh.Col(0.40f, 0.68f, 0.70f), ArchMesh.Col(0.58f, 0.63f, 0.70f), ArchMesh.Col(0.80f, 0.79f, 0.76f),
        };
        static readonly Color32 Trim = ArchMesh.Col(0.96f, 0.95f, 0.92f);
        static readonly Color32 Reveal = ArchMesh.Col(0.82f, 0.81f, 0.78f);
        static readonly Color32 Glass = ArchMesh.Col(1f, 1f, 1f, 0.5f);
        static readonly Color32 White = ArchMesh.Col(1f, 1f, 1f);
        static readonly Color32 Metal = ArchMesh.Col(0.62f, 0.64f, 0.68f);
        static readonly Color32 Mullion = ArchMesh.Col(0.34f, 0.38f, 0.44f);
        static readonly Color32 Concrete = ArchMesh.Col(0.78f, 0.77f, 0.74f);
        static readonly Color32 Zinc = ArchMesh.Col(0.72f, 0.74f, 0.76f);
        static readonly Color32 RoofGreen = ArchMesh.Col(0.32f, 0.58f, 0.44f), RoofRed = ArchMesh.Col(0.78f, 0.38f, 0.26f);

        static readonly List<Opening> _holes = new List<Opening>(64);

        class B
        {
            public BuildingSpec s;
            public System.Random rng;
            public bool detail, glass, tower;
            public Color32 wall, trim, accent;
            public float G, S, h;
            public int lv;
        }

        public static void Build(BuildingSpec s, ArchMesh m, bool detail)
        {
            var b = new B { s = s, rng = new System.Random(s.seed), detail = detail, h = s.height, lv = Mathf.Max(1, s.levels) };
            b.glass = s.colour >= 9 && s.colour <= 11;
            b.tower = s.kind == BuildingKind.Office && b.lv >= 6;
            b.wall = s.kind == BuildingKind.Worship ? ArchMesh.Col(0.97f, 0.96f, 0.93f)
                   : s.kind == BuildingKind.Shed ? Zinc
                   : s.colour > 0 ? Pastel[(s.colour - 1) % Pastel.Length] : Pastel[b.rng.Next(8)];
            b.trim = b.rng.NextDouble() < 0.7 ? Trim : ArchMesh.Shade(b.wall, 0.82f);
            b.accent = ArchMesh.Shade(b.wall, 0.72f);
            b.G = b.lv <= 1 ? b.h : Mathf.Clamp(b.h / b.lv * 1.3f, 3.6f, 5.4f);
            b.S = b.lv <= 1 ? 0f : (b.h - b.G) / (b.lv - 1);

            var ring = s.ring;
            int n = ring.Length;
            int front = Shophouse.FrontEdge(s);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = ring[i], c = ring[(i + 1) % n];
                float len = (c - a).magnitude;
                if (len < 0.05f) continue;
                var f = Frame.Edge(a, c, s.y0);
                var e = s.Edge(i);
                bool party = (e & EdgeFlags.Party) != 0;
                bool street = (e & (EdgeFlags.Street | EdgeFlags.Walkway)) != 0 || i == front;
                if (party && b.lv <= 6) { m.Rect(f, 0f, len, 0f, b.h, 0f, ArchMesh.Shade(b.wall, 0.88f), ArchTex.White); continue; }
                Facade(m, f, len, b, street, i == front);
            }
            Roof(m, b);
        }

        // ------------------------------------------------------------------------------------------ facades
        /// <summary>The distance version of a wall: the ground floor and the floors above each painted on one quad.</summary>
        static void FarFacade(ArchMesh m, in Frame f, float len, B b, bool street)
        {
            var kind = b.s.kind;
            if (kind == BuildingKind.Shed) { m.Rect(f, 0f, len, 0f, b.h, 0f, b.wall, ArchTex.Zinc, 1.2f); return; }
            float bays = Mathf.Max(1f, Mathf.Round(len / (b.tower ? 3.0f : 3.2f)));
            if (street && (kind == BuildingKind.Office || kind == BuildingKind.Flats || kind == BuildingKind.Civic))
                m.Quad(f.P(0f, 0f, 0f), f.P(len, 0f, 0f), f.P(len, b.G, 0f), f.P(0f, b.G, 0f), White, ArchTex.ShopFar, 0f, 0f, Mathf.Max(1f, Mathf.Round(len / 4.5f)), 1f);
            else m.Quad(f.P(0f, 0f, 0f), f.P(len, 0f, 0f), f.P(len, b.G, 0f), f.P(0f, b.G, 0f), b.wall, ArchTex.WinFar, 0f, 0f, bays, 1f);
            if (b.lv < 2) return;
            if (b.tower && b.glass)
                m.Quad(f.P(0f, b.G, 0f), f.P(len, b.G, 0f), f.P(len, b.h, 0f), f.P(0f, b.h, 0f), Color32.Lerp(b.wall, White, 0.55f), ArchTex.Curtain,
                       0f, 0f, bays * 0.5f, (b.lv - 1) * 0.5f);
            else
                m.Quad(f.P(0f, b.G, 0f), f.P(len, b.G, 0f), f.P(len, b.h, 0f), f.P(0f, b.h, 0f), b.wall, b.tower ? ArchTex.RibbonFar : ArchTex.WinFar,
                       0f, 0f, bays, b.lv - 1);
        }

        static void Facade(ArchMesh m, in Frame f, float len, B b, bool street, bool main)
        {
            if (!b.detail) { FarFacade(m, f, len, b, street); return; }
            var kind = b.s.kind;
            // ground floor
            _holes.Clear();
            if (kind == BuildingKind.Shed)
            {
                int doors = Mathf.Max(1, Mathf.FloorToInt(len / 7f));
                for (int k = 0; k < doors && len > 4.5f; k++)
                {
                    float cx = len * (k + 0.5f) / doors;
                    _holes.Add(new Opening(cx - 1.8f, cx + 1.8f, 0f, Mathf.Min(3.6f, b.G - 0.6f), b.detail ? 0.15f : 0f, ArchTex.Roller, White, Reveal));
                }
                m.Panel(f, 0f, len, 0f, b.h, 0f, b.wall, ArchTex.Zinc, 1.2f, _holes);
                return;
            }
            float bay = b.tower ? 3.0f : kind == BuildingKind.Worship ? 3.4f : 3.2f;
            int bays = Mathf.Max(1, Mathf.RoundToInt(len / bay));
            float bw = len / bays;
            if (street && (kind == BuildingKind.Office || kind == BuildingKind.Flats || kind == BuildingKind.Civic))
            {
                // shops, a lobby or an entrance hall along the street
                for (int k = 0; k < bays; k++)
                {
                    float x0 = k * bw + 0.25f, x1 = (k + 1) * bw - 0.25f;
                    var back = b.tower ? ArchTex.Glass : (k % 3 == 1 ? ArchTex.DoorGlass : ArchTex.GlassShop);
                    if (kind == BuildingKind.Civic) back = k == bays / 2 ? ArchTex.DoorGlass : ArchTex.Glass;
                    _holes.Add(new Opening(x0, x1, 0.0f, Mathf.Min(3.3f, b.G - 0.7f), b.detail ? 0.25f : 0f, back, back == ArchTex.Glass ? Glass : White, Reveal));
                }
            }
            else if (kind == BuildingKind.Worship)
            {
                for (int k = 0; k < bays; k++)
                {
                    float cx = (k + 0.5f) * bw;
                    _holes.Add(new Opening(cx - 0.6f, cx + 0.6f, 0.9f, Mathf.Min(b.G - 0.6f, 3.2f), b.detail ? 0.3f : 0f,
                                           k == bays / 2 && main ? ArchTex.Door : ArchTex.Grille, k == bays / 2 && main ? ArchMesh.Col(0.4f, 0.55f, 0.35f) : White, Reveal));
                }
            }
            else
            {
                for (int k = 0; k < bays; k++)
                {
                    float cx = (k + 0.5f) * bw;
                    if (k == 0 && main && kind != BuildingKind.Office)
                        _holes.Add(new Opening(cx - 0.55f, cx + 0.55f, 0f, 2.3f, b.detail ? 0.15f : 0f, ArchTex.Door, b.accent, Reveal));
                    else _holes.Add(new Opening(cx - 0.6f, cx + 0.6f, 1.0f, Mathf.Min(2.4f, b.G - 0.5f), b.detail ? 0.15f : 0f, ArchTex.Grille, White, Reveal));
                }
            }
            m.Panel(f, 0f, len, 0f, b.G, 0f, b.wall, ArchTex.White, 0f, _holes);
            if (b.detail && street && kind != BuildingKind.Worship && kind != BuildingKind.Shed)
            {
                // a canopy over the ground floor
                m.Box(f, -0.05f, len + 0.05f, b.G - 0.55f, b.G - 0.3f, 0f, 1.6f, b.tower ? Metal : b.trim);
                if (kind == BuildingKind.Civic && len > 10f)
                {
                    // a portico: four columns under a pediment
                    float c = len * 0.5f, w = Mathf.Min(8f, len * 0.5f);
                    for (int k = 0; k < 4; k++)
                    {
                        float x = c - w / 2 + w * k / 3f;
                        m.Box(f, x - 0.3f, x + 0.3f, 0f, b.G + 0.6f, 2.2f, 2.8f, Trim);
                    }
                    m.Box(f, c - w / 2 - 0.5f, c + w / 2 + 0.5f, b.G + 0.6f, b.G + 1.2f, 0f, 3.0f, Trim);
                    m.Extrude(f, new[] { new Vector2(c - w / 2 - 0.5f, b.G + 1.2f), new Vector2(c + w / 2 + 0.5f, b.G + 1.2f), new Vector2(c, b.G + 2.6f) },
                              2.0f, 3.0f, Trim, ArchTex.White);
                }
            }

            // upper floors
            for (int k = 1; k < b.lv; k++)
            {
                float y0 = b.G + (k - 1) * b.S, y1 = y0 + b.S;
                _holes.Clear();
                if (b.tower && b.glass)
                {
                    // curtain wall: a glazed band across the whole face, the spandrel between
                    _holes.Add(new Opening(0.15f, len - 0.15f, y0 + 0.75f, y1 - 0.12f, b.detail ? 0.12f : 0f, ArchTex.Curtain, Glass, Mullion));
                    m.Panel(f, 0f, len, y0, y1, 0f, b.wall, ArchTex.White, 0f, _holes);
                    continue;
                }
                if (b.tower)
                {
                    // ribbon windows between concrete bands
                    _holes.Add(new Opening(0.3f, len - 0.3f, y0 + 0.9f, y1 - 0.35f, b.detail ? 0.15f : 0f, ArchTex.GlassDark, Glass, Reveal));
                    m.Panel(f, 0f, len, y0, y1, 0f, b.wall, ArchTex.White, 0f, _holes);
                    if (b.detail)
                        for (int q = 1; q < bays; q++)
                        {
                            float x = q * bw;
                            m.Box(f, x - 0.06f, x + 0.06f, y0 + 0.9f, y1 - 0.35f, -0.15f, -0.05f, Mullion, Faces.Front | Faces.Left | Faces.Right);
                        }
                    continue;
                }
                for (int q = 0; q < bays; q++)
                {
                    float cx = (q + 0.5f) * bw;
                    float ww = kind == BuildingKind.Worship ? 1.1f : Mathf.Min(1.8f, bw - 0.7f), wy = y0 + 0.95f, wh = Mathf.Min(1.55f, b.S - 1.3f);
                    double r = b.rng.NextDouble();
                    var back = kind == BuildingKind.Worship ? ArchTex.Grille : r < 0.35 ? ArchTex.Glass : r < 0.6 ? ArchTex.Grille : r < 0.85 ? ArchTex.Curtained : ArchTex.Louvre;
                    var col = back == ArchTex.Louvre ? b.accent : back == ArchTex.Glass ? Glass : White;
                    _holes.Add(new Opening(cx - ww / 2, cx + ww / 2, wy, wy + wh, b.detail ? 0.18f : 0f, back, col, Reveal));
                }
                m.Panel(f, 0f, len, y0, y1, 0f, b.wall, ArchTex.White, 0f, _holes);
                if (!b.detail) continue;
                foreach (var h in _holes)
                {
                    m.Box(f, h.x0 - 0.1f, h.x1 + 0.1f, h.y0 - 0.1f, h.y0, 0f, 0.12f, b.trim);                // sill
                    m.Box(f, h.x0 - 0.12f, h.x1 + 0.12f, h.y1, h.y1 + 0.12f, 0f, 0.3f, b.trim);              // a little rain hood
                    if (kind != BuildingKind.Flats) continue;
                    double r = b.rng.NextDouble();
                    if (r < 0.3) AcUnit(m, f, h.x1 + 0.15f, h.y0 + 0.05f);
                    else if (r < 0.5) Laundry(m, f, (h.x0 + h.x1) * 0.5f, h.y0 - 0.2f, b.rng);
                }
                if (kind == BuildingKind.Flats && k % 2 == 1 && len > 8f && main)
                {
                    // balconies on alternate floors along the front
                    for (int q = 0; q + 1 < bays; q += 2)
                    {
                        float x0 = q * bw + 0.3f, x1 = (q + 2) * bw - 0.3f;
                        m.Box(f, x0, x1, y0 - 0.15f, y0, 0f, 1.2f, Concrete);
                        m.Box(f, x0, x1, y0, y0 + 1.05f, 1.1f, 1.2f, b.trim, Faces.Front | Faces.Back | Faces.Top);
                        m.Rect(f, x0 + 0.05f, x1 - 0.05f, y0 + 0.1f, y0 + 0.95f, 1.21f, b.trim, ArchTex.Rail, 1.0f);
                        m.Box(f, x0, x0 + 0.1f, y0, y0 + 1.05f, 0f, 1.2f, b.trim, Faces.Left | Faces.Right | Faces.Top);
                        m.Box(f, x1 - 0.1f, x1, y0, y0 + 1.05f, 0f, 1.2f, b.trim, Faces.Left | Faces.Right | Faces.Top);
                    }
                }
                if (k < b.lv - 1 && (kind != BuildingKind.Office))
                    m.Box(f, -0.03f, len + 0.03f, y1 - 0.08f, y1 + 0.08f, 0f, 0.1f, b.trim, Faces.Front | Faces.Top | Faces.Bottom);
            }
            // cornice
            m.Box(f, -0.05f, len + 0.05f, b.h - 0.1f, b.h + 0.2f, 0f, b.detail ? 0.25f : 0.15f, b.trim, Faces.Front | Faces.Top | Faces.Bottom | Faces.Left | Faces.Right);
            // walk-up flats: the stair core at one end, a tower of ventilation blocks standing proud of the roof
            if (kind == BuildingKind.Flats && main && len > 12f && b.lv >= 3)
            {
                float x0 = b.rng.NextDouble() < 0.5 ? 0.6f : len - 3.6f;
                m.Box(f, x0, x0 + 3f, 0f, b.h + 2.4f, 0f, 0.35f, b.wall, Faces.Front | Faces.Left | Faces.Right | Faces.Top);
                m.Rect(f, x0 + 0.3f, x0 + 2.7f, b.G, b.h + 1.8f, 0.36f, b.trim, ArchTex.Breeze, 1.2f);
                m.Box(f, x0 - 0.15f, x0 + 3.15f, b.h + 2.4f, b.h + 2.6f, -0.4f, 0.5f, b.trim);
            }
        }

        /// <summary>Laundry out to dry on poles under a window, the way every KL flat does it.</summary>
        static void Laundry(ArchMesh m, in Frame f, float cx, float y, System.Random rng)
        {
            var pole = ArchMesh.Col(0.75f, 0.68f, 0.5f);
            for (int p = -1; p <= 1; p += 2)
                m.Box(f, cx + p * 0.45f - 0.025f, cx + p * 0.45f + 0.025f, y, y + 0.05f, 0f, 1.5f, pole, Faces.All & ~Faces.Back);
            int n = rng.Next(2, 5);
            for (int k = 0; k < n; k++)
            {
                float z = 0.3f + k * 1.1f / n, w = 0.35f + (float)rng.NextDouble() * 0.3f, hgt = 0.4f + (float)rng.NextDouble() * 0.5f;
                var c = Cloth[rng.Next(Cloth.Length)];
                m.Box(f, cx - w * 0.5f, cx + w * 0.5f, y - hgt, y, z - 0.02f, z + 0.02f, c, Faces.Front | Faces.Back | Faces.Left | Faces.Right);
            }
        }

        static readonly Color32[] Cloth =
        {
            ArchMesh.Col(0.9f, 0.25f, 0.25f), ArchMesh.Col(0.25f, 0.5f, 0.9f), ArchMesh.Col(0.98f, 0.85f, 0.3f), ArchMesh.Col(0.95f, 0.95f, 0.95f),
            ArchMesh.Col(0.4f, 0.75f, 0.45f), ArchMesh.Col(0.95f, 0.55f, 0.75f), ArchMesh.Col(0.6f, 0.4f, 0.75f),
        };

        static void AcUnit(ArchMesh m, in Frame f, float x, float y)
        {
            m.Box(f, x, x + 0.85f, y - 0.6f, y, 0.06f, 0.38f, ArchMesh.Col(0.93f, 0.93f, 0.92f), Faces.All & ~Faces.Front & ~Faces.Back);
            m.Rect(f, x, x + 0.85f, y - 0.6f, y, 0.38f, White, ArchTex.AC);
        }

        // ------------------------------------------------------------------------------------------ roofs
        static void Roof(ArchMesh m, B b)
        {
            var s = b.s;
            float y = s.y0 + b.h;
            var ring = s.ring;
            int n = ring.Length;
            if (s.kind == BuildingKind.Worship || (s.kind == BuildingKind.Shed && s.Area() < 900f) || (s.kind == BuildingKind.House))
            {
                if (HipRoof(m, s, y, s.kind == BuildingKind.Worship ? (s.colour == 0 ? RoofGreen : RoofRed) : s.kind == BuildingKind.Shed ? Zinc : RoofRed,
                            s.kind == BuildingKind.Shed ? ArchTex.Zinc : ArchTex.Tiles)) return;
            }
            m.Cap(ring, y + 0.04f, Concrete);
            float parapet = b.tower ? 1.2f : 0.9f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = ring[i], c = ring[(i + 1) % n];
                float len = (c - a).magnitude;
                if (len < 0.2f) continue;
                var f = Frame.Edge(a, c, y);
                m.Box(f, 0f, len, 0.2f, parapet, -0.22f, 0f, b.wall, Faces.Front | Faces.Back | Faces.Top);
                if (b.detail) m.Box(f, -0.02f, len + 0.02f, parapet, parapet + 0.1f, -0.26f, 0.04f, b.trim, Faces.Front | Faces.Back | Faces.Top);
            }
            if (!b.detail) return;
            var cen = s.Centre();
            float area = Mathf.Abs(s.Area());
            if (b.tower)
            {
                // a plant room set back on the roof, an antenna or a helipad ring
                float r = Mathf.Sqrt(area) * 0.22f;
                var pf = new Frame(new Vector3(cen.x, y, cen.y), Vector3.right, Vector3.up, Vector3.back);
                m.Box(pf, -r, r, 0f, 3.2f, -r, r, Concrete);
                m.Box(pf, -r * 0.6f, r * 0.6f, 3.2f, 4.0f, -r * 0.6f, r * 0.6f, ArchMesh.Col(0.86f, 0.86f, 0.84f));
                if (b.rng.NextDouble() < 0.5) m.Cylinder(new Vector3(cen.x + r * 0.3f, y + 4f, cen.y), 0.12f, 9f, 6, Metal);
                return;
            }
            // tanks and condensers on lower blocks
            int items = Mathf.Clamp(Mathf.RoundToInt(area / 120f), 1, 6);
            for (int k = 0; k < items; k++)
            {
                var p = cen + new Vector2((float)b.rng.NextDouble() - 0.5f, (float)b.rng.NextDouble() - 0.5f) * Mathf.Sqrt(area) * 0.4f;
                if (k % 2 == 0) m.Cylinder(new Vector3(p.x, y + 0.05f, p.y), 0.7f, 1.3f, 10, ArchMesh.Col(0.24f, 0.46f, 0.74f));
                else
                {
                    var af = new Frame(new Vector3(p.x, y + 0.05f, p.y), Vector3.right, Vector3.up, Vector3.back);
                    m.Box(af, -0.45f, 0.45f, 0f, 0.62f, -0.18f, 0.18f, ArchMesh.Col(0.93f, 0.93f, 0.92f), Faces.All & ~Faces.Front & ~Faces.Bottom);
                    m.Rect(af, -0.45f, 0.45f, 0f, 0.62f, 0.18f, White, ArchTex.AC);
                }
            }
        }

        /// <summary>A hipped roof over the footprint's oriented bounding box, when the footprint is near enough to one.</summary>
        public static bool HipRoof(ArchMesh m, BuildingSpec s, float y, Color32 col, ArchTex tex)
        {
            var ring = s.ring;
            // the oriented box along the longest edge
            int longest = 0; float best = 0f;
            for (int i = 0; i < ring.Length; i++) { float l = (ring[(i + 1) % ring.Length] - ring[i]).sqrMagnitude; if (l > best) { best = l; longest = i; } }
            var ax = (ring[(longest + 1) % ring.Length] - ring[longest]).normalized;
            var ay = new Vector2(-ax.y, ax.x);
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var p in ring) { float u = Vector2.Dot(p, ax), v = Vector2.Dot(p, ay); x0 = Mathf.Min(x0, u); x1 = Mathf.Max(x1, u); z0 = Mathf.Min(z0, v); z1 = Mathf.Max(z1, v); }
            float boxArea = (x1 - x0) * (z1 - z0);
            if (boxArea < 1f || Mathf.Abs(s.Area()) / boxArea < 0.82f) return false;
            float ov = 0.45f;
            x0 -= ov; x1 += ov; z0 -= ov; z1 += ov;
            float w = x1 - x0, d = z1 - z0, rise = Mathf.Min(w, d) * 0.32f;
            Vector3 P(float u, float v, float yy) { var q = ax * u + ay * v; return new Vector3(q.x, yy, q.y); }
            float inset = Mathf.Min(w, d) * 0.5f;
            float rx0 = x0 + inset, rx1 = x1 - inset, rz = (z0 + z1) * 0.5f;
            if (rx1 < rx0) rx0 = rx1 = (x0 + x1) * 0.5f;
            float ye = y - 0.15f, yr = y + rise;
            // two long slopes and two hips
            m.QuadFacing(P(x0, z0, ye), P(x1, z0, ye), P(rx1, rz, yr), P(rx0, rz, yr), Vector3.up + new Vector3(-ay.x, 0f, -ay.y), col, tex, 0f, 0f, w / 1.2f, d * 0.6f / 1.2f);
            m.QuadFacing(P(x1, z1, ye), P(x0, z1, ye), P(rx0, rz, yr), P(rx1, rz, yr), Vector3.up + new Vector3(ay.x, 0f, ay.y), col, tex, 0f, 0f, w / 1.2f, d * 0.6f / 1.2f);
            m.TriFacing(P(x0, z1, ye), P(x0, z0, ye), P(rx0, rz, yr), Vector3.up + new Vector3(-ax.x, 0f, -ax.y), col, tex, Vector2.zero, new Vector2(d / 1.2f, 0f), new Vector2(d / 2.4f, d / 2.4f));
            m.TriFacing(P(x1, z0, ye), P(x1, z1, ye), P(rx1, rz, yr), Vector3.up + new Vector3(ax.x, 0f, ax.y), col, tex, Vector2.zero, new Vector2(d / 1.2f, 0f), new Vector2(d / 2.4f, d / 2.4f));
            // the underside of the eaves, seen from the street
            var soffit = ArchMesh.Shade(col, 0.62f);
            m.QuadFacing(P(x0, z0, ye), P(x1, z0, ye), P(x1 - ov, z0 + ov, ye), P(x0 + ov, z0 + ov, ye), Vector3.down, soffit, ArchTex.White);
            m.QuadFacing(P(x1, z1, ye), P(x0, z1, ye), P(x0 + ov, z1 - ov, ye), P(x1 - ov, z1 - ov, ye), Vector3.down, soffit, ArchTex.White);
            m.QuadFacing(P(x0, z1, ye), P(x0, z0, ye), P(x0 + ov, z0 + ov, ye), P(x0 + ov, z1 - ov, ye), Vector3.down, soffit, ArchTex.White);
            m.QuadFacing(P(x1, z0, ye), P(x1, z1, ye), P(x1 - ov, z1 - ov, ye), P(x1 - ov, z0 + ov, ye), Vector3.down, soffit, ArchTex.White);
            return true;
        }
    }
}
