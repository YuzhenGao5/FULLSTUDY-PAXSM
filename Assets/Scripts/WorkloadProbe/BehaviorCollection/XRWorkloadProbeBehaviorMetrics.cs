using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class XRWorkloadProbeBehaviorSample
{
    public int sampleIndex;
    public float elapsedSeconds;
    public float realtimeSeconds;
    public bool trialActive;
    public int trialIndex;

    public bool headValid;
    public Vector3 headPosition;
    public Quaternion headRotation;

    public bool leftHandValid;
    public Vector3 leftHandPosition;
    public Quaternion leftHandRotation;

    public bool rightHandValid;
    public Vector3 rightHandPosition;
    public Quaternion rightHandRotation;

    public bool rayValid;
    public Vector3 rayOrigin;
    public Vector3 rayDirection;
    public Vector3 rayEndpoint;
    public float rayProjectionDistanceMeters;

    public bool gazeValid;
    public Vector3 gazeDirection;
    public float gazeVelocityDegreesPerSecond = -1f;
}

internal sealed class XRWorkloadProbeTrialSummary
{
    public bool valid;
    public int trialCount;
    public int correctCount;
    public int errorCount;
    public int timeoutCount;
    public float totalDecisionTime;
    public float trialSpanSeconds = -1f;

    public float Accuracy => trialCount > 0 ? correctCount / (float)trialCount : -1f;
    public float ErrorRate => trialCount > 0 ? errorCount / (float)trialCount : -1f;
    public float SuccessRate => Accuracy;
    public float ResponseSpeed => totalDecisionTime > 0f ? trialCount / totalDecisionTime : -1f;
}

internal sealed class XRWorkloadProbeBehaviorMetric
{
    public string dimension = "";
    public string metricId = "";
    public string metricName = "";
    public float value;
    public string unit = "";
    public bool valid;
    public string source = "";
    public string operationalDefinition = "";
    public string qualityNote = "";
}

internal sealed class XRWorkloadProbePoseAccumulator
{
    public int PositionSampleCount { get; private set; }
    public int RotationSampleCount { get; private set; }
    public float PositionPathMeters { get; private set; }
    public float RotationPathDegrees { get; private set; }
    public float PositionTrackedSeconds { get; private set; }

    Vector3 _lastPosition;
    Quaternion _lastRotation;
    float _lastPositionTime;
    float _lastRotationTime;
    bool _hasLastPosition;
    bool _hasLastRotation;

    public void Add(
        bool valid,
        Vector3 position,
        Quaternion rotation,
        float realtime,
        float maximumSampleGap,
        float maximumPositionStep)
    {
        if (!valid)
        {
            _hasLastPosition = false;
            _hasLastRotation = false;
            return;
        }

        PositionSampleCount++;
        RotationSampleCount++;

        if (_hasLastPosition)
        {
            float deltaTime = realtime - _lastPositionTime;
            float distance = Vector3.Distance(_lastPosition, position);
            if (deltaTime > 0f && deltaTime <= maximumSampleGap && distance <= maximumPositionStep)
            {
                PositionPathMeters += distance;
                PositionTrackedSeconds += deltaTime;
            }
        }

        if (_hasLastRotation)
        {
            float deltaTime = realtime - _lastRotationTime;
            if (deltaTime > 0f && deltaTime <= maximumSampleGap)
                RotationPathDegrees += Quaternion.Angle(_lastRotation, rotation);
        }

        _lastPosition = position;
        _lastRotation = rotation;
        _lastPositionTime = realtime;
        _lastRotationTime = realtime;
        _hasLastPosition = true;
        _hasLastRotation = true;
    }
}

internal sealed class XRWorkloadProbePathAccumulator
{
    public int SampleCount { get; private set; }
    public float PathMeters { get; private set; }
    public float TrackedSeconds { get; private set; }

    Vector3 _lastPosition;
    float _lastRealtime;
    bool _hasLastPosition;

