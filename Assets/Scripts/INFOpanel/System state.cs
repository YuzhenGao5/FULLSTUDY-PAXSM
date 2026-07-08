// 文件名：StageTextFromAllcontrol.cs
using UnityEngine;
using TMPro;

public class StageTextFromAllcontrol : MonoBehaviour
{
    [Header("Text")]
    public TextMeshProUGUI stageText;   // 你的 “Reading” Text

    [Header("Optional: 强制引用 ALLCONTROL（不填就用 ALLCONTROL.Instance）")]
    public ALLCONTROL allcontrol;

    [Header("显示文案")]
    public string readLabel   = "Reading";
    public string answerLabel = "Answer";
    public string submitLabel = "Submit";

    [Header("如果 ALLCONTROL 不存在时的兜底文案")]
    public string fallbackLabel = "—";

    void Reset()
    {
        if (!stageText) stageText = GetComponent<TextMeshProUGUI>();
    }

    void OnEnable()
    {
        if (!stageText) stageText = GetComponent<TextMeshProUGUI>();

        // 订阅（如果现在 Instance 还没起来也没事，Start 会再 Refresh）
        if (ALLCONTROL.Instance != null)
        {
            Bind(ALLCONTROL.Instance);
        }
    }

    void Start()
    {
        // Start 时再尝试绑定一次（解决 ALLCONTROL Awake/Start 顺序问题）
        if (allcontrol == null) allcontrol = ALLCONTROL.Instance;
        if (allcontrol != null) Bind(allcontrol);

        RefreshNow();
    }

    void OnDisable()
    {
        Unbind();
    }

    void Bind(ALLCONTROL ac)
    {
        // 防止重复绑
        Unbind();

        allcontrol = ac;
        allcontrol.OnCurrentQuestionChanged += HandleQuestionChanged;
        allcontrol.OnStageChanged           += HandleStageChanged;
    }

    void Unbind()
    {
        if (allcontrol == null) return;

        allcontrol.OnCurrentQuestionChanged -= HandleQuestionChanged;
        allcontrol.OnStageChanged           -= HandleStageChanged;
    }

    void HandleQuestionChanged(int idx)
    {
        RefreshNow();
    }

    void HandleStageChanged(int idx, ALLCONTROL.QuestionStage stage)
    {
        // 只在当前题变化时刷新（避免别题触发）
        if (allcontrol != null && idx == allcontrol.currentIndex)
            RefreshNow();
    }

    [ContextMenu("RefreshNow")]
    public void RefreshNow()
    {
        if (!stageText) return;

        var ac = (allcontrol != null) ? allcontrol : ALLCONTROL.Instance;
        if (ac == null)
        {
            stageText.text = fallbackLabel;
            return;
        }

        var stage = ac.GetStageForCurrent();

        switch (stage)
        {
            case ALLCONTROL.QuestionStage.Read:
                stageText.text = readLabel;
                break;
            case ALLCONTROL.QuestionStage.Answer:
                stageText.text = answerLabel;
                break;
            case ALLCONTROL.QuestionStage.Submit:
                stageText.text = submitLabel;
                break;
            default:
                stageText.text = fallbackLabel;
                break;
        }
    }
}
