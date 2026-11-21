using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    internal sealed class TreeSpecies
    {
        public string name;
        public Mesh mesh;                    // sub-mesh 0 = bark, 1 = leaves
        public Material bark, leaf;
        public float trunkRadius, height;
    }

    /// <summary>
    /// Procedural vegetation: tapered trunks and irregular canopies made of alpha-cut leaf cards (no spheres).
    /// Species suit an Indian city: neem (round shade tree), gulmohar (wide, red-orange flowers), coconut palm, ashoka (narrow).
    /// ~80 triangles each, so thousands can be merged per chunk cheaply.
    /// </summary>
    internal static class TreeKit
    {
        public const string TexDir = "Assets/_Project/Art/Textures/Vegetation";
        public static readonly List<TreeSpecies> Species = new List<TreeSpecies>();

        public static TreeSpecies Get(string name) => Species.Find(s => s.name == name);

        public static void BuildAll()
        {
            Species.Clear();
            CityAssetFactory.EnsureFolder(TexDir);
            Material bark = CityAssetFactory.Lit("M_TreeBark", new Color(0.30f, 0.22f, 0.16f), null, 0.08f);

            Texture2D neemTex = LeafTexture("T_LeafNeem_v2", new Color(0.20f, 0.46f, 0.16f), new Color(0.38f, 0.66f, 0.22f), Color.clear, 0.0f, false, 11);
            Texture2D gulTex = LeafTexture("T_LeafGulmohar_v2", new Color(0.24f, 0.50f, 0.17f), new Color(0.40f, 0.68f, 0.24f), new Color(0.92f, 0.22f, 0.08f), 0.10f, false, 12);
            Texture2D ashokaTex = LeafTexture("T_LeafAshoka_v2", new Color(0.16f, 0.40f, 0.14f), new Color(0.28f, 0.56f, 0.20f), Color.clear, 0.0f, false, 13);
            Texture2D palmTex = LeafTexture("T_LeafPalm_v2", new Color(0.24f, 0.50f, 0.14f), new Color(0.40f, 0.68f, 0.20f), Color.clear, 0.0f, true, 14);

            Make("Neem", bark, Leaf("M_LeafNeem", neemTex, Color.white), 0.26f, 3.2f, (b, r) => Canopy(b, r, 22, 1.8f, new Vector3(2.7f, 1.8f, 2.7f), 5.0f), 3.4f, 0.30f);
            Make("Gulmohar", bark, Leaf("M_LeafGulmohar", gulTex, Color.white), 0.30f, 3.0f, (b, r) => Canopy(b, r, 26, 1.9f, new Vector3(3.8f, 1.5f, 3.8f), 5.4f), 3.0f, 0.34f);
            Make("Ashoka", bark, Leaf("M_LeafAshoka", ashokaTex, Color.white), 0.20f, 2.0f, (b, r) => Canopy(b, r, 20, 1.3f, new Vector3(1.5f, 2.6f, 1.5f), 4.6f), 2.0f, 0.22f);
            Make("Palm", bark, Leaf("M_LeafPalm", palmTex, Color.white), 0.17f, 7.2f, Palm, 7.2f, 0.2f);
            AssetDatabase.SaveAssets();
        }

        /// <summary>(Re)writes the legacy Tree.prefab used by the original core district so it no longer shows sphere trees.</summary>
        public static GameObject EnsureCorePrefab()
        {
            if (Species.Count == 0) BuildAll();
            TreeSpecies s = Get("Neem");
            var root = new GameObject("Tree");
            var mf = root.AddComponent<MeshFilter>(); mf.sharedMesh = s.mesh;
            root.AddComponent<MeshRenderer>().sharedMaterials = new[] { s.bark, s.leaf };
            var cap = root.AddComponent<CapsuleCollider>(); cap.radius = 0.3f; cap.height = 3.2f; cap.center = new Vector3(0, 1.6f, 0);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, CityAssetFactory.EnvPrefabsDir + "/Tree.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ================================================================================== species construction

        private static void Make(string name, Material bark, Material leaf, float trunkR, float trunkH, System.Action<CityMeshBuilder, System.Random> foliage, float height, float colliderR)
        {
            var rnd = new System.Random(name.GetHashCode());
            var b = new CityMeshBuilder(1f, 1f);
            b.Submesh = 0;
            // Tapered trunk from two stacked cylinders, slightly offset like a real stem.
            b.AddCylinder(Vector3.zero, trunkR, trunkH * 0.55f, 7);
            b.AddCylinder(new Vector3(0.04f, trunkH * 0.55f, 0), trunkR * 0.78f, trunkH * 0.50f, 7);
            b.Submesh = 1;
            foliage(b, rnd);
            Mesh mesh = CityAssetFactory.SaveMesh(b.ToMesh("Tree_" + name), $"{CityAssetFactory.MeshesDir}/Tree_{name}.asset");
            Species.Add(new TreeSpecies { name = name, mesh = mesh, bark = bark, leaf = leaf, trunkRadius = colliderR, height = height });
        }

        // Cards scattered inside an ellipsoid, each facing a random direction and tilted: an irregular, bushy silhouette.
        private static void Canopy(CityMeshBuilder b, System.Random r, int cards, float size, Vector3 radii, float centreY)
        {
            for (int i = 0; i < cards; i++)
            {
                Vector3 dir = new Vector3((float)(r.NextDouble() * 2 - 1), (float)(r.NextDouble() * 2 - 1), (float)(r.NextDouble() * 2 - 1));
                if (dir.sqrMagnitude < 0.05f) dir = Vector3.up;
                Vector3 c = new Vector3(0, centreY, 0) + Vector3.Scale(dir.normalized, radii) * (0.45f + 0.55f * (float)r.NextDouble());
                Quaternion q = Quaternion.Euler((float)(r.NextDouble() * 70 - 35), (float)(r.NextDouble() * 360), (float)(r.NextDouble() * 50 - 25));
                float s = size * (0.8f + 0.5f * (float)r.NextDouble());
                Vector3 right = q * Vector3.right * s, up = q * Vector3.up * s;
                b.AddCard(c - right - up, c - right + up, c + right + up, c + right - up, new Rect(0, 0, 1, 1));
            }
        }

        private static void Palm(CityMeshBuilder b, System.Random r)
        {
            Vector3 top = new Vector3(0.1f, 6.9f, 0);
            for (int i = 0; i < 11; i++)
            {
                float yaw = i * (360f / 11f) + (float)r.NextDouble() * 12f;
                float droop = 25f + (float)r.NextDouble() * 35f;
                Quaternion q = Quaternion.Euler(droop, yaw, 0);
                Vector3 along = q * Vector3.forward, side = q * Vector3.right;
                float len = 3.4f + (float)r.NextDouble() * 0.8f, wid = 0.8f;
                Vector3 baseP = top, tip = top + along * len + Vector3.down * 0.7f;
                b.AddCard(baseP - side * wid * 0.25f, tip - side * wid, tip + side * wid, baseP + side * wid * 0.25f, new Rect(0, 0, 1, 1));
            }
        }

        // ================================================================================== textures / materials

        private static Material Leaf(string name, Texture2D tex, Color tint)
        {
            string path = $"{CityAssetFactory.MaterialsDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", 0.1f); m.SetFloat("_Metallic", 0f);
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.45f); m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);                                 // leaves are visible from both sides
            m.renderQueue = 2450;
            EditorUtility.SetDirty(m);
            return m;
        }

        // Alpha-cut leaf cluster: many small ellipses in two greens (plus optional flower dots), transparent elsewhere.
        private static Texture2D LeafTexture(string name, Color dark, Color light, Color flower, float flowerAmount, bool frond, int seed)
        {
            string path = $"{TexDir}/{name}.png";
            if (!File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path)))
            {
                const int S = 256;
                var px = new Color[S * S];
                var r = new System.Random(seed);
                if (frond)
                {
                    // Feather frond: central rib + angled leaflets along it.
                    for (int y = 0; y < S; y++)
                    {
                        float t = y / (float)(S - 1);
                        float half = (1f - Mathf.Abs(t - 0.38f) * 1.3f) * 0.46f * S;
                        for (int x = 0; x < S; x++)
                        {
                            float dx = Mathf.Abs(x - S / 2f);
                            bool rib = dx < 2.2f;
                            bool leaflet = dx < half && Mathf.Repeat(y * 0.55f + dx * 0.35f, 6f) < 3.6f;
                            if (rib || leaflet) px[y * S + x] = Color.Lerp(dark, light, (float)r.NextDouble() * 0.7f + t * 0.3f);
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < 520; i++)
                    {
                        float cx = (float)r.NextDouble() * S, cy = (float)r.NextDouble() * S;
                        float dc = Mathf.Sqrt((cx - S / 2f) * (cx - S / 2f) + (cy - S / 2f) * (cy - S / 2f));
                        if (dc > S * 0.5f) continue;                                           // keep the cluster roughly round
                        float rx = 5f + (float)r.NextDouble() * 7f, ry = rx * (0.45f + (float)r.NextDouble() * 0.3f), ang = (float)r.NextDouble() * Mathf.PI;
                        bool isFlower = flowerAmount > 0f && r.NextDouble() < flowerAmount;
                        Color c = isFlower ? Color.Lerp(flower, new Color(1f, 0.5f, 0.1f), (float)r.NextDouble() * 0.5f) : Color.Lerp(dark, light, (float)r.NextDouble());
                        float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                        int x0 = Mathf.Max(0, (int)(cx - rx - 1)), x1 = Mathf.Min(S - 1, (int)(cx + rx + 1)), y0 = Mathf.Max(0, (int)(cy - rx - 1)), y1 = Mathf.Min(S - 1, (int)(cy + rx + 1));
                        for (int y = y0; y <= y1; y++)
                            for (int x = x0; x <= x1; x++)
                            {
                                float ux = ((x - cx) * ca + (y - cy) * sa) / rx, uy = (-(x - cx) * sa + (y - cy) * ca) / ry;
                                if (ux * ux + uy * uy <= 1f) px[y * S + x] = c;
                            }
                    }
                }

                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false, false);
                tex.SetPixels(px); tex.Apply();
                File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp; imp.maxTextureSize = 256;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
