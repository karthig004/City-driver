using CityDriver.Controls;
using CityDriver.Game;
using UnityEngine;

namespace CityDriver.Vehicle
{
    /// <summary>
    /// Visual-only lamps. Tail lights glow at night and flare when braking, reverse lamps light when reversing, headlights follow
    /// <see cref="HeadlightsOn"/>: strong spot lights that actually light the road, a visible beam cone, and soft halos on every lamp
    /// (so the car's lights read clearly from the chase camera and from other cars).
    /// </summary>
    public sealed class VehicleLights : MonoBehaviour
    {
        [SerializeField] private Color tailDay = new Color(0.45f, 0.03f, 0.03f);
        [SerializeField] private Color tailNight = new Color(2.2f, 0.06f, 0.05f);
        [SerializeField] private Color tailBrake = new Color(5f, 0.15f, 0.1f);
        [SerializeField] private Color headOn = new Color(4.2f, 4.0f, 3.4f);
        [SerializeField] private Color headOff = new Color(0.25f, 0.25f, 0.23f);
        [SerializeField] private Color reverseOn = new Color(2.4f, 2.4f, 2.4f);

        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private Renderer[] tail, head, reverse;
        private MaterialPropertyBlock block;
        private VehicleInputHandler input;
        private VehicleController car;
        private int lastTail = -1, lastHead = -1, lastRev = -1;

        private Light[] beams;
        private float beamRange = 24f;
        private readonly System.Collections.Generic.List<GlowBillboard> headGlows = new System.Collections.Generic.List<GlowBillboard>(), tailGlows = new System.Collections.Generic.List<GlowBillboard>();
        private readonly System.Collections.Generic.List<GameObject> built = new System.Collections.Generic.List<GameObject>();
        private MeshRenderer[] cones;
        private float coneAlpha = -1f;

        public bool HeadlightsOn { get; set; }
        public int ActiveBeams { get { int n = 0; if (beams != null) foreach (var b in beams) if (b != null && b.enabled) n++; return n; } }

        /// <summary>Headlight upgrade: how far the beams reach (metres). The lights are tuned so the road is lit well beyond this at night.</summary>
        public void SetBeamRange(float metres) { beamRange = metres; if (beams != null) foreach (var b in beams) if (b != null) b.range = metres * 1.8f; }

        private void Awake()
        {
            input = GetComponent<VehicleInputHandler>(); car = GetComponent<VehicleController>();
            beams = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "BeamL" : "BeamR");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(i == 0 ? -0.6f : 0.6f, 0.75f, 2.1f);
                go.transform.localRotation = Quaternion.Euler(3f, i == 0 ? -2f : 2f, 0f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = 72f; l.innerSpotAngle = 30f; l.range = beamRange * 1.8f; l.intensity = 320f;
                l.color = new Color(1f, 0.94f, 0.8f); l.shadows = LightShadows.None; l.enabled = false;
                beams[i] = l;
            }
            Rescan();
        }

