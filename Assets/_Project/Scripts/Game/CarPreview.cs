using CityDriver.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.Game
{
    /// <summary>
    /// Live 3D showroom used by the garage and vehicle screens: a hidden stage far below the city with a rotating copy of the selected car
    /// (body model + wheels, paint colour, upgrade body kit, lowered stance) rendered into a RenderTexture that the UI shows in a RawImage.
    /// The camera only renders while a <see cref="PreviewImage"/> is on screen.
    /// </summary>
    public sealed class CarPreview : MonoBehaviour
    {
        public static CarPreview Instance { get; private set; }

        private const float StageY = -3000f;
        private RenderTexture rt;
        private Camera cam;
        private Transform stage, carRoot;
        private float yaw = 215f, touchedAt = -10f, camHeight = 1.4f, camDist = 5.7f;
        public bool AutoRotate { get; set; } = true;
        public RenderTexture Texture => rt;
        public string ShownId { get; private set; } = "";

        public static CarPreview Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CarPreview");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CarPreview>();
            Instance.Build();
            return Instance;
        }

        private void Build()
        {
            rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "CarPreviewRT" };
            rt.Create();
            stage = new GameObject("Stage").transform; stage.SetParent(transform, false); stage.position = new Vector3(0f, StageY, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder); floor.name = "Floor"; floor.transform.SetParent(stage, false);
            floor.transform.localScale = new Vector3(9f, 0.05f, 9f); floor.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            Destroy(floor.GetComponent<Collider>());
            var fr = floor.GetComponent<Renderer>(); if (fr != null) fr.sharedMaterial = StageMaterial.Make(new Color(0.10f, 0.11f, 0.14f));

            Light MakeLight(string n, LightType t, Vector3 pos, Vector3 euler, float intensity, Color c, float range)
            {
                var l = new GameObject(n).AddComponent<Light>(); l.transform.SetParent(stage, false); l.type = t; l.transform.localPosition = pos; l.transform.localEulerAngles = euler;
                l.intensity = intensity; l.color = c; l.range = range; l.shadows = LightShadows.None; return l;
            }
            MakeLight("Key", LightType.Directional, Vector3.zero, new Vector3(42f, -40f, 0f), 2.4f, new Color(1f, 0.96f, 0.9f), 0f);
            MakeLight("Rim", LightType.Directional, Vector3.zero, new Vector3(25f, 150f, 0f), 1.4f, new Color(0.55f, 0.7f, 1f), 0f);
            MakeLight("Top", LightType.Point, new Vector3(0f, 5f, 0f), Vector3.zero, 40f, new Color(1f, 1f, 1f), 14f);

            cam = new GameObject("PreviewCam").AddComponent<Camera>(); cam.transform.SetParent(stage, false);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f, 1f); cam.fieldOfView = 32f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 60f;
            cam.targetTexture = rt; cam.enabled = false; cam.allowHDR = false;
            var ad = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (ad == null) ad = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            ad.renderPostProcessing = false; ad.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
        }

        /// <summary>Shows the car described by <paramref name="spec"/> with the given paint, body-kit stage and suspension level.</summary>
        public void Show(VehicleSpec spec, string paintHex, int kitStage, int suspensionLevel)
        {
            if (spec == null) return;
            Touch();
            if (carRoot != null) Destroy(carRoot.gameObject);
            carRoot = new GameObject("PreviewCar").transform; carRoot.SetParent(stage, false); carRoot.localPosition = Vector3.zero;
            ShownId = spec.id;

            var prefab = Resources.Load<GameObject>("CarBodies/Body_" + spec.id);
            if (prefab != null)
            {
                var body = Instantiate(prefab, carRoot); body.name = "Body"; body.transform.localPosition = Vector3.zero; body.transform.localRotation = Quaternion.identity;
                VehicleTuning.ApplyKit(body.transform, kitStage, suspensionLevel);
                foreach (var l in body.GetComponentsInChildren<Light>(true)) l.enabled = false;      // the neon underglow only belongs on the street
            }

            // wheels: copy the live car's wheel visuals (tyre, rim, brake disc) and put them in their rest pose
            var live = FindFirstObjectByType<VehicleController>();
            Transform wheels = live != null ? live.transform.Find("Wheels") : null;
            if (wheels != null)
            {
                var w = Instantiate(wheels.gameObject, carRoot); w.name = "Wheels"; w.transform.localPosition = Vector3.zero; w.transform.localRotation = Quaternion.identity;
                foreach (Transform p in w.transform) { var lp = p.localPosition; p.localPosition = new Vector3(lp.x, 0.33f, lp.z); p.localRotation = Quaternion.identity; }
            }

            if (ColorUtility.TryParseHtmlString(string.IsNullOrEmpty(paintHex) ? spec.defaultPaint : paintHex, out Color c))
            {
                var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", c);
                foreach (var r in carRoot.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name == "M_CarPaint") r.SetPropertyBlock(block, i);
                }
            }
        }

        /// <summary>Called every frame by a visible <see cref="PreviewImage"/>.</summary>
        public void Touch() { touchedAt = Time.unscaledTime; }
        public void Rotate(float degrees) { yaw += degrees; }

        private void LateUpdate()
        {
            bool visible = Time.unscaledTime - touchedAt < 0.25f;
            if (cam.enabled != visible) cam.enabled = visible;
            if (!visible) return;
            if (AutoRotate) yaw += 22f * Time.unscaledDeltaTime;
            float a = yaw * Mathf.Deg2Rad;
            cam.transform.localPosition = new Vector3(Mathf.Sin(a) * camDist, camHeight, Mathf.Cos(a) * camDist);
            cam.transform.LookAt(stage.position + new Vector3(0f, 0.62f, 0f));
        }

        private void OnDestroy() { if (rt != null) rt.Release(); }
    }
}
