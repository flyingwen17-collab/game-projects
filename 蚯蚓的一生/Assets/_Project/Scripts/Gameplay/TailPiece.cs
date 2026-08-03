using UnityEngine;

/// 斷掉的蚯蚓尾巴：雞會跑來吃，吃掉的雞進入「飽食」狀態
public class TailPiece : MonoBehaviour
{
    public static TailPiece Active { get; private set; }

    float life = 15f;

    public static void Create(Vector3 pos)
    {
        if (Active != null) Destroy(Active.gameObject);
        var go = new GameObject("TailPiece");
        go.transform.position = new Vector3(pos.x, 0.18f, pos.z);
        var tp = go.AddComponent<TailPiece>();
        Active = tp;

        for (int i = 0; i < 2; i++)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(go.transform, false);
            b.transform.localPosition = new Vector3(0f, 0f, -i * 0.2f);
            b.transform.localScale = Vector3.one * (0.3f - i * 0.06f);
            b.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(new Color(1f, 0.55f, 0.65f));
        }
    }

    void Update()
    {
        life -= Time.deltaTime;
        // 掙扎扭動，吸引雞
        transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 10f) * 40f, 0f);
        if (life <= 0f)
        {
            ParticleFx.Burst(transform.position, new Color(0.6f, 0.4f, 0.3f), 6, 1.2f, 0.15f, 1f, 0.5f, "dust_puff");
            Destroy(gameObject);
        }
    }

    /// 被雞吃掉
    public void Consume()
    {
        ParticleFx.Burst(transform.position + Vector3.up * 0.2f,
            new Color(1f, 0.6f, 0.7f), 10, 2f, 0.2f, 0.6f, 0.5f);
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
    }
}
