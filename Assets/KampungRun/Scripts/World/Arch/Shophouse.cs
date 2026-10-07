using System.Collections.Generic;
using UnityEngine;

namespace KampungRun.Arch
{
    public enum ShopStyle { PreWar, Deco, Modern }

    /// <summary>
    /// KL shophouses, one lot at a time, from a footprint whose street edge is the front. Hit &amp; Run's way with
    /// buildings: few, bold, readable forms, every one modelled, flat paint and painted detail.
    ///  - Ground floor: a five-foot way where the lot is deep enough (the upper floors carried over the pavement
    ///    on square columns), the shopfront set back with folding doors, a roller shutter or the shop open, and
    ///    a band of ventilation blocks over the doors; a signboard along the floor line above.
    ///  - Upper floors: pre-war (tall shuttered windows in moulded frames, sills, hoods), Art Deco (concrete
    ///    sun-hoods and fins) or 1970s modern (a ribbon window with grilles, an air-con unit hung under it).
    ///  - Top: cornice and parapet, a dated pediment (pre-war) or a stepped one with a flagpole (Deco), a clay
    ///    tile roof behind with fire walls on the party lines; modern lots get a flat roof with a water tank.
    ///  - The other walls: blank on party lines, small windows and a back door elsewhere.
    /// `detail` false gives the distance version: same massing, flat windows, no frames or clutter.
    /// </summary>
    public static class Shophouse
    {
        // the paint of Jalan Petaling and Jalan Sultan: chalky pastels, white trim, deep-coloured shutters
        static readonly Color32[] Walls =
        {
            ArchMesh.Col(0.66f, 0.85f, 0.73f), ArchMesh.Col(0.66f, 0.80f, 0.89f), ArchMesh.Col(0.95f, 0.90f, 0.67f), ArchMesh.Col(0.96f, 0.72f, 0.76f),
            ArchMesh.Col(0.97f, 0.78f, 0.63f), ArchMesh.Col(0.79f, 0.71f, 0.89f), ArchMesh.Col(0.97f, 0.86f, 0.44f), ArchMesh.Col(0.95f, 0.95f, 0.92f),
            ArchMesh.Col(0.50f, 0.80f, 0.80f), ArchMesh.Col(0.94f, 0.58f, 0.54f), ArchMesh.Col(0.82f, 0.88f, 0.62f), ArchMesh.Col(0.98f, 0.93f, 0.85f),
        };
        static readonly Color32[] Shutters =
        {
            ArchMesh.Col(0.16f, 0.47f, 0.34f), ArchMesh.Col(0.16f, 0.50f, 0.66f), ArchMesh.Col(0.55f, 0.34f, 0.17f), ArchMesh.Col(0.66f, 0.20f, 0.15f),
            ArchMesh.Col(0.22f, 0.60f, 0.58f), ArchMesh.Col(0.20f, 0.28f, 0.55f), ArchMesh.Col(0.92f, 0.92f, 0.88f),
        };
        static readonly Color32 Trim = ArchMesh.Col(0.97f, 0.96f, 0.92f);
        static readonly Color32 Reveal = ArchMesh.Col(0.84f, 0.82f, 0.78f);
        static readonly Color32 Soffit = ArchMesh.Col(0.82f, 0.80f, 0.76f);
        static readonly Color32 Concrete = ArchMesh.Col(0.80f, 0.79f, 0.76f);
        static readonly Color32 TileRed = ArchMesh.Col(0.80f, 0.40f, 0.26f), TileGreen = ArchMesh.Col(0.34f, 0.58f, 0.44f), TileDark = ArchMesh.Col(0.52f, 0.30f, 0.24f);
        static readonly Color32 White = ArchMesh.Col(1f, 1f, 1f);
        static readonly Color32 Glass = ArchMesh.Col(1f, 1f, 1f, 0.45f);
        static readonly Color32 Metal = ArchMesh.Col(0.62f, 0.64f, 0.68f);
        static readonly Color32 Dark = ArchMesh.Col(0.26f, 0.25f, 0.28f);
        static readonly Color32 SignSide = ArchMesh.Col(0.2f, 0.18f, 0.2f);

        static readonly ArchTex[] ShopBacks =
            { ArchTex.Folding, ArchTex.Roller, ArchTex.GlassShop, ArchTex.Kopitiam, ArchTex.Hardware, ArchTex.Textile, ArchTex.GoldShop, ArchTex.Folding, ArchTex.Roller, ArchTex.Kopitiam };

        const float ColW = 0.5f;

        class Lot
        {
            public System.Random rng;
            public ShopStyle style;
            public Color32 wall, wallDark, trim, shutter, tile;
            public float W, D, G, S, h, A, P;
            public int lv, sign, plaque;
            public ArchTex shopBack, shopBack2;
            public bool detail, shop, flat;

            /// <summary>One bay of a wide front: the same building, its own shop, sign and (unless the row was
            /// painted as one) its own colours.</summary>
            public Lot Bay(float w, bool uniform)
            {
                var b = (Lot)MemberwiseClone();
                b.W = w;
                if (!uniform)
                {
                    b.wall = Walls[rng.Next(Walls.Length)];
                    b.wallDark = ArchMesh.Shade(b.wall, 0.86f);
                    b.shutter = Shutters[rng.Next(Shutters.Length)];
                }
                b.sign = rng.Next(16);
                b.plaque = rng.Next(8);
                b.shopBack = ShopBacks[rng.Next(ShopBacks.Length)];
                b.shopBack2 = rng.NextDouble() < 0.6 ? b.shopBack : ShopBacks[rng.Next(ShopBacks.Length)];
                return b;
            }
        }

