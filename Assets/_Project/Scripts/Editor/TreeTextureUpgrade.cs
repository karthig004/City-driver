using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Replaces the sparse ellipse-blob leaf textures with dense, layered leaf clusters (individual leaves with midrib, veins and
    /// shading, darker inside and lighter outside) and gives trunks a fissured bark texture. Written in place over the existing
    /// texture files, so the tree meshes and the streamed city chunks do not need rebuilding.
    /// </summary>
    public static class TreeTextureUpgrade
    {
        private const string Dir = "Assets/_Project/Art/Textures/Vegetation";

        [BridgeCommand]
        public static void Run()
        {
            Directory.CreateDirectory(Path.Combine(Directory.GetParent(Application.dataPath).FullName, Dir));
            Leaves("T_LeafNeem_v2", new Color(0.10f, 0.30f, 0.09f), new Color(0.30f, 0.58f, 0.16f), Color.clear, 0f, 11, 300);
            Leaves("T_LeafGulmohar_v2", new Color(0.12f, 0.33f, 0.10f), new Color(0.34f, 0.62f, 0.18f), new Color(0.93f, 0.20f, 0.08f), 0.07f, 12, 300);
            Leaves("T_LeafAshoka_v2", new Color(0.08f, 0.26f, 0.09f), new Color(0.22f, 0.50f, 0.16f), Color.clear, 0f, 13, 360);
            Frond("T_LeafPalm_v2", new Color(0.14f, 0.36f, 0.08f), new Color(0.40f, 0.64f, 0.16f), 14);
            Bark("T_Bark");
            AssetDatabase.SaveAssets();

            // trunk material gets the bark texture
            foreach (string g in AssetDatabase.FindAssets("M_TreeBark t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_Bark.png");
                if (m == null || tex == null) continue;
                m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(1f, 3f)); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.05f);
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
            File.AppendAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "env_assets.txt"), "tree textures upgraded\n");
        }

        private static float Hash(int a, int b, int seed) { unchecked { int h = a * 73856093 ^ b * 19349663 ^ seed * 83492791; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; } }

        private static void Leaves(string name, Color dark, Color light, Color flower, float flowerAmount, int seed, int count)
        {
            const int S = 512;
            var px = new Color[S * S];
            var rnd = new System.Random(seed);
            // paint back-to-front: inner/back leaves are darker, outer/front ones lighter, so the cluster has depth
            for (int i = 0; i < count; i++)
            {
                float depth = i / (float)count;
                float cx, cy;
                do { cx = (float)rnd.NextDouble() * S; cy = (float)rnd.NextDouble() * S; } while (Vector2.Distance(new Vector2(cx, cy), new Vector2(S / 2f, S / 2f)) > S * 0.47f);
                float len = 34f + (float)rnd.NextDouble() * 30f, wid = len * (0.34f + (float)rnd.NextDouble() * 0.14f), ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                bool isFlower = flowerAmount > 0f && rnd.NextDouble() < flowerAmount && depth > 0.5f;
                Color tint = Color.Lerp(dark, light, Mathf.Clamp01(depth * 0.9f + (float)rnd.NextDouble() * 0.25f));
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                int r = Mathf.CeilToInt(len);
                int x0 = Mathf.Max(0, (int)(cx - r)), x1 = Mathf.Min(S - 1, (int)(cx + r)), y0 = Mathf.Max(0, (int)(cy - r)), y1 = Mathf.Min(S - 1, (int)(cy + r));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x - cx, dy = y - cy;
                        float u = (dx * ca + dy * sa) / len + 0.5f;                  // along the leaf 0..1
                        float v = (-dx * sa + dy * ca) / (wid * 0.5f);               // across -1..1
                        if (u < 0f || u > 1f) continue;
                        float profile = Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.75f);
                        if (isFlower) profile = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((u - 0.5f) * 2f, 2f))) * 0.9f;
                        float edge = profile - Mathf.Abs(v);
                        if (edge <= 0f) continue;
                        Color c;
                        if (isFlower) c = Color.Lerp(flower, new Color(1f, 0.55f, 0.1f), Mathf.Abs(v));
                        else
                        {
                            c = tint * Mathf.Lerp(0.78f, 1.08f, 1f - Mathf.Abs(v));            // darker towards the rim
                            if (Mathf.Abs(v) < 0.09f) c = Color.Lerp(c, new Color(0.7f, 0.8f, 0.4f), 0.55f);             // midrib
                            float vein = Mathf.Abs(Mathf.Sin((u * 9f - Mathf.Abs(v) * 2.2f) * Mathf.PI));
                            if (vein < 0.12f && Mathf.Abs(v) > 0.1f) c = Color.Lerp(c, light * 1.12f, 0.25f);          // side veins
                            c *= 0.92f + 0.16f * Hash(x, y, seed);
                        }
                        c.a = Mathf.Clamp01(edge * 9f);
                        int idx = y * S + x;
                        float oa = px[idx].a;
                        px[idx] = oa <= 0f ? c : Color.Lerp(px[idx], c, c.a); px[idx].a = Mathf.Max(oa, c.a);
                    }
            }
            Write(name, px, S);
        }

        private static void Frond(string name, Color dark, Color light, int seed)
        {
            const int S = 512;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                float t = y / (float)(S - 1);
                for (int x = 0; x < S; x++)
                {
                    float dx = Mathf.Abs(x - S / 2f);
                    float reach = (1f - Mathf.Abs(t - 0.4f) * 1.25f) * 0.47f * S;
                    float leaflet = Mathf.Repeat(y * 0.42f + dx * 0.55f, 7f);
                    bool rib = dx < 3f;
                    bool blade = dx < reach && leaflet < 4.6f;
                    if (!rib && !blade) continue;
                    Color c = Color.Lerp(dark, light, Mathf.Clamp01(t * 0.6f + dx / S + 0.25f * Hash(x / 3, y / 3, seed)));
                    if (rib) c = Color.Lerp(light, new Color(0.7f, 0.75f, 0.35f), 0.4f);
                    c.a = rib ? 1f : Mathf.Clamp01((4.6f - leaflet) * 1.6f);
                    px[y * S + x] = c;
                }
            }
            Write(name, px, S);
        }

        private static void Bark(string name)
        {
            const int S = 256;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    // periodic in x so it tiles around the trunk; stretched along y for vertical fissures
                    float n1 = Mathf.PerlinNoise(Mathf.Cos(u * Mathf.PI * 2f) * 3.2f + 10f, Mathf.Sin(u * Mathf.PI * 2f) * 3.2f + v * 2.2f);
                    float n2 = Mathf.PerlinNoise(Mathf.Cos(u * Mathf.PI * 2f) * 9f + 40f, Mathf.Sin(u * Mathf.PI * 2f) * 9f + v * 5f);
                    float ridge = Mathf.Abs(n1 - 0.5f) * 2f;                       // dark crevices where the noise crosses 0.5
                    float shade = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 0.55f, ridge)) * (0.85f + 0.3f * n2);
                    Color c = Color.Lerp(new Color(0.20f, 0.14f, 0.10f), new Color(0.46f, 0.36f, 0.27f), shade);
                    px[y * S + x] = new Color(c.r, c.g, c.b, 1f);
                }
            Write(name, px, S, true);
        }

        private static void Write(string name, Color[] px, int size, bool opaque = false)
        {
            string path = $"{Dir}/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = !opaque; imp.mipmapEnabled = true; imp.wrapMode = opaque ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; imp.maxTextureSize = 512;
            imp.SaveAndReimport();
        }
    }
}
