// 文件名：OptionHighLight.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class OptionHighLight : MonoBehaviour
{
    [Header("关联")]
    public KnobCore        knobCore;
    public KnobModeManager knobModeManager;

    [Header("✅ Submit Button Events (Answer stage only)")]
    public StageAwarePagerSubmitButton submitButton;

    [Header("Mode Gate")]
    [Tooltip("✅ 默认 false：卡片/slider 模式也能渐变与确认；若你想强制只有 knobMode 才高亮就勾上")]
    public bool requireKnobMode = false;

    [Header("颜色设置")]
    public Color highlightColor = new Color(1f, 0.1f, 0.7f, 1f);
    public Color confirmedColor = new Color(0.2f, 1f, 0.6f, 1f);

    [Header("点击行为")]
    public bool enableClickOnConfirm = true;

    Image[]  bgImages;
    Color[]  originalColors;
    Button[] optionButtons;

    int lastIndex      = -1;
    int confirmedIndex = -1;

    bool  isHolding    = false;
    float holdProgress = 0f;   // 0..1

    bool _boundSubmitEvents = false;

    // ✅ NEW：外部可控：是否启用“待选高亮”（旋钮指向高亮 + 长按渐变）
    bool _pendingHighlightEnabled = true;

    void OnEnable()  => BindAnswerStageEventsIfNeeded();
    void OnDisable() => UnbindAnswerStageEvents();

    void BindAnswerStageEventsIfNeeded()
    {
        if (_boundSubmitEvents) return;
        if (!submitButton) return;

        submitButton.OnAnswerSelecting.AddListener(HandleSelecting);
        submitButton.OnAnswerHolding.AddListener(HandleHolding);
        submitButton.OnAnswerHoldConfirmed.AddListener(HandleAnswerConfirmed);
        submitButton.OnAnswerHoldCanceled.AddListener(HandleHoldCanceled);

        _boundSubmitEvents = true;
        Debug.Log("[OptionHighLight] 已绑定 Answer-stage SubmitButton events");
    }

    void UnbindAnswerStageEvents()
    {
        if (!_boundSubmitEvents) return;
        if (!submitButton) return;

        submitButton.OnAnswerSelecting.RemoveListener(HandleSelecting);
        submitButton.OnAnswerHolding.RemoveListener(HandleHolding);
        submitButton.OnAnswerHoldConfirmed.RemoveListener(HandleAnswerConfirmed);
        submitButton.OnAnswerHoldCanceled.RemoveListener(HandleHoldCanceled);

        _boundSubmitEvents = false;
    }

    // ======================= SubmitButton 回调（Answer stage） =======================

    void HandleSelecting()
    {
        if (!_pendingHighlightEnabled) return;

        EnsureInit();
        isHolding = true;
        holdProgress = 0f;

        ApplyColors(GetCurrentIndexSafe());
    }

    void HandleHolding(float p)
    {
        if (!_pendingHighlightEnabled) return;

        EnsureInit();
        UpdateHoldVisual(p);

        // ✅ 关键：每帧长按都刷新一次（否则 Update 被 gate 时看不到渐变）
        ApplyColors(GetCurrentIndexSafe());
    }

    void HandleHoldCanceled()
    {
        if (!_pendingHighlightEnabled) return;

        EnsureInit();
        CancelHoldVisual();
        ApplyColors(GetCurrentIndexSafe());
    }

    void HandleAnswerConfirmed()
    {
        // ✅ Confirm 本身允许在 pending OFF 时也能调用（你若想彻底禁 confirm，可改为 if(!_pendingHighlightEnabled) return;)
        EnsureInit();
        ConfirmCurrentSelection();
    }

    // ======================= Update：旋钮指向高亮 =======================

    void Update()
    {
        if (!Application.isPlaying) return;
        if (!knobCore) return;

        if (bgImages == null || bgImages.Length == 0)
        {
            TryInitBGs();
            if (bgImages == null || bgImages.Length == 0) return;
        }

        // ✅ 不要默认掐死卡片模式；由 requireKnobMode 决定
        if (requireKnobMode && knobModeManager && !knobModeManager.isKnobMode)
            return;

        // ✅ 关闭待选高亮时：不再跟随旋钮，只保留 confirmedIndex 的颜色
        if (!_pendingHighlightEnabled)
        {
            ApplyColors(-1); // -1 表示没有 currentIndex
            return;
        }

        int idx = Mathf.Clamp(knobCore.CurrentSlot - 1, 0, bgImages.Length - 1);
        if (idx != lastIndex) lastIndex = idx;

        ApplyColors(idx);
    }

    void ApplyColors(int currentIndex)
    {
        for (int i = 0; i < bgImages.Length; i++)
        {
            var img = bgImages[i];
            if (!img) continue;

            if (i == confirmedIndex)
            {
                img.color = confirmedColor;
            }
            else if (currentIndex >= 0 && i == currentIndex)
            {
                img.color = isHolding
                    ? Color.Lerp(highlightColor, confirmedColor, holdProgress)
                    : highlightColor;
            }
            else
            {
                img.color = originalColors[i];
            }
        }
    }

    // ======================= Init / Utils =======================

    void EnsureInit()
    {
        if (bgImages == null || bgImages.Length == 0)
            TryInitBGs();
    }

    int GetCurrentIndexSafe()
    {
        if (!knobCore) return 0;
        if (bgImages == null || bgImages.Length == 0) return 0;
        return Mathf.Clamp(knobCore.CurrentSlot - 1, 0, bgImages.Length - 1);
    }

    void TryInitBGs()
    {
        var allImages = GetComponentsInChildren<Image>(true);
        var listBG    = new List<Image>();
        var listBtn   = new List<Button>();

        foreach (var img in allImages)
        {
            if (img != null && img.name == "BG")
            {
                listBG.Add(img);
                Button btn = img.GetComponentInParent<Button>();
                listBtn.Add(btn);
            }
        }

        if (listBG.Count == 0)
        {
            Debug.LogWarning("[OptionHighLight] 在子节点里没找到名为 BG 的 Image");
            return;
        }

        bgImages       = listBG.ToArray();
        optionButtons  = listBtn.ToArray();
        originalColors = new Color[bgImages.Length];

        for (int i = 0; i < bgImages.Length; i++)
            originalColors[i] = bgImages[i].color;

        lastIndex    = -1;
        isHolding    = false;
        holdProgress = 0f;

        Debug.Log($"[OptionHighLight] 初始化完成，BG 数量：{bgImages.Length}");
    }

    void RestoreAll()
    {
        if (bgImages == null || originalColors == null) return;

        for (int i = 0; i < bgImages.Length; i++)
        {
            var img = bgImages[i];
            if (!img) continue;
            img.color = originalColors[i];
        }

        lastIndex      = -1;
        confirmedIndex = -1;
        isHolding      = false;
        holdProgress   = 0f;
    }

    // ======================= 对外 API =======================

    public void ConfirmCurrentSelection()
    {
        if (!enableClickOnConfirm) return;
        if (!knobCore) return;

        EnsureInit();
        if (bgImages == null || bgImages.Length == 0 || optionButtons == null || optionButtons.Length == 0)
        {
            Debug.LogWarning("[OptionHighLight] 还没初始化 BG/Button，ConfirmCurrentSelection 无效");
            return;
        }

        int idx = Mathf.Clamp(knobCore.CurrentSlot - 1, 0, optionButtons.Length - 1);
        var btn = optionButtons[idx];

        if (!btn)
        {
            Debug.LogWarning($"[OptionHighLight] idx={idx} 对应的 Button 为空");
            return;
        }

        for (int i = 0; i < bgImages.Length; i++)
            bgImages[i].color = originalColors[i];

        bgImages[idx].color = confirmedColor;
        confirmedIndex      = idx;
        isHolding           = false;
        holdProgress        = 0f;

        Debug.Log($"[OptionHighLight] ✅ Answer确认 → slot={knobCore.CurrentSlot}, index={idx}, Button={btn.name}");
        btn.onClick?.Invoke();

        if (ALLCONTROL.Instance != null)
        {
            int slot = knobCore.CurrentSlot;
            ALLCONTROL.Instance.SetAnswerByIndex(slot);
        }

        // ✅ Confirm 后如果 pending OFF，也确保只显示 confirmed
        if (!_pendingHighlightEnabled)
            ApplyColors(-1);
    }

    public void UpdateHoldVisual(float progress)
    {
        isHolding    = true;
        holdProgress = Mathf.Clamp01(progress);
    }

    public void CancelHoldVisual()
    {
        isHolding    = false;
        holdProgress = 0f;
    }

    public void ResetVisual()
    {
        RestoreAll();
        Debug.Log("[OptionHighLight] ResetVisual -> 所有卡片 BG 恢复原色");
    }

    public void RestoreConfirmedVisual(int slot)
    {
        EnsureInit();
        if (bgImages == null || bgImages.Length == 0) return;

        int idx = Mathf.Clamp(slot - 1, 0, bgImages.Length - 1);

        for (int i = 0; i < bgImages.Length; i++)
        {
            var img = bgImages[i];
            if (!img) continue;
            img.color = (i == idx) ? confirmedColor : originalColors[i];
        }

        confirmedIndex = idx;
        isHolding      = false;
        holdProgress   = 0f;

        // ✅ 如果 pending OFF，确保不出现待选
        if (!_pendingHighlightEnabled)
            ApplyColors(-1);
    }

    // ======================= ✅ NEW：供外部调用的“开/关待选高亮” =======================

    /// <summary>
    /// ✅ 开启“待选高亮”（旋钮指向高亮 + Hold 渐变）
    /// - 不会清除 confirmedIndex（已选绿高亮仍保留）
    /// </summary>
    public void EnablePendingHighlight()
    {
        EnsureInit();
        _pendingHighlightEnabled = true;

        // 不改 confirmedIndex，立即刷新
        ApplyColors(GetCurrentIndexSafe());

        Debug.Log("[OptionHighLight] ✅ EnablePendingHighlight -> pending highlight ON (keep confirmed)");
    }

    /// <summary>
    /// ✅ 关闭“待选高亮”（不再跟随旋钮）
    /// - 保留 confirmedIndex 的高亮（已选项仍是绿的）
    /// - 清掉 holding 渐变状态，避免残留把颜色刷回去
    /// </summary>
    public void DisablePendingHighlightKeepConfirmed()
    {
        EnsureInit();
        _pendingHighlightEnabled = false;

        isHolding = false;
        holdProgress = 0f;
        lastIndex = -1;

        // 只渲染 confirmedIndex
        ApplyColors(-1);

        Debug.Log("[OptionHighLight] ✅ DisablePendingHighlightKeepConfirmed -> pending highlight OFF (keep confirmed)");
    }
}
