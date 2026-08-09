using UnityEngine;

/// <summary>
/// 手部動作（企劃書 §3.3 的灰盒版）。
///
/// P1.5 先用「程序式雙手」：兩顆手球依力士狀態伸向正確的目標點——
///   突き/寄り  → 交替推向對方肩口
///   組手       → 雙手鎖在對方廻し的實際位置，跟著對方的腰移動（拉腰帶的視覺來源）
///   引き       → 抓住並往自己胸口收
///   防禦       → 雙手前舉架勢
///   閒置       → 垂在身側微擺
///
/// 純視覺、在 LateUpdate 跑，完全不碰物理。
/// P2 換 MakeHuman 模型後，這裡的目標點直接餵給 Animation Rigging 的 Two Bone IK，
/// 邏輯不用重寫——這就是先做灰盒的意義。
/// </summary>
public class RikishiHands : MonoBehaviour
{
    public Rikishi rikishi;
    [Tooltip("由場景建構器指定。執行期用 CreatePrimitive 的預設材質在 URP build 會被剝掉變洋紅")]
    public Material handMaterial;

    Transform handL, handR;
    Vector3 curL, curR;      // 本地座標的目前位置（平滑用）
    float punchPhase;

    const float HandRadius = 0.11f;
    const float ShoulderX = 0.26f;

    void Start()
    {
        if (rikishi == null) rikishi = GetComponent<Rikishi>();
        handL = MakeHand("HandL");
        handR = MakeHand("HandR");
        curL = RestPos(-1f);
        curR = RestPos(1f);
    }

    Transform MakeHand(string n)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = n;
        Destroy(go.GetComponent<Collider>());       // 視覺球，不參與物理
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * (HandRadius * 2f);
        if (handMaterial != null) go.GetComponent<Renderer>().sharedMaterial = handMaterial;
        return go.transform;
    }

    Vector3 RestPos(float side) =>
        new Vector3(ShoulderX * side, rikishi.cfg.mawashiHeight + 0.12f, 0.18f);

    Vector3 GuardPos(float side) =>
        new Vector3(ShoulderX * side * 0.8f, rikishi.cfg.shoulderHeight - 0.08f, 0.42f);

    void LateUpdate()
    {
        if (rikishi == null || rikishi.cfg == null) return;
        var cfg = rikishi.cfg;
        var foe = rikishi.opponent;

        Vector3 tgtL, tgtR;
        float speed = 10f;

        if (rikishi.BodyTouchedGround)
        {
            tgtL = RestPos(-1f); tgtR = RestPos(1f);
            speed = 4f;
        }
        else if (rikishi.Gripping && foe != null)
        {
            // 雙手鎖在對方廻し（世界座標 → 我的本地座標），跟著對方的腰走
            Vector3 mawashi = foe.PointAt(cfg.mawashiHeight);
            Vector3 l = transform.InverseTransformPoint(mawashi + foe.transform.right * -0.16f);
            Vector3 r = transform.InverseTransformPoint(mawashi + foe.transform.right * 0.16f);
            tgtL = l; tgtR = r;
            speed = 14f;
        }
        else if (rikishi.Pulling && foe != null)
        {
            // 引き：往自己胸口收
            tgtL = new Vector3(-ShoulderX * 0.5f, cfg.shoulderHeight - 0.15f, 0.22f);
            tgtR = new Vector3(ShoulderX * 0.5f, cfg.shoulderHeight - 0.15f, 0.22f);
            speed = 16f;
        }
        else if ((rikishi.Thrusting || rikishi.Charging) && foe != null)
        {
            // 突き/突進/寄り：交替出掌推向對方肩口
            punchPhase += Time.deltaTime * 14f;
            float a = Mathf.PingPong(punchPhase, 1f);
            Vector3 shoulder = transform.InverseTransformPoint(foe.PointAt(cfg.shoulderHeight));
            shoulder.z = Mathf.Min(shoulder.z, 0.72f);   // 手臂長度上限，不要無限伸長
            tgtL = Vector3.Lerp(GuardPos(-1f), shoulder + new Vector3(-0.10f, 0f, 0f), a);
            tgtR = Vector3.Lerp(GuardPos(1f), shoulder + new Vector3(0.10f, 0f, 0f), 1f - a);
            speed = 22f;
        }
        else if (rikishi.Bracing)
        {
            tgtL = GuardPos(-1f); tgtR = GuardPos(1f);
            speed = 12f;
        }
        else
        {
            // 閒置：垂在身側，隨移動微擺
            float sway = Mathf.Sin(Time.time * 6f) * 0.03f * Mathf.Clamp01(rikishi.Velocity.magnitude);
            tgtL = RestPos(-1f) + new Vector3(0f, sway, 0f);
            tgtR = RestPos(1f) + new Vector3(0f, -sway, 0f);
            speed = 8f;
        }

        curL = Vector3.Lerp(curL, tgtL, Time.deltaTime * speed);
        curR = Vector3.Lerp(curR, tgtR, Time.deltaTime * speed);
        handL.localPosition = curL;
        handR.localPosition = curR;
    }
}