    public void Add(
        bool valid,
        Vector3 position,
        float realtime,
        float maximumSampleGap,
        float maximumPositionStep)
    {
        if (!valid)
        {
            _hasLastPosition = false;
            return;
        }

        SampleCount++;
        if (_hasLastPosition)
        {
            float deltaTime = realtime - _lastRealtime;
            float distance = Vector3.Distance(_lastPosition, position);
            if (deltaTime > 0f && deltaTime <= maximumSampleGap && distance <= maximumPositionStep)
            {
                PathMeters += distance;
                TrackedSeconds += deltaTime;
            }
        }

        _lastPosition = position;
        _lastRealtime = realtime;
        _hasLastPosition = true;
    }
}

internal sealed class XRWorkloadProbeSpatialEntropyAccumulator
{
    readonly Dictionary<Vector3Int, int> _bins = new Dictionary<Vector3Int, int>();
    readonly float _binSizeMeters;

    public int SampleCount { get; private set; }

    public XRWorkloadProbeSpatialEntropyAccumulator(float binSizeMeters)
    {
        _binSizeMeters = Mathf.Max(0.01f, binSizeMeters);
    }

    public void Add(Vector3 position)
    {
        var key = new Vector3Int(
            Mathf.FloorToInt(position.x / _binSizeMeters),
            Mathf.FloorToInt(position.y / _binSizeMeters),
            Mathf.FloorToInt(position.z / _binSizeMeters));
        _bins.TryGetValue(key, out int count);
        _bins[key] = count + 1;
        SampleCount++;
    }

    public float EntropyBits()
    {
        if (SampleCount <= 0)
            return -1f;

        double entropy = 0d;
        foreach (int count in _bins.Values)
        {
            double probability = count / (double)SampleCount;
            entropy -= probability * (Math.Log(probability) / Math.Log(2d));
        }
        return (float)entropy;
    }
}

internal sealed class XRWorkloadProbeDirectionEntropyAccumulator
{
    readonly Dictionary<Vector2Int, int> _bins = new Dictionary<Vector2Int, int>();
    readonly float _binSizeDegrees;

    public int SampleCount { get; private set; }

    public XRWorkloadProbeDirectionEntropyAccumulator(float binSizeDegrees)
    {
        _binSizeDegrees = Mathf.Max(1f, binSizeDegrees);
    }

    public void Add(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.000001f)
            return;

