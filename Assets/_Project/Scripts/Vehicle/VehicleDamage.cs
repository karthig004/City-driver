using System;
using UnityEngine;

namespace CityDriver.Vehicle
{
    /// <summary>
    /// Vehicle condition (0-100 %). Collisions with anything that is not floor reduce it according to impact speed.
    /// Exposes events for later systems (HUD, "Safe Driver" mission) and a performance factor for the engine.
    /// </summary>
    public sealed class VehicleDamage : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float maxCondition = 100f;

        [Tooltip("Impacts slower than this (m/s) cause no damage.")]
        [SerializeField, Min(0f)] private float minImpactSpeed = 3f;

        [Tooltip("Condition lost per m/s of impact speed above the minimum.")]
        [SerializeField, Min(0f)] private float damagePerMetrePerSecond = 1.8f;

        [Tooltip("Impacts at or above this speed (m/s) raise MajorCollision.")]
        [SerializeField, Min(0f)] private float majorImpactSpeed = 9f;

        [Tooltip("Engine power left when condition reaches 0 (1 = no effect).")]
        [SerializeField, Range(0.2f, 1f)] private float minPerformance = 0.6f;

        [Tooltip("Surfaces whose normal points up at least this much are floor, not a collision.")]
        [SerializeField, Range(0.3f, 1f)] private float groundNormalY = 0.7f;

        private float condition;
        private bool initialised;

        /// <summary>1 = normal damage; armour upgrades lower it (e.g. 0.4 = 60% less damage).</summary>
        public float DamageMultiplier { get; set; } = 1f;

        /// <summary>Raised when condition changes. Argument = new condition 0..1.</summary>
        public event Action<float> ConditionChanged;

        /// <summary>Raised on a hard hit. Argument = impact speed in m/s.</summary>
        public event Action<float> MajorCollision;

        public float Condition01 { get { EnsureInitialised(); return condition / maxCondition; } }
        public float PerformanceFactor => Mathf.Lerp(minPerformance, 1f, Condition01);

        private void Awake() => EnsureInitialised();

        private void EnsureInitialised()
        {
            if (initialised) return;
            initialised = true;
            condition = maxCondition;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0) return;
            if (collision.GetContact(0).normal.y >= groundNormalY) return; // landing on floor / ramps

            float impact = collision.relativeVelocity.magnitude;
            ApplyImpact(impact);
        }

        /// <summary>Applies damage for an impact of the given speed (m/s). Public so tests and scripted events can use it.</summary>
        public void ApplyImpact(float impactSpeed)
        {
            EnsureInitialised();
            if (impactSpeed < minImpactSpeed) return;

            condition = Mathf.Max(0f, condition - (impactSpeed - minImpactSpeed) * damagePerMetrePerSecond * DamageMultiplier);
            ConditionChanged?.Invoke(Condition01);
            if (impactSpeed >= majorImpactSpeed) MajorCollision?.Invoke(impactSpeed);
        }

        public void Repair()
        {
            initialised = true;
            condition = maxCondition;
            ConditionChanged?.Invoke(Condition01);
        }

        /// <summary>Restores a saved condition value (0..1).</summary>
        public void SetCondition01(float value01)
        {
            initialised = true;
            condition = Mathf.Clamp01(value01) * maxCondition;
            ConditionChanged?.Invoke(Condition01);
        }
    }
}