        /// <summary>A wide front is a row of lots under one roof, each with its own shop and sign and (unless the row
        /// was painted as one) its own colours. Drawn up front, so the near and far versions agree.</summary>
        static Lot[] Bays(Lot L)
        {
            bool uniform = L.rng.NextDouble() < 0.45;
            int n = L.W > 7.5f ? Mathf.Max(2, Mathf.RoundToInt(L.W / 5.2f)) : 1;
            var bays = new Lot[n];
            for (int i = 0; i < n; i++) bays[i] = n == 1 ? L : L.Bay(L.W / n, uniform);
            return bays;
        }

        static readonly List<Opening> _holes = new List<Opening>(16);

        public static bool Tinted(ArchTex t) =>
            t == ArchTex.Louvre || t == ArchTex.PanelShutter || t == ArchTex.Folding || t == ArchTex.Door || t == ArchTex.Breeze ||
            t == ArchTex.Tiles || t == ArchTex.Zinc || t == ArchTex.PlanksH || t == ArchTex.PlanksV || t == ArchTex.Ornament ||
            t == ArchTex.Rail || t == ArchTex.Kerawang || t == ArchTex.Vent || t == ArchTex.Concrete || t == ArchTex.Brick || t == ArchTex.Attap;

        static float Rand(Lot L, float a, float b) => a + (float)L.rng.NextDouble() * (b - a);
        static T Pick<T>(Lot L, T[] from) => from[L.rng.Next(from.Length)];

        public static void Build(BuildingSpec s, ArchMesh m, bool detail)
        {
            var ring = s.ring;
            int n = ring.Length;
            int front = FrontEdge(s);
            Vector2 p0 = ring[front], p1 = ring[(front + 1) % n];
            var f = Frame.Edge(p0, p1, s.y0);
            var dir = (p1 - p0).normalized;
            var inward = new Vector2(-dir.y, dir.x);
            float W = (p1 - p0).magnitude, D = 0f;
            foreach (var p in ring) D = Mathf.Max(D, Vector2.Dot(p - p0, inward));

            var L = new Lot { rng = new System.Random(s.seed), detail = detail, W = W, D = Mathf.Max(D, 3f), h = s.height, lv = Mathf.Max(1, s.levels) };
            L.shop = s.kind == BuildingKind.Shop;
            double roll = L.rng.NextDouble();
            L.style = L.lv >= 5 ? ShopStyle.Modern : roll < 0.5 ? ShopStyle.PreWar : roll < 0.72 ? ShopStyle.Deco : ShopStyle.Modern;
            L.wall = s.colour > 0 ? Walls[(s.colour - 1) % Walls.Length] : Pick(L, Walls);
            L.wallDark = ArchMesh.Shade(L.wall, 0.86f);
            L.trim = L.rng.NextDouble() < 0.75 ? Trim : ArchMesh.Shade(L.wall, 1.12f);
            L.shutter = Pick(L, Shutters);
            L.tile = L.rng.NextDouble() < 0.82 ? TileRed : TileGreen;
            L.flat = L.style == ShopStyle.Modern || L.D > 30f || s.flatRoof;
            L.sign = L.rng.Next(16);
            L.plaque = L.rng.Next(8);
            L.shopBack = Pick(L, ShopBacks);
            L.shopBack2 = L.rng.NextDouble() < 0.6 ? L.shopBack : Pick(L, ShopBacks);
            Storeys(L.h, L.lv, out L.G, out L.S);
            L.A = HasArcade(s.kind, L.D, L.lv, L.G) ? ArcadeDepth : 0f;
            L.P = L.style == ShopStyle.PreWar ? 1.0f : L.style == ShopStyle.Deco ? 1.3f : 0.9f;

            // a column (and a pilaster above it) at each end of the front that isn't shared with the next lot
            // (the neighbour puts the one on a shared line)
            bool leftEnd = true;
            bool rightEnd = (s.Edge(front + 1) & EdgeFlags.Party) == 0;

            if (!detail) { Far(m, s, f, L, front, leftEnd, rightEnd); return; }
            var row = Bays(L);
            int bays = row.Length;
            float bw = W / bays;
            for (int i = 0; i < bays; i++)
            {
                var B = row[i];
                var fb = f.Shift(i * bw, 0f, 0f);
                bool re = i == bays - 1 && rightEnd;
                Ground(m, fb, B, true, re);
                for (int k = 1; k < B.lv; k++)
                {
                    float y0 = B.G + (k - 1) * B.S, y1 = y0 + B.S;
                    if (B.style == ShopStyle.Modern) UpperModern(m, fb, B, k, y0, y1);
                    else UpperClassic(m, fb, B, k, y0, y1);
                    if (k < B.lv - 1) m.Box(fb, -0.04f, bw + 0.04f, y1 - 0.1f, y1 + 0.12f, 0f, 0.14f, B.trim, Faces.Front | Faces.Top | Faces.Bottom | Faces.Left | Faces.Right);
                }
                if (B.lv >= 2) Pilasters(m, fb, B, true, re);
                Top(m, fb, B);
            }
            if (L.flat) FlatRoof(m, s, L);
            else TileRoof(m, f, L);
            OtherWalls(m, s, L, front);
        }

        public const float ArcadeDepth = 1.9f;

        static bool HasArcade(BuildingKind kind, float D, int lv, float G) => kind == BuildingKind.Shop && D >= 8f && lv >= 2 && G >= 3.6f;

