using UnityEngine;
using UnityEngine.AI;

/// 雞 AI 狀態機：閒晃 → 警覺 → 追擊 → 啄食 / 搜索
/// 加上程序動畫（擺腿/拍翅/啄擊）、公雞呼叫同伴、誘餌反應、險過判定
[RequireComponent(typeof(NavMeshAgent))]
public class ChickenAI : MonoBehaviour
{
    public enum State { Wander, Alert, Chase, Peck, Search }
    public State Current { get; private set; } = State.Wander;

    /// 目前正在追玩家的雞數（BGM 切換用）
    public static int ChaserCount { get; private set; }

    [Header("速度")]
    public float walkSpeed = 1.6f;
    public float chaseSpeed = 4.6f;

    [Header("感知")]
    public float visionRange = 10f;
    public float visionAngle = 120f;
    public float alertTime = 0.8f;
    public float loseTargetTime = 1.5f;
    public float searchTime = 4.5f;
    public float wanderRadius = 8f;

    [Header("啄食")]
    public float peckRange = 1.1f;
    public float peckWindup = 0.3f;
    public float peckKillRadius = 1.4f;

    NavMeshAgent agent;
    Transform worm;
    Rigidbody wormRb;
    WormController wormCtrl;
    BurrowSystem wormBurrow;
    ChickenBody body;

    float stateTimer;
    float loseSightTimer;
    float callCooldown;
    Vector3 lastKnownPos;
    TextMesh alertText;
    bool peckResolved;
    bool peckThreatened; // 啄下去的瞬間玩家原本在必死範圍內
    bool counted;        // 已計入 ChaserCount
    float walkPhase;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        body = GetComponent<ChickenBody>();
        if (body != null)
        {
            walkSpeed = body.walkSpeed;
            chaseSpeed = body.chaseSpeed;
            visionRange = body.visionRange;
            visionAngle = body.visionAngle;
            alertTime = body.alertTime;
        }

