// 文件名：KnobCore.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// KnobCore（✅ Stage 边界严格由 ALLCONTROL.OnStageChanged 驱动 + slot-time path 回溯）
///
/// ✅ 当前规则（严格实现）：
/// A) flick / normal / still / pause / micro 都是各自“自然统计”，不做任何强行闭合（不再要求谁+谁=某个RT）
/// B) micro 单独计算（次数/时间），允许出现在 flick/normal episode 内（重叠统计）
/// C) micro 必须同时满足：路径 motif + “该 micro 段平均速度 <= 预设慢速阈值(默认 7 steps/sec)”（也可切换为 <= median）
/// D) flick/normal 不逐 gap 判别：用 still/pause dwell 作为隔板，按 episode 整段判别
/// E) override（敏感）：
///    - 若完全没拨动就提交：整段算 flick
///    - 否则掐头去尾看中间速度序列：若 maxSpeed-minSpeed 很小且平均速度很快(>=overrideFastMeanMin) → 整段 flick
///
/// ✅ 关键修复（pause/still 逻辑）：
/// 1) pause/still 不再依赖 steps==0 的 gap（避免 idleSampling 切碎导致永远 <0.25）
/// 2) pause/still 按“在某个刻度停住，到下一次真正 slot change 之前的 dwell 时间”计算：
///    - dwell >= pauseMinDuration => pauseCount++
///    - dwell >= stillThresholdSec => stillEpisodeCount++ 且 stillTimeSum += dwell
/// 3) flick 阈值为固定值 flickThresholdSps（默认 60 steps/sec）
///
/// ✅ 本次修复（你报的 CS0206）：
/// - List<T> 的索引器不是 ref-returning，不能 ref moveGaps[i]
/// - GetSpeedDt / GetDurationOnce 改为按值传参（去掉 ref）
/// </summary>
public class KnobCore : MonoBehaviour
{
    [Header("刻度与旋钮")]
    [Tooltip("拖：TickRingLocal 或 TickRingConfidenceLocal（任何实现 ITickRing 的组件都行）")]
    public MonoBehaviour ringBehaviour;

    [Tooltip("要旋转的整个 Knob（盘子+箭头），不填就用自己")]
    public Transform knob;

    [Header("JSON 配置")]
    [Tooltip("拖：有 SimpleJsonReader 的对象（里面有 data.default_mode / scale 等）")]
    public SimpleJsonReader reader;

    [Header("HighLight 脚本")]
    public OptionHighLight cardsHighLight;
    public SliderTickHighLight sliderHighLight;

    [Header("旋钮设置")]
    [Tooltip("旋转插值速度（度/秒）")]
    public float rotateSpeed = 720f;

    [Tooltip("当前刻度 index（0 = 第一个刻度）")]
    public int currentIndex = 0;

    public int CurrentSlot => currentIndex + 1;

    // ===== 对外事件 =====
    public Action<int> OnSelectionChanged;
    public Action<int> OnConfirmed;
    public Action<float> OnHoldProgress;
    public Action OnHoldCanceled;

    // ---------- 内部 ----------
    Quaternion baseLocalRotation;
    bool hasTarget = false;

    // ✅ Ring 缓存
    ITickRing _ring;
    ITickRing Ring => _ring;
    bool HasRing => (Ring != null && Ring.TickCount > 0);

    // ============ 各类反馈开关 ============
    [Header("反馈开关（本地控制）")]
    public bool enableHapticFeedback = true;
    public bool enableTickSound = true;
    public bool useGlobalLighting = false;
    public bool lightingOnSelection = false;
    public bool lightingOnConfirm = true;
    public bool useGlobalDampingSound = false;

    // ============ 手柄震动 ============
    [Header("手柄震动反馈（可选）")]
    public GameObject rightHapticObject;
    XRBaseController rightHapticController;

    [Range(0f, 1f)] public float hapticMinAmplitude = 0.1f;
    [Range(0f, 1f)] public float hapticMaxAmplitude = 0.6f;
    public float hapticDuration = 0.05f;

    // ============ 旋钮拨动音效 ============
    [Header("旋钮拨动音效（可选）")]
    public AudioSource audioSource;
    public AudioClip knobTickClip;
    [Range(0f, 1f)] public float knobTickVolume = 0.7f;

    // ============================================================
    // ✅ Mark-based Summary Logging + Path/Still/Micro/Speed
    // ============================================================

    [Header("✅ Mark Summary Logging")]
    [Tooltip("可选：不填则用 ALLCONTROL.Instance")]
    public ALLCONTROL allcontrol;

    [Tooltip("用于区分这个 knob 属于哪个用途（例如 AnswerKnob / ConfidenceKnob）")]
    public string knobRole = "AnswerKnob";

    [Tooltip("是否在 Console 打印 mark 切换/Finalize 日志")]
    public bool logMarkDebug = true;

    [Tooltip("如果你会通过 SetActive(false)/Disable 来隐藏 Knob，这会触发 Finalize。若你只是隐藏但想继续桶，关掉它。")]
    public bool finalizeOnDisable = true;

    [Header("✅ Count Gate (Which stages are counted)")]
    public bool countInAnswerStage = true;
    public bool countInSubmitStage = false; // Submit == Confidence
    public bool countInReadStage = false;
    public bool countInFinishedStage = false;

    [Header("✅ Still/Pause threshold (dwell-based)")]
    public float stillThresholdSec = 0.25f;
    public float pauseMinDuration = 0.25f;

    [Header("✅ Reverse (optional)")]
    public bool enableReverseCount = true;
    public float reverseDebounce = 0.15f;

    [Header("✅ Micro motif rules (path-based + slow-speed constraint)")]
    public int microMaxSpan = 2;
    public int microMinMotifCount = 2;
    public int microMaxAnomalies = 2;

    public bool microRequireSlowSpeed = true;
    public bool microUseFixedMaxSpeed = true;
    public float microMaxAvgSpeedSps = 7f;

    [Header("✅ Speed band diagnostics (Median/MAD for notes only)")]
    public float speedDeltaMin = 1.0f;
    public float speedDeltaK = 1.5f;

    [Header("✅ Flick Threshold (Fixed)")]
    public float flickThresholdSps = 60f;

    [Header("✅ Override (All Flick)")]
    public int overrideMinMoveGaps = 2;
    public int overrideTrimEachSide = 1;
    public float overrideMaxSpeedRange = 2.0f;
    public float overrideFastMeanMin = 60.0f;

    [Header("✅ Idle Sampling (Optional)")]
    public bool enableIdleSampling = true;
    public float idleSampleInterval = 0.05f;

    float _lastRecordedEventT = -1f;
    int _lastRecordedEventSlot = -1;
    ALLCONTROL.QuestionStage _lastRecordedEventStage = ALLCONTROL.QuestionStage.Read;