        /// <summary>Ground-floor height and the height of each floor above it.</summary>
        static void Storeys(float h, int lv, out float G, out float S)
        {
            if (lv <= 1) { G = h; S = 0f; return; }
            G = Mathf.Clamp(h / lv * 1.2f, 3.8f, 4.4f);
            S = (h - G) / (lv - 1);
            if (S < 2.9f) { S = 2.9f; G = Mathf.Max(3.2f, h - S * (lv - 1)); }
        }

        /// <summary>Where a lot's five-foot way is: its front edge, its depth (0 = none) and the ground-floor height.</summary>
        public static bool Arcade(BuildingSpec s, out int front, out float A, out float G)
        {
            front = FrontEdge(s);
            var ring = s.ring;
            Vector2 p0 = ring[front], p1 = ring[(front + 1) % ring.Length];
            var dir = (p1 - p0).normalized;
            var inward = new Vector2(-dir.y, dir.x);
            float D = 0f;
            foreach (var p in ring) D = Mathf.Max(D, Vector2.Dot(p - p0, inward));
            int lv = Mathf.Max(1, s.levels);
            Storeys(s.height, lv, out G, out _);
            A = HasArcade(s.kind, Mathf.Max(D, 3f), lv, G) ? ArcadeDepth : 0f;
            return true;
        }

        /// <summary>The longest edge on a street (else the longest that isn't a party wall, else the longest).</summary>
        public static int FrontEdge(BuildingSpec s)
        {
            var ring = s.ring;
            int n = ring.Length, front = -1;
            for (int pass = 0; pass < 3 && front < 0; pass++)
            {
                float best = 0f;
                for (int i = 0; i < n; i++)
                {
                    var e = s.Edge(i);
                    if (pass == 0 && (e & (EdgeFlags.Street | EdgeFlags.Walkway)) == 0) continue;
                    if (pass == 1 && (e & EdgeFlags.Party) != 0) continue;
                    float len = (ring[(i + 1) % n] - ring[i]).magnitude;
                    if (len > best) { best = len; front = i; }
                }
            }
            return Mathf.Max(0, front);
        }

        /// <summary>
        /// The distance version (about 60 triangles): the same massing, five-foot way, signboard, parapet, pediment and
        /// roof, but each floor's windows painted on one wall.
        /// </summary>
        static void Far(ArchMesh m, BuildingSpec s, in Frame f, Lot L, int front, bool leftEnd, bool rightEnd)
        {
            float W = L.W, G = L.G, A = L.A, h = L.h;
            var row = Bays(L);
            int bays = row.Length;
            float bw = W / bays;
            for (int i = 0; i < bays; i++)
            {
                var B = row[i];
                var fb = f.Shift(i * bw, 0f, 0f);
                if (B.shop) m.Rect(fb, 0f, bw, 0f, G, -A, White, ArchTex.ShopFar);
                else m.Quad(fb.P(0f, 0f, 0f), fb.P(bw, 0f, 0f), fb.P(bw, G, 0f), fb.P(0f, G, 0f), B.wall, ArchTex.WinFar, 0f, 0f, Mathf.Max(1f, Mathf.Round(bw / 3f)), 1f);
                if (A > 0f)
                {
                    var cf = Faces.Front | Faces.Left | Faces.Right;
                    m.Box(fb, 0f, ColW, 0f, G, -ColW, 0f, B.wall, cf);
                    if (i == bays - 1 && rightEnd) m.Box(fb, bw - ColW, bw, 0f, G, -ColW, 0f, B.wall, cf);
                }
                if (B.lv >= 2)
                {
                    int nw = bw < 4.2f ? 2 : bw < 6.6f ? 3 : 4;
                    bool modern = B.style == ShopStyle.Modern;
                    m.Quad(fb.P(0f, G, 0f), fb.P(bw, G, 0f), fb.P(bw, h, 0f), fb.P(0f, h, 0f), B.wall, modern ? ArchTex.RibbonFar : ArchTex.WinFar,
                           0f, 0f, modern ? Mathf.Max(1f, Mathf.Round(bw / 3f)) : nw, B.lv - 1);
                    if (B.shop) Sign(m, fb, B, false);
                }
                m.Box(fb, 0f, bw, h + 0.22f, h + B.P, -0.25f, 0f, B.wall, Faces.Front | Faces.Top | Faces.Back);
                float pw = Mathf.Min(bw * 0.72f, 3.4f), cx = bw * 0.5f, y = h + B.P;
                if (B.style == ShopStyle.PreWar)
                    m.Extrude(fb, new[] { new Vector2(cx - pw / 2, y), new Vector2(cx + pw / 2, y), new Vector2(cx + pw * 0.2f, y + 0.9f), new Vector2(cx, y + 1.1f),
                                          new Vector2(cx - pw * 0.2f, y + 0.9f) }, 0f, 0f, B.wall, ArchTex.White);
                else if (B.style == ShopStyle.Deco)
                    m.Box(fb, cx - pw * 0.36f, cx + pw * 0.36f, y, y + 0.9f, -0.25f, 0f, B.wall, Faces.Front | Faces.Top);
            }
            if (A > 0f) m.Box(f, 0f, W, G - 0.14f, G, -A, 0f, Soffit, Faces.Bottom);
            m.Box(f, -0.06f, W + 0.06f, h - 0.12f, h + 0.22f, 0f, 0.2f, L.trim, Faces.Front | Faces.Top);
            if (L.flat) m.Cap(s.ring, s.y0 + h + 0.05f, Concrete);
            else TileRoof(m, f, L);
            OtherWalls(m, s, L, front);
        }

