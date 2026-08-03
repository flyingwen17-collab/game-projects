using UnityEngine;

/// 世界空間分數浮字：+50、+500 從吃到的位置飄起淡出（市售遊戲標配的回饋感）
public class ScorePopup : MonoBehaviour
{
    static Font cachedFont;

    TextMesh tm;
    float life = 0.9f;
    float age;
    Vector3 vel = new Vector3(0f, 1.6f, 0f);

    public static void Show(Vector3 pos, string text, Color color, float scale = 1f)
    {
        var go = new GameObject("ScorePopup");
        go.transform.position = pos + Vector3.up * 0.5f;
        var p = go.AddComponent<ScorePopup>();
        p.tm = go.AddComponent<TextMesh>();
        if (cachedFont == null)
        {
            cachedFont = Font.CreateDynamicFontFromOSFont("Microsoft JhengHei", 48);
            if (cachedFont == null) cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        p.tm.font = cachedFont;
        go.GetComponent<MeshRenderer>().sharedMaterial = cachedFont.material;
        p.tm.fontSize = 44;
        p.tm.characterSize = 0.09f * scale;
        p.tm.anchor = TextAnchor.MiddleCenter;
        p.tm.alignment = TextAlignment.Center;
        p.tm.fontStyle = FontStyle.Bold;
        p.tm.text = text;
        p.tm.color = color;
    }

    void Update()
    {
        age += Time.deltaTime;
        transform.position += vel * Time.deltaTime;
        vel *= 1f - 1.5f * Time.deltaTime;

        if (Camera.main != null)
            transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);

        if (tm != null)
        {
            var c = tm.color;
            c.a = Mathf.Clamp01(1f - (age - life * 0.5f) / (life * 0.5f));
            tm.color = c;
        }
        if (age >= life) Destroy(gameObject);
    }
}
