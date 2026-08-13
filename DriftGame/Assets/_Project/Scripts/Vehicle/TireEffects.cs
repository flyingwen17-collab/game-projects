using UnityEngine;

/// 輪胎視覺回饋：胎痕與胎煙的濃度由「該輪實際用掉多少抓地力」決定，
/// 而不是一個粗略的甩尾開關 —— 所以輕微推頭、鎖死、燒胎的煙量都不一樣。
///
/// v2 重做（根治「奇怪的紅線」）：
///  1. 材質一律用 CarFactory 建好的 .mat 資產 —— 舊版在執行期 Shader.Find 生材質，
///     打包後 shader 被剝除，整條胎痕變成洋紅色錯誤線。
///  2. 胎痕跟隨 CarController 的真實接地點與路面法線 —— 舊版掛在停用的
///     WheelCollider 錨點上（不隨懸吊移動），線會浮空或插進地面。
[RequireComponent(typeof(CarController))]
public class TireEffects : MonoBehaviour
{
    [Header("材質（CarFactory 指定的資產，執行期不再 Shader.Find）")]
    public Material skidMaterial;
    public Material smokeMaterial;

    [Header("門檻")]
    [Tooltip("抓地力使用率超過此值開始留胎痕")]
    public float markThreshold = 0.86f;
    [Tooltip("超過此值開始冒煙")]
    public float smokeThreshold = 0.90f;
    public float maxSmokeRate = 130f;

    CarController car;
    TrailRenderer[] trails;
    ParticleSystem[] smokes;
    Vector3[] lastContact;

    void Start()
    {
        car = GetComponent<CarController>();
        trails = new TrailRenderer[4];
        smokes = new ParticleSystem[4];
        lastContact = new Vector3[4];

        for (int i = 0; i < 4; i++)
        {
            trails[i] = MakeTrail(i);
            smokes[i] = MakeSmoke(i);
        }
    }

    TrailRenderer MakeTrail(int i)
    {
        var go = new GameObject("SkidTrail" + i);
        go.transform.SetParent(transform, true);   // world 座標由每幀更新，parent 只管生命週期

        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 14f;
        tr.startWidth = 0.24f;
        tr.endWidth = 0.20f;
        tr.minVertexDistance = 0.08f;
        tr.alignment = LineAlignment.TransformZ;   // 面在 XY 平面 → Z 軸對齊路面法線就平貼地
        tr.numCapVertices = 2;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;

        if (skidMaterial != null) tr.sharedMaterial = skidMaterial;
        tr.emitting = false;
        return tr;
    }

    ParticleSystem MakeSmoke(int i)
    {
        var go = new GameObject("TireSmoke" + i);
        go.transform.SetParent(transform, true);

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startColor = new Color(0.9f, 0.89f, 0.87f, 0.30f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 420;
        main.gravityModifier = -0.035f;   // 煙會緩緩上飄

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.16f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.55f, 1f, 2.6f));

        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.z = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.86f, 0.86f, 0.88f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.34f, 0.12f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (smokeMaterial != null) renderer.sharedMaterial = smokeMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }

    void Update()
    {
        if (car == null || trails == null) return;
        bool active = car.enabled;

        for (int i = 0; i < 4; i++)
        {
            if (trails[i] == null) continue;

            bool grounded = active && car.Grounded[i];
            float usage = grounded ? car.GripUsage[i] : 0f;

            Vector3 contact = car.ContactPoint[i];
            Vector3 normal = grounded ? car.ContactNormal[i] : Vector3.up;

            // 車被重置/瞬移時把舊軌跡剪斷，否則會拉出一條橫跨全場的直線
            if ((contact - lastContact[i]).sqrMagnitude > 25f) trails[i].Clear();
            lastContact[i] = contact;

            // 貼在路面上方一點點，Z 軸對齊法線 → 胎痕平貼路面（含斜坡）
            trails[i].transform.SetPositionAndRotation(
                contact + normal * 0.03f, Quaternion.LookRotation(normal));

            // 草地/泥土：塵土是棕色的、輕滑就揚起；柏油才是白色燒胎煙
            bool offroad = grounded && car.SurfaceMu[i] < 0.8f;
            float mThresh = offroad ? 0.55f : markThreshold;
            float sThresh = offroad ? 0.58f : smokeThreshold;

            // 泥地不留黑胎痕；幾乎靜止時也不畫（避免原地疊出黑點）
            trails[i].emitting = grounded && !offroad && usage > mThresh && car.SpeedKmh > 5f;

            smokes[i].transform.position = contact + normal * 0.1f;

            var main = smokes[i].main;
            main.startColor = offroad ? new Color(0.58f, 0.48f, 0.36f, 0.32f)
                                      : new Color(0.9f, 0.89f, 0.87f, 0.30f);

            var emission = smokes[i].emission;
            if (grounded && usage > sThresh)
            {
                float t = Mathf.InverseLerp(sThresh, 1f, usage);
                // 車速也影響煙量：靜止空轉的煙比高速甩尾少
                float speedScale = Mathf.Clamp01(car.SpeedKmh / 40f);
                emission.rateOverTime = maxSmokeRate * t * Mathf.Max(0.25f, speedScale);
            }
            else
            {
                emission.rateOverTime = 0f;
            }
        }
    }
}
