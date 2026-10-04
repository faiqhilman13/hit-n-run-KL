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

namespace KampungRun.EditorTools.Anim
{
    /// <summary>
    /// Bakes the cast's motion library (<see cref="AnimLibrary"/>) into Humanoid clips under
    /// Assets/KampungRun/Animation/Clips, and renders review strips of them on any of the characters.
    ///   Bake:    -executeMethod KampungRun.EditorTools.Anim.AnimationStudio.BakeBatch
    ///   Review:  -executeMethod KampungRun.EditorTools.Anim.AnimationStudio.ReviewBatch
    ///            [-animClips walk,run] [-animChars chr_pakmat,chr_adik] [-animOut dir] [-animFrames 10] [-animVideo 1]
    /// Re-baking keeps each clip's asset (and GUID), so the controller's references hold.
    /// </summary>
    public static class AnimationStudio
    {
        public const string ClipDir = "Assets/KampungRun/Animation/Clips";
        public const string ReferenceModel = "Assets/Models/KL/chr_aiman.fbx";
        const float Fps = 60f;

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        [MenuItem("Kampung Run/Animation/Bake Motion Library")]
        public static void BakeMenu() => Bake(null);

        public static void BakeBatch()
        {
            var only = Arg("-animClips", "");
            Bake(string.IsNullOrEmpty(only) ? null : new HashSet<string>(only.Split(',')));
        }

        /// <summary>Bake every clip (or just the named ones). Returns the clips by name.</summary>
        public static Dictionary<string, AnimationClip> Bake(HashSet<string> only)
        {
            Directory.CreateDirectory(ClipDir);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceModel);
            var rig = new AnimRig(prefab);
            var report = new StringBuilder("Motion library bake\n");
            var an = rig.animator;
            report.AppendLine($"rig: hips {rig.HipsRest.ToString("F3")} chest {rig.ChestRest.ToString("F3")} neck {an.GetBoneTransform(HumanBodyBones.Neck).position.ToString("F3")} " +
                $"head {rig.HeadRest.ToString("F3")} shoulderR {rig.ShoulderRest(1).ToString("F3")} handR(T) {an.GetBoneTransform(HumanBodyBones.RightHand).position.ToString("F3")} " +
                $"hipR {rig.HipJointRest(1).ToString("F3")} ankleR {rig.AnkleRest(1).ToString("F3")} ballR {rig.BallRest(1).ToString("F3")} top {ModelFactory.LocalBounds(rig.go).max.y:F3}");
            var made = new Dictionary<string, AnimationClip>();
            try
            {
                foreach (var def in AnimLibrary.Build(rig))
                {
                    if (only != null && !only.Contains(def.name)) continue;
                    var clip = BakeClip(rig, def, out float lowest, out float highest, out string limits);
                    string path = $"{ClipDir}/{def.name}.anim";
                    var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (existing != null)
                    {
                        EditorUtility.CopySerialized(clip, existing);
                        Object.DestroyImmediate(clip);
                        clip = existing;
                    }
                    else AssetDatabase.CreateAsset(clip, path);
                    made[def.name] = clip;
                    report.AppendLine($"{def.name,-14} {def.length:F2}s {(def.loop ? "loop" : "once")}  feet low {lowest:F3} high {highest:F3}" +
                                      (def.speed > 0 ? $"  {def.speed:F2} m/s" : "") + limits);
                }
            }
            finally { rig.Dispose(); }
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
            return made;
        }

