using System.Collections.Generic;
using UnityEngine;

namespace KampungRun.Arch
{
    public enum FloraKind : byte { RainTree, Angsana, Palm, Frangipani, Banana, Shrub, Bougainvillea, Tufts }

    /// <summary>A plant, modelled into the city cells with the buildings (same material, same near/far swap).</summary>
    public class FloraSpec : IArchItem
    {
        public Vector3 pos;
        public FloraKind kind;
        public float scale = 1f;
        public int seed;

        public Vector2 Where() => new Vector2(pos.x, pos.z);
        public void Build(ArchMesh m, bool detail) => Flora.Build(this, m, detail);
    }

    /// <summary>
    /// KL's trees and plants, cartoon-modelled: crowns are clusters of soft, lumpy puffs shaded dark underneath and
    /// sunlit on top and painted with leaves; trunks are round and barked.
    ///  - Rain tree: a stout trunk forking into heavy limbs under a wide, flat umbrella of a crown.
    ///  - Angsana: a taller trunk, a dense round crown hanging lower at its edges.
    ///  - Coconut palm: a leaning, curving ringed trunk, arching fronds with leaflets, a bunch of coconuts.
    ///  - Frangipani: crooked forked branches, leaf tufts at their tips in flower.
    ///  - Banana: a green pseudo-stem and big arching leaves, torn, the old ones yellowing.
    ///  - Shrubs and bougainvillea: low clumps of puffs. Tufts: little fans of grass blades.
    /// The distance version keeps the silhouette with a puff or two and a plain trunk.
    /// </summary>
    public static class Flora
    {
        static readonly Color32 BarkCol = ArchMesh.Col(0.50f, 0.42f, 0.36f), PalmCol = ArchMesh.Col(0.62f, 0.56f, 0.48f);
        static readonly Color32 White = ArchMesh.Col(1f, 1f, 1f);

        // ---------------------------------------------------------------------------------------- unit spheres
        static Vector3[] _v0, _v1;
        static int[] _f0, _f1;

        static void Spheres()
        {
            if (_v0 != null) return;
            float g = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0), new Vector3(0, -1, g), new Vector3(0, 1, g),
                new Vector3(0, -1, -g), new Vector3(0, 1, -g), new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                        3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            _v0 = v.ToArray();
            _f0 = Outward(_v0, f);
            // once subdivided: 80 faces
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                mid[key] = v.Count - 1;
                return v.Count - 1;
            }
            var f1 = new List<int>();
            for (int i = 0; i < _f0.Length; i += 3)
            {
                int a = _f0[i], b = _f0[i + 1], c = _f0[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                f1.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            _v1 = v.ToArray();
            _f1 = Outward(_v1, f1.ToArray());
        }

        /// <summary>Wind each face so it faces out of the sphere (clockwise seen from outside).</summary>
        static int[] Outward(Vector3[] v, int[] f)
        {
            var o = (int[])f.Clone();
            for (int i = 0; i < o.Length; i += 3)
            {
                Vector3 a = v[o[i]], b = v[o[i + 1]], c = v[o[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) { int t = o[i + 1]; o[i + 1] = o[i + 2]; o[i + 2] = t; }
            }
            return o;
        }

        static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, Mathf.Clamp01(t));

        static float Hash(int seed, int i)
        {
            uint h = (uint)(seed * 374761393 + i * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
        }

        /// <summary>
        /// A lumpy puff of foliage: an ellipsoid with its corners pushed in and out, shaded from `dark` underneath to
        /// `light` on top, leaves painted on with a projection per face (the seams hide in the leaves).
        /// </summary>
        public static void Puff(ArchMesh m, Vector3 c, Vector3 r, Color32 dark, Color32 light, int seed, bool fine, ArchTex tex = ArchTex.Leaves, float lump = 0.14f)
        {
            Spheres();
            var V = fine ? _v1 : _v0;
            var F = fine ? _f1 : _f0;
            int n = V.Length;
            var p = new Vector3[n];
            var nn = new Vector3[n];
            var col = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                float j = 1f + (Hash(seed, i) - 0.5f) * 2f * lump;
                p[i] = c + Vector3.Scale(V[i] * j, r);
                nn[i] = new Vector3(V[i].x / r.x, V[i].y / r.y, V[i].z / r.z).normalized;
                col[i] = Lerp(dark, light, 0.42f + nn[i].y * 0.55f + (Hash(seed + 7, i) - 0.5f) * 0.2f);
            }
            const float tile = 1.3f;
            for (int i = 0; i < F.Length; i += 3)
            {
                int a = F[i], b = F[i + 1], d = F[i + 2];
                var fn = Vector3.Cross(p[b] - p[a], p[d] - p[a]);
                var an = new Vector3(Mathf.Abs(fn.x), Mathf.Abs(fn.y), Mathf.Abs(fn.z));
                Vector2 Uv(Vector3 q) => an.y >= an.x && an.y >= an.z ? new Vector2(q.x, q.z) / tile : an.x >= an.z ? new Vector2(q.z, q.y) / tile : new Vector2(q.x, q.y) / tile;
                m.TriN(p[a], p[b], p[d], nn[a], nn[b], nn[d], col[a], col[b], col[d], tex, Uv(p[a]), Uv(p[b]), Uv(p[d]));
            }
        }

        /// <summary>A tapered round limb from p0 to p1, barked.</summary>
        public static void Limb(ArchMesh m, Vector3 p0, Vector3 p1, float r0, float r1, int sides, Color32 col, ArchTex tex = ArchTex.Bark, float uTile = 1f)
        {
            var axis = p1 - p0;
            float len = axis.magnitude;
            if (len < 1e-3f) return;
            axis /= len;
            var s1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            var s2 = Vector3.Cross(axis, s1);
            var dark = ArchMesh.Shade(col, 0.8f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var d0 = s1 * Mathf.Cos(a0) + s2 * Mathf.Sin(a0);
                var d1 = s1 * Mathf.Cos(a1) + s2 * Mathf.Sin(a1);
                float u0 = i / (float)sides * uTile, u1 = (i + 1) / (float)sides * uTile, v1 = len / 1.4f;
                m.QuadN(p0 + d0 * r0, p0 + d1 * r0, p1 + d1 * r1, p1 + d0 * r1, d0, d1, d1, d0,
                        dark, dark, col, col, tex, new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, v1), new Vector2(u0, v1));
            }
        }

