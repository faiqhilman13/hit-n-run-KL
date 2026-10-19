using System.Collections.Generic;
using UnityEngine;

namespace KampungRun.Arch
{
    /// <summary>What a walk-in room is furnished as.</summary>
    public enum InteriorKind : byte
    {
        Auto,              // let the building decide (a shophouse lot: its sign, else what its shopfront shows)
        Closed,            // never open (shutters down)
        Home,              // Pak Mat's living room
        House,             // any other kampung house's living room
        Kitchen,           // a kampung house's kitchen wing (dapur)
        Runcit,            // kedai runcit: sundry shop
        Kopi,              // kedai kopi / kopitiam / restoran
        Barber,            // kedai gunting rambut
        Hardware,          // kedai perkakas
        Textile,           // kedai kain / tailor
        Gold,              // kedai emas
        Mamak,             // the open-sided mamak (its own model is the room: furniture only)
    }

    /// <summary>A hole in one of a room's walls, in room coordinates: along x for the front and back walls,
    /// along the depth (0 at the front, D at the back) for the side walls.</summary>
    public struct RoomOpening
    {
        public int wall;            // 0 front, 1 back, 2 left (x = 0), 3 right (x = W)
        public float c0, c1, y0, y1;
        public bool door;           // a way through (nothing painted in it)
        public bool folding;        // folding shop doors stacked open at its sides
        public float reveal;        // a door's reveal back through the wall's thickness
        public ArchTex tex;         // a window's (or painted door's) picture, seen from inside
        public Color32 col;
    }

    /// <summary>Somewhere a shopkeeper stands, and the way they face.</summary>
    public struct Spot
    {
        public Vector3 pos;
        public Vector3 facing;
    }

    /// <summary>A box physics should know about (furniture), in world space.</summary>
    public struct SolidBox
    {
        public Vector3 centre, size;
        public Quaternion rot;
    }

    /// <summary>
    /// One walk-in room. Its frame's origin is the inner box's front-left floor corner (left as seen from the street),
    /// x runs across (W), y up (H), and n points out of the front toward the street, so the room is z in [-D, 0]
    /// (n = up x r, the same handedness as Frame.Edge).
    /// </summary>
    public class RoomSpec
    {
        public InteriorKind kind;
        public Frame f;
        public float W, D, H;
        public int seed;
        public bool shell = true;                   // draw its walls, floor and ceiling (false: the building has its own)
        public readonly List<RoomOpening> openings = new List<RoomOpening>();
        public Color32 wall;                        // inside wall paint (0 alpha: the kind's)
        public string name;

        public Vector3 Centre => f.P(W * 0.5f, 0f, -D * 0.5f);
        public float FloorY => f.o.y;
        public float CeilingY => f.o.y + H;

        /// <summary>Is a point in the room (feet between the floor and the ceiling)? margin grows (or, negative, shrinks) the box.</summary>
        public bool Contains(Vector3 p, float margin = 0f)
        {
            var d = p - f.o;
            float x = Vector3.Dot(d, f.r), y = Vector3.Dot(d, f.u), z = -Vector3.Dot(d, f.n);
            return x > -margin && x < W + margin && z > -margin && z < D + margin && y > -0.7f && y < H;
        }
    }

    /// <summary>
    /// Walk-in interiors (vertical slice: Pak Mat's house, the Chow Kit shops; then every open shophouse lot and every
    /// kampung house). The shell of each room (walls with their doorways and windows, floor, ceiling) is modelled with the
    /// building's own near-detail version, so it's there whenever the street is (ArchCity); its furniture, solids and
    /// shopkeeper are built by Interiors only when the player is close. Rooms are lit by LatInk's interior branch
    /// (ArchMesh.Inside): warm lamplight, whatever the sun and CityShade say.
    /// Seeds come from the building's own seed (hashed), never from CityBuilder's dice.
    /// </summary>
    public static class Interior
    {
        /// <summary>Off: no building has rooms (shopfronts painted shut, houses solid, as before; captures compare).</summary>
        public static bool Enabled = true;
        public const float Wall = 0.2f;              // inner walls stand this far in from a shopfront
        const float Side = 0.12f;                    // ...and in from a lot's party lines

        static Color32 C(float r, float g, float b, float gloss = 0f) => ArchMesh.Col(r, g, b, gloss);
        static readonly Color32 White = C(1f, 1f, 1f);
        static readonly Color32 Wood = C(0.60f, 0.42f, 0.27f), WoodDark = C(0.42f, 0.28f, 0.18f), WoodLight = C(0.80f, 0.64f, 0.44f);
        static readonly Color32 Steel = C(0.74f, 0.76f, 0.80f, 0.5f), Chrome = C(0.86f, 0.88f, 0.92f, 0.8f), Black = C(0.16f, 0.16f, 0.18f);
        static readonly Color32 Marble = C(0.96f, 0.96f, 0.94f, 0.6f), Glass = C(0.86f, 0.94f, 1f, 0.8f), Lamp = C(1f, 1f, 0.94f);
        static readonly Color32 Reveal = C(0.86f, 0.84f, 0.80f);

        // ------------------------------------------------------------------------------------ which rooms a building has
        static readonly List<Shophouse.BayPlan> _bays = new List<Shophouse.BayPlan>(8);

        /// <summary>A shophouse sign (Signs0..3, 4 a cell, top first: gen_arch_atlas.py SIGNS) -> the shop behind it.</summary>
        static readonly InteriorKind[] SignShop =
        {
            InteriorKind.Kopi, InteriorKind.Hardware, InteriorKind.Runcit, InteriorKind.Textile,      // kopitiam, perkakas, ubat, kain
            InteriorKind.Kopi, InteriorKind.Runcit, InteriorKind.Kopi, InteriorKind.Gold,             // restoran, runcit, restoran, emas
            InteriorKind.Runcit, InteriorKind.Kopi, InteriorKind.Barber, InteriorKind.Runcit,         // farmasi, nasi kandar, gunting rambut, runcit
            InteriorKind.Hardware, InteriorKind.Textile, InteriorKind.Kopi, InteriorKind.Textile,     // perabot, jahit, restoran, tekstil
        };

        /// <summary>The sign to hang over a shop of a kind (for lots told what to be).</summary>
        public static int SignFor(InteriorKind k) =>
            k == InteriorKind.Runcit ? 5 : k == InteriorKind.Kopi ? 0 : k == InteriorKind.Barber ? 10 : k == InteriorKind.Hardware ? 1 :
            k == InteriorKind.Textile ? 15 : k == InteriorKind.Gold ? 7 : -1;

        static int Hash(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u ^ (uint)salt * 40503u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (int)(h & 0x7fffffff);
            }
        }

        static InteriorKind KindFor(BuildingSpec s, Shophouse.BayPlan b, int bay)
        {
            if (!b.shop || s.interior == InteriorKind.Closed) return InteriorKind.Closed;
            if (s.interior != InteriorKind.Auto) return s.interior;
            if (b.back == ArchTex.Roller) return InteriorKind.Closed;                       // shutters down
            if (b.lv >= 2) return SignShop[b.sign & 15];                                    // the sign says what it is
            switch (b.back)
            {
                case ArchTex.Kopitiam: return InteriorKind.Kopi;
                case ArchTex.Hardware: return InteriorKind.Hardware;
                case ArchTex.Textile: return InteriorKind.Textile;
                case ArchTex.GoldShop: return InteriorKind.Gold;
                case ArchTex.GlassShop: return InteriorKind.Runcit;
                default:
                    var pick = new[] { InteriorKind.Runcit, InteriorKind.Kopi, InteriorKind.Barber, InteriorKind.Hardware, InteriorKind.Runcit, InteriorKind.Textile };
                    return pick[Hash(s.seed, 11 + bay) % pick.Length];
            }
        }

        /// <summary>
        /// The walk-in rooms of a shophouse: one per open ground-floor bay, behind its shopfront, as deep as the
        /// footprint allows (up to 12 m). Worked out once and kept on the spec (the facade, physics and the
        /// streamer all ask).
        /// </summary>
        public static List<RoomSpec> RoomsOf(BuildingSpec s)
        {
            if (s.rooms != null) return s.rooms;
            s.rooms = new List<RoomSpec>();
            if (!Enabled || s.kind != BuildingKind.Shop || s.interior == InteriorKind.Closed || s.ring == null || s.ring.Length < 3) return s.rooms;
            Shophouse.Plan(s, _bays, out int front, out float G, out float A);
            var ring = s.ring;
            var f = Frame.Edge(ring[front], ring[(front + 1) % ring.Length], s.y0);
            for (int i = 0; i < _bays.Count; i++)
            {
                var b = _bays[i];
                var kind = KindFor(s, b, i);
                if (kind == InteriorKind.Closed) continue;
                float depth = Mathf.Min(12f, UsableDepth(ring, f, b.x0 + Side + 0.05f, b.x0 + b.w - Side - 0.05f, A));
                float D = depth - Wall - 0.2f, W = b.w - 2f * Side, H = G - 0.17f;
                if (D < 3.2f || W < 3.0f || H < 2.6f) continue;
                var r = new RoomSpec
                {
                    kind = kind, seed = Hash(s.seed, 101 + i), W = W, D = D, H = H,
                    f = new Frame(f.P(b.x0 + Side, 0.02f, -A - Wall), f.r, f.u, f.n),
                };
                // the shopfront's openings (Shophouse.Ground), folding doors stacked open where it had them
                int nOpen = b.w > 5.6f ? 2 : 1;
                float pier = 0.32f, ow = (b.w - pier * (nOpen + 1)) / nOpen, doorTop = Mathf.Min(3.0f, G - 0.9f);
                for (int k = 0; k < nOpen; k++)
                {
                    float x0 = pier + k * (ow + pier) - Side;
                    var back = k == 0 ? b.back : b.back2;
                    r.openings.Add(new RoomOpening { wall = 0, c0 = x0, c1 = x0 + ow, y0 = 0f, y1 = doorTop - 0.02f, door = true, folding = back == ArchTex.Folding });
                }
                // a back door (painted shut) to the lane
                float bd = (Hash(r.seed, 3) & 1) == 0 ? 0.5f : W - 1.5f;
                r.openings.Add(new RoomOpening { wall = 1, c0 = bd, c1 = bd + 1f, y0 = 0f, y1 = 2.1f, tex = ArchTex.Door, col = WoodDark });
                s.rooms.Add(r);
            }
            return s.rooms;
        }

