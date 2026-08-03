using System.Collections.Generic;
using UnityEngine;

/// 蚯蚓節段身體：沿頭部軌跡跟隨 + 蠕動波，取代單一膠囊視覺
public class WormBody : MonoBehaviour
{
    public int segmentCount = 7;
    public float spacing = 0.26f;
    public float wiggleAmp = 0.06f;
    public float wiggleFreq = 7f;

    readonly List<Vector3> trail = new List<Vector3>(); // [0] = 最新頭部位置
    Transform[] segments;
    Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        // 關掉原本的膠囊視覺
        var oldBody = transform.Find("Body");
        if (oldBody != null) oldBody.gameObject.SetActive(false);

        segments = new Transform[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "Segment" + i;
            Object.Destroy(s.GetComponent<Collider>());
            float t = i / (float)(segmentCount - 1);
            float sc = Mathf.Lerp(0.46f, 0.2f, t * t);
            s.transform.localScale = Vector3.one * sc;
            Color c = Color.Lerp(new Color(1f, 0.55f, 0.65f), new Color(0.85f, 0.4f, 0.5f), t);
            s.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(c);
            s.transform.position = transform.position - transform.forward * spacing * (i + 1);
            segments[i] = s.transform;
        }

        // 頭再放大一點，五官清楚
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "HeadBall";
        Object.Destroy(head.GetComponent<Collider>());
        head.transform.SetParent(transform, false);
        head.transform.localPosition = new Vector3(0f, 0f, 0.15f);
        head.transform.localScale = Vector3.one * 0.52f;
        head.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(new Color(1f, 0.58f, 0.68f));

        trail.Add(transform.position);
    }

    void FixedUpdate()
    {
        Vector3 headPos = transform.position;
        if (trail.Count == 0 || Vector3.Distance(trail[0], headPos) > 0.04f)
        {
            trail.Insert(0, headPos);
            int maxPoints = Mathf.CeilToInt(segmentCount * spacing / 0.04f) + 20;
            if (trail.Count > maxPoints) trail.RemoveAt(trail.Count - 1);
        }
    }

    void LateUpdate()
    {
        if (segments == null || trail.Count < 2) return;

        bool moving = rb != null && rb.velocity.sqrMagnitude > 0.3f;
        float amp = wiggleAmp * (moving ? 1f : 0.25f);

        for (int i = 0; i < segmentCount; i++)
        {
            float targetDist = (i + 1) * spacing;
            Vector3 pos = SampleTrail(targetDist, out Vector3 dir);

            // 蠕動：沿身體方向的側向正弦波
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            float wave = Mathf.Sin(Time.time * wiggleFreq - i * 0.9f) * amp;
            segments[i].position = pos + side * wave;
        }
    }

    Vector3 SampleTrail(float dist, out Vector3 dir)
    {
        float acc = 0f;
        for (int i = 0; i < trail.Count - 1; i++)
        {
            float seg = Vector3.Distance(trail[i], trail[i + 1]);
            if (acc + seg >= dist && seg > 0.0001f)
            {
                float t = (dist - acc) / seg;
                dir = (trail[i] - trail[i + 1]).normalized;
                return Vector3.Lerp(trail[i], trail[i + 1], t);
            }
            acc += seg;
        }
        dir = transform.forward;
        return trail[trail.Count - 1];
    }
}
