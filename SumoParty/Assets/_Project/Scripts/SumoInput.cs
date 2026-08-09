using UnityEngine;

public enum SumoGesture { None, Tap, Forward, Back, Left, Right }

/// <summary>某一幀送給力士的指令。</summary>
public struct SumoCommand
{
    public SumoGesture gesture;   // 這一幀觸發的手勢（只在觸發當幀有值）
    public bool holding;          // 目前是否按住
}

/// <summary>
/// 螢幕分區觸控（企劃書 §1.1）。
/// 左半／右半各屬一名玩家，**只認自己區域內的手指**，
/// 所以一人狂點不會吃掉另一人的輸入 —— 這是同機雙人的關鍵。
///
/// 手勢判定（企劃書 §1.2）：
///   點一下      → Tap
///   往上滑      → Forward（衝向對手）
///   往下滑      → Back（後退／引き）
///   往左右滑    → Left / Right（いなし／投げ）
///   按住不放    → holding
/// 方向一律是「玩家自己的視角」：上＝朝對手，不管鏡頭在哪邊，兩人操作一致。
/// </summary>
public class SumoTouchZone
{
    public enum Half { Left, Right }

    readonly Half half;
    readonly SumoConfig cfg;

    int fingerId = -1;
    Vector2 startPos;
    float startTime;
    bool consumed;        // 這一次觸摸已經產生過手勢了
    bool isHolding;

    // PC 模擬用
    readonly KeyCode kUp, kDown, kLeft, kRight, kTap, kHold;

    public SumoTouchZone(Half half, SumoConfig cfg,
                         KeyCode up, KeyCode down, KeyCode left, KeyCode right,
                         KeyCode tap, KeyCode hold)
    {
        this.half = half; this.cfg = cfg;
        kUp = up; kDown = down; kLeft = left; kRight = right; kTap = tap; kHold = hold;
    }

    bool InMyHalf(Vector2 pos)
    {
        float mid = Screen.width * 0.5f;
        return half == Half.Left ? pos.x < mid : pos.x >= mid;
    }

    /// <summary>每幀在 Update 呼叫一次。</summary>
    public SumoCommand Poll()
    {
        var cmd = PollTouch();
        if (cmd.gesture == SumoGesture.None && !cmd.holding)
            cmd = PollKeyboard();      // 觸控沒輸入時才吃鍵盤，兩者可共存
        return cmd;
    }

    SumoCommand PollTouch()
    {
        var cmd = new SumoCommand();
        float swipePx = Mathf.Min(Screen.width, Screen.height) * cfg.swipeThreshold;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);

            if (t.phase == TouchPhase.Began)
            {
                // 只接自己半邊、而且目前沒在追蹤別的手指
                if (fingerId != -1 || !InMyHalf(t.position)) continue;
                fingerId = t.fingerId;
                startPos = t.position;
                startTime = Time.unscaledTime;
                consumed = false;
                isHolding = false;
                continue;
            }

            if (t.fingerId != fingerId) continue;

            Vector2 delta = t.position - startPos;
            float held = Time.unscaledTime - startTime;

            switch (t.phase)
            {
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (!consumed && delta.magnitude >= swipePx)
                    {
                        cmd.gesture = ToGesture(delta);
                        consumed = true;
                    }
                    else if (!consumed && held >= cfg.holdMinTime)
                    {
                        isHolding = true;   // 按住但沒滑 → 防禦／組手
                    }
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    if (!consumed && !isHolding && held <= cfg.tapMaxTime && delta.magnitude < swipePx)
                        cmd.gesture = SumoGesture.Tap;
                    fingerId = -1;
                    isHolding = false;
                    break;
            }
        }

        cmd.holding = isHolding;
        return cmd;
    }

    /// <summary>PC 模擬層（企劃書 §1.1）：開發期單人測試用，不是正式體驗。</summary>
    SumoCommand PollKeyboard()
    {
        var cmd = new SumoCommand();
        if (Input.GetKeyDown(kUp)) cmd.gesture = SumoGesture.Forward;
        else if (Input.GetKeyDown(kDown)) cmd.gesture = SumoGesture.Back;
        else if (Input.GetKeyDown(kLeft)) cmd.gesture = SumoGesture.Left;
        else if (Input.GetKeyDown(kRight)) cmd.gesture = SumoGesture.Right;
        else if (Input.GetKeyDown(kTap)) cmd.gesture = SumoGesture.Tap;
        cmd.holding = Input.GetKey(kHold);
        return cmd;
    }

    static SumoGesture ToGesture(Vector2 d)
    {
        if (Mathf.Abs(d.y) >= Mathf.Abs(d.x))
            return d.y > 0f ? SumoGesture.Forward : SumoGesture.Back;
        return d.x > 0f ? SumoGesture.Right : SumoGesture.Left;
    }
}
