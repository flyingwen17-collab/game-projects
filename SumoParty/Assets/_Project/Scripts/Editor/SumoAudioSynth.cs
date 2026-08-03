using System.IO;
using UnityEditor;
using UnityEngine;

/// 程式合成相撲音訊：太鼓祭典 BGM、拍子木、衝擊音、觀眾聲。
/// 44.1kHz 16-bit mono WAV（寫法沿用 DriftGame AudioSynth）。
public static class SumoAudioSynth
{
    const int Rate = 44100;
    public const string AudioDir = "Assets/_Project/Audio";

    [MenuItem("Sumo/Generate Audio")]
    public static void GenerateAll()
    {
        Directory.CreateDirectory(AudioDir);
        WriteWav(AudioDir + "/bgm_taiko.wav", Bgm());
        WriteWav(AudioDir + "/crowd_loop.wav", Crowd());
        WriteWav(AudioDir + "/sfx_hit.wav", Hit());
        WriteWav(AudioDir + "/sfx_bigslam.wav", BigSlam());
        WriteWav(AudioDir + "/sfx_ringout.wav", RingOut());
        WriteWav(AudioDir + "/sfx_start.wav", StartClaps());
        WriteWav(AudioDir + "/sfx_dodge.wav", Dodge());
        AssetDatabase.Refresh();
        Debug.Log("[SumoAudioSynth] audio generated");
    }

    // ---------- 樂器 ----------

