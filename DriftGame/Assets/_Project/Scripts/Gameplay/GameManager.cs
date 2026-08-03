using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// 遊戲流程：選車畫面、HUD（時速/圈時/甩尾分數與連段）、完圈結算、成績存檔。
/// UI 全部在執行時用程式建立，場景端只需指定車輛與攝影機。
public class GameManager : MonoBehaviour
{
    public CarController[] cars;
    public string[] carNames = { "SUBARU WRX", "HONDA FIT", "TOYOTA 86" };
    public string[] carDescs = { "AWD・四驅穩定高速", "FWD・靈活小鋼砲", "RWD・甩尾之魂" };
    public CameraFollow cameraFollow;

    [Header("結算")]
    public float resultAutoHideSeconds = 7f;

    CarController current;
    int currentIndex = -1;
    DriftDetector currentDrift;
    DriftScoring currentScoring;

    Font font;
    Canvas canvas;
    GameObject selectPanel;
    Text speedText, timeText, lastText, bestText, driftText, hintText;
    Text scoreText, comboText, totalText, flashText;

    GameObject resultPanel;
    Text resultTitle, resultBody;
    float resultTimer;
    float flashTimer;

    void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        foreach (var c in cars) if (c != null) c.enabled = false;
        BuildUI();
        ShowSelect(true);

