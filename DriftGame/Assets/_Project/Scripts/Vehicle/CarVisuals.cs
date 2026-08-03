using UnityEngine;

/// 讓輪胎模型跟隨 WheelCollider 的位置與旋轉（含懸吊伸縮與滾動）。
public class CarVisuals : MonoBehaviour
{
    [System.Serializable]
    public struct WheelPair
    {
        public WheelCollider collider;
        public Transform mesh;
    }

    public WheelPair[] wheels;

    void Update()
    {
        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i].collider == null || wheels[i].mesh == null) continue;
            wheels[i].collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
            wheels[i].mesh.SetPositionAndRotation(pos, rot);
        }
    }
}
