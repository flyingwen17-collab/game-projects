using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Unity 6 升級後的功能開關與量測工具（流程 MD v3.0 §5 關卡 3）。
/// 全部走 batch mode -executeMethod，不需要開編輯器。
/// 注意：算圖的入口不能加 -nographics。
/// </summary>
public static class U6Tools
{
    const string ScenePath = "Assets/_Project/Scenes/Dohyo.unity";

    // ---------- 1. 探勘：這個 URP 版本到底有哪些可設的欄位 ----------

    public static void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== U6 REPORT ===");
        sb.AppendLine("Unity: " + Application.unityVersion);

        var rp = GraphicsSettings.currentRenderPipeline;
        sb.AppendLine("Pipeline asset: " + (rp != null ? rp.name + " (" + rp.GetType().FullName + ")" : "NULL"));

        if (rp != null)
        {
            sb.AppendLine("--- 相關成員 ---");
            foreach (var p in rp.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                string n = p.Name.ToLowerInvariant();
                if (n.Contains("gpu") || n.Contains("resident") || n.Contains("upscal") ||
                    n.Contains("renderscale") || n.Contains("probe") || n.Contains("rendergraph") ||
                    n.Contains("batcher") || n.Contains("occlusion"))
                {
                    object v = "?";
                    try { v = p.GetValue(rp); } catch (Exception e) { v = "<" + e.GetType().Name + ">"; }
                    sb.AppendLine($"  [prop] {p.Name} = {v}  (canWrite={p.CanWrite})");
                }
            }
        }

        // 序列化欄位才是我們真正要寫的東西（屬性名和欄位名常常不一樣）
        if (rp != null)
        {
            sb.AppendLine("--- 序列化欄位 ---");
            var so = new SerializedObject(rp);
            var it = so.GetIterator();
            while (it.NextVisible(true))
            {
                string n = it.name.ToLowerInvariant();
                if (n.Contains("gpu") || n.Contains("resident") || n.Contains("upscal") ||
                    n.Contains("renderscale") || n.Contains("probe") || n.Contains("rendergraph") ||
                    n.Contains("batcher") || n.Contains("occlusion") || n.Contains("mesh"))
                    sb.AppendLine($"  [field] {it.propertyPath} ({it.propertyType})");
            }
        }

