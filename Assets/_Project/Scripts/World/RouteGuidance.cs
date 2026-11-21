using System.Collections.Generic;
using UnityEngine;

namespace CityDriver.World
{
    public enum TurnType { Straight, SlightLeft, Left, SharpLeft, SlightRight, Right, SharpRight, UTurn, Arrive }

    public struct TurnPoint
    {
        public float distanceAlongRoute;
        public TurnType type;
        public int nodeId;
    }

    /// <summary>
    /// Follows a <see cref="Route"/> as the player drives: remaining distance, next turn and distance to it, and whether the
    /// player has left the route (the caller then asks the RoadGraph for a new route). No allocations in Update.
    /// </summary>
    public sealed class RouteGuidance
    {
        private const float MinTurnAngle = 25f;

        private readonly List<TurnPoint> turns = new List<TurnPoint>();
        private Route route;
        private int segment;

        public float OffRouteMetres = 30f;
        public float ArriveMetres = 12f;

        public bool HasRoute => route != null && route.points.Count > 1;
        public float DistanceAlongRoute { get; private set; }
        public float DistanceRemaining { get; private set; }
        public float DistanceFromRoute { get; private set; }
        public bool OffRoute { get; private set; }
        public bool Arrived { get; private set; }
        public TurnType NextTurn { get; private set; } = TurnType.Straight;
        public float DistanceToNextTurn { get; private set; }

        public void SetRoute(Route newRoute)
        {
            route = newRoute;
            segment = 0;
            OffRoute = Arrived = false;
            turns.Clear();
            DistanceAlongRoute = 0f;
            if (!HasRoute) return;

            for (int i = 1; i < route.points.Count - 1; i++)
            {
                Vector3 a = Flat(route.points[i] - route.points[i - 1]);
                Vector3 b = Flat(route.points[i + 1] - route.points[i]);
                if (a.sqrMagnitude < 0.01f || b.sqrMagnitude < 0.01f) continue;
                float angle = Vector3.SignedAngle(a, b, Vector3.up);     // + = right turn (clockwise seen from above)
                TurnType type = Classify(angle);
                if (type != TurnType.Straight) turns.Add(new TurnPoint { distanceAlongRoute = route.cumulative[i], type = type, nodeId = -1 });
            }

            turns.Add(new TurnPoint { distanceAlongRoute = route.Length, type = TurnType.Arrive, nodeId = -1 });
            DistanceRemaining = route.Length;
            NextTurn = turns[0].type;
            DistanceToNextTurn = turns[0].distanceAlongRoute;
        }

        public void Update(Vector3 position)
        {
            if (!HasRoute) return;

            // Closest point on the polyline, searching a window around the last segment (avoids jumping to a parallel street).
            float best = float.PositiveInfinity; int bestSeg = segment; float bestAlong = DistanceAlongRoute;
            int last = route.points.Count - 2;
            int from = Mathf.Max(0, segment - 1), to = Mathf.Min(last, segment + 4);
            for (int pass = 0; pass < 2 && float.IsPositiveInfinity(best); pass++)
            {
                if (pass == 1) { from = 0; to = last; }                  // lost: search everywhere
                for (int i = from; i <= to; i++)
                {
                    Vector3 a = route.points[i], b = route.points[i + 1];
                    Vector3 ab = Flat(b - a);
                    float len2 = ab.sqrMagnitude;
                    float t = len2 < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(Flat(position - a), ab) / len2);
                    Vector3 p = a + (b - a) * t;
                    float d = Flat(position - p).magnitude;
                    if (d < best) { best = d; bestSeg = i; bestAlong = route.cumulative[i] + (route.cumulative[i + 1] - route.cumulative[i]) * t; }
                }

                if (pass == 0 && best > OffRouteMetres) best = float.PositiveInfinity; // retry globally before declaring off-route
            }

            segment = bestSeg;
            DistanceAlongRoute = Mathf.Max(DistanceAlongRoute - 5f, bestAlong); // small tolerance, but progress never jumps backwards far
            DistanceFromRoute = best;
            DistanceRemaining = Mathf.Max(0f, route.Length - DistanceAlongRoute);
            OffRoute = best > OffRouteMetres;
            Arrived = DistanceRemaining <= ArriveMetres;

            for (int i = 0; i < turns.Count; i++)
                if (turns[i].distanceAlongRoute > DistanceAlongRoute + 1f)
                {
                    NextTurn = turns[i].type;
                    DistanceToNextTurn = turns[i].distanceAlongRoute - DistanceAlongRoute;
                    return;
                }

            NextTurn = TurnType.Arrive;
            DistanceToNextTurn = DistanceRemaining;
        }

        /// <summary>Short English instruction (localisation hooks in later).</summary>
        public string Instruction()
        {
            string dist = DistanceToNextTurn >= 1000f ? (DistanceToNextTurn / 1000f).ToString("0.0") + " km" : (Mathf.Round(DistanceToNextTurn / 10f) * 10f) + " m";
            switch (NextTurn)
            {
                case TurnType.Arrive: return $"Arrive in {dist}";
                case TurnType.UTurn: return $"Make a U-turn in {dist}";
                case TurnType.Left: case TurnType.SharpLeft: return $"Turn left in {dist}";
                case TurnType.Right: case TurnType.SharpRight: return $"Turn right in {dist}";
                case TurnType.SlightLeft: return $"Bear left in {dist}";
                case TurnType.SlightRight: return $"Bear right in {dist}";
                default: return $"Continue for {dist}";
            }
        }

        private static TurnType Classify(float angle)
        {
            float a = Mathf.Abs(angle);
            if (a < MinTurnAngle) return TurnType.Straight;
            if (a > 150f) return TurnType.UTurn;
            bool right = angle > 0f;
            if (a < 60f) return right ? TurnType.SlightRight : TurnType.SlightLeft;
            if (a < 120f) return right ? TurnType.Right : TurnType.Left;
            return right ? TurnType.SharpRight : TurnType.SharpLeft;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
