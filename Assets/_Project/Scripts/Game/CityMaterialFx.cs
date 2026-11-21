using CityDriver.Population;
using UnityEngine;

namespace CityDriver.Game
{
    /// <summary>
    /// Time-of-day and weather on the city's own materials (the city is baked, so this drives the shared material assets): building windows light
    /// up at night (the building materials carry a window-only emission map), lane markings pick up a little glow, and the asphalt gets
    /// glossy and darker when it rains so headlights and the sun reflect in it. Original values are restored when the scene ends.
    /// </summary>
    public sealed class CityMaterialFx : MonoBehaviour
    {
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor"), Smooth = Shader.PropertyToID("_Smoothness"), BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color[] WindowTint = { new Color(1.0f, 0.80f, 0.50f), new Color(0.80f, 0.88f, 1.0f), new Color(1.0f, 0.72f, 0.45f), new Color(0.90f, 1.0f, 0.78f) };

        private Material[] buildings = new Material[4];
        private Material asphalt, paintWhite, paintYellow;
        private float asphaltSmooth0 = 0.12f; private Color asphaltBase0 = Color.white;
        private float nextFind, shownNight = -1f, shownWet = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != CityPopulation.SceneName) return;
            if (FindFirstObjectByType<CityMaterialFx>() != null) return;
            new GameObject("CityMaterialFx").AddComponent<CityMaterialFx>();
        }

        private void Find()
        {
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                switch (m.name)
                {
                    case "M_Building1": buildings[0] = m; break;
                    case "M_Building2": buildings[1] = m; break;
                    case "M_Building3": buildings[2] = m; break;
                    case "M_Building4": buildings[3] = m; break;
                    case "M_PaintWhite": paintWhite = m; break;
                    case "M_PaintYellow": paintYellow = m; break;
                    case "M_Asphalt": if (asphalt == null) { asphalt = m; if (m.HasProperty(Smooth)) asphaltSmooth0 = m.GetFloat(Smooth); if (m.HasProperty(BaseColor)) asphaltBase0 = m.GetColor(BaseColor); } break;
                }
            }
            foreach (var b in buildings) if (b != null && b.HasProperty("_EmissionMap")) b.EnableKeyword("_EMISSION");
            foreach (var p in new[] { paintWhite, paintYellow }) if (p != null) p.EnableKeyword("_EMISSION");
            shownNight = shownWet = -1f;
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextFind) { nextFind = Time.unscaledTime + (asphalt == null || buildings[0] == null ? 2f : 6f); Find(); }
            var env = WorldEnvironment.Instance; if (env == null) return;
            float night = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.85f, env.Night01)), wet = Mathf.Clamp01(env.RainIntensity);
            if (!Mathf.Approximately(night, shownNight))
            {
                shownNight = night;
                for (int i = 0; i < buildings.Length; i++) if (buildings[i] != null) buildings[i].SetColor(Emission, WindowTint[i] * (1.5f * night));
                if (paintWhite != null) paintWhite.SetColor(Emission, new Color(0.5f, 0.5f, 0.46f) * (0.35f * night));
                if (paintYellow != null) paintYellow.SetColor(Emission, new Color(0.6f, 0.45f, 0.05f) * (0.35f * night));
            }
            if (!Mathf.Approximately(wet, shownWet) && asphalt != null)
            {
                shownWet = wet;
                if (asphalt.HasProperty(Smooth)) asphalt.SetFloat(Smooth, Mathf.Lerp(asphaltSmooth0, 0.82f, wet));
                if (asphalt.HasProperty(BaseColor)) asphalt.SetColor(BaseColor, Color.Lerp(asphaltBase0, asphaltBase0 * new Color(0.55f, 0.58f, 0.66f, 1f), wet));
            }
        }

        private void OnDestroy()
        {
            foreach (var b in buildings) if (b != null) b.SetColor(Emission, Color.black);
            if (paintWhite != null) paintWhite.SetColor(Emission, Color.black);
            if (paintYellow != null) paintYellow.SetColor(Emission, Color.black);
            if (asphalt != null) { if (asphalt.HasProperty(Smooth)) asphalt.SetFloat(Smooth, asphaltSmooth0); if (asphalt.HasProperty(BaseColor)) asphalt.SetColor(BaseColor, asphaltBase0); }
        }
    }
}
