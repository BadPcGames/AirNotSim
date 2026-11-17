using System.Collections.Generic;
using UnityEngine;

public class DronePhysics : MonoBehaviour
{
    [System.Serializable]
    public class Motor
    {
        public Transform motorTransform;
        public float rotationDirection;

        [Header("Propeller Specs")]
        public float diameterInches = 5.0f;
        public float pitchInches = 4.3f;

        [Header("Damage Simulation")]
        public int initialBladeCount = 3;
        [Tooltip("Масса одной лопасти в килограммах (например, 0.003 = 3 грамма)")]
        public float singleBladeMass = 0.003f;
        [Tooltip("Расстояние от оси мотора до центра масс одной лопасти в метрах")]
        public float bladeCoMRadius = 0.025f;

        public List<GameObject> bladesVisual = new List<GameObject>();
        [HideInInspector] public bool[] activeBlades;

        [Header("Visuals & State")]
        public Transform propellerTransform;
        [HideInInspector] public float currentRPM;

        public void InitializeBlades()
        {
            activeBlades = new bool[initialBladeCount];
            for (int i = 0; i < initialBladeCount; i++) activeBlades[i] = true;
        }

        public bool BreakBlade(int index)
        {
            if (index >= 0 && index < activeBlades.Length && activeBlades[index])
            {
                activeBlades[index] = false;
                if (index < bladesVisual.Count && bladesVisual[index] != null)
                {
                    bladesVisual[index].SetActive(false);
                }
                return true;
            }
            return false;
        }
    }

    [System.Serializable]
    public class AeroBlock
    {
        public Transform blockTransform;
        public Vector3 dimensions = new Vector3(0.1f, 0.05f, 0.1f);
        public float dragCoefficient = 1.0f;
        public float liftCoefficient = 0.2f;
        public bool showDebug = true;
    }

    public List<Motor> motors;
    public List<AeroBlock> aeroBlocks;
    public Rigidbody rb;
    public Transform CM;

    [Header("Environment")]
    public float airDensity = 1.225f;

    [Header("Propeller Aerodynamics")]
    public float baseCt = 0.025f;
    public float baseCq = 0.0015f;

    [Header("Damage & Vibration Tuning")]
    [Tooltip("Множитель силы вибрации. Используйте для настройки, чтобы физика Unity не сошла с ума (от 0 до 1)")]
    [Range(0f, 1f)] public float vibrationMultiplier = 0.5f;

    [Header("Ground Effect Settings")]
    public bool enableGroundEffect = true;
    public LayerMask groundLayer = ~0;
    public float groundEffectMaxMultiplier = 1.5f;

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (CM != null) rb.centerOfMass = transform.InverseTransformPoint(CM.position);

