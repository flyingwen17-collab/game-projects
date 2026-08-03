using UnityEngine;
using UnityEngine.InputSystem;

/// 蚯蚓移動控制（M0：單一膠囊，尚無身體節段）
[RequireComponent(typeof(Rigidbody))]
public class WormController : MonoBehaviour
{
    [Header("移動速度")]
    public float crawlSpeed = 3.5f;   // 地面爬行
    public float sprintSpeed = 6f;    // 衝刺（耗體力）
    public float burrowSpeed = 5f;    // 地下移動
    public float turnSpeed = 540f;    // 轉向角速度（度/秒）

    public bool IsSprinting { get; private set; }
    /// 雞的震動感知半徑：衝刺或地下移動時會發出「震動」
    public float NoiseRadius { get; private set; }
    /// 速度倍率（無敵模式加速用）
    public float SpeedMultiplier { get; set; } = 1f;

    Rigidbody rb;
    BurrowSystem burrow;
    WormStamina stamina;
    Transform cam;
    Vector3 inputDir;
    float dashTimer;
    float dashBonus;

    /// 短暫加速（地下突進用）
    public void Dash(float speedBonus, float duration)
    {
        dashBonus = speedBonus;
        dashTimer = duration;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        burrow = GetComponent<BurrowSystem>();
        stamina = GetComponent<WormStamina>();
        rb.freezeRotation = true;
    }

    void Start()
    {
        if (Camera.main != null) cam = Camera.main.transform;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || GameManager.I == null || GameManager.I.State != GameState.Playing)
        {
            inputDir = Vector3.zero;
            IsSprinting = false;
            NoiseRadius = 0f;
            return;
        }

        float h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        Vector3 raw = new Vector3(h, 0f, v);
        if (raw.sqrMagnitude > 1f) raw.Normalize();

        // 以攝影機朝向為移動基準
        if (cam != null)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            inputDir = right * raw.x + fwd * raw.z;
        }
        else inputDir = raw;

        bool moving = raw.sqrMagnitude > 0.01f;
        IsSprinting = !burrow.IsBurrowed && kb.leftShiftKey.isPressed && moving &&
                      stamina.Current > 0.5f && !stamina.Exhausted;

        if (burrow.IsBurrowed) NoiseRadius = moving ? 3f : 0f;
        else NoiseRadius = IsSprinting ? 6f : 0f;
    }

    void FixedUpdate()
    {
        float speed = (burrow.IsBurrowed ? burrowSpeed : (IsSprinting ? sprintSpeed : crawlSpeed)) * SpeedMultiplier;

        if (dashTimer > 0f)
        {
            dashTimer -= Time.fixedDeltaTime;
            speed += dashBonus;
        }

        Vector3 moveDir = inputDir.sqrMagnitude > 0.001f ? inputDir : (dashTimer > 0f ? transform.forward : Vector3.zero);
        Vector3 vel = moveDir * speed;

        if (burrow.IsBurrowed)
            rb.velocity = new Vector3(vel.x, 0f, vel.z); // 地下：Y 由 BurrowSystem 控制
        else
            rb.velocity = new Vector3(vel.x, rb.velocity.y, vel.z);

        if (inputDir.sqrMagnitude > 0.001f)
        {
            Quaternion target = Quaternion.LookRotation(inputDir, Vector3.up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, target, turnSpeed * Time.fixedDeltaTime));
        }
    }
}
