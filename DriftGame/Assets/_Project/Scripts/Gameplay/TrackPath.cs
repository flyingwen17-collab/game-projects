using System.Collections.Generic;
using UnityEngine;

/// 賽道路徑：由 RallySceneBuilder 產生的中心線取樣點。
/// NPC 用它來循跡、預判彎道曲率、決定進彎速度。
public class TrackPath : MonoBehaviour
{
    [SerializeField] List<Vector3> points = new List<Vector3>();
    public float roadWidth = 11f;

    public static TrackPath Instance { get; private set; }
    public int Count => points.Count;

    void Awake() { Instance = this; }

    public void SetPoints(List<Vector3> pts, float width)
    {
        points = new List<Vector3>(pts);
        roadWidth = width;
    }

    public Vector3 At(int i)
    {
        if (points.Count == 0) return Vector3.zero;
        return points[((i % points.Count) + points.Count) % points.Count];
    }

    /// 沿路徑的切線方向
    public Vector3 Tangent(int i)
    {
        if (points.Count < 2) return Vector3.forward;
        return (At(i + 1) - At(i - 1)).normalized;
    }

    /// 找出離某點最近的取樣索引。給 hint 可大幅縮小搜尋範圍（NPC 每幀都要查）。
    public int NearestIndex(Vector3 pos, int hint = -1, int window = 40)
    {
        if (points.Count == 0) return 0;

        int start = 0, end = points.Count;
        if (hint >= 0) { start = hint - window; end = hint + window; }

        int best = hint >= 0 ? hint : 0;
        float bestSqr = float.MaxValue;
        for (int k = start; k < end; k++)
        {
            int i = ((k % points.Count) + points.Count) % points.Count;
            float d = (points[i] - pos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = i; }
        }
        return best;
    }

    /// 前方 aheadCount 個取樣點處的彎道曲率（0=直線，越大彎越急）。
    /// NPC 靠這個決定要不要減速、要不要甩尾。
    public float CurvatureAhead(int index, int aheadCount)
    {
        Vector3 a = At(index);
        Vector3 b = At(index + aheadCount / 2);
        Vector3 c = At(index + aheadCount);

        Vector3 v1 = (b - a);
        Vector3 v2 = (c - b);
        if (v1.sqrMagnitude < 0.01f || v2.sqrMagnitude < 0.01f) return 0f;

        float angle = Vector3.Angle(v1.normalized, v2.normalized);
        return angle / 90f;   // 正規化成 0..~1
    }

    /// 路徑上的某點加上橫向偏移（讓 NPC 各跑各的路線，不會疊在一起）
    public Vector3 PointWithOffset(int index, float lateral)
    {
        Vector3 p = At(index);
        Vector3 tan = Tangent(index);
        Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
        return p + right * lateral;
    }
}
