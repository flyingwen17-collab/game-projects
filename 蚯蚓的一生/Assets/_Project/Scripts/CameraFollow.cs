using UnityEngine;
using UnityEngine.InputSystem;

/// 攝影機：低角度跟隨蚯蚓，Q/E 旋轉
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float distance = 7f;
    public float height = 4.5f;
    public float smooth = 8f;
    public float rotateSpeed = 100f;

    float yaw;
    float shakeAmp;

    public static CameraFollow Main { get; private set; }

    /// 畫面震動（死亡、險過時用）
    public void Shake(float amount)
    {
        shakeAmp = Mathf.Max(shakeAmp, amount);
    }

    void Awake()
    {
        Main = this;
    }

    void Start()
    {
        if (target != null)
            yaw = target.eulerAngles.y;
        SnapToTarget();
    }

    void LateUpdate()
    {
        if (target == null) return;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.qKey.isPressed) yaw -= rotateSpeed * Time.unscaledDeltaTime;
            if (kb.eKey.isPressed) yaw += rotateSpeed * Time.unscaledDeltaTime;
        }

        Vector3 anchor = AnchorPos();
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 want = anchor + rot * new Vector3(0f, height, -distance);
        float t = 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, want, t);
        transform.LookAt(anchor + Vector3.up * 0.5f);

        if (shakeAmp > 0.001f)
        {
            transform.position += Random.insideUnitSphere * shakeAmp;
            shakeAmp = Mathf.MoveTowards(shakeAmp, 0f, Time.unscaledDeltaTime * 1.2f);
        }
    }

    Vector3 AnchorPos()
    {
        // 蚯蚓在地下時攝影機不跟著下沉
        Vector3 p = target.position;
        p.y = Mathf.Max(p.y, 0f);
        return p;
    }

    void SnapToTarget()
    {
        if (target == null) return;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        transform.position = AnchorPos() + rot * new Vector3(0f, height, -distance);
        transform.LookAt(AnchorPos() + Vector3.up * 0.5f);
    }
}
