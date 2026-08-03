using UnityEngine;

/// 環境蝴蝶：在場地上空飛舞，讓畫面生動（純裝飾、無碰撞）
public class Butterfly : MonoBehaviour
{
    Transform wingL, wingR;
    Vector3 center;
    float speed;
    float phase;
    Vector3 target;

    public static void Spawn(Vector3 center, Color color)
    {
        var go = new GameObject("Butterfly");
        var b = go.AddComponent<Butterfly>();
        b.center = center;
        b.speed = Random.Range(1.2f, 2f);
        b.phase = Random.value * 10f;

        b.wingL = MakeWing(go.transform, color, -1);
        b.wingR = MakeWing(go.transform, color, +1);
        go.transform.position = center + new Vector3(Random.Range(-3f, 3f), Random.Range(0.8f, 1.6f), Random.Range(-3f, 3f));
        b.PickTarget();
    }

    static Transform MakeWing(Transform parent, Color color, int side)
    {
        var pivot = new GameObject(side < 0 ? "WingL" : "WingR").transform;
        pivot.SetParent(parent, false);
        var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(w.GetComponent<Collider>());
        w.transform.SetParent(pivot, false);
        w.transform.localPosition = new Vector3(side * 0.09f, 0f, 0f);
        w.transform.localScale = new Vector3(0.16f, 0.01f, 0.12f);
        w.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(color);
        return pivot;
    }

    void PickTarget()
    {
        target = center + new Vector3(Random.Range(-6f, 6f), Random.Range(0.7f, 1.8f), Random.Range(-6f, 6f));
    }

    void Update()
    {
        float flap = Mathf.Sin(Time.time * 18f + phase) * 55f;
        if (wingL != null) wingL.localRotation = Quaternion.Euler(0f, 0f, flap);
        if (wingR != null) wingR.localRotation = Quaternion.Euler(0f, 0f, -flap);

        Vector3 to = target - transform.position;
        if (to.magnitude < 0.4f) { PickTarget(); return; }
        Vector3 dir = to.normalized;
        transform.position += dir * speed * Time.deltaTime
            + Vector3.up * Mathf.Sin(Time.time * 5f + phase) * 0.25f * Time.deltaTime;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z)), 3f * Time.deltaTime);
    }
}
