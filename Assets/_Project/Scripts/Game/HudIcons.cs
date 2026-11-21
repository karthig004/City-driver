using System;
using UnityEngine;

namespace CityDriver.Game
{
    /// <summary>
    /// Procedurally drawn HUD sprites in the style of the reference racing HUD: thick chevron steering arrows, ridged grip-pad pedals and
    /// round ringed icon buttons (camera, gear, horn, handbrake, map pin). Everything is drawn in code with anti-aliased shapes, so there
    /// are no image assets to license or import.
    /// </summary>
    public static class HudIcons
    {
        public enum Kind { Camera, Gear, Horn, Handbrake, Pin, Radio }

        private static Sprite chevronL, chevronR, pedal;
        private static readonly Sprite[] discs = new Sprite[6];

        private static Sprite Make(int w, int h, Func<float, float, Color> f)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f((x + 0.5f) / w * 2f - 1f, (y + 0.5f) / h * 2f - 1f);
            tex.SetPixels(px); tex.Apply(true, false);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        // ------------------------------------------------------------------------------- distance helpers (coordinates -1..1, y up)
        private static float Aa(float d, float px) => Mathf.Clamp01(0.5f - d / px);                 // 1 inside, 0 outside, soft edge
        private static float SegDist(Vector2 p, Vector2 a, Vector2 b) { Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude); return (p - (a + ab * t)).magnitude; }
        private static float BoxDist(Vector2 p, Vector2 half, float r) { Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + Vector2.one * r; return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r; }
        private static Color Over(Color under, Color over) { float a = over.a + under.a * (1f - over.a); if (a <= 0f) return Color.clear; Color c = (over * over.a + under * under.a * (1f - over.a)) / a; c.a = a; return c; }

        // ------------------------------------------------------------------------------- chevrons (steering)
        public static Sprite ChevronLeft => chevronL != null ? chevronL : chevronL = Chevron(-1f);
        public static Sprite ChevronRight => chevronR != null ? chevronR : chevronR = Chevron(1f);

        private static Sprite Chevron(float dir)
        {
            return Make(128, 128, (u, v) =>
            {
                var p = new Vector2(u, v);
                Vector2 tip = new Vector2(0.38f * dir, 0f), a = new Vector2(-0.38f * dir, 0.78f), b = new Vector2(-0.38f * dir, -0.78f);
                float d = Mathf.Min(SegDist(p, tip, a), SegDist(p, tip, b)) - 0.115f;
                float al = Aa(d, 0.03f);
                return new Color(1f, 1f, 1f, al * 0.95f);
            });
        }

        // ------------------------------------------------------------------------------- pedals (grip pad with ridges)
        public static Sprite Pedal => pedal != null ? pedal : pedal = Make(128, 256, (u, v) =>
        {
            var p = new Vector2(u, v);
            float d = BoxDist(p, new Vector2(0.92f, 0.95f), 0.18f);
            if (d > 0.03f) return Color.clear;
            Color baseC = new Color(0.17f, 0.18f, 0.21f, 0.94f);
            float edge = Mathf.Clamp01((d + 0.07f) / 0.07f);
            Color c = Color.Lerp(baseC, new Color(0.88f, 0.9f, 0.93f, 0.95f), Mathf.Clamp01((d + 0.045f) / 0.045f));    // light rim
            float ridge = Mathf.Abs(Mathf.Repeat(v * 4.5f, 1f) - 0.5f);
            if (Mathf.Abs(u) < 0.72f && Mathf.Abs(v) < 0.78f && ridge < 0.1f) c = Color.Lerp(c, new Color(0.55f, 0.57f, 0.62f, 0.9f), Aa(ridge - 0.045f, 0.04f));      // grip ridges
            c.a *= Aa(d, 0.03f);
            return c;
        });

        // ------------------------------------------------------------------------------- metal ridged pedals and the steering wheel (reference style)
        private static Sprite pedalMetalTall, pedalMetalShort, wheelSprite;
        public static Sprite PedalMetalTall => pedalMetalTall != null ? pedalMetalTall : pedalMetalTall = PedalMetal(124, 262, 11);
        public static Sprite PedalMetalShort => pedalMetalShort != null ? pedalMetalShort : pedalMetalShort = PedalMetal(124, 178, 7);

