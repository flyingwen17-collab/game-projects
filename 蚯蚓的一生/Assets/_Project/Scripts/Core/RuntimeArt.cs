using System.Collections.Generic;
using UnityEngine;

/// 執行期材質工廠：同色共用一份材質
public static class RuntimeArt
{
    static readonly Dictionary<Color, Material> cache = new Dictionary<Color, Material>();
    static Shader shader;

    public static Material Mat(Color color)
    {
        if (cache.TryGetValue(color, out var m) && m != null) return m;

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
        }

        m = new Material(shader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        cache[color] = m;
        return m;
    }
}
