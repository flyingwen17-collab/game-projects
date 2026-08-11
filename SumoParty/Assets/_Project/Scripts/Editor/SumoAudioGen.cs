using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 程序合成整套音訊到 Assets/_Project/Audio/（流程 MD §4.8）。
///
/// 定位：**佔位但可出貨**的合成音。之後從 freesound(CC0) 挑真實錄音時，
/// 用相同檔名覆蓋即可，SumoAudioDirector 一行不用改。
/// 全部自製合成 → 零授權風險，CREDITS 記「程序合成」。
///
///   Unity.exe -batchmode -quit -projectPath X -executeMethod SumoAudioGen.Generate
/// </summary>
public static class SumoAudioGen
{
    const int SR = 44100;
    const string Dir = "Assets/_Project/Audio";
    static System.Random rng;

    [MenuItem("Sumo/生成音訊(程序合成)")]
    public static void Generate()
    {
        rng = new System.Random(20260809);   // 固定種子：每次生成結果一致，可重現
        Directory.CreateDirectory(Dir);

        Save("impact_light", ImpactBody(0.16f, 900f, 0.5f));
        Save("impact_mid", ImpactBody(0.22f, 520f, 0.8f));
        Save("impact_heavy", ImpactBody(0.32f, 300f, 1.0f));
        Save("slap", Slap());
        Save("stomp", Stomp());
        Save("shout_a", Shout(118f, 0.42f));
        Save("shout_b", Shout(96f, 0.55f));
        Save("grunt", Shout(80f, 0.28f));
        Save("crowd_loop", CrowdLoop(6f));
        Save("crowd_roar", CrowdRoar(2.6f));
        Save("taiko_base", TaikoBase());
        Save("taiko_intense", TaikoIntense());
        Save("grab", Grab());
        Save("creak", Creak());

        AssetDatabase.Refresh();

        // 匯入設定：短音效解壓載入、迴圈長音串流（流程 MD §4.8）
        foreach (var f in Directory.GetFiles(Dir, "*.wav"))
        {
            var imp = (AudioImporter)AssetImporter.GetAtPath(f.Replace('\\', '/'));
            if (imp == null) continue;
            var s = imp.defaultSampleSettings;
            bool stream = f.Contains("crowd_loop") || f.Contains("taiko");
            s.loadType = stream ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            imp.defaultSampleSettings = s;
            imp.SaveAndReimport();
        }
        Debug.Log($"[SumoAudioGen] 12 個音檔生成完畢 → {Dir}");
    }

    // ---------- 合成 ----------