        private static Sprite PedalMetal(int w, int h, int ridges)
        {
            float asp = w / (float)h;
            return Make(w, h, (u, v) =>
            {
                var p = new Vector2(u * asp, v);
                float d = BoxDist(p, new Vector2(0.95f * asp, 0.95f), 0.16f);
                if (d > 0.03f) return Color.clear;
                // light brushed-metal frame, darker recessed face with horizontal grip ridges
                float frame = Mathf.Clamp01((d + 0.11f) / 0.06f);
                Color frameCol = Color.Lerp(new Color(0.60f, 0.62f, 0.66f), new Color(0.86f, 0.88f, 0.91f), Mathf.Clamp01(0.5f + v * 0.35f));
                Color face = Color.Lerp(new Color(0.20f, 0.21f, 0.24f), new Color(0.33f, 0.35f, 0.39f), Mathf.Clamp01(0.5f + v * 0.5f));
                Color c = Color.Lerp(face, frameCol, frame);
                float inner = BoxDist(p, new Vector2(0.78f * asp, 0.82f), 0.10f);
                if (inner < 0f)
                {
                    float t = (v + 0.82f) / 1.64f * ridges;
                    float ridge = Mathf.Abs(Mathf.Repeat(t, 1f) - 0.5f);                 // 0 at a ridge centre
                    float bar = Aa(ridge - 0.22f, 0.10f);
                    Color barCol = Color.Lerp(new Color(0.55f, 0.57f, 0.62f), new Color(0.83f, 0.85f, 0.89f), Mathf.Clamp01(0.5f + v * 0.4f));
                    c = Color.Lerp(c, barCol, bar * Aa(inner + 0.015f, 0.03f));
                }
                c.a = 0.97f * Aa(d, 0.03f);
                return c;
            });
        }

        /// <summary>Steering wheel: thick dark rim with a soft highlight, three metal spokes (left, right, down), hub with an orange ring and a yellow top marker.</summary>
        public static Sprite SteeringWheel => wheelSprite != null ? wheelSprite : wheelSprite = Make(256, 256, (u, v) =>
        {
            var p = new Vector2(u, v);
            float r = p.magnitude, ang = Mathf.Atan2(v, u) * Mathf.Rad2Deg;
            Color c = Color.clear;
            float rimOuter = r - 0.98f, rimInner = 0.78f - r;
            float rim = Mathf.Max(rimOuter, rimInner);
            if (rim < 0.02f)
            {
                float hl = Mathf.Clamp01(Vector2.Dot(p.normalized, new Vector2(-0.6f, 0.8f)) * 0.5f + 0.5f);
                Color rimCol = Color.Lerp(new Color(0.05f, 0.05f, 0.06f), new Color(0.30f, 0.31f, 0.34f), hl * hl);
                float edgeHi = Aa(Mathf.Abs(r - 0.93f) - 0.012f, 0.02f) * 0.35f;
                c = Over(c, new Color(rimCol.r + edgeHi, rimCol.g + edgeHi, rimCol.b + edgeHi, Aa(rim, 0.02f)));
            }
            // spokes: 180 deg (left), 0 deg (right), 270 deg (down)
            float spoke = float.MaxValue;
            foreach (float a in new[] { 180f, 0f, -90f })
            {
                Vector2 dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                spoke = Mathf.Min(spoke, SegDist(p, dir * 0.22f, dir * 0.82f) - (0.085f + 0.03f * (1f - Mathf.Clamp01(r / 0.8f))));
            }
            if (spoke < 0.02f) c = Over(c, new Color(Mathf.Lerp(0.42f, 0.7f, Mathf.Clamp01(0.5f + v * 0.5f)), Mathf.Lerp(0.44f, 0.72f, Mathf.Clamp01(0.5f + v * 0.5f)), Mathf.Lerp(0.48f, 0.76f, Mathf.Clamp01(0.5f + v * 0.5f)), Aa(spoke, 0.02f)));
            // hub
            float hub = r - 0.30f;
            if (hub < 0.02f)
            {
                c = Over(c, new Color(0.10f, 0.11f, 0.13f, Aa(hub, 0.02f)));
                c = Over(c, new Color(0.96f, 0.55f, 0.10f, Aa(Mathf.Abs(r - 0.22f) - 0.018f, 0.02f)));
                c = Over(c, new Color(0.22f, 0.24f, 0.27f, Aa(r - 0.17f, 0.02f)));
            }
            // top marker
            if (Mathf.Abs(Mathf.DeltaAngle(ang, 90f)) < 4f && r > 0.80f && r < 0.97f) c = Over(c, new Color(0.98f, 0.78f, 0.14f, 0.95f));
            return c;
        });

        // ------------------------------------------------------------------------------- styled pedals (glass pad, accent rim and glow, icon)
        private static Sprite pedalGas, pedalBrake, pedalRev;
        public static Sprite PedalGas => pedalGas != null ? pedalGas : pedalGas = PedalStyled(140, 200, new Color(0.25f, 0.90f, 0.50f), 0);
        public static Sprite PedalBrake => pedalBrake != null ? pedalBrake : pedalBrake = PedalStyled(140, 161, new Color(1.00f, 0.30f, 0.30f), 1);
        public static Sprite PedalRev => pedalRev != null ? pedalRev : pedalRev = PedalStyled(160, 88, new Color(1.00f, 0.74f, 0.22f), 2);

