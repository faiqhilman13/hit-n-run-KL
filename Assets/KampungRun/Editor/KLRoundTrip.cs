using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// The handoff's Unity round-trip check for KL assets: import, inspect scale/axis/avatar/
    /// clips, and render an in-engine proof shot next to a 1 m cube. Results go to
    /// deliverables/&lt;id&gt;/unity-import.png and unity-import.txt.
    /// Batch: -executeMethod KampungRun.EditorTools.KLRoundTrip.Run -kl chr_aiman
    /// </summary>
    public static class KLRoundTrip
    {
        const string Deliverables = @"C:\Users\User\Downloads\kl-asset-production-handoff\asset-production-handoff\deliverables";

        /// <summary>Handoff assets live in the handoff's deliverables; the rest of the KL cast (family,
        /// townsfolk) in the project's Tools/kl_assets - same layout either way.</summary>
        static string Dir(string id)
        {
            var handoff = Path.Combine(Deliverables, id);
            return Directory.Exists(handoff) ? handoff : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "kl_assets", id));
        }

        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-kl");
            var ids = i >= 0 ? args.Skip(i + 1).TakeWhile(a => !a.StartsWith("-")).ToArray() : new[] { "chr_aiman" };
            AssetDatabase.Refresh();
            foreach (var id in ids)
            {
                if (File.Exists(Path.Combine(Dir(id), "stats.json")) && File.ReadAllText(Path.Combine(Dir(id), "stats.json")).Contains("\"modules\"")) CheckKit(id);
                else Check(id);
            }
        }

        [MenuItem("Kampung Run/KL Round Trip (Aiman)")]
        public static void RunAiman() => Check("chr_aiman");

        public static void Check(string id)
        {
            string path = $"Assets/Models/KL/{id}.fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var sb = new StringBuilder();
            sb.AppendLine($"Unity {Application.unityVersion} / URP - round trip for {id}");
            if (prefab == null) { sb.AppendLine("FAILED: model did not import"); Write(id, sb); return; }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = Vector3.zero;
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);

            // --- avatar + clips
            var anim = go.GetComponent<Animator>();
            if (anim != null)
            {
                var av = anim.avatar;
                sb.AppendLine($"Animator: avatar={(av ? av.name : "none")} valid={(av && av.isValid)} human={(av && av.isHuman)}");
                if (av && av.isHuman)
                {
                    foreach (HumanBodyBones hb in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand,
                                 HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperLeg })
                    {
                        var t = anim.GetBoneTransform(hb);
                        sb.AppendLine($"  {hb,-14} -> {(t ? t.name : "UNMAPPED")}");
                    }
                }
            }
            var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<AnimationClip>().ToArray();
            sb.AppendLine($"Clips ({clips.Length}): " + string.Join(", ", clips.Select(c => $"{c.name} {c.length:F2}s{(c.isLooping ? " loop" : "")}")));

            // --- size, axis, blendshapes, materials
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var rends = go.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (smr)
            {
                // skinned renderer bounds are the importer's bind-space box: measure the posed mesh
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                var mtx = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one); // bake already scaled
                var vs = baked.vertices;
                b = new Bounds(mtx.MultiplyPoint3x4(vs[0]), Vector3.zero);
                foreach (var v in vs) b.Encapsulate(mtx.MultiplyPoint3x4(v));
                Object.DestroyImmediate(baked);
            }
            sb.AppendLine($"Bounds size (m): {b.size.x:F3} x {b.size.y:F3} x {b.size.z:F3}; min.y={b.min.y:F3}");
            if (anim != null && anim.isHuman)
            {
                // the toe joint straight from the skeleton (the avatar may leave Toes unmapped - it's optional)
                var toe = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "LeftToes");
                var foot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                var fwd = toe.position - foot.position;
                sb.AppendLine($"Forward check: toes-ankle = {fwd} -> faces {(fwd.z > 0 ? "+Z (OK)" : "-Z (WRONG)")}" +
                              $" (avatar toe mapping: {(anim.GetBoneTransform(HumanBodyBones.LeftToes) ? "yes" : "none - optional bone")})");
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                sb.AppendLine($"Joint heights (m): hips {hips.position.y:F3}, head {head.position.y:F3}; root scale {go.transform.lossyScale}, " +
                              $"mesh node scale {smr.transform.lossyScale}, importer globalScale {mi.globalScale} useFileScale {mi.useFileScale} fileScale {mi.fileScale}");
                var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                sb.AppendLine($"Handedness: left hand x = {lh.position.x:F2} ({(lh.position.x < 0 ? "left is -X, correct for +Z forward" : "mirrored?")})");
            }
            // rigid assemblies (vehicles, props): named parts, pivots, forward, collision proxy
            if (anim == null)
            {
                var all = go.GetComponentsInChildren<Transform>(true);
                Transform Find(string n) => all.FirstOrDefault(t => t.name == n);
                var fw = Find("FrontWheel") ?? Find("Wheel_FL"); var rw = Find("RearWheel") ?? Find("Wheel_RL");
                if (fw && rw)
                    sb.AppendLine($"Forward check: front wheel z={fw.position.z:F2}, rear z={rw.position.z:F2} -> faces " +
                                  $"{(fw.position.z > rw.position.z ? "+Z (OK)" : "-Z (WRONG)")}");
                foreach (var t in all.Where(t => t != go.transform))
                {
                    var mf = t.GetComponent<MeshFilter>();
                    sb.AppendLine($"  part {t.name,-24} pivot {t.localPosition} rot {t.localRotation.eulerAngles}" +
                                  (mf ? $" tris {mf.sharedMesh.triangles.Length / 3}" : ""));
                }
                var lgv = go.GetComponent<LODGroup>();
                if (lgv)
                {
                    var lods = lgv.GetLODs();
                    int T(LOD l) => l.renderers.Where(r => r).Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
                    sb.AppendLine($"LODGroup: {lods.Length} levels - " + string.Join(" / ", lods.Select((l, i) =>
                        $"LOD{i} {string.Join("+", l.renderers.Where(r => r).Select(r => r.name))} {T(l)} tris @{l.screenRelativeTransitionHeight:0.##}")));
                }
                var colT = all.FirstOrDefault(t => t.name.StartsWith("COL_"));
                sb.AppendLine(colT ? $"Collision proxy: {colT.name} (renderer hidden at runtime, used as collider)" : "Collision proxy: none");
                var mr = go.GetComponentInChildren<MeshRenderer>();
                if (mr) sb.AppendLine($"Material: {mr.sharedMaterial.name} shader={mr.sharedMaterial.shader.name} " +
                                      $"palette={(mr.sharedMaterial.GetTexture("_BaseMap") ? "kl_palette" : "MISSING")}");
                if (colT) colT.gameObject.SetActive(false);
            }
            if (smr != null)
            {
                var m = smr.sharedMesh;
                sb.AppendLine($"Mesh: {m.vertexCount} verts, {m.triangles.Length / 3} tris, {smr.sharedMaterials.Length} material(s), " +
                              $"{smr.bones.Length} bones, blendshapes: {string.Join(", ", Enumerable.Range(0, m.blendShapeCount).Select(m.GetBlendShapeName))}");
                sb.AppendLine($"Material: {smr.sharedMaterial.name} shader={smr.sharedMaterial.shader.name} " +
                              $"palette={(smr.sharedMaterial.GetTexture("_BaseMap") ? smr.sharedMaterial.GetTexture("_BaseMap").name : "MISSING")}");
            }

            // --- pose it (idle) and render the proof shot beside a 1 m cube
            var idle = clips.FirstOrDefault(c => c.name == "idle");
            if (idle != null)
            {
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(go, idle, 0.3f);
                AnimationMode.EndSampling();
            }
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Ref_1m";
            cube.transform.position = new Vector3(1.2f, 0.5f, 0);
            var mat = new Material(Shader.Find("KampungRun/LatInk"));
            mat.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f));
            cube.GetComponent<Renderer>().sharedMaterial = mat;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 2;
            var gmat = new Material(mat); gmat.SetColor("_BaseColor", new Color(0.55f, 0.75f, 0.4f));
            ground.GetComponent<Renderer>().sharedMaterial = gmat;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(45, -30, 0);
            sun.shadows = LightShadows.Soft;
            Shader.SetGlobalVector("_LatStyle", new Vector4(0.55f, 0f, 0.25f, 0.12f));
            Shader.SetGlobalVector("_LatStyle2", new Vector4(0f, 1.05f, 0f, 0f));
            Shader.SetGlobalFloat("_LatStyleSet", 1f);
            Shader.SetGlobalColor("_LatPaper", new Color(1, 0.98f, 0.92f, 1));
            Shader.SetGlobalColor("_LatShadow", new Color(0.68f, 0.7f, 0.9f, 1));
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.7f, 0.98f);
            cam.fieldOfView = 30;
            // three-quarter front view framed on the model (+Z-facing), the 1 m cube beside it
            float span = Mathf.Max(b.size.x, b.size.y, b.size.z, 1.8f);
            cube.transform.position = new Vector3(b.max.x + 0.8f, 0.5f, 0);
            var look = new Vector3((b.center.x + b.max.x + 0.8f) * 0.5f, b.center.y * 0.9f, b.center.z);
            cam.transform.position = look + new Vector3(0.45f, 0.35f, 1f).normalized * span * 3.2f;
            cam.transform.LookAt(look);
            var rt = new RenderTexture(900, 900, 24);
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); }
            RenderTexture.active = rt;
            var tex = new Texture2D(900, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            var dir = Dir(id);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "unity-import.png"), tex.EncodeToPNG());
            if (idle != null) AnimationMode.StopAnimationMode();
            sb.AppendLine("Proof render: unity-import.png (idle pose sampled at 0.3 s, 1 m cube at x = 1.2)");
            Write(id, sb);
        }

        // ------------------------------------------------------------------ kits (env_*)
        static readonly string[] TenMetreTiles = { "env_road_straight", "env_road_straight_wet", "env_road_corner", "env_road_t",
            "env_road_cross", "env_crosswalk", "env_kb_lane" };

        /// <summary>Round trip for a modular kit: every module FBX under Models/KL/env, with size,
        /// facing, collision proxy and LODGroup checks, a 10 m snap test for the road tiles and an
        /// in-engine lineup render.</summary>
        public static void CheckKit(string kitId)
        {
            var dir = Dir(kitId);
            var statsPath = Path.Combine(dir, "stats.json");
            var sb = new StringBuilder();
            sb.AppendLine($"Unity {Application.unityVersion} / URP - kit round trip for {kitId}");
            if (!File.Exists(statsPath)) { sb.AppendLine("FAILED: stats.json missing (build the kit first)"); Write(kitId, sb); return; }
            var ids = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(statsPath), "\"id\": \"(env_[a-z0-9_]+)\"")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).ToArray();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            float x = 0f;
            var placed = new System.Collections.Generic.List<GameObject>();
            foreach (var mid in ids)
            {
                string path = $"Assets/Models/KL/env/{mid}.fbx";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"  {mid,-28} FAILED: did not import"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var all = go.GetComponentsInChildren<Transform>(true);
                var col = all.FirstOrDefault(t => t.name.StartsWith("COL_"));
                if (col) col.gameObject.SetActive(false);
                var rends = go.GetComponentsInChildren<Renderer>().Where(r => !r.name.EndsWith("_LOD1")).ToArray();
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                var lg = go.GetComponent<LODGroup>();
                string lod = "no LOD";
                if (lg)
                {
                    var lods = lg.GetLODs();
                    int T(LOD l) => l.renderers.Where(r => r).Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
                    lod = $"LODGroup {lods.Length} levels: " + string.Join(" / ", lods.Select((l, i) => $"LOD{i} {T(l)} tris @{l.screenRelativeTransitionHeight:0.##}"));
                }
                string snap = "";
                if (TenMetreTiles.Contains(mid))
                {
                    bool ok = Mathf.Abs(b.size.x - 10f) < 0.05f && Mathf.Abs(b.size.z - 10f) < 0.05f &&
                              Mathf.Abs(b.center.x) < 0.05f && Mathf.Abs(b.center.z) < 0.05f && Mathf.Abs(b.max.y) < 0.06f;
                    snap = ok ? " | snaps: 10 x 10 m, centred, surface y=0 (OK)" : " | snaps: WRONG footprint";
                }
                sb.AppendLine($"  {mid,-28} size {b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2} m, min.y {b.min.y:F2}, " +
                              $"collision {(col ? "COL proxy" : "none (walk-through)")}, {lod}{snap}");
                go.transform.position = new Vector3(x + b.extents.x - b.center.x, 0, -b.center.z);
                x += b.size.x + 1.5f;
                placed.Add(go);
            }
            // snap test: a 3 x 3 patch of road tiles laid on the 10 m grid must leave no seams
            var straight = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/KL/env/env_road_straight.fbx");
            var cross = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/KL/env/env_road_cross.fbx");
            if (straight && cross && ids.Contains("env_road_straight"))
            {
                float covered = 0f;
                for (int i = -1; i <= 1; i++)
                    for (int k = -1; k <= 1; k++)
                    {
                        var t = (GameObject)PrefabUtility.InstantiatePrefab(i == 0 && k == 0 ? cross : straight);
                        t.transform.position = new Vector3(i * 10f, 0, -30f + k * 10f);
                        covered += t.GetComponentsInChildren<Renderer>().Where(r => !r.name.EndsWith("_LOD1")).Max(r => r.bounds.size.x * r.bounds.size.z);
                    }
                sb.AppendLine($"Snap test: 3 x 3 tiles on the 10 m grid cover {covered:F1} m2 of 900 m2 -> " +
                              $"{(Mathf.Abs(covered - 900f) < 5f ? "seamless (OK)" : "gaps/overlaps (WRONG)")}");
            }
            var mr = placed.Count > 0 ? placed[0].GetComponentInChildren<MeshRenderer>() : null;
            if (mr) sb.AppendLine($"Material: {mr.sharedMaterial.name} shader={mr.sharedMaterial.shader.name} " +
                                  $"palette={(mr.sharedMaterial.GetTexture("_BaseMap") ? "kl_palette" : "MISSING")}");
            sb.AppendLine("Facing: modules are authored front = -Y and turned at assembly; fronts face Unity +Z (the render looks from +Z).");
            // lineup render from the front (+Z), sun from the upper left
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(40, 150, 0);
            sun.shadows = LightShadows.Soft;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.position = new Vector3(x / 2, -0.02f, -10);
            ground.transform.localScale = new Vector3(x / 8 + 4, 1, 8);
            var gmat = new Material(Shader.Find("KampungRun/LatInk")); gmat.SetColor("_BaseColor", new Color(0.62f, 0.78f, 0.45f));
            ground.GetComponent<Renderer>().sharedMaterial = gmat;
            Shader.SetGlobalVector("_LatStyle", new Vector4(0.55f, 0f, 0.25f, 0.12f));
            Shader.SetGlobalVector("_LatStyle2", new Vector4(0f, 1.05f, 0f, 0f));
            Shader.SetGlobalFloat("_LatStyleSet", 1f);
            Shader.SetGlobalColor("_LatPaper", new Color(1, 0.98f, 0.92f, 1));
            Shader.SetGlobalColor("_LatShadow", new Color(0.68f, 0.7f, 0.9f, 1));
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.7f, 0.98f);
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(6f, x / 2f / (1800f / 700f)) * 1.08f;
            cam.transform.position = new Vector3(x / 2, 30f, 60f);
            cam.transform.LookAt(new Vector3(x / 2, 2f, 0));
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 400f;
            var rt = new RenderTexture(1800, 700, 24);
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); }
            RenderTexture.active = rt;
            var tex = new Texture2D(1800, 700, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1800, 700), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, "unity-import.png"), tex.EncodeToPNG());
            sb.AppendLine($"Proof render: unity-import.png ({placed.Count} modules imported and placed, viewed from the front)");
            Write(kitId, sb);
        }

        static void Write(string id, StringBuilder sb)
        {
            var dir = Dir(id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "unity-import.txt"), sb.ToString());
            WriteNotes(id, dir, sb.ToString());
            Debug.Log("[KL] " + sb);
        }

        /// <summary>NOTES.md per the handoff: size, tris, materials, rig/clips, what was tested.</summary>
        static void WriteNotes(string id, string dir, string unityReport)
        {
            string statsPath = Path.Combine(dir, "stats.json");
            string stats = File.Exists(statsPath) ? File.ReadAllText(statsPath) : "{}";
            string decisions = File.Exists(Path.Combine(dir, "decisions.md")) ? File.ReadAllText(Path.Combine(dir, "decisions.md")) : "";
            bool ok = !unityReport.Contains("FAILED") && !unityReport.Contains("WRONG") && !unityReport.Contains("UNMAPPED");
            var sb = new StringBuilder();
            sb.AppendLine($"# {id}");
            sb.AppendLine();
            sb.AppendLine($"**Status:** {(ok ? "exported, Unity round trip PASSED" : "exported, Unity round trip has issues (see below)")}");
            sb.AppendLine();
            sb.AppendLine("## Files");
            sb.AppendLine($"- `{id}.blend` - editable source (collections EXPORT / COLLISION / REFERENCE)");
            sb.AppendLine($"- `{id}.fbx` - export (Forward +Z / Up +Y in Unity, 1 unit = 1 m)");
            sb.AppendLine("- `textures/kl_palette.png` - shared flat-colour atlas (point-filtered in Unity)");
            sb.AppendLine("- `previews/` - three-quarter + front/side/back (Blender Workbench), `unity-import.png` - in-engine proof");
            sb.AppendLine();
            sb.AppendLine("## Blender stats (from the build script)");
            sb.AppendLine("```json");
            sb.AppendLine(stats.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("## Unity import (actually tested)");
            sb.AppendLine("```");
            sb.AppendLine(unityReport.Trim());
            sb.AppendLine("```");
            if (decisions.Length > 0) { sb.AppendLine(); sb.AppendLine(decisions.Trim()); }
            File.WriteAllText(Path.Combine(dir, "NOTES.md"), sb.ToString());
        }
    }
}
