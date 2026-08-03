using System.IO;
using UnityEditor;
using UnityEngine;

/// 把 _Refs 的 AI 生成圖處理後匯入 Resources/Art：
/// 粒子圖（黑底）→ 亮度轉 Alpha；UI 圖示（白底）→ 去背；材質/天空 → 原樣複製
public static class RefArtImporter
{
    const string OutDir = "Assets/_Project/Resources/Art";

    [MenuItem("Tools/蚯蚓的一生/匯入 _Refs 美術")]
    public static void ImportAll()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string refs = Path.GetFullPath(Path.Combine(projectRoot, "..", "_Refs"));
        if (!Directory.Exists(refs))
        {
            Debug.LogWarning("找不到 _Refs 資料夾：" + refs);
            return;
        }
        Directory.CreateDirectory(OutDir);

        int done = 0;

        foreach (var name in new[] { "dust_puff", "glow_soft", "leaves_sheet", "feathers_sheet" })
            if (ProcessLuminanceAlpha(FindFile(refs, name + ".png"), OutDir + "/" + name + ".png")) done++;

        if (ProcessWhiteUnmatte(FindFile(refs, "ui_icons.png"), OutDir + "/ui_icons.png")) done++;

        foreach (var name in new[] { "sky_gradient", "grass_tile", "dirt_tile", "wood_planks_tile", "straw_tile" })
            if (CopyRaw(FindFile(refs, name + ".png"), OutDir + "/" + name + ".png")) done++;

        AssetDatabase.Refresh();
        ApplyImportSettings();
        Debug.Log($"RefArt 匯入完成，處理 {done} 張圖");
    }

    static string FindFile(string root, string fileName)
    {
        var hits = Directory.GetFiles(root, fileName, SearchOption.AllDirectories);
        return hits.Length > 0 ? hits[0] : null;
    }

    static Texture2D LoadPng(string path)
    {
        if (path == null || !File.Exists(path)) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
        return tex;
    }

    /// 黑底灰階 → 白色 + 亮度當 Alpha（粒子用，可染色）
    static bool ProcessLuminanceAlpha(string src, string dst)
    {
        var tex = LoadPng(src);
        if (tex == null) return false;
        var px = tex.GetPixels();
        for (int i = 0; i < px.Length; i++)
        {
            float lum = Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b));
            px[i] = new Color(1f, 1f, 1f, lum);
        }
        tex.SetPixels(px);
        File.WriteAllBytes(dst, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        return true;
    }

    /// 白底彩圖 → 去背（UI 圖示用）
    static bool ProcessWhiteUnmatte(string src, string dst)
    {
        var tex = LoadPng(src);
        if (tex == null) return false;
        var px = tex.GetPixels();
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            float whiteness = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float a = 1f - whiteness;
            if (a < 0.03f) { px[i] = Color.clear; continue; }
            // un-matte：把混進來的白色扣掉
            px[i] = new Color(
                Mathf.Clamp01((c.r - whiteness) / a),
                Mathf.Clamp01((c.g - whiteness) / a),
                Mathf.Clamp01((c.b - whiteness) / a),
                a);
        }
        tex.SetPixels(px);
        File.WriteAllBytes(dst, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        return true;
    }

    static bool CopyRaw(string src, string dst)
    {
        if (src == null || !File.Exists(src)) return false;
        File.Copy(src, dst, true);
        return true;
    }

    static void ApplyImportSettings()
    {
        SetImporter(OutDir + "/dust_puff.png", TextureWrapMode.Clamp, true);
        SetImporter(OutDir + "/glow_soft.png", TextureWrapMode.Clamp, true);
        SetImporter(OutDir + "/leaves_sheet.png", TextureWrapMode.Clamp, true);
        SetImporter(OutDir + "/feathers_sheet.png", TextureWrapMode.Clamp, true);
        SetImporter(OutDir + "/ui_icons.png", TextureWrapMode.Clamp, true);
        SetImporter(OutDir + "/sky_gradient.png", TextureWrapMode.Clamp, false);
        SetImporter(OutDir + "/grass_tile.png", TextureWrapMode.Mirror, false);
        SetImporter(OutDir + "/dirt_tile.png", TextureWrapMode.Mirror, false);
        SetImporter(OutDir + "/wood_planks_tile.png", TextureWrapMode.Mirror, false);
        SetImporter(OutDir + "/straw_tile.png", TextureWrapMode.Mirror, false);
    }

    static void SetImporter(string path, TextureWrapMode wrap, bool alphaTransparency)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;
        imp.wrapMode = wrap;
        imp.alphaIsTransparency = alphaTransparency;
        imp.mipmapEnabled = true;
        imp.SaveAndReimport();
    }
}
