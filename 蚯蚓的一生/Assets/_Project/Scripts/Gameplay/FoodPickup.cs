using UnityEngine;

/// 食物：碰到就吃，+分數（吃連擊有倍率）+體力
public class FoodPickup : MonoBehaviour
{
    public int points = 50;
    public float staminaRestore = 30f;
    public Color baseColor = Color.green;

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
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (GameManager.I != null) GameManager.I.AddFoodScore(points);
        var stamina = other.GetComponent<WormStamina>();
        if (stamina != null) stamina.Restore(staminaRestore);
        SynthSfx.Play("eat", 0.6f, Random.Range(0.95f, 1.15f));
        ParticleFx.Burst(transform.position, baseColor, 14, 2.2f, 0.12f, 0.8f);
        Destroy(gameObject);
    }
}
