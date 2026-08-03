using UnityEngine;
using UnityEngine.AI;

/// 用 primitive 組出三種雞：母雞 / 小雞 / 公雞
public static class ChickenFactory
{
    public static GameObject Create(Vector3 pos, ChickenBody.Kind kind = ChickenBody.Kind.Hen)
    {
        var root = new GameObject(kind.ToString());
        root.transform.position = pos;
        var body = root.AddComponent<ChickenBody>();
        body.kind = kind;

        float s = 1f;              // 整體比例
        Color feather = Color.white;
        Color accent = new Color(0.9f, 0.15f, 0.15f);

        switch (kind)
        {
            case ChickenBody.Kind.Chick:
                s = 0.55f;
                feather = new Color(1f, 0.9f, 0.35f);
                body.walkSpeed = 1.2f; body.chaseSpeed = 3.2f;
                body.visionRange = 6f; body.visionAngle = 90f; body.alertTime = 0.6f;
                break;
            case ChickenBody.Kind.Rooster:
                s = 1.25f;
                feather = new Color(0.95f, 0.93f, 0.88f);
                body.walkSpeed = 1.9f; body.chaseSpeed = 5.2f;
                body.visionRange = 12f; body.visionAngle = 130f; body.alertTime = 0.6f;
                break;
            default:
                body.walkSpeed = 1.6f; body.chaseSpeed = 4.6f;
                body.visionRange = 10f; body.visionAngle = 120f; body.alertTime = 0.8f;
                break;
        }

        // 身體
        Part(root, PrimitiveType.Sphere, new Vector3(0f, 0.55f, 0f) * s,
            new Vector3(0.85f, 0.7f, 1.0f) * s, feather);
        // 尾巴
        var tailC = kind == ChickenBody.Kind.Rooster ? new Color(0.1f, 0.4f, 0.25f) : feather;
        var tail = Part(root, PrimitiveType.Sphere, new Vector3(0f, 0.78f, -0.45f) * s,
            new Vector3(0.4f, 0.5f, 0.4f) * s, tailC);
        if (kind == ChickenBody.Kind.Rooster)
        {
            // 公雞的大羽尾：幾片上翹的橢圓
            for (int i = 0; i < 3; i++)
            {
                var f = Part(root, PrimitiveType.Sphere,
                    new Vector3((i - 1) * 0.12f, 0.95f + i * 0.06f, -0.6f) * s,
                    new Vector3(0.12f, 0.5f, 0.3f) * s, new Color(0.08f, 0.45f - i * 0.08f, 0.3f));
                f.transform.localRotation = Quaternion.Euler(-35f, (i - 1) * 15f, 0f);
            }
        }

        // 頭（可動，動畫用）
        var headGo = new GameObject("Head");
        headGo.transform.SetParent(root.transform, false);
        headGo.transform.localPosition = new Vector3(0f, 1.0f, 0.42f) * s;
        Part(headGo, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.42f, 0.42f, 0.42f) * s, feather);
        // 雞冠（公雞更大）
        float combS = kind == ChickenBody.Kind.Rooster ? 1.7f : (kind == ChickenBody.Kind.Chick ? 0.5f : 1f);
        Part(headGo, PrimitiveType.Cube, new Vector3(0f, 0.26f, -0.02f) * s,
            new Vector3(0.09f, 0.18f * combS, 0.22f * combS) * s, accent);
        // 肉垂（公雞）
        if (kind == ChickenBody.Kind.Rooster)
            Part(headGo, PrimitiveType.Sphere, new Vector3(0f, -0.2f, 0.18f) * s,
                new Vector3(0.1f, 0.18f, 0.1f) * s, accent);
        // 嘴
        Part(headGo, PrimitiveType.Cube, new Vector3(0f, 0f, 0.24f) * s,
            new Vector3(0.12f, 0.09f, 0.24f) * s, new Color(1f, 0.6f, 0.1f));
        // 眼睛
        Part(headGo, PrimitiveType.Sphere, new Vector3(0.13f, 0.06f, 0.14f) * s,
            new Vector3(0.09f, 0.09f, 0.09f) * s, Color.black);
        Part(headGo, PrimitiveType.Sphere, new Vector3(-0.13f, 0.06f, 0.14f) * s,
            new Vector3(0.09f, 0.09f, 0.09f) * s, Color.black);
        body.head = headGo.transform;

        // 翅膀（可動）
        body.wingL = Wing(root, s, feather, -1);
        body.wingR = Wing(root, s, feather, +1);

        // 腳（可動）
        body.legL = Leg(root, s, new Vector3(0.15f, 0.3f, 0f) * s);
        body.legR = Leg(root, s, new Vector3(-0.15f, 0.3f, 0f) * s);

        body.CacheBase();

        // 碰撞與導航
        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.6f * s, 0f);
        col.radius = 0.42f * s;
        col.height = 1.2f * s;

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.42f * s;
        agent.height = 1.2f * s;
        agent.acceleration = 14f;
        agent.angularSpeed = 520f;

        root.AddComponent<ChickenAI>();
        SetLayerRecursive(root, LayerMask.NameToLayer("Chicken"));
        return root;
    }

    static Transform Wing(GameObject root, float s, Color feather, int side)
    {
        var pivot = new GameObject(side < 0 ? "WingL" : "WingR");
        pivot.transform.SetParent(root.transform, false);
        pivot.transform.localPosition = new Vector3(side * 0.4f, 0.62f, 0f) * s;
        var mesh = Part(pivot, PrimitiveType.Sphere, new Vector3(side * 0.1f, 0f, 0f) * s,
            new Vector3(0.18f, 0.4f, 0.6f) * s, feather * 0.96f);
        mesh.transform.localRotation = Quaternion.Euler(0f, 0f, side * 12f);
        return pivot.transform;
    }

    static Transform Leg(GameObject root, float s, Vector3 hip)
    {
        var pivot = new GameObject("Leg");
        pivot.transform.SetParent(root.transform, false);
        pivot.transform.localPosition = hip; // 髖關節在上端，往下擺
        Part(pivot, PrimitiveType.Cube, new Vector3(0f, -0.15f, 0f) * s,
            new Vector3(0.07f, 0.3f, 0.07f) * s, new Color(1f, 0.6f, 0.1f));
        Part(pivot, PrimitiveType.Cube, new Vector3(0f, -0.3f, 0.05f) * s,
            new Vector3(0.14f, 0.04f, 0.18f) * s, new Color(1f, 0.6f, 0.1f));
        return pivot.transform;
    }

    static GameObject Part(GameObject parent, PrimitiveType type, Vector3 localPos, Vector3 scale, Color color)
    {
        var p = GameObject.CreatePrimitive(type);
        Object.Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent.transform, false);
        p.transform.localPosition = localPos;
        p.transform.localScale = scale;
        p.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(color);
        return p;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
