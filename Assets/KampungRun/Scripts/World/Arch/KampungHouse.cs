using System.Collections.Generic;
using UnityEngine;

namespace KampungRun.Arch
{
    /// <summary>A kampung house to model (its local front is +z, turned by yaw; the pivot is the middle of the house).</summary>
    public class HouseSpec : IArchItem
    {
        public Vector3 pos;
        public float yaw;
        public float width = 8f;
        public int seed;
        /// <summary>Wall colour (KampungHouse.Walls index; -1 = by the seed). Pak Mat's is teal.</summary>
        public int wall = -1;

        public Vector2 Where() => new Vector2(pos.x, pos.z);
        public void Build(ArchMesh m, bool detail) => KampungHouse.Build(this, m, detail);
    }

    /// <summary>
    /// A rumah kampung, Kampung Baru style. Raised on timber posts over concrete footings, planked walls in a
    /// pastel, tall louvred windows under carved kerawang vents, a front verandah with a balustrade, the tiled
    /// concrete stair every Malay house has, and a steep zinc roof gabled to the front with its decorated gable
    /// board (tebar layar), white fascia and crossed finial; a lower kitchen wing behind on some. The body runs
    /// z = -3.4..1.6, the verandah to 3.6 and the stair out to about 5.4 (where the old kit house had its own).
    /// </summary>
    public static class KampungHouse
    {
        static readonly Color32[] Walls =
        {
            ArchMesh.Col(0.36f, 0.70f, 0.66f), ArchMesh.Col(0.62f, 0.84f, 0.70f), ArchMesh.Col(0.50f, 0.68f, 0.86f), ArchMesh.Col(0.96f, 0.86f, 0.52f),
            ArchMesh.Col(0.94f, 0.70f, 0.72f), ArchMesh.Col(0.98f, 0.94f, 0.84f), ArchMesh.Col(0.74f, 0.56f, 0.40f),
        };
        static readonly Color32[] Trims = { ArchMesh.Col(0.97f, 0.97f, 0.94f), ArchMesh.Col(0.98f, 0.92f, 0.60f), ArchMesh.Col(0.86f, 0.30f, 0.26f) };
        static readonly Color32[] Shutters = { ArchMesh.Col(0.18f, 0.45f, 0.40f), ArchMesh.Col(0.55f, 0.34f, 0.18f), ArchMesh.Col(0.20f, 0.36f, 0.62f), ArchMesh.Col(0.96f, 0.96f, 0.92f) };
        static readonly Color32[] Roofs = { ArchMesh.Col(0.78f, 0.30f, 0.24f), ArchMesh.Col(0.64f, 0.66f, 0.70f), ArchMesh.Col(0.30f, 0.46f, 0.66f), ArchMesh.Col(0.36f, 0.58f, 0.40f) };
        static readonly Color32 Timber = ArchMesh.Col(0.46f, 0.32f, 0.22f), Footing = ArchMesh.Col(0.78f, 0.77f, 0.74f);
        static readonly Color32 White = ArchMesh.Col(1f, 1f, 1f), Reveal = ArchMesh.Col(0.40f, 0.30f, 0.22f), Soffit = ArchMesh.Col(0.62f, 0.52f, 0.40f);

        const float Stilt = 1.3f, FloorT = 0.2f, WallH = 2.8f, BodyZ0 = -3.4f, BodyZ1 = 1.6f, VerZ1 = 3.6f;
        static readonly List<Opening> _holes = new List<Opening>(8);

