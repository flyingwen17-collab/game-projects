using UnityEngine;
using UnityEngine.InputSystem;

/// 蚯蚓技能：F 斷尾誘餌、地下 Shift 突進、無敵吃雞模式（特殊飼料）、鑽土土痕特效
public class WormSkills : MonoBehaviour
{
    [Header("斷尾")]
    public float tailDropCooldown = 6f;

    [Header("地下突進")]
    public float dashCooldown = 4f;
    public float dashCost = 18f;
    public float dashSpeedBonus = 9f;
    public float dashDuration = 0.35f;

    [Header("無敵模式（特殊飼料）")]
    public float powerDuration = 8f;

    public float TailCdLeft { get; private set; }
    public float DashCdLeft { get; private set; }
    public float PowerTimeLeft { get; private set; }
    public bool PowerActive => PowerTimeLeft > 0f;

    /// 全域查詢：蚯蚓現在是否無敵（雞要逃跑）
    public static WormSkills Instance { get; private set; }

    WormController ctrl;
    BurrowSystem burrow;
    WormStamina stamina;
    WormBody body;
    TrailRenderer trail;
    ParticleSystem digPs; // 常駐土痕發射器（效能：不再每 0.12 秒生一個新物件）
    bool wasBurrowed;
    float auraTimer;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        ctrl = GetComponent<WormController>();
        burrow = GetComponent<BurrowSystem>();
        stamina = GetComponent<WormStamina>();
        body = GetComponent<WormBody>();
        MakeTrail();
        MakeDigEmitter();
    }

    void MakeDigEmitter()
    {
        var go = new GameObject("DigEmitter");
        digPs = go.AddComponent<ParticleSystem>();
        var main = digPs.main;
        main.loop = true;
        main.startLifetime = 0.6f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startColor = new Color(0.42f, 0.28f, 0.15f);
        main.gravityModifier = 1.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;
        var em = digPs.emission;
        em.rateOverTime = 0f;
        var shape = digPs.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;
        go.GetComponent<ParticleSystemRenderer>().material = ParticleFx.SharedMat("dust_puff", false);
        digPs.Play();
    }

    void MakeTrail()
    {
        var go = new GameObject("SprintTrail");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.4f);
        trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.35f;
        trail.startWidth = 0.22f;
        trail.endWidth = 0.02f;
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var m = new Material(shader);
        Color c = new Color(1f, 0.75f, 0.82f, 0.6f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        trail.material = m;
        trail.emitting = false;
    }

    public void ActivatePower()
    {
        PowerTimeLeft = powerDuration;
        SynthSfx.Play("closecall", 0.8f, 0.8f);
        ParticleFx.Burst(transform.position + Vector3.up * 0.3f,
            new Color(1f, 0.85f, 0.2f), 24, 3f, 0.4f, 0.1f, 0.8f, "glow_soft", true);
    }

    void Update()
    {
        TailCdLeft = Mathf.Max(0f, TailCdLeft - Time.deltaTime);
        DashCdLeft = Mathf.Max(0f, DashCdLeft - Time.deltaTime);
        PowerTimeLeft = Mathf.Max(0f, PowerTimeLeft - Time.deltaTime);

        if (ctrl != null) ctrl.SpeedMultiplier = PowerActive ? 1.35f : 1f; // 追得上逃跑的雞

        // 無敵光環
        if (PowerActive)
        {
            auraTimer -= Time.deltaTime;
            if (auraTimer <= 0f)
            {
                auraTimer = 0.18f;
                ParticleFx.Burst(transform.position + Vector3.up * 0.25f,
                    new Color(1f, 0.85f, 0.25f), 3, 1.2f, 0.3f, -0.3f, 0.5f, "glow_soft", true);
            }
        }

        // 鑽土/出土的髒土特效
        if (burrow.IsBurrowed != wasBurrowed)
        {
            wasBurrowed = burrow.IsBurrowed;
            Vector3 p = new Vector3(transform.position.x, 0.15f, transform.position.z);
            ParticleFx.Burst(p, new Color(0.45f, 0.3f, 0.16f), 40, 3.8f, 0.35f, 1.6f, 0.8f, "dust_puff");
            SynthSfx.Play(burrow.IsBurrowed ? "dig" : "surface", 0.7f);
        }

        // 地下移動時的連續土痕——常駐發射器跟著蚯蚓（看起來真的在土裡鑽）
        if (digPs != null)
        {
            var rb = GetComponent<Rigidbody>();
            bool digging = burrow.IsBurrowed && rb != null && rb.velocity.sqrMagnitude > 1f;
            digPs.transform.position = new Vector3(transform.position.x, 0.1f, transform.position.z);
            var em = digPs.emission;
            em.rateOverTime = digging ? 30f : 0f;
        }

        if (trail != null) trail.emitting = ctrl.IsSprinting && !burrow.IsBurrowed;

        var kb = Keyboard.current;
        if (kb == null || GameManager.I == null || GameManager.I.State != GameState.Playing) return;

        // F：斷尾誘餌（掉一節尾巴，雞吃了會飽）
        if (kb.fKey.wasPressedThisFrame && TailCdLeft <= 0f && !burrow.IsBurrowed && body != null)
        {
            Vector3? dropPos = body.Shrink();
            if (dropPos.HasValue)
            {
                TailCdLeft = tailDropCooldown;
                TailPiece.Create(dropPos.Value);
                SynthSfx.Play("pop", 0.7f);
                ParticleFx.Burst(dropPos.Value + Vector3.up * 0.2f,
                    new Color(1f, 0.6f, 0.7f), 12, 2f, 0.25f, 0.6f, 0.5f, "glow_soft", true);
            }
            else
            {
                SynthSfx.Play("peck", 0.3f, 1.6f); // 太短了不能再斷
            }
        }

        // 地下 Shift：土遁突進
        if (burrow.IsBurrowed && kb.leftShiftKey.wasPressedThisFrame &&
            DashCdLeft <= 0f && stamina.Current >= dashCost)
        {
            DashCdLeft = dashCooldown;
            stamina.Restore(-dashCost);
            ctrl.Dash(dashSpeedBonus, dashDuration);
            Vector3 p = new Vector3(transform.position.x, 0.15f, transform.position.z);
            ParticleFx.Burst(p, new Color(0.4f, 0.26f, 0.13f), 34, 4.5f, 0.35f, 1.4f, 0.7f, "dust_puff");
            SynthSfx.Play("dash", 0.7f);
        }
    }

    /// 無敵時撞到雞 = 吃掉牠
    void OnCollisionEnter(Collision c)
    {
        if (!PowerActive) return;
        if (c.gameObject.layer != LayerMask.NameToLayer("Chicken")) return;
        var chicken = c.gameObject.GetComponent<ChickenAI>();
        if (chicken == null) return;

        ParticleFx.Burst(c.transform.position + Vector3.up * 0.6f,
            Color.white, 26, 4f, 0.35f, 0.6f, 1f, "feathers_sheet", false, 4, 2);
        ParticleFx.Burst(c.transform.position + Vector3.up * 0.5f,
            new Color(1f, 0.8f, 0.2f), 10, 2.5f, 0.4f, 0.1f, 0.6f, "glow_soft", true);
        SynthSfx.PlayAt("cluck", c.transform.position, 0.9f, 0.7f);
        SynthSfx.Play("eat", 0.8f, 0.8f);

        if (GameManager.I != null) GameManager.I.ChickenEaten();
        if (body != null) body.Grow(1);
        Destroy(c.gameObject);
    }
}
