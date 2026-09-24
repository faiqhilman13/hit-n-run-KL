using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// One-click project setup: layers, the GameAssets registry, the game scene and
    /// build settings. Menu: Kampung Run / Rebuild Everything.
    /// </summary>
    public static class ProjectBuilder
    {
        const string ScenePath = "Assets/Scenes/KampungRun.unity";
        const string AssetsPath = "Assets/KampungRun/Resources/GameAssets.asset";

        [InitializeOnLoadMethod]
        static void EditorGlobals()
        {
            // so the Scene view shows the right paper/shadow tones outside Play mode
            Shader.SetGlobalColor("_LatPaper", new Color(0.97f, 0.94f, 0.86f, 1));
            Shader.SetGlobalColor("_LatShadow", new Color(0.78f, 0.72f, 0.8f, 1));
        }

        [MenuItem("Kampung Run/Rebuild Everything")]
        public static void RebuildEverything()
        {
            SetupLayers();
            RemoveSSAO();
            RebuildGameAssets();
            BuildScene();
            Debug.Log("[KampungRun] Rebuilt layers, GameAssets and scene. Open Assets/Scenes/KampungRun.unity and press Play.");
        }

        [MenuItem("Kampung Run/Rebuild Game Assets")]
        public static void RebuildGameAssets()
        {
            Directory.CreateDirectory("Assets/KampungRun/Resources");
            var ga = AssetDatabase.LoadAssetAtPath<GameAssets>(AssetsPath);
            if (ga == null)
            {
                ga = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(ga, AssetsPath);
            }
            ga.latInk = Shader.Find("KampungRun/LatInk");
            ga.glass = Shader.Find("KampungRun/Glass");
            ga.beam = Shader.Find("KampungRun/Beam");
            ga.sky = Shader.Find("KampungRun/CartoonSky");
            ga.text3d = Shader.Find("KampungRun/Text3D");
            ga.humanController = HumanControllerBuilder.Build();
            ga.surfaces = BuildSurfaceArray();
            ga.models = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models" })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(m => m != null).OrderBy(m => m.name).ToList();
            EditorUtility.SetDirty(ga);
            AssetDatabase.SaveAssets();
            Debug.Log($"[KampungRun] GameAssets: {ga.models.Count} models, shader={(ga.latInk ? ga.latInk.name : "MISSING")}");
        }

        /// <summary>Pack Textures/Surfaces/surf_NN_*.png into one Texture2DArray (slice = id - 1).</summary>
        static Texture2DArray BuildSurfaceArray()
        {
            const string dir = "Assets/KampungRun/Textures/Surfaces";
            const string path = "Assets/KampungRun/Resources/SurfaceArray.asset";
            if (!Directory.Exists(dir)) return null;
            var files = Directory.GetFiles(dir, "surf_*.png").OrderBy(f => f).ToArray();
            if (files.Length == 0) return null;
            var texs = files.Select(f => AssetDatabase.LoadAssetAtPath<Texture2D>(f.Replace('\\', '/'))).Where(t => t != null).ToArray();
            int size = texs[0].width;
            var arr = new Texture2DArray(size, size, texs.Length, TextureFormat.RGBA32, true, true)
            {
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "SurfaceArray"
            };
            for (int i = 0; i < texs.Length; i++) arr.SetPixels(texs[i].GetPixels(), i, 0);
            arr.Apply(true, false);
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(arr, path);
            Debug.Log($"[KampungRun] Surface array: {texs.Length} painted textures");
            return AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
        }

        /// <summary>
        /// The ink look doesn't use SSAO. A disabled SSAO feature still breaks renderer
        /// creation in player builds (its shaders get stripped), so remove it outright.
        /// </summary>
        static void RemoveSSAO()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Settings" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(path);
                var so = new SerializedObject(data);
                var feats = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                bool changed = false;
                for (int i = feats.arraySize - 1; i >= 0; i--)
                {
                    var f = feats.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (f == null || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    feats.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    feats.DeleteArrayElementAtIndex(i);
                    if (map != null && i < map.arraySize) map.DeleteArrayElementAtIndex(i);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.RemoveObjectFromAsset(f);
                    Object.DestroyImmediate(f, true);
                    changed = true;
                }
                if (changed)
                {
                    EditorUtility.SetDirty(data);
                    Debug.Log($"[KampungRun] Removed SSAO from {path}");
                }
            }
            AssetDatabase.SaveAssets();
        }

        static void SetupLayers()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            void Set(int i, string n) { var p = layers.GetArrayElementAtIndex(i); if (string.IsNullOrEmpty(p.stringValue)) p.stringValue = n; }
            Set(Layers.Vehicle, "Vehicle");
            Set(Layers.Character, "Character");
            Set(Layers.Pickup, "Pickup");
            tm.ApplyModifiedProperties();
        }

        [MenuItem("Kampung Run/Build Scene")]
        public static void BuildScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            cam.AddComponent<ChaseCamera>();
            cam.transform.position = new Vector3(0, 40, -120);
            var sun = new GameObject("Matahari").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(48, 35, 0);
            new GameObject("KampungRun").AddComponent<GameManager>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "Kampung Run: KL";
            PlayerSettings.companyName = "Kampung Run";
        }

        /// <summary>Batch entry point: everything needed before tests or a build.</summary>
        public static void BatchSetup()
        {
            AssetDatabase.Refresh();
            RebuildEverything();
        }

        [MenuItem("Kampung Run/Build Windows Player")]
        public static void BuildWindows()
        {
            RebuildEverything();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/KampungRunKL.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"[KampungRun] Build {report.summary.result}: {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors");
        }

        /// <summary>
        /// Browser build for itch.io / any static host: gzip-compressed with the JS decompression
        /// fallback (so no special server headers are needed), growable memory, WebGL 2, the
        /// Kampung Run loading page, and a ready-to-upload zip next to it.
        /// Batch: -buildTarget WebGL -executeMethod KampungRun.EditorTools.ProjectBuilder.BuildWebGL
        /// </summary>
        [MenuItem("Kampung Run/Build WebGL (browser)")]
        public static void BuildWebGL()
        {
            RebuildEverything();
            PlayerSettings.WebGL.template = "PROJECT:KampungRun";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.WebGL.maximumMemorySize = 2048;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
            PlayerSettings.runInBackground = false;
            const string outDir = "Builds/WebGL";
            if (System.IO.Directory.Exists(outDir)) System.IO.Directory.Delete(outDir, true);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            Debug.Log($"[KampungRun] WebGL build {report.summary.result}: {report.summary.totalSize / (1024 * 1024)} MB, " +
                      $"{report.summary.totalErrors} errors");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) return;
            // itch.io wants a zip with index.html at its root
            const string zip = "Builds/KampungRunKL_web_itch.zip";
            if (System.IO.File.Exists(zip)) System.IO.File.Delete(zip);
            System.IO.Compression.ZipFile.CreateFromDirectory(outDir, zip);
            Debug.Log($"[KampungRun] itch.io zip: {zip} ({new System.IO.FileInfo(zip).Length / (1024 * 1024)} MB)");
        }
    }
}
