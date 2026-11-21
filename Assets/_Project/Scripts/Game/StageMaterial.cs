using UnityEngine;

namespace CityDriver.Game
{
    /// <summary>Lit materials for code-made preview and map objects. Unity's built-in primitive material is stripped from web builds (magenta), so this copies a material that the car prefabs already reference.</summary>
    public static class StageMaterial
    {
        private static Material source;

        public static Material Make(Color c)
        {
            if (source == null)
            {
                var prefab = Resources.Load<GameObject>("CarBodies/Body_sedan_compact");
                if (prefab != null)
                    foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials) if (m != null && m.name == "M_CarTrim") source = m;
            }
            var mat = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default"));
            mat.SetColor("_BaseColor", c); mat.color = c;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.4f);
            return mat;
        }
    }
}
