using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Adaptive Probe Volumes（流程 MD §6.3 半寫實光照清單）。
/// APV 的烘焙 API 在 URP 各小版本間搬過家，所以先 Report 探勘、再用反射呼叫，
/// 不對著文件猜方法名。
/// </summary>
public static class APVTools
{
    const string ScenePath = "Assets/_Project/Scenes/P1_Dohyo.unity";

    // ---------- 探勘 ----------

    public static void Report()
    {
        var sb = new StringBuilder("=== APV REPORT ===\n");

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }

            foreach (var t in types)
            {
                if (t.Name != "AdaptiveProbeVolumes" && t.Name != "ProbeVolumeBakingSet" &&
                    t.Name != "ProbeVolume" && t.Name != "ProbeReferenceVolume") continue;

                sb.AppendLine($"[type] {t.FullName}  ({asm.GetName().Name})");
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    string n = m.Name.ToLowerInvariant();
                    if (n.Contains("bake") || n.Contains("generate") || n.Contains("set"))
                        sb.AppendLine($"    static {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
                }
            }
        }

        Debug.Log(sb.ToString());
    }

    // ---------- 設定 + 烘焙 ----------

    /// <summary>0 = 傳統光探針，1 = Adaptive Probe Volumes。用來做 A/B。</summary>
    public static void SetLightProbeSystem(int value)
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) return;
        var so = new SerializedObject(rp);
        var p = so.FindProperty("m_LightProbeSystem");
        if (p == null) return;
        p.intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rp);
        AssetDatabase.SaveAssets();
        Debug.Log($"[APV] m_LightProbeSystem → {value}");
    }

    public static void SetupOnly()
    {
        var sb = new StringBuilder("=== APV SETUP ===\n");

        // 1) URP asset：光探針系統切成 APV
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) { Debug.LogError("[APV] 找不到 render pipeline asset"); return; }
        var so = new SerializedObject(rp);
        var lps = so.FindProperty("m_LightProbeSystem");
        if (lps == null) { Debug.LogError("[APV] 找不到 m_LightProbeSystem"); return; }
        sb.AppendLine($"  m_LightProbeSystem: {lps.intValue} → 1 (ProbeVolumes)");
        lps.intValue = 1;                                   // LightProbeSystem.ProbeVolumes
        var shBands = so.FindProperty("m_ProbeVolumeSHBands");
        if (shBands != null) { shBands.intValue = 1; sb.AppendLine("  m_ProbeVolumeSHBands → L2（間接光細節）"); }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rp);
        AssetDatabase.SaveAssets();

        // 2) 場景裡放一個涵蓋整個會場的 Probe Volume
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) { EditorSceneManager.OpenScene(ScenePath); scene = SceneManager.GetActiveScene(); }

        var existing = UnityEngine.Object.FindObjectsByType<ProbeVolume>();
        if (existing.Length == 0)
        {
            var go = new GameObject("Adaptive Probe Volume");
            var pv = go.AddComponent<ProbeVolume>();
            pv.mode = ProbeVolume.Mode.Global;              // 自動涵蓋整個場景，不用手動量尺寸
            sb.AppendLine("  已新增 Probe Volume（Global 模式）");
        }
        else sb.AppendLine($"  場景已有 {existing.Length} 個 Probe Volume，沿用");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 讓場景「有東西可烘」。單純開 APV 是沒用的——實測 6 個光源全 Realtime、
    /// 361 個物件全部未標記 Contribute GI，烘出來是空的，畫面零變化。
    /// </summary>
    public static void PrepareSceneForGI()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) { EditorSceneManager.OpenScene(ScenePath); scene = SceneManager.GetActiveScene(); }

        var sb = new StringBuilder("=== APV 場景準備 ===\n");

        // 1) 光源改 Mixed：直接光仍即時（保留動態陰影），間接光才進得了 APV
        int lit = 0;
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>())
        {
            if (l.lightmapBakeType == LightmapBakeType.Mixed) continue;
            l.lightmapBakeType = LightmapBakeType.Mixed;
            lit++;
        }
        sb.AppendLine($"  {lit} 個光源 Realtime → Mixed");

        // 2) 靜態標記。
        //    刻意「不」加 BatchingStatic —— 靜態批次會把物件排除在 GPU Resident Drawer
        //    之外，等於把剛量到的 SetPass 峰值改善(3996→44)吐回去。
        const StaticEditorFlags Flags =
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.ReflectionProbeStatic;

        int marked = 0, skipped = 0;
        foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
        {
            var go = r.gameObject;
            // 會動的東西不能標靜態：力士、任何有剛體或動畫的物件
            bool dynamic = go.GetComponentInParent<Rigidbody>() != null
                        || go.GetComponentInParent<Animator>() != null
                        || go.GetComponentInParent<Rikishi>() != null;
            if (dynamic) { skipped++; continue; }

            GameObjectUtility.SetStaticEditorFlags(go, Flags);
            r.receiveGI = ReceiveGI.LightProbes;   // APV 走探針，不需要 lightmap UV
            marked++;
        }
        sb.AppendLine($"  {marked} 個物件標記 Contribute GI（跳過 {skipped} 個會動的）");
        sb.AppendLine("  刻意未加 BatchingStatic（會停用 GPU Resident Drawer）");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(sb.ToString());
    }

    /// <summary>完整流程：設定 → 準備場景 → 只烘 APV。</summary>
    public static void FullSetup()
    {
        SetupOnly();
        PrepareSceneForGI();
        Bake();
    }

    static void Bake()
    {
        var sb = new StringBuilder("=== APV BAKE ===\n");

        // 註：型別的命名空間是 UnityEngine.Rendering，只有「組件」叫 ...Core.Editor。
        // 一開始用命名空間過濾找不到，白走了一次完整烘焙的退路。
        Type apv = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
            .FirstOrDefault(t => t.Name == "AdaptiveProbeVolumes" &&
                                 t.Assembly.GetName().Name.EndsWith(".Editor"));

        if (apv != null)
        {
            var m = apv.GetMethod("BakeAsync", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)
                 ?? apv.GetMethod("Bake", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            sb.AppendLine($"  找到 {apv.FullName}（有 {(m != null ? m.Name : "無")} ）");
        }
        else sb.AppendLine("  找不到 AdaptiveProbeVolumes 型別");

        // ⚠️ 不要用 BakeAsync()：它是非同步的，而 Lightmapping.isRunning 追蹤不到
        // APV 的工作，等待迴圈會直接放行，接著 -quit 就把烘焙砍在半路。
        // 症狀是 log 只有 "Baking Adaptive Probe Volumes" 卻沒有
        // "Generating Probe Volume Bricks"，Baking Set 停在 3 KB、畫面零變化。
        // 同步的 Lightmapping.Bake() 在 batch mode 才跑得完。
        var t0 = DateTime.Now;
        sb.AppendLine("  Lightmapping.Bake()（同步）");
        Lightmapping.Bake();
        sb.AppendLine($"  烘焙完成，耗時 {(DateTime.Now - t0).TotalSeconds:0} 秒");
        Debug.Log(sb.ToString());
    }
}
