using System.Collections.Generic;
using UnityEngine;

namespace CityDriver.Population
{
    /// <summary>
    /// Runtime-generated low-poly cars and pedestrians. Every mesh samples one shared palette texture (one material for the whole
    /// population, so the SRP Batcher can draw them all cheaply). Pedestrians are three pose meshes (stand / step L / step R) that the
    /// agent swaps, which gives a walking animation without skinned meshes. These are stylised procedural models, not realistic ones.
    /// </summary>
    public static class PopulationMeshes
    {
        // palette layout: row 0 = 16 detail colours, row 1 = 16 car paints
        public const int Cols = 32, Rows = 2;
        public const int Skin0 = 0, Hair = 3, Jeans = 4, TrouserGrey = 5, TrouserKhaki = 6, Shirt0 = 7, ShirtCount = 6, Dark = 13, Glass = 14, Light = 15,
            Saffron = 16, Maroon = 17, Teal = 18, Pink = 19, Yellow = 20, Navy = 21, Cream = 22, Leather = 23, Black = 24, GreyHair = 25, SareeRed = 26, SareeGreen = 27, SareeBlue = 28, Gold = 29, SchoolShirt = 30, Beige = 31;
        public const int CarPaintCount = 12;
        public const int PedVariants = 36;

        private static Material material, faceMaterial;
        private static Mesh[] carMeshes;                 // one per paint colour
        private static Mesh[][] pedMeshes;               // [variant][pose]

        /// <summary>Second material for pedestrians: a small painted face (eyes, brows, nose, mouth) in four skin tones.</summary>
        public static Material FaceMaterial { get { if (faceMaterial == null) Build(); return faceMaterial; } }

        public static Material Material
        {
            get { if (material == null) Build(); return material; }
        }

        public static Mesh Car(int paint) { if (carMeshes == null || material == null) Build(); return carMeshes[Mathf.Abs(paint) % carMeshes.Length]; }
        private static Mesh[] propMeshes;
        public const int PropTypes = 6;
        /// <summary>Street props: 0 bin, 1 bench, 2 planter, 3 hawker stall, 4 parked motorbike, 5 shop-sign pole, 6 bus shelter (index 5 and 6 share PropTypes-1 cap).</summary>
        public static Mesh Prop(int type) { if (propMeshes == null || material == null) Build(); return propMeshes[Mathf.Clamp(type, 0, propMeshes.Length - 1)]; }

        private static Mesh[] pursuitMeshes;
        /// <summary>0 = gang SUV (black), 1 = police (red left / blue right light bar), 2 = police (blue left / red right) - swap for flashing.</summary>
        public static Mesh Pursuit(int type) { if (pursuitMeshes == null || material == null) Build(); return pursuitMeshes[Mathf.Clamp(type, 0, 2)]; }

        public static Mesh Pedestrian(int variant, int pose) { if (pedMeshes == null || material == null) Build(); return pedMeshes[Mathf.Abs(variant) % PedVariants][pose]; }

