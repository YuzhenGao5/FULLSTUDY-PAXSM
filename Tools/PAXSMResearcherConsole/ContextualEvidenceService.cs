using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PAXSMResearcherConsole;

internal sealed class ContextualResponseRecord
{
    public string ParticipantId { get; set; } = "";
    public int SessionNumber { get; set; }
    public string BlockId { get; set; } = "";
    public int PresentationOrder { get; set; }
    public string ItemId { get; set; } = "";
    public string ItemDimension { get; set; } = "";
    public int SelectedScore { get; set; }
    public int Confidence { get; set; }
    public double? BaselineScore { get; set; }
    public string XPattern { get; set; } = "";
    public string XEvidence { get; set; } = "";
    public string ConfidencePattern { get; set; } = "";
    public string YPattern { get; set; } = "";
    public double ProbeMatchRatio { get; set; }
    public string YEvidence { get; set; } = "";
    public string ScoreContext { get; set; } = "";
    public string PluginName { get; set; } = "";
    public string SourceQuestionnairePath { get; set; } = "";
    public string SourceCombinedMetricsPath { get; set; } = "";
    public string SourceBaselineMetricsPath { get; set; } = "";
    public string SourceResponseProfilePath { get; set; } = "";
}

internal sealed class ContextualEvidenceResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string OutputPath { get; init; } = "";
    public IReadOnlyList<ContextualResponseRecord> Records { get; init; } = Array.Empty<ContextualResponseRecord>();
}

internal sealed class StagePatternResult
{
    public string Category { get; init; } = "reviewable";
    public string Evidence { get; init; } = "";
}

internal sealed class ProbeMatchResult
{
    public string Category { get; init; } = "none";
    public double Ratio { get; init; }
    public string Evidence { get; init; } = "";
}

internal sealed class ResponsePatternProfile
{
    public double DirectPathRatioMax { get; init; } = 1.2d;
    public int LowCorrectionCountMax { get; init; } = 1;
    public double AnswerFastRt { get; init; } = double.NaN;
    public double ConfidenceFastRt { get; init; } = double.NaN;
    public double AnswerHighSpeed { get; init; } = double.NaN;
    public double ConfidenceHighSpeed { get; init; } = double.NaN;
    public double AnswerExtraPath { get; init; } = double.NaN;
    public double ConfidenceExtraPath { get; init; } = double.NaN;
    public double AnswerHighCorrection { get; init; } = double.NaN;
    public double ConfidenceHighCorrection { get; init; } = double.NaN;
    public double AnswerLowCorrection { get; init; } = double.NaN;
    public double ConfidenceLowCorrection { get; init; } = double.NaN;
    public double AnswerSlowRt { get; init; } = double.NaN;
    public double ConfidenceSlowRt { get; init; } = double.NaN;
    public IReadOnlyList<PersonalDistanceThreshold> AnswerDistanceThresholds { get; init; } = Array.Empty<PersonalDistanceThreshold>();
    public IReadOnlyList<PersonalDistanceThreshold> ConfidenceDistanceThresholds { get; init; } = Array.Empty<PersonalDistanceThreshold>();

