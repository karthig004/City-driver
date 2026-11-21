using UnityEngine;

namespace CityDriver.Vehicle
{
    public enum DriveLayout { Front, Rear, All }

    /// <summary>
    /// All tuning for one vehicle model. Create one asset per car (Assets > Create > City Driver > Vehicle Stats);
    /// the controller reads everything from here, so new cars and future upgrades are data, not code.
    /// </summary>
    [CreateAssetMenu(fileName = "VehicleStats", menuName = "City Driver/Vehicle Stats")]
    public sealed class VehicleStats : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "City Sedan";

        [Header("Body")]
        [Min(100f)] public float mass = 1300f;
        [Tooltip("Local-space centre of mass. Keep it low for stability.")]
        public Vector3 centerOfMass = new Vector3(0f, 0.4f, 0.1f);
        [Min(0f)] public float linearDamping = 0.02f;
        [Min(0f)] public float angularDamping = 0.8f;
        [Min(1f)] public float maxAngularVelocity = 6f;

        [Header("Engine")]
        [Tooltip("Total torque at the driven wheels (Nm) at full throttle and low speed.")]
        [Min(0f)] public float maxMotorTorque = 1300f;
        [Min(10f)] public float maxSpeedKmh = 140f;
        [Min(5f)] public float maxReverseSpeedKmh = 30f;
        [Range(0.1f, 1f)] public float reverseTorqueFactor = 0.6f;
        public DriveLayout drivenWheels = DriveLayout.Rear;
        [Tooltip("Torque multiplier (0..1) over speed as a fraction of max speed.")]
        public AnimationCurve torqueBySpeed = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.45f, 0.85f), new Keyframe(0.85f, 0.5f), new Keyframe(1f, 0f));

        [Header("Brakes")]
        [Tooltip("Brake torque per wheel at full pedal (Nm).")]
        [Min(0f)] public float maxBrakeTorque = 620f;
        [Range(0f, 1f)] public float frontBrakeBias = 0.6f;
        [Min(0f)] public float handbrakeTorque = 2500f;
        [Tooltip("Light brake that stops the car creeping when stationary with no input.")]
        [Min(0f)] public float idleBrakeTorque = 250f;
        [Tooltip("Engine braking when coasting.")]
        [Min(0f)] public float coastBrakeTorque = 60f;

        [Header("Steering")]
        [Tooltip("Steering lock at walking pace (degrees).")]
        [Range(5f, 45f)] public float maxSteerAngleLow = 32f;
        [Tooltip("Smallest steering angle left at very high speed (degrees).")]
        [Range(0.2f, 10f)] public float minSteerAngle = 1f;
        [Tooltip("Steering never asks for more lateral acceleration than this (m/s2). Keep at or below the tyre grip so the car understeers gently instead of spinning.")]
        [Range(2f, 12f)] public float steerGripLimit = 8.5f;
        [Tooltip("Degrees per second the wheels turn towards the target angle.")]
        [Min(10f)] public float steerRate = 110f;
        [Min(10f)] public float steerReturnRate = 200f;

        [Header("Handling character (what makes each car feel different)")]
        [Tooltip("Rear sideways grip relative to the front. Below 1 = a loose tail that steps out; above 1 = planted, understeering.")]
        [Range(0.5f, 1.4f)] public float rearGripRatio = 1f;
        [Tooltip("Rear grip multiplier while a slide is being induced (handbrake or a power slide). Lower = easier, longer drifts.")]
        [Range(0.25f, 1f)] public float driftGripDrop = 0.6f;
        [Tooltip("How much the car steers itself into a slide (0 = none, 1 = full counter-steer help).")]
        [Range(0f, 1f)] public float counterSteer = 0.5f;
        [Tooltip("Multiplies the steering speed (sharp sports cars > 1, heavy cruisers < 1).")]
        [Range(0.5f, 1.8f)] public float steerResponse = 1f;
        [Tooltip("Steering input shaping: 1 = linear, higher = calmer around the centre and sharper at the end of the travel.")]
        [Range(1f, 1.8f)] public float steerCurve = 1f;
        [Tooltip("How readily full throttle plus a turn breaks the rear loose (rear-driven cars only). 0 = never, 1 = like a muscle car.")]
        [Range(0f, 1.2f)] public float powerOversteer = 0f;
        [Tooltip("The slide angle (degrees) the car is allowed to reach; beyond it the nose is pulled back towards the direction of travel so a drift never turns into a spin.")]
        [Range(15f, 70f)] public float maxDriftAngle = 40f;
        [Tooltip("Forward push that keeps speed up while the car is sliding under throttle.")]
        [Range(0f, 1f)] public float driftSustain = 0.3f;

        [Header("Suspension (per wheel)")]
        [Min(0.05f)] public float suspensionDistance = 0.22f;
        [Min(1000f)] public float spring = 32000f;
        [Min(100f)] public float damper = 4200f;
        [Range(0f, 1f)] public float targetPosition = 0.5f;
        [Min(1f)] public float wheelMass = 20f;
        [Min(0f)] public float wheelDampingRate = 0.25f;
        [Tooltip("Anti-roll bar stiffness. Higher = less body roll.")]
        [Min(0f)] public float antiRollFront = 6000f;
        [Min(0f)] public float antiRollRear = 4500f;

        [Header("Tyres (friction)")]
        [Min(0.1f)] public float forwardStiffness = 1.3f;
        [Min(0.1f)] public float sidewaysStiffness = 1.3f;

        [Header("Stability")]
        [Tooltip("Extra downward force = coefficient x speed^2 (N per (m/s)^2).")]
        [Min(0f)] public float downforce = 2f;
        [Tooltip("How strongly the car is steered towards the yaw rate its steering angle asks for (0 = off).")]
        [Range(0f, 1f)] public float yawAssist = 0.4f;
        [Min(0f)] public float yawAssistTorque = 2500f;
        [Tooltip("Body tilt (degrees) beyond which a righting torque is applied.")]
        [Range(10f, 80f)] public float uprightAssistStartAngle = 40f;
        [Min(0f)] public float uprightTorque = 5000f;
        [Tooltip("Automatically put the car back on its wheels after being flipped this long (seconds). 0 = disabled.")]
        [Min(0f)] public float autoRecoverDelay = 3f;

        [Header("Gearbox (display only)")]
        [Tooltip("Speeds (km/h) at which the display shifts up: 1>2, 2>3, 3>4, 4>5.")]
        public float[] upshiftSpeedsKmh = { 25f, 50f, 80f, 110f };
        [Min(0f)] public float shiftHysteresisKmh = 4f;

        [Header("Physics sub-stepping")]
        [Min(1f)] public float substepSpeed = 6f;
        [Range(1, 50)] public int substepsBelowSpeed = 8;
        [Range(1, 50)] public int substepsAboveSpeed = 4;
    }
}
