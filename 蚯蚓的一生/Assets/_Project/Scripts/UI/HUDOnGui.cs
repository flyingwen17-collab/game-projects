using UnityEngine;

/// HUD：分數、倍率、體力、技能冷卻、險過提示、死亡畫面
public class HUDOnGui : MonoBehaviour
{
    Font font;
    GUIStyle scoreStyle, hintStyle, comboStyle, skillStyle, overTitle, overBody, closeCallStyle;
    Texture2D white;
    WormStamina stamina;
    BurrowSystem burrow;
    WormSkills skills;

    void Start()
    {
        font = Font.CreateDynamicFontFromOSFont("Microsoft JhengHei", 24);
        white = new Texture2D(1, 1);
        white.SetPixel(0, 0, Color.white);
        white.Apply();

        var w = GameObject.FindGameObjectWithTag("Player");
        if (w != null)
        {
            stamina = w.GetComponent<WormStamina>();
            burrow = w.GetComponent<BurrowSystem>();
        }
    }

    void EnsureStyles()
    {
        if (scoreStyle != null) return;
        scoreStyle = new GUIStyle { font = font, fontSize = 26, normal = { textColor = Color.white } };
        hintStyle = new GUIStyle { font = font, fontSize = 16, normal = { textColor = new Color(1f, 1f, 1f, 0.75f) } };
        comboStyle = new GUIStyle { font = font, fontSize = 30, normal = { textColor = new Color(1f, 0.85f, 0.2f) } };
        skillStyle = new GUIStyle { font = font, fontSize = 15, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        overTitle = new GUIStyle { font = font, fontSize = 44, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.35f, 0.3f) } };
        overBody = new GUIStyle { font = font, fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        closeCallStyle = new GUIStyle { font = font, fontSize = 36, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.9f, 0.3f) } };
    }

    void OnGUI()
    {
        if (GameManager.I == null) return;
        EnsureStyles();
        var gm = GameManager.I;

        if (skills == null)
        {
            var w = GameObject.FindGameObjectWithTag("Player");
            if (w != null) skills = w.GetComponent<WormSkills>();
        }

        // 分數
        GUI.Label(new Rect(20, 15, 460, 40), $"分數  {Mathf.FloorToInt(gm.Score)}", scoreStyle);
        GUI.Label(new Rect(20, 48, 460, 30),
            $"存活 {gm.SurvivalTime:0.0} 秒   食物 {gm.FoodEaten}   險過 {gm.CloseCalls}", hintStyle);

        // 連吃倍率
        if (gm.ComboStreak > 1)
        {
            GUI.Label(new Rect(20, 78, 300, 40), $"連吃 x{gm.ComboMultiplier:0.0}", comboStyle);
            GUI.color = new Color(1f, 0.85f, 0.2f, 0.8f);
            GUI.DrawTexture(new Rect(22, 114, 120f * (gm.ComboTimeLeft / 10f), 6), white);
            GUI.color = Color.white;
        }

        // 體力條
        if (stamina != null)
        {
            float barW = 320f, barH = 16f;
            float x = (Screen.width - barW) / 2f, y = Screen.height - 78f;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(x - 2, y - 2, barW + 4, barH + 4), white);
            GUI.color = stamina.Percent > 0.3f ? new Color(1f, 0.55f, 0.75f) : new Color(1f, 0.25f, 0.2f);
            GUI.DrawTexture(new Rect(x, y, barW * stamina.Percent, barH), white);
            GUI.color = Color.white;

            string hint = burrow != null && burrow.IsBurrowed
                ? "地下潛行中… 放開 Space 出土｜Shift 土遁突進"
                : "按住 Space 鑽土｜Shift 衝刺｜F 誘餌｜Q/E 視角";
            GUI.Label(new Rect(x, y + 20, barW + 240, 24), hint, hintStyle);

            // 技能冷卻格
            if (skills != null)
            {
                DrawSkill(new Rect(x + barW + 20, y - 14, 74, 44), "誘餌 F",
                    skills.DecoyCdLeft, skills.decoyCooldown);
                DrawSkill(new Rect(x + barW + 102, y - 14, 74, 44), "突進",
                    skills.DashCdLeft, skills.dashCooldown);
            }
        }

        // 險過提示
        if (gm.CloseCallFlash > 0f)
        {
            var c = closeCallStyle.normal.textColor;
            c.a = Mathf.Clamp01(gm.CloseCallFlash);
            closeCallStyle.normal.textColor = c;
            GUI.Label(new Rect(Screen.width / 2f - 250, Screen.height * 0.32f, 500, 50), "驚險閃避！+300", closeCallStyle);
        }

        // 死亡畫面
        if (gm.State == GameState.GameOver)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
            GUI.color = Color.white;
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            GUI.Label(new Rect(cx - 300, cy - 90, 600, 60), "你被吃掉了！", overTitle);
            GUI.Label(new Rect(cx - 300, cy - 12, 600, 40),
                $"總分 {Mathf.FloorToInt(gm.Score)}   存活 {gm.SurvivalTime:0.0} 秒   食物 {gm.FoodEaten}   險過 {gm.CloseCalls}", overBody);
            GUI.Label(new Rect(cx - 300, cy + 38, 600, 40), "按 R 再來一次", overBody);
        }
    }

    void DrawSkill(Rect r, string label, float cdLeft, float cdTotal)
    {
        bool ready = cdLeft <= 0f;
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(r, white);
        GUI.color = ready ? new Color(0.35f, 0.9f, 0.5f, 0.9f) : new Color(0.5f, 0.5f, 0.5f, 0.7f);
        float fill = ready ? 1f : 1f - cdLeft / cdTotal;
        GUI.DrawTexture(new Rect(r.x, r.y + r.height * (1f - fill), r.width, r.height * fill), white);
        GUI.color = Color.white;
        GUI.Label(r, ready ? label : $"{cdLeft:0.0}s", skillStyle);
    }
}
