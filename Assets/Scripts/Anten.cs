using UnityEngine;

public class Anten : MonoBehaviour
{
    [Header("Signal Settings")]
    public float maxRange = 1000f;

    public AnimationCurve signalFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    public LayerMask obstacleMask;
    public float obstaclePenalty = 0.2f;
}
