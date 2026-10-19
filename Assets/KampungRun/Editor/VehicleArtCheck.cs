using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// Checks the imported Hit &amp; Run car models against the runtime part-name contract (Vehicle,
    /// VehicleVisuals, SeatFit, the roof tests) and reports triangle counts per LOD.
    /// Batch: -executeMethod KampungRun.EditorTools.VehicleArtCheck.Run  (report: Tools/promo_frames/x_cars/vehicle_report.txt)
    /// </summary>
    public static class VehicleArtCheck
    {
        static readonly string[] Cars = { "veh_saga", "veh_myvi", "veh_kancil", "veh_hilux", "veh_alphard", "veh_teksi", "veh_polis",
                                          "veh_kapcai", "veh_iriz", "veh_mpv", "veh_lorry", "veh_food_truck", "veh_delivery_bike", "veh_taxi" };
        static readonly string[] CarParts = { "Body", "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR", "SteeringWheel", "SteeringAxis",
                                              "Seat_Driver", "HeadLights", "BrakeLights", "Glass", "FX_Exhaust" };
        static readonly string[] BikeParts = { "Body", "FrontWheel", "RearWheel", "Steering", "Seat_Rider", "HeadLights" };

        /// <summary>Force-reimport every script (repairs a copied Library whose MonoScripts went stale).</summary>
        public static void ReimportScripts()
        {
            AssetDatabase.ImportAsset("Assets/KampungRun/Scripts", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/KampungRun/Tests", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            Debug.Log("[VehicleArtCheck] scripts reimported");
        }

        /// <summary>Add any new Assets/Models/KL/veh_*.fbx to the GameAssets model registry (ModelFactory spawns from
        /// it), in the same name order Rebuild Game Assets uses, without touching anything else in the registry.</summary>
        public static int RegisterVehicleModels()
        {
            var ga = AssetDatabase.LoadAssetAtPath<GameAssets>("Assets/KampungRun/Resources/GameAssets.asset");
            if (ga == null) return 0;
            int added = 0;
            foreach (var guid in AssetDatabase.FindAssets("veh_ t:Model", new[] { "Assets/Models/KL" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !m.name.StartsWith("veh_") || ga.models.Contains(m)) continue;
                int i = ga.models.FindIndex(x => x != null && string.Compare(x.name, m.name) > 0);
                ga.models.Insert(i < 0 ? ga.models.Count : i, m);
                added++;
            }
            if (added > 0)
            {
                EditorUtility.SetDirty(ga);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[VehicleArtCheck] registered {added} new vehicle models");
            return added;
        }

        [MenuItem("Kampung Run/Check Vehicle Art")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            RegisterVehicleModels();
            var sb = new StringBuilder();
            int problems = 0;
            foreach (var id in Cars)
            {
                var path = $"Assets/Models/KL/{id}.fbx";
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) { sb.AppendLine($"{id}: missing"); continue; }
                var all = go.GetComponentsInChildren<Transform>(true);
                Transform F(string n) => all.FirstOrDefault(t => t.name == n);
                bool bike = F("FrontWheel") != null;
                var need = bike ? BikeParts : CarParts;
                var missing = need.Where(n => F(n) == null).ToList();
                if (id == "veh_food_truck" || id == "veh_delivery_bike" || id == "veh_taxi") missing.Clear();   // legacy-style traffic models
                problems += missing.Count;
                int Tris(Renderer r) { var f = r.GetComponent<MeshFilter>(); return f && f.sharedMesh ? f.sharedMesh.triangles.Length / 3 : 0; }
                var groups = go.GetComponentsInChildren<LODGroup>(true);
                var inGroup = new HashSet<Renderer>();
                int near = 0, far = 0, always = 0, optional = 0;
                foreach (var g in groups)
                {
                    var lods = g.GetLODs();
                    foreach (var r in lods[0].renderers) if (r && inGroup.Add(r)) { if (r.name.StartsWith("Var_")) optional += Tris(r); else near += Tris(r); }
                    if (lods.Length > 1) foreach (var r in lods[lods.Length - 1].renderers) if (r && inGroup.Add(r)) far += Tris(r);
                }
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (inGroup.Contains(r) || r.name.StartsWith("COL_")) continue;
                    if (r.name.StartsWith("Var_")) optional += Tris(r); else always += Tris(r);
                }
                int vars = all.Count(t => t.name.StartsWith("Var_"));
                int varsOn = all.Count(t => t.name.StartsWith("Var_") && t.gameObject.activeSelf);
                sb.AppendLine($"{id}: near (LOD0) {near + always} tris, far (LOD1) {far + always} tris (always drawn {always}); " +
                              $"LODGroups {groups.Length} [{string.Join(", ", groups.Select(g => g.name + ":" + g.lodCount + "@" + g.GetLODs()[0].screenRelativeTransitionHeight))}]; " +
                              $"optional parts {vars} ({varsOn} on at import), all options together {optional} tris");
                if (vars > 0) sb.AppendLine("   groups: " + string.Join(" ", all.Where(t => t.name.StartsWith("Var_"))
                    .Select(t => string.Join("_", t.name.Split('_').Skip(1).Take(2))).Distinct()));
                if (missing.Count > 0) sb.AppendLine($"   MISSING parts: {string.Join(", ", missing)}");
                foreach (var w in new[] { "Wheel_FL", "Wheel_RR", "FrontWheel", "RearWheel" })
                {
                    var wt = F(w);
                    if (!wt) continue;
                    var mf = wt.GetComponent<MeshFilter>();
                    if (!mf || !mf.sharedMesh) { sb.AppendLine($"   {w}: NO MESH (wheel radius contract)"); problems++; continue; }
                    sb.AppendLine($"   {w}: mesh extents {mf.sharedMesh.bounds.extents.ToString("F3")} at {go.transform.InverseTransformPoint(wt.position).ToString("F2")}");
                }
                var sw = F("SteeringWheel");
                if (sw && F("Body") && F("Body_LOD1") && sw.GetComponent<MeshFilter>() && sw.GetComponent<MeshFilter>().sharedMesh)
                {
                    var e = sw.GetComponent<MeshFilter>().sharedMesh.bounds.extents;
                    float r = Mathf.Max(e.x, e.y, e.z);
                    sb.AppendLine($"   SteeringWheel: max extent {r:F3} (rim contract 0.188) at {go.transform.InverseTransformPoint(sw.position).ToString("F2")}");
                    if (Mathf.Abs(r - 0.188f) > 0.006f) { sb.AppendLine("   STEERING WHEEL EXTENT CHANGED"); problems++; }
                }
                foreach (var n in new[] { "Seat_Driver", "Seat_Rider", "Body" })
                    if (F(n)) sb.AppendLine($"   {n} at {go.transform.InverseTransformPoint(F(n).position).ToString("F2")}");
                var names = all.Select(t => t.name).Where(n => n.StartsWith("Door_") || n.StartsWith("SlideDoor") || n.Contains("Glass") ||
                                                               n.StartsWith("COL_") || n.EndsWith("Lights") || n == "LightBar" || n == "RoofSign");
                sb.AppendLine($"   parts: {string.Join(" ", names)}");
            }
            sb.AppendLine($"problems: {problems}");
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "x_cars"));
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "vehicle_report.txt"), sb.ToString());
            Debug.Log("[VehicleArtCheck]\n" + sb);
        }
    }
}
