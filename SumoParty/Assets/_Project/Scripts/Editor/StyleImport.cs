using System.IO;
using UnityEditor;
using UnityEngine;

/// AI 圖後處理入庫（流程 MD §4.2 第④步）：
/// 黑底粒子 → 亮度轉 alpha；白底幟旗 → 去背；場景圖直接複製。
/// 原圖永遠留在 _Refs\，成品進 Assets/_Project/Art/。
public static class StyleImport
{
    const string ArtDir = "Assets/_Project/Art";
    static string RefsDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../_Refs"));

    [MenuItem("Sumo/Import AI Art (sumo01)")]
    public static void ProcessAll()
    {
        Directory.CreateDirectory(ArtDir);

        BlackToAlpha(Path.Combine(RefsDir, "02_粒子貼圖/sumo01_dust_puff.png"), ArtDir + "/fx_dust.png");
        BlackToAlpha(Path.Combine(RefsDir, "02_粒子貼圖/sumo01_impact_burst.png"), ArtDir + "/fx_impact.png");
        WhiteCut(Path.Combine(RefsDir, "08_場景背景/sumo01_nobori_banner.png"), ArtDir + "/nobori_banner.png");
        CopyPlain(Path.Combine(RefsDir, "04_材質貼圖/sumo01_dohyo_surface.png"), ArtDir + "/dohyo_surface.png");
        CopyPlain(Path.Combine(RefsDir, "08_場景背景/sumo01_crowd_stand.png"), ArtDir + "/crowd_stand.png");

        AssetDatabase.Refresh();
        SetImporter(ArtDir + "/fx_dust.png", true);
        SetImporter(ArtDir + "/fx_impact.png", true);
        SetImporter(ArtDir + "/nobori_banner.png", true);
        SetImporter(ArtDir + "/dohyo_surface.png", false);
        SetImporter(ArtDir + "/crowd_stand.png", false, 2048);
        Debug.Log("[StyleImport] AI art processed into " + ArtDir);
    }

    static Texture2D LoadRaw(string absPath)
    {
        if (!File.Exists(absPath)) { Debug.LogWarning("[StyleImport] missing " + absPath); return null; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(absPath));
        return tex;
    }

    // 黑底灰階 → 白色 + 亮度當 alpha（粒子貼圖標準流程）
    static void BlackToAlpha(string src, string dst)
    {
        var tex = LoadRaw(src);
        if (tex == null) return;
        var px = tex.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            byte lum = (byte)((px[i].r + px[i].g + px[i].b) / 3);
            px[i] = new Color32(255, 255, 255, lum);
        }
        tex.SetPixels32(px);
        File.WriteAllBytes(dst, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    // 近白背景 → 透明（幟旗去背）
    static void WhiteCut(string src, string dst)
    {
        var tex = LoadRaw(src);
        if (tex == null) return;
        var px = tex.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            int min = Mathf.Min(px[i].r, Mathf.Min(px[i].g, px[i].b));
            if (min > 235) px[i].a = 0;
        }
        tex.SetPixels32(px);
        File.WriteAllBytes(dst, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    static void CopyPlain(string src, string dst)
    {
        if (!File.Exists(src)) { Debug.LogWarning("[StyleImport] missing " + src); return; }
        File.Copy(src, dst, true);
    }

    static void SetImporter(string assetPath, bool alphaTransparency, int maxSize = 1024)
    {
        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp == null) return;
        imp.alphaIsTransparency = alphaTransparency;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.maxTextureSize = maxSize;
        imp.mipmapEnabled = true;
        imp.SaveAndReimport();
    }
}
