using UnityEngine;

/// 車輛音效 v2 —— 真實引擎聲的三個支柱：
///  1. 多轉速分層：低/中/高三個真實取樣層做交叉混音，每層只做 ±35% 的音高微調。
///     （單一 loop 從 0.55 拉到 2.45 倍就是「割草機感」的來源。）
///  2. 負載分離：踩油門時音量大、高頻層前傾；收油只剩機械滾動聲 —— 引擎「呼吸」的關鍵。
///  3. 事件音：換檔頓挫「喀」、退檔補油轉速跳升（物理自然發生）、高轉收油/降檔的排氣回火。
[RequireComponent(typeof(CarController))]
[RequireComponent(typeof(DriftDetector))]
public class EngineAudio : MonoBehaviour
{
    [Header("引擎分層取樣（低/中/高轉速）")]
    public AudioClip engineLowClip;
    public AudioClip engineMidClip;
    public AudioClip engineHighClip;
    [Tooltip("三層取樣的原始錄音對應轉速")]
    public float lowLayerRpm = 1600f;
    public float midLayerRpm = 3900f;
    public float highLayerRpm = 6300f;

    [Header("其他音效")]
    public AudioClip skidClip;
    public AudioClip brakeClip;
    public AudioClip gearShiftClip;
    [Tooltip("回火放砲：多個變體隨機選，避免重複感")]
    public AudioClip[] backfireClips;

    [Header("回火")]
    public float backfireVolume = 0.9f;
    [Tooltip("排氣火焰粒子（可留空）")]
    public ParticleSystem exhaustFlame;

    [Header("引擎")]
    public float engineVolume = 0.6f;

    [Header("輪胎")]
    public float skidVolume = 0.85f;
    [Tooltip("抓地力使用率超過這個值才開始有摩擦聲")]
    public float skidThreshold = 0.82f;

    CarController car;
    DriftDetector drift;
    AudioSource lowSrc, midSrc, highSrc, skidSrc, oneShotSrc;
    bool wasBraking;
    int lastGear = 1;
    float backfireCooldown;

    void Awake()
    {
        car = GetComponent<CarController>();
        drift = GetComponent<DriftDetector>();

        lowSrc = MakeSource(engineLowClip, true);
        midSrc = MakeSource(engineMidClip, true);
        highSrc = MakeSource(engineHighClip, true);
        skidSrc = MakeSource(skidClip, true);
        skidSrc.volume = 0f;
        oneShotSrc = MakeSource(null, false);
    }

    AudioSource MakeSource(AudioClip clip, bool loop)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = loop;
        src.playOnAwake = false;
        src.volume = 0f;
        src.spatialBlend = 1f;
        src.minDistance = 6f;
        src.maxDistance = 90f;
        src.dopplerLevel = 0.35f;
        if (loop && clip != null) src.Play();
        return src;
    }

    void Update()
    {
        bool active = car != null && car.enabled;
        float dt = Time.deltaTime;

        // ---- 引擎三層交叉混音 ----
        float rpm = car.EngineRpm;

        // 負載：踩油門時整體大聲、離合器斷開（換檔瞬間）時輕微收
        float load = 0.34f + 0.66f * car.Throttle01;
        load *= Mathf.Lerp(0.55f, 1f, car.ClutchEngage);
        float master = active ? engineVolume * load : 0f;

        MixLayer(lowSrc, rpm, lowLayerRpm, master, dt, rpm < midLayerRpm ? 1.15f : 1f);
        MixLayer(midSrc, rpm, midLayerRpm, master, dt, 1f);
        // 高轉層踩油門時前傾（排氣咆哮），收油時退後（只剩機械聲）
        MixLayer(highSrc, rpm, highLayerRpm, master, dt, 0.7f + 0.5f * car.Throttle01);

        // ---- 換檔：頓挫聲 + 回火判定 ----
        if (active && car.Gear != lastGear && gearShiftClip != null)
        {
            oneShotSrc.pitch = Random.Range(0.92f, 1.1f);
            oneShotSrc.PlayOneShot(gearShiftClip, 0.55f);
        }
        lastGear = car.Gear;

        // ---- 回火放砲：CarController 觸發（高轉收油、高轉升檔、降檔補油）----
        if (active && car.BackfireTriggered && backfireClips != null
            && backfireClips.Length > 0 && backfireCooldown <= 0f)
        {
            var clip = backfireClips[Random.Range(0, backfireClips.Length)];
            if (clip != null)
            {
                oneShotSrc.pitch = Random.Range(0.88f, 1.18f);
                oneShotSrc.PlayOneShot(clip, backfireVolume * Random.Range(0.7f, 1f));
            }
            if (exhaustFlame != null) exhaustFlame.Emit(Random.Range(10, 22));
            backfireCooldown = 0.18f;
        }
        if (backfireCooldown > 0f) backfireCooldown -= dt;

        // ---- 輪胎：用抓地力使用率，真的打滑才叫 ----
        float maxUsage = 0f;
        for (int i = 0; i < car.GripUsage.Length; i++)
            if (car.Grounded[i] && car.GripUsage[i] > maxUsage) maxUsage = car.GripUsage[i];

        float slip01 = Mathf.InverseLerp(skidThreshold, 1f, maxUsage);
        bool skidding = active && slip01 > 0.01f && car.SpeedKmh > 8f;
        float skidTarget = skidding ? skidVolume * slip01 : 0f;
        skidSrc.volume = Mathf.Lerp(skidSrc.volume, skidTarget, 10f * dt);
        skidSrc.pitch = 0.88f + slip01 * 0.35f;

        if (active && car.IsBraking && !wasBraking && car.SpeedKmh > 45f && brakeClip != null)
            oneShotSrc.PlayOneShot(brakeClip, 0.7f);
        wasBraking = car.IsBraking;
    }

    /// 單一轉速層：音量 = 三角權重（轉速離該層中心越遠越小聲），音高 = rpm/該層中心。
    /// 相鄰層中心比約 2.4，任何轉速都有 1~2 層在響，交叉區兩層等權 → 無縫。
    void MixLayer(AudioSource src, float rpm, float centerRpm, float master, float dt, float bias)
    {
        if (src == null || src.clip == null) return;

        // 對數距離的三角權重：一個八度（×2 / ÷2）外完全靜音
        float dist = Mathf.Abs(Mathf.Log(Mathf.Max(rpm, 500f) / centerRpm, 2f));
        float w = Mathf.Clamp01(1f - dist);

        float targetVol = master * w * bias;
        src.volume = Mathf.Lerp(src.volume, targetVol, 14f * dt);

        float targetPitch = Mathf.Clamp(rpm / centerRpm, 0.6f, 1.75f);
        src.pitch = Mathf.Lerp(src.pitch, targetPitch, 16f * dt);

        if (targetVol > 0.005f && !src.isPlaying) src.Play();
        else if (src.volume < 0.004f && targetVol < 0.004f && src.isPlaying) src.Pause();
    }
}
