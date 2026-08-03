using UnityEngine;

// 鍵盤 + 滑鼠/觸控 → 力士指令。P1 = WASD+J推+K馬步+Q/E閃 (外加滑鼠/單指觸控)；
// P2 = 方向鍵 + 右Ctrl推 + 右Shift馬步 + ,/. 閃
public class PlayerDriver : MonoBehaviour
{
    public SumoWrestler W;
    public bool ArrowScheme;        // true = P2 鍵位
    public bool MouseTouchEnabled;  // 只給 P1：點=推、拖=移動、快速下滑=馬步、快速橫滑=閃

    Vector2 pressPos;
    float pressTime;

    void Update()
    {
        if (W == null || W.Eliminated) return;
        Vector2 mv = Vector2.zero;

        if (!ArrowScheme)
        {
            if (Input.GetKey(KeyCode.A)) mv.x -= 1f;
            if (Input.GetKey(KeyCode.D)) mv.x += 1f;
            if (Input.GetKey(KeyCode.W)) mv.y += 1f;
            if (Input.GetKey(KeyCode.S)) mv.y -= 1f;
            if (Input.GetKeyDown(KeyCode.J)) W.TryPush();
            if (Input.GetKeyDown(KeyCode.K)) W.TryBrace();
            if (Input.GetKeyDown(KeyCode.Q)) W.TryDodge(-1f);
            if (Input.GetKeyDown(KeyCode.E)) W.TryDodge(1f);
        }
        else
        {
            if (Input.GetKey(KeyCode.LeftArrow)) mv.x -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) mv.x += 1f;
            if (Input.GetKey(KeyCode.UpArrow)) mv.y += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) mv.y -= 1f;
            if (Input.GetKeyDown(KeyCode.RightControl)) W.TryPush();
            if (Input.GetKeyDown(KeyCode.RightShift)) W.TryBrace();
            if (Input.GetKeyDown(KeyCode.Comma)) W.TryDodge(-1f);
            if (Input.GetKeyDown(KeyCode.Period)) W.TryDodge(1f);
        }

        if (MouseTouchEnabled)
        {
            Vector2 mp = Input.mousePosition;
            if (Input.GetMouseButtonDown(0)) { pressPos = mp; pressTime = Time.time; }
            if (Input.GetMouseButton(0))
            {
                Vector2 d = mp - pressPos;
                if (d.magnitude > 30f) mv = d.normalized; // 拖曳 = 移動（螢幕上 = 朝對手）
            }
            if (Input.GetMouseButtonUp(0))
            {
                Vector2 d = mp - pressPos;
                float dur = Time.time - pressTime;
                if (dur < 0.25f && d.magnitude < 25f) W.TryPush();               // 點一下
                else if (dur < 0.35f && d.y < -80f) W.TryBrace();                // 快速下滑
                else if (dur < 0.35f && Mathf.Abs(d.x) > 80f && Mathf.Abs(d.y) < 60f)
                    W.TryDodge(Mathf.Sign(d.x));                                 // 快速橫滑
            }
        }

        W.SetMove(mv);
    }
}
