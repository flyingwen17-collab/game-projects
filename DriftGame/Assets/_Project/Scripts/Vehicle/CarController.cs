using UnityEngine;
using UnityEngine.InputSystem;

/// 車輛主控制：讀取輸入、驅動 WheelCollider、動態調整後輪抓地力做甩尾。
/// 掛在車輛根物件（需有 Rigidbody 與四顆 WheelCollider）。
public enum Drivetrain { FWD, RWD, AWD }

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(DriftDetector))]
public class CarController : MonoBehaviour
{
    [Header("Wheels")]
    public WheelCollider wheelFL;
    public WheelCollider wheelFR;
    public WheelCollider wheelRL;
    public WheelCollider wheelRR;

    [Header("Drivetrain")]
    public Drivetrain drivetrain = Drivetrain.RWD;
    [Range(0f, 1f)] public float awdFrontShare = 0.45f;

    [Header("Engine")]
    public float maxMotorTorque = 2600f;   // 驅動輪總扭力
    public float maxBrakeTorque = 3500f;
    public float handbrakeTorque = 5000f;
    public float topSpeedKmh = 170f;

    [Header("Steering")]
    public float maxSteerLowSpeed = 33f;   // 低速最大轉向角
    public float maxSteerHighSpeed = 12f;  // 高速最大轉向角（防止高速甩飛）
    public float steerSmoothing = 7f;

    [Header("Grip / Drift")]
    public float frontStiffness = 1.7f;
    public float rearStiffness = 1.55f;        // 正常抓地
    public float rearDriftStiffness = 0.9f;    // 甩尾中
    public float rearHandbrakeStiffness = 0.5f;// 手煞車瞬間
    public float gripChangeSpeed = 5f;         // 抓地力變化的平滑速度

