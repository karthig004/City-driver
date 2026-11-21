using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Builds the WebGL (browser) version into Builds/WebGL and writes Bridge/build_report_webgl.txt from Unity's BuildReport. Switches the active build target to WebGL.</summary>
    public static class WebGLBuilder
    {
        public const string OutputPath = "Builds/WebGL";
        private const string CityScene = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        [MenuItem("City Driver/Build/WebGL", priority = 110)]
        [BridgeCommand]
        public static void Build()
        {
            string report = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "build_report_webgl.txt");
            var sb = new StringBuilder(); sb.AppendLine($"WebGL build started {DateTime.Now:o}");
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL)) throw new InvalidOperationException("WebGL Build Support is not installed.");
                if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveOpenScenes();

                BrandingSettings.Apply();
                PlayerSettings.WebGL.template = BrandingSettings.WebTemplate;
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                PlayerSettings.WebGL.decompressionFallback = true;          // works on any static host, no special Content-Encoding headers needed
                PlayerSettings.WebGL.initialMemorySize = 512;
                PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
                PlayerSettings.WebGL.dataCaching = true;

                var scenes = new System.Collections.Generic.List<string> { CityScene };
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes/CityChunks" })) scenes.Add(AssetDatabase.GUIDToAssetPath(guid));
                if (Directory.Exists(OutputPath)) Directory.Delete(OutputPath, true);
                Directory.CreateDirectory(OutputPath);
                var options = new BuildPlayerOptions { scenes = scenes.ToArray(), locationPathName = OutputPath, target = BuildTarget.WebGL, targetGroup = BuildTargetGroup.WebGL, options = BuildOptions.None };
                BuildReport r = BuildPipeline.BuildPlayer(options);
                sb.AppendLine($"Result: {r.summary.result}, errors {r.summary.totalErrors}, warnings {r.summary.totalWarnings}, output size {r.summary.totalSize / (1024 * 1024)} MB, time {r.summary.totalTime}");
                foreach (var step in r.steps) foreach (var m in step.messages) if (m.type == LogType.Error || m.type == LogType.Exception) sb.AppendLine("ERROR: " + m.content);
            }
            catch (Exception e) { sb.AppendLine("FAILED: " + e); }
            File.WriteAllText(report, sb.ToString());
            Debug.Log("[WebGL] " + sb);
        }
    }
}
