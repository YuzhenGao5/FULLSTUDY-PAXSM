using System.Globalization;
using System.Text.Json;

namespace PAXSMResearcherConsole;

internal sealed class PersonalReferenceMetric
{
    public string MetricId { get; init; } = "";
    public string Units { get; init; } = "";
    public int SampleCount { get; init; }
    public double? Median { get; init; }
    public double? P25 { get; init; }
    public double? P90 { get; init; }
    public double? LowerReference { get; init; }
    public double? UpperReference { get; init; }
}

internal sealed class PersonalReferenceDistanceBin
{
    public string BinId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int MinimumSlotDistance { get; init; }
    public int MaximumSlotDistance { get; init; }
    public int ReferenceTrialCount { get; init; }
    public PersonalReferenceMetric MaxAbsVelocity { get; init; } = new();
    public PersonalReferenceMetric CorrectionRate { get; init; } = new();
}

internal sealed class PersonalReferenceStage
{
    public string Name { get; init; } = "";
    public int ReferenceTrialCount { get; init; }
    public int ValidSlotSpeedEventCount { get; init; }
    public int ValidPhysicalSpeedSampleCount { get; init; }
    public IReadOnlyDictionary<string, PersonalReferenceMetric> Metrics { get; init; } =
        new Dictionary<string, PersonalReferenceMetric>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<PersonalReferenceDistanceBin> DistanceBins { get; init; } =
        Array.Empty<PersonalReferenceDistanceBin>();
}

internal sealed class PersonalReferenceProfileDetails
{
    public bool Found { get; init; }
    public bool BoundToActiveSession { get; init; }
    public string Message { get; init; } = "";
    public string ProfilePath { get; init; } = "";
    public string ParticipantId { get; init; } = "";
    public int SessionNumber { get; init; }
    public string SourceScene { get; init; } = "";
    public string RunId { get; init; } = "";
    public string GeneratedUtc { get; init; } = "";
    public bool CalibrationComplete { get; init; }
    public bool ProfileReady { get; init; }
    public string ProfileQuality { get; init; } = "unavailable";
    public int ExpectedTrials { get; init; }
    public int MinimumReferenceTrials { get; init; }
    public float MinimumTargetAccuracy { get; init; }
    public int CompletedTrials { get; init; }
    public int ReferenceTrials { get; init; }
    public float AnswerTargetAccuracy { get; init; }
    public float ConfidenceTargetAccuracy { get; init; }
    public double DirectPathRatioMax { get; init; } = 1.2d;
    public PersonalReferenceStage Answer { get; init; } = new() { Name = "Answer" };
    public PersonalReferenceStage Confidence { get; init; } = new() { Name = "Confidence" };
}

internal sealed class PersonalReferenceProfileReader
{
    private static readonly string[] CoreMetricIds =
    {
        "decisionRt",
        "maxAbsVelocity",
        "pathRatio",
        "pauseRate",
        "reverseCount",
        "microAdjustCount",
        "correctionRate"
    };

    public PersonalReferenceProfileDetails Read(
        ResponseCalibrationProfileSnapshot snapshot,
        ResearchSession? session)
    {
        if (session == null)
            return Unavailable("Start a participant session to view a personal reference profile.");
        if (!snapshot.Found || string.IsNullOrWhiteSpace(snapshot.ProfilePath))
            return Unavailable("No completed personal knob reference profile was found for this participant and session.");
        if (!File.Exists(snapshot.ProfilePath))
            return Unavailable("The linked personal knob reference profile file is no longer available.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(snapshot.ProfilePath));
            JsonElement root = document.RootElement;
            string participantId = GetString(root, "participantId");
            int sessionNumber = GetInt(root, "sessionNumber");
            bool bound = participantId.Equals(session.ParticipantId, StringComparison.OrdinalIgnoreCase) &&
                         (sessionNumber <= 0 || sessionNumber == session.SessionNumber);
            if (!bound)
            {
                return new PersonalReferenceProfileDetails
                {
                    Found = true,
                    ProfilePath = snapshot.ProfilePath,
                    ParticipantId = participantId,
                    SessionNumber = sessionNumber,
                    BoundToActiveSession = false,
                    Message = "The profile file does not match the active participant/session and will not be used."
                };
            }

            JsonElement reference = GetObject(root, "personalReference");
            if (reference.ValueKind != JsonValueKind.Object)
                reference = GetObject(root, "carefulReference");
            JsonElement thresholds = GetObject(root, "responsePatternThresholds");

            return new PersonalReferenceProfileDetails
            {
                Found = true,
                BoundToActiveSession = true,
                Message = GetBool(root, "profileReady")
                    ? "Personal reference profile is ready for researcher-facing response-process review."
                    : "Personal reference profile was found but did not meet the calibration readiness checks.",
                ProfilePath = snapshot.ProfilePath,
                ParticipantId = participantId,
                SessionNumber = sessionNumber,
                SourceScene = GetString(root, "sourceScene"),
                RunId = GetString(root, "runId"),
                GeneratedUtc = GetString(root, "generatedUtc"),
                CalibrationComplete = GetBool(root, "calibrationComplete"),
                ProfileReady = GetBool(root, "profileReady"),
                ProfileQuality = GetString(root, "profileQuality", "unavailable"),
                ExpectedTrials = GetInt(root, "expectedTrials"),
                MinimumReferenceTrials = GetInt(root, "minimumReferenceTrials"),
                MinimumTargetAccuracy = GetFloat(root, "minimumTargetAccuracy"),
                CompletedTrials = GetInt(reference, "completedTrials"),
                ReferenceTrials = GetInt(reference, "referenceTrialCount"),
                AnswerTargetAccuracy = GetFloat(reference, "answerTargetAccuracy"),
                ConfidenceTargetAccuracy = GetFloat(reference, "confidenceTargetAccuracy"),
                DirectPathRatioMax = GetDouble(thresholds, "directPathRatioMax", 1.2d),
                Answer = ReadStage(reference, "answer", "Answer"),
                Confidence = ReadStage(reference, "confidence", "Confidence")
            };
        }
        catch (Exception exception)
        {
            return Unavailable($"The personal profile could not be read: {exception.Message}");
        }
    }

