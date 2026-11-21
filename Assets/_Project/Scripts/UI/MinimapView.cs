using System.Collections.Generic;
using CityDriver.Game;
using CityDriver.Navigation;
using CityDriver.World;
using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.UI
{
    /// <summary>
    /// Driving minimap. Shows a window of the real road-map texture centred on the player and rotated so the car's heading
    /// is always "up", with the live GPS route drawn on top. The player arrow stays fixed in the centre.
    /// </summary>
    public sealed class MinimapView : MonoBehaviour
    {
        [SerializeField] private RectTransform mapContainer;   // rotates with the heading; holds both images
        [SerializeField] private RawImage baseMap;
        [SerializeField] private RawImage routeOverlay;
        [Tooltip("Metres visible across the minimap.")]
        [SerializeField, Min(50f)] private float windowMetres = 340f;
        [SerializeField, Min(1f)] private float overscan = 1.5f;   // image is larger than the mask so rotation never shows corners

        private Transform viewer;

        // radar dressing and chaser blips, created on first use at runtime
        private RectTransform deco;
        private Image sweepImg, alertRing;
        private Text threatText;
        private Image threatPill;
        private readonly List<Image> blips = new List<Image>();
        private readonly List<Image> pulses = new List<Image>();
        private float radius = 140f;

        public void SetViewer(Transform newViewer) => viewer = newViewer;

        private void OnEnable() => BindOverlay();

        private void BindOverlay()
        {
            NavigationService nav = NavigationService.Instance;
            if (nav != null && routeOverlay != null) routeOverlay.texture = nav.RouteOverlay;
        }

        private void LateUpdate()
        {
            if (viewer == null || baseMap == null) return;
            if (routeOverlay != null && routeOverlay.texture == null) BindOverlay();   // navigation may start after this view

            float half = RoadMapRenderer.MapHalfExtent;
            Vector3 p = viewer.position;
            float span = windowMetres * overscan / (half * 2f);
            var centre = new Vector2((p.x + half) / (half * 2f), (p.z + half) / (half * 2f));
            var uv = new Rect(centre.x - span * 0.5f, centre.y - span * 0.5f, span, span);

            baseMap.uvRect = uv;
            if (routeOverlay != null) routeOverlay.uvRect = uv;
            if (mapContainer != null) mapContainer.localRotation = Quaternion.Euler(0f, 0f, viewer.eulerAngles.y);   // heading up
            UpdateRadar();
        }

        private void EnsureDeco()
        {
            if (deco != null || mapContainer == null) return;
            var host = (RectTransform)mapContainer.parent;
            radius = Mathf.Min(host.rect.width, host.rect.height) * 0.5f;
            deco = new GameObject("RadarDeco", typeof(RectTransform)).GetComponent<RectTransform>(); deco.SetParent(host, false);
            deco.anchorMin = deco.anchorMax = new Vector2(0.5f, 0.5f); deco.sizeDelta = new Vector2(radius * 2f, radius * 2f); deco.anchoredPosition = Vector2.zero;
            deco.SetSiblingIndex(mapContainer.GetSiblingIndex() + 1);                       // above the map, below the player arrow

            sweepImg = ThreatMarkers.NewImage(deco, "Sweep", ThreatMarkers.Sweep, new Vector2(radius * 2f, radius * 2f)); sweepImg.color = new Color(0.35f, 1f, 0.7f, 0.22f);
            foreach (float f in new[] { 0.34f, 0.67f, 0.985f })
            { var r = ThreatMarkers.NewImage(deco, "Range", ThreatMarkers.Ring, new Vector2(radius * 2f * f, radius * 2f * f)); r.color = new Color(1f, 1f, 1f, f > 0.9f ? 0.0f : 0.22f); }
            alertRing = ThreatMarkers.NewImage(deco, "Alert", ThreatMarkers.Ring, new Vector2(radius * 2f, radius * 2f)); alertRing.color = Color.clear;
            var pill = ThreatMarkers.NewImage(deco, "ThreatPill", null, new Vector2(radius * 1.25f, 26f)); pill.color = new Color(0f, 0f, 0f, 0f); pill.rectTransform.anchoredPosition = new Vector2(0f, -radius * 0.52f); threatPill = pill;
            threatText = ThreatMarkers.NewText(pill.transform, "ThreatText", 17, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(radius * 1.25f, 26f)); threatText.text = "";
            // the player arrow stays on top of the decoration
            var arrow = host.Find("PlayerArrow"); if (arrow != null) arrow.SetAsLastSibling();
            deco.SetAsLastSibling(); if (arrow != null) arrow.SetAsLastSibling();
        }

        private Image Blip(List<Image> pool, int i, Sprite sprite)
        {
            while (pool.Count <= i) { var b = ThreatMarkers.NewImage(deco, "Chaser", sprite, new Vector2(22, 22)); pool.Add(b); }
            return pool[i];
        }

        /// <summary>Round-radar dressing: range rings, a scanning sweep, and a blip for every police car, gang car and roadblock.
        /// Chasers inside the radar show as dots; chasers outside are pinned to the rim as arrows so you always know which way they come from.</summary>
        private void UpdateRadar()
        {
            EnsureDeco();
            if (deco == null) return;
            float time = Time.unscaledTime;
            var threats = ThreatMarkers.Current();
            sweepImg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, time * 120f);

            float k = baseMap.rectTransform.rect.width / (windowMetres * overscan);        // pixels per metre on screen
            float yaw = viewer.eulerAngles.y * Mathf.Deg2Rad, cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            float clampR = radius - 15f, nearest = float.MaxValue; int police = 0, gang = 0, blocks = 0;
            Vector3 vp = viewer.position;
            for (int i = 0; i < blips.Count; i++) blips[i].gameObject.SetActive(false);
            int used = 0;
            foreach (var t in threats)
            {
                float dx = t.position.x - vp.x, dz = t.position.z - vp.z;
                float metres = Mathf.Sqrt(dx * dx + dz * dz);
                if (t.roadblock) blocks++; else if (t.kind == PursuerKind.Police) police++; else gang++;
                if (!t.roadblock && !t.disabled) nearest = Mathf.Min(nearest, metres);
                var sv = new Vector2((dx * cs - dz * sn) * k, (dx * sn + dz * cs) * k);   // world offset turned so the car heading is up
                bool outside = sv.magnitude > clampR;
                Sprite want = outside ? ThreatMarkers.Arrow : t.roadblock ? ThreatMarkers.Diamond : ThreatMarkers.Dot;
                var b = Blip(blips, used++, want); b.gameObject.SetActive(true);
                if (b.sprite != want) b.sprite = want;
                Color c = ThreatMarkers.ColourFor(t, time);
                var rt = b.rectTransform;
                if (outside)
                {
                    Vector2 dir = sv.normalized; rt.anchoredPosition = dir * clampR;
                    rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
                    float near = Mathf.Clamp01(1f - (metres - 170f) / 260f);              // farther = smaller and fainter
                    rt.sizeDelta = Vector2.one * Mathf.Lerp(14f, 24f, near); c.a = Mathf.Lerp(0.55f, 1f, near);
                }
                else
                {
                    rt.anchoredPosition = sv; rt.localRotation = Quaternion.identity;
                    float pulse = metres < 70f && !t.disabled ? 1f + 0.25f * Mathf.Sin(time * 14f) : 1f;
                    rt.sizeDelta = Vector2.one * (t.roadblock ? 22f : 20f) * pulse;
                }
                b.color = c;
            }

            // alert ring + text
            bool chased = threats.Count > 0;
            if (chased)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * (nearest < 60f ? 10f : 5f));
                alertRing.color = new Color(1f, 0.2f, 0.15f, 0.35f + 0.65f * pulse); alertRing.rectTransform.sizeDelta = Vector2.one * radius * 2f * (0.97f + 0.03f * pulse);
                string label = police > 0 && gang > 0 ? "POLICE+GANG" : police > 0 ? "POLICE" : gang > 0 ? "GANG" : blocks > 0 ? "ROADBLOCK" : "";
                if (nearest < float.MaxValue) label += "  " + Mathf.RoundToInt(nearest) + " m";
                threatText.text = label; threatText.color = Color.Lerp(Color.white, new Color(1f, 0.55f, 0.5f), pulse); threatPill.color = new Color(0f, 0f, 0f, 0.62f);
            }
            else { alertRing.color = Color.clear; threatPill.color = Color.clear; if (threatText.text.Length > 0) threatText.text = ""; }
        }
    }
}
