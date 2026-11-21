using System.IO;
using System.Text;
using CityDriver.Population;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Writes Bridge/pursuer_check.txt: are the baked police / gang car meshes and materials really present (no missing slots)?</summary>
    public static class PursuerCheck
    {
        [BridgeCommand]
        public static void Run()
        {
            var sb = new StringBuilder();
            var prefab = Resources.Load<GameObject>("TrafficCars/TrafficCarSet");
            var set = prefab != null ? prefab.GetComponent<TrafficCarSet>() : null;
            sb.AppendLine("TrafficCarSet prefab: " + (prefab != null) + ", component: " + (set != null));
            if (set != null)
            {
                void Mesh(string n, Mesh m) => sb.AppendLine($"  {n}: {(m != null ? m.vertexCount + " verts, " + m.subMeshCount + " submeshes, bounds " + m.bounds.size : "NULL")}");
                Mesh("police", set.police); Mesh("policeSuv", set.policeSuv); Mesh("gang", set.gang);
                for (int i = 0; i < set.baseMaterials.Length; i++) sb.AppendLine($"  baseMaterials[{i}] = {(set.baseMaterials[i] != null ? set.baseMaterials[i].name + " / " + set.baseMaterials[i].shader.name : "NULL")}");
                sb.AppendLine("  paints: " + set.paints.Length);
                for (int i = 0; i < set.paints.Length && i < 3; i++) sb.AppendLine($"  paints[{i}] = {(set.paints[i] != null ? set.paints[i].name : "NULL")}");
                Material[] gm = null; try { gm = set.GangMaterials(); } catch (System.Exception e) { sb.AppendLine("  GangMaterials threw: " + e.Message); } sb.AppendLine("  gang array: " + (gm == null ? "null" : gm.Length.ToString())); if (gm != null && gm.Length > 0) sb.AppendLine("  gang materials: " + gm.Length + ", nulls " + System.Array.FindAll(gm, m => m == null).Length + ", first " + (gm[0] != null ? gm[0].name : "NULL"));
                Material[] pm = null; try { pm = set.PoliceMaterials(true); } catch (System.Exception e) { sb.AppendLine("  PoliceMaterials threw: " + e.Message); } sb.AppendLine("  police array: " + (pm == null ? "null" : pm.Length.ToString())); if (pm != null && pm.Length > 0) sb.AppendLine("  police materials: " + pm.Length + ", nulls " + System.Array.FindAll(pm, m => m == null).Length + ", first " + (pm[0] != null ? pm[0].name : "NULL"));
            }
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge", "pursuer_check.txt"), sb.ToString());
        }
    }
}
