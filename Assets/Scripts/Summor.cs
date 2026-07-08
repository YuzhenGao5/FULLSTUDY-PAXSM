using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

public class QEContainerSummoner : MonoBehaviour
{
    [Header("Target")]
    public GameObject qeContainer;

    [Header("Camera / Placement")]
    public Transform cameraTransform;         // XR Origin 下的 Main Camera；为空则自动找
    public float forwardDistance = 1.0f;
    public float verticalOffset = -0.05f;
    public bool levelToHorizon = true;

    [Header("Input (Input System)")]
    public InputActionReference confirmAction; // 真实设备用：如 <XRController>{RightHand}/triggerButton

    [Header("Simulator Fallback (Editor/PC)")]
    public bool enableSimulatorFallback = true;
    public KeyCode simulatorKey = KeyCode.Space;     // 空格
    public bool mouseLeftAsConfirm = true;           // 鼠标左键当确认

    [Header("Haptics (optional)")]
    public XRBaseController rightControllerForHaptics;
    public float hapticAmplitude = 0.4f;
    public float hapticDuration  = 0.05f;

    void Reset()
    {
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
    }

    void OnEnable()
    {
        confirmAction?.action?.Enable();
    }

    void OnDisable()
    {
        confirmAction?.action?.Disable();
    }

    void Update()
    {
        bool pressed = false;

        // 1) InputAction（真机 or 你额外加的绑定）
        if (confirmAction && confirmAction.action.WasPressedThisFrame())
            pressed = true;

        // 2) 模拟器兜底：键盘 / 鼠标
        if (!pressed && enableSimulatorFallback)
        {
            if (Input.GetKeyDown(simulatorKey)) pressed = true;
            if (!pressed && mouseLeftAsConfirm && Mouse.current != null)
                pressed = Mouse.current.leftButton.wasPressedThisFrame;
        }

        if (pressed) ShowInFront();
    }

    public void ShowInFront()
    {
        if (!qeContainer) { Debug.LogWarning("[QEContainerSummoner] qeContainer 未赋值"); return; }

        if (!cameraTransform)
        {
            if (Camera.main) cameraTransform = Camera.main.transform;
            else { Debug.LogWarning("[QEContainerSummoner] cameraTransform 未赋值且找不到 Camera.main"); return; }
        }

        Vector3 camPos = cameraTransform.position;
        Vector3 fwd = cameraTransform.forward;
        Vector3 up  = cameraTransform.up;

        if (levelToHorizon)
        {
            fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = cameraTransform.forward;
            fwd.Normalize();
            up = Vector3.up;
        }

        Vector3 targetPos = camPos + fwd * forwardDistance + up * verticalOffset;

        Quaternion targetRot = Quaternion.LookRotation(targetPos - camPos);
        if (levelToHorizon)
        {
            Vector3 faceDir = (camPos - targetPos); faceDir.y = 0f;
            if (faceDir.sqrMagnitude > 1e-4f) targetRot = Quaternion.LookRotation(faceDir.normalized, Vector3.up);
        }

        qeContainer.transform.SetPositionAndRotation(targetPos, targetRot);
        if (!qeContainer.activeSelf) qeContainer.SetActive(true);

        if (rightControllerForHaptics != null)
            rightControllerForHaptics.SendHapticImpulse(Mathf.Clamp01(hapticAmplitude), Mathf.Max(0f, hapticDuration));
    }
}
