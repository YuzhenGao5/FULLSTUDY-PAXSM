using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class QuestionnaireTraceSample
{
    public float realtime;
    public int slot;
    public float angle;
    public string sampleType = "";
    public string source = "";
    public bool isAnchor;
}

[Serializable]
public class QuestionnaireStageMetrics
{
    public int tickCount;
    public int currentSlot = -1;
    public float currentAngleY;
    public int slotChangeCount;
    public int reverseCount;
    public int pauseCount;
    public int confirmCount;
    public int minSlot = -1;
    public int maxSlot = -1;
    public int uniqueSlotsVisited;
    public int stillEpisodeCount;
    public float stillOverThresholdSum;
    public float stillTimeSum;
    public float microAdjustTimeSum;
    public int microAdjustCount;
    public float normalAdjustTimeSum;
    public int normalAdjustCount;
    public float flickTimeSum;
    public int fastFlickCount;
    public float maxFlickVel;
    public float maxAbsVel;
    public float activeMoveTimeSum;
    public int activeMoveCount;
    public float totalAbsAngle;
    public bool speedBandValid;
    public float speedMedian = -1f;
    public float speedMAD = -1f;
    public float speedThLow = -1f;
    public float speedThHigh = -1f;
    public string speedBandNote = "";

    public int SlotSpan => minSlot > 0 && maxSlot >= minSlot ? maxSlot - minSlot : -1;
}

public static class QuestionnaireTraceAnalyzer
{
    public const string AlgorithmVersion = "PAXSM_QuestionnaireTrace_v2.0";

    [Serializable]
    public class Settings
    {
        public float pauseThresholdSec = 0.2f;
        public float stillThresholdSec = 0.25f;
        public float fastFlickThresholdSps = 15f;
        public float speedDeltaMin = 1f;
        public float speedDeltaK = 1.5f;
        public int speedBandMinimumEpisodes = 3;
        public int microMinimumTransitions = 4;
        public int microMaximumSlotSpan = 2;
    }

    class MoveGap
    {
        public int fromSlot;
        public int toSlot;
        public int steps;
        public int direction;
        public float dwellBefore;
        public float moveDuration;
        public bool startsAfterPause;
    }

    class Episode
    {
        public readonly List<MoveGap> gaps = new List<MoveGap>();
        public int totalSteps;
        public float duration;
        public int minSlot = int.MaxValue;
        public int maxSlot = int.MinValue;
    }

    public static QuestionnaireStageMetrics Analyze(
        IReadOnlyList<QuestionnaireTraceSample> samples,
        int tickCount,
        float minAngle,
        float maxAngle,
        Settings settings)
    {
        settings ??= new Settings();
        var result = new QuestionnaireStageMetrics { tickCount = Mathf.Max(0, tickCount) };
        if (samples == null || samples.Count == 0)
        {
            result.speedBandNote = "no_trace_samples";
            return result;
        }

        float pauseThreshold = Mathf.Max(0.01f, settings.pauseThresholdSec);
        float stillThreshold = Mathf.Max(0.01f, settings.stillThresholdSec);
        float degreesPerSlot = tickCount > 1
            ? Mathf.Abs(maxAngle - minAngle) / (tickCount - 1f)
            : 0f;
        var visited = new HashSet<int>();
        var moveGaps = new List<MoveGap>();

        QuestionnaireTraceSample first = FirstValidSample(samples);
        if (first == null)
        {
            result.speedBandNote = "no_valid_slots";
            return result;
        }

        int runSlot = first.slot;
        float runStart = first.realtime;
        float lastSame = first.realtime;
        bool runHasIdleSamples = false;
        TouchSlot(result, visited, runSlot);

        int previousDirection = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            QuestionnaireTraceSample sample = samples[i];
            if (sample == null || sample.slot <= 0 || sample.realtime < runStart)
                continue;

            TouchSlot(result, visited, sample.slot);
            if (sample.slot == runSlot)
            {
                if (sample.realtime > lastSame + 0.000001f)
                {
                    lastSame = sample.realtime;
                    runHasIdleSamples = true;
                }
                continue;
            }

            float totalDuration = Mathf.Max(0f, sample.realtime - runStart);
            float dwell = runHasIdleSamples
                ? Mathf.Max(0f, lastSame - runStart)
                : totalDuration;
            float moveDuration = runHasIdleSamples
                ? Mathf.Max(0.0001f, sample.realtime - lastSame)
                : Mathf.Max(0.0001f, totalDuration);

            CountDwell(result, dwell, pauseThreshold, stillThreshold);

            int delta = sample.slot - runSlot;
            int direction = Math.Sign(delta);
            int steps = Mathf.Abs(delta);
            moveGaps.Add(new MoveGap
            {
                fromSlot = runSlot,
                toSlot = sample.slot,
                steps = steps,
                direction = direction,
                dwellBefore = dwell,
                moveDuration = moveDuration,
                startsAfterPause = dwell >= pauseThreshold
            });

            result.slotChangeCount++;
            result.totalAbsAngle += steps * degreesPerSlot;
            if (previousDirection != 0 && direction != previousDirection)
                result.reverseCount++;
            previousDirection = direction;

            runSlot = sample.slot;
            runStart = sample.realtime;
            lastSame = sample.realtime;
            runHasIdleSamples = false;
        }

