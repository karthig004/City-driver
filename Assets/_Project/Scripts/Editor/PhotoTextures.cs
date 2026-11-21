using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Prepares the downloaded CC0 photo textures for URP: colour x ambient occlusion with smoothness packed into the alpha
    /// channel (so one texture feeds base colour + smoothness) and an imported normal map. Also composites facade tiles
    /// (windows, curtain glass, shopfronts) on top of real wall photographs.
    /// </summary>
    internal static class PhotoTextures
    {
        public const string Root = "Assets/_Project/Art/Textures/Photo";
        public const string FacadeDir = "Assets/_Project/Art/Textures/Facades";

        internal struct Set { public Texture2D baseSmooth; public Texture2D normal; }

        private static string ProjectPath(string assetPath) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);

        private static Texture2D LoadRaw(string assetPath)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!tex.LoadImage(File.ReadAllBytes(ProjectPath(assetPath)))) throw new InvalidOperationException("Could not decode " + assetPath);
            return tex;
        }

        public static bool Exists(string id) => File.Exists(ProjectPath($"{Root}/{id}/{id}_1K-JPG_Color.jpg"));

        /// <summary>Builds (or reuses) the packed base+smoothness PNG and the normal map for an ambientCG material.</summary>
        public static Set Prepare(string id)
        {
            string dir = $"{Root}/{id}";
            string basePath = $"{dir}/{id}_BaseS.png";
            string normalPath = $"{dir}/{id}_1K-JPG_NormalGL.jpg";

            if (!File.Exists(ProjectPath(basePath)))
            {
                Texture2D color = LoadRaw($"{dir}/{id}_1K-JPG_Color.jpg");
                Texture2D rough = LoadRaw($"{dir}/{id}_1K-JPG_Roughness.jpg");
                string aoPath = $"{dir}/{id}_1K-JPG_AmbientOcclusion.jpg";
                Texture2D ao = File.Exists(ProjectPath(aoPath)) ? LoadRaw(aoPath) : null;

                Color32[] c = color.GetPixels32(), r = rough.GetPixels32(), a = ao != null ? ao.GetPixels32() : null;
                var o = new Color32[c.Length];
                for (int i = 0; i < c.Length; i++)
                {
                    float occ = a != null ? Mathf.Lerp(1f, a[i].r / 255f, 0.65f) : 1f;
                    o[i] = new Color32((byte)(c[i].r * occ), (byte)(c[i].g * occ), (byte)(c[i].b * occ), (byte)(255 - r[i].r));   // alpha = smoothness
                }

                var outTex = new Texture2D(color.width, color.height, TextureFormat.RGBA32, false, false);
                outTex.SetPixels32(o); outTex.Apply();
                File.WriteAllBytes(ProjectPath(basePath), outTex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(outTex); UnityEngine.Object.DestroyImmediate(color); UnityEngine.Object.DestroyImmediate(rough);
                if (ao != null) UnityEngine.Object.DestroyImmediate(ao);
            }

            AssetDatabase.ImportAsset(basePath, ImportAssetOptions.ForceSynchronousImport);
            var bi = (TextureImporter)AssetImporter.GetAtPath(basePath);
            bi.sRGBTexture = true; bi.alphaIsTransparency = false; bi.mipmapEnabled = true; bi.anisoLevel = 4; bi.maxTextureSize = 1024; bi.wrapMode = TextureWrapMode.Repeat;
            bi.SaveAndReimport();

            AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceSynchronousImport);
            var ni = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            ni.textureType = TextureImporterType.NormalMap; ni.mipmapEnabled = true; ni.anisoLevel = 4; ni.maxTextureSize = 1024; ni.wrapMode = TextureWrapMode.Repeat;
            ni.SaveAndReimport();

            return new Set { baseSmooth = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath), normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) };
        }

        // ================================================================================== facade composites

        public enum Facade { WindowsWall, CurtainGlass, Shopfront, PlainWall }

        /// <summary>
        /// One tile = 4 m wide x 3.5 m tall (one floor). Walls come from a real photograph; alpha carries smoothness
        /// (matt walls, glossy glass). Rebuilt only if missing.
        /// </summary>
        public static Texture2D MakeFacade(string name, string wallPhotoId, Facade kind, float wallRepeatX = 2f, float wallRepeatY = 1.75f)
        {
            string path = $"{FacadeDir}/{name}.png";
            if (!File.Exists(ProjectPath(path)))
            {
                CityAssetFactory.EnsureFolder(FacadeDir);
                Texture2D wall = LoadRaw($"{Root}/{wallPhotoId}/{wallPhotoId}_1K-JPG_Color.jpg");
                Color32[] wp = wall.GetPixels32(); int ww = wall.width, wh = wall.height;
                const int W = 512, H = 448;
                var o = new Color32[W * H];
                var rnd = new System.Random(name.GetHashCode());

                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float fx = x / (float)W, fy = y / (float)H;                       // fy = 0 at the floor line
                        Color32 wc = wp[(int)(Mathf.Repeat(fx * wallRepeatX, 1f) * (ww - 1)) + (int)(Mathf.Repeat(fy * wallRepeatY, 1f) * (wh - 1)) * ww];
                        Color col = new Color(wc.r / 255f, wc.g / 255f, wc.b / 255f); float smooth = 0.12f;

                        switch (kind)
                        {
                            case Facade.WindowsWall: WindowsPixel(fx, fy, ref col, ref smooth); break;
                            case Facade.CurtainGlass: GlassPixel(x, y, fx, fy, rnd, ref col, ref smooth); break;
                            case Facade.Shopfront: ShopPixel(fx, fy, ref col, ref smooth); break;
                        }

                        o[y * W + x] = new Color32((byte)(Mathf.Clamp01(col.r) * 255), (byte)(Mathf.Clamp01(col.g) * 255), (byte)(Mathf.Clamp01(col.b) * 255), (byte)(smooth * 255));
                    }

                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
                tex.SetPixels32(o); tex.Apply();
                File.WriteAllBytes(ProjectPath(path), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(wall);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = true; imp.alphaIsTransparency = false; imp.mipmapEnabled = true; imp.anisoLevel = 4; imp.maxTextureSize = 512; imp.wrapMode = TextureWrapMode.Repeat;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void WindowsPixel(float fx, float fy, ref Color col, ref float smooth)
        {
            if (fy < 0.06f) { col *= 0.70f; return; }                                         // floor slab band
            // Two windows per 4 m bay, with a darker reveal and a frame.
            for (int w = 0; w < 2; w++)
            {
                float cx = 0.25f + w * 0.5f, hx = 0.17f;
                bool inX = Mathf.Abs(fx - cx) < hx, inY = fy > 0.26f && fy < 0.84f;
                if (!(inX && inY)) continue;
                bool frame = Mathf.Abs(fx - cx) > hx - 0.018f || fy < 0.285f || fy > 0.82f || Mathf.Abs(fx - cx) < 0.008f;
                if (frame) { col = new Color(0.82f, 0.82f, 0.80f) * 0.85f; smooth = 0.3f; return; }
                float g = Mathf.InverseLerp(0.285f, 0.82f, fy);
                col = Color.Lerp(new Color(0.07f, 0.11f, 0.17f), new Color(0.32f, 0.45f, 0.60f), g * g); smooth = 0.85f;
                return;
            }

            if (fy > 0.84f && fy < 0.875f) col *= 0.78f;                                        // lintel shadow
        }

        private static void GlassPixel(int x, int y, float fx, float fy, System.Random rnd, ref Color col, ref float smooth)
        {
            // 1 m x 1.75 m panes with slim mullions; each pane gets a slightly different reflection.
            float px = Mathf.Repeat(fx * 4f, 1f), py = Mathf.Repeat(fy * 2f, 1f);
            bool mullion = px < 0.035f || px > 0.965f || py < 0.04f || py > 0.96f;
            if (mullion) { col = new Color(0.12f, 0.13f, 0.15f); smooth = 0.4f; return; }
            int pane = Mathf.FloorToInt(fx * 4f) + 4 * Mathf.FloorToInt(fy * 2f);
            float shade = 0.75f + 0.25f * Mathf.Abs(Mathf.Sin(pane * 12.9898f));
            col = Color.Lerp(new Color(0.10f, 0.20f, 0.30f), new Color(0.45f, 0.62f, 0.78f), (1f - py) * 0.8f * shade);
            smooth = 0.92f;
        }

        private static void ShopPixel(float fx, float fy, ref Color col, ref float smooth)
        {
            // Ground floor: signboard band on top, large shop glazing on the left 70%, door on the right, solid base below.
            if (fy > 0.86f) { col = Color.Lerp(col, new Color(0.92f, 0.90f, 0.82f), 0.85f); smooth = 0.25f; return; }          // sign band
            bool glazing = fx > 0.04f && fx < 0.70f && fy > 0.22f && fy < 0.80f;
            bool door = fx > 0.76f && fx < 0.94f && fy > 0.02f && fy < 0.80f;
            if (glazing || door)
            {
                bool frame = (glazing && (fx < 0.065f || fx > 0.675f || fy < 0.245f || fy > 0.775f || Mathf.Abs(fx - 0.37f) < 0.01f)) || (door && (fx < 0.78f || fx > 0.92f || fy > 0.78f));
                if (frame) { col = new Color(0.25f, 0.26f, 0.28f); smooth = 0.4f; return; }
                col = Color.Lerp(new Color(0.06f, 0.09f, 0.12f), new Color(0.38f, 0.50f, 0.58f), Mathf.InverseLerp(0.22f, 0.8f, fy) * 0.55f); smooth = 0.9f;
                return;
            }

            if (fy < 0.20f) col *= 0.75f;                                                      // plinth
        }
    }
}
