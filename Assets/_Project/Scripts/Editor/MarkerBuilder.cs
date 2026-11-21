using System.Collections.Generic;
using System.Linq;
using CityDriver.Missions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Phase A: replaces the old always-on beacons with the small waypoint indicators and adds the MarkerDirector (the only
    /// thing that decides which waypoint is visible). Marker objects, ids and positions are never touched; only the
    /// prefab content under them changes. Does not modify the "City" object other than via those prefab instances.
    /// </summary>
    public static class MarkerBuilder
    {
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        [MenuItem("City Driver/Build/Phase A - Waypoint Markers", priority = 20)]
        [BridgeCommand]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Markers] Exit Play Mode first."); return; }
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty && active.path != CityScenePath) { Debug.LogError($"[Markers] Scene '{active.name}' has unsaved changes."); return; }

            var scene = EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);

            // 1) Snapshot marker ids + positions to prove they are untouched afterwards.
            var before = Snapshot();

            // 2) Rebuild both prefabs in place (same paths => scene instances update).
            CityAssetFactory.EnsureFolder(CityAssetFactory.EnvPrefabsDir);
            MarkerAssets.Build("PickupMarker", MarkerAssets.PickupColor);
            MarkerAssets.Build("DropoffMarker", MarkerAssets.DropoffColor);

            // 3) Director + debug keys under Systems.
            var systems = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Systems");
            if (systems == null) systems = new GameObject("Systems");
            var markers = systems.transform.Find("Markers");
            if (markers == null)
            {
                markers = new GameObject("Markers").transform;
                markers.SetParent(systems.transform, false);
            }

            if (markers.GetComponent<MarkerDirector>() == null) markers.gameObject.AddComponent<MarkerDirector>();
            if (markers.GetComponent<MarkerDebugControls>() == null) markers.gameObject.AddComponent<MarkerDebugControls>();

            // 4) HUD hint text mentions the new debug keys.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var text in root.GetComponentsInChildren<Text>(true))
                    if (text.name == "Hints") text.text = "W accelerate | S reverse | Space brake | A/D steer | Shift handbrake | C camera | R recover | F2 next waypoint | F3 all waypoints";

            // 5) Verify marker identity + position unchanged.
            var after = Snapshot();
            bool same = before.Count == after.Count && before.All(kv => after.TryGetValue(kv.Key, out var p) && Vector3.Distance(p, kv.Value) < 0.001f);
            Debug.Log($"[Markers] {after.Count} markers, ids/positions unchanged: {same}");
            if (!same) Debug.LogError("[Markers] Marker identity/position changed unexpectedly!");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static Dictionary<string, Vector3> Snapshot()
        {
            var d = new Dictionary<string, Vector3>();
            foreach (var m in Object.FindObjectsByType<LocationMarker>()) d[m.LocationId] = m.transform.position;
            return d;
        }
    }
}
