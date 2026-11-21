using System.Collections.Generic;
using UnityEngine;

namespace CityDriver.World
{
    /// <summary>
    /// Draws the real road network into a texture: this is the source image for the full-screen map and the minimap
    /// (the minimap shows a window of it). It is a rendering of RoadGraphData, never a hand-painted picture.
    /// World X/Z maps to texture X/Y with north (+Z) up.
    /// </summary>
    public static class RoadMapRenderer
    {
        public static readonly Color Background = new Color(0.10f, 0.12f, 0.16f);

        public static Color DistrictTint(DistrictType t)
        {
            switch (t)
            {
                case DistrictType.Downtown: return new Color(0.16f, 0.18f, 0.24f);
                case DistrictType.Residential: return new Color(0.14f, 0.19f, 0.17f);
                case DistrictType.Commercial: return new Color(0.19f, 0.17f, 0.15f);
                case DistrictType.Industrial: return new Color(0.17f, 0.16f, 0.16f);
                case DistrictType.Entertainment: return new Color(0.20f, 0.14f, 0.20f);
                case DistrictType.Special: return new Color(0.14f, 0.17f, 0.21f);
                default: return Background;
            }
        }

        public static Color RoadColor(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Highway: return new Color(1.00f, 0.72f, 0.25f);
                case RoadClass.Arterial: return new Color(0.92f, 0.92f, 0.95f);
                case RoadClass.Collector: return new Color(0.70f, 0.74f, 0.80f);
                default: return new Color(0.48f, 0.52f, 0.58f);
            }
        }

        public static Color PoiColor(PoiType t)
        {
            switch (t)
            {
                case PoiType.Hospital: return new Color(1f, 0.3f, 0.3f);
                case PoiType.PoliceStation: return new Color(0.3f, 0.5f, 1f);
                case PoiType.PetrolStation: return new Color(0.3f, 0.9f, 0.4f);
                case PoiType.Garage: case PoiType.RepairWorkshop: return new Color(1f, 0.8f, 0.2f);
                case PoiType.SafeHouse: return new Color(0.8f, 0.4f, 1f);
                default: return new Color(0.9f, 0.9f, 0.9f);
            }
        }

        /// <summary>Renders the full city. <paramref name="route"/> (optional) is drawn on top as a bright polyline.</summary>
        public static Texture2D Render(RoadGraphData graph, int size, Route route = null, bool drawPois = true)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Background;
            float half = MapHalfExtent;
            float scale = size / (half * 2f);

            // District tints.
            foreach (var d in graph.districts)
            {
                Vector2Int a = ToPx(d.min.x, d.min.y, size, half), b = ToPx(d.max.x, d.max.y, size, half);
                Color tint = DistrictTint(d.type);
                for (int y = Mathf.Max(0, Mathf.Min(a.y, b.y)); y <= Mathf.Min(size - 1, Mathf.Max(a.y, b.y)); y++)
                    for (int x = Mathf.Max(0, Mathf.Min(a.x, b.x)); x <= Mathf.Min(size - 1, Mathf.Max(a.x, b.x)); x++) px[y * size + x] = tint;
            }

            // Roads: minor first so major roads sit on top.
            for (int cls = 0; cls <= 3; cls++)
                foreach (var e in graph.edges)
                {
                    if ((int)e.roadClass != cls) continue;
                    Vector3 p0 = graph.nodes[e.from].position, p1 = graph.nodes[e.to].position;
                    DrawLine(px, size, ToPx(p0.x, p0.z, size, half), ToPx(p1.x, p1.z, size, half), RoadColor(e.roadClass), Mathf.Max(1, Mathf.RoundToInt(e.width * scale * 0.9f)));
                }

            if (route != null)
                for (int i = 0; i < route.points.Count - 1; i++)
                {
                    Vector3 p0 = route.points[i], p1 = route.points[i + 1];
                    DrawLine(px, size, ToPx(p0.x, p0.z, size, half), ToPx(p1.x, p1.z, size, half), new Color(0.2f, 0.85f, 1f), Mathf.Max(3, size / 300));
                }

            if (drawPois)
                foreach (var poi in graph.pois)
                    FillSquare(px, size, ToPx(poi.position.x, poi.position.z, size, half), Mathf.Max(3, size / 220), PoiColor(poi.type));

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>World half-extent covered by a map texture (the city plus a margin).</summary>
        public const float MapHalfExtent = CityLayout.HalfSize + 300f;   // generous margin so the minimap never samples past the texture edge near the ring road

        /// <summary>
        /// Transparent overlay with only the GPS route and destination ring, same framing as <see cref="Render"/>.
        /// Re-rendered when the route changes; cheap enough to do at 1024 px.
        /// </summary>
        public static void RenderRouteOverlay(Texture2D target, Route route, Vector3? destination)
        {
            int size = target.width;
            var px = new Color[size * size];                   // all transparent (0,0,0,0)
            float half = MapHalfExtent;
            Color routeColor = new Color(0.20f, 0.85f, 1f, 1f);

            if (route != null)
                for (int i = 0; i < route.points.Count - 1; i++)
                {
                    Vector3 p0 = route.points[i], p1 = route.points[i + 1];
                    DrawLine(px, size, ToPx(p0.x, p0.z, size, half), ToPx(p1.x, p1.z, size, half), routeColor, Mathf.Max(3, size / 220));
                }

            if (destination.HasValue)
            {
                Vector2Int c = ToPx(destination.Value.x, destination.Value.z, size, half);
                int r = Mathf.Max(4, size / 90);
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                    {
                        float d = Mathf.Sqrt(x * x + y * y);
                        if (d > r || d < r * 0.45f) continue;      // ring
                        int xx = c.x + x, yy = c.y + y;
                        if (xx >= 0 && yy >= 0 && xx < size && yy < size) px[yy * size + xx] = new Color(1f, 0.72f, 0.18f, 1f);
                    }
            }

            target.SetPixels(px);
            target.Apply(false);
        }

        public static Vector2Int ToPx(float worldX, float worldZ, int size, float half) =>
            new Vector2Int(Mathf.RoundToInt((worldX + half) / (half * 2f) * (size - 1)), Mathf.RoundToInt((worldZ + half) / (half * 2f) * (size - 1)));

        private static void DrawLine(Color[] px, int size, Vector2Int a, Vector2Int b, Color color, int thickness)
        {
            int dx = Mathf.Abs(b.x - a.x), dy = Mathf.Abs(b.y - a.y), sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1, err = dx - dy;
            int x = a.x, y = a.y, r = thickness / 2;
            while (true)
            {
                FillSquare(px, size, new Vector2Int(x, y), r, color);
                if (x == b.x && y == b.y) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }
        }

        private static void FillSquare(Color[] px, int size, Vector2Int c, int r, Color color)
        {
            for (int y = c.y - r; y <= c.y + r; y++)
                for (int x = c.x - r; x <= c.x + r; x++)
                    if (x >= 0 && y >= 0 && x < size && y < size) px[y * size + x] = color;
        }
    }
}
