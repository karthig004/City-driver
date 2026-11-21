using UnityEngine;

namespace CityDriver.Population
{
    /// <summary>
    /// The baked traffic / police / gang car models: every car is ONE mesh with 11 fixed material slots
    /// (paint, glass, trim, chrome, tyre, rim, headlight, taillight, livery, light bar A, light bar B), so traffic stays cheap to draw.
    /// Lives in Resources/TrafficCars/TrafficCarSet.prefab (built by the editor baker).
    /// </summary>
    public sealed class TrafficCarSet : MonoBehaviour
    {
        public const int Slots = 11;
        public Mesh[] traffic = new Mesh[0];            // civilian shapes
        public Mesh police, policeSuv, gang;
        public Material[] baseMaterials = new Material[Slots];
        public Material[] paints = new Material[0];
        public Material barRedOn, barRedOff, barBlueOn, barBlueOff;

        private static TrafficCarSet instance;
        private static bool searched;
        private Material[][] cache;

        /// <summary>The loaded set, or null when the baker has not been run (callers then fall back to the old box meshes).</summary>
        public static TrafficCarSet Get()
        {
            if (instance != null) return instance;
            if (searched) return null;
            searched = true;
            var prefab = Resources.Load<GameObject>("TrafficCars/TrafficCarSet");
            if (prefab != null) instance = prefab.GetComponent<TrafficCarSet>();
            return instance;
        }

        public int PaintCount => paints != null ? paints.Length : 0;
        public int ShapeCount => traffic != null ? traffic.Length : 0;

        /// <summary>Material array for a civilian car with the given paint.</summary>
        public Material[] Materials(int paint)
        {
            if (cache == null || cache.Length != Mathf.Max(1, PaintCount)) cache = new Material[Mathf.Max(1, PaintCount)][];
            paint = Mathf.Abs(paint) % cache.Length;
            if (cache[paint] == null || cache[paint].Length != baseMaterials.Length)
            {
                var a = (Material[])baseMaterials.Clone();
                if (paints != null && paints.Length > 0) a[0] = paints[paint];
                cache[paint] = a;
            }
            return cache[paint];
        }

        private Material[] policeA, policeB, gangMats;

        /// <summary>Police materials; the two states swap which light bar half is lit (red / blue), giving the flashing effect.</summary>
        public Material[] PoliceMaterials(bool stateA)
        {
            if (policeA == null || policeA.Length != baseMaterials.Length || policeB == null || policeB.Length != baseMaterials.Length)          // rebuilt if a stale / empty cache exists
            {
                policeA = (Material[])baseMaterials.Clone(); policeA[9] = barRedOn; policeA[10] = barBlueOff;
                policeB = (Material[])baseMaterials.Clone(); policeB[9] = barRedOff; policeB[10] = barBlueOn;
            }
            return stateA ? policeA : policeB;
        }

        public Material[] GangMaterials()
        {
            if (gangMats == null || gangMats.Length != baseMaterials.Length) { gangMats = (Material[])baseMaterials.Clone(); if (paints != null && paints.Length > 1) gangMats[0] = paints[1]; }
            return gangMats;
        }
    }
}
