using UnityEditor;
using UnityEngine;

/// 組裝可開的車：物理（WheelCollider + 真實規格）不變，
/// 視覺改用 Kenney Car Kit 的 FBX 模型；模型缺席時退回舊的方塊拼裝。
/// 三台玩家車：WRX（race 塗裝賽車）、FIT（hatchback-sports 熱掀背）、AE86（sedan-sports 雙門）。
/// 交通 NPC 可用 visualOverride 指定民用車款（taxi / van / suv…）。
public static class CarFactory
{
    public enum Style { WRX, FIT, AE86 }

    const string MaterialsDir = "Assets/_Project/Materials";

    public static GameObject Build(Style style, Vector3 pos, Quaternion rot, string visualOverride = null)
    {
        var car = new GameObject("Car_" + style);
        // 組裝期間保持無旋轉 —— 量測模型邊界用的是世界軸 AABB，
        // 斜著量會把長度灌水、縮放算錯。最後才套上目標朝向。
        car.transform.position = pos;

        var rb = car.AddComponent<Rigidbody>();
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.6f;
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

        // ---- Kenney 車體模型（視覺主體）----
        // WRX 用一般四門房車（"race" 是方程式賽車，開在公路上完全出戲）
        string bodyModel = visualOverride ?? (style == Style.WRX ? "sedan"
                          : style == Style.FIT ? "hatchback-sports"
                          : "sedan-sports");
        var kitBody = KenneyLib.Place("cars", bodyModel, car.transform,
                                      car.transform.position, car.transform.rotation);
        bool kit = kitBody != null;
        if (kit)
        {
            var b = KenneyLib.MeasureBounds(kitBody);
            // 模型長軸可能在 X 或 Z：統一轉到車頭朝 +Z
            if (b.size.x > b.size.z)
                kitBody.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            float rawLen = Mathf.Max(b.size.x, b.size.z);
            kitBody.transform.localScale = Vector3.one * ((bodySize.z + 0.4f) / rawLen);

            // 底盤略低於輪心 → 輪拱包住輪胎上緣，不會像方塊時期那樣「浮」在輪子上
            b = KenneyLib.MeasureBounds(kitBody);
            kitBody.transform.position += Vector3.up *
                (car.transform.position.y - wheelRadius * 0.55f - b.min.y);
            KenneyLib.CenterXZ(kitBody, car.transform.position);

            // 金屬清漆車漆：真車的漆面是有反射的。只換車身模型的材質複本，
            // 不動 Kenney 共用材質（否則整座城市的建築會跟著變亮面）。
            ApplyCarPaint(kitBody);
        }

        // ---- 碰撞體（方塊）：有模型時只留碰撞、不畫 ----
        float bodyY = 0.32f + bodySize.y * 0.5f;
        var body = Part(car, "Body", new Vector3(0f, bodyY, 0f), bodySize, bodyMat, true);

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
        var cabin = Part(car, "Cabin", cabinPos, cabinSize, glassMat, true);

        // 車殼物理材質：低摩擦 + 一點彈性 —— 擦牆會滑開、對撞會分離，
        // 而不是像預設材質那樣「黏」在障礙物上原地卡死。
        var bodyPhys = PhysMat("CarBodyPhys", 0.22f, 0.12f);
        body.GetComponent<Collider>().sharedMaterial = bodyPhys;
        cabin.GetComponent<Collider>().sharedMaterial = bodyPhys;

        if (kit)
        {
            StripVisual(body);
            StripVisual(cabin);
        }

        float halfL = bodySize.z * 0.5f;
        float topY = bodyY + bodySize.y * 0.5f;

        // ---- 方塊時期的風格細節：只有沒模型時才拼 ----
        if (!kit)
        {
            if (style == Style.WRX)
            {
                Part(car, "HoodScoop", new Vector3(0f, topY + 0.07f, halfL - 0.85f), new Vector3(0.55f, 0.14f, 0.5f), accentMat, false);
                Part(car, "WingPostL", new Vector3(-0.55f, topY + 0.18f, -halfL + 0.28f), new Vector3(0.08f, 0.36f, 0.12f), accentMat, false);
                Part(car, "WingPostR", new Vector3(0.55f, topY + 0.18f, -halfL + 0.28f), new Vector3(0.08f, 0.36f, 0.12f), accentMat, false);
                Part(car, "WingPlank", new Vector3(0f, topY + 0.38f, -halfL + 0.28f), new Vector3(1.65f, 0.06f, 0.38f), accentMat, false);
            }
            else if (style == Style.AE86)
            {
                Part(car, "BlackHood", new Vector3(0f, topY + 0.012f, halfL - 0.75f), new Vector3(1.55f, 0.03f, 1.4f), accentMat, false);
                Part(car, "DuckTail", new Vector3(0f, topY + 0.06f, -halfL + 0.12f), new Vector3(1.5f, 0.08f, 0.22f), accentMat, false);
            }
            Part(car, "BumperF", new Vector3(0f, bodyY - bodySize.y * 0.35f, halfL + 0.06f), new Vector3(bodySize.x * 0.98f, 0.22f, 0.14f), darkMat, false);
            Part(car, "BumperR", new Vector3(0f, bodyY - bodySize.y * 0.35f, -halfL - 0.06f), new Vector3(bodySize.x * 0.98f, 0.22f, 0.14f), darkMat, false);
        }

        // 車燈方塊一律保留：CarLights 靠它們的 emission 做開燈效果（夜晚吃 Bloom）。
        // 有模型時縮小、貼在車頭尾表面，看起來就是燈殼。
        var headMat = Mat("Headlight", new Color(1f, 0.97f, 0.85f), 0.9f);
        var tailMat = Mat("Taillight", new Color(0.8f, 0.06f, 0.05f), 0.85f);
        float lightY = kit ? bodyY - 0.06f : bodyY + 0.08f;
        Vector3 lightSize = kit ? new Vector3(0.24f, 0.09f, 0.05f) : new Vector3(0.32f, 0.12f, 0.06f);
        float lightX = bodySize.x * (kit ? 0.28f : 0.33f);
        var headL = Part(car, "HeadL", new Vector3(-lightX, lightY, halfL + 0.015f), lightSize, headMat, false);
        var headR = Part(car, "HeadR", new Vector3(lightX, lightY, halfL + 0.015f), lightSize, headMat, false);
        var tailL = Part(car, "TailL", new Vector3(-lightX, lightY, -halfL - 0.015f), lightSize, tailMat, false);
        var tailR = Part(car, "TailR", new Vector3(lightX, lightY, -halfL - 0.015f), lightSize, tailMat, false);

        // ---- 輪子 ----
        float axleF = halfL - 0.72f;
        float axleR = -halfL + 0.72f;
        float trackHalf = bodySize.x * 0.5f - 0.05f;
        if (kit)
        {
            // Kenney 車身比真實規格寬，輪距跟著模型實寬走，輪子才不會整顆縮進車殼裡
            var kb = KenneyLib.MeasureBounds(kitBody);
            trackHalf = Mathf.Max(trackHalf, kb.size.x * 0.5f - 0.12f);
        }
        Vector3[] wheelPos =
        {
            new Vector3(-trackHalf, 0f, axleF),
            new Vector3( trackHalf, 0f, axleF),
            new Vector3(-trackHalf, 0f, axleR),
            new Vector3( trackHalf, 0f, axleR),
        };
        string[] names = { "FL", "FR", "RL", "RR" };
        // 玩家車配賽車圈、民用車配原廠圈
        string wheelModel = visualOverride != null ? "wheel-default" : "wheel-racing";

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

            GameObject kitWheel = kit
                ? KenneyLib.Place("cars", wheelModel, pivot.transform,
                                  pivot.transform.position, pivot.transform.rotation)
                : null;
            if (kitWheel != null)
            {
                // 右側輪鏡像（輪圈造型有內外之分）
                if (wheelPos[i].x > 0f)
                    kitWheel.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                // 視覺放大 1.3 倍：Kenney 車體是卡通比例，照真實胎徑裝會像玩具滑板輪。
                // 物理半徑不動，輪心往上抬讓「放大後的胎底」仍貼地。
                const float visualScale = 1.3f;
                var wb = KenneyLib.MeasureBounds(kitWheel);
                float dia = Mathf.Max(wb.size.y, wb.size.z);
                kitWheel.transform.localScale = Vector3.one * (wheelRadius * 2f * visualScale / dia);
                // 邊界中心對齊輪心（模型樞紐不一定在中心）
                wb = KenneyLib.MeasureBounds(kitWheel);
                kitWheel.transform.position += pivot.transform.position - wb.center
                                               + Vector3.up * (wheelRadius * (visualScale - 1f));
            }
            else
            {
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
            }

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

        // FX 材質全部做成資產：執行期 new Material(Shader.Find) 在打包後
        // 會因 shader 被剝除而變成洋紅色（胎痕變「紅線」的元凶）。
        var smokeTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/FX_Smoke.png");
        var sparkTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/FX_Spark.png");
        var flameTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/FX_Flame.png");
        var skidMat = FxMat("FxSkidMark", "Universal Render Pipeline/Unlit",
                            null, new Color(0.035f, 0.035f, 0.04f, 0.78f), false);
        var smokeMat = FxMat("FxTireSmoke", "Universal Render Pipeline/Particles/Unlit",
                             smokeTex, Color.white, false);
        var sparkMat = FxMat("FxSpark", "Universal Render Pipeline/Particles/Unlit",
                             sparkTex, Color.white, true);
        var flameMat = FxMat("FxFlame", "Universal Render Pipeline/Particles/Unlit",
                             flameTex, Color.white, true);

        var effects = car.AddComponent<TireEffects>();
        effects.skidMaterial = skidMat;
        effects.smokeMaterial = smokeMat;

        var lights = car.AddComponent<CarLights>();
        lights.headlightRenderers = new[] { headL.GetComponent<Renderer>(), headR.GetComponent<Renderer>() };
        lights.taillightRenderers = new[] { tailL.GetComponent<Renderer>(), tailR.GetComponent<Renderer>() };
        lights.headlightLocalLeft = headL.transform.localPosition + new Vector3(0f, 0f, 0.1f);
        lights.headlightLocalRight = headR.transform.localPosition + new Vector3(0f, 0f, 0.1f);

        var impact = car.AddComponent<CollisionImpact>();
        impact.sparkTexture = sparkTex;
        impact.sparkMaterial = sparkMat;
        impact.impactClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/impact.wav");

        var audio = car.AddComponent<EngineAudio>();
        audio.engineLowClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/engine_low.wav");
        audio.engineMidClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/engine_mid.wav");
        audio.engineHighClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/engine_high.wav");
        audio.skidClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/skid_loop.wav");
        audio.brakeClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/brake_squeal.wav");
        audio.gearShiftClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/gearshift.wav");
        audio.backfireClips = new[]
        {
            AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/backfire.wav"),
            AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/backfire_2.wav"),
            AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/backfire_3.wav"),
        };
        audio.exhaustFlame = BuildExhaustFlame(car, -halfL - 0.05f);

        // 組裝完才套目標朝向（見開頭註解）
        car.transform.rotation = rot;

        return car;
    }

