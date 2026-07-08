// 文件名：LeftPanelController.cs
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 左侧进度面板：
/// - Num_QS 显示总题目数量
/// - Process 显示当前题号
/// - Container 里自动生成按钮，点击可跳转到指定题目
/// - 自动监听 ALLCONTROL 的当前题目变化 / 作答变化
/// - 点击按钮时调用 PagerController 直接跳页
///
/// ✅ FIX(跳题版)：
/// 1) 按钮点击不再重复 SetCurrentQuestion（避免多次触发导致刷新顺序混乱）
/// 2) LeftPanel 自己不再“主动刷新当前题 UI”，而是依赖 ALLCONTROL 事件统一刷新
/// 3) 可选防抖：点击同一题不重复触发跳题
/// </summary>
public class LeftPanelController : MonoBehaviour
{
    [Header("文字显示")]
    [Tooltip("显示总题目数量，例如 10")]
    public TextMeshProUGUI numQsLabel;      // 对应 Num_QS

    [Tooltip("显示当前题号，例如 3")]
    public TextMeshProUGUI processLabel;    // 对应 Process

    [Header("按钮生成")]
    [Tooltip("生成题目跳转按钮的父物体（Container）")]
    public Transform buttonsParent;         // 对应 Container

    [Tooltip("题目按钮预制体（Button + Text）")]
    public GameObject questionButtonPrefab;

    [Header("按钮颜色（可选）")]
    public Color normalColor   = new Color(1f, 1f, 1f, 0.2f);
    public Color currentColor  = new Color(0.2f, 0.8f, 1f, 0.9f);
    public Color answeredColor = new Color(0.2f, 1f, 0.4f, 0.9f);

    [Header("Pager")]
    [Tooltip("负责翻页的 PagerController，LeftPanel 点击时会调用它的 JumpToQuestion / SetPage")]
    public PagerController pager;

    [Header("Behavior")]
    [Tooltip("点击当前题时是否忽略（防止重复触发一堆刷新）")]
    public bool ignoreClickOnCurrent = true;

    int totalQuestions = 0;
    int currentIndex   = -1;     // 当前题（0-based）
    bool[] answered;             // 是否答完

    // 按钮点击后调用的回调（这里会指向 OnLeftButtonJump）
    Action<int> onJumpToQuestion;

    bool _subscribed = false;

    void Start()
    {
        Debug.Log("[LeftPanelController] Start() 运行，启动 InitRoutine 等待 ALLCONTROL 准备好");
        StartCoroutine(InitRoutine());
    }

