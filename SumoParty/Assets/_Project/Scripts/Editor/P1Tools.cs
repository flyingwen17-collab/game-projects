using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;

// 一鍵建灰盒場景 + 批次截圖。batch mode 進入點：BuildAndCapture
public static class P1Tools
{
    const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
    const string ParamsPath = "Assets/_Project/Config/SumoParams.asset";

    public static void BuildAndCapture()
    {
        BuildGraybox();
        Capture();
    }

    [MenuItem("Sumo/Build Graybox Scene")]
    public static void BuildGraybox()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 光
        var lightGo = new GameObject("Sun", typeof(Light));
        var light = lightGo.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(50f, -32f, 0f);

        // 參數資產
        var p = AssetDatabase.LoadAssetAtPath<SumoParams>(ParamsPath);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<SumoParams>();
            Directory.CreateDirectory("Assets/_Project/Config");
            AssetDatabase.CreateAsset(p, ParamsPath);
        }

        // 材質
        var matDohyo = MakeMat("Dohyo", new Color(0.76f, 0.64f, 0.45f));
        var matGround = MakeMat("Ground", new Color(0.22f, 0.22f, 0.25f));
        var matP1 = MakeMat("P1Blue", new Color(0.25f, 0.45f, 0.95f));
        var matP2 = MakeMat("P2Red", new Color(0.95f, 0.3f, 0.25f));

        // 土俵（圓柱，半徑 = 0.5 * scaleX）
        float baseRadius = 4f;
        var dohyo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        dohyo.name = "Dohyo";
        Object.DestroyImmediate(dohyo.GetComponent<CapsuleCollider>()); // 圓柱附的是膠囊碰撞體，站不住
        dohyo.AddComponent<MeshCollider>();
        dohyo.transform.localScale = new Vector3(baseRadius * 2f, 0.3f, baseRadius * 2f);
        dohyo.transform.position = new Vector3(0f, -0.3f, 0f); // 頂面 y=0
        dohyo.GetComponent<MeshRenderer>().sharedMaterial = matDohyo;

        // 下方地面（掉下去的視覺參照）
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "GroundFar";
        ground.transform.localScale = Vector3.one * 6f;
        ground.transform.position = new Vector3(0f, -4f, 0f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = matGround;

        // 力士
        var w1 = MakeWrestler("P1_Blue", new Vector3(-1.6f, 1.1f, 0f), matP1, p, "藍方");
        var w2 = MakeWrestler("P2_Red", new Vector3(1.6f, 1.1f, 0f), matP2, p, "紅方");
        w1.Opponent = w2;
        w2.Opponent = w1;

        // 相機
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CameraRig));
        camGo.tag = "MainCamera";
        var rig = camGo.GetComponent<CameraRig>();
        rig.A = w1.transform;
        rig.B = w2.transform;
        camGo.transform.position = rig.BasePosition;
        camGo.transform.LookAt(rig.LookTarget);

        // 流程管理
        var mmGo = new GameObject("MatchManager", typeof(MatchManager));
        var mm = mmGo.GetComponent<MatchManager>();
        mm.P = p;
        mm.P1 = w1;
        mm.P2 = w2;
        mm.Dohyo = dohyo.transform;
        mm.BaseRadius = baseRadius;

        // 輸入：P1 玩家、P2 預設 NPC（真人驅動元件掛著但停用）
        var d1 = mmGo.AddComponent<PlayerDriver>();
        d1.W = w1; d1.MouseTouchEnabled = true;
        var d2 = mmGo.AddComponent<PlayerDriver>();
        d2.W = w2; d2.ArrowScheme = true; d2.enabled = false;
        var npc = mmGo.AddComponent<NpcBrain>();
        npc.W = w2; npc.Match = mm;
        mm.P2Driver = d2;
        mm.P2Npc = npc;

        Directory.CreateDirectory("Assets/_Project/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[P1Tools] Graybox scene built: " + ScenePath);
    }

    static SumoWrestler MakeWrestler(string name, Vector3 pos, Material mat, SumoParams p, string display)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = new Vector3(1.2f, 0.9f, 1.2f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 100f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var w = go.AddComponent<SumoWrestler>();
        w.P = p;
        w.DisplayName = display;
        return w;
    }

    static Material MakeMat(string name, Color c)
    {
        string dir = "Assets/_Project/Materials";
        Directory.CreateDirectory(dir);
        string path = dir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(shader != null ? shader : Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        return m;
    }

    [MenuItem("Sumo/Capture Graybox Screenshots")]
    public static void Capture()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath);

        string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(outDir);

        var cam = Object.FindAnyObjectByType<Camera>();
        Shoot(cam, Path.Combine(outDir, "graybox_overview.png"));

        // 第二角度：低角度看俵際
        cam.transform.position = new Vector3(4.5f, 1.6f, -4.5f);
        cam.transform.LookAt(new Vector3(0f, 0.9f, 0f));
        var rig = cam.GetComponent<CameraRig>();
        if (rig != null) rig.enabled = false; // 別讓 rig 拉回去
        Shoot(cam, Path.Combine(outDir, "graybox_low_angle.png"));

        Debug.Log("[P1Tools] Screenshots saved to " + outDir);
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
