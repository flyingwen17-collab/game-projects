using UnityEngine;

/// 程式合成 BGM：平時輕快曲 / 被追時緊張曲，自動交叉淡入淡出
public class MusicSystem : MonoBehaviour
{
    const int SR = 44100;
    AudioSource calmSrc, chaseSrc;
    float checkTimer;
    bool gameOverHandled;

    void Start()
    {
        calmSrc = MakeSource(BuildCalm(), 0.35f);
        chaseSrc = MakeSource(BuildChase(), 0f);
    }

    AudioSource MakeSource(AudioClip clip, float vol)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.volume = vol;
        src.spatialBlend = 0f;
        src.Play();
        return src;
    }

    void Update()
    {
        if (GameManager.I != null && GameManager.I.State == GameState.GameOver)
        {
            if (!gameOverHandled)
            {
                gameOverHandled = true;
                SynthSfx.Play("death", 0.7f);
            }
            calmSrc.volume = Mathf.MoveTowards(calmSrc.volume, 0.08f, Time.unscaledDeltaTime * 0.5f);
            chaseSrc.volume = Mathf.MoveTowards(chaseSrc.volume, 0f, Time.unscaledDeltaTime * 0.5f);
            return;
        }
        gameOverHandled = false;

        checkTimer -= Time.unscaledDeltaTime;
        bool chased = ChickenAI.ChaserCount > 0;
        float calmTarget = chased ? 0.06f : 0.35f;
        float chaseTarget = chased ? 0.38f : 0f;
        calmSrc.volume = Mathf.MoveTowards(calmSrc.volume, calmTarget, Time.unscaledDeltaTime * 0.45f);
        chaseSrc.volume = Mathf.MoveTowards(chaseSrc.volume, chaseTarget, Time.unscaledDeltaTime * 0.6f);
    }

    // ---------- 作曲 ----------

    static float NoteHz(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

    /// 平時：C 大調五聲音階、90 BPM、輕快
    static AudioClip BuildCalm()
    {
        float beat = 60f / 90f;
        int bars = 8;
        int n = (int)(SR * beat * 4 * bars);
        var d = new float[n];

        // 低音進行：C Am F G ×2（每小節一個根音，四拍）
        int[] bassRoots = { 48, 45, 41, 43, 48, 45, 41, 43 }; // C3 A2 F2 G2
        for (int bar = 0; bar < bars; bar++)
            for (int b = 0; b < 4; b++)
                AddNote(d, NoteHz(bassRoots[bar] - 12), (bar * 4 + b) * beat, beat * 0.9f, 0.16f, false);

        // 主旋律（八分音符格，0 = 休止）C大調五聲
        int[][] melody =
        {
            new[] { 72, 0, 76, 0, 79, 0, 76, 0 },
            new[] { 81, 0, 79, 76, 0, 72, 0, 0 },
            new[] { 69, 0, 72, 0, 76, 0, 72, 0 },
            new[] { 74, 76, 74, 0, 71, 0, 67, 0 },
            new[] { 72, 0, 76, 0, 79, 0, 84, 0 },
            new[] { 81, 79, 76, 0, 79, 0, 81, 0 },
            new[] { 76, 0, 74, 0, 72, 0, 69, 0 },
            new[] { 72, 0, 0, 0, 67, 0, 72, 0 },
        };
        for (int bar = 0; bar < bars; bar++)
            for (int e = 0; e < 8; e++)
                if (melody[bar][e] > 0)
                    AddNote(d, NoteHz(melody[bar][e]), (bar * 4 + e * 0.5f) * beat, beat * 0.55f, 0.12f, true);

        Normalize(d, 0.8f);
        var clip = AudioClip.Create("bgm_calm", n, 1, SR, false);
        clip.SetData(d, 0);
        return clip;
    }

    /// 追擊：A 小調、150 BPM、急促八分低音
    static AudioClip BuildChase()
    {
        float beat = 60f / 150f;
        int bars = 8;
        int n = (int)(SR * beat * 4 * bars);
        var d = new float[n];

        int[] bassRoots = { 45, 45, 41, 43, 45, 45, 48, 43 }; // Am Am F G / Am Am C G
        for (int bar = 0; bar < bars; bar++)
            for (int e = 0; e < 8; e++)
                AddNote(d, NoteHz(bassRoots[bar] - 12), (bar * 4 + e * 0.5f) * beat, beat * 0.45f, 0.18f, false);

        int[][] melody =
        {
            new[] { 69, 0, 69, 71, 72, 0, 71, 69 },
            new[] { 68, 0, 68, 0, 69, 0, 0, 0 },
            new[] { 65, 0, 69, 0, 72, 0, 69, 65 },
            new[] { 67, 0, 71, 0, 74, 0, 71, 67 },
            new[] { 69, 0, 72, 0, 76, 0, 72, 69 },
            new[] { 68, 0, 71, 0, 74, 0, 71, 0 },
            new[] { 72, 0, 71, 0, 69, 0, 68, 0 },
            new[] { 69, 0, 0, 0, 64, 0, 69, 0 },
        };
        for (int bar = 0; bar < bars; bar++)
            for (int e = 0; e < 8; e++)
                if (melody[bar][e] > 0)
                    AddNote(d, NoteHz(melody[bar][e]), (bar * 4 + e * 0.5f) * beat, beat * 0.4f, 0.11f, true);

        Normalize(d, 0.8f);
        var clip = AudioClip.Create("bgm_chase", n, 1, SR, false);
        clip.SetData(d, 0);
        return clip;
    }

    static void AddNote(float[] d, float freq, float startSec, float durSec, float amp, bool bright)
    {
        int start = (int)(startSec * SR);
        int len = (int)(durSec * SR);
        float phase = 0f;
        for (int i = 0; i < len && start + i < d.Length; i++)
        {
            float t = i / (float)len;
            phase += 2f * Mathf.PI * freq / SR;
            float env = Mathf.Min(1f, t * 12f) * Mathf.Exp(-t * 2.5f);
            float s = Mathf.Sin(phase);
            if (bright) s = s * 0.8f + Mathf.Sin(phase * 2f) * 0.2f; // 加一點泛音
            d[start + i] += s * env * amp;
        }
    }

    static void Normalize(float[] d, float peak)
    {
        float max = 0.0001f;
        for (int i = 0; i < d.Length; i++) max = Mathf.Max(max, Mathf.Abs(d[i]));
        float k = peak / max;
        if (k < 1f) for (int i = 0; i < d.Length; i++) d[i] *= k;
    }
}
