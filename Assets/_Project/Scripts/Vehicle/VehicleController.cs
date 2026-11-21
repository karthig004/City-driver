using CityDriver.Controls;
using UnityEngine;

namespace CityDriver.Vehicle
{
    /// <summary>
    /// Rigidbody + four WheelColliders vehicle. Reads a <see cref="VehicleInput"/> (from <see cref="VehicleInputHandler"/>),
    /// applies motor / brake / steering, anti-roll bars, downforce, yaw assist and flip recovery.
    /// All numbers come from <see cref="VehicleStats"/>. No allocations in the physics loop.
    ///
    /// Why WheelCollider: built-in, supported on Android/Web, stable at the speeds we need, cheap for 4 wheels, and it
    /// gives suspension + tyre slip for free. Trade-off: its friction model is fiddly and not very realistic at limit,
    /// so stability is helped by the anti-roll / downforce / yaw-assist layer below instead of a custom tyre model.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-10)]
    public sealed class VehicleController : MonoBehaviour
    {
        [Header("Setup")]
        [SerializeField] private VehicleStats stats;
        [SerializeField] private WheelCollider frontLeft;
        [SerializeField] private WheelCollider frontRight;
        [SerializeField] private WheelCollider rearLeft;
        [SerializeField] private WheelCollider rearRight;

        [Header("Optional")]
        [SerializeField] private VehicleInputHandler inputHandler;
        [SerializeField] private VehicleDamage damage;

        private static readonly string[] GearLabels = { "R", "N", "1", "2", "3", "4", "5", "6", "7", "8" };

        private Rigidbody body;
        private WheelCollider[] wheels;
        private float wheelbase = 2.7f;
        private float steerAngle;
        private float flippedTimer;
        private int gear;
        private int groundedWheels;
        private VehicleInput manualInput;

        // drift model
        private float driftIntent, driftAmount, slipDeg, rearMul = 1f, appliedRearMul = -1f, driftClock, driftScoreRunning;
        private WheelFrictionCurve sideBase;

        /// <summary>0..1: how far the car is sliding (angle between where it points and where it travels), smoothed. For smoke, sound and the HUD.</summary>
        public float DriftAmount => driftAmount;
        /// <summary>Signed slip angle in degrees (+ = travelling to the right of where the nose points).</summary>
        public float SlipAngle => slipDeg;
        public bool IsDrifting => driftAmount > 0.25f;
        /// <summary>Points scored by the drift in progress (0 when not drifting). It banks into <see cref="LastDriftScore"/> when the slide ends.</summary>
        public int DriftScore => Mathf.RoundToInt(driftScoreRunning);
        public int LastDriftScore { get; private set; }
        public float LastDriftTime { get; private set; } = -100f;

        public VehicleStats Stats => stats;
        public Rigidbody Body => body;
        public WheelCollider FrontLeft => frontLeft;
        public WheelCollider FrontRight => frontRight;
        public WheelCollider RearLeft => rearLeft;
        public WheelCollider RearRight => rearRight;

        /// <summary>Speed in km/h (always >= 0).</summary>
        public float SpeedKmh { get; private set; }
        /// <summary>Signed speed along the car's forward axis in m/s (negative = reversing).</summary>
        public float ForwardSpeed { get; private set; }
        /// <summary>-1 = reverse, 0 = neutral, 1..5 = forward gear (display only).</summary>
        public int Gear => gear;
        public string GearLabel => GearLabels[Mathf.Clamp(gear + 1, 0, GearLabels.Length - 1)];
        /// <summary>Front wheel steering angle in degrees (+ = right).</summary>
        public float SteerAngle => steerAngle;
        public int GroundedWheels => groundedWheels;
        public float TiltAngle { get; private set; }
        public float Condition01 => damage != null ? damage.Condition01 : 1f;
        public VehicleDamage Damage => damage;

        // ------------------------------------------------------------------------------------ setup

