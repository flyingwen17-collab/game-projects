using System.Collections.Generic;
using UnityEngine;

/// 蚯蚓節段身體：沿頭部軌跡跟隨 + 蠕動波；支援動態變長/斷尾、鑽土時隱藏
public class WormBody : MonoBehaviour
{
    public int startSegments = 7;
    public int maxSegments = 14;
    public int minSegments = 4;
    public float spacing = 0.26f;
    public float wiggleAmp = 0.06f;
    public float wiggleFreq = 7f;

    public int SegmentCount => segments.Count;

    readonly List<Vector3> trail = new List<Vector3>();
    readonly List<Transform> segments = new List<Transform>();
    Rigidbody rb;
    BurrowSystem burrow;
    Renderer[] headRenderers;
    bool visible = true;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        burrow = GetComponent<BurrowSystem>();

        var oldBody = transform.Find("Body");
        if (oldBody != null) oldBody.gameObject.SetActive(false);

        for (int i = 0; i < startSegments; i++) AddSegment();

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "HeadBall";
        Object.Destroy(head.GetComponent<Collider>());
        head.transform.SetParent(transform, false);
        head.transform.localPosition = new Vector3(0f, 0f, 0.15f);
        head.transform.localScale = Vector3.one * 0.52f;
        head.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(new Color(1f, 0.58f, 0.68f));

        headRenderers = GetComponentsInChildren<Renderer>(true);
        trail.Add(transform.position);
    }

    /// 變長 n 節（吃雞獎勵）
    public void Grow(int n)
    {
        for (int i = 0; i < n && segments.Count < maxSegments; i++) AddSegment();
        RescaleSegments();
    }

    /// 斷尾：移除最後一節，回傳斷掉位置；不足最少節數回傳 null
    public Vector3? Shrink()
    {
        if (segments.Count <= minSegments) return null;
        var last = segments[segments.Count - 1];
        Vector3 pos = last.position;
        segments.RemoveAt(segments.Count - 1);
        Object.Destroy(last.gameObject);
        RescaleSegments();
        return pos;
    }

    void AddSegment()
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = "Segment" + segments.Count;
        Object.Destroy(s.GetComponent<Collider>());
        Vector3 basePos = segments.Count > 0
            ? segments[segments.Count - 1].position
            : transform.position - transform.forward * spacing;
        s.transform.position = basePos - transform.forward * spacing;
        segments.Add(s.transform);
        RescaleSegments();
    }

    void RescaleSegments()
    {
        int n = segments.Count;
        for (int i = 0; i < n; i++)
        {
            if (segments[i] == null) continue;
            float t = n > 1 ? i / (float)(n - 1) : 0f;
            float sc = Mathf.Lerp(0.46f, 0.2f, t * t);
            segments[i].localScale = Vector3.one * sc;
            Color c = Color.Lerp(new Color(1f, 0.55f, 0.65f), new Color(0.85f, 0.4f, 0.5f), t);
            segments[i].GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(c);
        }
    }

    void FixedUpdate()
    {
        Vector3 headPos = transform.position;
        if (trail.Count == 0 || Vector3.Distance(trail[0], headPos) > 0.04f)
        {
            trail.Insert(0, headPos);
            int maxPoints = Mathf.CeilToInt(maxSegments * spacing / 0.04f) + 20;
            if (trail.Count > maxPoints) trail.RemoveAt(trail.Count - 1);
        }
    }

    void LateUpdate()
    {
        // 鑽土時整條蚯蚓隱形——看起來真的鑽進土裡（位置由土丘與土痕表示）
        bool shouldShow = burrow == null || !burrow.IsBurrowed;
        if (shouldShow != visible)
        {
            visible = shouldShow;
            if (headRenderers != null)
                foreach (var r in headRenderers) if (r != null) r.enabled = visible;
            foreach (var s in segments)
                if (s != null) { var r = s.GetComponent<Renderer>(); if (r != null) r.enabled = visible; }
        }

        if (segments.Count == 0 || trail.Count < 2) return;

        bool moving = rb != null && rb.velocity.sqrMagnitude > 0.3f;
        float amp = wiggleAmp * (moving ? 1f : 0.25f);

        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i] == null) continue;
            float targetDist = (i + 1) * spacing;
            Vector3 pos = SampleTrail(targetDist, out Vector3 dir);
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            float wave = Mathf.Sin(Time.time * wiggleFreq - i * 0.9f) * amp;
            segments[i].position = pos + side * wave;

            // 新增節可能還沒開 renderer 狀態同步
            var r = segments[i].GetComponent<Renderer>();
            if (r != null && r.enabled != visible) r.enabled = visible;
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
