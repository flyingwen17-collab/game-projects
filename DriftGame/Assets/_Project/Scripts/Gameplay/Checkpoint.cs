using UnityEngine;

/// 賽道檢查點（含起點線 index 0）。
/// 通過統一回報給 RaceDirector 做每台車的計圈與名次；
/// 沒有 RaceDirector 的舊場景才直接打 RaceTimer（只給玩家）。
[RequireComponent(typeof(BoxCollider))]
public class Checkpoint : MonoBehaviour
{
    public int index;

    void OnTriggerEnter(Collider other)
    {
        if (other.attachedRigidbody == null) return;
        var car = other.attachedRigidbody.GetComponent<CarController>();
        if (car == null || !car.enabled) return;

        if (RaceDirector.Instance != null)
        {
            RaceDirector.Instance.PassCheckpoint(car, index);
        }
        else if (RaceTimer.Instance != null && car.GetComponent<NPCDriver>() == null)
        {
            // 舊場景後援：只有玩家（沒掛 AI 的車）能推進圈速計時
            RaceTimer.Instance.Checkpoint(index);
        }
    }
}
