// 文件名：PanelAnswerRevealAndChoose.cs
using System.Collections;
using UnityEngine;

public class PanelAnswerRevealAndChoose : MonoBehaviour
{
    [Header("一开始隐藏的两个物体（已经在场景里）")]
    public GameObject optionA;
    public GameObject optionB;

    [Header("后退设置")]
    [Tooltip("被选中的物体沿自身 forward 的反方向后退多少米")]
    public float moveBackDistance = 0.2f;

    [Tooltip("Start 时自动把两个物体隐藏（第一次抓旋钮才显示）")]
    public bool hideOnStart = true;

    // ====== 监听旋钮抓取 ======
    [Header("Knob 抓取监听（Answer Mode 触发源）")]
    [Tooltip("拖 KnobGrabByWrist 进来")]
    public KnobGrabByWrist knobGrab;

    // ====== 平滑后退的目标 ======
    [Header("平滑后退的目标（场景里的物体）")]
    [Tooltip("拖一个已经在场景里的对象（例如你的 panel root / table / 或需要后退的那块）")]
    public GameObject smoothBackTarget;

    [Tooltip("平滑后退用时（秒）")]
    public float smoothMoveDuration = 0.4f;

    Coroutine _smoothMoveCo;
    bool _smoothOriginalSaved = false;
    Vector3 _smoothOriginalPos;

    bool _answerActivatedOnce = false; // ✅ 关键：进入一次 Answer 后就保持
    bool _hasChosen = false;

    // ====== ExitAnswerMode 默认行为 ======
    [Header("Exit Answer Mode defaults")]
    [Tooltip("退出 Answer 时是否把 ALLCONTROL 的阶段写回 Read")]
    public bool exitRevertStageToRead = true;

    [Tooltip("退出 Answer 时是否隐藏 A/B 选项")]
    public bool exitHideOptions = true;

    [Tooltip("退出 Answer 时是否把 smoothBackTarget 位置恢复到原位")]
    public bool exitRestorePosition = true;

    void Start()
    {
        if (hideOnStart)
        {
            if (optionA) optionA.SetActive(false);
            if (optionB) optionB.SetActive(false);
        }

        // 如果脚本启用较晚，但当下已经抓住旋钮，则立刻进入 Answer
        if (knobGrab != null && knobGrab.IsGrabbing)
        {
            EnterAnswerMode();
        }
    }

    void OnEnable()
    {
        if (knobGrab != null)
        {
            knobGrab.OnGrabStart.AddListener(HandleGrabStart);
            knobGrab.OnGrabEnd.AddListener(HandleGrabEnd);
        }
        else
        {
            Debug.LogWarning("[PanelAnswerRevealAndChoose] knobGrab 未设置，无法监听抓取。", this);
        }
    }

    void OnDisable()
    {
        if (knobGrab != null)
        {
            knobGrab.OnGrabStart.RemoveListener(HandleGrabStart);
            knobGrab.OnGrabEnd.RemoveListener(HandleGrabEnd);
        }
    }

    void HandleGrabStart()
    {
        Debug.Log("[PanelAnswerRevealAndChoose] Knob Grab START → Enter Answer (sticky).", this);
        EnterAnswerMode();
    }

    void HandleGrabEnd()
    {
        // ✅ 松手不回弹，不隐藏，不恢复位置
        Debug.Log("[PanelAnswerRevealAndChoose] Knob Grab END → stay in Answer (no revert).", this);
    }

    // ================== Answer 模式（进入一次后常驻） ==================

    public void EnterAnswerMode()
    {
        if (_answerActivatedOnce) return;
        _answerActivatedOnce = true;

        _hasChosen = false;

        // ✅ 写入每题阶段：Answer
        if (ALLCONTROL.Instance != null)
        {
            ALLCONTROL.Instance.SetStageForCurrent(ALLCONTROL.QuestionStage.Answer, force: true, logEvent: true);
            ALLCONTROL.Instance.Record("EnterAnswerMode");
        }
        else
        {
            Debug.LogWarning("[PanelAnswerRevealAndChoose] ALLCONTROL.Instance is null, stage not recorded.", this);
        }

        if (optionA) optionA.SetActive(true);
        if (optionB) optionB.SetActive(true);

        StartSmoothBack();

        Debug.Log("[PanelAnswerRevealAndChoose] Answer ON → options shown & smooth back.", this);
    }

        public void EnterFinishedAAnswerMode()
    {
        if (_answerActivatedOnce) return;
        _answerActivatedOnce = true;

        _hasChosen = false;

        if (optionA) optionA.SetActive(true);
        if (optionB) optionB.SetActive(true);

        StartSmoothBack();

        Debug.Log("[PanelAnswerRevealAndChoose] Answer ON → options shown & smooth back.", this);
    }

