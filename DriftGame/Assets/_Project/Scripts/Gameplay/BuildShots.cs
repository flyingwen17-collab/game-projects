using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// 打包版驗收截圖：由環境變數 DRIFT_BUILDSHOTS 觸發，
/// 依序載入三個賽道，各等畫面穩定後截一張圖，全部拍完自動退出。
///
/// 存在的理由（流程 MD §5 關卡 2）：編輯器離線算圖不套後處理、shader variant
/// 也可能沒編譯（會拍出洋紅假影），光照與材質的最終判定只能看打包後的畫面。
public class BuildShots : MonoBehaviour
{
    static readonly string[] Scenes = { "RallyTrack", "Expressway", "CityStreet" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string dir = System.Environment.GetEnvironmentVariable("DRIFT_BUILDSHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        var go = new GameObject("BuildShots");
        DontDestroyOnLoad(go);
        go.AddComponent<BuildShots>().outDir = dir;
    }

    string outDir;

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        Directory.CreateDirectory(outDir);

        foreach (var name in Scenes)
        {
            var load = SceneManager.LoadSceneAsync(name);
            while (!load.isDone) yield return null;

            // 選一台車開賽，畫面才有主角與追尾鏡頭（含起跑燈畫面）
            var gm = FindAnyObjectByType<GameManager>();
            if (gm != null) gm.StartRace(0);

            // 等 shader 編譯、光照與粒子穩定
            yield return new WaitForSeconds(2.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "build_" + name + ".png"));
            yield return new WaitForSeconds(0.8f);
        }

        Application.Quit(0);
    }
}
