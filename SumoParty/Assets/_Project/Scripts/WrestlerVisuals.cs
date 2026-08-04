using UnityEngine;

/// 力士視覺層的「肉感」：果凍肚彈簧、受擊壓扁回彈、移動搖擺步、加速度前傾。
/// 只動 Visual 子節點，不碰剛體與碰撞體。
public class WrestlerVisuals : MonoBehaviour
{
    [Header("接線")]
    public Transform Visual;      // 所有網格的父節點

    [Header("手感參數")]
    public float JiggleStiffness = 90f;   // 彈簧勁度：越大回彈越快
    public float JiggleDamping = 7f;      // 阻尼：越小晃越久
    public float WaddleAmount = 0.045f;   // 走路上下起伏幅度
    public float WaddleRollDeg = 5f;      // 走路左右搖擺角度
    public float LeanDegPerSpeed = 3.2f;  // 每單位速度的前傾角
    public float MaxLeanDeg = 14f;

    Rigidbody rb;
    float jiggle, jiggleVel;   // >0 壓扁(寬矮)、<0 拉長(瘦高)
    float waddlePhase;
    Vector3 baseScale = Vector3.one;
    Quaternion leanRot = Quaternion.identity;

    void Awake()
    {
        rb = GetComponentInParent<Rigidbody>();
        if (Visual != null) baseScale = Visual.localScale;
    }

    /// 受擊/落地：壓扁。strength 建議 0.2~1.2
    public void Squash(float strength) => jiggleVel += Mathf.Clamp(strength, 0f, 1.4f) * 8f;

    /// 突進出手：瞬間拉長
    public void Stretch(float strength) => jiggleVel -= Mathf.Clamp(strength, 0f, 1f) * 6f;

    void Update()
    {
        if (Visual == null) return;
        float dt = Mathf.Min(Time.deltaTime, 0.033f);

        // ---- 果凍彈簧（臨界阻尼附近，晃 2~3 下停）----
        float accel = -JiggleStiffness * jiggle - JiggleDamping * jiggleVel;
        jiggleVel += accel * dt;
        jiggle += jiggleVel * dt;
        float squash = Mathf.Clamp(jiggle, -0.35f, 0.45f);

        // ---- 搖擺步 ----
        Vector3 v = rb != null ? rb.velocity : Vector3.zero;
        float speed = new Vector3(v.x, 0f, v.z).magnitude;
        float gait = Mathf.Clamp01(speed / 2.5f);
        waddlePhase += speed * 5.5f * dt;
        float bob = Mathf.Abs(Mathf.Sin(waddlePhase)) * WaddleAmount * gait;
        float roll = Mathf.Sin(waddlePhase) * WaddleRollDeg * gait;

        // ---- 速度前傾（重量感：往移動方向壓）----
        Vector3 local = transform.InverseTransformDirection(v);
        float pitch = Mathf.Clamp(local.z * LeanDegPerSpeed, -MaxLeanDeg, MaxLeanDeg);
        float side = Mathf.Clamp(-local.x * LeanDegPerSpeed * 0.6f, -MaxLeanDeg, MaxLeanDeg);
        leanRot = Quaternion.Slerp(leanRot, Quaternion.Euler(pitch, 0f, side + roll), 12f * dt);

        // ---- 合成（體積守恆：壓多少高就寬多少）----
        Visual.localPosition = new Vector3(0f, bob - squash * 0.10f, 0f);
        Visual.localRotation = leanRot;
        Visual.localScale = new Vector3(
            baseScale.x * (1f + squash * 0.5f),
            baseScale.y * (1f - squash * 0.6f),
            baseScale.z * (1f + squash * 0.5f));
    }
}
