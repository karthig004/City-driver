using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Everything the city assembler needs: materials and reusable prefab modules.</summary>
    internal sealed class CityLibrary
    {
        public readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        public GameObject Streetlight, Tree, SignStop, SignSpeed, Bench, GarageBuilding, PickupMarker, DropoffMarker, Passenger;
        public GameObject[] Buildings;            // Low, Wide, Mid, Tall
        public Material[] BuildingMaterials;      // four tints sharing one window texture
    }

    /// <summary>
    /// Creates placeholder textures, materials and prefabs with code. Every asset has a fixed path, so re-running
    /// updates it in place and keeps GUIDs (scene/prefab references survive). Replace any prefab later with a real
    /// model by dropping a new prefab at the same path or swapping it in the CityBuilder library.
    /// </summary>
    internal static class CityAssetFactory
    {
        public const string Root = "Assets/_Project";
        public const string MaterialsDir = Root + "/Art/Materials";
        public const string TexturesDir = Root + "/Art/Textures";
        public const string MeshesDir = Root + "/Art/Environment/Meshes";
        public const string EnvPrefabsDir = Root + "/Prefabs/Environment";
        public const string CharPrefabsDir = Root + "/Prefabs/Characters";

        public static CityLibrary CreateAll()
        {
            EnsureFolder(MaterialsDir); EnsureFolder(TexturesDir); EnsureFolder(MeshesDir);
            EnsureFolder(EnvPrefabsDir); EnsureFolder(CharPrefabsDir);

            lowSphere = null;
            var lib = new CityLibrary();
            CreateMaterials(lib);
            CreatePrefabs(lib);
            AssetDatabase.SaveAssets();
            return lib;
        }

        // ================================================================================== textures

        private static Texture2D MakeTexture(string name, int size, Func<int, int, Color> pixel)
        {
            string assetPath = $"{TexturesDir}/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            var colors = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    colors[y * size + x] = pixel(x, y);
            tex.SetPixels(colors);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        // Tile-safe noise: pure per-pixel hash + periodic low-frequency wobble (no Perlin, so no seams).
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        private static float Wobble(int x, int y, int size)
        {
            float u = x / (float)size * Mathf.PI * 2f, v = y / (float)size * Mathf.PI * 2f;
            return Mathf.Sin(u * 3f + 1f) * Mathf.Cos(v * 2f) * 0.5f + Mathf.Sin(v * 5f) * 0.25f;
        }

        // ================================================================================== materials

        private static void CreateMaterials(CityLibrary lib)
        {
            var asphalt = MakeTexture("T_Asphalt", 128, (x, y) =>
            {
                float n = 0.19f + (Hash(x, y, 1) - 0.5f) * 0.07f + Wobble(x, y, 128) * 0.02f;
                if (Hash(x, y, 2) > 0.985f) n += 0.12f; // light aggregate speckles
                return new Color(n, n, n * 1.03f, 1f);
            });
            var concrete = MakeTexture("T_Concrete", 128, (x, y) =>
            {
                float n = 0.64f + (Hash(x, y, 3) - 0.5f) * 0.06f + Wobble(x, y, 128) * 0.025f;
                bool joint = x < 2 || y < 2; // expansion joint on the 4 m tile border
                if (joint) n *= 0.7f;
                return new Color(n, n, n * 0.98f, 1f);
            });
            var grass = MakeTexture("T_Grass", 128, (x, y) =>
            {
                float n = (Hash(x, y, 4) - 0.5f) * 0.12f + Wobble(x, y, 128) * 0.05f;
                return new Color(0.20f + n, 0.42f + n, 0.16f + n * 0.5f, 1f);
            });
            var window = MakeTexture("T_Window", 256, (x, y) =>
            {
                float fx = x / 256f, fy = y / 256f; // one 4 m x 3.5 m floor
                float noise = (Hash(x, y, 5) - 0.5f) * 0.03f;
                Color wall = new Color(0.82f + noise, 0.80f + noise, 0.76f + noise, 1f);
                if (fy < 0.07f) return wall * 0.72f;                                   // floor slab band
                bool inWindow = fx > 0.20f && fx < 0.80f && fy > 0.28f && fy < 0.84f;
                if (!inWindow) return wall;
                bool frame = fx < 0.22f || fx > 0.78f || fy < 0.30f || fy > 0.82f || Mathf.Abs(fx - 0.5f) < 0.012f;
                if (frame) return new Color(0.55f, 0.57f, 0.60f, 1f);
                float g = Mathf.InverseLerp(0.30f, 0.82f, fy);                         // sky reflection gradient
                return Color.Lerp(new Color(0.09f, 0.14f, 0.21f), new Color(0.30f, 0.42f, 0.56f), g);
            });

            Add(lib, "Asphalt", Lit("M_Asphalt", Color.white, asphalt, 0.12f));
            Add(lib, "Concrete", Lit("M_Concrete", Color.white, concrete, 0.08f));
            Add(lib, "ConcreteDark", Lit("M_ConcreteDark", new Color(0.62f, 0.63f, 0.66f), concrete, 0.08f));
            Add(lib, "Grass", Lit("M_Grass", Color.white, grass, 0.03f));
            Add(lib, "PaintWhite", Lit("M_PaintWhite", new Color(0.92f, 0.92f, 0.9f), null, 0.25f));
            Add(lib, "PaintYellow", Lit("M_PaintYellow", new Color(0.95f, 0.75f, 0.10f), null, 0.25f));

            lib.BuildingMaterials = new[]
            {
                Lit("M_Building1", new Color(1.00f, 0.95f, 0.88f), window, 0.25f),
                Lit("M_Building2", new Color(0.86f, 0.91f, 1.00f), window, 0.25f),
                Lit("M_Building3", new Color(0.95f, 0.84f, 0.78f), window, 0.25f),
                Lit("M_Building4", new Color(0.84f, 0.90f, 0.84f), window, 0.25f),
            };

            Add(lib, "GarageWall", Lit("M_GarageWall", new Color(0.80f, 0.86f, 0.95f), concrete, 0.1f));
            Add(lib, "GarageDoor", Lit("M_GarageDoor", new Color(0.11f, 0.12f, 0.14f), null, 0.3f));
            Add(lib, "GarageBanner", Lit("M_GarageBanner", new Color(0.10f, 0.40f, 0.85f), null, 0.3f, 0f, new Color(0.05f, 0.25f, 0.7f)));
            Add(lib, "Metal", Lit("M_Metal", new Color(0.22f, 0.23f, 0.26f), null, 0.45f, 0.4f));
            Add(lib, "LampGlow", Lit("M_LampGlow", new Color(1f, 0.92f, 0.65f), null, 0.2f, 0f, new Color(1.6f, 1.4f, 0.8f)));
            Add(lib, "Trunk", Lit("M_Trunk", new Color(0.35f, 0.24f, 0.15f), null, 0.05f));
            Add(lib, "Leaves", Lit("M_Leaves", new Color(0.18f, 0.44f, 0.17f), null, 0.08f));
            Add(lib, "Leaves2", Lit("M_Leaves2", new Color(0.30f, 0.50f, 0.15f), null, 0.08f));
            Add(lib, "SignRed", Lit("M_SignRed", new Color(0.78f, 0.09f, 0.09f), null, 0.35f));
            Add(lib, "SignWhite", Lit("M_SignWhite", new Color(0.95f, 0.95f, 0.95f), null, 0.35f));
            Add(lib, "Wood", Lit("M_Wood", new Color(0.45f, 0.30f, 0.18f), null, 0.1f));
            Add(lib, "PassengerCloth", Lit("M_PassengerCloth", new Color(0.20f, 0.35f, 0.75f), null, 0.1f));
            Add(lib, "PassengerSkin", Lit("M_PassengerSkin", new Color(0.78f, 0.58f, 0.44f), null, 0.1f));

            // Procedural daylight sky.
            string skyPath = MaterialsDir + "/M_Skybox.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            var skyShader = Shader.Find("Skybox/Procedural");
            if (sky == null && skyShader != null) { sky = new Material(skyShader); AssetDatabase.CreateAsset(sky, skyPath); }
            if (sky != null) { sky.SetFloat("_SunSize", 0.04f); sky.SetFloat("_AtmosphereThickness", 1.0f); sky.SetFloat("_Exposure", 1.25f); sky.SetColor("_GroundColor", new Color(0.62f, 0.70f, 0.64f)); EditorUtility.SetDirty(sky); }
            Add(lib, "Skybox", sky);
        }

        private static void Add(CityLibrary lib, string key, Material m) { lib.Materials[key] = m; }

        internal static Material Lit(string name, Color color, Texture2D tex, float smoothness, float metallic = 0f, Color? emission = null)
        {
            Material m = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(m);
            return m;
        }

        internal static Material Unlit(string name, Color color)
        {
            Material m = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material LoadOrCreate(string name, string shaderName)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException($"Shader '{shaderName}' not found (is URP installed?).");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            return m;
        }

        // ================================================================================== prefabs

        private static void CreatePrefabs(CityLibrary lib)
        {
            var mat = lib.Materials;

            // Streetlight: 6 m pole, arm reaching along local +Z (towards the road), glowing lamp head.
            var lamp = new GameObject("Streetlight");
            Prim(PrimitiveType.Cylinder, "Pole", lamp.transform, new Vector3(0, 3f, 0), new Vector3(0.15f, 3f, 0.15f), mat["Metal"], true);
            Prim(PrimitiveType.Cube, "Arm", lamp.transform, new Vector3(0, 5.95f, 0.8f), new Vector3(0.1f, 0.1f, 1.7f), mat["Metal"]);
            Prim(PrimitiveType.Cube, "LampHead", lamp.transform, new Vector3(0, 5.88f, 1.55f), new Vector3(0.4f, 0.1f, 0.8f), mat["LampGlow"]);
            lib.Streetlight = SavePrefab(lamp, EnvPrefabsDir + "/Streetlight.prefab");

            // Tree: the core district shares the neem tree from the vegetation kit (no sphere canopies).
            lib.Tree = TreeKit.EnsureCorePrefab();

            // Stop sign (red disc + white bar) and speed-limit sign (white plate, red ring, white centre). Face local +Z.
            var stop = new GameObject("Sign_Stop");
            Prim(PrimitiveType.Cylinder, "Pole", stop.transform, new Vector3(0, 1.4f, 0), new Vector3(0.07f, 1.4f, 0.07f), mat["Metal"], true);
            Prim(PrimitiveType.Cylinder, "Plate", stop.transform, new Vector3(0, 2.45f, 0.06f), new Vector3(0.8f, 0.02f, 0.8f), mat["SignRed"], false, new Vector3(90, 0, 0));
            Prim(PrimitiveType.Cube, "Bar", stop.transform, new Vector3(0, 2.45f, 0.08f), new Vector3(0.55f, 0.12f, 0.02f), mat["SignWhite"]);
            lib.SignStop = SavePrefab(stop, EnvPrefabsDir + "/Sign_Stop.prefab");

            var speed = new GameObject("Sign_Speed");
            Prim(PrimitiveType.Cylinder, "Pole", speed.transform, new Vector3(0, 1.4f, 0), new Vector3(0.07f, 1.4f, 0.07f), mat["Metal"], true);
            Prim(PrimitiveType.Cube, "Plate", speed.transform, new Vector3(0, 2.45f, 0.06f), new Vector3(0.7f, 0.9f, 0.03f), mat["SignWhite"]);
            Prim(PrimitiveType.Cylinder, "Ring", speed.transform, new Vector3(0, 2.5f, 0.08f), new Vector3(0.55f, 0.01f, 0.55f), mat["SignRed"], false, new Vector3(90, 0, 0));
            Prim(PrimitiveType.Cylinder, "Centre", speed.transform, new Vector3(0, 2.5f, 0.09f), new Vector3(0.38f, 0.01f, 0.38f), mat["SignWhite"], false, new Vector3(90, 0, 0));
            lib.SignSpeed = SavePrefab(speed, EnvPrefabsDir + "/Sign_Speed.prefab");

            // Bench facing local +Z.
            var bench = new GameObject("Bench");
            Prim(PrimitiveType.Cube, "Seat", bench.transform, new Vector3(0, 0.45f, 0), new Vector3(1.7f, 0.08f, 0.5f), mat["Wood"], true);
            Prim(PrimitiveType.Cube, "Back", bench.transform, new Vector3(0, 0.78f, -0.22f), new Vector3(1.7f, 0.42f, 0.06f), mat["Wood"]);
            Prim(PrimitiveType.Cube, "LegL", bench.transform, new Vector3(-0.75f, 0.22f, 0), new Vector3(0.08f, 0.44f, 0.4f), mat["Metal"]);
            Prim(PrimitiveType.Cube, "LegR", bench.transform, new Vector3(0.75f, 0.22f, 0), new Vector3(0.08f, 0.44f, 0.4f), mat["Metal"]);
            lib.Bench = SavePrefab(bench, EnvPrefabsDir + "/Bench.prefab");

            // Buildings: footprint multiples of 4 m and heights of whole 3.5 m floors so the window texture aligns.
            lib.Buildings = new[]
            {
                MakeBuilding("Building_Low", 16f, 16f, 10.5f, lib.BuildingMaterials[0]),
                MakeBuilding("Building_Wide", 16f, 12f, 17.5f, lib.BuildingMaterials[1]),
                MakeBuilding("Building_Mid", 12f, 16f, 21f, lib.BuildingMaterials[2]),
                MakeBuilding("Building_Tall", 12f, 12f, 35f, lib.BuildingMaterials[3]),
            };

            // Garage: workshop with three dark bay doors and a blue banner on its local -Z face.
            var garage = new GameObject("GarageBuilding");
            var body = NewMeshObject("Body", garage.transform, "GarageBody", 24f, 7f, 18f, mat["GarageWall"]);
            var bodyCol = body.AddComponent<BoxCollider>(); bodyCol.center = new Vector3(0, 3.5f, 0); bodyCol.size = new Vector3(24f, 7f, 18f);
            for (int i = -1; i <= 1; i++)
                Prim(PrimitiveType.Cube, "BayDoor" + (i + 2), garage.transform, new Vector3(i * 7.5f, 2.25f, -9.05f), new Vector3(6.2f, 4.5f, 0.2f), mat["GarageDoor"]);
            Prim(PrimitiveType.Cube, "Banner", garage.transform, new Vector3(0, 5.9f, -9.1f), new Vector3(12f, 1.3f, 0.25f), mat["GarageBanner"]);
            Prim(PrimitiveType.Cube, "RoofSlab", garage.transform, new Vector3(0, 7.2f, 0), new Vector3(25f, 0.4f, 19f), mat["Metal"]);
            lib.GarageBuilding = SavePrefab(garage, EnvPrefabsDir + "/GarageBuilding.prefab");

            // Mission markers: flat disc + thin beacon pillar (opaque unlit = cheap on mobile).
            lib.PickupMarker = MarkerAssets.Build("PickupMarker", MarkerAssets.PickupColor);
            lib.DropoffMarker = MarkerAssets.Build("DropoffMarker", MarkerAssets.DropoffColor);

            // Passenger placeholder: capsule body + sphere head, ~1.9 m tall, pivot at feet.
            var p = new GameObject("Passenger_Placeholder");
            Prim(PrimitiveType.Capsule, "Body", p.transform, new Vector3(0, 0.85f, 0), new Vector3(0.55f, 0.85f, 0.55f), mat["PassengerCloth"]);
            Prim(PrimitiveType.Sphere, "Head", p.transform, new Vector3(0, 1.75f, 0), new Vector3(0.36f, 0.36f, 0.36f), mat["PassengerSkin"]);
            lib.Passenger = SavePrefab(p, CharPrefabsDir + "/Passenger_Placeholder.prefab");
        }

        private static GameObject MakeBuilding(string name, float w, float d, float h, Material m)
        {
            var root = new GameObject(name);
            var mesh = NewMeshObject("Mesh", root.transform, name, w, h, d, m);
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, h / 2f, 0);
            col.size = new Vector3(w, h, d);
            return SavePrefab(root, EnvPrefabsDir + "/" + name + ".prefab");
        }

        /// <summary>Box mesh with window-aligned meter UVs, pivot at the centre of its base.</summary>
        private static GameObject NewMeshObject(string objName, Transform parent, string meshName, float w, float h, float d, Material m)
        {
            var builder = new CityMeshBuilder(4f, 3.5f) { UvOrigin = new Vector3(-w / 2f, 0, -d / 2f), UniformTopUv = true };
            builder.AddBox(new Vector3(0, h / 2f, 0), new Vector3(w, h, d));
            Mesh mesh = SaveMesh(builder.ToMesh("Mesh_" + meshName), $"{MeshesDir}/Mesh_{meshName}.asset");

            var go = new GameObject(objName);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        // ================================================================================== helpers

        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material m, bool keepCollider = false, Vector3? euler = null)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = localScale;
            if (euler.HasValue) g.transform.localEulerAngles = euler.Value;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (type == PrimitiveType.Sphere) g.GetComponent<MeshFilter>().sharedMesh = LowSphere(); // ~80 tris instead of 760
            if (!keepCollider) UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        private static Mesh lowSphere;

        /// <summary>Low-poly UV sphere (diameter 1, 8 x 6 segments) used for tree canopies and heads.</summary>
        private static Mesh LowSphere()
        {
            if (lowSphere != null) return lowSphere;
            const int lon = 8, lat = 6;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i <= lat; i++)
            {
                float theta = Mathf.PI * i / lat;
                for (int j = 0; j <= lon; j++)
                {
                    float phi = Mathf.PI * 2f * j / lon;
                    var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    verts.Add(n * 0.5f); norms.Add(n); uv.Add(new Vector2(j / (float)lon, 1f - i / (float)lat));
                }
            }

            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j, b = a + lon + 1;
                    tris.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b }); // clockwise seen from outside
                }

            var mesh = new Mesh { name = "Mesh_LowSphere" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            lowSphere = SaveMesh(mesh, MeshesDir + "/Mesh_LowSphere.asset");
            return lowSphere;
        }

        public static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.SetVertices(mesh.vertices);
            existing.SetNormals(mesh.normals);
            existing.SetUVs(0, mesh.uv);
            existing.subMeshCount = mesh.subMeshCount;
            for (int sm = 0; sm < mesh.subMeshCount; sm++) existing.SetTriangles(mesh.GetTriangles(sm), sm);
            if (mesh.tangents != null && mesh.tangents.Length == mesh.vertexCount) existing.SetTangents(mesh.tangents);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        public static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }
    }
}
