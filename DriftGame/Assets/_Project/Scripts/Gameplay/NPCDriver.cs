using UnityEngine;

/// NPC 駕駛 AI。
///
/// 關鍵設計：它不是走動畫路徑，而是把方向盤與油門「餵給和玩家完全相同的 CarController」，
/// 所以 NPC 會真的因為輪胎失去抓地而甩尾、真的會撞牆、真的有重量轉移。
///
/// 三個行為層：
///   1. 循跡 —— 純追蹤法（pure pursuit）瞄準前方一個預瞄點
///   2. 配速 —— 依前方彎道曲率決定目標速度，過快就煞車
///   3. 甩尾 —— 急彎且速度夠高時點手煞車入彎（可依個性關閉）
[RequireComponent(typeof(CarController))]
public class NPCDriver : MonoBehaviour
{
    public enum Style { Racer, Drifter, Traffic }

    [Header("個性")]
    public Style style = Style.Racer;
    [Tooltip("實力 0..1：影響過彎速度、預瞄距離、失誤率")]
    [Range(0f, 1f)] public float skill = 0.7f;
    [Tooltip("跑在賽道中心線的橫向偏移，讓每台車各走各的線")]
    public float laneOffset;

    [Header("循跡")]
    public float baseLookAhead = 12f;
    public float speedLookAheadGain = 0.55f;

    [Header("配速")]
    public float straightTargetKmh = 150f;
    public float minCornerKmh = 42f;

    [Header("甩尾")]
    public bool allowDrift = true;
    public float driftCurvature = 0.32f;    // 曲率超過此值考慮甩尾
    public float driftMinKmh = 55f;

    public float TargetKmh { get; private set; }
    public bool IsDrifting => car != null && drift != null && drift.IsDrifting;

    CarController car;
    DriftDetector drift;
    Rigidbody rb;
    TrackPath path;
    int pathIndex;
    float handbrakeTimer;
    float stuckTimer;
    float noiseSeed;

    void Start()
    {
        car = GetComponent<CarController>();
        drift = GetComponent<DriftDetector>();
        rb = GetComponent<Rigidbody>();
        path = TrackPath.Instance;
        car.useExternalInput = true;
        car.autoShift = true;
        noiseSeed = Random.value * 100f;

        if (style == Style.Traffic)
        {
            straightTargetKmh = Random.Range(55f, 80f);
            allowDrift = false;
            skill = 0.5f;
        }
        else if (style == Style.Drifter)
        {
            driftCurvature = 0.22f;   // 更愛甩
        }

        if (path != null) pathIndex = path.NearestIndex(transform.position);
    }

    void FixedUpdate()
    {
        if (car == null || path == null || path.Count == 0) return;
        if (!car.enabled) return;

        float speedKmh = car.SpeedKmh;

        // ---- 1. 循跡：瞄準前方預瞄點 ----
        pathIndex = path.NearestIndex(transform.position, pathIndex);

        // 速度越快看越遠（不然高速時方向修正會太晚）
        float lookAhead = baseLookAhead + speedKmh * speedLookAheadGain * 0.1f;
        int aheadSteps = Mathf.Clamp(Mathf.RoundToInt(lookAhead / 2.5f), 2, 40);
        Vector3 target = path.PointWithOffset(pathIndex + aheadSteps, laneOffset);

        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;
        float steerAngle = Vector3.SignedAngle(transform.forward, toTarget.normalized, Vector3.up);

        // 甩尾中要瞄「車頭該指的方向」而不是路徑方向，否則會一直反向修正
        float steerCmd = Mathf.Clamp(steerAngle / Mathf.Max(8f, car.spec.maxSteerDeg), -1f, 1f);

        // 技術差的車手方向會抖，也偶爾走錯線
        float wobble = (Mathf.PerlinNoise(Time.time * 1.3f, noiseSeed) - 0.5f) * (1f - skill) * 0.5f;
        steerCmd = Mathf.Clamp(steerCmd + wobble, -1f, 1f);

        // ---- 2. 配速：依前方曲率決定目標速度 ----
        float curve = path.CurvatureAhead(pathIndex, aheadSteps);
        float cornerSpeed = Mathf.Lerp(straightTargetKmh, minCornerKmh, Mathf.Clamp01(curve * 1.8f));
        TargetKmh = Mathf.Lerp(cornerSpeed * 0.82f, cornerSpeed * 1.08f, skill);

        float throttleCmd;
        if (speedKmh < TargetKmh - 4f) throttleCmd = 1f;
        else if (speedKmh > TargetKmh + 6f) throttleCmd = -0.75f;   // 煞車
        else throttleCmd = 0.35f;

        // ---- 3. 甩尾：急彎 + 夠快 → 點一下手煞車入彎 ----
        bool handbrake = false;
        if (allowDrift && handbrakeTimer <= 0f && curve > driftCurvature && speedKmh > driftMinKmh)
        {
            handbrakeTimer = Random.Range(0.35f, 0.6f);
        }
        if (handbrakeTimer > 0f)
        {
            handbrakeTimer -= Time.fixedDeltaTime;
            handbrake = true;
            throttleCmd = 0.7f;      // 甩尾中維持油門控制角度
        }

        // ---- 卡住自救：長時間幾乎不動就倒車脫困 ----
        if (speedKmh < 3f) stuckTimer += Time.fixedDeltaTime; else stuckTimer = 0f;
        if (stuckTimer > 2.5f)
        {
            throttleCmd = -1f;                       // 進倒檔往後退
            steerCmd = -steerCmd;
            if (stuckTimer > 4.5f) stuckTimer = 0f;
        }

        car.externalSteer = steerCmd;
        car.externalThrottle = throttleCmd;
        car.externalHandbrake = handbrake;
    }
}
