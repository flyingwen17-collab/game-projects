using UnityEngine;

/// 甩尾視覺回饋：後輪輪胎痕（TrailRenderer）與輪胎煙（ParticleSystem）。
[RequireComponent(typeof(DriftDetector))]
public class TireEffects : MonoBehaviour
{
    public WheelCollider rearLeft;
    public WheelCollider rearRight;

    DriftDetector drift;
    CarController car;
    TrailRenderer[] trails = new TrailRenderer[2];
    ParticleSystem[] smokes = new ParticleSystem[2];

    void Start()
    {
        drift = GetComponent<DriftDetector>();
        car = GetComponent<CarController>();
        var wheels = new[] { rearLeft, rearRight };
        for (int i = 0; i < 2; i++)
        {
            if (wheels[i] == null) continue;
            trails[i] = MakeTrail(wheels[i]);
            smokes[i] = MakeSmoke(wheels[i]);
        }
    }

    TrailRenderer MakeTrail(WheelCollider wheel)
    {
        var go = new GameObject("SkidTrail");
        go.transform.SetParent(wheel.transform, false);
        go.transform.localPosition = new Vector3(0f, -wheel.radius + 0.06f, 0f);
        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 6f;
        tr.startWidth = 0.32f;
        tr.endWidth = 0.28f;
        tr.minVertexDistance = 0.12f;
        tr.alignment = LineAlignment.TransformZ;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.05f, 0.55f));
        MakeTransparent(mat);
        tr.material = mat;
        tr.emitting = false;
        return tr;
    }

    ParticleSystem MakeSmoke(WheelCollider wheel)
    {
        var go = new GameObject("TireSmoke");
        go.transform.SetParent(wheel.transform, false);
        go.transform.localPosition = new Vector3(0f, -wheel.radius + 0.15f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 1.1f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
        main.startColor = new Color(0.85f, 0.85f, 0.85f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 250;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = grad;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetTexture("_BaseMap", MakeSoftCircle());
        MakeTransparent(mat);
        renderer.material = mat;
        return ps;
    }

    static Texture2D circleTex;
    static Texture2D MakeSoftCircle()
    {
        if (circleTex != null) return circleTex;
        int size = 64;
        circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                float a = Mathf.Clamp01(1f - d);
                circleTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        circleTex.Apply();
        return circleTex;
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
        bool active = car != null && car.enabled &&
                      (drift.IsDrifting || (car.Handbrake && car.SpeedKmh > 20f));
        var wheels = new[] { rearLeft, rearRight };
        for (int i = 0; i < 2; i++)
        {
            if (trails[i] == null) continue;
            bool grounded = wheels[i].GetGroundHit(out _);
            trails[i].emitting = active && grounded;
            var emission = smokes[i].emission;
            emission.rateOverTime = active && grounded ? 45f : 0f;
        }
    }
}
