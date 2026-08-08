using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 打包可玩版本到 &lt;專案&gt;/Build/，供「!開始遊戲」捷徑使用（流程 MD §7.1）。
/// 用 EditorBuildSettings 裡已啟用的場景，不寫死場景路徑。
///
///   Unity.exe -batchmode -quit -projectPath X -executeMethod GameBuild.BuildPlayable
/// </summary>
public static class GameBuild
{
    [MenuItem("Build/打包可玩版本")]
    public static void BuildPlayable()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) { Debug.LogError("[GameBuild] EditorBuildSettings 裡沒有啟用的場景"); return; }

        string root = Directory.GetParent(Application.dataPath).FullName;
        string dir = Path.Combine(root, "Build");
        Directory.CreateDirectory(dir);

        string exeName = Path.GetFileName(root) + ".exe";
        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(dir, exeName),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        var s = BuildPipeline.BuildPlayer(opts).summary;
        Debug.Log($"[GameBuild] {s.result}  {s.totalSize / 1024 / 1024} MB  {s.totalTime.TotalSeconds:0}s  → {opts.locationPathName}");
        if (s.result != BuildResult.Succeeded) Debug.LogError($"[GameBuild] 失敗：{s.totalErrors} errors");
    }
}