        private static void Build()
        {
            var tex = new Texture2D(Cols, Rows, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "PopulationPalette" };
            Color[] detail = new Color[Cols];
            Color[] baseDetail =
            {
                new Color(0.87f, 0.68f, 0.52f), new Color(0.66f, 0.46f, 0.32f), new Color(0.46f, 0.31f, 0.21f), new Color(0.08f, 0.06f, 0.05f),
                new Color(0.16f, 0.22f, 0.38f), new Color(0.30f, 0.30f, 0.32f), new Color(0.52f, 0.46f, 0.36f),
                new Color(0.75f, 0.20f, 0.18f), new Color(0.20f, 0.38f, 0.72f), new Color(0.92f, 0.92f, 0.90f), new Color(0.22f, 0.55f, 0.30f), new Color(0.90f, 0.55f, 0.15f), new Color(0.55f, 0.30f, 0.62f),
                new Color(0.04f, 0.04f, 0.045f), new Color(0.10f, 0.15f, 0.22f), new Color(0.95f, 0.93f, 0.80f),
                new Color(0.93f, 0.55f, 0.15f), new Color(0.50f, 0.10f, 0.16f), new Color(0.12f, 0.50f, 0.52f), new Color(0.90f, 0.45f, 0.62f),
                new Color(0.95f, 0.82f, 0.25f), new Color(0.10f, 0.14f, 0.32f), new Color(0.94f, 0.91f, 0.84f), new Color(0.40f, 0.26f, 0.15f),
                new Color(0.07f, 0.07f, 0.08f), new Color(0.62f, 0.62f, 0.64f), new Color(0.72f, 0.12f, 0.20f), new Color(0.10f, 0.45f, 0.30f),
                new Color(0.16f, 0.30f, 0.62f), new Color(0.90f, 0.72f, 0.22f), new Color(0.55f, 0.68f, 0.85f), new Color(0.78f, 0.70f, 0.55f),
            };
            for (int i = 0; i < Cols; i++) detail[i] = baseDetail[i];
            Color[] paints = new Color[Cols];
            Color[] basePaints =
            {
                new Color(0.85f, 0.85f, 0.86f), new Color(0.10f, 0.10f, 0.11f), new Color(0.55f, 0.57f, 0.60f), new Color(0.70f, 0.12f, 0.12f),
                new Color(0.12f, 0.25f, 0.55f), new Color(0.92f, 0.92f, 0.90f), new Color(0.80f, 0.62f, 0.12f), new Color(0.18f, 0.42f, 0.28f),
                new Color(0.35f, 0.20f, 0.12f), new Color(0.62f, 0.66f, 0.72f), new Color(0.75f, 0.35f, 0.10f), new Color(0.30f, 0.30f, 0.34f),
            };
            for (int i = 0; i < Cols; i++) paints[i] = i < basePaints.Length ? basePaints[i] : Color.gray;
            for (int i = 0; i < Cols; i++) { tex.SetPixel(i, 0, detail[i]); tex.SetPixel(i, 1, paints[i]); }
            tex.Apply(false, true);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) { Debug.LogError("[Population] No URP Lit shader found; population will not render correctly."); shader = Shader.Find("Sprites/Default"); }
            material = new Material(shader) { name = "M_PopulationPalette", enableInstancing = true };
            material.SetTexture("_BaseMap", tex); material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.35f); material.SetFloat("_Metallic", 0f);

            faceMaterial = new Material(shader) { name = "M_PopulationFace", enableInstancing = true };
            faceMaterial.SetTexture("_BaseMap", BuildFaceAtlas(detail)); faceMaterial.SetColor("_BaseColor", Color.white);
            faceMaterial.SetFloat("_Smoothness", 0.25f); faceMaterial.SetFloat("_Metallic", 0f);

            carMeshes = new Mesh[CarPaintCount];
            for (int i = 0; i < CarPaintCount; i++) carMeshes[i] = BuildCar(i);

            propMeshes = new Mesh[7];
            for (int i = 0; i < propMeshes.Length; i++) propMeshes[i] = BuildProp(i);

            pursuitMeshes = new Mesh[3];
            for (int i = 0; i < 3; i++) pursuitMeshes[i] = BuildPursuit(i);