    /// <summary>
    /// 关闭 Answer stage（可从别的脚本调用）
    /// - 如果当前根本没进入过 Answer：不会报错，只会 log，然后 return
    /// - 如果 ALLCONTROL 不存在：不会报错，只是跳过写 stage
    /// </summary>
    public void ExitAnswerMode(
        bool? revertStageToRead = null,
        bool? restorePosition = null,
        bool? hideOptions = null
    )
    {
        // ✅ 情况1：Answer 根本没开启过 —— 直接安全退出（不报错）
        if (!_answerActivatedOnce)
        {
            Debug.Log("[PanelAnswerRevealAndChoose] ExitAnswerMode called but Answer was not active. (no-op)", this);
            return;
        }

        bool doRevertStage = revertStageToRead ?? exitRevertStageToRead;
        bool doRestorePos  = restorePosition   ?? exitRestorePosition;
        bool doHide        = hideOptions       ?? exitHideOptions;

        // ✅ 写回 stage（如果系统存在）
        if (doRevertStage && ALLCONTROL.Instance != null)
        {
            ALLCONTROL.Instance.SetStageForCurrent(ALLCONTROL.QuestionStage.Read, force: true, logEvent: true);
            ALLCONTROL.Instance.Record("ExitAnswerMode");
        }

        // ✅ 隐藏选项
        if (doHide)
        {
            if (optionA) optionA.SetActive(false);
            if (optionB) optionB.SetActive(false);
        }

        // ✅ 停止平滑移动协程
        if (_smoothMoveCo != null)
        {
            StopCoroutine(_smoothMoveCo);
            _smoothMoveCo = null;
        }

        // ✅ 位置回弹（如果你之前保存过原位）
        if (doRestorePos && smoothBackTarget != null && _smoothOriginalSaved)
        {
            smoothBackTarget.transform.position = _smoothOriginalPos;
        }

        // ✅ 复位内部状态（让下次还能再进 Answer）
        _answerActivatedOnce = false;
        _hasChosen = false;

        Debug.Log("[PanelAnswerRevealAndChoose] ExitAnswerMode done.", this);
    }

    // ================== 选项选择（你原来的逻辑保留） ==================

    public void OnOptionAChosen() => MoveBack(optionA);
    public void OnOptionBChosen() => MoveBack(optionB);

    void MoveBack(GameObject target)
    {
        if (_hasChosen) return; // 只允许选一次
        if (!target) return;

        _hasChosen = true;

        Transform t = target.transform;
        t.position -= t.forward * moveBackDistance;

        Debug.Log("[PanelAnswerRevealAndChoose] Chosen object moved back: " + target.name, this);
    }

    // ================== 平滑后退 ==================

    void StartSmoothBack()
    {
        if (smoothBackTarget == null) return;

        var t = smoothBackTarget.transform;

        // 第一次保存原始位置，避免多次触发导致越退越远
        if (!_smoothOriginalSaved)
        {
            _smoothOriginalPos = t.position;
            _smoothOriginalSaved = true;
        }

        if (_smoothMoveCo != null) StopCoroutine(_smoothMoveCo);
        _smoothMoveCo = StartCoroutine(MoveBackSmooth(t));
    }

    IEnumerator MoveBackSmooth(Transform target)
    {
        if (target == null) yield break;

        Vector3 startPos = _smoothOriginalPos;
        Vector3 endPos = startPos - target.forward * moveBackDistance;

        float timer = 0f;
        while (timer < smoothMoveDuration)
        {
            timer += Time.deltaTime;
            float u = Mathf.Clamp01(timer / smoothMoveDuration);

            // easeInOut
            float eased = u * u * (3f - 2f * u);

            target.position = Vector3.Lerp(startPos, endPos, eased);
            yield return null;
        }

        target.position = endPos;
        _smoothMoveCo = null;
    }

    // ================== 可选：你想“提交成功后再回弹”就调用这个 ==================

    [ContextMenu("ResetToInitial")]
    public void ResetToInitial()
    {
        _answerActivatedOnce = false;
        _hasChosen = false;

        if (optionA) optionA.SetActive(false);
        if (optionB) optionB.SetActive(false);

        if (_smoothMoveCo != null)
        {
            StopCoroutine(_smoothMoveCo);
            _smoothMoveCo = null;
        }

        if (smoothBackTarget != null && _smoothOriginalSaved)
        {
            smoothBackTarget.transform.position = _smoothOriginalPos;
        }

        Debug.Log("[PanelAnswerRevealAndChoose] ResetToInitial() done.", this);
    }
}
