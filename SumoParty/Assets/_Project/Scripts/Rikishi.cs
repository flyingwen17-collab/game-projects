using System;
using UnityEngine;

/// <summary>
/// 力士本體（企劃書 §3）。重製版的三條鐵則：
///
///   1. **只用力，不覆寫速度** —— 移動、推、拉、摔全走 AddForce/AddForceAtPosition，
///      讓求解器處理質量對抗。舊版每幀 `rb.velocity = ...` 會把推撞的衝量吃掉。
///   2. **允許身體傾斜** —— 沒有 FreezeRotation。用平衡扭矩撐住，扭矩強度隨 CP 衰減，
///      所以「失衡」是物理現象而不是狀態旗標。
///   3. **組手＝真的 ConfigurableJoint** —— 角力由關節傳遞，誰重、誰重心低、誰施力點好誰贏。
///
/// 轉身也用扭矩（不是直接設 transform.rotation），避免跟求解器搶控制權。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Rikishi : MonoBehaviour
{
    public SumoConfig cfg;
    public Rikishi opponent;
    public string displayName = "力士";

    [Tooltip("腳底在本地座標的高度以下算腳；以上的部位觸地＝倒")]
    public float footLocalHeight = 0.28f;

    // ---- 對外狀態（給規則、AI、音效、UI 讀） ----
    public float CP { get; private set; }
    public float CPRatio => cfg != null ? Mathf.Clamp01(CP / cfg.cpMax) : 0f;
    public bool Charging => chargeTimer > 0f;
    public bool Bracing { get; private set; }
    public bool Gripping { get; private set; }          // 我抓著對方
    public bool Vulnerable => vulnerableTimer > 0f;      // いなし 之後的破綻
    public bool BodyTouchedGround { get; private set; }  // 非腳部觸地＝倒
    public bool Active { get; set; } = true;
    public float TiltAngle => Vector3.Angle(transform.up, Vector3.up);
    public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;

    /// <summary>撞擊事件（衝量大小、世界座標）——音效與特效掛這裡。</summary>
    public event Action<float, Vector3> OnImpact;
    /// <summary>出招事件——給音效與觀眾反應用。</summary>
    public event Action<SumoGesture, bool> OnAction;   // (手勢, 是否在組手狀態)

    Rigidbody rb;
    ConfigurableJoint gripJoint;
    SumoCommand cmd;
    SumoGesture pending = SumoGesture.None;

    float chargeTimer, vulnerableTimer, cpIdleTimer, holdTimer;

    /// <summary>持續施力的種類。真實的推/拉都有時間長度，不是單幀脈衝。</summary>
    enum Sustain { None, Thrust, Yori, Hiki }
    Sustain sustainKind;
    float sustainTimer, sustainForce;
    float thrustCd, chargeCd, sidestepCd, retreatCd;
    float matchStartTime;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (cfg == null) { Debug.LogError($"[{name}] 沒有指定 SumoConfig"); enabled = false; return; }

        rb.mass = cfg.mass;
        rb.centerOfMass = new Vector3(0f, cfg.centerOfMassHeight, 0f);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.None;   // 刻意不鎖旋轉：傾斜是玩法的一部分
        rb.maxAngularVelocity = 12f;

        CP = cfg.cpMax;
        matchStartTime = Time.time;
    }

    /// <summary>由 SumoMatch 或 AI 每幀餵入。</summary>
    public void Feed(SumoCommand c)
    {
        if (c.gesture != SumoGesture.None) pending = c.gesture;
        cmd = c;
    }

    public void ResetForBout(Vector3 pos, Vector3 lookDir)
    {
        ReleaseGrip();
        rb.linearVelocity = Vector3.zero;      // 只在重置時歸零，這不是每幀覆寫
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookDir, Vector3.up));
        CP = cfg.cpMax;
        chargeTimer = vulnerableTimer = holdTimer = 0f;
        thrustCd = chargeCd = sidestepCd = retreatCd = 0f;
        sustainTimer = 0f; sustainKind = Sustain.None;
        BodyTouchedGround = false;
        Bracing = false;
        pending = SumoGesture.None;
        Active = true;
        matchStartTime = Time.time;
    }

    void FixedUpdate()
    {
        if (cfg == null) return;
        float dt = Time.fixedDeltaTime;

        Tick(dt);

        if (!Active) return;

        Bracing = cmd.holding && !Gripping;
        holdTimer = cmd.holding ? holdTimer + dt : 0f;

        Balance(dt);
        Facing();
        FootGrip();
        Stamina(dt);

        if (pending != SumoGesture.None)
        {
            DoAction(pending);
            pending = SumoGesture.None;
        }

        SustainTick();
        if (Charging) rb.AddForce(Forward() * cfg.chargeForce * ChargeScale(), ForceMode.Force);
    }

    void Tick(float dt)
    {
        chargeTimer = Mathf.Max(0f, chargeTimer - dt);
        sustainTimer = Mathf.Max(0f, sustainTimer - dt);
        vulnerableTimer = Mathf.Max(0f, vulnerableTimer - dt);
        thrustCd = Mathf.Max(0f, thrustCd - dt);
        chargeCd = Mathf.Max(0f, chargeCd - dt);
        sidestepCd = Mathf.Max(0f, sidestepCd - dt);
        retreatCd = Mathf.Max(0f, retreatCd - dt);
    }

    // ---------- 平衡 / 姿態 ----------

    void Balance(float dt)
    {
        if (BodyTouchedGround) return;      // 倒了就交給物理，不再撐

        float cpFactor = Mathf.Lerp(cfg.lowCpBalance, 1f, CPRatio);
        Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
        if (axis.sqrMagnitude > 1e-6f)
        {
            float angleRad = Vector3.Angle(transform.up, Vector3.up) * Mathf.Deg2Rad;
            rb.AddTorque(axis.normalized * (angleRad * cfg.balanceTorque * cpFactor), ForceMode.Force);
        }
        rb.AddTorque(-rb.angularVelocity * cfg.balanceDamping, ForceMode.Force);
    }

    void Facing()
    {
        if (opponent == null || BodyTouchedGround) return;
        Vector3 to = opponent.transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1e-4f) return;

        Vector3 fwd = Forward();
        float err = Vector3.SignedAngle(fwd, to.normalized, Vector3.up);
        rb.AddTorque(Vector3.up * (err * Mathf.Deg2Rad * cfg.facingTorque), ForceMode.Force);
    }

    /// <summary>
    /// 腳底摩擦。用「阻尼力 + 上限」模擬真實摩擦：
    /// 慢速時抓得住站得穩，力道超過摩擦上限就是會被推著滑 —— 這正是相撲的推し出し。
    /// </summary>
    void FootGrip()
    {
        Vector3 hv = rb.linearVelocity; hv.y = 0f;
        if (hv.sqrMagnitude < 1e-6f) return;

        float maxGrip = cfg.footGrip * (Bracing ? 1f + cfg.braceResist : 1f);
        Vector3 f = -hv * cfg.footDamping;
        if (f.magnitude > maxGrip) f = f.normalized * maxGrip;
        rb.AddForce(f, ForceMode.Force);
    }

    void Stamina(float dt)
    {
        if (Bracing) { Spend(cfg.braceCPPerSec * dt); return; }
        if (Gripping) { Spend(cfg.gripCPPerSec * dt); return; }

        cpIdleTimer += dt;
        if (cpIdleTimer >= cfg.cpRegenDelay)
            CP = Mathf.Min(cfg.cpMax, CP + cfg.cpRegenPerSec * dt);
    }

    void Spend(float amount)
    {
        CP = Mathf.Max(0f, CP - amount);
        cpIdleTimer = 0f;
    }

    /// <summary>被對方反削 CP（防禦擋下突き時用）。</summary>
    public void DrainCP(float amount) => Spend(amount);

    // ---------- 招式 ----------

    void DoAction(SumoGesture g)
    {
        if (Gripping) DoGrippedAction(g);
        else DoFreeAction(g);
        OnAction?.Invoke(g, Gripping);
    }

    void DoFreeAction(SumoGesture g)
    {
        switch (g)
        {
            case SumoGesture.Tap: Thrust(); break;
            // 前滑：遠距離＝突進，貼身＝伸手抓廻し。
            // 用距離區分而不是另給一個手勢 —— 一來符合真實相撲（貼身才組得到手），
            // 二來「按住」就能專心當防禦，不會跟組手打架。
            case SumoGesture.Forward:
                if (InFront(cfg.gripRange, 90f)) TryGrip();
                else Charge();
                break;
            case SumoGesture.Back: Retreat(); break;
            case SumoGesture.Left: Sidestep(-1f); break;
            case SumoGesture.Right: Sidestep(1f); break;
        }
    }

    void Thrust()
    {
        if (thrustCd > 0f || opponent == null) return;
        thrustCd = cfg.thrustCooldown;
        Spend(cfg.thrustCP);

        if (!InFront(cfg.thrustRange, cfg.thrustAngle)) return;

        float force = cfg.thrustForce * TachiaiScale();

        // 相剋：防禦擋下突き，還反削我的 CP
        if (opponent.Bracing)
        {
            force *= 1f - cfg.braceResist;
            Spend(cfg.braceReflectCP);
        }
        // 相剋：對方剛閃身，破綻期吃突き加倍
        if (opponent.Vulnerable) force *= 2f;

        Begin(Sustain.Thrust, force, cfg.thrustDuration);
        OnImpact?.Invoke(force, HandPoint());
    }

    void Begin(Sustain kind, float force, float duration)
    {
        sustainKind = kind;
        sustainForce = force;
        sustainTimer = duration;
    }

    /// <summary>
    /// 持續施力。單幀施力對 150 公斤的人只推得動 8 毫米（自測實際量到的），
    /// 而且推在肩高會讓對方前後搖晃、晃回來就抵銷了 —— 真實的推有時間長度。
    /// 這也是手部動畫的基礎：手推出去要有一段時間才動得起來。
    /// </summary>
    void SustainTick()
    {
        if (sustainTimer <= 0f || sustainKind == Sustain.None || opponent == null) return;

        Vector3 dir = opponent.transform.position - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir.Normalize();
        float f = sustainForce;

        switch (sustainKind)
        {
            case Sustain.Thrust:
                // 施力點在對方肩高 —— 推肩會讓對方轉，推腰才是直推。位置決定結果。
                opponent.ReceiveForce(dir * f, opponent.PointAt(cfg.shoulderHeight));
                rb.AddForce(-dir * f * 0.25f, ForceMode.Force);          // 反作用
                break;

            case Sustain.Yori:   // 壓著對方前進：自己也往前，才推得動
                rb.AddForce(dir * f, ForceMode.Force);
                opponent.ReceiveForce(dir * f, opponent.PointAt(cfg.mawashiHeight));
                break;

            case Sustain.Hiki:   // 把對方拉向自己，破壞他的平衡
                opponent.ReceiveForce(-dir * f, opponent.PointAt(cfg.shoulderHeight));
                rb.AddForce(-dir * f * 0.4f, ForceMode.Force);
                break;
        }
    }

    void Charge()
    {
        if (chargeCd > 0f) return;
        chargeCd = cfg.chargeCooldown;
        chargeTimer = cfg.chargeDuration;
        Spend(cfg.chargeCP);
    }

    float ChargeScale()
    {
        float s = TachiaiScale();
        // 相剋：突進破防（只被抵消一小部分）
        if (opponent != null && opponent.Bracing)
            s *= 1f - cfg.braceResist * (1f - cfg.chargeBreakThrough);
        return s;
    }

    void Retreat()
    {
        if (retreatCd > 0f) return;
        retreatCd = cfg.retreatCooldown;
        Spend(cfg.retreatCP);

        rb.AddForce(-Forward() * cfg.retreatForce, ForceMode.Force);

        // 相剋：對方正在突進時後拉 → 引き落とし，把他往前下方拽
        if (opponent != null && opponent.Charging && InFront(cfg.gripRange * 1.6f, 100f))
        {
            // 把對手往「我的前方 + 下方」拽 —— 他正衝過來，順勢拉他撲地
            Vector3 pull = (Forward() * 0.6f + Vector3.down).normalized;
            opponent.ReceiveForce(pull * cfg.pullDownForce, opponent.PointAt(cfg.shoulderHeight));
            opponent.Stumble();
            OnImpact?.Invoke(cfg.pullDownForce, opponent.PointAt(cfg.shoulderHeight));
        }
    }

    void Sidestep(float sign)
    {
        if (sidestepCd > 0f) return;
        sidestepCd = cfg.sidestepCooldown;
        vulnerableTimer = cfg.sidestepVulnerable;
        Spend(cfg.sidestepCP);

        Vector3 side = Vector3.Cross(Vector3.up, Forward()).normalized * sign;
        rb.AddForce(side * cfg.sidestepImpulse, ForceMode.VelocityChange);

        // 閃身也是掙脫組手的手段（被抓住時唯一的解法）
        ReleaseGrip();
        if (opponent != null && opponent.Gripping) opponent.ReleaseGrip();

        // 相剋：閃掉正在突進的對手 → 他衝空、往前踉蹌
        if (opponent != null && opponent.Charging)
        {
            opponent.ReceiveForce(opponent.Forward() * cfg.whiffStumble,
                                  opponent.PointAt(cfg.shoulderHeight));
            opponent.Stumble();
        }
    }

    // ---------- 組手 ----------

    void TryGrip()
    {
        // gripJoint != null 的檢查不能省：Destroy 是延後執行的，
        // 只看 Gripping 旗標會在同一幀掛上第二個關節。
        if (Gripping || gripJoint != null || opponent == null) return;
        if (BodyTouchedGround || opponent.BodyTouchedGround) return;
        if (!InFront(cfg.gripRange, 90f)) return;
        if (opponent.Vulnerable) return;         // 對方正在閃身，抓不到

        Spend(cfg.thrustCP);

        // 關節的限制距離必須用「當下實際的間距」，不能寫死。
        // 寫死 0.35 m 而兩人實際相距 1.1 m 時，關節會用巨力把兩人猛拽，
        // 瞬間超過斷裂閾值自己斷掉 —— 自測抓到的就是這個（抓了等於沒抓）。
        float separation = Vector3.Distance(transform.position, opponent.transform.position);

        gripJoint = gameObject.AddComponent<ConfigurableJoint>();
        gripJoint.connectedBody = opponent.GetComponent<Rigidbody>();
        gripJoint.autoConfigureConnectedAnchor = false;
        gripJoint.anchor = new Vector3(0f, cfg.mawashiHeight, 0f);
        gripJoint.connectedAnchor = new Vector3(0f, cfg.mawashiHeight, 0f);
        gripJoint.xMotion = gripJoint.yMotion = gripJoint.zMotion = ConfigurableJointMotion.Limited;
        gripJoint.linearLimit = new SoftJointLimit { limit = separation + 0.05f };
        // 軟限制：抓住是「拉得住」不是「焊死」，有彈性才像抓布帶
        gripJoint.linearLimitSpring = new SoftJointLimitSpring { spring = 6000f, damper = 400f };
        gripJoint.angularXMotion = gripJoint.angularYMotion = gripJoint.angularZMotion = ConfigurableJointMotion.Free;
        gripJoint.breakForce = cfg.gripBreakForce;
        gripJoint.breakTorque = cfg.gripBreakForce;
        gripJoint.enableCollision = true;

        Gripping = true;
    }

    void DoGrippedAction(SumoGesture g)
    {
        Vector3 fwd = Forward();
        Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 mawashi = opponent.PointAt(cfg.mawashiHeight);

        switch (g)
        {
            case SumoGesture.Forward:   // 寄り：壓著對方前進
                Begin(Sustain.Yori, cfg.yoriForce, cfg.thrustDuration * 2f);
                Spend(cfg.thrustCP);
                break;

            case SumoGesture.Back:      // 引き：把對方拉向自己使其失衡
                Begin(Sustain.Hiki, cfg.hikiForce, cfg.thrustDuration * 1.5f);
                Spend(cfg.retreatCP);
                break;

            case SumoGesture.Left:
            case SumoGesture.Right:     // 投げ：往該方向摔
                float sign = g == SumoGesture.Left ? -1f : 1f;
                var orb = opponent.GetComponent<Rigidbody>();
                orb.AddTorque(fwd * (sign * cfg.throwTorque), ForceMode.Force);
                opponent.ReceiveForce(side * (sign * cfg.throwTorque) + Vector3.down * cfg.throwTorque * 0.5f,
                                      opponent.PointAt(cfg.shoulderHeight));
                Spend(cfg.chargeCP);
                ReleaseGrip();
                break;

            case SumoGesture.Tap:       // 揺さぶり：搖晃破壞平衡
                float wobble = Mathf.Sin(Time.time * 30f) >= 0f ? 1f : -1f;
                opponent.ReceiveForce(side * (wobble * cfg.shakeForce), opponent.PointAt(cfg.shoulderHeight));
                Spend(cfg.thrustCP * 0.5f);
                break;
        }
    }

    public void ReleaseGrip()
    {
        if (gripJoint != null) Destroy(gripJoint);
        gripJoint = null;
        Gripping = false;
    }

    void OnJointBreak(float breakForce)
    {
        Gripping = false;
        gripJoint = null;
        OnImpact?.Invoke(breakForce * 0.3f, PointAt(cfg.mawashiHeight));
    }


    // ---------- 受力與倒地判定 ----------

    public void ReceiveForce(Vector3 force, Vector3 worldPoint)
    {
        if (!Active) return;
        rb.AddForceAtPosition(force, worldPoint, ForceMode.Force);
    }

    public void Stumble() => vulnerableTimer = Mathf.Max(vulnerableTimer, cfg.sidestepVulnerable);

    void OnCollisionEnter(Collision c) => CheckBodyGround(c, true);
    void OnCollisionStay(Collision c) => CheckBodyGround(c, false);

    void CheckBodyGround(Collision c, bool reportImpact)
    {
        if (reportImpact && c.impulse.magnitude > 200f)
            OnImpact?.Invoke(c.impulse.magnitude, c.GetContact(0).point);

        if (BodyTouchedGround) return;

        for (int i = 0; i < c.contactCount; i++)
        {
            // 用本地座標判斷：接觸點在腳的高度以上＝身體碰到地了，不管當下轉成什麼姿勢
            Vector3 local = transform.InverseTransformPoint(c.GetContact(i).point);
            if (local.y > footLocalHeight && c.GetContact(i).normal.y > 0.4f)
            {
                BodyTouchedGround = true;
                ReleaseGrip();
                return;
            }
        }
    }

    // ---------- 小工具 ----------

    public Vector3 Forward()
    {
        Vector3 f = transform.forward; f.y = 0f;
        return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
    }

    /// <summary>本地高度 h 的世界座標（施力點用）。</summary>
    public Vector3 PointAt(float h) => transform.TransformPoint(new Vector3(0f, h, 0f));

    Vector3 HandPoint() => transform.TransformPoint(new Vector3(0f, cfg.shoulderHeight, 0.45f));

    /// <summary>給自測用：對手在不在攻擊範圍內。</summary>
    public bool CanReach(float range, float angle) => InFront(range, angle);

    bool InFront(float range, float angle)
    {
        if (opponent == null) return false;
        Vector3 to = opponent.transform.position - transform.position; to.y = 0f;
        if (to.magnitude > range) return false;
        return Vector3.Angle(Forward(), to.normalized) <= angle;
    }

    /// <summary>立合い：開局瞬間出手有加成（真實相撲最關鍵的一瞬）。</summary>
    float TachiaiScale()
        => Time.time - matchStartTime <= cfg.tachiaiWindow ? cfg.tachiaiBonus : 1f;

    /// <summary>腳的水平位置離土俵中心多遠（出界判定用）。</summary>
    public float DistanceFromCenter(Vector3 center)
    {
        Vector3 p = transform.position - center; p.y = 0f;
        return p.magnitude;
    }
}