    [Serializable]
    public class KnobMarkSummary
    {
        public string mark;
        public string itemId;
        public int qIndex0;
        public int qIndex1;
        public int enterCount;
        public string role;

        public string stage;

        public float t_read_in = -1f, t_read_out = -1f;
        public float t_answer_in = -1f, t_answer_out = -1f;
        public float t_conf_in = -1f, t_conf_out = -1f;

        public float t_firstMove_answer = -1f;
        public float t_firstMove_conf = -1f;

        public int tickCount;
        public int currentSlot;
        public float currentAngleY;

        public int slotChangeCount;
        public int reverseCount;
        public int pauseCount;
        public int confirmCount;

        public int minSlot = int.MaxValue;
        public int maxSlot = int.MinValue;
        public int uniqueSlotsVisited = 0;

        public int stillEpisodeCount = 0;
        public float stillOverThresholdSum = 0f;
        public float stillTimeSum = 0f;

        public float microAdjustTimeSum = 0f;
        public int microAdjustCount = 0;

        public float normalAdjustTimeSum = 0f;
        public int normalAdjustCount = 0;

        public float flickTimeSum = 0f;
        public int fastFlickCount = 0;

        public float maxFlickVel = 0f;
        public float maxAbsVel = 0f;

        public float activeMoveTimeSum = 0f;
        public int activeMoveCount = 0;

        public float totalAbsAngle = 0f;

        public bool speedBandValid = false;
        public float speedMedian = -1f;
        public float speedMAD = -1f;
        public float speedThLow = -1f;
        public float speedThHigh = -1f;
        public string speedBandNote = "";

        public bool overrideAllFlick = false;
        public string overrideNote = "";

        public float RT_Read() => (t_read_in >= 0f && t_read_out >= 0f) ? (t_read_out - t_read_in) : -1f;
        public float RT_Answer() => (t_answer_in >= 0f && t_answer_out >= 0f) ? (t_answer_out - t_answer_in) : -1f;
        public float RT_Conf() => (t_conf_in >= 0f && t_conf_out >= 0f) ? (t_conf_out - t_conf_in) : -1f;

        public float RT_Initiation_Answer() => (t_answer_in >= 0f && t_firstMove_answer >= 0f) ? (t_firstMove_answer - t_answer_in) : -1f;
        public float RT_Initiation_Conf() => (t_conf_in >= 0f && t_firstMove_conf >= 0f) ? (t_firstMove_conf - t_conf_in) : -1f;

        public void TouchSlot(int slot, HashSet<int> visited)
        {
            currentSlot = slot;
            if (slot > 0)
            {
                minSlot = Mathf.Min(minSlot, slot);
                maxSlot = Mathf.Max(maxSlot, slot);
                if (visited != null)
                {
                    visited.Add(slot);
                    uniqueSlotsVisited = visited.Count;
                }
            }
        }
    }

    public List<KnobMarkSummary> summaries = new List<KnobMarkSummary>();

    string _currentMark = "";
    KnobMarkSummary _cur = null;

    readonly HashSet<int> _visitedSlots = new HashSet<int>();

    ALLCONTROL AC => (allcontrol != null) ? allcontrol : ALLCONTROL.Instance;

    private ALLCONTROL.QuestionStage _stageNow = ALLCONTROL.QuestionStage.Read;
    private bool _stageInited = false;

    struct SlotEvent
    {
        public float t;
        public int slot;
        public ALLCONTROL.QuestionStage st;
        public bool anchor;
    }

    readonly List<SlotEvent> _events = new List<SlotEvent>(2048);

    float _lastReverseT = -999f;

    void Reset()
    {
        if (!knob) knob = transform;
        CacheRing();
    }

    void OnValidate()
    {
        CacheRing();
        if (currentIndex < 0) currentIndex = 0;

        stillThresholdSec = Mathf.Clamp(stillThresholdSec, 0.01f, 5.0f);
        pauseMinDuration = Mathf.Clamp(pauseMinDuration, 0.01f, 10.0f);
        reverseDebounce = Mathf.Clamp(reverseDebounce, 0.01f, 2.0f);

        microMaxSpan = Mathf.Clamp(microMaxSpan, 1, 6);
        microMinMotifCount = Mathf.Max(1, microMinMotifCount);
        microMaxAnomalies = Mathf.Clamp(microMaxAnomalies, 0, 10);

        microMaxAvgSpeedSps = Mathf.Max(0f, microMaxAvgSpeedSps);

        speedDeltaMin = Mathf.Max(0f, speedDeltaMin);
        speedDeltaK = Mathf.Max(0f, speedDeltaK);

        flickThresholdSps = Mathf.Max(0f, flickThresholdSps);

        overrideMinMoveGaps = Mathf.Max(1, overrideMinMoveGaps);
        overrideTrimEachSide = Mathf.Clamp(overrideTrimEachSide, 0, 4);
        overrideMaxSpeedRange = Mathf.Max(0f, overrideMaxSpeedRange);
        overrideFastMeanMin = Mathf.Max(0f, overrideFastMeanMin);

        idleSampleInterval = Mathf.Clamp(idleSampleInterval, 0.01f, 0.5f);
    }

    void Awake()
    {
        if (!knob) knob = transform;
        baseLocalRotation = knob.localRotation;

        CacheRing();
        ValidateRingOrWarn();

        if (rightHapticObject != null)
        {
            rightHapticController = rightHapticObject.GetComponent<XRBaseController>();
            if (!rightHapticController)
                Debug.LogWarning("[KnobCore] Right Haptic Object 上没有 XRBaseController，将使用 XRNode.RightHand 兜底震动。", rightHapticObject);
        }

        EnsureSummaryForCurrentMark(forceNew: true);
    }

    void OnEnable()
    {
        if (AC != null)
        {
            AC.OnCurrentQuestionChanged += HandleQuestionChanged;
            AC.OnStageChanged += HandleStageChanged;

            _stageNow = AC.GetCurrentStageSafe();
            _stageInited = true;
        }
    }

    void OnDisable()
    {
        if (AC != null)
        {
            AC.OnCurrentQuestionChanged -= HandleQuestionChanged;
            AC.OnStageChanged -= HandleStageChanged;
        }

        if (!finalizeOnDisable) return;

        float now = Time.realtimeSinceStartup;
        OnStageExitBoundary(_stageNow, now);
        FinalizeCurrentSummary("OnDisable");
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        EnsureSummaryForCurrentMark(forceNew: false);

        if (HasRing && hasTarget)
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, Ring.TickCount - 1);
            float angle = Ring.GetAngleForIndex(currentIndex);
            Quaternion target = baseLocalRotation * Quaternion.AngleAxis(angle, Vector3.up);

