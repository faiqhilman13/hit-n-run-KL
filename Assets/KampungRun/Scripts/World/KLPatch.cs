using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// One real-KL district of the map, generated offline from OpenStreetMap by Tools/klmap/patches.py
    /// and stored in Resources/KLMap/&lt;name&gt;.bytes: meshes (roads, pavements, land, river, flyovers,
    /// buildings, trees), the street graph and where it meets the grid, pavement loops, landmark
    /// anchors, river outlines and elevated rail lines. See write_patch() there for the format.
    /// </summary>
    public class KLPatch
    {
        public class MeshData
        {
            public string key;
            public Vector3[] v, n;
            public Vector2[] uv;        // facade walls only: window bays x storeys
            public int[] t;
        }

        public struct Edge { public int a, b; public int dir; }
        public struct Anchor { public string name, model; public Vector2 pos, place; }
        public struct Rail { public string kind; public Vector3[] pts; }

        public string name;
        public Rect rect;
        public readonly List<MeshData> meshes = new List<MeshData>();
        public Vector3[] nodes;
        public bool[] junction;
        public Edge[] edges;
        public int[] links;
        public readonly List<Vector2[]> rings = new List<Vector2[]>();
        public readonly List<byte> ringKinds = new List<byte>();      // 0 pavement, 1 pedestrian street
        public readonly List<Vector4> trees = new List<Vector4>();      // x, z, scale, kind
        public readonly List<Anchor> anchors = new List<Anchor>();
        public readonly List<Vector2[]> water = new List<Vector2[]>();
        public readonly List<Rail> rails = new List<Rail>();

        public static KLPatch Load(string name)
        {
            var ta = Resources.Load<TextAsset>("KLMap/" + name);
            if (ta == null) { Debug.LogError($"[KLMap] missing patch {name}"); return null; }
            using var br = new BinaryReader(new MemoryStream(ta.bytes));
            var magic = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (magic != "KLP5") { Debug.LogError($"[KLMap] {name}: bad format {magic} (rerun Tools/klmap/patches.py)"); return null; }
            var p = new KLPatch { name = Str(br) };
            float x0 = br.ReadSingle(), z0 = br.ReadSingle(), x1 = br.ReadSingle(), z1 = br.ReadSingle();
            p.rect = Rect.MinMaxRect(x0, z0, x1, z1);
            int nm = br.ReadInt32();
            for (int m = 0; m < nm; m++)
            {
                var md = new MeshData { key = Str(br) };
                int nv = br.ReadInt32();
                md.v = V3(br, nv);
                md.n = V3(br, nv);
                if (br.ReadByte() != 0) md.uv = V2(br, nv);
                int nt = br.ReadInt32();
                md.t = new int[nt];
                var tb = br.ReadBytes(nt * 4);
                System.Buffer.BlockCopy(tb, 0, md.t, 0, tb.Length);
                p.meshes.Add(md);
            }
            int nn = br.ReadInt32();
            p.nodes = new Vector3[nn];
            p.junction = new bool[nn];
            for (int i = 0; i < nn; i++)
            {
                p.nodes[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                p.junction[i] = br.ReadByte() != 0;
            }
            int ne = br.ReadInt32();
            p.edges = new Edge[ne];
            for (int i = 0; i < ne; i++) p.edges[i] = new Edge { a = br.ReadInt32(), b = br.ReadInt32(), dir = br.ReadSByte() };
            int nl = br.ReadInt32();
            p.links = new int[nl];
            for (int i = 0; i < nl; i++) p.links[i] = br.ReadInt32();
            int nr = br.ReadInt32();
            for (int i = 0; i < nr; i++)
            {
                int n = br.ReadInt32();
                p.ringKinds.Add(br.ReadByte());
                p.rings.Add(V2(br, n));
            }
            int ntr = br.ReadInt32();
            for (int i = 0; i < ntr; i++) p.trees.Add(new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadByte()));
            int na = br.ReadInt32();
            for (int i = 0; i < na; i++)
            {
                var a = new Anchor { name = Str(br), model = Str(br) };
                a.pos = new Vector2(br.ReadSingle(), br.ReadSingle());
                a.place = new Vector2(br.ReadSingle(), br.ReadSingle());
                p.anchors.Add(a);
            }
            int nw = br.ReadInt32();
            for (int i = 0; i < nw; i++) p.water.Add(V2(br, br.ReadInt32()));
            int nrl = br.ReadInt32();
            for (int i = 0; i < nrl; i++)
            {
                var r = new Rail { kind = Str(br) };
                r.pts = V3(br, br.ReadInt32());
                p.rails.Add(r);
            }
            return p;
        }

        static string Str(BinaryReader br) => Encoding.UTF8.GetString(br.ReadBytes(br.ReadInt32()));

        // little-endian floats straight into the vector arrays (the patches run to a few hundred thousand vertices)
        static Vector3[] V3(BinaryReader br, int n) => System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Vector3>(br.ReadBytes(n * 12)).ToArray();

        static Vector2[] V2(BinaryReader br, int n) => System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Vector2>(br.ReadBytes(n * 8)).ToArray();

        /// <summary>Point-in-polygon for the river outlines.</summary>
        public static bool Inside(Vector2[] ring, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i].y > y) != (ring[j].y > y) && x < (ring[j].x - ring[i].x) * (y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x)
                    inside = !inside;
            return inside;
        }
    }
}