        public static void Build(HouseSpec h, ArchMesh m, bool detail)
        {
            var rng = new System.Random(h.seed);
            var wall = Walls[rng.Next(Walls.Length)];
            if (h.wall >= 0) wall = Walls[h.wall % Walls.Length];
            var trim = Trims[rng.Next(Trims.Length)];
            var shutter = Shutters[rng.Next(Shutters.Length)];
            var roof = Roofs[rng.Next(Roofs.Length)];
            bool kitchen = rng.NextDouble() < 0.6;
            float w = h.width, y1 = Stilt + FloorT, y2 = y1 + WallH;
            var rot = Quaternion.Euler(0f, h.yaw, 0f);
            // the house's own frame: x across, z toward the front, origin on the ground in the middle
            var F = new Frame(h.pos, rot * Vector3.right, Vector3.up, rot * Vector3.forward);
            // a frame on each face of the body (x along the face, left to right seen from outside; z out of it)
            Frame Face(float x0, float z0, float x1, float z1, float y) =>
                new Frame(F.P(x0, y, z0), (F.P(x1, y, z1) - F.P(x0, y, z0)).normalized, Vector3.up,
                          Vector3.Cross(Vector3.up, (F.P(x1, y, z1) - F.P(x0, y, z0)).normalized));

            // ---- stilts, the deck
            if (detail)
                foreach (float x in new[] { -w / 2 + 0.3f, 0f, w / 2 - 0.3f })
                    foreach (float z in new[] { BodyZ0 + 0.3f, (BodyZ0 + BodyZ1) / 2f, BodyZ1, VerZ1 - 0.3f })
                    {
                        Post(m, F, x, z, 0f, 0.16f, 0.5f, Footing);
                        Post(m, F, x, z, 0.16f, Stilt, 0.2f, Timber);
                    }
            else m.Box(F, -w / 2 + 0.3f, w / 2 - 0.3f, 0f, Stilt, BodyZ0 + 0.3f, VerZ1 - 0.3f, ArchMesh.Shade(Timber, 0.6f), Faces.Front | Faces.Back | Faces.Left | Faces.Right);
            m.Box(F, -w / 2, w / 2, Stilt, y1, BodyZ0, VerZ1, Timber, ArchTex.White, Faces.All & ~Faces.Top);
            m.QuadFacing(F.P(-w / 2, y1, VerZ1), F.P(w / 2, y1, VerZ1), F.P(w / 2, y1, BodyZ0), F.P(-w / 2, y1, BodyZ0), Vector3.up,
                         ArchMesh.Col(0.72f, 0.56f, 0.40f), ArchTex.PlanksV, 0f, 0f, w / 1.2f, (VerZ1 - BodyZ0) / 1.2f);

            // ---- the body: planked walls, windows under carved vents, the front door
            float bx0 = -w / 2 + 0.2f, bx1 = w / 2 - 0.2f;
            Wall(m, Face(bx1, BodyZ1, bx0, BodyZ1, y1), bx1 - bx0, WallH, wall, trim, shutter, detail, true, rng);           // front
            Wall(m, Face(bx1, BodyZ0, bx1, BodyZ1, y1), BodyZ1 - BodyZ0, WallH, wall, trim, shutter, detail, false, rng);    // right
            Wall(m, Face(bx0, BodyZ0, bx1, BodyZ0, y1), bx1 - bx0, WallH, wall, trim, shutter, detail, false, rng);          // back
            Wall(m, Face(bx0, BodyZ1, bx0, BodyZ0, y1), BodyZ1 - BodyZ0, WallH, wall, trim, shutter, detail, false, rng);    // left
            if (detail)
                foreach (var (x, z) in new[] { (bx0, BodyZ0), (bx1, BodyZ0), (bx0, BodyZ1), (bx1, BodyZ1) })
                    Post(m, F, x, z, y1, y2 + 0.05f, 0.18f, trim);

            // ---- the verandah: balustrade with a gap for the stair, posts up to the eaves
            float gap = 0.75f;
            if (detail)
            {
                Rail(m, F, -w / 2 + 0.1f, VerZ1 - 0.1f, -gap, VerZ1 - 0.1f, y1, trim);
                Rail(m, F, gap, VerZ1 - 0.1f, w / 2 - 0.1f, VerZ1 - 0.1f, y1, trim);
                Rail(m, F, -w / 2 + 0.1f, BodyZ1, -w / 2 + 0.1f, VerZ1 - 0.1f, y1, trim);
                Rail(m, F, w / 2 - 0.1f, VerZ1 - 0.1f, w / 2 - 0.1f, BodyZ1, y1, trim);
                foreach (float x in new[] { -w / 2 + 0.1f, -gap, gap, w / 2 - 0.1f }) Post(m, F, x, VerZ1 - 0.1f, y1, y2, 0.14f, trim);
            }
            else m.Box(F, -w / 2, w / 2, y1, y1 + 0.9f, VerZ1 - 0.15f, VerZ1 - 0.05f, trim, ArchTex.Rail, Faces.Front | Faces.Back | Faces.Top);

            // ---- the stair: tiled risers between low concrete cheeks
            const int steps = 5;
            float rise = y1 / (steps + 1), run = 0.34f;
            var tread = ArchMesh.Col(0.86f, 0.84f, 0.8f);
            if (detail)
            {
                for (int k = 0; k < steps; k++)
                {
                    float top = y1 - rise * (k + 1), z0 = VerZ1 + k * run;
                    m.Box(F, -gap + 0.05f, gap - 0.05f, 0f, top, z0, z0 + run, tread, Faces.Top | Faces.Left | Faces.Right);
                    m.Rect(new Frame(F.P(-gap + 0.05f, 0f, z0 + run), F.r, Vector3.up, F.n), 0f, 2f * gap - 0.1f, top - rise, top, 0f, White, ArchTex.Steps, 0.35f);
                }
                // the cheek walls: following the stair down, a hand above the treads, capped
                float zFoot = VerZ1 + steps * run;
                foreach (float x in new[] { -gap - 0.06f, gap + 0.06f })
                {
                    var side = x < 0 ? -F.r : F.r;
                    Vector3 a = F.P(x, 0f, zFoot), b = F.P(x, 0f, VerZ1), c = F.P(x, y1 + 0.35f, VerZ1), d = F.P(x, 0.55f, zFoot);
                    m.QuadFacing(a, b, c, d, side, White, ArchTex.White);
                    m.QuadFacing(a, b, c, d, -side, White, ArchTex.White);
                    Board(m, d + Vector3.up * 0.02f, c + Vector3.up * 0.02f, Vector3.up, trim);
                }
            }
            else m.QuadFacing(F.P(-gap, 0.02f, VerZ1 + steps * run), F.P(gap, 0.02f, VerZ1 + steps * run), F.P(gap, y1, VerZ1), F.P(-gap, y1, VerZ1),
                              Vector3.up + F.n, tread, ArchTex.Steps, 0f, 0f, 3f, 4f);

            // ---- the roof: steep zinc gable to the front, out over the verandah
            float over = 0.6f, rh = w * 0.42f, ye = y2 - 0.05f;
            float zf = VerZ1 + 0.45f, zb = BodyZ0 - over;
            Vector3 L0 = F.P(-w / 2 - over, ye, zb), L1 = F.P(-w / 2 - over, ye, zf), R0 = F.P(w / 2 + over, ye, zb), R1 = F.P(w / 2 + over, ye, zf);
            Vector3 T0 = F.P(0f, ye + rh, zb), T1 = F.P(0f, ye + rh, zf);
            float slope = Mathf.Sqrt(rh * rh + (w / 2 + over) * (w / 2 + over)), depth = zf - zb;
            m.QuadFacing(L0, L1, T1, T0, Vector3.up - F.r, roof, ArchTex.Zinc, 0f, 0f, depth / 1.0f, slope / 1.0f);
            m.QuadFacing(R1, R0, T0, T1, Vector3.up + F.r, roof, ArchTex.Zinc, 0f, 0f, depth / 1.0f, slope / 1.0f);
            m.QuadFacing(L0, L1, T1, T0, -Vector3.up + F.r, Soffit, ArchTex.PlanksV, 0f, 0f, depth, slope);                  // the underside
            m.QuadFacing(R1, R0, T0, T1, -Vector3.up - F.r, Soffit, ArchTex.PlanksV, 0f, 0f, depth, slope);
            // the gable board over the verandah, carved; the back gable planked
            var gf = Face(w / 2, VerZ1 + 0.1f, -w / 2, VerZ1 + 0.1f, ye);
            m.Extrude(gf, new[] { new Vector2(0.15f, 0f), new Vector2(w - 0.15f, 0f), new Vector2(w * 0.5f, rh - 0.15f) }, 0f, 0f, trim, ArchTex.Kerawang, 0.9f);
            var gb = Face(-w / 2 + 0.2f, BodyZ0, w / 2 - 0.2f, BodyZ0, ye);
            m.Extrude(gb, new[] { new Vector2(0f, 0f), new Vector2(w - 0.4f, 0f), new Vector2((w - 0.4f) * 0.5f, rh - 0.2f) }, 0f, 0f, wall, ArchTex.PlanksH, 0.9f);
            if (detail)
            {
                // white fascia boards down the front edge, the crossed finial over the ridge, the ridge cap
                Board(m, L1 + Vector3.up * 0.02f, T1 + Vector3.up * 0.02f, F.n, trim);
                Board(m, R1 + Vector3.up * 0.02f, T1 + Vector3.up * 0.02f, F.n, trim);
                Board(m, T1 + (F.r * 0.1f), T1 + Vector3.up * 0.9f - F.r * 0.35f, F.n, trim);
                Board(m, T1 - (F.r * 0.1f), T1 + Vector3.up * 0.9f + F.r * 0.35f, F.n, trim);
                Board(m, T0 + Vector3.up * 0.05f, T1 + Vector3.up * 0.05f, Vector3.up, ArchMesh.Shade(roof, 0.75f));
            }

            // ---- the kitchen wing behind, lower, with its own small gable
            if (kitchen)
            {
                float kw = w * 0.6f, kz0 = BodyZ0 - 3.0f, kz1 = BodyZ0, ky0 = 0.6f, ky1 = ky0 + 2.6f;
                if (detail) foreach (float x in new[] { -kw / 2 + 0.2f, kw / 2 - 0.2f }) foreach (float z in new[] { kz0 + 0.2f }) Post(m, F, x, z, 0f, ky0, 0.18f, Footing);
                m.Box(F, -kw / 2, kw / 2, ky0, ky1, kz0, kz1, wall, ArchTex.PlanksH, Faces.Back | Faces.Left | Faces.Right, 0.9f);
                m.Box(F, -kw / 2, kw / 2, 0f, ky0, kz0, kz1, ArchMesh.Shade(Footing, 0.85f), Faces.Back | Faces.Left | Faces.Right);
                if (detail) m.Rect(Face(-kw / 2, kz0, kw / 2, kz0, ky0), kw * 0.3f, kw * 0.55f, 0.9f, 2.0f, 0.02f, shutter, ArchTex.Louvre);
                float krh = kw * 0.4f;
                Vector3 a = F.P(-kw / 2 - 0.4f, ky1, kz0 - 0.4f), b = F.P(-kw / 2 - 0.4f, ky1, kz1), c = F.P(kw / 2 + 0.4f, ky1, kz0 - 0.4f), d = F.P(kw / 2 + 0.4f, ky1, kz1);
                Vector3 t0 = F.P(0f, ky1 + krh, kz0 - 0.4f), t1 = F.P(0f, ky1 + krh, kz1);
                m.QuadFacing(a, b, t1, t0, Vector3.up - F.r, roof, ArchTex.Zinc, 0f, 0f, 3.4f, kw * 0.6f);
                m.QuadFacing(d, c, t0, t1, Vector3.up + F.r, roof, ArchTex.Zinc, 0f, 0f, 3.4f, kw * 0.6f);
                m.Extrude(Face(-kw / 2, kz0, kw / 2, kz0, ky1), new[] { new Vector2(0f, 0f), new Vector2(kw, 0f), new Vector2(kw * 0.5f, krh) }, 0f, 0f, wall, ArchTex.PlanksH, 0.9f);
            }
            if (detail) Flora.Tufts(m, F.P(0f, 0.01f, 0f), w * 0.7f, 8, h.seed + 5);
        }

