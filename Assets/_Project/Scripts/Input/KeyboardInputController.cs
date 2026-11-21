using UnityEngine;
using UnityEngine.InputSystem;

namespace CityDriver.Controls
{
    /// <summary>
    /// Editor / desktop testing controls (Input System package):
    /// W or Up = accelerate, S or Down = reverse (brakes first when rolling forward), Space = brake,
    /// A/D or Left/Right = steer, Left Shift = handbrake, C = reset camera, R = recover (flip back upright).
    /// </summary>
    public sealed class KeyboardInputController : MonoBehaviour, IVehicleInputSource
    {
        [Tooltip("How fast digital steering keys ramp towards full lock (1/s).")]
        [SerializeField, Min(0.1f)] private float steerRiseRate = 4f;

        [Tooltip("How fast steering returns to centre when keys are released (1/s).")]
        [SerializeField, Min(0.1f)] private float steerReturnRate = 7f;

        private float steer;

        public void Read(ref VehicleInput input)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) { steer = 0f; return; }

            float target = 0f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) target -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) target += 1f;

            float rate = Mathf.Approximately(target, 0f) || Mathf.Sign(target) != Mathf.Sign(steer) && steer != 0f ? steerReturnRate : steerRiseRate;
            steer = Mathf.MoveTowards(steer, target, rate * Time.unscaledDeltaTime);

            input.Steer = steer;
            input.Throttle = (kb.wKey.isPressed || kb.upArrowKey.isPressed) ? 1f : 0f;
            input.Reverse = (kb.sKey.isPressed || kb.downArrowKey.isPressed) ? 1f : 0f;
            input.Brake = kb.spaceKey.isPressed ? 1f : 0f;
            input.Handbrake = kb.leftShiftKey.isPressed;
            input.CameraReset = kb.cKey.wasPressedThisFrame;
            input.Recover = kb.rKey.wasPressedThisFrame;
        }
    }
}
