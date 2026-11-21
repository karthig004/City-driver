using CityDriver.CameraSystem;
using CityDriver.Population;
using CityDriver.Vehicle;
using UnityEngine;

namespace CityDriver.Game
{
    public enum TimeOfDay { Morning = 0, Day = 1, Sunset = 2, Night = 3 }
    public enum Weather { Clear = 0, Rain = 1, HeavyRain = 2, Fog = 3 }

    /// <summary>
    /// Day / night and weather. Presets for morning, day, sunset and night drive the sun, ambient light, sky and fog; weather adds
    /// rain particles, heavy rain with thunder, or dense fog, lowers tyre grip and visibility, and switches the car's headlights on
    /// when it is dark or foggy. Changes blend over a few seconds. The scene's skybox material is cloned, never modified on disk.
    /// </summary>
    public sealed class WorldEnvironment : MonoBehaviour
    {
        private struct Look
        {
            public float sunPitch, sunYaw, sunIntensity, skyExposure, fogStart, fogEnd, atmosphere;
            public Color sunColor, ambientSky, ambientEquator, ambientGround, fogColor, skyTint;
        }

        private static Look Preset(TimeOfDay t)
        {
            switch (t)
            {
                case TimeOfDay.Morning: return new Look { sunPitch = 14f, sunYaw = 100f, sunIntensity = 1.1f, skyExposure = 1.1f, fogStart = 120f, fogEnd = 480f, atmosphere = 1.2f, sunColor = new Color(1f, 0.78f, 0.55f), ambientSky = new Color(0.45f, 0.5f, 0.6f), ambientEquator = new Color(0.36f, 0.36f, 0.38f), ambientGround = new Color(0.2f, 0.19f, 0.18f), fogColor = new Color(0.86f, 0.76f, 0.68f), skyTint = new Color(0.6f, 0.5f, 0.45f) };
                case TimeOfDay.Sunset: return new Look { sunPitch = 5f, sunYaw = 265f, sunIntensity = 1.0f, skyExposure = 0.95f, fogStart = 100f, fogEnd = 430f, atmosphere = 1.5f, sunColor = new Color(1f, 0.5f, 0.25f), ambientSky = new Color(0.5f, 0.38f, 0.38f), ambientEquator = new Color(0.34f, 0.26f, 0.24f), ambientGround = new Color(0.16f, 0.12f, 0.1f), fogColor = new Color(0.95f, 0.62f, 0.45f), skyTint = new Color(0.75f, 0.45f, 0.35f) };
                case TimeOfDay.Night: return new Look { sunPitch = 38f, sunYaw = 40f, sunIntensity = 0.26f, skyExposure = 0.3f, fogStart = 90f, fogEnd = 340f, atmosphere = 0.6f, sunColor = new Color(0.5f, 0.6f, 0.95f), ambientSky = new Color(0.13f, 0.16f, 0.27f), ambientEquator = new Color(0.10f, 0.12f, 0.19f), ambientGround = new Color(0.06f, 0.06f, 0.1f), fogColor = new Color(0.07f, 0.09f, 0.16f), skyTint = new Color(0.14f, 0.17f, 0.34f) };
                default: return new Look { sunPitch = 58f, sunYaw = 130f, sunIntensity = 1.25f, skyExposure = 1.25f, fogStart = 200f, fogEnd = 520f, atmosphere = 1f, sunColor = new Color(1f, 0.96f, 0.88f), ambientSky = new Color(0.55f, 0.6f, 0.68f), ambientEquator = new Color(0.42f, 0.43f, 0.45f), ambientGround = new Color(0.24f, 0.23f, 0.22f), fogColor = new Color(0.78f, 0.84f, 0.9f), skyTint = new Color(0.5f, 0.5f, 0.5f) };
            }
        }

        public static WorldEnvironment Instance { get; private set; }

