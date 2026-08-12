using UnityEngine;

/// <summary>
/// 肉體次級運動（secondary motion）——「肉會晃」的正解。
///
/// 舊版只在挨招瞬間縮放 5%，肉眼看不見。真實的脂肪是：
/// **身體動、肉滯後**。每一步、每次急停、每下撞擊，視覺網格都會
/// 落後物理體一拍再被彈簧拉回——這就是肥肉的甩動。
///
/// 兩層彈簧：
///   位置層 —— 視覺相對物理體的滯後偏移（軟彈簧，甩動主體）
///   擠壓層 —— 撞擊瞬間的壓扁回彈（硬彈簧，撞擊的「肉感」）
/// 純視覺、LateUpdate、不碰物理。
/// </summary>
public class BodyJiggle : MonoBehaviour
{
    public Rikishi rikishi;

    Vector3 baseLocalPos, baseScale;
    Vector3 offset, offsetVel;        // 位置層
    float squash, squashVel;          // 擠壓層
    Vector3 prevBodyPos;
    bool ready;

    const float PosK = 55f, PosC = 6.5f;      // 軟：甩得起來
    const float SqK = 150f, SqC = 10f;        // 硬：快速回彈
    const float LagGain = 0.6f;               // 身體位移有多少比例甩進肉裡
    const float MaxOffset = 0.10f;

    void Start()
    {
        baseLocalPos = transform.localPosition;
        baseScale = transform.localScale;
        if (rikishi == null) rikishi = GetComponentInParent<Rikishi>();
        if (rikishi == null) { enabled = false; return; }

        prevBodyPos = rikishi.transform.position;
        rikishi.OnImpact += (force, pos) =>
            squashVel += Mathf.Min(2.6f, force / 3500f);   // 撞擊直接打進擠壓彈簧
        ready = true;
    }

    void LateUpdate()
    {
        if (!ready) return;
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        var body = rikishi.transform;

        // 位置層：身體移動 → 肉往反方向滯後，彈簧拉回（過衝＝甩動）
        Vector3 delta = body.position - prevBodyPos;
        prevBodyPos = body.position;
        offset -= delta * LagGain;
        offsetVel += (-PosK * offset - PosC * offsetVel) * dt;
        offset += offsetVel * dt;
        offset = Vector3.ClampMagnitude(offset, MaxOffset);
        transform.localPosition = baseLocalPos + body.InverseTransformVector(offset);

        // 擠壓層：垂直壓扁、水平外擠，過衝反向＝回彈
        squashVel += (-SqK * squash - SqC * squashVel) * dt;
        squash += squashVel * dt;
        float s = Mathf.Clamp(squash, -0.18f, 0.26f);
        transform.localScale = new Vector3(
            baseScale.x * (1f + s * 0.75f),
            baseScale.y * (1f - s * 0.6f),
            baseScale.z * (1f + s * 0.75f));
    }
}
