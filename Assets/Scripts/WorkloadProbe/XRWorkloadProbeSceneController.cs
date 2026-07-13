using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using InputSystemKeyboard = UnityEngine.InputSystem.Keyboard;
using InputSystemMouse = UnityEngine.InputSystem.Mouse;
#endif

public class XRWorkloadProbeSceneController : MonoBehaviour
{
    const float RuntimeQuestionnaireKnobDiameterMeters = 0.10f;

    [Serializable]
    public class ProbeBlockProfile
    {
        public string blockId = "baseline";
        public string displayName = "Baseline";
        public string targetTlxDimension = "baseline";
        [Range(1, 3)] public int ruleComplexity = 1;
        [Tooltip("Maximum number of selectable objects shown in a trial.")]
        [Range(2, 10)] public int targetCount = 4;
        [Tooltip("Number of incorrect selectable objects. The effective choice count is one correct target plus these distractors, capped by Target Count.")]
        [Range(1, 7)] public int distractorCount = 3;
        [Range(0.8f, 4.0f)] public float targetDistance = 1.6f;
        [Range(0.08f, 0.35f)] public float targetSize = 0.22f;
        [Range(0f, 10f)] public float timeLimitSeconds = 0f;
        [Range(0f, 1f)] public float successThresholdStrictness = 0f;
        [Range(0f, 1.5f)] public float feedbackDelaySeconds = 0f;
        [Range(0f, 8f)] public float controlNoiseDegrees = 0f;
        [Range(4, 24)] public int trialsPerBlock = 10;
        [TextArea(2, 4)] public string rationale = "";
    }

    struct TargetInfo
    {
        public GameObject gameObject;
        public string colorName;
        public string shapeName;
        public Vector3 baseScale;
    }

    struct TrialSpec
    {
        public string ruleType;
        public int correctPosition;
        public int layoutShift;

        public TrialSpec(string ruleType, int correctPosition, int layoutShift)
        {
            this.ruleType = ruleType;
            this.correctPosition = correctPosition;
            this.layoutShift = layoutShift;
        }
    }

    class TrialRecord
    {
        public string blockId = "";
        public string targetDimension = "";
        public int presentationOrder;
        public int trialIndex;
        public string scheduleId = "";
        public string cue = "";
        public string rule = "";
        public string targetLayout = "";
        public int ruleComplexity;
        public int targetCount;
        public int distractorCount;
        public float targetDistance;
        public float targetSize;
        public float timeLimit;
        public float successThresholdStrictness;
        public float effectiveSelectionCone;
        public float effectiveSelectionAssistRadius;
        public bool gazeFallbackAllowed;
        public float feedbackDelay;
        public float controlNoise;
        public float cueTime;
        public float selectionTime;
        public float decisionRt;
        public bool timeout;
        public bool isCorrect;
        public bool correctHapticPlayed;
        public bool correctHapticSuppressed;
        public int correctIndex;
        public int selectedIndex;
        public float pointerPath;
        public float pointerPeakSpeed;
        public int pauseCount;
        public int hoverChangeCount;
    }

    class TlxItem
    {
        public string itemId = "";
        public string dimension = "";
        public string prompt = "";
        public string leftAnchor = "Very low";
        public string rightAnchor = "Very high";
        public bool performanceLike;
    }

    class QuestionnaireMotionStats
    {
        public float stageStartTime;
        public float duration;
        public float confirmHoldDuration;
        public float path;
        public float peakSpeed;
        public int pauseCount;
        public int hoverChangeCount;
        public float totalAbsAngle;
        public float maxAbsVel;
        public int slotChangeCount;
        public int reverseCount;
        public int microAdjustCount;
        public int fastFlickCount;
        public float lastSampleTime;
        public Vector3 lastPointerPosition;
        public bool hasLastPointerPosition;
        public float pauseAccum;
        public int lastHoverValue = -1;
        public int lastSlot = -1;
        public int lastDirection;
        public float lastSlotChangeTime;
        public bool pauseCountedForCurrentDwell;
    }

    class QuestionnaireRecord
    {
        public string blockId = "";
        public string targetDimension = "";
        public int presentationOrder;
        public int itemIndex;
        public string itemId = "";
        public string itemDimension = "";
        public string prompt = "";
        public string leftAnchor = "";
        public string rightAnchor = "";
        public string responseMode = "";
        public int scale;
        public int selectedScore;
        public int confidence;
        public float answerRt;
        public float answerDecisionRt;
        public float answerConfirmHoldRt;
        public float confidenceRt;
        public float confidenceDecisionRt;
        public float confidenceConfirmHoldRt;
        public float answerPointerPath;
        public float answerPeakSpeed;
        public int answerPauseCount;
        public int answerHoverChangeCount;
        public float confidencePointerPath;
        public float confidencePeakSpeed;
        public int confidencePauseCount;
        public int confidenceHoverChangeCount;
        public float answerTotalAbsAngle;
        public float answerMaxAbsVel;
        public int answerSlotChangeCount;
        public int answerReverseCount;
        public int answerMicroAdjustCount;
        public int answerFastFlickCount;
        public float confidenceTotalAbsAngle;
        public float confidenceMaxAbsVel;
        public int confidenceSlotChangeCount;
        public int confidenceReverseCount;
        public int confidenceMicroAdjustCount;
        public int confidenceFastFlickCount;
    }

    public string participantId = "P001";
    public bool randomizeWorkloadBlocks = false;
    public bool startAutomatically = true;
    public bool writeCsvOnQuit = true;
    public string outputFolderName = "XRWorkloadProbe_Data";

    [Header("Inter-block PAXSM Questionnaire")]
    public bool collectQuestionnaireBetweenBlocks = true;
    public bool useMainSceneKnobRig = true;
    public GameObject paxsmKnobRigPrefab;
    public string questionnaireBankResourcesPath = "QuestionBanks/Scale";
    [Range(0.04f, 0.15f)] public float questionnaireKnobDiameterMeters = 0.10f;
    public Vector3 questionnaireKnobPosition = new Vector3(0.38f, 1.02f, 0.64f);
    [Range(-30f, 30f)] public float questionnairePanelTiltDegrees = 18f;
    [Range(30f, 180f)] public float questionnaireKnobArcDegrees = 120f;
    public bool questionnaireUseHeadRelativePlacement = true;
    [Range(0.3f, 0.7f)] public float questionnaireForwardOffset = 0.42f;
    [Range(0f, 0.35f)] public float questionnaireRightOffset = 0.16f;
    [Range(0.15f, 0.5f)] public float questionnaireBelowEyeOffset = 0.28f;
    [Range(0.6f, 1.2f)] public float questionnaireMinimumHeight = 0.72f;
    [Range(1.2f, 1.8f)] public float questionnaireMaximumHeight = 1.45f;
    [Range(0.12f, 0.45f)] public float questionnaireGrabRadius = 0.14f;
    [Range(5, 21)] public int questionnaireScale = 21;
    [Range(3, 7)] public int questionnaireConfidenceScale = 5;
    public bool collectConfidenceAfterEachItem = true;

    [Header("Questionnaire Hold Confirmation")]
    [Range(0.4f, 1.5f)] public float questionnaireConfirmHoldSeconds = 0.8f;
    [Range(0.04f, 0.2f)] public float questionnaireConfirmHapticInterval = 0.08f;
    [Range(0.01f, 0.25f)] public float questionnaireConfirmHapticMin = 0.04f;
    [Range(0.2f, 0.9f)] public float questionnaireConfirmHapticMax = 0.62f;

    [Header("Feedback Haptics")]
    public bool enableCorrectHaptics = true;
    public bool suppressCorrectHapticsInFrustrationBlock = true;
    [Range(0.05f, 1f)] public float correctHapticAmplitude = 0.45f;
    [Range(0.02f, 0.4f)] public float correctHapticDuration = 0.08f;

    [Header("Comfortable Selection")]
    [Range(4f, 30f)] public float selectionMaxDistance = 18f;
    [Range(0.15f, 1.5f)] public float selectionAssistRadius = 0.65f;
    [Range(3f, 28f)] public float controllerSelectionConeDegrees = 14f;
    [Range(3f, 35f)] public float gazeFallbackConeDegrees = 18f;
    public bool enableGazeFallbackSelection = true;
    public bool showSelectionRay = true;
    [Range(1f, 20f)] public float selectionRayVisualLength = 7f;

    public List<ProbeBlockProfile> blockProfiles = new List<ProbeBlockProfile>();

    Camera _mainCamera;
    TextMesh _titleText;
    TextMesh _cueText;
    TextMesh _statusText;
    TextMesh _timerText;
    TextMesh _feedbackText;
    Transform _targetRoot;
    LineRenderer _selectionRayRenderer;
    Material _normalMaterial;
    Material _hoverMaterial;
    Material _selectionRayMaterial;
    Material _correctMaterial;
    Material _wrongMaterial;
    Material _questionnaireTickMaterial;
    Material _questionnaireHoverMaterial;
    Material _questionnaireSelectedMaterial;
    Material _questionnairePanelMaterial;
    readonly Dictionary<string, Material> _colorMaterials = new Dictionary<string, Material>();
    readonly List<TargetInfo> _targets = new List<TargetInfo>();
    readonly List<TrialRecord> _trialRecords = new List<TrialRecord>();
    readonly List<QuestionnaireRecord> _questionnaireRecords = new List<QuestionnaireRecord>();
    readonly List<Behaviour> _questionnaireDisabledJumpBehaviours = new List<Behaviour>();
    readonly List<GameObject> _questionnaireTicks = new List<GameObject>();
    readonly List<GameObject> _questionnaireWallTicks = new List<GameObject>();
    readonly List<GameObject> _questionnairePanelObjects = new List<GameObject>();
    GameObject _questionnaireKnobFace;
    GameObject _questionnaireKnobPointer;
    GameObject _questionnaireKnobHub;
    GameObject _questionnaireConfirmTrack;
    GameObject _questionnaireConfirmFill;
    GameObject _paxsmKnobRigInstance;
    KnobCore _paxsmQuestionnaireKnobCore;
    TickRingLocal _paxsmQuestionnaireTickRing;
    KnobGrabByWrist _paxsmQuestionnaireGrab;
    KnobModeManager _paxsmQuestionnaireModeManager;
    QuestionnaireMotionStats _activeQuestionnaireMotionStats;
    KnobCore _mainSceneAnswerExportMirror;
    KnobCore _mainSceneConfidenceExportMirror;
    KnobBehaviorMergedCSVExporter _mainSceneMergedExporter;
    int _questionnaireCurrentScale;
    int _paxsmQuestionnaireLastSlot = -1;
    Quaternion _questionnairePanelYawRotation = Quaternion.identity;
    bool _questionnaireConfirmNeedsRelease;
    float _questionnaireConfirmHoldStart = -1f;
    float _questionnaireNextConfirmHapticTime;
    readonly List<InputDevice> _rightHandDevices = new List<InputDevice>();
    Transform _rightPointerTransform;
    Transform _targetAreaAnchor;
    Transform _questionnaireRoot;
    TextMesh _questionnaireTitleText;
    TextMesh _questionnairePromptText;
    TextMesh _questionnaireScaleText;
    TextMesh _questionnaireScaleRightText;
    TextMesh _questionnaireProgressText;
    TextMesh _questionnaireValueText;
    TextMesh _questionnaireKnobHintText;
    readonly string[] _colorNames = { "blue", "yellow", "red", "green", "purple", "white", "orange", "cyan" };
    readonly Color[] _colors =
    {
        new Color(0.18f, 0.45f, 1.0f),
        new Color(1.0f, 0.82f, 0.16f),
        new Color(1.0f, 0.25f, 0.22f),
        new Color(0.2f, 0.78f, 0.36f),
        new Color(0.68f, 0.35f, 1.0f),
        new Color(0.9f, 0.9f, 0.9f),
        new Color(1.0f, 0.52f, 0.12f),
        new Color(0.1f, 0.86f, 0.9f)
    };
    readonly string[] _shapeNames = { "sphere", "cube", "capsule" };

    bool _trialActive;
    bool _waitingForContinue;
    bool _prevTrigger;
    int _blockIndex;
    int _runBlockCount;
    int _trialIndex;
    int _correctIndex;
    int _hoverIndex = -1;
    int _lastHoverIndex = -1;
    float _trialStartTime;
    float _lastPointerSampleTime;
    float _pauseAccum;
    Vector3 _lastPointerPosition;
    bool _hasLastPointerPosition;
    string _previousCorrectColor = "blue";
    ProbeBlockProfile _currentProfile;
    TrialRecord _currentTrial;
    Coroutine _feedbackCoroutine;
    bool _questionnaireActive;
    bool _questionnaireStageActive;
    int _questionnaireHoverValue = -1;
    int _questionnaireSelectedValue = -1;

    void Awake()
    {
        questionnaireKnobDiameterMeters = RuntimeQuestionnaireKnobDiameterMeters;
        EnsureCamera();
        AddTrackedPoseDriverIfAvailable(_mainCamera.gameObject);
        BuildDefaultProfilesIfNeeded();
        BuildSceneObjects();
    }

    void Start()
    {
        if (startAutomatically)
            StartCoroutine(RunExperiment());
    }

    void Update()
    {
        if (_waitingForContinue && SkipPressedThisFrame())
        {
            _waitingForContinue = false;
            return;
        }

        if (!_trialActive)
            return;

        bool pressed = GetSelectionPressed(out Ray pointerRay, out Vector3 pointerOrigin);
        TrackPointerMotion(pointerOrigin);
        HighlightHoveredTarget(pointerRay);
        UpdateSelectionRayVisual(pointerRay);

        if (_currentProfile.timeLimitSeconds > 0f)
        {
            float remaining = _currentProfile.timeLimitSeconds - (Time.time - _trialStartTime);
            UpdateTimerDisplay(remaining);
            if (remaining <= 0f)
                FinishTrial(-1, timeout: true);
        }

        if (pressed)
        {
            int selectedIndex = ResolveTargetIndex(pointerRay);
            if (selectedIndex < 0 && IsGazeFallbackAllowed(_currentProfile) && _mainCamera != null)
            {
                selectedIndex = ResolveTargetIndex(
                    new Ray(_mainCamera.transform.position, _mainCamera.transform.forward),
                    EffectiveGazeFallbackCone(_currentProfile),
                    EffectiveSelectionAssistRadius(_currentProfile) * 1.15f);
            }
            if (selectedIndex < 0)
                selectedIndex = _hoverIndex;
            FinishTrial(selectedIndex, timeout: false);
        }
    }

    IEnumerator RunExperiment()
    {
        _blockIndex = 0;
        List<ProbeBlockProfile> runOrder = BuildRunOrder();
        _runBlockCount = runOrder.Count;

        ShowIntro();
        yield return WaitForSecondsOrN(4f);

        for (_blockIndex = 0; _blockIndex < runOrder.Count; _blockIndex++)
        {
            _currentProfile = runOrder[_blockIndex];
            _previousCorrectColor = "blue";
            ShowBlockInstruction(_currentProfile);
            yield return WaitForSecondsOrN(4f);

            for (_trialIndex = 0; _trialIndex < _currentProfile.trialsPerBlock; _trialIndex++)
            {
                BeginTrial(_currentProfile, _trialIndex);
                while (_trialActive)
                    yield return null;

                yield return new WaitForSeconds(0.45f);
            }

            ShowBlockComplete(_currentProfile);
            yield return WaitForSecondsOrN(1.5f);
            if (collectQuestionnaireBetweenBlocks)
                yield return RunBlockQuestionnaire(_currentProfile);
            else
                yield return WaitForSecondsOrN(2f);
        }

        WriteCsvFiles("completed");
        _titleText.text = "XR Workload Probe Complete";
        _cueText.text = "All blocks are finished. Review the saved workload-probe CSV or stop the session.";
        _statusText.text = $"Saved {_trialRecords.Count} trial records to:\n{GetOutputFolder()}";
        if (_timerText != null)
            _timerText.text = "";
        _feedbackText.text = "";
        ClearTargets();
    }

