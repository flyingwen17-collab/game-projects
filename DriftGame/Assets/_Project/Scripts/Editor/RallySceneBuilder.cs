using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// 一鍵建置正式拉力賽道場景：Catmull-Rom 賽道網格、護牆、起點線、檢查點、
/// 草地/山丘/樹木環境、後製特效、三台車、HUD 與 BGM。
public static class RallySceneBuilder
{
    const string ScenesDir = "Assets/_Project/Scenes";
    const string TexturesDir = "Assets/_Project/Textures";
    const string MaterialsDir = "Assets/_Project/Materials";
    const string SettingsDir = "Assets/Settings";
    const float RoadWidth = 11f;

    [MenuItem("Tools/Drift Game/Build Rally Scene")]
    public static void Build()
    {
        Directory.CreateDirectory(ScenesDir);
        Directory.CreateDirectory(TexturesDir);
        Directory.CreateDirectory(MaterialsDir);
        AssetDatabase.Refresh();

        if (AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/engine_loop.wav") == null)
            AudioSynth.GenerateAll();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildLighting();
        BuildEnvironment();
        var samples = BuildTrack();
        var cars = BuildCars(samples);
        BuildManagers(cars, samples);
        BuildPostProcessing();

        string path = ScenesDir + "/RallyTrack.unity";
        EditorSceneManager.SaveScene(scene, path);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(path, true),
            new EditorBuildSettingsScene(ScenesDir + "/PracticeGround.unity", true),
        };
        AssetDatabase.SaveAssets();
        Debug.Log("[RallySceneBuilder] DONE");
    }

