using CityDriver.Characters;
using UnityEngine;

namespace CityDriver.Game
{
    /// <summary>Hidden 3D stage that renders the customisable driver into a RenderTexture for the character screen (turntable, drag to turn).</summary>
    public sealed class CharacterPreview : MonoBehaviour
    {
        public static CharacterPreview Instance { get; private set; }
        private const float StageY = -3500f;
        private RenderTexture rt;
        private Camera cam;
        private Transform stage, person;
        private CharacterCustomizer customizer;
        private CharacterAnimator animator;
        private string shownPrefab = "";
        private float yaw = 160f, touchedAt = -10f;
        public bool AutoRotate { get; set; } = true;
        public RenderTexture Texture => rt;

        public static CharacterPreview Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CharacterPreview"); DontDestroyOnLoad(go);
            Instance = go.AddComponent<CharacterPreview>(); Instance.Build();
            return Instance;
        }

        private void Build()
        {
            rt = new RenderTexture(880, 1120, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "CharacterPreviewRT" }; rt.Create();
            stage = new GameObject("Stage").transform; stage.SetParent(transform, false); stage.position = new Vector3(0f, StageY, 0f);
            Light MakeLight(string n, LightType t, Vector3 pos, Vector3 euler, float intensity, Color c, float range)
            {
                var l = new GameObject(n).AddComponent<Light>(); l.transform.SetParent(stage, false); l.type = t; l.transform.localPosition = pos; l.transform.localEulerAngles = euler;
                l.intensity = intensity; l.color = c; l.range = range; l.shadows = LightShadows.None; return l;
            }
            MakeLight("Key", LightType.Directional, Vector3.zero, new Vector3(30f, -35f, 0f), 2.3f, new Color(1f, 0.95f, 0.88f), 0f);
            MakeLight("Rim", LightType.Directional, Vector3.zero, new Vector3(20f, 150f, 0f), 1.5f, new Color(0.55f, 0.7f, 1f), 0f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder); floor.transform.SetParent(stage, false); floor.transform.localScale = new Vector3(2.2f, 0.02f, 2.2f); floor.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            Destroy(floor.GetComponent<Collider>()); var fr = floor.GetComponent<Renderer>(); if (fr != null) fr.sharedMaterial = StageMaterial.Make(new Color(0.12f, 0.13f, 0.17f));
            cam = new GameObject("Cam").AddComponent<Camera>(); cam.transform.SetParent(stage, false);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.07f, 0.08f, 0.12f, 1f); cam.fieldOfView = 24f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 40f;
            cam.targetTexture = rt; cam.enabled = false;
            var ad = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); ad.renderPostProcessing = false; ad.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
        }

        /// <summary>Shows a character (prefab id under Resources/Characters) wearing <paramref name="look"/>. Re-applies the look without rebuilding when only the look changed.</summary>
        public void Show(string prefabId, CharacterLook look, CharPose pose = CharPose.Idle)
        {
            Touch();
            if (person == null || shownPrefab != prefabId)
            {
                if (person != null) Destroy(person.gameObject);
                var prefab = Resources.Load<GameObject>("Characters/Char_" + prefabId);
                if (prefab == null) { Debug.LogWarning("[CharacterPreview] Missing prefab " + prefabId); return; }
                person = Instantiate(prefab, stage).transform; person.localPosition = Vector3.zero; person.localRotation = Quaternion.identity;
                customizer = person.GetComponent<CharacterCustomizer>(); animator = person.GetComponent<CharacterAnimator>(); shownPrefab = prefabId;
            }
            if (customizer != null) customizer.Apply(look);
            if (animator != null) animator.pose = pose;
        }

        public void SetPose(CharPose p) { if (animator != null) animator.pose = p; }
        public void Touch() { touchedAt = Time.unscaledTime; }
        public void Rotate(float degrees) { yaw += degrees; }

        private void Update()
        {
            // the animator uses Time.deltaTime, which is 0 while a menu pauses the game: drive it by hand with unscaled time
            if (animator != null && Time.timeScale == 0f && Time.unscaledTime - touchedAt < 0.25f) animator.Tick(Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            bool visible = Time.unscaledTime - touchedAt < 0.25f;
            if (cam.enabled != visible) cam.enabled = visible;
            if (!visible) return;
            if (AutoRotate) yaw += 18f * Time.unscaledDeltaTime;
            float a = yaw * Mathf.Deg2Rad, d = 4.6f;
            cam.transform.localPosition = new Vector3(Mathf.Sin(a) * d, 1.2f, Mathf.Cos(a) * d);
            cam.transform.LookAt(stage.position + new Vector3(0f, 0.95f, 0f));
        }

        private void OnDestroy() { if (rt != null) rt.Release(); }
    }
}
