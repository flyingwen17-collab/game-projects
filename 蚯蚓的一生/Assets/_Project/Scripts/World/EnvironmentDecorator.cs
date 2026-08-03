using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// 場景美化：天空、霧、後製、地面貼圖、草、石、樹、木圍籬（全程式生成）
public class EnvironmentDecorator : MonoBehaviour
{
    // 固定色盤：共用材質才能合批（效能關鍵——之前每株草一份材質是卡頓主因）
    static readonly Color[] GrassPalette =
    {
        new Color(0.24f, 0.45f, 0.16f),
        new Color(0.3f, 0.52f, 0.19f),
        new Color(0.36f, 0.58f, 0.22f),
    };
    static readonly Color[] LeafPalette =
    {
        new Color(0.18f, 0.4f, 0.16f),
        new Color(0.24f, 0.48f, 0.18f),
        new Color(0.3f, 0.52f, 0.2f),
    };
    static readonly Color[] RockPalette =
    {
        new Color(0.42f, 0.42f, 0.45f),
        new Color(0.5f, 0.5f, 0.53f),
    };

    void Start()
    {
        Random.InitState(20260802);
        SkyAndLight();
        PostFx();
        RenderTuning();
        GroundTextures();
        ScatterGrass(230);
        ScatterRocks(22);
        PlantTrees(12);
        FenceMakeover();
        BuildFarm();
        SpawnButterflies(6);
        BatchStatics();
    }

