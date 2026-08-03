using UnityEngine;

/// 甩尾計分：訂閱 DriftDetector 事件，依「滑移角 × 車速」累積分數，
/// 連段倍率在甩尾間隔內遞增，撞擊則沒收未結算分數。
/// 對外只發事件，UI / 音效 / 特效各自訂閱，互不相依。
[RequireComponent(typeof(DriftDetector))]
public class DriftScoring : MonoBehaviour
{
    [Header("基礎計分")]
    [Tooltip("每秒基礎分 = (滑移角 / 45) × (車速 km/h / 60) × 此係數")]
    public float scoreRate = 100f;
    public float minBankScore = 50f;      // 低於此分數不入帳，避免小碎分洗版

    [Header("連段")]
    public float comboWindow = 2f;        // 甩尾結束後多久內再甩算連段
    public float comboStep = 0.5f;        // 每段增加的倍率
    public float comboMax = 5f;

    [Header("貼牆加分")]
    public bool proximityBonusEnabled = true;
    public float proximityDistance = 2.5f;    // 側向偵測距離
    public float proximityMultiplier = 1.5f;  // 貼牆時的額外倍率
    public LayerMask proximityMask = ~0;

    [Header("失誤判定")]
    public float crashImpulse = 4f;       // 碰撞衝量超過此值視為撞車

    // ---- 對外狀態 ----
    public float PendingScore { get; private set; }   // 本連段累積、尚未入帳
    public float TotalScore { get; private set; }     // 已入帳總分
    public float Multiplier { get; private set; } = 1f;
    public int ComboCount { get; private set; }
    public bool NearWall { get; private set; }
    public float BestCombo { get; private set; }      // 本場最高倍率
    public float LongestDriftTime { get; private set; }

    /// 分數入帳（連段結束）。參數為本次入帳分數。
    public event System.Action<float> OnBanked;
    /// 撞車，未結算分數被沒收。參數為損失的分數。
    public event System.Action<float> OnCrash;
    /// 連段倍率提升。參數為新倍率。
    public event System.Action<float> OnComboUp;

    DriftDetector drift;
    Rigidbody rb;
    float comboTimer;
    bool comboOpen;      // 連段視窗開著（剛結束一次甩尾）

    void Awake()
    {
        drift = GetComponent<DriftDetector>();
        rb = GetComponent<Rigidbody>();
    }

    void OnEnable()
    {
        drift.OnDriftStart += HandleDriftStart;
        drift.OnDriftEnd += HandleDriftEnd;
    }

    void OnDisable()
    {
        drift.OnDriftStart -= HandleDriftStart;
        drift.OnDriftEnd -= HandleDriftEnd;
    }

    void HandleDriftStart()
    {
        if (comboOpen)
        {
            // 在連段視窗內再次甩尾 → 升倍率
            ComboCount++;
            Multiplier = Mathf.Min(comboMax, Multiplier + comboStep);
            if (Multiplier > BestCombo) BestCombo = Multiplier;
            OnComboUp?.Invoke(Multiplier);
        }
        comboOpen = false;
    }

    void HandleDriftEnd()
    {
        if (drift.DriftTime > LongestDriftTime) LongestDriftTime = drift.DriftTime;
        // 開啟連段視窗，時間內沒再甩就入帳
        comboOpen = true;
        comboTimer = comboWindow;
    }

    void Update()
    {
        if (drift.IsDrifting)
        {
            AccumulateScore();
        }
        else if (comboOpen)
        {
            comboTimer -= Time.deltaTime;
            if (comboTimer <= 0f) Bank();
        }
    }

    void AccumulateScore()
    {
        float angleFactor = Mathf.Abs(drift.SlipAngle) / 45f;
        float speedFactor = drift.SpeedKmh / 60f;
        float gain = scoreRate * angleFactor * speedFactor * Time.deltaTime;

        NearWall = proximityBonusEnabled && CheckNearWall();
        if (NearWall) gain *= proximityMultiplier;

        PendingScore += gain;
    }

    bool CheckNearWall()
    {
        Vector3 origin = transform.position + Vector3.up * 0.4f;
        return Physics.Raycast(origin, transform.right, proximityDistance, proximityMask, QueryTriggerInteraction.Ignore)
            || Physics.Raycast(origin, -transform.right, proximityDistance, proximityMask, QueryTriggerInteraction.Ignore);
    }

    /// 連段結束，把累積分數乘上倍率後入帳。
    void Bank()
    {
        comboOpen = false;
        float earned = PendingScore * Multiplier;
        if (earned >= minBankScore)
        {
            TotalScore += earned;
            OnBanked?.Invoke(earned);
        }
        PendingScore = 0f;
        Multiplier = 1f;
        ComboCount = 0;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.impulse.magnitude / Mathf.Max(Time.fixedDeltaTime, 1e-5f) < crashImpulse * 100f) return;
        if (PendingScore <= 0f && !comboOpen) return;

        float lost = PendingScore * Multiplier;
        PendingScore = 0f;
        Multiplier = 1f;
        ComboCount = 0;
        comboOpen = false;
        if (lost > 0f) OnCrash?.Invoke(lost);
    }

    /// 重新開始一場：清空所有分數與紀錄。
    public void ResetAll()
    {
        PendingScore = 0f;
        TotalScore = 0f;
        Multiplier = 1f;
        ComboCount = 0;
        BestCombo = 0f;
        LongestDriftTime = 0f;
        comboOpen = false;
        comboTimer = 0f;
    }
}
