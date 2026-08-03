using UnityEngine;

/// 車燈：頭燈（近燈/遠燈）、尾燈、煞車燈、倒車燈。
/// 天黑會自動開頭燈，也可以按 L 手動切換。
///
/// 每盞燈同時做兩件事：投射真正的 Light（照亮路面）＋ 讓燈殼材質發光（Emission，會吃到 Bloom）。
/// 只有 Light 沒有 Emission，車燈本身看起來是暗的；只有 Emission 沒有 Light，地上不會亮。
[RequireComponent(typeof(CarController))]
public class CarLights : MonoBehaviour
{
    public enum Mode { Off, Low, High }

    [Header("燈殼（由 CarFactory 指定）")]
    public Renderer[] headlightRenderers;
    public Renderer[] taillightRenderers;

    [Header("頭燈位置")]
    public Vector3 headlightLocalLeft = new Vector3(-0.58f, 0.62f, 2.1f);
    public Vector3 headlightLocalRight = new Vector3(0.58f, 0.62f, 2.1f);

    [Header("參數")]
    public float lowBeamRange = 55f;
    public float highBeamRange = 105f;
    public float lowBeamIntensity = 4.5f;
    public float highBeamIntensity = 9f;
    public float lowBeamAngle = 62f;
    public float highBeamAngle = 44f;
    public Color beamColor = new Color(1f, 0.96f, 0.86f);

    public Mode CurrentMode { get; private set; } = Mode.Off;
    public bool LightsOn => CurrentMode != Mode.Off;

    CarController car;
    Light[] beams;
    Material headMat, tailMat;
    bool userOverride;          // 玩家手動操作過就不再自動開關

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    void Start()
    {
        car = GetComponent<CarController>();
        BuildBeams();

        // 燈殼材質做成獨立實例，否則會改到共用材質、三台車一起亮
        if (headlightRenderers != null && headlightRenderers.Length > 0 && headlightRenderers[0] != null)
        {
            headMat = headlightRenderers[0].material;
            foreach (var r in headlightRenderers) if (r != null) r.sharedMaterial = headMat;
            headMat.EnableKeyword("_EMISSION");
        }
        if (taillightRenderers != null && taillightRenderers.Length > 0 && taillightRenderers[0] != null)
        {
            tailMat = taillightRenderers[0].material;
            foreach (var r in taillightRenderers) if (r != null) r.sharedMaterial = tailMat;
            tailMat.EnableKeyword("_EMISSION");
        }

        ApplyMode(Mode.Off);
    }

    void BuildBeams()
    {
        beams = new Light[2];
        Vector3[] pos = { headlightLocalLeft, headlightLocalRight };
        for (int i = 0; i < 2; i++)
        {
            var go = new GameObject("Headlight" + (i == 0 ? "L" : "R"));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos[i];
            go.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);   // 略微朝下，近燈不刺眼

            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = beamColor;
            l.shadows = LightShadows.None;   // 車燈陰影很吃效能，關掉
            l.enabled = false;
            beams[i] = l;
        }
    }

    void Update()
    {
        if (car == null) return;

        // L 切換 關 → 近燈 → 遠燈
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.lKey.wasPressedThisFrame && car.enabled)
        {
            userOverride = true;
            ApplyMode(CurrentMode == Mode.Off ? Mode.Low : CurrentMode == Mode.Low ? Mode.High : Mode.Off);
        }

        // 天黑自動開近燈（玩家手動操作過就不再干涉）
        if (!userOverride && TimeOfDay.Instance != null)
        {
            bool shouldBeOn = TimeOfDay.Instance.IsDark;
            if (shouldBeOn && CurrentMode == Mode.Off) ApplyMode(Mode.Low);
            else if (!shouldBeOn && CurrentMode != Mode.Off) ApplyMode(Mode.Off);
        }

        UpdateTaillights();
    }

    void ApplyMode(Mode m)
    {
        CurrentMode = m;
        bool on = m != Mode.Off;
        bool high = m == Mode.High;

        foreach (var l in beams)
        {
            if (l == null) continue;
            l.enabled = on;
            l.range = high ? highBeamRange : lowBeamRange;
            l.intensity = high ? highBeamIntensity : lowBeamIntensity;
            l.spotAngle = high ? highBeamAngle : lowBeamAngle;
            l.transform.localRotation = Quaternion.Euler(high ? 0.5f : 4f, 0f, 0f);
        }

        if (headMat != null)
            headMat.SetColor(EmissionColorId, on ? beamColor * (high ? 5f : 2.6f) : Color.black);
    }

    void UpdateTaillights()
    {
        if (tailMat == null) return;

        // 煞車 > 倒車 > 小燈 > 熄滅
        Color c;
        if (car.IsBraking) c = new Color(1f, 0.05f, 0.03f) * 6f;          // 煞車：最亮的紅
        else if (car.IsReverse) c = new Color(1f, 1f, 0.95f) * 3.2f;      // 倒車：白燈
        else if (LightsOn) c = new Color(1f, 0.06f, 0.04f) * 1.3f;        // 小燈：暗紅
        else c = Color.black;

        tailMat.SetColor(EmissionColorId, c);
    }
}
