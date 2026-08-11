using UnityEngine;

/// <summary>
/// 音訊總監（企劃書 §4）。全部由遊戲事件與激烈度驅動，不是定時播放：
///
///   撞擊    —— Rikishi.OnImpact 依衝量分三層（light/mid/heavy）+ 音量音高隨力道
///   喊聲    —— 出招事件：突進/投げ吼、突き短喝、被摔悶哼
///   觀眾    —— 底噪音量跟著 SumoMatch.Intensity；決着瞬間爆發歡呼
///   BGM     —— 太鼓雙層：底層恆在，激烈層音量 = Intensity（分層混音）
///
/// 音檔是程序合成佔位（SumoAudioGen），之後同名覆蓋真實錄音即可。
/// </summary>
public class SumoAudioDirector : MonoBehaviour
{
    public SumoMatch match;

    [Header("音檔（P1DohyoBuilder 自動接線）")]
    public AudioClip impactLight, impactMid, impactHeavy, slap, stomp;
    public AudioClip shoutA, shoutB, grunt;
    public AudioClip crowdLoop, crowdRoar;
    public AudioClip taikoBase, taikoIntense;
    public AudioClip grab, creak;

    [Header("混音")]
    [Range(0f, 1f)] public float masterSfx = 0.9f;
    [Range(0f, 1f)] public float crowdMax = 0.75f;
    [Range(0f, 1f)] public float bgmMax = 0.55f;

    AudioSource crowdSrc, roarSrc, bgmBaseSrc, bgmHotSrc;
    AudioSource[] sfxPool;
    int sfxIdx;
    float lastImpactTime;

    const float ImpactCooldown = 0.08f;   // 連續碰撞不要變機關槍

    void Start()
    {
        if (match == null) { enabled = false; return; }

        crowdSrc = MakeSource(crowdLoop, true);
        roarSrc = MakeSource(null, false);
        bgmBaseSrc = MakeSource(taikoBase, true);
        bgmHotSrc = MakeSource(taikoIntense, true);

        // 兩層太鼓同時起跑保持節拍對齊，用音量做 crossfade
        crowdSrc.volume = 0f; crowdSrc.Play();
        bgmBaseSrc.volume = bgmMax; bgmBaseSrc.Play();
        bgmHotSrc.volume = 0f; bgmHotSrc.Play();

        sfxPool = new AudioSource[6];
        for (int i = 0; i < sfxPool.Length; i++) sfxPool[i] = MakeSource(null, false);

        Hook(match.east);
        Hook(match.west);
        match.OnBoutEnd += OnBoutEnd;
        match.OnPhaseChange += OnPhase;
    }

