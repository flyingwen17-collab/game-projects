using UnityEngine;
using UnityEngine.AI;

/// 雞 AI：閒晃/警覺/追擊/啄食/搜索 + 吃斷尾/飽食/飛撲攻擊/逃離無敵蚯蚓/母雞下蛋
[RequireComponent(typeof(NavMeshAgent))]
public class ChickenAI : MonoBehaviour
{
    public enum State { Wander, Alert, Chase, Peck, Search, EatTail, Full, Fly, Flee }
    public State Current { get; private set; } = State.Wander;

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

    [Header("飽食 / 飛撲")]
    public float fullDuration = 8f;
    public float flyCooldown = 18f;
    public float flyUnlockTime = 50f; // 存活幾秒後解鎖飛撲

    NavMeshAgent agent;
    Transform worm;
    Rigidbody wormRb;
    WormController wormCtrl;
    BurrowSystem wormBurrow;
    ChickenBody body;

    float stateTimer;
    float loseSightTimer;
    float callCooldown;
    float flyCdLeft;
    float eggTimer;
    Vector3 lastKnownPos;
    Vector3 flyTargetXZ;
    Transform flyMarker;
    TextMesh alertText;
    bool peckResolved;
    bool peckThreatened;
    bool flyResolved;
    bool counted;
    bool isLaying;
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
        eggTimer = Random.Range(25f, 45f);
        flyCdLeft = Random.Range(4f, 10f);
        PickWanderTarget();
    }

    void OnDestroy()
    {
        if (counted) { ChaserCount--; counted = false; }
        if (flyMarker != null) Destroy(flyMarker.gameObject);
    }

    void Update()
    {
        if (GameManager.I != null && GameManager.I.State != GameState.Playing)
        {
            if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            SetCounted(false);
            return;
        }
        if (worm == null) return;
        if (!agent.enabled && Current != State.Fly) return; // 保險

        stateTimer += Time.deltaTime;
        callCooldown -= Time.deltaTime;
        flyCdLeft -= Time.deltaTime;

        if (isLaying) { if (agent.enabled) agent.isStopped = true; return; } // 下蛋動畫中

        ReactToWorld();

        switch (Current)
        {
            case State.Wander:  TickWander();  break;
            case State.Alert:   TickAlert();   break;
            case State.Chase:   TickChase();   break;
            case State.Peck:    TickPeck();    break;
            case State.Search:  TickSearch();  break;
            case State.EatTail: TickEatTail(); break;
            case State.Full:    TickFull();    break;
            case State.Fly:     TickFly();     break;
            case State.Flee:    TickFlee();    break;
        }
    }

    // ---------- 世界事件反應（優先權高於狀態） ----------

    void ReactToWorld()
    {
        if (Current == State.Fly || Current == State.Full) return;

        // 蚯蚓無敵 → 快逃！
        if (WormSkills.Instance != null && WormSkills.Instance.PowerActive)
        {
            if (Current != State.Flee) Enter(State.Flee);
            return;
        }

        // 斷尾在附近 → 跑去吃
        if (TailPiece.Active != null && Current != State.Peck && Current != State.EatTail)
        {
            float d = Vector3.Distance(transform.position, TailPiece.Active.transform.position);
            if (d < 13f) Enter(State.EatTail);
        }
    }

    // ---------- 各狀態 ----------

    void TickWander()
    {
        agent.isStopped = false;
        agent.speed = walkSpeed;

        if (!agent.pathPending && agent.remainingDistance < 0.6f)
            PickWanderTarget();

        // 母雞下蛋
        if (body != null && body.kind == ChickenBody.Kind.Hen)
        {
            eggTimer -= Time.deltaTime;
            if (eggTimer <= 0f)
            {
                eggTimer = Random.Range(30f, 50f);
                if (EggPickup.Count < 3)
                    StartCoroutine(LayEggRoutine());
            }
        }

        if (CanSeeWorm()) { Enter(State.Alert); return; }
        if (HearsVibration()) { lastKnownPos = worm.position; Enter(State.Search); }
    }

    /// 下蛋動畫：蹲下→抖兩下→蛋蹦出來（畫面生動）
    System.Collections.IEnumerator LayEggRoutine()
    {
        isLaying = true;
        Vector3 baseScale = transform.localScale;
        SynthSfx.PlayAt("cluck", transform.position, 0.6f, 1.3f);

        // 蹲下
        float t = 0f;
        while (t < 0.3f)
        {
            t += Time.deltaTime;
            float squash = Mathf.Lerp(1f, 0.72f, t / 0.3f);
            transform.localScale = new Vector3(baseScale.x * (2f - squash) * 0.85f, baseScale.y * squash, baseScale.z);
            yield return null;
        }

        // 抖動醞釀
        t = 0f;
        while (t < 0.45f)
        {
            t += Time.deltaTime;
            transform.localRotation *= Quaternion.Euler(0f, Mathf.Sin(Time.time * 40f) * 2.2f, 0f);
            yield return null;
        }

        // 蛋蹦出來！
        Vector3 eggPos = transform.position - transform.forward * 0.5f;
        EggPickup.Create(eggPos);
        SynthSfx.PlayAt("pop", transform.position, 0.7f, 1.1f);
        SynthSfx.PlayAt("cluck", transform.position, 0.7f, 1.5f);
        ParticleFx.Burst(eggPos + Vector3.up * 0.25f,
            Color.white, 10, 2f, 0.25f, 0.7f, 0.6f, "feathers_sheet", false, 4, 2);
        ParticleFx.Burst(eggPos + Vector3.up * 0.2f,
            new Color(1f, 0.95f, 0.7f), 6, 1.5f, 0.3f, 0.1f, 0.5f, "glow_soft", true);

        // 站回來
        t = 0f;
        while (t < 0.2f)
        {
            t += Time.deltaTime;
            float squash = Mathf.Lerp(0.72f, 1f, t / 0.2f);
            transform.localScale = new Vector3(baseScale.x * (2f - squash) * 0.85f + baseScale.x * (squash - 0.72f) * 0.54f, baseScale.y * squash, baseScale.z);
            yield return null;
        }
        transform.localScale = baseScale;
        isLaying = false;
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

        float dist = HorizontalDist(worm.position);

        // 飛撲：解鎖後，距離適中且蚯蚓在地表就有機會起飛
        bool canFly = body != null && body.kind != ChickenBody.Kind.Chick;
        if (canFly && flyCdLeft <= 0f &&
            GameManager.I != null && GameManager.I.SurvivalTime >= flyUnlockTime &&
            dist > 3f && dist < 10f &&
            (wormBurrow == null || !wormBurrow.IsBurrowed))
        {
            Enter(State.Fly);
            return;
        }

        if (dist <= peckRange && CanSeeWorm())
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
                new Color(0.5f, 0.36f, 0.2f), 10, 2f, 0.25f, 1.5f, 0.7f, "dust_puff");

            if (wormOnSurface && inRange)
            {
                if (GameManager.I != null) GameManager.I.GameOver();
                return;
            }

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

    void TickEatTail()
    {
        if (TailPiece.Active == null) { Enter(State.Wander); return; }

        agent.isStopped = false;
        agent.speed = chaseSpeed;
        Vector3 tailPos = TailPiece.Active.transform.position;
        agent.SetDestination(tailPos);

        if (HorizontalDist(tailPos) < 1.0f)
        {
            TailPiece.Active.Consume();
            SynthSfx.PlayAt("cluck", transform.position, 0.7f, 0.85f);
            Enter(State.Full);
        }
    }

    void TickFull()
    {
        // 吃飽了：慢慢晃，不理蚯蚓
        agent.isStopped = false;
        agent.speed = walkSpeed * 0.5f;
        if (!agent.pathPending && agent.remainingDistance < 0.5f)
            PickWanderTarget();

        if (stateTimer >= fullDuration) Enter(State.Wander);
    }

    void TickFlee()
    {
        bool stillPowered = WormSkills.Instance != null && WormSkills.Instance.PowerActive;
        if (!stillPowered) { Enter(State.Wander); return; }

        agent.isStopped = false;
        agent.speed = chaseSpeed * 0.85f; // 比無敵蚯蚓慢——追得上才有吃雞的爽感

        if (!agent.pathPending && agent.remainingDistance < 1f)
        {
            Vector3 away = (transform.position - worm.position).normalized;
            Vector2 jitter = Random.insideUnitCircle * 2f;
            SetDestinationSafe(transform.position + away * 6f + new Vector3(jitter.x, 0f, jitter.y));
        }
    }

    void TickFly()
    {
        const float ascendEnd = 0.8f, trackEnd = 1.8f, diveEnd = 2.15f;
        float t = stateTimer;
        Vector3 pos = transform.position;

        if (t < ascendEnd)
        {
            pos.y = Mathf.Lerp(pos.y, 3.6f, Time.deltaTime * 6f);
            flyTargetXZ = new Vector3(worm.position.x, 0f, worm.position.z);
        }
        else if (t < trackEnd)
        {
            // 空中追蹤蚯蚓
            flyTargetXZ = new Vector3(worm.position.x, 0f, worm.position.z);
            Vector3 target = new Vector3(flyTargetXZ.x, 3.6f, flyTargetXZ.z);
            pos = Vector3.MoveTowards(pos, target, 7f * Time.deltaTime);
        }
        else if (t < diveEnd)
        {
            pos.y = Mathf.MoveTowards(pos.y, 0.55f, 16f * Time.deltaTime);
            pos.x = Mathf.MoveTowards(pos.x, flyTargetXZ.x, 4f * Time.deltaTime);
            pos.z = Mathf.MoveTowards(pos.z, flyTargetXZ.z, 4f * Time.deltaTime);
        }

        transform.position = pos;
        FaceTowards(new Vector3(flyTargetXZ.x, transform.position.y, flyTargetXZ.z));

        // 落點警示圈：越接近俯衝越大越紅
        if (flyMarker != null)
        {
            flyMarker.position = new Vector3(flyTargetXZ.x, 0.04f, flyTargetXZ.z);
            float warn = Mathf.Clamp01((t - ascendEnd) / (trackEnd - ascendEnd));
            float s = Mathf.Lerp(0.8f, 2.2f, warn);
            flyMarker.localScale = new Vector3(s, 0.02f, s);
        }

        // 羽毛飄落
        if (Random.value < 0.06f)
            ParticleFx.Burst(transform.position, Color.white, 1, 0.6f, 0.22f, 0.5f, 1.2f, "feathers_sheet", false, 4, 2);

        if (!flyResolved && t >= diveEnd)
        {
            flyResolved = true;
            float d = HorizontalDist(worm.position);
            bool wormOnSurface = wormBurrow == null || !wormBurrow.IsBurrowed;

            ParticleFx.Burst(new Vector3(transform.position.x, 0.15f, transform.position.z),
                new Color(0.5f, 0.36f, 0.2f), 30, 4.5f, 0.4f, 1.6f, 0.8f, "dust_puff");
            SynthSfx.PlayAt("peck", transform.position, 1f, 0.8f);

            if (wormOnSurface && d <= 1.7f)
            {
                if (GameManager.I != null) GameManager.I.GameOver();
                return;
            }
            if (d < 5f && CameraFollow.Main != null) CameraFollow.Main.Shake(0.15f); // 有驚無險的落地震動
            if (!wormOnSurface && d <= 1.7f && GameManager.I != null) GameManager.I.CloseCall();
        }

        if (t >= diveEnd + 0.6f)
        {
            LandAndResume();
        }
    }

    void LandAndResume()
    {
        flyCdLeft = flyCooldown;
        if (flyMarker != null) { Destroy(flyMarker.gameObject); flyMarker = null; }
        agent.enabled = true;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            agent.Warp(hit.position);
        lastKnownPos = worm.position;
        Enter(State.Search);
    }

    void Enter(State next)
    {
        var prev = Current;
        Current = next;
        stateTimer = 0f;
        loseSightTimer = 0f;
        peckResolved = false;
        flyResolved = false;

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

        if (next == State.Fly)
        {
            agent.enabled = false;
            SynthSfx.PlayAt("crow", transform.position, 0.7f, 1.2f);
            ParticleFx.Burst(transform.position + Vector3.up * 0.8f,
                Color.white, 12, 2.5f, 0.3f, 0.4f, 0.8f, "feathers_sheet", false, 4, 2);
            MakeFlyMarker();
        }

        SetCounted(next == State.Chase || next == State.Peck || next == State.Fly);

        if (next == State.Search) SetDestinationSafe(lastKnownPos);
        UpdateAlertText();
    }

    void MakeFlyMarker()
    {
        var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        m.name = "DiveMarker";
        Destroy(m.GetComponent<Collider>());
        m.transform.localScale = new Vector3(0.8f, 0.02f, 0.8f);
        m.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(new Color(0.9f, 0.15f, 0.1f));
        flyMarker = m.transform;
    }

    void SetCounted(bool on)
    {
        if (on == counted) return;
        counted = on;
        ChaserCount += on ? 1 : -1;
    }

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
        if (Current == State.Chase || Current == State.Peck || Current == State.Fly ||
            Current == State.Full || Current == State.Flee) return;
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

        float speed = (agent != null && agent.enabled) ? agent.velocity.magnitude :
                      (Current == State.Fly ? 5f : 0f);
        walkPhase += speed * 5.5f * Time.deltaTime;

        float swing = Mathf.Sin(walkPhase) * Mathf.Clamp01(speed / 1.2f) * 35f;
        if (Current == State.Fly) swing = 15f; // 飛行時腳縮起
        if (body.legL != null) body.legL.localRotation = Quaternion.Euler(swing, 0f, 0f);
        if (body.legR != null) body.legR.localRotation = Quaternion.Euler(-swing, 0f, 0f);

        if (body.head != null)
        {
            Vector3 headPos = body.headBasePos;
            Quaternion headRot = body.headBaseRot;

            if (Current == State.Wander || Current == State.Full)
                headPos += new Vector3(0f, Mathf.Sin(walkPhase * 2f) * 0.03f, 0f);
            else if (Current == State.Alert)
                headRot = body.headBaseRot * Quaternion.Euler(0f, 0f, 24f);
            else if (Current == State.Chase || Current == State.Fly || Current == State.Flee)
                headPos += new Vector3(0f, -0.06f, 0.14f);
            else if (Current == State.Peck)
            {
                float p = stateTimer < peckWindup
                    ? stateTimer / peckWindup
                    : Mathf.Max(0f, 1f - (stateTimer - peckWindup) / 0.2f);
                float lunge = stateTimer < peckWindup ? -0.1f * p : 0.45f * p;
                headPos += new Vector3(0f, -lunge * 0.8f, lunge * 0.5f + 0.1f);
            }

            body.head.localPosition = Vector3.Lerp(body.head.localPosition, headPos, 0.5f);
            body.head.localRotation = Quaternion.Slerp(body.head.localRotation, headRot, 0.5f);
        }

        bool flap = Current == State.Chase || Current == State.Peck || Current == State.Flee;
        bool bigFlap = Current == State.Fly;
        float flapAng = bigFlap ? 70f + Mathf.Sin(Time.time * 30f) * 35f :
                        flap ? 45f + Mathf.Sin(Time.time * 22f) * 25f : 0f;
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
        if (!agent.enabled) return;
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    // ---------- 頭上符號 ----------

    void CreateAlertText()
    {
        var go = new GameObject("AlertText");
        go.transform.SetParent(transform, false);
        float h = body != null && body.kind == ChickenBody.Kind.Rooster ? 2.2f :
                  body != null && body.kind == ChickenBody.Kind.Chick ? 1.1f : 1.7f;
        go.transform.localPosition = new Vector3(0f, h, 0f);
        alertText = go.AddComponent<TextMesh>();
        var cjk = Font.CreateDynamicFontFromOSFont("Microsoft JhengHei", 48);
        alertText.font = cjk != null ? cjk : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            case State.Alert:   alertText.text = "?"; alertText.color = Color.yellow; break;
            case State.Search:  alertText.text = "?"; alertText.color = new Color(1f, 0.6f, 0f); break;
            case State.Chase:
            case State.Peck:    alertText.text = "!"; alertText.color = Color.red; break;
            case State.Fly:     alertText.text = "!!"; alertText.color = new Color(1f, 0.1f, 0.1f); break;
            case State.EatTail: alertText.text = "~"; alertText.color = new Color(1f, 0.5f, 0.7f); break;
            case State.Full:    alertText.text = "飽"; alertText.color = new Color(0.4f, 0.9f, 0.4f); break;
            case State.Flee:    alertText.text = "!"; alertText.color = new Color(0.3f, 0.7f, 1f); break;
            default:            alertText.text = ""; break;
        }
    }
}
