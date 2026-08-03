using System.IO;
using UnityEditor;
using UnityEngine;

/// 程式合成遊戲貼圖：粒子、無縫平鋪材質、天空漸層、法線圖。
/// 設計原則（對應 AI 繪圖的三大缺陷）：
///   1. 平鋪材質用「週期性雜訊」→ 數學上保證無縫，不會有接縫。
///   2. 只輸出 Albedo 本色，不烘任何光影 → 進 Unity 打光不會雙重打光。
///   3. 法線 / 粗糙度由高度圖用 Sobel 算出 → PBR 材質組完整。
/// 產物同時寫進專案 Textures/Generated 與 ｇａｍｅ\_Refs\ 供檢視。
public static class TextureSynth
{
    const string OutDir = "Assets/_Project/Textures/Generated";

    // _Refs 在 Unity 專案的上兩層（Assets → DriftGame → ｇａｍｅ）
    static string RefsRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../_Refs"));

    [MenuItem("Tools/Drift Game/Generate Textures")]
    public static void GenerateAll()
    {
        Directory.CreateDirectory(OutDir);

        // ---- 粒子：白色形狀 + Alpha 遮罩，黑底思維改用真 Alpha，兩種混合模式都能用 ----
        Save(SmokePuff(512), "smoke_puff", "02_粒子貼圖", alpha: true);
        Save(RadialGlow(256), "glow_radial", "02_粒子貼圖", alpha: true);
        Save(SparkBurst(512), "spark_burst", "02_粒子貼圖", alpha: true);
        Save(DustSoft(256), "dust_soft", "02_粒子貼圖", alpha: true);
        Save(TireStreak(256), "tire_streak", "02_粒子貼圖", alpha: true);

        // ---- 無縫平鋪材質（Albedo + 由高度算出的 Normal）----
        var asphaltH = AsphaltHeight(1024);
        Save(Colorize(asphaltH, new Color(0.20f, 0.20f, 0.21f), new Color(0.40f, 0.40f, 0.42f)),
             "asphalt_albedo", "04_材質貼圖");
        Save(HeightToNormal(asphaltH, 2.0f), "asphalt_normal", "04_材質貼圖", linear: true);

        var dirtH = DirtHeight(1024);
        Save(Colorize(dirtH, new Color(0.28f, 0.20f, 0.13f), new Color(0.52f, 0.40f, 0.27f)),
             "dirt_albedo", "04_材質貼圖");
        Save(HeightToNormal(dirtH, 3.0f), "dirt_normal", "04_材質貼圖", linear: true);

        Save(StylizedGrass(1024), "grass_stylized_albedo", "04_材質貼圖");

        // ---- 天空漸層（黃昏 / 藍調兩組）----
        Save(SkyGradient(64, 512,
                new Color(0.09f, 0.13f, 0.33f), new Color(0.72f, 0.45f, 0.42f), new Color(0.98f, 0.66f, 0.35f)),
             "sky_goldenhour", "05_天空盒");
        Save(SkyGradient(64, 512,
                new Color(0.05f, 0.08f, 0.22f), new Color(0.24f, 0.30f, 0.55f), new Color(0.55f, 0.58f, 0.72f)),
             "sky_bluehour", "05_天空盒");

        AssetDatabase.Refresh();
        Debug.Log($"[TextureSynth] 貼圖已生成 → {OutDir}  以及  {RefsRoot}");
    }

    // ==================== 粒子 ====================

