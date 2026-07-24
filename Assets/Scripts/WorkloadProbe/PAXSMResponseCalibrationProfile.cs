using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class PAXSMCalibrationMetricReference
{
    public string metric = "";
    public string units = "";
    public int sampleCount;
    public float median = -1f;
    public float mad = -1f;
    public float robustSigma = -1f;
    public float p10 = -1f;
    public float p25 = -1f;
    public float p90 = -1f;
    public float p95 = -1f;
    public float lowerReference = -1f;
    public float upperReference = -1f;
}

[Serializable]
public sealed class PAXSMCalibrationDistanceBinSummary
{
    public string binId = "";
    public string displayName = "";
    public int minimumSlotDistance;
    public int maximumSlotDistance;
    public int referenceTrialCount;
    public PAXSMCalibrationMetricReference decisionRt = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference firstInteractionRt = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference maxAbsVelocity = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference maxFlickVelocity = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference totalAbsAngle = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference pathRatio = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference pauseRate = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference reverseCount = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference microAdjustCount = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference correctionRate = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference fastFlickCount = new PAXSMCalibrationMetricReference();
}

[Serializable]
public sealed class PAXSMCalibrationStageSummary
{
    public int referenceTrialCount;
    public int validSlotSpeedEventCount;
    public int validPhysicalSpeedSampleCount;
    public PAXSMCalibrationMetricReference decisionRt = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference firstInteractionRt = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference maxAbsVelocity = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference maxFlickVelocity = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference physicalAngularSpeed = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference totalAbsAngle = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference pathRatio = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference slotChangeCount = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference pauseCount = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference pauseRate = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference reverseCount = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference microAdjustCount = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference correctionRate = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference fastFlickCount = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference grabCount = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationMetricReference confirmCancelCount = new PAXSMCalibrationMetricReference();
    public List<PAXSMCalibrationDistanceBinSummary> distanceBins = new List<PAXSMCalibrationDistanceBinSummary>();
}

[Serializable]
public sealed class PAXSMCalibrationConditionSummary
{
    public string conditionId = "";
    public string instructionLabel = "";
    public int completedTrials;
    public int answerTargetCorrect;
    public int confidenceTargetCorrect;
    public int referenceTrialCount;
    public float answerTargetAccuracy;
    public float confidenceTargetAccuracy;
    public PAXSMCalibrationMetricReference readRt = new PAXSMCalibrationMetricReference();
    public PAXSMCalibrationStageSummary answer = new PAXSMCalibrationStageSummary();
    public PAXSMCalibrationStageSummary confidence = new PAXSMCalibrationStageSummary();
}

[Serializable]
public sealed class PAXSMResponsePatternThresholds
{
    public string thresholdVersion = "PAXSM_PersonalKnobThresholds_v2";
    public string fastRtRule = "Decision time is exported for audit but is not used as the core accelerated-pattern rule.";
    public string highSpeedRule = "Current maximum angular velocity is above this participant's personal reference range for the same movement-distance bin.";
    public string extraPathRule = "Current path ratio is above this participant's personal reference range.";
    public string highCorrectionRule = "Current correction rate is above this participant's personal reference range.";
    public string directPathRule = "Path ratio <= directPathRatioMax; this is a structural path-efficiency rule, not a participant label.";
    public string lowCorrectionRule = "Current correction rate is at or below this participant's personal lower reference; it is only one component of a researcher-facing review cue.";
    public float directPathRatioMax = 1.2f;
    public int lowCorrectionCountMax = 1;
    public float answerFastDecisionRtBelowSec = -1f;
    public float confidenceFastDecisionRtBelowSec = -1f;
    public float answerHighMaxAbsVelocityAbove = -1f;
    public float confidenceHighMaxAbsVelocityAbove = -1f;
    public float answerHighPhysicalAngularSpeedAboveDps = -1f;
    public float confidenceHighPhysicalAngularSpeedAboveDps = -1f;
    public float answerExtraPathRatioAbove = -1f;
    public float confidenceExtraPathRatioAbove = -1f;
    public float answerHighCorrectionRateAbove = -1f;
    public float confidenceHighCorrectionRateAbove = -1f;
    public float answerLowCorrectionRateAtOrBelow = -1f;
    public float confidenceLowCorrectionRateAtOrBelow = -1f;
}

[Serializable]
public sealed class PAXSMResponseCalibrationProfile
{
    public string schemaVersion = "PAXSM_PersonalKnobProfile_v2";
    public string profileVersion = "2.0";
    public string participantId = "";
    public int sessionNumber;
    public string sourceScene = "";
    public string runId = "";
    public string generatedUtc = "";
    public string completionReason = "";
    public bool calibrationComplete;
    public bool profileReady;
    public string profileQuality = "insufficient";
    public string profilePurpose =
        "Participant-relative response-process reference for researcher-facing review cues; not a careless-response label or exclusion decision.";
    public int expectedTrials;
    public int minimumReferenceTrials;
    public float minimumTargetAccuracy;
    public int minimumValidSlotEvents;
    public int minimumValidPhysicalSamples;
    public PAXSMCalibrationConditionSummary personalReference = new PAXSMCalibrationConditionSummary();
    public PAXSMResponsePatternThresholds responsePatternThresholds = new PAXSMResponsePatternThresholds();
    public string calibrationNotes = "";
}

public static class PAXSMCalibrationStatistics
{
    const float MadToSigma = 1.4826f;

    public static PAXSMCalibrationMetricReference CreateReference(
        string metric,
        string units,
        IEnumerable<float> source,
        float minimumSpread,
        float referenceMultiplier = 2f)
    {
        var values = new List<float>();
        if (source != null)
        {
            foreach (float value in source)
            {
                if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f)
                    values.Add(value);
            }
        }

        var reference = new PAXSMCalibrationMetricReference
        {
            metric = metric ?? "",
            units = units ?? "",
            sampleCount = values.Count
        };
        if (values.Count == 0)
            return reference;

        values.Sort();
        reference.median = Percentile(values, 0.5f);
        reference.p10 = Percentile(values, 0.1f);
        reference.p25 = Percentile(values, 0.25f);
        reference.p90 = Percentile(values, 0.9f);
        reference.p95 = Percentile(values, 0.95f);

        var deviations = new List<float>(values.Count);
        for (int i = 0; i < values.Count; i++)
            deviations.Add(Math.Abs(values[i] - reference.median));
        deviations.Sort();
        reference.mad = Percentile(deviations, 0.5f);
        reference.robustSigma = reference.mad * MadToSigma;

        float spread = Math.Max(Math.Max(0f, minimumSpread), reference.robustSigma);
        float multiplier = Math.Max(0.1f, referenceMultiplier);
        reference.lowerReference = Math.Max(0f, reference.median - multiplier * spread);
        reference.upperReference = reference.median + multiplier * spread;
        return reference;
    }

    public static float Percentile(IReadOnlyList<float> sortedValues, float percentile)
    {
        if (sortedValues == null || sortedValues.Count == 0)
            return -1f;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        float bounded = Mathf.Clamp01(percentile);
        float index = bounded * (sortedValues.Count - 1f);
        int lower = Mathf.FloorToInt(index);
        int upper = Mathf.CeilToInt(index);
        if (lower == upper)
            return sortedValues[lower];
        return Mathf.Lerp(sortedValues[lower], sortedValues[upper], index - lower);
    }

    public static float Rate(int count, float durationSeconds)
    {
        return count / Mathf.Max(0.1f, durationSeconds);
    }
}
