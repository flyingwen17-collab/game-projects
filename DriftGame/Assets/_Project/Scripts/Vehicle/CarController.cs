using UnityEngine;
using UnityEngine.InputSystem;

public enum Drivetrain { FWD, RWD, AWD }

/// 車輛物理主控 —— 真實模型版。
///
/// 設計要點（和舊版最大的差異）：
/// WheelCollider 只保留「懸吊 + 接地偵測 + 垂直載荷」，它內建的摩擦被歸零，
/// 所有胎面力改由 TireModel 的 Pacejka 魔術公式自算，再以 AddForceAtPosition 施加。
///
/// 為什麼這樣才有甩尾感：
///  1. 胎力有峰值 —— 滑移角超過峰值後抓地力「掉下來」，車尾滑出去，必須反打救車。
///  2. 摩擦橢圓 —— 縱向與側向共用抓地力預算，所以能用油門控制甩尾角度。
///  3. 載荷敏感 —— 重量轉移會實際改變各輪抓地力，煞車轉向、收油轉向都成立。
///  4. 手煞車鎖後輪 → 滑移率飽和 → 吃光側向力預算 → 後軸失去抓地。
///     這是物理推導出來的，不是寫死的參數。
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(DriftDetector))]
public class CarController : MonoBehaviour
{
    [Header("Wheels（順序：前左 前右 後左 後右）")]
    public WheelCollider wheelFL;
    public WheelCollider wheelFR;
    public WheelCollider wheelRL;
    public WheelCollider wheelRR;

    [Header("規格（真實車輛數據）")]
    public CarSpec spec = new CarSpec();

    [Header("輔助（0 = 純物理）")]
    [Range(0f, 1f)] public float counterSteerAssist = 0.35f;  // 甩尾中自動補一點反打
    [Range(0f, 1f)] public float stabilityAssist = 0.15f;     // 抑制原地打轉
    public bool autoShift = true;

    // ---------------- 對外狀態 ----------------
    public float SpeedKmh { get; private set; }
    public bool Handbrake { get; private set; }
    public float Throttle01 => Mathf.Max(0f, throttleInput);
    public bool IsBraking { get; private set; }
    public float EngineRpm { get; private set; }
    /// 0 = 倒檔 R，1..n = 前進檔
    public int Gear { get; private set; } = 1;
    public bool IsReverse => Gear == 0;
    /// 檔位顯示字串（R / 1 / 2 …）
    public string GearLabel => Gear == 0 ? "R" : Gear.ToString();
    /// 這一幀是否觸發回火（供音效與排氣火焰使用）
    public bool BackfireTriggered { get; private set; }
    public bool ShiftedThisFrame { get; private set; }
    public float SteerAngleDeg { get; private set; }
    public float ClutchEngage { get; private set; } = 1f;

    /// 各輪狀態（0=FL 1=FR 2=RL 3=RR），特效與音效用
    public float[] SlipRatio = new float[4];
    public float[] SlipAngleDeg = new float[4];
    public float[] TireLoad = new float[4];
    public float[] WheelOmega = new float[4];
    public bool[] Grounded = new bool[4];
    /// 該輪的胎力用掉多少抓地力預算（0..1），>0.95 就是在打滑
    public float[] GripUsage = new float[4];
    /// 該輪腳下路面的抓地係數（1=乾柏油、0.55=草地泥土），特效也讀這個決定揚塵顏色
    public float[] SurfaceMu = { 1f, 1f, 1f, 1f };

    /// 各輪實際接地點與路面法線（特效用：胎痕要畫在這裡，不是輪心正下方）
    [System.NonSerialized] public Vector3[] ContactPoint = new Vector3[4];
    [System.NonSerialized] public Vector3[] ContactNormal = new Vector3[4];

    /// 診斷用：實際算出並施加到剛體的胎面力（車輛座標，x=縱向 y=側向）
    [System.NonSerialized] public Vector2[] LastTireForce = new Vector2[4];
    /// 診斷用：本次 FixedUpdate 施加的合力（世界座標）
    [System.NonSerialized] public Vector3 LastTotalForce;