        /// <summary>How far in from the shopfront (x0..x1 along the front edge, set back by A) the footprint goes:
        /// the shortest of three rays straight in.</summary>
        static float UsableDepth(Vector2[] ring, in Frame f, float x0, float x1, float A)
        {
            var dir = new Vector2(-f.n.x, -f.n.z);
            float best = float.MaxValue;
            foreach (float x in new[] { x0, (x0 + x1) * 0.5f, x1 })
            {
                var p3 = f.P(x, 0f, -A + 0.01f);
                var p = new Vector2(p3.x, p3.z);
                float hit = float.MaxValue;
                for (int i = 0; i < ring.Length; i++)
                {
                    Vector2 a = ring[i], b = ring[(i + 1) % ring.Length], e = b - a;
                    float den = dir.x * e.y - dir.y * e.x;
                    if (Mathf.Abs(den) < 1e-6f) continue;
                    var ap = a - p;
                    float t = (ap.x * e.y - ap.y * e.x) / den, u = (ap.x * dir.y - ap.y * dir.x) / den;
                    if (t > 0.05f && u >= -1e-4f && u <= 1f + 1e-4f) hit = Mathf.Min(hit, t);
                }
                best = Mathf.Min(best, hit);
            }
            return best == float.MaxValue ? 0f : best;
        }

        /// <summary>A kampung house's rooms: the living room in its body and, if it has one, the kitchen wing behind.</summary>
        public static List<RoomSpec> RoomsOf(HouseSpec h)
        {
            if (h.rooms != null) return h.rooms;
            h.rooms = new List<RoomSpec>();
            if (Enabled) KampungHouse.Rooms(h, h.rooms);
            return h.rooms;
        }

        // ------------------------------------------------------------------------------------ the shell
        struct Look
        {
            public Color32 wall, dado, floor, ceiling;
            public ArchTex floorTex, ceilTex, dadoTex;
            public float floorTile, dadoH;
        }

        static Look LookOf(RoomSpec r)
        {
            var k = new Look { floorTex = ArchTex.Floor, floorTile = 1.0f, ceilTex = ArchTex.White, ceiling = C(0.95f, 0.94f, 0.90f), dadoTex = ArchTex.Floor, dadoH = 0f };
            switch (r.kind)
            {
                case InteriorKind.Runcit: k.wall = C(0.84f, 0.93f, 0.84f); k.floor = C(0.95f, 0.92f, 0.86f, 0.3f); break;
                case InteriorKind.Kopi: k.wall = C(0.99f, 0.92f, 0.76f); k.floor = C(0.92f, 0.88f, 0.82f, 0.3f); k.dado = C(0.56f, 0.80f, 0.68f, 0.4f); k.dadoH = 1.3f; break;
                case InteriorKind.Barber: k.wall = C(0.78f, 0.88f, 0.97f); k.floor = C(0.92f, 0.92f, 0.94f, 0.4f); k.dado = C(0.95f, 0.95f, 0.97f, 0.4f); k.dadoH = 1.0f; break;
                case InteriorKind.Hardware: k.wall = C(0.90f, 0.88f, 0.80f); k.floor = C(0.76f, 0.75f, 0.72f); k.floorTex = ArchTex.Concrete; k.floorTile = 2.5f; break;
                case InteriorKind.Textile: k.wall = C(0.99f, 0.86f, 0.78f); k.floor = C(0.94f, 0.90f, 0.84f, 0.3f); break;
                case InteriorKind.Gold: k.wall = C(0.99f, 0.93f, 0.80f); k.floor = C(0.97f, 0.95f, 0.92f, 0.6f); k.dado = C(0.72f, 0.14f, 0.14f); k.dadoTex = ArchTex.White; k.dadoH = 1.1f; break;
                case InteriorKind.Kitchen:
                    k.wall = C(0.97f, 0.95f, 0.88f); k.floor = C(0.86f, 0.84f, 0.80f, 0.3f); k.dado = C(0.92f, 0.95f, 0.96f, 0.4f); k.dadoH = 1.2f;
                    k.ceilTex = ArchTex.PlanksV; k.ceiling = C(0.96f, 0.95f, 0.90f); break;
                default:                                                  // a kampung house's living room: planked floor and ceiling
                    k.wall = C(0.82f, 0.93f, 0.88f); k.floor = C(0.78f, 0.58f, 0.40f, 0.2f); k.floorTex = ArchTex.PlanksV; k.floorTile = 1.2f;
                    k.ceilTex = ArchTex.PlanksV; k.ceiling = C(0.97f, 0.96f, 0.91f); break;
            }
            if (r.wall.a > 0) k.wall = r.wall;
            return k;
        }

        static readonly List<Opening> _holes = new List<Opening>(8);

        /// <summary>A wall's inner face: its frame (x along the wall as seen from inside, n into the room) and length.</summary>
        static Frame WallFrame(RoomSpec r, int wall, out float len)
        {
            var f = r.f;
            switch (wall)
            {
                case 0: len = r.W; return new Frame(f.P(r.W, 0f, 0f), -f.r, f.u, -f.n);
                case 1: len = r.W; return new Frame(f.P(0f, 0f, -r.D), f.r, f.u, f.n);
                case 2: len = r.D; return new Frame(f.P(0f, 0f, 0f), -f.n, f.u, f.r);
                default: len = r.D; return new Frame(f.P(r.W, 0f, -r.D), f.n, f.u, -f.r);
            }
        }

        /// <summary>Room coordinate along a wall -> the wall's own x (as seen from inside).</summary>
        static float AlongWall(RoomSpec r, int wall, float c) => wall == 0 ? r.W - c : wall == 3 ? r.D - c : c;

        /// <summary>The room's walls, floor and ceiling (inward-facing, lit as an interior), with its doorways and windows.</summary>
        public static void Shell(ArchMesh m, RoomSpec r)
        {
            if (!r.shell) return;
            var look = LookOf(r);
            bool was = m.Inside;
            m.Inside = true;
            int v0 = m.Count;
            var f = r.f;
            float W = r.W, D = r.D, H = r.H;
            m.QuadFacing(f.P(0f, 0f, -D), f.P(W, 0f, -D), f.P(W, 0f, 0f), f.P(0f, 0f, 0f), f.u, look.floor, look.floorTex, 0f, 0f, W / look.floorTile, D / look.floorTile);
            m.QuadFacing(f.P(0f, H, 0f), f.P(W, H, 0f), f.P(W, H, -D), f.P(0f, H, -D), -f.u, look.ceiling, look.ceilTex, 0f, 0f, W / 1.2f, D / 1.2f);
            for (int w = 0; w < 4; w++)
            {
                var wf = WallFrame(r, w, out float len);
                _holes.Clear();
                bool anyHole = false;
                foreach (var o in r.openings)
                {
                    if (o.wall != w) continue;
                    float a = AlongWall(r, w, o.c0), b = AlongWall(r, w, o.c1);
                    var h = new Opening(Mathf.Min(a, b), Mathf.Max(a, b), o.y0, o.y1, o.door ? o.reveal : 0.04f, o.tex, o.col, Reveal) { open = o.door };
                    _holes.Add(h);
                    anyHole = true;
                }
                m.Panel(wf, 0f, len, 0f, H, 0f, look.wall, ArchTex.White, 0f, _holes);
                // a tiled dado on the walls without a way through them, a skirting on all
                if (look.dadoH > 0f && !anyHole) m.Rect(wf, 0f, len, 0f, look.dadoH, 0.006f, look.dado, look.dadoTex, look.dadoTex == ArchTex.White ? 0f : 0.6f);
                if (look.dadoH > 0f && !anyHole) m.Box(wf, 0f, len, look.dadoH - 0.03f, look.dadoH + 0.03f, 0f, 0.025f, ArchMesh.Shade(look.dado, 0.8f), Faces.Front | Faces.Top);
                float run = 0f;
                _holes.Sort((p, q) => p.x0.CompareTo(q.x0));
                foreach (var h in _holes)
                {
                    if (h.y0 < 0.1f) { if (h.x0 > run) m.Box(wf, run, h.x0, 0f, 0.1f, 0f, 0.015f, WoodDark, Faces.Front | Faces.Top); run = h.x1; }
                }
                if (len > run) m.Box(wf, run, len, 0f, 0.1f, 0f, 0.015f, WoodDark, Faces.Front | Faces.Top);
                // folding shop doors stacked open at each side of a shopfront opening
                foreach (var o in r.openings)
                {
                    if (o.wall != w || !o.folding) continue;
                    float a = AlongWall(r, w, o.c0), b = AlongWall(r, w, o.c1);
                    float x0 = Mathf.Min(a, b), x1 = Mathf.Max(a, b);
                    var leaf = C(0.62f, 0.18f, 0.16f);
                    foreach (var (s0, s1) in new[] { (x0, x0 + 0.3f), (x1 - 0.3f, x1) })
                    {
                        m.Box(wf, s0, s1, 0f, o.y1, 0.02f, 0.14f, leaf, ArchTex.Folding, Faces.Front | Faces.Back | Faces.Left | Faces.Right);
                    }
                }
            }
            // baked lamp shade: walls a little darker toward the floor
            for (int i = v0; i < m.Count; i++)
            {
                if (Mathf.Abs(m.N[i].y) > 0.5f) continue;
                float y = Vector3.Dot(m.V[i] - f.o, f.u);
                m.C[i] = ArchMesh.Shade(m.C[i], Mathf.Lerp(0.8f, 1.03f, Mathf.Clamp01(y / H)));
            }
            m.Inside = was;
        }