    // ---------------- 光照與天空 ----------------
    static void BuildLighting()
    {
        var lightGo = new GameObject("Sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.35f;
        light.color = new Color(1f, 0.96f, 0.88f); // 午後暖陽
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 120f;
        RenderSettings.fogEndDistance = 650f;
        RenderSettings.fogColor = new Color(0.75f, 0.82f, 0.9f);
        RenderSettings.ambientIntensity = 1.1f;

        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(SettingsDir + "/URP_Pipeline.asset");
        if (urp != null)
        {
            urp.shadowDistance = 160f;
            EditorUtility.SetDirty(urp);
        }
    }

    // ---------------- 環境：草地、山、樹 ----------------
    static void BuildEnvironment()
    {
        var grassMat = TexMat("Grass", GrassTexture(), 140f);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Grass";
        ground.transform.position = new Vector3(75f, 0f, 0f);
        ground.transform.localScale = new Vector3(140f, 1f, 140f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = grassMat;

        var hillMat = ColorMat("Hill", new Color(0.32f, 0.42f, 0.3f), 0.05f);
        var hillRoot = new GameObject("Hills");
        var rnd = new System.Random(9);
        for (int i = 0; i < 10; i++)
        {
            float ang = i * Mathf.PI * 2f / 10f;
            float radius = 430f + (float)rnd.NextDouble() * 120f;
            var hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hill.name = "Hill" + i;
            Object.DestroyImmediate(hill.GetComponent<Collider>());
            hill.transform.SetParent(hillRoot.transform);
            hill.transform.position = new Vector3(75f + Mathf.Cos(ang) * radius, -8f, Mathf.Sin(ang) * radius);
            float w = 200f + (float)rnd.NextDouble() * 160f;
            hill.transform.localScale = new Vector3(w, 55f + (float)rnd.NextDouble() * 45f, w);
            hill.GetComponent<MeshRenderer>().sharedMaterial = hillMat;
        }
    }

    static void ScatterTrees(List<Vector3> trackSamples)
    {
        var trunkMat = ColorMat("Trunk", new Color(0.35f, 0.24f, 0.14f), 0.1f);
        var leafMat = ColorMat("Leaf", new Color(0.13f, 0.38f, 0.15f), 0.1f);
        var root = new GameObject("Trees");
        var rnd = new System.Random(23);
        int placed = 0, attempts = 0;
        while (placed < 45 && attempts < 400)
        {
            attempts++;
            var p = new Vector3(-140f + (float)rnd.NextDouble() * 430f, 0f, -260f + (float)rnd.NextDouble() * 520f);
            bool nearTrack = false;
            foreach (var s in trackSamples)
                if ((s - p).sqrMagnitude < 18f * 18f) { nearTrack = true; break; }
            if (nearTrack) continue;

            var tree = new GameObject("Tree" + placed);
            tree.transform.SetParent(root.transform);
            tree.transform.position = p;
            float scale = 0.8f + (float)rnd.NextDouble() * 0.7f;

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.transform.SetParent(tree.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 1.5f * scale, 0f);
            trunk.transform.localScale = new Vector3(0.35f * scale, 1.5f * scale, 0.35f * scale);
            trunk.GetComponent<MeshRenderer>().sharedMaterial = trunkMat;

            var leaves = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(leaves.GetComponent<Collider>());
            leaves.transform.SetParent(tree.transform, false);
            leaves.transform.localPosition = new Vector3(0f, 3.6f * scale, 0f);
            leaves.transform.localScale = Vector3.one * 3.2f * scale;
            leaves.GetComponent<MeshRenderer>().sharedMaterial = leafMat;
            placed++;
        }
    }

    // ---------------- 賽道 ----------------
    static List<Vector3> BuildTrack()
    {
        // 控制點：長直線＋高速彎＋中央 S 彎＋髮夾組成的閉環（約 900m）
        Vector3[] pts =
        {
            new Vector3(0, 0, -100), new Vector3(0, 0, 100),
            new Vector3(40, 0, 160), new Vector3(100, 0, 170),
            new Vector3(150, 0, 140), new Vector3(150, 0, 80),
            new Vector3(110, 0, 50), new Vector3(110, 0, -10),
            new Vector3(150, 0, -40), new Vector3(150, 0, -100),
            new Vector3(100, 0, -160), new Vector3(40, 0, -160),
        };

        var samples = new List<Vector3>();
        int perSeg = 30;
        for (int i = 0; i < pts.Length; i++)
        {
            Vector3 p0 = pts[(i - 1 + pts.Length) % pts.Length];
            Vector3 p1 = pts[i];
            Vector3 p2 = pts[(i + 1) % pts.Length];
            Vector3 p3 = pts[(i + 2) % pts.Length];
            for (int j = 0; j < perSeg; j++)
                samples.Add(CatmullRom(p0, p1, p2, p3, (float)j / perSeg));
        }

        BuildRoadMesh(samples);
        BuildWalls(samples);
        BuildStartAndCheckpoints(samples);
        ScatterTrees(samples);
        return samples;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
    {
        return 0.5f * ((2f * p1) + (-p0 + p2) * u
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u
            + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
    }

    static Vector3 Tangent(List<Vector3> s, int i)
    {
        int n = s.Count;
        return (s[(i + 1) % n] - s[(i - 1 + n) % n]).normalized;
    }

    static void BuildRoadMesh(List<Vector3> s)
    {
        int n = s.Count;
        var verts = new Vector3[(n + 1) * 2];
        var uvs = new Vector2[(n + 1) * 2];
        var tris = new int[n * 6];
        float v = 0f;

        for (int i = 0; i <= n; i++)
        {
            int idx = i % n;
            Vector3 tan = Tangent(s, idx);
            Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
            Vector3 center = s[idx] + Vector3.up * 0.03f;
            verts[i * 2] = center - right * RoadWidth * 0.5f;
            verts[i * 2 + 1] = center + right * RoadWidth * 0.5f;
            if (i > 0) v += Vector3.Distance(s[idx], s[(idx - 1 + n) % n]) / 9f;
            uvs[i * 2] = new Vector2(0f, v);
            uvs[i * 2 + 1] = new Vector2(1f, v);
        }

        for (int i = 0; i < n; i++)
        {
            int t = i * 6;
            int a = i * 2;
            tris[t] = a; tris[t + 1] = a + 2; tris[t + 2] = a + 1;
            tris[t + 3] = a + 1; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
        }

        var mesh = new Mesh { name = "RoadMesh" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, ScenesDir + "/RoadMesh.asset");

        var road = new GameObject("Road");
        road.AddComponent<MeshFilter>().sharedMesh = mesh;
        road.AddComponent<MeshRenderer>().sharedMaterial = TexMat("Asphalt", AsphaltTexture(), 0f);
        road.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    static void BuildWalls(List<Vector3> s)
    {
        var wallMat = ColorMat("Barrier", new Color(0.75f, 0.75f, 0.78f), 0.15f);
        var root = new GameObject("Walls");
        int n = s.Count;
        int step = 2;
        for (int i = 0; i < n; i += step)
        {
            Vector3 a = s[i];
            Vector3 b = s[(i + step) % n];
            Vector3 dir = (b - a).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            float len = Vector3.Distance(a, b);
            foreach (float side in new[] { -1f, 1f })
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Wall";
                wall.transform.SetParent(root.transform);
                wall.transform.position = (a + b) * 0.5f + right * side * (RoadWidth * 0.5f + 1.2f) + Vector3.up * 0.55f;
                wall.transform.rotation = Quaternion.LookRotation(dir);
                wall.transform.localScale = new Vector3(0.35f, 1.1f, len + 0.5f);
                wall.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
                wall.isStatic = true;
            }
        }
    }

    static int startIndex = -1;

    static void BuildStartAndCheckpoints(List<Vector3> s)
    {
        int n = s.Count;
        // 起點設在主直線中段（最接近 (0,0,-20) 的取樣點）
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            float d = (s[i] - new Vector3(0f, 0f, -20f)).sqrMagnitude;
            if (d < best) { best = d; startIndex = i; }
        }

        var checkerMat = TexMat("Checker", CheckerTexture(), 1f);
        var cpRoot = new GameObject("Checkpoints");

        for (int c = 0; c < 4; c++)
        {
            int idx = (startIndex + c * n / 4) % n;
            Vector3 tan = Tangent(s, idx);

            var cp = new GameObject("Checkpoint" + c);
            cp.transform.SetParent(cpRoot.transform);
            cp.transform.SetPositionAndRotation(s[idx] + Vector3.up * 2.5f, Quaternion.LookRotation(tan));
            var box = cp.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(RoadWidth + 8f, 6f, 2f);
            cp.AddComponent<Checkpoint>().index = c;

            if (c == 0)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                line.name = "StartLine";
                Object.DestroyImmediate(line.GetComponent<Collider>());
                line.transform.SetPositionAndRotation(s[idx] + Vector3.up * 0.055f, Quaternion.LookRotation(tan));
                line.transform.localScale = new Vector3(RoadWidth, 0.02f, 1.4f);
                line.GetComponent<MeshRenderer>().sharedMaterial = checkerMat;
            }
        }
    }

    // ---------------- 車輛與管理 ----------------
    static CarController[] BuildCars(List<Vector3> s)
    {
        int n = s.Count;
        // 起跑格：起點線後方，沿賽道方向排三格
        var cars = new CarController[3];
        var styles = new[] { CarFactory.Style.WRX, CarFactory.Style.FIT, CarFactory.Style.AE86 };
        float[] lateral = { -3f, 0f, 3f };

        for (int i = 0; i < 3; i++)
        {
            int idx = (startIndex - 8 - i * 5 + n) % n;
            Vector3 tan = Tangent(s, idx);
            Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
            Vector3 pos = s[idx] + right * lateral[i] + Vector3.up * 0.6f;
            var car = CarFactory.Build(styles[i], pos, Quaternion.LookRotation(tan));
            cars[i] = car.GetComponent<CarController>();
        }
        return cars;
    }

    static void BuildManagers(CarController[] cars, List<Vector3> s)
    {
        // 攝影機
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.farClipPlane = 900f;
        camGo.AddComponent<AudioListener>();
        var follow = camGo.AddComponent<CameraFollow>();
        Vector3 start = s[startIndex];
        camGo.transform.position = start + new Vector3(-20f, 18f, -30f);
        camGo.transform.LookAt(start);

        // 計時
        var timerGo = new GameObject("RaceTimer");
        timerGo.AddComponent<RaceTimer>().checkpointCount = 4;

        // BGM
        var music = new GameObject("Music");
        var src = music.AddComponent<AudioSource>();
        src.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/bgm_loop.wav");
        src.loop = true;
        src.playOnAwake = true;
        src.volume = 0.3f;
        src.spatialBlend = 0f;

        // 遊戲管理
        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        gm.cars = cars;
        gm.cameraFollow = follow;
    }

    // ---------------- 後製 ----------------
    static void BuildPostProcessing()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, SettingsDir + "/RallyPostFX.asset");

        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.55f);
        bloom.threshold.Override(1.0f);

        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.24f);

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);

        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.15f);
        color.saturation.Override(12f);
        color.contrast.Override(8f);

        var volumeGo = new GameObject("Global Volume");
        var volume = volumeGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.profile = profile;

        AssetDatabase.SaveAssets();
    }

    // ---------------- 程式產生貼圖與材質 ----------------
    static Material TexMat(string name, Texture2D tex, float tiling)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetTexture("_BaseMap", tex);
        mat.SetFloat("_Smoothness", 0.05f);
        if (tiling > 1f) mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material ColorMat(string name, Color c, float smooth)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", c);
        mat.SetFloat("_Smoothness", smooth);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Texture2D SaveTexture(string name, Texture2D tex)
    {
        string path = TexturesDir + "/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D AsphaltTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesDir + "/Asphalt.png");
        if (existing != null) return existing;
        int size = 256;
        var tex = new Texture2D(size, size);
        var rnd = new System.Random(5);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float g = 0.16f + (float)rnd.NextDouble() * 0.05f;
                float u = (float)x / size;
                // 路緣白線
                if (u < 0.035f || u > 0.965f) g = 0.85f + (float)rnd.NextDouble() * 0.1f;
                tex.SetPixel(x, y, new Color(g, g, g * 1.03f));
            }
        tex.Apply();
        return SaveTexture("Asphalt", tex);
    }

    static Texture2D GrassTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesDir + "/GrassTex.png");
        if (existing != null) return existing;
        int size = 256;
        var tex = new Texture2D(size, size);
        var rnd = new System.Random(6);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float v = (float)rnd.NextDouble();
                var c = new Color(0.2f + v * 0.08f, 0.42f + v * 0.12f, 0.16f + v * 0.06f);
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return SaveTexture("GrassTex", tex);
    }

    static Texture2D CheckerTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesDir + "/CheckerTex.png");
        if (existing != null) return existing;
        int size = 64;
        var tex = new Texture2D(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool white = ((x / 8) + (y / 8)) % 2 == 0;
                tex.SetPixel(x, y, white ? Color.white : Color.black);
            }
        tex.Apply();
        return SaveTexture("CheckerTex", tex);
    }
}