    public static ResponsePatternProfile? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            JsonElement thresholds = GetObject(root, "responsePatternThresholds");
            JsonElement personal = GetObject(root, "personalReference");
            // Read old pilot files too, but treat their careful block only as the one available personal reference.
            if (personal.ValueKind != JsonValueKind.Object)
                personal = GetObject(root, "carefulReference");
            JsonElement personalAnswer = GetObject(personal, "answer");
            JsonElement personalConfidence = GetObject(personal, "confidence");
            JsonElement answerSpeed = GetObject(personalAnswer, "maxAbsVelocity");
            JsonElement confidenceSpeed = GetObject(personalConfidence, "maxAbsVelocity");
            JsonElement answerCorrection = GetObject(personalAnswer, "correctionRate");
            JsonElement confidenceCorrection = GetObject(personalConfidence, "correctionRate");
            return new ResponsePatternProfile
            {
                DirectPathRatioMax = GetDouble(thresholds, "directPathRatioMax", 1.2d),
                LowCorrectionCountMax = (int)GetDouble(thresholds, "lowCorrectionCountMax", 1d),
                AnswerFastRt = GetDouble(thresholds, "answerFastDecisionRtBelowSec"),
                ConfidenceFastRt = GetDouble(thresholds, "confidenceFastDecisionRtBelowSec"),
                AnswerHighSpeed = FirstFinite(
                    GetDouble(thresholds, "answerHighMaxAbsVelocityAbove"),
                    GetDouble(answerSpeed, "p90"),
                    GetDouble(answerSpeed, "upperReference")),
                ConfidenceHighSpeed = FirstFinite(
                    GetDouble(thresholds, "confidenceHighMaxAbsVelocityAbove"),
                    GetDouble(confidenceSpeed, "p90"),
                    GetDouble(confidenceSpeed, "upperReference")),
                AnswerExtraPath = GetDouble(thresholds, "answerExtraPathRatioAbove"),
                ConfidenceExtraPath = GetDouble(thresholds, "confidenceExtraPathRatioAbove"),
                AnswerHighCorrection = GetDouble(thresholds, "answerHighCorrectionRateAbove"),
                ConfidenceHighCorrection = GetDouble(thresholds, "confidenceHighCorrectionRateAbove"),
                AnswerLowCorrection = FirstFinite(
                    GetDouble(thresholds, "answerLowCorrectionRateAtOrBelow"),
                    GetDouble(answerCorrection, "p25"),
                    GetDouble(answerCorrection, "lowerReference")),
                ConfidenceLowCorrection = FirstFinite(
                    GetDouble(thresholds, "confidenceLowCorrectionRateAtOrBelow"),
                    GetDouble(confidenceCorrection, "p25"),
                    GetDouble(confidenceCorrection, "lowerReference")),
                AnswerSlowRt = GetDouble(GetObject(personalAnswer, "decisionRt"), "upperReference"),
                ConfidenceSlowRt = GetDouble(GetObject(personalConfidence, "decisionRt"), "upperReference"),
                AnswerDistanceThresholds = ReadDistanceThresholds(personalAnswer),
                ConfidenceDistanceThresholds = ReadDistanceThresholds(personalConfidence)
            };
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement GetObject(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement result)
            ? result
            : default;

    private static JsonElement GetArray(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement result) &&
        result.ValueKind == JsonValueKind.Array
            ? result
            : default;

    private static IReadOnlyList<PersonalDistanceThreshold> ReadDistanceThresholds(JsonElement stage)
    {
        var thresholds = new List<PersonalDistanceThreshold>();
        JsonElement bins = GetArray(stage, "distanceBins");
        if (bins.ValueKind != JsonValueKind.Array)
            return thresholds;

        foreach (JsonElement bin in bins.EnumerateArray())
        {
            int minimum = (int)GetDouble(bin, "minimumSlotDistance", -1d);
            int maximum = (int)GetDouble(bin, "maximumSlotDistance", -1d);
            if (minimum < 0 || maximum < minimum)
                continue;
            JsonElement speed = GetObject(bin, "maxAbsVelocity");
            JsonElement correction = GetObject(bin, "correctionRate");
            thresholds.Add(new PersonalDistanceThreshold
            {
                BinId = GetString(bin, "binId", "movement"),
                MinimumSlotDistance = minimum,
                MaximumSlotDistance = maximum,
                HighSpeed = FirstFinite(GetDouble(speed, "p90"), GetDouble(speed, "upperReference")),
                LowCorrection = FirstFinite(GetDouble(correction, "p25"), GetDouble(correction, "lowerReference"))
            });
        }
        return thresholds;
    }

    public double ResolveHighSpeedThreshold(bool answer, int slotDistance, out string distanceBin)
    {
        IReadOnlyList<PersonalDistanceThreshold> candidates = answer
            ? AnswerDistanceThresholds
            : ConfidenceDistanceThresholds;
        PersonalDistanceThreshold? match = candidates.FirstOrDefault(bin =>
            slotDistance >= bin.MinimumSlotDistance && slotDistance <= bin.MaximumSlotDistance);
        if (match != null && IsPositive(match.HighSpeed))
        {
            distanceBin = match.BinId;
            return match.HighSpeed;
        }

        distanceBin = "global";
        return answer ? AnswerHighSpeed : ConfidenceHighSpeed;
    }

