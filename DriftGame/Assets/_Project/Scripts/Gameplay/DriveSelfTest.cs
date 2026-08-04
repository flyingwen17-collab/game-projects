using System.IO;
using System.Text;
using UnityEngine;

/// 自動駕駛自我測試：在 Play 模式下用程式操控車輛，量測「車子到底動不動、甩不甩得起來」，
/// 把結果寫成報告檔後結束。
///
/// 存在的理由：離線編譯與批次算圖都抓不到「離合器係數寫成 0 導致車子開不動」這類執行期邏輯錯誤，
/// 只有真的讓車跑一段才驗得出來。
///
/// 由環境變數 DRIFT_SELFTEST=1 觸發，正常遊玩不會啟動。
public class DriveSelfTest : MonoBehaviour
{
    const float AccelPhase = 6f;    // 全油門直線（再長就會撞上彎道護欄）
    const float DriftPhase = 5f;    // 手煞車入彎 + 油門維持
    const float CoastPhase = 2f;    // 放開，看是否回穩
    const float ReversePhase = 3.5f;// 停住後長按 S 倒車

    /// 診斷探針（踢一腳／直接施力／吊到空中）。
    /// 它們已完成任務——證明了問題出在 WheelCollider 的地面接觸——
    /// 但會干擾正常量測（空中實驗會把車從 6m 摔下來），平時一律關閉。
    const bool EnableKickTest = false;
    const bool EnableProbes = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (System.Environment.GetEnvironmentVariable("DRIFT_SELFTEST") != "1") return;
        var go = new GameObject("DriveSelfTest");
        go.AddComponent<DriveSelfTest>();
        DontDestroyOnLoad(go);
    }

    CarController car;
    DriftDetector drift;
    DriftScoring scoring;
    Vector3 startPos;
    float t;
    int phase;

    float maxSpeed, maxSlipAngle, maxRpm, distance;
    float driftSeconds;
    int maxGear = 1;
    bool everGrounded;
    Vector3 reverseStart;
    float reverseDistance;
    bool reachedReverseGear;
    bool nightLightsOn;
    string nightPreset = "";
    NPCDriver[] npcDrivers = new NPCDriver[0];
    Vector3[] npcStartPos = new Vector3[0];
    readonly StringBuilder log = new StringBuilder();
    readonly System.Collections.Generic.List<string> timeline = new System.Collections.Generic.List<string>();

    void Start()
    {
        var gm = FindObjectOfType<GameManager>();
        if (gm != null && gm.cars != null && gm.cars.Length > 0)
        {
            // 選 AE86（後驅、最好甩）；找不到就用第一台
            int idx = 0;
            for (int i = 0; i < gm.carNames.Length && i < gm.cars.Length; i++)
                if (gm.carNames[i].Contains("86")) { idx = i; break; }
            gm.StartRace(idx);
            car = gm.cars[idx];
        }
        if (car == null) car = FindObjectOfType<CarController>();

        if (car == null) { Finish("找不到任何 CarController"); return; }

        car.enabled = true;
        car.useExternalInput = true;
        drift = car.GetComponent<DriftDetector>();
        scoring = car.GetComponent<DriftScoring>();
        spy = car.gameObject.AddComponent<ContactSpy>();
        startPos = car.transform.position;

        // 記錄 NPC 起始位置，最後確認它們真的有在跑
        npcDrivers = FindObjectsOfType<NPCDriver>();
        npcStartPos = new Vector3[npcDrivers.Length];
        for (int i = 0; i < npcDrivers.Length; i++)
            npcStartPos[i] = npcDrivers[i].transform.position;
        log.AppendLine("NPC 數量：" + npcDrivers.Length);

        log.AppendLine("車輛：" + car.spec.displayName);
        log.AppendLine("質量：" + car.spec.massKg + " kg   胎徑：" + car.spec.wheelRadiusM.ToString("0.000") + " m");
        log.AppendLine("理論極速：" + car.TopSpeedKmh.ToString("0") + " km/h");
        log.AppendLine("----");
    }

    void FixedUpdate()
    {
        if (car == null) return;

        t += Time.fixedDeltaTime;

        if (phase == 0)                       // 直線全油門
        {
            car.externalThrottle = 1f;
            car.externalSteer = 0f;
            car.externalHandbrake = false;
            if (EnableKickTest && !kicked && t > 2f) KickTest();
            if (t > AccelPhase) { NextPhase("加速段結束"); }
        }
        else if (phase == 1)                  // 手煞車入彎，之後靠油門維持角度
        {
            car.externalThrottle = 0.8f;
            car.externalSteer = 0.6f;
            car.externalHandbrake = t < 0.7f;
            if (t > DriftPhase) { NextPhase("甩尾段結束"); }
        }
        else if (phase == 2)                  // 收油回穩
        {
            car.externalThrottle = 0f;
            car.externalSteer = 0f;
            car.externalHandbrake = false;
            if (t > CoastPhase) { reverseStart = car.transform.position; NextPhase("收油段結束"); }
        }
        else if (phase == 3)                  // 倒車：持續按 S
        {
            car.externalThrottle = -1f;
            car.externalSteer = 0f;
            car.externalHandbrake = false;

            if (car.IsReverse) reachedReverseGear = true;
            // 只算「往車頭反方向」的位移，避免把打滑亂跑算進來
            float back = -Vector3.Dot(car.transform.position - reverseStart, car.transform.forward);
            if (back > reverseDistance) reverseDistance = back;

            if (t > ReversePhase) { StartCoroutine(NightShotThenEvaluate()); phase = 99; }
        }

        // 對照實驗：t=3~4s 直接對剛體施加 3000N 前向力，繞過整個輪胎模型。
        // 若這樣車就會動 → 問題在我的胎力沒送達；若還是不動 → 剛體被外部東西壓制。
        if (EnableProbes && phase == 0 && t >= 3f && t < 4f)
        {
            var rbc = car.GetComponent<Rigidbody>();
            if (!controlStarted)
            {
                controlStarted = true;
                controlV0 = rbc.velocity.magnitude;
                log.AppendLine($"[對照實驗] 剛體狀態 kinematic={rbc.isKinematic} useGravity={rbc.useGravity} "
                             + $"睡眠={rbc.IsSleeping()} 約束={rbc.constraints} mass={rbc.mass:0} "
                             + $"timeScale={Time.timeScale:0.00} fixedDelta={Time.fixedDeltaTime:0.000}");
            }
            rbc.AddForce(car.transform.forward * 3000f, ForceMode.Force);
            if (!controlForceLogged)
            {
                controlForceLogged = true;
                // 求解器實際收到的累積力 —— 若這裡是 0，代表 AddForce 根本沒被登記
                log.AppendLine($"[對照實驗] AddForce 後累積力 = {rbc.GetAccumulatedForce().magnitude:0} N（施加 3000N）");
            }
        }
        if (EnableProbes && phase == 0 && t >= 4f && !controlLogged)
        {
            controlLogged = true;
            var rbc = car.GetComponent<Rigidbody>();
            float dv = rbc.velocity.magnitude - controlV0;
            log.AppendLine($"[對照實驗] 直接施加 3000N 一秒：速度 {controlV0:0.00} → {rbc.velocity.magnitude:0.00} m/s（理論應 +3.09）"
                         + (dv > 1.5f ? "  → 剛體吃力正常，問題在胎力傳遞" : "  → 剛體不吃力，被外部壓制"));
        }

        // 空中對照實驗：把車吊到 6m 高完全脫離地面接觸後再施力。
        // 空中會加速 = 地面接觸把車釘住；空中也不動 = 剛體本身有問題。
        if (EnableProbes && phase == 0 && t >= 6f && !airLifted)
        {
            airLifted = true;
            car.transform.position += Vector3.up * 6f;
            var rba = car.GetComponent<Rigidbody>();
            rba.velocity = Vector3.zero;
            airV0 = 0f;
        }
        if (EnableProbes && phase == 0 && airLifted && t >= 6.2f && t < 7.2f)
        {
            var rba = car.GetComponent<Rigidbody>();
            if (airV0 == 0f) airV0 = Vector3.Dot(rba.velocity, car.transform.forward);
            rba.AddForce(car.transform.forward * 3000f, ForceMode.Force);
        }
        if (EnableProbes && phase == 0 && airLifted && t >= 7.2f && !airLogged)
        {
            airLogged = true;
            var rba = car.GetComponent<Rigidbody>();
            float fv = Vector3.Dot(rba.velocity, car.transform.forward);
            bool grounded = car.Grounded[0] || car.Grounded[1] || car.Grounded[2] || car.Grounded[3];
            log.AppendLine($"[空中實驗] 離地={!grounded} 高度={car.transform.position.y:0.0}m  "
                         + $"前向速度 {airV0:0.00} → {fv:0.00} m/s（理論應 +3.09）"
                         + (fv - airV0 > 1.5f ? "  → 空中會加速：地面接觸是元兇" : "  → 空中也不動：剛體本身有問題"));
        }

        // 速度時間軸：分辨「完全沒加速」與「加速後被歸零」
        if (phase == 0 && timeline.Count < 24 && t >= timeline.Count * 0.4f)
        {
            var rb0 = car.GetComponent<Rigidbody>();
            timeline.Add($"t={t:0.0}s v={rb0.velocity.magnitude:0.00}m/s 前向={Vector3.Dot(rb0.velocity, car.transform.forward):0.00} ωRL={car.WheelOmega[2]:0.0} 合力={Vector3.Dot(car.LastTotalForce, car.transform.forward):0}N");
        }

        // 取樣
        maxSpeed = Mathf.Max(maxSpeed, car.SpeedKmh);
        maxRpm = Mathf.Max(maxRpm, car.EngineRpm);
        maxGear = Mathf.Max(maxGear, car.Gear);
        distance = Vector3.Distance(startPos, car.transform.position);
        for (int i = 0; i < 4; i++) if (car.Grounded[i]) everGrounded = true;
        if (drift != null)
        {
            maxSlipAngle = Mathf.Max(maxSlipAngle, Mathf.Abs(drift.SlipAngle));
            if (drift.IsDrifting) driftSeconds += Time.fixedDeltaTime;
        }
    }

    void NextPhase(string label)
    {
        log.AppendLine($"[{label}] 速度 {car.SpeedKmh:0.0} km/h  轉速 {car.EngineRpm:0}  檔位 {car.Gear}  位移 {distance:0.0} m");
        log.AppendLine("        " + WheelDump());
        phase++;
        t = 0f;
    }

    /// 每輪的接地、載荷、角速度、滑移 —— 診斷「輪子空轉但車不動」的關鍵資訊
    string WheelDump()
    {
        var sb = new StringBuilder();
        string[] n = { "FL", "FR", "RL", "RR" };
        for (int i = 0; i < 4; i++)
        {
            sb.Append(n[i]).Append(car.Grounded[i] ? "接地" : "懸空");
            sb.Append(" 載荷").Append(car.TireLoad[i].ToString("0"));
            sb.Append("N ω").Append(car.WheelOmega[i].ToString("0.0"));
            sb.Append(" 滑移率").Append(car.SlipRatio[i].ToString("0.00"));
            sb.Append("  ");
        }
        sb.Append("車高 ").Append(car.transform.position.y.ToString("0.00"));
        var rb = car.GetComponent<Rigidbody>();
        sb.Append("  kinematic=").Append(rb.isKinematic);
        sb.Append("  約束=").Append(rb.constraints);
        sb.AppendLine();
        sb.Append("        胎力(縱向N)：");
        for (int i = 0; i < 4; i++) sb.Append(n[i]).Append("=").Append(car.LastTireForce[i].x.ToString("0")).Append(" ");
        sb.Append("  施加合力=").Append(car.LastTotalForce.magnitude.ToString("0")).Append("N");
        sb.Append("  車體前向分量=").Append(Vector3.Dot(car.LastTotalForce, car.transform.forward).ToString("0")).Append("N");
        sb.AppendLine();
        var w = car.wheelRL;
        sb.Append("        剛體 mass=").Append(rb.mass.ToString("0")).Append(" drag=").Append(rb.drag.ToString("0.00"));
        sb.Append("  睡眠=").Append(rb.IsSleeping());
        sb.Append("  ‖ RL WheelCollider: 縱向stiffness=").Append(w.forwardFriction.stiffness.ToString("0.00"));
        sb.Append(" 側向stiffness=").Append(w.sidewaysFriction.stiffness.ToString("0.00"));
        sb.Append(" motorTorque=").Append(w.motorTorque.ToString("0"));
        sb.Append(" brakeTorque=").Append(w.brakeTorque.ToString("0"));
        sb.Append(" wcRPM=").Append(w.rpm.ToString("0"));
        sb.AppendLine();
        sb.Append("        懸吊行程=").Append(w.suspensionDistance.ToString("0.00"));
        sb.Append(" 彈簧=").Append(w.suspensionSpring.spring.ToString("0"));
        sb.Append("  載荷合計=").Append((car.TireLoad[0] + car.TireLoad[1] + car.TireLoad[2] + car.TireLoad[3]).ToString("0"));
        sb.Append("N  車重=").Append((rb.mass * 9.81f).ToString("0")).Append("N");
        sb.Append("  ‖ 碰撞接觸: ").Append(spy != null && spy.hits.Count > 0
                  ? string.Join(" / ", spy.hits) : "無");
        return sb.ToString();
    }

    void LateUpdate()
    {
        // 開場前 0.5 秒取一次樣，看落地瞬間的狀態
        if (!sampledEarly && Time.timeSinceLevelLoad > 0.5f && car != null)
        {
            sampledEarly = true;
            log.AppendLine("[開場 0.5s] " + WheelDump());
        }
    }

    bool sampledEarly;
    bool controlStarted, controlLogged, controlForceLogged;
    bool airLifted, airLogged;
    float airV0;
    float controlV0;
    bool kicked;
    float kickVelAfter = -1f;
    ContactSpy spy;

    /// 掛在車上記錄碰撞對象 —— 用來確認車子是不是被某個靜態碰撞體卡住
    class ContactSpy : MonoBehaviour
    {
        public readonly System.Collections.Generic.HashSet<string> hits = new System.Collections.Generic.HashSet<string>();
        void OnCollisionStay(Collision c) { if (hits.Count < 12) hits.Add(c.collider.name + "(" + c.contactCount + "點)"); }
        void OnCollisionEnter(Collision c) { if (hits.Count < 12) hits.Add(c.collider.name); }
    }

    /// 直接給剛體一個初速，看它保不保得住 —— 保不住就是被外力壓制
    void KickTest()
    {
        var rb = car.GetComponent<Rigidbody>();
        rb.velocity = car.transform.forward * 8f;
        log.AppendLine($"[踢一腳] 設定初速 8 m/s，當下實測 {rb.velocity.magnitude:0.00} m/s");
        kicked = true;
        StartCoroutine(CheckKick(rb));
    }

    System.Collections.IEnumerator CheckKick(Rigidbody rb)
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        kickVelAfter = rb.velocity.magnitude;
        log.AppendLine($"[踢一腳] 兩個物理步之後 {kickVelAfter:0.00} m/s"
                     + (kickVelAfter < 1f ? "  → 速度被吃掉，車子被卡住" : "  → 剛體正常，速度保持"));
        log.AppendLine("[碰撞對象] " + (spy != null && spy.hits.Count > 0
                       ? string.Join(", ", spy.hits) : "無（沒有任何碰撞接觸）"));
    }

    /// 切到夜晚、確認車燈自動亮起，並在 Play 模式下拍一張真實畫面
    /// （編輯器批次算圖拍不到車燈，因為 Light 是執行期才建立的）。
    System.Collections.IEnumerator NightShotThenEvaluate()
    {
        car.externalThrottle = 0f;
        car.externalHandbrake = false;

        var tod = TimeOfDay.Instance;
        var lights = car.GetComponent<CarLights>();

        if (tod != null) { tod.Set(TimeOfDay.Preset.Night); yield return null; }
        yield return new WaitForSeconds(0.6f);   // 等車燈自動開啟與畫面穩定

        nightLightsOn = lights != null && lights.LightsOn;
        nightPreset = tod != null ? tod.Label : "(無時段系統)";
        log.AppendLine($"[夜間] 時段={nightPreset}  車燈自動開啟={nightLightsOn}"
                     + (lights != null ? $"  模式={lights.CurrentMode}" : ""));

        // 注意：yield 不能出現在有 catch 的 try 區塊內，所以拆成「發起 → 等待 → 檢查」三段
        string shotPath = TryStartScreenshot();
        if (shotPath != null)
        {
            yield return new WaitForSeconds(0.8f);
            log.AppendLine("[夜間] 截圖 → " + shotPath + (File.Exists(shotPath) ? "（已產生）" : "（未產生）"));
        }

        Evaluate();
    }

    /// 發起截圖，回傳預期路徑；失敗回 null。
    string TryStartScreenshot()
    {
        string dir = System.Environment.GetEnvironmentVariable("DRIFT_SHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return null;
        try
        {
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, "play_night.png");
            if (File.Exists(p)) File.Delete(p);
            ScreenCapture.CaptureScreenshot(p);
            return p;
        }
        catch (System.Exception e)
        {
            log.AppendLine("[夜間] 截圖失敗：" + e.Message);
            return null;
        }
    }

    void Evaluate()
    {
        log.AppendLine($"[倒車段結束] 檔位 {car.GearLabel}  後退距離 {reverseDistance:0.0} m");
        log.AppendLine("---- 加速段速度時間軸 ----");
        foreach (var line in timeline) log.AppendLine("  " + line);
        log.AppendLine("----");
        log.AppendLine($"最高速度      {maxSpeed:0.0} km/h");
        log.AppendLine($"最高轉速      {maxRpm:0} rpm（紅線 {car.spec.redlineRpm:0}）");
        log.AppendLine($"最高檔位      {maxGear} / {car.spec.gearRatios.Length}");
        log.AppendLine($"總位移        {distance:0.0} m");
        log.AppendLine($"最大滑移角    {maxSlipAngle:0.0}°");
        log.AppendLine($"甩尾累計時間  {driftSeconds:0.00} s");
        log.AppendLine($"曾經接地      {everGrounded}");
        if (scoring != null)
            log.AppendLine($"甩尾分數      {scoring.TotalScore + scoring.PendingScore:0}");
        log.AppendLine("----");

        // ---- 判定 ----
        int fail = 0;
        fail += Check(everGrounded, "輪胎有接地");
        fail += Check(distance > 55f, $"能從靜止開動並前進（實際 {distance:0.0} m，需 > 55 m）");
        fail += Check(maxSpeed > 45f, $"能加速到合理速度（實際 {maxSpeed:0.0} km/h，需 > 45）");
        fail += Check(maxGear >= 2, $"變速箱能升檔（最高 {maxGear} 檔）");
        fail += Check(maxSlipAngle > 15f, $"手煞車能讓車尾滑出（最大滑移角 {maxSlipAngle:0.0}°，需 > 15°）");
        fail += Check(driftSeconds > 0.25f, $"甩尾判定有觸發（{driftSeconds:0.00} s，需 > 0.25）");
        fail += Check(maxSpeed < car.TopSpeedKmh * 1.25f, $"速度沒有爆衝失控（{maxSpeed:0.0} < {car.TopSpeedKmh * 1.25f:0.0}）");
        fail += Check(reachedReverseGear, "長按煞車鍵能進入倒檔 R");
        fail += Check(reverseDistance > 3f, $"倒檔能實際往後開（後退 {reverseDistance:0.0} m，需 > 3 m）");
        fail += Check(nightPreset == "夜晚", $"時段能切換到夜晚（實際 {nightPreset}）");
        fail += Check(nightLightsOn, "天黑時車頭燈自動開啟");

        // NPC：確認它們真的在跑，而不是原地不動或卡住
        float npcMoved = 0f;
        int npcMovers = 0;
        for (int i = 0; i < npcDrivers.Length; i++)
        {
            if (npcDrivers[i] == null) continue;
            float d = Vector3.Distance(npcDrivers[i].transform.position, npcStartPos[i]);
            npcMoved += d;
            if (d > 30f) npcMovers++;
        }
        float npcAvg = npcDrivers.Length > 0 ? npcMoved / npcDrivers.Length : 0f;
        log.AppendLine($"NPC 平均移動 {npcAvg:0.0} m，其中 {npcMovers}/{npcDrivers.Length} 台跑超過 30 m");

        fail += Check(npcDrivers.Length >= 4, $"場上有足夠的 NPC（{npcDrivers.Length} 台）");
        fail += Check(npcMovers >= npcDrivers.Length / 2,
                      $"至少一半 NPC 有正常行駛（{npcMovers}/{npcDrivers.Length}）");

        Finish(fail == 0 ? "全部通過" : fail + " 項未通過");
    }

    int Check(bool ok, string desc)
    {
        log.AppendLine((ok ? "  [通過] " : "  [失敗] ") + desc);
        return ok ? 0 : 1;
    }

    void Finish(string verdict)
    {
        log.AppendLine("結論：" + verdict);
        string text = log.ToString();
        Debug.Log("[DriveSelfTest]\n" + text);

        string outPath = System.Environment.GetEnvironmentVariable("DRIFT_SELFTEST_OUT");
        if (string.IsNullOrEmpty(outPath))
            outPath = Path.Combine(Application.dataPath, "../selftest_report.txt");
        try { File.WriteAllText(outPath, text, Encoding.UTF8); }
        catch (System.Exception e) { Debug.LogWarning("[DriveSelfTest] 報告寫入失敗：" + e.Message); }

        enabled = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(verdict == "全部通過" ? 0 : 2);
#else
        Application.Quit(verdict == "全部通過" ? 0 : 2);
#endif
    }
}
