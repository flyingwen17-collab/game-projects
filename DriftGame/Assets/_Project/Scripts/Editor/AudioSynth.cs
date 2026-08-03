using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// 程式合成遊戲音效：引擎循環、輪胎摩擦、煞車聲、熱血 BGM。
/// 全部產生為 44.1kHz 16-bit mono WAV，免下載素材。
public static class AudioSynth
{
    const int Rate = 44100;
    public const string AudioDir = "Assets/_Project/Audio";

    [MenuItem("Tools/Drift Game/Generate Audio")]
    public static void GenerateAll()
    {
        Directory.CreateDirectory(AudioDir);
        WriteWav(AudioDir + "/engine_loop.wav", Engine());
        WriteWav(AudioDir + "/skid_loop.wav", Skid());
        WriteWav(AudioDir + "/brake_squeal.wav", Brake());
        WriteWav(AudioDir + "/bgm_loop.wav", Bgm());
        WriteWav(AudioDir + "/impact.wav", Impact());
        WriteWav(AudioDir + "/backfire.wav", Backfire());
        WriteWav(AudioDir + "/horn.wav", Horn());
        AssetDatabase.Refresh();
        Debug.Log("[AudioSynth] audio generated");
    }

    // ---------- 引擎：四缸四行程點火脈衝模型 ----------
    /// 真實引擎聲的來源不是正弦諧波，而是「一連串排氣脈衝」：
    /// 四缸四行程每轉兩次點火，每次點火在排氣管產生一個帶共鳴尾巴的爆音。
    /// 脈衝的銳利度 + 排氣管共鳴 + 各缸的細微不平均，才是引擎聲的特徵。
    static float[] Engine()
    {
        int n = Rate;                 // 1 秒，f0 取整數才能無縫循環
        var s = new float[n];
        const float f0 = 100f;        // 點火頻率 100Hz ≈ 四缸 3000rpm
        const int cylinders = 4;
        var rnd = new System.Random(7);

        // 各缸的細微差異（點火時機與強度），真車不會完全平均
        var cylGain = new float[cylinders];
        var cylPhase = new float[cylinders];
        for (int c = 0; c < cylinders; c++)
        {
            cylGain[c] = 0.85f + (float)rnd.NextDouble() * 0.3f;
            cylPhase[c] = ((float)rnd.NextDouble() - 0.5f) * 0.02f;
        }

        // 排氣管共鳴用的雙極點濾波器狀態
        float r1 = 0.9955f, w1 = 2f * Mathf.PI * 180f / Rate;   // 低頻筒身共鳴
        float r2 = 0.988f, w2 = 2f * Mathf.PI * 720f / Rate;    // 中頻金屬感
        float a1 = 2f * r1 * Mathf.Cos(w1), b1 = -r1 * r1;
        float a2 = 2f * r2 * Mathf.Cos(w2), b2 = -r2 * r2;
        float y1 = 0f, y2 = 0f, z1 = 0f, z2 = 0f;

        float pulsePeriod = Rate / f0;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;

            // 產生點火脈衝串
            float pulsePos = (i % pulsePeriod) / pulsePeriod;   // 0..1 在單次點火週期內
            int cyl = (int)(i / pulsePeriod) % cylinders;
            float shifted = Mathf.Repeat(pulsePos + cylPhase[cyl], 1f);

            // 脈衝：極短的爆發（約佔週期 8%），之後靜默等下一次點火
            float excite = shifted < 0.08f
                ? cylGain[cyl] * Mathf.Exp(-shifted * 45f) * (1f - shifted / 0.08f)
                : 0f;
            excite += 0.02f * ((float)rnd.NextDouble() * 2f - 1f);   // 底噪

            // 送進兩組共鳴器 → 排氣管音色
            float o1 = excite + a1 * y1 + b1 * y2; y2 = y1; y1 = o1;
            float o2 = excite + a2 * z1 + b2 * z2; z2 = z1; z1 = o2;

            // 次諧波：四缸的「突突」低頻拍子
            float sub = 0.35f * Mathf.Sin(2f * Mathf.PI * (f0 / cylinders) * t);

            float v = o1 * 0.55f + o2 * 0.18f + sub;
            s[i] = (float)Math.Tanh(v * 1.5f);
        }
        Crossfade(s, Rate / 200);
        Normalize(s, 0.78f);
        return s;
    }

    // ---------- 輪胎摩擦：共振濾波噪音 ----------
    static float[] Skid()
    {
        int n = Rate * 2;
        var s = new float[n];
        var rnd = new System.Random(3);
        float y1 = 0, y2 = 0, z1 = 0, z2 = 0;
        float r1 = 0.965f, w1 = 2f * Mathf.PI * 1100f / Rate;
        float r2 = 0.955f, w2 = 2f * Mathf.PI * 2400f / Rate;
        float a1 = 2f * r1 * Mathf.Cos(w1), b1 = -r1 * r1;
        float a2 = 2f * r2 * Mathf.Cos(w2), b2 = -r2 * r2;
        for (int i = 0; i < n; i++)
        {
            float x = (float)rnd.NextDouble() * 2f - 1f;
            float o1 = x * 0.05f + a1 * y1 + b1 * y2; y2 = y1; y1 = o1;
            float o2 = x * 0.04f + a2 * z1 + b2 * z2; z2 = z1; z1 = o2;
            float t = (float)i / Rate;
            float wobble = 1f + 0.2f * Mathf.Sin(2f * Mathf.PI * 8f * t);
            s[i] = (o1 + o2 * 0.6f) * wobble;
        }
        Crossfade(s, Rate / 10);
        Normalize(s, 0.65f);
        return s;
    }

    // ---------- 煞車：下滑音高的尖銳聲 ----------
    static float[] Brake()
    {
        int n = (int)(Rate * 0.6f);
        var s = new float[n];
        var rnd = new System.Random(11);
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float freq = Mathf.Lerp(2600f, 1700f, t / 0.6f);
            phase += 2f * Mathf.PI * freq / Rate;
            float env = Mathf.Exp(-t * 6f) * Mathf.Clamp01(t * 60f);
            float v = Mathf.Sin(phase) * 0.8f + ((float)rnd.NextDouble() * 2f - 1f) * 0.3f;
            s[i] = v * env;
        }
        Normalize(s, 0.7f);
        return s;
    }

    // ---------- 汽車喇叭：兩個不諧和音程疊在一起（實車就是雙音喇叭） ----------
    static float[] Horn()
    {
        int n = (int)(Rate * 0.9f);
        var s = new float[n];
        // 常見雙音喇叭約 400Hz + 500Hz（大三度略偏），刻意不完全協和才刺耳
        float[] f = { 400f, 500f };
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Clamp01(t * 60f) * Mathf.Clamp01((0.9f - t) * 12f);
            float v = 0f;
            foreach (var freq in f)
                for (int h = 1; h <= 6; h++)                    // 方波化 → 喇叭的金屬刺耳感
                    v += Mathf.Sin(2f * Mathf.PI * freq * h * t) / h;
            s[i] = (float)Math.Tanh(v * 0.5f) * env;
        }
        Normalize(s, 0.8f);
        return s;
    }

    // ---------- 回火放砲：排氣管中未燃燒混合氣爆燃 ----------
    /// 高轉收油或升檔時，未燃燒的油氣進到高溫排氣管引爆。
    /// 聲音特徵：極銳利的爆裂前緣 + 低頻膨脹 + 劈啪碎響尾巴。
    static float[] Backfire()
    {
        int n = (int)(Rate * 0.7f);
        var s = new float[n];
        var rnd = new System.Random(1337);

        // 排氣管共鳴（比引擎聲的共鳴更長更響）
        float r = 0.9975f, w = 2f * Mathf.PI * 150f / Rate;
        float a = 2f * r * Mathf.Cos(w), b = -r * r;
        float y1 = 0f, y2 = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;

            // 主爆：極短的高能量衝擊
            float blast = Mathf.Exp(-t * 90f) * ((float)rnd.NextDouble() * 2f - 1f);

            // 劈啪：連續的小爆，前 250ms，越後面越稀疏
            float crackleEnv = Mathf.Exp(-t * 9f);
            float crackle = 0f;
            if (rnd.NextDouble() < 0.035 * crackleEnv * 12f)
                crackle = ((float)rnd.NextDouble() * 2f - 1f) * crackleEnv;

            float excite = blast * 1.4f + crackle * 0.9f;
            float o = excite + a * y1 + b * y2; y2 = y1; y1 = o;

            // 低頻膨脹（「砰」的體感）
            float boom = 1.1f * Mathf.Exp(-t * 16f) * Mathf.Sin(2f * Mathf.PI * (75f * Mathf.Exp(-t * 12f) + 38f) * t);

            s[i] = (float)Math.Tanh((o * 0.5f + boom) * 1.2f);
        }
        Normalize(s, 0.95f);
        return s;
    }

    /// 鋸齒波：諧波比正弦豐富，主旋律才切得出來
    static float Saw(float freq, float t)
    {
        float phase = freq * t;
        return 2f * (phase - Mathf.Floor(phase + 0.5f));
    }

    // ---------- 撞擊：金屬板材的低頻衝擊 + 高頻碎裂 ----------
    static float[] Impact()
    {
        int n = (int)(Rate * 0.55f);
        var s = new float[n];
        var rnd = new System.Random(77);

        // 三個共振峰模擬鈑金被打到的金屬味
        float[] modes = { 190f, 640f, 1830f };
        float[] decay = { 12f, 20f, 34f };
        float[] gain = { 1.0f, 0.55f, 0.30f };

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float v = 0f;

            // 低頻「碰」：瞬間衝擊
            v += 1.15f * Mathf.Exp(-t * 26f) * Mathf.Sin(2f * Mathf.PI * (95f * Mathf.Exp(-t * 30f) + 42f) * t);

            // 金屬共振
            for (int m = 0; m < modes.Length; m++)
                v += gain[m] * Mathf.Exp(-t * decay[m]) * Mathf.Sin(2f * Mathf.PI * modes[m] * t);

            // 碎裂噪音（前 60ms）
            float crackle = Mathf.Exp(-t * 55f);
            v += 0.7f * crackle * ((float)rnd.NextDouble() * 2f - 1f);

            s[i] = (float)Math.Tanh(v * 0.85f);
        }
        Normalize(s, 0.92f);
        return s;
    }

    // ---------- 熱血 BGM：150BPM Em-C-G-D 動力搖滾循環 ----------
    static float[] Bgm()
    {
        float bpm = 150f;
        float beat = 60f / bpm;               // 0.4s
        int bars = 32;                        // 加長到 51.2 秒，A/B 段交替，開久了才不會膩
        int n = (int)(Rate * beat * 4 * bars);
        var s = new float[n];
        var rnd = new System.Random(42);

        // 和弦根音（E2 C2 G2 D2，各 2 小節）
        float[] roots = { 82.41f, 65.41f, 98.00f, 73.42f };

        // 主旋律：E 小調五聲音階即興句（每拍一音，2 小節一句）
        float[] pent = { 329.63f, 392.00f, 440.00f, 493.88f, 587.33f, 659.25f }; // E4 G4 A4 B4 D5 E5
        int[] riff = { 0, 2, 3, 5, 4, 3, 2, 1 };

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float beatPos = t / beat;               // 目前第幾拍
            int beatIdx = (int)beatPos;
            float inBeat = (beatPos - beatIdx) * beat; // 拍內秒數
            int chord = (beatIdx / 8) % 4;          // 每 2 小節換和弦
            float root = roots[chord];

            int bar = beatIdx / 4;
            bool sectionB = (bar / 8) % 2 == 1;     // 每 8 小節切換 A/B 段
            bool isFillBar = (bar % 8) == 7;        // 每 8 小節最後一小節加過門

            float v = 0f;

            // 八分音符推進 Bass
            float inEighth = (beatPos * 2f - (int)(beatPos * 2f)) * beat * 0.5f;
            bool offbeat = ((int)(beatPos * 2f)) % 2 == 1;
            float bassF = offbeat ? root * 2f : root;
            float bassEnv = Mathf.Exp(-inEighth * 14f);
            v += 0.30f * bassEnv * ((float)Math.Tanh(2.2 * Mathf.Sin(2f * Mathf.PI * bassF * t)));

            // 強力和弦鋪底（root+5th+octave，微失諧）
            float fifth = root * 1.5f, oct = root * 2f;
            float pad = Mathf.Sin(2f * Mathf.PI * root * 2f * t) + Mathf.Sin(2f * Mathf.PI * fifth * 2f * t)
                      + Mathf.Sin(2f * Mathf.PI * oct * 2f * t) + Mathf.Sin(2f * Mathf.PI * root * 2.006f * t);
            v += 0.085f * pad;

            // 主旋律：B 段高八度並加上三連音感，做出段落對比
            int riffNote = riff[beatIdx % 8];
            float leadF = pent[riffNote] * (sectionB ? 2f : 1f);
            float leadEnv = Mathf.Exp(-inBeat * (sectionB ? 7f : 5f)) * Mathf.Clamp01(inBeat * 80f);
            float vib = 1f + 0.004f * Mathf.Sin(2f * Mathf.PI * 5.5f * t);
            // 三個微失諧鋸齒疊成 supersaw，比單一正弦厚
            float saw = 0f;
            for (int d = -1; d <= 1; d++)
                saw += Saw(leadF * vib * (1f + d * 0.0035f), t);
            v += (sectionB ? 0.10f : 0.13f) * leadEnv * (float)Math.Tanh(saw * 0.9f);

            // 大鼓：每拍，過門小節改成八分
            float kickEnv = Mathf.Exp(-inBeat * 22f);
            float kickF = 130f * Mathf.Exp(-inBeat * 26f) + 45f;
            v += 0.5f * kickEnv * Mathf.Sin(2f * Mathf.PI * kickF * inBeat);

            // 小鼓：2、4 拍；過門小節每八分都打
            bool snareHit = (beatIdx % 4 == 1 || beatIdx % 4 == 3);
            if (isFillBar && beatIdx % 4 >= 2) snareHit = true;
            if (snareHit)
            {
                float sn = (float)rnd.NextDouble() * 2f - 1f;
                float env = isFillBar ? Mathf.Exp(-inEighth * 26f) : Mathf.Exp(-inBeat * 24f);
                v += 0.28f * sn * env;
            }

            // Hi-hat：八分音符，每 4 拍一次開鈸
            bool openHat = (beatIdx % 4 == 3) && offbeat;
            float hatEnv = openHat ? Mathf.Exp(-inEighth * 9f) : Mathf.Exp(-inEighth * 70f);
            float hat = (float)rnd.NextDouble() * 2f - 1f;
            v += (openHat ? 0.07f : 0.09f) * hat * hatEnv;

            s[i] = (float)Math.Tanh(v * 1.25f);
        }
        Crossfade(s, Rate / 20);
        Normalize(s, 0.8f);
        return s;
    }

    // ---------- 工具 ----------
    static void Normalize(float[] s, float peak)
    {
        float max = 0f;
        foreach (var v in s) max = Mathf.Max(max, Mathf.Abs(v));
        if (max < 1e-5f) return;
        float k = peak / max;
        for (int i = 0; i < s.Length; i++) s[i] *= k;
    }

    /// 頭尾交叉淡化，確保循環無爆音
    static void Crossfade(float[] s, int fade)
    {
        for (int i = 0; i < fade; i++)
        {
            float a = (float)i / fade;
            s[i] = s[i] * a + s[s.Length - fade + i] * (1f - a);
        }
    }

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
            bw.Write((short)1);        // PCM
            bw.Write((short)1);        // mono
            bw.Write(Rate);
            bw.Write(Rate * 2);        // byte rate
            bw.Write((short)2);        // block align
            bw.Write((short)16);       // bits
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(dataLen);
            foreach (var v in samples)
                bw.Write((short)(Mathf.Clamp(v, -1f, 1f) * 32760f));
        }
    }
}
