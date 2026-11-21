using System.Collections.Generic;
using System.IO;
using CityDriver.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Bakes the whole city into one low-poly 3D map model (Resources/Map3D/CityMapModel.prefab): ground, road ribbons by class from the road graph,
    /// and a box for every building read from the 97 streamed chunk scenes, grouped by height. Used by the in-game 3D map.
    /// </summary>
    internal static class MapModelBuilder
    {
        private const string GraphPath = "Assets/_Project/Data/World/CityRoadGraph.asset";
        private const string ChunkDir = "Assets/_Project/Scenes/CityChunks";
        private const string OutDir = "Assets/_Project/Resources/Map3D";
        private const string MeshDir = "Assets/_Project/Art/Map3D";

        private sealed class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<int> t = new List<int>(); public readonly List<Color32> col = new List<Color32>();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
                Color lin = PlayerSettings.colorSpace == ColorSpace.Linear ? color.linear : color;
                for (int k = 0; k < 4; k++) col.Add(lin);
            }
            public void Box(Vector3 min, Vector3 max, Color baseCol)
            {
                Vector3 p000 = new Vector3(min.x, min.y, min.z), p100 = new Vector3(max.x, min.y, min.z), p110 = new Vector3(max.x, max.y, min.z), p010 = new Vector3(min.x, max.y, min.z);
                Vector3 p001 = new Vector3(min.x, min.y, max.z), p101 = new Vector3(max.x, min.y, max.z), p111 = new Vector3(max.x, max.y, max.z), p011 = new Vector3(min.x, max.y, max.z);
                Color C(float k) => new Color(baseCol.r * k, baseCol.g * k, baseCol.b * k, 1f);
                Quad(p000, p010, p110, p100, C(0.80f)); Quad(p101, p111, p011, p001, C(0.64f)); Quad(p001, p011, p010, p000, C(0.58f)); Quad(p100, p110, p111, p101, C(0.92f)); Quad(p010, p011, p111, p110, C(1.05f));
            }
            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(v); m.SetTriangles(t, 0); m.SetColors(col); m.RecalculateNormals(); m.RecalculateBounds(); return m;
            }
        }

        private static Material VertexMaterial()
        {
            const string path = "Assets/_Project/Art/Materials/M_MapVertex.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var sh = Shader.Find("CityDriver/VertexColorUnlit");
                m = new Material(sh) { name = "M_MapVertex" };
                AssetDatabase.CreateAsset(m, path);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Color Jitter(Color c, Vector3 p)
        {
            float k = 0.9f + 0.2f * Mathf.Abs(Mathf.Sin(p.x * 12.9898f + p.z * 78.233f));
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }


        [BridgeCommand]
        public static void RenderPreview()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutDir}/CityMapModel.prefab");
            if (prefab == null) { Debug.LogError("[Map3D] No model."); return; }
            var shader = Shader.Find("CityDriver/VertexColorUnlit");
            string msgs = shader == null ? "shader not found" : $"supported={shader.isSupported}";
            if (shader != null) foreach (var m in ShaderUtil.GetShaderMessages(shader)) msgs += " | " + m.severity + ": " + m.message + " line " + m.line;
            foreach (Transform ch in prefab.transform)
            {
                var f = ch.GetComponent<MeshFilter>(); var rr = ch.GetComponent<MeshRenderer>();
                Debug.Log($"[Map3D] child {ch.name}: mesh={(f != null && f.sharedMesh != null ? f.sharedMesh.vertexCount + " verts, colors " + f.sharedMesh.colors32.Length + ", bounds " + f.sharedMesh.bounds.size : "MISSING")}, material={(rr != null && rr.sharedMaterial != null ? rr.sharedMaterial.name : "MISSING")}");
            }
            var tmp = new GameObject("MapPreviewTmp"); tmp.transform.position = new Vector3(0f, -6000f, 0f);
            Object.Instantiate(prefab, tmp.transform);
            var cam = new GameObject("Cam").AddComponent<Camera>(); cam.transform.SetParent(tmp.transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f); cam.fieldOfView = 38f; cam.nearClipPlane = 5f; cam.farClipPlane = 6000f;
            Quaternion rot = Quaternion.Euler(56f, 0f, 0f);
            cam.transform.localPosition = rot * new Vector3(0f, 0f, -900f); cam.transform.LookAt(tmp.transform.position);
            var rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32); rt.Create(); cam.targetTexture = rt; cam.Render();
            var tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0, false); tex.Apply(false); RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "map3d_preview.png"), tex.EncodeToPNG());
            cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(tmp);
        }

        [BridgeCommand]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Map3D] Exit Play Mode first."); return; }
            var graph = AssetDatabase.LoadAssetAtPath<RoadGraphAsset>(GraphPath);
            if (graph == null) { Debug.LogError("[Map3D] Road graph asset not found."); return; }
            var d = graph.data;
            CityAssetFactory.EnsureFolder(OutDir); CityAssetFactory.EnsureFolder(MeshDir);

            // ---- roads (flat ribbons) and junction patches
            var roads = new[] { new Builder(), new Builder(), new Builder() };        // local+collector, arterial, highway
            var nodeW = new float[d.nodes.Count];
            foreach (var e in d.edges)
            {
                Vector3 a = d.nodes[e.from].position, b = d.nodes[e.to].position; a.y = b.y = 0f;
                Vector3 dir = (b - a); if (dir.sqrMagnitude < 0.01f) continue; dir.Normalize(); Vector3 side = Vector3.Cross(Vector3.up, dir) * (e.width * 0.5f);
                int cls = e.roadClass == RoadClass.Highway ? 2 : e.roadClass == RoadClass.Arterial ? 1 : 0;
                float y = 0.30f + cls * 0.05f;
                roads[cls].Quad(a - side + Vector3.up * y, b - side + Vector3.up * y, b + side + Vector3.up * y, a + side + Vector3.up * y, cls == 2 ? new Color(1.0f, 0.70f, 0.25f) : cls == 1 ? new Color(0.86f, 0.87f, 0.92f) : new Color(0.47f, 0.49f, 0.55f));
                nodeW[e.from] = Mathf.Max(nodeW[e.from], e.width); nodeW[e.to] = Mathf.Max(nodeW[e.to], e.width);
            }
            for (int i = 0; i < d.nodes.Count; i++)
            {
                float w = nodeW[i] * 0.5f; if (w <= 0f) continue; Vector3 p = d.nodes[i].position; p.y = 0.34f;
                roads[0].Quad(p + new Vector3(-w, 0, -w), p + new Vector3(-w, 0, w), p + new Vector3(w, 0, w), p + new Vector3(w, 0, -w), new Color(0.47f, 0.49f, 0.55f));
            }

            // ---- buildings from the chunk scenes
            var bld = new[] { new Builder(), new Builder(), new Builder() };          // low, mid, tall
            var active = EditorSceneManager.GetActiveScene();
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { ChunkDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                foreach (var root in scene.GetRootGameObjects())
                {
                    var buildings = root.transform.Find("Buildings"); if (buildings == null) continue;
                    foreach (Transform b in buildings)
                    {
                        var rends = b.GetComponentsInChildren<Renderer>(true); if (rends.Length == 0) continue;
                        Bounds bb = rends[0].bounds; foreach (var r in rends) bb.Encapsulate(r.bounds);
                        if (bb.size.y < 3f || bb.size.x > 140f || bb.size.z > 140f) continue;
                        float h = bb.max.y;
                        int g = h < 10f ? 0 : h < 26f ? 1 : 2;
                        bld[g].Box(new Vector3(bb.min.x, 0f, bb.min.z), new Vector3(bb.max.x, h, bb.max.z), Jitter(g == 0 ? new Color(0.80f, 0.69f, 0.55f) : g == 1 ? new Color(0.58f, 0.66f, 0.82f) : new Color(0.38f, 0.70f, 0.98f), bb.center)); count++;
                    }
                }
                EditorSceneManager.CloseScene(scene, true);
            }
            if (active.IsValid()) SceneManager.SetActiveScene(active);

            // ---- assemble the prefab
            var rootGo = new GameObject("CityMapModel");
            void AddMesh(string name, Mesh mesh, Material mat)
            {
                string mp = $"{MeshDir}/{name}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
                if (existing != null) { EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); mesh = existing; }      // keeps the GUID; CityAssetFactory.SaveMesh would drop the vertex colours
                else AssetDatabase.CreateAsset(mesh, mp);
                AssetDatabase.SaveAssets();
                var g = new GameObject(name); g.transform.SetParent(rootGo.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            var vmat = VertexMaterial();
            var ground = new Builder(); float half = CityLayout.HalfSize + 400f;
            ground.Quad(new Vector3(-half, -0.2f, -half), new Vector3(-half, -0.2f, half), new Vector3(half, -0.2f, half), new Vector3(half, -0.2f, -half), new Color(0.09f, 0.11f, 0.15f));
            AddMesh("MapGround", ground.ToMesh("MapGround"), vmat);
            AddMesh("MapRoadsLocal", roads[0].ToMesh("MapRoadsLocal"), vmat);
            AddMesh("MapRoadsArterial", roads[1].ToMesh("MapRoadsArterial"), vmat);
            AddMesh("MapRoadsHighway", roads[2].ToMesh("MapRoadsHighway"), vmat);
            AddMesh("MapBuildingsLow", bld[0].ToMesh("MapBuildingsLow"), vmat);
            AddMesh("MapBuildingsMid", bld[1].ToMesh("MapBuildingsMid"), vmat);
            AddMesh("MapBuildingsTall", bld[2].ToMesh("MapBuildingsTall"), vmat);
            string prefabPath = $"{OutDir}/CityMapModel.prefab";
            AssetDatabase.DeleteAsset(prefabPath);
            var saved = PrefabUtility.SaveAsPrefabAssetAndConnect(rootGo, prefabPath, InteractionMode.AutomatedAction, out bool ok);
            Debug.Log($"[Map3D] prefab save success={ok} asset={(saved != null ? saved.name : "null")}");
            Object.DestroyImmediate(rootGo);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log($"[Map3D] Baked the city map model: {d.edges.Count} roads, {count} buildings (low {bld[0].v.Count / 20}, mid {bld[1].v.Count / 20}, tall {bld[2].v.Count / 20}).");
        }
    }
}
