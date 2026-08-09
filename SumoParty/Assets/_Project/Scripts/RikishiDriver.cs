using UnityEngine;

/// <summary>
/// 把輸入接到力士身上。一名玩家一個 Driver。
/// 觸控走自己半邊螢幕；PC 上用鍵盤模擬同一組手勢（企劃書 §1.1）。
/// </summary>
public class RikishiDriver : MonoBehaviour
{
    public Rikishi rikishi;
    public SumoConfig cfg;
    public SumoTouchZone.Half screenHalf = SumoTouchZone.Half.Left;
    public bool aiControlled;
    public RikishiAI ai;

    [Header("PC 模擬按鍵")]
    public KeyCode keyForward = KeyCode.W;
    public KeyCode keyBack = KeyCode.S;
    public KeyCode keyLeft = KeyCode.A;
    public KeyCode keyRight = KeyCode.D;
    public KeyCode keyTap = KeyCode.Space;
    public KeyCode keyHold = KeyCode.LeftShift;

    SumoTouchZone zone;

    void Awake()
    {
        if (cfg == null || rikishi == null) { enabled = false; return; }
        zone = new SumoTouchZone(screenHalf, cfg, keyForward, keyBack, keyLeft, keyRight, keyTap, keyHold);
        if (aiControlled && ai == null) ai = GetComponent<RikishiAI>();
    }

    void Update()
    {
        if (aiControlled && ai != null) rikishi.Feed(ai.Think(rikishi));
        else rikishi.Feed(zone.Poll());
    }
}
