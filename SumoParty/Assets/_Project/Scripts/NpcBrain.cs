using UnityEngine;

// 灰盒版 NPC：逼近 → 射程內機率出掌；被逼到俵際偶爾扎馬步；偶爾橫移。
public class NpcBrain : MonoBehaviour
{
    public SumoWrestler W;
    public MatchManager Match;

    float decisionTimer;
    Vector2 currentMove;

    void Update()
    {
        if (W == null || W.Eliminated || W.Opponent == null) return;
        var p = W.P;

        decisionTimer -= Time.deltaTime;
        if (decisionTimer <= 0f)
        {
            decisionTimer = p.npcReactionDelay * Random.Range(0.7f, 1.4f);
            Decide(p);
        }
        W.SetMove(currentMove);
    }

    void Decide(SumoParams p)
    {
        Vector3 self = W.transform.position;
        Vector3 to = W.Opponent.transform.position - self;
        to.y = 0f;
        float dist = to.magnitude;

        // 快被擠出去了 → 扎馬步保命
        if (Match != null)
        {
            float edge = new Vector3(self.x, 0f, self.z).magnitude / Mathf.Max(0.1f, Match.CurrentRadius);
            if (edge > 0.72f && Random.value < p.npcBraceChance) { currentMove = Vector2.zero; W.TryBrace(); return; }
        }

        if (dist > p.pushRange * 0.9f)
        {
            currentMove = new Vector2(Random.Range(-0.3f, 0.3f), 1f); // 逼近＋一點蛇行
        }
        else
        {
            currentMove = Vector2.zero;
            if (Random.value < p.npcPushChance) W.TryPush();
            else currentMove = new Vector2(Random.value < 0.5f ? -1f : 1f, 0.2f); // 繞側
        }
    }
}
