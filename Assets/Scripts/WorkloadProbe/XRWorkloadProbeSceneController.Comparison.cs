using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

public partial class XRWorkloadProbeSceneController
{
    public enum QuestionnaireInputMethod
    {
        PaxsmKnob,
        PointAndClick
    }

    struct ComparisonBlockSpec
    {
        public QuestionnaireInputMethod method;
        public int formIndex;

        public ComparisonBlockSpec(QuestionnaireInputMethod method, int formIndex)
        {
            this.method = method;
            this.formIndex = formIndex;
        }
    }

    sealed class ClassicPointClickResponseState
    {
        public TlxItem item;
        public QuestionnaireRecord record;
        public int answer = -1;
        public int confidence = -1;
        public float answerLastSelectionRealtime = -1f;
        public float confidenceLastSelectionRealtime = -1f;
        public int answerSelectionCount;
        public int confidenceSelectionCount;
        public int answerChangeCount;
        public int confidenceChangeCount;
    }

    sealed class ClassicPointClickRadioTarget
    {
        public int responseIndex;
        public int value;
        public GameObject hitObject;
        public Renderer outerRenderer;
        public Renderer innerRenderer;
    }

    sealed class ClassicPointClickPageRecord
    {
        public string blockId = "";
        public string stage = "";
        public int pageIndex;
        public int pageCount;
        public int visitIndex;
        public int firstItemIndex;
        public int lastItemIndex;
        public int requiredResponses;
        public int answeredAtExit;
        public float enterRealtime;
        public float exitRealtime;
        public float duration;
        public string navigationAction = "";
        public int selectionCount;
        public int answerChangeCount;
        public float pointerPath;
        public float pointerPeakSpeed;
        public int hoverChangeCount;
    }

    [Header("Questionnaire Input Method")]
    public QuestionnaireInputMethod questionnaireInputMethod = QuestionnaireInputMethod.PaxsmKnob;

    [Header("PAXSM Comparison Study")]
    public bool questionnaireComparisonMode;
    public string comparisonWorkloadBankResourcesPath = "QuestionBanks/NASA_TLX_21_Comparison";
    public string comparisonSusBankResourcesPath = "QuestionBanks/SUS";
    public string comparisonPracticeBankResourcesPath = "QuestionBanks/Comparison_Practice_Targets_21";
    public string comparisonFormalBankAResourcesPath = "QuestionBanks/Comparison_Formal_Targets_A_21";
    public string comparisonFormalBankBResourcesPath = "QuestionBanks/Comparison_Formal_Targets_B_21";
    [Range(8, 12)] public int comparisonFormalTrialsPerMethod = 12;
    [Range(5f, 120f)] public float comparisonRestSeconds = 20f;
    public bool comparisonCollectConfidence = false;

    [Header("Classic Point-and-click Baseline")]
    [Range(1, 3)] public int comparisonPointClickItemsPerPage = 1;
    [Range(0.035f, 0.09f)] public float comparisonPointClickRadioDiameterMeters = 0.055f;

    readonly List<GameObject> _questionnairePointClickTargets = new List<GameObject>();
    readonly List<GameObject> _questionnairePointClickLabels = new List<GameObject>();
    readonly List<ClassicPointClickRadioTarget> _classicPointClickRadioTargets =
        new List<ClassicPointClickRadioTarget>();
    readonly List<ClassicPointClickPageRecord> _classicPointClickPageRecords =
        new List<ClassicPointClickPageRecord>();

    GameObject _classicPointClickPageRoot;
    GameObject _classicPointClickPreviousButton;
    GameObject _classicPointClickNextButton;
    Renderer _classicPointClickPreviousRenderer;
    Renderer _classicPointClickNextRenderer;
    TextMesh _classicPointClickPreviousLabel;
    TextMesh _classicPointClickNextLabel;
    TextMesh _classicPointClickStatusText;
    bool _classicPointClickSyntheticScreenshotCaptured;
    bool _comparisonPointClickWasHeld;
    string _comparisonSequenceCode = "not_run";
    readonly Dictionary<string, int> _comparisonPresentationOrderByMethod =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _comparisonTargetFormByMethod =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    int _comparisonQuestionnaireItemLimit;

    const int ComparisonPracticeItemCount = 2;
    const int ComparisonScaleMidpoint = 11;

    protected bool IsQuestionnaireKnobInputActive =>
        questionnaireInputMethod == QuestionnaireInputMethod.PaxsmKnob;

    string CurrentQuestionnaireResponseMode()
    {
        if (questionnaireInputMethod == QuestionnaireInputMethod.PointAndClick)
            return questionnaireComparisonMode
                ? $"classic_xr_radio_panel_{Mathf.Clamp(comparisonPointClickItemsPerPage, 1, 3)}_per_page"
                : "point_and_click_ray";
        return useMainSceneKnobRig
            ? "paxsm_main_scene_knobrig"
            : "paxsm_generated_fallback_knob";
    }

    IEnumerator RunQuestionnaireComparisonExperiment()
    {
        string originalBank = questionnaireBankResourcesPath;
        int originalScale = questionnaireScale;
        bool originalConfidence = collectConfidenceAfterEachItem;
        QuestionnaireInputMethod originalMethod = questionnaireInputMethod;
        int originalItemLimit = _comparisonQuestionnaireItemLimit;

        ComparisonBlockSpec[] sequence = BuildComparisonSequence();
        _runBlockCount = sequence.Length;

        _titleText.text = "Controlled Input-Method Comparison";
        _cueText.text = "You will enter target values with both questionnaire input methods.";
        _statusText.text = $"Order {_comparisonSequenceCode}\nRead the wall, then use the right controller.";
        _timerText.text = "";
        _feedbackText.text = "Press N on the desktop to skip timed instructions.";
        yield return WaitForSecondsOrN(4f);

        for (int i = 0; i < sequence.Length; i++)
        {
            _blockIndex = i;
            ComparisonBlockSpec block = sequence[i];
            string methodKey = ComparisonMethodKey(block.method);
            string methodLabel = ComparisonMethodLabel(block.method);
            string targetForm = block.formIndex == 0 ? "A" : "B";

            questionnaireInputMethod = block.method;
            collectConfidenceAfterEachItem = false;
            yield return RunComparisonMethodTutorial(block);

            questionnaireBankResourcesPath = comparisonPracticeBankResourcesPath;
            _comparisonQuestionnaireItemLimit = ComparisonPracticeItemCount;
            var practiceProfile = new ProbeBlockProfile
            {
                blockId = $"practice_input_{methodKey}",
                displayName = $"Practice with {methodLabel}",
                targetTlxDimension = $"practice_excluded_{methodKey}"
            };
            _titleText.text = $"{methodLabel} practice";
            _cueText.text = $"Complete {ComparisonPracticeItemCount} practice target selections.";
            _statusText.text = "Practice is logged for auditing but excluded from the primary analysis.";
            yield return WaitForSecondsOrN(2.5f);
            yield return RunBlockQuestionnaire(practiceProfile);

            questionnaireInputMethod = block.method;
            questionnaireBankResourcesPath = block.formIndex == 0
                ? comparisonFormalBankAResourcesPath
                : comparisonFormalBankBResourcesPath;
            _comparisonQuestionnaireItemLimit = Mathf.Clamp(comparisonFormalTrialsPerMethod, 8, 12);
            var formalInputProfile = new ProbeBlockProfile
            {
                blockId = $"formal_input_{methodKey}",
                displayName = $"Formal target input with {methodLabel}",
                targetTlxDimension = $"formal_input_performance_{methodKey}"
            };
            _titleText.text = $"Block {i + 1}/{sequence.Length}: {methodLabel}";
            _cueText.text = $"Complete {_comparisonQuestionnaireItemLimit} formal target-value trials.";
            _statusText.text = $"Target order {targetForm}. Use midpoint {ComparisonScaleMidpoint} as the neutral reference.";
            yield return WaitForSecondsOrN(2.5f);
            yield return RunBlockQuestionnaire(formalInputProfile);

            questionnaireInputMethod = block.method;
            questionnaireBankResourcesPath = comparisonWorkloadBankResourcesPath;
            _comparisonQuestionnaireItemLimit = 0;
            collectConfidenceAfterEachItem = comparisonCollectConfidence;
            var workloadProfile = new ProbeBlockProfile
            {
                blockId = ComparisonBlockId(block),
                displayName = ComparisonMethodLabel(block.method),
                targetTlxDimension = "comparison_questionnaire_method"
            };
            _titleText.text = $"Workload rating: {methodLabel}";
            _cueText.text = $"Rate the workload of entering the formal target values with {methodLabel}.";
            _statusText.text = "Answer the six NASA-TLX items about the method you just used.";
            yield return WaitForSecondsOrN(3f);
            yield return RunBlockQuestionnaire(workloadProfile);

            QuestionnaireInputMethod evaluatedMethod = block.method;
            questionnaireInputMethod = QuestionnaireInputMethod.PointAndClick;
            questionnaireBankResourcesPath = comparisonSusBankResourcesPath;
            _comparisonQuestionnaireItemLimit = 0;
            collectConfidenceAfterEachItem = false;
            var susProfile = new ProbeBlockProfile
            {
                blockId = $"sus_{ComparisonMethodKey(evaluatedMethod)}",
                displayName = $"SUS for {ComparisonMethodLabel(evaluatedMethod)}",
                targetTlxDimension = $"usability_{ComparisonMethodKey(evaluatedMethod)}"
            };

            string evaluatedMethodLabel = ComparisonMethodLabel(evaluatedMethod);
            _titleText.text = $"NOW RATING: {evaluatedMethodLabel.ToUpperInvariant()}";
            _cueText.text = $"The following System Usability Scale questions are about the {evaluatedMethodLabel} system.";
            _statusText.text = "Please rate that system only. SUS is answered by pointing and clicking for both systems.";
            yield return WaitForSecondsOrN(3.5f);
            yield return RunBlockQuestionnaire(susProfile);

            if (i < sequence.Length - 1)
                yield return RunComparisonRest(sequence[i + 1].method);
        }

        questionnaireBankResourcesPath = originalBank;
        questionnaireScale = originalScale;
        collectConfidenceAfterEachItem = originalConfidence;
        questionnaireInputMethod = originalMethod;
        _comparisonQuestionnaireItemLimit = originalItemLimit;

        WriteCsvFiles("completed");
        _titleText.text = "Comparison Complete";
        _cueText.text = "All formal input, NASA-TLX, and SUS records have been saved.";
        _statusText.text = $"Saved {CountComparisonFormalRecords()} formal target trials and " +
                           $"{_questionnaireRecords.Count} total records to:\n{GetOutputFolder()}";
        _timerText.text = "";
        _feedbackText.text = "You may now remove the headset.";
    }

