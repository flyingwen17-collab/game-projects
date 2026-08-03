using UnityEngine;

// 灰盒回合流程：立合倒數 → 開戰 → 出圈/落地判定 → 勝負 → 重來。
// UI 全用 OnGUI（P1 規定零美術）。
public class MatchManager : MonoBehaviour
{
    public static event System.Action RoundStarted;
    public static event System.Action<Vector3> RingOutAt;

    public SumoParams P;
    public SumoWrestler P1;
    public SumoWrestler P2;
    public PlayerDriver P2Driver;   // 人類 P2（預設關）
    public NpcBrain P2Npc;          // NPC P2（預設開）
    public Transform Dohyo;         // 土俵圓柱
    public float BaseRadius = 4f;

    public float CurrentRadius { get; private set; }

    enum State { Intro, Fight, End }
    State state;
    float stateTimer, fightTime;
    SumoWrestler winner;
    Vector3 p1Spawn, p2Spawn, dohyoBaseScale;
    Texture2D white;
    bool npcMode = true;

    void Start()
    {
        p1Spawn = P1.transform.position;
        p2Spawn = P2.transform.position;
        dohyoBaseScale = Dohyo.localScale;
        white = Texture2D.whiteTexture;
        EnterIntro();
    }

    void EnterIntro()
    {
        Time.timeScale = 1f; // 慢動作/頓幀保險
        state = State.Intro;
        stateTimer = 1.6f;
        CurrentRadius = BaseRadius;
        Dohyo.localScale = dohyoBaseScale;
        P1.ResetState(p1Spawn);
        P2.ResetState(p2Spawn);
        P1.ControlLocked = P2.ControlLocked = true;
        winner = null;
    }

    void Update()
    {
        // T 切換 P2 人類/NPC
        if (Input.GetKeyDown(KeyCode.T))
        {
            npcMode = !npcMode;
            ApplyP2Mode();
        }

        switch (state)
        {
            case State.Intro:
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    state = State.Fight;
                    fightTime = 0f;
                    P1.ControlLocked = P2.ControlLocked = false;
                    RoundStarted?.Invoke();
                }
                break;

            case State.Fight:
                fightTime += Time.deltaTime;
                UpdateShrink();
                CheckOut(P1, P2);
                CheckOut(P2, P1);
                break;

            case State.End:
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f &&
                    (Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(0)))
                    EnterIntro();
                break;
        }
    }

    void ApplyP2Mode()
    {
        if (P2Npc != null) P2Npc.enabled = npcMode;
        if (P2Driver != null) P2Driver.enabled = !npcMode;
    }

    void UpdateShrink()
    {
        if (fightTime < P.shrinkDelay) return;
        float min = BaseRadius * P.minRadiusRatio;
        CurrentRadius = Mathf.Max(min, CurrentRadius - BaseRadius * P.shrinkPerSec * Time.deltaTime);
        float k = CurrentRadius / BaseRadius;
        Dohyo.localScale = new Vector3(dohyoBaseScale.x * k, dohyoBaseScale.y, dohyoBaseScale.z * k);
    }

    void CheckOut(SumoWrestler w, SumoWrestler other)
    {
        if (w.Eliminated) return;
        Vector3 pos = w.transform.position;
        bool fell = pos.y < -1.2f;
        bool outside = new Vector3(pos.x, 0f, pos.z).magnitude > CurrentRadius + 0.7f;
        if (fell || outside)
        {
            w.MarkEliminated();
            winner = other;
            state = State.End;
            stateTimer = 1.2f; // 防止勝負瞬間誤觸立刻重開
            P1.ControlLocked = P2.ControlLocked = true;
            RingOutAt?.Invoke(w.transform.position);
        }
    }

    // ---------- 灰盒 UI ----------
    void OnGUI()
    {
        var big = new GUIStyle(GUI.skin.label) { fontSize = 42, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        var mid = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        float w = Screen.width, h = Screen.height;

        DrawStamina(new Rect(20, 20, 280, 26), P1, new Color(0.25f, 0.5f, 1f));
        DrawStamina(new Rect(w - 300, 20, 280, 26), P2, new Color(1f, 0.35f, 0.3f));
        GUI.Label(new Rect(20, 48, 280, 22), P1.DisplayName + (P1.IsBracing ? "(馬步)" : ""));
        GUI.Label(new Rect(w - 300, 48, 280, 22), P2.DisplayName + (P2.IsBracing ? "(馬步)" : "") + (npcMode ? " [NPC]" : " [P2]"));

        if (state == State.Intro)
            GUI.Label(new Rect(0, h * 0.3f, w, 60), stateTimer > 0.6f ? "見合って——" : "はっけよい!", big);
        else if (state == State.Fight && fightTime > P.shrinkDelay)
            GUI.Label(new Rect(0, 80, w, 30), "土俵縮小中!", mid);
        else if (state == State.End && winner != null)
        {
            GUI.Label(new Rect(0, h * 0.3f, w, 60), winner.DisplayName + " 勝利!", big);
            if (stateTimer <= 0f)
                GUI.Label(new Rect(0, h * 0.3f + 70, w, 30), "按 R 或點擊 再來一場", mid);
        }

        GUI.Label(new Rect(0, h - 28, w, 24),
            "P1: WASD移動 J推 K馬步 Q/E閃避 | 滑鼠/觸控: 點=推 拖=移動 快下滑=馬步 | T鍵: P2切換NPC/真人(方向鍵+右Ctrl推)", mid);
    }

    void DrawStamina(Rect r, SumoWrestler wr, Color c)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(r, white);
        GUI.color = c;
        float ratio = wr.P != null ? wr.Stamina / wr.P.staminaMax : 0f;
        GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * ratio, r.height - 4), white);
        GUI.color = Color.white;
    }
}