            pedMeshes = new Mesh[PedVariants][];
            for (int v = 0; v < PedVariants; v++)
            {
                pedMeshes[v] = new Mesh[3];
                for (int pose = 0; pose < 3; pose++) pedMeshes[v][pose] = BuildPed(v, pose);
            }
        }


        // Four 64x64 faces side by side (one per skin tone): eyes with whites and pupils, brows, nose shadow, mouth, light blush.
        private static Texture2D BuildFaceAtlas(Color[] detail)
        {
            const int F = 64, N = 4;
            var tex = new Texture2D(F * N, F, TextureFormat.RGBA32, true, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "PopulationFaces" };
            Color[] tones = { detail[0], detail[1], detail[2], new Color(0.95f, 0.78f, 0.64f) };
            var px = new Color[F * N * F];
            for (int t = 0; t < N; t++)
                for (int y = 0; y < F; y++)
                    for (int x = 0; x < F; x++)
                    {
                        float u = (x + 0.5f) / F, v = (y + 0.5f) / F;
                        Color c = tones[t] * (0.96f + 0.08f * Mathf.PerlinNoise(x * 0.15f + t * 7f, y * 0.15f));
                        c = Color.Lerp(c, tones[t] * 1.12f, Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 2.4f) * 0.25f);               // soft highlight in the centre
                        foreach (float ex in new[] { 0.31f, 0.69f })
                        {
                            float dx = (u - ex) / 0.105f, dy = (v - 0.58f) / 0.062f;
                            if (dx * dx + dy * dy < 1f) c = Color.white * 0.94f;                                                    // eye white
                            float pd = ((u - ex) * (u - ex)) / (0.04f * 0.04f) + ((v - 0.58f) * (v - 0.58f)) / (0.045f * 0.045f);
                            if (pd < 1f) c = new Color(0.07f, 0.05f, 0.04f);                                                        // pupil
                            if (Mathf.Abs(v - 0.74f - 0.06f * (ex < 0.5f ? (u - 0.31f) : -(u - 0.69f)) * 2f) < 0.022f && Mathf.Abs(u - ex) < 0.13f) c = new Color(0.1f, 0.07f, 0.05f);   // brow
                        }
                        if (Mathf.Abs(u - 0.5f) < 0.045f && v > 0.36f && v < 0.52f) c *= 0.88f;                                      // nose shadow
                        if (Mathf.Abs(v - 0.27f) < 0.02f && Mathf.Abs(u - 0.5f) < 0.13f) c = Color.Lerp(c, new Color(0.45f, 0.12f, 0.12f), 0.85f);    // mouth
                        px[y * (F * N) + t * F + x] = c;
                    }
            tex.SetPixels(px); tex.Apply(true, true);
            return tex;
        }

        // ------------------------------------------------------------------ mesh builder with palette UVs
        private sealed class MB
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int> t = new List<int>(); public readonly List<int> t2 = new List<int>();

            public void Box(Vector3 c, Vector3 size, int col, int row, Quaternion? rot = null)
            {
                Quaternion q = rot ?? Quaternion.identity; Vector3 h = size * 0.5f;
                Vector2 u = new Vector2((col + 0.5f) / Cols, (row + 0.5f) / Rows);
                // six faces: normal, right, up
                Vector3[][] f =
                {
                    new[] { Vector3.forward, Vector3.right, Vector3.up }, new[] { Vector3.back, Vector3.left, Vector3.up },
                    new[] { Vector3.right, Vector3.back, Vector3.up }, new[] { Vector3.left, Vector3.forward, Vector3.up },
                    new[] { Vector3.up, Vector3.right, Vector3.back }, new[] { Vector3.down, Vector3.right, Vector3.forward },
                };
                foreach (var face in f)
                {
                    Vector3 nrm = face[0], r = face[1], up = face[2];
                    Vector3 centre = Vector3.Scale(nrm, h);
                    Vector3 rr = Vector3.Scale(r, h), uu = Vector3.Scale(up, h);
                    int b = v.Count;
                    Vector3[] corners = { centre - rr - uu, centre - rr + uu, centre + rr + uu, centre + rr - uu };
                    foreach (var p in corners) { v.Add(c + q * p); n.Add(q * nrm); uv.Add(u); }
                    // winding so the face points along its normal
                    Vector3 cross = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
                    if (Vector3.Dot(cross, nrm) >= 0) { t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3); }
                    else { t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2); }
                }
            }


            // Tapered 'limb' hanging down from a pivot (hip / shoulder), swung about X by swingDeg. Elliptical cross-section.
            public void Limb(Vector3 pivot, float length, float rxTop, float rxBot, float rzTop, float rzBot, float swingDeg, int col, int row, int sides = 8, float lateralDeg = 0f)
            {
                Quaternion q = Quaternion.Euler(swingDeg, 0f, lateralDeg);
                Vector2 u = new Vector2((col + 0.5f) / Cols, (row + 0.5f) / Rows);
                int b = v.Count;
                for (int ring = 0; ring < 2; ring++)
                    for (int k = 0; k < sides; k++)
                    {
                        float a = k * Mathf.PI * 2f / sides;
                        float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                        float rx = ring == 0 ? rxTop : rxBot, rz = ring == 0 ? rzTop : rzBot;
                        Vector3 local = new Vector3(cx * rx, -ring * length, cz * rz);
                        v.Add(pivot + q * local);
                        n.Add((q * new Vector3(cx, 0f, cz)).normalized);
                        uv.Add(u);
                    }
                for (int k = 0; k < sides; k++)
                {
                    int k2 = (k + 1) % sides;
                    int a0 = b + k, a1 = b + k2, b0 = b + sides + k, b1 = b + sides + k2;
                    t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1);
                }
                // bottom cap
                int c = v.Count; v.Add(pivot + q * new Vector3(0f, -length, 0f)); n.Add(q * Vector3.down); uv.Add(u);
                for (int k = 0; k < sides; k++) { int k2 = (k + 1) % sides; t.Add(c); t.Add(b + sides + k2); t.Add(b + sides + k); }
            }

            public void Ellipsoid(Vector3 centre, Vector3 radii, int col, int row, int segs = 8, int rings = 5)
            {
                Vector2 u = new Vector2((col + 0.5f) / Cols, (row + 0.5f) / Rows);
                int b = v.Count;
                for (int r = 0; r <= rings; r++)
                {
                    float phi = Mathf.PI * r / rings;
                    for (int k = 0; k <= segs; k++)
                    {
                        float th = 2f * Mathf.PI * k / segs;
                        Vector3 dir = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                        v.Add(centre + Vector3.Scale(dir, radii)); n.Add(dir); uv.Add(u);
                    }
                }
                int row2 = segs + 1;
                for (int r = 0; r < rings; r++)
                    for (int k = 0; k < segs; k++)
                    {
                        int i0 = b + r * row2 + k, i1 = i0 + 1, i2 = i0 + row2, i3 = i2 + 1;
                        t.Add(i0); t.Add(i1); t.Add(i2); t.Add(i1); t.Add(i3); t.Add(i2);
                    }
            }

            /// <summary>A flat quad (facing +Z) textured from the face atlas, drawn with the second material.</summary>
            public void FaceQuad(Vector3 centre, float w, float h, int tone)
            {
                int b = v.Count; float u0 = tone / 4f, u1 = (tone + 1) / 4f;
                v.Add(centre + new Vector3(-w / 2, -h / 2, 0)); v.Add(centre + new Vector3(-w / 2, h / 2, 0)); v.Add(centre + new Vector3(w / 2, h / 2, 0)); v.Add(centre + new Vector3(w / 2, -h / 2, 0));
                for (int i = 0; i < 4; i++) n.Add(Vector3.forward);
                uv.Add(new Vector2(u1, 0)); uv.Add(new Vector2(u1, 1)); uv.Add(new Vector2(u0, 1)); uv.Add(new Vector2(u0, 0));
                t2.Add(b); t2.Add(b + 1); t2.Add(b + 2); t2.Add(b); t2.Add(b + 2); t2.Add(b + 3);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
                if (t2.Count > 0) { m.subMeshCount = 2; m.SetTriangles(t, 0); m.SetTriangles(t2, 1); } else m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        private static Mesh BuildPursuit(int type)
        {
            var b = new MB();
            bool police = type != 0;
            int paint = police ? 5 : 1;                                                                          // white police car / black gang SUV
            float tall = police ? 1f : 1.12f;
            b.Box(new Vector3(0, 0.62f * tall, 0), new Vector3(1.86f, 0.55f * tall, 4.3f), paint, 1);
            b.Box(new Vector3(0, 1.12f * tall, -0.25f), new Vector3(1.6f, 0.44f * tall, 2.1f), Glass, 0);
            b.Box(new Vector3(0, 1.36f * tall, -0.25f), new Vector3(1.56f, 0.06f, 1.85f), paint, 1);
            b.Box(new Vector3(0, 0.40f, 2.17f), new Vector3(1.76f, 0.22f, 0.12f), Dark, 0);
            b.Box(new Vector3(0, 0.40f, -2.17f), new Vector3(1.76f, 0.22f, 0.12f), Dark, 0);
            b.Box(new Vector3(-0.6f, 0.72f * tall, 2.16f), new Vector3(0.4f, 0.12f, 0.06f), Light, 0);
            b.Box(new Vector3(0.6f, 0.72f * tall, 2.16f), new Vector3(0.4f, 0.12f, 0.06f), Light, 0);
            b.Box(new Vector3(-0.6f, 0.8f * tall, -2.16f), new Vector3(0.4f, 0.12f, 0.06f), Shirt0, 0);
            b.Box(new Vector3(0.6f, 0.8f * tall, -2.16f), new Vector3(0.4f, 0.12f, 0.06f), Shirt0, 0);
            foreach (float x in new[] { -0.9f, 0.9f })
                foreach (float z in new[] { 1.32f, -1.32f })
                    b.Box(new Vector3(x, 0.34f, z), new Vector3(0.24f, 0.68f, 0.68f), Dark, 0);
            if (police)
            {
                int left = type == 1 ? Shirt0 : Shirt0 + 1, right = type == 1 ? Shirt0 + 1 : Shirt0;          // palette: Shirt0 = red, Shirt0+1 = blue
                b.Box(new Vector3(-0.34f, 1.46f, -0.25f), new Vector3(0.6f, 0.13f, 0.26f), left, 0);
                b.Box(new Vector3(0.34f, 1.46f, -0.25f), new Vector3(0.6f, 0.13f, 0.26f), right, 0);
                b.Box(new Vector3(0, 0.66f, 0f), new Vector3(1.88f, 0.12f, 4.32f), Dark, 0);                // black stripe along the doors
            }
            else b.Box(new Vector3(0, 1.0f * tall, 2.18f), new Vector3(0.9f, 0.24f, 0.05f), Dark, 0);       // gang grille bar
            return b.ToMesh("PursuitCar_" + type);
        }

        // Car: centred on its footprint, wheels on y=0, length along +Z. About 4.2 m x 1.8 m x 1.45 m.
        private static Mesh BuildCar(int paint)
        {
            var b = new MB();
            b.Box(new Vector3(0, 0.62f, 0), new Vector3(1.80f, 0.55f, 4.20f), paint, 1);                       // lower body
            b.Box(new Vector3(0, 1.10f, -0.25f), new Vector3(1.56f, 0.42f, 2.05f), Glass, 0);                  // greenhouse glass
            b.Box(new Vector3(0, 1.33f, -0.25f), new Vector3(1.52f, 0.06f, 1.80f), paint, 1);                  // roof
            b.Box(new Vector3(0, 0.40f, 2.12f), new Vector3(1.70f, 0.20f, 0.12f), Dark, 0);                    // front bumper
            b.Box(new Vector3(0, 0.40f, -2.12f), new Vector3(1.70f, 0.20f, 0.12f), Dark, 0);                   // rear bumper
            b.Box(new Vector3(-0.58f, 0.70f, 2.11f), new Vector3(0.38f, 0.12f, 0.06f), Light, 0);             // headlights
            b.Box(new Vector3(0.58f, 0.70f, 2.11f), new Vector3(0.38f, 0.12f, 0.06f), Light, 0);
            b.Box(new Vector3(-0.60f, 0.78f, -2.11f), new Vector3(0.40f, 0.12f, 0.06f), Shirt0, 0);            // tail lights (red)
            b.Box(new Vector3(0.60f, 0.78f, -2.11f), new Vector3(0.40f, 0.12f, 0.06f), Shirt0, 0);
            foreach (float x in new[] { -0.86f, 0.86f })
                foreach (float z in new[] { 1.30f, -1.30f })
                    b.Box(new Vector3(x, 0.32f, z), new Vector3(0.22f, 0.64f, 0.64f), Dark, 0);                 // wheels (blocks)
            return b.ToMesh("TrafficCar_" + paint);
        }


        private static Mesh BuildProp(int type)
        {
            var b = new MB();
            switch (type)
            {
                case 0:   // litter bin
                    b.Limb(new Vector3(0, 0.85f, 0), 0.8f, 0.2f, 0.17f, 0.2f, 0.17f, 0f, 10, 0, 10);
                    b.Box(new Vector3(0, 0.88f, 0), new Vector3(0.46f, 0.07f, 0.46f), Dark, 0);
                    break;
                case 1:   // bench
                    b.Box(new Vector3(0, 0.46f, 0), new Vector3(1.7f, 0.06f, 0.5f), Leather, 0);
                    b.Box(new Vector3(0, 0.78f, -0.22f), new Vector3(1.7f, 0.4f, 0.05f), Leather, 0);
                    b.Box(new Vector3(-0.75f, 0.22f, 0), new Vector3(0.06f, 0.44f, 0.46f), Dark, 0);
                    b.Box(new Vector3(0.75f, 0.22f, 0), new Vector3(0.06f, 0.44f, 0.46f), Dark, 0);
                    break;
                case 2:   // planter with a bushy plant
                    b.Limb(new Vector3(0, 0.55f, 0), 0.55f, 0.36f, 0.26f, 0.36f, 0.26f, 0f, Leather, 0, 10);
                    b.Ellipsoid(new Vector3(0, 0.95f, 0), new Vector3(0.5f, 0.42f, 0.5f), 10, 0, 9, 6);
                    b.Ellipsoid(new Vector3(0.18f, 1.2f, 0.05f), new Vector3(0.3f, 0.28f, 0.3f), 10, 0, 8, 5);
                    break;
                case 3:   // hawker stall: counter, poles, striped awning
                {
                    b.Box(new Vector3(0, 0.55f, 0), new Vector3(2.1f, 0.9f, 1.0f), Cream, 0);
                    b.Box(new Vector3(0, 1.03f, 0.05f), new Vector3(2.2f, 0.06f, 1.1f), Leather, 0);
                    foreach (float x in new[] { -1.0f, 1.0f }) { b.Box(new Vector3(x, 1.5f, -0.5f), new Vector3(0.06f, 1.8f, 0.06f), Dark, 0); b.Box(new Vector3(x, 1.5f, 0.5f), new Vector3(0.06f, 1.8f, 0.06f), Dark, 0); }
                    for (int i = 0; i < 8; i++) b.Box(new Vector3(-0.98f + i * 0.28f, 2.45f - 0.0f, 0.0f), new Vector3(0.28f, 0.05f, 1.35f), i % 2 == 0 ? Saffron : Cream, 0, Quaternion.Euler(10f, 0, 0));
                    b.Box(new Vector3(-0.5f, 1.14f, 0.1f), new Vector3(0.4f, 0.16f, 0.4f), SareeRed, 0);
                    b.Box(new Vector3(0.3f, 1.14f, 0.1f), new Vector3(0.5f, 0.16f, 0.4f), Gold, 0);
                    break;
                }
                case 4:   // parked motorbike
                    b.Box(new Vector3(0, 0.55f, 0), new Vector3(0.28f, 0.28f, 1.0f), Maroon, 0);
                    b.Box(new Vector3(0, 0.78f, -0.1f), new Vector3(0.3f, 0.1f, 0.5f), Black, 0);
                    b.Box(new Vector3(0, 0.85f, 0.45f), new Vector3(0.7f, 0.04f, 0.05f), Dark, 0);
                    b.Box(new Vector3(0, 0.45f, 0.52f), new Vector3(0.06f, 0.55f, 0.06f), Dark, 0, Quaternion.Euler(-18f, 0, 0));
                    b.Box(new Vector3(0, 0.3f, 0.78f), new Vector3(0.12f, 0.6f, 0.6f), Dark, 0);
                    b.Box(new Vector3(0, 0.3f, -0.78f), new Vector3(0.12f, 0.6f, 0.6f), Dark, 0);
                    b.Box(new Vector3(0, 0.55f, 0.74f), new Vector3(0.2f, 0.12f, 0.12f), Light, 0);
                    break;
                case 5:   // shop sign on a pole
                    b.Box(new Vector3(0, 1.3f, 0), new Vector3(0.1f, 2.6f, 0.1f), Dark, 0);
                    b.Box(new Vector3(0, 2.35f, 0.07f), new Vector3(1.5f, 0.7f, 0.08f), new[] { Teal, Maroon, Navy, Saffron }[0], 0);
                    b.Box(new Vector3(0, 2.35f, 0.115f), new Vector3(1.3f, 0.5f, 0.02f), Cream, 0);
                    b.Box(new Vector3(0, 2.35f, 0.13f), new Vector3(0.9f, 0.1f, 0.02f), Dark, 0);
                    b.Box(new Vector3(0, 2.2f, 0.13f), new Vector3(0.6f, 0.08f, 0.02f), SareeRed, 0);
                    break;
                default:  // bus shelter
                    b.Box(new Vector3(0, 2.55f, 0), new Vector3(3.2f, 0.1f, 1.5f), Dark, 0);
                    foreach (float x in new[] { -1.5f, 1.5f }) b.Box(new Vector3(x, 1.25f, -0.65f), new Vector3(0.08f, 2.5f, 0.08f), Dark, 0);
                    b.Box(new Vector3(0, 1.4f, -0.72f), new Vector3(3.0f, 1.9f, 0.04f), Glass, 0);
                    b.Box(new Vector3(0, 0.5f, -0.45f), new Vector3(2.4f, 0.07f, 0.45f), Leather, 0);
                    b.Box(new Vector3(1.2f, 1.5f, 0.1f), new Vector3(0.05f, 1.2f, 0.8f), Maroon, 0);
                    break;
            }
            return b.ToMesh("Prop_" + type);
        }

        // Pedestrian (~1.5-1.8 m): rounded limbs, ellipsoid head with hair, shoes, and six clothing types suited to an Indian city
        // (casual, formal, kurta, saree, shorts, school uniform). pose 0 = standing, 1 = left foot forward, 2 = right foot forward.
        private static Mesh BuildPed(int variant, int pose)
        {
            var rnd = new System.Random(variant * 7919 + 13);
            int type = variant % 6, look = variant / 6;                        // look 0..5 picks colours inside the type
            int skin = (variant + look) % 3;
            float height = 0.93f + 0.14f * (float)rnd.NextDouble();           // 0.93 .. 1.07
            bool female = type == 3 || (type == 0 && variant % 4 == 1);
            if (type == 5) height = 0.80f;                                     // school child
            float swing = pose == 0 ? 0f : (pose == 1 ? 24f : -24f);
            var b = new MB();

            int shirt = Shirt0 + look % ShirtCount, trousers = new[] { Jeans, TrouserGrey, TrouserKhaki, Navy }[look % 4];
            int shoes = type == 1 ? Black : (look % 2 == 0 ? Leather : Black);
            int hair = look == 5 && type == 2 ? GreyHair : Hair;
            float shoulderY = 1.40f, hipY = 0.90f, hipX = 0.095f;

            // legs: trousers (or skin for shorts), shoes
            bool shorts = type == 4 || type == 5;
            foreach (int side in new[] { -1, 1 })
            {
                float sw = swing * side;
                if (type != 3)                                                  // saree wearers have a long skirt instead of visible legs
                {
                    b.Limb(new Vector3(side * hipX, hipY, 0f), 0.84f, 0.085f, 0.055f, 0.095f, 0.06f, sw, shorts ? skin : trousers, 0);
                    if (shorts) b.Limb(new Vector3(side * hipX, hipY, 0f), 0.36f, 0.092f, 0.082f, 0.102f, 0.09f, sw, trousers, 0);
                }
                float ang = sw * Mathf.Deg2Rad;
                Vector3 foot = new Vector3(side * hipX, hipY, 0f) + new Vector3(0f, -0.84f * Mathf.Cos(ang), 0.84f * Mathf.Sin(ang));
                b.Box(foot + new Vector3(0f, 0.035f, 0.05f), new Vector3(0.105f, 0.075f, 0.26f), shoes, 0, Quaternion.Euler(-sw * 0.3f, 0f, 0f));
            }

            // torso + clothing
            if (type == 2) b.Limb(new Vector3(0f, shoulderY + 0.02f, 0f), 0.78f, 0.19f, 0.20f, 0.12f, 0.13f, 0f, new[] { Saffron, Maroon, Teal, Cream, Pink, Navy }[look % 6], 0, 10);     // kurta
            else if (type == 3) b.Limb(new Vector3(0f, hipY + 0.06f, 0f), 0.86f, 0.17f, 0.33f, 0.12f, 0.27f, swing * 0.12f, new[] { SareeRed, SareeGreen, SareeBlue, Maroon, Pink, Teal }[look % 6], 0, 10);   // saree drape
            else b.Limb(new Vector3(0f, shoulderY, 0f), 0.52f, 0.20f, female ? 0.16f : 0.18f, 0.115f, 0.10f, 0f, shirt, 0, 10);
            if (type == 3) b.Limb(new Vector3(0f, shoulderY, 0f), 0.34f, 0.19f, 0.15f, 0.11f, 0.10f, 0f, Gold, 0, 10);                                                                           // blouse
            if (type == 1) b.Box(new Vector3(0f, 1.18f, 0.105f), new Vector3(0.05f, 0.30f, 0.02f), Navy, 0);                                                                                       // tie

            // arms: sleeve then forearm, hand
            foreach (int side in new[] { -1, 1 })
            {
                float sw = -swing * 0.8f * side;
                Vector3 shoulder = new Vector3(side * 0.255f, shoulderY - 0.03f, 0f);
                int sleeve = type == 2 ? new[] { Saffron, Maroon, Teal, Cream, Pink, Navy }[look % 6] : type == 3 ? Gold : shirt;
                b.Limb(shoulder, 0.28f, 0.052f, 0.046f, 0.052f, 0.046f, sw, sleeve, 0, 7);
                float a = sw * Mathf.Deg2Rad;
                Vector3 elbow = shoulder + new Vector3(0f, -0.28f * Mathf.Cos(a), 0.28f * Mathf.Sin(a));
                bool shortSleeve = type == 4 || type == 5 || type == 0 && look % 2 == 0;
                b.Limb(elbow, 0.27f, 0.044f, 0.036f, 0.044f, 0.036f, sw - 6f, shortSleeve || type == 3 ? skin : sleeve, 0, 7);
                float a2 = (sw - 6f) * Mathf.Deg2Rad;
                Vector3 hand = elbow + new Vector3(0f, -0.28f * Mathf.Cos(a2), 0.28f * Mathf.Sin(a2));
                b.Ellipsoid(hand, new Vector3(0.038f, 0.05f, 0.04f), skin, 0, 6, 4);
            }

            // neck, head, hair
            b.Limb(new Vector3(0f, shoulderY + 0.10f, 0f), 0.09f, 0.05f, 0.055f, 0.05f, 0.055f, 0f, skin, 0, 7);
            b.Ellipsoid(new Vector3(0f, shoulderY + 0.23f, 0f), new Vector3(0.095f, 0.12f, 0.105f), skin, 0, 9, 6);
            int style = (variant * 3 + look) % 6;
            b.FaceQuad(new Vector3(0f, shoulderY + 0.232f, 0.1075f), 0.145f, 0.165f, Mathf.Min(skin, 3));
            if (type == 2 && style % 2 == 0)                                                                                                    // turban (kurta wearers)
                b.Ellipsoid(new Vector3(0f, shoulderY + 0.275f, -0.005f), new Vector3(0.118f, 0.098f, 0.125f), new[] { Cream, Maroon, Saffron }[look % 3], 0, 9, 5);
            else if (style == 4 && !female)                                                                                                     // cap
            {
                b.Ellipsoid(new Vector3(0f, shoulderY + 0.285f, -0.01f), new Vector3(0.108f, 0.07f, 0.115f), new[] { Navy, Maroon, TrouserGrey }[look % 3], 0, 9, 4);
                b.Box(new Vector3(0f, shoulderY + 0.262f, 0.1f), new Vector3(0.12f, 0.012f, 0.08f), Navy, 0);
            }
            else
            {
                b.Ellipsoid(new Vector3(0f, shoulderY + 0.262f, -0.02f), new Vector3(0.105f, 0.10f, 0.112f), hair, 0, 9, 5);                  // hair cap, set back so the forehead shows
                if (style == 1 && !female) b.Ellipsoid(new Vector3(0.03f, shoulderY + 0.295f, 0.035f), new Vector3(0.075f, 0.045f, 0.06f), hair, 0, 7, 4);     // swept fringe
            }
            if (female)
            {
                if (style % 3 == 0) b.Ellipsoid(new Vector3(0f, shoulderY + 0.30f, -0.115f), new Vector3(0.05f, 0.05f, 0.05f), hair, 0, 7, 4);   // bun
                else b.Limb(new Vector3(0f, shoulderY + 0.25f, -0.095f), style % 3 == 1 ? 0.40f : 0.28f, 0.085f, 0.07f, 0.045f, 0.035f, 6f, hair, 0, 8);   // long hair / plait
            }

            // a shoulder bag on some people
            if (type == 0 && look % 3 == 1) b.Box(new Vector3(0.30f, 0.98f, -0.02f), new Vector3(0.09f, 0.22f, 0.28f), Leather, 0, Quaternion.Euler(0f, 0f, -6f));
            if (type == 5) b.Box(new Vector3(0f, 1.12f, -0.17f), new Vector3(0.26f, 0.34f, 0.12f), new[] { Maroon, Teal, SareeBlue }[look % 3], 0);   // school bag

            Mesh m = b.ToMesh($"Ped_{variant}_{pose}");
            // scale about the feet for height variety
            var verts = m.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] *= height;
            m.vertices = verts; m.RecalculateBounds();
            return m;
        }
    }
}
