using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 流程 MD §5 關卡 3 的 batch mode 入口。
///
/// 用法（注意：不能加 -quit，也不能加 -nographics）：
///   Unity.exe -batchmode -projectPath X -executeMethod PerfHarness.RunBaseline -logFile Y
///
/// executeMethod 只負責進 Play；量完由 PerfSampler 自己呼叫 EditorApplication.Exit(0) 收工。
/// 加了 -quit 會在進 Play 前就關掉，什麼都量不到。
/// </summary>
public static class PerfHarness
{
    const string ScenePath = "Assets/_Project/Scenes/Arena.unity";

    [MenuItem("Sumo/效能量測（目前設定）")]
    public static void RunCurrent() { Run("current"); }

    /// <summary>對照組：關掉 STP 與 GPU Resident Drawer 再量。</summary>
    public static void RunBaseline()
    {
        U6Tools.DisableFeatures();
        Run("baseline");
    }

    /// <summary>實驗組：開啟 STP 與 GPU Resident Drawer 再量。</summary>
    public static void RunOptimized()
    {
        U6Tools.EnableFeatures();
        Run("optimized");
    }

    static void Run(string tag)
    {
        EditorSceneManager.OpenScene(ScenePath);
        EditorPrefs.SetBool(PerfSampler.FlagKey, true);
        EditorPrefs.SetString(PerfSampler.TagKey, tag);
        Debug.Log($"[PerfHarness] 進 Play 量測：{tag}");
        EditorApplication.EnterPlaymode();
    }
}
