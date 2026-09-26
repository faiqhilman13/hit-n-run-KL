using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Runtime palette swaps for KL models. Materials sample the environment or character
    /// colour atlas; recolouring an instance (costumes, pedestrian
    /// variety) means giving it a copy of the atlas with some named cells repainted. The cell
    /// table comes from Resources/kl_palette.json, written by the Blender build. Variants are
    /// cached, so a crowd of pedestrians shares a handful of textures and materials.
    /// </summary>
    public static class KLPalette
    {
        [Serializable] class Entry { public string name; public int index; public float r, g, b, gloss, alpha; }
        [Serializable] class Table { public int cells; public int cellPx; public Entry[] entries; }

        static Table _table;
        static Dictionary<string, Entry> _byName;
        static readonly Dictionary<(Texture2D, string), Texture2D> Textures = new Dictionary<(Texture2D, string), Texture2D>();
        static readonly Dictionary<(Material, string), Material> Materials = new Dictionary<(Material, string), Material>();

        /// <summary>Old per-material colour names (legacy models, costumes, mission NPC tints) -> the
        /// KL garment cells that play the same role.</summary>
        static readonly Dictionary<string, string[]> Legacy = new Dictionary<string, string[]>
        {
            ["Batik"] = new[] { "batik_orange" }, ["Sarong"] = new[] { "sarong_green" }, ["Songkok"] = new[] { "songkok" },
            ["Kurung"] = new[] { "kurung_pink", "kurung_skirt" }, ["Tudung"] = new[] { "tudung_blue" },
            ["TshirtRed"] = new[] { "tshirt_red" }, ["Shorts"] = new[] { "shorts_blue", "kid_shorts", "dress_trim" },
            ["Tshirt"] = new[] { "dress_red" }, ["BatikBlue"] = new[] { "batik_blue" }, ["Pants"] = new[] { "pants_khaki" },
            ["Pastel3"] = new[] { "pastel_lilac" }, ["Pastel2"] = new[] { "pastel_mint" }, ["SarongRed"] = new[] { "sarong_red" },
            ["CarBlue"] = new[] { "car_paint" }, ["CarYellow"] = new[] { "car_paint" }, ["CarGreen"] = new[] { "car_paint" },
            ["White"] = new[] { "baju_white", "kid_shirt" }, ["PoliceBlue"] = new[] { "police_blue" }, ["Skin"] = new[] { "skin" },
        };

        static bool Load()
        {
            if (_table != null) return true;
            var ta = Resources.Load<TextAsset>("kl_palette");
            if (ta == null) return false;
            _table = JsonUtility.FromJson<Table>(ta.text);
            _byName = _table.entries.ToDictionary(e => e.name, e => e);
            return true;
        }

        /// <summary>True if the renderer uses the shared KL atlas material (or a swap of it).</summary>
        public static bool IsKL(Material m)
        {
            if (m == null || !m.HasProperty("_BaseMap")) return false;
            var t = m.GetTexture("_BaseMap");
            return t != null && (t.name.StartsWith("kl_palette") || t.name.StartsWith("kl_character_palette"));
        }

        /// <summary>Resolve swap keys (KL cell names or legacy names) to KL cells.</summary>
        public static Dictionary<string, Color> Resolve(IDictionary<string, Color> swaps)
        {
            var result = new Dictionary<string, Color>();
            if (!Load()) return result;
            foreach (var kv in swaps)
            {
                if (_byName.ContainsKey(kv.Key)) result[kv.Key] = kv.Value;
                else if (Legacy.TryGetValue(kv.Key, out var cells))
                    foreach (var c in cells) if (_byName.ContainsKey(c)) result[c] = kv.Value;
            }
            return result;
        }

        static string Key(Dictionary<string, Color> swaps) =>
            string.Join(";", swaps.OrderBy(k => k.Key).Select(k => $"{k.Key}={ColorUtility.ToHtmlStringRGB(k.Value)}"));

        public static Texture2D Texture(Dictionary<string, Color> swaps, Texture2D source = null)
        {
            bool preserveSource = source && source.isReadable && source.name.StartsWith("kl_character_palette");
            var key = (preserveSource ? source : null, Key(swaps));
            if (Textures.TryGetValue(key, out var cached) && cached) return cached;
            int size = _table.cells * _table.cellPx;
            var px = preserveSource ? source.GetPixels32() : new Color32[size * size];
            foreach (var e in _table.entries)
            {
                // A tuned character atlas can differ from the environment's source table.
                // Only requested costume/crowd cells change; all other pixels stay intact.
                if (preserveSource && !swaps.ContainsKey(e.name)) continue;
                var c = swaps.TryGetValue(e.name, out var sw) ? sw : new Color(e.r, e.g, e.b);
                Color32 c32 = c;
                c32.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(e.alpha) * 255f);   // gloss / painted-surface code
                int cx = e.index % _table.cells, cy = e.index / _table.cells;
                for (int y = 0; y < _table.cellPx; y++)
                {
                    int row = size - 1 - (cy * _table.cellPx + y);           // PNG rows run bottom-up in Unity
                    for (int x = 0; x < _table.cellPx; x++)
                    {
                        int index = row * size + cx * _table.cellPx + x;
                        if (preserveSource) c32.a = px[index].a;
                        px[index] = c32;
                    }
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = (preserveSource ? "kl_character_palette~" : "kl_palette~") + Textures.Count,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, !preserveSource);
            Textures[key] = tex;
            return tex;
        }

        /// <summary>A cached copy of `baseMat` whose atlas has the given cells repainted.</summary>
        public static Material Swap(Material baseMat, IDictionary<string, Color> swaps)
        {
            var resolved = Resolve(swaps);
            if (resolved.Count == 0) return baseMat;
            var source = baseMat.GetTexture("_BaseMap") as Texture2D;
            // NPC skin variants should carry their ear/facial shading with them. Preserve
            // the authored tint ratio; a caller's explicit skin_shadow takes precedence.
            if (source && source.isReadable && source.name.StartsWith("kl_character_palette") &&
                resolved.TryGetValue("skin", out var skin) && !resolved.ContainsKey("skin_shadow") &&
                _byName.TryGetValue("skin", out var baseEntry) && _byName.TryGetValue("skin_shadow", out var shadowEntry))
            {
                Color Cell(Entry entry) => source.GetPixel((entry.index % _table.cells) * _table.cellPx,
                    source.height - 1 - (entry.index / _table.cells) * _table.cellPx);
                var oldSkin = Cell(baseEntry);
                var oldShadow = Cell(shadowEntry);
                resolved["skin_shadow"] = new Color(
                    Mathf.Clamp01(skin.r * oldShadow.r / Mathf.Max(oldSkin.r, 1f / 255f)),
                    Mathf.Clamp01(skin.g * oldShadow.g / Mathf.Max(oldSkin.g, 1f / 255f)),
                    Mathf.Clamp01(skin.b * oldShadow.b / Mathf.Max(oldSkin.b, 1f / 255f)));
            }
            string key = Key(resolved);
            var ck = (baseMat, key);
            if (Materials.TryGetValue(ck, out var m) && m) return m;
            m = new Material(baseMat) { name = baseMat.name + "~" + Materials.Count };
            m.SetTexture("_BaseMap", Texture(resolved, source));
            Materials[ck] = m;
            return m;
        }
    }
}
