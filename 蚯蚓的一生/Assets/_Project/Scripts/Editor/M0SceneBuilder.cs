using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.AI.Navigation;

/// M0 灰盒場景生成器：選單 Tools → 蚯蚓的一生 → 建立 M0 場景
public static class M0SceneBuilder
{
    const string ScenesDir = "Assets/_Project/Scenes";
    const string MaterialsDir = "Assets/_Project/Materials";
    const string ScenePath = ScenesDir + "/M0_Prototype.unity";

    [MenuItem("Tools/蚯蚓的一生/建立 M0 場景")]
    public static void BuildScene()
    {
        EnsureFolders();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------- 燈光 ----------
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.color = new Color(1f, 0.96f, 0.87f);
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

        // ---------- 環境 ----------
        var env = new GameObject("Environment");

        // 軟土地面 40x40
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground_SoftSoil";
        ground.tag = "SoftSoil";
        ground.transform.SetParent(env.transform);
        ground.transform.localScale = new Vector3(4f, 1f, 4f);
        ground.GetComponent<Renderer>().sharedMaterial = MakeMat("M_SoftSoil", new Color(0.45f, 0.3f, 0.17f));

        // 硬地石板路（不能鑽，貫穿地圖）
        var path = GameObject.CreatePrimitive(PrimitiveType.Cube);
        path.name = "HardPath";
        path.tag = "HardGround";
        path.transform.SetParent(env.transform);
        path.transform.localPosition = new Vector3(0f, 0.02f, 5f);
        path.transform.localScale = new Vector3(40f, 0.04f, 4f);
        path.GetComponent<Renderer>().sharedMaterial = MakeMat("M_HardPath", new Color(0.55f, 0.55f, 0.55f));

        // 石板路的地下阻擋（讓蚯蚓不能從地下穿越）
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name = "HardPath_UndergroundBlocker";
        blocker.transform.SetParent(env.transform);
        blocker.transform.localPosition = new Vector3(0f, -1.4f, 5f);
        blocker.transform.localScale = new Vector3(40f, 2.4f, 4f);
        Object.DestroyImmediate(blocker.GetComponent<Renderer>());
        Object.DestroyImmediate(blocker.GetComponent<MeshFilter>());

        // 四面圍籬（地上+地下都擋住）
        var fenceMat = MakeMat("M_Fence", new Color(0.4f, 0.28f, 0.18f));
        MakeWall(env, "Fence_N", new Vector3(0f, -0.5f, 20.5f), new Vector3(42f, 4f, 1f), fenceMat);
        MakeWall(env, "Fence_S", new Vector3(0f, -0.5f, -20.5f), new Vector3(42f, 4f, 1f), fenceMat);
        MakeWall(env, "Fence_E", new Vector3(20.5f, -0.5f, 0f), new Vector3(1f, 4f, 42f), fenceMat);
        MakeWall(env, "Fence_W", new Vector3(-20.5f, -0.5f, 0f), new Vector3(1f, 4f, 42f), fenceMat);

        // 幾個掩體木箱
        var boxMat = MakeMat("M_Box", new Color(0.6f, 0.45f, 0.25f));
        MakeBox(env, "Box_A", new Vector3(-6f, 0.5f, -3f), new Vector3(1.5f, 1f, 1.5f), boxMat);
        MakeBox(env, "Box_B", new Vector3(7f, 0.5f, -8f), new Vector3(2f, 1f, 1.2f), boxMat);
        MakeBox(env, "Box_C", new Vector3(3f, 0.5f, 11f), new Vector3(1.2f, 1f, 2f), boxMat);

        // ---------- 蚯蚓 ----------
        var worm = new GameObject("Worm");
        worm.tag = "Player";
        worm.layer = LayerMask.NameToLayer("Worm");
        worm.transform.position = new Vector3(0f, 0.3f, -8f);

        var rb = worm.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        var col = worm.AddComponent<CapsuleCollider>();
        col.direction = 2; // Z 軸向
        col.radius = 0.25f;
        col.height = 1.2f;

        // 視覺：粉紅膠囊 + 眼睛
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(worm.transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);
        body.GetComponent<Renderer>().sharedMaterial = MakeMat("M_Worm", new Color(1f, 0.55f, 0.65f));
        MakeEye(worm, new Vector3(0.1f, 0.22f, 0.5f));
        MakeEye(worm, new Vector3(-0.1f, 0.22f, 0.5f));

        worm.AddComponent<WormController>();
        worm.AddComponent<WormStamina>();
        worm.AddComponent<BurrowSystem>();

        // ---------- 攝影機 ----------
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        var follow = camGo.AddComponent<CameraFollow>();
        follow.target = worm.transform;
        camGo.transform.position = new Vector3(0f, 5f, -15f);

        // ---------- 遊戲管理 ----------
        var gm = new GameObject("GameManager");
        gm.AddComponent<GameManager>();
        gm.AddComponent<HUDOnGui>();
        gm.AddComponent<FoodSpawner>();
        gm.AddComponent<ChickenSpawner>();

        // ---------- NavMesh 烘焙 ----------
        var surface = env.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.BuildNavMesh();

        EditorSceneManager.SaveScene(scene, ScenePath);

        if (surface.navMeshData != null)
        {
            AssetDatabase.CreateAsset(surface.navMeshData, ScenesDir + "/M0_NavMesh.asset");
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("M0 場景建立完成：" + ScenePath);
    }

    // ---------- 小工具 ----------

    static void MakeWall(GameObject parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent.transform);
        wall.transform.localPosition = pos;
        wall.transform.localScale = scale;
        wall.GetComponent<Renderer>().sharedMaterial = mat;
        wall.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
    }

    static void MakeBox(GameObject parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent.transform);
        box.transform.localPosition = pos;
        box.transform.localScale = scale;
        box.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static void MakeEye(GameObject parent, Vector3 localPos)
    {
        var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = "Eye";
        Object.DestroyImmediate(eye.GetComponent<Collider>());
        eye.transform.SetParent(parent.transform, false);
        eye.transform.localPosition = localPos;
        eye.transform.localScale = Vector3.one * 0.13f;
        eye.GetComponent<Renderer>().sharedMaterial = MakeMat("M_Eye", new Color(0.08f, 0.08f, 0.08f));
    }

    static Material MakeMat(string name, Color color)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/_Project"))
            AssetDatabase.CreateFolder("Assets", "_Project");
        if (!AssetDatabase.IsValidFolder(ScenesDir))
            AssetDatabase.CreateFolder("Assets/_Project", "Scenes");
        if (!AssetDatabase.IsValidFolder(MaterialsDir))
            AssetDatabase.CreateFolder("Assets/_Project", "Materials");
    }
}