    ComparisonBlockSpec[] BuildComparisonSequence()
    {
        int group = (ParseParticipantNumber(participantId) - 1) % 4;
        ComparisonBlockSpec[] sequence;
        switch (group)
        {
            case 1:
                _comparisonSequenceCode = "B: Click-A / PAXSM-B";
                sequence = new[]
                {
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PointAndClick, 0),
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PaxsmKnob, 1)
                };
                break;
            case 2:
                _comparisonSequenceCode = "C: PAXSM-B / Click-A";
                sequence = new[]
                {
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PaxsmKnob, 1),
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PointAndClick, 0)
                };
                break;
            case 3:
                _comparisonSequenceCode = "D: Click-B / PAXSM-A";
                sequence = new[]
                {
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PointAndClick, 1),
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PaxsmKnob, 0)
                };
                break;
            default:
                _comparisonSequenceCode = "A: PAXSM-A / Click-B";
                sequence = new[]
                {
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PaxsmKnob, 0),
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PointAndClick, 1)
                };
                break;
        }

        _comparisonPresentationOrderByMethod.Clear();
        _comparisonTargetFormByMethod.Clear();
        for (int i = 0; i < sequence.Length; i++)
        {
            string method = ComparisonMethodKey(sequence[i].method);
            _comparisonPresentationOrderByMethod[method] = i + 1;
            _comparisonTargetFormByMethod[method] = sequence[i].formIndex == 0 ? "A" : "B";
        }
        return sequence;
    }

    IEnumerator RunComparisonMethodTutorial(ComparisonBlockSpec block)
    {
        string methodLabel = ComparisonMethodLabel(block.method);
        _titleText.text = $"Standardized tutorial: {methodLabel}";
        _cueText.text = block.method == QuestionnaireInputMethod.PaxsmKnob
            ? "Grab the virtual knob, rotate to the requested value, release it, then hold the primary A button to confirm."
            : "Aim the right-controller ray at the requested value and press the Right Trigger to select it.";
        _statusText.text = $"The next {ComparisonPracticeItemCount} trials are practice and are excluded from the primary analysis.";
        _timerText.text = "";
        _feedbackText.text = "Use the same technique for every trial.";
        yield return WaitForSecondsOrN(5f);
    }

    IEnumerator RunComparisonRest(QuestionnaireInputMethod nextMethod)
    {
        _titleText.text = "Rest between methods";
        _cueText.text = $"Please rest before starting {ComparisonMethodLabel(nextMethod)}.";
        _statusText.text = $"The next method begins in {Mathf.CeilToInt(comparisonRestSeconds)} seconds.";
        _timerText.text = "";
        _feedbackText.text = "The experimenter may press N to continue when the participant is ready.";
        yield return WaitForSecondsOrN(Mathf.Max(5f, comparisonRestSeconds));
    }

    IEnumerator WaitForComparisonPointClickRelease()
    {
        while (ComparisonPointClickHeldNow())
            yield return null;
        _comparisonPointClickWasHeld = false;
    }

    bool GetComparisonPointClickPressed(out Ray ray, out Vector3 origin)
    {
        bool hasXrRay = TryGetXrPointer(out ray, out origin, out _);
        bool held = ComparisonPointClickHeldNow();
        if (!hasXrRay)
        {
            ray = GetDesktopPointerRay();
            origin = ray.origin;
        }

        bool pressed = held && !_comparisonPointClickWasHeld;
        _comparisonPointClickWasHeld = held;
        return pressed;
    }

    bool ComparisonPointClickHeldNow()
    {
        bool held = false;
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, _rightHandDevices);
        for (int i = 0; i < _rightHandDevices.Count; i++)
        {
            InputDevice device = _rightHandDevices[i];
            if (!device.isValid)
                continue;
            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger) && trigger)
                held = true;
        }
        return held || DesktopSelectHeld();
    }

    string ComparisonBlockId(ComparisonBlockSpec block)
    {
        return $"comparison_{ComparisonMethodKey(block.method)}";
    }

    string ComparisonMethodKey(QuestionnaireInputMethod method)
    {
        return method == QuestionnaireInputMethod.PaxsmKnob ? "paxsm" : "point_click";
    }

    string ComparisonMethodLabel(QuestionnaireInputMethod method)
    {
        return method == QuestionnaireInputMethod.PaxsmKnob ? "PAXSM knob" : "Point-and-click";
    }

    IEnumerator RunClassicPointClickQuestionnaire(ProbeBlockProfile profile, TlxItem[] items)
    {
        ClearQuestionnaireTicks();
        _questionnaireStageActive = true;
        _questionnaireTitleText.text = "";
        _questionnairePromptText.text = "";
        _questionnaireScaleText.text = "";
        _questionnaireScaleRightText.text = "";
        _questionnaireProgressText.text = "";
        _questionnaireValueText.text = "";

        var responses = new ClassicPointClickResponseState[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            TlxItem item = items[i];
            responses[i] = new ClassicPointClickResponseState
            {
                item = item,
                record = new QuestionnaireRecord
                {
                    blockId = profile.blockId,
                    targetDimension = profile.targetTlxDimension,
                    presentationOrder = _blockIndex + 1,
                    itemIndex = i + 1,
                    itemId = item.itemId,
                    itemDimension = item.dimension,
                    prompt = item.prompt,
                    leftAnchor = item.leftAnchor,
                    rightAnchor = item.rightAnchor,
                    responseMode = CurrentQuestionnaireResponseMode(),
                    scale = questionnaireScale,
                    confidence = -1,
                    readExitEvent = "not_separate_in_classic_panel"
                }
            };
        }

        yield return RunClassicPointClickStagePages(
            profile,
            responses,
            "Answer",
            questionnaireScale);

        if (collectConfidenceAfterEachItem)
        {
            yield return RunClassicPointClickStagePages(
                profile,
                responses,
                "Confidence",
                questionnaireConfidenceScale);
        }

        ClearClassicPointClickPage();
        for (int i = 0; i < responses.Length; i++)
        {
            _questionnaireRecords.Add(responses[i].record);
            WriteQuestionnaireLiveCheckpoint();
        }
        _questionnaireStageActive = false;
    }

    IEnumerator RunClassicPointClickStagePages(
        ProbeBlockProfile profile,
        ClassicPointClickResponseState[] responses,
        string stage,
        int scale)
    {
        int pageSize = Mathf.Clamp(comparisonPointClickItemsPerPage, 1, 3);
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(responses.Length / (float)pageSize));
        int pageIndex = 0;

        while (pageIndex < pageCount)
        {
            int first = pageIndex * pageSize;
            int lastExclusive = Mathf.Min(responses.Length, first + pageSize);
            BuildClassicPointClickPage(profile, responses, stage, scale, pageIndex, pageCount, first, lastExclusive);

            float pageEnter = Time.realtimeSinceStartup;
            int selectionCountAtEnter = ClassicSelectionCount(responses, stage, first, lastExclusive);
            int changeCountAtEnter = ClassicChangeCount(responses, stage, first, lastExclusive);
            for (int i = first; i < lastExclusive; i++)
                BeginClassicPointClickItemStage(responses[i], stage, scale, pageIndex, pageCount, pageEnter);

            yield return WaitForComparisonPointClickRelease();

            string navigationAction = "";
            float pointerPath = 0f;
            float pointerPeakSpeed = 0f;
            int hoverChangeCount = 0;
            int previousHoverKey = int.MinValue;
            bool hasLastPointer = false;
            Vector3 lastPointer = Vector3.zero;
            float lastPointerTime = pageEnter;
            bool pageFinished = false;

#if UNITY_EDITOR
            if (SyntheticParticipantActive)
            {
                if (!_classicPointClickSyntheticScreenshotCaptured &&
                    string.Equals(stage, "Answer", StringComparison.OrdinalIgnoreCase) &&
                    profile.blockId.StartsWith("comparison_", StringComparison.OrdinalIgnoreCase))
                {
                    string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
                    string screenshotPath = Path.Combine(
                        projectRoot,
                        "Temp",
                        $"PAXSMComparison_ClassicPointClick_{participantId}.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath));
                    ScreenCapture.CaptureScreenshot(screenshotPath);
                    _classicPointClickSyntheticScreenshotCaptured = true;
                    yield return new WaitForEndOfFrame();
                    yield return new WaitForSecondsRealtime(0.20f);
                }

                for (int i = first; i < lastExclusive; i++)
                {
                    yield return new WaitForSecondsRealtime(0.06f);
                    int value = SyntheticQuestionnaireTarget(responses[i].record, stage, scale);
                    ApplyClassicPointClickSelection(responses[i], stage, scale, value, pageIndex);
                    UpdateClassicPointClickVisuals(null, false, false, responses, stage);
                }
                yield return new WaitForSecondsRealtime(0.05f);
                navigationAction = pageIndex == pageCount - 1 ? "submit" : "next";
                pageFinished = true;
            }
#endif

            while (!pageFinished)
            {
                bool pressed = GetComparisonPointClickPressed(out Ray ray, out Vector3 pointerOrigin);
                ClassicPointClickRadioTarget hoverTarget = ResolveClassicPointClickRadio(ray);
                bool hoverPrevious = pageIndex > 0 && RayHitsClassicObject(ray, _classicPointClickPreviousButton);
                bool hoverNext = RayHitsClassicObject(ray, _classicPointClickNextButton);
                UpdateClassicPointClickVisuals(hoverTarget, hoverPrevious, hoverNext, responses, stage);
                UpdateSelectionRayVisual(ray);

                int hoverKey = hoverTarget != null
                    ? hoverTarget.responseIndex * 100 + hoverTarget.value
                    : (hoverPrevious ? -2 : (hoverNext ? -3 : -1));
                if (hoverKey != previousHoverKey)
                {
                    if (previousHoverKey != int.MinValue)
                        hoverChangeCount++;
                    previousHoverKey = hoverKey;
                }

                float now = Time.realtimeSinceStartup;
                float dt = Mathf.Max(0.0001f, now - lastPointerTime);
                if (hasLastPointer)
                {
                    float distance = Vector3.Distance(lastPointer, pointerOrigin);
                    pointerPath += distance;
                    pointerPeakSpeed = Mathf.Max(pointerPeakSpeed, distance / dt);
                }
                lastPointer = pointerOrigin;
                lastPointerTime = now;
                hasLastPointer = true;

                if (pressed && hoverTarget != null)
                {
                    ApplyClassicPointClickSelection(
                        responses[hoverTarget.responseIndex],
                        stage,
                        scale,
                        hoverTarget.value,
                        pageIndex);
                    if (_classicPointClickStatusText != null)
                        _classicPointClickStatusText.text = "Response selected. You can change it before continuing.";
                }
                else if (pressed && hoverPrevious)
                {
                    navigationAction = "previous";
                    pageFinished = true;
                }
                else if (pressed && hoverNext)
                {
                    if (ClassicPageAnswered(responses, stage, first, lastExclusive))
                    {
                        navigationAction = pageIndex == pageCount - 1 ? "submit" : "next";
                        pageFinished = true;
                    }
                    else
                    {
                        if (_classicPointClickStatusText != null)
                            _classicPointClickStatusText.text = "Please answer every question on this page.";
                        TryPlayRightHandHaptic(0.14f, 0.04f, force: true);
                    }
                }

                yield return null;
            }

            float pageExit = Time.realtimeSinceStartup;
            int answeredAtExit = ClassicAnsweredCount(responses, stage, first, lastExclusive);
            _classicPointClickPageRecords.Add(new ClassicPointClickPageRecord
            {
                blockId = profile.blockId,
                stage = stage,
                pageIndex = pageIndex + 1,
                pageCount = pageCount,
                visitIndex = ClassicPageVisitCount(profile.blockId, stage, pageIndex + 1) + 1,
                firstItemIndex = first + 1,
                lastItemIndex = lastExclusive,
                requiredResponses = lastExclusive - first,
                answeredAtExit = answeredAtExit,
                enterRealtime = pageEnter,
                exitRealtime = pageExit,
                duration = Mathf.Max(0f, pageExit - pageEnter),
                navigationAction = navigationAction,
                selectionCount = ClassicSelectionCount(responses, stage, first, lastExclusive) - selectionCountAtEnter,
                answerChangeCount = ClassicChangeCount(responses, stage, first, lastExclusive) - changeCountAtEnter,
                pointerPath = pointerPath,
                pointerPeakSpeed = pointerPeakSpeed,
                hoverChangeCount = hoverChangeCount
            });

            CommitClassicPointClickPage(responses, stage, scale, first, lastExclusive, navigationAction);
            ClearClassicPointClickPage();
            if (string.Equals(navigationAction, "previous", StringComparison.Ordinal))
                pageIndex = Mathf.Max(0, pageIndex - 1);
            else
                pageIndex++;
        }

        FinalizeClassicPointClickStage(responses, stage, scale);
    }

    void BeginClassicPointClickItemStage(
        ClassicPointClickResponseState response,
        string stage,
        int scale,
        int pageIndex,
        int pageCount,
        float enterRealtime)
    {
        QuestionnaireRecord record = response.record;
        bool answerStage = string.Equals(stage, "Answer", StringComparison.OrdinalIgnoreCase);
        float existingEnter = answerStage ? record.answerEnterRealtime : record.confidenceEnterRealtime;
        if (existingEnter >= 0f)
            return;

        if (answerStage)
            record.answerEnterRealtime = enterRealtime;
        else
            record.confidenceEnterRealtime = enterRealtime;

        LogQuestionnaireStageEvent(record, stage, "Enter");
        LogQuestionnaireInteractionEvent(
            record,
            stage,
            "StageEnter",
            $"layout=classic_xr_radio_panel;page={pageIndex + 1}/{pageCount};scale={scale}");
    }

    bool ApplyClassicPointClickSelection(
        ClassicPointClickResponseState response,
        string stage,
        int scale,
        int value,
        int pageIndex)
    {
        float now = Time.realtimeSinceStartup;
        bool answerStage = string.Equals(stage, "Answer", StringComparison.OrdinalIgnoreCase);
        int previous = answerStage ? response.answer : response.confidence;
        bool changed = previous > 0 && previous != value;

        if (answerStage)
        {
            response.answer = value;
            response.answerLastSelectionRealtime = now;
            response.answerSelectionCount++;
            if (changed)
                response.answerChangeCount++;
            response.record.selectedScore = value;
            response.record.answerConfirmAttemptCount = response.answerSelectionCount;
            if (response.record.answerFirstInteractionRt < 0f)
                response.record.answerFirstInteractionRt = Mathf.Max(0f, now - response.record.answerEnterRealtime);
        }
        else
        {
            response.confidence = value;
            response.confidenceLastSelectionRealtime = now;
            response.confidenceSelectionCount++;
            if (changed)
                response.confidenceChangeCount++;
            response.record.confidence = value;
            response.record.confidenceConfirmAttemptCount = response.confidenceSelectionCount;
            if (response.record.confidenceFirstInteractionRt < 0f)
                response.record.confidenceFirstInteractionRt = Mathf.Max(0f, now - response.record.confidenceEnterRealtime);
        }

        LogQuestionnaireInteractionEvent(
            response.record,
            stage,
            changed ? "RadioResponseChanged" : "RadioSelected",
            $"value={value};previous={previous};page={pageIndex + 1}");
        TryPlayRightHandHaptic(0.30f, 0.05f, force: true);
        return changed;
    }

    void CommitClassicPointClickPage(
        ClassicPointClickResponseState[] responses,
        string stage,
        int scale,
        int first,
        int lastExclusive,
        string navigationAction)
    {
        for (int i = first; i < lastExclusive; i++)
        {
            QuestionnaireRecord record = responses[i].record;
            LogQuestionnaireInteractionEvent(
                record,
                stage,
                "PageNavigation",
                $"action={navigationAction};scale={scale}");
        }
    }

    void FinalizeClassicPointClickStage(
        ClassicPointClickResponseState[] responses,
        string stage,
        int scale)
    {
        bool answerStage = string.Equals(stage, "Answer", StringComparison.OrdinalIgnoreCase);
        for (int i = 0; i < responses.Length; i++)
        {
            ClassicPointClickResponseState response = responses[i];
            QuestionnaireRecord record = response.record;
            int selected = answerStage ? response.answer : response.confidence;
            float enter = answerStage ? record.answerEnterRealtime : record.confidenceEnterRealtime;
            float exit = answerStage
                ? response.answerLastSelectionRealtime
                : response.confidenceLastSelectionRealtime;
            float duration = enter >= 0f && exit >= enter ? exit - enter : -1f;
            int selectionCount = answerStage
                ? response.answerSelectionCount
                : response.confidenceSelectionCount;

            var metrics = new QuestionnaireStageMetrics
            {
                tickCount = scale,
                currentSlot = selected,
                confirmCount = selectionCount,
                minSlot = selected,
                maxSlot = selected,
                uniqueSlotsVisited = selected > 0 ? 1 : 0,
                speedBandNote = "classic_radio_panel_no_knob_trace"
            };

            if (answerStage)
            {
                record.selectedScore = selected;
                record.answerExitRealtime = exit;
                record.answerRt = duration;
                record.answerDecisionRt = duration;
                record.answerConfirmHoldRt = 0f;
                record.answerConfirmAttemptCount = selectionCount;
                record.answerMetrics = metrics;
            }
            else
            {
                record.confidence = selected;
                record.confidenceExitRealtime = exit;
                record.confidenceRt = duration;
                record.confidenceDecisionRt = duration;
                record.confidenceConfirmHoldRt = 0f;
                record.confidenceConfirmAttemptCount = selectionCount;
                record.confidenceMetrics = metrics;
            }

            LogQuestionnaireStageEvent(record, stage, "Confirm");
            LogQuestionnaireStageEvent(record, stage, "Exit");
            LogQuestionnaireInteractionEvent(record, stage, "StageExit", $"rt={F(duration)};layout=classic_xr_radio_panel");
        }
    }

    void BuildClassicPointClickPage(
        ProbeBlockProfile profile,
        ClassicPointClickResponseState[] responses,
        string stage,
        int scale,
        int pageIndex,
        int pageCount,
        int first,
        int lastExclusive)
    {
        ClearClassicPointClickPage();
        _questionnaireTitleText.text = "";
        _questionnairePromptText.text = "";
        _questionnaireScaleText.text = "";
        _questionnaireScaleRightText.text = "";
        _questionnaireProgressText.text = "";
        _questionnaireValueText.text = "";

        _classicPointClickPageRoot = new GameObject("Classic XR Questionnaire Page");
        _classicPointClickPageRoot.transform.SetParent(_questionnaireRuntimeRoot, false);

        CreateClassicPanelPrimitive(
            "Questionnaire Panel",
            PrimitiveType.Cube,
            new Vector3(0f, 1.69f, 4.235f),
            new Vector3(5.18f, 2.08f, 0.055f),
            _questionnairePanelMaterial,
            false);
        bool isSusPage = profile.blockId.StartsWith("sus_", StringComparison.OrdinalIgnoreCase);
        string evaluatedSystemLabel = isSusPage
            ? ComparisonEvaluatedSystemLabel(profile.blockId)
            : "";

        CreateClassicPanelPrimitive(
            "Questionnaire Header",
            PrimitiveType.Cube,
            new Vector3(0f, 2.58f, 4.19f),
            new Vector3(5.02f, 0.24f, 0.035f),
            isSusPage ? _questionnaireSelectedMaterial : _normalMaterial,
            false);

        string header = string.Equals(stage, "Confidence", StringComparison.OrdinalIgnoreCase)
            ? "Rate your confidence in each response"
            : (isSusPage
                ? $"NOW RATING: {evaluatedSystemLabel}"
                : "Choose one response for each question");
        CreateClassicPanelText(
            "Header Text",
            header,
            new Vector3(0f, 2.59f, 4.14f),
            0.013f,
            TextAnchor.MiddleCenter,
            isSusPage ? new Color(0.08f, 0.10f, 0.12f) : Color.white);
        if (isSusPage)
        {
            CreateClassicPanelText(
                "SUS Context Label",
                $"SUS FOR: {evaluatedSystemLabel}",
                new Vector3(-2.30f, 2.43f, 4.14f),
                0.0075f,
                TextAnchor.MiddleLeft,
                new Color(1.0f, 0.86f, 0.30f));
        }
        CreateClassicPanelText(
            "Page Counter",
            $"Page {pageIndex + 1} / {pageCount}",
            new Vector3(2.30f, 2.43f, 4.14f),
            0.0075f,
            TextAnchor.MiddleRight,
            new Color(0.72f, 0.80f, 0.88f));

        List<string> bankLabels = LoadClassicPointClickScaleLabels();
        bool singleItemPage = lastExclusive - first == 1;
        float promptTop = singleItemPage ? 2.08f : 2.31f;
        float rowSpacing = 0.50f;
        for (int responseIndex = first; responseIndex < lastExclusive; responseIndex++)
        {
            int row = responseIndex - first;
            ClassicPointClickResponseState response = responses[responseIndex];
            float promptY = promptTop - row * rowSpacing;
            float radioY = promptY - (singleItemPage ? 0.34f : 0.19f);
            float labelY = radioY - (singleItemPage ? 0.14f : 0.105f);
            string prompt = string.Equals(stage, "Confidence", StringComparison.OrdinalIgnoreCase)
                ? $"Confidence in your {response.item.dimension} rating"
                : response.item.prompt;

            CreateClassicPanelText(
                $"Question {responseIndex + 1}",
                WrapForWall(prompt, 74),
                new Vector3(0f, promptY, 4.14f),
                0.0095f,
                TextAnchor.MiddleCenter,
                new Color(0.94f, 0.97f, 1f));

            float left = scale <= 7 ? -1.48f : -2.03f;
            float right = scale <= 7 ? 1.48f : 2.03f;
            for (int value = 1; value <= scale; value++)
            {
                float t = scale <= 1 ? 0.5f : (value - 1f) / (scale - 1f);
                float x = Mathf.Lerp(left, right, t);
                CreateClassicRadioTarget(responseIndex, value, scale, new Vector3(x, radioY, 4.125f));

                if (!ShouldShowClassicScaleLabel(scale, value))
                    continue;
                string label = FormatClassicScaleLabel(
                    ClassicScaleLabel(response.item, stage, bankLabels, scale, value));
                CreateClassicPanelText(
                    $"Question {responseIndex + 1} Scale Label {value}",
                    label,
                    new Vector3(x, labelY, 4.13f),
                    scale <= 7 ? 0.0074f : 0.0068f,
                    TextAnchor.UpperCenter,
                    new Color(0.74f, 0.81f, 0.88f));
            }

            if (responseIndex < lastExclusive - 1)
            {
                CreateClassicPanelPrimitive(
                    $"Row Separator {row + 1}",
                    PrimitiveType.Cube,
                    new Vector3(0f, promptY - 0.40f, 4.17f),
                    new Vector3(4.82f, 0.008f, 0.012f),
                    _questionnaireTickMaterial,
                    false);
            }
        }

        _classicPointClickPreviousButton = CreateClassicNavigationButton(
            "Previous",
            new Vector3(-1.92f, 0.72f, 4.13f),
            pageIndex > 0,
            out _classicPointClickPreviousRenderer,
            out _classicPointClickPreviousLabel);
        _classicPointClickNextButton = CreateClassicNavigationButton(
            pageIndex == pageCount - 1 ? "Submit" : "Next",
            new Vector3(1.92f, 0.72f, 4.13f),
            true,
            out _classicPointClickNextRenderer,
            out _classicPointClickNextLabel);

        _classicPointClickStatusText = CreateClassicPanelText(
            "Page Status",
            "Point at a circle and press the Right Trigger.",
            new Vector3(0f, 0.72f, 4.12f),
            0.0070f,
            TextAnchor.MiddleCenter,
            new Color(0.78f, 0.86f, 0.94f));
        UpdateClassicPointClickVisuals(null, false, false, responses, stage);
    }

    string ComparisonEvaluatedSystemLabel(string blockId)
    {
        if (string.Equals(blockId, "sus_paxsm", StringComparison.OrdinalIgnoreCase))
            return "PAXSM VIRTUAL KNOB";
        if (string.Equals(blockId, "sus_point_click", StringComparison.OrdinalIgnoreCase))
            return "POINT-AND-CLICK";
        return "CURRENT SYSTEM";
    }

    void CreateClassicRadioTarget(int responseIndex, int value, int scale, Vector3 position)
    {
        float diameter = comparisonPointClickRadioDiameterMeters;
        if (scale <= 7)
            diameter *= 1.12f;

        GameObject outer = CreateClassicPanelPrimitive(
            $"Question {responseIndex + 1} Radio {value}",
            PrimitiveType.Sphere,
            position,
            new Vector3(diameter, diameter, diameter * 0.42f),
            _questionnaireTickMaterial,
            true);
        Renderer outerRenderer = outer.GetComponent<Renderer>();

        GameObject inner = CreateClassicPanelPrimitive(
            $"Question {responseIndex + 1} Radio {value} Center",
            PrimitiveType.Sphere,
            position + new Vector3(0f, 0f, -0.025f),
            new Vector3(diameter * 0.48f, diameter * 0.48f, diameter * 0.25f),
            _questionnairePanelMaterial,
            false);
        Renderer innerRenderer = inner.GetComponent<Renderer>();

        _classicPointClickRadioTargets.Add(new ClassicPointClickRadioTarget
        {
            responseIndex = responseIndex,
            value = value,
            hitObject = outer,
            outerRenderer = outerRenderer,
            innerRenderer = innerRenderer
        });
    }

    GameObject CreateClassicNavigationButton(
        string label,
        Vector3 position,
        bool enabled,
        out Renderer renderer,
        out TextMesh labelText)
    {
        GameObject button = CreateClassicPanelPrimitive(
            $"{label} Button",
            PrimitiveType.Cube,
            position,
            new Vector3(0.62f, 0.16f, 0.05f),
            enabled ? _questionnaireTickMaterial : _questionnairePanelMaterial,
            enabled);
        renderer = button.GetComponent<Renderer>();
        labelText = CreateClassicPanelText(
            $"{label} Button Label",
            label,
            position + new Vector3(0f, 0f, -0.04f),
            0.0074f,
            TextAnchor.MiddleCenter,
            enabled ? Color.white : new Color(0.43f, 0.48f, 0.54f));
        return button;
    }

    GameObject CreateClassicPanelPrimitive(
        string name,
        PrimitiveType primitiveType,
        Vector3 position,
        Vector3 scale,
        Material material,
        bool colliderEnabled)
    {
        GameObject value = GameObject.CreatePrimitive(primitiveType);
        value.name = name;
        value.transform.SetParent(_classicPointClickPageRoot.transform, false);
        value.transform.position = position;
        value.transform.rotation = Quaternion.identity;
        value.transform.localScale = scale;
        Renderer renderer = value.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;
        Collider collider = value.GetComponent<Collider>();
        if (collider != null)
            collider.enabled = colliderEnabled;
        return value;
    }

    TextMesh CreateClassicPanelText(
        string name,
        string text,
        Vector3 position,
        float characterSize,
        TextAnchor anchor,
        Color color)
    {
        var value = new GameObject(name);
        value.transform.SetParent(_classicPointClickPageRoot.transform, false);
        value.transform.position = position;
        value.transform.rotation = Quaternion.identity;
        TextMesh mesh = value.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.fontSize = 48;
        mesh.characterSize = characterSize;
        mesh.anchor = anchor;
        mesh.alignment = anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.UpperLeft
            ? TextAlignment.Left
            : (anchor == TextAnchor.MiddleRight || anchor == TextAnchor.UpperRight
                ? TextAlignment.Right
                : TextAlignment.Center);
        mesh.color = color;
        return mesh;
    }

    ClassicPointClickRadioTarget ResolveClassicPointClickRadio(Ray ray)
    {
        ClassicPointClickRadioTarget result = null;
        float nearest = float.MaxValue;
        for (int i = 0; i < _classicPointClickRadioTargets.Count; i++)
        {
            ClassicPointClickRadioTarget target = _classicPointClickRadioTargets[i];
            Collider collider = target.hitObject != null ? target.hitObject.GetComponent<Collider>() : null;
            if (collider == null || !collider.Raycast(ray, out RaycastHit hit, selectionMaxDistance))
                continue;
            if (hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            result = target;
        }
        return result;
    }

    bool RayHitsClassicObject(Ray ray, GameObject target)
    {
        if (target == null || !target.activeInHierarchy)
            return false;
        Collider collider = target.GetComponent<Collider>();
        return collider != null && collider.enabled &&
               collider.Raycast(ray, out _, selectionMaxDistance);
    }

    void UpdateClassicPointClickVisuals(
        ClassicPointClickRadioTarget hover,
        bool hoverPrevious,
        bool hoverNext,
        ClassicPointClickResponseState[] responses,
        string stage)
    {
        bool answerStage = string.Equals(stage, "Answer", StringComparison.OrdinalIgnoreCase);
        for (int i = 0; i < _classicPointClickRadioTargets.Count; i++)
        {
            ClassicPointClickRadioTarget target = _classicPointClickRadioTargets[i];
            bool hovered = ReferenceEquals(target, hover);
            int selected = answerStage
                ? responses[target.responseIndex].answer
                : responses[target.responseIndex].confidence;
            if (target.outerRenderer != null)
                target.outerRenderer.sharedMaterial = hovered
                    ? _questionnaireHoverMaterial
                    : _questionnaireTickMaterial;
            if (target.innerRenderer != null)
                target.innerRenderer.sharedMaterial = selected == target.value
                    ? _questionnaireSelectedMaterial
                    : _questionnairePanelMaterial;
        }

        if (_classicPointClickPreviousRenderer != null &&
            _classicPointClickPreviousButton != null &&
            _classicPointClickPreviousButton.GetComponent<Collider>()?.enabled == true)
        {
            _classicPointClickPreviousRenderer.sharedMaterial = hoverPrevious
                ? _questionnaireHoverMaterial
                : _questionnaireTickMaterial;
        }
        if (_classicPointClickNextRenderer != null)
        {
            _classicPointClickNextRenderer.sharedMaterial = hoverNext
                ? _questionnaireHoverMaterial
                : _questionnaireTickMaterial;
        }
    }

    List<string> LoadClassicPointClickScaleLabels()
    {
        TextAsset bankAsset = Resources.Load<TextAsset>(questionnaireBankResourcesPath);
        if (bankAsset == null)
            return new List<string>();
        try
        {
            LikertSurveyConfig bank = JsonUtility.FromJson<LikertSurveyConfig>(bankAsset.text);
            return bank?.labels != null ? new List<string>(bank.labels) : new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    bool ShouldShowClassicScaleLabel(int scale, int value)
    {
        if (scale <= 7)
            return true;
        if (scale == 21)
            return (value - 1) % 5 == 0;
        return value == 1 || value == scale || value == Mathf.CeilToInt(scale * 0.5f);
    }

    string ClassicScaleLabel(
        TlxItem item,
        string stage,
        List<string> bankLabels,
        int scale,
        int value)
    {
        if (string.Equals(stage, "Confidence", StringComparison.OrdinalIgnoreCase) && scale == 5)
        {
            string[] confidenceLabels =
            {
                "Not confident", "Slightly", "Moderately", "Confident", "Very confident"
            };
            return confidenceLabels[value - 1];
        }
        if (scale <= 7 && bankLabels.Count == scale)
            return bankLabels[value - 1];
        if (value == 1)
            return $"1\n{item.leftAnchor}";
        if (value == scale)
            return $"{scale}\n{item.rightAnchor}";
        return value.ToString();
    }

    string FormatClassicScaleLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "";

        string[] sourceLines = label.Split('\n');
        for (int lineIndex = 0; lineIndex < sourceLines.Length; lineIndex++)
        {
            string line = sourceLines[lineIndex].Trim();
            if (line.Length <= 11 || !line.Contains(" "))
            {
                sourceLines[lineIndex] = line;
                continue;
            }

            int midpoint = line.Length / 2;
            int bestSpace = -1;
            int bestDistance = int.MaxValue;
            for (int i = 1; i < line.Length - 1; i++)
            {
                if (line[i] != ' ')
                    continue;
                int distance = Mathf.Abs(i - midpoint);
                if (distance >= bestDistance)
                    continue;
                bestSpace = i;
                bestDistance = distance;
            }

            sourceLines[lineIndex] = bestSpace > 0
                ? line.Substring(0, bestSpace) + "\n" + line.Substring(bestSpace + 1)
                : line;
        }
        return string.Join("\n", sourceLines);
    }

    bool ClassicPageAnswered(
        ClassicPointClickResponseState[] responses,
        string stage,
        int first,
        int lastExclusive)
    {
        return ClassicAnsweredCount(responses, stage, first, lastExclusive) == lastExclusive - first;
    }

    int ClassicAnsweredCount(
        ClassicPointClickResponseState[] responses,
        string stage,
        int first,
        int lastExclusive)
    {
        bool answerStage = string.Equals(stage, "Answer", StringComparison.OrdinalIgnoreCase);
        int count = 0;
        for (int i = first; i < lastExclusive; i++)
        {
            int selected = answerStage ? responses[i].answer : responses[i].confidence;
            if (selected > 0)
                count++;
        }
        return count;
    }

    int ClassicSelectionCount(
        ClassicPointClickResponseState[] responses,
        string stage,
        int first,
        int lastExclusive)
    {
        bool answerStage = string.Equals(stage, "Answer", StringComparison.OrdinalIgnoreCase);
        int count = 0;
        for (int i = first; i < lastExclusive; i++)
            count += answerStage ? responses[i].answerSelectionCount : responses[i].confidenceSelectionCount;
        return count;
    }

    int ClassicChangeCount(
        ClassicPointClickResponseState[] responses,
        string stage,
        int first,
        int lastExclusive)
    {
        bool answerStage = string.Equals(stage, "Answer", StringComparison.OrdinalIgnoreCase);
        int changes = 0;
        for (int i = first; i < lastExclusive; i++)
            changes += answerStage ? responses[i].answerChangeCount : responses[i].confidenceChangeCount;
        return changes;
    }

    int ClassicPageVisitCount(string blockId, string stage, int pageIndex)
    {
        int count = 0;
        for (int i = 0; i < _classicPointClickPageRecords.Count; i++)
        {
            ClassicPointClickPageRecord record = _classicPointClickPageRecords[i];
            if (string.Equals(record.blockId, blockId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.stage, stage, StringComparison.OrdinalIgnoreCase) &&
                record.pageIndex == pageIndex)
                count++;
        }
        return count;
    }

    void ClearClassicPointClickPage()
    {
        if (_classicPointClickPageRoot != null)
        {
            _classicPointClickPageRoot.SetActive(false);
            Destroy(_classicPointClickPageRoot);
        }
        _classicPointClickPageRoot = null;
        _classicPointClickRadioTargets.Clear();
        _classicPointClickPreviousButton = null;
        _classicPointClickNextButton = null;
        _classicPointClickPreviousRenderer = null;
        _classicPointClickNextRenderer = null;
        _classicPointClickPreviousLabel = null;
        _classicPointClickNextLabel = null;
        _classicPointClickStatusText = null;
    }

    void RegisterQuestionnairePointClickTarget(GameObject tick, int value, int scale)
    {
        if (tick == null)
            return;
        tick.name = $"PointClickScaleOption_{value:00}";
        _questionnairePointClickTargets.Add(tick);

        bool major = scale <= 7 || value == 1 || value == scale ||
                     value == Mathf.CeilToInt(scale * 0.5f) ||
                     (scale == 21 && (value - 1) % 5 == 0);
        if (!major)
            return;

        GameObject labelObject = new GameObject($"PointClickScaleLabel_{value:00}");
        labelObject.transform.SetParent(_questionnaireRuntimeRoot, false);
        labelObject.transform.position = tick.transform.position + new Vector3(0f, 0.22f, -0.045f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = value.ToString();
        label.fontSize = 36;
        label.characterSize = scale >= 15 ? 0.0065f : 0.009f;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.color = new Color(0.76f, 0.84f, 0.92f);
        _questionnairePointClickLabels.Add(labelObject);
    }

    Vector3 QuestionnairePointClickTickScale(int scale, bool hover, bool selected)
    {
        float spacing = scale <= 1 ? 4.1f : 4.1f / (scale - 1f);
        float baseWidth = Mathf.Clamp(spacing * 0.64f, 0.11f, 0.46f);
        float width = selected ? baseWidth * 1.22f : (hover ? baseWidth * 1.14f : baseWidth);
        float height = selected ? 0.36f : (hover ? 0.33f : 0.28f);
        return new Vector3(width, height, 0.055f);
    }

    int ResolveQuestionnairePointClickValue(Ray ray)
    {
        int result = -1;
        float nearest = float.MaxValue;
        for (int i = 0; i < _questionnairePointClickTargets.Count; i++)
        {
            GameObject target = _questionnairePointClickTargets[i];
            Collider collider = target != null ? target.GetComponent<Collider>() : null;
            if (collider == null || !collider.Raycast(ray, out RaycastHit hit, selectionMaxDistance))
                continue;
            if (hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            result = i + 1;
        }
        return result;
    }

    void ClearQuestionnairePointClickTargets()
    {
        ClearClassicPointClickPage();
        _questionnairePointClickTargets.Clear();
        for (int i = _questionnairePointClickLabels.Count - 1; i >= 0; i--)
        {
            if (_questionnairePointClickLabels[i] != null)
                Destroy(_questionnairePointClickLabels[i]);
        }
        _questionnairePointClickLabels.Clear();
        _comparisonPointClickWasHeld = false;
    }

    void WriteComparisonOutputs(string folder, string stamp, string reason, List<string> savedPaths)
    {
        if (!questionnaireComparisonMode)
            return;

        string suffix = $"{participantId}_{stamp}_{reason}";
        string formalInputPath = Path.Combine(folder, $"PAXSMComparison_FormalInput_{suffix}.csv");
        string practicePath = Path.Combine(folder, $"PAXSMComparison_Practice_{suffix}.csv");
        string susPath = Path.Combine(folder, $"PAXSMComparison_SUS_{suffix}.csv");
        string pointClickPagesPath = Path.Combine(folder, $"PAXSMComparison_PointClickPages_{suffix}.csv");
        string manifestPath = Path.Combine(folder, $"PAXSMComparison_Manifest_{suffix}.csv");
        DirectoryInfo rawDataParent = Directory.GetParent(Path.GetFullPath(folder));
        string analysisFolder = rawDataParent != null ? rawDataParent.FullName : folder;
        Directory.CreateDirectory(analysisFolder);
        string analysisReadyPath = Path.Combine(
            analysisFolder,
            $"PAXSMComparison_AnalysisReady_{suffix}.csv");
        File.WriteAllText(formalInputPath, BuildComparisonTargetInputCsv("formal_input_", "formal"), Encoding.UTF8);
        File.WriteAllText(practicePath, BuildComparisonTargetInputCsv("practice_input_", "practice"), Encoding.UTF8);
        File.WriteAllText(susPath, BuildComparisonSusCsv(), Encoding.UTF8);
        File.WriteAllText(pointClickPagesPath, BuildComparisonPointClickPagesCsv(), Encoding.UTF8);
        File.WriteAllText(manifestPath, BuildComparisonManifestCsv(), Encoding.UTF8);
        File.WriteAllText(analysisReadyPath, BuildComparisonAnalysisReadyCsv(reason), Encoding.UTF8);
        savedPaths.Add(formalInputPath);
        savedPaths.Add(practicePath);
        savedPaths.Add(susPath);
        savedPaths.Add(pointClickPagesPath);
        savedPaths.Add(manifestPath);
        savedPaths.Add(analysisReadyPath);
    }

    string BuildComparisonPointClickPagesCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "participantId,sessionNumber,conditionLabel,blockId,stage,pageIndex,pageCount,visitIndex," +
            "firstItemIndex,lastItemIndex,requiredResponses,answeredAtExit,enterRealtime,exitRealtime," +
            "pageRt,navigationAction,selectionCount,answerChangeCount,pointerPath,pointerPeakSpeed,hoverChangeCount");
        for (int i = 0; i < _classicPointClickPageRecords.Count; i++)
        {
            ClassicPointClickPageRecord r = _classicPointClickPageRecords[i];
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(r.blockId), Csv(r.stage),
                I(r.pageIndex), I(r.pageCount), I(r.visitIndex), I(r.firstItemIndex), I(r.lastItemIndex),
                I(r.requiredResponses), I(r.answeredAtExit), F(r.enterRealtime), F(r.exitRealtime), F(r.duration),
                Csv(r.navigationAction), I(r.selectionCount), I(r.answerChangeCount), F(r.pointerPath),
                F(r.pointerPeakSpeed), I(r.hoverChangeCount)
            }));
        }
        return sb.ToString();
    }

    string BuildComparisonSusCsv()
    {
        var contributions = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            if (record == null || !record.blockId.StartsWith("sus_", StringComparison.OrdinalIgnoreCase) ||
                record.selectedScore < 1 || record.selectedScore > 5)
                continue;

            int itemNumber = record.itemIndex;
            float contribution = itemNumber % 2 == 1
                ? record.selectedScore - 1f
                : 5f - record.selectedScore;
            contributions.TryGetValue(record.blockId, out float sum);
            counts.TryGetValue(record.blockId, out int count);
            contributions[record.blockId] = sum + contribution;
            counts[record.blockId] = count + 1;
        }

        var sb = new StringBuilder();
        sb.AppendLine("participantId,sessionNumber,conditionLabel,evaluatedMethod,answeredItems,susScore,complete,scoringNote");
        foreach (KeyValuePair<string, float> pair in contributions)
        {
            int count = counts[pair.Key];
            string method = pair.Key.Substring("sus_".Length);
            float score = pair.Value * 2.5f;
            sb.AppendLine(string.Join(",",
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(method), I(count),
                F(score), B(count == 10), Csv("Odd items: response-1; even items: 5-response; sum x 2.5")));
        }
        return sb.ToString();
    }

    string BuildComparisonTargetInputCsv(string blockPrefix, string trialType)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "participantId,sessionNumber,conditionLabel,presentationOrder,sequenceCode,method,targetOrderForm," +
            "trialType,itemIndex,itemId,referenceValue,targetValue,targetDistanceFromCenter,selectedValue," +
            "completed,absoluteError,exactMatch,responseMode,completionTime,firstInteractionRt," +
            "correctionOccurred,correctionCount,correctionDefinition,confirmAttempts,confirmCancels");
        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            if (record == null ||
                !record.blockId.StartsWith(blockPrefix, StringComparison.OrdinalIgnoreCase) ||
                !TryParseComparisonTarget(record.itemId, out int target))
                continue;

            string method = record.blockId.Substring(blockPrefix.Length);
            bool completed = record.selectedScore > 0;
            int absoluteError = record.selectedScore > 0
                ? Mathf.Abs(record.selectedScore - target)
                : -1;
            int correctionCount = ComparisonCorrectionCount(record, method);
            sb.AppendLine(string.Join(",",
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()),
                I(FirstComparisonPresentationOrder(method)), Csv(_comparisonSequenceCode), Csv(method),
                Csv(string.Equals(trialType, "formal", StringComparison.OrdinalIgnoreCase)
                    ? FirstComparisonTaskForm(method)
                    : "practice"),
                Csv(trialType), I(record.itemIndex), Csv(record.itemId), I(ComparisonScaleMidpoint), I(target),
                I(Mathf.Abs(target - ComparisonScaleMidpoint)), I(record.selectedScore), B(completed),
                I(absoluteError), B(completed && record.selectedScore == target), Csv(record.responseMode),
                F(record.answerRt), F(record.answerFirstInteractionRt), B(correctionCount > 0),
                I(correctionCount), Csv(ComparisonCorrectionDefinition(method)),
                I(record.answerConfirmAttemptCount), I(record.answerConfirmCancelCount)));
        }
        return sb.ToString();
    }

    bool TryParseComparisonTarget(string itemId, out int target)
    {
        target = -1;
        if (string.IsNullOrWhiteSpace(itemId))
            return false;
        int separator = itemId.LastIndexOf('_');
        return separator >= 0 && separator + 1 < itemId.Length &&
               int.TryParse(itemId.Substring(separator + 1), out target);
    }

    int ComparisonCorrectionCount(QuestionnaireRecord record, string method)
    {
        if (record == null)
            return 0;
        return string.Equals(method, "point_click", StringComparison.OrdinalIgnoreCase)
            ? Mathf.Max(0, record.answerConfirmAttemptCount - 1)
            : Mathf.Max(0, record.answerReverseCount);
    }

    string ComparisonCorrectionDefinition(string method)
    {
        return string.Equals(method, "point_click", StringComparison.OrdinalIgnoreCase)
            ? "selection_change_after_first_selection"
            : "knob_direction_reversal_before_confirmation";
    }

    string BuildComparisonManifestCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "designSchemaVersion,participantId,sessionNumber,conditionLabel,studyDesign,independentVariable," +
            "methodLevels,sequenceCode,targetOrderForms,practiceItemsPerMethod,practiceIncludedInPrimaryAnalysis," +
            "formalItemsPerMethod,scaleReferenceValue,scaleMinimum,scaleMaximum,practiceBank,formalBankA,formalBankB," +
            "workloadBank,susBank,restSeconds,confidenceAfterWorkload,pointClickLayout,pointClickItemsPerPage," +
            "pointClickRadioDiameterM,pointClickConfirmation,paxsmConfirmation");
        sb.AppendLine(string.Join(",",
            Csv("CAREXR_PAXSMComparison_Design_v4"), Csv(participantId), I(sessionNumber),
            Csv(EffectiveConditionLabel()), Csv("within_subject_single_factor"),
            Csv("questionnaire_interaction_method"), Csv("paxsm|point_click"), Csv(_comparisonSequenceCode),
            Csv("A|B"), I(ComparisonPracticeItemCount), B(false),
            I(Mathf.Clamp(comparisonFormalTrialsPerMethod, 8, 12)), I(ComparisonScaleMidpoint), I(1), I(21),
            Csv(comparisonPracticeBankResourcesPath), Csv(comparisonFormalBankAResourcesPath),
            Csv(comparisonFormalBankBResourcesPath), Csv(comparisonWorkloadBankResourcesPath),
            Csv(comparisonSusBankResourcesPath), F(Mathf.Max(5f, comparisonRestSeconds)),
            B(comparisonCollectConfidence), Csv("classic_xr_radio_panel"),
            I(Mathf.Clamp(comparisonPointClickItemsPerPage, 1, 3)), F(comparisonPointClickRadioDiameterMeters),
            Csv("radio_dot_selection_then_page_navigation"),
            Csv($"hold_primary_A_{F(questionnaireConfirmHoldSeconds)}s")));
        return sb.ToString();
    }

    string BuildComparisonAnalysisReadyCsv(string exportReason)
    {
        var headers = new List<string>
        {
            "analysisSchemaVersion", "participantId", "sessionNumber", "conditionLabel", "runId",
            "sequenceCode", "method", "methodPresentationOrder", "targetOrderForm",
            "methodComplete", "qualityFlags", "exportReason", "generatedUtc",
            "formalResponseMode", "formalItems", "formalCompletedItems", "formalCompletionRate",
            "formalExactMatches", "formalAccuracy", "formalMeanAbsoluteError", "formalMaxAbsoluteError",
            "formalMeanCompletionTime", "formalMedianCompletionTime", "formalSdCompletionTime",
            "formalTotalCompletionTime", "formalValidFirstInteractionItems", "formalMeanFirstInteractionRt",
            "formalCorrectedTrials", "formalCorrectionRate", "formalCorrectionEvents",
            "formalConfirmAttempts", "formalConfirmCancels", "formalIncompleteItems",
            "susAnsweredItems", "susScore", "susComplete", "susTotalRt",
            "sus01", "sus02", "sus03", "sus04", "sus05", "sus06", "sus07", "sus08", "sus09", "sus10"
        };
        AddComparisonMethodAnalysisHeaders(headers);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers));
        string generatedUtc = DateTime.UtcNow.ToString("O");
        string[] methods = { "paxsm", "point_click" };
        for (int i = 0; i < methods.Length; i++)
        {
            string method = methods[i];
            string qualityFlags = BuildComparisonMethodQualityFlags(method);
            var cells = new List<string>
            {
                Csv("CAREXR_PAXSMComparison_AnalysisReady_v4"),
                Csv(participantId),
                I(sessionNumber),
                Csv(EffectiveConditionLabel()),
                Csv(ExperimentRunContext.RunId),
                Csv(_comparisonSequenceCode),
                Csv(method),
                OptionalInt(FirstComparisonPresentationOrder(method)),
                Csv(FirstComparisonTaskForm(method)),
                B(string.Equals(qualityFlags, "none", StringComparison.Ordinal)),
                Csv(qualityFlags),
                Csv(exportReason),
                Csv(generatedUtc)
            };

            AddComparisonFormalInputAnalysisCells(cells, method);
            AddComparisonSusAnalysisCells(cells, method);
            AddComparisonMethodAnalysisCells(cells, method);
            sb.AppendLine(string.Join(",", cells));
        }
        return sb.ToString();
    }

    void AddComparisonMethodAnalysisHeaders(List<string> headers)
    {
        headers.AddRange(new[]
        {
            "nasaResponseMode", "nasaItems", "nasaComplete",
            "nasaMental", "nasaPhysical", "nasaTemporal", "nasaPerformance",
            "nasaPerformanceWorkloadCoded", "nasaEffort", "nasaFrustration", "nasaRawMean",
            "questionnaireMeanReadRt", "questionnaireMeanAnswerRt",
            "questionnaireMeanAnswerDecisionRt", "questionnaireTotalRt",
            "answerConfirmAttempts", "answerConfirmCancels"
        });
    }

    void AddComparisonFormalInputAnalysisCells(List<string> cells, string method)
    {
        string blockId = "formal_input_" + method;
        string responseMode = "";
        int items = 0;
        int completed = 0;
        int exact = 0;
        int incomplete = 0;
        int correctedTrials = 0;
        int correctionEvents = 0;
        int confirmAttempts = 0;
        int confirmCancels = 0;
        float absoluteErrorSum = 0f;
        int maxAbsoluteError = 0;
        var answerRts = new List<float>();
        var firstInteractionRts = new List<float>();

        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            if (record == null || !string.Equals(record.blockId, blockId, StringComparison.OrdinalIgnoreCase))
                continue;
            items++;
            if (string.IsNullOrWhiteSpace(responseMode))
                responseMode = record.responseMode;
            if (!TryParseComparisonTarget(record.itemId, out int target) || record.selectedScore <= 0)
            {
                incomplete++;
            }
            else
            {
                completed++;
                int error = Mathf.Abs(record.selectedScore - target);
                absoluteErrorSum += error;
                maxAbsoluteError = Mathf.Max(maxAbsoluteError, error);
                if (error == 0) exact++;
            }
            int corrections = ComparisonCorrectionCount(record, method);
            correctionEvents += corrections;
            if (corrections > 0) correctedTrials++;
            if (record.answerRt >= 0f) answerRts.Add(record.answerRt);
            if (record.answerFirstInteractionRt >= 0f) firstInteractionRts.Add(record.answerFirstInteractionRt);
            confirmAttempts += record.answerConfirmAttemptCount;
            confirmCancels += record.answerConfirmCancelCount;
        }

        cells.Add(Csv(responseMode));
        cells.Add(I(items));
        cells.Add(I(completed));
        cells.Add(OptionalRatio(completed, items));
        cells.Add(I(exact));
        cells.Add(OptionalRatio(exact, completed));
        cells.Add(completed > 0 ? F(absoluteErrorSum / completed) : "");
        cells.Add(completed > 0 ? I(maxAbsoluteError) : "");
        cells.Add(OptionalMean(answerRts));
        cells.Add(OptionalMedian(answerRts));
        cells.Add(OptionalSampleSd(answerRts));
        cells.Add(answerRts.Count > 0 ? F(SumNonNegative(answerRts)) : "");
        cells.Add(I(firstInteractionRts.Count));
        cells.Add(OptionalMean(firstInteractionRts));
        cells.Add(I(correctedTrials));
        cells.Add(OptionalRatio(correctedTrials, completed));
        cells.Add(I(correctionEvents));
        cells.Add(I(confirmAttempts));
        cells.Add(I(confirmCancels));
        cells.Add(I(incomplete));
    }

    void AddComparisonSusAnalysisCells(List<string> cells, string method)
    {
        string blockId = "sus_" + method;
        var responses = new int[10];
        var seen = new bool[10];
        int answered = 0;
        float contributionSum = 0f;
        float itemTimingSum = 0f;

        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            if (record == null || !string.Equals(record.blockId, blockId, StringComparison.OrdinalIgnoreCase) ||
                record.itemIndex < 1 || record.itemIndex > 10 || record.selectedScore < 1 || record.selectedScore > 5)
                continue;
            int index = record.itemIndex - 1;
            if (!seen[index])
            {
                seen[index] = true;
                answered++;
                contributionSum += record.itemIndex % 2 == 1
                    ? record.selectedScore - 1f
                    : 5f - record.selectedScore;
            }
            responses[index] = record.selectedScore;
            itemTimingSum += Mathf.Max(0f, record.readRt) + Mathf.Max(0f, record.answerRt);
        }

        float pageTiming = ClassicPointClickStageTotalRt(blockId, "Answer");
        float totalRt = pageTiming >= 0f ? pageTiming : itemTimingSum;

        cells.Add(I(answered));
        cells.Add(answered > 0 ? F(contributionSum * 2.5f) : "");
        cells.Add(B(answered == 10));
        cells.Add(answered > 0 ? F(totalRt) : "");
        for (int i = 0; i < responses.Length; i++)
            cells.Add(seen[i] ? I(responses[i]) : "");
    }

    void AddComparisonMethodAnalysisCells(List<string> cells, string method)
    {
        string blockId = $"comparison_{method}";
        QuestionnaireRecord[] nasa = new QuestionnaireRecord[6];
        string responseMode = "";
        int nasaItems = 0;
        int answerConfirmAttempts = 0;
        int answerConfirmCancels = 0;
        var readRts = new List<float>();
        var answerRts = new List<float>();
        var answerDecisionRts = new List<float>();

        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            if (record == null || !string.Equals(record.blockId, blockId, StringComparison.OrdinalIgnoreCase))
                continue;
            int dimensionIndex = ComparisonNasaDimensionIndex(record.itemId);
            if (dimensionIndex < 0 || nasa[dimensionIndex] != null)
                continue;
            nasa[dimensionIndex] = record;
            nasaItems++;
            if (string.IsNullOrWhiteSpace(responseMode)) responseMode = record.responseMode;
            if (record.readRt >= 0f) readRts.Add(record.readRt);
            if (record.answerRt >= 0f) answerRts.Add(record.answerRt);
            if (record.answerDecisionRt >= 0f) answerDecisionRts.Add(record.answerDecisionRt);
            answerConfirmAttempts += record.answerConfirmAttemptCount;
            answerConfirmCancels += record.answerConfirmCancelCount;
        }

        cells.Add(Csv(responseMode));
        cells.Add(I(nasaItems));
        cells.Add(B(nasaItems == 6));
        float nasaSum = 0f;
        int validNasa = 0;
        for (int i = 0; i < nasa.Length; i++)
        {
            bool valid = nasa[i] != null && nasa[i].selectedScore >= 1;
            if (valid)
            {
                nasaSum += i == 3
                    ? nasa[i].scale + 1 - nasa[i].selectedScore
                    : nasa[i].selectedScore;
                validNasa++;
            }
        }
        for (int i = 0; i < 4; i++)
            cells.Add(nasa[i] != null && nasa[i].selectedScore >= 1 ? I(nasa[i].selectedScore) : "");
        bool performanceValid = nasa[3] != null && nasa[3].selectedScore >= 1;
        cells.Add(performanceValid ? I(nasa[3].scale + 1 - nasa[3].selectedScore) : "");
        for (int i = 4; i < nasa.Length; i++)
            cells.Add(nasa[i] != null && nasa[i].selectedScore >= 1 ? I(nasa[i].selectedScore) : "");
        cells.Add(validNasa > 0 ? F(nasaSum / validNasa) : "");
        cells.Add(OptionalMean(readRts));
        cells.Add(OptionalMean(answerRts));
        cells.Add(OptionalMean(answerDecisionRts));
        float questionnaireTotalRt = SumNonNegative(readRts) + SumNonNegative(answerRts);
        float answerPageRt = ClassicPointClickStageTotalRt(blockId, "Answer");
        if (answerPageRt >= 0f)
            questionnaireTotalRt = answerPageRt;
        cells.Add(nasaItems > 0 ? F(questionnaireTotalRt) : "");
        cells.Add(I(answerConfirmAttempts));
        cells.Add(I(answerConfirmCancels));
    }

    string BuildComparisonMethodQualityFlags(string method)
    {
        var flags = new List<string>();
        int practiceItems = CountComparisonQuestionnaireItems("practice_input_" + method, false, out _);
        int formalItems = CountComparisonQuestionnaireItems("formal_input_" + method, false, out _);
        int susItems = CountComparisonQuestionnaireItems("sus_" + method, false, out _);
        if (practiceItems < ComparisonPracticeItemCount)
            flags.Add($"practice_items_{practiceItems}_minimum_{ComparisonPracticeItemCount}");
        int expectedFormalItems = Mathf.Clamp(comparisonFormalTrialsPerMethod, 8, 12);
        if (formalItems != expectedFormalItems)
            flags.Add($"formal_items_{formalItems}_of_{expectedFormalItems}");
        if (susItems != 10) flags.Add($"sus_items_{susItems}_of_10");

        int nasaItems = CountComparisonQuestionnaireItems($"comparison_{method}", true, out int confidenceItems);
        if (nasaItems != 6) flags.Add($"nasa_items_{nasaItems}_of_6");
        if (comparisonCollectConfidence && confidenceItems != 6)
            flags.Add($"confidence_items_{confidenceItems}_of_6");
        return flags.Count == 0 ? "none" : string.Join(";", flags);
    }

    int CountComparisonQuestionnaireItems(string blockId, bool nasaOnly, out int confidenceItems)
    {
        int count = 0;
        confidenceItems = 0;
        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            if (record == null || !string.Equals(record.blockId, blockId, StringComparison.OrdinalIgnoreCase) ||
                record.selectedScore < 1 || record.selectedScore > record.scale ||
                (nasaOnly && ComparisonNasaDimensionIndex(record.itemId) < 0))
                continue;
            count++;
            if (record.confidence >= 1 && record.confidence <= 5) confidenceItems++;
        }
        return count;
    }

    int FirstComparisonPresentationOrder(string method)
    {
        return _comparisonPresentationOrderByMethod.TryGetValue(method, out int order) ? order : -1;
    }

    string FirstComparisonTaskForm(string method)
    {
        return _comparisonTargetFormByMethod.TryGetValue(method, out string form) ? form : "";
    }

    int CountComparisonFormalRecords()
    {
        int count = 0;
        for (int i = 0; i < _questionnaireRecords.Count; i++)
            if (_questionnaireRecords[i] != null &&
                _questionnaireRecords[i].blockId.StartsWith("formal_input_", StringComparison.OrdinalIgnoreCase))
                count++;
        return count;
    }

    int ComparisonNasaDimensionIndex(string itemId)
    {
        if (string.Equals(itemId, "nasa_tlx_mental", StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(itemId, "nasa_tlx_physical", StringComparison.OrdinalIgnoreCase)) return 1;
        if (string.Equals(itemId, "nasa_tlx_temporal", StringComparison.OrdinalIgnoreCase)) return 2;
        if (string.Equals(itemId, "nasa_tlx_performance", StringComparison.OrdinalIgnoreCase)) return 3;
        if (string.Equals(itemId, "nasa_tlx_effort", StringComparison.OrdinalIgnoreCase)) return 4;
        if (string.Equals(itemId, "nasa_tlx_frustration", StringComparison.OrdinalIgnoreCase)) return 5;
        return -1;
    }

    float ClassicPointClickStageTotalRt(string blockId, string stage)
    {
        float total = 0f;
        int pages = 0;
        for (int i = 0; i < _classicPointClickPageRecords.Count; i++)
        {
            ClassicPointClickPageRecord record = _classicPointClickPageRecords[i];
            if (!string.Equals(record.blockId, blockId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(record.stage, stage, StringComparison.OrdinalIgnoreCase))
                continue;
            total += Mathf.Max(0f, record.duration);
            pages++;
        }
        return pages > 0 ? total : -1f;
    }

    string OptionalInt(int value)
    {
        return value >= 0 ? I(value) : "";
    }

    string OptionalRatio(int numerator, int denominator)
    {
        return denominator > 0 ? F((float)numerator / denominator) : "";
    }

    string OptionalMean(List<float> values)
    {
        if (values == null || values.Count == 0) return "";
        return F(SumNonNegative(values) / values.Count);
    }

    string OptionalMedian(List<float> values)
    {
        if (values == null || values.Count == 0) return "";
        var sorted = new List<float>(values);
        sorted.Sort();
        int middle = sorted.Count / 2;
        float median = sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) * 0.5f;
        return F(median);
    }

    string OptionalSampleSd(List<float> values)
    {
        if (values == null || values.Count < 2) return "";
        float mean = SumNonNegative(values) / values.Count;
        float sumSquares = 0f;
        for (int i = 0; i < values.Count; i++)
        {
            float delta = values[i] - mean;
            sumSquares += delta * delta;
        }
        return F(Mathf.Sqrt(sumSquares / (values.Count - 1)));
    }

    float SumNonNegative(List<float> values)
    {
        float sum = 0f;
        if (values == null) return sum;
        for (int i = 0; i < values.Count; i++)
            sum += Mathf.Max(0f, values[i]);
        return sum;
    }
}
