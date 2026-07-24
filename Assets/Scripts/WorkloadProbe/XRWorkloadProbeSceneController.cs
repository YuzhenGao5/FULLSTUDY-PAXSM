using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using InputSystemKeyboard = UnityEngine.InputSystem.Keyboard;
using InputSystemMouse = UnityEngine.InputSystem.Mouse;
#endif

public partial class XRWorkloadProbeSceneController : MonoBehaviour
{
    const float RuntimeQuestionnaireKnobDiameterMeters = 0.10f;
    const string QuestionnaireSchemaVersion = "CAREXR_Questionnaire_v2.0";
    const string QuestionnaireSpeedSchemaVersion = "CAREXR_QuestionnaireSpeed_v1.0";

    [Serializable]
    public class ProbeBlockProfile
    {
        public string blockId = "baseline";
        [Tooltip("Stable task definition shared by repeated block instances. Leave empty to use Block Id.")]
        public string taskProfileId = "";
        public string displayName = "Baseline";
        public string targetTlxDimension = "baseline";
        [Range(1, 3)] public int ruleComplexity = 1;
        [Tooltip("Maximum number of selectable objects shown in a trial.")]
        [Range(2, 10)] public int targetCount = 4;
        [Tooltip("Number of incorrect selectable objects. The effective choice count is one correct target plus these distractors, capped by Target Count.")]
        [Range(1, 7)] public int distractorCount = 3;
        [Range(0.8f, 4.0f)] public float targetDistance = 1.6f;
        [Range(0.08f, 0.35f)] public float targetSize = 0.22f;
        [Tooltip("Optional horizontal span for a wide movement layout. Zero uses the standard distance-derived layout.")]
        [Range(0f, 5.2f)] public float targetHorizontalSpan = 0f;
        [Tooltip("Optional vertical span for a staggered movement layout. Zero keeps targets on the table.")]
        [Range(0f, 1.2f)] public float targetVerticalSpan = 0f;
        [Tooltip("Require controller-ray selection for this block instead of allowing head-gaze fallback.")]
        public bool disableGazeFallback = false;
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
        public string taskProfileId = "";
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
        public float targetHorizontalSpan;
        public float targetVerticalSpan;
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

    class InterBlockConfirmationRecord
    {
        public string fromBlockId = "";
        public string toBlockId = "";
        public int fromPresentationOrder;
        public int toPresentationOrder;
        public float shownRealtime;
        public float confirmedRealtime;
        public float waitSeconds;
        public string inputSource = "";
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

    class ResponseCalibrationCondition
    {
        public string id = "";
        public string displayName = "";
        public string instruction = "";
        public int[] answerTargets = Array.Empty<int>();
        public int[] confidenceTargets = Array.Empty<int>();
    }

    class QuestionnaireMotionStats
    {
        public float stageStartTime;
        public float stageEndTime;
        public float duration;
        public float firstInteractionRt = -1f;
        public float confirmHoldDuration;
        public int confirmAttemptCount;
        public int confirmCancelCount;
        public int confirmCount;
        public int grabCount;
        public bool wasGrabbing;
        public float nextTraceSampleTime;
        public readonly List<QuestionnaireTraceSample> traceSamples = new List<QuestionnaireTraceSample>();
        public QuestionnaireStageMetrics derived = new QuestionnaireStageMetrics();
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
        public int activeGrabIndex;
        public bool hasSlotSpeedBaseline;
        public int slotSpeedBaselineSlot = -1;
        public float slotSpeedBaselineTime;
        public bool hasPhysicalSpeedBaseline;
        public float lastPhysicalSpeedSampleTime;
        public float lastPhysicalTwistDegrees;
        public float nextPhysicalSpeedSampleTime;
        public float latestPhysicalAngularSpeedDps;
        public int physicalSpeedSampleIndex;
        public int slotSpeedEventIndex;
    }

    class QuestionnaireRecord
    {
        public string blockId = "";
        public string targetDimension = "";
        public string calibrationCondition = "";
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
        public int expectedAnswerTarget = -1;
        public int expectedConfidenceTarget = -1;
        public int answerInitialSlot = -1;
        public int confidenceInitialSlot = -1;
        public int answerTargetDistanceSlots = -1;
        public int confidenceTargetDistanceSlots = -1;
        public string answerTargetDistanceBin = "";
        public string confidenceTargetDistanceBin = "";
        public float answerShortestRequiredAngle = -1f;
        public float confidenceShortestRequiredAngle = -1f;
        public int answerTargetError = -1;
        public int confidenceTargetError = -1;
        public float answerPathRatio = -1f;
        public float confidencePathRatio = -1f;
        public float readRt = -1f;
        public float readEnterRealtime = -1f;
        public float readExitRealtime = -1f;
        public string readExitEvent = "not_collected";
        public float answerRt;
        public float answerDecisionRt;
        public float answerFirstInteractionRt = -1f;
        public float answerConfirmHoldRt;
        public float confidenceRt;
        public float confidenceDecisionRt;
        public float confidenceFirstInteractionRt = -1f;
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
        public float answerEnterRealtime = -1f;
        public float answerExitRealtime = -1f;
        public float confidenceEnterRealtime = -1f;
        public float confidenceExitRealtime = -1f;
        public int answerConfirmAttemptCount;
        public int answerConfirmCancelCount;
        public int answerGrabCount;
        public int confidenceConfirmAttemptCount;
        public int confidenceConfirmCancelCount;
        public int confidenceGrabCount;
        public QuestionnaireStageMetrics answerMetrics = new QuestionnaireStageMetrics();
        public QuestionnaireStageMetrics confidenceMetrics = new QuestionnaireStageMetrics();
    }

    class QuestionnaireStageEvent
    {
        public string blockId = "";
        public int presentationOrder;
        public int itemIndex;
        public string itemId = "";
        public string stage = "";
        public string eventType = "";
        public float realtime;
        public string utc = "";
    }

    class QuestionnaireRawTraceRecord
    {
        public string blockId = "";
        public int presentationOrder;
        public int itemIndex;
        public string itemId = "";
        public string stage = "";
        public float stageStartRealtime;
        public int eventIndex;
        public QuestionnaireTraceSample sample;
    }

    class QuestionnaireInteractionEvent
    {
        public string blockId = "";
        public int presentationOrder;
        public int itemIndex;
        public string itemId = "";
        public string stage = "";
        public string tag = "";
        public string data = "";
        public float realtime;
        public string utc = "";
    }

    class QuestionnairePhysicalSpeedSample
    {
        public string blockId = "";
        public int presentationOrder;
        public int itemIndex;
        public string itemId = "";
        public string stage = "";
        public float stageStartRealtime;
        public int grabIndex;
        public int sampleIndex;
        public float realtime;
        public int slot;
        public float wristTwistDegrees;
        public float deltaDegrees;
        public float deltaTime;
        public float physicalAngularSpeedDps;
        public bool validForCalibration;
        public string exclusionReason = "";
    }

    class QuestionnaireSlotSpeedEvent
    {
        public string blockId = "";
        public int presentationOrder;
        public int itemIndex;
        public string itemId = "";
        public string stage = "";
        public float stageStartRealtime;
        public int grabIndex;
        public int eventIndex;
        public float realtime;
        public int fromSlot;
        public int toSlot;
        public int deltaSlots;
        public float deltaTime;
        public float slotsPerSecond;
        public float slotAngleDegrees;
        public float detentAngularSpeedDps;
        public float wristTwistDegrees;
        public float latestPhysicalAngularSpeedDps;
        public bool validForCalibration;
        public string exclusionReason = "";
    }

    [Header("Run Mode")]
    public bool questionnaireOnlyMode = false;
    public bool requireReadAcknowledgement = false;
    public string questionnaireOnlySessionId = "standalone_questionnaire";

    [Header("Personal Knob Reference")]
    [Tooltip("Runs one distance-balanced target-entry reference block instead of a substantive questionnaire. The exported profile describes this participant's normal knob-motion range for researcher-facing review cues; it never labels a response as careless.")]
    [FormerlySerializedAs("responseProcessCalibrationMode")]
    public bool personalKnobReferenceMode = false;
    [FormerlySerializedAs("responseCalibrationTrialsPerCondition")]
    [Range(8, 24)] public int personalReferenceTrialCount = 12;
    [FormerlySerializedAs("responseCalibrationMinimumReferenceTrials")]
    [Range(4, 20)] public int personalReferenceMinimumValidTrials = 8;
    [FormerlySerializedAs("responseCalibrationMinimumTargetAccuracy")]
    [Range(0.5f, 1f)] public float personalReferenceMinimumTargetAccuracy = 0.8f;
    [FormerlySerializedAs("responseCalibrationInstructionSeconds")]
    [Range(2f, 12f)] public float personalReferenceInstructionSeconds = 6f;
    [FormerlySerializedAs("responseCalibrationCarefulLabel")]
    public string personalReferenceLabel = "Personal knob reference";
    [FormerlySerializedAs("responseCalibrationDirectPathRatioMax")]
    [Range(1.05f, 1.5f)] public float personalReferenceDirectPathRatioMax = 1.2f;
    [FormerlySerializedAs("responseCalibrationLowCorrectionCountMax")]
    [Range(0, 4)] public int personalReferenceLowCorrectionCountMax = 1;

    public string participantId = "P001";
    [Range(1, 99)] public int sessionNumber = 2;
    public string conditionLabel = "WorkloadProbe";
    [Tooltip("Keep baseline and combined_high fixed; randomize only cognitive_heavy, physical_heavy, and temporal_heavy.")]
    public bool randomizeWorkloadBlocks = false;
    public bool startAutomatically = true;
    public bool writeCsvOnQuit = true;
    public string outputFolderName = "XRWorkloadProbe_Data";

    [Header("Inter-block Confirmation")]
    public bool requireParticipantConfirmationBetweenBlocks = false;
    [TextArea(2, 4)]
    public string interBlockConfirmationPrompt =
        "Part 1 is complete. When you are ready, press A on the right controller to begin Part 2.";

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

    [Header("Questionnaire Trace Recording")]
    public string featureAlgorithmVersion = QuestionnaireTraceAnalyzer.AlgorithmVersion;
    [Range(0.05f, 1f)] public float questionnairePauseThresholdSec = 0.2f;
    [Range(0.05f, 1f)] public float questionnaireStillThresholdSec = 0.25f;
    [Range(1f, 100f)] public float questionnaireFastFlickThresholdSps = 15f;
    [Range(0.01f, 0.25f)] public float questionnaireTraceSampleIntervalSec = 0.05f;
    [Range(0f, 20f)] public float questionnaireSpeedDeltaMin = 1f;
    [Range(0f, 5f)] public float questionnaireSpeedDeltaK = 1.5f;
    [Range(2, 12)] public int questionnaireSpeedBandMinimumEpisodes = 3;
    [Range(2, 12)] public int questionnaireMicroMinimumTransitions = 4;
    [Range(1, 5)] public int questionnaireMicroMaximumSlotSpan = 2;

    [Header("Questionnaire Personal Speed Calibration")]
    public bool recordQuestionnairePersonalSpeed = true;
    [Range(0.01f, 0.1f)] public float questionnairePhysicalSpeedSampleIntervalSec = 0.02f;
    [Range(0f, 2f)] public float questionnairePhysicalSpeedMinimumDeltaDegrees = 0.1f;
    [Range(0f, 50f)] public float questionnairePhysicalSpeedMinimumDps = 5f;
    [Range(0.03f, 0.5f)] public float questionnairePhysicalSpeedMaximumSampleGapSec = 0.12f;
    [Range(0.05f, 2f)] public float questionnaireSlotSpeedMaximumTransitionGapSec = 0.5f;
    [Range(1, 100)] public int questionnaireCalibrationMinimumSlotEvents = 20;
    [Range(5, 500)] public int questionnaireCalibrationMinimumPhysicalSamples = 30;

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
    readonly List<ProbeBlockProfile> _runOrderUsed = new List<ProbeBlockProfile>();
    readonly List<TrialRecord> _trialRecords = new List<TrialRecord>();
    readonly List<InterBlockConfirmationRecord> _interBlockConfirmationRecords =
        new List<InterBlockConfirmationRecord>();
    readonly List<QuestionnaireRecord> _questionnaireRecords = new List<QuestionnaireRecord>();
    readonly List<QuestionnaireStageEvent> _questionnaireStageEvents = new List<QuestionnaireStageEvent>();
    readonly List<QuestionnaireRawTraceRecord> _questionnaireRawTraceRecords = new List<QuestionnaireRawTraceRecord>();
    readonly List<QuestionnaireInteractionEvent> _questionnaireInteractionEvents = new List<QuestionnaireInteractionEvent>();
    readonly List<QuestionnairePhysicalSpeedSample> _questionnairePhysicalSpeedSamples = new List<QuestionnairePhysicalSpeedSample>();
    readonly List<QuestionnaireSlotSpeedEvent> _questionnaireSlotSpeedEvents = new List<QuestionnaireSlotSpeedEvent>();
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
    QuestionnaireRecord _activeQuestionnaireRecord;
    string _activeQuestionnaireStageName = "";
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
    Transform _questionnaireRuntimeRoot;
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
    XRWorkloadProbeBehaviorCollector _probeBehaviorCollector;
    bool _questionnaireActive;
    bool _questionnaireStageActive;
    bool _completedExportWritten;
    bool _lastDataIntegrityPassed = true;
    string _lastDataIntegrityPath = "";
    int _questionnaireHoverValue = -1;
    int _questionnaireSelectedValue = -1;

    protected virtual void Awake()
    {
#if UNITY_EDITOR
        ApplySyntheticParticipantSettings();
#endif
        if (ExperimentRunContext.IsConfigured)
        {
            participantId = ExperimentRunContext.ParticipantIdOr(participantId);
            if (ExperimentRunContext.SessionNumber > 0)
                sessionNumber = ExperimentRunContext.SessionNumber;
        }

        questionnaireKnobDiameterMeters = RuntimeQuestionnaireKnobDiameterMeters;
        EnsureCamera();
        AddTrackedPoseDriverIfAvailable(_mainCamera.gameObject);
        if (!questionnaireOnlyMode)
            BuildDefaultProfilesIfNeeded();
        EnsureProbeBehaviorCollector();
        BuildSceneObjects();
    }

    void EnsureProbeBehaviorCollector()
    {
        if (questionnaireOnlyMode)
            return;

        _probeBehaviorCollector = GetComponent<XRWorkloadProbeBehaviorCollector>();
        if (_probeBehaviorCollector == null)
            _probeBehaviorCollector = gameObject.AddComponent<XRWorkloadProbeBehaviorCollector>();

        _probeBehaviorCollector.probeController = this;
        _probeBehaviorCollector.writeRawSamples = true;
    }

    protected virtual void Start()
    {
        if (startAutomatically)
            StartCoroutine(RunExperiment());
    }

    protected virtual void Update()
    {
        if (_waitingForContinue && SkipPressedThisFrame())
        {
            _waitingForContinue = false;
            return;
        }

        if (!_trialActive)
            return;

#if UNITY_EDITOR
        if (SyntheticParticipantActive && UpdateSyntheticProbeTrial())
            return;
#endif

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
        if (questionnaireOnlyMode)
        {
            yield return RunQuestionnaireOnlyExperiment();
            yield break;
        }

        _blockIndex = 0;
        List<ProbeBlockProfile> runOrder = BuildRunOrder();
        _runOrderUsed.Clear();
        _runOrderUsed.AddRange(runOrder);
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

            if (requireParticipantConfirmationBetweenBlocks && _blockIndex < runOrder.Count - 1)
                yield return WaitForInterBlockConfirmation(_currentProfile, runOrder[_blockIndex + 1]);
        }

        WriteCsvFiles("completed");
        _titleText.text = "XR Workload Probe Complete";
        _cueText.text = "All blocks are finished. Review the saved workload-probe CSV or stop the session.";
        _statusText.text = _lastDataIntegrityPassed
            ? $"DATA COMPLETE: all required checks passed.\nSaved {_trialRecords.Count} trial records to:\n{GetOutputFolder()}"
            : $"DATA INTEGRITY CHECK FAILED. Do not use this run.\nReview:\n{_lastDataIntegrityPath}";
        if (_timerText != null)
            _timerText.text = "";
        _feedbackText.text = "";
        ClearTargets();
    }

    IEnumerator RunQuestionnaireOnlyExperiment()
    {
        if (questionnaireComparisonMode)
        {
            yield return RunQuestionnaireComparisonExperiment();
            yield break;
        }

        if (personalKnobReferenceMode)
        {
            yield return RunPersonalKnobReferenceExperiment();
            yield break;
        }

        _blockIndex = 0;
        _runBlockCount = 1;
        var questionnaireSession = new ProbeBlockProfile
        {
            blockId = string.IsNullOrWhiteSpace(questionnaireOnlySessionId)
                ? "standalone_questionnaire"
                : questionnaireOnlySessionId.Trim(),
            displayName = "Questionnaire",
            targetTlxDimension = "questionnaire_only"
        };

        yield return RunBlockQuestionnaire(questionnaireSession);
        WriteCsvFiles("completed");

        _titleText.text = "Questionnaire Complete";
        _cueText.text = "All responses and stage events have been saved.";
        _statusText.text = $"Saved {_questionnaireRecords.Count} questionnaire records to:\n{GetOutputFolder()}";
        if (_timerText != null)
            _timerText.text = "";
        _feedbackText.text = "";
        ClearTargets();
    }

    IEnumerator RunPersonalKnobReferenceExperiment()
    {
        _blockIndex = 0;
        _runBlockCount = 1;
        ResponseCalibrationCondition reference = BuildPersonalKnobReferenceSchedule();

        _titleText.text = "Personal Knob Reference";
        _cueText.text =
            "You will complete short target-entry trials using the same Answer and Confidence knobs used in the study.";
        _statusText.text =
            "This records your own normal knob-motion range. It does not label your responses or judge your effort.";
        _timerText.text = "";
        _feedbackText.text = "";
        ClearTargets();
        yield return WaitForSecondsOrN(3f);

        yield return RunPersonalKnobReferencePractice(reference);
        yield return ShowPersonalKnobReferenceInstruction(reference);
        yield return RunPersonalKnobReferenceBlock(reference);

        WriteCsvFiles("completed");
        _titleText.text = "Personal Reference Complete";
        _cueText.text = _lastDataIntegrityPassed
            ? "Your personal Answer and Confidence knob-reference profile has been saved."
            : "The reference data were saved, but the profile is not ready. Please ask the researcher to review the integrity report.";
        _statusText.text = _lastDataIntegrityPassed
            ? $"DATA COMPLETE: saved {_questionnaireRecords.Count} formal reference trials and a profile to:\n{GetOutputFolder()}"
            : $"PROFILE NEEDS REVIEW. See:\n{_lastDataIntegrityPath}";
        _timerText.text = "";
        _feedbackText.text = "";
        ClearTargets();
    }

    IEnumerator ShowPersonalKnobReferenceInstruction(ResponseCalibrationCondition condition)
    {
        if (_questionnaireRuntimeRoot != null)
            _questionnaireRuntimeRoot.gameObject.SetActive(false);

        _titleText.text = condition.displayName;
        _cueText.text = condition.instruction;
        _statusText.text =
            $"{personalReferenceTrialCount} formal target-entry trials follow two excluded practice trials. Press A when ready, or wait to continue.";
        _timerText.text = "";
        _feedbackText.text = "";
        ClearTargets();
        yield return WaitForSecondsOrN(personalReferenceInstructionSeconds);
    }

    IEnumerator RunPersonalKnobReferencePractice(ResponseCalibrationCondition condition)
    {
        ClearTargets();
        CalibrateQuestionnairePlacementFromHead();
        SetQuestionnaireJumpSuppressed(true);
        _questionnaireActive = true;
        _questionnaireRuntimeRoot.gameObject.SetActive(true);
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;

        var profile = new ProbeBlockProfile
        {
            blockId = "personal_reference_practice",
            displayName = "Practice",
            targetTlxDimension = "personal_knob_reference"
        };

        int[] practiceIndices = { 0, Mathf.Min(7, condition.answerTargets.Length - 1) };
        for (int i = 0; i < practiceIndices.Length; i++)
        {
            int source = practiceIndices[i];
            int answerTarget = condition.answerTargets[source];
            int confidenceTarget = condition.confidenceTargets[source];

            yield return RunQuestionnaireSelectionStage(
                profile,
                $"Practice Answer  {i + 1}/{practiceIndices.Length}",
                $"Set the Answer dial to {answerTarget}.",
                "1",
                questionnaireScale.ToString(CultureInfo.InvariantCulture),
                questionnaireScale,
                "PRACTICE - NOT SAVED",
                false,
                new QuestionnaireMotionStats(),
                null,
                "PracticeAnswer");

            yield return RunQuestionnaireSelectionStage(
                profile,
                $"Practice Confidence  {i + 1}/{practiceIndices.Length}",
                $"Set the Confidence dial to {confidenceTarget}.",
                "1",
                questionnaireConfidenceScale.ToString(CultureInfo.InvariantCulture),
                questionnaireConfidenceScale,
                "PRACTICE - NOT SAVED",
                true,
                new QuestionnaireMotionStats(),
                null,
                "PracticeConfidence");
        }

        ClearQuestionnaireTicks();
        _questionnaireRuntimeRoot.gameObject.SetActive(false);
        _questionnaireActive = false;
        _questionnaireStageActive = false;
        SetQuestionnaireJumpSuppressed(false);
    }

