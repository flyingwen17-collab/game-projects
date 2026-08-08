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
    const string ScenePath = "Assets/_Project/Scenes/Arena.unity";

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

    public static void SetupAndBake()
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

        // 3) 烘焙。API 位置在版本間會變，反射找 AdaptiveProbeVolumes.BakeAsync/Bake
        Bake();
    }

    static void Bake()
    {
        var sb = new StringBuilder("=== APV BAKE ===\n");

        Type apv = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
            .FirstOrDefault(t => t.Name == "AdaptiveProbeVolumes" && t.Namespace != null && t.Namespace.Contains("Editor"));

        if (apv != null)
        {
            var m = apv.GetMethod("BakeAsync", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)
                 ?? apv.GetMethod("Bake", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (m != null)
            {
                sb.AppendLine($"  呼叫 {apv.FullName}.{m.Name}()");
                m.Invoke(null, null);
                // BakeAsync 是非同步的，等它結束
                while (Lightmapping.isRunning) System.Threading.Thread.Sleep(500);
                sb.AppendLine("  APV 烘焙完成");
                Debug.Log(sb.ToString());
                return;
            }
            sb.AppendLine($"  找到 {apv.FullName} 但沒有無參數的 Bake/BakeAsync");
        }
        else sb.AppendLine("  找不到 AdaptiveProbeVolumes 型別");

        // 退路：走完整烘焙（會連 lightmap 一起烘，比較慢）
        sb.AppendLine("  退回 Lightmapping.Bake()");
        Lightmapping.Bake();
        sb.AppendLine("  烘焙完成");
        Debug.Log(sb.ToString());
    }
}
