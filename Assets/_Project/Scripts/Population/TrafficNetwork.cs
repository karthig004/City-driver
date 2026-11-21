using System.Collections.Generic;
using CityDriver.World;
using UnityEngine;

namespace CityDriver.Population
{
    /// <summary>The road graph as seen by traffic and pedestrians: incident edges per node, lane geometry and a simple signal cycle.</summary>
    public sealed class TrafficNetwork
    {
        public const float SignalCycle = 28f;

        public readonly RoadGraphData data;
        private readonly List<int>[] incident;
        public readonly Vector3[] edgeMid;

        public TrafficNetwork(RoadGraphData d)
        {
            data = d;
            incident = new List<int>[d.nodes.Count];
            for (int i = 0; i < incident.Length; i++) incident[i] = new List<int>(4);
            edgeMid = new Vector3[d.edges.Count];
            for (int i = 0; i < d.edges.Count; i++)
            {
                RoadEdge e = d.edges[i];
                incident[e.from].Add(i); incident[e.to].Add(i);
                edgeMid[i] = (d.nodes[e.from].position + d.nodes[e.to].position) * 0.5f;
            }
        }

        public int EdgeCount => data.edges.Count;
        public RoadEdge Edge(int i) => data.edges[i];
        public Vector3 NodePos(int n) => data.nodes[n].position;
        public bool Signalised(int n) => data.nodes[n].signalised;
        public List<int> Incident(int node) => incident[node];
        public int Other(int edge, int node) { RoadEdge e = data.edges[edge]; return e.from == node ? e.to : e.from; }

        public float Length(int edge)
        {
            RoadEdge e = data.edges[edge];
            return Mathf.Max(1f, (data.nodes[e.to].position - data.nodes[e.from].position).magnitude);
        }

        /// <summary>True when traffic may enter the edge from <paramref name="fromNode"/> (honours one-way streets).</summary>
        public bool CanEnter(int edge, int fromNode) { RoadEdge e = data.edges[edge]; return !e.oneWay || e.from == fromNode; }

        /// <summary>Unit direction of travel along the edge when starting at <paramref name="fromNode"/>.</summary>
        public Vector3 Direction(int edge, int fromNode)
        {
            int to = Other(edge, fromNode);
            Vector3 d = data.nodes[to].position - data.nodes[fromNode].position; d.y = 0f;
            return d.sqrMagnitude < 1e-4f ? Vector3.forward : d.normalized;
        }

        public static Vector3 Left(Vector3 dir) => Vector3.Cross(dir, Vector3.up);        // left-hand traffic keeps to this side of the road centre

        /// <summary>Sidewalk width by road class (matches the generator).</summary>
        public static float SidewalkWidth(RoadClass c) => c == RoadClass.Highway ? 2.5f : c == RoadClass.Arterial ? 4f : 3f;

        /// <summary>Distance from the road centre line to the centre of a lane (lane 0 is the one nearest the kerb).</summary>
        public float LaneOffset(int edge, int lane)
        {
            RoadEdge e = data.edges[edge];
            int lanes = Mathf.Max(1, e.lanesPerDirection);
            float half = e.width * 0.5f;
            return (Mathf.Clamp(lane, 0, lanes - 1) + 0.5f) * (half / lanes) * 0.85f;
        }

        /// <summary>Green / yellow / red for traffic travelling along <paramref name="dir"/> into <paramref name="node"/>.</summary>
        public SignalState Signal(int node, Vector3 dir, float time)
        {
            if (!data.nodes[node].signalised) return SignalState.Green;
            bool axisX = Mathf.Abs(dir.x) >= Mathf.Abs(dir.z);
            float phase = Mathf.Repeat(time + node * 3.7f + (axisX ? 0f : SignalCycle * 0.5f), SignalCycle);
            if (phase < 10f) return SignalState.Green;
            if (phase < 12f) return SignalState.Yellow;
            return SignalState.Red;
        }
    }

    public enum SignalState { Green, Yellow, Red }
}
