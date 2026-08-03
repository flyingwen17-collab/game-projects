using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// 場景美化：天空、霧、後製、地面貼圖、草、石、樹、木圍籬（全程式生成）
public class EnvironmentDecorator : MonoBehaviour
{
    void Start()
    {
        Random.InitState(20260802);
        SkyAndLight();
        PostFx();
        GroundTextures();
        ScatterGrass(260);
        ScatterRocks(22);
        PlantTrees(12);
        FenceMakeover();
    }

    void SkyAndLight()
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
            var tex = ProceduralTex.GrassBlotch();
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(7f, 7f)); }
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
                float g = Random.Range(0.38f, 0.55f);
                blade.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(new Color(g * 0.55f, g, g * 0.35f));
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
            float v = Random.Range(0.4f, 0.55f);
            rock.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(new Color(v, v, v * 1.05f));
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
                float g = Random.Range(0.32f, 0.5f);
                leaf.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(new Color(g * 0.5f, g, g * 0.35f));
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
