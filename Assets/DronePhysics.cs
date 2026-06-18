using System.Collections.Generic;
using UnityEngine;

public class DronePhysics : MonoBehaviour
{
    [System.Serializable]
    public class Motor
    {
        public Transform motorTransform; // Точка (пустышка), где находится пропеллер
        public float rotationDirection;  // 1 для по часовой (CW), -1 для против (CCW)

        [Header("Visuals")]
        public Transform propellerTransform;

        [HideInInspector]
        public float currentRPM;         // Текущая скорость вращения мотора
    }

    public float goalRPM;

    public List<Motor> motors;
    public Rigidbody rb;

    [Header("Physics Constants")]
    public float thrustCoefficient = 0.01f; // Тот самый k_f
    public float torqueCoefficient = 0.002f; // Тот самый k_t

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();

        // ВАЖНО: У дронов стандартная схема:
        // Передний левый и задний правый крутятся по часовой (1)
        // Передний правый и задний левый крутятся против (-1)
    }

    // Вся физика ДОЛЖНА считаться в FixedUpdate
    void FixedUpdate()
    {
        ApplyDronePhysics();
    }


    private void Update()
    {
        foreach (Motor motor in motors)
        {
            motor.currentRPM = goalRPM;

             if (motor.propellerTransform != null)
            {
                // Переводим RPM (обороты в минуту) в Градусы в секунду.
                // 1 оборот = 360 градусов. 1 минута = 60 секунд.
                // Скорость (град/сек) = RPM * (360 / 60) = RPM * 6.
                float degreesPerSecond = motor.currentRPM * 6f;

                // Вычисляем угол поворота для текущего кадра
                // Умножаем на направление вращения (1 или -1)
                float rotationStep = degreesPerSecond * motor.rotationDirection * Time.deltaTime;

                // Вращаем 3D-модель вокруг её локальной оси Y. 
                // ВАЖНО: Если у вашей 3D модели пропеллера ось вращения это Z или X, 
                // поменяйте Vector3.up на Vector3.forward или Vector3.right
                motor.propellerTransform.Rotate(Vector3.up, rotationStep, Space.Self);
            }
        }


    }

    void ApplyDronePhysics()
    {
        foreach (Motor motor in motors)
        {
            // Берем квадрат скорости вращения (w^2)
            float rpmSquared = motor.currentRPM * motor.currentRPM;

            // 1. ПОДЪЕМНАЯ СИЛА (THRUST)
            // Формула: F = k_f * w^2
            float thrustForce = thrustCoefficient * rpmSquared;

            // Вектор силы направлен вверх относительно самого мотора (или дрона)
            Vector3 forceVector = motor.motorTransform.up * thrustForce;

            // Применяем силу В ТОЧКЕ НАХОЖДЕНИЯ МОТОРА. 
            // Это автоматически создаст Pitch и Roll, если моторы крутятся с разной скоростью.
            rb.AddForceAtPosition(forceVector, motor.motorTransform.position, ForceMode.Force);

            // 2. РЕАКТИВНЫЙ МОМЕНТ (YAW TORQUE)
            // Формула: T = k_t * w^2 * Направление вращения
            // Заметьте: момент направлен ПРОТИВОПОЛОЖНО вращению (поэтому умножаем на -1)
            float torqueForce = torqueCoefficient * rpmSquared * motor.rotationDirection * -1f;

            // Применяем крутящий момент вокруг локальной оси Y дрона
            Vector3 torqueVector = transform.up * torqueForce;

            // AddTorque действует на весь объект (центр масс)
            rb.AddTorque(torqueVector, ForceMode.Force);
        }
    }
}
