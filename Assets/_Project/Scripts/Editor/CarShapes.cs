using System.Collections.Generic;

namespace CityDriver.EditorTools
{
    /// <summary>Silhouette parameters of one car body (metres, car-local: +Z = nose, Y up). All 8 cars share the physics frame (wheelbase +-1.36, track +-0.84).</summary>
    internal sealed class CarShape
    {
        public string id = "sedan_compact", displayName = "City Taxi";
        public float half = 2.28f, halfW = 0.93f, flatZ = 1.55f, yb = 0.30f;
        public float belt = 0.95f, noseH = 0.76f, tailH = 0.84f, hoodStart = 0.95f, trunkStart = -1.25f;
        public float roofTop = 1.46f, flatFront = 0.30f, baseFront = 1.02f, flatRear = -0.62f, baseRear = -1.28f;
        public float cabW = 0.82f, roofW = 0.64f;
        public int doors = 4;                       // 2 or 4
        public int spoiler;                         // 0 none, 1 lip, 2 wing
        public bool taxiSign, roofRack, hoodScoop, flares, cladding, bigGrille, rearHatch, diffuser, police, lightBar, bullBar;
        public string paintHex = "#F5BD0D";
    }

    internal static class CarShapes
    {
        public static readonly List<CarShape> All = new List<CarShape>
        {
            new CarShape { id = "sedan_compact", displayName = "City Taxi", half = 2.22f, taxiSign = true, paintHex = "#F5BD0D" },
            new CarShape { id = "hatch_city", displayName = "Metro Hatch", half = 1.98f, halfW = 0.90f, belt = 0.95f, noseH = 0.80f, tailH = 0.98f, trunkStart = -1.0f, hoodStart = 0.85f,
                           roofTop = 1.52f, flatFront = 0.35f, baseFront = 0.92f, flatRear = -0.95f, baseRear = -1.90f, cabW = 0.80f, roofW = 0.66f, rearHatch = true, paintHex = "#2E8B57" },
            new CarShape { id = "sedan_premium", displayName = "Metro Premium", half = 2.36f, halfW = 0.95f, belt = 0.98f, noseH = 0.80f, tailH = 0.90f, trunkStart = -1.3f,
                           roofTop = 1.48f, flatFront = 0.28f, baseFront = 1.06f, flatRear = -0.70f, baseRear = -1.35f, bigGrille = true, paintHex = "#15161A" },
            new CarShape { id = "sedan_sports", displayName = "Apex GT", half = 2.32f, halfW = 0.96f, belt = 0.90f, noseH = 0.68f, tailH = 0.82f, hoodStart = 0.85f,
                           roofTop = 1.38f, flatFront = 0.20f, baseFront = 1.12f, flatRear = -0.55f, baseRear = -1.30f, spoiler = 1, flares = true, diffuser = true, paintHex = "#B3141A" },
            new CarShape { id = "muscle_coupe", displayName = "Bulldog", half = 2.42f, halfW = 0.97f, belt = 0.93f, noseH = 0.82f, tailH = 0.88f, hoodStart = 0.55f, trunkStart = -1.45f,
                           roofTop = 1.36f, flatFront = -0.05f, baseFront = 0.78f, flatRear = -0.70f, baseRear = -1.30f, doors = 2, spoiler = 1, hoodScoop = true, flares = true, paintHex = "#E07A1F" },
            new CarShape { id = "suv_performance", displayName = "Ridge Runner", half = 2.30f, halfW = 0.97f, yb = 0.40f, belt = 1.10f, noseH = 0.98f, tailH = 1.14f, hoodStart = 1.10f, trunkStart = -2.0f,
                           roofTop = 1.72f, flatFront = 0.45f, baseFront = 1.18f, flatRear = -1.30f, baseRear = -2.06f, cabW = 0.86f, roofW = 0.74f, roofRack = true, cladding = true, paintHex = "#4C4F57" },
            new CarShape { id = "suv_luxury", displayName = "Sovereign LX", half = 2.40f, halfW = 0.99f, yb = 0.42f, belt = 1.14f, noseH = 1.02f, tailH = 1.16f, hoodStart = 1.15f, trunkStart = -2.05f,
                           roofTop = 1.78f, flatFront = 0.42f, baseFront = 1.20f, flatRear = -1.35f, baseRear = -2.12f, cabW = 0.88f, roofW = 0.76f, bigGrille = true, cladding = true, paintHex = "#E9E9E6" },
            new CarShape { id = "supercar", displayName = "Vantage S", half = 2.34f, halfW = 1.00f, yb = 0.20f, belt = 0.74f, noseH = 0.52f, tailH = 0.78f, hoodStart = 0.55f, trunkStart = -1.05f,
                           roofTop = 1.14f, flatFront = 0.10f, baseFront = 1.10f, flatRear = -0.30f, baseRear = -1.05f, cabW = 0.80f, roofW = 0.56f, doors = 2, spoiler = 2, flares = true, diffuser = true, paintHex = "#1F4FA3" },
        };

        public static readonly List<CarShape> Extra = new List<CarShape>
        {
            new CarShape { id = "traffic_sedan", displayName = "Traffic Sedan", half = 2.22f, paintHex = "#C8CCD2" },
            new CarShape { id = "police_sedan", displayName = "Police Sedan", half = 2.36f, halfW = 0.95f, belt = 0.98f, noseH = 0.80f, tailH = 0.90f, trunkStart = -1.3f, roofTop = 1.48f, flatFront = 0.28f, baseFront = 1.06f, flatRear = -0.70f, baseRear = -1.35f,
                           bigGrille = true, police = true, lightBar = true, bullBar = true, paintHex = "#F4F4F2" },
            new CarShape { id = "police_suv", displayName = "Police SUV", half = 2.30f, halfW = 0.97f, yb = 0.40f, belt = 1.10f, noseH = 0.98f, tailH = 1.14f, hoodStart = 1.10f, trunkStart = -2.0f, roofTop = 1.72f, flatFront = 0.45f, baseFront = 1.18f,
                           flatRear = -1.30f, baseRear = -2.06f, cabW = 0.86f, roofW = 0.74f, cladding = true, police = true, lightBar = true, bullBar = true, paintHex = "#F4F4F2" },
            new CarShape { id = "gang_suv", displayName = "Gang SUV", half = 2.40f, halfW = 0.99f, yb = 0.42f, belt = 1.14f, noseH = 1.02f, tailH = 1.16f, hoodStart = 1.15f, trunkStart = -2.05f, roofTop = 1.78f, flatFront = 0.42f, baseFront = 1.20f,
                           flatRear = -1.35f, baseRear = -2.12f, cabW = 0.88f, roofW = 0.76f, bigGrille = true, cladding = true, bullBar = true, paintHex = "#0F1013" },
        };

        public static CarShape Find(string id) { foreach (var s in All) if (s.id == id) return s; foreach (var s in Extra) if (s.id == id) return s; return All[0]; }
    }
}