        /// <summary>Finds the lamp renderers again (call after the body model was swapped for another car) and rebuilds the halos and beam cones.</summary>
        public void Rescan()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            var t = new System.Collections.Generic.List<Renderer>(); var h = new System.Collections.Generic.List<Renderer>(); var r = new System.Collections.Generic.List<Renderer>();
            foreach (var rend in all)
            {
                if (rend.GetComponent<GlowBillboard>() != null || rend.name == "BeamCone") continue;
                if (rend.name.StartsWith("Taillight")) t.Add(rend);
                else if (rend.name.StartsWith("Headlight") && !rend.name.Contains("Surround")) h.Add(rend);
                else if (rend.name == "ReverseLamp") r.Add(rend);
            }
            tail = t.ToArray(); head = h.ToArray(); reverse = r.ToArray();
            if (block == null) block = new MaterialPropertyBlock();
            lastTail = lastHead = lastRev = -1;          // force the lamps to be repainted on the next frame
            BuildGlows();
        }

        private Vector3 LocalCentre(Renderer r) => transform.InverseTransformPoint(r.bounds.center);

        private void BuildGlows()
        {
            foreach (var g in built) if (g != null) Destroy(g);
            built.Clear(); headGlows.Clear(); tailGlows.Clear();
            var heads = new System.Collections.Generic.List<Vector3>(); var tails = new System.Collections.Generic.List<Vector3>();
            foreach (var r in head) heads.Add(LocalCentre(r));
            foreach (var r in tail) tails.Add(LocalCentre(r));
            if (heads.Count == 0) { heads.Add(new Vector3(-0.6f, 0.72f, 2.1f)); heads.Add(new Vector3(0.6f, 0.72f, 2.1f)); }
            if (tails.Count == 0) { tails.Add(new Vector3(-0.65f, 0.85f, -2.1f)); tails.Add(new Vector3(0.65f, 0.85f, -2.1f)); }
            foreach (var p in heads) { var g = GlowKit.AddGlow(transform, p, 1.2f, new Color(1f, 0.96f, 0.82f, 0.85f)); g.intensity = 0f; headGlows.Add(g); built.Add(g.gameObject); }
            foreach (var p in tails) { var g = GlowKit.AddGlow(transform, p, 0.85f, new Color(1f, 0.08f, 0.05f, 0.9f)); g.intensity = 0f; tailGlows.Add(g); built.Add(g.gameObject); }
            // a warm pool of light on the road ahead (the dark asphalt barely shows the real spot lights, so this reads clearly from the chase camera)
            var pg = new GameObject("LightPool"); pg.transform.SetParent(transform, false); pg.transform.localPosition = new Vector3(0f, 0.12f, 8.5f); pg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); pg.transform.localScale = new Vector3(8.5f, 15f, 1f);
            pg.AddComponent<MeshFilter>().sharedMesh = GlowKit.Quad;
            var pr = pg.AddComponent<MeshRenderer>(); pr.sharedMaterial = GlowKit.SoftMaterial; pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pr.receiveShadows = false; pr.enabled = false;
            cones = new[] { pr }; built.Add(pg);
            coneAlpha = -1f;
        }

        private void Update()
        {
            VehicleInput i = input != null ? input.Current : default;
            bool braking = i.Brake > 0.1f || i.Handbrake || (i.Reverse > 0.1f && car != null && car.ForwardSpeed > 0.5f);
            bool reversing = i.Reverse > 0.1f && car != null && car.ForwardSpeed < 0.3f;
            float night = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.Night01 : 0f;
            bool lit = HeadlightsOn;

            ApplyTail(braking ? 1 : lit ? 2 : 0);
            Apply(head, lit, ref lastHead, headOn, headOff);
            if (beams != null) foreach (var b in beams) if (b != null && b.enabled != lit) b.enabled = lit;
            Apply(reverse, reversing, ref lastRev, reverseOn, Color.black);

            foreach (var g in headGlows) if (g != null) g.intensity = lit ? Mathf.Lerp(0.55f, 1f, night) : 0f;
            float tailI = braking ? 1.15f : lit ? 0.75f : 0f;
            foreach (var g in tailGlows) if (g != null) g.intensity = tailI;
            if (cones != null)
            {
                float a = lit ? Mathf.Lerp(0.45f, 1f, night) : 0f;
                if (!Mathf.Approximately(a, coneAlpha))
                {
                    coneAlpha = a;
                    foreach (var c in cones) if (c != null) { c.enabled = a > 0.01f; var pb = new MaterialPropertyBlock(); pb.SetColor("_BaseColor", new Color(1f, 0.93f, 0.74f, 0.34f * a)); c.SetPropertyBlock(pb); }
                }
            }
        }

        private void ApplyTail(int state)
        {
            if (state == lastTail) return;
            lastTail = state;
            block.SetColor(Emission, state == 1 ? tailBrake : state == 2 ? tailNight : tailDay);
            foreach (var r in tail) if (r != null) r.SetPropertyBlock(block);
        }

        private void Apply(Renderer[] list, bool on, ref int last, Color onColour, Color offColour)
        {
            int state = on ? 1 : 0;
            if (state == last) return;
            last = state;
            block.SetColor(Emission, on ? onColour : offColour);
            foreach (var r in list) if (r != null) r.SetPropertyBlock(block);
        }
    }
}
