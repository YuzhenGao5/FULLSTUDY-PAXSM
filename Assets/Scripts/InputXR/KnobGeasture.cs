// 文件名：KnobGrabByWrist.cs
using UnityEngine;
using UnityEngine.XR;          // InputDevices / CommonUsages
using UnityEngine.Events;

public class KnobGrabByWrist : MonoBehaviour
{
    [Header("核心引用")]
    [Tooltip("当前旋钮的 KnobCore")]
    public KnobCore knobCore;

    [Tooltip("KnobMode 的管理器（可选），用于 Enter/Exit KnobMode")]
    public KnobModeManager knobModeManager;

    [Tooltip("Knob 中心点（不填会自动用 knobCore.knob 或本物体）")]
    public Transform knobCenter;

    [Tooltip("右手控制器 Transform（XR Origin/Camera Offset/Right Controller）")]
    public Transform rightController;

    [Header("抓取条件")]
    [Tooltip("手柄与 Knob 中心的最大抓取距离（米）")]
    public float grabRadius = 0.15f;

    [Header("视觉：手 & 控制器")]
    [Tooltip("抓住时显示的手的根节点（推荐：你的 Cube，下面有 正手/反手 手模）")]
    public GameObject handVisualRoot;

    [Tooltip("抓住时隐藏的手柄模型（Right Controller 下的 ControllerModel），可为空")]
    public GameObject controllerVisualToHide;

    [Header("正手 / 反手 手模切换")]
    [Tooltip("主手：激光“穿过”Knob 时出现（例如 RightHandQuestVisual_Positive）")]
    public GameObject positiveHand;

    [Tooltip("侧手：只有当激光几乎平行 Knob 面才出现（例如 RightHandQuestVisual_Negative）")]
    public GameObject negativeHand;

    [Tooltip("判断“激光是否几乎对着面法线”：|dot(forward, normal)| >= 阈值 → 激光穿过 Knob → 主手")]
    [Range(0f, 1f)]
    public float parallelDotThreshold = 0.8f;

    [Header("手模相对 Knob 的 Y 角度")]
    [Tooltip("把『手柄相对于 Knob 的 Y 角度』再加一个偏移，用来对齐建模时的 0° 方向")]
    public float handYawOffset = 0f;

    [Header("旋转映射（按刻度）")]
    [Tooltip("每转多少“手腕角度（度）”≈ 1 个刻度")]
    public float degreesPerStep = 10f;

    [Tooltip("手腕角度变化小于该值时不响应（度），滤掉抖动")]
    public float deadZoneDegrees = 3f;

    [Tooltip("限制最大扭腕角度（度），防止一下子转太多导致从最左跳到最右）")]
    public float maxTwistDegrees = 80f;

    [Tooltip("是否根据 TickRing 自动估计 degreesPerStep")]
    public bool autoDegreesPerStepFromRing = true;

    [Header("事件（可选）")]
    public UnityEvent OnGrabStart;
    public UnityEvent OnGrabEnd;

    /// <summary>当前是否处于“抓住 Knob”状态</summary>
    public bool IsGrabbing { get; private set; } = false;

    // 内部状态
    bool lastButtonsHeld = false;
    int  baseIndex;               // 抓住那一刻 KnobCore.currentIndex

    // 手腕基准姿势（抓住那一刻）
    Quaternion baseControllerRotation;
    Vector3    baseControllerForward;   // 用来确定扭转的正负号

    void Reset()
    {
        if (!knobCore)
            knobCore = GetComponent<KnobCore>();

        if (knobCore && knobCore.knob)
            knobCenter = knobCore.knob.transform;
        else
            knobCenter = transform;

        if (!knobModeManager)
            knobModeManager = GetComponentInParent<KnobModeManager>();
    }

    void OnEnable()
    {
        SetVisualGrabbing(false);
        IsGrabbing = false;
    }

    void OnDisable()
    {
        SetVisualGrabbing(false);
        IsGrabbing = false;
    }

