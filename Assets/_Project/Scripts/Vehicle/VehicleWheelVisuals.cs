using UnityEngine;

namespace CityDriver.Vehicle
{
    /// <summary>
    /// Keeps wheel meshes in sync with their WheelColliders (position from suspension, rotation from spin and steering).
    /// Each visual's pivot must sit at the wheel centre with its local X axis along the axle.
    /// Runs in LateUpdate so it follows the interpolated rigidbody pose.
    /// </summary>
    public sealed class VehicleWheelVisuals : MonoBehaviour
    {
        [SerializeField] private WheelCollider[] colliders = new WheelCollider[4];
        [SerializeField] private Transform[] visuals = new Transform[4];

        private void OnValidate()
        {
            if (colliders.Length != visuals.Length)
                Debug.LogWarning($"[VehicleWheelVisuals] '{name}': colliders ({colliders.Length}) and visuals ({visuals.Length}) must match.", this);
        }

        private void LateUpdate() => UpdateVisuals();

        /// <summary>Copies the current WheelCollider poses onto the visual transforms.</summary>
        public void UpdateVisuals()
        {
            int count = Mathf.Min(colliders.Length, visuals.Length);
            for (int i = 0; i < count; i++)
            {
                if (colliders[i] == null || visuals[i] == null) continue;
                colliders[i].GetWorldPose(out Vector3 position, out Quaternion rotation);
                visuals[i].SetPositionAndRotation(position, rotation);
            }
        }
    }
}
