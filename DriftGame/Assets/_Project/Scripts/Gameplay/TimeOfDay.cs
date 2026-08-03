using UnityEngine;
using UnityEngine.Rendering;

/// 時段系統：正午 / 黃昏 / 夜晚。
/// 一次切換天空盒、太陽角度與色溫、環境光、霧、以及「該不該開車燈」。
///
/// 三個時段的設計來自真實攝影的色溫變化：
///   正午 5500K 偏白、黃昏 3000K 偏橘且太陽低角度拉長影子、夜晚只剩天空散射的冷藍。
public class TimeOfDay : MonoBehaviour
{
    public enum Preset { Noon, Dusk, Night }

    [Header("目前時段")]
    public Preset preset = Preset.Dusk;

    [Header("天空盒（由 SceneLook 建立）")]
    public Material skyNoon;
    public Material skyDusk;
    public Material skyNight;

    [Header("太陽")]
    public Light sun;

    /// 夜晚（或昏暗時段）—— 車燈系統會讀這個決定要不要自動開燈
    public bool IsDark => preset == Preset.Night;
    /// 環境亮度 0..1，給 HUD 或其他系統參考
    public float AmbientLevel { get; private set; } = 1f;

    public static TimeOfDay Instance { get; private set; }

    void Awake()
    {
        Instance = this;
        if (sun == null)
        {
            foreach (var l in FindObjectsOfType<Light>())
                if (l.type == LightType.Directional) { sun = l; break; }
        }
    }

    void Start() { Apply(); }

    /// 循環切換時段（正午 → 黃昏 → 夜晚 → 正午）
    public void Cycle()
    {
        preset = preset == Preset.Noon ? Preset.Dusk
               : preset == Preset.Dusk ? Preset.Night
               : Preset.Noon;
        Apply();
    }

    public void Set(Preset p) { preset = p; Apply(); }

    public string Label => preset == Preset.Noon ? "正午" : preset == Preset.Dusk ? "黃昏" : "夜晚";

    public void Apply()
    {
        switch (preset)
        {
            case Preset.Noon:
                ApplySettings(
                    sky: skyNoon,
                    sunEuler: new Vector3(58f, 25f, 0f),      // 高角度 → 短影子
                    sunColor: new Color(1f, 0.98f, 0.94f),    // 約 5500K
                    sunIntensity: 1.45f,
                    ambSky: new Color(0.52f, 0.60f, 0.74f),
                    ambEq: new Color(0.50f, 0.50f, 0.50f),
                    ambGround: new Color(0.36f, 0.34f, 0.30f),
                    fogColor: new Color(0.74f, 0.79f, 0.86f),
                    fogStart: 260f, fogEnd: 900f,
                    ambient: 1f);
                break;

            case Preset.Dusk:
                ApplySettings(
                    sky: skyDusk,
                    sunEuler: new Vector3(12f, -38f, 0f),     // 低角度 → 長影子，黃昏的靈魂
                    sunColor: new Color(1f, 0.72f, 0.46f),    // 約 3000K
                    sunIntensity: 1.05f,
                    ambSky: new Color(0.40f, 0.44f, 0.62f),
                    ambEq: new Color(0.44f, 0.38f, 0.36f),
                    ambGround: new Color(0.30f, 0.23f, 0.18f),
                    fogColor: new Color(0.68f, 0.60f, 0.58f),
                    fogStart: 180f, fogEnd: 780f,
                    ambient: 0.75f);
                break;

            default: // Night
                ApplySettings(
                    sky: skyNight,
                    sunEuler: new Vector3(-14f, 200f, 0f),    // 在地平線下 → 不投射直射光
                    sunColor: new Color(0.55f, 0.62f, 0.85f), // 月光冷藍
                    sunIntensity: 0.12f,
                    ambSky: new Color(0.10f, 0.13f, 0.22f),
                    ambEq: new Color(0.11f, 0.11f, 0.15f),
                    ambGround: new Color(0.06f, 0.06f, 0.08f),
                    fogColor: new Color(0.09f, 0.10f, 0.16f),
                    fogStart: 90f, fogEnd: 520f,
                    ambient: 0.22f);
                break;
        }
    }

    void ApplySettings(Material sky, Vector3 sunEuler, Color sunColor, float sunIntensity,
                       Color ambSky, Color ambEq, Color ambGround,
                       Color fogColor, float fogStart, float fogEnd, float ambient)
    {
        if (sky != null) RenderSettings.skybox = sky;

        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(sunEuler);
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            // 太陽在地平線下就不要投影，否則會出現詭異的反向陰影
            sun.shadows = sunIntensity > 0.3f ? LightShadows.Soft : LightShadows.None;
            RenderSettings.sun = sun;
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambSky;
        RenderSettings.ambientEquatorColor = ambEq;
        RenderSettings.ambientGroundColor = ambGround;
        RenderSettings.ambientIntensity = 1f;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;

        AmbientLevel = ambient;
        DynamicGI.UpdateEnvironment();
    }
}