    List<ProbeBlockProfile> BuildRunOrder()
    {
        var source = new List<ProbeBlockProfile>(blockProfiles);
        if (!randomizeWorkloadBlocks || source.Count <= 1)
            return source;

        int baselineIndex = source.FindIndex(p => string.Equals(p.blockId, "baseline", StringComparison.OrdinalIgnoreCase));
        var runOrder = new List<ProbeBlockProfile>();
        if (baselineIndex >= 0)
        {
            runOrder.Add(source[baselineIndex]);
            source.RemoveAt(baselineIndex);
        }

        for (int i = 0; i < source.Count; i++)
        {
            int j = UnityEngine.Random.Range(i, source.Count);
            (source[i], source[j]) = (source[j], source[i]);
        }
        runOrder.AddRange(source);
        return runOrder;
    }

    IEnumerator WaitForSecondsOrN(float seconds)
    {
        _waitingForContinue = true;
        float end = Time.time + seconds;
        while (_waitingForContinue && Time.time < end)
            yield return null;
        _waitingForContinue = false;
    }

    void UpdateTimerDisplay(float secondsRemaining)
    {
        if (_timerText == null)
            return;

        float shown = Mathf.Max(0f, secondsRemaining);
        _timerText.text = $"TIME\n{shown:0.0}s";
        _timerText.color = shown <= 1f
            ? new Color(1f, 0.25f, 0.2f)
            : new Color(1f, 0.92f, 0.25f);
    }

    void BeginTrial(ProbeBlockProfile profile, int trialIndex)
    {
        ClearTargets();
        _trialActive = true;
        _hoverIndex = -1;
        _lastHoverIndex = -1;
        _hasLastPointerPosition = false;
        _pauseAccum = 0f;
        _trialStartTime = Time.time;
        _lastPointerSampleTime = Time.time;

        TrialSpec spec = GetTrialSpec(profile, trialIndex);
        string rule;
        string cue;
        string targetLayout;
        SpawnTargets(profile, spec, out _correctIndex, out rule, out cue, out targetLayout);

        _currentTrial = new TrialRecord
        {
            blockId = profile.blockId,
            targetDimension = profile.targetTlxDimension,
            presentationOrder = _blockIndex + 1,
            trialIndex = trialIndex + 1,
            scheduleId = $"{profile.blockId}_T{trialIndex + 1:00}",
            cue = cue,
            rule = rule,
            targetLayout = targetLayout,
            ruleComplexity = Mathf.Clamp(profile.ruleComplexity, 1, 3),
            targetCount = EffectiveTargetCount(profile),
            distractorCount = Mathf.Max(1, EffectiveTargetCount(profile) - 1),
            targetDistance = profile.targetDistance,
            targetSize = profile.targetSize,
            timeLimit = profile.timeLimitSeconds,
            successThresholdStrictness = Mathf.Clamp01(profile.successThresholdStrictness),
            effectiveSelectionCone = EffectiveSelectionCone(profile),
            effectiveSelectionAssistRadius = EffectiveSelectionAssistRadius(profile),
            gazeFallbackAllowed = IsGazeFallbackAllowed(profile),
            feedbackDelay = profile.feedbackDelaySeconds,
            controlNoise = profile.controlNoiseDegrees,
            cueTime = Time.time,
            correctIndex = _correctIndex,
            selectedIndex = -1
        };

        _titleText.text = profile.displayName;
        _cueText.text = cue;
        _feedbackText.text = "";
        _statusText.text = $"Task type: {profile.blockId}\nBlock {_blockIndex + 1}/{_runBlockCount}  Trial {trialIndex + 1}/{profile.trialsPerBlock}";
        if (profile.timeLimitSeconds > 0f)
            UpdateTimerDisplay(profile.timeLimitSeconds);
        else if (_timerText != null)
            _timerText.text = "NO\nLIMIT";
    }

    void FinishTrial(int selectedIndex, bool timeout)
    {
        if (!_trialActive)
            return;

        _trialActive = false;
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;

        bool isCorrect = !timeout && selectedIndex == _correctIndex;
        bool correctHapticSuppressed = isCorrect && ShouldSuppressCorrectHaptic(_currentProfile);
        bool correctHapticPlayed = false;
        if (isCorrect && !correctHapticSuppressed)
            correctHapticPlayed = TryPlayRightHandHaptic(correctHapticAmplitude, correctHapticDuration);

        _currentTrial.selectionTime = Time.time;
        _currentTrial.decisionRt = Time.time - _currentTrial.cueTime;
        _currentTrial.timeout = timeout;
        _currentTrial.isCorrect = isCorrect;
        _currentTrial.correctHapticPlayed = correctHapticPlayed;
        _currentTrial.correctHapticSuppressed = correctHapticSuppressed;
        _currentTrial.selectedIndex = selectedIndex;
        _trialRecords.Add(_currentTrial);

        if (_feedbackCoroutine != null)
            StopCoroutine(_feedbackCoroutine);
        _feedbackCoroutine = StartCoroutine(ShowFeedbackDelayed(selectedIndex, isCorrect, timeout, _currentProfile.feedbackDelaySeconds));
    }

    bool ShouldSuppressCorrectHaptic(ProbeBlockProfile profile)
    {
        if (!enableCorrectHaptics)
            return true;
        if (profile == null)
            return false;
        return suppressCorrectHapticsInFrustrationBlock &&
               string.Equals(profile.blockId, "frustration_heavy", StringComparison.OrdinalIgnoreCase);
    }

    bool TryPlayRightHandHaptic(float amplitude, float duration, bool force = false)
    {
        if (!enableCorrectHaptics && !force)
            return false;

        bool played = false;
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, _rightHandDevices);
        for (int i = 0; i < _rightHandDevices.Count; i++)
        {
            InputDevice device = _rightHandDevices[i];
            if (!device.isValid)
                continue;

            if (device.TryGetHapticCapabilities(out HapticCapabilities caps) &&
                caps.supportsImpulse &&
                caps.numChannels > 0)
            {
                played = device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), Mathf.Max(0.01f, duration)) || played;
            }
        }
        return played;
    }

    IEnumerator ShowFeedbackDelayed(int selectedIndex, bool isCorrect, bool timeout, float delay)
    {
        if (delay > 0f)
        {
            _feedbackText.text = "Feedback pending...";
            yield return new WaitForSeconds(delay);
        }

        for (int i = 0; i < _targets.Count; i++)
        {
            _targets[i].gameObject.transform.localScale = _targets[i].baseScale;
            Renderer r = _targets[i].gameObject.GetComponent<Renderer>();
            if (r == null) continue;
            if (i == _correctIndex)
                r.sharedMaterial = _correctMaterial;
            else if (i == selectedIndex)
                r.sharedMaterial = _wrongMaterial;
        }

        if (timeout)
            _feedbackText.text = "Timeout. The correct target is highlighted.";
        else if (isCorrect)
            _feedbackText.text = "Correct.";
        else
            _feedbackText.text = $"Incorrect. Selected {GetTargetLabel(selectedIndex)}; target was {GetTargetLabel(_correctIndex)}.";
    }

    TrialSpec GetTrialSpec(ProbeBlockProfile profile, int trialIndex)
    {
        TrialSpec[] schedule = GetPresetSchedule(profile.blockId);
        if (schedule.Length == 0)
            return new TrialSpec(
                RuleTypeForComplexity(profile.ruleComplexity, trialIndex),
                trialIndex % EffectiveTargetCount(profile),
                trialIndex);

        TrialSpec spec = schedule[trialIndex % schedule.Length];
        spec.ruleType = RuleTypeForComplexity(profile.ruleComplexity, trialIndex);
        return spec;
    }

    string RuleTypeForComplexity(int complexity, int trialIndex)
    {
        switch (Mathf.Clamp(complexity, 1, 3))
        {
            case 1:
                return "direct-color";
            case 2:
                return trialIndex % 2 == 0 ? "direct-color" : "shape-color";
            default:
                switch (trialIndex % 3)
                {
                    case 0: return "direct-color";
                    case 1: return "shape-color";
                    default: return "previous-color";
                }
        }
    }

    TrialSpec[] GetPresetSchedule(string blockId)
    {
        switch (blockId)
        {
            case "baseline":
                return new[]
                {
                    new TrialSpec("direct-color", 0, 0),
                    new TrialSpec("direct-color", 2, 1),
                    new TrialSpec("direct-color", 1, 2),
                    new TrialSpec("direct-color", 3, 3),
                    new TrialSpec("direct-color", 0, 1),
                    new TrialSpec("direct-color", 3, 2),
                    new TrialSpec("direct-color", 2, 0),
                    new TrialSpec("direct-color", 1, 3)
                };
            case "cognitive_heavy":
                return new[]
                {
                    new TrialSpec("direct-color", 1, 0),
                    new TrialSpec("shape-color", 5, 2),
                    new TrialSpec("previous-color", 3, 4),
                    new TrialSpec("shape-color", 0, 1),
                    new TrialSpec("previous-color", 6, 3),
                    new TrialSpec("direct-color", 4, 5),
                    new TrialSpec("shape-color", 2, 0),
                    new TrialSpec("previous-color", 5, 2),
                    new TrialSpec("direct-color", 6, 1),
                    new TrialSpec("previous-color", 1, 4)
                };
            case "physical_heavy":
                return new[]
                {
                    new TrialSpec("direct-color", 0, 0),
                    new TrialSpec("direct-color", 4, 1),
                    new TrialSpec("direct-color", 1, 2),
                    new TrialSpec("direct-color", 3, 3),
                    new TrialSpec("direct-color", 2, 4),
                    new TrialSpec("direct-color", 0, 2),
                    new TrialSpec("direct-color", 4, 0),
                    new TrialSpec("direct-color", 1, 3),
                    new TrialSpec("direct-color", 3, 1),
                    new TrialSpec("direct-color", 2, 4)
                };
            case "temporal_heavy":
                return new[]
                {
                    new TrialSpec("direct-color", 2, 0),
                    new TrialSpec("direct-color", 0, 1),
                    new TrialSpec("direct-color", 4, 2),
                    new TrialSpec("direct-color", 1, 3),
                    new TrialSpec("direct-color", 3, 4),
                    new TrialSpec("direct-color", 2, 1),
                    new TrialSpec("direct-color", 4, 3),
                    new TrialSpec("direct-color", 0, 2),
                    new TrialSpec("direct-color", 3, 0),
                    new TrialSpec("direct-color", 1, 4)
                };
            case "performance_strict":
                return new[]
                {
                    new TrialSpec("direct-color", 0, 0),
                    new TrialSpec("shape-color", 5, 1),
                    new TrialSpec("direct-color", 2, 2),
                    new TrialSpec("shape-color", 4, 3),
                    new TrialSpec("shape-color", 1, 4),
                    new TrialSpec("direct-color", 3, 5),
                    new TrialSpec("direct-color", 5, 2),
                    new TrialSpec("shape-color", 0, 4),
                    new TrialSpec("direct-color", 4, 1),
                    new TrialSpec("shape-color", 2, 3)
                };
            case "frustration_heavy":
                return new[]
                {
                    new TrialSpec("direct-color", 1, 0),
                    new TrialSpec("direct-color", 3, 1),
                    new TrialSpec("direct-color", 0, 2),
                    new TrialSpec("direct-color", 4, 3),
                    new TrialSpec("direct-color", 2, 4),
                    new TrialSpec("direct-color", 3, 0),
                    new TrialSpec("direct-color", 1, 2),
                    new TrialSpec("direct-color", 4, 1),
                    new TrialSpec("direct-color", 0, 4),
                    new TrialSpec("direct-color", 2, 3)
                };
            case "combined_high":
                return new[]
                {
                    new TrialSpec("direct-color", 0, 0),
                    new TrialSpec("shape-color", 6, 2),
                    new TrialSpec("previous-color", 2, 4),
                    new TrialSpec("direct-color", 5, 1),
                    new TrialSpec("shape-color", 1, 3),
                    new TrialSpec("previous-color", 4, 5),
                    new TrialSpec("shape-color", 3, 0),
                    new TrialSpec("direct-color", 6, 1),
                    new TrialSpec("previous-color", 0, 2),
                    new TrialSpec("shape-color", 5, 4)
                };
            default:
                return Array.Empty<TrialSpec>();
        }
    }

    void SpawnTargets(ProbeBlockProfile profile, TrialSpec spec, out int correctIndex, out string rule, out string cue, out string targetLayout)
    {
        int count = EffectiveTargetCount(profile);
        correctIndex = Mathf.Clamp(spec.correctPosition, 0, count - 1);
        int[] colorIndices = BuildColorLayout(count, spec.layoutShift);

        string requiredColor = _colorNames[colorIndices[correctIndex]];
        string requiredShape = _shapeNames[correctIndex % _shapeNames.Length];

        if (spec.ruleType == "direct-color")
        {
            rule = "direct-color";
            cue = $"Select the {requiredColor.ToUpperInvariant()} target.";
        }
        else if (spec.ruleType == "shape-color")
        {
            rule = "shape-color";
            cue = $"Rule: SHAPE + COLOR. Select the {requiredColor.ToUpperInvariant()} {requiredShape.ToUpperInvariant()} target.";
        }
        else
        {
            rule = "previous-color";
            int previousColorIndex = Mathf.Max(0, Array.IndexOf(_colorNames, _previousCorrectColor));
            correctIndex = EnsureColorInLayout(colorIndices, previousColorIndex, correctIndex);
            requiredColor = _colorNames[colorIndices[correctIndex]];
            requiredShape = _shapeNames[correctIndex % _shapeNames.Length];
            cue = $"Rule: MEMORY. Select the previous correct color: {requiredColor.ToUpperInvariant()}.";
        }

        _previousCorrectColor = requiredColor;
        targetLayout = BuildTargetLayoutString(colorIndices, count);

        float tableTopY = 0.76f;
        float visibleWidth = Mathf.Clamp(profile.targetDistance * 1.35f, 2.2f, 4.8f);
        float targetDepth = Mathf.Clamp(profile.targetDistance, 1.45f, 2.35f);
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            float x = Mathf.Lerp(-visibleWidth * 0.5f, visibleWidth * 0.5f, t);

            PrimitiveType primitiveType = ShapeToPrimitive(_shapeNames[i % _shapeNames.Length]);
            GameObject target = GameObject.CreatePrimitive(primitiveType);
            string colorName = _colorNames[colorIndices[i]];
            target.name = $"ProbeTarget_{i + 1}_{colorName}_{_shapeNames[i % _shapeNames.Length]}";
            target.transform.SetParent(_targetRoot, false);
            target.transform.localScale = Vector3.one * profile.targetSize;
            float centerY = tableTopY + GetPrimitiveHalfHeight(primitiveType, profile.targetSize) + 0.015f;
            target.transform.localPosition = new Vector3(x, centerY, targetDepth);
            var renderer = target.GetComponent<Renderer>();
            renderer.sharedMaterial = _colorMaterials[colorName];

            _targets.Add(new TargetInfo
            {
                gameObject = target,
                colorName = colorName,
                shapeName = _shapeNames[i % _shapeNames.Length],
                baseScale = target.transform.localScale
            });
        }
    }

    int EffectiveTargetCount(ProbeBlockProfile profile)
    {
        if (profile == null)
            return 2;

        int capacity = Mathf.Clamp(profile.targetCount, 2, _colorNames.Length);
        int distractors = Mathf.Clamp(profile.distractorCount, 1, capacity - 1);
        return 1 + distractors;
    }

    float GetPrimitiveHalfHeight(PrimitiveType primitiveType, float scale)
    {
        return primitiveType == PrimitiveType.Capsule ? scale : scale * 0.5f;
    }

    int[] BuildColorLayout(int count, int layoutShift)
    {
        int[] colorIndices = new int[count];
        for (int i = 0; i < count; i++)
            colorIndices[i] = PositiveModulo(layoutShift + i, _colorNames.Length);
        return colorIndices;
    }

    int EnsureColorInLayout(int[] colorIndices, int colorIndex, int preferredPosition)
    {
        for (int i = 0; i < colorIndices.Length; i++)
        {
            if (colorIndices[i] == colorIndex)
                return i;
        }

        int position = Mathf.Clamp(preferredPosition, 0, colorIndices.Length - 1);
        colorIndices[position] = colorIndex;
        return position;
    }

    int PositiveModulo(int value, int modulo)
    {
        return ((value % modulo) + modulo) % modulo;
    }

    string BuildTargetLayoutString(int[] colorIndices, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                sb.Append("|");
            sb.Append(i);
            sb.Append(":");
            sb.Append(_colorNames[colorIndices[i]]);
            sb.Append("/");
            sb.Append(_shapeNames[i % _shapeNames.Length]);
        }
        return sb.ToString();
    }

    string GetTargetLabel(int index)
    {
        if (index < 0 || index >= _targets.Count)
            return "no target";

        TargetInfo target = _targets[index];
        return $"{target.colorName.ToUpperInvariant()} {target.shapeName}";
    }

    PrimitiveType ShapeToPrimitive(string shape)
    {
        if (shape == "cube") return PrimitiveType.Cube;
        if (shape == "capsule") return PrimitiveType.Capsule;
        return PrimitiveType.Sphere;
    }

    bool GetSelectionPressed(out Ray pointerRay, out Vector3 pointerOrigin)
    {
        bool hasXr = TryGetXrPointer(out pointerRay, out pointerOrigin, out bool triggerDown);
        if (!hasXr)
        {
            pointerRay = GetDesktopPointerRay();
            pointerOrigin = pointerRay.origin;
            triggerDown = DesktopSelectHeld();
        }

        if (!_questionnaireActive && _currentProfile != null && _currentProfile.controlNoiseDegrees > 0f)
        {
            Quaternion jitter = Quaternion.Euler(
                UnityEngine.Random.Range(-_currentProfile.controlNoiseDegrees, _currentProfile.controlNoiseDegrees),
                UnityEngine.Random.Range(-_currentProfile.controlNoiseDegrees, _currentProfile.controlNoiseDegrees),
                0f);
            pointerRay = new Ray(pointerRay.origin, jitter * pointerRay.direction);
        }

        bool pressed = triggerDown && !_prevTrigger;
        _prevTrigger = triggerDown;
        return pressed;
    }

    bool SkipPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        InputSystemKeyboard keyboard = InputSystemKeyboard.current;
        if (keyboard != null && keyboard.nKey.wasPressedThisFrame)
            return true;
