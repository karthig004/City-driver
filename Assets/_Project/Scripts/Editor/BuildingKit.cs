using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    internal sealed class BuildingInfo
    {
        public string archetype;
        public GameObject prefab;
        public float width, depth, height;     // footprint in prefab space (front faces -Z)
        public int triangles;
    }

    /// <summary>
    /// Procedural modular building kit. Every archetype is built from facade volumes (real wall photographs with windows,
    /// curtain glass, shopfronts) plus detail modules: balconies, AC units, rooftop water tanks, parapets, awnings, signs,
    /// gable roofs, chimneys, containers. Each prefab has two LODs (detailed, plain box) and a box collider.
    /// Floor height is 3.5 m so the 4 x 3.5 m facade tile lines up exactly.
    /// </summary>
    internal static class BuildingKit
    {
        public const string PrefabDir = "Assets/_Project/Prefabs/Buildings";
        private const float FloorH = 3.5f;

        // material slots
        private const int Facade = 0, Shop = 1, Trim = 2, Dark = 3, Roof = 4, Accent = 5, Extra = 6;

        public static readonly List<BuildingInfo> All = new List<BuildingInfo>();

        private static WorldMaterials mat;

        public static IReadOnlyList<BuildingInfo> Of(string archetype) => All.FindAll(b => b.archetype == archetype);

        public static void BuildAll(WorldMaterials materials)
        {
            mat = materials;
            All.Clear();
            CityAssetFactory.EnsureFolder(PrefabDir);
            CityAssetFactory.EnsureFolder(CityAssetFactory.MeshesDir + "/Buildings");

            string[] plasters = { "FacPlasterWarm", "FacPlasterCool", "FacPlasterPeach", "FacPlasterGreen", "FacPlasterWhite", "FacBrick" };
            string[] shops = { "FacShopA", "FacShopB", "FacShopC" };
            string[] awnings = { "Awning1", "Awning2", "Awning3" };

            // ---- residential / street-level
            int n = 0;
            foreach (var (w, d, fl) in new[] { (16f, 14f, 5), (20f, 16f, 6), (24f, 16f, 7), (18f, 14f, 8) })
            {
                int k = n++;
                Add("Apartment", $"Apartment_{w}x{d}x{fl}_{k}", w, d, fl, new[] { plasters[k % plasters.Length], shops[k % 3], "TrimLight", "MetalDark", "TrimLight", awnings[k % 3], "TankBlack" },
                    (b, r) => { Volume(b, w, d, fl, true); Balconies(b, w, d, fl, 4f, r, true); AcUnits(b, w, d, fl, r, 5 + fl); Parapet(b, w, d, fl); RoofTank(b, w, d, fl, r, 2); Awning(b, w, d); });
            }

            foreach (var (w, d, fl) in new[] { (8f, 14f, 3), (10f, 14f, 4), (12f, 14f, 3), (8f, 12f, 4) })
            {
                int k = n++;
                Add("Shophouse", $"Shophouse_{w}x{d}x{fl}_{k}", w, d, fl, new[] { plasters[(k + 2) % plasters.Length], shops[k % 3], "TrimLight", "MetalDark", "RoofTile", awnings[(k + 1) % 3], "TankBlack" },
                    (b, r) => { Volume(b, w, d, fl, true); Balconies(b, w, d, fl, 4f, r, false); AcUnits(b, w, d, fl, r, 3); Parapet(b, w, d, fl); RoofTank(b, w, d, fl, r, 1); Awning(b, w, d); });
            }

            foreach (var (w, d, tint) in new[] { (10f, 10f, 0), (12f, 10f, 1), (11f, 12f, 2), (9f, 9f, 4) })
            {
                int k = n++;
                Add("House", $"House_{w}x{d}_{k}", w, d, 2, new[] { plasters[tint], "FacShopA", "TrimLight", "MetalDark", "RoofTile", awnings[k % 3], "TankBlack" },
                    (b, r) => { Volume(b, w, d, 2, false); b.Submesh = Roof; b.AddGableRoof(new Vector3(0, 2 * FloorH, 0), w, d, 2.6f, w > d); b.Submesh = Trim; b.AddBox(new Vector3(0, 0.4f, -d / 2 - 0.9f), new Vector3(3f, 0.25f, 1.6f)); AcUnits(b, w, d, 2, r, 2); });
            }

            // ---- commercial / downtown
            foreach (var (w, d, fl, mt) in new[] { (20f, 20f, 10, "FacGlass"), (26f, 22f, 14, "FacGlassTeal"), (22f, 20f, 18, "FacGlass"), (24f, 24f, 12, "FacConcrete") })
            {
                int k = n++;
                Add("Office", $"Office_{w}x{d}x{fl}_{k}", w, d, fl, new[] { mt, "FacShopB", "TrimLight", "MetalDark", "TrimLight", "NeonCyan", "TankBlack" },
                    (b, r) => { Volume(b, w, d, fl, true); Crown(b, w, d, fl, r); Parapet(b, w, d, fl); });
            }

            foreach (var (w, d, fl, mt) in new[] { (18f, 18f, 24, "FacGlass"), (20f, 20f, 30, "FacGlassTeal"), (22f, 18f, 27, "FacGlass") })
            {
                int k = n++;
                Add("Tower", $"Tower_{w}x{d}x{fl}_{k}", w, d, fl, new[] { mt, "FacShopB", "TrimLight", "MetalDark", "TrimLight", "NeonCyan", "TankBlack" },
                    (b, r) => { Volume(b, w, d, fl, true); b.Submesh = Trim; for (int f = 8; f < fl; f += 8) b.AddBox(new Vector3(0, f * FloorH, 0), new Vector3(w + 0.5f, 0.5f, d + 0.5f)); Crown(b, w, d, fl, r); });
            }

            Add("Mall", "Mall_44x30x4", 44f, 30f, 4, new[] { "FacConcrete", "FacShopA", "TrimLight", "MetalDark", "TrimLight", "Awning3", "TankBlack" },
                (b, r) => { Volume(b, 44f, 30f, 4, true); b.Submesh = Shop; b.AddBox(new Vector3(0, 6f, -15.2f), new Vector3(30f, 7f, 0.6f)); b.Submesh = Accent; b.AddBox(new Vector3(0, 12.4f, -15.6f), new Vector3(22f, 2.2f, 0.4f)); Parapet(b, 44f, 30f, 4); RoofTank(b, 44f, 30f, 4, r, 3); });

            // ---- industrial
            foreach (var (w, d, h, rust) in new[] { (36f, 24f, 9f, false), (48f, 30f, 11f, true), (40f, 26f, 10f, false) })
            {
                int k = n++;
                Add("Warehouse", $"Warehouse_{w}x{d}_{k}", w, d, Mathf.RoundToInt(h / FloorH), new[] { rust ? "CorrugatedRust" : "Corrugated", "Corrugated", "TrimLight", "MetalDark", "Corrugated", "Awning3", "TankBlack" },
                    (b, r) => { b.Submesh = Facade; b.AddBoxNoBottom(new Vector3(0, h / 2, 0), new Vector3(w, h, d)); b.Submesh = Dark; for (int i = -1; i <= 1; i++) b.AddBox(new Vector3(i * w * 0.28f, 2.6f, -d / 2 - 0.08f), new Vector3(w * 0.2f, 5.2f, 0.2f)); b.Submesh = Trim; b.AddBox(new Vector3(0, h + 0.2f, 0), new Vector3(w + 0.4f, 0.4f, d + 0.4f)); for (int i = 0; i < 4; i++) { b.Submesh = Dark; b.AddCylinder(new Vector3(-w / 2 + (i + 1) * w / 5, h + 0.4f, r.Next(-4, 4)), 0.7f, 0.9f, 8); } });
            }

            foreach (var (w, d, h) in new[] { (30f, 26f, 11f), (36f, 28f, 12f) })
            {
                int k = n++;
                Add("Factory", $"Factory_{w}x{d}_{k}", w, d, Mathf.RoundToInt(h / FloorH), new[] { "Corrugated", "FacConcrete", "TrimLight", "MetalDark", "Corrugated", "Awning1", "TankBlack" },
                    (b, r) => { b.Submesh = Facade; b.AddBoxNoBottom(new Vector3(0, h / 2, 0), new Vector3(w, h, d)); b.Submesh = Shop; b.AddBoxNoBottom(new Vector3(-w / 2 - 5, 4f, 0), new Vector3(10f, 8f, d * 0.6f)); b.Submesh = Dark; b.AddCylinder(new Vector3(w / 2 - 4, h, 3), 1.3f, 14f, 12); b.AddCylinder(new Vector3(w / 2 - 9, h, 3), 1.0f, 10f, 12); b.AddCylinder(new Vector3(w / 2 + 3f, 0, -d / 4), 2.6f, 7f, 14); b.Submesh = Trim; b.AddBox(new Vector3(0, h + 0.2f, 0), new Vector3(w + 0.4f, 0.4f, d + 0.4f)); });
            }

            Add("ContainerYard", "ContainerYard_40x30", 40f, 30f, 1, new[] { "ContainerRed", "ContainerBlue", "ContainerGreen", "ContainerOrange", "ConcreteDark", "MetalDark", "TankBlack" },
                (b, r) =>
                {
                    for (int row = 0; row < 3; row++)
                        for (int col = 0; col < 5; col++)
                        {
                            int stack = r.Next(1, 4);
                            for (int s = 0; s < stack; s++) { b.Submesh = r.Next(0, 4); b.AddBoxNoBottom(new Vector3(-16 + col * 8f, 1.3f + s * 2.6f, -10 + row * 9f), new Vector3(6.2f, 2.5f, 2.5f)); }
                        }
                });

            // ---- special / landmark
            Add("Hospital", "Hospital_48x26x6", 48f, 26f, 6, new[] { "FacPlasterWhite", "FacShopB", "TrimLight", "MetalDark", "TrimLight", "RedCross", "TankBlack" },
                (b, r) => { Volume(b, 48f, 26f, 6, true); Parapet(b, 48f, 26f, 6); b.Submesh = Accent; b.AddBox(new Vector3(0, 17f, -13.2f), new Vector3(1.6f, 5f, 0.3f)); b.AddBox(new Vector3(0, 17f, -13.2f), new Vector3(5f, 1.6f, 0.3f)); b.Submesh = Trim; b.AddBox(new Vector3(0, 4.2f, -15f), new Vector3(10f, 0.4f, 4.6f)); RoofTank(b, 48f, 26f, 6, r, 2); AcUnits(b, 48f, 26f, 6, r, 14); });
            Add("Police", "Police_30x20x3", 30f, 20f, 3, new[] { "FacConcrete", "FacShopB", "TrimLight", "MetalDark", "TrimLight", "PoliceBlue", "TankBlack" },
                (b, r) => { Volume(b, 30f, 20f, 3, true); Parapet(b, 30f, 20f, 3); b.Submesh = Accent; b.AddBox(new Vector3(0, 3.55f, -10.08f), new Vector3(30.2f, 0.7f, 0.2f)); b.AddBox(new Vector3(0, 11.6f, -10.2f), new Vector3(12f, 1.4f, 0.3f)); b.Submesh = Dark; b.AddCylinder(new Vector3(12f, 10.7f, -6f), 0.08f, 7f, 6); });
            Add("Nightclub", "Nightclub_24x18x2", 24f, 18f, 2, new[] { "FacConcrete", "FacShopB", "TrimLight", "MetalDark", "TrimLight", "NeonMagenta", "NeonCyan" },
                (b, r) => { Volume(b, 24f, 18f, 2, true); b.Submesh = Accent; b.AddBox(new Vector3(0, 7.4f, -9.2f), new Vector3(20f, 0.35f, 0.3f)); b.AddBox(new Vector3(0, 3.8f, -9.2f), new Vector3(22f, 0.3f, 0.3f)); b.Submesh = Extra; b.AddBox(new Vector3(-12.1f, 4.5f, 0), new Vector3(0.3f, 0.3f, 14f)); b.AddBox(new Vector3(12.1f, 4.5f, 0), new Vector3(0.3f, 0.3f, 14f)); Parapet(b, 24f, 18f, 2); });
            Add("Cinema", "Cinema_30x24x3", 30f, 24f, 3, new[] { "FacConcrete", "FacShopC", "TrimLight", "MetalDark", "TrimLight", "NeonMagenta", "Awning1" },
                (b, r) => { Volume(b, 30f, 24f, 3, true); Parapet(b, 30f, 24f, 3); b.Submesh = Extra; b.AddBox(new Vector3(0, 4.4f, -13.3f), new Vector3(16f, 1.0f, 3.0f)); b.Submesh = Accent; b.AddBox(new Vector3(-12f, 7f, -12.6f), new Vector3(1.6f, 9f, 0.5f)); });
            Add("Hotel", "Hotel_30x20x9", 30f, 20f, 9, new[] { "FacPlasterWhite", "FacShopB", "TrimLight", "MetalDark", "TrimLight", "NeonCyan", "TankBlack" },
                (b, r) => { Volume(b, 30f, 20f, 9, true); Balconies(b, 30f, 20f, 9, 4f, r, true); Parapet(b, 30f, 20f, 9); b.Submesh = Accent; b.AddBox(new Vector3(0, 9 * FloorH + 1.2f, -10f), new Vector3(14f, 1.6f, 0.4f)); RoofTank(b, 30f, 20f, 9, r, 2); });
            Add("Luxury", "Luxury_24x24x15", 24f, 24f, 15, new[] { "FacGlassTeal", "FacShopB", "Gold", "MetalDark", "TrimLight", "Gold", "TankBlack" },
                (b, r) => { Volume(b, 24f, 24f, 11, true); b.Submesh = Facade; b.AddBoxNoBottom(new Vector3(0, 11 * FloorH + 2 * FloorH, 0), new Vector3(18f, 4 * FloorH, 18f)); b.Submesh = Trim; for (int f = 4; f <= 11; f += 4) b.AddBox(new Vector3(0, f * FloorH, 0), new Vector3(24.6f, 0.45f, 24.6f)); b.AddBox(new Vector3(0, 15 * FloorH + 0.3f, 0), new Vector3(18.6f, 0.6f, 18.6f)); });
            Add("RepairShop", "RepairShop_20x16", 20f, 16f, 2, new[] { "FacConcrete", "FacShopA", "TrimLight", "MetalDark", "TrimLight", "Awning2", "TankBlack" },
                (b, r) => { Volume(b, 20f, 16f, 2, false); b.Submesh = Dark; for (int i = -1; i <= 1; i++) b.AddBox(new Vector3(i * 6f, 2f, -8.06f), new Vector3(4.6f, 4f, 0.2f)); b.Submesh = Accent; b.AddBox(new Vector3(0, 5.6f, -8.2f), new Vector3(14f, 1.2f, 0.3f)); Parapet(b, 20f, 16f, 2); AcUnits(b, 20f, 16f, 2, r, 2); });

            AssetDatabase.SaveAssets();
        }

        // ================================================================================== prefab assembly

        private static void Add(string archetype, string name, float w, float d, int floors, string[] materialNames, Action<CityMeshBuilder, System.Random> build)
        {
            var rnd = new System.Random(name.GetHashCode());
            var b = new CityMeshBuilder(4f, FloorH) { UvOrigin = new Vector3(-w / 2f, 0f, -d / 2f), UniformTopUv = true };
            build(b, rnd);
            Mesh lod0 = CityAssetFactory.SaveMesh(b.ToMesh("Bld_" + name), $"{CityAssetFactory.MeshesDir}/Buildings/{name}.asset");

            float h = lod0.bounds.max.y;
            var box = new CityMeshBuilder(4f, FloorH) { UvOrigin = new Vector3(-w / 2f, 0f, -d / 2f), UniformTopUv = true };
            box.AddBoxNoBottom(new Vector3(0, h / 2f, 0), new Vector3(w, h, d));
            Mesh lod1 = CityAssetFactory.SaveMesh(box.ToMesh("Bld_" + name + "_LOD1"), $"{CityAssetFactory.MeshesDir}/Buildings/{name}_LOD1.asset");

            var mats = new Material[lod0.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat[materialNames[Mathf.Min(i, materialNames.Length - 1)]];

            var root = new GameObject(name);
            GameObject g0 = Child(root, "LOD0", lod0, mats);
            GameObject g1 = Child(root, "LOD1", lod1, new[] { mats[0] });
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[]
            {
                new LOD(0.12f, new[] { g0.GetComponent<Renderer>() }),
                new LOD(0.015f, new[] { g1.GetComponent<Renderer>() }),
            });
            lod.RecalculateBounds();
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, h / 2f, 0); col.size = new Vector3(w, h, d);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            All.Add(new BuildingInfo { archetype = archetype, prefab = prefab, width = w, depth = d, height = h, triangles = lod0.triangles.Length / 3 });
        }

        private static GameObject Child(GameObject parent, string name, Mesh mesh, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        // ================================================================================== modules

        /// <summary>Main volume: ground floor (shopfront slot when shop=true) plus the upper floors.</summary>
        private static void Volume(CityMeshBuilder b, float w, float d, int floors, bool shop)
        {
            b.Submesh = shop ? Shop : Facade;
            b.AddBoxNoBottom(new Vector3(0, FloorH / 2f, 0), new Vector3(w, FloorH, d));
            if (floors > 1)
            {
                b.Submesh = Facade;
                b.AddBoxNoBottom(new Vector3(0, FloorH + (floors - 1) * FloorH / 2f, 0), new Vector3(w, (floors - 1) * FloorH, d));
            }
        }

        private static void Balconies(CityMeshBuilder b, float w, float d, int floors, float spacing, System.Random r, bool both)
        {
            for (int f = 1; f < floors; f++)
                for (float x = -w / 2f + spacing / 2f; x < w / 2f; x += spacing)
                {
                    if (r.NextDouble() < 0.25) continue;
                    foreach (int side in both ? new[] { -1, 1 } : new[] { -1 })
                    {
                        b.Submesh = Trim; b.AddBox(new Vector3(x, f * FloorH + 0.09f, side * (d / 2f + 0.62f)), new Vector3(2.6f, 0.18f, 1.24f));
                        b.Submesh = Dark; b.AddBox(new Vector3(x, f * FloorH + 0.72f, side * (d / 2f + 1.2f)), new Vector3(2.6f, 1.0f, 0.06f));
                    }
                }
        }

        private static void AcUnits(CityMeshBuilder b, float w, float d, int floors, System.Random r, int count)
        {
            b.Submesh = Dark;
            for (int i = 0; i < count; i++)
            {
                int f = r.Next(1, Mathf.Max(2, floors)); float x = (float)(r.NextDouble() - 0.5) * (w - 3f);
                b.AddBox(new Vector3(x, f * FloorH + 2.3f, -d / 2f - 0.3f), new Vector3(0.9f, 0.55f, 0.6f));
            }
        }

        private static void Parapet(CityMeshBuilder b, float w, float d, int floors)
        {
            b.Submesh = Trim; float y = floors * FloorH + 0.45f;
            b.AddBox(new Vector3(0, y, -d / 2f + 0.1f), new Vector3(w, 0.9f, 0.2f)); b.AddBox(new Vector3(0, y, d / 2f - 0.1f), new Vector3(w, 0.9f, 0.2f));
            b.AddBox(new Vector3(-w / 2f + 0.1f, y, 0), new Vector3(0.2f, 0.9f, d)); b.AddBox(new Vector3(w / 2f - 0.1f, y, 0), new Vector3(0.2f, 0.9f, d));
        }

        private static void RoofTank(CityMeshBuilder b, float w, float d, int floors, System.Random r, int count)
        {
            float y = floors * FloorH + 0.9f;
            for (int i = 0; i < count; i++)
            {
                float x = (float)(r.NextDouble() - 0.5) * (w - 5f), z = (float)(r.NextDouble() - 0.5) * (d - 5f);
                b.Submesh = Extra; b.AddCylinder(new Vector3(x, y + 0.5f, z), 1.0f, 1.7f, 10);
                b.Submesh = Dark; b.AddBox(new Vector3(x, y + 0.25f, z), new Vector3(1.6f, 0.5f, 1.6f));
            }

            b.Submesh = Facade; b.AddBoxNoBottom(new Vector3(w / 2f - 3f, y + 1.1f, d / 2f - 3f), new Vector3(3f, 2.2f, 3f));
        }

        private static void Awning(CityMeshBuilder b, float w, float d)
        {
            b.Submesh = Accent; b.AddBox(new Vector3(0, 3.1f, -d / 2f - 0.7f), new Vector3(w * 0.9f, 0.14f, 1.4f));
        }

        private static void Crown(CityMeshBuilder b, float w, float d, int floors, System.Random r)
        {
            float y = floors * FloorH;
            b.Submesh = Trim; b.AddBox(new Vector3(0, y + 0.3f, 0), new Vector3(w + 0.4f, 0.6f, d + 0.4f));
            b.Submesh = Facade; b.AddBoxNoBottom(new Vector3(0, y + 1.9f, 0), new Vector3(w * 0.55f, 2.6f, d * 0.55f));
            b.Submesh = Dark; b.AddCylinder(new Vector3(w * 0.2f, y + 3.2f, 0), 0.06f, 5.5f, 6);
            b.Submesh = Dark; for (int i = 0; i < 3; i++) b.AddBox(new Vector3(-w * 0.3f + i * 2.2f, y + 1.0f, d * 0.3f), new Vector3(1.8f, 1.0f, 1.8f));
        }
    }
}
