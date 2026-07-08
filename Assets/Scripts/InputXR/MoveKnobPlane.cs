using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;      // 直接读 XRNode + CommonUsages

/// <summary>
/// 挂在 DockRig 上：
/// - 长按右手 A 键（primaryButton）
/// - 把 DockRig 移动到右手控制器前方一点的位置，并稍微倾斜
/// 不依赖 InputAction，不用在 Inspector 里拖任何 Action。
/// </summary>
public class DockRigCalibrateByA_XRDevice : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("右手控制器 Transform（激光发射的位置）")]
    public Transform rightHand;

    [Header("长按设置")]
    [Tooltip("按住多久算一次触发（秒）")]
    public float holdDuration = 0.6f;

    [Header("相对控制器的位置偏移")]
    [Tooltip("沿控制器 forward 的距离（控制板离手多远）")]
    public float forwardOffset = 0.22f;

    [Tooltip("沿控制器 -up 的距离（控制板比手稍微低一点）")]
    public float downOffset = 0.05f;

    [Header("控制板倾斜角度")]
    [Tooltip("围绕自身 X 轴向上倾斜的角度（20~30° 比较舒服）")]
    public float tiltAngle = 25f;

    // XR 设备引用
    InputDevice rightDevice;
    bool hasDevice = false;

    // 长按状态
    float holdTimer = 0f;
    bool firedThisHold = false;

    void OnEnable()
    {
        TryInitDevice();
    }

    void Update()
    {
        if (rightHand == null) return;

        if (!hasDevice || !rightDevice.isValid)
            TryInitDevice();

        if (!hasDevice) return;

        // 读取 A 键（primaryButton）
        if (!rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool pressed))
            return;

        if (pressed)
        {
            holdTimer += Time.deltaTime;

            if (!firedThisHold && holdTimer >= holdDuration)
            {
                firedThisHold = true;
                PlaceDockAtHand();
            }
        }
        else
        {
            holdTimer = 0f;
            firedThisHold = false;
        }
    }

    // 找右手设备
    void TryInitDevice()
    {
        var list = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, list);
        if (list.Count > 0)
        {
            rightDevice = list[0];
            hasDevice = true;
            Debug.Log("[DockRigCalibrateByA_XRDevice] Right-hand device found.");
        }
        else
        {
            hasDevice = false;
        }
    }

    // 把 DockRig 摆到手前方
    void PlaceDockAtHand()
    {
        // 1) 位置：手前 + 稍微低一点
        Vector3 pos =
            rightHand.position +
            rightHand.forward * forwardOffset -
            rightHand.up      * downOffset;

        transform.position = pos;

        // 2) 朝向：基于手的 forward（水平化），再加一个向上的倾斜
        Vector3 flatForward = Vector3.ProjectOnPlane(rightHand.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = rightHand.forward;

        Quaternion baseRot = Quaternion.LookRotation(flatForward, Vector3.up);
        Quaternion tilt    = Quaternion.Euler(tiltAngle, 0f, 0f); // 负号=朝自己抬起

        transform.rotation = baseRot * tilt;

        Debug.Log("[DockRigCalibrateByA_XRDevice] DockRig repositioned by A long-press.");
    }
}