        foreach (var motor in motors)
        {
            motor.InitializeBlades();
        }
    }

    void FixedUpdate()
    {
        if (AirPresureManager.instance != null)
            airDensity = AirPresureManager.instance.GetAirDensityForDrone(transform.position.y);

        ApplyPropulsionPhysics();
        ApplyBodyAerodynamics();
    }

    private void Update()
    {
        foreach (Motor motor in motors)
        {
            if (motor.propellerTransform != null)
            {
                float degreesPerSecond = motor.currentRPM * 6f;
                float rotationStep = degreesPerSecond * motor.rotationDirection * Time.deltaTime;
                motor.propellerTransform.Rotate(Vector3.up, rotationStep, Space.Self);
            }
        }
    }

    private void BreakRandomBladeOnMotor(int motorIndex)
    {
        Motor m = motors[motorIndex];
        for (int i = 0; i < m.activeBlades.Length; i++)
        {
            if (m.BreakBlade(i))
            {
                Debug.Log($"Отломана лопасть {i} на моторе {motorIndex}!");
                break;
            }
        }
    }

    void ApplyPropulsionPhysics()
    {
        foreach (Motor motor in motors)
        {
            if (motor.motorTransform == null || motor.currentRPM <= 0f) continue;

            int activeBladeCount = 0;
            Vector2 imbalanceVector = Vector2.zero;

            float angleStep = 360f / motor.initialBladeCount;

            for (int i = 0; i < motor.initialBladeCount; i++)
            {
                if (motor.activeBlades[i])
                {
                    activeBladeCount++;
                    float angleRad = (i * angleStep) * Mathf.Deg2Rad;
                    imbalanceVector += new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
                }
            }

            if (activeBladeCount == 0) continue;

            Vector3 motorPos = motor.motorTransform.position;
            Vector3 motorUp = motor.motorTransform.up;
            Vector3 motorWorldVel = rb.GetPointVelocity(motorPos);

            // Обороты винта относительно воздуха с учётом вращения корпуса вокруг оси мотора
            float bodyOmegaAlongAxis = Vector3.Dot(rb.angularVelocity, motorUp);
            float bodyRPSAlongAxis = bodyOmegaAlongAxis / (2f * Mathf.PI);
            float signedMotorRPS = (motor.currentRPM / 60f) * motor.rotationDirection;
            float airRelativeSignedRPS = signedMotorRPS + bodyRPSAlongAxis;
            float airRelativeRPS = Mathf.Abs(airRelativeSignedRPS);
            float rpsSquared = airRelativeRPS * airRelativeRPS;

            float diameterMeters = motor.diameterInches * 0.0254f;
            float radiusMeters = diameterMeters * 0.5f;
            float pitchMeters = motor.pitchInches * 0.0254f;
            float pitchToDiameterRatio = pitchMeters / diameterMeters;

            float Ct = baseCt * activeBladeCount * pitchToDiameterRatio;
            float Cq = baseCq * activeBladeCount * pitchToDiameterRatio;

            float d4 = Mathf.Pow(diameterMeters, 4);
            float thrustForce = Ct * airDensity * rpsSquared * d4;

            // Влияние осевой скорости потока на тягу (Advance Ratio)
            float axialVel = Vector3.Dot(motorWorldVel, motorUp);
            float pitchSpeed = Mathf.Max(0.1f, airRelativeRPS * pitchMeters);
            float advanceFactor = Mathf.Clamp(1f - (axialVel / pitchSpeed), 0f, 1.5f);
            thrustForce *= advanceFactor;

            // Экранный эффект с игнорированием собственного коллайдера дрона
            if (enableGroundEffect)
            {
                float rayDistance = diameterMeters * 3f;
                if (Physics.Raycast(motorPos, -motorUp, out RaycastHit hit, rayDistance, groundLayer, QueryTriggerInteraction.Ignore))
                {
                    if (hit.rigidbody != rb)
                    {
                        float h = hit.distance;
                        if (h < diameterMeters * groundEffectMaxMultiplier && h > 0.01f)
                        {
                            float ratio = Mathf.Clamp(radiusMeters / (4f * h), 0f, 0.8f);
                            float groundEffectFactor = Mathf.Clamp(1f / (1f - (ratio * ratio)), 1f, 1.4f);
                            thrustForce *= groundEffectFactor;
                        }
                    }
                }
            }

            Vector3 forceVector = motorUp * thrustForce;
            rb.AddForceAtPosition(forceVector, motorPos, ForceMode.Force);

            // Реактивный момент вдоль оси данного мотора
            float d5 = Mathf.Pow(diameterMeters, 5);
            float torqueMagnitude = Cq * airDensity * rpsSquared * d5;
            float torqueSign = airRelativeSignedRPS >= 0f ? -1f : 1f;
            rb.AddTorque(motorUp * (torqueMagnitude * torqueSign), ForceMode.Force);

            // Профильная боковая сила винта (Profile H-force)
            float omegaRad = airRelativeRPS * 2f * Mathf.PI;
            if (omegaRad > 1f)
            {
                Vector3 lateralVel = motorWorldVel - motorUp * axialVel;
                float hForceFactor = (2f * torqueMagnitude) / (omegaRad * radiusMeters * radiusMeters);
                Vector3 hForce = -lateralVel * hForceFactor;
                rb.AddForceAtPosition(hForce, motorPos, ForceMode.Force);
            }

            // Симуляция вибрации при повреждении лопастей
            float imbalanceMagnitude = imbalanceVector.magnitude;

            if (imbalanceMagnitude > 0.01f)
            {
                float normalizedRPM = Mathf.Clamp01(motor.currentRPM / 30000f);

                float maxSafeVibrationForce = 15f * rb.mass;

                float currentVibrationForce = imbalanceMagnitude * maxSafeVibrationForce * (normalizedRPM * normalizedRPM);
                currentVibrationForce *= vibrationMultiplier;

                float simulatedFrequency = normalizedRPM * 50f;
                float currentAngleRad = simulatedFrequency * Time.fixedTime * Mathf.PI * 2f;

                float imbalancePhase = Mathf.Atan2(imbalanceVector.y, imbalanceVector.x);
                float finalAngle = currentAngleRad + imbalancePhase;

                Vector3 localVibrationDir = new Vector3(Mathf.Cos(finalAngle), 0, Mathf.Sin(finalAngle));

                Vector3 vibrationForceVector = motor.motorTransform.TransformDirection(localVibrationDir) * currentVibrationForce;

                rb.AddForceAtPosition(vibrationForceVector, motorPos, ForceMode.Force);
            }
        }
    }

    void ApplyBodyAerodynamics()
    {
        foreach (AeroBlock block in aeroBlocks)
        {
            if (block.blockTransform == null) continue;

            Vector3 quarterExtents = block.dimensions * 0.25f;
            float areaX = block.dimensions.y * block.dimensions.z;
            float areaY = block.dimensions.x * block.dimensions.z;
            float areaZ = block.dimensions.x * block.dimensions.y;

            // Разбиваем блок на 8 октантов для расчёта как линейного, так и вращательного сопротивления
            for (int ix = -1; ix <= 1; ix += 2)
            {
                for (int iy = -1; iy <= 1; iy += 2)
                {
                    for (int iz = -1; iz <= 1; iz += 2)
                    {
                        Vector3 localPoint = new Vector3(ix * quarterExtents.x, iy * quarterExtents.y, iz * quarterExtents.z);
                        Vector3 worldPoint = block.blockTransform.TransformPoint(localPoint);

                        Vector3 worldVelocity = rb.GetPointVelocity(worldPoint);
                        float speed = Mathf.Clamp(worldVelocity.magnitude, 0f, 40f);

                        if (speed < 0.001f) continue;

                        Vector3 airflowDir = -worldVelocity / speed;
                        Vector3 localAirflowDir = block.blockTransform.InverseTransformDirection(airflowDir);

                        float projectedArea = (areaX * Mathf.Abs(localAirflowDir.x) +
                                               areaY * Mathf.Abs(localAirflowDir.y) +
                                               areaZ * Mathf.Abs(localAirflowDir.z)) * 0.125f;

                        float dynamicPressure = 0.5f * airDensity * speed * speed;
                        float dragForce = dynamicPressure * block.dragCoefficient * projectedArea;

                        rb.AddForceAtPosition(airflowDir * dragForce, worldPoint, ForceMode.Force);
                    }
                }
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (aeroBlocks == null) return;
        foreach (AeroBlock block in aeroBlocks)
        {
            if (block.blockTransform != null && block.showDebug)
            {
                Gizmos.color = new Color(0, 1, 1, 0.3f);
                Matrix4x4 oldMatrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(block.blockTransform.position, block.blockTransform.rotation, Vector3.one);
                Gizmos.DrawCube(Vector3.zero, block.dimensions);
                Gizmos.color = new Color(0, 1, 1, 1f);
                Gizmos.DrawWireCube(Vector3.zero, block.dimensions);
                Gizmos.matrix = oldMatrix;
            }
        }
    }
}