using UnityEngine;

/// 程序化貼圖：不用外部圖檔，程式畫出泥土與石板
public static class ProceduralTex
{
    /// 泥土：多層柏林雜訊 + 細沙點
    public static Texture2D Dirt(int size = 256)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
        var dark = new Color(0.30f, 0.19f, 0.10f);
        var mid = new Color(0.48f, 0.32f, 0.17f);
        var light = new Color(0.58f, 0.42f, 0.24f);
        var px = new Color[size * size];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float n = Mathf.PerlinNoise(x * 0.045f, y * 0.045f);
            float fine = Mathf.PerlinNoise(x * 0.35f + 100f, y * 0.35f + 100f);
            float speck = Mathf.PerlinNoise(x * 0.9f + 500f, y * 0.9f + 500f);
            Color c = Color.Lerp(dark, mid, n);
            c = Color.Lerp(c, light, fine * 0.35f);
            c *= 0.88f + speck * 0.24f;
            px[y * size + x] = c;
        }
        tex.SetPixels(px);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Apply(true);
        return tex;
    }

    /// 石板：磚縫格線 + 雜訊風化
    public static Texture2D Stone(int size = 256)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
        var baseC = new Color(0.58f, 0.58f, 0.60f);
        var gap = new Color(0.30f, 0.30f, 0.32f);
        var px = new Color[size * size];
        int cell = size / 4;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int row = y / cell;
            int xo = (row % 2 == 0) ? 0 : cell / 2; // 交錯磚縫
            bool seam = ((x + xo) % cell < 3) || (y % cell < 3);
            float n = Mathf.PerlinNoise(x * 0.08f, y * 0.08f);
            float wear = Mathf.PerlinNoise(x * 0.3f + 50f, y * 0.3f + 50f);
            Color c = seam ? gap : baseC * (0.82f + n * 0.3f);
            c *= 0.9f + wear * 0.18f;
            px[y * size + x] = c;
        }
        tex.SetPixels(px);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Apply(true);
        return tex;
    }

    /// 草地色斑（灑在軟土上做變化）
    public static Texture2D GrassBlotch(int size = 256)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
        var soil = new Color(0.42f, 0.29f, 0.15f);
        var green = new Color(0.35f, 0.48f, 0.2f);
        var px = new Color[size * size];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float n = Mathf.PerlinNoise(x * 0.03f + 30f, y * 0.03f + 30f);
            float fine = Mathf.PerlinNoise(x * 0.4f, y * 0.4f);
            Color c = Color.Lerp(soil, green, Mathf.SmoothStep(0f, 1f, (n - 0.45f) * 2.2f));
            c *= 0.88f + fine * 0.22f;
            px[y * size + x] = c;
        }
        tex.SetPixels(px);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Apply(true);
        return tex;
    }
}
