using System.Collections.Generic;
using UnityEngine;

public class DronePhysics : MonoBehaviour
{
    [System.Serializable]
    public class Motor
    {
        public Transform motorTransform;
        public float rotationDirection; // 1 (CW), -1 (CCW)

        [Header("Propeller Specs (from Table)")]
        [Tooltip("Диаметр пропеллера в дюймах (например, 5)")]
        public float diameterInches = 5.0f;

        [Tooltip("Шаг пропеллера в дюймах (например, 4.3)")]
        public float pitchInches = 4.3f;

        [Tooltip("Количество лопастей (например, 3)")]
        public int bladeCount = 3;

        [Header("Visuals & State")]
        public Transform propellerTransform;
        [HideInInspector] public float currentRPM; // Текущие обороты (0 - 30000+)
    }

    public List<Motor> motors;
    public Rigidbody rb;

    [Header("Environment")]
    [Tooltip("Плотность воздуха в кг/м³. На уровне моря при 15°C равна 1.225")]
    public float airDensity = 1.225f;

    [Header("Aerodynamic Base Coefficients")]
    [Tooltip("Базовый коэффициент профиля для тяги. Настраивается для подгонки под реальность (обычно 0.02 - 0.05)")]
    public float baseCt = 0.025f;

    [Tooltip("Базовый коэффициент профиля для момента (обычно на порядок меньше тяги, 0.001 - 0.003)")]
    public float baseCq = 0.0015f;

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        ApplyDronePhysics();
    }

    private void Update()
    {
        // Визуал вращения (оставлен без изменений, но вынесен для чистоты)
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

    void ApplyDronePhysics()
    {
        foreach (Motor motor in motors)
        {
            if (motor.currentRPM <= 0) continue;

            // 1. Конвертация величин в систему СИ (Метры, Секунды)
            float diameterMeters = motor.diameterInches * 0.0254f;
            float pitchMeters = motor.pitchInches * 0.0254f;
            float rps = motor.currentRPM / 60f; // Revolutions Per Second (n)
            float rpsSquared = rps * rps;       // n^2

            // 2. Эмпирический расчет коэффициентов C_T и C_Q 
            // Чем больше шаг по отношению к диаметру и чем больше лопастей, тем выше коэффициенты.
            float pitchToDiameterRatio = pitchMeters / diameterMeters;

            // Грубая, но эффективная аппроксимация влияния геометрии винта:
            float Ct = baseCt * motor.bladeCount * pitchToDiameterRatio;
            float Cq = baseCq * motor.bladeCount * pitchToDiameterRatio;

            // 3. Расчет ТЯГИ (Thrust) по аэродинамической формуле: F = C_T * rho * n^2 * D^4
            float d4 = Mathf.Pow(diameterMeters, 4);
            float thrustForce = Ct * airDensity * rpsSquared * d4;

            Vector3 forceVector = motor.motorTransform.up * thrustForce;
            rb.AddForceAtPosition(forceVector, motor.motorTransform.position, ForceMode.Force);

            // 4. Расчет РЕАКТИВНОГО МОМЕНТА (Torque): T = C_Q * rho * n^2 * D^5
            // Обратите внимание на D^5 - момент сильнее зависит от диаметра, чем тяга!
            float d5 = Mathf.Pow(diameterMeters, 5);
            float torqueForce = Cq * airDensity * rpsSquared * d5;

            // Момент направлен противоположно вращению мотора
            torqueForce *= (motor.rotationDirection * -1f);

            Vector3 torqueVector = transform.up * torqueForce;
            rb.AddTorque(torqueVector, ForceMode.Force);
        }
    }
}