#endif
        return false;
    }

    Ray GetDesktopPointerRay()
    {
#if ENABLE_INPUT_SYSTEM
        InputSystemMouse mouse = InputSystemMouse.current;
        if (mouse != null)
        {
            Vector2 position = mouse.position.ReadValue();
            return _mainCamera.ScreenPointToRay(new Vector3(position.x, position.y, 0f));
        }
#endif
        return new Ray(_mainCamera.transform.position, _mainCamera.transform.forward);
    }

    bool DesktopSelectHeld()
    {
        bool held = false;
#if ENABLE_INPUT_SYSTEM
        InputSystemMouse mouse = InputSystemMouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
            held = true;

        InputSystemKeyboard keyboard = InputSystemKeyboard.current;
        if (keyboard != null && keyboard.spaceKey.isPressed)
            held = true;
#endif
        return held;
    }

    bool TryGetXrPointer(out Ray ray, out Vector3 origin, out bool triggerDown)
    {
        bool hasDevice = TryGetRightHandTrigger(out triggerDown);

        if (hasDevice && TryGetRightPointerTransform(out Transform pointerTransform))
        {
            origin = pointerTransform.position;
            ray = new Ray(pointerTransform.position, pointerTransform.forward);
            return true;
        }

        if (!hasDevice)
        {
            ray = default;
            origin = default;
            triggerDown = false;
            return false;
        }

        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, _rightHandDevices);
        for (int i = 0; i < _rightHandDevices.Count; i++)
        {
            InputDevice device = _rightHandDevices[i];
            if (!device.isValid) continue;

            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) &&
                device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
            {
                origin = position;
                ray = new Ray(position, rotation * Vector3.forward);
                return true;
            }
        }

        ray = default;
        origin = default;
        triggerDown = false;
        return false;
    }

    bool TryGetRightHandTrigger(out bool triggerDown)
    {
        triggerDown = false;
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, _rightHandDevices);
        bool foundDevice = false;
        for (int i = 0; i < _rightHandDevices.Count; i++)
        {
            InputDevice device = _rightHandDevices[i];
            if (!device.isValid)
                continue;

            foundDevice = true;
            bool trigger = false;
            device.TryGetFeatureValue(CommonUsages.triggerButton, out trigger);
            if (!trigger && device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primary))
                trigger = primary;
            if (!trigger && device.TryGetFeatureValue(CommonUsages.gripButton, out bool grip))
                trigger = grip;

            triggerDown = triggerDown || trigger;
        }

        return foundDevice;
    }

    bool TryGetRightPointerTransform(out Transform pointerTransform)
    {
        if (_rightPointerTransform != null && _rightPointerTransform.gameObject.activeInHierarchy)
        {
            pointerTransform = _rightPointerTransform;
            return true;
        }

        Transform[] transforms = FindObjectsOfType<Transform>(true);
        Transform fallback = null;
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            string path = GetTransformPath(t).ToLowerInvariant();
            if (!path.Contains("right"))
                continue;

            bool looksLikeRay = path.Contains("ray") || HasComponentNamed(t, "XRRayInteractor");
            bool looksLikeController = path.Contains("controller") || path.Contains("hand");
            if (looksLikeRay)
            {
                _rightPointerTransform = t;
                pointerTransform = t;
                return true;
            }

            if (fallback == null && looksLikeController)
                fallback = t;
        }

        if (fallback != null)
        {
            _rightPointerTransform = fallback;
            pointerTransform = fallback;
            return true;
        }

        pointerTransform = null;
        return false;
    }

    string GetTransformPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        Transform current = t.parent;
        while (current != null)
        {
            sb.Insert(0, current.name + "/");
            current = current.parent;
        }
        return sb.ToString();
    }

    bool HasComponentNamed(Transform t, string componentName)
    {
        Component[] components = t.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component != null && component.GetType().Name.Contains(componentName))
                return true;
        }
        return false;
    }

    void TrackPointerMotion(Vector3 pointerPosition)
    {
        float now = Time.time;
        float dt = Mathf.Max(0.0001f, now - _lastPointerSampleTime);
        _lastPointerSampleTime = now;

        if (!_hasLastPointerPosition)
        {
            _lastPointerPosition = pointerPosition;
            _hasLastPointerPosition = true;
            return;
        }

        float delta = Vector3.Distance(_lastPointerPosition, pointerPosition);
        float speed = delta / dt;
        _lastPointerPosition = pointerPosition;
        _currentTrial.pointerPath += delta;
        _currentTrial.pointerPeakSpeed = Mathf.Max(_currentTrial.pointerPeakSpeed, speed);

        if (speed < 0.025f)
        {
            _pauseAccum += dt;
            if (_pauseAccum > 0.35f)
            {
                _currentTrial.pauseCount++;
                _pauseAccum = 0f;
            }
        }
        else
        {
            _pauseAccum = 0f;
        }
    }

    void HighlightHoveredTarget(Ray ray)
    {
        int nextHover = ResolveTargetIndex(ray);

        if (nextHover != _hoverIndex)
        {
            if (_hoverIndex >= 0 && _hoverIndex < _targets.Count)
            {
                TargetInfo previous = _targets[_hoverIndex];
                previous.gameObject.GetComponent<Renderer>().sharedMaterial = _colorMaterials[previous.colorName];
                previous.gameObject.transform.localScale = previous.baseScale;
            }

            _hoverIndex = nextHover;
            if (_hoverIndex >= 0)
            {
                TargetInfo current = _targets[_hoverIndex];
                current.gameObject.GetComponent<Renderer>().sharedMaterial = _colorMaterials[current.colorName];
                current.gameObject.transform.localScale = current.baseScale * 1.18f;
            }
        }

        if (_hoverIndex != _lastHoverIndex)
        {
            if (_lastHoverIndex != -1)
                _currentTrial.hoverChangeCount++;
            _lastHoverIndex = _hoverIndex;
        }
    }

    int ResolveTargetIndex(Ray ray)
    {
        return ResolveTargetIndex(
            ray,
            EffectiveSelectionCone(_currentProfile),
            EffectiveSelectionAssistRadius(_currentProfile));
    }

    float EffectiveSelectionCone(ProbeBlockProfile profile)
    {
        float strictness = profile == null ? 0f : Mathf.Clamp01(profile.successThresholdStrictness);
        float strictCone = Mathf.Max(2.5f, controllerSelectionConeDegrees * 0.25f);
        return Mathf.Lerp(controllerSelectionConeDegrees, strictCone, strictness);
    }

    float EffectiveSelectionAssistRadius(ProbeBlockProfile profile)
    {
        float strictness = profile == null ? 0f : Mathf.Clamp01(profile.successThresholdStrictness);
        float strictRadius = Mathf.Max(0.04f, selectionAssistRadius * 0.08f);
        return Mathf.Lerp(selectionAssistRadius, strictRadius, strictness);
    }

    float EffectiveGazeFallbackCone(ProbeBlockProfile profile)
    {
        float strictness = profile == null ? 0f : Mathf.Clamp01(profile.successThresholdStrictness);
        float strictCone = Mathf.Max(3f, gazeFallbackConeDegrees * 0.25f);
        return Mathf.Lerp(gazeFallbackConeDegrees, strictCone, strictness);
    }

    bool IsGazeFallbackAllowed(ProbeBlockProfile profile)
    {
        return enableGazeFallbackSelection &&
               (profile == null || profile.successThresholdStrictness < 0.75f);
    }

    int ResolveTargetIndex(Ray ray, float coneDegrees, float assistRadiusMeters)
    {
        int exactHit = -1;
        float exactDistance = float.MaxValue;

        for (int i = 0; i < _targets.Count; i++)
        {
            Collider c = _targets[i].gameObject.GetComponent<Collider>();
            if (c != null && c.Raycast(ray, out RaycastHit hit, selectionMaxDistance) && hit.distance < exactDistance)
            {
                exactDistance = hit.distance;
                exactHit = i;
            }
        }

        if (exactHit >= 0)
            return exactHit;

        int assistedHit = -1;
        float bestScore = float.MaxValue;
        Vector3 direction = ray.direction.normalized;
        float safeCone = Mathf.Max(0.01f, coneDegrees);
        float safeAssistRadius = Mathf.Max(0.01f, assistRadiusMeters);

        for (int i = 0; i < _targets.Count; i++)
        {
            Collider c = _targets[i].gameObject.GetComponent<Collider>();
            if (c == null)
                continue;

            Vector3 center = c.bounds.center;
            Vector3 toCenter = center - ray.origin;
            float alongRay = Vector3.Dot(toCenter, direction);
            if (alongRay < 0.15f || alongRay > selectionMaxDistance)
                continue;

            Vector3 closestOnRay = ray.origin + direction * alongRay;
            float missDistance = Vector3.Distance(center, closestOnRay);
            float targetRadius = Mathf.Max(safeAssistRadius, c.bounds.extents.magnitude * 1.35f);
            float angularError = Vector3.Angle(direction, toCenter.normalized);
            bool withinRadius = missDistance <= targetRadius;
            bool withinCone = angularError <= safeCone;
            if (!withinRadius && !withinCone)
                continue;

            float score = (angularError / safeCone) + (missDistance / targetRadius) + alongRay * 0.01f;
            if (score < bestScore)
            {
                bestScore = score;
                assistedHit = i;
            }
        }

        return assistedHit;
    }

    void UpdateSelectionRayVisual(Ray ray)
    {
        if (_selectionRayRenderer == null)
            return;

        _selectionRayRenderer.enabled = showSelectionRay && (_trialActive || _questionnaireStageActive);
        if (!_selectionRayRenderer.enabled)
            return;

        Vector3 end = ray.origin + ray.direction.normalized * selectionRayVisualLength;
        _selectionRayRenderer.SetPosition(0, ray.origin);
        _selectionRayRenderer.SetPosition(1, end);
    }

    void ClearTargets()
    {
        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            if (_targets[i].gameObject != null)
                Destroy(_targets[i].gameObject);
        }
        _targets.Clear();
        _hoverIndex = -1;
    }

    void BuildDefaultProfilesIfNeeded()
    {
        if (blockProfiles.Count > 0)
            return;

        blockProfiles.Add(new ProbeBlockProfile
        {
            blockId = "baseline",
            displayName = "Baseline",
            targetTlxDimension = "low workload baseline",
            ruleComplexity = 1,
            targetCount = 4,
            distractorCount = 3,
            targetDistance = 1.35f,
            targetSize = 0.26f,
            trialsPerBlock = 8,
            rationale = "Simple rule, near targets, no time pressure, stable feedback."
        });
        blockProfiles.Add(new ProbeBlockProfile
        {
            blockId = "cognitive_heavy",
            displayName = "Cognitive-heavy",
            targetTlxDimension = "Mental Demand",
            ruleComplexity = 3,
            targetCount = 7,
            distractorCount = 6,
            targetDistance = 1.55f,
            targetSize = 0.22f,
            trialsPerBlock = 10,
            rationale = "Rule switching, memory cue, and distractors should increase attention and working-memory demand."
        });
        blockProfiles.Add(new ProbeBlockProfile
        {
            blockId = "physical_heavy",
            displayName = "Physical-heavy",
            targetTlxDimension = "Physical Demand",
            ruleComplexity = 1,
            targetCount = 5,
            distractorCount = 4,
            targetDistance = 2.65f,
            targetSize = 0.18f,
            successThresholdStrictness = 0.65f,
            trialsPerBlock = 10,
            rationale = "Farther and smaller targets should increase reaching/pointing demand."
        });
        blockProfiles.Add(new ProbeBlockProfile
        {
            blockId = "temporal_heavy",
            displayName = "Temporal-heavy",
            targetTlxDimension = "Temporal Demand",
            ruleComplexity = 1,
            targetCount = 5,
            distractorCount = 4,
            targetDistance = 1.55f,
            targetSize = 0.22f,
            timeLimitSeconds = 2.25f,
            trialsPerBlock = 10,
            rationale = "Short selection window should increase perceived time pressure."
        });
        blockProfiles.Add(new ProbeBlockProfile
        {
            blockId = "performance_strict",
            displayName = "Performance-strict",
            targetTlxDimension = "Performance",
            ruleComplexity = 2,
            targetCount = 6,
            distractorCount = 5,
            targetDistance = 1.8f,
            targetSize = 0.14f,
            successThresholdStrictness = 1.0f,
            trialsPerBlock = 10,
            rationale = "Small targets and strict success standard should make performance feel harder to maintain."
        });
        blockProfiles.Add(new ProbeBlockProfile
        {
            blockId = "frustration_heavy",
            displayName = "Frustration-heavy",
            targetTlxDimension = "Frustration",
            ruleComplexity = 1,
            targetCount = 5,
            distractorCount = 4,
            targetDistance = 1.55f,
            targetSize = 0.2f,
            feedbackDelaySeconds = 0.9f,
            controlNoiseDegrees = 3.5f,
            trialsPerBlock = 10,
            rationale = "Delayed feedback and mild control disturbance should create recoverable friction."
        });
        blockProfiles.Add(new ProbeBlockProfile
        {
            blockId = "combined_high",
            displayName = "Combined-high",
            targetTlxDimension = "Effort / overall workload",
            ruleComplexity = 3,
            targetCount = 7,
            distractorCount = 6,
            targetDistance = 2.2f,
            targetSize = 0.16f,
            timeLimitSeconds = 2.5f,
            feedbackDelaySeconds = 0.35f,
            controlNoiseDegrees = 1.5f,
            trialsPerBlock = 10,
            rationale = "Combined cognitive, physical, temporal, and feedback demands should increase perceived effort."
        });
    }

    void BuildSceneObjects()
    {
        RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.5f);

        _normalMaterial = NewMaterial("ProbeNormal", new Color(0.65f, 0.70f, 0.78f));
        _hoverMaterial = NewMaterial("ProbeHover", new Color(1.0f, 0.95f, 0.45f));
        _selectionRayMaterial = NewMaterial("ProbeSelectionRay", new Color(0.35f, 0.85f, 1.0f, 0.85f));
        _correctMaterial = NewMaterial("ProbeCorrect", new Color(0.25f, 0.95f, 0.45f));
        _wrongMaterial = NewMaterial("ProbeWrong", new Color(1.0f, 0.3f, 0.25f));
        _questionnaireTickMaterial = NewMaterial("QuestionnaireTick", new Color(0.42f, 0.50f, 0.58f));
        _questionnaireHoverMaterial = NewMaterial("QuestionnaireHover", new Color(0.45f, 0.82f, 1.0f));
        _questionnaireSelectedMaterial = NewMaterial("QuestionnaireSelected", new Color(1.0f, 0.82f, 0.18f));
        _questionnairePanelMaterial = NewMaterial("QuestionnaireKnobPanel", new Color(0.16f, 0.19f, 0.22f));
        for (int i = 0; i < _colorNames.Length; i++)
            _colorMaterials[_colorNames[i]] = NewMaterial("Probe_" + _colorNames[i], _colors[i]);

        Material floorMaterial = NewMaterial("ProbeRoomFloor", new Color(0.13f, 0.15f, 0.17f));
        Material wallMaterial = NewMaterial("ProbeRoomWall", new Color(0.08f, 0.10f, 0.12f));
        Material tableMaterial = NewMaterial("ProbeTable", new Color(0.24f, 0.27f, 0.28f));
        Material tableEdgeMaterial = NewMaterial("ProbeTableEdge", new Color(0.12f, 0.14f, 0.15f));

        FindOrCreateRoomSurface("Probe_Room_Floor", new Vector3(0f, -0.04f, 1.55f), new Vector3(6.4f, 0.06f, 5.6f), floorMaterial);
        FindOrCreateRoomSurface("Probe_Room_BackWall", new Vector3(0f, 1.35f, 4.35f), new Vector3(6.4f, 2.8f, 0.08f), wallMaterial);
        FindOrCreateRoomSurface("Probe_Room_LeftWall", new Vector3(-3.2f, 1.35f, 1.55f), new Vector3(0.08f, 2.8f, 5.6f), wallMaterial);
        FindOrCreateRoomSurface("Probe_Room_RightWall", new Vector3(3.2f, 1.35f, 1.55f), new Vector3(0.08f, 2.8f, 5.6f), wallMaterial);
        FindOrCreateRoomSurface("Probe_Room_FrontWall", new Vector3(0f, 1.35f, -1.25f), new Vector3(6.4f, 2.8f, 0.08f), wallMaterial);

        FindOrCreateRoomSurface("Probe_TableTop", new Vector3(0f, 0.70f, 2.02f), new Vector3(5.65f, 0.12f, 2.25f), tableMaterial);
        FindOrCreateRoomSurface("Probe_TableFrontEdge", new Vector3(0f, 0.78f, 0.88f), new Vector3(5.75f, 0.08f, 0.08f), tableEdgeMaterial);
        FindOrCreateRoomSurface("Probe_TableBackEdge", new Vector3(0f, 0.78f, 3.16f), new Vector3(5.75f, 0.08f, 0.08f), tableEdgeMaterial);
        FindOrCreateRoomSurface("Probe_TableLeftLeg", new Vector3(-2.55f, 0.32f, 1.03f), new Vector3(0.12f, 0.70f, 0.12f), tableEdgeMaterial);
        FindOrCreateRoomSurface("Probe_TableRightLeg", new Vector3(2.55f, 0.32f, 1.03f), new Vector3(0.12f, 0.70f, 0.12f), tableEdgeMaterial);
        FindOrCreateRoomSurface("Probe_TableBackLeftLeg", new Vector3(-2.55f, 0.32f, 3.00f), new Vector3(0.12f, 0.70f, 0.12f), tableEdgeMaterial);
        FindOrCreateRoomSurface("Probe_TableBackRightLeg", new Vector3(2.55f, 0.32f, 3.00f), new Vector3(0.12f, 0.70f, 0.12f), tableEdgeMaterial);

        GameObject lightObj = new GameObject("Probe_KeyLight");
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.4f;
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        _targetAreaAnchor = FindOrCreateAnchor("Probe_TargetAreaCenter", Vector3.zero, Quaternion.identity);
        _targetRoot = new GameObject("Probe_Targets").transform;
        _targetRoot.SetParent(_targetAreaAnchor, false);
        _targetRoot.localPosition = Vector3.zero;
        _targetRoot.localRotation = Quaternion.identity;

        _titleText = CreateText("Probe_Title", "Probe_TitleAnchor", new Vector3(0f, 2.32f, 4.24f), 0.03f, TextAnchor.MiddleCenter);
        _cueText = CreateText("Probe_Cue", "Probe_CueAnchor", new Vector3(0f, 2.04f, 4.24f), 0.022f, TextAnchor.MiddleCenter);
        _statusText = CreateText("Probe_Status", "Probe_StatusAnchor", new Vector3(-1.85f, 1.72f, 4.24f), 0.017f, TextAnchor.UpperLeft);
        _timerText = CreateText("Probe_Timer", "Probe_TimerAnchor", new Vector3(0f, 1.72f, 4.24f), 0.038f, TextAnchor.MiddleCenter);
        _feedbackText = CreateText("Probe_Feedback", "Probe_FeedbackAnchor", new Vector3(0f, 1.18f, 4.24f), 0.024f, TextAnchor.MiddleCenter);

        _titleText.color = Color.white;
        _cueText.color = new Color(0.92f, 0.96f, 1f);
        _statusText.color = new Color(0.75f, 0.82f, 0.9f);
        _timerText.color = new Color(1f, 0.92f, 0.25f);
        _feedbackText.color = new Color(1f, 0.9f, 0.45f);

        BuildQuestionnaireRig();
        BuildMainSceneCompatibleExporter();

        GameObject rayObj = GameObject.Find("Probe_SelectionRay");
        if (rayObj == null)
            rayObj = new GameObject("Probe_SelectionRay");
        _selectionRayRenderer = rayObj.GetComponent<LineRenderer>();
        if (_selectionRayRenderer == null)
            _selectionRayRenderer = rayObj.AddComponent<LineRenderer>();
        _selectionRayRenderer.sharedMaterial = _selectionRayMaterial;
        _selectionRayRenderer.positionCount = 2;
        _selectionRayRenderer.useWorldSpace = true;
        _selectionRayRenderer.startWidth = 0.018f;
        _selectionRayRenderer.endWidth = 0.008f;
        _selectionRayRenderer.enabled = false;
    }

    void BuildMainSceneCompatibleExporter()
    {
        GameObject root = GameObject.Find("PAXSM_MainSceneCompatibleExporter");
        if (root == null)
            root = new GameObject("PAXSM_MainSceneCompatibleExporter");

        Transform answerTransform = root.transform.Find("AnswerKnobSummaryMirror");
        if (answerTransform == null)
        {
            GameObject answerObject = new GameObject("AnswerKnobSummaryMirror");
            answerObject.transform.SetParent(root.transform, false);
            answerTransform = answerObject.transform;
        }

        Transform confidenceTransform = root.transform.Find("ConfidenceKnobSummaryMirror");
        if (confidenceTransform == null)
        {
            GameObject confidenceObject = new GameObject("ConfidenceKnobSummaryMirror");
            confidenceObject.transform.SetParent(root.transform, false);
            confidenceTransform = confidenceObject.transform;
        }

        _mainSceneAnswerExportMirror = answerTransform.GetComponent<KnobCore>();
        if (_mainSceneAnswerExportMirror == null)
            _mainSceneAnswerExportMirror = answerTransform.gameObject.AddComponent<KnobCore>();
        _mainSceneAnswerExportMirror.finalizeOnDisable = false;
        _mainSceneAnswerExportMirror.summaries.Clear();
        _mainSceneAnswerExportMirror.enabled = false;

        _mainSceneConfidenceExportMirror = confidenceTransform.GetComponent<KnobCore>();
        if (_mainSceneConfidenceExportMirror == null)
            _mainSceneConfidenceExportMirror = confidenceTransform.gameObject.AddComponent<KnobCore>();
        _mainSceneConfidenceExportMirror.finalizeOnDisable = false;
        _mainSceneConfidenceExportMirror.summaries.Clear();
        _mainSceneConfidenceExportMirror.enabled = false;

        _mainSceneMergedExporter = root.GetComponent<KnobBehaviorMergedCSVExporter>();
        if (_mainSceneMergedExporter == null)
            _mainSceneMergedExporter = root.AddComponent<KnobBehaviorMergedCSVExporter>();
        _mainSceneMergedExporter.answerKnob = _mainSceneAnswerExportMirror;
        _mainSceneMergedExporter.confidenceKnob = _mainSceneConfidenceExportMirror;
        _mainSceneMergedExporter.participantNumber = ParseParticipantNumber(participantId);
        _mainSceneMergedExporter.sessionNumber = 1;
        _mainSceneMergedExporter.conditionLabel = "WorkloadProbe";
        _mainSceneMergedExporter.outputMode = KnobBehaviorMergedCSVExporter.OutputMode.PersistentDataPath;
        _mainSceneMergedExporter.outputSubfolder = outputFolderName;
        _mainSceneMergedExporter.fileNamePrefix = "XRWorkloadProbe";
        _mainSceneMergedExporter.autoExportOnQuit = false;
    }

    int ParseParticipantNumber(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 1;

        var digits = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsDigit(value[i]))
                digits.Append(value[i]);
        }

        return int.TryParse(digits.ToString(), out int parsed) ? Mathf.Max(1, parsed) : 1;
    }

    void BuildQuestionnaireRig()
    {
        GameObject root = GameObject.Find("PAXSM_InterBlockQuestionnaire");
        if (root == null)
            root = new GameObject("PAXSM_InterBlockQuestionnaire");

        _questionnaireRoot = root.transform;
        _questionnaireRoot.position = Vector3.zero;
        _questionnaireRoot.rotation = Quaternion.identity;

        _questionnaireTitleText = CreateQuestionnaireText(
            "PAXSM_QuestionnaireTitle", new Vector3(0f, 1.98f, 4.23f), 0.024f, TextAnchor.MiddleCenter);
        _questionnairePromptText = CreateQuestionnaireText(
            "PAXSM_QuestionnairePrompt", new Vector3(0f, 1.74f, 4.23f), 0.022f, TextAnchor.MiddleCenter);
        _questionnaireScaleText = CreateQuestionnaireText(
            "PAXSM_QuestionnaireScaleLeft", new Vector3(-2.05f, 1.53f, 4.23f), 0.014f, TextAnchor.MiddleLeft);
        _questionnaireScaleRightText = CreateQuestionnaireText(
            "PAXSM_QuestionnaireScaleRight", new Vector3(2.05f, 1.53f, 4.23f), 0.014f, TextAnchor.MiddleRight);
        _questionnaireProgressText = CreateQuestionnaireText(
            "PAXSM_QuestionnaireProgress", new Vector3(-2.25f, 1.42f, 4.23f), 0.015f, TextAnchor.UpperLeft);
        _questionnaireValueText = CreateQuestionnaireText(
            "PAXSM_QuestionnaireValue", new Vector3(0f, 1.12f, 4.23f), 0.028f, TextAnchor.MiddleCenter);

        _questionnaireTitleText.color = Color.white;
        _questionnairePromptText.color = new Color(0.92f, 0.96f, 1f);
        _questionnaireScaleText.color = new Color(0.78f, 0.86f, 0.95f);
        _questionnaireScaleRightText.color = new Color(0.78f, 0.86f, 0.95f);
        _questionnaireProgressText.color = new Color(0.75f, 0.82f, 0.9f);
        _questionnaireValueText.color = new Color(1f, 0.85f, 0.25f);

        _questionnaireRoot.gameObject.SetActive(false);
    }

    TextMesh CreateQuestionnaireText(string name, Vector3 worldPosition, float size, TextAnchor anchor)
    {
        Transform existing = _questionnaireRoot.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        go.transform.SetParent(_questionnaireRoot, false);
        go.transform.position = worldPosition;
        go.transform.rotation = Quaternion.identity;

        TextMesh text = go.GetComponent<TextMesh>();
        if (text == null)
            text = go.AddComponent<TextMesh>();
        text.text = "";
        text.fontSize = 42;
        text.characterSize = size;
        text.anchor = anchor;
        text.alignment = anchor == TextAnchor.UpperLeft ? TextAlignment.Left : TextAlignment.Center;
        return text;
    }

    IEnumerator RunBlockQuestionnaire(ProbeBlockProfile profile)
    {
        ClearTargets();
        CalibrateQuestionnairePlacementFromHead();
        SetQuestionnaireJumpSuppressed(true);
        _questionnaireActive = true;
        _questionnaireRoot.gameObject.SetActive(true);
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;

        _titleText.text = "";
        _cueText.text = "";
        _statusText.text = "";
        _timerText.text = "";
        _feedbackText.text = "";

        TlxItem[] items = BuildTlxItems();
        for (int i = 0; i < items.Length; i++)
        {
            TlxItem item = items[i];
            var record = new QuestionnaireRecord
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
                responseMode = useMainSceneKnobRig ? "paxsm_main_scene_knobrig" : "paxsm_generated_fallback_knob",
                scale = questionnaireScale
            };

            var answerStats = new QuestionnaireMotionStats();
            string answerPrompt = item.prompt;
            yield return RunQuestionnaireSelectionStage(
                profile,
                $"{item.dimension}  {i + 1}/{items.Length}",
                answerPrompt,
                item.leftAnchor,
                item.rightAnchor,
                questionnaireScale,
                "",
                false,
                answerStats);

            record.selectedScore = _questionnaireSelectedValue;
            record.answerRt = answerStats.duration;
            record.answerConfirmHoldRt = answerStats.confirmHoldDuration;
            record.answerDecisionRt = Mathf.Max(0f, answerStats.duration - answerStats.confirmHoldDuration);
            CopyQuestionnaireStats(answerStats, record, answerStage: true);

            if (collectConfidenceAfterEachItem)
            {
                var confidenceStats = new QuestionnaireMotionStats();
                yield return RunQuestionnaireSelectionStage(
                    profile,
                    $"Confidence  {i + 1}/{items.Length}",
                    $"How confident are you in your {item.dimension} rating?",
                    "Not confident",
                    "Very confident",
                    questionnaireConfidenceScale,
                    "",
                    true,
                    confidenceStats);

                record.confidence = _questionnaireSelectedValue;
                record.confidenceRt = confidenceStats.duration;
                record.confidenceConfirmHoldRt = confidenceStats.confirmHoldDuration;
                record.confidenceDecisionRt = Mathf.Max(0f, confidenceStats.duration - confidenceStats.confirmHoldDuration);
                CopyQuestionnaireStats(confidenceStats, record, answerStage: false);
            }
            else
            {
                record.confidence = -1;
            }

            _questionnaireRecords.Add(record);
            AppendMainSceneCompatibleSummaries(record);
            yield return new WaitForSeconds(0.2f);
        }

        ClearQuestionnaireTicks();
        _questionnaireTitleText.text = "Questionnaire complete";
        _questionnairePromptText.text = "The next task block will start shortly.";
        _questionnaireScaleText.text = "";
        _questionnaireScaleRightText.text = "";
        _questionnaireProgressText.text = "";
        _questionnaireValueText.text = "";
        yield return WaitForSecondsOrN(1.25f);

        _questionnaireRoot.gameObject.SetActive(false);
        _questionnaireActive = false;
        _questionnaireStageActive = false;
        SetQuestionnaireJumpSuppressed(false);
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;
    }

    void SetQuestionnaireJumpSuppressed(bool suppress)
    {
        if (!suppress)
        {
            for (int i = 0; i < _questionnaireDisabledJumpBehaviours.Count; i++)
            {
                Behaviour behaviour = _questionnaireDisabledJumpBehaviours[i];
                if (behaviour != null)
                    behaviour.enabled = true;
            }
            _questionnaireDisabledJumpBehaviours.Clear();
            return;
        }

        _questionnaireDisabledJumpBehaviours.Clear();
        MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || !behaviour.enabled || behaviour == this)
                continue;

            string typeName = behaviour.GetType().Name;
            string objectName = behaviour.gameObject.name;
            bool isJumpProvider = typeName.IndexOf("Jump", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  objectName.Equals("Jump", StringComparison.OrdinalIgnoreCase);
            bool consumesPrimaryForPanelMovement = typeName.Equals("MoveKnobPlane", StringComparison.Ordinal);
            if (!isJumpProvider && !consumesPrimaryForPanelMovement)
                continue;

            behaviour.enabled = false;
            _questionnaireDisabledJumpBehaviours.Add(behaviour);
        }

        Debug.Log($"[PAXSM Questionnaire] Disabled {_questionnaireDisabledJumpBehaviours.Count} " +
                  "A-button jump/panel-move behaviour(s) during questionnaire mode.", this);
    }

    TlxItem[] BuildTlxItems()
    {
        TextAsset bankAsset = Resources.Load<TextAsset>(questionnaireBankResourcesPath);
        if (bankAsset != null)
        {
            try
            {
                LikertSurveyConfig bank = JsonUtility.FromJson<LikertSurveyConfig>(bankAsset.text);
                if (bank != null && bank.items != null && bank.items.Count > 0)
                {
                    questionnaireScale = Mathf.Max(2, bank.scale);
                    string defaultLeft = bank.labels != null && bank.labels.Count > 0
                        ? bank.labels[0]
                        : "Very low";
                    string defaultRight = bank.labels != null && bank.labels.Count > 1
                        ? bank.labels[bank.labels.Count - 1]
                        : "Very high";

                    var loaded = new List<TlxItem>(bank.items.Count);
                    for (int i = 0; i < bank.items.Count; i++)
                    {
                        LikertItem source = bank.items[i];
                        if (source == null || string.IsNullOrWhiteSpace(source.stem))
                            continue;

                        string normalizedId = (source.id ?? $"item_{i + 1}").Trim();
                        bool performance = normalizedId.IndexOf("performance", StringComparison.OrdinalIgnoreCase) >= 0;
                        loaded.Add(new TlxItem
                        {
                            itemId = normalizedId,
                            dimension = QuestionnaireDimensionFromId(normalizedId, i),
                            prompt = source.stem.Trim(),
                            leftAnchor = performance ? "Very successful" : defaultLeft,
                            rightAnchor = performance ? "Not successful" : defaultRight,
                            performanceLike = performance
                        });
                    }

                    if (loaded.Count > 0)
                    {
                        Debug.Log($"[PAXSM Questionnaire] Loaded {loaded.Count} item(s), scale={questionnaireScale}, " +
                                  $"from Resources/{questionnaireBankResourcesPath}.json", this);
                        return loaded.ToArray();
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PAXSM Questionnaire] Could not parse Resources/{questionnaireBankResourcesPath}.json: " +
                                 exception.Message, this);
            }
        }

        Debug.LogWarning("[PAXSM Questionnaire] Falling back to built-in NASA-TLX items because the MainScene bank was unavailable.", this);
        return new[]
        {
            new TlxItem
            {
                itemId = "nasa_tlx_mental",
                dimension = "Mental Demand",
                prompt = "How mentally demanding was the block you just completed?",
                leftAnchor = "Very low",
                rightAnchor = "Very high"
            },
            new TlxItem
            {
                itemId = "nasa_tlx_physical",
                dimension = "Physical Demand",
                prompt = "How physically demanding was the block you just completed?",
                leftAnchor = "Very low",
                rightAnchor = "Very high"
            },
            new TlxItem
            {
                itemId = "nasa_tlx_temporal",
                dimension = "Temporal Demand",
                prompt = "How hurried or time-pressured did the block feel?",
                leftAnchor = "Very low",
                rightAnchor = "Very high"
            },
            new TlxItem
            {
                itemId = "nasa_tlx_performance",
                dimension = "Performance",
                prompt = "How successful do you feel you were in this block?",
                leftAnchor = "Very successful",
                rightAnchor = "Not successful",
                performanceLike = true
            },
            new TlxItem
            {
                itemId = "nasa_tlx_effort",
                dimension = "Effort",
                prompt = "How hard did you have to work to accomplish this block?",
                leftAnchor = "Very low",
                rightAnchor = "Very high"
            },
            new TlxItem
            {
                itemId = "nasa_tlx_frustration",
                dimension = "Frustration",
                prompt = "How irritated, stressed, or annoyed did you feel during this block?",
                leftAnchor = "Very low",
                rightAnchor = "Very high"
            }
        };
    }

    string QuestionnaireDimensionFromId(string itemId, int fallbackIndex)
    {
        string normalized = (itemId ?? "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        if (normalized.Contains("mental")) return "Mental Demand";
        if (normalized.Contains("physical")) return "Physical Demand";
        if (normalized.Contains("temporal") || normalized.Contains("time")) return "Temporal Demand";
        if (normalized.Contains("performance") || normalized.Contains("success")) return "Performance";
        if (normalized.Contains("effort")) return "Effort";
        if (normalized.Contains("frustration") || normalized.Contains("stress")) return "Frustration";
        return $"Item {fallbackIndex + 1}";
    }

    void AppendMainSceneCompatibleSummaries(QuestionnaireRecord record)
    {
        if (_mainSceneAnswerExportMirror == null || _mainSceneConfidenceExportMirror == null || record == null)
            return;

        int qIndex0 = Mathf.Max(0, record.itemIndex - 1);
        int qIndex1 = qIndex0 + 1;
        int enterCount = Mathf.Max(1, record.presentationOrder);
        string mark = $"{qIndex1}-{enterCount}";

        var answer = new KnobCore.KnobMarkSummary
        {
            mark = mark,
            itemId = record.itemId,
            qIndex0 = qIndex0,
            qIndex1 = qIndex1,
            enterCount = enterCount,
            role = "Answer",
            stage = "Answer",
            t_answer_in = 0f,
            t_answer_out = record.answerDecisionRt,
            tickCount = record.scale,
            currentSlot = record.selectedScore,
            currentAngleY = SlotAngle(record.selectedScore, record.scale, QuestionnaireKnobMinAngle(), QuestionnaireKnobMaxAngle()),
            slotChangeCount = record.answerSlotChangeCount,
            reverseCount = record.answerReverseCount,
            pauseCount = record.answerPauseCount,
            confirmCount = 1,
            microAdjustCount = record.answerMicroAdjustCount,
            fastFlickCount = record.answerFastFlickCount,
            maxFlickVel = record.answerFastFlickCount > 0 ? record.answerMaxAbsVel : 0f,
            maxAbsVel = record.answerMaxAbsVel,
            activeMoveCount = record.answerSlotChangeCount,
            totalAbsAngle = record.answerTotalAbsAngle,
            speedBandValid = false,
            speedBandNote = "workload_probe_stage_adapter"
        };

        var confidence = new KnobCore.KnobMarkSummary
        {
            mark = mark,
            itemId = record.itemId,
            qIndex0 = qIndex0,
            qIndex1 = qIndex1,
            enterCount = enterCount,
            role = "Confidence",
            stage = "Submit",
            t_conf_in = 0f,
            t_conf_out = record.confidenceDecisionRt,
            tickCount = questionnaireConfidenceScale,
            currentSlot = record.confidence,
            currentAngleY = SlotAngle(record.confidence, questionnaireConfidenceScale, QuestionnaireKnobMinAngle(), QuestionnaireKnobMaxAngle()),
            slotChangeCount = record.confidenceSlotChangeCount,
            reverseCount = record.confidenceReverseCount,
            pauseCount = record.confidencePauseCount,
            confirmCount = record.confidence >= 0 ? 1 : 0,
            microAdjustCount = record.confidenceMicroAdjustCount,
            fastFlickCount = record.confidenceFastFlickCount,
            maxFlickVel = record.confidenceFastFlickCount > 0 ? record.confidenceMaxAbsVel : 0f,
            maxAbsVel = record.confidenceMaxAbsVel,
            activeMoveCount = record.confidenceSlotChangeCount,
            totalAbsAngle = record.confidenceTotalAbsAngle,
            speedBandValid = false,
            speedBandNote = "workload_probe_stage_adapter"
        };

        _mainSceneAnswerExportMirror.summaries.Add(answer);
        _mainSceneConfidenceExportMirror.summaries.Add(confidence);
    }

    float SlotAngle(int slot, int scale, float minAngle, float maxAngle)
    {
        if (scale <= 1 || slot <= 0)
            return 0f;
        float t = (Mathf.Clamp(slot, 1, scale) - 1f) / (scale - 1f);
        return Mathf.Lerp(minAngle, maxAngle, t);
    }

    IEnumerator RunQuestionnaireSelectionStage(
        ProbeBlockProfile profile,
        string title,
        string prompt,
        string leftAnchor,
        string rightAnchor,
        int scale,
        string progress,
        bool confidenceStage,
        QuestionnaireMotionStats stats)
    {
        _questionnaireStageActive = true;
        _questionnaireSelectedValue = -1;
        _questionnaireHoverValue = Mathf.CeilToInt(scale * 0.5f);
        SetupQuestionnaireScale(scale);
        UpdateQuestionnaireTickVisuals(_questionnaireHoverValue, _questionnaireSelectedValue);

        _questionnaireTitleText.text = title;
        _questionnairePromptText.text = WrapForWall(prompt, 62);
        _questionnaireScaleText.text = leftAnchor;
        _questionnaireScaleRightText.text = rightAnchor;
        _questionnaireProgressText.text = progress;
        _questionnaireValueText.text = $"{_questionnaireHoverValue}";

        stats.stageStartTime = Time.time;
        stats.lastSampleTime = Time.time;
        stats.lastSlot = _questionnaireHoverValue;
        stats.lastSlotChangeTime = Time.time;
        stats.pauseCountedForCurrentDwell = false;
        _activeQuestionnaireMotionStats = stats;
        ResetQuestionnaireConfirmHold(IsQuestionnaireConfirmHeldNow());

        while (_questionnaireSelectedValue < 0)
        {
            HandleQuestionnaireKeyboard(scale);

            bool hasGrabKnob = _paxsmQuestionnaireKnobCore != null && _paxsmQuestionnaireGrab != null;
            if (hasGrabKnob)
            {
                _questionnaireHoverValue = _paxsmQuestionnaireKnobCore.CurrentSlot;
                TrackQuestionnaireKnobDwell(stats);
                if (_selectionRayRenderer != null)
                    _selectionRayRenderer.enabled = false;
            }
            else
            {
                GetSelectionPressed(out Ray pointerRay, out Vector3 pointerOrigin);
                int hoverValue = ResolveQuestionnaireScaleValue(pointerRay, scale);
                if (hoverValue > 0)
                    _questionnaireHoverValue = hoverValue;
                TrackQuestionnairePointerMotion(pointerOrigin, stats);
                UpdateSelectionRayVisual(pointerRay);
            }

            UpdateQuestionnaireTickVisuals(_questionnaireHoverValue, _questionnaireSelectedValue);
            _questionnaireValueText.text = $"{_questionnaireHoverValue}";

            bool canConfirm = _questionnaireHoverValue > 0 &&
                              (!hasGrabKnob || !_paxsmQuestionnaireGrab.IsGrabbing);
            if (UpdateQuestionnaireConfirmHold(canConfirm, stats))
            {
                _questionnaireSelectedValue = _questionnaireHoverValue;
            }

            yield return null;
        }

        _activeQuestionnaireMotionStats = null;
        stats.duration = Time.time - stats.stageStartTime;
        UpdateQuestionnaireTickVisuals(_questionnaireHoverValue, _questionnaireSelectedValue);
        _questionnaireValueText.text = $"Confirmed: {_questionnaireSelectedValue}";
        UpdateQuestionnaireConfirmVisual(1f, confirmed: true);
        yield return new WaitForSeconds(0.35f);
        _questionnaireStageActive = false;
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;
    }

    void SetupQuestionnaireScale(int scale)
    {
        ClearQuestionnaireTicks();
        _questionnaireCurrentScale = scale;
        BuildQuestionnaireWallScale(scale);
        BuildQuestionnaireKnobPanel();

        if (useMainSceneKnobRig && TrySetupMainSceneKnobRig(scale))
            return;

        Vector3 center = QuestionnaireKnobCenter();
        float radius = QuestionnaireKnobRadius();

        _questionnaireKnobFace = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _questionnaireKnobFace.name = "PAXSM_KnobFace";
        _questionnaireKnobFace.transform.SetParent(_questionnaireRoot, false);
        _questionnaireKnobFace.transform.position = center;
        _questionnaireKnobFace.transform.rotation = QuestionnairePanelRotation() * Quaternion.Euler(90f, 0f, 0f);
        _questionnaireKnobFace.transform.localScale = new Vector3(radius * 1.55f, 0.075f, radius * 1.55f);
        Renderer faceRenderer = _questionnaireKnobFace.GetComponent<Renderer>();
        if (faceRenderer != null)
            faceRenderer.sharedMaterial = _normalMaterial;
        _questionnaireTicks.Add(_questionnaireKnobFace);

        _questionnaireKnobPointer = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _questionnaireKnobPointer.name = "PAXSM_KnobPointer";
        _questionnaireKnobPointer.transform.SetParent(_questionnaireRoot, false);
        Renderer pointerRenderer = _questionnaireKnobPointer.GetComponent<Renderer>();
        if (pointerRenderer != null)
            pointerRenderer.sharedMaterial = _questionnaireSelectedMaterial;
        _questionnaireTicks.Add(_questionnaireKnobPointer);

        _questionnaireKnobHub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _questionnaireKnobHub.name = "PAXSM_KnobHub";
        _questionnaireKnobHub.transform.SetParent(_questionnaireRoot, false);
        _questionnaireKnobHub.transform.position = center + QuestionnairePanelFront() * 0.055f;
        _questionnaireKnobHub.transform.localScale = Vector3.one * 0.12f;
        Renderer hubRenderer = _questionnaireKnobHub.GetComponent<Renderer>();
        if (hubRenderer != null)
            hubRenderer.sharedMaterial = _questionnaireHoverMaterial;
        _questionnaireTicks.Add(_questionnaireKnobHub);

        for (int value = 1; value <= scale; value++)
        {
            float t = scale == 1 ? 0.5f : (value - 1) / (float)(scale - 1);
            float angle = Mathf.Lerp(QuestionnaireKnobMinAngle(), QuestionnaireKnobMaxAngle(), t);
            Vector3 dir = QuestionnaireKnobDirection(angle);
            GameObject tick = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tick.name = $"PAXSM_KnobTick_{value:00}";
            tick.transform.SetParent(_questionnaireRoot, false);
            tick.transform.position = center + dir * radius + QuestionnairePanelFront() * 0.055f;
            tick.transform.rotation = Quaternion.LookRotation(QuestionnairePanelFront(), dir);
            bool major = value == 1 || value == scale || value == Mathf.CeilToInt(scale * 0.5f) || (scale == 21 && (value - 1) % 5 == 0);
            bool denseScale = scale >= 15;
            tick.transform.localScale = new Vector3(
                denseScale ? (major ? 0.020f : 0.014f) : (major ? 0.035f : 0.024f),
                denseScale ? (major ? 0.11f : 0.075f) : (major ? 0.13f : 0.085f),
                denseScale ? 0.025f : 0.035f);
            Renderer renderer = tick.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = _questionnaireTickMaterial;
            _questionnaireTicks.Add(tick);
        }
    }

    void ClearQuestionnaireTicks()
    {
        if (_paxsmQuestionnaireKnobCore != null)
            _paxsmQuestionnaireKnobCore.OnSelectionChanged -= HandleQuestionnaireKnobSelectionChanged;

        for (int i = _questionnaireTicks.Count - 1; i >= 0; i--)
        {
            if (_questionnaireTicks[i] != null)
                Destroy(_questionnaireTicks[i]);
        }
        _questionnaireTicks.Clear();

        for (int i = _questionnairePanelObjects.Count - 1; i >= 0; i--)
        {
            if (_questionnairePanelObjects[i] != null)
                Destroy(_questionnairePanelObjects[i]);
        }
        _questionnairePanelObjects.Clear();

        for (int i = _questionnaireWallTicks.Count - 1; i >= 0; i--)
        {
            if (_questionnaireWallTicks[i] != null)
                Destroy(_questionnaireWallTicks[i]);
        }
        _questionnaireWallTicks.Clear();

        _questionnaireKnobFace = null;
        _questionnaireKnobPointer = null;
        _questionnaireKnobHub = null;
        _paxsmKnobRigInstance = null;
        _paxsmQuestionnaireKnobCore = null;
        _paxsmQuestionnaireTickRing = null;
        _paxsmQuestionnaireGrab = null;
        _paxsmQuestionnaireModeManager = null;
        _questionnaireKnobHintText = null;
        _questionnaireConfirmTrack = null;
        _questionnaireConfirmFill = null;
        _activeQuestionnaireMotionStats = null;
        _paxsmQuestionnaireLastSlot = -1;
    }

    void UpdateQuestionnaireTickVisuals(int hoverValue, int selectedValue)
    {
        UpdateQuestionnaireWallScaleVisuals(hoverValue, selectedValue);

        if (_paxsmQuestionnaireKnobCore != null)
        {
            _paxsmQuestionnaireLastSlot = _paxsmQuestionnaireKnobCore.CurrentSlot;
            return;
        }

        int scale = Mathf.Max(1, _questionnaireTicks.Count - 3);
        UpdateQuestionnaireKnobPointer(hoverValue, scale);

        for (int i = 3; i < _questionnaireTicks.Count; i++)
        {
            GameObject tick = _questionnaireTicks[i];
            if (tick == null)
                continue;

            int value = i - 2;
            Renderer renderer = tick.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (value == selectedValue)
                    renderer.sharedMaterial = _questionnaireSelectedMaterial;
                else if (value == hoverValue)
                    renderer.sharedMaterial = _questionnaireHoverMaterial;
                else
                    renderer.sharedMaterial = _questionnaireTickMaterial;
            }

            bool major = value == 1 || value == scale || value == Mathf.CeilToInt(scale * 0.5f) || (scale == 21 && (value - 1) % 5 == 0);
            bool denseScale = scale >= 15;
            float width = value == hoverValue || value == selectedValue
                ? (denseScale ? 0.030f : 0.075f)
                : (denseScale ? (major ? 0.020f : 0.014f) : (major ? 0.055f : 0.035f));
            float length = value == hoverValue || value == selectedValue
                ? (denseScale ? 0.14f : 0.20f)
                : (denseScale ? (major ? 0.11f : 0.075f) : (major ? 0.16f : 0.10f));
            tick.transform.localScale = new Vector3(width * 0.55f, length, 0.04f);
        }
    }

    void UpdateQuestionnaireKnobPointer(int value, int scale)
    {
        if (_questionnaireKnobPointer == null)
            return;

        float t = scale <= 1 ? 0.5f : (Mathf.Clamp(value, 1, scale) - 1) / (float)(scale - 1);
        float angle = Mathf.Lerp(QuestionnaireKnobMinAngle(), QuestionnaireKnobMaxAngle(), t);
        Vector3 center = QuestionnaireKnobCenter();
        Vector3 dir = QuestionnaireKnobDirection(angle);
        float length = QuestionnaireKnobRadius() * 0.82f;
        _questionnaireKnobPointer.transform.position = center + QuestionnairePanelFront() * 0.065f + dir * (length * 0.5f);
        _questionnaireKnobPointer.transform.rotation = Quaternion.LookRotation(QuestionnairePanelFront(), dir);
        _questionnaireKnobPointer.transform.localScale = new Vector3(0.045f, length, 0.045f);
    }

    bool TrySetupMainSceneKnobRig(int scale)
    {
        GameObject prefab = paxsmKnobRigPrefab != null
            ? paxsmKnobRigPrefab
            : Resources.Load<GameObject>("PAXSM/KnobRig");
        if (prefab == null)
            return false;

        _paxsmKnobRigInstance = Instantiate(prefab, _questionnaireRoot);
        _paxsmKnobRigInstance.name = "PAXSM_MainSceneKnobRig_Runtime";
        _paxsmKnobRigInstance.transform.position = QuestionnaireKnobCenter();
        _paxsmKnobRigInstance.transform.rotation = QuestionnairePanelRotation() * Quaternion.Euler(-90f, 0f, 0f);
        _paxsmKnobRigInstance.transform.localScale = Vector3.one;
        _questionnaireTicks.Add(_paxsmKnobRigInstance);

        Transform knobTransform = FindDeepChild(_paxsmKnobRigInstance.transform, "Knob");
        if (knobTransform == null)
            knobTransform = _paxsmKnobRigInstance.transform;

        FitKnobRigToWorldDiameter(
            _paxsmKnobRigInstance,
            knobTransform,
            RuntimeQuestionnaireKnobDiameterMeters);

        GameObject tickTemplate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tickTemplate.name = "PAXSM_RuntimeTickTemplate";
        tickTemplate.transform.SetParent(_paxsmKnobRigInstance.transform, false);
        float actualRigScale = Mathf.Max(0.01f, Mathf.Abs(_paxsmKnobRigInstance.transform.lossyScale.x));
        float inverseRigScale = 1f / actualRigScale;
        Vector3 tickWorldSize = scale >= 15
            ? new Vector3(0.0032f, 0.020f, 0.0045f)
            : new Vector3(0.006f, 0.026f, 0.006f);
        tickTemplate.transform.localScale = Vector3.Scale(
            tickWorldSize,
            Vector3.one * inverseRigScale);
        Renderer tickRenderer = tickTemplate.GetComponent<Renderer>();
        if (tickRenderer != null)
            tickRenderer.sharedMaterial = _questionnaireTickMaterial;
        tickTemplate.SetActive(false);

        _paxsmQuestionnaireTickRing = _paxsmKnobRigInstance.AddComponent<TickRingLocal>();
        _paxsmQuestionnaireTickRing.knobRoot = knobTransform;
        _paxsmQuestionnaireTickRing.tickPrefab = tickTemplate;
        _paxsmQuestionnaireTickRing.autoHideTemplate = true;
        _paxsmQuestionnaireTickRing.countMode = TickRingLocal.CountMode.Normal_FromJsonScale;
        _paxsmQuestionnaireTickRing.fallbackScale = Mathf.Max(2, scale);
        _paxsmQuestionnaireTickRing.radius = 0.085f;
        _paxsmQuestionnaireTickRing.startDeg = QuestionnaireKnobMinAngle();
        _paxsmQuestionnaireTickRing.endDeg = QuestionnaireKnobMaxAngle();
        _paxsmQuestionnaireTickRing.longAxis = TickRingLocal.LongAxis.Y;
        _paxsmQuestionnaireTickRing.followKnobRotation = false;
        _paxsmQuestionnaireTickRing.useInitialKnobRotation = false;
        _paxsmQuestionnaireTickRing.lookOutward = true;
        _paxsmQuestionnaireTickRing.Rebuild();

        _paxsmQuestionnaireKnobCore = _paxsmKnobRigInstance.AddComponent<KnobCore>();
        _paxsmQuestionnaireKnobCore.knob = knobTransform;
        _paxsmQuestionnaireKnobCore.rotateSpeed = 1440f;
        _paxsmQuestionnaireKnobCore.enableHapticFeedback = true;
        _paxsmQuestionnaireKnobCore.enableTickSound = false;
        _paxsmQuestionnaireKnobCore.suppressMissingHighlightWarnings = true;
        _paxsmQuestionnaireKnobCore.useGlobalLighting = false;
        _paxsmQuestionnaireKnobCore.finalizeOnDisable = false;
        _paxsmQuestionnaireKnobCore.countInAnswerStage = false;
        _paxsmQuestionnaireKnobCore.countInSubmitStage = false;
        _paxsmQuestionnaireKnobCore.countInReadStage = false;
        _paxsmQuestionnaireKnobCore.RuntimeBind(_paxsmQuestionnaireTickRing, knobTransform);

        _paxsmQuestionnaireModeManager = _paxsmKnobRigInstance.AddComponent<KnobModeManager>();
        _paxsmQuestionnaireModeManager.controlMode = KnobModeManager.ControlMode.GestureOnly;
        _paxsmQuestionnaireModeManager.knobCore = _paxsmQuestionnaireKnobCore;
        _paxsmQuestionnaireModeManager.hapticSlotsCount = Mathf.Max(2, scale);

        _paxsmQuestionnaireGrab = _paxsmKnobRigInstance.AddComponent<KnobGrabByWrist>();
        _paxsmQuestionnaireGrab.knobCore = _paxsmQuestionnaireKnobCore;
        _paxsmQuestionnaireGrab.knobModeManager = _paxsmQuestionnaireModeManager;
        _paxsmQuestionnaireGrab.knobCenter = knobTransform;
        _paxsmQuestionnaireGrab.grabRadius = questionnaireGrabRadius;
        _paxsmQuestionnaireGrab.deadZoneDegrees = 2f;
        _paxsmQuestionnaireGrab.maxTwistDegrees = 130f;
        _paxsmQuestionnaireGrab.autoDegreesPerStepFromRing = true;
        if (TryGetQuestionnaireRightControllerTransform(out Transform rightController))
            _paxsmQuestionnaireGrab.rightController = rightController;

        _paxsmQuestionnaireKnobCore.OnSelectionChanged += HandleQuestionnaireKnobSelectionChanged;
        _paxsmQuestionnaireKnobCore.InitToMiddle();
        _paxsmQuestionnaireLastSlot = _paxsmQuestionnaireKnobCore.CurrentSlot;
        return true;
    }

    void FitKnobRigToWorldDiameter(GameObject rig, Transform knobTransform, float targetDiameter)
    {
        if (rig == null || knobTransform == null)
            return;

        Renderer[] renderers = knobTransform.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
            return;

        float currentDiameter = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (currentDiameter <= 0.0001f)
            return;

        float scaleFactor = Mathf.Clamp(targetDiameter, 0.03f, 0.15f) / currentDiameter;
        rig.transform.localScale *= scaleFactor;

        Renderer[] resizedRenderers = knobTransform.GetComponentsInChildren<Renderer>(true);
        bool hasResizedBounds = false;
        Bounds resizedBounds = default;
        for (int i = 0; i < resizedRenderers.Length; i++)
        {
            Renderer renderer = resizedRenderers[i];
            if (renderer == null)
                continue;
            if (!hasResizedBounds)
            {
                resizedBounds = renderer.bounds;
                hasResizedBounds = true;
            }
            else
            {
                resizedBounds.Encapsulate(renderer.bounds);
            }
        }

        float actualDiameter = hasResizedBounds
            ? Mathf.Max(resizedBounds.size.x, resizedBounds.size.y, resizedBounds.size.z)
            : targetDiameter;
        Debug.Log($"[PAXSM Questionnaire] Knob normalized to {actualDiameter * 100f:F1} cm " +
                  $"(target {targetDiameter * 100f:F1} cm, root scale {rig.transform.localScale.x:F3}).", rig);
    }

    void BuildQuestionnaireWallScale(int scale)
    {
        float left = -2.05f;
        float right = 2.05f;
        float y = 1.37f;
        float z = 4.19f;

        for (int value = 1; value <= scale; value++)
        {
            float t = scale <= 1 ? 0.5f : (value - 1) / (float)(scale - 1);
            bool major = value == 1 || value == scale || value == Mathf.CeilToInt(scale * 0.5f) ||
                         (scale == 21 && (value - 1) % 5 == 0);

            GameObject tick = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tick.name = $"PAXSM_WallScaleTick_{value:00}";
            tick.transform.SetParent(_questionnaireRoot, false);
            tick.transform.position = new Vector3(Mathf.Lerp(left, right, t), y, z);
            tick.transform.localScale = new Vector3(major ? 0.045f : 0.026f, major ? 0.18f : 0.11f, 0.035f);
            Collider collider = tick.GetComponent<Collider>();
            if (collider != null)
                collider.enabled = false;
            Renderer renderer = tick.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = _questionnaireTickMaterial;
            _questionnaireWallTicks.Add(tick);
        }
    }

    void BuildQuestionnaireKnobPanel()
    {
        Vector3 center = QuestionnaireKnobCenter();

        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "PAXSM_KnobBackingPanel";
        panel.transform.SetParent(_questionnaireRoot, false);
        panel.transform.position = QuestionnairePanelPoint(new Vector3(0f, -0.005f, 0.045f));
        panel.transform.rotation = QuestionnairePanelRotation();
        panel.transform.localScale = new Vector3(0.20f, 0.21f, 0.03f);
        Collider panelCollider = panel.GetComponent<Collider>();
        if (panelCollider != null)
            panelCollider.enabled = false;
        Renderer panelRenderer = panel.GetComponent<Renderer>();
        if (panelRenderer != null)
            panelRenderer.sharedMaterial = _questionnairePanelMaterial;
        _questionnairePanelObjects.Add(panel);

        _questionnaireConfirmTrack = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _questionnaireConfirmTrack.name = "PAXSM_ConfirmProgressTrack";
        _questionnaireConfirmTrack.transform.SetParent(_questionnaireRoot, false);
        _questionnaireConfirmTrack.transform.position = QuestionnairePanelPoint(new Vector3(0f, -0.085f, -0.035f));
        _questionnaireConfirmTrack.transform.rotation = QuestionnairePanelRotation();
        _questionnaireConfirmTrack.transform.localScale = new Vector3(0.16f, 0.008f, 0.008f);
        Collider trackCollider = _questionnaireConfirmTrack.GetComponent<Collider>();
        if (trackCollider != null)
            trackCollider.enabled = false;
        Renderer trackRenderer = _questionnaireConfirmTrack.GetComponent<Renderer>();
        if (trackRenderer != null)
            trackRenderer.sharedMaterial = _questionnaireTickMaterial;
        _questionnairePanelObjects.Add(_questionnaireConfirmTrack);

        _questionnaireConfirmFill = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _questionnaireConfirmFill.name = "PAXSM_ConfirmProgressFill";
        _questionnaireConfirmFill.transform.SetParent(_questionnaireRoot, false);
        _questionnaireConfirmFill.transform.rotation = QuestionnairePanelRotation();
        Collider fillCollider = _questionnaireConfirmFill.GetComponent<Collider>();
        if (fillCollider != null)
            fillCollider.enabled = false;
        Renderer fillRenderer = _questionnaireConfirmFill.GetComponent<Renderer>();
        if (fillRenderer != null)
            fillRenderer.sharedMaterial = _questionnaireSelectedMaterial;
        _questionnairePanelObjects.Add(_questionnaireConfirmFill);

        GameObject hintObject = new GameObject("PAXSM_KnobHint");
        hintObject.transform.SetParent(_questionnaireRoot, false);
        hintObject.transform.position = QuestionnairePanelPoint(new Vector3(0f, -0.115f, -0.03f));
        hintObject.transform.rotation = QuestionnairePanelRotation();
        _questionnaireKnobHintText = hintObject.AddComponent<TextMesh>();
        _questionnaireKnobHintText.text = "B + Trigger: rotate     Hold A: confirm";
        _questionnaireKnobHintText.fontSize = 32;
        _questionnaireKnobHintText.characterSize = 0.0032f;
        _questionnaireKnobHintText.anchor = TextAnchor.MiddleCenter;
        _questionnaireKnobHintText.alignment = TextAlignment.Center;
        _questionnaireKnobHintText.color = new Color(0.78f, 0.86f, 0.92f);
        _questionnairePanelObjects.Add(hintObject);
        UpdateQuestionnaireConfirmVisual(0f, confirmed: false);
    }

    void UpdateQuestionnaireWallScaleVisuals(int hoverValue, int selectedValue)
    {
        for (int i = 0; i < _questionnaireWallTicks.Count; i++)
        {
            GameObject tick = _questionnaireWallTicks[i];
            if (tick == null)
                continue;

            int value = i + 1;
            bool major = value == 1 || value == _questionnaireWallTicks.Count ||
                         value == Mathf.CeilToInt(_questionnaireWallTicks.Count * 0.5f) ||
                         (_questionnaireWallTicks.Count == 21 && (value - 1) % 5 == 0);
            bool confirmed = value == selectedValue;
            bool pending = selectedValue < 0 && value == hoverValue;

            Renderer renderer = tick.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = confirmed ? _questionnaireSelectedMaterial :
                    (pending ? _questionnaireHoverMaterial : _questionnaireTickMaterial);

            float width = confirmed ? 0.075f : (pending ? 0.065f : (major ? 0.045f : 0.026f));
            float height = confirmed ? 0.30f : (pending ? 0.25f : (major ? 0.18f : 0.11f));
            tick.transform.localScale = new Vector3(width, height, 0.035f);
        }
    }

    void HandleQuestionnaireKnobSelectionChanged(int slot)
    {
        _questionnaireHoverValue = slot;
        _paxsmQuestionnaireLastSlot = slot;

        QuestionnaireMotionStats stats = _activeQuestionnaireMotionStats;
        if (stats == null)
            return;

        float now = Time.time;
        if (stats.lastSlot < 0)
        {
            stats.lastSlot = slot;
            stats.lastSlotChangeTime = now;
            return;
        }

        int deltaSlots = slot - stats.lastSlot;
        if (deltaSlots == 0)
            return;

        float dt = Mathf.Max(0.001f, now - stats.lastSlotChangeTime);
        float degreesPerSlot = _questionnaireCurrentScale <= 1
            ? 0f
            : Mathf.Abs(QuestionnaireKnobMaxAngle() - QuestionnaireKnobMinAngle()) /
              (_questionnaireCurrentScale - 1f);
        float deltaAngle = Mathf.Abs(deltaSlots) * degreesPerSlot;
        float absVelocity = deltaAngle / dt;
        int direction = Math.Sign(deltaSlots);

        stats.slotChangeCount++;
        stats.hoverChangeCount++;
        stats.path += Mathf.Abs(deltaSlots);
        stats.totalAbsAngle += deltaAngle;
        stats.peakSpeed = Mathf.Max(stats.peakSpeed, absVelocity);
        stats.maxAbsVel = Mathf.Max(stats.maxAbsVel, absVelocity);
        if (stats.lastDirection != 0 && direction != stats.lastDirection)
            stats.reverseCount++;
        if (Mathf.Abs(deltaSlots) <= 1 && absVelocity <= degreesPerSlot * 7f)
            stats.microAdjustCount++;
        if (degreesPerSlot > 0f && absVelocity / degreesPerSlot >= 60f)
            stats.fastFlickCount++;

        stats.lastDirection = direction;
        stats.lastSlot = slot;
        stats.lastSlotChangeTime = now;
        stats.pauseCountedForCurrentDwell = false;
    }

    void TrackQuestionnaireKnobDwell(QuestionnaireMotionStats stats)
    {
        if (stats == null || stats.pauseCountedForCurrentDwell)
            return;
        if (Time.time - stats.lastSlotChangeTime < 0.25f)
            return;

        stats.pauseCount++;
        stats.pauseCountedForCurrentDwell = true;
    }

    bool TryGetQuestionnaireRightControllerTransform(out Transform controller)
    {
        Transform[] transforms = FindObjectsOfType<Transform>(true);
        Transform best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            string path = GetTransformPath(candidate).ToLowerInvariant();
            if (!path.Contains("right"))
                continue;

            int score = 0;
            string objectName = candidate.name.ToLowerInvariant();
            if (objectName.Contains("right controller") || objectName.Contains("rightcontroller")) score += 12;
            if (objectName.Contains("right hand") || objectName.Contains("righthand")) score += 8;
            if (path.Contains("controller")) score += 4;
            if (HasComponentNamed(candidate, "ActionBasedController") || HasComponentNamed(candidate, "XRController")) score += 8;
            if (path.Contains("ray") || path.Contains("line") || path.Contains("visual") || path.Contains("model")) score -= 10;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best != null)
        {
            controller = best;
            return true;
        }

        if (TryGetRightPointerTransform(out controller))
            return true;

        GameObject proxy = GameObject.Find("PAXSM_RightControllerPoseProxy");
        if (proxy == null)
            proxy = new GameObject("PAXSM_RightControllerPoseProxy");
        XRNodePoseFollower follower = proxy.GetComponent<XRNodePoseFollower>();
        if (follower == null)
            follower = proxy.AddComponent<XRNodePoseFollower>();
        follower.node = XRNode.RightHand;
        controller = proxy.transform;
        return true;
    }

    Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
                return child;
            Transform nested = FindDeepChild(child, childName);
            if (nested != null)
                return nested;
        }
        return null;
    }

    int ResolveQuestionnaireScaleValue(Ray ray, int scale)
    {
        Plane knobPlane = new Plane(QuestionnairePanelRotation() * Vector3.forward, QuestionnaireKnobCenter());
        if (!knobPlane.Raycast(ray, out float enter))
            return -1;

        Vector3 hit = ray.GetPoint(enter);
        Vector3 offset = Quaternion.Inverse(QuestionnairePanelRotation()) * (hit - QuestionnaireKnobCenter());
        offset.z = 0f;
        if (offset.magnitude > QuestionnaireKnobRadius() * 1.55f)
            return -1;
        if (offset.magnitude < QuestionnaireKnobRadius() * 0.18f)
            return _questionnaireHoverValue > 0 ? _questionnaireHoverValue : Mathf.CeilToInt(scale * 0.5f);

        float angle = Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg;
        float clampedAngle = Mathf.Clamp(angle, QuestionnaireKnobMinAngle(), QuestionnaireKnobMaxAngle());
        float t = Mathf.InverseLerp(QuestionnaireKnobMinAngle(), QuestionnaireKnobMaxAngle(), clampedAngle);
        return Mathf.Clamp(Mathf.RoundToInt(t * (scale - 1)) + 1, 1, scale);
    }

    void TrackQuestionnairePointerMotion(Vector3 pointerPosition, QuestionnaireMotionStats stats)
    {
        float now = Time.time;
        float dt = Mathf.Max(0.0001f, now - stats.lastSampleTime);
        stats.lastSampleTime = now;

        if (!stats.hasLastPointerPosition)
        {
            stats.lastPointerPosition = pointerPosition;
            stats.hasLastPointerPosition = true;
            return;
        }

        float delta = Vector3.Distance(stats.lastPointerPosition, pointerPosition);
        float speed = delta / dt;
        stats.path += delta;
        stats.peakSpeed = Mathf.Max(stats.peakSpeed, speed);
        stats.lastPointerPosition = pointerPosition;

        if (speed < 0.025f)
        {
            stats.pauseAccum += dt;
            if (stats.pauseAccum > 0.35f)
            {
                stats.pauseCount++;
                stats.pauseAccum = 0f;
            }
        }
        else
        {
            stats.pauseAccum = 0f;
        }

        if (_questionnaireHoverValue != stats.lastHoverValue)
        {
            if (stats.lastHoverValue > 0)
                stats.hoverChangeCount++;
            stats.lastHoverValue = _questionnaireHoverValue;
        }
    }

    void CopyQuestionnaireStats(QuestionnaireMotionStats stats, QuestionnaireRecord record, bool answerStage)
    {
        if (answerStage)
        {
            record.answerPointerPath = stats.path;
            record.answerPeakSpeed = stats.peakSpeed;
            record.answerPauseCount = stats.pauseCount;
            record.answerHoverChangeCount = stats.hoverChangeCount;
            record.answerTotalAbsAngle = stats.totalAbsAngle;
            record.answerMaxAbsVel = stats.maxAbsVel;
            record.answerSlotChangeCount = stats.slotChangeCount;
            record.answerReverseCount = stats.reverseCount;
            record.answerMicroAdjustCount = stats.microAdjustCount;
            record.answerFastFlickCount = stats.fastFlickCount;
        }
        else
        {
            record.confidencePointerPath = stats.path;
            record.confidencePeakSpeed = stats.peakSpeed;
            record.confidencePauseCount = stats.pauseCount;
            record.confidenceHoverChangeCount = stats.hoverChangeCount;
            record.confidenceTotalAbsAngle = stats.totalAbsAngle;
            record.confidenceMaxAbsVel = stats.maxAbsVel;
            record.confidenceSlotChangeCount = stats.slotChangeCount;
            record.confidenceReverseCount = stats.reverseCount;
            record.confidenceMicroAdjustCount = stats.microAdjustCount;
            record.confidenceFastFlickCount = stats.fastFlickCount;
        }
    }

    IEnumerator WaitForSelectionRelease()
    {
        while (SelectionHeldNow())
            yield return null;
        _prevTrigger = false;
    }

    bool SelectionHeldNow()
    {
        if (TryGetXrPointer(out _, out _, out bool triggerDown))
            return triggerDown;
        return DesktopSelectHeld();
    }

    void HandleQuestionnaireKeyboard(int scale)
    {
#if ENABLE_INPUT_SYSTEM
        InputSystemKeyboard keyboard = InputSystemKeyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.leftArrowKey.wasPressedThisFrame)
        {
            if (_paxsmQuestionnaireKnobCore != null) _paxsmQuestionnaireKnobCore.Step(-1);
            else _questionnaireHoverValue = Mathf.Max(1, _questionnaireHoverValue - 1);
        }
        if (keyboard.rightArrowKey.wasPressedThisFrame)
        {
            if (_paxsmQuestionnaireKnobCore != null) _paxsmQuestionnaireKnobCore.Step(+1);
            else _questionnaireHoverValue = Mathf.Min(scale, _questionnaireHoverValue + 1);
        }
