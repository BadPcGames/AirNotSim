using System.Collections.Generic;
using UnityEngine;

public class DronePhysics : MonoBehaviour
{
    [System.Serializable]
    public class Motor
    {
        public Transform motorTransform;
        public float rotationDirection; // 1 (CW), -1 (CCW)

        [Header("Propeller Specs")]
        public float diameterInches = 5.0f;
        public float pitchInches = 4.3f;
        public int bladeCount = 3;

        [Header("Visuals & State")]
        public Transform propellerTransform;
        [HideInInspector] public float currentRPM;
    }

    [System.Serializable]
    public class AeroBlock
    {
        [Tooltip("Пустой GameObject, определяющий центр и поворот блока")]
        public Transform blockTransform;

        [Tooltip("Размеры невидимого блока в метрах (X - ширина, Y - высота, Z - длина)")]
        public Vector3 dimensions = new Vector3(0.1f, 0.05f, 0.1f);

        [Tooltip("Коэффициент лобового сопротивления (Drag). Плоская пластина ~1.0, куб ~1.05, обтекаемый ~0.3")]
        public float dragCoefficient = 1.0f;

        [Tooltip("Коэффициент подъёмной силы (Lift). Возникает, когда поток бьёт в днище под углом.")]
        public float liftCoefficient = 0.2f;

        [Tooltip("Показывать рамку блока в редакторе")]
        public bool showDebug = true;
    }

    public List<Motor> motors;

    [Header("Aerodynamics (Body & Arms)")]
    public List<AeroBlock> aeroBlocks;

    public Rigidbody rb;
    public Transform CM;

    [Header("Environment")]
    public float airDensity = 1.225f;

    [Header("Propeller Aerodynamics")]
    public float baseCt = 0.025f;
    public float baseCq = 0.0015f;

    [Header("Ground Effect Settings")]
    public bool enableGroundEffect = true;
    public LayerMask groundLayer = ~0;
    public float groundEffectMaxMultiplier = 1.5f;

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.centerOfMass = CM.localPosition;
    }

    void FixedUpdate()
    {
        if (AirPresureManager.instance != null)
        {
            airDensity = AirPresureManager.instance.GetAirDensityForDrone(transform.position.y);
        }

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

    void ApplyPropulsionPhysics()
    {
        foreach (Motor motor in motors)
        {
            if (motor.currentRPM <= 0) continue;

            float diameterMeters = motor.diameterInches * 0.0254f;
            float radiusMeters = diameterMeters * 0.5f;
            float pitchMeters = motor.pitchInches * 0.0254f;
            float rps = motor.currentRPM / 60f;
            float rpsSquared = rps * rps;

            float pitchToDiameterRatio = pitchMeters / diameterMeters;
            float Ct = baseCt * motor.bladeCount * pitchToDiameterRatio;
            float Cq = baseCq * motor.bladeCount * pitchToDiameterRatio;

            float d4 = Mathf.Pow(diameterMeters, 4);
            float thrustForce = Ct * airDensity * rpsSquared * d4;

            if (enableGroundEffect)
            {
                RaycastHit hit;
                float rayDistance = diameterMeters * 3f;

                if (Physics.Raycast(motor.motorTransform.position, -motor.motorTransform.up, out hit, rayDistance, groundLayer))
                {
                    float h = hit.distance;
                    if (h < diameterMeters * groundEffectMaxMultiplier && h > 0.01f)
                    {
                        float ratio = radiusMeters / (4f * h);
                        ratio = Mathf.Clamp(ratio, 0f, 0.8f);
                        float groundEffectFactor = 1f / (1f - (ratio * ratio));
                        groundEffectFactor = Mathf.Clamp(groundEffectFactor, 1f, 1.4f);
                        thrustForce *= groundEffectFactor;
                    }
                }
            }

            Vector3 forceVector = motor.motorTransform.up * thrustForce;
            rb.AddForceAtPosition(forceVector, motor.motorTransform.position, ForceMode.Force);

            float d5 = Mathf.Pow(diameterMeters, 5);
            float torqueForce = Cq * airDensity * rpsSquared * d5;
            torqueForce *= (motor.rotationDirection * -1f);

            Vector3 torqueVector = transform.up * torqueForce;
            rb.AddTorque(torqueVector, ForceMode.Force);
        }
    }

    void ApplyBodyAerodynamics()
    {
        foreach (AeroBlock block in aeroBlocks)
        {
            if (block.blockTransform == null) continue;

            Vector3 worldVelocity = rb.GetPointVelocity(block.blockTransform.position);
            float speed = worldVelocity.magnitude;

            if (speed < 0.1f) continue;

            Vector3 airflowDir = -worldVelocity / speed;

            Vector3 localAirflowDir = block.blockTransform.InverseTransformDirection(airflowDir);

            float areaX = block.dimensions.y * block.dimensions.z; 
            float areaY = block.dimensions.x * block.dimensions.z; 
            float areaZ = block.dimensions.x * block.dimensions.y;

            float projectedArea = areaX * Mathf.Abs(localAirflowDir.x) +
                                  areaY * Mathf.Abs(localAirflowDir.y) +
                                  areaZ * Mathf.Abs(localAirflowDir.z);

            float dynamicPressure = 0.5f * airDensity * speed * speed;

            float dragForce = dynamicPressure * block.dragCoefficient * projectedArea;
            Vector3 dragVector = airflowDir * dragForce; 

            rb.AddForceAtPosition(dragVector, block.blockTransform.position, ForceMode.Force);

            if (block.liftCoefficient > 0)
            {
                float angleOfAttackEffect = localAirflowDir.y;

                if (angleOfAttackEffect > 0.05f) 
                {
                    float liftForce = dynamicPressure * block.liftCoefficient * projectedArea * angleOfAttackEffect;
                    Vector3 liftVector = block.blockTransform.up * liftForce;
                    rb.AddForceAtPosition(liftVector, block.blockTransform.position, ForceMode.Force);
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