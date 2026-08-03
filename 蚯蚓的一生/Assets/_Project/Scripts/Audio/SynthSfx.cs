using System.Collections.Generic;
using UnityEngine;

/// 程式合成音效：不用音檔，直接產生波形
public static class SynthSfx
{
    const int SR = 44100;
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

    public static void Play(string name, float volume = 1f, float pitch = 1f)
    {
        var clip = Get(name);
        if (clip == null) return;
        var go = new GameObject("sfx_" + name);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.pitch = pitch;
        src.spatialBlend = 0f;
        src.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }

    public static void PlayAt(string name, Vector3 pos, float volume = 1f, float pitch = 1f)
    {
        var clip = Get(name);
        if (clip == null) return;
        var go = new GameObject("sfx_" + name);
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.pitch = pitch;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.maxDistance = 28f;
        src.dopplerLevel = 0f;
        src.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }

    static AudioClip Get(string name)
    {
        if (clips.TryGetValue(name, out var c) && c != null) return c;
        float[] data = Synth(name);
        if (data == null) return null;
        var clip = AudioClip.Create(name, data.Length, 1, SR, false);
        clip.SetData(data, 0);
        clips[name] = clip;
        return clip;
    }

    static float[] Synth(string name)
    {
        switch (name)
        {
            case "dig":      return Noise(0.22f, 900f, 0.5f);                      // 沙沙鑽土
            case "surface":  return Chirp(180f, 420f, 0.18f, 0.35f, Wave.Sine);    // 破土而出
            case "eat":      return Chirp(600f, 950f, 0.1f, 0.35f, Wave.Sine);     // 吃到食物
            case "alert":    return Cluck(700f);                                    // 雞警覺「咕?」
            case "cluck":    return Cluck(560f);
            case "crow":     return Crow();                                         // 公雞啼叫
            case "peck":     return Thud();                                         // 啄地
            case "death":    return Sting(new[] { 392f, 311.1f, 233.1f }, 0.22f);   // 死亡下行
            case "closecall":return Chirp(300f, 1200f, 0.25f, 0.3f, Wave.Saw);      // 險過急升
            case "dash":     return Chirp(500f, 200f, 0.2f, 0.3f, Wave.Saw);        // 地下突進
            case "decoy":    return Chirp(400f, 700f, 0.15f, 0.25f, Wave.Square);   // 放誘餌
            case "pop":      return Chirp(900f, 300f, 0.1f, 0.3f, Wave.Sine);       // 誘餌破掉
            default: return null;
        }
    }

    enum Wave { Sine, Square, Saw }

    static float Osc(Wave w, float phase)
    {
        switch (w)
        {
            case Wave.Square: return Mathf.Sign(Mathf.Sin(phase)) * 0.5f;
            case Wave.Saw:    return Mathf.Repeat(phase / (2f * Mathf.PI), 1f) * 2f - 1f;
            default:          return Mathf.Sin(phase);
        }
    }

    static float[] Chirp(float f0, float f1, float dur, float amp, Wave w)
    {
        int n = (int)(SR * dur);
        var d = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float f = Mathf.Lerp(f0, f1, t);
            phase += 2f * Mathf.PI * f / SR;
            float env = Mathf.Sin(t * Mathf.PI); // 淡入淡出
            d[i] = Osc(w, phase) * env * amp;
        }
        return d;
    }

    static float[] Noise(float dur, float cutoffSim, float amp)
    {
        int n = (int)(SR * dur);
        var d = new float[n];
        var rnd = new System.Random(7);
        float last = 0f;
        float k = Mathf.Clamp01(cutoffSim / SR * 6f); // 簡易低通
        for (int i = 0; i < n; i++)
        {
            float white = (float)(rnd.NextDouble() * 2.0 - 1.0);
            last += k * (white - last);
            float t = i / (float)n;
            float env = (1f - t) * Mathf.Min(1f, t * 20f);
            d[i] = last * env * amp * 2.2f;
        }
        return d;
    }

    static float[] Cluck(float baseF)
    {
        // 兩短音的「咕咕」
        int n = (int)(SR * 0.28f);
        var d = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float seg = t < 0.12f ? t / 0.12f : (t - 0.16f) / 0.12f;
            bool active = t < 0.12f || (t >= 0.16f && t < 0.28f);
            float f = baseF * (t < 0.12f ? 1f : 0.85f) * (1f + 0.15f * Mathf.Sin(seg * Mathf.PI));
            phase += 2f * Mathf.PI * f / SR;
            float env = active ? Mathf.Sin(Mathf.Clamp01(seg) * Mathf.PI) : 0f;
            d[i] = (Mathf.Sin(phase) * 0.7f + Mathf.Sign(Mathf.Sin(phase * 2f)) * 0.15f) * env * 0.4f;
        }
        return d;
    }

    static float[] Crow()
    {
        // 咕—咕—咕——！四段公雞啼
        float[] freqs = { 620f, 780f, 700f, 880f };
        float[] durs = { 0.14f, 0.14f, 0.12f, 0.42f };
        float total = 0.9f;
        int n = (int)(SR * total);
        var d = new float[n];
        float phase = 0f;
        float t0 = 0f;
        for (int s = 0; s < 4; s++)
        {
            int start = (int)(SR * t0);
            int len = (int)(SR * durs[s]);
            for (int i = 0; i < len && start + i < n; i++)
            {
                float t = i / (float)len;
                float vib = 1f + 0.06f * Mathf.Sin(i / (float)SR * 2f * Mathf.PI * 9f);
                float f = freqs[s] * vib * (s == 3 ? (1f - t * 0.25f) : 1f);
                phase += 2f * Mathf.PI * f / SR;
                float env = Mathf.Sin(t * Mathf.PI);
                d[start + i] = (Mathf.Sin(phase) * 0.55f + Osc(Wave.Saw, phase) * 0.2f) * env * 0.5f;
            }
            t0 += durs[s] + 0.06f;
        }
        return d;
    }

    static float[] Thud()
    {
        int n = (int)(SR * 0.16f);
        var d = new float[n];
        float phase = 0f;
        var rnd = new System.Random(3);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float f = Mathf.Lerp(150f, 60f, t);
            phase += 2f * Mathf.PI * f / SR;
            float click = i < SR * 0.01f ? (float)(rnd.NextDouble() * 2.0 - 1.0) * 0.4f : 0f;
            d[i] = (Mathf.Sin(phase) * (1f - t) + click) * 0.55f;
        }
        return d;
    }

    static float[] Sting(float[] notes, float noteDur)
    {
        int n = (int)(SR * noteDur * notes.Length);
        var d = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            int idx = Mathf.Min((int)(i / (SR * noteDur)), notes.Length - 1);
            float t = (i - idx * SR * noteDur) / (SR * noteDur);
            phase += 2f * Mathf.PI * notes[idx] / SR;
            float env = Mathf.Exp(-t * 3f);
            d[i] = (Mathf.Sin(phase) + 0.3f * Mathf.Sin(phase * 2f)) * env * 0.4f;
        }
        return d;
    }
}