#endif
    }

    void ResetQuestionnaireConfirmHold(bool requireRelease)
    {
        _questionnaireConfirmNeedsRelease = requireRelease;
        _questionnaireConfirmHoldStart = -1f;
        _questionnaireNextConfirmHapticTime = 0f;
        UpdateQuestionnaireConfirmVisual(0f, confirmed: false);
    }

    bool UpdateQuestionnaireConfirmHold(bool canConfirm, QuestionnaireMotionStats stats)
    {
        bool held = IsQuestionnaireConfirmHeldNow();

        if (_questionnaireConfirmNeedsRelease)
        {
            if (!held)
                _questionnaireConfirmNeedsRelease = false;
            else if (_questionnaireKnobHintText != null)
                _questionnaireKnobHintText.text = "Release A, then hold to confirm";
            return false;
        }

        if (!canConfirm)
        {
            if (held)
                _questionnaireConfirmNeedsRelease = true;
            _questionnaireConfirmHoldStart = -1f;
            UpdateQuestionnaireConfirmVisual(0f, confirmed: false);
            return false;
        }

        if (!held)
        {
            _questionnaireConfirmHoldStart = -1f;
            _questionnaireNextConfirmHapticTime = 0f;
            UpdateQuestionnaireConfirmVisual(0f, confirmed: false);
            return false;
        }

        float now = Time.unscaledTime;
        if (_questionnaireConfirmHoldStart < 0f)
        {
            _questionnaireConfirmHoldStart = now;
            _questionnaireNextConfirmHapticTime = now;
        }

        float holdSeconds = Mathf.Max(0.1f, questionnaireConfirmHoldSeconds);
        float elapsed = now - _questionnaireConfirmHoldStart;
        float progress = Mathf.Clamp01(elapsed / holdSeconds);
        UpdateQuestionnaireConfirmVisual(progress, confirmed: false);

        if (progress >= 1f)
        {
            if (stats != null)
                stats.confirmHoldDuration = holdSeconds;
            _questionnaireConfirmNeedsRelease = true;
            _questionnaireConfirmHoldStart = -1f;
            TryPlayRightHandHaptic(
                Mathf.Min(0.9f, questionnaireConfirmHapticMax + 0.18f),
                0.12f,
                force: true);
            return true;
        }

        if (now >= _questionnaireNextConfirmHapticTime)
        {
            float shapedProgress = progress * progress;
            float amplitude = Mathf.Lerp(
                questionnaireConfirmHapticMin,
                questionnaireConfirmHapticMax,
                shapedProgress);
            float pulseDuration = Mathf.Min(0.055f, questionnaireConfirmHapticInterval * 0.75f);
            TryPlayRightHandHaptic(amplitude, pulseDuration, force: true);
            _questionnaireNextConfirmHapticTime = now + Mathf.Max(0.04f, questionnaireConfirmHapticInterval);
        }

        return false;
    }

    void UpdateQuestionnaireConfirmVisual(float progress, bool confirmed)
    {
        progress = Mathf.Clamp01(progress);
        const float maxWidth = 0.16f;

        if (_questionnaireConfirmFill != null)
        {
            float width = maxWidth * progress;
            _questionnaireConfirmFill.SetActive(progress > 0.001f || confirmed);
            _questionnaireConfirmFill.transform.localScale = new Vector3(Mathf.Max(0.001f, width), 0.012f, 0.012f);
            _questionnaireConfirmFill.transform.position = QuestionnairePanelPoint(new Vector3(
                -maxWidth * 0.5f + width * 0.5f,
                -0.085f,
                -0.045f));
            _questionnaireConfirmFill.transform.rotation = QuestionnairePanelRotation();
        }

        if (_questionnaireKnobHintText == null)
            return;

        if (confirmed)
        {
            _questionnaireKnobHintText.text = "Confirmed";
            _questionnaireKnobHintText.color = new Color(1f, 0.84f, 0.2f);
        }
        else if (progress > 0f)
        {
            _questionnaireKnobHintText.text = $"Hold A to confirm  {Mathf.RoundToInt(progress * 100f)}%";
            _questionnaireKnobHintText.color = Color.Lerp(
                new Color(0.55f, 0.78f, 1f),
                new Color(1f, 0.84f, 0.2f),
                progress);
        }
        else
        {
            _questionnaireKnobHintText.text = "B + Trigger: rotate     Hold A: confirm";
            _questionnaireKnobHintText.color = new Color(0.78f, 0.86f, 0.92f);
        }
    }

    bool IsQuestionnaireConfirmHeldNow()
    {
        bool held = false;
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, _rightHandDevices);
        for (int i = 0; i < _rightHandDevices.Count; i++)
        {
            InputDevice device = _rightHandDevices[i];
            if (!device.isValid)
                continue;
            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primary) && primary)
                held = true;
        }

