using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// 賽道生成的共用核心：路徑取樣、路面網格、路緣石、護欄、檢查點、車輛與管理器。
/// 三個賽道（拉力／高速公路／市區街道）都用這裡的元件組裝，只是參數與裝飾不同。
public static class TrackGen
{
    public const string ScenesDir = "Assets/_Project/Scenes";
    public const string TexturesDir = "Assets/_Project/Textures";
    public const string MaterialsDir = "Assets/_Project/Materials";

    // ---------------- 路徑 ----------------

    /// 由控制點產生封閉的 Catmull-Rom 取樣路徑
    public static List<Vector3> SamplePath(Vector3[] controlPoints, int perSegment = 30)
    {
        var samples = new List<Vector3>();
        int n = controlPoints.Length;
        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = controlPoints[(i - 1 + n) % n];
            Vector3 p1 = controlPoints[i];
            Vector3 p2 = controlPoints[(i + 1) % n];
            Vector3 p3 = controlPoints[(i + 2) % n];
            for (int j = 0; j < perSegment; j++)
                samples.Add(CatmullRom(p0, p1, p2, p3, (float)j / perSegment));
        }
        return samples;
    }

    public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
    {
        return 0.5f * ((2f * p1) + (-p0 + p2) * u
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u
            + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
    }

    public static Vector3 Tangent(List<Vector3> s, int i)
    {
        int n = s.Count;
        return (s[(i + 1) % n] - s[(i - 1 + n) % n]).normalized;
    }

    public static Vector3 Right(List<Vector3> s, int i)
    {
        return Vector3.Cross(Vector3.up, Tangent(s, i)).normalized;
    }

    // ---------------- 路面 ----------------

    /// 沿路徑產生一條帶狀網格。lateralInner/Outer 是相對中心線的左右偏移。
    /// uvScale 決定貼圖每幾公尺重複一次（用公尺為單位，顆粒比例才對）。
    public static GameObject BuildRibbon(List<Vector3> s, string name, string meshAssetName,
                                         float lateralInner, float lateralOuter,
                                         float heightInner, float heightOuter,
                                         Material mat, float uvMeters, bool addCollider)
    {
        int n = s.Count;
        var verts = new Vector3[(n + 1) * 2];
        var uvs = new Vector2[(n + 1) * 2];
        var tris = new int[n * 6];
        float v = 0f;
        float width = Mathf.Abs(lateralOuter - lateralInner);

        for (int i = 0; i <= n; i++)
        {
            int idx = i % n;
            Vector3 right = Right(s, idx);
            verts[i * 2] = s[idx] + right * lateralInner + Vector3.up * heightInner;
            verts[i * 2 + 1] = s[idx] + right * lateralOuter + Vector3.up * heightOuter;

            if (i > 0) v += Vector3.Distance(s[idx], s[(idx - 1 + n) % n]) / uvMeters;
            uvs[i * 2] = new Vector2(0f, v);
            uvs[i * 2 + 1] = new Vector2(width / uvMeters, v);
        }

        // 纏繞方向：外側在 +right 時用這組（與路面 mesh 一致，面朝上）
        bool flip = lateralOuter < lateralInner;
        for (int i = 0; i < n; i++)
        {
            int t = i * 6, a = i * 2;
            if (!flip)
            {
                tris[t] = a; tris[t + 1] = a + 2; tris[t + 2] = a + 1;
                tris[t + 3] = a + 1; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
            }
            else
            {
                tris[t] = a; tris[t + 1] = a + 1; tris[t + 2] = a + 2;
                tris[t + 3] = a + 1; tris[t + 4] = a + 3; tris[t + 5] = a + 2;
            }
        }

        var mesh = new Mesh { name = meshAssetName };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, ScenesDir + "/" + meshAssetName + ".asset");

        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (addCollider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.isStatic = true;
        return go;
    }

    /// 兩側連續的牆（護欄或隔音牆）。用 Cube 逐段排列，段長越短越貼合彎道。
    public static void BuildWalls(List<Vector3> s, float lateral, float height, float thickness,
                                  Material mat, int step, string name)
    {
        var root = new GameObject(name);
        root.isStatic = true;
        var wallPhys = CarFactory.PhysMat("WallPhys", 0.04f, 0.18f);
        int n = s.Count;
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
                wall.transform.position = (a + b) * 0.5f + right * side * lateral + Vector3.up * (height * 0.5f);
                wall.transform.rotation = Quaternion.LookRotation(dir);
                wall.transform.localScale = new Vector3(thickness, height, len + 0.5f);
                wall.GetComponent<MeshRenderer>().sharedMaterial = mat;
                // 護欄低摩擦：擦到會滑開減速，不會把車釘在牆上
                wall.GetComponent<Collider>().sharedMaterial = wallPhys;
                wall.isStatic = true;
            }
        }
    }

    /// 起點線與檢查點。回傳起點取樣索引。
    public static int BuildCheckpoints(List<Vector3> s, float roadWidth, Vector3 nearPoint,
                                       Material checkerMat, int count = 4)
    {
        int n = s.Count;
        int startIndex = 0;
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            float d = (s[i] - nearPoint).sqrMagnitude;
            if (d < best) { best = d; startIndex = i; }
        }

        var root = new GameObject("Checkpoints");
        for (int c = 0; c < count; c++)
        {
            int idx = (startIndex + c * n / count) % n;
            Vector3 tan = Tangent(s, idx);

            var cp = new GameObject("Checkpoint" + c);
            cp.transform.SetParent(root.transform);
            cp.transform.SetPositionAndRotation(s[idx] + Vector3.up * 2.5f, Quaternion.LookRotation(tan));
            var box = cp.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(roadWidth + 8f, 6f, 2f);
            cp.AddComponent<Checkpoint>().index = c;

            if (c == 0 && checkerMat != null)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                line.name = "StartLine";
                Object.DestroyImmediate(line.GetComponent<Collider>());
                line.transform.SetPositionAndRotation(s[idx] + Vector3.up * 0.06f, Quaternion.LookRotation(tan));
                line.transform.localScale = new Vector3(roadWidth, 0.02f, 1.4f);
                line.GetComponent<MeshRenderer>().sharedMaterial = checkerMat;
            }
        }
        return startIndex;
    }

    // ---------------- 車輛與管理 ----------------

    public static CarController[] BuildPlayerCars(List<Vector3> s, int startIndex)
    {
        int n = s.Count;
        var cars = new CarController[3];
        var styles = new[] { CarFactory.Style.WRX, CarFactory.Style.FIT, CarFactory.Style.AE86 };
        float[] lateral = { -3f, 0f, 3f };

        for (int i = 0; i < 3; i++)
        {
            int idx = (startIndex - 8 - i * 5 + n) % n;
            Vector3 tan = Tangent(s, idx);
            Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
            Vector3 pos = s[idx] + right * lateral[i] + Vector3.up * 0.6f;
            cars[i] = CarFactory.Build(styles[i], pos, Quaternion.LookRotation(tan)).GetComponent<CarController>();
        }
        return cars;
    }

    public static GameObject[] BuildNPCs(List<Vector3> s, int startIndex, int racerCount, int trafficCount)
    {
        int n = s.Count;
        var styles = new[] { CarFactory.Style.WRX, CarFactory.Style.FIT, CarFactory.Style.AE86 };
        var result = new List<GameObject>();
        var root = new GameObject("NPCs");
        var hornClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/horn.wav");

        int total = racerCount + trafficCount;
        if (total <= 0) return result.ToArray();

        // 一般車流用民用車款，看起來才像「路上的車」而不是對手
        string[] civilian = { "taxi", "van", "suv", "sedan", "police", "delivery" };

        for (int i = 0; i < total; i++)
        {
            bool isTraffic = i >= racerCount;
            int idx = ((startIndex + 40 + i * (n / Mathf.Max(1, total))) % n + n) % n;
            Vector3 tan = Tangent(s, idx);
            Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
            float lane = isTraffic ? (i % 2 == 0 ? 2.6f : -2.6f) : ((i % 3) - 1) * 2.2f;
            Vector3 pos = s[idx] + right * lane + Vector3.up * 0.6f;

            var go = CarFactory.Build(styles[i % styles.Length], pos, Quaternion.LookRotation(tan),
                                      isTraffic ? civilian[i % civilian.Length] : null);
            go.name = (isTraffic ? "NPC_Traffic_" : "NPC_Racer_") + i;
            go.transform.SetParent(root.transform);

            var ai = go.AddComponent<NPCDriver>();
            ai.laneOffset = lane;
            if (isTraffic) ai.style = NPCDriver.Style.Traffic;
            else
            {
                ai.style = (i % 2 == 0) ? NPCDriver.Style.Racer : NPCDriver.Style.Drifter;
                ai.skill = Mathf.Lerp(0.55f, 0.92f, (float)i / Mathf.Max(1, racerCount - 1));
            }

            go.AddComponent<NPCReaction>().hornClip = hornClip;
            result.Add(go);
        }
        return result.ToArray();
    }

    public static void BuildManagers(CarController[] cars, GameObject[] npcs, List<Vector3> s, int startIndex)
    {
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.farClipPlane = 1200f;
        camGo.AddComponent<AudioListener>();
        var follow = camGo.AddComponent<CameraFollow>();
        Vector3 start = s[startIndex];
        camGo.transform.position = start + new Vector3(-20f, 18f, -30f);
        camGo.transform.LookAt(start);

        var timerGo = new GameObject("RaceTimer");
        timerGo.AddComponent<RaceTimer>().checkpointCount = 4;

        var music = new GameObject("Music");
        var src = music.AddComponent<AudioSource>();
        src.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/bgm_loop.wav");
        src.loop = true;
        src.playOnAwake = true;
        src.volume = 0.3f;
        src.spatialBlend = 0f;

        var directorGo = new GameObject("RaceDirector");
        var director = directorGo.AddComponent<RaceDirector>();
        director.beepCountClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/beep_count.wav");
        director.beepGoClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/beep_go.wav");

        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        gm.cars = cars;
        gm.cameraFollow = follow;
        gm.npcs = npcs;
        gm.director = director;
    }

    // ---------------- 材質 ----------------

    public static Texture2D Tex(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesDir + "/" + name + ".png");
    }

    public static Material TexMat(string name, Texture2D tex, float smoothness, Vector2 tiling)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        if (tex != null) mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0f);
        mat.SetTextureScale("_BaseMap", tiling);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    public static Material ColorMat(string name, Color c, float smooth, Color emission = default)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", null);
        mat.SetColor("_BaseColor", c);
        mat.SetFloat("_Smoothness", smooth);
        if (emission != default && emission != Color.black)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
