using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Builds the Android debug APK and writes a verifiable report (Bridge/build_report.txt). Uses the already configured
    /// IL2CPP / ARM64 / landscape settings. Never fabricates success: the report is generated from Unity's BuildReport.
    /// Output: Builds/Android/CITY_DRIVER_UNDERCOVER.apk (the Builds folder is ignored by version control).
    /// </summary>
    public static class AndroidBuilder
    {
        public const string OutputPath = "Builds/Android/CITY_DRIVER_UNDERCOVER.apk";
        private const string CityScene = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        [MenuItem("City Driver/Build/Android Debug APK", priority = 100)]
        [BridgeCommand]
        public static void BuildDebugApk() => Build(OutputPath, null);

        /// <summary>The driving scene plus every streamed city-chunk scene.</summary>
        private static string[] BuildScenes()
        {
            var list = new System.Collections.Generic.List<string> { CityScene };
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes/CityChunks" })) list.Add(AssetDatabase.GUIDToAssetPath(guid));
            return list.ToArray();
        }

        private static void Build(string outputPath, AndroidArchitecture? overrideArch)
        {
            string reportPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", overrideArch.HasValue ? "build_report_emulator.txt" : "build_report.txt");
            AndroidArchitecture savedArch = PlayerSettings.Android.targetArchitectures;
            if (overrideArch.HasValue) PlayerSettings.Android.targetArchitectures = overrideArch.Value;
            var sb = new StringBuilder();
            sb.AppendLine($"Build started {DateTime.Now:o}");

            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)) throw new InvalidOperationException("Android Build Support is not installed.");
                if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveOpenScenes();

                EditorUserBuildSettings.buildAppBundle = false;     // APK, not AAB
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                if (File.Exists(outputPath)) File.Delete(outputPath);

                var options = new BuildPlayerOptions
                {
                    // Early de-risking build: the driving scene only. The full scene list (menu, garage) comes with the menu work.
                    scenes = BuildScenes(),
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.Development,
                };

                sb.AppendLine($"Scenes: {string.Join(", ", options.scenes)}");
                sb.AppendLine($"Settings: package={PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android)}, backend={PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android)}, arch={PlayerSettings.Android.targetArchitectures}, minSdk={PlayerSettings.Android.minSdkVersion}, orientation={PlayerSettings.defaultInterfaceOrientation}");

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary s = report.summary;
                sb.AppendLine($"RESULT: {s.result}");
                sb.AppendLine($"Duration: {s.totalTime}");
                sb.AppendLine($"Output: {s.outputPath}");
                sb.AppendLine($"Reported size: {s.totalSize / (1024.0 * 1024.0):F1} MB");
                sb.AppendLine($"Errors: {s.totalErrors}, warnings: {s.totalWarnings}");

                foreach (var step in report.steps)
                    foreach (var m in step.messages)
                        if (m.type == LogType.Error || m.type == LogType.Exception) sb.AppendLine($"  [{step.name}] ERROR: {m.content}");

                if (File.Exists(outputPath)) sb.AppendLine($"File on disk: {new FileInfo(outputPath).FullName}, {new FileInfo(outputPath).Length} bytes");
                else sb.AppendLine("File on disk: NOT FOUND");
            }
            catch (Exception e) { sb.AppendLine("BUILD EXCEPTION: " + e); }
            finally { PlayerSettings.Android.targetArchitectures = savedArch; }
            sb.AppendLine($"Architecture restored to: {PlayerSettings.Android.targetArchitectures}");

            File.WriteAllText(reportPath, sb.ToString());
            Debug.Log("[AndroidBuild]\n" + sb);
        }
    }
}
