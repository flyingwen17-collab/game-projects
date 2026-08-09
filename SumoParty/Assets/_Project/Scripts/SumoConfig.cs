using UnityEngine;

/// <summary>
/// 相撲對戰的全部手感參數（企劃書 §3、流程 MD §6.4 鐵則 7：調手感不改程式碼）。
/// 力的單位是牛頓，質量是公斤——真實量級，數字才有物理意義。
/// </summary>
[CreateAssetMenu(menuName = "Sumo/對戰參數", fileName = "SumoConfig")]
public class SumoConfig : ScriptableObject
{
    [Header("體格（真實幕內力士量級）")]
    public float mass = 150f;
    [Tooltip("質心離腳底的高度。壓低＝相撲的「腰を落とす」")]
    public float centerOfMassHeight = 0.62f;
    public float shoulderHeight = 1.35f;
    public float mawashiHeight = 0.85f;

    [Header("平衡：失衡是物理現象，不是狀態旗標")]
    [Tooltip("把身體拉回直立的扭矩。CP 越低越弱 → 站不穩")]
    public float balanceTorque = 2600f;
    public float balanceDamping = 420f;
    [Tooltip("轉身面向對手的扭矩。用扭矩而不是直接設 rotation，才不會跟求解器搶控制權")]
    public float facingTorque = 900f;
    [Tooltip("傾斜超過這個角度就再也站不回來")]
    public float fallAngle = 52f;
    [Range(0f, 1f), Tooltip("CP 歸零時平衡力剩多少比例")]
    public float lowCpBalance = 0.25f;

    [Header("站立與移動")]
    [Tooltip("腳底摩擦力上限（N）。真實量級：μ≈0.8 × 150kg × 9.81 ≈ 1180 N")]
    public float footGrip = 1250f;
    [Tooltip("抵抗滑動的阻尼係數（N per m/s）。越大越站得住")]
    public float footDamping = 1400f;
    public float moveForce = 900f;

    [Header("突き Tap：推掌")]
    [Tooltip("持續施力的時間。真實的推是持續 0.1~0.2 秒，不是一瞬間的脈衝——" +
             "單幀施力對 150 公斤的人只推得動 8 毫米")]
    public float thrustDuration = 0.14f;
    public float thrustForce = 3200f;
    public float thrustRange = 1.55f;
    public float thrustAngle = 70f;
    public float thrustCP = 8f;
    public float thrustCooldown = 0.26f;

    [Header("突進 Swipe↑：大衝撞，剋防禦")]
    public float chargeForce = 6200f;
    public float chargeDuration = 0.32f;
    public float chargeCP = 24f;
    public float chargeCooldown = 1.15f;
    [Range(0f, 1f), Tooltip("突進能無視多少比例的防禦抵抗")]
    public float chargeBreakThrough = 0.75f;

    [Header("後退／引き Swipe↓")]
    public float retreatForce = 2400f;
    [Tooltip("對方正在突進時後拉 → 引き落とし，把他往前下方拽")]
    public float pullDownForce = 3800f;
    public float retreatCP = 9f;
    public float retreatCooldown = 0.5f;

    [Header("いなし Swipe←→：閃身，剋突進")]
    public float sidestepImpulse = 6.2f;
    public float sidestepCP = 12f;
    public float sidestepCooldown = 0.85f;
    [Tooltip("閃身後的破綻時間，這段期間吃突き會加倍")]
    public float sidestepVulnerable = 0.32f;
    public float whiffStumble = 2600f;

    [Header("防禦 Hold：剋突き")]
    [Range(0f, 1f)] public float braceResist = 0.7f;
    public float braceCPPerSec = 15f;
    [Tooltip("擋下突き時反削對方的 CP")]
    public float braceReflectCP = 7f;

    [Header("組手 四つ身")]
    public float gripRange = 1.3f;
    public float gripHoldTime = 0.22f;
    [Tooltip("關節承受超過這個衝量就脫手")]
    public float gripBreakForce = 4200f;
    public float yoriForce = 2800f;
    public float hikiForce = 2300f;
    public float throwTorque = 2400f;
    public float shakeForce = 1100f;
    public float gripCPPerSec = 6f;

    [Header("CP 気力")]
    public float cpMax = 100f;
    public float cpRegenPerSec = 20f;
    [Tooltip("停手多久之後才開始回復")]
    public float cpRegenDelay = 0.55f;

    [Header("土俵（真實直徑 4.55 m）")]
    public float dohyoRadius = 2.275f;
    public float matchTime = 60f;
    [Tooltip("立合い：開局後多久內出手算搶到先手")]
    public float tachiaiWindow = 0.6f;
    [Range(1f, 2f)] public float tachiaiBonus = 1.4f;

    [Header("輸入手勢")]
    [Tooltip("移動超過螢幕短邊的這個比例才算滑動")]
    public float swipeThreshold = 0.055f;
    public float tapMaxTime = 0.22f;
    public float holdMinTime = 0.16f;
}
