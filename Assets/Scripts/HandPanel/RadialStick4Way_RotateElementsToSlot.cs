using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class RadialStick4Way_DynamicSlots_Stable_SelectState_Hold : MonoBehaviour
{
    [Header("Input")]
    public InputActionReference stickAction;
    public InputActionReference confirmAction;

    [Header("Rotate Elements")]
    public RectTransform elementsRoot;

    [Header("Sector Images (BASE ORDER)")]
    [Tooltip("Fixed identity order (NOT direction):\n" +
             "0=Reading, 1=Answer, 2=Submit, 3=Pager (example).")]
    public Image[] sectorImages = new Image[4];

    [Header("Angles (BASE LAYOUT)")]
    public float[] sectorAngles = new float[4] { 90f, 0f, 270f, 180f };

    [Header("Slot Angles (DISPLAY POSITIONS)")]
    public float[] slotAngles = new float[4] { 90f, 0f, 270f, 180f };

    [Header("Stick Stability (IMPORTANT)")]
    public float enterThreshold = 0.55f;
    public float exitThreshold = 0.35f;
    public float switchCooldown = 0.18f;
    public bool requireReleaseToReselect = true;

    [Header("Auto rotate highlighted to Slot1")]
    public bool autoRotateToSlot1 = true;
    [Range(0, 3)]
    public int slot1Index = 0;

    [Header("Rotation Motion")]
    public bool smooth = true;
    public float rotSpeed = 12f;
    public bool snap = false;

    [Header("Visual States Colors")]
    public Color normalColor    = new Color(1f, 1f, 1f, 0.35f);
    public Color highlightColor = new Color(1f, 1f, 1f, 0.85f);
    public Color selectedColor  = new Color(1f, 1f, 1f, 1f);

    // ---------------- Confirm Hold ----------------

    [Header("Confirm Hold")]
    [Min(0.05f)]
    public float confirmHoldDuration = 0.6f;

    [Tooltip("If true, you must release confirm before next confirm.")]
    public bool requireReleaseToReconfirm = true;

    [Header("Hold Gradient")]
    public bool useHoldGradient = true;
    public bool holdEaseInOut = true;

    [Header("Gradient Colors - CONFIRM (Select)")]
    public Color confirmHoldStartColor = new Color(1f, 0.15f, 0.15f, 1f);
    public Color confirmHoldEndColor   = new Color(0.15f, 1f, 0.15f, 1f);

    [Header("Gradient Colors - CANCEL (Unselect)")]
    public Color cancelHoldStartColor  = new Color(0.15f, 1f, 0.15f, 1f);
    public Color cancelHoldEndColor    = new Color(1f, 0.15f, 0.15f, 1f);

    [Header("Optional Hold Progress UI")]
    public Image holdProgressImage;
    public float holdIdleAlpha = 0.15f;
    public float holdActiveAlpha = 0.9f;

    [Header("Confirm Feedback (optional flash)")]
    public bool useConfirmFlash = false;
    public float confirmFlashDuration = 0.10f;
    public Color confirmFlashColor = new Color(1f, 1f, 1f, 1f);

    // ---------------- TOGGLE 逻辑 ----------------

    [Header("Toggle Confirm State")]
    public bool toggleOffIfConfirmSameSelected = true;

    // ✅ Pager 例外规则
    [Header("Pager Exception")]
    [Tooltip("Which index is Pager in BASE ORDER? Usually 3.")]
    public int pagerIndex = 3;

    /// <summary>Pager 的确认态单独存</summary>
    public bool PagerSelected { get; private set; } = false;

    // ---------------- Events ----------------

    [Header("Confirm Events (EVENT ONLY)")]
    public UnityEvent<int>   onConfirmedIndex;
    public UnityEvent<Image> onConfirmedImage;
    public System.Action<int, Image> Confirmed;

    [Header("Selection Changed Events")]
    public UnityEvent<int> onSelectedChanged;
    public System.Action<int> SelectedChanged;

    [Header("Pager Selected Events")]
    public UnityEvent<bool> onPagerSelectedChanged;

    // ✅ 新增：任何按钮在任何情况下“取消高亮(选中)”时抛出
    [Header("Off Events (取消选中/高亮时抛出)")]
    public UnityEvent<int> onDeselectedIndex;   // idx
    public System.Action<int> Deselected;       // code

    // ---------------- runtime ----------------

    public int CurrentSectorIndex  { get; private set; } = -1;
    public int CurrentSlotIndex    { get; private set; } = -1;

    /// <summary>只代表“非 Pager 的互斥确认”</summary>
    public int SelectedSectorIndex { get; private set; } = -1;

    Quaternion _targetRot;
    float _lastSwitchTime = -999f;
    bool _armed = true;

    Coroutine _flashCo;

    float _holdT = 0f;
    bool _holdFired = false;
    bool _confirmArmed = true;

    int  _confirmCachedIndex = -1;
    bool _isHolding          = false;

    bool _willToggleOff = false;

    void Awake()
    {
        if (!elementsRoot)
            Debug.LogWarning("[RadialStick4Way_SelectState_Hold] elementsRoot not assigned.", this);

        _targetRot = elementsRoot ? elementsRoot.localRotation : Quaternion.identity;
    }

    void OnEnable()
    {
        stickAction?.action?.Enable();
        confirmAction?.action?.Enable();
        RefreshVisuals();
        ResetHoldVisual(true);
    }

    void OnDisable()
    {
        stickAction?.action?.Disable();
        confirmAction?.action?.Disable();
    }

    void Update()
    {
        TickRotation();
        TickStickHighlight();
        TickConfirmHold();
    }

    // =========================
    // 1) Stick -> Highlight
    // =========================

    void TickStickHighlight()
    {
        Vector2 v = stickAction != null ? stickAction.action.ReadValue<Vector2>() : Vector2.zero;
        float mag = v.magnitude;

        if (mag < exitThreshold)
        {
            CurrentSlotIndex = -1;
            if (requireReleaseToReselect) _armed = true;
            return;
        }

        if (mag < enterThreshold) return;
        if (requireReleaseToReselect && !_armed) return;
        if (Time.time - _lastSwitchTime < switchCooldown) return;

        int slotIdx = GetSlotByStick(v);
        if (slotIdx == CurrentSlotIndex) return;

        CurrentSlotIndex = slotIdx;

        int sectorIdx = FindSectorCurrentlyInSlot(slotIdx);

        if (sectorIdx != CurrentSectorIndex)
        {
            CurrentSectorIndex = sectorIdx;
            RefreshVisuals();

            if (autoRotateToSlot1 && CurrentSectorIndex >= 0)
            {
                RotateSectorToSlot(CurrentSectorIndex, slot1Index);
            }
        }

        _lastSwitchTime = Time.time;
        if (requireReleaseToReselect) _armed = false;
    }

    // =========================
    // 2) Confirm Hold -> Gradient + Toggle
    // =========================

    void TickConfirmHold()
    {
        if (confirmAction == null || confirmAction.action == null) return;

        bool pressed = confirmAction.action.IsPressed();

        if (!pressed)
        {
            if (_holdT > 0f || _isHolding)
            {
                ResetHold();
                RefreshVisuals();
            }

            if (requireReleaseToReconfirm) _confirmArmed = true;
            SetHoldAlpha(holdIdleAlpha);
            return;
        }

        if (requireReleaseToReconfirm && !_confirmArmed) return;

        SetHoldAlpha(holdActiveAlpha);

        if (_holdT <= 0f)
        {
            _confirmCachedIndex = CurrentSectorIndex;
            _holdFired          = false;
            _isHolding          = true;

            // 判断“本次是否是取消”
            if (_confirmCachedIndex == pagerIndex)
            {
                _willToggleOff = toggleOffIfConfirmSameSelected && PagerSelected;
            }
            else
            {
                _willToggleOff =
                    toggleOffIfConfirmSameSelected &&
                    _confirmCachedIndex >= 0 &&
                    _confirmCachedIndex == SelectedSectorIndex;
            }

            if (useHoldGradient &&
                _confirmCachedIndex >= 0 &&
                _confirmCachedIndex < sectorImages.Length)
            {
                var img0 = sectorImages[_confirmCachedIndex];
                if (img0)
                    img0.color = _willToggleOff ? cancelHoldStartColor : confirmHoldStartColor;
            }
        }

        if (_confirmCachedIndex < 0 || _confirmCachedIndex >= sectorImages.Length)
            return;

        _holdT += Time.deltaTime;

        if (useHoldGradient)
        {
            Image img = sectorImages[_confirmCachedIndex];
            if (img)
            {
                float p = Mathf.Clamp01(_holdT / Mathf.Max(0.001f, confirmHoldDuration));
                if (holdEaseInOut) p = SmoothStep(p);

                Color a = _willToggleOff ? cancelHoldStartColor : confirmHoldStartColor;
                Color b = _willToggleOff ? cancelHoldEndColor   : confirmHoldEndColor;

                img.color = Color.Lerp(a, b, p);
            }
        }

        UpdateHoldProgress();

        if (!_holdFired && _holdT >= confirmHoldDuration)
        {
            _holdFired    = true;
            _confirmArmed = false;

            int idx = _confirmCachedIndex;
            ConfirmHighlightAsSelectedOrToggle(idx);
        }
    }

    void ConfirmHighlightAsSelectedOrToggle(int idx)
    {
        if (idx < 0 || idx >= sectorImages.Length) return;
        Image img = sectorImages[idx];
        if (!img) return;

        // ========== Pager 与其它三者分流 ==========
        if (idx == pagerIndex)
        {
            bool oldPager = PagerSelected;
            PagerSelected = !PagerSelected;
            onPagerSelectedChanged?.Invoke(PagerSelected);

            // ✅ Pager 从 true -> false 视为“取消高亮”，抛关闭事件
            if (oldPager && !PagerSelected)
            {
                onDeselectedIndex?.Invoke(pagerIndex);
                Deselected?.Invoke(pagerIndex);
                Debug.Log($"[Radial] Pager OFF → onDeselectedIndex({pagerIndex})", this);
            }
        }
        else
        {
            int oldSelected = SelectedSectorIndex;

            if (toggleOffIfConfirmSameSelected && SelectedSectorIndex == idx)
            {
                // ✅ 同一个再按一遍：取消这个按钮
                SelectedSectorIndex = -1;

                onDeselectedIndex?.Invoke(idx);
                Deselected?.Invoke(idx);
                Debug.Log($"[Radial] Sector {idx} TOGGLE OFF → onDeselectedIndex({idx})", this);
            }
            else
            {
                // ✅ 切到新的按钮：旧的那个算“被关闭”
                if (oldSelected >= 0 && oldSelected != idx)
                {
                    onDeselectedIndex?.Invoke(oldSelected);
                    Deselected?.Invoke(oldSelected);
                    Debug.Log($"[Radial] Sector {oldSelected} REPLACED → onDeselectedIndex({oldSelected})", this);
                }

                SelectedSectorIndex = idx;
            }

            onSelectedChanged?.Invoke(SelectedSectorIndex);
            SelectedChanged?.Invoke(SelectedSectorIndex);
        }

        // 只发 confirm 事件（无论是 on/off）
        onConfirmedIndex?.Invoke(idx);
        onConfirmedImage?.Invoke(img);
        Confirmed?.Invoke(idx, img);

        RefreshVisuals();

        if (useConfirmFlash)
        {
            if (_flashCo != null) StopCoroutine(_flashCo);
            _flashCo = StartCoroutine(FlashOnce(img));
        }

        ResetHold();
    }

    IEnumerator FlashOnce(Image img)
    {
        if (!img) yield break;

        img.color = confirmFlashColor;
        yield return new WaitForSeconds(confirmFlashDuration);
        RefreshVisuals();
    }

    // =========================
    // Rotation
    // =========================

    void TickRotation()
    {
        if (!elementsRoot) return;

        if (snap || !smooth)
        {
            elementsRoot.localRotation = _targetRot;
            return;
        }

        elementsRoot.localRotation = Quaternion.Slerp(
            elementsRoot.localRotation,
            _targetRot,
            rotSpeed * Time.deltaTime
        );
    }

    void RotateSectorToSlot(int sectorIdx, int slotIdx)
    {
        if (!elementsRoot) return;
        if (sectorAngles == null || sectorAngles.Length < 4) return;
        if (slotAngles   == null || slotAngles.Length   < 4) return;

        float selectedBase = sectorAngles[sectorIdx];
        float targetSlot   = slotAngles[slotIdx];

        float delta = targetSlot - selectedBase;
        _targetRot = Quaternion.Euler(0, 0, delta);
    }

    // =========================
    // Dynamic slot mapping
    // =========================

    int GetSlotByStick(Vector2 v)
    {
        float ax = Mathf.Abs(v.x);
        float ay = Mathf.Abs(v.y);

        if (ay >= ax)
            return v.y >= 0 ? 0 : 2;
        else
            return v.x >= 0 ? 1 : 3;
    }

    int FindSectorCurrentlyInSlot(int slotIdx)
    {
        if (!elementsRoot) return -1;
        if (sectorAngles == null || sectorAngles.Length < 4) return -1;

        float rotZ    = elementsRoot.localEulerAngles.z;
        float desired = slotAngles[slotIdx];

        int   best     = 0;
        float bestDiff = 999f;

        for (int i = 0; i < 4; i++)
        {
            float displayed = NormalizeAngle(sectorAngles[i] + rotZ);
            float diff      = Mathf.Abs(Mathf.DeltaAngle(displayed, desired));

            if (diff < bestDiff)
            {
                bestDiff = diff;
                best     = i;
            }
        }

        return best;
    }

    float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a < 0) a += 360f;
        return a;
    }

    // =========================
    // Visuals
    // =========================

    void RefreshVisuals()
    {
        for (int i = 0; i < sectorImages.Length; i++)
        {
            Image img = sectorImages[i];
            if (!img) continue;

            // hold 中不要抢颜色
            if (_isHolding && i == _confirmCachedIndex && useHoldGradient)
                continue;

            bool isPagerSelected    = (i == pagerIndex) && PagerSelected;
            bool isExclusiveSelected = (i == SelectedSectorIndex);

            if (isPagerSelected || isExclusiveSelected)
                img.color = selectedColor;
            else if (i == CurrentSectorIndex)
                img.color = highlightColor;
            else
                img.color = normalColor;
        }
    }

    // =========================
    // Hold UI helpers
    // =========================

    void UpdateHoldProgress()
    {
        if (!holdProgressImage) return;
        holdProgressImage.fillAmount = Mathf.Clamp01(_holdT / Mathf.Max(0.001f, confirmHoldDuration));
    }

    void ResetHold()
    {
        _holdT            = 0f;
        _holdFired        = false;
        _confirmCachedIndex = -1;
        _isHolding        = false;
        _willToggleOff    = false;
        ResetHoldVisual(false);
    }

    void ResetHoldVisual(bool forceZero)
    {
        if (!holdProgressImage) return;
        if (forceZero) holdProgressImage.fillAmount = 0f;
        SetHoldAlpha(holdIdleAlpha);
    }

    void SetHoldAlpha(float a)
    {
        if (!holdProgressImage) return;
        Color c = holdProgressImage.color;
        c.a     = a;
        holdProgressImage.color = c;
    }

    static float SmoothStep(float t)
    {
        return t * t * (3f - 2f * t);
    }

    // =========================
    // Public helper
    // =========================

    /// <summary>
    /// 只改“非 Pager 的互斥确认”
    /// </summary>
    public void ForceSelect(int sectorIndex)
    {
        if (sectorIndex < -1 || sectorIndex >= sectorImages.Length) return;
        if (sectorIndex == pagerIndex) return; // 避免误用

        int oldSelected = SelectedSectorIndex;

        // 如果旧的有选中而现在要改掉/清除 → 抛关闭事件
        if (oldSelected >= 0 && oldSelected != sectorIndex)
        {
            onDeselectedIndex?.Invoke(oldSelected);
            Deselected?.Invoke(oldSelected);
            Debug.Log($"[Radial] ForceSelect: Sector {oldSelected} OFF → onDeselectedIndex({oldSelected})", this);
        }
        else if (sectorIndex == -1 && oldSelected >= 0)
        {
            onDeselectedIndex?.Invoke(oldSelected);
            Deselected?.Invoke(oldSelected);
            Debug.Log($"[Radial] ForceSelect: Sector {oldSelected} CLEARED → onDeselectedIndex({oldSelected})", this);
        }

        SelectedSectorIndex = sectorIndex;
        RefreshVisuals();

        onSelectedChanged?.Invoke(SelectedSectorIndex);
        SelectedChanged?.Invoke(SelectedSectorIndex);
    }

    /// <summary>
    /// 强制设置 Pager 选中/取消
    /// </summary>
    public void ForcePager(bool selected)
    {
        bool oldPager = PagerSelected;
        PagerSelected = selected;
        onPagerSelectedChanged?.Invoke(PagerSelected);

        // 如果从 true → false，同样视为“取消高亮”
        if (oldPager && !PagerSelected)
        {
            onDeselectedIndex?.Invoke(pagerIndex);
            Deselected?.Invoke(pagerIndex);
            Debug.Log($"[Radial] ForcePager: Pager OFF → onDeselectedIndex({pagerIndex})", this);
        }

        RefreshVisuals();
    }
}
