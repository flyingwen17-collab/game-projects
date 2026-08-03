using UnityEngine;

/// 車輛規格 —— 全部採用市售車的公開真實數據（重量、軸距、扭力、齒比、胎徑）。
/// 手感調校只改這裡，不動物理程式碼。
[System.Serializable]
public class CarSpec
{
    [Header("車身")]
    public string displayName = "CAR";
    public float massKg = 1000f;
    public float wheelbaseM = 2.5f;         // 軸距
    public float trackWidthM = 1.45f;       // 輪距
    public float frontWeightRatio = 0.53f;  // 前軸配重比
    public float cgHeightM = 0.50f;         // 重心高（越高側傾與重量轉移越明顯）
    public float dragArea = 0.70f;          // Cd × A，空氣阻力用
    public float downforceCoef = 0.9f;      // 下壓力係數

    [Header("引擎")]
    public float peakTorqueNm = 149f;       // 最大扭力
    public float peakTorqueRpm = 5200f;
    public float peakPowerRpm = 6600f;      // 最大馬力轉速
    public float redlineRpm = 7400f;
    public float idleRpm = 850f;
    public float engineInertia = 0.22f;     // 引擎轉動慣量（影響補油反應）
    public float engineBrakeNm = 22f;       // 引擎煞車

    [Header("傳動")]
    public float[] gearRatios = { 3.587f, 2.022f, 1.384f, 1.000f, 0.861f };
    public float finalDrive = 4.30f;
    public float drivetrainEfficiency = 0.90f;
    public Drivetrain drivetrain = Drivetrain.RWD;
    public float awdFrontShare = 0.41f;     // AWD 前軸分配
    [Tooltip("LSD 鎖定率 0=開放式差速 1=完全鎖定。越高越好甩、越穩定")]
    [Range(0f, 1f)] public float diffLock = 0.55f;

    [Header("輪胎")]
    public float wheelRadiusM = 0.289f;     // 由胎規換算
    public float wheelInertia = 1.1f;
    public float tireGripFront = 1.05f;     // 前胎摩擦係數
    public float tireGripRear = 1.00f;      // 後胎（比前胎低 → 天生偏轉向過度，好甩）
    [Tooltip("載荷敏感度：載重越大單位抓地力越低，這是重量轉移能改變操控的原因")]
    public float loadSensitivity = 0.22f;

    [Header("轉向與煞車")]
    public float maxSteerDeg = 34f;
    public float steerSpeedDegPerSec = 180f;
    public float steerReturnDegPerSec = 260f;
    public float highSpeedSteerLimit = 0.42f; // 高速時最大轉向角縮到這個比例
    public float brakeTorqueNm = 2400f;
    public float handbrakeTorqueNm = 3200f;  // 只作用在後輪
    public float brakeBiasFront = 0.62f;

    [Header("懸吊")]
    public float suspensionTravelM = 0.30f;
    public float springRate = 42000f;
    public float damperRate = 3800f;
    public float antiRollBarNm = 9000f;

    // ---------------- 三台實車的真實規格 ----------------

    /// Toyota Sprinter Trueno AE86 GT-APEX（1983）——後驅、輕、天生好甩。
    public static CarSpec AE86() => new CarSpec
    {
        displayName = "TOYOTA AE86",
        massKg = 970f,
        wheelbaseM = 2.400f,
        trackWidthM = 1.355f,
        frontWeightRatio = 0.53f,
        cgHeightM = 0.48f,
        dragArea = 0.66f,
        downforceCoef = 0.6f,

        // 4A-GE 1.6L：130PS@6600、149N·m@5200
        peakTorqueNm = 149f,
        peakTorqueRpm = 5200f,
        peakPowerRpm = 6600f,
        redlineRpm = 7600f,
        engineInertia = 0.16f,
        engineBrakeNm = 20f,

        gearRatios = new[] { 3.587f, 2.022f, 1.384f, 1.000f, 0.861f },
        finalDrive = 4.300f,
        drivetrain = Drivetrain.RWD,
        diffLock = 0.70f,

        wheelRadiusM = 0.289f,   // 185/60R14
        wheelInertia = 0.9f,
        tireGripFront = 1.06f,
        tireGripRear = 0.98f,
        loadSensitivity = 0.24f,

        maxSteerDeg = 36f,
        brakeTorqueNm = 1900f,
        handbrakeTorqueNm = 2800f,
        brakeBiasFront = 0.62f,
        springRate = 33000f,
        damperRate = 3100f,
        antiRollBarNm = 7000f,
    };

