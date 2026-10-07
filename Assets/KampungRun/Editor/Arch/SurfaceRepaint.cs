using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// Refill Resources/SurfaceArray.asset from Textures/Surfaces/surf_NN_*.png (Tools/gen_surfaces.py) in place, so
    /// its GUID (and GameAssets' reference to it) survives. ProjectBuilder's own BuildSurfaceArray deletes and
    /// recreates it, which only works inside the full "Rebuild Game Assets".
    ///   Unity -batchmode -projectPath . -executeMethod KampungRun.EditorTools.SurfaceRepaint.Run -quit
    /// </summary>
    public static class SurfaceRepaint
    {
        public static void Run()
        {
            const string dir = "Assets/KampungRun/Textures/Surfaces";
            const string path = "Assets/KampungRun/Resources/SurfaceArray.asset";
            var arr = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
            if (arr == null) { Debug.LogError("[Surfaces] " + path + " missing"); return; }
            var files = Directory.GetFiles(dir, "surf_*.png").OrderBy(f => f).ToArray();
            for (int i = 0; i < files.Length && i < arr.depth; i++)
            {
                AssetDatabase.ImportAsset(files[i].Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(files[i].Replace('\\', '/'));
                if (t == null || t.width != arr.width) { Debug.LogWarning("[Surfaces] skipped " + files[i]); continue; }
                arr.SetPixels(t.GetPixels(), i, 0);
            }
            arr.Apply(true, false);
            EditorUtility.SetDirty(arr);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Surfaces] refilled {Mathf.Min(files.Length, arr.depth)} slices of {path} in place");
        }
    }
}
