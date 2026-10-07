using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KampungRun.Arch
{
    /// <summary>
    /// A local frame on a facade: x runs along the wall (left to right seen from outside), y up, z out of the
    /// wall toward the street. Footprints are counter-clockwise seen from above, so walking an edge from p0
    /// to p1 runs left to right for someone standing outside it.
    /// </summary>
    public readonly struct Frame
    {
        public readonly Vector3 o, r, u, n;

        public Frame(Vector3 origin, Vector3 right, Vector3 up, Vector3 outward) { o = origin; r = right; u = up; n = outward; }

        /// <summary>The frame on footprint edge p0 -> p1 (counter-clockwise footprint), standing at height y.</summary>
        public static Frame Edge(Vector2 p0, Vector2 p1, float y)
        {
            var d = (p1 - p0).normalized;
            return new Frame(new Vector3(p0.x, y, p0.y), new Vector3(d.x, 0f, d.y), Vector3.up, new Vector3(d.y, 0f, -d.x));
        }

        public Vector3 P(float x, float y, float z) => o + r * x + u * y + n * z;

        /// <summary>The same frame moved along itself.</summary>
        public Frame Shift(float x, float y, float z) => new Frame(P(x, y, z), r, u, n);
    }

    /// <summary>A rectangular hole in a wall panel, with a reveal and something painted at its back.</summary>
    public struct Opening
    {
        public float x0, x1, y0, y1, depth;
        public ArchTex back;
        public Color32 backCol, revealCol;

        public Opening(float x0, float x1, float y0, float y1, float depth, ArchTex back, Color32 backCol, Color32 revealCol)
        {
            this.x0 = x0; this.x1 = x1; this.y0 = y0; this.y1 = y1; this.depth = depth;
            this.back = back; this.backCol = backCol; this.revealCol = revealCol;
        }
    }

    [System.Flags]
    public enum Faces { None = 0, Front = 1, Back = 2, Left = 4, Right = 8, Top = 16, Bottom = 32, All = 63 }

    /// <summary>
    /// A flat-shaded mesh under construction. Every face carries a tint (vertex colour, alpha = 1 - gloss) and
    /// uv = (u, v, slice of the ArchTex array). Faces are built from their corners as seen from the front:
    /// bottom-left, bottom-right, top-right, top-left.
    /// </summary>
    public class ArchMesh
    {
        public readonly List<Vector3> V = new List<Vector3>(4096);
        public readonly List<Vector3> N = new List<Vector3>(4096);
        public readonly List<Color32> C = new List<Color32>(4096);
        public readonly List<Vector3> UV = new List<Vector3>(4096);
        public readonly List<int> T = new List<int>(8192);

        public int Count => V.Count;

        public void Clear() { V.Clear(); N.Clear(); C.Clear(); UV.Clear(); T.Clear(); }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col, ArchTex tex,
                         float u0 = 0f, float v0 = 0f, float u1 = 1f, float v1 = 1f)
        {
            var nrm = Vector3.Cross(d - a, b - a);
            float len = nrm.magnitude;
            if (len < 1e-8f) return;
            nrm /= len;
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            N.Add(nrm); N.Add(nrm); N.Add(nrm); N.Add(nrm);
            C.Add(col); C.Add(col); C.Add(col); C.Add(col);
            float s = (float)tex;
            UV.Add(new Vector3(u0, v0, s)); UV.Add(new Vector3(u1, v0, s)); UV.Add(new Vector3(u1, v1, s)); UV.Add(new Vector3(u0, v1, s));
            T.Add(i); T.Add(i + 3); T.Add(i + 2);
            T.Add(i); T.Add(i + 2); T.Add(i + 1);
        }

        /// <summary>A triangle, a -> b -> c clockwise seen from its front.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color32 col, ArchTex tex, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var nrm = Vector3.Cross(b - a, c - a);
            float len = nrm.magnitude;
            if (len < 1e-8f) return;
            nrm /= len;
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            N.Add(nrm); N.Add(nrm); N.Add(nrm);
            C.Add(col); C.Add(col); C.Add(col);
            float s = (float)tex;
            UV.Add(new Vector3(ua.x, ua.y, s)); UV.Add(new Vector3(ub.x, ub.y, s)); UV.Add(new Vector3(uc.x, uc.y, s));
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        /// <summary>A triangle with its own normals and colours at each corner (round, shaded things: tree crowns,
        /// trunks), a -> b -> c clockwise seen from its front.</summary>
        public void TriN(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Color32 ca, Color32 cb, Color32 cc,
                         ArchTex tex, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            N.Add(na); N.Add(nb); N.Add(nc);
            C.Add(ca); C.Add(cb); C.Add(cc);
            float s = (float)tex;
            UV.Add(new Vector3(ua.x, ua.y, s)); UV.Add(new Vector3(ub.x, ub.y, s)); UV.Add(new Vector3(uc.x, uc.y, s));
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        /// <summary>A quad with corner normals (smooth shading), wound to face the way its normals point.</summary>
        public void QuadN(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
                          Color32 ca, Color32 cb, Color32 cc, Color32 cd, ArchTex tex, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            if (Vector3.Dot(Vector3.Cross(d - a, b - a), na + nb + nc + nd) >= 0f)
            {
                TriN(a, d, c, na, nd, nc, ca, cd, cc, tex, ua, ud, uc);
                TriN(a, c, b, na, nc, nb, ca, cc, cb, tex, ua, uc, ub);
            }
            else
            {
                TriN(a, b, c, na, nb, nc, ca, cb, cc, tex, ua, ub, uc);
                TriN(a, c, d, na, nc, nd, ca, cc, cd, tex, ua, uc, ud);
            }
        }

        /// <summary>A quad whose winding is chosen so it faces `want` (handy where frames get mirrored).</summary>
        public void QuadFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 want, Color32 col, ArchTex tex,
                               float u0 = 0f, float v0 = 0f, float u1 = 1f, float v1 = 1f)
        {
            if (Vector3.Dot(Vector3.Cross(d - a, b - a), want) >= 0f) Quad(a, b, c, d, col, tex, u0, v0, u1, v1);
            else Quad(b, a, d, c, col, tex, u1, v0, u0, v1);
        }

        /// <summary>A triangle wound to face `want`.</summary>
        public void TriFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 want, Color32 col, ArchTex tex, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), want) >= 0f) Tri(a, b, c, col, tex, ua, ub, uc);
            else Tri(a, c, b, col, tex, ua, uc, ub);
        }

        static readonly List<int> _otri = new List<int>(64);

        /// <summary>
        /// A flat shape drawn on a wall (outline in the frame's x, y, counter-clockwise seen from the front) pushed
        /// out from z0 to z1: pediments, gables, signs with shaped tops. The front takes `tex` stretched over the
        /// outline's bounds (or tiled every `tile` metres).
        /// </summary>
        public void Extrude(in Frame f, IList<Vector2> outline, float z0, float z1, Color32 col, ArchTex tex, float tile = 0f, Color32? sideCol = null)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in outline) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            Vector2 Uv(Vector2 p) => tile > 0f ? p / tile : new Vector2((p.x - minX) / Mathf.Max(1e-4f, maxX - minX), (p.y - minY) / Mathf.Max(1e-4f, maxY - minY));
            Triangulate(outline, _otri);
            for (int i = 0; i + 2 < _otri.Count; i += 3)
            {
                Vector2 a = outline[_otri[i]], b = outline[_otri[i + 1]], c = outline[_otri[i + 2]];
                TriFacing(f.P(a.x, a.y, z1), f.P(b.x, b.y, z1), f.P(c.x, c.y, z1), f.n, col, tex, Uv(a), Uv(b), Uv(c));
                if (z1 - z0 > 1e-4f) TriFacing(f.P(a.x, a.y, z0), f.P(b.x, b.y, z0), f.P(c.x, c.y, z0), -f.n, col, ArchTex.White, Vector2.zero, Vector2.zero, Vector2.zero);
            }
            if (z1 - z0 <= 1e-4f) return;
            var sc = sideCol ?? col;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 p = outline[i], q = outline[(i + 1) % outline.Count];
                var d = q - p;
                if (d.sqrMagnitude < 1e-8f) continue;
                var out2 = new Vector2(d.y, -d.x);                                     // outward of a counter-clockwise outline
                var want = f.r * out2.x + f.u * out2.y;
                QuadFacing(f.P(p.x, p.y, z0), f.P(q.x, q.y, z0), f.P(q.x, q.y, z1), f.P(p.x, p.y, z1), want, sc, ArchTex.White);
            }
        }

        /// <summary>An upright n-sided prism (water tanks, columns, posts) from base centre c, radius r, height h.</summary>
        public void Cylinder(Vector3 c, float r, float h, int sides, Color32 col, ArchTex tex = ArchTex.White, bool top = true)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = c + new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
                var p1 = c + new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                var mid = ((p0 + p1) * 0.5f - c).normalized;
                QuadFacing(p0, p1, p1 + Vector3.up * h, p0 + Vector3.up * h, mid, col, tex, i / (float)sides, 0f, (i + 1) / (float)sides, 1f);
                if (top) TriFacing(c + Vector3.up * h, p0 + Vector3.up * h, p1 + Vector3.up * h, Vector3.up, Shade(col, 1.08f), ArchTex.White, Vector2.zero, Vector2.zero, Vector2.zero);
            }
        }

        /// <summary>A rectangle in a frame's wall plane at depth z, x0..x1 by y0..y1. tile &gt; 0 tiles the texture
        /// every `tile` metres (planks, brick); 0 stretches it once over the rectangle.</summary>
        public void Rect(in Frame f, float x0, float x1, float y0, float y1, float z, Color32 col, ArchTex tex, float tile = 0f)
        {
            if (x1 - x0 < 1e-4f || y1 - y0 < 1e-4f) return;
            if (tile > 0f) Quad(f.P(x0, y0, z), f.P(x1, y0, z), f.P(x1, y1, z), f.P(x0, y1, z), col, tex, x0 / tile, y0 / tile, x1 / tile, y1 / tile);
            else Quad(f.P(x0, y0, z), f.P(x1, y0, z), f.P(x1, y1, z), f.P(x0, y1, z), col, tex);
        }

        /// <summary>A plain painted box with only some of its faces.</summary>
        public void Box(in Frame f, float x0, float x1, float y0, float y1, float z0, float z1, Color32 col, Faces faces) =>
            Box(f, x0, x1, y0, y1, z0, z1, col, ArchTex.White, faces);

        /// <summary>A box in a frame, x0..x1, y0..y1, z0..z1 (z out of the wall). tile as for Rect.</summary>
        public void Box(in Frame f, float x0, float x1, float y0, float y1, float z0, float z1, Color32 col, ArchTex tex = ArchTex.White,
                        Faces faces = Faces.All, float tile = 0f)
        {
            float U(float a) => tile > 0f ? a / tile : 0f;
            bool T0 = tile > 0f;
            if ((faces & Faces.Front) != 0)
                Quad(f.P(x0, y0, z1), f.P(x1, y0, z1), f.P(x1, y1, z1), f.P(x0, y1, z1), col, tex, T0 ? U(x0) : 0, T0 ? U(y0) : 0, T0 ? U(x1) : 1, T0 ? U(y1) : 1);
            if ((faces & Faces.Back) != 0)
                Quad(f.P(x1, y0, z0), f.P(x0, y0, z0), f.P(x0, y1, z0), f.P(x1, y1, z0), col, tex, T0 ? U(-x1) : 0, T0 ? U(y0) : 0, T0 ? U(-x0) : 1, T0 ? U(y1) : 1);
            if ((faces & Faces.Right) != 0)
                Quad(f.P(x1, y0, z1), f.P(x1, y0, z0), f.P(x1, y1, z0), f.P(x1, y1, z1), col, tex, T0 ? U(-z1) : 0, T0 ? U(y0) : 0, T0 ? U(-z0) : 1, T0 ? U(y1) : 1);
            if ((faces & Faces.Left) != 0)
                Quad(f.P(x0, y0, z0), f.P(x0, y0, z1), f.P(x0, y1, z1), f.P(x0, y1, z0), col, tex, T0 ? U(z0) : 0, T0 ? U(y0) : 0, T0 ? U(z1) : 1, T0 ? U(y1) : 1);
            if ((faces & Faces.Top) != 0)
                Quad(f.P(x0, y1, z1), f.P(x1, y1, z1), f.P(x1, y1, z0), f.P(x0, y1, z0), col, tex, T0 ? U(x0) : 0, T0 ? U(-z1) : 0, T0 ? U(x1) : 1, T0 ? U(-z0) : 1);
            if ((faces & Faces.Bottom) != 0)
                Quad(f.P(x0, y0, z0), f.P(x1, y0, z0), f.P(x1, y0, z1), f.P(x0, y0, z1), col, tex, T0 ? U(x0) : 0, T0 ? U(z0) : 0, T0 ? U(x1) : 1, T0 ? U(z1) : 1);
        }

        static readonly List<float> _xs = new List<float>(16), _ys = new List<float>(16);

        /// <summary>
        /// A wall panel x0..x1 by y0..y1 at depth z with rectangular holes, each with a reveal `depth` deep and its
        /// back face painted. The solid part is cut into as few strips as the holes allow.
        /// </summary>
        public void Panel(in Frame f, float x0, float x1, float y0, float y1, float z, Color32 col, ArchTex tex, float tile,
                          List<Opening> holes)
        {
            if (holes == null || holes.Count == 0) { Rect(f, x0, x1, y0, y1, z, col, tex, tile); return; }
            _xs.Clear(); _ys.Clear();
            _xs.Add(x0); _xs.Add(x1); _ys.Add(y0); _ys.Add(y1);
            foreach (var h in holes)
            {
                _xs.Add(Mathf.Clamp(h.x0, x0, x1)); _xs.Add(Mathf.Clamp(h.x1, x0, x1));
                _ys.Add(Mathf.Clamp(h.y0, y0, y1)); _ys.Add(Mathf.Clamp(h.y1, y0, y1));
            }
            _xs.Sort(); _ys.Sort();
            for (int j = 0; j + 1 < _ys.Count; j++)
            {
                float ya = _ys[j], yb = _ys[j + 1];
                if (yb - ya < 1e-4f) continue;
                float cy = (ya + yb) * 0.5f;
                int run = -1;
                for (int i = 0; i + 1 < _xs.Count; i++)
                {
                    float xa = _xs[i], xb = _xs[i + 1];
                    bool solid = xb - xa > 1e-4f;
                    if (solid)
                    {
                        float cx = (xa + xb) * 0.5f;
                        foreach (var h in holes) if (cx > h.x0 && cx < h.x1 && cy > h.y0 && cy < h.y1) { solid = false; break; }
                    }
                    if (solid) { if (run < 0) run = i; continue; }
                    if (run >= 0 && xb - xa > 1e-4f) { Rect(f, _xs[run], xa, ya, yb, z, col, tex, tile); run = -1; }
                }
                if (run >= 0) Rect(f, _xs[run], _xs[_xs.Count - 1], ya, yb, z, col, tex, tile);
            }
            foreach (var h in holes) Hole(f, h, z);
        }

        /// <summary>The reveal and back of one opening (the wall round it is someone else's job).</summary>
        public void Hole(in Frame f, in Opening h, float z)
        {
            float zb = z - h.depth;
            if (h.depth > 1e-4f)
            {
                // left jamb faces right, right jamb faces left, head faces down, sill faces up
                Quad(f.P(h.x0, h.y0, zb), f.P(h.x0, h.y0, z), f.P(h.x0, h.y1, z), f.P(h.x0, h.y1, zb), h.revealCol, ArchTex.White);
                Quad(f.P(h.x1, h.y0, z), f.P(h.x1, h.y0, zb), f.P(h.x1, h.y1, zb), f.P(h.x1, h.y1, z), h.revealCol, ArchTex.White);
                Quad(f.P(h.x0, h.y1, z), f.P(h.x1, h.y1, z), f.P(h.x1, h.y1, zb), f.P(h.x0, h.y1, zb), Shade(h.revealCol, 0.8f), ArchTex.White);
                Quad(f.P(h.x0, h.y0, zb), f.P(h.x1, h.y0, zb), f.P(h.x1, h.y0, z), f.P(h.x0, h.y0, z), h.revealCol, ArchTex.White);
            }
            Rect(f, h.x0, h.x1, h.y0, h.y1, zb, h.backCol, h.back);
        }

        /// <summary>The walls of a footprint ring (counter-clockwise) from y0 to y1, outward-facing.</summary>
        public void Walls(IList<Vector2> ring, float y0, float y1, Color32 col, ArchTex tex = ArchTex.White, float tile = 0f)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 a = ring[i], b = ring[(i + 1) % ring.Count];
                float len = (b - a).magnitude;
                if (len < 1e-3f) continue;
                Rect(Frame.Edge(a, b, y0), 0f, len, 0f, y1 - y0, 0f, col, tex, tile);
            }
        }

        static readonly List<int> _tri = new List<int>(64);

        /// <summary>A flat cap over a footprint ring at height y, facing up (or down).</summary>
        public void Cap(IList<Vector2> ring, float y, Color32 col, ArchTex tex = ArchTex.White, float tile = 0f, bool down = false)
        {
            Triangulate(ring, _tri);
            for (int i = 0; i + 2 < _tri.Count; i += 3)
            {
                Vector2 a = ring[_tri[i]], b = ring[_tri[i + 1]], c = ring[_tri[i + 2]];
                Vector2 Uv(Vector2 p) => tile > 0f ? p / tile : Vector2.zero;
                var A = new Vector3(a.x, y, a.y); var B = new Vector3(b.x, y, b.y); var Cc = new Vector3(c.x, y, c.y);
                // ring is counter-clockwise from above: a, c, b is clockwise from above, so it faces up
                if (!down) Tri(A, Cc, B, col, tex, Uv(a), Uv(c), Uv(b));
                else Tri(A, B, Cc, col, tex, Uv(a), Uv(b), Uv(c));
            }
        }

        /// <summary>Ear-clipping triangulation of a simple counter-clockwise ring (indices into it).</summary>
        public static void Triangulate(IList<Vector2> ring, List<int> outTris)
        {
            outTris.Clear();
            int n = ring.Count;
            if (n < 3) return;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            int guard = n * n + 10;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    Vector2 a = ring[ia], b = ring[ib], c = ring[ic];
                    if (Cross(b - a, c - b) <= 1e-9f) continue;                 // reflex (or flat) corner
                    bool inside = false;
                    for (int m = 0; m < idx.Count && !inside; m++)
                    {
                        int im = idx[m];
                        if (im == ia || im == ib || im == ic) continue;
                        inside = InTri(ring[im], a, b, c);
                    }
                    if (inside) continue;
                    outTris.Add(ia); outTris.Add(ib); outTris.Add(ic);
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break;                                           // degenerate ring: fan the rest
            }
            for (int k = 1; k + 1 < idx.Count; k++) { outTris.Add(idx[0]); outTris.Add(idx[k]); outTris.Add(idx[k + 1]); }
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f;

        public static Color32 Shade(Color32 c, float k) =>
            new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), c.a);

        public static Color32 Col(float r, float g, float b, float gloss = 0f) =>
            new Color32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), (byte)((1f - gloss) * 255));

        /// <summary>Append another mesh's faces.</summary>
        public void Add(ArchMesh m)
        {
            int b = V.Count;
            V.AddRange(m.V); N.AddRange(m.N); C.AddRange(m.C); UV.AddRange(m.UV);
            foreach (int i in m.T) T.Add(b + i);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(V);
            mesh.SetNormals(N);
            mesh.SetColors(C);
            mesh.SetUVs(0, UV);
            mesh.SetTriangles(T, 0, true);
            return mesh;
        }
    }
}
