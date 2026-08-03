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
    public int Gear { get; private set; } = 1;          // 1 起始，0 保留給空檔
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

    // ---- 自動化測試用的輸入注入（正常遊玩時 useExternalInput 為 false）----
    [System.NonSerialized] public bool useExternalInput;
    [System.NonSerialized] public float externalSteer;
    [System.NonSerialized] public float externalThrottle;
    [System.NonSerialized] public bool externalHandbrake;

    void ReadInput()
    {
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

            if (!autoShift)
            {
                if (kb.eKey.wasPressedThisFrame) ShiftUp();
                if (kb.qKey.wasPressedThisFrame) ShiftDown();
            }
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
        Vector3 vel = rb.velocity;
        SpeedKmh = vel.magnitude * 3.6f;
        LastTotalForce = Vector3.zero;

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
                suspensionDrop[i] = travel;                       // 完全伸長
                WheelWorldPos[i] = origin - up * travel;
                WheelWorldRot[i] = anchor.rotation;
                continue;
            }

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

    void UpdateEngineAndGears(float dt)
    {
        if (spec.gearRatios == null || spec.gearRatios.Length == 0) return;
        Gear = Mathf.Clamp(Gear, 1, spec.gearRatios.Length);

        // 由驅動輪轉速回推引擎轉速
        float avgOmega = DrivenWheelOmega();
        float ratio = spec.gearRatios[Gear - 1] * spec.finalDrive;
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
        float launchRpm = Mathf.Lerp(spec.idleRpm, spec.peakTorqueRpm * 1.05f, Throttle01) * slipAllowance;
        float targetRpm = Mathf.Max(wheelDrivenRpm, launchRpm, spec.idleRpm);

        EngineRpm = Mathf.Lerp(EngineRpm, Mathf.Min(targetRpm, spec.redlineRpm * 1.02f), 10f * dt);

        // 空油門時引擎自然回落
        if (Throttle01 < 0.05f)
            EngineRpm = Mathf.Lerp(EngineRpm, Mathf.Max(spec.idleRpm, wheelDrivenRpm), 5f * dt);

        if (autoShift && shiftCooldown <= 0f)
        {
            if (EngineRpm > spec.redlineRpm * 0.94f && Gear < spec.gearRatios.Length && throttleInput > 0.1f)
                ShiftUp();
            else if (EngineRpm < spec.peakTorqueRpm * 0.52f && Gear > 1)
                ShiftDown();
        }
    }

    // 換檔頓挫要短。過長會讓驅動力頻繁中斷，加速變得斷斷續續。
    void ShiftUp()
    {
        if (spec.gearRatios == null || Gear >= spec.gearRatios.Length) return;
        Gear++;
        shiftCooldown = 0.12f;
    }

    void ShiftDown()
    {
        if (Gear <= 1) return;
        Gear--;
        shiftCooldown = 0.10f;
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

    /// 引擎輸出經齒比放大後、送到「一條軸」的總扭力
    float CurrentDriveTorquePerAxle()
    {
        if (spec.gearRatios == null || spec.gearRatios.Length == 0) return 0f;
        float ratio = spec.gearRatios[Gear - 1] * spec.finalDrive;

        float throttle = Mathf.Max(0f, throttleInput);
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
        float brakeInput = Mathf.Max(0f, -throttleInput);
        IsBraking = brakeInput > 0.05f && Vector3.Dot(rb.velocity, transform.forward) > 1f;

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
                    peak = TireModel.PeakForce(TireLoad[i], baseMu, spec.loadSensitivity);
                    SlipRatio[i] = Mathf.Clamp(
                        (WheelOmega[i] * spec.wheelRadiusM - vLong) / vRef, -4f, 4f);
                    fx = TireModel.Compute(SlipAngleDeg[i] * Mathf.Deg2Rad, SlipRatio[i],
                                           TireLoad[i], baseMu, spec.loadSensitivity).x;
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
                                          TireLoad[i], baseMu, spec.loadSensitivity);
            float peakForce = TireModel.PeakForce(TireLoad[i], baseMu, spec.loadSensitivity);
            GripUsage[i] = peakForce > 1f ? Mathf.Clamp01(force.magnitude / peakForce) : 0f;
            LastTireForce[i] = force;

            Vector3 worldForce = fwd[i] * force.x + right[i] * force.y;
            rb.AddForceAtPosition(worldForce, contact[i]);
            LastTotalForce += worldForce;
        }
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

    void ZeroMotion()
    {
        // 未選中的車是 kinematic，對 kinematic 剛體寫 velocity 會噴警告
        if (rb != null && !rb.isKinematic)
        {
            rb.velocity = Vector3.zero;
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
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

    }

    void OnEnable()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;
    }
}
