using UnityEngine;
using UnityEngine.InputSystem;

/// 攝影機：第三人稱跟隨 / 第一人稱（C 切換）、Q/E 旋轉、
/// 震動只在受擊或衝刺時、衝刺 FOV 加速感
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float distance = 7f;
    public float height = 4.5f;
    public float smooth = 8f;
    public float rotateSpeed = 100f;

    float yaw;
    float shakeAmp;
    bool firstPerson;
    Camera cam;
    WormController wormCtrl;
    BurrowSystem wormBurrow;

    public static CameraFollow Main { get; private set; }

    /// 只有「被攻擊 / 險過 / 落地衝擊」會呼叫這個
    public void Shake(float amount)
    {
        shakeAmp = Mathf.Max(shakeAmp, amount);
    }

    void Awake()
    {
        Main = this;
        cam = GetComponent<Camera>();
    }

    void Start()
    {
        if (target != null)
        {
            yaw = target.eulerAngles.y;
            wormCtrl = target.GetComponent<WormController>();
            wormBurrow = target.GetComponent<BurrowSystem>();
        }
        SnapToTarget();
    }

    void LateUpdate()
    {
        if (target == null) return;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.cKey.wasPressedThisFrame) firstPerson = !firstPerson;
            if (!firstPerson)
            {
                if (kb.qKey.isPressed) yaw -= rotateSpeed * Time.unscaledDeltaTime;
                if (kb.eKey.isPressed) yaw += rotateSpeed * Time.unscaledDeltaTime;
            }
        }

        bool sprinting = wormCtrl != null && wormCtrl.IsSprinting;
        float t = 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime);

        if (firstPerson)
        {
            // 第一人稱：貼在蚯蚓頭上，跟著身體轉
            bool underground = wormBurrow != null && wormBurrow.IsBurrowed;
            float eyeY = underground ? 0.5f : Mathf.Max(target.position.y, 0.25f) + 0.3f;
            Vector3 eye = new Vector3(target.position.x, eyeY, target.position.z)
                          - target.forward * 0.15f;
            transform.position = Vector3.Lerp(transform.position, eye, t * 1.6f);
            Quaternion look = Quaternion.LookRotation(
                new Vector3(target.forward.x, -0.12f, target.forward.z).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, t * 1.4f);
        }
        else
        {
            Vector3 anchor = new Vector3(target.position.x, 0.35f, target.position.z); // 固定高度，不跟著彈跳
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 want = anchor + rot * new Vector3(0f, height, -distance);
            transform.position = Vector3.Lerp(transform.position, want, t);
            transform.LookAt(anchor + Vector3.up * 0.5f);
        }

        // FOV 加速感：衝刺/無敵時視野拉開
        if (cam != null)
        {
            bool powered = WormSkills.Instance != null && WormSkills.Instance.PowerActive;
            float targetFov = 60f + (sprinting ? 8f : 0f) + (powered ? 5f : 0f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, t);
        }

        // 震動：受擊事件（Shake 呼叫）+ 衝刺微震
        float amp = shakeAmp;
        if (sprinting) amp = Mathf.Max(amp, 0.035f);
        if (amp > 0.001f)
            transform.position += Random.insideUnitSphere * amp;
        shakeAmp = Mathf.MoveTowards(shakeAmp, 0f, Time.unscaledDeltaTime * 1.2f);
    }

    void SnapToTarget()
    {
        if (target == null) return;
        Vector3 anchor = new Vector3(target.position.x, 0.35f, target.position.z);
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        transform.position = anchor + rot * new Vector3(0f, height, -distance);
        transform.LookAt(anchor + Vector3.up * 0.5f);
    }
}
