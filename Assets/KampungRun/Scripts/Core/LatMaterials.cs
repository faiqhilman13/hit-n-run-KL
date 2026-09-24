using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>Shared LatInk materials, cached per colour so the SRP batcher stays happy.</summary>
    public static class LatMaterials
    {
        static readonly Dictionary<int, Material> Cache = new Dictionary<int, Material>();

        public static readonly Color Paper = new Color(0.97f, 0.94f, 0.86f);
        public static readonly Color Ink = new Color(0.09f, 0.07f, 0.06f);

        /// <param name="surface">painted texture id (SurfaceKinds); -1 = pick from the colour</param>
        public static Material Get(Color c, float outline = 2.2f, int surface = -1)
        {
            if (surface < 0) surface = SurfaceKinds.Classify(c);
            int key = ((int)(c.r * 255) << 24) ^ ((int)(c.g * 255) << 16) ^ ((int)(c.b * 255) << 8) ^ (int)(outline * 10) ^ (surface << 20);
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(GameAssets.I.latInk) { name = $"LatInk_{ColorUtility.ToHtmlStringRGB(c)}", enableInstancing = true };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_OutlineWidth", outline);
            m.SetFloat("_Surface", surface);
            Cache[key] = m;
            return m;
        }

        static Material _signText;

        /// <summary>Depth-tested material for world-space TextMesh signs.</summary>
        public static Material SignText(Font font)
        {
            if (_signText == null && GameAssets.I.text3d != null)
            {
                _signText = new Material(GameAssets.I.text3d) { name = "SignText" };
                _signText.mainTexture = font.material.mainTexture;
                Font.textureRebuilt += f => { if (_signText != null && f == font) _signText.mainTexture = f.material.mainTexture; };
            }
            return _signText != null ? _signText : font.material;
        }

        public static Material Beam(Color c)
        {
            var m = new Material(GameAssets.I.beam);
            m.SetColor("_Color", c);
            return m;
        }

        /// <summary>Palette shared by the procedural city (mirrors the Blender palette).</summary>
        public static class Pal
        {
            public static readonly Color Road = new Color(0.46f, 0.48f, 0.52f);
            public static readonly Color RoadLine = new Color(1f, 0.95f, 0.55f);
            public static readonly Color Pavement = new Color(0.86f, 0.8f, 0.7f);
            public static readonly Color Grass = new Color(0.52f, 0.76f, 0.38f);
            public static readonly Color Dirt = new Color(0.86f, 0.66f, 0.42f);
            public static readonly Color Water = new Color(0.28f, 0.58f, 0.78f);
            public static readonly Color Wall = new Color(0.8f, 0.74f, 0.66f);
            public static readonly Color Rail = new Color(0.95f, 0.92f, 0.85f);
            public static readonly Color Park = new Color(0.4f, 0.72f, 0.3f);
            public static readonly Color Coin = new Color(0.95f, 0.78f, 0.25f);
            public static readonly Color Red = new Color(0.82f, 0.28f, 0.22f);
        }
    }
}
