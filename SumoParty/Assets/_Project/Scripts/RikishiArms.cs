using UnityEngine;

/// <summary>
/// 三節式手臂（企劃書 §3.3）：上臂 + 前臂 + 手掌，解析式雙骨 IK。
/// 取代 P1.5 的浮球——現在是真的手臂在推、在抓。
///
/// 目標點依力士狀態決定（跟浮球版同一套狀態機，所以行為已被驗證過）：
///   組手   → 雙手鎖在對方廻し的實際位置，跟著對方的腰移動（拉腰帶）
///   突き   → 交替出掌推向對方肩口；引き → 收向自己胸口
///   防禦   → 前舉架勢；閒置 → 垂在身側
///
/// 純視覺、LateUpdate、不碰物理。換上有骨架的模型時，這裡的目標點
/// 直接改餵 Animation Rigging 的 Two Bone IK。
/// </summary>
public class RikishiArms : MonoBehaviour
{
    public Rikishi rikishi;
    public Material skinMaterial;

    const float UpperLen = 0.34f, ForeLen = 0.30f;
    const float UpperThick = 0.105f, ForeThick = 0.088f, HandR = 0.115f;   // 力士的粗手臂
    const float ShoulderX = 0.45f;   // 力士腹寬 0.46，肩點要在體外，手臂才不會穿肚

    Transform[] upper = new Transform[2], fore = new Transform[2], hand = new Transform[2];
    Vector3[] curTarget = new Vector3[2];
    float punchPhase;

    void Start()
    {
        if (rikishi == null) rikishi = GetComponent<Rikishi>();
        for (int i = 0; i < 2; i++)
        {
            upper[i] = MakeLimb($"UpperArm{i}", PrimitiveType.Capsule, UpperThick);
            fore[i] = MakeLimb($"ForeArm{i}", PrimitiveType.Capsule, ForeThick);
            hand[i] = MakeLimb($"Hand{i}", PrimitiveType.Sphere, HandR);
            curTarget[i] = Shoulder(i) + Vector3.down * (UpperLen + ForeLen) * 0.8f;
        }
    }

    Transform MakeLimb(string n, PrimitiveType t, float r)
    {
        var go = GameObject.CreatePrimitive(t);
        go.name = n;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        if (skinMaterial != null) go.GetComponent<Renderer>().sharedMaterial = skinMaterial;
        if (t == PrimitiveType.Sphere) go.transform.localScale = Vector3.one * (r * 2f);
        return go.transform;
    }

    float Side(int i) => i == 0 ? -1f : 1f;

    Vector3 Shoulder(int i) =>
        transform.TransformPoint(new Vector3(ShoulderX * Side(i), rikishi.cfg.shoulderHeight + 0.02f, 0.06f));

    void LateUpdate()
    {
        if (rikishi == null || rikishi.cfg == null) return;
        var cfg = rikishi.cfg;
        var foe = rikishi.opponent;

        for (int i = 0; i < 2; i++)
        {
            float side = Side(i);
            Vector3 target;
            float speed = 10f;

            if (rikishi.BodyTouchedGround)
            {
                target = Shoulder(i) + transform.TransformDirection(new Vector3(0.15f * side, -0.45f, 0.1f));
                speed = 4f;
            }
            else if (rikishi.Gripping && foe != null)
            {
                target = foe.PointAt(cfg.mawashiHeight) + foe.transform.right * (0.17f * side);
                speed = 15f;
            }
            else if (rikishi.Pulling && foe != null)
            {
                target = transform.TransformPoint(new Vector3(0.16f * side, cfg.shoulderHeight - 0.12f, 0.24f));
                speed = 16f;
            }
            else if ((rikishi.Thrusting || rikishi.Charging) && foe != null)
            {
                punchPhase += Time.deltaTime * 7f;
                float a = Mathf.PingPong(punchPhase + i * 0.5f, 1f);   // 左右交替
                Vector3 guard = transform.TransformPoint(new Vector3(0.24f * side, cfg.shoulderHeight, 0.38f));
                Vector3 strike = foe.PointAt(cfg.shoulderHeight) + foe.transform.right * (0.10f * side);
                target = Vector3.Lerp(guard, strike, a);
                speed = 24f;
            }
            else if (rikishi.Bracing)
            {
                target = transform.TransformPoint(new Vector3(0.24f * side, cfg.shoulderHeight - 0.05f, 0.42f));
                speed = 12f;
            }
            else
            {
                float sway = Mathf.Sin(Time.time * 5f + i * Mathf.PI) * 0.02f;
                target = transform.TransformPoint(new Vector3(0.58f * side, cfg.mawashiHeight + 0.10f + sway, 0.16f));
                speed = 8f;
            }

            curTarget[i] = Vector3.Lerp(curTarget[i], target, Time.deltaTime * speed);
            SolveArm(i, curTarget[i]);
        }
    }

    /// <summary>解析式雙骨 IK：肩→肘→腕，肘往外下方頂（人類手肘的自然方向）。</summary>
    void SolveArm(int i, Vector3 target)
    {
        Vector3 s = Shoulder(i);
        Vector3 to = target - s;
        float d = Mathf.Clamp(to.magnitude, 0.12f, UpperLen + ForeLen - 0.02f);
        Vector3 dir = to.normalized;
        Vector3 wrist = s + dir * d;

        // 三角形定位手肘：a = 肩到「肘投影點」的距離，h = 肘離軸線的高
        float a = (UpperLen * UpperLen - ForeLen * ForeLen + d * d) / (2f * d);
        float h = Mathf.Sqrt(Mathf.Max(0.0001f, UpperLen * UpperLen - a * a));
        Vector3 hint = (transform.right * Side(i) * 0.8f + Vector3.down * 0.5f).normalized;
        Vector3 perp = Vector3.ProjectOnPlane(hint, dir).normalized;
        Vector3 elbow = s + dir * a + perp * h;

        PlaceCapsule(upper[i], s, elbow, UpperThick);
        PlaceCapsule(fore[i], elbow, wrist, ForeThick);
        hand[i].position = wrist + dir * 0.03f;
    }

    static void PlaceCapsule(Transform t, Vector3 a, Vector3 b, float thick)
    {
        Vector3 mid = (a + b) * 0.5f;
        Vector3 axis = b - a;
        t.position = mid;
        t.rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
        // Unity 膠囊預設高 2（scaleY=1 時），所以 scaleY = 半長
        t.localScale = new Vector3(thick * 2f, axis.magnitude * 0.5f, thick * 2f);
        // 抵銷父層縮放（力士模型會被整體縮放）
        var p = t.parent.lossyScale;
        t.localScale = new Vector3(t.localScale.x / Mathf.Max(0.01f, p.x),
                                   t.localScale.y / Mathf.Max(0.01f, p.y),
                                   t.localScale.z / Mathf.Max(0.01f, p.z));
    }
}
