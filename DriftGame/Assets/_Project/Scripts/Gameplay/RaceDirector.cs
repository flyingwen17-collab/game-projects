using System.Collections.Generic;
using UnityEngine;

/// 比賽總監：起跑格、紅燈倒數、計圈、名次、完賽。
///
/// 設計重點：
///  - 每台參賽車（玩家 + Racer NPC）都有自己的計圈進度，用「依序通過檢查點」防抄捷徑，
///    名次由 圈數 + 路徑進度 即時排序。
///  - Checkpoint 統一回報到這裡；只有「玩家」的通過會轉發給 RaceTimer 計圈速。
///    （舊版任何 NPC 過線都會幫玩家完圈 —— 那是 bug。）
///  - 圈數與對手數量由賽前設定（static，跨場景保留）。
public class RaceDirector : MonoBehaviour
{
    public static RaceDirector Instance { get; private set; }

    // ---- 賽前設定（選單改這些，跨場景載入仍記得）----
    public static int LapsSetting = 3;
    public static int RacersSetting = 3;
    public static bool TrafficSetting = true;

    public enum Phase { Idle, Countdown, Racing, Finished }
    public Phase CurrentPhase { get; private set; } = Phase.Idle;

    [Header("音效")]
    public AudioClip beepCountClip;
    public AudioClip beepGoClip;

    /// 倒數步（3、2、1）
    public event System.Action<int> OnCountdown;
    /// 紅燈熄滅，起跑！
    public event System.Action OnGreen;
    /// 玩家完賽：名次、總時間
    public event System.Action<int, float> OnPlayerFinished;

    class Entry
    {
        public CarController car;
        public string label;
        public int laps;
        public int expectedCp;
        public int pathHint;
        public bool started;     // 是否已第一次壓過起點線（那一次是起算，不是完圈）
        public bool finished;
        public float finishTime;
        public float progress;   // laps*N + 路徑索引進度，排序用
    }

    readonly List<Entry> entries = new List<Entry>();
    Entry player;
    TrackPath path;
    int checkpointCount = 4;
    int startPathIndex;

    float countdownTimer;
    int countdownStep;
    float raceClock;
    AudioSource beepSrc;

    public int TotalLaps { get; private set; } = 3;
    public float RaceClock => raceClock;
    public int EntryCount => entries.Count;

    public int PlayerLap => player == null ? 1 : Mathf.Clamp(player.laps + 1, 1, Mathf.Max(TotalLaps, 1));
    public int PlayerPosition
    {
        get
        {
            if (player == null) return 1;
            int pos = 1;
            foreach (var e in entries)
                if (e != player && e.progress > player.progress) pos++;
            return pos;
        }
    }

    void Awake()
    {
        Instance = this;
        beepSrc = gameObject.AddComponent<AudioSource>();
        beepSrc.playOnAwake = false;
        beepSrc.spatialBlend = 0f;   // 倒數嗶聲是 UI 音，不做 3D 衰減
        beepSrc.volume = 0.8f;
    }

    /// 開賽：排起跑格 → 鎖操作 → 紅燈倒數。racerPool 給場上所有 NPC（含車流）。
    public void BeginRace(CarController playerCar, GameObject[] npcPool)
    {
        path = TrackPath.Instance;
        var timer = RaceTimer.Instance;
        if (timer != null) timer.ResetAll();

        TotalLaps = Mathf.Max(1, LapsSetting);
        entries.Clear();
        raceClock = 0f;

        player = new Entry { car = playerCar, label = "你" };
        entries.Add(player);

        // ---- 把 NPC 分成對手與車流：對手取前 N 台，車流依設定開關 ----
        int racersEnabled = 0;
        if (npcPool != null)
        {
            foreach (var go in npcPool)
            {
                if (go == null) continue;
                var ai = go.GetComponent<NPCDriver>();
                if (ai == null) continue;

                if (ai.style == NPCDriver.Style.Traffic)
                {
                    go.SetActive(TrafficSetting);
                }
                else
                {
                    bool use = racersEnabled < RacersSetting;
                    go.SetActive(use);
                    if (use)
                    {
                        racersEnabled++;
                        entries.Add(new Entry
                        {
                            car = go.GetComponent<CarController>(),
                            label = go.name.Replace("NPC_Racer_", "對手 "),
                        });
                    }
                }
            }
        }

        // ---- 起跑格：起點線後方，兩輛一排（玩家在最後排 —— 追上去才有比賽感）----
        if (path != null && path.Count > 0)
        {
            startPathIndex = FindStartIndex();
            int n = path.Count;
            // 取樣點平均間距（公尺）→ 每排 7m 要往回退幾個索引
            float avgStep = Mathf.Max(0.5f, PathLength() / n);
            int stepsPerRow = Mathf.Max(2, Mathf.RoundToInt(7f / avgStep));

            // 名次高的排前面：對手在前、玩家最後
            for (int i = 0; i < entries.Count; i++)
            {
                int slot = entries.Count - 1 - i;      // player(0) 拿最後一格
                int row = slot / 2;
                float lateral = (slot % 2 == 0 ? -1f : 1f) * 2.4f;
                int idx = ((startPathIndex - (row + 2) * stepsPerRow) % n + n) % n;

                Vector3 tan = path.Tangent(idx);
                Vector3 pos = path.PointWithOffset(idx, lateral) + Vector3.up * 0.5f;
                var e = entries[i];
                if (e.car != null)
                {
                    e.car.TeleportTo(pos, Quaternion.LookRotation(tan));
                    e.car.controlsLocked = true;
                    e.pathHint = idx;
                }
                e.laps = 0; e.expectedCp = 0; e.started = false; e.finished = false; e.progress = -row;
            }
        }

        // 倒數期間鎖「場上所有車」—— 只鎖參賽者的話，車流會繼續繞圈
        // 一頭撞進排好的起跑格（實測起跑格連環追撞就是這樣來的）
        SetAllControlsLocked(true);

        CurrentPhase = Phase.Countdown;
        countdownStep = 4;                 // 下一次 tick 變 3
        countdownTimer = 0.8f;             // 稍停一拍再開始數
    }

