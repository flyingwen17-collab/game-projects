using System;
using UnityEngine;

public enum BoutResult { None, PushOut, Down, TimeUp }
public enum MatchPhase { Shikiri, Tachiai, Torikumi, Kecchaku }

/// <summary>
/// 一番的規則與流程（企劃書 §2）。
///
/// 勝負判定照真實相撲：
///   押し出し／寄り切り —— 腳踏出土俵俵線外
///   倒し               —— 腳掌以外的身體部位觸地
///   時間到             —— 依剩餘 CP 與推進距離判優勢
///
/// 流程：仕切り → 立合い → 取組 → 決着，三本勝負。
/// </summary>
public class SumoMatch : MonoBehaviour
{
    public SumoConfig cfg;
    public Rikishi east;      // P1
    public Rikishi west;      // P2
    public Transform dohyoCenter;
    public int winsNeeded = 2;

    public MatchPhase Phase { get; private set; } = MatchPhase.Shikiri;
    public int EastWins { get; private set; }
    public int WestWins { get; private set; }
    public float BoutTime { get; private set; }
    /// <summary>激烈度 0~1：驅動觀眾吶喊與 BGM 混音（企劃書 §4）。</summary>
    public float Intensity { get; private set; }

    /// <summary>(勝者, 敗者, 決まり手)。null 勝者＝平手。</summary>
    public event Action<Rikishi, Rikishi, BoutResult> OnBoutEnd;
    public event Action<Rikishi> OnMatchEnd;
    public event Action<MatchPhase> OnPhaseChange;

    float phaseTimer;
    float intensityAccum;
    Vector3 eastStart, westStart;

    public const float ShikiriDuration = 1.6f;

    void Start()
    {
        if (cfg == null || east == null || west == null)
        {
            Debug.LogError("[SumoMatch] cfg / east / west 沒設定");
            enabled = false; return;
        }

        east.opponent = west;
        west.opponent = east;

        float d = cfg.dohyoRadius * 0.45f;
        Vector3 c = Center;
        eastStart = c + new Vector3(0f, 0f, -d);
        westStart = c + new Vector3(0f, 0f, d);

        east.OnImpact += (f, p) => intensityAccum += f;
        west.OnImpact += (f, p) => intensityAccum += f;

        BeginBout();
    }

    Vector3 Center => dohyoCenter != null ? dohyoCenter.position : Vector3.zero;

    void BeginBout()
    {
        east.ResetForBout(eastStart, Vector3.forward);
        west.ResetForBout(westStart, Vector3.back);
        east.Active = false;
        west.Active = false;
        BoutTime = 0f;
        intensityAccum = 0f;
        SetPhase(MatchPhase.Shikiri);
        phaseTimer = ShikiriDuration;
    }

    void SetPhase(MatchPhase p)
    {
        if (Phase == p) return;
        Phase = p;
        OnPhaseChange?.Invoke(p);
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // 激烈度：撞擊累積 + 逼近俵線的緊張感，兩者取高
        float edge = Mathf.Max(EdgeCloseness(east), EdgeCloseness(west));
        float hits = Mathf.Clamp01(intensityAccum / 40000f);
        Intensity = Mathf.Lerp(Intensity, Mathf.Max(edge, hits), dt * 3f);
        intensityAccum = Mathf.Max(0f, intensityAccum - 12000f * dt);

        switch (Phase)
        {
            case MatchPhase.Shikiri:
                phaseTimer -= dt;
                if (phaseTimer <= 0f)
                {
                    east.Active = true;
                    west.Active = true;
                    SetPhase(MatchPhase.Tachiai);
                    phaseTimer = cfg.tachiaiWindow;
                }
                break;

            case MatchPhase.Tachiai:
                BoutTime += dt;
                phaseTimer -= dt;
                if (phaseTimer <= 0f) SetPhase(MatchPhase.Torikumi);
                CheckDecision();
                break;

            case MatchPhase.Torikumi:
                BoutTime += dt;
                CheckDecision();
                if (BoutTime >= cfg.matchTime) Decide(null, BoutResult.TimeUp);
                break;

            case MatchPhase.Kecchaku:
                phaseTimer -= dt;
                if (phaseTimer <= 0f)
                {
                    if (EastWins >= winsNeeded || WestWins >= winsNeeded)
                        OnMatchEnd?.Invoke(EastWins > WestWins ? east : west);
                    else BeginBout();
                }
                break;
        }
    }

    float EdgeCloseness(Rikishi r)
    {
        float d = r.DistanceFromCenter(Center);
        return Mathf.Clamp01(d / cfg.dohyoRadius);
    }

    void CheckDecision()
    {
        // 倒し優先於出界：真實相撲裡先落地的先輸
        if (east.BodyTouchedGround) { Decide(west, BoutResult.Down); return; }
        if (west.BodyTouchedGround) { Decide(east, BoutResult.Down); return; }

        if (east.DistanceFromCenter(Center) > cfg.dohyoRadius) { Decide(west, BoutResult.PushOut); return; }
        if (west.DistanceFromCenter(Center) > cfg.dohyoRadius) { Decide(east, BoutResult.PushOut); return; }
    }

    void Decide(Rikishi winner, BoutResult result)
    {
        if (Phase == MatchPhase.Kecchaku) return;

        if (result == BoutResult.TimeUp)
        {
            // 時間到：CP 高、且離中心近（推進較多）的一方優勢
            float e = east.CPRatio - EdgeCloseness(east);
            float w = west.CPRatio - EdgeCloseness(west);
            if (Mathf.Abs(e - w) < 0.05f) winner = null;
            else winner = e > w ? east : west;
        }

        if (winner == east) EastWins++;
        else if (winner == west) WestWins++;

        east.Active = false;
        west.Active = false;
        east.ReleaseGrip();
        west.ReleaseGrip();

        SetPhase(MatchPhase.Kecchaku);
        phaseTimer = 2.5f;

        Rikishi loser = winner == null ? null : (winner == east ? west : east);
        OnBoutEnd?.Invoke(winner, loser, result);
    }

    /// <summary>
    /// 給 SumoSelfTest 用：直接進入取組階段。
    /// 自測要驗的是物理，不是流程；讓狀態機自己跑會在仕切り階段把力士設成
    /// Active=false，推力全被忽略，測出來全是假的。
    /// </summary>
    public void TestBeginTorikumi()
    {
        SetPhase(MatchPhase.Torikumi);
        BoutTime = 0f;
        east.Active = true;
        west.Active = true;
    }

    public static string ResultName(BoutResult r) => r switch
    {
        BoutResult.PushOut => "押し出し",
        BoutResult.Down => "倒し",
        BoutResult.TimeUp => "時間切れ",
        _ => "",
    };
}