        // ------------------------------------------------------------------------------------ physics
        /// <summary>The solid walls of a room (its inner faces, 0.14 m thick going outward), leaving its doorways open.
        /// The front wall is left to the building (it knows how far its shopfront is from the room).</summary>
        public static void RoomWalls(RoomSpec r, List<Vector3> v, List<int> t, bool front, float thick = 0.14f)
        {
            if (!r.shell) return;
            for (int w = front ? 0 : 1; w < 4; w++)
            {
                var wf = WallFrame(r, w, out float len);
                float run = 0f;
                _gaps.Clear();
                foreach (var o in r.openings)
                {
                    if (o.wall != w || !o.door) continue;
                    float a = AlongWall(r, w, o.c0), b = AlongWall(r, w, o.c1);
                    float g0 = Mathf.Min(a, b), g1 = Mathf.Max(a, b);
                    if (o.folding) { g0 += 0.3f; g1 -= 0.3f; }
                    _gaps.Add((g0, g1, o.y0, o.y1));
                }
                _gaps.Sort((p, q) => p.Item1.CompareTo(q.Item1));
                foreach (var (g0, g1, y0, y1) in _gaps)
                {
                    if (g0 > run) Solid.Box(v, t, wf, run - thick, g0, -0.3f, r.H + 0.05f, -thick, 0f);
                    if (y0 > 0.02f) Solid.Box(v, t, wf, g0, g1, -0.3f, y0, -thick, 0f);              // a sill or step under the doorway
                    Solid.Box(v, t, wf, g0, g1, y1, r.H + 0.05f, -thick, 0f);                         // over the doorway
                    run = g1;
                }
                Solid.Box(v, t, wf, run - (run > 0f ? 0f : thick), len + thick, -0.3f, r.H + 0.05f, -thick, 0f);
            }
        }

        static readonly List<(float, float, float, float)> _gaps = new List<(float, float, float, float)>(4);

        /// <summary>Pedestrians stay out: a thin box across each street doorway, for the DoorWards collider (a layer the
        /// player, cars and camera ignore; pedestrians walk by raycasts, which see it).</summary>
        public static void DoorWards(RoomSpec r, List<Vector3> v, List<int> t)
        {
            if (!r.shell) return;
            var wf = WallFrame(r, 0, out _);
            foreach (var o in r.openings)
            {
                if (o.wall != 0 || !o.door) continue;
                float a = AlongWall(r, 0, o.c0), b = AlongWall(r, 0, o.c1);
                Solid.Box(v, t, wf, Mathf.Min(a, b), Mathf.Max(a, b), o.y0, o.y1, -0.1f, -0.04f);
            }
        }

        // ------------------------------------------------------------------------------------ furniture
        /// <summary>A room's furniture into a mesh (world space, lit as an interior), its solids and a shopkeeper's spot.</summary>
        public static bool Furnish(RoomSpec r, ArchMesh m, List<SolidBox> solids, out Spot keeper)
        {
            var fit = new Fit { m = m, r = r, solids = solids, rng = new System.Random(r.seed) };
            keeper = default;
            bool was = m.Inside;
            m.Inside = true;
            bool hasKeeper = true;
            switch (r.kind)
            {
                case InteriorKind.Runcit: fit.Runcit(out keeper); break;
                case InteriorKind.Kopi: fit.Kopi(out keeper); break;
                case InteriorKind.Barber: fit.Barber(out keeper); break;
                case InteriorKind.Hardware: fit.Hardware(out keeper); break;
                case InteriorKind.Textile: fit.Textile(out keeper); break;
                case InteriorKind.Gold: fit.Gold(out keeper); break;
                case InteriorKind.Home: fit.Living(true); hasKeeper = false; break;
                case InteriorKind.House: fit.Living(false); hasKeeper = false; break;
                case InteriorKind.Kitchen: fit.Kitchen(); hasKeeper = false; break;
                case InteriorKind.Mamak: fit.Mamak(); hasKeeper = false; break;
                default: hasKeeper = false; break;
            }
            m.Inside = was;
            return hasKeeper;
        }

        /// <summary>The furniture builder: room coordinates are x across (0..W), y up, d in from the front (0..D).</summary>
        sealed class Fit
        {
            public ArchMesh m;
            public RoomSpec r;
            public List<SolidBox> solids;
            public System.Random rng;

            float W => r.W;
            float D => r.D;
            float H => r.H;
            Vector3 P(float x, float y, float d) => r.f.P(x, y, -d);
            Vector3 Fw => r.f.n;          // toward the street
            Vector3 Bk => -r.f.n;         // toward the back
            Vector3 Rt => r.f.r;          // toward x = W
            Vector3 Lf => -r.f.r;         // toward x = 0
            float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            T Pick<T>(params T[] from) => from[rng.Next(from.Length)];

            static readonly Color32[] Brights = { C(0.86f, 0.22f, 0.20f), C(0.20f, 0.46f, 0.82f), C(0.98f, 0.78f, 0.20f), C(0.24f, 0.66f, 0.38f), C(0.94f, 0.50f, 0.18f), C(0.62f, 0.34f, 0.74f) };

            /// <summary>A box x0..x1, y0..y1, d0..d1. Faces.Front faces the street, Back the back wall, Left x = 0, Right x = W.</summary>
            public void Box(float x0, float x1, float y0, float y1, float d0, float d1, Color32 c, ArchTex t = ArchTex.White, Faces faces = Faces.All, bool solid = false, float tile = 0f)
            {
                m.Box(r.f, x0, x1, y0, y1, -d1, -d0, c, t, faces, tile);
                if (solid) Solid(x0, x1, y0, y1, d0, d1);
            }

            public void Solid(float x0, float x1, float y0, float y1, float d0, float d1)
            {
                // nothing low enough to step up onto (the CharacterController steps 0.45 m): tables and beds stay floor
                if (y0 < 0.05f) y1 = Mathf.Max(y1, 0.6f);
                solids.Add(new SolidBox
                {
                    centre = P((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (d0 + d1) * 0.5f),
                    rot = Quaternion.LookRotation(r.f.n, r.f.u),
                    size = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(d1 - d0)),
                });
            }

            /// <summary>A flat picture facing N, centred at (x, y, d).</summary>
            public void Decal(float x, float y, float d, Vector3 N, float w, float h, Color32 col, ArchTex tex, float u0 = 0f, float v0 = 0f, float u1 = 1f, float v1 = 1f)
            {
                var c = P(x, y, d) + N * 0.006f;
                // the picture's up: up the wall, or (on a floor) toward the back; its right makes up x right face N
                var up = Mathf.Abs(N.y) > 0.5f ? Bk : Vector3.up;
                var right = Vector3.Cross(N, up).normalized;
                var a = c - right * w * 0.5f - up * h * 0.5f;
                m.Quad(a, a + right * w, a + right * w + up * h, a + up * h, col, tex, u0, v0, u1, v1);
            }

            public void Cyl(float x, float y, float d, float rad, float h, int sides, Color32 col, ArchTex tex = ArchTex.White, bool top = true) =>
                m.Cylinder(P(x, y, d), rad, h, sides, col, tex, top);

            /// <summary>A picture on a wall: wall 0 front, 1 back, 2 left, 3 right; c along it (x, or d for the sides).</summary>
            public void OnWall(int wall, float c, float y, float w, float h, Color32 col, ArchTex tex, float u0 = 0f, float v0 = 0f, float u1 = 1f, float v1 = 1f)
            {
                c = Free(wall, c, w, y - h * 0.5f, y + h * 0.5f);
                if (float.IsNaN(c)) return;
                switch (wall)
                {
                    case 0: Decal(c, y, 0.01f, Bk, w, h, col, tex, u0, v0, u1, v1); break;
                    case 1: Decal(c, y, D - 0.01f, Fw, w, h, col, tex, u0, v0, u1, v1); break;
                    case 2: Decal(0.01f, y, c, Rt, w, h, col, tex, u0, v0, u1, v1); break;
                    default: Decal(W - 0.01f, y, c, Lf, w, h, col, tex, u0, v0, u1, v1); break;
                }
            }

            /// <summary>Is the strip in front of the shopfront's doorways (for the first 1.4 m) clear at x0..x1?</summary>
            bool ClearOfDoors(float x0, float x1, float d0)
            {
                if (d0 > 1.4f) return true;
                foreach (var o in r.openings)
                    if (o.wall == 0 && o.door && x1 > o.c0 - 0.1f && x0 < o.c1 + 0.1f) return false;
                return true;
            }