    /// <summary>肉體撞擊：低頻 thump + 帶通噪音，衝量越大越低沉。</summary>
    static float[] ImpactBody(float dur, float noiseCut, float lowAmp)
    {
        int n = (int)(SR * dur);
        var s = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 22f);
            // thump：60→38Hz 下滑正弦
            float f0 = Mathf.Lerp(64f, 38f, t / dur);
            float thump = Mathf.Sin(2f * Mathf.PI * f0 * t) * env * lowAmp;
            // 皮肉聲：低通噪音
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float k = Mathf.Clamp01(noiseCut / SR * 2f * Mathf.PI);
            lp += k * (noise - lp);
            s[i] = Mathf.Clamp(thump + lp * env * 0.9f, -1f, 1f) * 0.9f;
        }
        return s;
    }

    /// <summary>張手拍擊（突き 命中）：短促高頻爆點。</summary>
    static float[] Slap()
    {
        int n = (int)(SR * 0.09f);
        var s = new float[n];
        float bp = 0f, bpPrev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 90f);
            float noise = (float)(rng.NextDouble() * 2 - 1);
            // 粗糙帶通 ~2kHz
            bp += 0.28f * (noise - bp);
            float hi = bp - bpPrev; bpPrev = bp;
            s[i] = Mathf.Clamp(hi * 14f * env, -1f, 1f) * 0.8f;
        }
        return s;
    }

    /// <summary>四股踏地。</summary>
    static float[] Stomp()
    {
        int n = (int)(SR * 0.3f);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 16f);
            float f0 = Mathf.Lerp(90f, 45f, t * 4f);
            s[i] = Mathf.Sin(2f * Mathf.PI * f0 * t) * env * 0.85f;
        }
        return s;
    }

    /// <summary>力士吼聲：諧波堆疊 + 音高下滑 + 氣音。粗糙但有魄力。</summary>
    static float[] Shout(float baseHz, float dur)
    {
        int n = (int)(SR * dur);
        var s = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float p = t / dur;
            float env = Mathf.Sin(Mathf.Clamp01(p / 0.12f) * Mathf.PI * 0.5f) * Mathf.Exp(-Mathf.Max(0f, p - 0.4f) * 5f);
            float f0 = baseHz * (1.06f - 0.18f * p) * (1f + 0.015f * Mathf.Sin(2f * Mathf.PI * 5.5f * t));
            float v = 0f;
            for (int h = 1; h <= 9; h++)          // 鋸齒感的諧波
                v += Mathf.Sin(2f * Mathf.PI * f0 * h * t) / h;
            float breath = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.10f * (breath - lp);
            s[i] = Mathf.Clamp((v * 0.30f + lp * 0.5f) * env, -1f, 1f) * 0.85f;
        }
        return s;
    }

    /// <summary>觀眾底噪：多層慢調變的低通噪音，頭尾交叉淡化成無縫迴圈。</summary>
    static float[] CrowdLoop(float dur)
    {
        int n = (int)(SR * dur);
        var s = new float[n];
        float lp = 0f;
        float m1 = (float)rng.NextDouble() * 6f, m2 = (float)rng.NextDouble() * 6f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float mod = 0.65f + 0.2f * Mathf.Sin(2f * Mathf.PI * 0.31f * t + m1)
                              + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.83f * t + m2);
            float noise = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.055f * (noise - lp);          // ~400Hz 人聲團感
            s[i] = lp * mod * 0.8f;
        }
        // 無縫迴圈：末端 0.25s 與開頭交叉淡化
        int x = (int)(SR * 0.25f);
        for (int i = 0; i < x; i++)
        {
            float a = i / (float)x;
            s[n - x + i] = s[n - x + i] * (1f - a) + s[i] * a;
        }
        return s;
    }

    /// <summary>觀眾爆發歡呼：噪音湧起 + 高頻口哨感。</summary>
    static float[] CrowdRoar(float dur)
    {
        int n = (int)(SR * dur);
        var s = new float[n];
        float lp = 0f, hp = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float p = t / dur;
            float env = Mathf.Sin(Mathf.Clamp01(p / 0.18f) * Mathf.PI * 0.5f) * Mathf.Exp(-Mathf.Max(0f, p - 0.35f) * 3.2f);
            float noise = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.09f * (noise - lp);
            hp = noise - prev; prev = noise;
            s[i] = Mathf.Clamp((lp * 0.9f + hp * 0.12f) * env, -1f, 1f) * 0.9f;
        }
        return s;
    }

    /// <summary>抓廻し：布料抓握聲（中頻噪音抓合 + 小 thump）。</summary>
    static float[] Grab()
    {
        int n = (int)(SR * 0.16f);
        var s = new float[n];
        float bp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 40f);
            float noise = (float)(rng.NextDouble() * 2 - 1);
            bp += 0.18f * (noise - bp);                     // ~1.2kHz 布料摩擦感
            float thump = Mathf.Sin(2f * Mathf.PI * 85f * t) * Mathf.Exp(-t * 55f) * 0.5f;
            s[i] = Mathf.Clamp((bp * 3.2f + thump) * env, -1f, 1f) * 0.8f;
        }
        return s;
    }

    /// <summary>廻し拉扯的緊繃聲：低頻鋸齒緩慢調變（拉的觸覺回饋）。</summary>
    static float[] Creak()
    {
        int n = (int)(SR * 0.42f);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float p = t / 0.42f;
            float env = Mathf.Sin(Mathf.Clamp01(p / 0.2f) * Mathf.PI * 0.5f) * Mathf.Exp(-Mathf.Max(0f, p - 0.45f) * 6f);
            float f0 = 70f + 26f * Mathf.Sin(2f * Mathf.PI * 3.2f * t);   // 緊繃的顫動
            float v = 0f;
            for (int h = 1; h <= 6; h++) v += Mathf.Sin(2f * Mathf.PI * f0 * h * t) / h;
            float grit = (float)(rng.NextDouble() * 2 - 1) * 0.10f;
            s[i] = Mathf.Clamp((v * 0.32f + grit) * env, -1f, 1f) * 0.7f;
        }
        return s;
    }

    // ---------- 太鼓 ----------

    static void Don(float[] s, float at, float amp, float hz = 120f)
    {
        int start = (int)(SR * at);
        int len = (int)(SR * 0.45f);
        for (int i = 0; i < len && start + i < s.Length; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 11f);
            float f0 = Mathf.Lerp(hz, hz * 0.42f, Mathf.Min(1f, t * 7f));
            float click = i < 130 ? (float)(rng.NextDouble() * 2 - 1) * 0.35f * (1f - i / 130f) : 0f;
            s[start + i] = Mathf.Clamp(s[start + i] + (Mathf.Sin(2f * Mathf.PI * f0 * t) * env + click) * amp, -1f, 1f);
        }
    }

    static void Rim(float[] s, float at, float amp)
    {
        int start = (int)(SR * at);
        int len = (int)(SR * 0.07f);
        float bp = 0f, prev = 0f;
        for (int i = 0; i < len && start + i < s.Length; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 70f);
            float noise = (float)(rng.NextDouble() * 2 - 1);
            bp += 0.4f * (noise - bp);
            float hi = bp - prev; prev = bp;
            s[start + i] = Mathf.Clamp(s[start + i] + hi * 9f * env * amp, -1f, 1f);
        }
    }

    /// <summary>底層：沉穩的 don...don...（2.4s @ 100BPM，無縫循環）。</summary>
    static float[] TaikoBase()
    {
        var s = new float[(int)(SR * 2.4f)];
        Don(s, 0.0f, 0.8f); Don(s, 0.6f, 0.55f);
        Don(s, 1.2f, 0.8f); Don(s, 1.5f, 0.45f); Don(s, 1.8f, 0.65f);
        return s;
    }

    /// <summary>激烈層：密集連打 + 締太鼓，疊在底層上。</summary>
    static float[] TaikoIntense()
    {
        var s = new float[(int)(SR * 2.4f)];
        for (int k = 0; k < 8; k++) Don(s, k * 0.3f, k % 2 == 0 ? 0.7f : 0.4f, 150f);
        for (int k = 0; k < 16; k++) Rim(s, k * 0.15f, k % 4 == 0 ? 0.5f : 0.28f);
        return s;
    }

    // ---------- WAV 輸出（16-bit PCM mono） ----------

    static void Save(string name, float[] samples)
    {
        string path = $"{Dir}/{name}.wav";
        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);
        int byteCount = samples.Length * 2;
        w.Write("RIFF".ToCharArray()); w.Write(36 + byteCount);
        w.Write("WAVE".ToCharArray()); w.Write("fmt ".ToCharArray());
        w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data".ToCharArray()); w.Write(byteCount);
        foreach (var f in samples) w.Write((short)(Mathf.Clamp(f, -1f, 1f) * 32760f));
    }
}
