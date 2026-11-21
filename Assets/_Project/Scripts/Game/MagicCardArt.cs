using System;
using UnityEngine;

namespace CityDriver.Game
{
    /// <summary>
    /// Procedural artwork for the Shadow Pass card: an ornate violet-and-gold card face and back, an all-seeing-eye sigil, a ring of runes,
    /// a shimmer beam, soft aura and four-point sparkles. Everything is drawn in code (no image assets).
    /// </summary>
    public static class MagicCardArt
    {
        public const int W = 512, H = 768;
        private static Sprite face, back, sigil, runes, beam, aura, spark, rays;

        private static Sprite Build(int w, int h, Func<int, int, Color> f, bool mip = true)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mip, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
            tex.SetPixels(px); tex.Apply(mip);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private static float Hash(int x, int y) { unchecked { int n = x * 374761393 + y * 668265263; n = (n ^ (n >> 13)) * 1274126177; return ((n ^ (n >> 16)) & 0xffff) / 65535f; } }
        private static float RoundBox(float px, float py, float hx, float hy, float r) { float qx = Mathf.Abs(px) - hx + r, qy = Mathf.Abs(py) - hy + r; return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r; }
        private static Color Over(Color under, Color over) { float a = over.a + under.a * (1f - over.a); if (a <= 0f) return Color.clear; Color c = (over * over.a + under * under.a * (1f - over.a)) / a; c.a = a; return c; }
        private static float Aa(float d) => Mathf.Clamp01(0.5f - d);

