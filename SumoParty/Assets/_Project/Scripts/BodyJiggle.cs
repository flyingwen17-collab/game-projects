using UnityEngine;

/// <summary>
/// 肉體顫動：挨打時脂肪的擠壓回彈（阻尼彈簧）。
/// 150 公斤的身體吃到衝擊該晃的是肉，不是整個人像紙片平移——
/// 這是「噸位感」的視覺核心之一。掛在視覺子物件上，不碰物理。
/// </summary>
public class BodyJiggle : MonoBehaviour
{
    public Rikishi rikishi;

    Vector3 baseScale;
    float squash, vel;    // 阻尼彈簧狀態

    const float Stiffness = 130f;
    const float Damping = 9f;

    void Start()
    {
        baseScale = transform.localScale;
        if (rikishi != null)
            rikishi.OnImpact += (force, pos) =>
                squash = Mathf.Min(0.16f, squash + force / 45000f);
    }

    void LateUpdate()
    {
        if (squash < 0.001f && Mathf.Abs(vel) < 0.001f)
        {
            transform.localScale = baseScale;
            return;
        }
        float dt = Time.deltaTime;
        vel += (-squash * Stiffness - vel * Damping) * dt;
        squash += vel * dt;
        // 垂直壓扁、水平外擠（肉往外擠的體積感）；彈簧過衝時反向＝回彈晃動
        transform.localScale = new Vector3(
            baseScale.x * (1f + squash * 0.9f),
            baseScale.y * (1f - squash),
            baseScale.z * (1f + squash * 0.9f));
    }
}