            // ---------------------------------------------------------------- shared pieces
            /// <summary>A shelf unit against a wall, open on its `open` side (a direction), goods painted on its shelves.</summary>
            public void Shelf(float x0, float x1, float d0, float d1, float h, Vector3 open, ArchTex goods, Color32 carcass, float strip = 0.82f)
            {
                Box(x0, x1, 0f, h, d0, d1, carcass, ArchTex.White, Faces.All & ~Faces.Bottom, true);
                bool alongD = Mathf.Abs(Vector3.Dot(open, Rt)) > 0.5f;              // opens to a side: runs along the depth
                float len = alongD ? d1 - d0 : x1 - x0;
                float fx = Vector3.Dot(open, Rt) > 0.5f ? x1 : Vector3.Dot(open, Lf) > 0.5f ? x0 : (x0 + x1) * 0.5f;
                float fd = Vector3.Dot(open, Fw) > 0.5f ? d0 : Vector3.Dot(open, Bk) > 0.5f ? d1 : (d0 + d1) * 0.5f;
                int k = 0;
                for (float y = 0.12f; y + strip * 0.5f < h - 0.05f; y += strip, k++)
                {
                    float sh = Mathf.Min(strip, h - 0.06f - y);
                    float vv = goods == ArchTex.Goods ? ((k + (int)(x0 * 3f)) % 2 == 0 ? 0.5f : 0f) : 0f;
                    float vh = goods == ArchTex.Goods ? 0.5f * sh / strip : sh / 1.4f;
                    Decal(fx, y + sh * 0.5f, fd, open, len - 0.06f, sh, White, goods, 0f, vv, len / 1.4f, vv + vh);
                    // the shelf's front edge
                    if (alongD)
                    {
                        float e0 = Vector3.Dot(open, Rt) > 0f ? fx : fx - 0.03f;
                        Box(e0, e0 + 0.03f, y - 0.04f, y, d0, d1, ArchMesh.Shade(carcass, 0.85f), ArchTex.White, Faces.Front | Faces.Back | Faces.Top | Faces.Right | Faces.Left);
                    }
                    else Box(x0, x1, y - 0.04f, y, fd - 0.03f, fd + 0.03f, ArchMesh.Shade(carcass, 0.85f), ArchTex.White, Faces.Front | Faces.Back | Faces.Top);
                }
            }

            public void Fridge(float x0, float d0, Vector3 open)
            {
                bool side = Mathf.Abs(Vector3.Dot(open, Rt)) > 0.5f;
                float x1 = x0 + (side ? 0.7f : 0.8f), d1 = d0 + (side ? 0.8f : 0.7f);
                Box(x0, x1, 0f, 1.95f, d0, d1, C(0.94f, 0.94f, 0.95f, 0.4f), ArchTex.White, Faces.All & ~Faces.Bottom, true);
                float fx = Vector3.Dot(open, Rt) > 0.5f ? x1 : Vector3.Dot(open, Lf) > 0.5f ? x0 : (x0 + x1) * 0.5f;
                float fd = Vector3.Dot(open, Fw) > 0.5f ? d0 : Vector3.Dot(open, Bk) > 0.5f ? d1 : (d0 + d1) * 0.5f;
                Decal(fx, 1.0f, fd, open, side ? 0.74f : 0.74f, 1.86f, White, ArchTex.Fridge);
            }

            /// <summary>Somewhere along a wall (near `want`) for a thing w wide spanning y0..y1 that keeps clear of the wall's
            /// doors and windows; NaN if there's nowhere.</summary>
            public float Free(int wall, float want, float w, float y0, float y1)
            {
                float len = wall < 2 ? W : D;
                for (int k = 0; k < 24; k++)
                {
                    float c = want + (k == 0 ? 0f : ((k + 1) / 2) * 0.25f * (k % 2 == 0 ? 1f : -1f));
                    if (c - w * 0.5f < 0.08f || c + w * 0.5f > len - 0.08f) continue;
                    bool clear = true;
                    foreach (var o in r.openings)
                        if (o.wall == wall && c + w * 0.5f > o.c0 - 0.1f && c - w * 0.5f < o.c1 + 0.1f && y1 > o.y0 - 0.05f && y0 < o.y1 + 0.12f) { clear = false; break; }
                    if (clear) return c;
                }
                return float.NaN;
            }

            public void Fan(float x, float d)
            {
                // hung close under the ceiling, short blades: the chase camera works in the space below it
                float y = H - 0.02f, len = Mathf.Min(0.62f, W * 0.13f);
                Cyl(x, y - 0.14f, d, 0.02f, 0.14f, 4, Steel, ArchTex.White, false);
                Cyl(x, y - 0.24f, d, 0.12f, 0.11f, 8, C(0.95f, 0.95f, 0.93f));
                var hub = P(x, y - 0.18f, d);
                float spin = (float)rng.NextDouble() * 120f;
                for (int b = 0; b < 3; b++)
                {
                    var dir = Quaternion.AngleAxis(spin + b * 120f, Vector3.up) * r.f.r;
                    var side = Vector3.Cross(Vector3.up, dir) * 0.07f;
                    Vector3 a = hub + dir * 0.12f, e = hub + dir * len;
                    m.QuadFacing(a - side, e - side, e + side, a + side, Vector3.up, C(0.70f, 0.52f, 0.34f), ArchTex.White);
                    m.QuadFacing(a - side, e - side, e + side, a + side, Vector3.down, C(0.62f, 0.46f, 0.30f), ArchTex.White);
                }
            }

            public void Tube(float x, float d, bool alongD = true)
            {
                float y = H - 0.06f;
                if (alongD) Box(x - 0.06f, x + 0.06f, y - 0.06f, y, d - 0.6f, d + 0.6f, Lamp, ArchTex.White, Faces.All & ~Faces.Top);
                else Box(x - 0.6f, x + 0.6f, y - 0.06f, y, d - 0.06f, d + 0.06f, Lamp, ArchTex.White, Faces.All & ~Faces.Top);
            }

            /// <summary>A counter (the shop's till side): a body, a top, a picture on its customer face.</summary>
            public void Counter(float x0, float x1, float d0, float d1, Vector3 customer, Color32 body, ArchTex face = ArchTex.White, Color32? top = null)
            {
                Box(x0, x1, 0f, 0.92f, d0, d1, body, ArchTex.White, Faces.All & ~Faces.Bottom, true);
                Box(x0 - 0.03f, x1 + 0.03f, 0.92f, 0.97f, d0 - 0.03f, d1 + 0.03f, top ?? Marble, ArchTex.White, Faces.All & ~Faces.Bottom);
                if (face != ArchTex.White)
                {
                    bool side = Mathf.Abs(Vector3.Dot(customer, Rt)) > 0.5f;
                    float fx = Vector3.Dot(customer, Rt) > 0.5f ? x1 : Vector3.Dot(customer, Lf) > 0.5f ? x0 : (x0 + x1) * 0.5f;
                    float fd = Vector3.Dot(customer, Fw) > 0.5f ? d0 : Vector3.Dot(customer, Bk) > 0.5f ? d1 : (d0 + d1) * 0.5f;
                    float len = side ? d1 - d0 : x1 - x0;
                    Decal(fx, 0.5f, fd, customer, len - 0.1f, 0.7f, White, face, 0f, 0f, len / 1.2f, 1f);
                }
            }

            public void Till(float x, float d, Vector3 face)
            {
                Box(x - 0.2f, x + 0.2f, 0.97f, 1.12f, d - 0.18f, d + 0.18f, C(0.30f, 0.30f, 0.34f));
                Box(x - 0.12f, x + 0.12f, 1.12f, 1.3f, d - 0.05f, d + 0.05f, C(0.22f, 0.22f, 0.26f));
                Decal(x, 1.21f, d, face, 0.2f, 0.12f, C(0.4f, 0.95f, 0.5f), ArchTex.White);
            }

            public void Stool(float x, float d, Color32 c)
            {
                Cyl(x, 0f, d, 0.03f, 0.42f, 4, Steel, ArchTex.White, false);
                Cyl(x, 0.42f, d, 0.17f, 0.06f, 8, c);
            }

            public void RoundTable(float x, float d)
            {
                Cyl(x, 0f, d, 0.06f, 0.72f, 6, Black, ArchTex.White, false);
                Cyl(x, 0f, d, 0.25f, 0.03f, 8, Black);
                Cyl(x, 0.72f, d, 0.42f, 0.04f, 12, Marble);
                Solid(x - 0.36f, x + 0.36f, 0f, 0.76f, d - 0.36f, d + 0.36f);
            }

            public void Calendar(int wall, float c, float y) => OnWall(wall, c, y, 0.42f, 0.5f, White, ArchTex.Screens, 0f, 0f, 0.5f, 0.5f);

            Spot Keeper(float x, float d, Vector3 facing) => new Spot { pos = P(x, 0f, d), facing = facing };

