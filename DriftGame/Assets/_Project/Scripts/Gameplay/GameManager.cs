using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// 遊戲流程：選車畫面、HUD（時速/圈時/甩尾提示）、換車（1/2/3）。
/// UI 全部在執行時用程式建立，場景端只需指定車輛與攝影機。
public class GameManager : MonoBehaviour
{
    public CarController[] cars;
    public string[] carNames = { "SUBARU WRX", "HONDA FIT", "TOYOTA 86" };
    public string[] carDescs = { "AWD・四驅穩定高速", "FWD・靈活小鋼砲", "RWD・甩尾之魂" };
    public CameraFollow cameraFollow;

    CarController current;
    Font font;
    Canvas canvas;
    GameObject selectPanel;
    Text speedText, timeText, lastText, bestText, driftText, hintText;
    DriftDetector currentDrift;

    void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        foreach (var c in cars) if (c != null) c.enabled = false;
        BuildUI();
        ShowSelect(true);
    }

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

        // 甩尾提示（中下）
        driftText = MakeText("Drift", new Vector2(0.5f, 0f), new Vector2(0f, 170f), 54, TextAnchor.LowerCenter);
        driftText.color = new Color(1f, 0.5f, 0.1f);
        driftText.text = "";

        // 操作提示（左下）
        hintText = MakeText("Hint", new Vector2(0f, 0f), new Vector2(30f, 30f), 22, TextAnchor.LowerLeft);
        hintText.text = "WASD 駕駛  Space 手煞車甩尾  R 重置  1/2/3 換車";
        hintText.color = new Color(1f, 1f, 1f, 0.65f);

        BuildSelectPanel();
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

            var label = MakeText("Label", new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), 36, TextAnchor.MiddleCenter);
            label.transform.SetParent(btnGo.transform, false);
            label.text = carNames[i];
            label.color = i == 2 ? Color.black : Color.white;

            var desc = MakeText("Desc", new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), 24, TextAnchor.MiddleCenter);
            desc.transform.SetParent(btnGo.transform, false);
            desc.text = carDescs[i] + "\n（按 " + (i + 1) + "）";
            desc.color = i == 2 ? new Color(0.2f, 0.2f, 0.2f) : new Color(1f, 1f, 1f, 0.85f);
        }
    }

    void ShowSelect(bool show)
    {
        if (selectPanel != null) selectPanel.SetActive(show);
    }

    public void StartRace(int carIndex)
    {
        if (carIndex < 0 || carIndex >= cars.Length || cars[carIndex] == null) return;

        foreach (var c in cars)
        {
            if (c == null) continue;
            c.RespawnAtStart();
            c.enabled = false;
        }

        current = cars[carIndex];
        current.enabled = true;
        currentDrift = current.GetComponent<DriftDetector>();
        if (cameraFollow != null) cameraFollow.SetTarget(current.transform);
        if (RaceTimer.Instance != null) RaceTimer.Instance.ResetAll();
        ShowSelect(false);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) StartRace(0);
            if (kb.digit2Key.wasPressedThisFrame) StartRace(1);
            if (kb.digit3Key.wasPressedThisFrame) StartRace(2);
            if (kb.escapeKey.wasPressedThisFrame) ShowSelect(!selectPanel.activeSelf);
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

        if (currentDrift != null && currentDrift.IsDrifting)
        {
            driftText.text = "DRIFT! " + Mathf.Abs(currentDrift.SlipAngle).ToString("F0") + "°";
            float pulse = 1f + Mathf.Sin(Time.time * 10f) * 0.06f;
            driftText.transform.localScale = Vector3.one * pulse;
        }
        else
        {
            driftText.text = "";
        }
    }
}