            knob.localRotation = Quaternion.RotateTowards(
                knob.localRotation,
                target,
                rotateSpeed * Time.deltaTime
            );

            if (useGlobalDampingSound &&
                XRFeedbackMaster.Instance != null &&
                XRFeedbackMaster.Instance.enableKnobDampingSound)
            {
                float yy = knob.localEulerAngles.y;
                XRFeedbackMaster.Instance.ReportKnobAngle(yy);
            }
        }

        UpdateSnapshot();
        MaybeRecordIdleSample();
    }

    bool IsCountingEnabledForStage(ALLCONTROL.QuestionStage st)
    {
        switch (st)
        {
            case ALLCONTROL.QuestionStage.Answer: return countInAnswerStage;
            case ALLCONTROL.QuestionStage.Submit: return countInSubmitStage;
            case ALLCONTROL.QuestionStage.Read: return countInReadStage;
            case ALLCONTROL.QuestionStage.Finished: return countInFinishedStage;
            default: return true;
        }
    }

    bool IsCountingEnabledForCurrentStage()
    {
        if (!_stageInited) return false;
        return IsCountingEnabledForStage(_stageNow);
    }

    bool IsAnswerStage(ALLCONTROL.QuestionStage st) => st == ALLCONTROL.QuestionStage.Answer;
    bool IsConfStage(ALLCONTROL.QuestionStage st) => st == ALLCONTROL.QuestionStage.Submit;
    bool IsReadStage(ALLCONTROL.QuestionStage st) => st == ALLCONTROL.QuestionStage.Read;

    void HandleQuestionChanged(int q)
    {
        float now = Time.realtimeSinceStartup;
        OnStageExitBoundary(_stageNow, now);
        FinalizeCurrentSummary("QuestionChanged");

        EnsureSummaryForCurrentMark(forceNew: true);

        if (AC != null)
        {
            _stageNow = AC.GetCurrentStageSafe();
            _stageInited = true;
        }
    }

    void HandleStageChanged(int q, ALLCONTROL.QuestionStage newStage)
    {
        float now = Time.realtimeSinceStartup;

        if (!_stageInited)
        {
            _stageNow = newStage;
            _stageInited = true;
        }

        OnStageExitBoundary(_stageNow, now);
        OnStageEnterBoundary(newStage, now);

        _stageNow = newStage;

        if (_cur != null) _cur.stage = newStage.ToString();

        _lastRecordedEventT = -1f;
        _lastRecordedEventSlot = -1;
        _lastRecordedEventStage = newStage;
    }

    void EnsureSummaryForCurrentMark(bool forceNew)
    {
        string mark = GetCurrentMark(out int q0, out int enterCount, out string itemId, out string stage);
        bool markChanged = (mark != _currentMark) && !string.IsNullOrEmpty(mark);

        if (forceNew || markChanged || _cur == null)
        {
            _currentMark = mark;

            _cur = new KnobMarkSummary
            {
                mark = mark,
                itemId = itemId,
                qIndex0 = q0,
                qIndex1 = q0 + 1,
                enterCount = enterCount,
                role = knobRole,
                stage = stage,
                tickCount = HasRing ? Ring.TickCount : 0
            };

            _visitedSlots.Clear();
            _events.Clear();
            _lastReverseT = -999f;

            _lastRecordedEventT = -1f;
            _lastRecordedEventSlot = -1;
            _lastRecordedEventStage = _stageNow;

            summaries.Add(_cur);

            if (HasRing) _cur.TouchSlot(CurrentSlot, _visitedSlots);

            if (logMarkDebug)
                Debug.Log($"[KnobCore][Mark] NEW mark={mark} q={q0} enter={enterCount} stage={stage} role={knobRole} itemId='{itemId}'", this);
        }
        else
        {
            if (_cur != null) _cur.stage = stage;
        }
    }

    string GetCurrentMark(out int qIndex0, out int enterCount, out string itemId, out string stage)
    {
        qIndex0 = -1;
        enterCount = 1;
        itemId = "";
        stage = "Read";

        var ac = AC;
        if (ac == null || ac.answers == null || ac.answers.Count == 0) return "NA";

        qIndex0 = ac.currentIndex;
        if (qIndex0 < 0 || qIndex0 >= ac.answers.Count) return "NA";

        var st = ac.answers[qIndex0];

        int qNumber = qIndex0 + 1;
        int rawEnter = st.enterCount;
        int enter1Based = (rawEnter <= 0) ? 1 : rawEnter;

        enterCount = enter1Based;
        itemId = st.itemId;
        stage = st.stage.ToString();

        return $"Q{qNumber}-{enter1Based}";
    }

    void OnStageEnterBoundary(ALLCONTROL.QuestionStage st, float now)
    {
        if (_cur == null) return;

        if (IsReadStage(st))
        {
            if (_cur.t_read_in < 0f) _cur.t_read_in = now;
        }
        else if (IsAnswerStage(st))
        {
            if (_cur.t_answer_in < 0f) _cur.t_answer_in = now;
        }
        else if (IsConfStage(st))
        {
            if (_cur.t_conf_in < 0f) _cur.t_conf_in = now;
        }

        if (IsCountingEnabledForStage(st))
            RecordEvent(now, CurrentSlot, st, anchor: true);
    }

    void OnStageExitBoundary(ALLCONTROL.QuestionStage st, float now)
    {
        if (_cur == null) return;

        if (IsCountingEnabledForStage(st))
            RecordEvent(now, CurrentSlot, st, anchor: true);

        if (IsReadStage(st))
        {
            if (_cur.t_read_in >= 0f) _cur.t_read_out = now;
        }
        else if (IsAnswerStage(st))
        {
            if (_cur.t_answer_in >= 0f) _cur.t_answer_out = now;
        }
        else if (IsConfStage(st))
        {
            if (_cur.t_conf_in >= 0f) _cur.t_conf_out = now;
        }
    }

    bool HasStageEnteredInSummary(ALLCONTROL.QuestionStage st)
    {
        if (_cur == null) return false;
        if (IsAnswerStage(st)) return _cur.t_answer_in >= 0f;
        if (IsConfStage(st)) return _cur.t_conf_in >= 0f;
        if (IsReadStage(st)) return _cur.t_read_in >= 0f;
        return false;
    }

    void RecordEvent(float t, int slot, ALLCONTROL.QuestionStage st, bool anchor)
    {
        if (_cur == null) return;
        if (!IsCountingEnabledForStage(st)) return;

        _events.Add(new SlotEvent { t = t, slot = slot, st = st, anchor = anchor });

        _lastRecordedEventT = t;
        _lastRecordedEventSlot = slot;
        _lastRecordedEventStage = st;
    }

    void RecordSlotChangeEvent(int newSlot)
    {
        if (_cur == null) return;
        if (!_stageInited) return;

        var st = _stageNow;

        if (!IsCountingEnabledForStage(st)) return;
        if (!HasStageEnteredInSummary(st)) return;

        RecordEvent(Time.realtimeSinceStartup, newSlot, st, anchor: false);
    }

    void MaybeRecordIdleSample()
    {
        if (!Application.isPlaying) return;
        if (!enableIdleSampling) return;
        if (_cur == null) return;
        if (!_stageInited) return;

        var st = _stageNow;
        if (!IsCountingEnabledForStage(st)) return;
        if (!HasStageEnteredInSummary(st)) return;

        float now = Time.realtimeSinceStartup;

        if (_lastRecordedEventT < 0f)
        {
            RecordEvent(now, CurrentSlot, st, anchor: false);
            return;
        }

        if (_lastRecordedEventStage != st)
        {
            RecordEvent(now, CurrentSlot, st, anchor: false);
            return;
        }

        if (now - _lastRecordedEventT < idleSampleInterval) return;

        if (CurrentSlot == _lastRecordedEventSlot)
        {
            RecordEvent(now, CurrentSlot, st, anchor: false);
        }
    }

    void FinalizeCurrentSummary(string reason)
    {
        if (_cur == null) return;

        _cur.stillEpisodeCount = 0;
        _cur.stillOverThresholdSum = 0f;
        _cur.stillTimeSum = 0f;

        _cur.microAdjustTimeSum = 0f;
        _cur.microAdjustCount = 0;

        _cur.normalAdjustTimeSum = 0f;
        _cur.normalAdjustCount = 0;

        _cur.flickTimeSum = 0f;
        _cur.fastFlickCount = 0;

        _cur.maxAbsVel = 0f;
        _cur.maxFlickVel = 0f;

        _cur.activeMoveTimeSum = 0f;
        _cur.activeMoveCount = 0;

        _cur.speedBandValid = false;
        _cur.speedMedian = -1f;
        _cur.speedMAD = -1f;
        _cur.speedThLow = -1f;
        _cur.speedThHigh = -1f;
        _cur.speedBandNote = "";

        _cur.overrideAllFlick = false;
        _cur.overrideNote = "";

        AnalyzeEnabledStagesIntoSummary(_cur);

        if (logMarkDebug)
        {
            Debug.Log(
                $"[KnobCore][Finalize:{reason}] mark={_cur.mark} role={_cur.role} stageSnap={_cur.stage} " +
                $"RT(Read/Ans/Conf)=({_cur.RT_Read():F3},{_cur.RT_Answer():F3},{_cur.RT_Conf():F3}) " +
                $"init(Ans/Conf)=({_cur.RT_Initiation_Answer():F3},{_cur.RT_Initiation_Conf():F3}) | " +
                $"slotChg={_cur.slotChangeCount} rev={_cur.reverseCount} pause={_cur.pauseCount} conf={_cur.confirmCount} | " +
                $"override={_cur.overrideAllFlick}({_cur.overrideNote}) | " +
                $"still={_cur.stillTimeSum:F3}s(ep={_cur.stillEpisodeCount}) " +
                $"micro={_cur.microAdjustTimeSum:F3}s({_cur.microAdjustCount}) " +
                $"normal={_cur.normalAdjustTimeSum:F3}s({_cur.normalAdjustCount}) " +
                $"flick={_cur.flickTimeSum:F3}s({_cur.fastFlickCount}) " +
                $"activeMove={_cur.activeMoveTimeSum:F3}s " +
                $"maxSps={_cur.maxAbsVel:F2} maxFlickSps={_cur.maxFlickVel:F2} " +
                $"bandValid={_cur.speedBandValid} med={_cur.speedMedian:F2} mad={_cur.speedMAD:F2} " +
                $"note={_cur.speedBandNote}",
                this
            );
        }
    }

    void AnalyzeEnabledStagesIntoSummary(KnobMarkSummary sum)
    {
        if (_events.Count < 2)
        {
            sum.speedBandNote = "no_events";
            return;
        }

        if (countInAnswerStage && sum.t_answer_in >= 0f && sum.t_answer_out >= 0f)
            AnalyzeOneStage(sum, ALLCONTROL.QuestionStage.Answer, sum.t_answer_in, sum.t_answer_out);

        if (countInSubmitStage && sum.t_conf_in >= 0f && sum.t_conf_out >= 0f)
            AnalyzeOneStage(sum, ALLCONTROL.QuestionStage.Submit, sum.t_conf_in, sum.t_conf_out);

        if (countInReadStage && sum.t_read_in >= 0f && sum.t_read_out >= 0f)
            AnalyzeOneStage(sum, ALLCONTROL.QuestionStage.Read, sum.t_read_in, sum.t_read_out);

        sum.activeMoveTimeSum = sum.normalAdjustTimeSum + sum.flickTimeSum;
        sum.activeMoveCount = sum.normalAdjustCount + sum.fastFlickCount;
    }

    class MicroSeg
    {
        public int startPoint;
        public int endPoint;   // inclusive point index
        public int motifCount;
    }

    struct MoveGap
    {
        public int fromSlot;
        public int toSlot;
        public int steps;

        public float dwellBefore;
        public float moveDt;
        public float dtTotal;
        public bool noIdleSplit;

        public float tTo;
        public bool barrier;
    }

    // ✅✅✅ 这里是修复点：去掉 ref（按值传参即可）
    static float GetSpeedDt(MoveGap g, float eps)
    {
        if (g.noIdleSplit) return Mathf.Max(eps, g.dtTotal);
        return Mathf.Max(eps, g.moveDt);
    }

    static float GetDurationOnce(MoveGap g)
    {
        if (g.noIdleSplit) return Mathf.Max(0f, g.dtTotal);
        return Mathf.Max(0f, g.dwellBefore) + Mathf.Max(0f, g.moveDt);
    }

    void AnalyzeOneStage(KnobMarkSummary sum, ALLCONTROL.QuestionStage stage, float tIn, float tOut)
    {
        List<SlotEvent> ev = CollectStageEventsWithAnchors(stage, tIn, tOut);
        if (ev.Count < 2) return;

        if (IsAnswerStage(stage) && sum.t_firstMove_answer < 0f)
        {
            for (int i = 0; i < ev.Count - 1; i++)
                if (ev[i + 1].slot != ev[i].slot) { sum.t_firstMove_answer = ev[i + 1].t; break; }
        }
        if (IsConfStage(stage) && sum.t_firstMove_conf < 0f)
        {
            for (int i = 0; i < ev.Count - 1; i++)
                if (ev[i + 1].slot != ev[i].slot) { sum.t_firstMove_conf = ev[i + 1].t; break; }
        }

        List<MoveGap> moveGaps = new List<MoveGap>(128);
        List<int> pathSlots = new List<int>(128);

        int runSlot = ev[0].slot;
        float runStartT = ev[0].t;
        float lastSameT = ev[0].t;
        bool runHasIdle = false;

        pathSlots.Add(runSlot);

        for (int i = 1; i < ev.Count; i++)
        {
            var e = ev[i];

            if (e.slot == runSlot)
            {
                if (e.t > lastSameT + 0.000001f) runHasIdle = true;
                lastSameT = e.t;
                continue;
            }

            float tChange = e.t;
            int toSlot = e.slot;

            float dtTotal = Mathf.Max(0f, tChange - runStartT);
            float dwell = runHasIdle ? Mathf.Max(0f, lastSameT - runStartT) : dtTotal;
            float moveDt = runHasIdle ? Mathf.Max(0f, tChange - lastSameT) : dtTotal;

            if (dwell >= pauseMinDuration)
                sum.pauseCount += 1;

            if (dwell >= stillThresholdSec)
            {
                sum.stillEpisodeCount += 1;
                sum.stillTimeSum += dwell;
                sum.stillOverThresholdSum += Mathf.Max(0f, dwell - stillThresholdSec);
            }

            int steps = Mathf.Abs(toSlot - runSlot);
            bool barrier = (dwell >= stillThresholdSec);

            if (steps > 0)
            {
                moveGaps.Add(new MoveGap
                {
                    fromSlot = runSlot,
                    toSlot = toSlot,
                    steps = steps,
                    dwellBefore = dwell,
                    moveDt = moveDt,
                    dtTotal = dtTotal,
                    noIdleSplit = !runHasIdle,
                    tTo = tChange,
                    barrier = barrier
                });

                pathSlots.Add(toSlot);
            }

            runSlot = toSlot;
            runStartT = tChange;
            lastSameT = tChange;
            runHasIdle = false;
        }

        float tailDwell = Mathf.Max(0f, tOut - runStartT);
        if (tailDwell >= pauseMinDuration) sum.pauseCount += 1;
        if (tailDwell >= stillThresholdSec)
        {
            sum.stillEpisodeCount += 1;
            sum.stillTimeSum += tailDwell;
            sum.stillOverThresholdSum += Mathf.Max(0f, tailDwell - stillThresholdSec);
        }

        float stageRT = Mathf.Max(0f, tOut - tIn);

        const float EPS = 0.0001f;

        List<float> moveGapSpeeds = new List<float>(moveGaps.Count);
        for (int i = 0; i < moveGaps.Count; i++)
        {
            float dt = GetSpeedDt(moveGaps[i], EPS);
            float sps = moveGaps[i].steps / dt;
            moveGapSpeeds.Add(sps);
            if (sps > sum.maxAbsVel) sum.maxAbsVel = sps;
        }

        List<float> episodeSpeeds = new List<float>(64);

        int epStart = 0;
        Action<int, int> collectEpisodeSpeed = (startIdx, endIdx) =>
        {
            if (endIdx < startIdx) return;

            int epSteps = 0;
            float epMoveTime = 0f;

            for (int k = startIdx; k <= endIdx; k++)
            {
                epSteps += moveGaps[k].steps;
                epMoveTime += GetSpeedDt(moveGaps[k], EPS);
            }

            if (epSteps <= 0 || epMoveTime <= EPS) return;

            float epAvg = epSteps / epMoveTime;
            episodeSpeeds.Add(epAvg);
            if (epAvg > sum.maxAbsVel) sum.maxAbsVel = epAvg;
        };

        for (int k = 0; k < moveGaps.Count; k++)
        {
            if (moveGaps[k].barrier)
            {
                collectEpisodeSpeed(epStart, k - 1);
                epStart = k;
            }
        }
        collectEpisodeSpeed(epStart, moveGaps.Count - 1);

        List<float> baseSpeeds = (episodeSpeeds.Count >= 3) ? episodeSpeeds : moveGapSpeeds;

        float speedMedian = (baseSpeeds.Count > 0) ? Median(baseSpeeds) : 0f;
        float speedMad = (baseSpeeds.Count > 0) ? MAD(baseSpeeds, speedMedian) : 0f;
        float delta = Mathf.Max(speedDeltaMin, speedDeltaK * speedMad);

        sum.speedMedian = speedMedian;
        sum.speedMAD = speedMad;
        sum.speedThLow = Mathf.Max(0f, speedMedian - delta);
        sum.speedThHigh = flickThresholdSps;
        sum.speedBandValid = (baseSpeeds.Count >= 3);

        string baseTag = (baseSpeeds == episodeSpeeds) ? "episode" : "gapFallback";
        sum.speedBandNote =
            $"stage={stage};base={baseTag};medSps={speedMedian:F2};mad={speedMad:F2};delta={delta:F2};" +
            $"flickThFixed={flickThresholdSps:F2};stillTh={stillThresholdSec:F2};pauseTh={pauseMinDuration:F2};" +
            $"idle={enableIdleSampling};idleInt={idleSampleInterval:F2};epsN={episodeSpeeds.Count};gapN={moveGapSpeeds.Count}";

        if (enableReverseCount)
        {
            int lastDir = 0;
            for (int i = 0; i < moveGaps.Count; i++)
            {
                int d = moveGaps[i].toSlot - moveGaps[i].fromSlot;
                int dir = (d > 0) ? 1 : (d < 0 ? -1 : 0);
                if (dir == 0) continue;

                if (lastDir != 0 && dir != lastDir)
                {
                    float t = moveGaps[i].tTo;
                    if (t - _lastReverseT >= reverseDebounce)
                    {
                        sum.reverseCount += 1;
                        _lastReverseT = t;
                    }
                }
                lastDir = dir;
            }
        }

        bool overrideAllFlick = false;
        string overrideNote = "";

        if (moveGapSpeeds.Count == 0)
        {
            overrideAllFlick = true;
            overrideNote = "no_move_gaps";
        }
        else if (moveGapSpeeds.Count >= overrideMinMoveGaps)
        {
            int n = moveGapSpeeds.Count;
            int trim = Mathf.Clamp(overrideTrimEachSide, 0, n / 2);

            int lo = trim;
            int hi = n - 1 - trim;
            if (hi < lo) { lo = 0; hi = n - 1; }

            float minS = float.PositiveInfinity;
            float maxS = float.NegativeInfinity;
            float sumS = 0f;
            int cntS = 0;

            for (int k = lo; k <= hi; k++)
            {
                float s = moveGapSpeeds[k];
                minS = Mathf.Min(minS, s);
                maxS = Mathf.Max(maxS, s);
                sumS += s;
                cntS++;
            }

            float meanS = (cntS > 0) ? (sumS / cntS) : 0f;
            float range = (cntS > 0) ? (maxS - minS) : float.PositiveInfinity;

            if (cntS >= overrideMinMoveGaps && range <= overrideMaxSpeedRange && meanS >= overrideFastMeanMin)
            {
                overrideAllFlick = true;
                overrideNote = $"trim={trim};midRange={range:F2}<={overrideMaxSpeedRange:F2};midMean={meanS:F2}>={overrideFastMeanMin:F2}";
            }
            else
            {
                overrideNote = $"trim={trim};midRange={range:F2};midMean={meanS:F2}";
            }
        }
        else
        {
            overrideNote = $"moveGaps={moveGapSpeeds.Count}<min({overrideMinMoveGaps})";
        }

        if (overrideAllFlick)
        {
            sum.overrideAllFlick = true;
            sum.overrideNote = overrideNote;

            sum.flickTimeSum += stageRT;
            sum.fastFlickCount += 1;

            if (sum.maxAbsVel > sum.maxFlickVel) sum.maxFlickVel = sum.maxAbsVel;
            return;
        }
        else
        {
            sum.overrideAllFlick = false;
            sum.overrideNote = overrideNote;
        }

        // micro：路径 motif + 慢速约束
        if (pathSlots.Count >= 3 && moveGaps.Count > 0)
        {
            int[] pointSlots = pathSlots.ToArray();
            List<MicroSeg> microSegs = FindMicroSegments(pointSlots);

            for (int m = 0; m < microSegs.Count; m++)
            {
                MicroSeg seg = microSegs[m];
                int startGap = Mathf.Clamp(seg.startPoint, 0, moveGaps.Count - 1);
                int endGap = Mathf.Clamp(seg.endPoint - 1, 0, moveGaps.Count - 1);
                if (endGap < startGap) continue;

                float segDuration = 0f;
                int segSteps = 0;
                float segMoveTime = 0f;

                for (int g = startGap; g <= endGap; g++)
                {
                    segDuration += GetDurationOnce(moveGaps[g]);

                    segSteps += moveGaps[g].steps;
                    segMoveTime += GetSpeedDt(moveGaps[g], EPS);
                }

                float segAvgSpeed = (segMoveTime > EPS) ? (segSteps / segMoveTime) : float.PositiveInfinity;

                bool slowOk = true;
                if (microRequireSlowSpeed)
                {
                    if (microUseFixedMaxSpeed)
                        slowOk = (segAvgSpeed <= microMaxAvgSpeedSps + 0.0001f);
                    else
                        slowOk = (segAvgSpeed <= speedMedian + 0.0001f);
                }

                if (segSteps > 0 && segDuration > 0f && slowOk)
                {
                    sum.microAdjustCount += 1;
                    sum.microAdjustTimeSum += segDuration;
                }
            }
        }

        // flick/normal：按 barrier 切 episode，整段判别
        int epStart2 = 0;

        Action<int, int> finalizeEpisode = (startIdx, endIdx) =>
        {
            if (endIdx < startIdx) return;

            float epDuration = 0f;
            int epSteps = 0;
            float epMoveTime = 0f;

            for (int k = startIdx; k <= endIdx; k++)
            {
                var mg = moveGaps[k];

                epMoveTime += GetSpeedDt(mg, EPS);
                epSteps += mg.steps;

                if (mg.noIdleSplit)
                {
                    if (!mg.barrier)
                        epDuration += Mathf.Max(0f, mg.dtTotal);
                }
                else
                {
                    float addDwell = mg.barrier ? 0f : Mathf.Max(0f, mg.dwellBefore);
                    float addMove = Mathf.Max(0f, mg.moveDt);
                    epDuration += addDwell + addMove;
                }
            }

            if (epSteps <= 0) return;

            float epAvgSpeed = (epMoveTime > EPS) ? (epSteps / epMoveTime) : float.PositiveInfinity;
            bool isFlickEpisode = (epAvgSpeed >= flickThresholdSps);

            if (isFlickEpisode)
            {
                sum.flickTimeSum += epDuration;
                sum.fastFlickCount += 1;
                if (epAvgSpeed > sum.maxFlickVel) sum.maxFlickVel = epAvgSpeed;
            }
            else
            {
                sum.normalAdjustTimeSum += epDuration;
                sum.normalAdjustCount += 1;
            }
        };

        for (int k = 0; k < moveGaps.Count; k++)
        {
            if (moveGaps[k].barrier && k > epStart2)
            {
                finalizeEpisode(epStart2, k - 1);
                epStart2 = k;
            }
        }
        finalizeEpisode(epStart2, moveGaps.Count - 1);
    }

    List<MicroSeg> FindMicroSegments(int[] pointSlots)
    {
        int n = pointSlots.Length;
        int gapN = n - 1;
        List<MicroSeg> result = new List<MicroSeg>();

        if (n < 3) return result;

        Func<int, bool> isABA = (k) =>
        {
            if (k + 2 >= n) return false;
            return pointSlots[k] == pointSlots[k + 2] && pointSlots[k] != pointSlots[k + 1];
        };

        Func<int, bool> isABCBA = (k) =>
        {
            if (k + 4 >= n) return false;
            return pointSlots[k] == pointSlots[k + 4] &&
                   pointSlots[k + 1] == pointSlots[k + 3] &&
                   pointSlots[k] != pointSlots[k + 1] &&
                   pointSlots[k + 1] != pointSlots[k + 2];
        };

        int i = 0;
        while (i < gapN)
        {
            if (pointSlots[i] == pointSlots[i + 1]) { i++; continue; }

            int start = i;
            int bestEnd = -1;
            int bestMotif = 0;
            int anomalies = 0;

            int localMin = Mathf.Min(pointSlots[i], pointSlots[i + 1]);
            int localMax = Mathf.Max(pointSlots[i], pointSlots[i + 1]);

            int motif = 0;

            int j = i + 1;
            while (j < n)
            {
                int s = pointSlots[j];

                int newMin = Mathf.Min(localMin, s);
                int newMax = Mathf.Max(localMax, s);
                int span = newMax - newMin;

                if (span > microMaxSpan)
                {
                    anomalies++;
                    if (anomalies > microMaxAnomalies) break;
                }
                else
                {
                    localMin = newMin;
                    localMax = newMax;
                }

                int k = j - 2;
                if (k >= 0 && isABA(k)) motif++;
                int k2 = j - 4;
                if (k2 >= 0 && isABCBA(k2)) motif++;

                int currSpan = localMax - localMin;
                if (currSpan <= microMaxSpan && motif >= microMinMotifCount)
                {
                    bestEnd = j;
                    bestMotif = motif;
                }

                j++;
            }

            if (bestEnd >= 0)
            {
                result.Add(new MicroSeg { startPoint = start, endPoint = bestEnd, motifCount = bestMotif });
                i = bestEnd;
            }
            else
            {
                i = start + 1;
            }
        }

        return result;
    }

    List<SlotEvent> CollectStageEventsWithAnchors(ALLCONTROL.QuestionStage stage, float tIn, float tOut)
    {
        List<SlotEvent> ev = new List<SlotEvent>(256);

        int slotAtIn = CurrentSlot;
        float best = float.MaxValue;
        for (int i = 0; i < _events.Count; i++)
        {
            if (_events[i].st != stage) continue;
            float d = Mathf.Abs(_events[i].t - tIn);
            if (d < best)
            {
                best = d;
                slotAtIn = _events[i].slot;
            }
        }

        ev.Add(new SlotEvent { t = tIn, slot = slotAtIn, st = stage, anchor = true });

        for (int i = 0; i < _events.Count; i++)
        {
            var e = _events[i];
            if (e.st != stage) continue;
            if (e.t < tIn || e.t > tOut) continue;
            ev.Add(e);
        }

        int slotAtOut = ev[ev.Count - 1].slot;
        ev.Add(new SlotEvent { t = tOut, slot = slotAtOut, st = stage, anchor = true });

        ev.Sort((a, b) => a.t.CompareTo(b.t));

        List<SlotEvent> compact = new List<SlotEvent>(ev.Count);
        for (int i = 0; i < ev.Count; i++)
        {
            if (compact.Count == 0) { compact.Add(ev[i]); continue; }
            var last = compact[compact.Count - 1];

            bool sameTime = Mathf.Abs(ev[i].t - last.t) < 0.000001f;
            bool sameSlot = ev[i].slot == last.slot;
            bool sameAnchor = ev[i].anchor == last.anchor;
            bool sameStage = ev[i].st == last.st;

            if (sameTime && sameSlot && sameAnchor && sameStage)
                continue;

            compact.Add(ev[i]);
        }

        return compact;
    }

    static float Median(List<float> xs)
    {
        if (xs == null || xs.Count == 0) return 0f;
        float[] arr = xs.ToArray();
        Array.Sort(arr);
        int n = arr.Length;
        if (n % 2 == 1) return arr[n / 2];
        return 0.5f * (arr[n / 2 - 1] + arr[n / 2]);
    }

    static float MAD(List<float> xs, float median)
    {
        if (xs == null || xs.Count == 0) return 0f;
        float[] dev = new float[xs.Count];
        for (int i = 0; i < xs.Count; i++) dev[i] = Mathf.Abs(xs[i] - median);
        Array.Sort(dev);
        int n = dev.Length;
        if (n % 2 == 1) return dev[n / 2];
        return 0.5f * (dev[n / 2 - 1] + dev[n / 2]);
    }

    void UpdateSnapshot()
    {
        if (_cur == null) return;

        _cur.tickCount = HasRing ? Ring.TickCount : 0;
        _cur.currentSlot = HasRing ? CurrentSlot : 0;
        _cur.currentAngleY = HasRing ? Ring.GetAngleForIndex(currentIndex) : 0f;
    }

    public void InitToMiddle()
    {
        if (!HasRing) return;

        currentIndex = Ring.TickCount / 2;
        hasTarget = true;

        int slot = CurrentSlot;
        OnSelectionChanged?.Invoke(slot);

        PlayHapticForCurrentSlot();
        if (useGlobalLighting && lightingOnSelection) MaybeUpdateGlobalLighting(slot);

        if (_cur != null) _cur.TouchSlot(slot, _visitedSlots);

        RecordSlotChangeEvent(slot);
    }

    public void Step(int delta)
    {
        if (!HasRing) return;

        EnsureSummaryForCurrentMark(forceNew: false);
        if (_cur == null) return;

        hasTarget = true;

        int oldIndex = currentIndex;
        currentIndex = Mathf.Clamp(currentIndex + delta, 0, Ring.TickCount - 1);

        int oldSlot = oldIndex + 1;
        int newSlot = CurrentSlot;

        if (newSlot != oldSlot)
        {
            OnSelectionChanged?.Invoke(newSlot);

            _cur.TouchSlot(newSlot, _visitedSlots);

            if (IsCountingEnabledForCurrentStage())
            {
                _cur.slotChangeCount += 1;

                float angA = Ring.GetAngleForIndex(oldIndex);
                float angB = Ring.GetAngleForIndex(currentIndex);
                _cur.totalAbsAngle += Mathf.Abs(Mathf.DeltaAngle(angA, angB));
            }

            RecordSlotChangeEvent(newSlot);

            PlayHapticForCurrentSlot();
            PlayKnobTickSound();

            if (useGlobalLighting && lightingOnSelection) MaybeUpdateGlobalLighting(newSlot);
        }
    }

    public void SnapTo(int humanIndex)
    {
        if (!HasRing) return;

        EnsureSummaryForCurrentMark(forceNew: false);

        humanIndex = Mathf.Clamp(humanIndex, 1, Ring.TickCount);

        int oldSlot = CurrentSlot;
        int oldIndex = currentIndex;

        currentIndex = humanIndex - 1;
        hasTarget = true;

        int newSlot = CurrentSlot;
        OnSelectionChanged?.Invoke(newSlot);

        PlayHapticForCurrentSlot();
        PlayKnobTickSound();

        if (useGlobalLighting && lightingOnSelection) MaybeUpdateGlobalLighting(newSlot);

        if (_cur != null) _cur.TouchSlot(newSlot, _visitedSlots);

        if (IsCountingEnabledForCurrentStage() && newSlot != oldSlot)
        {
            _cur.slotChangeCount += 1;

            float angA = Ring.GetAngleForIndex(oldIndex);
            float angB = Ring.GetAngleForIndex(currentIndex);
            _cur.totalAbsAngle += Mathf.Abs(Mathf.DeltaAngle(angA, angB));
        }

        RecordSlotChangeEvent(newSlot);
    }

    public void CalibrateFromCurrentRotation()
    {
        if (!HasRing) return;
        if (!knob) knob = transform;

        EnsureSummaryForCurrentMark(forceNew: false);

        Quaternion deltaRot = Quaternion.Inverse(baseLocalRotation) * knob.localRotation;
        float currentAngle = Mathf.DeltaAngle(0f, deltaRot.eulerAngles.y);

        int bestIndex = 0;
        float bestDiff = float.MaxValue;

        for (int i = 0; i < Ring.TickCount; i++)
        {
            float a = Ring.GetAngleForIndex(i);
            float diff = Mathf.Abs(Mathf.DeltaAngle(a, currentAngle));
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIndex = i;
            }
        }

        int oldSlot = CurrentSlot;
        int oldIndex = currentIndex;

        currentIndex = bestIndex;
        hasTarget = true;

        int slot = CurrentSlot;
        OnSelectionChanged?.Invoke(slot);

        PlayHapticForCurrentSlot();
        if (useGlobalLighting && lightingOnSelection) MaybeUpdateGlobalLighting(slot);

        if (_cur != null) _cur.TouchSlot(slot, _visitedSlots);

        if (IsCountingEnabledForCurrentStage() && slot != oldSlot)
        {
            _cur.slotChangeCount += 1;

            float angA = Ring.GetAngleForIndex(oldIndex);
            float angB = Ring.GetAngleForIndex(currentIndex);
            _cur.totalAbsAngle += Mathf.Abs(Mathf.DeltaAngle(angA, angB));
        }

        RecordSlotChangeEvent(slot);
    }

    public void Confirm()
    {
        if (!HasRing) return;

        EnsureSummaryForCurrentMark(forceNew: false);

        currentIndex = Mathf.Clamp(currentIndex, 0, Ring.TickCount - 1);

        int human = CurrentSlot;

        var cfg = (reader != null) ? reader.data : null;
        string mode = "cards";
        if (cfg != null && !string.IsNullOrEmpty(cfg.default_mode))
            mode = cfg.default_mode.ToLowerInvariant();

        if (mode == "slider")
        {
            if (sliderHighLight != null) sliderHighLight.ConfirmCurrentSelection();
            else Debug.LogWarning("[KnobCore] default_mode=slider 但 sliderHighLight 为空");
        }
        else
        {
            if (cardsHighLight != null) cardsHighLight.ConfirmCurrentSelection();
            else Debug.LogWarning("[KnobCore] default_mode=cards 但 cardsHighLight 为空");
        }

        OnConfirmed?.Invoke(human);

        if (_cur != null)
        {
            _cur.currentSlot = human;
            _cur.currentAngleY = Ring.GetAngleForIndex(currentIndex);
            _cur.TouchSlot(human, _visitedSlots);

            if (IsCountingEnabledForCurrentStage())
                _cur.confirmCount += 1;
        }

        if (useGlobalLighting && lightingOnConfirm) MaybeUpdateGlobalLighting(human);
    }

    public void UpdateConfirmHoldVisual(float progress)
    {
        progress = Mathf.Clamp01(progress);

        var cfg = (reader != null) ? reader.data : null;
        string mode = "cards";
        if (cfg != null && !string.IsNullOrEmpty(cfg.default_mode))
            mode = cfg.default_mode.ToLowerInvariant();

        if (mode == "slider")
        {
            if (sliderHighLight != null) sliderHighLight.UpdateHoldVisual(progress);
        }
        else
        {
            if (cardsHighLight != null) cardsHighLight.UpdateHoldVisual(progress);
        }

        OnHoldProgress?.Invoke(progress);
    }

    public void CancelConfirmHoldVisual()
    {
        var cfg = (reader != null) ? reader.data : null;
        string mode = "cards";
        if (cfg != null && !string.IsNullOrEmpty(cfg.default_mode))
            mode = cfg.default_mode.ToLowerInvariant();

        if (mode == "slider")
        {
            if (sliderHighLight != null) sliderHighLight.CancelHoldVisual();
        }
        else
        {
            if (cardsHighLight != null) cardsHighLight.CancelHoldVisual();
        }

        OnHoldCanceled?.Invoke();
    }

    void CacheRing()
    {
        _ring = ringBehaviour as ITickRing;
    }

    void ValidateRingOrWarn()
    {
        if (ringBehaviour == null)
        {
            Debug.LogWarning("[KnobCore] ringBehaviour 未设置：请拖入 TickRingLocal / TickRingConfidenceLocal（实现 ITickRing）", this);
            return;
        }

        if (Ring == null)
        {
            Debug.LogError($"[KnobCore] ringBehaviour={ringBehaviour.GetType().Name} 没有实现 ITickRing。请在类声明后加: , ITickRing", ringBehaviour);
            return;
        }

        if (Ring.TickCount <= 0)
        {
            Debug.LogWarning("[KnobCore] Ring 已绑定但 TickCount=0：请确认 Ring 是否已 Rebuild。", ringBehaviour);
        }
    }

    void MaybeUpdateGlobalLighting(int slot)
    {
        if (!useGlobalLighting) return;
        if (XRFeedbackMaster.Instance == null) return;
        if (!HasRing) return;

        int min = 1;
        int max = Ring.TickCount;
        XRFeedbackMaster.Instance.SetLevelFromLikert(slot, min, max);
    }

    void PlayHapticForCurrentSlot()
    {
        if (!Application.isPlaying) return;
        if (!enableHapticFeedback) return;
        if (!HasRing) return;
        if (hapticDuration <= 0f) return;
        if (hapticMinAmplitude <= 0f && hapticMaxAmplitude <= 0f) return;

        int total = Mathf.Max(1, Ring.TickCount);
        int slot = Mathf.Clamp(CurrentSlot, 1, total);

        float t = (total == 1) ? 1f : (slot - 1) / (float)(total - 1);
        float amplitude = Mathf.Lerp(hapticMinAmplitude, hapticMaxAmplitude, t);
        amplitude = Mathf.Clamp01(amplitude);
        if (amplitude <= 0f) return;

        PlayHapticImpulse(amplitude, hapticDuration);
    }

    void PlayHapticImpulse(float amplitude, float duration)
    {
        if (rightHapticController != null)
        {
            rightHapticController.SendHapticImpulse(amplitude, duration);
        }
        else
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (device.isValid)
            {
                device.SendHapticImpulse(0u, amplitude, duration);
            }
        }
    }

    void PlayKnobTickSound()
    {
        if (!Application.isPlaying) return;
        if (!enableTickSound) return;
        if (!knobTickClip) return;
        if (knobTickVolume <= 0f) return;

        if (audioSource != null)
        {
            audioSource.PlayOneShot(knobTickClip, knobTickVolume);
        }
        else
        {
            Vector3 pos = knob ? knob.position : transform.position;
            AudioSource.PlayClipAtPoint(knobTickClip, pos, knobTickVolume);
        }
    }
}

/// <summary>
/// ✅ 小扩展：安全取 stage
/// </summary>
public static class AllControlStageExt
{
    public static ALLCONTROL.QuestionStage GetCurrentStageSafe(this ALLCONTROL ac)
    {
        if (ac == null || ac.answers == null || ac.answers.Count == 0) return ALLCONTROL.QuestionStage.Read;
        int idx = ac.currentIndex;
        if (idx < 0 || idx >= ac.answers.Count) return ALLCONTROL.QuestionStage.Read;
        return ac.answers[idx].stage;
    }
}
