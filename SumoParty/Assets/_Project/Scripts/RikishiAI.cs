using UnityEngine;

/// <summary>
/// NPC 力士。依企劃書 §1.3 的四角相剋表決策，不是亂出招 ——
/// 這樣玩家才有「讀心」可言，而不是在跟隨機亂數對戰。
///
///   突進 剋 防禦 ／ 防禦 剋 突き ／ 突き 剋 いなし ／ いなし 剋 突進
///
/// 難度用「反應延遲」與「讀錯機率」控制，不是靠加傷害或加速度作弊。
/// </summary>
public class RikishiAI : MonoBehaviour
{
    public SumoConfig cfg;

    [Header("難度")]
    [Tooltip("看到對手動作後多久才反應（秒）。越小越強")]
    public float reactionDelay = 0.35f;
    [Range(0f, 1f), Tooltip("讀錯而選到非相剋招的機率")]
    public float mistakeChance = 0.3f;
    [Tooltip("兩次出招之間的最短間隔")]
    public float actionInterval = 0.45f;

    float nextActionTime;
    float observedTime;
    SumoGesture plannedCounter = SumoGesture.None;
    bool holdIntent;

    public SumoCommand Think(Rikishi self)
    {
        var cmd = new SumoCommand();
        if (self == null || self.opponent == null || cfg == null) return cmd;

        var foe = self.opponent;
        float dist = Vector3.Distance(
            new Vector3(self.transform.position.x, 0f, self.transform.position.z),
            new Vector3(foe.transform.position.x, 0f, foe.transform.position.z));

        // ---- 觀察：對手在做什麼 → 排一個相剋招（帶反應延遲） ----
        SumoGesture counter = ReadCounter(foe, dist);
        if (counter != plannedCounter)
        {
            plannedCounter = counter;
            observedTime = Time.time;
        }

        bool reacted = Time.time - observedTime >= reactionDelay;

        // ---- 防守意圖：CP 夠、對手在近距離逼近時舉架勢 ----
        holdIntent = self.CPRatio > 0.35f && dist < cfg.gripRange * 1.4f && !foe.Charging;
        cmd.holding = holdIntent;

        if (Time.time < nextActionTime || !reacted) return cmd;

        SumoGesture pick = plannedCounter;

        // 讀錯：改成一個不相剋的招
        if (Random.value < mistakeChance)
            pick = Random.value < 0.5f ? SumoGesture.Tap : SumoGesture.Forward;

        // 沒有明確目標時的基本行為：遠了逼近、近了推
        if (pick == SumoGesture.None)
            pick = dist > cfg.thrustRange ? SumoGesture.Forward : SumoGesture.Tap;

        // CP 不足就別出大招，先喘
        if (self.CPRatio < 0.25f && (pick == SumoGesture.Forward || pick == SumoGesture.Left || pick == SumoGesture.Right))
            pick = SumoGesture.Back;

        cmd.gesture = pick;
        nextActionTime = Time.time + actionInterval;
        plannedCounter = SumoGesture.None;
        return cmd;
    }

    /// <summary>四角相剋表：對手在做 X → 我該做 Y。</summary>
    SumoGesture ReadCounter(Rikishi foe, float dist)
    {
        if (foe.Charging)                       // 突進 → いなし 閃掉
            return Random.value < 0.5f ? SumoGesture.Left : SumoGesture.Right;

        if (foe.Vulnerable)                     // 對手閃身後的破綻 → 突き
            return SumoGesture.Tap;

        if (foe.Bracing)                        // 防禦 → 突進破防
            return SumoGesture.Forward;

        if (foe.Gripping)                       // 被抓住 → 後退掙脫
            return SumoGesture.Back;

        return SumoGesture.None;
    }
}
