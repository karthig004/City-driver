using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CityDriver.Missions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Read-only visual audit of the CityDriving scene. Reproduces the player's camera at spawn (no objects are created
    /// or changed), then reports what is visible, what is green or oversized, and the lighting / quality / post-processing
    /// state. Output: Bridge/visual_audit.txt.
    /// </summary>
    public static class VisualAudit
    {
        private const string CityScenePath = "Assets/_Project/Scenes/CityDriving/CityDriving.unity";

        [BridgeCommand]
        public static void Run()
        {
            if (EditorSceneManager.GetActiveScene().path != CityScenePath) EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            var sb = new StringBuilder();

            // ---- 1. Camera at spawn, exactly as VehicleCameraController frames it (distance 6.5, height 2.6, lookHeight 1.1, fov 60)
            LocationMarker start = LocationMarker.Find(LocationKind.PlayerStart);
            Transform startT = Object.FindObjectsByType<LocationMarker>().First(m => m.Kind == LocationKind.PlayerStart).transform;
            Quaternion yaw = Quaternion.Euler(0, startT.eulerAngles.y, 0);
            Vector3 carPos = startT.position;
            Vector3 pivot = carPos + Vector3.up * 1.1f;
            Vector3 camPos = pivot + yaw * new Vector3(0, 2.6f - 1.1f, -6.5f);
            var camGo = new GameObject("AuditCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(pivot - camPos));
            cam.fieldOfView = 60f; cam.aspect = 16f / 9f; cam.farClipPlane = 800f;
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);

            sb.AppendLine("=== A. What the player camera sees at spawn ===");
            sb.AppendLine($"player_start {startT.position}, camera {camPos}, car forward = {yaw * Vector3.forward}");

            var visible = new List<(Renderer r, float dist)>();
            foreach (var r in Object.FindObjectsByType<Renderer>())
            {
                if (!r.enabled || r.name == "AuditCam") continue;
                if (GeometryUtility.TestPlanesAABB(planes, r.bounds)) visible.Add((r, Vector3.Distance(camPos, r.bounds.center)));
            }

            sb.AppendLine($"visible renderers: {visible.Count}");
            sb.AppendLine("\n-- Marker visuals in view (these are the player-facing beacons) --");
            foreach (var m in Object.FindObjectsByType<LocationMarker>().Where(m => m.Kind == LocationKind.Pickup || m.Kind == LocationKind.Dropoff))
            {
                bool inView = m.GetComponentsInChildren<Renderer>().Any(r => GeometryUtility.TestPlanesAABB(planes, r.bounds));
                float d = Vector3.Distance(camPos, m.transform.position);
                var rends = m.GetComponentsInChildren<Renderer>().Where(r => r.name == "Beacon" || r.name == "Disc").ToList();
                string size = string.Join(", ", rends.Select(r => $"{r.name} {r.bounds.size.x:F1}x{r.bounds.size.y:F1}x{r.bounds.size.z:F1} m"));
                Color c = rends.Count > 0 ? ColorOf(rends[0].sharedMaterial) : Color.black;
                sb.AppendLine($"  {m.LocationId,-10} {m.Kind,-8} colour=({c.r:F2},{c.g:F2},{c.b:F2}) {Hue(c)} | {d:F0} m from camera | in view: {inView} | {size}");
            }

            sb.AppendLine("\n-- Green-dominant objects in view --");
            foreach (var g in visible.Where(v => IsGreen(ColorOf(v.r.sharedMaterial))).OrderBy(v => v.dist).Take(12))
                sb.AppendLine($"  {Path(g.r.transform)}  {g.dist:F0} m  size {g.r.bounds.size.x:F1}x{g.r.bounds.size.y:F1}x{g.r.bounds.size.z:F1}");

            sb.AppendLine("\n-- Largest/tallest thin objects in view --");
            foreach (var g in visible.Where(v => v.r.bounds.size.y > 4f && Mathf.Max(v.r.bounds.size.x, v.r.bounds.size.z) < 1.0f).Take(8))
                sb.AppendLine($"  {Path(g.r.transform)}  height {g.r.bounds.size.y:F1} m, width {g.r.bounds.size.x:F2} m  ({g.dist:F0} m)");

            // ---- 2. Debug visuals that are NOT geometry
            sb.AppendLine("\n=== B. Debug / editor-only drawing ===");
            sb.AppendLine("  LocationMarker.OnDrawGizmos: wire sphere (r=1.2 m, at 1 m height) + forward ray. Colours: Pickup green, Dropoff orange, Garage blue, PlayerStart white.");
            sb.AppendLine("  Gizmos draw in the Game view only when the Game view 'Gizmos' toggle is ON. WheelColliders also draw GREEN wheel circles + suspension lines under the car when Gizmos are ON.");
            sb.AppendLine($"  WheelColliders in prefab: {AssetDatabase.LoadAssetAtPath<GameObject>(VehicleBuilder.PrefabPath).GetComponentsInChildren<WheelCollider>().Length}");

            // ---- 3. Lighting / camera / post
            sb.AppendLine("\n=== C. Lighting, camera, post-processing ===");
            var sun = RenderSettings.sun;
            sb.AppendLine($"  sun: {(sun != null ? $"intensity {sun.intensity}, colour {sun.color}, rotation {sun.transform.eulerAngles}, shadows {sun.shadows}" : "none")}");
            sb.AppendLine($"  skybox: {(RenderSettings.skybox != null ? RenderSettings.skybox.name + " / " + RenderSettings.skybox.shader.name : "none")}, ambientMode {RenderSettings.ambientMode}, ambientIntensity {RenderSettings.ambientIntensity}");
            sb.AppendLine($"  fog: {RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogStartDistance}-{RenderSettings.fogEndDistance} colour {RenderSettings.fogColor}");
            var mainCam = Camera.allCameras.FirstOrDefault(c => c.name == "Main Camera") ?? Object.FindAnyObjectByType<Camera>();
            if (mainCam != null)
            {
                var data = mainCam.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "UniversalAdditionalCameraData");
                sb.AppendLine($"  main camera: fov {mainCam.fieldOfView}, near {mainCam.nearClipPlane}, far {mainCam.farClipPlane}, HDR {mainCam.allowHDR}, MSAA {mainCam.allowMSAA}");
                sb.AppendLine(data == null ? "  UniversalAdditionalCameraData: ABSENT -> post-processing OFF, anti-aliasing NONE (URP defaults)" : $"  UniversalAdditionalCameraData present: post-processing={new SerializedObject(data).FindProperty("m_RenderPostProcessing")?.boolValue}, AA={new SerializedObject(data).FindProperty("m_Antialiasing")?.intValue}");
            }

            int volumes = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                volumes += root.GetComponentsInChildren<Component>(true).Count(c => c != null && c.GetType().Name == "Volume");
            sb.AppendLine($"  scene Volume components: {volumes} (none = only the URP global default profile can apply, and only if the camera has post-processing enabled)");

            sb.AppendLine($"  Editor quality level now: {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            var rp = QualitySettings.renderPipeline;
            if (rp != null)
            {
                var so = new SerializedObject(rp);
                sb.AppendLine($"  active URP asset {rp.name}: renderScale {so.FindProperty("m_RenderScale")?.floatValue}, HDR {so.FindProperty("m_SupportsHDR")?.boolValue}, MSAA {so.FindProperty("m_MSAA")?.intValue}, shadowRes {so.FindProperty("m_MainLightShadowmapResolution")?.intValue}, shadowDist {so.FindProperty("m_ShadowDistance")?.floatValue}, cascades {so.FindProperty("m_ShadowCascadeCount")?.intValue}, softShadows {so.FindProperty("m_SoftShadowsSupported")?.boolValue}");
            }

            // ---- 4. Geometry / asset stats
            sb.AppendLine("\n=== D. Scene content ===");
            var all = Object.FindObjectsByType<MeshRenderer>();
            long tris = 0; var meshes = new HashSet<Mesh>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>()) if (mf.sharedMesh != null) { tris += mf.sharedMesh.triangles.Length / 3; meshes.Add(mf.sharedMesh); }
            sb.AppendLine($"  mesh renderers {all.Length}, unique meshes {meshes.Count}, total triangles {tris}, distinct materials {all.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count()}");
            var byPrefab = all.GroupBy(r => PrefabName(r.gameObject)).OrderByDescending(g => g.Count()).Take(10);
            foreach (var g in byPrefab) sb.AppendLine($"    {g.Key,-28} x{g.Count()}");
            sb.AppendLine($"  distinct building prefabs: {all.Select(r => PrefabName(r.gameObject)).Where(n => n.StartsWith("Building_")).Distinct().Count()} (x4 tint materials) over {all.Count(r => PrefabName(r.gameObject).StartsWith("Building_"))} placed buildings");
            foreach (string f in Directory.GetFiles("Assets/_Project/Art/Textures", "*.png"))
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(f.Replace('\\', '/'));
                sb.AppendLine($"  texture {System.IO.Path.GetFileName(f)}: {t.width}x{t.height}, normal map: no, tiles every 4 m");
            }

            Object.DestroyImmediate(camGo);
            string dir = System.IO.Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
            File.WriteAllText(System.IO.Path.Combine(dir, "visual_audit.txt"), sb.ToString());
            Debug.Log("[VisualAudit] written to Bridge/visual_audit.txt");
        }

        private static Color ColorOf(Material m)
        {
            if (m == null) return Color.magenta;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            return m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
        }

        private static bool IsGreen(Color c) => c.g > c.r * 1.25f && c.g > c.b * 1.25f && c.g > 0.25f;
        private static string Hue(Color c) => IsGreen(c) ? "GREEN" : (c.r > c.g && c.g > c.b ? "ORANGE/YELLOW" : "other");

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; i < 3 && t != null; i++, t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        private static string PrefabName(GameObject go)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root != null) return root.name;
            return src != null ? src.name : go.name;
        }
    }
}
