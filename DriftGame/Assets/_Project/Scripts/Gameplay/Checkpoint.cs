using UnityEngine;

/// 賽道檢查點（含起點線 index 0），觸發後通知 RaceTimer。
[RequireComponent(typeof(BoxCollider))]
public class Checkpoint : MonoBehaviour
{
    public int index;

    void OnTriggerEnter(Collider other)
    {
        if (other.attachedRigidbody == null) return;
        var car = other.attachedRigidbody.GetComponent<CarController>();
        if (car != null && car.enabled && RaceTimer.Instance != null)
            RaceTimer.Instance.Checkpoint(index);
    }
}
