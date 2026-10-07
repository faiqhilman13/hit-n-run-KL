using UnityEditor;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// Imports the architecture library's painted details (Resources/KLMap/arch_atlas.png, from
    /// Tools/klmap/gen_arch_atlas.py) as a Texture2DArray: the 8 x 8 sheet becomes 64 slices, top-left first,
    /// each mipmapped and tiling on its own so nothing bleeds between cells.
    /// </summary>
    public class ArchImport : AssetPostprocessor
    {
        public const string AtlasPath = "Assets/KampungRun/Resources/KLMap/arch_atlas.png";

        void OnPreprocessTexture()
        {
            if (assetPath != AtlasPath) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2DArray;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.flipbookColumns = 8;
            s.flipbookRows = 8;
            s.textureShape = TextureImporterShape.Texture2DArray;
            ti.SetTextureSettings(s);
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 4;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
