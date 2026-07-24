using System.Text.Json.Serialization;

namespace PAXSMResearcherConsole;

internal sealed record SceneDefinition(
    string Id,
    string DisplayName,
    string SceneName,
    string DataNamespace,
    string Description);

internal static class SceneDefinitions
{
    public static readonly SceneDefinition Comparison = new(
        "paxsm-comparison",
        "Controlled input-method comparison",
        "PAXSMComparisonScene",
        "PAXSMComparison_Data",
        "Practice, formal targets, NASA-TLX, SUS, rest, and crossover run inside one scene.");

    public static readonly SceneDefinition Workload = new(
        "workload-probe",
        "Workload probe calibration",
        "XRWorkloadProbeScene",
        "XRWorkloadProbe_Data",
        "Baseline plus Mental, Physical, and Temporal demand-probe blocks run inside one scene.");

    public static readonly SceneDefinition ResponseCalibration = new(
        "paxsm-response-calibration",
        "Personal knob reference calibration",
        "XRQuestionnaireReadScene",
        "PAXSMPersonalKnobReference_Data",
        "One distance-balanced target-entry block creates a participant-relative Answer and Confidence knob profile.");

    public static readonly SceneDefinition Combined = new(
        "combined-probe",
        "Held-out Combined experiment",
        "XRCombinedProbeScene",
        "XRCombinedProbe_Data",
        "Two identical Combined repetitions with an explicit participant confirmation gate.");

    public static readonly SceneDefinition MainQuestionnaire = new(
        "main-questionnaire",
        "Main questionnaire",
        "MainScene",
        "MainScene_Data",
        "General PAXSM questionnaire scene.");

    public static readonly IReadOnlyList<SceneDefinition> All =
        new[] { Comparison, ResponseCalibration, Workload, Combined, MainQuestionnaire };
}

internal sealed class ResearchSession
{
    public required string ParticipantId { get; init; }
    public required int SessionNumber { get; init; }
    public required string OutputRoot { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

internal sealed class ConsoleLaunchRequest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "PAXSM_ResearcherConsole_LaunchRequest_v1";

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("projectRoot")]
    public string ProjectRoot { get; set; } = "";

    [JsonPropertyName("participantId")]
    public string ParticipantId { get; set; } = "";

    [JsonPropertyName("sessionNumber")]
    public int SessionNumber { get; set; } = 1;

    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = "";

    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; } = "";

    [JsonPropertyName("dataNamespace")]
    public string DataNamespace { get; set; } = "";

    [JsonPropertyName("outputRoot")]
    public string OutputRoot { get; set; } = "";

    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; } = true;

    [JsonPropertyName("requestedAtUtc")]
    public string RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

internal sealed class ConsoleSessionManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "PAXSM_ResearcherConsole_Session_v1";

    [JsonPropertyName("participantId")]
    public string ParticipantId { get; set; } = "";

    [JsonPropertyName("sessionNumber")]
    public int SessionNumber { get; set; }

    [JsonPropertyName("outputRoot")]
    public string OutputRoot { get; set; } = "";

    [JsonPropertyName("projectRoot")]
    public string ProjectRoot { get; set; } = "";

    [JsonPropertyName("openedAtUtc")]
    public string OpenedAtUtc { get; set; } = "";

    [JsonPropertyName("lastUpdatedAtUtc")]
    public string LastUpdatedAtUtc { get; set; } = "";
}

internal sealed class RunManifestRecord
{
    public string ParticipantId { get; init; } = "";
    public int SessionNumber { get; init; }
    public string RunId { get; init; } = "";
    public string SceneId { get; init; } = "";
    public string SceneName { get; init; } = "";
    public string RunDirectory { get; init; } = "";
    public DateTimeOffset ConfiguredAtUtc { get; init; }
}

internal enum CalibrationBlockState
{
    Queued,
    QuestionnairePartial,
    Collected
}

internal sealed class CalibrationBlockSnapshot
{
    public required string BlockId { get; init; }
    public required string DisplayName { get; init; }
    public int? PresentationOrder { get; set; }
    public CalibrationBlockState State { get; set; }
    public int QuestionnaireItemCount { get; set; }
}

internal sealed class ResponseCalibrationProfileSnapshot
{
    public bool Found { get; init; }
    public bool Complete { get; init; }
    public bool Ready { get; init; }
    public string Quality { get; init; } = "unavailable";
    public string ProfilePath { get; init; } = "";
    public int PersonalTrials { get; init; }
    public int PersonalReferenceTrials { get; init; }
    public float PersonalAnswerAccuracy { get; init; }
    public float PersonalConfidenceAccuracy { get; init; }
}

internal sealed class DataSnapshot
{
    public string ParticipantDirectory { get; init; } = "";
    public int CsvFileCount { get; init; }
    public int JsonFileCount { get; init; }
    public long TotalBytes { get; init; }
    public DateTimeOffset? LatestWriteUtc { get; init; }
    public IReadOnlyList<RunManifestRecord> Runs { get; init; } = Array.Empty<RunManifestRecord>();
    public IReadOnlyList<CalibrationBlockSnapshot> CalibrationBlocks { get; init; } = Array.Empty<CalibrationBlockSnapshot>();
    public ResponseCalibrationProfileSnapshot ResponseCalibration { get; init; } = new();
    public IReadOnlyList<FileInfo> LatestFiles { get; init; } = Array.Empty<FileInfo>();
    public bool CalibrationComplete =>
        CalibrationBlocks.Count == 4 && CalibrationBlocks.All(block => block.State == CalibrationBlockState.Collected);
    public bool CalibrationBundleReady => CalibrationComplete && ResponseCalibration.Ready;
}
