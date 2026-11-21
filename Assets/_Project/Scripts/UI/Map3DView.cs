using System.Collections.Generic;
using CityDriver.Game;
using CityDriver.Navigation;
using CityDriver.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CityDriver.UI
{
    /// <summary>
    /// 3D city map for the full-screen map: the baked low-poly city model (Resources/Map3D/CityMapModel) seen from a tilted camera that can be panned,
    /// zoomed and rotated. Shows district names, points of interest, the player's car (a scaled copy of the real body), the GPS route and the
    /// destination; tap to plan a route. The model sits far below the city so it never overlaps the live world.
    /// </summary>
    public sealed class Map3DView : MonoBehaviour, IPointerClickHandler, IDragHandler, IBeginDragHandler, IEndDragHandler, IScrollHandler
    {
        private const float StageY = -6000f;
        private RenderTexture rt;
        private Camera cam;
        private Transform stage, marker, destPin;
        private RawImage image;
        private RectTransform rect;
        private LineRenderer routeLine;
        private Transform viewer;
        private Vector3 focus;
        private float yaw, pitch = 56f, dist = 760f;
        private bool dragging, follow = true, built, fogOverridden, prevFog;
        private int routeVersion = -1;
        private FullMapView owner;
        private sealed class ThreatPin { public Transform root, ball, beam; public Renderer ballR, beamR; }
        private readonly List<ThreatPin> pins = new List<ThreatPin>();
        private Material matRed, matBlue, matGang, matBlock, matDown;

        public static Map3DView Create(Transform panel, RectTransform mapRect, FullMapView owner)
        {
            var go = new GameObject("Map3D", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(panel, false);
            var v = go.AddComponent<Map3DView>(); v.owner = owner; v.rect = (RectTransform)go.transform;
            v.rect.anchorMin = mapRect.anchorMin; v.rect.anchorMax = mapRect.anchorMax; v.rect.pivot = mapRect.pivot; v.rect.anchoredPosition = mapRect.anchoredPosition; v.rect.sizeDelta = mapRect.sizeDelta;
            v.image = go.GetComponent<RawImage>(); v.image.raycastTarget = true;
            v.AddButtons();
            return v;
        }

        public bool Available => Resources.Load<GameObject>("Map3D/CityMapModel") != null;

        public void SetViewer(Transform t) { viewer = t; }

        private void Build()
        {
            built = true;
            rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2, name = "Map3DRT" }; rt.Create();
            stage = new GameObject("Map3DStage").transform; stage.position = new Vector3(0f, StageY, 0f); DontDestroyOnLoad(stage.gameObject);
            var prefab = Resources.Load<GameObject>("Map3D/CityMapModel");
            if (prefab != null) Instantiate(prefab, stage);
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.transform.SetParent(stage, false); sun.type = LightType.Directional; sun.intensity = 2.0f; sun.color = new Color(1f, 0.95f, 0.9f); sun.transform.rotation = Quaternion.Euler(52f, -30f, 0f); sun.shadows = LightShadows.None;
            var fill = new GameObject("Fill").AddComponent<Light>(); fill.transform.SetParent(stage, false); fill.type = LightType.Directional; fill.intensity = 0.6f; fill.color = new Color(0.6f, 0.7f, 1f); fill.transform.rotation = Quaternion.Euler(30f, 150f, 0f);
            cam = new GameObject("Map3DCam").AddComponent<Camera>(); cam.transform.SetParent(stage, false);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f); cam.fieldOfView = 38f; cam.nearClipPlane = 5f; cam.farClipPlane = 6000f; cam.targetTexture = rt; cam.enabled = false;
            var ad = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); ad.renderPostProcessing = false; ad.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
            image.texture = rt;

            // district names (billboards)
            var nav = NavigationService.Instance; var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (nav != null && nav.Data != null)
            {
                foreach (var dr in nav.Data.districts)
                {
                    var tg = new GameObject("District_" + dr.name); tg.transform.SetParent(stage, false);
                    tg.transform.localPosition = new Vector3((dr.min.x + dr.max.x) * 0.5f, 70f, (dr.min.y + dr.max.y) * 0.5f);
                    var tm = tg.AddComponent<TextMesh>(); tm.text = dr.name.ToUpperInvariant(); tm.font = font; tm.fontSize = 64; tm.characterSize = 5.5f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = new Color(1f, 1f, 1f, 0.85f);
                    tg.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                }
                // points of interest: coloured pillars
                foreach (var poi in nav.Data.pois)
                {
                    var pil = GameObject.CreatePrimitive(PrimitiveType.Cube); pil.name = "POI_" + poi.displayName; pil.transform.SetParent(stage, false);
                    Destroy(pil.GetComponent<Collider>());
                    pil.transform.localPosition = new Vector3(poi.position.x, 30f, poi.position.z); pil.transform.localScale = new Vector3(16f, 60f, 16f);
                    pil.GetComponent<Renderer>().sharedMaterial = CityDriver.Game.StageMaterial.Make(PoiColor(poi.type));
                }
            }

            var lineGo = new GameObject("Route"); lineGo.transform.SetParent(stage, false);
            routeLine = lineGo.AddComponent<LineRenderer>(); routeLine.widthMultiplier = 11f; routeLine.numCapVertices = 4; routeLine.numCornerVertices = 4; routeLine.alignment = LineAlignment.View; routeLine.useWorldSpace = false;
            routeLine.material = CityDriver.Game.StageMaterial.Make(new Color(1f, 0.85f, 0.15f));
            routeLine.startColor = routeLine.endColor = new Color(1f, 0.85f, 0.15f); routeLine.positionCount = 0;

            destPin = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform; destPin.name = "Destination"; destPin.SetParent(stage, false); Destroy(destPin.GetComponent<Collider>());
            destPin.localScale = Vector3.one * 34f; destPin.GetComponent<Renderer>().sharedMaterial = CityDriver.Game.StageMaterial.Make(new Color(1f, 0.25f, 0.2f)); destPin.gameObject.SetActive(false);
        }

        private static Color PoiColor(PoiType t)
        {
            switch (t)
            {
                case PoiType.Hospital: return new Color(1f, 0.3f, 0.3f);
                case PoiType.PoliceStation: return new Color(0.3f, 0.5f, 1f);
                case PoiType.PetrolStation: return new Color(0.3f, 0.9f, 0.4f);
                case PoiType.Garage: case PoiType.RepairWorkshop: return new Color(1f, 0.8f, 0.2f);
                case PoiType.SafeHouse: return new Color(0.8f, 0.4f, 1f);
                default: return new Color(1f, 0.6f, 0.9f);
            }
        }

        private void AddButtons()
        {
            void B(string label, Vector2 pos, UnityEngine.Events.UnityAction act)
            {
                var g = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button)); g.transform.SetParent(transform, false);
                var r = (RectTransform)g.transform; r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f); r.anchoredPosition = pos; r.sizeDelta = new Vector2(96, 64);
                g.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.14f, 0.88f); g.GetComponent<Button>().onClick.AddListener(act);
                var t = new GameObject("Text", typeof(RectTransform), typeof(Text)); t.transform.SetParent(g.transform, false);
                var tr = (RectTransform)t.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
                var tx = t.GetComponent<Text>(); tx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); tx.text = label; tx.fontSize = 26; tx.fontStyle = FontStyle.Bold; tx.alignment = TextAnchor.MiddleCenter; tx.color = Color.white; tx.raycastTarget = false;
            }
            B("+", new Vector2(14, -14), () => dist = Mathf.Max(120f, dist * 0.8f));
            B("-", new Vector2(14, -86), () => dist = Mathf.Min(2600f, dist * 1.25f));
            B("<", new Vector2(14, -170), () => yaw -= 30f);
            B(">", new Vector2(14, -242), () => yaw += 30f);
            B("TILT", new Vector2(14, -326), () => pitch = pitch > 70f ? 38f : pitch + 16f);
            B("ME", new Vector2(14, -398), () => { follow = true; });
        }

        public void SetActive(bool on)
        {
            gameObject.SetActive(on);
            if (!on) { if (cam != null) cam.enabled = false; RestoreFog(); return; }
            if (!fogOverridden) { prevFog = RenderSettings.fog; RenderSettings.fog = false; fogOverridden = true; }       // distance fog would cover the whole map
            if (!built) Build();
            if (stage != null) stage.gameObject.SetActive(true);
            follow = true; if (viewer != null) focus = viewer.position;
            RebuildMarker();
        }

        private void RestoreFog() { if (fogOverridden) { RenderSettings.fog = prevFog; fogOverridden = false; } }

        private void OnDisable() { if (cam != null) cam.enabled = false; RestoreFog(); }

        private void RebuildMarker()
        {
            if (marker != null) Destroy(marker.gameObject);
            marker = new GameObject("PlayerMarker").transform; marker.SetParent(stage, false);
            var body = viewer != null ? viewer.Find("Body") : null;
            if (body != null)
            {
                var copy = Instantiate(body.gameObject, marker); copy.name = "Car"; copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.identity; copy.transform.localScale = Vector3.one * 7f;
                foreach (var l in copy.GetComponentsInChildren<Light>(true)) l.enabled = false;
            }
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube); beam.transform.SetParent(marker, false); Destroy(beam.GetComponent<Collider>());
            beam.transform.localPosition = new Vector3(0f, 90f, 0f); beam.transform.localScale = new Vector3(5f, 180f, 5f);
            beam.GetComponent<Renderer>().sharedMaterial = CityDriver.Game.StageMaterial.Make(new Color(1f, 0.9f, 0.2f));
        }

        private void LateUpdate()
        {
            if (!built || cam == null) return;
            if (viewer != null)
            {
                if (follow && !dragging) focus = Vector3.Lerp(focus, viewer.position, 0.2f);
                marker.localPosition = new Vector3(viewer.position.x, 1f, viewer.position.z); marker.localRotation = Quaternion.Euler(0f, viewer.eulerAngles.y, 0f);
            }
            UpdateThreatPins();
            var nav = NavigationService.Instance;
            if (nav != null)
            {
                if (nav.RouteVersion != routeVersion) { routeVersion = nav.RouteVersion; RefreshRoute(nav); }
                destPin.gameObject.SetActive(nav.HasDestination);
                if (nav.HasDestination) destPin.localPosition = new Vector3(nav.Destination.x, 60f, nav.Destination.z);
            }
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 f = new Vector3(focus.x, 0f, focus.z);
            cam.transform.localPosition = f + rot * new Vector3(0f, 0f, -dist);
            cam.transform.LookAt(stage.position + f);
            cam.enabled = true;
            foreach (var tm in stage.GetComponentsInChildren<TextMesh>()) tm.transform.rotation = cam.transform.rotation;
        }

        /// <summary>Police, gang cars and roadblocks as glowing pins with tall beams so they can be spotted from any angle and zoom.</summary>
        private void UpdateThreatPins()
        {
            if (matRed == null)
            {
                matRed = StageMaterial.Make(ThreatMarkers.PoliceRed); matBlue = StageMaterial.Make(ThreatMarkers.PoliceBlue);
                matGang = StageMaterial.Make(ThreatMarkers.Gang); matBlock = StageMaterial.Make(ThreatMarkers.Block); matDown = StageMaterial.Make(ThreatMarkers.Down);
            }
            var threats = ThreatMarkers.Current(); float time = Time.unscaledTime;
            while (pins.Count < threats.Count)
            {
                var pin = new ThreatPin { root = new GameObject("Threat").transform };
                pin.root.SetParent(stage, false);
                pin.ball = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform; pin.ball.SetParent(pin.root, false); Destroy(pin.ball.GetComponent<Collider>()); pin.ballR = pin.ball.GetComponent<Renderer>();
                pin.beam = GameObject.CreatePrimitive(PrimitiveType.Cube).transform; pin.beam.SetParent(pin.root, false); Destroy(pin.beam.GetComponent<Collider>()); pin.beamR = pin.beam.GetComponent<Renderer>();
                pin.beam.localScale = new Vector3(8f, 420f, 8f); pin.beam.localPosition = new Vector3(0f, 210f, 0f);
                pins.Add(pin);
            }
            for (int i = 0; i < pins.Count; i++)
            {
                var pin = pins[i]; bool on = i < threats.Count;
                if (pin.root.gameObject.activeSelf != on) pin.root.gameObject.SetActive(on);
                if (!on) continue;
                var t = threats[i];
                pin.root.localPosition = new Vector3(t.position.x, 4f, t.position.z);
                float pulse = t.disabled ? 1f : 1f + 0.22f * Mathf.Sin(time * 9f + i);
                pin.ball.localPosition = new Vector3(0f, 30f, 0f);
                pin.ball.localScale = t.roadblock ? new Vector3(64f, 40f, 64f) : Vector3.one * 52f * pulse;
                Material m = t.disabled ? matDown : t.roadblock ? matBlock : t.kind == PursuerKind.Police ? (Mathf.FloorToInt(time * 4f) % 2 == 0 ? matRed : matBlue) : matGang;
                pin.ballR.sharedMaterial = m; pin.beamR.sharedMaterial = m;
            }
        }

        private void RefreshRoute(NavigationService nav)
        {
            var route = nav.Route;
            if (route == null || !nav.HasDestination || route.points == null || route.points.Count < 2) { routeLine.positionCount = 0; return; }
            routeLine.positionCount = route.points.Count;
            for (int i = 0; i < route.points.Count; i++) routeLine.SetPosition(i, new Vector3(route.points[i].x, 3f, route.points[i].z));
        }

        // ------------------------------------------------------------------------------------------ input
        public void OnBeginDrag(PointerEventData e) { dragging = true; follow = false; }
        public void OnEndDrag(PointerEventData e) { dragging = false; }
        public void OnDrag(PointerEventData e)
        {
            float k = dist * 0.0017f; Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            focus -= yawRot * new Vector3(e.delta.x, 0f, e.delta.y / Mathf.Max(0.35f, Mathf.Sin(pitch * Mathf.Deg2Rad))) * k;
            float lim = CityLayout.HalfSize + 200f; focus.x = Mathf.Clamp(focus.x, -lim, lim); focus.z = Mathf.Clamp(focus.z, -lim, lim);
        }
        public void OnScroll(PointerEventData e) { dist = Mathf.Clamp(dist * Mathf.Exp(-e.scrollDelta.y * 0.08f), 120f, 2600f); }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging || cam == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, null, out Vector2 local)) return;
            Vector2 uv = new Vector2(local.x / rect.rect.width + rect.pivot.x, local.y / rect.rect.height + rect.pivot.y);
            Ray ray = cam.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
            var plane = new Plane(Vector3.up, new Vector3(0f, StageY, 0f));
            if (!plane.Raycast(ray, out float t)) return;
            Vector3 world = ray.GetPoint(t) - new Vector3(0f, StageY, 0f); world.y = 0f;
            var nav = NavigationService.Instance; if (nav == null) return;
            string label = "Map destination";
            if (nav.Data != null)
            {
                float best = 80f;
                foreach (var poi in nav.Data.pois) { float d = Vector3.Distance(new Vector3(poi.position.x, 0, poi.position.z), world); if (d < best) { best = d; label = poi.displayName; world = poi.position; } }
            }
            nav.SetDestination(world, label);
            if (owner != null) owner.NotifyDestination(nav.HasDestination ? label : "No route found");
        }

        private void OnDestroy() { if (rt != null) rt.Release(); if (stage != null) Destroy(stage.gameObject); }
    }
}
