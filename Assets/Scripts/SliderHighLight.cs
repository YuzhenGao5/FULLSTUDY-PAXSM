// 文件名：SliderTickHighLight.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SliderTickHighLight : MonoBehaviour
{
    [Header("关联")]
    public KnobCore knobCore;
    public KnobModeManager knobModeManager;

    [Header("✅ 监听的按钮事件（Answer stage）")]
    [Tooltip("拖：StageAwarePagerSubmitButton（要能触发 OnAnswerSelecting/OnAnswerHolding/OnAnswerHoldConfirmed/OnAnswerHoldCanceled）")]
    public StageAwarePagerSubmitButton submitButton;

    [Tooltip("✅ slider 模式一般不走 knobMode；若你想强制只有 knobMode 才高亮/确认，就勾上")]
    public bool requireKnobMode = false;

    [Tooltip("刻度线父节点（通常是 QE_SliderBarSimple/BarRoot/TickContainer）")]
    public RectTransform tickContainer;

    [Header("颜色 & 高度")]
    public Color highlightColor = new Color(1f, 0.1f, 0.7f, 1f);
    public Color confirmedColor = new Color(0.2f, 1f, 0.6f, 1f);

    [Tooltip("高亮时刻度高度倍率")]
    public float highlightHeightMul = 1.4f;

    [Tooltip("确认后刻度高度倍率")]
    public float confirmedHeightMul = 2.0f;

    [Header("点击行为")]
    [Tooltip("仅控制：确认时是否触发刻度 Button.onClick。✅ 不影响 confirmed 视觉 & 写 ALLCONTROL")]
    public bool enableClickOnConfirm = true;

    [Header("✅ Pending Highlight Gate")]
    [Tooltip("控制“待选高亮”（当前指向刻度 + holding 渐变）。关闭后只保留 confirmed 高亮。")]
    public bool pendingHighlightEnabled = true;

    Image[] tickImages;
    Color[] originalColors;
    Vector2[] originalSizes;
    Button[] tickButtons;

    int lastIndex = -1;
    int confirmedIndex = -1;

    // 长按状态
    bool isHolding = false;
    float holdProgress = 0f;

    bool _bound = false;

    // ✅ NEW：用来判断“换题后缓存是否过期”
    int _tickContainerId = 0;
    int _tickChildCount = -1;

    void Reset()
    {
        if (!tickContainer && transform.Find("BarRoot/TickContainer") is Transform tc)
            tickContainer = tc as RectTransform;
    }

    void OnEnable()
    {
        BindButtonEvents();
        EnsureInit(true);
        ApplyVisuals(GetCurrentIndexSafe());
    }

    void OnDisable()
    {
        UnbindButtonEvents();
    }

    void BindButtonEvents()
    {
        if (_bound) return;
        if (!submitButton) return;

        submitButton.OnAnswerSelecting.AddListener(OnAnswerSelecting);
        submitButton.OnAnswerHolding.AddListener(OnAnswerHolding);
        submitButton.OnAnswerHoldCanceled.AddListener(OnAnswerCanceled);
        submitButton.OnAnswerHoldConfirmed.AddListener(OnAnswerConfirmed);

        _bound = true;
        Debug.Log("[SliderTickHighLight] ✅ Bound submitButton Answer events");
    }

    void UnbindButtonEvents()
    {
        if (!_bound) return;
        if (!submitButton) return;

        submitButton.OnAnswerSelecting.RemoveListener(OnAnswerSelecting);
        submitButton.OnAnswerHolding.RemoveListener(OnAnswerHolding);
        submitButton.OnAnswerHoldCanceled.RemoveListener(OnAnswerCanceled);
        submitButton.OnAnswerHoldConfirmed.RemoveListener(OnAnswerConfirmed);

        _bound = false;
    }

    // ====== ✅ 关键：检测缓存是否失效（换题后 tickContainer 或 children 变了） ======

    bool IsCacheStale()
    {
        if (!tickContainer) return true;

        int id = tickContainer.GetInstanceID();
        int cc = tickContainer.childCount;

        if (_tickContainerId != id) return true;
        if (_tickChildCount != cc) return true;

        if (tickImages == null || tickImages.Length == 0) return true;

        // 任何一个 Image 变 null 都视为缓存过期（很常见：换题时对象被销毁/disable）
        for (int i = 0; i < tickImages.Length; i++)
        {
            if (tickImages[i] == null) return true;
        }

        return false;
    }

    /// <summary>
    /// EnsureInit(forceRebuild=true) 可强制重建缓存。
    /// </summary>
    bool EnsureInit(bool forceRebuild = false)
    {
        if (forceRebuild || IsCacheStale())
        {
            TryInitTicks();
        }
        return (tickImages != null && tickImages.Length > 0);
    }

    // ====== ✅ 两个开关函数 ======

    public void EnablePendingHighlight()
    {
        pendingHighlightEnabled = true;
        Debug.Log("[SliderTickHighLight] ✅ EnablePendingHighlight -> pendingHighlightEnabled=true");

        if (!EnsureInit()) return;

        // ✅ 开 pending 时也同步一次 confirmed（防止你换题后 confirmedIndex 丢了）
        SyncConfirmedFromAllControlIfAny();
        ApplyVisuals(GetCurrentIndexSafe());
    }

    public void DisablePendingHighlight()
    {
        pendingHighlightEnabled = false;
        isHolding = false;
        holdProgress = 0f;

        Debug.Log("[SliderTickHighLight] ✅ DisablePendingHighlight -> pendingHighlightEnabled=false (keep confirmed)");

        if (!EnsureInit()) return;

        SyncConfirmedFromAllControlIfAny();
        ApplyVisuals(GetCurrentIndexSafe());
    }

    // ====== 事件回调 ======

    void OnAnswerSelecting()
    {
        if (!EnsureInit()) return;
        UpdateHoldVisual(0f);
        ApplyVisuals(GetCurrentIndexSafe());
    }

    void OnAnswerHolding(float p)
    {
        if (!EnsureInit()) return;
        UpdateHoldVisual(p);
        ApplyVisuals(GetCurrentIndexSafe());
    }

    void OnAnswerCanceled()
    {
        if (!EnsureInit()) return;
        CancelHoldVisual();
        ApplyVisuals(GetCurrentIndexSafe());
    }

    void OnAnswerConfirmed()
    {
        ConfirmCurrentSelection();
    }

    int GetCurrentIndexSafe()
    {
        if (!knobCore) return 0;
        if (tickImages == null || tickImages.Length == 0) return 0;
        return Mathf.Clamp(knobCore.CurrentSlot - 1, 0, tickImages.Length - 1);
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (!knobCore) return;

        if (requireKnobMode && knobModeManager && !knobModeManager.isKnobMode)
            return;

        if (!EnsureInit()) return;

        int idx = Mathf.Clamp(knobCore.CurrentSlot - 1, 0, tickImages.Length - 1);
        if (idx != lastIndex) lastIndex = idx;

        ApplyVisuals(idx);
    }

    void ApplyVisuals(int currentIndex)
    {
        if (tickImages == null || tickImages.Length == 0) return;

        for (int i = 0; i < tickImages.Length; i++)
        {
            var img = tickImages[i];
            if (!img) continue;

            var rt = img.rectTransform;

            // 1) confirmed 永远优先显示
            if (i == confirmedIndex)
            {
                img.color = confirmedColor;
                rt.sizeDelta = new Vector2(originalSizes[i].x, originalSizes[i].y * confirmedHeightMul);
                continue;
            }

            // 2) 关闭待选：非 confirmed 全部恢复
            if (!pendingHighlightEnabled)
            {
                img.color = originalColors[i];
                rt.sizeDelta = originalSizes[i];
                continue;
            }

            // 3) 待选开启：当前 index 高亮/渐变，其余恢复
            if (i == currentIndex)
            {
                float hMul = highlightHeightMul;
                Color c = highlightColor;

                if (isHolding)
                {
                    hMul = Mathf.Lerp(highlightHeightMul, confirmedHeightMul, holdProgress);
                    c = Color.Lerp(highlightColor, confirmedColor, holdProgress);
                }

                img.color = c;
                rt.sizeDelta = new Vector2(originalSizes[i].x, originalSizes[i].y * hMul);
            }
            else
            {
                img.color = originalColors[i];
                rt.sizeDelta = originalSizes[i];
            }
        }
    }

    void TryInitTicks()
    {
        if (!tickContainer)
        {
            Debug.LogWarning("[SliderTickHighLight] 没有设置 tickContainer");
            return;
        }

        var images = new List<Image>();
        var buttons = new List<Button>();

        for (int i = 0; i < tickContainer.childCount; i++)
        {
            var child = tickContainer.GetChild(i);

            var img = child.GetComponent<Image>();
            if (!img) img = child.GetComponentInChildren<Image>(true);

            if (img != null)
            {
                images.Add(img);

                Button btn = child.GetComponent<Button>();
                if (!btn) btn = child.GetComponentInParent<Button>();
                buttons.Add(btn);
            }
        }

        if (images.Count == 0)
        {
            Debug.LogWarning("[SliderTickHighLight] 在 tickContainer 下没找到刻度线 Image");
            return;
        }

        tickImages = images.ToArray();
        tickButtons = buttons.ToArray();
        originalColors = new Color[tickImages.Length];
        originalSizes = new Vector2[tickImages.Length];

        for (int i = 0; i < tickImages.Length; i++)
        {
            originalColors[i] = tickImages[i].color;
            originalSizes[i] = tickImages[i].rectTransform.sizeDelta;
        }

        // ✅ 更新缓存签名（换题后会变）
        _tickContainerId = tickContainer.GetInstanceID();
        _tickChildCount = tickContainer.childCount;

        // ✅ 重建时不要盲目清 confirmed：先从 ALLCONTROL 同步一次
        confirmedIndex = -1;
        lastIndex = -1;
        isHolding = false;
        holdProgress = 0f;

        SyncConfirmedFromAllControlIfAny();

        Debug.Log($"[SliderTickHighLight] ✅ ReInit ticks: count={tickImages.Length}, containerId={_tickContainerId}, childCount={_tickChildCount}, confirmedIndex={confirmedIndex}");
    }

    void SyncConfirmedFromAllControlIfAny()
    {
        if (tickImages == null || tickImages.Length == 0) return;

        // 如果 ALLCONTROL 当前题已经回答，就把 confirmedIndex 对齐
        if (ALLCONTROL.Instance != null && ALLCONTROL.Instance.TryGetAnswerForCurrent(out int slot) && slot > 0)
        {
            confirmedIndex = Mathf.Clamp(slot - 1, 0, tickImages.Length - 1);
        }
    }

    public void ConfirmCurrentSelection()
    {
        if (!knobCore) return;
        if (requireKnobMode && knobModeManager && !knobModeManager.isKnobMode) return;
        if (!EnsureInit()) return;

        int idx = Mathf.Clamp(knobCore.CurrentSlot - 1, 0, tickImages.Length - 1);
        Button btn = (tickButtons != null && idx < tickButtons.Length) ? tickButtons[idx] : null;

        // 先恢复所有
        for (int i = 0; i < tickImages.Length; i++)
        {
            var img = tickImages[i];
            if (!img) continue;
            img.color = originalColors[i];
            img.rectTransform.sizeDelta = originalSizes[i];
        }

        // confirmed
        var curImg = tickImages[idx];
        curImg.color = confirmedColor;
        curImg.rectTransform.sizeDelta = new Vector2(originalSizes[idx].x, originalSizes[idx].y * confirmedHeightMul);

        confirmedIndex = idx;
        isHolding = false;
        holdProgress = 0f;

        Debug.Log($"[SliderTickHighLight] ✅ Confirm -> slot={knobCore.CurrentSlot}, index={idx}, btn={(btn ? btn.name : "null")}");

        // 仅控制按钮触发
        if (enableClickOnConfirm && btn != null)
            btn.onClick?.Invoke();

        // 写入 ALLCONTROL
        if (ALLCONTROL.Instance != null)
        {
            int slot = knobCore.CurrentSlot;
            ALLCONTROL.Instance.SetAnswerByIndex(slot);
            Debug.Log($"[SliderTickHighLight] ▶ Write ALLCONTROL: q={ALLCONTROL.Instance.currentIndex}, slot={slot}");
        }
        else
        {
            Debug.LogWarning("[SliderTickHighLight] ALLCONTROL.Instance 为空，无法记录答案");
        }
    }

    public void UpdateHoldVisual(float progress)
    {
        isHolding = true;
        holdProgress = Mathf.Clamp01(progress);
    }

    public void CancelHoldVisual()
    {
        isHolding = false;
        holdProgress = 0f;
    }

    public void ResetVisual()
    {
        if (!EnsureInit(true)) return; // ✅ Reset 时强制重建，最稳

        pendingHighlightEnabled = true;
        confirmedIndex = -1;
        lastIndex = -1;
        isHolding = false;
        holdProgress = 0f;

        // 清回原样
        for (int i = 0; i < tickImages.Length; i++)
        {
            var img = tickImages[i];
            if (!img) continue;
            img.color = originalColors[i];
            img.rectTransform.sizeDelta = originalSizes[i];
        }

        Debug.Log("[SliderTickHighLight] ResetVisual -> forceRebuild + pending=true");

        ApplyVisuals(GetCurrentIndexSafe());
    }

    public void RestoreConfirmedVisual(int slot)
    {
        if (!EnsureInit()) return;

        int idx = Mathf.Clamp(slot - 1, 0, tickImages.Length - 1);

        for (int i = 0; i < tickImages.Length; i++)
        {
            var img = tickImages[i];
            if (!img) continue;

            var rt = img.rectTransform;

            if (i == idx)
            {
                img.color = confirmedColor;
                rt.sizeDelta = new Vector2(originalSizes[i].x, originalSizes[i].y * confirmedHeightMul);
            }
            else
            {
                img.color = originalColors[i];
                rt.sizeDelta = originalSizes[i];
            }
        }

        confirmedIndex = idx;
        isHolding = false;
        holdProgress = 0f;

        Debug.Log($"[SliderTickHighLight] RestoreConfirmedVisual: slot={slot}, index={idx}");
    }

    // （可选）外部如果换题会换一个新的 tickContainer，可以显式调用它
    public void SetTickContainer(RectTransform newContainer, bool forceReinit = true)
    {
        tickContainer = newContainer;
        if (forceReinit) EnsureInit(true);
        ApplyVisuals(GetCurrentIndexSafe());
    }
}
