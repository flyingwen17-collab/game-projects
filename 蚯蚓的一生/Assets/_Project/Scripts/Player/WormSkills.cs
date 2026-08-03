using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// 蚯蚓技能：F 假蚯蚓誘餌、地下 Shift 突進、衝刺拖尾、鑽土出土特效
public class WormSkills : MonoBehaviour
{
    [Header("誘餌")]
    public float decoyCooldown = 18f;
    public float decoyDuration = 5f;

    [Header("地下突進")]
    public float dashCooldown = 4f;
    public float dashCost = 18f;
    public float dashSpeedBonus = 9f;
    public float dashDuration = 0.35f;

    public static Transform ActiveDecoy { get; private set; }
    public float DecoyCdLeft { get; private set; }
    public float DashCdLeft { get; private set; }

    WormController ctrl;
    BurrowSystem burrow;
    WormStamina stamina;
    TrailRenderer trail;
    bool wasBurrowed;

    void Start()
    {
        ctrl = GetComponent<WormController>();
        burrow = GetComponent<BurrowSystem>();
        stamina = GetComponent<WormStamina>();
        MakeTrail();
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

    void Update()
    {
        DecoyCdLeft = Mathf.Max(0f, DecoyCdLeft - Time.deltaTime);
        DashCdLeft = Mathf.Max(0f, DashCdLeft - Time.deltaTime);

        // 鑽土/出土的髒土特效
        if (burrow.IsBurrowed != wasBurrowed)
        {
            wasBurrowed = burrow.IsBurrowed;
            Vector3 p = new Vector3(transform.position.x, 0.15f, transform.position.z);
            ParticleFx.Burst(p, new Color(0.45f, 0.3f, 0.16f), 26, 3.2f, 0.16f, 1.6f);
            SynthSfx.Play(burrow.IsBurrowed ? "dig" : "surface", 0.7f);
        }

        if (trail != null) trail.emitting = ctrl.IsSprinting;

        var kb = Keyboard.current;
        if (kb == null || GameManager.I == null || GameManager.I.State != GameState.Playing) return;

        // F：假蚯蚓誘餌
        if (kb.fKey.wasPressedThisFrame && DecoyCdLeft <= 0f && !burrow.IsBurrowed)
        {
            DecoyCdLeft = decoyCooldown;
            StartCoroutine(DecoyRoutine());
        }

        // 地下 Shift：土遁突進
        if (burrow.IsBurrowed && kb.leftShiftKey.wasPressedThisFrame &&
            DashCdLeft <= 0f && stamina.Current >= dashCost)
        {
            DashCdLeft = dashCooldown;
            stamina.Restore(-dashCost);
            ctrl.Dash(dashSpeedBonus, dashDuration);
            Vector3 p = new Vector3(transform.position.x, 0.15f, transform.position.z);
            ParticleFx.Burst(p, new Color(0.4f, 0.26f, 0.13f), 34, 4.5f, 0.2f, 1.4f);
            SynthSfx.Play("dash", 0.7f);
        }
    }

    IEnumerator DecoyRoutine()
    {
        SynthSfx.Play("decoy", 0.6f);
        var decoy = new GameObject("WormDecoy");
        decoy.transform.position = transform.position;
        decoy.transform.rotation = transform.rotation;

        var wiggler = decoy.AddComponent<DecoyWiggler>();
        wiggler.Build();

        ActiveDecoy = decoy.transform;
        yield return new WaitForSeconds(decoyDuration);
        ActiveDecoy = null;

        if (decoy != null)
        {
            ParticleFx.Burst(decoy.transform.position + Vector3.up * 0.2f,
                new Color(1f, 0.7f, 0.78f), 20, 2.5f, 0.14f, 0.6f);
            SynthSfx.PlayAt("pop", decoy.transform.position, 0.6f);
            Destroy(decoy);
        }
    }
}

/// 誘餌：原地扭動的假蚯蚓
public class DecoyWiggler : MonoBehaviour
{
    Transform[] balls;

    public void Build()
    {
        balls = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(transform, false);
            b.transform.localPosition = new Vector3(0f, 0.2f, -i * 0.24f);
            b.transform.localScale = Vector3.one * Mathf.Lerp(0.42f, 0.24f, i / 3f);
            b.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(new Color(1f, 0.62f, 0.7f));
            balls[i] = b.transform;
        }
    }

    void Update()
    {
        if (balls == null) return;
        for (int i = 0; i < balls.Length; i++)
        {
            var p = balls[i].localPosition;
            p.x = Mathf.Sin(Time.time * 9f + i * 1.1f) * 0.1f;
            balls[i].localPosition = p;
        }
    }
}
