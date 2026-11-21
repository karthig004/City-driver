using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// The material library for the large city: photographic PBR surfaces (asphalt, paving, concrete, grass, brick, plaster,
    /// corrugated steel, roof tiles), composited facades with windows and shopfronts, and the flat colours for trim and signage.
    /// Everything is created at fixed paths and updated in place.
    /// </summary>
    internal sealed class WorldMaterials
    {
        public const string Dir = "Assets/_Project/Art/Materials/World";

        private readonly Dictionary<string, Material> map = new Dictionary<string, Material>();

        public Material this[string key]
        {
            get { if (!map.TryGetValue(key, out var m)) throw new KeyNotFoundException("World material missing: " + key); return m; }
        }

        public static WorldMaterials Create()
        {
            CityAssetFactory.EnsureFolder(Dir);
            var lib = new WorldMaterials();
            lib.Build();
            AssetDatabase.SaveAssets();
            return lib;
        }

        private void Build()
        {
            // ---- photographic surfaces
            var asphalt = PhotoTextures.Prepare("Asphalt031");
            var concrete = PhotoTextures.Prepare("Concrete034");
            var paving = PhotoTextures.Prepare("PavingStones150");
            var grass = PhotoTextures.Prepare("Grass004");
            var brick = PhotoTextures.Prepare("Bricks106A");
            var plaster = PhotoTextures.Prepare("PaintedPlaster017");
            var steel = PhotoTextures.Prepare("CorrugatedSteel009");
            var roof = PhotoTextures.Prepare("RoofingTiles013A");

            Pbr("Road", asphalt, Color.white, Vector2.one);
            Pbr("Sidewalk", paving, new Color(0.92f, 0.92f, 0.94f), Vector2.one);
            Pbr("Concrete", concrete, Color.white, Vector2.one);
            Pbr("ConcreteDark", concrete, new Color(0.62f, 0.64f, 0.68f), Vector2.one);
            Pbr("Grass", grass, new Color(0.85f, 0.95f, 0.8f), Vector2.one);
            Pbr("Corrugated", steel, new Color(0.80f, 0.84f, 0.88f), new Vector2(2f, 1.75f));      // texture repeats every 2 m on a 4 x 3.5 m UV tile
            Pbr("CorrugatedRust", steel, new Color(0.85f, 0.62f, 0.50f), new Vector2(2f, 1.75f));
            Pbr("RoofTile", roof, new Color(0.90f, 0.65f, 0.55f), new Vector2(2f, 2f));

            // ---- composited facades (windows on real walls)
            Texture2D plasterWin = PhotoTextures.MakeFacade("Fac_PlasterWin", "PaintedPlaster017", PhotoTextures.Facade.WindowsWall);
            Texture2D brickWin = PhotoTextures.MakeFacade("Fac_BrickWin", "Bricks106A", PhotoTextures.Facade.WindowsWall, 2f, 1.75f);
            Texture2D concreteWin = PhotoTextures.MakeFacade("Fac_ConcreteWin", "Concrete034", PhotoTextures.Facade.WindowsWall);
            Texture2D glass = PhotoTextures.MakeFacade("Fac_Glass", "Concrete034", PhotoTextures.Facade.CurtainGlass);
            Texture2D shop = PhotoTextures.MakeFacade("Fac_Shop", "PaintedPlaster017", PhotoTextures.Facade.Shopfront);

            Facade("FacPlasterWarm", plasterWin, new Color(1.00f, 0.92f, 0.80f));
            Facade("FacPlasterCool", plasterWin, new Color(0.84f, 0.90f, 1.00f));
            Facade("FacPlasterPeach", plasterWin, new Color(1.00f, 0.80f, 0.70f));
            Facade("FacPlasterGreen", plasterWin, new Color(0.82f, 0.95f, 0.84f));
            Facade("FacPlasterWhite", plasterWin, new Color(0.96f, 0.96f, 0.94f));
            Facade("FacBrick", brickWin, new Color(1.0f, 0.92f, 0.88f));
            Facade("FacConcrete", concreteWin, new Color(0.92f, 0.93f, 0.95f));
            Facade("FacGlass", glass, Color.white);
            Facade("FacGlassTeal", glass, new Color(0.75f, 1.0f, 0.95f));
            Facade("FacShopA", shop, new Color(1f, 0.95f, 0.85f));
            Facade("FacShopB", shop, new Color(0.85f, 0.95f, 1f));
            Facade("FacShopC", shop, new Color(0.95f, 0.85f, 0.85f));

            // ---- flat colours
            Flat("TrimLight", new Color(0.84f, 0.84f, 0.82f), 0.12f);
            Flat("MetalDark", new Color(0.13f, 0.14f, 0.16f), 0.35f, 0.4f);
            Flat("TankBlack", new Color(0.06f, 0.06f, 0.07f), 0.25f);
            Flat("Awning1", new Color(0.78f, 0.12f, 0.12f), 0.2f);
            Flat("Awning2", new Color(0.12f, 0.45f, 0.75f), 0.2f);
            Flat("Awning3", new Color(0.95f, 0.70f, 0.10f), 0.2f);
            Flat("White", new Color(0.93f, 0.94f, 0.95f), 0.15f);
            Flat("PoliceBlue", new Color(0.12f, 0.22f, 0.50f), 0.25f);
            Flat("RedCross", new Color(0.80f, 0.08f, 0.08f), 0.2f);
            Flat("Gold", new Color(0.86f, 0.66f, 0.22f), 0.7f, 0.8f);
            Flat("ContainerRed", new Color(0.62f, 0.14f, 0.12f), 0.3f, 0.2f);
            Flat("ContainerBlue", new Color(0.14f, 0.30f, 0.58f), 0.3f, 0.2f);
            Flat("ContainerGreen", new Color(0.16f, 0.42f, 0.26f), 0.3f, 0.2f);
            Flat("ContainerOrange", new Color(0.85f, 0.46f, 0.10f), 0.3f, 0.2f);
            Flat("Barrier", new Color(0.62f, 0.62f, 0.60f), 0.1f);
            Flat("RailMetal", new Color(0.55f, 0.58f, 0.60f), 0.6f, 0.7f);
            Flat("SignalBox", new Color(0.10f, 0.10f, 0.11f), 0.3f);
            Emissive("NeonMagenta", new Color(1f, 0.15f, 0.75f), 2.2f);
            Emissive("NeonCyan", new Color(0.1f, 0.9f, 1f), 2.2f);
            Emissive("LampGlow", new Color(1f, 0.92f, 0.65f), 1.6f);
            Emissive("SignalRed", new Color(1f, 0.1f, 0.05f), 1.6f);
            Emissive("SignalAmber", new Color(1f, 0.6f, 0.05f), 1.2f);
            Emissive("SignalGreen", new Color(0.1f, 1f, 0.3f), 1.2f);
            Flat("PaintWhite", new Color(0.92f, 0.92f, 0.90f), 0.25f);
            Flat("PaintYellow", new Color(0.95f, 0.75f, 0.10f), 0.25f);
            Flat("PetrolRed", new Color(0.80f, 0.10f, 0.10f), 0.3f);
        }

        /// <summary>Gives the original core-district materials the photographic textures (same assets, new look).</summary>
        public static void UpgradeCoreMaterials(WorldMaterials lib)
        {
            Copy("M_Asphalt", lib["Road"]);
            Copy("M_Concrete", lib["Sidewalk"]);
            Copy("M_ConcreteDark", lib["ConcreteDark"]);
            Copy("M_Grass", lib["Grass"]);
        }

        private static void Copy(string coreName, Material source)
        {
            var target = AssetDatabase.LoadAssetAtPath<Material>($"{CityAssetFactory.MaterialsDir}/{coreName}.mat");
            if (target == null) return;
            target.CopyPropertiesFromMaterial(source);
            EditorUtility.SetDirty(target);
        }

        // ================================================================================== creation helpers

        private Material Make(string name, string shader)
        {
            string path = $"{Dir}/{name}.mat";
            Shader s = Shader.Find(shader);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(s); AssetDatabase.CreateAsset(m, path); } else m.shader = s;
            map[name] = m;
            EditorUtility.SetDirty(m);
            return m;
        }

        private void Pbr(string name, PhotoTextures.Set set, Color tint, Vector2 tiling)
        {
            Material m = Make(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseMap", set.baseSmooth); m.SetTextureScale("_BaseMap", tiling);
            m.SetTexture("_BumpMap", set.normal); m.SetTextureScale("_BumpMap", tiling);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_SmoothnessTextureChannel", 1f);                 // smoothness from albedo alpha
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_Metallic", 0f);
        }

        private void Facade(string name, Texture2D tex, Color tint)
        {
            Material m = Make(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_SmoothnessTextureChannel", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_Metallic", 0f);
        }

        private void Flat(string name, Color color, float smoothness, float metallic = 0f)
        {
            Material m = Make(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color); m.SetTexture("_BaseMap", null);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", metallic);
        }

        private void Emissive(string name, Color color, float intensity)
        {
            Material m = Make(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color * 0.4f); m.SetFloat("_Smoothness", 0.3f); m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
        }
    }
}
