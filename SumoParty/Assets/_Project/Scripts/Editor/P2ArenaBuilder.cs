using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// P2 場景：照真實土俵規制建（企劃書 §5.2）。
///
///   盛土台     —— 方形黏土台（實物約 6.7m 見方、高 0.54m），圓形競技面在其上
///   俵         —— 半埋的稻草袋圍成直徑 4.55m 的圓
///   蛇の目     —— 俵外一圈深色砂（判定出界足跡用）
///   仕切り線   —— 中央兩條白線（相距 70cm、各長 90cm）
///   吊り屋根   —— 懸吊的神明造屋頂 + 四色房（青=東 赤=南 白=西 黒=北）
///   觀眾席     —— 環形三層看板（AI 觀眾貼圖）+ CrowdWave 隨激烈度跳動
///
/// 力士視覺用 Blender 生成的 rikishi_base.fbx 當皮，物理膠囊照舊
/// （流程 MD §4.5：場景生成器只換視覺，手感零風險）。
///
///   Unity.exe -batchmode -quit -projectPath X -executeMethod P2ArenaBuilder.Build
/// </summary>
public static class P2ArenaBuilder
{
    const string ScenePath = "Assets/_Project/Scenes/Dohyo.unity";
    const string ConfigPath = "Assets/_Project/Config/SumoConfig.asset";
    const string FbxPath = "Assets/_Project/Art/Rikishi/rikishi_base.fbx";

