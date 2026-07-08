// 文件名：QuestionRenderer.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

#region 数据结构（与 JSON 对应）

[Serializable]
public class QB
{
    // 整套题统一刻度（JSON 顶层，优先级最高）
    public int scale = 7;

    // 可选标签（左右/中性）——没有也不影响
    [Serializable]
    public class Labels
    {
        public string left;
        public string right;
        public string neutral;
    }
    public Labels labels = new Labels();

    // 题目数组
    [Serializable]
    public class Item
    {
        public string id;    // 题目 ID
        public string stem;  // 题干文本
    }
    public List<Item> items = new();
}

#endregion

/// <summary>
/// 负责：
/// - 加载题库
/// - 解析整套题刻度
/// - 分页生成题目 UI
/// - ★ 每次翻页时通知 ALLCONTROL 当前题号
/// </summary>
public class QuestionRenderer : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("直接拖入 TextAsset（可不填，若使用 ResourcesPath 加载）")]
    public TextAsset questionBankJson;

    [Tooltip("Resources 内的相对路径（不带 .json 后缀），例如：QuestionBanks/NASATLX")]
    public string resourcesPath;

    [Min(2), Tooltip("当 JSON 未提供有效 scale 时的回退值")]
    public int fallbackLikertK = 7;

    [Header("Template & Mount")]
    [Tooltip("指向 QE_ItemRenderer 下的 ItemTemplate（必须 Inactive）")]
    public GameObject itemTemplate;

    [Tooltip("克隆体挂载的父节点（例如：QE_Container/ItemViewport/ItemContent）")]
    public Transform runtimeParent;

    [Min(1), Tooltip("每页显示条目数")]
    public int itemsPerPage = 1;   // 你当前一页一道题，默认给 1
    public bool clearOnBuild = true;

    [Header("Pager (Optional)")]
    public PagerController pager;   // 可不填；不填则默认只显示第 1 页

    // 解析后的题库
    private QB _bank;

    // ★ 解析并缓存下来的整套题刻度与标签（给外部读取）
    private int _resolvedScale = 7;
    private QB.Labels _resolvedLabels = new QB.Labels();

    // 运行时
    private int _currentPage = 0;
    private readonly List<GameObject> _spawned = new();

    /// 对外公开读取：整套题刻度
    public int ResolvedScale => _resolvedScale;

    /// 对外公开读取：标签（可能为空字符串）
    public QB.Labels ResolvedLabels => _resolvedLabels;

    void Awake()
    {
        // 1) 加载题库
        if (!questionBankJson && !string.IsNullOrEmpty(resourcesPath))
        {
            questionBankJson = Resources.Load<TextAsset>(resourcesPath);
        }
        if (!questionBankJson)
        {
            Debug.LogError("[QuestionRenderer] 未提供题库资源（questionBankJson / resourcesPath）。");
            return;
        }

        // 2) 解析 JSON
        try
        {
            _bank = JsonUtility.FromJson<QB>(questionBankJson.text);
        }
        catch (Exception e)
        {
            Debug.LogError("[QuestionRenderer] 题库解析失败: " + e);
            return;
        }

        if (_bank == null || _bank.items == null || _bank.items.Count == 0)
        {
            Debug.LogError("[QuestionRenderer] 题库为空或 items 数量为 0。");
            return;
        }

        // 3) 解析并缓存整套题刻度/标签
        _resolvedScale   = Mathf.Max(2, (_bank.scale > 1) ? _bank.scale : fallbackLikertK);
        _resolvedLabels  = _bank.labels ?? new QB.Labels();

        // 4) 初始化分页
        int pageCount = Mathf.CeilToInt((float)_bank.items.Count / Mathf.Max(1, itemsPerPage));
        if (pager)
        {
            pager.SetPageCount(pageCount);
            pager.OnPageChanged += ShowPage;
        }

        // 5) 首次显示第 0 页
        Debug.Log($"[QuestionRenderer] Awake：items={_bank.items.Count}, scale={_resolvedScale}, pages={pageCount}");
        ShowPage(0);
    }

    void OnDestroy()
    {
        if (pager) pager.OnPageChanged -= ShowPage;
    }

    /// <summary>
    /// 显示指定页：克隆模板 → 填题干 → 构建刻度条（如果有占位条）
    /// ★ 同时通知 ALLCONTROL：当前展示的题 globalIndex = start
    /// </summary>
    public void ShowPage(int pageIndex)
    {
        if (_bank == null) return;

        _currentPage = Mathf.Clamp(pageIndex, 0, MaxPageIndex());
        if (clearOnBuild) ClearRuntime();

        int per   = Mathf.Max(1, itemsPerPage);
        int start = _currentPage * per;
        int end   = Mathf.Min(start + per, _bank.items.Count);

        // ★ 告诉 ALLCONTROL：当前显示的“第一页上的第一道题”的全局 index
        if (ALLCONTROL.Instance != null)
        {
            ALLCONTROL.Instance.SetCurrentQuestion(start);
        }
        else
        {
            Debug.LogWarning("[QuestionRenderer] ShowPage 时 ALLCONTROL.Instance 为空，当前题号没被记录");
        }

        Debug.Log($"[QuestionRenderer] ShowPage({pageIndex})：start={start}, end={end}");

        for (int i = start; i < end; i++)
        {
            // 克隆模板
            var go = Instantiate(itemTemplate, runtimeParent);
            go.SetActive(true);
            _spawned.Add(go);

            // 填题干（ItemTemplate 里只保留 Text_Stem 即可）
            var stem = go.transform.Find("Text_Stem")?.GetComponent<TextMeshProUGUI>();
            if (stem) stem.text = _bank.items[i].stem;

            // 如果模板里还有一个 Image 占位条叫 "Bar_Placeholder"，就顺便画视觉刻度
            var bar = go.transform.Find("Bar_Placeholder") as RectTransform;
            if (bar) BuildTicks(bar, _resolvedScale);

            // （可选）记录元信息
            var meta = go.AddComponent<ItemRef>();
            meta.itemId      = _bank.items[i].id;
            meta.pageIndex   = _currentPage;
            meta.indexOnPage = i - start;
        }
    }

    int MaxPageIndex()
    {
        int per = Mathf.Max(1, itemsPerPage);
        return Mathf.Max(0, Mathf.CeilToInt((float)_bank.items.Count / per) - 1);
    }

    void ClearRuntime()
    {
        if (!runtimeParent) return;
        for (int i = runtimeParent.childCount - 1; i >= 0; i--)
        {
            Destroy(runtimeParent.GetChild(i).gameObject);
        }
        _spawned.Clear();
    }

    /// <summary>
    /// 在占位条上生成 k 根竖线（中位加粗）。
    /// 若没有占位条，本函数不会被调用。
    /// </summary>
    void BuildTicks(RectTransform bar, int k)
    {
        var root = bar.Find("TicksRoot");
        if (!root)
        {
            var go = new GameObject("TicksRoot", typeof(RectTransform));
            go.transform.SetParent(bar, false);
            var r = go.transform as RectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            root = go.transform;
        }
        else
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);
        }

        int steps = Mathf.Max(2, k);

        for (int i = 0; i < steps; i++)
        {
            var tick = new GameObject($"Tick_{i + 1}", typeof(RectTransform), typeof(Image));
            tick.transform.SetParent(root, false);

            var rt = (RectTransform)tick.transform;
            float t = (steps == 1) ? 0.5f : (float)i / (steps - 1);

            // 锚到指定 x；高充满
            rt.anchorMin = new Vector2(t, 0);
            rt.anchorMax = new Vector2(t, 1);

            bool isMid = (steps % 2 == 1) && (i == (steps - 1) / 2);
            rt.sizeDelta = new Vector2(isMid ? 3.5f : 2f, 0f);

            var img = tick.GetComponent<Image>();
            img.color = new Color(1, 1, 1, isMid ? 0.7f : 0.4f);
        }
    }

    /// <summary>可在运行时动态调整每页数量</summary>
    public void SetItemsPerPage(int value)
    {
        itemsPerPage = Mathf.Max(1, value);
        int pageCount = Mathf.CeilToInt((float)_bank.items.Count / itemsPerPage);
        if (pager) pager.SetPageCount(pageCount);
        ShowPage(Mathf.Min(_currentPage, pageCount - 1));
    }
}

/// 可选：每条实例的元数据（便于日志/追踪）
public class ItemRef : MonoBehaviour
{
    public string itemId;
    public int pageIndex;
    public int indexOnPage;
}