        // OnEnable (not Awake) so references are rebuilt after a script reload during Play Mode too.
        private void OnEnable()
        {
            body = GetComponent<Rigidbody>();
            if (!Validate()) { enabled = false; return; }
            ApplyStats();
            if (GetComponent<DriftFx>() == null) gameObject.AddComponent<DriftFx>();         // tyre smoke and skid marks
        }

        private bool Validate()
        {
            if (stats == null) { Debug.LogError($"[VehicleController] '{name}': VehicleStats is not assigned.", this); return false; }
            if (frontLeft == null || frontRight == null || rearLeft == null || rearRight == null)
            { Debug.LogError($"[VehicleController] '{name}': all four WheelColliders must be assigned.", this); return false; }

            wheels = new[] { frontLeft, frontRight, rearLeft, rearRight };
            return true;
        }

        /// <summary>Swaps the tuning (vehicle class + upgrades + weather) and re-applies it to the Rigidbody and WheelColliders.</summary>
        public void SetStats(VehicleStats newStats) { if (newStats == null) return; stats = newStats; ApplyStats(); }

        /// <summary>Pushes <see cref="VehicleStats"/> into the Rigidbody and WheelColliders. Call again after swapping stats.</summary>
        public void ApplyStats()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (wheels == null && !Validate()) return;

            body.mass = stats.mass;
            body.centerOfMass = stats.centerOfMass;
            body.linearDamping = stats.linearDamping;
            body.angularDamping = stats.angularDamping;
            body.maxAngularVelocity = stats.maxAngularVelocity;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var spring = new JointSpring { spring = stats.spring, damper = stats.damper, targetPosition = stats.targetPosition };
            var forward = new WheelFrictionCurve { extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.6f, stiffness = stats.forwardStiffness };
            var sideways = new WheelFrictionCurve { extremumSlip = 0.25f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.75f, stiffness = stats.sidewaysStiffness };

            sideBase = sideways; appliedRearMul = -1f;
            foreach (WheelCollider w in wheels)
            {
                w.suspensionDistance = stats.suspensionDistance;
                w.suspensionSpring = spring;
                w.mass = stats.wheelMass;
                w.wheelDampingRate = stats.wheelDampingRate;
                w.forwardFriction = forward;
                w.sidewaysFriction = sideways;
                w.forceAppPointDistance = 0.1f;
                w.ConfigureVehicleSubsteps(stats.substepSpeed, stats.substepsBelowSpeed, stats.substepsAboveSpeed);
            }

            SetRearGrip(rearMul);
            Vector3 delta = transform.InverseTransformPoint(frontLeft.transform.position) - transform.InverseTransformPoint(rearLeft.transform.position);
            wheelbase = Mathf.Max(1f, Mathf.Abs(delta.z));
        }

        // ------------------------------------------------------------------------------------ loop

        /// <summary>Lets code (tests, AI, replays) drive the car when there is no <see cref="VehicleInputHandler"/>.</summary>
        public void SetManualInput(in VehicleInput input) => manualInput = input;

        private void FixedUpdate()
        {
            if (inputHandler != null && inputHandler.ConsumeRecover()) Recover();
            Tick(Time.fixedDeltaTime, inputHandler != null ? inputHandler.Current : manualInput);
        }

