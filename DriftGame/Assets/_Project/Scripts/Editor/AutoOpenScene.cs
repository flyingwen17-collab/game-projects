using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// 編輯器啟動時若停在無標題空場景，自動打開練習場景。
public static class AutoOpenScene
{
    const string RallyPath = "Assets/_Project/Scenes/RallyTrack.unity";
    const string ScenePath = "Assets/_Project/Scenes/PracticeGround.unity";
    const string SessionKey = "DriftGame.AutoOpenedScene";

    [InitializeOnLoadMethod]
    static void OpenPracticeSceneIfUntitled()
    {
        EditorApplication.delayCall += () =>
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);

            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            string target = System.IO.File.Exists(RallyPath) ? RallyPath : ScenePath;
            if (!System.IO.File.Exists(target)) return;

            // 已經在目標場景（或使用者自己開了別的場景並有未存變更）就不動
            Scene active = SceneManager.GetActiveScene();
            if (active.path == target || active.isDirty) return;

            EditorSceneManager.OpenScene(target, OpenSceneMode.Single);
        };
    }
}
