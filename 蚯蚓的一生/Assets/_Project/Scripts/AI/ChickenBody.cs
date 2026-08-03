using UnityEngine;

/// 雞的身體部件與屬性（由 ChickenFactory 填好，ChickenAI 讀取）
public class ChickenBody : MonoBehaviour
{
    public enum Kind { Hen, Chick, Rooster }

    public Kind kind = Kind.Hen;
    public bool isRooster => kind == Kind.Rooster;

    [Header("屬性")]
    public float walkSpeed = 1.6f;
    public float chaseSpeed = 4.6f;
    public float visionRange = 10f;
    public float visionAngle = 120f;
    public float alertTime = 0.8f;

    [Header("部件")]
    public Transform head;
    public Transform legL, legR;
    public Transform wingL, wingR;

    [HideInInspector] public Vector3 headBasePos;
    [HideInInspector] public Quaternion headBaseRot;
    [HideInInspector] public Quaternion wingLBase, wingRBase;

    public void CacheBase()
    {
        if (head != null) { headBasePos = head.localPosition; headBaseRot = head.localRotation; }
        if (wingL != null) wingLBase = wingL.localRotation;
        if (wingR != null) wingRBase = wingR.localRotation;
    }
}
