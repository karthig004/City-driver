using System.Collections.Generic;
using UnityEngine;

namespace CityDriver.Population
{
    /// <summary>
    /// One AI car. It follows the road graph in the left-hand lane, slows for the car or player ahead, stops at red signals
    /// (a simple fixed-cycle model, not the visible signal heads), and turns through junctions on a curve. It is kinematic (no
    /// physics) and is recycled near the player by <see cref="CityPopulation"/>. A parked car is the same object with no movement.
    /// </summary>
    public sealed class TrafficCar
    {
        private const float TurnRadiusLead = 9f;      // metres before/after the node where the turn curve starts/ends
        private const float StopLine = 9.5f;

        public readonly Transform transform;
        public bool Parked { get; private set; }
        public float Speed { get; private set; }
        public bool Active => transform.gameObject.activeSelf;
        public float StoppedForSignalSeconds { get; private set; }

        private TrafficNetwork net;
        private int edge, from, to, lane;
        private float s;
        private Vector3 dir, laneVec;
        private bool turning;
        private float u, turnLen;
        private Vector3 p0, p1, p2;
        private int nextEdge = -1, nextTo;
        private Vector3 nextDir, nextLane;
        private float cruise;
        private float groundY;

        public TrafficCar(Transform t) { transform = t; }

        public void SetGround(float y) { groundY = y; }

        public void PlaceMoving(TrafficNetwork network, int edgeId, int fromNode, float along, int laneIndex, float cruiseSpeed)
        {
            net = network; Parked = false; edge = edgeId; from = fromNode; to = net.Other(edge, from); lane = laneIndex; s = along;
            dir = net.Direction(edge, from); laneVec = TrafficNetwork.Left(dir) * net.LaneOffset(edge, lane);
            turning = false; nextEdge = -1; cruise = cruiseSpeed; Speed = cruiseSpeed * 0.6f; StoppedForSignalSeconds = 0f;
            Vector3 pos = net.NodePos(from) + dir * s + laneVec; pos.y = groundY;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
        }

        public void PlaceParked(TrafficNetwork network, int edgeId, int fromNode, float along)
        {
            net = network; Parked = true; edge = edgeId; from = fromNode; Speed = 0f;
            Vector3 d = net.Direction(edge, from);
            float half = net.Edge(edge).width * 0.5f;
            Vector3 pos = net.NodePos(from) + d * along + TrafficNetwork.Left(d) * (half - 0.8f); pos.y = groundY;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(d));
        }

