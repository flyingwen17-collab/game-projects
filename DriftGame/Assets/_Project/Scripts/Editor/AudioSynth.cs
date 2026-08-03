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
        AssetDatabase.Refresh();
        Debug.Log("[AudioSynth] audio generated");
    }

    // ---------- 引擎：整數倍諧波 → 無縫循環 ----------
    static float[] Engine()
    {
        int n = Rate; // 1 秒
        var s = new float[n];
        float f0 = 55f;
        float[] harm = { 1f, 0.6f, 0.42f, 0.3f, 0.22f, 0.14f };
        int[] mult = { 1, 2, 3, 4, 6, 8 };
        var rnd = new System.Random(7);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float v = 0f;
            for (int h = 0; h < harm.Length; h++)
                v += harm[h] * Mathf.Sin(2f * Mathf.PI * f0 * mult[h] * t);
            // 點火脈動感
            v *= 1f + 0.25f * Mathf.Sin(2f * Mathf.PI * f0 * 0.5f * t);
            v += 0.05f * ((float)rnd.NextDouble() * 2f - 1f);
            s[i] = (float)Math.Tanh(v * 0.9f);
        }
        Normalize(s, 0.75f);
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

    // ---------- 熱血 BGM：150BPM Em-C-G-D 動力搖滾循環 ----------
    static float[] Bgm()
    {
        float bpm = 150f;
        float beat = 60f / bpm;               // 0.4s
        int bars = 8;
        int n = (int)(Rate * beat * 4 * bars); // 12.8s
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

            // 主旋律
            int riffNote = riff[beatIdx % 8];
            float leadF = pent[riffNote];
            float leadEnv = Mathf.Exp(-inBeat * 5f) * Mathf.Clamp01(inBeat * 80f);
            float vib = 1f + 0.004f * Mathf.Sin(2f * Mathf.PI * 5.5f * t);
            v += 0.14f * leadEnv * ((float)Math.Tanh(1.8 * Mathf.Sin(2f * Mathf.PI * leadF * vib * t)));

            // 大鼓：每拍
            float kickEnv = Mathf.Exp(-inBeat * 22f);
            float kickF = 130f * Mathf.Exp(-inBeat * 26f) + 45f;
            v += 0.5f * kickEnv * Mathf.Sin(2f * Mathf.PI * kickF * inBeat);

            // 小鼓：2、4 拍
            if (beatIdx % 4 == 1 || beatIdx % 4 == 3)
            {
                float sn = (float)rnd.NextDouble() * 2f - 1f;
                v += 0.28f * sn * Mathf.Exp(-inBeat * 24f);
            }

            // Hi-hat：八分音符
            float hatEnv = Mathf.Exp(-inEighth * 70f);
            float hat = (float)rnd.NextDouble() * 2f - 1f;
            v += 0.09f * hat * hatEnv;

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