        Debug.Log(sb.ToString());
    }

    // ---------- 2. 開啟 Unity 6 的三項功能 ----------

    /// <summary>
    /// 階段 A：不需要烘焙的純效能/畫質提升。APV 另外走 EnableAPV（要放 Probe Volume + 烘焙）。
    /// 欄位名由 Report() 在 URP 17.5.0 實地確認過，不是猜的。
    /// </summary>
    public static void EnableFeatures()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) { Debug.LogError("[U6] 找不到 render pipeline asset"); return; }

        var so = new SerializedObject(rp);
        var sb = new StringBuilder("=== U6 ENABLE (階段 A) ===\n");

        // STP：低解析度算圖再時空升頻。1060 上最大的效能來源。
        // UpscalingFilterSelection: Auto=0 Linear=1 Point=2 FSR=3 STP=4
        SetInt(so, "m_UpscalingFilter", 4, "Upscaling Filter → STP", sb);
        SetFloat(so, "m_RenderScale", 0.75f, "Render Scale → 0.75", sb);

        // GPU Resident Drawer：GPUResidentDrawerMode Disabled=0 InstancedDrawing=1
        SetInt(so, "m_GPUResidentDrawerMode", 1, "GPU Resident Drawer → Instanced Drawing", sb);
        SetFloat(so, "m_SmallMeshScreenPercentage", 0f, "Small Mesh Screen % → 0", sb);
        SetBool(so, "m_GPUResidentDrawerEnableOcclusionCullingInCameras", true, "GPU Occlusion Culling → on", sb);

        // 半寫實光照清單（流程 MD §6.3）：反射探針的混合與盒投影，成本低、去塑膠感
        SetBool(so, "m_ReflectionProbeBlending", true, "Reflection Probe Blending → on", sb);
        SetBool(so, "m_ReflectionProbeBoxProjection", true, "Reflection Probe Box Projection → on", sb);

        // 註：m_EnableRenderGraph 在 URP 17.5 已移除（Compatibility Mode 廢止，Render Graph 是唯一路徑）

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rp);
        AssetDatabase.SaveAssets();
        Debug.Log(sb.ToString());
    }

    /// <summary>把階段 A 的設定還原成 Unity 6 預設，用來取得公平的對照組。</summary>
    public static void DisableFeatures()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) { Debug.LogError("[U6] 找不到 render pipeline asset"); return; }

        var so = new SerializedObject(rp);
        var sb = new StringBuilder("=== U6 DISABLE (對照組) ===\n");
        SetInt(so, "m_UpscalingFilter", 0, "Upscaling Filter → Auto", sb);
        SetFloat(so, "m_RenderScale", 1f, "Render Scale → 1.0", sb);
        SetInt(so, "m_GPUResidentDrawerMode", 0, "GPU Resident Drawer → Disabled", sb);
        SetBool(so, "m_GPUResidentDrawerEnableOcclusionCullingInCameras", false, "GPU Occlusion Culling → off", sb);
        SetBool(so, "m_ReflectionProbeBlending", false, "Reflection Probe Blending → off", sb);
        SetBool(so, "m_ReflectionProbeBoxProjection", false, "Reflection Probe Box Projection → off", sb);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rp);
        AssetDatabase.SaveAssets();
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 真正的 A/B：同一個 Unity session 內，關功能拍一次 → 開功能拍一次。
    /// 每次拍攝前都先暖機算圖，否則第一張會是黑的（著色器變體與光照尚未就緒）。
    /// </summary>
    public static void RunAB()
    {
        DisableFeatures();
        Capture("before");
        EnableFeatures();
        Capture("after");
    }

    static bool SetInt(SerializedObject so, string path, int value, string label, StringBuilder sb)
    {
        var p = so.FindProperty(path);
        if (p == null) { sb.AppendLine($"  [跳過] {path} 不存在"); return false; }
        sb.AppendLine($"  [設定] {label}（{path}: {p.intValue} → {value}）");
        p.intValue = value;
        return true;
    }

    static bool SetFloat(SerializedObject so, string path, float value, string label, StringBuilder sb)
    {
        var p = so.FindProperty(path);
        if (p == null) { sb.AppendLine($"  [跳過] {path} 不存在"); return false; }
        sb.AppendLine($"  [設定] {label}（{path}: {p.floatValue} → {value}）");
        p.floatValue = value;
        return true;
    }

    static bool SetBool(SerializedObject so, string path, bool value, string label, StringBuilder sb)
    {
        var p = so.FindProperty(path);
        if (p == null) { sb.AppendLine($"  [跳過] {path} 不存在"); return false; }
        sb.AppendLine($"  [設定] {label}（{path}: {p.boolValue} → {value}）");
        p.boolValue = value;
        return true;
    }

    // ---------- 3. 截圖 + 效能數字（關卡 2 / 關卡 3） ----------

    public static void CaptureBefore() { Capture("before"); }
    public static void CaptureAfter() { Capture("after"); }

    static void Capture(string tag)
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath);

        string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(outDir);

        var cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
        if (cam == null) { Debug.LogError("[U6] 場景裡沒有相機"); return; }


        var sb = new StringBuilder($"=== U6 CAPTURE [{tag}] ===\n");

        Shoot(cam, Path.Combine(outDir, $"u6_{tag}_overview.png"), sb, "overview",
              new Vector3(0f, 6.5f, -11f), new Vector3(0f, 1.0f, 0f));
        Shoot(cam, Path.Combine(outDir, $"u6_{tag}_low.png"), sb, "low_angle",
              new Vector3(4.5f, 1.6f, -4.5f), new Vector3(0f, 0.9f, 0f));
        Shoot(cam, Path.Combine(outDir, $"u6_{tag}_wide.png"), sb, "wide",
              new Vector3(-9f, 3.5f, -9f), new Vector3(0f, 1.0f, 0f));

        Debug.Log(sb.ToString());
    }

    static void Shoot(Camera cam, string file, StringBuilder sb, string label, Vector3 pos, Vector3 lookAt)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(lookAt);

        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt;

        // 暖機：batch mode 第一次算圖時著色器變體/光照/反射探針還沒就緒，
        // 直接拍會得到一張黑圖並誤判成「功能有效」。丟掉前幾張。
        for (int i = 0; i < 5; i++) cam.Render();

        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());

        sb.AppendLine($"  [{label}] tris={UnityStats.triangles} verts={UnityStats.vertices} " +
                      $"setPass={UnityStats.setPassCalls} drawCalls={UnityStats.drawCalls} " +
                      $"→ {Path.GetFileName(file)}");

        cam.targetTexture = null;
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(tex);
    }
}
