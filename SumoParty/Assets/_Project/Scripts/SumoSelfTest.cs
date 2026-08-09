using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 物理自測（流程 MD §6.4 鐵則 8、§5 關卡 3）。
///
/// 為什麼要有：編譯過、看起來會動，不代表相剋關係在**物理上**真的成立。
/// 這支用腳本餵固定輸入，量實際位移／傾斜／狀態，把「我以為有效」變成數字。
///
/// 跑法（batch mode 的 Play 不真的算圖也不跑物理迴圈，必須用打包後的執行檔）：
///   SumoParty.exe -sumotest
/// 結果寫在執行檔旁的 selftest.txt，跑完自動結束。
/// </summary>
public class SumoSelfTest : MonoBehaviour
{
    SumoMatch match;
    Rikishi east, west;
    readonly StringBuilder log = new StringBuilder();
    int passed, failed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        foreach (var a in System.Environment.GetCommandLineArgs())
            if (a == "-sumotest")
            {
                var go = new GameObject("~SumoSelfTest");
                DontDestroyOnLoad(go);
                go.AddComponent<SumoSelfTest>();
                return;
            }
    }

    IEnumerator Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        yield return null;
        match = FindAnyObjectByType<SumoMatch>();
        if (match == null) { Fail("找不到 SumoMatch"); Finish(); yield break; }

        east = match.east; west = match.west;

        // 關掉玩家/AI 驅動，改由本測試直接餵指令
        foreach (var d in FindObjectsByType<RikishiDriver>(FindObjectsInactive.Include))
            d.enabled = false;
        foreach (var b in FindObjectsByType<RikishiAI>(FindObjectsInactive.Include))
            b.enabled = false;

        log.AppendLine("=== 相撲物理自測 ===");
        log.AppendLine($"Unity {Application.unityVersion}   fixedDeltaTime {Time.fixedDeltaTime:0.0000}");
        log.AppendLine($"力士質量 {east.GetComponent<Rigidbody>().mass:0} kg   土俵半徑 {match.cfg.dohyoRadius:0.000} m");
        log.AppendLine();

        yield return WaitPhase();          // 等仕切り結束

        yield return T1_StandStill();
        yield return T2_ThrustMoves();
        yield return T3_BraceBlocksThrust();
        yield return T4_ChargeBreaksBrace();
        yield return T5_SidestepBeatsCharge();
        yield return T6_GripCreatesJoint();
        yield return T7_PushOutDetected();

        Finish();
    }

    IEnumerator WaitPhase()
    {
        float t = 0f;
        while (match.Phase == MatchPhase.Shikiri && t < 5f) { t += Time.deltaTime; yield return null; }
    }

    // ---------- 情境 ----------

    IEnumerator T1_StandStill()
    {
        Reset();
        yield return Idle(3f);
        float tilt = east.TiltAngle;
        float drift = Vector3.ProjectOnPlane(east.transform.position - StartPos(east), Vector3.up).magnitude;
        Check("靜置 3 秒不該自己倒", tilt < 5f, $"傾斜 {tilt:0.0}°");
        Check("靜置 3 秒不該漂移", drift < 0.12f, $"位移 {drift:0.000} m");
    }

    float unbracedThrust;   // T2 量到的基準，供 T3 做相對比較

    IEnumerator T2_ThrustMoves()
    {
        Reset();
        yield return Approach();
        Vector3 before = west.transform.position;
        float dist = Flat(west.transform.position - east.transform.position).magnitude;
        bool reach = east.CanReach(match.cfg.thrustRange, match.cfg.thrustAngle);
        float peakV = 0f;

        for (int i = 0; i < 4; i++)
        {
            Feed(east, SumoGesture.Tap, false);
            float t = 0f;
            while (t < 0.3f)
            {
                Feed(east, SumoGesture.None, false);
                Feed(west, SumoGesture.None, false);
                peakV = Mathf.Max(peakV, Flat(west.Velocity).magnitude);
                t += Time.deltaTime;
                yield return null;
            }
        }
        unbracedThrust = Flat(west.transform.position - before).magnitude;
        Check("突き 應該推得動對方", unbracedThrust > 0.03f,
              $"後退 {unbracedThrust:0.000} m｜距離 {dist:0.00} m｜射程內={reach}｜對方峰值速度 {peakV:0.000} m/s｜west.Active={west.Active}");
    }

    IEnumerator T3_BraceBlocksThrust()
    {
        Reset();
        yield return Approach();
        Vector3 before = west.transform.position;
        float cpBefore = east.CP;
        for (int i = 0; i < 4; i++)
        {
            Feed(west, SumoGesture.None, true);      // 西方持續防禦
            Feed(east, SumoGesture.Tap, false);
            yield return Idle(0.3f, keepWestHolding: true);
        }
        float moved = Flat(west.transform.position - before).magnitude;
        // 用相對比較而不是絕對門檻：調參數時門檻不會失效
        Check("防禦 應該擋下突き（位移少於未防禦的一半）",
              moved < unbracedThrust * 0.5f,
              $"防禦 {moved:0.000} m vs 未防禦 {unbracedThrust:0.000} m");
        Check("防禦 應該反削攻擊方 CP", east.CP < cpBefore, $"攻方 CP {cpBefore:0} → {east.CP:0}");
    }

    IEnumerator T4_ChargeBreaksBrace()
    {
        Reset();
        // 必須拉開到組手範圍外，否則前滑會變成抓廻し而不是突進
        yield return Separate(0.95f);
        Vector3 before = west.transform.position;
        Feed(west, SumoGesture.None, true);          // 西方防禦
        Feed(east, SumoGesture.Forward, false);      // 東方突進
        yield return Idle(1.0f, keepWestHolding: true);
        float moved = Flat(west.transform.position - before).magnitude;
        Check("突進 應該破防（推得比突き遠）", moved > 0.15f, $"對方後退 {moved:0.000} m");
    }

    IEnumerator T5_SidestepBeatsCharge()
    {
        Reset();
        yield return Separate(0.95f);                // 拉開到突進距離
        Feed(west, SumoGesture.Forward, false);      // 西方突進
        yield return Idle(0.08f);
        bool wasCharging = west.Charging;
        Vector3 before = west.transform.position;

        Feed(east, SumoGesture.Left, false);         // 東方閃身
        yield return Idle(0.2f);                     // 破綻只有 0.32s，要在窗口內量
        bool stumbled = west.Vulnerable;
        yield return Idle(0.5f);
        float overrun = Flat(west.transform.position - before).magnitude;

        Check("いなし 應該讓突進方衝空失衡",
              wasCharging && (stumbled || overrun > 0.25f),
              $"對方突進中={wasCharging} 破綻={stumbled} 衝過頭 {overrun:0.000} m");
    }

    IEnumerator T6_GripCreatesJoint()
    {
        Reset();
        yield return Approach();
        float gd = Flat(west.transform.position - east.transform.position).magnitude;
        bool greach = east.CanReach(match.cfg.gripRange, 90f);
        Feed(east, SumoGesture.Forward, false);       // 貼身前滑＝伸手抓廻し
        yield return Idle(0.25f);
        Check("貼身前滑 應該抓到廻し（建立關節）", east.Gripping,
              $"Gripping={east.Gripping}｜距離 {gd:0.00} m（門檻 {match.cfg.gripRange:0.00}）｜射程內={greach}｜對方破綻={west.Vulnerable}");

        if (east.Gripping)
        {
            Vector3 before = west.transform.position;
            for (int i = 0; i < 3; i++)
            {
                Feed(east, SumoGesture.Forward, false);   // 組手中前滑＝寄り
                yield return Idle(0.3f);
            }
            float moved = Flat(west.transform.position - before).magnitude;
            Check("寄り 應該壓著對方前進", moved > 0.06f, $"推進 {moved:0.000} m");
        }
        east.ReleaseGrip();
    }

    IEnumerator T7_PushOutDetected()
    {
        Reset();
        // 這一項要驗規則，所以把狀態機打開並直接進取組階段
        match.enabled = true;
        match.TestBeginTorikumi();
        yield return Idle(0.1f);

        // 直接把西方搬到俵線外，看規則有沒有判出界
        var rb = west.GetComponent<Rigidbody>();
        Vector3 c = match.dohyoCenter != null ? match.dohyoCenter.position : Vector3.zero;
        rb.position = c + new Vector3(0f, 0f, match.cfg.dohyoRadius + 0.4f);
        west.transform.position = rb.position;
        yield return Idle(0.5f);
        Check("腳出俵線外 應該判出界", match.Phase == MatchPhase.Kecchaku || match.EastWins > 0,
              $"phase={match.Phase} 東勝={match.EastWins}");
    }

    // ---------- 工具 ----------

    void Reset()
    {
        // 關掉狀態機再擺位。開著的話仕切り階段會把 Active 設回 false，
        // 所有推力都會被 ReceiveForce 忽略 —— 前一版就是栽在這裡。
        match.enabled = false;

        Vector3 c = match.dohyoCenter != null ? match.dohyoCenter.position : Vector3.zero;
        float d = match.cfg.dohyoRadius * 0.45f;
        east.ResetForBout(c + new Vector3(0f, 0f, -d), Vector3.forward);
        west.ResetForBout(c + new Vector3(0f, 0f, d), Vector3.back);
        east.Active = true; west.Active = true;
    }

    Vector3 StartPos(Rikishi r)
    {
        Vector3 c = match.dohyoCenter != null ? match.dohyoCenter.position : Vector3.zero;
        float d = match.cfg.dohyoRadius * 0.45f;
        return c + new Vector3(0f, 0f, r == east ? -d : d);
    }

    /// <summary>把兩人拉近到組手/突き範圍。</summary>
    IEnumerator Approach() => Separate(0.55f);

    /// <summary>把兩人各自擺在離中心 halfGap 的位置（總間距 = halfGap × 2）。</summary>
    IEnumerator Separate(float halfGap)
    {
        Vector3 c = match.dohyoCenter != null ? match.dohyoCenter.position : Vector3.zero;
        east.transform.position = c + new Vector3(0f, 0f, -halfGap);
        west.transform.position = c + new Vector3(0f, 0f, halfGap);
        east.GetComponent<Rigidbody>().position = east.transform.position;
        west.GetComponent<Rigidbody>().position = west.transform.position;
        yield return Idle(0.35f);
    }

    void Feed(Rikishi r, SumoGesture g, bool hold)
        => r.Feed(new SumoCommand { gesture = g, holding = hold });

    IEnumerator Idle(float seconds, bool keepEastHolding = false, bool keepWestHolding = false)
    {
        float t = 0f;
        while (t < seconds)
        {
            Feed(east, SumoGesture.None, keepEastHolding);
            Feed(west, SumoGesture.None, keepWestHolding);
            t += Time.deltaTime;
            yield return null;
        }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    void Check(string name, bool ok, string detail)
    {
        if (ok) passed++; else failed++;
        log.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {name}   （{detail}）");
    }

    void Fail(string msg) { failed++; log.AppendLine($"  [FAIL] {msg}"); }

    void Finish()
    {
        log.AppendLine();
        log.AppendLine($"通過 {passed} / 失敗 {failed}");
        string file = Path.Combine(Application.dataPath, "..", "selftest.txt");
        File.WriteAllText(file, log.ToString(), new UTF8Encoding(true));
        Debug.Log(log.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(failed == 0 ? 0 : 1);
#else
        Application.Quit(failed == 0 ? 0 : 1);
#endif
    }
}