    IEnumerator RunPersonalKnobReferenceBlock(ResponseCalibrationCondition condition)
    {
        ClearTargets();
        CalibrateQuestionnairePlacementFromHead();
        SetQuestionnaireJumpSuppressed(true);
        _questionnaireActive = true;
        _questionnaireRuntimeRoot.gameObject.SetActive(true);
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;

        _titleText.text = "";
        _cueText.text = "";
        _statusText.text = "";
        _timerText.text = "";
        _feedbackText.text = "";

        int trialCount = Mathf.Min(condition.answerTargets.Length, condition.confidenceTargets.Length);
        var profile = new ProbeBlockProfile
        {
            blockId = condition.id,
            displayName = condition.displayName,
            targetTlxDimension = "personal_knob_reference"
        };

        for (int trialIndex = 0; trialIndex < trialCount; trialIndex++)
        {
            int answerTarget = condition.answerTargets[trialIndex];
            int confidenceTarget = condition.confidenceTargets[trialIndex];
            var record = new QuestionnaireRecord
            {
                blockId = condition.id,
                targetDimension = "personal_knob_reference",
                calibrationCondition = condition.id,
                presentationOrder = 1,
                itemIndex = trialIndex + 1,
                itemId = $"{condition.id}_{trialIndex + 1:00}",
                itemDimension = "Personal target-entry reference",
                prompt = $"Set the Answer dial to {answerTarget}.",
                leftAnchor = "1",
                rightAnchor = questionnaireScale.ToString(CultureInfo.InvariantCulture),
                responseMode = CurrentQuestionnaireResponseMode(),
                scale = questionnaireScale,
                expectedAnswerTarget = answerTarget,
                expectedConfidenceTarget = confidenceTarget
            };

            if (requireReadAcknowledgement)
            {
                string readPrompt =
                    $"For the next target-entry trial, set the Answer dial to {answerTarget}. " +
                    "Then set the Confidence dial to the displayed target.";
                yield return RunQuestionnaireReadStage(
                    $"Read  {trialIndex + 1}/{trialCount}",
                    readPrompt,
                    "1",
                    questionnaireScale.ToString(CultureInfo.InvariantCulture),
                    questionnaireScale,
                    record);
            }

            var answerStats = new QuestionnaireMotionStats();
            yield return RunQuestionnaireSelectionStage(
                profile,
                $"Answer target  {trialIndex + 1}/{trialCount}",
                $"Set the Answer dial to {answerTarget}.",
                "1",
                questionnaireScale.ToString(CultureInfo.InvariantCulture),
                questionnaireScale,
                condition.displayName,
                false,
                answerStats,
                record,
                "Answer");

            record.selectedScore = _questionnaireSelectedValue;
            record.answerRt = answerStats.duration;
            record.answerConfirmHoldRt = answerStats.confirmHoldDuration;
            record.answerDecisionRt = Mathf.Max(0f, answerStats.duration - answerStats.confirmHoldDuration);
            record.answerFirstInteractionRt = answerStats.firstInteractionRt;
            record.answerEnterRealtime = answerStats.stageStartTime;
            record.answerExitRealtime = answerStats.stageEndTime;
            record.answerConfirmAttemptCount = answerStats.confirmAttemptCount;
            record.answerConfirmCancelCount = answerStats.confirmCancelCount;
            record.answerGrabCount = answerStats.grabCount;
            record.answerTargetError = Mathf.Abs(record.selectedScore - answerTarget);
            CopyQuestionnaireStats(answerStats, record, answerStage: true);
            record.answerPathRatio = CalculateCalibrationPathRatio(record, answerStage: true);
            PopulatePersonalReferenceDistanceFields(record, answerStage: true);

            var confidenceStats = new QuestionnaireMotionStats();
            yield return RunQuestionnaireSelectionStage(
                profile,
                $"Confidence target  {trialIndex + 1}/{trialCount}",
                $"Set the Confidence dial to {confidenceTarget}. This is a target-entry movement, not a confidence judgment.",
                "1",
                questionnaireConfidenceScale.ToString(CultureInfo.InvariantCulture),
                questionnaireConfidenceScale,
                condition.displayName,
                true,
                confidenceStats,
                record,
                "Confidence");

            record.confidence = _questionnaireSelectedValue;
            record.confidenceRt = confidenceStats.duration;
            record.confidenceConfirmHoldRt = confidenceStats.confirmHoldDuration;
            record.confidenceDecisionRt = Mathf.Max(0f, confidenceStats.duration - confidenceStats.confirmHoldDuration);
            record.confidenceFirstInteractionRt = confidenceStats.firstInteractionRt;
            record.confidenceEnterRealtime = confidenceStats.stageStartTime;
            record.confidenceExitRealtime = confidenceStats.stageEndTime;
            record.confidenceConfirmAttemptCount = confidenceStats.confirmAttemptCount;
            record.confidenceConfirmCancelCount = confidenceStats.confirmCancelCount;
            record.confidenceGrabCount = confidenceStats.grabCount;
            record.confidenceTargetError = Mathf.Abs(record.confidence - confidenceTarget);
            CopyQuestionnaireStats(confidenceStats, record, answerStage: false);
            record.confidencePathRatio = CalculateCalibrationPathRatio(record, answerStage: false);
            PopulatePersonalReferenceDistanceFields(record, answerStage: false);

            _questionnaireRecords.Add(record);
            if (IsQuestionnaireKnobInputActive)
                AppendMainSceneCompatibleSummaries(record);
            WriteQuestionnaireLiveCheckpoint();
            yield return new WaitForSeconds(0.2f);
        }

        ClearQuestionnaireTicks();
        _questionnaireTitleText.text = "Personal reference complete";
        _questionnairePromptText.text = "Your formal Answer and Confidence movements have been recorded.";
        _questionnaireScaleText.text = "";
        _questionnaireScaleRightText.text = "";
        _questionnaireProgressText.text = "";
        _questionnaireValueText.text = "";
        yield return WaitForSecondsOrN(1.25f);

        _questionnaireRuntimeRoot.gameObject.SetActive(false);
        _questionnaireActive = false;
        _questionnaireStageActive = false;
        SetQuestionnaireJumpSuppressed(false);
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;
    }

    ResponseCalibrationCondition BuildPersonalKnobReferenceSchedule()
    {
        int count = Mathf.Clamp(personalReferenceTrialCount, 8, 24);
        return new ResponseCalibrationCondition
        {
            id = "personal_reference",
            displayName = string.IsNullOrWhiteSpace(personalReferenceLabel)
                ? "Personal knob reference"
                : personalReferenceLabel.Trim(),
            instruction =
                "Use the knob naturally and accurately. Targets vary in direction and travel distance so the system can learn your own normal movement range.",
            answerTargets = BuildPersonalReferenceTargets(questionnaireScale, count),
            confidenceTargets = BuildPersonalReferenceTargets(questionnaireConfidenceScale, count)
        };
    }

    public static int[] BuildPersonalReferenceTargets(int scale, int count)
    {
        int boundedScale = Mathf.Max(2, scale);
        int centeredSlot = Mathf.CeilToInt(boundedScale * 0.5f);
        int maxBalancedDistance = Mathf.Max(1, Mathf.Min(centeredSlot - 1, boundedScale - centeredSlot));
        int[] offsets;
        if (boundedScale == 21)
        {
            // Short, medium, and long moves are balanced across both directions from slot 11.
            offsets = new[] { -1, 1, -2, 2, -4, 4, -5, 5, -8, 8, -10, 10 };
        }
        else if (boundedScale == 5)
        {
            // A five-point Confidence dial has only one short and one long distance from slot 3.
            offsets = new[] { -1, 1, -1, 1, -1, 1, -2, 2, -2, 2, -2, 2 };
        }
        else
        {
            int shortDistance = Mathf.Min(1, maxBalancedDistance);
            int mediumDistance = Mathf.Clamp(Mathf.RoundToInt(maxBalancedDistance * 0.55f), shortDistance, maxBalancedDistance);
            offsets = new[]
            {
                -shortDistance, shortDistance, -shortDistance, shortDistance,
                -mediumDistance, mediumDistance, -mediumDistance, mediumDistance,
                -maxBalancedDistance, maxBalancedDistance, -maxBalancedDistance, maxBalancedDistance
            };
        }

        var targets = new int[Mathf.Max(1, count)];
        for (int i = 0; i < targets.Length; i++)
        {
            int offset = offsets[i % offsets.Length];
            if ((i / offsets.Length) % 2 == 1)
                offset = -offset;
            int target = Mathf.Clamp(centeredSlot + offset, 1, boundedScale);
            if (target == centeredSlot)
                target = Mathf.Clamp(target + (i % 2 == 0 ? -1 : 1), 1, boundedScale);
            targets[i] = target;
        }
        return targets;
    }

    List<ProbeBlockProfile> BuildRunOrder()
    {
        var source = new List<ProbeBlockProfile>(blockProfiles);
        if (!randomizeWorkloadBlocks || source.Count <= 1)
            return source;

        var randomizedProfiles = new List<ProbeBlockProfile>();
        var randomizedSlots = new List<int>();
        for (int i = 0; i < source.Count; i++)
        {
            if (!IsRandomizedMiddleWorkloadBlock(source[i]))
                continue;
            randomizedProfiles.Add(source[i]);
            randomizedSlots.Add(i);
        }

        for (int i = 0; i < randomizedProfiles.Count; i++)
        {
            int j = UnityEngine.Random.Range(i, randomizedProfiles.Count);
            (randomizedProfiles[i], randomizedProfiles[j]) = (randomizedProfiles[j], randomizedProfiles[i]);
        }

        for (int i = 0; i < randomizedSlots.Count; i++)
            source[randomizedSlots[i]] = randomizedProfiles[i];
        return source;
    }

    static bool IsRandomizedMiddleWorkloadBlock(ProbeBlockProfile profile)
    {
        if (profile == null || string.IsNullOrWhiteSpace(profile.blockId))
            return false;
        return string.Equals(profile.blockId, "cognitive_heavy", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(profile.blockId, "physical_heavy", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(profile.blockId, "temporal_heavy", StringComparison.OrdinalIgnoreCase);
    }

    IEnumerator WaitForSecondsOrN(float seconds)
    {
#if UNITY_EDITOR
        if (SyntheticParticipantActive)
        {
            yield return new WaitForSecondsRealtime(Mathf.Min(0.08f, Mathf.Max(0.01f, seconds)));
            yield break;
        }
#endif
        _waitingForContinue = true;
        float end = Time.time + seconds;
        while (_waitingForContinue && Time.time < end)
            yield return null;
        _waitingForContinue = false;
    }

    IEnumerator WaitForInterBlockConfirmation(ProbeBlockProfile current, ProbeBlockProfile next)
    {
        ClearTargets();
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;
        if (_timerText != null)
            _timerText.text = "";
        _feedbackText.text = "Waiting for participant confirmation";
        _titleText.text = $"Part {_blockIndex + 1} complete";
        _cueText.text = interBlockConfirmationPrompt;
        _statusText.text = "Press A on the right controller.\nDesktop test: Enter, Space, or left mouse click.";

        var record = new InterBlockConfirmationRecord
        {
            fromBlockId = current != null ? current.blockId : "",
            toBlockId = next != null ? next.blockId : "",
            fromPresentationOrder = _blockIndex + 1,
            toPresentationOrder = _blockIndex + 2,
            shownRealtime = Time.realtimeSinceStartup
        };

#if UNITY_EDITOR
        if (SyntheticParticipantActive)
        {
            yield return new WaitForSecondsRealtime(0.08f);
            record.confirmedRealtime = Time.realtimeSinceStartup;
            record.waitSeconds = record.confirmedRealtime - record.shownRealtime;
            record.inputSource = "synthetic";
            _interBlockConfirmationRecords.Add(record);
            yield break;
        }
#endif

        while (IsInterBlockContinueHeldNow(out _))
            yield return null;

        string inputSource = "";
        while (!IsInterBlockContinueHeldNow(out inputSource))
            yield return null;

        record.confirmedRealtime = Time.realtimeSinceStartup;
        record.waitSeconds = record.confirmedRealtime - record.shownRealtime;
        record.inputSource = inputSource;
        _interBlockConfirmationRecords.Add(record);

        while (IsInterBlockContinueHeldNow(out _))
            yield return null;
    }

    bool IsInterBlockContinueHeldNow(out string inputSource)
    {
        inputSource = "";
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, _rightHandDevices);
        for (int i = 0; i < _rightHandDevices.Count; i++)
        {
            InputDevice device = _rightHandDevices[i];
            if (!device.isValid)
                continue;
            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primary) && primary)
            {
                inputSource = "right_primary_button";
                return true;
            }
        }

#if ENABLE_INPUT_SYSTEM
        InputSystemKeyboard keyboard = InputSystemKeyboard.current;
        if (keyboard != null)
        {
            if ((keyboard.enterKey != null && keyboard.enterKey.isPressed) ||
                (keyboard.numpadEnterKey != null && keyboard.numpadEnterKey.isPressed))
            {
                inputSource = "keyboard_enter";
                return true;
            }
            if (keyboard.spaceKey != null && keyboard.spaceKey.isPressed)
            {
                inputSource = "keyboard_space";
                return true;
            }
        }

        InputSystemMouse mouse = InputSystemMouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
        {
            inputSource = "mouse_left";
            return true;
        }
#endif
        return false;
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
            taskProfileId = EffectiveTaskProfileId(profile),
            targetDimension = profile.targetTlxDimension,
            presentationOrder = _blockIndex + 1,
            trialIndex = trialIndex + 1,
            scheduleId = $"{EffectiveTaskProfileId(profile)}_T{trialIndex + 1:00}",
            cue = cue,
            rule = rule,
            targetLayout = targetLayout,
            ruleComplexity = Mathf.Clamp(profile.ruleComplexity, 1, 3),
            targetCount = EffectiveTargetCount(profile),
            distractorCount = Mathf.Max(1, EffectiveTargetCount(profile) - 1),
            targetDistance = profile.targetDistance,
            targetSize = profile.targetSize,
            targetHorizontalSpan = EffectiveTargetHorizontalSpan(profile),
            targetVerticalSpan = EffectiveTargetVerticalSpan(profile),
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
        TrialSpec[] schedule = GetPresetSchedule(EffectiveTaskProfileId(profile));
        if (schedule.Length == 0)
            return new TrialSpec(
                RuleTypeForComplexity(profile.ruleComplexity, trialIndex),
                trialIndex % EffectiveTargetCount(profile),
                trialIndex);

        TrialSpec spec = schedule[trialIndex % schedule.Length];
        spec.ruleType = RuleTypeForComplexity(profile.ruleComplexity, trialIndex);
        return spec;
    }

    static string EffectiveTaskProfileId(ProbeBlockProfile profile)
    {
        if (profile == null)
            return "";
        return string.IsNullOrWhiteSpace(profile.taskProfileId)
            ? profile.blockId
            : profile.taskProfileId.Trim();
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
            case "combined_high_v1":
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
            cue = "Rule: 1-BACK MEMORY. Select the color that was correct on the previous trial.";
        }

        _previousCorrectColor = requiredColor;
        targetLayout = BuildTargetLayoutString(colorIndices, count);

        float tableTopY = 0.76f;
        float visibleWidth = EffectiveTargetHorizontalSpan(profile);
        float verticalSpan = EffectiveTargetVerticalSpan(profile);
        bool useWideMovementLayout = verticalSpan > 0.001f;
        float targetDepth = Mathf.Clamp(profile.targetDistance, 1.45f, 3.0f);
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
            float minimumCenterY = tableTopY + GetPrimitiveHalfHeight(primitiveType, profile.targetSize) + 0.015f;
            float centerY = minimumCenterY;
            if (useWideMovementLayout)
            {
                float verticalFactor = i % 3 == 0 ? -0.5f : (i % 3 == 1 ? 0.5f : 0f);
                centerY = Mathf.Max(minimumCenterY, 1.25f + verticalFactor * verticalSpan);
            }
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

    float EffectiveTargetHorizontalSpan(ProbeBlockProfile profile)
    {
        if (profile != null && profile.targetHorizontalSpan > 0.001f)
            return Mathf.Clamp(profile.targetHorizontalSpan, 2.2f, 5.2f);
        float distance = profile == null ? 1.6f : profile.targetDistance;
        return Mathf.Clamp(distance * 1.35f, 2.2f, 4.8f);
    }

    float EffectiveTargetVerticalSpan(ProbeBlockProfile profile)
    {
        return profile == null ? 0f : Mathf.Clamp(profile.targetVerticalSpan, 0f, 1.2f);
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
        float now = Time.realtimeSinceStartup;
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
               (profile == null || !profile.disableGazeFallback) &&
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

        _selectionRayRenderer.enabled = showSelectionRay &&
                                        (_trialActive || _questionnaireStageActive);
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
            targetHorizontalSpan = 4.8f,
            targetVerticalSpan = 0.9f,
            disableGazeFallback = true,
            successThresholdStrictness = 0.65f,
            trialsPerBlock = 10,
            rationale = "Widely separated low/high targets require large controller movements across the visual field."
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
        _mainSceneMergedExporter.sessionNumber = Mathf.Max(1, sessionNumber);
        _mainSceneMergedExporter.conditionLabel = string.IsNullOrWhiteSpace(conditionLabel)
            ? (questionnaireOnlyMode ? "QuestionnaireRead" : "WorkloadProbe")
            : conditionLabel.Trim();
        _mainSceneMergedExporter.outputMode = KnobBehaviorMergedCSVExporter.OutputMode.PersistentDataPath;
        _mainSceneMergedExporter.outputSubfolder = outputFolderName;
        _mainSceneMergedExporter.fileNamePrefix = questionnaireComparisonMode
            ? "PAXSMComparison"
            : (questionnaireOnlyMode ? "XRQuestionnaireRead" : "XRWorkloadProbe");
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

        _questionnaireRuntimeRoot = root.transform;
        _questionnaireRuntimeRoot.position = Vector3.zero;
        _questionnaireRuntimeRoot.rotation = Quaternion.identity;

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

        _questionnaireRuntimeRoot.gameObject.SetActive(false);
    }

    TextMesh CreateQuestionnaireText(string name, Vector3 worldPosition, float size, TextAnchor anchor)
    {
        Transform existing = _questionnaireRuntimeRoot.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        go.transform.SetParent(_questionnaireRuntimeRoot, false);
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
        _questionnaireRuntimeRoot.gameObject.SetActive(true);
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;

        _titleText.text = "";
        _cueText.text = "";
        _statusText.text = "";
        _timerText.text = "";
        _feedbackText.text = "";

        TlxItem[] items = BuildTlxItems();
        if (questionnaireComparisonMode && _comparisonQuestionnaireItemLimit > 0 &&
            items.Length > _comparisonQuestionnaireItemLimit)
        {
            Array.Resize(ref items, _comparisonQuestionnaireItemLimit);
        }
        if (questionnaireComparisonMode &&
            questionnaireInputMethod == QuestionnaireInputMethod.PointAndClick)
        {
            yield return RunClassicPointClickQuestionnaire(profile, items);
        }
        else
        {
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
                    responseMode = CurrentQuestionnaireResponseMode(),
                    scale = questionnaireScale
                };

                if (requireReadAcknowledgement)
                {
                    yield return RunQuestionnaireReadStage(
                        $"Read  {i + 1}/{items.Length}",
                        item.prompt,
                        item.leftAnchor,
                        item.rightAnchor,
                        questionnaireScale,
                        record);
                }

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
                    answerStats,
                    record,
                    "Answer");

