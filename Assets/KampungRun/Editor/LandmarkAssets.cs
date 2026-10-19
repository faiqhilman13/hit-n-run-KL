using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// The kit-3 landmarks (Tools/blender/kl/kl_landmarks3.py, Assets/Models/KL/env/env_lm3_*.fbx) in the GameAssets
    /// registry, inserted in name order (as VehicleArtCheck does for cars) without rebuilding the scene.
    ///   Unity -batchmode -projectPath . -executeMethod KampungRun.EditorTools.LandmarkAssets.Register -quit
    /// </summary>
    public static class LandmarkAssets
    {
        public static void Register()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var ga = AssetDatabase.LoadAssetAtPath<GameAssets>("Assets/KampungRun/Resources/GameAssets.asset");
            if (ga == null) { Debug.LogError("[Landmarks] no GameAssets"); return; }
            int added = 0;
            foreach (var guid in AssetDatabase.FindAssets("env_lm3_ t:Model", new[] { "Assets/Models/KL/env" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !m.name.StartsWith("env_lm3_") || ga.models.Contains(m)) continue;
                int i = ga.models.FindIndex(x => x != null && string.CompareOrdinal(x.name, m.name) > 0);
                ga.models.Insert(i < 0 ? ga.models.Count : i, m);
                added++;
            }
            EditorUtility.SetDirty(ga);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Landmarks] registered {added} new kit-3 models; {ga.models.Count(x => x && x.name.StartsWith("env_lm3_"))} in all");
        }
    }
}
