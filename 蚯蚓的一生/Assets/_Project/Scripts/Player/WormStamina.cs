using UnityEngine;

/// 體力：鑽土與衝刺消耗，地面回復，吃食物快速回復
public class WormStamina : MonoBehaviour
{
    public float max = 100f;
    public float burrowDrain = 12f;   // 鑽土每秒消耗
    public float sprintDrain = 30f;   // 衝刺每秒消耗（約 3.3 秒衝完，衝刺是爆發不是常駐）
    public float regen = 22f;         // 地面每秒回復
    public float exhaustRecover = 30f; // 力竭後要回到這個值才能再衝刺

    public float Current { get; private set; }
    public float Percent => max > 0f ? Current / max : 0f;
    /// 力竭：體力見底後鎖住衝刺，回復到門檻才解鎖——雞就有機會追上你
    public bool Exhausted { get; private set; }

    WormController worm;
    BurrowSystem burrow;

    void Awake()
    {
        worm = GetComponent<WormController>();
        burrow = GetComponent<BurrowSystem>();
        Current = max;
    }

    void Update()
    {
        if (GameManager.I != null && GameManager.I.State != GameState.Playing) return;

        if (burrow.IsBurrowed) Current -= burrowDrain * Time.deltaTime;
        else if (worm.IsSprinting) Current -= sprintDrain * Time.deltaTime;
        else Current += regen * Time.deltaTime;

        Current = Mathf.Clamp(Current, 0f, max);

        if (Current <= 0.01f) Exhausted = true;
        else if (Exhausted && Current >= exhaustRecover) Exhausted = false;
    }

    public void Restore(float amount)
    {
        Current = Mathf.Clamp(Current + amount, 0f, max);
    }
}
