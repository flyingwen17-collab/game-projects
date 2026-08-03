using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 專案根目錄有 autoplay_once.flag 時：自動開 M0 場景並進入 Play（一次性）
/// 用 EditorApplication.update 輪詢，比 delayCall 可靠（啟動early階段 delayCall 可能被清掉）
[InitializeOnLoad]
public static class AutoPlayOnce
{
    const string ScenePath = "Assets/_Project/Scenes/M0_Prototype.unity";
    static readonly string FlagPath =
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, "autoplay_once.flag");
    static double startTime;

    static AutoPlayOnce()
    {
        if (!File.Exists(FlagPath)) return;
        startTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += TryPlay;
    }

    static void TryPlay()
    {
        // 等編輯器完全就緒（沒在編譯/更新資產）再動作
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.timeSinceStartup - startTime < 1.0) return; // 稍等啟動塵埃落定

        EditorApplication.update -= TryPlay;

        try { File.Delete(FlagPath); } catch { }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath);

        Debug.Log("AutoPlayOnce：自動進入 Play 模式");
        EditorApplication.EnterPlaymode();
    }
}
