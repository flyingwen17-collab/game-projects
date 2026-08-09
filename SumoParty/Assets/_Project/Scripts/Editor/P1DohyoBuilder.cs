using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// P1 灰盒場景（流程 MD §3 P1：零美術、零 UI 美化，只驗證核心機制）。
/// 一鍵重建，所以隨時可以砍掉重來。
///
///   Unity.exe -batchmode -quit -projectPath X -executeMethod P1DohyoBuilder.Build
/// </summary>
public static class P1DohyoBuilder
{
    const string ScenePath = "Assets/_Project/Scenes/P1_Dohyo.unity";
    const string ConfigPath = "Assets/_Project/Config/SumoConfig.asset";

    [MenuItem("Sumo/建立 P1 灰盒場景")]
    public static void Build()
    {
        var cfg = LoadOrCreateConfig();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- 光照（灰盒也要看得見）----
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(48f, 35f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.45f, 0.48f, 0.55f);
        RenderSettings.ambientEquatorColor = new Color(0.32f, 0.30f, 0.28f);
        RenderSettings.ambientGroundColor = new Color(0.18f, 0.16f, 0.14f);

        // ---- 地板與土俵 ----
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = Vector3.one * 4f;
        floor.transform.position = new Vector3(0f, -0.30f, 0f);
        floor.GetComponent<Renderer>().sharedMaterial = Mat("M_Floor", new Color(0.16f, 0.15f, 0.14f));

        var dohyo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        dohyo.name = "Dohyo";
        // 真實土俵直徑 4.55 m。Unity 圓柱預設半徑 0.5、高 2。
        dohyo.transform.localScale = new Vector3(cfg.dohyoRadius * 2f, 0.15f, cfg.dohyoRadius * 2f);
        dohyo.transform.position = new Vector3(0f, -0.15f, 0f);   // 上緣正好在 y = 0
        dohyo.GetComponent<Renderer>().sharedMaterial = Mat("M_Dohyo", new Color(0.78f, 0.68f, 0.50f));

        // ⚠️ Unity 的 Cylinder primitive 給的是 CapsuleCollider（圓頂），不是圓柱！
        // 直接用會讓力士站在弧面上往外滑 —— 自測抓到靜置 3 秒漂移 1.7 m 就是這個。
        // 換成真正的圓柱網格碰撞體（靜態物件可用非凸面，形狀精確）。
        Object.DestroyImmediate(dohyo.GetComponent<Collider>());
        var dohyoCol = dohyo.AddComponent<MeshCollider>();
        dohyoCol.sharedMesh = dohyo.GetComponent<MeshFilter>().sharedMesh;

        // 俵（邊界線）—— 灰盒用一圈小方塊標出來，看得到界線才調得了手感
        var tawara = new GameObject("Tawara").transform;
        var tawaraMat = Mat("M_Tawara", new Color(0.55f, 0.42f, 0.24f));
        const int segments = 40;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = $"Tawara_{i:00}";
            b.transform.SetParent(tawara);
            b.transform.position = new Vector3(Mathf.Cos(a) * cfg.dohyoRadius, 0.03f, Mathf.Sin(a) * cfg.dohyoRadius);
            b.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            b.transform.localScale = new Vector3(0.12f, 0.06f, 0.34f);
            b.GetComponent<Renderer>().sharedMaterial = tawaraMat;
            Object.DestroyImmediate(b.GetComponent<Collider>());   // 純視覺，不擋物理
        }

        // ---- 兩名力士 ----
        float d = cfg.dohyoRadius * 0.45f;
        var east = MakeRikishi("East_P1", new Vector3(0f, 0f, -d), Vector3.forward, cfg, new Color(0.30f, 0.55f, 0.95f));
        var west = MakeRikishi("West_P2", new Vector3(0f, 0f, d), Vector3.back, cfg, new Color(0.95f, 0.35f, 0.30f));
        east.displayName = "東方";
        west.displayName = "西方";

        // ---- 相機：側視，東在畫面左、西在畫面右，跟各自的觸控半邊對齊 ----
        var camGo = new GameObject("Main Camera");
        var cam = camGo.AddComponent<Camera>();
        camGo.tag = "MainCamera";
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(6.8f, 3.4f, 0f);
        camGo.transform.LookAt(new Vector3(0f, 1.0f, 0f));
        cam.fieldOfView = 42f;

        // ---- 規則與輸入 ----
        var mgr = new GameObject("SumoMatch");
        var match = mgr.AddComponent<SumoMatch>();
        match.cfg = cfg;
        match.east = east;
        match.west = west;
        match.dohyoCenter = dohyo.transform;

        var hud = mgr.AddComponent<SumoDebugHUD>();
        hud.match = match;

        AddDriver(east, cfg, SumoTouchZone.Half.Left, false,
                  KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space, KeyCode.LeftShift);
        AddDriver(west, cfg, SumoTouchZone.Half.Right, true,
                  KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
                  KeyCode.Return, KeyCode.RightShift);

        Directory.CreateDirectory("Assets/_Project/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();

        Debug.Log($"[P1DohyoBuilder] 灰盒場景完成：{ScenePath}（土俵半徑 {cfg.dohyoRadius} m）");
    }

    static Rikishi MakeRikishi(string name, Vector3 pos, Vector3 look, SumoConfig cfg, Color color)
    {
        // 根節點放在腳底 —— Rikishi 的出界判定與腳/身體觸地判定都以此為基準
        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look, Vector3.up));

        const float height = 1.80f, radius = 0.42f;

        var col = root.AddComponent<CapsuleCollider>();
        col.height = height;
        col.radius = radius;
        col.center = new Vector3(0f, height * 0.5f, 0f);

        var rb = root.AddComponent<Rigidbody>();
        rb.mass = cfg.mass;

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Visual";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        body.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        body.GetComponent<Renderer>().sharedMaterial = Mat("M_" + name, color);
        Object.DestroyImmediate(body.GetComponent<Collider>());

        // 朝向標記：灰盒看不出正面就沒辦法判斷相剋有沒有生效
        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "Facing";
        nose.transform.SetParent(root.transform, false);
        nose.transform.localPosition = new Vector3(0f, cfg.shoulderHeight, radius + 0.08f);
        nose.transform.localScale = new Vector3(0.34f, 0.14f, 0.16f);
        nose.GetComponent<Renderer>().sharedMaterial = Mat("M_Facing", new Color(0.95f, 0.92f, 0.85f));
        Object.DestroyImmediate(nose.GetComponent<Collider>());

        var r = root.AddComponent<Rikishi>();
        r.cfg = cfg;
        return r;
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

    static SumoConfig LoadOrCreateConfig()
    {
        var cfg = AssetDatabase.LoadAssetAtPath<SumoConfig>(ConfigPath);
        if (cfg != null) return cfg;

        Directory.CreateDirectory("Assets/_Project/Config");
        cfg = ScriptableObject.CreateInstance<SumoConfig>();
        AssetDatabase.CreateAsset(cfg, ConfigPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[P1DohyoBuilder] 建立參數檔 {ConfigPath}");
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