    public static IReadOnlyList<string> CoreMetrics => CoreMetricIds;

    private static PersonalReferenceProfileDetails Unavailable(string message) => new()
    {
        Found = false,
        BoundToActiveSession = false,
        Message = message
    };

    private static PersonalReferenceStage ReadStage(JsonElement reference, string property, string name)
    {
        JsonElement stage = GetObject(reference, property);
        var metrics = new Dictionary<string, PersonalReferenceMetric>(StringComparer.OrdinalIgnoreCase);
        foreach (string metricId in CoreMetricIds)
            metrics[metricId] = ReadMetric(stage, metricId);

        var bins = new List<PersonalReferenceDistanceBin>();
        JsonElement distanceBins = GetArray(stage, "distanceBins");
        if (distanceBins.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement bin in distanceBins.EnumerateArray())
            {
                bins.Add(new PersonalReferenceDistanceBin
                {
                    BinId = GetString(bin, "binId"),
                    DisplayName = GetString(bin, "displayName"),
                    MinimumSlotDistance = GetInt(bin, "minimumSlotDistance"),
                    MaximumSlotDistance = GetInt(bin, "maximumSlotDistance"),
                    ReferenceTrialCount = GetInt(bin, "referenceTrialCount"),
                    MaxAbsVelocity = ReadMetric(bin, "maxAbsVelocity"),
                    CorrectionRate = ReadMetric(bin, "correctionRate")
                });
            }
        }

        return new PersonalReferenceStage
        {
            Name = name,
            ReferenceTrialCount = GetInt(stage, "referenceTrialCount"),
            ValidSlotSpeedEventCount = GetInt(stage, "validSlotSpeedEventCount"),
            ValidPhysicalSpeedSampleCount = GetInt(stage, "validPhysicalSpeedSampleCount"),
            Metrics = metrics,
            DistanceBins = bins
        };
    }

    private static PersonalReferenceMetric ReadMetric(JsonElement owner, string metricId)
    {
        JsonElement metric = GetObject(owner, metricId);
        return new PersonalReferenceMetric
        {
            MetricId = GetString(metric, "metric", metricId),
            Units = GetString(metric, "units"),
            SampleCount = GetInt(metric, "sampleCount"),
            Median = GetNullableDouble(metric, "median"),
            P25 = GetNullableDouble(metric, "p25"),
            P90 = GetNullableDouble(metric, "p90"),
            LowerReference = GetNullableDouble(metric, "lowerReference"),
            UpperReference = GetNullableDouble(metric, "upperReference")
        };
    }

    private static JsonElement GetObject(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value)
            ? value
            : default;

    private static JsonElement GetArray(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Array
            ? value
            : default;

    private static string GetString(JsonElement element, string property, string fallback = "") =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString() ?? fallback
            : fallback;

    private static int GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) &&
        value.TryGetInt32(out int number)
            ? number
            : 0;

    private static float GetFloat(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) &&
        value.TryGetSingle(out float number)
            ? number
            : 0f;

    private static bool GetBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static double GetDouble(JsonElement element, string property, double fallback) =>
        GetNullableDouble(element, property) ?? fallback;

    private static double? GetNullableDouble(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
            return null;
        if (!value.TryGetDouble(out double number) || double.IsNaN(number) || double.IsInfinity(number) || number < 0d)
            return null;
        return number;
    }
}
