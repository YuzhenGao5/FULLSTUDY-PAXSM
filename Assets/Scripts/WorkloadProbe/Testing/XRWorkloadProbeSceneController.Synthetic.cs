#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class XRWorkloadProbeSceneController
{
    sealed class SyntheticQuestionnaireState
    {
        public string key = "";
        public float startRealtime;
        public bool pointAndClick;
        public int scale;
        public int target;
        public readonly List<int> path = new List<int>();
        public int pathIndex;
        public float nextStepRealtime;
        public float movementFinishedRealtime = -1f;
        public float twistDegrees;
        public bool grabStarted;
        public bool clickIssued;
    }

    string _syntheticProbeTrialKey = "";
    float _syntheticProbeTrialStart;
    float _syntheticProbeTrialDuration;
    bool _syntheticProbeTimeout;
    bool _syntheticProbeWrong;

    string _syntheticReadKey = "";
    float _syntheticReadStart;
    float _syntheticReadDelay;

    SyntheticQuestionnaireState _syntheticQuestionnaire;

    bool SyntheticParticipantActive => CAREXRSyntheticParticipantRuntime.Active;

    void ApplySyntheticParticipantSettings()
    {
        if (!SyntheticParticipantActive)
            return;

        participantId = CAREXRSyntheticParticipantRuntime.ParticipantId;
        conditionLabel = "SYNTHETIC_HESITANT_FIRST_USE";
        writeCsvOnQuit = true;
        recordQuestionnairePersonalSpeed = true;
        Debug.Log(
            $"[CARE-XR Synthetic] Driving {gameObject.scene.name} as {participantId}; " +
            "these outputs are synthetic test data.",
            this);
    }

    bool UpdateSyntheticProbeTrial()
    {
        if (!SyntheticParticipantActive || !_trialActive || _currentTrial == null || _currentProfile == null)
            return false;

        string key = $"{_currentTrial.blockId}:{_currentTrial.trialIndex}";
        if (!string.Equals(key, _syntheticProbeTrialKey, StringComparison.Ordinal))
        {
            _syntheticProbeTrialKey = key;
            _syntheticProbeTrialStart = Time.realtimeSinceStartup;
            ConfigureSyntheticProbeOutcome(
                _currentProfile.blockId,
                _currentTrial.trialIndex,
                out _syntheticProbeTrialDuration,
                out _syntheticProbeTimeout,
                out _syntheticProbeWrong);
        }

        float elapsed = Time.realtimeSinceStartup - _syntheticProbeTrialStart;
        float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.1f, _syntheticProbeTrialDuration));
        int targetCount = Mathf.Max(1, _targets.Count);
        int finalIndex = _syntheticProbeWrong
            ? (_correctIndex + 1 + _currentTrial.trialIndex) % targetCount
            : _correctIndex;

        int hoverIndex;
        if (progress < 0.28f)
            hoverIndex = (_correctIndex + 1) % targetCount;
        else if (progress < 0.55f)
            hoverIndex = (_correctIndex + targetCount - 1) % targetCount;
        else if (progress < 0.74f && _currentTrial.trialIndex % 2 == 0)
            hoverIndex = (_correctIndex + 2) % targetCount;
        else
            hoverIndex = finalIndex;

        Vector3 head = new Vector3(
            0.012f * Mathf.Sin(Time.realtimeSinceStartup * 1.7f),
            1.62f + 0.008f * Mathf.Sin(Time.realtimeSinceStartup * 1.1f),
            0f);
        Vector3 left = new Vector3(-0.24f, 1.08f, 0.36f);
        Vector3 right = new Vector3(
            0.22f + 0.055f * Mathf.Sin(progress * Mathf.PI * 4f),
            1.11f + 0.035f * Mathf.Sin(progress * Mathf.PI * 3f),
            0.38f + 0.025f * Mathf.Cos(progress * Mathf.PI * 2f));

        Vector3 aimPoint = right + Vector3.forward * 2f;
        if (hoverIndex >= 0 && hoverIndex < _targets.Count && _targets[hoverIndex].gameObject != null)
            aimPoint = _targets[hoverIndex].gameObject.GetComponent<Collider>()?.bounds.center ??
                       _targets[hoverIndex].gameObject.transform.position;
        Vector3 direction = (aimPoint - right).normalized;
        Ray ray = new Ray(right, direction);
        TrackPointerMotion(right);
        HighlightHoveredTarget(ray);
        UpdateSelectionRayVisual(ray);
        SetSyntheticTrackedPose(head, left, right, direction, progress);

        if (elapsed >= _syntheticProbeTrialDuration)
        {
            FinishTrial(_syntheticProbeTimeout ? -1 : finalIndex, _syntheticProbeTimeout);
            _syntheticProbeTrialKey = "";
        }

        return true;
    }

    void ConfigureSyntheticProbeOutcome(
        string blockId,
        int trialIndex,
        out float duration,
        out bool timeout,
        out bool wrong)
    {
        timeout = false;
        wrong = false;
        duration = 0.72f + 0.08f * (trialIndex % 3);

        switch ((blockId ?? "").ToLowerInvariant())
        {
            case "baseline":
                duration = 0.68f + 0.07f * (trialIndex % 3);
                wrong = trialIndex == 6;
                break;
            case "cognitive_heavy":
                duration = 1.08f + 0.12f * (trialIndex % 4);
                wrong = trialIndex == 4 || trialIndex == 9;
                break;
            case "physical_heavy":
                duration = 1.02f + 0.10f * (trialIndex % 4);
                wrong = trialIndex == 5 || trialIndex == 10;
                break;
            case "temporal_heavy":
                timeout = trialIndex == 3 || trialIndex == 8;
                wrong = trialIndex == 5 || trialIndex == 10;
                duration = timeout && _currentProfile.timeLimitSeconds > 0f
                    ? _currentProfile.timeLimitSeconds + 0.03f
                    : 0.76f + 0.06f * (trialIndex % 3);
                break;
            case "combined_high":
                timeout = trialIndex == 4 || trialIndex == 9;
                wrong = trialIndex == 2 || trialIndex == 6 || trialIndex == 10;
                duration = timeout && _currentProfile.timeLimitSeconds > 0f
                    ? _currentProfile.timeLimitSeconds + 0.03f
                    : 1.15f + 0.10f * (trialIndex % 3);
                break;
        }
    }

    void BeginSyntheticQuestionnaireRead(QuestionnaireRecord record)
    {
        if (!SyntheticParticipantActive || record == null)
            return;

        _syntheticReadKey = $"{record.blockId}:{record.itemIndex}:{record.itemId}";
        _syntheticReadStart = Time.realtimeSinceStartup;
        _syntheticReadDelay = 0.42f + 0.09f * ((record.itemIndex + _blockIndex) % 4);
        CAREXRSyntheticParticipantRuntime.SetReadHeld(false);
    }

    bool SyntheticQuestionnaireReadHeld()
    {
        if (!SyntheticParticipantActive || string.IsNullOrEmpty(_syntheticReadKey))
            return false;

        float elapsed = Time.realtimeSinceStartup - _syntheticReadStart;
        bool held = elapsed >= _syntheticReadDelay;
        CAREXRSyntheticParticipantRuntime.SetReadHeld(held);
        return held;
    }

    void BeginSyntheticQuestionnaireStage(
        QuestionnaireRecord record,
        string stageName,
        int scale,
        bool pointAndClick)
    {
        if (!SyntheticParticipantActive || record == null)
            return;

        var state = new SyntheticQuestionnaireState
        {
            key = $"{record.blockId}:{record.itemIndex}:{record.itemId}:{stageName}",
            startRealtime = Time.realtimeSinceStartup,
            pointAndClick = pointAndClick,
            scale = scale,
            target = SyntheticQuestionnaireTarget(record, stageName, scale),
            nextStepRealtime = Time.realtimeSinceStartup + 0.28f
        };

        int start = _paxsmQuestionnaireKnobCore != null
            ? _paxsmQuestionnaireKnobCore.CurrentSlot
            : Mathf.CeilToInt(scale * 0.5f);
        BuildSyntheticQuestionnairePath(start, state.target, scale, state.path);
        _syntheticQuestionnaire = state;
        CAREXRSyntheticParticipantRuntime.SetQuestionnaireState(false, 0f, false);
    }

    void UpdateSyntheticKnobQuestionnaire()
    {
        SyntheticQuestionnaireState state = _syntheticQuestionnaire;
        if (!SyntheticParticipantActive || state == null || state.pointAndClick ||
            _paxsmQuestionnaireKnobCore == null)
            return;

        float now = Time.realtimeSinceStartup;
        float elapsed = now - state.startRealtime;
        if (elapsed < 0.24f)
        {
            CAREXRSyntheticParticipantRuntime.SetQuestionnaireState(false, state.twistDegrees, false);
            return;
        }

        if (state.pathIndex < state.path.Count)
        {
            if (!state.grabStarted)
            {
                state.grabStarted = true;
                state.nextStepRealtime = now + 0.055f;
                CAREXRSyntheticParticipantRuntime.SetQuestionnaireState(true, state.twistDegrees, false);
                return;
            }

            if (now >= state.nextStepRealtime)
            {
                int current = _paxsmQuestionnaireKnobCore.CurrentSlot;
                int desired = state.path[state.pathIndex];
                int delta = Math.Sign(desired - current);
                if (delta != 0)
                {
                    _paxsmQuestionnaireKnobCore.Step(delta);
                    float slotDegrees = state.scale <= 1
                        ? 0f
                        : questionnaireKnobArcDegrees / (state.scale - 1f);
                    state.twistDegrees += delta * slotDegrees;
                }
                if (_paxsmQuestionnaireKnobCore.CurrentSlot == desired)
                    state.pathIndex++;

                float pause = state.pathIndex == Mathf.Max(1, state.path.Count / 2) ? 0.24f : 0f;
                state.nextStepRealtime = now + 0.065f + pause;
            }

            float tremor = 0.35f * Mathf.Sin(now * 13f);
            CAREXRSyntheticParticipantRuntime.SetQuestionnaireState(
                true,
                state.twistDegrees + tremor,
                false);
            return;
        }

        if (state.movementFinishedRealtime < 0f)
            state.movementFinishedRealtime = now;

        float afterMovement = now - state.movementFinishedRealtime;
        bool grabbing = afterMovement < 0.18f;
        bool confirmHeld = afterMovement >= 0.42f;
        CAREXRSyntheticParticipantRuntime.SetQuestionnaireState(
            grabbing,
            state.twistDegrees,
            confirmHeld);
    }

    bool UpdateSyntheticPointClickQuestionnaire(
        out int hoverValue,
        out Ray ray,
        out Vector3 pointerOrigin)
    {
        hoverValue = -1;
        pointerOrigin = new Vector3(0.22f, 1.12f, 0.38f);
        ray = new Ray(pointerOrigin, Vector3.forward);
        SyntheticQuestionnaireState state = _syntheticQuestionnaire;
        if (!SyntheticParticipantActive || state == null || !state.pointAndClick)
            return false;

        float elapsed = Time.realtimeSinceStartup - state.startRealtime;
        if (elapsed >= 0.24f && elapsed < 0.48f)
            hoverValue = Mathf.Clamp(state.target + 2, 1, state.scale);
        else if (elapsed >= 0.48f && elapsed < 0.72f)
            hoverValue = Mathf.Clamp(state.target - 1, 1, state.scale);
        else if (elapsed >= 0.72f)
            hoverValue = state.target;

        float progress = Mathf.Clamp01(elapsed / 1.05f);
        pointerOrigin += new Vector3(
            0.05f * Mathf.Sin(progress * Mathf.PI * 3f),
            0.03f * Mathf.Sin(progress * Mathf.PI * 2f),
            0f);
        Vector3 aim = pointerOrigin + Vector3.forward * 3f;
        int targetIndex = hoverValue - 1;
        if (targetIndex >= 0 && targetIndex < _questionnairePointClickTargets.Count &&
            _questionnairePointClickTargets[targetIndex] != null)
        {
            aim = _questionnairePointClickTargets[targetIndex].GetComponent<Collider>()?.bounds.center ??
                  _questionnairePointClickTargets[targetIndex].transform.position;
        }
        Vector3 direction = (aim - pointerOrigin).normalized;
        ray = new Ray(pointerOrigin, direction);
        SetSyntheticTrackedPose(
            new Vector3(0f, 1.62f, 0f),
            new Vector3(-0.24f, 1.08f, 0.36f),
            pointerOrigin,
            direction,
            progress);

        bool click = !state.clickIssued && elapsed >= 1.05f;
        if (click)
            state.clickIssued = true;
        return click;
    }

    bool SyntheticQuestionnaireConfirmHeld()
    {
        return SyntheticParticipantActive && CAREXRSyntheticParticipantRuntime.QuestionnaireConfirmHeld;
    }

    bool SyntheticQuestionnaireGrabbing()
    {
        return SyntheticParticipantActive && CAREXRSyntheticParticipantRuntime.KnobGrabbing;
    }

    float SyntheticQuestionnaireTwistDegrees()
    {
        return SyntheticParticipantActive
            ? CAREXRSyntheticParticipantRuntime.KnobTwistDegrees
            : 0f;
    }

    int SyntheticQuestionnaireTarget(
        QuestionnaireRecord record,
        string stageName,
        int scale)
    {
        // Calibration trials have explicit intended values. Keep the synthetic run exact so
        // the P4444 walkthrough tests the same export/profile route a real target-entry run uses.
        if (string.Equals(stageName, "Answer", StringComparison.OrdinalIgnoreCase) &&
            record.expectedAnswerTarget > 0)
            return Mathf.Clamp(record.expectedAnswerTarget, 1, scale);
        if (string.Equals(stageName, "Confidence", StringComparison.OrdinalIgnoreCase) &&
            record.expectedConfidenceTarget > 0)
            return Mathf.Clamp(record.expectedConfidenceTarget, 1, scale);

        if (string.Equals(stageName, "Confidence", StringComparison.OrdinalIgnoreCase))
            return Mathf.Clamp(2 + ((record.itemIndex + _blockIndex) % 3), 1, scale);

        string block = (record.blockId ?? "").ToLowerInvariant();
        string item = (record.itemId ?? "").ToLowerInvariant();
        if (block.StartsWith("practice_input_", StringComparison.Ordinal) ||
            block.StartsWith("formal_input_", StringComparison.Ordinal))
        {
            int target = ParseTrailingNumber(item, Mathf.CeilToInt(scale * 0.5f));
            // Keep one realistic first-use miss in the formal PAXSM data for regression coverage.
            if (block.StartsWith("formal_input_", StringComparison.Ordinal) &&
                block.Contains("paxsm") && record.itemIndex == 1)
                target = Mathf.Clamp(target + 1, 1, scale);
            return target;
        }

        if (block.StartsWith("sus_", StringComparison.Ordinal))
        {
            int[] hesitantSus = { 4, 3, 4, 2, 4, 2, 4, 2, 3, 3 };
            return hesitantSus[Mathf.Clamp(record.itemIndex - 1, 0, hesitantSus.Length - 1)];
        }

        bool performance = item.Contains("performance");
        bool mental = item.Contains("mental");
        bool physical = item.Contains("physical");
        bool temporal = item.Contains("temporal");
        bool effort = item.Contains("effort");
        bool frustration = item.Contains("frustration");

        int value;
        if (block.Contains("combined"))
            value = performance ? 15 : (mental ? 17 : physical ? 14 : temporal ? 18 : effort ? 17 : 14);
        else if (block.Contains("cognitive") || block.Contains("mental"))
            value = performance ? 8 : (mental ? 16 : physical ? 4 : temporal ? 7 : effort ? 15 : 8);
        else if (block.Contains("physical_heavy"))
            value = performance ? 7 : (mental ? 7 : physical ? 16 : temporal ? 6 : effort ? 15 : 7);
        else if (block.Contains("temporal_heavy"))
            value = performance ? 11 : (mental ? 11 : physical ? 4 : temporal ? 18 : effort ? 14 : 12);
        else if (block.Contains("hard"))
            value = performance ? 8 : (mental ? 16 : physical ? 3 : temporal ? 12 : effort ? 16 : 9);
        else if (block.Contains("easy"))
            value = performance ? 3 : (mental ? 6 : physical ? 2 : temporal ? 4 : effort ? 7 : 3);
        else
            value = performance ? 4 : (mental ? 4 : physical ? 2 : temporal ? 3 : effort ? 5 : frustration ? 3 : 4);

        return Mathf.Clamp(value, 1, scale);
    }

    static int ParseTrailingNumber(string value, int fallback)
    {
        if (string.IsNullOrEmpty(value))
            return fallback;
        int end = value.Length - 1;
        while (end >= 0 && char.IsDigit(value[end]))
            end--;
        string digits = value.Substring(end + 1);
        return int.TryParse(digits, out int parsed) ? parsed : fallback;
    }

    static void BuildSyntheticQuestionnairePath(
        int start,
        int target,
        int scale,
        List<int> path)
    {
        path.Clear();
        start = Mathf.Clamp(start, 1, scale);
        target = Mathf.Clamp(target, 1, scale);
        int direction = target >= start ? 1 : -1;

        int falseStart = Mathf.Clamp(start - direction, 1, scale);
        if (falseStart != start)
        {
            path.Add(falseStart);
            path.Add(start);
        }

        int current = start;
        while (current != target)
        {
            current += Math.Sign(target - current);
            path.Add(current);
        }

        int overshoot = Mathf.Clamp(target + direction, 1, scale);
        if (overshoot != target)
        {
            path.Add(overshoot);
            path.Add(target);
        }
        else if (target > 1)
        {
            path.Add(target - 1);
            path.Add(target);
        }
    }

    void SetSyntheticTrackedPose(
        Vector3 head,
        Vector3 left,
        Vector3 right,
        Vector3 rayDirection,
        float progress)
    {
        Quaternion headRotation = Quaternion.Euler(
            1.5f * Mathf.Sin(progress * Mathf.PI * 2f),
            3f * Mathf.Sin(progress * Mathf.PI * 1.5f),
            0f);
        Quaternion rightRotation = Quaternion.LookRotation(
            rayDirection.sqrMagnitude > 0.000001f ? rayDirection : Vector3.forward,
            Vector3.up);
        CAREXRSyntheticParticipantRuntime.SetTrackedPose(
            head,
            headRotation,
            left,
            Quaternion.identity,
            right,
            rightRotation,
            right,
            rayDirection);
    }
}
#endif
