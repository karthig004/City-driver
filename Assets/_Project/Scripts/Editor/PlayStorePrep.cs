using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Google Play preparation: generates the launcher icon (adaptive + round + legacy) from the game's own shield emblem, applies
    /// release-oriented player settings, and builds a NON-debuggable release APK / AAB. Signing: the keystore is NEVER created or stored
    /// here. A release AAB is signed only when the developer supplies their own upload keystore through environment variables
    /// (CDU_KEYSTORE_PATH, CDU_KEYSTORE_PASS, CDU_KEY_ALIAS, CDU_KEY_PASS). Without them the bundle is produced for structure checks only
    /// and named *_NOT_UPLOADABLE (Google Play rejects debug-signed bundles).
    /// </summary>
    public static class PlayStorePrep
    {
        private const string IconDir = "Assets/_Project/Art/AppIcon";
        private const string CityScene = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        // ------------------------------------------------------------------------------------------------ launcher icon
        [BridgeCommand, MenuItem("City Driver/Play Store/Generate launcher icon", priority = 200)]
        public static void GenerateIcon()
        {
            Directory.CreateDirectory(IconDir);
            var emblem = CityDriver.Game.IntroSequence.EmblemTexture();
            string bg = SavePng("icon_background.png", 432, (u, v) => Background(u, v));
            string fg = SavePng("icon_foreground.png", 432, (u, v) => Emblem(emblem, u, v, 0.56f, 0f));
            string legacy = SavePng("icon_legacy.png", 192, (u, v) => Over(Background(u, v), Emblem(emblem, u, v, 0.74f, 0.04f)), true, false);
            string round = SavePng("icon_round.png", 192, (u, v) => { var c = Over(Background(u, v), Emblem(emblem, u, v, 0.72f, 0.04f)); c.a *= Mathf.Clamp01((0.98f - Mathf.Sqrt(u * u + v * v)) * 40f); return c; }, true, true);
            string store = SavePng("play_store_icon_512.png", 512, (u, v) => Over(Background(u, v), Emblem(emblem, u, v, 0.78f, 0.04f)), true, false);
            AssetDatabase.Refresh();

            var target = NamedBuildTarget.Android;
            foreach (var kind in PlayerSettings.GetSupportedIconKindsForPlatform(BuildTargetGroup.Android))
            {
                var icons = PlayerSettings.GetPlatformIcons(target, kind);
                string kn = kind.ToString();
                foreach (var icon in icons)
                {
                    if (kn.Contains("Adaptive")) { icon.SetTexture(Load(bg), 0); icon.SetTexture(Load(fg), 1); }
                    else if (kn.Contains("Round")) icon.SetTexture(Load(round), 0);
                    else icon.SetTexture(Load(legacy), 0);
                }
                PlayerSettings.SetPlatformIcons(target, kind, icons);
            }
            PlayerSettings.SetIcons(target, new[] { Load(legacy) }, IconKind.Application);
            AssetDatabase.SaveAssets();
            Debug.Log("[PlayStorePrep] launcher icon generated in " + IconDir + " (" + store + ")");
        }

        private static Texture2D Load(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        private static string SavePng(string name, int size, Func<float, float, Color> f, bool opaqueNotNeeded = false, bool alpha = true)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = f((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
            tex.SetPixels(px); tex.Apply();
            string path = IconDir + "/" + name;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default; imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.npotScale = TextureImporterNPOTScale.None; imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return path;
        }

        // deep night-blue to burgundy gradient with a golden sunset glow low in the frame
        private static Color Background(float u, float v)
        {
            float t = (v + 1f) * 0.5f;
            Color top = new Color(0.06f, 0.07f, 0.16f), bottom = new Color(0.46f, 0.07f, 0.12f);
            Color c = Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, t));
            float glow = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(u * u + (v + 0.55f) * (v + 0.55f)) / 1.05f), 2f);
            c = Color.Lerp(c, new Color(1f, 0.66f, 0.2f), glow * 0.75f);
            c.a = 1f; return c;
        }

        private static Color Emblem(Texture2D emblem, float u, float v, float scale, float yShift)
        {
            // the emblem is 210x250: keep its aspect, scale as a share of the icon height
            float h = scale, w = scale * 210f / 250f;
            float eu = (u / w) * 0.5f + 0.5f, ev = ((v - yShift) / h) * 0.5f + 0.5f;
            if (eu < 0f || eu > 1f || ev < 0f || ev > 1f) return Color.clear;
            Color c = emblem.GetPixelBilinear(eu, ev);
            // a soft dark drop shadow keeps the emblem readable on the bright glow
            return c;
        }

        private static Color Over(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0f) return Color.clear;
            Color c = (over * over.a + under * under.a * (1f - over.a)) / a; c.a = a; return c;
        }

        // ------------------------------------------------------------------------------------------------ release settings
        [BridgeCommand, MenuItem("City Driver/Play Store/Apply release settings", priority = 201)]
        public static void ApplyReleaseSettings()
        {
            PlayerSettings.Android.forceInternetPermission = false;          // the game is fully offline: no INTERNET permission, so the Data safety form is simple
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.minifyRelease = true;
            PlayerSettings.Android.minifyDebug = false;
            PlayerSettings.Android.useCustomKeystore = false;                // credentials are never stored in the project
            AssetDatabase.SaveAssets();
            Debug.Log("[PlayStorePrep] release settings applied (no INTERNET permission, auto target SDK, minify on release)");
        }

        // ------------------------------------------------------------------------------------------------ builds
        [BridgeCommand, MenuItem("City Driver/Play Store/Build release APK (not debuggable)", priority = 210)]
        public static void BuildReleaseApk() => Build(false);

        [BridgeCommand, MenuItem("City Driver/Play Store/Build release AAB (Google Play)", priority = 211)]
        public static void BuildReleaseAab() => Build(true);

        /// <summary>Clears any signing path / alias / passwords from the player settings and saves, so nothing about your keystore stays in the project.</summary>
        [BridgeCommand]
        public static void ScrubCredentials()
        {
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.keystoreName = ""; PlayerSettings.Android.keystorePass = "";
            PlayerSettings.Android.keyaliasName = ""; PlayerSettings.Android.keyaliasPass = "";
            AssetDatabase.SaveAssets(); EditorApplication.ExecuteMenuItem("File/Save Project");
        }

        private static void Build(bool bundle)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string reportPath = Path.Combine(root, "Bridge", bundle ? "build_report_aab.txt" : "build_report_release_apk.txt");
            var sb = new StringBuilder(); sb.AppendLine($"Release build started {DateTime.Now:o}  bundle={bundle}");
            string ks = Environment.GetEnvironmentVariable("CDU_KEYSTORE_PATH");
            bool haveKey = !string.IsNullOrEmpty(ks) && File.Exists(ks) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CDU_KEY_ALIAS"));
            string output = bundle ? (haveKey ? "Builds/Android/CITY_DRIVER_UNDERCOVER.aab" : "Builds/Android/CITY_DRIVER_UNDERCOVER_NOT_UPLOADABLE_debug_signed.aab") : "Builds/Android/CITY_DRIVER_UNDERCOVER_release.apk";
            var saved = (PlayerSettings.Android.useCustomKeystore, PlayerSettings.Android.keystoreName, PlayerSettings.Android.keystorePass, PlayerSettings.Android.keyaliasName, PlayerSettings.Android.keyaliasPass);
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
                ApplyReleaseSettings();
                EditorUserBuildSettings.buildAppBundle = bundle;
                EditorUserBuildSettings.development = false;
                if (haveKey)
                {
                    PlayerSettings.Android.useCustomKeystore = true;
                    PlayerSettings.Android.keystoreName = ks;
                    PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("CDU_KEYSTORE_PASS") ?? "";
                    PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("CDU_KEY_ALIAS");
                    PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("CDU_KEY_PASS") ?? Environment.GetEnvironmentVariable("CDU_KEYSTORE_PASS") ?? "";
                    sb.AppendLine("Signing: developer-supplied keystore from environment variables (not stored).");
                }
                else sb.AppendLine("Signing: NO release keystore supplied -> debug signing. This output must NOT be uploaded to Google Play.");

                var scenes = new System.Collections.Generic.List<string> { CityScene };
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes/CityChunks" })) scenes.Add(AssetDatabase.GUIDToAssetPath(guid));
                Directory.CreateDirectory(Path.GetDirectoryName(output)); if (File.Exists(output)) File.Delete(output);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes.ToArray(), locationPathName = output, target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None });
                var s = report.summary;
                sb.AppendLine($"RESULT: {s.result}  duration {s.totalTime}  errors {s.totalErrors}  warnings {s.totalWarnings}");
                foreach (var step in report.steps) foreach (var m in step.messages) if (m.type == LogType.Error || m.type == LogType.Exception) sb.AppendLine($"  [{step.name}] ERROR: {m.content}");
                sb.AppendLine(File.Exists(output) ? $"File on disk: {new FileInfo(output).FullName}, {new FileInfo(output).Length} bytes" : "File on disk: NOT FOUND");
            }
            catch (Exception e) { sb.AppendLine("BUILD EXCEPTION: " + e); }
            finally
            {
                (PlayerSettings.Android.useCustomKeystore, PlayerSettings.Android.keystoreName, PlayerSettings.Android.keystorePass, PlayerSettings.Android.keyaliasName, PlayerSettings.Android.keyaliasPass) = saved;
                PlayerSettings.Android.useCustomKeystore = false;   // never leave credentials in ProjectSettings
                PlayerSettings.Android.keystorePass = ""; PlayerSettings.Android.keyaliasPass = "";
                EditorUserBuildSettings.buildAppBundle = false;
                ScrubCredentials();
            }
            File.WriteAllText(reportPath, sb.ToString()); Debug.Log("[PlayStorePrep]\n" + sb);
        }
    }
}
