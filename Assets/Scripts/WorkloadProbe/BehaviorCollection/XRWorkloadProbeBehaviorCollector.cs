using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

[DisallowMultipleComponent]
[RequireComponent(typeof(XRWorkloadProbeSceneController))]
public sealed class XRWorkloadProbeBehaviorCollector : MonoBehaviour
{
    const string SchemaVersion = "XRWorkloadProbe_Behavior_v1.2";
    const string AlgorithmVersion = "XRWorkloadProbe_29Metrics_v1.2";
    const int ExpectedMetricCount = 29;

    [Header("Scene-only Scope")]
    public XRWorkloadProbeSceneController probeController;
    public bool writeRawSamples = true;

    [Header("Sampling")]
    [Range(10f, 120f)] public float sampleRateHz = 30f;
    [Range(0.05f, 1f)] public float maximumTrackingGapSeconds = 0.25f;
    [Range(0.05f, 1f)] public float maximumPositionStepMeters = 0.35f;

    [Header("Controller Ray Movement")]
    [Range(0.1f, 3f)] public float maximumRayEndpointStepMeters = 1f;
    [Range(0.5f, 5f)] public float fallbackRayProjectionDistanceMeters = 2f;

    [Header("Optional Eye Movement")]
    [Tooltip("Enable only when the approved protocol includes eye tracking for this scene.")]
    public bool collectEyeGaze = false;
    [Range(5f, 80f)] public float fixationVelocityThresholdDegreesPerSecond = 30f;
    [Range(40f, 300f)] public float saccadeVelocityThresholdDegreesPerSecond = 100f;
    [Range(0.05f, 0.5f)] public float minimumFixationSeconds = 0.1f;

    [Header("Entropy")]
    [Range(0.02f, 0.5f)] public float handEntropyBinMeters = 0.1f;
    [Range(2f, 30f)] public float headDirectionEntropyBinDegrees = 10f;

    readonly List<InputDevice> _headDevices = new List<InputDevice>();
    readonly List<InputDevice> _leftHandDevices = new List<InputDevice>();
    readonly List<InputDevice> _rightHandDevices = new List<InputDevice>();
    readonly List<InputDevice> _eyeDevices = new List<InputDevice>();
    readonly HashSet<int> _finalizedPresentationOrders = new HashSet<int>();

    FieldInfo _trialActiveField;
    FieldInfo _questionnaireActiveField;
    FieldInfo _currentProfileField;
    FieldInfo _blockIndexField;
    FieldInfo _trialIndexField;
    FieldInfo _trialRecordsField;

    Camera _mainCamera;
    Transform _rightRayTransform;
    float _nextRightRaySearchRealtime;
    XRWorkloadProbeBehaviorBlock _activeBlock;
    string _runStamp;
    float _nextSampleRealtime;
    bool _reflectionReady;
    bool _isShuttingDown;

