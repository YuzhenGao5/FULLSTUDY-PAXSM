using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;
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

    struct ComparisonMathQuestion
    {
        public string problem;
        public int correctAnswer;
        public int[] options;

        public ComparisonMathQuestion(string problem, int correctAnswer, params int[] options)
        {
            this.problem = problem;
            this.correctAnswer = correctAnswer;
            this.options = options;
        }
    }

    class ComparisonTaskRecord
    {
        public int presentationOrder;
        public string orderCode = "";
        public string method = "";
        public string taskDemand = "";
        public string form = "";
        public int trialIndex;
        public string problem = "";
        public string options = "";
        public int correctAnswer;
        public int selectedAnswer = -1;
        public int selectedIndex = -1;
        public bool isCorrect;
        public bool timeout;
        public float decisionRt;
        public float pointerPath;
        public float pointerPeakSpeed;
        public int hoverChangeCount;
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
    public string comparisonInputCheckBankResourcesPath = "QuestionBanks/Comparison_Input_Check_21";
    [FormerlySerializedAs("comparisonArithmeticTrialsPerBlock")]
    [Range(2, 4)] public int comparisonArithmeticTrialsPerMethod = 4;
    [Range(10f, 60f)] public float comparisonTrialTimeoutSeconds = 25f;
    public bool comparisonCollectConfidence = true;

    [Header("Classic Point-and-click Baseline")]
    [Range(1, 3)] public int comparisonPointClickItemsPerPage = 1;
    [Range(0.035f, 0.09f)] public float comparisonPointClickRadioDiameterMeters = 0.055f;

    readonly List<ComparisonTaskRecord> _comparisonTaskRecords = new List<ComparisonTaskRecord>();
    readonly List<GameObject> _comparisonOptionObjects = new List<GameObject>();
    readonly List<Renderer> _comparisonOptionRenderers = new List<Renderer>();
    readonly List<TextMesh> _comparisonOptionLabels = new List<TextMesh>();
    readonly List<GameObject> _questionnairePointClickTargets = new List<GameObject>();
    readonly List<GameObject> _questionnairePointClickLabels = new List<GameObject>();
    readonly List<ClassicPointClickRadioTarget> _classicPointClickRadioTargets =
        new List<ClassicPointClickRadioTarget>();
    readonly List<ClassicPointClickPageRecord> _classicPointClickPageRecords =
        new List<ClassicPointClickPageRecord>();

    GameObject _comparisonTaskRoot;
    GameObject _classicPointClickPageRoot;
    GameObject _classicPointClickPreviousButton;
    GameObject _classicPointClickNextButton;
    Renderer _classicPointClickPreviousRenderer;
    Renderer _classicPointClickNextRenderer;
    TextMesh _classicPointClickPreviousLabel;
    TextMesh _classicPointClickNextLabel;
    TextMesh _classicPointClickStatusText;
    bool _classicPointClickSyntheticScreenshotCaptured;
    bool _comparisonTaskActive;
    bool _comparisonPointClickWasHeld;
    string _comparisonSequenceCode = "not_run";

    const int ComparisonInputCheckItemCount = 8;
    const string ComparisonTaskDemandKey = "matched_moderate";

    static readonly ComparisonMathQuestion[] MatchedComparisonQuestions =
    {
        new ComparisonMathQuestion("27 + 38 = ?", 65, 55, 63, 65, 67),
        new ComparisonMathQuestion("74 - 29 = ?", 45, 43, 45, 47, 49),
        new ComparisonMathQuestion("12 * 7 = ?", 84, 72, 82, 84, 96),
        new ComparisonMathQuestion("17 * 6 = ?", 102, 92, 100, 102, 112),
        new ComparisonMathQuestion("46 + 57 = ?", 103, 93, 101, 103, 113),
        new ComparisonMathQuestion("83 - 47 = ?", 36, 34, 36, 38, 46),
        new ComparisonMathQuestion("14 * 6 = ?", 84, 74, 82, 84, 94),
        new ComparisonMathQuestion("16 * 7 = ?", 112, 102, 110, 112, 122)
    };

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

        ComparisonBlockSpec[] sequence = BuildComparisonSequence();
        _runBlockCount = sequence.Length;

        _titleText.text = "PAXSM Questionnaire Comparison";
        _cueText.text = "Complete two matched task blocks, one with each questionnaire interaction method.";
        _statusText.text = $"Order {_comparisonSequenceCode}\nRead the wall, then use the right controller.";
        _timerText.text = "";
        _feedbackText.text = "Press N on the desktop to skip timed instructions.";
        yield return WaitForSecondsOrN(4f);

        for (int i = 0; i < sequence.Length; i++)
        {
            _blockIndex = i;
            ComparisonBlockSpec block = sequence[i];

            questionnaireInputMethod = block.method;
            questionnaireBankResourcesPath = comparisonInputCheckBankResourcesPath;
            collectConfidenceAfterEachItem = false;
            var inputCheckProfile = new ProbeBlockProfile
            {
                blockId = $"input_check_{ComparisonMethodKey(block.method)}",
                displayName = $"Input check for {ComparisonMethodLabel(block.method)}",
                targetTlxDimension = $"input_accuracy_{ComparisonMethodKey(block.method)}"
            };
            _titleText.text = $"{ComparisonMethodLabel(block.method)} practice";
            _cueText.text = $"Complete {ComparisonInputCheckItemCount} target-value selections before the study block.";
            _statusText.text = "These items measure selection accuracy and are not questionnaire scores.";
            yield return WaitForSecondsOrN(2.5f);
            yield return RunBlockQuestionnaire(inputCheckProfile);

            yield return RunComparisonArithmeticBlock(block);

            questionnaireInputMethod = block.method;
            questionnaireBankResourcesPath = comparisonWorkloadBankResourcesPath;
            collectConfidenceAfterEachItem = comparisonCollectConfidence;
            var workloadProfile = new ProbeBlockProfile
            {
                blockId = ComparisonBlockId(block),
                displayName = ComparisonMethodLabel(block.method),
                targetTlxDimension = "comparison_questionnaire_method"
            };
            yield return RunBlockQuestionnaire(workloadProfile);

            QuestionnaireInputMethod evaluatedMethod = block.method;
            questionnaireInputMethod = QuestionnaireInputMethod.PointAndClick;
            questionnaireBankResourcesPath = comparisonSusBankResourcesPath;
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
        }

        questionnaireBankResourcesPath = originalBank;
        questionnaireScale = originalScale;
        collectConfidenceAfterEachItem = originalConfidence;
        questionnaireInputMethod = originalMethod;

        WriteCsvFiles("completed");
        _titleText.text = "Comparison Complete";
        _cueText.text = "All task, questionnaire, Confidence, and SUS records have been saved.";
        _statusText.text = $"Saved {_comparisonTaskRecords.Count} arithmetic trials and " +
                           $"{_questionnaireRecords.Count} questionnaire records to:\n{GetOutputFolder()}";
        _timerText.text = "";
        _feedbackText.text = "You may now remove the headset.";
        ClearComparisonOptions();
    }

    ComparisonBlockSpec[] BuildComparisonSequence()
    {
        int group = (ParseParticipantNumber(participantId) - 1) % 4;
        switch (group)
        {
            case 1:
                _comparisonSequenceCode = "B: Click-A / PAXSM-B";
                return new[]
                {
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PointAndClick, 0),
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PaxsmKnob, 1)
                };
            case 2:
                _comparisonSequenceCode = "C: PAXSM-B / Click-A";
                return new[]
                {
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PaxsmKnob, 1),
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PointAndClick, 0)
                };
            case 3:
                _comparisonSequenceCode = "D: Click-B / PAXSM-A";
                return new[]
                {
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PointAndClick, 1),
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PaxsmKnob, 0)
                };
            default:
                _comparisonSequenceCode = "A: PAXSM-A / Click-B";
                return new[]
                {
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PaxsmKnob, 0),
                    new ComparisonBlockSpec(QuestionnaireInputMethod.PointAndClick, 1)
                };
        }
    }

    IEnumerator RunComparisonArithmeticBlock(ComparisonBlockSpec block)
    {
        string methodLabel = ComparisonMethodLabel(block.method);
        _titleText.text = $"Block {_blockIndex + 1}/{_runBlockCount}: Matched arithmetic";
        _cueText.text = "Solve each arithmetic problem and point at the correct answer.";
        _statusText.text = block.method == QuestionnaireInputMethod.PaxsmKnob
            ? $"After the task, answer the workload questionnaire using {methodLabel}."
            : "After the task, answer the workload questionnaire by pointing and clicking.";
        _timerText.text = "";
        _feedbackText.text = "Right Trigger: select an answer";
        yield return WaitForSecondsOrN(3f);

        int trialCount = Mathf.Clamp(comparisonArithmeticTrialsPerMethod, 2, 4);
        for (int trial = 0; trial < trialCount; trial++)
        {
            ComparisonMathQuestion question = GetComparisonQuestion(block.formIndex, trial);
            yield return RunComparisonArithmeticTrial(block, trial, question);
            yield return new WaitForSeconds(0.35f);
        }

        _titleText.text = "Task block complete";
        _cueText.text = "The questionnaire will begin next.";
        _statusText.text = $"Input method: {methodLabel}";
        _timerText.text = "";
        _feedbackText.text = "";
        yield return WaitForSecondsOrN(1.5f);
    }

    ComparisonMathQuestion GetComparisonQuestion(
        int formIndex,
        int trialIndex)
    {
        int formOffset = Mathf.Clamp(formIndex, 0, 1) * 4;
        return MatchedComparisonQuestions[formOffset + Mathf.Clamp(trialIndex, 0, 3)];
    }

    IEnumerator RunComparisonArithmeticTrial(
        ComparisonBlockSpec block,
        int trialIndex,
        ComparisonMathQuestion question)
    {
        ClearComparisonOptions();
        BuildComparisonOptions(question.options);
        _comparisonTaskActive = true;

        _titleText.text = $"Arithmetic  {trialIndex + 1}/{Mathf.Clamp(comparisonArithmeticTrialsPerMethod, 2, 4)}";
        _cueText.text = question.problem;
        _statusText.text = $"Block {_blockIndex + 1}/{_runBlockCount}  |  Task form {(block.formIndex == 0 ? "A" : "B")}";
        _feedbackText.text = "Aim at one answer and press the Right Trigger";

        yield return WaitForComparisonPointClickRelease();

        float start = Time.realtimeSinceStartup;
#if UNITY_EDITOR
        if (SyntheticParticipantActive)
            BeginSyntheticComparisonTrial(block, trialIndex, question);
#endif
        float lastSampleTime = start;
        bool hasLastPoint = false;
        Vector3 lastPoint = Vector3.zero;
        float pointerPath = 0f;
        float pointerPeakSpeed = 0f;
        int hover = -1;
        int previousHover = -1;
        int hoverChanges = 0;
        int selectedIndex = -1;
        bool timeout = false;

        while (selectedIndex < 0 && !timeout)
        {
#if UNITY_EDITOR
            bool pressed;
            Ray ray;
            if (SyntheticParticipantActive)
                GetSyntheticComparisonPointClick(out ray, out _, out pressed);
            else
                pressed = GetComparisonPointClickPressed(out ray, out _);
#else
            bool pressed = GetComparisonPointClickPressed(out Ray ray, out _);
#endif
            int currentHover = ResolveComparisonOption(ray);
            if (currentHover != previousHover)
            {
                if (currentHover >= 0)
                    hoverChanges++;
                previousHover = currentHover;
            }
            hover = currentHover;
            UpdateComparisonOptionHover(hover);
            UpdateSelectionRayVisual(ray);

            if (TryProjectComparisonPointer(ray, out Vector3 point))
            {
                float now = Time.realtimeSinceStartup;
                float dt = Mathf.Max(0.0001f, now - lastSampleTime);
                if (hasLastPoint)
                {
                    float delta = Vector3.Distance(lastPoint, point);
                    pointerPath += delta;
                    pointerPeakSpeed = Mathf.Max(pointerPeakSpeed, delta / dt);
                }
                lastPoint = point;
                hasLastPoint = true;
                lastSampleTime = now;
            }

            float elapsed = Time.realtimeSinceStartup - start;
            float remaining = Mathf.Max(0f, comparisonTrialTimeoutSeconds - elapsed);
            _timerText.text = $"{remaining:0}";
            if (elapsed >= comparisonTrialTimeoutSeconds)
                timeout = true;
            else if (pressed && hover >= 0)
                selectedIndex = hover;

            yield return null;
        }

        _comparisonTaskActive = false;
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;

        int selectedAnswer = selectedIndex >= 0 && selectedIndex < question.options.Length
            ? question.options[selectedIndex]
            : -1;
        bool correct = selectedAnswer == question.correctAnswer;
        float decisionRt = Mathf.Max(0f, Time.realtimeSinceStartup - start);

        _comparisonTaskRecords.Add(new ComparisonTaskRecord
        {
            presentationOrder = _blockIndex + 1,
            orderCode = _comparisonSequenceCode,
            method = ComparisonMethodKey(block.method),
            taskDemand = ComparisonTaskDemandKey,
            form = block.formIndex == 0 ? "A" : "B",
            trialIndex = trialIndex + 1,
            problem = question.problem,
            options = string.Join("|", question.options),
            correctAnswer = question.correctAnswer,
            selectedAnswer = selectedAnswer,
            selectedIndex = selectedIndex,
            isCorrect = correct,
            timeout = timeout,
            decisionRt = decisionRt,
            pointerPath = pointerPath,
            pointerPeakSpeed = pointerPeakSpeed,
            hoverChangeCount = hoverChanges
        });

        if (timeout)
        {
            _feedbackText.text = $"Time ended. Correct answer: {question.correctAnswer}";
            _feedbackText.color = new Color(1f, 0.65f, 0.25f);
        }
        else if (correct)
        {
            SetComparisonOptionResult(selectedIndex, true);
            _feedbackText.text = "Correct";
            _feedbackText.color = new Color(0.35f, 1f, 0.55f);
            TryPlayRightHandHaptic(0.38f, 0.07f, force: true);
        }
        else
        {
            SetComparisonOptionResult(selectedIndex, false);
            _feedbackText.text = $"Incorrect. Correct answer: {question.correctAnswer}";
            _feedbackText.color = new Color(1f, 0.42f, 0.35f);
        }
        _timerText.text = "";
        yield return new WaitForSeconds(0.55f);
        _feedbackText.color = new Color(1f, 0.9f, 0.45f);
        ClearComparisonOptions();
    }

    void BuildComparisonOptions(int[] options)
    {
        _comparisonTaskRoot = new GameObject("PAXSMComparison_ArithmeticOptions");
        float[] xPositions = { -1.5f, -0.5f, 0.5f, 1.5f };
        for (int i = 0; i < options.Length && i < xPositions.Length; i++)
        {
            GameObject card = GameObject.CreatePrimitive(PrimitiveType.Cube);
            card.name = $"ComparisonOption_{i + 1}_{options[i]}";
            card.transform.SetParent(_comparisonTaskRoot.transform, false);
            card.transform.position = new Vector3(xPositions[i], 1.34f, 4.12f);
            card.transform.localScale = new Vector3(0.72f, 0.42f, 0.08f);
            Renderer renderer = card.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = _normalMaterial;

            GameObject labelObject = new GameObject($"ComparisonOptionLabel_{i + 1}");
            labelObject.transform.SetParent(_comparisonTaskRoot.transform, false);
            labelObject.transform.position = new Vector3(xPositions[i], 1.34f, 4.065f);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = options[i].ToString();
            label.fontSize = 48;
            label.characterSize = 0.018f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Color.white;

            _comparisonOptionObjects.Add(card);
            _comparisonOptionRenderers.Add(renderer);
            _comparisonOptionLabels.Add(label);
        }
    }

    int ResolveComparisonOption(Ray ray)
    {
        int result = -1;
        float nearest = float.MaxValue;
        for (int i = 0; i < _comparisonOptionObjects.Count; i++)
        {
            Collider collider = _comparisonOptionObjects[i] != null
                ? _comparisonOptionObjects[i].GetComponent<Collider>()
                : null;
            if (collider == null || !collider.Raycast(ray, out RaycastHit hit, selectionMaxDistance))
                continue;
            if (hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            result = i;
        }
        return result;
    }

    void UpdateComparisonOptionHover(int hover)
    {
        for (int i = 0; i < _comparisonOptionRenderers.Count; i++)
        {
            Renderer renderer = _comparisonOptionRenderers[i];
            if (renderer != null)
                renderer.sharedMaterial = i == hover ? _questionnaireHoverMaterial : _normalMaterial;
            if (i < _comparisonOptionLabels.Count && _comparisonOptionLabels[i] != null)
                _comparisonOptionLabels[i].color = i == hover
                    ? new Color(0.05f, 0.12f, 0.16f)
                    : Color.white;
        }
    }

    void SetComparisonOptionResult(int index, bool correct)
    {
        if (index < 0 || index >= _comparisonOptionRenderers.Count)
            return;
        Renderer renderer = _comparisonOptionRenderers[index];
        if (renderer != null)
            renderer.sharedMaterial = correct ? _correctMaterial : _wrongMaterial;
    }

    void ClearComparisonOptions()
    {
        if (_comparisonTaskRoot != null)
            Destroy(_comparisonTaskRoot);
        _comparisonTaskRoot = null;
        _comparisonOptionObjects.Clear();
        _comparisonOptionRenderers.Clear();
        _comparisonOptionLabels.Clear();
    }

    bool TryProjectComparisonPointer(Ray ray, out Vector3 point)
    {
        Plane wall = new Plane(Vector3.back, new Vector3(0f, 0f, 4.05f));
        if (wall.Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }
        point = default;
        return false;
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
        string trialsPath = Path.Combine(folder, $"PAXSMComparison_Arithmetic_{suffix}.csv");
        string susPath = Path.Combine(folder, $"PAXSMComparison_SUS_{suffix}.csv");
        string inputAccuracyPath = Path.Combine(folder, $"PAXSMComparison_InputAccuracy_{suffix}.csv");
        string pointClickPagesPath = Path.Combine(folder, $"PAXSMComparison_PointClickPages_{suffix}.csv");
        string manifestPath = Path.Combine(folder, $"PAXSMComparison_Manifest_{suffix}.csv");
        DirectoryInfo rawDataParent = Directory.GetParent(Path.GetFullPath(folder));
        string analysisFolder = rawDataParent != null ? rawDataParent.FullName : folder;
        Directory.CreateDirectory(analysisFolder);
        string analysisReadyPath = Path.Combine(
            analysisFolder,
            $"PAXSMComparison_AnalysisReady_{suffix}.csv");
        File.WriteAllText(trialsPath, BuildComparisonTaskCsv(), Encoding.UTF8);
        File.WriteAllText(susPath, BuildComparisonSusCsv(), Encoding.UTF8);
        File.WriteAllText(inputAccuracyPath, BuildComparisonInputAccuracyCsv(), Encoding.UTF8);
        File.WriteAllText(pointClickPagesPath, BuildComparisonPointClickPagesCsv(), Encoding.UTF8);
        File.WriteAllText(manifestPath, BuildComparisonManifestCsv(), Encoding.UTF8);
        File.WriteAllText(analysisReadyPath, BuildComparisonAnalysisReadyCsv(reason), Encoding.UTF8);
        savedPaths.Add(trialsPath);
        savedPaths.Add(susPath);
        savedPaths.Add(inputAccuracyPath);
        savedPaths.Add(pointClickPagesPath);
        savedPaths.Add(manifestPath);
        savedPaths.Add(analysisReadyPath);
    }

    string BuildComparisonTaskCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,sessionNumber,conditionLabel,presentationOrder,orderCode,method,taskDemand,form,trialIndex,problem,options,correctAnswer,selectedAnswer,selectedIndex,isCorrect,timeout,decisionRt,pointerPath,pointerPeakSpeed,hoverChangeCount");
        for (int i = 0; i < _comparisonTaskRecords.Count; i++)
        {
            ComparisonTaskRecord r = _comparisonTaskRecords[i];
            sb.AppendLine(string.Join(",",
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), I(r.presentationOrder),
                Csv(r.orderCode), Csv(r.method), Csv(r.taskDemand), Csv(r.form), I(r.trialIndex),
                Csv(r.problem), Csv(r.options), I(r.correctAnswer), I(r.selectedAnswer), I(r.selectedIndex),
                B(r.isCorrect), B(r.timeout), F(r.decisionRt), F(r.pointerPath), F(r.pointerPeakSpeed),
                I(r.hoverChangeCount)));
        }
        return sb.ToString();
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

    string BuildComparisonInputAccuracyCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,sessionNumber,conditionLabel,method,itemIndex,itemId,targetValue,selectedValue,absoluteError,exactMatch,responseMode,answerRt,firstInteractionRt,confirmAttempts,confirmCancels");
        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            if (record == null ||
                !record.blockId.StartsWith("input_check_", StringComparison.OrdinalIgnoreCase) ||
                !TryParseInputCheckTarget(record.itemId, out int target))
                continue;

            string method = record.blockId.Substring("input_check_".Length);
            int absoluteError = record.selectedScore > 0
                ? Mathf.Abs(record.selectedScore - target)
                : -1;
            sb.AppendLine(string.Join(",",
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(method),
                I(record.itemIndex), Csv(record.itemId), I(target), I(record.selectedScore), I(absoluteError),
                B(record.selectedScore == target), Csv(record.responseMode), F(record.answerRt),
                F(record.answerFirstInteractionRt), I(record.answerConfirmAttemptCount),
                I(record.answerConfirmCancelCount)));
        }
        return sb.ToString();
    }

    bool TryParseInputCheckTarget(string itemId, out int target)
    {
        target = -1;
        if (string.IsNullOrWhiteSpace(itemId))
            return false;
        int separator = itemId.LastIndexOf('_');
        return separator >= 0 && separator + 1 < itemId.Length &&
               int.TryParse(itemId.Substring(separator + 1), out target);
    }

    string BuildComparisonManifestCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("designSchemaVersion,participantId,sessionNumber,conditionLabel,studyDesign,independentVariable,methodLevels,sequenceCode,taskDemand,taskForms,inputCheckItemsPerMethod,arithmeticTrialsPerMethod,trialTimeoutSec,inputCheckBank,workloadBank,susBank,confidenceAfterWorkload,pointClickLayout,pointClickItemsPerPage,pointClickRadioDiameterM,pointClickConfirmation,paxsmConfirmation");
        sb.AppendLine(string.Join(",",
            Csv("CAREXR_PAXSMComparison_Design_v3"), Csv(participantId), I(sessionNumber),
            Csv(EffectiveConditionLabel()), Csv("within_subject_single_factor"),
            Csv("questionnaire_interaction_method"), Csv("paxsm|point_click"), Csv(_comparisonSequenceCode),
            Csv(ComparisonTaskDemandKey), Csv("A|B"), I(ComparisonInputCheckItemCount),
            I(Mathf.Clamp(comparisonArithmeticTrialsPerMethod, 2, 4)), F(comparisonTrialTimeoutSeconds),
            Csv(comparisonInputCheckBankResourcesPath), Csv(comparisonWorkloadBankResourcesPath),
            Csv(comparisonSusBankResourcesPath),
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
            "sequenceCode", "method", "methodPresentationOrder", "taskForm", "taskDemand",
            "methodComplete", "qualityFlags", "exportReason", "generatedUtc",
            "inputResponseMode", "inputItems", "inputExactMatches", "inputAccuracy",
            "inputMeanAbsoluteError", "inputMaxAbsoluteError", "inputMeanAnswerRt", "inputMedianAnswerRt",
            "inputValidFirstInteractionItems", "inputMeanFirstInteractionRt", "inputConfirmAttempts",
            "inputConfirmCancels", "inputIncompleteItems",
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
                Csv("CAREXR_PAXSMComparison_AnalysisReady_v3"),
                Csv(participantId),
                I(sessionNumber),
                Csv(EffectiveConditionLabel()),
                Csv(ExperimentRunContext.RunId),
                Csv(_comparisonSequenceCode),
                Csv(method),
                OptionalInt(FirstComparisonPresentationOrder(method)),
                Csv(FirstComparisonTaskForm(method)),
                Csv(ComparisonTaskDemandKey),
                B(string.Equals(qualityFlags, "none", StringComparison.Ordinal)),
                Csv(qualityFlags),
                Csv(exportReason),
                Csv(generatedUtc)
            };

            AddComparisonInputAnalysisCells(cells, method);
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
            "taskTrials", "taskCorrect", "taskAccuracy", "taskTimeouts", "taskMeanDecisionRt",
            "taskMedianDecisionRt", "taskSdDecisionRt", "taskTotalDecisionRt",
            "taskMeanPointerPath", "taskMeanPointerPeakSpeed", "taskMeanHoverChanges",
            "nasaResponseMode", "nasaItems", "nasaComplete",
            "nasaMental", "nasaPhysical", "nasaTemporal", "nasaPerformance",
            "nasaEffort", "nasaFrustration", "nasaRawMean",
            "confidenceComplete", "confidenceMean", "confidenceMental", "confidencePhysical",
            "confidenceTemporal", "confidencePerformance", "confidenceEffort", "confidenceFrustration",
            "questionnaireMeanReadRt", "questionnaireMeanAnswerRt",
            "questionnaireMeanAnswerDecisionRt", "questionnaireMeanConfidenceRt",
            "questionnaireTotalRt", "answerConfirmAttempts", "answerConfirmCancels"
        });
    }

    void AddComparisonInputAnalysisCells(List<string> cells, string method)
    {
        string blockId = "input_check_" + method;
        string responseMode = "";
        int items = 0;
        int exact = 0;
        int incomplete = 0;
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
            if (!TryParseInputCheckTarget(record.itemId, out int target) || record.selectedScore <= 0)
            {
                incomplete++;
            }
            else
            {
                int error = Mathf.Abs(record.selectedScore - target);
                absoluteErrorSum += error;
                maxAbsoluteError = Mathf.Max(maxAbsoluteError, error);
                if (error == 0) exact++;
            }
            if (record.answerRt >= 0f) answerRts.Add(record.answerRt);
            if (record.answerFirstInteractionRt >= 0f) firstInteractionRts.Add(record.answerFirstInteractionRt);
            confirmAttempts += record.answerConfirmAttemptCount;
            confirmCancels += record.answerConfirmCancelCount;
        }

        int validItems = items - incomplete;
        cells.Add(Csv(responseMode));
        cells.Add(I(items));
        cells.Add(I(exact));
        cells.Add(OptionalRatio(exact, validItems));
        cells.Add(validItems > 0 ? F(absoluteErrorSum / validItems) : "");
        cells.Add(validItems > 0 ? I(maxAbsoluteError) : "");
        cells.Add(OptionalMean(answerRts));
        cells.Add(OptionalMedian(answerRts));
        cells.Add(I(firstInteractionRts.Count));
        cells.Add(OptionalMean(firstInteractionRts));
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
        int taskTrials = 0;
        int taskCorrect = 0;
        int taskTimeouts = 0;
        float taskTotalRt = 0f;
        var taskRts = new List<float>();
        var pointerPaths = new List<float>();
        var pointerPeakSpeeds = new List<float>();
        float hoverChanges = 0f;

        for (int i = 0; i < _comparisonTaskRecords.Count; i++)
        {
            ComparisonTaskRecord record = _comparisonTaskRecords[i];
            if (!string.Equals(record.method, method, StringComparison.OrdinalIgnoreCase))
                continue;
            taskTrials++;
            if (record.isCorrect) taskCorrect++;
            if (record.timeout) taskTimeouts++;
            taskTotalRt += record.decisionRt;
            taskRts.Add(record.decisionRt);
            pointerPaths.Add(record.pointerPath);
            pointerPeakSpeeds.Add(record.pointerPeakSpeed);
            hoverChanges += record.hoverChangeCount;
        }

        cells.Add(I(taskTrials));
        cells.Add(I(taskCorrect));
        cells.Add(OptionalRatio(taskCorrect, taskTrials));
        cells.Add(I(taskTimeouts));
        cells.Add(OptionalMean(taskRts));
        cells.Add(OptionalMedian(taskRts));
        cells.Add(OptionalSampleSd(taskRts));
        cells.Add(taskTrials > 0 ? F(taskTotalRt) : "");
        cells.Add(OptionalMean(pointerPaths));
        cells.Add(OptionalMean(pointerPeakSpeeds));
        cells.Add(taskTrials > 0 ? F(hoverChanges / taskTrials) : "");

        string blockId = $"comparison_{method}";
        QuestionnaireRecord[] nasa = new QuestionnaireRecord[6];
        string responseMode = "";
        int nasaItems = 0;
        int confidenceItems = 0;
        int answerConfirmAttempts = 0;
        int answerConfirmCancels = 0;
        var readRts = new List<float>();
        var answerRts = new List<float>();
        var answerDecisionRts = new List<float>();
        var confidenceRts = new List<float>();

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
            if (record.confidence >= 1 && record.confidence <= 5) confidenceItems++;
            if (string.IsNullOrWhiteSpace(responseMode)) responseMode = record.responseMode;
            if (record.readRt >= 0f) readRts.Add(record.readRt);
            if (record.answerRt >= 0f) answerRts.Add(record.answerRt);
            if (record.answerDecisionRt >= 0f) answerDecisionRts.Add(record.answerDecisionRt);
            if (record.confidenceRt > 0f) confidenceRts.Add(record.confidenceRt);
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
            cells.Add(valid ? I(nasa[i].selectedScore) : "");
            if (valid)
            {
                nasaSum += nasa[i].selectedScore;
                validNasa++;
            }
        }
        cells.Add(validNasa > 0 ? F(nasaSum / validNasa) : "");
        cells.Add(B(!comparisonCollectConfidence || confidenceItems == 6));
        float confidenceSum = 0f;
        for (int i = 0; i < nasa.Length; i++)
            if (nasa[i] != null && nasa[i].confidence >= 1 && nasa[i].confidence <= 5)
                confidenceSum += nasa[i].confidence;
        cells.Add(confidenceItems > 0 ? F(confidenceSum / confidenceItems) : "");
        for (int i = 0; i < nasa.Length; i++)
            cells.Add(nasa[i] != null && nasa[i].confidence >= 1 && nasa[i].confidence <= 5
                ? I(nasa[i].confidence)
                : "");
        cells.Add(OptionalMean(readRts));
        cells.Add(OptionalMean(answerRts));
        cells.Add(OptionalMean(answerDecisionRts));
        cells.Add(OptionalMean(confidenceRts));
        float questionnaireTotalRt = SumNonNegative(readRts) + SumNonNegative(answerRts) + SumNonNegative(confidenceRts);
        float answerPageRt = ClassicPointClickStageTotalRt(blockId, "Answer");
        if (answerPageRt >= 0f)
        {
            float confidencePageRt = ClassicPointClickStageTotalRt(blockId, "Confidence");
            questionnaireTotalRt = answerPageRt + Mathf.Max(0f, confidencePageRt);
        }
        cells.Add(nasaItems > 0 ? F(questionnaireTotalRt) : "");
        cells.Add(I(answerConfirmAttempts));
        cells.Add(I(answerConfirmCancels));
    }

    string BuildComparisonMethodQualityFlags(string method)
    {
        var flags = new List<string>();
        int inputItems = CountComparisonQuestionnaireItems("input_check_" + method, false, out _);
        int susItems = CountComparisonQuestionnaireItems("sus_" + method, false, out _);
        if (inputItems != ComparisonInputCheckItemCount)
            flags.Add($"input_items_{inputItems}_of_{ComparisonInputCheckItemCount}");
        if (susItems != 10) flags.Add($"sus_items_{susItems}_of_10");
        int expectedTrials = Mathf.Clamp(comparisonArithmeticTrialsPerMethod, 2, 4);
        int taskItems = 0;
        for (int i = 0; i < _comparisonTaskRecords.Count; i++)
            if (string.Equals(_comparisonTaskRecords[i].method, method, StringComparison.OrdinalIgnoreCase))
                taskItems++;
        if (taskItems != expectedTrials)
            flags.Add($"task_items_{taskItems}_of_{expectedTrials}");

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
        int result = int.MaxValue;
        for (int i = 0; i < _comparisonTaskRecords.Count; i++)
            if (string.Equals(_comparisonTaskRecords[i].method, method, StringComparison.OrdinalIgnoreCase))
                result = Mathf.Min(result, _comparisonTaskRecords[i].presentationOrder);
        return result == int.MaxValue ? -1 : result;
    }

    string FirstComparisonTaskForm(string method)
    {
        int firstOrder = int.MaxValue;
        string form = "";
        for (int i = 0; i < _comparisonTaskRecords.Count; i++)
        {
            ComparisonTaskRecord record = _comparisonTaskRecords[i];
            if (!string.Equals(record.method, method, StringComparison.OrdinalIgnoreCase) ||
                record.presentationOrder >= firstOrder)
                continue;
            firstOrder = record.presentationOrder;
            form = record.form;
        }
        return form;
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