        QuestionnaireTraceSample last = LastValidSample(samples);
        if (last != null)
            CountDwell(result, Mathf.Max(0f, last.realtime - runStart), pauseThreshold, stillThreshold);

        result.currentSlot = last != null ? last.slot : runSlot;
        result.currentAngleY = SlotAngle(result.currentSlot, tickCount, minAngle, maxAngle);
        result.uniqueSlotsVisited = visited.Count;

        List<Episode> episodes = BuildEpisodes(moveGaps);
        var episodeSpeeds = new List<float>(episodes.Count);
        for (int i = 0; i < episodes.Count; i++)
        {
            Episode episode = episodes[i];
            if (episode.gaps.Count == 0 || episode.totalSteps <= 0)
                continue;

            float duration = Mathf.Max(0.0001f, episode.duration);
            float speed = episode.totalSteps / duration;
            episodeSpeeds.Add(speed);
            result.activeMoveCount++;
            result.activeMoveTimeSum += duration;
            result.maxAbsVel = Mathf.Max(result.maxAbsVel, speed);

            if (speed >= Mathf.Max(0.01f, settings.fastFlickThresholdSps))
            {
                result.fastFlickCount++;
                result.flickTimeSum += duration;
                result.maxFlickVel = Mathf.Max(result.maxFlickVel, speed);
            }
            else
            {
                result.normalAdjustCount++;
                result.normalAdjustTimeSum += duration;
            }

            if (IsMicroAdjustment(episode, settings, speed))
            {
                result.microAdjustCount++;
                result.microAdjustTimeSum += duration;
            }
        }

