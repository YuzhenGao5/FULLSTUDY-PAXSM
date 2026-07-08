using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// ReadingMode content binder (REAL-TIME):
/// - Subscribes to ALLCONTROL.OnCurrentQuestionChanged
/// - Reads stem + labels from ctrl.reader.data
/// - Maps to ReadingMode TMP texts
///
/// JSON format:
/// {
///   "scale": 7,
///   "labels": [...],
///   "items": [ { "id": "...", "stem": "..." }, ...]
/// }
/// </summary>
public class ReadingMode_FromALLCONTROL : MonoBehaviour
{
    [Header("Targets on ReadingMode")]
    public TMP_Text questionText;
    public TMP_Text scaleText;

    [Tooltip("显示 '这是第几题' 的 Text")]
    public TMP_Text questionIndexText;

    [Header("Behaviour")]
    [Tooltip("ReadingMode 启用时自动刷新一次")]
    public bool refreshOnEnable = true;

    [Tooltip("考虑 ALLCONTROL/Reader 可能晚一帧初始化")]
    public bool delayOneFrame = true;

    [Tooltip("是否订阅题目切换事件，做到实时更新")]
    public bool subscribeToQuestionChange = true;

    [Tooltip("刻度标签连接符")]
    public string labelJoiner = "  |  ";

    [Header("Index Display")]
    [Tooltip("题号显示格式：{0}=当前题号(从1开始)，{1}=总题数")]
    public string indexFormat = "第 {0} / {1} 题";

    // ---- cached refs ----
    ALLCONTROL _ctrl;

    void OnEnable()
    {
        if (delayOneFrame)
            StartCoroutine(InitNextFrame());
        else
            InitNow();
    }

    IEnumerator InitNextFrame()
    {
        yield return null;
        InitNow();
    }

    void InitNow()
    {
        _ctrl = ALLCONTROL.Instance;

        if (subscribeToQuestionChange && _ctrl != null)
        {
            // 防重复订阅
            _ctrl.OnCurrentQuestionChanged -= HandleQuestionChanged;
            _ctrl.OnCurrentQuestionChanged += HandleQuestionChanged;
        }

        if (refreshOnEnable)
            Refresh();
    }

    void OnDisable()
    {
        if (_ctrl != null)
            _ctrl.OnCurrentQuestionChanged -= HandleQuestionChanged;
    }

    void HandleQuestionChanged(int idx)
    {
        // ✅ 只有 ReadingMode 激活时才需要实时刷新
        if (!gameObject.activeInHierarchy) return;

        Refresh();
    }

    /// <summary>
    /// 你也可以在“Confirm Reading”那一刻手动调用
    /// </summary>
    public void Refresh()
    {
        var ctrl = _ctrl != null ? _ctrl : ALLCONTROL.Instance;
        if (!ctrl)
        {
            Debug.LogWarning("[ReadingMode_FromALLCONTROL] ALLCONTROL.Instance not found.");
            return;
        }

        var reader = ctrl.reader;
        if (!reader || reader.data == null)
        {
            Debug.LogWarning("[ReadingMode_FromALLCONTROL] reader/data not ready.");
            return;
        }

        if (reader.data.items == null || reader.data.items.Count == 0)
        {
            Debug.LogWarning("[ReadingMode_FromALLCONTROL] reader.data.items empty.");
            return;
        }

        int total = reader.data.items.Count;
        int idx = Mathf.Clamp(ctrl.currentIndex, 0, total - 1);
        var item = reader.data.items[idx];

        // ---- stem -> questionText ----
        if (questionText)
        {
            questionText.text = string.IsNullOrEmpty(item.stem)
                ? $"(Missing stem)  id={item.id}"
                : item.stem;
        }

        // ---- labels -> scaleText (optional) ----
        if (scaleText)
        {
            if (reader.data.labels != null && reader.data.labels.Count > 0)
            {
                scaleText.text = string.Join(labelJoiner, reader.data.labels);
            }
            else
            {
                int n = Mathf.Max(0, reader.data.scale);
                if (n > 0)
                {
                    string[] nums = new string[n];
                    for (int i = 0; i < n; i++) nums[i] = (i + 1).ToString();
                    scaleText.text = string.Join(labelJoiner, nums);
                }
                else
                {
                    scaleText.text = "";
                }
            }
        }

        // ---- idx -> questionIndexText ----
        if (questionIndexText)
        {
            int currentHumanIndex = idx + 1; // 从1开始显示
            questionIndexText.text = string.Format(indexFormat, currentHumanIndex, total);
        }

        Debug.Log($"[ReadingMode_FromALLCONTROL] Refreshed reading content idx={idx}, id={item.id}", this);
    }
}