    void SetAllControlsLocked(bool locked)
    {
        foreach (var c in FindObjectsByType<CarController>())
            c.controlsLocked = locked;
    }

    /// 跳過倒數直接開跑（自動化測試用；正常遊玩不會呼叫）
    public void SkipCountdown()
    {
        if (CurrentPhase != Phase.Countdown) return;
        SetAllControlsLocked(false);
        CurrentPhase = Phase.Racing;
        OnGreen?.Invoke();
    }

    int FindStartIndex()
    {
        foreach (var cp in FindObjectsByType<Checkpoint>())
            if (cp.index == 0 && path != null)
                return path.NearestIndex(cp.transform.position);
        return 0;
    }

    float PathLength()
    {
        if (path == null || path.Count < 2) return 1f;
        float len = 0f;
        for (int i = 0; i < path.Count; i++)
            len += Vector3.Distance(path.At(i), path.At(i + 1));
        return len;
    }

    void Update()
    {
        if (CurrentPhase == Phase.Countdown)
        {
            countdownTimer -= Time.deltaTime;
            if (countdownTimer <= 0f)
            {
                countdownStep--;
                if (countdownStep > 0)
                {
                    // 3…2…1，紅燈逐顆亮
                    OnCountdown?.Invoke(countdownStep);
                    if (beepCountClip != null) beepSrc.PlayOneShot(beepCountClip);
                    countdownTimer = 1f;
                }
                else
                {
                    // 紅燈熄滅 → 起跑
                    SetAllControlsLocked(false);
                    CurrentPhase = Phase.Racing;
                    if (beepGoClip != null) beepSrc.PlayOneShot(beepGoClip);
                    OnGreen?.Invoke();
                }
            }
            return;
        }

        if (CurrentPhase != Phase.Racing && CurrentPhase != Phase.Finished) return;

        raceClock += Time.deltaTime;

        // ---- 即時進度（名次排序用）：圈數 + 路徑索引 ----
        if (path != null && path.Count > 0)
        {
            int n = path.Count;
            foreach (var e in entries)
            {
                if (e.car == null) continue;
                e.pathHint = path.NearestIndex(e.car.transform.position, e.pathHint);
                int rel = ((e.pathHint - startPathIndex) % n + n) % n;

                // 剛過起點線但檢查點還沒登記時 rel 會回到 0 附近 —— 用檢查點圈數為主，
                // rel 只做同圈內的細排序
                e.progress = e.laps * n + rel;
                if (e.finished) e.progress = TotalLaps * n + (float)n / Mathf.Max(1f, e.finishTime);
            }
        }
    }

    /// Checkpoint 統一回報進來（任何啟用中的車）
    public void PassCheckpoint(CarController car, int index)
    {
        if (CurrentPhase != Phase.Racing && CurrentPhase != Phase.Finished) return;

        Entry entry = null;
        foreach (var e in entries)
            if (e.car == car) { entry = e; break; }
        if (entry == null) return;   // 車流不參賽

        // 玩家的通過轉發給 RaceTimer（圈速表）
        if (entry == player && RaceTimer.Instance != null)
            RaceTimer.Instance.Checkpoint(index);

        // 依序通過才算（防抄捷徑/倒著跑）
        if (index != entry.expectedCp) return;
        entry.expectedCp = (entry.expectedCp + 1) % checkpointCount;

        if (index == 0)
        {
            if (!entry.started) { entry.started = true; return; }   // 第一次壓線 = 起算
            entry.laps++;
            if (!entry.finished && entry.laps >= TotalLaps)
            {
                entry.finished = true;
                entry.finishTime = raceClock;
                if (entry == player)
                {
                    CurrentPhase = Phase.Finished;
                    OnPlayerFinished?.Invoke(PlayerPosition, raceClock);
                }
            }
        }
    }
}
