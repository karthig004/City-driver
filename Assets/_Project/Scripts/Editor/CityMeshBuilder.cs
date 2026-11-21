using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Accumulates boxes, cylinders, roofs and quads into one mesh (with optional sub-meshes = material slots).
    /// UVs are measured in meters (world position / tile size) so a tiling texture looks consistent on every piece.
    /// </summary>
    internal sealed class CityMeshBuilder
    {
        private enum UvPlane { XZ, XY, ZY }

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<List<int>> triangles = new List<List<int>> { new List<int>() };
        private readonly Vector2 tileMeters;

        /// <summary>World position treated as UV (0,0). Buildings set this to their min corner so windows start at the corner.</summary>
        public Vector3 UvOrigin { get; set; }

        /// <summary>When true, up-facing box faces sample one fixed wall-coloured texel (plain roofs).</summary>
        public bool UniformTopUv { get; set; }

        /// <summary>Material slot (sub-mesh) that following geometry is added to.</summary>
        public int Submesh
        {
            get => current;
            set { current = value; while (triangles.Count <= value) triangles.Add(new List<int>()); }
        }

        private int current;

        public CityMeshBuilder(float tileU, float tileV) { tileMeters = new Vector2(tileU, tileV); }

        public int QuadCount => vertices.Count / 4;
        public int VertexCount => vertices.Count;

        // ------------------------------------------------------------------------------ boxes

        public void AddBox(Vector3 center, Vector3 size) => AddBox(center, size, Quaternion.identity);

        public void AddBox(Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3 h = size * 0.5f;
            AddFace(center, rotation, Vector3.up,      UvPlane.XZ, new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, h.y, h.z),  new Vector3(h.x, h.y, h.z),   new Vector3(h.x, h.y, -h.z));
            AddFace(center, rotation, Vector3.down,    UvPlane.XZ, new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z),  new Vector3(-h.x, -h.y, h.z));
            // Side faces: verified numerically so that cross(v1-v0, v2-v0) equals the outward normal.
            AddFace(center, rotation, Vector3.forward, UvPlane.XY, new Vector3(h.x, -h.y, h.z),  new Vector3(h.x, h.y, h.z),   new Vector3(-h.x, h.y, h.z),  new Vector3(-h.x, -h.y, h.z));
            AddFace(center, rotation, Vector3.back,    UvPlane.XY, new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z),   new Vector3(h.x, -h.y, -h.z));
            AddFace(center, rotation, Vector3.right,   UvPlane.ZY, new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z),  new Vector3(h.x, h.y, h.z),    new Vector3(h.x, -h.y, h.z));
            AddFace(center, rotation, Vector3.left,    UvPlane.ZY, new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z),  new Vector3(-h.x, h.y, -h.z),  new Vector3(-h.x, -h.y, -h.z));
        }

        /// <summary>Box without a bottom face (buildings, containers: the bottom is never seen).</summary>
        public void AddBoxNoBottom(Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            AddFace(center, Quaternion.identity, Vector3.up,      UvPlane.XZ, new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, h.y, h.z),  new Vector3(h.x, h.y, h.z),   new Vector3(h.x, h.y, -h.z));
            AddFace(center, Quaternion.identity, Vector3.forward, UvPlane.XY, new Vector3(h.x, -h.y, h.z),  new Vector3(h.x, h.y, h.z),   new Vector3(-h.x, h.y, h.z),  new Vector3(-h.x, -h.y, h.z));
            AddFace(center, Quaternion.identity, Vector3.back,    UvPlane.XY, new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z),   new Vector3(h.x, -h.y, -h.z));
            AddFace(center, Quaternion.identity, Vector3.right,   UvPlane.ZY, new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z),  new Vector3(h.x, h.y, h.z),    new Vector3(h.x, -h.y, h.z));
            AddFace(center, Quaternion.identity, Vector3.left,    UvPlane.ZY, new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z),  new Vector3(-h.x, h.y, -h.z),  new Vector3(-h.x, -h.y, -h.z));
        }

        // ------------------------------------------------------------------------------ cylinders, roofs, quads

        /// <summary>Upright cylinder with a top cap (side UVs wrap once around, v in meters).</summary>
        public void AddCylinder(Vector3 baseCenter, float radius, float height, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.PI * 2f * i / segments, a1 = Mathf.PI * 2f * (i + 1) / segments;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius), p1 = new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
                Vector3 normal = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0, Mathf.Sin((a0 + a1) * 0.5f));
                float u0 = (float)i / segments * radius * 6.2832f / tileMeters.x, u1 = (float)(i + 1) / segments * radius * 6.2832f / tileMeters.x;
                // Clockwise from outside: bottom p1, top p1, top p0, bottom p0 (checked against the normal below).
                AddRawQuad(baseCenter + p1, baseCenter + p1 + Vector3.up * height, baseCenter + p0 + Vector3.up * height, baseCenter + p0, normal,
                    new Vector2(u1, 0), new Vector2(u1, height / tileMeters.y), new Vector2(u0, height / tileMeters.y), new Vector2(u0, 0));
            }

            Vector3 topCentre = baseCenter + Vector3.up * height;
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.PI * 2f * i / segments, a1 = Mathf.PI * 2f * (i + 1) / segments;
                Vector3 p0 = topCentre + new Vector3(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius), p1 = topCentre + new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
                AddRawQuad(topCentre, p0, p1, p1, Vector3.up, new Vector2(0.02f, 0.5f), new Vector2(0.02f, 0.5f), new Vector2(0.02f, 0.5f), new Vector2(0.02f, 0.5f));
            }
        }

        /// <summary>Triangular-prism roof sitting on a width x depth footprint. Ridge runs along X when <paramref name="ridgeAlongX"/>.</summary>
        public void AddGableRoof(Vector3 baseCenter, float width, float depth, float rise, bool ridgeAlongX, float overhang = 0.4f)
        {
            float w = width + overhang * 2f, d = depth + overhang * 2f;
            Vector3 c = baseCenter;
            Vector3 a = c + new Vector3(-w / 2, 0, -d / 2), b = c + new Vector3(w / 2, 0, -d / 2), cc = c + new Vector3(w / 2, 0, d / 2), dd = c + new Vector3(-w / 2, 0, d / 2);
            if (ridgeAlongX)
            {
                Vector3 r0 = c + new Vector3(-w / 2, rise, 0), r1 = c + new Vector3(w / 2, rise, 0);
                AddSlope(a, b, r1, r0);
                AddSlope(cc, dd, r0, r1);
                AddGableEnd(a, dd, r0, Vector3.left);
                AddGableEnd(b, cc, r1, Vector3.right);
            }
            else
            {
                Vector3 r0 = c + new Vector3(0, rise, -d / 2), r1 = c + new Vector3(0, rise, d / 2);
                AddSlope(dd, a, r0, r1);
                AddSlope(b, cc, r1, r0);
                AddGableEnd(a, b, r0, Vector3.back);
                AddGableEnd(cc, dd, r1, Vector3.forward);
            }
        }

        // Sloped quad (a,b along the eave, c,d along the ridge); winding chosen so the normal faces up/outwards.
        private void AddSlope(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 n = Vector3.Cross(b - a, d - a);
            if (n.y < 0f) n = -n;
            AddRawQuad(a, b, c, d, n.normalized, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
        }

        private void AddGableEnd(Vector3 baseA, Vector3 baseB, Vector3 apex, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(apex - baseA, baseB - baseA).normalized;
            if (Vector3.Dot(n, outward) < 0f) { (baseA, baseB) = (baseB, baseA); n = Vector3.Cross(apex - baseA, baseB - baseA).normalized; }
            int s = vertices.Count;
            vertices.Add(baseA); vertices.Add(baseB); vertices.Add(apex);
            for (int i = 0; i < 3; i++) { normals.Add(outward); uvs.Add(new Vector2(0.02f, 0.5f)); }
            // Triangle winding must have cross(v1-v0, v2-v0) along 'outward'.
            if (Vector3.Dot(Vector3.Cross(baseB - baseA, apex - baseA), outward) >= 0f) triangles[current].AddRange(new[] { s, s + 1, s + 2 });
            else triangles[current].AddRange(new[] { s, s + 2, s + 1 });
        }

        /// <summary>A single quad with explicit UVs (foliage cards, signs). Order a,b,c,d clockwise seen from the front.</summary>
        public void AddCard(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Rect uv)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            AddRawQuad(a, b, c, d, n, new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMax, uv.yMin));
        }

        /// <summary>A single up-facing quad (road paint, decals).</summary>
        public void AddTopQuad(float centerX, float centerZ, float sizeX, float sizeZ, float y)
        {
            float hx = sizeX * 0.5f, hz = sizeZ * 0.5f;
            AddFace(Vector3.zero, Quaternion.identity, Vector3.up, UvPlane.XZ,
                new Vector3(centerX - hx, y, centerZ - hz), new Vector3(centerX - hx, y, centerZ + hz),
                new Vector3(centerX + hx, y, centerZ + hz), new Vector3(centerX + hx, y, centerZ - hz));
        }

        public Mesh ToMesh(string meshName)
        {
            var mesh = new Mesh { name = meshName };
            if (vertices.Count > 60000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Count;
            for (int i = 0; i < triangles.Count; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------------------ internals

        private void AddRawQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 uva, Vector2 uvb, Vector2 uvc, Vector2 uvd)
        {
            // Make the winding agree with the requested normal (cross(v1-v0, v2-v0) must point along it).
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f) { (b, d) = (d, b); (uvb, uvd) = (uvd, uvb); }
            int s = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            uvs.Add(uva); uvs.Add(uvb); uvs.Add(uvc); uvs.Add(uvd);
            triangles[current].AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
        }

        private void AddFace(Vector3 center, Quaternion rotation, Vector3 localNormal, UvPlane plane, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count;
            Vector3 normal = rotation * localNormal;
            foreach (Vector3 local in new[] { a, b, c, d })
            {
                Vector3 world = center + rotation * local;
                vertices.Add(world);
                normals.Add(normal);
                bool flatTop = UniformTopUv && localNormal == Vector3.up;
                uvs.Add(flatTop ? new Vector2(0.02f, 0.5f) : ToUv(world - UvOrigin, plane));
            }

            triangles[current].AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        private Vector2 ToUv(Vector3 p, UvPlane plane)
        {
            switch (plane)
            {
                case UvPlane.XY: return new Vector2(p.x / tileMeters.x, p.y / tileMeters.y);
                case UvPlane.ZY: return new Vector2(p.z / tileMeters.x, p.y / tileMeters.y);
                default: return new Vector2(p.x / tileMeters.x, p.z / tileMeters.y);
            }
        }
    }
}
