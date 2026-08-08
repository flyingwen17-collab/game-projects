using UnityEngine;

/// 甩尾判定：計算滑移角（車頭朝向 vs 實際速度方向的夾角），
/// 發出甩尾開始/結束事件，供計分、特效、音效訂閱。
[RequireComponent(typeof(Rigidbody))]
public class DriftDetector : MonoBehaviour
{
    [Header("判定條件")]
    public float minSpeedKmh = 25f;   // 低於此速度不算甩尾
    public float minAngle = 12f;      // 滑移角下限
    public float maxAngle = 80f;      // 超過視為失控/倒車，不算甩尾
    public float exitGraceTime = 0.3f;// 短暫回正不立即中斷甩尾

    public bool IsDrifting { get; private set; }
    public float SlipAngle { get; private set; }   // 有正負（左滑/右滑）
    public float SpeedKmh { get; private set; }
    public float DriftTime { get; private set; }   // 本次甩尾持續秒數

    public event System.Action OnDriftStart;
    public event System.Action OnDriftEnd;

    Rigidbody rb;
    float graceTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        Vector3 flatVel = rb.linearVelocity;
        flatVel.y = 0f;
        SpeedKmh = flatVel.magnitude * 3.6f;

        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;

        SlipAngle = (flatVel.sqrMagnitude > 0.5f && flatForward.sqrMagnitude > 0.01f)
            ? Vector3.SignedAngle(flatForward.normalized, flatVel.normalized, Vector3.up)
            : 0f;

        float absAngle = Mathf.Abs(SlipAngle);
        bool conditionMet = SpeedKmh >= minSpeedKmh && absAngle >= minAngle && absAngle <= maxAngle;

        if (conditionMet)
        {
            graceTimer = exitGraceTime;
            if (!IsDrifting)
            {
                IsDrifting = true;
                DriftTime = 0f;
                OnDriftStart?.Invoke();
            }
            DriftTime += Time.fixedDeltaTime;
        }
        else if (IsDrifting)
        {
            graceTimer -= Time.fixedDeltaTime;
            if (graceTimer <= 0f || SpeedKmh < minSpeedKmh * 0.5f)
            {
                IsDrifting = false;
                OnDriftEnd?.Invoke();
            }
        }
    }
}