    /// Subaru Impreza WRX STI（GDB）——四驅、重、高速穩定。
    public static CarSpec WRX() => new CarSpec
    {
        displayName = "SUBARU WRX STI",
        massKg = 1470f,
        wheelbaseM = 2.525f,
        trackWidthM = 1.485f,
        frontWeightRatio = 0.59f,
        cgHeightM = 0.53f,
        dragArea = 0.78f,
        downforceCoef = 1.1f,

        // EJ25：280PS@6400、392N·m@4400
        peakTorqueNm = 392f,
        peakTorqueRpm = 4400f,
        peakPowerRpm = 6400f,
        redlineRpm = 7000f,
        engineInertia = 0.30f,
        engineBrakeNm = 30f,

        gearRatios = new[] { 3.636f, 2.375f, 1.761f, 1.346f, 1.062f, 0.842f },
        finalDrive = 3.900f,
        drivetrain = Drivetrain.AWD,
        awdFrontShare = 0.41f,
        diffLock = 0.80f,

        wheelRadiusM = 0.317f,   // 225/45R17
        wheelInertia = 1.35f,
        tireGripFront = 1.16f,
        tireGripRear = 1.14f,
        loadSensitivity = 0.18f,

        maxSteerDeg = 32f,
        brakeTorqueNm = 3200f,
        handbrakeTorqueNm = 3400f,
        brakeBiasFront = 0.64f,
        springRate = 52000f,
        damperRate = 4600f,
        antiRollBarNm = 13000f,
    };

    /// Honda Fit RS（GK5）——前驅、靈活、不好甩但好開。
    public static CarSpec FIT() => new CarSpec
    {
        displayName = "HONDA FIT RS",
        massKg = 1080f,
        wheelbaseM = 2.530f,
        trackWidthM = 1.470f,
        frontWeightRatio = 0.62f,
        cgHeightM = 0.55f,
        dragArea = 0.72f,
        downforceCoef = 0.5f,

        // L15B：132PS@6600、155N·m@4600
        peakTorqueNm = 155f,
        peakTorqueRpm = 4600f,
        peakPowerRpm = 6600f,
        redlineRpm = 6900f,
        engineInertia = 0.18f,
        engineBrakeNm = 18f,

        gearRatios = new[] { 3.643f, 1.958f, 1.310f, 0.971f, 0.775f, 0.633f },
        finalDrive = 4.294f,
        drivetrain = Drivetrain.FWD,
        diffLock = 0.25f,

        wheelRadiusM = 0.305f,   // 185/55R16
        wheelInertia = 1.0f,
        tireGripFront = 1.10f,
        tireGripRear = 1.06f,
        loadSensitivity = 0.20f,

        maxSteerDeg = 38f,
        brakeTorqueNm = 2100f,
        handbrakeTorqueNm = 3000f,
        brakeBiasFront = 0.68f,
        springRate = 38000f,
        damperRate = 3400f,
        antiRollBarNm = 9500f,
    };

    /// 引擎扭力曲線：怠速偏低 → 峰值 → 紅線衰退，用兩段拋物線近似實車曲線。
    public float TorqueAt(float rpm)
    {
        rpm = Mathf.Clamp(rpm, 0f, redlineRpm * 1.05f);
        if (rpm < idleRpm) return peakTorqueNm * 0.35f;

        if (rpm <= peakTorqueRpm)
        {
            // 低轉：由 55% 上升到 100%
            float t = Mathf.InverseLerp(idleRpm, peakTorqueRpm, rpm);
            return peakTorqueNm * Mathf.Lerp(0.55f, 1f, Mathf.Sin(t * Mathf.PI * 0.5f));
        }

        // 高轉：扭力下滑但馬力仍上升到 peakPowerRpm，紅線後急落
        float u = Mathf.InverseLerp(peakTorqueRpm, redlineRpm, rpm);
        float fall = Mathf.Lerp(1f, 0.72f, u);
        if (rpm > redlineRpm) fall *= 0.35f;   // 斷油
        return peakTorqueNm * fall;
    }
}