        // ---------------------------------------------------------------------------------------- ground floor
        static void Ground(ArchMesh m, in Frame f, Lot L, bool leftEnd, bool rightEnd)
        {
            float W = L.W, G = L.G, A = L.A;
            _holes.Clear();
            if (L.shop)
            {
                int nOpen = W > 5.6f ? 2 : 1;
                float pier = 0.32f, ow = (W - pier * (nOpen + 1)) / nOpen;
                float doorTop = Mathf.Min(3.0f, G - 0.9f);
                for (int k = 0; k < nOpen; k++)
                {
                    float x0 = pier + k * (ow + pier);
                    var back = k == 0 ? L.shopBack : L.shopBack2;
                    _holes.Add(new Opening(x0, x0 + ow, 0f, doorTop, L.detail ? 0.15f : 0f, back, Tinted(back) ? L.shutter : White, Reveal));
                    if (L.style != ShopStyle.Modern || k == 0)
                        _holes.Add(new Opening(x0 + 0.12f, x0 + ow - 0.12f, doorTop + 0.22f, G - 0.5f, L.detail ? 0.08f : 0f, ArchTex.Breeze, L.trim, Reveal));
                }
            }
            else
            {
                // a house front: a door and a window
                float dw = 1.0f, dx = W * 0.25f - dw * 0.5f;
                _holes.Add(new Opening(dx, dx + dw, 0f, 2.2f, L.detail ? 0.12f : 0f, ArchTex.Door, L.shutter, Reveal));
                if (W > 3.2f) _holes.Add(new Opening(W * 0.55f, W - 0.5f, 0.9f, 2.2f, L.detail ? 0.15f : 0f, ArchTex.Grille, White, Reveal));
            }
            m.Panel(f, 0f, W, 0f, G, -A, L.wall, ArchTex.White, 0f, _holes);
            if (A <= 0f) return;

            // the five-foot way: the floor above's underside, its tiled floor, the beam and the columns
            m.Box(f, 0f, W, G - 0.14f, G, -A, 0f, Soffit, Faces.Bottom);
            m.QuadFacing(f.P(0f, 0.02f, -A), f.P(W, 0.02f, -A), f.P(W, 0.02f, 0f), f.P(0f, 0.02f, 0f), Vector3.up, White, ArchTex.Steps,
                         0f, 0f, W / 1.2f, A / 1.2f);
            m.Box(f, 0f, W, G - 0.55f, G, -0.3f, 0.04f, L.trim, Faces.Front | Faces.Bottom | Faces.Back);
            if (leftEnd) Column(m, f, 0f, L);
            if (rightEnd) Column(m, f, W - ColW, L);
            if (L.detail && L.rng.NextDouble() < 0.5)
            {
                // a red lantern or two under the five-foot way
                for (int k = 0; k < (W > 5f ? 2 : 1); k++)
                {
                    float x = W * (k + 1) / (W > 5f ? 3f : 2f);
                    m.Box(f, x - 0.22f, x + 0.22f, G - 1.25f, G - 0.75f, -A * 0.5f - 0.22f, -A * 0.5f + 0.22f, ArchMesh.Col(0.86f, 0.12f, 0.12f));
                    m.Box(f, x - 0.25f, x + 0.25f, G - 0.8f, G - 0.72f, -A * 0.5f - 0.25f, -A * 0.5f + 0.25f, ArchMesh.Col(0.95f, 0.78f, 0.25f));
                }
            }
        }

        static void Column(ArchMesh m, in Frame f, float x0, Lot L)
        {
            float x1 = x0 + ColW;
            if (L.detail)
            {
                m.Box(f, x0 - 0.05f, x1 + 0.05f, 0f, 0.45f, -ColW - 0.05f, 0.05f, L.trim, Faces.All & ~Faces.Bottom);
                m.Box(f, x0, x1, 0.45f, L.G - 0.75f, -ColW, 0f, L.wall, Faces.Front | Faces.Back | Faces.Left | Faces.Right);
                m.Box(f, x0 - 0.07f, x1 + 0.07f, L.G - 0.75f, L.G - 0.55f, -ColW - 0.07f, 0.07f, L.trim);
            }
            else m.Box(f, x0, x1, 0f, L.G - 0.55f, -ColW, 0f, L.wall, Faces.Front | Faces.Back | Faces.Left | Faces.Right);
        }