    public bool IsCollectingExperiment => _activeBlock != null;
    public string ActiveBlockId => _activeBlock != null ? _activeBlock.blockId : "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallOnlyInXRWorkloadProbeScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() ||
            !string.Equals(activeScene.name, "XRWorkloadProbeScene", StringComparison.Ordinal))
            return;

        GameObject bootstrap = GameObject.Find("XR Workload Probe Bootstrap");
        if (bootstrap == null)
            return;

        XRWorkloadProbeSceneController controller = bootstrap.GetComponent<XRWorkloadProbeSceneController>();
        if (controller == null || bootstrap.GetComponent<XRWorkloadProbeBehaviorCollector>() != null)
            return;

        // Runtime-only installation keeps the shared Unity scene YAML untouched.
        bootstrap.AddComponent<XRWorkloadProbeBehaviorCollector>();
    }

    void Awake()
    {
        if (probeController == null)
            probeController = GetComponent<XRWorkloadProbeSceneController>();

        _runStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        _reflectionReady = CacheSceneControllerState();
        if (!_reflectionReady)
        {
            enabled = false;
            Debug.LogError(
                "[XRWorkloadProbe Behavior] Could not bind the XRWorkloadProbe scene lifecycle. " +
                "No behavior data will be collected.",
                this);
        }
    }

    void Update()
    {
        if (!_reflectionReady || probeController == null || probeController.questionnaireOnlyMode)
            return;

        bool trialActive = ReadControllerBool(_trialActiveField);
        bool questionnaireActive = ReadControllerBool(_questionnaireActiveField);
        XRWorkloadProbeSceneController.ProbeBlockProfile profile = ReadCurrentProfile();
        IList trialRecords = ReadTrialRecords();

        if (_activeBlock == null)
        {
            int presentationOrder = CurrentPresentationOrder();
            if (trialActive && !questionnaireActive && profile != null &&
                !_finalizedPresentationOrders.Contains(presentationOrder))
            {
                BeginBlock(profile, trialRecords);
            }
            else
            {
                return;
            }
        }

        int completedTrials = CountBlockTrialRecords(
            trialRecords,
            _activeBlock.trialRecordStartIndex,
            _activeBlock.blockId);

        if (questionnaireActive)
        {
            EndBlock(trialRecords, completedTrials >= _activeBlock.expectedTrials
                ? "completed_before_questionnaire"
                : "questionnaire_started_early");
            return;
        }

        bool profileChanged = profile != null &&
                              !string.Equals(profile.blockId, _activeBlock.blockId, StringComparison.OrdinalIgnoreCase);
        if (!trialActive && (completedTrials >= _activeBlock.expectedTrials || profileChanged))
        {
            EndBlock(trialRecords, completedTrials >= _activeBlock.expectedTrials
                ? "completed"
                : "profile_changed");
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (now >= _nextSampleRealtime)
        {
            CaptureSample(now);
            _nextSampleRealtime = now + 1f / Mathf.Max(1f, sampleRateHz);
        }
    }

    void OnApplicationQuit()
    {
        _isShuttingDown = true;
        if (_activeBlock != null)
            EndBlock(ReadTrialRecords(), "application_quit_partial");
    }

    void OnDisable()
    {
        if (!_isShuttingDown && _activeBlock != null)
            EndBlock(ReadTrialRecords(), "collector_disabled_partial");
    }

    bool CacheSceneControllerState()
    {
        if (probeController == null)
            return false;

        Type controllerType = typeof(XRWorkloadProbeSceneController);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        _trialActiveField = controllerType.GetField("_trialActive", flags);
        _questionnaireActiveField = controllerType.GetField("_questionnaireActive", flags);
        _currentProfileField = controllerType.GetField("_currentProfile", flags);
        _blockIndexField = controllerType.GetField("_blockIndex", flags);
        _trialIndexField = controllerType.GetField("_trialIndex", flags);
        _trialRecordsField = controllerType.GetField("_trialRecords", flags);
        return _trialActiveField != null &&
               _questionnaireActiveField != null &&
               _currentProfileField != null &&
               _blockIndexField != null &&
               _trialRecordsField != null;
    }

    bool ReadControllerBool(FieldInfo field)
    {
        if (field == null || probeController == null)
            return false;
        object value = field.GetValue(probeController);
        return value is bool boolean && boolean;
    }

    int ReadBlockIndex()
    {
        if (_blockIndexField == null || probeController == null)
            return _finalizedPresentationOrders.Count;
        object value = _blockIndexField.GetValue(probeController);
        return value is int index ? index : _finalizedPresentationOrders.Count;
    }

    int CurrentPresentationOrder()
    {
        return Mathf.Max(1, ReadBlockIndex() + 1);
    }

    int ReadTrialIndex()
    {
        if (_trialIndexField == null || probeController == null)
            return -1;
        object value = _trialIndexField.GetValue(probeController);
        return value is int index ? index : -1;
    }

    XRWorkloadProbeSceneController.ProbeBlockProfile ReadCurrentProfile()
    {
        return _currentProfileField?.GetValue(probeController)
            as XRWorkloadProbeSceneController.ProbeBlockProfile;
    }

    IList ReadTrialRecords()
    {
        return _trialRecordsField?.GetValue(probeController) as IList;
    }

    void BeginBlock(XRWorkloadProbeSceneController.ProbeBlockProfile profile, IList trialRecords)
    {
        float now = Time.realtimeSinceStartup;
        string participant = string.IsNullOrWhiteSpace(probeController.participantId)
            ? "unknown_participant"
            : probeController.participantId.Trim();
        string condition = string.IsNullOrWhiteSpace(probeController.conditionLabel)
            ? "WorkloadProbe"
            : probeController.conditionLabel.Trim();
        int presentationOrder = CurrentPresentationOrder();

        _activeBlock = new XRWorkloadProbeBehaviorBlock(
            participant,
            Mathf.Max(1, probeController.sessionNumber),
            condition,
            presentationOrder,
            profile.blockId,
            profile.targetTlxDimension,
            Mathf.Max(1, profile.trialsPerBlock),
            trialRecords?.Count ?? 0,
            now,
            handEntropyBinMeters,
            headDirectionEntropyBinDegrees,
            fixationVelocityThresholdDegreesPerSecond,
            saccadeVelocityThresholdDegreesPerSecond,
            minimumFixationSeconds);

        _nextSampleRealtime = now;
        CaptureSample(now);
        _nextSampleRealtime = now + 1f / Mathf.Max(1f, sampleRateHz);

        Debug.Log(
            $"[XRWorkloadProbe Behavior] Started independent behavior set {presentationOrder}: " +
            $"{profile.blockId}. Questionnaire sampling is disabled by lifecycle scope.",
            this);
    }

    void EndBlock(IList trialRecords, string reason)
    {
        if (_activeBlock == null)
            return;

        XRWorkloadProbeBehaviorBlock completed = _activeBlock;
        _activeBlock = null;
        completed.Finish(Time.realtimeSinceStartup, reason);
        XRWorkloadProbeTrialSummary trials = BuildTrialSummary(
            trialRecords,
            completed.trialRecordStartIndex,
            completed.blockId);
        List<XRWorkloadProbeBehaviorMetric> metrics = completed.BuildMetrics(trials);

        if (metrics.Count != ExpectedMetricCount)
        {
            Debug.LogError(
                $"[XRWorkloadProbe Behavior] {completed.blockId} produced {metrics.Count} metrics; " +
                $"expected {ExpectedMetricCount}. The set was not written.",
                this);
            return;
        }

        try
        {
            string folder = GetOutputFolder();
            Directory.CreateDirectory(folder);
            string safeParticipant = SafeFilePart(completed.participantId);
            string safeBlock = SafeFilePart(completed.blockId);
            string prefix = $"XRWorkloadProbe_Behavior_{completed.presentationOrder:00}_{safeBlock}_{safeParticipant}_{_runStamp}";
            string metricPath = Path.Combine(folder, prefix + "_Metrics.csv");
            WriteAtomic(metricPath, BuildMetricCsv(completed, trials, metrics));

            string rawPath = "";
            if (writeRawSamples)
            {
                rawPath = Path.Combine(folder, prefix + "_RawSamples.csv");
                WriteAtomic(rawPath, BuildRawSampleCsv(completed));
            }

            _finalizedPresentationOrders.Add(completed.presentationOrder);
            Debug.Log(
                $"[XRWorkloadProbe Behavior] Saved behavior set {completed.presentationOrder} " +
                $"({completed.blockId}, {metrics.Count} dimension-tagged metrics, " +
                $"{completed.samples.Count} experiment-only samples):\n{metricPath}" +
                (string.IsNullOrEmpty(rawPath) ? "" : $"\n{rawPath}"),
                this);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"[XRWorkloadProbe Behavior] Failed to save {completed.blockId}: {exception}",
                this);
        }
    }

    void CaptureSample(float realtime)
    {
        if (_activeBlock == null)
            return;

        if (_mainCamera == null)
            _mainCamera = Camera.main;

        bool trialActive = ReadControllerBool(_trialActiveField);
        var sample = new XRWorkloadProbeBehaviorSample
        {
            sampleIndex = _activeBlock.samples.Count + 1,
            elapsedSeconds = Mathf.Max(0f, realtime - _activeBlock.startRealtime),
            realtimeSeconds = realtime,
            trialActive = trialActive,
            trialIndex = trialActive ? ReadTrialIndex() + 1 : 0
        };

        sample.headValid = TryGetNodePose(
            XRNode.Head,
            _headDevices,
            out sample.headPosition,
            out sample.headRotation);

        sample.leftHandValid = TryGetNodePose(
            XRNode.LeftHand,
            _leftHandDevices,
            out sample.leftHandPosition,
            out sample.leftHandRotation);
        sample.rightHandValid = TryGetNodePose(
            XRNode.RightHand,
            _rightHandDevices,
            out sample.rightHandPosition,
            out sample.rightHandRotation);
        sample.rayValid = TryGetRightControllerRay(
            sample,
            out sample.rayOrigin,
            out sample.rayDirection);
        if (sample.rayValid)
        {
            XRWorkloadProbeSceneController.ProbeBlockProfile profile = ReadCurrentProfile();
            sample.rayProjectionDistanceMeters = profile != null && profile.targetDistance > 0f
                ? profile.targetDistance
                : fallbackRayProjectionDistanceMeters;
            sample.rayEndpoint = sample.rayOrigin +
                                 sample.rayDirection * sample.rayProjectionDistanceMeters;
        }
        sample.gazeValid = collectEyeGaze &&
                           TryGetEyeGazeDirection(out sample.gazeDirection);

        _activeBlock.AddSample(
            sample,
            maximumTrackingGapSeconds,
            maximumPositionStepMeters,
            maximumRayEndpointStepMeters);
    }

    bool TryGetRightControllerRay(
        XRWorkloadProbeBehaviorSample sample,
        out Vector3 origin,
        out Vector3 direction)
    {
        // A static scene transform is not evidence of a tracked controller.
        if (!sample.rightHandValid)
        {
            origin = default;
            direction = default;
            return false;
        }

        if ((_rightRayTransform == null || !_rightRayTransform.gameObject.activeInHierarchy) &&
            Time.realtimeSinceStartup >= _nextRightRaySearchRealtime)
        {
            _rightRayTransform = FindRightRayTransform();
            _nextRightRaySearchRealtime = Time.realtimeSinceStartup + 1f;
        }

        if (_rightRayTransform != null)
        {
            origin = _rightRayTransform.position;
            direction = _rightRayTransform.forward;
            if (IsFinite(origin) && IsFinite(direction) && direction.sqrMagnitude > 0.000001f)
            {
                direction.Normalize();
                return true;
            }
        }

        if (sample.rightHandValid)
        {
            origin = sample.rightHandPosition;
            direction = sample.rightHandRotation * Vector3.forward;
            if (IsFinite(origin) && IsFinite(direction) && direction.sqrMagnitude > 0.000001f)
            {
                direction.Normalize();
                return true;
            }
        }

        origin = default;
        direction = default;
        return false;
    }

    Transform FindRightRayTransform()
    {
        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate.gameObject.scene != gameObject.scene)
                continue;

            string path = GetTransformPath(candidate).ToLowerInvariant();
            if (!path.Contains("right"))
                continue;

            if (path.Contains("ray") || HasComponentNamed(candidate, "XRRayInteractor"))
                return candidate;
        }
        return null;
    }

    static string GetTransformPath(Transform transform)
    {
        var path = new StringBuilder(transform.name);
        Transform current = transform.parent;
        while (current != null)
        {
            path.Insert(0, current.name + "/");
            current = current.parent;
        }
        return path.ToString();
    }

    static bool HasComponentNamed(Transform transform, string componentName)
    {
        Component[] components = transform.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component != null && component.GetType().Name.Contains(componentName))
                return true;
        }
        return false;
    }

    static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    bool TryGetNodePose(
        XRNode node,
        List<InputDevice> devices,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = default;
        rotation = default;
        devices.Clear();
        InputDevices.GetDevicesAtXRNode(node, devices);
        for (int i = 0; i < devices.Count; i++)
        {
            InputDevice device = devices[i];
            if (!device.isValid)
                continue;
            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out position) &&
                device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation))
                return true;
        }
        return false;
    }

    bool TryGetEyeGazeDirection(out Vector3 gazeDirection)
    {
        gazeDirection = default;
        _eyeDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, _eyeDevices);
        for (int i = 0; i < _eyeDevices.Count; i++)
        {
            InputDevice device = _eyeDevices[i];
            if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.eyesData, out Eyes eyes))
                continue;

            if (eyes.TryGetFixationPoint(out Vector3 fixationPoint))
            {
                bool hasLeftPosition = eyes.TryGetLeftEyePosition(out Vector3 leftPosition);
                bool hasRightPosition = eyes.TryGetRightEyePosition(out Vector3 rightPosition);
                Vector3 eyeOrigin;
                if (hasLeftPosition && hasRightPosition)
                    eyeOrigin = (leftPosition + rightPosition) * 0.5f;
                else if (hasLeftPosition)
                    eyeOrigin = leftPosition;
                else if (hasRightPosition)
                    eyeOrigin = rightPosition;
                else if (_mainCamera != null)
                    eyeOrigin = _mainCamera.transform.position;
                else
                    eyeOrigin = Vector3.zero;

                Vector3 fixationDirection = fixationPoint - eyeOrigin;
                if (fixationDirection.sqrMagnitude > 0.000001f)
                {
                    gazeDirection = fixationDirection.normalized;
                    return true;
                }
            }

            bool hasLeftRotation = eyes.TryGetLeftEyeRotation(out Quaternion leftRotation);
            bool hasRightRotation = eyes.TryGetRightEyeRotation(out Quaternion rightRotation);
            if (hasLeftRotation || hasRightRotation)
            {
                Quaternion rotation = hasLeftRotation && hasRightRotation
                    ? Quaternion.Slerp(leftRotation, rightRotation, 0.5f)
                    : hasLeftRotation ? leftRotation : rightRotation;
                gazeDirection = rotation * Vector3.forward;
                if (gazeDirection.sqrMagnitude > 0.000001f)
                {
                    gazeDirection.Normalize();
                    return true;
                }
            }
        }
        return false;
    }

    int CountBlockTrialRecords(IList records, int startIndex, string blockId)
    {
        if (records == null)
            return 0;

        int count = 0;
        for (int i = Mathf.Clamp(startIndex, 0, records.Count); i < records.Count; i++)
        {
            object record = records[i];
            if (record != null && string.Equals(
                    ReadRecordString(record, "blockId"),
                    blockId,
                    StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }

    XRWorkloadProbeTrialSummary BuildTrialSummary(IList records, int startIndex, string blockId)
    {
        var result = new XRWorkloadProbeTrialSummary();
        if (records == null)
            return result;

        float firstCueTime = float.MaxValue;
        float lastSelectionTime = float.MinValue;
        for (int i = Mathf.Clamp(startIndex, 0, records.Count); i < records.Count; i++)
        {
            object record = records[i];
            if (record == null || !string.Equals(
                    ReadRecordString(record, "blockId"),
                    blockId,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            result.trialCount++;
            bool correct = ReadRecordBool(record, "isCorrect");
            bool timeout = ReadRecordBool(record, "timeout");
            if (correct)
                result.correctCount++;
            else
                result.errorCount++;
            if (timeout)
                result.timeoutCount++;
            result.totalDecisionTime += Mathf.Max(0f, ReadRecordFloat(record, "decisionRt"));
            firstCueTime = Mathf.Min(firstCueTime, ReadRecordFloat(record, "cueTime"));
            lastSelectionTime = Mathf.Max(lastSelectionTime, ReadRecordFloat(record, "selectionTime"));
        }

        result.valid = result.trialCount > 0;
        if (result.valid && firstCueTime < float.MaxValue && lastSelectionTime > float.MinValue)
            result.trialSpanSeconds = Mathf.Max(0f, lastSelectionTime - firstCueTime);
        return result;
    }

    static string ReadRecordString(object record, string fieldName)
    {
        object value = ReadRecordValue(record, fieldName);
        return value as string ?? "";
    }

    static bool ReadRecordBool(object record, string fieldName)
    {
        object value = ReadRecordValue(record, fieldName);
        return value is bool boolean && boolean;
    }

    static float ReadRecordFloat(object record, string fieldName)
    {
        object value = ReadRecordValue(record, fieldName);
        if (value is float single)
            return single;
        if (value is double number)
            return (float)number;
        return 0f;
    }

    static object ReadRecordValue(object record, string fieldName)
    {
        if (record == null)
            return null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return record.GetType().GetField(fieldName, flags)?.GetValue(record);
    }

    string GetOutputFolder()
    {
        return ExperimentRunContext.IsConfigured
            ? ExperimentRunContext.ResolveOutputDirectory(probeController.outputFolderName)
            : Path.Combine(Application.persistentDataPath, probeController.outputFolderName);
    }

    string BuildMetricCsv(
        XRWorkloadProbeBehaviorBlock block,
        XRWorkloadProbeTrialSummary trials,
        List<XRWorkloadProbeBehaviorMetric> metrics)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "schemaVersion,algorithmVersion,participantId,sessionNumber,conditionLabel,presentationOrder," +
            "blockId,targetDimension,expectedTrials,recordedTrials,completionReason,sampleRateHz," +
            "observedSampleRateHz,rawSampleCount,eyeGazeCollectionEnabled," +
            "dimension,metricId,metricName,value,unit,valid,source,operationalDefinition,qualityNote");

        float observedSampleRate = ObservedSampleRateHz(block);

        for (int i = 0; i < metrics.Count; i++)
        {
            XRWorkloadProbeBehaviorMetric metric = metrics[i];
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(SchemaVersion),
                Csv(AlgorithmVersion),
                Csv(block.participantId),
                block.sessionNumber.ToString(CultureInfo.InvariantCulture),
                Csv(block.conditionLabel),
                block.presentationOrder.ToString(CultureInfo.InvariantCulture),
                Csv(block.blockId),
                Csv(block.targetDimension),
                block.expectedTrials.ToString(CultureInfo.InvariantCulture),
                (trials?.trialCount ?? 0).ToString(CultureInfo.InvariantCulture),
                Csv(block.completionReason),
                F(sampleRateHz),
                observedSampleRate >= 0f ? F(observedSampleRate) : "",
                block.samples.Count.ToString(CultureInfo.InvariantCulture),
                B(collectEyeGaze),
                Csv(metric.dimension),
                Csv(metric.metricId),
                Csv(metric.metricName),
                metric.valid ? F(metric.value) : "",
                Csv(metric.unit),
                metric.valid ? "1" : "0",
                Csv(metric.source),
                Csv(metric.operationalDefinition),
                Csv(metric.qualityNote)
            }));
        }
        return sb.ToString();
    }

    string BuildRawSampleCsv(XRWorkloadProbeBehaviorBlock block)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "schemaVersion,participantId,sessionNumber,conditionLabel,presentationOrder,blockId,targetDimension," +
            "sampleIndex,elapsedSeconds,realtimeSeconds,trialActive,trialIndex," +
            "headValid,headX,headY,headZ,headQx,headQy,headQz,headQw," +
            "leftHandValid,leftHandX,leftHandY,leftHandZ,leftHandQx,leftHandQy,leftHandQz,leftHandQw," +
            "rightHandValid,rightHandX,rightHandY,rightHandZ,rightHandQx,rightHandQy,rightHandQz,rightHandQw," +
            "rayValid,rayOriginX,rayOriginY,rayOriginZ,rayDirectionX,rayDirectionY,rayDirectionZ," +
            "rayEndpointX,rayEndpointY,rayEndpointZ,rayProjectionDistanceMeters," +
            "gazeValid,gazeX,gazeY,gazeZ,gazeVelocityDegreesPerSecond");

        for (int i = 0; i < block.samples.Count; i++)
        {
            XRWorkloadProbeBehaviorSample sample = block.samples[i];
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(SchemaVersion), Csv(block.participantId),
                block.sessionNumber.ToString(CultureInfo.InvariantCulture),
                Csv(block.conditionLabel),
                block.presentationOrder.ToString(CultureInfo.InvariantCulture),
                Csv(block.blockId), Csv(block.targetDimension),
                sample.sampleIndex.ToString(CultureInfo.InvariantCulture),
                F(sample.elapsedSeconds), F(sample.realtimeSeconds),
                B(sample.trialActive),
                sample.trialIndex.ToString(CultureInfo.InvariantCulture),
                B(sample.headValid), V(sample.headValid, sample.headPosition.x), V(sample.headValid, sample.headPosition.y),
                V(sample.headValid, sample.headPosition.z), V(sample.headValid, sample.headRotation.x),
                V(sample.headValid, sample.headRotation.y), V(sample.headValid, sample.headRotation.z),
                V(sample.headValid, sample.headRotation.w),
                B(sample.leftHandValid), V(sample.leftHandValid, sample.leftHandPosition.x),
                V(sample.leftHandValid, sample.leftHandPosition.y), V(sample.leftHandValid, sample.leftHandPosition.z),
                V(sample.leftHandValid, sample.leftHandRotation.x), V(sample.leftHandValid, sample.leftHandRotation.y),
                V(sample.leftHandValid, sample.leftHandRotation.z), V(sample.leftHandValid, sample.leftHandRotation.w),
                B(sample.rightHandValid), V(sample.rightHandValid, sample.rightHandPosition.x),
                V(sample.rightHandValid, sample.rightHandPosition.y), V(sample.rightHandValid, sample.rightHandPosition.z),
                V(sample.rightHandValid, sample.rightHandRotation.x), V(sample.rightHandValid, sample.rightHandRotation.y),
                V(sample.rightHandValid, sample.rightHandRotation.z), V(sample.rightHandValid, sample.rightHandRotation.w),
                B(sample.rayValid), V(sample.rayValid, sample.rayOrigin.x),
                V(sample.rayValid, sample.rayOrigin.y), V(sample.rayValid, sample.rayOrigin.z),
                V(sample.rayValid, sample.rayDirection.x), V(sample.rayValid, sample.rayDirection.y),
                V(sample.rayValid, sample.rayDirection.z), V(sample.rayValid, sample.rayEndpoint.x),
                V(sample.rayValid, sample.rayEndpoint.y), V(sample.rayValid, sample.rayEndpoint.z),
                V(sample.rayValid, sample.rayProjectionDistanceMeters),
                B(sample.gazeValid), V(sample.gazeValid, sample.gazeDirection.x),
                V(sample.gazeValid, sample.gazeDirection.y), V(sample.gazeValid, sample.gazeDirection.z),
                sample.gazeVelocityDegreesPerSecond >= 0f ? F(sample.gazeVelocityDegreesPerSecond) : ""
            }));
        }
        return sb.ToString();
    }

    static float ObservedSampleRateHz(XRWorkloadProbeBehaviorBlock block)
    {
        if (block == null || block.samples.Count < 2)
            return -1f;
        float elapsed = block.samples[block.samples.Count - 1].realtimeSeconds -
                        block.samples[0].realtimeSeconds;
        return elapsed > 0f ? (block.samples.Count - 1) / elapsed : -1f;
    }

    static void WriteAtomic(string path, string content)
    {
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(true));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
    }

    static string SafeFilePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";
        var sb = new StringBuilder(value.Length);
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            sb.Append(Array.IndexOf(invalid, character) >= 0 || char.IsWhiteSpace(character) ? '_' : character);
        }
        return sb.ToString();
    }

    static string Csv(string value)
    {
        string safe = value ?? "";
        return '"' + safe.Replace("\"", "\"\"") + '"';
    }

    static string F(float value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    static string B(bool value)
    {
        return value ? "1" : "0";
    }

    static string V(bool valid, float value)
    {
        return valid ? F(value) : "";
    }
}