    public float TopSpeedKmh
    {
        get
        {
            if (spec.gearRatios == null || spec.gearRatios.Length == 0) return 180f;
            float topGear = spec.gearRatios[spec.gearRatios.Length - 1];
            float omega = spec.redlineRpm * 2f * Mathf.PI / 60f / (topGear * spec.finalDrive);
            return omega * spec.wheelRadiusM * 3.6f;
        }
    }

    // ---------------- 內部 ----------------
    /// 輪胎的世界座標姿態（射線懸吊自算，供 CarVisuals 使用）
    [System.NonSerialized] public Vector3[] WheelWorldPos = new Vector3[4];
    [System.NonSerialized] public Quaternion[] WheelWorldRot = new Quaternion[4];

    [Header("接地偵測")]
    public LayerMask groundMask = ~0;

    Rigidbody rb;
    DriftDetector drift;
    WheelCollider[] wheels;
    Transform[] anchors;
    float springRate, damperRate;
    readonly float[] suspensionDrop = new float[4];   // 輪心相對掛點下沉量

    float steerInput, throttleInput;
    bool handbrakeInput;
    float steerCurrent;
    float shiftCooldown;
    float reverseHoldTimer;
    float lastDriveInput;
    Vector3 spawnPos;
    Quaternion spawnRot;

    const int SubSteps = 4;   // 輪胎角速度積分的子步數，避免高扭力下數值爆掉

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        drift = GetComponent<DriftDetector>();
        wheels = new[] { wheelFL, wheelFR, wheelRL, wheelRR };

        rb.mass = spec.massKg;
        rb.centerOfMass = new Vector3(0f, spec.cgHeightM - 0.85f, (spec.frontWeightRatio - 0.5f) * spec.wheelbaseM);
        rb.maxAngularVelocity = 8f;

