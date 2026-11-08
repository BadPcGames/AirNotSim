using UnityEngine;

public class AirPresureManager : MonoBehaviour
{
    public static AirPresureManager instance {private set; get; }

    public float BasePreshure = 101325f;
    public float BaseAltitude = 100f;
    public float BaseTemp = 25f;
    public float HumidityPercent = 50f;


    private void Awake()
    {
        instance = this;
    }

    public float GetAirDensityForDrone(float droneAltitude)
    {
        return GetAirDensity(droneAltitude, BaseAltitude, BaseTemp, BasePreshure, HumidityPercent);
    }

    /// <summary>
    /// Расчет плотности воздуха с учетом высоты, температуры, давления и влажности.
    /// </summary>
    /// <param name="droneAltitude">Текущая высота дрона над уровнем моря (в метрах)</param>
    /// <param name="baseAltitude">Высота метеостанции/земли над уровнем моря (по умолчанию 0)</param>
    /// <param name="baseTempC">Температура на базовой высоте в °C (по умолчанию 15°C)</param>
    /// <param name="basePressurePa">Давление на базовой высоте в Паскалях (по умолчанию 101325 Па)</param>
    /// <param name="humidityPercent">Влажность в % от 0 до 100 (по умолчанию 50%)</param>
    /// <returns>Плотность воздуха в кг/м³</returns>
    public float GetAirDensity(float droneAltitude, float baseAltitude = 0f, float baseTempC = 15f, float basePressurePa = 101325f, float humidityPercent = 50f)
    {
        // Физические константы
        const float R_d = 287.058f; // Газовая постоянная сухого воздуха
        const float R_v = 461.495f; // Газовая постоянная водяного пара
        const float L = 0.0065f;    // Падение температуры на метр высоты (6.5 градусов на км)

        // Разница высот между дроном и базовой точкой
        float deltaH = droneAltitude - baseAltitude;

        // Температура на базовой высоте в Кельвинах
        float baseTempK = baseTempC + 273.15f;

        // 1. Рассчитываем температуру и давление НА ВЫСОТЕ ДРОНА
        float currentTempK = baseTempK - (L * deltaH);
        float currentTempC = currentTempK - 273.15f;

        // Барометрическая формула для давления
        float currentPressurePa = basePressurePa * Mathf.Pow(1f - (L * deltaH) / baseTempK, 5.255f);

        // 2. Учет влажности (давление водяного пара)
        // Формула Тетенса для давления насыщенного пара (Es). Умножаем на 100 для перевода из гПа в Паскали
        float Es = 6.1078f * Mathf.Pow(10f, (7.5f * currentTempC) / (currentTempC + 237.3f)) * 100f;

        // Фактическое парциальное давление пара (Pv) и сухого воздуха (Pd)
        float Pv = Es * (humidityPercent / 100f);
        float Pd = currentPressurePa - Pv;

        // 3. Финальный расчет плотности (сухая часть + влажная часть)
        float airDensity = (Pd / (R_d * currentTempK)) + (Pv / (R_v * currentTempK));

        return airDensity;
    }
}
