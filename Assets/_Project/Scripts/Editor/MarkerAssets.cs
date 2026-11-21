using System.Collections.Generic;
using CityDriver.Missions;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Builds the player-facing waypoint prefabs (PickupMarker / DropoffMarker). Paths are fixed, so rebuilding updates the
    /// existing prefab in place and every marker in the scene picks up the new look without moving.
    /// </summary>
    internal static class MarkerAssets
    {
        public static readonly Color PickupColor = new Color(0.30f, 0.78f, 1.00f);   // soft sky blue
        public static readonly Color DropoffColor = new Color(1.00f, 0.72f, 0.18f);  // warm amber

        private const float Hover = 2.3f;

        public static GameObject Build(string prefabName, Color color)
        {
            string key = prefabName.StartsWith("Pickup") ? "Pickup" : "Dropoff";
            Material mat = CityAssetFactory.Unlit($"M_Waypoint{key}", color);
            Mesh gemMesh = CityAssetFactory.SaveMesh(BuildGemMesh(), $"{CityAssetFactory.MeshesDir}/Mesh_WaypointGem.asset");
            Mesh ringMesh = CityAssetFactory.SaveMesh(BuildRingMesh(0.95f, 1.15f, 48), $"{CityAssetFactory.MeshesDir}/Mesh_WaypointRing.asset");

            var root = new GameObject(prefabName);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            // Gem: small elongated diamond that floats above the stop point.
            var gem = new GameObject("Gem");
            gem.transform.SetParent(visual, false);
            gem.transform.localPosition = new Vector3(0, Hover, 0);
            gem.AddComponent<MeshFilter>().sharedMesh = gemMesh;
            gem.AddComponent<MeshRenderer>().sharedMaterial = mat;

            // Stem: hairline from the ground up to the gem.
            float stemHeight = Hover - 0.45f;
            var stem = CityAssetFactory.Prim(PrimitiveType.Cylinder, "Stem", visual, new Vector3(0, stemHeight / 2f, 0), new Vector3(0.06f, stemHeight / 2f, 0.06f), mat);

            // Ring: slim 2 m wide circle on the ground marking where to stop.
            var ring = new GameObject("Ring");
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0, 0.04f, 0);
            ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            ring.AddComponent<MeshRenderer>().sharedMaterial = mat;

            // Distance label (TextMesh = no canvas cost).
            var labelGo = new GameObject("DistanceLabel");
            labelGo.transform.SetParent(visual, false);
            labelGo.transform.localPosition = new Vector3(0, Hover + 0.85f, 0);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var text = labelGo.AddComponent<TextMesh>();
            text.font = font; text.fontSize = 96; text.characterSize = 0.075f; text.text = "0 m";
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
            text.color = new Color(1f, 1f, 1f, 0.95f);
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            var indicator = root.AddComponent<WaypointIndicator>();
            var so = new SerializedObject(indicator);
            so.FindProperty("visualRoot").objectReferenceValue = visual;
            so.FindProperty("gem").objectReferenceValue = gem.transform;
            so.FindProperty("ring").objectReferenceValue = ring;
            so.FindProperty("stem").objectReferenceValue = stem;
            so.FindProperty("label").objectReferenceValue = text;
            so.FindProperty("hoverHeight").floatValue = Hover;
            so.ApplyModifiedPropertiesWithoutUndo();

            string path = $"{CityAssetFactory.EnvPrefabsDir}/{prefabName}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // Elongated octahedron, flat-shaded, faces wound clockwise from outside.
        private static Mesh BuildGemMesh()
        {
            Vector3 top = new Vector3(0, 0.40f, 0), bottom = new Vector3(0, -0.40f, 0);
            float r = 0.24f;
            Vector3[] eq = { new Vector3(r, 0, 0), new Vector3(0, 0, r), new Vector3(-r, 0, 0), new Vector3(0, 0, -r) };

            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 centroid = (a + b + c) / 3f;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), centroid) < 0f) { var tmp = b; b = c; c = tmp; }
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                int s = v.Count;
                v.Add(a); v.Add(b); v.Add(c); n.Add(normal); n.Add(normal); n.Add(normal);
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
            }

            for (int i = 0; i < 4; i++)
            {
                Face(top, eq[i], eq[(i + 1) % 4]);
                Face(bottom, eq[(i + 1) % 4], eq[i]);
            }

            var mesh = new Mesh { name = "Mesh_WaypointGem" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
            mesh.SetUVs(0, new Vector2[v.Count]);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Flat annulus facing up.
        private static Mesh BuildRingMesh(float inner, float outer, int segments)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.PI * 2f * i / segments, a1 = Mathf.PI * 2f * (i + 1) / segments;
                Vector3 o0 = new Vector3(Mathf.Cos(a0) * outer, 0, Mathf.Sin(a0) * outer), o1 = new Vector3(Mathf.Cos(a1) * outer, 0, Mathf.Sin(a1) * outer);
                Vector3 i0 = new Vector3(Mathf.Cos(a0) * inner, 0, Mathf.Sin(a0) * inner), i1 = new Vector3(Mathf.Cos(a1) * inner, 0, Mathf.Sin(a1) * inner);
                Vector3[] quad = { o0, o1, i1, i0 };
                if (Vector3.Cross(quad[1] - quad[0], quad[2] - quad[0]).y < 0f) System.Array.Reverse(quad); // ensure it faces up
                int s = v.Count;
                v.AddRange(quad);
                for (int k = 0; k < 4; k++) n.Add(Vector3.up);
                t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }

            var mesh = new Mesh { name = "Mesh_WaypointRing" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
            mesh.SetUVs(0, new Vector2[v.Count]);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
