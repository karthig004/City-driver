using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CityDriver.Game
{
    /// <summary>
    /// The look of the whole interface in one place: the game's typefaces (Poppins for text, Bebas Neue for titles, both SIL Open Font
    /// License, in Resources/Fonts), the gold / midnight palette, and the small motion components that make every screen feel like a
    /// console title: press feedback on buttons, pop-in modals, staggered entrances, text gradients, letter spacing and drag sliders.
    /// All of it runs on unscaled time, so it animates while the game is paused behind a menu.
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Gold = new Color(0.98f, 0.76f, 0.12f), GoldLight = new Color(1f, 0.93f, 0.55f), GoldDeep = new Color(0.88f, 0.5f, 0.06f);
        public static readonly Color Ink = new Color(0.035f, 0.045f, 0.075f), Navy = new Color(0.075f, 0.095f, 0.15f), NavyLight = new Color(0.13f, 0.16f, 0.24f), Crimson = new Color(0.78f, 0.09f, 0.13f);

        private static Font display, bold, semi, italic, fallback;
        public static Font Fallback => fallback != null ? fallback : fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        private static Font Load(string name) { var f = Resources.Load<Font>("Fonts/" + name); return f != null ? f : Fallback; }
        /// <summary>Tall condensed title face (Bebas Neue).</summary>
        public static Font Display => display != null ? display : display = Load("BebasNeue-Regular");
        public static Font Bold => bold != null ? bold : bold = Load("Poppins-Bold");
        public static Font Semi => semi != null ? semi : semi = Load("Poppins-SemiBold");
        /// <summary>Bold italic for the big speed number (like the reference speedometer).</summary>
        public static Font Italic => italic != null ? italic : italic = Load("Poppins-BoldItalic");

        /// <summary>Swaps the engine's default font for the game fonts on every Text under <paramref name="root"/> (bold style becomes the real bold face).</summary>
        public static void ApplyFonts(Transform root)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<Text>(true)) Apply(t);
        }

        public static void Apply(Text t)
        {
            if (t == null) return;
            string n = t.font != null ? t.font.name : "";
            if (t.font != null && !n.StartsWith("Legacy") && !n.StartsWith("Arial")) return;
            bool b = (t.fontStyle & FontStyle.Bold) != 0;
            t.font = b ? Bold : Semi;
            if (b) t.fontStyle = t.fontStyle & ~FontStyle.Bold;
        }

        private static Sprite disc;
        /// <summary>Plain anti-aliased white disc (tint it), no rim.</summary>
        public static Sprite Disc
        {
            get
            {
                if (disc != null) return disc;
                const int n = 128; var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f; px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) * n * 0.5f)); }
                tex.SetPixels(px); tex.Apply(true);
                return disc = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
        }

        private static Sprite glow;
        /// <summary>Soft round glow (white, tint it).</summary>
        public static Sprite Glow
        {
            get
            {
                if (glow != null) return glow;
                const int n = 96; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f; float d = Mathf.Sqrt(u * u + v * v); px[y * n + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1f - d), 2f)); }
                tex.SetPixels(px); tex.Apply();
                return glow = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
        }

        public static float EaseOutBack(float k) { k = Mathf.Clamp01(k) - 1f; return 1f + k * k * (2.70158f * k + 1.70158f); }
        public static float EaseOutCubic(float k) { k = 1f - Mathf.Clamp01(k); return 1f - k * k * k; }
    }

    /// <summary>Small white glyphs drawn in code (tint them): used on the HUD buttons.</summary>
    public static class UiIcons
    {
        private static Sprite grid, clipboard;
        private static float BoxD(float x, float y, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x) - hx + r, qy = Mathf.Abs(y) - hy + r;
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }
        private static Sprite Make(Func<float, float, float> sdf)
        {
            const int n = 96; var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f; px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(0.5f - sdf(u, v) * n * 0.5f)); }
            tex.SetPixels(px); tex.Apply(true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }
        /// <summary>Four rounded squares (menu).</summary>
        public static Sprite Grid => grid != null ? grid : grid = Make((u, v) => BoxD(Mathf.Abs(u) - 0.5f, Mathf.Abs(v) - 0.5f, 0.38f, 0.38f, 0.12f));
        /// <summary>Clipboard with lines (missions).</summary>
        public static Sprite Clipboard => clipboard != null ? clipboard : clipboard = Make((u, v) =>
        {
            float body = Mathf.Max(BoxD(u, v - 0.0f, 0.62f, 0.78f, 0.16f), -BoxD(u, v, 0.48f, 0.64f, 0.08f));           // outline of the board
            float clip = BoxD(u, v - 0.78f, 0.30f, 0.16f, 0.08f);
            float l1 = BoxD(u, v - 0.28f, 0.30f, 0.065f, 0.03f), l2 = BoxD(u, v + 0.02f, 0.30f, 0.065f, 0.03f), l3 = BoxD(u, v + 0.32f, 0.20f, 0.065f, 0.03f);
            return Mathf.Min(Mathf.Min(body, clip), Mathf.Min(l1, Mathf.Min(l2, l3)));
        });
    }

    /// <summary>Press feedback: shrinks a little while held, grows a little under a mouse pointer, springs back smoothly.</summary>
    public sealed class PressFx : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private float cur = 1f; private bool down, hover; private Vector3 baseScale = Vector3.one; private bool baseSet;
        private void Awake() { baseScale = transform.localScale; baseSet = true; }
        public void OnPointerDown(PointerEventData e) { down = true; }
        public void OnPointerUp(PointerEventData e) { down = false; }
        public void OnPointerEnter(PointerEventData e) { hover = UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Touchscreen.current == null; }
        public void OnPointerExit(PointerEventData e) { hover = false; down = false; }
        private void OnDisable() { cur = 1f; down = hover = false; if (baseSet) transform.localScale = baseScale; }
        private void Update()
        {
            float want = down ? 0.93f : hover ? 1.035f : 1f;
            if (Mathf.Abs(cur - want) < 0.0005f && Mathf.Approximately(want, 1f)) { if (cur != 1f) { cur = 1f; transform.localScale = baseScale; } return; }
            cur = Mathf.Lerp(cur, want, 1f - Mathf.Exp(-24f * Time.unscaledDeltaTime));
            transform.localScale = baseScale * cur;
        }
    }

    /// <summary>Fade + slide in after an optional delay (a staggered list entrance). Captures the rest position when added.</summary>
    public sealed class Entrance : MonoBehaviour
    {
        private RectTransform rt; private CanvasGroup group; private Vector2 rest, offset; private float delay, duration = 0.32f, clock;
        public static void Add(RectTransform target, float delay, Vector2 offset, float duration = 0.32f)
        {
            if (target == null) return;
            var e = target.gameObject.GetComponent<Entrance>(); if (e == null) e = target.gameObject.AddComponent<Entrance>();
            e.rt = target; e.rest = target.anchoredPosition; e.offset = offset; e.delay = delay; e.duration = duration; e.clock = 0f;
            e.group = target.GetComponent<CanvasGroup>(); if (e.group == null) e.group = target.gameObject.AddComponent<CanvasGroup>(); e.group.alpha = 0f;
            target.anchoredPosition = e.rest + offset;
        }
        private void Update()
        {
            if (group == null || rt == null) { Destroy(this); return; }
            clock += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01((clock - delay) / duration), e = UiTheme.EaseOutCubic(k);
            group.alpha = e; rt.anchoredPosition = rest + offset * (1f - e);
            if (k >= 1f) { group.alpha = 1f; rt.anchoredPosition = rest; Destroy(this); }
        }
    }

    /// <summary>Modal open animation: the dim layer fades in and the panel pops up with a soft overshoot.</summary>
    public sealed class ModalPop : MonoBehaviour
    {
        private Image dim; private RectTransform panel; private CanvasGroup panelGroup; private float targetAlpha, clock;
        public bool stagger;
        private RectTransform content;
        public static ModalPop Add(GameObject dimGo, RectTransform holder, RectTransform content)
        {
            var m = dimGo.AddComponent<ModalPop>(); m.dim = dimGo.GetComponent<Image>(); m.panel = holder; m.content = content; m.targetAlpha = m.dim.color.a;
            m.panelGroup = holder.gameObject.AddComponent<CanvasGroup>(); m.panelGroup.alpha = 0f; holder.localScale = Vector3.one * 0.9f;
            m.dim.color = new Color(m.dim.color.r, m.dim.color.g, m.dim.color.b, 0f);
            return m;
        }

        // list entrance: the screen's buttons slide up one after another (after the caller has finished building the screen)
        private void Start()
        {
            if (!stagger || content == null) return;
            int i = 0;
            foreach (Transform c in content)
            {
                if (!c.name.StartsWith("Button_") || c.name == "Button_X") continue;
                Entrance.Add((RectTransform)c, Mathf.Min(0.08f + i * 0.04f, 0.5f), new Vector2(0f, -18f), 0.3f); i++;
            }
        }
        private void Update()
        {
            if (dim == null || panel == null || panelGroup == null) { Destroy(this); return; }
            clock += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(clock / 0.34f);
            var c = dim.color; dim.color = new Color(c.r, c.g, c.b, targetAlpha * Mathf.Clamp01(clock / 0.2f));
            panelGroup.alpha = Mathf.Clamp01(clock / 0.18f);
            panel.localScale = Vector3.one * Mathf.LerpUnclamped(0.9f, 1f, UiTheme.EaseOutBack(k));
            if (k >= 1f) { panel.localScale = Vector3.one; panelGroup.alpha = 1f; dim.color = new Color(c.r, c.g, c.b, targetAlpha); Destroy(panelGroup); Destroy(this); }
        }
    }

    /// <summary>Vertical colour gradient over a Text (gold lettering, metallic titles).</summary>
    public sealed class TextGradient : BaseMeshEffect
    {
        public Color top = Color.white, bottom = new Color(0.8f, 0.8f, 0.8f);
        /// <summary>Horizontal position (0..1 across the text) of a travelling light band; below 0 hides it.</summary>
        public float shine = -1f, shineWidth = 0.14f, shineStrength = 0.9f;
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var v = new UIVertex(); float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++) { vh.PopulateUIVertex(ref v, i); minY = Mathf.Min(minY, v.position.y); maxY = Mathf.Max(maxY, v.position.y); minX = Mathf.Min(minX, v.position.x); maxX = Mathf.Max(maxX, v.position.x); }
            float h = maxY - minY, wd = maxX - minX; if (h <= 0.01f) return;
            float alpha = graphic != null ? graphic.color.a : 1f;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                Color c = Color.Lerp(bottom, top, (v.position.y - minY) / h);
                if (shine > -0.5f && wd > 0.01f)
                {
                    float nx = (v.position.x - minX) / wd + (v.position.y - minY) / h * 0.12f, dd = (nx - shine) / shineWidth;      // slightly slanted band
                    c = Color.Lerp(c, new Color(1f, 1f, 0.92f), Mathf.Exp(-dd * dd) * shineStrength);
                }
                c.a *= alpha; v.color = c; vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>Extra spacing between letters (legacy Text has none). Add it BEFORE any Outline / Shadow effect.</summary>
    public sealed class LetterSpacing : BaseMeshEffect
    {
        public float spacing = 6f;
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount < 4) return;
            int glyphs = vh.currentVertCount / 4; var text = graphic as Text;
            float shift = 0f;
            if (text != null)
            {
                TextAnchor a = text.alignment;
                bool center = a == TextAnchor.UpperCenter || a == TextAnchor.MiddleCenter || a == TextAnchor.LowerCenter;
                bool right = a == TextAnchor.UpperRight || a == TextAnchor.MiddleRight || a == TextAnchor.LowerRight;
                shift = center ? -(glyphs - 1) * spacing * 0.5f : right ? -(glyphs - 1) * spacing : 0f;
            }
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i); v.position += new Vector3(shift + (i / 4) * spacing, 0f, 0f); vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>Drag slider on a rect: reports a 0..1 position while the finger / mouse is down or dragging.</summary>
    public sealed class UiSlider : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Action<float> onValue;
        private RectTransform rt;
        private void Awake() { rt = (RectTransform)transform; }
        public void OnPointerDown(PointerEventData e) { Set(e); }
        public void OnDrag(PointerEventData e) { Set(e); }
        private void Set(PointerEventData e)
        {
            if (rt == null) rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out Vector2 local)) return;
            float k = Mathf.Clamp01((local.x - rt.rect.xMin) / Mathf.Max(1f, rt.rect.width));
            onValue?.Invoke(k);
        }
    }

    /// <summary>Toggle switch visuals: the knob glides and the track changes colour smoothly toward the current state.</summary>
    public sealed class SwitchAnim : MonoBehaviour
    {
        public RectTransform knob; public Image track; public bool on; public float travel = 30f;
        public Color onColor = UiTheme.Gold, offColor = new Color(0.22f, 0.25f, 0.33f);
        private float k = -1f;
        public void SnapTo(bool state) { on = state; k = on ? 1f : 0f; Apply(); }
        private void Update() { float want = on ? 1f : 0f; if (k < 0f) k = want; k = Mathf.MoveTowards(k, want, Time.unscaledDeltaTime * 8f); Apply(); }
        private void Apply() { if (knob != null) knob.anchoredPosition = new Vector2(Mathf.Lerp(-travel, travel, k), 0f); if (track != null) track.color = Color.Lerp(offColor, onColor, k); }
    }
}