            // ---------------------------------------------------------------- the shops
            public void Runcit(out Spot keeper)
            {
                var shelfCol = Pick(C(0.62f, 0.44f, 0.27f), C(0.88f, 0.88f, 0.86f), C(0.36f, 0.56f, 0.72f));
                // the counter and till inside the door on the right, the keeper behind it, sundries on the wall behind him
                Counter(W - 1.6f, W - 1.0f, 0.5f, 2.4f, Lf, C(0.80f, 0.32f, 0.26f), ArchTex.GlassShop);
                Till(W - 1.3f, 1.0f, Lf);
                Box(W - 1.5f, W - 1.1f, 0.97f, 1.25f, 1.6f, 2.2f, Glass, ArchTex.GlassShop);                // a sweets jar case
                Shelf(W - 0.32f, W, 0.3f, 2.6f, 2.3f, Lf, ArchTex.Goods, shelfCol, 0.6f);
                keeper = Keeper(W - 0.62f, 1.5f, Lf);
                // wall shelves down both sides, a fridge at the back left, an island down the middle if it's wide enough
                Shelf(0f, 0.45f, 1.6f, D - 1.0f, 2.1f, Rt, ArchTex.Goods, shelfCol);
                Fridge(0.02f, D - 0.86f, Rt);
                Shelf(W - 0.45f, W, 3.0f, D - 0.05f, 2.1f, Lf, ArchTex.Goods, shelfCol);
                float iz0 = 3.4f, iz1 = D - 1.5f;
                if (W >= 4.2f && iz1 - iz0 > 1.4f)
                {
                    float cx = W * 0.5f - 0.15f;
                    Box(cx - 0.36f, cx + 0.36f, 0f, 1.35f, iz0, iz1, shelfCol, ArchTex.White, Faces.All & ~Faces.Bottom, true);
                    Decal(cx - 0.36f, 0.78f, (iz0 + iz1) * 0.5f, Lf, iz1 - iz0 - 0.06f, 0.95f, White, ArchTex.Goods, 0f, 0.5f, (iz1 - iz0) / 1.4f, 1f);
                    Decal(cx + 0.36f, 0.78f, (iz0 + iz1) * 0.5f, Rt, iz1 - iz0 - 0.06f, 0.95f, White, ArchTex.Goods, 0f, 0f, (iz1 - iz0) / 1.4f, 0.5f);
                }
                // rice sacks and egg trays by the door, a basket of onions
                for (int k = 0; k < 3; k++)
                    Box(0.15f + k * 0.02f, 0.75f, 0.0f + k * 0.26f, 0.26f + k * 0.26f, 0.35f, 1.0f - k * 0.04f, C(0.94f, 0.90f, 0.80f), ArchTex.White, Faces.All & ~Faces.Bottom, k == 0);
                Box(0.2f, 0.7f, 0.78f, 0.86f, 0.4f, 0.9f, C(0.86f, 0.30f, 0.26f));
                Cyl(0.5f, 0f, 1.35f, 0.24f, 0.35f, 8, C(0.70f, 0.52f, 0.30f));
                Cyl(0.5f, 0.35f, 1.35f, 0.2f, 0.06f, 8, C(0.78f, 0.44f, 0.56f));
                Calendar(1, W * 0.5f + 0.6f, 1.9f);
                OnWall(1, W * 0.5f - 0.6f, 1.9f, 0.9f, 0.6f, White, ArchTex.Posters, 0f, 0.5f, 1f, 1f);
                Tube(W * 0.3f, D * 0.35f); Tube(W * 0.7f, D * 0.65f);
            }

            public void Kopi(out Spot keeper)
            {
                // the drinks counter across the back: urns, the kuih cabinet, menu boards over it; the taukeh behind
                float cd0 = D - 1.25f, cd1 = D - 0.6f;
                Counter(0.5f, W - 0.4f, cd0, cd1, Fw, C(0.30f, 0.52f, 0.40f), ArchTex.White, Marble);
                Cyl(0.9f, 0.97f, cd0 + 0.3f, 0.17f, 0.45f, 10, Chrome);
                Cyl(1.35f, 0.97f, cd0 + 0.3f, 0.17f, 0.45f, 10, Chrome);
                Box(W - 1.7f, W - 0.7f, 0.97f, 1.5f, cd0 + 0.05f, cd1 - 0.05f, Glass, ArchTex.GlassShop);
                for (int k = 0; k < 6; k++) Cyl(1.9f + k * 0.14f, 0.97f, cd0 + 0.25f, 0.04f, 0.1f, 6, White);   // a row of glasses
                OnWall(1, W * 0.5f - 0.7f, 2.3f, 1.2f, 0.62f, White, ArchTex.Menus, 0f, 0.5f, 1f, 1f);
                OnWall(1, W * 0.5f + 0.75f, 2.3f, 0.8f, 0.8f, White, ArchTex.Screens, 0.5f, 0f, 1f, 0.5f);
                keeper = Keeper(W * 0.5f, D - 0.3f, Fw);
                // marble tables with stools, in two columns either side of the way in
                var stoolCols = new[] { C(0.86f, 0.20f, 0.18f), C(0.20f, 0.44f, 0.80f) };
                float[] xs = W >= 4.4f ? new[] { W * 0.24f, W * 0.76f } : new[] { W * 0.3f };
                foreach (float x in xs)
                    for (float d = 1.7f; d < cd0 - 0.9f; d += 1.85f)
                    {
                        RoundTable(x, d);
                        var sc = stoolCols[rng.Next(2)];
                        Stool(x - 0.62f, d, sc); Stool(x + 0.62f, d, sc); Stool(x, d - 0.62f, sc); Stool(x, d + 0.62f, sc);
                        if (rng.NextDouble() < 0.6) Cyl(x + 0.1f, 0.76f, d - 0.1f, 0.05f, 0.11f, 6, C(0.72f, 0.46f, 0.26f));      // a glass of kopi
                    }
                Fridge(0.02f, 0.6f, Rt);
                Calendar(2, D * 0.5f, 1.9f);
                Fan(W * 0.5f, D * 0.33f);
                if (D > 6f) Fan(W * 0.5f, D * 0.68f);
            }

            public void Barber(out Spot keeper)
            {
                // the long mirror down the right wall over a shelf of bottles, two chairs facing it
                float m0 = 1.2f, m1 = D - 1.4f;
                Box(W - 0.4f, W, 0f, 0.85f, m0, m1, C(0.94f, 0.94f, 0.92f), ArchTex.White, Faces.All & ~Faces.Bottom, true);
                Box(W - 0.42f, W, 0.85f, 0.89f, m0, m1, Marble);
                Decal(W - 0.02f, 1.55f, (m0 + m1) * 0.5f, Lf, m1 - m0, 1.1f, C(0.80f, 0.92f, 1f, 0.9f), ArchTex.Glass, 0f, 0f, (m1 - m0) / 1.6f, 1f);
                Box(W - 0.05f, W, 0.95f, 1.0f, m0 - 0.05f, m1 + 0.05f, Steel); Box(W - 0.05f, W, 2.1f, 2.15f, m0 - 0.05f, m1 + 0.05f, Steel);
                Box(W - 0.05f, W, 0.95f, 2.15f, m0 - 0.05f, m0, Steel); Box(W - 0.05f, W, 0.95f, 2.15f, m1, m1 + 0.05f, Steel);
                for (float d = m0 + 0.2f; d < m1 - 0.1f; d += 0.16f)
                    Box(W - 0.3f, W - 0.2f, 0.89f, 0.89f + Rand(0.12f, 0.24f), d, d + 0.08f, Brights[rng.Next(Brights.Length)]);
                int chairs = 0;
                for (float d = m0 + 0.9f; d < m1 - 0.5f && chairs < 3; d += 1.6f, chairs++)
                {
                    float x = W - 1.15f;
                    Cyl(x, 0f, d, 0.28f, 0.06f, 10, Chrome);
                    Cyl(x, 0.06f, d, 0.07f, 0.4f, 6, Chrome, ArchTex.White, false);
                    Box(x - 0.3f, x + 0.3f, 0.46f, 0.62f, d - 0.3f, d + 0.3f, C(0.80f, 0.12f, 0.14f, 0.5f));                 // seat
                    Box(x - 0.36f, x - 0.22f, 0.62f, 1.3f, d - 0.3f, d + 0.3f, C(0.80f, 0.12f, 0.14f, 0.5f));               // back
                    Box(x - 0.36f, x - 0.22f, 1.3f, 1.42f, d - 0.12f, d + 0.12f, C(0.80f, 0.12f, 0.14f, 0.5f));             // headrest
                    Box(x - 0.26f, x + 0.26f, 0.62f, 0.7f, d - 0.36f, d - 0.3f, Chrome); Box(x - 0.26f, x + 0.26f, 0.62f, 0.7f, d + 0.3f, d + 0.36f, Chrome);
                    Box(x + 0.25f, x + 0.45f, 0.12f, 0.16f, d - 0.2f, d + 0.2f, Chrome);                                     // footrest
                    Solid(x - 0.4f, x + 0.45f, 0f, 1.2f, d - 0.36f, d + 0.36f);
                }
                keeper = Keeper(W - 1.95f, m0 + 0.9f, Fw);
                // a waiting bench on the left with the newspapers, the price list, a TV, the pole by the door
                Box(0.05f, 0.5f, 0.42f, 0.48f, 1.4f, Mathf.Min(D - 1f, 4.4f), WoodLight, ArchTex.PlanksH, Faces.All & ~Faces.Bottom, true, 0.6f);
                for (float d = 1.6f; d < Mathf.Min(D - 1f, 4.4f); d += 1.2f) Box(0.1f, 0.45f, 0f, 0.42f, d, d + 0.06f, Steel);
                Box(0.05f, 0.1f, 0.48f, 0.95f, 1.4f, Mathf.Min(D - 1f, 4.4f), WoodLight);
                Box(0.15f, 0.45f, 0.48f, 0.5f, 2.0f, 2.35f, C(0.92f, 0.90f, 0.84f));                                       // a newspaper
                OnWall(1, W * 0.5f, 1.7f, 1.1f, 0.62f, White, ArchTex.Menus, 0f, 0f, 1f, 0.5f);
                Box(0.02f, 0.12f, 1.9f, 2.45f, D * 0.5f - 0.4f, D * 0.5f + 0.4f, Black);
                Decal(0.12f, 2.17f, D * 0.5f, Rt, 0.72f, 0.48f, White, ArchTex.Screens, 0.5f, 0.5f, 1f, 1f);
                float px = 0.35f, pd = 0.4f;
                for (int k = 0; k < 6; k++) Cyl(px, 0.2f + k * 0.28f, pd, 0.09f, 0.28f, 8, k % 3 == 0 ? C(0.86f, 0.14f, 0.14f) : k % 3 == 1 ? White : C(0.18f, 0.32f, 0.78f));
                Cyl(px, 0f, pd, 0.12f, 0.2f, 8, Chrome); Cyl(px, 1.88f, pd, 0.12f, 0.12f, 8, Chrome);
                Calendar(2, D - 0.8f, 1.8f);
                Tube(W * 0.5f, D * 0.3f); Tube(W * 0.5f, D * 0.7f);
            }

