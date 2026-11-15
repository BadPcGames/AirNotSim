using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneWindReceiver : MonoBehaviour
{
    public enum AerodynamicMode
    {
        FullRelativeAirflow, 
        WindPushOnly        
    }

    [Header("Режим интеграции с физикой")]
    public AerodynamicMode mode = AerodynamicMode.FullRelativeAirflow;

    [Header("Геометрия дрона (площадь проекции в м²)")]
    [Tooltip("Площадь профиля спереди/сзади (X/Z), сбоку и сверху/снизу (Y)")]
    public Vector3 crossSectionArea = new Vector3(0.06f, 0.14f, 0.06f);
    [Tooltip("Коэффициенты аэродинамического сопротивления Cd по локальным осям")]
    public Vector3 dragCoefficients = new Vector3(1.1f, 1.3f, 1.1f);

    [Header("Вращательные возмущения (Момент)")]
    [Range(0f, 1f), Tooltip("Смещение центра давления относительно центра масс (создаёт занос/крен при боковом ветре)")]
    public float torqueSensitivity = 0.15f;
    [Tooltip("Вибрация/болтанка углов дрона от мелких вихрей турбулентности")]
    public float turbulenceTorqueScale = 0.04f;

    [Header("Телеметрия (Read Only)")]
    [SerializeField] private Vector3 currentWorldWind;
    [SerializeField] private Vector3 currentAerodynamicForce;

    public Vector3 CurrentWorldWind => currentWorldWind;
    public float CurrentWindSpeed => currentWorldWind.magnitude;

    private Rigidbody rb;
    private float droneSeed;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        droneSeed = Random.Range(0f, 500f);
    }

    private void FixedUpdate()
    {
        Vector3 totalWind = Vector3.zero;
        float totalTurbulence = 0f;

        var zones = WindFieldManager.ActiveZones;
        for (int i = 0; i < zones.Count; i++)
        {
            totalWind += zones[i].SampleWindAtPosition(transform.position, out float turb);
            totalTurbulence += turb;
        }

        currentWorldWind = totalWind;

        float airDensity = 1.225f * Mathf.Exp(-Mathf.Max(0f, transform.position.y) / 8500f);

        Vector3 droneVelocity = GetRigidbodyVelocity();
        Vector3 relativeAirVelocity = (mode == AerodynamicMode.FullRelativeAirflow)
            ? (totalWind - droneVelocity)
            : totalWind;

        Vector3 localAirVel = transform.InverseTransformDirection(relativeAirVelocity);

        float fx = 0.5f * airDensity * dragCoefficients.x * crossSectionArea.x * Mathf.Abs(localAirVel.x) * localAirVel.x;
        float fy = 0.5f * airDensity * dragCoefficients.y * crossSectionArea.y * Mathf.Abs(localAirVel.y) * localAirVel.y;
        float fz = 0.5f * airDensity * dragCoefficients.z * crossSectionArea.z * Mathf.Abs(localAirVel.z) * localAirVel.z;

        Vector3 localForce = new Vector3(fx, fy, fz);
        currentAerodynamicForce = transform.TransformDirection(localForce);

        rb.AddForce(currentAerodynamicForce, ForceMode.Force);

        if (torqueSensitivity > 0f && relativeAirVelocity.sqrMagnitude > 0.01f)
        {
            Vector3 centerOfPressureOffset = new Vector3(0f, 0.05f, 0f);
            Vector3 aeroTorque = Vector3.Cross(centerOfPressureOffset, localForce) * torqueSensitivity * 10f;
            rb.AddRelativeTorque(aeroTorque, ForceMode.Force);
        }

        if (totalTurbulence > 0.01f && turbulenceTorqueScale > 0f)
        {
            float t = Time.fixedTime * 3.5f + droneSeed;
            Vector3 randomTorque = new Vector3(
                (Mathf.PerlinNoise(t, 10f) - 0.5f) * 2f,
                (Mathf.PerlinNoise(t, 50f) - 0.5f) * 1.2f,
                (Mathf.PerlinNoise(t, 90f) - 0.5f) * 2f
            ) * (totalTurbulence * turbulenceTorqueScale);

            rb.AddRelativeTorque(randomTorque, ForceMode.Force);
        }
    }

    private Vector3 GetRigidbodyVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, currentWorldWind * 0.5f);
        Gizmos.color = Color.red;
        Gizmos.DrawRay(transform.position, currentAerodynamicForce * 0.2f);
    }
}