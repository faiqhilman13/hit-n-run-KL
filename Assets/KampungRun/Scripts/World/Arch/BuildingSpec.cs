using UnityEngine;

namespace KampungRun.Arch
{
    /// <summary>What a footprint edge faces (from Tools/klmap/patches.py for the real-KL districts).</summary>
    [System.Flags]
    public enum EdgeFlags : byte
    {
        None = 0,
        Street = 1,        // a road or pavement runs along it: the shop or main front
        Party = 2,         // shared with the next building: a blank wall
        Walkway = 4,       // a pedestrian street (Petaling Street) runs along it
        Water = 8,         // the river
    }

    /// <summary>What sort of building stands on a footprint, which picks its generator.</summary>
    public enum BuildingKind : byte
    {
        Shop,              // shophouse: shop or kopitiam below, rooms above, five-foot way where there's room
        House,             // terrace / link house
        Flats,             // walk-up flats, PPR, condo
        Office,            // office block, hotel, mall
        Worship,           // mosque, temple, church (plain, painted white)
        Shed,              // warehouse, workshop, car park
        Civic,             // school, government, hall
    }

    /// <summary>Something the architecture library models into a city cell: a building, a tree, a clump of grass.</summary>
    public interface IArchItem
    {
        /// <summary>Where it stands (world x, z), which picks its cell.</summary>
        Vector2 Where();
        /// <summary>Model it: full detail near the camera, the distance version otherwise.</summary>
        void Build(ArchMesh m, bool detail);
    }

    /// <summary>One building to model: its footprint and what it is.</summary>
    public class BuildingSpec : IArchItem
    {
        public Vector2 Where() => Centre();
        public void Build(ArchMesh m, bool detail) => ArchGen.Build(this, m, detail);

        public Vector2[] ring;          // footprint (world x, z), counter-clockwise seen from above
        public EdgeFlags[] edges;       // per edge ring[i] -> ring[i + 1]
        public float y0 = 0.18f;        // the pavement it stands on
        public float height;            // eaves / top of the walls above y0
        public int levels;
        public BuildingKind kind;
        public int seed;
        public byte colour;             // palette index chosen by the map (0 = let the style choose)
        public bool tiledRoof;
        /// <summary>A flat roof you can stand on (Chow Kit's rows, where the awnings bounce you up).</summary>
        public bool flatRoof;

        public float Area()
        {
            float a = 0f;
            for (int i = 0; i < ring.Length; i++) { var p = ring[i]; var q = ring[(i + 1) % ring.Length]; a += p.x * q.y - q.x * p.y; }
            return a * 0.5f;
        }

        public Vector2 Centre()
        {
            var c = Vector2.zero;
            foreach (var p in ring) c += p;
            return c / Mathf.Max(1, ring.Length);
        }

        public EdgeFlags Edge(int i) => edges != null && edges.Length > 0 ? edges[((i % ring.Length) + ring.Length) % ring.Length] : EdgeFlags.None;
    }
}