        /// <summary>One physics step of vehicle logic. Normally called from FixedUpdate; public so it can be stepped by tests.</summary>
        public void Tick(float dt, in VehicleInput input)
        {
            if (wheels == null) return;

            Vector3 velocity = body.linearVelocity;
            float speed = velocity.magnitude;
            float fwd = Vector3.Dot(velocity, transform.forward);
            ForwardSpeed = fwd;
            SpeedKmh = speed * 3.6f;
            float performance = damage != null ? damage.PerformanceFactor : 1f;

            // ---- pedals -> motor torque / brake demand
            float maxSpeed = stats.maxSpeedKmh / 3.6f;
            float maxReverse = stats.maxReverseSpeedKmh / 3.6f;
            float motor = 0f;
            float brake = input.Brake;

            if (input.Reverse > 0.01f && input.Throttle < 0.01f)
            {
                if (fwd > 1f) brake = Mathf.Max(brake, input.Reverse);               // still rolling forward: brake first
                else if (-fwd < maxReverse) motor = -input.Reverse * stats.maxMotorTorque * stats.reverseTorqueFactor * performance;
            }
            else if (input.Throttle > 0.01f)
            {
                if (fwd < -1f) brake = Mathf.Max(brake, input.Throttle);             // still rolling backward: brake first
                else motor = input.Throttle * stats.maxMotorTorque * stats.torqueBySpeed.Evaluate(Mathf.Clamp01(fwd / maxSpeed)) * performance;
            }

            if (brake > 0.01f) motor = 0f;

            bool noInput = motor == 0f && brake < 0.01f && !input.Handbrake;
            float brakeTorque = brake * stats.maxBrakeTorque;
            if (noInput) brakeTorque = speed < 1.5f ? stats.idleBrakeTorque : stats.coastBrakeTorque;

            ApplyDrive(motor, brakeTorque, input.Handbrake);
            float lateral = Vector3.Dot(velocity, transform.right);
            slipDeg = speed > 3f ? Mathf.Atan2(lateral, Mathf.Abs(fwd)) * Mathf.Rad2Deg : 0f;
            UpdateDrift(input, speed, fwd, dt);
            ApplySteering(input.Steer, speed, fwd, dt);

            // ---- stability layer
            groundedWheels = 0;
            ApplyAntiRoll(frontLeft, frontRight, stats.antiRollFront);
            ApplyAntiRoll(rearLeft, rearRight, stats.antiRollRear);
            if (stats.downforce > 0f && groundedWheels > 0) body.AddForce(-transform.up * (stats.downforce * speed * speed));
            ApplyYawAssist(fwd);
            ApplyUprightAssist(dt, speed);

            UpdateGear(fwd, noInput);
        }

        // ------------------------------------------------------------------------------------ drive / steer

        private void ApplyDrive(float motorTorque, float brakeTorque, bool handbrake)
        {
            bool frontDriven = stats.drivenWheels != DriveLayout.Rear;
            bool rearDriven = stats.drivenWheels != DriveLayout.Front;
            float perWheel = motorTorque / ((frontDriven ? 2 : 0) + (rearDriven ? 2 : 0));

            frontLeft.motorTorque = frontRight.motorTorque = frontDriven ? perWheel : 0f;
            rearLeft.motorTorque = rearRight.motorTorque = rearDriven ? perWheel : 0f;

            float front = brakeTorque * 2f * stats.frontBrakeBias;
            float rear = brakeTorque * 2f * (1f - stats.frontBrakeBias) + (handbrake ? stats.handbrakeTorque : 0f);
            frontLeft.brakeTorque = frontRight.brakeTorque = front;
            rearLeft.brakeTorque = rearRight.brakeTorque = rear;
        }

        // Rear sideways grip: the car's own ratio, lowered while a slide is being induced. Only touches the WheelColliders when it changed.
        private void SetRearGrip(float mul)
        {
            if (rearLeft == null || rearRight == null || stats == null) return;
            if (Mathf.Abs(mul - appliedRearMul) < 0.01f) return;
            appliedRearMul = mul;
            var f = sideBase; f.stiffness = sideBase.stiffness * stats.rearGripRatio * mul;
            rearLeft.sidewaysFriction = f; rearRight.sidewaysFriction = f;
        }