    [MenuItem("Sumo/建立 P2 土俵場景")]
    public static void Build()
    {
        var cfg = LoadOrCreateConfig();
        TuneForSumo(cfg);
        float R = cfg.dohyoRadius;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildLights();
        BuildDohyo(R);
        var crowdWave = BuildCrowd();

        // ---- 力士 ----
        float d = R * 0.45f;
        var east = MakeRikishi("East_P1", new Vector3(0f, 0f, -d), Vector3.forward, cfg,
                               new Color(0.16f, 0.30f, 0.62f));   // 東＝藍廻し
        var west = MakeRikishi("West_P2", new Vector3(0f, 0f, d), Vector3.back, cfg,
                               new Color(0.62f, 0.14f, 0.14f));   // 西＝紅廻し
        east.displayName = "東方";
        west.displayName = "西方";

        // ---- 相機：側視（東左西右，對齊觸控分區） ----
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(6.1f, 2.5f, 0f);   // 拉近：力士是主角，要佔滿畫面
        camGo.transform.LookAt(new Vector3(0f, 1.05f, 0f));
        cam.fieldOfView = 38f;

        // ---- 規則 / 輸入 / HUD / 音訊 ----
        var mgr = new GameObject("SumoMatch");
        var match = mgr.AddComponent<SumoMatch>();
        match.cfg = cfg;
        match.east = east;
        match.west = west;
        match.dohyoCenter = GameObject.Find("Dohyo").transform;

        mgr.AddComponent<SumoDebugHUD>().match = match;
        crowdWave.match = match;

        var audio = mgr.AddComponent<SumoAudioDirector>();
        audio.match = match;
        audio.impactLight = Clip("impact_light"); audio.impactMid = Clip("impact_mid");
        audio.impactHeavy = Clip("impact_heavy"); audio.slap = Clip("slap");
        audio.stomp = Clip("stomp"); audio.shoutA = Clip("shout_a");
        audio.shoutB = Clip("shout_b"); audio.grunt = Clip("grunt");
        audio.crowdLoop = Clip("crowd_loop"); audio.crowdRoar = Clip("crowd_roar");
        audio.taikoBase = Clip("taiko_base"); audio.taikoIntense = Clip("taiko_intense");
        audio.grab = Clip("grab"); audio.creak = Clip("creak");

        // 大噸位撞擊感：震屏 + 塵土 + 決着慢動作
        var dustTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/fx_dust.png");
        string dustPath = "Assets/_Project/Materials/M_Dust.mat";
        var dustMat = AssetDatabase.LoadAssetAtPath<Material>(dustPath);
        if (dustMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            dustMat = new Material(sh);
            AssetDatabase.CreateAsset(dustMat, dustPath);
        }
        if (dustTex != null)
        {
            dustMat.mainTexture = dustTex;
            if (dustMat.HasProperty("_BaseMap")) dustMat.SetTexture("_BaseMap", dustTex);
        }
        if (dustMat.HasProperty("_Surface")) dustMat.SetFloat("_Surface", 1f);   // Transparent
        if (dustMat.HasProperty("_Blend")) dustMat.SetFloat("_Blend", 0f);
        dustMat.renderQueue = 3000;
        EditorUtility.SetDirty(dustMat);

        var juice = mgr.AddComponent<ImpactJuice>();
        juice.match = match;
        juice.cam = cam;
        juice.dustMaterial = dustMat;

        AddDriver(east, cfg, SumoTouchZone.Half.Left, false,
                  KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space, KeyCode.LeftShift);
        AddDriver(west, cfg, SumoTouchZone.Half.Right, true,
                  KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
                  KeyCode.Return, KeyCode.RightShift);

        Directory.CreateDirectory("Assets/_Project/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log($"[P2ArenaBuilder] 土俵場景完成：{ScenePath}");
    }

    // ---------- 物理數值調到相撲量級（P1 自測抓到的偏差在這裡修正） ----------

    static void TuneForSumo(SumoConfig cfg)
    {
        cfg.thrustForce = 2600f;      // 突き（小招）
        cfg.chargeForce = 7400f;      // 突進（大招）——原本推得比突き還近，反直覺；
                                      // 8600 會一擊把防禦中的對手推 3.9m（直接出界），砍掉一檔
        cfg.whiffStumble = 1500f;     // いなし 衝空原本 3.95m，快飛出整個土俵
        cfg.sidestepImpulse = 4.6f;
        cfg.yoriForce = 3400f;        // 寄り 是組手勝負手，要推得動
        EditorUtility.SetDirty(cfg);
        Debug.Log("[P2ArenaBuilder] SumoConfig 已調到相撲量級");
    }

    // ---------- 土俵 ----------

    static void BuildDohyo(float R)
    {
        // 外圍地板（會場暗色）
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        floor.name = "HallFloor";
        floor.transform.localScale = new Vector3(22f, 0.05f, 22f);
        floor.transform.position = new Vector3(0f, -0.60f, 0f);
        floor.GetComponent<Renderer>().sharedMaterial = Mat("M_Hall", new Color(0.10f, 0.09f, 0.09f));
        // Cylinder primitive 的 CapsuleCollider 最小高度=直徑 → 這裡會變成半徑 11m 的
        // 隱形圓頂把整個土俵頂在弧面上（自測抓到靜置漂移 8.86m）。換平的 BoxCollider
        Object.DestroyImmediate(floor.GetComponent<Collider>());
        floor.AddComponent<BoxCollider>();

        // 盛土台：方形黏土台
        var mound = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mound.name = "Mound";
        mound.transform.localScale = new Vector3(6.7f, 0.54f, 6.7f);
        mound.transform.position = new Vector3(0f, -0.29f, 0f);
        mound.GetComponent<Renderer>().sharedMaterial = Mat("M_Clay", new Color(0.52f, 0.42f, 0.32f));

        // 競技面（打亮的砂面）—— 圓柱 primitive 是膠囊碰撞體，要換真圓柱網格
        var dohyo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        dohyo.name = "Dohyo";
        dohyo.transform.localScale = new Vector3((R + 0.55f) * 2f, 0.02f, (R + 0.55f) * 2f);
        dohyo.transform.position = new Vector3(0f, -0.02f, 0f);
        dohyo.GetComponent<Renderer>().sharedMaterial = Mat("M_Sand", new Color(0.82f, 0.72f, 0.55f));
        Object.DestroyImmediate(dohyo.GetComponent<Collider>());
        var mc = dohyo.AddComponent<MeshCollider>();
        mc.sharedMesh = dohyo.GetComponent<MeshFilter>().sharedMesh;

        // 蛇の目：俵外一圈深色砂（薄碟，只蓋在砂面上）
        var janome = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        janome.name = "Janome";
        janome.transform.localScale = new Vector3((R + 0.30f) * 2f, 0.006f, (R + 0.30f) * 2f);
        janome.transform.position = new Vector3(0f, 0.004f, 0f);
        janome.GetComponent<Renderer>().sharedMaterial = Mat("M_Janome", new Color(0.55f, 0.46f, 0.34f));
        Object.DestroyImmediate(janome.GetComponent<Collider>());

        var inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);   // 內圈亮砂蓋回來
        inner.name = "InnerSand";
        inner.transform.localScale = new Vector3(R * 2f, 0.008f, R * 2f);
        inner.transform.position = new Vector3(0f, 0.006f, 0f);
        inner.GetComponent<Renderer>().sharedMaterial = Mat("M_Sand", new Color(0.82f, 0.72f, 0.55f));
        Object.DestroyImmediate(inner.GetComponent<Collider>());

        // 俵：半埋的稻草袋
        var tawara = new GameObject("Tawara").transform;
        var tawaraMat = Mat("M_Tawara", new Color(0.58f, 0.47f, 0.28f));
        const int segs = 36;
        for (int i = 0; i < segs; i++)
        {
            float a = i / (float)segs * Mathf.PI * 2f;
            var b = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            b.name = $"Tawara_{i:00}";
            b.transform.SetParent(tawara);
            b.transform.position = new Vector3(Mathf.Cos(a) * R, 0.035f, Mathf.Sin(a) * R);
            b.transform.rotation = Quaternion.Euler(90f, -a * Mathf.Rad2Deg, 0f);
            b.transform.localScale = new Vector3(0.12f, 0.17f, 0.12f);
            b.GetComponent<Renderer>().sharedMaterial = tawaraMat;
            Object.DestroyImmediate(b.GetComponent<Collider>());
        }

        // 仕切り線：中央兩條白線（相距 70cm、各長 90cm）
        var lineMat = Mat("M_Shikirisen", new Color(0.94f, 0.93f, 0.90f));
        for (int s = -1; s <= 1; s += 2)
        {
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = s < 0 ? "ShikiriEast" : "ShikiriWest";
            line.transform.localScale = new Vector3(0.90f, 0.006f, 0.06f);
            line.transform.position = new Vector3(0f, 0.012f, 0.35f * s);
            line.GetComponent<Renderer>().sharedMaterial = lineMat;
            Object.DestroyImmediate(line.GetComponent<Collider>());
        }

        // 吊り屋根：懸吊屋頂（四角錐）+ 四色房
        var roof = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(roof.GetComponent<Collider>());
        Object.DestroyImmediate(roof.GetComponent<MeshFilter>());
        Object.DestroyImmediate(roof.GetComponent<MeshRenderer>());
        roof.name = "Tsuriyane";
        roof.transform.position = new Vector3(0f, 5.1f, 0f);

        var top = GameObject.CreatePrimitive(PrimitiveType.Cube);        // 屋頂主體（扁方錐感）
        top.name = "RoofTop";
        top.transform.SetParent(roof.transform);
        top.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        top.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        top.transform.localScale = new Vector3(4.6f, 0.5f, 4.6f);
        top.GetComponent<Renderer>().sharedMaterial = Mat("M_Roof", new Color(0.24f, 0.15f, 0.10f));
        Object.DestroyImmediate(top.GetComponent<Collider>());

        var eave = GameObject.CreatePrimitive(PrimitiveType.Cube);       // 簷板
        eave.name = "RoofEave";
        eave.transform.SetParent(roof.transform);
        eave.transform.localPosition = Vector3.zero;
        eave.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        eave.transform.localScale = new Vector3(5.4f, 0.28f, 5.4f);
        eave.GetComponent<Renderer>().sharedMaterial = Mat("M_RoofEave", new Color(0.42f, 0.28f, 0.16f));
        Object.DestroyImmediate(eave.GetComponent<Collider>());

        // 四色房：青=東(+z 東方側? 我們的東在 -z) —— 依我方場景：東=-z 藍、南=+x 紅、西=+z 白、北=-x 黑
        (Vector3 pos, Color c, string n)[] fusa =
        {
            (new Vector3(0f, 0f, -2.55f), new Color(0.15f, 0.35f, 0.70f), "FusaEast_青"),
            (new Vector3(2.55f, 0f, 0f),  new Color(0.75f, 0.15f, 0.12f), "FusaSouth_赤"),
            (new Vector3(0f, 0f, 2.55f),  new Color(0.92f, 0.90f, 0.86f), "FusaWest_白"),
            (new Vector3(-2.55f, 0f, 0f), new Color(0.06f, 0.06f, 0.07f), "FusaNorth_黒"),
        };
        foreach (var f in fusa)
        {
            var tassel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            tassel.name = f.n;
            tassel.transform.SetParent(roof.transform);
            tassel.transform.localPosition = f.pos + new Vector3(0f, -0.75f, 0f);
            tassel.transform.localScale = new Vector3(0.16f, 0.42f, 0.16f);
            tassel.GetComponent<Renderer>().sharedMaterial = Mat("M_" + f.n, f.c);
            Object.DestroyImmediate(tassel.GetComponent<Collider>());
        }

        // 屋頂不投影：實物的土俵照明來自屋根下方，投影下去會整片蓋黑競技面
        foreach (var r in roof.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ---------- 觀眾席 ----------

    static CrowdWave BuildCrowd()
    {
        var root = new GameObject("Crowd");
        var wave = root.AddComponent<CrowdWave>();

        // 觀眾看板用 Unlit：觀眾席自帶亮度（像場館裡的觀眾席燈），不受土俵打光影響，
        // 行動裝置也省一次光照計算
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/crowd_stand.png");
        string mp = "Assets/_Project/Materials/M_Crowd.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            AssetDatabase.CreateAsset(m, mp);
        }
        m.color = new Color(0.72f, 0.70f, 0.68f);   // 壓一點亮度，別跟主角搶
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
        if (tex != null)
        {
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        }
        EditorUtility.SetDirty(m);

        // 三層環形看板，一層比一層高、遠
        (float radius, float height, int count)[] tiers =
        {
            (6.2f, 1.05f, 24),
            (7.6f, 2.05f, 28),
            (9.0f, 3.10f, 32),
        };
        foreach (var t in tiers)
        {
            for (int i = 0; i < t.count; i++)
            {
                float a = (i + 0.5f) / t.count * Mathf.PI * 2f;
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = $"Seat_{t.radius:0}_{i:00}";
                q.transform.SetParent(root.transform);
                Vector3 pos = new Vector3(Mathf.Cos(a) * t.radius, t.height, Mathf.Sin(a) * t.radius);
                q.transform.position = pos;
                q.transform.LookAt(new Vector3(0f, t.height, 0f));       // Quad 面朝 -z → LookAt 中心後正面對場中
                q.transform.Rotate(0f, 180f, 0f);
                q.transform.localScale = new Vector3(1.9f, 1.15f, 1f);
                q.GetComponent<Renderer>().sharedMaterial = m;
                Object.DestroyImmediate(q.GetComponent<Collider>());
                wave.Register(q.transform);
            }
        }

        // 看台結構（深色環）
        var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stand.name = "StandRing";
        stand.transform.SetParent(root.transform);
        stand.transform.localScale = new Vector3(19f, 0.5f, 19f);
        stand.transform.position = new Vector3(0f, 0.0f, 0f);
        stand.GetComponent<Renderer>().sharedMaterial = Mat("M_Stand", new Color(0.13f, 0.11f, 0.10f));
        Object.DestroyImmediate(stand.GetComponent<Collider>());
        stand.transform.position = new Vector3(0f, -0.35f, 0f);

        return wave;
    }

    // ---------- 力士 ----------

    static Rikishi MakeRikishi(string name, Vector3 pos, Vector3 look, SumoConfig cfg, Color mawashiColor)
    {
        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look, Vector3.up));