        static AnimationClip BakeClip(AnimRig rig, ClipDef def, out float lowest, out float highest, out string limits)
        {
            int frames = Mathf.Max(2, Mathf.RoundToInt(def.length * Fps));
            int mc = HumanTrait.MuscleCount;
            var times = new float[frames + 1];
            var mus = new float[mc, frames + 1];
            var rootT = new Vector3[frames + 1];
            var rootQ = new Quaternion[frames + 1];
            lowest = 9f; highest = -9f;
            string lowestAt = "";
            for (int i = 0; i <= frames; i++)
            {
                float u = (float)i / frames;
                times[i] = u * def.length;
                rig.Apply(def.pose(def.loop && i == frames ? 0f : u));
                if (rig.LowestFoot < lowest) lowestAt = $"{rig.LowestWhat}@{u:F2}";
                lowest = Mathf.Min(lowest, rig.LowestFoot);
                highest = Mathf.Max(highest, rig.LowestFoot);
                var hp = rig.Capture();
                for (int m = 0; m < mc; m++) mus[m, i] = hp.muscles[m];
                rootT[i] = hp.bodyPosition;
                var q = hp.bodyRotation;
                if (i > 0 && Quaternion.Dot(q, rootQ[i - 1]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                rootQ[i] = q;
            }

            // muscles pushed past the avatar's range get clamped on playback: list them
            var over = new List<string>();
            for (int m = 0; m < mc; m++)
            {
                float worst = 0f;
                int at = 0;
                for (int i = 0; i <= frames; i++) if (Mathf.Abs(mus[m, i]) > worst) { worst = Mathf.Abs(mus[m, i]); at = i; }
                if (worst > 1.001f) over.Add($"{HumanTrait.MuscleName[m]} {mus[m, at]:F2}@{(float)at / frames:F2}");
            }
            limits = (lowest < -0.015f ? $"  (low {lowestAt})" : "") + (over.Count > 0 ? "  LIMIT: " + string.Join(", ", over) : "");

            var clip = new AnimationClip { name = def.name, frameRate = Fps };
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            void Add(string prop, Func<int, float> value, float tolerance)
            {
                var v = new float[frames + 1];
                for (int i = 0; i <= frames; i++) v[i] = value(i);
                bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), prop));
                curves.Add(Curve(times, v, def.loop, tolerance));
            }
            // tolerances: the body's position to a fraction of a millimetre, its turn and every muscle to about a
            // tenth of a degree
            Add("RootT.x", i => rootT[i].x, 0.0004f); Add("RootT.y", i => rootT[i].y, 0.0004f); Add("RootT.z", i => rootT[i].z, 0.0004f);
            Add("RootQ.x", i => rootQ[i].x, 0.0002f); Add("RootQ.y", i => rootQ[i].y, 0.0002f); Add("RootQ.z", i => rootQ[i].z, 0.0002f); Add("RootQ.w", i => rootQ[i].w, 0.0002f);
            for (int m = 0; m < mc; m++)
            {
                string n = HumanTrait.MuscleName[m];
                if (n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little")) continue;
                // the cast have no upper chest, eyes or jaw bones
                if (n.StartsWith("UpperChest") || n.Contains("Eye") || n.StartsWith("Jaw")) continue;
                int mm = m;
                Add(n, i => mus[mm, i], 0.0015f);
            }
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            var st = AnimationUtility.GetAnimationClipSettings(clip);
            st.loopTime = def.loop;
            st.loopBlend = false;
            st.loopBlendOrientation = true;
            st.loopBlendPositionY = true;
            st.loopBlendPositionXZ = true;
            st.keepOriginalOrientation = true;
            st.keepOriginalPositionY = true;
            st.keepOriginalPositionXZ = true;
            st.heightFromFeet = false;
            st.mirror = false;
            AnimationUtility.SetAnimationClipSettings(clip, st);
            return clip;
        }

