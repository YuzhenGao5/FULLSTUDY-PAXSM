// 文件名：ConfidenceHighLight.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ConfidenceHighLight : MonoBehaviour
{
    [Header("关联")]
    public KnobCore knobCore;
    public KnobModeManager knobModeManager;

    [Header("✅ Submit Button Events (Submit stage only)")]
    public StageAwarePagerSubmitButton submitButton;

    [Header("Mode Gate")]
    public bool requireKnobMode = false;

    [Header("信号条容器（SigBar_x 都在这里下面）")]
    public RectTransform contentRoot;

    [Header("颜色设置")]
    public Color highlightColor = new Color(1f, 0.1f, 0.7f, 1f);
    public Color confirmedColor = new Color(0.2f, 1f, 0.6f, 1f);

    [Header("信号条行为")]
    public bool fillUpToSelection = true;

    [Header("Confirm 时是否打点到 ALLCONTROL.Record")]
    public bool recordToAllcontrol = true;

    [Header("自动查找规则")]
    public string barNamePrefix = "SigBar_";

    class BarRef
    {
        public GameObject go;
        public Image bg;
        public Image fill;
        public Image hi;
        public Color bg0;
        public Color hi0;
        public bool hasBg0;
        public bool hasHi0;
        public bool hiIsOverlay;
    }

    readonly List<BarRef> _bars = new();

    int lastIndex = -1;
    int confirmedIndex = -1;

    bool isHolding = false;
    float holdProgress = 0f;

    bool _bound = false;

    // ✅ NEW：控制“待选高亮/跟随旋钮”
    bool _pendingEnabled = true;
    bool _dirtyApplyWhenLocked = false;

    void Reset()
    {
        if (!contentRoot) contentRoot = GetComponent<RectTransform>();
    }

    void Awake()
    {
        if (!contentRoot) contentRoot = GetComponent<RectTransform>();
    }

    void OnEnable()  => BindSubmitStageEventsIfNeeded();
    void OnDisable() => UnbindSubmitStageEvents();

    void BindSubmitStageEventsIfNeeded()
    {
        if (_bound) return;
        if (!submitButton) return;

        submitButton.OnSubmitSelecting.AddListener(HandleSubmitSelecting);
        submitButton.OnSubmitHolding.AddListener(HandleSubmitHolding);
        submitButton.OnSubmitHoldConfirmed.AddListener(HandleSubmitConfirmed);
        submitButton.OnSubmitHoldCanceled.AddListener(HandleSubmitCanceled);

        _bound = true;
        Debug.Log("[ConfidenceHighLight] 已绑定 Submit-stage SubmitButton events");
    }

    void UnbindSubmitStageEvents()
    {
        if (!_bound) return;
        if (!submitButton) return;

        submitButton.OnSubmitSelecting.RemoveListener(HandleSubmitSelecting);
        submitButton.OnSubmitHolding.RemoveListener(HandleSubmitHolding);
        submitButton.OnSubmitHoldConfirmed.RemoveListener(HandleSubmitConfirmed);
        submitButton.OnSubmitHoldCanceled.RemoveListener(HandleSubmitCanceled);

        _bound = false;
    }

    // ======================= SubmitButton 回调（Submit stage） =======================

    void HandleSubmitSelecting()
    {
        if (!_pendingEnabled) return;
        EnsureInit();
        isHolding = true;
        holdProgress = 0f;
        ApplyVisual(GetCurrentIndexSafe());
    }

    void HandleSubmitHolding(float p)
    {
        if (!_pendingEnabled) return;
        EnsureInit();
        UpdateHoldVisual(p);
        ApplyVisual(GetCurrentIndexSafe());
    }

    void HandleSubmitCanceled()
    {
        if (!_pendingEnabled) return;
        EnsureInit();
        CancelHoldVisual();
        ApplyVisual(GetCurrentIndexSafe());
    }

    void HandleSubmitConfirmed()
    {
        if (!_pendingEnabled) return;
        EnsureInit();
        ConfirmCurrentSelection();
    }

    // ======================= Update：旋钮指向高亮 =======================

    void Update()
    {
        if (!Application.isPlaying) return;
        if (!knobCore) return;

        if (_bars.Count == 0)
        {
            TryInitBars();
            if (_bars.Count == 0) return;
        }

        if (requireKnobMode && knobModeManager && !knobModeManager.isKnobMode)
            return;

        // ✅ 锁住待选：不再跟随 knob，只保留 confirmed
        if (!_pendingEnabled)
        {
            if (_dirtyApplyWhenLocked)
            {
                ApplyVisual(-1);
                _dirtyApplyWhenLocked = false;
            }
            return;
        }

        int idx = Mathf.Clamp(knobCore.CurrentSlot - 1, 0, _bars.Count - 1);
        if (idx != lastIndex) lastIndex = idx;

        ApplyVisual(idx);
    }

    // ======================= Init / Utils =======================

    void EnsureInit()
    {
        if (_bars.Count == 0) TryInitBars();
    }

    int GetCurrentIndexSafe()
    {
        if (!knobCore) return 0;
        if (_bars.Count == 0) return 0;
        return Mathf.Clamp(knobCore.CurrentSlot - 1, 0, _bars.Count - 1);
    }

    void TryInitBars()
    {
        _bars.Clear();

        if (!contentRoot)
        {
            Debug.LogWarning("[ConfidenceHighLight] contentRoot 为空，无法初始化。", this);
            return;
        }

        for (int i = 0; i < contentRoot.childCount; i++)
        {
            var child = contentRoot.GetChild(i);
            if (!child) continue;
            if (!string.IsNullOrEmpty(barNamePrefix) && !child.name.StartsWith(barNamePrefix)) continue;

            var go = child.gameObject;
            var rt = go.GetComponent<RectTransform>();
            if (!rt) continue;

            Image bg = null;
            var bgTr = rt.Find("BG");
            if (bgTr) bg = bgTr.GetComponent<Image>();
            if (!bg) bg = go.GetComponent<Image>();

            Image fill = null;
            var fillTr = rt.Find("Fill");
            if (fillTr) fill = fillTr.GetComponent<Image>();

            Image hi = null;
            var hiTr = rt.Find("Highlight");
            if (hiTr) hi = hiTr.GetComponent<Image>();
            if (!hi) hi = bg;

            var br = new BarRef
            {
                go = go,
                bg = bg,
                fill = fill,
                hi = hi,
                hiIsOverlay = (hi != null && bg != null && hi != bg)
            };

            if (bg != null) { br.bg0 = bg.color; br.hasBg0 = true; }
            if (hi != null) { br.hi0 = hi.color; br.hasHi0 = true; }

            _bars.Add(br);
        }

        lastIndex = -1;
        confirmedIndex = -1;
        isHolding = false;
        holdProgress = 0f;

        Debug.Log($"[ConfidenceHighLight] 初始化完成，Bars 数量：{_bars.Count}", this);
    }

    // ======================= 核心视觉 =======================

    void ApplyVisual(int currentIndex)
    {
        for (int i = 0; i < _bars.Count; i++)
        {
            var b = _bars[i];
            if (b == null) continue;

            bool isConfirmed = (i == confirmedIndex && confirmedIndex >= 0);
            bool isCurrent   = (currentIndex >= 0 && i == currentIndex);

            // Fill：优先按 confirmed；没有 confirmed 才按 current；都没有则全灭
            if (b.fill != null)
            {
                if (fillUpToSelection)
                {
                    int k = 0;
                    if (confirmedIndex >= 0) k = confirmedIndex + 1;
                    else if (currentIndex >= 0) k = currentIndex + 1;
                    else k = 0;

                    b.fill.enabled = (i < k);
                }
                else
                {
                    b.fill.enabled = true;
                }
            }

            if (b.hiIsOverlay && b.hi != null)
            {
                // ✅ overlay 的情况下：锁住时 currentIndex = -1，所以不会显示“当前”
                b.hi.enabled = isConfirmed || isCurrent;

                if (isConfirmed) b.hi.color = confirmedColor;
                else if (isCurrent) b.hi.color = isHolding ? Color.Lerp(highlightColor, confirmedColor, holdProgress) : highlightColor;
                else if (b.hasHi0) b.hi.color = b.hi0;
            }
            else
            {
                if (b.bg == null) continue;

                if (isConfirmed) b.bg.color = confirmedColor;
                else if (isCurrent) b.bg.color = isHolding ? Color.Lerp(highlightColor, confirmedColor, holdProgress) : highlightColor;
                else if (b.hasBg0) b.bg.color = b.bg0;
            }
        }
    }

    void RestoreAll()
    {
        for (int i = 0; i < _bars.Count; i++)
        {
            var b = _bars[i];
            if (b == null) continue;

            if (b.fill != null) b.fill.enabled = false;

            if (b.hiIsOverlay && b.hi != null)
            {
                b.hi.enabled = false;
                if (b.hasHi0) b.hi.color = b.hi0;
            }

            if (b.bg != null && b.hasBg0) b.bg.color = b.bg0;
        }

        lastIndex = -1;
        confirmedIndex = -1;
        isHolding = false;
        holdProgress = 0f;
    }

    // ======================= 对外 API（你要的两个开关） =======================

    public void EnablePendingHighlight()
    {
        _pendingEnabled = true;
        isHolding = false;
        holdProgress = 0f;
        _dirtyApplyWhenLocked = false;
    }

    public void DisablePendingHighlightKeepConfirmed()
    {
        _pendingEnabled = false;
        isHolding = false;
        holdProgress = 0f;
        lastIndex = -1;
        _dirtyApplyWhenLocked = true;
    }

    // ======================= 对外 API（原有） =======================

    public void ConfirmCurrentSelection()
    {
        if (!knobCore) return;
        if (!_pendingEnabled) return; // ✅ 锁住时禁止确认

        if (requireKnobMode && knobModeManager && !knobModeManager.isKnobMode) return;

        EnsureInit();
        if (_bars.Count == 0)
        {
            Debug.LogWarning("[ConfidenceHighLight] Bars 还没初始化，ConfirmCurrentSelection 无效", this);
            return;
        }

        int level = Mathf.Clamp(knobCore.CurrentSlot, 1, _bars.Count);
        confirmedIndex = level - 1;

        isHolding = false;
        holdProgress = 0f;

        ApplyVisual(confirmedIndex);

        Debug.Log($"[ConfidenceHighLight] ✅ Submit确认信心 → level={level}, index={confirmedIndex}", this);

        if (ALLCONTROL.Instance != null)
        {
            ALLCONTROL.Instance.SetSelfConfidenceForCurrent(level);
            if (recordToAllcontrol)
                ALLCONTROL.Instance.Record("SelfConfidenceConfirm", $"level={level}");

            ALLCONTROL.Instance.GoNextQuestion();
        }
        else
        {
            Debug.LogWarning("[ConfidenceHighLight] ALLCONTROL.Instance 为空，无法写入 selfConfidence", this);
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
        EnsureInit();
        RestoreAll();
        Debug.Log("[ConfidenceHighLight] ResetVisual -> 所有信号条恢复初始状态", this);
    }

    public void RestoreConfirmedVisual(int level)
    {
        EnsureInit();
        if (_bars.Count == 0) return;

        level = Mathf.Clamp(level, 1, _bars.Count);
        confirmedIndex = level - 1;

        isHolding = false;
        holdProgress = 0f;

        ApplyVisual(-1); // ✅ 只显示 confirmed，不显示“当前”
        Debug.Log($"[ConfidenceHighLight] RestoreConfirmedVisual -> level={level}, index={confirmedIndex}", this);
    }
}