    public double ResolveLowCorrectionThreshold(bool answer, int slotDistance)
    {
        IReadOnlyList<PersonalDistanceThreshold> candidates = answer
            ? AnswerDistanceThresholds
            : ConfidenceDistanceThresholds;
        PersonalDistanceThreshold? match = candidates.FirstOrDefault(bin =>
            slotDistance >= bin.MinimumSlotDistance && slotDistance <= bin.MaximumSlotDistance);
        if (match != null && IsNonNegative(match.LowCorrection))
            return match.LowCorrection;
        return answer ? AnswerLowCorrection : ConfidenceLowCorrection;
    }

    private static bool IsPositive(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
    private static bool IsNonNegative(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;

    private static double FirstFinite(params double[] candidates)
    {
        foreach (double value in candidates)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d)
                return value;
        }
        return double.NaN;
    }

    private static string GetString(JsonElement element, string property, string fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString() ?? fallback
            : fallback;

    private static double GetDouble(JsonElement element, string property, double fallback = double.NaN)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
            return fallback;
        if (value.TryGetDouble(out double number))
            return number;
        return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : fallback;
    }
}

internal sealed class PersonalDistanceThreshold
{
    public string BinId { get; init; } = "movement";
    public int MinimumSlotDistance { get; init; }
    public int MaximumSlotDistance { get; init; }
    public double HighSpeed { get; init; } = double.NaN;
    public double LowCorrection { get; init; } = double.NaN;
}

internal sealed class ContextualEvidenceService
{
    private readonly ProbeCalibrationReader _probeReader = new();

