using UnityEditor;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>Batch diagnostics for the worktree: do the scene's scripts resolve to classes?</summary>
    public static class InteriorProbe
    {
        /// <summary>Reimport every script so its MonoScript points at its class again (a copied Library can leave
        /// them blank: scenes then load with "missing script" and the game never boots).</summary>
        public static void FixScripts()
        {
            foreach (var dir in new[] { "Assets/KampungRun/Scripts", "Assets/KampungRun/Tests", "Assets/KampungRun/Editor" })
                AssetDatabase.ImportAsset(dir, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);
            Scripts();
        }

        public static void Scripts()
        {
            foreach (var path in new[] { "Assets/KampungRun/Scripts/Core/GameManager.cs", "Assets/KampungRun/Scripts/Core/ChaseCamera.cs" })
            {
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                Debug.Log($"PROBE {path}: monoscript {(ms != null)} class {(ms != null ? ms.GetClass()?.FullName : "-")} guid {AssetDatabase.AssetPathToGUID(path)}");
            }
            Debug.Log($"PROBE type lookup: {System.Type.GetType("KampungRun.GameManager, KampungRun")}");
        }
    }
}