        Vector3 normalized = direction.normalized;
        float yaw = Mathf.Atan2(normalized.x, normalized.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Asin(Mathf.Clamp(normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        var key = new Vector2Int(
            Mathf.FloorToInt((yaw + 180f) / _binSizeDegrees),
            Mathf.FloorToInt((pitch + 90f) / _binSizeDegrees));
        _bins.TryGetValue(key, out int count);
        _bins[key] = count + 1;
        SampleCount++;
    }

    public float EntropyBits()
    {
        if (SampleCount <= 0)
            return -1f;

        double entropy = 0d;
        foreach (int count in _bins.Values)
        {
            double probability = count / (double)SampleCount;
            entropy -= probability * (Math.Log(probability) / Math.Log(2d));
        }
        return (float)entropy;
    }
}

internal sealed class XRWorkloadProbeGazeAccumulator
{
    readonly float _fixationVelocityThreshold;
    readonly float _saccadeVelocityThreshold;
    readonly float _minimumFixationSeconds;
    readonly List<Vector3> _directions = new List<Vector3>();

    Vector3 _lastDirection;
    float _lastTime;
    bool _hasLast;
    bool _fixationActive;
    float _fixationStartTime;
    float _fixationPathDegrees;
    float _gazeVelocitySum;
    float _saccadeVelocitySum;
    float _fixationDurationSum;
    float _fixationPathSum;

    public int DirectionSampleCount => _directions.Count;
    public int VelocitySampleCount { get; private set; }
    public int SaccadeSampleCount { get; private set; }
    public int FixationCount { get; private set; }
    public float LatestVelocityDegreesPerSecond { get; private set; } = -1f;

    public XRWorkloadProbeGazeAccumulator(
        float fixationVelocityThreshold,
        float saccadeVelocityThreshold,
        float minimumFixationSeconds)
    {
        _fixationVelocityThreshold = Mathf.Max(1f, fixationVelocityThreshold);
        _saccadeVelocityThreshold = Mathf.Max(_fixationVelocityThreshold, saccadeVelocityThreshold);
        _minimumFixationSeconds = Mathf.Max(0.05f, minimumFixationSeconds);
    }

    public void Add(bool valid, Vector3 direction, float realtime, float maximumSampleGap)
    {
        if (!valid || direction.sqrMagnitude < 0.000001f)
        {
            EndFixation(_lastTime);
            _hasLast = false;
            LatestVelocityDegreesPerSecond = -1f;
            return;
        }

        direction.Normalize();
        _directions.Add(direction);

        if (_hasLast)
        {
            float deltaTime = realtime - _lastTime;
            if (deltaTime > 0f && deltaTime <= maximumSampleGap)
            {
                float angle = Vector3.Angle(_lastDirection, direction);
                float velocity = angle / deltaTime;
                LatestVelocityDegreesPerSecond = velocity;
                _gazeVelocitySum += velocity;
                VelocitySampleCount++;

                if (velocity >= _saccadeVelocityThreshold)
                {
                    _saccadeVelocitySum += velocity;
                    SaccadeSampleCount++;
                }

                if (velocity <= _fixationVelocityThreshold)
                {
                    if (!_fixationActive)
                    {
                        _fixationActive = true;
                        _fixationStartTime = _lastTime;
                        _fixationPathDegrees = 0f;
                    }
                    _fixationPathDegrees += angle;
                }
                else
                {
                    EndFixation(_lastTime);
                }
            }
            else
            {
                EndFixation(_lastTime);
            }
        }

        _lastDirection = direction;
        _lastTime = realtime;
        _hasLast = true;
    }

    public void Finish(float realtime)
    {
        EndFixation(Mathf.Max(realtime, _lastTime));
    }

    public float MeanGazeVelocity()
    {
        return VelocitySampleCount > 0 ? _gazeVelocitySum / VelocitySampleCount : -1f;
    }

    public float MeanSaccadeVelocity()
    {
        return SaccadeSampleCount > 0 ? _saccadeVelocitySum / SaccadeSampleCount : -1f;
    }

    public float MeanFixationDuration()
    {
        return FixationCount > 0 ? _fixationDurationSum / FixationCount : -1f;
    }

    public float MeanFixationPathLength()
    {
        return FixationCount > 0 ? _fixationPathSum / FixationCount : -1f;
    }

    public float AngularDispersionDegrees()
    {
        if (_directions.Count < 2)
            return -1f;

        Vector3 mean = Vector3.zero;
        for (int i = 0; i < _directions.Count; i++)
            mean += _directions[i];
        if (mean.sqrMagnitude < 0.000001f)
            return -1f;
        mean.Normalize();

        double squaredSum = 0d;
        for (int i = 0; i < _directions.Count; i++)
        {
            float angle = Vector3.Angle(mean, _directions[i]);
            squaredSum += angle * angle;
        }
        return Mathf.Sqrt((float)(squaredSum / _directions.Count));
    }

    void EndFixation(float endTime)
    {
        if (!_fixationActive)
            return;

        float duration = Mathf.Max(0f, endTime - _fixationStartTime);
        if (duration >= _minimumFixationSeconds)
        {
            FixationCount++;
            _fixationDurationSum += duration;
            _fixationPathSum += _fixationPathDegrees;
        }

        _fixationActive = false;
        _fixationPathDegrees = 0f;
    }
}

internal sealed class XRWorkloadProbeBehaviorBlock
{
    public readonly string participantId;
    public readonly int sessionNumber;
    public readonly string conditionLabel;
    public readonly int presentationOrder;
    public readonly string blockId;
    public readonly string targetDimension;
    public readonly int expectedTrials;
    public readonly int trialRecordStartIndex;
    public readonly float startRealtime;
    public readonly List<XRWorkloadProbeBehaviorSample> samples = new List<XRWorkloadProbeBehaviorSample>();
    public readonly XRWorkloadProbePoseAccumulator head = new XRWorkloadProbePoseAccumulator();
    public readonly XRWorkloadProbePoseAccumulator leftHand = new XRWorkloadProbePoseAccumulator();
    public readonly XRWorkloadProbePoseAccumulator rightHand = new XRWorkloadProbePoseAccumulator();
    public readonly XRWorkloadProbePathAccumulator rayEndpoint = new XRWorkloadProbePathAccumulator();
    public readonly XRWorkloadProbeSpatialEntropyAccumulator handEntropy;
    public readonly XRWorkloadProbeDirectionEntropyAccumulator headDirectionEntropy;
    public readonly XRWorkloadProbeGazeAccumulator gaze;

    public float endRealtime;
    public string completionReason = "";

    Vector3 _headStartPosition;
    bool _hasHeadStart;
    double _horizontalDriftSquaredSum;
    int _horizontalDriftCount;

    public XRWorkloadProbeBehaviorBlock(
        string participantId,
        int sessionNumber,
        string conditionLabel,
        int presentationOrder,
        string blockId,
        string targetDimension,
        int expectedTrials,
        int trialRecordStartIndex,
        float startRealtime,
        float handEntropyBinMeters,
        float directionEntropyBinDegrees,
        float fixationVelocityThreshold,
        float saccadeVelocityThreshold,
        float minimumFixationSeconds)
    {
        this.participantId = participantId;
        this.sessionNumber = sessionNumber;
        this.conditionLabel = conditionLabel;
        this.presentationOrder = presentationOrder;
        this.blockId = blockId;
        this.targetDimension = targetDimension;
        this.expectedTrials = expectedTrials;
        this.trialRecordStartIndex = trialRecordStartIndex;
        this.startRealtime = startRealtime;
        handEntropy = new XRWorkloadProbeSpatialEntropyAccumulator(handEntropyBinMeters);
        headDirectionEntropy = new XRWorkloadProbeDirectionEntropyAccumulator(directionEntropyBinDegrees);
        gaze = new XRWorkloadProbeGazeAccumulator(
            fixationVelocityThreshold,
            saccadeVelocityThreshold,
            minimumFixationSeconds);
    }

    public void AddSample(
        XRWorkloadProbeBehaviorSample sample,
        float maximumSampleGap,
        float maximumPositionStep,
        float maximumRayEndpointStep)
    {
        samples.Add(sample);
        head.Add(
            sample.headValid,
            sample.headPosition,
            sample.headRotation,
            sample.realtimeSeconds,
            maximumSampleGap,
            maximumPositionStep);
        leftHand.Add(
            sample.leftHandValid,
            sample.leftHandPosition,
            sample.leftHandRotation,
            sample.realtimeSeconds,
            maximumSampleGap,
            maximumPositionStep);
        rightHand.Add(
            sample.rightHandValid,
            sample.rightHandPosition,
            sample.rightHandRotation,
            sample.realtimeSeconds,
            maximumSampleGap,
            maximumPositionStep);
        rayEndpoint.Add(
            sample.rayValid,
            sample.rayEndpoint,
            sample.realtimeSeconds,
            maximumSampleGap,
            maximumRayEndpointStep);
        gaze.Add(sample.gazeValid, sample.gazeDirection, sample.realtimeSeconds, maximumSampleGap);
        sample.gazeVelocityDegreesPerSecond = gaze.LatestVelocityDegreesPerSecond;

        if (sample.leftHandValid)
            handEntropy.Add(sample.leftHandPosition);
        if (sample.rightHandValid)
            handEntropy.Add(sample.rightHandPosition);

        if (sample.headValid)
        {
            Vector3 headForward = sample.headRotation * Vector3.forward;
            headDirectionEntropy.Add(headForward);
            if (!_hasHeadStart)
            {
                _headStartPosition = sample.headPosition;
                _hasHeadStart = true;
            }

            Vector2 horizontalOffset = new Vector2(
                sample.headPosition.x - _headStartPosition.x,
                sample.headPosition.z - _headStartPosition.z);
            _horizontalDriftSquaredSum += horizontalOffset.sqrMagnitude;
            _horizontalDriftCount++;
        }
    }

    public void Finish(float realtime, string reason)
    {
        endRealtime = Mathf.Max(realtime, startRealtime);
        completionReason = reason ?? "";
        gaze.Finish(endRealtime);
    }

    public float HorizontalDriftRmsMeters()
    {
        return _horizontalDriftCount > 0
            ? Mathf.Sqrt((float)(_horizontalDriftSquaredSum / _horizontalDriftCount))
            : -1f;
    }

    public List<XRWorkloadProbeBehaviorMetric> BuildMetrics(XRWorkloadProbeTrialSummary trials)
    {
        var metrics = new List<XRWorkloadProbeBehaviorMetric>(29);
        bool headValid = head.PositionSampleCount >= 2;
        bool rightHandValid = rightHand.PositionSampleCount >= 2;
        bool anyHandValid = leftHand.PositionSampleCount >= 2 || rightHand.PositionSampleCount >= 2;
        bool handEntropyValid = handEntropy.SampleCount >= 2;
        bool headDirectionEntropyValid = headDirectionEntropy.SampleCount >= 2;
        bool gazeVelocityValid = gaze.VelocitySampleCount > 0;
        bool rayMovementValid = rayEndpoint.SampleCount >= 2 && rayEndpoint.TrackedSeconds > 0f;
        bool saccadeValid = gaze.SaccadeSampleCount > 0;
        bool fixationValid = gaze.FixationCount > 0;
        bool dispersionValid = gaze.DirectionSampleCount >= 2;
        bool trialValid = trials != null && trials.valid && trials.trialCount > 0;

        float handDistance = leftHand.PositionPathMeters + rightHand.PositionPathMeters;
        float completionTime = trialValid && trials.trialSpanSeconds >= 0f
            ? trials.trialSpanSeconds
            : Mathf.Max(0f, endRealtime - startRealtime);

        Add(metrics, "MD", "RayMovementDistance", "Ray Movement Distance", rayEndpoint.PathMeters, "m",
            rayMovementValid, "xr_right_controller_ray",
            "Cumulative path length of the raw right-controller ray endpoint projected to the block's nominal target distance.",
            rayMovementValid ? "" : "Insufficient valid right-controller ray samples in this experiment block.");
        Add(metrics, "MD", "GestureDistance", "Gesture Distance", rightHand.PositionPathMeters, "m",
            rightHandValid, "xr_right_hand",
            "Cumulative path length of the dominant right hand/controller during the experiment block.",
            TrackingNote(rightHandValid, "right-hand/controller"));
        Add(metrics, "MD", "GestureSpeed", "Gesture Speed",
            rightHand.PositionTrackedSeconds > 0f ? rightHand.PositionPathMeters / rightHand.PositionTrackedSeconds : 0f,
            "m/s", rightHandValid && rightHand.PositionTrackedSeconds > 0f, "xr_right_hand",
            "Dominant right-hand/controller path length divided by valid tracked time.",
            TrackingNote(rightHandValid && rightHand.PositionTrackedSeconds > 0f, "right-hand/controller"));
        Add(metrics, "MD", "GazeVelocity", "Gaze Velocity", gaze.MeanGazeVelocity(), "deg/s",
            gazeVelocityValid, "xr_eye_tracking",
            "Mean sample-to-sample angular velocity of the eye-tracking gaze direction.",
            EyeNote(gazeVelocityValid));
        Add(metrics, "MD", "SaccadeVelocity", "Saccade Velocity", gaze.MeanSaccadeVelocity(), "deg/s",
            saccadeValid, "xr_eye_tracking",
            "Mean gaze angular velocity for samples at or above the configured saccade threshold.",
            EyeNote(saccadeValid));
        Add(metrics, "MD", "FixationDuration", "Fixation Duration", gaze.MeanFixationDuration(), "s",
            fixationValid, "xr_eye_tracking",
            "Mean duration of low-velocity gaze episodes meeting the minimum fixation duration.",
            EyeNote(fixationValid));
        Add(metrics, "MD", "FixationPathLength", "Fixation Path Length", gaze.MeanFixationPathLength(), "deg",
            fixationValid, "xr_eye_tracking",
            "Mean cumulative angular gaze path within detected fixation episodes.",
            EyeNote(fixationValid));
        Add(metrics, "MD", "Dispersion", "Dispersion", gaze.AngularDispersionDegrees(), "deg_rms",
            dispersionValid, "xr_eye_tracking",
            "Root-mean-square angular deviation of gaze directions from the block mean direction.",
            EyeNote(dispersionValid));
        Add(metrics, "MD", "ManipulationError", "Manipulation Error",
            trialValid ? trials.errorCount : 0f, "count", trialValid, "probe_trial_records",
            "Number of trials ending in an incorrect selection or timeout.", TrialNote(trialValid));
        Add(metrics, "MD", "UnintendedPositionalDrift", "Unintended Positional Drift",
            HorizontalDriftRmsMeters(), "m_rms", headValid, "xr_hmd",
            "RMS horizontal HMD displacement from the first valid head position in the block.",
            TrackingNote(headValid, "HMD"));
        Add(metrics, "MD", "HeadDistance", "Head Distance", head.PositionPathMeters, "m",
            headValid, "xr_hmd", "Cumulative HMD positional path length.", TrackingNote(headValid, "HMD"));
        Add(metrics, "MD", "HeadRotation", "Head Rotation", head.RotationPathDegrees, "deg",
            headValid, "xr_hmd", "Cumulative sample-to-sample HMD rotation.", TrackingNote(headValid, "HMD"));
        Add(metrics, "MD", "HandDistance", "Hand Distance", handDistance, "m",
            anyHandValid, "xr_hands", "Combined cumulative path length of valid left and right hand/controller tracking.",
            TrackingNote(anyHandValid, "hand/controller"));
        Add(metrics, "MD", "HandEntropy", "Hand Entropy", handEntropy.EntropyBits(), "bits",
            handEntropyValid, "xr_hands",
            "Shannon entropy of left/right hand positions discretized into spatial voxels.",
            TrackingNote(handEntropyValid, "hand/controller"));
        Add(metrics, "MD", "HeadDirectionEntropy", "Head Direction Entropy", headDirectionEntropy.EntropyBits(), "bits",
            headDirectionEntropyValid, "xr_hmd",
            "Shannon entropy of HMD forward directions discretized into yaw/pitch bins.",
            TrackingNote(headDirectionEntropyValid, "HMD"));

        Add(metrics, "PD", "Accuracy", "Accuracy", trialValid ? trials.Accuracy : 0f, "proportion",
            trialValid, "probe_trial_records", "Correct trials divided by all completed trials.", TrialNote(trialValid));
        Add(metrics, "PD", "ErrorRate", "Error Rate", trialValid ? trials.ErrorRate : 0f, "proportion",
            trialValid, "probe_trial_records", "Incorrect selections and timeouts divided by all completed trials.",
            TrialNote(trialValid));
        Add(metrics, "PD", "SuccessRate", "Success Rate", trialValid ? trials.SuccessRate : 0f, "proportion",
            trialValid, "probe_trial_records", "Successfully completed correct trials divided by all completed trials.",
            TrialNote(trialValid));
        Add(metrics, "PD", "NavigationAccuracy", "Navigation Accuracy", 0f, "proportion", false,
            "not_applicable",
            "Navigation accuracy requires a defined route or destination error measure.",
            "Not applicable: XRWorkloadProbe is a stationary target-selection task with no navigation route.");
        Add(metrics, "PD", "HeadDistance", "Head Distance", head.PositionPathMeters, "m",
            headValid, "xr_hmd", "Cumulative HMD positional path length.", TrackingNote(headValid, "HMD"));
        Add(metrics, "PD", "HeadRotation", "Head Rotation", head.RotationPathDegrees, "deg",
            headValid, "xr_hmd", "Cumulative sample-to-sample HMD rotation.", TrackingNote(headValid, "HMD"));
        Add(metrics, "PD", "HandEntropy", "Hand Entropy", handEntropy.EntropyBits(), "bits",
            handEntropyValid, "xr_hands",
            "Shannon entropy of left/right hand positions discretized into spatial voxels.",
            TrackingNote(handEntropyValid, "hand/controller"));
        Add(metrics, "PD", "HeadDirectionEntropy", "Head Direction Entropy", headDirectionEntropy.EntropyBits(), "bits",
            headDirectionEntropyValid, "xr_hmd",
            "Shannon entropy of HMD forward directions discretized into yaw/pitch bins.",
            TrackingNote(headDirectionEntropyValid, "HMD"));

        Add(metrics, "TD", "GestureDistance", "Gesture Distance", rightHand.PositionPathMeters, "m",
            rightHandValid, "xr_right_hand",
            "Cumulative path length of the dominant right hand/controller during the experiment block.",
            TrackingNote(rightHandValid, "right-hand/controller"));
        Add(metrics, "TD", "GestureSpeed", "Gesture Speed",
            rightHand.PositionTrackedSeconds > 0f ? rightHand.PositionPathMeters / rightHand.PositionTrackedSeconds : 0f,
            "m/s", rightHandValid && rightHand.PositionTrackedSeconds > 0f, "xr_right_hand",
            "Dominant right-hand/controller path length divided by valid tracked time.",
            TrackingNote(rightHandValid && rightHand.PositionTrackedSeconds > 0f, "right-hand/controller"));
        Add(metrics, "TD", "HeadMovement", "Head Movement", head.PositionPathMeters, "m",
            headValid, "xr_hmd", "Cumulative HMD positional path length during the experiment block.",
            TrackingNote(headValid, "HMD"));
        Add(metrics, "TD", "CompletionTime", "Completion Time", completionTime, "s",
            completionTime >= 0f, trialValid ? "probe_trial_records" : "collector_clock",
            "Elapsed time from the first trial cue to the final trial response/timeout.",
            trialValid ? "" : "Collector clock fallback used because trial records were unavailable.");
        Add(metrics, "TD", "TravelTime", "Travel Time", 0f, "s", false, "not_applicable",
            "Travel time requires a navigation start and destination.",
            "Not applicable: XRWorkloadProbe contains no navigation segment.");
        Add(metrics, "TD", "ResponseSpeed", "Response Speed",
            trialValid ? trials.ResponseSpeed : 0f, "responses/s",
            trialValid && trials.ResponseSpeed >= 0f, "probe_trial_records",
            "Completed trials divided by the sum of trial decision times (inverse mean decision time).",
            TrialNote(trialValid && trials.ResponseSpeed >= 0f));

        return metrics;
    }

    static void Add(
        List<XRWorkloadProbeBehaviorMetric> metrics,
        string dimension,
        string metricId,
        string metricName,
        float value,
        string unit,
        bool valid,
        string source,
        string operationalDefinition,
        string qualityNote)
    {
        metrics.Add(new XRWorkloadProbeBehaviorMetric
        {
            dimension = dimension,
            metricId = metricId,
            metricName = metricName,
            value = value,
            unit = unit,
            valid = valid,
            source = source,
            operationalDefinition = operationalDefinition,
            qualityNote = qualityNote
        });
    }

    static string TrackingNote(bool valid, string device)
    {
        return valid ? "" : $"Insufficient valid {device} tracking samples in this experiment block.";
    }

    static string EyeNote(bool valid)
    {
        return valid ? "" : "No sufficient XR eye-tracking gaze data were available; HMD direction was not substituted.";
    }

    static string TrialNote(bool valid)
    {
        return valid ? "" : "No readable completed trial records were available for this experiment block.";
    }
}
