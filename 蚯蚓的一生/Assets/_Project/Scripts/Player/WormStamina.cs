using UnityEngine;

/// 體力：鑽土與衝刺消耗，地面回復，吃食物快速回復
public class WormStamina : MonoBehaviour
{
    public float max = 100f;
    public float burrowDrain = 12f;  // 鑽土每秒消耗
    public float sprintDrain = 15f;  // 衝刺每秒消耗
    public float regen = 22f;        // 地面每秒回復

    public float Current { get; private set; }
    public float Percent => max > 0f ? Current / max : 0f;

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
    }

    public void Restore(float amount)
    {
        Current = Mathf.Clamp(Current + amount, 0f, max);
    }
}