        // ---------------------------------------------------------------------------------------- upper floors
        static void UpperClassic(ArchMesh m, in Frame f, Lot L, int k, float y0, float y1)
        {
            _holes.Clear();
            int nw = L.W < 4.2f ? 2 : L.W < 6.6f ? 3 : 4;
            float storey = y1 - y0;
            float wh = Mathf.Min(L.style == ShopStyle.Deco ? 1.7f : 2.05f, storey - 1.0f);
            float ww = Mathf.Min(L.style == ShopStyle.Deco ? 1.25f : 0.95f, (L.W - 0.9f) / nw - 0.3f);
            float sill = y0 + Mathf.Max(k == 1 && L.shop ? 1.0f : 0.6f, (storey - wh) * 0.62f);
            if (sill + wh > y1 - 0.3f) wh = y1 - 0.3f - sill;
            float gap = (L.W - nw * ww) / (nw + 1);
            for (int i = 0; i < nw; i++)
            {
                float x0 = gap + i * (ww + gap);
                double r = L.rng.NextDouble();
                var back = L.style == ShopStyle.Deco ? (r < 0.5 ? ArchTex.Grille : r < 0.8 ? ArchTex.Glass : ArchTex.Curtained)
                                                       : (r < 0.55 ? ArchTex.Louvre : r < 0.75 ? ArchTex.PanelShutter : r < 0.9 ? ArchTex.Curtained : ArchTex.Glass);
                var col = Tinted(back) ? L.shutter : back == ArchTex.Glass ? Glass : White;
                _holes.Add(new Opening(x0, x0 + ww, sill, sill + wh, L.detail ? 0.2f : 0f, back, col, Reveal));
            }
            m.Panel(f, 0f, L.W, y0, y1, 0f, L.wall, ArchTex.White, 0f, _holes);
            if (!L.detail) return;
            foreach (var h in _holes)
            {
                if (L.style == ShopStyle.PreWar)
                {
                    Architrave(m, f, h.x0, h.x1, h.y0, h.y1, 0.1f, 0.06f, L.trim);
                    m.Box(f, h.x0 - 0.14f, h.x1 + 0.14f, h.y0 - 0.12f, h.y0, 0f, 0.15f, L.trim);                 // sill
                    m.Box(f, h.x0 - 0.18f, h.x1 + 0.18f, h.y1 + 0.1f, h.y1 + 0.26f, 0f, 0.2f, L.trim);            // hood
                    if (L.rng.NextDouble() < 0.3)                                                                    // a vent over it
                        m.Rect(f, h.x0 + 0.1f, h.x1 - 0.1f, h.y1 + 0.32f, Mathf.Min(y1 - 0.2f, h.y1 + 0.62f), 0.01f, L.trim, ArchTex.Breeze);
                }
                else
                {
                    // Art Deco: a concrete sun-hood standing well out, thin fins down the sides
                    m.Box(f, h.x0 - 0.25f, h.x1 + 0.25f, h.y1 + 0.08f, h.y1 + 0.2f, 0f, 0.5f, L.trim);
                    m.Box(f, h.x0 - 0.12f, h.x0 - 0.04f, h.y0 - 0.1f, h.y1 + 0.08f, 0f, 0.32f, L.trim);
                    m.Box(f, h.x1 + 0.04f, h.x1 + 0.12f, h.y0 - 0.1f, h.y1 + 0.08f, 0f, 0.32f, L.trim);
                    m.Box(f, h.x0 - 0.12f, h.x1 + 0.12f, h.y0 - 0.12f, h.y0, 0f, 0.18f, L.trim);
                }
            }
            // now and then an air-con unit hung beside a window
            if (L.rng.NextDouble() < 0.35)
            {
                var h = _holes[L.rng.Next(_holes.Count)];
                float x = h.x1 + 0.2f < L.W - 1.0f ? h.x1 + 0.2f : h.x0 - 1.0f;
                AcUnit(m, f, x, h.y0 - 0.1f);
            }
        }

        static void UpperModern(ArchMesh m, in Frame f, Lot L, int k, float y0, float y1)
        {
            _holes.Clear();
            float storey = y1 - y0;
            float sill = y0 + (k == 1 && L.shop ? 1.1f : 0.9f), top = Mathf.Min(y1 - 0.35f, sill + 1.5f);
            bool grille = L.rng.NextDouble() < 0.6;
            var back = grille ? ArchTex.Grille : ArchTex.Glass;
            _holes.Add(new Opening(0.45f, L.W - 0.45f, sill, top, L.detail ? 0.12f : 0f, back, grille ? White : Glass, Metal));
            m.Panel(f, 0f, L.W, y0, y1, 0f, L.wall, ArchTex.White, 0f, _holes);
            if (!L.detail) return;
            var hh = _holes[0];
            // aluminium frame and mullions, a slim sill
            Architrave(m, f, hh.x0, hh.x1, hh.y0, hh.y1, 0.06f, 0.03f, Metal);
            int mull = Mathf.Max(1, Mathf.RoundToInt((hh.x1 - hh.x0) / 1.1f));
            for (int i = 1; i < mull; i++)
            {
                float x = Mathf.Lerp(hh.x0, hh.x1, i / (float)mull);
                m.Box(f, x - 0.025f, x + 0.025f, hh.y0, hh.y1, -0.12f, -0.08f, Metal, Faces.Front | Faces.Left | Faces.Right);
            }
            m.Box(f, hh.x0 - 0.06f, hh.x1 + 0.06f, hh.y0 - 0.08f, hh.y0, 0f, 0.1f, L.trim);
            // a band of ventilation blocks or tiles under the window, an air-con unit hung on it
            if (L.rng.NextDouble() < 0.5) m.Rect(f, 0.45f, L.W - 0.45f, y0 + 0.15f, sill - 0.2f, 0.01f, L.trim, ArchTex.Breeze, 0.6f);
            if (L.rng.NextDouble() < 0.7) AcUnit(m, f, L.rng.NextDouble() < 0.5 ? 0.6f : L.W - 1.6f, y0 + 0.2f);
        }

        static void Architrave(ArchMesh m, in Frame f, float x0, float x1, float y0, float y1, float w, float d, Color32 c)
        {
            var faces = Faces.Front | Faces.Left | Faces.Right | Faces.Top | Faces.Bottom;
            m.Box(f, x0 - w, x0, y0 - w, y1 + w, 0f, d, c, faces);
            m.Box(f, x1, x1 + w, y0 - w, y1 + w, 0f, d, c, faces);
            m.Box(f, x0, x1, y1, y1 + w, 0f, d, c, faces);
            m.Box(f, x0, x1, y0 - w, y0, 0f, d, c, faces);
        }