    System.Collections.IEnumerator InitRoutine()
    {
        // 等待 ALLCONTROL 和题目列表准备好
        ALLCONTROL ac = null;

        while (true)
        {
            ac = ALLCONTROL.Instance;

            if (ac != null && ac.TotalQuestions > 0)
            {
                Debug.Log($"[LeftPanelController] InitRoutine：找到 ALLCONTROL，TotalQuestions = {ac.TotalQuestions}");
                break;
            }

            yield return null; // 下一帧再检查
        }

        totalQuestions = ac.TotalQuestions;
        if (totalQuestions <= 0)
        {
            Debug.LogWarning("[LeftPanelController] InitRoutine：totalQuestions 仍然 <= 0，放弃初始化");
            yield break;
        }

        if (!numQsLabel)    Debug.LogWarning("[LeftPanelController] numQsLabel 没有拖引用");
        if (!processLabel)  Debug.LogWarning("[LeftPanelController] processLabel 没有拖引用");
        if (!buttonsParent) Debug.LogWarning("[LeftPanelController] buttonsParent(Container) 没有拖引用");
        if (!questionButtonPrefab) Debug.LogWarning("[LeftPanelController] questionButtonPrefab 没有拖引用");
        if (!pager) Debug.LogWarning("[LeftPanelController] pager 没有拖引用（点击按钮不会驱动 Pager 翻页）");

        // ① 初始化：根据 ALLCONTROL 的总题数生成按钮
        Init(totalQuestions, OnLeftButtonJump);

        // ② 根据当前答案表，把已作答的按钮先标绿
        for (int i = 0; i < totalQuestions; i++)
        {
            if (ac.IsAnswered(i))
            {
                SetAnswered(i, true);
            }
        }

        // ③ 当前题号同步
        SetCurrentQuestion(ac.currentIndex);

        // ④ 订阅 ALLCONTROL 的事件（只订一次）
        Subscribe(ac);
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void Subscribe(ALLCONTROL ac)
    {
        if (_subscribed) return;
        if (ac == null) return;

        ac.OnCurrentQuestionChanged += HandleCurrentQuestionChanged;
        ac.OnAnswerChanged          += HandleAnswerChanged;

        _subscribed = true;
        Debug.Log("[LeftPanelController] ✅ 已订阅 ALLCONTROL 事件");
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;

        var ac = ALLCONTROL.Instance;
        if (ac != null)
        {
            ac.OnCurrentQuestionChanged -= HandleCurrentQuestionChanged;
            ac.OnAnswerChanged          -= HandleAnswerChanged;
        }

        _subscribed = false;
        Debug.Log("[LeftPanelController] ✅ 已取消订阅 ALLCONTROL 事件");
    }

    // ====== 对 ALLCONTROL 事件的响应 ======

    void HandleCurrentQuestionChanged(int index)
    {
        // 只更新 UI，不做跳题动作
        SetCurrentQuestion(index);
    }

    void HandleAnswerChanged(int index, bool hasAnswer)
    {
        SetAnswered(index, hasAnswer);
    }

    // ====== 初始化与按钮创建 ======

    /// <summary>
    /// 初始化：传入总题目数和按钮点击时的回调
    /// </summary>
    public void Init(int total, Action<int> jumpCallback)
    {
        totalQuestions   = Mathf.Max(0, total);
        onJumpToQuestion = jumpCallback;

        if (totalQuestions <= 0)
        {
            Debug.LogWarning("[LeftPanelController] Init：totalQuestions <= 0");
            return;
        }

        answered = new bool[totalQuestions];

        // 更新总题目数量
        if (numQsLabel)
            numQsLabel.text = totalQuestions.ToString();

        // 清空旧按钮
        if (buttonsParent)
        {
            for (int i = buttonsParent.childCount - 1; i >= 0; i--)
            {
                Destroy(buttonsParent.GetChild(i).gameObject);
            }
        }

        // 生成新按钮
        for (int i = 0; i < totalQuestions; i++)
        {
            CreateQuestionButton(i);
        }

        // 默认显示第 1 题（只是 UI）
        SetCurrentQuestion(0);
    }

    /// <summary>
    /// 创建单个题目按钮
    /// </summary>
    void CreateQuestionButton(int index)
    {
        if (!buttonsParent || !questionButtonPrefab) return;

        GameObject go = Instantiate(questionButtonPrefab, buttonsParent);
        go.name = $"QButton_{index + 1}";

        // 显示数字
        var text = go.GetComponentInChildren<TextMeshProUGUI>();
        if (text)
            text.text = (index + 1).ToString();

        // 注册点击事件
        var btn = go.GetComponent<Button>();
        if (btn)
        {
            int capturedIndex = index;   // 闭包
            btn.onClick.AddListener(() =>
            {
                // ✅ 防抖：点当前题就不做任何跳题（可关）
                if (ignoreClickOnCurrent && capturedIndex == currentIndex)
                {
                    Debug.Log($"[LeftPanelController] 点击当前题 {capturedIndex} -> 忽略（ignoreClickOnCurrent=true）");
                    return;
                }

                Debug.Log($"[LeftPanelController] 按钮点击：请求跳转到题 {capturedIndex}");
                onJumpToQuestion?.Invoke(capturedIndex);
                // ❌ 不在这里 SetCurrentQuestion(capturedIndex)
                // ✅ 等 ALLCONTROL.OnCurrentQuestionChanged 事件回来再刷新 UI
            });
        }

        // 初始颜色
        UpdateButtonVisual(index, go);
    }

    // ====== 左侧按钮点击后的逻辑 ======

    /// <summary>
    /// 左侧某个题目按钮被点击时调用
    /// </summary>
    void OnLeftButtonJump(int index)
    {
        var ac = ALLCONTROL.Instance;

        // 1）更新全局当前题（唯一真相）
        if (ac != null)
        {
            ac.SetCurrentQuestion(index);
        }

        // 2）让 Pager 真正翻到对应页（= 对应题）
        if (pager != null)
        {
            pager.JumpToQuestion(index);
            // 等价：pager.SetPage(index);
        }

        // 3）不在这里刷新 LeftPanel UI，等事件回调 HandleCurrentQuestionChanged
    }

    // ====== 对外接口：设置当前题 / 标记作答 ======

    /// <summary>
    /// 更新当前题号（外部翻页时也要调；同时会更新 Process 文本 + 按钮高亮）
    /// </summary>
    public void SetCurrentQuestion(int index)
    {
        if (totalQuestions <= 0) return;
        currentIndex = Mathf.Clamp(index, 0, totalQuestions - 1);

        if (processLabel)
            processLabel.text = (currentIndex + 1).ToString(); // 显示 1-based

        RefreshAllButtons();
    }

    /// <summary>
    /// 标记某题已作答（选完一个选项时从外部调用）
    /// </summary>
    public void SetAnswered(int index, bool isAnswered)
    {
        if (index < 0 || index >= totalQuestions) return;
        if (answered == null || answered.Length != totalQuestions)
        {
            answered = new bool[totalQuestions];
        }

        answered[index] = isAnswered;
        RefreshAllButtons();
    }

    // ====== 内部：刷新按钮视觉 ======

    void RefreshAllButtons()
    {
        if (!buttonsParent) return;

        int childCount = buttonsParent.childCount;
        int n = Mathf.Min(childCount, totalQuestions);

        for (int i = 0; i < n; i++)
        {
            var child = buttonsParent.GetChild(i).gameObject;
            UpdateButtonVisual(i, child);
        }
    }

    void UpdateButtonVisual(int index, GameObject buttonGO)
    {
        var img = buttonGO.GetComponent<Image>();
        if (!img) return;

        Color c;
        if (index == currentIndex)
        {
            c = currentColor;          // 当前题：高亮色
        }
        else if (answered != null && index < answered.Length && answered[index])
        {
            c = answeredColor;         // 已作答：绿色
        }
        else
        {
            c = normalColor;           // 未作答：半透明
        }

        img.color = c;
    }
}