        /// <summary>One planked wall of the body with its windows (and the door on the front), each window shuttered,
        /// framed and topped with a carved kerawang vent.</summary>
        static void Wall(ArchMesh m, in Frame f, float len, float hgt, Color32 wall, Color32 trim, Color32 shutter, bool detail, bool front, System.Random rng)
        {
            _holes.Clear();
            int nw = Mathf.Max(1, Mathf.RoundToInt(len / 2.6f));
            for (int i = 0; i < nw; i++)
            {
                float cx = len * (i + 0.5f) / nw;
                if (front && i == nw / 2)
                {
                    _holes.Add(new Opening(cx - 0.5f, cx + 0.5f, 0f, 2.1f, detail ? 0.1f : 0f, ArchTex.Door, shutter, Reveal));
                    continue;
                }
                var back = rng.NextDouble() < 0.75 ? ArchTex.Louvre : ArchTex.Curtained;
                _holes.Add(new Opening(cx - 0.5f, cx + 0.5f, 0.7f, 2.2f, detail ? 0.1f : 0f, back, back == ArchTex.Louvre ? shutter : White, Reveal));
            }
            m.Panel(f, 0f, len, 0f, hgt, 0f, wall, ArchTex.PlanksH, 0.9f, _holes);
            if (!detail) return;
            foreach (var h in _holes)
            {
                // the frame, and the carved vent over it
                m.Box(f, h.x0 - 0.08f, h.x0, h.y0, h.y1 + 0.06f, 0f, 0.05f, trim, Faces.Front | Faces.Left | Faces.Right);
                m.Box(f, h.x1, h.x1 + 0.08f, h.y0, h.y1 + 0.06f, 0f, 0.05f, trim, Faces.Front | Faces.Left | Faces.Right);
                m.Box(f, h.x0 - 0.08f, h.x1 + 0.08f, h.y1, h.y1 + 0.08f, 0f, 0.06f, trim, Faces.Front | Faces.Top | Faces.Bottom);
                if (h.y0 > 0.1f) m.Box(f, h.x0 - 0.1f, h.x1 + 0.1f, h.y0 - 0.08f, h.y0, 0f, 0.1f, trim);
                m.Rect(f, h.x0, h.x1, h.y1 + 0.12f, Mathf.Min(hgt - 0.05f, h.y1 + 0.5f), 0.02f, trim, ArchTex.Kerawang);
            }
        }

