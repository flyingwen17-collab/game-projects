using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// 遊戲流程：選車與賽事設定、紅燈倒數、HUD（時速/圈數/名次/甩尾分數）、完賽結算、成績存檔。
/// UI 全部在執行時用程式建立，場景端只需指定車輛與攝影機。
public class GameManager : MonoBehaviour
{
    public CarController[] cars;
    public string[] carNames = { "SUBARU WRX", "HONDA FIT", "TOYOTA 86" };
    public string[] carDescs = { "AWD・四驅穩定高速", "FWD・靈活小鋼砲", "RWD・甩尾之魂" };
    public CameraFollow cameraFollow;

    [Header("NPC")]
    public GameObject[] npcs = new GameObject[0];

    [Header("比賽")]
    public RaceDirector director;

    CarController current;
    int currentIndex = -1;
    DriftDetector currentDrift;
    DriftScoring currentScoring;

    Font font;
    Canvas canvas;
    GameObject selectPanel;
    Text speedText, timeText, lastText, bestText, driftText, hintText;
    Text scoreText, comboText, totalText, flashText, gearText, rpmText;
    Text lapText, posText;

    // 起跑燈
    GameObject lightsPanel;
    Image[] lightDots;
    Text countdownText;
    float goFadeTimer;

    // 賽事設定按鈕（高亮顯示目前選擇）
    readonly int[] lapChoices = { 1, 3, 5 };
    readonly int[] racerChoices = { 0, 3, 5 };
    Image[] lapBtns, racerBtns;
    Image trafficBtn;
    Text trafficBtnLabel;

    GameObject resultPanel;
    Text resultTitle, resultBody;
    float resultTimer;
    float flashTimer;

    void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        foreach (var c in cars) if (c != null) c.enabled = false;
        if (director == null) director = FindAnyObjectByType<RaceDirector>();
        BuildUI();
        ShowSelect(true);

        if (RaceTimer.Instance != null)
            RaceTimer.Instance.OnLapCompleted += HandleLapCompleted;

