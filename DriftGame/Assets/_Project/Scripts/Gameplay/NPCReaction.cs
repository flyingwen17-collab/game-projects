using UnityEngine;
using UnityEngine.UI;

/// NPC 情緒反應（《瘋狂計程車》式）：玩家靠太近或撞到就按喇叭、冒對話泡罵人。
///
/// 三段情緒：
///   靠近（8m 內）→ 皺眉、按短喇叭
///   很近（4m 內）→ 長喇叭 + 罵人對話泡
///   被撞到      → 最生氣的台詞 + 連續喇叭
///
/// 對話泡是每台車自己的 world-space Canvas，會自動面向攝影機，不需要中央管理器。
[RequireComponent(typeof(CarController))]
public class NPCReaction : MonoBehaviour
{
    [Header("音效")]
    public AudioClip hornClip;

    [Header("距離門檻（公尺）")]
    public float noticeRange = 8f;
    public float angryRange = 4f;

    [Header("對話泡")]
    public float bubbleHeight = 2.1f;
    public float bubbleDuration = 2.2f;

    static readonly string[] AnnoyedLines =
    {
        "喂！", "看路啊！", "會不會開車", "太近了啦", "嗶——",
    };
    static readonly string[] AngryLines =
    {
        "你在幹嘛！！", "撞三小啦！", "神經病喔", "找死是不是", "賠我車啦！",
    };
    static readonly string[] CrashLines =
    {
        "我的車！！", "你完蛋了", "報警囉！", "有沒有搞錯啊", "白痴喔！！",
    };

    Transform player;
    AudioSource hornSrc;
    Canvas bubbleCanvas;
    Text bubbleText;
    Image bubbleBg;
    float bubbleTimer;
    float hornCooldown;
    float lineCooldown;
    Camera cam;

    void Start()
    {
        hornSrc = gameObject.AddComponent<AudioSource>();
        hornSrc.playOnAwake = false;
        hornSrc.spatialBlend = 1f;
        hornSrc.minDistance = 10f;
        hornSrc.maxDistance = 140f;
        hornSrc.clip = hornClip;

        BuildBubble();
        SetBubbleVisible(false);
    }

    void BuildBubble()
    {
        var go = new GameObject("Bubble");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, bubbleHeight, 0f);

        bubbleCanvas = go.AddComponent<Canvas>();
        bubbleCanvas.renderMode = RenderMode.WorldSpace;
        var rt = bubbleCanvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300f, 90f);
        rt.localScale = Vector3.one * 0.011f;   // world-space canvas 要縮很小

        var bgGo = new GameObject("Bg");
        bgGo.transform.SetParent(go.transform, false);
        bubbleBg = bgGo.AddComponent<Image>();
        bubbleBg.color = new Color(0.06f, 0.06f, 0.08f, 0.82f);
        var brt = bubbleBg.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = brt.offsetMax = Vector2.zero;

        var txtGo = new GameObject("Text");
        txtGo.transform.SetParent(go.transform, false);
        bubbleText = txtGo.AddComponent<Text>();
        bubbleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bubbleText.fontSize = 46;
        bubbleText.fontStyle = FontStyle.Bold;
        bubbleText.alignment = TextAnchor.MiddleCenter;
        bubbleText.color = Color.white;
        bubbleText.horizontalOverflow = HorizontalWrapMode.Overflow;
        var trt = bubbleText.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
    }

    void SetBubbleVisible(bool v)
    {
        if (bubbleCanvas != null) bubbleCanvas.gameObject.SetActive(v);
    }

    void Update()
    {
        if (hornCooldown > 0f) hornCooldown -= Time.deltaTime;
        if (lineCooldown > 0f) lineCooldown -= Time.deltaTime;

        if (cam == null) cam = Camera.main;
        FindPlayer();

        if (bubbleTimer > 0f)
        {
            bubbleTimer -= Time.deltaTime;
            if (bubbleTimer <= 0f) SetBubbleVisible(false);
            else if (cam != null && bubbleCanvas != null)
            {
                // 面向攝影機（billboard）
                bubbleCanvas.transform.rotation =
                    Quaternion.LookRotation(bubbleCanvas.transform.position - cam.transform.position);
            }
        }

        if (player == null) return;
        float dist = Vector3.Distance(transform.position, player.position);

        if (dist < angryRange)
        {
            Honk(0.85f, 0.55f);
            Say(AngryLines, new Color(1f, 0.55f, 0.4f));
        }
        else if (dist < noticeRange)
        {
            Honk(0.5f, 0.18f);
            Say(AnnoyedLines, Color.white);
        }
    }

    void FindPlayer()
    {
        if (player != null) return;
        // 玩家 = 目前被 GameManager 啟用、且沒有 NPCDriver 的那台車
        foreach (var c in FindObjectsOfType<CarController>())
        {
            if (c == null || !c.enabled) continue;
            if (c.GetComponent<NPCDriver>() != null) continue;
            player = c.transform;
            return;
        }
    }

    void Honk(float volume, float length)
    {
        if (hornCooldown > 0f || hornClip == null) return;
        hornSrc.pitch = Random.Range(0.92f, 1.08f);
        hornSrc.PlayOneShot(hornClip, volume);
        hornCooldown = length + Random.Range(0.6f, 1.4f);
    }

    void Say(string[] pool, Color color)
    {
        if (lineCooldown > 0f) return;
        bubbleText.text = pool[Random.Range(0, pool.Length)];
        bubbleText.color = color;
        bubbleTimer = bubbleDuration;
        SetBubbleVisible(true);
        lineCooldown = Random.Range(2.5f, 5f);
    }

    void OnCollisionEnter(Collision collision)
    {
        // 只有被「玩家」撞到才發飆，NPC 互撞不算
        if (player == null || collision.transform != player) return;
        if (collision.impulse.magnitude < 800f) return;

        hornCooldown = 0f;
        lineCooldown = 0f;
        Honk(1f, 0.9f);
        Say(CrashLines, new Color(1f, 0.3f, 0.25f));
    }
}
