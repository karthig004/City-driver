using System.Collections.Generic;
using CityDriver.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CityDriver.Controls
{
    /// <summary>
    /// On-screen driving controls: steer left / right, accelerator, brake, reverse and camera reset. It reads every active
    /// touch (and the mouse, so it can be tested in the Editor) and hit-tests the button rectangles itself, so several fingers
    /// work at once (steer + accelerate) without needing an EventSystem. Publishes a <see cref="VehicleInput"/> that the
    /// vehicle's <see cref="MobileInputController"/> forwards. Runs before the input handler so there is no frame of latency.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class TouchControlsPanel : MonoBehaviour
    {
        [Header("Buttons (screen-space overlay rects)")]
        [SerializeField] private RectTransform steerLeft;
        [SerializeField] private RectTransform steerRight;
        [SerializeField] private RectTransform accelerator;
        [SerializeField] private RectTransform brake;
        [SerializeField] private RectTransform reverse;
        [SerializeField] private RectTransform cameraReset;

        [Header("Response")]
        [Tooltip("How fast steering ramps to full lock while a steer button is held (1/s).")]
        [SerializeField, Min(0.5f)] private float steerRise = 5f;
        [SerializeField, Min(0.5f)] private float steerReturn = 9f;
        [Tooltip("How fast the accelerator ramps up while held (1/s). Smooth enough to avoid wheelspin, quick enough to feel direct.")]
        [SerializeField, Min(0.5f)] private float throttleRise = 4f;
        [SerializeField, Min(0.5f)] private float throttleFall = 10f;

        [Header("Feedback")]
        [SerializeField] private Color idleTint = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] private Color pressedTint = new Color(0.35f, 0.7f, 1f, 0.85f);

        private readonly List<Vector2> pointers = new List<Vector2>(12);
        private VehicleInput current;
        private float steer, throttle;
        private bool cameraResetWasDown;

        // ---- steering modes (buttons / wheel / tilt)
        private RectTransform wheelRect;
        private float wheelAngle;                    // degrees, +ve = clockwise = steer right
        private const float WheelLock = 150f;
        private bool tiltEnabled;

        public static TouchControlsPanel Instance { get; private set; }

        /// <summary>The on-screen steering wheel (created by the UI layer). Used only in Wheel mode.</summary>
        public void SetWheel(RectTransform wheel) { wheelRect = wheel; }
        private RectTransform gearRect, gearKnob;
        /// <summary>Gear lever position: 0 = P, 1 = R, 2 = N, 3 = D (default).</summary>
        public int Gear { get; private set; } = 3;
        public void SetGear(RectTransform panel, RectTransform knob) { gearRect = panel; gearKnob = knob; }
        public void SelectGear(int index) { Gear = Mathf.Clamp(index, 0, 3); }
        private RectTransform handbrakeRect;
        /// <summary>The on-screen handbrake button (created by the UI layer).</summary>
        public void SetHandbrake(RectTransform button) { handbrakeRect = button; }
        public float WheelAngleDegrees => wheelAngle;
        public bool IsPressedWheel => wheelRect != null && HitTest(wheelRect);
        public RectTransform SteerLeftRect => steerLeft;
        public RectTransform SteerRightRect => steerRight;

        public VehicleInput Current => current;
        public bool IsPressed(RectTransform button) => HitTest(button);

        private List<Vector2> testPointers;

        /// <summary>Test seam: while set, these screen positions replace the real touch/mouse devices. Pass null to go back to real input.</summary>
        public void SetTestPointers(ICollection<Vector2> positions) { testPointers = positions == null ? null : new List<Vector2>(positions); }

        private void Awake()
        {
            Instance = this;
            // Visible on phones/tablets, in the Editor (mouse works as a finger) and on any device with a touch screen.
            gameObject.SetActive(Application.isMobilePlatform || Application.isEditor || Touchscreen.current != null);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            CollectPointers();

            bool left = HitTest(steerLeft), right = HitTest(steerRight);
            bool gas = HitTest(accelerator), brk = HitTest(brake), rev = HitTest(reverse), cam = HitTest(cameraReset);

            SteeringMode steerMode = GameSettings.Steering;
            float sens = GameSettings.SteerSensitivity;
            if (steerLeft != null && steerLeft.gameObject.activeSelf != (steerMode == SteeringMode.Buttons)) { steerLeft.gameObject.SetActive(steerMode == SteeringMode.Buttons); }
            if (steerRight != null && steerRight.gameObject.activeSelf != (steerMode == SteeringMode.Buttons)) { steerRight.gameObject.SetActive(steerMode == SteeringMode.Buttons); }
            if (wheelRect != null && wheelRect.gameObject.activeSelf != (steerMode == SteeringMode.Wheel)) wheelRect.gameObject.SetActive(steerMode == SteeringMode.Wheel);

            if (steerMode == SteeringMode.Buttons)
            {
                float steerTarget = ((right ? 1f : 0f) - (left ? 1f : 0f)) * Mathf.Clamp(sens, 0.5f, 1f);
                if (sens > 1f) steerTarget = Mathf.Clamp(steerTarget * sens, -1f, 1f);
                bool returning = steerTarget == 0f || (steer != 0f && Mathf.Sign(steerTarget) != Mathf.Sign(steer));
                steer = Mathf.MoveTowards(steer, steerTarget, (returning ? steerReturn : steerRise * sens) * Time.unscaledDeltaTime);
            }
            else if (steerMode == SteeringMode.Wheel)
            {
                steer = Mathf.Clamp(UpdateWheel(Time.unscaledDeltaTime) / WheelLock * sens, -1f, 1f);
            }
            else
            {
                steer = Mathf.MoveTowards(steer, ReadTilt() * sens, 8f * Time.unscaledDeltaTime);
            }
            throttle = Mathf.MoveTowards(throttle, gas ? 1f : 0f, (gas ? throttleRise : throttleFall) * Time.unscaledDeltaTime);
            UpdateGearLever();

            current = new VehicleInput
            {
                Steer = steer,
                Throttle = Gear == 3 ? throttle : 0f,
                Brake = brk ? 1f : 0f,
                Reverse = Gear == 1 ? throttle : (rev ? 1f : 0f),
                Handbrake = Gear == 0 || (handbrakeRect != null && HitTest(handbrakeRect)),
                CameraReset = cam && !cameraResetWasDown,    // one pulse per press
            };
            cameraResetWasDown = cam;

            Tint(steerLeft, left); Tint(steerRight, right); Tint(accelerator, gas); Tint(brake, brk); Tint(reverse, rev); Tint(cameraReset, cam);
        }

        // Gear lever: touching a position selects it; the knob slides there.
        private void UpdateGearLever()
        {
            if (gearRect == null || !gearRect.gameObject.activeInHierarchy) return;
            for (int i = 0; i < pointers.Count; i++)
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(gearRect, pointers[i], null)) continue;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(gearRect, pointers[i], null, out Vector2 local)) continue;
                Rect r = gearRect.rect;
                Gear = Mathf.Clamp(Mathf.FloorToInt((r.yMax - local.y) / (r.height / 4f)), 0, 3);
            }
            if (gearKnob != null)
            {
                float y = gearRect.rect.height * 0.5f - (Gear + 0.5f) * gearRect.rect.height / 4f;
                gearKnob.anchoredPosition = Vector2.Lerp(gearKnob.anchoredPosition, new Vector2(gearKnob.anchoredPosition.x, y), 0.35f);
            }
        }

        // Wheel: grab it anywhere on the rim and turn it like a real wheel. It follows the RELATIVE rotation of the finger from where you
        // grabbed it (no snapping to the touch point), keeps tracking even when the finger slides outside the wheel, and springs back
        // quickly (faster the more it is turned) when you let go.
        private bool wheelGrabbed; private float grabWheel0, grabFinger0, fingerUnwrapped, lastFinger; private Vector2 grabPos;

        private float UpdateWheel(float dt)
        {
            if (wheelRect == null) return 0f;
            Vector3 c3 = wheelRect.TransformPoint(wheelRect.rect.center);
            Vector2 centre = new Vector2(c3.x, c3.y);
            var corners = new Vector3[4]; wheelRect.GetWorldCorners(corners);
            float radiusPx = Mathf.Abs(corners[2].x - corners[0].x) * 0.5f;

            if (!wheelGrabbed)
            {
                for (int i = 0; i < pointers.Count; i++)
                {
                    Vector2 d = pointers[i] - centre;
                    if (d.magnitude > radiusPx * 1.2f || d.sqrMagnitude < 25f) continue;
                    wheelGrabbed = true; grabPos = pointers[i];
                    lastFinger = grabFinger0 = fingerUnwrapped = Vector2.SignedAngle(Vector2.up, d); grabWheel0 = wheelAngle;
                    break;
                }
            }
            if (wheelGrabbed)
            {
                int best = -1; float bd = 280f;
                for (int i = 0; i < pointers.Count; i++) { float dd = Vector2.Distance(pointers[i], grabPos); if (dd < bd) { bd = dd; best = i; } }
                if (best < 0) wheelGrabbed = false;
                else
                {
                    grabPos = pointers[best];
                    Vector2 d = grabPos - centre;
                    if (d.sqrMagnitude > 25f)
                    {
                        float cur = Vector2.SignedAngle(Vector2.up, d);
                        fingerUnwrapped += Mathf.DeltaAngle(lastFinger, cur); lastFinger = cur;
                        float target = Mathf.Clamp(grabWheel0 - (fingerUnwrapped - grabFinger0), -WheelLock * 1.15f, WheelLock * 1.15f);    // clockwise finger = steer right
                        wheelAngle = Mathf.MoveTowards(wheelAngle, target, 1800f * dt);
                    }
                }
            }
            if (!wheelGrabbed) wheelAngle = Mathf.MoveTowards(wheelAngle, 0f, (260f + Mathf.Abs(wheelAngle) * 3.2f) * dt);
            wheelRect.localRotation = Quaternion.Euler(0f, 0f, -wheelAngle);
            return Mathf.Clamp(wheelAngle, -WheelLock, WheelLock);
        }

        // Tilt: roll the phone like a steering wheel. Sign depends on the screen orientation (landscape left/right); the
        // accelerometer path is implemented but has not been verified on a physical phone.
        private float ReadTilt()
        {
            var acc = Accelerometer.current;
            if (acc == null) return 0f;
            if (!tiltEnabled) { InputSystem.EnableDevice(acc); tiltEnabled = true; }
            Vector3 g = acc.acceleration.ReadValue();
            float roll = Screen.orientation == ScreenOrientation.LandscapeRight ? g.y : -g.y;
            return Mathf.Clamp(roll / 0.45f, -1f, 1f);
        }

        private void CollectPointers()
        {
            pointers.Clear();
            if (testPointers != null) { pointers.AddRange(testPointers); return; }
            Touchscreen ts = Touchscreen.current;
            if (ts != null)
            {
                var touches = ts.touches;
                for (int i = 0; i < touches.Count; i++)
                    if (touches[i].isInProgress) pointers.Add(touches[i].position.ReadValue());
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed) pointers.Add(mouse.position.ReadValue());
        }

        private bool HitTest(RectTransform button)
        {
            if (button == null || !button.gameObject.activeInHierarchy) return false;
            for (int i = 0; i < pointers.Count; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(button, pointers[i], null)) return true;
            return false;
        }

        private void Tint(RectTransform button, bool pressed)
        {
            if (button == null) return;
            var image = button.GetComponent<Image>();
            if (image != null) image.color = pressed ? pressedTint : idleTint;
        }
    }
}
