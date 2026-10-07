using UnityEngine;

namespace KampungRun.Arch
{
    /// <summary>The architecture library's shared look: one LatInk material in its arch mode over the ArchTex array.</summary>
    public static class ArchKit
    {
        static Material _mat;
        static Texture2DArray _tex;

        /// <summary>The one material every arch mesh draws with (the SRP batcher keeps them all in one batch).</summary>
        public static Material Material
        {
            get
            {
                if (_mat == null)
                {
                    Init();
                    _mat = new Material(GameAssets.I.latInk) { name = "Arch", enableInstancing = false };
                    _mat.EnableKeyword("_ARCH");
                    _mat.SetColor("_BaseColor", Color.white);
                    _mat.SetFloat("_OutlineWidth", 0f);
                    _mat.SetFloat("_Surface", 0f);
                    _mat.SetFloat("_Gloss", 1f);
                }
                return _mat;
            }
        }

        public static void Init()
        {
            if (_tex == null) _tex = Resources.Load<Texture2DArray>("KLMap/arch_atlas");
            if (_tex != null) Shader.SetGlobalTexture("_ArchArray", _tex);
            else Debug.LogError("[Arch] Resources/KLMap/arch_atlas is missing or not imported as a 2D array");
        }
    }
}
