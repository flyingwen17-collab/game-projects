using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// 程序化網格工廠：不靠 DCC 建模，用程式碼生出平滑的一體成形幾何。
/// 所有網格都存成 .asset，場景重開不會遺失。
public static class SumoMeshFactory
{
    const string MeshDir = "Assets/_Project/Meshes";

    // ---------- Catmull-Rom 取樣：控制點少、曲線平滑 ----------
    static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// 把控制點串成密集取樣的輪廓線（首尾各補一個端點延伸，讓曲線通過端點）
    public static List<Vector2> SampleProfile(Vector2[] pts, int samplesPerSeg)
    {
        var outPts = new List<Vector2>();
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector2 p0 = i == 0 ? pts[0] + (pts[0] - pts[1]) : pts[i - 1];
            Vector2 p3 = i == pts.Length - 2 ? pts[i + 1] + (pts[i + 1] - pts[i]) : pts[i + 2];
            int n = i == pts.Length - 2 ? samplesPerSeg + 1 : samplesPerSeg;
            for (int s = 0; s < n; s++)
                outPts.Add(CatmullRom(p0, pts[i], pts[i + 1], p3, s / (float)samplesPerSeg));
        }
        return outPts;
    }

    /// 車削曲面：profile = (半徑, 高度) 序列，繞 Y 軸旋轉。法線解析計算，接縫不可見。
    public static Mesh Lathe(string name, Vector2[] controlPts, int radialSegs = 48, int samplesPerSeg = 10)
    {
        var prof = SampleProfile(controlPts, samplesPerSeg);
        int rows = prof.Count;
        int cols = radialSegs + 1; // 接縫複製一列頂點

        // 輪廓切線 → 輪廓法線（指向外側）
        var profNormal = new Vector2[rows];
        for (int i = 0; i < rows; i++)
        {
            Vector2 a = prof[Mathf.Max(0, i - 1)];
            Vector2 b = prof[Mathf.Min(rows - 1, i + 1)];
            Vector2 tan = (b - a).normalized;
            profNormal[i] = new Vector2(tan.y, -tan.x); // 旋轉 -90°：曲線由下往上畫時法線朝外
            if (profNormal[i].x < 0f && prof[i].x > 0.001f) profNormal[i] = -profNormal[i];
        }

        var verts = new Vector3[rows * cols];
        var norms = new Vector3[rows * cols];
        var uvs = new Vector2[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            float radius = Mathf.Max(0f, prof[r].x);
            float y = prof[r].y;
            for (int c = 0; c < cols; c++)
            {
                float a = c / (float)radialSegs * Mathf.PI * 2f;
                float sin = Mathf.Sin(a), cos = Mathf.Cos(a);
                int idx = r * cols + c;
                verts[idx] = new Vector3(sin * radius, y, cos * radius);
                Vector3 n = new Vector3(sin * profNormal[r].x, profNormal[r].y, cos * profNormal[r].x);
                // 極點（半徑≈0）法線直接取軸向，避免除零亂轉
                if (radius < 0.002f) n = profNormal[r].y >= 0f ? Vector3.up : Vector3.down;
                norms[idx] = n.normalized;
                uvs[idx] = new Vector2(c / (float)radialSegs, r / (float)(rows - 1));
            }
        }

        var tris = new List<int>(rows * radialSegs * 6);
        for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < radialSegs; c++)
            {
                int i0 = r * cols + c, i1 = i0 + 1, i2 = i0 + cols, i3 = i2 + 1;
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }

        var mesh = new Mesh { name = name };
        mesh.indexFormat = verts.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvs;
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return Save(mesh, name);
    }

    /// 沿 3D 路徑掃出圓管，兩端加半球蓋（手臂用）。平行移動框架避免扭轉。
    public static Mesh Tube(string name, Vector3[] path, float radius, int radialSegs = 20, int capRings = 6)
    {
        // 路徑細分（Catmull-Rom on 3D）
        var pts = new List<Vector3>();
        for (int i = 0; i < path.Length - 1; i++)
        {
            Vector3 p0 = i == 0 ? path[0] + (path[0] - path[1]) : path[i - 1];
            Vector3 p3 = i == path.Length - 2 ? path[i + 1] + (path[i + 1] - path[i]) : path[i + 2];
            int n = i == path.Length - 2 ? 9 : 8;
            for (int s = 0; s < n; s++)
            {
                float t = s / 8f, t2 = t * t, t3 = t2 * t;
                pts.Add(0.5f * ((2f * path[i]) + (-p0 + path[i + 1]) * t
                    + (2f * p0 - 5f * path[i] + 4f * path[i + 1] - p3) * t2
                    + (-p0 + 3f * path[i] - 3f * path[i + 1] + p3) * t3));
            }
        }

        // 平行移動框架
        int n2 = pts.Count;
        var tang = new Vector3[n2];
        for (int i = 0; i < n2; i++)
        {
            Vector3 a = pts[Mathf.Max(0, i - 1)], b = pts[Mathf.Min(n2 - 1, i + 1)];
            tang[i] = (b - a).normalized;
        }
        var normal = Vector3.Cross(tang[0], Mathf.Abs(tang[0].y) > 0.9f ? Vector3.right : Vector3.up).normalized;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        int cols = radialSegs + 1;

        // 起點半球（往 -tangent 方向鼓出）
        for (int ring = capRings; ring >= 1; ring--)
        {
            float phi = ring / (float)capRings * Mathf.PI * 0.5f;
            AddRing(pts[0] - tang[0] * Mathf.Sin(phi) * radius, tang[0], ref normal,
                radius * Mathf.Cos(phi), radialSegs, verts, norms, -tang[0] * Mathf.Sin(phi));
        }
        for (int i = 0; i < n2; i++)
        {
            if (i > 0)
            {
                // 平行移動：把 normal 對新切線做最小旋轉
                var rot = Quaternion.FromToRotation(tang[i - 1], tang[i]);
                normal = (rot * normal).normalized;
            }
            AddRing(pts[i], tang[i], ref normal, radius, radialSegs, verts, norms, Vector3.zero);
        }
        // 終點半球
        for (int ring = 1; ring <= capRings; ring++)
        {
            float phi = ring / (float)capRings * Mathf.PI * 0.5f;
            AddRing(pts[n2 - 1] + tang[n2 - 1] * Mathf.Sin(phi) * radius, tang[n2 - 1], ref normal,
                radius * Mathf.Cos(phi), radialSegs, verts, norms, tang[n2 - 1] * Mathf.Sin(phi));
        }

        int rows = verts.Count / cols;
        var tris = new List<int>();
        for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < radialSegs; c++)
            {
                int i0 = r * cols + c, i1 = i0 + 1, i2 = i0 + cols, i3 = i2 + 1;
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return Save(mesh, name);
    }

    static void AddRing(Vector3 center, Vector3 tangent, ref Vector3 normal, float radius, int radialSegs,
        List<Vector3> verts, List<Vector3> norms, Vector3 capOffsetDir)
    {
        Vector3 bin = Vector3.Cross(tangent, normal).normalized;
        for (int c = 0; c <= radialSegs; c++)
        {
            float a = c / (float)radialSegs * Mathf.PI * 2f;
            Vector3 dir = normal * Mathf.Cos(a) + bin * Mathf.Sin(a);
            verts.Add(center + dir * radius);
            // 半球蓋的法線混入軸向分量
            Vector3 nrm = capOffsetDir == Vector3.zero ? dir : (dir * Mathf.Max(radius, 0.0001f) + capOffsetDir).normalized;
            norms.Add(nrm);
        }
    }

    /// X 鏡射（左臂 → 右臂）：翻頂點、翻法線、反轉繞序
    public static Mesh MirrorX(string name, Mesh src)
    {
        var verts = src.vertices;
        var norms = src.normals;
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i].x = -verts[i].x;
            norms[i].x = -norms[i].x;
        }
        var tris = src.triangles;
        for (int i = 0; i < tris.Length; i += 3)
            (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
        var mesh = new Mesh { name = name };
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return Save(mesh, name);
    }

    /// 倒置圓柱牆（法線朝內）＋朝下的天花板圓盤：場館背景，消滅黑色虛空
    public static Mesh ArenaShell(string name, float radius, float yBottom, float yTop, int segs = 64)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        int cols = segs + 1;

        for (int r = 0; r < 2; r++)
        {
            float y = r == 0 ? yBottom : yTop;
            for (int c = 0; c <= segs; c++)
            {
                float a = c / (float)segs * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius));
                norms.Add(new Vector3(-Mathf.Sin(a), 0f, -Mathf.Cos(a)));
                uvs.Add(new Vector2(c * 4f / segs, r));
            }
        }
        for (int c = 0; c < segs; c++)
        {
            int i0 = c, i1 = c + 1, i2 = c + cols, i3 = i2 + 1;
            tris.Add(i0); tris.Add(i1); tris.Add(i2); // 朝內繞序
            tris.Add(i1); tris.Add(i3); tris.Add(i2);
        }
        // 天花板（法線朝下）
        int baseIdx = verts.Count;
        verts.Add(new Vector3(0f, yTop, 0f));
        norms.Add(Vector3.down);
        uvs.Add(new Vector2(0.5f, 0.5f));
        for (int c = 0; c <= segs; c++)
        {
            float a = c / (float)segs * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Sin(a) * radius, yTop, Mathf.Cos(a) * radius));
            norms.Add(Vector3.down);
            uvs.Add(new Vector2(Mathf.Sin(a) * 0.5f + 0.5f, Mathf.Cos(a) * 0.5f + 0.5f));
        }
        for (int c = 0; c < segs; c++)
        {
            tris.Add(baseIdx); tris.Add(baseIdx + 1 + c + 1); tris.Add(baseIdx + 1 + c);
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return Save(mesh, name);
    }

    /// 圓盤（土俵頂面，UV 平面展開）
    public static Mesh Disc(string name, float radius, int segments = 48)
    {
        var mesh = new Mesh { name = name };
        var verts = new Vector3[segments + 1];
        var uvs = new Vector2[segments + 1];
        var tris = new int[segments * 3];
        verts[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            verts[i + 1] = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
            uvs[i + 1] = new Vector2(Mathf.Sin(a) * 0.5f + 0.5f, Mathf.Cos(a) * 0.5f + 0.5f);
            int next = (i + 1) % segments + 1;
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = next;
        }
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return Save(mesh, name);
    }

    /// 身體輪廓半徑查詢：給腰帶、眼睛貼臉用
    public static float ProfileRadiusAt(Vector2[] controlPts, float y, int samplesPerSeg = 10)
    {
        var prof = SampleProfile(controlPts, samplesPerSeg);
        for (int i = 0; i < prof.Count - 1; i++)
        {
            if ((prof[i].y <= y && prof[i + 1].y >= y) || (prof[i].y >= y && prof[i + 1].y <= y))
            {
                float t = Mathf.InverseLerp(prof[i].y, prof[i + 1].y, y);
                return Mathf.Lerp(prof[i].x, prof[i + 1].x, t);
            }
        }
        return 0f;
    }

    static Mesh Save(Mesh mesh, string name)
    {
        Directory.CreateDirectory(MeshDir);
        string path = MeshDir + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
}
