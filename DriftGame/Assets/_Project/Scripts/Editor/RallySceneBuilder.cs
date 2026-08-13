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

        if (AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/engine_low.wav") == null)
            AudioSynth.GenerateAll();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildLighting();
        BuildEnvironment();
        var samples = BuildTrack();
        var cars = BuildCars(samples);
        var npcs = BuildNPCs(samples, racerCount: 4, trafficCount: 3);
        BuildManagers(cars, samples, npcs);
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
        // Plane 預設 10 單位、縮放 140 → 1400m 見方；貼圖每 4m 重複一次
        var grassMat = PbrLib.Available("Grass001")
            ? PbrLib.Mat("GrassPBR", "Grass001", 0.05f, new Vector2(350f, 350f))
            : TexMat("Grass", GrassTexture(), 350f, 0.04f);
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

    /// Kenney Nature Kit 的樹隨機散布；模型缺席時退回舊的圓柱+球。
    static void ScatterTrees(List<Vector3> trackSamples)
    {
        var root = new GameObject("Trees");
        var rnd = new System.Random(23);
        string[] models =
        {
            "tree_default", "tree_detailed", "tree_oak", "tree_fat",
            "tree_pineDefaultA", "tree_pineDefaultB", "tree_pineRoundA", "tree_default_fall",
        };

        int placed = 0, attempts = 0;
        while (placed < 70 && attempts < 600)
        {
            attempts++;
            var p = new Vector3(-140f + (float)rnd.NextDouble() * 430f, 0f, -260f + (float)rnd.NextDouble() * 520f);
            bool nearTrack = false;
            foreach (var s in trackSamples)
                if ((s - p).sqrMagnitude < 16f * 16f) { nearTrack = true; break; }
            if (nearTrack) continue;

            string model = models[rnd.Next(models.Length)];
            float height = model.Contains("pine") ? 8f + (float)rnd.NextDouble() * 5f
                                                  : 5.5f + (float)rnd.NextDouble() * 3.5f;
            var rot = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            var tree = KenneyLib.PlaceToHeight("nature", model, root.transform, p, rot, height);
            if (tree == null) { FallbackTree(root.transform, p, rnd); }
            else KenneyLib.MakeStatic(tree);
            placed++;
        }
    }

    static void FallbackTree(Transform root, Vector3 p, System.Random rnd)
    {
        var trunkMat = ColorMat("Trunk", new Color(0.35f, 0.24f, 0.14f), 0.1f);
        var leafMat = ColorMat("Leaf", new Color(0.13f, 0.38f, 0.15f), 0.1f);
        var tree = new GameObject("Tree");
        tree.transform.SetParent(root);
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

        // 路徑資料給 NPC 循跡用
        var pathGo = new GameObject("TrackPath");
        pathGo.AddComponent<TrackPath>().SetPoints(samples, RoadWidth);

        BuildRoadMesh(samples);
        BuildWalls(samples);
        BuildStartAndCheckpoints(samples);
        BuildStartProps(samples);
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
            // UV 以「公尺」為單位，貼圖每 3.2 公尺重複一次 —— 顆粒大小才符合真實比例
            if (i > 0) v += Vector3.Distance(s[idx], s[(idx - 1 + n) % n]) / 3.2f;
            float uSpan = RoadWidth / 3.2f;
            uvs[i * 2] = new Vector2(0f, v);
            uvs[i * 2 + 1] = new Vector2(uSpan, v);
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
        road.AddComponent<MeshRenderer>().sharedMaterial = PbrLib.Available("Asphalt025C")
            ? PbrLib.Mat("RallyAsphaltPBR", "Asphalt025C", 0.20f, Vector2.one)
            : TexMat("Asphalt", AsphaltTexture(), 1f, 0.22f);
        road.AddComponent<MeshCollider>().sharedMesh = mesh;
        road.isStatic = true;

        BuildKerbs(s);
    }

    /// 路緣石：沿賽道兩側鋪一條紅白帶，是賽道最容易辨識的視覺特徵。
    static void BuildKerbs(List<Vector3> s)
    {
        int n = s.Count;
        var kerbMat = TexMat("Kerb", KerbTexture(), 1f, 0.30f);
        var root = new GameObject("Kerbs");
        root.isStatic = true;

        foreach (float side in new[] { -1f, 1f })
        {
            var verts = new Vector3[(n + 1) * 2];
            var uvs = new Vector2[(n + 1) * 2];
            var tris = new int[n * 6];
            float v = 0f;
            const float kerbWidth = 0.85f;

            for (int i = 0; i <= n; i++)
            {
                int idx = i % n;
                Vector3 tan = Tangent(s, idx);
                Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
                Vector3 inner = s[idx] + right * side * (RoadWidth * 0.5f) + Vector3.up * 0.05f;
                Vector3 outer = inner + right * side * kerbWidth + Vector3.up * 0.055f;
                verts[i * 2] = inner;
                verts[i * 2 + 1] = outer;
                if (i > 0) v += Vector3.Distance(s[idx], s[(idx - 1 + n) % n]) / 1.6f;
                uvs[i * 2] = new Vector2(0f, v);
                uvs[i * 2 + 1] = new Vector2(1f, v);
            }

            for (int i = 0; i < n; i++)
            {
                // 纏繞方向決定面朝上還是朝下。頂點的偏移方向是 right*side，
                // side 變號時外積跟著變號，所以左右兩側的纏繞必須相反，
                // 且右側要和路面 mesh 用同一種（路面是可見的，拿它當基準）。
                int t = i * 6, a = i * 2;
                if (side > 0f)
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

            var mesh = new Mesh { name = "Kerb" + (side < 0 ? "L" : "R") };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, ScenesDir + "/" + mesh.name + ".asset");

            var go = new GameObject(mesh.name);
            go.transform.SetParent(root.transform);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = kerbMat;
            go.isStatic = true;
        }
    }

    static void BuildWalls(List<Vector3> s)
    {
        var wallMat = TexMat("Barrier", GuardrailTexture(), 1f, 0.55f, new Vector2(2.5f, 1f));
        bool useKit = KenneyLib.Load("racing", "rail") != null;
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
                Vector3 mid = (a + b) * 0.5f + right * side * (RoadWidth * 0.5f + 1.2f);

                // 物理牆：永遠是隱形方塊（碰撞行為與舊版完全一致）
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Wall";
                wall.transform.SetParent(root.transform);
                wall.transform.position = mid + Vector3.up * 0.55f;
                wall.transform.rotation = Quaternion.LookRotation(dir);
                wall.transform.localScale = new Vector3(0.35f, 1.1f, len + 0.5f);
                // 低摩擦：擦護欄滑開減速，而不是被釘在牆上
                wall.GetComponent<Collider>().sharedMaterial = CarFactory.PhysMat("WallPhys", 0.04f, 0.18f);
                wall.isStatic = true;
                if (useKit)
                {
                    Object.DestroyImmediate(wall.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(wall.GetComponent<MeshFilter>());
                    // 視覺牆：Kenney 金屬護欄，沿段長非等比拉伸（低模護欄拉一點看不出來）
                    var rail = KenneyLib.PlaceToSize("racing", "rail", root.transform,
                        mid, Quaternion.LookRotation(dir), new Vector3(0.5f, 1.0f, len + 0.3f));
                    if (rail != null) KenneyLib.MakeStatic(rail);
                }
                else
                {
                    wall.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
                }
            }
        }
    }

    /// 起點區的賽事氛圍道具：拱門、看台、維修站、旗幟、廣告牌。
    static void BuildStartProps(List<Vector3> s)
    {
        if (KenneyLib.Load("racing", "grandStandCovered") == null) return;
        int n = s.Count;
        var root = new GameObject("StartProps");
        int idx = startIndex;
        Vector3 pos = s[idx];
        Vector3 tan = Tangent(s, idx);
        Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;

        // 起點拱門：橫跨路面（含護欄外緣）
        var arch = KenneyLib.PlaceToSize("racing", "overheadLights", root.transform,
            pos, Quaternion.LookRotation(tan), new Vector3(RoadWidth + 5f, 7f, 2.5f));
        if (arch != null) KenneyLib.MakeStatic(arch);

        // 兩座有頂看台在起點外側，面向賽道。
        // 賽道會繞回來，固定偏移可能壓到另一段路 —— 放之前先驗淨空。
        for (int k = 0; k < 2; k++)
        {
            int gi = (idx - 10 + k * 14 + n) % n;
            Vector3 gpos = s[gi] + Vector3.Cross(Vector3.up, Tangent(s, gi)).normalized * 22f;
            if (!FarFromTrack(gpos, s, 15f)) continue;
            var stand = KenneyLib.PlaceToHeight("racing", "grandStandCovered", root.transform,
                gpos, Quaternion.LookRotation(-Vector3.Cross(Vector3.up, Tangent(s, gi)).normalized), 7.5f);
            if (stand != null) KenneyLib.MakeStatic(stand);
        }

        // 維修站在看台對側
        Vector3 pitPos = pos - right * 26f;
        if (FarFromTrack(pitPos, s, 17f))
        {
            var pit = KenneyLib.PlaceToHeight("racing", "pitsGarage", root.transform,
                pitPos, Quaternion.LookRotation(right), 5.5f);
            if (pit != null) KenneyLib.MakeStatic(pit);
        }

        // 起點兩側的方格旗
        foreach (float side in new[] { -1f, 1f })
        {
            var flag = KenneyLib.PlaceToHeight("racing", "flagCheckers", root.transform,
                pos + right * side * (RoadWidth * 0.5f + 2.2f), Quaternion.LookRotation(tan), 4.5f);
            if (flag != null) KenneyLib.MakeStatic(flag);
        }

        // 廣告牌沿賽道外側每隔一段一塊，交錯左右
        string[] boards = { "billboard", "billboardLow", "bannerTowerRed", "bannerTowerGreen" };
        for (int k = 0; k < 8; k++)
        {
            int bi = (idx + 25 + k * (n / 8)) % n;
            float side = k % 2 == 0 ? 1f : -1f;
            Vector3 br = Vector3.Cross(Vector3.up, Tangent(s, bi)).normalized;
            Vector3 bpos = s[bi] + br * side * 14f;
            if (!FarFromTrack(bpos, s, 10f)) continue;
            var board = KenneyLib.PlaceToHeight("racing", boards[k % boards.Length], root.transform,
                bpos, Quaternion.LookRotation(-br * side), 5f);
            if (board != null) KenneyLib.MakeStatic(board);
        }
    }

    /// 該點與整條賽道所有取樣點都保持最小距離才回傳 true。
    static bool FarFromTrack(Vector3 p, List<Vector3> s, float minDist)
    {
        float sq = minDist * minDist;
        foreach (var q in s)
            if ((q - p).sqrMagnitude < sq) return false;
        return true;
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

    /// 生出 NPC：前段是會甩尾的對手，後段是慢速的一般車流。
    /// 它們沿賽道均勻分布，各自有不同的走線偏移，避免一開始就疊在一起。
    static GameObject[] BuildNPCs(List<Vector3> s, int racerCount, int trafficCount)
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

            // 沿賽道均勻散開，起跑區留給玩家
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
            if (isTraffic)
            {
                ai.style = NPCDriver.Style.Traffic;
            }
            else
            {
                // 一半是純競速、一半是甩尾狂，實力有高有低才有變化
                ai.style = (i % 2 == 0) ? NPCDriver.Style.Racer : NPCDriver.Style.Drifter;
                ai.skill = Mathf.Lerp(0.55f, 0.92f, (float)i / Mathf.Max(1, racerCount - 1));
            }

            var react = go.AddComponent<NPCReaction>();
            react.hornClip = hornClip;

            result.Add(go);
        }
        return result.ToArray();
    }

    static void BuildManagers(CarController[] cars, List<Vector3> s, GameObject[] npcs)
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

        // 比賽總監（起跑燈、計圈、名次）
        var directorGo = new GameObject("RaceDirector");
        var director = directorGo.AddComponent<RaceDirector>();
        director.beepCountClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/beep_count.wav");
        director.beepGoClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/beep_go.wav");

        // 遊戲管理
        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        gm.cars = cars;
        gm.cameraFollow = follow;
        gm.npcs = npcs;
        gm.director = director;
    }

    // ---------------- 後製 ----------------
    /// 後處理、天空、光照、抗鋸齒全部交給 SceneLook。
    /// （舊版在這裡自建 VolumeProfile 但漏了 AddObjectToAsset，
    ///   元件沒寫進 .asset 檔，後處理實際上完全沒生效。）
    static void BuildPostProcessing()
    {
        SceneLook.ApplyAll();
    }

    // ---------------- 程式產生貼圖與材質 ----------------
    /// 每次重建都覆寫材質，確保換過的貼圖會生效（舊版遇到既有檔就直接沿用，導致貼圖換不掉）。
    static Material TexMat(string name, Texture2D tex, float tiling, float smoothness = 0.06f, Vector2 tilingUV = default)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0f);
        Vector2 scale = tilingUV != default ? tilingUV : new Vector2(Mathf.Max(1f, tiling), Mathf.Max(1f, tiling));
        mat.SetTextureScale("_BaseMap", scale);
        EditorUtility.SetDirty(mat);
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

    /// 載入 Codex 生成的實拍質感貼圖；找不到才退回舊的程式生成版。
    static Texture2D LoadTex(string name, string legacy)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesDir + "/" + name + ".png");
        if (t != null) return t;
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesDir + "/" + legacy + ".png");
    }

    static Texture2D AsphaltTexture() { return LoadTex("TRK_Asphalt", "Asphalt"); }
    static Texture2D GrassTexture() { return LoadTex("TRK_Grass", "GrassTex"); }
    static Texture2D KerbTexture() { return LoadTex("TRK_Kerb", "CheckerTex"); }
    static Texture2D GuardrailTexture() { return LoadTex("TRK_Guardrail", "Asphalt"); }

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
