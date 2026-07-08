// 文件名：StageAwarePagerSubmitButton.cs
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(Button))]
public class StageAwarePagerSubmitButton : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Refs")]
    public ALLCONTROL allcontrol;            // 可不填，用 ALLCONTROL.Instance
    public Button button;                    // 根 Button
    public TextMeshProUGUI label;            // 按钮文字（Submit!/Pager）

    [Header("Pager Panel (Read stage click)")]
    public GameObject pagerPanel;            // 点击 Pager 时显示/隐藏的面板
    public bool autoHidePagerWhenLeaveRead = true;

    [Header("Labels")]
    public string readLabel = "Pager";
    public string answerLabel = "Submit!";
    public string submitLabel = "Submit!";

    // ✅ Finished 阶段显示
    public string finishedLabel = "redo!";

    [Header("Finished Redo Behavior")]
    [Tooltip("✅ true: Finished 阶段使用长按触发 redo（和 Answer/Submit 一样）；false: 仍然 click 触发 redo")]
    public bool redoUseHold = true;

    [Header("Hold Submit (Answer/Submit stage)")]
    [Tooltip("长按多久算提交成功（秒）")]
    public float holdDuration = 0.8f;

    [Tooltip("Read 阶段是否允许长按提交（一般 false）")]
    public bool allowHoldInRead = false;

    [Tooltip("提交成功后是否自动复位视觉（一般 false，让外部接事件后决定下一步）")]
    public bool autoResetAfterConfirm = false;

    [Header("✅ Visual Reset Policy")]
    [Tooltip("✅ 修复你说的问题：Answer 确认后如果不切 stage，按钮会卡在 confirmed。这里让它自动下一帧复位。")]
    public bool autoResetVisualAfterAnswerConfirm = true;

    [Header("Visual (Optional)")]
    [Tooltip("可选：进度环 Image（Image Type 设为 Filled）")]
    public Image holdFillImage;

    [Tooltip("可选：按钮背景 Image（用于颜色渐变）")]
    public Image bg;

    [Tooltip("长按时是否做缩放动画")]
    public bool useScale = true;

    public float normalScale = 1f;
    public float holdingScale = 0.97f;

    [Tooltip("颜色（不填就不改颜色）")]
    public bool useColor = false;
    public Color normalColor = Color.white;
    public Color holdingColor = Color.white;
    public Color confirmedColor = Color.white;

    // ===================== Events =====================

    [Header("Pager Events (Read stage)")]
    public UnityEvent OnPagerOpened;
    public UnityEvent OnPagerClosed;

    [System.Serializable] public class FloatEvent : UnityEvent<float> { }

    [Header("Generic Hold Events (Back-compat)")]
    public FloatEvent OnHoldProgress;     // 0..1
    public UnityEvent OnHoldCanceled;     // 没按够松开
    public UnityEvent OnSubmitConfirmed;  // 按够了，提交成功

    [Header("Extra Events (Requested, Back-compat)")]
    public UnityEvent OnSelecting;        // 按下开始（一次）
    public FloatEvent OnHolding;          // 长按中（连续 0..1）
    public UnityEvent OnSubmitConfirm;    // 长按成功（一次）

    [Header("Stage-specific Events (New)")]
    public UnityEvent OnReadPointerDown;
    public UnityEvent OnReadPointerUp;
    public UnityEvent OnReadClick;             // Read 阶段 click（Pager toggle 前/后都行）

    public UnityEvent OnAnswerSelecting;        // Answer 阶段按下开始
    public FloatEvent OnAnswerHolding;          // Answer 阶段长按中
    public UnityEvent OnAnswerHoldCanceled;     // Answer 阶段取消
    public UnityEvent OnAnswerHoldConfirmed;    // Answer 阶段成功

    public UnityEvent OnSubmitSelecting;        // Submit 阶段按下开始
    public FloatEvent OnSubmitHolding;          // Submit 阶段长按中
    public UnityEvent OnSubmitHoldCanceled;     // Submit 阶段取消
    public UnityEvent OnSubmitHoldConfirmed;    // Submit 阶段成功

    // ✅ NEW：Finished 阶段也给一套（redo 用）
    public UnityEvent OnFinishedSelecting;        // Finished 阶段按下开始
    public FloatEvent OnFinishedHolding;          // Finished 阶段长按中
    public UnityEvent OnFinishedHoldCanceled;     // Finished 阶段取消
    public UnityEvent OnFinishedHoldConfirmed;    // Finished 阶段成功（redo 触发前/后你都能接）

    // ===================== internal =====================
    Coroutine _holdCo;
    Coroutine _resetVisualCo;

    bool _isHolding = false;
    bool _confirmed = false;
    bool _pointerDown = false;

    ALLCONTROL.QuestionStage _holdStage = ALLCONTROL.QuestionStage.Read;

    void Reset()
    {
        button = GetComponent<Button>();
        if (!label) label = GetComponentInChildren<TextMeshProUGUI>(true);
        if (!bg) bg = GetComponent<Image>();
    }

    void Awake()
    {
        if (!button) button = GetComponent<Button>();
        if (!label) label = GetComponentInChildren<TextMeshProUGUI>(true);

        button.onClick.RemoveListener(HandleClick);
        button.onClick.AddListener(HandleClick);

        ApplyVisualInstant(0f, holding: false, confirmed: false);
    }

    void OnEnable()
    {
        if (allcontrol == null) allcontrol = ALLCONTROL.Instance;
        if (allcontrol != null)
        {
            allcontrol.OnCurrentQuestionChanged += HandleQuestionChanged;
            allcontrol.OnStageChanged += HandleStageChanged;
        }
        RefreshFromStage();
    }

    void OnDisable()
    {
        StopHoldCoroutine();
        StopResetVisualCoroutine();

        if (allcontrol != null)
        {
            allcontrol.OnCurrentQuestionChanged -= HandleQuestionChanged;
            allcontrol.OnStageChanged -= HandleStageChanged;
        }
    }

    void HandleQuestionChanged(int idx) => RefreshFromStage();

    void HandleStageChanged(int idx, ALLCONTROL.QuestionStage stage)
    {
        if (allcontrol != null && idx == allcontrol.currentIndex)
            RefreshFromStage();
    }

    void RefreshFromStage()
    {
        var ac = allcontrol != null ? allcontrol : ALLCONTROL.Instance;
        if (ac == null)
        {
            if (label) label.text = "—";
            SafeHidePager();
            return;
        }

        var st = ac.GetStageForCurrent();

        if (autoHidePagerWhenLeaveRead && st != ALLCONTROL.QuestionStage.Read)
            SafeHidePager();

        if (label)
        {
            if (st == ALLCONTROL.QuestionStage.Read) label.text = readLabel;
            else if (st == ALLCONTROL.QuestionStage.Answer) label.text = answerLabel;
            else if (st == ALLCONTROL.QuestionStage.Submit) label.text = submitLabel;
            else if (st == ALLCONTROL.QuestionStage.Finished) label.text = finishedLabel;
            else label.text = submitLabel;
        }

        CancelHoldVisualOnly();
    }

    void HandleClick()
    {
        var ac = allcontrol != null ? allcontrol : ALLCONTROL.Instance;
        if (ac == null) return;

        var st = ac.GetStageForCurrent();

        if (st == ALLCONTROL.QuestionStage.Read)
        {
            OnReadClick?.Invoke();
            TogglePager();
            return;
        }

        // ✅ Finished：如果 redoUseHold=true，则 click 不做事（避免误触），redo 走长按
        if (st == ALLCONTROL.QuestionStage.Finished)
        {
            if (!redoUseHold)
            {
                ac.RedoCurrentQuestionToRead(logEvent: true);
            }
            return;
        }

        // Answer/Submit: click 默认不做事（避免误触提交）
    }

    void TogglePager()
    {
        if (!pagerPanel) return;

        bool newOn = !pagerPanel.activeSelf;
        pagerPanel.SetActive(newOn);

        if (newOn) OnPagerOpened?.Invoke();
        else OnPagerClosed?.Invoke();
    }

    void SafeHidePager()
    {
        if (!pagerPanel) return;
        if (pagerPanel.activeSelf)
        {
            pagerPanel.SetActive(false);
            OnPagerClosed?.Invoke();
        }
    }

    // ===================== Pointer / Hold =====================

    public void OnPointerDown(PointerEventData eventData)
    {
        _pointerDown = true;

        var ac = allcontrol != null ? allcontrol : ALLCONTROL.Instance;
        if (ac == null) return;

        var st = ac.GetStageForCurrent();

        if (st == ALLCONTROL.QuestionStage.Read) OnReadPointerDown?.Invoke();

        bool canHold =
            (st == ALLCONTROL.QuestionStage.Answer || st == ALLCONTROL.QuestionStage.Submit) ||
            (allowHoldInRead && st == ALLCONTROL.QuestionStage.Read) ||
            (redoUseHold && st == ALLCONTROL.QuestionStage.Finished);

        if (!canHold) return;

        _holdStage = st;

        // 兼容事件（旧）
        OnSelecting?.Invoke();

        // 分流 Selecting
        if (_holdStage == ALLCONTROL.QuestionStage.Answer) OnAnswerSelecting?.Invoke();
        else if (_holdStage == ALLCONTROL.QuestionStage.Submit) OnSubmitSelecting?.Invoke();
        else if (_holdStage == ALLCONTROL.QuestionStage.Finished) OnFinishedSelecting?.Invoke();

        StartHold();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _pointerDown = false;

        var ac = allcontrol != null ? allcontrol : ALLCONTROL.Instance;
        if (ac != null && ac.GetStageForCurrent() == ALLCONTROL.QuestionStage.Read)
            OnReadPointerUp?.Invoke();

        if (_isHolding && !_confirmed)
        {
            StopHoldCoroutine();
            ApplyVisualInstant(0f, holding: false, confirmed: false);

            // 兼容事件（旧）
            OnHoldCanceled?.Invoke();

            // 分流 Cancel
            if (_holdStage == ALLCONTROL.QuestionStage.Answer) OnAnswerHoldCanceled?.Invoke();
            else if (_holdStage == ALLCONTROL.QuestionStage.Submit) OnSubmitHoldCanceled?.Invoke();
            else if (_holdStage == ALLCONTROL.QuestionStage.Finished) OnFinishedHoldCanceled?.Invoke();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_pointerDown)
        {
            _pointerDown = false;

            if (_isHolding && !_confirmed)
            {
                StopHoldCoroutine();
                ApplyVisualInstant(0f, holding: false, confirmed: false);

                // 兼容事件（旧）
                OnHoldCanceled?.Invoke();

                // 分流 Cancel
                if (_holdStage == ALLCONTROL.QuestionStage.Answer) OnAnswerHoldCanceled?.Invoke();
                else if (_holdStage == ALLCONTROL.QuestionStage.Submit) OnSubmitHoldCanceled?.Invoke();
                else if (_holdStage == ALLCONTROL.QuestionStage.Finished) OnFinishedHoldCanceled?.Invoke();
            }
        }
    }

    void StartHold()
    {
        StopHoldCoroutine();
        StopResetVisualCoroutine();

        _isHolding = true;
        _confirmed = false;
        _holdCo = StartCoroutine(HoldRoutine());
    }

    IEnumerator HoldRoutine()
    {
        float t = 0f;

        while (t < holdDuration)
        {
            if (!_pointerDown) yield break;

            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / holdDuration);

            ApplyVisualInstant(p, holding: true, confirmed: false);

            // 兼容事件（旧）
            OnHoldProgress?.Invoke(p);
            OnHolding?.Invoke(p);

            // 分流 Holding
            if (_holdStage == ALLCONTROL.QuestionStage.Answer) OnAnswerHolding?.Invoke(p);
            else if (_holdStage == ALLCONTROL.QuestionStage.Submit) OnSubmitHolding?.Invoke(p);
            else if (_holdStage == ALLCONTROL.QuestionStage.Finished) OnFinishedHolding?.Invoke(p);

            yield return null;
        }

        _confirmed = true;
        ApplyVisualInstant(1f, holding: false, confirmed: true);

        // 兼容事件（旧）
        OnSubmitConfirmed?.Invoke();
        OnSubmitConfirm?.Invoke();

        // 分流 Confirm（先发事件，再做动作）
        if (_holdStage == ALLCONTROL.QuestionStage.Answer) OnAnswerHoldConfirmed?.Invoke();
        else if (_holdStage == ALLCONTROL.QuestionStage.Submit) OnSubmitHoldConfirmed?.Invoke();
        else if (_holdStage == ALLCONTROL.QuestionStage.Finished) OnFinishedHoldConfirmed?.Invoke();

        // ✅ Finished 的“redo”在长按确认时触发（和 Answer/Submit 一样）
        if (_holdStage == ALLCONTROL.QuestionStage.Finished)
        {
            var ac = allcontrol != null ? allcontrol : ALLCONTROL.Instance;
            if (ac != null) ac.RedoCurrentQuestionToRead(logEvent: true);
        }

        // ✅ 你原本的全局 autoReset（保持不变）
        if (autoResetAfterConfirm)
        {
            yield return null;
            ApplyVisualInstant(0f, holding: false, confirmed: false);
        }
        else
        {
            // ✅ 修复问题 1：Answer confirm 后自动复位（下一帧），避免卡在 confirmedColor
            if (_holdStage == ALLCONTROL.QuestionStage.Answer && autoResetVisualAfterAnswerConfirm)
            {
                StartResetVisualNextFrame();
            }
        }

        StopHoldCoroutine();
    }

    void StopHoldCoroutine()
    {
        if (_holdCo != null)
        {
            StopCoroutine(_holdCo);
            _holdCo = null;
        }
        _isHolding = false;
        _confirmed = false;
    }

    void CancelHoldVisualOnly()
    {
        if (_holdCo != null)
        {
            StopCoroutine(_holdCo);
            _holdCo = null;
        }
        _isHolding = false;
        _confirmed = false;
        ApplyVisualInstant(0f, holding: false, confirmed: false);
    }

    void StartResetVisualNextFrame()
    {
        StopResetVisualCoroutine();
        _resetVisualCo = StartCoroutine(CoResetVisualNextFrame());
    }

    IEnumerator CoResetVisualNextFrame()
    {
        yield return null; // ✅ 保留一帧 confirmed，让外部 listener 先吃到“确认态”
        ApplyVisualInstant(0f, holding: false, confirmed: false);
        _resetVisualCo = null;
    }

    void StopResetVisualCoroutine()
    {
        if (_resetVisualCo != null)
        {
            StopCoroutine(_resetVisualCo);
            _resetVisualCo = null;
        }
    }

    void ApplyVisualInstant(float progress01, bool holding, bool confirmed)
    {
        if (holdFillImage)
        {
            holdFillImage.enabled = holding;
            holdFillImage.fillAmount = progress01;
        }

        if (useScale)
        {
            float s = holding ? Mathf.Lerp(normalScale, holdingScale, progress01) : normalScale;
            transform.localScale = Vector3.one * s;
        }

        if (useColor && bg)
        {
            if (confirmed) bg.color = confirmedColor;
            else if (holding) bg.color = Color.Lerp(normalColor, holdingColor, progress01);
            else bg.color = normalColor;
        }
    }

    [ContextMenu("ForceRefreshNow")]
    public void ForceRefreshNow() => RefreshFromStage();

    [ContextMenu("HidePagerPanel")]
    public void HidePagerPanel() => SafeHidePager();
}
