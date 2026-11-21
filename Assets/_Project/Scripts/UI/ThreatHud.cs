using System.Collections.Generic;
using CityDriver.Game;
using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.UI
{
    /// <summary>
    /// Makes chasers impossible to miss while driving: a pulsing reticle with name and distance over every police car, gang car and
    /// roadblock on screen; an arrow pinned to the screen edge pointing at each one that is off screen (behind or beside you); a red
    /// danger glow around the screen when one is close; and a CHASED banner with the heat level and the nearest distance.
    /// Colours match the radar and the maps (police flash red/blue, gang orange, roadblock yellow, stopped grey).
    /// </summary>
    public sealed class ThreatHud : MonoBehaviour
    {
        private sealed class Tag { public RectTransform root, ringRt, arrowRt; public Image ring, dot, arrow; public Text label; }

        private readonly List<Tag> tags = new List<Tag>();
        private RectTransform area;
        private Image vignette, bannerBg, bannerEdge;
        private Text bannerTitle, bannerSub;
        private CanvasGroup bannerGroup; private RectTransform bannerRt;

        /// <summary>Where the CHASED banner sits (canvas y of its top edge): it stacks under the route / mission cards in the same column.</summary>
        public void SetStackTop(float y) { if (bannerRt != null) bannerRt.anchoredPosition = new Vector2(330f, y); }
        private float bannerHold;
        private static Sprite vignetteSprite;

        public static ThreatHud Create(RectTransform parent)
        {
            var go = new GameObject("ThreatHud", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var hud = go.AddComponent<ThreatHud>(); hud.area = rt; hud.Build();
            return hud;
        }

        private static Sprite VignetteSprite()
        {
            if (vignetteSprite != null) return vignetteSprite;
            const int n = 96; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f; float d = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) * 0.6f + Mathf.Sqrt(u * u + v * v) * 0.4f;
                px[y * n + x] = new Color(1, 1, 1, Mathf.SmoothStep(0.55f, 1.15f, d));
            }
            tex.SetPixels(px); tex.Apply();
            return vignetteSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private void Build()
        {
            var vg = new GameObject("DangerGlow", typeof(RectTransform), typeof(Image)); vg.transform.SetParent(area, false);
            var vr = (RectTransform)vg.transform; vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.offsetMin = vr.offsetMax = Vector2.zero;
            vignette = vg.GetComponent<Image>(); vignette.sprite = VignetteSprite(); vignette.color = Color.clear; vignette.raycastTarget = false;

            var b = new GameObject("ChasedBanner", typeof(RectTransform), typeof(Image), typeof(CanvasGroup)); b.transform.SetParent(area, false);
            var br = (RectTransform)b.transform; br.anchorMin = br.anchorMax = new Vector2(0f, 1f); br.pivot = new Vector2(0f, 1f); br.anchoredPosition = new Vector2(330f, -30f); br.sizeDelta = new Vector2(620f, 86f); bannerRt = br;
            bannerBg = b.GetComponent<Image>(); bannerBg.color = new Color(0.30f, 0.04f, 0.06f, 0.92f); bannerBg.raycastTarget = false; bannerGroup = b.GetComponent<CanvasGroup>(); bannerGroup.alpha = 0f;
            var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image)); edge.transform.SetParent(b.transform, false);
            var er = (RectTransform)edge.transform; er.anchorMin = new Vector2(0, 0); er.anchorMax = new Vector2(0, 1); er.pivot = new Vector2(0, 0.5f); er.sizeDelta = new Vector2(10, 0); er.anchoredPosition = Vector2.zero;
            bannerEdge = edge.GetComponent<Image>(); bannerEdge.color = ThreatMarkers.PoliceRed; bannerEdge.raycastTarget = false;
            bannerTitle = ThreatMarkers.NewText(b.transform, "Title", 40, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(560, 56)); bannerTitle.font = UiTheme.Display; bannerTitle.fontStyle = FontStyle.Normal; bannerTitle.rectTransform.anchorMin = bannerTitle.rectTransform.anchorMax = new Vector2(0, 1); bannerTitle.rectTransform.pivot = new Vector2(0, 1); bannerTitle.rectTransform.anchoredPosition = new Vector2(26, -6);
            bannerSub = ThreatMarkers.NewText(b.transform, "Sub", 22, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(580, 30)); bannerSub.rectTransform.anchorMin = bannerSub.rectTransform.anchorMax = new Vector2(0, 0); bannerSub.rectTransform.pivot = new Vector2(0, 0); bannerSub.rectTransform.anchoredPosition = new Vector2(26, 9); bannerSub.color = new Color(1f, 0.82f, 0.8f);
        }

        private Tag GetTag(int i)
        {
            while (tags.Count <= i)
            {
                var t = new Tag { root = new GameObject("Tag", typeof(RectTransform)).GetComponent<RectTransform>() };
                t.root.SetParent(area, false); t.root.anchorMin = t.root.anchorMax = new Vector2(0.5f, 0.5f); t.root.sizeDelta = Vector2.zero;
                t.ring = ThreatMarkers.NewImage(t.root, "Ring", ThreatMarkers.Ring, new Vector2(84, 84)); t.ringRt = t.ring.rectTransform;
                t.dot = ThreatMarkers.NewImage(t.root, "Dot", ThreatMarkers.Dot, new Vector2(16, 16));
                t.arrow = ThreatMarkers.NewImage(t.root, "Arrow", ThreatMarkers.Arrow, new Vector2(54, 54)); t.arrowRt = t.arrow.rectTransform;
                t.label = ThreatMarkers.NewText(t.root, "Label", 24, TextAnchor.MiddleCenter, new Vector2(0, 62), new Vector2(260, 34));
                tags.Add(t);
            }
            return tags[i];
        }

        private void LateUpdate()
        {
            var threats = ThreatMarkers.Current();
            var cam = Camera.main;
            if (cam == null) foreach (var c0 in Camera.allCameras) if (c0.enabled && c0.targetTexture == null) { cam = c0; break; }
            var canvas = area.GetComponentInParent<Canvas>(); Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float time = Time.unscaledTime;
            Rect r = area.rect; float halfW = r.width * 0.5f, halfH = r.height * 0.5f, margin = 78f, marginX = 150f, marginTop = 320f, marginBottom = 330f;      // off-screen arrows stay clear of the HUD (speedometer, pedals, cards)
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            Vector3 carPos = camPos;
            var pm = PursuitManager.Instance;
            var player = FindPlayer(); if (player != null) carPos = player.position;

            float nearest = float.MaxValue; int police = 0, gang = 0, blocks = 0;
            for (int i = 0; i < tags.Count; i++) tags[i].root.gameObject.SetActive(false);
            for (int i = 0; i < threats.Count; i++)
            {
                var t = threats[i];
                float dist = Vector3.Distance(t.position, carPos);
                if (t.roadblock) blocks++; else if (t.kind == PursuerKind.Police) police++; else gang++;
                if (!t.roadblock && !t.disabled) nearest = Mathf.Min(nearest, dist);
                if (cam == null || dist > 420f) continue;

                Vector3 sp = cam.WorldToScreenPoint(t.position + Vector3.up * 1.6f);
                bool behind = sp.z < 0f;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, new Vector2(sp.x, sp.y), uiCam, out Vector2 local)) continue;
                if (behind) local = -local;                                           // behind the camera: mirror through the centre so the arrow points the right way
                bool inside = !behind && Mathf.Abs(local.x) < halfW - margin && local.y < halfH - margin && local.y > -halfH + margin;

                var tag = GetTag(i); tag.root.gameObject.SetActive(true);
                Color c = ThreatMarkers.ColourFor(t, time);
                string name = t.roadblock ? "ROADBLOCK" : t.disabled ? "STOPPED" : t.kind == PursuerKind.Police ? "POLICE" : "GANG";
                string text = name + "  " + Mathf.RoundToInt(dist) + " m";
                if (inside)
                {
                    tag.root.anchoredPosition = local;
                    float pulse = dist < 70f && !t.disabled ? 1f + 0.16f * Mathf.Sin(time * 12f) : 1f;
                    float size = Mathf.Clamp(150f - dist * 0.55f, 62f, 120f) * pulse;
                    tag.ring.gameObject.SetActive(true); tag.dot.gameObject.SetActive(true); tag.arrow.gameObject.SetActive(false);
                    tag.ringRt.sizeDelta = Vector2.one * size; tag.ring.color = c; tag.dot.color = c;
                    tag.label.rectTransform.anchoredPosition = new Vector2(0f, size * 0.5f + 20f);
                    tag.label.text = text; tag.label.color = c;
                }
                else
                {
                    Vector2 dir = local.sqrMagnitude < 1f ? Vector2.down : local.normalized;
                    float kx = Mathf.Abs(dir.x) > 0.001f ? (halfW - marginX) / Mathf.Abs(dir.x) : float.MaxValue;
                    float ky = Mathf.Abs(dir.y) < 0.001f ? float.MaxValue : dir.y > 0f ? (halfH - marginTop) / dir.y : (halfH - marginBottom) / -dir.y;
                    Vector2 edge = dir * Mathf.Min(kx, ky);
                    tag.root.anchoredPosition = edge;
                    float blink = dist < 45f ? 0.55f + 0.45f * Mathf.Sin(time * 14f) : 0.85f;
                    tag.ring.gameObject.SetActive(false); tag.dot.gameObject.SetActive(false); tag.arrow.gameObject.SetActive(true);
                    tag.arrowRt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
                    tag.arrowRt.sizeDelta = Vector2.one * Mathf.Lerp(42f, 66f, Mathf.Clamp01(1f - dist / 250f));
                    c.a = blink; tag.arrow.color = c;
                    Vector2 inward = -dir * 54f;                                      // the label sits inside the screen, next to the arrow
                    tag.label.rectTransform.anchoredPosition = inward + new Vector2(0f, dir.y > 0.6f ? -10f : 0f);
                    tag.label.text = text; tag.label.color = new Color(c.r, c.g, c.b, Mathf.Max(0.8f, blink));
                }
            }

            bool chased = threats.Count > 0 && (police + gang) > 0;
            bannerHold = chased ? 1f : Mathf.MoveTowards(bannerHold, 0f, Time.unscaledDeltaTime * 2f);
            bannerGroup.alpha = Mathf.Clamp01(bannerHold);
            if (chased)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * (nearest < 60f ? 9f : 4f));
                bannerBg.color = Color.Lerp(new Color(0.26f, 0.03f, 0.05f, 0.92f), new Color(0.46f, 0.05f, 0.08f, 0.95f), pulse);
                bannerEdge.color = Mathf.FloorToInt(time * 3f) % 2 == 0 ? ThreatMarkers.PoliceRed : ThreatMarkers.PoliceBlue;
                int heat = pm != null ? pm.Heat : 0;
                bannerTitle.text = (police > 0 && gang > 0 ? "POLICE + GANG" : police > 0 ? "POLICE CHASE" : "GANG ATTACK") + (heat > 0 ? "   HEAT " + heat + "/5" : "");
                bannerTitle.color = Color.white;
                bannerSub.text = (nearest < float.MaxValue ? $"Nearest {Mathf.RoundToInt(nearest)} m   -   {police} police, {gang} gang" + (blocks > 0 ? $", {blocks} block" : "") : "Stay on the move") + (pm != null && pm.BustedProgress01 > 0.05f ? $"   -   BUSTED {Mathf.RoundToInt(pm.BustedProgress01 * 100f)}%" : "");
            }

            float danger = nearest < 32f ? (1f - nearest / 32f) : 0f;
            float flash = danger > 0f ? (0.55f + 0.45f * Mathf.Sin(time * 11f)) : 0f;
            vignette.color = new Color(1f, 0.08f, 0.06f, danger * 0.55f * flash);
        }

        private Transform playerCache;
        private Transform FindPlayer()
        {
            if (playerCache == null) { var v = FindFirstObjectByType<CityDriver.Vehicle.VehicleController>(); if (v != null) playerCache = v.transform; }
            return playerCache;
        }
    }
}