        static void AcUnit(ArchMesh m, in Frame f, float x, float y)
        {
            m.Box(f, x, x + 0.95f, y - 0.65f, y, 0.06f, 0.4f, ArchMesh.Col(0.93f, 0.93f, 0.92f), Faces.All & ~Faces.Front & ~Faces.Back);
            m.Rect(f, x, x + 0.95f, y - 0.65f, y, 0.4f, White, ArchTex.AC);
            m.Box(f, x + 0.1f, x + 0.16f, y - 0.75f, y - 0.65f, 0f, 0.3f, Metal, Faces.Front | Faces.Left | Faces.Right);
            m.Box(f, x + 0.79f, x + 0.85f, y - 0.75f, y - 0.65f, 0f, 0.3f, Metal, Faces.Front | Faces.Left | Faces.Right);
        }

        static void Pilasters(ArchMesh m, in Frame f, Lot L, bool leftEnd, bool rightEnd)
        {
            if (!L.detail) return;
            var faces = Faces.Front | Faces.Left | Faces.Right;
            if (leftEnd) m.Box(f, 0f, 0.32f, L.G, L.h, 0f, 0.1f, L.trim, faces);
            if (rightEnd) m.Box(f, L.W - 0.32f, L.W, L.G, L.h, 0f, 0.1f, L.trim, faces);
        }

        // ---------------------------------------------------------------------------------------- top
        static void Top(ArchMesh m, in Frame f, Lot L)
        {
            float W = L.W, h = L.h, P = L.P;
            if (L.lv >= 2 && L.shop) Sign(m, f, L, true);
            // main cornice and parapet with its coping
            m.Box(f, -0.06f, W + 0.06f, h - 0.12f, h + 0.22f, 0f, L.detail ? 0.32f : 0.2f, L.trim, Faces.Front | Faces.Top | Faces.Bottom | Faces.Left | Faces.Right);
            m.Box(f, 0f, W, h + 0.22f, h + P, -0.25f, 0f, L.wall, Faces.Front | Faces.Back | Faces.Left | Faces.Right);
            m.Box(f, -0.04f, W + 0.04f, h + P, h + P + 0.12f, -0.3f, 0.06f, L.trim);
            float pw = Mathf.Min(W * 0.72f, 3.4f), cx = W * 0.5f, y = h + P + 0.12f;
            if (L.style == ShopStyle.PreWar)
            {
                // a curved (Dutch) gable over the middle of the parapet, its year in a plaque
                var outline = new[]
                {
                    new Vector2(cx - pw / 2, y - 0.6f), new Vector2(cx + pw / 2, y - 0.6f), new Vector2(cx + pw / 2, y + 0.15f),
                    new Vector2(cx + pw * 0.34f, y + 0.4f), new Vector2(cx + pw * 0.2f, y + 0.9f), new Vector2(cx, y + 1.1f),
                    new Vector2(cx - pw * 0.2f, y + 0.9f), new Vector2(cx - pw * 0.34f, y + 0.4f), new Vector2(cx - pw / 2, y + 0.15f),
                };
                m.Extrude(f, outline, -0.25f, 0.02f, L.wall, ArchTex.White, 0f, L.trim);
                PlaqueOrOrnament(m, f, L, cx, y + 0.1f, Mathf.Min(1.3f, pw * 0.5f));
            }
            else if (L.style == ShopStyle.Deco)
            {
                // stepped parapet, a flagpole, the year in relief
                for (int st = 0; st < 3; st++)
                {
                    float w = pw * (1f - st * 0.28f), yb = y - 0.12f + st * 0.42f;
                    m.Box(f, cx - w / 2, cx + w / 2, yb, yb + 0.42f, -0.25f, 0.04f + st * 0.02f, L.wall, Faces.All & ~Faces.Bottom);
                    m.Box(f, cx - w / 2 - 0.03f, cx + w / 2 + 0.03f, yb + 0.36f, yb + 0.44f, -0.28f, 0.08f + st * 0.02f, L.trim);
                }
                PlaqueOrOrnament(m, f, L, cx, y, Mathf.Min(1.2f, pw * 0.45f));
                if (L.detail) m.Box(f, cx - 0.04f, cx + 0.04f, y + 1.3f, y + 3.2f, -0.1f, -0.02f, Metal);
                if (L.detail)
                    for (int side = 0; side < 2; side++)
                    {
                        float x = side == 0 ? 0.1f : W - 0.3f;
                        m.Box(f, x, x + 0.2f, L.G + 0.3f, y + 0.6f, 0f, 0.36f, L.trim);       // a fin up each side
                    }
            }
            else if (L.rng.NextDouble() < 0.25 && L.detail)
            {
                // a 1970s lot with a rooftop billboard
                float bw = Mathf.Min(W - 0.4f, 4.5f);
                var tex = L.rng.NextDouble() < 0.5 ? ArchTex.Billboard0 : ArchTex.Billboard1;
                m.Box(f, cx - 0.06f - bw * 0.3f, cx + 0.06f - bw * 0.3f, y, y + 1.4f, -1.2f, -1.0f, Metal);
                m.Box(f, cx - 0.06f + bw * 0.3f, cx + 0.06f + bw * 0.3f, y, y + 1.4f, -1.2f, -1.0f, Metal);
                m.Box(f, cx - bw / 2, cx + bw / 2, y + 1.4f, y + 1.4f + bw * 0.45f, -1.0f, -0.92f, Dark, Faces.All & ~Faces.Front);
                m.Rect(f, cx - bw / 2, cx + bw / 2, y + 1.4f, y + 1.4f + bw * 0.45f, -0.92f, White, tex);
            }
        }

