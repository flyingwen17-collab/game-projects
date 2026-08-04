using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 在批次模式下進入 Play 模式跑 DriveSelfTest。
/// 注意：呼叫此方法時 Unity 不可加 -quit，否則方法一返回就關閉，來不及進 Play。
/// 測試跑完後由 DriveSelfTest 自己呼叫 EditorApplication.Exit 結束。
public static class RunPlayTest
{
    const string DefaultScene = "RallyTrack";

    [MenuItem("Tools/Drift Game/Run Drive Self Test")]
    public static void Run()
    {
        // 用環境變數 DRIFT_TEST_SCENE 指定要測哪個賽道，未指定就測拉力賽道
        string sceneName = System.Environment.GetEnvironmentVariable("DRIFT_TEST_SCENE");
        if (string.IsNullOrEmpty(sceneName)) sceneName = DefaultScene;

        string path = "Assets/_Project/Scenes/" + sceneName + ".unity";
        if (!System.IO.File.Exists(path))
        {
            Debug.LogError("[RunPlayTest] 找不到場景：" + path);
            EditorApplication.Exit(3);
            return;
        }

        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        Debug.Log("[RunPlayTest] 進入 Play 模式執行自我測試（場景：" + sceneName + "）…");
        EditorApplication.EnterPlaymode();
    }
}
