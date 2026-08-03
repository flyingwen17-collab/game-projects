using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 專案根目錄有 autoplay_once.flag 時：自動開 M0 場景並進入 Play（一次性）
[InitializeOnLoad]
public static class AutoPlayOnce
{
    const string ScenePath = "Assets/_Project/Scenes/M0_Prototype.unity";

    static AutoPlayOnce()
    {
        string flag = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "autoplay_once.flag");
        if (!File.Exists(flag)) return;

        EditorApplication.delayCall += () =>
        {
            try { File.Delete(flag); } catch { }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);

            EditorApplication.EnterPlaymode();
        };
    }
}
