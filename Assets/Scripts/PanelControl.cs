using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class OptionCardsPanelSimple : MonoBehaviour
{
    [Header("Data")]
    public SimpleJsonReader reader;               // 拖有 SimpleJsonReader 的对象
    [Min(2)] public int fallbackScale = 5;        // 没读到就用它
    public bool useNumbersWhenNoLabels = true;    // 没有 labels 时是否显示 1..k

    [Header("Prefab & Mount")]
    public GameObject cardPrefab;                 // 预制体里需要有 TMP_Text 或 UGUI Text
    public RectTransform content;                 // 承接节点（RectTransform 宽度要设好）

    [Header("Manual Layout (无 LayoutGroup 时生效)")]
    public float spacing = 12f;
    public Vector4 padding = new Vector4(8, 8, 8, 8); // 左/上/右/下
    public bool forceSameSize = true;

    [Header("Auto Fit")]
    [Tooltip("根据 content 的可用宽度自动把每个卡片等分到合适宽度")]
    public bool fitToContentWidth = true;
    [Min(40f)] public float minCellWidth = 80f;
    [Min(20f)] public float minCellHeight = 40f;

    void Reset()
    {
        if (!content) content = transform as RectTransform;
    }

    void Start()
    {
        StartCoroutine(WaitReaderThenBuild());
    }

    /// <summary>
    /// 只有当 JSON 的 default_mode 是 "cards" 时才继续生成；
    /// 如果是 "slider" 或别的，就直接关掉这个面板。
    /// </summary>
    bool ShouldUseCards()
    {
        if (reader == null || reader.data == null)
        {
            // 兼容老 JSON：没有写 default_mode 就当 cards 使用
            return true;
        }

        var mode = reader.data.default_mode;
        if (string.IsNullOrEmpty(mode))
        {
            // 兼容：没填就当 cards
            return true;
        }

        // 忽略大小写比较
        if (string.Equals(mode, "cards", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// 等待 reader.data 可用后再生成，避免先用数字兜底
    IEnumerator WaitReaderThenBuild()
    {
        // 最多等待 1 秒（按需可调）
        float timeout = 1f, t = 0f;
        while (reader != null && reader.data == null && t < timeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        // 如果最终 reader 还是空，或者 data 是 slider 模式，就直接关闭自己
        if (reader == null || reader.data == null || !ShouldUseCards())
        {
            Debug.Log("[OptionCardsPanelSimple] default_mode != cards，禁用卡片面板");
            gameObject.SetActive(false);
            yield break;
        }

        // 再保险：labels 可能在下一帧才填好
        if (reader.data.labels == null || reader.data.labels.Count == 0)
        {
            yield return null;
        }

        BuildFromReader();
    }

    /// 手动重建（比如你换了 JSON）
    public void BuildFromReader()
    {
        if (!content) content = transform as RectTransform;

        // 🛑 如果不是 cards 模式，直接禁用自己，防止误调用
        if (!ShouldUseCards())
        {
            Debug.Log("[OptionCardsPanelSimple] BuildFromReader 发现 default_mode != cards，禁用卡片面板");
            gameObject.SetActive(false);
            return;
        }

        // 1) 读数据
        var k = fallbackScale;
        List<string> labels = null;

        if (reader != null && reader.data != null)
        {
            if (reader.data.scale > 1) k = reader.data.scale;
            labels = reader.data.labels; // 可能为 null
        }

        // 2) 准备名字数组（没有就 1..k）
        var names = new List<string>(k);
        for (int i = 0; i < k; i++)
        {
            string n = (labels != null && i < labels.Count && !string.IsNullOrEmpty(labels[i]))
                ? labels[i]
                : (useNumbersWhenNoLabels ? (i + 1).ToString() : string.Empty);
            names.Add(n);
        }

        // 3) 清空旧的
        ClearChildren(content);

        // 4) 生成并摆放
        DoBuild(k, names);
    }

    void DoBuild(int k, IList<string> names)
    {
        if (!cardPrefab || !content)
        {
            Debug.LogWarning("[OptionCardsPanelSimple] 缺少 cardPrefab 或 content");
            return;
        }

        // 有无 LayoutGroup？
        bool hasLayout = content.GetComponent<HorizontalOrVerticalLayoutGroup>() != null
                      || content.GetComponent<GridLayoutGroup>() != null;

        // 预制体原始尺寸 & 宽高比
        var prt = cardPrefab.GetComponent<RectTransform>();
        float prefabW = prt ? prt.sizeDelta.x : minCellWidth;
        float prefabH = prt ? prt.sizeDelta.y : minCellHeight;
        float aspect = prefabH > 0 ? (prefabW / prefabH) : 1f;

        // 计算目标尺寸（仅在无 LayoutGroup）
        float cellW = prefabW;
        float cellH = prefabH;

        if (!hasLayout && fitToContentWidth)
        {
            float availableW = content.rect.width - padding.x - padding.z;
            float totalSpacing = spacing * (k - 1);
            cellW = Mathf.Max(minCellWidth, (availableW - totalSpacing) / k);

            if (aspect > 0.01f) cellH = Mathf.Max(minCellHeight, cellW / aspect);
            else cellH = Mathf.Max(minCellHeight, prefabH);
        }

        // 居中起点（仅在无 LayoutGroup）
        float totalW = hasLayout ? 0f : k * cellW + (k - 1) * spacing + padding.x + padding.z;
        float startX = hasLayout ? 0f : -totalW * 0.5f + padding.x + cellW * 0.5f;

        for (int i = 0; i < k; i++)
        {
            var go = Instantiate(cardPrefab, content);
            go.SetActive(true);

            // 写文字（优先 TMP）
            if (!TrySetText(go, names[i]))
            {
                Debug.LogWarning($"[OptionCardsPanelSimple] 找不到文字组件来写入：index={i}, text='{names[i]}'");
            }

            // 摆放 / 尺寸
            var rt = go.GetComponent<RectTransform>();
            if (forceSameSize)
            {
                if (!hasLayout)
                {
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, cellW);
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,   cellH);
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(startX + i * (cellW + spacing), 0f);
                }
            }
            else if (!hasLayout)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                float w = rt.sizeDelta.x > 0 ? rt.sizeDelta.x : cellW;
                float x = startX + i * (w + spacing);
                rt.anchoredPosition = new Vector2(x, 0f);
            }
        }
    }

    /// 尝试为卡片设置文字：先找 TMP，再找 UGUI Text
    bool TrySetText(GameObject go, string text)
    {
        // 若卡片上有 OptionCardItem，优先用它（如果你有这个脚本）
        var oci = go.GetComponent<OptionCardItem>();
        if (oci != null) { oci.SetLabel(text); return true; }

        var tmp = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp) { tmp.text = text; return true; }

        var ugui = go.GetComponentInChildren<Text>(true);
        if (ugui) { ugui.text = text; return true; }

        return false;
    }

    void ClearChildren(RectTransform parent)
    {
        if (!parent) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(parent.GetChild(i).gameObject);
            else Destroy(parent.GetChild(i).gameObject);
#else
            Destroy(parent.GetChild(i).gameObject);
#endif
        }
    }
}
