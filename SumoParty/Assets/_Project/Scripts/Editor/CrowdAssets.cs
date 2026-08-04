using UnityEditor;
using UnityEngine;

/// Quaternius「Background Posed Humans」（CC0）觀眾模型的載入工具。
/// 模型單位不一，一律「量邊界 → 縮到目標身高 → 底部貼地」。
public static class CrowdAssets
{
    public const string Root = "Assets/_Project/Art/Crowd";

    public static GameObject Spawn(string model, Transform parent, Vector3 pos,
                                   Quaternion rot, float targetHeight)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + model + ".fbx");
        if (prefab == null) { Debug.LogWarning("[CrowdAssets] 找不到 " + model); return null; }
        var go = (GameObject)Object.Instantiate(prefab, pos, rot);
        go.name = model;
        if (parent != null) go.transform.SetParent(parent, true);

        // 這批 FBX 是 Z 朝上的匯出慣例，直接擺會整批躺平。
        // 用邊界判斷：垂直尺寸明顯小於水平尺寸就補轉 -90°X 立正。
        var b = MeasureBounds(go);
        if (b.size.y < Mathf.Max(b.size.x, b.size.z) * 0.75f)
        {
            go.transform.rotation = rot * Quaternion.Euler(-90f, 0f, 0f);
            b = MeasureBounds(go);
        }
        go.transform.localScale *= targetHeight / Mathf.Max(0.001f, b.size.y);
        b = MeasureBounds(go);
        go.transform.position += Vector3.up * (pos.y - b.min.y);
        return go;
    }

    public static Bounds MeasureBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }
}

/// 匯入時把 FBX 內嵌材質轉成 URP Lit（否則整批粉紅色），並開 GPU Instancing。
class CrowdModelPostprocessor : AssetPostprocessor
{
    bool IsCrowd => assetPath.Replace('\\', '/').Contains("/_Project/Art/Crowd/");

    void OnPostprocessMaterial(Material mat)
    {
        if (!IsCrowd) return;
        var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
        var tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", color);
        if (tex != null) mat.SetTexture("_BaseMap", tex);
        mat.SetFloat("_Smoothness", 0.2f);
        mat.SetFloat("_Metallic", 0f);
        mat.enableInstancing = true;
    }
}
