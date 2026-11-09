using UnityEngine;
using UnityEngine.UI;

public class EngineStatSlider : MonoBehaviour
{
    public EngineController controller;
    public Slider engineStats;
    public int EngineIndex = 0;

    private void Start()
    {
        UpdateVisual();
    }

    public void OnValueChange()
    {
        controller.ChangeHealth(EngineIndex, engineStats.value);
    }

    public void UpdateVisual()
    {
        float value = controller.EngineHealth[EngineIndex];
        engineStats.value = value;
    }
}
