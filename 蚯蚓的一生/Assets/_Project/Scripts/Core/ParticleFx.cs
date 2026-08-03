using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// 粒子特效工具：一次性爆發，支援 AI 生成貼圖（dust/glow/leaves/feathers）
public static class ParticleFx
{
    static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

    /// tex: Resources/Art 下的貼圖名（null = 無貼圖）；additive: 發光疊加；sheetX/Y: 圖集格數
    public static void Burst(Vector3 pos, Color color, int count, float speed, float size,
        float gravity = 1f, float life = 0.7f,
        string tex = null, bool additive = false, int sheetX = 0, int sheetY = 0)
    {
        var go = new GameObject("fx_burst");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = life;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count + 8;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;

        // 淡出
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        if (sheetX > 0 && sheetY > 0)
        {
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = sheetX;
            tsa.numTilesY = sheetY;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); // 不播動畫
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f); // 隨機一格
        }

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = GetMat(tex, additive);

        ps.Emit(count);
        Object.Destroy(go, life + 0.5f);
    }

    /// 給外部常駐粒子系統共用材質
    public static Material SharedMat(string texName, bool additive) => GetMat(texName, additive);

    static Material GetMat(string texName, bool additive)
    {
        string key = (texName ?? "_none") + (additive ? "_add" : "_alpha");
        if (matCache.TryGetValue(key, out var cached) && cached != null) return cached;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var m = new Material(shader);

        // 透明設定（URP Unlit 粒子）
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // Transparent
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
        if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");

        if (texName != null)
        {
            var tex = Resources.Load<Texture2D>("Art/" + texName);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                m.mainTexture = tex;
            }
        }

        matCache[key] = m;
        return m;
    }
}
