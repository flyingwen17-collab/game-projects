using UnityEngine;

// 全部手感參數集中此處（流程 MD P1 規定：調手感不改程式碼）
[CreateAssetMenu(fileName = "SumoParams", menuName = "Sumo/Params")]
public class SumoParams : ScriptableObject
{
    [Header("移動")]
    public float moveSpeed = 3.2f;
    public float dodgeImpulse = 4.5f;
    public float dodgeCooldown = 0.8f;

    [Header("推擊")]
    public float pushForce = 5.5f;       // 命中時施加給對手的水平速度變化
    public float pushRange = 2.1f;       // 兩人中心距離內視為命中
    public float pushAngle = 75f;        // 面向對手的容許角度
    public float pushCooldown = 0.35f;
    public float pushLunge = 1.2f;       // 出掌時自己往前的小衝

    [Header("氣力（防連點拚手速）")]
    public float staminaMax = 100f;
    public float staminaCostPerPush = 20f;
    public float staminaRegenPerSec = 24f;
    [Range(0.1f, 1f)] public float tiredPushMultiplier = 0.35f;

    [Header("扎馬步")]
    public float braceDuration = 2.5f;
    public float braceCooldown = 2f;
    [Range(0f, 1f)] public float braceResist = 0.7f;

    [Header("土俵縮圈")]
    public float shrinkDelay = 30f;
    public float shrinkPerSec = 0.06f;   // 半徑比例每秒縮減
    public float minRadiusRatio = 0.4f;

    [Header("NPC")]
    public float npcReactionDelay = 0.45f;
    [Range(0f, 1f)] public float npcPushChance = 0.7f;
    [Range(0f, 1f)] public float npcBraceChance = 0.3f;
}
