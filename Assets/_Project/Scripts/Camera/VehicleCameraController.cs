using CityDriver.Controls;
using CityDriver.Game;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CityDriver.CameraSystem
{
    /// <summary>Camera views. The CAM button cycles through them; the choice is stored in <see cref="GameSettings.CameraView"/>.</summary>
    public enum VehicleCameraMode { ThirdPerson = 0, FarChase = 1, Bonnet = 2, TopDown = 3 }

    /// <summary>
    /// Third-person chase camera with four views. Chase / far chase follow the heading with a lag, pull back and rise a little at
    /// speed, look further ahead, widen the field of view, and are pushed in front of walls (only buildings and road surfaces count:
    /// traffic, pedestrians and the player car never push the camera). Bonnet sits at the front of the car; top-down looks straight
    /// down with the heading up. Runs in LateUpdate so it follows the interpolated vehicle pose without jitter.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class VehicleCameraController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private Rigidbody targetBody;
        [SerializeField] private VehicleCameraMode mode = VehicleCameraMode.ThirdPerson;

        [Header("Framing (chase)")]
        [SerializeField, Min(1f)] private float distance = 6.5f;
        [SerializeField, Min(0f)] private float height = 2.6f;
        [Tooltip("Extra distance / height added at top speed so the car does not fill the screen when fast.")]
        [SerializeField, Min(0f)] private float extraDistanceAtSpeed = 1.6f;
        [SerializeField, Min(0f)] private float extraHeightAtSpeed = 0.5f;
        [Tooltip("Height above the vehicle origin that the camera looks at.")]
        [SerializeField, Min(0f)] private float lookHeight = 1.1f;
        [Tooltip("How far ahead of the car the camera looks at top speed (m).")]
        [SerializeField, Min(0f)] private float lookAheadDistance = 6f;
        [SerializeField, Min(1f)] private float lookAheadFullSpeed = 25f;

        [Header("Smoothing")]
        [SerializeField, Min(0.01f)] private float positionSmoothTime = 0.14f;
        [SerializeField, Min(0.01f)] private float yawSmoothTime = 0.22f;
        [SerializeField, Min(0.1f)] private float lookSmoothSpeed = 9f;

        [Header("Field of view")]
        [SerializeField, Range(30f, 90f)] private float baseFov = 62f;
        [SerializeField, Range(0f, 30f)] private float extraFovAtSpeed = 9f;

        [Header("Obstacle avoidance")]
        [SerializeField] private LayerMask collisionMask = 0;
        [SerializeField, Min(0.05f)] private float collisionRadius = 0.3f;
        [SerializeField, Min(0.5f)] private float minDistance = 1.8f;

        private Camera cam;
        private VehicleInputHandler inputHandler;
        private float yaw;
        private float yawVelocity;
        private Vector3 positionVelocity;
        private Vector3 smoothedLook;
        private bool snapNextFrame;
        private bool appliedSetting;

        // ---- cinematic override (Shadow Pass nightclub shot): the camera glides to a fixed pose, then hands back to the chase camera
        private bool cinematic;
        private Vector3 cinematicPos; private Quaternion cinematicRot;
        public bool CinematicActive => cinematic;
        public void SetCinematic(Vector3 position, Quaternion rotation) { cinematic = true; cinematicPos = position; cinematicRot = rotation; }
        public void EndCinematic() { cinematic = false; snapNextFrame = true; }

        public Transform Target => target;
        public VehicleCameraMode Mode => mode;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.nearClipPlane = 0.3f; cam.farClipPlane = Mathf.Max(cam.farClipPlane, 800f);      // 0.3 m near plane: far better depth precision, no z-fighting on road markings
            var urpData = cam.GetUniversalAdditionalCameraData(); urpData.renderPostProcessing = true; urpData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.FastApproximateAntialiasing;
            // chase framing tuned against the reference: the car sits low in the frame with plenty of road ahead (scene-serialised values are overridden)
            distance = 7.4f; height = 3.1f; lookHeight = 1.3f; lookAheadDistance = 7f; baseFov = 62f;
            collisionMask = LayerMask.GetMask("Environment", "Road");   // buildings and road surfaces only: traffic, people and the player car never push the camera
        }

        private void OnEnable() { GameSettings.Changed += OnSettingsChanged; mode = (VehicleCameraMode)GameSettings.CameraView; }
        private void OnDisable() { GameSettings.Changed -= OnSettingsChanged; }

        private void OnSettingsChanged()
        {
            var wanted = (VehicleCameraMode)GameSettings.CameraView;
            if (wanted != mode) { mode = wanted; snapNextFrame = true; }
        }

        /// <summary>Starts following a vehicle and snaps behind it.</summary>
        public void SetTarget(Transform newTarget, Rigidbody newBody, VehicleInputHandler handler = null)
        {
            target = newTarget;
            targetBody = newBody;
            inputHandler = handler;
            ResetCamera();
        }

        /// <summary>Snaps the camera directly behind the vehicle on the next frame.</summary>
        public void ResetCamera() => snapNextFrame = true;

        /// <summary>Switches to the next view (chase, far chase, bonnet, top-down).</summary>
        public void CycleMode() { GameSettings.CameraView = ((int)mode + 1) % 4; }

        public void SetMode(VehicleCameraMode newMode) { GameSettings.CameraView = (int)newMode; }

        private void LateUpdate()
        {
            if (target == null) return;
            if (inputHandler != null && inputHandler.ConsumeCameraReset() && !cinematic) CycleMode();
            if (cinematic)
            {
                float k = 1f - Mathf.Exp(-2.2f * Time.unscaledDeltaTime);
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, cinematicPos, k), Quaternion.Slerp(transform.rotation, cinematicRot, k));
                return;
            }
            Tick(Time.deltaTime);
        }

        /// <summary>Advances the camera by dt seconds. Public so tests can step it deterministically.</summary>
        public void Tick(float dt)
        {
            if (target == null) return;
            if (cam == null) cam = GetComponent<Camera>();
            if (!appliedSetting) { mode = (VehicleCameraMode)GameSettings.CameraView; appliedSetting = true; }

            Vector3 velocity = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
            float speed = velocity.magnitude;
            float speed01 = Mathf.Clamp01(speed / lookAheadFullSpeed);

            float targetYaw = target.eulerAngles.y;
            if (snapNextFrame) { yaw = targetYaw; yawVelocity = 0f; }
            else yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, mode == VehicleCameraMode.Bonnet ? 0.05f : yawSmoothTime, Mathf.Infinity, dt);
            Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);

            float fovTarget = baseFov + extraFovAtSpeed * speed01;

            switch (mode)
            {
                case VehicleCameraMode.Bonnet:
                {
                    Vector3 eye = target.TransformPoint(new Vector3(0f, 1.18f, 0.35f));
                    transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target.forward + Vector3.down * 0.03f, Vector3.up));
                    fovTarget = 72f + 8f * speed01;
                    smoothedLook = eye + target.forward * 10f;
                    break;
                }
                case VehicleCameraMode.TopDown:
                {
                    Vector3 above = target.position + Vector3.up * (26f + 14f * speed01) + yawRotation * Vector3.back * 2f;
                    Vector3 p = snapNextFrame ? above : Vector3.SmoothDamp(transform.position, above, ref positionVelocity, 0.12f, Mathf.Infinity, dt);
                    transform.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.down, yawRotation * Vector3.forward));
                    fovTarget = 48f;
                    break;
                }
                default:
                {
                    bool far = mode == VehicleCameraMode.FarChase;
                    float dist = (far ? 10.5f : distance) + extraDistanceAtSpeed * speed01;
                    float h = (far ? 4.6f : height) + extraHeightAtSpeed * speed01;
                    Vector3 pivot = target.position + Vector3.up * lookHeight;
                    Vector3 desired = pivot + yawRotation * new Vector3(0f, h - lookHeight, -dist);
                    desired = AvoidObstacles(pivot, desired);

                    Vector3 position;
                    if (snapNextFrame) { position = desired; positionVelocity = Vector3.zero; }
                    else position = Vector3.SmoothDamp(transform.position, desired, ref positionVelocity, positionSmoothTime, Mathf.Infinity, dt);
                    position = AvoidObstacles(pivot, position);     // the smoothed position can lag into a wall on fast turns

                    Vector3 look = pivot + yawRotation * Vector3.forward * (3.2f + lookAheadDistance * speed01);            // always look a little down the road
                    smoothedLook = snapNextFrame ? look : Vector3.Lerp(smoothedLook, look, 1f - Mathf.Exp(-lookSmoothSpeed * dt));

                    transform.position = position;
                    Vector3 toLook = smoothedLook - position;
                    if (toLook.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(toLook, Vector3.up);
                    break;
                }
            }

            if (cam != null) cam.fieldOfView = snapNextFrame ? fovTarget : Mathf.Lerp(cam.fieldOfView, fovTarget, 1f - Mathf.Exp(-3f * dt));
            snapNextFrame = false;
        }

        private Vector3 AvoidObstacles(Vector3 pivot, Vector3 desired)
        {
            Vector3 toCamera = desired - pivot;
            float length = toCamera.magnitude;
            if (length < 0.01f) return desired;

            Vector3 direction = toCamera / length;
            if (Physics.SphereCast(pivot, collisionRadius, direction, out RaycastHit hit, length, collisionMask, QueryTriggerInteraction.Ignore))
            {
                float safe = Mathf.Max(minDistance, hit.distance - 0.05f);
                return pivot + direction * Mathf.Min(safe, length);
            }

            return desired;
        }
    }
}
