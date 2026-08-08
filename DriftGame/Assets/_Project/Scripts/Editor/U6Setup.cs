using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Unity 6 / URP 17.5 的功能開關（流程 MD §6.3）。
/// 欄位名已在 SumoParty 用反射探勘確認過，這裡直接套用。
/// </summary>
public static class U6Setup
{
    [MenuItem("Drift/開啟 Unity 6 畫質與效能功能")]
    public static void EnableFeatures()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) { Debug.LogError("[U6] 找不到 render pipeline asset"); return; }

        var so = new SerializedObject(rp);
        var sb = new StringBuilder("=== U6 ENABLE (DriftGame) ===\n");

        // STP：低解析度算圖再時空升頻。競速遊戲畫面動得快，STP 的時間累積效果特別划算。
        SetInt(so, "m_UpscalingFilter", 4, "Upscaling Filter → STP", sb);
        SetFloat(so, "m_RenderScale", 0.75f, "Render Scale → 0.75", sb);

        // GPU Resident Drawer：賽道兩側大量重複物件，這裡收益最大
        SetInt(so, "m_GPUResidentDrawerMode", 1, "GPU Resident Drawer → Instanced Drawing", sb);
        SetFloat(so, "m_SmallMeshScreenPercentage", 0f, "Small Mesh Screen % → 0", sb);
        SetBool(so, "m_GPUResidentDrawerEnableOcclusionCullingInCameras", true, "GPU Occlusion Culling → on", sb);

        // 半寫實光照清單
        SetBool(so, "m_ReflectionProbeBlending", true, "Reflection Probe Blending → on", sb);
        SetBool(so, "m_ReflectionProbeBoxProjection", true, "Reflection Probe Box Projection → on", sb);

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rp);
        AssetDatabase.SaveAssets();
        Debug.Log(sb.ToString());
    }

    static void SetInt(SerializedObject so, string path, int v, string label, StringBuilder sb)
    {
        var p = so.FindProperty(path);
        if (p == null) { sb.AppendLine($"  [跳過] {path} 不存在"); return; }
        sb.AppendLine($"  [設定] {label}（{p.intValue} → {v}）");
        p.intValue = v;
    }

    static void SetFloat(SerializedObject so, string path, float v, string label, StringBuilder sb)
    {
        var p = so.FindProperty(path);
        if (p == null) { sb.AppendLine($"  [跳過] {path} 不存在"); return; }
        sb.AppendLine($"  [設定] {label}（{p.floatValue} → {v}）");
        p.floatValue = v;
    }

    static void SetBool(SerializedObject so, string path, bool v, string label, StringBuilder sb)
    {
        var p = so.FindProperty(path);
        if (p == null) { sb.AppendLine($"  [跳過] {path} 不存在"); return; }
        sb.AppendLine($"  [設定] {label}（{p.boolValue} → {v}）");
        p.boolValue = v;
    }
}