    [Header("Assists")]
    public float downforce = 16f;            // 高速下壓力
    public float driftSteerAssist = 1.4f;    // 甩尾中轉向輔助力（rad/s^2）
    public float antiRollForce = 9000f;      // 防側翻
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.45f, 0f);

    Rigidbody rb;
    DriftDetector drift;

    float steerInput;
    float throttleInput;
    bool handbrakeInput;
    float currentSteer;
    float currentRearStiffness;
    Vector3 spawnPos;
    Quaternion spawnRot;

    public float SpeedKmh => rb.velocity.magnitude * 3.6f;
    public bool Handbrake => handbrakeInput;
    public float Throttle01 => Mathf.Max(0f, throttleInput);
    public bool IsBraking { get; private set; }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        drift = GetComponent<DriftDetector>();
        rb.centerOfMass += centerOfMassOffset;
        currentRearStiffness = rearStiffness;
        spawnPos = transform.position;
        spawnRot = transform.rotation;

        SetSidewaysStiffness(wheelFL, frontStiffness);
        SetSidewaysStiffness(wheelFR, frontStiffness);
        SetSidewaysStiffness(wheelRL, rearStiffness);
        SetSidewaysStiffness(wheelRR, rearStiffness);
    }

    void Update()
    {
        ReadInput();

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            ResetCar();
    }

    void ReadInput()
    {
        float steer = 0f, throttle = 0f;
        bool hb = false;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1f;
            hb = kb.spaceKey.isPressed;
        }

        var pad = Gamepad.current;
        if (pad != null)
        {
            float padSteer = pad.leftStick.x.ReadValue();
            if (Mathf.Abs(padSteer) > 0.1f) steer = padSteer;
            float padThrottle = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            if (Mathf.Abs(padThrottle) > 0.05f) throttle = padThrottle;
            hb |= pad.buttonSouth.isPressed;
        }

        steerInput = Mathf.Clamp(steer, -1f, 1f);
        throttleInput = Mathf.Clamp(throttle, -1f, 1f);
        handbrakeInput = hb;
    }

    void FixedUpdate()
    {
        float speed = rb.velocity.magnitude;
        float localForwardSpeed = transform.InverseTransformDirection(rb.velocity).z;

        ApplySteering(speed);
        ApplyMotorAndBrakes(localForwardSpeed);
        ApplyGrip();
        ApplyDownforce(speed);
        ApplyDriftAssist(speed);
        AntiRoll(wheelFL, wheelFR);
        AntiRoll(wheelRL, wheelRR);
    }

    void ApplySteering(float speed)
    {
        float t = Mathf.InverseLerp(5f, 35f, speed);
        float maxSteer = Mathf.Lerp(maxSteerLowSpeed, maxSteerHighSpeed, t);
        currentSteer = Mathf.Lerp(currentSteer, steerInput * maxSteer, steerSmoothing * Time.fixedDeltaTime);
        wheelFL.steerAngle = currentSteer;
        wheelFR.steerAngle = currentSteer;
    }

    void ApplyMotorAndBrakes(float localForwardSpeed)
    {
        float motor = 0f, brake = 0f;

        bool wantsBrake = throttleInput < -0.05f && localForwardSpeed > 1f;
        IsBraking = wantsBrake;
        if (wantsBrake)
        {
            brake = -throttleInput * maxBrakeTorque;
        }
        else if (SpeedKmh < topSpeedKmh)
        {
            motor = throttleInput * maxMotorTorque;
        }

        float frontMotor = 0f, rearMotor = 0f;
        switch (drivetrain)
        {
            case Drivetrain.FWD: frontMotor = motor * 0.5f; break;
            case Drivetrain.RWD: rearMotor = motor * 0.5f; break;
            case Drivetrain.AWD:
                frontMotor = motor * awdFrontShare * 0.5f;
                rearMotor = motor * (1f - awdFrontShare) * 0.5f;
                break;
        }

        // 手煞車時後輪不出力
        if (handbrakeInput) rearMotor = 0f;

        wheelFL.motorTorque = frontMotor;
        wheelFR.motorTorque = frontMotor;
        wheelRL.motorTorque = rearMotor;
        wheelRR.motorTorque = rearMotor;

        wheelFL.brakeTorque = brake;
        wheelFR.brakeTorque = brake;
        float rearBrake = handbrakeInput ? handbrakeTorque : brake * 0.6f;
        wheelRL.brakeTorque = rearBrake;
        wheelRR.brakeTorque = rearBrake;
    }

    void OnDisable()
    {
        // 停用時把車停穩（選車畫面中未選中的車）
        if (wheelFL == null) return;
        foreach (var w in new[] { wheelFL, wheelFR, wheelRL, wheelRR })
        {
            w.motorTorque = 0f;
            w.brakeTorque = 6000f;
            w.steerAngle = 0f;
        }
    }

    void ApplyGrip()
    {
        float target = handbrakeInput ? rearHandbrakeStiffness
                     : drift.IsDrifting ? rearDriftStiffness
                     : rearStiffness;
        currentRearStiffness = Mathf.Lerp(currentRearStiffness, target, gripChangeSpeed * Time.fixedDeltaTime);
        SetSidewaysStiffness(wheelRL, currentRearStiffness);
        SetSidewaysStiffness(wheelRR, currentRearStiffness);
    }

    void ApplyDownforce(float speed)
    {
        rb.AddForce(-transform.up * downforce * speed);
    }

    void ApplyDriftAssist(float speed)
    {
        // 甩尾中補一點轉向力矩，讓玩家更容易控制車身角度
        if (drift.IsDrifting && speed > 5f)
            rb.AddTorque(transform.up * steerInput * driftSteerAssist, ForceMode.Acceleration);
    }

    void AntiRoll(WheelCollider left, WheelCollider right)
    {
        float travelL = 1f, travelR = 1f;
        bool groundedL = left.GetGroundHit(out WheelHit hitL);
        if (groundedL)
            travelL = (-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / left.suspensionDistance;
        bool groundedR = right.GetGroundHit(out WheelHit hitR);
        if (groundedR)
            travelR = (-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / right.suspensionDistance;

        float force = (travelL - travelR) * antiRollForce;
        if (groundedL) rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
        if (groundedR) rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
    }

    void SetSidewaysStiffness(WheelCollider wheel, float stiffness)
    {
        var friction = wheel.sidewaysFriction;
        friction.stiffness = stiffness;
        wheel.sidewaysFriction = friction;
    }

    public void ResetCar()
    {
        float yaw = transform.eulerAngles.y;
        transform.SetPositionAndRotation(transform.position + Vector3.up * 1.5f, Quaternion.Euler(0f, yaw, 0f));
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public void RespawnAtStart()
    {
        transform.SetPositionAndRotation(spawnPos, spawnRot);
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }
}
