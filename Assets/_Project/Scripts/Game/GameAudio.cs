using System.Collections.Concurrent;
using System.Threading.Tasks;
using CityDriver.Controls;
using CityDriver.Population;
using CityDriver.Vehicle;
using UnityEngine;

namespace CityDriver.Game
{
    /// <summary>
    /// Vehicle and world audio plus the car radio. Engine pitch follows rpm (speed within the current gear), tyres screech under
    /// side slip or hard braking, wind and a city murmur scale with speed, collisions play a thump, and the radio plays one of three
    /// synthesised stations. Volumes come from <see cref="GameSettings"/> (master x music / sfx). Created when the CityDriving scene loads.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private static readonly float[] GearTopKmh = { 0f, 28f, 52f, 82f, 118f, 160f };

        private AudioSource engine, tyre, wind, ambience, radio, oneShot, rain;
        private AudioClip crash, click, horn, thunder, siren, suspense, cardChime;
        private readonly AudioClip[] stations = new AudioClip[4];
        private VehicleController car;
        private VehicleInputHandler input;
        private Vector3 lastVelocity;
        private float crashCooldown;
        private int shownStation = -1;
        private readonly ConcurrentQueue<ProceduralAudio.AudioData> ready = new ConcurrentQueue<ProceduralAudio.AudioData>();
        private int expected, created;

        /// <summary>True once every synthesised clip (engine, tyres, wind, ambience, one-shots, three stations) has been built.</summary>
        public bool Ready => created >= expected && expected > 0;
        public bool EngineReady => engine != null && engine.clip != null;

        public static GameAudio Instance { get; private set; }
        public AudioSource EngineSource => engine;
        public AudioSource TyreSource => tyre;
        public AudioSource WindSource => wind;
        public AudioSource RadioSource => radio;
        public AudioClip CurrentStationClip => radio != null ? radio.clip : null;
        public AudioSource RainSource => rain;
        public AudioClip SirenClip => siren;

