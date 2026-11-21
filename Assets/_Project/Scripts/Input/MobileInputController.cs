using UnityEngine;

namespace CityDriver.Controls
{
    /// <summary>
    /// Vehicle-side adapter for touch input: forwards what the scene's <see cref="TouchControlsPanel"/> publishes.
    /// Lives on the vehicle next to the keyboard controller, so the physics never knows which device is driving.
    /// </summary>
    public sealed class MobileInputController : MonoBehaviour, IVehicleInputSource
    {
        public void Read(ref VehicleInput input)
        {
            TouchControlsPanel panel = TouchControlsPanel.Instance;
            if (panel == null) return;
            input.Merge(panel.Current);
        }
    }
}