    void RenderTuning()
    {
        Application.targetFrameRate = 60;
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            urp.msaaSampleCount = 4;
            urp.shadowDistance = 45f; // 陰影集中在近處，更銳利也更省
        }
    }

    void SpawnButterflies(int count)
    {
        Color[] colors =
        {
            new Color(1f, 0.75f, 0.2f),
            new Color(0.95f, 0.5f, 0.65f),
            new Color(0.55f, 0.7f, 1f),
        };
        for (int i = 0; i < count; i++)
            Butterfly.Spawn(new Vector3(Random.Range(-14f, 14f), 0f, Random.Range(-14f, 14f)),
                colors[i % colors.Length]);
    }

    /// 靜態合批：把不會動的裝飾合成大批次，大幅減少 draw call
    void BatchStatics()
    {
        foreach (Transform child in transform)
            StaticBatchingUtility.Combine(child.gameObject);
    }

    // ---------- 農場建築（圍籬外，不影響 NavMesh） ----------

    void BuildFarm()
    {
        Texture2D woodTex = Resources.Load<Texture2D>("Art/wood_planks_tile");
        Texture2D strawTex = Resources.Load<Texture2D>("Art/straw_tile");
        var wood = TexMat(woodTex, new Color(0.55f, 0.4f, 0.25f));
        var redWood = TexMat(woodTex, new Color(0.75f, 0.25f, 0.2f));
        var straw = TexMat(strawTex, new Color(0.9f, 0.75f, 0.3f));

        var farm = new GameObject("Farm").transform;
        farm.SetParent(transform);

        // 穀倉（東北角圍籬外）
        BuildBarn(farm, new Vector3(16f, 0f, 26f), redWood, wood);
        // 雞舍（西邊圍籬外）
        BuildCoop(farm, new Vector3(-26f, 0f, 4f), wood, straw);
        // 乾草捆（場內裝飾，無碰撞）
        BuildHayBale(farm, new Vector3(-13f, 0f, 13f), straw);
        BuildHayBale(farm, new Vector3(-11.6f, 0f, 12.4f), straw);
        BuildHayBale(farm, new Vector3(12f, 0f, -14f), straw);
        // 飼料槽（石板路旁）
        BuildTrough(farm, new Vector3(6f, 0f, 7.8f), wood);
        // 向日葵一排（北邊圍籬內側）
        for (int i = 0; i < 8; i++)
            BuildSunflower(farm, new Vector3(-16f + i * 4.5f, 0f, 18.6f));
        // 菜園畦（東南角，兩條土壟 + 菜苗）
        BuildVeggiePatch(farm, new Vector3(12f, 0f, -8f));
    }

    Material TexMat(Texture2D tex, Color tint)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tex != null ? Color.Lerp(tint, Color.white, 0.55f) : tint);
        if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
        }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        return m;
    }

    GameObject Prop(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
    {
        var p = GameObject.CreatePrimitive(type);
        Object.Destroy(p.GetComponent<Collider>()); // 場外裝飾不需要碰撞
        p.transform.SetParent(parent);
        p.transform.position = pos;
        p.transform.localScale = scale;
        if (euler.HasValue) p.transform.rotation = Quaternion.Euler(euler.Value);
        p.GetComponent<Renderer>().sharedMaterial = mat;
        return p;
    }

    void BuildBarn(Transform parent, Vector3 pos, Material walls, Material roof)
    {
        Prop(parent, PrimitiveType.Cube, pos + new Vector3(0f, 2f, 0f), new Vector3(7f, 4f, 5f), walls);
        // 斜屋頂：兩片斜板
        Prop(parent, PrimitiveType.Cube, pos + new Vector3(-1.85f, 4.8f, 0f), new Vector3(4.4f, 0.25f, 5.4f), roof, new Vector3(0f, 0f, 35f));
        Prop(parent, PrimitiveType.Cube, pos + new Vector3(1.85f, 4.8f, 0f), new Vector3(4.4f, 0.25f, 5.4f), roof, new Vector3(0f, 0f, -35f));
        // 大門
        Prop(parent, PrimitiveType.Cube, pos + new Vector3(0f, 1.2f, -2.55f), new Vector3(2.2f, 2.4f, 0.15f), roof);
    }

    void BuildCoop(Transform parent, Vector3 pos, Material wood, Material straw)
    {
        Prop(parent, PrimitiveType.Cube, pos + new Vector3(0f, 1.1f, 0f), new Vector3(3.5f, 2.2f, 3f), wood);
        Prop(parent, PrimitiveType.Cube, pos + new Vector3(0f, 2.5f, 0f), new Vector3(4f, 0.2f, 3.5f), straw, new Vector3(0f, 0f, 8f));
        // 四支腳
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Prop(parent, PrimitiveType.Cube, pos + new Vector3(x * 1.5f, 0.3f, z * 1.2f), new Vector3(0.2f, 0.6f, 0.2f), wood);
        // 小門洞
        Prop(parent, PrimitiveType.Cube, pos + new Vector3(0f, 0.9f, 1.55f), new Vector3(0.8f, 1f, 0.1f), straw);
    }

    void BuildHayBale(Transform parent, Vector3 pos, Material straw)
    {
        Prop(parent, PrimitiveType.Cylinder, pos + new Vector3(0f, 0.55f, 0f),
            new Vector3(1.1f, 0.6f, 1.1f), straw, new Vector3(0f, Random.Range(0f, 360f), 90f));
    }

    void BuildTrough(Transform parent, Vector3 pos, Material wood)
    {
        Prop(parent, PrimitiveType.Cube, pos + new Vector3(0f, 0.25f, 0f), new Vector3(2.2f, 0.5f, 0.7f), wood);
        Prop(parent, PrimitiveType.Cube, pos + new Vector3(0f, 0.52f, 0f), new Vector3(1.9f, 0.1f, 0.5f), RuntimeArt.Mat(new Color(0.85f, 0.7f, 0.3f)));
    }

    void BuildSunflower(Transform parent, Vector3 pos)
    {
        var stem = RuntimeArt.Mat(new Color(0.3f, 0.5f, 0.2f));
        float h = Random.Range(1.4f, 1.9f);
        Prop(parent, PrimitiveType.Cylinder, pos + new Vector3(0f, h / 2f, 0f), new Vector3(0.08f, h / 2f, 0.08f), stem);
        Prop(parent, PrimitiveType.Sphere, pos + new Vector3(0f, h + 0.1f, -0.06f), new Vector3(0.5f, 0.5f, 0.12f), RuntimeArt.Mat(new Color(1f, 0.8f, 0.1f)));
        Prop(parent, PrimitiveType.Sphere, pos + new Vector3(0f, h + 0.1f, -0.13f), new Vector3(0.22f, 0.22f, 0.1f), RuntimeArt.Mat(new Color(0.4f, 0.25f, 0.1f)));
    }

    void BuildVeggiePatch(Transform parent, Vector3 pos)
    {
        var soil = RuntimeArt.Mat(new Color(0.35f, 0.23f, 0.12f));
        var leafGreen = RuntimeArt.Mat(new Color(0.35f, 0.65f, 0.25f));
        for (int row = 0; row < 2; row++)
        {
            Vector3 rowPos = pos + new Vector3(0f, 0.08f, row * 1.6f);
            Prop(parent, PrimitiveType.Cube, rowPos, new Vector3(6f, 0.16f, 0.9f), soil);
            for (int i = 0; i < 6; i++)
                Prop(parent, PrimitiveType.Sphere,
                    rowPos + new Vector3(-2.5f + i * 1f, 0.2f, 0f),
                    new Vector3(0.35f, 0.28f, 0.35f), leafGreen);
        }
    }

    void SkyAndLight()
    {
        // 優先用 AI 生成的漸層天空，沒有才用程序天空
        var skyTex = Resources.Load<Texture2D>("Art/sky_gradient");
        var panoShader = Shader.Find("Skybox/Panoramic");
        if (skyTex != null && panoShader != null)
        {
            var sky = new Material(panoShader);
            sky.SetTexture("_MainTex", skyTex);
            if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.05f);
            RenderSettings.skybox = sky;
        }
        else
        {
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var sky = new Material(skyShader);
                sky.SetColor("_SkyTint", new Color(0.52f, 0.72f, 0.92f));
                sky.SetColor("_GroundColor", new Color(0.55f, 0.47f, 0.35f));
                sky.SetFloat("_Exposure", 1.15f);
                sky.SetFloat("_AtmosphereThickness", 0.9f);
                RenderSettings.skybox = sky;
            }
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.012f;
        RenderSettings.fogColor = new Color(0.75f, 0.8f, 0.85f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.6f, 0.68f, 0.78f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.52f, 0.45f);
        RenderSettings.ambientGroundColor = new Color(0.35f, 0.3f, 0.22f);

        var lightGo = GameObject.Find("Directional Light");
        if (lightGo != null)
        {
            var l = lightGo.GetComponent<Light>();
            l.intensity = 1.35f;
            l.color = new Color(1f, 0.94f, 0.8f);
            lightGo.transform.rotation = Quaternion.Euler(44f, -35f, 0f);
            RenderSettings.sun = l;
        }
    }

    void PostFx()
    {
        var volGo = new GameObject("PostFxVolume");
        volGo.transform.SetParent(transform);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        vol.profile = profile;

        var bloom = profile.Add<Bloom>();
        bloom.intensity.Override(0.45f);
        bloom.threshold.Override(1.0f);

        var vig = profile.Add<Vignette>();
        vig.intensity.Override(0.26f);
        vig.smoothness.Override(0.4f);

        var ca = profile.Add<ColorAdjustments>();
        ca.saturation.Override(14f);
        ca.contrast.Override(10f);
        ca.postExposure.Override(0.1f);

        var tone = profile.Add<Tonemapping>();
        tone.mode.Override(TonemappingMode.ACES);

        var cam = Camera.main;
        if (cam != null)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }
    }

    void GroundTextures()
    {
        var ground = GameObject.Find("Ground_SoftSoil");
        if (ground != null)
        {
            var r = ground.GetComponent<Renderer>();
            var m = r.material; // 執行期實例，不動資產

            // 優先用 AI 手繪草地（鏡像平鋪隱藏接縫），沒有才用程序貼圖
            Texture2D tex = Resources.Load<Texture2D>("Art/grass_tile");
            float tiling = 6f;
            if (tex == null) { tex = ProceduralTex.GrassBlotch(); tiling = 7f; }

            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling)); }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            m.mainTexture = tex;
        }

        var path = GameObject.Find("HardPath");
        if (path != null)
        {
            var r = path.GetComponent<Renderer>();
            var m = r.material;
            var tex = ProceduralTex.Stone();
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(10f, 1f)); }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            m.mainTexture = tex;
        }
    }

    void ScatterGrass(int count)
    {
        var parent = new GameObject("Grass").transform;
        parent.SetParent(transform);
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = RandomGroundPos();
            if (pos.z > 2.6f && pos.z < 7.4f) continue; // 避開石板路

            var tuft = new GameObject("Tuft").transform;
            tuft.SetParent(parent);
            tuft.position = pos;
            tuft.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            int blades = Random.Range(2, 4);
            for (int b = 0; b < blades; b++)
            {
                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(blade.GetComponent<Collider>());
                blade.transform.SetParent(tuft);
                float h = Random.Range(0.25f, 0.55f);
                blade.transform.localPosition = new Vector3(Random.Range(-0.1f, 0.1f), h / 2f, Random.Range(-0.1f, 0.1f));
                blade.transform.localRotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
                blade.transform.localScale = new Vector3(0.05f, h, 0.02f);
                blade.GetComponent<Renderer>().sharedMaterial =
                    RuntimeArt.Mat(GrassPalette[Random.Range(0, GrassPalette.Length)]);
            }
        }
    }

    void ScatterRocks(int count)
    {
        var parent = new GameObject("Rocks").transform;
        parent.SetParent(transform);
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = RandomGroundPos();
            if (pos.z > 2.6f && pos.z < 7.4f) continue;
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(rock.GetComponent<Collider>()); // 視覺石頭，不卡蚯蚓
            rock.transform.SetParent(parent);
            float s = Random.Range(0.25f, 0.7f);
            rock.transform.position = new Vector3(pos.x, s * 0.28f, pos.z);
            rock.transform.localScale = new Vector3(s, s * 0.6f, s * Random.Range(0.8f, 1.2f));
            rock.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            rock.GetComponent<Renderer>().sharedMaterial =
                RuntimeArt.Mat(RockPalette[Random.Range(0, RockPalette.Length)]);
        }
    }

    void PlantTrees(int count)
    {
        var parent = new GameObject("Trees").transform;
        parent.SetParent(transform);
        var barkMat = RuntimeArt.Mat(new Color(0.42f, 0.29f, 0.17f));
        for (int i = 0; i < count; i++)
        {
            // 圍籬外一圈
            float ang = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            float rad = Random.Range(23f, 30f);
            Vector3 pos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);

            var tree = new GameObject("Tree").transform;
            tree.SetParent(parent);
            tree.position = pos;

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(trunk.GetComponent<Collider>());
            trunk.transform.SetParent(tree);
            float th = Random.Range(2.2f, 3.5f);
            trunk.transform.localPosition = new Vector3(0f, th / 2f, 0f);
            trunk.transform.localScale = new Vector3(0.45f, th / 2f, 0.45f);
            trunk.GetComponent<Renderer>().sharedMaterial = barkMat;

            int clumps = Random.Range(2, 4);
            for (int c = 0; c < clumps; c++)
            {
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(leaf.GetComponent<Collider>());
                leaf.transform.SetParent(tree);
                float ls = Random.Range(1.6f, 2.6f);
                leaf.transform.localPosition = new Vector3(Random.Range(-0.8f, 0.8f), th + Random.Range(-0.3f, 0.8f), Random.Range(-0.8f, 0.8f));
                leaf.transform.localScale = new Vector3(ls, ls * 0.8f, ls);
                leaf.GetComponent<Renderer>().sharedMaterial =
                    RuntimeArt.Mat(LeafPalette[Random.Range(0, LeafPalette.Length)]);
            }
        }
    }

    void FenceMakeover()
    {
        var wood = RuntimeArt.Mat(new Color(0.5f, 0.36f, 0.2f));
        var woodDark = RuntimeArt.Mat(new Color(0.42f, 0.3f, 0.16f));
        string[] names = { "Fence_N", "Fence_S", "Fence_E", "Fence_W" };
        foreach (var n in names)
        {
            var wall = GameObject.Find(n);
            if (wall == null) continue;
            var r = wall.GetComponent<Renderer>();
            if (r != null) r.enabled = false; // 隱藏灰牆，碰撞保留
        }

        var parent = new GameObject("FenceVisual").transform;
        parent.SetParent(transform);
        BuildFenceLine(parent, new Vector3(-20f, 0f, 20f), new Vector3(20f, 0f, 20f), wood, woodDark);
        BuildFenceLine(parent, new Vector3(-20f, 0f, -20f), new Vector3(20f, 0f, -20f), wood, woodDark);
        BuildFenceLine(parent, new Vector3(20f, 0f, -20f), new Vector3(20f, 0f, 20f), wood, woodDark);
        BuildFenceLine(parent, new Vector3(-20f, 0f, -20f), new Vector3(-20f, 0f, 20f), wood, woodDark);
    }

    void BuildFenceLine(Transform parent, Vector3 a, Vector3 b, Material post, Material rail)
    {
        Vector3 dir = (b - a).normalized;
        float len = Vector3.Distance(a, b);
        int posts = Mathf.FloorToInt(len / 2f) + 1;

        for (int i = 0; i < posts; i++)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(parent);
            p.transform.position = a + dir * (i * 2f) + new Vector3(0f, 0.55f, 0f);
            p.transform.localScale = new Vector3(0.16f, 1.1f, 0.16f);
            p.GetComponent<Renderer>().sharedMaterial = post;
        }
        for (int lane = 0; lane < 2; lane++)
        {
            var railGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(railGo.GetComponent<Collider>());
            railGo.transform.SetParent(parent);
            railGo.transform.position = (a + b) / 2f + new Vector3(0f, lane == 0 ? 0.38f : 0.85f, 0f);
            railGo.transform.rotation = Quaternion.LookRotation(dir);
            railGo.transform.localScale = new Vector3(0.08f, 0.12f, len);
            railGo.GetComponent<Renderer>().sharedMaterial = rail;
        }
    }

    static Vector3 RandomGroundPos()
    {
        return new Vector3(Random.Range(-18.5f, 18.5f), 0f, Random.Range(-18.5f, 18.5f));
    }
}
