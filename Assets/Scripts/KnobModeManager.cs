// 文件名：KnobModeManager.cs
using UnityEngine;
using UnityEngine.InputSystem;          // 新输入系统
using UnityEngine.XR;                  // XRNode / InputDevices
using UnityEngine.XR.Interaction.Toolkit;
using TMPro;                           // 用于 TextMeshPro 文本

public class KnobModeManager : MonoBehaviour
{
    public enum ControlMode
    {
        ToggleAndAxis,   // ✅ 原来的：按键切换 + 摇杆控制（Step / Pager）
        GestureOnly      // ✅ 新增：只允许外部调用 Enter/Exit（例如 KnobGrabByWrist）
    }

    [Header("控制模式")]
    public ControlMode controlMode = ControlMode.ToggleAndAxis;

    [Header("核心")]
    [Tooltip("拖 Knob 上挂着的 KnobCore")]
    public KnobCore knobCore;

    [Header("XR 输入（原模式才需要）")]
    [Tooltip("进入/退出 Knob 模式（例如 RightHand/SecondaryButton 或 Select）")]
    public InputActionReference toggleKnobModeAction;   // Button

    [Tooltip("用于拨动旋钮和翻页的摇杆 Axis（例如 RightHand/Move / Primary2DAxis）")]
    public InputActionReference knobAxisAction;         // Vector2

    [Header("Knob 模式下要禁用的东西")]
    public Behaviour moveBehaviour;
    public Behaviour turnBehaviour;

    [Header("手柄 / 手 模型切换")]
    public GameObject rightControllerModel;
    public GameObject handOnKnob;

    [Header("左右刻度步进阈值（原模式才需要）")]
    public float stepThresholdX  = 0.2f;
    public float resetThresholdX = 0.1f;

    [Header("上下翻页阈值（原模式才需要）")]
    public float stepThresholdY  = 0.4f;
    public float resetThresholdY = 0.2f;

    [Header("题目翻页（PagerController）（原模式才需要）")]
    public PagerController pager;

    [Header("调试状态")]
    public bool isKnobMode = false;

    bool axisReadyX = true;
    bool axisReadyY = true;

    bool loggedAxisDisabled = false;

    // ================== 手柄震动相关（原脚本保留） ==================
    [Header("手柄震动反馈（从左到右强度递增）（可选）")]
    public GameObject rightHapticObject;

    XRBaseController rightHapticController;

    [Range(0f, 1f)] public float hapticMinAmplitude = 0.1f;
    [Range(0f, 1f)] public float hapticMaxAmplitude = 0.6f;

    public float hapticDuration = 0.05f;
    public int hapticSlotsCount = 5;

    // ================== Knob 模式提示文字 ==================
    [Header("Knob 模式提示文字（可选）")]
    public TMP_Text knobModeHintText;
    public string knobModeEnterText = "KNOB MODE ENTER";

    string _originalHintText;
    bool   _hasOriginalHint = false;

    void Awake()
    {
        if (knobModeHintText != null)
        {
            _originalHintText = knobModeHintText.text;
            _hasOriginalHint  = true;
        }

        if (rightHapticObject != null)
        {
            rightHapticController = rightHapticObject.GetComponent<XRBaseController>();
        }
    }

    void Reset()
    {
        if (!knobCore) knobCore = FindObjectOfType<KnobCore>();
    }

    void OnEnable()
    {
        ApplyInputEnableState();
    }

    void OnDisable()
    {
        // 统一关掉，避免 Action 残留占用
        if (toggleKnobModeAction) toggleKnobModeAction.action.Disable();
        if (knobAxisAction)       knobAxisAction.action.Disable();
    }

    void OnValidate()
    {
        // 在编辑器切换模式时，尽量让 Action 状态跟着走（Play 时更明显）
        if (Application.isPlaying)
            ApplyInputEnableState();
    }

    void ApplyInputEnableState()
    {
        if (controlMode == ControlMode.ToggleAndAxis)
        {
            if (toggleKnobModeAction) toggleKnobModeAction.action.Enable();
            if (knobAxisAction)       knobAxisAction.action.Enable();
        }
        else // GestureOnly
        {
            if (toggleKnobModeAction) toggleKnobModeAction.action.Disable();
            if (knobAxisAction)       knobAxisAction.action.Disable();
        }
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (!knobCore) return;

        // ✅ GestureOnly：完全不监听按键/摇杆
        if (controlMode == ControlMode.GestureOnly)
            return;

        // =============== 原模式：按键切换 KnobMode ===============
        if (toggleKnobModeAction && toggleKnobModeAction.action.triggered)
        {
            if (isKnobMode) ExitKnobMode();
            else            EnterKnobMode();
        }

        if (!isKnobMode) return;

        // =============== 原模式：摇杆输入（左右 Step / 上下 Pager） ===============
        HandleKnobAxis();
    }

