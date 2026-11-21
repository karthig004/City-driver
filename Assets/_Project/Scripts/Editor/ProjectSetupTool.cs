using System;
using System.IO;
using System.Text;
using CityDriver.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Phase 2 project preparation. All changes to ProjectSettings go through the Unity Editor APIs
    /// (never by editing YAML while the Editor is open). Every step is idempotent: running it twice is safe.
    /// Menu: City Driver > Setup
    /// </summary>
    public static class ProjectSetupTool
    {
        // ---- Project identity -------------------------------------------------------------
        private const string CompanyName = "Karthi Lab";
        private const string ProductName = "CITY DRIVER: UNDERCOVER";
        private const string AndroidPackageId = "com.karthilab.citydriverundercover";

        // ---- Paths ------------------------------------------------------------------------
        private const string ProjectRoot = "Assets/_Project";
        private const string SettingsFolder = ProjectRoot + "/Settings";
        private const string MobileUrpTemplate = "Assets/Settings/Mobile_RPAsset.asset"; // template asset, left untouched
        private const string PerformanceProfilePath = SettingsFolder + "/Resources/PerformanceProfile.asset";

        private static readonly string[] ScenePaths =
        {
            ProjectRoot + "/Scenes/MainMenu/MainMenu.unity",
            ProjectRoot + "/Scenes/CityDriving/CityDriving.unity",
            ProjectRoot + "/Scenes/Garage/Garage.unity",
        };

        // ---- Layers & tags (indexes 8+ are user layers) ----------------------------------------
        private static readonly string[] LayerNames = { "PlayerVehicle", "TrafficVehicle", "Road", "Environment", "MissionTrigger" };
        private const int FirstUserLayer = 8;
        private static readonly string[] TagNames = { "Traffic", "Passenger", "Destination" };

        // ---- Quality tiers ----------------------------------------------------------------
        private sealed class Tier
        {
            public string Name;
            // URP asset
            public bool Hdr; public int Msaa; public float RenderScale; public int ShadowRes; public float ShadowDist;
            public int Cascades; public bool SoftShadows; public int AdditionalLightsMode; public int AdditionalLightsLimit;
            // Quality level (legacy fields, still used for texture/LOD/skinning)
            public int TextureMipLimit; public int Aniso; public float LodBias; public int SkinWeights; public bool SoftParticles;
            public bool ReflectionProbes;
        }

        private static readonly Tier[] Tiers =
        {
            new Tier { Name = "Low",    Hdr = false, Msaa = 1, RenderScale = 0.70f, ShadowRes = 512,  ShadowDist = 25f, Cascades = 1, SoftShadows = false, AdditionalLightsMode = 0, AdditionalLightsLimit = 0, TextureMipLimit = 1, Aniso = 0, LodBias = 0.7f, SkinWeights = 1, SoftParticles = false, ReflectionProbes = false },
            new Tier { Name = "Medium", Hdr = false, Msaa = 1, RenderScale = 0.85f, ShadowRes = 1024, ShadowDist = 40f, Cascades = 2, SoftShadows = false, AdditionalLightsMode = 1, AdditionalLightsLimit = 4, TextureMipLimit = 0, Aniso = 1, LodBias = 1.0f, SkinWeights = 2, SoftParticles = false, ReflectionProbes = true },
            new Tier { Name = "High",   Hdr = true,  Msaa = 2, RenderScale = 1.00f, ShadowRes = 2048, ShadowDist = 60f, Cascades = 2, SoftShadows = true,  AdditionalLightsMode = 2, AdditionalLightsLimit = 4, TextureMipLimit = 0, Aniso = 2, LodBias = 1.5f, SkinWeights = 4, SoftParticles = true,  ReflectionProbes = true },
        };

        // Default quality index per platform (Low=0, Medium=1, High=2). Editor on integrated GPU uses Standalone's value.
        private const int DefaultAndroidTier = 1;
        private const int DefaultEditorStandaloneTier = 0;
        private const int DefaultWebTier = 0;

        // =============================================================================================
        // Menu items
        // =============================================================================================

        [BridgeCommand]
        [MenuItem("City Driver/Setup/Run Phase 2 Setup (All Steps)", priority = 0)]
        public static void RunAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[CityDriver Setup] Exit Play Mode before running setup.");
                return;
            }

            Step("Player settings", ApplyPlayerSettings);
            Step("URP quality assets + quality levels", ConfigureQuality);
            Step("Layers, tags, collision matrix", ConfigureLayersTagsPhysics);
            Step("Performance profile asset", CreatePerformanceProfile);
            Step("Scenes + build settings", CreateScenesAndBuildSettings);
            AssetDatabase.SaveAssets();
            PrintReport();
        }

        [MenuItem("City Driver/Setup/1 Apply Player Settings", priority = 20)]
        public static void ApplyPlayerSettingsMenu() { Step("Player settings", ApplyPlayerSettings); AssetDatabase.SaveAssets(); }

        [MenuItem("City Driver/Setup/2 Configure Quality (Low-Medium-High)", priority = 21)]
        public static void ConfigureQualityMenu() { Step("Quality", ConfigureQuality); AssetDatabase.SaveAssets(); }

        [MenuItem("City Driver/Setup/3 Configure Layers, Tags, Physics", priority = 22)]
        public static void ConfigureLayersMenu() { Step("Layers/tags/physics", ConfigureLayersTagsPhysics); AssetDatabase.SaveAssets(); }

        [MenuItem("City Driver/Setup/4 Create Performance Profile", priority = 23)]
        public static void CreateProfileMenu() { Step("Performance profile", CreatePerformanceProfile); AssetDatabase.SaveAssets(); }

        [MenuItem("City Driver/Setup/5 Create Scenes + Build Settings", priority = 24)]
        public static void CreateScenesMenu() { Step("Scenes", CreateScenesAndBuildSettings); AssetDatabase.SaveAssets(); }

        [BridgeCommand]
        [MenuItem("City Driver/Setup/Switch Build Target To Android", priority = 40)]
        public static void SwitchToAndroid()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[CityDriver Setup] Android Build Support is NOT installed for this Editor. " +
                               "Unity Hub > Installs > 6000.6.9f1 > gear > Add modules > Android Build Support " +
                               "(+ OpenJDK + Android SDK & NDK Tools), then restart Unity and run this item again.");
                return;
            }

            bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Debug.Log(ok ? "[CityDriver Setup] Active build target is now Android." : "[CityDriver Setup] Switching to Android failed - see Console.");
        }

        [BridgeCommand]
        [MenuItem("City Driver/Setup/Print Setup Report", priority = 60)]
        public static void PrintReportMenu() => PrintReport();

        // =============================================================================================
        // Steps
        // =============================================================================================

        private static void Step(string name, Action action)
        {
            try { action(); Debug.Log($"[CityDriver Setup] OK: {name}"); }
            catch (Exception e) { Debug.LogError($"[CityDriver Setup] FAILED: {name}\n{e}"); }
        }

        private static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidPackageId);

            // Landscape only (auto-rotates between left/right landscape).
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Android: IL2CPP + ARM64 (required for 64-bit store builds), Android 8.0+, newest installed target SDK.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        }

        private static void ConfigureQuality()
        {
            EnsureFolder(SettingsFolder);

            // 1) Create / refresh one URP asset per tier, cloned from the template's mobile asset.
            var urpAssets = new RenderPipelineAsset[Tiers.Length];
            for (int i = 0; i < Tiers.Length; i++)
            {
                string path = $"{SettingsFolder}/URP_{Tiers[i].Name}.asset";
                if (AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path) == null)
                {
                    if (!AssetDatabase.CopyAsset(MobileUrpTemplate, path))
                        throw new InvalidOperationException($"Could not copy '{MobileUrpTemplate}' to '{path}'.");
                }

                var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
                ApplyTierToUrpAsset(asset, Tiers[i]);
                urpAssets[i] = asset;
            }

            // 2) Replace the template's "Mobile" / "PC" levels with Low / Medium / High.
            var qualityObjects = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qualityObjects == null || qualityObjects.Length == 0)
                throw new InvalidOperationException("Could not load ProjectSettings/QualitySettings.asset.");

            var so = new SerializedObject(qualityObjects[0]);
            var levels = so.FindProperty("m_QualitySettings");
            levels.arraySize = Tiers.Length;

            for (int i = 0; i < Tiers.Length; i++)
            {
                var t = Tiers[i];
                var lvl = levels.GetArrayElementAtIndex(i);
                Write(lvl, "name", t.Name);
                Write(lvl, "pixelLightCount", i + 1);
                Write(lvl, "shadows", t.ShadowRes > 0 ? (t.SoftShadows ? 2 : 1) : 0);
                Write(lvl, "shadowCascades", t.Cascades);
                Write(lvl, "shadowDistance", t.ShadowDist);
                Write(lvl, "anisotropicTextures", t.Aniso);
                Write(lvl, "antiAliasing", 0);               // MSAA is controlled by the URP asset
                Write(lvl, "softParticles", t.SoftParticles);
                Write(lvl, "realtimeReflectionProbes", t.ReflectionProbes);
                Write(lvl, "vSyncCount", 0);                 // frame cap handled by PerformanceProfile
                Write(lvl, "lodBias", t.LodBias);
                Write(lvl, "skinWeights", t.SkinWeights);
                Write(lvl, "globalTextureMipmapLimit", t.TextureMipLimit);

                var pipeline = lvl.FindPropertyRelative("customRenderPipeline");
                if (pipeline != null) pipeline.objectReferenceValue = urpAssets[i];

                var excluded = lvl.FindPropertyRelative("excludedTargetPlatforms");
                if (excluded != null) excluded.arraySize = 0; // every level available on every platform
            }

            // 3) Default level per platform.
            var perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
            if (perPlatform != null && perPlatform.isArray)
            {
                for (int i = 0; i < perPlatform.arraySize; i++)
                {
                    var el = perPlatform.GetArrayElementAtIndex(i);
                    var key = el.FindPropertyRelative("first");
                    var val = el.FindPropertyRelative("second");
                    if (key == null || val == null) continue;
                    val.intValue = key.stringValue switch
                    {
                        "Android" or "iPhone" or "AndroidXR" or "MetaQuest" => DefaultAndroidTier,
                        "Standalone" => DefaultEditorStandaloneTier,
                        "WebGL" => DefaultWebTier,
                        _ => 0,
                    };
                }
            }
            else
            {
                Debug.LogWarning("[CityDriver Setup] m_PerPlatformDefaultQuality not found; per-platform defaults unchanged.");
            }

            var current = so.FindProperty("m_CurrentQuality");
            if (current != null) current.intValue = DefaultEditorStandaloneTier; // Editor on Intel Iris Xe: Low

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        private static void ApplyTierToUrpAsset(RenderPipelineAsset asset, Tier t)
        {
            var so = new SerializedObject(asset);
            Write(so, "m_SupportsHDR", t.Hdr);
            Write(so, "m_MSAA", t.Msaa);
            Write(so, "m_RenderScale", t.RenderScale);
            Write(so, "m_MainLightShadowsSupported", true);
            Write(so, "m_MainLightShadowmapResolution", t.ShadowRes);
            Write(so, "m_ShadowDistance", t.ShadowDist);
            Write(so, "m_ShadowCascadeCount", t.Cascades);
            Write(so, "m_SoftShadowsSupported", t.SoftShadows);
            Write(so, "m_AdditionalLightsRenderingMode", t.AdditionalLightsMode);
            Write(so, "m_AdditionalLightsPerObjectLimit", t.AdditionalLightsLimit);
            Write(so, "m_UseSRPBatcher", true);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureLayersTagsPhysics()
        {
            var tagObjects = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagObjects == null || tagObjects.Length == 0)
                throw new InvalidOperationException("Could not load ProjectSettings/TagManager.asset.");

            var so = new SerializedObject(tagObjects[0]);

            var tags = so.FindProperty("tags");
            foreach (string tag in TagNames)
            {
                bool exists = false;
                for (int i = 0; i < tags.arraySize; i++)
                    if (tags.GetArrayElementAtIndex(i).stringValue == tag) { exists = true; break; }
                if (exists) continue;
                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            }

            var layers = so.FindProperty("layers");
            for (int i = 0; i < LayerNames.Length; i++)
            {
                var slot = layers.GetArrayElementAtIndex(FirstUserLayer + i);
                if (string.IsNullOrEmpty(slot.stringValue) || slot.stringValue == LayerNames[i])
                    slot.stringValue = LayerNames[i];
                else
                    Debug.LogWarning($"[CityDriver Setup] Layer slot {FirstUserLayer + i} already holds '{slot.stringValue}'; not overwritten.");
            }

            so.ApplyModifiedProperties();

            // MissionTrigger only interacts with PlayerVehicle (everything else ignores it).
            int trigger = FirstUserLayer + Array.IndexOf(LayerNames, "MissionTrigger");
            int player = FirstUserLayer + Array.IndexOf(LayerNames, "PlayerVehicle");
            for (int layer = 0; layer < 32; layer++)
                Physics.IgnoreLayerCollision(trigger, layer, layer != player);
        }

        private static void CreatePerformanceProfile()
        {
            EnsureFolder(SettingsFolder + "/Resources");
            if (AssetDatabase.LoadAssetAtPath<PerformanceProfile>(PerformanceProfilePath) != null) return;

            var profile = ScriptableObject.CreateInstance<PerformanceProfile>();
            AssetDatabase.CreateAsset(profile, PerformanceProfilePath);
        }

        private static void CreateScenesAndBuildSettings()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[CityDriver Setup] Scene creation cancelled (unsaved scene changes).");
                return;
            }

            foreach (string path in ScenePaths)
            {
                if (File.Exists(path)) continue;
                EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, path);
            }

            // Build order: MainMenu (0), CityDriving (1), Garage (2). The template SampleScene stays on disk but leaves the build.
            var buildScenes = new EditorBuildSettingsScene[ScenePaths.Length];
            for (int i = 0; i < ScenePaths.Length; i++)
                buildScenes[i] = new EditorBuildSettingsScene(ScenePaths[i], true);
            EditorBuildSettings.scenes = buildScenes;

            EditorSceneManager.OpenScene(ScenePaths[0], OpenSceneMode.Single);
        }

        // =============================================================================================
        // Report
        // =============================================================================================

        private static void PrintReport()
        {
            var sb = new StringBuilder("[CityDriver Setup] REPORT\n");
            sb.AppendLine($"Unity {Application.unityVersion}");
            sb.AppendLine($"Company / Product : {PlayerSettings.companyName} / {PlayerSettings.productName}");
            sb.AppendLine($"Android package ID: {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}");
            sb.AppendLine($"Orientation       : {PlayerSettings.defaultInterfaceOrientation} (portrait={PlayerSettings.allowedAutorotateToPortrait}, landscapeL={PlayerSettings.allowedAutorotateToLandscapeLeft}, landscapeR={PlayerSettings.allowedAutorotateToLandscapeRight})");
            sb.AppendLine($"Android backend   : {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}, arch={PlayerSettings.Android.targetArchitectures}, minSdk={PlayerSettings.Android.minSdkVersion}");
            sb.AppendLine($"Color space       : {PlayerSettings.colorSpace}");
            sb.AppendLine($"Android support   : {(BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android) ? "installed" : "NOT INSTALLED")}; active target = {EditorUserBuildSettings.activeBuildTarget}");
            sb.AppendLine($"Quality levels    : {string.Join(", ", QualitySettings.names)} (current: {QualitySettings.names[QualitySettings.GetQualityLevel()]})");
            sb.Append("Layers            : ");
            for (int i = FirstUserLayer; i < FirstUserLayer + LayerNames.Length; i++) sb.Append($"{i}={LayerMask.LayerToName(i)} ");
            sb.AppendLine();
            sb.AppendLine($"Build scenes      : {string.Join(", ", Array.ConvertAll(EditorBuildSettings.scenes, s => $"{(s.enabled ? "" : "(off) ")}{s.path}"))}");
            sb.AppendLine($"PerformanceProfile: {(AssetDatabase.LoadAssetAtPath<PerformanceProfile>(PerformanceProfilePath) != null ? "present" : "MISSING")}");
            Debug.Log(sb.ToString());
        }

        // =============================================================================================
        // Helpers
        // =============================================================================================

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }

        private static void Write(SerializedObject so, string name, object value) => WriteProperty(so.FindProperty(name), value, name);
        private static void Write(SerializedProperty parent, string relative, object value) => WriteProperty(parent.FindPropertyRelative(relative), value, relative);

        private static void WriteProperty(SerializedProperty p, object value, string label)
        {
            if (p == null) { Debug.LogWarning($"[CityDriver Setup] Property '{label}' not found; skipped."); return; }

            switch (p.propertyType)
            {
                // Enums here (MSAA, shadow resolution, quality fields) store their raw value, so intValue is correct
                // (enumValueIndex would be a list position, which is wrong for values like 1/2/4/8).
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Integer:p.intValue = Convert.ToInt32(value is bool bi ? (bi ? 1 : 0) : value); break;
                case SerializedPropertyType.Boolean: p.boolValue = value is bool b ? b : Convert.ToInt32(value) != 0; break;
                case SerializedPropertyType.Float: p.floatValue = Convert.ToSingle(value); break;
                case SerializedPropertyType.String: p.stringValue = (string)value; break;
                default: Debug.LogWarning($"[CityDriver Setup] Property '{label}' has unsupported type {p.propertyType}; skipped."); break;
            }
        }
    }
}