        // ------------------------------------------------------------------------------------------------ simulation
        public void Tick(float dt, float time, IList<TrafficCar> others, Transform player)
        {
            if (Parked || net == null) { return; }

            float allowed = turning ? Mathf.Min(cruise, 6.5f) : cruise;
            float length = net.Length(edge);
            bool holdingForSignal = false;

            if (!turning)
            {
                float remaining = length - s;
                if (nextEdge == -1 && remaining < 45f) ChooseNext();
                if (nextEdge == -2) { Speed = 0f; return; }       // stuck: the manager will recycle this car

                if (remaining < 45f && net.Signalised(to))
                {
                    SignalState st = net.Signal(to, dir, time);
                    bool canStopOnYellow = Speed * Speed / (2f * 3.5f) < remaining - StopLine;
                    if ((st == SignalState.Red || (st == SignalState.Yellow && canStopOnYellow)) && remaining > StopLine - 0.3f)
                    {
                        allowed = Mathf.Min(allowed, Mathf.Sqrt(2f * 3.5f * Mathf.Max(0f, remaining - StopLine)));
                        holdingForSignal = true;
                    }
                }

                if (remaining <= TurnRadiusLead) BeginTurn();
            }

            allowed = Mathf.Min(allowed, GapLimit(others, player));
            if (Speed > allowed) Speed = Mathf.Max(allowed, Speed - 8f * dt); else Speed = Mathf.Min(allowed, Speed + 2.2f * dt);
            StoppedForSignalSeconds = holdingForSignal && Speed < 0.2f ? StoppedForSignalSeconds + dt : 0f;

            Vector3 pos, forward;
            if (!turning)
            {
                s += Speed * dt;
                pos = net.NodePos(from) + dir * s + laneVec; forward = dir;
            }
            else
            {
                u += Speed * dt / turnLen;
                if (u >= 1f) { EndTurn(); pos = net.NodePos(from) + dir * s + laneVec; forward = dir; }
                else
                {
                    float w = 1f - u;
                    pos = w * w * p0 + 2f * w * u * p1 + u * u * p2;
                    forward = 2f * w * (p1 - p0) + 2f * u * (p2 - p1);
                    forward.y = 0f; if (forward.sqrMagnitude < 1e-4f) forward = dir; else forward.Normalize();
                }
            }

            pos.y = Mathf.Lerp(transform.position.y, groundY, 0.2f);
            transform.SetPositionAndRotation(pos, Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(forward), 0.35f));
        }

        private float GapLimit(IList<TrafficCar> others, Transform player)
        {
            float best = float.MaxValue;
            Vector3 fwd = transform.forward, right = transform.right, me = transform.position;
            for (int i = 0; i < others.Count; i++)
            {
                TrafficCar o = others[i];
                if (o == this || !o.Active) continue;
                best = Mathf.Min(best, GapTo(o.transform.position - me, fwd, right, 4.4f));
            }
            if (player != null) best = Mathf.Min(best, GapTo(player.position - me, fwd, right, 4.4f));
            return best == float.MaxValue ? float.MaxValue : Mathf.Max(0f, (best - 5.5f) * 0.9f);
        }

        private static float GapTo(Vector3 rel, Vector3 fwd, Vector3 right, float bodyLength)
        {
            float along = Vector3.Dot(rel, fwd);
            if (along < 0.5f || along > 24f) return float.MaxValue;
            if (Mathf.Abs(Vector3.Dot(rel, right)) > 2.0f) return float.MaxValue;
            return along;
        }

        private void ChooseNext()
        {
            var options = new List<int>(4);
            foreach (int e in net.Incident(to))
                if (e != edge && net.CanEnter(e, to)) options.Add(e);

            int chosen;
            if (options.Count == 0) { if (net.CanEnter(edge, to)) chosen = edge; else { nextEdge = -2; return; } }
            else
            {
                // prefer going roughly straight (60 %), otherwise any turn
                chosen = options[Random.Range(0, options.Count)];
                if (Random.value < 0.6f)
                    foreach (int e in options) if (Vector3.Dot(net.Direction(e, to), dir) > 0.9f) { chosen = e; break; }
            }

            nextEdge = chosen; nextTo = net.Other(chosen, to);
            nextDir = net.Direction(chosen, to);
            int lanes = Mathf.Max(1, net.Edge(chosen).lanesPerDirection);
            nextLane = TrafficNetwork.Left(nextDir) * net.LaneOffset(chosen, Mathf.Min(lane, lanes - 1));
        }

        private void BeginTurn()
        {
            Vector3 node = net.NodePos(to);
            p0 = node + laneVec - dir * TurnRadiusLead; p2 = node + nextLane + nextDir * TurnRadiusLead;
            float cross = dir.x * nextDir.z - dir.z * nextDir.x;
            if (Mathf.Abs(cross) > 0.2f)
            {
                Vector3 d = p2 - p0;
                float t = (d.x * nextDir.z - d.z * nextDir.x) / cross;
                p1 = t > 0f && t < 40f ? p0 + dir * t : (p0 + p2) * 0.5f;
            }
            else p1 = (p0 + p2) * 0.5f;
            turnLen = Mathf.Max(4f, ((p0 - p1).magnitude + (p1 - p2).magnitude + 2f * (p2 - p0).magnitude) / 3f);
            u = 0f; turning = true;
        }

        private void EndTurn()
        {
            edge = nextEdge; from = to; to = nextTo; dir = nextDir; laneVec = nextLane;
            lane = Mathf.Min(lane, Mathf.Max(1, net.Edge(edge).lanesPerDirection) - 1);
            s = TurnRadiusLead; turning = false; nextEdge = -1;
            cruise = Mathf.Clamp(net.Edge(edge).speedLimitKmh * 0.75f / 3.6f, 6f, 13f);
        }

        public bool NeedsRecycle => !Parked && nextEdge == -2;
    }
}