        static void Post(ArchMesh m, in Frame F, float x, float z, float y0, float y1, float size, Color32 col)
        {
            float s = size * 0.5f;
            m.Box(F, x - s, x + s, y0, y1, z - s, z + s, col, Faces.All & ~Faces.Bottom);
        }

        /// <summary>A balustrade from (x0, z0) to (x1, z1): top and bottom rails with the balusters painted between.</summary>
        static void Rail(ArchMesh m, in Frame F, float x0, float z0, float x1, float z1, float y, Color32 trim)
        {
            var a = F.P(x0, y, z0); var b = F.P(x1, y, z1);
            float len = (b - a).magnitude;
            if (len < 0.1f) return;
            var d = (b - a) / len;
            var f = new Frame(a, d, Vector3.up, Vector3.Cross(Vector3.up, d));
            m.Box(f, 0f, len, 0.82f, 0.92f, -0.05f, 0.05f, trim);
            m.Box(f, 0f, len, 0.1f, 0.82f, -0.02f, 0.02f, trim, ArchTex.Rail, Faces.Front | Faces.Back, 0.9f);
        }

        /// <summary>A flat board from a to b (fascia, finial), facing `face`.</summary>
        static void Board(ArchMesh m, Vector3 a, Vector3 b, Vector3 face, Color32 col)
        {
            var d = (b - a).normalized;
            var side = Vector3.Cross(face, d).normalized * 0.11f;
            m.QuadFacing(a - side, b - side, b + side, a + side, face, col, ArchTex.White);
            m.QuadFacing(a - side, b - side, b + side, a + side, -face, col, ArchTex.White);
        }

