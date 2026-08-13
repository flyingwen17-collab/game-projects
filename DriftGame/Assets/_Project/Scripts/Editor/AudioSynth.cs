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
        GenerateEngineLayers();
        WriteWav(AudioDir + "/beep_count.wav", Beep(880f, 0.20f));
        WriteWav(AudioDir + "/beep_go.wav", Beep(1318.5f, 0.55f));
        WriteWav(AudioDir + "/gearshift.wav", GearShift());
        WriteWav(AudioDir + "/backfire_2.wav", BackfireVariant(4242, 130f, 11f));
        WriteWav(AudioDir + "/backfire_3.wav", BackfireVariant(90210, 175f, 7f));
        AssetDatabase.Refresh();
        Debug.Log("[AudioSynth] audio generated");
    }

    // ---------- 真實引擎取樣 → 多轉速分層 ----------
    /// 引擎聲要「真」的關鍵不是單一取樣的品質，而是**多轉速分層交叉混音**：
    /// 一個 loop 從 0.55 拉到 2.45 倍音高會像割草機；
    /// 低/中/高三層各只拉 ±30%，聽感就是換檔會呼吸的真引擎。
    /// 來源：Trigger Rally 專案的真實引擎錄音（CC-BY 3.0, qubodup），入庫於 _Assets\audio_cc0。
    /// 來源檔缺席時退回合成脈衝引擎（不同基頻），管線在任何機器都能重跑。
    public static void GenerateEngineLayers()
    {
        string src = Path.Combine(Application.dataPath,
            "../../_Assets/audio_cc0/engine-loop/engine-loop-1-normalized.wav");

        float[] mono = File.Exists(src) ? ReadWavMono(src) : null;

        if (mono != null && mono.Length > Rate / 2)
        {
            // 重取樣 = 音高平移（連時長一起變，對 loop 完全合法）
            WriteWav(AudioDir + "/engine_low.wav", Resample(mono, 0.52f));
            WriteWav(AudioDir + "/engine_mid.wav", Resample(mono, 1.0f));
            WriteWav(AudioDir + "/engine_high.wav", Resample(mono, 1.72f));
            Debug.Log("[AudioSynth] 引擎三層由真實取樣產生: " + src);
        }
        else
        {
            WriteWav(AudioDir + "/engine_low.wav", EngineAt(62f));
            WriteWav(AudioDir + "/engine_mid.wav", EngineAt(120f));
            WriteWav(AudioDir + "/engine_high.wav", EngineAt(215f));
            Debug.LogWarning("[AudioSynth] 找不到真實引擎取樣，改用合成分層: " + src);
        }
    }

    /// 讀 16-bit PCM WAV → 混成 mono float。用 chunk 走訪，容忍 LIST 等額外區塊。
    static float[] ReadWavMono(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44) return null;
            int channels = 1, bits = 16, dataOfs = -1, dataLen = 0;
            int p = 12;   // 跳過 RIFF____WAVE
            while (p + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, p, 4);
                int len = System.BitConverter.ToInt32(bytes, p + 4);
                if (id == "fmt ")
                {
                    channels = System.BitConverter.ToInt16(bytes, p + 10);
                    bits = System.BitConverter.ToInt16(bytes, p + 22);
                }
                else if (id == "data") { dataOfs = p + 8; dataLen = len; break; }
                p += 8 + len + (len & 1);
            }
            if (dataOfs < 0 || bits != 16) return null;

            int frames = dataLen / 2 / channels;
            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += System.BitConverter.ToInt16(bytes, dataOfs + (i * channels + c) * 2);
                mono[i] = sum / channels / 32768f;
            }
            return mono;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[AudioSynth] WAV 讀取失敗: " + e.Message);
            return null;
        }
    }

    /// 線性插值重取樣：rate>1 音高變高、時長變短
    static float[] Resample(float[] src, float rate)
    {
        int n = Mathf.Max(64, (int)(src.Length / rate));
        var outp = new float[n];
        for (int i = 0; i < n; i++)
        {
            float x = i * rate;
            int i0 = (int)x;
            float f = x - i0;
            outp[i] = src[i0 % src.Length] * (1f - f) + src[(i0 + 1) % src.Length] * f;
        }
        Crossfade(outp, Rate / 100);
        Normalize(outp, 0.8f);
        return outp;
    }

    /// 指定點火頻率的合成引擎（給真實取樣缺席時的退路）
    static float[] EngineAt(float f0)
    {
        int n = Rate;
        var s = new float[n];
        var rnd = new System.Random((int)f0);
        float r1 = 0.9955f, w1 = 2f * Mathf.PI * (f0 * 1.8f) / Rate;
        float r2 = 0.988f, w2 = 2f * Mathf.PI * (f0 * 7.2f) / Rate;
        float a1 = 2f * r1 * Mathf.Cos(w1), b1 = -r1 * r1;
        float a2 = 2f * r2 * Mathf.Cos(w2), b2 = -r2 * r2;
        float y1 = 0f, y2 = 0f, z1 = 0f, z2 = 0f;
        float pulsePeriod = Rate / f0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float pulsePos = (i % pulsePeriod) / pulsePeriod;
            float excite = pulsePos < 0.08f ? Mathf.Exp(-pulsePos * 45f) * (1f - pulsePos / 0.08f) : 0f;
            excite += 0.02f * ((float)rnd.NextDouble() * 2f - 1f);
            float o1 = excite + a1 * y1 + b1 * y2; y2 = y1; y1 = o1;
            float o2 = excite + a2 * z1 + b2 * z2; z2 = z1; z1 = o2;
            float sub = 0.35f * Mathf.Sin(2f * Mathf.PI * (f0 / 4f) * t);
            s[i] = (float)Math.Tanh((o1 * 0.55f + o2 * 0.18f + sub) * 1.5f);
        }
        Crossfade(s, Rate / 200);
        Normalize(s, 0.78f);
        return s;
    }

    // ---------- 起跑倒數嗶聲：F1 式短嗶 ×3 + 長嗶起跑 ----------
    static float[] Beep(float freq, float dur)
    {
        int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Clamp01(t * 400f) * Mathf.Clamp01((dur - t) * 30f);
            // 基頻 + 少量 3 次諧波：電子計時器的「嗶」而不是純正弦的悶
            float v = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.22f * Mathf.Sin(2f * Mathf.PI * freq * 3f * t);
            s[i] = v * env * 0.75f;
        }
        return s;
    }

    // ---------- 換檔頓挫：變速箱接合的機械「喀」聲 ----------
    static float[] GearShift()
    {
        int n = (int)(Rate * 0.14f);
        var s = new float[n];
        var rnd = new System.Random(555);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            // 低頻悶擊（傳動系受衝）
            float thump = 0.9f * Mathf.Exp(-t * 60f) * Mathf.Sin(2f * Mathf.PI * 120f * Mathf.Exp(-t * 18f) * t);
            // 金屬咔嗒（撥叉入檔）
            float click = 0.5f * Mathf.Exp(-t * 220f) * ((float)rnd.NextDouble() * 2f - 1f);
            float ring = 0.18f * Mathf.Exp(-t * 90f) * Mathf.Sin(2f * Mathf.PI * 2100f * t);
            s[i] = (float)Math.Tanh(thump + click + ring);
        }
        Normalize(s, 0.7f);
        return s;
    }

    /// 回火變體：不同亂數種子與共鳴/衰減 → 每次放砲不重樣
    static float[] BackfireVariant(int seed, float resonHz, float crackleDecay)
    {
        int n = (int)(Rate * 0.7f);
        var s = new float[n];
        var rnd = new System.Random(seed);
        float r = 0.9975f, w = 2f * Mathf.PI * resonHz / Rate;
        float a = 2f * r * Mathf.Cos(w), b = -r * r;
        float y1 = 0f, y2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float blast = Mathf.Exp(-t * 90f) * ((float)rnd.NextDouble() * 2f - 1f);
            float crackleEnv = Mathf.Exp(-t * crackleDecay);
            float crackle = 0f;
            if (rnd.NextDouble() < 0.035 * crackleEnv * 12f)
                crackle = ((float)rnd.NextDouble() * 2f - 1f) * crackleEnv;
            float excite = blast * 1.4f + crackle * 0.9f;
            float o = excite + a * y1 + b * y2; y2 = y1; y1 = o;
            float boom = 1.1f * Mathf.Exp(-t * 16f) * Mathf.Sin(2f * Mathf.PI * (75f * Mathf.Exp(-t * 12f) + 38f) * t);
            s[i] = (float)Math.Tanh((o * 0.5f + boom) * 1.2f);
        }
        Normalize(s, 0.95f);
        return s;
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
