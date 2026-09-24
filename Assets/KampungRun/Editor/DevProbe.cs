using UnityEditor;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>Batch-mode diagnostics used while developing the import pipeline.</summary>
    public static class DevProbe
    {
        public static void ReimportModels()
        {
            AssetDatabase.ImportAsset("Assets/KampungRun/Shaders/LatInk.shader", ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/Models", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
            ProbeModels();
        }

        public static void ProbeModels()
        {
            foreach (var name in new[] { "Car_Saga", "Char_PakMat" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/{name}.fbx");
                if (go == null) { Debug.Log($"PROBE missing {name}"); continue; }
                foreach (Transform t in go.GetComponentsInChildren<Transform>())
                {
                    var r = t.GetComponent<Renderer>();
                    string b = r ? $" bounds c={r.bounds.center} s={r.bounds.size}" : "";
                    Debug.Log($"PROBE {name}/{t.name} lp={t.localPosition} lr={t.localRotation.eulerAngles} ls={t.localScale}{b}");
                }
                var mr = go.GetComponentInChildren<MeshRenderer>();
                if (mr) Debug.Log($"PROBE mat {mr.sharedMaterial.name} shader={mr.sharedMaterial.shader.name} col={mr.sharedMaterial.GetColor("_BaseColor")}");
            }
        }
    }
}
