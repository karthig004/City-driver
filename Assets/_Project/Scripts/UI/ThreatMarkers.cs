using System.Collections.Generic;
using CityDriver.Game;
using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.UI
{
    /// <summary>
    /// Shared look of "who is coming for me" on every map: the driving radar, the flat city map and the 3D city map. Police flash red and blue,
    /// gang cars burn orange-red, roadblocks are yellow diamonds, and a chaser that was stopped by an EMP turns grey. Sprites are drawn in code.
    /// </summary>
    public static class ThreatMarkers
    {
        public static readonly Color PoliceRed = new Color(1f, 0.22f, 0.22f), PoliceBlue = new Color(0.3f, 0.55f, 1f), Gang = new Color(1f, 0.45f, 0.1f), Block = new Color(1f, 0.85f, 0.1f), Down = new Color(0.62f, 0.64f, 0.7f);

        private static Sprite dot, arrow, ring, sweep, diamond;
        private static readonly List<PursuitManager.ThreatInfo> scratch = new List<PursuitManager.ThreatInfo>();

        public static Color ColourFor(PursuitManager.ThreatInfo t, float time)
        {
            if (t.disabled) return Down;
            if (t.roadblock) return Block;
            if (t.kind == PursuerKind.Police) return Mathf.FloorToInt(time * 4f) % 2 == 0 ? PoliceRed : PoliceBlue;
            return Color.Lerp(Gang, new Color(1f, 0.15f, 0.1f), 0.5f + 0.5f * Mathf.Sin(time * 9f));
        }

        /// <summary>Editor capture tool: draw these chasers instead of the live pursuit.</summary>
        public static List<PursuitManager.ThreatInfo> PreviewOverride;

        public static List<PursuitManager.ThreatInfo> Current()
        {
            if (PreviewOverride != null) return PreviewOverride;                  // editor previews only
            scratch.Clear();
            var pm = PursuitManager.Instance;
            if (pm != null) pm.GetThreats(scratch);
            return scratch;
        }

        private static Sprite Make(int n, System.Func<float, float, Color> f)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
            tex.SetPixels(px); tex.Apply(true, false);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>White filled disc with a black rim: tint it for the core colour.</summary>
        public static Sprite Dot => dot != null ? dot : dot = Make(64, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Clamp01((1f - r) * 32f);
            float core = Mathf.Clamp01((0.68f - r) * 32f);
            return new Color(core, core, core, a);
        });

        /// <summary>Upward-pointing arrow head with a dark outline (edge-of-radar "something is out there" marker).</summary>
        public static Sprite Arrow => arrow != null ? arrow : arrow = Make(64, (u, v) =>
        {
            float d = TriDist(new Vector2(u, v), new Vector2(0f, 0.92f), new Vector2(-0.78f, -0.82f), new Vector2(0.78f, -0.82f));
            float dn = TriDist(new Vector2(u, v), new Vector2(0f, 0.55f), new Vector2(-0.46f, -0.5f), new Vector2(0.46f, -0.5f));
            float a = Mathf.Clamp01(-d * 32f);
            float core = Mathf.Clamp01(-dn * 32f);
            return new Color(core, core, core, a);
        });

        public static Sprite Diamond => diamond != null ? diamond : diamond = Make(64, (u, v) =>
        {
            float d = Mathf.Abs(u) + Mathf.Abs(v);
            float a = Mathf.Clamp01((0.95f - d) * 28f), core = Mathf.Clamp01((0.62f - d) * 28f);
            return new Color(core, core, core, a);
        });

        /// <summary>Thin hollow ring (range rings, alert pulse).</summary>
        public static Sprite Ring => ring != null ? ring : ring = Make(256, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Clamp01((0.012f - Mathf.Abs(r - 0.985f)) * 140f);
            return new Color(1f, 1f, 1f, a);
        });

        /// <summary>Radar sweep: a fading wedge behind a bright leading edge (rotate it for the scanning look).</summary>
        public static Sprite Sweep => sweep != null ? sweep : sweep = Make(128, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            if (r > 1f) return Color.clear;
            float ang = Mathf.Atan2(v, u);
            float behind = Mathf.Repeat(ang, Mathf.PI * 2f);               // angle swept since the leading edge (scan runs counter-clockwise)
            behind = Mathf.PI * 2f - behind; if (behind >= Mathf.PI * 2f) behind = 0f;
            float a = Mathf.Clamp01(1f - behind / 1.25f); a *= a * 0.6f;
            if (behind < 0.025f) a = 0.9f;
            return new Color(1f, 1f, 1f, a * Mathf.Clamp01((1f - r) * 8f));
        });

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 e = b - a, w = p - a;
            return (w - e * Mathf.Clamp01(Vector2.Dot(w, e) / e.sqrMagnitude)).magnitude;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        /// <summary>Signed distance to a triangle (negative inside).</summary>
        private static float TriDist(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float c1 = Cross(a, b, p), c2 = Cross(b, c, p), c3 = Cross(c, a, p);
            bool inside = (c1 >= 0f && c2 >= 0f && c3 >= 0f) || (c1 <= 0f && c2 <= 0f && c3 <= 0f);
            float d = Mathf.Min(SegDist(p, a, b), Mathf.Min(SegDist(p, b, c), SegDist(p, c, a)));
            return inside ? -d : d;
        }

        public static Image NewImage(Transform parent, string name, Sprite sprite, Vector2 size)
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(Image)); g.transform.SetParent(parent, false);
            var rt = (RectTransform)g.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = size;
            var im = g.GetComponent<Image>(); im.sprite = sprite; im.raycastTarget = false; return im;
        }

        public static Text NewText(Transform parent, string name, int size, TextAnchor anchor, Vector2 pos, Vector2 box)
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(Text)); g.transform.SetParent(parent, false);
            var rt = (RectTransform)g.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = box;
            var t = g.GetComponent<Text>(); t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = size; t.fontStyle = FontStyle.Bold; t.alignment = anchor; t.color = Color.white; t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = g.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.9f); sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }
    }
}
