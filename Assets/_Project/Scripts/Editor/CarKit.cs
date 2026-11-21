using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityDriver.EditorTools
{
    /// <summary>
    /// Procedural car visuals (no licensed model, so nothing here is a third-party asset). The body is a lofted shell (hood, cabin, boot,
    /// tumblehome) with separate paint / glass / roof submeshes, plus grille, bumpers, lamps, mirrors, door cuts, spoilers, roof racks and
    /// wheels. <see cref="CarShape"/> parameters give each of the 8 cars its own silhouette. Only visuals: the Rigidbody, colliders and
    /// WheelColliders are built elsewhere and untouched.
    /// </summary>
    internal static class CarKit
    {
        public const string MeshDir = "Assets/_Project/Art/Vehicles";
        private static CarShape S = new CarShape();

        internal sealed class Mats { public Material paint, glass, trim, chrome, tyre, rim, head, tail, plate, disc, rimDark, accent, barA, barB; }

        // ---------------------------------------------------------------- body profile
        private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        private static float HalfWidth(float z)
        {
            float a = Mathf.Abs(z);
            return a < S.flatZ ? S.halfW : S.halfW - 0.15f * Mathf.Pow((a - S.flatZ) / Mathf.Max(0.1f, S.half - S.flatZ), 2f);
        }

        private static float Deck(float z)         // top surface height of the lower body
        {
            if (z > S.hoodStart) return Mathf.Lerp(S.belt, S.noseH, Smooth((z - S.hoodStart) / (S.half - S.hoodStart)));          // hood falls to the nose
            if (z < S.trunkStart) return Mathf.Lerp(S.belt, S.tailH, Smooth((S.trunkStart - z) / (S.half + S.trunkStart)));       // boot falls to the tail
            return S.belt;
        }

        private static float Roof(float z)         // greenhouse roof height
        {
            if (z > S.flatFront) return Mathf.Lerp(S.roofTop, S.belt, Smooth((z - S.flatFront) / (S.baseFront - S.flatFront)));    // raked windscreen
            if (z < S.flatRear) return Mathf.Lerp(S.roofTop, S.belt, Smooth((S.flatRear - z) / (S.flatRear - S.baseRear)));        // rear window
            return S.roofTop;
        }

        // ---------------------------------------------------------------- loft helper
        private sealed class Loft
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<int>[] tris;
            public Loft(int submeshes) { tris = new List<int>[submeshes]; for (int i = 0; i < submeshes; i++) tris[i] = new List<int>(); }

            public int AddRing(IList<Vector3> ring) { int start = v.Count; v.AddRange(ring); return start; }

            // Outward-facing triangle: flips the winding when the face normal points toward 'inside'.
            public void Tri(int sub, int a, int b, int c, Vector3 inside)
            {
                Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                Vector3 centroid = (v[a] + v[b] + v[c]) / 3f;
                if (Vector3.Dot(n, centroid - inside) < 0f) { int t = b; b = c; c = t; }
                tris[sub].Add(a); tris[sub].Add(b); tris[sub].Add(c);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt16 };
                m.SetVertices(v);
                m.subMeshCount = tris.Length;
                for (int i = 0; i < tris.Length; i++) m.SetTriangles(tris[i], i);
                var uv = new Vector2[v.Count];
                for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(v[i].x * 0.5f, v[i].z * 0.25f);
                m.uv = uv;
                m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }
        }

        private static Mesh BuildLowerBody()
        {
            var loft = new Loft(1);
            const int N = 9;
            var starts = new List<int>();
            var zs = new List<float>();
            float Yb = S.yb, Half = S.half, mid = (Yb + S.belt) * 0.5f;
            int steps = Mathf.CeilToInt(2f * Half / 0.19f);
            for (int i = 0; i <= steps; i++)
            {
                float zc = Mathf.Lerp(-Half, Half, i / (float)steps);
                float w = HalfWidth(zc), wt = w - 0.07f, hb = Deck(zc), cr = 0.03f;
                var ring = new[]
                {
                    new Vector3(-w + 0.10f, Yb, zc), new Vector3(-w, Yb + 0.14f, zc), new Vector3(-w, hb - 0.12f, zc),
                    new Vector3(-wt, hb, zc), new Vector3(0, hb + cr, zc), new Vector3(wt, hb, zc),
                    new Vector3(w, hb - 0.12f, zc), new Vector3(w, Yb + 0.14f, zc), new Vector3(w - 0.10f, Yb, zc),
                };
                starts.Add(loft.AddRing(ring)); zs.Add(zc);
            }

            for (int i = 0; i < starts.Count - 1; i++)
            {
                Vector3 inside = new Vector3(0, mid, (zs[i] + zs[i + 1]) * 0.5f);
                for (int k = 0; k < N; k++)
                {
                    int a = starts[i] + k, b = starts[i] + (k + 1) % N, c = starts[i + 1] + k, d = starts[i + 1] + (k + 1) % N;
                    loft.Tri(0, a, b, c, inside); loft.Tri(0, b, d, c, inside);
                }
            }

            // End caps (fan from the ring centre) so the shell is closed.
            foreach (int end in new[] { 0, starts.Count - 1 })
            {
                Vector3 centre = new Vector3(0, mid, zs[end]);
                int c = loft.v.Count; loft.v.Add(centre);
                Vector3 inside = new Vector3(0, mid, end == 0 ? zs[end] + 1f : zs[end] - 1f);
                for (int k = 0; k < N; k++) loft.Tri(0, starts[end] + k, starts[end] + (k + 1) % N, c, inside);
            }
            return loft.ToMesh(S.id + "_Body");
        }

        private static Mesh BuildGreenhouse()
        {
            var loft = new Loft(2);                // 0 = glass, 1 = roof panel
            var starts = new List<int>(); var zs = new List<float>(); var roofH = new List<float>();
            int steps = Mathf.Max(8, Mathf.CeilToInt((S.baseFront - S.baseRear) / 0.095f));
            float centreZ = (S.baseFront + S.baseRear) * 0.5f, span = (S.baseFront - S.baseRear) * 0.5f;
            float rise = Mathf.Max(0.2f, S.roofTop - S.belt);
            for (int i = 0; i <= steps; i++)
            {
                float z = Mathf.Lerp(S.baseRear, S.baseFront, i / (float)steps);
                float hr = Roof(z);
                float wBase = S.cabW - 0.04f * Mathf.Clamp01((Mathf.Abs(z - centreZ) - span * 0.78f) / 0.4f);
                float k = Mathf.Clamp01((hr - S.belt) / rise);
                float wRoof = Mathf.Lerp(wBase, S.roofW, k);
                float yBase = S.belt - 0.02f;
                var ring = new[]
                {
                    new Vector3(-wBase, yBase, z), new Vector3(-wRoof, hr, z), new Vector3(wRoof, hr, z), new Vector3(wBase, yBase, z),
                };
                starts.Add(loft.AddRing(ring)); zs.Add(z); roofH.Add(hr);
            }

            float flatLimit = S.roofTop - 0.005f;
            for (int i = 0; i < starts.Count - 1; i++)
            {
                float ym = (roofH[i] + roofH[i + 1]) * 0.25f + S.belt * 0.5f;
                Vector3 inside = new Vector3(0, ym, (zs[i] + zs[i + 1]) * 0.5f);
                bool flatRoof = roofH[i] > flatLimit && roofH[i + 1] > flatLimit;
                for (int e = 0; e < 3; e++)
                {
                    int sub = (e == 1 && flatRoof) ? 1 : 0;
                    int a = starts[i] + e, b = starts[i] + e + 1, c = starts[i + 1] + e, d = starts[i + 1] + e + 1;
                    loft.Tri(sub, a, b, c, inside); loft.Tri(sub, b, d, c, inside);
                }
            }
            return loft.ToMesh(S.id + "_Greenhouse");
        }

        // ---------------------------------------------------------------- assembly
        public static void AddBody(Transform parent, Mats m) => AddBody(parent, m, new CarShape());

        public static void AddBody(Transform parent, Mats m, CarShape shape)
        {
            S = shape;
            float lz = S.half / 2.28f, wx = S.halfW / 0.93f;
            float dyN = S.noseH - 0.76f, dyT = S.tailH - 0.84f;
            CityAssetFactory.EnsureFolder(MeshDir);
            BeginBatch();
            Mesh lower = CityAssetFactory.SaveMesh(BuildLowerBody(), $"{MeshDir}/{S.id}_Body.asset");
            Mesh house = CityAssetFactory.SaveMesh(BuildGreenhouse(), $"{MeshDir}/{S.id}_Greenhouse.asset");
            Mesh(parent, "Shell", lower, new[] { m.paint });
            Mesh(parent, "Greenhouse", house, new[] { m.glass, m.paint });

            float cabMidZ = (S.flatFront + S.flatRear) * 0.5f, gh = Mathf.Max(0.25f, S.roofTop - S.belt);
            foreach (int s in new[] { -1, 1 })
            {
                // Pillars, window frame and door cuts (dark trim)
                Box(parent, "BPillar", new Vector3(s * (S.cabW - 0.075f), S.belt + gh * 0.5f, cabMidZ - 0.22f), new Vector3(0.05f, gh * 0.9f, 0.085f), m.trim, new Vector3(0, 0, -s * 8f));
                Box(parent, "APillar", new Vector3(s * (S.cabW - 0.14f), S.belt + gh * 0.5f - 0.02f, S.flatFront + (S.baseFront - S.flatFront) * 0.45f), new Vector3(0.05f, gh * 0.95f, 0.06f),
                    m.trim, new Vector3(-Mathf.Clamp(Mathf.Atan2(S.baseFront - S.flatFront, gh) * Mathf.Rad2Deg, 25f, 62f), 0, -s * 9f));
                float[] doorZ = S.doors == 4 ? new[] { 0.78f, 0.12f, -0.56f, -1.38f } : new[] { 0.95f, -0.25f, -1.40f };
                foreach (float z in doorZ)
                    Box(parent, "DoorCut", new Vector3(s * (S.halfW + 0.002f), S.yb + (S.belt - S.yb) * 0.55f, z * lz), new Vector3(0.012f, (S.belt - S.yb) * 0.75f, 0.02f), m.trim);
                Box(parent, "Sill", new Vector3(s * (S.halfW - 0.005f), S.yb + 0.10f, -0.15f * lz), new Vector3(0.03f, S.cladding ? 0.22f : 0.10f, 2.9f * lz), m.trim);
                foreach (float z in S.doors == 4 ? new[] { 0.34f, -0.40f } : new[] { 0.20f })
                    Box(parent, "DoorHandle", new Vector3(s * (S.halfW + 0.01f), S.belt - 0.09f, z * lz), new Vector3(0.03f, 0.035f, 0.17f), m.chrome);

                // wing mirrors
                float mz = S.baseFront - 0.38f;
                Box(parent, "MirrorStalk", new Vector3(s * (S.halfW - 0.03f), S.belt + 0.03f, mz), new Vector3(0.12f, 0.03f, 0.04f), m.trim);
                Box(parent, "Mirror", new Vector3(s * (S.halfW + 0.07f), S.belt + 0.07f, mz - 0.02f), new Vector3(0.07f, 0.11f, 0.17f), m.paint, new Vector3(0, s * 12f, 0));

                // headlights (lens + dark surround) and tail lights
                float hx = 0.55f * wx;
                Box(parent, "HeadlightSurround", new Vector3(s * hx, 0.66f + dyN, S.half - 0.11f), new Vector3(0.42f * wx, 0.17f, 0.14f), m.trim);
                Lamp(parent, "Headlight" + s, new Vector3(s * hx, 0.67f + dyN, S.half - 0.065f), new Vector3(S.spoiler == 2 ? 0.40f : 0.36f, S.spoiler == 2 ? 0.08f : 0.12f, 0.08f), m.head);
                Box(parent, "FogLight", new Vector3(s * 0.60f * wx, S.yb + 0.10f, S.half - 0.01f), new Vector3(0.14f, 0.07f, 0.05f), m.head);
                Lamp(parent, "Taillight" + s, new Vector3(s * hx, 0.84f + dyT, -S.half + 0.065f), new Vector3(0.40f * wx, 0.13f, 0.08f), m.tail);
                Lamp(parent, "ReverseLamp", new Vector3(s * 0.30f * wx, 0.84f + dyT, -S.half + 0.03f), new Vector3(0.14f, 0.07f, 0.05f), m.head);
            }

            // Front: grille (chrome frame, dark mesh), lower intake, bumper
            float gw = S.bigGrille ? 0.98f : 0.78f, gwn = gw - 0.06f;
            Box(parent, "GrilleFrame", new Vector3(0, 0.62f + dyN, S.half - 0.015f), new Vector3(gw, S.bigGrille ? 0.24f : 0.19f, 0.05f), m.chrome);
            Box(parent, "Grille", new Vector3(0, 0.62f + dyN, S.half + 0.005f), new Vector3(gwn, S.bigGrille ? 0.19f : 0.14f, 0.04f), m.trim);
            for (int i = -2; i <= 2; i++) Box(parent, "GrilleBar", new Vector3(i * gwn / 5.5f, 0.62f + dyN, S.half + 0.03f), new Vector3(0.012f, S.bigGrille ? 0.18f : 0.13f, 0.02f), m.chrome);
            Box(parent, "BumperFront", new Vector3(0, S.yb + 0.10f, S.half - 0.075f), new Vector3(1.50f * wx, 0.22f, 0.16f), m.trim);
            Box(parent, "IntakeFront", new Vector3(0, S.yb + 0.08f, S.half + 0.015f), new Vector3(0.80f * wx, 0.09f, 0.04f), m.paint);
            Box(parent, "PlateFront", new Vector3(0, S.yb + 0.16f, S.half + 0.04f), new Vector3(0.46f, 0.11f, 0.015f), m.plate);
            Box(parent, "SplitterFront", new Vector3(0, S.yb + 0.01f, S.half - 0.08f), new Vector3(1.50f * wx, 0.035f, 0.22f), m.trim);

            // Rear: bumper, plate, exhaust
            Box(parent, "BumperRear", new Vector3(0, S.yb + 0.14f, -S.half + 0.08f), new Vector3(1.50f * wx, 0.26f, 0.16f), m.trim);
            Box(parent, "PlateRear", new Vector3(0, 0.62f + dyT, -S.half + 0.025f), new Vector3(0.46f, 0.11f, 0.015f), m.plate);
            Box(parent, "BootHandle", new Vector3(0, 0.80f + dyT, -S.half + 0.025f), new Vector3(0.22f, 0.03f, 0.02f), m.chrome);
            Box(parent, "Exhaust", new Vector3(0.55f * wx, S.yb + 0.03f, -S.half - 0.02f), new Vector3(0.09f, 0.09f, 0.10f), m.chrome);
            if (S.spoiler == 2 || S.diffuser || S.flares) Box(parent, "Exhaust2", new Vector3(-0.55f * wx, S.yb + 0.03f, -S.half - 0.02f), new Vector3(0.09f, 0.09f, 0.10f), m.chrome);
            if (S.diffuser) Box(parent, "Diffuser", new Vector3(0, S.yb + 0.02f, -S.half + 0.12f), new Vector3(1.2f * wx, 0.05f, 0.26f), m.trim);

            // Bonnet / boot panel lines
            Box(parent, "BonnetLine", new Vector3(0, S.belt + 0.005f, S.hoodStart + 0.04f), new Vector3(S.halfW * 1.74f, 0.008f, 0.02f), m.trim);
            if (!S.rearHatch) Box(parent, "BootLine", new Vector3(0, S.belt + 0.01f, S.trunkStart - 0.02f), new Vector3(S.halfW * 1.72f, 0.008f, 0.02f), m.trim);

            // Wheel wells: dark discs on the flanks behind each wheel so the tyres read as sitting in arches
            foreach (int ws in new[] { -1, 1 })
                foreach (float wz in new[] { 1.36f, -1.36f })
                    Add(cylinderMesh, m.trim, Matrix4x4.TRS(new Vector3(ws * (S.halfW + 0.0045f), 0.38f, wz), Quaternion.Euler(0, 0, 90), new Vector3(0.36f, 0.006f, 0.36f)));

            // Body-type features
            if (S.flares)
                foreach (int s in new[] { -1, 1 }) foreach (float z in new[] { 1.36f, -1.36f })
                    Box(parent, "Flare", new Vector3(s * (S.halfW + 0.03f), 0.60f, z), new Vector3(0.07f, 0.09f, 0.98f), m.paint);
            if (S.cladding)
                foreach (int s in new[] { -1, 1 }) foreach (float z in new[] { 1.36f, -1.36f })
                    Box(parent, "Cladding", new Vector3(s * (S.halfW + 0.02f), 0.58f, z), new Vector3(0.06f, 0.07f, 0.95f), m.trim);
            if (S.hoodScoop) Box(parent, "HoodScoop", new Vector3(0, S.belt + 0.045f, S.hoodStart + 0.55f), new Vector3(0.46f, 0.09f, 0.62f), m.trim);
            if (S.spoiler == 1) Box(parent, "SpoilerLip", new Vector3(0, S.tailH + 0.035f, -S.half + 0.32f), new Vector3(S.halfW * 1.6f, 0.04f, 0.14f), m.paint);
            if (S.spoiler == 2)
            {
                Box(parent, "Wing", new Vector3(0, S.tailH + 0.30f, -S.half + 0.28f), new Vector3(S.halfW * 1.9f, 0.04f, 0.34f), m.trim);
                foreach (int s in new[] { -1, 1 }) Box(parent, "WingPost", new Vector3(s * 0.40f, S.tailH + 0.15f, -S.half + 0.28f), new Vector3(0.04f, 0.30f, 0.12f), m.trim);
            }
            if (S.roofRack)
            {
                foreach (int s in new[] { -1, 1 }) Box(parent, "RackRail", new Vector3(s * (S.roofW - 0.06f), S.roofTop + 0.045f, cabMidZ - 0.1f), new Vector3(0.035f, 0.04f, (S.flatFront - S.flatRear) + 0.3f), m.chrome);
                foreach (float z in new[] { -0.7f, -0.1f, 0.5f }) Box(parent, "RackBar", new Vector3(0, S.roofTop + 0.07f, cabMidZ + z * 0.55f), new Vector3(S.roofW * 1.9f, 0.03f, 0.04f), m.chrome);
            }
            if (S.police && m.accent != null)
            {
                foreach (int s in new[] { -1, 1 })
                {
                    Box(parent, "Livery", new Vector3(s * (S.halfW + 0.004f), S.yb + (S.belt - S.yb) * 0.42f, -0.2f * lz), new Vector3(0.014f, 0.30f, 2.5f * lz), m.accent);
                    Box(parent, "LiveryStripe", new Vector3(s * (S.halfW + 0.004f), S.yb + (S.belt - S.yb) * 0.80f, -0.2f * lz), new Vector3(0.014f, 0.035f, 2.5f * lz), m.accent);
                }
                Box(parent, "HoodStripe", new Vector3(0, S.belt + 0.014f, S.hoodStart + 0.55f), new Vector3(0.45f, 0.012f, 1.0f), m.accent);
            }
            if (S.bullBar)
            {
                Box(parent, "BullBarTop", new Vector3(0, S.yb + 0.34f, S.half + 0.075f), new Vector3(1.20f * wx, 0.05f, 0.05f), m.trim);
                Box(parent, "BullBarBottom", new Vector3(0, S.yb + 0.12f, S.half + 0.075f), new Vector3(1.20f * wx, 0.05f, 0.05f), m.trim);
                foreach (int s in new[] { -1, 0, 1 }) Box(parent, "BullBarPost", new Vector3(s * 0.55f * wx, S.yb + 0.23f, S.half + 0.075f), new Vector3(0.05f, 0.30f, 0.05f), m.trim);
            }
            if (S.lightBar && m.barA != null)
            {
                Box(parent, "BarBase", new Vector3(0, S.roofTop + 0.03f, cabMidZ), new Vector3(0.96f, 0.04f, 0.30f), m.trim);
                Lamp(parent, "BarA", new Vector3(-0.25f, S.roofTop + 0.09f, cabMidZ), new Vector3(0.46f, 0.09f, 0.26f), m.barA);
                Lamp(parent, "BarB", new Vector3(0.25f, S.roofTop + 0.09f, cabMidZ), new Vector3(0.46f, 0.09f, 0.26f), m.barB);
            }
            if (S.taxiSign) Lamp(parent, "RoofSign", new Vector3(0, S.roofTop + 0.085f, -0.12f), new Vector3(0.50f, 0.13f, 0.19f), m.plate);
            FlushBatch(parent, "Details");
            AddKits(parent, m);
        }

        // Body kit stages (hidden until bought): Kit1 street lip + skirts, Kit2 sport vents + duck tail + flares, Kit3 race wing, stripes, canards, neon underglow.
        private static void AddKits(Transform parent, Mats m)
        {
            float wx = S.halfW / 0.93f, cabMidZ = (S.flatFront + S.flatRear) * 0.5f;
            for (int k = 1; k <= 3; k++)
            {
                var kit = new GameObject("Kit" + k).transform; kit.SetParent(parent, false);
                BeginBatch();
                if (k == 1)
                {
                    Box(kit, "Lip", new Vector3(0, S.yb - 0.02f, S.half - 0.06f), new Vector3(1.56f * wx, 0.045f, 0.34f), m.trim);
                    Box(kit, "RearLip", new Vector3(0, S.yb + 0.02f, -S.half + 0.08f), new Vector3(1.50f * wx, 0.04f, 0.26f), m.trim);
                    foreach (int s in new[] { -1, 1 }) Box(kit, "Skirt", new Vector3(s * (S.halfW + 0.02f), S.yb + 0.07f, -0.1f), new Vector3(0.05f, 0.12f, 2.45f), m.paint);
                }
                else if (k == 2)
                {
                    if (S.spoiler == 0) Box(kit, "DuckTail", new Vector3(0, S.tailH + 0.04f, -S.half + 0.34f), new Vector3(S.halfW * 1.5f, 0.05f, 0.18f), m.paint);
                    foreach (int s in new[] { -1, 1 })
                    {
                        Box(kit, "Vent", new Vector3(s * 0.36f, S.belt + 0.012f, S.hoodStart + 0.5f), new Vector3(0.30f, 0.02f, 0.26f), m.trim);
                        if (!S.flares) foreach (float z in new[] { 1.36f, -1.36f }) Box(kit, "Flare", new Vector3(s * (S.halfW + 0.035f), 0.60f, z), new Vector3(0.07f, 0.09f, 0.98f), m.paint);
                    }
                }
                else
                {
                    if (S.spoiler < 2)
                    {
                        Box(kit, "Wing", new Vector3(0, S.tailH + 0.30f, -S.half + 0.28f), new Vector3(S.halfW * 1.9f, 0.04f, 0.34f), m.trim);
                        foreach (int s in new[] { -1, 1 }) Box(kit, "WingPost", new Vector3(s * 0.40f, S.tailH + 0.15f, -S.half + 0.28f), new Vector3(0.04f, 0.30f, 0.12f), m.trim);
                    }
                    Box(kit, "HoodStripe", new Vector3(0, S.belt + 0.02f, S.hoodStart + 0.7f), new Vector3(0.34f, 0.012f, 1.4f), m.trim);
                    Box(kit, "RoofStripe", new Vector3(0, S.roofTop + 0.012f, cabMidZ), new Vector3(0.34f, 0.012f, (S.flatFront - S.flatRear) + 0.9f), m.trim);
                    foreach (int s in new[] { -1, 1 }) Box(kit, "Canard", new Vector3(s * 0.78f * wx, S.yb + 0.12f, S.half - 0.12f), new Vector3(0.20f, 0.03f, 0.16f), m.trim, new Vector3(0, 0, s * 15f));
                }
                FlushBatch(kit, "Kit" + k);
                if (k == 3)
                {
                    var gl = new GameObject("Underglow"); gl.transform.SetParent(kit, false); gl.transform.localPosition = new Vector3(0, 0.06f, 0);
                    var li = gl.AddComponent<Light>(); li.type = LightType.Point; li.color = new Color(0.2f, 0.6f, 1f); li.range = 2.4f; li.intensity = 0.25f; li.shadows = LightShadows.None;
                }
                kit.gameObject.SetActive(false);
            }
        }

        public static void AddWheel(Transform pivot, float radius, float width, float side, Mats m)
        {
            BeginBatch();
            float R = radius, w = width * 0.5f; Vector3 xo = new Vector3(side * 0.075f, 0, 0);   // wheel assembly stands proud of the body flank
            // Tyre: rounded sidewalls, tread ribs and grooves. Profile = (axle position, radius), closed loop.
            var tyre = new List<Vector2>
            {
                new Vector2(-0.80f * w, 0.64f * R), new Vector2(0.80f * w, 0.64f * R), new Vector2(0.92f * w, 0.68f * R), new Vector2(1.0f * w, 0.80f * R),
                new Vector2(0.97f * w, 0.90f * R), new Vector2(0.88f * w, 0.97f * R), new Vector2(0.72f * w, 1.0f * R), new Vector2(0.46f * w, 1.0f * R),
                new Vector2(0.43f * w, 0.982f * R), new Vector2(0.30f * w, 0.982f * R), new Vector2(0.27f * w, 1.0f * R), new Vector2(-0.27f * w, 1.0f * R),
                new Vector2(-0.30f * w, 0.982f * R), new Vector2(-0.43f * w, 0.982f * R), new Vector2(-0.46f * w, 1.0f * R), new Vector2(-0.72f * w, 1.0f * R),
                new Vector2(-0.88f * w, 0.97f * R), new Vector2(-0.97f * w, 0.90f * R), new Vector2(-1.0f * w, 0.80f * R), new Vector2(-0.92f * w, 0.68f * R),
            };
            Add(Lathe("Tyre", tyre, 40, side), m.tyre, Matrix4x4.Translate(xo));
            // Rim: dished face with a lip and a hub boss (graphite), ten chrome spokes, a brake disc visible between them, lug nuts.
            var rim = new List<Vector2>
            {
                new Vector2(-0.5f * w, 0f), new Vector2(0.64f * w, 0f), new Vector2(0.64f * w, 0.17f * R), new Vector2(0.52f * w, 0.20f * R), new Vector2(0.52f * w, 0.52f * R),
                new Vector2(0.66f * w, 0.60f * R), new Vector2(0.98f * w, 0.66f * R), new Vector2(1.0f * w, 0.72f * R), new Vector2(0.70f * w, 0.72f * R), new Vector2(-0.5f * w, 0.72f * R),
            };
            Add(Lathe("Rim", rim, 40, side), m.rimDark != null ? m.rimDark : m.rim, Matrix4x4.Translate(xo));
            var disc = new List<Vector2> { new Vector2(0.06f * w, 0.20f * R), new Vector2(0.30f * w, 0.20f * R), new Vector2(0.30f * w, 0.55f * R), new Vector2(0.06f * w, 0.55f * R) };
            Add(Lathe("Disc", disc, 32, side), m.disc != null ? m.disc : m.trim, Matrix4x4.Translate(xo));
            const int spokes = 10;
            for (int i = 0; i < spokes; i++)
            {
                float ang = i * 360f / spokes;
                Vector3 pos = Quaternion.Euler(ang, 0, 0) * new Vector3(side * 0.62f * w, 0f, 0.40f * R) + xo;
                Box(pivot, "Spoke", pos, new Vector3(0.16f * w, 0.075f * R, 0.42f * R), m.chrome, new Vector3(ang, 0, 0));
            }
            for (int i = 0; i < 5; i++)
            {
                float ang = i * 72f;
                Vector3 pos = Quaternion.Euler(ang, 0, 0) * new Vector3(side * 0.68f * w, 0f, 0.115f * R) + xo;
                Add(cylinderMesh, m.chrome, Matrix4x4.TRS(pos, Quaternion.Euler(0, 0, 90), new Vector3(0.03f * R, 0.04f * w, 0.03f * R)));
            }
            Add(cylinderMesh, m.chrome, Matrix4x4.TRS(new Vector3(side * 0.66f * w, 0, 0) + xo, Quaternion.Euler(0, 0, 90), new Vector3(0.07f * R, 0.04f * w, 0.07f * R)));
            FlushBatch(pivot, side < 0 ? "WheelL" : "WheelR");
        }

        /// <summary>Surface of revolution about the local X axis. 'loop' is a closed (axle position, radius) profile; faces are lit with the analytic
        /// outward normal (winding is derived from it), and 'side' mirrors the wheel for the left-hand side.</summary>
        private static Mesh Lathe(string name, IList<Vector2> loop, int segs, float side)
        {
            int n = loop.Count; float area = 0f;
            for (int i = 0; i < n; i++) { Vector2 p = loop[i], q = loop[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
            float orient = area >= 0f ? 1f : -1f;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int k = 0; k < n; k++)
            {
                Vector2 p = loop[k], q = loop[(k + 1) % n], t = q - p;
                if (t.sqrMagnitude < 1e-10f) continue;
                Vector2 nrm = new Vector2(t.y, -t.x).normalized * orient;
                int baseIdx = verts.Count;
                for (int j = 0; j <= segs; j++)
                {
                    float ang = j / (float)segs * Mathf.PI * 2f, cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                    verts.Add(new Vector3(side * p.x, p.y * cs, p.y * sn)); verts.Add(new Vector3(side * q.x, q.y * cs, q.y * sn));
                    Vector3 n3 = new Vector3(side * nrm.x, nrm.y * cs, nrm.y * sn); norms.Add(n3); norms.Add(n3);
                    uvs.Add(new Vector2(j / (float)segs, 0)); uvs.Add(new Vector2(j / (float)segs, 1));
                }
                for (int j = 0; j < segs; j++)
                {
                    int a = baseIdx + j * 2, b = a + 1, c = a + 2, d = a + 3;
                    AddOriented(verts, norms, tris, a, b, c); AddOriented(verts, norms, tris, b, d, c);
                }
            }
            var mesh = new Mesh { name = "Wheel_" + name, indexFormat = IndexFormat.UInt16 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddOriented(List<Vector3> v, List<Vector3> nrm, List<int> tris, int a, int b, int c)
        {
            Vector3 face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (face.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(face, nrm[a] + nrm[b] + nrm[c]) < 0f) { int t = b; b = c; c = t; }
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        // ---------------------------------------------------------------- tiny helpers
        private static void Mesh(Transform parent, string name, Mesh mesh, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
        }

        // ---- detail parts are merged per material into one mesh each (draw calls!). Lamps stay separate objects.
        private sealed class Part { public Mesh mesh; public Matrix4x4 matrix; }
        private static Dictionary<Material, List<Part>> batch;
        private static Mesh cubeMesh, cylinderMesh;

        private static void BeginBatch()
        {
            batch = new Dictionary<Material, List<Part>>();
            cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            cylinderMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        }

        private static void Add(Mesh mesh, Material mat, Matrix4x4 m)
        {
            if (!batch.TryGetValue(mat, out var list)) batch[mat] = list = new List<Part>();
            list.Add(new Part { mesh = mesh, matrix = m });
        }

        private static void FlushBatch(Transform parent, string key)
        {
            foreach (var kv in batch)
            {
                var ci = new CombineInstance[kv.Value.Count];
                for (int i = 0; i < ci.Length; i++) ci[i] = new CombineInstance { mesh = kv.Value[i].mesh, transform = kv.Value[i].matrix };
                var combined = new Mesh { name = $"{S.id}_{key}_{kv.Key.name}", indexFormat = IndexFormat.UInt16 };
                combined.CombineMeshes(ci, true, true);
                combined = CityAssetFactory.SaveMesh(combined, $"{MeshDir}/{S.id}_{key}_{kv.Key.name}.asset");
                Mesh(parent, $"{key}_{kv.Key.name}", combined, new[] { kv.Key });
            }
            batch = null;
        }

        private static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, Vector3? euler = null)
            => Add(cubeMesh, mat, Matrix4x4.TRS(pos, Quaternion.Euler(euler ?? Vector3.zero), size));

        // A lamp that VehicleLights drives at runtime: its own renderer so its emission can change.
        private static void Lamp(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, Vector3? euler = null)
            => CityAssetFactory.Prim(PrimitiveType.Cube, name, parent, pos, size, mat, false, euler);
    }
}