        public TimeOfDay Time01Preset { get; private set; } = TimeOfDay.Day;
        public Weather CurrentWeather { get; private set; } = Weather.Clear;
        /// <summary>0 = full daylight, 1 = full night (for HUD / tests).</summary>
        public float Night01 { get; private set; }
        public float RainIntensity { get; private set; }
        public float FogStart => RenderSettings.fogStartDistance;
        public float FogEnd => RenderSettings.fogEndDistance;
        public float GripFactor { get; private set; } = 1f;
        public bool ShadowMode { get; private set; }
        public bool RainParticlesPlaying => rainSystem != null && rainSystem.isPlaying && rainSystem.emission.rateOverTime.constant > 1f;
        public int RainParticleCount => rainSystem != null ? rainSystem.particleCount : 0;
        public float SunIntensity => sun != null ? sun.intensity : 0f;
        public bool AutoCycle { get; set; }

        private Light sun;
        private Material skyClone;
        private Look from, to, shown;
        private float blend = 1f, blendSeconds = 1f;
        private float targetRain, targetGrip = 1f, weatherFogFactor = 1f;
        private ParticleSystem rainSystem;
        private float nextThunder, autoClock;
        private bool headlightsWanted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != CityPopulation.SceneName) return;
            if (FindFirstObjectByType<WorldEnvironment>() != null) return;
            new GameObject("WorldEnvironment").AddComponent<WorldEnvironment>();
        }

        // Dynamic sky: the player never picks the time or weather. The game starts at the device's time of day; each normal job that starts moves
        // the clock on one part of the day and rolls the weather; when the job ends the weather mostly clears. Shadow Runs force night and rain.
        private TimeOfDay directorTime = ClockStage();
        private Weather directorWeather = Weather.Clear;

        private static TimeOfDay ClockStage()
        {
            int h = System.DateTime.Now.Hour;
            return h >= 6 && h < 10 ? TimeOfDay.Morning : h >= 10 && h < 17 ? TimeOfDay.Day : h >= 17 && h < 19 ? TimeOfDay.Sunset : TimeOfDay.Night;
        }

        private static Weather RollWeather(TimeOfDay t)
        {
            float r = Random.value;
            switch (t)
            {
                case TimeOfDay.Morning: return r < 0.6f ? Weather.Clear : r < 0.8f ? Weather.Fog : Weather.Rain;
                case TimeOfDay.Day: return r < 0.7f ? Weather.Clear : r < 0.95f ? Weather.Rain : Weather.HeavyRain;
                case TimeOfDay.Sunset: return r < 0.55f ? Weather.Clear : r < 0.9f ? Weather.Rain : Weather.Fog;
                default: return r < 0.5f ? Weather.Clear : r < 0.85f ? Weather.Rain : Weather.HeavyRain;
            }
        }

        /// <summary>A normal job starts: time moves on and the weather rolls (the HUD announces the change).</summary>
        public void OnMissionStarted(bool nightOnly = false)
        {
            if (ShadowMode) return;
            directorTime = nightOnly ? TimeOfDay.Night : (TimeOfDay)(((int)directorTime + 1) % 4);       // "night" jobs always run at night
            directorWeather = RollWeather(directorTime);
            SetTimeOfDay(directorTime, 8f); SetWeather(directorWeather, 8f);
        }

        /// <summary>The job is over: bad weather usually blows over.</summary>
        public void OnMissionEnded()
        {
            if (ShadowMode) return;
            if (directorWeather != Weather.Clear && Random.value < 0.8f) { directorWeather = Weather.Clear; SetWeather(Weather.Clear, 12f); }
        }

        private void Awake()
        {
            Instance = this;
            sun = RenderSettings.sun;
            if (sun == null) foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.type == LightType.Directional) { sun = l; break; }
            if (RenderSettings.skybox != null) { skyClone = new Material(RenderSettings.skybox); RenderSettings.skybox = skyClone; }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            shown = Preset(TimeOfDay.Day); from = to = shown;
            Apply(shown);
            GameSettings.Changed += OnSettings;
        }

        private void OnDestroy()
        {
            GameSettings.Changed -= OnSettings;
            if (Instance == this) Instance = null;
            VehicleTuning.WeatherGrip = 1f;
            if (skyClone != null) Destroy(skyClone);
        }

        private void Start() { OnSettings(); }

        /// <summary>Re-applies the player's time/weather settings (used after the intro cinematic forced a sunset sky).</summary>
        public void RestoreFromSettings() { OnSettings(); }

        private void OnSettings()
        {
            if (ShadowMode) return;                                    // Shadow Run owns the sky while it lasts
            AutoCycle = false;
            SetTimeOfDay(directorTime, 2.5f);
            SetWeather(directorWeather, 3f);
        }

        // ---------------------------------------------------------------------------------------------- public API
        public void SetTimeOfDay(TimeOfDay t, float seconds)
        {
            if (Time01Preset == t && blend >= 1f) return;
            Time01Preset = t;
            from = shown; to = Preset(t); blend = 0f; blendSeconds = Mathf.Max(0.01f, seconds);
        }

        public void SetWeather(Weather w, float seconds)
        {
            CurrentWeather = w;
            switch (w)
            {
                case Weather.Rain: targetRain = 0.55f; targetGrip = 0.82f; weatherFogFactor = 0.55f; break;
                case Weather.HeavyRain: targetRain = 1f; targetGrip = 0.7f; weatherFogFactor = 0.3f; break;
                case Weather.Fog: targetRain = 0f; targetGrip = 0.95f; weatherFogFactor = 0.18f; break;
                default: targetRain = 0f; targetGrip = 1f; weatherFogFactor = 1f; break;
            }
            nextThunder = UnityEngine.Time.unscaledTime + 4f;
        }

        /// <summary>Shadow Run look: night, rain, suspense music, headlights. Ends with <see cref="EndShadowMode"/>.</summary>
        public void BeginShadowMode(float seconds)
        {
            ShadowMode = true;
            SetTimeOfDay(TimeOfDay.Night, seconds); SetWeather(Weather.Rain, seconds);
            if (GameAudio.Instance != null) GameAudio.Instance.ShadowMusic = true;
        }

        public void EndShadowMode()
        {
            ShadowMode = false;
            directorTime = TimeOfDay.Morning; directorWeather = Weather.Clear;      // dawn after the night's work
            if (GameAudio.Instance != null) GameAudio.Instance.ShadowMusic = false;
            OnSettings();
        }

        // ---------------------------------------------------------------------------------------------- update
        private void Update()
        {
            float dt = UnityEngine.Time.unscaledDeltaTime;
            if (AutoCycle && !ShadowMode)
            {
                autoClock += dt;                                       // a full day in 12 real minutes
                int phase = Mathf.FloorToInt(autoClock / 180f) % 4;
                if ((TimeOfDay)phase != Time01Preset) SetTimeOfDay((TimeOfDay)phase, 20f);
            }

            if (blend < 1f) { blend = Mathf.Min(1f, blend + dt / blendSeconds); shown = Lerp(from, to, Mathf.SmoothStep(0f, 1f, blend)); }
            RainIntensity = Mathf.MoveTowards(RainIntensity, targetRain, dt / 3f);
            GripFactor = Mathf.MoveTowards(GripFactor, targetGrip, dt / 3f);
            Apply(shown);

            Night01 = Mathf.InverseLerp(1.25f, 0.26f, shown.sunIntensity);
            if (Mathf.Abs(VehicleTuning.WeatherGrip - GripFactor) > 0.02f) { VehicleTuning.WeatherGrip = GripFactor; VehicleTuning.ApplyToCurrentCar(); }

            UpdateRain();
            UpdateHeadlights();
            if (GameAudio.Instance != null) GameAudio.Instance.RainIntensity = RainIntensity;
            if (CurrentWeather == Weather.HeavyRain && UnityEngine.Time.unscaledTime > nextThunder) { GameAudio.PlayThunder(); nextThunder = UnityEngine.Time.unscaledTime + Random.Range(7f, 16f); }
        }

        private static Look Lerp(Look a, Look b, float t) => new Look
        {
            sunPitch = Mathf.Lerp(a.sunPitch, b.sunPitch, t), sunYaw = Mathf.LerpAngle(a.sunYaw, b.sunYaw, t), sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t),
            skyExposure = Mathf.Lerp(a.skyExposure, b.skyExposure, t), fogStart = Mathf.Lerp(a.fogStart, b.fogStart, t), fogEnd = Mathf.Lerp(a.fogEnd, b.fogEnd, t), atmosphere = Mathf.Lerp(a.atmosphere, b.atmosphere, t),
            sunColor = Color.Lerp(a.sunColor, b.sunColor, t), ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t), ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t),
            ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t), fogColor = Color.Lerp(a.fogColor, b.fogColor, t), skyTint = Color.Lerp(a.skyTint, b.skyTint, t),
        };

        private void Apply(Look l)
        {
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(l.sunPitch, l.sunYaw, 0f);
                sun.color = l.sunColor; sun.intensity = l.sunIntensity * Mathf.Lerp(1f, 0.55f, RainIntensity);
            }
            RenderSettings.ambientSkyColor = l.ambientSky; RenderSettings.ambientEquatorColor = l.ambientEquator; RenderSettings.ambientGroundColor = l.ambientGround;
            Color grey = Color.Lerp(l.fogColor, Color.Lerp(l.fogColor, new Color(0.4f, 0.43f, 0.47f) * Mathf.Lerp(1f, 0.25f, Night01), 0.6f), Mathf.Max(RainIntensity, 1f - weatherFogFactor));
            RenderSettings.fogColor = grey;
            float fogScale = weatherFogFactor;
            RenderSettings.fogStartDistance = l.fogStart * fogScale * 0.5f;
            RenderSettings.fogEndDistance = Mathf.Max(RenderSettings.fogStartDistance + 20f, l.fogEnd * fogScale);
            if (skyClone != null)
            {
                if (skyClone.HasProperty("_Exposure")) skyClone.SetFloat("_Exposure", l.skyExposure * Mathf.Lerp(1f, 0.55f, Mathf.Max(RainIntensity, 1f - weatherFogFactor)));
                if (skyClone.HasProperty("_SkyTint")) skyClone.SetColor("_SkyTint", l.skyTint);
                if (skyClone.HasProperty("_AtmosphereThickness")) skyClone.SetFloat("_AtmosphereThickness", l.atmosphere);
            }
        }

        private void UpdateRain()
        {
            if (rainSystem == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                var go = new GameObject("Rain");
                go.transform.SetParent(cam.transform, false);
                go.transform.localPosition = new Vector3(0f, 9f, 8f);
                rainSystem = go.AddComponent<ParticleSystem>();
                var main = rainSystem.main; main.loop = true; main.startLifetime = 0.85f; main.startSpeed = 0f; main.startSize = 0.03f;
                main.startColor = new Color(0.8f, 0.86f, 1f, 0.55f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 4000; main.gravityModifier = 3.5f;
                var shape = rainSystem.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(34f, 1f, 34f);
                var vel = rainSystem.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.y = new ParticleSystem.MinMaxCurve(-24f); vel.x = new ParticleSystem.MinMaxCurve(-2f);
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 6f; r.velocityScale = 0.04f; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                Material m = Resources.Load<Material>("RainParticle");
                if (m == null) { Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit"); if (sh == null) sh = Shader.Find("Sprites/Default"); if (sh != null) m = new Material(sh); }
                if (m != null) r.sharedMaterial = m;
                var em = rainSystem.emission; em.rateOverTime = 0f;
                rainSystem.Play();
            }
            var emission = rainSystem.emission;
            emission.rateOverTime = RainIntensity * 2200f;
        }

        private void UpdateHeadlights()
        {
            var lights = FindCarLights();
            if (lights == null) return;
            bool want = Night01 > 0.45f || weatherFogFactor < 0.35f || (RainIntensity > 0.8f);
            if (want != headlightsWanted || lights.HeadlightsOn != want) { headlightsWanted = want; lights.HeadlightsOn = want; }
        }

        private VehicleLights cachedLights;
        private VehicleLights FindCarLights() { if (cachedLights == null) cachedLights = FindFirstObjectByType<VehicleLights>(); return cachedLights; }
    }
}
