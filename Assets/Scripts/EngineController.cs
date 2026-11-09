using System.Collections.Generic;
using UnityEngine;

public class EngineController : MonoBehaviour
{
    public float[] GoalSpeeds = new float[4];

    public float[] throtle = new float[4];

    public float[] EngineHealth = new float[4];

    public float MaxRPM = 30000f;

    public DronePhysics Physics;

    public bool Synh;

    private void FixedUpdate()
    {
        for(int i = 0; i < Physics.motors.Count; i++)
        {
            Physics.motors[i].currentRPM = throtle[i] * MaxRPM * EngineHealth[i];
        }
    }

    public void ChangeHealth(int i, float value)
    {
        EngineHealth[i] = value;
    }
}