        /// <summary>The house's solid for physics: its body and verandah up to the eaves, the stair as a ramp.</summary>
        public static void Collider(HouseSpec h, List<Vector3> v, List<int> t)
        {
            var rot = Quaternion.Euler(0f, h.yaw, 0f);
            float w = h.width;
            Vector3 P(float x, float y, float z) => h.pos + rot * new Vector3(x, y, z);
            float top = Stilt + FloorT + WallH;
            int b = v.Count;
            var corners = new[] { P(-w / 2, 0, BodyZ0), P(w / 2, 0, BodyZ0), P(w / 2, 0, VerZ1), P(-w / 2, 0, VerZ1) };
            foreach (var c in corners) { v.Add(c); v.Add(c + Vector3.up * top); }
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4, a0 = b + i * 2, a1 = a0 + 1, c0 = b + j * 2, c1 = c0 + 1;
                t.Add(a0); t.Add(c1); t.Add(a1);
                t.Add(a0); t.Add(c0); t.Add(c1);
                t.Add(a0); t.Add(a1); t.Add(c1);
                t.Add(a0); t.Add(c1); t.Add(c0);
            }
            t.Add(b + 1); t.Add(b + 3); t.Add(b + 5); t.Add(b + 1); t.Add(b + 5); t.Add(b + 7);
            t.Add(b + 1); t.Add(b + 5); t.Add(b + 3); t.Add(b + 1); t.Add(b + 7); t.Add(b + 5);
        }
    }
}