    // 太鼓：正弦起音音高下滑 + 皮膜噪聲，指數衰減
    static void Taiko(float[] s, int at, float freq, float amp, float decay, float noise, System.Random rnd)
    {
        int len = Mathf.Min(s.Length - at, Rate / 2);
        float phase = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Exp(-t * decay);
            float f = freq * (1f + 0.6f * Mathf.Exp(-t * 40f));
            phase += 2f * Mathf.PI * f / Rate;
            float v = Mathf.Sin(phase) * env;
            if (i < 400) v += noise * env * ((float)rnd.NextDouble() * 2f - 1f);
            s[at + i] += v * amp;
        }
    }

    // 拍子木/締太鼓的「喀」：高通噪聲短脈衝
    static void Clap(float[] s, int at, float amp, System.Random rnd)
    {
        int len = (int)(Rate * 0.07f);
        float lp = 0f;
        for (int i = 0; i < len && at + i < s.Length; i++)
        {
            float env = Mathf.Exp(-i / (Rate * 0.012f));
            float n = (float)rnd.NextDouble() * 2f - 1f;
            lp += (n - lp) * 0.55f;
            s[at + i] += (n - lp) * env * amp;
        }
    }

    // Karplus-Strong 撥弦（三味線感）
    static void Pluck(float[] s, int at, float freq, float amp, float dur)
    {
        int N = Mathf.Max(2, (int)(Rate / freq));
        var buf = new float[N];
        var rnd = new System.Random((int)freq * 7 + 3);
        for (int i = 0; i < N; i++) buf[i] = (float)rnd.NextDouble() * 2f - 1f;
        int len = (int)(Rate * dur);
        for (int i = 0; i < len && at + i < s.Length; i++)
        {
            int j = i % N;
            float v = buf[j];
            buf[j] = (v + buf[(j + 1) % N]) * 0.497f;
            s[at + i] += v * amp * Mathf.Exp(-i / (Rate * dur * 0.6f));
        }
    }

    static void Normalize(float[] s, float peak)
    {
        float max = 0.0001f;
        foreach (var v in s) max = Mathf.Max(max, Mathf.Abs(v));
        float k = peak / max;
        for (int i = 0; i < s.Length; i++) s[i] *= k;
    }

    // ---------- 曲目 ----------

    // 祭典太鼓 BGM：132 BPM、4 小節循環。don-ka 骨架 + 三味線五聲音階句 + 結尾 doko-doko 過門
    static float[] Bgm()
    {
        float beat = Rate * 60f / 132f;       // 32 步 = 16 beat × 2 細分
        int n = (int)(beat * 16f);
        var s = new float[n];
        var rnd = new System.Random(11);
        int At(float step) => (int)(step * beat * 0.5f) % n;

        // 太鼓骨架：強拍 don、反拍 ka
        int[] don = { 0, 4, 8, 12, 16, 20, 24, 28 };
        foreach (int st in don)
            Taiko(s, At(st), st % 8 == 0 ? 75f : 85f, st % 8 == 0 ? 0.9f : 0.55f, 14f, 0.5f, rnd);
        int[] ka = { 2, 6, 10, 14, 18, 22, 26 };
        foreach (int st in ka) Clap(s, At(st), 0.35f, rnd);
        // 過門 doko-doko
        foreach (float st in new[] { 29f, 29.5f, 30f, 30.5f, 31f, 31.5f })
            Taiko(s, At(st), 95f, 0.4f, 22f, 0.4f, rnd);

        // 三味線句（D 五聲）：D F G A C
        float D4 = 293.66f, F4 = 349.23f, G4 = 392f, A4 = 440f, C5 = 523.25f, D5 = 587.33f;
        (float step, float f)[] riff = {
            (0, D4), (3, F4), (4, G4), (7, A4),
            (8, G4), (11, F4), (12, D4),
            (16, D4), (19, G4), (20, A4), (23, C5),
            (24, D5), (26, C5), (27, A4), (28, G4),
        };
        foreach (var (st, f) in riff) Pluck(s, At(st), f, 0.30f, 0.5f);

        Normalize(s, 0.85f);
        return s;
    }

    // 觀眾環境聲：帶通噪聲 + 慢速起伏
    static float[] Crowd()
    {
        int n = Rate * 4;
        var s = new float[n];
        var rnd = new System.Random(23);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float noise = (float)rnd.NextDouble() * 2f - 1f;
            lp += (noise - lp) * 0.06f;      // 低通 → 人聲團塊感
            lp2 += (lp - lp2) * 0.02f;
            float swell = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * t / 4f);   // 4 秒週期正好循環
            s[i] = (lp - lp2) * 3.5f * swell;
        }
        Normalize(s, 0.5f);
        return s;
    }

    static float[] Hit()
    {
        var s = new float[(int)(Rate * 0.25f)];
        var rnd = new System.Random(5);
        Taiko(s, 0, 100f, 1f, 18f, 0.8f, rnd);
        Normalize(s, 0.85f);
        return s;
    }

    static float[] BigSlam()
    {
        var s = new float[(int)(Rate * 0.5f)];
        var rnd = new System.Random(6);
        Taiko(s, 0, 62f, 1f, 8f, 1.0f, rnd);
        Taiko(s, (int)(Rate * 0.02f), 124f, 0.3f, 20f, 0.3f, rnd);
        Normalize(s, 0.92f);
        return s;
    }

    static float[] RingOut()
    {
        int n = (int)(Rate * 1.4f);
        var s = new float[n];
        var rnd = new System.Random(7);
        Taiko(s, 0, 52f, 1f, 5f, 1.0f, rnd);
        Taiko(s, (int)(Rate * 0.12f), 78f, 0.6f, 9f, 0.6f, rnd);
        // 觀眾歡呼湧上來
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float noise = (float)rnd.NextDouble() * 2f - 1f;
            lp += (noise - lp) * 0.08f;
            lp2 += (lp - lp2) * 0.02f;
            float env = Mathf.Clamp01(t / 0.25f) * Mathf.Exp(-Mathf.Max(0f, t - 0.5f) * 2.2f);
            s[i] += (lp - lp2) * 3f * env * 0.7f;
        }
        Normalize(s, 0.95f);
        return s;
    }

    static float[] StartClaps()
    {
        var s = new float[(int)(Rate * 0.6f)];
        var rnd = new System.Random(9);
        Clap(s, 0, 1f, rnd);
        Clap(s, (int)(Rate * 0.28f), 1f, rnd);
        Normalize(s, 0.8f);
        return s;
    }

    static float[] Dodge()
    {
        int n = (int)(Rate * 0.22f);
        var s = new float[n];
        var rnd = new System.Random(13);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float noise = (float)rnd.NextDouble() * 2f - 1f;
            float cutoff = 0.15f + 0.7f * t;          // 往上掃 → 咻
            lp += (noise - lp) * cutoff;
            s[i] = lp * Mathf.Sin(t * Mathf.PI);      // 弧形包絡
        }
        Normalize(s, 0.6f);
        return s;
    }

    // ---------- WAV ----------
    static void WriteWav(string path, float[] samples)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            int dataLen = samples.Length * 2;
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataLen);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(Rate);
            bw.Write(Rate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(dataLen);
            foreach (var v in samples)
                bw.Write((short)(Mathf.Clamp(v, -1f, 1f) * 32760f));
        }
    }
}
