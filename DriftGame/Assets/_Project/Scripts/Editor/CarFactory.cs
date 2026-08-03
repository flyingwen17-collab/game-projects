using UnityEditor;
using UnityEngine;

/// 用基本幾何拼出三台真實風格拉力車：WRX（藍/金圈/大尾翼）、FIT（銀色小掀背）、86（熊貓配色雙門）。
public static class CarFactory
{
    public enum Style { WRX, FIT, AE86 }

    const string MaterialsDir = "Assets/_Project/Materials";

    public static GameObject Build(Style style, Vector3 pos, Quaternion rot)
    {
        var car = new GameObject("Car_" + style);
        car.transform.SetPositionAndRotation(pos, rot);

        var rb = car.AddComponent<Rigidbody>();
        rb.drag = 0.05f;
        rb.angularDrag = 0.6f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // 規格先決定：車身幾何、WheelCollider、Rigidbody 全部以它為準，
        // 避免物理輪徑與視覺輪徑對不上（會造成輪胎陷進地面或懸空）。
        CarSpec spec = style == Style.WRX ? CarSpec.WRX()
                     : style == Style.FIT ? CarSpec.FIT()
                     : CarSpec.AE86();

        float wheelRadius;
        Vector3 bodySize;
        Material bodyMat, accentMat, wheelMat;
        var glassMat = Mat("Glass", new Color(0.08f, 0.1f, 0.12f), 0.9f);
        var darkMat = Mat("CarDark", new Color(0.08f, 0.08f, 0.09f), 0.5f);

        switch (style)
        {
            case Style.WRX:
                rb.mass = 1350f;
                wheelRadius = 0.34f;
                bodySize = new Vector3(1.82f, 0.55f, 4.5f);
                bodyMat = Mat("WrxBlue", new Color(0.07f, 0.2f, 0.72f), 0.75f);
                accentMat = darkMat;
                wheelMat = Mat("GoldWheel", new Color(0.85f, 0.68f, 0.15f), 0.6f);
                break;
            case Style.FIT:
                rb.mass = 1080f;
                wheelRadius = 0.31f;
                bodySize = new Vector3(1.72f, 0.6f, 3.85f);
                bodyMat = Mat("FitSilver", new Color(0.68f, 0.7f, 0.73f), 0.8f);
                accentMat = darkMat;
                wheelMat = Mat("DarkWheel", new Color(0.15f, 0.15f, 0.16f), 0.4f);
                break;
            default: // AE86
                rb.mass = 970f;
                wheelRadius = 0.33f;
                bodySize = new Vector3(1.72f, 0.48f, 4.25f);
                bodyMat = Mat("PandaWhite", new Color(0.93f, 0.93f, 0.9f), 0.7f);
                accentMat = Mat("PandaBlack", new Color(0.05f, 0.05f, 0.05f), 0.6f);
                wheelMat = Mat("DarkWheel", new Color(0.15f, 0.15f, 0.16f), 0.4f);
                break;
        }

        // 一律採用規格的真實胎徑（由胎規換算），覆寫上面的美術暫定值
        wheelRadius = spec.wheelRadiusM;
        rb.mass = spec.massKg;

        // ---- 車身 ----
        float bodyY = 0.32f + bodySize.y * 0.5f;
        var body = Part(car, "Body", new Vector3(0f, bodyY, 0f), bodySize, bodyMat, true);

        // 車艙（含車窗色）
        Vector3 cabinSize; Vector3 cabinPos;
        switch (style)
        {
            case Style.WRX:
                cabinSize = new Vector3(1.6f, 0.48f, 2.1f);
                cabinPos = new Vector3(0f, bodyY + bodySize.y * 0.5f + cabinSize.y * 0.5f - 0.03f, -0.25f);
                break;
            case Style.FIT:
                cabinSize = new Vector3(1.58f, 0.62f, 2.6f);
                cabinPos = new Vector3(0f, bodyY + bodySize.y * 0.5f + cabinSize.y * 0.5f - 0.03f, -0.05f);
                break;
            default:
                cabinSize = new Vector3(1.55f, 0.4f, 1.9f);
                cabinPos = new Vector3(0f, bodyY + bodySize.y * 0.5f + cabinSize.y * 0.5f - 0.03f, -0.4f);
                break;
        }
        Part(car, "Cabin", cabinPos, cabinSize, glassMat, true);

        float halfL = bodySize.z * 0.5f;
        float topY = bodyY + bodySize.y * 0.5f;

        // ---- 風格細節 ----
        if (style == Style.WRX)
        {
            // 引擎蓋進氣孔
            Part(car, "HoodScoop", new Vector3(0f, topY + 0.07f, halfL - 0.85f), new Vector3(0.55f, 0.14f, 0.5f), accentMat, false);
            // 拉力大尾翼
            Part(car, "WingPostL", new Vector3(-0.55f, topY + 0.18f, -halfL + 0.28f), new Vector3(0.08f, 0.36f, 0.12f), accentMat, false);
            Part(car, "WingPostR", new Vector3(0.55f, topY + 0.18f, -halfL + 0.28f), new Vector3(0.08f, 0.36f, 0.12f), accentMat, false);
            Part(car, "WingPlank", new Vector3(0f, topY + 0.38f, -halfL + 0.28f), new Vector3(1.65f, 0.06f, 0.38f), accentMat, false);
        }
        else if (style == Style.AE86)
        {
            // 熊貓黑引擎蓋 + 小鴨尾
            Part(car, "BlackHood", new Vector3(0f, topY + 0.012f, halfL - 0.75f), new Vector3(1.55f, 0.03f, 1.4f), accentMat, false);
            Part(car, "DuckTail", new Vector3(0f, topY + 0.06f, -halfL + 0.12f), new Vector3(1.5f, 0.08f, 0.22f), accentMat, false);
        }

        // 保險桿與車燈
        Part(car, "BumperF", new Vector3(0f, bodyY - bodySize.y * 0.35f, halfL + 0.06f), new Vector3(bodySize.x * 0.98f, 0.22f, 0.14f), darkMat, false);
        Part(car, "BumperR", new Vector3(0f, bodyY - bodySize.y * 0.35f, -halfL - 0.06f), new Vector3(bodySize.x * 0.98f, 0.22f, 0.14f), darkMat, false);
        var headMat = Mat("Headlight", new Color(1f, 0.97f, 0.85f), 0.9f);
        var tailMat = Mat("Taillight", new Color(0.8f, 0.06f, 0.05f), 0.85f);
        var headL = Part(car, "HeadL", new Vector3(-bodySize.x * 0.33f, bodyY + 0.08f, halfL + 0.015f), new Vector3(0.32f, 0.12f, 0.06f), headMat, false);
        var headR = Part(car, "HeadR", new Vector3(bodySize.x * 0.33f, bodyY + 0.08f, halfL + 0.015f), new Vector3(0.32f, 0.12f, 0.06f), headMat, false);
        var tailL = Part(car, "TailL", new Vector3(-bodySize.x * 0.33f, bodyY + 0.08f, -halfL - 0.015f), new Vector3(0.32f, 0.1f, 0.06f), tailMat, false);
        var tailR = Part(car, "TailR", new Vector3(bodySize.x * 0.33f, bodyY + 0.08f, -halfL - 0.015f), new Vector3(0.32f, 0.1f, 0.06f), tailMat, false);

        // ---- 輪子 ----
        float axleF = halfL - 0.72f;
        float axleR = -halfL + 0.72f;
        float trackHalf = bodySize.x * 0.5f - 0.05f;
        Vector3[] wheelPos =
        {
            new Vector3(-trackHalf, 0f, axleF),
            new Vector3( trackHalf, 0f, axleF),
            new Vector3(-trackHalf, 0f, axleR),
            new Vector3( trackHalf, 0f, axleR),
        };
        string[] names = { "FL", "FR", "RL", "RR" };

        var colliderRoot = new GameObject("WheelColliders");
        colliderRoot.transform.SetParent(car.transform, false);
        var meshRoot = new GameObject("WheelMeshes");
        meshRoot.transform.SetParent(car.transform, false);

        var colliders = new WheelCollider[4];
        var meshes = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            var wcGo = new GameObject("Wheel" + names[i]);
            wcGo.transform.SetParent(colliderRoot.transform, false);
            wcGo.transform.localPosition = wheelPos[i];
            var wc = wcGo.AddComponent<WheelCollider>();
            wc.radius = wheelRadius;
            wc.suspensionDistance = 0.22f;
            wc.mass = 22f;
            var spring = wc.suspensionSpring;
            spring.spring = rb.mass * 30f;
            spring.damper = rb.mass * 3.6f;
            spring.targetPosition = 0.5f;
            wc.suspensionSpring = spring;
            colliders[i] = wc;

            var pivot = new GameObject("Wheel" + names[i] + "_Pivot");
            pivot.transform.SetParent(meshRoot.transform, false);
            pivot.transform.localPosition = wheelPos[i];

            var tire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tire.name = "Tire";
            Object.DestroyImmediate(tire.GetComponent<Collider>());
            tire.transform.SetParent(pivot.transform, false);
            tire.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            tire.transform.localScale = new Vector3(wheelRadius * 2f, 0.115f, wheelRadius * 2f);
            tire.GetComponent<MeshRenderer>().sharedMaterial = darkMat;

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Rim";
            Object.DestroyImmediate(rim.GetComponent<Collider>());
            rim.transform.SetParent(pivot.transform, false);
            rim.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            rim.transform.localScale = new Vector3(wheelRadius * 1.25f, 0.118f, wheelRadius * 1.25f);
            rim.GetComponent<MeshRenderer>().sharedMaterial = wheelMat;

            meshes[i] = pivot.transform;
        }

