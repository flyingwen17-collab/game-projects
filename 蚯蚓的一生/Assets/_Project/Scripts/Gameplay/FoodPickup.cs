using UnityEngine;

/// 食物：碰到就吃，+分數（吃連擊有倍率）+體力
public class FoodPickup : MonoBehaviour
{
    public int points = 50;
    public float staminaRestore = 30f;
    public Color baseColor = Color.green;
    public bool isPowerFeed; // 特殊飼料：吃了進入無敵吃雞模式

    public static int Alive { get; private set; }

    float bobOffset;

    void OnEnable() { Alive++; }
    void OnDisable() { Alive--; }

    void Start()
    {
        bobOffset = Random.value * Mathf.PI * 2f;
    }

    void Update()
    {
        Vector3 p = transform.position;
        p.y = 0.28f + Mathf.Sin(Time.time * 2.5f + bobOffset) * 0.06f;
        transform.position = p;
        transform.Rotate(0f, 60f * Time.deltaTime, 0f);

        // 特殊飼料：脈動發光提示
        if (isPowerFeed)
        {
            float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.15f;
            transform.localScale = Vector3.one * 0.45f * pulse;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (GameManager.I != null)
        {
            GameManager.I.AddFoodScore(points);
            int shown = Mathf.RoundToInt(points * GameManager.I.ComboMultiplier);
            ScorePopup.Show(transform.position, "+" + shown,
                isPowerFeed ? new Color(1f, 0.5f, 0.1f) : new Color(1f, 0.95f, 0.4f));
        }
        var stamina = other.GetComponent<WormStamina>();
        if (stamina != null) stamina.Restore(staminaRestore);
        if (isPowerFeed)
        {
            var skills = other.GetComponent<WormSkills>();
            if (skills != null) skills.ActivatePower();
        }
        SynthSfx.Play("eat", 0.6f, Random.Range(0.95f, 1.15f));
        ParticleFx.Burst(transform.position, baseColor, 10, 2.2f, 0.28f, 0.8f, 0.8f, "leaves_sheet", false, 4, 2);
        ParticleFx.Burst(transform.position, new Color(1f, 1f, 0.7f), 6, 1.6f, 0.3f, 0.1f, 0.4f, "glow_soft", true);
        Destroy(gameObject);
    }
}