    // ================== 摇杆：左右刻度 + 上下翻页（原逻辑保留） ==================
    void HandleKnobAxis()
    {
        float x = 0f;
        float y = 0f;

        if (knobAxisAction != null && knobAxisAction.action != null)
        {
            if (!knobAxisAction.action.enabled)
            {
                if (!loggedAxisDisabled)
                {
                    Debug.LogWarning("[KnobModeManager] knobAxisAction was disabled, re-enabling.");
                    loggedAxisDisabled = true;
                }
                knobAxisAction.action.Enable();
            }
            else loggedAxisDisabled = false;

            Vector2 stick = knobAxisAction.action.ReadValue<Vector2>();
            x = stick.x;
            y = stick.y;
        }

#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed) x += 1f;
            if (kb.wKey.isPressed) y += 1f;
            if (kb.sKey.isPressed) y -= 1f;
        }
#endif

        // --- X 轴：左右刻度 ---
        if (axisReadyX)
        {
            if (x > stepThresholdX)
            {
                knobCore.Step(+1);
                axisReadyX = false;
                PlayHapticForCurrentSlot();
            }
            else if (x < -stepThresholdX)
            {
                knobCore.Step(-1);
                axisReadyX = false;
                PlayHapticForCurrentSlot();
            }
        }
        else if (Mathf.Abs(x) < resetThresholdX)
            axisReadyX = true;

        // --- Y 轴：翻页 ---
        if (pager == null) return;

        if (axisReadyY)
        {
            if (y > stepThresholdY)
            {
                pager.StepPage(-1);
                axisReadyY = false;
            }
            else if (y < -stepThresholdY)
            {
                pager.StepPage(+1);
                axisReadyY = false;
            }
        }
        else if (Mathf.Abs(y) < resetThresholdY)
            axisReadyY = true;
    }

    // ================== 手柄震动（原逻辑保留） ==================
    void PlayHapticForCurrentSlot()
    {
        if (knobCore == null) return;

        int total = Mathf.Max(1, hapticSlotsCount);
        int slot  = Mathf.Clamp(knobCore.CurrentSlot, 1, total);

        float t = (total == 1) ? 1f : (slot - 1) / (float)(total - 1);
        float amplitude = Mathf.Lerp(hapticMinAmplitude, hapticMaxAmplitude, t);
        amplitude = Mathf.Clamp01(amplitude);

        PlayHapticImpulse(amplitude, hapticDuration);
    }

    void PlayHapticImpulse(float amplitude, float duration)
    {
        if (rightHapticController != null)
        {
            rightHapticController.SendHapticImpulse(amplitude, duration);
            return;
        }

        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (device.isValid)
            device.SendHapticImpulse(0u, amplitude, duration);
    }

    // ================== Knob 模式 开 / 关（给按键模式和 Gesture 调用都用） ==================
    public void EnterKnobMode()
    {
        if (isKnobMode) return;

        isKnobMode = true;
        axisReadyX = axisReadyY = true;

        if (knobCore) knobCore.CalibrateFromCurrentRotation();

        if (moveBehaviour) moveBehaviour.enabled = false;
        if (turnBehaviour) turnBehaviour.enabled = false;

        if (rightControllerModel) rightControllerModel.SetActive(false);
        if (handOnKnob)           handOnKnob.SetActive(true);

        if (knobModeHintText != null)
            knobModeHintText.text = knobModeEnterText;
    }

    public void ExitKnobMode()
    {
        if (!isKnobMode) return;

        isKnobMode = false;
        axisReadyX = axisReadyY = true;

        if (moveBehaviour) moveBehaviour.enabled = true;
        if (turnBehaviour) turnBehaviour.enabled = true;

        if (rightControllerModel) rightControllerModel.SetActive(true);
        if (handOnKnob)           handOnKnob.SetActive(false);

        if (knobModeHintText != null && _hasOriginalHint)
            knobModeHintText.text = _originalHintText;
    }
}
