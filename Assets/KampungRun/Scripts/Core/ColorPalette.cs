using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// One small texture holding every flat LatInk colour the city uses, so meshes of many colours can be
    /// merged and drawn with a single material: each colour gets a 4x4 texel cell (colour in RGB, its painted
    /// surface id in alpha, which is how LatInk reads a KL atlas) and the vertices sample the middle of it.
    /// A browser pays for every draw call; this turns a city block's thirty colours into one.
    /// </summary>
    public static class ColorPalette
    {
        const int Cell = 4, Size = 256, PerRow = Size / Cell;      // 4096 colours
        static Texture2D _tex;
        static Material _mat;
        static readonly Dictionary<(Color32, int), int> Slots = new Dictionary<(Color32, int), int>();
        static bool _dirty;

        public static Texture2D Texture
        {
            get
            {
                if (_tex == null)
                {
                    _tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false)
                    { name = "CityPalette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    var px = new Color32[Size * Size];
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 0, 255, 0);
                    _tex.SetPixels32(px);
                    Slots.Clear();
                    _dirty = true;
                }
                return _tex;
            }
        }

        /// <summary>The shared material (LatInk, white, painted with the palette).</summary>
        public static Material Material => _mat != null ? _mat : _mat = LatMaterials.GetTextured(Color.white, Texture, SurfaceKinds.None, 0f);

        /// <summary>The UV that paints colour c with painted surface `surface` (-1: pick from the colour).</summary>
        public static Vector2 UV(Color c, int surface = -1)
        {
            var tex = Texture;
            if (surface < 0) surface = SurfaceKinds.Classify(c);
            Color32 key = c;
            if (!Slots.TryGetValue((key, surface), out int slot))
            {
                slot = Slots.Count;
                if (slot >= PerRow * PerRow) slot = 0;                  // full (never in practice): reuse the first
                else
                {
                    Slots[(key, surface)] = slot;
                    int x0 = slot % PerRow * Cell, y0 = slot / PerRow * Cell;
                    var fill = new Color32[Cell * Cell];
                    for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(key.r, key.g, key.b, (byte)Mathf.Clamp(surface, 0, 15));
                    tex.SetPixels32(x0, y0, Cell, Cell, fill);
                    _dirty = true;
                }
            }
            return new Vector2((slot % PerRow * Cell + Cell * 0.5f) / Size, (slot / PerRow * Cell + Cell * 0.5f) / Size);
        }

        /// <summary>Upload the new colours (after a batch of UV() calls).</summary>
        public static void Apply()
        {
            if (_tex != null && _dirty) { _tex.Apply(false, false); _dirty = false; }
        }

        /// <summary>Is this a flat-coloured LatInk material (no texture, glow or gloss), and if so its colour and surface.</summary>
        public static bool Plain(Material m, out Color c, out int surface)
        {
            c = default;
            surface = 0;
            if (m == null || GameAssets.I == null || m.shader != GameAssets.I.latInk) return false;
            var tex = m.GetTexture("_BaseMap");
            if (tex != null && tex.name != "UnityWhite") return false;
            if (m.GetFloat("_Emit") > 0.001f || m.GetFloat("_Gloss") > 0.001f) return false;
            c = m.GetColor("_BaseColor");
            surface = Mathf.RoundToInt(m.GetFloat("_Surface"));
            return true;
        }
    }
}