        private void UpdateDrift(in VehicleInput input, float speed, float fwd, float dt)
        {
            float kmh = SpeedKmh; bool rearDriven = stats.drivenWheels != DriveLayout.Front;
            float intent = 0f;
            if (fwd > 2f)
            {
                if (input.Handbrake && kmh > 22f) intent = 1f;                                                          // handbrake turn: the classic way in
                else if (input.Brake > 0.4f && Mathf.Abs(input.Steer) > 0.45f && kmh > 50f) intent = Mathf.Clamp01((kmh - 50f) / 25f);     // brake into a turn at speed: the touch-screen way to start a drift
                else if (rearDriven && input.Throttle > 0.8f && Mathf.Abs(input.Steer) > 0.62f && kmh > 45f)           // power slide: full throttle + a hard turn
                    intent = stats.powerOversteer * Mathf.Clamp01((kmh - 45f) / 55f);
            }
            driftIntent = Mathf.MoveTowards(driftIntent, intent, dt * (intent > driftIntent ? 7f : 1.4f));
            rearMul = Mathf.MoveTowards(rearMul, Mathf.Lerp(1f, stats.driftGripDrop, driftIntent), dt * 4f);           // grip returns gradually, so the tail never snaps back
            SetRearGrip(rearMul);

            float raw = Mathf.Clamp01((Mathf.Abs(slipDeg) - 6f) / 24f) * Mathf.Clamp01((kmh - 22f) / 40f);
            driftAmount = Mathf.MoveTowards(driftAmount, raw, dt * (raw > driftAmount ? 5f : 2.5f));
            body.angularDamping = stats.angularDamping + 0.9f * driftAmount;

            if (driftAmount > 0.1f && input.Throttle > 0.1f && groundedWheels >= 2)                                    // keep the slide alive and the speed up
                body.AddForce(transform.forward * (driftAmount * input.Throttle * stats.driftSustain * body.mass * 1.6f));
            float over = Mathf.Abs(slipDeg) - stats.maxDriftAngle;                                                     // never beyond the car's drift angle: the nose is pulled back round
            if (over > 0f && speed > 4f) body.AddTorque(Vector3.up * (Mathf.Sign(slipDeg) * over * 0.3f), ForceMode.Acceleration);
            float yaw = body.angularVelocity.y, yawLimit = 2.1f;
            if (Mathf.Abs(yaw) > yawLimit) body.AddTorque(Vector3.up * (-(Mathf.Abs(yaw) - yawLimit) * Mathf.Sign(yaw) * 9f), ForceMode.Acceleration);   // no spin-outs

            // score: points per second of slide, scaled by speed and angle; banks when the slide ends
            if (driftAmount > 0.28f) { driftScoreRunning += dt * kmh * driftAmount * 0.55f; driftClock = 0.7f; }
            else if (driftClock > 0f)
            {
                driftClock -= dt;
                if (driftClock <= 0f) { if (driftScoreRunning >= 40f) { LastDriftScore = Mathf.RoundToInt(driftScoreRunning); LastDriftTime = Time.time; } driftScoreRunning = 0f; }
            }
        }

        private void ApplySteering(float steerInput, float speed, float fwd, float dt)
        {
            // Largest wheel angle whose bicycle-model lateral acceleration (v^2 * tan(angle) / wheelbase) stays within the grip limit.
            float gripAngle = Mathf.Atan(wheelbase * stats.steerGripLimit / Mathf.Max(speed * speed, 1f)) * Mathf.Rad2Deg;
            float maxAngle = Mathf.Clamp(gripAngle, stats.minSteerAngle, stats.maxSteerAngleLow);
            maxAngle = Mathf.Max(maxAngle, Mathf.Max(driftAmount, driftIntent) * stats.maxSteerAngleLow * 0.8f);      // starting or holding a slide needs more lock than grip driving
            float shaped = Mathf.Sign(steerInput) * Mathf.Pow(Mathf.Abs(Mathf.Clamp(steerInput, -1f, 1f)), stats.steerCurve);
            float target = shaped * maxAngle;
            float rate = stats.steerRate * stats.steerResponse;

            if (driftAmount > 0.02f && fwd > 2f && stats.counterSteer > 0f)
            {
                // counter-steer help: point the wheels along the real direction of travel (slip angle), keeping half of what the player asks for
                float counter = Mathf.Clamp(slipDeg, -stats.maxSteerAngleLow, stats.maxSteerAngleLow);
                float w = stats.counterSteer * Mathf.Clamp01(driftAmount * 1.4f);
                target = Mathf.Clamp(Mathf.Lerp(target, counter + target * 0.5f, w), -stats.maxSteerAngleLow, stats.maxSteerAngleLow);
                rate *= 1.8f;
            }

            bool returning = Mathf.Abs(target) < Mathf.Abs(steerAngle) || Mathf.Sign(target) != Mathf.Sign(steerAngle);
            if (returning) rate = Mathf.Max(rate, stats.steerReturnRate * stats.steerResponse);
            steerAngle = Mathf.MoveTowards(steerAngle, target, rate * dt);

            frontLeft.steerAngle = steerAngle;
            frontRight.steerAngle = steerAngle;
        }

