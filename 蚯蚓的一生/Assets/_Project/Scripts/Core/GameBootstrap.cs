using UnityEngine;
using UnityEngine.SceneManagement;

/// 遊戲啟動器：進 Play 後自動掛上所有新系統（不需改場景檔）
public static class GameBootstrap
{
    static GameObject systems;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (systems != null) return;
        systems = new GameObject("GameSystems");
        Object.DontDestroyOnLoad(systems);
        systems.AddComponent<MusicSystem>();
        SceneManager.sceneLoaded += (s, m) => SetupScene();
        SetupScene();
    }

    static void SetupScene()
    {
        if (GameObject.Find("GameManager") == null) return; // 不是遊戲場景

        var fx = new GameObject("SceneFx");
        fx.AddComponent<EnvironmentDecorator>();

        var worm = GameObject.FindGameObjectWithTag("Player");
        if (worm != null)
        {
            if (worm.GetComponent<WormBody>() == null) worm.AddComponent<WormBody>();
            if (worm.GetComponent<WormSkills>() == null) worm.AddComponent<WormSkills>();
        }
    }
}