        private static Sprite PedalStyled(int w, int h, Color accent, int glyph)
        {
            float asp = w / (float)h;
            return Make(w, h, (u, v) =>
            {
                var p = new Vector2(u * asp, v);
                float d = BoxDist(p, new Vector2(0.94f * asp, 0.94f), 0.30f);
                if (d > 0.03f) return Color.clear;
                Color c = Color.Lerp(new Color(0.05f, 0.06f, 0.09f, 0.82f), new Color(0.17f, 0.19f, 0.25f, 0.84f), Mathf.Clamp01(0.5f + v * 0.5f));
                c = Over(c, new Color(accent.r, accent.g, accent.b, 0.34f * Mathf.Clamp01((-v + 0.25f) / 1.25f)));
                c = Over(c, new Color(accent.r, accent.g, accent.b, 0.95f * Aa(Mathf.Abs(d + 0.035f) - 0.026f, 0.03f)));
                c = Over(c, new Color(1f, 1f, 1f, 0.07f * Aa(BoxDist(p - new Vector2(0f, 0.55f), new Vector2(0.70f * asp, 0.22f), 0.2f), 0.04f)));
                float s = Mathf.Min(1f, asp * 1.15f);
                Vector2 q = new Vector2(p.x / s, (p.y - (h > 120 ? 0.10f : 0f)) / s);
                c = Over(c, new Color(1f, 1f, 1f, 0.96f * PedalGlyph(glyph, q)));
                c.a *= Aa(d, 0.03f);
                return c;
            });
        }

        private static float PedalGlyph(int g, Vector2 p)
        {
            const float w = 0.075f;
            if (g == 0)       // accelerator: two stacked chevrons pointing up
            {
                float d1 = Mathf.Min(SegDist(p, new Vector2(-0.34f, 0.00f), new Vector2(0f, 0.34f)), SegDist(p, new Vector2(0.34f, 0.00f), new Vector2(0f, 0.34f))) - w;
                float d2 = Mathf.Min(SegDist(p, new Vector2(-0.34f, -0.32f), new Vector2(0f, 0.02f)), SegDist(p, new Vector2(0.34f, -0.32f), new Vector2(0f, 0.02f))) - w;
                return Mathf.Max(Aa(d1, 0.03f), Aa(d2, 0.03f));
            }
            if (g == 1)       // brake: a stop square inside a ring
            {
                float ring = Mathf.Abs(p.magnitude - 0.50f) - w * 0.8f;
                float sq = BoxDist(p, new Vector2(0.22f, 0.22f), 0.05f);
                return Mathf.Max(Aa(ring, 0.03f), Aa(sq, 0.03f));
            }
            // reverse: a U-turn arrow
            float arc = Mathf.Abs((p - new Vector2(0f, 0.04f)).magnitude - 0.30f) - w * 0.9f;
            float an = Mathf.Atan2(p.y - 0.04f, p.x) * Mathf.Rad2Deg;
            float a1 = an >= -4f ? Aa(arc, 0.03f) : 0f;
            float rl = SegDist(p, new Vector2(0.30f, 0.04f), new Vector2(0.30f, -0.34f)) - w * 0.9f;
            float ll = SegDist(p, new Vector2(-0.30f, 0.04f), new Vector2(-0.30f, -0.18f)) - w * 0.9f;
            float h1 = SegDist(p, new Vector2(-0.30f, -0.34f), new Vector2(-0.46f, -0.14f)) - w * 0.9f;
            float h2 = SegDist(p, new Vector2(-0.30f, -0.34f), new Vector2(-0.14f, -0.14f)) - w * 0.9f;
            return Mathf.Max(a1, Aa(rl, 0.03f), Aa(ll, 0.03f), Aa(h1, 0.03f), Aa(h2, 0.03f));
        }

        // ------------------------------------------------------------------------------- round icon buttons
        public static Sprite Disc(Kind kind)
        {
            int i = (int)kind;
            if (discs[i] != null) return discs[i];
            return discs[i] = Make(160, 160, (u, v) =>
            {
                var p = new Vector2(u, v);
                float r = p.magnitude;
                Color c = Color.clear;
                c = Over(c, new Color(0.06f, 0.08f, 0.12f, 0.62f * Aa(r - 0.94f, 0.04f)));                                   // dark translucent disc
                c = Over(c, new Color(1f, 1f, 1f, 0.92f * Aa(Mathf.Abs(r - 0.88f) - 0.035f, 0.035f)));                       // white ring
                c = Over(c, new Color(1f, 1f, 1f, 0.97f * Glyph(kind, p)));
                return c;
            });
        }

