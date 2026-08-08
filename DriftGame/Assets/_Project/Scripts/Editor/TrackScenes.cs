using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 建置兩個新賽道場景：
///   Expressway  —— 日本首都高速公路風格：高架、連續隔音牆、濕路面、高速連續彎
///   CityStreet  —— 台北信義區風格：寬敞市區大道、直角路口、玻璃帷幕大樓、遠處分節高塔
///
/// 兩者都是「明確致敬、原創幾何」——不重製真實地標的精確外型，避免商標問題。
public static class TrackScenes
{
    [MenuItem("Tools/Drift Game/Build All Tracks")]
    public static void BuildAll()
    {
        BuildExpressway();
        BuildCityStreet();
        RegisterScenes();
        Debug.Log("[TrackScenes] 兩個新賽道建置完成");
    }

    // ==================== 日本高速公路 ====================

    [MenuItem("Tools/Drift Game/Build Expressway")]
    public static void BuildExpressway()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EnsureAudio();

        const float roadWidth = 14f;   // 三線道，比拉力賽道寬

        // 首都高的特徵：長直線接高速連續彎，再接一段緊的連接道
        Vector3[] pts =
        {
            new Vector3(0, 0, -180), new Vector3(0, 0, 60),
            new Vector3(20, 0, 150), new Vector3(90, 0, 210),
            new Vector3(180, 0, 200), new Vector3(230, 0, 140),
            new Vector3(230, 0, 40),  new Vector3(185, 0, -10),
            new Vector3(120, 0, -10), new Vector3(80, 0, -60),
            new Vector3(80, 0, -150), new Vector3(40, 0, -200),
        };
        var s = TrackGen.SamplePath(pts, 34);

        var pathGo = new GameObject("TrackPath");
        pathGo.AddComponent<TrackPath>().SetPoints(s, roadWidth);

