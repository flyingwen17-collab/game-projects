using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 專案根目錄有 autoplay_once.flag 時：開啟灰盒場景並自動進 Play（用完即刪，只觸發一次）
[InitializeOnLoad]
static class AutoPlayOnce
{
    static AutoPlayOnce()
    {
        EditorApplication.delayCall += () =>
        {
            string flag = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "autoplay_once.flag");
            if (!File.Exists(flag) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(flag);
            string arena = "Assets/_Project/Scenes/Dohyo.unity";
            EditorSceneManager.OpenScene(File.Exists(arena) ? arena : "Assets/_Project/Scenes/Graybox.unity");
            EditorApplication.isPlaying = true;
        };
    }
}
