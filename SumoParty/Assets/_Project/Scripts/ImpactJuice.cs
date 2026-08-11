using System.Collections;
using UnityEngine;

/// <summary>
/// 大噸位撞擊感（企劃書三根柱子之一「質量感」的視覺層）。
/// 全部由物理事件驅動，力道多大回饋多大：
///
///   震屏     —— 撞擊衝量 → 相機抖動幅度（300kg 對撞要撼動鏡頭）
///   塵土爆   —— 撞擊點噴沙塵粒子（土俵是砂地）
///   決着演出 —— 勝負瞬間 0.35 秒慢動作 + 大震屏
/// </summary>
public class ImpactJuice : MonoBehaviour
{
    public SumoMatch match;
    public Camera cam;
    public Material dustMaterial;   // 建構器指定（執行期建材質在 URP build 會變洋紅）

    ParticleSystem dust;
    Vector3 camBasePos;
    float shake;

    const float ShakeDecay = 4.5f;

    void Start()
    {
        if (match == null || cam == null) { enabled = false; return; }
        camBasePos = cam.transform.position;

        BuildDust();
        match.east.OnImpact += OnImpact;
        match.west.OnImpact += OnImpact;
        match.OnBoutEnd += OnBoutEnd;
        // 踉蹌踏步 → 腳邊小塵土（踩沙地的份量）
        match.east.OnStep += s => StepDust(match.east, s);
        match.west.OnStep += s => StepDust(match.west, s);
    }

    void StepDust(Rikishi r, float speed)
    {
        if (dust == null) return;
        dust.transform.position = r.transform.position + Vector3.up * 0.06f;
        dust.Emit(Mathf.RoundToInt(2 + Mathf.Min(6f, speed)));
    }

    void BuildDust()
    {
        var go = new GameObject("DustFX");
        dust = go.AddComponent<ParticleSystem>();
        var main = dust.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startColor = new Color(0.80f, 0.72f, 0.58f, 0.55f);
        main.gravityModifier = 0.35f;
        main.playOnAwake = false;
        main.maxParticles = 120;                      // 行動裝置預算

        var emission = dust.emission;
        emission.enabled = false;                     // 只用 Emit() 事件觸發

        var shape = dust.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.18f;

        var colorOverLife = dust.colorOverLifetime;
        colorOverLife.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = grad;

        var r = dust.GetComponent<ParticleSystemRenderer>();
        if (dustMaterial != null) r.sharedMaterial = dustMaterial;
    }

    void OnImpact(float force, Vector3 pos)
    {
        // 力道 → 回饋量。門檻以下不打擾（小碰不值得震屏）
        if (force < 1500f) return;
        float k = Mathf.Clamp01(force / 8000f);

        shake = Mathf.Max(shake, 0.045f + 0.16f * k);

        // 重擊微頓幀（格鬥遊戲的 hit-stop）：一瞬間的凝滯讓衝擊「咬」進去
        if (force >= 5200f && Mathf.Approximately(Time.timeScale, 1f))
            StartCoroutine(MicroStop());

        if (dust != null)
        {
            dust.transform.position = new Vector3(pos.x, Mathf.Max(0.05f, pos.y * 0.3f), pos.z);
            dust.Emit(Mathf.RoundToInt(6 + 22 * k));
        }
    }

    void OnBoutEnd(Rikishi winner, Rikishi loser, BoutResult result)
    {
        shake = Mathf.Max(shake, 0.22f);
        if (result != BoutResult.TimeUp) StartCoroutine(HitStop());
    }

    IEnumerator MicroStop()
    {
        Time.timeScale = 0.5f;
        yield return new WaitForSecondsRealtime(0.07f);
        if (Mathf.Approximately(Time.timeScale, 0.5f)) Time.timeScale = 1f;   // 別蓋掉決着慢動作
    }

    IEnumerator HitStop()
    {
        Time.timeScale = 0.25f;                       // 決着慢動作
        yield return new WaitForSecondsRealtime(0.35f);
        Time.timeScale = 1f;
    }

    void LateUpdate()
    {
        if (shake <= 0.001f)
        {
            cam.transform.position = camBasePos;
            return;
        }
        shake = Mathf.Lerp(shake, 0f, Time.unscaledDeltaTime * ShakeDecay);
        cam.transform.position = camBasePos + (Vector3)(Random.insideUnitCircle * shake)
                                            + Vector3.up * Random.Range(-shake, shake) * 0.5f;
    }
}
