// 文件名：PagerController.cs
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class PagerController : MonoBehaviour
{
    [Header("Wiring")]
    public Button leftBtn;
    public Button rightBtn;
    public RectTransform pageDotsRoot;  // PageDots
    public GameObject dotPrefab;        // DotTemplate(Disabled)

    [Header("State")]
    [SerializeField] int pageCount = 3;
    [SerializeField] int currentPage = 0;

    readonly List<GameObject> dots = new();

    // 外部订阅：页码改变时回调（参数是 0-based）
    public System.Action<int> OnPageChanged;

    [Header("音效设置")]
    [Tooltip("翻页时播放音效的 AudioSource（可为空，为空则用 PlayClipAtPoint）")]
    public AudioSource audioSource;

    [Tooltip("翻页时播放的音效 Clip")]
    public AudioClip pageTurnClip;

    [Range(0f, 1f)]
    [Tooltip("翻页音效音量")]
    public float pageTurnVolume = 1f;

    void Awake()
    {
        if (leftBtn)  leftBtn.onClick.AddListener(() => Go(-1));
        if (rightBtn) rightBtn.onClick.AddListener(() => Go(+1));
        RebuildDots();
        UpdateUI();
    }

    /// <summary>外部设置总页数，比如 items.Count。</summary>
    public void SetPageCount(int count)
    {
        pageCount   = Mathf.Max(1, count);
        currentPage = Mathf.Clamp(currentPage, 0, pageCount - 1);
        RebuildDots();
        UpdateUI();
    }

    /// <summary>外部直接切到某一页（0-based）。</summary>
    public void SetPage(int idx)
    {
        int newPage = Mathf.Clamp(idx, 0, pageCount - 1);

        // 如果页码没有变化，就不播放音效，也不触发回调
        if (newPage == currentPage)
        {
            UpdateUI();
            return;
        }

        currentPage = newPage;
        UpdateUI();
        PlayPageTurnSfx();
        OnPageChanged?.Invoke(currentPage);
    }

    /// <summary>
    /// 语义清晰一点的封装：根据“题目编号”跳转。
    /// 实际上就是调用 SetPage。
    /// </summary>
    public void JumpToQuestion(int questionIndex)
    {
        SetPage(questionIndex);
    }

    /// <summary>
    /// 🔹 给外部用的翻页接口
    /// delta = -1 / +1（上一页 / 下一页）
    /// 你在 KnobModeManager 里就用这个。
    /// </summary>
    public void StepPage(int delta)
    {
        SetPage(currentPage + delta);
    }

    // 内部用的小工具
    void Go(int delta) => SetPage(currentPage + delta);

    void RebuildDots()
    {
        foreach (Transform c in pageDotsRoot)
            Destroy(c.gameObject);

        dots.Clear();

        for (int i = 0; i < pageCount; i++)
        {
            var go = Instantiate(dotPrefab, pageDotsRoot);
            go.SetActive(true);

            int idx = i;
            var btn = go.GetComponent<Button>();
            if (btn) btn.onClick.AddListener(() => SetPage(idx));

            dots.Add(go);
        }
    }

    void UpdateUI()
    {
        if (leftBtn)  leftBtn.interactable  = currentPage > 0;
        if (rightBtn) rightBtn.interactable = currentPage < pageCount - 1;

        for (int i = 0; i < dots.Count; i++)
        {
            var t = dots[i].transform as RectTransform;
            float s = (i == currentPage) ? 1.2f : 1.0f; // 当前页放大
            if (t) t.localScale = Vector3.one * s;
        }
    }

    /// <summary>实际播放翻页音效。</summary>
    void PlayPageTurnSfx()
    {
        if (!pageTurnClip) return;

        if (audioSource)
        {
            audioSource.PlayOneShot(pageTurnClip, pageTurnVolume);
        }
        else
        {
            // 没有指定 AudioSource，就在当前物体位置播放一次
            AudioSource.PlayClipAtPoint(pageTurnClip, transform.position, pageTurnVolume);
        }
    }
}
