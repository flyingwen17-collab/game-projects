using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// P2 正式場館場景：可愛力士、土俵+俵繩+吊屋頂、AI 觀眾席與幟旗、光照後處理、粒子與音訊接線。
/// batch 進入點：BuildAll（合成音訊 → 入庫 AI 圖 → 建場景 → 截圖）
public static class P2SceneBuilder
{
    const string ScenePath = "Assets/_Project/Scenes/Arena.unity";
    const string ParamsPath = "Assets/_Project/Config/SumoParams.asset";
    const float PlatformRadius = 4.2f;   // 土俵臺面半徑
    const float RopeRadius = 3.6f;       // 俵繩＝勝負圈半徑

    public static void BuildAll()
    {
        SumoAudioSynth.GenerateAll();
        StyleImport.ProcessAll();
        BuildArena();
        Capture();
    }

    [MenuItem("Sumo/Build Arena Scene (P2)")]
    public static void BuildArena()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- 參數 ----
        var p = AssetDatabase.LoadAssetAtPath<SumoParams>(ParamsPath);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<SumoParams>();
            Directory.CreateDirectory("Assets/_Project/Config");
            AssetDatabase.CreateAsset(p, ParamsPath);
        }

        // ---- 環境光 / 霧 ----
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.52f, 0.47f, 0.44f);
        RenderSettings.ambientEquatorColor = new Color(0.36f, 0.31f, 0.28f);
        RenderSettings.ambientGroundColor = new Color(0.14f, 0.11f, 0.10f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.10f, 0.08f, 0.08f);
        RenderSettings.fogDensity = 0.012f;

        // ---- 燈光 ----
        var sunGo = new GameObject("Sun", typeof(Light));
        var sun = sunGo.GetComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.93f, 0.82f);
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sunGo.transform.rotation = Quaternion.Euler(55f, -25f, 0f);

        var spotGo = new GameObject("DohyoSpot", typeof(Light));
        var spot = spotGo.GetComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color(1f, 0.95f, 0.85f);
        spot.intensity = 45f;
        spot.range = 20f;
        spot.spotAngle = 75f;
        spot.shadows = LightShadows.None;
        spotGo.transform.position = new Vector3(0f, 9f, 0f);
        spotGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // ---- 材質 ----
        var texDohyo = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/dohyo_surface.png");
        var texCrowd = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/crowd_stand.png");
        var texBanner = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/nobori_banner.png");
        var texDust = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/fx_dust.png");
        var texImpact = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/fx_impact.png");

        var matClay = MakeLit("ClaySide", new Color(0.62f, 0.50f, 0.36f));
        var matDohyoTop = MakeLit("DohyoTop", Color.white, texDohyo);
        var matTawara = MakeLit("Tawara", new Color(0.80f, 0.68f, 0.42f));
        var matFloor = MakeLit("ArenaFloor", new Color(0.30f, 0.17f, 0.13f));
        var matRoof = MakeLit("RoofWood", new Color(0.36f, 0.22f, 0.14f));
        var matValance = MakeLit("RoofValance", new Color(0.36f, 0.16f, 0.55f));
        var matCrowd = MakeLit("CrowdStand", Color.white, texCrowd, emissive: 0.5f);
        var matBanner = MakeCutout("Nobori", texBanner);
        var matSkin1 = MakeLit("Skin1", new Color(0.95f, 0.77f, 0.60f));
        var matSkin2 = MakeLit("Skin2", new Color(0.86f, 0.64f, 0.45f));
        var matMawashiB = MakeLit("MawashiBlue", new Color(0.16f, 0.36f, 0.85f));
        var matMawashiR = MakeLit("MawashiRed", new Color(0.85f, 0.22f, 0.16f));
        var matHair = MakeLit("Hair", new Color(0.12f, 0.10f, 0.10f));
        var matEye = MakeLit("Eye", new Color(0.08f, 0.07f, 0.07f));
        var matBlush = MakeLit("Blush", new Color(0.95f, 0.55f, 0.48f));

        // ---- 土俵（整組掛在 DohyoRoot 下，縮圈時一起縮放）----
        var dohyoRoot = new GameObject("DohyoRoot");

        var baseCyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseCyl.name = "DohyoBase";
        Object.DestroyImmediate(baseCyl.GetComponent<CapsuleCollider>());
        baseCyl.AddComponent<MeshCollider>();
        baseCyl.transform.SetParent(dohyoRoot.transform);
        baseCyl.transform.localScale = new Vector3(PlatformRadius * 2f, 1.25f, PlatformRadius * 2f);
        baseCyl.transform.localPosition = new Vector3(0f, -1.25f, 0f);   // 頂面 y=0，臺高 2.5
        baseCyl.GetComponent<MeshRenderer>().sharedMaterial = matClay;

        var top = new GameObject("DohyoTop", typeof(MeshFilter), typeof(MeshRenderer));
        top.transform.SetParent(dohyoRoot.transform);
        top.transform.localPosition = new Vector3(0f, 0.012f, 0f);
        top.GetComponent<MeshFilter>().sharedMesh = DiscMesh(PlatformRadius, 40);
        top.GetComponent<MeshRenderer>().sharedMaterial = matDohyoTop;

        for (int i = 0; i < 28; i++)   // 俵繩：一圈稻草袋
        {
            float a = i / 28f * Mathf.PI * 2f;
            var bale = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bale.name = "Tawara_" + i;
            Object.DestroyImmediate(bale.GetComponent<CapsuleCollider>());
            bale.transform.SetParent(dohyoRoot.transform);
            bale.transform.localPosition = new Vector3(Mathf.Sin(a) * RopeRadius, 0.07f, Mathf.Cos(a) * RopeRadius);
            bale.transform.localRotation = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            bale.transform.localScale = new Vector3(0.16f, 0.38f, 0.16f);
            bale.GetComponent<MeshRenderer>().sharedMaterial = matTawara;
        }

        // ---- 場館地板 ----
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "ArenaFloor";
        floor.transform.localScale = Vector3.one * 8f;
        floor.transform.position = new Vector3(0f, -2.5f, 0f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = matFloor;

        // ---- 吊屋頂 ----
        var roof = new GameObject("Roof");
        roof.transform.position = new Vector3(0f, 6.4f, 0f);
        for (int i = 0; i < 4; i++)
        {
            float yaw = i * 90f;
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "RoofPanel_" + i;
            Object.DestroyImmediate(panel.GetComponent<BoxCollider>());
            panel.transform.SetParent(roof.transform);
            panel.transform.localRotation = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.Euler(30f, 0f, 0f);
            panel.transform.localPosition = Quaternion.AngleAxis(yaw, Vector3.up) * new Vector3(0f, 0.35f, 1.6f);
            panel.transform.localScale = new Vector3(6.2f, 0.14f, 2.8f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = matRoof;

            var val = GameObject.CreatePrimitive(PrimitiveType.Cube);
            val.name = "Valance_" + i;
            Object.DestroyImmediate(val.GetComponent<BoxCollider>());
            val.transform.SetParent(roof.transform);
            val.transform.localRotation = Quaternion.AngleAxis(yaw, Vector3.up);
            val.transform.localPosition = Quaternion.AngleAxis(yaw, Vector3.up) * new Vector3(0f, -0.35f, 2.85f);
            val.transform.localScale = new Vector3(6.0f, 0.6f, 0.08f);
            val.GetComponent<MeshRenderer>().sharedMaterial = matValance;
        }
        var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cap.name = "RoofCap";
        Object.DestroyImmediate(cap.GetComponent<BoxCollider>());
        cap.transform.SetParent(roof.transform);
        cap.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        cap.transform.localScale = new Vector3(2.4f, 0.55f, 2.4f);
        cap.GetComponent<MeshRenderer>().sharedMaterial = matRoof;
        // 四色房（相撲屋頂四角的黑白紅綠流蘇）
        Color[] fusa = { Color.black, Color.white, new Color(0.8f, 0.15f, 0.1f), new Color(0.15f, 0.55f, 0.2f) };
        for (int i = 0; i < 4; i++)
        {
            float a = (i + 0.5f) * 90f * Mathf.Deg2Rad;
            var tas = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tas.name = "Fusa_" + i;
            Object.DestroyImmediate(tas.GetComponent<CapsuleCollider>());
            tas.transform.SetParent(roof.transform);
            tas.transform.localPosition = new Vector3(Mathf.Sin(a) * 3.4f, -0.85f, Mathf.Cos(a) * 3.4f);
            tas.transform.localScale = new Vector3(0.09f, 0.45f, 0.09f);
            tas.GetComponent<MeshRenderer>().sharedMaterial = MakeLit("Fusa" + i, fusa[i]);
        }

        // ---- 觀眾席（AI 圖 billboard，兩層弧形）----
        MakeStands(matCrowd, 10.5f, -0.9f, 6.5f, 3.2f, 6, 0f);
        MakeStands(matCrowd, 12.5f, 2.3f, 7.5f, 3.6f, 6, 12f);

        // ---- 幟旗 ----
        if (texBanner != null)
        {
            float[] angles = { -125f, -95f, -60f, 60f, 95f, 125f };
            var camPos = new Vector3(0f, 6.5f, -8.5f);
            for (int i = 0; i < angles.Length; i++)
            {
                float a = angles[i] * Mathf.Deg2Rad;
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Nobori_" + i;
                Object.DestroyImmediate(q.GetComponent<MeshCollider>());
                var pos = new Vector3(Mathf.Sin(a) * 7.2f, -0.7f, Mathf.Cos(a) * 7.2f);
                q.transform.position = pos;
                q.transform.localScale = new Vector3(1.7f, 3.6f, 1f);
                Vector3 toCam = pos - camPos; toCam.y = 0f;
                q.transform.rotation = Quaternion.LookRotation(toCam);   // Quad 正面朝 -Z → 面向鏡頭
                q.GetComponent<MeshRenderer>().sharedMaterial = matBanner;
            }
        }

        // ---- 力士 ----
        // y=0.05：膠囊碰撞體底部剛好貼土俵面，不會開場浮空下墜
        var w1 = MakeRikishi("P1_Blue", new Vector3(-1.6f, 0.05f, 0f), matSkin1, matMawashiB, matHair, matEye, matBlush, p, "藍方");
        var w2 = MakeRikishi("P2_Red", new Vector3(1.6f, 0.05f, 0f), matSkin2, matMawashiR, matHair, matEye, matBlush, p, "紅方");
        w1.Opponent = w2;
        w2.Opponent = w1;
        w1.transform.rotation = Quaternion.Euler(0f, 90f, 0f);    // 開場即對望
        w2.transform.rotation = Quaternion.Euler(0f, -90f, 0f);

        // ---- 相機 + 後處理 ----
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CameraRig));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.055f, 0.06f);
        var rig = camGo.GetComponent<CameraRig>();
        rig.A = w1.transform;
        rig.B = w2.transform;
        camGo.transform.position = rig.BasePosition;
        camGo.transform.LookAt(rig.LookTarget);
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, "Assets/_Project/Config/SumoPostFX.asset");
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.value = 0.7f;
        bloom.threshold.value = 1.1f;
        var tone = profile.Add<Tonemapping>(true);
        tone.mode.value = TonemappingMode.ACES;
        var vig = profile.Add<Vignette>(true);
        vig.intensity.value = 0.28f;
        var colAdj = profile.Add<ColorAdjustments>(true);
        colAdj.postExposure.value = 0.15f;
        colAdj.saturation.value = 8f;
        var volGo = new GameObject("PostFX", typeof(Volume));
        var vol = volGo.GetComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;

        // ---- 流程 + 輸入 ----
        var mmGo = new GameObject("MatchManager", typeof(MatchManager));
        var mm = mmGo.GetComponent<MatchManager>();
        mm.P = p;
        mm.P1 = w1;
        mm.P2 = w2;
        mm.Dohyo = dohyoRoot.transform;
        mm.BaseRadius = RopeRadius;

        var d1 = mmGo.AddComponent<PlayerDriver>();
        d1.W = w1; d1.MouseTouchEnabled = true;
        var d2 = mmGo.AddComponent<PlayerDriver>();
        d2.W = w2; d2.ArrowScheme = true; d2.enabled = false;
        var npc = mmGo.AddComponent<NpcBrain>();
        npc.W = w2; npc.Match = mm;
        mm.P2Driver = d2;
        mm.P2Npc = npc;

        // ---- 打擊感 + 粒子 + 音訊 ----
        var juiceGo = new GameObject("Juice", typeof(JuiceManager));
        var juice = juiceGo.GetComponent<JuiceManager>();
        juice.Rig = rig;
        juice.BgmLoop = LoadClip("bgm_taiko");
        juice.CrowdLoop = LoadClip("crowd_loop");
        juice.SfxHit = LoadClip("sfx_hit");
        juice.SfxBigSlam = LoadClip("sfx_bigslam");
        juice.SfxRingOut = LoadClip("sfx_ringout");
        juice.SfxStart = LoadClip("sfx_start");
        juice.SfxDodge = LoadClip("sfx_dodge");
        juice.DustPs = MakeBurstPs(juiceGo.transform, "FX_Dust", texDust, false, 14,
            0.5f, 1.0f, 1.5f, 3f, 0.4f, 0.8f, new Color(0.92f, 0.85f, 0.72f, 0.95f));
        juice.ImpactPs = MakeBurstPs(juiceGo.transform, "FX_Impact", texImpact, true, 1,
            1.8f, 1.8f, 0f, 0f, 0.2f, 0.25f, Color.white);
        juice.RingOutPs = MakeBurstPs(juiceGo.transform, "FX_RingOut", texDust, false, 26,
            0.8f, 1.6f, 3f, 6f, 0.6f, 1.0f, new Color(0.95f, 0.88f, 0.75f, 0.95f));

        Directory.CreateDirectory("Assets/_Project/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[P2SceneBuilder] Arena scene built: " + ScenePath);
    }

    // ---------- 力士組裝：一套素體，之後靠縮放與換色生五角 ----------
    static SumoWrestler MakeRikishi(string name, Vector3 pos, Material skin, Material mawashi,
        Material hair, Material eye, Material blush, SumoParams p, string display)
    {
        var root = new GameObject(name);
        root.transform.position = pos;
        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.95f, 0f);
        col.radius = 0.62f;
        col.height = 1.9f;
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 100f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        void Part(string n, PrimitiveType type, Vector3 lp, Vector3 ls, Material m)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = n;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(root.transform);
            g.transform.localPosition = lp;
            g.transform.localScale = ls;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        Part("Belly", PrimitiveType.Sphere, new Vector3(0f, 0.85f, 0f), new Vector3(1.35f, 1.15f, 1.2f), skin);
        Part("Head", PrimitiveType.Sphere, new Vector3(0f, 1.75f, 0.05f), Vector3.one * 0.8f, skin);
        Part("Topknot", PrimitiveType.Sphere, new Vector3(0f, 2.16f, 0.02f), new Vector3(0.24f, 0.13f, 0.34f), hair);
        Part("EyeL", PrimitiveType.Sphere, new Vector3(-0.14f, 1.80f, 0.44f), Vector3.one * 0.09f, eye);
        Part("EyeR", PrimitiveType.Sphere, new Vector3(0.14f, 1.80f, 0.44f), Vector3.one * 0.09f, eye);
        Part("BlushL", PrimitiveType.Sphere, new Vector3(-0.27f, 1.66f, 0.35f), new Vector3(0.13f, 0.08f, 0.06f), blush);
        Part("BlushR", PrimitiveType.Sphere, new Vector3(0.27f, 1.66f, 0.35f), new Vector3(0.13f, 0.08f, 0.06f), blush);
        Part("ArmL", PrimitiveType.Sphere, new Vector3(-0.78f, 0.95f, 0.18f), Vector3.one * 0.42f, skin);
        Part("ArmR", PrimitiveType.Sphere, new Vector3(0.78f, 0.95f, 0.18f), Vector3.one * 0.42f, skin);
        Part("Mawashi", PrimitiveType.Cylinder, new Vector3(0f, 0.60f, 0f), new Vector3(1.42f, 0.17f, 1.30f), mawashi);
        Part("MawashiFront", PrimitiveType.Cube, new Vector3(0f, 0.42f, 0.58f), new Vector3(0.30f, 0.45f, 0.07f), mawashi);
        Part("FootL", PrimitiveType.Sphere, new Vector3(-0.30f, 0.12f, 0.06f), new Vector3(0.36f, 0.22f, 0.46f), skin);
        Part("FootR", PrimitiveType.Sphere, new Vector3(0.30f, 0.12f, 0.06f), new Vector3(0.36f, 0.22f, 0.46f), skin);

        var w = root.AddComponent<SumoWrestler>();
        w.P = p;
        w.DisplayName = display;
        return w;
    }

    // ---------- 觀眾席弧形 ----------
    static void MakeStands(Material mat, float radius, float centerY, float w, float h, int count, float angleOffset)
    {
        for (int i = 0; i < count; i++)
        {
            float ang = (-60f + i * 24f + angleOffset) * Mathf.Deg2Rad;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Crowd_" + radius + "_" + i;
            Object.DestroyImmediate(q.GetComponent<MeshCollider>());
            var pos = new Vector3(Mathf.Sin(ang) * radius, centerY, Mathf.Cos(ang) * radius);
            q.transform.position = pos;
            q.transform.localScale = new Vector3(w, h, 1f);
            Vector3 outward = pos; outward.y = 0f;
            q.transform.rotation = Quaternion.LookRotation(outward);   // -Z 朝場中 → 從場中看得到
            q.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    // ---------- 材質 ----------
    static Material MakeLit(string name, Color c, Texture2D tex = null, float emissive = 0f)
    {
        string dir = "Assets/_Project/Materials";
        Directory.CreateDirectory(dir);
        string path = dir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(sh != null ? sh : Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        if (tex != null) m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Smoothness", 0.25f);
        if (emissive > 0f && tex != null)
        {
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", tex);
            m.SetColor("_EmissionColor", Color.white * emissive);
        }
        return m;
    }

    static Material MakeCutout(string name, Texture2D tex)
    {
        var m = MakeLit(name, Color.white, tex);
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Cull", 0f);   // 雙面
        m.renderQueue = 2450;
        return m;
    }

    // ---------- 粒子 ----------
    static ParticleSystem MakeBurstPs(Transform parent, string name, Texture2D tex, bool additive, int count,
        float sizeMin, float sizeMax, float spdMin, float spdMax, float lifeMin, float lifeMax, Color color)
    {
        var go = new GameObject(name, typeof(ParticleSystem));
        go.transform.SetParent(parent);
        var ps = go.GetComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(spdMin, spdMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = color;
        main.gravityModifier = 0.05f;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.25f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        string dir = "Assets/_Project/Materials";
        Directory.CreateDirectory(dir);
        string path = dir + "/PS_" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            mat = new Material(sh != null ? sh : Shader.Find("Particles/Standard Unlit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        if (tex != null) mat.SetTexture("_BaseMap", tex);
        mat.SetFloat("_Surface", 1f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        return ps;
    }

    // ---------- 圓盤網格（土俵頂面，UV 平面展開避免圓柱蓋 UV 亂掉）----------
    static Mesh DiscMesh(float radius, int segments)
    {
        var mesh = new Mesh { name = "DohyoDisc" };
        var verts = new Vector3[segments + 1];
        var uvs = new Vector2[segments + 1];
        var tris = new int[segments * 3];
        verts[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            verts[i + 1] = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
            uvs[i + 1] = new Vector2(Mathf.Sin(a) * 0.5f + 0.5f, Mathf.Cos(a) * 0.5f + 0.5f);
            int next = (i + 1) % segments + 1;
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = next;
        }
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }

    static AudioClip LoadClip(string name) =>
        AssetDatabase.LoadAssetAtPath<AudioClip>(SumoAudioSynth.AudioDir + "/" + name + ".wav");

    // ---------- 批次截圖 ----------
    [MenuItem("Sumo/Capture Arena Screenshots")]
    public static void Capture()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath);

        string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(outDir);

        var cam = Object.FindObjectOfType<Camera>();
        var rig = cam.GetComponent<CameraRig>();
        if (rig != null) rig.enabled = false;

        Shoot(cam, Path.Combine(outDir, "arena_overview.png"));
        cam.transform.position = new Vector3(3.8f, 2.2f, -5.2f);
        cam.transform.LookAt(new Vector3(0f, 1.1f, 0f));
        Shoot(cam, Path.Combine(outDir, "arena_ringside.png"));
        cam.transform.position = new Vector3(0f, 1.4f, -4.6f);
        cam.transform.LookAt(new Vector3(0f, 1.3f, 0f));
        Shoot(cam, Path.Combine(outDir, "arena_closeup.png"));

        Debug.Log("[P2SceneBuilder] screenshots saved to " + outDir);
    }

    static void Shoot(Camera cam, string file)
    {
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
