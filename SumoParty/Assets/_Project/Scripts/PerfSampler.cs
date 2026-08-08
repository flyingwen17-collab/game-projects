using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// 流程 MD §5 關卡 3 的執行期取樣器。
/// 進 Play 後暖機數秒（避開著色器編譯與第一幀尖峰），再取樣固定秒數，
/// 把 FPS / draw call / 三角面 / 每幀 GC 配置寫成 log。
///
/// 由 PerfHarness（Editor）在 batch mode 觸發；沒有旗標時完全不啟動，
/// 所以正常開發按 Play 不會受影響。
/// </summary>
public class PerfSampler : MonoBehaviour
{
    public const string FlagKey = "SumoParty.PerfRun";
    public const string TagKey = "SumoParty.PerfTag";

    const float WarmupSeconds = 4f;    // 著色器變體 + GC 穩定
    const float SampleSeconds = 12f;

    string runTag = "run";
    float elapsed;
    bool sampling;

    readonly List<float> frameMs = new List<float>(2048);
    ProfilerRecorder drawCalls, setPass, triangles, verts, batches, gcAlloc;

    readonly List<long> drawCallSamples = new List<long>(2048);
    readonly List<long> setPassSamples = new List<long>(2048);
    readonly List<long> triangleSamples = new List<long>(2048);
    readonly List<long> gcSamples = new List<long>(2048);

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        if (!UnityEditor.EditorPrefs.GetBool(FlagKey, false)) return;
        var go = new GameObject("~PerfSampler");
        DontDestroyOnLoad(go);
        var s = go.AddComponent<PerfSampler>();
        s.runTag = UnityEditor.EditorPrefs.GetString(TagKey, "run");
    }
#endif

    void OnEnable()
    {
        drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
        batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
    }

    void OnDisable()
    {
        drawCalls.Dispose(); setPass.Dispose(); triangles.Dispose();
        verts.Dispose(); batches.Dispose(); gcAlloc.Dispose();
    }

    void Update()
    {
        elapsed += Time.unscaledDeltaTime;

        if (!sampling)
        {
            if (elapsed < WarmupSeconds) return;
            sampling = true;
            elapsed = 0f;
            return;
        }

        frameMs.Add(Time.unscaledDeltaTime * 1000f);
        if (drawCalls.Valid) drawCallSamples.Add(drawCalls.LastValue);
        if (setPass.Valid) setPassSamples.Add(setPass.LastValue);
        if (triangles.Valid) triangleSamples.Add(triangles.LastValue);
        if (gcAlloc.Valid) gcSamples.Add(gcAlloc.LastValue);

        if (elapsed >= SampleSeconds) Finish();
    }

    void Finish()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PERF [" + runTag + "] ===");
        sb.AppendLine("解析度 " + Screen.width + "x" + Screen.height +
                      "  取樣 " + frameMs.Count + " 幀 / " + SampleSeconds + "s（暖機 " + WarmupSeconds + "s）");

        frameMs.Sort();
        if (frameMs.Count > 0)
        {
            float sum = 0f;
            foreach (var f in frameMs) sum += f;
            float avg = sum / frameMs.Count;
            float p99 = frameMs[Mathf.Min(frameMs.Count - 1, Mathf.FloorToInt(frameMs.Count * 0.99f))];
            float median = frameMs[frameMs.Count / 2];
            sb.AppendLine(Fmt("平均 frame time", avg, "ms") + "  → 平均 FPS " + (1000f / avg).ToString("0.0"));
            sb.AppendLine(Fmt("中位數 frame time", median, "ms"));
            sb.AppendLine(Fmt("1% low frame time", p99, "ms") + "  → 1% low FPS " + (1000f / p99).ToString("0.0"));
        }

        sb.AppendLine(Avg("Draw Calls", drawCallSamples));
        sb.AppendLine(Avg("SetPass Calls", setPassSamples));
        sb.AppendLine(Avg("Triangles", triangleSamples));
        sb.AppendLine(Avg("GC Alloc / frame (bytes)", gcSamples) + "   ← 目標 0");

        string outDir = Path.Combine(Application.dataPath, "../Screenshots");
        Directory.CreateDirectory(outDir);
        string file = Path.Combine(outDir, "perf_" + runTag + ".txt");
        File.WriteAllText(file, sb.ToString());
        Debug.Log(sb.ToString());

#if UNITY_EDITOR
        UnityEditor.EditorPrefs.SetBool(FlagKey, false);
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    static string Fmt(string label, float v, string unit)
    {
        return "  " + label.PadRight(24) + v.ToString("0.00", CultureInfo.InvariantCulture) + " " + unit;
    }

    static string Avg(string label, List<long> data)
    {
        if (data.Count == 0) return "  " + label.PadRight(24) + "(無資料)";
        long sum = 0, max = long.MinValue;
        foreach (var v in data) { sum += v; if (v > max) max = v; }
        return "  " + label.PadRight(24) + "平均 " + (sum / data.Count) + "   峰值 " + max;
    }
}
