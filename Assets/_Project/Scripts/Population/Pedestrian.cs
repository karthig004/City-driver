using System.Collections.Generic;
using CityDriver.World;
using UnityEngine;

namespace CityDriver.Population
{
    /// <summary>
    /// A sidewalk pedestrian. It walks along one side of a road, turns the corner onto the cross street on the same side, and never
    /// steps into the carriageway (no crossings are implemented). It sometimes stops and waits for a few seconds. Animation swaps
    /// between three pose meshes, so there is no skinned mesh cost.
    /// </summary>
    public sealed class Pedestrian
    {
        public readonly Transform transform;
        public bool Active => transform.gameObject.activeSelf;
        public bool Idle { get; private set; }

        private readonly MeshFilter filter;
        private TrafficNetwork net;
        private int edge, from, to, variant;
        private float s, speed, idleTimer, stride, groundY;
        private Vector3 dir, side;
        private float offset;

        public Pedestrian(Transform t, MeshFilter mf) { transform = t; filter = mf; }

        public void SetGround(float y) { groundY = y; }

        public void Place(TrafficNetwork network, int edgeId, int fromNode, float along, int sideSign, int meshVariant)
        {
            net = network; edge = edgeId; from = fromNode; to = net.Other(edge, from); s = along; variant = meshVariant;
            dir = net.Direction(edge, from); side = TrafficNetwork.Left(dir) * sideSign;
            offset = Offset(edge);
            speed = Random.Range(1.05f, 1.55f); Idle = Random.value < 0.25f; idleTimer = Random.Range(2f, 9f); stride = Random.value;
            Vector3 pos = net.NodePos(from) + dir * s + side * offset; pos.y = groundY;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
            filter.sharedMesh = PopulationMeshes.Pedestrian(variant, 0);
        }

        private float Offset(int e) { RoadEdge ed = net.Edge(e); return ed.width * 0.5f + TrafficNetwork.SidewalkWidth(ed.roadClass) * 0.5f; }

        public void Tick(float dt)
        {
            if (net == null) return;
            if (Idle)
            {
                idleTimer -= dt;
                if (idleTimer <= 0f) Idle = false;
                filter.sharedMesh = PopulationMeshes.Pedestrian(variant, 0);
            }
            else
            {
                s += speed * dt;
                float length = net.Length(edge);
                // corner position along this edge: where the cross street's sidewalk begins
                float stop = length - CrossOffset();
                if (s >= stop) { s = stop; Turn(); }

                stride += dt * 1.9f;
                float f = stride - Mathf.Floor(stride);
                filter.sharedMesh = PopulationMeshes.Pedestrian(variant, f < 0.25f ? 1 : f < 0.5f ? 0 : f < 0.75f ? 2 : 0);
            }

            Vector3 pos = net.NodePos(from) + dir * s + side * offset;
            pos.y = Mathf.Lerp(transform.position.y, groundY, 0.25f);
            transform.SetPositionAndRotation(pos, Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 0.2f));
        }

        // Cross-street sidewalk centre offset at the end node (approximated by the widest road there).
        private float CrossOffset()
        {
            float best = 4.5f + 1.5f;
            foreach (int e in net.Incident(to)) if (e != edge) best = Mathf.Max(best, Offset(e));
            return best;
        }

        private void Turn()
        {
            // Candidate edges: leave the node toward our own side of the road (so we turn onto the near-side sidewalk without crossing).
            var options = new List<int>(3);
            foreach (int e in net.Incident(to))
            {
                if (e == edge) continue;
                if (net.Edge(e).roadClass == RoadClass.Highway) continue;
                if (Vector3.Dot(net.Direction(e, to), side) > 0.7f) options.Add(e);
            }

            if (options.Count > 0 && Random.value < 0.35f) { Idle = true; idleTimer = Random.Range(3f, 11f); }

            if (options.Count == 0)
            {
                // dead end for this side: turn back along the same physical sidewalk
                int old = from; from = to; to = old; dir = -dir; s = net.Length(edge) - s;
                return;
            }

            int next = options[Random.Range(0, options.Count)];
            Vector3 oldDir = dir;
            Vector3 corner = net.NodePos(to) + side * offset - oldDir * Offset(next);       // the block corner
            from = to; to = net.Other(next, from); edge = next;
            dir = net.Direction(next, from);
            side = -oldDir;                                                              // near-side sidewalk of the cross street
            offset = Offset(next);
            s = Mathf.Max(0.5f, Vector3.Dot(corner - net.NodePos(from), dir));
        }
    }
}
