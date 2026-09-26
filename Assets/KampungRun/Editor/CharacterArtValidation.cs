using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// Import and render the human cast without saving a game scene or rebuilding its
    /// controller. Batch entry: -executeMethod KampungRun.EditorTools.CharacterArtValidation.Run
    /// Optional: -characterIds chr_pakmat,chr_maksom -characterOutput absolute-directory
    /// </summary>
    public static class CharacterArtValidation
    {
        [Serializable] class PaletteEntry { public string name; public int index; }
        [Serializable] class PaletteTable { public int cells; public int cellPx; public PaletteEntry[] entries; }
        static readonly string[] Cast = { "chr_pakmat", "chr_maksom", "chr_along", "chr_adik", "chr_aiman",
            "chr_mei", "chr_ravi", "chr_townman", "chr_townaunty", "chr_pakcik", "chr_kid", "chr_polis", "chr_datukmega" };
        static readonly string[] Clips = { "idle", "walk", "run", "jump", "punch", "kick", "knockdown",
            "ride", "mount", "dismount", "sit", "wave", "panic" };
        static readonly HumanBodyBones[] Bones = { HumanBodyBones.Hips, HumanBodyBones.Spine,
            HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };

        [MenuItem("Kampung Run/Validate Refined Character Art")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before validating imported character art.");
            if (AnimationMode.InAnimationMode()) throw new InvalidOperationException("End the current animation preview before validating character art.");
            string Arg(string name, string fallback)
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
            }
            var ids = Arg("-characterIds", string.Join(",", Cast)).Split(',');
            string output = Arg("-characterOutput", Path.GetFullPath(Path.Combine(Application.dataPath,
                "..", "docs", "character-concepts", "round-02", "unity")));
            Directory.CreateDirectory(output);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var results = new StringBuilder("Refined character Unity verification\n");
            results.AppendLine($"Unity {Application.unityVersion}; UTC {DateTime.UtcNow:O}");
            int errors = 0;
            void Require(bool condition, string message)
            {
                results.AppendLine((condition ? "PASS " : "FAIL ") + message);
                if (!condition) errors++;
            }
            var registry = Resources.Load<GameAssets>("GameAssets");
            Require(registry && registry.humanController, "GameAssets keeps the shared humanoid controller");
            if (registry && registry.humanController)
            {
                Require(registry.humanController.animationClips.All(c => c), "Shared controller has no missing clip references");
                foreach (string clip in Clips)
                    Require(registry.humanController.animationClips.Any(c => c && c.name == clip), "Shared controller still references " + clip);
            }

            var previousScene = SceneManager.GetActiveScene();
            // Headless Unity starts with an untitled scene, which cannot accept an
            // additive scene. It is safe to replace that transient scene in batch mode.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var globals = new[] { "_ShadeSky", "_ShadeGround", "_LatStyle", "_LatStyle2", "_LatPaper", "_LatShadow" };
            var priorGlobals = globals.ToDictionary(n => n, Shader.GetGlobalVector);
            float oldMode = Shader.GetGlobalFloat("_ShadeMode");
            float oldStyle = Shader.GetGlobalFloat("_LatStyleSet");
            var temporary = new List<Object>();
            try
            {
                Shader.SetGlobalFloat("_ShadeMode", 1);
                Shader.SetGlobalFloat("_LatStyleSet", 1);
                Shader.SetGlobalVector("_LatStyle", Vector4.zero);
                Shader.SetGlobalVector("_LatStyle2", new Vector4(0, 1, 0, 0));
                Shader.SetGlobalColor("_ShadeSky", new Color(0.31f, 0.37f, 0.46f, 1));
                Shader.SetGlobalColor("_ShadeGround", new Color(0.23f, 0.20f, 0.17f, 1));
                RenderSettings.fog = false;
                var sun = new GameObject("CharacterProof_Key").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = new Color(1f, 0.91f, 0.82f);
                sun.intensity = 1.15f;
                sun.transform.rotation = Quaternion.Euler(35, 150, 0);
                sun.shadows = LightShadows.Soft;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "CharacterProof_Ground";
                ground.transform.position = new Vector3(0, -0.015f, 0);
                ground.transform.localScale = Vector3.one * 4;
                var groundMaterial = new Material(Shader.Find("KampungRun/LatInk"));
                groundMaterial.SetColor("_BaseColor", new Color(0.70f, 0.70f, 0.66f));
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                temporary.Add(groundMaterial);
                var camera = new GameObject("CharacterProof_Camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.82f, 0.84f, 0.82f);
                camera.fieldOfView = 29;
                camera.nearClipPlane = 0.03f;
                camera.farClipPlane = 100;
                camera.allowHDR = false;
                camera.allowMSAA = true;
                camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;

                foreach (string id in ids)
                {
                    results.AppendLine("\n" + id);
                    string path = "Assets/Models/KL/" + id + ".fbx";
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    // Reimporting a referenced FBX can invalidate previously loaded dependent assets.
                    registry = AssetDatabase.LoadAssetAtPath<GameAssets>("Assets/KampungRun/Resources/GameAssets.asset");
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    Require(prefab, "Model imported at stable asset path");
                    if (!prefab) continue;
                    Require(registry && registry.models.Contains(prefab), "GameAssets registry references this imported model");
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var animator = go.GetComponentInChildren<Animator>();
                    Require(animator && animator.avatar && animator.avatar.isValid && animator.avatar.isHuman, "Valid humanoid avatar");
                    if (!animator || !animator.avatar || !animator.avatar.isHuman) { Object.DestroyImmediate(go); continue; }
                    foreach (var bone in Bones) Require(animator.GetBoneTransform(bone), "Humanoid bone " + bone);
                    var toe = go.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "LeftToes");
                    Require(toe && toe.position.z > animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.z, "Faces Unity +Z");
                    var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
                    foreach (string clip in Clips) Require(clips.Any(c => c.name == clip && c.length > 0), "Clip " + clip);
                    var meshes = go.GetComponentsInChildren<SkinnedMeshRenderer>();
                    Require(meshes.Length > 0, "Has skinned character mesh");
                    foreach (var mesh in meshes)
                    {
                        Require(mesh.sharedMesh && mesh.bones.Length >= 14, mesh.name + " has weighted humanoid geometry");
                        foreach (var material in mesh.sharedMaterials)
                            Require(material && material.shader.name == "KampungRun/CharacterSoft" && KLPalette.IsKL(material),
                                mesh.name + " has soft character shader and swappable atlas");
                        results.AppendLine($"INFO {mesh.name}: {mesh.sharedMesh.vertexCount} vertices; {mesh.sharedMesh.triangles.Length / 3} triangles");
                    }
                    var face = meshes.FirstOrDefault(m => m.sharedMesh.blendShapeCount > 0);
                    Require(face && face.sharedMesh.GetBlendShapeIndex("Blink") >= 0, "Blink facial shape");
                    var group = go.GetComponentInChildren<LODGroup>();
                    Require(group && group.lodCount >= 2, "Two character LODs");
                    if (group) group.ForceLOD(0);
                    var restBounds = ModelFactory.LocalBounds(go);
                    Require(restBounds.min.y > -0.12f && restBounds.min.y < 0.2f,
                        "Authored feet near ground " + restBounds.min.y.ToString("F3"));
                    if (registry && registry.humanController)
                    {
                        // Sample the controller's shared clips directly; a live controller
                        // would overwrite the editor's sampled run/sit pose with its idle state.
                        animator.runtimeAnimatorController = null;
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        animator.applyRootMotion = false;
                        animator.Rebind();
                        AnimationMode.StartAnimationMode();
                        Sample(go, registry.humanController.animationClips.First(c => c.name == "idle"), 0.3f);
                    }
                    var bounds = ModelFactory.LocalBounds(go);
                    Vector3 idleHand = go.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
                    Require(bounds.size.y >= 0.9f && bounds.size.y <= 2.7f, "Metre scale height " + bounds.size.y.ToString("F3"));
                    Require(bounds.size.x < 2.5f && bounds.size.z < 1.6f, "Finite posed silhouette bounds " + bounds.size);
                    // AnimationMode includes clip root motion; position this isolated
                    // studio proof on its floor just as the gameplay capsule does.
                    go.transform.position = new Vector3(0, -bounds.min.y, 0);
                    var aim = new Vector3(0, bounds.size.y * 0.5f, 0);
                    camera.transform.position = aim + new Vector3(0.34f, 0.15f, 1f).normalized * bounds.size.y * 2.6f;
                    camera.transform.LookAt(aim);
                    Capture(camera, go, Path.Combine(output, id + "-idle.png"), 900, 1000);

                    if (meshes.Length > 0 && meshes[0].sharedMaterial)
                    {
                        var material = meshes[0].sharedMaterial;
                        var source = material.GetTexture("_BaseMap") as Texture2D;
                        Require(source && source.isReadable, "Character palette allows colour-preserving swaps");
                        if (source && source.isReadable)
                        {
                            var swapped = KLPalette.Swap(material, new Dictionary<string, Color> { ["Skin"] = new Color(0.6f, 0.4f, 0.25f) });
                            var texture = swapped.GetTexture("_BaseMap") as Texture2D;
                            var originalPixels = source.GetPixels32();
                            var swappedPixels = texture.GetPixels32();
                            int changed = originalPixels.Where((p, n) => !p.Equals(swappedPixels[n])).Count();
                            var table = JsonUtility.FromJson<PaletteTable>(Resources.Load<TextAsset>("kl_palette").text);
                            Require(changed > 0 && changed <= table.cellPx * table.cellPx * 2, "Skin swap changes only skin and its related shading cell");
                            Require(swapped.shader == material.shader, "Palette swap preserves character shader");
                            Color Cell(Texture2D atlas, string name)
                            {
                                var entry = table.entries.First(e => e.name == name);
                                return atlas.GetPixel(entry.index % table.cells * table.cellPx,
                                    atlas.height - 1 - entry.index / table.cells * table.cellPx);
                            }
                            var originalSkin = Cell(source, "skin");
                            var originalShade = Cell(source, "skin_shadow");
                            var newSkin = Cell(texture, "skin");
                            var newShade = Cell(texture, "skin_shadow");
                            var expectedShade = new Color(newSkin.r * originalShade.r / originalSkin.r,
                                newSkin.g * originalShade.g / originalSkin.g, newSkin.b * originalShade.b / originalSkin.b);
                            Require(Vector3.Distance(new Vector3(newShade.r, newShade.g, newShade.b),
                                new Vector3(expectedShade.r, expectedShade.g, expectedShade.b)) < 0.012f,
                                "NPC skin shading retains the authored skin-to-shadow colour ratio");
                            var overrideColour = new Color(0.13f, 0.21f, 0.29f);
                            var explicitShade = KLPalette.Swap(material, new Dictionary<string, Color>
                                { ["Skin"] = newSkin, ["skin_shadow"] = overrideColour });
                            var explicitPixel = Cell(explicitShade.GetTexture("_BaseMap") as Texture2D, "skin_shadow");
                            Require(Vector3.Distance(new Vector3(explicitPixel.r, explicitPixel.g, explicitPixel.b),
                                new Vector3(overrideColour.r, overrideColour.g, overrideColour.b)) < 0.008f,
                                "Explicit skin-shadow colour overrides the derived shade");
                        }
                    }

                    if (registry && registry.humanController)
                    {
                        var run = registry.humanController.animationClips.First(c => c.name == "run");
                        Sample(go, run, run.length * 0.25f);
                        Require(Vector3.Distance(idleHand, go.transform.InverseTransformPoint(
                            animator.GetBoneTransform(HumanBodyBones.LeftHand).position)) > 0.08f,
                            "Shared run clip moves the hand away from its idle pose");
                        Capture(camera, go, Path.Combine(output, id + "-run.png"), 900, 1000);
                        var sit = registry.humanController.animationClips.First(c => c.name == "sit");
                        Sample(go, sit, sit.length * 0.4f);
                        var leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position -
                            animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
                        var rightThigh = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg).position -
                            animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position;
                        Require(Vector3.Dot(leftThigh.normalized, Vector3.down) < 0.7f &&
                            Vector3.Dot(rightThigh.normalized, Vector3.down) < 0.7f, "Shared sitting clip bends both thighs");
                        // The isolated seated proof has no vehicle/SeatFit to lift the body.
                        // Keep the feet above the studio floor so all deformation is visible.
                        var seatedBounds = ModelFactory.LocalBounds(go);
                        go.transform.position = new Vector3(0, -seatedBounds.min.y, 0);
                        Capture(camera, go, Path.Combine(output, id + "-sit.png"), 900, 1000);
                    }
                    if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                    Object.DestroyImmediate(go);
                }
                var shader = Shader.Find("KampungRun/CharacterSoft");
                Require(shader && !ShaderUtil.ShaderHasError(shader), "Character shader compiled without errors");
            }
            catch (Exception exception)
            {
                errors++;
                results.AppendLine("ERROR " + exception);
                throw;
            }
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                foreach (var resource in temporary) Object.DestroyImmediate(resource);
                Shader.SetGlobalFloat("_ShadeMode", oldMode);
                Shader.SetGlobalFloat("_LatStyleSet", oldStyle);
                foreach (var pair in priorGlobals) Shader.SetGlobalVector(pair.Key, pair.Value);
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                if (!Application.isBatchMode) EditorSceneManager.CloseScene(scene, true);
                results.AppendLine($"\nResult: {errors} failed checks.");
                File.WriteAllText(Path.Combine(output, "validation.txt"), results.ToString());
            }
            if (errors > 0) throw new InvalidOperationException($"Character art validation failed {errors} checks. See {output}/validation.txt");
            Debug.Log("Character art verification passed: " + output);
        }

        static void Sample(GameObject go, AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(go, clip, time);
            AnimationMode.EndSampling();
        }

        static void Capture(Camera camera, GameObject character, string path, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var previous = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var skin = character.GetComponentsInChildren<SkinnedMeshRenderer>();
            var enabled = skin.Select(s => s.enabled).ToArray();
            var proofMeshes = new List<Mesh>();
            var proofObjects = new List<GameObject>();
            try
            {
                // A headless editor has no intervening render frame to refresh its
                // skinning buffer. Bake the sampled pose for a faithful still image.
                foreach (var renderer in skin)
                {
                    renderer.enabled = false;
                    if (!renderer.sharedMesh || renderer.name.Contains("LOD1")) continue;
                    var baked = new Mesh();
                    renderer.BakeMesh(baked, true);
                    proofMeshes.Add(baked);
                    var proof = new GameObject("CharacterProof_Pose");
                    proofObjects.Add(proof);
                    proof.transform.SetParent(renderer.transform, false);
                    proof.AddComponent<MeshFilter>().sharedMesh = baked;
                    proof.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                }
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
                else { camera.targetTexture = rt; camera.Render(); camera.targetTexture = null; }
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                foreach (var proof in proofObjects) Object.DestroyImmediate(proof);
                foreach (var mesh in proofMeshes) Object.DestroyImmediate(mesh);
                for (int i = 0; i < skin.Length; i++) skin[i].enabled = enabled[i];
                RenderTexture.active = previous;
                camera.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(image);
            }
        }
    }
}
