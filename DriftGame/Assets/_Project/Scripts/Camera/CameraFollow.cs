using UnityEngine;

/// 甩尾感追尾攝影機：跟隨「車輛速度方向」而非車頭朝向，
/// 甩尾時自然看到車身橫滑的角度；速度越快 FOV 越大。
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float distance = 6.8f;
    public float height = 2.7f;
    public float positionDamping = 6f;
    public float directionDamping = 3.5f;
    public float lookHeight = 1.2f;

    [Header("FOV")]
    public float baseFov = 60f;
    public float maxFov = 76f;
    public float maxFovSpeed = 45f; // m/s 時達到最大 FOV

    [Header("甩尾鏡頭")]
    [Tooltip("甩尾時鏡頭往滑移方向外偏，看得到車身側面")]
    public float driftLateralOffset = 2.2f;
    [Tooltip("甩尾時鏡頭跟著側傾（度）")]
    public float driftRoll = 5f;

    [Header("震動")]
    public float shakeDecay = 3.2f;

    Rigidbody targetRb;
    CarController targetCar;
    DriftDetector targetDrift;
    Camera cam;
    Vector3 smoothedDir = Vector3.forward;
    float lateralOffset;
    float rollAngle;
    float shakeAmount;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (target != null) SetTarget(target);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        targetRb = target.GetComponent<Rigidbody>();
        targetCar = target.GetComponent<CarController>();
        targetDrift = target.GetComponent<DriftDetector>();
        smoothedDir = FlatForward();
        SnapToTarget();
    }

    /// 外部觸發鏡頭震動（碰撞、落地）。amount 建議 0.1～1.0
    public void Shake(float amount)
    {
        shakeAmount = Mathf.Min(1.4f, shakeAmount + amount);
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 flatVel = targetRb != null ? targetRb.velocity : Vector3.zero;
        flatVel.y = 0f;
        float speed = flatVel.magnitude;

        Vector3 desiredDir = speed > 3f ? flatVel.normalized : FlatForward();
        smoothedDir = Vector3.Slerp(smoothedDir, desiredDir, directionDamping * Time.deltaTime);

        // 甩尾時鏡頭往外側偏移＋側傾，把車身側面帶進畫面
        float slip = targetDrift != null ? targetDrift.SlipAngle : 0f;
        float slip01 = Mathf.Clamp(slip / 45f, -1f, 1f);
        lateralOffset = Mathf.Lerp(lateralOffset, slip01 * driftLateralOffset, 4f * Time.deltaTime);
        rollAngle = Mathf.Lerp(rollAngle, -slip01 * driftRoll, 4f * Time.deltaTime);

        Vector3 right = Vector3.Cross(Vector3.up, smoothedDir);
        Vector3 desiredPos = target.position - smoothedDir * distance
                             + Vector3.up * height + right * lateralOffset;
        transform.position = Vector3.Lerp(transform.position, desiredPos, positionDamping * Time.deltaTime);
        transform.LookAt(target.position + Vector3.up * lookHeight);

        // 震動：位置抖動 + 輕微翻滾
        if (shakeAmount > 0.001f)
        {
            float t = Time.time * 38f;
            Vector3 jitter = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f,
                                         Mathf.PerlinNoise(0f, t) - 0.5f,
                                         Mathf.PerlinNoise(t, t) - 0.5f) * shakeAmount * 0.9f;
            transform.position += jitter;
            rollAngle += (Mathf.PerlinNoise(t * 0.7f, 4f) - 0.5f) * shakeAmount * 12f;
            shakeAmount = Mathf.MoveTowards(shakeAmount, 0f, shakeDecay * Time.deltaTime);
        }

        transform.rotation *= Quaternion.Euler(0f, 0f, rollAngle);

        // FOV：速度感 + 全油門再推一點
        if (cam != null)
        {
            float throttleKick = targetCar != null ? targetCar.Throttle01 * 3.5f : 0f;
            float targetFov = Mathf.Lerp(baseFov, maxFov, speed / maxFovSpeed) + throttleKick;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 3.5f * Time.deltaTime);
        }
    }

    Vector3 FlatForward()
    {
        Vector3 f = target.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
    }

    void SnapToTarget()
    {
        transform.position = target.position - smoothedDir * distance + Vector3.up * height;
        transform.LookAt(target.position + Vector3.up * lookHeight);
    }
}
