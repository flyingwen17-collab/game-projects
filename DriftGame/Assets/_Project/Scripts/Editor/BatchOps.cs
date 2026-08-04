using UnityEditor;
using UnityEngine;

/// batch mode 專用入口：一次重建全部賽道並截圖，給
/// Unity.exe -batchmode -executeMethod BatchOps.RebuildAllAndCapture 呼叫。
public static class BatchOps
{
    public static void RebuildAllAndCapture()
    {
        Debug.Log("[BatchOps] 開始重建全部賽道");
        RallySceneBuilder.Build();
        TrackScenes.BuildAll();
        SceneCapture.CaptureAllTracks();
        Debug.Log("[BatchOps] 全部完成");
    }
}