        /// <summary>The signboard along the floor line over the five-foot way.</summary>
        static void Sign(ArchMesh m, in Frame f, Lot L, bool sides)
        {
            float sy0 = L.G + 0.06f, sy1 = L.G + 0.86f, x0 = 0.3f, x1 = L.W - 0.3f;
            int s = L.sign, cell = s / 4, row = s % 4;
            float v0 = 1f - (row + 1) / 4f, v1 = 1f - row / 4f;
            var tex = (ArchTex)((int)ArchTex.Signs0 + cell);
            if (sides) m.Box(f, x0, x1, sy0, sy1, 0f, 0.12f, SignSide, Faces.Left | Faces.Right | Faces.Top | Faces.Bottom);
            m.Quad(f.P(x0, sy0, 0.12f), f.P(x1, sy0, 0.12f), f.P(x1, sy1, 0.12f), f.P(x0, sy1, 0.12f), White, tex, 0f, v0, 1f, v1);
        }

        static void PlaqueOrOrnament(ArchMesh m, in Frame f, Lot L, float cx, float y, float w)
        {
            float hgt = w * 0.42f;
            if (L.rng.NextDouble() < 0.65)
            {
                int col = L.plaque % 2, row = L.plaque / 2;
                m.Quad(f.P(cx - w / 2, y, 0.05f), f.P(cx + w / 2, y, 0.05f), f.P(cx + w / 2, y + hgt, 0.05f), f.P(cx - w / 2, y + hgt, 0.05f),
                       L.trim, ArchTex.Plaques, col * 0.5f, 1f - (row + 1) / 4f, col * 0.5f + 0.5f, 1f - row / 4f);
            }
            else m.Rect(f, cx - w * 0.6f, cx + w * 0.6f, y - 0.1f, y + w * 0.62f, 0.05f, L.trim, ArchTex.Ornament);
        }

        // ---------------------------------------------------------------------------------------- roofs
        /// <summary>A clay-tile roof pitched front and back over the lot, fire walls on the party lines.</summary>
        static void TileRoof(ArchMesh m, in Frame f, Lot L)
        {
            float W = L.W, D = L.D, ye = L.h + 0.15f;
            float rise = Mathf.Clamp(D * 0.2f, 1.1f, 3.0f);
            float zf = -0.28f, zb = -D - 0.45f, zm = (zf - D) * 0.5f, yr = ye + rise;
            var up = f.u;
            float slopeF = Mathf.Sqrt(rise * rise + (zf - zm) * (zf - zm)), slopeB = Mathf.Sqrt((rise + 0.2f) * (rise + 0.2f) + (zm - zb) * (zm - zb));
            m.QuadFacing(f.P(-0.05f, ye, zf), f.P(W + 0.05f, ye, zf), f.P(W + 0.05f, yr, zm), f.P(-0.05f, yr, zm), up + f.n, L.tile, ArchTex.Tiles,
                         0f, 0f, W / 1.2f, slopeF / 1.2f);
            m.QuadFacing(f.P(W + 0.05f, ye - 0.2f, zb), f.P(-0.05f, ye - 0.2f, zb), f.P(-0.05f, yr, zm), f.P(W + 0.05f, yr, zm), up - f.n, L.tile, ArchTex.Tiles,
                         0f, 0f, W / 1.2f, slopeB / 1.2f);
            // the ridge
            if (L.detail) m.Box(f, -0.08f, W + 0.08f, yr - 0.08f, yr + 0.16f, zm - 0.14f, zm + 0.14f, TileDark, Faces.All & ~Faces.Bottom);
            // gable ends (fire walls a hand above the tiles)
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? 0f : W;
                var want = side == 0 ? -f.r : f.r;
                m.TriFacing(f.P(x, ye - 0.2f, zb), f.P(x, ye, zf), f.P(x, yr + 0.18f, zm), want, L.wallDark, ArchTex.White, Vector2.zero, Vector2.zero, Vector2.zero);
                if (L.detail)
                {
                    // coping along the top of the fire wall
                    var a = f.P(x, ye + 0.05f, zf); var b = f.P(x, yr + 0.25f, zm); var c = f.P(x, ye - 0.15f, zb);
                    Coping(m, a, b, f.r, L.trim);
                    Coping(m, b, c, f.r, L.trim);
                }
            }
            // the back wall's top up to the eaves is the other walls' job
        }

        static void Coping(ArchMesh m, Vector3 a, Vector3 b, Vector3 side, Color32 c)
        {
            var up = Vector3.up * 0.12f;
            var s = side * 0.14f;
            m.QuadFacing(a - s + up, b - s + up, b + s + up, a + s + up, Vector3.up, c, ArchTex.White);
            m.QuadFacing(a + s, b + s, b + s + up, a + s + up, side, c, ArchTex.White);
            m.QuadFacing(a - s, b - s, b - s + up, a - s + up, -side, c, ArchTex.White);
        }

