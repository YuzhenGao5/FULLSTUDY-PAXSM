using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Press X on LEFT controller to toggle a left-hand panel.
/// - X on Quest = CommonUsages.primaryButton (LeftHand)
/// - No InputActionReference needed.
/// 
/// Extra:
/// - When panel opens, disable left-stick locomotion components.
/// </summary>
public class LeftX_TogglePanel : MonoBehaviour
{
    [Header("Panel Root")]
    [Tooltip("Drag your left-hand panel root GameObject here.")]
    public GameObject leftPanelRoot;

    [Header("Optional")]
    [Tooltip("If true, panel will be hidden on start.")]
    public bool hideOnStart = true;

    [Header("Disable Movement While Panel Open (RECOMMENDED)")]
    [Tooltip(
        "Drag ANY movement-related components here, e.g.:\n" +
        "- ActionBasedContinuousMoveProvider\n" +
        "- ContinuousMoveProvider\n" +
        "- Your own LeftStickMove script\n" +
        "- Or even CharacterController driver\n" +
        "Panel OPEN -> these disabled\n" +
        "Panel CLOSE -> these enabled"
    )]
    public Behaviour[] disableWhilePanelOpen;

    [Header("Debug")]
    public bool debugLog = false;

    InputDevice _leftDevice;
    bool _lastXPressed = false;

    void Start()
    {
        TryGetLeftDevice();

        if (leftPanelRoot)
            leftPanelRoot.SetActive(!hideOnStart);

        // ✅ 同步一次移动组件状态
        ApplyMovementBlock(leftPanelRoot && leftPanelRoot.activeSelf);

        if (debugLog)
            Debug.Log("[LeftX_TogglePanel] Started. Panel=" + (leftPanelRoot ? leftPanelRoot.name : "null"), this);
    }

    void Update()
    {
        if (!_leftDevice.isValid)
            TryGetLeftDevice();

        if (!_leftDevice.isValid || !leftPanelRoot)
            return;

        bool xPressed = false;
        _leftDevice.TryGetFeatureValue(CommonUsages.primaryButton, out xPressed);

        // rising edge = a single press
        if (xPressed && !_lastXPressed)
        {
            bool newState = !leftPanelRoot.activeSelf;
            leftPanelRoot.SetActive(newState);

            // ✅ 面板开关 -> 同步禁用/恢复移动
            ApplyMovementBlock(newState);

            if (debugLog)
                Debug.Log("[LeftX_TogglePanel] X pressed -> Panel " + (newState ? "OPEN" : "CLOSE"), this);
        }

        _lastXPressed = xPressed;
    }

    void ApplyMovementBlock(bool panelOpen)
    {
        if (disableWhilePanelOpen == null) return;

        for (int i = 0; i < disableWhilePanelOpen.Length; i++)
        {
            var comp = disableWhilePanelOpen[i];
            if (!comp) continue;

            comp.enabled = !panelOpen;
        }

        if (debugLog)
            Debug.Log($"[LeftX_TogglePanel] Movement blocked = {panelOpen}", this);
    }

    void TryGetLeftDevice()
    {
        _leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
    }
}
