using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KampungRun
{
    /// <summary>
    /// The city's painted shade, the way Hit &amp; Run lights Springfield: there nothing is lit per frame; the shading
    /// is baked into every vertex, dark at the foot of walls, in corners and down narrow streets. Here, once the
    /// city is built, every static renderer is drawn from straight above into a height map (a texel a metre, the
    /// highest surface winning). One GPU pass (Resources/CityShadeBake.shader) then works out how much sky each spot
    /// at street level sees past the roofs, awnings, decks and crowns around it, from a horizon search in 16
    /// directions, and a second pass smooths it. LatInk reads the result in its Hit &amp; Run mode. Streets darken
    /// beside walls, under awnings, flyovers and trees and down lanes. Walls darken toward their feet and fade back
    /// to full light going up. It costs one texture read per pixel, and the bake runs once at load.
    /// </summary>
    public static class CityShade
    {
        public const float Street = 0.18f;          // pavement and block-top level (CityBuilder.G)
        /// <summary>Renderers with this rendering-layer bit keep clean colours (cars: their paint isn't the street's).</summary>
        public const uint NoShade = 1u << 8;

        /// <summary>x how strong (0 = off), y how much of the sunlight it takes too, z the darker band at the foot
        /// of walls, w street level.</summary>
        public static Vector4 Look = new Vector4(1f, 0.85f, 0.4f, Street);
        /// <summary>How far the shade leans toward the level's shadow colour (0 = plain darker).</summary>
        public static float Tint = 0.5f;

        public static RenderTexture Map { get; private set; }
        public static bool On { get; private set; }

        static readonly int MapId = Shader.PropertyToID("_CityShade");
        static readonly int RectId = Shader.PropertyToID("_CityShadeRect");
        static readonly int ParamsId = Shader.PropertyToID("_CityShadeParams");
        static readonly int TintId = Shader.PropertyToID("_CityShadeTint");

        public static void Enable(bool on)
        {
            On = on && Map != null;
            Shader.SetGlobalVector(ParamsId, On ? Look : new Vector4(0f, 0f, 0f, Street));
            Shader.SetGlobalFloat(TintId, On ? Tint : 0f);
        }

        /// <summary>Bake the shade over a world rectangle (x, z) from every static renderer under root.</summary>
        public static void Bake(Transform root, Rect area, float cell)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (Map != null) { Map.Release(); Kill(Map); Map = null; }
            var shader = Resources.Load<Shader>("CityShadeBake");
            // the height map needs a float render target (WebGL 2 has one wherever EXT_color_buffer_float is)
            var hfFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat) ? RenderTextureFormat.RFloat : RenderTextureFormat.RHalf;
            if (shader == null || !shader.isSupported || !SystemInfo.SupportsRenderTextureFormat(hfFormat))
            {
                Debug.LogWarning("[Shade] can't bake here (shader or float target missing): the city keeps its plain lighting");
                Enable(false);
                return;
            }
            int w = Mathf.CeilToInt(area.width / cell), d = Mathf.CeilToInt(area.height / cell);
            area = new Rect(area.xMin, area.yMin, w * cell, d * cell);
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetVector("_Bake", new Vector4(cell, Street + 0.3f, 0f, 0f));

            // the heights: every static renderer drawn from straight above, the highest surface winning the depth test
            var heights = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, d, hfFormat, 24) { sRGB = false });
            heights.filterMode = FilterMode.Point;
            heights.wrapMode = TextureWrapMode.Clamp;
            const float Top = 600f;                          // over Merdeka 118's spire
            var cmd = new CommandBuffer { name = "CityShade heights" };
            cmd.SetRenderTarget(heights);
            cmd.ClearRenderTarget(true, true, new Color(Street, 0f, 0f, 0f));
            var eye = Matrix4x4.TRS(new Vector3(area.center.x, Top, area.center.y), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            cmd.SetViewProjectionMatrices(Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * eye.inverse,
                Matrix4x4.Ortho(-area.width * 0.5f, area.width * 0.5f, -area.height * 0.5f, area.height * 0.5f, 1f, Top + 40f));
            int drawn = 0;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!mr.enabled || mr.bounds.max.y < Street + 0.6f) continue;               // the street itself
                int layer = mr.gameObject.layer;
                if (layer == Layers.Vehicle || layer == Layers.Character || layer == Layers.Pickup || mr.GetComponent<TextMesh>()) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) cmd.DrawRenderer(mr, mat, s, 2);
                drawn++;
            }
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            // how much sky each texel sees, then smoothed into the map LatInk reads
            var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
            var raw = RenderTexture.GetTemporary(w, d, 0, fmt, RenderTextureReadWrite.Linear);
            raw.filterMode = FilterMode.Bilinear;
            Graphics.Blit(heights, raw, mat, 0);
            RenderTexture.ReleaseTemporary(heights);
            Map = new RenderTexture(w, d, 0, fmt, RenderTextureReadWrite.Linear)
            {
                name = "CityShade", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false,
            };
            Map.Create();
            Graphics.Blit(raw, Map, mat, 1);
            RenderTexture.ReleaseTemporary(raw);
            Kill(mat);

            Shader.SetGlobalTexture(MapId, Map);
            Shader.SetGlobalVector(RectId, new Vector4(area.xMin, area.yMin, 1f / area.width, 1f / area.height));
            _area = area;
            Enable(true);
            Debug.Log($"[Shade] baked {w}x{d} at {cell} m ({hfFormat}) from {drawn} renderers in {clock.ElapsedMilliseconds} ms");
        }

        static Rect _area;

        /// <summary>Destroy that also works in the editor's review tools (outside play mode).</summary>
        static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// <summary>A copy of part of the map (world x, z), white = open sky, for checking the bake by eye.</summary>
        public static Texture2D Snapshot(Rect world, int px)
        {
            if (Map == null) return null;
            var tmp = RenderTexture.GetTemporary(px, px, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var scale = new Vector2(world.width / _area.width, world.height / _area.height);
            var offset = new Vector2((world.xMin - _area.xMin) / _area.width, (world.yMin - _area.yMin) / _area.height);
            Graphics.Blit(Map, tmp, scale, offset);
            var prev = RenderTexture.active;
            RenderTexture.active = tmp;
            var tex = new Texture2D(px, px, TextureFormat.RGB24, false, true);
            tex.ReadPixels(new Rect(0, 0, px, px), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);
            return tex;
        }

        /// <summary>Switch real-time shadows off (or back on) for everything in the city that casts them: Hit &amp; Run's
        /// world casts none, its shade is all painted in. Characters and cars keep theirs.</summary>
        public static void WorldShadows(Transform root, bool on)
        {
            if (!on)
            {
                _casting.Clear();
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                    if (mr.shadowCastingMode != ShadowCastingMode.Off) { _casting.Add((mr, mr.shadowCastingMode)); mr.shadowCastingMode = ShadowCastingMode.Off; }
            }
            else
            {
                foreach (var (mr, mode) in _casting) if (mr) mr.shadowCastingMode = mode;
                _casting.Clear();
            }
        }

        static readonly List<(MeshRenderer, ShadowCastingMode)> _casting = new List<(MeshRenderer, ShadowCastingMode)>();

    }
}
