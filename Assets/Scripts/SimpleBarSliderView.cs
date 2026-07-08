using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class SimpleBarSliderView : MonoBehaviour
{
    [Header("Data")]
    public SimpleJsonReader reader;              // 拖：有 SimpleJsonReader 的对象
    [Min(2)] public int fallbackScale = 11;      // 读不到 scale 的兜底

    [Header("Refs")]
    public RectTransform barRect;                // 对齐 Bar，用来算宽度
    public RectTransform tickContainer;          // 刻度线父节点
    public GameObject tickPrefab;                // 小刻度线预制体（Tick.prefab，可设为隐藏）

    public TextMeshProUGUI labelMin;
    public TextMeshProUGUI labelMid;
    public TextMeshProUGUI labelMax;

    [Header("Tick Style")]
    public float minorWidth  = 2.5f;             // 次刻度宽
    public float minorHeight = 10f;              // 次刻度高
    public float majorWidth  = 3.5f;             // 主刻度宽
    public float majorHeight = 20f;              // 主刻度高

    public Color minorColor = new Color(1f, 1f, 1f, 0.5f); // 次刻度淡一点
    public Color majorColor = Color.white;                 // 主刻度亮一点

    // 只读调试
    [SerializeField] int currentScale;
    [SerializeField] List<string> currentLabels;

    void Reset()
    {
        // 自动尝试绑定引用（缺了你也可以手动拖）
        if (!barRect && transform.Find("BarRoot/Bar") is Transform b)
            barRect = b as RectTransform;

        if (!tickContainer && transform.Find("BarRoot/TickContainer") is Transform tc)
            tickContainer = tc as RectTransform;

        if (!labelMin && transform.Find("Labels/Label_Min") is Transform lmin)
            labelMin = lmin.GetComponent<TextMeshProUGUI>();

        if (!labelMid && transform.Find("Labels/Label_Mid") is Transform lmid)
            labelMid = lmid.GetComponent<TextMeshProUGUI>();

        if (!labelMax && transform.Find("Labels/Label_Max") is Transform lmax)
            labelMax = lmax.GetComponent<TextMeshProUGUI>();
    }

    void OnEnable()
    {
        // 只要这个对象被 ModeSwitcher 打开，就初始化一次
        StartCoroutine(InitRoutine());
    }

    IEnumerator InitRoutine()
    {
        // 等一帧让 reader / Layout 准备好
        yield return null;

        var cfg = (reader != null) ? reader.data : null;

        int scale = fallbackScale;
        List<string> labels = null;

        if (cfg != null)
        {
            if (cfg.scale > 1) scale = cfg.scale;
            labels = cfg.labels;
        }
        else
        {
            Debug.LogWarning("[SimpleBarSliderView] reader.data 为空，使用 fallbackScale");
        }

        Init(scale, labels);
    }

    /// <summary>外部也可以手动调用：给一个 scale 和 labels。</summary>
    public void Init(int scale, List<string> labels = null)
    {
        currentScale  = Mathf.Max(2, scale);   // 至少 2 点
        currentLabels = labels;

        BuildTicks();
        SetupLabels();
    }

    void BuildTicks()
    {
        if (!barRect || !tickContainer || !tickPrefab)
        {
            Debug.LogWarning("[SimpleBarSliderView] 缺少引用 barRect/tickContainer/tickPrefab");
            return;
        }

        // 1) 清空旧刻度
        for (int i = tickContainer.childCount - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(tickContainer.GetChild(i).gameObject);
            else Destroy(tickContainer.GetChild(i).gameObject);
#else
            Destroy(tickContainer.GetChild(i).gameObject);
#endif
        }

        // 2) 根据 Bar 的宽度平均铺开
        float width = barRect.rect.width;
        if (width <= 0f)
        {
            Debug.LogWarning("[SimpleBarSliderView] barRect 宽度为 0，检查 RectTransform 尺寸/Canvas 缩放");
            return;
        }

        float halfW   = width * 0.5f;
        int   midIndex = (currentScale - 1) / 2;   // 中间那根的 index

        for (int i = 0; i < currentScale; i++)
        {
            float t = (currentScale <= 1) ? 0f : (float)i / (currentScale - 1);
            float x = Mathf.Lerp(-halfW, halfW, t);

            var tick = Instantiate(tickPrefab, tickContainer);
            // 🔴 关键：不管 tickPrefab 本身是不是隐藏，这里强制打开
            tick.SetActive(true);

            var rt  = tick.GetComponent<RectTransform>();
            var img = tick.GetComponent<Image>();

            rt.localScale       = Vector3.one;
            rt.anchoredPosition = new Vector2(x, 0f);

            bool isMajor = (i == 0) || (i == currentScale - 1) || (i == midIndex);

            if (isMajor)
            {
                rt.sizeDelta = new Vector2(majorWidth, majorHeight);
                if (img) img.color = majorColor;
            }
            else
            {
                rt.sizeDelta = new Vector2(minorWidth, minorHeight);
                if (img) img.color = minorColor;
            }
        }

        Debug.Log($"[SimpleBarSliderView] BuildTicks done: scale={currentScale}, width={width}");
    }

    void SetupLabels()
    {
        int midIndex = (currentScale - 1) / 2;

        if (currentLabels != null && currentLabels.Count > 0)
        {
            string min = currentLabels[0];
            string max = currentLabels[currentLabels.Count - 1];
            string mid;

            if (currentLabels.Count >= 3)
                mid = currentLabels[Mathf.Clamp(midIndex, 0, currentLabels.Count - 1)];
            else
                mid = "Neutral";

            if (labelMin) labelMin.text = min;
            if (labelMax) labelMax.text = max;
            if (labelMid) labelMid.text = mid;
        }
        else
        {
            if (labelMin) labelMin.text = "Low";
            if (labelMax) labelMax.text = "High";
            if (labelMid) labelMid.text = "Neutral";
        }
    }
}
