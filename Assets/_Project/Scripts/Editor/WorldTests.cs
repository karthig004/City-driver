using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CityDriver.World;
using UnityEditor;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Edit Mode tests for the road network, routing and guidance, plus generation of the graph asset and a map image.</summary>
    public static class WorldTests
    {
        private const string AssetPath = "Assets/_Project/Data/World/CityRoadGraph.asset";

        private static StringBuilder log;
        private static int failures, checks;

        // The 8 existing mission markers (positions unchanged since Phase 3).
        private static readonly (string id, Vector3 pos)[] Markers =
        {
            ("pickup_a", new Vector3(-40, 0.17f, 6)), ("pickup_b", new Vector3(6, 0.17f, -40)), ("pickup_c", new Vector3(74, 0.17f, 40)),
            ("dropoff_a", new Vector3(40, 0.17f, -6)), ("dropoff_b", new Vector3(-74, 0.17f, 40)), ("dropoff_c", new Vector3(-40, 0.17f, -74)),
            ("garage", new Vector3(40, 0.2f, 6)), ("player_start", new Vector3(40, 0.6f, 22)),
        };

        /// <summary>One-off repair: deletes the road-graph asset (whose script reference was lost) and regenerates it.</summary>
        [BridgeCommand]
        public static void RecreateGraphAsset()
        {
            AssetDatabase.DeleteAsset(AssetPath);
            GenerateAndTest();
        }

        /// <summary>Generates the road graph, saves it as an asset, renders the map to Bridge/, and runs all network tests.</summary>
        [BridgeCommand]
        public static void GenerateAndTest()
        {
            log = new StringBuilder(); failures = 0; checks = 0;
            RoadGraphData data = CityLayout.Generate();
            SaveAsset(data);
            var graph = new RoadGraph(data);

            Layout(data, graph); Routing(data, graph); SmallGraphs(); Guidance(data, graph); Markers_(data, graph);
            RenderMaps(data, graph);

            Line($"\nRESULT: {checks - failures}/{checks} checks passed{(failures == 0 ? " - ALL PASSED" : $" - {failures} FAILED")}");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
            File.WriteAllText(Path.Combine(dir, "world_test.txt"), log.ToString());
            Debug.Log("[WorldTests] " + (failures == 0 ? "all passed" : failures + " failed") + $" ({checks} checks)");
        }

        // ================================================================================== tests

        private static void Layout(RoadGraphData d, RoadGraph g)
        {
            Section("Layout");
            float minX = d.nodes.Min(n => n.position.x), maxX = d.nodes.Max(n => n.position.x), minZ = d.nodes.Min(n => n.position.z), maxZ = d.nodes.Max(n => n.position.z);
            float km = d.edges.Sum(e => e.length) / 1000f;
            Line($"  nodes {d.nodes.Count}, edges {d.edges.Count}, total road length {km:F1} km, extent {(maxX - minX) / 1000f:F2} x {(maxZ - minZ) / 1000f:F2} km, signalised junctions {d.nodes.Count(n => n.signalised)}");
            Line($"  road classes: highway {d.edges.Count(e => e.roadClass == RoadClass.Highway)}, arterial {d.edges.Count(e => e.roadClass == RoadClass.Arterial)}, collector {d.edges.Count(e => e.roadClass == RoadClass.Collector)}, local {d.edges.Count(e => e.roadClass == RoadClass.Local)}");
            foreach (var dt in System.Enum.GetValues(typeof(DistrictType)).Cast<DistrictType>())
                Line($"    {dt,-14} nodes {d.nodes.Count(n => n.district == dt)}");

            Check(maxX - minX >= 1900f && maxZ - minZ >= 1900f, "city is about 1.92 km across");
            Check(g.IsFullyConnected(), "every junction is reachable from every other (one connected network)");
            Check(Enumerable.Range(0, g.NodeCount).All(i => g.Degree(i) >= 2), "no dead-end junctions");
            Check(d.edges.All(e => e.length > 1f && e.width >= 9f && e.lanesPerDirection >= 1), "all edges have length, width and lanes");
            Check(d.edges.Any(e => e.roadClass == RoadClass.Highway && e.lanesPerDirection == 3 && e.width == 22f), "highway ring has 3 lanes per direction, 22 m wide");
            Check(d.edges.Any(e => e.roadClass == RoadClass.Arterial && e.lanesPerDirection == 2), "arterials have 2 lanes per direction (different road widths exist)");
            Check(d.edges.Select(e => e.width).Distinct().Count() >= 3, "at least three different road widths");
            Check(d.nodes.Any(n => n.signalised), "traffic-signal junctions exist");
            Check(System.Enum.GetValues(typeof(DistrictType)).Cast<DistrictType>().All(t => d.nodes.Any(n => n.district == t)), "all seven district types contain roads");
            Check(new[] { PoiType.Hospital, PoiType.PoliceStation, PoiType.SafeHouse, PoiType.PetrolStation, PoiType.Garage }.All(t => d.pois.Any(p => p.type == t)), "hospital, police, safe house, fuel and garage locations exist");

            // Downtown preserved: the original roads (z=0 and x=0, 9 m wide) still exist exactly.
            bool preserved = true;
            foreach (float c in new float[] { -80, 0, 80 })
            {
                bool h = d.edges.Any(e => e.width == 9f && Mathf.Abs(d.nodes[e.from].position.z - c) < 0.1f && Mathf.Abs(d.nodes[e.to].position.z - c) < 0.1f && Mathf.Abs(d.nodes[e.from].position.x) <= 80f && Mathf.Abs(d.nodes[e.to].position.x) <= 80f);
                bool v = d.edges.Any(e => e.width == 9f && Mathf.Abs(d.nodes[e.from].position.x - c) < 0.1f && Mathf.Abs(d.nodes[e.to].position.x - c) < 0.1f && Mathf.Abs(d.nodes[e.from].position.z) <= 80f && Mathf.Abs(d.nodes[e.to].position.z) <= 80f);
                if (!h || !v) preserved = false;
            }

            Check(preserved, "original downtown 9 m road grid (0, +-80) is preserved");
            Check(CityLayout.Generate().edges.Count == d.edges.Count && CityLayout.Generate().nodes.Count == d.nodes.Count, "generation is deterministic");
        }

        private static void Routing(RoadGraphData d, RoadGraph g)
        {
            Section("Routing (A*)");
            var rng = new System.Random(7);
            int mismatches = 0, unreachable = 0;
            var pa = new List<int>(); var pd = new List<int>();
            for (int i = 0; i < 80; i++)
            {
                int a = rng.Next(g.NodeCount), b = rng.Next(g.NodeCount);
                bool okA = g.TryFindPath(a, b, pa, out float ma);
                bool okD = g.TryFindPathDijkstra(a, b, pd, out float md);
                if (!okA || !okD) { unreachable++; continue; }
                if (Mathf.Abs(PathTime(g, pa) - PathTime(g, pd)) > 0.01f) mismatches++;
            }

            Check(unreachable == 0, "all 80 random node pairs are reachable");
            Check(mismatches == 0, "A* finds the same optimal travel time as Dijkstra on 80 random pairs");

            int sw = NodeAt(d, -960, -960), ne = NodeAt(d, 960, 960);
            Check(g.TryFindPath(sw, ne, pa, out float meters), "route from the south-west corner to the north-east corner exists");
            float manhattan = 1920f * 2f;
            Line($"  corner to corner: {meters:F0} m over {pa.Count} junctions (Manhattan lower bound {manhattan:F0} m)");
            Check(meters >= manhattan - 1f && meters <= manhattan * 1.1f, "corner-to-corner distance is sensible (equal to the grid distance)");

            var sameNode = new List<int>();
            Check(g.TryFindPath(sw, sw, sameNode, out float zero) && zero == 0f && sameNode.Count == 1, "route to the same node is trivial");
            Check(!g.TryFindPath(-1, 3, null, out _) && !g.TryFindPath(0, g.NodeCount + 5, null, out _), "invalid node ids are rejected, not crashed on");

            // Highways are preferred when they are faster.
            int a2 = NodeAt(d, -960, 0), b2 = NodeAt(d, 960, 0);
            g.TryFindPath(a2, b2, pa, out float m2);
            Check(m2 <= 1920f * 1.05f, $"long west-east trip ({m2:F0} m) stays near-straight");
        }

        private static void SmallGraphs()
        {
            Section("Routing edge cases (hand-built graphs)");
            // A -> B one-way, B <-> C two-way, and an isolated D.
            var data = new RoadGraphData();
            data.nodes.Add(new RoadNode { id = 0, position = new Vector3(0, 0, 0) });
            data.nodes.Add(new RoadNode { id = 1, position = new Vector3(100, 0, 0) });
            data.nodes.Add(new RoadNode { id = 2, position = new Vector3(200, 0, 0) });
            data.nodes.Add(new RoadNode { id = 3, position = new Vector3(0, 0, 500) });
            data.edges.Add(new RoadEdge { id = 0, from = 0, to = 1, speedLimitKmh = 50, lanesPerDirection = 1, width = 9, oneWay = true });
            data.edges.Add(new RoadEdge { id = 1, from = 1, to = 2, speedLimitKmh = 50, lanesPerDirection = 1, width = 9 });
            var g = new RoadGraph(data);
            var path = new List<int>();
            Check(g.TryFindPath(0, 2, path, out float m) && m == 200f && path.SequenceEqual(new[] { 0, 1, 2 }), "forward along a one-way street works");
            Check(!g.TryFindPath(1, 0, path, out _), "driving against a one-way street is impossible");
            Check(!g.TryFindPath(0, 3, path, out _), "an unreachable node returns false (no crash)");
            Check(!g.IsFullyConnected(), "connectivity check detects an isolated node");

            bool threw = false;
            try { var bad = new RoadGraphData(); bad.nodes.Add(new RoadNode { id = 5 }); new RoadGraph(bad); } catch (System.ArgumentException) { threw = true; }
            Check(threw, "inconsistent node ids are rejected with an exception");
        }

        private static void Guidance(RoadGraphData d, RoadGraph g)
        {
            Section("GPS guidance");
            var route = new Route();
            Vector3 from = Markers.First(m => m.id == "pickup_a").pos, to = Markers.First(m => m.id == "dropoff_c").pos;
            Check(g.TryBuildRoute(from, to, route), "route between pickup_a and dropoff_c is built");
            Line($"  route length {route.Length:F0} m, {route.nodes.Count} junctions, {route.points.Count} polyline points");
            Check(route.Length >= Vector3.Distance(from, to), "route is at least as long as the straight line");

            var gd = new RouteGuidance();
            gd.SetRoute(route);
            gd.Update(from);
            Check(Mathf.Abs(gd.DistanceRemaining - route.Length) < 15f, $"at the start, remaining ≈ route length ({gd.DistanceRemaining:F0} of {route.Length:F0})");
            Line($"  first instruction: \"{gd.Instruction()}\"");

            // Drive the polyline in 5 m steps: remaining distance must only decrease, and we must arrive.
            float prev = gd.DistanceRemaining; bool monotonic = true; bool offRouteEver = false; var seen = new HashSet<TurnType>();
            float travelled = 0f;
            for (float s = 0f; s < route.Length; s += 5f)
            {
                gd.Update(PointAt(route, s));
                if (gd.DistanceRemaining > prev + 6f) monotonic = false;
                prev = gd.DistanceRemaining; offRouteEver |= gd.OffRoute; seen.Add(gd.NextTurn); travelled = s;
            }

            gd.Update(route.points[route.points.Count - 1]);
            Check(monotonic, "remaining distance decreases steadily while driving the route");
            Check(!offRouteEver, "never flagged off-route while following the route");
            Check(gd.Arrived, "arrival is detected at the destination");
            Check(seen.Contains(TurnType.Left) || seen.Contains(TurnType.Right), "turn instructions are produced (" + string.Join(",", seen) + ")");

            gd.SetRoute(route);
            Vector3 lost = PointAt(route, route.Length * 0.4f) + new Vector3(60f, 0f, 60f);
            gd.Update(lost);
            Check(gd.OffRoute, "leaving the route by 60+ m is detected");
            var reroute = new Route();
            Check(g.TryBuildRoute(lost, to, reroute) && Vector3.Distance(reroute.points[0], lost) <= 90f, "recalculation from the off-route position gives a new route that starts nearby");
            gd.SetRoute(reroute); gd.Update(lost);
            Check(!gd.OffRoute, "after recalculation the player is back on the route");
        }

        private static void Markers_(RoadGraphData d, RoadGraph g)
        {
            Section("Existing mission markers on the network");
            bool allNear = true;
            foreach (var (id, pos) in Markers)
            {
                bool ok = g.TrySnap(pos, out RoadSnap s);
                float allowed = id == "player_start" ? 30f : 12f;   // the spawn point is inside the garage forecourt, not on a kerb
                if (!ok || s.distance > allowed) allNear = false;
                Line($"  {id,-13} snaps to a road {(ok ? s.distance.ToString("F1") : "n/a")} m away (road class {(ok ? g.Edge(s.edge).roadClass.ToString() : "-")})");
            }

            Check(allNear, "all 8 marker positions are near the network (kerb markers within 12 m, the garage-forecourt spawn within 30 m)");
            var r = new Route(); int built = 0;
            foreach (var p in Markers.Where(m => m.id.StartsWith("pickup"))) foreach (var q in Markers.Where(m => m.id.StartsWith("dropoff"))) if (g.TryBuildRoute(p.pos, q.pos, r)) built++;
            Check(built == 9, "routes exist between every pickup and every drop-off (9 of 9)");
        }

        // ================================================================================== output

        private static void SaveAsset(RoadGraphData data)
        {
            CityAssetFactory.EnsureFolder("Assets/_Project/Data/World");
            var asset = AssetDatabase.LoadAssetAtPath<RoadGraphAsset>(AssetPath);
            if (asset == null) { asset = ScriptableObject.CreateInstance<RoadGraphAsset>(); AssetDatabase.CreateAsset(asset, AssetPath); }
            asset.data = data;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private static void RenderMaps(RoadGraphData data, RoadGraph g)
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
            var route = new Route();
            g.TryBuildRoute(Markers.First(m => m.id == "pickup_a").pos, new Vector3(800, 0, 700), route);
            Texture2D tex = RoadMapRenderer.Render(data, 1024, route);
            File.WriteAllBytes(Path.Combine(dir, "road_graph_map.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // ================================================================================== helpers

        private static float PathTime(RoadGraph g, List<int> path)
        {
            float t = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                RoadNode a = g.Node(path[i]), b = g.Node(path[i + 1]);
                // find the edge's speed: best (fastest) matching edge between the two nodes
                float best = float.PositiveInfinity;
                for (int e = 0; e < g.EdgeCount; e++)
                {
                    RoadEdge ed = g.Edge(e);
                    if ((ed.from == a.id && ed.to == b.id) || (ed.to == a.id && ed.from == b.id)) best = Mathf.Min(best, Vector3.Distance(a.position, b.position) / (Mathf.Max(5f, ed.speedLimitKmh) / 3.6f));
                }

                t += best;
            }

            return t;
        }

        private static int NodeAt(RoadGraphData d, float x, float z) => d.nodes.FindIndex(n => Mathf.Abs(n.position.x - x) < 0.1f && Mathf.Abs(n.position.z - z) < 0.1f);

        private static Vector3 PointAt(Route r, float distance)
        {
            for (int i = 0; i < r.points.Count - 1; i++)
                if (distance <= r.cumulative[i + 1])
                {
                    float seg = r.cumulative[i + 1] - r.cumulative[i];
                    return seg < 0.0001f ? r.points[i] : Vector3.Lerp(r.points[i], r.points[i + 1], (distance - r.cumulative[i]) / seg);
                }

            return r.points[r.points.Count - 1];
        }

        private static void Section(string t) => Line("\n" + t);
        private static void Line(string s) => log.AppendLine(s);
        private static void Check(bool ok, string text) { checks++; if (!ok) failures++; Line($"  [{(ok ? "PASS" : "FAIL")}] {text}"); }
    }
}
