using UnityEngine;

/// <summary>
/// P1 灰盒期的除錯顯示（流程 MD §3 P1：零 UI 美化，但要看得到數值才能調手感）。
/// P2 會被正式 UI 取代。
/// </summary>
public class SumoDebugHUD : MonoBehaviour
{
    public SumoMatch match;
    GUIStyle big, mid;

    void OnGUI()
    {
        if (match == null || match.east == null || match.west == null) return;

        big ??= new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
        mid ??= new GUIStyle(GUI.skin.label) { fontSize = 16 };

        float w = Screen.width, h = Screen.height;

        // 觸控分區線（企劃書 §1.1：常駐但低調）
        GUI.color = new Color(1f, 1f, 1f, 0.18f);
        GUI.DrawTexture(new Rect(w * 0.5f - 1f, 0f, 2f, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        Bar(new Rect(20, 20, w * 0.5f - 60, 22), match.east.CPRatio, new Color(0.35f, 0.6f, 1f),
            $"{match.east.displayName}  CP {match.east.CP:0}");
        Bar(new Rect(w * 0.5f + 40, 20, w * 0.5f - 60, 22), match.west.CPRatio, new Color(1f, 0.4f, 0.35f),
            $"{match.west.displayName}  CP {match.west.CP:0}");

        GUI.Label(new Rect(0, h * 0.06f, w, 34),
                  $"{match.Phase}   {match.EastWins} - {match.WestWins}   {match.BoutTime:0.0}s   激烈度 {match.Intensity:0.00}", big);

        GUI.Label(new Rect(20, 56, w * 0.5f, 200), State(match.east), mid);
        GUI.Label(new Rect(w * 0.5f + 40, 56, w * 0.5f, 200), State(match.west), mid);

        GUI.Label(new Rect(20, h - 54, w - 40, 40),
            "P1: W前衝 S後退 A/D閃身 空白突き 左Shift按住(防禦/組手)　　" +
            "P2: ↑↓←→ Enter突き 右Shift按住　　觸控：各自半邊 點/滑/按住", mid);
    }

    static string State(Rikishi r)
    {
        string s = "";
        if (r.Charging) s += "突進中\n";
        if (r.Bracing) s += "防禦中\n";
        if (r.Gripping) s += "組手中（四つ身）\n";
        if (r.Vulnerable) s += "破綻！\n";
        if (r.BodyTouchedGround) s += "倒地\n";
        s += $"傾斜 {r.TiltAngle:0}°\n速度 {r.Velocity.magnitude:0.0} m/s";
        return s;
    }

    static void Bar(Rect rect, float t, Color c, string label)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = c;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(t), rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 6, rect.y + 1, rect.width, rect.height), label);
    }
}