#if ENABLE_INPUT_SYSTEM
        InputSystemKeyboard keyboard = InputSystemKeyboard.current;
        if (keyboard != null)
        {
            held = held || (keyboard.enterKey != null && keyboard.enterKey.isPressed);
            held = held || (keyboard.numpadEnterKey != null && keyboard.numpadEnterKey.isPressed);
        }
#endif
        return held;
    }

    void CalibrateQuestionnairePlacementFromHead()
    {
        if (!questionnaireUseHeadRelativePlacement || _mainCamera == null)
        {
            _questionnairePanelYawRotation = Quaternion.identity;
            return;
        }

        Vector3 horizontalForward = Vector3.ProjectOnPlane(_mainCamera.transform.forward, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.001f)
            horizontalForward = Vector3.forward;
        horizontalForward.Normalize();

        Vector3 horizontalRight = Vector3.Cross(Vector3.up, horizontalForward).normalized;
        Vector3 headPosition = _mainCamera.transform.position;
        Vector3 calibratedPosition = headPosition +
                                     horizontalForward * questionnaireForwardOffset +
                                     horizontalRight * questionnaireRightOffset -
                                     Vector3.up * questionnaireBelowEyeOffset;
        calibratedPosition.y = Mathf.Clamp(
            calibratedPosition.y,
            questionnaireMinimumHeight,
            questionnaireMaximumHeight);

        questionnaireKnobPosition = calibratedPosition;
        _questionnairePanelYawRotation = Quaternion.LookRotation(horizontalForward, Vector3.up);
        Debug.Log(
            $"[PAXSM Questionnaire] Head-relative placement calibrated at {questionnaireKnobPosition} " +
            $"from head {headPosition}.",
            this);
    }

    Vector3 QuestionnaireKnobCenter() => questionnaireKnobPosition;
    float QuestionnaireKnobRadius() => 0.48f;
    float QuestionnaireKnobMinAngle() => -Mathf.Clamp(questionnaireKnobArcDegrees, 30f, 180f) * 0.5f;
    float QuestionnaireKnobMaxAngle() => Mathf.Clamp(questionnaireKnobArcDegrees, 30f, 180f) * 0.5f;
    Quaternion QuestionnairePanelRotation() =>
        _questionnairePanelYawRotation * Quaternion.Euler(questionnairePanelTiltDegrees, 0f, 0f);
    Vector3 QuestionnairePanelFront() => QuestionnairePanelRotation() * Vector3.back;
    Vector3 QuestionnairePanelPoint(Vector3 localOffset) =>
        QuestionnaireKnobCenter() + QuestionnairePanelRotation() * localOffset;

    Vector3 QuestionnaireKnobDirection(float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        Vector3 localDirection = new Vector3(Mathf.Sin(rad), Mathf.Cos(rad), 0f).normalized;
        return QuestionnairePanelRotation() * localDirection;
    }

    GameObject FindOrCreateRoomSurface(string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject surface = GameObject.Find(name);
        if (surface == null)
        {
            surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = name;
            surface.transform.position = position;
            surface.transform.localScale = scale;
        }

        Renderer renderer = surface.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;
        return surface;
    }

    Transform FindOrCreateAnchor(string name, Vector3 position, Quaternion rotation)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null)
            return existing.transform;

        GameObject anchor = new GameObject(name);
        anchor.transform.position = position;
        anchor.transform.rotation = rotation;
        return anchor.transform;
    }

    Material NewMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material mat = new Material(shader) { name = name };
        mat.color = color;
        return mat;
    }

    TextMesh CreateText(string name, string anchorName, Vector3 defaultPosition, float size, TextAnchor anchor)
    {
        Transform anchorTransform = FindOrCreateAnchor(anchorName, defaultPosition, Quaternion.identity);
        GameObject go = new GameObject(name);
        go.transform.SetParent(anchorTransform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        TextMesh text = go.AddComponent<TextMesh>();
        text.text = "";
        text.fontSize = 42;
        text.characterSize = size;
        text.anchor = anchor;
        text.alignment = anchor == TextAnchor.UpperLeft ? TextAlignment.Left : TextAlignment.Center;
        return text;
    }

    void EnsureCamera()
    {
        _mainCamera = Camera.main;
        if (_mainCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            _mainCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        _mainCamera.transform.position = new Vector3(0f, 1.55f, -0.15f);
        _mainCamera.transform.rotation = Quaternion.identity;
        _mainCamera.nearClipPlane = 0.03f;
        _mainCamera.farClipPlane = 100f;
        _mainCamera.clearFlags = CameraClearFlags.SolidColor;
        _mainCamera.backgroundColor = new Color(0.035f, 0.04f, 0.05f);
    }

    void AddTrackedPoseDriverIfAvailable(GameObject cameraObject)
    {
        TryAddComponentByName(cameraObject, "UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem");
        TryAddComponentByName(cameraObject, "UnityEngine.SpatialTracking.TrackedPoseDriver, UnityEngine.SpatialTracking");
    }

    void TryAddComponentByName(GameObject target, string assemblyQualifiedName)
    {
        Type type = Type.GetType(assemblyQualifiedName);
        if (type == null || target.GetComponent(type) != null)
            return;

        try
        {
            target.AddComponent(type);
        }
        catch
        {
            // Some XR tracking components require package-specific setup. The scene still supports controller ray input and desktop fallback.
        }
    }

    void ShowIntro()
    {
        _titleText.text = "XR Workload Probe";
        _cueText.text = "Select the target shown in each cue.\nUse right trigger, Space, or mouse click.";
        _statusText.text = "Press N to skip waits.\nRecord NASA-TLX ratings after each block.";
        if (_timerText != null)
            _timerText.text = "";
        _feedbackText.text = "";
    }

    void ShowBlockInstruction(ProbeBlockProfile profile)
    {
        ClearTargets();
        _titleText.text = profile.displayName;
        _cueText.text = $"Target dimension: {profile.targetTlxDimension}\n{WrapForWall(profile.rationale, 54)}";
        _statusText.text = $"Task type: {profile.blockId}\nGet ready. Select targets according to each cue.";
        if (_timerText != null)
            _timerText.text = "";
        _feedbackText.text = "Starting block...";
    }

    void ShowBlockComplete(ProbeBlockProfile profile)
    {
        ClearTargets();
        _titleText.text = $"{profile.displayName} complete";
        _cueText.text = "Now collect NASA-TLX dimension ratings and confidence outside this scene.";
        _statusText.text = "Recommended item order: Mental, Physical, Temporal, Performance, Effort, Frustration.\nPress N or wait to continue.";
        if (_timerText != null)
            _timerText.text = "";
        _feedbackText.text = "";
    }

    string WrapForWall(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text) || maxChars <= 0)
            return "";

        string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        int lineLength = 0;
        foreach (string word in words)
        {
            if (lineLength > 0 && lineLength + word.Length + 1 > maxChars)
            {
                sb.AppendLine();
                lineLength = 0;
            }

            if (lineLength > 0)
            {
                sb.Append(' ');
                lineLength++;
            }

            sb.Append(word);
            lineLength += word.Length;
        }
        return sb.ToString();
    }

    void OnApplicationQuit()
    {
        if (writeCsvOnQuit && _trialRecords.Count > 0)
            WriteCsvFiles("quit");
    }

    void OnDisable()
    {
        SetQuestionnaireJumpSuppressed(false);
    }

    void WriteCsvFiles(string reason)
    {
        string folder = GetOutputFolder();
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string trialPath = Path.Combine(folder, $"WorkloadProbe_Trials_{participantId}_{stamp}_{reason}.csv");
        string blockPath = Path.Combine(folder, $"WorkloadProbe_Blocks_{participantId}_{stamp}_{reason}.csv");
        string questionnairePath = Path.Combine(folder, $"CAREXR_Questionnaire_{participantId}_{stamp}_{reason}.csv");

        File.WriteAllText(trialPath, BuildTrialCsv(), Encoding.UTF8);
        File.WriteAllText(blockPath, BuildBlockCsv(), Encoding.UTF8);
        File.WriteAllText(questionnairePath, BuildQuestionnaireCsv(), Encoding.UTF8);

        if (_mainSceneMergedExporter != null &&
            _mainSceneAnswerExportMirror != null &&
            _mainSceneAnswerExportMirror.summaries.Count > 0)
        {
            _mainSceneMergedExporter.participantNumber = ParseParticipantNumber(participantId);
            _mainSceneMergedExporter.outputSubfolder = outputFolderName;
            _mainSceneMergedExporter.ExportNow(reason);
        }

        Debug.Log($"[XRWorkloadProbe] Saved logs:\n{trialPath}\n{blockPath}\n{questionnairePath}", this);
    }

    string GetOutputFolder()
    {
        return Path.Combine(Application.persistentDataPath, outputFolderName);
    }

    string BuildTrialCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,taskType,blockId,targetDimension,presentationOrder,trialIndex,scheduleId,cue,rule,targetLayout,ruleComplexity,targetCount,distractorCount,targetDistance,targetSize,timeLimit,successThresholdStrictness,effectiveSelectionCone,effectiveSelectionAssistRadius,gazeFallbackAllowed,feedbackDelay,controlNoise,decisionRt,timeout,isCorrect,correctHapticPlayed,correctHapticSuppressed,correctIndex,selectedIndex,pointerPath,pointerPeakSpeed,pauseCount,hoverChangeCount");
        foreach (TrialRecord r in _trialRecords)
        {
            sb.AppendLine(string.Join(",",
                Csv(participantId), Csv(r.blockId), Csv(r.blockId), Csv(r.targetDimension), r.presentationOrder, r.trialIndex,
                Csv(r.scheduleId), Csv(r.cue), Csv(r.rule), Csv(r.targetLayout), r.ruleComplexity,
                r.targetCount, r.distractorCount, F(r.targetDistance), F(r.targetSize), F(r.timeLimit),
                F(r.successThresholdStrictness), F(r.effectiveSelectionCone), F(r.effectiveSelectionAssistRadius),
                r.gazeFallbackAllowed ? "1" : "0", F(r.feedbackDelay), F(r.controlNoise),
                F(r.decisionRt), r.timeout ? "1" : "0", r.isCorrect ? "1" : "0",
                r.correctHapticPlayed ? "1" : "0", r.correctHapticSuppressed ? "1" : "0",
                r.correctIndex, r.selectedIndex, F(r.pointerPath), F(r.pointerPeakSpeed),
                r.pauseCount, r.hoverChangeCount));
        }
        return sb.ToString();
    }

    string BuildBlockCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,taskType,blockId,targetDimension,ruleComplexity,targetCount,distractorCount,targetDistance,targetSize,timeLimit,successThresholdStrictness,effectiveSelectionCone,effectiveSelectionAssistRadius,gazeFallbackAllowed,feedbackDelay,controlNoise,trials,accuracy,meanDecisionRt,timeoutCount,meanPointerPath,meanPeakSpeed,totalPauseCount,totalHoverChangeCount");
        foreach (ProbeBlockProfile profile in blockProfiles)
        {
            int n = 0;
            int correct = 0;
            int timeout = 0;
            float rt = 0f;
            float path = 0f;
            float peak = 0f;
            int pauses = 0;
            int hovers = 0;
            foreach (TrialRecord r in _trialRecords)
            {
                if (r.blockId != profile.blockId) continue;
                n++;
                if (r.isCorrect) correct++;
                if (r.timeout) timeout++;
                rt += r.decisionRt;
                path += r.pointerPath;
                peak += r.pointerPeakSpeed;
                pauses += r.pauseCount;
                hovers += r.hoverChangeCount;
            }
            if (n == 0) continue;
            sb.AppendLine(string.Join(",",
                Csv(participantId), Csv(profile.blockId), Csv(profile.blockId), Csv(profile.targetTlxDimension),
                Mathf.Clamp(profile.ruleComplexity, 1, 3), EffectiveTargetCount(profile),
                Mathf.Max(1, EffectiveTargetCount(profile) - 1), F(profile.targetDistance), F(profile.targetSize),
                F(profile.timeLimitSeconds), F(Mathf.Clamp01(profile.successThresholdStrictness)),
                F(EffectiveSelectionCone(profile)), F(EffectiveSelectionAssistRadius(profile)),
                IsGazeFallbackAllowed(profile) ? "1" : "0", F(profile.feedbackDelaySeconds), F(profile.controlNoiseDegrees), n,
                F(correct / (float)n), F(rt / n), timeout,
                F(path / n), F(peak / n), pauses, hovers));
        }
        return sb.ToString();
    }

    string BuildQuestionnaireCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,taskType,blockId,targetDimension,presentationOrder,itemIndex,itemId,itemDimension,prompt,leftAnchor,rightAnchor,scale,responseMode,selectedScore,confidence,answerRt,answerDecisionRt,answerConfirmHoldRt,confidenceRt,confidenceDecisionRt,confidenceConfirmHoldRt,answerPointerPath,answerPeakSpeed,answerPauseCount,answerHoverChangeCount,confidencePointerPath,confidencePeakSpeed,confidencePauseCount,confidenceHoverChangeCount,answerTotalAbsAngle,answerMaxAbsVel,answerSlotChangeCount,answerReverseCount,answerMicroAdjustCount,answerFastFlickCount,confidenceTotalAbsAngle,confidenceMaxAbsVel,confidenceSlotChangeCount,confidenceReverseCount,confidenceMicroAdjustCount,confidenceFastFlickCount");
        foreach (QuestionnaireRecord r in _questionnaireRecords)
        {
            sb.AppendLine(string.Join(",",
                Csv(participantId), Csv(r.blockId), Csv(r.blockId), Csv(r.targetDimension), r.presentationOrder,
                r.itemIndex, Csv(r.itemId), Csv(r.itemDimension), Csv(r.prompt), Csv(r.leftAnchor), Csv(r.rightAnchor), r.scale,
                Csv(r.responseMode), r.selectedScore, r.confidence,
                F(r.answerRt), F(r.answerDecisionRt), F(r.answerConfirmHoldRt),
                F(r.confidenceRt), F(r.confidenceDecisionRt), F(r.confidenceConfirmHoldRt),
                F(r.answerPointerPath), F(r.answerPeakSpeed), r.answerPauseCount, r.answerHoverChangeCount,
                F(r.confidencePointerPath), F(r.confidencePeakSpeed), r.confidencePauseCount, r.confidenceHoverChangeCount,
                F(r.answerTotalAbsAngle), F(r.answerMaxAbsVel), r.answerSlotChangeCount, r.answerReverseCount,
                r.answerMicroAdjustCount, r.answerFastFlickCount,
                F(r.confidenceTotalAbsAngle), F(r.confidenceMaxAbsVel), r.confidenceSlotChangeCount,
                r.confidenceReverseCount, r.confidenceMicroAdjustCount, r.confidenceFastFlickCount));
        }
        return sb.ToString();
    }

    string Csv(string value)
    {
        if (value == null) return "";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    string F(float value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}

public class XRNodePoseFollower : MonoBehaviour
{
    public XRNode node = XRNode.RightHand;

    readonly List<InputDevice> _devices = new List<InputDevice>();

    void Update()
    {
        InputDevices.GetDevicesAtXRNode(node, _devices);
        for (int i = 0; i < _devices.Count; i++)
        {
            InputDevice device = _devices[i];
            if (!device.isValid)
                continue;

            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position))
                transform.position = position;
            if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
                transform.rotation = rotation;
            return;
        }
    }
}
