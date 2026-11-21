using System.Linq;
using CityDriver.Controls;
using CityDriver.Navigation;
using CityDriver.Player;
using CityDriver.UI;
using CityDriver.World;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Builds the driving HUD for landscape phones: safe-area root, speed/gear cluster (bottom centre), condition bar,
    /// GPS banner (top centre), minimap and MAP button (top right), touch controls (bottom corners), and the full-screen map.
    /// Also adds the NavigationService and an EventSystem, and gives the vehicle prefab its touch input adapter.
    /// Everything is anchored to the safe area, so it adapts to any aspect ratio.
    /// </summary>
    public static class HudBuilder
    {
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        private static readonly Color Panel = new Color(0.05f, 0.07f, 0.10f, 0.70f);
        private static readonly Color Accent = new Color(0.35f, 0.70f, 1.00f, 1f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.60f);
        private static Font font;

        [MenuItem("City Driver/Build/HUD + Touch Controls + Maps", priority = 30)]
        [BridgeCommand]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[HUD] Exit Play Mode first."); return; }
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty && active.path != CityScenePath) { Debug.LogError("[HUD] Active scene has unsaved changes."); return; }

            var scene = EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            UiSprites.EnsureAll();
            Texture2D mapTexture = MapBaker.Bake();
            AddMobileInputToVehiclePrefab();

            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "VehicleHUD" || root.name == "DrivingHUD") Object.DestroyImmediate(root);

            EnsureEventSystem();
            NavigationService nav = EnsureNavigation(scene);
            DrivingHudRefs refs = BuildHud(mapTexture);
            WireSpawner(scene, refs);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[HUD] Built DrivingHUD (touch controls, minimap, full map, GPS banner) and NavigationService.");
        }

        // ============================================================================ scene plumbing

        private static void AddMobileInputToVehiclePrefab()
        {
            using (var edit = new PrefabUtility.EditPrefabContentsScope(VehicleBuilder.PrefabPath))
            {
                if (edit.prefabContentsRoot.GetComponent<MobileInputController>() == null)
                    edit.prefabContentsRoot.AddComponent<MobileInputController>();
            }
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private static NavigationService EnsureNavigation(UnityEngine.SceneManagement.Scene scene)
        {
            var systems = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Systems") ?? new GameObject("Systems");
            Transform t = systems.transform.Find("Navigation");
            if (t == null) { t = new GameObject("Navigation").transform; t.SetParent(systems.transform, false); }
            var nav = t.GetComponent<NavigationService>() ?? t.gameObject.AddComponent<NavigationService>();
            var so = new SerializedObject(nav);
            so.FindProperty("graphAsset").objectReferenceValue = AssetDatabase.LoadAssetAtPath<RoadGraphAsset>(MapBaker.RoadGraphPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            return nav;
        }

        private sealed class DrivingHudRefs { public VehicleHUD Hud; public MinimapView Minimap; public FullMapView FullMap; }

        private static void WireSpawner(UnityEngine.SceneManagement.Scene scene, DrivingHudRefs refs)
        {
            var spawner = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlayerSpawner>(true)).FirstOrDefault();
            if (spawner == null) { Debug.LogError("[HUD] PlayerSpawner not found; run City Driver > Build > Phase 4 first."); return; }
            var so = new SerializedObject(spawner);
            so.FindProperty("hud").objectReferenceValue = refs.Hud;
            so.FindProperty("minimap").objectReferenceValue = refs.Minimap;
            so.FindProperty("fullMap").objectReferenceValue = refs.FullMap;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ============================================================================ HUD layout (reference 1920 x 1080)

        private static DrivingHudRefs BuildHud(Texture2D mapTexture)
        {
            var canvasGo = new GameObject("DrivingHUD", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<GraphicRaycaster>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform safe = Rt("SafeArea", canvasGo.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            var refs = new DrivingHudRefs();

            // ---- top left: title, condition, editor hint
            Text(safe, "Title", "CITY DRIVER: UNDERCOVER", 30, TextAnchor.UpperLeft, Color.white, TL(40, -30, 700, 44));
            Text(safe, "ConditionText", "CONDITION 100%", 24, TextAnchor.UpperLeft, Color.white, TL(40, -80, 400, 34), out Text conditionText);
            RectTransform barBg = Img(safe, "ConditionBackground", UiSprites.Round, new Color(0, 0, 0, 0.55f), TL(40, -118, 300, 16), true);
            Image fill = Img(barBg, "ConditionFill", UiSprites.Round, new Color(0.30f, 0.85f, 0.45f), Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(2, 2), new Vector2(296, 12)), true).GetComponent<Image>();
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
            Text(safe, "Hints", "W accelerate  S reverse  Space brake  A/D steer  Shift handbrake  C camera  R recover  M map  F2 waypoint", 18, TextAnchor.UpperLeft, Dim, TL(40, -146, 1100, 28));

            // ---- bottom centre: speed + gear
            RectTransform cluster = Img(safe, "SpeedCluster", UiSprites.Round, Panel, Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(440, 170)), true);
            Text(cluster, "Speed", "0", 100, TextAnchor.MiddleCenter, Color.white, Center(-45, 14, 250, 120), out Text speedText);
            Text(cluster, "SpeedUnit", "km/h", 28, TextAnchor.MiddleCenter, Dim, Center(-45, -52, 250, 40));
            Text(cluster, "GearLabel", "GEAR", 20, TextAnchor.MiddleCenter, Dim, Center(150, 48, 110, 30));
            Text(cluster, "Gear", "N", 76, TextAnchor.MiddleCenter, Accent, Center(150, -6, 110, 100), out Text gearText);

            // ---- top centre: GPS banner (shown only with a route)
            RectTransform bannerHost = Rt("NavigationBanner", safe);
            Stretch(bannerHost);
            RectTransform bannerPanel = Img(bannerHost, "Panel", UiSprites.Round, Panel, Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(760, 112)), true);
            RectTransform turnArrow = Img(bannerPanel, "TurnArrow", UiSprites.Triangle, Accent, Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(68, 68)), false);
            Text(bannerPanel, "Instruction", "", 36, TextAnchor.MiddleLeft, Color.white, Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(112, 20), new Vector2(630, 50)), out Text instructionText);
            Text(bannerPanel, "Detail", "", 24, TextAnchor.MiddleLeft, Dim, Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(112, -24), new Vector2(630, 36)), out Text detailText);
            bannerPanel.gameObject.SetActive(false);
            var banner = bannerHost.gameObject.AddComponent<NavigationBanner>();
            Set(banner, "root", bannerPanel.gameObject); Set(banner, "instructionText", instructionText); Set(banner, "detailText", detailText); Set(banner, "arrow", turnArrow);

            // ---- top right: minimap, map button
            RectTransform frame = Img(safe, "Minimap", UiSprites.Round, Panel, Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(296, 296)), true);
            RectTransform mask = Rt("Mask", frame);
            Place(mask, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280, 280));
            mask.gameObject.AddComponent<RectMask2D>();
            RectTransform container = Rt("MapContainer", mask);
            Place(container, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, 420));
            RawImage baseMap = Raw(container, "BaseMap", mapTexture);
            RawImage overlay = Raw(container, "RouteOverlay", null);
            Img(mask, "PlayerArrow", UiSprites.Triangle, new Color(1f, 1f, 1f, 1f), Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34)), false);
            var minimap = frame.gameObject.AddComponent<MinimapView>();
            Set(minimap, "mapContainer", container); Set(minimap, "baseMap", baseMap); Set(minimap, "routeOverlay", overlay);
            refs.Minimap = minimap;

            RectTransform mapButton = Img(safe, "MapButton", UiSprites.Round, Panel, Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -348), new Vector2(140, 64)), true);
            Text(mapButton, "Label", "MAP", 30, TextAnchor.MiddleCenter, Color.white, Stretched());
            var mapBtn = mapButton.gameObject.AddComponent<Button>();

            // ---- touch controls
            RectTransform controls = Rt("TouchControls", safe);
            Stretch(controls);
            RectTransform left = Pad(controls, "SteerLeft", UiSprites.Round, Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(50, 60), new Vector2(210, 210)), UiSprites.Triangle, 90f, null);
            RectTransform right = Pad(controls, "SteerRight", UiSprites.Round, Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(290, 60), new Vector2(210, 210)), UiSprites.Triangle, -90f, null);
            RectTransform gas = Pad(controls, "Accelerator", UiSprites.Round, Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-50, 60), new Vector2(230, 330)), null, 0f, "GAS");
            RectTransform brake = Pad(controls, "Brake", UiSprites.Round, Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-310, 60), new Vector2(200, 230)), null, 0f, "BRAKE");
            RectTransform reverse = Pad(controls, "Reverse", UiSprites.Round, Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-310, 310), new Vector2(200, 110)), null, 0f, "REV");
            RectTransform cam = Pad(controls, "CameraReset", UiSprites.Circle, Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -432), new Vector2(110, 110)), null, 0f, "CAM");
            var panel = controls.gameObject.AddComponent<TouchControlsPanel>();
            Set(panel, "steerLeft", left); Set(panel, "steerRight", right); Set(panel, "accelerator", gas); Set(panel, "brake", brake); Set(panel, "reverse", reverse); Set(panel, "cameraReset", cam);

            // ---- vehicle HUD component
            var hud = canvasGo.AddComponent<VehicleHUD>();
            Set(hud, "speedText", speedText); Set(hud, "gearText", gearText); Set(hud, "conditionText", conditionText); Set(hud, "conditionFill", fill);
            refs.Hud = hud;

            // ---- full-screen map (hidden until opened)
            RectTransform host = Rt("FullMap", safe);
            Stretch(host);
            RectTransform mapPanel = Img(host, "Panel", UiSprites.Round, new Color(0.03f, 0.04f, 0.06f, 0.96f), Place(new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, Vector2.zero), true);
            Stretch(mapPanel);
            RectTransform mapRect = Rt("MapArea", mapPanel);
            Place(mapRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-330, 0), new Vector2(980, 980));
            RawImage fullBase = Raw(mapRect, "BaseMap", mapTexture, true);
            RawImage fullOverlay = Raw(mapRect, "RouteOverlay", null, false);
            fullBase.uvRect = fullOverlay.uvRect = new Rect(0, 0, 1, 1);
            RectTransform fullArrow = Img(mapRect, "PlayerArrow", UiSprites.Triangle, Color.white, Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30)), false);
            Text(mapPanel, "Title", "CITY MAP", 56, TextAnchor.UpperLeft, Color.white, Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -50), new Vector2(520, 70)));
            Text(mapPanel, "Info", "Tap the map to set a destination", 30, TextAnchor.UpperLeft, Accent, Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -140), new Vector2(520, 130)), out Text infoText);
            Text(mapPanel, "Legend", "<color=#FFB840>Highway</color>   <color=#EBEBF2>Arterial</color>\n<color=#FF4D4D>Hospital</color>   <color=#4D80FF>Police</color>   <color=#4DE666>Fuel</color>\n<color=#FFCC33>Garage / Repair</color>   <color=#CC66FF>Safe house</color>", 24, TextAnchor.UpperLeft, Dim, Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -330), new Vector2(520, 130)));
            RectTransform closeBtn = Img(mapPanel, "CloseButton", UiSprites.Round, Panel, Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-60, 60), new Vector2(230, 80)), true);
            Text(closeBtn, "Label", "CLOSE", 32, TextAnchor.MiddleCenter, Color.white, Stretched());
            RectTransform clearBtn = Img(mapPanel, "ClearButton", UiSprites.Round, Panel, Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-310, 60), new Vector2(300, 80)), true);
            Text(clearBtn, "Label", "CLEAR ROUTE", 28, TextAnchor.MiddleCenter, Color.white, Stretched());
            var closeButton = closeBtn.gameObject.AddComponent<Button>();
            var clearButton = clearBtn.gameObject.AddComponent<Button>();

            var fullMap = host.gameObject.AddComponent<FullMapView>();
            Set(fullMap, "panel", mapPanel.gameObject); Set(fullMap, "mapRect", mapRect); Set(fullMap, "baseMap", fullBase);
            Set(fullMap, "routeOverlay", fullOverlay); Set(fullMap, "playerArrow", fullArrow); Set(fullMap, "infoText", infoText);
            var receiver = fullBase.gameObject.AddComponent<MapClickReceiver>();
            Set(receiver, "target", fullMap);
            fullBase.raycastTarget = true;
            mapPanel.gameObject.SetActive(false);
            refs.FullMap = fullMap;

            UnityEventTools.AddPersistentListener(mapBtn.onClick, fullMap.Toggle);
            UnityEventTools.AddPersistentListener(closeButton.onClick, fullMap.Toggle);
            UnityEventTools.AddPersistentListener(clearButton.onClick, fullMap.ClearRoute);
            return refs;
        }

        // ============================================================================ small UI helpers

        private struct Layout { public Vector2 aMin, aMax, pivot, pos, size; }

        private static Layout Place(Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size) => new Layout { aMin = anchor, aMax = anchor, pivot = pivot, pos = pos, size = size };
        private static Layout TL(float x, float y, float w, float h) => Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y), new Vector2(w, h));
        private static Layout Center(float x, float y, float w, float h) => Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(w, h));
        private static Layout Stretched() => new Layout { aMin = Vector2.zero, aMax = Vector2.one, pivot = new Vector2(0.5f, 0.5f), pos = Vector2.zero, size = Vector2.zero };

        private static RectTransform Rt(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Apply(RectTransform rt, Layout l)
        {
            rt.anchorMin = l.aMin; rt.anchorMax = l.aMax; rt.pivot = l.pivot;
            rt.anchoredPosition = l.pos; rt.sizeDelta = l.size;
        }

        private static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size) { Apply(rt, Place(anchor, pivot, pos, size)); return rt; }

        private static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

        private static RectTransform Img(Transform parent, string name, Sprite sprite, Color color, Layout l, bool sliced)
        {
            RectTransform rt = Rt(name, parent);
            Apply(rt, l);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.color = color; img.type = sliced ? Image.Type.Sliced : Image.Type.Simple; img.raycastTarget = false;
            return rt;
        }

        private static Text Text(Transform parent, string name, string content, int size, TextAnchor align, Color color, Layout l) => Text(parent, name, content, size, align, color, l, out _);

        private static Text Text(Transform parent, string name, string content, int size, TextAnchor align, Color color, Layout l, out Text result)
        {
            RectTransform rt = Rt(name, parent);
            Apply(rt, l);
            var t = rt.gameObject.AddComponent<Text>();
            t.text = content; t.font = font; t.fontSize = size; t.alignment = align; t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            result = t;
            return t;
        }

        private static RawImage Raw(Transform parent, string name, Texture texture, bool raycast = false)
        {
            RectTransform rt = Rt(name, parent);
            Stretch(rt);
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = texture; raw.raycastTarget = raycast;
            return raw;
        }

        // A touch button: translucent rounded pad, optional triangle icon (rotated) or label.
        private static RectTransform Pad(Transform parent, string name, Sprite background, Layout l, Sprite icon, float iconRotation, string label)
        {
            RectTransform rt = Img(parent, name, background, new Color(1f, 1f, 1f, 0.35f), l, background == UiSprites.Round);
            if (icon != null)
            {
                RectTransform ic = Img(rt, "Icon", icon, new Color(1f, 1f, 1f, 0.9f), Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90, 90)), false);
                ic.localRotation = Quaternion.Euler(0, 0, iconRotation);
            }

            if (label != null) Text(rt, "Label", label, 34, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.95f), Stretched());
            return rt;
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[HUD] Field '{field}' not found on {target.GetType().Name}."); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