        ComputeSpeedBand(result, episodeSpeeds, settings);
        return result;
    }

    static List<Episode> BuildEpisodes(List<MoveGap> gaps)
    {
        var episodes = new List<Episode>();
        Episode current = null;
        for (int i = 0; i < gaps.Count; i++)
        {
            MoveGap gap = gaps[i];
            if (current == null || gap.startsAfterPause)
            {
                if (current != null && current.gaps.Count > 0)
                    episodes.Add(current);
                current = new Episode();
            }

            current.gaps.Add(gap);
            current.totalSteps += gap.steps;
            current.duration += Mathf.Max(0.0001f, gap.moveDuration);
            current.minSlot = Mathf.Min(current.minSlot, gap.fromSlot, gap.toSlot);
            current.maxSlot = Mathf.Max(current.maxSlot, gap.fromSlot, gap.toSlot);
        }

        if (current != null && current.gaps.Count > 0)
            episodes.Add(current);
        return episodes;
    }

    static bool IsMicroAdjustment(Episode episode, Settings settings, float speed)
    {
        if (episode.gaps.Count < Mathf.Max(2, settings.microMinimumTransitions))
            return false;
        if (episode.maxSlot - episode.minSlot > Mathf.Max(1, settings.microMaximumSlotSpan))
            return false;
        if (speed >= Mathf.Max(0.01f, settings.fastFlickThresholdSps))
            return false;

        int reversals = 0;
        int previousDirection = 0;
        for (int i = 0; i < episode.gaps.Count; i++)
        {
            MoveGap gap = episode.gaps[i];
            if (gap.steps != 1)
                return false;
            if (previousDirection != 0 && gap.direction != previousDirection)
                reversals++;
            previousDirection = gap.direction;
        }

        int startSlot = episode.gaps[0].fromSlot;
        int endSlot = episode.gaps[episode.gaps.Count - 1].toSlot;
        return reversals > 0 && Mathf.Abs(endSlot - startSlot) <= 1;
    }

    static void ComputeSpeedBand(
        QuestionnaireStageMetrics result,
        List<float> speeds,
        Settings settings)
    {
        if (speeds.Count < Mathf.Max(2, settings.speedBandMinimumEpisodes))
        {
            result.speedBandNote = $"insufficient_episodes:{speeds.Count}";
            return;
        }

        speeds.Sort();
        float median = MedianSorted(speeds);
        var deviations = new List<float>(speeds.Count);
        for (int i = 0; i < speeds.Count; i++)
            deviations.Add(Mathf.Abs(speeds[i] - median));
        deviations.Sort();
        float mad = MedianSorted(deviations);
        float delta = Mathf.Max(Mathf.Max(0f, settings.speedDeltaMin), Mathf.Max(0f, settings.speedDeltaK) * mad);

        result.speedBandValid = true;
        result.speedMedian = median;
        result.speedMAD = mad;
        result.speedThLow = Mathf.Max(0f, median - delta);
        result.speedThHigh = Mathf.Max(0.01f, settings.fastFlickThresholdSps);
        result.speedBandNote = $"median_mad_episode_speed;flick_fixed={result.speedThHigh:F2}";
    }

    static float MedianSorted(List<float> values)
    {
        if (values == null || values.Count == 0)
            return -1f;
        int middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) * 0.5f;
    }

    static void CountDwell(
        QuestionnaireStageMetrics result,
        float dwell,
        float pauseThreshold,
        float stillThreshold)
    {
        if (dwell >= pauseThreshold)
            result.pauseCount++;
        if (dwell < stillThreshold)
            return;
        result.stillEpisodeCount++;
        result.stillTimeSum += dwell;
        result.stillOverThresholdSum += dwell - stillThreshold;
    }

    static void TouchSlot(
        QuestionnaireStageMetrics result,
        HashSet<int> visited,
        int slot)
    {
        if (slot <= 0)
            return;
        visited.Add(slot);
        result.minSlot = result.minSlot < 0 ? slot : Mathf.Min(result.minSlot, slot);
        result.maxSlot = result.maxSlot < 0 ? slot : Mathf.Max(result.maxSlot, slot);
    }

    static QuestionnaireTraceSample FirstValidSample(IReadOnlyList<QuestionnaireTraceSample> samples)
    {
        for (int i = 0; i < samples.Count; i++)
            if (samples[i] != null && samples[i].slot > 0)
                return samples[i];
        return null;
    }

    static QuestionnaireTraceSample LastValidSample(IReadOnlyList<QuestionnaireTraceSample> samples)
    {
        for (int i = samples.Count - 1; i >= 0; i--)
            if (samples[i] != null && samples[i].slot > 0)
                return samples[i];
        return null;
    }

    static float SlotAngle(int slot, int scale, float minAngle, float maxAngle)
    {
        if (scale <= 1 || slot <= 0)
            return 0f;
        float t = (Mathf.Clamp(slot, 1, scale) - 1f) / (scale - 1f);
        return Mathf.Lerp(minAngle, maxAngle, t);
    }
}