            public void Hardware(out Spot keeper)
            {
                var shelfCol = C(0.46f, 0.50f, 0.56f);
                Counter(W - 1.6f, W - 1.0f, 0.5f, 2.2f, Lf, WoodDark);
                Till(W - 1.3f, 0.9f, Lf);
                keeper = Keeper(W - 0.6f, 1.4f, Lf);
                Shelf(0f, 0.55f, 1.6f, D - 0.1f, 2.4f, Rt, ArchTex.Hardware, shelfCol, 1.0f);
                Shelf(W - 0.55f, W, 2.6f, D - 0.1f, 2.4f, Lf, ArchTex.Hardware, shelfCol, 1.0f);
                // paint tins stacked up, a bundle of pipes, a ladder, brooms in a bucket, gas tanks
                for (int k = 0; k < 9; k++)
                {
                    float x = 0.9f + (k % 3) * 0.32f, d = D - 1.4f + (k / 3) * 0.32f;
                    int n = 1 + rng.Next(3);
                    for (int s = 0; s < n; s++) Cyl(x, s * 0.26f, d, 0.13f, 0.25f, 8, Brights[rng.Next(Brights.Length)]);
                }
                Solid(0.75f, 1.75f, 0f, 0.8f, D - 1.55f, D - 0.6f);
                if (W > 3.8f)
                    for (int k = 0; k < 5; k++) Box(W * 0.5f - 0.3f + k * 0.12f, W * 0.5f - 0.22f + k * 0.12f, 0f, 2.6f, D - 0.4f, D - 0.3f, C(0.92f, 0.92f, 0.90f));
                Box(0.7f, 0.76f, 0f, 2.2f, 1.0f, 1.04f, C(0.85f, 0.70f, 0.20f)); Box(1.1f, 1.16f, 0f, 2.2f, 1.0f, 1.04f, C(0.85f, 0.70f, 0.20f));
                for (int k = 0; k < 6; k++) Box(0.76f, 1.1f, 0.3f + k * 0.35f, 0.34f + k * 0.35f, 1.0f, 1.04f, C(0.85f, 0.70f, 0.20f));
                Cyl(1.6f, 0f, 0.8f, 0.2f, 0.32f, 8, C(0.20f, 0.44f, 0.82f));
                for (int k = 0; k < 4; k++) Box(1.5f + k * 0.06f, 1.53f + k * 0.06f, 0.3f, 1.5f, 0.78f, 0.81f, C(0.60f, 0.44f, 0.30f));
                Cyl(0.5f, 0f, D * 0.5f, 0.17f, 0.6f, 8, C(0.86f, 0.20f, 0.18f)); Cyl(0.5f, 0.6f, D * 0.5f, 0.05f, 0.1f, 6, Steel);
                Calendar(1, W - 1.0f, 1.9f);
                Tube(W * 0.5f, D * 0.3f); Tube(W * 0.5f, D * 0.7f);
            }

            public void Textile(out Spot keeper)
            {
                var shelfCol = C(0.70f, 0.52f, 0.34f);
                Shelf(0f, 0.6f, 1.2f, D - 0.1f, 2.5f, Rt, ArchTex.Textile, shelfCol, 1.2f);
                Shelf(W - 0.6f, W, 3.4f, D - 0.1f, 2.5f, Lf, ArchTex.Textile, shelfCol, 1.2f);
                // the cutting table, a bolt open on it with the scissors and the tape
                float tx = W * 0.5f, td = Mathf.Min(3.0f, D * 0.45f);
                Box(tx - 0.5f, tx + 0.5f, 0f, 0.86f, td - 0.9f, td + 0.9f, WoodLight, ArchTex.White, Faces.All & ~Faces.Bottom, true);
                Box(tx - 0.45f, tx + 0.45f, 0.86f, 0.9f, td - 0.7f, td + 0.5f, Brights[rng.Next(Brights.Length)], ArchTex.Rugs, Faces.Top | Faces.Front | Faces.Left | Faces.Right);
                Box(tx - 0.2f, tx + 0.2f, 0.9f, 1.04f, td + 0.55f, td + 0.85f, C(0.30f, 0.30f, 0.80f));
                keeper = Keeper(tx + 0.85f, td, Lf);
                // a rail of baju kurung by the door
                float rx = W - 0.45f;
                Box(rx - 0.02f, rx + 0.02f, 0f, 1.8f, 0.6f, 0.64f, Steel); Box(rx - 0.02f, rx + 0.02f, 0f, 1.8f, 2.9f, 2.94f, Steel);
                Box(rx - 0.02f, rx + 0.02f, 1.76f, 1.8f, 0.6f, 2.94f, Steel);
                for (float d = 0.75f; d < 2.85f; d += 0.16f)
                    Box(rx - 0.25f, rx + 0.25f, 0.75f, 1.72f, d, d + 0.1f, Brights[rng.Next(Brights.Length)]);
                Solid(rx - 0.3f, rx + 0.3f, 0f, 1.8f, 0.6f, 2.94f);
                Decal(0.62f, 1.4f, 0.75f, Rt, 0.5f, 1.4f, C(0.92f, 0.97f, 1f, 0.9f), ArchTex.Glass);
                Calendar(1, W * 0.5f, 1.9f);
                Tube(W * 0.5f, D * 0.3f); Tube(W * 0.5f, D * 0.7f);
            }

            public void Gold(out Spot keeper)
            {
                // glass counters round three sides, the gold on red velvet inside them, the safe at the back
                var glassTop = C(0.88f, 0.95f, 1f, 0.9f);
                float c0 = 1.4f, c1 = D - 1.6f;
                Counter(0.15f, 0.75f, c0, c1, Rt, C(0.62f, 0.10f, 0.12f), ArchTex.GoldShop, glassTop);
                Counter(W - 0.75f, W - 0.15f, c0, c1, Lf, C(0.62f, 0.10f, 0.12f), ArchTex.GoldShop, glassTop);
                Counter(0.75f, W - 0.75f, c1 - 0.6f, c1, Fw, C(0.62f, 0.10f, 0.12f), ArchTex.GoldShop, glassTop);
                keeper = Keeper(W * 0.5f, c1 + 0.35f, Fw);
                Box(W - 1.1f, W - 0.2f, 0f, 1.3f, D - 0.75f, D - 0.05f, C(0.28f, 0.30f, 0.34f, 0.4f), ArchTex.White, Faces.All & ~Faces.Bottom, true);
                Cyl(W - 0.65f, 0.7f, D - 0.78f, 0.12f, 0.03f, 10, Chrome);
                OnWall(1, W * 0.5f - 0.6f, 2.0f, 0.8f, 0.8f, White, ArchTex.Photos, 0.5f, 0f, 1f, 0.5f);
                Calendar(1, W * 0.5f + 0.6f, 2.0f);
                Tube(W * 0.5f, D * 0.3f); Tube(W * 0.5f, D * 0.7f);
            }

