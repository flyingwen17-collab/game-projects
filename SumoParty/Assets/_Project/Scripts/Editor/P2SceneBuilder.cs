using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// P2 正式場館場景：程序化一體成形力士、場館圓頂+燈籠、四聚光燈氛圍、粒子與音訊接線。
/// batch 進入點：BuildAll（合成音訊 → 入庫 AI 圖 → 建場景 → 截圖）
public static class P2SceneBuilder
{
    const string ScenePath = "Assets/_Project/Scenes/Arena.unity";
    const string ParamsPath = "Assets/_Project/Config/SumoParams.asset";
    const float PlatformRadius = 4.2f;   // 土俵臺面半徑
    const float RopeRadius = 3.6f;       // 俵繩＝勝負圈半徑

    // 力士身體輪廓（半徑, 高度）：肚子最寬 0.68、脖子收 0.34、頭 0.47，一條曲線到頂
    static readonly Vector2[] BodyProfile =
    {
        new Vector2(0.001f, 0.16f),
        new Vector2(0.34f, 0.20f),
        new Vector2(0.58f, 0.48f),
        new Vector2(0.68f, 0.90f),
        new Vector2(0.60f, 1.22f),
        new Vector2(0.42f, 1.44f),
        new Vector2(0.30f, 1.58f),
        new Vector2(0.44f, 1.70f),
        new Vector2(0.48f, 1.90f),
        new Vector2(0.42f, 2.10f),
        new Vector2(0.20f, 2.24f),
        new Vector2(0.001f, 2.30f),
    };

    public static void BuildAll()
    {
        SumoAudioSynth.GenerateAll();
        StyleImport.ProcessAll();
        BuildArena();
        Capture();
    }

    /// 迭代用：跳過音訊合成與圖片入庫，只重建場景+截圖
    public static void Rebuild()
    {
        BuildArena();
        Capture();
    }

    [MenuItem("Sumo/Build Arena Scene (P2)")]
    public static void BuildArena()
    {
        FixRenderPipeline();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- 參數 ----
        var p = AssetDatabase.LoadAssetAtPath<SumoParams>(ParamsPath);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<SumoParams>();
            Directory.CreateDirectory("Assets/_Project/Config");
            AssetDatabase.CreateAsset(p, ParamsPath);
        }

