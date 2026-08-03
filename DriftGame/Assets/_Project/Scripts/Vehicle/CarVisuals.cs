using UnityEngine;

/// 輪胎模型跟隨懸吊與轉向，滾動角度由物理算出的輪胎角速度驅動。
/// （WheelCollider 的摩擦已歸零、不再自己轉，所以滾動必須自己給。）
public class CarVisuals : MonoBehaviour
{
    [System.Serializable]
    public struct WheelPair
    {
        public WheelCollider collider;
        public Transform mesh;
    }

    public WheelPair[] wheels;

    CarController car;
    float[] spinAngle;

    void Awake()
    {
        car = GetComponent<CarController>();
        spinAngle = new float[wheels != null ? wheels.Length : 0];
    }

    void Update()
    {
        if (wheels == null) return;

        for (int i = 0; i < wheels.Length; i++)
        {
            var mesh = wheels[i].mesh;
            if (mesh == null || car == null || i >= car.WheelWorldPos.Length) continue;

            // 位置與轉向來自 CarController 的射線懸吊（WheelCollider 已停用）
            Vector3 pos = car.WheelWorldPos[i];
            Quaternion rot = car.WheelWorldRot[i];

            // 滾動角度自己積分：ω(rad/s) → 度/秒
            if (spinAngle != null && i < car.WheelOmega.Length)
            {
                spinAngle[i] += car.WheelOmega[i] * Mathf.Rad2Deg * Time.deltaTime;
                if (spinAngle[i] > 360f || spinAngle[i] < -360f) spinAngle[i] %= 360f;
                rot *= Quaternion.Euler(spinAngle[i], 0f, 0f);
            }

            mesh.SetPositionAndRotation(pos, rot);
        }
    }
}