        // ---- 元件 ----
        car.AddComponent<DriftDetector>();
        car.AddComponent<DriftScoring>();
        var ctrl = car.AddComponent<CarController>();
        ctrl.wheelFL = colliders[0];
        ctrl.wheelFR = colliders[1];
        ctrl.wheelRL = colliders[2];
        ctrl.wheelRR = colliders[3];

        // 規格全部採用市售車公開真實數據（見 CarSpec）
        ctrl.spec = spec;
        ctrl.counterSteerAssist = style == Style.WRX ? 0.28f   // 四驅本來就穩，輔助給少一點
                                : style == Style.FIT ? 0.22f   // 前驅甩尾靠慣性，輔助幫不上太多
                                : 0.38f;                       // 後驅最好甩，也最需要反打

        var visuals = car.AddComponent<CarVisuals>();
        visuals.wheels = new CarVisuals.WheelPair[4];
        for (int i = 0; i < 4; i++)
            visuals.wheels[i] = new CarVisuals.WheelPair { collider = colliders[i], mesh = meshes[i] };

        var effects = car.AddComponent<TireEffects>();
        effects.allWheels = colliders;
        effects.smokeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/FX_Smoke.png");

        var lights = car.AddComponent<CarLights>();
        lights.headlightRenderers = new[] { headL.GetComponent<Renderer>(), headR.GetComponent<Renderer>() };
        lights.taillightRenderers = new[] { tailL.GetComponent<Renderer>(), tailR.GetComponent<Renderer>() };
        lights.headlightLocalLeft = headL.transform.localPosition + new Vector3(0f, 0f, 0.1f);
        lights.headlightLocalRight = headR.transform.localPosition + new Vector3(0f, 0f, 0.1f);

