using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityDriver.World
{
    /// <summary>Where one streamed chunk lives and how big it is. Serialised into the streamer so no chunk has to be loaded to be known.</summary>
    [Serializable]
    public struct ChunkInfo
    {
        public string scenePath;
        public Vector2Int index;
        public Vector3 center;
        public float radius;
        public int triangleCount;
        public int buildingCount;
    }

    /// <summary>
    /// True streaming: every 240 m chunk of the city is its own scene. Chunks near the player are loaded additively and
    /// asynchronously (a couple at a time, so there are no hitches); far chunks are unloaded to give the memory back. The main
    /// scene stays small, so Play and app start are fast, and the city can be far larger than one scene could hold.
    /// </summary>
    public sealed class ChunkStreamer : MonoBehaviour
    {
        [SerializeField] private ChunkInfo[] chunks = new ChunkInfo[0];
        [Tooltip("Chunks whose nearest edge is within this many metres of the player are loaded.")]
        [SerializeField, Min(100f)] private float loadDistance = 230f;
        [Tooltip("A loaded chunk stays loaded until it is this much farther away.")]
        [SerializeField, Min(0f)] private float hysteresis = 60f;
        [Tooltip("Chunks that must be loaded before the player may spawn / drive (nearest edge within this distance).")]
        [SerializeField, Min(20f)] private float requiredDistance = 140f;
        [SerializeField, Range(1, 6)] private int maxConcurrentLoads = 2;
        [SerializeField, Min(0.1f)] private float interval = 0.4f;

        private Transform viewer;
        private float next;
        private enum Phase : byte { Idle, Loading, Unloading }

        private AsyncOperation[] operations;
        private Phase[] phase;
        private bool[] loaded;
        private int pendingLoads;
        private float[] startedAt;
        private bool[] warned;
        private float loadSecondsSum;
        private int loadSamples;

        public int LoadedCount { get; private set; }
        public float AverageLoadSeconds => loadSamples == 0 ? 0f : loadSecondsSum / loadSamples;
        public float MaxLoadSeconds { get; private set; }
        public int CompletedLoads => loadSamples;
        public int TotalCount => chunks.Length;
        public int PendingLoads => pendingLoads;
        public float LoadDistance { get => loadDistance; set => loadDistance = Mathf.Max(100f, value); }

        public void SetChunks(ChunkInfo[] infos) { chunks = infos; operations = null; loaded = null; phase = null; }

        public void SetViewer(Transform newViewer)
        {
            viewer = newViewer;
            if (viewer != null) Refresh(viewer.position);
        }

        private bool initialLoadDone;
        private ThreadPriority savedPriority;

        // While the area around the spawn point is still loading, give scene integration more of each frame so the game starts fast.
        private void Awake() { EnsureState(); savedPriority = Application.backgroundLoadingPriority; Application.backgroundLoadingPriority = ThreadPriority.High; }

        private void OnDestroy() { Application.backgroundLoadingPriority = savedPriority; }

        private void Start() { if (viewer == null) Refresh(new Vector3(40f, 0f, 22f)); }   // spawn point until the player exists

        private void Update()
        {
            if (viewer == null || Time.time < next) return;
            next = Time.time + interval;
            Refresh(viewer.position);
        }

        private void EnsureState()
        {
            if (operations == null || operations.Length != chunks.Length) { operations = new AsyncOperation[chunks.Length]; phase = new Phase[chunks.Length]; loaded = new bool[chunks.Length]; startedAt = new float[chunks.Length]; warned = new bool[chunks.Length]; }
        }

        private float EdgeDistance(int i, Vector3 position)
        {
            float dx = chunks[i].center.x - position.x, dz = chunks[i].center.z - position.z;
            return Mathf.Sqrt(dx * dx + dz * dz) - chunks[i].radius;
        }

        /// <summary>Starts loading chunks near <paramref name="position"/> and unloads far ones. Safe to call every frame.</summary>
        public void Refresh(Vector3 position)
        {
            EnsureState();
            int count = 0; pendingLoads = 0;

            // 1) Finish operations that completed.
            for (int i = 0; i < chunks.Length; i++)
            {
                if (phase[i] != Phase.Idle && operations[i] != null && operations[i].isDone)
                {
                    if (phase[i] == Phase.Loading) { float took = Time.realtimeSinceStartup - startedAt[i]; loadSecondsSum += took; loadSamples++; MaxLoadSeconds = Mathf.Max(MaxLoadSeconds, took); }
                    loaded[i] = phase[i] == Phase.Loading;
                    phase[i] = Phase.Idle; operations[i] = null;
                }

                if (phase[i] == Phase.Loading)
                {
                    pendingLoads++;
                    float age = Time.realtimeSinceStartup - startedAt[i];
                    if (age > 15f && !warned[i] && operations[i] != null)
                    {
                        warned[i] = true;
                        Debug.LogWarning($"[ChunkStreamer] load of {chunks[i].scenePath} still running after {age:F0}s: progress {operations[i].progress:F2}, allowSceneActivation {operations[i].allowSceneActivation}, priority {operations[i].priority}, backgroundLoadingPriority {Application.backgroundLoadingPriority}, frame {Time.frameCount}, dt {Time.unscaledDeltaTime * 1000f:F0} ms, loaded scenes {SceneManager.loadedSceneCount}");
                    }
                }
                if (loaded[i]) count++;
            }

            // 2) Start new work. A chunk that is still loading/unloading is never touched again until that finishes.
            for (int i = 0; i < chunks.Length; i++)
            {
                if (phase[i] != Phase.Idle) continue;
                float d = EdgeDistance(i, position);
                bool want = loaded[i] ? d < loadDistance + hysteresis : d < loadDistance;

                if (want && !loaded[i])
                {
                    if (pendingLoads >= maxConcurrentLoads) continue;
                    operations[i] = SceneManager.LoadSceneAsync(chunks[i].scenePath, LoadSceneMode.Additive);
                    if (operations[i] == null) { Scene existing = SceneManager.GetSceneByPath(chunks[i].scenePath); Debug.LogError($"[ChunkStreamer] Could not load '{chunks[i].scenePath}': buildIndex {SceneUtility.GetBuildIndexByScenePath(chunks[i].scenePath)}, scenesInBuild {SceneManager.sceneCountInBuildSettings}, sceneAlreadyKnown {existing.IsValid()} loaded {existing.isLoaded} loading {existing.isLoaded == false && existing.IsValid()}, loadedSceneCount {SceneManager.loadedSceneCount}, sceneCount {SceneManager.sceneCount}"); continue; }
                    phase[i] = Phase.Loading; startedAt[i] = Time.realtimeSinceStartup; warned[i] = false; pendingLoads++;
                }
                else if (!want && loaded[i])
                {
                    Scene s = SceneManager.GetSceneByPath(chunks[i].scenePath);
                    if (!s.isLoaded) { loaded[i] = false; count--; continue; }
                    operations[i] = SceneManager.UnloadSceneAsync(s);
                    phase[i] = Phase.Unloading;
                    count--; loaded[i] = false;
                }
            }

            LoadedCount = count;
        }

        /// <summary>Human-readable list of required chunks that are not ready yet (for diagnostics).</summary>
        public string DescribeBlocking(Vector3 position)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < chunks.Length; i++)
                if (EdgeDistance(i, position) < requiredDistance && !(loaded[i] && phase[i] == Phase.Idle))
                    sb.Append($"[{chunks[i].index.x},{chunks[i].index.y}: loaded={loaded[i]} phase={phase[i]} op={(operations[i] == null ? "null" : operations[i].isDone ? "done" : "running " + operations[i].progress.ToString("0.00"))} sceneLoaded={SceneManager.GetSceneByPath(chunks[i].scenePath).isLoaded}] ");
            sb.Append($"pendingLoads={pendingLoads}");
            return sb.ToString();
        }

        /// <summary>0..1 share of the chunks within the required distance of <paramref name="position"/> that are loaded and idle (for the loading bar).</summary>
        public float ReadyFraction(Vector3 position)
        {
            if (chunks == null || phase == null) return 0f;
            int need = 0, ready = 0;
            for (int i = 0; i < chunks.Length; i++)
                if (EdgeDistance(i, position) < requiredDistance) { need++; if (loaded[i] && phase[i] == Phase.Idle) ready++; }
            return need == 0 ? 1f : (float)ready / need;
        }

        /// <summary>True when every chunk within the required distance of <paramref name="position"/> is fully loaded (safe to place the car).</summary>
        public bool IsReadyAround(Vector3 position)
        {
            Refresh(position);
            for (int i = 0; i < chunks.Length; i++)
                if (EdgeDistance(i, position) < requiredDistance && !(loaded[i] && phase[i] == Phase.Idle)) return false;
            if (!initialLoadDone) { initialLoadDone = true; Application.backgroundLoadingPriority = ThreadPriority.Low; }   // gameplay: integrate new city chunks in small slices so driving stays smooth
            return true;
        }
    }
}
