// 文件名：SignalBarsPanel_OneScript.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SignalBarsPanel_OneScript : MonoBehaviour
{
    [Header("区域（RectTransform）")]
    [Tooltip("推荐：拖一个空的 ContentRoot（RectTransform），作为生成条的父物体")]
    public RectTransform contentRoot;

    [Header("条预制体（必须是 UI Prefab：含 RectTransform）")]
    [Tooltip("Prefab 根节点必须是 UI（RectTransform）。建议结构：BarRoot(Image) / Fill(Image) / Highlight(Image)")]
    public GameObject barPrefab;

    [Header("启动时自动生成")]
    public bool rebuildOnStart = true;

    [Header("数量 & 填充（暂时手输数字）")]
    [Min(1)] public int barsCount = 5;
    [Min(0)] public int filledCount = 0;

    [Header("高亮（当前选中条）")]
    [Tooltip("1-based：1..barsCount；0=不高亮")]
    public int highlightIndex = 0;

    [Header("高度从左到右变高（小→大）")]
    [Range(0f, 1f)] public float minHeight01 = 0.25f;
    [Range(0f, 1f)] public float maxHeight01 = 1.0f;
    public AnimationCurve heightCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Layout（自动铺满区域）")]
    public bool autoEnsureHorizontalLayoutGroup = true;
    public bool expandWidth = true;
    public float spacing = 8f;
    public int paddingLeft = 10, paddingRight = 10, paddingTop = 6, paddingBottom = 6;

    [Header("子层自动查找（要求子物体命名为 Fill/Highlight）")]
    public bool autoFindFillImage = true;
    public bool autoFindHighlightImage = true;

    [Header("（可选）Debug")]
    public bool verboseLog = false;

    class BarRef
    {
        public GameObject go;
        public RectTransform rt;
        public LayoutElement le;
        public Image fill;
        public Image highlight;
    }

    readonly List<BarRef> _bars = new();

    void Reset()
    {
        if (!contentRoot) contentRoot = GetComponent<RectTransform>();
    }

    void Awake()
    {
        if (!contentRoot) contentRoot = GetComponent<RectTransform>();
        if (autoEnsureHorizontalLayoutGroup) EnsureLayout();
    }

    void Start()
    {
        if (rebuildOnStart) Rebuild();
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        if (!contentRoot || !barPrefab)
        {
            Debug.LogWarning("[SignalBarsPanel] 缺少 contentRoot 或 barPrefab（barPrefab 必须是 UI Prefab）", this);
            return;
        }

        if (autoEnsureHorizontalLayoutGroup) EnsureLayout();

        Clear();

        barsCount = Mathf.Max(1, barsCount);
        filledCount = Mathf.Clamp(filledCount, 0, barsCount);
        highlightIndex = Mathf.Clamp(highlightIndex, 0, barsCount);

        // 用 contentRoot 的可用高度（扣掉 padding）
        float areaH = contentRoot.rect.height;
        float usableH = Mathf.Max(0f, areaH - paddingTop - paddingBottom);

        if (verboseLog)
            Debug.Log($"[SignalBarsPanel] areaH={areaH}, usableH={usableH}", this);

        for (int i = 0; i < barsCount; i++)
        {
            var go = Instantiate(barPrefab, contentRoot);
            go.name = $"SigBar_{i + 1}";
            go.SetActive(true);

            var rt = go.GetComponent<RectTransform>();
            if (!rt)
            {
                Debug.LogError("[SignalBarsPanel] barPrefab 不是 UI（没有 RectTransform）！请用 UI/Image 做 prefab。", this);
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(go);
                else Destroy(go);
#else
                Destroy(go);
#endif
                continue;
            }

            // ✅ 强制修复：不要纵向 Stretch，pivot 在底部，保证高度能被 LayoutElement 控制
            FixBarRectTransform(rt);

            // ✅ 确保 LayoutElement 存在
            var le = go.GetComponent<LayoutElement>();
            if (!le) le = go.AddComponent<LayoutElement>();

            le.flexibleWidth = expandWidth ? 1f : 0f;
            le.minWidth = 1f;

            // ✅ 左→右：小→大
            float t = (barsCount <= 1) ? 1f : i / (float)(barsCount - 1);
            float eased = Mathf.Clamp01(heightCurve.Evaluate(t));
            float h01 = Mathf.Lerp(minHeight01, maxHeight01, eased);

            // ✅ 关键：preferredHeight 生效需要 HorizontalLayoutGroup.childControlHeight = true
            le.preferredHeight = Mathf.Max(0f, h01 * usableH);

            // --- 自动找 Fill/Highlight ---
            Image fill = null;
            if (autoFindFillImage)
            {
                var tr = rt.Find("Fill");
                if (tr) fill = tr.GetComponent<Image>();
                if (fill) FixChildOverlayRect(fill.rectTransform);
            }

            Image hi = null;
            if (autoFindHighlightImage)
            {
                var tr = rt.Find("Highlight");
                if (tr) hi = tr.GetComponent<Image>();
                if (hi) FixChildOverlayRect(hi.rectTransform);
            }

            _bars.Add(new BarRef { go = go, rt = rt, le = le, fill = fill, highlight = hi });

            if (verboseLog)
                Debug.Log($"[SignalBarsPanel] spawn {go.name}: preferredH={le.preferredHeight}", this);
        }

        ApplyFilledVisual();
        ApplyHighlightVisual();

        Debug.Log($"[SignalBarsPanel] Rebuild OK. bars={barsCount}, filled={filledCount}, hi={highlightIndex}", this);

        // ✅ 强制刷新 Layout（避免第一次看起来没变化）
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        _bars.Clear();
        if (!contentRoot) return;

        for (int i = contentRoot.childCount - 1; i >= 0; i--)
        {
            var c = contentRoot.GetChild(i);
            if (!c) continue;
            if (!c.name.StartsWith("SigBar_")) continue;

#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(c.gameObject);
            else Destroy(c.gameObject);
#else
            Destroy(c.gameObject);
#endif
        }
    }

    // ===== 对外 API：你别处直接调用 =====

    public void SetBarsCount(int n)
    {
        barsCount = Mathf.Max(1, n);
        Rebuild();
    }

    public void SetFilledCount(int k)
    {
        filledCount = Mathf.Clamp(k, 0, Mathf.Max(1, barsCount));
        ApplyFilledVisual();
    }

    /// <summary>1-based: 1..barsCount；0=清空高亮</summary>
    public void SetHighlightIndex(int slot1Based)
    {
        highlightIndex = Mathf.Clamp(slot1Based, 0, Mathf.Max(1, barsCount));
        ApplyHighlightVisual();
    }

    public void ClearHighlight()
    {
        highlightIndex = 0;
        ApplyHighlightVisual();
    }

    // ===== 内部：视觉 =====

    void ApplyFilledVisual()
    {
        for (int i = 0; i < _bars.Count; i++)
        {
            bool on = i < filledCount;
            if (_bars[i].fill != null) _bars[i].fill.enabled = on;
        }
    }

    void ApplyHighlightVisual()
    {
        for (int i = 0; i < _bars.Count; i++)
        {
            int slot = i + 1;
            bool hiOn = (highlightIndex > 0 && slot == highlightIndex);
            if (_bars[i].highlight != null) _bars[i].highlight.enabled = hiOn;
        }
    }

    // ===== 内部：Layout =====

    void EnsureLayout()
    {
        if (!contentRoot) return;

        var hlg = contentRoot.GetComponent<HorizontalLayoutGroup>();
        if (!hlg) hlg = contentRoot.gameObject.AddComponent<HorizontalLayoutGroup>();

        hlg.childAlignment = TextAnchor.LowerLeft;
        hlg.padding = new RectOffset(paddingLeft, paddingRight, paddingTop, paddingBottom);
        hlg.spacing = spacing;

        hlg.childControlWidth = true;

        // ✅ 关键：必须为 true，否则 LayoutElement.preferredHeight 不生效，条会按 prefab 自己的高度/Stretch 撑满
        hlg.childControlHeight = true;

        hlg.childForceExpandWidth = expandWidth;
        hlg.childForceExpandHeight = false;

        // 推荐：让 contentRoot 本身不要被 ContentSizeFitter 干预
        var fitter = contentRoot.GetComponent<ContentSizeFitter>();
        if (fitter != null)
        {
            // 不强行删，你要用也可以；这里只提示
            if (verboseLog) Debug.LogWarning("[SignalBarsPanel] contentRoot 上有 ContentSizeFitter，可能影响布局。建议移除。", this);
        }
    }

    // ===== 内部：RectTransform 修复 =====

    void FixBarRectTransform(RectTransform rt)
    {
        // ✅ 不允许纵向 Stretch（y=0..1 会导致永远撑满）
        rt.anchorMin = new Vector2(rt.anchorMin.x, 0f);
        rt.anchorMax = new Vector2(rt.anchorMax.x, 0f);

        // ✅ pivot 在底部：从下往上长，像信号条
        rt.pivot = new Vector2(rt.pivot.x, 0f);

        // 给一个合理初始值（高度真正由 LayoutElement.preferredHeight 控）
        var sd = rt.sizeDelta;
        if (sd.y <= 0.01f) sd.y = 10f;
        rt.sizeDelta = sd;
    }

    void FixChildOverlayRect(RectTransform rt)
    {
        // ✅ 子层 Fill / Highlight 覆盖父条：全 Stretch
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
    }
}