        // 物理照舊：膠囊碰撞體（流程 MD §4.5——模型純當皮，手感零風險）
        const float height = 1.80f, radius = 0.42f;
        var col = root.AddComponent<CapsuleCollider>();
        col.height = height;
        col.radius = radius;
        col.center = new Vector3(0f, height * 0.5f, 0f);
        root.AddComponent<Rigidbody>().mass = cfg.mass;

        // 視覺：Blender 生成的寫實體型力士
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var skin = Mat("M_Skin", new Color(0.76f, 0.57f, 0.44f));
        // 皮膚不是塑膠：Smoothness 壓低（流程 MD §6.3——不放預設值）
        if (skin.HasProperty("_Smoothness")) skin.SetFloat("_Smoothness", 0.32f);
        if (fbx != null)
        {
            var vis = (GameObject)Object.Instantiate(fbx, root.transform);
            vis.name = "Visual";
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;

            // 永不假設模型比例（流程 MD §4.5）：量 bounds、縮放到 1.80m、底部貼地
            var rends = vis.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            float scale = height / Mathf.Max(0.01f, b.size.y);
            vis.transform.localScale = Vector3.one * scale;
            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            vis.transform.localPosition = new Vector3(0f, -(b.min.y - root.transform.position.y), 0f);

            // 依名稱換 URP 材質（FBX 內嵌材質在 URP 會變粉紅）
            var mawashiMat = Mat("M_Mawashi_" + name, mawashiColor);
            var hairMat = Mat("M_Hair", new Color(0.05f, 0.04f, 0.04f));
            foreach (var r in rends)
            {
                if (r.name.Contains("Mawashi")) r.sharedMaterial = mawashiMat;
                else if (r.name.Contains("Hair") || r.name.Contains("Eye")) r.sharedMaterial = hairMat;
                else r.sharedMaterial = skin;
            }

            // 肉體顫動（挨打時脂肪的擠壓回彈）
            var jiggle = vis.AddComponent<BodyJiggle>();
            jiggle.rikishi = null;   // Rikishi 稍後才掛上，Reset 後由下面補
        }
        else
        {
            Debug.LogWarning("[P2ArenaBuilder] 找不到 rikishi_base.fbx，退回膠囊視覺");
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Visual";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            body.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            body.GetComponent<Renderer>().sharedMaterial = skin;
            Object.DestroyImmediate(body.GetComponent<Collider>());
        }

