using System.Collections.Generic;
using CityDriver.Population;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Bakes the traffic, police and gang cars (body + details + wheels + lamps) into single meshes with 11 fixed material slots and saves Resources/TrafficCars/TrafficCarSet.prefab.</summary>
    internal static class TrafficCarBaker
    {
        private const string OutDir = "Assets/_Project/Resources/TrafficCars";
        private const string MeshDir = "Assets/_Project/Art/Vehicles/Baked";
        private const float WheelRadius = 0.33f, WheelWidth = 0.22f, TrackHalf = 0.84f, WheelBaseHalf = 1.36f;
        private static readonly string[] SlotNames = { "M_CarPaint", "M_CarGlass", "M_CarTrim", "M_CarChrome", "M_CarTyre", "M_CarRimDark", "M_CarHeadlight", "M_CarTaillight", "M_CarAccent", "M_CarBarRedOn", "M_CarBarBlueOff" };

        private static int SlotOf(Material m)
        {
            if (m == null) return -1;
            switch (m.name)
            {
                case "M_CarPaint": return 0;
                case "M_CarGlass": return 1;
                case "M_CarTrim": case "M_CarDisc": return 2;
                case "M_CarChrome": case "M_CarPlate": case "M_CarRim": return 3;
                case "M_CarTyre": return 4;
                case "M_CarRimDark": return 5;
                case "M_CarHeadlight": case "M_CarRoofSign": return 6;
                case "M_CarTaillight": return 7;
                case "M_CarAccent": return 8;
                case "M_CarBarRedOn": return 9;
                case "M_CarBarBlueOff": return 10;
                default: return -1;
            }
        }

        private static CarKit.Mats Mats()
        {
            return new CarKit.Mats
            {
                paint = CityAssetFactory.Lit("M_CarPaint", new Color(0.96f, 0.74f, 0.05f), null, 0.88f, 0.35f),
                glass = CityAssetFactory.Lit("M_CarGlass", new Color(0.05f, 0.07f, 0.10f), null, 0.9f, 0.1f),
                trim = CityAssetFactory.Lit("M_CarTrim", new Color(0.07f, 0.07f, 0.08f), null, 0.3f),
                tyre = CityAssetFactory.Lit("M_CarTyre", new Color(0.11f, 0.11f, 0.12f), null, 0.35f),
                rim = CityAssetFactory.Lit("M_CarRim", new Color(0.72f, 0.72f, 0.75f), null, 0.8f, 0.8f),
                rimDark = CityAssetFactory.Lit("M_CarRimDark", new Color(0.30f, 0.31f, 0.36f), null, 0.65f, 0.45f),
                disc = CityAssetFactory.Lit("M_CarDisc", new Color(0.52f, 0.52f, 0.55f), null, 0.5f, 0.5f),
                chrome = CityAssetFactory.Lit("M_CarChrome", new Color(0.86f, 0.87f, 0.91f), null, 0.8f, 0.6f),
                plate = CityAssetFactory.Lit("M_CarPlate", new Color(0.93f, 0.93f, 0.9f), null, 0.3f),
                head = CityAssetFactory.Lit("M_CarHeadlight", new Color(1f, 1f, 0.95f), null, 0.5f, 0f, new Color(1.4f, 1.4f, 1.3f)),
                tail = CityAssetFactory.Lit("M_CarTaillight", new Color(0.8f, 0.05f, 0.05f), null, 0.5f, 0f, new Color(0.35f, 0.02f, 0.02f)),
                accent = CityAssetFactory.Lit("M_CarAccent", new Color(0.05f, 0.16f, 0.52f), null, 0.7f, 0.3f),
                barA = CityAssetFactory.Lit("M_CarBarRedOn", new Color(0.9f, 0.05f, 0.05f), null, 0.5f, 0f, new Color(4f, 0.1f, 0.1f)),
                barB = CityAssetFactory.Lit("M_CarBarBlueOff", new Color(0.05f, 0.1f, 0.45f), null, 0.5f, 0f, new Color(0.02f, 0.04f, 0.2f)),
            };
        }

        private static Mesh Bake(CarShape shape, CarKit.Mats mats)
        {
            var root = new GameObject("BakeCar");
            var body = new GameObject("Body").transform; body.SetParent(root.transform, false);
            CarKit.AddBody(body, mats, shape);
            foreach (float z in new[] { WheelBaseHalf, -WheelBaseHalf })
                foreach (float x in new[] { -TrackHalf, TrackHalf })
                {
                    var pivot = new GameObject("W").transform; pivot.SetParent(root.transform, false); pivot.localPosition = new Vector3(x, WheelRadius, z);
                    CarKit.AddWheel(pivot, WheelRadius, WheelWidth, Mathf.Sign(x), mats);
                }

            var lists = new List<CombineInstance>[TrafficCarSet.Slots];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<CombineInstance>();
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!mr.gameObject.activeInHierarchy) continue;
                var mf = mr.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var ms = mr.sharedMaterials;
                for (int j = 0; j < ms.Length; j++)
                {
                    int slot = SlotOf(ms[j]); if (slot < 0 || j >= mf.sharedMesh.subMeshCount) continue;
                    lists[slot].Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = j, transform = root.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix });
                }
            }
            var perSlot = new CombineInstance[TrafficCarSet.Slots];
            var temp = new List<Mesh>();
            for (int i = 0; i < TrafficCarSet.Slots; i++)
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
                if (lists[i].Count > 0) m.CombineMeshes(lists[i].ToArray(), true, true);
                else { m.vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero }; m.triangles = new[] { 0, 1, 2 }; }
                temp.Add(m); perSlot[i] = new CombineInstance { mesh = m, transform = Matrix4x4.identity, subMeshIndex = 0 };
            }
            var final = new Mesh { name = "Baked_" + shape.id, indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            final.CombineMeshes(perSlot, false, false);
            final.RecalculateBounds();
            foreach (var t in temp) Object.DestroyImmediate(t);
            Object.DestroyImmediate(root);

            CityAssetFactory.EnsureFolder(MeshDir);
            string path = $"{MeshDir}/Baked_{shape.id}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(final, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(final); return existing; }
            AssetDatabase.CreateAsset(final, path); return final;
        }

        [BridgeCommand]
        public static void BakeAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Traffic] Exit Play Mode first."); return; }
            CityAssetFactory.EnsureFolder(OutDir);
            var mats = Mats();
            var go = new GameObject("TrafficCarSet"); var set = go.AddComponent<TrafficCarSet>();

            var shapes = new List<Mesh>();
            foreach (string id in new[] { "traffic_sedan", "hatch_city", "sedan_premium", "suv_performance" }) shapes.Add(Bake(CarShapes.Find(id), mats));
            set.traffic = shapes.ToArray();
            set.police = Bake(CarShapes.Find("police_sedan"), mats);
            set.policeSuv = Bake(CarShapes.Find("police_suv"), mats);
            set.gang = Bake(CarShapes.Find("gang_suv"), mats);

            var baseMats = new Material[TrafficCarSet.Slots];
            for (int i = 0; i < baseMats.Length; i++) baseMats[i] = AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Project/Art/Materials/{SlotNames[i]}.mat");
            set.baseMaterials = baseMats;
            string[] paintHex = { "#F2F2F0", "#1A1B1F", "#8B8F98", "#B3141A", "#1F4FA3", "#2E6B4A", "#C9A227", "#5B3A29" };
            var paints = new Material[paintHex.Length];
            for (int i = 0; i < paints.Length; i++) { ColorUtility.TryParseHtmlString(paintHex[i], out Color c); paints[i] = CityAssetFactory.Lit("M_TrafficPaint" + i, c, null, 0.85f, 0.35f); }
            set.paints = paints;
            set.barRedOn = CityAssetFactory.Lit("M_CarBarRedOn", new Color(0.9f, 0.05f, 0.05f), null, 0.5f, 0f, new Color(4f, 0.1f, 0.1f));
            set.barRedOff = CityAssetFactory.Lit("M_CarBarRedOff", new Color(0.35f, 0.02f, 0.02f), null, 0.5f, 0f, new Color(0.15f, 0.01f, 0.01f));
            set.barBlueOn = CityAssetFactory.Lit("M_CarBarBlueOn", new Color(0.1f, 0.2f, 0.9f), null, 0.5f, 0f, new Color(0.15f, 0.35f, 4f));
            set.barBlueOff = CityAssetFactory.Lit("M_CarBarBlueOff", new Color(0.05f, 0.1f, 0.45f), null, 0.5f, 0f, new Color(0.02f, 0.04f, 0.2f));

            // police and gang paint: white / black body baked into slot 0 materials via dedicated materials
            set.baseMaterials[0] = paints[0];
            AssetDatabase.SaveAssets();
            string prefabPath = $"{OutDir}/TrafficCarSet.prefab";
            AssetDatabase.DeleteAsset(prefabPath);
            PrefabUtility.SaveAsPrefabAssetAndConnect(go, prefabPath, InteractionMode.AutomatedAction, out bool ok);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log($"[Traffic] Baked {shapes.Count} civilian shapes + police sedan / SUV + gang SUV into TrafficCarSet (saved={ok}).");
        }
    }
}
