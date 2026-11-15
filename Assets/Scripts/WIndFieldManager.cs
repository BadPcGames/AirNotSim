using System.Collections.Generic;
using UnityEngine;

public class WindFieldManager : MonoBehaviour
{
    public static readonly List<WindFieldManager> ActiveZones = new List<WindFieldManager>();

    public enum WindAreaMode
    {
        Global,   
        LocalizedSphere,
        LocalizedBox   
    }

    [Header("Зона действия и положение")]
    public WindAreaMode areaMode = WindAreaMode.Global;
    [Tooltip("Радиус зоны (для LocalizedSphere)")]
    public float sphereRadius = 50f;
    [Tooltip("Размеры зоны (для LocalizedBox)")]
    public Vector3 boxSize = new Vector3(60f, 30f, 60f);
    [Range(0.01f, 1f), Tooltip("Доля границы с плавным затуханием ветра")]
    public float edgeFalloff = 0.25f;

    [Header("Направление и базовая сила")]
    [Tooltip("Если включено — направление ветра берется из оси +Z (Forward) этого объекта")]
    public bool useTransformDirection = true;
    [Range(0f, 360f)] public float azimuthAngle = 0f;
    [Range(-45f, 45f)] public float elevationAngle = 0f;
    [Range(0f, 40f), Tooltip("Базовая скорость ветра в м/с (на эталонной высоте)")]
    public float baseWindSpeed = 6f;

    [Header("Высотный градиент (пограничный слой)")]
    public bool useAltitudeProfile = true;
    [Tooltip("Высота уровня земли (Y)")]
    public float groundLevelY = 0f;
    [Tooltip("Эталонная высота (обычно 10 м), на которой измерена baseWindSpeed")]
    public float referenceHeight = 10f;
    [Range(0.05f, 0.45f), Tooltip("Шероховатость рельефа: 0.14 = открытое поле, 0.30 = плотная застройка")]
    public float terrainRoughnessAlpha = 0.18f;

    [Header("Порывы ветра (Gusts)")]
    public bool enableGusts = true;
    [Range(0f, 2f), Tooltip("Множитель силы порывов относительно базового ветра")]
    public float gustStrength = 0.45f;
    [Range(0.05f, 2f), Tooltip("Частота смены порывов")]
    public float gustFrequency = 0.25f;

    [Header("3D Турбулентность (вихри)")]
    public bool enableTurbulence = true;
    [Range(0f, 1.5f), Tooltip("Интенсивность турбулентности (доля от текущей скорости ветра)")]
    public float turbulenceIntensity = 0.25f;
    [Tooltip("Масштаб вихрей в пространстве (чем больше, тем мельче вихри)")]
    public float spatialNoiseScale = 0.08f;
    [Tooltip("Скорость эволюции вихрей во времени")]
    public float timeNoiseScale = 0.6f;

    [Header("Препятствия и ветровая тень")]
    public bool enableObstacleOcclusion = true;
    [Tooltip("Слои зданий, деревьев и рельефа")]
    public LayerMask obstacleMask;
    [Tooltip("На какое расстояние против ветра проверять препятствия")]
    public float occlusionCheckDistance = 40f;
    [Range(0f, 1f), Tooltip("Сколько процентов ветра остается в тени за зданием")]
    public float shadowWindFactor = 0.2f;
    [Range(1f, 3f), Tooltip("Во сколько раз усиливается турбулентность в зоне срыва потока за зданием")]
    public float wakeTurbulenceBoost = 2.0f;

    private float seedOffset;

    private void OnEnable()
    {
        if (!ActiveZones.Contains(this)) ActiveZones.Add(this);
        seedOffset = Random.Range(0f, 1000f);
    }

    private void OnDisable()
    {
        ActiveZones.Remove(this);
    }

    /// <summary>
    /// Возвращает базовое направление ветра (нормализованный вектор).
    /// </summary>
    public Vector3 GetBaseWindDirection()
    {
        if (useTransformDirection)
            return transform.forward.normalized;

        Quaternion rot = Quaternion.Euler(-elevationAngle, azimuthAngle, 0f);
        return (rot * Vector3.forward).normalized;
    }

    /// <summary>
    /// Главный метод: вычисляет вектор ветра (м/с) в заданной точке мира.
    /// Дополнительно возвращает уровень локальной турбулентности (для угловых возмущений дрона).
    /// </summary>
    public Vector3 SampleWindAtPosition(Vector3 worldPos, out float localTurbulenceMagnitude)
    {
        localTurbulenceMagnitude = 0f;
        if (baseWindSpeed <= 0.001f) return Vector3.zero;

        float zoneWeight = GetZoneWeight(worldPos);
        if (zoneWeight <= 0.001f) return Vector3.zero;

        Vector3 dir = GetBaseWindDirection();
        float t = Time.time;

        float altitudeFactor = 1f;
        if (useAltitudeProfile)
        {
            float heightAboveGround = Mathf.Max(0.5f, worldPos.y - groundLevelY);
            altitudeFactor = Mathf.Pow(heightAboveGround / Mathf.Max(1f, referenceHeight), terrainRoughnessAlpha);
            altitudeFactor = Mathf.Clamp(altitudeFactor, 0.25f, 2.5f);
        }

        float currentSpeed = baseWindSpeed * altitudeFactor;

        if (enableGusts)
        {
            float gustWave = Mathf.PerlinNoise(t * gustFrequency + seedOffset, seedOffset * 0.5f);
            float gustEnvelope = Mathf.Pow(gustWave, 2.2f) * 2.0f;
            currentSpeed *= (1f + gustEnvelope * gustStrength);
        }

        float turbMultiplier = 1f;
        if (enableObstacleOcclusion && obstacleMask != 0)
        {
            if (Physics.Raycast(worldPos, -dir, out RaycastHit hit, occlusionCheckDistance, obstacleMask))
            {
                float proximity = 1f - (hit.distance / occlusionCheckDistance);
                float occlusionFactor = Mathf.Lerp(1f, shadowWindFactor, proximity);
                currentSpeed *= occlusionFactor;
                turbMultiplier = Mathf.Lerp(1f, wakeTurbulenceBoost, proximity);
            }
        }

        Vector3 windVelocity = dir * currentSpeed;

        if (enableTurbulence && turbulenceIntensity > 0f)
        {
            Vector3 turbVector = Sample3DTurbulence(worldPos, t);
            float turbStrength = currentSpeed * turbulenceIntensity * turbMultiplier;
            windVelocity += turbVector * turbStrength;
            localTurbulenceMagnitude = turbStrength;
        }

        return windVelocity * zoneWeight;
    }

