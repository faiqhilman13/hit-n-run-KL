using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Costume recolours for characters that bring their own baked atlas (the MPFB cast) instead of sampling
    /// the shared palette. Their material carries a mask (_MaskMap: R = shirt/baju, G = sarong/kain,
    /// B = songkok); a swap shifts just that region toward the costume colour in HSV, keeping the print's own
    /// light and dark, so a batik stays a batik in the new colourway. Variants are cached per atlas + swap.
    /// </summary>
    public static class AtlasRecolor
    {
        static readonly Dictionary<string, int> Channel = new Dictionary<string, int>
        {
            ["Batik"] = 0, ["BatikBlue"] = 0, ["Shirt"] = 0, ["Baju"] = 0, ["Kurung"] = 0, ["TshirtRed"] = 0, ["Tshirt"] = 0, ["White"] = 0,
            ["Sarong"] = 1, ["SarongRed"] = 1, ["Kain"] = 1, ["Pants"] = 1, ["Shorts"] = 1,
            ["Songkok"] = 2,
        };

        static readonly Dictionary<(Texture2D, string), Texture2D> Textures = new Dictionary<(Texture2D, string), Texture2D>();
        static readonly Dictionary<(Material, string), Material> Materials = new Dictionary<(Material, string), Material>();
        static readonly Dictionary<(Texture2D, int), Color> Reference = new Dictionary<(Texture2D, int), Color>();

        static Texture2D Mask(Material m) => m && m.HasProperty("_MaskMap") ? m.GetTexture("_MaskMap") as Texture2D : null;

        /// <summary>True if this material can be recoloured by mask (an own-atlas character, not the palette).</summary>
        public static bool Has(Material m)
        {
            var mask = Mask(m);
            return mask != null && mask.isReadable && m.GetTexture("_BaseMap") is Texture2D b && b.isReadable && !KLPalette.IsKL(m);
        }

        public static Material Swap(Material source, IDictionary<string, Color> swaps)
        {
            var wanted = new Dictionary<int, Color>();
            foreach (var kv in swaps)
                if (Channel.TryGetValue(kv.Key, out int ch)) wanted[ch] = kv.Value;
            if (wanted.Count == 0) return source;
            string key = string.Join(";", wanted.OrderBy(k => k.Key).Select(k => $"{k.Key}={ColorUtility.ToHtmlStringRGB(k.Value)}"));
            if (Materials.TryGetValue((source, key), out var cachedMat) && cachedMat) return cachedMat;
            var baseTex = (Texture2D)source.GetTexture("_BaseMap");
            var tex = Texture(baseTex, Mask(source), wanted, key);
            var mat = new Material(source) { name = source.name + " [" + key + "]" };
            mat.SetTexture("_BaseMap", tex);
            Materials[(source, key)] = mat;
            return mat;
        }

        static Texture2D Texture(Texture2D src, Texture2D mask, Dictionary<int, Color> wanted, string key)
        {
            if (Textures.TryGetValue((src, key), out var cached) && cached) return cached;
            int w = src.width, h = src.height, mw = mask.width, mh = mask.height;
            var px = src.GetPixels32();
            var mk = mask.GetPixels32();
            // the region's own average colour is the reference the costume colour replaces
            var refs = new Dictionary<int, (float h, float s, float v)>();
            foreach (var ch in wanted.Keys)
            {
                var r = RegionAverage(src, px, w, h, mk, mw, mh, ch);
                Color.RGBToHSV(r, out float rh, out float rs, out float rv);
                refs[ch] = (rh, Mathf.Max(0.05f, rs), Mathf.Max(0.05f, rv));
            }
            var targets = wanted.ToDictionary(k => k.Key, k => { Color.RGBToHSV(k.Value, out float th, out float ts, out float tv); return (th, ts, tv); });
            for (int y = 0; y < h; y++)
            {
                int my = y * mh / h;
                for (int x = 0; x < w; x++)
                {
                    var m = mk[my * mw + x * mw / w];
                    int ch = m.r > 127 ? 0 : m.g > 127 ? 1 : m.b > 127 ? 2 : -1;
                    if (ch < 0 || !targets.ContainsKey(ch)) continue;
                    int i = y * w + x;
                    var c = px[i];
                    Color.RGBToHSV(c, out float ph, out float ps, out float pv);
                    var (rh, rs, rv) = refs[ch];
                    var (th, ts, tv) = targets[ch];
                    float nh = Mathf.Repeat(ph + (th - rh), 1f);
                    float ns = Mathf.Clamp01(ps * ts / rs);
                    float nv = Mathf.Clamp01(pv * tv / rv);
                    var o = Color.HSVToRGB(nh, ns, nv);
                    px[i] = new Color32((byte)(o.r * 255), (byte)(o.g * 255), (byte)(o.b * 255), c.a);
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = src.name + "_" + key, wrapMode = src.wrapMode, filterMode = src.filterMode, anisoLevel = src.anisoLevel };
            tex.SetPixels32(px);
            tex.Apply(true);
            tex.Compress(true);
            Textures[(src, key)] = tex;
            return tex;
        }

        static Color RegionAverage(Texture2D src, Color32[] px, int w, int h, Color32[] mk, int mw, int mh, int ch)
        {
            if (Reference.TryGetValue((src, ch), out var known)) return known;
            double r = 0, g = 0, b = 0; long n = 0;
            for (int y = 0; y < h; y += 4)
                for (int x = 0; x < w; x += 4)
                {
                    var m = mk[(y * mh / h) * mw + x * mw / w];
                    byte v = ch == 0 ? m.r : ch == 1 ? m.g : m.b;
                    if (v < 128) continue;
                    var c = px[y * w + x];
                    r += c.r; g += c.g; b += c.b; n++;
                }
            var avg = n > 0 ? new Color((float)(r / n / 255), (float)(g / n / 255), (float)(b / n / 255)) : Color.gray;
            Reference[(src, ch)] = avg;
            return avg;
        }
    }
}
