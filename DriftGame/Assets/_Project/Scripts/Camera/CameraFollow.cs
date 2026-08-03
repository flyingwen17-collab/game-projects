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

    Rigidbody targetRb;
    Camera cam;
    Vector3 smoothedDir = Vector3.forward;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (target != null) SetTarget(target);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        targetRb = target.GetComponent<Rigidbody>();
        smoothedDir = FlatForward();
        SnapToTarget();
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 flatVel = targetRb != null ? targetRb.velocity : Vector3.zero;
        flatVel.y = 0f;
        float speed = flatVel.magnitude;

        Vector3 desiredDir = speed > 3f ? flatVel.normalized : FlatForward();
        smoothedDir = Vector3.Slerp(smoothedDir, desiredDir, directionDamping * Time.deltaTime);

        Vector3 desiredPos = target.position - smoothedDir * distance + Vector3.up * height;
        transform.position = Vector3.Lerp(transform.position, desiredPos, positionDamping * Time.deltaTime);
        transform.LookAt(target.position + Vector3.up * lookHeight);

        if (cam != null)
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView,
                Mathf.Lerp(baseFov, maxFov, speed / maxFovSpeed), 3f * Time.deltaTime);
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
