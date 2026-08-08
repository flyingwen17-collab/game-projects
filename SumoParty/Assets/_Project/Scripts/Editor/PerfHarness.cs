using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 流程 MD §5 關卡 3：打包成獨立執行檔再量效能。
///
/// 為什麼不在編輯器裡量：batch mode 的 Play 不真的算圖（實測 12 秒只跑 1 幀、
/// draw call 取不到），量出來的數字是假的。只有真的 player 有真的繪圖迴圈。
///
///   Unity.exe -batchmode -quit -projectPath X -executeMethod PerfHarness.BuildBaseline
///   Builds\baseline\SumoParty.exe -perftest baseline -screen-width 1280 -screen-height 720
///
/// 量測結果寫在執行檔旁邊的 perf_&lt;tag&gt;.txt，跑完自己結束。
/// </summary>
public static class PerfHarness
{
    const string ScenePath = "Assets/_Project/Scenes/Arena.unity";

    public static void BuildBaseline()
    {
        U6Tools.DisableFeatures();
        Build("baseline");
    }

    public static void BuildOptimized()
    {
        U6Tools.EnableFeatures();
        Build("optimized");
    }

    // Draw Calls 與 GC Alloc 的計數器只存在於 development build。
    // release build 量這兩項一定是「無資料」，不是程式寫錯。
    public static void BuildDevBaseline()
    {
        U6Tools.DisableFeatures();
        Build("dev_baseline", true);
    }

    public static void BuildDevOptimized()
    {
        U6Tools.EnableFeatures();
        Build("dev_optimized", true);
    }

    /// <summary>APV 開/關的 A/B。畫面驗證只有從 build 截圖才作數（見 PerfSampler 註解）。</summary>
    public static void BuildApvOff()
    {
        U6Tools.EnableFeatures();
        APVTools.SetLightProbeSystem(0);
        Build("apv_off");
    }

    public static void BuildApvOn()
    {
        U6Tools.EnableFeatures();
        APVTools.SetLightProbeSystem(1);
        Build("apv_on");
    }

    static void Build(string tag, bool development = false)
    {
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", tag);
        Directory.CreateDirectory(dir);

        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(dir, "SumoParty.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = development
                ? (BuildOptions.Development | BuildOptions.EnableDeepProfilingSupport)
                : BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log($"[PerfHarness] build [{tag}] {s.result}  大小 {s.totalSize / 1024 / 1024} MB  " +
                  $"耗時 {s.totalTime.TotalSeconds:0} 秒  → {opts.locationPathName}");

        if (s.result != BuildResult.Succeeded)
            Debug.LogError($"[PerfHarness] build 失敗：{s.totalErrors} errors");
    }
}
