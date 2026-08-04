using UnityEditor;
using UnityEngine;

/// Kenney CC0 模型庫（Assets/_Project/Art/Kenney）的載入與擺放工具。
/// 所有模型單位不一，一律用「量測邊界 → 縮放到目標尺寸 → 底部貼地」處理，
/// 不假設任何 FBX 的原始比例或樞紐位置。
public static class KenneyLib
{
    public const string Root = "Assets/_Project/Art/Kenney";

    public static GameObject Load(string kit, string model)
    {
        string path = Root + "/" + kit + "/" + model + ".fbx";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) Debug.LogWarning("[KenneyLib] 找不到模型 " + path);
        return prefab;
    }

    /// 實例化模型。回傳 null 表示模型不存在（呼叫端可退回舊視覺）。
    public static GameObject Place(string kit, string model, Transform parent,
                                   Vector3 pos, Quaternion rot)
    {
        var prefab = Load(kit, model);
        if (prefab == null) return null;
        var go = (GameObject)Object.Instantiate(prefab, pos, rot);
        go.name = model;
        if (parent != null) go.transform.SetParent(parent, true);
        return go;
    }

    /// 世界空間的 Renderer 總邊界。
    public static Bounds MeasureBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    /// 等比縮放到目標高度，底部貼在 pos.y（樹、燈柱、看台、旗幟）。
    public static GameObject PlaceToHeight(string kit, string model, Transform parent,
                                           Vector3 pos, Quaternion rot, float targetHeight)
    {
        var go = Place(kit, model, parent, pos, rot);
        if (go == null) return null;
        var b = MeasureBounds(go);
        go.transform.localScale *= targetHeight / Mathf.Max(0.001f, b.size.y);
        DropToGround(go, pos.y);
        return go;
    }

    /// 各軸獨立縮放到目標尺寸（模型自身座標的 寬x高x深），適合建築與護欄。
    public static GameObject PlaceToSize(string kit, string model, Transform parent,
                                         Vector3 pos, Quaternion rot, Vector3 targetSize)
    {
        var go = Place(kit, model, parent, pos, Quaternion.identity);
        if (go == null) return null;
        var b = MeasureBounds(go);
        go.transform.localScale = new Vector3(
            targetSize.x / Mathf.Max(0.001f, b.size.x),
            targetSize.y / Mathf.Max(0.001f, b.size.y),
            targetSize.z / Mathf.Max(0.001f, b.size.z));
        go.transform.rotation = rot;
        DropToGround(go, pos.y);
        return go;
    }

    /// 讓模型邊界底部貼齊指定高度（不懸空、不陷地）。
    public static void DropToGround(GameObject go, float groundY)
    {
        var b = MeasureBounds(go);
        go.transform.position += Vector3.up * (groundY - b.min.y);
    }

    /// 水平置中：把邊界中心移到指定的世界 XZ 位置（樞紐不在中心的模型用）。
    public static void CenterXZ(GameObject go, Vector3 worldPos)
    {
        var b = MeasureBounds(go);
        var d = worldPos - b.center;
        go.transform.position += new Vector3(d.x, 0f, d.z);
    }

    public static void MakeStatic(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>())
            t.gameObject.isStatic = true;
    }
}

/// 匯入設定：Kenney FBX 的內嵌材質從 Standard 轉成 URP Lit（否則整包是粉紅色）。
/// 順便開 GPU Instancing —— 護欄和樹會鋪上千個實例。
class KenneyModelPostprocessor : AssetPostprocessor
{
    bool IsKenney => assetPath.Replace('\\', '/').Contains("/_Project/Art/Kenney/");

    void OnPostprocessMaterial(Material mat)
    {
        if (!IsKenney) return;
        var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
        var tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", color);
        if (tex != null) mat.SetTexture("_BaseMap", tex);
        mat.SetFloat("_Smoothness", 0.18f);
        mat.SetFloat("_Metallic", 0f);
        mat.enableInstancing = true;
    }

    /// 全景天空 HDR：解析度放到 4K，別被預設 2048 壓糊。
    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("SKY_") || !assetPath.EndsWith(".hdr")) return;
        var ti = (TextureImporter)assetImporter;
        ti.maxTextureSize = 4096;
        ti.mipmapEnabled = true;
    }
}
