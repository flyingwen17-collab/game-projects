using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// 一鍵建置：URP 設定、材質、練習場景（地面＋錐筒）、可甩尾的車輛、攝影機。
/// 選單 Tools > Drift Game > Setup Project，或 batchmode -executeMethod ProjectSetup.SetupProject
public static class ProjectSetup
{
    const string SettingsDir = "Assets/Settings";
    const string MaterialsDir = "Assets/_Project/Materials";
    const string ScenesDir = "Assets/_Project/Scenes";

    [MenuItem("Tools/Drift Game/Setup Project")]
    public static void SetupProject()
    {
        Directory.CreateDirectory(SettingsDir);
        Directory.CreateDirectory(MaterialsDir);
        Directory.CreateDirectory(ScenesDir);
        AssetDatabase.Refresh();

        SetupInputHandler();
        var pipeline = SetupUrp();
        BuildPracticeScene();

        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectSetup] DONE");
    }

    static void SetupInputHandler()
    {
        // activeInputHandler: 0=舊 Input Manager, 1=新 Input System, 2=兩者並用
        var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (settings.Length > 0)
        {
            var so = new SerializedObject(settings[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop != null)
            {
                prop.intValue = 2;
                so.ApplyModifiedProperties();
                Debug.Log("[ProjectSetup] Input handler set to Both");
            }
        }
        PlayerSettings.companyName = "DriftGame";
        PlayerSettings.productName = "Drift Master 3D";
    }

    static RenderPipelineAsset SetupUrp()
    {
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rendererData, SettingsDir + "/URP_Renderer.asset");

        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        AssetDatabase.CreateAsset(pipeline, SettingsDir + "/URP_Pipeline.asset");

        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        Debug.Log("[ProjectSetup] URP assigned");
        return pipeline;
    }

    static Material CreateMat(string name, Color color, float smoothness = 0.4f)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void BuildPracticeScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var matGround = CreateMat("Ground", new Color(0.22f, 0.22f, 0.24f), 0.1f);
        var matBody = CreateMat("CarBody", new Color(0.85f, 0.15f, 0.12f), 0.7f);
        var matDark = CreateMat("CarDark", new Color(0.08f, 0.08f, 0.09f), 0.5f);
        var matWheel = CreateMat("Wheel", new Color(0.05f, 0.05f, 0.05f), 0.3f);
        var matCone = CreateMat("Cone", new Color(1f, 0.45f, 0.05f), 0.3f);

        // 燈光
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 地面（400x400 公尺）
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(40f, 1f, 40f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = matGround;

        // 8 字繞錐：兩個錐筒圓圈
        BuildConeCircle(new Vector3(-18f, 0f, 0f), 12f, 10, matCone);
        BuildConeCircle(new Vector3(18f, 0f, 0f), 12f, 10, matCone);

        // 車輛
        var car = BuildCar(matBody, matDark, matWheel);
        car.transform.position = new Vector3(0f, 0.6f, -30f);

        // 攝影機
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        camGo.AddComponent<AudioListener>();
        var follow = camGo.AddComponent<CameraFollow>();
        follow.target = car.transform;

        string scenePath = ScenesDir + "/PracticeGround.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        Debug.Log("[ProjectSetup] Scene saved: " + scenePath);
    }

    static void BuildConeCircle(Vector3 center, float radius, int count, Material mat)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2f / count;
            var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cone.name = "Cone";
            cone.transform.position = center + new Vector3(Mathf.Cos(angle) * radius, 0.35f, Mathf.Sin(angle) * radius);
            cone.transform.localScale = new Vector3(0.45f, 0.35f, 0.45f);
            cone.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var rb = cone.AddComponent<Rigidbody>();
            rb.mass = 2f; // 撞得動、會噴飛
        }
    }

    static GameObject BuildCar(Material matBody, Material matDark, Material matWheel)
    {
        var car = new GameObject("Car");
        var rb = car.AddComponent<Rigidbody>();
        rb.mass = 1200f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.6f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // 車身
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(car.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        body.transform.localScale = new Vector3(1.8f, 0.55f, 4.2f);
        body.GetComponent<MeshRenderer>().sharedMaterial = matBody;

        // 車頂
        var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cabin.name = "Cabin";
        cabin.transform.SetParent(car.transform, false);
        cabin.transform.localPosition = new Vector3(0f, 0.85f, -0.35f);
        cabin.transform.localScale = new Vector3(1.5f, 0.5f, 2.0f);
        cabin.GetComponent<MeshRenderer>().sharedMaterial = matDark;

        // 四顆 WheelCollider 與輪胎模型
        Vector3[] wheelPos =
        {
            new Vector3(-0.85f, 0f,  1.35f), // FL
            new Vector3( 0.85f, 0f,  1.35f), // FR
            new Vector3(-0.85f, 0f, -1.35f), // RL
            new Vector3( 0.85f, 0f, -1.35f), // RR
        };
        string[] wheelNames = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };

        var colliders = new WheelCollider[4];
        var meshes = new Transform[4];
        var colliderRoot = new GameObject("WheelColliders");
        colliderRoot.transform.SetParent(car.transform, false);
        var meshRoot = new GameObject("WheelMeshes");
        meshRoot.transform.SetParent(car.transform, false);

        for (int i = 0; i < 4; i++)
        {
            var wcGo = new GameObject(wheelNames[i]);
            wcGo.transform.SetParent(colliderRoot.transform, false);
            wcGo.transform.localPosition = wheelPos[i];
            var wc = wcGo.AddComponent<WheelCollider>();
            wc.radius = 0.35f;
            wc.suspensionDistance = 0.25f;
            wc.mass = 25f;
            var spring = wc.suspensionSpring;
            spring.spring = 38000f;
            spring.damper = 4500f;
            spring.targetPosition = 0.5f;
            wc.suspensionSpring = spring;
            colliders[i] = wc;

            // 外層 pivot 讓 CarVisuals 直接套用 WheelCollider 的位置/旋轉，
            // 內層圓柱保留 90 度旋轉當輪胎外型
            var pivot = new GameObject(wheelNames[i] + "_Pivot");
            pivot.transform.SetParent(meshRoot.transform, false);
            pivot.transform.localPosition = wheelPos[i];

            var wheelMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheelMesh.name = wheelNames[i] + "_Mesh";
            Object.DestroyImmediate(wheelMesh.GetComponent<Collider>());
            wheelMesh.transform.SetParent(pivot.transform, false);
            wheelMesh.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheelMesh.transform.localScale = new Vector3(0.7f, 0.125f, 0.7f);
            wheelMesh.GetComponent<MeshRenderer>().sharedMaterial = matWheel;
            meshes[i] = pivot.transform;
        }

        var drift = car.AddComponent<DriftDetector>();
        var controller = car.AddComponent<CarController>();
        controller.wheelFL = colliders[0];
        controller.wheelFR = colliders[1];
        controller.wheelRL = colliders[2];
        controller.wheelRR = colliders[3];

        var visuals = car.AddComponent<CarVisuals>();
        visuals.wheels = new CarVisuals.WheelPair[4];
        for (int i = 0; i < 4; i++)
            visuals.wheels[i] = new CarVisuals.WheelPair { collider = colliders[i], mesh = meshes[i] };

        return car;
    }
}
