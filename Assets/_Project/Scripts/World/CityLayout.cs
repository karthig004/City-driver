using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityDriver.World
{
    /// <summary>
    /// Deterministic generator for the city's road network (about 1.92 km across). Pure data: no scene, no meshes.
    /// The central 480 m keeps the original 80 m downtown grid (roads at 0, +-80, +-160, +-240, 9 m wide), so the existing
    /// roads and mission markers stay exactly where they were. Outside it the grid widens, arterials (14 m, 2 lanes each way)
    /// run at +-480 / +-720 and a 3-lane highway ring (22 m) encloses the city at +-960. Local streets are pruned in
    /// industrial and commercial districts to create large blocks.
    ///
    /// Districts (X/Z):  NW,W residential | N,SE commercial | centre downtown | NE entertainment | E special | SW,S industrial.
    /// Traffic convention: left-hand.
    /// </summary>
    public static class CityLayout
    {
        public const float HalfSize = 960f;

        private static readonly float[] LineCoords =
        {
            -960, -840, -720, -600, -480, -360, -240, -160, -80, 0, 80, 160, 240, 360, 480, 600, 720, 840, 960
        };

        public static IReadOnlyList<float> Lines => LineCoords;

        public static RoadClass ClassOfLine(float c)
        {
            float a = Mathf.Abs(c);
            if (a >= 959f) return RoadClass.Highway;
            if (Mathf.Abs(a - 480f) < 1f || Mathf.Abs(a - 720f) < 1f) return RoadClass.Arterial;
            if (a < 1f || Mathf.Abs(a - 240f) < 1f) return RoadClass.Collector;
            return RoadClass.Local;
        }

        public static float WidthOf(RoadClass c) => c == RoadClass.Highway ? 22f : c == RoadClass.Arterial ? 14f : 9f;
        public static int LanesOf(RoadClass c) => c == RoadClass.Highway ? 3 : c == RoadClass.Arterial ? 2 : 1;

        public static float SpeedOf(RoadClass c, float lineCoord)
        {
            switch (c)
            {
                case RoadClass.Highway: return 80f;
                case RoadClass.Arterial: return 60f;
                case RoadClass.Collector: return 50f;
                default: return Mathf.Abs(lineCoord) <= 240f ? 40f : 30f;
            }
        }

        public static List<DistrictRect> BuildDistricts()
        {
            DistrictRect D(string n, DistrictType t, float x0, float z0, float x1, float z1) =>
                new DistrictRect { name = n, type = t, min = new Vector2(x0, z0), max = new Vector2(x1, z1) };

            return new List<DistrictRect>
            {
                D("Downtown", DistrictType.Downtown, -240, -240, 240, 240),
                D("Northwest Residential", DistrictType.Residential, -960, 240, -240, 960),
                D("West Residential", DistrictType.Residential, -960, -240, -240, 240),
                D("North Commercial", DistrictType.Commercial, -240, 240, 240, 960),
                D("Entertainment District", DistrictType.Entertainment, 240, 240, 960, 960),
                D("Special Locations", DistrictType.Special, 240, -240, 960, 240),
                D("Southeast Commercial", DistrictType.Commercial, 240, -960, 960, -240),
                D("Southwest Industrial", DistrictType.Industrial, -960, -960, -240, -240),
                D("South Industrial", DistrictType.Industrial, -240, -960, 240, -240),
            };
        }

        public static DistrictType DistrictAt(IReadOnlyList<DistrictRect> districts, float x, float z)
        {
            if (Mathf.Abs(x) >= 959f || Mathf.Abs(z) >= 959f) return DistrictType.Highway;
            for (int i = 0; i < districts.Count; i++)
            {
                DistrictRect d = districts[i];
                // Half-open on the max side so shared borders belong to exactly one district.
                if (x >= d.min.x && x < d.max.x && z >= d.min.y && z < d.max.y) return d.type;
            }

            return DistrictType.Downtown;
        }

        public static RoadGraphData Generate()
        {
            var data = new RoadGraphData { districts = BuildDistricts() };
            int n = LineCoords.Length;

            // 1) Candidate edges on the full grid. Index [ix, iz] is the node at (LineCoords[ix], LineCoords[iz]).
            var candidates = new List<(int a, int b, RoadClass cls, float line, bool prune)>();
            int Id(int ix, int iz) => ix * n + iz;

            for (int iz = 0; iz < n; iz++)                      // horizontal roads (constant Z)
                for (int ix = 0; ix < n - 1; ix++)
                    candidates.Add(MakeCandidate(data.districts, Id(ix, iz), Id(ix + 1, iz), LineCoords[iz], new Vector2(LineCoords[ix], LineCoords[iz]), new Vector2(LineCoords[ix + 1], LineCoords[iz])));
            for (int ix = 0; ix < n; ix++)                      // vertical roads (constant X)
                for (int iz = 0; iz < n - 1; iz++)
                    candidates.Add(MakeCandidate(data.districts, Id(ix, iz), Id(ix, iz + 1), LineCoords[ix], new Vector2(LineCoords[ix], LineCoords[iz]), new Vector2(LineCoords[ix], LineCoords[iz + 1])));

            // 2) Prune local streets inside industrial / commercial districts, then iteratively remove dead-end local stubs.
            var alive = new List<bool>(candidates.Count);
            foreach (var c in candidates) alive.Add(!c.prune);

            bool changed = true;
            while (changed)
            {
                changed = false;
                var degree = new int[n * n];
                for (int i = 0; i < candidates.Count; i++) if (alive[i]) { degree[candidates[i].a]++; degree[candidates[i].b]++; }
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!alive[i] || candidates[i].cls != RoadClass.Local) continue;
                    if (degree[candidates[i].a] == 1 || degree[candidates[i].b] == 1) { alive[i] = false; changed = true; }
                }
            }

            // 3) Re-index nodes that still have edges.
            var remap = new int[n * n];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var nodeDegree = new int[n * n];
            for (int i = 0; i < candidates.Count; i++) if (alive[i]) { nodeDegree[candidates[i].a]++; nodeDegree[candidates[i].b]++; }

            for (int ix = 0; ix < n; ix++)
                for (int iz = 0; iz < n; iz++)
                {
                    int old = Id(ix, iz);
                    if (nodeDegree[old] == 0) continue;
                    remap[old] = data.nodes.Count;
                    data.nodes.Add(new RoadNode { id = data.nodes.Count, position = new Vector3(LineCoords[ix], 0f, LineCoords[iz]), district = DistrictAt(data.districts, LineCoords[ix], LineCoords[iz]) });
                }

            // 4) Edges.
            var classes = new Dictionary<int, List<RoadClass>>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!alive[i]) continue;
                var c = candidates[i];
                int from = remap[c.a], to = remap[c.b];
                float len = Vector3.Distance(data.nodes[from].position, data.nodes[to].position);
                data.edges.Add(new RoadEdge
                {
                    id = data.edges.Count, from = from, to = to, roadClass = c.cls, lanesPerDirection = LanesOf(c.cls),
                    width = WidthOf(c.cls), speedLimitKmh = SpeedOf(c.cls, c.line), oneWay = false, length = len,
                });
                foreach (int node in new[] { from, to })
                {
                    if (!classes.TryGetValue(node, out var list)) classes[node] = list = new List<RoadClass>();
                    list.Add(c.cls);
                }
            }

            // 5) Traffic signals: junctions of 3+ roads where an arterial/highway is involved, or two collectors cross.
            for (int i = 0; i < data.nodes.Count; i++)
            {
                RoadNode node = data.nodes[i];
                var list = classes[i];
                int major = 0, collectors = 0;
                foreach (var cls in list) { if (cls >= RoadClass.Arterial) major++; else if (cls == RoadClass.Collector) collectors++; }
                node.signalised = list.Count >= 3 && (major > 0 || collectors >= 4);
                data.nodes[i] = node;
            }

            data.pois = BuildPois(data.districts);
            return data;
        }

        private static (int a, int b, RoadClass cls, float line, bool prune) MakeCandidate(List<DistrictRect> districts, int a, int b, float line, Vector2 p0, Vector2 p1)
        {
            RoadClass cls = ClassOfLine(line);
            Vector2 mid = (p0 + p1) * 0.5f;
            DistrictType d = DistrictAt(districts, mid.x, mid.y);
            bool prune = cls == RoadClass.Local && Mathf.Abs(line) > 240f && (d == DistrictType.Industrial || d == DistrictType.Commercial);
            return (a, b, cls, line, prune);
        }

        private static List<PointOfInterest> BuildPois(List<DistrictRect> districts)
        {
            PointOfInterest P(string id, string name, PoiType type, float x, float z) =>
                new PointOfInterest { id = id, displayName = name, type = type, position = new Vector3(x, 0f, z), district = DistrictAt(districts, x, z) };

            return new List<PointOfInterest>
            {
                P("garage_main", "City Garage", PoiType.Garage, 40, 40),                       // existing garage block
                P("hospital_main", "City General Hospital", PoiType.Hospital, 420, 40),
                P("police_main", "Central Police Station", PoiType.PoliceStation, 540, -40),
                P("safehouse_1", "Safe House North-West", PoiType.SafeHouse, -540, 120),
                P("safehouse_2", "Safe House South", PoiType.SafeHouse, -40, -300),
                P("petrol_1", "Highway Fuel Station East", PoiType.PetrolStation, 900, 120),
                P("petrol_2", "Highway Fuel Station West", PoiType.PetrolStation, -900, -120),
                P("petrol_3", "Downtown Fuel Station", PoiType.PetrolStation, -120, 120),
                P("repair_1", "Karthi Auto Repair", PoiType.RepairWorkshop, -420, -40),
                P("luxury_1", "Skyline Residences", PoiType.LuxuryBuilding, 660, 120),
                P("nightclub_1", "Neon Lounge", PoiType.Nightclub, 420, 420),
                P("cinema_1", "Grand Cinema", PoiType.Cinema, 540, 540),
                P("hotel_1", "Harbour Hotel", PoiType.Hotel, 300, 660),
                P("warehouse_1", "Warehouse 7", PoiType.Warehouse, -420, -420),
                P("warehouse_2", "Container Yard", PoiType.Warehouse, -40, -540),
            };
        }
    }
}
