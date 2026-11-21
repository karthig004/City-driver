using UnityEngine;

namespace CityDriver.Controls
{
    /// <summary>
    /// Device-independent driving command for one frame. Keyboard, touch, AI or a replay all produce this same struct,
    /// so the vehicle physics never knows which device is driving it.
    /// </summary>
    public struct VehicleInput
    {
        /// <summary>-1 (full left) .. +1 (full right).</summary>
        public float Steer;
        /// <summary>0..1 accelerator.</summary>
        public float Throttle;
        /// <summary>0..1 foot brake.</summary>
        public float Brake;
        /// <summary>0..1 reverse pedal. Brakes first while still rolling forward, then drives backwards.</summary>
        public float Reverse;
        public bool Handbrake;

        /// <summary>One-shot pulses (true only on the frame they were requested).</summary>
        public bool CameraReset;
        public bool Recover;

        /// <summary>Combines two sources: strongest value wins per channel.</summary>
        public void Merge(in VehicleInput other)
        {
            if (Mathf.Abs(other.Steer) > Mathf.Abs(Steer)) Steer = other.Steer;
            Throttle = Mathf.Max(Throttle, other.Throttle);
            Brake = Mathf.Max(Brake, other.Brake);
            Reverse = Mathf.Max(Reverse, other.Reverse);
            Handbrake |= other.Handbrake;
            CameraReset |= other.CameraReset;
            Recover |= other.Recover;
        }
    }

    /// <summary>Implemented by anything that can drive a vehicle (keyboard now, touch UI in Phase 6).</summary>
    public interface IVehicleInputSource
    {
        /// <summary>Write this source's current command into <paramref name="input"/>. Called once per rendered frame.</summary>
        void Read(ref VehicleInput input);
    }
}
