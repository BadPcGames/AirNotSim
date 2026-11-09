using UnityEngine;
using UnityEngine.UI; // Обязательно для работы с UI

[RequireComponent(typeof(Button))]
[RequireComponent(typeof(Image))]
public class BladeUI : MonoBehaviour
{
    [Header("Связь с дроном")]
    [Tooltip("Ссылка на скрипт физики дрона")]
    public DronePhysics dronePhysics;

    [Tooltip("Индекс мотора, который будет ломать эта кнопка (0, 1, 2, 3...)")]
    public int motorIndex = 0;

    [Header("Цвета состояний")]
    public Color colorFull = Color.green;   
    public Color colorDamaged = Color.yellow;   
    public Color colorCritical = Color.red; 
    public Color colorDestroyed = Color.black;    

    private Button button;
    private Image buttonImage;

    void Start()
    {
        button = GetComponent<Button>();
        buttonImage = GetComponent<Image>();

        button.onClick.AddListener(OnButtonClicked);

        Invoke(nameof(UpdateButtonState), 0.1f);
    }

    void OnButtonClicked()
    {
        if (dronePhysics == null || dronePhysics.motors == null || motorIndex >= dronePhysics.motors.Count)
        {
            Debug.LogWarning("DronePhysics не назначен или индекс мотора выходит за пределы!");
            return;
        }

        DronePhysics.Motor motor = dronePhysics.motors[motorIndex];


        for (int i = 0; i < motor.activeBlades.Length; i++)
        {
            if (motor.activeBlades[i])
            {
                motor.BreakBlade(i);
                Debug.Log($"[UI] Отломана лопасть {i} у мотора {motorIndex}");
                break;
            }
        }

        UpdateButtonState();
    }

    public void UpdateButtonState()
    {
        if (dronePhysics == null || dronePhysics.motors == null || motorIndex >= dronePhysics.motors.Count) return;

        DronePhysics.Motor motor = dronePhysics.motors[motorIndex];
        if (motor.activeBlades == null) return; 

        int remainingBlades = 0;
        for (int i = 0; i < motor.activeBlades.Length; i++)
        {
            if (motor.activeBlades[i]) remainingBlades++;
        }

        float health = (float)remainingBlades / motor.initialBladeCount;

        if (health > 0.9f) 
        {
            buttonImage.color = colorFull;
        }
        else if (health > 0.6f)
        {
            buttonImage.color = colorDamaged;
        }
        else if (health > 0.3f) 
        {
            buttonImage.color = colorCritical;
        }
        else 
        {
            buttonImage.color = colorDestroyed;
            button.interactable = false; 
        }
    }
}