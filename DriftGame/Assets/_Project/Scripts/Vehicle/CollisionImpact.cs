using UnityEngine;

/// 碰撞打擊感：依撞擊力道分級，噴火花、震鏡頭、播撞擊聲、車身抖動。
/// 力道取自實際的物理衝量，所以輕擦與重撞的回饋差距是真的。
[RequireComponent(typeof(Rigidbody))]
public class CollisionImpact : MonoBehaviour
{
    [Header("素材")]
    public Texture2D sparkTexture;
    public AudioClip impactClip;

    [Header("門檻（衝量，N·s）")]
    public float minImpulse = 400f;      // 低於此值只算擦碰，不觸發
    public float heavyImpulse = 6000f;   // 達到此值為最大回饋

    [Header("回饋強度")]
    public float maxCameraShake = 1.1f;
    public float sparkBurstMax = 90f;
    public float impactVolume = 0.9f;

    ParticleSystem sparks;
    AudioSource audioSrc;
    CameraFollow camFollow;
    float cooldown;

    void Start()
    {
        sparks = BuildSparks();
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 1f;
        audioSrc.minDistance = 8f;
        audioSrc.maxDistance = 120f;
    }

    ParticleSystem BuildSparks()
    {
        var go = new GameObject("ImpactSparks");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 13f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.28f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.4f;
        main.maxParticles = 400;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 42f;
        shape.radius = 0.12f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f),
                    new GradientColorKey(new Color(1f, 0.35f, 0.05f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.06f;
        renderer.lengthScale = 2.2f;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        if (sparkTexture != null) mat.SetTexture("_BaseMap", sparkTexture);
        mat.SetColor("_BaseColor", Color.white);
        // 相加混合，火花才會發亮並吃到 Bloom
        mat.SetFloat("_Surface", 1f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        renderer.material = mat;

        return ps;
    }

    void Update()
    {
        if (cooldown > 0f) cooldown -= Time.deltaTime;
        if (camFollow == null && Camera.main != null)
            camFollow = Camera.main.GetComponent<CameraFollow>();
    }

    void OnCollisionEnter(Collision collision)
    {
        Impact(collision);
    }

    void OnCollisionStay(Collision collision)
    {
        // 沿牆磨蹭也要有持續的火花，但要節流
        if (cooldown > 0f) return;
        if (collision.relativeVelocity.magnitude < 4f) return;
        Impact(collision, 0.35f);
    }

    void Impact(Collision collision, float scale = 1f)
    {
        float impulse = collision.impulse.magnitude;
        if (impulse < minImpulse) return;

        float severity = Mathf.Clamp01((impulse - minImpulse) / (heavyImpulse - minImpulse)) * scale;
        if (severity <= 0.01f) return;

        var contact = collision.GetContact(0);

        // 火花朝碰撞法線的反射方向噴
        sparks.transform.position = contact.point;
        sparks.transform.rotation = Quaternion.LookRotation(
            Vector3.Reflect(-collision.relativeVelocity.normalized, contact.normal));
        sparks.Emit(Mathf.RoundToInt(Mathf.Lerp(8f, sparkBurstMax, severity)));

        if (camFollow != null) camFollow.Shake(Mathf.Lerp(0.12f, maxCameraShake, severity));

        if (impactClip != null && cooldown <= 0f)
        {
            audioSrc.pitch = Random.Range(0.85f, 1.15f) * Mathf.Lerp(1.25f, 0.75f, severity);
            audioSrc.PlayOneShot(impactClip, impactVolume * Mathf.Lerp(0.25f, 1f, severity));
        }

        cooldown = 0.12f;
    }
}