        var impact = car.AddComponent<CollisionImpact>();
        impact.sparkTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/FX_Spark.png");
        impact.impactClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/impact.wav");

        var audio = car.AddComponent<EngineAudio>();
        audio.engineClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/engine_loop.wav");
        audio.skidClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/skid_loop.wav");
        audio.brakeClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/brake_squeal.wav");
        audio.backfireClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/backfire.wav");
        audio.exhaustFlame = BuildExhaustFlame(car, -halfL - 0.05f);

        return car;
    }

    /// 排氣管回火火焰：只在回火時 Emit，平時不噴。
    static ParticleSystem BuildExhaustFlame(GameObject car, float rearZ)
    {
        var go = new GameObject("ExhaustFlame");
        go.transform.SetParent(car.transform, false);
        go.transform.localPosition = new Vector3(0.32f, 0.28f, rearZ);
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 朝車尾噴

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.75f, 0.35f), new Color(1f, 0.45f, 0.12f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.04f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.75f, 0.85f, 1f), 0f),      // 根部偏藍
                    new GradientColorKey(new Color(1f, 0.6f, 0.15f), 0.4f),
                    new GradientColorKey(new Color(0.5f, 0.12f, 0.02f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        var flameTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/FX_Flame.png");
        if (flameTex != null) mat.SetTexture("_BaseMap", flameTex);
        mat.SetColor("_BaseColor", Color.white);
        // 相加混合 → 會發光並吃到 Bloom
        mat.SetFloat("_Surface", 1f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        renderer.material = mat;

        return ps;
    }

    static GameObject Part(GameObject parent, string name, Vector3 localPos, Vector3 size, Material mat, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static Material Mat(string name, Color color, float smoothness)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        System.IO.Directory.CreateDirectory(MaterialsDir);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
