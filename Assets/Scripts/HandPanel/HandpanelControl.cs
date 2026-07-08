using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class RadialStick4WayButtons_NoPointer : MonoBehaviour
{
    [Header("Input")]
    public InputActionReference stickAction;
    public InputActionReference confirmAction;

    [Header("Drag Buttons Here")]
    public Button upButton;
    public Button rightButton;
    public Button downButton;
    public Button leftButton;

    [Header("Behavior")]
    public float deadzone = 0.25f;
    public bool keepSelectionWhenIdle = true;

    [Header("Colors")]
    public Color normalColor = new Color(1f, 1f, 1f, 0.55f);
    public Color hoverColor = new Color(1f, 1f, 1f, 1f);
    public Color confirmedColor = new Color(1f, 1f, 1f, 1f);

    public enum Dir { None, Up, Right, Down, Left }
    public Dir Current { get; private set; } = Dir.None;

    [Header("Pager Rules")]
    [Tooltip("Set this to the direction that represents PAGER in your 4-way UI.")]
    public Dir pagerDir = Dir.Left;

    [Tooltip("Pager confirmed latch.\n" +
             "Once true, it will NEVER be cleared by confirming other dirs.\n" +
             "Only confirming pager again (or SetPagerLatched(false)) cancels it.")]
    [SerializeField] private bool pagerLatched = false;

    [Tooltip("Non-pager confirmed mode (exclusive).")]
    [SerializeField] private Dir confirmedExclusiveDir = Dir.None;

    [Header("Hard Override")]
    [Tooltip("Disable Button Transition so Unity won't override our colors.")]
    public bool forceDisableButtonTransition = true;

    [Tooltip("Force re-apply colors in LateUpdate every frame.\n" +
             "This will win against any other scripts that modify colors.")]
    public bool alwaysForceApplyInLateUpdate = true;

    [Header("Debug")]
    public bool debugLog = false;

    Image _upImg, _rightImg, _downImg, _leftImg;

    void OnEnable()
    {
        stickAction?.action?.Enable();
        confirmAction?.action?.Enable();

        CacheImages();
        if (forceDisableButtonTransition) DisableTransitions();

        ApplyHighlight();
    }

    void OnDisable()
    {
        stickAction?.action?.Disable();
        confirmAction?.action?.Disable();
    }

    void CacheImages()
    {
        _upImg = GetImg(upButton);
        _rightImg = GetImg(rightButton);
        _downImg = GetImg(downButton);
        _leftImg = GetImg(leftButton);
    }

    Image GetImg(Button b)
    {
        if (!b) return null;
        var img = b.GetComponent<Image>();
        if (!img) img = b.GetComponentInChildren<Image>();
        return img;
    }

    void DisableTransitions()
    {
        DisableBtnTransition(upButton);
        DisableBtnTransition(rightButton);
        DisableBtnTransition(downButton);
        DisableBtnTransition(leftButton);
    }

    void DisableBtnTransition(Button b)
    {
        if (!b) return;
        b.transition = Selectable.Transition.None;
    }

    void Update()
    {
        Vector2 v = stickAction != null ? stickAction.action.ReadValue<Vector2>() : Vector2.zero;

        if (v.magnitude < deadzone)
        {
            if (!keepSelectionWhenIdle && Current != Dir.None)
            {
                Current = Dir.None;
                ApplyHighlight();
            }
        }
        else
        {
            Dir dir = Get4Way(v);
            if (dir != Current)
            {
                Current = dir;
                ApplyHighlight();
            }
        }

        if (confirmAction != null && confirmAction.action.WasPressedThisFrame())
        {
            ConfirmCurrent();
        }
    }

    void LateUpdate()
    {
        if (alwaysForceApplyInLateUpdate)
            ApplyHighlight();
    }

    Dir Get4Way(Vector2 v)
    {
        float ax = Mathf.Abs(v.x);
        float ay = Mathf.Abs(v.y);

        if (ay >= ax)
            return v.y >= 0 ? Dir.Up : Dir.Down;
        else
            return v.x >= 0 ? Dir.Right : Dir.Left;
    }

    void ConfirmCurrent()
    {
        if (Current == Dir.None) return;

        // ✅ 最硬规则：
        // - Pager 只 toggle 自己
        // - Confirm 其它方向永不触碰 pagerLatched
        if (Current == pagerDir)
        {
            pagerLatched = !pagerLatched;

            if (debugLog)
                Debug.Log($"[Radial4Way] PagerLatched -> {pagerLatched}", this);
        }
        else
        {
            confirmedExclusiveDir = Current;

            if (debugLog)
                Debug.Log($"[Radial4Way] ExclusiveConfirmed -> {confirmedExclusiveDir}", this);
        }

        // 保留行为触发
        switch (Current)
        {
            case Dir.Up: upButton?.onClick?.Invoke(); break;
            case Dir.Right: rightButton?.onClick?.Invoke(); break;
            case Dir.Down: downButton?.onClick?.Invoke(); break;
            case Dir.Left: leftButton?.onClick?.Invoke(); break;
        }

        ApplyHighlight();
    }

    void ApplyHighlight()
    {
        ApplyBtn(_upImg, Dir.Up);
        ApplyBtn(_rightImg, Dir.Right);
        ApplyBtn(_downImg, Dir.Down);
        ApplyBtn(_leftImg, Dir.Left);
    }

    void ApplyBtn(Image img, Dir d)
    {
        if (!img) return;

        bool isPagerConfirmed = pagerLatched && (d == pagerDir);
        bool isExclusiveConfirmed = (confirmedExclusiveDir == d);

        bool isConfirmed = isPagerConfirmed || isExclusiveConfirmed;
        bool isHover = (Current == d);

        if (isConfirmed)
            img.color = confirmedColor;
        else if (isHover)
            img.color = hoverColor;
        else
            img.color = normalColor;
    }

    // =========================
    // Public API
    // =========================
    public void SetPagerLatched(bool latched)
    {
        pagerLatched = latched;
        ApplyHighlight();
    }

    public bool IsPagerLatched => pagerLatched;

    public void SetExclusiveConfirmed(Dir d)
    {
        if (d == pagerDir) return;
        confirmedExclusiveDir = d;
        ApplyHighlight();
    }

    public Dir GetExclusiveConfirmed() => confirmedExclusiveDir;

    public void ClearExclusiveConfirmed()
    {
        confirmedExclusiveDir = Dir.None;
        ApplyHighlight();
    }
}