    private float GetZoneWeight(Vector3 worldPos)
    {
        if (areaMode == WindAreaMode.Global) return 1f;

        if (areaMode == WindAreaMode.LocalizedSphere)
        {
            float dist = Vector3.Distance(worldPos, transform.position);
            if (dist >= sphereRadius) return 0f;
            float fadeStart = sphereRadius * (1f - edgeFalloff);
            return dist <= fadeStart ? 1f : Mathf.SmoothStep(1f, 0f, (dist - fadeStart) / (sphereRadius - fadeStart));
        }

        if (areaMode == WindAreaMode.LocalizedBox)
        {
            Vector3 localPos = transform.InverseTransformPoint(worldPos);
            Vector3 half = boxSize * 0.5f;

            float nx = Mathf.Abs(localPos.x) / Mathf.Max(0.01f, half.x);
            float ny = Mathf.Abs(localPos.y) / Mathf.Max(0.01f, half.y);
            float nz = Mathf.Abs(localPos.z) / Mathf.Max(0.01f, half.z);
            float maxNorm = Mathf.Max(nx, Mathf.Max(ny, nz));

            if (maxNorm >= 1f) return 0f;
            float fadeStart = 1f - edgeFalloff;
            return maxNorm <= fadeStart ? 1f : Mathf.SmoothStep(1f, 0f, (maxNorm - fadeStart) / edgeFalloff);
        }

        return 1f;
    }

    private Vector3 Sample3DTurbulence(Vector3 pos, float time)
    {
        Vector3 flowOffset = GetBaseWindDirection() * (time * timeNoiseScale * 3f);
        Vector3 p = (pos - flowOffset) * spatialNoiseScale;
        float t = time * timeNoiseScale + seedOffset;

        float tx = SampleAxisNoise(p, t, 11.3f) * 0.7f + SampleAxisNoise(p * 2.3f, t * 2.1f, 37.1f) * 0.3f;
        float ty = SampleAxisNoise(p, t, 71.7f) * 0.7f + SampleAxisNoise(p * 2.3f, t * 2.1f, 103.5f) * 0.3f;
        float tz = SampleAxisNoise(p, t, 149.9f) * 0.7f + SampleAxisNoise(p * 2.3f, t * 2.1f, 193.2f) * 0.3f;
        return new Vector3(tx, ty * 0.5f, tz);
    }

    private float SampleAxisNoise(Vector3 p, float t, float offset)
    {
        float n1 = Mathf.PerlinNoise(p.x + t + offset, p.y + p.z + offset);
        float n2 = Mathf.PerlinNoise(p.z - t + offset * 1.3f, p.x + p.y + offset);
        return ((n1 + n2) - 1f);
    }

    /// <summary>
    /// Удобный метод для изменения параметров из UI или по сети.
    /// </summary>
    public void SetWindParameters(float speedMs, float azimuthDeg, float elevationDeg = 0f)
    {
        baseWindSpeed = Mathf.Max(0f, speedMs);
        azimuthAngle = azimuthDeg;
        elevationAngle = elevationDeg;

        if (useTransformDirection)
            transform.rotation = Quaternion.Euler(-elevationDeg, azimuthDeg, 0f);
    }

    private void OnDrawGizmos()
    {
        Vector3 dir = GetBaseWindDirection();
        Vector3 center = transform.position;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.85f);
        float arrowLen = Mathf.Clamp(baseWindSpeed, 2f, 20f);
        Vector3 tip = center + dir * arrowLen;
        Gizmos.DrawLine(center, tip);

        Vector3 right = Vector3.Cross(dir, Vector3.up);
        if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(dir, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, dir).normalized;

        Gizmos.DrawLine(tip, tip - dir * 1.2f + right * 0.6f);
        Gizmos.DrawLine(tip, tip - dir * 1.2f - right * 0.6f);
        Gizmos.DrawLine(tip, tip - dir * 1.2f + up * 0.6f);
        Gizmos.DrawLine(tip, tip - dir * 1.2f - up * 0.6f);

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        if (areaMode == WindAreaMode.LocalizedSphere)
        {
            Gizmos.DrawWireSphere(center, sphereRadius);
        }
        else if (areaMode == WindAreaMode.LocalizedBox)
        {
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, boxSize);
            Gizmos.matrix = oldMatrix;
        }
    }
}