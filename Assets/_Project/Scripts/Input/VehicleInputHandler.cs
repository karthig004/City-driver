using UnityEngine;

namespace CityDriver.Controls
{
    /// <summary>
    /// Collects every <see cref="IVehicleInputSource"/> on this GameObject once per frame, merges them, and exposes the
    /// result to the vehicle. One-shot requests (camera reset, recover) are latched until consumed, so they are never
    /// lost between the render frame and the next physics step.
    /// </summary>
    public sealed class VehicleInputHandler : MonoBehaviour
    {
        private IVehicleInputSource[] sources;
        private VehicleInput current;
        private bool cameraResetPending;
        private bool recoverPending;

        public VehicleInput Current => current;

        // OnEnable (not Awake) so the source list is rebuilt after a script reload during Play Mode too.
        private void OnEnable() => RefreshSources();

        /// <summary>Call after adding or removing an input component at runtime.</summary>
        public void RefreshSources() => sources = GetComponents<IVehicleInputSource>();

        private void Update()
        {
            if (sources == null) RefreshSources();
            var merged = default(VehicleInput);
            for (int i = 0; i < sources.Length; i++)
            {
                var v = default(VehicleInput);
                sources[i].Read(ref v);
                merged.Merge(v);
            }

            current = merged;
            cameraResetPending |= merged.CameraReset;
            recoverPending |= merged.Recover;
        }

        public bool ConsumeCameraReset() { bool r = cameraResetPending; cameraResetPending = false; return r; }

        public bool ConsumeRecover() { bool r = recoverPending; recoverPending = false; return r; }
    }
}