                record.selectedScore = _questionnaireSelectedValue;
                record.answerRt = answerStats.duration;
                record.answerConfirmHoldRt = answerStats.confirmHoldDuration;
                record.answerDecisionRt = Mathf.Max(0f, answerStats.duration - answerStats.confirmHoldDuration);
                record.answerFirstInteractionRt = answerStats.firstInteractionRt;
                record.answerEnterRealtime = answerStats.stageStartTime;
                record.answerExitRealtime = answerStats.stageEndTime;
                record.answerConfirmAttemptCount = answerStats.confirmAttemptCount;
                record.answerConfirmCancelCount = answerStats.confirmCancelCount;
                record.answerGrabCount = answerStats.grabCount;
                CopyQuestionnaireStats(answerStats, record, answerStage: true);
                record.answerPathRatio = CalculateObservedPathRatio(
                    record.answerInitialSlot,
                    record.selectedScore,
                    record.answerTotalAbsAngle,
                    questionnaireScale);

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
                        confidenceStats,
                        record,
                        "Confidence");

                    record.confidence = _questionnaireSelectedValue;
                    record.confidenceRt = confidenceStats.duration;
                    record.confidenceConfirmHoldRt = confidenceStats.confirmHoldDuration;
                    record.confidenceDecisionRt = Mathf.Max(0f, confidenceStats.duration - confidenceStats.confirmHoldDuration);
                    record.confidenceFirstInteractionRt = confidenceStats.firstInteractionRt;
                    record.confidenceEnterRealtime = confidenceStats.stageStartTime;
                    record.confidenceExitRealtime = confidenceStats.stageEndTime;
                    record.confidenceConfirmAttemptCount = confidenceStats.confirmAttemptCount;
                    record.confidenceConfirmCancelCount = confidenceStats.confirmCancelCount;
                    record.confidenceGrabCount = confidenceStats.grabCount;
                    CopyQuestionnaireStats(confidenceStats, record, answerStage: false);
                    record.confidencePathRatio = CalculateObservedPathRatio(
                        record.confidenceInitialSlot,
                        record.confidence,
                        record.confidenceTotalAbsAngle,
                        questionnaireConfidenceScale);
                }
                else
                {
                    record.confidence = -1;
                }

                _questionnaireRecords.Add(record);
                if (IsQuestionnaireKnobInputActive)
                    AppendMainSceneCompatibleSummaries(record);
                WriteQuestionnaireLiveCheckpoint();
                yield return new WaitForSeconds(0.2f);
            }
        }

        ClearQuestionnaireTicks();
        _questionnaireTitleText.text = "Questionnaire complete";
        _questionnairePromptText.text = questionnaireOnlyMode
            ? "All responses have been recorded."
            : "The next task block will start shortly.";
        _questionnaireScaleText.text = "";
        _questionnaireScaleRightText.text = "";
        _questionnaireProgressText.text = "";
        _questionnaireValueText.text = "";
        yield return WaitForSecondsOrN(1.25f);

        _questionnaireRuntimeRoot.gameObject.SetActive(false);
        _questionnaireActive = false;
        _questionnaireStageActive = false;
        SetQuestionnaireJumpSuppressed(false);
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;
    }

    IEnumerator RunQuestionnaireReadStage(
        string title,
        string prompt,
        string leftAnchor,
        string rightAnchor,
        int scale,
        QuestionnaireRecord record)
    {
        _questionnaireStageActive = false;
        ClearQuestionnaireTicks();

        _questionnaireTitleText.text = title;
        _questionnairePromptText.text = WrapForWall(prompt, 62);
        _questionnaireScaleText.text = questionnaireOnlyMode ? "" : $"1  {leftAnchor}";
        _questionnaireScaleRightText.text = questionnaireOnlyMode ? "" : $"{rightAnchor}  {scale}";
        _questionnaireProgressText.text = "READ";
        _questionnaireValueText.text = "Press A to start answering";

        record.readEnterRealtime = Time.realtimeSinceStartup;
        record.readExitEvent = "primary_button_a";
        LogQuestionnaireStageEvent(record, "Read", "Enter");
        LogQuestionnaireInteractionEvent(record, "Read", "StageEnter", "prompt_only");

#if UNITY_EDITOR
        BeginSyntheticQuestionnaireRead(record);
#endif

        while (IsQuestionnaireReadContinueHeldNow())
            yield return null;
        while (!IsQuestionnaireReadContinueHeldNow())
            yield return null;

        record.readExitRealtime = Time.realtimeSinceStartup;
        record.readRt = Mathf.Max(0f, record.readExitRealtime - record.readEnterRealtime);
        LogQuestionnaireStageEvent(record, "Read", "Exit");
        LogQuestionnaireInteractionEvent(record, "Read", "ReadAcknowledged", record.readExitEvent);
        LogQuestionnaireInteractionEvent(record, "Read", "StageExit", $"readRt={F(record.readRt)}");
        TryPlayRightHandHaptic(0.18f, 0.045f, force: true);
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
        string mark = $"Q{qIndex1}-{enterCount}";
        QuestionnaireStageMetrics answerMetrics = record.answerMetrics ?? new QuestionnaireStageMetrics();
        QuestionnaireStageMetrics confidenceMetrics = record.confidenceMetrics ?? new QuestionnaireStageMetrics();

        var answer = new KnobCore.KnobMarkSummary
        {
            mark = mark,
            itemId = record.itemId,
            qIndex0 = qIndex0,
            qIndex1 = qIndex1,
            enterCount = enterCount,
            role = "Answer",
            stage = "Answer",
            t_read_in = record.readEnterRealtime,
            t_read_out = record.readExitRealtime,
            t_answer_in = record.answerEnterRealtime,
            t_answer_out = record.answerExitRealtime,
            t_conf_in = record.confidenceEnterRealtime,
            t_conf_out = record.confidenceExitRealtime,
            t_firstMove_answer = record.answerFirstInteractionRt >= 0f
                ? record.answerEnterRealtime + record.answerFirstInteractionRt
                : -1f,
            t_firstMove_conf = record.confidenceFirstInteractionRt >= 0f
                ? record.confidenceEnterRealtime + record.confidenceFirstInteractionRt
                : -1f,
            tickCount = record.scale,
            currentSlot = record.selectedScore,
            currentAngleY = SlotAngle(record.selectedScore, record.scale, QuestionnaireKnobMinAngle(), QuestionnaireKnobMaxAngle())
        };
        ApplyQuestionnaireStageMetrics(answer, answerMetrics);

        var confidence = new KnobCore.KnobMarkSummary
        {
            mark = mark,
            itemId = record.itemId,
            qIndex0 = qIndex0,
            qIndex1 = qIndex1,
            enterCount = enterCount,
            role = "Confidence",
            stage = "Submit",
            t_read_in = record.readEnterRealtime,
            t_read_out = record.readExitRealtime,
            t_answer_in = record.answerEnterRealtime,
            t_answer_out = record.answerExitRealtime,
            t_conf_in = record.confidenceEnterRealtime,
            t_conf_out = record.confidenceExitRealtime,
            t_firstMove_answer = record.answerFirstInteractionRt >= 0f
                ? record.answerEnterRealtime + record.answerFirstInteractionRt
                : -1f,
            t_firstMove_conf = record.confidenceFirstInteractionRt >= 0f
                ? record.confidenceEnterRealtime + record.confidenceFirstInteractionRt
                : -1f,
            tickCount = questionnaireConfidenceScale,
            currentSlot = record.confidence,
            currentAngleY = SlotAngle(record.confidence, questionnaireConfidenceScale, QuestionnaireKnobMinAngle(), QuestionnaireKnobMaxAngle())
        };
        ApplyQuestionnaireStageMetrics(confidence, confidenceMetrics);

        _mainSceneAnswerExportMirror.summaries.Add(answer);
        _mainSceneConfidenceExportMirror.summaries.Add(confidence);
    }

    void ApplyQuestionnaireStageMetrics(
        KnobCore.KnobMarkSummary summary,
        QuestionnaireStageMetrics metrics)
    {
        if (summary == null || metrics == null)
            return;

        summary.tickCount = metrics.tickCount > 0 ? metrics.tickCount : summary.tickCount;
        summary.currentSlot = metrics.currentSlot > 0 ? metrics.currentSlot : summary.currentSlot;
        summary.currentAngleY = metrics.currentSlot > 0 ? metrics.currentAngleY : summary.currentAngleY;
        summary.slotChangeCount = metrics.slotChangeCount;
        summary.reverseCount = metrics.reverseCount;
        summary.pauseCount = metrics.pauseCount;
        summary.confirmCount = metrics.confirmCount;
        summary.minSlot = metrics.minSlot;
        summary.maxSlot = metrics.maxSlot;
        summary.uniqueSlotsVisited = metrics.uniqueSlotsVisited;
        summary.stillEpisodeCount = metrics.stillEpisodeCount;
        summary.stillOverThresholdSum = metrics.stillOverThresholdSum;
        summary.stillTimeSum = metrics.stillTimeSum;
        summary.microAdjustTimeSum = metrics.microAdjustTimeSum;
        summary.microAdjustCount = metrics.microAdjustCount;
        summary.normalAdjustTimeSum = metrics.normalAdjustTimeSum;
        summary.normalAdjustCount = metrics.normalAdjustCount;
        summary.flickTimeSum = metrics.flickTimeSum;
        summary.fastFlickCount = metrics.fastFlickCount;
        summary.maxFlickVel = metrics.maxFlickVel;
        summary.maxAbsVel = metrics.maxAbsVel;
        summary.activeMoveTimeSum = metrics.activeMoveTimeSum;
        summary.activeMoveCount = metrics.activeMoveCount;
        summary.totalAbsAngle = metrics.totalAbsAngle;
        summary.speedBandValid = metrics.speedBandValid;
        summary.speedMedian = metrics.speedMedian;
        summary.speedMAD = metrics.speedMAD;
        summary.speedThLow = metrics.speedThLow;
        summary.speedThHigh = metrics.speedThHigh;
        summary.speedBandNote = metrics.speedBandNote;
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
        QuestionnaireMotionStats stats,
        QuestionnaireRecord record,
        string stageName)
    {
        bool pointAndClick = questionnaireInputMethod == QuestionnaireInputMethod.PointAndClick;
        _questionnaireStageActive = true;
        _questionnaireSelectedValue = -1;
        _questionnaireHoverValue = pointAndClick ? -1 : Mathf.CeilToInt(scale * 0.5f);
        SetupQuestionnaireScale(scale);
        if (!pointAndClick && _paxsmQuestionnaireKnobCore != null)
            _questionnaireHoverValue = _paxsmQuestionnaireKnobCore.CurrentSlot;
        if (record != null)
        {
            if (string.Equals(stageName, "Answer", StringComparison.OrdinalIgnoreCase))
                record.answerInitialSlot = _questionnaireHoverValue;
            else if (string.Equals(stageName, "Confidence", StringComparison.OrdinalIgnoreCase))
                record.confidenceInitialSlot = _questionnaireHoverValue;
        }
        UpdateQuestionnaireTickVisuals(_questionnaireHoverValue, _questionnaireSelectedValue);

        _questionnaireTitleText.text = title;
        _questionnairePromptText.text = WrapForWall(prompt, 62);
        _questionnaireScaleText.text = leftAnchor;
        _questionnaireScaleRightText.text = rightAnchor;
        _questionnaireProgressText.text = pointAndClick ? "POINT AND CLICK" : progress;
        _questionnaireValueText.text = pointAndClick ? "Aim at a value" : $"{_questionnaireHoverValue}";

        stats.stageStartTime = Time.realtimeSinceStartup;
        stats.lastSampleTime = stats.stageStartTime;
        stats.lastSlot = _questionnaireHoverValue;
        stats.lastSlotChangeTime = stats.stageStartTime;
        stats.pauseCountedForCurrentDwell = false;
        stats.nextTraceSampleTime = stats.stageStartTime + Mathf.Max(0.01f, questionnaireTraceSampleIntervalSec);
        stats.nextPhysicalSpeedSampleTime = stats.stageStartTime;
        stats.activeGrabIndex = 0;
        stats.hasSlotSpeedBaseline = false;
        stats.hasPhysicalSpeedBaseline = false;
        _activeQuestionnaireMotionStats = stats;
        _activeQuestionnaireRecord = record;
        _activeQuestionnaireStageName = stageName;
        AddQuestionnaireTraceSample(stats, _questionnaireHoverValue, "stage_enter", "system", isAnchor: true);
        if (pointAndClick)
            _comparisonPointClickWasHeld = ComparisonPointClickHeldNow();
        else
            ResetQuestionnaireConfirmHold(IsQuestionnaireConfirmHeldNow());
        LogQuestionnaireStageEvent(record, stageName, "Enter");
        LogQuestionnaireInteractionEvent(
            record,
            stageName,
            "StageEnter",
            $"scale={scale};responseMode={CurrentQuestionnaireResponseMode()}");

#if UNITY_EDITOR
        BeginSyntheticQuestionnaireStage(record, stageName, scale, pointAndClick);
#endif

        while (_questionnaireSelectedValue < 0)
        {
            HandleQuestionnaireKeyboard(scale);

#if UNITY_EDITOR
            if (SyntheticParticipantActive && !pointAndClick)
                UpdateSyntheticKnobQuestionnaire();
#endif

            bool pointClickPressed = false;
            bool hasGrabKnob = !pointAndClick &&
                               _paxsmQuestionnaireKnobCore != null &&
                               _paxsmQuestionnaireGrab != null;
            bool knobGrabbing = hasGrabKnob && QuestionnaireGrabIsActive();
            if (hasGrabKnob)
            {
                _questionnaireHoverValue = _paxsmQuestionnaireKnobCore.CurrentSlot;
                if (knobGrabbing)
                    MarkQuestionnaireFirstInteraction(stats, Time.realtimeSinceStartup);
                TrackQuestionnaireGrabState(stats, knobGrabbing);
                TrackQuestionnairePhysicalSpeed(stats, knobGrabbing);
                TrackQuestionnaireKnobDwell(stats);
                if (_selectionRayRenderer != null)
                    _selectionRayRenderer.enabled = false;
            }
            else if (pointAndClick)
            {
#if UNITY_EDITOR
                Ray pointerRay;
                Vector3 pointerOrigin;
                int hoverValue;
                if (SyntheticParticipantActive)
                {
                    pointClickPressed = UpdateSyntheticPointClickQuestionnaire(
                        out hoverValue,
                        out pointerRay,
                        out pointerOrigin);
                }
                else
                {
                    pointClickPressed = GetComparisonPointClickPressed(out pointerRay, out pointerOrigin);
                    hoverValue = ResolveQuestionnairePointClickValue(pointerRay);
                }
#else
                pointClickPressed = GetComparisonPointClickPressed(out Ray pointerRay, out Vector3 pointerOrigin);
                int hoverValue = ResolveQuestionnairePointClickValue(pointerRay);
#endif
                if (hoverValue != _questionnaireHoverValue)
                {
                    _questionnaireHoverValue = hoverValue;
                    if (hoverValue > 0)
                    {
                        AddQuestionnaireTraceSample(stats, hoverValue, "slot_change", "point_and_click", isAnchor: false);
                        MarkQuestionnaireFirstInteraction(stats, Time.realtimeSinceStartup);
                    }
                }
                TrackQuestionnairePointerMotion(pointerOrigin, stats);
                UpdateSelectionRayVisual(pointerRay);
            }
            else
            {
                GetSelectionPressed(out Ray pointerRay, out Vector3 pointerOrigin);
                int hoverValue = ResolveQuestionnaireScaleValue(pointerRay, scale);
                if (hoverValue > 0 && hoverValue != _questionnaireHoverValue)
                {
                    _questionnaireHoverValue = hoverValue;
                    AddQuestionnaireTraceSample(stats, hoverValue, "slot_change", "pointer", isAnchor: false);
                    MarkQuestionnaireFirstInteraction(stats, Time.realtimeSinceStartup);
                }
                TrackQuestionnairePointerMotion(pointerOrigin, stats);
                UpdateSelectionRayVisual(pointerRay);
            }

            string traceSource = hasGrabKnob
                ? "paxsm_knob"
                : (pointAndClick ? "point_and_click" : "pointer");
            SampleQuestionnaireTrace(stats, _questionnaireHoverValue, traceSource);

            UpdateQuestionnaireTickVisuals(_questionnaireHoverValue, _questionnaireSelectedValue);
            _questionnaireValueText.text = _questionnaireHoverValue > 0
                ? $"{_questionnaireHoverValue}"
                : "Aim at a value";

            bool canConfirm = _questionnaireHoverValue > 0 &&
                              (!hasGrabKnob || !knobGrabbing);
            bool confirmed = pointAndClick
                ? pointClickPressed && canConfirm
                : UpdateQuestionnaireConfirmHold(canConfirm, stats);
            if (confirmed)
            {
                _questionnaireSelectedValue = _questionnaireHoverValue;
                stats.confirmCount++;
                if (pointAndClick)
                {
                    stats.confirmAttemptCount++;
                    stats.confirmHoldDuration = 0f;
                    TryPlayRightHandHaptic(0.42f, 0.07f, force: true);
                }
                stats.stageEndTime = Time.realtimeSinceStartup;
                AddQuestionnaireTraceSample(stats, _questionnaireSelectedValue, "stage_exit", "system", isAnchor: true);
                FinalizeQuestionnaireStage(stats, scale);
                AppendQuestionnaireRawTraceRecords(record, stageName, stats);
                LogQuestionnaireStageEvent(record, stageName, "Confirm");
                LogQuestionnaireStageEvent(record, stageName, "Exit");
                LogQuestionnaireInteractionEvent(
                    record,
                    stageName,
                    pointAndClick ? "PointClickConfirmed" : "Confirmed",
                    $"slot={_questionnaireSelectedValue}");
                LogQuestionnaireInteractionEvent(record, stageName, "StageExit", $"rt={F(stats.duration)}");
            }

            yield return null;
        }

        _activeQuestionnaireMotionStats = null;
        _activeQuestionnaireRecord = null;
        _activeQuestionnaireStageName = "";
        UpdateQuestionnaireTickVisuals(_questionnaireHoverValue, _questionnaireSelectedValue);
        _questionnaireValueText.text = $"Confirmed: {_questionnaireSelectedValue}";
        if (!pointAndClick)
            UpdateQuestionnaireConfirmVisual(1f, confirmed: true);
        yield return new WaitForSeconds(0.35f);
        _questionnaireStageActive = false;
        if (_selectionRayRenderer != null)
            _selectionRayRenderer.enabled = false;
    }

    void LogQuestionnaireStageEvent(QuestionnaireRecord record, string stage, string eventType)
    {
        if (record == null)
            return;

        _questionnaireStageEvents.Add(new QuestionnaireStageEvent
        {
            blockId = record.blockId,
            presentationOrder = record.presentationOrder,
            itemIndex = record.itemIndex,
            itemId = record.itemId,
            stage = stage,
            eventType = eventType,
            realtime = Time.realtimeSinceStartup,
            utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        });
    }

    void LogQuestionnaireInteractionEvent(
        QuestionnaireRecord record,
        string stage,
        string tag,
        string data = "")
    {
        if (record == null)
            return;

        _questionnaireInteractionEvents.Add(new QuestionnaireInteractionEvent
        {
            blockId = record.blockId,
            presentationOrder = record.presentationOrder,
            itemIndex = record.itemIndex,
            itemId = record.itemId,
            stage = stage,
            tag = tag,
            data = data ?? "",
            realtime = Time.realtimeSinceStartup,
            utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        });
    }

    void MarkQuestionnaireFirstInteraction(QuestionnaireMotionStats stats, float now)
    {
        if (stats == null || stats.firstInteractionRt >= 0f)
            return;
        stats.firstInteractionRt = Mathf.Max(0f, now - stats.stageStartTime);
        LogQuestionnaireInteractionEvent(
            _activeQuestionnaireRecord,
            _activeQuestionnaireStageName,
            "FirstInteraction",
            $"rt={F(stats.firstInteractionRt)}");
    }

    void TrackQuestionnaireGrabState(QuestionnaireMotionStats stats, bool grabbing)
    {
        if (stats == null || stats.wasGrabbing == grabbing)
            return;

        float now = Time.realtimeSinceStartup;
        stats.wasGrabbing = grabbing;
        if (grabbing)
        {
            stats.grabCount++;
            if (ShouldRecordQuestionnairePersonalSpeed())
            {
                stats.activeGrabIndex++;
                stats.hasSlotSpeedBaseline = true;
                stats.slotSpeedBaselineSlot = _questionnaireHoverValue;
                stats.slotSpeedBaselineTime = now;
                stats.hasPhysicalSpeedBaseline = false;
                stats.nextPhysicalSpeedSampleTime = now;
            }
            LogQuestionnaireInteractionEvent(
                _activeQuestionnaireRecord,
                _activeQuestionnaireStageName,
                "GrabStart",
                $"slot={_questionnaireHoverValue}");
        }
        else
        {
            stats.hasSlotSpeedBaseline = false;
            stats.hasPhysicalSpeedBaseline = false;
            LogQuestionnaireInteractionEvent(
                _activeQuestionnaireRecord,
                _activeQuestionnaireStageName,
                "GrabEnd",
                $"slot={_questionnaireHoverValue}");
        }
    }

    bool ShouldRecordQuestionnairePersonalSpeed()
    {
        return recordQuestionnairePersonalSpeed && IsQuestionnaireKnobInputActive;
    }

    bool QuestionnaireGrabIsActive()
    {
#if UNITY_EDITOR
        if (SyntheticParticipantActive)
            return SyntheticQuestionnaireGrabbing();
#endif
        return _paxsmQuestionnaireGrab != null && _paxsmQuestionnaireGrab.IsGrabbing;
    }

    float QuestionnaireCurrentTwistDegrees()
    {
#if UNITY_EDITOR
        if (SyntheticParticipantActive)
            return SyntheticQuestionnaireTwistDegrees();
#endif
        return _paxsmQuestionnaireGrab != null
            ? _paxsmQuestionnaireGrab.CurrentTwistDegrees
            : 0f;
    }

    void TrackQuestionnairePhysicalSpeed(QuestionnaireMotionStats stats, bool grabbing)
    {
        if (!ShouldRecordQuestionnairePersonalSpeed() || stats == null || !grabbing ||
            _paxsmQuestionnaireGrab == null || _activeQuestionnaireRecord == null)
            return;

        float now = Time.realtimeSinceStartup;
        float twist = QuestionnaireCurrentTwistDegrees();
        if (!stats.hasPhysicalSpeedBaseline)
        {
            stats.hasPhysicalSpeedBaseline = true;
            stats.lastPhysicalSpeedSampleTime = now;
            stats.lastPhysicalTwistDegrees = twist;
            stats.nextPhysicalSpeedSampleTime = now + Mathf.Max(0.01f, questionnairePhysicalSpeedSampleIntervalSec);
            AddQuestionnairePhysicalSpeedSample(
                stats, now, twist, 0f, 0f, 0f, false, "baseline");
            return;
        }

        if (now < stats.nextPhysicalSpeedSampleTime)
            return;

        float dt = now - stats.lastPhysicalSpeedSampleTime;
        float deltaDegrees = twist - stats.lastPhysicalTwistDegrees;
        float angularSpeed = dt > 0.0001f ? Mathf.Abs(deltaDegrees) / dt : 0f;
        bool valid = true;
        string exclusionReason = "";
        if (dt <= 0.0001f)
        {
            valid = false;
            exclusionReason = "nonpositive_dt";
        }
        else if (dt > Mathf.Max(0.03f, questionnairePhysicalSpeedMaximumSampleGapSec))
        {
            valid = false;
            exclusionReason = "sample_gap_too_long";
        }
        else if (Mathf.Abs(deltaDegrees) < Mathf.Max(0f, questionnairePhysicalSpeedMinimumDeltaDegrees))
        {
            valid = false;
            exclusionReason = "below_motion_delta";
        }
        else if (angularSpeed < Mathf.Max(0f, questionnairePhysicalSpeedMinimumDps))
        {
            valid = false;
            exclusionReason = "below_motion_speed";
        }

        stats.latestPhysicalAngularSpeedDps = angularSpeed;
        AddQuestionnairePhysicalSpeedSample(
            stats, now, twist, deltaDegrees, dt, angularSpeed, valid, exclusionReason);
        stats.lastPhysicalSpeedSampleTime = now;
        stats.lastPhysicalTwistDegrees = twist;
        stats.nextPhysicalSpeedSampleTime = now + Mathf.Max(0.01f, questionnairePhysicalSpeedSampleIntervalSec);
    }

    void AddQuestionnairePhysicalSpeedSample(
        QuestionnaireMotionStats stats,
        float now,
        float twist,
        float deltaDegrees,
        float dt,
        float angularSpeed,
        bool valid,
        string exclusionReason)
    {
        QuestionnaireRecord record = _activeQuestionnaireRecord;
        if (stats == null || record == null)
            return;

        stats.physicalSpeedSampleIndex++;
        _questionnairePhysicalSpeedSamples.Add(new QuestionnairePhysicalSpeedSample
        {
            blockId = record.blockId,
            presentationOrder = record.presentationOrder,
            itemIndex = record.itemIndex,
            itemId = record.itemId,
            stage = _activeQuestionnaireStageName,
            stageStartRealtime = stats.stageStartTime,
            grabIndex = stats.activeGrabIndex,
            sampleIndex = stats.physicalSpeedSampleIndex,
            realtime = now,
            slot = _questionnaireHoverValue,
            wristTwistDegrees = twist,
            deltaDegrees = deltaDegrees,
            deltaTime = dt,
            physicalAngularSpeedDps = angularSpeed,
            validForCalibration = valid,
            exclusionReason = exclusionReason ?? ""
        });
    }

    void AddQuestionnaireTraceSample(
        QuestionnaireMotionStats stats,
        int slot,
        string sampleType,
        string source,
        bool isAnchor)
    {
        if (stats == null || slot <= 0)
            return;

        float now = Time.realtimeSinceStartup;
        stats.traceSamples.Add(new QuestionnaireTraceSample
        {
            realtime = now,
            slot = slot,
            angle = SlotAngle(slot, _questionnaireCurrentScale, QuestionnaireKnobMinAngle(), QuestionnaireKnobMaxAngle()),
            sampleType = sampleType ?? "",
            source = source ?? "",
            isAnchor = isAnchor
        });
    }

    void SampleQuestionnaireTrace(QuestionnaireMotionStats stats, int slot, string source)
    {
        if (stats == null || slot <= 0)
            return;
        float now = Time.realtimeSinceStartup;
        if (now < stats.nextTraceSampleTime)
            return;

        AddQuestionnaireTraceSample(stats, slot, "idle_sample", source, isAnchor: false);
        float interval = Mathf.Max(0.01f, questionnaireTraceSampleIntervalSec);
        stats.nextTraceSampleTime = now + interval;
    }

    void FinalizeQuestionnaireStage(QuestionnaireMotionStats stats, int scale)
    {
        if (stats == null)
            return;
        if (stats.stageEndTime < stats.stageStartTime)
            stats.stageEndTime = Time.realtimeSinceStartup;
        stats.duration = Mathf.Max(0f, stats.stageEndTime - stats.stageStartTime);

        var settings = new QuestionnaireTraceAnalyzer.Settings
        {
            pauseThresholdSec = questionnairePauseThresholdSec,
            stillThresholdSec = questionnaireStillThresholdSec,
            fastFlickThresholdSps = questionnaireFastFlickThresholdSps,
            speedDeltaMin = questionnaireSpeedDeltaMin,
            speedDeltaK = questionnaireSpeedDeltaK,
            speedBandMinimumEpisodes = questionnaireSpeedBandMinimumEpisodes,
            microMinimumTransitions = questionnaireMicroMinimumTransitions,
            microMaximumSlotSpan = questionnaireMicroMaximumSlotSpan
        };
        stats.derived = QuestionnaireTraceAnalyzer.Analyze(
            stats.traceSamples,
            scale,
            QuestionnaireKnobMinAngle(),
            QuestionnaireKnobMaxAngle(),
            settings);
        stats.derived.confirmCount = stats.confirmCount;
    }

    void AppendQuestionnaireRawTraceRecords(
        QuestionnaireRecord record,
        string stageName,
        QuestionnaireMotionStats stats)
    {
        if (record == null || stats == null)
            return;
        for (int i = 0; i < stats.traceSamples.Count; i++)
        {
            _questionnaireRawTraceRecords.Add(new QuestionnaireRawTraceRecord
            {
                blockId = record.blockId,
                presentationOrder = record.presentationOrder,
                itemIndex = record.itemIndex,
                itemId = record.itemId,
                stage = stageName,
                stageStartRealtime = stats.stageStartTime,
                eventIndex = i,
                sample = stats.traceSamples[i]
            });
        }
    }

    void SetupQuestionnaireScale(int scale)
    {
        ClearQuestionnaireTicks();
        _questionnaireCurrentScale = scale;
        BuildQuestionnaireWallScale(scale);

        if (questionnaireInputMethod == QuestionnaireInputMethod.PointAndClick)
            return;

        BuildQuestionnaireKnobPanel();

        if (useMainSceneKnobRig && TrySetupMainSceneKnobRig(scale))
            return;

        Vector3 center = QuestionnaireKnobCenter();
        float radius = QuestionnaireKnobRadius();

        _questionnaireKnobFace = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _questionnaireKnobFace.name = "PAXSM_KnobFace";
        _questionnaireKnobFace.transform.SetParent(_questionnaireRuntimeRoot, false);
        _questionnaireKnobFace.transform.position = center;
        _questionnaireKnobFace.transform.rotation = QuestionnairePanelRotation() * Quaternion.Euler(90f, 0f, 0f);
        _questionnaireKnobFace.transform.localScale = new Vector3(radius * 1.55f, 0.075f, radius * 1.55f);
        Renderer faceRenderer = _questionnaireKnobFace.GetComponent<Renderer>();
        if (faceRenderer != null)
            faceRenderer.sharedMaterial = _normalMaterial;
        _questionnaireTicks.Add(_questionnaireKnobFace);

        _questionnaireKnobPointer = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _questionnaireKnobPointer.name = "PAXSM_KnobPointer";
        _questionnaireKnobPointer.transform.SetParent(_questionnaireRuntimeRoot, false);
        Renderer pointerRenderer = _questionnaireKnobPointer.GetComponent<Renderer>();
        if (pointerRenderer != null)
            pointerRenderer.sharedMaterial = _questionnaireSelectedMaterial;
        _questionnaireTicks.Add(_questionnaireKnobPointer);

        _questionnaireKnobHub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _questionnaireKnobHub.name = "PAXSM_KnobHub";
        _questionnaireKnobHub.transform.SetParent(_questionnaireRuntimeRoot, false);
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
            tick.transform.SetParent(_questionnaireRuntimeRoot, false);
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
            {
                _questionnaireTicks[i].SetActive(false);
                Destroy(_questionnaireTicks[i]);
            }
        }
        _questionnaireTicks.Clear();

        for (int i = _questionnairePanelObjects.Count - 1; i >= 0; i--)
        {
            if (_questionnairePanelObjects[i] != null)
            {
                _questionnairePanelObjects[i].SetActive(false);
                Destroy(_questionnairePanelObjects[i]);
            }
        }
        _questionnairePanelObjects.Clear();

        for (int i = _questionnaireWallTicks.Count - 1; i >= 0; i--)
        {
            if (_questionnaireWallTicks[i] != null)
            {
                _questionnaireWallTicks[i].SetActive(false);
                Destroy(_questionnaireWallTicks[i]);
            }
        }
        _questionnaireWallTicks.Clear();
        ClearQuestionnairePointClickTargets();

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

        if (questionnaireInputMethod == QuestionnaireInputMethod.PointAndClick)
            return;

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

        _paxsmKnobRigInstance = Instantiate(prefab, _questionnaireRuntimeRoot);
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
            tick.transform.SetParent(_questionnaireRuntimeRoot, false);
            tick.transform.position = new Vector3(Mathf.Lerp(left, right, t), y, z);
            bool pointAndClick = questionnaireInputMethod == QuestionnaireInputMethod.PointAndClick;
            tick.transform.localScale = pointAndClick
                ? QuestionnairePointClickTickScale(scale, hover: false, selected: false)
                : new Vector3(major ? 0.045f : 0.026f, major ? 0.18f : 0.11f, 0.035f);
            Collider collider = tick.GetComponent<Collider>();
            if (collider != null)
                collider.enabled = pointAndClick;
            Renderer renderer = tick.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = _questionnaireTickMaterial;
            _questionnaireWallTicks.Add(tick);
            if (pointAndClick)
                RegisterQuestionnairePointClickTarget(tick, value, scale);
        }
    }

    void BuildQuestionnaireKnobPanel()
    {
        Vector3 center = QuestionnaireKnobCenter();

        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "PAXSM_KnobBackingPanel";
        panel.transform.SetParent(_questionnaireRuntimeRoot, false);
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
        _questionnaireConfirmTrack.transform.SetParent(_questionnaireRuntimeRoot, false);
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
        _questionnaireConfirmFill.transform.SetParent(_questionnaireRuntimeRoot, false);
        _questionnaireConfirmFill.transform.rotation = QuestionnairePanelRotation();
        Collider fillCollider = _questionnaireConfirmFill.GetComponent<Collider>();
        if (fillCollider != null)
            fillCollider.enabled = false;
        Renderer fillRenderer = _questionnaireConfirmFill.GetComponent<Renderer>();
        if (fillRenderer != null)
            fillRenderer.sharedMaterial = _questionnaireSelectedMaterial;
        _questionnairePanelObjects.Add(_questionnaireConfirmFill);

        GameObject hintObject = new GameObject("PAXSM_KnobHint");
        hintObject.transform.SetParent(_questionnaireRuntimeRoot, false);
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
        bool pointAndClick = questionnaireInputMethod == QuestionnaireInputMethod.PointAndClick;
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

            if (pointAndClick)
            {
                tick.transform.localScale = QuestionnairePointClickTickScale(
                    _questionnaireWallTicks.Count,
                    pending,
                    confirmed);
                continue;
            }

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

        float now = Time.realtimeSinceStartup;
        if (stats.lastSlot < 0)
        {
            stats.lastSlot = slot;
            stats.lastSlotChangeTime = now;
            return;
        }

        int deltaSlots = slot - stats.lastSlot;
        if (deltaSlots == 0)
            return;

        RecordQuestionnaireSlotSpeedEvent(stats, slot, now);
        MarkQuestionnaireFirstInteraction(stats, now);
        AddQuestionnaireTraceSample(stats, slot, "slot_change", "paxsm_knob", isAnchor: false);
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

    void RecordQuestionnaireSlotSpeedEvent(
        QuestionnaireMotionStats stats,
        int toSlot,
        float now)
    {
        if (!ShouldRecordQuestionnairePersonalSpeed() || stats == null ||
            _activeQuestionnaireRecord == null)
            return;

        bool grabbing = QuestionnaireGrabIsActive();
        bool hasBaseline = stats.hasSlotSpeedBaseline && stats.slotSpeedBaselineSlot > 0;
        int fromSlot = hasBaseline ? stats.slotSpeedBaselineSlot : stats.lastSlot;
        int deltaSlots = toSlot - fromSlot;
        float dt = hasBaseline ? now - stats.slotSpeedBaselineTime : 0f;
        float slotsPerSecond = dt > 0.0001f ? Mathf.Abs(deltaSlots) / dt : -1f;
        float slotAngleDegrees = _questionnaireCurrentScale > 1
            ? Mathf.Abs(QuestionnaireKnobMaxAngle() - QuestionnaireKnobMinAngle()) /
              (_questionnaireCurrentScale - 1f)
            : 0f;
        float detentAngularSpeedDps = slotsPerSecond >= 0f
            ? slotsPerSecond * slotAngleDegrees
            : -1f;

        bool valid = true;
        string exclusionReason = "";
        if (!grabbing)
        {
            valid = false;
            exclusionReason = "not_wrist_grab_input";
        }
        else if (!hasBaseline)
        {
            valid = false;
            exclusionReason = "missing_grab_baseline";
        }
        else if (deltaSlots == 0)
        {
            valid = false;
            exclusionReason = "no_slot_change";
        }
        else if (dt <= 0.0001f)
        {
            valid = false;
            exclusionReason = "nonpositive_dt";
        }
        else if (dt > Mathf.Max(0.05f, questionnaireSlotSpeedMaximumTransitionGapSec))
        {
            valid = false;
            exclusionReason = "transition_gap_too_long";
        }

        stats.slotSpeedEventIndex++;
        QuestionnaireRecord record = _activeQuestionnaireRecord;
        _questionnaireSlotSpeedEvents.Add(new QuestionnaireSlotSpeedEvent
        {
            blockId = record.blockId,
            presentationOrder = record.presentationOrder,
            itemIndex = record.itemIndex,
            itemId = record.itemId,
            stage = _activeQuestionnaireStageName,
            stageStartRealtime = stats.stageStartTime,
            grabIndex = stats.activeGrabIndex,
            eventIndex = stats.slotSpeedEventIndex,
            realtime = now,
            fromSlot = fromSlot,
            toSlot = toSlot,
            deltaSlots = deltaSlots,
            deltaTime = dt,
            slotsPerSecond = slotsPerSecond,
            slotAngleDegrees = slotAngleDegrees,
            detentAngularSpeedDps = detentAngularSpeedDps,
            wristTwistDegrees = QuestionnaireCurrentTwistDegrees(),
            latestPhysicalAngularSpeedDps = stats.latestPhysicalAngularSpeedDps,
            validForCalibration = valid,
            exclusionReason = exclusionReason
        });

        if (grabbing)
        {
            stats.hasSlotSpeedBaseline = true;
            stats.slotSpeedBaselineSlot = toSlot;
            stats.slotSpeedBaselineTime = now;
        }
    }

    void TrackQuestionnaireKnobDwell(QuestionnaireMotionStats stats)
    {
        if (stats == null || stats.pauseCountedForCurrentDwell)
            return;
        if (Time.realtimeSinceStartup - stats.lastSlotChangeTime < questionnairePauseThresholdSec)
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
        float now = Time.realtimeSinceStartup;
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
            {
                stats.hoverChangeCount++;
                MarkQuestionnaireFirstInteraction(stats, now);
            }
            stats.lastHoverValue = _questionnaireHoverValue;
        }
    }

    void CopyQuestionnaireStats(QuestionnaireMotionStats stats, QuestionnaireRecord record, bool answerStage)
    {
        QuestionnaireStageMetrics metrics = stats.derived ?? new QuestionnaireStageMetrics();
        if (answerStage)
        {
            record.answerMetrics = metrics;
            record.answerPointerPath = stats.path;
            record.answerPeakSpeed = metrics.maxAbsVel;
            record.answerPauseCount = metrics.pauseCount;
            record.answerHoverChangeCount = stats.hoverChangeCount;
            record.answerTotalAbsAngle = metrics.totalAbsAngle;
            record.answerMaxAbsVel = metrics.maxAbsVel;
            record.answerSlotChangeCount = metrics.slotChangeCount;
            record.answerReverseCount = metrics.reverseCount;
            record.answerMicroAdjustCount = metrics.microAdjustCount;
            record.answerFastFlickCount = metrics.fastFlickCount;
        }
        else
        {
            record.confidenceMetrics = metrics;
            record.confidencePointerPath = stats.path;
            record.confidencePeakSpeed = metrics.maxAbsVel;
            record.confidencePauseCount = metrics.pauseCount;
            record.confidenceHoverChangeCount = stats.hoverChangeCount;
            record.confidenceTotalAbsAngle = metrics.totalAbsAngle;
            record.confidenceMaxAbsVel = metrics.maxAbsVel;
            record.confidenceSlotChangeCount = metrics.slotChangeCount;
            record.confidenceReverseCount = metrics.reverseCount;
            record.confidenceMicroAdjustCount = metrics.microAdjustCount;
            record.confidenceFastFlickCount = metrics.fastFlickCount;
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
            else
            {
                int oldValue = _questionnaireHoverValue;
                _questionnaireHoverValue = Mathf.Max(1, _questionnaireHoverValue - 1);
                if (_questionnaireHoverValue != oldValue)
                {
                    AddQuestionnaireTraceSample(_activeQuestionnaireMotionStats, _questionnaireHoverValue, "slot_change", "keyboard", false);
                    MarkQuestionnaireFirstInteraction(_activeQuestionnaireMotionStats, Time.realtimeSinceStartup);
                }
            }
        }
        if (keyboard.rightArrowKey.wasPressedThisFrame)
        {
            if (_paxsmQuestionnaireKnobCore != null) _paxsmQuestionnaireKnobCore.Step(+1);
            else
            {
                int oldValue = _questionnaireHoverValue;
                _questionnaireHoverValue = Mathf.Min(scale, _questionnaireHoverValue + 1);
                if (_questionnaireHoverValue != oldValue)
                {
                    AddQuestionnaireTraceSample(_activeQuestionnaireMotionStats, _questionnaireHoverValue, "slot_change", "keyboard", false);
                    MarkQuestionnaireFirstInteraction(_activeQuestionnaireMotionStats, Time.realtimeSinceStartup);
                }
            }
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
            if (_questionnaireConfirmHoldStart >= 0f)
            {
                if (stats != null) stats.confirmCancelCount++;
                LogQuestionnaireInteractionEvent(
                    _activeQuestionnaireRecord,
                    _activeQuestionnaireStageName,
                    "ConfirmHoldCanceled",
                    "reason=cannot_confirm");
            }
            if (held)
                _questionnaireConfirmNeedsRelease = true;
            _questionnaireConfirmHoldStart = -1f;
            UpdateQuestionnaireConfirmVisual(0f, confirmed: false);
            return false;
        }

        if (!held)
        {
            if (_questionnaireConfirmHoldStart >= 0f)
            {
                if (stats != null) stats.confirmCancelCount++;
                LogQuestionnaireInteractionEvent(
                    _activeQuestionnaireRecord,
                    _activeQuestionnaireStageName,
                    "ConfirmHoldCanceled",
                    "reason=released_early");
            }
            _questionnaireConfirmHoldStart = -1f;
            _questionnaireNextConfirmHapticTime = 0f;
            UpdateQuestionnaireConfirmVisual(0f, confirmed: false);
            return false;
        }

        float now = Time.realtimeSinceStartup;
        if (_questionnaireConfirmHoldStart < 0f)
        {
            _questionnaireConfirmHoldStart = now;
            _questionnaireNextConfirmHapticTime = now;
            if (stats != null) stats.confirmAttemptCount++;
            LogQuestionnaireInteractionEvent(
                _activeQuestionnaireRecord,
                _activeQuestionnaireStageName,
                "ConfirmHoldStarted",
                $"slot={_questionnaireHoverValue}");
        }

        float holdSeconds = Mathf.Max(0.1f, questionnaireConfirmHoldSeconds);
        float elapsed = now - _questionnaireConfirmHoldStart;
        float progress = Mathf.Clamp01(elapsed / holdSeconds);
        UpdateQuestionnaireConfirmVisual(progress, confirmed: false);

        if (progress >= 1f)
        {
            if (stats != null)
                stats.confirmHoldDuration = Mathf.Max(0f, elapsed);
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
#if UNITY_EDITOR
        if (SyntheticParticipantActive)
            return SyntheticQuestionnaireConfirmHeld();
#endif
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

    bool IsQuestionnaireReadContinueHeldNow()
    {
#if UNITY_EDITOR
        if (SyntheticParticipantActive)
            return SyntheticQuestionnaireReadHeld();
#endif
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
        if (keyboard != null && keyboard.aKey != null && keyboard.aKey.isPressed)
            held = true;
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

    protected virtual void OnApplicationQuit()
    {
        if (!_completedExportWritten && writeCsvOnQuit &&
            (_trialRecords.Count > 0 || _questionnaireRecords.Count > 0 ||
             _questionnaireStageEvents.Count > 0 || _questionnaireRawTraceRecords.Count > 0 ||
             _questionnaireInteractionEvents.Count > 0 ||
             _questionnairePhysicalSpeedSamples.Count > 0 || _questionnaireSlotSpeedEvents.Count > 0))
            WriteCsvFiles("quit");
    }

    protected virtual void OnDisable()
    {
        SetQuestionnaireJumpSuppressed(false);
    }

    void WriteCsvFiles(string reason)
    {
        if (!questionnaireOnlyMode)
        {
            if (_probeBehaviorCollector == null)
                _probeBehaviorCollector = GetComponent<XRWorkloadProbeBehaviorCollector>();
            _probeBehaviorCollector?.FlushForExport(reason);
        }

        string folder = GetOutputFolder();
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var savedPaths = new List<string>();
        WriteComparisonOutputs(folder, stamp, reason, savedPaths);
        if (!questionnaireOnlyMode)
        {
            string trialPath = Path.Combine(folder, $"WorkloadProbe_Trials_{participantId}_{stamp}_{reason}.csv");
            string blockPath = Path.Combine(folder, $"WorkloadProbe_Blocks_{participantId}_{stamp}_{reason}.csv");
            string runOrderPath = Path.Combine(folder, $"WorkloadProbe_RunOrder_{participantId}_{stamp}_{reason}.csv");
            WriteCheckpointFile(trialPath, BuildTrialCsv());
            WriteCheckpointFile(blockPath, BuildBlockCsv());
            WriteCheckpointFile(runOrderPath, BuildRunOrderCsv());
            savedPaths.Add(trialPath);
            savedPaths.Add(blockPath);
            savedPaths.Add(runOrderPath);
            if (requireParticipantConfirmationBetweenBlocks)
            {
                string confirmationPath = Path.Combine(
                    folder,
                    $"WorkloadProbe_InterBlockConfirmations_{participantId}_{stamp}_{reason}.csv");
                WriteCheckpointFile(confirmationPath, BuildInterBlockConfirmationCsv());
                savedPaths.Add(confirmationPath);
            }
        }

        string suffix = $"{participantId}_{stamp}_{reason}";
        string questionnairePath = Path.Combine(folder, $"CAREXR_Questionnaire_{suffix}.csv");
        string stageEventsPath = Path.Combine(folder, $"CAREXR_Questionnaire_StageEvents_{suffix}.csv");
        WriteCheckpointFile(questionnairePath, BuildQuestionnaireCsv());
        WriteCheckpointFile(stageEventsPath, BuildQuestionnaireStageEventsCsv());
        savedPaths.Add(questionnairePath);
        savedPaths.Add(stageEventsPath);

        if (_questionnaireRecords.Count > 0 || _questionnaireRawTraceRecords.Count > 0 ||
            _questionnaireInteractionEvents.Count > 0 ||
            _questionnairePhysicalSpeedSamples.Count > 0 || _questionnaireSlotSpeedEvents.Count > 0)
        {
            string rawTracePath = Path.Combine(folder, $"CAREXR_Questionnaire_RawTrace_{suffix}.csv");
            string interactionPath = Path.Combine(folder, $"CAREXR_Questionnaire_InteractionEvents_{suffix}.csv");
            string metadataPath = Path.Combine(folder, $"CAREXR_Questionnaire_Metadata_{suffix}.csv");
            string physicalSpeedPath = Path.Combine(folder, $"CAREXR_Questionnaire_PhysicalSpeedSamples_{suffix}.csv");
            string slotSpeedPath = Path.Combine(folder, $"CAREXR_Questionnaire_SlotSpeedEvents_{suffix}.csv");
            string speedSummaryPath = Path.Combine(folder, $"CAREXR_Questionnaire_SpeedSummary_{suffix}.csv");
            WriteCheckpointFile(rawTracePath, BuildQuestionnaireRawTraceCsv());
            WriteCheckpointFile(interactionPath, BuildQuestionnaireInteractionEventsCsv());
            WriteCheckpointFile(metadataPath, BuildQuestionnaireMetadataCsv());
            WriteCheckpointFile(physicalSpeedPath, BuildQuestionnairePhysicalSpeedSamplesCsv());
            WriteCheckpointFile(slotSpeedPath, BuildQuestionnaireSlotSpeedEventsCsv());
            WriteCheckpointFile(speedSummaryPath, BuildQuestionnaireSpeedSummaryCsv());
            savedPaths.Add(rawTracePath);
            savedPaths.Add(interactionPath);
            savedPaths.Add(metadataPath);
            savedPaths.Add(physicalSpeedPath);
            savedPaths.Add(slotSpeedPath);
            savedPaths.Add(speedSummaryPath);
        }

        WriteResponseCalibrationArtifacts(folder, stamp, reason, savedPaths);

        // The legacy merged exporter owns its own subfolder convention. Console-configured
        // CARE-XR runs use the unified ExperimentRunContext paths above instead.
        if (!personalKnobReferenceMode &&
            !ExperimentRunContext.IsConfigured &&
            _mainSceneMergedExporter != null &&
            _mainSceneAnswerExportMirror != null &&
            _mainSceneAnswerExportMirror.summaries.Count > 0)
        {
            _mainSceneMergedExporter.participantNumber = ParseParticipantNumber(participantId);
            _mainSceneMergedExporter.outputSubfolder = outputFolderName;
            _mainSceneMergedExporter.ExportNow(reason);
        }

        if (!questionnaireOnlyMode)
        {
            string integrityPath = Path.Combine(
                folder,
                $"WorkloadProbe_DataIntegrity_{participantId}_{stamp}_{reason}.csv");
            WriteCheckpointFile(integrityPath, BuildWorkloadProbeDataIntegrityCsv(reason, folder, stamp));
            _lastDataIntegrityPath = integrityPath;
            savedPaths.Add(integrityPath);
            if (!_lastDataIntegrityPassed)
                Debug.LogError(
                    $"[XRWorkloadProbe] DATA INTEGRITY CHECK FAILED. Do not use this run. Review: {integrityPath}",
                    this);
        }

        if (string.Equals(reason, "completed", StringComparison.OrdinalIgnoreCase))
            DeleteQuestionnaireLiveCheckpoints(folder);

        Debug.Log($"[XRWorkloadProbe] Saved logs:\n{string.Join("\n", savedPaths)}", this);
        if (string.Equals(reason, "completed", StringComparison.OrdinalIgnoreCase))
            _completedExportWritten = true;
    }

    // Target-entry calibration uses the same knob instrumentation as a substantive item,
    // while keeping the intended target explicit so that the personal reference remains auditable.
    float CalculateCalibrationPathRatio(QuestionnaireRecord record, bool answerStage)
    {
        if (record == null)
            return -1f;

        int scale = answerStage ? Mathf.Max(2, record.scale) : Mathf.Max(2, questionnaireConfidenceScale);
        int target = answerStage ? record.expectedAnswerTarget : record.expectedConfidenceTarget;
        int initial = answerStage ? record.answerInitialSlot : record.confidenceInitialSlot;
        float totalAbsAngle = answerStage ? record.answerTotalAbsAngle : record.confidenceTotalAbsAngle;
        if (target < 1 || initial < 1 || totalAbsAngle < 0f)
            return -1f;

        float shortestRequiredAngle = CalculateShortestRequiredAngle(initial, target, scale);
        float oneSlotAngle = Mathf.Abs(QuestionnaireKnobMaxAngle() - QuestionnaireKnobMinAngle()) /
                             Mathf.Max(1f, scale - 1f);
        if (shortestRequiredAngle <= 0.0001f)
            return totalAbsAngle <= oneSlotAngle * 0.5f
                ? 1f
                : totalAbsAngle / Mathf.Max(0.0001f, oneSlotAngle);

        return totalAbsAngle / shortestRequiredAngle;
    }

    float CalculateObservedPathRatio(int initialSlot, int finalSlot, float totalAbsAngle, int scale)
    {
        if (initialSlot < 1 || finalSlot < 1 || totalAbsAngle < 0f)
            return -1f;

        float shortestRequiredAngle = CalculateShortestRequiredAngle(initialSlot, finalSlot, scale);
        float oneSlotAngle = Mathf.Abs(QuestionnaireKnobMaxAngle() - QuestionnaireKnobMinAngle()) /
                             Mathf.Max(1f, Mathf.Max(2, scale) - 1f);
        if (shortestRequiredAngle <= 0.0001f)
            return totalAbsAngle <= oneSlotAngle * 0.5f
                ? 1f
                : totalAbsAngle / Mathf.Max(0.0001f, oneSlotAngle);

        return totalAbsAngle / shortestRequiredAngle;
    }

    float CalculateShortestRequiredAngle(int initialSlot, int targetSlot, int scale)
    {
        int boundedScale = Mathf.Max(2, scale);
        return Mathf.Abs(
            SlotAngle(targetSlot, boundedScale, QuestionnaireKnobMinAngle(), QuestionnaireKnobMaxAngle()) -
            SlotAngle(initialSlot, boundedScale, QuestionnaireKnobMinAngle(), QuestionnaireKnobMaxAngle()));
    }

    void PopulatePersonalReferenceDistanceFields(QuestionnaireRecord record, bool answerStage)
    {
        if (record == null)
            return;

        int scale = answerStage ? Mathf.Max(2, record.scale) : Mathf.Max(2, questionnaireConfidenceScale);
        int initial = answerStage ? record.answerInitialSlot : record.confidenceInitialSlot;
        int target = answerStage ? record.expectedAnswerTarget : record.expectedConfidenceTarget;
        int distanceSlots = initial > 0 && target > 0 ? Mathf.Abs(target - initial) : -1;
        string distanceBin = GetPersonalReferenceDistanceBin(scale, distanceSlots);
        float shortestAngle = distanceSlots >= 0
            ? CalculateShortestRequiredAngle(initial, target, scale)
            : -1f;

        if (answerStage)
        {
            record.answerTargetDistanceSlots = distanceSlots;
            record.answerTargetDistanceBin = distanceBin;
            record.answerShortestRequiredAngle = shortestAngle;
        }
        else
        {
            record.confidenceTargetDistanceSlots = distanceSlots;
            record.confidenceTargetDistanceBin = distanceBin;
            record.confidenceShortestRequiredAngle = shortestAngle;
        }
    }

    public static string GetPersonalReferenceDistanceBin(int scale, int slotDistance)
    {
        if (slotDistance < 0)
            return "unknown";
        if (slotDistance == 0)
            return "zero";
        if (scale <= 5)
            return slotDistance <= 1 ? "short" : "long";

        int centeredSlot = Mathf.CeilToInt(Mathf.Max(2, scale) * 0.5f);
        int maximumDistance = Mathf.Max(1, Mathf.Min(centeredSlot - 1, Mathf.Max(2, scale) - centeredSlot));
        int shortMaximum = Mathf.Max(1, Mathf.RoundToInt(maximumDistance * 0.2f));
        int mediumMaximum = Mathf.Max(shortMaximum + 1, Mathf.RoundToInt(maximumDistance * 0.6f));
        if (slotDistance <= shortMaximum)
            return "short";
        if (slotDistance <= mediumMaximum)
            return "medium";
        return "long";
    }

    void WriteResponseCalibrationArtifacts(
        string folder,
        string stamp,
        string reason,
        List<string> savedPaths)
    {
        if (!personalKnobReferenceMode)
            return;

        PAXSMResponseCalibrationProfile profile = BuildResponseCalibrationProfile(reason);
        string suffix = $"{participantId}_{stamp}_{reason}";
        string trialsPath = Path.Combine(folder, $"PAXSM_PersonalKnobReferenceTrials_{suffix}.csv");
        string profileJsonPath = Path.Combine(folder, $"PAXSM_PersonalKnobProfile_{suffix}.json");
        string profileCsvPath = Path.Combine(folder, $"PAXSM_PersonalKnobProfile_{suffix}.csv");
        string integrityPath = Path.Combine(folder, $"PAXSM_PersonalKnobReferenceIntegrity_{suffix}.csv");
        WriteCheckpointFile(trialsPath, BuildResponseCalibrationTrialsCsv());
        WriteCheckpointFile(profileJsonPath, JsonUtility.ToJson(profile, true));
        WriteCheckpointFile(profileCsvPath, BuildResponseCalibrationProfileCsv(profile));
        WriteCheckpointFile(
            integrityPath,
            BuildPersonalKnobReferenceIntegrityCsv(
                profile,
                reason,
                folder,
                stamp,
                trialsPath,
                profileJsonPath,
                profileCsvPath));
        savedPaths.Add(trialsPath);
        savedPaths.Add(profileJsonPath);
        savedPaths.Add(profileCsvPath);
        savedPaths.Add(integrityPath);
        _lastDataIntegrityPath = integrityPath;
    }

    PAXSMResponseCalibrationProfile BuildResponseCalibrationProfile(string reason)
    {
        var profile = new PAXSMResponseCalibrationProfile
        {
            participantId = participantId,
            sessionNumber = sessionNumber,
            sourceScene = gameObject.scene.name,
            runId = ExperimentRunContext.RunId,
            generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            completionReason = reason ?? "",
            expectedTrials = Mathf.Clamp(personalReferenceTrialCount, 8, 24),
            minimumReferenceTrials = Mathf.Max(4, personalReferenceMinimumValidTrials),
            minimumTargetAccuracy = Mathf.Clamp01(personalReferenceMinimumTargetAccuracy),
            minimumValidSlotEvents = Mathf.Max(1, questionnaireCalibrationMinimumSlotEvents),
            minimumValidPhysicalSamples = Mathf.Max(1, questionnaireCalibrationMinimumPhysicalSamples),
            personalReference = BuildResponseCalibrationConditionSummary(
                "personal_reference", personalReferenceLabel)
        };

        profile.calibrationComplete =
            string.Equals(reason, "completed", StringComparison.OrdinalIgnoreCase) &&
            profile.personalReference.completedTrials >= profile.expectedTrials;

        int personalSlotEvents = profile.personalReference.answer.validSlotSpeedEventCount +
                                 profile.personalReference.confidence.validSlotSpeedEventCount;
        int personalPhysicalSamples = profile.personalReference.answer.validPhysicalSpeedSampleCount +
                                      profile.personalReference.confidence.validPhysicalSpeedSampleCount;
        bool enoughReferenceTrials =
            profile.personalReference.referenceTrialCount >= profile.minimumReferenceTrials;
        bool enoughAccuracy =
            profile.personalReference.answerTargetAccuracy >= profile.minimumTargetAccuracy &&
            profile.personalReference.confidenceTargetAccuracy >= profile.minimumTargetAccuracy;
        bool enoughInstrumentation =
            personalSlotEvents >= profile.minimumValidSlotEvents &&
            personalPhysicalSamples >= profile.minimumValidPhysicalSamples;

        profile.profileReady = profile.calibrationComplete && enoughReferenceTrials &&
                               enoughAccuracy && enoughInstrumentation;
        profile.profileQuality = profile.profileReady
            ? "ready"
            : profile.calibrationComplete ? "limited" : "insufficient";
        profile.calibrationNotes =
            "This profile is a descriptive reference for this participant's normal Answer and Confidence knob movements. " +
            "It defines personal ranges for speed, path, pauses, reversals, micro-adjustments, and corrections across movement-distance bins. " +
            "Any later pattern match remains a researcher-facing review cue linked to raw item and stage records, not a careless-response label.";

        PAXSMResponsePatternThresholds thresholds = profile.responsePatternThresholds;
        thresholds.directPathRatioMax = personalReferenceDirectPathRatioMax;
        thresholds.lowCorrectionCountMax = personalReferenceLowCorrectionCountMax;
        thresholds.answerFastDecisionRtBelowSec = -1f;
        thresholds.confidenceFastDecisionRtBelowSec = -1f;
        thresholds.answerHighMaxAbsVelocityAbove = profile.personalReference.answer.maxAbsVelocity.p90;
        thresholds.confidenceHighMaxAbsVelocityAbove = profile.personalReference.confidence.maxAbsVelocity.p90;
        thresholds.answerHighPhysicalAngularSpeedAboveDps =
            profile.personalReference.answer.physicalAngularSpeed.p90;
        thresholds.confidenceHighPhysicalAngularSpeedAboveDps =
            profile.personalReference.confidence.physicalAngularSpeed.p90;
        thresholds.answerExtraPathRatioAbove = profile.personalReference.answer.pathRatio.upperReference;
        thresholds.confidenceExtraPathRatioAbove = profile.personalReference.confidence.pathRatio.upperReference;
        thresholds.answerHighCorrectionRateAbove = profile.personalReference.answer.correctionRate.upperReference;
        thresholds.confidenceHighCorrectionRateAbove = profile.personalReference.confidence.correctionRate.upperReference;
        thresholds.answerLowCorrectionRateAtOrBelow = profile.personalReference.answer.correctionRate.p25;
        thresholds.confidenceLowCorrectionRateAtOrBelow = profile.personalReference.confidence.correctionRate.p25;
        return profile;
    }

    string BuildPersonalKnobReferenceIntegrityCsv(
        PAXSMResponseCalibrationProfile profile,
        string reason,
        string folder,
        string stamp,
        string trialsPath,
        string profileJsonPath,
        string profileCsvPath)
    {
        int expectedTrials = Mathf.Clamp(personalReferenceTrialCount, 8, 24);
        int formalRecords = 0;
        int answerCorrect = 0;
        int confidenceCorrect = 0;
        int readRows = 0;
        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            if (record == null ||
                !string.Equals(record.calibrationCondition, "personal_reference", StringComparison.OrdinalIgnoreCase))
                continue;

            formalRecords++;
            if (record.answerTargetError == 0)
                answerCorrect++;
            if (record.confidenceTargetError == 0)
                confidenceCorrect++;
            if (record.readRt >= 0f)
                readRows++;
        }

        int stageEvents = CountPersonalReferenceRows(_questionnaireStageEvents, row => row.blockId);
        int rawTraceRows = CountPersonalReferenceRows(_questionnaireRawTraceRecords, row => row.blockId);
        int interactionRows = CountPersonalReferenceRows(_questionnaireInteractionEvents, row => row.blockId);
        int physicalSpeedRows = CountPersonalReferenceRows(_questionnairePhysicalSpeedSamples, row => row.blockId);
        int slotSpeedRows = CountPersonalReferenceRows(_questionnaireSlotSpeedEvents, row => row.blockId);
        int minimumStageEvents = expectedTrials * (requireReadAcknowledgement ? 8 : 6);
        int minimumRawTraceRows = expectedTrials * 4;
        int minimumInteractionRows = expectedTrials * 8;
        bool completed = string.Equals(reason, "completed", StringComparison.OrdinalIgnoreCase);
        bool rawFilesWritten = File.Exists(trialsPath) && File.Exists(profileJsonPath) && File.Exists(profileCsvPath);
        bool enoughRawEvents = rawTraceRows >= minimumRawTraceRows && interactionRows >= minimumInteractionRows;
        bool speedObserved = physicalSpeedRows > 0 && slotSpeedRows > 0;

        var rows = new List<string>();
        bool allRequiredChecksPass = true;
        void AddCheck(string checkId, string expected, string observed, bool pass, string note)
        {
            if (!pass)
                allRequiredChecksPass = false;
            rows.Add(string.Join(",", new[]
            {
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(reason),
                Csv(checkId), "1", Csv(expected), Csv(observed),
                Csv(pass ? "PASS" : "FAIL"), Csv(note)
            }));
        }

        AddCheck("run_completion", "completed", reason, completed,
            "The personal-reference protocol must end through the normal completion path.");
        AddCheck("formal_target_entry_trials", I(expectedTrials), I(formalRecords), formalRecords == expectedTrials,
            "Two practice trials are intentionally excluded; only the formal target-entry trials count here.");
        AddCheck("answer_target_accuracy", $">={F(personalReferenceMinimumTargetAccuracy)}", F(profile.personalReference.answerTargetAccuracy),
            profile.personalReference.answerTargetAccuracy >= personalReferenceMinimumTargetAccuracy,
            "Answer target accuracy is required before using Answer-stage personal reference values.");
        AddCheck("confidence_target_accuracy", $">={F(personalReferenceMinimumTargetAccuracy)}", F(profile.personalReference.confidenceTargetAccuracy),
            profile.personalReference.confidenceTargetAccuracy >= personalReferenceMinimumTargetAccuracy,
            "Confidence target accuracy is required before using Confidence-stage personal reference values.");
        AddCheck("valid_reference_trials", $">={I(personalReferenceMinimumValidTrials)}", I(profile.personalReference.referenceTrialCount),
            profile.personalReference.referenceTrialCount >= personalReferenceMinimumValidTrials,
            "A valid reference trial requires correct Answer and Confidence target entry.");
        AddCheck("read_stage_rows", I(expectedTrials), I(readRows), readRows == expectedTrials,
            "Every formal reference trial must contain a Read-stage acknowledgement.");
        AddCheck("stage_events", $">={I(minimumStageEvents)}", I(stageEvents), stageEvents >= minimumStageEvents,
            "Stage Enter, Confirm, and Exit events must be available for Read, Answer, and Confidence.");
        AddCheck("raw_trace_anchors", $">={I(minimumRawTraceRows)}", I(rawTraceRows), rawTraceRows >= minimumRawTraceRows,
            "Raw traces retain at least the start and end anchors for Answer and Confidence on each trial.");
        AddCheck("interaction_events", $">={I(minimumInteractionRows)}", I(interactionRows), interactionRows >= minimumInteractionRows,
            "Interaction events retain grabbing, first movement, confirmation, and stage-transition evidence.");
        AddCheck("physical_speed_samples", ">0", I(physicalSpeedRows), physicalSpeedRows > 0,
            "Wrist-twist angular-speed samples are required for participant-relative speed calibration.");
        AddCheck("slot_speed_events", ">0", I(slotSpeedRows), slotSpeedRows > 0,
            "Detent/slot transition-speed events are required for participant-relative speed calibration.");
        AddCheck("profile_artifacts", "3", rawFilesWritten ? "3" : "missing file", rawFilesWritten,
            "The trial table, JSON profile, and analysis-friendly profile CSV must all be present.");
        AddCheck("profile_ready", "true", B(profile.profileReady), profile.profileReady,
            "Only a ready profile should supply participant-relative Evidence Matrix thresholds.");

        var sb = new StringBuilder();
        sb.AppendLine(
            "participantId,sessionNumber,conditionLabel,reason,checkId,required,expected,observed,status,note");
        sb.AppendLine(string.Join(",", new[]
        {
            Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(reason),
            Csv("overall"), "1", Csv("all required checks PASS"),
            Csv(allRequiredChecksPass ? "all required checks PASS" : "one or more required checks FAIL"),
            Csv(allRequiredChecksPass ? "PASS" : "FAIL"),
            Csv("Do not use a failed personal profile to set participant-relative response-process thresholds.")
        }));
        for (int i = 0; i < rows.Count; i++)
            sb.AppendLine(rows[i]);

        _lastDataIntegrityPassed = allRequiredChecksPass;
        return sb.ToString();
    }

    int CountPersonalReferenceRows<T>(IList<T> rows, Func<T, string> getBlockId)
    {
        int count = 0;
        if (rows == null || getBlockId == null)
            return count;
        for (int i = 0; i < rows.Count; i++)
        {
            T row = rows[i];
            if (row != null && string.Equals(getBlockId(row), "personal_reference", StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }

    PAXSMCalibrationConditionSummary BuildResponseCalibrationConditionSummary(
        string conditionId,
        string instructionLabel)
    {
        var allRecords = new List<QuestionnaireRecord>();
        var referenceRecords = new List<QuestionnaireRecord>();
        int answerCorrect = 0;
        int confidenceCorrect = 0;
        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            if (record == null ||
                !string.Equals(record.calibrationCondition, conditionId, StringComparison.OrdinalIgnoreCase))
                continue;

            allRecords.Add(record);
            bool answerIsCorrect = record.expectedAnswerTarget > 0 && record.answerTargetError == 0;
            bool confidenceIsCorrect = record.expectedConfidenceTarget > 0 && record.confidenceTargetError == 0;
            if (answerIsCorrect)
                answerCorrect++;
            if (confidenceIsCorrect)
                confidenceCorrect++;
            if (answerIsCorrect && confidenceIsCorrect)
                referenceRecords.Add(record);
        }

        int completed = allRecords.Count;
        return new PAXSMCalibrationConditionSummary
        {
            conditionId = conditionId,
            instructionLabel = string.IsNullOrWhiteSpace(instructionLabel) ? conditionId : instructionLabel.Trim(),
            completedTrials = completed,
            answerTargetCorrect = answerCorrect,
            confidenceTargetCorrect = confidenceCorrect,
            referenceTrialCount = referenceRecords.Count,
            answerTargetAccuracy = completed > 0 ? answerCorrect / (float)completed : 0f,
            confidenceTargetAccuracy = completed > 0 ? confidenceCorrect / (float)completed : 0f,
            readRt = PAXSMCalibrationStatistics.CreateReference(
                "read_rt", "seconds", CollectCalibrationValues(referenceRecords, "read_rt"), 0.15f),
            answer = BuildResponseCalibrationStageSummary(referenceRecords, true),
            confidence = BuildResponseCalibrationStageSummary(referenceRecords, false)
        };
    }

    PAXSMCalibrationStageSummary BuildResponseCalibrationStageSummary(
        List<QuestionnaireRecord> referenceRecords,
        bool answerStage)
    {
        string stage = answerStage ? "Answer" : "Confidence";
        var summary = new PAXSMCalibrationStageSummary
        {
            referenceTrialCount = referenceRecords.Count,
            validSlotSpeedEventCount = CountCalibrationSlotSpeedEvents(referenceRecords, stage),
            validPhysicalSpeedSampleCount = CountCalibrationPhysicalSpeedSamples(referenceRecords, stage)
        };

        summary.decisionRt = PAXSMCalibrationStatistics.CreateReference(
            "decision_rt", "seconds", CollectCalibrationValues(referenceRecords, answerStage, "decision_rt"), 0.15f);
        summary.firstInteractionRt = PAXSMCalibrationStatistics.CreateReference(
            "first_interaction_rt", "seconds", CollectCalibrationValues(referenceRecords, answerStage, "first_interaction_rt"), 0.1f);
        summary.maxAbsVelocity = PAXSMCalibrationStatistics.CreateReference(
            "max_abs_velocity", "degrees_per_second", CollectCalibrationValues(referenceRecords, answerStage, "max_abs_velocity"), 5f);
        summary.maxFlickVelocity = PAXSMCalibrationStatistics.CreateReference(
            "max_flick_velocity", "degrees_per_second", CollectCalibrationValues(referenceRecords, answerStage, "max_flick_velocity"), 5f);
        summary.physicalAngularSpeed = PAXSMCalibrationStatistics.CreateReference(
            "physical_angular_speed", "degrees_per_second", CollectCalibrationPhysicalSpeedValues(referenceRecords, stage), 5f);
        summary.totalAbsAngle = PAXSMCalibrationStatistics.CreateReference(
            "total_abs_angle", "degrees", CollectCalibrationValues(referenceRecords, answerStage, "total_abs_angle"), 5f);
        summary.pathRatio = PAXSMCalibrationStatistics.CreateReference(
            "path_ratio", "ratio", CollectCalibrationValues(referenceRecords, answerStage, "path_ratio"), 0.05f);
        summary.slotChangeCount = PAXSMCalibrationStatistics.CreateReference(
            "slot_change_count", "count", CollectCalibrationValues(referenceRecords, answerStage, "slot_change_count"), 0.25f);
        summary.pauseCount = PAXSMCalibrationStatistics.CreateReference(
            "pause_count", "count", CollectCalibrationValues(referenceRecords, answerStage, "pause_count"), 0.25f);
        summary.pauseRate = PAXSMCalibrationStatistics.CreateReference(
            "pause_rate", "count_per_second", CollectCalibrationValues(referenceRecords, answerStage, "pause_rate"), 0.05f);
        summary.reverseCount = PAXSMCalibrationStatistics.CreateReference(
            "reverse_count", "count", CollectCalibrationValues(referenceRecords, answerStage, "reverse_count"), 0.25f);
        summary.microAdjustCount = PAXSMCalibrationStatistics.CreateReference(
            "micro_adjust_count", "count", CollectCalibrationValues(referenceRecords, answerStage, "micro_adjust_count"), 0.25f);
        summary.correctionRate = PAXSMCalibrationStatistics.CreateReference(
            "correction_rate", "corrections_per_slot_change", CollectCalibrationValues(referenceRecords, answerStage, "correction_rate"), 0.05f);
        summary.fastFlickCount = PAXSMCalibrationStatistics.CreateReference(
            "fast_flick_count", "count", CollectCalibrationValues(referenceRecords, answerStage, "fast_flick_count"), 0.25f);
        summary.grabCount = PAXSMCalibrationStatistics.CreateReference(
            "grab_count", "count", CollectCalibrationValues(referenceRecords, answerStage, "grab_count"), 0.25f);
        summary.confirmCancelCount = PAXSMCalibrationStatistics.CreateReference(
            "confirm_cancel_count", "count", CollectCalibrationValues(referenceRecords, answerStage, "confirm_cancel_count"), 0.25f);
        summary.distanceBins = BuildPersonalReferenceDistanceBins(referenceRecords, answerStage);
        return summary;
    }

    List<PAXSMCalibrationDistanceBinSummary> BuildPersonalReferenceDistanceBins(
        List<QuestionnaireRecord> referenceRecords,
        bool answerStage)
    {
        var summaries = new List<PAXSMCalibrationDistanceBinSummary>();
        int scale = answerStage
            ? Mathf.Max(2, referenceRecords.Count > 0 ? referenceRecords[0].scale : questionnaireScale)
            : Mathf.Max(2, questionnaireConfidenceScale);
        string[] bins = { "short", "medium", "long" };
        for (int i = 0; i < bins.Length; i++)
        {
            GetPersonalReferenceDistanceBinBounds(scale, bins[i], out int minimum, out int maximum);
            if (minimum > maximum)
                continue;

            var binRecords = new List<QuestionnaireRecord>();
            for (int recordIndex = 0; recordIndex < referenceRecords.Count; recordIndex++)
            {
                QuestionnaireRecord record = referenceRecords[recordIndex];
                string recordBin = answerStage ? record.answerTargetDistanceBin : record.confidenceTargetDistanceBin;
                if (string.Equals(recordBin, bins[i], StringComparison.OrdinalIgnoreCase))
                    binRecords.Add(record);
            }
            if (binRecords.Count == 0)
                continue;

            summaries.Add(new PAXSMCalibrationDistanceBinSummary
            {
                binId = bins[i],
                displayName = $"{bins[i]} target movement",
                minimumSlotDistance = minimum,
                maximumSlotDistance = maximum,
                referenceTrialCount = binRecords.Count,
                decisionRt = PAXSMCalibrationStatistics.CreateReference(
                    "decision_rt", "seconds", CollectCalibrationValues(binRecords, answerStage, "decision_rt"), 0.15f),
                firstInteractionRt = PAXSMCalibrationStatistics.CreateReference(
                    "first_interaction_rt", "seconds", CollectCalibrationValues(binRecords, answerStage, "first_interaction_rt"), 0.1f),
                maxAbsVelocity = PAXSMCalibrationStatistics.CreateReference(
                    "max_abs_velocity", "degrees_per_second", CollectCalibrationValues(binRecords, answerStage, "max_abs_velocity"), 5f),
                maxFlickVelocity = PAXSMCalibrationStatistics.CreateReference(
                    "max_flick_velocity", "degrees_per_second", CollectCalibrationValues(binRecords, answerStage, "max_flick_velocity"), 5f),
                totalAbsAngle = PAXSMCalibrationStatistics.CreateReference(
                    "total_abs_angle", "degrees", CollectCalibrationValues(binRecords, answerStage, "total_abs_angle"), 5f),
                pathRatio = PAXSMCalibrationStatistics.CreateReference(
                    "path_ratio", "ratio", CollectCalibrationValues(binRecords, answerStage, "path_ratio"), 0.05f),
                pauseRate = PAXSMCalibrationStatistics.CreateReference(
                    "pause_rate", "count_per_second", CollectCalibrationValues(binRecords, answerStage, "pause_rate"), 0.05f),
                reverseCount = PAXSMCalibrationStatistics.CreateReference(
                    "reverse_count", "count", CollectCalibrationValues(binRecords, answerStage, "reverse_count"), 0.25f),
                microAdjustCount = PAXSMCalibrationStatistics.CreateReference(
                    "micro_adjust_count", "count", CollectCalibrationValues(binRecords, answerStage, "micro_adjust_count"), 0.25f),
                correctionRate = PAXSMCalibrationStatistics.CreateReference(
                    "correction_rate", "corrections_per_slot_change", CollectCalibrationValues(binRecords, answerStage, "correction_rate"), 0.05f),
                fastFlickCount = PAXSMCalibrationStatistics.CreateReference(
                    "fast_flick_count", "count", CollectCalibrationValues(binRecords, answerStage, "fast_flick_count"), 0.25f)
            });
        }
        return summaries;
    }

    static void GetPersonalReferenceDistanceBinBounds(
        int scale,
        string bin,
        out int minimum,
        out int maximum)
    {
        minimum = 1;
        maximum = 0;
        if (scale <= 5)
        {
            if (string.Equals(bin, "short", StringComparison.OrdinalIgnoreCase))
            {
                minimum = 1;
                maximum = 1;
            }
            else if (string.Equals(bin, "long", StringComparison.OrdinalIgnoreCase))
            {
                minimum = 2;
                maximum = Mathf.Max(1, Mathf.Min(
                    Mathf.CeilToInt(Mathf.Max(2, scale) * 0.5f) - 1,
                    Mathf.Max(2, scale) - Mathf.CeilToInt(Mathf.Max(2, scale) * 0.5f)));
            }
            return;
        }

        int centeredSlot = Mathf.CeilToInt(Mathf.Max(2, scale) * 0.5f);
        int maxDistance = Mathf.Max(1, Mathf.Min(centeredSlot - 1, Mathf.Max(2, scale) - centeredSlot));
        int shortMaximum = Mathf.Max(1, Mathf.RoundToInt(maxDistance * 0.2f));
        int mediumMaximum = Mathf.Max(shortMaximum + 1, Mathf.RoundToInt(maxDistance * 0.6f));
        if (string.Equals(bin, "short", StringComparison.OrdinalIgnoreCase))
        {
            minimum = 1;
            maximum = shortMaximum;
        }
        else if (string.Equals(bin, "medium", StringComparison.OrdinalIgnoreCase))
        {
            minimum = shortMaximum + 1;
            maximum = Mathf.Min(maxDistance, mediumMaximum);
        }
        else if (string.Equals(bin, "long", StringComparison.OrdinalIgnoreCase))
        {
            minimum = mediumMaximum + 1;
            maximum = maxDistance;
        }
    }

    List<float> CollectCalibrationValues(List<QuestionnaireRecord> records, string metric)
    {
        var values = new List<float>();
        for (int i = 0; i < records.Count; i++)
        {
            if (string.Equals(metric, "read_rt", StringComparison.OrdinalIgnoreCase))
                values.Add(records[i].readRt);
        }
        return values;
    }

    List<float> CollectCalibrationValues(List<QuestionnaireRecord> records, bool answerStage, string metric)
    {
        var values = new List<float>();
        for (int i = 0; i < records.Count; i++)
        {
            QuestionnaireRecord record = records[i];
            float decisionRt = answerStage ? record.answerDecisionRt : record.confidenceDecisionRt;
            float firstInteractionRt = answerStage ? record.answerFirstInteractionRt : record.confidenceFirstInteractionRt;
            float maxVelocity = answerStage ? record.answerMaxAbsVel : record.confidenceMaxAbsVel;
            float maxFlickVelocity = answerStage
                ? (record.answerMetrics?.maxFlickVel ?? -1f)
                : (record.confidenceMetrics?.maxFlickVel ?? -1f);
            float totalAbsAngle = answerStage ? record.answerTotalAbsAngle : record.confidenceTotalAbsAngle;
            float pathRatio = answerStage ? record.answerPathRatio : record.confidencePathRatio;
            int slotChanges = answerStage ? record.answerSlotChangeCount : record.confidenceSlotChangeCount;
            int pauses = answerStage ? record.answerPauseCount : record.confidencePauseCount;
            int reverses = answerStage ? record.answerReverseCount : record.confidenceReverseCount;
            int microAdjustments = answerStage ? record.answerMicroAdjustCount : record.confidenceMicroAdjustCount;
            int fastFlicks = answerStage ? record.answerFastFlickCount : record.confidenceFastFlickCount;
            int grabs = answerStage ? record.answerGrabCount : record.confidenceGrabCount;
            int confirmCancels = answerStage ? record.answerConfirmCancelCount : record.confidenceConfirmCancelCount;
            float value = -1f;

            if (string.Equals(metric, "decision_rt", StringComparison.OrdinalIgnoreCase)) value = decisionRt;
            else if (string.Equals(metric, "first_interaction_rt", StringComparison.OrdinalIgnoreCase)) value = firstInteractionRt;
            else if (string.Equals(metric, "max_abs_velocity", StringComparison.OrdinalIgnoreCase)) value = maxVelocity;
            else if (string.Equals(metric, "max_flick_velocity", StringComparison.OrdinalIgnoreCase)) value = maxFlickVelocity;
            else if (string.Equals(metric, "total_abs_angle", StringComparison.OrdinalIgnoreCase)) value = totalAbsAngle;
            else if (string.Equals(metric, "path_ratio", StringComparison.OrdinalIgnoreCase)) value = pathRatio;
            else if (string.Equals(metric, "slot_change_count", StringComparison.OrdinalIgnoreCase)) value = slotChanges;
            else if (string.Equals(metric, "pause_count", StringComparison.OrdinalIgnoreCase)) value = pauses;
            else if (string.Equals(metric, "pause_rate", StringComparison.OrdinalIgnoreCase))
                value = PAXSMCalibrationStatistics.Rate(pauses, decisionRt);
            else if (string.Equals(metric, "reverse_count", StringComparison.OrdinalIgnoreCase)) value = reverses;
            else if (string.Equals(metric, "micro_adjust_count", StringComparison.OrdinalIgnoreCase)) value = microAdjustments;
            else if (string.Equals(metric, "correction_rate", StringComparison.OrdinalIgnoreCase))
                value = (reverses + microAdjustments) / Mathf.Max(1f, slotChanges);
            else if (string.Equals(metric, "fast_flick_count", StringComparison.OrdinalIgnoreCase)) value = fastFlicks;
            else if (string.Equals(metric, "grab_count", StringComparison.OrdinalIgnoreCase)) value = grabs;
            else if (string.Equals(metric, "confirm_cancel_count", StringComparison.OrdinalIgnoreCase)) value = confirmCancels;
            values.Add(value);
        }
        return values;
    }

    List<float> CollectCalibrationPhysicalSpeedValues(List<QuestionnaireRecord> records, string stage)
    {
        var values = new List<float>();
        for (int i = 0; i < _questionnairePhysicalSpeedSamples.Count; i++)
        {
            QuestionnairePhysicalSpeedSample sample = _questionnairePhysicalSpeedSamples[i];
            if (!sample.validForCalibration || sample.physicalAngularSpeedDps < 0f)
                continue;
            if (CalibrationSpeedSampleMatchesAnyRecord(sample, records, stage))
                values.Add(sample.physicalAngularSpeedDps);
        }
        return values;
    }

    int CountCalibrationPhysicalSpeedSamples(List<QuestionnaireRecord> records, string stage)
    {
        int count = 0;
        for (int i = 0; i < _questionnairePhysicalSpeedSamples.Count; i++)
        {
            QuestionnairePhysicalSpeedSample sample = _questionnairePhysicalSpeedSamples[i];
            if (sample.validForCalibration && CalibrationSpeedSampleMatchesAnyRecord(sample, records, stage))
                count++;
        }
        return count;
    }

    int CountCalibrationSlotSpeedEvents(List<QuestionnaireRecord> records, string stage)
    {
        int count = 0;
        for (int i = 0; i < _questionnaireSlotSpeedEvents.Count; i++)
        {
            QuestionnaireSlotSpeedEvent speedEvent = _questionnaireSlotSpeedEvents[i];
            if (!speedEvent.validForCalibration || speedEvent.slotsPerSecond < 0f)
                continue;
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                QuestionnaireRecord record = records[recordIndex];
                if (CalibrationRecordMatches(
                        record,
                        speedEvent.blockId,
                        speedEvent.presentationOrder,
                        speedEvent.itemIndex,
                        speedEvent.itemId,
                        speedEvent.stage,
                        stage))
                {
                    count++;
                    break;
                }
            }
        }
        return count;
    }

    bool CalibrationSpeedSampleMatchesAnyRecord(
        QuestionnairePhysicalSpeedSample sample,
        List<QuestionnaireRecord> records,
        string stage)
    {
        for (int i = 0; i < records.Count; i++)
        {
            if (CalibrationRecordMatches(
                    records[i], sample.blockId, sample.presentationOrder, sample.itemIndex,
                    sample.itemId, sample.stage, stage))
                return true;
        }
        return false;
    }

    bool CalibrationRecordMatches(
        QuestionnaireRecord record,
        string blockId,
        int presentationOrder,
        int itemIndex,
        string itemId,
        string recordStage,
        string requestedStage)
    {
        return record != null &&
               string.Equals(record.blockId, blockId, StringComparison.OrdinalIgnoreCase) &&
               record.presentationOrder == presentationOrder &&
               record.itemIndex == itemIndex &&
               string.Equals(record.itemId, itemId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(recordStage, requestedStage, StringComparison.OrdinalIgnoreCase);
    }

    string BuildResponseCalibrationTrialsCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "participantId,sessionNumber,conditionLabel,calibrationCondition,presentationOrder,itemIndex,itemId," +
            "expectedAnswerTarget,selectedAnswerTarget,answerTargetError,answerInitialSlot,answerTargetDistanceSlots," +
            "answerTargetDistanceBin,answerShortestRequiredAngle,answerPathRatio,answerRt,answerDecisionRt," +
            "answerFirstInteractionRt,answerTotalAbsAngle,answerMaxAbsVel,answerMaxFlickVel,answerSlotChangeCount," +
            "answerPauseCount,answerPauseRate,answerReverseCount,answerMicroAdjustCount,answerCorrectionRate," +
            "answerFastFlickCount,answerGrabCount,answerConfirmCancelCount," +
            "expectedConfidenceTarget,selectedConfidenceTarget,confidenceTargetError,confidenceInitialSlot," +
            "confidenceTargetDistanceSlots,confidenceTargetDistanceBin,confidenceShortestRequiredAngle,confidencePathRatio," +
            "confidenceRt,confidenceDecisionRt,confidenceFirstInteractionRt,confidenceTotalAbsAngle,confidenceMaxAbsVel," +
            "confidenceMaxFlickVel," +
            "confidenceSlotChangeCount,confidencePauseCount,confidencePauseRate,confidenceReverseCount," +
            "confidenceMicroAdjustCount,confidenceCorrectionRate,confidenceFastFlickCount,confidenceGrabCount," +
            "confidenceConfirmCancelCount,readRt");

        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            if (record == null || string.IsNullOrWhiteSpace(record.calibrationCondition))
                continue;
            float answerPauseRate = PAXSMCalibrationStatistics.Rate(record.answerPauseCount, record.answerDecisionRt);
            float confidencePauseRate = PAXSMCalibrationStatistics.Rate(record.confidencePauseCount, record.confidenceDecisionRt);
            float answerCorrectionRate = (record.answerReverseCount + record.answerMicroAdjustCount) /
                                       Mathf.Max(1f, record.answerSlotChangeCount);
            float confidenceCorrectionRate = (record.confidenceReverseCount + record.confidenceMicroAdjustCount) /
                                           Mathf.Max(1f, record.confidenceSlotChangeCount);
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(record.calibrationCondition),
                I(record.presentationOrder), I(record.itemIndex), Csv(record.itemId),
                I(record.expectedAnswerTarget), I(record.selectedScore), I(record.answerTargetError),
                I(record.answerInitialSlot), I(record.answerTargetDistanceSlots), Csv(record.answerTargetDistanceBin),
                F(record.answerShortestRequiredAngle), F(record.answerPathRatio), F(record.answerRt), F(record.answerDecisionRt),
                F(record.answerFirstInteractionRt), F(record.answerTotalAbsAngle), F(record.answerMaxAbsVel),
                F(record.answerMetrics?.maxFlickVel ?? -1f),
                I(record.answerSlotChangeCount), I(record.answerPauseCount), F(answerPauseRate),
                I(record.answerReverseCount), I(record.answerMicroAdjustCount), F(answerCorrectionRate),
                I(record.answerFastFlickCount), I(record.answerGrabCount), I(record.answerConfirmCancelCount),
                I(record.expectedConfidenceTarget), I(record.confidence), I(record.confidenceTargetError),
                I(record.confidenceInitialSlot), I(record.confidenceTargetDistanceSlots), Csv(record.confidenceTargetDistanceBin),
                F(record.confidenceShortestRequiredAngle), F(record.confidencePathRatio), F(record.confidenceRt),
                F(record.confidenceDecisionRt),
                F(record.confidenceFirstInteractionRt), F(record.confidenceTotalAbsAngle),
                F(record.confidenceMaxAbsVel), F(record.confidenceMetrics?.maxFlickVel ?? -1f),
                I(record.confidenceSlotChangeCount),
                I(record.confidencePauseCount), F(confidencePauseRate), I(record.confidenceReverseCount),
                I(record.confidenceMicroAdjustCount), F(confidenceCorrectionRate),
                I(record.confidenceFastFlickCount), I(record.confidenceGrabCount),
                I(record.confidenceConfirmCancelCount), F(record.readRt)
            }));
        }
        return sb.ToString();
    }

    string BuildResponseCalibrationProfileCsv(PAXSMResponseCalibrationProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "participantId,sessionNumber,profileQuality,profileReady,calibrationComplete,condition,stage,distanceBin," +
            "minimumSlotDistance,maximumSlotDistance,metric,units,sampleCount,median,mad,robustSigma,p10,p25,p90,p95," +
            "lowerReference,upperReference");
        AppendResponseCalibrationProfileRow(sb, profile, "personal_reference", "Read", profile.personalReference.readRt);
        AppendResponseCalibrationStageRows(sb, profile, "personal_reference", "Answer", profile.personalReference.answer);
        AppendResponseCalibrationStageRows(sb, profile, "personal_reference", "Confidence", profile.personalReference.confidence);
        return sb.ToString();
    }

    void AppendResponseCalibrationStageRows(
        StringBuilder sb,
        PAXSMResponseCalibrationProfile profile,
        string condition,
        string stage,
        PAXSMCalibrationStageSummary summary)
    {
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.decisionRt);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.firstInteractionRt);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.maxAbsVelocity);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.maxFlickVelocity);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.physicalAngularSpeed);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.totalAbsAngle);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.pathRatio);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.slotChangeCount);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.pauseCount);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.pauseRate);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.reverseCount);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.microAdjustCount);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.correctionRate);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.fastFlickCount);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.grabCount);
        AppendResponseCalibrationProfileRow(sb, profile, condition, stage, summary.confirmCancelCount);
        if (summary.distanceBins == null)
            return;

        for (int i = 0; i < summary.distanceBins.Count; i++)
        {
            PAXSMCalibrationDistanceBinSummary bin = summary.distanceBins[i];
            AppendResponseCalibrationProfileRow(sb, profile, condition, stage, bin.maxAbsVelocity, bin);
            AppendResponseCalibrationProfileRow(sb, profile, condition, stage, bin.maxFlickVelocity, bin);
            AppendResponseCalibrationProfileRow(sb, profile, condition, stage, bin.totalAbsAngle, bin);
            AppendResponseCalibrationProfileRow(sb, profile, condition, stage, bin.pathRatio, bin);
            AppendResponseCalibrationProfileRow(sb, profile, condition, stage, bin.pauseRate, bin);
            AppendResponseCalibrationProfileRow(sb, profile, condition, stage, bin.reverseCount, bin);
            AppendResponseCalibrationProfileRow(sb, profile, condition, stage, bin.microAdjustCount, bin);
            AppendResponseCalibrationProfileRow(sb, profile, condition, stage, bin.correctionRate, bin);
            AppendResponseCalibrationProfileRow(sb, profile, condition, stage, bin.fastFlickCount, bin);
        }
    }

    void AppendResponseCalibrationProfileRow(
        StringBuilder sb,
        PAXSMResponseCalibrationProfile profile,
        string condition,
        string stage,
        PAXSMCalibrationMetricReference reference,
        PAXSMCalibrationDistanceBinSummary distanceBin = null)
    {
        if (reference == null)
            return;
        sb.AppendLine(string.Join(",", new[]
        {
            Csv(profile.participantId), I(profile.sessionNumber), Csv(profile.profileQuality), B(profile.profileReady),
            B(profile.calibrationComplete), Csv(condition), Csv(stage), Csv(distanceBin?.binId ?? "global"),
            I(distanceBin?.minimumSlotDistance ?? -1), I(distanceBin?.maximumSlotDistance ?? -1),
            Csv(reference.metric), Csv(reference.units),
            I(reference.sampleCount), F(reference.median), F(reference.mad), F(reference.robustSigma),
            F(reference.p10), F(reference.p25), F(reference.p90), F(reference.p95),
            F(reference.lowerReference), F(reference.upperReference)
        }));
    }

    string BuildWorkloadProbeDataIntegrityCsv(string reason, string folder, string stamp)
    {
        int expectedBlockCount = _runBlockCount > 0 ? _runBlockCount : blockProfiles.Count;
        int expectedTrialCount = 0;
        for (int i = 0; i < blockProfiles.Count; i++)
            expectedTrialCount += Mathf.Max(1, blockProfiles[i].trialsPerBlock);

        var observedBlockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _trialRecords.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(_trialRecords[i].blockId))
                observedBlockIds.Add(_trialRecords[i].blockId);
        }

        int questionnaireItemsPerBlock = ExpectedQuestionnaireItemCount();
        int expectedQuestionnaireRows = collectQuestionnaireBetweenBlocks
            ? expectedBlockCount * questionnaireItemsPerBlock
            : 0;
        int stageEventsPerItem = 3 + (collectConfidenceAfterEachItem ? 3 : 0) +
                                 (requireReadAcknowledgement ? 2 : 0);
        int expectedStageEventRows = expectedQuestionnaireRows * stageEventsPerItem;
        int minimumRawTraceRows = expectedQuestionnaireRows *
                                  (collectConfidenceAfterEachItem ? 4 : 2);
        int minimumInteractionRows = expectedQuestionnaireRows *
                                     (collectConfidenceAfterEachItem ? 8 : 4);
        int observedMergedRows = _mainSceneAnswerExportMirror?.summaries.Count ?? 0;
        int behaviorMetricFiles = _probeBehaviorCollector?.MetricFileCountWritten ?? 0;
        int behaviorRawFiles = _probeBehaviorCollector?.RawFileCountWritten ?? 0;
        string outputSuffix = $"{participantId}_{stamp}_{reason}";
        var expectedPrimaryPaths = new List<string>
        {
            Path.Combine(folder, $"WorkloadProbe_Trials_{outputSuffix}.csv"),
            Path.Combine(folder, $"WorkloadProbe_Blocks_{outputSuffix}.csv"),
            Path.Combine(folder, $"WorkloadProbe_RunOrder_{outputSuffix}.csv"),
            Path.Combine(folder, $"CAREXR_Questionnaire_{outputSuffix}.csv"),
            Path.Combine(folder, $"CAREXR_Questionnaire_StageEvents_{outputSuffix}.csv")
        };
        if (requireParticipantConfirmationBetweenBlocks)
            expectedPrimaryPaths.Add(
                Path.Combine(folder, $"WorkloadProbe_InterBlockConfirmations_{outputSuffix}.csv"));
        if (_questionnaireRecords.Count > 0)
        {
            expectedPrimaryPaths.Add(Path.Combine(folder, $"CAREXR_Questionnaire_RawTrace_{outputSuffix}.csv"));
            expectedPrimaryPaths.Add(Path.Combine(folder, $"CAREXR_Questionnaire_InteractionEvents_{outputSuffix}.csv"));
            expectedPrimaryPaths.Add(Path.Combine(folder, $"CAREXR_Questionnaire_Metadata_{outputSuffix}.csv"));
            expectedPrimaryPaths.Add(Path.Combine(folder, $"CAREXR_Questionnaire_PhysicalSpeedSamples_{outputSuffix}.csv"));
            expectedPrimaryPaths.Add(Path.Combine(folder, $"CAREXR_Questionnaire_SlotSpeedEvents_{outputSuffix}.csv"));
            expectedPrimaryPaths.Add(Path.Combine(folder, $"CAREXR_Questionnaire_SpeedSummary_{outputSuffix}.csv"));
        }
        int observedPrimaryFiles = 0;
        for (int i = 0; i < expectedPrimaryPaths.Count; i++)
            if (File.Exists(expectedPrimaryPaths[i])) observedPrimaryFiles++;
        int mergedFileCount = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "KnobBehavior_Merged_*.csv", SearchOption.AllDirectories).Length
            : 0;
        bool legacyMergedFileRequired = collectQuestionnaireBetweenBlocks &&
                                        !ExperimentRunContext.IsConfigured;

        var rows = new List<string>();
        bool allRequiredChecksPass = true;

        void AddCheck(
            string checkId,
            string expected,
            string observed,
            bool pass,
            bool required,
            string note)
        {
            string status = pass ? "PASS" : required ? "FAIL" : "WARN";
            if (required && !pass)
                allRequiredChecksPass = false;
            rows.Add(string.Join(",", new[]
            {
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(reason),
                Csv(checkId), B(required), Csv(expected), Csv(observed), Csv(status), Csv(note)
            }));
        }

        bool completed = string.Equals(reason, "completed", StringComparison.OrdinalIgnoreCase);
        AddCheck("run_completion", "completed", reason, completed, true,
            "A formal-study run should end through the normal completion path.");
        AddCheck("primary_csv_files", I(expectedPrimaryPaths.Count), I(observedPrimaryFiles),
            observedPrimaryFiles == expectedPrimaryPaths.Count, true,
            "Trial, block, questionnaire, event, trace, metadata, and speed files must all exist on disk.");
        AddCheck("behavior_collector_present", "1", _probeBehaviorCollector != null ? "1" : "0",
            _probeBehaviorCollector != null, true,
            "The scene-level collector supplies the 29 metrics and 30 Hz task traces.");
        AddCheck("behavior_metric_files", I(expectedBlockCount), I(behaviorMetricFiles),
            behaviorMetricFiles == expectedBlockCount, true,
            "One 29-row Metrics.csv is required for every task block.");
        AddCheck("behavior_raw_files", I(expectedBlockCount), I(behaviorRawFiles),
            behaviorRawFiles == expectedBlockCount, true,
            "One 30 Hz RawSamples.csv is required for every task block.");
        AddCheck("block_rows", I(expectedBlockCount), I(observedBlockIds.Count),
            observedBlockIds.Count == expectedBlockCount, true,
            "Distinct block IDs observed in the trial records.");
        AddCheck("trial_rows", I(expectedTrialCount), I(_trialRecords.Count),
            _trialRecords.Count == expectedTrialCount, true,
            "Expected count is the sum of trialsPerBlock in the configured profiles.");

        int expectedConfirmationRows = requireParticipantConfirmationBetweenBlocks
            ? Mathf.Max(0, expectedBlockCount - 1)
            : 0;
        bool confirmationOrderAligned = _interBlockConfirmationRecords.Count == expectedConfirmationRows;
        for (int i = 0; i < _interBlockConfirmationRecords.Count && confirmationOrderAligned; i++)
        {
            InterBlockConfirmationRecord record = _interBlockConfirmationRecords[i];
            confirmationOrderAligned = record.fromPresentationOrder == i + 1 &&
                                       record.toPresentationOrder == i + 2 &&
                                       record.confirmedRealtime >= record.shownRealtime &&
                                       !string.IsNullOrWhiteSpace(record.inputSource);
            if (confirmationOrderAligned && i + 1 < _runOrderUsed.Count)
            {
                confirmationOrderAligned =
                    string.Equals(record.fromBlockId, _runOrderUsed[i].blockId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.toBlockId, _runOrderUsed[i + 1].blockId, StringComparison.OrdinalIgnoreCase);
            }
        }
        AddCheck("inter_block_confirmations", I(expectedConfirmationRows),
            I(_interBlockConfirmationRecords.Count), confirmationOrderAligned,
            requireParticipantConfirmationBetweenBlocks,
            "Each transition must be released by an explicit fresh participant confirmation and retain its input source.");

        var trialOrderByBlock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool trialOrderConsistent = true;
        for (int i = 0; i < _trialRecords.Count; i++)
        {
            TrialRecord trial = _trialRecords[i];
            if (trialOrderByBlock.TryGetValue(trial.blockId, out int existingOrder) &&
                existingOrder != trial.presentationOrder)
                trialOrderConsistent = false;
            else
                trialOrderByBlock[trial.blockId] = trial.presentationOrder;
        }

        bool runOrderAligned = _runOrderUsed.Count == expectedBlockCount;
        for (int i = 0; i < _runOrderUsed.Count && runOrderAligned; i++)
        {
            ProbeBlockProfile profile = _runOrderUsed[i];
            runOrderAligned = profile != null &&
                              trialOrderByBlock.TryGetValue(profile.blockId, out int observedOrder) &&
                              observedOrder == i + 1;
        }
        AddCheck("run_order_alignment", $"{expectedBlockCount} block/order mappings",
            I(trialOrderByBlock.Count), trialOrderConsistent && runOrderAligned, true,
            "The explicit run-order manifest must match the block IDs and presentation orders in trial rows.");

        bool questionnaireOrderAligned = true;
        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            if (!trialOrderByBlock.TryGetValue(record.blockId, out int trialOrder) ||
                trialOrder != record.presentationOrder)
            {
                questionnaireOrderAligned = false;
                break;
            }
        }
        AddCheck("questionnaire_block_order_alignment", "all questionnaire rows match trial block/order",
            questionnaireOrderAligned ? "all matched" : "mismatch detected",
            questionnaireOrderAligned, collectQuestionnaireBetweenBlocks,
            "Randomized cognitive, physical, and temporal blocks must retain the same blockId and presentationOrder in questionnaire data.");

        AddCheck("questionnaire_rows", I(expectedQuestionnaireRows), I(_questionnaireRecords.Count),
            _questionnaireRecords.Count == expectedQuestionnaireRows, collectQuestionnaireBetweenBlocks,
            "Each completed block should contain every configured questionnaire item.");
        AddCheck("questionnaire_stage_event_rows", I(expectedStageEventRows), I(_questionnaireStageEvents.Count),
            _questionnaireStageEvents.Count == expectedStageEventRows, collectQuestionnaireBetweenBlocks,
            "Expected events include Enter, Confirm, and Exit for Answer and Confidence, plus Read when enabled.");
        AddCheck("questionnaire_raw_trace_rows", $">={minimumRawTraceRows}", I(_questionnaireRawTraceRecords.Count),
            _questionnaireRawTraceRecords.Count >= minimumRawTraceRows, collectQuestionnaireBetweenBlocks,
            "Minimum requires stage-enter and stage-exit anchors for every recorded knob stage.");
        AddCheck("questionnaire_interaction_event_rows", $">={minimumInteractionRows}", I(_questionnaireInteractionEvents.Count),
            _questionnaireInteractionEvents.Count >= minimumInteractionRows, collectQuestionnaireBetweenBlocks,
            "Interaction events preserve first interaction, grabs, confirmation, and stage transitions.");

        bool speedRequired = collectQuestionnaireBetweenBlocks &&
                             recordQuestionnairePersonalSpeed &&
                             IsQuestionnaireKnobInputActive;
        AddCheck("physical_speed_samples", ">0", I(_questionnairePhysicalSpeedSamples.Count),
            _questionnairePhysicalSpeedSamples.Count > 0, speedRequired,
            "Controller wrist-twist samples used for participant-specific speed calibration.");
        AddCheck("slot_speed_events", ">0", I(_questionnaireSlotSpeedEvents.Count),
            _questionnaireSlotSpeedEvents.Count > 0, speedRequired,
            "Event-level slot transitions used for detent and slots-per-second summaries.");
        AddCheck("main_scene_compatible_rows", I(expectedQuestionnaireRows), I(observedMergedRows),
            observedMergedRows == expectedQuestionnaireRows, collectQuestionnaireBetweenBlocks,
            "Compatibility rows support the existing KnobBehavior_Merged analysis pipeline.");
        AddCheck("main_scene_compatible_file",
            legacyMergedFileRequired ? ">=1" : "not required for Console-managed CARE-XR runs",
            I(mergedFileCount),
            !legacyMergedFileRequired || mergedFileCount > 0,
            legacyMergedFileRequired,
            legacyMergedFileRequired
                ? "The standalone legacy pipeline writes a merged compatibility CSV."
                : "The unified CAREXR_Questionnaire export already contains the compatibility rows; no duplicate legacy file is written.");

        if (requireReadAcknowledgement)
        {
            int readRows = 0;
            for (int i = 0; i < _questionnaireRecords.Count; i++)
                if (_questionnaireRecords[i].readRt >= 0f) readRows++;
            AddCheck("read_stage_rows", I(expectedQuestionnaireRows), I(readRows),
                readRows == expectedQuestionnaireRows, true,
                "Read-stage RT is required by the current scene configuration.");
        }
        else
        {
            AddCheck("read_stage_rows", "not configured", "0", true, false,
                "This Probe protocol currently records Answer and Confidence, not a separate Read acknowledgement.");
        }

        var sb = new StringBuilder();
        sb.AppendLine(
            "participantId,sessionNumber,conditionLabel,reason,checkId,required,expected,observed,status,note");
        sb.AppendLine(string.Join(",", new[]
        {
            Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(reason),
            Csv("overall"), "1", Csv("all required checks PASS"),
            Csv(allRequiredChecksPass ? "all required checks PASS" : "one or more required checks FAIL"),
            Csv(allRequiredChecksPass ? "PASS" : "FAIL"),
            Csv("Do not treat a run as analysis-ready when this row is FAIL.")
        }));
        for (int i = 0; i < rows.Count; i++)
            sb.AppendLine(rows[i]);
        _lastDataIntegrityPassed = allRequiredChecksPass;
        return sb.ToString();
    }

    int ExpectedQuestionnaireItemCount()
    {
        TextAsset bankAsset = Resources.Load<TextAsset>(questionnaireBankResourcesPath);
        if (bankAsset != null)
        {
            try
            {
                LikertSurveyConfig bank = JsonUtility.FromJson<LikertSurveyConfig>(bankAsset.text);
                if (bank?.items != null)
                {
                    int validCount = 0;
                    for (int i = 0; i < bank.items.Count; i++)
                        if (bank.items[i] != null && !string.IsNullOrWhiteSpace(bank.items[i].stem))
                            validCount++;
                    if (validCount > 0)
                        return validCount;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[XRWorkloadProbe] Could not count questionnaire items for integrity validation: {exception.Message}",
                    this);
            }
        }
        return 6;
    }

    void DeleteQuestionnaireLiveCheckpoints(string folder)
    {
        string suffix = $"{participantId}_LIVE.csv";
        string[] filePrefixes =
        {
            "CAREXR_Questionnaire_",
            "CAREXR_Questionnaire_StageEvents_",
            "CAREXR_Questionnaire_RawTrace_",
            "CAREXR_Questionnaire_InteractionEvents_",
            "CAREXR_Questionnaire_Metadata_",
            "CAREXR_Questionnaire_PhysicalSpeedSamples_",
            "CAREXR_Questionnaire_SlotSpeedEvents_",
            "CAREXR_Questionnaire_SpeedSummary_"
        };

        for (int i = 0; i < filePrefixes.Length; i++)
        {
            string path = Path.Combine(folder, filePrefixes[i] + suffix);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[XRWorkloadProbe] Could not remove completed LIVE checkpoint {path}: {exception.Message}", this);
            }
        }
    }

    void WriteQuestionnaireLiveCheckpoint()
    {
        if (_questionnaireRecords.Count == 0)
            return;

        try
        {
            string folder = GetOutputFolder();
            Directory.CreateDirectory(folder);
            string suffix = $"{participantId}_LIVE";
            WriteCheckpointFile(
                Path.Combine(folder, $"CAREXR_Questionnaire_{suffix}.csv"),
                BuildQuestionnaireCsv());
            WriteCheckpointFile(
                Path.Combine(folder, $"CAREXR_Questionnaire_StageEvents_{suffix}.csv"),
                BuildQuestionnaireStageEventsCsv());
            WriteCheckpointFile(
                Path.Combine(folder, $"CAREXR_Questionnaire_RawTrace_{suffix}.csv"),
                BuildQuestionnaireRawTraceCsv());
            WriteCheckpointFile(
                Path.Combine(folder, $"CAREXR_Questionnaire_InteractionEvents_{suffix}.csv"),
                BuildQuestionnaireInteractionEventsCsv());
            WriteCheckpointFile(
                Path.Combine(folder, $"CAREXR_Questionnaire_Metadata_{suffix}.csv"),
                BuildQuestionnaireMetadataCsv());
            WriteCheckpointFile(
                Path.Combine(folder, $"CAREXR_Questionnaire_PhysicalSpeedSamples_{suffix}.csv"),
                BuildQuestionnairePhysicalSpeedSamplesCsv());
            WriteCheckpointFile(
                Path.Combine(folder, $"CAREXR_Questionnaire_SlotSpeedEvents_{suffix}.csv"),
                BuildQuestionnaireSlotSpeedEventsCsv());
            WriteCheckpointFile(
                Path.Combine(folder, $"CAREXR_Questionnaire_SpeedSummary_{suffix}.csv"),
                BuildQuestionnaireSpeedSummaryCsv());
        }
        catch (Exception exception)
        {
            Debug.LogError($"[PAXSM Questionnaire] Live checkpoint failed: {exception.Message}", this);
        }
    }

    void WriteCheckpointFile(string path, string content)
    {
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, Encoding.UTF8);
        if (File.Exists(path))
        {
            try
            {
                File.Replace(temporaryPath, path, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(temporaryPath, path, true);
                File.Delete(temporaryPath);
            }
        }
        else
            File.Move(temporaryPath, path);
    }

    string GetOutputFolder()
    {
        return ExperimentRunContext.ResolveOutputDirectory(outputFolderName);
    }

    string BuildTrialCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,sessionNumber,conditionLabel,taskType,blockId,taskProfileId,targetDimension,presentationOrder,trialIndex,scheduleId,cue,rule,targetLayout,ruleComplexity,targetCount,distractorCount,targetDistance,targetSize,targetHorizontalSpan,targetVerticalSpan,timeLimit,successThresholdStrictness,effectiveSelectionCone,effectiveSelectionAssistRadius,gazeFallbackAllowed,feedbackDelay,controlNoise,decisionRt,timeout,isCorrect,correctHapticPlayed,correctHapticSuppressed,correctIndex,selectedIndex,pointerPath,pointerPeakSpeed,pauseCount,hoverChangeCount");
        foreach (TrialRecord r in _trialRecords)
        {
            sb.AppendLine(string.Join(",",
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(r.blockId), Csv(r.blockId), Csv(r.taskProfileId), Csv(r.targetDimension), r.presentationOrder, r.trialIndex,
                Csv(r.scheduleId), Csv(r.cue), Csv(r.rule), Csv(r.targetLayout), r.ruleComplexity,
                r.targetCount, r.distractorCount, F(r.targetDistance), F(r.targetSize),
                F(r.targetHorizontalSpan), F(r.targetVerticalSpan), F(r.timeLimit),
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
        sb.AppendLine("participantId,sessionNumber,conditionLabel,taskType,blockId,taskProfileId,targetDimension,presentationOrder,ruleComplexity,targetCount,distractorCount,targetDistance,targetSize,targetHorizontalSpan,targetVerticalSpan,timeLimit,successThresholdStrictness,effectiveSelectionCone,effectiveSelectionAssistRadius,gazeFallbackAllowed,feedbackDelay,controlNoise,trials,accuracy,meanDecisionRt,timeoutCount,meanPointerPath,meanPeakSpeed,totalPauseCount,totalHoverChangeCount");
        List<ProbeBlockProfile> summaryOrder = _runOrderUsed.Count > 0 ? _runOrderUsed : blockProfiles;
        foreach (ProbeBlockProfile profile in summaryOrder)
        {
            int n = 0;
            int presentationOrder = 0;
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
                if (presentationOrder == 0)
                    presentationOrder = r.presentationOrder;
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
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(profile.blockId), Csv(profile.blockId), Csv(EffectiveTaskProfileId(profile)), Csv(profile.targetTlxDimension),
                I(presentationOrder), Mathf.Clamp(profile.ruleComplexity, 1, 3), EffectiveTargetCount(profile),
                Mathf.Max(1, EffectiveTargetCount(profile) - 1), F(profile.targetDistance), F(profile.targetSize),
                F(EffectiveTargetHorizontalSpan(profile)), F(EffectiveTargetVerticalSpan(profile)),
                F(profile.timeLimitSeconds), F(Mathf.Clamp01(profile.successThresholdStrictness)),
                F(EffectiveSelectionCone(profile)), F(EffectiveSelectionAssistRadius(profile)),
                IsGazeFallbackAllowed(profile) ? "1" : "0", F(profile.feedbackDelaySeconds), F(profile.controlNoiseDegrees), n,
                F(correct / (float)n), F(rt / n), timeout,
                F(path / n), F(peak / n), pauses, hovers));
        }
        return sb.ToString();
    }

    string BuildRunOrderCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "participantId,sessionNumber,conditionLabel,presentationOrder,blockId,taskProfileId,displayName," +
            "targetDimension,trialsPerBlock,configuredOrder,randomizedMiddleBlock");
        for (int i = 0; i < _runOrderUsed.Count; i++)
        {
            ProbeBlockProfile profile = _runOrderUsed[i];
            if (profile == null)
                continue;
            int configuredOrder = blockProfiles.IndexOf(profile) + 1;
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), I(i + 1),
                Csv(profile.blockId), Csv(EffectiveTaskProfileId(profile)), Csv(profile.displayName), Csv(profile.targetTlxDimension),
                I(profile.trialsPerBlock), I(configuredOrder),
                B(randomizeWorkloadBlocks && IsRandomizedMiddleWorkloadBlock(profile))
            }));
        }
        return sb.ToString();
    }

    string BuildInterBlockConfirmationCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "participantId,sessionNumber,conditionLabel,fromBlockId,toBlockId," +
            "fromPresentationOrder,toPresentationOrder,shownRealtime,confirmedRealtime,waitSeconds,inputSource");
        for (int i = 0; i < _interBlockConfirmationRecords.Count; i++)
        {
            InterBlockConfirmationRecord r = _interBlockConfirmationRecords[i];
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()),
                Csv(r.fromBlockId), Csv(r.toBlockId), I(r.fromPresentationOrder),
                I(r.toPresentationOrder), F(r.shownRealtime), F(r.confirmedRealtime),
                F(r.waitSeconds), Csv(r.inputSource)
            }));
        }
        return sb.ToString();
    }

    string BuildQuestionnaireCsv()
    {
        var sb = new StringBuilder();
        var headers = new List<string>
        {
            "participantId", "sessionNumber", "conditionLabel", "schemaVersion", "featureAlgorithmVersion",
            "taskType", "blockId", "targetDimension", "calibrationCondition", "presentationOrder", "itemIndex", "itemId",
            "itemDimension", "prompt", "leftAnchor", "rightAnchor", "scale", "responseMode",
            "expectedAnswerTarget", "expectedConfidenceTarget", "answerInitialSlot", "confidenceInitialSlot",
            "answerTargetError", "confidenceTargetError", "answerPathRatio", "confidencePathRatio",
            "selectedScore", "confidence", "pauseThresholdSec", "stillThresholdSec",
            "fastFlickThresholdSps", "traceSampleIntervalSec", "speedDeltaMin", "speedDeltaK",
            "speedBandMinimumEpisodes", "microMinimumTransitions", "microMaximumSlotSpan",
            "confirmHoldRequiredSec", "readRt", "readEnterRealtime", "readExitRealtime", "readExitEvent",
            "answerEnterRealtime", "answerExitRealtime", "answerRt", "answerDecisionRt",
            "answerFirstInteractionRt", "answerConfirmHoldRt", "answerConfirmAttemptCount",
            "answerConfirmCancelCount", "answerGrabCount", "answerPointerPath", "answerHoverChangeCount",
            "confidenceEnterRealtime", "confidenceExitRealtime", "confidenceRt", "confidenceDecisionRt",
            "confidenceFirstInteractionRt", "confidenceConfirmHoldRt", "confidenceConfirmAttemptCount",
            "confidenceConfirmCancelCount", "confidenceGrabCount", "confidencePointerPath",
            "confidenceHoverChangeCount"
        };
        AppendStageMetricHeaders(headers, "answer");
        AppendStageMetricHeaders(headers, "confidence");
        sb.AppendLine(string.Join(",", headers));

        foreach (QuestionnaireRecord r in _questionnaireRecords)
        {
            var values = new List<string>
            {
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(QuestionnaireSchemaVersion),
                Csv(EffectiveFeatureAlgorithmVersion()), Csv(r.blockId), Csv(r.blockId), Csv(r.targetDimension),
                Csv(r.calibrationCondition), I(r.presentationOrder), I(r.itemIndex), Csv(r.itemId),
                Csv(r.itemDimension), Csv(r.prompt), Csv(r.leftAnchor), Csv(r.rightAnchor), I(r.scale),
                Csv(r.responseMode), I(r.expectedAnswerTarget), I(r.expectedConfidenceTarget),
                I(r.answerInitialSlot), I(r.confidenceInitialSlot), I(r.answerTargetError),
                I(r.confidenceTargetError), F(r.answerPathRatio), F(r.confidencePathRatio), I(r.selectedScore),
                I(r.confidence), F(questionnairePauseThresholdSec), F(questionnaireStillThresholdSec),
                F(questionnaireFastFlickThresholdSps), F(questionnaireTraceSampleIntervalSec),
                F(questionnaireSpeedDeltaMin), F(questionnaireSpeedDeltaK), I(questionnaireSpeedBandMinimumEpisodes),
                I(questionnaireMicroMinimumTransitions), I(questionnaireMicroMaximumSlotSpan),
                F(questionnaireConfirmHoldSeconds), F(r.readRt), F(r.readEnterRealtime), F(r.readExitRealtime),
                Csv(r.readExitEvent), F(r.answerEnterRealtime), F(r.answerExitRealtime), F(r.answerRt),
                F(r.answerDecisionRt), F(r.answerFirstInteractionRt), F(r.answerConfirmHoldRt),
                I(r.answerConfirmAttemptCount), I(r.answerConfirmCancelCount), I(r.answerGrabCount),
                F(r.answerPointerPath), I(r.answerHoverChangeCount), F(r.confidenceEnterRealtime),
                F(r.confidenceExitRealtime), F(r.confidenceRt), F(r.confidenceDecisionRt),
                F(r.confidenceFirstInteractionRt), F(r.confidenceConfirmHoldRt),
                I(r.confidenceConfirmAttemptCount), I(r.confidenceConfirmCancelCount), I(r.confidenceGrabCount),
                F(r.confidencePointerPath), I(r.confidenceHoverChangeCount)
            };
            AppendStageMetricValues(values, r.answerMetrics);
            AppendStageMetricValues(values, r.confidenceMetrics);
            sb.AppendLine(string.Join(",", values));
        }
        return sb.ToString();
    }

    void AppendStageMetricHeaders(List<string> headers, string prefix)
    {
        string[] names =
        {
            "TickCount", "CurrentSlot", "CurrentAngleY", "SlotChangeCount", "ReverseCount", "PauseCount",
            "ConfirmCount", "MinSlot", "MaxSlot", "SlotSpan", "UniqueSlotsVisited", "StillEpisodeCount",
            "StillOverThresholdSum", "StillTimeSum", "MicroAdjustTimeSum", "MicroAdjustCount",
            "NormalAdjustTimeSum", "NormalAdjustCount", "FlickTimeSum", "FastFlickCount", "MaxFlickVel",
            "MaxAbsVel", "ActiveMoveTimeSum", "ActiveMoveCount", "TotalAbsAngle", "SpeedBandValid",
            "SpeedMedian", "SpeedMAD", "SpeedThLow", "SpeedThHigh", "SpeedBandNote"
        };
        for (int i = 0; i < names.Length; i++)
            headers.Add(prefix + names[i]);
    }

    void AppendStageMetricValues(List<string> values, QuestionnaireStageMetrics metrics)
    {
        QuestionnaireStageMetrics m = metrics ?? new QuestionnaireStageMetrics();
        values.AddRange(new[]
        {
            I(m.tickCount), I(m.currentSlot), F(m.currentAngleY), I(m.slotChangeCount), I(m.reverseCount),
            I(m.pauseCount), I(m.confirmCount), I(m.minSlot), I(m.maxSlot), I(m.SlotSpan),
            I(m.uniqueSlotsVisited), I(m.stillEpisodeCount), F(m.stillOverThresholdSum), F(m.stillTimeSum),
            F(m.microAdjustTimeSum), I(m.microAdjustCount), F(m.normalAdjustTimeSum), I(m.normalAdjustCount),
            F(m.flickTimeSum), I(m.fastFlickCount), F(m.maxFlickVel), F(m.maxAbsVel),
            F(m.activeMoveTimeSum), I(m.activeMoveCount), F(m.totalAbsAngle), B(m.speedBandValid),
            F(m.speedMedian), F(m.speedMAD), F(m.speedThLow), F(m.speedThHigh), Csv(m.speedBandNote)
        });
    }

    string BuildQuestionnaireStageEventsCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,sessionNumber,conditionLabel,schemaVersion,taskType,blockId,presentationOrder,itemIndex,itemId,mark,stage,event,realtime,utc");
        foreach (QuestionnaireStageEvent e in _questionnaireStageEvents)
        {
            sb.AppendLine(string.Join(",",
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(QuestionnaireSchemaVersion),
                Csv(e.blockId), Csv(e.blockId), I(e.presentationOrder), I(e.itemIndex), Csv(e.itemId),
                Csv($"Q{e.itemIndex}-{Mathf.Max(1, e.presentationOrder)}"), Csv(e.stage), Csv(e.eventType),
                F(e.realtime), Csv(e.utc)));
        }
        return sb.ToString();
    }

    string BuildQuestionnaireRawTraceCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,sessionNumber,conditionLabel,schemaVersion,featureAlgorithmVersion,taskType,blockId,presentationOrder,itemIndex,itemId,mark,stage,eventIndex,realtime,stageElapsed,slot,angle,sampleType,source,isAnchor");
        foreach (QuestionnaireRawTraceRecord r in _questionnaireRawTraceRecords)
        {
            QuestionnaireTraceSample sample = r.sample;
            if (sample == null)
                continue;
            sb.AppendLine(string.Join(",",
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(QuestionnaireSchemaVersion),
                Csv(EffectiveFeatureAlgorithmVersion()), Csv(r.blockId), Csv(r.blockId), I(r.presentationOrder),
                I(r.itemIndex), Csv(r.itemId), Csv($"Q{r.itemIndex}-{Mathf.Max(1, r.presentationOrder)}"),
                Csv(r.stage), I(r.eventIndex), F(sample.realtime),
                F(Mathf.Max(0f, sample.realtime - r.stageStartRealtime)), I(sample.slot), F(sample.angle),
                Csv(sample.sampleType), Csv(sample.source), B(sample.isAnchor)));
        }
        return sb.ToString();
    }

    string BuildQuestionnairePhysicalSpeedSamplesCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,sessionNumber,conditionLabel,schemaVersion,speedSchemaVersion,taskType,blockId,presentationOrder,itemIndex,itemId,mark,stage,grabIndex,sampleIndex,realtime,stageElapsed,slot,wristTwistDegrees,deltaDegrees,deltaTimeSec,physicalAngularSpeedDps,validForCalibration,exclusionReason");
        foreach (QuestionnairePhysicalSpeedSample sample in _questionnairePhysicalSpeedSamples)
        {
            sb.AppendLine(string.Join(",",
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(QuestionnaireSchemaVersion),
                Csv(QuestionnaireSpeedSchemaVersion), Csv("QuestionnaireRead"), Csv(sample.blockId),
                I(sample.presentationOrder), I(sample.itemIndex), Csv(sample.itemId),
                Csv($"Q{sample.itemIndex}-{Mathf.Max(1, sample.presentationOrder)}"), Csv(sample.stage),
                I(sample.grabIndex), I(sample.sampleIndex), F(sample.realtime),
                F(Mathf.Max(0f, sample.realtime - sample.stageStartRealtime)), I(sample.slot),
                F(sample.wristTwistDegrees), F(sample.deltaDegrees), F(sample.deltaTime),
                F(sample.physicalAngularSpeedDps), B(sample.validForCalibration), Csv(sample.exclusionReason)));
        }
        return sb.ToString();
    }

    string BuildQuestionnaireSlotSpeedEventsCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,sessionNumber,conditionLabel,schemaVersion,speedSchemaVersion,taskType,blockId,presentationOrder,itemIndex,itemId,mark,stage,grabIndex,eventIndex,realtime,stageElapsed,fromSlot,toSlot,deltaSlots,deltaTimeSec,slotsPerSecond,slotAngleDegrees,detentAngularSpeedDps,wristTwistDegrees,latestPhysicalAngularSpeedDps,validForCalibration,exclusionReason");
        foreach (QuestionnaireSlotSpeedEvent speedEvent in _questionnaireSlotSpeedEvents)
        {
            sb.AppendLine(string.Join(",",
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(QuestionnaireSchemaVersion),
                Csv(QuestionnaireSpeedSchemaVersion), Csv("QuestionnaireRead"), Csv(speedEvent.blockId),
                I(speedEvent.presentationOrder), I(speedEvent.itemIndex), Csv(speedEvent.itemId),
                Csv($"Q{speedEvent.itemIndex}-{Mathf.Max(1, speedEvent.presentationOrder)}"), Csv(speedEvent.stage),
                I(speedEvent.grabIndex), I(speedEvent.eventIndex), F(speedEvent.realtime),
                F(Mathf.Max(0f, speedEvent.realtime - speedEvent.stageStartRealtime)),
                I(speedEvent.fromSlot), I(speedEvent.toSlot), I(speedEvent.deltaSlots), F(speedEvent.deltaTime),
                F(speedEvent.slotsPerSecond), F(speedEvent.slotAngleDegrees),
                F(speedEvent.detentAngularSpeedDps), F(speedEvent.wristTwistDegrees),
                F(speedEvent.latestPhysicalAngularSpeedDps), B(speedEvent.validForCalibration),
                Csv(speedEvent.exclusionReason)));
        }
        return sb.ToString();
    }

    string BuildQuestionnaireSpeedSummaryCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,sessionNumber,conditionLabel,schemaVersion,speedSchemaVersion,scope,blockId,presentationOrder,itemIndex,itemId,stage,responseScale,slotAngleDegrees,slotTransitionCount,validSlotTransitionCount,slotSpsMedian,slotSpsMAD,slotSpsP90,slotSpsP95,slotSpsMax,detentDpsMedian,detentDpsMAD,detentDpsP90,detentDpsP95,detentDpsMax,physicalSampleCount,validPhysicalSampleCount,physicalDpsMedian,physicalDpsMAD,physicalDpsP90,physicalDpsP95,physicalDpsMax,calibrationQuality,qualityNote");

        for (int i = 0; i < _questionnaireRecords.Count; i++)
        {
            QuestionnaireRecord record = _questionnaireRecords[i];
            AppendQuestionnaireSpeedSummaryRow(
                sb, "item_stage", record, "Answer", Mathf.Max(2, record.scale));
            if (record.confidenceEnterRealtime >= 0f && record.confidenceExitRealtime >= 0f)
            {
                AppendQuestionnaireSpeedSummaryRow(
                    sb, "item_stage", record, "Confidence", Mathf.Max(2, questionnaireConfidenceScale));
            }
        }

        AppendQuestionnaireSpeedSummaryRow(
            sb, "participant_stage", null, "Answer", Mathf.Max(2, questionnaireScale));
        if (_questionnaireRecords.Exists(record =>
                record != null && record.confidenceEnterRealtime >= 0f && record.confidenceExitRealtime >= 0f))
        {
            AppendQuestionnaireSpeedSummaryRow(
                sb, "participant_stage", null, "Confidence", Mathf.Max(2, questionnaireConfidenceScale));
        }
        return sb.ToString();
    }

    void AppendQuestionnaireSpeedSummaryRow(
        StringBuilder sb,
        string scope,
        QuestionnaireRecord record,
        string stage,
        int responseScale)
    {
        var slotSpeeds = new List<float>();
        var detentSpeeds = new List<float>();
        var physicalSpeeds = new List<float>();
        int slotEventCount = 0;
        int physicalSampleCount = 0;

        for (int i = 0; i < _questionnaireSlotSpeedEvents.Count; i++)
        {
            QuestionnaireSlotSpeedEvent speedEvent = _questionnaireSlotSpeedEvents[i];
            if (!QuestionnaireSpeedContextMatches(
                    speedEvent.blockId, speedEvent.presentationOrder, speedEvent.itemIndex,
                    speedEvent.itemId, speedEvent.stage, record, stage))
                continue;

            slotEventCount++;
            if (!speedEvent.validForCalibration || speedEvent.slotsPerSecond < 0f)
                continue;
            slotSpeeds.Add(speedEvent.slotsPerSecond);
            detentSpeeds.Add(speedEvent.detentAngularSpeedDps);
        }

        for (int i = 0; i < _questionnairePhysicalSpeedSamples.Count; i++)
        {
            QuestionnairePhysicalSpeedSample sample = _questionnairePhysicalSpeedSamples[i];
            if (!QuestionnaireSpeedContextMatches(
                    sample.blockId, sample.presentationOrder, sample.itemIndex,
                    sample.itemId, sample.stage, record, stage))
                continue;

            physicalSampleCount++;
            if (sample.validForCalibration)
                physicalSpeeds.Add(sample.physicalAngularSpeedDps);
        }

        QuestionnaireSpeedDistribution slotDistribution = QuestionnaireSpeedStatistics.Calculate(slotSpeeds);
        QuestionnaireSpeedDistribution detentDistribution = QuestionnaireSpeedStatistics.Calculate(detentSpeeds);
        QuestionnaireSpeedDistribution physicalDistribution = QuestionnaireSpeedStatistics.Calculate(physicalSpeeds);
        bool participantScope = string.Equals(scope, "participant_stage", StringComparison.Ordinal);
        string quality = participantScope
            ? QuestionnaireSpeedStatistics.CalibrationQuality(
                slotDistribution.count,
                physicalDistribution.count,
                questionnaireCalibrationMinimumSlotEvents,
                questionnaireCalibrationMinimumPhysicalSamples)
            : "descriptive_only";
        string qualityNote = participantScope
            ? $"valid_slot_events={slotDistribution.count}/{Mathf.Max(1, questionnaireCalibrationMinimumSlotEvents)};" +
              $"valid_physical_samples={physicalDistribution.count}/{Mathf.Max(1, questionnaireCalibrationMinimumPhysicalSamples)};" +
              "use_stage_specific_profile"
            : "item rows are descriptive; use participant_stage for personal calibration";
        float slotAngleDegrees = responseScale > 1
            ? Mathf.Abs(QuestionnaireKnobMaxAngle() - QuestionnaireKnobMinAngle()) / (responseScale - 1f)
            : 0f;

        sb.AppendLine(string.Join(",",
            Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(QuestionnaireSchemaVersion),
            Csv(QuestionnaireSpeedSchemaVersion), Csv(scope), Csv(record != null ? record.blockId : "ALL"),
            I(record != null ? record.presentationOrder : 0), I(record != null ? record.itemIndex : 0),
            Csv(record != null ? record.itemId : "ALL"), Csv(stage), I(responseScale), F(slotAngleDegrees),
            I(slotEventCount), I(slotDistribution.count),
            F(slotDistribution.median), F(slotDistribution.mad), F(slotDistribution.p90),
            F(slotDistribution.p95), F(slotDistribution.max),
            F(detentDistribution.median), F(detentDistribution.mad), F(detentDistribution.p90),
            F(detentDistribution.p95), F(detentDistribution.max),
            I(physicalSampleCount), I(physicalDistribution.count),
            F(physicalDistribution.median), F(physicalDistribution.mad), F(physicalDistribution.p90),
            F(physicalDistribution.p95), F(physicalDistribution.max), Csv(quality), Csv(qualityNote)));
    }

    bool QuestionnaireSpeedContextMatches(
        string blockId,
        int presentationOrder,
        int itemIndex,
        string itemId,
        string eventStage,
        QuestionnaireRecord record,
        string requestedStage)
    {
        if (!string.Equals(eventStage, requestedStage, StringComparison.OrdinalIgnoreCase))
            return false;
        if (record == null)
            return true;
        return string.Equals(blockId, record.blockId, StringComparison.Ordinal) &&
               presentationOrder == record.presentationOrder &&
               itemIndex == record.itemIndex &&
               string.Equals(itemId, record.itemId, StringComparison.Ordinal);
    }

    string BuildQuestionnaireInteractionEventsCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,sessionNumber,conditionLabel,schemaVersion,taskType,blockId,presentationOrder,itemIndex,itemId,mark,stage,tag,data,realtime,utc");
        foreach (QuestionnaireInteractionEvent e in _questionnaireInteractionEvents)
        {
            sb.AppendLine(string.Join(",",
                Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()), Csv(QuestionnaireSchemaVersion),
                Csv(e.blockId), Csv(e.blockId), I(e.presentationOrder), I(e.itemIndex), Csv(e.itemId),
                Csv($"Q{e.itemIndex}-{Mathf.Max(1, e.presentationOrder)}"), Csv(e.stage), Csv(e.tag), Csv(e.data),
                F(e.realtime), Csv(e.utc)));
        }
        return sb.ToString();
    }

    string BuildQuestionnaireMetadataCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,sessionNumber,conditionLabel,questionnaireSessionId,schemaVersion,featureAlgorithmVersion,questionBankResourcesPath,responseScale,confidenceScale,collectConfidence,requireReadAcknowledgement,readExitControl,confirmHoldSeconds,knobDiameterMeters,knobArcDegrees,pauseThresholdSec,stillThresholdSec,fastFlickThresholdSps,traceSampleIntervalSec,speedDeltaMin,speedDeltaK,speedBandMinimumEpisodes,microMinimumTransitions,microMaximumSlotSpan,personalSpeedRecordingEnabled,speedSchemaVersion,physicalSpeedSampleIntervalSec,physicalSpeedMinimumDeltaDegrees,physicalSpeedMinimumDps,physicalSpeedMaximumSampleGapSec,slotSpeedMaximumTransitionGapSec,calibrationMinimumSlotEvents,calibrationMinimumPhysicalSamples,physicalAngleSource,slotSpeedDefinition,speedConfirmationSamplesExcluded,unityVersion,platform,deviceModel,operatingSystem,xrLoadedDevice,generatedUtc");
        sb.AppendLine(string.Join(",",
            Csv(participantId), I(sessionNumber), Csv(EffectiveConditionLabel()),
            Csv(questionnaireOnlyMode ? questionnaireOnlySessionId : "workload_probe_inter_block"),
            Csv(QuestionnaireSchemaVersion), Csv(EffectiveFeatureAlgorithmVersion()), Csv(questionnaireBankResourcesPath),
            I(questionnaireScale), I(questionnaireConfidenceScale), B(collectConfidenceAfterEachItem),
            B(requireReadAcknowledgement), Csv(requireReadAcknowledgement ? "right_primary_button_a" : "not_collected"), F(questionnaireConfirmHoldSeconds),
            F(questionnaireKnobDiameterMeters), F(questionnaireKnobArcDegrees), F(questionnairePauseThresholdSec),
            F(questionnaireStillThresholdSec), F(questionnaireFastFlickThresholdSps),
            F(questionnaireTraceSampleIntervalSec), F(questionnaireSpeedDeltaMin), F(questionnaireSpeedDeltaK),
            I(questionnaireSpeedBandMinimumEpisodes), I(questionnaireMicroMinimumTransitions),
            I(questionnaireMicroMaximumSlotSpan), B(ShouldRecordQuestionnairePersonalSpeed()),
            Csv(QuestionnaireSpeedSchemaVersion), F(questionnairePhysicalSpeedSampleIntervalSec),
            F(questionnairePhysicalSpeedMinimumDeltaDegrees), F(questionnairePhysicalSpeedMinimumDps),
            F(questionnairePhysicalSpeedMaximumSampleGapSec), F(questionnaireSlotSpeedMaximumTransitionGapSec),
            I(questionnaireCalibrationMinimumSlotEvents), I(questionnaireCalibrationMinimumPhysicalSamples),
            Csv("controller_wrist_twist_relative_to_grab_start"),
            Csv("absolute_delta_slots_over_time_between_grab_baseline_or_slot_events"), B(true),
            Csv(Application.unityVersion), Csv(Application.platform.ToString()),
            Csv(SystemInfo.deviceModel), Csv(SystemInfo.operatingSystem), Csv(XRSettings.loadedDeviceName),
            Csv(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))));
        return sb.ToString();
    }

    string EffectiveConditionLabel()
    {
        if (!string.IsNullOrWhiteSpace(conditionLabel))
            return conditionLabel.Trim();
        return questionnaireOnlyMode ? "QuestionnaireRead" : "WorkloadProbe";
    }

    string EffectiveFeatureAlgorithmVersion()
    {
        return string.IsNullOrWhiteSpace(featureAlgorithmVersion)
            ? QuestionnaireTraceAnalyzer.AlgorithmVersion
            : featureAlgorithmVersion.Trim();
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

    string I(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    string B(bool value)
    {
        return value ? "1" : "0";
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