    /// 拿掉渲染、留下碰撞體（Kenney 模型接手視覺後，方塊只當物理外殼）。
    static void StripVisual(GameObject go)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) Object.DestroyImmediate(mr);
        var mf = go.GetComponent<MeshFilter>();
        if (mf != null) Object.DestroyImmediate(mf);
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
        renderer.sharedMaterial = FxMat("FxFlame", "Universal Render Pipeline/Particles/Unlit",
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/FX_Flame.png"),
            Color.white, true);

        return ps;
    }

    /// 特效材質資產：透明（alpha 混合）或相加混合（additive，會發光吃 Bloom）。
    /// 存成 .mat 才會被打包收錄 shader，執行期生成的材質在 build 裡會變洋紅。
    static Material FxMat(string name, string shaderName, Texture2D tex, Color color, bool additive)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find(shaderName));
            System.IO.Directory.CreateDirectory(MaterialsDir);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find(shaderName);
        if (tex != null) mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Surface", 1f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", additive ? (int)UnityEngine.Rendering.BlendMode.One
                                         : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        UnityEditor.EditorUtility.SetDirty(mat);
        return mat;
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

    /// 車漆材質：以原材質為底做「Paint_」複本資產（metallic + 高 smoothness 的清漆感）
    static void ApplyCarPaint(GameObject body)
    {
        foreach (var r in body.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null) mats[i] = PaintVersion(mats[i]);
            r.sharedMaterials = mats;
        }
    }

    static Material PaintVersion(Material src)
    {
        string path = MaterialsDir + "/Paint_" + src.name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        System.IO.Directory.CreateDirectory(MaterialsDir);
        var mat = new Material(src)
        {
            name = "Paint_" + src.name,
        };
        mat.SetFloat("_Metallic", 0.42f);
        mat.SetFloat("_Smoothness", 0.8f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// 物理材質資產（摩擦取小、彈性取大 —— 牆的材質再高摩擦也拖不住低摩擦的車殼）
    public static PhysicsMaterial PhysMat(string name, float friction, float bounce)
    {
        string path = MaterialsDir + "/" + name + ".physicMaterial";
        var existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (existing != null) return existing;
        System.IO.Directory.CreateDirectory(MaterialsDir);
        var pm = new PhysicsMaterial(name)
        {
            dynamicFriction = friction,
            staticFriction = friction,
            bounciness = bounce,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Maximum,
        };
        AssetDatabase.CreateAsset(pm, path);
        return pm;
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
