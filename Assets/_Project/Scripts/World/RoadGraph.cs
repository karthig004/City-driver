using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityDriver.World
{
    /// <summary>A world position projected onto the road network.</summary>
    public struct RoadSnap
    {
        public int edge;
        public float t;           // 0 at edge.from .. 1 at edge.to
        public Vector3 point;
        public float distance;    // metres from the original position to the road centre line
    }

    /// <summary>A computed route: a polyline plus the node ids it passes through.</summary>
    public sealed class Route
    {
        public readonly List<Vector3> points = new List<Vector3>();
        public readonly List<int> nodes = new List<int>();
        public readonly List<float> cumulative = new List<float>();   // metres along the polyline at each point
        public float Length => cumulative.Count == 0 ? 0f : cumulative[cumulative.Count - 1];

        public void Clear() { points.Clear(); nodes.Clear(); cumulative.Clear(); }

        public void AddPoint(Vector3 p)
        {
            if (points.Count > 0 && (points[points.Count - 1] - p).sqrMagnitude < 0.0001f) return;
            cumulative.Add(points.Count == 0 ? 0f : cumulative[cumulative.Count - 1] + Vector3.Distance(points[points.Count - 1], p));
            points.Add(p);
        }
    }

    /// <summary>
    /// Routing and spatial queries over <see cref="RoadGraphData"/>. Pure C#, no scene access: usable from missions, GPS,
    /// traffic and tests. Costs are travel time (length / speed limit) so highways are preferred where sensible.
    /// </summary>
    public sealed class RoadGraph
    {
        private struct Arc { public int edge, to; public float length, cost; }

        private readonly RoadNode[] nodes;
        private readonly RoadEdge[] edges;
        private readonly List<Arc>[] adjacency;
        private readonly float maxSpeedMps;

        public RoadGraph(RoadGraphData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            nodes = data.nodes.ToArray();
            edges = data.edges.ToArray();
            adjacency = new List<Arc>[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].id != i) throw new ArgumentException($"Node id {nodes[i].id} must equal its index {i}.");
                adjacency[i] = new List<Arc>(4);
            }

            float maxSpeed = 10f;
            for (int i = 0; i < edges.Length; i++)
            {
                RoadEdge e = edges[i];
                if (e.id != i) throw new ArgumentException($"Edge id {e.id} must equal its index {i}.");
                if (e.from < 0 || e.to < 0 || e.from >= nodes.Length || e.to >= nodes.Length) throw new ArgumentException($"Edge {i} references a missing node.");

                float len = e.length > 0f ? e.length : Vector3.Distance(nodes[e.from].position, nodes[e.to].position);
                float mps = Mathf.Max(5f, e.speedLimitKmh) / 3.6f;
                maxSpeed = Mathf.Max(maxSpeed, mps);
                adjacency[e.from].Add(new Arc { edge = i, to = e.to, length = len, cost = len / mps });
                if (!e.oneWay) adjacency[e.to].Add(new Arc { edge = i, to = e.from, length = len, cost = len / mps });
            }

            maxSpeedMps = maxSpeed;
        }

        public int NodeCount => nodes.Length;
        public int EdgeCount => edges.Length;
        public RoadNode Node(int id) => nodes[id];
        public RoadEdge Edge(int id) => edges[id];
        public int Degree(int node) => adjacency[node].Count;

        // ------------------------------------------------------------------------------ search

        /// <summary>Fastest path (by travel time) between two nodes. Returns false if unreachable.</summary>
        public bool TryFindPath(int start, int goal, List<int> path, out float meters) => Search(start, goal, true, path, out meters);

        /// <summary>Same search without the heuristic (plain Dijkstra). Used by tests to prove A* is optimal.</summary>
        public bool TryFindPathDijkstra(int start, int goal, List<int> path, out float meters) => Search(start, goal, false, path, out meters);

        private bool Search(int start, int goal, bool useHeuristic, List<int> path, out float meters)
        {
            path?.Clear();
            meters = 0f;
            if (start < 0 || goal < 0 || start >= nodes.Length || goal >= nodes.Length) return false;

            int n = nodes.Length;
            var g = new float[n]; var parent = new int[n]; var parentLen = new float[n]; var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.PositiveInfinity; parent[i] = -1; }

            var heap = new List<(float f, int node)>(64);
            g[start] = 0f;
            Push(heap, (Heuristic(start, goal, useHeuristic), start));

            while (heap.Count > 0)
            {
                int u = Pop(heap).node;
                if (closed[u]) continue;
                closed[u] = true;
                if (u == goal) break;

                foreach (Arc a in adjacency[u])
                {
                    if (closed[a.to]) continue;
                    float ng = g[u] + a.cost;
                    if (ng < g[a.to])
                    {
                        g[a.to] = ng; parent[a.to] = u; parentLen[a.to] = a.length;
                        Push(heap, (ng + Heuristic(a.to, goal, useHeuristic), a.to));
                    }
                }
            }

            if (float.IsPositiveInfinity(g[goal])) return false;

            int cur = goal;
            while (cur != -1) { meters += cur == start ? 0f : parentLen[cur]; path?.Add(cur); cur = parent[cur]; }
            path?.Reverse();
            return true;
        }

        private float Heuristic(int node, int goal, bool use) =>
            use ? Vector3.Distance(nodes[node].position, nodes[goal].position) / maxSpeedMps : 0f;

        private static void Push(List<(float f, int node)> heap, (float f, int node) item)
        {
            heap.Add(item);
            int i = heap.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (heap[p].f <= heap[i].f) break;
                (heap[p], heap[i]) = (heap[i], heap[p]);
                i = p;
            }
        }

        private static (float f, int node) Pop(List<(float f, int node)> heap)
        {
            var top = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, s = i;
                if (l < heap.Count && heap[l].f < heap[s].f) s = l;
                if (r < heap.Count && heap[r].f < heap[s].f) s = r;
                if (s == i) break;
                (heap[s], heap[i]) = (heap[i], heap[s]);
                i = s;
            }

            return top;
        }

        // ------------------------------------------------------------------------------ spatial

        public int NearestNode(Vector3 p)
        {
            int best = -1; float bestSq = float.PositiveInfinity;
            for (int i = 0; i < nodes.Length; i++)
            {
                float sq = Flat(nodes[i].position - p).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }

            return best;
        }

        /// <summary>Projects a position onto the closest road segment (horizontal distance).</summary>
        public bool TrySnap(Vector3 p, out RoadSnap snap)
        {
            snap = default;
            float best = float.PositiveInfinity;
            for (int i = 0; i < edges.Length; i++)
            {
                Vector3 a = nodes[edges[i].from].position, b = nodes[edges[i].to].position;
                Vector3 ab = Flat(b - a);
                float len2 = ab.sqrMagnitude;
                float t = len2 < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(Flat(p - a), ab) / len2);
                Vector3 point = a + (b - a) * t;
                float d = Flat(p - point).magnitude;
                if (d < best) { best = d; snap = new RoadSnap { edge = i, t = t, point = point, distance = d }; }
            }

            return !float.IsPositiveInfinity(best);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ------------------------------------------------------------------------------ routes

        /// <summary>
        /// Builds a drivable route between two arbitrary world positions (e.g. the player and a mission waypoint):
        /// snaps both to the road network, picks the best combination of edge ends, and returns the polyline.
        /// </summary>
        public bool TryBuildRoute(Vector3 from, Vector3 to, Route route)
        {
            route.Clear();
            if (!TrySnap(from, out RoadSnap s) || !TrySnap(to, out RoadSnap e)) return false;

            // Same road segment: drive straight along it (if two-way or in the allowed direction).
            if (s.edge == e.edge && (!edges[s.edge].oneWay || e.t >= s.t))
            {
                route.AddPoint(s.point); route.AddPoint(e.point); route.AddPoint(to);
                return true;
            }

            RoadEdge se = edges[s.edge], ee = edges[e.edge];
            int[] startNodes = se.oneWay ? new[] { se.to } : new[] { se.from, se.to };
            int[] endNodes = ee.oneWay ? new[] { ee.from } : new[] { ee.from, ee.to };

            float bestTotal = float.PositiveInfinity;
            var bestPath = new List<int>();
            var tmp = new List<int>();
            foreach (int sn in startNodes)
                foreach (int en in endNodes)
                {
                    if (!TryFindPath(sn, en, tmp, out float meters)) continue;
                    float total = Vector3.Distance(s.point, nodes[sn].position) + meters + Vector3.Distance(e.point, nodes[en].position);
                    if (total < bestTotal) { bestTotal = total; bestPath = new List<int>(tmp); }
                }

            if (bestPath.Count == 0) return false;

            route.AddPoint(s.point);
            foreach (int id in bestPath) { route.nodes.Add(id); route.AddPoint(nodes[id].position); }
            route.AddPoint(e.point);
            route.AddPoint(to);
            return true;
        }

        /// <summary>True if every node can reach every other node (undirected connectivity of the two-way network).</summary>
        public bool IsFullyConnected()
        {
            if (nodes.Length == 0) return true;
            var seen = new bool[nodes.Length];
            var stack = new Stack<int>(); stack.Push(0); seen[0] = true; int count = 1;
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                foreach (Arc a in adjacency[u]) if (!seen[a.to]) { seen[a.to] = true; count++; stack.Push(a.to); }
            }

            return count == nodes.Length;
        }
    }
}
