using UnityEngine;

// 灰盒攝影機：斜俯環繞定點，微幅跟隨兩力士中點（企劃 §6 多人模式「轉播感」的雛形）
public class CameraRig : MonoBehaviour
{
    public Transform A;
    public Transform B;
    public Vector3 BasePosition = new Vector3(0f, 6.5f, -8.5f);
    public Vector3 LookTarget = new Vector3(0f, 0.8f, 0f);

    void LateUpdate()
    {
        Vector3 mid = Vector3.zero;
        if (A != null && B != null)
        {
            mid = (A.position + B.position) * 0.5f;
            mid.y = 0f;
        }
        transform.position = Vector3.Lerp(transform.position, BasePosition + mid * 0.25f, 4f * Time.deltaTime);
        transform.LookAt(LookTarget + mid * 0.5f);
    }
}
