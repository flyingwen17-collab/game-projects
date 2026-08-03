using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 在批次模式下進入 Play 模式跑 DriveSelfTest。
/// 注意：呼叫此方法時 Unity 不可加 -quit，否則方法一返回就關閉，來不及進 Play。
/// 測試跑完後由 DriveSelfTest 自己呼叫 EditorApplication.Exit 結束。
public static class RunPlayTest
{
    const string ScenePath = "Assets/_Project/Scenes/RallyTrack.unity";

    [MenuItem("Tools/Drift Game/Run Drive Self Test")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Debug.Log("[RunPlayTest] 進入 Play 模式執行自我測試…");
        EditorApplication.EnterPlaymode();
    }
}
