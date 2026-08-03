using UnityEngine;

/// 車輛音效：引擎（音高隨速度/油門）、輪胎摩擦（甩尾）、煞車聲。
[RequireComponent(typeof(CarController))]
[RequireComponent(typeof(DriftDetector))]
public class EngineAudio : MonoBehaviour
{
    public AudioClip engineClip;
    public AudioClip skidClip;
    public AudioClip brakeClip;

    [Header("Engine")]
    public float minPitch = 0.65f;
    public float maxPitch = 2.3f;
    public float engineVolume = 0.55f;

    [Header("Skid")]
    public float skidVolume = 0.8f;

    CarController car;
    DriftDetector drift;
    AudioSource engineSrc, skidSrc, oneShotSrc;
    bool wasBraking;

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
        src.spatialBlend = 1f;      // 3D 音效
        src.minDistance = 6f;
        src.maxDistance = 80f;
        src.dopplerLevel = 0.3f;
        if (loop && clip != null) src.Play();
        return src;
    }

    void Update()
    {
        bool active = car != null && car.enabled;

        float speed01 = Mathf.Clamp01(car.SpeedKmh / Mathf.Max(1f, car.topSpeedKmh));
        float targetPitch = Mathf.Lerp(minPitch, maxPitch, speed01) + car.Throttle01 * 0.15f;
        engineSrc.pitch = Mathf.Lerp(engineSrc.pitch, targetPitch, 5f * Time.deltaTime);
        float targetVol = active ? engineVolume * (0.45f + 0.55f * car.Throttle01) : 0f;
        engineSrc.volume = Mathf.Lerp(engineSrc.volume, targetVol, 6f * Time.deltaTime);

        bool skidding = active && (drift.IsDrifting || (car.Handbrake && car.SpeedKmh > 20f));
        float slip01 = Mathf.Clamp01(Mathf.Abs(drift.SlipAngle) / 45f);
        float skidTarget = skidding ? skidVolume * Mathf.Max(0.35f, slip01) : 0f;
        skidSrc.volume = Mathf.Lerp(skidSrc.volume, skidTarget, 8f * Time.deltaTime);
        skidSrc.pitch = 0.9f + slip01 * 0.25f;

        // 高速踩煞車的瞬間播一聲煞車聲
        if (active && car.IsBraking && !wasBraking && car.SpeedKmh > 45f && brakeClip != null)
            oneShotSrc.PlayOneShot(brakeClip, 0.7f);
        wasBraking = car.IsBraking;
    }
}