        /// <summary>Shadow Run mode: the radio is replaced by the suspense bed.</summary>
        public bool ShadowMusic { get => shadowMusic; set { if (shadowMusic == value) return; shadowMusic = value; shownStation = -1; ApplyStation(); } }
        private bool shadowMusic;
        public float RainIntensity { get; set; }
        public float EngineRpm01 { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != CityPopulation.SceneName) return;
            if (FindFirstObjectByType<GameAudio>() != null) return;
            new GameObject("GameAudio").AddComponent<GameAudio>();
        }

        private AudioSource Source(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = loop; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f; s.ignoreListenerPause = false;
            return s;
        }

        private void Awake()
        {
            Instance = this;
            if (FindFirstObjectByType<AudioListener>() == null && Camera.main != null) Camera.main.gameObject.AddComponent<AudioListener>();

            engine = Source("Engine", null, true);
            tyre = Source("Tyres", null, true);
            wind = Source("Wind", null, true);
            ambience = Source("CityAmbience", null, true);
            radio = Source("Radio", null, true);
            rain = Source("Rain", null, true);
            oneShot = Source("OneShot", null, false);

            // Synthesis is pure maths, so it runs on a worker thread: the game never waits for it. Engine first, radio stations last.
            var jobs = new System.Func<ProceduralAudio.AudioData>[]
            {
                ProceduralAudio.EngineLoop, ProceduralAudio.TyreScreech, ProceduralAudio.Wind, ProceduralAudio.CityAmbience,
                ProceduralAudio.Crash, ProceduralAudio.Click, ProceduralAudio.Horn, ProceduralAudio.Rain, ProceduralAudio.Thunder, ProceduralAudio.Siren, ProceduralAudio.Suspense, ProceduralAudio.CardChime,
                () => ProceduralAudio.Station(1), () => ProceduralAudio.Station(2), () => ProceduralAudio.Station(3),
            };
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browsers have no worker threads: synthesise a lighter set on the main thread, one clip per frame (no ambience bed, thunder, suspense or radio).
            var webJobs = new System.Func<ProceduralAudio.AudioData>[] { ProceduralAudio.EngineLoop, ProceduralAudio.TyreScreech, ProceduralAudio.Wind, ProceduralAudio.Crash, ProceduralAudio.Click, ProceduralAudio.Horn, ProceduralAudio.Siren, ProceduralAudio.CardChime };
            expected = webJobs.Length;
            StartCoroutine(WebSynthesis(webJobs));
#else
            expected = jobs.Length;
            Task.Run(() => { foreach (var job in jobs) { try { ready.Enqueue(job()); } catch (System.Exception e) { UnityEngine.Debug.LogError("[GameAudio] synthesis failed: " + e.Message); } } });
#endif
        }

        private System.Collections.IEnumerator WebSynthesis(System.Func<ProceduralAudio.AudioData>[] list)
        {
            foreach (var job in list)
            {
                yield return null;
                try { ready.Enqueue(job()); } catch (System.Exception e) { UnityEngine.Debug.LogError("[GameAudio] synthesis failed: " + e.Message); }
            }
        }

        private void Adopt(ProceduralAudio.AudioData data)
        {
            AudioClip clip = data.ToClip();
            created++;
            switch (data.name)
            {
                case "Engine": engine.clip = clip; engine.Play(); break;
                case "Screech": tyre.clip = clip; tyre.Play(); break;
                case "Wind": wind.clip = clip; wind.Play(); break;
                case "CityAmbience": ambience.clip = clip; ambience.Play(); break;
                case "Crash": crash = clip; break;
                case "Click": click = clip; break;
                case "Horn": horn = clip; break;
                case "Rain": rain.clip = clip; rain.Play(); break;
                case "Thunder": thunder = clip; break;
                case "Siren": siren = clip; break;
                case "Suspense": suspense = clip; if (shadowMusic) { shownStation = -1; ApplyStation(); } break;
                case "CardChime": cardChime = clip; break;
                case "Radio_LoFiCity": stations[1] = clip; break;
                case "Radio_NightDrive": stations[2] = clip; break;
                case "Radio_MetroBeats": stations[3] = clip; break;
            }
            if (data.name.StartsWith("Radio_")) { shownStation = -1; ApplyStation(); }
        }

        private void OnEnable() { GameSettings.Changed += ApplyStation; }
        private void OnDisable() { GameSettings.Changed -= ApplyStation; }
        private void Start() { ApplyStation(); }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public static void PlayClick() { if (Instance != null && Instance.click != null) Instance.oneShot.PlayOneShot(Instance.click, 0.6f * GameSettings.Sfx * GameSettings.Master); }
        public static void PlayThunder() { if (Instance != null && Instance.thunder != null) Instance.oneShot.PlayOneShot(Instance.thunder, 0.55f * GameSettings.Sfx * GameSettings.Master); }
        public static void PlayCard() { if (Instance != null && Instance.cardChime != null) Instance.oneShot.PlayOneShot(Instance.cardChime, 0.8f * GameSettings.Sfx * GameSettings.Master); }
        public static void PlayHorn() { if (Instance != null && Instance.horn != null) Instance.oneShot.PlayOneShot(Instance.horn, 0.7f * GameSettings.Sfx * GameSettings.Master); }

        private void ApplyStation()
        {
            int id = shadowMusic ? 9 : GameSettings.Radio;
            if (id == shownStation) return;
            shownStation = id;
            if (id == 9)
            {
                if (suspense == null) { shownStation = -1; return; }
                radio.clip = suspense; radio.Play(); return;
            }
            if (id == 0) { radio.Stop(); radio.clip = null; return; }
            if (stations[id] == null) { shownStation = -1; return; }                  // still being synthesised: picked up when ready
            radio.clip = stations[id]; radio.Play();
        }

        private void Update()
        {
            if (car == null) { car = FindFirstObjectByType<VehicleController>(); if (car != null) input = car.GetComponent<VehicleInputHandler>(); }
            int taken = 0;
            while (taken++ < 2 && ready.TryDequeue(out var data)) Adopt(data);       // at most two clips per frame, so no hitch

            float master = GameSettings.Master, sfx = GameSettings.Sfx * master, music = GameSettings.Music * master;
            radio.volume = music * 0.9f;
            ambience.volume = 0.1f * sfx;
            rain.volume = Mathf.Lerp(0f, 0.3f, RainIntensity) * sfx;
            if (car == null) { engine.volume = tyre.volume = wind.volume = 0f; return; }

            bool paused = Time.timeScale == 0f;
            float speed = car.SpeedKmh, speed01 = Mathf.Clamp01(speed / 140f);
            VehicleInput i = input != null ? input.Current : default;

            // rpm: position inside the current gear's speed band, with a floor at idle
            int gear = Mathf.Clamp(Mathf.Abs(car.Gear), 1, GearTopKmh.Length - 1);
            float lo = GearTopKmh[gear - 1] * 0.62f, hi = GearTopKmh[gear];
            float rpmBand = Mathf.Clamp01((Mathf.Abs(speed) - lo) / Mathf.Max(1f, hi - lo));
            float rpm = Mathf.Lerp(0.08f, 1f, rpmBand);
            rpm = Mathf.Max(rpm, 0.1f + 0.45f * i.Throttle * (speed < 12f ? 1f : 0.4f));       // revving the engine while pulling away
            EngineRpm01 = Mathf.Lerp(EngineRpm01, rpm, 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime));

            engine.pitch = Mathf.Lerp(0.72f, 2.3f, EngineRpm01);
            engine.volume = paused ? 0f : (0.22f + 0.5f * i.Throttle + 0.25f * EngineRpm01) * sfx;

            float slip = 0f;
            if (!paused && car.GroundedWheels > 0)
            {
                slip = Mathf.Max(WheelSlip(car.FrontLeft), WheelSlip(car.FrontRight), WheelSlip(car.RearLeft), WheelSlip(car.RearRight));
                if (speed > 30f && i.Brake > 0.7f) slip = Mathf.Max(slip, 0.7f);
            }
            tyre.volume = paused ? 0f : Mathf.Clamp01((slip - 0.45f) * 2.2f) * 0.8f * sfx;
            tyre.pitch = 0.85f + 0.35f * speed01;

            wind.volume = paused ? 0f : Mathf.Pow(speed01, 1.4f) * 0.55f * sfx;
            wind.pitch = 0.8f + 0.5f * speed01;

            crashCooldown -= Time.unscaledDeltaTime;
        }

        private static float WheelSlip(WheelCollider w)
        {
            if (w == null || !w.GetGroundHit(out WheelHit hit)) return 0f;
            return Mathf.Max(Mathf.Abs(hit.sidewaysSlip), Mathf.Max(0f, Mathf.Abs(hit.forwardSlip) - 0.3f));
        }

        private void FixedUpdate()
        {
            if (car == null || car.Body == null) return;
            Vector3 v = car.Body.linearVelocity;
            float dv = (v - lastVelocity).magnitude;
            lastVelocity = v;
            if (dv > 3.2f && crashCooldown <= 0f && Time.timeScale > 0f && crash != null)
            {
                crashCooldown = 0.35f;
                oneShot.PlayOneShot(crash, Mathf.Clamp01(dv / 12f) * GameSettings.Sfx * GameSettings.Master);
            }
        }
    }
}
