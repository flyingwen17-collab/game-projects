using System.Collections;
using UnityEngine;

/// 打擊感中樞：音效、粒子、頓幀(hit-stop)、震屏、勝負慢動作。
/// Time.timeScale 只由本類別操作，避免多處互踩。
public class JuiceManager : MonoBehaviour
{
    [Header("音訊")]
    public AudioClip BgmLoop;
    public AudioClip CrowdLoop;
    public AudioClip SfxHit;
    public AudioClip SfxBigSlam;
    public AudioClip SfxRingOut;
    public AudioClip SfxStart;
    public AudioClip SfxDodge;

    [Header("粒子")]
    public ParticleSystem DustPs;
    public ParticleSystem ImpactPs;
    public ParticleSystem RingOutPs;

    [Header("鏡頭")]
    public CameraRig Rig;

    AudioSource bgm, crowd, sfx;
    bool hitStopActive;

    void Awake()
    {
        bgm = MakeSource(BgmLoop, true, 0.5f);
        crowd = MakeSource(CrowdLoop, true, 0.3f);
        sfx = MakeSource(null, false, 0.9f);
    }

    AudioSource MakeSource(AudioClip clip, bool loop, float vol)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = loop;
        src.volume = vol;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        return src;
    }

    void Start()
    {
        if (bgm.clip != null) bgm.Play();
        if (crowd.clip != null) crowd.Play();
    }

    void OnEnable()
    {
        SumoWrestler.PushHit += OnPushHit;
        SumoWrestler.Dodged += OnDodged;
        MatchManager.RoundStarted += OnRoundStarted;
        MatchManager.RingOutAt += OnRingOut;
    }

    void OnDisable()
    {
        SumoWrestler.PushHit -= OnPushHit;
        SumoWrestler.Dodged -= OnDodged;
        MatchManager.RoundStarted -= OnRoundStarted;
        MatchManager.RingOutAt -= OnRingOut;
    }

    void OnRoundStarted()
    {
        if (SfxStart != null) sfx.PlayOneShot(SfxStart);
    }

    void OnPushHit(Vector3 pos, bool big)
    {
        sfx.PlayOneShot(big ? SfxBigSlam : SfxHit, big ? 1f : 0.8f);
        Emit(DustPs, pos);
        if (big) Emit(ImpactPs, pos);
        if (Rig != null) Rig.Shake(big ? 0.35f : 0.15f);
        StartCoroutine(HitStop(big ? 0.09f : 0.045f));
    }

    void OnDodged(Vector3 pos)
    {
        if (SfxDodge != null) sfx.PlayOneShot(SfxDodge, 0.7f);
        Emit(DustPs, pos + Vector3.up * 0.2f);
    }

    void OnRingOut(Vector3 pos)
    {
        StopAllCoroutines();          // 別讓 hit-stop 蓋掉慢動作
        hitStopActive = false;
        if (SfxRingOut != null) sfx.PlayOneShot(SfxRingOut);
        Emit(RingOutPs, pos);
        if (Rig != null) Rig.Shake(0.6f);
        StartCoroutine(SlowMo());
    }

    static void Emit(ParticleSystem ps, Vector3 pos)
    {
        if (ps == null) return;
        ps.transform.position = pos;
        ps.Play();
    }

    IEnumerator HitStop(float duration)
    {
        if (hitStopActive) yield break;
        hitStopActive = true;
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
        hitStopActive = false;
    }

    IEnumerator SlowMo()
    {
        Time.timeScale = 0.3f;
        yield return new WaitForSecondsRealtime(1.1f);
        Time.timeScale = 1f;
    }
}