    AudioSource MakeSource(AudioClip clip, bool loop)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = loop;
        src.playOnAwake = false;
        src.spatialBlend = 0f;    // 行動裝置雙人同機：全域聲場，不做 3D 定位
        return src;
    }

    void Hook(Rikishi r)
    {
        r.OnImpact += (force, pos) => PlayImpact(force);
        r.OnAction += (gesture, gripped) => PlayAction(r, gesture, gripped);
        r.OnGrip += () => PlayOneShot(grab, 0.75f * masterSfx, Random.Range(0.95f, 1.05f));
        // 踉蹌踏步：一步一跺，速度越快越重
        r.OnStep += speed => PlayOneShot(stomp,
            Mathf.Clamp01(speed / 6f) * 0.5f * masterSfx, Random.Range(0.85f, 1.05f));
    }

    // ---------- 事件 ----------

    void PlayImpact(float force)
    {
        if (Time.time - lastImpactTime < ImpactCooldown) return;
        lastImpactTime = Time.time;

        // 依衝量分層：門檻對應 SumoConfig 的力量級（突き 3200 / 突進 6200）
        AudioClip clip = force < 2000f ? impactLight : force < 4500f ? impactMid : impactHeavy;
        float vol = Mathf.Clamp01(force / 7000f) * 0.7f + 0.3f;
        float pitch = Random.Range(0.92f, 1.08f) * Mathf.Lerp(1.1f, 0.85f, Mathf.Clamp01(force / 7000f));
        PlayOneShot(clip, vol * masterSfx, pitch);

        // 重擊 → 觀眾即時驚呼（不等激烈度慢慢爬，情緒是瞬間的）
        if (force >= 4500f && !roarSrc.isPlaying)
        {
            roarSrc.clip = crowdRoar;
            roarSrc.volume = crowdMax * 0.45f;
            roarSrc.pitch = Random.Range(1.05f, 1.15f);   // 短促「喔！」
            roarSrc.Play();
        }
    }

    void PlayAction(Rikishi r, SumoGesture g, bool gripped)
    {
        switch (g)
        {
            case SumoGesture.Forward when !gripped:      // 突進：吼
                PlayOneShot(shoutA, 0.8f * masterSfx, Random.Range(0.95f, 1.05f));
                break;
            case SumoGesture.Left when gripped:          // 投げ：大吼
            case SumoGesture.Right when gripped:
                PlayOneShot(shoutB, 0.9f * masterSfx, Random.Range(0.9f, 1.0f));
                break;
            case SumoGesture.Forward when gripped:       // 寄り：發力喝 + 廻し緊繃
            case SumoGesture.Back when gripped:          // 引き：同
                PlayOneShot(creak, 0.7f * masterSfx, Random.Range(0.9f, 1.1f));
                PlayOneShot(grunt, 0.6f * masterSfx, Random.Range(0.85f, 0.95f));
                break;
            case SumoGesture.Tap:                        // 突き：短喝 + 拍擊
                PlayOneShot(grunt, 0.5f * masterSfx, Random.Range(0.95f, 1.15f));
                PlayOneShot(slap, 0.55f * masterSfx, Random.Range(0.9f, 1.1f));
                break;
            case SumoGesture.Back:
            case SumoGesture.Left:
            case SumoGesture.Right:                      // 移動腳步
                PlayOneShot(stomp, 0.35f * masterSfx, Random.Range(0.9f, 1.1f));
                break;
        }
    }

    void OnBoutEnd(Rikishi winner, Rikishi loser, BoutResult result)
    {
        roarSrc.clip = crowdRoar;
        roarSrc.volume = crowdMax;
        roarSrc.pitch = 1f;
        roarSrc.Play();
        if (loser != null) PlayOneShot(grunt, 0.7f * masterSfx, 0.8f);   // 敗者悶哼
    }

    void OnPhase(MatchPhase p)
    {
        if (p == MatchPhase.Shikiri) PlayOneShot(stomp, 0.6f * masterSfx, 0.85f);   // 仕切り四股
    }

    void PlayOneShot(AudioClip clip, float vol, float pitch)
    {
        if (clip == null) return;
        var src = sfxPool[sfxIdx];
        sfxIdx = (sfxIdx + 1) % sfxPool.Length;
        src.clip = clip; src.volume = vol; src.pitch = pitch;
        src.Play();
    }

    // ---------- 激烈度混音（企劃書 §4：觀眾隨比賽激烈程度吶喊） ----------

    void Update()
    {
        if (match == null) return;
        float k = match.Intensity;

        // 觀眾：安靜比賽也有低鳴（0.18），激烈時湧上來
        crowdSrc.volume = Mathf.Lerp(crowdSrc.volume, Mathf.Lerp(0.18f, crowdMax, k), Time.deltaTime * 2f);
        crowdSrc.pitch = Mathf.Lerp(1f, 1.06f, k);

        // BGM 分層：底層恆在，激烈層淡入
        bgmHotSrc.volume = Mathf.Lerp(bgmHotSrc.volume, bgmMax * k, Time.deltaTime * 2.5f);

        // 仕切り時 BGM 收小聲，營造屏息感
        float duck = match.Phase == MatchPhase.Shikiri ? 0.35f : 1f;
        bgmBaseSrc.volume = Mathf.Lerp(bgmBaseSrc.volume, bgmMax * duck, Time.deltaTime * 3f);
    }
}
