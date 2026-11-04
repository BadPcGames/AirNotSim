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

            // --- РАСЧЕТ ЭФФЕКТА ЗЕМЛИ ---
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
            // ----------------------------

            Vector3 forceVector = motor.motorTransform.up * thrustForce;
            rb.AddForceAtPosition(forceVector, motor.motorTransform.position, ForceMode.Force);

            float d5 = Mathf.Pow(diameterMeters, 5);
            float torqueForce = Cq * airDensity * rpsSquared * d5;
            torqueForce *= (motor.rotationDirection * -1f);

            Vector3 torqueVector = transform.up * torqueForce;
            rb.AddTorque(torqueVector, ForceMode.Force);
        }
    }

    // ==========================================
    // НОВАЯ ЛОГИКА: АЭРОДИНАМИКА КОРПУСА (AeroBlocks)
    // ==========================================
    void ApplyBodyAerodynamics()
    {
        foreach (AeroBlock block in aeroBlocks)
        {
            if (block.blockTransform == null) continue;

            // 1. Получаем скорость воздуха в точке этого блока
            Vector3 worldVelocity = rb.GetPointVelocity(block.blockTransform.position);
            float speed = worldVelocity.magnitude;

            // Если дрон стоит на месте, аэродинамики нет
            if (speed < 0.1f) continue;

            // Направление движения воздуха (относительно дрона ветер дует навстречу)
            Vector3 airflowDir = -worldVelocity / speed;

            // Переводим направление ветра в локальные координаты блока
            Vector3 localAirflowDir = block.blockTransform.InverseTransformDirection(airflowDir);

            // 2. Считаем эффективную площадь сечения (Проекция коробки на направление ветра)
            // Это гениально просто: мы умножаем площадь каждой грани на угол падения потока
            float areaX = block.dimensions.y * block.dimensions.z; // Площадь вида сбоку
            float areaY = block.dimensions.x * block.dimensions.z; // Площадь вида сверху/снизу
            float areaZ = block.dimensions.x * block.dimensions.y; // Площадь вида спереди/сзади

            float projectedArea = areaX * Mathf.Abs(localAirflowDir.x) +
                                  areaY * Mathf.Abs(localAirflowDir.y) +
                                  areaZ * Mathf.Abs(localAirflowDir.z);

            // Динамическое давление (Dynamic Pressure) = 0.5 * Плотность * Скорость^2
            float dynamicPressure = 0.5f * airDensity * speed * speed;

            // 3. Расчет силы лобового сопротивления (Drag)
            // F = Динам_давление * Cd * Площадь
            float dragForce = dynamicPressure * block.dragCoefficient * projectedArea;
            Vector3 dragVector = airflowDir * dragForce; // Сопротивление тянет дрон по ветру (назад)

            rb.AddForceAtPosition(dragVector, block.blockTransform.position, ForceMode.Force);

            // 4. Расчет подъемной силы корпуса (Lift)
            // Работает, когда ветер бьёт в нижнюю грань (например, когда дрон летит вперед с наклоном вниз)
            if (block.liftCoefficient > 0)
            {
                // Насколько сильно ветер бьёт снизу вверх (угол атаки днища)
                // Если localAirflowDir.y положительный — ветер дует снизу вверх относительно блока.
                float angleOfAttackEffect = localAirflowDir.y;

                if (angleOfAttackEffect > 0.05f) // Если ветер бьет в дно
                {
                    float liftForce = dynamicPressure * block.liftCoefficient * projectedArea * angleOfAttackEffect;

                    // Подъемная сила направлена локально "вверх" от плоскости блока
                    Vector3 liftVector = block.blockTransform.up * liftForce;
                    rb.AddForceAtPosition(liftVector, block.blockTransform.position, ForceMode.Force);
                }
            }
        }
    }

    // Рисуем наши блоки в редакторе Unity для удобной настройки
    private void OnDrawGizmos()
    {
        if (aeroBlocks == null) return;

        foreach (AeroBlock block in aeroBlocks)
        {
            if (block.blockTransform != null && block.showDebug)
            {
                Gizmos.color = new Color(0, 1, 1, 0.3f); // Полупрозрачный голубой цвет

                // Сохраняем старую матрицу и применяем матрицу блока (чтобы кубы поворачивались вместе с дроном)
                Matrix4x4 oldMatrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(block.blockTransform.position, block.blockTransform.rotation, Vector3.one);

                Gizmos.DrawCube(Vector3.zero, block.dimensions);

                Gizmos.color = new Color(0, 1, 1, 1f); // Яркие края
                Gizmos.DrawWireCube(Vector3.zero, block.dimensions);

                Gizmos.matrix = oldMatrix;
            }
        }
    }
}