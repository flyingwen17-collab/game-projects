using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;   // GetUniversalAdditionalCameraData 是擴充方法，少了這行會找不到

/// 批次模式下把賽道場景算圖存成 PNG，用來在不開編輯器 GUI 的情況下檢查畫面。
/// 走完整 URP 管線，所以後處理（ACES / Bloom / 分離色調）都會出現在截圖裡。
public static class SceneCapture
{
    const string ScenePath = "Assets/_Project/Scenes/RallyTrack.unity";

    /// 三個賽道各拍一張全景，用來快速檢查新賽道有沒有建壞
    [MenuItem("Tools/Drift Game/Capture All Tracks")]
    public static void CaptureAllTracks()
    {
        string outDir = ResolveOutDir();
        Directory.CreateDirectory(outDir);

        string[] scenes = { "RallyTrack", "Expressway", "CityStreet" };
        foreach (var sceneName in scenes)
        {
            string path = "Assets/_Project/Scenes/" + sceneName + ".unity";
            if (!File.Exists(path)) { Debug.LogWarning("[SceneCapture] 找不到場景 " + sceneName); continue; }
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            var car = GameObject.Find("Car_AE86") ?? GameObject.Find("Car_WRX");
            if (car == null) { Debug.LogWarning("[SceneCapture] " + sceneName + " 沒有車輛"); continue; }

            Shot(outDir, "track_" + sceneName + "_chase",
                 car.transform.position + car.transform.forward * -9f + Vector3.up * 3.4f,
                 car.transform.position + car.transform.forward * 18f + Vector3.up * 1f);

            Shot(outDir, "track_" + sceneName + "_vista",
                 car.transform.position + Vector3.up * 52f - car.transform.forward * 70f + car.transform.right * 30f,
                 car.transform.position + car.transform.forward * 80f);

            Debug.Log("[SceneCapture] " + sceneName + " 截圖完成");
        }
    }

    static string ResolveOutDir()
    {
        string outDir = System.Environment.GetEnvironmentVariable("DRIFT_SHOT_DIR");
        if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Application.dataPath, "../../_Refs/07_實機截圖");
        return outDir;
    }

    [MenuItem("Tools/Drift Game/Capture Screenshots")]
    public static void CaptureAll()
    {
        string outDir = System.Environment.GetEnvironmentVariable("DRIFT_SHOT_DIR");
        if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Application.dataPath, "../../_Refs/07_實機截圖");
        Directory.CreateDirectory(outDir);

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var car = GameObject.Find("Car_AE86") ?? GameObject.Find("Car_WRX");
        if (car == null) { Debug.LogError("[SceneCapture] 找不到車輛"); return; }

        // 三個機位：追尾、低角度側面、賽道全景
        Shot(outDir, "shot_chase",
             car.transform.position + car.transform.forward * -8.5f + Vector3.up * 3.2f,
             car.transform.position + Vector3.up * 0.9f);

        Shot(outDir, "shot_lowside",
             car.transform.position + car.transform.right * 7f + Vector3.up * 1.1f - car.transform.forward * 2f,
             car.transform.position + Vector3.up * 0.8f);

        Shot(outDir, "shot_vista",
             car.transform.position + Vector3.up * 34f - car.transform.forward * 46f + car.transform.right * 22f,
             car.transform.position + car.transform.forward * 60f);

        Debug.Log("[SceneCapture] 截圖完成 → " + Path.GetFullPath(outDir));
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 lookAt)
    {
        var camGo = new GameObject("CaptureCam");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 62f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 1200f;
        cam.allowHDR = true;
        camGo.transform.position = pos;
        camGo.transform.LookAt(lookAt);

        // 沿用主相機的 URP 設定（後處理、抗鋸齒）
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }

        int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
    }
}