        // ---- 環境光 / 霧：夜場館，暖色舞台光是主角 ----
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.36f, 0.31f, 0.30f);
        RenderSettings.ambientEquatorColor = new Color(0.23f, 0.19f, 0.18f);
        RenderSettings.ambientGroundColor = new Color(0.10f, 0.08f, 0.08f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.055f, 0.042f, 0.045f);
        RenderSettings.fogDensity = 0.0075f;

        // ---- 燈光：低強度定向光補輪廓，四盞暖聚光打土俵 ----
        var sunGo = new GameObject("FillLight", typeof(Light));
        var sun = sunGo.GetComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(0.85f, 0.80f, 0.90f);   // 偏冷的補光，和暖聚光拉出層次
        sun.intensity = 0.35f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.55f;
        sunGo.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

        for (int i = 0; i < 4; i++)
        {
            float yaw = (45f + i * 90f) * Mathf.Deg2Rad;
            var spotGo = new GameObject("StageSpot_" + i, typeof(Light));
            var spot = spotGo.GetComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = new Color(1f, 0.93f, 0.78f);
            spot.intensity = 46f;
            spot.range = 24f;
            spot.spotAngle = 62f;
            spot.innerSpotAngle = 28f;
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.75f;
            spotGo.transform.position = new Vector3(Mathf.Sin(yaw) * 5.5f, 8.2f, Mathf.Cos(yaw) * 5.5f);
            spotGo.transform.LookAt(new Vector3(0f, 0.3f, 0f));
        }

        // ---- 材質 ----
        var texDohyo = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/dohyo_surface.png");
        var texCrowd = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/crowd_stand.png");
        var texBanner = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/nobori_banner.png");
        var texDust = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/fx_dust.png");
        var texImpact = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/fx_impact.png");

        var matClay = MakeLit("ClaySide", new Color(0.48f, 0.35f, 0.25f), smooth: 0.15f);
        var matDohyoTop = MakeLit("DohyoTop", Color.white, texDohyo, smooth: 0.12f);
        var matTawara = MakeLit("Tawara", new Color(0.85f, 0.73f, 0.48f), smooth: 0.2f);
        var matFloor = MakeLit("ArenaFloor", new Color(0.14f, 0.10f, 0.09f), smooth: 0.1f);
        var matShell = MakeGlow("ArenaShell", new Color(0.15f, 0.105f, 0.085f), new Color(0.30f, 0.17f, 0.12f), 0.22f);
        var matRoof = MakeLit("RoofWood", new Color(0.30f, 0.185f, 0.11f), smooth: 0.3f);
        var matValance = MakeLit("RoofValance", new Color(0.30f, 0.11f, 0.45f), smooth: 0.35f);
        var matGold = MakeGlow("GoldTrim", new Color(0.9f, 0.72f, 0.30f), new Color(0.9f, 0.65f, 0.25f), 0.35f);
        var matLantern = MakeGlow("Lantern", new Color(1f, 0.72f, 0.42f), new Color(1f, 0.68f, 0.38f), 1.8f);
        var matRope = MakeLit("LanternRope", new Color(0.20f, 0.15f, 0.11f), smooth: 0.1f);
        var matCrowd = MakeLit("CrowdStand", Color.white, texCrowd, emissive: 0.45f);
        var matBanner = MakeCutout("Nobori", texBanner);
        var matSkin1 = MakeLit("Skin1", new Color(0.97f, 0.80f, 0.62f), smooth: 0.42f);
        var matSkin2 = MakeLit("Skin2", new Color(0.88f, 0.66f, 0.47f), smooth: 0.42f);
        var matMawashiB = MakeLit("MawashiBlue", new Color(0.18f, 0.42f, 0.95f), smooth: 0.60f);
        var matMawashiR = MakeLit("MawashiRed", new Color(0.92f, 0.22f, 0.16f), smooth: 0.60f);
        var matHair = MakeLit("Hair", new Color(0.11f, 0.09f, 0.09f), smooth: 0.6f);
        var matEye = MakeLit("Eye", new Color(0.07f, 0.06f, 0.06f), smooth: 0.85f);
        var matBlush = MakeLit("Blush", new Color(0.96f, 0.56f, 0.48f), smooth: 0.3f);

        // ---- 物理材質：土俵有摩擦、力士外皮滑（移動由速度控制）----
        var pmDohyo = MakePhysMat("PM_Dohyo", 0.7f, 0.6f, 0f);
        var pmWrestler = MakePhysMat("PM_Wrestler", 0.08f, 0.06f, 0.12f);

        // ---- 程序化網格（一次生成，兩位力士共用）----
        var meshBody = SumoMeshFactory.Lathe("SumoBody", BodyProfile);
        var meshMawashi = SumoMeshFactory.Lathe("SumoMawashi", MawashiProfile());
        var armPath = new[]
        {
            new Vector3(0.42f, 1.32f, 0.02f),   // 肩（深埋體內，絕不浮空）
            new Vector3(0.76f, 1.06f, 0.10f),   // 肘（抬高讓開腰帶）
            new Vector3(0.72f, 0.76f, 0.32f),   // 腕（撐在身側，備戰架勢）
        };
        var meshArmR = SumoMeshFactory.Tube("SumoArmR", armPath, 0.16f);
        var meshArmL = SumoMeshFactory.MirrorX("SumoArmL", meshArmR);
        var meshShell = SumoMeshFactory.ArenaShell("ArenaShellMesh", 15f, -2.5f, 11f);
        var meshDisc = SumoMeshFactory.Disc("DohyoDisc", PlatformRadius);

        // ---- 土俵（整組掛在 DohyoRoot 下，縮圈時一起縮放）----
        var dohyoRoot = new GameObject("DohyoRoot");

        var baseCyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseCyl.name = "DohyoBase";
        Object.DestroyImmediate(baseCyl.GetComponent<CapsuleCollider>());
        var baseCol = baseCyl.AddComponent<MeshCollider>();
        baseCol.material = pmDohyo;
        baseCyl.transform.SetParent(dohyoRoot.transform);
        baseCyl.transform.localScale = new Vector3(PlatformRadius * 2f, 1.25f, PlatformRadius * 2f);
        baseCyl.transform.localPosition = new Vector3(0f, -1.25f, 0f);   // 頂面 y=0，臺高 2.5
        baseCyl.GetComponent<MeshRenderer>().sharedMaterial = matClay;

        var top = new GameObject("DohyoTop", typeof(MeshFilter), typeof(MeshRenderer));
        top.transform.SetParent(dohyoRoot.transform);
        top.transform.localPosition = new Vector3(0f, 0.012f, 0f);
        top.GetComponent<MeshFilter>().sharedMesh = meshDisc;
        top.GetComponent<MeshRenderer>().sharedMaterial = matDohyoTop;

        for (int i = 0; i < 24; i++)   // 俵繩：一圈稻草袋（半埋進土面）
        {
            float a = i / 24f * Mathf.PI * 2f;
            var bale = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bale.name = "Tawara_" + i;
            Object.DestroyImmediate(bale.GetComponent<CapsuleCollider>());
            bale.transform.SetParent(dohyoRoot.transform);
            bale.transform.localPosition = new Vector3(Mathf.Sin(a) * RopeRadius, 0.05f, Mathf.Cos(a) * RopeRadius);
            bale.transform.localRotation = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            bale.transform.localScale = new Vector3(0.20f, 0.30f, 0.20f);
            bale.GetComponent<MeshRenderer>().sharedMaterial = matTawara;
        }

        // ---- 場館地板（暗色，讓土俵成為視覺焦點）----
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "ArenaFloor";
        floor.transform.localScale = Vector3.one * 8f;
        floor.transform.position = new Vector3(0f, -2.5f, 0f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = matFloor;
        floor.GetComponent<MeshCollider>().material = pmDohyo;

        // ---- 場館圓頂：把黑色虛空包起來 ----
        var shell = new GameObject("ArenaShell", typeof(MeshFilter), typeof(MeshRenderer));
        shell.GetComponent<MeshFilter>().sharedMesh = meshShell;
        shell.GetComponent<MeshRenderer>().sharedMaterial = matShell;

        // ---- 燈籠串：兩圈暖光點（吊繩到天花板），Bloom 之後會發光 ----
        MakeLanternRing(matLantern, matRope, 13.5f, 5.2f, 20, 11f);
        MakeLanternRing(matLantern, matRope, 8.5f, 8.4f, 14, 11f);

        // ---- 中場空氣光：微亮的暖點光，讓圓頂與屋頂不至於死黑 ----
        var airGo = new GameObject("AirGlow", typeof(Light));
        var air = airGo.GetComponent<Light>();
        air.type = LightType.Point;
        air.color = new Color(1f, 0.72f, 0.45f);
        air.intensity = 9f;
        air.range = 26f;
        air.shadows = LightShadows.None;
        airGo.transform.position = new Vector3(0f, 7f, 0f);

        // ---- 吊屋頂（不投影：四盞聚光燈會把屋頂投成十字黑影蓋在土俵上）----
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

            var trim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trim.name = "GoldTrim_" + i;
            Object.DestroyImmediate(trim.GetComponent<BoxCollider>());
            trim.transform.SetParent(roof.transform);
            trim.transform.localRotation = Quaternion.AngleAxis(yaw, Vector3.up);
            trim.transform.localPosition = Quaternion.AngleAxis(yaw, Vector3.up) * new Vector3(0f, -0.68f, 2.85f);
            trim.transform.localScale = new Vector3(6.0f, 0.07f, 0.09f);
            trim.GetComponent<MeshRenderer>().sharedMaterial = matGold;
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

        foreach (var r in roof.GetComponentsInChildren<MeshRenderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // ---- 觀眾席 ----
        // 內圈：真 3D 姿勢人物（Quaternius CC0）坐在階梯座台上，鏡頭附近經得起看；
        // 外圈上層：AI 圖 billboard 當遠景人海。兩者疊出縱深。
        MakeCrowd3D(matFloor);
        MakeStands(matCrowd, 12.5f, 2.3f, 7.5f, 3.6f, 6, 12f);

        // ---- 幟旗 ----
        var camBase = new Vector3(0f, 4.8f, -7.4f);
        if (texBanner != null)
        {
            float[] angles = { -125f, -95f, -60f, 60f, 95f, 125f };
            for (int i = 0; i < angles.Length; i++)
            {
                float a = angles[i] * Mathf.Deg2Rad;
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Nobori_" + i;
                Object.DestroyImmediate(q.GetComponent<MeshCollider>());
                var pos = new Vector3(Mathf.Sin(a) * 7.2f, -0.7f, Mathf.Cos(a) * 7.2f);
                q.transform.position = pos;
                q.transform.localScale = new Vector3(1.7f, 3.6f, 1f);
                Vector3 toCam = pos - camBase; toCam.y = 0f;
                q.transform.rotation = Quaternion.LookRotation(toCam);   // Quad 正面朝 -Z → 面向鏡頭
                q.GetComponent<MeshRenderer>().sharedMaterial = matBanner;
            }
        }

        // ---- 力士 ----
        var w1 = MakeRikishi("P1_Blue", new Vector3(-1.6f, 0.05f, 0f), meshBody, meshMawashi, meshArmL, meshArmR,
            matSkin1, matMawashiB, matHair, matEye, matBlush, pmWrestler, p, "藍方");
        var w2 = MakeRikishi("P2_Red", new Vector3(1.6f, 0.05f, 0f), meshBody, meshMawashi, meshArmL, meshArmR,
            matSkin2, matMawashiR, matHair, matEye, matBlush, pmWrestler, p, "紅方");
        w1.Opponent = w2;
        w2.Opponent = w1;
        w1.transform.rotation = Quaternion.Euler(0f, 90f, 0f);    // 開場即對望
        w2.transform.rotation = Quaternion.Euler(0f, -90f, 0f);

        // ---- 相機 + 後處理 ----
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CameraRig));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.03f, 0.035f);
        var rig = camGo.GetComponent<CameraRig>();
        rig.A = w1.transform;
        rig.B = w2.transform;
        rig.BasePosition = camBase;
        rig.LookTarget = new Vector3(0f, 1.0f, 0f);
        camGo.transform.position = rig.BasePosition;
        camGo.transform.LookAt(rig.LookTarget);
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, "Assets/_Project/Config/SumoPostFX.asset");
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.value = 1.0f;
        bloom.threshold.value = 0.95f;
        bloom.scatter.value = 0.65f;
        var tone = profile.Add<Tonemapping>(true);
        tone.mode.value = TonemappingMode.ACES;
        var vig = profile.Add<Vignette>(true);
        vig.intensity.value = 0.32f;
        vig.smoothness.value = 0.42f;
        var colAdj = profile.Add<ColorAdjustments>(true);
        colAdj.postExposure.value = 0.12f;
        colAdj.saturation.value = 10f;
        colAdj.contrast.value = 6f;
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

    // ---------- URP 修復：接上遺失的 PostProcessData，後處理才真的會跑 ----------
    static void FixRenderPipeline()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP_Renderer.asset");
        if (rendererData != null && rendererData.postProcessData == null)
        {
            rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            Debug.Log("[P2SceneBuilder] URP_Renderer.postProcessData 已接上");
        }
    }

    // ---------- 腰帶輪廓：貼著身體曲線外擴 45mm，上下緣收口 ----------
    static Vector2[] MawashiProfile()
    {
        float R(float y) => SumoMeshFactory.ProfileRadiusAt(BodyProfile, y);
        return new[]
        {
            new Vector2(R(0.55f) + 0.010f, 0.55f),
            new Vector2(R(0.65f) + 0.100f, 0.65f),
            new Vector2(R(0.85f) + 0.110f, 0.85f),
            new Vector2(R(1.05f) + 0.100f, 1.05f),
            new Vector2(R(1.15f) + 0.010f, 1.16f),
        };
    }

    // ---------- 力士組裝：程序化一體成形，視覺全掛在 Visual 子節點（果凍/搖擺不影響物理）----------
    static SumoWrestler MakeRikishi(string name, Vector3 pos, Mesh body, Mesh mawashi, Mesh armL, Mesh armR,
        Material skin, Material mawashiMat, Material hair, Material eye, Material blush,
        PhysicMaterial pm, SumoParams p, string display)
    {
        var root = new GameObject(name);
        root.transform.position = pos;
        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 1.0f, 0f);
        col.radius = 0.62f;
        col.height = 2.0f;
        col.material = pm;
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 120f;
        rb.drag = 0.15f;
        rb.angularDrag = 1.2f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform);
        visual.localPosition = Vector3.zero;

        void MeshPart(string n, Mesh m, Material mat)
        {
            var g = new GameObject(n, typeof(MeshFilter), typeof(MeshRenderer));
            g.transform.SetParent(visual);
            g.transform.localPosition = Vector3.zero;
            g.GetComponent<MeshFilter>().sharedMesh = m;
            g.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
        void Part(string n, PrimitiveType type, Vector3 lp, Vector3 ls, Material m, Vector3? euler = null)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = n;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(visual);
            g.transform.localPosition = lp;
            g.transform.localScale = ls;
            if (euler.HasValue) g.transform.localRotation = Quaternion.Euler(euler.Value);
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        // 一體成形：身體+頭同一條曲面
        MeshPart("Body", body, skin);
        MeshPart("Mawashi", mawashi, mawashiMat);
        MeshPart("ArmL", armL, skin);
        MeshPart("ArmR", armR, skin);

        // 手掌（和手臂管相切，不浮空）
        Part("HandL", PrimitiveType.Sphere, new Vector3(-0.72f, 0.62f, 0.40f), Vector3.one * 0.40f, skin);
        Part("HandR", PrimitiveType.Sphere, new Vector3(0.72f, 0.62f, 0.40f), Vector3.one * 0.40f, skin);

        // 短腿 + 腳掌（深埋進身體底，沒有縫）
        Part("LegL", PrimitiveType.Capsule, new Vector3(-0.26f, 0.26f, 0.02f), new Vector3(0.32f, 0.20f, 0.32f), skin);
        Part("LegR", PrimitiveType.Capsule, new Vector3(0.26f, 0.26f, 0.02f), new Vector3(0.32f, 0.20f, 0.32f), skin);
        Part("FootL", PrimitiveType.Sphere, new Vector3(-0.27f, 0.10f, 0.14f), new Vector3(0.30f, 0.17f, 0.44f), skin);
        Part("FootR", PrimitiveType.Sphere, new Vector3(0.27f, 0.10f, 0.14f), new Vector3(0.30f, 0.17f, 0.44f), skin);

        // 腰帶前擋布 + 後結
        Part("MawashiFront", PrimitiveType.Cube, new Vector3(0f, 0.42f, 0.66f), new Vector3(0.34f, 0.55f, 0.10f), mawashiMat, new Vector3(7f, 0f, 0f));
        Part("MawashiKnot", PrimitiveType.Sphere, new Vector3(0f, 0.88f, -0.76f), new Vector3(0.30f, 0.24f, 0.20f), mawashiMat);

        // 髮：髮帽（露出額頭與眼睛）+ 丁髷（前後向小髻）
        Part("HairCap", PrimitiveType.Sphere, new Vector3(0f, 2.16f, -0.03f), new Vector3(0.84f, 0.38f, 0.88f), hair);
        Part("Chonmage", PrimitiveType.Capsule, new Vector3(0f, 2.36f, 0.05f), new Vector3(0.13f, 0.16f, 0.11f), hair, new Vector3(90f, 0f, 0f));

        // 臉：貼著頭部曲面放
        Part("EyeL", PrimitiveType.Sphere, new Vector3(-0.15f, 1.93f, 0.40f), Vector3.one * 0.10f, eye);
        Part("EyeR", PrimitiveType.Sphere, new Vector3(0.15f, 1.93f, 0.40f), Vector3.one * 0.10f, eye);
        Part("BrowL", PrimitiveType.Sphere, new Vector3(-0.16f, 2.02f, 0.40f), new Vector3(0.14f, 0.04f, 0.04f), hair, new Vector3(0f, 0f, -8f));
        Part("BrowR", PrimitiveType.Sphere, new Vector3(0.16f, 2.02f, 0.40f), new Vector3(0.14f, 0.04f, 0.04f), hair, new Vector3(0f, 0f, 8f));
        Part("Mouth", PrimitiveType.Sphere, new Vector3(0f, 1.79f, 0.445f), new Vector3(0.09f, 0.03f, 0.03f), eye);
        Part("BlushL", PrimitiveType.Sphere, new Vector3(-0.30f, 1.82f, 0.32f), new Vector3(0.13f, 0.075f, 0.05f), blush);
        Part("BlushR", PrimitiveType.Sphere, new Vector3(0.30f, 1.82f, 0.32f), new Vector3(0.13f, 0.075f, 0.05f), blush);

        var w = root.AddComponent<SumoWrestler>();
        w.P = p;
        w.DisplayName = display;
        var vis = root.AddComponent<WrestlerVisuals>();
        vis.Visual = visual;
        w.Visuals = vis;
        return w;
    }

    // ---------- 燈籠串（含吊繩，才不會像懸浮的光點）----------
    static void MakeLanternRing(Material mat, Material ropeMat, float radius, float y, int count, float ceilingY)
    {
        var ring = new GameObject("Lanterns_r" + radius);
        for (int i = 0; i < count; i++)
        {
            float a = i / (float)count * Mathf.PI * 2f;
            var pos = new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius);

            var l = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            l.name = "Lantern_" + i;
            Object.DestroyImmediate(l.GetComponent<SphereCollider>());
            l.transform.SetParent(ring.transform);
            l.transform.localPosition = pos;
            l.transform.localScale = new Vector3(0.34f, 0.42f, 0.34f);
            l.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var rope = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rope.name = "Rope_" + i;
            Object.DestroyImmediate(rope.GetComponent<CapsuleCollider>());
            rope.transform.SetParent(ring.transform);
            float half = (ceilingY - y) * 0.5f;
            rope.transform.localPosition = pos + Vector3.up * (half + 0.2f);
            rope.transform.localScale = new Vector3(0.03f, half, 0.03f);
            rope.GetComponent<MeshRenderer>().sharedMaterial = ropeMat;
        }
    }

    // ---------- 3D 觀眾：兩層階梯座台 + 坐姿人物，少數站著歡呼 ----------
    static void MakeCrowd3D(Material platformMat)
    {
        var root = new GameObject("Crowd3D");
        var rnd = new System.Random(7);
        var benchMat = MakeLit("CrowdBench", new Color(0.22f, 0.16f, 0.12f), smooth: 0.15f);

        string[] sitting = { "Female_Sitting", "Female_Sitting_Cheering", "Male_Sitting",
                             "Female_Sitting_Cheering" };   // 歡呼姿勢加權出現
        string[] standing = { "Woman_Standing_Waving", "Male_LookingUp", "Female_Standing" };

        // (半徑, 座面高, 人數)：兩層階梯
        var tiers = new[]
        {
            (radius: 9.5f,  y: -1.45f, count: 24),
            (radius: 10.9f, y: -0.45f, count: 28),
        };

        foreach (var t in tiers)
        {
            // 座台環：分段箱子圍成環（中心留空 —— 整片圓盤會把土俵 2.5m 的落差蓋掉）
            int segs = 36;
            float segLen = t.radius * 2f * Mathf.PI / segs + 0.25f;
            for (int k = 0; k < segs; k++)
            {
                float sa = k / (float)segs * Mathf.PI * 2f;
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = "Tier_r" + t.radius + "_" + k;
                Object.DestroyImmediate(seg.GetComponent<BoxCollider>());
                seg.transform.SetParent(root.transform);
                seg.transform.localPosition = new Vector3(Mathf.Sin(sa) * t.radius, t.y - 0.1f, Mathf.Cos(sa) * t.radius);
                seg.transform.localRotation = Quaternion.AngleAxis(sa * Mathf.Rad2Deg + 90f, Vector3.up);
                seg.transform.localScale = new Vector3(segLen, 0.2f, 2.0f);
                seg.GetComponent<MeshRenderer>().sharedMaterial = platformMat;
            }

            for (int i = 0; i < t.count; i++)
            {
                float a = (i + (float)rnd.NextDouble() * 0.4f) / t.count * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Sin(a) * t.radius, t.y, Mathf.Cos(a) * t.radius);

                // 面向土俵，帶一點隨機偏轉才不像閱兵
                Vector3 inward = -new Vector3(pos.x, 0f, pos.z);
                var rot = Quaternion.LookRotation(inward)
                        * Quaternion.Euler(0f, -14f + (float)rnd.NextDouble() * 28f, 0f);

                bool stand = rnd.NextDouble() < 0.12;   // 少數激動到站起來
                string model = stand ? standing[rnd.Next(standing.Length)]
                                     : sitting[rnd.Next(sitting.Length)];
                float h = stand ? 1.62f + (float)rnd.NextDouble() * 0.18f
                                : 1.02f + (float)rnd.NextDouble() * 0.14f;

                var person = CrowdAssets.Spawn(model, root.transform, pos, rot, h);
                if (person == null) continue;

                if (!stand)
                {
                    var bench = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bench.name = "Bench";
                    Object.DestroyImmediate(bench.GetComponent<BoxCollider>());
                    bench.transform.SetParent(root.transform);
                    bench.transform.position = pos + new Vector3(0f, 0.14f, 0f);
                    bench.transform.rotation = rot;
                    bench.transform.localScale = new Vector3(0.85f, 0.28f, 0.55f);
                    bench.GetComponent<MeshRenderer>().sharedMaterial = benchMat;
                }
            }
        }
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
    static Material MakeLit(string name, Color c, Texture2D tex = null, float emissive = 0f, float smooth = 0.25f)
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
        m.SetFloat("_Smoothness", smooth);
        if (emissive > 0f && tex != null)
        {
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", tex);
            m.SetColor("_EmissionColor", Color.white * emissive);
        }
        return m;
    }

    /// 自發光材質（燈籠、金邊）：無貼圖也能發光吃 Bloom
    static Material MakeGlow(string name, Color baseColor, Color glowColor, float intensity)
    {
        var m = MakeLit(name, baseColor, smooth: 0.4f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", glowColor * intensity);
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

    static PhysicMaterial MakePhysMat(string name, float staticF, float dynamicF, float bounce)
    {
        string dir = "Assets/_Project/Config";
        Directory.CreateDirectory(dir);
        string path = dir + "/" + name + ".physicMaterial";
        var pm = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(path);
        if (pm == null)
        {
            pm = new PhysicMaterial(name);
            AssetDatabase.CreateAsset(pm, path);
        }
        pm.staticFriction = staticF;
        pm.dynamicFriction = dynamicF;
        pm.bounciness = bounce;
        pm.frictionCombine = PhysicMaterialCombine.Average;
        pm.bounceCombine = PhysicMaterialCombine.Maximum;
        return pm;
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

        cam.transform.position = new Vector3(0f, 4.8f, -7.4f);
        cam.transform.LookAt(new Vector3(0f, 1.0f, 0f));
        Shoot(cam, Path.Combine(outDir, "arena_overview.png"));
        cam.transform.position = new Vector3(4.2f, 1.8f, -5.0f);
        cam.transform.LookAt(new Vector3(0f, 1.0f, 0f));
        Shoot(cam, Path.Combine(outDir, "arena_ringside.png"));
        cam.transform.position = new Vector3(0f, 1.5f, -4.2f);
        cam.transform.LookAt(new Vector3(0f, 1.35f, 0f));
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