        private static Color Frame(int x, int y, bool front)
        {
            float px = x + 0.5f - W * 0.5f, py = y + 0.5f - H * 0.5f;
            float d = RoundBox(px, py, W * 0.5f - 1f, H * 0.5f - 1f, 46f);
            float a = Aa(d); if (a <= 0f) return Color.clear;
            float s = -d;
            Color c;
            float diag = Mathf.Clamp01(0.5f + (py * 0.45f - px * 0.55f) / 700f);
            if (s < 12f)
            {
                c = Color.Lerp(new Color(0.55f, 0.32f, 0.08f), new Color(1f, 0.88f, 0.5f), diag);
                c = Color.Lerp(c, new Color(1f, 0.97f, 0.82f), Mathf.Clamp01(1f - s / 2.5f) * 0.6f);
                c = Color.Lerp(c, new Color(0.35f, 0.18f, 0.05f), Mathf.Clamp01((s - 9f) / 3f) * 0.7f);
            }
            else if (s < 17f) c = new Color(0.04f, 0.02f, 0.09f);
            else
            {
                float t = Mathf.InverseLerp(-H * 0.5f, H * 0.5f, py);                             // 0 bottom .. 1 top
                c = Color.Lerp(new Color(0.03f, 0.015f, 0.08f), new Color(0.30f, 0.10f, 0.46f), Mathf.Pow(t, 1.3f));
                float gr = Mathf.Sqrt(px * px + (py - 40f) * (py - 40f));
                c += new Color(0.34f, 0.12f, 0.46f) * Mathf.Exp(-gr * gr / (2f * 190f * 190f)) * 0.9f;                 // glow behind the sigil
                float hs = Hash(x, y); if (hs > 0.9984f) c += Color.white * (0.4f + 0.6f * Hash(y, x));                // tiny stars
                if (!front)
                {
                    float lat = Mathf.Abs(Mathf.Repeat(px + py, 46f) - 23f), lat2 = Mathf.Abs(Mathf.Repeat(px - py, 46f) - 23f);       // diamond lattice on the back
                    c += new Color(0.5f, 0.3f, 0.8f) * (Mathf.Clamp01(1.6f - Mathf.Min(lat, lat2)) * 0.22f);
                }
                // fine inner gold lines
                float d1 = Mathf.Abs(RoundBox(px, py, W * 0.5f - 40f, H * 0.5f - 40f, 32f)), d2 = Mathf.Abs(RoundBox(px, py, W * 0.5f - 52f, H * 0.5f - 52f, 26f));
                c = Color.Lerp(c, new Color(1f, 0.82f, 0.4f), Mathf.Clamp01(1.4f - d1) * 0.85f);
                c = Color.Lerp(c, new Color(1f, 0.82f, 0.4f), Mathf.Clamp01(1.0f - d2) * 0.3f);
                foreach (float sx in new[] { -1f, 1f }) foreach (float sy in new[] { -1f, 1f })                                    // corner diamonds
                {
                    float dx = Mathf.Abs(px - sx * (W * 0.5f - 46f)), dy = Mathf.Abs(py - sy * (H * 0.5f - 46f));
                    c = Color.Lerp(c, new Color(1f, 0.9f, 0.55f), Mathf.Clamp01((13f - (dx + dy)) * 0.7f));
                }
                if (front)
                {
                    foreach (float sepY in new[] { 224f, -204f })                                                            // separators with a centre diamond
                    {
                        float dy = Mathf.Abs(py - sepY), dx = Mathf.Abs(px);
                        float line = Mathf.Clamp01(1.2f - dy) * Mathf.Clamp01((170f - dx) / 20f) * Mathf.Clamp01((dx - 16f) / 6f);
                        float dia = Mathf.Clamp01((10f - (dx + dy)) * 0.8f);
                        c = Color.Lerp(c, new Color(1f, 0.85f, 0.5f), Mathf.Max(line * 0.8f, dia));
                    }
                }
                else
                {
                    float r = Mathf.Sqrt(px * px + py * py), ang = Mathf.Atan2(py, px);
                    float ring = 0f; foreach (float rr in new[] { 150f, 112f, 76f }) ring = Mathf.Max(ring, Mathf.Clamp01(1.6f - Mathf.Abs(r - rr)));
                    float rays = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 4f)), 18f) * Mathf.Clamp01((r - 20f) / 10f) * Mathf.Clamp01((170f - r) / 12f);
                    c = Color.Lerp(c, new Color(1f, 0.84f, 0.45f), Mathf.Max(ring * 0.9f, rays * 0.8f));
                    c = Color.Lerp(c, new Color(1f, 0.95f, 0.75f), Mathf.Clamp01((12f - r) * 0.5f));
                }
                float vig = Mathf.SmoothStep(0.55f, 1.15f, Mathf.Sqrt((px / (W * 0.5f)) * (px / (W * 0.5f)) + (py / (H * 0.5f)) * (py / (H * 0.5f))));
                c *= 1f - 0.4f * vig;
            }
            c.a = a; return c;
        }

        public static Sprite Face => face != null ? face : face = Build(W, H, (x, y) => Frame(x, y, true));
        public static Sprite Back => back != null ? back : back = Build(W, H, (x, y) => Frame(x, y, false));

        /// <summary>Crescent moon and a star inside a rune ring (night / shadow), gold: tint to taste.</summary>
        public static Sprite Sigil => sigil != null ? sigil : sigil = Build(384, 384, (x, y) =>
        {
            float u = (x + 0.5f) / 384f * 2f - 1f, v = (y + 0.5f) / 384f * 2f - 1f, r = Mathf.Sqrt(u * u + v * v), ang = Mathf.Atan2(v, u);
            float px = 2f / 384f, a = 0f;
            a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - 0.94f) / (px * 2.2f)));
            a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - 0.70f) / (px * 1.6f)) * 0.8f);
            float tick = Mathf.Repeat(ang * 36f / (Mathf.PI * 2f), 1f);
            a = Mathf.Max(a, (r > 0.78f && r < 0.88f && tick < 0.3f) ? Mathf.Clamp01((0.88f - r) / px) * Mathf.Clamp01((r - 0.78f) / px) : 0f);
            float dMoon = Mathf.Max(Mathf.Sqrt((u + 0.08f) * (u + 0.08f) + v * v) - 0.50f, -(Mathf.Sqrt((u - 0.16f) * (u - 0.16f) + (v - 0.06f) * (v - 0.06f)) - 0.43f));
            a = Mathf.Max(a, Mathf.Clamp01(0.5f - dMoon / px));
            float sx = u - 0.34f, sy = v - 0.10f, f = Mathf.Sqrt(Mathf.Abs(sx)) + Mathf.Sqrt(Mathf.Abs(sy));
            a = Mathf.Max(a, Mathf.Clamp01((0.50f - f) * 9f));                                          // four-point star inside the horn of the moon
            foreach (var d in new[] { new Vector2(0.50f, -0.30f), new Vector2(-0.52f, 0.46f), new Vector2(0.10f, -0.56f), new Vector2(0.44f, 0.46f) })
            { float dd = Mathf.Sqrt((u - d.x) * (u - d.x) + (v - d.y) * (v - d.y)); a = Mathf.Max(a, Mathf.Clamp01(1f - dd / 0.035f)); }
            Color c = Color.Lerp(new Color(1f, 0.82f, 0.4f), new Color(1f, 0.96f, 0.8f), Mathf.Clamp01(0.5f + v * 0.5f));
            return new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
        });

        /// <summary>Thin ring with ticks and small rune blocks (slowly rotating halo behind the card).</summary>
        public static Sprite Runes => runes != null ? runes : runes = Build(512, 512, (x, y) =>
        {
            float u = (x + 0.5f) / 512f * 2f - 1f, v = (y + 0.5f) / 512f * 2f - 1f, r = Mathf.Sqrt(u * u + v * v), ang = Mathf.Atan2(v, u);
            float px = 2f / 512f, a = 0f;
            a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - 0.98f) / (px * 1.8f)) * 0.9f);
            a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - 0.74f) / (px * 1.4f)) * 0.6f);
            float t96 = Mathf.Repeat(ang * 96f / (Mathf.PI * 2f), 1f);
            if (r > 0.94f && r < 0.98f && t96 < 0.25f) a = Mathf.Max(a, 0.7f);
            float cell = ang * 12f / (Mathf.PI * 2f), cf = Mathf.Repeat(cell, 1f);                     // 12 rune blocks between the rings
            if (r > 0.80f && r < 0.90f)
            {
                int id = Mathf.FloorToInt(Mathf.Repeat(cell, 12f));
                float gx = cf - 0.5f, gy = (r - 0.85f) / 0.05f;
                float shape = id % 3 == 0 ? Mathf.Abs(gx) * 2f + Mathf.Abs(gy) : id % 3 == 1 ? Mathf.Max(Mathf.Abs(gx) * 1.7f, Mathf.Abs(gy)) : Mathf.Abs(gx * 1.6f + gy * 0.4f) + Mathf.Abs(gy) * 0.5f;
                if (shape < 0.55f && shape > 0.38f) a = Mathf.Max(a, 0.85f);
            }
            return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        });

        /// <summary>A soft diagonal light band (rotate and slide it across the card).</summary>
        public static Sprite Beam => beam != null ? beam : beam = Build(64, 256, (x, y) =>
        {
            float u = (x + 0.5f) / 64f * 2f - 1f, v = (y + 0.5f) / 256f * 2f - 1f;
            return new Color(1f, 1f, 1f, Mathf.Exp(-u * u * 7f) * Mathf.Clamp01(1f - Mathf.Abs(v) * 0.1f));
        }, false);

        /// <summary>Soft rays fanning out from the centre (rotate slowly behind the card).</summary>
        public static Sprite Rays => rays != null ? rays : rays = Build(512, 512, (x, y) =>
        {
            float u = (x + 0.5f) / 512f * 2f - 1f, v = (y + 0.5f) / 512f * 2f - 1f, r = Mathf.Sqrt(u * u + v * v), ang = Mathf.Atan2(v, u);
            float ray = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ang * 14f), 7f) * 0.9f + Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ang * 5f + 1.3f), 9f) * 0.6f;
            return new Color(1f, 1f, 1f, Mathf.Clamp01(ray) * Mathf.Pow(Mathf.Clamp01(1f - r), 1.3f) * Mathf.SmoothStep(0.08f, 0.35f, r));
        });

        public static Sprite Aura => aura != null ? aura : aura = Build(160, 160, (x, y) =>
        {
            float u = (x + 0.5f) / 160f * 2f - 1f, v = (y + 0.5f) / 160f * 2f - 1f, d = Mathf.Sqrt(u * u + v * v);
            return new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f));
        }, false);

        public static Sprite Spark => spark != null ? spark : spark = Build(96, 96, (x, y) =>
        {
            float u = (x + 0.5f) / 96f * 2f - 1f, v = (y + 0.5f) / 96f * 2f - 1f;
            float f = Mathf.Sqrt(Mathf.Abs(u)) + Mathf.Sqrt(Mathf.Abs(v)); float a = Mathf.Pow(Mathf.Clamp01(1f - f), 1.7f);
            float core = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v) * 3.2f), 2f);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(a + core));
        }, false);
    }
}