        if (RaceTimer.Instance != null)
            RaceTimer.Instance.OnLapCompleted += HandleLapCompleted;
    }

    void OnDestroy()
    {
        if (RaceTimer.Instance != null)
            RaceTimer.Instance.OnLapCompleted -= HandleLapCompleted;
        UnsubscribeScoring();
    }

    // ---------------- UI 建立 ----------------

    void BuildUI()
    {
        var canvasGo = new GameObject("HUD Canvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // 時速（右下）
        speedText = MakeText("Speed", new Vector2(1f, 0f), new Vector2(-60f, 60f), 76, TextAnchor.LowerRight);
        speedText.text = "0 km/h";

        // 圈時（上中）
        timeText = MakeText("Time", new Vector2(0.5f, 1f), new Vector2(0f, -50f), 44, TextAnchor.UpperCenter);
        lastText = MakeText("Last", new Vector2(0.5f, 1f), new Vector2(0f, -100f), 26, TextAnchor.UpperCenter);
        bestText = MakeText("Best", new Vector2(0.5f, 1f), new Vector2(0f, -132f), 26, TextAnchor.UpperCenter);
        bestText.color = new Color(1f, 0.85f, 0.3f);

        // 總分（右上）
        totalText = MakeText("Total", new Vector2(1f, 1f), new Vector2(-60f, -50f), 46, TextAnchor.UpperRight);
        totalText.color = new Color(1f, 0.93f, 0.6f);
        totalText.text = "0";

        // 甩尾中的即時分數與倍率（中下）
        scoreText = MakeText("Score", new Vector2(0.5f, 0f), new Vector2(0f, 250f), 64, TextAnchor.LowerCenter);
        scoreText.color = new Color(1f, 0.82f, 0.25f);
        scoreText.text = "";

        comboText = MakeText("Combo", new Vector2(0.5f, 0f), new Vector2(0f, 210f), 38, TextAnchor.LowerCenter);
        comboText.color = new Color(1f, 0.55f, 0.15f);
        comboText.text = "";

        // 甩尾角度提示（中下，最底）
        driftText = MakeText("Drift", new Vector2(0.5f, 0f), new Vector2(0f, 170f), 44, TextAnchor.LowerCenter);
        driftText.color = new Color(1f, 0.5f, 0.1f);
        driftText.text = "";

        // 入帳 / 撞車的短暫提示（畫面中央偏上）
        flashText = MakeText("Flash", new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), 54, TextAnchor.MiddleCenter);
        flashText.text = "";

        // 操作提示（左下）
        hintText = MakeText("Hint", new Vector2(0f, 0f), new Vector2(30f, 30f), 22, TextAnchor.LowerLeft);
        hintText.text = "WASD 駕駛   Space 手煞車甩尾   R 重置   1/2/3 換車   Esc 選單";
        hintText.color = new Color(1f, 1f, 1f, 0.65f);

        BuildSelectPanel();
        BuildResultPanel();
    }

    Text MakeText(string name, Vector2 anchor, Vector2 offset, int size, TextAnchor align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(canvas.transform, false);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = align;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = offset;
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    void BuildSelectPanel()
    {
        selectPanel = new GameObject("SelectPanel");
        selectPanel.transform.SetParent(canvas.transform, false);
        var bg = selectPanel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);
        var rt = bg.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var title = MakeText("Title", new Vector2(0.5f, 1f), new Vector2(0f, -180f), 64, TextAnchor.UpperCenter);
        title.transform.SetParent(selectPanel.transform, false);
        title.text = "選擇你的拉力車";

        Color[] btnColors =
        {
            new Color(0.10f, 0.25f, 0.75f), // WRX 藍
            new Color(0.55f, 0.57f, 0.60f), // FIT 銀
            new Color(0.92f, 0.92f, 0.92f), // 86 白
        };

        for (int i = 0; i < cars.Length; i++)
        {
            int idx = i;
            var btnGo = new GameObject("CarBtn" + i);
            btnGo.transform.SetParent(selectPanel.transform, false);
            var img = btnGo.AddComponent<Image>();
            img.color = btnColors[i % btnColors.Length];
            var brt = img.rectTransform;
            brt.sizeDelta = new Vector2(360f, 220f);
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2((i - 1) * 420f, -40f);
            var btn = btnGo.AddComponent<Button>();
            btn.onClick.AddListener(() => StartRace(idx));

            var label = MakeText("Label", new Vector2(0.5f, 0.5f), new Vector2(0f, 46f), 34, TextAnchor.MiddleCenter);
            label.transform.SetParent(btnGo.transform, false);
            label.text = carNames[i];
            label.color = i == 2 ? Color.black : Color.white;

            var desc = MakeText("Desc", new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), 22, TextAnchor.MiddleCenter);
            desc.transform.SetParent(btnGo.transform, false);
            desc.text = carDescs[i] + "\n（按 " + (i + 1) + "）";
            desc.color = i == 2 ? new Color(0.2f, 0.2f, 0.2f) : new Color(1f, 1f, 1f, 0.85f);

            // 該車的歷史最佳，直接顯示在選車卡片上
            var rec = SaveSystem.Data.For(i);
            var best = MakeText("Rec", new Vector2(0.5f, 0.5f), new Vector2(0f, -76f), 19, TextAnchor.MiddleCenter);
            best.transform.SetParent(btnGo.transform, false);
            best.text = rec.laps > 0
                ? "最速 " + RaceTimer.Format(rec.bestLap) + "   最高分 " + Mathf.RoundToInt(rec.bestScore)
                : "尚無紀錄";
            best.color = i == 2 ? new Color(0.35f, 0.3f, 0.1f) : new Color(1f, 0.9f, 0.55f, 0.9f);
        }
    }

    void BuildResultPanel()
    {
        resultPanel = new GameObject("ResultPanel");
        resultPanel.transform.SetParent(canvas.transform, false);
        var bg = resultPanel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.78f);
        var rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(760f, 460f);
        rt.anchoredPosition = Vector2.zero;

        resultTitle = MakeText("ResultTitle", new Vector2(0.5f, 1f), new Vector2(0f, -46f), 54, TextAnchor.UpperCenter);
        resultTitle.transform.SetParent(resultPanel.transform, false);
        resultTitle.text = "完成一圈";

        resultBody = MakeText("ResultBody", new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), 32, TextAnchor.MiddleCenter);
        resultBody.transform.SetParent(resultPanel.transform, false);
        resultBody.text = "";

        var tip = MakeText("ResultTip", new Vector2(0.5f, 0f), new Vector2(0f, 30f), 22, TextAnchor.LowerCenter);
        tip.transform.SetParent(resultPanel.transform, false);
        tip.text = "按 Space 關閉";
        tip.color = new Color(1f, 1f, 1f, 0.6f);

        resultPanel.SetActive(false);
    }

    void ShowSelect(bool show)
    {
        if (selectPanel != null) selectPanel.SetActive(show);
    }

    // ---------------- 流程 ----------------

    public void StartRace(int carIndex)
    {
        if (carIndex < 0 || carIndex >= cars.Length || cars[carIndex] == null) return;

        foreach (var c in cars)
        {
            if (c == null) continue;
            c.RespawnAtStart();
            c.enabled = false;
        }

        UnsubscribeScoring();

        current = cars[carIndex];
        currentIndex = carIndex;
        current.enabled = true;
        currentDrift = current.GetComponent<DriftDetector>();
        currentScoring = current.GetComponent<DriftScoring>();

        if (currentScoring != null)
        {
            currentScoring.ResetAll();
            currentScoring.OnBanked += HandleBanked;
            currentScoring.OnCrash += HandleCrash;
            currentScoring.OnComboUp += HandleComboUp;
        }

        if (cameraFollow != null) cameraFollow.SetTarget(current.transform);
        if (RaceTimer.Instance != null) RaceTimer.Instance.ResetAll();

        resultPanel.SetActive(false);
        flashText.text = "";
        ShowSelect(false);
    }

    void UnsubscribeScoring()
    {
        if (currentScoring == null) return;
        currentScoring.OnBanked -= HandleBanked;
        currentScoring.OnCrash -= HandleCrash;
        currentScoring.OnComboUp -= HandleComboUp;
    }

    void HandleBanked(float earned)
    {
        Flash("+" + Mathf.RoundToInt(earned), new Color(0.5f, 1f, 0.5f));
    }

    void HandleCrash(float lost)
    {
        Flash("撞車！ -" + Mathf.RoundToInt(lost), new Color(1f, 0.35f, 0.3f));
    }

    void HandleComboUp(float multiplier)
    {
        Flash("COMBO x" + multiplier.ToString("0.0"), new Color(1f, 0.75f, 0.2f));
    }

    void Flash(string msg, Color c)
    {
        flashText.text = msg;
        flashText.color = c;
        flashTimer = 1.2f;
    }

    void HandleLapCompleted(float lapTime)
    {
        float score = currentScoring != null ? currentScoring.TotalScore + currentScoring.PendingScore : 0f;
        float combo = currentScoring != null ? currentScoring.BestCombo : 0f;
        float longest = currentScoring != null ? currentScoring.LongestDriftTime : 0f;

        SaveSystem.Submit(currentIndex, lapTime, score, combo,
                          out bool newLap, out bool newScore, out bool newCombo);

        string carName = currentIndex >= 0 && currentIndex < carNames.Length ? carNames[currentIndex] : "";
        resultTitle.text = carName + "　完成第 " + (RaceTimer.Instance != null ? RaceTimer.Instance.LapsDone : 1) + " 圈";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("圈速　　" + RaceTimer.Format(lapTime) + (newLap ? "　★新紀錄" : ""));
        sb.AppendLine("甩尾總分　" + Mathf.RoundToInt(score) + (newScore ? "　★新紀錄" : ""));
        sb.AppendLine("最高連段　x" + combo.ToString("0.0") + (newCombo ? "　★新紀錄" : ""));
        sb.Append("最長甩尾　" + longest.ToString("0.0") + " 秒");
        resultBody.text = sb.ToString();

        resultPanel.SetActive(true);
        resultTimer = resultAutoHideSeconds;
    }

    // ---------------- 每幀更新 ----------------

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) StartRace(0);
            if (kb.digit2Key.wasPressedThisFrame) StartRace(1);
            if (kb.digit3Key.wasPressedThisFrame) StartRace(2);
            if (kb.escapeKey.wasPressedThisFrame) ShowSelect(!selectPanel.activeSelf);
            if (kb.spaceKey.wasPressedThisFrame && resultPanel.activeSelf) resultPanel.SetActive(false);
        }

        if (resultPanel.activeSelf)
        {
            resultTimer -= Time.deltaTime;
            if (resultTimer <= 0f) resultPanel.SetActive(false);
        }

        if (flashTimer > 0f)
        {
            flashTimer -= Time.deltaTime;
            var c = flashText.color;
            c.a = Mathf.Clamp01(flashTimer / 0.5f);
            flashText.color = c;
            if (flashTimer <= 0f) flashText.text = "";
        }

        if (current == null) return;

        speedText.text = Mathf.RoundToInt(current.SpeedKmh) + " km/h";

        var timer = RaceTimer.Instance;
        if (timer != null)
        {
            timeText.text = "TIME  " + RaceTimer.Format(timer.Running ? timer.CurrentLap : -1f);
            lastText.text = "LAST  " + RaceTimer.Format(timer.LastLap);
            bestText.text = "BEST  " + RaceTimer.Format(timer.BestLap);
        }

        UpdateScoreHud();
    }

    void UpdateScoreHud()
    {
        if (currentScoring == null) return;

        totalText.text = Mathf.RoundToInt(currentScoring.TotalScore).ToString("N0");

        bool building = currentScoring.PendingScore > 0f;
        if (building)
        {
            scoreText.text = "+" + Mathf.RoundToInt(currentScoring.PendingScore * currentScoring.Multiplier);
            comboText.text = currentScoring.Multiplier > 1f
                ? "x" + currentScoring.Multiplier.ToString("0.0") + (currentScoring.NearWall ? "　貼牆！" : "")
                : (currentScoring.NearWall ? "貼牆加分！" : "");
        }
        else
        {
            scoreText.text = "";
            comboText.text = "";
        }

        if (currentDrift != null && currentDrift.IsDrifting)
        {
            driftText.text = "DRIFT  " + Mathf.Abs(currentDrift.SlipAngle).ToString("F0") + "°";
            float pulse = 1f + Mathf.Sin(Time.time * 10f) * 0.06f;
            driftText.transform.localScale = Vector3.one * pulse;
        }
        else
        {
            driftText.text = "";
            driftText.transform.localScale = Vector3.one;
        }
    }
}
