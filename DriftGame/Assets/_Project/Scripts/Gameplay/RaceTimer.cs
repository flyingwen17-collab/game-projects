using UnityEngine;

/// 圈速計時：由 Checkpoint 依序觸發，跨過起點線完成一圈。
public class RaceTimer : MonoBehaviour
{
    public static RaceTimer Instance { get; private set; }

    public int checkpointCount = 4;

    public bool Running { get; private set; }
    public float CurrentLap { get; private set; }
    public float LastLap { get; private set; } = -1f;
    public float BestLap { get; private set; } = -1f;
    public int LapsDone { get; private set; }

    int expected;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (Running) CurrentLap += Time.deltaTime;
    }

    public void Checkpoint(int index)
    {
        if (!Running)
        {
            if (index == 0)
            {
                Running = true;
                CurrentLap = 0f;
                expected = 1 % checkpointCount;
            }
            return;
        }

        if (index != expected) return; // 走錯方向或抄捷徑不算

        if (index == 0)
        {
            LastLap = CurrentLap;
            if (BestLap < 0f || LastLap < BestLap) BestLap = LastLap;
            LapsDone++;
            CurrentLap = 0f;
            expected = 1 % checkpointCount;
        }
        else
        {
            expected = (expected + 1) % checkpointCount;
        }
    }

    public void ResetAll()
    {
        Running = false;
        CurrentLap = 0f;
        LastLap = -1f;
        BestLap = -1f;
        LapsDone = 0;
        expected = 0;
    }

    public static string Format(float t)
    {
        if (t < 0f) return "-:--.--";
        int m = (int)(t / 60f);
        float s = t - m * 60;
        return string.Format("{0}:{1:00.00}", m, s);
    }
}