        /// <summary>A flat roof: the slab, a low parapet round it, a water tank on legs and the air-con condensers.</summary>
        static void FlatRoof(ArchMesh m, BuildingSpec s, Lot L)
        {
            float y = s.y0 + L.h + 0.05f;
            m.Cap(s.ring, y, Concrete);
            var ring = s.ring;
            int n = ring.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = ring[i], b = ring[(i + 1) % n];
                float len = (b - a).magnitude;
                if (len < 0.3f || i == FrontEdge(s)) continue;
                var fe = Frame.Edge(a, b, y);
                m.Box(fe, 0f, len, 0f, 0.7f, -0.2f, 0f, L.wall, Faces.Front | Faces.Back | Faces.Top);
            }
            if (!L.detail) return;
            var c = s.Centre();
            var rng = L.rng;
            // the tank: a blue (or grey) box on a little steel stand
            var tc = new Vector3(c.x + Rand(L, -1f, 1f), y, c.y + Rand(L, -1f, 1f));
            var tank = rng.NextDouble() < 0.6 ? ArchMesh.Col(0.24f, 0.46f, 0.74f) : ArchMesh.Col(0.82f, 0.82f, 0.8f);
            if (rng.NextDouble() < 0.6) m.Cylinder(tc + Vector3.up * 0.9f, 0.7f, 1.3f, 10, tank);
            else
            {
                var tf = new Frame(tc, Vector3.right, Vector3.up, Vector3.back);
                m.Box(tf, -0.9f, 0.9f, 0.9f, 2.1f, -0.7f, 0.7f, tank);
            }
            for (int k = 0; k < 4; k++)
            {
                var leg = tc + new Vector3(k < 2 ? -0.5f : 0.5f, 0f, k % 2 == 0 ? -0.5f : 0.5f);
                m.Cylinder(leg, 0.05f, 0.9f, 4, Metal, ArchTex.White, false);
            }
            // condensers
            int ac = rng.Next(1, 4);
            for (int k = 0; k < ac; k++)
            {
                var p = new Vector3(c.x + Rand(L, -2f, 2f), y, c.y + Rand(L, -2f, 2f));
                var af = new Frame(p, Vector3.right, Vector3.up, Vector3.back);
                m.Box(af, -0.45f, 0.45f, 0f, 0.62f, -0.18f, 0.18f, ArchMesh.Col(0.93f, 0.93f, 0.92f), Faces.All & ~Faces.Front & ~Faces.Bottom);
                m.Rect(af, -0.45f, 0.45f, 0f, 0.62f, 0.18f, White, ArchTex.AC);
            }
        }

        // ---------------------------------------------------------------------------------------- the rest
        static void OtherWalls(ArchMesh m, BuildingSpec s, Lot L, int front)
        {
            var ring = s.ring;
            int n = ring.Length;
            float top = L.h + (L.flat ? 0.05f : 0.15f);
            Vector2 f0 = ring[front], f1 = ring[(front + 1) % n];
            var fdir = (f1 - f0).normalized;
            var inward = new Vector2(-fdir.y, fdir.x);
            for (int i = 0; i < n; i++)
            {
                if (i == front) continue;
                Vector2 a = ring[i], b = ring[(i + 1) % n];
                float len = (b - a).magnitude;
                if (len < 0.05f) continue;
                var fe = Frame.Edge(a, b, s.y0);
                var e = s.Edge(i);
                // the five-foot way runs on through the lot's sides: no wall across it under the floor above
                float c0 = 0f, c1 = 0f;
                if (L.A > 0f)
                {
                    var d = (b - a) / len;
                    if (i == (front + 1) % n) { c0 = 0f; c1 = Mathf.Min(len, L.A / Mathf.Max(0.2f, Vector2.Dot(d, inward))); }
                    else if (i == (front + n - 1) % n) { c1 = len; c0 = Mathf.Max(0f, len - L.A / Mathf.Max(0.2f, -Vector2.Dot(d, inward))); }
                }
                if (c1 > c0)
                {
                    m.Rect(fe, c0, c1, L.G, top, 0f, L.wallDark, ArchTex.White);
                    if (c0 <= 0.01f && c1 >= len - 0.01f) continue;
                    // the rest of the wall, beyond the five-foot way
                    float r0 = c0 <= 0.01f ? c1 : 0f, r1 = c0 <= 0.01f ? len : c0;
                    m.Rect(fe, r0, r1, 0f, top, 0f, L.wallDark, ArchTex.White);
                    continue;
                }
                if ((e & EdgeFlags.Party) != 0 || len < 2.4f || !L.detail)
                {
                    m.Rect(fe, 0f, len, 0f, top, 0f, L.wallDark, ArchTex.White);
                    continue;
                }
                // a back or side wall: small windows on each floor, a back door, pipes
                _holes.Clear();
                int nw = Mathf.Clamp(Mathf.FloorToInt(len / 2.8f), 1, 12);
                float gap = len / nw;
                for (int k = 0; k < L.lv; k++)
                {
                    float y0 = k == 0 ? 0f : L.G + (k - 1) * L.S, st = k == 0 ? L.G : L.S;
                    for (int w = 0; w < nw; w++)
                    {
                        float cx = gap * (w + 0.5f);
                        if (k == 0 && w == 0 && (e & EdgeFlags.Street) == 0)
                        {
                            _holes.Add(new Opening(cx - 0.5f, cx + 0.5f, 0f, 2.2f, L.detail ? 0.1f : 0f, ArchTex.Door, L.shutter, Reveal));
                            continue;
                        }
                        float wy = y0 + st * 0.42f, wh = Mathf.Min(1.2f, st * 0.4f);
                        var back = (k + w) % 3 == 0 ? ArchTex.Grille : (k + w) % 3 == 1 ? ArchTex.Louvre : ArchTex.Glass;
                        _holes.Add(new Opening(cx - 0.45f, cx + 0.45f, wy, wy + wh, L.detail ? 0.1f : 0f, back,
                                               back == ArchTex.Louvre ? L.shutter : back == ArchTex.Glass ? Glass : White, Reveal));
                    }
                }
                m.Panel(fe, 0f, len, 0f, top, 0f, L.wallDark, ArchTex.White, 0f, _holes);
                if (L.detail && len > 3f)
                    m.Box(fe, len - 0.4f, len - 0.28f, 0f, top, 0f, 0.12f, Metal, Faces.Front | Faces.Left | Faces.Right);   // a down pipe
            }
        }
    }
}