    void Update()
    {
        if (!knobCore || !rightController) return;

        var ring = GetRing();
        if (ring == null || ring.TickCount <= 0) return;

        if (!knobCenter)
        {
            if (knobCore.knob != null) knobCenter = knobCore.knob;
            else knobCenter = transform;
        }

        // 1) 计算距离
        float dist = Vector3.Distance(rightController.position, knobCenter.position);

        // 2) 读 XR 设备的 B + Trigger
        bool bHeld = false;
        bool triggerHeld = false;

        var rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightDevice.isValid)
        {
            rightDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out bHeld);     // B
            rightDevice.TryGetFeatureValue(CommonUsages.triggerButton, out triggerHeld); // Trigger
        }

        bool bothHeld = bHeld && triggerHeld;
        bool justPressedCombo = bothHeld && !lastButtonsHeld;

        if (!IsGrabbing)
        {
            if (dist <= grabRadius && justPressedCombo)
                BeginGrab(ring);
        }
        else
        {
            if (!bothHeld)
                EndGrab();
        }

        if (IsGrabbing)
            UpdateGrabRotationByIndex(ring);

        lastButtonsHeld = bothHeld;
    }

    // ✅ 从 KnobCore 拿 ITickRing（不依赖 knobCore.ring 字段）
    ITickRing GetRing()
    {
        if (knobCore == null) return null;

        // 如果你在 KnobCore 里加了 public ITickRing ring => Ring; 也可以 return knobCore.ring;
        // 这里做“通用拿法”，直接从 ringBehaviour 解析接口：
        if (knobCore.ringBehaviour == null) return null;
        return knobCore.ringBehaviour as ITickRing;
    }

    void BeginGrab(ITickRing ring)
    {
        IsGrabbing = true;

        // 抓取时自动进入 Knob 模式
        if (knobModeManager != null && !knobModeManager.isKnobMode)
        {
            knobModeManager.EnterKnobMode();
        }

        baseIndex = knobCore.currentIndex;

        baseControllerRotation = rightController.rotation;
        baseControllerForward  = rightController.forward;

        // 用 Ring 自动估一下“每格多少度”
        if (autoDegreesPerStepFromRing && ring != null && ring.TickCount > 1)
        {
            float a0 = ring.GetAngleForIndex(0);
            float a1 = ring.GetAngleForIndex(1);
            degreesPerStep = Mathf.Abs(Mathf.DeltaAngle(a0, a1));
        }

        AlignHandRootYawToControllerRelativeRotation();
        ChooseHandVariantByGrabDirection();
        SetVisualGrabbing(true);

        OnGrabStart?.Invoke();
        Debug.Log($"[KnobGrabByWrist] BeginGrab: index={baseIndex}, deg/step≈{degreesPerStep:F1}°");
    }

    void EndGrab()
    {
        IsGrabbing = false;
        SetVisualGrabbing(false);

        if (knobModeManager != null && knobModeManager.isKnobMode)
        {
            knobModeManager.ExitKnobMode();
        }

        OnGrabEnd?.Invoke();
        Debug.Log("[KnobGrabByWrist] EndGrab");
    }

    void SetVisualGrabbing(bool grabbing)
    {
        if (handVisualRoot != null)
            handVisualRoot.SetActive(grabbing);

        if (controllerVisualToHide != null)
            controllerVisualToHide.SetActive(!grabbing);
    }

    void AlignHandRootYawToControllerRelativeRotation()
    {
        if (!handVisualRoot || !knobCenter || !rightController) return;

        Transform ht = handVisualRoot.transform;

        Quaternion knobRot = knobCenter.rotation;
        Quaternion ctrlRot = rightController.rotation;
        Quaternion relativeRot = Quaternion.Inverse(knobRot) * ctrlRot;

        Vector3 relativeEuler = relativeRot.eulerAngles;
        float yaw = relativeEuler.y;

        Vector3 euler = ht.localEulerAngles;
        euler.y = yaw + handYawOffset;
        ht.localEulerAngles = euler;

        Debug.Log($"[KnobGrabByWrist] AlignHandRootYaw: relEuler={relativeEuler}, finalLocalY={euler.y:F1}", this);
    }

    void ChooseHandVariantByGrabDirection()
    {
        if (!knobCenter || !rightController) return;

        Vector3 rayDir = rightController.forward.normalized;
        Vector3 panelN = knobCenter.forward.normalized;

        float dot = Mathf.Clamp(Vector3.Dot(rayDir, panelN), -1f, 1f);
        float absDot = Mathf.Abs(dot);

        bool usePositive = absDot >= parallelDotThreshold;

        if (positiveHand) positiveHand.SetActive(usePositive);
        if (negativeHand) negativeHand.SetActive(!usePositive);

        Debug.Log(
            $"[KnobGrabByWrist] ChooseHand |dot|={absDot:F2} → " +
            (usePositive ? "Positive(Laser Passing Knob)" : "Negative(Parallel to Panel)"),
            this
        );
    }

    void UpdateGrabRotationByIndex(ITickRing ring)
    {
        float delta = GetRelativeTwistAngle();

        if (Mathf.Abs(delta) < deadZoneDegrees) return;
        if (Mathf.Approximately(degreesPerStep, 0f)) return;
        if (ring == null || ring.TickCount <= 0) return;

        float stepOffset = delta / degreesPerStep;
        int offsetIndex = Mathf.RoundToInt(stepOffset);

        int tickCount = ring.TickCount;
        int targetIndex = Mathf.Clamp(baseIndex + offsetIndex, 0, tickCount - 1);

        if (targetIndex != knobCore.currentIndex)
        {
            knobCore.SnapTo(targetIndex + 1); // SnapTo 参数是 1..N
        }
    }

    float GetRelativeTwistAngle()
    {
        Quaternion currentRot = rightController.rotation;
        Quaternion deltaRot   = currentRot * Quaternion.Inverse(baseControllerRotation);

        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f) angle -= 360f;

        float signDot = Mathf.Sign(Vector3.Dot(axis, baseControllerForward));
        float signedAngle = angle * (-signDot);

        signedAngle = Mathf.Clamp(signedAngle, -maxTwistDegrees, maxTwistDegrees);
        return signedAngle;
    }
}