        var w = GameObject.FindGameObjectWithTag("Player");
        if (w != null)
        {
            worm = w.transform;
            wormRb = w.GetComponent<Rigidbody>();
            wormCtrl = w.GetComponent<WormController>();
            wormBurrow = w.GetComponent<BurrowSystem>();
        }
        CreateAlertText();
        agent.speed = walkSpeed;
        PickWanderTarget();
    }

    void OnDestroy()
    {
        if (counted) { ChaserCount--; counted = false; }
    }

    void Update()
    {
        if (GameManager.I != null && GameManager.I.State != GameState.Playing)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            SetCounted(false);
            return;
        }
        if (worm == null || !agent.isOnNavMesh) return;

        stateTimer += Time.deltaTime;
        callCooldown -= Time.deltaTime;

        ReactToDecoy();

        switch (Current)
        {
            case State.Wander: TickWander(); break;
            case State.Alert:  TickAlert();  break;
            case State.Chase:  TickChase();  break;
            case State.Peck:   TickPeck();   break;
            case State.Search: TickSearch(); break;
        }
    }

    // ---------- 誘餌 ----------

    void ReactToDecoy()
    {
        var decoy = WormSkills.ActiveDecoy;
        if (decoy == null) return;
        if (Current == State.Peck) return;

        float d = Vector3.Distance(transform.position, decoy.position);
        if (d > 14f) return;

        // 追很近的真蚯蚓不會被騙
        if (Current == State.Chase && HorizontalDist(worm.position) < 3.5f) return;

        lastKnownPos = decoy.position;
        if (Current != State.Search) Enter(State.Search);
    }

    // ---------- 各狀態 ----------

    void TickWander()
    {
        agent.isStopped = false;
        agent.speed = walkSpeed;

        if (!agent.pathPending && agent.remainingDistance < 0.6f)
            PickWanderTarget();

        if (CanSeeWorm()) { Enter(State.Alert); return; }
        if (HearsVibration()) { lastKnownPos = worm.position; Enter(State.Search); }
    }

    void TickAlert()
    {
        agent.isStopped = true;
        FaceTowards(worm.position);

        if (stateTimer >= alertTime)
        {
            if (CanSeeWorm()) Enter(State.Chase);
            else { lastKnownPos = worm.position; Enter(State.Search); }
        }
    }

    void TickChase()
    {
        agent.isStopped = false;
        agent.speed = chaseSpeed;

        if (CanSeeWorm())
        {
            loseSightTimer = 0f;
            lastKnownPos = worm.position;
            agent.SetDestination(worm.position);
        }
        else
        {
            loseSightTimer += Time.deltaTime;
            if (loseSightTimer >= loseTargetTime) { Enter(State.Search); return; }
        }

        if (HorizontalDist(worm.position) <= peckRange && CanSeeWorm())
            Enter(State.Peck);
    }

    void TickPeck()
    {
        agent.isStopped = true;
        FaceTowards(worm.position);

        if (!peckResolved && stateTimer >= peckWindup)
        {
            peckResolved = true;
            bool wormOnSurface = wormBurrow == null || !wormBurrow.IsBurrowed;
            bool inRange = HorizontalDist(worm.position) <= peckKillRadius;

            SynthSfx.PlayAt("peck", transform.position, 0.8f);
            ParticleFx.Burst(transform.position + transform.forward * 0.5f + Vector3.up * 0.1f,
                new Color(0.5f, 0.36f, 0.2f), 10, 2f, 0.1f, 1.5f);

            if (wormOnSurface && inRange)
            {
                if (GameManager.I != null) GameManager.I.GameOver();
                return;
            }

            // 險過：啄下去的瞬間你剛好鑽土躲掉
            if (peckThreatened && wormBurrow != null && wormBurrow.IsBurrowed)
            {
                if (GameManager.I != null) GameManager.I.CloseCall();
            }
        }

        if (stateTimer >= peckWindup + 0.35f)
        {
            if (CanSeeWorm()) Enter(State.Chase);
            else { lastKnownPos = worm.position; Enter(State.Search); }
        }
    }

    void TickSearch()
    {
        agent.isStopped = false;
        agent.speed = walkSpeed * 1.4f;

        if (!agent.pathPending && agent.remainingDistance < 0.8f)
        {
            Vector2 c = Random.insideUnitCircle * 2.5f;
            SetDestinationSafe(lastKnownPos + new Vector3(c.x, 0f, c.y));
        }

        if (CanSeeWorm()) { Enter(State.Chase); return; }
        if (HearsVibration()) { lastKnownPos = worm.position; stateTimer = 0f; }
        if (stateTimer >= searchTime) Enter(State.Wander);
    }

    void Enter(State next)
    {
        var prev = Current;
        Current = next;
        stateTimer = 0f;
        loseSightTimer = 0f;
        peckResolved = false;

        if (next == State.Peck)
            peckThreatened = (wormBurrow == null || !wormBurrow.IsBurrowed) &&
                             HorizontalDist(worm.position) <= peckKillRadius;

        if (next == State.Alert && prev == State.Wander)
            SynthSfx.PlayAt("alert", transform.position, 0.55f);

        if (next == State.Chase && prev != State.Peck)
        {
            SynthSfx.PlayAt("cluck", transform.position, 0.6f, Random.Range(0.9f, 1.15f));
            if (body != null && body.isRooster && callCooldown <= 0f)
            {
                callCooldown = 12f;
                Crow();
            }
        }

        SetCounted(next == State.Chase || next == State.Peck);

        if (next == State.Search) SetDestinationSafe(lastKnownPos);
        UpdateAlertText();
    }

    void SetCounted(bool on)
    {
        if (on == counted) return;
        counted = on;
        ChaserCount += on ? 1 : -1;
    }

    /// 公雞啼叫：附近的雞全部過來查看蚯蚓最後位置
    void Crow()
    {
        SynthSfx.PlayAt("crow", transform.position, 0.9f);
        foreach (var other in FindObjectsOfType<ChickenAI>())
        {
            if (other == this) continue;
            if (Vector3.Distance(other.transform.position, transform.position) > 14f) continue;
            other.ReceiveCall(worm.position);
        }
    }

    public void ReceiveCall(Vector3 pos)
    {
        if (Current == State.Chase || Current == State.Peck) return;
        lastKnownPos = pos;
        Enter(State.Search);
    }

    // ---------- 感知 ----------

    bool CanSeeWorm()
    {
        if (wormBurrow != null && wormBurrow.IsBurrowed) return false;

        Vector3 toWorm = worm.position - transform.position;
        float dist = toWorm.magnitude;

        bool wormMoving = (wormCtrl != null && wormCtrl.NoiseRadius > 0f) ||
                          (wormRb != null && wormRb.velocity.sqrMagnitude > 0.2f);
        float range = wormMoving ? visionRange : visionRange * 0.5f;
        if (dist > range) return false;

        if (Vector3.Angle(transform.forward, toWorm) > visionAngle * 0.5f) return false;

        Vector3 eye = transform.position + Vector3.up * 0.9f;
        Vector3 target = worm.position + Vector3.up * 0.2f;
        if (Physics.Linecast(eye, target, out RaycastHit hit, 1 << 0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.CompareTag("SoftSoil") && !hit.collider.CompareTag("HardGround"))
                return false;
        }
        return true;
    }

    bool HearsVibration()
    {
        if (wormCtrl == null || wormCtrl.NoiseRadius <= 0f) return false;
        return HorizontalDist(worm.position) <= wormCtrl.NoiseRadius;
    }

    // ---------- 程序動畫 ----------

    void LateUpdate()
    {
        if (alertText != null && Camera.main != null)
            alertText.transform.rotation = Quaternion.LookRotation(
                alertText.transform.position - Camera.main.transform.position);

        if (body == null) return;

        // 從基準姿勢開始，每幀重算
        float speed = agent != null ? agent.velocity.magnitude : 0f;
        walkPhase += speed * 5.5f * Time.deltaTime;

        // 擺腿
        float swing = Mathf.Sin(walkPhase) * Mathf.Clamp01(speed / 1.2f) * 35f;
        if (body.legL != null) body.legL.localRotation = Quaternion.Euler(swing, 0f, 0f);
        if (body.legR != null) body.legR.localRotation = Quaternion.Euler(-swing, 0f, 0f);

        // 頭部
        if (body.head != null)
        {
            Vector3 headPos = body.headBasePos;
            Quaternion headRot = body.headBaseRot;

            if (Current == State.Wander)
            {
                headPos += new Vector3(0f, Mathf.Sin(walkPhase * 2f) * 0.03f, 0f); // 走路點頭
            }
            else if (Current == State.Alert)
            {
                headRot = body.headBaseRot * Quaternion.Euler(0f, 0f, 24f * Mathf.Sin(stateTimer * 12f > 1.5f ? 1f : stateTimer * 8f)); // 歪頭
            }
            else if (Current == State.Chase)
            {
                headPos += new Vector3(0f, -0.06f, 0.14f); // 脖子前伸
            }
            else if (Current == State.Peck)
            {
                float p = stateTimer < peckWindup
                    ? stateTimer / peckWindup                                  // 抬頭蓄力→
                    : Mathf.Max(0f, 1f - (stateTimer - peckWindup) / 0.2f);    // 猛啄下去
                float lunge = stateTimer < peckWindup ? -0.1f * p : 0.45f * p;
                headPos += new Vector3(0f, -lunge * 0.8f, lunge * 0.5f + 0.1f);
            }

            body.head.localPosition = Vector3.Lerp(body.head.localPosition, headPos, 0.5f);
            body.head.localRotation = Quaternion.Slerp(body.head.localRotation, headRot, 0.5f);
        }

        // 翅膀：追擊時張開拍動
        bool flap = Current == State.Chase || Current == State.Peck;
        float flapAng = flap ? 45f + Mathf.Sin(Time.time * 22f) * 25f : 0f;
        if (body.wingL != null)
            body.wingL.localRotation = Quaternion.Slerp(body.wingL.localRotation,
                body.wingLBase * Quaternion.Euler(0f, 0f, flapAng), 0.4f);
        if (body.wingR != null)
            body.wingR.localRotation = Quaternion.Slerp(body.wingR.localRotation,
                body.wingRBase * Quaternion.Euler(0f, 0f, -flapAng), 0.4f);
    }

    // ---------- 工具 ----------

    float HorizontalDist(Vector3 pos)
    {
        Vector3 a = transform.position; a.y = 0f;
        pos.y = 0f;
        return Vector3.Distance(a, pos);
    }

    void FaceTowards(Vector3 pos)
    {
        Vector3 dir = pos - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, Quaternion.LookRotation(dir), 360f * Time.deltaTime);
    }

    void PickWanderTarget()
    {
        Vector2 c = Random.insideUnitCircle * wanderRadius;
        SetDestinationSafe(transform.position + new Vector3(c.x, 0f, c.y));
    }

    void SetDestinationSafe(Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    // ---------- 警覺指示 ----------

    void CreateAlertText()
    {
        var go = new GameObject("AlertText");
        go.transform.SetParent(transform, false);
        float h = body != null && body.kind == ChickenBody.Kind.Rooster ? 2.2f :
                  body != null && body.kind == ChickenBody.Kind.Chick ? 1.1f : 1.7f;
        go.transform.localPosition = new Vector3(0f, h, 0f);
        alertText = go.AddComponent<TextMesh>();
        alertText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        alertText.GetComponent<MeshRenderer>().sharedMaterial = alertText.font.material;
        alertText.fontSize = 48;
        alertText.characterSize = 0.08f;
        alertText.anchor = TextAnchor.LowerCenter;
        alertText.alignment = TextAlignment.Center;
        alertText.text = "";
    }

    void UpdateAlertText()
    {
        if (alertText == null) return;
        switch (Current)
        {
            case State.Alert:  alertText.text = "?"; alertText.color = Color.yellow; break;
            case State.Search: alertText.text = "?"; alertText.color = new Color(1f, 0.6f, 0f); break;
            case State.Chase:
            case State.Peck:   alertText.text = "!"; alertText.color = Color.red; break;
            default:           alertText.text = ""; break;
        }
    }
}
