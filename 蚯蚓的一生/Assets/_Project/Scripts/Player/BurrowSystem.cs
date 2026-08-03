using UnityEngine;
using UnityEngine.InputSystem;

/// 鑽土系統：按住 Space 潛入地下（僅限軟土），體力耗盡強制彈出
public class BurrowSystem : MonoBehaviour
{
    public float burrowDepth = 1.1f;      // 地下深度
    public float surfaceY = 0.3f;         // 地面時 root 高度
    public float transitionSpeed = 6f;    // 升降速度
    public float minStaminaToBurrow = 10f;

    public bool IsBurrowed { get; private set; }

    WormStamina stamina;
    Rigidbody rb;
    int wormLayer, undergroundLayer;
    float targetY;
    Transform mound; // 地表土丘指示（鑽土時顯示位置）

    void Awake()
    {
        stamina = GetComponent<WormStamina>();
        rb = GetComponent<Rigidbody>();
        wormLayer = LayerMask.NameToLayer("Worm");
        undergroundLayer = LayerMask.NameToLayer("Underground");
        targetY = surfaceY;
        CreateMound();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || GameManager.I == null || GameManager.I.State != GameState.Playing) return;

        bool holdBurrow = kb.spaceKey.isPressed;

        if (!IsBurrowed && kb.spaceKey.wasPressedThisFrame)
        {
            if (stamina.Current >= minStaminaToBurrow && CanBurrowHere())
                Enter();
        }
        else if (IsBurrowed && (!holdBurrow || stamina.Current <= 0f))
        {
            Exit();
        }
    }

    void FixedUpdate()
    {
        // 平滑升降到目標深度
        float newY = Mathf.MoveTowards(rb.position.y, targetY, transitionSpeed * Time.fixedDeltaTime);
        if (IsBurrowed)
            rb.position = new Vector3(rb.position.x, newY, rb.position.z);
        else if (rb.position.y < targetY - 0.02f)
            rb.position = new Vector3(rb.position.x, newY, rb.position.z); // 出土上升段
    }

    void LateUpdate()
    {
        if (mound != null && mound.gameObject.activeSelf)
            mound.position = new Vector3(transform.position.x, 0.06f, transform.position.z);
    }

    /// 腳下是不是軟土（硬地/石板不能鑽）
    public bool CanBurrowHere()
    {
        Vector3 origin = transform.position + Vector3.up * 2f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            return !hit.collider.CompareTag("HardGround");
        return false;
    }

    void Enter()
    {
        IsBurrowed = true;
        SetLayerRecursive(gameObject, undergroundLayer);
        rb.useGravity = false;
        targetY = surfaceY - burrowDepth;
        if (mound != null) mound.gameObject.SetActive(true);
    }

    void Exit()
    {
        IsBurrowed = false;
        SetLayerRecursive(gameObject, wormLayer);
        rb.useGravity = true;
        targetY = surfaceY;
        if (mound != null) mound.gameObject.SetActive(false);
    }

    void CreateMound()
    {
        var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        m.name = "BurrowMound";
        Destroy(m.GetComponent<Collider>());
        m.transform.localScale = new Vector3(1.05f, 0.22f, 1.05f);
        // 土丘用 AI 泥土貼圖，更像真的隆起的土
        var dirtTex = Resources.Load<Texture2D>("Art/dirt_tile");
        if (dirtTex != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", dirtTex);
            mat.mainTexture = dirtTex;
            m.GetComponent<Renderer>().sharedMaterial = mat;
        }
        else
        {
            m.GetComponent<Renderer>().sharedMaterial = RuntimeArt.Mat(new Color(0.35f, 0.22f, 0.12f));
        }
        m.SetActive(false);
        mound = m.transform;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
