using UnityEngine;

/// 母雞下的蛋：12 秒後孵出 1~3 隻小雞；蚯蚓可以搶先吃掉
public class EggPickup : MonoBehaviour
{
    public static int Count { get; private set; }

    float hatchTime = 12f;
    float age;

    public static void Create(Vector3 pos)
    {
        var egg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        egg.name = "Egg";
        egg.transform.position = new Vector3(pos.x, 0.22f, pos.z);
        egg.transform.localScale = new Vector3(0.32f, 0.42f, 0.32f);
        egg.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(new Color(0.98f, 0.95f, 0.85f));
        var col = egg.GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 1.5f;
        egg.AddComponent<EggPickup>();
    }

    void OnEnable() { Count++; }
    void OnDisable() { Count--; }

    void Update()
    {
        if (GameManager.I == null || GameManager.I.State != GameState.Playing) return;
        age += Time.deltaTime;

        // 快孵化時搖晃
        float left = hatchTime - age;
        if (left < 3f)
            transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 18f) * 9f * (1f - left / 3f + 0.3f));

        if (age >= hatchTime) Hatch();
    }

    void Hatch()
    {
        int n = Random.Range(1, 4); // 1~3 隻
        int existing = FindObjectsOfType<ChickenAI>().Length;
        for (int i = 0; i < n && existing + i < 12; i++)
        {
            Vector2 off = Random.insideUnitCircle * 0.8f;
            ChickenFactory.Create(transform.position + new Vector3(off.x, 0f, off.y), ChickenBody.Kind.Chick);
        }
        ParticleFx.Burst(transform.position, Color.white, 14, 2.5f, 0.25f, 0.8f, 0.7f, "feathers_sheet", false, 4, 2);
        SynthSfx.PlayAt("cluck", transform.position, 0.7f, 1.4f);
        Destroy(gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (GameManager.I != null) GameManager.I.AddBonus(200);
        ScorePopup.Show(transform.position, "+200", new Color(1f, 0.98f, 0.8f));
        var stamina = other.GetComponent<WormStamina>();
        if (stamina != null) stamina.Restore(25f);
        SynthSfx.Play("eat", 0.7f, 0.9f);
        ParticleFx.Burst(transform.position, new Color(1f, 0.98f, 0.85f), 12, 2.2f, 0.25f, 0.8f, 0.6f, "glow_soft", true);
        Destroy(gameObject);
    }
}
