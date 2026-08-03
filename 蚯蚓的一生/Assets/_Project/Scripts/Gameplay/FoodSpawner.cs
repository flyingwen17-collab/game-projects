using UnityEngine;

/// 食物生成器：落葉(50) / 莓果(150) / 肥美腐葉(500，刷在雞附近——高風險高報酬)
public class FoodSpawner : MonoBehaviour
{
    public int targetCount = 6;
    public float respawnDelay = 3f;
    public Vector2 areaHalf = new Vector2(17f, 17f);

    float timer;

    void Start()
    {
        for (int i = 0; i < targetCount; i++) Spawn();
    }

    void Update()
    {
        if (GameManager.I == null || GameManager.I.State != GameState.Playing) return;

        if (FoodPickup.Alive < targetCount)
        {
            timer += Time.deltaTime;
            if (timer >= respawnDelay) { timer = 0f; Spawn(); }
        }
        else timer = 0f;
    }

    void Spawn()
    {
        float roll = Random.value;
        if (roll < 0.12f && TrySpawnRareNearChicken()) return;

        bool berry = roll > 0.75f;
        for (int attempt = 0; attempt < 15; attempt++)
        {
            Vector3 pos = new Vector3(
                Random.Range(-areaHalf.x, areaHalf.x), 0.28f,
                Random.Range(-areaHalf.y, areaHalf.y));

            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f) &&
                hit.collider.CompareTag("HardGround")) continue;

            if (berry)
                Make(pos, 0.28f, new Color(0.85f, 0.2f, 0.3f), 150, 20f);
            else
                Make(pos, 0.35f, new Color(0.5f, 0.8f, 0.25f), 50, 30f);
            return;
        }
    }

    bool TrySpawnRareNearChicken()
    {
        var chickens = FindObjectsOfType<ChickenAI>();
        if (chickens.Length == 0) return false;
        var c = chickens[Random.Range(0, chickens.Length)];
        Vector2 off = Random.insideUnitCircle.normalized * Random.Range(2f, 4f);
        Vector3 pos = c.transform.position + new Vector3(off.x, 0f, off.y);
        pos.y = 0.28f;
        if (Mathf.Abs(pos.x) > areaHalf.x || Mathf.Abs(pos.z) > areaHalf.y) return false;

        var food = Make(pos, 0.45f, new Color(1f, 0.82f, 0.2f), 500, 50f);
        food.name = "RareFood";
        return true;
    }

    GameObject Make(Vector3 pos, float scale, Color color, int points, float stamina)
    {
        var food = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        food.name = "Food";
        food.tag = "Food";
        food.transform.position = pos;
        food.transform.localScale = Vector3.one * scale;
        food.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(color);
        var col = food.GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 1.4f;
        var pickup = food.AddComponent<FoodPickup>();
        pickup.points = points;
        pickup.staminaRestore = stamina;
        pickup.baseColor = color;
        return food;
    }
}