        // white-on-transparent glyph coverage 0..1
        private static float Glyph(Kind kind, Vector2 p)
        {
            const float w = 0.075f;                                                                                          // stroke half-width
            switch (kind)
            {
                case Kind.Camera:
                {
                    float body = Mathf.Abs(BoxDist(p + new Vector2(0f, -0.02f), new Vector2(0.46f, 0.28f), 0.08f)) - w * 0.7f;
                    float top = Mathf.Abs(BoxDist(p - new Vector2(-0.18f, 0.36f), new Vector2(0.16f, 0.07f), 0.03f)) - w * 0.7f;
                    float lens = Mathf.Abs((p - new Vector2(0f, -0.02f)).magnitude - 0.17f) - w * 0.7f;
                    return Mathf.Max(Aa(body, 0.03f), Aa(top, 0.03f), Aa(lens, 0.03f));
                }
                case Kind.Gear:
                {
                    // a cog: solid body, eight chunky square teeth, centre hole
                    float r = p.magnitude;
                    float ang = Mathf.Atan2(p.y, p.x);
                    float sector = Mathf.Abs(Mathf.Repeat(ang * 8f / (2f * Mathf.PI) + 0.5f, 1f) - 0.5f);             // 0 at a tooth centre
                    float toothHalf = 0.11f / Mathf.Max(r, 0.2f);                                                      // constant tooth width in space
                    float body = r - 0.40f;
                    float tooth = Mathf.Max(r - 0.62f, 0.36f - r, (sector - toothHalf / (Mathf.PI * 0.25f)) * 0.3f);
                    float cog = Mathf.Min(body, tooth);
                    float hole = 0.17f - r;
                    return Aa(Mathf.Max(cog, hole), 0.03f);
                }
                case Kind.Horn:
                {
                    // speaker cone + two sound arcs
                    float cone = float.MaxValue;
                    Vector2 a = new Vector2(-0.46f, 0.14f), b = new Vector2(-0.46f, -0.14f), c = new Vector2(-0.08f, -0.38f), d = new Vector2(-0.08f, 0.38f);
                    cone = Mathf.Min(SegDist(p, a, b), SegDist(p, b, c), SegDist(p, c, d), SegDist(p, d, a)) - w * 0.7f;
                    float arcs = 1f;
                    float rr = (p - new Vector2(-0.1f, 0f)).magnitude; float an = Mathf.Atan2(p.y, p.x - -0.1f) * Mathf.Rad2Deg;
                    if (Mathf.Abs(an) < 42f) arcs = Mathf.Min(Mathf.Abs(rr - 0.5f), Mathf.Abs(rr - 0.74f)) - w * 0.6f;
                    else arcs = 1f;
                    return Mathf.Max(Aa(cone, 0.03f), Aa(arcs, 0.03f));
                }
                case Kind.Handbrake:
                {
                    float ring = Mathf.Abs(p.magnitude - 0.5f) - w * 0.8f;
                    float bar = BoxDist(p - new Vector2(0f, 0.1f), new Vector2(0.07f, 0.22f), 0.04f);
                    float dot = (p - new Vector2(0f, -0.27f)).magnitude - 0.085f;
                    float arcL = 1f; float an = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
                    if (Mathf.Abs(Mathf.Abs(an) - 180f) < 38f || Mathf.Abs(an) < 38f) arcL = Mathf.Abs(p.magnitude - 0.72f) - w * 0.6f;                   // the "( ! )" parentheses
                    return Mathf.Max(Aa(ring, 0.03f), Aa(bar, 0.03f), Aa(dot, 0.03f), Aa(arcL, 0.03f));
                }
                case Kind.Pin:
                {
                    float head = (p - new Vector2(0f, 0.16f)).magnitude - 0.3f;
                    float tail = SegDist(p, new Vector2(0f, -0.55f), new Vector2(0f, 0.02f)) - 0.19f * Mathf.Clamp01((p.y + 0.55f) / 0.5f);
                    float hole = (p - new Vector2(0f, 0.16f)).magnitude - 0.11f;
                    float shape = Mathf.Min(head, tail);
                    return Aa(Mathf.Max(shape, -hole), 0.03f);
                }
                default:   // Radio: a small speaker with waves
                {
                    float body = BoxDist(p - new Vector2(-0.25f, 0f), new Vector2(0.16f, 0.2f), 0.03f) - 0.0f;
                    float a1 = Mathf.Abs((p - new Vector2(-0.1f, 0f)).magnitude - 0.4f) - w * 0.6f;
                    float an = Mathf.Atan2(p.y, p.x - -0.1f) * Mathf.Rad2Deg;
                    return Mathf.Max(Aa(body, 0.03f), Mathf.Abs(an) < 40f ? Aa(a1, 0.03f) : 0f);
                }
            }
        }
    }
}
