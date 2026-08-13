using System.IO;
using UnityEditor;
using UnityEngine;

/// ambientCG 掃描 PBR 材質管線（CC0）。
/// 半寫實檔位的質感命脈：真實柏油/混凝土的顆粒與法線起伏，是程序合成貼圖給不了的。
///
/// 流程：_Assets\ambientcg\<ID>\ 的 Color/NormalGL/AO → 複製進專案 → 設匯入參數 →
/// 生成 URP Lit 材質（含法線與 AO）。原始 zip 永遠留在 _Assets 不動。
public static class PbrLib
{
    const string SrcRoot = "../_Assets/ambientcg";          // 相對於專案資料夾
    const string ArtDir = "Assets/_Project/Art/AmbientCG";
    const string MaterialsDir = "Assets/_Project/Materials";

    /// 建立（或更新）一顆掃描 PBR 材質。
    /// id = ambientCG 資產名（例 Asphalt025C）；tiling 以公尺為單位由呼叫端決定。
    public static Material Mat(string matName, string id, float smoothness, Vector2 tiling,
                               Color tint = default, float metallic = 0f)
    {
        ImportSet(id);

        string path = MaterialsDir + "/" + matName + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Directory.CreateDirectory(MaterialsDir);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");

        var color = Tex(id, "Color");
        var normal = Tex(id, "NormalGL");
        var ao = Tex(id, "AmbientOcclusion");

        mat.SetTexture("_BaseMap", color);
        mat.SetColor("_BaseColor", tint == default ? Color.white : tint);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }
        if (ao != null)
        {
            mat.SetTexture("_OcclusionMap", ao);
            mat.SetFloat("_OcclusionStrength", 1f);
            mat.EnableKeyword("_OCCLUSIONMAP");
        }
        mat.SetTextureScale("_BaseMap", tiling);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// 有這套掃描材質嗎（原始包存在即可，尚未匯入也算）
    public static bool Available(string id)
    {
        string src = Path.Combine(Application.dataPath, "..", SrcRoot, id);
        return Directory.Exists(src) || File.Exists(ArtPath(id, "Color"));
    }

    // ---------------- 內部 ----------------

    static string ArtPath(string id, string map) => ArtDir + "/" + id + "_" + map + ".jpg";

    static Texture2D Tex(string id, string map)
        => AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath(id, map));

    /// 把一套掃描圖從 _Assets 複製進專案並設好匯入參數（已存在就跳過複製）
    static void ImportSet(string id)
    {
        Directory.CreateDirectory(ArtDir);
        string srcDir = Path.Combine(Application.dataPath, "..", SrcRoot, id);

        foreach (var map in new[] { "Color", "NormalGL", "AmbientOcclusion" })
        {
            string dst = ArtPath(id, map);
            if (File.Exists(dst)) continue;
            string src = Path.Combine(srcDir, id + "_2K-JPG_" + map + ".jpg");
            if (!File.Exists(src)) continue;
            File.Copy(src, dst);
            AssetDatabase.ImportAsset(dst);

            var imp = AssetImporter.GetAtPath(dst) as TextureImporter;
            if (imp == null) continue;
            imp.maxTextureSize = 2048;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Repeat;
            if (map == "NormalGL")
            {
                imp.textureType = TextureImporterType.NormalMap;   // 不設這個法線是壞的
            }
            else if (map == "AmbientOcclusion")
            {
                imp.sRGBTexture = false;   // AO 是線性資料
            }
            imp.SaveAndReimport();
        }
    }
}
