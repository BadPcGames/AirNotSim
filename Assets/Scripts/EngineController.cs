using UnityEngine;

public class EngineController : MonoBehaviour
{
    public float[] GoalSpeeds = new float[4];

    public DronePhysics Physics;

    public bool Synh;
    public float value = 1000f;

    private void FixedUpdate()
    {

        if (Synh)
        {
            for (int i = 0; i < GoalSpeeds.Length; i++)
            {
                GoalSpeeds[i] = value;
            }
        }
        for(int i = 0; i < Physics.motors.Count; i++)
        {
            Physics.motors[i].currentRPM = GoalSpeeds[i];
        }
    }
}