    public ContextualEvidenceResult Build(
        ResearchSession session,
        DataSnapshot snapshot,
        ProbeRuleCardSet plugin)
    {
        if (!snapshot.ResponseCalibration.Ready)
            return Failure("A ready PAXSM response-calibration profile is required before assigning the X-axis pattern.");
        if (plugin.Dimensions.Count == 0)
            return Failure("Select a Probe Plugin containing at least one calibrated dimension before building the Y axis.");

        ProbeCalibrationSnapshot? calibration = _probeReader.LoadLatest(snapshot, session);
        if (calibration == null || !calibration.HasBaseline)
            return Failure("No complete Baseline + Probe calibration run was found for this participant.");

        RunManifestRecord? combinedRun = snapshot.Runs
            .Where(run => string.Equals(run.SceneName, SceneDefinitions.Combined.SceneName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.ConfiguredAtUtc)
            .FirstOrDefault();
        if (combinedRun == null || !Directory.Exists(combinedRun.RunDirectory))
            return Failure("No Combined target-study run was found for this participant/session.");

        string? questionnairePath = FindPrimaryQuestionnaire(combinedRun.RunDirectory, session.ParticipantId);
        if (questionnairePath == null)
            return Failure("The Combined run has no primary CAREXR_Questionnaire export yet.");
        Dictionary<string, ProbeMetricBlock> combinedBlocks = _probeReader.ReadMetricBlocks(combinedRun.RunDirectory, session.ParticipantId);
        if (combinedBlocks.Count == 0)
            return Failure("The Combined run has no valid Probe Metrics.csv export yet.");
        ResponsePatternProfile? responseProfile = ResponsePatternProfile.Load(snapshot.ResponseCalibration.ProfilePath);
        if (responseProfile == null)
            return Failure("The personal response-calibration profile could not be read.");

        Dictionary<string, double> baselineScores = ReadBaselineScores(calibration.RunDirectory, session.ParticipantId);
        CsvTable questionnaire = CsvTable.Read(questionnairePath);
        var records = new List<ContextualResponseRecord>();
        foreach (CsvRecord row in questionnaire.Records)
        {
            string itemDimension = row.Get("itemDimension");
            ProbeDimensionDefinition? definition = ProbeDimensionCatalog.FromItemDimension(itemDimension);
            if (definition == null)
                continue;
            ProbeDimensionRuleCard? rule = plugin.Find(definition.Id);
            if (rule == null || rule.Features.Count == 0 ||
                !combinedBlocks.TryGetValue(row.Get("blockId"), out ProbeMetricBlock? combinedBlock) ||
                !calibration.Blocks.TryGetValue("baseline", out ProbeMetricBlock? baselineBlock))
                continue;

            StagePatternResult answer = ClassifyStage(row, "answer", responseProfile);
            StagePatternResult confidence = ClassifyStage(row, "confidence", responseProfile);
            ProbeMatchResult probe = MatchProbe(rule, baselineBlock, combinedBlock);
            string itemId = row.Get("itemId");
            int score = row.GetInt("selectedScore");
            baselineScores.TryGetValue(itemId, out double baselineScore);
            bool hasBaselineScore = baselineScores.ContainsKey(itemId);
            records.Add(new ContextualResponseRecord
            {
                ParticipantId = session.ParticipantId,
                SessionNumber = session.SessionNumber,
                BlockId = row.Get("blockId"),
                PresentationOrder = row.GetInt("presentationOrder"),
                ItemId = itemId,
                ItemDimension = itemDimension,
                SelectedScore = score,
                Confidence = row.GetInt("confidence"),
                BaselineScore = hasBaselineScore ? baselineScore : null,
                XPattern = answer.Category,
                XEvidence = answer.Evidence,
                ConfidencePattern = confidence.Category,
                YPattern = probe.Category,
                ProbeMatchRatio = probe.Ratio,
                YEvidence = probe.Evidence,
                ScoreContext = DescribeScoreContext(score, hasBaselineScore ? baselineScore : null, probe.Category),
                PluginName = plugin.PluginName,
                SourceQuestionnairePath = questionnairePath,
                SourceCombinedMetricsPath = combinedBlock.FilePath,
                SourceBaselineMetricsPath = baselineBlock.FilePath,
                SourceResponseProfilePath = snapshot.ResponseCalibration.ProfilePath
            });
        }

        if (records.Count == 0)
            return Failure("No Combined questionnaire items matched the selected Plugin dimensions. Check the Plugin dimension IDs and task exports.");

        string outputDirectory = Path.Combine(combinedRun.RunDirectory, "EvidenceReview");
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory,
            $"CAREXR_ContextualResponseRecord_{session.ParticipantId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(outputPath, BuildCsv(records));
        return new ContextualEvidenceResult
        {
            Success = true,
            Message = $"Built {records.Count} contextual item records using Plugin '{plugin.PluginName}'.",
            OutputPath = outputPath,
            Records = records
        };
    }

    private static ContextualEvidenceResult Failure(string message) => new() { Success = false, Message = message };

    private static string? FindPrimaryQuestionnaire(string directory, string participantId)
    {
        if (!Directory.Exists(directory))
            return null;
        return Directory.EnumerateFiles(directory, $"CAREXR_Questionnaire_{participantId}_*.csv", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => !file.Name.Contains("_StageEvents_", StringComparison.OrdinalIgnoreCase) &&
                           !file.Name.Contains("_RawTrace_", StringComparison.OrdinalIgnoreCase) &&
                           !file.Name.Contains("_InteractionEvents_", StringComparison.OrdinalIgnoreCase) &&
                           !file.Name.Contains("_Metadata_", StringComparison.OrdinalIgnoreCase) &&
                           !file.Name.Contains("_PhysicalSpeedSamples_", StringComparison.OrdinalIgnoreCase) &&
                           !file.Name.Contains("_SlotSpeedEvents_", StringComparison.OrdinalIgnoreCase) &&
                           !file.Name.Contains("_SpeedSummary_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.Name.Contains("_completed", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static Dictionary<string, double> ReadBaselineScores(string calibrationDirectory, string participantId)
    {
        string? questionnairePath = FindPrimaryQuestionnaire(calibrationDirectory, participantId);
        if (questionnairePath == null)
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            return CsvTable.Read(questionnairePath).Records
                .Where(row => row.Get("blockId").Equals("baseline", StringComparison.OrdinalIgnoreCase))
                .Where(row => !string.IsNullOrWhiteSpace(row.Get("itemId")) && !double.IsNaN(row.GetDouble("selectedScore")))
                .GroupBy(row => row.Get("itemId"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().GetDouble("selectedScore"), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static StagePatternResult ClassifyStage(CsvRecord row, string stage, ResponsePatternProfile profile)
    {
        bool answer = stage.Equals("answer", StringComparison.OrdinalIgnoreCase);
        string prefix = answer ? "answer" : "confidence";
        double decisionRt = row.GetDouble(prefix + "DecisionRt");
        double maxSpeed = row.GetDouble(prefix + "MaxAbsVel");
        double pathRatio = row.GetDouble(prefix + "PathRatio");
        int reverses = row.GetInt(prefix + "ReverseCount");
        int microAdjustments = row.GetInt(prefix + "MicroAdjustCount");
        int slotChanges = Math.Max(1, row.GetInt(prefix + "SlotChangeCount"));
        double correctionRate = (reverses + microAdjustments) / (double)slotChanges;
        int initialSlot = row.GetInt(prefix + "InitialSlot");
        int finalSlot = answer ? row.GetInt("selectedScore") : row.GetInt("confidence");
        int slotDistance = initialSlot > 0 && finalSlot > 0
            ? Math.Abs(finalSlot - initialSlot)
            : -1;

        double fastThreshold = answer ? profile.AnswerFastRt : profile.ConfidenceFastRt;
        double speedThreshold = profile.ResolveHighSpeedThreshold(answer, slotDistance, out string speedDistanceBin);
        double lowCorrectionThreshold = profile.ResolveLowCorrectionThreshold(answer, slotDistance);
        double extraPathThreshold = answer ? profile.AnswerExtraPath : profile.ConfidenceExtraPath;
        double correctionThreshold = answer ? profile.AnswerHighCorrection : profile.ConfidenceHighCorrection;
        double slowThreshold = answer ? profile.AnswerSlowRt : profile.ConfidenceSlowRt;

        bool fast = IsPositiveThreshold(fastThreshold) && decisionRt >= 0d && decisionRt <= fastThreshold;
        bool highSpeed = IsPositiveThreshold(speedThreshold) && maxSpeed >= speedThreshold;
        bool directPath = pathRatio >= 0.9d && pathRatio <= profile.DirectPathRatioMax;
        bool lowCorrection = IsNonNegativeThreshold(lowCorrectionThreshold)
            ? correctionRate <= lowCorrectionThreshold + 0.000001d
            : reverses <= profile.LowCorrectionCountMax && microAdjustments <= profile.LowCorrectionCountMax;
        bool extraPath = IsPositiveThreshold(extraPathThreshold) && pathRatio > extraPathThreshold;
        bool highCorrection = IsPositiveThreshold(correctionThreshold) && correctionRate > correctionThreshold;
        bool slow = IsPositiveThreshold(slowThreshold) && decisionRt > slowThreshold;

        var evidence = new List<string>();
        if (fast) evidence.Add($"{stage} decision RT is below the participant's personal lower reference ({decisionRt:0.###} s).");
        if (highSpeed) evidence.Add($"{stage} peak knob speed exceeds the participant's {speedDistanceBin} movement personal p90/reference threshold ({maxSpeed:0.###}).");
        if (directPath) evidence.Add($"{stage} path is direct (path ratio {pathRatio:0.###}).");
        if (lowCorrection) evidence.Add($"{stage} correction is at or below the participant's personal lower reference (reverse={reverses}, micro-adjust={microAdjustments}).");
        if (extraPath) evidence.Add($"{stage} path is longer than the personal reference ({pathRatio:0.###}).");
        if (highCorrection) evidence.Add($"{stage} correction rate is above the personal reference ({correctionRate:0.###}).");
        if (slow) evidence.Add($"{stage} decision RT is slower than the participant's personal reference range ({decisionRt:0.###} s).");

        if (directPath && lowCorrection && highSpeed)
            return new StagePatternResult { Category = "accelerated_direct", Evidence = string.Join(" ", evidence) };
        if (new[] { extraPath, highCorrection, slow }.Count(value => value) >= 2)
            return new StagePatternResult { Category = "hesitant_corrective", Evidence = string.Join(" ", evidence) };
        return new StagePatternResult
        {
            Category = "reviewable",
            Evidence = evidence.Count == 0
                ? $"No dominant {stage.ToLowerInvariant()}-stage pattern exceeded the participant's personal knob-reference thresholds."
                : string.Join(" ", evidence)
        };
    }

    private static ProbeMatchResult MatchProbe(
        ProbeDimensionRuleCard rule,
        ProbeMetricBlock baseline,
        ProbeMetricBlock combined)
    {
        int matched = 0;
        int available = 0;
        var evidence = new List<string>();
        foreach (ProbeFeatureRule feature in rule.Features)
        {
            if (!baseline.Metrics.TryGetValue(feature.MetricId, out ProbeMetricValue? baselineMetric) ||
                !combined.Metrics.TryGetValue(feature.MetricId, out ProbeMetricValue? combinedMetric) ||
                !baselineMetric.Valid || !combinedMetric.Valid)
            {
                evidence.Add($"{feature.MetricName}: unavailable in the participant's Baseline or Combined export.");
                continue;
            }

            available++;
            double delta = combinedMetric.Value - baselineMetric.Value;
            double tolerance = Math.Max(Math.Abs(feature.CalibrationDelta) * 0.10d,
                Math.Max(Math.Abs(baselineMetric.Value) * 0.03d, 0.001d));
            bool directionMatch = feature.ExpectedDirection == "lower" ? delta < -tolerance : delta > tolerance;
            if (directionMatch)
                matched++;
            string expected = feature.ExpectedDirection == "lower" ? "lower" : "higher";
            string observed = Math.Abs(delta) < tolerance ? "near Baseline" : delta > 0d ? "higher" : "lower";
            evidence.Add($"{feature.MetricName}: {combinedMetric.Value:0.###} vs Baseline {baselineMetric.Value:0.###} ({observed}; expected {expected}).");
        }

        if (available == 0)
            return new ProbeMatchResult { Category = "none", Ratio = 0d, Evidence = string.Join(" ", evidence) };
        double ratio = matched / (double)available;
        string category = matched >= 2 && ratio >= 0.67d
            ? "strong"
            : matched >= 1
                ? "partial"
                : "none";
        return new ProbeMatchResult { Category = category, Ratio = ratio, Evidence = string.Join(" ", evidence) };
    }

    private static string DescribeScoreContext(int score, double? baselineScore, string probeCategory)
    {
        if (!baselineScore.HasValue)
            return "Baseline questionnaire score was not available; score-to-probe relation was not assessed.";
        double delta = score - baselineScore.Value;
        string scoreDirection = delta > 1d ? "higher" : delta < -1d ? "lower" : "similar";
        if (probeCategory is "strong" or "partial")
            return $"Score is {scoreDirection} than the participant's Baseline ({baselineScore.Value:0.###}); this is shown after, not used to calculate, the Probe match.";
        return $"Score is {scoreDirection} than the participant's Baseline ({baselineScore.Value:0.###}); Probe evidence was insufficient or did not match.";
    }

    private static bool IsPositiveThreshold(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
    private static bool IsNonNegativeThreshold(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;

    private static string BuildCsv(IEnumerable<ContextualResponseRecord> records)
    {
        string[] header =
        {
            "participantId", "sessionNumber", "blockId", "presentationOrder", "itemId", "itemDimension",
            "selectedScore", "confidence", "baselineScore", "xPattern", "xEvidence", "confidencePattern",
            "yPattern", "probeMatchRatio", "yEvidence", "scoreContext", "pluginName",
            "sourceQuestionnairePath", "sourceCombinedMetricsPath", "sourceBaselineMetricsPath", "sourceResponseProfilePath"
        };
        var builder = new StringBuilder(string.Join(",", header));
        foreach (ContextualResponseRecord record in records)
        {
            builder.AppendLine();
            builder.Append(string.Join(",", new[]
            {
                CsvTable.Escape(record.ParticipantId),
                record.SessionNumber.ToString(CultureInfo.InvariantCulture),
                CsvTable.Escape(record.BlockId),
                record.PresentationOrder.ToString(CultureInfo.InvariantCulture),
                CsvTable.Escape(record.ItemId),
                CsvTable.Escape(record.ItemDimension),
                record.SelectedScore.ToString(CultureInfo.InvariantCulture),
                record.Confidence.ToString(CultureInfo.InvariantCulture),
                record.BaselineScore?.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
                CsvTable.Escape(record.XPattern),
                CsvTable.Escape(record.XEvidence),
                CsvTable.Escape(record.ConfidencePattern),
                CsvTable.Escape(record.YPattern),
                record.ProbeMatchRatio.ToString("0.###", CultureInfo.InvariantCulture),
                CsvTable.Escape(record.YEvidence),
                CsvTable.Escape(record.ScoreContext),
                CsvTable.Escape(record.PluginName),
                CsvTable.Escape(record.SourceQuestionnairePath),
                CsvTable.Escape(record.SourceCombinedMetricsPath),
                CsvTable.Escape(record.SourceBaselineMetricsPath),
                CsvTable.Escape(record.SourceResponseProfilePath)
            }));
        }
        return builder.ToString();
    }
}
