using UnityEngine;

namespace CityDriver.Missions
{
    /// <summary>
    /// Player-facing waypoint: a small floating gem, a thin stem, a slim ground ring and a distance label.
    /// Replaces the old debug-style beacon. It bobs and spins gently, grows a little with distance so it stays
    /// readable from far away, and fades its ring/stem out when the player is far (cheap on/off, no per-frame allocation).
    /// </summary>
    public sealed class WaypointIndicator : MonoBehaviour
    {
        [Header("Parts")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform gem;
        [SerializeField] private GameObject ring;
        [SerializeField] private GameObject stem;
        [SerializeField] private TextMesh label;

        [Header("Motion")]
        [SerializeField, Min(0f)] private float hoverHeight = 2.3f;
        [SerializeField, Min(0f)] private float bobAmplitude = 0.10f;
        [SerializeField, Min(0f)] private float bobSpeed = 1.6f;
        [SerializeField] private float spinDegreesPerSecond = 55f;

        [Header("Distance behaviour")]
        [Tooltip("The waypoint grows with distance so it keeps a readable on-screen size: scale = distance x this, clamped.")]
        [SerializeField, Min(0.001f)] private float scalePerMetre = 0.08f;
        [SerializeField, Min(0.1f)] private float minScale = 1f;
        [SerializeField, Min(0.1f)] private float maxScale = 5f;
        [SerializeField, Min(1f)] private float ringVisibleDistance = 30f;
        [SerializeField, Min(1f)] private float stemVisibleDistance = 45f;
        [Tooltip("Distance label rounds to this many metres.")]
        [SerializeField, Min(1)] private int labelStepMetres = 5;

        private Transform viewer;
        private Camera viewCamera;
        private int shownMetres = -1;
        private bool ringOn = true, stemOn = true;

        /// <summary>Who the distance is measured from (normally the player's vehicle).</summary>
        public void SetViewer(Transform newViewer)
        {
            viewer = newViewer;
            shownMetres = -1;
        }

        private void OnEnable() => shownMetres = -1;

        private void LateUpdate()
        {
            if (gem == null) return;

            float t = Time.time;
            gem.localPosition = new Vector3(0f, hoverHeight + Mathf.Sin(t * bobSpeed) * bobAmplitude, 0f);
            gem.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.Self);

            if (viewer == null) return;
            float distance = Vector3.Distance(viewer.position, transform.position);

            float scale = Mathf.Clamp(distance * scalePerMetre, minScale, maxScale);
            if (visualRoot != null) visualRoot.localScale = new Vector3(scale, scale, scale);

            SetActiveIfChanged(ring, distance < ringVisibleDistance, ref ringOn);
            SetActiveIfChanged(stem, distance < stemVisibleDistance, ref stemOn);

            if (label != null)
            {
                int metres = Mathf.RoundToInt(distance / labelStepMetres) * labelStepMetres;
                if (metres != shownMetres) { shownMetres = metres; label.text = metres + " m"; }

                if (viewCamera == null) viewCamera = Camera.main;
                if (viewCamera != null) label.transform.rotation = viewCamera.transform.rotation; // billboard
            }
        }

        private static void SetActiveIfChanged(GameObject go, bool on, ref bool state)
        {
            if (go == null || state == on) return;
            state = on;
            go.SetActive(on);
        }
    }
}
