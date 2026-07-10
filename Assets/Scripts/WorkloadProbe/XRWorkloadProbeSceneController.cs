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
    [Serializable]
    public class ProbeBlockProfile
    {
        public string blockId = "baseline";
        public string displayName = "Baseline";
        public string targetTlxDimension = "baseline";
        [Range(1, 3)] public int ruleComplexity = 1;
        [Range(2, 10)] public int targetCount = 4;
        [Range(0, 8)] public int distractorCount = 1;
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
        public int targetCount;
        public float targetDistance;
        public float targetSize;
        public float timeLimit;
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

    public string participantId = "P001";
    public bool randomizeWorkloadBlocks = false;
    public bool startAutomatically = true;
    public bool writeCsvOnQuit = true;
    public string outputFolderName = "XRWorkloadProbe_Data";

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
    readonly Dictionary<string, Material> _colorMaterials = new Dictionary<string, Material>();
    readonly List<TargetInfo> _targets = new List<TargetInfo>();
    readonly List<TrialRecord> _trialRecords = new List<TrialRecord>();
    readonly List<InputDevice> _rightHandDevices = new List<InputDevice>();
    Transform _rightPointerTransform;
    Transform _targetAreaAnchor;
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

    void Awake()
    {
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
            if (selectedIndex < 0 && enableGazeFallbackSelection && _mainCamera != null)
            {
                selectedIndex = ResolveTargetIndex(
                    new Ray(_mainCamera.transform.position, _mainCamera.transform.forward),
                    gazeFallbackConeDegrees,
                    selectionAssistRadius * 1.15f);
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
            yield return WaitForSecondsOrN(3.5f);
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
            targetCount = profile.targetCount,
            targetDistance = profile.targetDistance,
            targetSize = profile.targetSize,
            timeLimit = profile.timeLimitSeconds,
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

    bool TryPlayRightHandHaptic(float amplitude, float duration)
    {
        if (!enableCorrectHaptics)
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
            return new TrialSpec("direct-color", trialIndex % Mathf.Max(2, profile.targetCount), trialIndex);
        return schedule[trialIndex % schedule.Length];
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
        int count = Mathf.Clamp(profile.targetCount, 2, _colorNames.Length);
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

        if (_currentProfile != null && _currentProfile.controlNoiseDegrees > 0f)
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
        return ResolveTargetIndex(ray, controllerSelectionConeDegrees, selectionAssistRadius);
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

        _selectionRayRenderer.enabled = showSelectionRay && _trialActive;
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
            distractorCount = 1,
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
            distractorCount = 5,
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
            distractorCount = 5,
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

    void WriteCsvFiles(string reason)
    {
        string folder = GetOutputFolder();
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string trialPath = Path.Combine(folder, $"WorkloadProbe_Trials_{participantId}_{stamp}_{reason}.csv");
        string blockPath = Path.Combine(folder, $"WorkloadProbe_Blocks_{participantId}_{stamp}_{reason}.csv");

        File.WriteAllText(trialPath, BuildTrialCsv(), Encoding.UTF8);
        File.WriteAllText(blockPath, BuildBlockCsv(), Encoding.UTF8);
        Debug.Log($"[XRWorkloadProbe] Saved logs:\n{trialPath}\n{blockPath}", this);
    }

    string GetOutputFolder()
    {
        return Path.Combine(Application.persistentDataPath, outputFolderName);
    }

    string BuildTrialCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("participantId,taskType,blockId,targetDimension,presentationOrder,trialIndex,scheduleId,cue,rule,targetLayout,targetCount,targetDistance,targetSize,timeLimit,feedbackDelay,controlNoise,decisionRt,timeout,isCorrect,correctHapticPlayed,correctHapticSuppressed,correctIndex,selectedIndex,pointerPath,pointerPeakSpeed,pauseCount,hoverChangeCount");
        foreach (TrialRecord r in _trialRecords)
        {
            sb.AppendLine(string.Join(",",
                Csv(participantId), Csv(r.blockId), Csv(r.blockId), Csv(r.targetDimension), r.presentationOrder, r.trialIndex,
                Csv(r.scheduleId), Csv(r.cue), Csv(r.rule), Csv(r.targetLayout), r.targetCount,
                F(r.targetDistance), F(r.targetSize), F(r.timeLimit), F(r.feedbackDelay), F(r.controlNoise),
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
        sb.AppendLine("participantId,taskType,blockId,targetDimension,trials,accuracy,meanDecisionRt,timeoutCount,meanPointerPath,meanPeakSpeed,totalPauseCount,totalHoverChangeCount");
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
                Csv(participantId), Csv(profile.blockId), Csv(profile.blockId), Csv(profile.targetTlxDimension), n,
                F(correct / (float)n), F(rt / n), timeout,
                F(path / n), F(peak / n), pauses, hovers));
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
