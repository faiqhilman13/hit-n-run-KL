using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Which painted texture (Tools/gen_surfaces.py) a flat-coloured surface gets in the Hit & Run
    /// shading, decided from its colour: greens are grass / leaves, dark greys asphalt, sandy tans
    /// paving, red-browns brick (roof tiles where they face the sky), wood browns timber, pale greys
    /// concrete, the river blue water, and every other painted colour plaster.
    /// KL models carry the id in their palette atlas instead (see kl_core.SURFACE).
    /// </summary>
    public static class SurfaceKinds
    {
        public const int None = 0, Grass = 1, Asphalt = 2, Paving = 3, Brick = 4, Timber = 5, Plaster = 6, Concrete = 7,
            Leaves = 8, Water = 9, Roof = 10, FloorTile = 11;

        public static int Classify(Color c)
        {
            if (Near(c, LatMaterials.Pal.Water)) return Water;
            if (Near(c, LatMaterials.Pal.Road)) return Asphalt;
            if (Near(c, LatMaterials.Pal.Pavement)) return Paving;
            if (Near(c, LatMaterials.Pal.Wall) || Near(c, LatMaterials.Pal.Rail)) return Concrete;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.12f) return v < 0.58f ? Asphalt : v < 0.9f ? Concrete : Plaster;
            if (h > 0.18f && h < 0.46f && s > 0.25f) return v > 0.55f ? Grass : Leaves;
            if (h > 0.05f && h < 0.13f && s > 0.4f && v < 0.7f) return Timber;
            if ((h < 0.045f || h > 0.96f) && s > 0.45f && v > 0.4f && v < 0.85f) return Brick;
            if (h > 0.05f && h < 0.17f && s < 0.38f && v > 0.62f) return Paving;
            return Plaster;
        }

        static bool Near(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;
    }
}