        var rikishi = root.AddComponent<Rikishi>();
        rikishi.cfg = cfg;

        var arms = root.AddComponent<RikishiArms>();
        arms.rikishi = rikishi;
        arms.skinMaterial = skin;

        var jig = root.GetComponentInChildren<BodyJiggle>();
        if (jig != null) jig.rikishi = rikishi;

        // 接觸摩擦：兩個膠囊互推不能像冰塊一樣滑掉（紙片感來源之一）
        var pmPath = "Assets/_Project/Config/PM_Rikishi.physicsMaterial";
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(pmPath);
        if (pm == null)
        {
            pm = new PhysicsMaterial("PM_Rikishi")
            {
                staticFriction = 0.85f,
                dynamicFriction = 0.65f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
            };
            AssetDatabase.CreateAsset(pm, pmPath);
        }
        col.material = pm;

        return rikishi;
    }

    static void AddDriver(Rikishi r, SumoConfig cfg, SumoTouchZone.Half half, bool ai,
                          KeyCode up, KeyCode down, KeyCode left, KeyCode right, KeyCode tap, KeyCode hold)
    {
        var drv = r.gameObject.AddComponent<RikishiDriver>();
        drv.rikishi = r;
        drv.cfg = cfg;
        drv.screenHalf = half;
        drv.aiControlled = ai;
        drv.keyForward = up; drv.keyBack = down; drv.keyLeft = left; drv.keyRight = right;
        drv.keyTap = tap; drv.keyHold = hold;
        if (ai)
        {
            var brain = r.gameObject.AddComponent<RikishiAI>();
            brain.cfg = cfg;
            drv.ai = brain;
        }
    }

    // ---------- 工具 ----------

    static void BuildLights()
    {
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 0.55f;                      // 壓暗，讓土俵聚光跳出來
        sun.color = new Color(0.9f, 0.88f, 0.85f);
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(55f, 30f, 0f);

        var spot = new GameObject("DohyoSpot").AddComponent<Light>();   // 轉播感的頂光
        spot.type = LightType.Spot;
        spot.intensity = 90f;
        spot.range = 12f;
        spot.spotAngle = 80f;
        spot.color = new Color(1f, 0.96f, 0.88f);
        spot.shadows = LightShadows.None;                               // 影子交給太陽光，省行動裝置成本
        spot.transform.position = new Vector3(0f, 4.4f, 0f);            // 吊在屋根「下方」，不會被屋頂擋住
        spot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // URP 附加光源必須是 per-pixel，聚光燈才會亮（預設可能是 per-vertex/off）
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        if (rp != null)
        {
            var so = new SerializedObject(rp);
            var mode = so.FindProperty("m_AdditionalLightsRenderingMode");
            var limit = so.FindProperty("m_AdditionalLightsPerObjectLimit");
            if (mode != null) mode.intValue = 1;    // PerPixel
            if (limit != null) limit.intValue = Mathf.Max(limit.intValue, 4);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rp);
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.28f, 0.28f, 0.33f);
        RenderSettings.ambientEquatorColor = new Color(0.18f, 0.16f, 0.15f);
        RenderSettings.ambientGroundColor = new Color(0.08f, 0.07f, 0.07f);
    }

    static AudioClip Clip(string n)
        => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/_Project/Audio/{n}.wav");

    static SumoConfig LoadOrCreateConfig()
    {
        var cfg = AssetDatabase.LoadAssetAtPath<SumoConfig>(ConfigPath);
        if (cfg != null) return cfg;
        Directory.CreateDirectory("Assets/_Project/Config");
        cfg = ScriptableObject.CreateInstance<SumoConfig>();
        AssetDatabase.CreateAsset(cfg, ConfigPath);
        return cfg;
    }

    static Material Mat(string name, Color c)
    {
        string dir = "Assets/_Project/Materials";
        Directory.CreateDirectory(dir);
        string path = $"{dir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        EditorUtility.SetDirty(m);
        return m;
    }
}
