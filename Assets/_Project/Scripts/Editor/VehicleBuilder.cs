using System.IO;
using CityDriver.CameraSystem;
using CityDriver.Controls;
using CityDriver.Player;
using CityDriver.UI;
using CityDriver.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Phase 4: builds the placeholder four-door sedan prefab (Rigidbody + 4 WheelColliders + visuals), its VehicleStats asset,
    /// and wires the spawner, follow camera and HUD into the CityDriving scene. It never touches the "City" object.
    /// Vehicle space: +Z forward, +X right, pivot at ground level under the centre of the car.
    /// </summary>
    public static class VehicleBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Vehicles/Sedan_Placeholder.prefab";
        public const string StatsPath = "Assets/_Project/Data/Vehicles/Sedan_Default.asset";
        private const string PhysicsMaterialPath = "Assets/_Project/Data/Vehicles/PM_VehicleBody.asset";
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        // Dimensions (m)
        private const float WheelRadius = 0.33f, WheelWidth = 0.22f;
        private const float TrackHalf = 0.84f, WheelBaseHalf = 1.36f;
        private const float SuspensionDistance = 0.22f, TargetPosition = 0.5f;

        [MenuItem("City Driver/Build/Phase 4 - Build Vehicle + Scene Setup", priority = 10)]
        [BridgeCommand]
        public static void BuildVehicle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Vehicle] Exit Play Mode first."); return; }
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty && active.path != CityScenePath) { Debug.LogError($"[Vehicle] Scene '{active.name}' has unsaved changes. Save it first."); return; }

            var scene = EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);

            CityAssetFactory.EnsureFolder("Assets/_Project/Prefabs/Vehicles");
            CityAssetFactory.EnsureFolder("Assets/_Project/Data/Vehicles");

            VehicleStats stats = LoadOrCreateStats();
            GameObject prefab = BuildPrefab(stats);
            SetupScene(scene, prefab);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Vehicle] Built Sedan_Placeholder prefab and scene setup (PlayerSpawner, VehicleHUD, follow camera).");
        }

        /// <summary>Regenerates only the sedan prefab (visuals + physics components); the scene, HUD and stats asset are left alone.</summary>
        [BridgeCommand]
        public static void RebuildPrefabOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Vehicle] Exit Play Mode first."); return; }
            CityAssetFactory.EnsureFolder("Assets/_Project/Prefabs/Vehicles");
            BuildPrefab(LoadOrCreateStats());
            AssetDatabase.SaveAssets();
            Debug.Log("[Vehicle] Rebuilt sedan prefab only.");
        }

        /// <summary>Resets the Sedan_Default stats asset to the code defaults (use after changing defaults in VehicleStats).</summary>
        [MenuItem("City Driver/Build/Reset Sedan Tuning To Defaults", priority = 11)]
        [BridgeCommand]
        public static void ResetTuningToDefaults()
        {
            VehicleStats stats = LoadOrCreateStats();
            var fresh = ScriptableObject.CreateInstance<VehicleStats>();
            EditorUtility.CopySerialized(fresh, stats);
            Object.DestroyImmediate(fresh);
            stats.name = "Sedan_Default"; // CopySerialized would otherwise blank the asset name
            EditorUtility.SetDirty(stats);
            AssetDatabase.SaveAssets();
            Debug.Log("[Vehicle] Sedan_Default tuning reset to code defaults.");
        }

        /// <summary>Repairs the asset's internal name (it was blank, which made Unity warn) without touching any tuning value.</summary>
        [BridgeCommand]
        public static void FixStatsName()
        {
            VehicleStats stats = LoadOrCreateStats();
            var so = new SerializedObject(stats);
            so.FindProperty("m_Name").stringValue = "Sedan_Default";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(stats);
            AssetDatabase.SaveAssets();
            Debug.Log("[Vehicle] Sedan_Default internal name set.");
        }

        // ================================================================================== assets

        private static VehicleStats LoadOrCreateStats()
        {
            var stats = AssetDatabase.LoadAssetAtPath<VehicleStats>(StatsPath);
            if (stats == null)
            {
                stats = ScriptableObject.CreateInstance<VehicleStats>();
                AssetDatabase.CreateAsset(stats, StatsPath);
            }

            return stats; // existing tuning is preserved on rebuilds
        }

        private static PhysicsMaterial LoadOrCreateBodyMaterial()
        {
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysicsMaterialPath);
            if (pm == null)
            {
                pm = new PhysicsMaterial("PM_VehicleBody");
                AssetDatabase.CreateAsset(pm, PhysicsMaterialPath);
            }

            // Low friction so the body slides along walls instead of snagging and flipping the car.
            pm.dynamicFriction = 0.05f;
            pm.staticFriction = 0.05f;
            pm.bounciness = 0f;
            pm.frictionCombine = PhysicsMaterialCombine.Minimum;
            pm.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(pm);
            return pm;
        }

        // ================================================================================== prefab

        private static GameObject BuildPrefab(VehicleStats stats)
        {
            Material paint = CityAssetFactory.Lit("M_CarPaint", new Color(0.96f, 0.74f, 0.05f), null, 0.88f, 0.35f);          // taxi yellow
            Material glass = CityAssetFactory.Lit("M_CarGlass", new Color(0.05f, 0.07f, 0.10f), null, 0.9f, 0.1f);
            Material trim = CityAssetFactory.Lit("M_CarTrim", new Color(0.07f, 0.07f, 0.08f), null, 0.3f);
            Material tyre = CityAssetFactory.Lit("M_CarTyre", new Color(0.11f, 0.11f, 0.12f), null, 0.35f);
            Material rimDark = CityAssetFactory.Lit("M_CarRimDark", new Color(0.30f, 0.31f, 0.36f), null, 0.65f, 0.45f);
            Material disc = CityAssetFactory.Lit("M_CarDisc", new Color(0.52f, 0.52f, 0.55f), null, 0.5f, 0.5f);
            Material rim = CityAssetFactory.Lit("M_CarRim", new Color(0.72f, 0.72f, 0.75f), null, 0.8f, 0.8f);
            Material headlight = CityAssetFactory.Lit("M_CarHeadlight", new Color(1f, 1f, 0.95f), null, 0.5f, 0f, new Color(1.4f, 1.4f, 1.3f));
            Material taillight = CityAssetFactory.Lit("M_CarTaillight", new Color(0.8f, 0.05f, 0.05f), null, 0.5f, 0f, new Color(0.35f, 0.02f, 0.02f));
            Material roofSign = CityAssetFactory.Lit("M_CarRoofSign", new Color(1f, 0.95f, 0.7f), null, 0.4f, 0f, new Color(1.2f, 1.1f, 0.5f));

            var root = new GameObject("Sedan_Placeholder");
            Material chrome = CityAssetFactory.Lit("M_CarChrome", new Color(0.86f, 0.87f, 0.91f), null, 0.8f, 0.6f);
            Material plate = CityAssetFactory.Lit("M_CarPlate", new Color(0.93f, 0.93f, 0.9f), null, 0.3f);
            var kit = new CarKit.Mats { paint = paint, glass = glass, trim = trim, chrome = chrome, tyre = tyre, rim = rim, head = headlight, tail = taillight, plate = roofSign, rimDark = rimDark, disc = disc };
            kit.plate = plate;

            // ---- body visuals (all colliders are separate, below). Roof sign keeps its own emissive material.
            var visuals = new GameObject("Body").transform;
            visuals.SetParent(root.transform, false);
            CarKit.AddBody(visuals, kit, CarShapes.Find("sedan_compact"));
            var sign = visuals.Find("RoofSign"); if (sign != null) sign.GetComponent<Renderer>().sharedMaterial = roofSign;

            // ---- colliders (compound, low friction material)
            var colliders = new GameObject("Colliders").transform;
            colliders.SetParent(root.transform, false);
            PhysicsMaterial bodyMaterial = LoadOrCreateBodyMaterial();
            AddBox(colliders, "LowerCollider", new Vector3(0, 0.65f, 0), new Vector3(1.86f, 0.55f, 4.5f), bodyMaterial);
            AddBox(colliders, "CabinCollider", new Vector3(0, 1.19f, -0.15f), new Vector3(1.56f, 0.52f, 2.35f), bodyMaterial);

            // ---- wheels: collider (no visual) + separate visual pivot (axle along local X)
            var wheelColliders = new GameObject("WheelColliders").transform;
            wheelColliders.SetParent(root.transform, false);
            var wheelVisuals = new GameObject("Wheels").transform;
            wheelVisuals.SetParent(root.transform, false);

            string[] names = { "FL", "FR", "RL", "RR" };
            float[] xs = { -TrackHalf, TrackHalf, -TrackHalf, TrackHalf };
            float[] zs = { WheelBaseHalf, WheelBaseHalf, -WheelBaseHalf, -WheelBaseHalf };
            var wcs = new WheelCollider[4];
            var vis = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var wcGo = new GameObject("WC_" + names[i]);
                wcGo.transform.SetParent(wheelColliders, false);
                // At rest the wheel hangs TargetPosition of the way down the travel, so its centre sits at WheelRadius height.
                wcGo.transform.localPosition = new Vector3(xs[i], WheelRadius + SuspensionDistance * TargetPosition, zs[i]);
                var wc = wcGo.AddComponent<WheelCollider>();
                wc.radius = WheelRadius;
                wc.suspensionDistance = SuspensionDistance;
                wcs[i] = wc;

                var pivot = new GameObject("Wheel_" + names[i]).transform;
                pivot.SetParent(wheelVisuals, false);
                pivot.localPosition = new Vector3(xs[i], WheelRadius, zs[i]);
                CarKit.AddWheel(pivot, WheelRadius, WheelWidth, Mathf.Sign(xs[i]), kit);
                vis[i] = pivot;
            }

            // ---- components
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = stats.mass;

            var input = root.AddComponent<VehicleInputHandler>();
            root.AddComponent<KeyboardInputController>();
            root.AddComponent<MobileInputController>();     // touch controls feed the vehicle through this; without it the HUD buttons do nothing
            var damage = root.AddComponent<VehicleDamage>();
            var controller = root.AddComponent<VehicleController>();
            var wheelSync = root.AddComponent<VehicleWheelVisuals>();
            root.AddComponent<VehicleLights>();

            var so = new SerializedObject(controller);
            so.FindProperty("stats").objectReferenceValue = stats;
            so.FindProperty("frontLeft").objectReferenceValue = wcs[0];
            so.FindProperty("frontRight").objectReferenceValue = wcs[1];
            so.FindProperty("rearLeft").objectReferenceValue = wcs[2];
            so.FindProperty("rearRight").objectReferenceValue = wcs[3];
            so.FindProperty("inputHandler").objectReferenceValue = input;
            so.FindProperty("damage").objectReferenceValue = damage;
            so.ApplyModifiedPropertiesWithoutUndo();

            var wso = new SerializedObject(wheelSync);
            var colProp = wso.FindProperty("colliders");
            var visProp = wso.FindProperty("visuals");
            colProp.arraySize = visProp.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                colProp.GetArrayElementAtIndex(i).objectReferenceValue = wcs[i];
                visProp.GetArrayElementAtIndex(i).objectReferenceValue = vis[i];
            }

            wso.ApplyModifiedPropertiesWithoutUndo();

            controller.ApplyStats(); // bake tuning into the prefab's Rigidbody + WheelColliders so it looks right in the Inspector

            SetLayerRecursively(root, LayerMask.NameToLayer("PlayerVehicle"));
            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        private static GameObject P(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
            => CityAssetFactory.Prim(type, name, parent, pos, scale, mat, false, euler);

        private static void AddBox(Transform parent, string name, Vector3 center, Vector3 size, PhysicsMaterial material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            box.sharedMaterial = material;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursively(t.gameObject, layer);
        }

        // ================================================================================== scene

        private static void SetupScene(UnityEngine.SceneManagement.Scene scene, GameObject vehiclePrefab)
        {
            // Rebuild only our own roots; "City" is never touched.
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Systems" || root.name == "VehicleHUD") Object.DestroyImmediate(root);

            // Camera: add the follow controller to the existing Main Camera.
            Camera cam = null;
            foreach (var root in scene.GetRootGameObjects()) { cam = root.GetComponentInChildren<Camera>(); if (cam != null) break; }
            if (cam == null) throw new System.InvalidOperationException("No camera found in the CityDriving scene.");
            var follow = cam.GetComponent<VehicleCameraController>() ?? cam.gameObject.AddComponent<VehicleCameraController>();
            var cso = new SerializedObject(follow);
            cso.FindProperty("collisionMask").intValue = LayerMask.GetMask("Road", "Environment");
            cso.ApplyModifiedPropertiesWithoutUndo();

            VehicleHUD hud = BuildHud();

            var systems = new GameObject("Systems");
            var spawner = new GameObject("PlayerSpawner").AddComponent<PlayerSpawner>();
            spawner.transform.SetParent(systems.transform, false);
            var sso = new SerializedObject(spawner);
            sso.FindProperty("vehiclePrefab").objectReferenceValue = vehiclePrefab;
            sso.FindProperty("cameraController").objectReferenceValue = follow;
            sso.FindProperty("hud").objectReferenceValue = hud;
            sso.FindProperty("groundMask").intValue = LayerMask.GetMask("Road", "Environment");
            sso.ApplyModifiedPropertiesWithoutUndo();
        }

        private static VehicleHUD BuildHud()
        {
            var canvasGo = new GameObject("VehicleHUD", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Transform t = canvasGo.transform;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var white = Color.white;
            var dim = new Color(1f, 1f, 1f, 0.55f);

            // Bottom-right: speed block
            Text speed = MakeText(t, "Speed", "0", 120, TextAnchor.LowerRight, font, white, new Vector2(1, 0), new Vector2(-60, 110), new Vector2(360, 140));
            MakeText(t, "SpeedUnit", "km/h", 36, TextAnchor.LowerRight, font, dim, new Vector2(1, 0), new Vector2(-60, 60), new Vector2(360, 50));
            Text gear = MakeText(t, "Gear", "N", 64, TextAnchor.LowerRight, font, new Color(0.35f, 0.7f, 1f), new Vector2(1, 0), new Vector2(-60, 255), new Vector2(200, 80));
            MakeText(t, "GearLabel", "GEAR", 24, TextAnchor.LowerRight, font, dim, new Vector2(1, 0), new Vector2(-270, 262), new Vector2(120, 40));

            // Bottom-left: condition bar
            var barBg = MakeImage(t, "ConditionBackground", new Color(0, 0, 0, 0.55f), new Vector2(0, 0), new Vector2(60, 60), new Vector2(380, 26));
            var fill = MakeImage(barBg.transform, "ConditionFill", new Color(0.3f, 0.85f, 0.45f), new Vector2(0, 0), new Vector2(3, 3), new Vector2(374, 20));
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            Text condition = MakeText(t, "ConditionText", "CONDITION 100%", 28, TextAnchor.LowerLeft, font, white, new Vector2(0, 0), new Vector2(60, 95), new Vector2(380, 40));

            // Top-left: title + control hints (Editor testing aid)
            MakeText(t, "Title", "CITY DRIVER: UNDERCOVER", 30, TextAnchor.UpperLeft, font, white, new Vector2(0, 1), new Vector2(60, -40), new Vector2(700, 44));
            MakeText(t, "Hints", "W accelerate | S reverse | Space brake | A/D steer | Shift handbrake | C camera | R recover", 22, TextAnchor.UpperLeft, font, dim, new Vector2(0, 1), new Vector2(60, -84), new Vector2(1300, 34));

            var hud = canvasGo.AddComponent<VehicleHUD>();
            var so = new SerializedObject(hud);
            so.FindProperty("speedText").objectReferenceValue = speed;
            so.FindProperty("gearText").objectReferenceValue = gear;
            so.FindProperty("conditionText").objectReferenceValue = condition;
            so.FindProperty("conditionFill").objectReferenceValue = fill;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }

        // Anchored at a corner of the parent: anchor (0|1, 0|1), position measured from that corner (inward), pivot = anchor.
        private static Text MakeText(Transform parent, string name, string content, int size, TextAnchor align, Font font, Color color, Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;
            var text = go.AddComponent<Text>();
            text.text = content; text.font = font; text.fontSize = size; text.alignment = align; text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Image MakeImage(Transform parent, string name, Color color, Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}
