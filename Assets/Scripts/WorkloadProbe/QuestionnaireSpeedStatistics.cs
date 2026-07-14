using System;
using System.Collections.Generic;

[Serializable]
public struct QuestionnaireSpeedDistribution
{
    public int count;
    public float median;
    public float mad;
    public float p90;
    public float p95;
    public float max;
}

public static class QuestionnaireSpeedStatistics
{
    public static QuestionnaireSpeedDistribution Calculate(IEnumerable<float> source)
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

        var result = new QuestionnaireSpeedDistribution
        {
            count = 0,
            median = -1f,
            mad = -1f,
            p90 = -1f,
            p95 = -1f,
            max = -1f
        };
        if (values.Count == 0)
            return result;

        values.Sort();
        result.count = values.Count;
        result.median = PercentileSorted(values, 0.5f);
        result.p90 = PercentileSorted(values, 0.9f);
        result.p95 = PercentileSorted(values, 0.95f);
        result.max = values[values.Count - 1];

        var deviations = new List<float>(values.Count);
        for (int i = 0; i < values.Count; i++)
            deviations.Add(Math.Abs(values[i] - result.median));
        deviations.Sort();
        result.mad = PercentileSorted(deviations, 0.5f);
        return result;
    }

    public static string CalibrationQuality(
        int validSlotEvents,
        int validPhysicalSamples,
        int minimumSlotEvents,
        int minimumPhysicalSamples)
    {
        int requiredSlots = Math.Max(1, minimumSlotEvents);
        int requiredPhysical = Math.Max(1, minimumPhysicalSamples);
        if (validSlotEvents >= requiredSlots && validPhysicalSamples >= requiredPhysical)
            return "ready";
        if (validSlotEvents >= Math.Max(3, requiredSlots / 2) &&
            validPhysicalSamples >= Math.Max(10, requiredPhysical / 2))
            return "limited";
        return "insufficient";
    }

    static float PercentileSorted(IReadOnlyList<float> sortedValues, float percentile)
    {
        if (sortedValues == null || sortedValues.Count == 0)
            return -1f;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        float boundedPercentile = Math.Max(0f, Math.Min(1f, percentile));
        float index = boundedPercentile * (sortedValues.Count - 1f);
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        if (lower == upper)
            return sortedValues[lower];
        float t = index - lower;
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * t;
    }
}