            // ---------------------------------------------------------------- homes
            /// <summary>A kampung living room. Pak Mat's (home): the shoe rack is outside, a sofa set facing the TV, the family
            /// photos, a ceiling fan, a mengkuang mat, the prayer mat laid toward the corner, the Quran on its stand.</summary>
            public void Living(bool home)
            {
                // which way's free: the door is on the front wall; furniture keeps to the walls and clear of it
                float doorC = 0f;
                foreach (var o in r.openings) if (o.wall == 0 && o.door) doorC = (o.c0 + o.c1) * 0.5f;
                bool tvLeft = doorC > W * 0.5f;                       // the TV on the side away from the door
                float tvX = tvLeft ? 0f : W;
                Vector3 tvFace = tvLeft ? Rt : Lf;
                float sofaX = tvLeft ? W : 0f;
                Vector3 sofaFace = -tvFace;
                float mid = D * 0.55f;
                // the TV cabinet and the TV, a vase, a clock over it
                float cx0 = tvLeft ? 0.02f : W - 0.48f, cx1 = cx0 + 0.46f;
                Box(cx0, cx1, 0f, 0.62f, mid - 0.9f, mid + 0.9f, WoodDark, ArchTex.White, Faces.All & ~Faces.Bottom, true);
                Decal(tvLeft ? cx1 : cx0, 0.33f, mid, tvFace, 1.7f, 0.5f, ArchMesh.Shade(WoodDark, 1.25f), ArchTex.PlanksV, 0f, 0f, 3f, 1f);
                float tx = tvLeft ? 0.12f : W - 0.22f;
                Box(tx, tx + 0.1f, 0.66f, 1.28f, mid - 0.52f, mid + 0.52f, Black);
                Box(tx + 0.02f, tx + 0.08f, 0.62f, 0.66f, mid - 0.2f, mid + 0.2f, Black);
                Decal(tvLeft ? tx + 0.1f : tx, 0.97f, mid, tvFace, 0.96f, 0.56f, White, ArchTex.Screens, 0f, 0.5f, 0.5f, 1f);
                Cyl(tvLeft ? 0.25f : W - 0.25f, 0.62f, mid + 0.75f, 0.08f, 0.32f, 8, C(0.30f, 0.56f, 0.74f, 0.6f));
                Cyl(tvLeft ? 0.25f : W - 0.25f, 0.94f, mid + 0.75f, 0.12f, 0.12f, 6, C(0.92f, 0.36f, 0.50f));
                // the sofa set: a long sofa against the other wall, an armchair at its end, a coffee table on a carpet
                var sofa = home ? C(0.66f, 0.30f, 0.26f) : Pick(C(0.30f, 0.46f, 0.64f), C(0.52f, 0.36f, 0.24f), C(0.36f, 0.56f, 0.40f));
                float sx0 = tvLeft ? W - 0.85f : 0.02f, sx1 = sx0 + 0.83f;
                bool backAtHigh = tvLeft;                              // the sofa's back against x = W (TV on the left)
                float bx0 = backAtHigh ? sx1 - 0.2f : sx0, bx1 = bx0 + 0.2f;
                Box(sx0, sx1, 0f, 0.42f, mid - 1.0f, mid + 1.0f, sofa, ArchTex.White, Faces.All & ~Faces.Bottom, true);
                Box(bx0, bx1, 0.42f, 0.9f, mid - 1.0f, mid + 1.0f, sofa);
                Box(sx0, sx1, 0.42f, 0.62f, mid - 1.0f, mid - 0.82f, sofa); Box(sx0, sx1, 0.42f, 0.62f, mid + 0.82f, mid + 1.0f, sofa);
                for (int k = 0; k < 3; k++)                                                                       // cushions in batik
                {
                    float d = mid - 0.55f + k * 0.55f;
                    float cx = backAtHigh ? bx0 - 0.08f : bx1 + 0.08f;
                    Box(cx - 0.07f, cx + 0.07f, 0.45f, 0.8f, d - 0.2f, d + 0.2f, White, ArchTex.Rugs, Faces.All & ~Faces.Bottom);
                }
                Solid(sx0, sx1, 0f, 0.9f, mid - 1.0f, mid + 1.0f);
                float ax0 = sx0, ad0 = mid + 1.25f;
                if (ad0 + 0.8f < D - 0.1f)
                {
                    Box(ax0, ax0 + 0.8f, 0f, 0.42f, ad0, ad0 + 0.8f, sofa, ArchTex.White, Faces.All & ~Faces.Bottom, true);
                    Box(ax0, ax0 + 0.8f, 0.42f, 0.88f, ad0 + 0.62f, ad0 + 0.8f, sofa);
                }
                float ccx = W * 0.5f + (tvLeft ? 0.6f : -0.6f);
                Decal(ccx, 0.012f, mid, Vector3.up, 2.6f, 1.9f, White, ArchTex.Rugs, home ? 0f : 0.5f, 0f, home ? 0.5f : 1f, 0.5f);
                Box(ccx - 0.45f, ccx + 0.45f, 0.36f, 0.42f, mid - 0.35f, mid + 0.35f, Glass, ArchTex.White, Faces.All, true);
                Box(ccx - 0.4f, ccx + 0.4f, 0f, 0.36f, mid - 0.3f, mid + 0.3f, WoodDark, ArchTex.White, Faces.Front | Faces.Back | Faces.Left | Faces.Right);
                Cyl(ccx + 0.15f, 0.42f, mid, 0.05f, 0.1f, 6, C(0.95f, 0.95f, 0.92f));                            // a cup of teh
                Box(ccx - 0.3f, ccx - 0.05f, 0.42f, 0.5f, mid - 0.15f, mid + 0.12f, C(0.86f, 0.30f, 0.22f));        // a tin of biscuits
                // the family photos over the sofa and on the back wall
                int sofaWall = tvLeft ? 3 : 2;
                OnWall(sofaWall, mid - 0.55f, 1.75f, 0.6f, 0.6f, White, ArchTex.Photos, 0f, 0.5f, 0.5f, 1f);
                OnWall(sofaWall, mid + 0.4f, 1.85f, 0.5f, 0.5f, White, ArchTex.Photos, 0.5f, 0.5f, 1f, 1f);
                OnWall(1, W * 0.5f - 0.6f, 1.9f, 0.62f, 0.62f, White, ArchTex.Photos, 0f, 0f, 0.5f, 0.5f);
                OnWall(1, W * 0.5f + 0.45f, 1.95f, 0.55f, 0.55f, White, ArchTex.Photos, 0.5f, 0f, 1f, 0.5f);
                Calendar(1, W * 0.5f + 1.3f, 1.8f);
                // the prayer corner: the sejadah toward the back corner on the TV side, the Quran on its rehal
                if (home || rng.NextDouble() < 0.6)
                {
                    float px = tvLeft ? 1.1f : W - 1.1f, pd = D - 0.75f;
                    Decal(px, 0.014f, pd, Vector3.up, 0.7f, 1.15f, White, ArchTex.Rugs, 0f, 0.5f, 0.5f, 1f);
                    float qx = tvLeft ? 0.25f : W - 0.25f;
                    Box(qx - 0.18f, qx + 0.18f, 0f, 0.5f, D - 0.4f, D - 0.05f, WoodLight, ArchTex.White, Faces.All & ~Faces.Bottom);
                    Box(qx - 0.15f, qx + 0.15f, 0.5f, 0.56f, D - 0.35f, D - 0.12f, C(0.12f, 0.36f, 0.22f));
                    Box(qx - 0.16f, qx + 0.16f, 0.3f, 0.36f, D - 0.36f, D - 0.1f, C(0.95f, 0.95f, 0.92f));          // the folded telekung
                }
                // a cabinet of good plates, a standing fan, the ceiling fan
                float kc = D > 3.5f ? Free(1, tvLeft ? W - 1.2f : 1.2f, 0.8f, 0f, 1.7f) : float.NaN;
                if (!float.IsNaN(kc))
                {
                    float kx0 = kc - 0.4f;
                    Box(kx0, kx0 + 0.8f, 0f, 1.7f, D - 0.42f, D - 0.02f, WoodDark, ArchTex.White, Faces.All & ~Faces.Bottom, true);
                    Decal(kx0 + 0.4f, 1.2f, D - 0.42f, Fw, 0.7f, 0.8f, C(0.92f, 0.97f, 1f, 0.8f), ArchTex.Glass);
                    for (int k = 0; k < 3; k++)                                                                    // the good plates on show
                        Cyl(kx0 + 0.2f + k * 0.2f, 1.05f, D - 0.3f, 0.08f, 0.02f, 10, C(0.98f, 0.98f, 0.96f, 0.5f));
                }
                Fan(W * 0.5f, D * 0.5f);
                if (home)
                {
                    // a songkok on the TV cabinet, Adik's school bag by the sofa, a basket of laundry
                    Box(cx0 + 0.1f, cx0 + 0.32f, 0.62f, 0.74f, mid - 0.85f, mid - 0.62f, Black);
                    Box(sx0 + (tvLeft ? -0.45f : 0.9f), sx0 + (tvLeft ? -0.1f : 1.25f), 0f, 0.4f, mid - 1.5f, mid - 1.2f, C(0.86f, 0.24f, 0.40f));
                    Cyl(W * 0.5f, 0f, D - 0.5f, 0.24f, 0.36f, 8, C(0.74f, 0.58f, 0.36f));
                    Box(W * 0.5f - 0.2f, W * 0.5f + 0.2f, 0.3f, 0.42f, D - 0.7f, D - 0.3f, C(0.96f, 0.70f, 0.30f));
                }
            }

