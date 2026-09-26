using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>Builds the current game assets without regenerating its scene or controller.</summary>
    public static class RefinedCharacterBuild
    {
        [Serializable] class PreservedAsset { public string path; public string before; public string after; }
        [Serializable] class PreservationReport { public string utc; public PreservedAsset[] assets; }
        [MenuItem("Kampung Run/Build Refined Character Preview (Windows)")]
        public static void Windows()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException("Select Windows Standalone first (batch: -buildTarget Win64).");
            string[] preserved = { "Assets/Scenes/KampungRun.unity", HumanControllerBuilder.Path,
                "Assets/KampungRun/Scripts/Characters/SeatFit.cs",
                "Assets/KampungRun/Scripts/Vehicles/VehicleVisuals.cs", "Assets/KampungRun/Tests/PromoCapture.cs" };
            string Hash(string path)
            {
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
            }
            var hashes = preserved.ToDictionary(path => path, Hash);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            const string destination = "Builds/RefinedCharacters/Windows/KampungRunKL.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { preserved[0] },
                locationPathName = destination,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            bool unchanged = hashes.All(pair => pair.Value == Hash(pair.Key));
            string result = $"{report.summary.result}: {report.summary.totalSize / (1024 * 1024)} MB, " +
                $"{report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings. " +
                $"Protected assets preserved: {unchanged}. Output: {Path.GetFullPath(destination)}";
            Directory.CreateDirectory("docs/character-concepts/round-02/unity");
            File.WriteAllText("docs/character-concepts/round-02/unity/windows-build.txt", result);
            File.WriteAllText("docs/character-concepts/round-02/unity/preserved-assets-build.json",
                JsonUtility.ToJson(new PreservationReport { utc = DateTime.UtcNow.ToString("O"),
                    assets = hashes.Select(pair => new PreservedAsset { path = pair.Key,
                        before = pair.Value, after = Hash(pair.Key) }).ToArray() }, true));
            if (report.summary.result != BuildResult.Succeeded || !unchanged) throw new InvalidOperationException(result);
            Debug.Log(result);
        }
    }
}
