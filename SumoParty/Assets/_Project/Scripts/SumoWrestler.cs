using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SumoWrestler : MonoBehaviour
{
    // (命中位置, 是否重擊) — JuiceManager 訂閱做音效/粒子/頓幀
    public static event System.Action<Vector3, bool> PushHit;
    public static event System.Action<Vector3> Dodged;

    public SumoParams P;
    public string DisplayName = "力士";
    public SumoWrestler Opponent;
    public WrestlerVisuals Visuals;

    public bool Eliminated { get; private set; }
    public float Stamina { get; private set; }
    public bool IsBracing => braceTimer > 0f;
    public bool ControlLocked { get; set; }   // 立合前/勝負後由 MatchManager 鎖定

    Rigidbody rb;
    Vector2 moveInput;
    float pushCd, dodgeCd, braceTimer, braceCd, stunTimer, knockTimer;
    Vector3 spawnPos;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.centerOfMass = new Vector3(0f, 0.55f, 0f);   // 低重心：推撞時沉、穩、有份量
        spawnPos = transform.position;
        Stamina = P != null ? P.staminaMax : 100f;
    }

    public void SetMove(Vector2 dir) => moveInput = Vector2.ClampMagnitude(dir, 1f);

    public bool TryPush()
    {
        if (!CanAct() || pushCd > 0f) return false;
        pushCd = P.pushCooldown;
        float factor = Mathf.Lerp(P.tiredPushMultiplier, 1f, Stamina / P.staminaMax);
        Stamina = Mathf.Max(0f, Stamina - P.staminaCostPerPush);
        rb.AddForce(transform.forward * P.pushLunge, ForceMode.VelocityChange);
        Visuals?.Stretch(0.7f);   // 突進瞬間拉長，出手有勁

        if (Opponent != null && !Opponent.Eliminated)
        {
            Vector3 to = Opponent.transform.position - transform.position;
            to.y = 0f;
            if (to.magnitude <= P.pushRange && Vector3.Angle(transform.forward, to) < P.pushAngle)
            {
                Opponent.ReceivePush(to.normalized * P.pushForce * factor);
                Vector3 mid = (transform.position + Opponent.transform.position) * 0.5f + Vector3.up * 0.9f;
                PushHit?.Invoke(mid, factor > 0.75f);
                return true;
            }
        }
        return false;
    }

    public bool TryDodge(float sideSign)
    {
        if (!CanAct() || dodgeCd > 0f) return false;
        dodgeCd = P.dodgeCooldown;
        rb.AddForce(transform.right * Mathf.Sign(sideSign) * P.dodgeImpulse, ForceMode.VelocityChange);
        Dodged?.Invoke(transform.position);
        return true;
    }

    public bool TryBrace()
    {
        if (!CanAct() || braceCd > 0f) return false;
        braceTimer = P.braceDuration;
        braceCd = P.braceDuration + P.braceCooldown;
        return true;
    }

    public void ReceivePush(Vector3 impulse)
    {
        if (Eliminated) return;
        if (IsBracing) impulse *= (1f - P.braceResist);
        knockTimer = 0.5f;
        // 微量上抬：被撞飛時稍微離地彈跳，衝擊看得見
        impulse += Vector3.up * impulse.magnitude * 0.14f;
        rb.AddForce(impulse, ForceMode.VelocityChange);
        Visuals?.Squash(Mathf.Clamp01(impulse.magnitude / 8f));
    }

    public void MarkEliminated()
    {
        Eliminated = true;
        moveInput = Vector2.zero;
        // 出局交還完整物理：解鎖旋轉、順著飛行方向翻滾摔出去
        rb.constraints = RigidbodyConstraints.None;
        Vector3 dir = rb.linearVelocity.sqrMagnitude > 0.1f ? rb.linearVelocity.normalized : transform.forward;
        Vector3 axis = Vector3.Cross(Vector3.up, dir);
        rb.AddTorque(axis * 6f + Random.insideUnitSphere * 1.5f, ForceMode.VelocityChange);
    }

    public void ResetState(Vector3 pos)
    {
        transform.position = pos;
        // 翻滾後回正：只保留朝向，直立鎖回旋轉
        Vector3 fwd = transform.forward; fwd.y = 0f;
        transform.rotation = Quaternion.LookRotation(fwd.sqrMagnitude > 0.01f ? fwd : Vector3.forward);
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        Eliminated = false;
        Stamina = P.staminaMax;
        pushCd = dodgeCd = braceTimer = braceCd = stunTimer = knockTimer = 0f;
        moveInput = Vector2.zero;
    }

    bool CanAct() => !Eliminated && !ControlLocked && stunTimer <= 0f && braceTimer <= 0f;

    void Update()
    {
        float dt = Time.deltaTime;
        pushCd -= dt; dodgeCd -= dt; braceTimer -= dt; braceCd -= dt;
        stunTimer -= dt; knockTimer -= dt;

        if (pushCd <= 0f && !Eliminated)
            Stamina = Mathf.Min(P.staminaMax, Stamina + P.staminaRegenPerSec * dt);

        // 永遠面向對手（灰盒版自動轉向）
        if (Opponent != null && !Eliminated)
        {
            Vector3 to = Opponent.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(to), 10f * dt);
        }
    }

    void FixedUpdate()
    {
        if (Eliminated) return; // 出局後交給物理自由落體

        Vector3 v = rb.linearVelocity;
        Vector3 target = CanAct()
            ? transform.TransformDirection(new Vector3(moveInput.x, 0f, moveInput.y)) * P.moveSpeed
            : Vector3.zero;

        // 被推飛的 0.5 秒內操控力變弱，讓衝量吃得出來
        float control = knockTimer > 0f ? 2f : 8f;
        Vector3 h = Vector3.Lerp(new Vector3(v.x, 0f, v.z), target, control * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(h.x, v.y, h.z);
    }
}