            /// <summary>The dapur: the counter along the back with the stove, the sink and the rice cooker, the gas tank under it,
            /// a fridge in the corner, a small table with its chairs, pots on the wall.</summary>
            public void Kitchen()
            {
                float c0 = D - 0.62f;
                Box(0.2f, W - 0.1f, 0f, 0.86f, c0, D, C(0.94f, 0.94f, 0.92f), ArchTex.White, Faces.All & ~Faces.Bottom, true);
                Box(0.18f, W - 0.08f, 0.86f, 0.9f, c0 - 0.03f, D, C(0.34f, 0.56f, 0.62f, 0.5f));
                for (float x = 0.3f; x < W - 0.4f; x += 0.6f) Box(x, x + 0.54f, 0.08f, 0.8f, c0 - 0.01f, c0, C(0.86f, 0.88f, 0.86f), ArchTex.White, Faces.Front);
                // the stove and its pot, the sink, the rice cooker, the gas tank peeping out
                float sx = W * 0.3f;
                Box(sx - 0.32f, sx + 0.32f, 0.9f, 0.98f, c0 + 0.08f, D - 0.08f, Black);
                Cyl(sx - 0.14f, 0.98f, c0 + 0.3f, 0.15f, 0.16f, 10, C(0.70f, 0.72f, 0.76f, 0.6f));
                Cyl(sx + 0.16f, 0.98f, c0 + 0.32f, 0.17f, 0.06f, 10, C(0.20f, 0.20f, 0.22f));                      // the kuali
                Box(W * 0.62f - 0.3f, W * 0.62f + 0.3f, 0.87f, 0.9f, c0 + 0.08f, D - 0.1f, Chrome);
                Cyl(W * 0.62f, 0.9f, D - 0.12f, 0.02f, 0.3f, 4, Chrome, ArchTex.White, false);
                Cyl(W - 0.45f, 0.9f, c0 + 0.3f, 0.14f, 0.22f, 10, C(0.96f, 0.96f, 0.94f));
                Cyl(sx + 0.65f, 0f, c0 - 0.16f, 0.16f, 0.5f, 8, C(0.86f, 0.22f, 0.20f, 0.4f));
                // pots and the ladle hanging on the wall over the counter, a shelf of tins
                for (int k = 0; k < 4; k++)
                {
                    float x = 0.6f + k * 0.36f, s = 0.1f + 0.02f * k, y = 1.3f + (k % 2) * 0.08f;
                    Box(x - s, x + s, y - s, y + s, D - 0.05f, D - 0.01f, C(0.66f, 0.68f, 0.72f, 0.6f));            // pans on hooks
                    Box(x - 0.015f, x + 0.015f, y + s, y + s + 0.18f, D - 0.04f, D - 0.02f, WoodDark);
                }
                Box(0.4f, W - 0.4f, 1.75f, 1.79f, D - 0.28f, D, WoodLight);
                for (float x = 0.5f; x < W - 0.5f; x += 0.22f) Cyl(x, 1.79f, D - 0.14f, 0.07f, 0.16f, 6, Brights[rng.Next(Brights.Length)]);
                // the fridge in the corner, the table and its chairs
                Fridge(0.02f, 0.2f, Rt);
                float tx = W * 0.55f, td = (D - 0.62f) * 0.5f;
                if (D > 2.2f)
                {
                    Box(tx - 0.55f, tx + 0.55f, 0.72f, 0.76f, td - 0.38f, td + 0.38f, White, ArchTex.Rugs, Faces.All & ~Faces.Bottom);
                    foreach (var (lx, ld) in new[] { (-0.48f, -0.32f), (0.48f, -0.32f), (-0.48f, 0.32f), (0.48f, 0.32f) })
                        Box(tx + lx - 0.03f, tx + lx + 0.03f, 0f, 0.72f, td + ld - 0.03f, td + ld + 0.03f, WoodDark);
                    Solid(tx - 0.55f, tx + 0.55f, 0f, 0.76f, td - 0.38f, td + 0.38f);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float x = tx + side * 0.8f;
                        Box(x - 0.2f, x + 0.2f, 0.44f, 0.48f, td - 0.2f, td + 0.2f, WoodLight);
                        float bx = x + side * 0.17f;
                        Box(bx - 0.03f, bx + 0.03f, 0.48f, 0.95f, td - 0.2f, td + 0.2f, WoodLight);
                        foreach (var (lx, ld) in new[] { (-0.17f, -0.17f), (0.17f, -0.17f), (-0.17f, 0.17f), (0.17f, 0.17f) })
                            Box(x + lx - 0.02f, x + lx + 0.02f, 0f, 0.44f, td + ld - 0.02f, td + ld + 0.02f, WoodDark);
                    }
                    Box(tx - 0.12f, tx + 0.12f, 0.76f, 0.86f, td - 0.12f, td + 0.12f, C(0.96f, 0.80f, 0.30f));       // tudung saji over the lauk
                }
                Calendar(2, D * 0.5f, 1.7f);
                Tube(W * 0.5f, D * 0.5f, false);
            }

            /// <summary>The mamak (its own model is the room: posts, roof, counter wall, tables and stools): the extras that make
            /// it one - the TV on a post showing the football, the menu board, the drinks fridge, the roti canai griddle.</summary>
            public void Mamak()
            {
                // room frame: x across the restaurant (seen from the road), d in from the road; the counter wall's face is d = D
                float cx = W * 0.5f;
                Box(cx - 1.3f, cx + 1.3f, 1.0f, 2.0f, D - 0.06f, D, C(0.22f, 0.22f, 0.24f));
                Decal(cx, 1.5f, D - 0.06f, Fw, 2.4f, 0.9f, White, ArchTex.Menus, 0f, 0.5f, 1f, 1f);
                // the TV hung off the roof by the right-hand posts, both faces showing the football
                float tvx = W - 1.4f, tvd = D * 0.45f;
                Box(tvx - 0.03f, tvx + 0.03f, 2.6f, H, tvd - 0.03f, tvd + 0.03f, Steel);
                Box(tvx - 0.55f, tvx + 0.55f, 2.0f, 2.62f, tvd - 0.05f, tvd + 0.05f, Black);
                Decal(tvx, 2.31f, tvd - 0.05f, Fw, 1.0f, 0.56f, White, ArchTex.Screens, 0.5f, 0.5f, 1f, 1f);
                Decal(tvx, 2.31f, tvd + 0.05f, Bk, 1.0f, 0.56f, White, ArchTex.Screens, 0.5f, 0.5f, 1f, 1f);
                // the drinks fridge and the roti canai station in front of the counter
                Fridge(1.2f, D - 0.75f, Fw);
                float rx = W - 2.8f, rd = D - 0.5f;
                Box(rx - 0.6f, rx + 0.6f, 0f, 0.86f, rd - 0.35f, rd + 0.35f, Steel, ArchTex.White, Faces.All & ~Faces.Bottom, true);
                Box(rx - 0.5f, rx + 0.5f, 0.86f, 0.92f, rd - 0.3f, rd + 0.3f, Black);
                for (int k = 0; k < 3; k++) Cyl(rx - 0.3f + k * 0.3f, 0.92f, rd, 0.11f, 0.02f, 10, C(0.94f, 0.80f, 0.52f));
                Box(rx - 0.55f, rx - 0.3f, 0.92f, 1.02f, rd + 0.15f, rd + 0.3f, C(0.96f, 0.95f, 0.90f));
                Fan(W * 0.3f, D * 0.4f); Fan(W * 0.7f, D * 0.4f);
            }
        }
    }

    /// <summary>Closed solids for physics meshes (all faces outward).</summary>
    public static class Solid
    {
        static readonly int[][] BoxFaces = { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 3, 2, 6, 7 }, new[] { 0, 3, 7, 4 }, new[] { 1, 2, 6, 5 } };

        /// <summary>A closed box in a frame.</summary>
        public static void Box(List<Vector3> v, List<int> t, in Frame f, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            if (x1 - x0 < 1e-3f || y1 - y0 < 1e-3f || z1 - z0 < 1e-3f) return;
            Convex(v, t, new[] { f.P(x0, y0, z0), f.P(x1, y0, z0), f.P(x1, y1, z0), f.P(x0, y1, z0), f.P(x0, y0, z1), f.P(x1, y0, z1), f.P(x1, y1, z1), f.P(x0, y1, z1) }, BoxFaces);
        }

        /// <summary>A wall standing along a counter-clockwise footprint edge a -> b, `thick` deep on the inside.</summary>
        public static void EdgeWall(List<Vector3> v, List<int> t, Vector2 a, Vector2 b, float y0, float y1, float thick)
        {
            float len = (b - a).magnitude;
            if (len < 0.05f) return;
            Box(v, t, Frame.Edge(a, b, y0), 0f, len, 0f, y1 - y0, -thick, 0f);
        }

        /// <summary>A prism standing on a triangle (x, y) in a frame's wall plane from z0 to z1: a roof, a ramp.</summary>
        public static void Tri(List<Vector3> v, List<int> t, in Frame f, Vector2 a, Vector2 b, Vector2 c, float z0, float z1)
        {
            Convex(v, t, new[] { f.P(a.x, a.y, z0), f.P(b.x, b.y, z0), f.P(c.x, c.y, z0), f.P(a.x, a.y, z1), f.P(b.x, b.y, z1), f.P(c.x, c.y, z1) },
                   new[] { new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 0, 1, 4, 3 }, new[] { 1, 2, 5, 4 }, new[] { 2, 0, 3, 5 } });
        }

        /// <summary>A convex solid from its corners and its faces (corner loops, either winding): each face is fanned and
        /// turned to face away from the middle.</summary>
        public static void Convex(List<Vector3> v, List<int> t, Vector3[] pts, int[][] faces)
        {
            var c = Vector3.zero;
            foreach (var p in pts) c += p;
            c /= pts.Length;
            int b = v.Count;
            v.AddRange(pts);
            foreach (var face in faces)
                for (int k = 1; k + 1 < face.Length; k++)
                {
                    int i0 = face[0], i1 = face[k], i2 = face[k + 1];
                    var n = Vector3.Cross(pts[i1] - pts[i0], pts[i2] - pts[i0]);
                    if (n.sqrMagnitude < 1e-12f) continue;
                    if (Vector3.Dot(n, pts[i0] - c) < 0f) { int s = i1; i1 = i2; i2 = s; }
                    t.Add(b + i0); t.Add(b + i1); t.Add(b + i2);
                }
        }
    }
}