        // 高速物體必開連續碰撞偵測：150km/h = 每個物理步 0.42m，
        // 離散偵測整個步距直接跳過 0.4m 的護欄 —— 這就是「穿牆」的來源。
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // WheelCollider 完全退出物理計算，只留它的 Transform 當輪胎掛點。
        //
        // 為什麼不用它：即使把摩擦 stiffness 歸零，它的輪子仍是實體碰撞體。
        // 當懸吊撐不住車重而觸底時，PhysX 會用剛性接觸補足支撐力，
        // 那個接觸的摩擦會把靜止的車完全鎖死 —— 施加數千牛頓也不動，
        // 而且它不觸發 OnCollisionEnter/Stay，從碰撞回呼完全看不到。
        // 改用射線懸吊後，接地、載荷、胎力全部由我們自己算，行為完全可控。
        anchors = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            var w = wheels[i];
            if (w == null) continue;
            anchors[i] = w.transform;
            w.enabled = false;
        }

        springRate = Mathf.Max(spec.springRate, spec.massKg * 9.81f / 4f / (spec.suspensionTravelM * 0.35f));
        damperRate = springRate * 0.13f;

        EngineRpm = spec.idleRpm;
        spawnPos = transform.position;
        spawnRot = transform.rotation;
    }

    void Update()
    {
        ReadInput();
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetCar();
    }

    /// 起跑倒數時鎖住操作（含 AI），紅燈熄滅才放行
    [System.NonSerialized] public bool controlsLocked;

    // ---- 自動化測試用的輸入注入（正常遊玩時 useExternalInput 為 false）----
    [System.NonSerialized] public bool useExternalInput;
    [System.NonSerialized] public float externalSteer;
    [System.NonSerialized] public float externalThrottle;
    [System.NonSerialized] public bool externalHandbrake;

    void ReadInput()
    {
        if (controlsLocked)
        {
            // 起跑倒數：油門轉向全鎖、手煞車拉住（可以催轉速的是引擎不是車輪）
            steerInput = 0f; throttleInput = 0f;
            handbrakeInput = true; Handbrake = true;
            return;
        }

        if (useExternalInput)
        {
            steerInput = Mathf.Clamp(externalSteer, -1f, 1f);
            throttleInput = Mathf.Clamp(externalThrottle, -1f, 1f);
            handbrakeInput = externalHandbrake;
            Handbrake = externalHandbrake;
            return;
        }

        float steer = 0f, throttle = 0f;
        bool hb = false;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1f;
            hb = kb.spaceKey.isPressed;

            // 手排：Q 升檔、E 降檔。按下任一鍵就自動關閉自排。
            if (kb.qKey.wasPressedThisFrame) { autoShift = false; ShiftUp(); }
            if (kb.eKey.wasPressedThisFrame) { autoShift = false; ShiftDown(); }
            if (kb.tKey.wasPressedThisFrame) autoShift = !autoShift;   // T 切換自排/手排
        }

        var pad = Gamepad.current;
        if (pad != null)
        {
            float padSteer = pad.leftStick.x.ReadValue();
            if (Mathf.Abs(padSteer) > 0.08f) steer = padSteer;
            float padThrottle = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            if (Mathf.Abs(padThrottle) > 0.04f) throttle = padThrottle;
            hb |= pad.buttonSouth.isPressed;
        }

        steerInput = Mathf.Clamp(steer, -1f, 1f);
        throttleInput = Mathf.Clamp(throttle, -1f, 1f);
        handbrakeInput = hb;
        Handbrake = hb;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 vel = rb.linearVelocity;
        SpeedKmh = vel.magnitude * 3.6f;
        LastTotalForce = Vector3.zero;
        ShiftedThisFrame = false;

        UpdateSteering(dt);

        // ---- 收集各輪接地資訊 ----
        var contact = new Vector3[4];
        var wheelFwd = new Vector3[4];
        var wheelRight = new Vector3[4];
        var normal = new Vector3[4];

        Quaternion steerRot = Quaternion.AngleAxis(SteerAngleDeg, transform.up);

        float travel = spec.suspensionTravelM;
        float radius = spec.wheelRadiusM;

        for (int i = 0; i < 4; i++)
        {
            var anchor = anchors != null ? anchors[i] : null;
            if (anchor == null) { Grounded[i] = false; TireLoad[i] = 0f; continue; }

            Vector3 up = transform.up;
            Vector3 origin = anchor.position;

            // 射線懸吊：從掛點往下打，長度 = 行程 + 胎半徑
            Grounded[i] = Physics.Raycast(origin, -up, out RaycastHit hit, travel + radius,
                                          groundMask, QueryTriggerInteraction.Ignore)
                          && hit.rigidbody != rb;

            if (!Grounded[i])
            {
                TireLoad[i] = 0f; SlipRatio[i] = 0f; SlipAngleDeg[i] = 0f; GripUsage[i] = 0f;
                SurfaceMu[i] = 1f;
                suspensionDrop[i] = travel;                       // 完全伸長
                WheelWorldPos[i] = origin - up * travel;
                WheelWorldRot[i] = anchor.rotation;
                ContactPoint[i] = WheelWorldPos[i] - up * radius;
                ContactNormal[i] = up;
                continue;
            }

            SurfaceMu[i] = SurfaceGripOf(hit);
            float drop = Mathf.Clamp(hit.distance - radius, 0f, travel);
            float compression = travel - drop;
            suspensionDrop[i] = drop;

            // 彈簧 + 阻尼，阻尼取掛點沿懸吊軸的速度
            float axisVel = Vector3.Dot(rb.GetPointVelocity(origin), up);
            float load = springRate * compression - damperRate * axisVel;
            load = Mathf.Clamp(load, 0f, springRate * travel * 2.5f);

            TireLoad[i] = load;
            contact[i] = hit.point;
            normal[i] = hit.normal;
            ContactPoint[i] = hit.point;
            ContactNormal[i] = hit.normal;

            // 懸吊力沿地面法線施加在掛點上
            rb.AddForceAtPosition(hit.normal * load, origin);

            bool isFront = i < 2;
            WheelWorldPos[i] = origin - up * drop;
            WheelWorldRot[i] = isFront ? steerRot * anchor.rotation : anchor.rotation;
            Vector3 fwd = isFront ? steerRot * transform.forward : transform.forward;
            Vector3 rgt = isFront ? steerRot * transform.right : transform.right;

            // 投影到接觸平面
            wheelFwd[i] = Vector3.ProjectOnPlane(fwd, normal[i]).normalized;
            wheelRight[i] = Vector3.ProjectOnPlane(rgt, normal[i]).normalized;

            Vector3 pv = rb.GetPointVelocity(contact[i]);
            Vector3 planarV = Vector3.ProjectOnPlane(pv, normal[i]);
            float vLong = Vector3.Dot(planarV, wheelFwd[i]);
            float vLat = Vector3.Dot(planarV, wheelRight[i]);

            // 滾動阻力：柏油輕微；草地泥土會實際把車拖慢（衝出賽道的懲罰來自物理，不是扣分）
            float rollRes = SurfaceMu[i] < 0.8f ? 0.055f : 0.012f;
            if (planarV.sqrMagnitude > 0.2f)
                rb.AddForceAtPosition(-planarV.normalized * load * rollRes, contact[i]);

            // 滑移角：胎面朝向與實際行進方向的夾角
            SlipAngleDeg[i] = Mathf.Atan2(vLat, Mathf.Max(Mathf.Abs(vLong), 0.6f)) * Mathf.Rad2Deg;

            // 滑移率：輪速 vs 地速
            float wheelSurfaceSpeed = WheelOmega[i] * spec.wheelRadiusM;
            SlipRatio[i] = (wheelSurfaceSpeed - vLong) / Mathf.Max(Mathf.Abs(vLong), 1.2f);
            SlipRatio[i] = Mathf.Clamp(SlipRatio[i], -4f, 4f);
        }

        UpdateEngineAndGears(dt);

        float driveTorque = CurrentDriveTorquePerAxle();
        float frontAxleTorque = 0f, rearAxleTorque = 0f;
        switch (spec.drivetrain)
        {
            case Drivetrain.FWD: frontAxleTorque = driveTorque; break;
            case Drivetrain.RWD: rearAxleTorque = driveTorque; break;
            case Drivetrain.AWD:
                frontAxleTorque = driveTorque * spec.awdFrontShare;
                rearAxleTorque = driveTorque * (1f - spec.awdFrontShare);
                break;
        }

        ApplyAxle(0, 1, frontAxleTorque, true, dt, contact, wheelFwd, wheelRight, normal);
        ApplyAxle(2, 3, rearAxleTorque, false, dt, contact, wheelFwd, wheelRight, normal);

        ApplyAero(vel);
        ApplyAntiRoll(0, 1);
        ApplyAntiRoll(2, 3);
        ApplyAssists();
    }

    // ---------------- 轉向 ----------------

    void UpdateSteering(float dt)
    {
        // 速度感應：高速時縮小最大轉向角，避免一拉就打轉（實車轉向機構不變，
        // 但這裡模擬駕駛不會在高速全打方向盤）
        float speedT = Mathf.InverseLerp(15f, 130f, SpeedKmh);
        float maxSteer = spec.maxSteerDeg * Mathf.Lerp(1f, spec.highSpeedSteerLimit, speedT);

        float target = steerInput * maxSteer;

        // 甩尾中的反打輔助：往滑移的反方向補一點，降低門檻但不接管
        if (counterSteerAssist > 0f && drift != null && drift.IsDrifting && SpeedKmh > 25f)
        {
            float desired = Mathf.Clamp(-drift.SlipAngle, -maxSteer, maxSteer);
            target = Mathf.Lerp(target, desired, counterSteerAssist * (1f - Mathf.Abs(steerInput) * 0.7f));
        }

        float rate = (Mathf.Abs(target) > Mathf.Abs(steerCurrent) ? spec.steerSpeedDegPerSec : spec.steerReturnDegPerSec);
        steerCurrent = Mathf.MoveTowards(steerCurrent, target, rate * dt);
        SteerAngleDeg = steerCurrent;

        // 轉向角只影響前輪的視覺姿態與胎面方向（WheelCollider 已停用）
    }

    // ---------------- 引擎與變速箱 ----------------

    /// 目前檔位的總傳動比（含終傳）。倒檔為負值，驅動力方向自然反轉。
    float TotalRatio()
    {
        if (spec.gearRatios == null || spec.gearRatios.Length == 0) return spec.finalDrive;
        if (Gear == 0) return -spec.reverseRatio * spec.finalDrive;
        return spec.gearRatios[Mathf.Clamp(Gear, 1, spec.gearRatios.Length) - 1] * spec.finalDrive;
    }

    /// 倒檔時 S 是油門、W 是煞車；前進檔則相反。
    float DriveInput => Gear == 0 ? Mathf.Max(0f, -throttleInput) : Mathf.Max(0f, throttleInput);
    float BrakeInput => Gear == 0 ? Mathf.Max(0f, throttleInput) : Mathf.Max(0f, -throttleInput);

    void UpdateEngineAndGears(float dt)
    {
        if (spec.gearRatios == null || spec.gearRatios.Length == 0) return;
        Gear = Mathf.Clamp(Gear, 0, spec.gearRatios.Length);

        UpdateReverseEngagement(dt);

        // 由驅動輪轉速回推引擎轉速（用絕對值，倒檔轉速也是正的）
        float avgOmega = DrivenWheelOmega();
        float ratio = Mathf.Abs(TotalRatio());
        float wheelDrivenRpm = Mathf.Abs(avgOmega) * ratio * 60f / (2f * Mathf.PI);

        // 離合器：平時接合，只有換檔瞬間斷開。
        // 起步時離合器「打滑但仍傳遞扭力」——這正是離合器存在的意義。
        // （早期版本把低速的 ClutchEngage 設成 0，等於靜止時完全不給扭力，車子永遠開不動。）
        ClutchEngage = 1f;
        if (shiftCooldown > 0f)
        {
            shiftCooldown -= dt;
            ClutchEngage = 0.08f;
        }

        // 低速時允許引擎轉速高於輪速反推值 —— 就是踩油門讓離合器打滑起步
        float slipAllowance = 1f - Mathf.Clamp01(SpeedKmh / 22f);
        float launchRpm = Mathf.Lerp(spec.idleRpm, spec.peakTorqueRpm * 1.05f, DriveInput) * slipAllowance;
        float targetRpm = Mathf.Max(wheelDrivenRpm, launchRpm, spec.idleRpm);

        float prevRpm = EngineRpm;
        EngineRpm = Mathf.Lerp(EngineRpm, Mathf.Min(targetRpm, spec.redlineRpm * 1.02f), 10f * dt);

        // 空油門時引擎自然回落
        if (DriveInput < 0.05f)
            EngineRpm = Mathf.Lerp(EngineRpm, Mathf.Max(spec.idleRpm, wheelDrivenRpm), 5f * dt);

        // 回火：高轉速下收油，未燃燒的混合氣在排氣管中爆燃
        BackfireTriggered = false;
        if (prevRpm > spec.peakTorqueRpm * 0.75f && DriveInput < 0.05f && lastDriveInput > 0.5f)
            BackfireTriggered = true;
        lastDriveInput = DriveInput;

        // 自排只在前進檔作用，倒檔不換
        if (autoShift && shiftCooldown <= 0f && Gear >= 1)
        {
            if (EngineRpm > spec.redlineRpm * 0.94f && Gear < spec.gearRatios.Length && DriveInput > 0.1f)
                ShiftUp();
            else if (EngineRpm < spec.peakTorqueRpm * 0.52f && Gear > 1)
                ShiftDown();
        }
    }

    /// 停下來後持續按煞車鍵就進倒檔；倒檔中按前進鍵且幾乎停住就回一檔。
    void UpdateReverseEngagement(float dt)
    {
        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward) * 3.6f;

        if (Gear >= 1)
        {
            bool wantsReverse = throttleInput < -0.5f && forwardSpeed < 1.5f;
            reverseHoldTimer = wantsReverse ? reverseHoldTimer + dt : 0f;
            if (reverseHoldTimer > 0.35f && Gear == 1)
            {
                Gear = 0;
                shiftCooldown = 0.15f;
                reverseHoldTimer = 0f;
            }
        }
        else // 倒檔中
        {
            bool wantsForward = throttleInput > 0.5f && forwardSpeed > -1.5f;
            reverseHoldTimer = wantsForward ? reverseHoldTimer + dt : 0f;
            if (reverseHoldTimer > 0.3f)
            {
                Gear = 1;
                shiftCooldown = 0.15f;
                reverseHoldTimer = 0f;
            }
        }
    }

    // 換檔頓挫要短。過長會讓驅動力頻繁中斷，加速變得斷斷續續。
    void ShiftUp()
    {
        if (spec.gearRatios == null || Gear >= spec.gearRatios.Length) return;
        // 高轉升檔會放砲
        if (EngineRpm > spec.peakTorqueRpm * 0.8f) BackfireTriggered = true;
        Gear++;
        shiftCooldown = 0.12f;
        ShiftedThisFrame = true;
    }

    /// 降檔。一檔再降就進倒檔（需幾乎停住，避免高速誤入倒檔打爆變速箱）。
    void ShiftDown()
    {
        if (Gear <= 0) return;
        if (Gear == 1)
        {
            float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward) * 3.6f;
            if (forwardSpeed > 3f) return;
            Gear = 0;
        }
        else
        {
            Gear--;
            // 降檔補油同樣容易放砲
            if (EngineRpm > spec.peakTorqueRpm * 0.6f) BackfireTriggered = true;
        }
        shiftCooldown = 0.10f;
        ShiftedThisFrame = true;
    }

    float DrivenWheelOmega()
    {
        switch (spec.drivetrain)
        {
            case Drivetrain.FWD: return (WheelOmega[0] + WheelOmega[1]) * 0.5f;
            case Drivetrain.RWD: return (WheelOmega[2] + WheelOmega[3]) * 0.5f;
            default: return (WheelOmega[0] + WheelOmega[1] + WheelOmega[2] + WheelOmega[3]) * 0.25f;
        }
    }

    /// 引擎輸出經齒比放大後、送到「一條軸」的總扭力。倒檔時 ratio 為負，扭力自然反向。
    float CurrentDriveTorquePerAxle()
    {
        if (spec.gearRatios == null || spec.gearRatios.Length == 0) return 0f;
        float ratio = TotalRatio();

        float throttle = DriveInput;

        // 倒檔限速：實車倒檔齒比高、拉不快
        if (Gear == 0)
        {
            float reverseSpeed = -Vector3.Dot(rb.linearVelocity, transform.forward) * 3.6f;
            if (reverseSpeed > spec.reverseTopSpeedKmh) throttle = 0f;
        }

        float engineT = spec.TorqueAt(EngineRpm) * throttle;

        // 放開油門的引擎煞車
        if (throttle < 0.05f)
            engineT = -spec.engineBrakeNm * Mathf.InverseLerp(spec.idleRpm, spec.redlineRpm, EngineRpm);

        return engineT * ratio * spec.drivetrainEfficiency * ClutchEngage;
    }

    // ---------------- 單軸：差速、輪速積分、胎力 ----------------

    void ApplyAxle(int li, int ri, float axleTorque, bool isFront, float dt,
                   Vector3[] contact, Vector3[] fwd, Vector3[] right, Vector3[] normal)
    {
        // LSD：鎖定率越高，左右輪轉速被拉得越近
        float avg = (WheelOmega[li] + WheelOmega[ri]) * 0.5f;
        float lockTorque = spec.diffLock * 900f;

        float baseMu = isFront ? spec.tireGripFront : spec.tireGripRear;
        float brakeInput = BrakeInput;
        IsBraking = brakeInput > 0.05f && Mathf.Abs(Vector3.Dot(rb.linearVelocity, transform.forward)) > 1f;

        float brakeT = brakeInput * spec.brakeTorqueNm *
                       (isFront ? spec.brakeBiasFront : 1f - spec.brakeBiasFront) * 2f;
        if (!isFront && handbrakeInput) brakeT += spec.handbrakeTorqueNm;

        // 手煞車時後軸不出力
        float driveT = (!isFront && handbrakeInput) ? 0f : axleTorque * 0.5f;

        int[] idx = { li, ri };
        float sub = dt / SubSteps;

        for (int k = 0; k < idx.Length; k++)
        {
            int i = idx[k];
            // 路面材質係數：柏油 1.0、路緣石略滑、草地泥土只剩一半多的抓地
            float mu = baseMu * SurfaceMu[i];
            float longForce = 0f;

            for (int s = 0; s < SubSteps; s++)
            {
                // 每個子步都用當下的接地速度重算滑移率，鎖死與空轉才收斂得了
                float vLong = 0f;
                if (Grounded[i])
                {
                    Vector3 pv = rb.GetPointVelocity(contact[i]);
                    vLong = Vector3.Dot(Vector3.ProjectOnPlane(pv, normal[i]), fwd[i]);
                }
                float vRef = Mathf.Max(Mathf.Abs(vLong), 1.2f);

                float fx = 0f, peak = 0f;
                if (Grounded[i] && TireLoad[i] > 1f)
                {
                    peak = TireModel.PeakForce(TireLoad[i], mu, spec.loadSensitivity);
                    SlipRatio[i] = Mathf.Clamp(
                        (WheelOmega[i] * spec.wheelRadiusM - vLong) / vRef, -4f, 4f);
                    fx = TireModel.Compute(SlipAngleDeg[i] * Mathf.Deg2Rad, SlipRatio[i],
                                           TireLoad[i], mu, spec.loadSensitivity).x;
                }

                // 輪胎角速度：Iw·dω/dt = 驅動 + 差速耦合 − 地面反作用
                float net = driveT + spec.diffLock * (avg - WheelOmega[i]) * lockTorque * 0.001f
                            - fx * spec.wheelRadiusM;

                // 半隱式積分：把胎力對輪速的線性化斜率放進「等效慣量」。
                //
                //   I·Δω/dt = T − Fx(ω)·r − k·r·Δω        （k = dFx/dω）
                //   ⇒ Δω = (T − Fx·r)·dt / (I + k·r·dt)
                //
                // 低速時 k 極大，顯式積分的特徵值會遠大於 1 而發散（輪速正負爆震、胎力互相抵銷）。
                // 注意只能阻尼「增量 Δω」；若寫成 (ω + …)/(1+damp) 會連 ω 本身一起衰減，
                // 輪子被強制拉停 → 滑移率變 −1 → 胎力變成煞車力把整台車拖住。
                float dFxdOmega = TireModel.LongSlopeAt(SlipRatio[i], peak) * spec.wheelRadiusM / vRef;
                float effectiveInertia = spec.wheelInertia + Mathf.Max(0f, dFxdOmega * spec.wheelRadiusM * sub);

                float newOmega = WheelOmega[i] + net * sub / effectiveInertia;

                // 煞車力矩永遠對抗當前轉向，且不得把輪子反轉
                float brakeDelta = brakeT * sub / spec.wheelInertia;
                if (Mathf.Abs(newOmega) <= brakeDelta) newOmega = 0f;
                else newOmega -= Mathf.Sign(newOmega) * brakeDelta;

                WheelOmega[i] = Mathf.Clamp(newOmega, -400f, 400f);
                longForce = fx;
            }

            if (!Grounded[i] || TireLoad[i] <= 1f) { GripUsage[i] = 0f; LastTireForce[i] = Vector2.zero; continue; }

            var force = TireModel.Compute(SlipAngleDeg[i] * Mathf.Deg2Rad, SlipRatio[i],
                                          TireLoad[i], mu, spec.loadSensitivity);
            float peakForce = TireModel.PeakForce(TireLoad[i], mu, spec.loadSensitivity);
            GripUsage[i] = peakForce > 1f ? Mathf.Clamp01(force.magnitude / peakForce) : 0f;
            LastTireForce[i] = force;

            Vector3 worldForce = fwd[i] * force.x + right[i] * force.y;
            rb.AddForceAtPosition(worldForce, contact[i]);
            LastTotalForce += worldForce;
        }
    }

    /// 由射線命中的物件判斷腳下路面。場景生成器對物件的命名就是路面類型的約定：
    /// Road/StartLine=柏油、Kerb=路緣石、Sidewalk=人行道、Grass=草地、Ground=市區鋪面。
    static float SurfaceGripOf(RaycastHit hit)
    {
        string n = hit.collider.gameObject.name;
        if (n.StartsWith("Road") || n.StartsWith("StartLine")) return 1f;
        if (n.StartsWith("Kerb")) return 0.92f;
        if (n.StartsWith("Sidewalk")) return 0.95f;
        if (n.StartsWith("Grass")) return 0.55f;
        if (n.StartsWith("Ground")) return 0.82f;
        return 0.8f;
    }

    // ---------------- 空力、防傾桿、輔助 ----------------

    void ApplyAero(Vector3 vel)
    {
        float v2 = vel.sqrMagnitude;
        if (v2 < 0.01f) return;
        // 空氣阻力 F = 0.5·ρ·Cd·A·v²，ρ 取 1.225
        rb.AddForce(-vel.normalized * 0.5f * 1.225f * spec.dragArea * v2);
        // 下壓力（隨速度平方），高速更穩
        rb.AddForce(-transform.up * spec.downforceCoef * v2 * 0.5f);
    }

    /// 防傾桿：左右輪懸吊行程差越大，反抗力矩越大（抑制側傾）
    void ApplyAntiRoll(int li, int ri)
    {
        if (anchors == null || anchors[li] == null || anchors[ri] == null) return;

        float travel = Mathf.Max(0.01f, spec.suspensionTravelM);
        float ratioL = suspensionDrop[li] / travel;   // 0=完全壓縮 1=完全伸長
        float ratioR = suspensionDrop[ri] / travel;

        float force = (ratioL - ratioR) * spec.antiRollBarNm;
        if (Grounded[li]) rb.AddForceAtPosition(transform.up * -force, anchors[li].position);
        if (Grounded[ri]) rb.AddForceAtPosition(transform.up * force, anchors[ri].position);
    }

    void ApplyAssists()
    {
        if (stabilityAssist <= 0f || SpeedKmh < 8f) return;
        // 只抑制「原地打轉」等級的偏航，不干涉正常甩尾
        float yaw = Vector3.Dot(rb.angularVelocity, transform.up);
        float excess = Mathf.Abs(yaw) - 1.6f;
        if (excess > 0f)
            rb.AddTorque(-transform.up * Mathf.Sign(yaw) * excess * stabilityAssist * rb.mass * 0.6f);
    }

    // ---------------- 重置 ----------------

    public void ResetCar()
    {
        float yaw = transform.eulerAngles.y;
        transform.SetPositionAndRotation(transform.position + Vector3.up * 1.2f, Quaternion.Euler(0f, yaw, 0f));
        ZeroMotion();
    }

    public void RespawnAtStart()
    {
        transform.SetPositionAndRotation(spawnPos, spawnRot);
        ZeroMotion();
    }

    /// 移到起跑格（並把這裡設為之後 RespawnAtStart 的家）
    public void TeleportTo(Vector3 pos, Quaternion rot)
    {
        transform.SetPositionAndRotation(pos, rot);
        spawnPos = pos;
        spawnRot = rot;
        ZeroMotion();
    }

    void ZeroMotion()
    {
        // 未選中的車是 kinematic，對 kinematic 剛體寫 velocity 會噴警告
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        for (int i = 0; i < 4; i++) { WheelOmega[i] = 0f; SlipRatio[i] = 0f; SlipAngleDeg[i] = 0f; GripUsage[i] = 0f; }
        EngineRpm = spec.idleRpm;
        Gear = 1;
        steerCurrent = 0f;
    }

    void OnDisable()
    {
        for (int i = 0; i < 4; i++) WheelOmega[i] = 0f;

        // 摩擦已歸零，WheelCollider 的 brakeTorque 產生不了任何力，
        // 未選中的車必須直接凍結，否則會在斜面上無摩擦滑走。
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            // kinematic 剛體只支援 Speculative 連續偵測，先降級再凍結才不噴警告
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;
        }

    }

    void OnEnable()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }
}