        public static void Build(FloraSpec f, ArchMesh m, bool detail)
        {
            switch (f.kind)
            {
                case FloraKind.RainTree: RainTree(f, m, detail); break;
                case FloraKind.Angsana: Angsana(f, m, detail); break;
                case FloraKind.Palm: Palm(f, m, detail); break;
                case FloraKind.Frangipani: Frangipani(f, m, detail); break;
                case FloraKind.Banana: Banana(f, m, detail); break;
                case FloraKind.Shrub: Shrub(f, m, detail, false); break;
                case FloraKind.Bougainvillea: Shrub(f, m, detail, true); break;
                case FloraKind.Tufts: if (detail) Tufts(m, f.pos, 2.4f * f.scale, 9, f.seed); break;
            }
        }

        // ---------------------------------------------------------------------------------------- trees
        static void RainTree(FloraSpec f, ArchMesh m, bool detail)
        {
            var rng = new System.Random(f.seed);
            float s = f.scale, th = 2.2f * s, r = 0.36f * s, R = 4.8f * s, cy = th + 2.3f * s;
            var b = f.pos;
            var dark = ArchMesh.Col(0.20f, 0.42f, 0.18f);
            var light = Lerp(ArchMesh.Col(0.62f, 0.82f, 0.34f), ArchMesh.Col(0.72f, 0.86f, 0.38f), (float)rng.NextDouble());
            if (!detail)
            {
                Limb(m, b, b + Vector3.up * (th + 1f), r, r * 0.7f, 4, BarkCol);
                Puff(m, b + Vector3.up * (cy + 0.3f), new Vector3(R, 1.9f * s, R), dark, light, f.seed, false, ArchTex.Leaves, 0.1f);
                return;
            }
            var top = b + Vector3.up * th;
            Limb(m, b, top, r * 1.2f, r * 0.85f, 7, BarkCol);
            int limbs = rng.Next(4, 6);
            float a0 = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i < limbs; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / limbs + ((float)rng.NextDouble() - 0.5f) * 0.6f;
                var dir = new Vector3(Mathf.Cos(a), 0.62f + (float)rng.NextDouble() * 0.25f, Mathf.Sin(a)).normalized;
                float len = (2.4f + (float)rng.NextDouble() * 0.9f) * s;
                var end = top + dir * len;
                Limb(m, top, end, r * 0.62f, r * 0.3f, 5, BarkCol);
                // a puff over each limb's end, round the rim of the umbrella
                var pc = new Vector3(end.x, b.y + cy + ((float)rng.NextDouble() - 0.3f) * 0.6f * s, end.z);
                Puff(m, pc, new Vector3(2.0f, 1.3f, 2.0f) * s, dark, light, f.seed + i * 31, true, ArchTex.Leaves, 0.09f);
            }
            // the umbrella itself, and the sunlit humps on top
            Puff(m, b + Vector3.up * cy, new Vector3(R * 0.78f, 1.55f * s, R * 0.78f), dark, light, f.seed + 5, true, ArchTex.Leaves, 0.1f);
            for (int i = 0; i < 3; i++)
            {
                float a = a0 + i * 2.1f;
                var pc = b + new Vector3(Mathf.Cos(a) * R * 0.32f, cy + 1.0f * s, Mathf.Sin(a) * R * 0.32f);
                Puff(m, pc, new Vector3(1.7f, 1.1f, 1.7f) * s, dark, light, f.seed + 50 + i, true);
            }
            Tufts(m, b, 1.3f * s, 5, f.seed);
        }

        static void Angsana(FloraSpec f, ArchMesh m, bool detail)
        {
            var rng = new System.Random(f.seed);
            float s = f.scale, th = 3.0f * s, r = 0.28f * s, cy = th + 1.9f * s;
            var b = f.pos;
            var dark = ArchMesh.Col(0.18f, 0.42f, 0.22f);
            var light = Lerp(ArchMesh.Col(0.56f, 0.80f, 0.36f), ArchMesh.Col(0.66f, 0.84f, 0.40f), (float)rng.NextDouble());
            if (!detail)
            {
                Limb(m, b, b + Vector3.up * (th + 0.6f), r, r * 0.7f, 4, BarkCol);
                Puff(m, b + Vector3.up * cy, new Vector3(2.7f, 2.4f, 2.7f) * s, dark, light, f.seed, false);
                return;
            }
            var top = b + Vector3.up * (th + 0.8f);
            Limb(m, b, top, r * 1.15f, r * 0.7f, 6, BarkCol);
            Puff(m, b + Vector3.up * cy, new Vector3(2.5f, 2.1f, 2.5f) * s, dark, light, f.seed, true);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f + (float)rng.NextDouble();
                var pc = b + new Vector3(Mathf.Cos(a) * 1.9f * s, cy - 0.6f * s, Mathf.Sin(a) * 1.9f * s);   // the weeping edge
                Puff(m, pc, new Vector3(1.4f, 1.3f, 1.4f) * s, dark, light, f.seed + i * 13, true, ArchTex.Leaves, 0.09f);
            }
            Puff(m, b + Vector3.up * (cy + 1.5f * s), new Vector3(1.5f, 1.1f, 1.5f) * s, dark, light, f.seed + 99, false);
            Tufts(m, b, 1.0f * s, 4, f.seed);
        }

        static void Palm(FloraSpec f, ArchMesh m, bool detail)
        {
            var rng = new System.Random(f.seed);
            float s = f.scale, H = (6.5f + (float)rng.NextDouble() * 2.5f) * s;
            var b = f.pos;
            float lean = (float)rng.NextDouble() * Mathf.PI * 2f;
            var leanDir = new Vector3(Mathf.Cos(lean), 0f, Mathf.Sin(lean));
            float bend = (0.6f + (float)rng.NextDouble() * 1.2f) * s;
            Vector3 At(float t) => b + Vector3.up * (H * t) + leanDir * (bend * t * t);
            int segs = detail ? 7 : 2;
            for (int i = 0; i < segs; i++)
            {
                float t0 = i / (float)segs, t1 = (i + 1) / (float)segs;
                Limb(m, At(t0), At(t1), Mathf.Lerp(0.26f, 0.17f, t0) * s, Mathf.Lerp(0.26f, 0.17f, t1) * s, detail ? 6 : 4, PalmCol, ArchTex.PalmTrunk);
            }
            var crown = At(1f);
            int fronds = detail ? rng.Next(12, 16) : 7;
            float a0 = (float)rng.NextDouble() * Mathf.PI * 2f;
            Color32 green = ArchMesh.Col(0.42f, 0.72f, 0.30f), old = ArchMesh.Col(0.74f, 0.66f, 0.34f);
            for (int i = 0; i < fronds; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / fronds + ((float)rng.NextDouble() - 0.5f) * 0.4f;
                float up = 0.15f + (float)rng.NextDouble() * 0.55f - (i % 3 == 0 ? 0.45f : 0f);
                var col = i % 4 == 3 ? Lerp(green, old, 0.7f) : Lerp(green, ArchMesh.Col(0.42f, 0.7f, 0.3f), (float)rng.NextDouble());
                Frond(m, crown, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), up, (3.2f + (float)rng.NextDouble() * 0.8f) * s, col, detail);
            }
            if (detail)
                for (int i = 0; i < 4; i++)
                {
                    float a = a0 + i * 1.7f;
                    Puff(m, crown + new Vector3(Mathf.Cos(a) * 0.32f, -0.35f, Mathf.Sin(a) * 0.32f) * s, Vector3.one * 0.2f * s,
                         ArchMesh.Col(0.36f, 0.42f, 0.14f), ArchMesh.Col(0.58f, 0.62f, 0.2f), f.seed + i, false, ArchTex.White, 0.05f);
                }
        }

        /// <summary>One palm frond: a spine arching out and drooping, leaflets painted on two halves folded into a V.</summary>
        static void Frond(ArchMesh m, Vector3 root, Vector3 dir, float rise, float len, Color32 col, bool detail)
        {
            int segs = detail ? 4 : 1;
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 Spine(float t) => root + dir * (len * t) + Vector3.up * (len * (rise * t - 0.55f * t * t));
            float Width(float t) => len * 0.26f * Mathf.Sin(Mathf.Clamp01(t * 1.1f + 0.05f) * Mathf.PI) + 0.06f;
            var dark = ArchMesh.Shade(col, 0.7f);
            for (int i = 0; i < segs; i++)
            {
                float t0 = i / (float)segs, t1 = (i + 1) / (float)segs;
                Vector3 s0 = Spine(t0), s1 = Spine(t1);
                foreach (float k in new[] { -1f, 1f })
                {
                    // each half hangs a little below the spine (the V), leaflets pointing forward
                    var e0 = s0 + side * (k * Width(t0)) + Vector3.down * Width(t0) * 0.45f;
                    var e1 = s1 + side * (k * Width(t1)) + Vector3.down * Width(t1) * 0.45f;
                    var nUp = (Vector3.up + side * k * 0.2f).normalized;
                    float v0 = k < 0 ? 0f : 0.5f, v1 = k < 0 ? 0.5f : 1f;
                    // both faces: fronds are seen from above and below
                    m.QuadN(s0, s1, e1, e0, nUp, nUp, nUp, nUp, dark, col, col, dark, ArchTex.Frond,
                            new Vector2(t0, k < 0 ? 0.5f : 0.5f), new Vector2(t1, 0.5f), new Vector2(t1, k < 0 ? v0 : v1), new Vector2(t0, k < 0 ? v0 : v1));
                    if (detail)
                        m.QuadN(s0, e0, e1, s1, -nUp, -nUp, -nUp, -nUp, dark, dark, col, col, ArchTex.Frond,
                                new Vector2(t0, 0.5f), new Vector2(t0, k < 0 ? v0 : v1), new Vector2(t1, k < 0 ? v0 : v1), new Vector2(t1, 0.5f));
                }
            }
        }

        static void Frangipani(FloraSpec f, ArchMesh m, bool detail)
        {
            var rng = new System.Random(f.seed);
            float s = f.scale * 1.35f;
            var b = f.pos;
            var dark = ArchMesh.Col(0.22f, 0.44f, 0.20f);
            var light = ArchMesh.Col(0.66f, 0.84f, 0.40f);
            if (!detail)
            {
                Limb(m, b, b + Vector3.up * 2.4f * s, 0.14f * s, 0.1f * s, 4, BarkCol);
                Puff(m, b + Vector3.up * 3.2f * s, new Vector3(1.9f, 1.2f, 1.9f) * s, dark, light, f.seed, false, ArchTex.Frangipani);
                return;
            }
            var fork = b + Vector3.up * 1.5f * s;
            Limb(m, b, fork, 0.16f * s, 0.12f * s, 6, BarkCol);
            int arms = rng.Next(3, 5);
            for (int i = 0; i < arms; i++)
            {
                float a = i * Mathf.PI * 2f / arms + (float)rng.NextDouble();
                var mid = fork + new Vector3(Mathf.Cos(a) * 0.7f, 0.8f, Mathf.Sin(a) * 0.7f) * s;
                var tip = mid + new Vector3(Mathf.Cos(a + 0.6f) * 0.7f, 0.9f, Mathf.Sin(a + 0.6f) * 0.7f) * s;
                Limb(m, fork, mid, 0.1f * s, 0.08f * s, 5, BarkCol);
                Limb(m, mid, tip, 0.08f * s, 0.06f * s, 5, BarkCol);
                Puff(m, tip + Vector3.up * 0.25f * s, new Vector3(0.95f, 0.6f, 0.95f) * s, dark, light, f.seed + i * 17, false,
                     i % 2 == 0 ? ArchTex.Frangipani : ArchTex.Leaves);
            }
            Tufts(m, b, 0.8f * s, 3, f.seed);
        }

        static void Banana(FloraSpec f, ArchMesh m, bool detail)
        {
            var rng = new System.Random(f.seed);
            float s = f.scale, sh = 1.6f * s;
            var b = f.pos;
            var stem = ArchMesh.Col(0.42f, 0.6f, 0.28f);
            Limb(m, b, b + Vector3.up * sh, 0.17f * s, 0.12f * s, detail ? 6 : 4, stem, ArchTex.White);
            int leaves = detail ? rng.Next(6, 9) : 4;
            float a0 = (float)rng.NextDouble() * Mathf.PI * 2f;
            Color32 green = ArchMesh.Col(0.44f, 0.72f, 0.30f), old = ArchMesh.Col(0.72f, 0.66f, 0.32f);
            for (int i = 0; i < leaves; i++)
            {
                float a = a0 + i * 2.4f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                bool aged = i >= leaves - 2;
                float rise = aged ? -0.2f : 0.5f + (float)rng.NextDouble() * 0.5f;
                Leaf(m, b + Vector3.up * (sh - i * 0.06f * s), dir, rise, (1.8f + (float)rng.NextDouble() * 0.6f) * s, 0.42f * s,
                     aged ? old : green, detail ? 4 : 1);
            }
        }

        /// <summary>A long leaf on an arching midrib, its two halves folded slightly, painted both sides.</summary>
        static void Leaf(ArchMesh m, Vector3 root, Vector3 dir, float rise, float len, float halfW, Color32 col, int segs)
        {
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 Spine(float t) => root + dir * (len * (0.15f + t)) + Vector3.up * (len * (rise * t - 0.7f * t * t));
            float W(float t) => halfW * Mathf.Sin(Mathf.Clamp01(t * 0.95f + 0.05f) * Mathf.PI);
            var dark = ArchMesh.Shade(col, 0.72f);
            for (int i = 0; i < segs; i++)
            {
                float t0 = i / (float)segs, t1 = (i + 1) / (float)segs;
                Vector3 s0 = Spine(t0), s1 = Spine(t1);
                foreach (float k in new[] { -1f, 1f })
                {
                    var e0 = s0 + side * (k * W(t0)) + Vector3.down * W(t0) * 0.25f;
                    var e1 = s1 + side * (k * W(t1)) + Vector3.down * W(t1) * 0.25f;
                    float u = k < 0 ? 0f : 1f;
                    m.QuadN(s0, s1, e1, e0, Vector3.up, Vector3.up, Vector3.up, Vector3.up, col, col, dark, dark, ArchTex.Banana,
                            new Vector2(0.5f, t0), new Vector2(0.5f, t1), new Vector2(u, t1), new Vector2(u, t0));
                    if (segs > 1)
                        m.QuadN(s0, e0, e1, s1, Vector3.down, Vector3.down, Vector3.down, Vector3.down, dark, dark, dark, dark, ArchTex.Banana,
                                new Vector2(0.5f, t0), new Vector2(u, t0), new Vector2(u, t1), new Vector2(0.5f, t1));
                }
            }
        }

        static void Shrub(FloraSpec f, ArchMesh m, bool detail, bool flowering)
        {
            var rng = new System.Random(f.seed);
            float s = f.scale;
            var b = f.pos;
            var dark = ArchMesh.Col(0.26f, 0.5f, 0.24f);
            var light = flowering ? ArchMesh.Col(1f, 1f, 1f) : ArchMesh.Col(0.7f, 0.9f, 0.42f);
            if (flowering) dark = ArchMesh.Col(0.8f, 0.8f, 0.8f);
            var tex = flowering ? ArchTex.Bougainvillea : ArchTex.Leaves;
            if (!detail) { Puff(m, b + Vector3.up * 0.55f * s, new Vector3(1.1f, 0.7f, 1.1f) * s, dark, light, f.seed, false, tex); return; }
            int n = rng.Next(3, 6);
            for (int i = 0; i < n; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = (float)rng.NextDouble() * 0.7f * s;
                float r = (0.5f + (float)rng.NextDouble() * 0.4f) * s;
                Puff(m, b + new Vector3(Mathf.Cos(a) * d, r * 0.85f, Mathf.Sin(a) * d), new Vector3(r, r * 0.85f, r), dark, light, f.seed + i * 7, true, tex, 0.08f);
            }
            Tufts(m, b, 1.0f * s, 3, f.seed + 3);
        }

        // ---------------------------------------------------------------------------------------- grass
        static readonly Color32 BladeBase = ArchMesh.Col(0.24f, 0.44f, 0.18f), BladeTip = ArchMesh.Col(0.56f, 0.76f, 0.32f);

        /// <summary>A scatter of grass tufts round a spot: little fans of blades, dark at the root, light at the tip.</summary>
        public static void Tufts(ArchMesh m, Vector3 c, float radius, int count, int seed)
        {
            for (int k = 0; k < count; k++)
            {
                float a = Hash(seed, k * 3) * Mathf.PI * 2f, d = Mathf.Sqrt(Hash(seed, k * 3 + 1)) * radius;
                var p = c + new Vector3(Mathf.Cos(a) * d, 0.01f, Mathf.Sin(a) * d);
                Tuft(m, p, 0.6f + Hash(seed, k * 3 + 2) * 0.6f, seed + k * 11);
            }
        }

        static void Tuft(ArchMesh m, Vector3 p, float size, int seed)
        {
            int blades = 6 + (int)(Hash(seed, 0) * 4f);
            var tip = Lerp(BladeTip, ArchMesh.Col(0.66f, 0.76f, 0.36f), Hash(seed, 1));
            for (int i = 0; i < blades; i++)
            {
                float a = i * Mathf.PI * 2f / blades + Hash(seed, i + 2) * 0.8f;
                var out_ = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = new Vector3(-out_.z, 0f, out_.x);
                float h = (0.22f + Hash(seed, i + 20) * 0.26f) * size, w = 0.05f * size;
                var b0 = p + out_ * 0.04f - side * w;
                var b1 = p + out_ * 0.04f + side * w;
                var t = p + out_ * (h * 0.45f) + Vector3.up * h;
                m.TriN(b0, t, b1, Vector3.up, Vector3.up, Vector3.up, BladeBase, tip, BladeBase, ArchTex.White, Vector2.zero, Vector2.zero, Vector2.zero);
                m.TriN(b1, t, b0, Vector3.up, Vector3.up, Vector3.up, BladeBase, tip, BladeBase, ArchTex.White, Vector2.zero, Vector2.zero, Vector2.zero);
            }
        }

        /// <summary>The trunk's solid for physics (a thin prism up the trunk), or nothing for plants you walk through.</summary>
        public static void Collider(FloraSpec f, List<Vector3> v, List<int> t)
        {
            float r, h;
            switch (f.kind)
            {
                case FloraKind.RainTree: r = 0.4f * f.scale; h = 3.0f * f.scale; break;
                case FloraKind.Angsana: r = 0.3f * f.scale; h = 3.0f * f.scale; break;
                case FloraKind.Palm: r = 0.25f * f.scale; h = 4.0f * f.scale; break;
                case FloraKind.Frangipani: r = 0.18f * f.scale; h = 1.6f * f.scale; break;
                default: return;
            }
            int b = v.Count;
            const int n = 6;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var o = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                v.Add(f.pos + o);
                v.Add(f.pos + o + Vector3.up * h);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int a0 = b + i * 2, a1 = a0 + 1, c0 = b + j * 2, c1 = c0 + 1;
                t.Add(a0); t.Add(a1); t.Add(c1);
                t.Add(a0); t.Add(c1); t.Add(c0);
            }
        }
    }
}
