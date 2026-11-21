using System.Collections.Generic;
using CityDriver.Game;
using CityDriver.Navigation;
using CityDriver.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CityDriver.UI
{
    /// <summary>
    /// Full-screen city map: the whole road network with districts, points of interest, the player and the GPS route.
    /// Tap (or click) anywhere to plan a route there; tapping near a point of interest selects it by name.
    /// Toggle with the Map button or the M key.
    /// </summary>
    public sealed class FullMapView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private RectTransform mapRect;         // square map area (also the click target)
        [SerializeField] private RawImage baseMap;
        [SerializeField] private RawImage routeOverlay;
        [SerializeField] private RectTransform playerArrow;
        [SerializeField] private Text infoText;
        [Tooltip("Tapping within this many metres of a point of interest selects that point of interest.")]
        [SerializeField, Min(5f)] private float poiSnapMetres = 70f;

        private Transform viewer;
        private bool isOpen;
        private Map3DView map3d;
        private bool mode3d = true;
        private Text toggleText;
        private readonly List<Image> blips2d = new List<Image>();
        private GameObject legend;
        private Text legendCount;
        private string threatSummary = "";

        public bool IsOpen => isOpen;

        public void SetViewer(Transform newViewer) { viewer = newViewer; if (map3d != null) map3d.SetViewer(viewer); }

        private void Start() => SetOpen(false);

        public void Toggle() => SetOpen(!isOpen);

        public void SetOpen(bool open)
        {
            isOpen = open;
            if (panel != null) panel.SetActive(open);
            if (open && routeOverlay != null && NavigationService.Instance != null) routeOverlay.texture = NavigationService.Instance.RouteOverlay;
            if (open) { EnsureToggle(); ApplyMode(); Refresh(); }
            else if (map3d != null) map3d.SetActive(false);
        }

        private void EnsureToggle()
        {
            if (toggleText != null || panel == null) return;
            var g = new GameObject("MapModeButton", typeof(RectTransform), typeof(Image), typeof(Button)); g.transform.SetParent(panel.transform, false);
            var r = (RectTransform)g.transform; r.anchorMin = r.anchorMax = new Vector2(1f, 0f); r.pivot = new Vector2(1f, 0f); r.anchoredPosition = new Vector2(-60f, 160f); r.sizeDelta = new Vector2(230f, 80f);
            g.GetComponent<Image>().color = new Color(0.17f, 0.21f, 0.29f, 1f); g.GetComponent<Button>().onClick.AddListener(() => { mode3d = !mode3d; ApplyMode(); });
            var t = new GameObject("Label", typeof(RectTransform), typeof(Text)); t.transform.SetParent(g.transform, false);
            var tr = (RectTransform)t.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            toggleText = t.GetComponent<Text>(); toggleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); toggleText.fontSize = 32; toggleText.fontStyle = FontStyle.Bold; toggleText.alignment = TextAnchor.MiddleCenter; toggleText.color = Color.white; toggleText.raycastTarget = false;
        }

        /// <summary>Shows the 3D city model (default) or the flat road map.</summary>
        private void ApplyMode()
        {
            if (mapRect == null) return;
            bool use3d = mode3d && Resources.Load<GameObject>("Map3D/CityMapModel") != null;
            if (use3d)
            {
                if (map3d == null) { map3d = Map3DView.Create(panel.transform, mapRect, this); map3d.SetViewer(viewer); }
                map3d.SetActive(true);
            }
            else if (map3d != null) map3d.SetActive(false);
            if (baseMap != null) baseMap.gameObject.SetActive(!use3d);
            if (routeOverlay != null) routeOverlay.gameObject.SetActive(!use3d);
            if (playerArrow != null) playerArrow.gameObject.SetActive(!use3d);
            if (toggleText != null) toggleText.text = use3d ? "2D MAP" : "3D MAP";
        }

        /// <summary>Called by the 3D map after a tap planned (or failed to plan) a route.</summary>
        public void NotifyDestination(string label) { if (infoText != null) infoText.text = label; Refresh(); }

        public void ClearRoute() { NavigationService.Instance?.ClearDestination(); Refresh(); }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.mKey.wasPressedThisFrame) Toggle();
            if (isOpen) Refresh();
        }

        private void Refresh()
        {
            if (viewer != null && playerArrow != null && mapRect != null)
            {
                float half = RoadMapRenderer.MapHalfExtent;
                Vector3 p = viewer.position;
                Vector2 size = mapRect.rect.size;
                playerArrow.anchoredPosition = new Vector2(((p.x + half) / (half * 2f) - 0.5f) * size.x, ((p.z + half) / (half * 2f) - 0.5f) * size.y);
                playerArrow.localRotation = Quaternion.Euler(0f, 0f, -viewer.eulerAngles.y);   // north is up on this map
            }

            UpdateThreats();
            if (infoText == null) return;
            NavigationService nav = NavigationService.Instance;
            if (nav == null || !nav.HasDestination) { infoText.text = threatSummary.Length > 0 ? "You are being chased!  Keep moving." : "Tap the map to set a destination"; return; }
            float km = nav.DistanceRemaining / 1000f;
            string dist = km >= 1f ? km.ToString("0.0") + " km" : Mathf.RoundToInt(nav.DistanceRemaining / 10f) * 10 + " m";
            infoText.text = $"{nav.DestinationLabel}   {dist}   about {Mathf.Max(1, Mathf.RoundToInt(nav.EtaSeconds / 60f))} min";
        }

        /// <summary>Chasers on the flat map (the 3D map draws its own) plus a legend that appears while anything is after you.</summary>
        private void UpdateThreats()
        {
            if (mapRect == null) return;
            var threats = ThreatMarkers.Current();
            int police = 0, gang = 0, blocks = 0;
            foreach (var t in threats) { if (t.roadblock) blocks++; else if (t.kind == PursuerKind.Police) police++; else gang++; }
            threatSummary = threats.Count == 0 ? "" : ("CHASERS   Police " + police + "   Gang " + gang + (blocks > 0 ? "   Roadblock " + blocks : ""));
            EnsureLegend();
            legend.SetActive(threats.Count > 0);
            if (threats.Count > 0 && legendCount != null) legendCount.text = threatSummary;

            bool flat = baseMap != null && baseMap.gameObject.activeSelf;
            for (int i = 0; i < blips2d.Count; i++) blips2d[i].gameObject.SetActive(false);
            if (!flat) return;
            float half = RoadMapRenderer.MapHalfExtent; Vector2 size = mapRect.rect.size; float time = Time.unscaledTime;
            for (int i = 0; i < threats.Count; i++)
            {
                while (blips2d.Count <= i) blips2d.Add(ThreatMarkers.NewImage(mapRect, "Chaser", ThreatMarkers.Dot, new Vector2(34, 34)));
                var t = threats[i]; var b = blips2d[i]; b.gameObject.SetActive(true);
                var want = t.roadblock ? ThreatMarkers.Diamond : ThreatMarkers.Dot; if (b.sprite != want) b.sprite = want;
                b.rectTransform.anchoredPosition = new Vector2(((t.position.x + half) / (half * 2f) - 0.5f) * size.x, ((t.position.z + half) / (half * 2f) - 0.5f) * size.y);
                b.rectTransform.sizeDelta = Vector2.one * (t.roadblock ? 36f : 32f) * (t.disabled ? 0.8f : 1f + 0.18f * Mathf.Sin(time * 10f));
                b.color = ThreatMarkers.ColourFor(t, time);
            }
        }

        private void EnsureLegend()
        {
            if (legend != null || panel == null) return;
            legend = new GameObject("ThreatLegend", typeof(RectTransform), typeof(Image)); legend.transform.SetParent(mapRect, false);
            var r = (RectTransform)legend.transform; r.anchorMin = r.anchorMax = new Vector2(1f, 1f); r.pivot = new Vector2(1f, 1f); r.anchoredPosition = new Vector2(-10f, -10f); r.sizeDelta = new Vector2(760f, 120f);
            legend.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.1f, 0.86f); legend.GetComponent<Image>().raycastTarget = false;
            legendCount = ThreatMarkers.NewText(legend.transform, "Count", 30, TextAnchor.MiddleLeft, new Vector2(0, 28), new Vector2(720, 44)); legendCount.color = new Color(1f, 0.55f, 0.5f);
            string[] names = { "POLICE", "GANG", "ROADBLOCK", "STOPPED" }; Color[] cols = { ThreatMarkers.PoliceRed, ThreatMarkers.Gang, ThreatMarkers.Block, ThreatMarkers.Down };
            for (int i = 0; i < 4; i++)
            {
                float x = -330 + i * 185;
                var ic = ThreatMarkers.NewImage(legend.transform, "Icon", i == 2 ? ThreatMarkers.Diamond : ThreatMarkers.Dot, new Vector2(28, 28)); ic.color = cols[i]; ic.rectTransform.anchoredPosition = new Vector2(x, -24);
                var tx = ThreatMarkers.NewText(legend.transform, "Name", 24, TextAnchor.MiddleLeft, new Vector2(x + 20, -24), new Vector2(150, 34)); tx.text = names[i]; tx.rectTransform.pivot = new Vector2(0f, 0.5f);
            }
        }

        /// <summary>Called by <see cref="MapClickReceiver"/> when the map image is tapped.</summary>
        public void OnMapClicked(PointerEventData eventData)
        {
            NavigationService nav = NavigationService.Instance;
            if (nav == null || mapRect == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRect, eventData.position, null, out Vector2 local)) return;

            Vector2 size = mapRect.rect.size;
            float half = RoadMapRenderer.MapHalfExtent;
            var world = new Vector3(((local.x / size.x) * 2f) * half, 0f, ((local.y / size.y) * 2f) * half);
            if (Mathf.Abs(world.x) > half || Mathf.Abs(world.z) > half) return;

            string label = "Map destination";
            var data = nav.Data;
            if (data != null)
            {
                float best = poiSnapMetres;
                foreach (var poi in data.pois)
                {
                    float d = Vector3.Distance(new Vector3(poi.position.x, 0, poi.position.z), world);
                    if (d < best) { best = d; label = poi.displayName; world = poi.position; }
                }
            }

            if (!nav.SetDestination(world, label)) infoText.text = "No route found";
            else Refresh();
        }
    }
}
