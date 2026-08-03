using UnityEngine;

/// Pacejka「魔術公式」輪胎模型（簡化版）。
///
/// 這是甩尾手感的來源：輪胎力不是線性的，它在某個滑移角達到峰值後會「掉下來」。
/// 峰值之後抓地力下降，車尾就會滑出去；要救回來必須反打把滑移角拉回峰值以內。
/// 這個「過峰值就失控、回峰值就抓回」的非線性，正是真實甩尾的手感，
/// 也是 WheelCollider 內建線性摩擦做不出來的東西。
///
///   F = D · sin( C · atan( B·s − E·( B·s − atan(B·s) ) ) )
///
///   B 剛性因子、C 形狀因子、D 峰值(=μ·Fz)、E 曲率因子
public static class TireModel
{
    // 側向（滑移角，單位：弧度）
    const float LatB = 9.5f;
    const float LatC = 1.42f;
    const float LatE = 0.97f;

    // 縱向（滑移率，無單位）
    const float LongB = 11.0f;
    const float LongC = 1.62f;
    const float LongE = 0.96f;

    /// 名目載荷（單輪約承受的重量），載荷敏感度以此為基準
    public const float NominalLoadN = 3500f;

    /// 魔術公式本體，回傳 -1..1 的正規化力（再乘上 D 得到實際牛頓）
    static float Magic(float slip, float b, float c, float e)
    {
        float bs = b * slip;
        return Mathf.Sin(c * Mathf.Atan(bs - e * (bs - Mathf.Atan(bs))));
    }

    /// 載荷敏感度：載重加倍時抓地力「不會」加倍，這是重量轉移能改變操控的物理根據。
    public static float LoadSensitiveMu(float baseMu, float loadN, float sensitivity)
    {
        float ratio = Mathf.Clamp(loadN / NominalLoadN, 0.05f, 3f);
        return baseMu * (1f - sensitivity * (ratio - 1f));
    }

    /// 計算單輪的胎面力。
    /// slipAngleRad：滑移角（胎面前進方向與實際速度方向的夾角）
    /// slipRatio   ：滑移率（輪速與地速的差，正=打滑加速、負=鎖死）
    /// loadN       ：垂直載荷
    /// 回傳 x=縱向力（前進為正）、y=側向力
    public static Vector2 Compute(float slipAngleRad, float slipRatio, float loadN,
                                  float baseMu, float loadSensitivity, float surfaceGrip = 1f)
    {
        if (loadN <= 1f) return Vector2.zero;

        float mu = LoadSensitiveMu(baseMu, loadN, loadSensitivity) * surfaceGrip;
        float peak = mu * loadN;

        float fy = -Magic(slipAngleRad, LatB, LatC, LatE) * peak;
        float fx = Magic(slipRatio, LongB, LongC, LongE) * peak;

        // 摩擦橢圓：縱向與側向共用同一份抓地力預算，
        // 全油門時側向力會變小 —— 這就是「油門控制甩尾角度」的原理。
        float combined = Mathf.Sqrt(fx * fx + fy * fy);
        if (combined > peak && combined > 1e-4f)
        {
            float scale = peak / combined;
            fx *= scale;
            fy *= scale;
        }

        return new Vector2(fx, fy);
    }

    /// 給定載荷下的最大可用抓地力（做特效門檻與 HUD 用）
    public static float PeakForce(float loadN, float baseMu, float loadSensitivity)
    {
        return LoadSensitiveMu(baseMu, loadN, loadSensitivity) * loadN;
    }

    /// 縱向胎力在「當前滑移率處」的局部斜率 dFx/dκ（數值中央差分）。
    ///
    /// 用途：輪胎角速度的積分是剛性系統 —— 低速時滑移率對輪速極度敏感，
    /// 顯式積分會發散成正負震盪。半隱式積分需要這個斜率來穩定。
    ///
    /// 必須取「局部」斜率而不是 κ=0 的斜率：輪胎飽和時真實斜率接近 0，
    /// 若一律套用 κ=0 的巨大斜率，等效慣量會被灌大十幾倍，
    /// 輪子反應遲鈍到跟不上車速 → 滑移率變負 → 胎力反而變成煞車力。
    public static float LongSlopeAt(float slipRatio, float peakForce)
    {
        const float h = 0.02f;
        float f1 = Magic(slipRatio + h, LongB, LongC, LongE);
        float f0 = Magic(slipRatio - h, LongB, LongC, LongE);
        return Mathf.Abs((f1 - f0) / (2f * h)) * peakForce;
    }
}
