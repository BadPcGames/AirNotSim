using UnityEngine;

public class CumeraTrecker : MonoBehaviour
{
    public Transform target;
    void Update()
    {
        transform.position = target.position;
    }
}
