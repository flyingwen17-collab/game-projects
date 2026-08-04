using UnityEngine;

/// 輪胎視覺回饋：胎痕與胎煙的濃度由「該輪實際用掉多少抓地力」決定，
/// 而不是一個粗略的甩尾開關 —— 所以輕微推頭、鎖死、燒胎的煙量都不一樣。
[RequireComponent(typeof(CarController))]
public class TireEffects : MonoBehaviour
{
    public WheelCollider[] allWheels = new WheelCollider[4];   // FL FR RL RR

    [Tooltip("輪胎煙貼圖（RGB 白、形狀在 Alpha）")]
    public Texture2D smokeTexture;

    [Header("門檻")]
    [Tooltip("抓地力使用率超過此值開始留胎痕")]
    public float markThreshold = 0.86f;
    [Tooltip("超過此值開始冒煙")]
    public float smokeThreshold = 0.90f;
    public float maxSmokeRate = 130f;

    CarController car;
    TrailRenderer[] trails;
    ParticleSystem[] smokes;

    void Start()
    {
        car = GetComponent<CarController>();
        trails = new TrailRenderer[4];
        smokes = new ParticleSystem[4];

        for (int i = 0; i < 4; i++)
        {
            if (allWheels[i] == null) continue;
            trails[i] = MakeTrail(allWheels[i]);
            // 只有驅動/後輪需要濃煙，前輪給少量即可，省效能
            smokes[i] = MakeSmoke(allWheels[i]);
        }
    }

    TrailRenderer MakeTrail(WheelCollider wheel)
    {
        var go = new GameObject("SkidTrail");
        go.transform.SetParent(wheel.transform, false);
        go.transform.localPosition = new Vector3(0f, -wheel.radius + 0.05f, 0f);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 9f;
        tr.startWidth = 0.26f;
        tr.endWidth = 0.22f;
        tr.minVertexDistance = 0.10f;
        tr.alignment = LineAlignment.TransformZ;
        tr.numCapVertices = 2;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.045f, 0.72f));
        MakeTransparent(mat);
        tr.material = mat;
        tr.emitting = false;
        return tr;
    }

    ParticleSystem MakeSmoke(WheelCollider wheel)
    {
        var go = new GameObject("TireSmoke");
        go.transform.SetParent(wheel.transform, false);
        go.transform.localPosition = new Vector3(0f, -wheel.radius + 0.12f, 0f);

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
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetTexture("_BaseMap", smokeTexture != null ? (Texture)smokeTexture : Texture2D.whiteTexture);
        MakeTransparent(mat);
        renderer.material = mat;
        return ps;
    }

    static void MakeTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    void Update()
    {
        if (car == null) return;
        bool active = car.enabled;

        for (int i = 0; i < 4; i++)
        {
            if (trails[i] == null) continue;

            bool grounded = active && car.Grounded[i];
            float usage = grounded ? car.GripUsage[i] : 0f;

            // 草地/泥土：塵土是棕色的、輕滑就揚起；柏油才是白色燒胎煙
            bool offroad = grounded && car.SurfaceMu[i] < 0.8f;
            float mThresh = offroad ? 0.55f : markThreshold;
            float sThresh = offroad ? 0.58f : smokeThreshold;

            trails[i].emitting = grounded && !offroad && usage > mThresh;   // 泥地不留黑胎痕

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
