using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 觀眾席視覺歡呼（企劃書 §4）。跟音訊同一個訊號源：SumoMatch.Intensity。
/// 安靜時輕微晃動，激烈時整片跳動；決着瞬間全場彈起。
/// 一個管理器驅動所有看板，不給每個觀眾掛腳本（行動裝置省 Update 開銷）。
/// </summary>
public class CrowdWave : MonoBehaviour
{
    public SumoMatch match;

    readonly List<Transform> seats = new List<Transform>();
    readonly List<float> phases = new List<float>();
    readonly List<float> baseY = new List<float>();
    float burst;   // 決着爆發（衰減）

    public void Register(Transform seat)
    {
        seats.Add(seat);
        phases.Add(Random.Range(0f, Mathf.PI * 2f));
        baseY.Add(seat.localPosition.y);
    }

    void Start()
    {
        if (match != null) match.OnBoutEnd += (w, l, r) => burst = 1f;
    }

    void Update()
    {
        if (match == null) return;
        float k = match.Intensity;
        burst = Mathf.Max(0f, burst - Time.deltaTime * 0.55f);

        float amp = 0.015f + 0.10f * k + 0.16f * burst;   // 激烈度 → 跳動幅度
        float freq = 5f + 5f * k + 4f * burst;
        float t = Time.time;

        for (int i = 0; i < seats.Count; i++)
        {
            float bounce = Mathf.Abs(Mathf.Sin(t * freq + phases[i]));
            var lp = seats[i].localPosition;
            lp.y = baseY[i] + bounce * amp;
            seats[i].localPosition = lp;
        }
    }
}