        // ------------------------------------------------------------------------------------ stability

        private void ApplyAntiRoll(WheelCollider left, WheelCollider right, float stiffness)
        {
            float travelL = 1f, travelR = 1f;
            bool groundedL = left.GetGroundHit(out WheelHit hitL);
            bool groundedR = right.GetGroundHit(out WheelHit hitR);
            if (groundedL) { travelL = (-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / left.suspensionDistance; groundedWheels++; }
            if (groundedR) { travelR = (-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / right.suspensionDistance; groundedWheels++; }

            float force = (travelL - travelR) * stiffness;
            if (groundedL) body.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (groundedR) body.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        private void ApplyYawAssist(float forwardSpeed)
        {
            if (stats.yawAssist <= 0f || groundedWheels < 3 || Mathf.Abs(forwardSpeed) < 3f) return;

            // Yaw rate the steering angle asks for (bicycle model) vs the yaw rate we actually have.
            float assist = stats.yawAssist * (1f - Mathf.Clamp01(driftAmount * 1.2f + driftIntent * 0.9f));     // let the car rotate while it is sliding
            if (assist <= 0.01f) return;
            float desired = forwardSpeed * Mathf.Tan(steerAngle * Mathf.Deg2Rad) / wheelbase;
            float limit = stats.steerGripLimit * 1.1f / Mathf.Abs(forwardSpeed); // never ask for more yaw than the tyres can hold
            desired = Mathf.Clamp(desired, -limit, limit);
            float actual = body.angularVelocity.y;
            body.AddTorque(Vector3.up * ((desired - actual) * assist * stats.yawAssistTorque), ForceMode.Force);
        }

        private void ApplyUprightAssist(float dt, float speed)
        {
            Vector3 up = transform.up;
            TiltAngle = Vector3.Angle(up, Vector3.up);

            if (TiltAngle > stats.uprightAssistStartAngle)
            {
                Vector3 axis = Vector3.Cross(up, Vector3.up);
                float strength = Mathf.Clamp01((TiltAngle - stats.uprightAssistStartAngle) / 60f);
                body.AddTorque(axis.normalized * (stats.uprightTorque * strength), ForceMode.Force);
            }

            bool flipped = TiltAngle > 100f || (TiltAngle > 65f && speed < 2f);
            flippedTimer = flipped ? flippedTimer + dt : 0f;
            if (stats.autoRecoverDelay > 0f && flippedTimer >= stats.autoRecoverDelay) Recover();
        }

        /// <summary>Puts the car back on its wheels at its current spot (keeps heading, clears velocity).</summary>
        public void Recover()
        {
            flippedTimer = 0f;
            float yaw = transform.eulerAngles.y;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position += Vector3.up * 1.2f;
            body.rotation = Quaternion.Euler(0f, yaw, 0f);
            transform.SetPositionAndRotation(body.position, body.rotation);
            steerAngle = 0f;
        }

        /// <summary>Teleports the car (used by the spawner) and clears all motion.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            steerAngle = 0f;
            flippedTimer = 0f;
        }

        // ------------------------------------------------------------------------------------ gearbox display

        private void UpdateGear(float forwardSpeed, bool noInput)
        {
            float kmh = SpeedKmh;
            if (forwardSpeed < -0.5f) { gear = -1; return; }
            if (kmh < 0.8f && noInput) { gear = 0; return; }

            float[] up = stats.upshiftSpeedsKmh;
            int maxGear = up.Length + 1;
            if (gear < 1) gear = 1;
            while (gear < maxGear && kmh > up[gear - 1] + stats.shiftHysteresisKmh) gear++;
            while (gear > 1 && kmh < up[gear - 2] - stats.shiftHysteresisKmh) gear--;
        }
    }
}