    /// 煙塵團：徑向衰減 × 無縫 fBm，邊緣柔和、不規則但整體圓。
    static Color[] SmokePuff(int n)
    {
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f; // 0=中心 1=邊
            float fall = Mathf.SmoothStep(1f, 0f, d);          // 徑向衰減
            float noise = Fbm(u, v, 8, 4, 1337) * 0.5f + 0.5f;  // 0..1 無縫
            float a = Mathf.Clamp01(fall * (0.45f + 0.75f * noise));
            a = Mathf.Pow(a, 1.35f);                            // 收邊，避免方框感
            px[y * n + x] = new Color(1f, 1f, 1f, a);
        }
        return px;
    }

    /// 純徑向光暈：完全對稱、無雜訊、無色偏——AI 幾乎畫不出這種乾淨度。
    static Color[] RadialGlow(int n)
    {
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n - 0.5f;
            float d = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v) * 2f);
            float a = Mathf.Exp(-4.5f * d * d) * Mathf.SmoothStep(1f, 0f, d); // 高斯 × 硬邊界歸零
            px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        }
        return px;
    }

    /// 火花爆散：角度雜訊控制放射狀條紋長度。
    static Color[] SparkBurst(int n)
    {
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n - 0.5f;
            float d = Mathf.Sqrt(u * u + v * v) * 2f;
            float ang = Mathf.Atan2(v, u) / (2f * Mathf.PI) + 0.5f;     // 0..1
            // 沿角度取無縫 1D 雜訊（用 x 週期雜訊，保證 0 與 1 接得起來）
            // 高頻 + 高次方 → 細而多的火花，而不是幾片肥厚的花瓣
            float spoke = ValueNoise(ang * 96f, 0.5f, 96, 991) * 0.5f + 0.5f;
            spoke = Mathf.Pow(spoke, 7f);
            float len = 0.30f + 0.70f * Mathf.Pow(spoke, 0.35f);         // 長度有差異，粗細才收斂
            float a = Mathf.SmoothStep(len, len * 0.10f, d) * Mathf.SmoothStep(0f, 0.02f, d);
            float core = Mathf.Exp(-70f * d * d);                        // 中心亮核
            px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a * spoke * 6f + core));
        }
        return px;
    }

    /// 細灰塵：比煙更淡更散，用來鋪環境浮塵。
    static Color[] DustSoft(int n)
    {
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
            float a = Mathf.SmoothStep(1f, 0f, d);
            a = Mathf.Pow(a, 2.2f) * (0.6f + 0.4f * (Fbm(u, v, 16, 3, 55) * 0.5f + 0.5f));
            px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a * 0.8f));
        }
        return px;
    }

    /// 輪胎痕：橫向為 TrailRenderer 的寬度方向，兩側柔和收邊、縱向可無縫接續。
    static Color[] TireStreak(int n)
    {
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            float edge = Mathf.SmoothStep(0f, 0.18f, u) * Mathf.SmoothStep(1f, 0.82f, u); // 左右收邊
            float grain = 0.7f + 0.3f * (Fbm(u, v, 16, 3, 404) * 0.5f + 0.5f);            // 縱向無縫顆粒
            px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(edge * grain));
        }
        return px;
    }

    // ==================== 無縫平鋪材質 ====================

    /// 柏油高度：大尺度起伏 + 中尺度碎石 + 逐像素細砂（逐像素雜訊天然無縫）。
    static float[] AsphaltHeight(int n)
    {
        var h = new float[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            float baseN = Fbm(u, v, 8, 5, 21) * 0.5f + 0.5f;
            float gravel = Mathf.Pow(Fbm(u, v, 64, 3, 77) * 0.5f + 0.5f, 2.5f);
            float grit = Hash01(x, y, 5150);
            h[y * n + x] = Mathf.Clamp01(baseN * 0.45f + gravel * 0.40f + grit * 0.15f);
        }
        return h;
    }

    /// 泥土高度：起伏比柏油大，顆粒更粗。
    static float[] DirtHeight(int n)
    {
        var h = new float[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            float baseN = Fbm(u, v, 4, 6, 909) * 0.5f + 0.5f;
            float clod = Mathf.Pow(Fbm(u, v, 32, 3, 12) * 0.5f + 0.5f, 1.8f);
            h[y * n + x] = Mathf.Clamp01(baseN * 0.6f + clod * 0.32f + Hash01(x, y, 31) * 0.08f);
        }
        return h;
    }

    /// 風格化草地：3 段色階的手繪感，不做寫實漸層（對應企劃書的風格化路線）。
    static Color[] StylizedGrass(int n)
    {
        Color c0 = new Color(0.20f, 0.38f, 0.18f);
        Color c1 = new Color(0.31f, 0.53f, 0.24f);
        Color c2 = new Color(0.44f, 0.66f, 0.30f);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            float patch = Fbm(u, v, 8, 4, 66) * 0.5f + 0.5f;
            float blade = Fbm(u * 1f, v * 1f, 128, 2, 88) * 0.5f + 0.5f; // 高頻＝草葉感
            float t = patch * 0.65f + blade * 0.35f;
            // 量化成 3 階 → 手繪平塗風，不是寫實漸層
            Color c = t < 0.42f ? c0 : (t < 0.62f ? c1 : c2);
            px[y * n + x] = c;
        }
        return px;
    }

    /// 高度圖 → 切線空間法線圖（Sobel）。取樣座標一律 wrap，所以法線圖也無縫。
    static Color[] HeightToNormal(float[] h, float strength)
    {
        int n = (int)Mathf.Sqrt(h.Length);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float hl = H(h, n, x - 1, y), hr = H(h, n, x + 1, y);
            float hd = H(h, n, x, y - 1), hu = H(h, n, x, y + 1);
            var nrm = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
            px[y * n + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
        }
        return px;
    }

    static float H(float[] h, int n, int x, int y) => h[((y % n + n) % n) * n + ((x % n + n) % n)];

    /// 高度圖 → 雙色插值上色（只出 Albedo，刻意不加任何方向性明暗）。
    static Color[] Colorize(float[] h, Color dark, Color light)
    {
        var px = new Color[h.Length];
        for (int i = 0; i < h.Length; i++) px[i] = Color.Lerp(dark, light, h[i]);
        return px;
    }

    // ==================== 天空漸層 ====================

    /// 三段式垂直漸層：天頂 → 中段 → 地平線。加極微抖動消除色帶。
    static Color[] SkyGradient(int w, int hgt, Color zenith, Color mid, Color horizon)
    {
        var px = new Color[w * hgt];
        for (int y = 0; y < hgt; y++)
        {
            float t = (float)y / (hgt - 1);                     // 0=下(地平線) 1=上(天頂)
            Color c = t < 0.5f ? Color.Lerp(horizon, mid, Smooth(t / 0.5f))
                               : Color.Lerp(mid, zenith, Smooth((t - 0.5f) / 0.5f));
            for (int x = 0; x < w; x++)
            {
                float d = (Hash01(x, y, 7) - 0.5f) * (1f / 255f);  // dither
                px[y * w + x] = new Color(c.r + d, c.g + d, c.b + d, 1f);
            }
        }
        return px;
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    // ==================== 無縫雜訊 ====================

    /// 週期性 value noise：晶格索引對 period 取模 → 邊界必然吻合，這就是「數學上保證無縫」。
    static float ValueNoise(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = Smooth(x - x0), fy = Smooth(y - y0);
        float a = Hash01(Mod(x0, period),     Mod(y0, period),     seed);
        float b = Hash01(Mod(x0 + 1, period), Mod(y0, period),     seed);
        float c = Hash01(Mod(x0, period),     Mod(y0 + 1, period), seed);
        float d = Hash01(Mod(x0 + 1, period), Mod(y0 + 1, period), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy) * 2f - 1f;
    }

    /// fBm：每層週期加倍。basePeriod 必須整除貼圖邊長（用 4/8/16… 皆可），否則會破壞無縫。
    static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
    {
        float sum = 0f, amp = 1f, norm = 0f;
        int p = basePeriod;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * ValueNoise(u * p, v * p, p, seed + o * 131);
            norm += amp;
            amp *= 0.5f;
            p *= 2;
        }
        return sum / norm; // -1..1
    }

    static int Mod(int a, int m) => (a % m + m) % m;

    static float Hash01(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
    }

    // ==================== 輸出 ====================

    static void Save(Color[] px, string name, string refSubDir, bool alpha = false, bool linear = false)
    {
        int n = (int)Mathf.Sqrt(px.Length);
        Save(px, n, n, name, refSubDir, alpha, linear);
    }

    static void Save(Color[] px, int w, int h, string name, string refSubDir, bool alpha = false, bool linear = false)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
        tex.SetPixels(px);
        tex.Apply();
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);

        string assetPath = $"{OutDir}/{name}.png";
        File.WriteAllBytes(assetPath, png);

        // 同步一份到 _Refs 供肉眼檢視（不影響專案）
        string refDir = Path.Combine(RefsRoot, refSubDir);
        Directory.CreateDirectory(refDir);
        File.WriteAllBytes(Path.Combine(refDir, name + ".png"), png);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp != null)
        {
            imp.alphaIsTransparency = alpha;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            if (linear) { imp.textureType = TextureImporterType.NormalMap; }
            imp.SaveAndReimport();
        }
    }
}