        /// <summary>
        /// A curve through the samples with smooth (central-difference) tangents (loops wrap round), thinned out:
        /// keys are kept only where the cubic between their neighbours would miss a sample by more than the tolerance.
        /// </summary>
        static AnimationCurve Curve(float[] t, float[] v, bool loop, float tolerance)
        {
            int n = t.Length;
            var slope = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && i < n - 1) slope[i] = (v[i + 1] - v[i - 1]) / (t[i + 1] - t[i - 1]);
                else if (loop) slope[i] = (v[1] - v[n - 2]) / (t[1] - t[0] + t[n - 1] - t[n - 2]);
                else if (i == 0) slope[i] = (v[1] - v[0]) / (t[1] - t[0]);
                else slope[i] = (v[n - 1] - v[n - 2]) / (t[n - 1] - t[n - 2]);
            }
            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            float Hermite(int a, int b, float tt)
            {
                float h = t[b] - t[a], s = (tt - t[a]) / h, s2 = s * s, s3 = s2 * s;
                return (2 * s3 - 3 * s2 + 1) * v[a] + (s3 - 2 * s2 + s) * h * slope[a] + (-2 * s3 + 3 * s2) * v[b] + (s3 - s2) * h * slope[b];
            }
            void Split(int a, int b)
            {
                if (b - a < 2) return;
                int worst = -1;
                float err = tolerance;
                for (int k = a + 1; k < b; k++)
                {
                    float e = Mathf.Abs(Hermite(a, b, t[k]) - v[k]);
                    if (e > err) { err = e; worst = k; }
                }
                if (worst < 0) return;
                keep[worst] = true;
                Split(a, worst);
                Split(worst, b);
            }
            Split(0, n - 1);
            var keys = new List<Keyframe>();
            for (int i = 0; i < n; i++) if (keep[i]) keys.Add(new Keyframe(t[i], v[i], slope[i], slope[i]));
            return new AnimationCurve(keys.ToArray());
        }

        // ================================================================== review renders
        public static void ReviewBatch()
        {
            var clipArg = Arg("-animClips", "");
            var only = string.IsNullOrEmpty(clipArg) ? null : new HashSet<string>(clipArg.Split(','));
            var chars = Arg("-animChars", "chr_aiman,chr_pakmat,chr_maksom,chr_adik").Split(',');
            string outDir = Arg("-animOut", Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "anim_review")));
            int frames = int.Parse(Arg("-animFrames", "10"));
            bool video = Arg("-animVideo", "0") == "1";
            bool skipBake = Arg("-animNoBake", "0") == "1";
            var clips = skipBake ? null : Bake(only);
            Review(only, chars, outDir, frames, video);
        }

        public static void Review(HashSet<string> only, string[] chars, string outDir, int frames, bool video)
        {
            Directory.CreateDirectory(outDir);
            var prefabRef = AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceModel);
            var refRig = new AnimRig(prefabRef);
            var defs = AnimLibrary.Build(refRig).Where(d => only == null || only.Contains(d.name)).ToList();
            float refScale = refRig.animator.humanScale;
            float refLeg = LegLength(refRig.animator);
            refRig.Dispose();
            var log = new StringBuilder("Review\n");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalFloat("_ShadeMode", 1);
            Shader.SetGlobalFloat("_LatStyleSet", 1);
            Shader.SetGlobalVector("_LatStyle", Vector4.zero);
            Shader.SetGlobalVector("_LatStyle2", new Vector4(0, 1, 0, 0));
            Shader.SetGlobalColor("_ShadeSky", new Color(0.31f, 0.37f, 0.46f, 1));
            Shader.SetGlobalColor("_ShadeGround", new Color(0.23f, 0.20f, 0.17f, 1));
            RenderSettings.fog = false;
            var sun = new GameObject("Key").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.85f);
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(40, 140, 0);
            sun.shadows = LightShadows.Soft;
            var grid = GridTexture();
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            groundMat.SetTexture("_BaseMap", grid);
            groundMat.SetTextureScale("_BaseMap", new Vector2(80, 80));
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 8f;      // 80 m, 1 m squares
            ground.transform.position = new Vector3(0, -0.002f, 20f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.80f, 0.85f, 0.88f);
            cam.fieldOfView = 26;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200;
            cam.allowHDR = false;
            cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;

            const int W = 360, H = 420;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var img = new Texture2D(W, H, TextureFormat.RGB24, false);
            try
            {
                foreach (var cid in chars)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/KL/{cid}.fbx");
                    if (prefab == null) { log.AppendLine("missing " + cid); continue; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var anim = go.GetComponentInChildren<Animator>();
                    anim.runtimeAnimatorController = null;
                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var lod = go.GetComponentInChildren<LODGroup>();
                    if (lod) lod.ForceLOD(0);
                    float height = ModelFactory.LocalBounds(go).size.y;
                    // this character's contact points (under each heel and each ball, at rest) and its pace relative to
                    // the reference: stride scales with leg length
                    var feet = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes }
                        .Select(b => anim.GetBoneTransform(b)).ToArray();
                    var contact = feet.Select(t => t.InverseTransformPoint(new Vector3(t.position.x, 0f,
                        t.position.z - (t.name.Contains("Toes") ? 0f : 0.04f)))).ToArray();
                    float leg = LegLength(anim);
                    float pace = Arg("-animPace", "leg") == "hs" ? anim.humanScale / refScale : leg / refLeg;
                    log.AppendLine($"{cid}: humanScale {anim.humanScale:F3} (x{anim.humanScale / refScale:F2}), leg {leg:F3} (x{pace:F2}), height {height:F2}");
                    AnimationMode.StartAnimationMode();
                    foreach (var def0 in defs)
                    {
                        var def = def0;
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipDir}/{def.name}.anim");
                        if (Arg("-animOld", "0") == "1")
                        {
                            // compare with the clips the game used before (Blender takes on chr_aiman.fbx)
                            clip = AssetDatabase.LoadAllAssetRepresentationsAtPath(ReferenceModel).OfType<AnimationClip>().FirstOrDefault(c => c.name == def.name);
                            def = new ClipDef(def.name, clip ? clip.length : 1f, def.loop, def.pose,
                                def.name == "walk" ? 2.55f : def.name == "run" ? 6.5f : 0f);
                        }
                        if (clip == null) { log.AppendLine("unbaked " + def.name); continue; }
                        string dir = Path.Combine(outDir, def.name);
                        Directory.CreateDirectory(dir);
                        // a quick measure at 120 Hz: how far below their rest height the feet go, and how fast a heel or
                        // ball slides while it rests on the ground (on the floor and not rising or falling) - zero means
                        // planted, for a body moving at the clip's pace
                        {
                            int m = Mathf.Max(8, Mathf.RoundToInt(def.length * 120f));
                            float dtk = def.length / m;
                            var pts = new Vector3[feet.Length, m + 1];
                            for (int k = 0; k <= m; k++)
                            {
                                float tk = dtk * k;
                                AnimationMode.BeginSampling();
                                AnimationMode.SampleAnimationClip(go, clip, tk);
                                AnimationMode.EndSampling();
                                var rootK = new Vector3(0, 0, def.speed * pace * tk);
                                for (int fi = 0; fi < feet.Length; fi++) pts[fi, k] = rootK + feet[fi].TransformPoint(contact[fi]);
                            }
                            float floor = 9f;
                            foreach (var q in pts) floor = Mathf.Min(floor, q.y);
                            float slideSum = 0f; int slideN = 0;
                            for (int fi = 0; fi < feet.Length; fi++)
                            for (int k = 1; k <= m; k++)
                            {
                                var a = pts[fi, k - 1];
                                var b = pts[fi, k];
                                if (a.y > floor + 0.03f || b.y > floor + 0.03f || Mathf.Abs(b.y - a.y) / dtk > 0.08f) continue;
                                var dv = b - a;
                                dv.y = 0f;
                                slideSum += dv.magnitude / dtk;
                                slideN++;
                            }
                            log.AppendLine($"{cid,-14} {def.name,-14} lowest foot {floor * 100f:F1} cm vs rest; planted slide {(slideN > 0 ? slideSum / slideN : 0f):F2} m/s (pace x{pace:F2})");
                        }
                        int n = video ? Mathf.Max(2, Mathf.RoundToInt(def.length * (def.loop ? 2f : 1f) * 30f)) : frames;
                        float minY = 9f, maxY = -9f;
                        for (int f = 0; f < n; f++)
                        {
                            float t = video ? f / 30f : (def.loop ? f / (float)n : f / (float)(n - 1)) * def.length;
                            float tc = def.loop ? Mathf.Repeat(t, def.length) : Mathf.Min(t, def.length);
                            AnimationMode.BeginSampling();
                            AnimationMode.SampleAnimationClip(go, clip, tc);
                            AnimationMode.EndSampling();
                            // walk the body along at the clip's own pace so a sliding foot shows against the grid
                            var root = new Vector3(0, 0, def.speed * pace * t);
                            go.transform.position = root;
                            var b = ModelFactory.LocalBounds(go);
                            minY = Mathf.Min(minY, b.min.y);
                            maxY = Mathf.Max(maxY, b.min.y);
                            var aim = root + Vector3.up * height * 0.52f;
                            foreach (int view in new[] { 0, 1 })
                            {
                                var dirv = view == 0 ? new Vector3(1f, 0.12f, 0.02f) : new Vector3(0.12f, 0.16f, 1f);
                                cam.transform.position = aim + dirv.normalized * height * 3.6f;
                                cam.transform.LookAt(aim);
                                Capture(cam, go, rt, img, Path.Combine(dir, $"{cid}_{(view == 0 ? "side" : "front")}_{f:000}.png"));
                            }
                        }

                    }
                    AnimationMode.StopAnimationMode();
                    Object.DestroyImmediate(go);
                }
            }
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(img);
            }
            File.WriteAllText(Path.Combine(outDir, "review.txt"), log.ToString());
            Debug.Log(log.ToString());
        }

        /// <summary>Hip joint to ankle plus the ankle's height: what sets the length of a stride.</summary>
        public static float LegLength(Animator a)
        {
            var up = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
            var lo = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position;
            var ft = a.GetBoneTransform(HumanBodyBones.LeftFoot).position;
            return Vector3.Distance(up, lo) + Vector3.Distance(lo, ft) + (ft.y - a.transform.position.y);
        }

        static Texture2D GridTexture()
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                bool line = x < 2 || y < 2;
                bool half = (x / 32 + y / 32) % 2 == 0;
                px[y * S + x] = line ? new Color(0.35f, 0.38f, 0.4f) : half ? new Color(0.78f, 0.78f, 0.74f) : new Color(0.72f, 0.73f, 0.7f);
            }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        static void Capture(Camera camera, GameObject character, RenderTexture rt, Texture2D image, string path)
        {
            var skin = character.GetComponentsInChildren<SkinnedMeshRenderer>();
            var proofs = new List<GameObject>();
            var meshes = new List<Mesh>();
            try
            {
                // headless: bake the sampled pose, the skinning buffer isn't refreshed between renders
                foreach (var r in skin)
                {
                    r.enabled = false;
                    if (!r.sharedMesh || r.name.Contains("LOD1")) continue;
                    var baked = new Mesh();
                    r.BakeMesh(baked, true);
                    meshes.Add(baked);
                    var proof = new GameObject("Proof");
                    proofs.Add(proof);
                    proof.transform.SetParent(r.transform, false);
                    proof.AddComponent<MeshFilter>().sharedMesh = baked;
                    proof.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                }
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
                else { camera.targetTexture = rt; camera.Render(); camera.targetTexture = null; }
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                image.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                foreach (var p in proofs) Object.DestroyImmediate(p);
                foreach (var m in meshes) Object.DestroyImmediate(m);
                foreach (var r in skin) r.enabled = true;
            }
        }

        // ================================================================== checks
        /// <summary>Bake, then play each clip back on the reference character and compare its joints with the authored pose.</summary>
        public static void RoundTripBatch()
        {
            var made = Bake(null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceModel);
            var rig = new AnimRig(prefab);
            var player = (GameObject)Object.Instantiate(prefab);
            var anim = player.GetComponentInChildren<Animator>();
            anim.runtimeAnimatorController = null;
            anim.applyRootMotion = false;
            var log = new StringBuilder($"Round trip (humanScale {anim.humanScale:F3}, hips {rig.hipHeight:F3}, leg {rig.legLength:F3}, thigh {rig.thigh:F3}, shin {rig.shin:F3}, arm {rig.upperArm:F3}+{rig.foreArm:F3})\n");
            try
            {
                AnimationMode.StartAnimationMode();
                foreach (var def in AnimLibrary.Build(rig))
                {
                    if (!made.TryGetValue(def.name, out var clip)) continue;
                    float worst = 0f;
                    string where = "";
                    foreach (float u in new[] { 0f, 0.25f, 0.5f, 0.75f })
                    {
                        rig.Apply(def.pose(u));
                        var want = rig.Joints();
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(player, clip, u * def.length);
                        AnimationMode.EndSampling();
                        foreach (var kv in want)
                        {
                            float e = Vector3.Distance(kv.Value, anim.GetBoneTransform(kv.Key).position);
                            if (e > worst) { worst = e; where = $"{kv.Key}@{u:F2} want {kv.Value} got {anim.GetBoneTransform(kv.Key).position}"; }
                        }
                    }
                    log.AppendLine($"{def.name,-14} worst {worst * 100f:F1} cm  {where}");
                }
            }
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                rig.Dispose();
                Object.DestroyImmediate(player);
            }
            Debug.Log(log.ToString());
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "anim_review_roundtrip.txt")), log.ToString());
        }
    }
}