        if (director != null)
        {
            director.OnCountdown += HandleCountdown;
            director.OnGreen += HandleGreen;
            director.OnPlayerFinished += HandlePlayerFinished;
        }
    }

    void OnDestroy()
    {
        if (RaceTimer.Instance != null)
            RaceTimer.Instance.OnLapCompleted -= HandleLapCompleted;
        if (director != null)
        {
            director.OnCountdown -= HandleCountdown;
            director.OnGreen -= HandleGreen;
            director.OnPlayerFinished -= HandlePlayerFinished;
        }
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

        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // 時速（右下）
        speedText = MakeText("Speed", new Vector2(1f, 0f), new Vector2(-60f, 60f), 76, TextAnchor.LowerRight);
        speedText.text = "0 km/h";

        // 檔位（時速左邊的大字）與轉速
        gearText = MakeText("Gear", new Vector2(1f, 0f), new Vector2(-330f, 52f), 92, TextAnchor.LowerRight);
        gearText.text = "1";
        rpmText = MakeText("Rpm", new Vector2(1f, 0f), new Vector2(-60f, 20f), 24, TextAnchor.LowerRight);
        rpmText.color = new Color(1f, 1f, 1f, 0.8f);
        rpmText.text = "";

        // 圈數與名次（左上）
        lapText = MakeText("Lap", new Vector2(0f, 1f), new Vector2(40f, -46f), 52, TextAnchor.UpperLeft);
        lapText.text = "";
        posText = MakeText("Pos", new Vector2(0f, 1f), new Vector2(40f, -112f), 64, TextAnchor.UpperLeft);
        posText.color = new Color(1f, 0.85f, 0.3f);
        posText.text = "";

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
        hintText.text = "WASD 駕駛（停住後長按 S 進倒檔）   Space 手煞車   Q 升檔 / E 降檔   T 自排手排\n"
                      + "L 車燈   N 時段（正午→黃昏→夜晚）   M 車流開關   R 重置   Esc 選單\n"
                      + "F1 山道   F2 日本高速公路   F3 台北街道";
        hintText.color = new Color(1f, 1f, 1f, 0.65f);

        BuildStartLights();
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

    /// F1 式起跑燈：三顆紅燈逐顆亮 → 全滅（=起跑）＋ GO! 字樣
    void BuildStartLights()
    {
        lightsPanel = new GameObject("StartLights");
        lightsPanel.transform.SetParent(canvas.transform, false);
        var bg = lightsPanel.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.03f, 0.04f, 0.85f);
        var rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(340f, 120f);
        rt.anchoredPosition = new Vector2(0f, -190f);

        lightDots = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            var dot = new GameObject("Dot" + i);
            dot.transform.SetParent(lightsPanel.transform, false);
            var img = dot.AddComponent<Image>();
            img.color = new Color(0.25f, 0.05f, 0.05f);   // 熄滅的暗紅
            var drt = img.rectTransform;
            drt.sizeDelta = new Vector2(80f, 80f);
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = new Vector2((i - 1) * 105f, 0f);
            lightDots[i] = img;
        }

        countdownText = MakeText("Countdown", new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), 150, TextAnchor.MiddleCenter);
        countdownText.text = "";

        lightsPanel.SetActive(false);
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

        var title = MakeText("Title", new Vector2(0.5f, 1f), new Vector2(0f, -120f), 60, TextAnchor.UpperCenter);
        title.transform.SetParent(selectPanel.transform, false);
        title.text = "選擇你的賽車";

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
            brt.anchoredPosition = new Vector2((i - 1) * 420f, 40f);
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

        BuildRaceSettings();
    }

    /// 賽事設定列：圈數 / 對手數 / 車流
    void BuildRaceSettings()
    {
        var rowTitle = MakeText("SetTitle", new Vector2(0.5f, 0.5f), new Vector2(-520f, -180f), 30, TextAnchor.MiddleCenter);
        rowTitle.transform.SetParent(selectPanel.transform, false);
        rowTitle.text = "圈數";

        lapBtns = new Image[lapChoices.Length];
        for (int i = 0; i < lapChoices.Length; i++)
        {
            int v = lapChoices[i];
            lapBtns[i] = MakeSettingButton(v.ToString(), new Vector2(-400f + i * 90f, -180f),
                () => { RaceDirector.LapsSetting = v; RefreshSettingButtons(); });
        }

        var rTitle = MakeText("SetTitle2", new Vector2(0.5f, 0.5f), new Vector2(-80f, -180f), 30, TextAnchor.MiddleCenter);
        rTitle.transform.SetParent(selectPanel.transform, false);
        rTitle.text = "對手";

        racerBtns = new Image[racerChoices.Length];
        for (int i = 0; i < racerChoices.Length; i++)
        {
            int v = racerChoices[i];
            racerBtns[i] = MakeSettingButton(v.ToString(), new Vector2(40f + i * 90f, -180f),
                () => { RaceDirector.RacersSetting = v; RefreshSettingButtons(); });
        }

        var tTitle = MakeText("SetTitle3", new Vector2(0.5f, 0.5f), new Vector2(330f, -180f), 30, TextAnchor.MiddleCenter);
        tTitle.transform.SetParent(selectPanel.transform, false);
        tTitle.text = "車流";

        trafficBtn = MakeSettingButton("開", new Vector2(440f, -180f),
            () => { RaceDirector.TrafficSetting = !RaceDirector.TrafficSetting; RefreshSettingButtons(); });
        trafficBtnLabel = trafficBtn.GetComponentInChildren<Text>();

        RefreshSettingButtons();
    }

    Image MakeSettingButton(string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Set_" + label);
        go.transform.SetParent(selectPanel.transform, false);
        var img = go.AddComponent<Image>();
        var rt = img.rectTransform;
        rt.sizeDelta = new Vector2(78f, 56f);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        go.AddComponent<Button>().onClick.AddListener(onClick);

        var t = MakeText("L", new Vector2(0.5f, 0.5f), Vector2.zero, 30, TextAnchor.MiddleCenter);
        t.transform.SetParent(go.transform, false);
        t.text = label;
        return img;
    }

    void RefreshSettingButtons()
    {
        var on = new Color(0.95f, 0.65f, 0.12f);
        var off = new Color(0.2f, 0.2f, 0.24f);
        for (int i = 0; i < lapBtns.Length; i++)
            lapBtns[i].color = RaceDirector.LapsSetting == lapChoices[i] ? on : off;
        for (int i = 0; i < racerBtns.Length; i++)
            racerBtns[i].color = RaceDirector.RacersSetting == racerChoices[i] ? on : off;
        trafficBtn.color = RaceDirector.TrafficSetting ? on : off;
        if (trafficBtnLabel != null) trafficBtnLabel.text = RaceDirector.TrafficSetting ? "開" : "關";
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
        resultTitle.text = "完賽";

        resultBody = MakeText("ResultBody", new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), 32, TextAnchor.MiddleCenter);
        resultBody.transform.SetParent(resultPanel.transform, false);
        resultBody.text = "";

        var tip = MakeText("ResultTip", new Vector2(0.5f, 0f), new Vector2(0f, 30f), 22, TextAnchor.LowerCenter);
        tip.transform.SetParent(resultPanel.transform, false);
        tip.text = "按 Space 關閉　Esc 回選單";
        tip.color = new Color(1f, 1f, 1f, 0.6f);

        resultPanel.SetActive(false);
    }

    void ShowSelect(bool show)
    {
        if (selectPanel != null) selectPanel.SetActive(show);
    }

    /// 切換賽道。場景已登記在 Build Settings（TrackScenes.RegisterScenes）。
    void LoadTrack(string sceneName)
    {
        var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (active.name == sceneName) return;
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
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

        // 比賽總監接手：起跑格 + 紅燈倒數 + 計圈名次
        if (director != null)
        {
            director.BeginRace(current, npcs);
            lightsPanel.SetActive(true);
            countdownText.text = "";
            foreach (var d in lightDots) d.color = new Color(0.25f, 0.05f, 0.05f);
        }
    }

    void UnsubscribeScoring()
    {
        if (currentScoring == null) return;
        currentScoring.OnBanked -= HandleBanked;
        currentScoring.OnCrash -= HandleCrash;
        currentScoring.OnComboUp -= HandleComboUp;
    }

    // ---------------- 事件 ----------------

    void HandleCountdown(int step)
    {
        // step 3→2→1：紅燈逐顆亮
        int lit = 4 - step;   // 3→1顆、2→2顆、1→3顆
        for (int i = 0; i < lightDots.Length; i++)
            lightDots[i].color = i < lit ? new Color(1f, 0.12f, 0.08f) : new Color(0.25f, 0.05f, 0.05f);
        countdownText.text = step.ToString();
        countdownText.color = new Color(1f, 0.25f, 0.15f);
        goFadeTimer = 0f;
    }

    void HandleGreen()
    {
        // 紅燈熄滅 = 起跑
        foreach (var d in lightDots) d.color = new Color(0.12f, 0.1f, 0.1f);
        countdownText.text = "GO!";
        countdownText.color = new Color(0.3f, 1f, 0.35f);
        goFadeTimer = 1.2f;
    }

    void HandlePlayerFinished(int position, float totalTime)
    {
        float score = currentScoring != null ? currentScoring.TotalScore + currentScoring.PendingScore : 0f;
        float combo = currentScoring != null ? currentScoring.BestCombo : 0f;
        float bestLap = RaceTimer.Instance != null ? RaceTimer.Instance.BestLap : -1f;

        SaveSystem.Submit(currentIndex, bestLap, score, combo,
                          out bool newLap, out bool newScore, out _);

        string carName = currentIndex >= 0 && currentIndex < carNames.Length ? carNames[currentIndex] : "";
        resultTitle.text = "完賽！　第 " + position + " 名";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(carName + "　" + director.TotalLaps + " 圈");
        sb.AppendLine("總時間　　" + RaceTimer.Format(totalTime));
        sb.AppendLine("最速圈　　" + RaceTimer.Format(bestLap) + (newLap ? "　★新紀錄" : ""));
        sb.AppendLine("甩尾總分　" + Mathf.RoundToInt(score) + (newScore ? "　★新紀錄" : ""));
        sb.Append("最高連段　x" + combo.ToString("0.0"));
        resultBody.text = sb.ToString();

        resultPanel.SetActive(true);
        resultTimer = 0f;   // 完賽結算不自動關
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
        // 中途圈：短提示就好，別擋畫面；最後一圈由 HandlePlayerFinished 收尾
        if (director != null && director.CurrentPhase == RaceDirector.Phase.Racing)
        {
            Flash("LAP " + director.PlayerLap + "/" + director.TotalLaps + "　" + RaceTimer.Format(lapTime),
                  new Color(0.6f, 0.9f, 1f));
        }
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

            // N 切換時段（正午 → 黃昏 → 夜晚）
            if (kb.nKey.wasPressedThisFrame && TimeOfDay.Instance != null)
            {
                TimeOfDay.Instance.Cycle();
                Flash("時段：" + TimeOfDay.Instance.Label, new Color(0.7f, 0.85f, 1f));
            }

            // M 開關車流
            if (kb.mKey.wasPressedThisFrame)
            {
                RaceDirector.TrafficSetting = !RaceDirector.TrafficSetting;
                foreach (var go in npcs)
                {
                    if (go == null) continue;
                    var ai = go.GetComponent<NPCDriver>();
                    if (ai != null && ai.style == NPCDriver.Style.Traffic)
                        go.SetActive(RaceDirector.TrafficSetting);
                }
                RefreshSettingButtons();
                Flash(RaceDirector.TrafficSetting ? "車流已上路" : "車流已離場", new Color(0.75f, 1f, 0.8f));
            }

            // F1/F2/F3 切換賽道
            if (kb.f1Key.wasPressedThisFrame) LoadTrack("RallyTrack");
            if (kb.f2Key.wasPressedThisFrame) LoadTrack("Expressway");
            if (kb.f3Key.wasPressedThisFrame) LoadTrack("CityStreet");
        }

        // 起跑燈：GO! 顯示一秒後整組淡出
        if (goFadeTimer > 0f)
        {
            goFadeTimer -= Time.deltaTime;
            if (goFadeTimer <= 0f)
            {
                lightsPanel.SetActive(false);
                countdownText.text = "";
            }
        }

        if (resultPanel.activeSelf && resultTimer > 0f)
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

        // 檔位與轉速（倒檔顯示 R，紅線區轉紅）
        gearText.text = current.GearLabel;
        gearText.color = current.IsReverse ? new Color(1f, 0.45f, 0.35f)
                       : current.EngineRpm > current.spec.redlineRpm * 0.92f ? new Color(1f, 0.3f, 0.25f)
                       : Color.white;
        rpmText.text = Mathf.RoundToInt(current.EngineRpm) + " rpm" + (current.autoShift ? "  [AT]" : "  [MT]");

        var timer = RaceTimer.Instance;
        if (timer != null)
        {
            timeText.text = "TIME  " + RaceTimer.Format(timer.Running ? timer.CurrentLap : -1f);
            lastText.text = "LAST  " + RaceTimer.Format(timer.LastLap);
            bestText.text = "BEST  " + RaceTimer.Format(timer.BestLap);
        }

        // 圈數與名次
        if (director != null &&
            (director.CurrentPhase == RaceDirector.Phase.Racing || director.CurrentPhase == RaceDirector.Phase.Finished))
        {
            lapText.text = "LAP " + director.PlayerLap + "/" + director.TotalLaps;
            posText.text = director.PlayerPosition + " / " + director.EntryCount;
        }
        else
        {
            lapText.text = "";
            posText.text = "";
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
