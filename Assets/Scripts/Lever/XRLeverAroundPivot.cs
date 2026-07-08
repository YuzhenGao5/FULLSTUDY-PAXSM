using UnityEngine;
using UnityEngine.XR;

public class HoldLever_NoXRI : MonoBehaviour
{
    [Header("Pivot (独立的转轴点)")]
    public Transform pivot;

    [Header("绕 Pivot 的本地轴旋转（X=right, Y=up, Z=forward）")]
    public Vector3 pivotLocalAxis = Vector3.right;

    [Header("角度限制（度）")]
    public float minAngle = -30f;
    public float maxAngle = 30f;

    [Header("接触判定：右手靠近到这个距离内才允许驱动（米）")]
    public float contactRadius = 0.12f;

    [Header("用哪个按钮当“确定键”")]
    public ConfirmButton confirmButton = ConfirmButton.PrimaryButton; // Quest 右手A
    public enum ConfirmButton { PrimaryButton, TriggerButton, SecondaryButton }

    [Header("可选：如果你有右手 Transform，拖进来更稳（没有也能用）")]
    public Transform rightHandTransform;

    // 内部状态
    bool _driving;
    bool _wasPressed;

    Quaternion _leverRot0;
    Vector3 _leverOffset0;
    Vector3 _refDir0;

    void Update()
    {
        if (pivot == null) return;

        // 1) 右手位置
        Vector3 handPos;
        if (rightHandTransform != null)
        {
            handPos = rightHandTransform.position;
        }
        else
        {
            // 用 XRNode 读右手设备位置（Unity 自带 XR）
            if (!TryGetXRNodePosition(XRNode.RightHand, out handPos))
                return;
        }

        // 2) 是否“接触”（用距离近似）
        bool touching = Vector3.Distance(handPos, transform.position) <= contactRadius;

        // 3) 读取“确定键”
        bool pressed = GetConfirmPressed();

        // 4) 按下瞬间：开始驱动（必须正在接触）
        if (pressed && !_wasPressed && touching)
        {
            _driving = true;

            _leverRot0 = transform.rotation;
            _leverOffset0 = transform.position - pivot.position;

            Vector3 axisW = pivot.TransformDirection(pivotLocalAxis.normalized);

            // 以当前杆朝向作为 0 度参考
            Vector3 dir = Vector3.ProjectOnPlane(transform.forward, axisW).normalized;
            if (dir.sqrMagnitude < 1e-6f)
                dir = Vector3.ProjectOnPlane(transform.up, axisW).normalized;

            _refDir0 = dir;
        }

        // 5) 松开：停止
        if (!pressed && _wasPressed)
            _driving = false;

        _wasPressed = pressed;

        // 6) 驱动：只要按住就更新角度（你也可以要求 touching 才更新）
        if (_driving)
        {
            Vector3 axisW = pivot.TransformDirection(pivotLocalAxis.normalized);

            Vector3 v = Vector3.ProjectOnPlane(handPos - pivot.position, axisW);
            if (v.sqrMagnitude < 1e-6f) return;
            v.Normalize();

            float angle = Vector3.SignedAngle(_refDir0, v, axisW);
            angle = Mathf.Clamp(angle, minAngle, maxAngle);

            Quaternion delta = Quaternion.AngleAxis(angle, axisW);

            transform.rotation = delta * _leverRot0;
            transform.position = pivot.position + delta * _leverOffset0;
        }
    }

    bool GetConfirmPressed()
    {
        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!device.isValid) return false;

        bool value = false;
        switch (confirmButton)
        {
            case ConfirmButton.PrimaryButton:
                device.TryGetFeatureValue(CommonUsages.primaryButton, out value); // Quest: A
                break;
            case ConfirmButton.SecondaryButton:
                device.TryGetFeatureValue(CommonUsages.secondaryButton, out value); // Quest: B
                break;
            case ConfirmButton.TriggerButton:
                device.TryGetFeatureValue(CommonUsages.triggerButton, out value); // 扳机
                break;
        }
        return value;
    }

    static bool TryGetXRNodePosition(XRNode node, out Vector3 pos)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.devicePosition, out pos))
            return true;

        pos = default;
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(transform.position, contactRadius);
    }
}