        // 地面：深色都市地表（高架下方）
        var groundMat = TrackGen.ColorMat("CityGround", new Color(0.10f, 0.10f, 0.12f), 0.05f);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = new Vector3(110f, -6f, 0f);
        ground.transform.localScale = new Vector3(120f, 1f, 120f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
        ground.isStatic = true;

        // 路面：濕柏油
        var roadMat = TrackGen.TexMat("ExpwyAsphalt", TrackGen.Tex("TRK_AsphaltWet"), 0.42f, Vector2.one);
        TrackGen.BuildRibbon(s, "Road", "ExpwyRoadMesh",
            -roadWidth * 0.5f, roadWidth * 0.5f, 0.03f, 0.03f, roadMat, 3.2f, true);

        // 車道分隔線（中央兩條細白線）
        var lineMat = TrackGen.ColorMat("LaneLine", new Color(0.85f, 0.85f, 0.82f), 0.2f);
        TrackGen.BuildRibbon(s, "LaneLineL", "ExpwyLaneL", -2.5f, -2.2f, 0.045f, 0.045f, lineMat, 6f, false);
        TrackGen.BuildRibbon(s, "LaneLineR", "ExpwyLaneR", 2.2f, 2.5f, 0.045f, 0.045f, lineMat, 6f, false);

        // 連續混凝土隔音牆 —— 首都高最有辨識度的元素
        var wallMat = TrackGen.TexMat("ExpwyWall", TrackGen.Tex("TRK_ConcreteWall"), 0.25f, new Vector2(3f, 1f));
        TrackGen.BuildWalls(s, roadWidth * 0.5f + 0.9f, 3.2f, 0.4f, wallMat, 2, "SoundWalls");

        // 護欄（牆內側較矮的金屬欄）
        var railMat = TrackGen.TexMat("ExpwyRail", TrackGen.Tex("TRK_Guardrail"), 0.6f, new Vector2(2.5f, 1f));
        TrackGen.BuildWalls(s, roadWidth * 0.5f + 0.35f, 0.85f, 0.2f, railMat, 2, "Guardrails");

        // 路燈：橘色鈉燈，夜晚是主要光源
        BuildStreetLights(s, roadWidth * 0.5f + 1.4f, 24, new Color(1f, 0.62f, 0.28f), 7f, 30f, 26f, "Expwy");

        var checker = TrackGen.TexMat("Checker", TrackGen.Tex("CheckerTex"), 0.1f, Vector2.one);
        int startIndex = TrackGen.BuildCheckpoints(s, roadWidth, new Vector3(0f, 0f, -60f), checker);

        var cars = TrackGen.BuildPlayerCars(s, startIndex);
        var npcs = TrackGen.BuildNPCs(s, startIndex, racerCount: 4, trafficCount: 4);
        TrackGen.BuildManagers(cars, npcs, s, startIndex);

        SceneLook.ApplyAll();
        // 高速公路預設夜晚 —— 濕路面反射霓虹是這條賽道的賣點
        var tod = Object.FindAnyObjectByType<TimeOfDay>();
        if (tod != null) tod.preset = TimeOfDay.Preset.Night;

        EditorSceneManager.SaveScene(scene, TrackGen.ScenesDir + "/Expressway.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("[TrackScenes] Expressway 完成");
    }

    // ==================== 台北街道 ====================

    [MenuItem("Tools/Drift Game/Build City Street")]
    public static void BuildCityStreet()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EnsureAudio();

        const float roadWidth = 16f;   // 市區大道更寬

        // 信義區的特徵：棋盤式街廓、接近直角的大路口
        Vector3[] pts =
        {
            new Vector3(-120, 0, -120), new Vector3(-120, 0, 40),
            new Vector3(-110, 0, 110),  new Vector3(-40, 0, 130),
            new Vector3(90, 0, 130),    new Vector3(150, 0, 110),
            new Vector3(165, 0, 40),    new Vector3(165, 0, -80),
            new Vector3(140, 0, -140),  new Vector3(40, 0, -155),
            new Vector3(-60, 0, -155),  new Vector3(-110, 0, -145),
        };
        var s = TrackGen.SamplePath(pts, 32);

        var pathGo = new GameObject("TrackPath");
        pathGo.AddComponent<TrackPath>().SetPoints(s, roadWidth);

        var groundMat = TrackGen.ColorMat("CityPavement", new Color(0.30f, 0.30f, 0.31f), 0.08f);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = new Vector3(20f, -0.05f, 0f);
        ground.transform.localScale = new Vector3(90f, 1f, 90f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
        ground.isStatic = true;

        var roadMat = TrackGen.TexMat("CityAsphalt", TrackGen.Tex("TRK_Asphalt"), 0.22f, Vector2.one);
        TrackGen.BuildRibbon(s, "Road", "CityRoadMesh",
            -roadWidth * 0.5f, roadWidth * 0.5f, 0.03f, 0.03f, roadMat, 3.2f, true);

        // 中央雙黃線
        var yellowMat = TrackGen.ColorMat("CenterLine", new Color(0.82f, 0.68f, 0.12f), 0.2f);
        TrackGen.BuildRibbon(s, "CenterL", "CityCenterL", -0.5f, -0.2f, 0.045f, 0.045f, yellowMat, 6f, false);
        TrackGen.BuildRibbon(s, "CenterR", "CityCenterR", 0.2f, 0.5f, 0.045f, 0.045f, yellowMat, 6f, false);

        // 人行道（比路面高一階，撞到會彈起來）
        var walkMat = TrackGen.ColorMat("Sidewalk", new Color(0.55f, 0.54f, 0.52f), 0.1f);
        TrackGen.BuildRibbon(s, "SidewalkL", "CitySidewalkL",
            -roadWidth * 0.5f - 3.5f, -roadWidth * 0.5f, 0.22f, 0.22f, walkMat, 4f, true);
        TrackGen.BuildRibbon(s, "SidewalkR", "CitySidewalkR",
            roadWidth * 0.5f, roadWidth * 0.5f + 3.5f, 0.22f, 0.22f, walkMat, 4f, true);

        // 起點要先算出來，建築物才知道哪一段必須淨空（車就生在那裡）
        var checker = TrackGen.TexMat("Checker", TrackGen.Tex("CheckerTex"), 0.1f, Vector2.one);
        int startIndex = TrackGen.BuildCheckpoints(s, roadWidth, new Vector3(-120f, 0f, -40f), checker);

        BuildCityBuildings(s, roadWidth, startIndex);
        BuildLandmarkTower(new Vector3(250f, 0f, 60f));
        BuildStreetLights(s, roadWidth * 0.5f + 2.2f, 20, new Color(1f, 0.92f, 0.75f), 8f, 28f, 30f, "City");

        var cars = TrackGen.BuildPlayerCars(s, startIndex);
        var npcs = TrackGen.BuildNPCs(s, startIndex, racerCount: 3, trafficCount: 5);
        TrackGen.BuildManagers(cars, npcs, s, startIndex);

        SceneLook.ApplyAll();
        var tod = Object.FindAnyObjectByType<TimeOfDay>();
        if (tod != null) tod.preset = TimeOfDay.Preset.Dusk;

        EditorSceneManager.SaveScene(scene, TrackGen.ScenesDir + "/CityStreet.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("[TrackScenes] CityStreet 完成");
    }

    // ==================== 裝飾 ====================

    /// 路燈：燈桿 + 燈頭 + 真正的點光源。夜晚時這是主要照明。
    static void BuildStreetLights(List<Vector3> s, float lateral, int step,
                                  Color color, float height, float range, float intensity,
                                  string matPrefix)
    {
        // 材質名稱必須帶賽道前綴 —— 否則兩個賽道共用同一個 .mat 資產會互相覆寫，
        // 後建的賽道會把先建的燈色改掉。
        var poleMat = TrackGen.ColorMat(matPrefix + "LampPole", new Color(0.22f, 0.22f, 0.24f), 0.4f);
        // 發光強度控制在 2 倍以內；過亮在未套後處理時會被截斷成怪色
        var glow = new Color(color.r, color.g, color.b, 1f) * 1.8f;
        glow.a = 1f;
        var headMat = TrackGen.ColorMat(matPrefix + "LampHead", new Color(0.95f, 0.93f, 0.88f), 0.5f, glow);
        var root = new GameObject("StreetLights");
        root.isStatic = true;

        bool useKit = KenneyLib.Load("racing", "lightPostModern") != null;
        int n = s.Count;
        for (int i = 0; i < n; i += step)
        {
            float side = (i / step) % 2 == 0 ? 1f : -1f;
            Vector3 right = TrackGen.Right(s, i);
            Vector3 basePos = s[i] + right * side * lateral;
            Vector3 headPos = basePos + Vector3.up * height - right * side * 0.7f;

            if (useKit)
            {
                // Kenney 現代路燈：燈臂朝路面
                var post = KenneyLib.PlaceToHeight("racing", "lightPostModern", root.transform,
                    basePos, Quaternion.LookRotation(-right * side), height);
                if (post != null)
                {
                    KenneyLib.MakeStatic(post);
                    var b = KenneyLib.MeasureBounds(post);
                    headPos = new Vector3(b.center.x, b.max.y - 0.4f, b.center.z) - right * side * 0.4f;
                }
            }
            else
            {
                var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pole.name = "Pole";
                Object.DestroyImmediate(pole.GetComponent<Collider>());
                pole.transform.SetParent(root.transform);
                pole.transform.position = basePos + Vector3.up * (height * 0.5f);
                pole.transform.localScale = new Vector3(0.16f, height * 0.5f, 0.16f);
                pole.GetComponent<MeshRenderer>().sharedMaterial = poleMat;
                pole.isStatic = true;

                var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                head.name = "Head";
                Object.DestroyImmediate(head.GetComponent<Collider>());
                head.transform.SetParent(root.transform);
                head.transform.position = headPos;
                head.transform.localScale = new Vector3(0.8f, 0.16f, 0.4f);
                head.GetComponent<MeshRenderer>().sharedMaterial = headMat;
                head.isStatic = true;
            }

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform);
            lightGo.transform.position = headPos - Vector3.up * 0.2f;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }
    }

    /// 沿街的商業大樓。高度與色調隨機，夜間窗戶會發光。
    static void BuildCityBuildings(List<Vector3> s, float roadWidth, int startIndex)
    {
        var root = new GameObject("Buildings");
        root.isStatic = true;
        var rnd = new System.Random(2026);

        var mats = new[]
        {
            TrackGen.ColorMat("BldgGlassA", new Color(0.16f, 0.20f, 0.26f), 0.82f, new Color(0.18f, 0.22f, 0.30f)),
            TrackGen.ColorMat("BldgGlassB", new Color(0.20f, 0.22f, 0.24f), 0.75f, new Color(0.24f, 0.24f, 0.20f)),
            TrackGen.ColorMat("BldgStone",  new Color(0.34f, 0.32f, 0.30f), 0.25f, new Color(0.10f, 0.09f, 0.07f)),
        };
        // Kenney City Kit：低層街屋 + 摩天樓，有窗框、雨遮、屋頂設備的真建築模型
        bool useKit = KenneyLib.Load("city", "building-a") != null;
        string[] lowRise = { "building-a", "building-b", "building-c", "building-d", "building-e",
                             "building-f", "building-g", "building-h", "building-i", "building-j",
                             "building-k", "building-l", "building-m", "building-n" };
        string[] towers  = { "building-skyscraper-a", "building-skyscraper-b", "building-skyscraper-c",
                             "building-skyscraper-d", "building-skyscraper-e" };

        int n = s.Count;
        int step = 14;   // 間距密一點，街廓才連續

        // 等比縮放的 Kenney 建築：先擺、量佔地、貼齊人行道、驗淨空。
        // 回傳實際佔地半寬（放棄時回 -1），給第二排推偏移用。
        float PlaceKitBuilding(int i, float side, float extraOffset, float h, string model,
                               Vector3 right, Quaternion rot)
        {
            var bk = KenneyLib.PlaceToHeight("city", model, root.transform,
                s[i] + right * side * 30f, rot, h);
            if (bk == null) return -1f;
            var bb = KenneyLib.MeasureBounds(bk);
            float half = Mathf.Max(bb.extents.x, bb.extents.z);
            Vector3 c2 = s[i] + right * side * (roadWidth * 0.5f + 3.5f + 6f + extraOffset + half);
            KenneyLib.CenterXZ(bk, new Vector3(c2.x, 0f, c2.z));
            if (!IsClearOfTrack(c2, rot, half, half, s, roadWidth * 0.5f + 4.5f))
            {
                // 塞不下就留空地，不要退回方塊 —— 模型群裡混一顆方塊更醜
                Object.DestroyImmediate(bk);
                return -1f;
            }
            KenneyLib.MakeStatic(bk);
            return half;
        }

        for (int i = 0; i < n; i += step)
        {
            Vector3 right = TrackGen.Right(s, i);
            Vector3 tan = TrackGen.Tangent(s, i);

            foreach (float side in new[] { -1f, 1f })
            {
                if (rnd.NextDouble() < 0.15) continue;   // 偶爾留空地，避免像牆

                float depth = 22f + (float)rnd.NextDouble() * 26f;   // 沿路方向
                float width = 18f + (float)rnd.NextDouble() * 20f;   // 垂直路面方向
                float h = 22f + (float)rnd.NextDouble() * 75f;

                Quaternion rot = Quaternion.LookRotation(tan);

                if (useKit)
                {
                    // 臨路第一排：街屋與中樓混合
                    string model = h > 45f ? towers[rnd.Next(towers.Length)]
                                           : lowRise[rnd.Next(lowRise.Length)];
                    float half1 = PlaceKitBuilding(i, side, 0f, h, model, right, rot);

                    // 第二排：高樓在後，天際線才有層次
                    if (rnd.NextDouble() < 0.6)
                    {
                        float h2 = 45f + (float)rnd.NextDouble() * 50f;
                        float extra = (half1 > 0f ? half1 * 2f : 24f) + 8f;
                        PlaceKitBuilding(i, side, extra, h2,
                            towers[rnd.Next(towers.Length)], right, rot);
                    }
                    continue;
                }

                // 側向偏移用 width（垂直路面那一邊）的一半，不是 depth
                float offset = roadWidth * 0.5f + 3.5f + 6f + width * 0.5f;
                Vector3 center = s[i] + right * side * offset;

                // 幾何淨空檢查：對「整條賽道」驗證，而不是只看路徑索引距離。
                // 賽道會彎回來，某一段旁邊的建築物可能剛好卡在另一段（甚至起跑格）上。
                if (!IsClearOfTrack(center, rot, width * 0.5f, depth * 0.5f, s,
                                    roadWidth * 0.5f + 4.5f)) continue;

                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "Building";
                b.transform.SetParent(root.transform);
                b.transform.position = center + Vector3.up * (h * 0.5f);
                b.transform.rotation = rot;
                b.transform.localScale = new Vector3(width, h, depth);
                b.GetComponent<MeshRenderer>().sharedMaterial = mats[rnd.Next(mats.Length)];
                b.isStatic = true;
            }
        }
    }

    /// 檢查一個「有方向的長方體」是否與賽道保持安全距離。
    ///
    /// 用外接圓半徑會過度保守 —— 旋轉方塊的對角線遠大於它實際朝向賽道那一側的厚度，
    /// 結果是幾乎所有建築物都被誤判為擋路。這裡把賽道取樣點轉進方塊的區域座標，
    /// 直接比對半寬與半深，判斷才準確。
    static bool IsClearOfTrack(Vector3 center, Quaternion rot, float halfWidth, float halfDepth,
                               List<Vector3> s, float clearance)
    {
        Quaternion inv = Quaternion.Inverse(rot);
        float wLimit = halfWidth + clearance;
        float dLimit = halfDepth + clearance;

        for (int k = 0; k < s.Count; k++)
        {
            Vector3 local = inv * (s[k] - center);
            if (Mathf.Abs(local.x) < wLimit && Mathf.Abs(local.z) < dLimit) return false;
        }
        return true;
    }

    /// 遠景地標：分節收束的超高塔（致敬台北的天際線，不做精確重製）
    static void BuildLandmarkTower(Vector3 basePos)
    {
        var root = new GameObject("LandmarkTower");
        root.isStatic = true;

        var baseMat = TrackGen.ColorMat("TowerBase", new Color(0.26f, 0.28f, 0.28f), 0.5f, new Color(0.08f, 0.09f, 0.08f));
        var segMat = TrackGen.ColorMat("TowerSeg", new Color(0.22f, 0.30f, 0.30f), 0.78f, new Color(0.14f, 0.22f, 0.20f));

        // 基座
        var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pedestal.transform.SetParent(root.transform);
        pedestal.transform.position = basePos + Vector3.up * 14f;
        pedestal.transform.localScale = new Vector3(46f, 28f, 46f);
        pedestal.GetComponent<MeshRenderer>().sharedMaterial = baseMat;
        pedestal.isStatic = true;

        // 八個往上略微外擴的節（分節造型是這座塔的視覺特徵）
        float y = 28f;
        for (int i = 0; i < 8; i++)
        {
            var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = "Seg" + i;
            seg.transform.SetParent(root.transform);
            float segH = 26f;
            float w = 26f + i * 0.9f;
            seg.transform.position = basePos + Vector3.up * (y + segH * 0.5f);
            seg.transform.localScale = new Vector3(w, segH, w);
            seg.GetComponent<MeshRenderer>().sharedMaterial = segMat;
            seg.isStatic = true;
            y += segH;
        }

        // 尖頂
        var spire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(spire.GetComponent<Collider>());
        spire.transform.SetParent(root.transform);
        spire.transform.position = basePos + Vector3.up * (y + 30f);
        spire.transform.localScale = new Vector3(2.4f, 30f, 2.4f);
        spire.GetComponent<MeshRenderer>().sharedMaterial = baseMat;
        spire.isStatic = true;
    }

    // ==================== 共用 ====================

    static void EnsureAudio()
    {
        if (AssetDatabase.LoadAssetAtPath<AudioClip>(AudioSynth.AudioDir + "/engine_loop.wav") == null)
            AudioSynth.GenerateAll();
    }

    /// 三個賽道全部登記進 Build Settings，之後才能用 SceneManager 切換
    public static void RegisterScenes()
    {
        var list = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(TrackGen.ScenesDir + "/RallyTrack.unity", true),
            new EditorBuildSettingsScene(TrackGen.ScenesDir + "/Expressway.unity", true),
            new EditorBuildSettingsScene(TrackGen.ScenesDir + "/CityStreet.unity", true),
            new EditorBuildSettingsScene(TrackGen.ScenesDir + "/PracticeGround.unity", true),
        };
        EditorBuildSettings.scenes = list.ToArray();
    }
}
