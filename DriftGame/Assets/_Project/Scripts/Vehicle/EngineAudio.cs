using UnityEngine;

/// 車輛音效：引擎音高改由「真實轉速」驅動（不是車速），
/// 所以升檔會聽到轉速掉下來再拉上去 —— 這是引擎聲聽起來真不真的關鍵。
/// 輪胎摩擦聲改由每輪的抓地力使用率驅動，真正打滑才會叫。
[RequireComponent(typeof(CarController))]
[RequireComponent(typeof(DriftDetector))]
public class EngineAudio : MonoBehaviour
{
    public AudioClip engineClip;
    public AudioClip skidClip;
    public AudioClip brakeClip;
    public AudioClip backfireClip;

    [Header("回火")]
    public float backfireVolume = 0.85f;
    [Tooltip("排氣火焰粒子（可留空）")]
    public ParticleSystem exhaustFlame;

    [Header("引擎")]
    public float minPitch = 0.55f;
    public float maxPitch = 2.45f;
    public float engineVolume = 0.55f;

    [Header("輪胎")]
    public float skidVolume = 0.85f;
    [Tooltip("抓地力使用率超過這個值才開始有摩擦聲")]
    public float skidThreshold = 0.82f;

    CarController car;
    DriftDetector drift;
    AudioSource engineSrc, skidSrc, oneShotSrc;
    bool wasBraking;
    int lastGear = 1;
    float backfireCooldown;

    void Awake()
    {
        car = GetComponent<CarController>();
        drift = GetComponent<DriftDetector>();

        engineSrc = MakeSource(engineClip, true);
        engineSrc.volume = 0f;
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

        // ---- 引擎：音高跟著轉速走 ----
        float rpm01 = Mathf.InverseLerp(car.spec.idleRpm, car.spec.redlineRpm, car.EngineRpm);
        float targetPitch = Mathf.Lerp(minPitch, maxPitch, rpm01);
        engineSrc.pitch = Mathf.Lerp(engineSrc.pitch, targetPitch, 12f * Time.deltaTime);

        // 負載越重聲音越沉、油門越大越響
        float load = 0.42f + 0.58f * car.Throttle01;
        float targetVol = active ? engineVolume * load : 0f;
        engineSrc.volume = Mathf.Lerp(engineSrc.volume, targetVol, 8f * Time.deltaTime);

        // 回火放砲：高轉收油或升檔觸發，音高隨機化避免重複感
        if (active && car.BackfireTriggered && backfireClip != null && backfireCooldown <= 0f)
        {
            oneShotSrc.pitch = Random.Range(0.88f, 1.18f);
            oneShotSrc.PlayOneShot(backfireClip, backfireVolume * Random.Range(0.7f, 1f));
            if (exhaustFlame != null) exhaustFlame.Emit(Random.Range(10, 22));
            backfireCooldown = 0.18f;
        }
        if (backfireCooldown > 0f) backfireCooldown -= Time.deltaTime;
        lastGear = car.Gear;

        // ---- 輪胎：用抓地力使用率，真的打滑才叫 ----
        float maxUsage = 0f;
        for (int i = 0; i < car.GripUsage.Length; i++)
            if (car.Grounded[i] && car.GripUsage[i] > maxUsage) maxUsage = car.GripUsage[i];

        float slip01 = Mathf.InverseLerp(skidThreshold, 1f, maxUsage);
        bool skidding = active && slip01 > 0.01f && car.SpeedKmh > 8f;
        float skidTarget = skidding ? skidVolume * slip01 : 0f;
        skidSrc.volume = Mathf.Lerp(skidSrc.volume, skidTarget, 10f * Time.deltaTime);
        skidSrc.pitch = 0.88f + slip01 * 0.35f;

        if (active && car.IsBraking && !wasBraking && car.SpeedKmh > 45f && brakeClip != null)
            oneShotSrc.PlayOneShot(brakeClip, 0.7f);
        wasBraking = car.IsBraking;
    }
}
